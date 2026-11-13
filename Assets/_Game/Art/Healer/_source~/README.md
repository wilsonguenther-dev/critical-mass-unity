# Healer art source (Blender 5.2, headless) — proposed sixth class, no bible page

Unity skips this `_source~` folder and imports `../Healer.fbx` (Rig + `Healer_Body` skinned + `Healer_Lantern` rigid on
`handslot.l`), `../HealerProps.fbx` (static `Healer_BloomGauntlet_Prop`, `Healer_Lantern_Prop`) and `../HealerFace.png`
(assign to `CM_FaceDecal`: alpha decal, no outline pass).

Design (v2, artist direction): armoured front-line battle healer, no sword. Heal = bloom gauntlet (right hand, part of
the body mesh, lime biomass orb). Remove corruption = cleansing lantern (left hand, warm cream-gold light).

| File | What |
| --- | --- |
| `build_healer.py` | builds everything: healer.blend, both FBX, face PNG, build_report.json |
| `healer_art/hpal.py` | palette, same contract and team values as the Miner |
| `healer_art/face_decal.py` | 256 px painted eyes/lashes/blush |
| `render_healer.py` | gameplay camera (both teams, 2x), 8 facings, ortho turnaround, face close-up, props |
| `sheet.py`, `compare_healer.py` | review sheets, 64 px silhouette overlap, line-up, side-by-sides, checks.json |
| `verify_healer.py` | FBX re-import checks incl. the KayKit Idle binding probe -> verify_report.json |

Reused unchanged from this branch: `../../Characters/_source~/cm_art` (rig, kit, FBX recipe) and
`../../Miner/_source~/miner_art` (mkit, toon3; their palette module is swapped for hpal at import).

## Rebuild
```sh
B=/Applications/Blender.app/Contents/MacOS/Blender; H="perl -e 'alarm 1500; exec @ARGV'"
cd "Assets/_Game/Art/Healer/_source~"
~/bin/cm-heavy blender perl -e 'alarm 600; exec @ARGV' $B -b --factory-startup --python-exit-code 1 -P build_healer.py
~/bin/cm-heavy blender perl -e 'alarm 600; exec @ARGV' $B -b --factory-startup --python-exit-code 1 -P verify_healer.py
~/bin/cm-heavy blender perl -e 'alarm 1500; exec @ARGV' $B -b --factory-startup --python-exit-code 1 -P render_healer.py -- /tmp/healer-r 600
python3 compare_healer.py /tmp/healer-r ~/Obsidian/Wilson-Brain/Projects/Kaiju-Demo/unity-port/healer-v1
```
