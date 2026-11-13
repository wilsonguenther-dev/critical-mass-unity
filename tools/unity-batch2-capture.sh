#!/usr/bin/env bash
# Integration batch 2's one-run capture walk (CriticalMass.Editor.Flow.Batch2CaptureTool.Run): every batch 2 screen as a
# 1920x1080 PNG plus walk.log in OUT_DIR. Needs a GPU (no -nographics). Usage: tools/unity-batch2-capture.sh OUT_DIR
set -euo pipefail
OUT="${1:?usage: unity-batch2-capture.sh OUT_DIR}"
source "$(dirname "${BASH_SOURCE[0]}")/_unity-env.sh"
LOG="$ROOT/Logs/batch2-capture.log"
mkdir -p "$OUT"
ABS="$(cd "$OUT" && pwd)"
set +e
# Through the machine lock (cm-heavy unity, DG-44); never call "$UNITY" bare.
CM_HEAVY_TIMEOUT="${CM_CAPTURE_TIMEOUT:-1200}" "$CM_HEAVY" unity "$UNITY" -batchmode -projectPath "$ROOT" \
  -executeMethod CriticalMass.Editor.Flow.Batch2CaptureTool.Run -walkOut "$ABS" -logFile "$LOG"
code=$?
set -e
echo "unity-batch2-capture: exit $code, $(ls "$ABS"/*.png 2>/dev/null | wc -l | tr -d ' ') image(s) in $ABS (log: $LOG)"
exit $code
