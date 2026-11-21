#!/usr/bin/env bash
# b15 tools: the balance report, headless (.NET 8 SDK, no Unity). Run from anywhere; it works in the repo root.
#   tools/balance/balance-report.sh [run options]          24 bot matches 16v16 (Full rules) + the duel tables -> Temp/balance/report
#   tools/balance/balance-report.sh --seeds 72 --set Depth.Hut.On=false --out Temp/balance/hut-off
#   tools/balance/balance-report.sh --replays ~/Library/Application\ Support/.../Replays      re-simulate saved replays
#   tools/balance/balance-report.sh compare --base <ref|switches|report.json> --head <ref|switches|report.json> [--seeds N]
#        a ref (origin/integration/batch-13-cand1, a sha, HEAD~3) is checked out in a temporary worktree with THIS tool laid
#        over it, so both sides are measured by the same code; switches are "profile=full;Depth.Hut.On=false" ("" = the game).
#   tools/balance/balance-report.sh tuning-index           regenerate docs/balance/TUNING-INDEX.md
# Options: see tools/ci/BalanceReport/Program.cs. Exit: 0 ok, 2 compile failure, 64 usage, 78 no dotnet.
# Shared machine: BALANCE_NICE=1 runs the tool under nice -n 10 (one thread per process; the sim is single-threaded).
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
command -v dotnet >/dev/null || { echo "balance-report: no dotnet SDK on PATH (use the balance-report GitHub workflow)" >&2; exit 78; }
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
NICE=(); [[ "${BALANCE_NICE:-0}" == 1 ]] && NICE=(nice -n 10)

# build <checkout root>: compiles the tool against that checkout's Sim and Net; prints the dll path
build() {
  local root="$1" defines
  defines="$(sed -n '/scriptingDefineSymbols:/,/^  [a-zA-Z]/{s/^ *Standalone: *//p;}' "$root/ProjectSettings/ProjectSettings.asset" | head -1 | tr -d '\r')"
  mkdir -p "$ROOT/Temp/balance"
  if ! dotnet build "$root/tools/ci/BalanceReport/BalanceReport.csproj" -c Release -nologo -clp:ErrorsOnly \
       "-p:CmDefines=${defines//;/%3B}" -p:TreatWarningsAsErrors=false > "$ROOT/Temp/balance/build.log" 2>&1; then
    grep -E 'error [A-Z]+[0-9]+' "$ROOT/Temp/balance/build.log" | sort -u | head -40 >&2
    echo "balance-report: COMPILE FAILED in $root (log Temp/balance/build.log)" >&2
    return 2
  fi
  echo "$root/tools/ci/BalanceReport/bin/Release/net8.0/balance-report.dll"
}

# lay this checkout's tool over another checkout (the tool files only; the sim stays the other checkout's)
overlay() {
  local wt="$1"
  mkdir -p "$wt/tools/ci/BalanceReport" "$wt/tools/balance" "$wt/docs/balance" "$wt/Assets/_Game/Sim.Tests/Harness"
  cp "$ROOT/tools/ci/BalanceReport/Program.cs" "$ROOT/tools/ci/BalanceReport/BalanceReport.csproj" "$wt/tools/ci/BalanceReport/"
  [[ -f "$ROOT/tools/ci/BalanceReport/AuditProbe.cs" ]] && cp "$ROOT/tools/ci/BalanceReport/AuditProbe.cs" "$wt/tools/ci/BalanceReport/"   # b16 probe only
  cp "$ROOT"/Assets/_Game/Sim.Tests/Harness/{BalanceProbe,BalanceStats,TuningIndex,WinShare}.cs "$wt/Assets/_Game/Sim.Tests/Harness/"
  cp "$ROOT/docs/balance/TARGETS.json" "$wt/docs/balance/"
}

is_spec() { [[ -z "$1" || "$1" == *=* || "$1" == *.json ]]; }

# measure_ref <name> <ref> <out dir> [run options...]: the report of another commit, measured by THIS tool (laid over it)
measure_ref() {
  local name="$1" v="$2" out="$3" sha wt dll; shift 3
  sha="$(git rev-parse --verify --quiet "$v^{commit}")" || { echo "balance-report: '$v' is not a ref, a report .json or a switch list" >&2; return 64; }
  wt="$ROOT/Temp/balance/wt-${sha:0:12}"
  [[ -d "$wt" ]] || git worktree add --detach --quiet "$wt" "$sha" >&2 || return 1
  overlay "$wt"
  dll="$(build "$wt")" || return 2
  (cd "$wt" && ${NICE[@]+"${NICE[@]}"} dotnet "$dll" run "$@" --label "$name $v" --commit "${sha:0:8}" --out "$out") >&2
  local rc=$?
  git worktree remove --force "$wt" >&2 || true
  return $rc
}

if [[ "${1:-}" == compare ]]; then
  shift
  args=(); base=""; head=""; have_base=0; have_head=0
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --base) base="$2"; have_base=1; shift 2 ;;
      --head) head="$2"; have_head=1; shift 2 ;;
      *) args+=("$1"); shift ;;
    esac
  done
  [[ $have_base == 1 && $have_head == 1 ]] || { echo "usage: balance-report.sh compare --base X --head Y [options]" >&2; exit 64; }
  side() {   # side <name> <spec>: echoes what the tool reads (a report json for a ref, else the spec itself)
    if is_spec "$2"; then echo "$2"; return 0; fi
    local out="$ROOT/Temp/balance/$1-report"
    measure_ref "$1" "$2" "$out" ${args[@]+"${args[@]}"} || return $?
    echo "$out/balance-report.json"
  }
  b="$(side base "$base")" || exit $?
  h="$(side head "$head")" || exit $?
  dll="$(build "$ROOT")" || exit 2
  exec ${NICE[@]+"${NICE[@]}"} dotnet "$dll" compare --base "$b" --head "$h" --commit "$(git rev-parse --short HEAD)" ${args[@]+"${args[@]}"}
fi

if [[ "${1:-}" == tuning-index ]]; then dll="$(build "$ROOT")" || exit 2; exec dotnet "$dll" "$@"; fi
[[ "${1:-}" == run ]] && shift
# run --ref <ref>: measure another commit with this tool (the report lands in --out, default Temp/balance/report)
if [[ "${1:-}" == --ref ]]; then
  ref="$2"; shift 2
  out="Temp/balance/report"; rest=()
  while [[ $# -gt 0 ]]; do
    if [[ "$1" == --out ]]; then out="$2"; shift 2; else rest+=("$1"); shift; fi
  done
  [[ "$out" == /* ]] || out="$ROOT/$out"
  measure_ref "ref" "$ref" "$out" ${rest[@]+"${rest[@]}"}; exit $?
fi
dll="$(build "$ROOT")" || exit 2
exec ${NICE[@]+"${NICE[@]}"} dotnet "$dll" run --commit "$(git rev-parse --short HEAD 2>/dev/null || echo unknown)" "$@"
