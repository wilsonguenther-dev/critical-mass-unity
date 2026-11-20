#!/usr/bin/env python3
"""
The Skybreak listening pack (b15): renders, from the WAVs build_game_score.py wrote (--wav-dir), a "match in sound"
mixdown that runs the game's own director rules offline (sections on the 24 s grid with their tails ringing out, mood
changes at the next bar line, a form change as a one-beat equal-power crossfade, layer gains up over a beat and down
over 1.5 s, the hatch stinger on the next beat with the score ducked under it, the end sting, then the title loop under
the results), plus a 20 s loop of every mix state, every sting and every effect, as AAC .m4a, and a plain README.

  python3 tools/audio/skybreak/listening_pack.py [--wav-dir DIR] [--out DIR]

Free tools only (numpy, soundfile, ffmpeg's own AAC encoder). Opens nothing.
"""
import argparse, math, os, subprocess, sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import build_game_score as G      # noqa: E402  the same tables (STATES, PLAN, LAYERS) and grid
import soundfile as sf            # noqa: E402

SR, BEAT, BAR, SECTION, TAIL = G.SR, G.BEAT, G.BAR, G.SECTION, G.TAIL
BASE, LIFT, RESULTS = 0.84, 1.0, 0.5           # MusicDirector.BaseGain, LiftGain, ResultsGain
SFX_GAIN = 0.45                                 # effects at about their table gain, under the score


def load(path):
    x, sr = sf.read(path, dtype="float32", always_2d=True)
    assert sr == SR, path
    return x if x.shape[1] == 2 else np.repeat(x, 2, 1)


def next_line(origin, step, t, margin=0.05):
    return origin + (math.floor((t + margin - origin) / step) + 1) * step


def hatch_duck(t, start, length):
    floor = 0.15
    d = np.ones_like(t)
    a = (t >= start - 0.12) & (t < start)
    d[a] = 1 - (1 - floor) * (t[a] - (start - 0.12)) / 0.12
    end = start + max(0.5, length - 1.0)
    d[(t >= start) & (t < end)] = floor
    r = (t >= end) & (t < end + BAR)
    d[r] = floor + (1 - floor) * (t[r] - end) / BAR
    return d


def ramp(times, values, n, up=BEAT, down=1.5, start=None):
    """A gain lane: each (time, target) moves toward the target from that time, up over `up` s, down over `down` s."""
    out = np.empty(n, np.float32)
    g = values[0] if start is None else start
    k = 0
    ev = sorted(zip(times, values))
    tgt = ev[0][1]
    for i0 in range(0, n, 256):
        t = i0 / SR
        while k < len(ev) and ev[k][0] <= t:
            tgt = ev[k][1]; k += 1
        step = 256 / SR / (up if tgt > g else down)
        g = min(tgt, g + step) if tgt > g else max(tgt, g - step)
        out[i0:i0 + 256] = g
    return out


