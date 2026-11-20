#!/usr/bin/env python3
"""Critical Mass in-game score and the new effects, built ONLY from the approved "Skybreak" material.

Skybreak is the intro film's original cue (tools/audio/skybreak/score_intro.py, ported unchanged from branch
fix/story-intro-sound, plan: Obsidian .../unity-port/intro-film-2/audio/SOUND-SCRIPT.md): F minor, 80 BPM, one 4-note
motif C F G Ab (5 1 2 b3) with a "feed" major form C F G A and a "corrupt" bent form C F Gb B. Every sound here is
synthesised with that file's own instruments (strings, ostinato, braam, horn, choir, taiko, timpani, glass, sub, riser)
and its one shared hall. No sample, no recording, no library sound, no soundfont, no AI generation.

    python3 tools/audio/skybreak/build_game_score.py [--wav-dir DIR] [--only music|sfx]

Writes (repo-relative):
  Assets/_Game/Audio/Music/Resources/CMScore/sb-<section>-<layer>-<variant>.ogg   the adaptive stems (36)
  Assets/_Game/Audio/Music/Resources/CMMusic/music-title.ogg                     the menu / results loop (16 bars)
  Assets/_Game/Audio/Music/Resources/CMStings/sting-{victory,defeat,draw}.ogg    end of match
  Assets/_Game/Audio/Music/Resources/CMStings/sting-hatch-<type>.ogg            the hatch, one per Kaiju type (5)
  Assets/_Game/Audio/Resources/CMSfxScore/sb_<cue>.ogg                          the new effects (mono)
  tools/audio/skybreak/score-manifest.json                                       grid, sections, plan, states, files
plus a .meta next to every new .ogg (deterministic GUIDs) and float WAV renders in --wav-dir (for the analyzer and the
listening pack; default ~/cm-audio-review-2026-11-18/_wav).

The grid: 80 BPM, beat 0.75 s, bar 3 s, section 8 bars = 24 s exactly (1,152,000 samples at 48 kHz). Each stem clip is
the section plus a 2.5 s release tail: the game schedules the next section at +24.000 s on a second source while the
first one rings out its own tail (horizontal re-sequencing), so no join is ever a hard cut and no clip is looped on
itself. The title loop is a true loop: its hall tail is wrapped onto its start, so the loop point is sample-continuous.
"""
import argparse, hashlib, json, math, os, subprocess, sys
import numpy as np
import scipy.signal as ss

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
sys.path.insert(0, HERE)
import score_intro as SI          # noqa: E402  the Skybreak instruments and hall (unchanged copy)
import audio_ear as EAR           # noqa: E402  loudness and true peak
import soundfile as sf            # noqa: E402

SR = SI.SR
BPM, BEAT, BAR = 80, 0.75, 3.0
SECTION_BARS = 8
SECTION = SECTION_BARS * BAR       # 24.0 s
TAIL = 2.5
CLIP = SECTION + TAIL
hz = SI.hz

MUSIC_DIR = "Assets/_Game/Audio/Music/Resources"
SCORE_DIR = MUSIC_DIR + "/CMScore"
SFX_DIR = "Assets/_Game/Audio/Resources/CMSfxScore"

# ------------------------------------------------------------------ the harmony (F minor, Skybreak's chords)
CHORDS = {
    "Fm": dict(root=41, third=3, pad="F3 Ab3 C4 F4"),
    "F": dict(root=41, third=4, pad="F3 A3 C4 F4"),        # feed: the motif's major colour
    "Db": dict(root=37, third=4, pad="Db3 F3 Ab3 Db4"),
    "Bbm": dict(root=34, third=3, pad="Bb2 Db3 F3 Bb3"),
    "C": dict(root=36, third=4, pad="C3 E3 G3 C4"),         # the dominant (harmonic minor), every section's last bar
    "Ab": dict(root=32, third=4, pad="Ab2 C3 Eb3 Ab3"),
    "Eb": dict(root=39, third=4, pad="Eb3 G3 Bb3 Eb4"),
}
NAMES = ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"]


