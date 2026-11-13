#!/usr/bin/env bash
# No-Unity Sim gate (ci/offload, 2026-11-13): compiles CriticalMass.Sim against .NET Standard 2.1 with C# 9 (the API
# level and language Unity uses), compiles CriticalMass.Sim.Tests, and runs the tests with NUnitLite. Needs only a .NET
# SDK (8 or newer); no Unity, no licence, no editor process. Used by .github/workflows/sim-tests.yml; works on any
# machine with `dotnet` on PATH. On this Mac without a .NET SDK, use tools/ci/sim-tests-mac.sh (Unity's bundled
# compiler and Mono, still no editor).
#   tools/ci/sim-tests.sh                 tests not in category Slow (the per-batch set)
#   tools/ci/sim-tests.sh --slow          only the Slow tests
#   tools/ci/sim-tests.sh --all           every test
#   tools/ci/sim-tests.sh --compile-only  compile both assemblies, run nothing
# Results: Temp/ci/sim-results.xml (NUnit 3 XML). Exit: 0 pass, 1 test failure, 2 compile failure.
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
WHERE='cat != Slow'; COMPILE_ONLY=0
case "${1:-}" in
  --slow) WHERE='cat == Slow' ;;
  --all) WHERE='' ;;
  --compile-only) COMPILE_ONLY=1 ;;
  '') ;;
  *) echo "usage: sim-tests.sh [--slow|--all|--compile-only]" >&2; exit 64 ;;
esac
command -v dotnet >/dev/null || { echo "sim-tests: no dotnet SDK on PATH (on the Mac use tools/ci/sim-tests-mac.sh)" >&2; exit 78; }
# The project's scripting define symbols (Standalone), e.g. CM_SIM_PHYSICS;CM_SIM_MECHANICS, as Unity passes them.
DEFINES="$(sed -n '/scriptingDefineSymbols:/,/^  [a-zA-Z]/{s/^ *Standalone: *//p;}' ProjectSettings/ProjectSettings.asset | head -1 | tr -d '\r')"
echo "sim-tests: project defines '${DEFINES}'"
mkdir -p Temp/ci
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
if ! dotnet build tools/ci/SimTests/SimTests.csproj -c Release -nologo -clp:ErrorsOnly \
     "-p:CmDefines=${DEFINES//;/%3B}" -p:TreatWarningsAsErrors=false > Temp/ci/sim-build.log 2>&1; then
  grep -E 'error [A-Z]+[0-9]+' Temp/ci/sim-build.log | sed -E "s#${ROOT}/##" | sort -u | head -60
  echo "sim-tests: COMPILE FAILED (log Temp/ci/sim-build.log)"; exit 2
fi
echo "sim-tests: compile OK (CriticalMass.Sim netstandard2.1 C# 9, CriticalMass.Sim.Tests)"
[[ $COMPILE_ONLY -eq 1 ]] && exit 0
EXE="tools/ci/SimTests/bin/Release/net8.0/CriticalMass.Sim.Tests.dll"
args=(--noheader --labels=Off "--result=Temp/ci/sim-results.xml")
[[ -n "$WHERE" ]] && args+=("--where=$WHERE")
dotnet "$EXE" "${args[@]}"
rc=$?
[[ $rc -eq 0 ]] && exit 0
echo "sim-tests: FAILED (NUnitLite exit $rc; results Temp/ci/sim-results.xml)"; exit 1
