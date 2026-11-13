#!/usr/bin/env bash
# intro-film-2: put the sound team's final mix under the round-2 picture (picture-only master) and export the copies.
#   bash tools/film/mux_final_mix.sh MASTER.mp4 FINAL_MIX.wav [--commit]
# Writes Assets/StreamingAssets/Story/intro-film-1.mp4 (2560x1440 H.264 CRF 18 + AAC 256k, the game's film) and
# ~/Desktop/CriticalMass-intro-round2.mp4 (1280x720 H.264 + AAC, under 25 MB). With --commit, commits and pushes the
# game film on the current branch. With FINAL_MIX.wav = "-", only the Desktop copy is written, SILENT.
set -euo pipefail
M="$1"; MIX="$2"; COMMIT="${3:-}"
REPO="$(cd "$(dirname "$0")/../.." && pwd)"
GAME="$REPO/Assets/StreamingAssets/Story/intro-film-1.mp4"
DESK="$HOME/Desktop/CriticalMass-intro-round2.mp4"
SMALL=(-vf scale=1280:720:flags=lanczos -c:v libx264 -profile:v high -pix_fmt yuv420p -crf 21 -maxrate 3400k
       -bufsize 6800k -preset slow -movflags +faststart)
if [[ "$MIX" == "-" ]]; then
  nice -n 15 ffmpeg -v error -y -i "$M" -an "${SMALL[@]}" "$DESK"
else
  nice -n 15 ffmpeg -v error -y -i "$M" -i "$MIX" -map 0:v -map 1:a -c:v libx264 -profile:v high -level 4.2 \
    -pix_fmt yuv420p -crf 18 -preset slow -colorspace bt709 -color_primaries bt709 -color_trc bt709 -color_range tv \
    -c:a aac -b:a 256k -ar 48000 -shortest -movflags +faststart "$GAME.tmp.mp4"
  mv "$GAME.tmp.mp4" "$GAME"
  nice -n 15 ffmpeg -v error -y -i "$GAME" "${SMALL[@]}" -c:a aac -b:a 160k "$DESK"
  if [[ "$COMMIT" == "--commit" ]]; then
    git -C "$REPO" add "$GAME"
    git -C "$REPO" commit -q -m "Intro film 2: round-2 picture with the final mix (intro-film-2/audio/final-mix.wav)" -- "$GAME"
    git -C "$REPO" push -q origin HEAD
  fi
fi
ls -la "$DESK" ${MIX:+"$GAME"}
ffprobe -v error -show_entries format=duration,size:stream=codec_name,width,height -of compact "$DESK"
