#!/bin/bash
# usage: build_all.sh <LOD> [types...]
B=/Applications/Blender.app/Contents/MacOS/Blender; S=~/cm-art-trial/parts-kaiju/scripts; L=~/cm-art-trial/parts-kaiju/work/logs
mkdir -p $L; LOD=$1; shift; TS=${@:-Elder Grove Brute Timber Mire}
for t in $TS; do
  ( CM_TYPE=$t CM_LOD=$LOD timeout 300 $B -b --factory-startup --python $S/build_kaiju.py </dev/null > $L/build_${t}_$LOD.log 2>&1; echo "$t $LOD exit $?" ) &
done; wait
for t in $TS; do grep -hE "Error|Traceback|line [0-9]+|BUILD_REPORT" $L/build_${t}_$LOD.log | cut -c1-400 | sed "s/^/$t: /"; done
