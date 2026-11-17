#!/usr/bin/env bash
# b13 S0: ONE script for the batch 13 Air list (~/cm-incidents/BATCH-13-ORDER.md, "Air list", steps 1 to 9). Copied from
# tools/air-captures-b12.sh. Every run goes through tools/remote-playtest.sh (copy the .app to the Air, run it hidden, copy
# the logs and pictures back); windows stay hidden: -showWindow is refused. One match at a time (remote-playtest's queue lock).
#   tools/air-captures-b13.sh --app CANDIDATE.app [--control CONTROL.app] [--host HOST] [--out DIR] [--only STEP[,STEP...]]
#                             [--quality Medium] [--seed 4242] [--online-args "FLAGS"] [--dry-run] [--yes]
#   STEP: a number 1..9 or its name; "cleanup" (not in the default list) removes batch builds from the Air (--yes to delete).
#    1 frame       same-hour control: control then candidate smoke (Medium, 1 match, -seed 4242), back to back. Writes
#                  OUT/1-frame/compare.txt: the [CM.Perf] scopes, idle CPU, worst hitch, frames over 33 ms (counter AND recorder).
#                  Limit: candidate p95 at most 33.3 ms on Medium and at most control + 10%, mean at most control + 1 ms.
#    2 hut         H -hutShots (hut L0 to L5 both teams, mining, carrying, depositing, level-up, the HUD badge)
#    3 walls       B -wallShots (ghost green/red, short, tall, 5-cell, block fills a crater, jump a short wall, crack, stairs)
#    4 dodge       J -dodgeShots (dash streak, dodge out of a slam, jump a short wall) and -hitFeelShots (reach, Healer too)
#    5 kaiju       K -kaijuTypeShots (four types at hatch with callout, SHROUD top-down and third person with fog on)
#    6 corruption  C -corruptionShots (veins, carrier, meter at 1/5/9, feeding a downed Kaiju, failed and successful roll)
#    7 characters  V -characterShowcaseShots (frames and stills, six classes), then --no-autoplay -flowShots at 1280x720 and
#                  2560x1440 (CHARACTERS screen, each tab, the match HUD with every new slot)
#    8 replay      -replayShots (#190's list), then one recorded 16v16 (-seed 4242) played back to the end with -replayLast
#                  (fingerprint lines in OUT/8-replay/lines.txt: all equal)
#    9 online      one hidden Air copy hosting through the real relay (--online-args, from W's docs/windows-crossplay.md)
#                  while W's crossplay workflow joins from a GitHub Windows runner (mode mac_host_external): MATCH line
# Player flags (all in AutomationWindow.AutomationFlags since S0, so the window stays hidden):
#   H -hutShots  B -wallShots  J -dodgeShots (+ existing -hitFeelShots)  K -kaijuTypeShots  C -corruptionShots
#   V -characterShowcaseShots (+ existing -flowShots)  #190 -replayShots, -replayLast  W the online host flags (--online-args)
# Output: everything under OUT/<n>-<name>/<run>/ (Player.log, perf*.json, summary.json, data/ with pictures); each step
# prints ONE line: step, runs and their exit codes, pictures, logs, and OUT/<n>-<name>/files.txt (every file it produced).
# Capture runs use --mode smoke (no -shots, so no second camera script) and quit before a match end, so remote-playtest's smoke verdict reads FAIL for them by design: judge the pictures.
# Exit: 0 when every run exited 0 or was a capture run, 1 when a smoke run failed, 64 usage.
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HOST="${CM_REMOTE_HOST:-wilsons-macbook-air.local}"
APP=""; CONTROL=""; OUT=""; ONLY=""; QUALITY=Medium; SEED=4242; DRY=0; YES=0
ONLINE_ARGS=""
KEEP_BUILDS="c2af29b 79b8afb"
usage() { sed -n '2,8p' "${BASH_SOURCE[0]}" | sed 's/^# *//' >&2; exit 64; }
while [[ $# -gt 0 ]]; do
  case "$1" in
    --app) APP="${2:?}"; shift 2 ;;
    --control) CONTROL="${2:?}"; shift 2 ;;
    --host) HOST="${2:?}"; shift 2 ;;
    --out) OUT="${2:?}"; shift 2 ;;
    --only) ONLY="${2:?}"; shift 2 ;;
    --quality) QUALITY="${2:?}"; shift 2 ;;
    --seed) SEED="${2:?}"; shift 2 ;;
    --online-args) ONLINE_ARGS="${2:?}"; shift 2 ;;
    --dry-run) DRY=1; shift ;;
    --yes) YES=1; shift ;;
    -h|--help) usage ;;
    *) echo "air-captures-b13: unknown flag $1" >&2; usage ;;
  esac
done
[[ -n "$APP" ]] || { echo "air-captures-b13: --app CANDIDATE.app is required" >&2; usage; }
OUT="${OUT:-$HOME/cm-b13-air/$(date -u +%Y%m%dT%H%M%SZ)}"
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
case "$(printf '%s' "$ONLINE_ARGS" | tr 'A-Z' 'a-z')" in
  *-showwindow*) echo "air-captures-b13: -showWindow is not allowed (automated runs stay hidden)" >&2; exit 64 ;;
