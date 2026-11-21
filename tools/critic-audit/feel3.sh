#!/bin/bash
# Pictures for the owner's named complaints: blast on open floor, Warrior dodge ("boost"), Warrior hitting a wall, Miner cracking a wall.
U=~/cm-editor-eyes/bin/u; O=~/cm-incidents/critic-audit-2026-11-21/data; S=~/cm-incidents/critic-audit-2026-11-21/shots; C=~/cm-wt-audit-critic/tools/critic-audit/Critic.cs; ES=~/cm-editor-eyes/scripts
r(){ $U ${2:-120} script run -- --file $C --entry Critic.$1 --args "$3" 2>&1 | grep "^result\|rror" | cut -c1-900; }
frz(){ $U 30 script eval --caller plugin --skill unity-cli "var d = UnityEngine.Object.FindAnyObjectByType<CriticalMass.Runtime.Game.SimDriverBehaviour>(); d.TimeScale = $1; UnityEditor.EditorUtility.audioMasterMute = true; UnityEngine.AudioListener.volume = 0f; return \"p\";" >/dev/null 2>&1; }
close(){ $U 30 script run -- --file $ES/Eyes.cs --entry Eyes.EyesCam --args "[$2, 0.6, -$3, ${5:-55}, 225, ${4:-7}]" >/dev/null 2>&1; $U 60 capture game_view -- --source camera --camera "Eyes Cam" --overlay false --width 1600 --height 900 --output-path $S/$1.png 2>&1 | grep -q savedPath || echo "FAIL $1"; $U 30 script run -- --file $ES/Eyes.cs --entry Eyes.EyesCamOff >/dev/null 2>&1; }
game(){ $U 60 capture game_view -- --source camera --camera "Main Camera" --width 1920 --height 1080 --output-path $S/$1.png 2>&1 | grep -q savedPath || echo "FAIL $1"; }
frz 0; r Put 30 "[24.5, 15.5, 0]" >/dev/null; close d0-before 28.5 15.5 8
echo "BLAST $(r Blast 120 "[24.5, 15.5, 28.5, 15.5]")"
close d1-blast-plus0.4s 28.5 15.5 8; game d1-blast-gameplay
echo "RUN $(r Run 30 "[1.0]")"; close d2-plus1.4s 28.5 15.5 8
echo "RUN $(r Run 30 "[5.0]")"; close d3-plus6.4s 28.5 15.5 8
echo "RUN $(r Run 30 "[15.0]")"; close d4-plus21s 28.5 15.5 8
for k in 2 4; do echo "DODGE k$k $(r DodgeShot 60 "[0, 24.5, 15.5, $k]")"; close c-warrior-dodge-t$k 26.5 15.5 6; game c-warrior-dodge-t$k-gameplay; done
frz 1
echo "W-WALL $(r AtWall 60 "[0, 55, 7, 1, 13, 0.2, \"$O/freeze-wall-hit-warrior.csv\"]")"; close c-warrior-hits-wall-contact 54.5 7.5 5; game c-warrior-hits-wall-gameplay; frz 1
echo "M-BRK $(r AtWall 60 "[2, 52, 11, 1, 8, 0.2, \"$O/freeze-brk-hit-miner.csv\"]")"; close c-miner-cracks-wall-contact 51.5 11.5 5; frz 1
echo "DONE3 $(date -u +%T)"
