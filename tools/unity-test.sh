#!/usr/bin/env bash
# Run EditMode tests. Results: Logs/editmode-results.xml. Fails if Unity fails, any test fails, or zero tests ran.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/_unity-env.sh"
LOG="$ROOT/Logs/editmode-tests.log"
RESULTS="$ROOT/Logs/editmode-results.xml"
rm -f "$RESULTS"

# No -quit: the Test Framework quits the editor itself when the run ends.
set +e
"$UNITY" -batchmode -nographics -projectPath "$ROOT" -runTests -testPlatform EditMode \
  -testResults "$RESULTS" -logFile "$LOG"
code=$?
set -e

if [[ ! -f "$RESULTS" ]]; then
  echo "unity-test: FAILED, no results file (Unity exit code $code, log: $LOG)" >&2
  [[ $code -ne 0 ]] && exit "$code"
  exit 1
fi

attr() { xmllint --xpath "string(/test-run/@$1)" "$RESULTS"; }
total="$(attr total)"; passed="$(attr passed)"; failed="$(attr failed)"; result="$(attr result)"
echo "unity-test: result=$result total=$total passed=$passed failed=$failed (results: $RESULTS)"

if [[ $code -ne 0 ]]; then
  echo "unity-test: FAILED, Unity exit code $code (log: $LOG)" >&2
  exit "$code"
fi
if [[ -z "$total" || "$total" -eq 0 ]]; then
  echo "unity-test: FAILED, zero tests ran" >&2
  exit 1
fi
if [[ "$failed" -ne 0 || "$result" != Passed* ]]; then
  echo "unity-test: FAILED, $failed test(s) failed" >&2
  exit 1
fi
echo "unity-test: OK"
