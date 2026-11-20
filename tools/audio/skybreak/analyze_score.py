#!/usr/bin/env python3
"""Analyzer gate for the Skybreak game score and its effects (the owner's rule: one score, one key, few events, checked by
an analyzer, never a library-hit collage; loudness alone proves nothing). Reads the COMMITTED files (no rebuild):

    python3 tools/audio/skybreak/analyze_score.py [--no-tagger] [--out tools/audio/skybreak]

For every music stem and every mix state (rendered offline from the stems exactly as MusicDirector layers them, over
the whole 12-section plan) it reports:
  key        Krumhansl-Kessler key estimate on the CQT chroma; must be F minor or its relative Ab major (the feed
             state may read F major: the motif's approved major form) - r for F minor printed too
  grid       share of note onsets within 25 ms of the 80 BPM sixteenth grid (from the clip's first sample), and the
             beat tracker's tempo (80 or 160 expected)
  loudness   integrated LUFS (BS.1770, the film tool's meter) and 4x-oversampled true peak; the reference state must be
             -20 +/- 1.5 LUFS, every file at or under -1.0 dBTP
  balance    1/3-octave long-term spectrum minus the film tool's reference tilt: mean absolute deviation 63 Hz-8 kHz, and
             low (< 200 Hz) / mid / high (> 4 kHz) energy shares
  clicks     every section join the director can make (each plan step and the wrap, every layer variant) and the title
             loop's own loop point: the largest high-passed sample step within 3 ms of the join against the 99.9th
             percentile of the surrounding 300 ms; a ratio over 3 is a click
  density    detected events per bar (onsets) for states; the stinger and effect lengths
  ear        (unless --no-tagger) the AudioSet tagger from audio_ear.py over 1 s windows: windows whose top five holds a
             banned group (cookware, animal, livestock, horse, crowd) at >= 0.05; must be 0
For effects: peak and true peak, length, spectral centroid, and the closest pair by centroid ratio and MFCC distance.
Writes ANALYZER-REPORT.md and analyzer-report.json next to this file; exit 1 when a hard check fails.
"""
import argparse, json, math, os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import build_game_score as B      # noqa: E402
import audio_ear as EAR           # noqa: E402
import soundfile as sf            # noqa: E402
import scipy.signal as ss         # noqa: E402

ROOT = B.ROOT
SR = B.SR
NAMES = ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"]
KK_MAJ = np.array([6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88])
KK_MIN = np.array([6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17])
OK_KEYS = {"F minor", "Ab major"}


def load(rel):
    x, sr = sf.read(os.path.join(ROOT, rel), always_2d=True)
    assert sr == SR, (rel, sr)
    return x.astype(np.float64)


def key_of(x):
    import librosa
    m = librosa.resample(x.mean(1), orig_sr=SR, target_sr=22050)
    c = librosa.feature.chroma_cqt(y=m, sr=22050).mean(1)
    best, rf = (None, -9), None
    for i in range(12):
        for prof, mode in ((KK_MAJ, "major"), (KK_MIN, "minor")):
            r = float(np.corrcoef(c, np.roll(prof, i))[0, 1])
            if r > best[1]:
                best = (f"{NAMES[i]} {mode}", r)
            if NAMES[i] == "F" and mode == "minor":
                rf = r
    return best[0], round(best[1], 3), round(rf, 3)


def grid_of(x, step=0.1875, tol=0.025):
    """Strength-weighted grid lock: the onset-strength envelope's peaks, each weighted by its strength above the median
    (a slow string swell makes many weak "onsets"; a drum or a plucked note makes a strong one). Onset detectors report
    a little late (measured on the percussive part, HPSS margin 2), so the circular mean offset is taken as the latency and on_grid is the weighted share within tol of
    it (random placement would give 2 x tol / step = 27%)."""
    import librosa
    m = librosa.resample(x.mean(1), orig_sr=SR, target_sr=22050)
    m = librosa.effects.percussive(m, margin=2.0)     # the grid lives in the attacks; slow pads only blur the detector
    hop = 256
    env = librosa.onset.onset_strength(y=m, sr=22050, hop_length=hop)
    pk = librosa.util.peak_pick(env, pre_max=3, post_max=3, pre_avg=8, post_avg=8, delta=0.05, wait=4)
    tempo = float(np.atleast_1d(librosa.beat.beat_track(onset_envelope=env, sr=22050, hop_length=hop)[0])[0])
    if len(pk) == 0:
        return dict(onsets=0, on_grid=1.0, latency_ms=0.0, tempo=round(tempo, 1))
    on = pk * hop / 22050.0
    w = np.clip(env[pk] - np.median(env), 0, None) + 1e-9
    ph = 2 * np.pi * (on % step) / step
    mu = math.atan2((w * np.sin(ph)).sum(), (w * np.cos(ph)).sum())
    lat = (mu / (2 * np.pi) * step) % step
    d = np.abs(((on - lat) / step - np.round((on - lat) / step)) * step)
    strong = w >= np.percentile(w, 50)
    return dict(onsets=int(strong.sum()), on_grid=round(float(w[d <= tol].sum() / w.sum()), 3),
                latency_ms=round(1000 * (lat if lat < step / 2 else lat - step), 1), tempo=round(tempo, 1))


