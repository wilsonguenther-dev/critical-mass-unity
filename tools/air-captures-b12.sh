#!/usr/bin/env bash
# b12-t: ONE script for the batch 12 Air play-test list (~/cm-incidents/BATCH-12-ORDER.md, "Air play-test list", steps 1
# to 10). Every run goes through tools/remote-playtest.sh (copy the .app to the Air, run it hidden, copy the logs and
# pictures back); windows stay hidden: -showWindow is refused. One match at a time (remote-playtest's queue lock).
#   tools/air-captures-b12.sh --app CANDIDATE.app [--control CONTROL.app] [--host HOST] [--out DIR] [--only STEP[,STEP...]]
#                             [--quality Medium] [--seed 4242] [--ab-switches LIST] [--fog-args "FLAGS"] [--fog-off-args "FLAGS"]
#                             [--online-args "FLAGS"] [--dry-run] [--yes]
#   STEP: a number 1..10 or its name; "cleanup" (not in the default list) removes batch builds from the Air (--yes to delete).
#    1 frame-control  control then candidate smoke (Medium, 1 match, -seed 4242), then the candidate with -sharpOff all;
#                     one run per AB switch off only when the candidate is over budget. Writes OUT/1-frame-control/compare.txt:
#                     the [CM.Perf] scope and clearance lines, idle CPU, worst hitch, frames over 33 ms (counter AND recorder).
#    2 ab-stills      -characterShots with and without -sharpOff all; Warrior -warriorTex 0 and 1
#    3 fog            -fogShots (P stages fog on and off stills itself), then one smoke with -fog off
#    4 kaiju-types    -kaijuTypeShots (GROVE, BRUTE, standard at hatch)
#    5 grenade        --affordance-shots (-affordanceShots) and -grenadeShots
#    6 t-shots        -kaijuKoShots and -goreShots (aftermath scene), and the [CM.KaijuOwn] line (from step 1, else one smoke)
#    7 reach          -hitFeelShots, all six classes
#    8 screens        --no-autoplay -flowShots (results screen); 60 s on the title (idle CPU); first-match load hitch line
#    9 clips          -characterShotsOnly healer and demolitions at Medium
#   10 online         one hidden Air copy hosting through the real relay (--online-args, from W's docs/windows-crossplay.md)
#                     while W's crossplay workflow joins from a GitHub Windows runner (mode mac_host_external)
# Player flags and who adds them in batch 12 (existing = already in c1f3bff):
#   existing: -seed -characterShots -characterShotsOnly -affordanceShots -goreShots -hitFeelShots -flowShots
#   added by T: -kaijuKoShots, the goreShots aftermath scene, the [CM.Perf] / [CM.KaijuOwn] lines
#   added by AB: -sharpOff <normals,rim,upscale,msaa,lod0|all>, -warriorTex 1 (anything else = old Warrior)
#   added by P: -fogShots [dir] (stills with fog on and off), -fog on|off (pin)    added by R: -kaijuTypeShots [dir]
#   added by JK: -grenadeShots, -affordanceShots (existing, extended)    T: -kaijuKoShots [dir], -goreShots
#   added by C2: the hitFeelShots fixes (dummy at sim reach, Builder side strip)    added by X1: the results / idle / load lines
#   added by W: the crossplay workflow's mac_host_external mode and the online host flags (--online-args)
# Output: everything under OUT/<n>-<name>/<run>/ (Player.log, perf*.json, summary.json, data/ with pictures); each step
# prints ONE line: step, runs and their exit codes, pictures, logs, and OUT/<n>-<name>/files.txt (every file it produced).
# Capture runs use --mode smoke (no -shots, so no second camera script) and quit before a match end, so remote-playtest's smoke verdict reads FAIL for them by design: judge the pictures.
# Exit: 0 when every run exited 0 or was a capture run, 1 when a smoke run failed, 64 usage.
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HOST="${CM_REMOTE_HOST:-wilsons-macbook-air.local}"
APP=""; CONTROL=""; OUT=""; ONLY=""; QUALITY=Medium; SEED=4242; DRY=0; YES=0
AB_SWITCHES="normals,rim,upscale,msaa,lod0"   # AB's switch names (SharpSwitches.cs: -sharpOff normals|rim|upscale|msaa|lod0|all)
FOG_ARGS="-fogShots"; FOG_OFF_ARGS="-fog off"   # P (FogConfig.cs): -fogShots [dir] stages the stills with the fog on AND off; -fog on|off pins it (here: a fog-off smoke)
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
    --ab-switches) AB_SWITCHES="${2:?}"; shift 2 ;;
    --fog-args) FOG_ARGS="${2:?}"; shift 2 ;;
    --fog-off-args) FOG_OFF_ARGS="${2:?}"; shift 2 ;;
    --online-args) ONLINE_ARGS="${2:?}"; shift 2 ;;
    --dry-run) DRY=1; shift ;;
    --yes) YES=1; shift ;;
    -h|--help) usage ;;
    *) echo "air-captures-b12: unknown flag $1" >&2; usage ;;
  esac
