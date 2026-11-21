#!/bin/bash
# Runs ON the second Mac (the Air), inside air-run.sh --exec, so it already holds the box's queue lock and runs under
# caffeinate with a hard time limit. Copied to ~/cm-remote/bin/air-sim-job.sh by tools/remote/air-sim.sh on every call.
# macOS /bin/bash is 3.2: no bash-4 features here.
#   air-sim-job.sh --id RUN_ID --suite fast|slow|all [--jobs N]
# Input:  ~/cm-remote/runs/RUN_ID/src.tgz  (git archive of the commit under test: Assets/_Game/{Sim,Sim.Tests,Net},
#         tools, docs, ProjectSettings), uploaded by the caller.
# Work:   ~/cm-remote/sim/src  (one persistent tree; rsync --checksum without times keeps unchanged files untouched,
#         so the .NET build stays incremental and a changed file always gets a fresh timestamp).
# Output: ~/cm-remote/runs/RUN_ID/sim-results/ci/{sim-results.xml,sim-build.log}, the same files and layout as the
#         sim-results artifact of .github/workflows/sim-tests.yml, plus job.txt (wall time, jobs, peak memory).
# Exit: 0 pass, 1 test failure (or a split that lost tests), 2 compile failure, 3 bad input.
set -u
ID=""; SUITE=fast; JOBS=1
while [ $# -gt 0 ]; do
  case "$1" in
    --id) ID="$2"; shift 2 ;;
    --suite) SUITE="$2"; shift 2 ;;
    --jobs) JOBS="$2"; shift 2 ;;
    *) echo "air-sim-job: unknown flag $1" >&2; exit 3 ;;
  esac
done
BASE="$HOME/cm-remote"; RUN="$BASE/runs/$ID"; SRC="$BASE/sim/src"; RES="$RUN/sim-results/ci"
[ -n "$ID" ] && [ -f "$RUN/src.tgz" ] || { echo "air-sim-job: no $RUN/src.tgz" >&2; exit 3; }
case "$SUITE" in fast) FLAG=''; WHERE='cat != Slow' ;; slow) FLAG='--slow'; WHERE='cat == Slow' ;; all) FLAG='--all'; WHERE='' ;;
  *) echo "air-sim-job: bad suite $SUITE" >&2; exit 3 ;; esac
case "$JOBS" in ''|*[!0-9]*) JOBS=1 ;; esac
PY="$(command -v python3 || echo /usr/bin/python3)"
now() { perl -MTime::HiRes=time -e 'printf "%.1f\n", time'; }
t0=$(now)
mkdir -p "$RES" "$SRC" "$RUN/stage"
tar -xzf "$RUN/src.tgz" -C "$RUN/stage" || exit 3
rsync -rlc --delete --exclude 'bin/' --exclude 'obj/' --exclude '/Temp/' "$RUN/stage/" "$SRC/" || exit 3
rm -rf "$RUN/stage" "$RUN/src.tgz"
cd "$SRC" || exit 3
rm -rf Temp/ci Temp/balance
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0   # no build servers left running after the job

# Memory sampler (every 5 s): summed resident memory of the dotnet processes (only this job runs dotnet on the box).
( while :; do ps -Ao rss=,comm= | awk '/dotnet/ {r+=$1} END {printf "%.0f\n", r/1024}'; sleep 5; done ) > "$RUN/rss.txt" 2>/dev/null &
SAMP=$!
trap 'kill $SAMP 2>/dev/null; dotnet build-server shutdown >/dev/null 2>&1' EXIT

avail_mb() { vm_stat | awk '/Pages free/ {f=$3} /Pages inactive/ {i=$3} /Pages speculative/ {s=$3} END {printf "%d", (f+i+s)*16384/1048576}'; }
used=1
if [ "$JOBS" -le 1 ]; then
  # Exactly what the workflow runs.
  tools/ci/sim-tests.sh $FLAG; rc=$?
else
  tools/ci/sim-tests.sh --compile-only; rc=$?
  if [ $rc -eq 0 ]; then
    EXE=tools/ci/SimTests/bin/Release/net8.0/CriticalMass.Sim.Tests.dll
    mkdir -p Temp/ci/split
    # Memory guard (8 GB box, the owner's apps keep running): about 700 MB per test process, never below one.
    a=$(avail_mb); cap=$(( a / 700 )); [ $cap -lt 1 ] && cap=1
    used=$JOBS; [ $used -gt $cap ] && used=$cap
    echo "air-sim-job: ${a} MB free or reclaimable; ${used} test process(es) (asked ${JOBS})"
    if [ "$used" -le 1 ]; then
      tools/ci/sim-tests.sh $FLAG; rc=$?
    else
      if [ -n "$WHERE" ]; then dotnet "$EXE" --noheader "--where=$WHERE" "--explore=Temp/ci/split/explore.xml" > /dev/null 2>&1
      else dotnet "$EXE" --noheader "--explore=Temp/ci/split/explore.xml" > /dev/null 2>&1; fi
      # Bins by fixture class, longest first onto the lightest bin, weights from the committed table (seconds on the
      # Air; a class missing from it counts 60 s). Prints one line per bin: space-separated class names.
      "$PY" -I - Temp/ci/split/explore.xml "$used" "$BASE/bin/air-sim-weights.tsv" > Temp/ci/split/bins.txt <<'PY'
