#!/usr/bin/env bash
# Play-tests ("smoke") and captures of a BUILT Mac player on the second Mac (the MacBook Air play-test box), so they
# leave the main Mac and its single heavy-job lock (~/bin/cm-heavy). See tools/remote/README.md.
#   tools/remote-playtest.sh [--host HOST] [--app PATH.app] [--mode smoke|captures] [--quality High|Medium|Low]
#                            [--matches N] [--out DIR] [--speed X] [--novsync] [--profile default|floor]
#                            [--width W --height H] [--title-seconds S] [--timeout S] [--lock-wait S] [--keep N]
#                            [--extra "PLAYER FLAGS"]  (e.g. "-showWindow -autoplayPacing rule,timed60")
# What it does: copies the .app (rsync, only changed files) to ~/cm-remote/builds/<build commit>/ on the box, starts
# ~/cm-remote/bin/air-run.sh there detached (queue lock, mute for the run, caffeinate, settings isolation, the same
# automation flags as tools/demo-smoke.sh: -autoplay -quitAfterMatch -autoplayMatches -autoplayTitleSeconds
# -autoplayTimeout -quality -dataDir -logFile -perfLog), waits, copies Player.log, perf*.json, samples.tsv, run.env and
# data/ (shots) back into OUT, deletes the run on the box, and judges it here with tools/demo-smoke.sh --summarise-only
# (same budgets, tools/budgets.json; summary.json in OUT). Old builds on the box beyond the newest two are deleted.
# --mode captures adds -shots: the player writes shot-title.png (title, 2 s in), shot-start.png (3 s of match time) and shot-mid.png (60 s),
# copied to OUT/shots/. (Screen grabs from outside the game are not possible over ssh: macOS refuses screen recording.)
# Defaults: host wilsons-macbook-air.local (or CM_REMOTE_HOST), app Builds/current or Builds/macOS of this checkout,
# smoke, Medium, 1 match, 1920x1080 window, 12 s on the title, OUT Logs/remote/<utc>-<mode>-<quality>.
# Exit: 0 pass, 1 fail (exceptions, budget over or not measured), 4 match timeout, 124 overall timeout, 75 queue wait
# ran out, 66 no app, 67 box refused (battery or disk), 69 box unreachable, 64 usage.
set -euo pipefail
EXTRA=""    # --extra "FLAGS": more player flags, passed as they are (perf-60hz-32p)
CAMYAW=""   # --camera-yaw D: the player's -cameraYaw (builds from a81efe4+1 on; 0 = true north, 45 = default)
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT/tools/remote/_remote-lib.sh"
APP="$ROOT/Builds/current/CriticalMass.app"; [[ -d "$APP" ]] || APP="$ROOT/Builds/macOS/CriticalMass.app"
MODE=smoke; QUALITY=Medium; MATCHES=1; OUT=""; SPEED=""; NOVSYNC=""; PROFILE=default; W=1920; H=1080; TITLE_S=12
TIMEOUT=900; LOCK_WAIT=3600; KEEP=2
usage() { sed -n '4,7p' "${BASH_SOURCE[0]}" | sed 's/^# *//' >&2; exit 64; }
while [[ $# -gt 0 ]]; do
  case "$1" in
    --host) REMOTE_HOST="${2:?}"; shift 2 ;;
    --app) APP="${2:?}"; shift 2 ;;
    --mode) MODE="${2:?}"; shift 2 ;;
    --quality) QUALITY="${2:?}"; shift 2 ;;
    --matches) MATCHES="${2:?}"; shift 2 ;;
    --out) OUT="${2:?}"; shift 2 ;;
    --speed) SPEED="${2:?}"; shift 2 ;;
    --novsync) NOVSYNC=1; shift ;;
    --camera-yaw) CAMYAW="$2"; shift 2 ;;
    --extra) EXTRA="$2"; shift 2 ;;
    --profile) PROFILE="${2:?}"; shift 2 ;;
    --width) W="${2:?}"; shift 2 ;;
    --height) H="${2:?}"; shift 2 ;;
    --title-seconds) TITLE_S="${2:?}"; shift 2 ;;
    --timeout) TIMEOUT="${2:?}"; shift 2 ;;
    --lock-wait) LOCK_WAIT="${2:?}"; shift 2 ;;
    --keep) KEEP="${2:?}"; shift 2 ;;
    -h|--help) usage ;;
    *) echo "remote-playtest: unknown flag $1" >&2; usage ;;
  esac
