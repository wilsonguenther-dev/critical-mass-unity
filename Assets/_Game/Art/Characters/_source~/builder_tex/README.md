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
- weights: with `weights_transfer.enabled` (OFF, see "Build v3 attempt" below), by robust skin weight transfer from the
  Warrior (below); otherwise by bone heat with region masks, edge-hop zone blends and ring smoothing (4 influences)
- exports the FBX and renders the proofs

## Weights by transfer from the Warrior (build v3)

`robust_weight_transfer.py` implements "Robust Skin Weights Transfer via Weight Inpainting" (Abdrashitov et al.,
SIGGRAPH Asia 2023), re-written from the paper and its MIT reference code (rin-23/RobustSkinWeightsTransferCode).
`build_char_remesh.py` calls it from `transfer_lod0()`:

1. Template fit. The Warrior LOD0 body is loaded without the shield and cleaver (vertices whose strongest bone is a
   hand slot). Each of its 20 deform bones is moved onto the Builder's bone as fitted to the A-pose: head on head,
   direction on direction (shortest-arc rotation), length on length. Across the bone it is scaled by the ratio of
   the two bodies' own extents (90th percentile, sideways as one value, front and back separately, clamped
   0.6–2.5). The Warrior mesh is then deformed by its own weights.
2. Match. A Builder vertex takes the Warrior weights at its closest surface point only if that point lies within
   5% of the bounding-box diagonal and its normal is within 30 degrees (the paper's defaults).
3. Inpaint. Every other vertex is solved from the matched ones with the energy tr(W^T (L + L M^-1 L) W), where
   L is the robust Laplacian (Sharp and Crane). Matched values outside the run-9 region limits are dropped first,
   and so is head weight more than 10 cm below the neck.
4. Smooth, then limit to 4 influences and normalise.
5. Clothing shells (pieces separate from the body) get a second pass from the transferred body. Pack and belt
   items stay rigid as before.
6. LOD1 carries the LOD0 weights through its collapse decimation. Shadow weights come from LOD1.

The heat weights are still computed on a copy (`BuilderTex_LOD0_heat`). They are measured next to the new weights
(`heat_LOD0` in the stretch table) and drawn in the close-up sheets. That copy is never exported.

Dependencies: scipy and robust-laplacian, installed with Blender's own Python into a local folder (never
system-wide). The folder is listed in `weights_transfer.pydeps`:

    /Applications/Blender.app/Contents/Resources/5.2/python/bin/python3.13 -m pip install --no-deps \
      --target ~/cm-art-trial/tools/pydeps libigl==2.6.3 robust-laplacian==1.0.0 scipy

(libigl is not used by the transfer. It is only there for the bounded-biharmonic fallback.)

**Other characters.** The template, its mesh prefix, the excluded prop bones, the thresholds, smoothing and the
LOD1 source are all keys of `weights_transfer` in the character's JSON. The target is the character the JSON
builds. A Healer or Demolitionist config can reuse the block unchanged.

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

**Build v3 attempt: transfer from the Warrior (switched off).** The committed FBX still uses run-9 weights.
The transfer run is recorded in `BuilderTex_report_v3_transfer.json` and `BuilderTex_stretch_v3_transfer.csv`.

- Match share was 19.6% at the paper's thresholds (15.4 cm, 30 degrees). 2,042 of the 2,604 body vertices failed
  only the normal test. The two low-poly bodies are about 3.6 cm apart at the median, but their facets disagree.
- Gameplay worst edge was LOD0 2.88x (run 9: 6.92x) and LOD1 2.37x (run 9: 2.80x). The 99.9th percentile was
  LOD0 2.11x (run 9: 2.35x) and LOD1 2.15x (run 9: 1.84x).
- Only Idle and Hit are under 2.5x on LOD0.
- The right-arm close-up shows the forearm skin and cuff staying at the armpit while the arm moves, in Run and in
  both Attack frames. The heat weights do not do this, so the transfer is not shipped.
- Head weight below the neck joint: 462 vertices on LOD0 (heat: 523). On the Warrior itself it is 53.

**Hybrid attempt (switched off).** This takes the transfer on legs, hips and torso and run 9 on the arms. Arm
membership comes from run 9's dominant bone. A 16 cm cross-fade is measured as edge-path distance. The run is
recorded in `BuilderTex_report_v3_hybrid.json`.

- The gameplay worst edge on LOD0 is at or below run 9 in every clip: Idle 1.79, Walk 2.78, Run 2.88, Attack 3.34
  (equal), AttackSlice 2.70, Hit 2.31, Block 2.60.
- LOD1's worst gameplay edge is 2.95x (run 9: 2.80x).
- The right-shoulder close-ups show a small patch of torso geometry poking through the sleeve at the border.
- Seeding the unmatched arm vertices from run 9 before the inpaint measured worse (3.91x).
- The forearm vertices were not left to the torso side: they kept 97% forearm/hand weight in the transfer, even
  though only 25% of them matched (median 7 cm and 50 degrees off).