import sys, xml.etree.ElementTree as ET
xml, n, wf = sys.argv[1], int(sys.argv[2]), sys.argv[3]
w = {}
try:
    for line in open(wf):
        p = line.split()
        if len(p) >= 2 and not line.startswith('#'):
            w[p[1]] = float(p[0])
except OSError:
    pass
classes = {}
for ts in ET.parse(xml).getroot().iter('test-suite'):
    if ts.get('type') in ('TestFixture', 'ParameterizedFixture') and ts.get('classname'):
        classes.setdefault(ts.get('classname'), 0)
        classes[ts.get('classname')] += int(ts.get('testcasecount') or 0)
bins = [[0.0, []] for _ in range(n)]
for c in sorted(classes, key=lambda c: -w.get(c, 60.0)):
    b = min(bins, key=lambda b: b[0]); b[0] += w.get(c, 60.0); b[1].append(c)
for b in bins:
    if b[1]:
        print(' '.join(b[1]))
PY
      expected=$("$PY" -I -c 'import sys,xml.etree.ElementTree as ET; print(ET.parse(sys.argv[1]).getroot().get("testcasecount") or 0)' Temp/ci/split/explore.xml 2>/dev/null)
      i=0; pids=""
      while read -r line; do
        [ -z "$line" ] && continue
        i=$((i + 1)); f=""
        for c in $line; do f="${f:+$f || }class == $c"; done
        dotnet "$EXE" --noheader --labels=Off "--result=Temp/ci/split/part-$i.xml" "--where=${WHERE:+$WHERE && }($f)" \
          > "Temp/ci/split/console-$i.txt" 2>&1 &
        pids="$pids $!"
      done < Temp/ci/split/bins.txt
      for p in $pids; do wait "$p"; done
      cat Temp/ci/split/console-*.txt
      # One NUnit 3 result file, the shape the workflow writes: summed totals on <test-run>, every part's suites inside.
      "$PY" -I - Temp/ci/sim-results.xml "${expected:-0}" "$(perl -e "printf '%.3f', $(now) - $t0")" Temp/ci/split/part-*.xml <<'PY'
import sys, xml.etree.ElementTree as ET
out, expected, wall, parts = sys.argv[1], int(sys.argv[2]), sys.argv[3], sys.argv[4:]
keys = ('testcasecount', 'total', 'passed', 'failed', 'warnings', 'inconclusive', 'skipped', 'asserts')
tot = dict.fromkeys(keys, 0); kids = []; first = None
for p in parts:
    try:
        r = ET.parse(p).getroot()
    except (OSError, ET.ParseError):
        tot['failed'] += 1; continue   # a part that crashed counts as a failure
    first = first or r
    for k in keys:
        tot[k] += int(r.get(k, 0) or 0)
    kids += [c for c in r if c.tag == 'test-suite']
root = ET.Element('test-run', dict(first.attrib) if first is not None else {})
for k in keys:
    root.set(k, str(tot[k]))
lost = expected and tot['total'] != expected
root.set('result', 'Failed' if tot['failed'] or lost or len(kids) != len(parts) else 'Passed')
root.set('duration', wall); root.set('split', str(len(parts)))
if lost:
    root.set('split-lost', f"{expected - tot['total']}")
    print(f"air-sim-job: the split ran {tot['total']} of {expected} tests", file=sys.stderr)
root.extend(kids)
ET.ElementTree(root).write(out, encoding='utf-8', xml_declaration=True)
print(f"air-sim-job: merged {len(parts)} parts: {tot['passed']} passed, {tot['failed']} failed, {tot['skipped']} skipped of {tot['total']}")
sys.exit(0 if root.get('result') == 'Passed' else 1)
PY
      rc=$?
    fi
  fi
fi
t1=$(now)
kill $SAMP 2>/dev/null
cp Temp/ci/sim-results.xml Temp/ci/sim-build.log "$RES/" 2>/dev/null
cp Temp/ci/*.cs "$RES/" 2>/dev/null
[ -f Temp/balance/TUNING-INDEX.md ] && { mkdir -p "$RUN/sim-results/balance"; cp Temp/balance/TUNING-INDEX.md "$RUN/sim-results/balance/"; }
peak=$(sort -n "$RUN/rss.txt" 2>/dev/null | tail -1)
printf 'suite=%s jobs=%s used=%s rc=%s wall_s=%s peak_dotnet_rss_mb=%s host=%s\n' "$SUITE" "$JOBS" "$used" "$rc" \
  "$(perl -e "printf '%.1f', $t1 - $t0")" "${peak:-0}" "$(scutil --get LocalHostName 2>/dev/null)" | tee "$RUN/job.txt"
rm -f "$RUN/rss.txt"
exit $rc
