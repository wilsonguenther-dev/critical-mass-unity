# Miner art source (Blender 5.2, headless)

Unity skips this `_source~` folder. Unity imports only the two FBX files one level up:
`Assets/_Game/Art/Miner/Miner.fbx` (character) and `Assets/_Game/Art/Miner/MinerBarricade.fbx` (prop).

## What is here

| File | What it is |
| --- | --- |
| `build_miner.py` | Builds the Miner (bible p08, CM-CHR-003) from nothing: `miner.blend`, `../Miner.fbx`, `../MinerBarricade.fbx`, `build_report.json` |
| `miner_art/mpal.py` | Miner palette (p08 swatch strip, sampled clusters) with provenance, team channels and Crimson swap values |
| `miner_art/mkit.py` | `MinerBuilder`: the shared `cm_art.kit.Builder` plus baked painted gradients, faceted crystals with a lighter lit facet, static-prop finalize |
| `miner_art/toon3.py` | Blender preview of CM/Toon at the 2026-11-12 art direction: 3 hard bands, team rim, Void Navy hull, team-mask debug output |
| `render_miner.py` | All renders: gameplay camera (Azure, Crimson, true 2x), barricade states, 8 facings + team masks, orthographic bible framings |
| `compare_miner.py` | Side-by-sides (bible left), 64 px silhouette sheet, team share, outline, indicative IoU, swatch dE2000 (system `python3` + Pillow + numpy) |
| `verify_miner.py` | Re-imports both FBX files in a fresh Blender and checks them; exit 1 on failure; writes `verify_report.json` |
| `miner.blend` | Built source scene (Git LFS). Rebuilt by the script; do not hand-edit |

Shared and read-only (not edited from here): `../../Characters/_source~/cm_art/` (rig loader, mesh kit, FBX export recipe),
`../../Characters/_source~/kaykit/Knight.glb` (KayKit 41-joint rig + 76 clips, CC0) and `rig_kaykit41.json`.

## Rebuild

```sh
B=/Applications/Blender.app/Contents/MacOS/Blender
cd "Assets/_Game/Art/Miner/_source~"
$B -b --factory-startup --python-exit-code 1 -P build_miner.py                  # miner.blend + both FBX
$B -b --factory-startup --python-exit-code 1 -P verify_miner.py                 # FBX re-import checks
$B -b --factory-startup --python-exit-code 1 -P render_miner.py -- /tmp/miner-r 600
python3 compare_miner.py /tmp/miner-r ~/Obsidian/Wilson-Brain/Projects/Kaiju-Demo/unity-port/miner-v1
```

## Contents of the FBX files

- `Miner.fbx`: `Rig` (KayKit, 41 joints, unmodified), `Miner_Body` (skinned), `Miner_PickHammer` (pick state) and
  `Miner_PickHammer_Hammer` (hammer state), both rigid on `handslot.r`. Unity shows one state at a time (the fold
  animation itself is not built). 11,696 triangles with both states, so either state is well inside 12,000.
- `MinerBarricade.fbx`: `Miner_Barricade_Folded`, `Miner_Barricade_Deployed`, `Miner_Barricade_Damaged`, three authored
  static meshes (no procedural destruction), 1,376 / 1,424 / 1,916 triangles, origin on the ground at the centre.

## Conventions (same as the Warrior)

Z up, faces -Y, character left = +X, 1 unit = 1 m, 2.0 u from boot soles to the helmet crystal tip (helmet dome top
1.755, crystal 0.245 above it). One `CM_Toon` material; region colour in vertex-colour RGB, team mask in alpha
(1.0 team cloth: neckerchief, sleeve/glove/boot trim, pick wrap; 0.75 team glow: every crystal; 0 fixed). Feature tags
are `feat_*` vertex groups kept in the .blend. `feat_*_detail` groups mark inner pieces drawn with the 1.5 px inner line.

## Not done (v1)

- No 256 px painted face decal; the face is primitives.
- The bible framings use a render-only capture stance (`STANCES` in `render_miner.py`); the in-game Idle keeps the arms
  low. Silhouette IoU against the bible crops is 0.50 to 0.72, below the 0.85 gate.
- The pick-to-hammer fold is two state meshes, not an animated transformation. Nothing imported into Unity yet.
