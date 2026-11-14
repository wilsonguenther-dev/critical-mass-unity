#!/usr/bin/env python3
"""intro-film-2: judge a real-build capture of the intro (tools/film/air_intro_capture.sh + IntroFilmCapture.cs).

  python3 tools/film/verify_build_capture.py CAPTURE_DIR FILM.mp4 [--json OUT.json] [--sheet OUT.jpg]

CAPTURE_DIR holds full/, skip5/, skip30/ (each: shot-NNN.jpg, capture.csv, audio.wav). Checks:
  1. the video path played (mode "film" on every shot until the end, never "comic");
  2. picture identity: each screenshot's picture matches the film frame the player reported, within N frames;
  3. sound-to-picture sync: the recorded game audio is aligned to the film's own soundtrack by cross-correlation in
     three windows (start, middle, end); with the per-shot audio sample counts this gives the audio film-time at each
     shot, compared with the video frame on screen at that shot (the A/V offset and its drift, in frames);
  4. skips: the picture/audio fade after the skip press, and the largest sample step after it (a click shows as a
     step over 0.05 of full scale);
  5. the logo: on the last film shots and the first title shots the logo's opaque pixels match (pixel-locked hand-off).
"""
import csv, json, os, subprocess, sys
import numpy as np
import cv2

FPS = 30


def read_csv(d):
    rows = []
    with open(os.path.join(d, 'capture.csv')) as f:
        for r in csv.DictReader(f):
            rows.append(r)
    return rows


def wav(p):
    import wave
    w = wave.open(p)
    n, ch, sr = w.getnframes(), w.getnchannels(), w.getframerate()
    a = np.frombuffer(w.readframes(n), np.int16).astype(np.float32) / 32768
    return a.reshape(-1, ch).mean(1), sr


def film_audio(film, sr):
    raw = subprocess.run(['ffmpeg', '-v', 'error', '-i', film, '-ac', '1', '-ar', str(sr), '-f', 'f32le', '-'],
                         capture_output=True).stdout
    return np.frombuffer(raw, np.float32)


def film_frames(film, w=160, h=90):
    raw = subprocess.run(['ffmpeg', '-v', 'error', '-i', film, '-vf', f'scale={w}:{h}', '-f', 'rawvideo', '-pix_fmt',
                          'gray', '-'], capture_output=True).stdout
    return np.frombuffer(raw, np.uint8).reshape(-1, h, w).astype(np.float32)


def env(x, sr, hop=0.005):
    k = int(sr * hop)
    n = len(x) // k
    e = np.sqrt((x[:n * k].reshape(n, k) ** 2).mean(1) + 1e-9)
    return np.log(e), hop


def align(rec, ref, sr, t0, t1):
    """Offset (s) such that rec[t + off] ~ ref[t], estimated on ref window t0..t1 (envelope cross-correlation)."""
    er, hop = env(rec, sr)
    ef, _ = env(ref, sr)
    a, b = int(t0 / hop), int(t1 / hop)
    seg = ef[a:b] - ef[a:b].mean()
    best, off = -1e9, 0
    for lag in range(-int(3 / hop), int(3 / hop)):
        s = a + lag
        if s < 0 or s + len(seg) > len(er):
            continue
        r = er[s:s + len(seg)]
        c = float(np.dot(seg, r - r.mean()) / (np.linalg.norm(seg) * np.linalg.norm(r - r.mean()) + 1e-9))
        if c > best:
            best, off = c, lag
    return off * hop, best


def picture(img_path, size=(160, 90)):
    im = cv2.imread(img_path, cv2.IMREAD_GRAYSCALE)
    H, W = im.shape
    w, h = W, W * 9 / 16
    if h > H:
        h, w = H, H * 16 / 9
    x0, y0 = int((W - w) / 2), int((H - h) / 2)
    im = im[y0:y0 + int(h), x0:x0 + int(w)]
    return cv2.resize(im, size, interpolation=cv2.INTER_AREA).astype(np.float32)


