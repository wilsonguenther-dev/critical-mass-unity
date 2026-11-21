#!/bin/bash
# Miner breaks a map wall cell hit by hit (47,46 near the hill): picture after each hit, frozen 2 ticks after contact.
U=~/cm-editor-eyes/bin/u; O=~/cm-incidents/critic-audit-2026-11-21/data; S=~/cm-incidents/critic-audit-2026-11-21/shots; C=~/cm-wt-audit-critic/tools/critic-audit/Critic.cs; ES=~/cm-editor-eyes/scripts
r(){ $U ${2:-120} script run -- --file $C --entry Critic.$1 --args "$3" 2>&1 | grep "^result\|rror" | cut -c1-500; }
fr(){ $U 30 script eval --caller plugin --skill unity-cli "var d = UnityEngine.Object.FindAnyObjectByType<CriticalMass.Runtime.Game.SimDriverBehaviour>(); d.TimeScale = $1; UnityEditor.EditorUtility.audioMasterMute = true; UnityEngine.AudioListener.volume = 0f; return \"p\";" >/dev/null 2>&1; }
shot(){ $U 30 script run -- --file $ES/Eyes.cs --entry Eyes.EyesCam --args "[46.6, 0.9, -46.5, 50, 200, 7]" >/dev/null 2>&1; $U 60 capture game_view -- --source camera --camera "Eyes Cam" --overlay false --width 1600 --height 900 --output-path $S/$1.png 2>&1 | grep -q savedPath || echo "FAIL $1"; $U 30 script run -- --file $ES/Eyes.cs --entry Eyes.EyesCamOff >/dev/null 2>&1; }
fr 0; r Put 30 "[46.3, 46.5, 0]" >/dev/null; shot c-wall-0-intact
for h in 1 2 3; do fr 1; echo "HIT $h $(r AtWall 60 "[2, 47, 46, 1, 9, 0.2, \"$O/freeze-miner-hit$h.csv\"]")"; shot c-wall-hit$h; done
fr 1; r Run 30 "[3.0]" >/dev/null; shot c-wall-after3s; fr 1
