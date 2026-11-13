"""
Bake the announcer and narrator lines (DG-18, DG-19) with the local Kokoro-82M voice engine (Apache-2.0 model,
stock voices only: never an imitation of a real person).

    nice -n 10 ~/.venvs/kokoro/bin/python tools/audio/bake_voice.py \
        --announcer Assets/_Game/Content/Lore/announcer.json --story Assets/_Game/Content/Lore/story.json

    # the voice audition the owner picks from (same line, three deep male stock voices, 0.9 speed):
    nice -n 10 ~/.venvs/kokoro/bin/python tools/audio/bake_voice.py --audition <folder>

Writes Assets/_Game/Audio/Voice/Resources/CMVoice/<line id>.ogg (mono, 24 kHz, -16 LUFS, true peak <= -1 dBTP) and
Assets/_Game/Audio/Voice/voice-manifest.json (id, text, voice, speed, milliseconds, sha1 of voice|speed|text).
Only lines whose text, voice or speed changed are re-baked (--force re-bakes all). One line at a time on the CPU.

Requires ~/.venvs/kokoro (kokoro, soundfile, numpy) and ffmpeg on PATH:
    uv venv ~/.venvs/kokoro --python 3.12 && VIRTUAL_ENV=~/.venvs/kokoro uv pip install kokoro soundfile
"""

import argparse
import hashlib
import json
import sys
import tempfile
from pathlib import Path

import numpy as np
import soundfile as sf

sys.path.insert(0, str(Path(__file__).resolve().parent))
import audionorm  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/_Game/Audio/Voice/Resources/CMVoice"
MANIFEST = ROOT / "Assets/_Game/Audio/Voice/voice-manifest.json"
RATE = 24000

# The narrator voice. bm_george is the 2026-11-12 decision D2 default and the best guess until the owner picks from
# the audition (am_onyx, am_fenrir, bm_george); change it here and run again to re-bake every line.
VOICE = "bm_george"
NARRATOR_SPEED = 0.9      # story.json: "deep, warm and slow"
ANNOUNCER_SPEED = 0.95    # story.json: "the same voice calls the match, read a touch quicker"

AUDITION_VOICES = ["am_onyx", "am_fenrir", "bm_george"]
AUDITION_LINE = "Far away, a Kaiju egg sleeps. It remembers every meal. Hold the hill."

# Used only when the words pack is not on this branch (DG-04 owns the real lines); ids match announcer.json.
FALLBACK_ANNOUNCER = [
    ("an-01", "Banners up. The hill is open."),
    ("an-02", "Hill taken. Now keep it."),
    ("an-03", "They have the hill. Take it back."),
    ("an-04", "Hill lost."),
    ("an-06", "One minute left. Make it count."),
    ("an-07", "The egg is cracking!"),
    ("an-08", "The Kaiju sides with Azure!"),
    ("an-09", "The Kaiju sides with Crimson!"),
    ("an-12", "Berserk! It wants everyone."),
    ("an-19", "Victory. The hill is yours."),
    ("an-20", "Defeat. Regroup, and climb again."),
    ("an-21", "A draw. The hill will take a rematch."),
]

_pipelines = {}


def pipeline(voice):
    from kokoro import KPipeline  # imported late: --help works without the venv
    lang = "b" if voice.startswith("b") else "a"
    if lang not in _pipelines:
        _pipelines[lang] = KPipeline(lang_code=lang, repo_id="hexgrad/Kokoro-82M")
    return _pipelines[lang]


def synth(text, voice, speed):
    chunks = [np.asarray(audio, dtype=np.float32) for _, _, audio in pipeline(voice)(text, voice=voice, speed=speed)]
    if not chunks:
        raise RuntimeError("no audio for: " + text)
    pad = np.zeros(int(RATE * 0.08), dtype=np.float32)
    return np.concatenate([pad] + chunks + [pad])


def render(text, voice, speed, dst):
    with tempfile.TemporaryDirectory() as tmp:
        raw = Path(tmp) / "raw.wav"
        sf.write(raw, synth(text, voice, speed), RATE)
        # Trim the engine's leading and trailing near-silence to 80 ms so a callout lands on its event.
        trim = ("silenceremove=start_periods=1:start_threshold=-50dB:start_silence=0.08,"
                "areverse,silenceremove=start_periods=1:start_threshold=-50dB:start_silence=0.12,areverse,"
                # Light broadcast-style compression so every line meets -16 LUFS without its peaks breaking -1 dBTP.
                "acompressor=threshold=-20dB:ratio=3:attack=5:release=90:makeup=1")
        return audionorm.normalise(raw, dst, "voice", channels=1, sample_rate=RATE, quality=3, filters_pre=trim)


def key(voice, speed, text):
    return hashlib.sha1(f"{voice}|{speed}|{text}".encode("utf-8")).hexdigest()[:12]


def lines(announcer, story):
    out = []
    if announcer and Path(announcer).exists():
        for row in json.loads(Path(announcer).read_text())["lines"]:
            out.append((row["id"], row["text"], ANNOUNCER_SPEED, "announcer"))
    else:
        print("words pack announcer.json not found: baking the 12-line fallback set", file=sys.stderr)
        out += [(i, t, ANNOUNCER_SPEED, "announcer") for i, t in FALLBACK_ANNOUNCER]
    if story and Path(story).exists():
        s = json.loads(Path(story).read_text())
        if s.get("titleVo"):
            out.append(("story-title", s["titleVo"], NARRATOR_SPEED, "narrator"))
        for c in s["cards"]:
            out.append(("story-" + c["id"], c["caption"], NARRATOR_SPEED, "narrator"))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--announcer")
    ap.add_argument("--story")
    ap.add_argument("--audition")
    ap.add_argument("--force", action="store_true")
    a = ap.parse_args()

    if a.audition:
        folder = Path(a.audition).expanduser()
        for v in AUDITION_VOICES:
            st = render(AUDITION_LINE, v, NARRATOR_SPEED, folder / f"narrator-test-{v}.ogg")
            print(f"{v}: {st}")
        return 0

    old = {}
    if MANIFEST.exists():
        old = {r["id"]: r for r in json.loads(MANIFEST.read_text())["lines"]}
    rows = []
    for lid, text, speed, role in lines(a.announcer, a.story):
        k = key(VOICE, speed, text)
        dst = OUT / f"{lid}.ogg"
        prev = old.get(lid)
        if not a.force and prev and prev.get("key") == k and dst.exists():
            rows.append(prev)
            continue
        st = render(text, VOICE, speed, dst)
        ms = round(audionorm.duration(dst) * 1000)
        print(f"{lid} {ms} ms I={st['I']} TP={st['TP']}  {text}")
        rows.append({"id": lid, "role": role, "text": text, "voice": VOICE, "speed": speed, "ms": ms, "key": k})
    MANIFEST.parent.mkdir(parents=True, exist_ok=True)
    MANIFEST.write_text(json.dumps({
        "schema": "cm.audio.voice/1",
        "engine": "Kokoro-82M (hexgrad/Kokoro-82M, Apache-2.0), stock voice",
        "lines": rows,
    }, indent=2) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
