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
$B -b --factory-startup --python-exit-code 1 -P build_warrior.py        # warrior.blend, ../Warrior.fbx, ../Warrior_Face.png
$B -b --factory-startup --python-exit-code 1 -P verify_warrior.py       # FBX re-import checks (40), all 76 clips
$B -b --factory-startup --python-exit-code 1 -P render_warrior.py -- bible   /tmp/w 720   # orthographic bible framings
$B -b --factory-startup --python-exit-code 1 -P render_warrior.py -- game    /tmp/w       # gameplay camera, both teams
$B -b --factory-startup --python-exit-code 1 -P render_warrior.py -- facings /tmp/w       # 8 facings + team ID masks
python3 compare_warrior.py bible   /tmp/w <out>     # IoU, swatch dE2000, sbs-<framing>.jpg
python3 compare_warrior.py game    /tmp/w <out>     # gameplay crops, pixel size
python3 compare_warrior.py facings /tmp/w <out>     # team-colour share per facing, directions sheet, 64 px silhouettes
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

## Version 2 (2026-11-12)

- Shapes: pauldrons, knee guards, boot feet, toe caps and shield rim are rounded slabs (superellipsoids); hair is 12
  sculpted tapered clumps plus a 6-clump palm topknot with a brass ring; every box and prism is bevelled 2 segments.
- Colour: vertex colour carries a +6 % value gradient toward the top of each part and a baked -10 % crevice term
  (ray occlusion); creases on the belt, scarf, shield rim, bracers and undershirt are modelled 25 % darker grooves.
- Face: a 256 x 256 painted decal (`cm_art/face.py`, drawn by script) on the head's front UV region, written to
  `../Warrior_Face.png`; the body mesh carries `UVMap` (non-face faces sample a transparent texel).
- Team read: scarf cowl under the chin, wide tabards front and back, shield core 0.35 of the shield diameter, glowing
  cracks on pauldrons and every rim slab, blade edge turned outward; at least 12 % team pixels in all 8 facings.
- `cm_art/toon.py` previews the 3-band ramp (x1.00 / x0.84 / x0.66 + 12 % Void Navy) from N.L, team rim, Void Navy hull.
- Capture poses are per framing in `render_warrior.py` (`POSES`): a clip frame plus joint rotations. The bible
  profile draws no shield, so the profile pose holds the shield arm out to the side, shield edge-on behind the body.

## Still not done

- The Unity `CM/Toon` shader (it must also sample `Warrior_Face.png` through `UVMap`); nothing imported in Unity yet.
- Cleave and shield-charge bible crops include swoosh and rock effects that are not modelled, so their IoU stays low.
- No LODs.
