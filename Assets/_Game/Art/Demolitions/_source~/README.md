# Demolitions Expert art source (Blender 5.2, headless) - DG-07, PROPOSED (no bible page), FEMALE class

Unity skips this `_source~` folder. Unity imports, one level up: `Demolitions.fbx` (character), `DemolitionsProps.fbx`
(props) and `DemolitionsFace.png` (face decal for material CM_FaceDecal, body slot 1, alpha, no outline pass).

Same route as the Miner v3 (`../../Miner/_source~`): shared read-only helpers in `../../Characters/_source~/cm_art`
(KayKit 41-joint rig loader, mesh kit, FBX recipe) and the rig source `kaykit/Knight.glb` there. Class-only code is in
`demo_art/` (dpal palette with provenance, dkit = the Miner's builder with baked light/dark value steps in vertex
colour, toon3 Blender preview of CM/Toon, face_decal painter).

| File | What it is |
| --- | --- |
| `build_demolitions.py` | Builds everything from nothing: `demolitions.blend`, both FBX, the face PNG, `build_report.json` |
| `render_demolitions.py` | Gameplay camera (Azure, Crimson, 2x crop, props), 8 facings + team masks, orthographic turnaround, throw/plant poses, accent swap, prop states |
| `class_silhouettes.py` | Black-fill silhouettes of Demolitions, Warrior, Farmer, Miner at the gameplay camera, 8 facings, KayKit Idle; lean widths |
| `compare_demolitions.py` | Sheets (turnaround, poses, line-up beside the bible's three class fronts, 64 px silhouettes, props) and `checks.json` (system python3 + Pillow + numpy) |
| `verify_demolitions.py` | Re-imports both FBX files and checks them (rig, weights, closed shells, budgets, vertex-colour contract vs the Miner, clip binding) |

## Rebuild (every Blender run through cm-heavy, one at a time)
```sh
B=/Applications/Blender.app/Contents/MacOS/Blender; H="$HOME/bin/cm-heavy blender perl -e"
cd "Assets/_Game/Art/Demolitions/_source~"; S=<your own scratch folder>
~/bin/cm-heavy blender perl -e 'alarm 400; exec @ARGV' $B -b --factory-startup --python-exit-code 1 -P build_demolitions.py
~/bin/cm-heavy blender perl -e 'alarm 400; exec @ARGV' $B -b --factory-startup --python-exit-code 1 -P verify_demolitions.py
~/bin/cm-heavy blender perl -e 'alarm 900; exec @ARGV' $B -b --factory-startup --python-exit-code 1 -P render_demolitions.py -- $S/r 600
# other classes' FBX: git show origin/art/warrior-blender:Assets/_Game/Art/Characters/Warrior.fbx | git lfs smudge > $S/Warrior.fbx (same for Farmer; Miner from this branch)
~/bin/cm-heavy blender perl -e 'alarm 600; exec @ARGV' $B -b --factory-startup --python-exit-code 1 -P class_silhouettes.py -- $S/sil Warrior=$S/Warrior.fbx Farmer=$S/Farmer.fbx Miner=../../Miner/Miner.fbx
python3 compare_demolitions.py $S/r $S/sil ~/Obsidian/Wilson-Brain/Projects/Kaiju-Demo/unity-port/demolitions-v1
```

## Contents
- `Demolitions.fbx`: `Rig` (KayKit, 41 joints, unmodified, no baked clips: the KayKit clips bind by bone name, checked for
  Idle, Throw, PickUp, Hit_A, Death_A, Interact), `Demolitions_Body` (skinned), `Demolitions_TNTHand` (rigid on
  handslot.r; hide it on release). 11,910 triangles together.
- `DemolitionsProps.fbx`: `Demolitions_Mine_Buried`, `_Mine_Armed`, `_Mine_Armed_OwnerRing` (team dashes; show only to the
  owner's team), `_Mine_Triggered`, `_TNT_Flight`, `_CraterRim` (draw Void Navy at 30 %), `_Explosion` (art direction 6.1
  key frame). Authored static meshes, origin on the ground.

## Conventions (identical to the Miner)
Z up, faces -Y, character left = +X, 1 unit = 1 m, 2.0 u from the boot soles to the fuse spark (cap crown 1.875). One
`CM_Toon` material plus `CM_FaceDecal`; vertex colour attribute `Col` (float, point) with the team mask in alpha
(1.0 team cloth: scarf, armband, glove cuffs, keg middle band, TNT wrapper bands, hair ties; 0.75 team glow: goggle
lenses; 0 fixed). The fuse spark is fixed Warm Cream to Tool Brass, never team. Feature tags are `feat_*` vertex groups.
