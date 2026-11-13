#!/bin/bash
# Runs ON the second Mac (the play-test box). tools/remote-playtest.sh copies this file to ~/cm-remote/bin/air-run.sh
# and starts it detached with nohup, so a dropped ssh connection never kills a run. macOS /bin/bash is 3.2: keep this
# file free of bash-4 features (no associative arrays, no mapfile, no ${x,,}).
#   air-run.sh --id RUN_ID --build DIR [--matches N] [--title-seconds S] [--timeout S] [--quality NAME] [--speed X]
#              [--novsync] [--fps-cap N] [--camera-yaw D] [--width W --height H] [--shots] [--affordance-shots] [--keep N] [--lock-wait S]
#   air-run.sh --id RUN_ID --exec 'COMMAND' [--lock-wait S]   (tools/remote-run.sh: any command, same queue lock, run
#              by bash in ~/cm-remote/work with ~/cm-remote/env.sh sourced: ffmpeg, the Python venv, dotnet on PATH;
#              output in runs/<id>/output.txt)
# Everything lives under ~/cm-remote: builds/<commit>/CriticalMass.app, runs/<id>/ (Player.log, perf*.json,
# samples.tsv, run.env, data/ with shot-*.png), lock.d/ (one job at a time), and the run's "state" file the
# caller polls: "uploaded", "waiting <who holds the lock>", "running <pid>", "done <exit code>".
# Exit codes written to state: the player's own (0 clean, 3 exceptions, 4 match timeout), 124 hard limit, 130 aborted
# (runs/<id>/ABORT exists), 75 lock wait timed out, 66 no app, 67 refused (low battery or low disk).
set -u
BASE="$HOME/cm-remote"
ID=""; BUILD=""; MATCHES=1; TITLE_S=12; TIMEOUT=900; QUALITY=""; SPEED=""; NOVSYNC=""; W=1920; H=1080; SHOTS=""; FPSCAP=""; CAMYAW=""
KEEP=2; LOCK_WAIT=3600; EXEC=""
while [ $# -gt 0 ]; do
  case "$1" in
    --id) ID="$2"; shift 2 ;;
    --build) BUILD="$2"; shift 2 ;;
    --matches) MATCHES="$2"; shift 2 ;;
    --title-seconds) TITLE_S="$2"; shift 2 ;;
    --timeout) TIMEOUT="$2"; shift 2 ;;
    --quality) QUALITY="$2"; shift 2 ;;
    --speed) SPEED="$2"; shift 2 ;;
    --novsync) NOVSYNC=1; shift ;;
    --width) W="$2"; shift 2 ;;
    --height) H="$2"; shift 2 ;;
    --shots) SHOTS=1; shift ;;
    --affordance-shots) AFF=1; shift ;;   # the player's -affordanceShots (feat/terrain-affordances captures)
    --fps-cap) FPSCAP="$2"; shift 2 ;;
    --camera-yaw) CAMYAW="$2"; shift 2 ;;
    --keep) KEEP="$2"; shift 2 ;;
    --exec) EXEC="$2"; shift 2 ;;
    --lock-wait) LOCK_WAIT="$2"; shift 2 ;;
    *) echo "air-run: unknown flag $1" >&2; exit 64 ;;
  esac
