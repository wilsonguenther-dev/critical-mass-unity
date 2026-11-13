"""
Loudness gate (DG-18): every audio file under the given folders (default Assets/_Game/Audio) is measured with
ffmpeg's EBU R128 scanner (ebur128, peak=true).

Hard rule for every file: true peak at or under -1.0 dBTP.
For files in a known category folder (CMVoice, CMStings, CMMusic) that are at least 1.0 s long: integrated loudness
within 1.5 LU of the category target in audionorm.TARGETS. UI clicks (CMUi) and files outside those folders are
checked for true peak only.

    python3 tools/audio/loudness_check.py [folder ...]      # exit 0 = all pass, 1 = a failure, 2 = no ffmpeg
Prints one line per file and a JSON summary on the last line.
"""

import json
import math
import shutil
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import audionorm  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
EXTS = {".ogg", ".wav", ".mp3", ".flac", ".aif", ".aiff"}


def is_lfs_pointer(p):
    try:
        with open(p, "rb") as f:
            return f.read(40).startswith(b"version https://git-lfs")
    except OSError:
        return False


def main(argv):
    if not shutil.which("ffmpeg") or not shutil.which("ffprobe"):
        print("ffmpeg and ffprobe are required", file=sys.stderr)
        return 2
    roots = [Path(a) for a in argv] or [ROOT / "Assets/_Game/Audio"]
    files = sorted({p for r in roots for p in (r.rglob("*") if r.is_dir() else [r]) if p.suffix.lower() in EXTS})
    fails, checked = [], 0
    for p in files:
        rel = p.relative_to(ROOT) if p.is_absolute() and ROOT in p.parents else p
        if is_lfs_pointer(p):
            fails.append(str(rel))
            print(f"FAIL {rel}: Git LFS pointer, not audio (run git lfs pull)")
            continue
        st = audionorm.measure(p)
        dur = audionorm.duration(p)
        cat = audionorm.category_of(p)
        target = audionorm.TARGETS.get(cat) if cat else None
        problems = []
        if math.isnan(st["TP"]):
            problems.append("no true-peak reading")
        elif st["TP"] > audionorm.TRUE_PEAK_CEILING:
            problems.append(f"true peak {st['TP']} dBTP > {audionorm.TRUE_PEAK_CEILING}")
        if target is not None and dur >= audionorm.MIN_LOUDNESS_SECONDS:
            if math.isnan(st["I"]) or abs(st["I"] - target) > audionorm.LOUDNESS_TOLERANCE:
                problems.append(f"integrated {st['I']} LUFS, target {target} +/- {audionorm.LOUDNESS_TOLERANCE}")
        checked += 1
        tag = "FAIL" if problems else "ok  "
        print(f"{tag} {rel}  [{cat or 'other'}] {dur:.2f}s I={st['I']} LUFS TP={st['TP']} dBTP"
              + (("  <- " + "; ".join(problems)) if problems else ""))
        if problems:
            fails.append(str(rel))
    print(json.dumps({"checked": checked, "files": len(files), "failed": fails}))
    return 1 if fails or not files else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
