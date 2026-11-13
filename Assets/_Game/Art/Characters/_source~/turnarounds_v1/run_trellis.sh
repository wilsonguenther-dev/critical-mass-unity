#!/bin/bash
# Step 2 for turnarounds-v1: free hosted TRELLIS.2 (microsoft/TRELLIS.2, HF ZeroGPU, about 8 runs a day). One generation per input.
# usage: run_trellis.sh <character> <out dir>      characters: farmer miner builder healer demolitions kaiju_hatchling kaiju_adult
# Runs body-three-quarter plus every prop PNG in trellis_input/<character>/ (body-front.png is kept as a spare input, not run).
# Needs a venv with gradio_client + pillow (VENV, default ~/cm-trellis-venv); HF token read by huggingface_hub from its cache (never printed).
# Exit 3 from generate.py = quota spent or service down: stop, do NOT retry, do NOT use a paid service.
set -e
V=${VENV:-$HOME/cm-trellis-venv}; H=$(cd "$(dirname "$0")" && pwd); CH=${1:?character}; OUT=${2:?out dir}
G="$H/../warrior_v8/generate.py"; IN="$H/trellis_input/$CH"
[ -d "$IN" ] || { echo "no inputs for $CH"; exit 1; }
for f in "$IN"/body-three-quarter.png $(command ls "$IN"/*.png | grep -v '/body-'); do
  k=$(basename "$f" .png)
  "$V/bin/python" -I "$G" "$f" "$OUT/$CH/$k" --res 1024 --faces 200000 --tex 2048
done
