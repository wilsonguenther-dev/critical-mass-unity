#!/usr/bin/env bash
# Build the macOS player to Builds/macOS/CriticalMass.app via CriticalMass.Editor.Build.MacOS.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/_unity-env.sh"
LOG="$ROOT/Logs/build-mac.log"

set +e
"$UNITY" -batchmode -nographics -projectPath "$ROOT" -buildTarget osxuniversal \
  -executeMethod CriticalMass.Editor.Build.MacOS -logFile "$LOG" -quit
code=$?
set -e

if [[ $code -ne 0 ]]; then
  echo "unity-build-mac: FAILED, Unity exit code $code (log: $LOG)" >&2
  exit "$code"
fi
if [[ ! -d "$ROOT/Builds/macOS/CriticalMass.app" ]]; then
  echo "unity-build-mac: FAILED, Builds/macOS/CriticalMass.app missing (log: $LOG)" >&2
  exit 1
fi
echo "unity-build-mac: OK -> $ROOT/Builds/macOS/CriticalMass.app"
