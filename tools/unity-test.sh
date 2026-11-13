#!/usr/bin/env bash
# Run EditMode tests. Results: Logs/editmode-results.xml. Fails if Unity fails, any test fails, or zero tests ran.
# Default: tests in NUnit category "Slow" are excluded (the soak lane runs them, U-028).
#   tools/unity-test.sh                 -testCategory "!Slow"
#   tools/unity-test.sh --category Slow only the Slow tests
#   tools/unity-test.sh --all           no category filter
# Syntax from Unity Test Framework 1.6 (Library/PackageCache/com.unity.test-framework@*/UnityEditor.TestRunner/
# CommandLineParser/CommandLineOption.cs splits the value on ';'; UnityEngine.TestRunner/TestRunner/
# RuntimeTestRunnerFilter.cs turns a leading '!' into a NotFilter).
set -euo pipefail
CATEGORY='!Slow'
while [[ $# -gt 0 ]]; do
  case "$1" in
    --category) CATEGORY="${2:?--category needs a value}"; shift 2 ;;
    --all) CATEGORY=''; shift ;;
    *) echo "usage: unity-test.sh [--category FILTER | --all]" >&2; exit 64 ;;
  esac
done
source "$(dirname "${BASH_SOURCE[0]}")/_unity-env.sh"
LOG="$ROOT/Logs/editmode-tests.log"
RESULTS="$ROOT/Logs/editmode-results.xml"
rm -f "$RESULTS"

# No -quit: the Test Framework quits the editor itself when the run ends.
set +e
# Through the machine lock (cm-heavy unity, DG-44); never call "$UNITY" bare.
"$CM_HEAVY" unity "$UNITY" -batchmode -nographics -projectPath "$ROOT" -runTests -testPlatform EditMode \
  -testResults "$RESULTS" -logFile "$LOG" ${CATEGORY:+-testCategory "$CATEGORY"}
code=$?
set -e

if [[ ! -f "$RESULTS" ]]; then
  echo "unity-test: FAILED, no results file (Unity exit code $code, log: $LOG)" >&2
  [[ $code -ne 0 ]] && exit "$code"
  exit 1
fi

attr() { xmllint --xpath "string(/test-run/@$1)" "$RESULTS"; }
total="$(attr total)"; passed="$(attr passed)"; failed="$(attr failed)"; result="$(attr result)"
echo "unity-test: category filter '${CATEGORY:-none}' result=$result total=$total passed=$passed failed=$failed (results: $RESULTS)"

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
