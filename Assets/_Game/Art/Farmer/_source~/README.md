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
| `farmer.blend` | Built scene (Git LFS). Rebuilt by the script; never hand-edit |

## Rebuild

```sh
B=/Applications/Blender.app/Contents/MacOS/Blender
cd "Assets/_Game/Art/Farmer/_source~"
$B -b --factory-startup --python-exit-code 1 -P build_farmer.py        # farmer.blend + ../Farmer.fbx
$B -b --factory-startup --python-exit-code 1 -P verify_farmer.py       # FBX re-import checks (31)
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
- Budget 12,000 triangles including the staff (11,852 now). Leaves are unbevelled thin blades; every block is bevelled.

## Known shared-kit quirk

The re-imported FBX shows two identical colour layers (`Col`, `Col.001`) because `cm_art.kit.Builder.finalize` writes the
bmesh layer and then a second attribute. Same on the Warrior; harmless, but worth one line in the kit when it is next touched.
