#!/usr/bin/env bash
# b14 sim-cost harness, CI only (never merged). Same job, alternating arms: control a72d2a5e, b13 base 6f32a6ec, the
# candidate f4d9f2a2 and this checkout (head). Three rounds of the clean bench (histogram), then one probed run per
# probed arm (per-system attribution of the heaviest 1% of ticks). Prints [simcost] / [simprobe] lines and a summary.
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"; cd "$ROOT"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
T="${RUNNER_TEMP:-/tmp}/simcost"; rm -rf "$T"; mkdir -p "$T"
declare -A SHA=([control]=a72d2a5e9dda6988296ea7f5a2795fb9f4625d23 [b13base]=6f32a6ec4bea4788991afa47132b4f20114ed161 [cand]=f4d9f2a2e1db248f04507347f10634006fdef54b)
PATHS="Assets/_Game/Sim Assets/_Game/Net Assets/_Game/Sim.Tests tools/ci ProjectSettings"
extract() {  # name sha
  mkdir -p "$T/$1"; git archive "$2" $PATHS | tar -x -C "$T/$1" || { echo "simcost: archive $2 failed"; exit 2; }
}
for a in control b13base cand; do git fetch -q --no-tags --depth=1 origin "${SHA[$a]}" || { echo "simcost: fetch ${SHA[$a]} failed"; exit 2; }; done
for a in control b13base cand; do extract "$a" "${SHA[$a]}"; done
for a in control b13base cand; do extract "$a-probe" "${SHA[$a]}"; done
extract head HEAD; extract head-probe HEAD
build() {  # dir [--patch]
  python3 "$ROOT/tools/ci/simcost/probe.py" "$1" ${2:-} || exit 2
  cp "$ROOT/tools/ci/simcost/SimCostBench.cs" "$1/Assets/_Game/Sim.Tests/SimCostBench.cs"
  local D; D="$(sed -n '/scriptingDefineSymbols:/,/^  [a-zA-Z]/{s/^ *Standalone: *//p;}' "$1/ProjectSettings/ProjectSettings.asset" | head -1 | tr -d '\r')"
  dotnet build "$1/tools/ci/SimTests/SimTests.csproj" -c Release -nologo -clp:ErrorsOnly "-p:CmDefines=${D//;/%3B}" -p:TreatWarningsAsErrors=false > "$1/simcost-build.log" 2>&1 \
    || { grep -E 'error [A-Z]+[0-9]+' "$1/simcost-build.log" | sort -u | head -20; echo "simcost: compile failed in $1"; exit 2; }
}
for a in control b13base cand head; do build "$T/$a"; done
for a in control b13base cand head; do build "$T/$a-probe" --patch; done
run() { (cd "$T/$1" && CM_COST_TAG="$2" dotnet tools/ci/SimTests/bin/Release/net8.0/CriticalMass.Sim.Tests.dll --noheader --labels=Off --where "class == CriticalMass.Sim.Tests.SimCostBench" --result="$T/r.xml") | grep -E '\[sim(cost|probe)\]' | tee -a "$ROOT/simcost.txt"; }
echo "simcost: head $(git rev-parse --short HEAD), runner $(nproc) cpus, $(date -u +%H:%M:%SZ)"
for r in 1 2 3; do for a in control b13base cand head; do run "$a" "$a run $r"; done; done
for a in control b13base cand head; do run "$a-probe" "$a probed"; done
python3 - "$ROOT/simcost.txt" <<'PY' | tee -a "${GITHUB_STEP_SUMMARY:-/dev/null}"
import re,sys,statistics as st
K=['trimmed','median','p95','p99','p99.9','max']
d={}
for l in open(sys.argv[1]):
    m=re.search(r'\] (\w+) run \d+: ([0-9.]+) us per tick.*median ([0-9.]+) us, p95 ([0-9.]+) us, p99 ([0-9.]+) us, p99.9 ([0-9.]+) us, max ([0-9.]+) us',l)
    if m: d.setdefault(m.group(1),[]).append([float(x) for x in m.groups()[1:]])
med={a:[st.median(r[i] for r in v) for i in range(len(K))] for a,v in d.items()}
print('[simcost] SUMMARY (median of 3 alternated runs, us per tick):')
for a in ['control','b13base','cand','head']:
    if a in med: print(f"[simcost]   {a:8s} " + ', '.join(f"{k} {x:.2f}" for k,x in zip(K,med[a])))
for a in ['cand','head']:
    if a not in med: continue
    for b in ['control','b13base']:
        print(f"[simcost]   {a}/{b}: " + ', '.join(f"{k} {x/y:.3f}" for k,x,y in zip(K,med[a],med[b])))
PY
