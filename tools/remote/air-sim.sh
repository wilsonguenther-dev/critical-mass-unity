#!/usr/bin/env bash
# The no-Unity Sim tests on the second Mac (the Air) instead of GitHub (feat/air-test-machine, 2026-11-21).
# Normally called by `loop/gates/remote sim --on air`, which falls back to GitHub on exit 69/75/67; usable alone:
#   tools/remote/air-sim.sh --suite fast|slow|all --ref BRANCH [--sha COMMIT] [--jobs N] [--wait MIN] [--timeout MIN] [--out DIR]
# Only our code runs there: COMMIT must be reachable from BRANCH as it is on origin (our repo on GitHub, checked with
# ls-remote + fetch of that one branch), else exit 77 and nothing is sent.
# What it does: packs the COMMITTED tree of COMMIT (default HEAD; uncommitted edits are not tested, as on GitHub) with
# `git archive` (Sim, Sim.Tests, Net, tools, docs, ProjectSettings: about 2 MB), copies it to ~/cm-remote/runs/<id>/
# on the box, and runs tools/remote/air-sim-job.sh there through air-run.sh: the box's queue lock (one job at a time;
# a second caller waits up to --wait minutes, default 30, then gives up with 75 so the gate can use GitHub), caffeinate,
# a hard limit (--timeout, default 75 min, the whole job's process group is stopped). --jobs N (default 4) splits the
# tests by fixture over N processes (the box caps N by free memory); --jobs 1 runs tools/ci/sim-tests.sh exactly as
# the workflow does. Results come back to OUT (default loop/logs/remote/air-<id>) in the workflow artifact's layout:
# sim-results/ci/sim-results.xml + sim-build.log, plus run.json ({conclusion, jobs}) like `gh run view --json`.
# Prints the results folder on stdout (last line). Exit: 0 pass, 1 test failure, 2 compile failure, 124 hard limit,
# 130 aborted, 77 commit not on our origin branch, 75 queue wait ran out, 67 box refused (battery under 25 % or disk under 12 GB), 69 unreachable, 64 usage.
# Nothing on the box outside ~/cm-remote is touched. Host: CM_REMOTE_HOST (default the ssh alias in _remote-lib.sh).
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(git -C "$PWD" rev-parse --show-toplevel 2>/dev/null || (cd "$HERE/../.." && pwd))"
source "$HERE/_remote-lib.sh"
SUITE=fast; SHA=HEAD; REF=''; JOBS="${CM_AIR_JOBS:-4}"; WAIT=30; TMIN=75; OUT=''
TRIES="${CM_AIR_TRIES:-3}"; GAP="${CM_AIR_TRY_GAP:-20}"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --suite) SUITE="${2:?}"; shift 2 ;;
    --sha) SHA="${2:?}"; shift 2 ;;
    --ref) REF="${2:?}"; shift 2 ;;
    --jobs) JOBS="${2:?}"; shift 2 ;;
    --wait) WAIT="${2:?}"; shift 2 ;;
    --timeout) TMIN="${2:?}"; shift 2 ;;
    --out) OUT="${2:?}"; shift 2 ;;
    *) sed -n '2,16p' "$0" >&2; exit 64 ;;
  esac
