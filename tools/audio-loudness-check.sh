#!/usr/bin/env bash
# Loudness gate (DG-18): true peak <= -1 dBTP on every audio file under Assets/_Game/Audio (or the paths given), and
# integrated loudness on target for voice, stings and music. Plain ffmpeg; never starts Unity.
#   tools/audio-loudness-check.sh [path ...]     exit 0 pass, 1 fail, 2 ffmpeg missing
set -euo pipefail
cd "$(dirname "$0")/.."
exec python3 tools/audio/loudness_check.py "$@"
