#!/usr/bin/env bash
# intro-film-2: the video path's build check, run ON the second Mac by tools/remote-run.sh (inside its queue lock).
#   bash air_intro_capture.sh APP [FILM.mp4]
# Plays the intro in the real player three times with the -introCapture harness (IntroFilmCapture.cs): the whole film
# plus the hand-off into the title, a skip at 5 s and a skip at 30 s. Each run writes 2 fps screenshots, a CSV of the
# video clock and frame per shot, and the game's own audio output (listener tap) to intro-cap/<run>/.
# FILM (optional): a newer intro-film-1.mp4 copied into the app's StreamingAssets first (tests a film without a new
# CI build). The machine's output is muted for the runs and restored after; the window is visible on the Air's screen.
set -uo pipefail
APP="${1:?app}"; FILM="${2:-}"
BIN="$APP/Contents/MacOS/$(ls "$APP/Contents/MacOS" | head -1)"
SA="$APP/Contents/Resources/Data/StreamingAssets/Story"
xattr -dr com.apple.quarantine "$APP" 2>/dev/null || true
if [[ -n "$FILM" && -f "$FILM" ]]; then cp "$FILM" "$SA/intro-film-1.mp4"; fi
shasum -a 256 "$SA/intro-film-1.mp4" | tee film.sha256
was_muted="$(osascript -e 'output muted of (get volume settings)' 2>/dev/null || echo false)"
osascript -e 'set volume output muted true' 2>/dev/null || true
rm -rf intro-cap && mkdir -p intro-cap
for spec in "full:-1" "skip5:5" "skip30:30"; do
  name="${spec%%:*}"; at="${spec##*:}"
  out="$PWD/intro-cap/$name"; mkdir -p "$out"
  args=(-introCapture "$out" -introCaptureFps 2 -introCaptureTail 4 -screen-width 1920 -screen-height 1080
        -screen-fullscreen 0 -logFile "$out/Player.log")
  [[ "$at" != "-1" ]] && args+=(-introSkipAt "$at")
  t0=$(date +%s)
  "$BIN" "${args[@]}" > /dev/null 2>&1 &
  pid=$!
  caffeinate -w "$pid" &
  for _ in $(seq 1 120); do kill -0 "$pid" 2>/dev/null || break; sleep 1; done
  kill -0 "$pid" 2>/dev/null && { echo "$name: still running after 120 s, stopping"; kill "$pid"; sleep 2; kill -9 "$pid" 2>/dev/null; }
  wait "$pid" 2>/dev/null; echo "$name: exit $? after $(( $(date +%s) - t0 )) s, $(ls "$out"/shot-*.jpg 2>/dev/null | wc -l | tr -d ' ') shots"
done
[[ "$was_muted" == "true" ]] || osascript -e 'set volume output muted false' 2>/dev/null || true
tar czf intro-cap.tgz intro-cap film.sha256 && ls -la intro-cap.tgz
