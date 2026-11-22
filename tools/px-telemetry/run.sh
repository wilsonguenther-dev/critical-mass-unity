#!/usr/bin/env bash
# Player-seat telemetry, headless (px lane T, 2026-11-22). Compiles tools/px-telemetry/PlayerSeatTelemetryProbe.cs against
# this checkout's Sim + Net + the player-seat rig (Sim.Tests/Harness, Sim.Tests/PlayerSeat/PlayerSeatRig.cs, Targets) with
# the C# compiler bundled in Unity, and runs it under Unity's bundled Mono. No Editor, no licence, no graphics.
#   tools/px-telemetry/run.sh [--root DIR] [probe options...]
# Probe options (all optional):
#   --class Warrior|all|A,B   --vs Warrior|all|A,B   --mode duel|5v5|16v16   --seeds 8   --seed0 4242   --ticks N
#   --reaction 6   --aim-help 0|1|2   --skill first|normal|strong   --verbs all|attack   --bots Normal|Easy|Hard
#   --out DIR (default ~/cm-incidents/player-experience-rebuild-2026-11-22/data/t-telemetry/runs/<utc stamp>)   --label TEXT
# Then: python3 tools/px-telemetry/summarize.py <out dir>
# Docs: docs/testing/PLAYER-SEAT-TELEMETRY.md
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
if [[ "${1:-}" == "--root" ]]; then ROOT="$(cd "${2:?}" && pwd)"; shift 2; fi
ARGS=("$@")
HAS_OUT=0
for a in "${ARGS[@]+"${ARGS[@]}"}"; do [[ "$a" == "--out" ]] && HAS_OUT=1; done
if [[ $HAS_OUT -eq 0 ]]; then
  OUTDIR="${CM_PX_TELEMETRY_OUT:-$HOME/cm-incidents/player-experience-rebuild-2026-11-22/data/t-telemetry/runs}/$(date -u +%Y%m%dT%H%M%SZ)"
  ARGS+=(--out "$OUTDIR")
fi
S=/Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/Resources/Scripting
API="$S/MonoBleedingEdge/lib/mono/unityjit-macos"
[[ -x "$S/NetCoreRuntime/dotnet" ]] || { echo "px-telemetry: Unity 6000.3.28f1 not installed" >&2; exit 78; }
DEFINES="$(sed -n '/scriptingDefineSymbols:/,/^  [a-zA-Z]/{s/^ *Standalone: *//p;}' "$ROOT/ProjectSettings/ProjectSettings.asset" | head -1 | tr -d '\r')"
OUT="$(mktemp -d "${TMPDIR:-/tmp}/cm-px-telemetry.XXXXXX")"; trap 'rm -rf "$OUT"' EXIT
{
  find "$ROOT/Assets/_Game/Sim" "$ROOT/Assets/_Game/Net" "$ROOT/Assets/_Game/Sim.Tests/Harness" -name '*.cs' -not -path '*~/*'
  echo "$ROOT/Assets/_Game/Sim.Tests/PlayerSeat/PlayerSeatRig.cs"
  echo "$ROOT/Assets/_Game/Sim.Tests/PlayerSeat/PlayerSeatTargets.cs"
} > "$OUT/files.txt"
"$S/NetCoreRuntime/dotnet" "$S/DotNetSdkRoslyn/csc.dll" -nologo -nostdlib -noconfig -langversion:9 -nowarn:1591,0618,0649,0169,0414 -optimize+ \
  "-define:${DEFINES}" -r:"$API/mscorlib.dll" -r:"$API/System.dll" -r:"$API/System.Core.dll" \
  -r:"$API/Facades/netstandard.dll" -r:"$API/Facades/System.Runtime.dll" -r:"$API/System.Net.Http.dll" \
  -target:exe -out:"$OUT/probe.exe" "$HERE/PlayerSeatTelemetryProbe.cs" @"$OUT/files.txt" > "$OUT/csc.txt" 2>&1
c=$?; grep -E "error CS" "$OUT/csc.txt" | sed -E "s#${ROOT}/##" | head -30
[[ $c -eq 0 ]] || { echo "px-telemetry: csc exit $c"; exit 2; }
"$S/MonoBleedingEdge/bin/mono" "$OUT/probe.exe" "${ARGS[@]}"