def mname(m):
    return NAMES[m % 12] + str(m // 12 - 1)


def mhz(m):
    return 440.0 * 2 ** ((m - 69) / 12)


SECTIONS = {
    "A": ["Fm", "Fm", "Db", "Db", "Bbm", "Bbm", "C", "C"],     # statement
    "B": ["Fm", "Db", "Ab", "Eb", "Fm", "Db", "Bbm", "C"],     # lift (relative-major colours)
    "C": ["Db", "Bbm", "Fm", "Fm", "Db", "Eb", "C", "C"],      # shadow (starts away from home)
    "D": ["Fm", "Fm", "Eb", "Eb", "Db", "Db", "C", "C"],       # drive
}
# The motif sounds only over F minor bars, so its feed form (A natural) always sits on an F major chord.
MOTIF_BARS = {"A": [0], "B": [0, 4], "C": [2], "D": [0, 1]}
# 12 sections = 288 s (4 min 48 s) before the order repeats; never the same section twice in a row (wrap included).
PLAN = ["A", "B", "C", "D", "B", "A", "D", "C", "A", "C", "B", "D"]

LAYERS = {"bed": ["minor", "feed"], "rhythm": ["pulse", "march", "drive", "heart"], "theme": ["minor", "feed", "corrupt"]}

MOTIF = {"minor": ["C4", "F4", "G4", "Ab4"], "feed": ["C4", "F4", "G4", "A4"], "corrupt": ["C4", "F4", "Gb4", "B3"]}
CORRUPT_CENTS = [0, -8, -16, -25]   # the film's bend (SOUND-SCRIPT: "horn motif C F Gb B, detuning")

# The game's mix states (MusicDirector / ScoreMix.cs holds the same table): layer gains and variants. "kaiju" picks the
# bed and theme variants: fed (the Kaiju sides with a team) = feed, berserk = corrupt (bed stays minor), else minor.
STATES = {
    "calm":      dict(bed=0.85, rhythm=("pulse", 0.60), theme=0.40),
    "contested": dict(bed=0.80, rhythm=("march", 1.00), theme=0.80),
    "leading":   dict(bed=0.90, rhythm=("march", 0.85), theme=1.00),
    "trailing":  dict(bed=1.00, rhythm=("pulse", 0.90), theme=0.55),
    "rally":     dict(bed=1.00, rhythm=("drive", 0.80), theme=1.00),
    "egg":       dict(bed=0.70, rhythm=("heart", 0.95), theme=0.35),
    "final":     dict(bed=1.00, rhythm=("drive", 1.00), theme=1.00),
    "berserk":   dict(bed=0.90, rhythm=("drive", 0.90), theme=1.00),
}
REFERENCE_STATE = "contested"      # normalised to the music target (-20 LUFS integrated, as the shipped loops were)
MUSIC_LUFS, STING_LUFS, TP_CEIL = -20.0, -16.0, -1.5

HATCH_TYPES = ["elder", "grove", "brute", "timber", "mire"]


def reseed(name):
    SI._seed[0] = int(hashlib.sha256(name.encode()).hexdigest()[:8], 16) % 100000000


class Buf:
    def __init__(self, seconds):
        self.x = np.zeros((int(round(seconds * SR)), 2))

    def add(self, t, sig, gain=1.0, p=0.0):
        sig = SI.pan(sig, p) if sig.ndim == 1 else sig
        i = int(round(t * SR))
        if i < 0:
            sig, i = sig[-i:], 0
        j = min(len(self.x), i + len(sig))
        if j > i:
            self.x[i:j] += sig[: j - i] * gain


def spans(chords):
    """Consecutive equal chords merged: [(start_bar, n_bars, chord)]."""
    out, i = [], 0
    while i < len(chords):
        j = i
        while j + 1 < len(chords) and chords[j + 1] == chords[i]:
            j += 1
        out.append((i, j - i + 1, chords[i]))
        i = j + 1
    return out


def notes(s):
    return [hz(n) for n in s.split()]


def ost_pattern(ch):
    c = CHORDS[ch]
    r = c["root"] if c["root"] >= 36 else c["root"] + 12   # keep the ostinato in C2..B2 as the film's
    t, f = r + c["third"], r + 7
    return [r, r, r + 12, r, t, r, f, r]


def pedal_pattern(ch):
    """The drive's ostinato: the chord tones on the off-steps over an F pedal on every other step (a tonic pedal)."""
    c = CHORDS[ch]
    r = c["root"] if c["root"] >= 36 else c["root"] + 12
    f = CHORDS["Fm"]["root"]; f = f if f >= 36 else f + 12
    return [f, r, f + 12, r + c["third"], f, r + 7, f + 12, r]


def ostinato(buf, t0, bars, chords, amp, step=0.375, cres=(1.0, 1.0), pedal=False):
    n = int(round(bars * BAR / step))
    for k in range(n):
        t = t0 + k * step
        bar = int((t - t0) // BAR)
        pat = (pedal_pattern if pedal else ost_pattern)(chords[bar])
        g = cres[0] + (cres[1] - cres[0]) * k / max(1, n - 1)
        acc = 1.0 if k % 4 == 0 else 0.78
        buf.add(t, SI.ostinato_note(mhz(pat[k % 8]), min(0.33, step * 0.9), amp * g * acc), p=(-0.25 if k % 2 else 0.25))


def hall(x, wet):
    if not hasattr(hall, "ir"):
        hall.ir = SI.hall_ir()
    return SI.reverb(x, hall.ir, wet)


def music_eq(x):
    """The film's music-bus EQ (score_intro.main): clear the sub mud, lift presence and air."""
    y = SI.filt(x, "highpass", 32, 2)
    for ch in range(2):
        y[:, ch] = SI.peq(SI.peq(SI.peq(SI.peq(SI.peq(y[:, ch], 70, -3.5, 0.7), 160, -3.0, 0.9), 2000, 3.0, 0.8), 3500,
                                 4.0, 0.7, kind="shelf"), 9000, 3.0, 0.7, kind="shelf")
    return y


def finish_section(buf, wet=0.32):
    y = music_eq(hall(buf.x, wet))
    k = int(0.4 * SR)
    y[-k:] *= np.linspace(1, 0, k)[:, None] ** 2
    return y


# ------------------------------------------------------------------ the stems
def bed(sec, variant):
    chords = list(SECTIONS[sec])
    if variant == "feed":
        for b in MOTIF_BARS[sec]:
            chords[b] = "F"
            if b + 1 < len(chords) and chords[b + 1] == "Fm":
                chords[b + 1] = "F"
    buf = Buf(CLIP)
    for b0, nb, ch in spans(chords):
        t, d = b0 * BAR, nb * BAR
        buf.add(t, SI.strings(notes(CHORDS[ch]["pad"]), d, 0.062, a=0.55, r=1.1, bright=2700))
        if nb >= 2:
            buf.add(t, SI.choir(notes(CHORDS[ch]["pad"])[1:], d, 0.042, "o", a=1.0, r=1.2))
        root = mhz(CHORDS[ch]["root"] - 12 if CHORDS[ch]["root"] >= 36 else CHORDS[ch]["root"])
        buf.add(t, SI.sub(root, root, d + 0.6, 0.085, a=0.25))
    return finish_section(buf)


def rhythm(sec, variant):
    chords = SECTIONS[sec]
    buf = Buf(CLIP)
    if variant == "pulse":
        ostinato(buf, 0.0, SECTION_BARS, chords, 0.30)
    elif variant == "march":
        ostinato(buf, 0.0, SECTION_BARS, chords, 0.24)
        for b in range(SECTION_BARS):
            t = b * BAR
            buf.add(t, SI.taiko(0.26 if b % 4 == 0 else 0.17, p=0.0 if b % 4 == 0 else -0.5, big=(b % 4 == 0)))
            buf.add(t + 2 * BEAT, SI.taiko(0.12, p=0.5))
            if b % 4 == 0:
                r = CHORDS[chords[b]]["root"]
                buf.add(t, SI.timpani(mhz(r if r >= 36 else r + 12), 0.10, 1.0, soft=True))
    elif variant == "drive":
        ostinato(buf, 0.0, SECTION_BARS, chords, 0.17, step=0.1875, pedal=True)
        for b in range(SECTION_BARS):
            for k in range(8):
                t = b * BAR + k * 0.375
                big = k == 0 and b % 2 == 0
                buf.add(t, SI.taiko(0.28 if big else (0.18 if k % 2 == 0 else 0.11), p=(0 if big else (-0.5 if k % 2 == 0 else 0.5)), big=big))
        # the braams hold the tonic pedal (F and C) whatever the chord above, so the drive stays in F minor
        r = CHORDS["Fm"]["root"]
        r = r if r >= 36 else r + 12
        for b in (3, 7):
            buf.add(b * BAR, SI.braam([mhz(r), mhz(r + 7)], 2.95, 0.085, a=1.8, r=0.05, open_t=2.9, top=1400, sat=1.3))
        buf.add(SECTION - 2.5, SI.riser(2.45, 0.035))
    elif variant == "heart":
        for b in range(SECTION_BARS):
            r = CHORDS[chords[b]]["root"]
            f = mhz(r if r >= 36 else r + 12)
            for beat in (0, 2):
                t = b * BAR + beat * BEAT
                buf.add(t, SI.timpani(f, 0.17, 0.35, soft=True))
                buf.add(t + 0.20, SI.timpani(f, 0.11, 0.30, soft=True))
        ostinato(buf, 0.0, SECTION_BARS, chords, 0.13, step=0.75)
    return finish_section(buf)


def motif(buf, t0, names, voice, amp, held=2.2, cents=None, low=True, p=-0.1):
    for k, nm in enumerate(names):
        t = t0 + k * BEAT
        last = k == len(names) - 1
        f = hz(nm) * (2 ** (cents[k] / 1200) if cents else 1.0)
        if voice == "horn":
            dur = held if last else 0.68
            buf.add(t, SI.horn(f, dur, amp, a=0.06, p=p))
            if low:
                buf.add(t, SI.horn(f / 2, dur, amp * 0.6, a=0.06, p=-p))
        elif voice == "glass":
            buf.add(t, SI.glass(f, held if last else 1.2, amp, p=p + 0.1 * k))
        elif voice == "pluck":
            buf.add(t, SI.ostinato_note(f, 0.33, amp, bright=4200), p=p)
        elif voice == "braam":
            buf.add(t, SI.braam([f / 4, f / 2], 0.7 if not last else held, amp, r=0.6, top=1500, sat=1.6))


def theme(sec, variant):
    chords = SECTIONS[sec]
    M = MOTIF_BARS[sec]
    buf = Buf(CLIP)
    names = MOTIF[variant]
    cents = CORRUPT_CENTS if variant == "corrupt" else None
    for i, b in enumerate(M):
        t = b * BAR
        if sec == "D" and i == 1:          # the echo: the same motif an octave up, on the glass voice
            motif(buf, t, [n[:-1] + str(int(n[-1]) + 1) for n in names], "glass", 0.045, cents=cents)
            continue
        motif(buf, t, names, "horn", 0.105, cents=cents)
        if sec in ("A", "C") and variant != "corrupt":
            motif(buf, t, [n[:-1] + str(int(n[-1]) + 1) for n in names], "glass", 0.03)
        if variant == "corrupt":           # the low strings rub a minor second under the bent motif
            buf.add(t, SI.strings([hz("F2"), hz("Gb2")], BAR, 0.032, a=0.3, r=0.6, bright=900))
            buf.add(t, SI.strings([hz("F3"), hz("C4")], 2 * BAR, 0.03, a=0.4, r=0.8, bright=1800))
    for b in range(SECTION_BARS):
        if b in M or (b - 1) in M or b % 2:
            continue
        c = CHORDS[chords[b]]
        m = 65 if variant == "corrupt" else c["root"] + c["third"]     # corrupt: a nagging, bent tonic (F4 -20 c)
        while m < 60:
            m += 12
        while m > 71:
            m -= 12
        f = mhz(m) * (2 ** (-20 / 1200) if variant == "corrupt" else 1.0)
        buf.add(b * BAR, SI.horn(f, 2.6, 0.048, a=0.45, r=0.8, p=0.15))
    return finish_section(buf)


def render_stems():
    out = {}
    for sec in SECTIONS:
        for layer, variants in LAYERS.items():
            for v in variants:
                name = f"sb-{sec}-{layer}-{v}"
                reseed(name)
                fn = {"bed": bed, "rhythm": rhythm, "theme": theme}[layer]
                out[name] = fn(sec, v)
                print(f"  {name}", flush=True)
    return out


# ------------------------------------------------------------------ the title loop, stings, hatch stingers
TITLE_CHORDS = ["Fm", "Fm", "Db", "Db", "Bbm", "Bbm", "C", "C", "Fm", "Fm", "Ab", "Ab", "Db", "Db", "C", "C"]
TITLE_SECONDS = len(TITLE_CHORDS) * BAR      # 48 s


def title_loop():
    reseed("music-title")
    buf = Buf(TITLE_SECONDS + 5.0)
    for b0, nb, ch in spans(TITLE_CHORDS):
        t, d = b0 * BAR, nb * BAR
        pad = notes(CHORDS[ch]["pad"])
        buf.add(t, SI.choir(pad[1:], d, 0.075, "u", a=1.4, r=1.4))
        hi = [f * 2 for f in pad[1:]]
        buf.add(t + 0.2, SI.strings(hi, d, 0.032, a=1.6, r=1.4, bright=3200))
        root = mhz(CHORDS[ch]["root"] - 12 if CHORDS[ch]["root"] >= 36 else CHORDS[ch]["root"])
        buf.add(t, SI.sub(root, root, d + 0.8, 0.05, a=0.8))
    for b in (1, 9):                          # the motif on the glass voice, the egg's colour, twice a loop
        motif(buf, b * BAR, ["C5", "F5", "G5", "Ab5"], "glass", 0.06, held=2.4)
    for b in (0, 4, 8, 12):                   # a slow heartbeat, very soft
        r = CHORDS[TITLE_CHORDS[b]]["root"]
        f = mhz(r if r >= 36 else r + 12)
        buf.add(b * BAR, SI.timpani(f, 0.10, 0.35, soft=True))
        buf.add(b * BAR + 0.2, SI.timpani(f, 0.065, 0.3, soft=True))
    y = music_eq(hall(buf.x, 0.36))
    n = int(round(TITLE_SECONDS * SR))
    loop = y[:n].copy()
    loop[: len(y) - n] += y[n:]               # wrap the hall tail onto the start: the loop point is continuous
    return loop


def sting(kind):
    reseed("sting-" + kind)
    buf = Buf(9.0)
    if kind == "victory":
        buf.add(0.0, SI.braam(notes("F1 F2 C3 A3"), 1.2, 0.20, r=1.6, top=2200, sat=1.4))
        buf.add(0.0, SI.taiko(0.45, 58, -0.2, big=True)); buf.add(0.01, SI.taiko(0.38, 64, 0.2, big=True))
        buf.add(0.0, SI.timpani(hz("F2"), 0.28, 1.6))
        motif(buf, 0.0, MOTIF["feed"], "horn", 0.12, held=3.0)
        buf.add(0.0, SI.strings(notes("F3 A3 C4 F4 A4"), 4.6, 0.08, a=0.05, r=2.0, bright=3600))
        buf.add(0.6, SI.choir(notes("F4 A4 C5"), 3.6, 0.06, "o", a=0.4, r=1.8))
        buf.add(3.0, SI.glass(hz("F5"), 2.4, 0.05))
    elif kind == "defeat":
        buf.add(0.0, SI.strings(notes("F2 C3 F3 Ab3"), 4.5, 0.07, a=0.5, r=2.2, bright=1800))
        buf.add(0.0, SI.choir(notes("F3 Ab3 C4"), 4.0, 0.05, "u", a=0.8, r=2.0))
        for k, nm in enumerate(["C4", "F4", "G4", "Ab4"]):
            buf.add(0.4 + k * 1.0, SI.horn(hz(nm), 0.9 if k < 3 else 1.4, 0.09, a=0.12, r=0.6))
        buf.add(4.4, SI.horn(hz("G4"), 2.0, 0.07, a=0.2, r=1.2))   # the third falls back: a sigh, unresolved upward
        buf.add(4.4, SI.timpani(hz("F2"), 0.16, 1.4, soft=True))
        buf.add(4.4, SI.strings(notes("Db3 F3 Ab3"), 1.2, 0.05, a=0.3, r=0.6, bright=1500))
        buf.add(5.6, SI.strings(notes("F2 C3 F3 Ab3"), 1.0, 0.05, a=0.3, r=1.8, bright=1400))
    elif kind == "draw":
        buf.add(0.0, SI.strings(notes("F4 G4 C5"), 4.0, 0.045, a=0.6, r=2.0, bright=3000))   # F sus2: neither third
        motif(buf, 0.3, ["C5", "F5", "G5"], "glass", 0.06, held=3.2)                       # the question, unanswered
        buf.add(0.0, SI.strings(notes("F2 C3"), 4.4, 0.05, a=0.6, r=2.0, bright=1400))
        buf.add(0.3, SI.timpani(hz("F2"), 0.12, 1.2, soft=True))
        buf.add(2.6, SI.choir(notes("F3 Ab3 C4"), 2.4, 0.035, "u", a=0.8, r=1.6))
    y = music_eq(hall(buf.x, 0.34))
    return trim_tail(y)


def hatch(kind):
    """The hatch, one per Kaiju type: the film's hatch tutti (braam, choir, taiko, timpani on F minor) with the motif
    in that type's voice. Elder = the film's own low horns; Grove = the feed form, bright; Brute = braam stabs, a
    drum on every note; Timber = plucked; Mire = the bent corrupt form over a rubbing low string."""
    reseed("hatch-" + kind)
    buf = Buf(7.5)
    tutti = "F1 F2 C3 F3 A3" if kind == "grove" else "F1 F2 C3 F3 Ab3"
    buf.add(0.0, SI.braam(notes(tutti), 2.2, 0.17, r=1.2))
    buf.add(0.0, SI.choir(notes("F3 A3 C4 F4" if kind == "grove" else "F3 Ab3 C4 F4"), 2.4, 0.07, "o", a=0.05, r=1.0))
    buf.add(0.0, SI.taiko(0.36, 58, -0.2, big=True)); buf.add(0.01, SI.taiko(0.32, 64, 0.2, big=True))
    buf.add(0.0, SI.timpani(hz("F2"), 0.26, 1.5))
    if kind == "elder":
        motif(buf, 0.0, ["C3", "F3", "G3", "Ab3"], "horn", 0.10, held=1.8)
    elif kind == "grove":
        motif(buf, 0.0, ["C5", "F5", "G5", "A5"], "glass", 0.06, held=2.2)
        motif(buf, 0.0, ["C4", "F4", "G4", "A4"], "horn", 0.08, held=1.8, low=False)
    elif kind == "brute":
        motif(buf, 0.0, ["C4", "F4", "G4", "Ab4"], "braam", 0.10, held=1.6)
        for k in range(4):
            buf.add(k * BEAT, SI.taiko(0.24, p=0.3 * (-1) ** k, big=(k == 3)))
    elif kind == "timber":
        motif(buf, 0.0, ["C4", "F4", "G4", "Ab4"], "pluck", 0.16)
        motif(buf, 0.0, ["C3", "F3", "G3", "Ab3"], "pluck", 0.10, p=0.2)
        buf.add(3 * BEAT, SI.strings(notes("F3 Ab3 C4"), 1.8, 0.05, a=0.2, r=1.0, bright=2200))
    elif kind == "mire":
        motif(buf, 0.0, MOTIF["corrupt"], "horn", 0.10, held=1.8, cents=CORRUPT_CENTS)
        buf.add(0.0, SI.strings([hz("F2"), hz("Gb2")], 1.8, 0.04, a=0.3, r=1.0, bright=900))
        buf.add(1.5, SI.strings(notes("F2 C3 F3 Ab3"), 2.4, 0.05, a=0.4, r=1.4, bright=1600))
    y = music_eq(hall(buf.x, 0.32))
    return trim_tail(y)


def trim_tail(y, floor_db=-70.0):
    e = np.abs(y).max(1)
    thr = e.max() * 10 ** (floor_db / 20)
    idx = np.nonzero(e > thr)[0]
    end = min(len(y), (idx[-1] if len(idx) else len(y) - 1) + int(0.05 * SR))
    y = y[:end].copy()
    k = min(len(y) // 4, int(0.6 * SR))
    y[-k:] *= np.linspace(1, 0, k)[:, None] ** 2
    return y


# ------------------------------------------------------------------ effects (mono, tuned to F minor, short)
def _n(sec):
    return int(round(sec * SR))


def membrane(f0, n, tau=0.12, drop=0.6, amp=1.0):
    t = np.arange(n) / SR
    f = f0 * (1 + drop * np.exp(-t / 0.015))
    x = SI.sine(f, n) * np.exp(-t / tau) + 0.3 * SI.sine(f * 1.5, n, 1.0) * np.exp(-t / (tau * 0.4))
    return x * amp


def grit(n, lo, hi, tau, amp=1.0, seed_=None):
    t = np.arange(n) / SR
    return SI.filt(SI.noise(n, seed_ if seed_ is not None else SI.seed()), "bp", (lo, hi)) * np.exp(-t / tau) * amp


def reson(f, n, q, tau):
    """A struck resonance: a 4 ms noise tap through one tuned peak (stone, not metal: no partials above it)."""
    t = np.arange(n) / SR
    b, a = ss.iirpeak(f, q, fs=SR)
    return ss.lfilter(b, a, SI.noise(n, SI.seed()) * np.exp(-t / 0.004)) * np.exp(-t / tau)


def thud(f, n, tau):
    """A body thump: one sine falling onto f over 10 ms."""
    t = np.arange(n) / SR
    return SI.sine(f * (1 + 0.5 * np.exp(-t / 0.01)), n) * np.exp(-t / tau)


def sfx(name, variant=0):
    reseed(f"{name}-{variant}")
    F1, F2, C2, C3, Ab2 = hz("F1"), hz("F2"), hz("C2"), hz("C3"), hz("Ab2")
    F3, C4, Ab3, Eb4, F4 = hz("F3"), hz("C4"), hz("Ab3"), hz("Eb4"), hz("F4")
    if name == "sb_dodge":                    # a quick air pass, rising: one gesture, nothing struck
        n = _n(0.34); t = np.arange(n) / SR
        fc = 700 * (3200 / 700) ** np.clip(t / 0.16, 0, 1)
        x = SI.sweep_bp(SI.noise(n, SI.seed()), fc, 1.6)
        e = SI.swell(_n(0.14), 30.0)
        env = np.concatenate([e, np.exp(-np.arange(n - len(e)) / SR / 0.05)])
        x = x * env + 0.25 * SI.tonal_air([hz("F5"), hz("C6")], n, q=40).mean(1) * env
    elif name == "sb_wall_place":             # the wall sets down: a tuned drum on F and a stone grain
        n = _n(0.45)
        x = membrane(F3 * (1 + 0.03 * variant), n, 0.10) + 0.6 * membrane(F2, n, 0.12) + grit(n, 350, 2600, 0.025, 1.1)
    elif name == "sb_block_place":            # a block: higher (C), shorter, a lighter grain
        n = _n(0.34)
        x = membrane(C3 * (1 + 0.03 * variant), n, 0.09, drop=0.4) + grit(n, 350, 1700, 0.035, 0.9) + 0.4 * membrane(C4, n, 0.03)
    elif name == "sb_wall_hit":               # a chip on stone: a dull slap with a stone grain, no tone, no ring
        n = _n(0.22)
        x = SI.filt(grit(n, 250, 1500, 0.025, 1.5), "lowpass", 2000) + 0.5 * thud(hz("Ab2") * (1 + 0.04 * variant), n, 0.05)
    elif name == "sb_wall_break":             # the wall falls: tuned low impact on F with three cracks
        x = 0.8 * SI.tuned_impact(F1, 0.9, tail=0.5, body_lp=900)[: _n(1.1)]
        n = len(x)
        for k, (t0, g) in enumerate([(0.0, 1.6), (0.055, 1.1), (0.15, 0.8), (0.31, 0.5)]):
            c = np.zeros(n); i = _n(t0); m = _n(0.14)
            c[i:i + m] = grit(m, 700, 4200, 0.014, g) + membrane(F3 * (1.0 + 0.12 * k), m, 0.03, amp=0.3 * g)
            x = x + c
    elif name == "sb_hut_level":              # the hut levels up: the motif's first three notes, rising, then Ab held
        b = Buf(1.7)
        for k, nm in enumerate(["C4", "F4", "G4"]):
            b.add(k * 0.18, SI.horn(hz(nm), 0.16, 0.10, a=0.03, r=0.25))
        b.add(0.54, SI.horn(hz("Ab4"), 0.6, 0.10, a=0.04, r=0.45))
        b.add(0.54, SI.glass(hz("Ab5"), 0.6, 0.04))
        b.add(0.0, SI.timpani(F2, 0.06, 0.8, soft=True))
        x = hall(b.x, 0.22).mean(1)
    elif name == "sb_ore_hit":                # the pick on ore: a resonant rock knock tuned C, Eb or Db, no metal ring
        f = [C4, Eb4, hz("Db4")][variant % 3]  # (F4 read as a clang to the ear; Db is the key's sixth)
        n = _n(0.22)
        x = 8 * reson(f, n, 6, 0.05) + 0.5 * thud(f / 2, n, 0.05) + grit(n, 500, 1800, 0.01, 0.6)
    elif name == "sb_ore_bank":               # ore into the hut: two soft plucks F then C, and a thud
        b = Buf(0.8)
        b.add(0.0, SI.ostinato_note(F3, 0.3, 0.5, bright=3200))
        b.add(0.12, SI.ostinato_note(C4, 0.3, 0.45, bright=3400))
        b.add(0.0, membrane(F2, _n(0.4), 0.09, amp=0.35))
        x = b.x.mean(1)
    elif name == "sb_ladder_place":           # two quick taps (Ab then C): the span is up
        b = Buf(0.5)
        b.add(0.0, membrane(Ab3, _n(0.3), 0.05) + grit(_n(0.3), 500, 2600, 0.012, 0.8))
        b.add(0.085, membrane(C4, _n(0.3), 0.045, amp=0.8) + grit(_n(0.3), 600, 3000, 0.010, 0.6))
        x = b.x.mean(1) * 2
    elif name == "sb_climb_step":             # one rung: a very short soft tap
        n = _n(0.12)
        x = membrane(C3 * (1 + 0.05 * variant), n, 0.025, drop=0.2) + grit(n, 600, 2400, 0.006, 0.7)
    elif name == "sb_bridge_place":           # the bridge lands: F then a lower C, a short low settle
        b = Buf(0.9)
        b.add(0.0, membrane(F3, _n(0.5), 0.10) + grit(_n(0.5), 300, 2200, 0.03, 0.9))
        b.add(0.18, membrane(C3, _n(0.6), 0.14, amp=0.9) + grit(_n(0.6), 250, 1600, 0.04, 0.6))
        b.add(0.0, SI.sub(F1, F1, 0.7, 0.25, a=0.004, tau=0.2))
        x = b.x.mean(1) * 2
    elif name == "sb_pit_dig":                # earth out: a dark burst and a low thump
        n = _n(0.36)
        t = np.arange(n) / SR
        x = (SI.filt(SI.noise(n, SI.seed(), "pink"), "bp", (250, 2200)) * np.exp(-t / 0.05) * 1.0
             + membrane(F2, n, 0.07, amp=0.6))
    elif name == "sb_behind_tick":            # "behind you": two soft taps, quick, mid-high so a laptop plays them
        b = Buf(0.22)
        b.add(0.0, membrane(C4 * 2, _n(0.1), 0.015, drop=0.1, amp=0.6) + grit(_n(0.1), 1500, 4500, 0.004, 0.7))
        b.add(0.075, membrane(C4 * 2, _n(0.1), 0.015, drop=0.1, amp=0.45) + grit(_n(0.1), 1500, 4500, 0.004, 0.55))
        x = SI.filt(b.x.mean(1) * 2, "lowpass", 6000)
    else:
        raise ValueError(name)
    x = np.asarray(x, float)
    x = x * SI.env_ar(len(x), 0.001, min(0.05, len(x) / SR / 3), curve=1)
    return x


SFX = {  # cue id -> variants
    "sb_dodge": 1, "sb_wall_place": 2, "sb_block_place": 2, "sb_wall_hit": 2, "sb_wall_break": 1, "sb_hut_level": 1,
    "sb_ore_hit": 3, "sb_ore_bank": 1, "sb_ladder_place": 1, "sb_climb_step": 2, "sb_bridge_place": 1,
    "sb_pit_dig": 1, "sb_behind_tick": 1,
}


def sfx_file(name, v, count):
    return name if count == 1 else f"{name}_{v}"


# ------------------------------------------------------------------ mixing helpers (shared with the analyzer and the pack)
def state_mix(stems, state, kaiju="minor", sections=None):
    """Offline mix of a steady state over a run of sections (default the whole plan), as the game plays it."""
    st = STATES[state]
    bed_v = "feed" if kaiju == "feed" else "minor"
    theme_v = kaiju if kaiju in ("feed", "corrupt") else "minor"
    if state == "berserk":
        theme_v = "corrupt"
    secs = sections or PLAN
    out = np.zeros((_n(len(secs) * SECTION + TAIL), 2))
    for k, s in enumerate(secs):
        i = _n(k * SECTION)
        for name, g in ((f"sb-{s}-bed-{bed_v}", st["bed"]), (f"sb-{s}-rhythm-{st['rhythm'][0]}", st["rhythm"][1]),
                        (f"sb-{s}-theme-{theme_v}", st["theme"])):
            x = stems[name]
            out[i:i + len(x)] += x * g
    return out[: _n(len(secs) * SECTION)]


def lufs(x):
    return EAR.loudness_curves(x, SR)["I"]


def gain_to(x, target):
    return SI.db(target - lufs(x))


# ------------------------------------------------------------------ files
def write_wav(path, x):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    sf.write(path, np.asarray(x, np.float32), SR, subtype="FLOAT")


def encode_ogg(wav, ogg, mono=False, q=5):
    os.makedirs(os.path.dirname(ogg), exist_ok=True)
    cmd = ["ffmpeg", "-y", "-hide_banner", "-loglevel", "error", "-i", wav, "-map_metadata", "-1", "-fflags", "+bitexact",
           "-flags:a", "+bitexact"]
    if mono:
        cmd += ["-ac", "1"]
    cmd += ["-c:a", "libvorbis", "-q:a", str(q), ogg]
    subprocess.run(cmd, check=True)


META = """fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 8
  defaultSettings:
    serializedVersion: 2
    loadType: {load}
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: {quality}
    conversionMode: 0
    preloadAudioData: {preload}
  platformSettingOverrides: {{}}
  forceToMono: 0
  normalize: 0
  loadInBackground: {bg}
  ambisonic: 0
  3D: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def write_meta(rel, load, quality, preload, bg):
    path = os.path.join(ROOT, rel + ".meta")
    if os.path.exists(path):
        return                                 # an existing file keeps its GUID and import settings
    guid = hashlib.md5(("cm-skybreak:" + rel).encode()).hexdigest()
    with open(path, "w") as f:
        f.write(META.format(guid=guid, load=load, quality=quality, preload=preload, bg=bg))


def folder_meta(rel):
    path = os.path.join(ROOT, rel + ".meta")
    if os.path.exists(path):
        return
    guid = hashlib.md5(("cm-skybreak-folder:" + rel).encode()).hexdigest()
    with open(path, "w") as f:
        f.write(f"fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n"
                "  userData: \n  assetBundleName: \n  assetBundleVariant: \n")


def limit(x, ceil=TP_CEIL):
    if EAR.true_peak(x) <= ceil:
        return x, 0.0
    y, gr = SI.tp_limit(x if x.ndim == 2 else np.stack([x, x], 1), ceil - 0.2)
    return (y if x.ndim == 2 else y[:, 0]), gr


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--wav-dir", default=os.path.expanduser("~/cm-audio-review-2026-11-18/_wav"))
    ap.add_argument("--only", choices=["music", "sfx"], default=None)
    a = ap.parse_args()
    wd = a.wav_dir
    manifest = dict(source="Skybreak (tools/audio/skybreak/score_intro.py): F minor, 80 BPM, motif C F G Ab",
                    bpm=BPM, beat=BEAT, bar=BAR, section_bars=SECTION_BARS, section_seconds=SECTION, tail_seconds=TAIL,
                    sample_rate=SR, sections=SECTIONS, motif_bars=MOTIF_BARS, plan=PLAN, layers=LAYERS, states=STATES,
                    reference_state=REFERENCE_STATE, music_lufs=MUSIC_LUFS, sting_lufs=STING_LUFS, files={})
    files = manifest["files"]
    if a.only in (None, "music"):
        print("stems", flush=True)
        stems = render_stems()
        ref = state_mix(stems, REFERENCE_STATE)
        g = gain_to(ref, MUSIC_LUFS)
        print(f"reference '{REFERENCE_STATE}' mix {lufs(ref):.2f} LUFS -> gain {20 * math.log10(g):+.2f} dB", flush=True)
        folder_meta(SCORE_DIR)
        for name, x in stems.items():
            y, gr = limit(x * g)
            stems[name] = y
            wav = f"{wd}/stems/{name}.wav"
            write_wav(wav, y)
            rel = f"{SCORE_DIR}/{name}.ogg"
            encode_ogg(wav, os.path.join(ROOT, rel), q=5)
            write_meta(rel, load=1, quality=0.5, preload=1, bg=1)
            files[rel] = dict(seconds=round(len(y) / SR, 4), samples=len(y), limiter_db=round(gr, 2),
                              wav_sha256=hashlib.sha256(np.asarray(y, np.float32).tobytes()).hexdigest()[:16])
        print("title, stings, hatch", flush=True)
        one = [("CMMusic/music-title", title_loop(), MUSIC_LUFS)]
        one += [(f"CMStings/sting-{k}", sting(k), STING_LUFS) for k in ("victory", "defeat", "draw")]
        one += [(f"CMStings/sting-hatch-{k}", hatch(k), STING_LUFS) for k in HATCH_TYPES]
        for rel0, x, target in one:
            y, gr = limit(x * gain_to(x, target))
            wav = f"{wd}/{rel0}.wav"
            write_wav(wav, y)
            rel = f"{MUSIC_DIR}/{rel0}.ogg"
            encode_ogg(wav, os.path.join(ROOT, rel), q=5)
            if "music-title" in rel0:
                write_meta(rel, load=2, quality=0.6, preload=0, bg=1)
            else:
                write_meta(rel, load=1, quality=0.6, preload=1, bg=1)
            files[rel] = dict(seconds=round(len(y) / SR, 4), samples=len(y), limiter_db=round(gr, 2),
                              wav_sha256=hashlib.sha256(np.asarray(y, np.float32).tobytes()).hexdigest()[:16])
    if a.only in (None, "sfx"):
        print("effects", flush=True)
        folder_meta(SFX_DIR)
        for name, count in SFX.items():
            for v in range(count):
                x = sfx(name, v)
                x = x / (np.abs(x).max() + 1e-12) * SI.db(-3.5)
                x, gr = limit(x, -2.0)
                fn = sfx_file(name, v, count)
                wav = f"{wd}/sfx/{fn}.wav"
                write_wav(wav, x)
                rel = f"{SFX_DIR}/{fn}.ogg"
                encode_ogg(wav, os.path.join(ROOT, rel), mono=True, q=4)
                write_meta(rel, load=0, quality=0.7, preload=1, bg=0)
                files[rel] = dict(seconds=round(len(x) / SR, 4), samples=len(x), limiter_db=round(gr, 2),
                                  wav_sha256=hashlib.sha256(np.asarray(x, np.float32).tobytes()).hexdigest()[:16])
    mp = os.path.join(HERE, "score-manifest.json")
    if a.only and os.path.exists(mp):
        old = json.load(open(mp))
        old["files"].update(files)
        files = old["files"]
        manifest["files"] = files
    json.dump(manifest, open(mp, "w"), indent=1, sort_keys=True)
    print(f"wrote {len(files)} files; manifest {os.path.relpath(mp, ROOT)}")


if __name__ == "__main__":
    main()
