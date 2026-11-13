# Farmer art source (bible p07, CM-CHR-002; Blender 5.2.3, headless)

Unity skips this `_source~` folder. The only file Unity imports is `Assets/_Game/Art/Farmer/Farmer.fbx`.

The Farmer reuses the Warrior branch's shared kit **by import, unmodified**: `../../Characters/_source~/cm_art`
(KayKit 41-joint rig loader, mesh kit, FBX export settings, palette) and `../../Characters/_source~/kaykit/Knight.glb`.
Nothing in `Characters/` is edited. Farmer-only code lives in `farmer_art/`.

| File | What it is |
| --- | --- |
| `build_farmer.py` | Builds the Farmer from nothing: `farmer.blend`, `../Farmer.fbx`, `build_report.json` (triangles per part, tank, feature heights) |
| `farmer_art/fpalette.py` | p07 palette (swatch-strip hex + sampled colours, each tagged), Crimson swap, the new mask code 0.25 (fixed biomass glow) |
| `farmer_art/fkit.py` | `cm_art.kit.Builder` subclass: painted vertical gradient per part (+6 % top, -10 % bottom), `tube()` (wavy hair clumps, hoses, straps, hook arc), `bcyl()` (chamfered cylinders), `leaf()` |
| `farmer_art/toon3.py` | Blender preview of CM/Toon at the 2026-11-12 spec: three N.L bands (1.00 / 0.84 / 0.66 + 12 % Void Navy), thin team rim, unlit glows, ID-mask view for pixel counts, Void Navy inverted hull |
| `render_farmer.py` | EEVEE renders: gameplay camera (perspective, pitch 55, FOV 35, 23.4 u, yaw 45) on grass and grey, 8 screen facings + ID masks, orthographic bible framings, harvest and root-seed poses, silhouettes (also of another character's .blend, read-only) |
| `compare_farmer.py` | Finishes the 2x renders (outer outline, downscale), writes side-by-sides (bible left), the 64 px silhouette sheet, and `checks.json` (IoU, team-colour %, lime invariance, outline px, dE2000) |
| `verify_farmer.py` | Re-imports the FBX in a fresh Blender and checks axes, scale, 41 joints, clip carry-over, weights, manifold, triangles, mask codes, bible features (exit 1 on failure) |
| `farmer_art/face_decal.py` | v2: the 256 x 256 painted face decal (`../Farmer_Face.png`), drawn procedurally: lidded eyes with lash flicks, shaded iris, two highlights, cocked left brow, lopsided smirk, cheeks |
| `bible_masks.py` | v2: clean bible silhouettes (fixed paper colour, character-only components, small holes filled); `compare_farmer.py` uses it for every IoU |
| `capture_poses.json` | v2: per-framing capture pose (base clip + frame, world-axis bone rotations, camera azimuth/elevation) written by `render_farmer.py ... fit`; the bible and pose renders read it |
| `farmer.blend` | Built scene (Git LFS). Rebuilt by the script; never hand-edit |

## Rebuild

```sh
B=/Applications/Blender.app/Contents/MacOS/Blender
cd "Assets/_Game/Art/Farmer/_source~"
$B -b --factory-startup --python-exit-code 1 -P build_farmer.py        # farmer.blend + ../Farmer.fbx
$B -b --factory-startup --python-exit-code 1 -P verify_farmer.py       # FBX re-import checks (31)
python3 bible_masks.py ~/critical-mass/loop/parity/ref /tmp/farmer-masks                # clean bible masks (.npy)
$B -b --factory-startup --python-exit-code 1 -P render_farmer.py -- /tmp/farmer-masks fit   # optional: refit capture_poses.json (GPU raster, ~8 min)
$B -b --factory-startup --python-exit-code 1 -P render_farmer.py -- /tmp/farmer-r game,facings,bible,poses
$B -b --factory-startup --python-exit-code 1 -P render_farmer.py -- /tmp/warrior-sil sil ../../Characters/_source~/warrior.blend
python3 compare_farmer.py /tmp/farmer-r <out_dir> /tmp/warrior-sil
```

Deterministic (factory startup, no randomness). Render output goes outside the repo.

## Conventions (same as the Warrior, plus two)

- Space, rig, objects: Blender rest pose (KayKit T-pose), Z up, faces -Y, left = +X, metres. `Farmer_Body` (everything
  worn, including the globe tank and the root-seed pod on the left hip) + `Farmer_Staff` (rigid on `handslot.r`, placed in
  the KayKit `Idle` pose and mapped back to rest). One material `CM_Toon` on both.
- Colour in vertex colour RGB (painted gradient baked in). Alpha = mask: `0` fixed, `1.0` team cloth (teal #4999A5 ->
  coral #E9705F), **`0.25` fixed self-lit glow (biomass #B7F238, never team-swapped): new code, the Unity CM/Toon must
  learn it**. No team glow (0.75) on the Farmer: the bible puts team colour on trim only.
- `outline_w` vertex group (Blender only): 0 on face features and the globe glint so the hull draws no rings there; the
  Unity outline pass needs an equivalent per-vertex width (open item for the shader).
- Outline in the previews: inverted hull 1.5 px (inner creases) + 1.5 px dilation outside the silhouette = 3.0 px outer,
  constant in screen pixels (renders are 2x and halved).
- Budget 12,000 triangles including the staff (11,784 in v2). Leaves are unbevelled thin blades; every block is bevelled.

## Version 2 additions (2026-11-12)

- Painted face decal: `Farmer_Body` carries a `UVMap` (front planar projection on the head skin, every other face on a
  transparent texel at (0.002, 0.002)) and `../Farmer_Face.png` (256 x 256 RGBA). `CM_Toon` lays the decal over the vertex
  colour by its alpha, exactly as the Warrior v2 does with `Warrior_Face.png`; the Unity material needs the same texture slot.
- The globe tank rides behind the LEFT shoulder (+X), as p07 front and back draw it (v1 had it on the right).
- The tank hose is part of `Farmer_Body` in three skinned spans (back, right arm, and a last span rigid on `handslot.r`)
  ending on a brass port on the staff, so it meets the staff in every clip.
- Capture poses are fitted to the bible crops by silhouette IoU (coordinate descent with seeded restarts) instead of the
  stock Idle pose; the bible's three-quarter crop is drawn with the staff on the far side, which no pose of a right-handed
  staff reproduces exactly (see the v2 report).

## Known shared-kit quirk

The re-imported FBX shows two identical colour layers (`Col`, `Col.001`) because `cm_art.kit.Builder.finalize` writes the
bmesh layer and then a second attribute. Same on the Warrior; harmless, but worth one line in the kit when it is next touched.
