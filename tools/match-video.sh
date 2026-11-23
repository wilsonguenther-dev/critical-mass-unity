#!/usr/bin/env bash
# b13 match video: one shareable gameplay film of a whole Critical Mass match (kick-off to the results screen), recorded
# INSIDE the game by a hidden automation run (Runtime/Game/MatchVideo.cs: capture clock, JPEG frames, the engine's own
# audio mix), then encoded with ffmpeg. The window never shows (AutomationWindow) and nothing reaches the speakers
# (AudioRenderer takes the mix). Actor 0 is the Azure Warrior on autopilot; the camera is the default gameplay camera.
#   tools/match-video.sh --app PATH.app [--seed N | --scout A-B] [--out DIR] [--final-dir DIR] [--fps 30]
#                        [--quality High] [--results-seconds 10] [--timeout S] [--keep-frames] [--scout-only] [--camera default|saved]
# --replay PATH (class moves T1-8, REPLAY.md path B): film a saved .cmreplay of THIS build instead of an autoplay match,
#   following the recorded player with his gameplay camera, kick-off to the final whistle plus --results-seconds. The file
#   is copied into OUT and the run uses its own -dataDir and -noReplay, so the owner's saved replays (newest 20) are never
#   touched or evicted. video.json carries the recorded score, the played-back one and the desync verdict; a desync or a
#   score that differs fails the run (exit 1). No --seed / --scout with --replay.
# --encode-only: re-encode OUT/capture (kept with --keep-frames) without recording again.
# --camera default (the default): the game's own default camera (top-down, default heading) for the film, whatever this
#   Mac's saved settings say (the cm.camera.* keys are removed for the run, then every saved setting is restored); saved: as saved.
# --scout A-B: sim-only matches on seeds A..B first (-batchmode -nographics -simScout, seconds each, no window) and
#   records the best one: the Kaiju hatched, both teams hold the hill, the closest final score (ties: more knock-outs).
# Audio: the engine's float mix can pass full scale (peaks near +6 dB after the hatch): -3 dB and a -2 dBFS limiter.
# Output: FINAL_DIR/CriticalMass-match-<version>-1080p.mp4 (H.264 CRF 19 capped at 14 Mb/s + AAC 192k) and -720p-text.mp4 (two-pass,
#   sized under 95 MB); OUT keeps Player.log, video.json, scout.txt (frames are deleted unless --keep-frames).
# The recording runs inside ~/bin/cm-heavy's Unity slot. Exit: 0 ok, 1 failed, 66 no app, 64 usage, 124 timeout.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP=""; SEED=""; SCOUT=""; OUT=""; FINAL="$HOME/Movies/CriticalMass"; FPS=30; QUALITY=High; RESULTS=10; TIMEOUT=1500
KEEP=0; SCOUT_ONLY=0; VFLIP=0; CAMERA=default; ENCODE_ONLY=0; REPLAY=
usage() { sed -n '6,7p' "${BASH_SOURCE[0]}" | sed 's/^# *//' >&2; exit 64; }
while [[ $# -gt 0 ]]; do
  case "$1" in
    --app) APP="${2:?}"; shift 2 ;; --seed) SEED="${2:?}"; shift 2 ;; --scout) SCOUT="${2:?}"; shift 2 ;;
    --out) OUT="${2:?}"; shift 2 ;; --final-dir) FINAL="${2:?}"; shift 2 ;; --fps) FPS="${2:?}"; shift 2 ;;
    --quality) QUALITY="${2:?}"; shift 2 ;; --results-seconds) RESULTS="${2:?}"; shift 2 ;;
    --timeout) TIMEOUT="${2:?}"; shift 2 ;; --keep-frames) KEEP=1; shift ;; --scout-only) SCOUT_ONLY=1; shift ;;
    --vflip) VFLIP=1; shift ;; --camera) CAMERA="${2:?}"; shift 2 ;; --encode-only) ENCODE_ONLY=1; shift ;;
    --replay) REPLAY="${2:?}"; shift 2 ;;
    -h|--help) usage ;; *) echo "match-video: unknown flag $1" >&2; usage ;;
  esac
