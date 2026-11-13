#!/usr/bin/env python3
"""Story intro 3: the film's soundtrack, mixed offline (plan intro-film-1, section 3).

One music-led mix replaces the 40 live one-shots. Every source is CC0 (repo music, Kenney Impact Sounds and RPG Audio
originals already on disk) or synthesised here with its own seed. Buses DX, MX, FX, BG -> Master (slow comp, true-peak
limiter, two-pass loudnorm to -16 LUFS). Never plays anything: writes WAV files and prints measurements only.

  python3 tools/film/build_soundtrack.py OUT_DIR

Writes OUT_DIR/intro-mix.wav (48 kHz stereo float), stem-{dx,mx,fx,bg}.wav, cues/<id>.wav (each cue alone, pre-bus) and
cues.json (the cue table actually rendered: id, start, length, bus, sources).
"""
import json, os, subprocess, sys
import numpy as np
import scipy.signal as ss

SR = 48000
FPS = 30
FRAMES = 1420                      # 47.333 s: film plus the hand-off dissolve
L = FRAMES / FPS
N = int(round(L * SR))
REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
MUSIC = os.path.join(REPO, 'Assets/_Game/Audio/Music/Resources/CMMusic')
VOICE = os.path.join(REPO, 'Assets/_Game/Audio/Voice/Resources/CMVoice')
SFX = os.path.join(REPO, 'Assets/_Game/Audio/Resources/CMSfx')
KEN = os.path.expanduser('~/critical-mass-assets/downloads/kenney-audio/x')
IMPACT = os.path.join(KEN, 'impact-sounds/Audio')
RPG = os.path.join(KEN, 'rpg-audio/Audio')

# Music grid, measured (librosa beat fit over the whole file, residual median 7 ms): music-final.ogg is 160.0 BPM in
# eighths = 80 BPM half-time, bar downbeats at 0.0707 + 3k s. No time stretch is needed (the plan's 0.9907 stretch
# would have moved it OFF 80 BPM: the half-time reading of 161.5 was a quantised estimate).
MF_BAR0 = 0.0707
TONIC = 87.31                      # F2; chroma of music-final leans F (F 0.72, E, G, C)
TRIAD = [698.46, 1046.5, 880.0]    # F5 C6 A5 (the egg motif)


# ---------------------------------------------------------------- io + dsp helpers
def load(path, semis=0.0):
    """Decode to float32 stereo 48 kHz. semis: pitch+speed together (asetrate+aresample, as Unity pitch does)."""
    af = []
    if semis:
        af.append(f'asetrate={SR * 2 ** (semis / 12):.3f},aresample={SR}')
    cmd = ['ffmpeg', '-v', 'error', '-i', path] + (['-af', ','.join(af)] if af else []) + \
          ['-ar', str(SR), '-ac', '2', '-f', 'f32le', '-']
    raw = subprocess.run(cmd, capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.float32).reshape(-1, 2).copy()


def save(path, x):
    x = np.asarray(x, np.float32)
    if x.ndim == 1:
        x = np.stack([x, x], 1)
    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-f', 'f32le', '-ar', str(SR), '-ac', '2', '-i', '-',
                    '-c:a', 'pcm_f32le', path], input=x.tobytes(), check=True)


def st(x):
    return np.stack([x, x], 1) if x.ndim == 1 else x


def mono(x):
    return x.mean(1) if x.ndim == 2 else x


def sec(s):
    return int(round(s * SR))


def fade(x, fin=0.005, fout=0.05, shape='sine'):
    x = st(x).copy()
    n = len(x)
    a, b = min(n, max(1, sec(fin))), min(n, max(1, sec(fout)))
    a = min(n, max(a, sec(0.006)))
    ra = np.linspace(0, 1, a) ** 3          # cubic attack: the first ms sits ~40 dB under the clip, no click
    rb = np.linspace(1, 0, b)
    if shape == 'sine':
        rb = np.sin(rb * np.pi / 2)
    x[:a] *= ra[:, None]
    x[n - b:] *= rb[:, None]
    return x


def sos(kind, f, order=2):
    if kind == 'bp':
        return ss.butter(order, [f[0] / (SR / 2), min(0.99, f[1] / (SR / 2))], 'bandpass', output='sos')
    return ss.butter(order, min(0.99, f / (SR / 2)), kind, output='sos')


def filt(x, kind, f, order=2):
    return ss.sosfilt(sos(kind, f, order), x, axis=0)


def sweep_filter(x, f0, f1, kind='lp', q=1.0, block=256, curve='exp'):
    """Time-varying biquad (block-wise, state carried) from f0 to f1 over the clip."""
    x = mono(x)
    out = np.zeros_like(x)
    n = len(x)
    zi = np.zeros((1, 2))
    for i in range(0, n, block):
        t = i / max(1, n - 1)
        f = f0 * (f1 / f0) ** t if curve == 'exp' else f0 + (f1 - f0) * t
        if kind == 'bp':
            bw = f / q
            s_ = ss.butter(1, [max(20, f - bw / 2) / (SR / 2), min(0.99, (f + bw / 2) / (SR / 2))], 'bandpass',
                           output='sos')
        else:
            s_ = ss.butter(2, min(0.99, f / (SR / 2)), kind, output='sos')
        seg, zi = ss.sosfilt(s_, x[i:i + block], zi=zi)
        out[i:i + block] = seg
    return out


def noise(sec_, seed, color='white'):
    r = np.random.default_rng(seed)
    w = r.standard_normal(sec(sec_))
    if color == 'pink':
        b, a = [0.049922035, -0.095993537, 0.050612699, -0.004408786], [1, -2.494956002, 2.017265875, -0.522189400]
        w = ss.lfilter(b, a, w)
    elif color == 'brown':
        w = np.cumsum(w)
        w = ss.sosfilt(sos('hp', 15), w)
    return w / (np.abs(w).max() + 1e-9)


def lfo_env(sec_, seed, rate=0.1, depth=0.5):
    r = np.random.default_rng(seed)
    n = sec(sec_)
    k = max(4, int(sec_ * rate * 4) + 4)
    pts = r.uniform(1 - depth, 1, k)
    return np.interp(np.linspace(0, k - 1, n), np.arange(k), pts) ** 1.5


def stereo_noise(sec_, seed, color):
    return np.stack([noise(sec_, seed, color), noise(sec_, seed + 7919, color)], 1)


