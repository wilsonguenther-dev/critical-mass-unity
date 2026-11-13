#!/usr/bin/env bash
# Owner walk 1 capture (CriticalMass.Editor.Flow.OwnerWalkCaptureTool.Run): every owner-facing screen at 3024x1964 and
# 1512x982 (14-inch MacBook Pro, notch as the safe-area inset), 1920x1080, 2560x1080 and 1280x800; PNGs plus walk.log.
# Needs a GPU (no -nographics). Usage: tools/unity-owner-walk-capture.sh OUT_DIR [RES_LIST e.g. 3024x1964,1920x1080]
set -euo pipefail
OUT="${1:?usage: unity-owner-walk-capture.sh OUT_DIR [RES_LIST]}"
RES="${2:-}"
source "$(dirname "${BASH_SOURCE[0]}")/_unity-env.sh"
LOG="$ROOT/Logs/owner-walk-capture.log"
mkdir -p "$OUT"
ABS="$(cd "$OUT" && pwd)"
set +e
# Through the machine lock (cm-heavy unity); never call "$UNITY" bare.
CM_HEAVY_TIMEOUT="${CM_CAPTURE_TIMEOUT:-1800}" "$CM_HEAVY" unity "$UNITY" -batchmode -projectPath "$ROOT" \
  -executeMethod CriticalMass.Editor.Flow.OwnerWalkCaptureTool.Run -walkOut "$ABS" ${RES:+-walkRes "$RES"} -logFile "$LOG"
code=$?
set -e
echo "unity-owner-walk-capture: exit $code, $(ls "$ABS"/*.png 2>/dev/null | wc -l | tr -d ' ') image(s) in $ABS (log: $LOG)"
exit $code
