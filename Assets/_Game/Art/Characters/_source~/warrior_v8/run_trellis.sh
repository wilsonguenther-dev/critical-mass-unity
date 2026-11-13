#!/bin/bash
# Step 2 of warrior-v8: free hosted TRELLIS.2 (microsoft/TRELLIS.2, HF ZeroGPU). One generation per input.
# QUOTA (checked 2026-11-13): free accounts get 5 GPU-min/day AND about 3 GPU runs/day (HF staff, forum, Apr 2026).
# Each generation makes TWO GPU runs (image_to_3d + extract_glb, both @spaces.GPU(duration=120) in the Space's app.py),
# so only ONE full generation fits per day. Run one input per day: pass it as the second argument (body-three-quarter, shield or cleaver).
# Quota resets 24 h after the account's first GPU use (HF docs); the refusal message carries no reset time.
# Needs a venv with gradio_client + pillow; the HF token is read from ~/.cache/huggingface/token by huggingface_hub (never printed).
# Exit 3 from generate.py = quota spent: stop, do NOT retry, do NOT use a paid service.
set -e
V=${VENV:-$HOME/cm-warrior-v8-scratch/venv}; H=$(cd "$(dirname "$0")" && pwd); OUT=${1:?out dir}
for k in ${2:-body-three-quarter}; do
  "$V/bin/python" -I "$H/generate.py" "$H/trellis_input/$k.png" "$OUT/$k" --res 1024 --faces 200000 --tex 2048
done
