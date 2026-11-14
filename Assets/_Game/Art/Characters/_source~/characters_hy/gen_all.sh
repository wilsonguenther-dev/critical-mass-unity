#!/bin/bash
# One call per asset, no retries. Stops at the first non-zero exit (2 API error, 3 cap, 4 billing words).
set -u
I=~/cm-hy-chars-scratch/in; O=~/Desktop/CM-3D-outputs
set -a; . ~/.config/critical-mass/tencent.env; set +a
export SSL_CERT_FILE=/etc/ssl/cert.pem HY3D_CREDIT_CAP=200
H=$(dirname "$0")/hy3d_tencent.py
run() { echo "== $*"; python3 -I "$H" "$@"; rc=$?; echo "rc=$rc"; [ $rc -eq 0 ] || { echo "STOPPING ALL"; exit $rc; }; }
run pro $O/Farmer $I/farmer/front.png --back $I/farmer/back.png --left $I/farmer/side.png --left-front $I/farmer/three-quarter.png --model 3.1 --tag body-pro31-mv
run rapid $O/Farmer $I/farmer/prop-staff.png --tag staff-express
run pro $O/Miner $I/miner/front.png --back $I/miner/back.png --left $I/miner/side.png --left-front $I/miner/three-quarter.png --model 3.1 --tag body-pro31-mv
run rapid $O/Miner $I/miner/prop-pick-hammer.png --tag pick-hammer-express
run pro $O/KaijuAdult $I/kaiju_adult/front.png --back $I/kaiju_adult/back.png --left $I/kaiju_adult/side.png --model 3.1 --tag body-pro31-mv
echo ALL_DONE
