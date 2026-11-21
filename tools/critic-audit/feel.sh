#!/bin/bash
# Critic audit, controls feel: assist map, swing commitment per class, wall push / wall hit, one TNT blast. Fresh match, seed 1337.
U=~/cm-editor-eyes/bin/u; O=~/cm-incidents/critic-audit-2026-11-21/data; C=~/cm-wt-audit-critic/tools/critic-audit/Critic.cs; ES=~/cm-editor-eyes/scripts
r(){ $U ${2:-120} script run -- --file $C --entry Critic.$1 --args "$3" 2>&1 | grep "^result\|rror" | cut -c1-900; }
$U 60 script run -- --file $ES/Match.cs --entry Match.Start --args "[0, 1337]" 2>&1 | grep ^result
sleep 8
$U 30 script run -- --file $ES/Match.cs --entry Match.Autopilot --args "[false]" 2>&1 | grep ^result
echo "ASSIST $(r AssistMap 120 "[\"$O/assist-map.csv\"]")"
# open floor spot near the Azure base for swings (pad 8,36 -> stand at 14,30)
for cls in 0 1 2 3 4 5; do
  echo "SWING-STAND $(r Swing 60 "[$cls, 0, 0, 14.5, 30.5]")"
  echo "SWING-MOVE  $(r Swing 60 "[$cls, 1, 0, 14.5, 30.5]")"
  echo "SWING-TURN  $(r Swing 60 "[$cls, 1, 1, 14.5, 30.5]")"
done
W=$(r FindCell 30 "[2, 0]" | sed 's/^result: //'); echo "WALL cell $W"
B=$(r FindCell 30 "[3, 0]" | sed 's/^result: //'); echo "BREAKABLE cell $B"
set -- $W; wx=$1; wz=$2; set -- $B; bx=$1; bz=$2
echo "PUSH-WALL $(r AtWall 60 "[0, $wx, $wz, 0, 45, 0.6, \"$O/wall-push.csv\"]")"
echo "WARRIOR-HITS-WALL $(r AtWall 60 "[0, $wx, $wz, 1, 30, 0.2, \"$O/wall-hit-warrior.csv\"]")"
echo "WARRIOR-HITS-BREAKABLE $(r AtWall 60 "[0, $bx, $bz, 1, 30, 0.2, \"$O/brk-hit-warrior.csv\"]")"
echo "MINER-ATTACKS-BREAKABLE $(r AtWall 60 "[2, $bx, $bz, 1, 30, 0.2, \"$O/brk-hit-miner-attack.csv\"]")"
echo "MINER-PICK-BREAKABLE $(r AtWall 60 "[2, $bx, $bz, 2, 30, 0.2, \"$O/brk-hit-miner-pick.csv\"]")"
echo "DONE $(date -u +%T)"