done
[[ "$MODE" == smoke || "$MODE" == captures ]] || usage
[[ "$PROFILE" == default || "$PROFILE" == floor ]] || usage
APP="${APP%/}"
[[ -d "$APP/Contents/MacOS" ]] || { echo "remote-playtest: no app at $APP" >&2; exit 66; }

# The build's own commit (BuildInfo resource baked into resources.assets: "commit=abc1234"); a content hash otherwise.
DATA="$APP/Contents/Resources/Data"
COMMIT="$(LC_ALL=C grep -a -o -m1 -E 'commit=[0-9a-f]{7,40}' "$DATA/resources.assets" 2>/dev/null | head -1 | cut -d= -f2 || true)"
[[ -n "$COMMIT" ]] || COMMIT="h$(cat "$APP/Contents/Info.plist" "$DATA"/Managed/CriticalMass*.dll 2>/dev/null | shasum | cut -c1-10)"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
ID="$STAMP-$MODE-$QUALITY-$$"
OUT="${OUT:-$ROOT/Logs/remote/$STAMP-$MODE-$QUALITY}"
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"

remote_preflight
echo "remote-playtest: build $COMMIT -> $REMOTE_HOST:~/cm-remote/builds/$COMMIT/ ($(du -sh "$APP" | cut -f1))"
rssh "mkdir -p ~/cm-remote/builds/$COMMIT && touch ~/cm-remote/builds/$COMMIT"
rsync_to --delete "$APP" "$REMOTE_USER@$REMOTE_HOST:cm-remote/builds/$COMMIT/" \
  || { echo "remote-playtest: copying the app failed" >&2; exit 69; }

FPS=(); [[ "$PROFILE" == floor ]] && FPS=(--fps-cap 30)
ARGS=(--build "builds/$COMMIT" --matches "$MATCHES" --title-seconds "$TITLE_S" --timeout "$TIMEOUT"
      --quality "$QUALITY" --width "$W" --height "$H" --keep "$KEEP" --lock-wait "$LOCK_WAIT"
      ${SPEED:+--speed "$SPEED"} ${NOVSYNC:+--novsync} ${CAMYAW:+--camera-yaw "$CAMYAW"} ${EXTRA:+--extra "$EXTRA"} ${FPS[@]+"${FPS[@]}"})
[[ "$MODE" == captures ]] && ARGS+=(--shots)
MAX=$((LOCK_WAIT + MATCHES * (TIMEOUT + 60) + 300))
echo "remote-playtest: $MODE, $MATCHES match(es) at $QUALITY on $REMOTE_HOST, job $ID (limit ${MAX}s incl. queue)"
remote_start "$ID" "$MAX" "${ARGS[@]}"
code="$REMOTE_CODE"
remote_collect "$ID" "$OUT" || echo "remote-playtest: could not collect the run folder (left on the box: runs/$ID)" >&2
if ls "$OUT"/data/shot-*.png >/dev/null 2>&1; then mkdir -p "$OUT/shots" && cp "$OUT"/data/shot-*.png "$OUT/shots/"; fi
case "$code" in 75|66|67|69|124|130) echo "remote-playtest: FAILED, job exit $code (see $OUT/runner.log)" >&2; exit "$code" ;; esac

launch_ms="$(sed -n 's/^LAUNCH_MS=//p' "$OUT/run.env" 2>/dev/null | tail -1)"
set +e
CM_PROFILE="$PROFILE" CM_SMOKE_EXIT="$code" CM_SMOKE_LAUNCH_MS="${launch_ms:-0}" bash "$ROOT/tools/demo-smoke.sh" \
  --summarise-only "$OUT" --matches "$MATCHES" --title-seconds "$TITLE_S" --quality "$QUALITY" ${NOVSYNC:+--novsync}
rc=$?
set -e
n_shots="$( (ls "$OUT"/shots/*.png 2>/dev/null || true) | wc -l | tr -d ' ')"
echo "remote-playtest: $( ((rc == 0)) && echo PASS || echo FAIL ) player exit $code, $n_shots shot(s), out $OUT"
(( code == 4 )) && exit 4
exit "$rc"
