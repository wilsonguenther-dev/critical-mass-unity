#!/usr/bin/env python3
"""audio_ear.py: an offline "ear" for the story-intro soundtrack. Never plays anything.

Loudness meters do not hear. This tool tries to model what a listener names when they hear a mix:

  1. Mel spectrogram PNGs, one per 5 s window plus a full-length overview, with every detected sound event drawn on
     it and labelled (event number, what the features say it would be heard as, the tagger's top label).
  2. An event list (onset detection on the effects stem when given, else on the mix) with per-event features:
     spectral centroid, bandwidth, noisiness (flatness), inharmonicity of the partials, ring time of the strongest
     partial, attack time, decay time, pitch, pitch glide, voiced fraction, roughness (20-150 Hz envelope modulation).
     A rule pass turns the features into a plain "heard as" name (cookware, livestock-like call, hoof-like thump ...).
  3. Density: non-music events per second, the busiest second, and simultaneous non-music sources (from a cue table
     when given, else from overlapping event decays).
  4. Repeats: two events within 10 s whose first 400 ms log-mel patches are near-identical (the "machine-gun" effect).
  5. Spectral balance: 1/3-octave long-term spectrum against a heuristic trailer-mix reference tilt.
  6. Dynamic contour: BS.1770 K-weighted momentary (400 ms) and short-term (3 s) loudness every 100 ms; integrated,
     LRA, sample and true peak (4x oversampled); narration minus bed while speaking (when stems are given).
  7. CLASSIFIER: an AudioSet tagger run locally over 1 s windows (0.5 s hop): the Audio Spectrogram Transformer
     MIT/ast-finetuned-audioset-10-10-0.4593 (BSD-3-Clause, 346 MB, public on Hugging Face, no sign-up). Its scores
     stand in for "it sounds like pots and pans, cows, horses". Banned label groups are reported per window.

  python3 tools/film/audio_ear.py analyze --mix MIX.wav --out DIR --name NAME \
        [--fx FX.wav ...] [--music MX.wav] [--dx DX.wav] [--cues cues.json] [--threshold-from OTHER/report.json]
  python3 tools/film/audio_ear.py compare A/report.json B/report.json --out COMPARE.png

What it cannot do: it does not hear taste, emotion, or whether the music is good. A tagger trained on YouTube clips
is a proxy for a human's first association, not a human.
"""
import argparse, csv, json, math, os, sys
import numpy as np
import scipy.signal as ss
import soundfile as sf

SR_A = 22050                     # feature analysis rate
TAGGER = "MIT/ast-finetuned-audioset-10-10-0.4593"

# Banned groups: matched against the tagger's own label names (lower case, exact).
GROUPS = {
    "cookware": ["dishes, pots, and pans", "cutlery, silverware", "clang", "chink, clink", "ding", "clatter",
                 "glass", "coin (dropping)", "frying (food)", "chopping (food)", "sink (filling or washing)"],
    "animal": ["animal", "domestic animals, pets", "wild animals", "roaring cats (lions, tigers)", "growling",
               "dog", "bark", "howl", "cat", "meow", "bird", "insect", "frog", "snake", "whale vocalization"],
    "livestock": ["livestock, farm animals, working animals", "cattle, bovinae", "moo", "cowbell", "pig", "oink",
                  "goat", "bleat", "sheep", "fowl", "chicken, rooster", "cluck", "crowing, cock-a-doodle-doo",
                  "turkey", "gobble", "duck", "quack", "goose", "honk"],
    "horse": ["horse", "neigh, whinny", "clip-clop"],
    "crowd": ["crowd", "cheering", "hubbub, speech noise, speech babble", "applause", "children shouting"],
}
WATCH = {"thunder": ["thunder", "thunderstorm"], "explosion": ["explosion", "boom", "burst, pop"],
         "roar": ["roar"], "vehicle": ["vehicle", "car", "motorboat, speedboat", "accelerating, revving, vroom",
                                      "engine", "idling", "motorcycle", "truck"],
         "siren_beep": ["siren", "police car (siren)", "civil defense siren", "beep, bleep", "television"]}
BANNED = ["cookware", "animal", "livestock", "horse", "crowd"]


