"""
Shared loudness helpers for the Critical Mass audio tools (DG-18, DG-31).

Every shipped clip goes through normalise(): a two-pass EBU R128 loudness normalisation with ffmpeg's loudnorm
filter (first pass measures, second pass applies one linear gain), then an Ogg Vorbis encode, then a re-measure of
the encoded file so the true-peak ceiling is proven on the bytes that ship, not on the intermediate.

Targets per category (integrated loudness, LUFS; true peak, dBTP). The gate (tools/audio-loudness-check.sh) reads
the same table from here, so the two can never disagree:

    voice   -16 LUFS   (announcer and narrator lines: the loudest thing in the mix, on purpose)
    sting   -16 LUFS   (victory, defeat and draw stings)
    music   -20 LUFS   (looping beds sit 4 LU under the voice so a callout always reads)
    ui      peak only  (clicks are shorter than one 400 ms loudness block; they are peak-normalised to -9 dBTP)

All categories: true peak at or under -1.0 dBTP on the final file (the gate's hard rule).

Requires ffmpeg and ffprobe on PATH. Pure standard library otherwise.
"""

import json
import re
import subprocess
from pathlib import Path

TRUE_PEAK_CEILING = -1.0          # dBTP, the gate's hard rule
TRUE_PEAK_TARGET = -2.0           # dBTP asked of loudnorm, leaving 1 dB for the Vorbis encode
LOUDNESS_TOLERANCE = 1.5          # LU, gate tolerance around a category target
MIN_LOUDNESS_SECONDS = 1.0        # shorter clips are checked for true peak only
UI_PEAK = -9.0                    # dBTP for peak-normalised UI clicks

TARGETS = {
    "voice": -16.0,
    "sting": -16.0,
    "music": -20.0,
    "ui": None,
}


def run(cmd):
    return subprocess.run(cmd, capture_output=True, text=True, check=False)


def duration(path):
    out = run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", str(path)])
    try:
        return float(out.stdout.strip())
    except ValueError:
        return 0.0


def measure(path):
    """Integrated loudness (LUFS), loudness range (LU) and true peak (dBTP) of a file, from ffmpeg ebur128."""
    out = run(["ffmpeg", "-hide_banner", "-nostats", "-i", str(path), "-af", "ebur128=peak=true", "-f", "null", "-"])
    text = out.stderr
    summary = text[text.rfind("Summary:"):] if "Summary:" in text else text

    def grab(pattern):
        m = re.search(pattern, summary, re.S)
        return float(m.group(1)) if m else float("nan")

    return {
        "I": grab(r"Integrated loudness:.*?I:\s*(-?[\d.]+|-inf)\s*LUFS"),
        "LRA": grab(r"Loudness range:.*?LRA:\s*(-?[\d.]+)\s*LU"),
        "TP": grab(r"True peak:.*?Peak:\s*(-?[\d.]+|-inf)\s*dBFS"),
    }


def _loudnorm_pass1(src, target, filters_pre):
    af = (filters_pre + "," if filters_pre else "") + \
        f"loudnorm=I={target}:TP={TRUE_PEAK_TARGET}:LRA=20:print_format=json"
    out = run(["ffmpeg", "-hide_banner", "-nostats", "-i", str(src), "-af", af, "-f", "null", "-"])
    m = re.search(r"\{[^{}]*\"input_i\"[^{}]*\}", out.stderr, re.S)
    if not m:
        raise RuntimeError("loudnorm pass 1 failed for %s:\n%s" % (src, out.stderr[-2000:]))
    return json.loads(m.group(0))


def normalise(src, dst, category, channels=1, sample_rate=44100, quality=4, filters_pre=""):
    """
    Normalise src to the category's target and write an Ogg Vorbis file at dst.
    filters_pre: optional ffmpeg filter chain applied before loudness (trims, fades).
    Returns the re-measured stats of dst.
    """
    dst = Path(dst)
    dst.parent.mkdir(parents=True, exist_ok=True)
    target = TARGETS[category]
    if target is None:
        # Peak normalisation for clips shorter than a loudness block: measure the true peak, apply one gain.
        tmp = dst.with_suffix(".tmp.wav")
        chain = (filters_pre + "," if filters_pre else "") + "anull"
        r = run(["ffmpeg", "-y", "-hide_banner", "-nostats", "-i", str(src), "-af", chain, "-ac", str(channels),
                 "-ar", str(sample_rate), str(tmp)])
        if r.returncode != 0:
            raise RuntimeError(r.stderr[-2000:])
        tp = measure(tmp)["TP"]
        gain = UI_PEAK - tp
        r = run(["ffmpeg", "-y", "-hide_banner", "-nostats", "-i", str(tmp), "-af", f"volume={gain:.2f}dB",
                 "-c:a", "libvorbis", "-q:a", str(quality), "-map_metadata", "-1", str(dst)])
        tmp.unlink(missing_ok=True)
        if r.returncode != 0:
            raise RuntimeError(r.stderr[-2000:])
        return measure(dst)

    m = _loudnorm_pass1(src, target, filters_pre)
    af = (filters_pre + "," if filters_pre else "") + (
        f"loudnorm=I={target}:TP={TRUE_PEAK_TARGET}:LRA=20:"
        f"measured_I={m['input_i']}:measured_TP={m['input_tp']}:measured_LRA={m['input_lra']}:"
        f"measured_thresh={m['input_thresh']}:offset={m['target_offset']}:linear=true:print_format=summary"
    )
    r = run(["ffmpeg", "-y", "-hide_banner", "-nostats", "-i", str(src), "-af", af, "-ac", str(channels),
             "-ar", str(sample_rate), "-c:a", "libvorbis", "-q:a", str(quality), "-map_metadata", "-1", str(dst)])
    if r.returncode != 0:
        raise RuntimeError(r.stderr[-2000:])
    stats = measure(dst)
    # loudnorm falls back to dynamic mode when a linear gain would break the peak target; if the encode still
    # overshoots the ceiling, pull the whole file down by the overshoot (a quieter file beats a clipped one).
    if stats["TP"] > TRUE_PEAK_CEILING - 0.2:
        fix = TRUE_PEAK_CEILING - 0.5 - stats["TP"]
        tmp = dst.with_suffix(".fix.ogg")
        r = run(["ffmpeg", "-y", "-hide_banner", "-nostats", "-i", str(dst), "-af", f"volume={fix:.2f}dB",
                 "-c:a", "libvorbis", "-q:a", str(quality), "-map_metadata", "-1", str(tmp)])
        if r.returncode != 0:
            raise RuntimeError(r.stderr[-2000:])
        tmp.replace(dst)
        stats = measure(dst)
    return stats


def category_of(path):
    """The loudness category of a shipped clip, from its folder (CMVoice, CMMusic, CMStings, CMUi)."""
    parts = {p.lower() for p in Path(path).parts}
    if "cmvoice" in parts:
        return "voice"
    if "cmstings" in parts:
        return "sting"
    if "cmmusic" in parts:
        return "music"
    if "cmui" in parts:
        return "ui"
    return None
