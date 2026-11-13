#!/usr/bin/env python3
"""Story intro 3: measure the soundtrack against the plan's acceptance items A1-A10 (never plays anything).

  python3 tools/film/check_soundtrack.py MIX_DIR [--json out.json]

MIX_DIR holds intro-mix.wav, stem-{dx,mx,fx,bg}.wav, cues/ and cues.json from build_soundtrack.py.
Exit 0 when every item passes.
"""
import json, os, re, subprocess, sys
import numpy as np
import scipy.signal as ss

SR = 48000
CUTS = [3.75, 9.75, 19.50, 23.25, 28.50, 32.25, 40.50]   # hard cuts of the film (intro-film.json)
HERO = {'crack': 4.50, 'clash': 22.50, 'roar': 34.50, 'logo': 43.50}
BANNED = re.compile(r'(bong_|confirmation_|click|CMUi/|impactPlate|impactPunch|impactSoft_heavy|SoundSynth feed)', re.I)


def load(p):
    raw = subprocess.run(['ffmpeg', '-v', 'error', '-i', p, '-ar', str(SR), '-ac', '2', '-f', 'f32le', '-'],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.float32).reshape(-1, 2).astype(np.float64)


def _num(v):
    try:
        f = float(v)
        return f if np.isfinite(f) else -120.0
    except ValueError:
        return -120.0


def ebur(p, pre=''):
    af = (pre + ',' if pre else '') + 'ebur128=peak=true:framelog=verbose'
    err = subprocess.run(['ffmpeg', '-hide_banner', '-nostats', '-v', 'verbose', '-i', p, '-af', af, '-f', 'null', '-'],
                         capture_output=True, text=True).stderr
    t, M = [], []
    for ln in err.splitlines():
        mm = re.search(r't:\s*([\d.]+).*?M:\s*(-?[\w.]+)', ln)
        if mm:
            t.append(float(mm.group(1)))
            v = mm.group(2)
            M.append(_num(v))
    summ = err[err.rfind('Summary:'):]
    I = float(re.search(r'I:\s*(-?[\d.]+)', summ).group(1))
    LRA = float(re.search(r'LRA:\s*(-?[\d.]+)', summ).group(1))
    tp = re.search(r'True peak:\s*Peak:\s*(-?[\d.inf]+)', summ, re.S)
    TP = float(tp.group(1)) if tp and 'inf' not in tp.group(1) else -120.0
    # ebur128 logs the momentary value at the END of its 400 ms window: shift to the window centre
    return dict(I=I, LRA=LRA, TP=TP, t=np.array(t) - 0.2, M=np.array(M))


def rms_db(x):
    return 20 * np.log10(np.sqrt((x ** 2).mean()) + 1e-12)


def win(x, t0, t1):
    return x[max(0, int(t0 * SR)):max(0, int(t1 * SR))]