done
[ -n "$ID" ] && [ -n "$BUILD$EXEC" ] || { echo "air-run: --id and --build (or --exec) are required" >&2; exit 64; }
case "$BUILD" in ''|/*) ;; *) BUILD="$BASE/$BUILD" ;; esac   # relative = under ~/cm-remote
RUN="$BASE/runs/$ID"; mkdir -p "$RUN/data"
STATE="$RUN/state"
ms() { perl -MTime::HiRes=time -e 'printf "%d\n", time*1000'; }
say() { echo "$(date -u +%H:%M:%SZ) $*"; }
finish() { echo "done $1" > "$STATE"; say "done exit=$1"; exit "$1"; }

# ── one job at a time: mkdir is atomic; a lock whose runner pid is gone is stale and is taken over ──
LOCK="$BASE/lock.d"; got=""; t0=$(date +%s); last=""
while :; do
  [ -e "$RUN/ABORT" ] && finish 130
  if mkdir "$LOCK" 2>/dev/null; then
    printf 'pid=%s id=%s since=%s\n' "$$" "$ID" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$LOCK/owner"; got=1; break
  fi
  owner="$(cat "$LOCK/owner" 2>/dev/null)"
  opid="$(printf '%s' "$owner" | sed -n 's/^pid=\([0-9]*\).*/\1/p')"
  if [ -n "$opid" ] && ! kill -0 "$opid" 2>/dev/null; then
    say "stale lock ($owner): runner gone, taking it over"; rm -rf "$LOCK"; continue
  fi
  if [ -z "$opid" ] && [ $(( $(date +%s) - $(stat -f %m "$LOCK" 2>/dev/null || date +%s) )) -gt 30 ]; then
    rm -rf "$LOCK"; continue   # half-written lock older than 30 s
  fi
  if [ "$owner" != "$last" ]; then say "waiting: the box is busy ($owner)"; last="$owner"; fi
  echo "waiting $owner" > "$STATE"
  [ $(( $(date +%s) - t0 )) -ge "$LOCK_WAIT" ] && finish 75
  sleep 5
done

pid=""; cafe=""; muted_before=""; prefs=""
DOMAIN="com.drivia.criticalmass"   # the player's PlayerPrefs on macOS: exported before the run, restored after
cleanup() {
  [ -n "$pid" ] && kill "$pid" 2>/dev/null && sleep 2 && kill -9 "$pid" 2>/dev/null
  [ -n "$cafe" ] && kill "$cafe" 2>/dev/null
  if [ "$prefs" = 1 ]; then defaults import "$DOMAIN" "$RUN/prefs-before.plist" 2>/dev/null
  elif [ "$prefs" = 0 ]; then defaults delete "$DOMAIN" 2>/dev/null; fi
  if [ "$muted_before" = "false" ]; then osascript -e 'set volume output muted false' >/dev/null 2>&1; fi
  [ -n "$got" ] && rm -rf "$LOCK"
}
trap cleanup EXIT
trap 'finish 130' INT TERM HUP

# ── refuse on a nearly flat battery or a nearly full disk ──
batt="$(pmset -g batt | sed -n 's/.*[[:space:]]\([0-9]*\)%; \([a-z ]*\);.*/\1 \2/p' | head -1)"
case "$batt" in *discharging*) [ "${batt%% *}" -lt 25 ] && { say "refused: on battery at ${batt%% *}%"; finish 67; } ;; esac
free_gb=$(df -g "$HOME" | awk 'NR==2{print $4}')
[ "$free_gb" -lt 12 ] && { say "refused: only ${free_gb} GB free (floor 12)"; finish 67; }

if [ -n "$EXEC" ]; then
  mkdir -p "$BASE/work"; echo "running $$" > "$STATE"; say "exec: $EXEC"
  ( cd "$BASE/work" && { [ -f "$BASE/env.sh" ] && . "$BASE/env.sh"; } ; exec caffeinate -i bash -c "$EXEC" ) > "$RUN/output.txt" 2>&1 &
  pid=$!
  while kill -0 "$pid" 2>/dev/null; do
    if [ -e "$RUN/ABORT" ]; then kill "$pid" 2>/dev/null; sleep 2; kill -9 "$pid" 2>/dev/null; wait "$pid" 2>/dev/null; pid=""; finish 130; fi
    sleep 1
  done
  wait "$pid"; code=$?; pid=""; finish "$code"
fi