done
APP="${APP%/}"; [[ -d "$APP/Contents/MacOS" ]] || { echo "match-video: no app at '$APP'" >&2; exit 66; }
command -v ffmpeg >/dev/null || { echo "match-video: ffmpeg missing (brew install ffmpeg)" >&2; exit 1; }
DATA="$APP/Contents/Resources/Data"
VERSION="$(LC_ALL=C grep -a -o -m1 -E 'version=[0-9]+\.[0-9]+\.[0-9]+' "$DATA/resources.assets" | head -1 | cut -d= -f2 || true)"
COMMIT="$(LC_ALL=C grep -a -o -m1 -E 'commit=[0-9a-f]{7,40}' "$DATA/resources.assets" | head -1 | cut -d= -f2 || true)"
VERSION="${VERSION:-unknown}"
OUT="${OUT:-$ROOT/Logs/match-video/$(date -u +%Y%m%dT%H%M%SZ)-$VERSION}"
mkdir -p "$OUT" "$FINAL"; OUT="$(cd "$OUT" && pwd)"
EXE="$APP/Contents/MacOS/$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$APP/Contents/Info.plist")"
echo "match-video: build $VERSION ($COMMIT), out $OUT"
if [[ -n "$REPLAY" ]]; then
  [[ -f "$REPLAY" ]] || { echo "match-video: no replay file '$REPLAY'" >&2; exit 66; }
  [[ -z "$SEED" && -z "$SCOUT" ]] || { echo "match-video: --replay takes no --seed / --scout" >&2; exit 64; }
  REPLAY="$(cd "$(dirname "$REPLAY")" && pwd)/$(basename "$REPLAY")"
  SEED="replay"
fi

# 1. Seed scout (sim only, batch mode: no window, no graphics slot).
if [[ -n "$SCOUT" ]]; then
  perl -e 'alarm shift; exec @ARGV' 900 "$EXE" -batchmode -nographics -simScout "$SCOUT" -dataDir "$OUT/scout-data" \
    -logFile "$OUT/scout.log" || { echo "match-video: scout run failed (exit $?)" >&2; exit 1; }
  grep -o 'SCOUT seed=.*' "$OUT/scout.log" | sed 's/^SCOUT //' > "$OUT/scout.txt" || true
  [[ -s "$OUT/scout.txt" ]] || { echo "match-video: the scout wrote no lines (see $OUT/scout.log)" >&2; exit 1; }
  cat "$OUT/scout.txt"
  SEED="$(python3 -I - "$OUT/scout.txt" <<'PY'
import sys
best=None
for line in open(sys.argv[1]):
    kv=dict(p.split('=',1) for p in line.split() if '=' in p)
    a,c=float(kv['azure']),float(kv['crimson'])
    hatched=kv['hatched']=='1'; both=a>=20 and c>=20
    margin=abs(a-c)/max(1.0,a+c); ko=int(kv['ko_azure'])+int(kv['ko_crimson'])
    key=(0 if hatched else 1, 0 if both else 1, round(margin,3), -int(kv['lead_changes']), -ko)
    if best is None or key<best[0]: best=(key,kv['seed'])
print(best[1])
PY
)"
  echo "match-video: picked seed $SEED"
fi
(( SCOUT_ONLY )) && exit 0
[[ -n "$SEED" ]] || { echo "match-video: give --seed N or --scout A-B" >&2; exit 64; }

# 2. The recording: hidden automation run inside the machine's Unity slot.
CAP="$OUT/capture"
if (( ! ENCODE_ONLY )); then
if [[ -z "${CM_HEAVY_HELD:-}" && -x "$HOME/bin/cm-heavy" ]]; then
  set +e
  SEL=(--seed "$SEED"); [[ -n "$REPLAY" ]] && SEL=(--replay "$REPLAY")
  CM_HEAVY_TIMEOUT=$((TIMEOUT + 120)) CM_HEAVY_HELD=1 "$HOME/bin/cm-heavy" unity env CM_HEAVY_HELD=1 bash "${BASH_SOURCE[0]}" \
    --app "$APP" "${SEL[@]}" --out "$OUT" --final-dir "$FINAL" --fps "$FPS" --quality "$QUALITY" \
    --results-seconds "$RESULTS" --timeout "$TIMEOUT" --camera "$CAMERA" $( ((KEEP)) && echo --keep-frames ) $( ((VFLIP)) && echo --vflip )
  rc=$?
  set -e
  # cm-heavy can exit 1 from its own cleanup after a good job: judge by the outputs.
  [[ -f "$OUT/encode.ok" ]] && exit 0
  exit "$rc"
fi

rm -rf "$CAP"; mkdir -p "$OUT/data"
LOG="$OUT/Player.log"; rm -f "$LOG"
DOMAIN="${CM_PREFS_DOMAIN:-com.drivia.criticalmass}"; HAD_PREFS=0
if defaults export "$DOMAIN" "$OUT/prefs-before.plist" 2>/dev/null; then HAD_PREFS=1; fi
restore_prefs() { if (( HAD_PREFS )); then defaults import "$DOMAIN" "$OUT/prefs-before.plist" 2>/dev/null || true
                  else defaults delete "$DOMAIN" 2>/dev/null || true; fi; }
trap restore_prefs EXIT
if [[ "$CAMERA" == default ]]; then for k in cm.camera.ground cm.camera.view cm.camera.heading cm.camera.manual; do defaults delete "$DOMAIN" "$k" 2>/dev/null || true; done; fi
if [[ -n "$REPLAY" ]]; then
  cp "$REPLAY" "$OUT/replay.cmreplay"
  RUN=(-replayFile "$OUT/replay.cmreplay" -noReplay)
