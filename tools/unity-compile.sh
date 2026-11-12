#!/usr/bin/env bash
# Open the project in batch mode, compile all scripts, quit. Fails on any compiler error.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/_unity-env.sh"
LOG="$ROOT/Logs/compile.log"

set +e
"$UNITY" -batchmode -nographics -projectPath "$ROOT" -logFile "$LOG" -quit
code=$?
set -e

if [[ $code -ne 0 ]]; then
  echo "unity-compile: FAILED, Unity exit code $code (log: $LOG)" >&2
  exit "$code"
fi
if grep -qE "error CS[0-9]+" "$LOG"; then
  grep -E "error CS[0-9]+" "$LOG" | sort -u | head -50 >&2
  echo "unity-compile: FAILED, compiler errors (log: $LOG)" >&2
  exit 1
fi
echo "unity-compile: OK (log: $LOG)"
