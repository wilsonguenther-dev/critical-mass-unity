#!/usr/bin/env python3
"""Intro film 2 soundtrack, built from zero (plan: intro-film-2/audio/SOUND-SCRIPT.md). Never plays anything.

Everything except the six narration lines is synthesised here with numpy/scipy: an original 47.333 s cue ("Skybreak",
F minor, 80 BPM) for synthesised strings, brass, horns, choir pad, taiko, timpani, glass voice and sub; twelve designed
effect moments; one shared hall (one impulse response, the same early reflections for every source). The narration is
the repo's Kokoro-82M bm_george lines (Apache-2.0), re-timed, EQ'd, de-essed, compressed and put in the same hall.

  python3 tools/film/score_intro.py OUT_DIR [--voice-dir DIR]

Writes OUT_DIR/final-mix.wav (48 kHz stereo, 24-bit), stems/{music,effects,narration}.wav (same gain as the mix, before
the limiter), checks/{mono-fold,laptop-hp180}.wav, cues.json (every designed cue: id, start, length, bus, recipe).
"""
import json, math, os, subprocess, sys
import numpy as np
import scipy.signal as ss

SR = 48000
L = 1420 / 30.0                    # 47.333 s
N = int(round(L * SR))
BEAT, BAR = 0.75, 3.0
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import audio_ear as EAR            # noqa: E402  (loudness and true-peak measurement)

RNG_SEED = 47
NOTES = {"C": 0, "Db": 1, "D": 2, "Eb": 3, "E": 4, "F": 5, "Gb": 6, "G": 7, "Ab": 8, "A": 9, "Bb": 10, "B": 11}


def hz(name):
    """'F2' -> 87.31 Hz (A4 = 440)."""
    p, o = name[:-1], int(name[-1])
    return 440.0 * 2 ** ((NOTES[p] + 12 * (o + 1) - 69) / 12)


def S(t):
    return int(round(t * SR))


# ------------------------------------------------------------------ dsp
def sos(kind, f, order=2):
    if kind == "bp":
        return ss.butter(order, [max(15, f[0]), min(SR / 2 - 100, f[1])], "bandpass", fs=SR, output="sos")
    return ss.butter(order, min(f, SR / 2 - 100), kind, fs=SR, output="sos")


def filt(x, kind, f, order=2):
    return ss.sosfilt(sos(kind, f, order), x, axis=0)