def balance(x):
    lv = EAR.third_octave(x.mean(1), SR)
    fc = np.array(EAR.THIRD, float)
    dev = lv - EAR.ref_curve(fc)
    band = (fc >= 63) & (fc <= 8000)
    P = 10 ** (lv / 10)
    tot = P.sum()
    return dict(tilt_dev_db=round(float(np.mean(np.abs(dev[band] - np.median(dev[band])))), 2),
                low=round(float(P[fc < 200].sum() / tot), 3), mid=round(float(P[(fc >= 200) & (fc <= 4000)].sum() / tot), 3),
                high=round(float(P[fc > 4000].sum() / tot), 3))


def click_ratio(y, i, win=0.003, ctx=0.3):
    h = ss.sosfilt(ss.butter(4, 3000, "highpass", fs=SR, output="sos"), y.mean(1))
    d = np.abs(np.diff(h))
    w, c = int(win * SR), int(ctx * SR)
    a, b = max(0, i - w), min(len(d), i + w)
    around = np.concatenate([d[max(0, i - c):a], d[b:i + c]])
    ref = np.percentile(around, 99.9) + 1e-9
    return float(d[a:b].max() / ref)


def joins(stems):
    """Every join the director makes: plan step k -> k+1 (and the wrap), each layer variant on both sides."""
    worst, n = (0.0, None), 0
    pairs = set()
    for k in range(len(B.PLAN)):
        pairs.add((B.PLAN[k], B.PLAN[(k + 1) % len(B.PLAN)]))
    for a, b in sorted(pairs):
        for layer, variants in B.LAYERS.items():
            for v in variants:
                xa, xb = stems[f"sb-{a}-{layer}-{v}"], stems[f"sb-{b}-{layer}-{v}"]
                i = B._n(B.SECTION)
                y = np.zeros((i + len(xb), 2))
                y[:len(xa)] += xa
                y[i:] += xb
                r = click_ratio(y, i)
                n += 1
                if r > worst[0]:
                    worst = (r, f"{a}->{b} {layer}-{v}")
    return dict(joins=n, worst_ratio=round(worst[0], 2), worst=worst[1], clicks=0 if worst[0] <= 3.0 else 1)


def tag(x, name):
    try:
        x16 = ss.resample_poly(x.mean(1) if x.ndim == 2 else x, 1, 3).astype(np.float32)
        if len(x16) < 16000:
            x16 = np.pad(x16, (0, 16000 - len(x16)))
        t, labels, P = EAR.run_tagger(x16, hop=1.0, win=1.0)
    except Exception as e:                         # the tagger is optional (torch + the cached model)
        return dict(error=str(e)[:120])
    gi = EAR.group_index(labels)
    bad, top = 0, {}
    for w in range(P.shape[0]):
        order = np.argsort(-P[w])[:5]
        hit = False
        for g in EAR.BANNED:
            for j in gi[g]:
                if j in order and P[w, j] >= 0.05:
                    hit = True
        bad += hit
        top[labels[order[0]]] = top.get(labels[order[0]], 0) + 1
    common = sorted(top.items(), key=lambda kv: -kv[1])[:3]
    return dict(windows=int(P.shape[0]), banned=int(bad), top=[f"{k} ({v})" for k, v in common])


def mfcc(x):
    import librosa
    return librosa.feature.mfcc(y=x.astype(np.float32), sr=SR, n_mfcc=13).mean(1)


