#!/usr/bin/env bash
# The Mac's no-Unity Sim gate (ci/offload, 2026-11-13; was the rules-dg-02 runner in Obsidian). Compiles
# CriticalMass.Sim plus CriticalMass.Sim.Tests with the C# compiler that ships inside the Unity app (Roslyn csc.dll run by
# the bundled dotnet) and runs them under the bundled Mono. No editor process, no graphics, no licence, no cm-heavy slot:
# a few hundred MB of memory for a minute or two. Needs NUnit from any Library/PackageCache (a worktree's own Library or
# --library DIR, e.g. ~/critical-mass-unity/Library).
#   tools/ci/sim-tests-mac.sh [--library DIR] [--slow|--all] [fixture substrings...]
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
LIB="$ROOT/Library"
if [[ "${1:-}" == "--library" ]]; then LIB="${2:?}"; shift 2; fi
S=/Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/Resources/Scripting
API="$S/MonoBleedingEdge/lib/mono/unityjit-macos"
[[ -x "$S/NetCoreRuntime/dotnet" ]] || { echo "sim-tests-mac: Unity 6000.3.28f1 not installed" >&2; exit 78; }
NUNIT="$(ls "$LIB"/PackageCache/com.unity.ext.nunit@*/net40/unity-custom/nunit.framework.dll 2>/dev/null | head -1)"
if [[ -z "$NUNIT" ]]; then NUNIT="$(ls "$HOME"/critical-mass-unity/Library/PackageCache/com.unity.ext.nunit@*/net40/unity-custom/nunit.framework.dll 2>/dev/null | head -1)"; fi
[[ -n "$NUNIT" ]] || { echo "sim-tests-mac: no nunit.framework.dll in $LIB/PackageCache (pass --library DIR)" >&2; exit 78; }
DEFINES="$(sed -n '/scriptingDefineSymbols:/,/^  [a-zA-Z]/{s/^ *Standalone: *//p;}' "$ROOT/ProjectSettings/ProjectSettings.asset" | head -1 | tr -d '\r')"
OUT="$(mktemp -d "${TMPDIR:-/tmp}/cm-sim-tests.XXXXXX")"; trap 'rm -rf "$OUT"' EXIT
cp "$NUNIT" "$OUT/"
find "$ROOT/Assets/_Game/Sim" "$ROOT/Assets/_Game/Sim.Tests" -name '*.cs' -not -path '*~/*' > "$OUT/files.txt"
"$S/NetCoreRuntime/dotnet" "$S/DotNetSdkRoslyn/csc.dll" -nologo -nostdlib -noconfig -langversion:9 -nowarn:1591,0618 \
  "-define:UNITY_INCLUDE_TESTS;${DEFINES}" -r:"$API/mscorlib.dll" -r:"$API/System.dll" -r:"$API/System.Core.dll" \
  -r:"$API/Facades/netstandard.dll" -r:"$API/Facades/System.Runtime.dll" -r:"$OUT/nunit.framework.dll" \
  -target:exe -out:"$OUT/simtests.exe" "$ROOT/tools/ci/mac/Runner.cs" @"$OUT/files.txt" > "$OUT/csc.txt" 2>&1
c=$?; grep -E "error CS" "$OUT/csc.txt" | sed -E "s#${ROOT}/##" | head -40; echo "sim-tests-mac: csc exit $c (defines UNITY_INCLUDE_TESTS;${DEFINES})"
[[ $c -eq 0 ]] || exit 2
cd "$ROOT" && "$S/MonoBleedingEdge/bin/mono" --debug "$OUT/simtests.exe" "$@"