def ir(seconds, seed, lp=4000, pre=0.02):
    r = np.random.default_rng(seed)
    n = sec(seconds)
    t = np.arange(n) / SR
    env = np.exp(-6.9 * t / seconds)
    h = np.stack([r.standard_normal(n), r.standard_normal(n)], 1) * env[:, None]
    h = filt(h, 'lp', lp)
    h = np.concatenate([np.zeros((sec(pre), 2)), h])
    return h / np.sqrt((h ** 2).sum(0).mean())


def reverb(x, seconds, seed, wet=0.35, lp=4000):
    x = st(x)
    h = ir(seconds, seed, lp)
    y = np.stack([ss.fftconvolve(x[:, c], h[:, c]) for c in range(2)], 1)
    y *= np.sqrt((x ** 2).mean() / ((y ** 2).mean() + 1e-12)) * 0.7
    out = np.zeros_like(y)
    out[:len(x)] += x * (1 - wet)
    out += y * wet
    return out


def pan(x, p):
    x = st(x)
    a = (p + 1) * np.pi / 4
    return x * np.array([np.cos(a), np.sin(a)]) * np.sqrt(2)


def pan_sweep(x, p0, p1):
    x = st(x)
    p = np.linspace(p0, p1, len(x))
    a = (p + 1) * np.pi / 4
    return x * np.stack([np.cos(a), np.sin(a)], 1) * np.sqrt(2)


def peak_to(x, db):
    x = st(x)
    return x * (10 ** (db / 20) / (np.abs(x).max() + 1e-9))


def rms_to(x, db):
    x = st(x)
    return x * (10 ** (db / 20) / (np.sqrt((x ** 2).mean()) + 1e-9))


def sat(x, amt=0.15, drive=2.5):
    return x * (1 - amt) + np.tanh(x * drive) / np.tanh(drive) * amt


def stretch(x, factor):
    """Granular-free time stretch by resampling + pitch restore is not needed: slow = lower (asetrate style)."""
    x = st(x)
    n = int(len(x) * factor)
    return np.stack([np.interp(np.linspace(0, len(x) - 1, n), np.arange(len(x)), x[:, c]) for c in range(2)], 1)


def seg(x, t0, t1):
    return x[sec(t0):sec(t1)].copy()


def tone(sec_, f, phase=0.0):
    t = np.arange(sec(sec_)) / SR
    return np.sin(2 * np.pi * f * t + phase)


def pitched_sine(sec_, f0, f1, tau=0.15):
    t = np.arange(sec(sec_)) / SR
    f = f1 + (f0 - f1) * np.exp(-t / tau)
    return np.sin(2 * np.pi * np.cumsum(f) / SR)


def expenv(sec_, decay, attack=0.005):
    t = np.arange(sec(sec_)) / SR
    return np.minimum(1, t / max(attack, 1e-4)) * np.exp(-t / decay)


# ---------------------------------------------------------------- synth recipes (each with its own seed)
def calm_air(seconds, seed):
    x = stereo_noise(seconds, seed, 'pink')
    x = filt(x, 'bp', (300, 4000))
    x = filt(x, 'hp', 200)
    return x * lfo_env(seconds, seed + 1, 0.08, 0.6)[:, None]


def storm_bed(seconds, seed, thunder=True):
    x = stereo_noise(seconds, seed, 'brown')
    x = filt(x, 'lp', 600) * lfo_env(seconds, seed + 2, 0.12, 0.5)[:, None]
    x = x / (np.abs(x).max() + 1e-9)
    if thunder:
        r = np.random.default_rng(seed + 3)
        t = r.uniform(0.6, 2.0)
        while t < seconds - 1.0:
            th = rolling_thunder(2.6, seed + int(t * 100)) * 0.32
            i = sec(t)
            x[i:i + len(th)] += th[:len(x) - i]
            t += r.uniform(3.0, 5.0)
    return x


def rolling_thunder(seconds, seed):
    r = np.random.default_rng(seed)
    x = stereo_noise(seconds, seed, 'brown')
    x = filt(x, 'lp', 280)
    n = len(x)
    env = np.zeros(n)
    for _ in range(int(seconds * 4)):
        c = r.uniform(0, seconds * 0.7)
        w = r.uniform(0.15, 0.6)
        tt = np.arange(n) / SR
        env += r.uniform(0.3, 1) * np.exp(-((tt - c) / w) ** 2)
    env *= np.exp(-np.arange(n) / SR / (seconds * 0.6))
    x = x * env[:, None]
    return fade(x / (np.abs(x).max() + 1e-9), 0.08, 0.6)


def thunder_clap(seed):
    n0 = noise(1.8, seed, 'white')
    body = sweep_filter(n0, 3000, 400, 'lp') * expenv(1.8, 0.45, 0.002)
    body = sat(body / (np.abs(body).max() + 1e-9), 0.15)
    return st(body)


def crackle(seed, seconds=0.16):
    r = np.random.default_rng(seed)
    n = sec(seconds)
    x = np.zeros(n)
    idx = r.choice(n, 60, replace=False)
    x[idx] = r.uniform(-1, 1, 60)
    x = filt(x, 'bp', (2000, 6000))
    x *= np.exp(-np.arange(n) / SR / 0.06)
    return pan(st(x / (np.abs(x).max() + 1e-9)), 0.1)


def sub_drop(seconds, f0, f1):
    x = pitched_sine(seconds, f0, f1, seconds / 3) * expenv(seconds, seconds / 2.5, 0.01)
    return st(x)


def granular(seconds, seed, grains=40, band=(300, 3000)):
    r = np.random.default_rng(seed)
    n = sec(seconds)
    x = np.zeros((n, 2))
    src = filt(stereo_noise(1.0, seed + 11, 'white'), 'bp', band)
    for _ in range(grains):
        g = sec(r.uniform(0.005, 0.02))
        at = int(r.uniform(0, 1) ** 1.6 * (n - g))
        s0 = r.integers(0, len(src) - g)
        w = np.hanning(g)[:, None] * r.uniform(0.2, 1) * np.array([r.uniform(.5, 1), r.uniform(.5, 1)])
        x[at:at + g] += src[s0:s0 + g] * w
    return x / (np.abs(x).max() + 1e-9)


def rock_grind(seconds, seed):
    x = filt(stereo_noise(seconds, seed, 'brown'), 'bp', (150, 800))
    t = np.linspace(0, 1, len(x))
    env = np.sin(np.pi * np.clip(t * 1.2, 0, 1)) ** 1.2
    return x * env[:, None] * lfo_env(seconds, seed + 5, 3, 0.5)[:, None]