esac

ALL="1 2 3 4 5 6 7 8 9"
name_of() {
  case "$1" in
    1) echo frame ;; 2) echo hut ;; 3) echo walls ;; 4) echo dodge ;; 5) echo kaiju ;;
    6) echo corruption ;; 7) echo characters ;; 8) echo replay ;; 9) echo online ;; cleanup) echo cleanup ;;
    *) echo "" ;;
  esac
}
num_of() {   # a step number or name -> its number ("cleanup" stays)
  local s="$1" n
  for n in $ALL cleanup; do [[ "$s" == "$n" || "$s" == "$(name_of "$n")" ]] && { echo "$n"; return; }; done
  echo ""
}
STEPS="$ALL"
if [[ -n "$ONLY" ]]; then
  STEPS=""
  for s in $(echo "$ONLY" | tr ',' ' '); do
    n="$(num_of "$s")"; [[ -n "$n" ]] || { echo "air-captures-b13: unknown step $s" >&2; usage; }
    STEPS="$STEPS $n"
  done
fi

SMOKE_FAILED=0
STEP_DIR=""; STEP_RUNS=""
# run LABEL APP CAPTURE(0|1) [remote-playtest flags...]: one hidden run into STEP_DIR/LABEL
run() {
  local label="$1" app="$2" capture="$3"; shift 3
  local a
  for a in "$@"; do
    case "$(echo "$a" | tr 'A-Z' 'a-z')" in *-showwindow*) echo "air-captures-b13: refusing -showWindow in $label" >&2; return 64 ;; esac
  done
  local out="$STEP_DIR/$label"
  local cmd=(bash "$ROOT/tools/remote-playtest.sh" --host "$HOST" --app "$app" --quality "$QUALITY" --out "$out" "$@")
  if (( DRY )); then
    printf 'DRY %s:' "$label"; printf ' %q' "${cmd[@]}"; printf '\n'
    STEP_RUNS="$STEP_RUNS $label=dry"
    return 0
  fi
  mkdir -p "$out"
  "${cmd[@]}" > "$out/remote-playtest.out" 2>&1
  local rc=$?
  STEP_RUNS="$STEP_RUNS $label=$rc"
  if (( rc != 0 && capture == 0 )); then SMOKE_FAILED=1; fi
  return 0
}

begin() { STEP_DIR="$OUT/$1-$(name_of "$1")"; STEP_RUNS=""; mkdir -p "$STEP_DIR"; }

# done_step N [NOTE]: the one line per step and its files.txt
done_step() {
  local n="$1" note="${2:-}"
  local list="$STEP_DIR/files.txt"
  find "$STEP_DIR" -type f ! -name files.txt 2>/dev/null | sort > "$list"
  local pics logs
  pics="$(grep -c '\.png$' "$list" 2>/dev/null)"; pics="${pics:-0}"
  logs="$(grep -c 'Player\.log$' "$list" 2>/dev/null)"; logs="${logs:-0}"
  echo "step $n $(name_of "$n"): runs[${STEP_RUNS# }] pictures=$pics logs=$logs files=$list${note:+ | $note}"
}

# lines DIR PATTERN: matching Player.log lines under DIR (one grep process)
lines() { find "$1" -name Player.log -print0 2>/dev/null | xargs -0 grep -h -E "$2" 2>/dev/null || true; }

step1() {
  begin 1
  local seedargs="-seed $SEED"
  if [[ -n "$CONTROL" ]]; then run control "$CONTROL" 0 --mode smoke --matches 1 --player-args "$seedargs"
  else STEP_RUNS="$STEP_RUNS control=skipped(no --control)"; fi
  run candidate "$APP" 0 --mode smoke --matches 1 --player-args "$seedargs"
  (( DRY )) || python3 - "$STEP_DIR" > "$STEP_DIR/compare.txt" 2>&1 <<'PY'
import json, os, re, sys
root = sys.argv[1]
for run in sorted(os.listdir(root)):
    d = os.path.join(root, run)
    if not os.path.isdir(d):
        continue
    try:
        s = json.load(open(os.path.join(d, 'summary.json')))
    except (OSError, ValueError):
        s = {}
    try:
        p = json.load(open(os.path.join(d, 'perf.json')))
    except (OSError, ValueError):
        p = {}
    txt = open(os.path.join(d, 'Player.log'), errors='replace').read() if os.path.exists(os.path.join(d, 'Player.log')) else ''
    meas = s.get('measured') or {}
    rec = p.get('frame_recorder')
    print(f"== {run}: pass={s.get('pass')} commit={s.get('commit')} quality={s.get('quality')}")
    print(f"   idle_cpu_pct={meas.get('idle_cpu_pct')} worst_hitch_ms={meas.get('worst_hitch_ms')} p95_ms={meas.get('p95_ms')} "
          f"mean_ms={meas.get('mean_ms')} frames_over_ms(counter)={s.get('frames_over_ms')} over_budget={s.get('over_budget')}")
    print(f"   frame_recorder={json.dumps(rec)[:400] if rec is not None else None}")
    for l in re.findall(r'\[CM\.Perf\][^\n]*', txt):
        print('   ' + l)
PY
  done_step 1 "compare: $STEP_DIR/compare.txt (limit: p95 <= 33.3 ms and <= control + 10%, mean <= control + 1 ms)"
}