def score_timeline(stems, moods, length):
    """moods: [(t, state, colour)] in score time (0 = first downbeat). Returns the score (2 ch) at gain 1."""
    n = int(length * SR) + int(TAIL * SR)
    out = np.zeros((n, 2), np.float32)
    # the mood in force at each bar (a change applies at the next bar line after it is asked for)
    applied = []
    for t, st, col in moods:
        at = 0.0 if t <= 0 else next_line(0.0, BAR, t)
        applied.append((at, st, col))

    def mood_at(t):
        m = applied[0]
        for a in applied:
            if a[0] <= t + 1e-9:
                m = a
        return m

    def variant(layer, st, col):
        if layer == "bed":
            return "feed" if col == "feed" else "minor"
        if layer == "rhythm":
            return G.STATES[st]["rhythm"][0]
        return "corrupt" if st == "berserk" else (col if col in ("feed", "corrupt") else "minor")

    def gain(layer, st):
        s = G.STATES[st]
        return s["rhythm"][1] if layer == "rhythm" else s[layer]

    steps = int(math.ceil(length / SECTION))
    fade = int(BEAT * SR)
    for layer in ("bed", "rhythm", "theme"):
        lane = np.zeros((n, 2), np.float32)
        for k in range(steps):
            sec = G.PLAN[k % len(G.PLAN)]
            t0 = k * SECTION
            # the form at the section start; a change mid-section at a bar line (bars 1..7, after the old tail)
            cur = variant(layer, *mood_at(t0)[1:])
            seg_start = 0.0
            pieces = []
            for b in range(1, 8):
                tb = t0 + b * BAR
                if b * BAR < TAIL + 0.05:
                    continue
                v = variant(layer, *mood_at(tb)[1:])
                if v != cur:
                    pieces.append((seg_start, b * BAR, cur)); cur = v; seg_start = b * BAR
            pieces.append((seg_start, SECTION + TAIL, cur))
            for j, (a, e, v) in enumerate(pieces):
                x = stems[f"sb-{sec}-{layer}-{v}"]
                ia, ie = int(a * SR), min(len(x), int(e * SR) + (fade if j < len(pieces) - 1 else 0))
                seg = x[ia:ie].copy()
                if j > 0:                                       # equal-power fade in over the beat
                    w = np.sin(np.linspace(0, np.pi / 2, min(fade, len(seg))))[:, None]
                    seg[:len(w)] *= w
                if j < len(pieces) - 1:                         # and the old form out over the same beat
                    w = np.cos(np.linspace(0, np.pi / 2, fade))[:, None]
                    seg[-fade:] *= w
                o = int((t0 + a) * SR)
                m = min(len(seg), n - o)
                if m > 0:
                    lane[o:o + m] += seg[:m]
        times = [a[0] for a in applied]
        g = ramp(times, [gain(layer, a[1]) for a in applied], n, start=gain(layer, applied[0][1]))
        out += lane * g[:, None]
    return out[: int(length * SR)]


def place(buf, x, t, g=1.0):
    i = int(t * SR)
    m = min(len(x), len(buf) - i)
    if m > 0:
        buf[i:i + m] += x[:m] * g


def fade_out(x, sec):
    n = min(len(x), int(sec * SR))
    x = x.copy()
    x[-n:] *= np.linspace(1, 0, n)[:, None]
    return x


