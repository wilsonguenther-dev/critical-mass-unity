#!/usr/bin/env bash
# Player-seat telemetry sweep on the second Mac (px lane T, 2026-11-22). Packs the COMMITTED tree (HEAD) of the files the
# probe needs, runs it there with the box's .NET SDK through tools/remote-run.sh (same queue and lock as every other
# remote job), copies the JSONL back, then deletes everything the job brought onto the box. Never writes the box's address.
#   tools/px-telemetry/air.sh [--out LOCAL_DIR] [probe options...]      (the probe options of run.sh, e.g. --class all --vs all)
# Commit first: the box runs HEAD, not the working tree.
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUTDIR=""
ARGS=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --out) OUTDIR="${2:?}"; shift 2 ;;
    *) ARGS+=("$1"); shift ;;
  esac
done
SHA="$(git -C "$ROOT" rev-parse --short HEAD)"
[[ -n "$OUTDIR" ]] || OUTDIR="${CM_PX_TELEMETRY_OUT:-$HOME/cm-incidents/player-experience-rebuild-2026-11-22/data/t-telemetry/runs}/air-$SHA-$(date -u +%Y%m%dT%H%M%SZ)"
ID="px-telemetry-$SHA-$$"
TMP="$(mktemp -d "${TMPDIR:-/tmp}/px-air.XXXXXX")"; trap 'rm -rf "$TMP"' EXIT
git -C "$ROOT" archive --format=tar.gz -o "$TMP/$ID.tgz" HEAD Assets/_Game/Sim Assets/_Game/Net Assets/_Game/Sim.Tests/Harness \
  Assets/_Game/Sim.Tests/PlayerSeat tools/ci/Sim tools/ci/Net tools/px-telemetry ProjectSettings/ProjectSettings.asset || { echo "air.sh: git archive failed" >&2; exit 2; }
DEFINES="$(sed -n '/scriptingDefineSymbols:/,/^  [a-zA-Z]/{s/^ *Standalone: *//p;}' "$ROOT/ProjectSettings/ProjectSettings.asset" | head -1 | tr -d '\r')"
Q="$(printf '%q ' "${ARGS[@]+"${ARGS[@]}"}")"
CMD="set -e; mkdir -p $ID; tar -xzf $ID.tgz -C $ID; rm -f $ID.tgz; cd $ID; export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0; \
dotnet build -c Release -nologo -v q tools/px-telemetry/PxTelemetry.csproj '-p:CmDefines=${DEFINES//;/%3B}' -p:TreatWarningsAsErrors=false -o bin-px > build.log 2>&1 || { tail -30 build.log; exit 2; }; \
dotnet bin-px/PxTelemetry.dll $Q --out out; dotnet build-server shutdown > /dev/null 2>&1 || true"
mkdir -p "$OUTDIR"
"$ROOT/tools/remote-run.sh" --put "$TMP/$ID.tgz" --get "$ID/out/" "$OUTDIR" --timeout 1800 -- "$CMD"
code=$?
# delete what this job brought onto the box (its folder and, if the command died early, the upload)
"$ROOT/tools/remote-run.sh" --lock-wait 600 --timeout 120 -- "rm -rf $ID $ID.tgz" > /dev/null 2>&1 || echo "air.sh: cleanup of $ID on the box failed; remove ~/cm-remote/work/$ID by hand" >&2
echo "air.sh: exit $code, results in $OUTDIR"
exit $code