done
[[ -n "$APP" ]] || { echo "air-captures-b12: --app CANDIDATE.app is required" >&2; usage; }
OUT="${OUT:-$HOME/cm-b12-air/$(date -u +%Y%m%dT%H%M%SZ)}"
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
case "$(printf '%s %s %s %s' "$FOG_ARGS" "$FOG_OFF_ARGS" "$ONLINE_ARGS" "$AB_SWITCHES" | tr 'A-Z' 'a-z')" in
  *-showwindow*) echo "air-captures-b12: -showWindow is not allowed (automated runs stay hidden)" >&2; exit 64 ;;
esac

ALL="1 2 3 4 5 6 7 8 9 10"
name_of() {
  case "$1" in
    1) echo frame-control ;; 2) echo ab-stills ;; 3) echo fog ;; 4) echo kaiju-types ;; 5) echo grenade ;;
    6) echo t-shots ;; 7) echo reach ;; 8) echo screens ;; 9) echo clips ;; 10) echo online ;; cleanup) echo cleanup ;;
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
    n="$(num_of "$s")"; [[ -n "$n" ]] || { echo "air-captures-b12: unknown step $s" >&2; usage; }
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
    case "$(echo "$a" | tr 'A-Z' 'a-z')" in *-showwindow*) echo "air-captures-b12: refusing -showWindow in $label" >&2; return 64 ;; esac
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
  run candidate-sharpoff-all "$APP" 0 --mode smoke --matches 1 --player-args "$seedargs -sharpOff all"   # -sharpOff: added by AB
  local over=""
  if [[ -f "$STEP_DIR/candidate/summary.json" ]]; then
    over="$(python3 -c 'import json,sys; print(" ".join(json.load(open(sys.argv[1])).get("over_budget") or []))' "$STEP_DIR/candidate/summary.json" 2>/dev/null)"
  fi
  if [[ -n "$over" ]]; then
    local sw
    for sw in $(echo "$AB_SWITCHES" | tr ',' ' '); do
      run "candidate-sharpoff-$sw" "$APP" 0 --mode smoke --matches 1 --player-args "$seedargs -sharpOff $sw"
    done
  fi
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
          f"frames_over_ms(counter)={s.get('frames_over_ms')} over_budget={s.get('over_budget')}")
    print(f"   frame_recorder={json.dumps(rec)[:400] if rec is not None else None}")
    for l in re.findall(r'\[CM\.(?:Perf|KaijuOwn)\][^\n]*', txt):
        print('   ' + l)
PY
  done_step 1 "compare: $STEP_DIR/compare.txt"
}

step2() {
  begin 2
  run chars-default "$APP" 1 --mode smoke --player-args "-characterShots"
  run chars-sharpoff-all "$APP" 1 --mode smoke --player-args "-characterShots -sharpOff all"   # added by AB
  run warrior-tex0 "$APP" 1 --mode smoke --player-args "-characterShotsOnly warrior -warriorTex 0"   # added by AB
  run warrior-tex1 "$APP" 1 --mode smoke --player-args "-characterShotsOnly warrior -warriorTex 1"   # added by AB
  done_step 2
}

