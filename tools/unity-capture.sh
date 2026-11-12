#!/usr/bin/env bash
# Gameplay-camera captures (CriticalMass.Editor.CaptureTool.Run): 16v16 bot match, fixed seed (GameConfig default 1337),
# N simulated seconds, then 1920x1080 PNGs of the Azure base, the hill, the shrine and a wide view of the whole map plus
# capture.json in OUT_DIR. YAW (optional, degrees) overrides the camera heading for this capture only (default: the game's
# 45; 0 = true north). Needs a GPU (no -nographics). Usage: tools/unity-capture.sh OUT_DIR [SIM_SECONDS] [YAW]
set -euo pipefail
OUT="${1:?usage: unity-capture.sh OUT_DIR [SIM_SECONDS]}"
SECONDS_ARG="${2:-30}"
YAW_ARGS=()
if [[ -n "${3:-}" ]]; then YAW_ARGS=(-captureYaw "$3"); fi
source "$(dirname "${BASH_SOURCE[0]}")/_unity-env.sh"
LOG="$ROOT/Logs/capture.log"
mkdir -p "$OUT"
set +e
"$UNITY" -batchmode -projectPath "$ROOT" -executeMethod CriticalMass.Editor.CaptureTool.Run \
  -captureOut "$OUT" -captureSeconds "$SECONDS_ARG" ${YAW_ARGS[@]+"${YAW_ARGS[@]}"} -logFile "$LOG"
code=$?
set -e
n="$(ls "$OUT"/capture-*.png 2>/dev/null | wc -l | tr -d ' ')"
echo "unity-capture: exit=$code pngs=$n out=$OUT (log: $LOG)"
if [[ $code -ne 0 ]]; then exit "$code"; fi
[[ "$n" -ge 4 ]] || { echo "unity-capture: FAILED, fewer than 4 PNGs" >&2; exit 1; }