def m4a(x, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    peak = float(np.abs(x).max())
    if peak > 0.84:                             # about -1.5 dBFS, so the AAC encode stays under -1 dBTP
        x = x * (0.84 / peak)
    tmp = path + ".tmp.wav"
    sf.write(tmp, np.asarray(x, np.float32), SR, subtype="FLOAT")
    subprocess.run(["ffmpeg", "-y", "-hide_banner", "-loglevel", "error", "-i", tmp, "-c:a", "aac", "-b:a", "192k",
                    "-map_metadata", "-1", path], check=True)
    os.remove(tmp)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--wav-dir", default=os.path.expanduser("~/cm-audio-review-2026-11-18/_wav"))
    ap.add_argument("--out", default=os.path.expanduser("~/cm-audio-review-2026-11-18"))
    a = ap.parse_args()
    wd, out = a.wav_dir, a.out
    stems = {f[:-4]: load(os.path.join(wd, "stems", f)) for f in sorted(os.listdir(os.path.join(wd, "stems")))}
    title = load(os.path.join(wd, "CMMusic", "music-title.wav"))
    sting = {f[:-4]: load(os.path.join(wd, "CMStings", f)) for f in os.listdir(os.path.join(wd, "CMStings"))}
    fx = {f[:-4]: load(os.path.join(wd, "sfx", f)) for f in os.listdir(os.path.join(wd, "sfx"))}

    # ---- 1. a match in sound: menu, build-up, contested, the egg, the hatch (grove, fed), leading, final minute, victory
    menu = 12.5                                  # the title loop under the menu; the score's first downbeat here
    moods = [(0, "calm", "minor"), (25.0, "contested", "minor"), (49.0, "egg", "minor"),
             (78.0, "leading", "feed"), (106.0, "final", "feed")]
    end = 138.0                                  # the match ends (score time)
    hatch_t = next_line(0.0, BEAT, 75.5, 0.06)
    score = score_timeline(stems, moods, end + 1.0)
    st = sting["sting-victory"]
    total = menu + end + len(st) / SR + 1.0 + 14.0
    n = int(total * SR)
    mix = np.zeros((n, 2), np.float32)
    t = np.arange(n) / SR
    # the title loop: the menu, fading out over 1.5 s as the score comes in
    tl = np.tile(title, (int(math.ceil(total / (len(title) / SR))) + 1, 1))[:n]
    tg = np.where(t < menu - 1.25, BASE, np.where(t < menu + 0.25, BASE * np.cos(np.clip((t - (menu - 1.25)) / 1.5, 0, 1) * np.pi / 2), 0))
    bed_at = menu + end + len(st) / SR + 1.0     # the results bed: the title loop again, quietly, after the sting
    tg = np.where(t >= bed_at, RESULTS * np.sin(np.clip((t - bed_at) / 1.5, 0, 1) * np.pi / 2), tg)
    mix += tl * tg[:, None]
    # the score: base gain, the final-minute lift, the hatch duck, the end fade, the score fades in over 1.5 s
    sc = np.zeros((n, 2), np.float32)
    place(sc, score, menu)
    ts = t - menu
    lift = np.where(ts >= next_line(0, BAR, 106.0), np.minimum(LIFT, BASE + (ts - next_line(0, BAR, 106.0)) / 0.6 * (LIFT - BASE)), BASE)
    lvl = np.clip(ts / 1.5, 0, 1) * np.clip(1 - (ts - end) / 0.8, 0, 1)
    duck = hatch_duck(ts, hatch_t, len(sting["sting-hatch-grove"]) / SR)
    mix += sc * (lift * lvl * duck)[:, None]
    place(mix, sting["sting-hatch-grove"], menu + hatch_t, BASE)
    place(mix, st, menu + end, 1.0)
    # a few effects where the game would play them (your own: dodge, a wall, ore, the hut, a bridge; a threat behind)
    for when, name in [(6.0, "sb_ore_hit_0"), (6.7, "sb_ore_hit_1"), (7.4, "sb_ore_hit_2"), (8.6, "sb_ore_bank"),
                       (14.0, "sb_hut_level"), (20.5, "sb_wall_place_0"), (21.6, "sb_block_place_1"),
                       (29.0, "sb_dodge"), (31.2, "sb_wall_hit_0"), (31.7, "sb_wall_hit_1"), (33.0, "sb_wall_break"),
                       (41.0, "sb_ladder_place"), (42.2, "sb_climb_step_0"), (42.6, "sb_climb_step_1"),
                       (43.0, "sb_climb_step_0"), (58.0, "sb_pit_dig"), (63.0, "sb_bridge_place"),
                       (96.0, "sb_behind_tick"), (98.0, "sb_dodge"), (112.0, "sb_wall_break")]:
        place(mix, fx[name], menu + when, SFX_GAIN)
    m4a(mix, os.path.join(out, "01-match-in-sound.m4a"))
    print(f"match in sound: {total:.1f} s; hatch stinger at {menu + hatch_t:.2f} s", flush=True)

    # ---- 2. every mix state, 20 s (the steady state from section A, then B)
    k = 2
    for name, col in [("calm", "minor"), ("contested", "minor"), ("leading", "minor"), ("trailing", "minor"),
                      ("rally", "minor"), ("egg", "minor"), ("final", "minor"), ("berserk", "corrupt"),
                      ("contested", "feed"), ("leading", "feed"), ("final", "feed")]:
        x = G.state_mix(stems, name, col, sections=["A", "B"])[: int(20 * SR)]
        tag = name + ("-fed" if col == "feed" else "")
        m4a(fade_out(x * BASE, 1.0), os.path.join(out, "states", f"{k:02d}-{tag}.m4a"))
        k += 1
    m4a(fade_out(np.concatenate([title, title])[: int(20 * SR)] * BASE, 1.0), os.path.join(out, "states", f"{k:02d}-menu-title-loop.m4a"))
    # ---- 3. stings and hatch stingers
    for s in sorted(sting):
        m4a(sting[s], os.path.join(out, "stings", f"{s}.m4a"))
    # ---- 4. every effect, and all of them one after another
    reel = []
    for f in sorted(fx):
        m4a(np.concatenate([np.zeros((int(0.2 * SR), 2), np.float32), fx[f], np.zeros((int(0.4 * SR), 2), np.float32)]),
            os.path.join(out, "effects", f"{f}.m4a"))
        reel += [fx[f], np.zeros((int(0.8 * SR), 2), np.float32)]
    m4a(np.concatenate(reel), os.path.join(out, "effects", "00-all-effects-in-a-row.m4a"))
    print("listening pack written to", out)


if __name__ == "__main__":
    main()