done
case "$SUITE" in fast|slow|all) ;; *) echo "air-sim: --suite fast|slow|all" >&2; exit 64 ;; esac
SHA="$(git -C "$ROOT" rev-parse --verify "$SHA^{commit}" 2>/dev/null)" || { echo "air-sim: no such commit" >&2; exit 64; }
# Our repo, our branch, a commit on it: never a fork, a PR ref or an arbitrary local commit.
REPO_RE='github\.com[:/]wilsonguenther-dev/critical-mass-unity(\.git)?$'
url="$(git -C "$ROOT" remote get-url origin 2>/dev/null)"
[[ "$url" =~ $REPO_RE ]] || { echo "air-sim: origin is not wilsonguenther-dev/critical-mass-unity; refusing" >&2; exit 77; }
[[ -n "$REF" && "$REF" != refs/* && "$REF" != */../* ]] || { echo "air-sim: --ref BRANCH is required (a branch of our repo)" >&2; exit 77; }
tip="$(git -C "$ROOT" ls-remote origin "refs/heads/$REF" 2>/dev/null | cut -f1)"
[[ -n "$tip" ]] || { echo "air-sim: branch $REF is not on origin; push it first" >&2; exit 77; }
git -C "$ROOT" cat-file -e "$tip^{commit}" 2>/dev/null || git -C "$ROOT" fetch -q origin "refs/heads/$REF" 2>/dev/null
git -C "$ROOT" merge-base --is-ancestor "$SHA" "$tip" 2>/dev/null \
  || { echo "air-sim: ${SHA:0:12} is not on origin/$REF (tip ${tip:0:12}); push it first" >&2; exit 77; }
ID="$(date -u +%Y%m%dT%H%M%SZ)-sim-$SUITE-$$"
OUT="${OUT:-$ROOT/loop/logs/remote/air-$ID}"

# Reachable? A few quick tries (the Air drops off its Wi-Fi now and then), then give up fast so the gate can fall back.
ok=0
for t in $(seq 1 "$TRIES"); do
  if rssh true 2>/dev/null; then ok=1; break; fi
  echo "air-sim: try $t of $TRIES: the Air does not answer" >&2
  (( t < TRIES )) && sleep "$GAP"
done
(( ok )) || { echo "air-sim: unreachable after $TRIES tries" >&2; exit 69; }
( remote_preflight ) || exit 69
rsync_to "$HERE/air-sim-job.sh" "$HERE/air-sim-weights.tsv" "$REMOTE_USER@$REMOTE_HOST:cm-remote/bin/" \
  || { echo "air-sim: could not copy the job script" >&2; exit 69; }

# The committed tree of SHA, only the paths that exist in it (git archive refuses a missing path).
paths=()
for p in Assets/_Game/Sim Assets/_Game/Sim.Tests Assets/_Game/Net tools docs ProjectSettings; do
  [[ -n "$(git -C "$ROOT" ls-tree "$SHA" -- "$p")" ]] && paths+=("$p")
done
git -C "$ROOT" archive --format=tar.gz "$SHA" -- "${paths[@]}" \
  | rssh "mkdir -p ~/cm-remote/runs/$ID && cat > ~/cm-remote/runs/$ID/src.tgz" \
  || { echo "air-sim: could not upload the source" >&2; exit 69; }
echo "air-sim: ${SHA:0:12} suite=$SUITE jobs=$JOBS on $REMOTE_HOST (job $ID, queue wait max ${WAIT} min, hard limit ${TMIN} min)" >&2

s0=$(date +%s)
remote_start "$ID" $(( WAIT * 60 + TMIN * 60 + 120 )) \
  --exec "bash ~/cm-remote/bin/air-sim-job.sh --id $ID --suite $SUITE --jobs $JOBS" \
  --lock-wait $(( WAIT * 60 )) --exec-timeout $(( TMIN * 60 ))
code="$REMOTE_CODE"
case "$code" in
  75|67) rssh "rm -rf ~/cm-remote/runs/$ID" 2>/dev/null; echo "air-sim: box busy or refused (exit $code); nothing ran" >&2; exit "$code" ;;
  69) echo "air-sim: lost the box during the job" >&2; exit 69 ;;
esac
mkdir -p "$OUT"
remote_collect "$ID" "$OUT" || { echo "air-sim: could not copy the results back" >&2; exit 69; }
[[ -f "$OUT/output.txt" ]] && cat "$OUT/output.txt" >&2
python3 -I - "$OUT" "$code" "$SHA" "$(( $(date +%s) - s0 ))" <<'PY'
import json, os, sys
d, code, sha, secs = sys.argv[1], int(sys.argv[2]), sys.argv[3], int(sys.argv[4])
job = {}
try:
    job = dict(kv.split('=', 1) for kv in open(os.path.join(d, 'job.txt')).read().split())
except (OSError, ValueError):
    pass
concl = 'success' if code == 0 else ('timed_out' if code == 124 else ('cancelled' if code == 130 else 'failure'))
json.dump({'conclusion': concl, 'runner': 'air', 'headSha': sha, 'seconds': secs, 'job': job,
           'jobs': [{'name': 'sim', 'conclusion': concl}]}, open(os.path.join(d, 'run.json'), 'w'))
PY
echo "$OUT"
exit "$code"