step2() { begin 2; run hut "$APP" 1 --mode smoke --player-args "-hutShots"; done_step 2; }   # added by H

step3() { begin 3; run walls "$APP" 1 --mode smoke --player-args "-wallShots"; done_step 3; }   # added by B

step4() {
  begin 4
  run dodge "$APP" 1 --mode smoke --player-args "-dodgeShots"     # added by J
  run hitfeel "$APP" 1 --mode smoke --player-args "-hitFeelShots"  # existing, reach rows by J
  done_step 4
}

step5() { begin 5; run kaiju-types "$APP" 1 --mode smoke --player-args "-kaijuTypeShots"; done_step 5; }   # added by K

step6() { begin 6; run corruption "$APP" 1 --mode smoke --player-args "-corruptionShots"; done_step 6; }   # added by C

step7() {
  begin 7
  run showcase "$APP" 1 --mode smoke --player-args "-characterShowcaseShots"   # added by V
  run flow-720 "$APP" 1 --mode smoke --no-autoplay --player-args "-flowShots -screen-width 1280 -screen-height 720"
  run flow-1440 "$APP" 1 --mode smoke --no-autoplay --player-args "-flowShots -screen-width 2560 -screen-height 1440"
  done_step 7
}

step8() {
  begin 8
  run replay-shots "$APP" 1 --mode smoke --player-args "-replayShots"   # PR #190
  run record-16v16 "$APP" 0 --mode smoke --matches 1 --player-args "-seed $SEED"
  run playback "$APP" 1 --mode smoke --player-args "-replayLast"        # PR #190: plays the newest replay to the end
  (( DRY )) || lines "$STEP_DIR" '\[CM\.Replay' > "$STEP_DIR/lines.txt"
  done_step 8 "replay lines (fingerprints all equal): $STEP_DIR/lines.txt"
}

step9() {
  begin 9
  if [[ -z "$ONLINE_ARGS" ]]; then
    STEP_RUNS="host=skipped"
    done_step 9 "needs --online-args (W's Air-host flags, docs/windows-crossplay.md) and W's crossplay workflow started with mode mac_host_external"
    return
  fi
  run online-host "$APP" 0 --mode smoke --matches 1 --player-args "$ONLINE_ARGS"   # W
  (( DRY )) || lines "$STEP_DIR" 'ONLINE SUMMARY|MATCH|leadmin|leadmax|\[CM\.Online' > "$STEP_DIR/lines.txt"
  done_step 9 "ONLINE SUMMARY / MATCH lines: $STEP_DIR/lines.txt"
}

# cleanup: remove batch builds from the Air, keeping c2af29b, 79b8afb and the control build (--yes to delete)
stepcleanup() {
  begin cleanup
  local keep="$KEEP_BUILDS" c
  if [[ -n "$CONTROL" ]]; then
    c="$(LC_ALL=C grep -a -o -m1 -E 'commit=[0-9a-f]{7,40}' "$CONTROL/Contents/Resources/Data/resources.assets" 2>/dev/null | head -1 | cut -d= -f2)"
    [[ -n "$c" ]] && keep="$keep ${c:0:7}"
  fi
  # shellcheck source=/dev/null
  REMOTE_HOST="$HOST"; source "$ROOT/tools/remote/_remote-lib.sh"; REMOTE_HOST="$HOST"
  local builds b k drop=""
  builds="$(rssh 'ls -1 ~/cm-remote/builds 2>/dev/null' 2>/dev/null)"
  for b in $builds; do
    local kept=0
    for k in $keep; do [[ "$b" == "$k"* ]] && kept=1; done
    (( kept )) || drop="$drop $b"
  done
  echo "keep:$keep | remove:${drop:- none}" > "$STEP_DIR/cleanup.txt"
  if [[ -n "$drop" && $YES -eq 1 && $DRY -eq 0 ]]; then
    for b in $drop; do rssh "rm -rf ~/cm-remote/builds/$b"; done
    STEP_RUNS="removed=$(echo $drop | wc -w | tr -d ' ')"
  else
    STEP_RUNS="would-remove=$(echo $drop | wc -w | tr -d ' ') (pass --yes)"
  fi
  done_step cleanup "$(cat "$STEP_DIR/cleanup.txt")"
}

echo "air-captures-b13: $(date '+%a %Y-%m-%d %H:%M %Z') host $HOST, candidate $APP, control ${CONTROL:-none}, out $OUT, steps$STEPS"
for n in $STEPS; do "step$n"; done
echo "air-captures-b13: done $(date '+%H:%M %Z'), out $OUT"
exit "$SMOKE_FAILED"
