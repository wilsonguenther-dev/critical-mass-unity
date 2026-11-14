#!/bin/bash
# Build every HY character in sequence (each Blender start goes through ~/bin/cm-heavy). Usage: run_all.sh [names...]
H=$(cd "$(dirname "$0")" && pwd); SC=~/cm-hy-chars-scratch; IN=$SC/in; ART=~/critical-mass-unity-art-hy/Assets/_Game/Art
BL="/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python-exit-code 1"
run() { local n=$1; shift; echo "== $n $(date -u +%H:%M:%S)"; env "$@" ~/bin/cm-heavy blender perl -e 'alarm 1700; exec @ARGV' $BL -P $BUILD -- $SC/$n/raw $SC/$n/work $SC/$n/exp $SC/$n/ren > $SC/$n/build.log 2>&1; echo "rc=$? $(date -u +%H:%M:%S)"; grep "Traceback" -A12 $SC/$n/build.log | tail -6; }
for n in ${@:-Warrior Farmer Miner KaijuAdult}; do
  BUILD=$H/build_hy3d.py
  case $n in
    Warrior) run Warrior HY_BASE=Warrior_HY HY_CLASS=Warrior HY_SHEET_FRONT=$HOME/Desktop/CM-3D-inputs/Warrior/warrior-body-front.png HY_SHEET_BACK=$HOME/Desktop/CM-3D-inputs/Warrior/warrior-body-back.png ;;
    Farmer) run Farmer HY_BASE=Farmer_HY HY_CLASS=Farmer HY_BONE_FIT=1 HY_SRC_FBX=$ART/Farmer/_fallback~/Farmer.fbx HY_PROPS=staff:auto HY_ATTACK=2H_Melee_Attack_Slice HY_SHEET_FRONT=$IN/farmer/front.png HY_SHEET_BACK=$IN/farmer/back.png ;;
    Miner) run Miner HY_BASE=Miner_HY HY_CLASS=Miner HY_BONE_FIT=1 HY_SRC_FBX=$ART/Miner/_fallback~/Miner.fbx HY_PROPS=pick:auto HY_ATTACK=2H_Melee_Attack_Chop HY_SHEET_FRONT=$IN/miner/front.png HY_SHEET_BACK=$IN/miner/back.png ;;
    KaijuAdult) BUILD=$H/build_hy3d_kaiju.py; run KaijuAdult HY_SRC_FBX=$ART/Kaiju/_fallback~/Kaiju_Adult.fbx HY_SHEET_FRONT=$IN/kaiju_adult/front.png HY_SHEET_BACK=$IN/kaiju_adult/back.png ;;
  esac
done
echo ALL_BUILT