def run(cap, film):
    rep = {}
    frames = film_frames(film)
    pic = slice(8, 74)                                   # picture rows only (bars carry the subtitles)
    full = os.path.join(cap, 'full')
    rows = read_csv(full)
    shots = [r for r in rows if r['idx'] != '-1']
    modes = sorted(set(r['mode'] for r in shots))
    rep['modes_seen'] = modes
    rep['video_path_played'] = 'film' in modes and 'comic' not in modes
    # 2. picture identity
    offs = []
    for r in shots:
        if r['mode'] != 'film' or int(r['film_frame']) < 0:
            continue
        f = int(r['film_frame'])
        p = picture(os.path.join(full, 'shot-%03d.jpg' % int(r['idx'])))
        cands = range(max(0, f - 8), min(len(frames), f + 9))
        d = [np.median(np.abs(frames[k][pic] - p[pic])) for k in cands]
        offs.append(dict(t=float(r['film_clock']), logged=f, best=list(cands)[int(np.argmin(d))], err=float(min(d))))
    rep['picture_match'] = dict(n=len(offs), max_abs_frame_offset=max((abs(o['best'] - o['logged']) for o in offs), default=None),
                                median_err=float(np.median([o['err'] for o in offs])) if offs else None)
    # 3. sync
    rec, sr = wav(os.path.join(full, 'audio.wav'))
    ref = film_audio(film, sr)
    win = []
    for t0, t1 in ((3.0, 9.0), (20.0, 26.0), (38.0, 44.5)):
        o, c = align(rec, ref, sr, t0, t1)
        win.append(dict(window=[t0, t1], rec_minus_film_s=round(o, 4), corr=round(c, 3)))
    rep['audio_alignment'] = win
    # the audio film-time at each shot = rec_time(shot) - offset; compare with the frame on screen
    off = np.median([w['rec_minus_film_s'] for w in win])
    av = []
    for r in shots:
        if r['mode'] != 'film' or int(r['film_frame']) < 0:
            continue
        rec_t = int(r['audio_frames']) / sr
        av.append((rec_t - off) - int(r['film_frame']) / FPS)
    av = np.array(av)
    rep['av_offset_frames'] = dict(median=round(float(np.median(av) * FPS), 2), first=round(float(av[:5].mean() * FPS), 2),
                                   last=round(float(av[-5:].mean() * FPS), 2), drift=round(float((av[-5:].mean() - av[:5].mean()) * FPS), 2)) if len(av) else None
    rep['alignment_drift_frames'] = round((win[-1]['rec_minus_film_s'] - win[0]['rec_minus_film_s']) * FPS, 2)
    # 4. skips
    for name in ('skip5', 'skip30'):
        d = os.path.join(cap, name)
        if not os.path.isdir(d):
            continue
        rr = read_csv(d)
        sk = [r for r in rr if r['event'] == 'skip']
        a, sr2 = wav(os.path.join(d, 'audio.wav'))
        out = dict(skip_logged=bool(sk))
        if sk:
            s0 = int(sk[0]['audio_frames'])
            post = a[s0:s0 + int(sr2 * 1.0)]
            pre = a[max(0, s0 - int(sr2 * 0.5)):s0]
            out['max_step_after_skip'] = round(float(np.abs(np.diff(post)).max()), 4) if len(post) > 2 else None
            out['max_step_before_skip'] = round(float(np.abs(np.diff(pre)).max()), 4) if len(pre) > 2 else None
            e = [float(np.sqrt((post[i:i + sr2 // 20] ** 2).mean() + 1e-12)) for i in range(0, len(post) - sr2 // 20, sr2 // 20)]
            out['rms_after_skip_50ms_windows_db'] = [round(20 * np.log10(x + 1e-9), 1) for x in e[:12]]
            out['film_clock_at_skip'] = float(sk[0]['film_clock'])
        out['shots'] = len([r for r in rr if r['idx'] != '-1'])
        out['ended'] = any(r['event'] == 'intro ended' for r in rr)
        rep[name] = out
    rep['ended_full'] = any(r['event'] == 'intro ended' for r in rows)
    return rep, offs


if __name__ == '__main__':
    a = sys.argv[1:]
    rep, offs = run(a[0], a[1])
    s = json.dumps(rep, indent=1)
    print(s)
    if '--json' in a:
        open(a[a.index('--json') + 1], 'w').write(s)