step3() {
  begin 3
  run fog-on "$APP" 1 --mode smoke --player-args "$FOG_ARGS"       # added by P
  run fog-off "$APP" 1 --mode smoke --player-args "$FOG_OFF_ARGS"  # added by P
  done_step 3 "fog flags: on='$FOG_ARGS' off='$FOG_OFF_ARGS' (P names them)"
}

step4() { begin 4; run kaiju-types "$APP" 1 --mode smoke --player-args "-kaijuTypeShots"; done_step 4; }   # added by R

step5() {
  begin 5
  run affordance "$APP" 1 --mode smoke --affordance-shots
  run grenade "$APP" 1 --mode smoke --player-args "-grenadeShots"   # added by JK
  done_step 5
}

step6() {
  begin 6
  run kaiju-ko "$APP" 1 --mode smoke --kaiju-ko-shots
  run gore-aftermath "$APP" 1 --mode smoke --gore-shots
  local own
  own="$(lines "$OUT/1-frame-control/candidate" '\[CM\.KaijuOwn\]' | head -1)"
  if [[ -z "$own" ]] && (( ! DRY )); then
    run kaiju-own "$APP" 0 --mode smoke --matches 1 --player-args "-seed $SEED"
    own="$(lines "$STEP_DIR/kaiju-own" '\[CM\.KaijuOwn\]' | head -1)"
  fi
  (( DRY )) || { lines "$STEP_DIR" '\[CM\.(KaijuKoShots|Gore)\]' > "$STEP_DIR/lines.txt"; echo "${own:-no [CM.KaijuOwn] line found}" >> "$STEP_DIR/lines.txt"; }
  done_step 6 "KO / gore / ownership lines: $STEP_DIR/lines.txt"
}

step7() { begin 7; run hitfeel "$APP" 1 --mode smoke --player-args "-hitFeelShots"; done_step 7; }   # fixed by C2

step8() {
  begin 8
  run flow "$APP" 1 --mode smoke --no-autoplay --player-args "-flowShots"
  run title-idle-60s "$APP" 0 --mode smoke --matches 1 --title-seconds 60
  (( DRY )) || lines "$STEP_DIR" '\[CM\.(Load|Hitch|Cost|Boot|Idle)\]' > "$STEP_DIR/lines.txt"
  done_step 8 "idle CPU in title-idle-60s/summary.json; load and title lines: $STEP_DIR/lines.txt"
}

step9() {
  begin 9
  run healer "$APP" 1 --mode smoke --player-args "-characterShotsOnly healer"
  run demolitions "$APP" 1 --mode smoke --player-args "-characterShotsOnly demolitions"
  done_step 9
}

step10() {
  begin 10
  if [[ -z "$ONLINE_ARGS" ]]; then
    STEP_RUNS="host=skipped"
    done_step 10 "needs --online-args (W's Air-host flags, docs/windows-crossplay.md) and W's crossplay workflow started with mode mac_host_external"
    return
  fi
  run online-host "$APP" 0 --mode smoke --matches 1 --player-args "$ONLINE_ARGS"   # added by W
  (( DRY )) || lines "$STEP_DIR" 'ONLINE SUMMARY|leadmin|leadmax|\[CM\.Online' > "$STEP_DIR/lines.txt"
  done_step 10 "ONLINE SUMMARY lines: $STEP_DIR/lines.txt"
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

echo "air-captures-b12: $(date '+%a %Y-%m-%d %H:%M %Z') host $HOST, candidate $APP, control ${CONTROL:-none}, out $OUT, steps$STEPS"
for n in $STEPS; do "step$n"; done
echo "air-captures-b12: done $(date '+%H:%M %Z'), out $OUT"
exit "$SMOKE_FAILED"
