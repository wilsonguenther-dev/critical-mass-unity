# Character art source (Blender 5.2.3, headless)

Unity skips this `_source~` folder (P-08). The only file Unity imports is the FBX one level up:
`Assets/_Game/Art/Characters/Warrior.fbx`.

## What is here

| File | What it is |
| --- | --- |
| `build_warrior.py` | Builds the Warrior (bible p06) from nothing: `warrior.blend`, `../Warrior.fbx`, `rig_kaykit41.json`, `build_report.json` |
| `cm_art/palette.py` | Shared bible palette (p05 core hex + p06 swatches + sampled colours, each tagged with its source) and the team-mask codes |
| `cm_art/rig.py` | Loads the shared KayKit 41-joint rig and its 76 clips from `kaykit/Knight.glb`, deletes every KayKit mesh |
| `cm_art/kit.py` | Mesh kit: closed primitives with colour, team mask, feature tags and skin weights |
| `cm_art/toon.py` | Blender preview of `CM/Toon`: flat colour, two-band ramp, team swap, inverted-hull outline (render only) |
| `cm_art/export.py` | FBX export settings for Unity (the settings and the reason for each are in the file) |
| `render_warrior.py` | Headless EEVEE renders: front, three-quarter, side, back, in-game oblique, Azure and Crimson |
| `compare_warrior.py` | Bible crop next to our render per framing, a contact sheet, an indicative silhouette IoU (system `python3` + Pillow) |
| `verify_warrior.py` | Re-imports the FBX in a fresh Blender and checks it (exit 1 on failure); writes `verify_report.json` |
| `kaykit/Knight.glb`, `kaykit/LICENSE.txt` | KayKit Adventurers 1.0 Knight (CC0), the rig and clip source, copied in so the build needs nothing outside the repo |
| `warrior.blend` | The built source scene (Git LFS). Rebuilt by the script; do not hand-edit, edit the script |

## Rebuild

```sh
B=/Applications/Blender.app/Contents/MacOS/Blender
cd "Assets/_Game/Art/Characters/_source~"
$B -b --factory-startup --python-exit-code 1 -P build_warrior.py      # warrior.blend + ../Warrior.fbx
$B -b --factory-startup --python-exit-code 1 -P verify_warrior.py     # FBX re-import checks
$B -b --factory-startup --python-exit-code 1 -P render_warrior.py -- /tmp/warrior-renders 720
python3 compare_warrior.py /tmp/warrior-renders /tmp/warrior-compare
```

Always `-b` (no window). The build is deterministic (factory startup, fixed seed). Render output goes outside the
repo; nothing in this folder should be committed from a render run.

## Conventions every character follows

- **Space.** Blender rest pose (the KayKit T-pose), Z up, the character faces -Y, its left is +X, 1 unit = 1 m. The bible's
  "viewer's right in front view" is the character's left, +X.
- **Rig.** The KayKit `Rig` armature, unmodified: 41 joints, names and rest positions exactly as in Knight.glb
  (`rig_kaykit41.json` lists them). Never move or rename a joint; the 76 KayKit clips depend on them.
- **Objects.** `<Class>_Body` (everything worn, one skinned mesh) plus one object per held item (`Warrior_Shield`,
  `Warrior_Cleaver`), each skinned rigidly to its hand slot (`handslot.l` / `handslot.r`).
- **Colour.** One material, `CM_Toon`, on every mesh. Region colour lives in the vertex colour RGB (bible hex, exported
  as sRGB). The vertex colour ALPHA is the team mask: `0` fixed, `1.0` team cloth (teal / coral), `0.75` team glow
  (cyan / red-orange, unlit), `0.5` reserved team-dark (unused on the Warrior). Unity keeps one vertex colour set, so
  the mask rides in alpha.
- **Feature tags.** Every primitive carries a `feat_*` vertex group (`feat_shield`, `feat_cleaver`, `feat_topknot`,
  `feat_stone_armour`, `feat_team_cloth`, `feat_team_glow`, ...). They stay in the `.blend` for checks; the FBX drops
  non-bone groups.
- **Held items** are placed where the bible draws them in the KayKit `Idle` pose, then mapped back to rest through the
  hand-slot bone (`held_matrix` in `build_warrior.py`), so they sit right in Idle and follow every clip.
- **Budget.** 12,000 triangles per character including held items (Warrior: 11,900), so 32 on screen is about 380k.

## Adding the Farmer or the Miner

1. Copy `build_warrior.py` to `build_farmer.py` (or `build_miner.py`); keep `main()`, `held_matrix()` and the imports.
2. Replace the `build_*` part functions with the new class from bible p07 (p08). Use only `cm_art/palette.py` names;
   add new swatches there with their provenance tag (Farmer lime biomass glow is a fixed colour, not a team channel;
   the p07 tank and p08 crystals need their own entries).
3. Name the objects `Farmer_Body` + `Farmer_Staff` (Miner: `Miner_Body` + `Miner_PickHammer`), skin held items to the
   hand slots, set `OUT_BLEND` / `OUT_FBX` to `farmer.blend` / `../Farmer.fbx`.
4. Copy `verify_warrior.py` and `render_warrior.py` and change the object names, the feature list and the bible
   crops (`07-*.png`, `08-*.png`); keep the triangle budget.
5. Run build, verify, render, compare; look at the side-by-sides before committing.

## Not done (v1)

- `CM/Toon` itself (Unity shader) is not built; `cm_art/toon.py` is only a Blender preview of the contract.
- Nothing has been imported into Unity. Axes, scale and Generic clip carry-over are verified in Blender only.
- Faces, hair and armour are built from primitives; they read as the bible's character but not at its painted
  finish. The profile view is the weakest. No hand-sculpted pass.
- The bible "shield charge", "cleave" and "front shield" framings are not rendered; renders use a capture stance
  (KayKit Idle frame 1 with the arms and legs opened, `BIBLE_STANCE` in `render_warrior.py`), not a new clip.
- No LODs, no UVs or textures (none needed for flat vertex colour), no ID-mask render for the U-010 gate.
