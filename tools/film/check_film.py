#!/usr/bin/env python3
"""Story intro 3: measure the encoded film against the plan's picture, edit and finish items (never plays anything).

  python3 tools/film/check_film.py FILM.mp4 [--json out.json]

B13 first frame / fade-up, C17 luma jumps outside the light events, C19 luma step across hard cuts, E26 fixed #000 bars
on every frame, E28 container (2560x1440, 30 fps CFR, H.264 High, yuv420p, BT.709, AAC 48 kHz, <= 60 MB, audio and
video the same length). Reads every frame at 1/4 size from ffmpeg.
"""
import json, os, subprocess, sys
import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
import render_intro as R


def probe(p):
    out = subprocess.run(['ffprobe', '-v', 'error', '-show_streams', '-show_format', '-of', 'json', p],
                         capture_output=True, text=True, check=True).stdout
    return json.loads(out)


def frames(p, w=640, h=360):
    raw = subprocess.run(['ffmpeg', '-v', 'error', '-i', p, '-vf', f'scale={w}:{h}:flags=area', '-f', 'rawvideo',
                          '-pix_fmt', 'rgb24', '-'], capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.uint8).reshape(-1, h, w, 3)


def main(p):
    res = {}
    info = probe(p)
    v = next(s for s in info['streams'] if s['codec_type'] == 'video')
    a = next((s for s in info['streams'] if s['codec_type'] == 'audio'), None)
    size = os.path.getsize(p)
    vd = float(v.get('duration', info['format']['duration']))
    ad = float(a.get('duration', 0)) if a else 0
    res['E28'] = dict(ok=v['width'] == 2560 and v['height'] == 1440 and v['r_frame_rate'] == '30/1' and
                      v['avg_frame_rate'] == '30/1' and v['codec_name'] == 'h264' and v['profile'] == 'High' and
                      v['pix_fmt'] == 'yuv420p' and v.get('color_primaries') == 'bt709' and a is not None and
                      a['codec_name'] == 'aac' and a['sample_rate'] == '48000' and size <= 60 * 1024 * 1024 and
                      abs(vd - ad) <= 1 / 30 + 0.03,
                      size_mb=round(size / 1048576, 1), width=v['width'], height=v['height'], fps=v['avg_frame_rate'],
                      profile=v['profile'], level=v.get('level'), pix_fmt=v['pix_fmt'],
                      primaries=v.get('color_primaries'), audio=a and a['codec_name'], audio_sr=a and a['sample_rate'],
                      video_s=round(vd, 3), audio_s=round(ad, 3), nb_frames=v.get('nb_frames'))
    F = frames(p)
    H = F.shape[1]
    bar = int(round(H * 80 / 1440))
    pic = F[:, bar + 1:H - bar - 1].astype(np.float32)
    luma = (pic @ np.array([0.2126, 0.7152, 0.0722], np.float32)).mean(axis=(1, 2))
    bars = np.concatenate([F[:, :bar - 1], F[:, H - bar + 1:]], axis=1)
    bar_max = int(bars.max())
    res['E26'] = dict(ok=bar_max <= 6, bar_max_value=bar_max, frames=len(F),
                      note='bars measured on every frame at 640x360 after decode (limited range black = 16 -> 0)')
    t = np.arange(len(luma)) / R.FPS
    black = t[luma > 8]
    first_lit = float(black[0]) if len(black) else 99
    l08 = float(luma[int(0.8 * R.FPS)])
    pcm = subprocess.run(['ffmpeg', '-v', 'error', '-i', p, '-t', '2', '-ac', '1', '-ar', '48000', '-f', 'f32le', '-'],
                         capture_output=True, check=True).stdout
    au = np.frombuffer(pcm, np.float32)
    rms = np.sqrt(np.mean(au[:len(au) // 480 * 480].reshape(-1, 480) ** 2, axis=1) + 1e-12)
    loud = np.nonzero(20 * np.log10(rms) > -50)[0]
    sound_t = float(loud[0]) / 100 if len(loud) else 99
    res['B13'] = dict(ok=first_lit <= 0.40 + 1e-3 and l08 >= 30 and sound_t < first_lit, first_lit_s=round(first_lit, 3),
                      luma_at_0_8s=round(l08, 1), sound_start_s=round(sound_t, 2))
    allowed = [(4.40, 4.85), (31.38, 31.90), (17.00, 17.50)]
    jumps = []
    for i in range(3, len(luma)):
        if luma[i] - luma[i - 3:i].min() > 40 and not any(a_ <= t[i] <= b_ for a_, b_ in allowed):
            jumps.append(round(float(t[i]), 2))
    res['C17'] = dict(ok=not jumps, luma_jumps_over_40_in_3_frames=jumps, light_events=len(R.LIGHT_EVENTS))
    steps = {}
    for c in R.HARD_CUTS:
        i = int(np.ceil(c * R.FPS - 1e-6))   # the first frame of the incoming shot
        steps[c] = round(float(abs(luma[i] - luma[i - 1])), 1)
    res['C19'] = dict(ok=all(s <= 15 for s in steps.values()), luma_step_at_cut=steps)
    res['luma_curve'] = [round(float(x), 1) for x in luma[::3]]
    for k, val in res.items():
        if isinstance(val, dict) and 'ok' in val:
            val['ok'] = bool(val['ok'])
    return res


if __name__ == '__main__':
    r = main(sys.argv[1])
    for k, v in r.items():
        if isinstance(v, dict):
            print(k, 'PASS' if v['ok'] else 'FAIL', {a: b for a, b in v.items() if a != 'ok'})
    if '--json' in sys.argv:
        json.dump(r, open(sys.argv[sys.argv.index('--json') + 1], 'w'), indent=1)
    sys.exit(0 if all(v['ok'] for v in r.values() if isinstance(v, dict)) else 1)
