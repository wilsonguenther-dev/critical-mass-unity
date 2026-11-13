#!/usr/bin/env bash
# Build the macOS player via CriticalMass.Editor.Build.MacOS (default Builds/macOS/CriticalMass.app in THIS checkout).
#   tools/unity-build-mac.sh [--output PATH] [--development]
# PATH is relative to the checkout (or absolute). --development builds a development player (profiler markers for
# the F3 overlay and the autoplay report, DG-05). Hard limit: CM_BUILD_TIMEOUT seconds (default 1800), then exit 142.
set -euo pipefail
OUT='Builds/macOS/CriticalMass.app'
DEV=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --output) OUT="${2:?--output needs a path}"; shift 2 ;;
    --development) DEV=(-development); shift ;;
    *) echo "usage: unity-build-mac.sh [--output PATH] [--development]" >&2; exit 64 ;;
  esac
done
source "$(dirname "${BASH_SOURCE[0]}")/_unity-env.sh"
LOG="${CM_BUILD_LOG:-$ROOT/Logs/build-mac.log}"
case "$OUT" in /*) ABS="$OUT" ;; *) ABS="$ROOT/$OUT" ;; esac

set +e
# Through the machine lock (cm-heavy unity, DG-44); never call "$UNITY" bare. The lock enforces the hard limit
# (CM_BUILD_TIMEOUT -> CM_HEAVY_TIMEOUT; its exit 124 is reported as 142, the code this script always used).
CM_HEAVY_TIMEOUT="${CM_BUILD_TIMEOUT:-1800}" "$CM_HEAVY" unity "$UNITY" -batchmode -nographics -projectPath "$ROOT" -buildTarget osxuniversal \
  -executeMethod CriticalMass.Editor.Build.MacOS -buildOutput "$ABS" ${DEV[@]+"${DEV[@]}"} -logFile "$LOG" -quit
code=$?
set -e
[[ $code -eq 124 ]] && code=142

if [[ $code -ne 0 ]]; then
  echo "unity-build-mac: FAILED, Unity exit code $code (log: $LOG)" >&2
  exit "$code"
fi
if [[ ! -d "$ABS" ]]; then
  echo "unity-build-mac: FAILED, $ABS missing (log: $LOG)" >&2
  exit 1
fi
echo "unity-build-mac: OK -> $ABS"