def air_tear(seconds, seed, f0=2000, f1=400):
    x = sweep_filter(noise(seconds, seed, 'white'), f0, f1, 'bp', q=1.5)
    t = np.linspace(0, 1, len(x))
    env = np.sin(np.pi * t) ** 0.7
    x = x * env
    return pan_sweep(st(x / (np.abs(x).max() + 1e-9)), -0.7, 0.7)


def glassy(seconds, f, seed):
    t = np.arange(sec(seconds)) / SR
    x = sum(a * np.sin(2 * np.pi * f * m * t) * np.exp(-t / d) for m, a, d in
            [(1, 1, 1.6), (2.76, 0.45, 0.9), (5.4, 0.22, 0.5)])
    x *= np.minimum(1, t / 0.04)
    return st(x / np.abs(x).max())


def low_boom(seconds, seed):
    x = pitched_sine(seconds, 70, 38, 0.3) + 0.35 * pitched_sine(seconds, 140, 76, 0.3) + \
        0.15 * pitched_sine(seconds, 210, 114, 0.3)
    x += 0.3 * filt(noise(seconds, seed, 'brown'), 'lp', 300)
    x *= expenv(seconds, seconds / 3, 0.012)
    x = filt(x, 'lp', 800)
    return st(x / np.abs(x).max())


def hum(seconds, seed):
    t = np.arange(sec(seconds)) / SR
    f = TONIC
    x = np.sin(2 * np.pi * f * t) + 0.7 * np.sin(2 * np.pi * (2 * f + 0.3) * t) + \
        0.4 * np.sin(2 * np.pi * (3 * f * 2 / 2 * 1.0 - 0.3) * t) * 0.8 + 0.25 * np.sin(2 * np.pi * 3 * f * t)
    x *= 0.8 + 0.2 * np.sin(2 * np.pi * 0.33 * t)
    return st(x / np.abs(x).max())


def heartbeat(seed, f=60.0):
    def thump(a):
        x = pitched_sine(0.22, f * 1.6, f, 0.03) + 0.4 * pitched_sine(0.22, f * 3.2, f * 2, 0.03)
        return x * expenv(0.22, 0.06, 0.005) * a
    one = np.zeros(sec(0.45))
    a, b = thump(1.0), thump(0.7)
    one[:len(a)] += a
    one[sec(0.2):sec(0.2) + len(b)] += b
    return st(one / np.abs(one).max())


def celesta(f, seed, seconds=1.6):
    t = np.arange(sec(seconds)) / SR
    idx = 2.2 * np.exp(-t / 0.25)
    x = np.sin(2 * np.pi * f * t + idx * np.sin(2 * np.pi * f * 3.5 * t)) * np.exp(-t / 0.55)
    x *= np.minimum(1, t / 0.004)
    return st(x / np.abs(x).max())


def taiko(seed, f0=90, f1=55):
    body = pitched_sine(0.9, f0, f1, 0.08) * expenv(0.9, 0.22, 0.003)
    skin = filt(noise(0.9, seed, 'white'), 'bp', (300, 2500)) * expenv(0.9, 0.03, 0.001) * 0.35
    x = body + skin
    return reverb(st(x / np.abs(x).max()), 1.2, seed + 1, 0.2, 3000)


def wind(seconds, seed, band=(200, 3000), gust=0.25):
    x = filt(stereo_noise(seconds, seed, 'pink'), 'bp', band)
    return x * lfo_env(seconds, seed + 9, gust, 0.75)[:, None]


def shimmer(seconds, seed):
    n = filt(stereo_noise(seconds, seed, 'white'), 'bp', (3000, 9000)) * 0.25
    t = np.arange(sec(seconds)) / SR
    f = 400 * (900 / 400) ** (t / seconds)
    s = np.sin(2 * np.pi * np.cumsum(f) / SR) * 0.6
    env = np.sin(np.pi * t / seconds) ** 1.5
    return (n + st(s)) * env[:, None]


def saw(seconds, f):
    t = np.arange(sec(seconds)) / SR
    return 2 * ((f * t) % 1) - 1


def corrupt_drone(seconds, seed):
    x = saw(seconds, TONIC * 1.5) + saw(seconds, TONIC * 1.5 * 2 ** (7 / 1200))
    x = filt(x, 'lp', 600)
    t = np.linspace(0, 1, len(x))
    x *= np.sin(np.pi * t) ** 0.8
    return st(x / np.abs(x).max())


def roar(seconds, seed):
    r = np.random.default_rng(seed)
    n = sec(seconds)
    t = np.arange(n) / SR
    f = 70 + 40 * np.sin(np.pi * np.clip(t / seconds, 0, 1)) ** 0.6 + filt(r.standard_normal(n), 'lp', 12) * 40
    src = 2 * ((np.cumsum(f) / SR) % 1) - 1
    src += 0.3 * filt(r.standard_normal(n), 'bp', (300, 1500))
    u = t / seconds
    f1 = sweep_filter(src, 700, 400, 'bp', q=4)
    f2 = sweep_filter(src, 1200, 800, 'bp', q=5)
    body = f1 + 0.6 * f2 + 0.5 * filt(src, 'lp', 160)
    env = np.minimum(1, t / 0.18) * np.exp(-np.maximum(0, t - seconds * 0.55) / (seconds * 0.2))
    body *= env
    breath = filt(noise(seconds, seed + 1, 'white'), 'bp', (300, 1500)) * env * 0.35
    x = sat(st((body + breath) / np.abs(body + breath).max()), 0.25, 3)
    return reverb(x, 2.5, seed + 2, 0.32, 3500)


def reversed_cymbal(seconds, seed):
    x = filt(stereo_noise(seconds, seed, 'white'), 'hp', 5000) * expenv(seconds, seconds / 4, 0.002)[:, None]
    return x[::-1].copy()


def riser(seconds, seed):
    x = sweep_filter(noise(seconds, seed, 'white'), 300, 3000, 'bp', q=2)
    t = np.linspace(0, 1, len(x))
    return st(x * t ** 2.5)


def energy_bloom(seconds, seed):
    x = sweep_filter(noise(seconds, seed, 'pink'), 200, 4000, 'lp')
    t = np.linspace(0, 1, len(x))
    return st(x * np.sin(np.pi * t) ** 0.8)