# ------------------------------------------------------------------ io
def read(path, sr=None):
    x, r = sf.read(path, always_2d=True, dtype="float32")
    if sr and r != sr:
        from math import gcd
        g = gcd(int(sr), int(r))
        x = ss.resample_poly(x, sr // g, r // g, axis=0).astype(np.float32)
        r = sr
    return x, r


def mono(x):
    return x.mean(1) if x.ndim == 2 else x


# ------------------------------------------------------------------ loudness (BS.1770-4)
def kweight(x, sr):
    # pre-filter (shelf) and RLB high-pass, coefficients derived for any sr (pyloudnorm's formulation)
    G, Q, fc = 3.999843853973347, 0.7071752369554196, 1681.974450955533
    K = math.tan(math.pi * fc / sr); Vh = 10 ** (G / 20); Vb = Vh ** 0.4996667741545416
    a0 = 1 + K / Q + K * K
    b1 = [(Vh + Vb * K / Q + K * K) / a0, 2 * (K * K - Vh) / a0, (Vh - Vb * K / Q + K * K) / a0]
    a1 = [1, 2 * (K * K - 1) / a0, (1 - K / Q + K * K) / a0]
    Q2, fc2 = 0.5003270373238773, 38.13547087602444
    K = math.tan(math.pi * fc2 / sr)
    a2 = [1, 2 * (K * K - 1) / (1 + K / Q2 + K * K), (1 - K / Q2 + K * K) / (1 + K / Q2 + K * K)]
    b2 = [1, -2, 1]
    y = ss.lfilter(b1, a1, x, axis=0)
    return ss.lfilter(b2, a2, y, axis=0)


def loudness_curves(x, sr, hop=0.1):
    """Momentary (0.4 s) and short-term (3 s) LUFS every hop, plus integrated and LRA."""
    x = x if x.ndim == 2 else x[:, None]
    if len(x) < int(3.2 * sr):
        x = np.pad(x, ((0, int(3.2 * sr) - len(x)), (0, 0)))
    y = kweight(x.astype(np.float64), sr)
    p = (y ** 2).sum(1)
    cs = np.concatenate([[0], np.cumsum(p)])
    def win(w):
        n = int(w * sr); h = int(hop * sr)
        idx = np.arange(0, max(1, len(p) - n + 1), h)
        ms = (cs[idx + n] - cs[idx]) / n
        return idx / sr + w, -0.691 + 10 * np.log10(ms + 1e-12)
    tM, M = win(0.4)
    tS, S = win(3.0)
    # integrated: 400 ms blocks, 75 % overlap, abs gate -70, rel gate -10
    n = int(0.4 * sr); h = int(0.1 * sr)
    idx = np.arange(0, len(p) - n + 1, h)
    bl = (cs[idx + n] - cs[idx]) / n
    lb = -0.691 + 10 * np.log10(bl + 1e-12)
    g1 = bl[lb > -70]
    I = -70.0
    if len(g1):
        rel = -0.691 + 10 * np.log10(g1.mean()) - 10
        g2 = bl[(lb > -70) & (lb > rel)]
        I = -0.691 + 10 * np.log10(g2.mean()) if len(g2) else -70.0
    # LRA from short-term values (3 s, rel gate -20)
    s = S[S > -70]
    LRA = 0.0
    if len(s):
        pw = 10 ** ((s + 0.691) / 10)
        rel = -0.691 + 10 * np.log10(pw.mean()) - 20
        s = s[s > rel]
        if len(s) > 2:
            LRA = float(np.percentile(s, 95) - np.percentile(s, 10))
    return dict(tM=tM, M=M, tS=tS, S=S, I=float(I), LRA=LRA)


def true_peak(x):
    y = ss.resample_poly(x, 4, 1, axis=0)
    return float(20 * np.log10(np.abs(y).max() + 1e-12))


# ------------------------------------------------------------------ events
def detect_events(x, sr, thr=1.2, delta=0.5):
    """Onsets by log-mel spectral flux (mean positive dB rise per frame over 64 mel bands, floor -80 dB), not
    normalised by the loudest event, so a quiet knock under a loud crack still counts. Threshold: the larger of
    1.2 dB/frame and the local median flux (0.5 s) + 0.5 dB/frame, the standard adaptive form, so the random
    frame-to-frame flicker of a sustained noise texture is not counted as a string of events. Tuned on round 1's
    effects stem against its own cue table: the adaptive form finds the same 20 of its 27 designed cues as the fixed
    1.2 threshold, and drops 11 detections that were flicker inside its beds. An onset must reach -50 dBFS in the next
    100 ms to count as audible."""
    import librosa
    S = np.abs(librosa.stft(x, n_fft=1024, hop_length=256)) ** 2
    M = np.maximum(librosa.power_to_db(librosa.feature.melspectrogram(S=S, sr=sr, n_mels=64), ref=1.0), -80)
    flux = np.concatenate([[0], np.maximum(0, np.diff(M, axis=1)).mean(0)])
    sm = np.convolve(flux, np.ones(3) / 3, "same")
    from scipy.ndimage import median_filter
    th = np.maximum(thr, median_filter(sm, size=int(0.5 * sr / 256)) + delta)
    pk, _ = ss.find_peaks(sm - th, height=0, distance=int(0.15 * sr / 256))
    keep = []
    for f in pk:
        o = max(0, int(f * 256) - int(0.01 * sr))
        b = x[o:o + int(0.10 * sr)]
        if len(b) >= 32 and 20 * np.log10(np.sqrt((b ** 2).mean()) + 1e-9) > -50:
            keep.append(o)
    return keep


def env_db(x, sr, hop_s=0.005):
    h = max(1, int(hop_s * sr))
    n = len(x) // h
    e = np.sqrt((x[:n * h].reshape(n, h) ** 2).mean(1) + 1e-12)
    return 20 * np.log10(e), h


def event_features(x, sr, o, end):
    import librosa
    seg = x[o:end]
    out = {}
    if len(seg) < int(0.05 * sr):
        seg = x[o:o + int(0.05 * sr)]
    e, h = env_db(seg, sr)
    ip = int(np.argmax(e[: max(1, int(0.6 / 0.005))]))
    pk = e[ip]
    lin = 10 ** (e / 20); lp = lin[ip]
    a10 = np.argmax(lin[: ip + 1] >= 0.1 * lp); a90 = np.argmax(lin[: ip + 1] >= 0.9 * lp)
    out["attack_ms"] = float(max(0, a90 - a10) * h / sr * 1000)
    post = e[ip:]
    d = np.argmax(post < pk - 20) if (post < pk - 20).any() else len(post)
    out["decay_s"] = float(d * h / sr)
    out["peak_dbfs"] = float(pk)
    S = np.abs(librosa.stft(seg, n_fft=2048, hop_length=256)) + 1e-9
    out["centroid_hz"] = float(np.median(librosa.feature.spectral_centroid(S=S, sr=sr)))
    out["bandwidth_hz"] = float(np.median(librosa.feature.spectral_bandwidth(S=S, sr=sr)))
    out["noisiness"] = float(np.median(librosa.feature.spectral_flatness(S=S)))
    # partials: 250 ms after the peak, spectrum peaks
    a = o + ip * h
    w = x[a:a + int(0.25 * sr)]
    if len(w) < 1024:
        w = np.pad(w, (0, 1024 - len(w)))
    W = np.abs(np.fft.rfft(w * np.hanning(len(w)), 8192))
    f = np.fft.rfftfreq(8192, 1 / sr)
    band = (f > 60) & (f < 8000)
    Wd = 20 * np.log10(W + 1e-9)
    pks, pr = ss.find_peaks(np.where(band, Wd, -200), prominence=10, distance=6)
    inh, ring = 0.0, 0.0
    if len(pks) >= 3:
        top = pks[np.argsort(Wd[pks])[::-1][:12]]
        fk = np.sort(f[top]); f0 = fk[0]
        r = fk / f0
        wts = W[top][np.argsort(f[top])]
        inh = float((np.abs(r - np.round(r)) * wts).sum() / (wts.sum() + 1e-9))
        # ring: how long the strongest partial above 300 Hz lasts (narrow band decay to -20 dB)
        hi = top[f[top] > 300]
        if len(hi):
            fr = f[hi[0]]
            bb = ss.butter(2, [max(30, fr * 0.97), min(sr / 2 - 10, fr * 1.03)], "bandpass", fs=sr, output="sos")
            nb = ss.sosfilt(bb, x[a:a + int(3.0 * sr)])
            ne, nh = env_db(nb, sr, 0.01)
            if len(ne):
                p0 = ne[:20].max()
                ring = float((np.argmax(ne < p0 - 20) if (ne < p0 - 20).any() else len(ne)) * 0.01)
    out["inharmonicity"] = inh
    out["ring_s"] = ring
    out["n_partials"] = int(len(pks))
    # pitch and glide
    try:
        f0, vf, vp = librosa.pyin(seg[: int(1.5 * sr)], fmin=45, fmax=900, sr=sr, frame_length=2048, hop_length=512)
        ok = np.isfinite(f0)
        out["voiced_frac"] = float(ok.mean()) if len(ok) else 0.0
        if ok.sum() >= 3:
            st_ = 12 * np.log2(f0[ok] / 440.0)
            tt = np.arange(len(f0))[ok] * 512 / sr
            out["pitch_hz"] = float(np.median(f0[ok]))
            out["glide_st_per_s"] = float(np.polyfit(tt, st_, 1)[0]) if np.ptp(tt) > 0.05 else 0.0
            out["pitch_wobble_st"] = float(np.std(st_ - np.polyval(np.polyfit(tt, st_, 1), tt))) if np.ptp(tt) > 0.05 else 0.0
        else:
            out["pitch_hz"], out["glide_st_per_s"], out["pitch_wobble_st"] = 0.0, 0.0, 0.0
    except Exception:
        out["voiced_frac"], out["pitch_hz"], out["glide_st_per_s"], out["pitch_wobble_st"] = 0, 0, 0, 0
    # roughness: share of envelope modulation energy at 20-150 Hz (the beating the ear hears as rasp)
    ea = np.abs(ss.hilbert(seg[: int(1.0 * sr)])) if len(seg) > 256 else np.abs(seg)
    ea = ss.resample_poly(ea, 1, 22) if sr > 4000 else ea
    fs_e = sr / 22
    E = np.abs(np.fft.rfft(ea - ea.mean())) ** 2
    fe = np.fft.rfftfreq(len(ea), 1 / fs_e)
    tot = E[(fe > 1) & (fe < 300)].sum() + 1e-12
    out["roughness"] = float(E[(fe >= 20) & (fe <= 150)].sum() / tot)
    return out


def heard_as(ft):
    """Plain-words name for an event from its features (a rule pass, written to match the complaints)."""
    c, inh, ring, att = ft["centroid_hz"], ft["inharmonicity"], ft["ring_s"], ft["attack_ms"]
    vf, glide, ptch = ft.get("voiced_frac", 0), abs(ft.get("glide_st_per_s", 0)), ft.get("pitch_hz", 0)
    wob = ft.get("pitch_wobble_st", 0)
    if inh > 0.12 and ring > 0.25 and c > 600 and att < 30:
        return "cookware (metal ring, inharmonic)"
    if vf > 0.35 and 70 < ptch < 600 and (glide > 1.5 or wob > 0.6) and ft["decay_s"] > 0.25:
        return "livestock-like call (pitched, gliding)"
    if c < 350 and att < 20 and ft["decay_s"] < 0.45:
        return "hoof-like thump (short low knock)"
    if att < 8 and c > 1500 and ft["decay_s"] < 0.3:
        return "click/clack"
    if ft["noisiness"] > 0.2 and att > 60:
        return "whoosh/air"
    if c < 250 and ft["decay_s"] >= 0.45:
        return "boom/rumble"
    if inh < 0.08 and vf > 0.4:
        return "tonal note"
    return "hit/texture"


# ------------------------------------------------------------------ tagger
_MODEL = {}


def run_tagger(x16, hop=0.25, win=1.0):
    import torch
    from transformers import ASTFeatureExtractor, ASTForAudioClassification
    torch.set_num_threads(4)
    if not _MODEL:
        _MODEL["fe"] = ASTFeatureExtractor.from_pretrained(TAGGER)
        _MODEL["m"] = ASTForAudioClassification.from_pretrained(TAGGER, use_safetensors=True).eval()
    fe, model = _MODEL["fe"], _MODEL["m"]
    labels = [model.config.id2label[i] for i in range(len(model.config.id2label))]
    n = int(win * 16000); h = int(hop * 16000)
    starts = list(range(0, max(1, len(x16) - n + 1), h))
    probs = []
    with torch.inference_mode():
        for i in range(0, len(starts), 8):
            chunk = [x16[s:s + n] for s in starts[i:i + 8]]
            inp = fe(chunk, sampling_rate=16000, return_tensors="pt")
            probs.append(torch.sigmoid(model(**inp).logits).numpy())
    P = np.concatenate(probs, 0)
    return [s / 16000 for s in starts], labels, P


def group_index(labels):
    low = [l.lower() for l in labels]
    gi = {}
    for g, names in list(GROUPS.items()) + list(WATCH.items()):
        gi[g] = [low.index(nm) for nm in names if nm in low]
    return gi


# ------------------------------------------------------------------ spectral balance
THIRD = [25, 31.5, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500, 630, 800, 1000, 1250, 1600, 2000, 2500,
         3150, 4000, 5000, 6300, 8000, 10000, 12500, 16000]


def ref_curve(fc):
    """Heuristic trailer-mix tilt in 1/3-octave band level (dB re 500-1k): flat 50 Hz-2 kHz, -2.5 dB/oct above,
    -6 dB/oct below 40 Hz. Not a measured studio curve: a sanity reference only."""
    fc = np.asarray(fc, float)
    r = np.zeros_like(fc)
    r[fc > 2000] = -2.5 * np.log2(fc[fc > 2000] / 2000)
    r[fc < 40] = -6 * np.log2(40 / fc[fc < 40])
    return r


def third_octave(x, sr):
    f, P = ss.welch(x, sr, nperseg=16384)
    out = []
    for fc in THIRD:
        lo, hi = fc / 2 ** (1 / 6), fc * 2 ** (1 / 6)
        m = (f >= lo) & (f < hi)
        out.append(10 * np.log10(P[m].sum() + 1e-20) if m.any() else -200)
    out = np.array(out)
    norm = out[[THIRD.index(500), THIRD.index(630), THIRD.index(800), THIRD.index(1000)]].mean()
    return out - norm


# ------------------------------------------------------------------ repeats
def patch(x, sr, o):
    import librosa
    seg = x[o:o + int(0.4 * sr)]
    if len(seg) < int(0.4 * sr):
        seg = np.pad(seg, (0, int(0.4 * sr) - len(seg)))
    M = librosa.power_to_db(librosa.feature.melspectrogram(y=seg, sr=sr, n_mels=48, hop_length=256) + 1e-10)
    v = (M - M.mean()).ravel()
    return v / (np.linalg.norm(v) + 1e-9)


# ------------------------------------------------------------------ drawing
def draw_specs(x, sr, events, tag_t, tag_top, out, name, dur):
    import librosa, matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    M = librosa.power_to_db(librosa.feature.melspectrogram(y=x, sr=sr, n_mels=128, hop_length=256, fmax=11000),
                            ref=np.max, top_db=80)
    tt = np.arange(M.shape[1]) * 256 / sr
    mf = librosa.mel_frequencies(130, fmax=11000)[1:-1]
    paths = []
    # overview
    fig, ax = plt.subplots(figsize=(22, 5), dpi=90)
    ax.pcolormesh(tt, mf, M, shading="auto", cmap="magma", vmin=-80, vmax=0)
    ax.set_yscale("symlog", linthresh=500); ax.set_ylim(30, 11000)
    for e in events:
        ax.axvline(e["t"], color="cyan", lw=0.6, alpha=0.6)
    ax.set_title(f"{name}: mel spectrogram, {len(events)} detected non-music events (cyan)")
    ax.set_xlabel("film seconds"); ax.set_ylabel("Hz")
    fig.tight_layout(); p = f"{out}/spectrogram-overview.png"; fig.savefig(p); plt.close(fig); paths.append(p)
    for k in range(int(math.ceil(dur / 5))):
        t0, t1 = 5 * k, min(dur, 5 * k + 5)
        m = (tt >= t0) & (tt <= t1)
        fig, ax = plt.subplots(figsize=(14, 5.5), dpi=90)
        ax.pcolormesh(tt[m], mf, M[:, m], shading="auto", cmap="magma", vmin=-80, vmax=0)
        ax.set_yscale("symlog", linthresh=500); ax.set_ylim(30, 11000); ax.set_xlim(t0, t1)
        lane = 0
        for e in events:
            if t0 <= e["t"] < t1:
                ax.axvline(e["t"], color="cyan", lw=1)
                ax.text(e["t"] + 0.02, 9000 / (1.9 ** (lane % 5)), f"#{e['n']} {e['heard_as'].split(' (')[0]}\n"
                        f"tag: {e.get('tag_sfx', '')}", color="white", fontsize=7, va="top",
                        bbox=dict(fc="black", alpha=0.55, lw=0))
                lane += 1
        for t, top in zip(tag_t, tag_top):
            if t0 <= t + 0.5 < t1 and top["banned"]:
                ax.axvspan(t, t + 1.0, ymin=0, ymax=0.04, color="red", alpha=0.8)
        ax.set_title(f"{name}  {t0:.0f}-{t1:.0f} s   (red strip under = tagger window with a banned label)")
        ax.set_xlabel("film seconds"); ax.set_ylabel("Hz")
        fig.tight_layout(); p = f"{out}/spec-{t0:02.0f}-{t1:02.0f}s.png"; fig.savefig(p); plt.close(fig)
        paths.append(p)
    return paths


# ------------------------------------------------------------------ analyze
def analyze(a):
    import librosa
    os.makedirs(a.out, exist_ok=True)
    mix48, sr48 = read(a.mix)
    dur = len(mix48) / sr48
    rep = {"name": a.name, "file": os.path.abspath(a.mix), "duration_s": dur}
    # loudness
    L = loudness_curves(mix48, sr48)
    rep["loudness"] = {"I": round(L["I"], 2), "LRA": round(L["LRA"], 2), "TP_dbtp": round(true_peak(mix48), 2),
                       "sample_peak_dbfs": round(float(20 * np.log10(np.abs(mix48).max() + 1e-12)), 2),
                       "clipped_samples": int((np.abs(mix48) >= 0.999).sum()),
                       "max_M": round(float(L["M"].max()), 2)}
    m48 = mono(mix48)
    for nm, y in [("mono_fold", m48), ("laptop_hp180", ss.sosfilt(ss.butter(4, 180, "highpass", fs=sr48,
                                                                                   output="sos"), m48))]:
        Ly = loudness_curves(y, sr48)
        rep["loudness"][nm + "_I"] = round(Ly["I"], 2)
    mixA = librosa.resample(m48, orig_sr=sr48, target_sr=SR_A)
    # effects stem for events
    if a.fx:
        fx = sum(mono(read(p, SR_A)[0])[: len(mixA)] for p in a.fx)
        fx = np.pad(fx, (0, max(0, len(mixA) - len(fx))))
        evsrc = "effects stem(s)"
    else:
        fx, evsrc = mixA, "mix"
    on = detect_events(fx, SR_A)
    events = []
    for i, o in enumerate(on):
        end = on[i + 1] if i + 1 < len(on) else len(fx)
        end = min(end, o + int(2.0 * SR_A))
        ft = event_features(fx, SR_A, o, max(end, o + int(0.08 * SR_A)))
        ft["n"] = i + 1; ft["t"] = round(o / SR_A, 3)
        ft["heard_as"] = heard_as(ft)
        events.append(ft)
    # repeats
    pats = [patch(fx, SR_A, int(e["t"] * SR_A)) for e in events]
    reps = []
    for i in range(len(events)):
        for j in range(i + 1, len(events)):
            if events[j]["t"] - events[i]["t"] > 10:
                break
            s = float(pats[i] @ pats[j])
            if s > 0.95:
                # same sample? normalised waveform cross-correlation of the first 300 ms, lag +/- 5 ms
                oi, oj, w_ = int(events[i]["t"] * SR_A), int(events[j]["t"] * SR_A), int(0.3 * SR_A)
                xa, xb = fx[oi:oi + w_], fx[oj:oj + w_]
                wc = 0.0
                if len(xa) == w_ and len(xb) == w_:
                    c_ = ss.correlate(xa, xb, mode="full", method="fft")
                    mid = len(xb) - 1; lag = int(0.005 * SR_A)
                    wc = float(np.abs(c_[mid - lag:mid + lag + 1]).max() / (np.linalg.norm(xa) * np.linalg.norm(xb) + 1e-12))
                reps.append({"a": events[i]["n"], "b": events[j]["n"], "ta": events[i]["t"], "tb": events[j]["t"],
                             "similarity": round(s, 3), "waveform_corr": round(wc, 3), "identical": wc > 0.8})
    rep["events_source"] = evsrc
    rep["repeats_within_10s"] = reps
    rep["identical_sample_repeats_within_10s"] = sum(1 for r_ in reps if r_["identical"])
    # density
    secs = np.floor([e["t"] for e in events]).astype(int) if events else np.array([], int)
    cnt = np.bincount(secs, minlength=int(math.ceil(dur))) if len(secs) else np.zeros(int(math.ceil(dur)), int)
    rep["density"] = {"events": len(events), "events_per_s": round(len(events) / dur, 3),
                      "busiest_second": int(cnt.argmax()), "busiest_count": int(cnt.max())}
    if a.cues:
        cj = json.load(open(a.cues)); cl = cj["cues"] if isinstance(cj, dict) else cj
        nm_ = [c for c in cl if c.get("bus") in ("FX", "BG")]
        ts = np.arange(0, dur, 0.05)
        act = np.array([sum(1 for c in nm_ if c["start"] <= t < c["start"] + c["length"]) for t in ts])
        fxonly = [c for c in cl if c.get("bus") == "FX"]
        rep["density"].update({"cue_table": os.path.abspath(a.cues), "non_music_cues": len(nm_),
                               "fx_cues": len(fxonly), "fx_cues_per_s": round(len(fxonly) / dur, 3),
                               "max_simultaneous_non_music": int(act.max()),
                               "seconds_with_3plus_non_music": round(float((act >= 3).mean() * dur), 2)})
    else:
        # estimate: events whose decay window still covers t
        ts = np.arange(0, dur, 0.05)
        act = np.array([sum(1 for e in events if e["t"] <= t < e["t"] + max(0.1, e["decay_s"])) for t in ts])
        rep["density"]["max_simultaneous_est"] = int(act.max()) if len(act) else 0
    # hero hits: how far each stands out (momentary peak in the 0.5 s after it, minus the median of the 2 s before),
    # on the full mix and on the laptop simulation (180 Hz high-pass)
    if a.hits:
        lap = ss.sosfilt(ss.butter(4, 180, "highpass", fs=sr48, output="sos"), m48)
        Lf, Ll = loudness_curves(m48, sr48), loudness_curves(lap, sr48)
        hh = {}
        for th in a.hits:
            row = {}
            for nm, Lx in (("full", Lf), ("laptop", Ll)):
                tc = Lx["tM"] - 0.2
                pk = Lx["M"][(tc >= th) & (tc <= th + 0.5)].max()
                pre = np.median(Lx["M"][(tc >= th - 2.0) & (tc <= th - 0.2)])
                row[nm] = round(float(pk - pre), 1)
                row[nm + "_M"] = round(float(pk), 1)
            hh[f"{th:.2f}"] = row
        rep["hits"] = hh
        print("hits (LU over the 2 s before; full / laptop):", {k: (v["full"], v["laptop"], v["full_M"]) for k, v in hh.items()})
    # narration vs bed
    if a.dx:
        dx48 = read(a.dx, sr48)[0][: len(mix48)]
        dx48 = np.pad(dx48, ((0, len(mix48) - len(dx48)), (0, 0)))
        if a.bed:
            bed = sum(read(p, sr48)[0][: len(mix48)] for p in a.bed)
            bed = np.pad(bed, ((0, len(mix48) - len(bed)), (0, 0)))
        else:
            bed = mix48 - dx48
        Ld, Lb = loudness_curves(dx48, sr48), loudness_curves(bed, sr48)
        n = min(len(Ld["M"]), len(Lb["M"]))
        speak = Ld["M"][:n] > Ld["M"][:n].max() - 20      # speech itself, not its reverb tail
        if a.speech_from:
            # known speech times (cue table): window centre inside a spoken line
            cj = json.load(open(a.speech_from))
            if "narration" in cj:
                spans = [(l_["speech_start"], l_["speech_end"]) for l_ in cj["narration"]]
            else:
                spans = [(c["start"] + 0.07, c["start"] + c["length"] - 0.08) for c in cj["cues"] if c.get("bus") == "DX"]
            tc_ = Ld["tM"][:n] - 0.2
            speak = np.zeros(n, bool)
            for s0, s1 in spans:
                speak |= (tc_ >= s0) & (tc_ <= s1)
        # per line: contiguous speech regions
        lines, cur = [], None
        for k in range(n):
            if speak[k] and cur is None: cur = k
            if (not speak[k] or k == n - 1) and cur is not None:
                if k - cur >= 5:
                    dm = Ld["M"][cur:k] - Lb["M"][cur:k]
                    lines.append({"t0": round(float(Ld["tM"][cur] - 0.4), 2), "t1": round(float(Ld["tM"][k]), 2),
                                  "voice_minus_bed_LU_median": round(float(np.median(dm)), 1),
                                  "voice_minus_bed_LU_min10pct": round(float(np.percentile(dm, 10)), 1)})
                cur = None
        rep["narration_vs_bed"] = lines
    # spectral balance
    tb = third_octave(m48, sr48)
    rc = ref_curve(THIRD)
    rep["spectral_balance"] = {"bands_hz": THIRD, "mix_db": [round(float(v), 1) for v in tb],
                               "ref_db": [round(float(v), 1) for v in rc],
                               "dev_db": [round(float(v - r), 1) for v, r in zip(tb, rc)],
                               "harsh_2k_6k_excess_db": round(float(np.mean([tb[i] - rc[i] for i, f in
                                                                         enumerate(THIRD) if 2000 <= f <= 6300])), 1),
                               "sub_25_63_excess_db": round(float(np.mean([tb[i] - rc[i] for i, f in
                                                                       enumerate(THIRD) if 25 <= f <= 63])), 1)}
    # tagger
    x16 = librosa.resample(m48, orig_sr=sr48, target_sr=16000)
    tag_t, labels, P = run_tagger(x16)
    gi = group_index(labels)
    rep["tagger"] = {"model": TAGGER, "licence": "BSD-3-Clause", "window_s": 1.0, "hop_s": 0.25,
                     "groups_resolved": {g: [labels[i] for i in ix] for g, ix in gi.items()}}
    thr = None
    if a.threshold_from:
        thr = json.load(open(a.threshold_from))["tagger"]["threshold"]
    tag_rows, tag_top = [], []
    for k, t in enumerate(tag_t):
        p = P[k]
        top = np.argsort(p)[::-1][:5]
        g = {gname: float(p[ix].max()) if ix else 0.0 for gname, ix in gi.items()}
        gl = {gname: labels[ix[int(np.argmax(p[ix]))]] if ix else "" for gname, ix in gi.items()}
        row = {"t": round(t, 2), "top": [(labels[i], round(float(p[i]), 3)) for i in top], "groups": g,
               "group_label": gl}
        tag_rows.append(row)
    # threshold from this file's own offenders if none given: half the median banned score among windows where a
    # banned label is in the top 5 (min 0.05, max 0.15)
    if thr is None:
        off = [max(r["groups"][b] for b in BANNED) for r in tag_rows
               if any(lbl.lower() in sum([GROUPS[b] for b in BANNED], []) for lbl, _ in r["top"])]
        thr = float(np.clip(0.5 * np.median(off), 0.05, 0.15)) if off else 0.10
        rep["tagger"]["threshold_basis"] = (f"half the median banned-group score over the {len(off)} windows of "
                                            f"this file with a banned label in the top 5, clipped to 0.05-0.15")
    rep["tagger"]["threshold"] = round(thr, 3)
    banned_names = {b: set(GROUPS[b]) for b in BANNED}
    fails = []
    for r in tag_rows:
        hits = []
        for b in BANNED:
            if r["groups"][b] >= thr:
                in_top = any(lbl.lower() in banned_names[b] for lbl, _ in r["top"][:5])
                hits.append({"group": b, "label": r["group_label"][b], "score": round(r["groups"][b], 3),
                             "in_top5": in_top})
        r["banned_hits"] = hits
        tag_top.append({"banned": any(h["in_top5"] for h in hits)})
        if any(h["in_top5"] for h in hits):
            fails.append({"t": r["t"], "hits": [h for h in hits if h["in_top5"]], "top": r["top"][:3]})
    rep["tagger"]["banned_windows_top5"] = fails
    rep["tagger"]["n_windows"] = len(tag_rows)
    rep["tagger"]["n_banned_windows"] = len(fails)
    rep["tagger"]["any_banned_above_thr"] = sum(1 for r in tag_rows if r["banned_hits"])
    rep["tagger"]["group_max"] = {g: round(max(r["groups"][g] for r in tag_rows), 3) for g in gi}
    rep["tagger"]["group_windows_above_thr"] = {g: sum(1 for r in tag_rows if r["groups"][g] >= thr) for g in gi}
    # the effects stem alone, and the narration alone, through the same tagger
    stems_to_tag = []
    if a.fx:
        stems_to_tag.append(("fx", fx.astype(np.float32), SR_A))
    if a.dx:
        stems_to_tag.append(("dx", mono(read(a.dx, 16000)[0]).astype(np.float32), 16000))
    for sname, sx, ssr in stems_to_tag:
        fx16 = librosa.resample(sx, orig_sr=ssr, target_sr=16000) if ssr != 16000 else sx
        ft_t, _, FP = run_tagger(fx16)
        frows = []
        quiet = 0
        for k, t in enumerate(ft_t):
            seg_ = fx16[int(t * 16000):int((t + 1.0) * 16000)]
            if 20 * np.log10(np.sqrt(np.mean(seg_ ** 2)) + 1e-12) < -50:
                quiet += 1      # the stem is (near) silent here: nothing for a listener to name
                continue
            p = FP[k]; top = np.argsort(p)[::-1][:5]
            g = {gname: float(p[ix].max()) if ix else 0.0 for gname, ix in gi.items()}
            gl = {gname: labels[ix[int(np.argmax(p[ix]))]] if ix else "" for gname, ix in gi.items()}
            topl = [(labels[i], round(float(p[i]), 3)) for i in top]
            hits = [{"group": b, "label": gl[b], "score": round(g[b], 3)} for b in BANNED
                    if g[b] >= thr and any(l.lower() in banned_names[b] for l, _ in topl)]
            frows.append({"t": round(t, 2), "top": topl, "groups": g, "hits": hits})
        key_ = "tagger_fx_stem" if sname == "fx" else "tagger_dx_stem"
        rep[key_] = {
            "quiet_windows_skipped_below_-50dBFS": quiet,
            "n_windows": len(frows), "n_banned_windows": sum(1 for r in frows if r["hits"]),
            "banned_windows_top5": [{"t": r["t"], "hits": r["hits"], "top": r["top"][:3]} for r in frows if r["hits"]],
            "group_max": {g: round(max(r["groups"][g] for r in frows), 3) for g in gi},
            "group_windows_above_thr": {g: sum(1 for r in frows if r["groups"][g] >= thr) for g in gi},
            "top1_per_window": [(r["t"], r["top"][0][0], r["top"][0][1]) for r in frows]}
        print(f"{sname.upper()}-STEM tagger: banned windows", rep[key_]["n_banned_windows"], "/", len(frows),
              "group max", rep[key_]["group_max"])
        for r in frows:
            if r["hits"]:
                print("  ", sname, r["t"], [(h["label"], h["score"]) for h in r["hits"]], r["top"][:3])
    # tag each event with the window that contains it
    for e in events:
        k = min(range(len(tag_t)), key=lambda i: abs(tag_t[i] + 0.5 - e["t"]))
        e["tag_top"] = tag_rows[k]["top"][0][0]
        e["tag_top_score"] = tag_rows[k]["top"][0][1]
        generic = {"Music", "Speech", "Male speech, man speaking", "Narration, monologue", "Sound effect",
                   "Musical instrument", "Inside, small room", "Silence"}
        sfx_lab = [(l, s_) for l, s_ in tag_rows[k]["top"] if l not in generic]
        e["tag_sfx"] = f"{sfx_lab[0][0]} {sfx_lab[0][1]:.2f}" if sfx_lab else ""
        e["tag_top5"] = "; ".join(f"{l} {s_:.2f}" for l, s_ in tag_rows[k]["top"])
        bh = [h for h in tag_rows[k]["banned_hits"]]
        e["tag_banned"] = ";".join(f"{h['label']} {h['score']}" for h in bh)
    rep["events"] = events
    rep["heard_as_counts"] = {}
    for e in events:
        rep["heard_as_counts"][e["heard_as"]] = rep["heard_as_counts"].get(e["heard_as"], 0) + 1
    # dynamic contour (decimated)
    rep["contour"] = {"tS": [round(float(v), 2) for v in L["tS"][::5]], "S": [round(float(v), 1) for v in L["S"][::5]],
                      "tM": [round(float(v), 2) for v in L["tM"][::2]], "M": [round(float(v), 1) for v in L["M"][::2]]}
    rep["images"] = draw_specs(mixA, SR_A, events, tag_t, tag_top, a.out, a.name, dur)
    with open(f"{a.out}/events.csv", "w", newline="") as fh:
        keys = ["n", "t", "heard_as", "tag_top", "tag_top_score", "tag_sfx", "tag_top5", "tag_banned", "centroid_hz", "bandwidth_hz",
                "noisiness", "inharmonicity", "ring_s", "attack_ms", "decay_s", "pitch_hz", "glide_st_per_s",
                "pitch_wobble_st", "voiced_frac", "roughness", "peak_dbfs", "n_partials"]
        w = csv.DictWriter(fh, keys, extrasaction="ignore"); w.writeheader()
        for e in events:
            w.writerow({k: (round(v, 3) if isinstance(v, float) else v) for k, v in e.items()})
    with open(f"{a.out}/tags.csv", "w", newline="") as fh:
        w = csv.writer(fh); w.writerow(["t", "top1", "s1", "top2", "s2", "top3", "s3"] + list(gi))
        for r in tag_rows:
            w.writerow([r["t"]] + sum([[l, s] for l, s in r["top"][:3]], []) + [round(r["groups"][g], 3) for g in gi])
    json.dump(rep, open(f"{a.out}/report.json", "w"), indent=1, default=float)
    print(json.dumps({k: rep[k] for k in ["name", "loudness", "density", "heard_as_counts"]}, indent=1))
    print("tagger thr", rep["tagger"]["threshold"], "banned windows (top5)", len(fails), "/", len(tag_rows),
          "group max", rep["tagger"]["group_max"], "windows>=thr", rep["tagger"]["group_windows_above_thr"])
    print("repeats", len(reps), "identical-sample repeats", rep["identical_sample_repeats_within_10s"], "harsh 2-6k excess", rep["spectral_balance"]["harsh_2k_6k_excess_db"])
    if "narration_vs_bed" in rep:
        print("narration-bed", [(l["t0"], l["voice_minus_bed_LU_median"]) for l in rep["narration_vs_bed"]])


def compare(a):
    import librosa, matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    R = [json.load(open(p)) for p in a.reports]
    fig, axs = plt.subplots(2 * len(R) + 1, 1, figsize=(24, 5.2 * len(R) + 3.5), dpi=80,
                            gridspec_kw={"height_ratios": sum([[4, 1] for _ in R], []) + [2]})
    for k, r in enumerate(R):
        x, sr = read(r["file"], SR_A); x = mono(x)
        M = librosa.power_to_db(librosa.feature.melspectrogram(y=x, sr=sr, n_mels=128, hop_length=256, fmax=11000),
                                ref=np.max, top_db=80)
        tt = np.arange(M.shape[1]) * 256 / sr
        mf = librosa.mel_frequencies(130, fmax=11000)[1:-1]
        ax = axs[2 * k]
        ax.pcolormesh(tt, mf, M, shading="auto", cmap="magma", vmin=-80, vmax=0)
        ax.set_yscale("symlog", linthresh=500); ax.set_ylim(30, 11000); ax.set_xlim(0, r["duration_s"])
        for e in r["events"]:
            ax.axvline(e["t"], color="cyan", lw=0.6, alpha=0.7)
        tg = r["tagger"]
        ax.set_title(f"{r['name']}: {r['density']['events']} non-music events "
                     f"({r['density']['events_per_s']}/s), tagger windows with a banned label in the top 5: "
                     f"{tg['n_banned_windows']}/{tg['n_windows']} (thr {tg['threshold']}), "
                     f"I {r['loudness']['I']} LUFS, LRA {r['loudness']['LRA']}, TP {r['loudness']['TP_dbtp']}",
                     fontsize=12, loc="left")
        ax.set_ylabel("Hz")
        ax2 = axs[2 * k + 1]
        ax2.set_xlim(0, r["duration_s"]); ax2.set_ylim(0, 1); ax2.set_yticks([])
        cols = {"cookware": "#e0a020", "animal": "#d04040", "livestock": "#ff4fa0", "horse": "#a050ff",
                "crowd": "#40c0ff"}
        for w in tg["banned_windows_top5"]:
            for h in w["hits"]:
                ax2.axvspan(w["t"], w["t"] + 1, color=cols[h["group"]], alpha=0.6)
                ax2.text(w["t"] + 0.05, 0.5, h["label"][:14], fontsize=6, rotation=90, va="center")
        ax2.set_ylabel("banned", rotation=0, ha="right")
    ax = axs[-1]
    for r in R:
        ax.plot(r["contour"]["tS"], r["contour"]["S"], label=f"{r['name']} short-term LUFS")
    ax.set_xlim(0, R[0]["duration_s"]); ax.set_ylim(-45, -5); ax.legend(loc="lower right"); ax.set_xlabel("film s")
    ax.grid(alpha=0.3)
    fig.tight_layout(); fig.savefig(a.out); print("wrote", a.out)


def main():
    p = argparse.ArgumentParser()
    sp = p.add_subparsers(dest="cmd", required=True)
    q = sp.add_parser("analyze")
    q.add_argument("--mix", required=True); q.add_argument("--out", required=True); q.add_argument("--name", default="mix")
    q.add_argument("--fx", nargs="*"); q.add_argument("--music"); q.add_argument("--dx"); q.add_argument("--cues")
    q.add_argument("--bed", nargs="*")
    q.add_argument("--hits", nargs="*", type=float)
    q.add_argument("--speech-from")
    q.add_argument("--threshold-from")
    c = sp.add_parser("compare")
    c.add_argument("reports", nargs="+"); c.add_argument("--out", required=True)
    a = p.parse_args()
    analyze(a) if a.cmd == "analyze" else compare(a)


if __name__ == "__main__":
    main()