def sweep_bp(x, fc, q=1.2, block=512):
    """Band-pass whose centre follows fc (array, Hz), block-wise with the filter state carried over."""
    y = np.zeros_like(x)
    zi = None
    for i in range(0, len(x), block):
        f = float(np.clip(fc[min(i + block // 2, len(fc) - 1)], 40, SR / 2 - 2000))
        bw = f / q
        s_ = ss.butter(2, [max(20, f - bw / 2), f + bw / 2], "bandpass", fs=SR, output="sos")
        if zi is None:
            zi = np.zeros((s_.shape[0], 2))
        y[i:i + block], zi = ss.sosfilt(s_, x[i:i + block], zi=zi)
    return y


def sweep_lp(x, fc, block=512):
    y = np.zeros_like(x)
    zi = None
    for i in range(0, len(x), block):
        f = float(np.clip(fc[min(i + block // 2, len(fc) - 1)], 30, SR / 2 - 2000))
        s_ = ss.butter(2, f, "lowpass", fs=SR, output="sos")
        if zi is None:
            zi = np.zeros((s_.shape[0], 2))
        y[i:i + block], zi = ss.sosfilt(s_, x[i:i + block], zi=zi)
    return y


def saw(f, n, ph0=0.0):
    """Band-limited (polyBLEP) sawtooth; f scalar or array of length n."""
    f = np.broadcast_to(np.asarray(f, float), (n,))
    dt = f / SR
    ph = (ph0 + np.cumsum(dt)) % 1.0
    y = 2 * ph - 1
    m = ph < dt
    x = ph[m] / dt[m]; y[m] -= x + x - x * x - 1
    m = ph > 1 - dt
    x = (ph[m] - 1) / dt[m]; y[m] -= x * x + x + x + 1
    return y


def sine(f, n, ph0=0.0):
    f = np.broadcast_to(np.asarray(f, float), (n,))
    return np.sin(2 * np.pi * np.cumsum(f) / SR + ph0)


def noise(n, seed, color="white"):
    r = np.random.default_rng(seed)
    w = r.standard_normal(n)
    if color == "pink":
        W = np.fft.rfft(w); f = np.fft.rfftfreq(n, 1 / SR); f[0] = f[1]
        w = np.fft.irfft(W / np.sqrt(f), n)
    elif color == "brown":
        W = np.fft.rfft(w); f = np.fft.rfftfreq(n, 1 / SR); f[0] = f[1]
        w = np.fft.irfft(W / f, n)
    return w / (np.std(w) + 1e-12)


def env_ar(n, a, r, hold=None, curve=2.0):
    """Attack a s (raised cosine), hold, release r s (exponential-ish)."""
    e = np.ones(n)
    na = max(1, min(n, S(a)))
    e[:na] = 0.5 - 0.5 * np.cos(np.linspace(0, np.pi, na))
    nr = max(1, min(n, S(r)))
    e[n - nr:] *= np.linspace(1, 0, nr) ** curve
    return e


def expdec(n, tau, a=0.002):
    t = np.arange(n) / SR
    e = np.exp(-t / tau)
    na = max(1, S(a))
    e[:na] *= np.linspace(0, 1, na) ** 2
    return e


def pan(x, p):
    """Constant-power pan, p in [-1, 1]; x mono -> stereo."""
    a = (p + 1) * np.pi / 4
    return np.stack([x * np.cos(a), x * np.sin(a)], 1)


def db(x):
    return 10 ** (x / 20)


def rms(x):
    return float(np.sqrt(np.mean(np.square(x)) + 1e-20))


def fade(x, fin=0.005, fout=0.01):
    x = x.copy()
    a, b = max(1, S(fin)), max(1, S(fout))
    x[:a] *= np.linspace(0, 1, a)[:, None] if x.ndim == 2 else np.linspace(0, 1, a)
    x[-b:] *= np.linspace(1, 0, b)[:, None] if x.ndim == 2 else np.linspace(1, 0, b)
    return x


# ------------------------------------------------------------------ buses
class Bus:
    def __init__(self, name):
        self.name = name
        self.x = np.zeros((N + S(4.0), 2))
        self.cues = []

    def add(self, t, sig, gain=1.0, p=0.0, cue=None):
        sig = pan(sig, p) if sig.ndim == 1 else sig
        i = S(t)
        if i < 0:
            sig = sig[-i:]; i = 0
        j = min(len(self.x), i + len(sig))
        self.x[i:j] += sig[: j - i] * gain
        if cue:
            self.cues.append(dict(id=cue[0], start=round(t, 3), length=round(len(sig) / SR, 3), bus=self.name,
                                  recipe=cue[1]))


MX, FX, DX = Bus("MX"), Bus("FX"), Bus("DX")
_seed = [1000]


def seed():
    _seed[0] += 1
    return _seed[0]


# ------------------------------------------------------------------ instruments (music)
def strings(notes, dur, amp=0.1, a=0.6, r=1.0, bright=4200, voices=6, vib=0.08, spread=0.6):
    """Ensemble pad: detuned band-limited saws with delayed vibrato, one low-pass for the section."""
    n = S(dur + r)
    rng = np.random.default_rng(seed())
    out = np.zeros((n, 2))
    t = np.arange(n) / SR
    for f in notes:
        for v in range(voices):
            det = 2 ** (rng.uniform(-9, 9) / 1200)
            vr = rng.uniform(4.6, 5.8)
            vd = vib * np.clip(t / 0.6, 0, 1) / 12
            ff = f * det * 2 ** (vd * np.sin(2 * np.pi * vr * t + rng.uniform(0, 6.28)))
            s_ = saw(ff, n, rng.uniform())
            out += pan(s_, rng.uniform(-spread, spread)) / math.sqrt(voices * len(notes))
    out = filt(filt(out, "lowpass", min(bright * 1.5, 7000)), "highpass", 55)
    e = env_ar(n, a, r, curve=1.5)
    return out * e[:, None] * amp


def ostinato_note(f, dur=0.33, amp=0.1, bright=3600):
    n = S(dur + 0.08)
    rng = np.random.default_rng(seed())
    x = np.zeros(n)
    for v in range(3):
        x += saw(f * 2 ** (rng.uniform(-6, 6) / 1200), n, rng.uniform()) + 0.5 * saw(2 * f * 2 ** (rng.uniform(-6, 6) / 1200), n, rng.uniform())
    x /= 4.5
    e_fast = expdec(n, 0.07, 0.004)
    dark, br = filt(x, "lowpass", 450), filt(x, "lowpass", bright)
    y = dark + (br - dark) * e_fast
    y *= expdec(n, 0.16, 0.004) * env_ar(n, 0.004, 0.06, curve=1)
    return filt(y, "highpass", 45) * amp


def braam(notes, dur, amp=0.2, a=0.03, r=1.2, open_t=0.25, top=1700, sat=1.8):
    n = S(dur + r)
    rng = np.random.default_rng(seed())
    x = np.zeros((n, 2))
    for f in notes:
        for v in range(5):
            s_ = saw(f * 2 ** (rng.uniform(-12, 12) / 1200), n, rng.uniform())
            x += pan(s_, rng.uniform(-0.5, 0.5)) / math.sqrt(5 * len(notes))
    t = np.arange(n) / SR
    open_ = np.clip(t / open_t, 0, 1) ** 0.7 * np.exp(-np.maximum(0, t - open_t) / 1.2)
    dark, br = filt(x, "lowpass", 220), filt(x, "lowpass", top)
    y = dark + (br - dark) * open_[:, None]
    y = np.tanh(sat * y) / np.tanh(sat)
    y = filt(y, "highpass", 30)
    return y * env_ar(n, a, r, curve=1.5)[:, None] * amp


def horn(f, dur, amp=0.12, a=0.09, r=0.5, p=0.0):
    n = S(dur + r)
    rng = np.random.default_rng(seed())
    t = np.arange(n) / SR
    x = np.zeros(n)
    for v in range(3):
        vib = 2 ** ((0.12 * np.clip((t - 0.25) / 0.4, 0, 1) * np.sin(2 * np.pi * 5.1 * t + v)) / 12)
        x += saw(f * vib * 2 ** (rng.uniform(-5, 5) / 1200), n, rng.uniform())
    x /= 3
    sw = np.clip(t / 0.35, 0, 1)
    y = filt(x, "lowpass", 700) * (1 - sw * 0.5) + filt(x, "lowpass", 1900) * sw * 0.5
    y = np.tanh(1.4 * y)
    return pan(filt(y, "highpass", 70) * env_ar(n, a, r, curve=1.5) * amp, p)


VOWELS = {"a": [(730, 1.0), (1090, 0.55), (2440, 0.22)], "o": [(420, 1.0), (800, 0.4), (2400, 0.07)],
          "u": [(320, 1.0), (760, 0.25), (2300, 0.05)]}


def choir(notes, dur, amp=0.1, vowel="o", a=0.7, r=1.4, voices=9):
    n = S(dur + r)
    rng = np.random.default_rng(seed())
    t = np.arange(n) / SR
    src = np.zeros((n, 2))
    for f in notes:
        for v in range(voices):
            vib = 2 ** ((0.1 * np.sin(2 * np.pi * rng.uniform(4.3, 5.6) * t + rng.uniform(0, 6.3))) / 12)
            s_ = saw(f * vib * 2 ** (rng.uniform(-13, 13) / 1200), n, rng.uniform())
            src += pan(s_, rng.uniform(-0.7, 0.7)) / math.sqrt(voices * len(notes))
    src += 0.04 * np.stack([noise(n, seed()), noise(n, seed())], 1)
    y = np.zeros_like(src)
    for fc, g in VOWELS[vowel]:
        y += g * filt(src, "bp", (fc * 0.8, fc * 1.2))
    y += 0.15 * filt(src, "lowpass", 500)
    return filt(y, "highpass", 90) * env_ar(n, a, r, curve=1.5)[:, None] * amp * 2.2


def taiko(amp=0.3, f0=None, p=0.0, big=False):
    rng = np.random.default_rng(seed())
    f0 = f0 or rng.uniform(62, 74)
    dur = 1.4 if big else 0.9
    n = S(dur)
    t = np.arange(n) / SR
    f = f0 * (1 + 0.9 * np.exp(-t / 0.018))
    body = sine(f, n) * np.exp(-t / (0.42 if big else 0.28))
    body += 0.35 * sine(f * 1.52, n, 1.0) * np.exp(-t / 0.12)
    body += 0.15 * sine(f * 2.2, n, 2.0) * np.exp(-t / 0.06)
    stick = filt(noise(n, seed()), "bp", (180, 1400)) * np.exp(-t / 0.014) * 0.5
    slap = filt(noise(n, seed()), "lowpass", 3500) * np.exp(-t / 0.004) * 0.12
    x = np.tanh(1.6 * (body + stick + slap)) * rng.uniform(0.85, 1.0)
    x *= env_ar(n, 0.0015, 0.05, curve=1)
    if big:
        x = x + 0.6 * sine(f0 * 0.5 * (1 + 0.5 * np.exp(-t / 0.03)), n) * np.exp(-t / 0.6)
    return pan(x * amp, p)


def timpani(f0, amp=0.2, tau=1.4, p=0.0, soft=False):
    rng = np.random.default_rng(seed())
    n = S(tau * 2.2)
    t = np.arange(n) / SR
    x = np.zeros(n)
    for ratio, g, tt in [(1, 1, 1), (1.5, 0.45, 0.6), (1.98, 0.3, 0.45), (2.44, 0.18, 0.3), (2.96, 0.1, 0.2)]:
        x += g * sine(f0 * ratio * (1 + 0.02 * np.exp(-t / 0.05)), n, rng.uniform(0, 6)) * np.exp(-t / (tau * tt))
    mallet = filt(noise(n, seed()), "lowpass", 500 if soft else 900) * np.exp(-t / 0.01) * (0.2 if soft else 0.4)
    x = (x + mallet) * env_ar(n, 0.012 if soft else 0.002, 0.1, curve=1)
    return pan(x * amp, p)


def glass(f, dur=2.0, amp=0.08, a=0.045, tau=1.6, p=0.0):
    """Harmonic partials only (no bell ratios), soft attack: glassy, never a ping."""
    rng = np.random.default_rng(seed())
    n = S(dur + 1.0)
    t = np.arange(n) / SR
    x = np.zeros(n)
    for k, g in [(1, 1.0), (2, 0.28), (3, 0.1), (4, 0.04)]:
        for d in (-3, 3):
            x += g * sine(f * k * 2 ** (d / 1200), n, rng.uniform(0, 6)) * np.exp(-t / (tau / k ** 0.5))
    x *= (1 + 0.08 * np.sin(2 * np.pi * 4.2 * t))
    x *= env_ar(n, a, 0.8, curve=1.5)
    return pan(x * amp / 2.6, p)


def sub(f0, f1, dur, amp=0.3, a=0.01, tau=None):
    n = S(dur)
    t = np.arange(n) / SR
    f = f1 + (f0 - f1) * np.exp(-t / 0.18)
    x = sine(f, n)
    e = np.exp(-t / tau) if tau else np.ones(n)
    return x * e * env_ar(n, a, min(0.4, dur / 2), curve=1.5) * amp


def riser(dur, amp=0.1, f0=300, f1=5000):
    n = S(dur)
    t = np.arange(n) / SR
    fc = f0 * (f1 / f0) ** (t / dur) ** 1.5
    x = np.stack([sweep_bp(noise(n, seed()), fc, 2.0), sweep_bp(noise(n, seed()), fc, 2.0)], 1)
    e = (t / dur) ** 2.2
    return x * e[:, None] * amp * env_ar(n, 0.05, 0.02, curve=1)[:, None]


# ------------------------------------------------------------------ the score
def compose():
    m = MX

    def chord(names):
        return [hz(x) for x in names.split()]

    # 1 calm: choir "oo" and high strings on F minor add9 from the first light
    m.add(0.40, choir(chord("F3 Ab3 C4"), 3.6, 0.11, "u", a=1.6, r=0.6), cue=("M1-calm-choir", "choir oo Fm"))
    m.add(0.60, strings(chord("F4 C5 F5 G5"), 3.3, 0.05, a=2.0, r=0.5, bright=3200), cue=("M1-calm-strings", "strings Fm add9"))
    # suck-in at the 3.75 cut: the chord swelling backwards into the crack downbeat
    rev = strings(chord("F3 C4 F4 Ab4"), 0.75, 0.16, a=0.005, r=0.7, bright=3000)[::-1]
    rev = rev[-S(0.75):]
    m.add(4.50 - 0.75, fade(rev, 0.01, 0.006), cue=("M1b-suck-in", "reversed strings Fm"))

    # 2+3 crack and rise
    m.add(4.50, braam(chord("F1 F2 C3"), 2.6, 0.24, r=1.6), cue=("M2-crack-braam", "braam F"))
    m.add(4.50, taiko(0.42, 58, 0, big=True), cue=("M2-crack-taiko", "big taiko"))
    m.add(4.62, strings(chord("F2 C3 F3 Ab3"), 2.9, 0.08, a=0.9, r=0.3, bright=2200), cue=("M2-pad-Fm", "strings Fm"))
    m.add(7.50, strings(chord("Db3 F3 Ab3 Db4"), 2.25, 0.10, a=0.25, r=0.06, bright=2800), cue=("M3-pad-Db", "strings Db cresc"))
    pat_F = ["F2", "F2", "F3", "F2", "Ab2", "F2", "C3", "F2"]
    pat_Db = ["Db2", "Db2", "Db3", "Db2", "F2", "Db2", "Ab2", "Db2"]
    pat_Bb = ["Bb1", "Bb1", "Bb2", "Bb1", "Db2", "Bb1", "F2", "Bb1"]
    pat_C = ["C2", "C2", "C3", "C2", "E2", "C2", "G2", "C2"]

    def ost(t0, t1, pats, amp, step=0.375, cres=(1.0, 1.0)):
        k, t = 0, t0
        while t < t1 - 1e-6:
            pat = pats[int((t - t0) // (8 * step)) % len(pats)] if isinstance(pats[0], list) else pats
            g = cres[0] + (cres[1] - cres[0]) * (t - t0) / max(1e-3, t1 - t0)
            acc = 1.0 if k % 4 == 0 else 0.78
            MX.add(t, ostinato_note(hz(pat[k % 8]), min(0.33, step * 0.9), amp * g * acc), p=(-0.25 if k % 2 else 0.25))
            k += 1; t += step
        MX.cues.append(dict(id=f"ost-{t0:.2f}", start=t0, length=round(t1 - t0, 3), bus="MX", recipe="ostinato"))

    ost(4.50, 7.50, pat_F, 0.11, cres=(0.8, 1.0))
    ost(7.50, 9.75, pat_Db, 0.12, cres=(1.0, 1.25))
    m.add(7.95, timpani(hz("F2"), 0.22, 1.2, soft=True), cue=("M3-settle-timp", "timpani F2"))

    # 4 fall: high strings hang on Db, far away
    m.add(9.75, strings(chord("F5 Ab5 Db6"), 2.6, 0.035, a=0.35, r=1.4, bright=4000), cue=("M4-fall-high", "strings Db high"))

    # 5 shrine: mystery, motif on the glass voice, the score's pulse becomes the heartbeat
    m.add(12.00, choir(chord("F3 Ab3 C4"), 3.1, 0.065, "u", a=1.2, r=1.0), cue=("M5-shrine-choir-Fm", "choir oo Fm"))
    m.add(15.00, choir(chord("Db3 F3 Ab3"), 2.1, 0.07, "u", a=0.8, r=0.5), cue=("M5-shrine-choir-Db", "choir oo Db"))
    m.add(12.20, strings(chord("Ab4 C5 F5"), 4.6, 0.03, a=1.5, r=0.6, bright=3500), cue=("M5-shrine-strings", "strings high"))
    for t, nm in [(13.50, "C5"), (14.25, "F5"), (15.00, "G5"), (15.75, "Ab5")]:
        m.add(t, glass(hz(nm), 2.2 if nm == "Ab5" else 1.2, 0.07, p=-0.15 + 0.1 * (t - 13.5)))
    m.cues.append(dict(id="M5-egg-motif", start=13.5, length=3.0, bus="MX", recipe="glass motif C F G Ab"))
    for t in [12.00, 13.50, 15.00, 16.50]:
        m.add(t, timpani(hz("F1") * 1.25, 0.20, 0.35, soft=True))
        m.add(t + 0.20, timpani(hz("F1") * 1.18, 0.13, 0.30, soft=True))
    m.cues.append(dict(id="M5-heartbeat", start=12.0, length=5.0, bus="MX", recipe="soft timpani lub-dub, beats 1 and 3"))
    # bloom into the peoples
    m.add(16.45, strings(chord("Bb2 Db3 F3 Bb3"), 0.85, 0.09, a=0.75, r=0.12, bright=2600), cue=("M5b-bloom", "strings swell"))

    # 6 peoples: the march begins on beat 2 (17.25); Azure drums left, Crimson right
    m.add(17.25, strings(chord("Bb2 Db3 F3"), 2.3, 0.06, a=0.2, r=0.2, bright=2200), cue=("M6-pad-Bbm", "strings Bbm"))
    ost(17.25, 19.50, pat_Bb, 0.085)
    for k in range(3):
        m.add(17.25 + k * 0.75, taiko(0.20, p=-0.55))
        m.add(17.625 + k * 0.75, taiko(0.15, p=0.55))
    m.cues.append(dict(id="M6-rival-taiko", start=17.25, length=2.25, bus="MX", recipe="taiko L/R alternating"))

    # 7 hill: F minor at "One"; taiko eighths, brass swell to the clash
    m.add(19.50, strings(chord("F2 C3 F3 Ab3"), 3.0, 0.07, a=0.15, r=0.05, bright=2400), cue=("M7-pad-Fm", "strings Fm"))
    m.add(19.50, braam(chord("F2 C3"), 2.95, 0.10, a=1.8, r=0.05, open_t=2.9, top=1400, sat=1.3), cue=("M7-brass-swell", "braam swell"))
    m.add(21.00, choir(chord("F3 Ab3 C4"), 1.48, 0.06, "o", a=1.4, r=0.03), cue=("M7-choir-swell", "choir ah swell"))
    ost(19.50, 22.50, pat_F, 0.10, cres=(0.9, 1.2))
    for k in range(8):
        t = 19.50 + k * 0.375
        if t < 22.10:
            m.add(t, taiko(0.24 if k % 2 == 0 else 0.15, p=(-0.5 if k % 2 == 0 else 0.5), big=(k in (0, 4))))
    for k, t in enumerate([21.75, 22.0, 22.1875, 22.3125]):
        m.add(t, taiko(0.14 + 0.05 * k, p=0.3 * (-1) ** k))
    m.cues.append(dict(id="M7-hill-taiko", start=19.5, length=3.0, bus="MX", recipe="taiko eighths + fill"))
    # the clash: tutti on Db
    m.add(22.50, braam(chord("Db2 Ab2 Db3 F3"), 0.55, 0.26, r=0.2, sat=1.4), cue=("M7b-clash-tutti", "braam Db"))
    m.add(22.50, strings(chord("Db3 F3 Ab3 Db4 F4"), 0.5, 0.09, a=0.01, r=0.7, bright=3000))
    m.add(22.50, choir(chord("Db4 F4 Ab4"), 0.5, 0.06, "o", a=0.02, r=0.6))
    m.add(22.50, taiko(0.42, 60, -0.2, big=True)); m.add(22.51, taiko(0.36, 66, 0.2, big=True))

    # 8 choice: pulled back under the voice
    m.add(23.25, strings(chord("F3 Ab3 C4"), 0.75, 0.05, a=0.4, r=0.4, bright=3000), cue=("M8-pad", "strings Fm"))
    m.add(23.80, strings(chord("F3 A3 C4 A4"), 1.85, 0.05, a=0.3, r=0.4, bright=2800), cue=("M8-feed-major", "strings F major"))
    for t, nm in [(23.80, "C5"), (24.15, "F5"), (24.50, "G5"), (24.85, "A5")]:
        m.add(t, glass(hz(nm), 1.4 if nm == "A5" else 0.6, 0.06, p=-0.2))
    m.cues.append(dict(id="M8-motif-major", start=23.8, length=2.0, bus="MX", recipe="glass motif C F G A"))
    m.add(25.80, strings(chord("F2 Gb2"), 2.6, 0.06, a=0.25, r=0.4, bright=900), cue=("M8-corrupt-rub", "low strings F/Gb"))
    m.add(25.80, strings(chord("F3 Ab3 C4"), 2.6, 0.05, a=0.3, r=0.4, bright=2500))
    for k, (t, nm, cents) in enumerate([(25.80, "C5", 0), (26.15, "F5", -8), (26.50, "Gb5", -16), (26.85, "B4", -25)]):
        m.add(t, horn(hz(nm) * 2 ** (cents / 1200), 0.45 if k < 3 else 1.1, 0.05, a=0.06, p=0.2))
    m.cues.append(dict(id="M8-motif-bent", start=25.8, length=2.2, bus="MX", recipe="horn motif C F Gb B, detuning"))
    for t in [23.25, 24.75, 26.25, 27.75]:
        m.add(t, timpani(hz("F1") * 1.25, 0.10, 0.3, soft=True))

    # 9 hatch: dominant pedal, the heartbeat quickens, silence 31.05-31.45
    m.add(28.50, strings(chord("C2 C3 G3 C4 E4"), 2.53, 0.12, a=1.3, r=0.02, bright=3600), cue=("M9-pedal-C", "strings C cresc"))
    m.add(29.50, choir(chord("C4 E4 G4"), 1.53, 0.07, "o", a=1.4, r=0.02), cue=("M9-choir-rise", "choir ah C"))
    ost(28.50, 31.05, pat_C, 0.075, step=0.1875, cres=(0.5, 1.4))
    m.add(29.05, riser(2.0, 0.05), cue=("M9-riser", "noise riser"))
    for t in [28.50, 29.10, 29.62, 30.05, 30.42, 30.72, 30.95]:
        m.add(t, timpani(hz("F1") * 1.25, 0.20, 0.3, soft=True))
    m.cues.append(dict(id="M9-heartbeat-quickens", start=28.5, length=2.55, bus="MX", recipe="soft timpani accelerating"))
    # hard stop at 31.05 (applied to the bus below), then the hatch
    m.add(31.50, braam(chord("F1 F2 C3 F3 Ab3"), 2.4, 0.19, r=1.0), cue=("M10-hatch-tutti", "braam Fm"))
    m.add(31.50, choir(chord("F3 Ab3 C4 F4"), 2.6, 0.08, "o", a=0.05, r=0.6))
    m.add(31.50, strings(chord("F3 Ab3 C4 F4"), 2.8, 0.07, a=0.05, r=0.4, bright=3000))
    m.add(31.50, taiko(0.38, 58, -0.2, big=True)); m.add(31.51, taiko(0.34, 64, 0.2, big=True))
    m.add(31.50, timpani(hz("F2"), 0.3, 1.5))
    for t, nm in [(31.50, "C3"), (32.25, "F3"), (33.00, "G3"), (33.75, "Ab3")]:
        m.add(t, horn(hz(nm), 0.7, 0.10, a=0.05))
        m.add(t, horn(hz(nm) / 2, 0.7, 0.07, a=0.05))
    m.cues.append(dict(id="M10-kaiju-theme", start=31.5, length=3.0, bus="MX", recipe="low horns motif C F G Ab"))
    ost(31.50, 34.125, pat_F, 0.09, cres=(1.0, 0.6))
    for k in range(4):
        m.add(32.25 + k * 0.75, taiko(0.22, p=0.4 * (-1) ** k))

    # 10 Kaiju and sides: held chord for the roar, three stabs, then the war
    m.add(34.50, strings(chord("F1 F2 C3"), 0.7, 0.04, a=0.05, r=0.3, bright=900), cue=("M11-held", "low strings"))
    m.add(35.25, strings(chord("F3 A3 C4 F4 A4"), 0.5, 0.11, a=0.008, r=0.5, bright=4200), p=-0.15, cue=("M11-stab-azure", "F major stab"))
    m.add(35.25, braam(chord("F2 C3 A3"), 0.45, 0.14, r=0.5, top=2400)); m.add(35.25, taiko(0.3, p=-0.3))
    m.add(36.00, braam(chord("Db2 Ab2 Db3 F3"), 0.7, 0.28, r=0.8, top=1500), cue=("M11-stab-crimson", "Db low stab"))
    m.add(36.00, taiko(0.36, 56, 0.3, big=True))
    m.add(37.50, braam(chord("F2 Gb2 B2 C3 E4 F4"), 0.9, 0.13, r=0.8, top=1800, sat=2.4), cue=("M11-stab-berserk", "cluster stab"))
    m.add(37.50, taiko(0.32, 60, -0.2, big=True)); m.add(37.51, taiko(0.30, 67, 0.2, big=True))
    m.add(37.60, strings(chord("Bb2 Db3 F3 Bb3"), 1.4, 0.06, a=0.3, r=0.05, bright=2400), cue=("M12-war-pad", "strings Bbm then C"))
    m.add(39.00, strings(chord("C3 E3 G3 C4"), 1.5, 0.08, a=0.3, r=0.02, bright=2800))
    ost(37.50, 39.00, pat_F, 0.075, step=0.1875)
    ost(39.00, 40.50, pat_C, 0.085, step=0.1875, cres=(1.0, 1.3))
    t = 38.25
    while t < 40.40:
        step = 0.375 if t < 39.0 else 0.1875
        g = 0.06 + 0.07 * (t - 38.25) / 2.15
        m.add(t, taiko(g, p=0.45 * math.sin(7 * t)))
        t += step
    m.cues.append(dict(id="M12-war-taiko", start=38.25, length=2.15, bus="MX", recipe="taiko eighths to sixteenths"))
    m.add(38.45, riser(2.0, 0.07), cue=("M12-riser", "noise riser"))

    # 11 hero: the theme in full
    m.add(40.50, choir(chord("F3 Ab3 C4 F4"), 2.7, 0.065, "o", a=0.06, r=0.1), cue=("M13-hero-choir", "choir ah Fm"))
    m.add(40.50, strings(chord("F2 C3 F3 Ab3 C4"), 2.7, 0.07, a=0.05, r=0.1, bright=3000), cue=("M13-hero-strings", "strings Fm"))
    m.add(40.50, taiko(0.42, 60, 0, big=True))
    for t, nm in [(40.50, "C4"), (41.25, "F4"), (42.00, "G4"), (42.75, "Ab4")]:
        m.add(t, horn(hz(nm), 0.68 if nm != "Ab4" else 0.4, 0.12, a=0.05, p=-0.1))
        m.add(t, horn(hz(nm) / 2, 0.68 if nm != "Ab4" else 0.4, 0.08, a=0.05, p=0.1))
    m.cues.append(dict(id="M13-hero-motif", start=40.5, length=3.0, bus="MX", recipe="horns motif C F G Ab"))
    ost(40.50, 43.125, pat_F, 0.09)
    for k in range(3):
        m.add(41.25 + k * 0.75, taiko(0.2, p=0.4 * (-1) ** k))

    # LOGO: the biggest moment
    m.add(43.50, braam(chord("F1 F2 C3 F3 Ab3"), 0.9, 0.46, r=1.0, top=2400, sat=1.4), cue=("M14-logo-tutti", "braam Fm"))
    m.add(43.50, strings(chord("F3 Ab3 C4 F4 G4 C5"), 0.6, 0.08, a=0.02, r=0.6, bright=3600))
    m.add(43.50, strings(chord("F2 C3 F3 Ab3 C4"), 3.0, 0.07, a=0.03, r=1.2, bright=1500))
    m.add(43.50, choir(chord("F4 Ab4 C5"), 0.35, 0.07, "o", a=0.03, r=0.25))
    m.add(43.50, taiko(0.6, 56, -0.25, big=True)); m.add(43.505, taiko(0.55, 62, 0.25, big=True))
    m.add(43.51, taiko(0.45, 70, 0.0, big=True))
    m.add(43.50, timpani(hz("F2"), 0.35, 1.8))
    m.add(46.50, glass(hz("F5"), 1.0, 0.05), cue=("M14b-last-note", "glass F5"))

    ga = np.ones(len(MX.x)); i0, i1 = S(31.40), S(40.45)
    ga[i0:i1] = db(-1.5); ga[i1:i1 + S(0.05)] = np.linspace(db(-1.5), 1, S(0.05))
    MX.x *= ga[:, None]
    # the silence before the hatch (31.05-31.45): nothing in the score
    i0, i1 = S(31.05), S(31.45)
    MX.x[i0 - S(0.03):i0] *= np.linspace(1, 0, S(0.03))[:, None]
    MX.x[i0:i1] = 0


# ------------------------------------------------------------------ effects (twelve moments)
def tuned_impact(root, amp=0.5, tail=1.8, body_lp=500, air=0.0, seed_=None):
    n = S(tail * 2.0 + 0.5)
    t = np.arange(n) / SR
    x = sub(root * 1.3, root, n / SR, 0.6, a=0.004, tau=1.0)
    x += 0.6 * sine(root * 2 * (1 + 0.1 * np.exp(-t / 0.08)), n) * np.exp(-t / 0.9)
    x += 0.35 * sine(root * 3 * (1 + 0.08 * np.exp(-t / 0.08)), n, 1.0) * np.exp(-t / 0.6)
    x += 0.18 * sine(root * 4, n, 2.0) * np.exp(-t / 0.4)
    body = filt(noise(n, seed()), "lowpass", body_lp) * np.exp(-t / 0.10) * 1.2
    x += body
    tl = np.zeros(n)
    for k, g in [(2, 1.0), (3, 0.6), (4, 0.35)]:
        tl += g * filt(noise(n, seed()), "bp", (root * k * 0.985, root * k * 1.015)) * np.exp(-t / (tail * 0.6))
    x += 2.2 * tl * env_ar(n, 0.05, 0.3, curve=1)
    if air:
        x += air * filt(noise(n, seed()), "bp", (500, 5000)) * np.exp(-t / 0.25)
    x = np.tanh(1.3 * x) / np.tanh(1.3)
    return x * amp * env_ar(n, 0.002, 0.3, curve=1)


def tonal_air(freqs, n, q=60.0, p_spread=0.6):
    """Crystalline but soft: very narrow bands of noise on tuned pitches (no struck partials, so no ding)."""
    rng = np.random.default_rng(seed())
    out = np.zeros((n, 2))
    for fr in freqs:
        bw = fr / q
        b = filt(noise(n, seed()), "bp", (fr - bw / 2, fr + bw / 2)) * math.sqrt(q) * 0.5
        out += pan(b, rng.uniform(-p_spread, p_spread))
    return out


def swell(n, range_db=34.0):
    """Exponential (dB-linear) rise over n samples from silence: a gesture the ear hears as one approach, not a
    string of hits (rate kept under about 85 dB/s)."""
    t = np.arange(n) / max(1, n - 1)
    e = 10 ** ((t - 1) * range_db / 20)
    k = min(n, S(0.03))
    e[:k] *= np.linspace(0, 1, k)
    return e


def kaiju_voice(dur, colour="roar", amp=0.4):
    """Formant-free creature voice: broad moving band of noise + slowly falling sub, irregular rasp, soft drive."""
    n = S(dur)
    t = np.arange(n) / SR
    rng = np.random.default_rng(seed())
    cfg = {"roar": dict(c0=700, c1=300, s0=62, s1=38, q=1.3, drive=1.3, hi=0.0, a=0.35),
           "azure": dict(c0=900, c1=1500, s0=55, s1=60, q=1.2, drive=1.1, hi=0.5, a=0.30),
           "crimson": dict(c0=450, c1=200, s0=50, s1=34, q=1.3, drive=1.9, hi=0.0, a=0.30),
           "berserk": dict(c0=750, c1=260, s0=58, s1=33, q=1.0, drive=2.2, hi=0.3, a=0.30)}[colour]
    fc = cfg["c1"] + (cfg["c0"] - cfg["c1"]) * np.exp(-t / (dur * 0.45))
    if colour == "azure":
        fc = cfg["c0"] + (cfg["c1"] - cfg["c0"]) * np.clip(t / dur, 0, 1)
    base = noise(n, seed(), "pink")
    band = sweep_bp(base, fc, cfg["q"]) * 2.5
    # irregular rasp: band-limited random amplitude (no steady rate, so no pitched buzz)
    rasp = filt(rng.standard_normal(n), "lowpass", 9)
    rasp = 1 + 0.15 * rasp / (np.std(rasp) + 1e-9)
    band *= np.clip(rasp, 0.1, 2.5)
    s_f = cfg["s1"] + (cfg["s0"] - cfg["s1"]) * np.exp(-t / (dur * 0.6))
    subo = sine(s_f, n) * 0.8
    x = np.tanh(cfg["drive"] * (band * 0.6 + subo * 0.7)) / np.tanh(cfg["drive"])
    x = filt(x, "lowpass", 2600)
    if cfg["hi"]:
        x += cfg["hi"] * sweep_bp(noise(n, seed()), fc * 3.2, 1.5) * 1.5
    if colour == "berserk":
        x = 0.6 * x + 0.5 * kaiju_voice(dur, "crimson", 1.0)[:, 0] * 1.2
    e = env_ar(n, 0.001, dur * 0.55, curve=2.2)
    na = S(cfg["a"])
    e[:na] *= swell(na, 26)
    y = filt(x * e * amp, "highpass", 28)
    return np.stack([y, y], 1)


def kaiju_sequence(t0=34.45, amp=0.44):
    """The Kaiju's voice as ONE continuous creature from the roar to the berserk lock, morphing colour on the three
    stabs (Azure 35.25 bright/left, Crimson 36.00 dark/right, berserk 37.50 both, rough). Formant-free: a broad moving
    band of noise over a slowly falling sub, soft drive. Trajectories are piecewise-linear in film time."""
    T = np.array([34.45, 34.75, 35.05, 35.55, 35.70, 36.25, 37.10, 37.70, 38.40, 39.40])
    keys = dict(
        amp=[0.0, 1.0, 0.9, 1.0, 0.8, 0.85, 0.6, 0.75, 0.5, 0.0],
        fc=[700, 520, 500, 1000, 800, 320, 300, 480, 380, 300],
        sub=[62, 56, 52, 58, 50, 40, 38, 46, 36, 33],
        drive=[1.3, 1.3, 1.25, 1.1, 1.4, 1.9, 1.9, 2.4, 2.4, 2.4],
        hi=[0, 0, 0, 0.25, 0.15, 0.0, 0.0, 0.12, 0.08, 0.0],
        pan=[0, 0, -0.1, -0.35, -0.1, 0.35, 0.2, 0.0, 0.0, 0.0])
    n = S(T[-1] - t0)
    tt = t0 + np.arange(n) / SR
    tr = {k: np.interp(tt, T, v) for k, v in keys.items()}
    # amplitude in dB-linear segments (smooth swells, no attacks faster than ~80 dB/s)
    a_db = np.interp(tt, T, 20 * np.log10(np.maximum(keys["amp"], 1e-3)))
    env = 10 ** (a_db / 20)
    env[: S(0.30)] = 10 ** ((-24 + 24 * np.arange(S(0.30)) / S(0.30)) / 20)
    env[-S(0.05):] *= np.linspace(1, 0, S(0.05))
    rng = np.random.default_rng(seed())
    band = sweep_bp(noise(n, seed(), "pink"), tr["fc"], 1.2, block=1024) * 2.5
    rasp = filt(rng.standard_normal(n), "lowpass", 7)
    band *= 1 + 0.12 * rasp / (np.std(rasp) + 1e-9)
    subo = sine(tr["sub"], n) * 0.8
    sig = band * 0.6 + subo * 0.7
    x = np.tanh(tr["drive"] * sig) / np.tanh(tr["drive"])
    x = filt(x, "lowpass", 2000)
    x += tr["hi"] * sweep_bp(noise(n, seed()), tr["fc"] * 3.2, 1.5, block=1024) * 1.5
    # the throat: a bowed, jittered tone in the cue's key (a creature that belongs to the score, not a moo and not a
    # blast). Pitch holds per colour and moves only at the stabs (80 ms portamento): no continuous glide, so no
    # engine "revving" and no animal call. Roar F3, Azure Ab3 + Eb4 (bright), Crimson Db3 (dark), berserk F3 + B3.
    steps = [(34.45, [hz("F3")]), (35.15, [hz("Ab3"), hz("Eb4")]), (35.90, [hz("Db3")]), (37.40, [hz("F3"), hz("B3")])]
    tone = np.zeros(n)
    for k_, (ts, fs_) in enumerate(steps):
        te = steps[k_ + 1][0] if k_ + 1 < len(steps) else T[-1]
        a0, a1 = S(max(0, ts - t0 - 0.04)), S(min(T[-1], te + 0.04) - t0)
        m_ = a1 - a0
        if m_ <= 0:
            continue
        tl = np.arange(m_) / SR
        g_ = np.ones(m_); r_ = min(m_, S(0.08))
        g_[:r_] = np.linspace(0, 1, r_); g_[-r_:] = np.linspace(1, 0, r_)
        jit = filt(rng.standard_normal(m_), "lowpass", 30); jit = 1 + 0.008 * jit / (np.std(jit) + 1e-9)
        bow = filt(rng.standard_normal(m_), "lowpass", 60); bow = 1 + 0.35 * bow / (np.std(bow) + 1e-9)
        seg_ = np.zeros(m_)
        for f_ in fs_:
            fall = 2 ** (-0.5 * tl / 12)                    # half a semitone of sag per second: weight, not a glide
            seg_ += sum(saw(f_ * fall * jit * 2 ** (d / 1200), m_, rng.uniform()) for d in (-9, 0, 11)) / 3
        seg_ = filt(seg_ * bow, "lowpass", 2600 if k_ != 1 else 3800) + 0.25 * filt(noise(m_, seed()), "bp", (1500, 5000))
        tone[a0:a1] += np.tanh(1.6 * seg_) * g_
    x = x * 0.55 + tone * 0.6 * np.interp(tt, T, [1, 1, 1, 1, 1, 1, 1, 1, 1, 1])
    x = filt(x * env * amp, "highpass", 28)
    a = (tr["pan"] + 1) * np.pi / 4
    return np.stack([x * np.cos(a), x * np.sin(a)], 1) * math.sqrt(2)


def effects():
    f = FX
    # E1 air bed
    n = S(4.2)
    t = np.arange(n) / SR
    fca = 1600 + 500 * np.sin(2 * np.pi * 0.11 * t)
    air = np.stack([sweep_bp(noise(n, seed(), "pink"), fca, 0.6), sweep_bp(noise(n, seed(), "pink"), fca * 1.1, 0.6)], 1) * 0.35
    low = filt(noise(n, seed(), "brown"), "bp", (60, 400)) * (1 + 0.4 * np.sin(2 * np.pi * 0.17 * t))
    e = env_ar(n, 1.6, 0.6, curve=1.5)
    e[S(3.75):] *= np.linspace(1, 0.25, n - S(3.75))
    f.add(0.40, (air + pan(low, 0) * 0.6) * e[:, None] * 0.05, cue=("E1-air-bed", "moving air band around 1.6 kHz + low wind"))

    # E2 sky crack: tearing whoosh into a tuned low impact with a tuned tail
    n = S(0.60)
    t = np.arange(n) / SR
    fc = 300 * (3500 / 300) ** (t / t[-1]) ** 1.4
    tear = filt(np.random.default_rng(seed()).standard_normal(n), "lowpass", 25)
    tear = np.clip(1 + 0.25 * tear / np.std(tear), 0, 3)
    whs = np.stack([sweep_bp(noise(n, seed()), fc, 1.6) * tear * swell(n, 48) for _ in range(2)], 1) * 0.5
    imp = tuned_impact(hz("F1"), 0.42, tail=2.2, body_lp=420, air=0.25)
    crack = np.zeros((n + len(imp), 2))
    crack[:n] += whs
    crack[n:] += pan(imp, 0)
    f.add(4.50 - 0.60, crack, cue=("E2-sky-crack", "tear whoosh 300-3.5k (dB-linear swell) -> F1 impact, tail F2 C3 F3"))

    # E3 land rises, settles 7.95
    n = S(3.6)
    t = np.arange(n) / SR
    rumble = filt(noise(n, seed(), "brown"), "bp", (40, 260)) * 1.4
    sg = sine(hz("C1") * (hz("F1") / hz("C1")) ** np.clip((t - 0.1) / 1.7, 0, 1), n) * 0.7
    e = np.clip(t / 1.7, 0, 1) ** 1.5 * np.where(t > 1.75, np.exp(-(t - 1.75) / 0.3), 1)
    land = (rumble + sg) * e
    settle = tuned_impact(hz("F1"), 0.35, tail=0.9, body_lp=300)
    x = pan(land * 0.33, 0)
    k = S(1.75)
    m_ = min(len(settle), n - k)
    x[k:k + m_] += pan(settle[:m_] * env_ar(m_, 0.001, 0.4, curve=1), 0)
    f.add(6.20, x, cue=("E3-land-rises", "brown rumble 40-260 + sub C1->F1, settle F1"))

    # E4 comet: Doppler tone far away, late boom tuned F
    n = S(1.9)
    t = np.arange(n) / SR
    tt = t - 1.0
    v, d0, c = 150.0, 60.0, 343.0
    dist = np.sqrt(d0 ** 2 + (v * tt) ** 2)
    vr = v * v * tt / dist
    fd = hz("C6") * c / (c + vr)
    tone = sweep_bp(noise(n, seed()), fd * 0.9, 1.0) * 1.3     # air only: a pitched falling core read as a moo
    g = (d0 / dist) ** 1.3
    tone = filt(tone * g, "lowpass", 4000)
    pp = np.clip(tt / 0.9, -1, 1) * 0.8
    st_ = np.stack([tone * np.cos((pp + 1) * np.pi / 4), tone * np.sin((pp + 1) * np.pi / 4)], 1)
    ce = env_ar(n, 0.001, 0.5); ce[:S(1.0)] *= swell(S(1.0), 36)
    f.add(9.70, st_ * ce[:, None] * 0.10, cue=("E4-comet", "Doppler-shifted air band around C6 (no pitched core), pan L->R"))
    boom = filt(tuned_impact(hz("F1"), 0.5, tail=1.8, body_lp=300), "lowpass", 650)
    f.add(10.85, pan(boom, 0.2), cue=("E4b-far-boom", "F1 impact low-passed 650 Hz, mostly hall"))

    # E5 egg shimmer (soft glass partials on F Ab C), blooms 17.05-17.25
    n = S(5.3)
    t = np.arange(n) / SR
    rng = np.random.default_rng(seed())
    sh = np.zeros((n, 2))
    for nm in ["F5", "Ab5", "C6", "F6", "C7"]:
        lfo = 0.55 + 0.45 * np.sin(2 * np.pi * rng.uniform(0.2, 0.6) * t + rng.uniform(0, 6))
        sh += tonal_air([hz(nm)], n, 80) * lfo[:, None] * (0.6 if nm == "C7" else 1.0)
    sh += 0.15 * np.stack([filt(noise(n, seed()), "bp", (5000, 11000)), filt(noise(n, seed()), "bp", (5000, 11000))], 1)
    e = env_ar(n, 1.5, 0.25, curve=1)
    e *= 1 + 1.6 * np.clip((t - 4.9) / 0.35, 0, 1) ** 2
    f.add(12.00, sh * e[:, None] * 0.010, cue=("E5-egg-shimmer", "tuned air bands F5 Ab5 C6 F6 C7 (narrow noise, no struck partials), bloom 17.05"))

    # E6 hill wind with two banner gusts
    n = S(6.2)
    t = np.arange(n) / SR
    fcw = 1100 + 400 * np.sin(2 * np.pi * 0.07 * t)
    w = [sweep_bp(noise(n, seed(), "pink"), fcw * (1 + 0.1 * k), 0.5) for k in range(2)]
    gust = np.zeros(n)
    gl, gr = np.zeros(n), np.zeros(n)
    for tg, side in [(0.65, "l"), (1.00, "r")]:
        b = np.exp(-0.5 * ((t - tg) / 0.5) ** 2)
        (gl if side == "l" else gr)[:] += b
    gust_noise = [sweep_bp(noise(n, seed(), "pink"), 1200 + 600 * (gl + gr), 0.8) for _ in range(2)]
    base_e = env_ar(n, 0.6, 0.8, curve=1.2) * 0.55
    wind = np.stack([w[0] * base_e + gust_noise[0] * gl * 0.9, w[1] * base_e + gust_noise[1] * gr * 0.9], 1)
    wind = filt(wind, "bp", (500, 3000), 4)
    wind[: S(1.0)] *= swell(S(1.0), 40)[:, None]
    f.add(17.25, wind * 0.06, cue=("E6-hill-wind", "wind band 500-3000 moving, gusts 17.90 L and 18.25 R"))

    # E7 clash: wind-up whoosh into one deep tonal impact on C
    n = S(0.50)
    t = np.arange(n) / SR
    wu = sweep_bp(noise(n, seed()), 500 * (2600 / 500) ** (t / t[-1]), 1.5) * swell(n, 40)
    imp = tuned_impact(hz("C2"), 0.45, tail=1.0, body_lp=600, air=0.2)
    x = np.zeros((n + len(imp), 2))
    x[:n] += pan(wu * 0.35, 0)
    x[n:] += pan(imp, 0)
    f.add(22.50 - 0.50, x, cue=("E7-clash", "whoosh 500-2.6k -> C2 impact, no metal"))

    # E8 feed motes: soft notes stepping up through F major plus faint sparkle air
    n = S(1.9)
    mot = np.zeros((n, 2))
    for k, nm in enumerate(["F5", "A5", "C6", "F6", "A6"]):
        m_ = S(0.9)
        g = tonal_air([hz(nm)], m_, 70, 0.1) * env_ar(m_, 0.25, 0.6, curve=1.5)[:, None] * 0.5
        g = g * np.array([1 - 0.2 * k, 0.6 + 0.2 * k])
        i = S(0.3 * k)
        mot[i:i + len(g)] += g[: n - i]
    t = np.arange(n) / SR
    mot += 0.05 * np.stack([filt(noise(n, seed()), "bp", (6000, 12000))] * 2, 1) * env_ar(n, 0.4, 0.8)[:, None]
    f.add(23.80, mot * 0.10, cue=("E8-feed-motes", "tuned air swells F5 A5 C6 F6 A6 stepping up, no glide"))

    # E9 corrupt: reversed dark swell into a low beating drone
    n = S(0.5)
    t = np.arange(n) / SR
    dk = filt(noise(n, seed(), "brown"), "lowpass", 700) * np.exp(-t / 0.15)
    rswell = dk[::-1]
    nd = S(1.8)
    td = np.arange(nd) / SR
    drone = (filt(noise(nd, seed()), "bp", (hz("F2") * 0.98, hz("F2") * 1.02)) +
             filt(noise(nd, seed()), "bp", (hz("Gb2") * 0.98, hz("Gb2") * 1.02))) * 4.0
    drone += 0.5 * sine(hz("F1"), nd) * (1 + 0.3 * np.sin(2 * np.pi * 3.1 * td))
    drone = np.tanh(1.5 * drone) * np.exp(-td / 0.8) * env_ar(nd, 0.01, 0.6, curve=1)
    x = np.concatenate([rswell * 0.8, drone])
    f.add(25.80 - 0.5, pan(x * 0.30, 0.15), cue=("E9-corrupt", "reversed dark swell -> F2/Gb2 noise drone + F1"))

    # E10 hatch: four stone-like cracks, breath in the silence, the hatch
    for k, (tc, fcen) in enumerate([(29.25, 700), (30.00, 950), (30.75, 1250), (31.13, 1600)]):
        n = S(0.35)
        t = np.arange(n) / SR
        cr = filt(noise(n, seed()), "bp", (fcen * 0.5, fcen * 2.2)) * np.exp(-t / (0.028 + 0.006 * k))
        cr += 0.6 * sine(110 + 15 * k, n) * np.exp(-t / 0.035)
        cr *= env_ar(n, 0.0015, 0.05, curve=1)
        f.add(tc, pan(cr * (0.16 + 0.04 * k), -0.1 + 0.07 * k))
    f.cues.append(dict(id="E10a-shell-cracks", start=29.25, length=2.2, bus="FX", recipe="band noise bursts 0.7-1.6k, no ring"))
    n = S(0.40)
    t = np.arange(n) / SR
    br = sweep_bp(noise(n, seed(), "pink"), 500 + 1800 * (t / t[-1]), 1.0) * swell(n, 26)
    f.add(31.07, pan(br * 0.05, 0), cue=("E10b-breath", "soft rising air in the silence"))
    hb = tuned_impact(hz("F1"), 0.44, tail=2.0, body_lp=500, air=0.5)
    n = len(hb)
    t = np.arange(n) / SR
    shb = tonal_air([hz("F6"), hz("C7"), hz("Ab6")], n, 60) * (np.exp(-t / 1.0) * env_ar(n, 0.15, 0.3, curve=1))[:, None]
    f.add(31.50, pan(hb, 0) + 0.025 * shb, cue=("E10c-hatch", "F1 impact + air bloom + tuned air F6 C7 Ab6"))

    # E11 the Kaiju's voice: roar, Azure, Crimson, berserk
    f.add(34.45, kaiju_sequence(), cue=("E11-kaiju-voice", "one continuous voice: formant-free noise band + sub + bowed tonal throat in F; roar 34.5, Azure 35.25 (Ab3+Eb4), Crimson 36.0 (Db3), berserk 37.5 (F3+B3)"))

    # E12 logo: air pass in, the biggest landing
    n = S(0.60)
    t = np.arange(n) / SR
    ap2 = [sweep_bp(noise(n, seed()), 400 * (2400 / 400) ** (t / t[-1]), 1.0) * swell(n, 50) for _ in range(2)]
    land = tuned_impact(hz("F1"), 1.1, tail=2.6, body_lp=600, air=0.5)
    x = np.zeros((n + len(land), 2))
    x[:n] += np.stack(ap2, 1) * 0.4
    x[n:] += pan(land, 0)
    f.add(43.50 - 0.60, x, cue=("E12-logo", "air pass 400-2.4k -> F1 impact, tail F2 C3 F3, the biggest"))


# ------------------------------------------------------------------ narration
LINES = [("story-card-1", 5.05), ("story-card-3", 12.55), ("story-card-2", 18.28), ("story-card-4", 23.67),
         ("story-card-5", 40.80), ("story-title", 44.00)]


def load(path):
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", path, "-f", "f32le", "-ac", "1", "-ar", str(SR), "-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.float32).astype(np.float64)


def peq(x, f0, gain_db, q=1.0, kind="peak"):
    A = 10 ** (gain_db / 40); w = 2 * np.pi * f0 / SR; al = np.sin(w) / (2 * q); cw = np.cos(w)
    if kind == "peak":
        b = [1 + al * A, -2 * cw, 1 - al * A]; a = [1 + al / A, -2 * cw, 1 - al / A]
    else:  # high shelf
        sA = np.sqrt(A)
        b = [A * ((A + 1) + (A - 1) * cw + 2 * sA * al), -2 * A * ((A - 1) + (A + 1) * cw), A * ((A + 1) + (A - 1) * cw - 2 * sA * al)]
        a = [(A + 1) - (A - 1) * cw + 2 * sA * al, 2 * ((A - 1) - (A + 1) * cw), (A + 1) - (A - 1) * cw - 2 * sA * al]
    return ss.lfilter(np.array(b) / a[0], np.array(a) / a[0], x)


def follower(x, att, rel):
    """Peak envelope (linear) with attack/release in seconds."""
    a1, r1 = math.exp(-1 / (att * SR)), math.exp(-1 / (rel * SR))
    ax = np.abs(x)
    # vectorised approximation: attack via max-filter, release via one-pole IIR
    from scipy.ndimage import maximum_filter1d
    pk = maximum_filter1d(ax, size=max(1, int(att * SR)), origin=0)
    return ss.lfilter([1 - r1], [1, -r1], pk)


def compress(x, thr_db, ratio, att=0.005, rel=0.08, knee=6.0):
    e = 20 * np.log10(follower(x, att, rel) + 1e-9)
    over = e - thr_db
    gr = np.where(over <= -knee / 2, 0, np.where(over >= knee / 2, over * (1 - 1 / ratio),
                                                  (1 - 1 / ratio) * (over + knee / 2) ** 2 / (2 * knee)))
    g = 10 ** (-gr / 20)
    return x * g, float(gr.max())


def voice_chain(x):
    x = filt(x, "highpass", 90, 2)
    x = peq(x, 280, -2.5, 0.9)
    x = peq(x, 1100, -1.5, 1.2)
    x = peq(x, 2300, -3.0, 0.8)          # the synthetic voice's nasal band
    x = peq(x, 5500, 3.0, 0.7, kind="shelf")
    # de-esser: compress only the 5.5-9 kHz band
    hi = filt(x, "bp", (5500, 9000), 2)
    hi_c, _ = compress(hi, -34, 4.0, 0.002, 0.06, 4)
    x = x - hi + hi_c
    x, gr = compress(x, -18, 2.0, 0.004, 0.09, 6)
    return x, gr


def narration(voice_dir, info):
    for k, (lid, cue) in enumerate(LINES):
        x = load(os.path.join(voice_dir, f"{lid}.ogg"))
        e, h = EAR.env_db(x.astype(np.float32), SR, 0.005)
        lead = np.argmax(e > e.max() - 40) * h / SR
        y, gr = voice_chain(x)
        if lid == "story-title":
            y = peq(y, 2200, -5.0, 0.7)      # line fix: "Mass" reads as a quack to the tagger without it
        y = y[max(0, S(lead) - S(0.010)):]          # speech now starts 10 ms into the clip
        y = fade(y, 0.008, 0.03)
        start = cue + 1 / 30 - 0.010                 # speech heard one frame after the cue
        DX.add(start, np.stack([y, y], 1), cue=(f"DX{k + 1}-{lid}", f"{lid}.ogg, HP90 EQ de-ess 3:1"))
        info.append(dict(line=lid, cue=cue, original_lead_s=round(lead, 3), speech_start=round(cue + 1 / 30, 3),
                         speech_end=round(start + len(y) / SR, 3), comp_gr_db=round(gr, 1)))


# ------------------------------------------------------------------ room, mix, master
def hall_ir(seconds=3.2, seed_=77):
    n = S(seconds)
    t = np.arange(n) / SR
    rng = np.random.default_rng(seed_)
    ir = np.zeros((n, 2))
    pre = S(0.018)
    for ch in range(2):
        w = rng.standard_normal(n)
        lo, mid, hi = filt(w, "lowpass", 300), filt(w, "bp", (300, 3000)), filt(w, "highpass", 3000)
        tail = lo * np.exp(-6.91 * t / 2.6) + mid * np.exp(-6.91 * t / 2.2) + hi * np.exp(-6.91 * t / 1.1)
        tail[:pre] = 0
        tail[pre:pre + S(0.04)] *= np.linspace(0, 1, S(0.04)) ** 2
        ir[:, ch] = tail * 0.35
    for ms, gl, gr in [(7, .55, .32), (11, .30, .52), (17, .42, .26), (23, .22, .36), (31, .26, .18), (41, .16, .21), (53, .12, .10)]:
        ir[S(ms / 1000), 0] += gl * 20; ir[S(ms / 1000), 1] += gr * 20
    ir = filt(ir, "lowpass", 7000)
    ir /= np.sqrt((ir ** 2).sum() / 2)
    return ir


def reverb(x, ir, wet):
    m = x.mean(1)
    out = np.stack([ss.fftconvolve(m, ir[:, 0])[: len(x)], ss.fftconvolve(m, ir[:, 1])[: len(x)]], 1)
    return x + wet * out


def duck_gain(key, depth_db, att=0.04, rel=0.25, thr_db=-45):
    e = 20 * np.log10(follower(key, att, rel) + 1e-9)
    amt = np.clip((e - thr_db) / 12, 0, 1)
    g_db = -depth_db * amt
    # smooth with the same release so the bed comes back over 250 ms
    r1 = math.exp(-1 / (0.06 * SR))
    g_db = ss.lfilter([1 - r1], [1, -r1], g_db)
    return 10 ** (g_db / 20)


def tp_limit(x, ceiling_db=-1.6, look=0.002, rel=0.08):
    from scipy.ndimage import minimum_filter1d
    c = 10 ** (ceiling_db / 20)
    up = ss.resample_poly(x, 4, 1, axis=0)
    pk = np.abs(up).max(1).reshape(-1, 4).max(1)[: len(x)]
    need = np.minimum(1.0, c / (pk + 1e-12))
    from scipy.ndimage import uniform_filter1d
    L_ = S(look)
    g = minimum_filter1d(need, size=2 * L_ + 1)          # every sample within +/- look of a peak is reduced
    g = np.minimum(uniform_filter1d(g, size=L_ + 1), g if False else 1.0)  # ramped attack, still <= need at the peak
    r1 = math.exp(-1 / (rel * SR))
    inv = 1 - g
    sm = ss.lfilter([1 - r1], [1, -r1], inv)             # slow release
    g2 = 1 - np.maximum(inv, sm)
    return x * g2[:, None], float(20 * np.log10(g2.min()))


def main():
    out = sys.argv[1]
    voice_dir = os.path.expanduser("~/cm-wt-story-3/Assets/_Game/Audio/Voice/Resources/CMVoice")
    if "--voice-dir" in sys.argv:
        voice_dir = sys.argv[sys.argv.index("--voice-dir") + 1]
    os.makedirs(f"{out}/stems", exist_ok=True); os.makedirs(f"{out}/checks", exist_ok=True)
    compose(); effects()
    info = []
    narration(voice_dir, info)
    ir = hall_ir()
    mx = reverb(MX.x, ir, 0.32)[:N]
    # the silence before the hatch is total: the hall tail of the score is cut too (31.05-31.45)
    gate = np.ones(N); i0, i1 = S(31.05), S(31.45)
    gate[i0 - S(0.04):i0] = np.linspace(1, 0, S(0.04)); gate[i0:i1] = 0
    mx *= gate[:, None]
    # music bus EQ: clear the sub mud, lift presence and air (the synth orchestra is dark by nature)
    mx = filt(mx, "highpass", 32, 2)
    for ch in range(2):
        mx[:, ch] = peq(peq(peq(peq(peq(mx[:, ch], 70, -3.5, 0.7), 160, -3.0, 0.9), 2000, 3.0, 0.8), 3500, 4.0, 0.7,
                            kind="shelf"), 9000, 3.0, 0.7, kind="shelf")
    fx = reverb(FX.x, ir, 0.24)[:N]
    dx = reverb(DX.x, ir, 0.06)[:N]
    # balance: music carries, effects sit at the music's level on their moments, voice set below
    mx *= db(-0.0); fx *= db(float(os.environ.get("FX_TRIM", "0")))
    key = DX.x[:N].mean(1)
    gd = duck_gain(key, float(os.environ.get("DUCK_DB", "7")))
    mx *= gd[:, None]; fx *= gd[:, None]
    # and carve the bed's speech band (700 Hz-4 kHz) a further 5 dB while a line is spoken, so the words sit in a
    # hole instead of fighting the chord
    gm = duck_gain(key, float(os.environ.get("CARVE_DB", "7")))
    for b_ in (mx, fx):
        mid = filt(b_, "bp", (300, 5000), 2)
        b_ -= mid * (1 - gm)[:, None]
    # voice level, line by line (a mixer's ride): each line sits VO_OVER_BED LU over the bed while speaking; where the
    # voice would need more than +4 dB, the bed dips the rest (ramps 120 ms in, 250 ms out)
    target = float(os.environ.get("VO_OVER_BED", "8.0"))

    def seg(env, t0, t1, g, rin=0.05, rout=0.05):
        i0, i1 = max(0, S(t0)), min(N, S(t1))
        e = np.ones(i1 - i0) * g
        ri, ro = min(len(e), S(rin)), min(len(e), S(rout))
        e[:ri] = 1 + (g - 1) * np.linspace(0, 1, ri)
        e[len(e) - ro:] = g + (1 - g) * np.linspace(0, 1, ro)
        env[i0:i1] *= e

    for it in range(3):
        Lb, Ld = EAR.loudness_curves(mx + fx, SR), EAR.loudness_curves(dx, SR)
        nM = min(len(Lb["M"]), len(Ld["M"]))
        tc = Ld["tM"][:nM] - 0.2
        vg, bg = np.ones(N), np.ones(N)
        for ln in info:
            s0, s1 = ln["speech_start"], ln["speech_end"]
            msk = (tc >= s0) & (tc <= s1) & (Ld["M"][:nM] > Ld["M"][:nM].max() - 25)
            d = float(np.median(Ld["M"][:nM][msk] - Lb["M"][:nM][msk]))
            need = target - d
            vgain = float(np.clip(need, -4, 4))
            short = need - vgain
            seg(vg, s0 - 0.10, s1 + 0.5, db(vgain))
            if short > 0.2:
                seg(bg, s0 - 0.15, s1 + 0.05, db(-short), 0.12, 0.25)
            ln.setdefault("rides", []).append(dict(vo_over_bed=round(d, 1), voice_db=round(vgain, 1),
                                                   bed_dip_db=round(max(0, short), 1)))
        dx *= vg[:, None]; mx *= bg[:, None]; fx *= bg[:, None]
    diff = 0.0
    # tails: fade the last 0.7 s with the engine dissolve
    fo = np.ones(N); fo[N - S(0.75):] = np.linspace(1, 0, S(0.75)) ** 1.5
    for x_ in (mx, fx, dx):
        x_ *= fo[:, None]
    mix = mx + fx + dx
    # glue + loudness + true-peak limiter, iterate to -16 LUFS
    gain = 1.0
    for it in range(6):
        y, lim_gr = tp_limit(mix * gain, -1.7)
        I = EAR.loudness_curves(y, SR)["I"]
        gain *= db(-16.0 - I)
    y, lim_gr = tp_limit(mix * gain, -1.7)
    M_ = EAR.loudness_curves(y, SR)
    tp = EAR.true_peak(y)
    import soundfile as sf
    sf.write(f"{out}/final-mix.wav", y.astype(np.float32), SR, subtype="PCM_24")
    for nm, s_ in [("music", mx), ("effects", fx), ("narration", dx)]:
        sf.write(f"{out}/stems/{nm}.wav", (s_ * gain).astype(np.float32), SR, subtype="PCM_24")
    mono_ = y.mean(1)
    sf.write(f"{out}/checks/mono-fold.wav", np.stack([mono_, mono_], 1).astype(np.float32), SR, subtype="PCM_24")
    lap = filt(mono_, "highpass", 180, 4)
    sf.write(f"{out}/checks/laptop-hp180.wav", np.stack([lap, lap], 1).astype(np.float32), SR, subtype="PCM_24")
    cues = MX.cues + FX.cues + DX.cues
    json.dump(dict(film_seconds=L, sample_rate=SR, master_gain_db=round(20 * math.log10(gain), 2),
                   limiter_max_gr_db=round(lim_gr, 2), narration=info, vo_trim_db=round(target - diff, 2),
                   cues=sorted(cues, key=lambda c: c["start"])), open(f"{out}/cues.json", "w"), indent=1)
    print(json.dumps(dict(I=round(M_["I"], 2), LRA=round(M_["LRA"], 2), TP=round(tp, 2), lim_gr=round(lim_gr, 2),
                          clipped=int((np.abs(y) >= 0.999).sum()), gain_db=round(20 * math.log10(gain), 2),
                          vo_trim=round(target - diff, 2), fx_cues=len([c for c in FX.cues]),
                          narration=info)))


if __name__ == "__main__":
    main()