# ── keep the newest KEEP builds (by last use) plus this one; never one touched in the last hour (an upload) ──
touch "$BUILD"
ls -1dt "$BASE"/builds/*/ 2>/dev/null | sed 's#/$##' | tail -n +$((KEEP + 1)) | while read -r d; do
  [ "$d" = "$BUILD" ] && continue
  [ -n "$(find "$d" -maxdepth 0 -mmin -60 2>/dev/null)" ] && continue
  say "pruning old build $d"; rm -rf "$d"
done
# Runs older than 2 days that nobody collected.
find "$BASE/runs" -mindepth 1 -maxdepth 1 -type d -mtime +2 -exec rm -rf {} + 2>/dev/null

APP="$BUILD/CriticalMass.app"
[ -d "$APP" ] || { say "no app at $APP"; finish 66; }
xattr -dr com.apple.quarantine "$APP" 2>/dev/null
EXE="$APP/Contents/MacOS/$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$APP/Contents/Info.plist")"
[ -x "$EXE" ] || { say "no executable at $EXE"; finish 66; }

# ── mute for the run only (restored by cleanup), keep the box awake while the player lives ──
muted_before="$(osascript -e 'output muted of (get volume settings)' 2>/dev/null)"
osascript -e 'set volume output muted true' >/dev/null 2>&1
if defaults export "$DOMAIN" "$RUN/prefs-before.plist" 2>/dev/null; then prefs=1; else prefs=0; fi
LOG="$RUN/Player.log"; SAMPLES="$RUN/samples.tsv"
LIMIT=$((MATCHES * (TIMEOUT + 60) + 120))
launch_ms=$(ms)
others="$(pgrep -f 'Unity.app/Contents/MacOS/Unity|CriticalMass.app/Contents/MacOS' | tr '\n' ' ')"
{ echo "LAUNCH_MS=$launch_ms"; echo "MATCHES=$MATCHES"; echo "TITLE_S=$TITLE_S"; echo "NOVSYNC=${NOVSYNC:+-novsync}"
  echo "OTHERS=$others"; echo "HOST=$(scutil --get LocalHostName 2>/dev/null)"; echo "BUILD=$(basename "$BUILD")"
  echo "QUALITY=$QUALITY"; echo "WIDTH=$W"; echo "HEIGHT=$H"; } > "$RUN/run.env"
say "launch $(basename "$BUILD") matches=$MATCHES quality=${QUALITY:-default} ${W}x${H} limit=${LIMIT}s"
# Started straight from this (ssh-born) shell: proven 2026-11-13 on macOS 26.3 that it gets a Metal device and a window
# in the logged-in console session; no `open` or `launchctl asuser` needed (and exit codes survive).
perl -e 'alarm shift; exec @ARGV' "$LIMIT" \
  "$EXE" -autoplay -quitAfterMatch -autoplayMatches "$MATCHES" -autoplayTitleSeconds "$TITLE_S" -autoplayTimeout "$TIMEOUT" \
  ${SPEED:+-autoplaySpeed "$SPEED"} ${NOVSYNC:+-novsync} ${QUALITY:+-quality "$QUALITY"} ${SHOTS:+-shots} ${AFF:+-affordanceShots} ${FPSCAP:+-fpsCap "$FPSCAP"} ${CAMYAW:+-cameraYaw "$CAMYAW"} \
  -dataDir "$RUN/data" -screen-fullscreen 0 -screen-width "$W" -screen-height "$H" -logFile "$LOG" -perfLog "$RUN/perf.json" \
  > "$RUN/stdout.txt" 2>&1 &
pid=$!
caffeinate -dimsu -w "$pid" & cafe=$!
echo "running $pid" > "$STATE"
# Same outside view as tools/demo-smoke.sh: wall ms, resident KB, cumulative CPU, title seen, match ends so far.
printf 'ms\trss_kb\tcputime\ttitle\tmatch_ends\n' > "$SAMPLES"
while kill -0 "$pid" 2>/dev/null; do
  if [ -e "$RUN/ABORT" ]; then say "aborted by the caller"; kill "$pid" 2>/dev/null; sleep 2; kill -9 "$pid" 2>/dev/null; wait "$pid" 2>/dev/null; pid=""; finish 130; fi
  line="$(ps -o rss=,cputime= -p "$pid" 2>/dev/null)"
  if [ -n "$line" ]; then
    t=0; e=0
    if [ -f "$LOG" ]; then
      grep -q '\[CM.Boot\] title on screen' "$LOG" && t=1
      e="$(grep -c '\[CM.Autoplay\] MATCH END' "$LOG")"
    fi
    printf '%s\t%s\t%s\t%s\t%s\n' "$(ms)" "$(echo $line | awk '{print $1}')" "$(echo $line | awk '{print $2}')" "$t" "${e:-0}" >> "$SAMPLES"
  fi
  sleep 1
done
wait "$pid"; code=$?; pid=""
[ "$code" -eq 142 ] && code=124   # perl alarm (SIGALRM) = the hard limit
echo "CODE=$code" >> "$RUN/run.env"
finish "$code"
