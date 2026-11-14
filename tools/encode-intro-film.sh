#!/bin/sh
# perf/app-size-1: the shipped intro film at the size the game draws it. The film was 2560x1440 at 9.1 Mbit/s (55 MB);
# the engine draws it into a 1920x1080 target, so 1920x1080 H.264 High (CRF 24, slow; CRF 21 came out at 8.7 Mbit/s, the same as the source) at the same 30 fps and the same
# 1420 frames keeps the picture and the clock (IntroFilm.Seconds = 1420 / 30). Sound: AAC 160 kbit/s stereo (was 255).
# Usage: tools/encode-intro-film.sh <source.mp4> <out.mp4>   (source: the film master, or the previous shipped file)
set -eu
src="$1"; out="$2"
ffmpeg -nostdin -hide_banner -loglevel error -y -i "$src" \
  -vf "scale=1920:1080:flags=lanczos" -r 30 -c:v libx264 -preset slow -crf ${CRF:-24} -profile:v high -level 4.1 \
  -pix_fmt yuv420p -g 60 -bf 2 -movflags +faststart \
  -c:a aac -b:a 160k -ac 2 "$out"
ffprobe -v error -count_frames -select_streams v:0 -show_entries stream=nb_read_frames,width,height,r_frame_rate -of compact "$out"
