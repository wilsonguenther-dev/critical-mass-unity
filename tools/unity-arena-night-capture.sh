#!/usr/bin/env bash
# Arena night life 1 capture walk (CriticalMass.Editor.Flow.ArenaNightCaptureTool.Run): READY with the TIME button, a
# 16 v 16 match at its start and mid-match in Day, Dusk and Night, ambient life frames and Night at Low, each at
# 3024x1964 and 1920x1080, plus walk.log and stats.json in OUT_DIR. Batch mode with a GPU (no -nographics), no window.
# Usage: tools/unity-arena-night-capture.sh OUT_DIR
set -euo pipefail
OUT="${1:?usage: unity-arena-night-capture.sh OUT_DIR}"
source "$(dirname "${BASH_SOURCE[0]}")/_unity-env.sh"
LOG="$ROOT/Logs/arena-night-capture.log"
mkdir -p "$OUT" "$ROOT/Logs"
ABS="$(cd "$OUT" && pwd)"
set +e
# Through the machine lock (cm-heavy unity, DG-44); never call "$UNITY" bare.
CM_HEAVY_TIMEOUT="${CM_CAPTURE_TIMEOUT:-1800}" "$CM_HEAVY" unity "$UNITY" -batchmode -projectPath "$ROOT" \
  -executeMethod CriticalMass.Editor.Flow.ArenaNightCaptureTool.Run -walkOut "$ABS" -logFile "$LOG"
code=$?
set -e
echo "unity-arena-night-capture: exit $code, $(ls "$ABS"/*.png 2>/dev/null | wc -l | tr -d ' ') image(s) in $ABS (log: $LOG)"
exit $code