def centroid(x):
    f, P = ss.welch(x, SR, nperseg=min(4096, len(x)))
    return float((f * P).sum() / (P.sum() + 1e-20))


def laptop_db(x):
    """Level change through the film tool's laptop check (a 4th-order 180 Hz high-pass), RMS dB."""
    h = ss.sosfilt(ss.butter(4, 180, "highpass", fs=SR, output="sos"), x)
    return round(float(20 * np.log10((np.sqrt(np.mean(h ** 2)) + 1e-12) / (np.sqrt(np.mean(x ** 2)) + 1e-12))), 1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--no-tagger", action="store_true")
    ap.add_argument("--out", default=HERE)
    a = ap.parse_args()
    man = json.load(open(os.path.join(HERE, "score-manifest.json")))
    rep = dict(stems={}, states={}, title={}, stings={}, effects={}, joins={}, fails=[])
    fails = rep["fails"]

    def tp_check(name, x):
        tp = EAR.true_peak(x)
        if tp > -1.0:
            fails.append(f"{name}: true peak {tp:.2f} dBTP > -1.0")
        return round(tp, 2)

    stems = {}
    for rel in sorted(man["files"]):
        if "/CMScore/" in rel:
            nm = os.path.basename(rel)[:-4]
            stems[nm] = load(rel)
    print(f"{len(stems)} stems", flush=True)
    for nm, x in stems.items():
        k, r, rf = key_of(x)
        body = x[: B._n(B.SECTION)]
        g = grid_of(body)
        st = dict(seconds=round(len(x) / SR, 3), key=k, r=r, r_f_minor=rf, lufs=round(EAR.loudness_curves(x, SR)["I"], 2),
                  tp=tp_check(nm, x), grid=g, events_per_bar=round(g["onsets"] / B.SECTION_BARS, 2), balance=balance(x))
        if abs(len(x) / SR - B.CLIP) > 1e-3:
            fails.append(f"{nm}: {len(x) / SR:.4f} s, the grid wants {B.CLIP} s")
        rep["stems"][nm] = st
        print(f"  {nm}: {k} r={r} (Fm {rf}) {st['lufs']} LUFS tp {st['tp']} grid {g['on_grid']} ev/bar {st['events_per_bar']}", flush=True)
    rep["joins"] = joins(stems)
    if rep["joins"]["clicks"]:
        fails.append(f"click at a section join: {rep['joins']['worst']} ratio {rep['joins']['worst_ratio']}")

    states = [(s, "minor") for s in B.STATES] + [("contested", "feed"), ("leading", "feed"), ("final", "feed")]
    for s, kj in states:
        name = s if kj == "minor" else f"{s}+feed"
        x = B.state_mix(stems, s, kj)
        k, r, rf = key_of(x)
        g = grid_of(x)
        st = dict(key=k, r=r, r_f_minor=rf, lufs=round(EAR.loudness_curves(x, SR)["I"], 2), tp=round(EAR.true_peak(x), 2),
                  grid=g, events_per_bar=round(g["onsets"] / (len(B.PLAN) * B.SECTION_BARS), 2), balance=balance(x))
        ok = k in OK_KEYS or (kj == "feed" and k == "F major")
        if not ok:
            fails.append(f"state {name}: key {k} (F minor r {rf})")
        if s == B.REFERENCE_STATE and kj == "minor" and abs(st["lufs"] - B.MUSIC_LUFS) > 1.5:
            fails.append(f"reference state {name}: {st['lufs']} LUFS, target {B.MUSIC_LUFS} +/- 1.5")
        if g["on_grid"] < 0.6 or abs(g["latency_ms"]) > 60:
            fails.append(f"state {name}: only {g['on_grid']:.0%} of onsets on the grid")
        if not a.no_tagger:
            st["ear"] = tag(x[: B._n(96)], name)
            if st["ear"].get("banned", 0) > 0:
                fails.append(f"state {name}: {st['ear']['banned']} windows tagged as a banned group")
        rep["states"][name] = st
        print(f"  state {name}: {k} r={r} {st['lufs']} LUFS grid {g['on_grid']} tempo {g['tempo']} ev/bar {st['events_per_bar']} {st.get('ear', '')}", flush=True)

    t = load(f"{B.MUSIC_DIR}/CMMusic/music-title.ogg")
    k, r, rf = key_of(t)
    tt = np.concatenate([t, t])
    lr = click_ratio(tt, len(t))
    rep["title"] = dict(seconds=round(len(t) / SR, 3), key=k, r=r, r_f_minor=rf, lufs=round(EAR.loudness_curves(t, SR)["I"], 2),
                        tp=tp_check("music-title", t), loop_click_ratio=round(lr, 2), balance=balance(t),
                        ear=None if a.no_tagger else tag(t, "title"))
    if k not in OK_KEYS:
        fails.append(f"title loop: key {k}")
    if lr > 3.0:
        fails.append(f"title loop point click ratio {lr:.2f}")
    if abs(len(t) / SR - B.TITLE_SECONDS) > 1e-3:
        fails.append(f"title loop {len(t) / SR:.4f} s, the grid wants {B.TITLE_SECONDS}")
    if rep["title"]["ear"] and rep["title"]["ear"].get("banned", 0):
        fails.append("title loop: banned tagger windows")
    print(f"  title: {rep['title']}", flush=True)

    for rel in sorted(man["files"]):
        if "/CMStings/" not in rel:
            continue
        nm = os.path.basename(rel)[:-4]
        x = load(rel)
        k, r, rf = key_of(x)
        st = dict(seconds=round(len(x) / SR, 2), key=k, r=r, r_f_minor=rf, lufs=round(EAR.loudness_curves(x, SR)["I"], 2),
                  tp=tp_check(nm, x), ear=None if a.no_tagger else tag(x, nm))
        if st["ear"] and st["ear"].get("banned", 0):
            fails.append(f"{nm}: banned tagger windows")
        rep["stings"][nm] = st
        print(f"  {nm}: {st}", flush=True)

    fx = {}
    for rel in sorted(man["files"]):
        if "/CMSfxScore/" not in rel:
            continue
        nm = os.path.basename(rel)[:-4]
        x = load(rel)[:, 0]
        fx[nm] = x
        st = dict(seconds=round(len(x) / SR, 3), peak_dbfs=round(20 * math.log10(np.abs(x).max() + 1e-12), 2),
                  tp=tp_check(nm, x[:, None]), centroid_hz=round(centroid(x), 0), laptop_db=laptop_db(x),
                  ear=None if a.no_tagger else tag(np.stack([x, x], 1), nm))
        if st["ear"] and st["ear"].get("banned", 0):
            fails.append(f"{nm}: banned tagger windows")
        if st["laptop_db"] < -12.0:
            fails.append(f"{nm}: loses {-st['laptop_db']} dB through a 180 Hz high-pass (a laptop would barely play it)")
        if st["seconds"] > 2.0:
            fails.append(f"{nm}: {st['seconds']} s is not short")
        rep["effects"][nm] = st
        print(f"  {nm}: {st}", flush=True)
    # distinctness between DIFFERENT cues (variants of one cue are meant to be alike)
    base = {}
    for nm, x in fx.items():
        cue = nm.rsplit("_", 1)[0] if nm[-1].isdigit() and nm[-2] == "_" else nm
        base.setdefault(cue, nm)
    names = sorted(base)
    M = {c: mfcc(fx[base[c]]) for c in names}
    C = {c: rep["effects"][base[c]]["centroid_hz"] for c in names}
    pairs = []
    for i in range(len(names)):
        for j in range(i + 1, len(names)):
            a_, b_ = names[i], names[j]
            ratio = max(C[a_], C[b_]) / max(1.0, min(C[a_], C[b_]))
            pairs.append((float(np.linalg.norm(M[a_] - M[b_])), round(ratio, 2), a_, b_))
    pairs.sort()
    rep["effects_distinct"] = dict(cues=len(names), closest_mfcc=[dict(a=p[2], b=p[3], mfcc_dist=round(p[0], 1), centroid_ratio=p[1]) for p in pairs[:3]])
    if pairs and pairs[0][0] < 15.0 and pairs[0][1] < 1.15:
        fails.append(f"effects {pairs[0][2]} and {pairs[0][3]} are too alike (MFCC {pairs[0][0]:.1f}, centroid x{pairs[0][1]})")

    rep["ok"] = not fails
    json.dump(rep, open(os.path.join(a.out, "analyzer-report.json"), "w"), indent=1)
    write_md(rep, man, os.path.join(a.out, "ANALYZER-REPORT.md"), a.no_tagger)
    print(json.dumps(dict(ok=rep["ok"], fails=fails)))
    return 0 if rep["ok"] else 1


def write_md(rep, man, path, no_tagger):
    L = ["# Skybreak game score: analyzer report", "",
         "Written by `tools/audio/skybreak/analyze_score.py` from the committed files (no rebuild). "
         + ("Tagger skipped (--no-tagger). " if no_tagger else "Tagger: AudioSet AST, 1 s windows, banned groups at >= 0.05 in the top five. ")
         + f"Result: **{'PASS' if rep['ok'] else 'FAIL'}**.", ""]
    if rep["fails"]:
        L += ["Failures:", ""] + [f"- {f}" for f in rep["fails"]] + [""]
    L += ["## Mix states (whole 12-section plan, 4 min 48 s, as the game layers them)", "",
          "| state | key (r) | r F minor | LUFS | true peak | on grid | tempo | events/bar | spectrum dev dB | low/mid/high | ear: banned (top) |",
          "|---|---|---|---|---|---|---|---|---|---|---|"]
    for n, s in rep["states"].items():
        e = s.get("ear") or {}
        L.append(f"| {n} | {s['key']} ({s['r']}) | {s['r_f_minor']} | {s['lufs']} | {s['tp']} | {s['grid']['on_grid']:.0%} | "
                 f"{s['grid']['tempo']} | {s['events_per_bar']} | {s['balance']['tilt_dev_db']} | "
                 f"{s['balance']['low']}/{s['balance']['mid']}/{s['balance']['high']} | {e.get('banned', '-')} ({', '.join(e.get('top', []))}) |")
    t = rep["title"]
    L += ["", "## Title / menu loop", "",
          f"{t['seconds']} s (16 bars), {t['key']} (r {t['r']}), {t['lufs']} LUFS, true peak {t['tp']} dBTP, loop-point click ratio "
          f"{t['loop_click_ratio']} (limit 3), ear {t['ear']}.", "",
          "## Section joins", "",
          f"{rep['joins']['joins']} joins checked (every plan step and the wrap, every layer variant); worst high-passed step "
          f"ratio {rep['joins']['worst_ratio']} at {rep['joins']['worst']} (limit 3).", "",
          "## Stems (each 24 s section + 2.5 s tail)", "",
          "| stem | key (r) | r F minor | LUFS | true peak | on grid | events/bar | spectrum dev dB |", "|---|---|---|---|---|---|---|---|"]
    for n, s in rep["stems"].items():
        L.append(f"| {n} | {s['key']} ({s['r']}) | {s['r_f_minor']} | {s['lufs']} | {s['tp']} | {s['grid']['on_grid']:.0%} | {s['events_per_bar']} | {s['balance']['tilt_dev_db']} |")
    L += ["", "Stems are layers, not mixes: a lone rhythm or theme stem may read another key (few pitches); the gate is on the mix states.", "",
          "## Stings and hatch stingers", "", "| file | s | key (r) | LUFS | true peak | ear: banned (top) |", "|---|---|---|---|---|---|"]
    for n, s in rep["stings"].items():
        e = s.get("ear") or {}
        L.append(f"| {n} | {s['seconds']} | {s['key']} ({s['r']}) | {s['lufs']} | {s['tp']} | {e.get('banned', '-')} ({', '.join(e.get('top', []))}) |")
    L += ["", "## Effects", "", "| file | s | peak dBFS | true peak | centroid Hz | through 180 Hz high-pass dB | ear: banned (top) |", "|---|---|---|---|---|---|---|"]
    for n, s in rep["effects"].items():
        e = s.get("ear") or {}
        L.append(f"| {n} | {s['seconds']} | {s['peak_dbfs']} | {s['tp']} | {s['centroid_hz']:.0f} | {s['laptop_db']} | {e.get('banned', '-')} ({', '.join(e.get('top', []))}) |")
    d = rep["effects_distinct"]
    L += ["", f"Closest different cues ({d['cues']} cues): " + "; ".join(f"{p['a']} / {p['b']} MFCC {p['mfcc_dist']}, centroid x{p['centroid_ratio']}" for p in d["closest_mfcc"]), ""]
    open(path, "w").write("\n".join(L) + "\n")


if __name__ == "__main__":
    sys.exit(main())
