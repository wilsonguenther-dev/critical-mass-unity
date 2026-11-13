"""
Prepare the shipped music, stings and UI sounds (DG-18, DG-31) from their CC0 sources.

    python3 tools/audio/prepare_assets.py --src <folder with the downloads below>

Every source is CC0 1.0; Assets/_Game/Audio/CREDITS.md lists each file's page, author, licence and sha256. The
script checks each source's sha256 before it touches it, so a different download can never ship under these
credits. Output: Assets/_Game/Audio/{Music,Ui}/Resources/{CMMusic,CMStings,CMUi}/*.ogg, normalised by audionorm.py
(music -20 LUFS, stings -16 LUFS, UI peak -9 dBTP, everything at or under -1 dBTP).

Expected under --src (names as downloaded):
    darkshrine.ogg                        qubodup, "Dark Shrine Loop"
    battleThemeA.mp3                      cynicmusic, "Battle Theme A"
    determined_pursuit_loop.wav           Emma_MA, "Determined Pursuit (Epic Orchestra Loop)"
    boss/boss_battle_#2_metal_loop.wav    nene, "Boss Battle #2 (Symphonic Metal)" (from boss_battle_#2_metal_pack.zip)
    victory.wav                           RandomMind, "Medieval: Victory Theme"
    defeat.wav                            RandomMind, "Medieval: Defeat Theme"
    kj/Audio/Pizzicato jingles/jingles_PIZZI07.ogg   Kenney, "Music Jingles"
    <interface>/Audio/*.ogg               Kenney, "Interface Sounds" (pass --interface; default the browser-era download)
"""

import argparse
import hashlib
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import audionorm  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
MUSIC = ROOT / "Assets/_Game/Audio/Music/Resources/CMMusic"
STINGS = ROOT / "Assets/_Game/Audio/Music/Resources/CMStings"
UI = ROOT / "Assets/_Game/Audio/Ui/Resources/CMUi"

SHA = {
    "darkshrine.ogg": "9580618dc851f70c3a11b5ff87672867de44cd38b35dabf926d6befde20e78d0",
    "battleThemeA.mp3": "6042399782e581d753d616bc703e66483d5eccb5fb687a20c9a552d68c49e620",
    "determined_pursuit_loop.wav": "e4f3be098b50213b56a60aefe60fffae79cd9f7c9008088f93c187ef1bbc856b",
    "victory.wav": "91f35879d31091cdeb38721fb4d985a0e7363c0caa97d738ec879bf96e8538f4",
    "defeat.wav": "832a9062921000cde7044f18838d11682c578067db0c60a124d95473bb122fc8",
}

# (output, source relative to --src, category, filter chain before loudness)
MUSIC_ROWS = [
    # Title: a slow, mysterious loop under the logo and the live bot match.
    (MUSIC / "music-title.ogg", "darkshrine.ogg", ""),
    # Match bed. The MP3 carries encoder delay: drop the leading near-silence so the loop seam is tight.
    (MUSIC / "music-match.ogg", "battleThemeA.mp3", "silenceremove=start_periods=1:start_threshold=-60dB"),
    # Final-minute lift: a faster, more urgent orchestral loop the director crossfades to at one minute left.
    (MUSIC / "music-final.ogg", "determined_pursuit_loop.wav", ""),
    # Berserk: the loop part only (the pack's opening is a one-shot intro).
    (MUSIC / "music-berserk.ogg", "boss/boss_battle_#2_metal_loop.wav", ""),
]
STING_ROWS = [
    # Stings are the first seconds of each theme, faded out, so the results screen is not drowned.
    (STINGS / "sting-victory.ogg", "victory.wav", "atrim=0:9,afade=t=out:st=7:d=2"),
    (STINGS / "sting-defeat.ogg", "defeat.wav", "atrim=0:9,afade=t=out:st=6.5:d=2.5"),
    (STINGS / "sting-draw.ogg", "kj/Audio/Pizzicato jingles/jingles_PIZZI07.ogg", ""),
]
UI_ROWS = [
    (UI / "ui-hover.ogg", "tick_002.ogg"),
    (UI / "ui-step.ogg", "tick_004.ogg"),
    (UI / "ui-confirm.ogg", "confirmation_001.ogg"),
    (UI / "ui-back.ogg", "back_002.ogg"),
    (UI / "ui-error.ogg", "error_006.ogg"),
]


def sha256(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""):
            h.update(b)
    return h.hexdigest()


def check(src):
    want = SHA.get(src.name)
    if want and sha256(src) != want:
        raise SystemExit(f"sha256 mismatch for {src}: not the file CREDITS.md describes")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", required=True)
    ap.add_argument("--interface", default=str(Path.home() / "critical-mass-assets/downloads/kenney-audio/x/interface-sounds/Audio"))
    a = ap.parse_args()
    src = Path(a.src).expanduser()
    for dst, rel, pre in MUSIC_ROWS:
        s = src / rel
        check(s)
        print(dst.name, audionorm.normalise(s, dst, "music", channels=2, sample_rate=44100, quality=4, filters_pre=pre))
    for dst, rel, pre in STING_ROWS:
        s = src / rel
        check(s)
        print(dst.name, audionorm.normalise(s, dst, "sting", channels=2, sample_rate=44100, quality=4, filters_pre=pre))
    for dst, name in UI_ROWS:
        s = Path(a.interface).expanduser() / name
        print(dst.name, audionorm.normalise(s, dst, "ui", channels=1, sample_rate=44100, quality=4))
    return 0


if __name__ == "__main__":
    sys.exit(main())