def swish(seconds, seed):
    x = sweep_filter(noise(seconds, seed, 'white'), 600, 3500, 'bp', q=1.2)
    t = np.linspace(0, 1, len(x))
    return st(x * t ** 1.5)


def air_pass(seconds, seed):
    n = noise(seconds, seed, 'white')
    a = sweep_filter(n[:len(n) // 2], 300, 2500, 'bp', q=1.5)
    b = sweep_filter(n[len(n) // 2:], 2500, 900, 'bp', q=1.5)
    x = np.concatenate([a, b])
    t = np.linspace(0, 1, len(x))
    return pan_sweep(st(x * np.sin(np.pi * t)), -0.6, 0.0)


def room_tone(seconds, seed):
    x = filt(filt(stereo_noise(seconds, seed, 'pink'), 'hp', 120), 'lp', 2000)
    return reverb(x, 3.5, seed + 1, 0.5, 2500)[:len(x)]


# ---------------------------------------------------------------- the cue sheet
class Mix:
    def __init__(self):
        self.bus = {b: np.zeros((N + SR * 4, 2)) for b in ('DX', 'MX', 'FX', 'BG', 'MXI', 'FXI')}
        self.cues = []
        self.renders = {}

    def add(self, cid, t, x, bus, db, sources, fin=0.005, fout=0.05, norm='peak', immune=False):
        x = st(np.asarray(x, np.float64))
        x = peak_to(x, db) if norm == 'peak' else rms_to(x, db) if norm == 'rms' else x * 10 ** (db / 20)
        x = fade(x, fin, fout)
        fin = max(fin, 0.006)
        i = sec(t)
        b = self.bus[bus + 'I' if immune else bus]
        m = min(len(x), len(b) - i)
        b[i:i + m] += x[:m]
        self.cues.append(dict(id=cid, start=round(t, 3), length=round(len(x) / SR, 3), bus=bus, level_db=db,
                              norm=norm, fade_in=fin, fade_out=fout, sources=sources))
        self.renders[cid] = x


def punch(x, lo=200, hi=2000, drive=4.0):
    """The weight that survives laptop speakers: the hit's own 200 Hz-2 kHz band, saturated (adds harmonics)."""
    b = filt(st(x), 'bp', (lo, hi))
    b = np.tanh(b / (np.abs(b).max() + 1e-9) * drive) / np.tanh(drive)
    return b


def build():
    m = Mix()
    mf = load(f'{MUSIC}/music-final.ogg')
    mt = load(f'{MUSIC}/music-title.ogg')
    imp = lambda f, s=0: load(f'{IMPACT}/{f}.ogg', s)
    rpg = lambda f, s=0: load(f'{RPG}/{f}.ogg', s)
    sfx = lambda f, s=0: load(f'{SFX}/{f}.ogg', s)
    bar = lambda k: MF_BAR0 + 3.0 * k

    # ---- shot 1-2: calm, myth drone, storm
    m.add('B1-calm-air', 0.0, calm_air(4.0, 101), 'BG', -37, ['synth pink noise 300-4k, 0.08 Hz LFO'], 0.45, 0.6, 'rms')
    m1 = filt(filt(seg(mt, 8.0, 12.2), 'lp', 2000), 'hp', 60)
    g = np.ones(len(m1))
    g[sec(3.9):] = 10 ** (-15 / 20)
    m.add('M1-myth-drone', 0.0, m1 * g[:, None], 'MX', -31, ['music-title.ogg 8.0-12.2 s, lp 2k hp 60'], 0.6, 0.3, 'rms')
    sb = storm_bed(9.05, 202)
    g = np.ones(len(sb))
    g[sec(0.85):sec(1.13)] = 10 ** (-15 / 20)
    g = np.convolve(g, np.ones(480) / 480, 'same')
    m.add('B2-storm', 3.35, sb * g[:, None], 'BG', -35, ['synth brown noise lp 600 + rolling thunder'], 0.4, 0.8, 'rms')

    # E1 sky crack: reversed swell -> hit on 4.50 frame
    body = thunder_clap(301)
    swell = body[:sec(0.3)][::-1] * np.linspace(0, 1, sec(0.3))[:, None] ** 2
    glass = filt(imp('impactGlass_heavy_000', -3), 'hp', 1000)
    hit = np.zeros((sec(2.8), 2))
    hit[:len(body)] += body * 0.9
    hit[:len(glass)] += peak_to(glass, -6)[:sec(2.8)]
    cr = crackle(302)
    hit[:len(cr)] += cr * 0.5
    sub = sub_drop(1.2, 60, 35)
    hit[:len(sub)] += sub * 0.35
    hit += punch(hit) * 0.45
    tail = rolling_thunder(3.0, 303)
    hit[sec(0.3):sec(0.3) + len(tail)] += tail[:sec(2.5)] * 0.55
    m.add('E1a-crack-swell', 4.20, swell, 'FX', -18, ['reversed thunder body'], 0.02, 0.005, immune=True)
    m.add('E1-sky-crack', 4.50, hit, 'FX', -3, ['impactGlass_heavy_000 hp1k -3st', 'synth thunder clap 3k->400',
                                              'synth crackle 2-6k', 'sub 60->35 Hz', 'rolling thunder'], 0.003, 0.6)
    # music: drone returns
    m1b = filt(seg(mt, 12.2, 19.4), 'lp', 1500)
    m.add('M1b-drone-return', 5.60, m1b, 'MX', -34, ['music-title.ogg 12.2-19.4 s lp 1.5k'], 1.0, 1.2, 'rms')
    # E2 land rises (J-cut 0.3 s before the 6.00 move)
    cr1 = stretch(rpg('creak1', -12), 3.0)
    cr3 = stretch(rpg('creak3', -12), 3.0)
    grind = rock_grind(2.7, 401)
    e2 = np.zeros((sec(2.7), 2))
    e2 += grind * 0.7
    c = filt(np.concatenate([cr1, cr3])[:sec(2.7)], 'lp', 800)
    e2[:len(c)] += peak_to(c, -4)
    m.add('E2-land-rises', 5.70, e2, 'FX', -10, ['creak1+creak3 -12st x3 lp800', 'synth rock grind 150-800'], 0.25, 0.4)
    m.add('E2b-rock-settle', 7.95, filt(sfx('impactMining_001', -7), 'lp', 3000), 'FX', -14,
          ['impactMining_001 -7st'], 0.005, 0.2)
    m.add('E2c-debris', 8.02, granular(1.0, 402), 'FX', -20, ['synth granular 40 grains seed A'], 0.01, 0.3)

    # ---- shot 3-4: comet, horizon, distant boom, egg voice
    m.add('E3-comet', 9.68, air_tear(0.8, 501), 'FX', -13, ['synth air tear 2k->400 Doppler pan L->R'], 0.05, 0.15)
    m.add('E4a-egg-voice', 10.50, reverb(glassy(2.3, TONIC * 4, 502), 2.0, 503, 0.4)[:sec(2.3)], 'FX', -24,
          ['synth glassy tone F4 partials 1/2.76/5.4'], 0.03, 0.8)
    m.add('E4b-distant-boom', 10.85, reverb(low_boom(1.75, 504), 3.0, 505, 0.45, 1500)[:sec(1.75)], 'FX', -17,
          ['synth low boom lp 800 + 3 s hall'], 0.03, 0.5)

    # ---- shot 5: shrine (hum, heartbeat, motif, room, quiet orchestra)
    m.add('E5-egg-hum', 12.00, hum(5.4, 601), 'FX', -35, ['synth hum F2 + harmonics 0.3 Hz beat'], 0.8, 0.6, 'rms')
    hb = np.zeros((sec(3.6), 2))
    for k in range(4):
        x = heartbeat(602 + k)
        i = sec(k * 60 / 55)
        hb[i:i + len(x)] += x[:len(hb) - i] * (0.75 + 0.08 * k)
    m.add('E5b-heartbeat', 13.50, hb, 'FX', -15, ['synth double thump 60 Hz, 55 bpm'], 0.005, 0.3)
    m.add('B3-shrine-room', 12.00, room_tone(5.4, 603), 'BG', -39, ['synth room tone + 3.5 s stone IR'], 0.6, 0.15,
          'rms')
    for k, (t, f) in enumerate(zip([13.50, 14.25, 15.00], TRIAD)):
        m.add(f'M2-motif-{k + 1}', t, reverb(celesta(f, 610 + k), 2.0, 620 + k, 0.4)[:sec(1.6)], 'MX', -22,
              [f'synth FM celesta {f:.0f} Hz'], 0.004, 0.3)
    m3 = seg(mf, bar(0), bar(5))           # segment A (quiet opening), first downbeat on 13.50
    m.add('M3-quiet-orchestra', 13.50, m3, 'MX', -24, ['music-final.ogg seg A bar0-5'], 0.03, 0.30, 'rms')

    # ---- shot 6-7: peoples, hill
    w4 = wind(6.8, 701)
    cl4 = rpg('cloth4')
    for k in range(4):
        i = sec(0.9 + k * 1.6)
        w4[i:i + len(cl4)] += peak_to(cl4, -14)[:len(w4) - i]
    m.add('B4-hilltop-wind', 16.95, w4, 'BG', -31, ['synth wind 200-3k gusts', 'cloth4 flutter'], 0.3, 0.5, 'rms')
    tk = np.zeros((sec(6.2), 2))
    for k, t in enumerate(np.arange(0, 5.99, 0.75)):
        x = pan(taiko(710 + k, 92 if k % 2 == 0 else 84), -0.4 if k % 2 == 0 else 0.4)
        acc = 1.0 if abs(17.25 + t - 18.75) < 0.1 else 0.55 + 0.05 * k
        i = sec(t)
        tk[i:i + len(x)] += x[:len(tk) - i] * acc
    m.add('M4-rival-drums', 17.25, tk, 'MX', -12, ['synth taiko pair, Azure L / Crimson R, alternating beats'], 0.004,
          0.3)
    W1, W2 = 18.33, 18.62                  # measured onsets of "Two" and "banners" (card-2 start 18.28)
    for cid, t, cf, p, wood in [('E6a-banner-azure', W1, 'cloth1', -0.4, 'impactWood_heavy_000'),
                                ('E6b-banner-crimson', W2, 'cloth3', 0.4, 'impactWood_heavy_001')]:
        cl = stretch(rpg(cf, -4), 1.5)
        th = filt(sfx(wood), 'lp', 2000)
        x = np.zeros((max(len(cl), len(th)) + sec(0.1), 2))
        x[:len(cl)] += peak_to(cl, -3)
        x[sec(0.06):sec(0.06) + len(th)] += peak_to(th, -6)
        m.add(cid, t - 0.04, pan(x, p), 'FX', -13, [f'{cf} -4st x1.5', f'{wood} lp2k'], 0.01, 0.2)
    m.add('E6c-turf', 18.85, filt(sfx('footstep_grass_001'), 'lp', 4000), 'FX', -22, ['footstep_grass_001'], 0.005,
          0.1)
    # E7 hill clash
    m.add('E7a-anticipation', 22.15, swish(0.30, 720), 'FX', -20, ['synth noise sweep'], 0.02, 0.01)
    bash = np.zeros((sec(1.3), 2))
    bash[:len(sfx('impactWood_heavy_003'))] += peak_to(sfx('impactWood_heavy_003'), -1)[:sec(1.3)]
    met = filt(imp('impactMetal_medium_002'), 'lp', 5000)
    bash[:len(met)] += peak_to(met, -4)[:sec(1.3)]
    kn = rpg('drawKnife2')
    bash[sec(0.03):sec(0.03) + len(kn)] += peak_to(kn, -14)[:sec(1.27)]
    th = pitched_sine(0.6, 140, 90, 0.05) * expenv(0.6, 0.15, 0.003)
    th = th + 0.3 * np.tanh(th * 3)
    bash[:len(th)] += st(th) * 0.4
    bash = filt(bash, 'hp', 110)
    bash += punch(bash, 250, 2500, 7) * 1.0 * np.exp(-np.arange(len(bash)) / SR / 0.35)[:, None]
    m.add('E7-hill-clash', 22.50, reverb(bash, 1.5, 721, 0.25)[:sec(1.25)], 'FX', -10.5,
          ['impactWood_heavy_003', 'impactMetal_medium_002 lp5k (the only metal)', 'drawKnife2 scrape',
           'synth 90 Hz thump'], 0.003, 0.4, 'rms')

    # ---- shot 8: choice
    m.add('B5-meadow', 23.00, wind(5.6, 801, (400, 5000), 0.4), 'BG', -33, ['synth soft wind + leafy AM'], 0.25, 0.4,
          'rms')
    m.add('E8-feed-shimmer', 23.80, shimmer(1.4, 802), 'FX', -24, ['synth shimmer 400->900 Hz, no transient'], 0.25,
          0.35)
    m.add('M2b-motif-major', 24.00, reverb(celesta(TRIAD[1] * 1.25, 803), 1.8, 804, 0.45)[:sec(1.6)], 'MX', -24,
          ['synth celesta E6 (major third over C6)'], 0.004, 0.3)
    e9 = corrupt_drone(1.5, 805) * 0.6
    cr2 = rpg('creak2', -9)
    e9[:len(cr2)] += peak_to(cr2, -2)[:len(e9)]
    m.add('E9-corrupt-creep', 25.90, e9, 'FX', -18, ['creak2 -9st', 'synth detuned saws 7c lp600'], 0.08, 0.3)
    m.add('M2c-motif-tritone', 26.25, reverb(celesta(TRIAD[0] * 2 ** (6 / 12), 806), 1.8, 807, 0.45)[:sec(1.6)], 'MX',
          -25, ['synth celesta tritone'], 0.004, 0.3)

    # ---- shot 9: hatch
    hbx = np.zeros((sec(2.55), 2))
    t = 0.0
    k = 0
    while t < 2.2:
        bpm = 55 + (96 - 55) * (t / 2.2) ** 1.3
        x = heartbeat(900 + k)
        i = sec(t)
        hbx[i:i + len(x)] += x[:len(hbx) - i] * (0.7 + 0.3 * t / 2.2)
        t += 60 / bpm
        k += 1
    m.add('E10-heartbeat-quickens', 28.43, hbx, 'FX', -18, ['synth heartbeat 55->120 bpm'], 0.005, 0.06)
    for k, (t, f) in enumerate(zip([29.25, 30.00, 30.75, 31.00], range(4))):
        gl = filt(imp(f'impactGlass_light_00{f}', -5), 'hp', 800)
        crk = rpg('creak1', 5)
        x = np.zeros((sec(0.4), 2))
        x[:len(gl)] += peak_to(gl, -3)[:sec(0.4)]
        x[:len(crk)] += peak_to(crk, -10)[:sec(0.4)]
        m.add(f'E10b-shell-{k + 1}', t, x, 'FX', -20 + 1.5 * k, [f'impactGlass_light_00{f} -5st hp800', 'creak1 +5st'],
              0.005, 0.05)
    m.add('B6-storm-shrine', 28.20, room_tone(2.85, 910) * 0.7 + storm_bed(2.85, 911, False) * 0.25, 'BG', -34,
          ['room tone + storm bed, new seeds'], 0.3, 0.05, 'rms')
    # SILENCE 31.05-31.45; E11 hatch at 31.50
    hx = np.zeros((sec(1.8), 2))
    gh = imp('impactGlass_heavy_002', -5)
    hx[:len(gh)] += peak_to(gh, -2)[:len(hx)]
    so = imp('impactSoft_medium_001', -3)
    hx[:len(so)] += peak_to(so, -5)[:len(hx)]
    bl = energy_bloom(0.5, 920)
    hx[:len(bl)] += bl * 0.35
    sb_ = sub_drop(0.8, 55, 40)
    hx[:len(sb_)] += sb_ * 0.4
    hx += punch(hx) * 0.45
    m.add('E11-hatch', 31.50, reverb(hx, 1.8, 921, 0.25)[:sec(1.8)], 'FX', -3,
          ['impactGlass_heavy_002 -5st', 'impactSoft_medium_001 -3st', 'synth energy bloom', '45 Hz sub'], 0.003, 0.5)
    rr = roar(2.7, 930)
    chirp = load_roar_chirp(rr)
    m.add('E11b-chirp-growl', 31.95, chirp, 'FX', -12, ['roar render +7 st'], 0.02, 0.15)
    m5 = seg(mf, bar(6), bar(6) + 2.95)    # segment B, the lift, on its downbeat
    m.add('M5-hatch-swell', 31.50, m5, 'MX', -22, ['music-final.ogg seg B from bar 6 (18.07 s)'], 0.01, 0.12, 'rms')

    # ---- shot 10: Kaiju and sides
    s7 = storm_bed(8.45, 1001)
    m.add('B7-storm-2', 32.00, s7, 'BG', -28, ['storm bed new seed'], 0.25, 0.6, 'rms')
    m.add('E12-roar', 34.50, rr + punch(rr, 250, 2000, 3) * 0.6, 'FX', -23, ['synth formant roar saw 70-110 Hz + breath + canyon IR'], 0.02, 0.4, 'rms')
    m.add('E12b-roar-debris', 34.80, granular(1.2, 1002), 'FX', -20, ['synth granular seed B'], 0.01, 0.3)
    stab_src = [(18.04, 0), (60.05, 2), (66.04, 6)]
    for k, ((t0, semis), at) in enumerate(zip(stab_src, [35.25, 36.00, 37.50])):
        s_ = seg(mf, t0 - 0.01, t0 + 1.4)
        if semis:
            s_ = st(np.stack([np.interp(np.arange(0, len(s_) - 1, 2 ** (semis / 12)), np.arange(len(s_)), s_[:, c])
                              for c in range(2)], 1))
        if k == 2:
            s7b = st(np.stack([np.interp(np.arange(0, len(s_) - 1, 2 ** (7 / 12)), np.arange(len(s_)), s_[:, c])
                               for c in range(2)], 1))
            s_[:len(s7b)] += s7b * 0.5
            s_ = reverb(s_, 1.6, 1010, 0.35)
        m.add(f'M6-stab-{k + 1}', at - 0.01, s_, 'MX', -13 if k < 2 else -11,
              [f'music-final.ogg onset {t0:.2f} s +{semis} st'], 0.005, 0.25 if k < 2 else 0.4)
    m6b = seg(mf, bar(23) + 0.4, bar(24))
    rs = riser(2.1, 1020)
    x = np.zeros((sec(2.1), 2))
    x[:min(len(m6b), len(x))] += rms_to(filt(m6b, 'lp', 900), -24)[:len(x)]
    x += rs * 0.15
    m.add('M6b-rise', 38.35, x, 'MX', -16.5, ['music-final.ogg pickup lp900', 'synth reversed-noise riser'], 0.3, 0.1,
          'rms')

    # ---- shot 11: hero and title
    m.add('B8-warm-wind', 40.20, wind(6.3, 1101, (150, 2500), 0.2), 'BG', -35, ['synth warm wind lp 2.5k'], 0.3, 1.0,
          'rms')
    m7 = seg(mf, bar(24) - 0.10, bar(25) - 0.06)  # segment C from a downbeat (72.07 s), 3 frames of pickup before the cut
    m.add('M7-full-theme', 40.40, m7, 'MX', -14, ['music-final.ogg seg C bar 24 (72.07 s)'], 0.008, 0.03, 'rms')
    m.add('M7b-reverse-cymbal', 42.00, reversed_cymbal(1.20, 1102), 'MX', -16, ['synth reversed filtered noise'],
          0.3, 0.005)
    m.add('E13-logo-pass', 43.17, air_pass(0.27, 1103), 'FX', -17, ['synth air pass 300->2.5k->900'], 0.02, 0.03, immune=True)
    th = np.zeros((sec(3.2), 2))
    ch = seg(mf, bar(25) - 0.005, bar(25) + 3.2)
    th[:len(ch)] += rms_to(ch, -9)[:len(th)]
    tko = taiko(1104, 80, 48)
    th[:len(tko)] += peak_to(tko, -4)
    bm = low_boom(2.5, 1105)
    th[:len(bm)] += peak_to(bm, -5)
    th += punch(th[:sec(0.6)], 200, 2000, 3).repeat(1, 0)[:0].sum() if False else 0
    pk = np.zeros_like(th)
    pk[:sec(0.7)] = punch(th[:sec(0.7)], 200, 2200, 3.5) * np.exp(-np.arange(sec(0.7)) / SR / 0.25)[:, None]
    th += pk * 0.5
    th = reverb(th, 3.0, 1106, 0.3)[:sec(3.2)]
    g = np.ones(len(th))
    g[sec(0.42):] = 10 ** (-16 / 20)       # ring-out steps under the spoken title line (44.00)
    g = np.convolve(g, np.ones(sec(0.12)) / sec(0.12), 'same')
    g[:sec(0.1)] = 1
    th *= (g * np.linspace(1, 0.6, len(th)) ** 1.5)[:, None]
    m.add('M8-title-hit', 43.50, th, 'MX', -6, ['music-final.ogg chord at bar 25 (75.07 s)', 'synth taiko',
                                                'synth low boom', '3 s hall'], 0.004, 1.2, 'rms', immune=True)
    if False: m.add('M8-title-hit', 43.50, th, 'MX', -1, ['music-final.ogg chord at bar 25 (75.07 s)', 'synth taiko',
                                                'synth low boom', '3 s hall'], 0.004, 1.2)
    # narration (slid so the marks land: "One" 2-4 frames before 19.50, "Corrupt" on the 25.80 whip)
    for cid, t, f in [('DX1-card-1', 5.05, 'story-card-1'), ('DX3-card-3', 12.55, 'story-card-3'),
                      ('DX2-card-2', 18.28, 'story-card-2'), ('DX4-card-4', 23.67, 'story-card-4'),
                      ('DX5-card-5', 40.80, 'story-card-5'), ('DX6-story-title', 44.00, 'story-title')]:
        v = load(f'{VOICE}/{f}.ogg')
        v = filt(v, 'hp', 100)
        m.add(cid, t, v, 'DX', -21, [f'{f}.ogg (hp 100)'], 0.005, 0.05, 'rms')
    return m, mt


def load_roar_chirp(rr):
    x = rr[:sec(1.0)]
    r = 2 ** (7 / 12)
    y = st(np.stack([np.interp(np.arange(0, len(x) - 1, r), np.arange(len(x)), x[:, c]) for c in range(2)], 1))
    return y[:sec(0.5)]


# ---------------------------------------------------------------- buses + master
def env_follow(x, att, rel):
    a = np.exp(-1 / (att * SR))
    r = np.exp(-1 / (rel * SR))
    e = np.abs(mono(x))
    # vectorised-ish: one-pole with different attack/release via scipy on chunks is complex; do a coarse loop at 1 kHz
    step = SR // 1000
    ec = e[:len(e) // step * step].reshape(-1, step).max(1)
    out = np.zeros_like(ec)
    a1 = np.exp(-1 / (att * 1000))
    r1 = np.exp(-1 / (rel * 1000))
    v = 0.0
    for i, s in enumerate(ec):
        v = a1 * v + (1 - a1) * s if s > v else r1 * v + (1 - r1) * s
        out[i] = v
    full = np.repeat(out, step)
    return np.concatenate([full, np.full(len(e) - len(full), full[-1] if len(full) else 0)])


def duck_gain(key, depth_db, att, rel, thresh_db=-40):
    """Gain curve: depth_db of cut while the key signal is above thresh (soft knee via the envelope)."""
    e = env_follow(key, att, rel)
    on = np.clip((20 * np.log10(e + 1e-9) - thresh_db) / 10, 0, 1)
    on = np.convolve(on, np.ones(sec(0.02)) / sec(0.02), 'same')
    # offline look-ahead: the duck is fully down 0.12 s before the key arrives (no music burst on a line's first word)
    la = sec(0.12)
    on = np.maximum(on, np.concatenate([on[la:], np.zeros(la)]))
    ramp = np.convolve(on, np.ones(sec(0.06)) / sec(0.06), 'same')
    on = np.maximum(on, ramp)
    return 10 ** (-depth_db * on / 20)


def compress(x, thresh_db, ratio, att, rel, makeup=0.0):
    e = env_follow(x, att, rel)
    lev = 20 * np.log10(e + 1e-9)
    over = np.maximum(0, lev - thresh_db)
    g = 10 ** ((-over * (1 - 1 / ratio) + makeup) / 20)
    return x * g[:, None]


def build_mix(out):
    m, mt = build()
    os.makedirs(f'{out}/cues', exist_ok=True)
    B = m.bus
    dx = B['DX']
    def fx_chain(x):
        x = filt(x, 'hp', 30)
        x = compress(x, -10, 2.0, 0.010, 0.150)
        x = x * 0.85 + np.tanh(x * 2.5) / np.tanh(2.5) * 0.15
        x = x * duck_gain(dx, 7, 0.05, 0.4)[:, None]
        band = filt(x, 'bp', (1000, 4000))
        return x - band * (1 - duck_gain(dx, 3, 0.05, 0.4))[:, None]
    fxi_p = fx_chain(B['FXI'])
    mxi_p = B['MXI'] * duck_gain(dx, 13, 0.05, 0.4)[:, None]
    # FX: hp 30, glue 2:1, parallel saturation 15%, carved 3 dB 1-4 kHz under voice
    fx = filt(B['FX'], 'hp', 30)
    fx = compress(fx, -10, 2.0, 0.010, 0.150)
    fx = fx * 0.85 + np.tanh(fx * 2.5) / np.tanh(2.5) * 0.15
    vg = duck_gain(dx, 3, 0.05, 0.4)
    fx = fx * duck_gain(dx, 7, 0.05, 0.4)[:, None]
    band = filt(fx, 'bp', (1000, 4000))
    fx = fx - band * (1 - vg)[:, None]
    # BG: hp 120, ducked 4 dB by DX
    bg = filt(B['BG'], 'hp', 120) * duck_gain(dx, 9, 0.05, 0.4)[:, None]
    # MX: ducked 6 dB by DX, 4 dB by the hero hits
    mx = B['MX'] * duck_gain(dx, 13, 0.05, 0.4)[:, None]
    hero = np.zeros(len(mx))
    for t in (4.50, 22.50, 31.50, 34.50):
        hero[sec(t):sec(t + 0.6)] = 1
    hero = np.convolve(hero, np.ones(sec(0.005)) / sec(0.005), 'same')
    hg = 10 ** (-4 * np.minimum(1, ss.lfilter([1 - np.exp(-1 / (0.3 * SR))], [1, -np.exp(-1 / (0.3 * SR))],
                                                     hero) * 3) / 20)
    mx = mx * hg[:, None]
    # the full theme under card-5 is the loudest bed under a line: 4 dB more duck there (look-ahead, 0.15 s ramps)
    xg = np.ones(len(mx))
    xg[sec(40.62):sec(43.40)] = 10 ** (-4 / 20)
    xg = np.convolve(xg, np.ones(sec(0.15)) / sec(0.15), 'same')
    mx = mx * xg[:, None]
    # DX: hp 100 (done per clip), gentle de-ess
    ds = filt(dx, 'bp', (5000, 8000))
    dx = dx - ds * 0.3
    # breath: everything under -40 dBFS from 31.05 to 31.45; 60 ms before the logo
    def hole(t0, t1, ramp=0.05):
        g = np.ones(len(dx))
        g[sec(t0):sec(t1)] = 10 ** (-60 / 20)
        r = sec(ramp)
        g[sec(t0) - r:sec(t0)] = np.linspace(1, 10 ** (-3), r)
        return g
    def suck(t, depth_db=-15, ramp=0.06):
        g = np.ones(len(dx))
        a, b = sec(t - 0.30), sec(t - 0.01)
        g[a:b] = 10 ** (depth_db / 20)
        r = sec(ramp)
        g[a - r:a] = np.linspace(1, 10 ** (depth_db / 20), r)
        return g
    breath = hole(31.05, 31.47, 0.12) * hole(43.44, 43.50, 0.03) * suck(22.50) * suck(34.50, -18) * suck(43.50, -12)
    stems = {}
    for k, v in (('dx', dx), ('mx', mx), ('fx', fx), ('bg', bg)):
        g = breath if k != 'dx' else hole(43.44, 43.50, 0.03)
        stems[k] = (v * g[:, None])[:N]
    stems['fx'] = stems['fx'] + fxi_p[:N] * hole(31.05, 31.47, 0.12)[:N, None]
    stems['mx'] = stems['mx'] + mxi_p[:N]
    pre = sum(stems.values())
    # master: slow 1.5:1, true-peak limiter, loudnorm (two-pass, linear)
    master = compress(pre, -10, 1.5, 0.03, 0.4)
    tmp = f'{out}/_premaster.wav'
    save(tmp, master)
    meas = subprocess.run(['ffmpeg', '-hide_banner', '-i', tmp, '-af',
                           'alimiter=limit=0.85:attack=5:release=60:level=disabled,loudnorm=I=-16:TP=-1.5:LRA=11:print_format=json',
                           '-f', 'null', '-'], capture_output=True, text=True).stderr
    js = json.loads(meas[meas.rfind('{'):meas.rfind('}') + 1])
    gain_db = -16.0 - float(js['input_i'])
    master_g = 10 ** (gain_db / 20)
    # apply: linear gain then a look-ahead true-peak-ish limiter in ffmpeg at -1.3 dBFS sample (4x oversampled check later)
    lim = f'{out}/_lim.wav'
    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', tmp, '-af',
                    f'volume={gain_db:.3f}dB,aresample=192000,alimiter=limit={10 ** (-1.4 / 20):.4f}:attack=5:release=60:level=disabled,aresample=48000',
                    '-c:a', 'pcm_f32le', lim], check=True)
    mixed = load(lim)[:N]
    # hand-off: the title loop enters under the ring-out at exactly the level MusicDirector plays it (BaseGain 0.84,
    # same Music group), after the master so the limiter/loudnorm never touch it: the seam is the same samples.
    TITLE_OFFSET = 28.0                    # music-title.ogg file offset at film t = 45.80 (its sparse section ends)
    t0 = 45.80
    tail = mt[sec(TITLE_OFFSET):sec(TITLE_OFFSET) + (N - sec(t0))] * 0.84
    x = np.linspace(0, 1, len(tail))
    mixed[sec(t0):sec(t0) + len(tail)] *= np.cos(x * np.pi / 2)[:, None]
    mixed[sec(t0):sec(t0) + len(tail)] += tail * np.sin(x * np.pi / 2)[:, None]
    save(f'{out}/intro-mix.wav', mixed)
    scale = master_g
    for k, v in stems.items():
        save(f'{out}/stem-{k}.wav', v * scale)
    for cid, x in m.renders.items():
        save(f'{out}/cues/{cid}.wav', x)
    handoff = dict(title_offset_at_t0=TITLE_OFFSET, t0=t0, film_seconds=L,
                   title_offset_at_end=TITLE_OFFSET + (L - t0), title_gain=0.84)
    json.dump(dict(cues=m.cues, handoff=handoff, master_gain_db=gain_db, film_seconds=L, sample_rate=SR),
              open(f'{out}/cues.json', 'w'), indent=1)
    os.remove(tmp)
    os.remove(lim)
    print('master gain', round(gain_db, 2), 'dB; cues', len(m.cues), '; handoff', handoff)


if __name__ == '__main__':
    build_mix(sys.argv[1] if len(sys.argv) > 1 else '.')
