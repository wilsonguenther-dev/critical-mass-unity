#!/usr/bin/env bash
# Story intro 3: build the shipped film end to end (never plays anything).
#   tools/film/build_film.sh WORK_DIR
# 1. soundtrack (build_soundtrack.py) and its acceptance measurement (check_soundtrack.py -> tools/film/acceptance/)
# 2. picture master (render_intro.py, near-lossless intermediate), then a two-pass H.264 High encode at a bitrate that
#    fits the 60 MB budget, muxed with the AAC soundtrack, BT.709, +faststart
# 3. the shot table for the engine and tests (Assets/_Game/Resources/Story/intro-film.json)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
W="${1:?work dir}"
mkdir -p "$W/audio"
nice -n 10 python3 "$ROOT/tools/film/build_soundtrack.py" "$W/audio"
python3 "$ROOT/tools/film/check_soundtrack.py" "$W/audio" --json "$ROOT/tools/film/acceptance/soundtrack.json" || true
python3 "$ROOT/tools/film/render_intro.py" --table "$ROOT/Assets/_Game/Resources/Story/intro-film.json"
FILM_CRF=8 FILM_PRESET=ultrafast nice -n 10 python3 "$ROOT/tools/film/render_intro.py" "$W/master.mkv"
BR="${FILM_BITRATE:-9000k}"
X=(-c:v libx264 -profile:v high -level 5.1 -preset slow -b:v "$BR" -maxrate 16000k -bufsize 24000k -pix_fmt yuv420p
   -x264-params colorprim=bt709:transfer=bt709:colormatrix=bt709 -r 30 -colorspace bt709 -color_primaries bt709 -color_trc bt709 -color_range tv)
ffmpeg -v error -y -i "$W/master.mkv" "${X[@]}" -pass 1 -passlogfile "$W/x264" -an -f null -
ffmpeg -v error -y -i "$W/master.mkv" -i "$W/audio/intro-mix.wav" "${X[@]}" -pass 2 -passlogfile "$W/x264" \
  -c:a aac -b:a 256k -ar 48000 -shortest -movflags +faststart "$W/intro-film-1.mp4"
mkdir -p "$ROOT/Assets/StreamingAssets/Story"
cp "$W/intro-film-1.mp4" "$ROOT/Assets/StreamingAssets/Story/intro-film-1.mp4"
ls -la "$ROOT/Assets/StreamingAssets/Story/intro-film-1.mp4"
