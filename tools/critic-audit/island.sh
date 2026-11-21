#!/bin/bash
# Critic audit area E: same six camera stations at 0, 2, 4, 6 min and match end, bot match (actor 0 on autopilot), per seed.
# Cell census per time (cells + lift files) and a position heatmap / blast log. Pace 4 between stations; frozen (pace 0) while shooting.
U=~/cm-editor-eyes/bin/u; S=~/cm-incidents/critic-audit-2026-11-21/shots/island; O=~/cm-incidents/critic-audit-2026-11-21/data/island; C=~/cm-wt-audit-critic/tools/critic-audit/Critic.cs; ES=~/cm-editor-eyes/scripts
mkdir -p $S $O
ev(){ $U ${2:-30} script eval --caller plugin --skill unity-cli "$1" 2>&1 | grep -v "^diagnostics\|^compileMs\|^executeMs\|^assemblyName\|^$" | tail -1 | cut -c1-300; }
DRV='var d = UnityEngine.Object.FindAnyObjectByType<CriticalMass.Runtime.Game.SimDriverBehaviour>();'
STN=("hill 56 40" "azurebase 12 36" "crimsonbase 100 36" "westmid 30 38" "eastmid 82 38" "shrine 56 10")
shoot(){ # seed tag
  ev "$DRV d.TimeScale = 0; UnityEditor.EditorUtility.audioMasterMute = true; UnityEngine.AudioListener.volume = 0f; return \"frozen\";" >/dev/null
  $U 60 script run -- --file $C --entry Critic.Census --args "[\"$O/s$1\", \"$2\"]" 2>&1 | grep ^result | sed "s/^/s$1 /"
  for st in "${STN[@]}"; do set -- $1 $2 $st; n=$3; x=$4; z=$5
    $U 30 script run -- --file $ES/Eyes.cs --entry Eyes.EyesCam --args "[$x, 0.5, -$z, 55, 225, 26]" >/dev/null 2>&1
    $U 60 capture game_view -- --source camera --camera "Eyes Cam" --overlay false --width 1280 --height 720 --output-path $S/s$1-$n-$2.png 2>&1 | grep -q savedPath || echo "FAIL s$1-$n-$2"
  done
  $U 30 script run -- --file $ES/Eyes.cs --entry Eyes.EyesCamOff >/dev/null 2>&1
  ev "$DRV d.TimeScale = 4; return \"resumed\";" >/dev/null
}
for seed in ${SEEDS:-4242 7 31337}; do
  $U 60 script run -- --file $ES/Match.cs --entry Match.Start --args "[0, $seed]" 2>&1 | grep ^result
  sleep 6
  $U 30 script run -- --file $ES/Match.cs --entry Match.Autopilot --args "[true]" 2>&1 | grep ^result
  $U 30 script run -- --file $C --entry Critic.Heat --args "[\"$O/s$seed\"]" 2>&1 | grep ^result
  ev "$DRV d.TimeScale = 4; return \"pace 4 t \" + d.World.Match.Time;"
  done0=0; done2=0; done4=0; done6=0; doneE=0
  for i in $(seq 1 200); do
    st=$(ev "$DRV var w = d.World; return w.Match.Phase + \" \" + ((int)w.Match.Time) + \" \" + w.Config.MatchSeconds;"); ph=$(echo $st | awk '{print $1}'); t=$(echo $st | awk '{print $2}'); ms=$(echo $st | awk '{print $3}')
    if [ "$ph" != "Playing" ] && [ $done0 = 1 ]; then echo "s$seed ended early at $t ($ph)"; break; fi
    if [ $done0 = 0 ] && [ "${t:-0}" -ge 3 ]; then done0=1; shoot $seed m0; fi
    if [ $done2 = 0 ] && [ "${t:-0}" -ge 120 ]; then done2=1; shoot $seed m2; fi
    if [ $done4 = 0 ] && [ "${t:-0}" -ge 240 ]; then done4=1; shoot $seed m4; fi
    if [ $done6 = 0 ] && [ "${t:-0}" -ge 360 ]; then done6=1; shoot $seed m6; fi
    if [ $doneE = 0 ] && [ "${t:-0}" -ge $(( ${ms%.*} - 8 )) ]; then doneE=1; shoot $seed end; break; fi
    sleep 2
  done
  echo "s$seed done $(date -u +%H:%M:%S)"
done
ev "$DRV d.TimeScale = 1; return \"pace 1\";"
