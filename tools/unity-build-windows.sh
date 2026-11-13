#!/usr/bin/env bash
# Build the Windows 64-bit player via CriticalMass.Editor.Build.Windows (default Builds/Windows/CriticalMass.exe in THIS
# checkout; U-051: the demo ships as a Windows build, the Mac build is for our own testing). Needs the editor's Windows
# Build Support module (PlaybackEngines/WindowsStandaloneSupport); the build-windows gate reports skipped (78) without it.
#   tools/unity-build-windows.sh [--output PATH] [--development]
# Hard limit: CM_BUILD_TIMEOUT seconds (default 1800), then exit 142.
set -euo pipefail
OUT='Builds/Windows/CriticalMass.exe'
DEV=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --output) OUT="${2:?--output needs a path}"; shift 2 ;;
    --development) DEV=(-development); shift ;;
    *) echo "usage: unity-build-windows.sh [--output PATH] [--development]" >&2; exit 64 ;;
  esac
done
source "$(dirname "${BASH_SOURCE[0]}")/_unity-env.sh"
LOG="${CM_BUILD_LOG:-$ROOT/Logs/build-windows.log}"
case "$OUT" in /*) ABS="$OUT" ;; *) ABS="$ROOT/$OUT" ;; esac

set +e
perl -e 'alarm shift; exec @ARGV' "${CM_BUILD_TIMEOUT:-1800}" \
  "$UNITY" -batchmode -nographics -projectPath "$ROOT" -buildTarget win64 \
  -executeMethod CriticalMass.Editor.Build.Windows -buildOutput "$ABS" ${DEV[@]+"${DEV[@]}"} -logFile "$LOG" -quit
code=$?
set -e

if [[ $code -ne 0 ]]; then
  echo "unity-build-windows: FAILED, Unity exit code $code (log: $LOG)" >&2
  exit "$code"
fi
if [[ ! -f "$ABS" ]]; then
  echo "unity-build-windows: FAILED, $ABS missing (log: $LOG)" >&2
  exit 1
fi
echo "unity-build-windows: OK -> $ABS"