def main(d):
    res = {}
    mix = load(f'{d}/intro-mix.wav')
    stems = {k: load(f'{d}/stem-{k}.wav') for k in ('dx', 'mx', 'fx', 'bg')}
    meta = json.load(open(f'{d}/cues.json'))
    cues = meta['cues']
    E = ebur(f'{d}/intro-mix.wav')
    over = int((np.abs(mix) > 1.0).sum())
    res['A1'] = dict(ok=abs(E['I'] + 16) <= 1 and E['TP'] <= -1.0 and over == 0 and 8 <= E['LRA'] <= 12,
                     I=E['I'], TP=E['TP'], LRA=E['LRA'], samples_over=over)
    fxc = sorted(c['start'] for c in cues if c['bus'] == 'FX')
    w1 = max(sum(1 for s in fxc if t <= s < t + 1.0) for t in np.arange(0, 48, 0.05))
    w02 = max(sum(1 for s in fxc if t <= s < t + 0.2) for t in np.arange(0, 48, 0.01))
    import librosa
    y = stems['fx'].mean(1).astype(np.float32)
    on = librosa.onset.onset_detect(y=librosa.resample(y, orig_sr=SR, target_sr=22050), sr=22050, units='time',
                                    delta=0.2)
    per_s = np.bincount(np.floor(on).astype(int)).max() if len(on) else 0
    res['A2'] = dict(ok=w1 <= 3 and w02 <= 2 and per_s <= 4, max_fx_starts_1s=w1, max_fx_starts_200ms=w02,
                     max_flux_onsets_per_s=int(per_s))
    bad3 = []
    renders = {}
    for c in cues:
        x = load(f"{d}/cues/{c['id']}.wav")
        renders[c['id']] = x
        first = 20 * np.log10(np.abs(x[:48]).max() + 1e-12)
        step = np.abs(np.diff(x[:96], axis=0)).max() if len(x) > 96 else 0
        if first > -40 or step > 0.05 or c['fade_in'] < 0.005 or c['fade_out'] < 0.005:
            bad3.append(dict(id=c['id'], first_ms_db=round(first, 1), step=round(float(step), 4)))
    res['A3'] = dict(ok=not bad3, failures=bad3)
    banned = [c['id'] for c in cues if any(BANNED.search(s) for s in c['sources'])]
    ids = list(renders)
    corr = []
    for i in range(len(ids)):
        for j in range(i + 1, len(ids)):
            a, b = renders[ids[i]].mean(1), renders[ids[j]].mean(1)
            n = min(len(a), len(b))
            if n < SR // 10:
                continue
            a, b = a[:n], b[:n]
            r = float(np.dot(a, b) / (np.linalg.norm(a) * np.linalg.norm(b) + 1e-12))
            if r > 0.9:
                corr.append((ids[i], ids[j], round(r, 3)))
    res['A4'] = dict(ok=not banned and not corr, banned=banned, correlated_pairs=corr)
    for k in ('mx', 'fx'):
        save_tmp = f'{d}/stem-{k}.wav'
    Imx, Ifx = ebur(f'{d}/stem-mx.wav')['I'], ebur(f'{d}/stem-fx.wav')['I']
    res['A5'] = dict(ok=abs(Imx - Ifx) <= 4, music_I=Imx, fx_I=Ifx)
    worst = []
    rest = stems['mx'] + stems['fx'] + stems['bg']
    for c in cues:
        if c['bus'] != 'DX':
            continue
        t0, t1 = c['start'], c['start'] + c['length']
        v = win(stems['dx'], t0, t1)
        e = win(rest, t0, t1)
        # RMS over the voiced 100 ms blocks only
        n = SR // 10
        blocks = [(rms_db(v[i:i + n]), rms_db(e[i:i + n])) for i in range(0, len(v) - n, n)]
        voiced = [(a, b) for a, b in blocks if a > -45]
        va = 10 * np.log10(np.mean([10 ** (a / 10) for a, _ in voiced]))
        ea = 10 * np.log10(np.mean([10 ** (b / 10) for _, b in voiced]))
        worst.append(dict(id=c['id'], voice_minus_bed_db=round(va - ea, 1)))
    pre_title = [c['id'] for c in cues if c['bus'] != 'DX' and 43.60 <= c['start'] < 44.00]
    res['A6'] = dict(ok=all(w['voice_minus_bed_db'] >= 9 for w in worst) and not pre_title, lines=worst,
                     transients_before_title=pre_title)
    sil = 20 * np.log10(np.abs(win(mix, 31.05, 31.45)).max() + 1e-12)
    drops = {}
    for k, t in HERO.items():
        before = rms_db(win(mix, t - 0.3, t - 0.02))
        after = rms_db(win(mix, t, t + 0.3))
        drops[k] = round(after - before, 1)
    res['A7'] = dict(ok=sil <= -40 and all(v >= 12 for v in drops.values()), silence_peak_db=round(sil, 1),
                     hit_minus_preceding_db=drops)
    lap = ebur(f'{d}/intro-mix.wav', 'highpass=f=150:poles=2,highpass=f=150:poles=2')
    lap_hits = {}
    for k, t in dict(HERO, hatch=31.50).items():
        m_hit = lap['M'][(lap['t'] >= t) & (lap['t'] <= t + 0.5)].max()
        m_pre = np.median(lap['M'][(lap['t'] >= t - 2.0) & (lap['t'] < t - 0.25)])
        lap_hits[k] = round(m_hit - m_pre, 1)
    res['A8'] = dict(ok=all(v >= 6 for v in lap_hits.values()) and E['I'] - lap['I'] <= 4,
                     hit_minus_2s_before_LU=lap_hits, integrated_drop_db=round(E['I'] - lap['I'], 2))
    i = int(np.argmax(E['M']))
    t_loud = float(E['t'][i])
    roar_m = E['M'][(E['t'] >= 34.4) & (E['t'] <= 35.0)].max()
    logo_m = E['M'][(E['t'] >= 43.4) & (E['t'] <= 44.2)].max()
    res['A9'] = dict(ok=abs(t_loud - 43.5) <= 0.35 and logo_m - roar_m >= 2, loudest_t=round(t_loud, 2),
                     logo_M=logo_m, roar_M=roar_m)
    bad10 = []
    for t in CUTS:
        on_cut = [c['id'] for c in cues if abs(c['start'] - t) < 1 / 60]
        prelap = [c['id'] for c in cues if 2 / 30 - 1e-3 <= t - c['start'] <= 6 / 30 + 1e-3]
        crossing = [c['id'] for c in cues if c['start'] < t - 0.01 and c['start'] + c['length'] > t + 0.05]
        if on_cut or not (prelap or crossing):
            bad10.append(dict(cut=t, on_cut=on_cut, prelap=prelap, crossing=len(crossing)))
    res['A10'] = dict(ok=not bad10, failures=bad10)
    for v in res.values():
        v['ok'] = bool(v['ok'])
    return res


if __name__ == '__main__':
    d = sys.argv[1]
    r = main(d)
    for k, v in r.items():
        print(k, 'PASS' if v['ok'] else 'FAIL', {a: b for a, b in v.items() if a != 'ok'})
    if '--json' in sys.argv:
        json.dump(r, open(sys.argv[sys.argv.index('--json') + 1], 'w'), indent=1, default=float)
    sys.exit(0 if all(v['ok'] for v in r.values()) else 1)
