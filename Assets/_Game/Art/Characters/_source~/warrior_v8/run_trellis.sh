#!/bin/bash
# Step 2 of warrior-v8: free hosted TRELLIS.2 (microsoft/TRELLIS.2, HF ZeroGPU, ~8 runs/day). One generation per input.
# Needs a venv with gradio_client + pillow; the HF token is read from ~/.cache/huggingface/token by huggingface_hub (never printed).
# Exit 3 from generate.py = quota spent: stop, do NOT retry, do NOT use a paid service.
set -e
V=${VENV:-$HOME/cm-warrior-v8-scratch/venv}; H=$(cd "$(dirname "$0")" && pwd); OUT=${1:?out dir}
for k in body-three-quarter shield cleaver; do
  "$V/bin/python" -I "$H/generate.py" "$H/trellis_input/$k.png" "$OUT/$k" --res 1024 --faces 200000 --tex 2048
done
