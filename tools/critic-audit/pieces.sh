#!/bin/bash
# Bot-built pieces (ladders, bridges) and Builder walls after ~200 s at pace 6: list, then close-ups at a low angle.
U=~/cm-editor-eyes/bin/u; O=~/cm-incidents/critic-audit-2026-11-21/data; S=~/cm-incidents/critic-audit-2026-11-21/shots; ES=~/cm-editor-eyes/scripts
ev(){ $U ${2:-30} script eval --caller plugin --skill unity-cli "$1" 2>&1 | grep -v "^diagnostics\|^compileMs\|^executeMs\|^assemblyName\|^$" | tail -1 | cut -c1-2000; }
DRV='var d = UnityEngine.Object.FindAnyObjectByType<CriticalMass.Runtime.Game.SimDriverBehaviour>(); UnityEditor.EditorUtility.audioMasterMute = true; UnityEngine.AudioListener.volume = 0f;'
$U 60 script run -- --file $ES/Match.cs --entry Match.Start --args "[4, 2026]" 2>&1 | grep ^result; sleep 6
$U 30 script run -- --file $ES/Match.cs --entry Match.Autopilot --args "[true]" 2>&1 | grep ^result
ev "$DRV d.TimeScale = 6; return \"p6\";"
for i in $(seq 1 30); do t=$(ev "$DRV return ((int)d.World.Match.Time).ToString();"); [ "${t:-0}" -ge 200 ] && break; sleep 3; done
ev "$DRV d.TimeScale = 0; return \"t \" + d.World.Match.Time;"
$U 60 script run -- --file $ES/Ladder.cs --entry Ladder.Pieces 2>&1 | grep -v "^diag\|^compile\|^exec\|^assembly" > $O/pieces.txt
L=$(ev "$DRV var m = d.World.Map; var sb = new System.Text.StringBuilder(); for (int i = 0; i < m.Cells.Length; i++) if (m.Cells[i] == 8 || m.Cells[i] == 9) sb.Append(i % m.Width).Append(',').Append(i / m.Width).Append(':').Append(m.Cells[i]).Append(' '); return sb.ToString();")
echo "walls: $L" >> $O/pieces.txt; cat $O/pieces.txt | cut -c1-300
n=0
for c in $(grep -o "c=([0-9.]*,[0-9.]*)" $O/pieces.txt | head -3 | tr -d 'c=()'); do x=${c%,*}; z=${c#*,}; n=$((n+1))
  $U 30 script run -- --file $ES/Eyes.cs --entry Eyes.EyesCam --args "[$x, 0.6, -$z, 30, 200, 6]" >/dev/null 2>&1; $U 60 capture game_view -- --source camera --camera "Eyes Cam" --overlay false --width 1600 --height 900 --output-path $S/c-piece-$n-low.png 2>&1 | grep -q savedPath || echo FAIL
  $U 30 script run -- --file $ES/Eyes.cs --entry Eyes.EyesCam --args "[$x, 0.6, -$z, 55, 225, 9]" >/dev/null 2>&1; $U 60 capture game_view -- --source camera --camera "Eyes Cam" --overlay false --width 1600 --height 900 --output-path $S/c-piece-$n-game.png 2>&1 | grep -q savedPath || echo FAIL
done
for c in $(echo $L | tr ' ' '\n' | head -2); do xz=${c%:*}; x=${xz%,*}; z=${xz#*,}; n=$((n+1))
  $U 30 script run -- --file $ES/Eyes.cs --entry Eyes.EyesCam --args "[$x.5, 0.6, -$z.5, 30, 200, 6]" >/dev/null 2>&1; $U 60 capture game_view -- --source camera --camera "Eyes Cam" --overlay false --width 1600 --height 900 --output-path $S/c-wallpiece-$n-low.png 2>&1 | grep -q savedPath || echo FAIL
done
$U 30 script run -- --file $ES/Eyes.cs --entry Eyes.EyesCamOff >/dev/null 2>&1
ev "$DRV d.TimeScale = 1; return \"p1\";"
