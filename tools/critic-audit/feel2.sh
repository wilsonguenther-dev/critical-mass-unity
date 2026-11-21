#!/bin/bash
# Follow-ups: isolate the Warrior turn-during-wind-up result, retest the standing whiff at a second spot, one TNT blast with pictures.
U=~/cm-editor-eyes/bin/u; O=~/cm-incidents/critic-audit-2026-11-21/data; S=~/cm-incidents/critic-audit-2026-11-21/shots; C=~/cm-wt-audit-critic/tools/critic-audit/Critic.cs; ES=~/cm-editor-eyes/scripts
r(){ $U ${2:-120} script run -- --file $C --entry Critic.$1 --args "$3" 2>&1 | grep "^result\|rror" | cut -c1-900; }
for spot in "14.5, 30.5" "20.5, 44.5"; do
  echo "W-STAND-TURN $spot $(r Swing 60 "[0, 0, 1, $spot]")"
  echo "W-STAND $spot $(r Swing 60 "[0, 0, 0, $spot]")"
  echo "H-STAND $spot $(r Swing 60 "[5, 0, 0, $spot]")"
done
cam(){ $U 30 script run -- --file $ES/Eyes.cs --entry Eyes.EyesCam --args "[18.5, 0.5, -30.5, 55, 225, 9]" >/dev/null 2>&1; $U 60 capture game_view -- --source camera --camera "Eyes Cam" --overlay false --width 1600 --height 900 --output-path $S/$1.png 2>&1 | grep -q savedPath || echo "FAIL $1"; $U 30 script run -- --file $ES/Eyes.cs --entry Eyes.EyesCamOff >/dev/null 2>&1; }
$U 30 script eval --caller plugin --skill unity-cli 'var d = UnityEngine.Object.FindAnyObjectByType<CriticalMass.Runtime.Game.SimDriverBehaviour>(); d.TimeScale = 0; return "f";' >/dev/null 2>&1
cam d0-before
echo "BLAST $(r Blast 120 "[14.5, 30.5, 18.5, 30.5]")"
cam d1-blast-plus0.4s
echo "RUN $(r Run 30 "[1.0]")"; cam d2-plus1.4s
echo "RUN $(r Run 30 "[5.0]")"; cam d3-plus6.4s
$U 30 script eval --caller plugin --skill unity-cli 'var d = UnityEngine.Object.FindAnyObjectByType<CriticalMass.Runtime.Game.SimDriverBehaviour>(); d.TimeScale = 1; return "p1";' >/dev/null 2>&1
echo "DONE2 $(date -u +%T)"
