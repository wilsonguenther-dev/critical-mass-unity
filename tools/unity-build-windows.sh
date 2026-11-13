#!/usr/bin/env bash
# Build the Windows 64-bit player to Builds/Windows/CriticalMass.exe via CriticalMass.Editor.Build.Windows (U-051:
# the demo ships as a Windows build; the Mac build is for our own testing). Needs the editor's Windows Build Support
# module (PlaybackEngines/WindowsStandaloneSupport); the build-windows gate reports skipped (78) without it.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/_unity-env.sh"
LOG="$ROOT/Logs/build-windows.log"

set +e
# Through the machine lock (cm-heavy unity, DG-44); never call "$UNITY" bare.
"$CM_HEAVY" unity "$UNITY" -batchmode -nographics -projectPath "$ROOT" -buildTarget win64 \
  -executeMethod CriticalMass.Editor.Build.Windows -logFile "$LOG" -quit
code=$?
set -e

if [[ $code -ne 0 ]]; then
  echo "unity-build-windows: FAILED, Unity exit code $code (log: $LOG)" >&2
  exit "$code"
fi
if [[ ! -f "$ROOT/Builds/Windows/CriticalMass.exe" ]]; then
  echo "unity-build-windows: FAILED, Builds/Windows/CriticalMass.exe missing (log: $LOG)" >&2
  exit 1
fi
echo "unity-build-windows: OK -> $ROOT/Builds/Windows/CriticalMass.exe"
