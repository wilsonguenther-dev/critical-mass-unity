#!/usr/bin/env bash
# Launcher for the Critical Mass (Unity) build loop (decision 8). It never launches the Workflow itself: the
# orchestrator does that with the args file this script writes, then records the ids with record-launch.
#
#   loop/bin/launch.sh --cap <total tokens> [--batches 1] [--builders N] [--dry-run] [--schedule FILE.json] [--hard-stop 2026-11-14T23:00:00Z]
#
# --builders N: the builder limit, 1..12 (U-037; default 5); the integrator, fix seat and reviewer run alone after
# the builders, so N is also the peak agent count.
# --batches N[,M]: the args file carries only those _ORDER.md batches, so the workflow stops after them (panel
# revision 2026-11-12: "batch 1 alone" is enforced in code, not by hoping the cap stops the run).
#
# --cap is REQUIRED (U-014: the workflow's own reported total tokens). Without it the launcher refuses before
# anything else. Pre-launch checklist (all must PASS or a real launch refuses): spend cap, disk >= 25 GB, item
# validator green, at least one item, no stale lock/stall/halt files, no orphan watcher pids, one loop at a time,
# no leftover lanes, main checked out and clean, main == origin/main, Unity license present, no Unity editor open on
# the main checkout, build-only (there is no deploy step for this repo; --deploy is refused).
# --dry-run: prints the checklist, says whether a real launch would refuse, starts every watcher in dry mode (they
# text nothing and write no stop files) and writes the args file; stop them with loop/bin/shutdown.sh --dry-run.
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOOP="$(cd "$HERE/.." && pwd)"
CM="$HERE/cmloop.py"
CAP=""; DRY=0; SCHED=""; HARD=""; ITEMS_DIR=""; BATCHES=""; BUILDERS="5"
while [ $# -gt 0 ]; do
  case "$1" in
    --cap) CAP="${2:-}"; shift 2 ;;
    --dry-run) DRY=1; shift ;;
    --schedule) SCHED="${2:-}"; shift 2 ;;
    --hard-stop) HARD="${2:-}"; shift 2 ;;
    --items-dir) ITEMS_DIR="${2:-}"; shift 2 ;;
    --batches) BATCHES="${2:-}"; shift 2 ;;
    --builders) BUILDERS="${2:-}"; shift 2 ;;
    --build-only) shift ;;
    --deploy) echo "launch: REFUSED: there is no deploy step for this repo (build-only, U-013)"; exit 2 ;;
    *) echo "launch: unknown argument $1"; exit 2 ;;
  esac
done
if [ -z "$CAP" ]; then
  echo "launch: REFUSED: --cap <total tokens> is required (the U-014 measure; Wilson sets the number)"; exit 2
fi
case "$CAP" in *[!0-9]*|0) echo "launch: REFUSED: --cap must be a positive whole number of tokens, not '$CAP'"; exit 2 ;; esac
case "$BUILDERS" in ''|*[!0-9]*|0) echo "launch: REFUSED: --builders must be 1..12, not '$BUILDERS'"; exit 2 ;; esac
if [ "$BUILDERS" -gt 12 ]; then echo "launch: REFUSED: --builders $BUILDERS is over the ceiling of 12 (U-037)"; exit 2; fi

NOW="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
MODE="real"; [ $DRY -eq 1 ] && MODE="dry-run"
echo "launch ($MODE) at $NOW, cap $CAP total tokens"
python3 "$CM" preflight --cap "$CAP" ${ITEMS_DIR:+--items-dir "$ITEMS_DIR"}
PF=$?
if [ $PF -ne 0 ]; then
  if [ $DRY -eq 0 ]; then echo "launch: REFUSED: the checklist has a FAIL line"; exit 1; fi
  echo "launch (dry-run): a real launch would REFUSE on the FAIL line(s) above; continuing in dry mode only"
else
  echo "launch: checklist all PASS"
fi

eval "$(python3 -c 'import sys; sys.path.insert(0, sys.argv[1]); import cmloop as C; print(f"STATE={C.STATE}; LOGS={C.LOGS}")' "$HERE")"
mkdir -p "$STATE/pids" "$LOGS"
python3 - "$HERE" "$CAP" "$NOW" "$DRY" "$SCHED" "$HARD" <<'PY'
import json, sys, time
sys.path.insert(0, sys.argv[1]); import cmloop as C
cap, now, dry, sched, hard = int(sys.argv[2]), sys.argv[3], sys.argv[4] == '1', sys.argv[5], sys.argv[6]
C.write_json(C.STATE / 'run.json', {'status': 'dry' if dry else 'running', 'dry': dry, 'cap': cap, 'started': now,
             'started_epoch': time.time(), 'harness_commit': C.branch_sha('HEAD'), 'disk_before_gb': round(C.free_gb(), 1)})
if sched or hard:
    s = json.loads(open(sched).read()) if sched else {'checkpoints': []}
    if hard:
        s['hard_stop'] = hard
    for cp in s.get('checkpoints', []):
        assert {'name', 'expected_by', 'items'} <= set(cp), f'checkpoint needs name, expected_by, items: {cp}'
    C.write_json(C.STATE / 'schedule.json', s)
PY
[ $? -eq 0 ] || { echo "launch: could not write run.json / schedule.json"; exit 1; }

FAILED=0
for W in stall memory clock disk spend; do
  if [ $DRY -eq 1 ]; then
    nohup python3 "$LOOP/watchers/watch.py" "$W" --dry-run >> "$LOGS/watch-$W.log" 2>&1 < /dev/null &
  else
    nohup python3 "$LOOP/watchers/watch.py" "$W" >> "$LOGS/watch-$W.log" 2>&1 < /dev/null &
  fi
  BGPID=$!
  for _ in 1 2 3 4 5 6 7 8 9 10; do [ -f "$STATE/pids/$W.pid" ] && break; sleep 0.3; done
  REC="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["pid"])' "$STATE/pids/$W.pid" 2>/dev/null)"
  if [ "$REC" = "$BGPID" ] && ps -p "$BGPID" -o command= >/dev/null; then
    echo "watcher $W started: pid $BGPID (pidfile $STATE/pids/$W.pid)"
  else
    echo "watcher $W FAILED to start (bg pid $BGPID, pidfile pid '${REC}')"; FAILED=1
  fi
done
if [ $FAILED -ne 0 ]; then echo "launch: a watcher failed; stopping the others"; python3 "$CM" watchers-stop; exit 1; fi

ARGS="$LOGS/launch-args.json"
python3 "$CM" plan --cap "$CAP" --now "$NOW" --out "$ARGS" ${ITEMS_DIR:+--items-dir "$ITEMS_DIR"} ${BATCHES:+--batches "$BATCHES"} --builders "$BUILDERS" --show || exit 1
SENT="The loop launcher ran ($MODE) with a cap of $CAP tokens; watchers are running."
if [ $DRY -eq 1 ]; then python3 "$CM" livelog --dry-run "$SENT"; else python3 "$CM" livelog "$SENT"; fi
echo
if [ $DRY -eq 1 ]; then
  echo "dry-run: nothing launched. Stop the watchers with: loop/bin/shutdown.sh --dry-run"
else
  echo "NEXT (orchestrator): Workflow({scriptPath: \"$LOOP/workflow/critical-mass-unity.js\", args: <contents of $ARGS>})"
  echo "THEN within 10 minutes: python3 $CM record-launch --task-id <w...> --run-id <wf_...>"
  echo "     (builders refuse to start until both ids are recorded; the stall alarm fires at 10 minutes)"
fi
