#!/bin/bash
# Wall-hit pictures reframed; the three camera views with HUD text counts; bot-built pieces after 150 s at pace 6.
U=~/cm-editor-eyes/bin/u; O=~/cm-incidents/critic-audit-2026-11-21/data; S=~/cm-incidents/critic-audit-2026-11-21/shots; C=~/cm-wt-audit-critic/tools/critic-audit/Critic.cs; ES=~/cm-editor-eyes/scripts
r(){ $U ${2:-120} script run -- --file $C --entry Critic.$1 --args "$3" 2>&1 | grep "^result\|rror" | cut -c1-900; }
ev(){ $U ${2:-30} script eval --caller plugin --skill unity-cli "$1" 2>&1 | grep -v "^diagnostics\|^compileMs\|^executeMs\|^assemblyName\|^$" | tail -1 | cut -c1-400; }
DRV='var d = UnityEngine.Object.FindAnyObjectByType<CriticalMass.Runtime.Game.SimDriverBehaviour>(); UnityEditor.EditorUtility.audioMasterMute = true; UnityEngine.AudioListener.volume = 0f;'
close(){ $U 30 script run -- --file $ES/Eyes.cs --entry Eyes.EyesCam --args "[$2, ${6:-0.8}, -$3, ${5:-50}, ${7:-225}, ${4:-8}]" >/dev/null 2>&1; $U 60 capture game_view -- --source camera --camera "Eyes Cam" --overlay false --width 1600 --height 900 --output-path $S/$1.png 2>&1 | grep -q savedPath || echo "FAIL $1"; $U 30 script run -- --file $ES/Eyes.cs --entry Eyes.EyesCamOff >/dev/null 2>&1; }
game(){ sleep ${2:-0.5}; $U 60 capture game_view -- --source camera --camera "Main Camera" --width 1920 --height 1080 --output-path $S/$1.png 2>&1 | grep -q savedPath || echo "FAIL $1"; }
ev "$DRV d.TimeScale = 1; return \"p\";" >/dev/null
echo "W-WALL $(r AtWall 60 "[0, 55, 7, 1, 13, 0.2, \"$O/freeze-wall-hit-warrior2.csv\"]")"; close c-warrior-hits-wall 53.8 7.5 7 50 1.2 270
ev "$DRV d.TimeScale = 1; return \"p\";" >/dev/null
echo "M-BRK $(r AtWall 60 "[2, 50, 11, 1, 8, 0.2, \"$O/freeze-brk-hit-miner2.csv\"]")"; close c-miner-cracks-wall 48.8 11.5 7 50 1.2 270
ev "$DRV d.TimeScale = 1; return \"p\";" >/dev/null
# camera views (actor 0 human, alive)
for v in ThirdPerson FirstPerson TopDown; do
  echo "VIEW $v: $(ev "$DRV CriticalMass.Runtime.Camera.GroundView.ViewOverride = CriticalMass.Runtime.Camera.CameraView.$v; return \"play \" + CriticalMass.Runtime.Camera.GroundView.PlayActive + \" view \" + CriticalMass.Runtime.Camera.GroundView.ActiveView;")"
  game b10-view-$v 1.5; r UiScan 30 "[\"$O/ui-text.csv\", \"b10-view-$v\"]"
  echo "  after: $(ev "return \"play \" + CriticalMass.Runtime.Camera.GroundView.PlayActive + \" view \" + CriticalMass.Runtime.Camera.GroundView.ActiveView;")"
done
ev 'CriticalMass.Runtime.Camera.GroundView.ViewOverride = null; return "cleared";'
echo "DONE4 $(date -u +%T)"
