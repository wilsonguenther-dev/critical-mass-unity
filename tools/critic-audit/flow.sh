#!/bin/bash
# Critic audit: every flow screen in order via the game's own FlowDirector API (no input), 1920x1080 camera capture + UI text scan.
U=~/cm-editor-eyes/bin/u; S=~/cm-incidents/critic-audit-2026-11-21/shots; O=~/cm-incidents/critic-audit-2026-11-21/data; C=~/cm-wt-audit-critic/tools/critic-audit/Critic.cs
ev(){ $U ${2:-30} script eval --caller plugin --skill unity-cli "$1" 2>&1 | grep -v "^diagnostics\|^compileMs\|^executeMs\|^assemblyName\|^$" | tail -1 | cut -c1-300; }
cap(){ sleep ${2:-1.5}; $U 60 capture game_view -- --source camera --camera "Main Camera" --width 1920 --height 1080 --output-path $S/$1.png 2>&1 | grep -q savedPath || echo "FAIL $1"; $U 60 script run -- --file $C --entry Critic.UiScan --args "[\"$O/ui-text.csv\", \"$1\"]" 2>&1 | grep ^result | cut -c1-400; }
ev 'UnityEditor.EditorUtility.audioMasterMute = true; UnityEngine.AudioListener.volume = 0f; return "m";' >/dev/null
F='CriticalMass.Runtime.Flow.FlowDirector.Current'
echo "title: $(ev "$F.ShowTitle(); return \"ok \" + $F.Visible;")"; cap b01-title 2.5
echo "menu: $(ev "$F.ShowMenu(); return \"ok \" + $F.Visible;")"; cap b02-mainmenu
echo "modes: $(ev "$F.ShowModes(CriticalMass.Runtime.Flow.GameFlow.LastSetup.Mode); return \"ok \" + $F.Visible;")"; cap b03-modes
echo "setup: $(ev "$F.ShowSetup(); return \"ok \" + $F.Visible;")"; cap b04-setup
echo "class: $(ev "$F.HideFlowScreens(); var cs = CriticalMass.Runtime.Menus.ClassSelect.ShowSetup(CriticalMass.Sim.Config.TeamId.Azure); cs.FocusClass(CriticalMass.Sim.Config.ClassId.Demolitions); return \"open \" + CriticalMass.Runtime.Menus.ClassSelect.IsOpen;")"; cap b05-classselect
ev 'CriticalMass.Runtime.Menus.ClassSelect.Hide(); return "hid";' >/dev/null
echo "loading: $(ev 'var l = CriticalMass.Runtime.Menus.LoadingScreen.ShowForMatch(1337); return l == null ? "null" : "shown";')"; cap b06-loading 0.8
ev 'foreach (var l in UnityEngine.Object.FindObjectsByType<CriticalMass.Runtime.Menus.LoadingScreen>(UnityEngine.FindObjectsSortMode.None)) UnityEngine.Object.Destroy(l.gameObject); return "x";' >/dev/null
for tab in Controls Audio Video Game; do
  echo "settings $tab: $(ev "$F.OpenSettings(CriticalMass.Runtime.Menu.PauseMenu.Tab.$tab, null); return \"ok \" + $F.SettingsOpen;")"; cap b07-settings-$tab
done
echo "codex: $(ev "return \"page \" + $F.OpenPausePage(CriticalMass.Runtime.Codex.CodexPage.Label, null);")"; cap b08-codex
echo "back to match: $(ev "$F.HideFlowScreens(); var pm = UnityEngine.Object.FindAnyObjectByType<CriticalMass.Runtime.Menu.PauseMenu>(); if (pm != null) pm.Resume(); return \"vis \" + $F.Visible + \" state \" + CriticalMass.Runtime.Flow.GameFlow.State;")"
echo "pause: $(ev 'var pm = UnityEngine.Object.FindAnyObjectByType<CriticalMass.Runtime.Menu.PauseMenu>(); pm.Pause(); pm.ShowRoot(); return "paused";')"; cap b09-pause
ev 'var pm = UnityEngine.Object.FindAnyObjectByType<CriticalMass.Runtime.Menu.PauseMenu>(); pm.Resume(); return "resumed";' >/dev/null
