# BuilderTex: textured Builder pilot (experimental)

Selected only by the `-builderTex 1` command-line switch. The old Builder stays the default.

## Build

Headless Blender 5.2.3 through the machine lock:

    CM_HEAVY_TIMEOUT=2400 ~/bin/cm-heavy blender /opt/homebrew/bin/blender -b --factory-startup \
      -P Assets/_Game/Art/Characters/_source~/builder_tex/build_char_remesh.py -- \
      Assets/_Game/Art/Characters/_source~/builder_tex/builder_tex.json <repo>

`build_char_remesh.py` (method 2) runs the following steps:

- voxel-remeshes the generated mesh with morphological closing (body, pack and rigid items separately)
- collapses it to LOD0 6,834 / LOD1 1,800 / Shadow 700 triangles
- unwraps by region (58% UV coverage)
- Cycles-bakes a 1024 base colour with the team mask in alpha
- weights by bone heat with region masks, edge-hop zone blends and ring smoothing (4 influences)
- exports the FBX and renders the proofs

It reuses the head and tail of `build_char_tex.py` (method 1). Outputs go to `out_dir_remesh`. Copy `BuilderTex.fbx` and `BuilderTex_Base.png` into the repo by hand.

The current outputs are recorded in these files:

- `BuilderTex_report_v2_final.json`: full report of the build that produced the committed FBX and texture.
- `BuilderTex_stretch.csv`: the clip stretch table. `BuilderTexTests` prints it and does not assert on it.

## Known limitation: imperfect weights at three spots

These three spots are weighted imperfectly:

- **Inner thigh just above the knee.** Hips and upper-leg weight sits next to lower-leg/foot weight.
- **Front of the shin.**
- **Right armpit.** Spine/chest/hips weight is mixed with the upper arm.

**Measured stretch (single worst edge).** LOD0 is 1.9x in Idle and 2.8–4.9x in attacks, Hit and Block. It reaches 5.6x in Walk and 6.9x in Run. The current shipping Builder measures 3.8–13.9x on the same metric.

**99.9th percentile.** The gameplay clips stay at or under 2.62x on LOD0 and 1.91x on LOD1.

**Visual check.** No tear is visible at gameplay size in the proof renders.

**Attempts that did not help.** Explicit region rules (runs 10–12) and data-defined overrides (runs 13–14) both scored worse. Both remain in the script but are switched off (`weights_rules.enabled`, `weights_overrides.enabled`).

**Status.** Treat the model as experimental until those spots are re-weighted by hand.