else
  RUN=(-autoplay -autoplayTitleSeconds 3 -autoplayTimeout "$TIMEOUT" -seed "$SEED")
fi
open -g -j -n "$APP" --args "${RUN[@]}" \
  -matchVideo "$CAP" -matchVideoFps "$FPS" -matchVideoResultsSeconds "$RESULTS" -soundVolume 1 -quality "$QUALITY" \
  -screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -dataDir "$OUT/data" -logFile "$LOG"
pid=""; for _ in $(seq 1 40); do pid="$(pgrep -n -f -- "$LOG" || true)"; [[ -n "$pid" ]] && break; sleep 1; done
[[ -n "$pid" ]] || { echo "match-video: the hidden player did not start" >&2; exit 1; }
echo "match-video: recording seed $SEED (player pid $pid, limit ${TIMEOUT}s)"
t0=$SECONDS
while kill -0 "$pid" 2>/dev/null; do
  sleep 5
  if (( SECONDS - t0 > TIMEOUT )); then
    echo "match-video: TIMEOUT, stopping pid $pid" >&2; kill "$pid" 2>/dev/null || true; sleep 10; kill -9 "$pid" 2>/dev/null || true; exit 124
  fi
done
grep -q '\[CM.MatchVideo\] DONE' "$LOG" || { echo "match-video: the recording did not finish (see $LOG)" >&2; exit 1; }
cp "$CAP/video.json" "$OUT/video.json"
if [[ -n "$REPLAY" ]]; then
  grep -h '\[CM.Replay\]' "$LOG" | tail -5 || true
  python3 -I -c 'import json,sys; j=json.load(open(sys.argv[1])); bad=j.get("replay_desync") or not j.get("score_matches_replay") or j.get("replay_problem"); print("match-video: replay", "desync" if j.get("replay_desync") else "in step", "| score", j.get("hill_azure"), "/", j.get("hill_crimson"), "recorded", j.get("recorded_hill_azure"), "/", j.get("recorded_hill_crimson"), "| problem:", j.get("replay_problem") or "none"); sys.exit(1 if bad else 0)' "$OUT/video.json" \
    || { echo "match-video: the replay film does not match its recording (see $OUT/video.json)" >&2; exit 1; }
fi
fi
read -r RATE CH < <(python3 -I -c 'import json,sys; j=json.load(open(sys.argv[1])); print(j["audio_rate"], j["audio_channels"])' "$OUT/video.json")

# 3. Encode: 1080p master, then a two-pass 720p copy that fits a text message.
NAME="CriticalMass-match-$VERSION"; [[ -n "$REPLAY" ]] && NAME="CriticalMass-replay-$VERSION-$(basename "${REPLAY%.*}")"; M1080="$FINAL/$NAME-1080p.mp4"; M720="$FINAL/$NAME-720p-text.mp4"
VF="format=yuv420p"; (( VFLIP )) && VF="vflip,$VF"
ffmpeg -hide_banner -loglevel error -y -framerate "$FPS" -i "$CAP/frames/f%06d.jpg" -f f32le -ar "$RATE" -ac "$CH" -i "$CAP/audio.f32" \
  -map 0:v -map 1:a -vf "$VF" -c:v libx264 -preset slow -crf 19 -maxrate 14M -bufsize 28M -profile:v high -r "$FPS" \
  -af "volume=-3dB,alimiter=limit=0.794:attack=5:release=80:level=disabled" -c:a aac -b:a 192k -ac 2 -shortest -movflags +faststart "$M1080"
DUR="$(ffprobe -v error -show_entries format=duration -of csv=p=0 "$M1080")"
VBR="$(python3 -I -c 'import sys; d=float(sys.argv[1]); print(int((86*8*1000*1000/d - 128000)/1000))' "$DUR")"
PASS="$OUT/x264-pass"
ffmpeg -hide_banner -loglevel error -y -i "$M1080" -vf scale=1280:720:flags=lanczos -c:v libx264 -preset slow -b:v "${VBR}k" \
  -pass 1 -passlogfile "$PASS" -an -f mp4 /dev/null
ffmpeg -hide_banner -loglevel error -y -i "$M1080" -vf scale=1280:720:flags=lanczos -c:v libx264 -preset slow -b:v "${VBR}k" \
  -pass 2 -passlogfile "$PASS" -c:a aac -b:a 128k -movflags +faststart "$M720"
rm -f "$PASS"*
(( KEEP )) || rm -rf "$CAP/frames" "$CAP/audio.f32"
touch "$OUT/encode.ok"
ls -l "$M1080" "$M720"
echo "match-video: done, seed $SEED, $DUR s"
