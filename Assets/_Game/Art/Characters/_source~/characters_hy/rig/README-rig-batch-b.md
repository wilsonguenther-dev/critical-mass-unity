# Rig batch B: a generated body into the game (method proven on the Builder, 2026-11-14)

All Blender work runs on GitHub (`tools/ci/offload.sh blender`, copy it from origin/main if your branch lacks it). One launch does everything:

```
tools/ci/offload.sh blender --ref <branch> \
  --script Assets/_Game/Art/Characters/_source~/characters_hy/rig/rigb_run.py \
  --args '{out} {repo} {inputs} <Class> [steps] [fingers]' \
  --inputs ~/Desktop/CM-3D-outputs/<class>/<class>-body-three-quarter.glb --out ~/cm-rigb-out/<name>
```
steps: comma list of `clean,rig,bake` (default all). fingers: `0` = whole-hand weights (accepted today).

## Steps (what rigb_run.py runs)
1. **Clean** (`Assets/_Game/Art/_gen2~/tools/clean_export.py` with `rigb_manifest.json`). Use the FULL-RESOLUTION glb, not the
   200k starter file.
   - Weld, drop floaters, scale to the class height, origin at the feet, facing Blender -Y.
   - `octree: 9`: an octree (SHARP) remesh wraps every surface, open sheets and interior sheets included, in one closed
     skin (about 5.5 mm cells, about 2.6 M triangles).
   - `voxel_after: 0.004`: a 4 mm voxel rebuild of that CLOSED skin makes it a clean 2-manifold. A voxel rebuild of the raw
     mesh shreds it, because the raw sheets are open.
   - Collapse to `lods` (Hero 22000, LOD0 4000, LOD1 1400, Shadow 750). When the collapse stops (about 3.8 k), vertices are
     clustered on a growing grid until the budget is met.
   - Per-face colour comes from the raw mesh (nearest point), quantised, team cloth code 8.
   - Output: `{out}/holding/<Class>/<Class>.fbx`. Commit it to `_gen2~/holding/<Class>/` and later runs skip the clean.
2. **Joint sheet.** `rig_body.py` first renders `measure_<Class>_bind_front.png` and `_side.png`: the Hero, orthographic,
   with a 0.1 m grid behind it (1000 px for 2.6 m, camera centre z 1.25 m, x 0 at the image centre, side view: image
   right is +Y, the back). Read these 22 joints by eye and write `rigb_joints_<Class>.json` (see the Builder's file):
   hips, spine, chest, head, _headtop, and per side upperleg, lowerleg, foot (ankle), toes, _toetip, upperarm, lowerarm,
   wrist, hand, handslot (grip centre). The automatic landmarks failed on the Builder (pack on the back, apron).
3. **Rig** (`rig_body.py`).
   - The skeleton is the class's current KayKit-joint model (41 bones, same names; it reads `_fallback~/<Class>.fbx` once
     replaced). Every deform bone is moved onto the joint file; its roll is carried from the KayKit bind by the smallest turn.
   - Hand slots keep KayKit's frame relative to the hand.
   - Weights: bone heat on a voxel proxy, transferred to the Hero. The head top is held rigid; a pack behind the chest is
     held on the chest. Arm weight farther than 0.075 H from the arm chain goes to the chest or the hips (rope coils,
     wings). Then smoothed, max 4 influences. The LODs take the Hero's weights.
   - Exports `{out}/<Class>.fbx` and renders `joints_<Class>_front/side.png` to check the joints.
4. **Bake** (`bake_clips.py <out> <repo> <Class> <fbx>`). The shared pack goes onto the class skeleton with foot IK
   (hip-height ratio = ModelSpec.StrideScale).
   - New bodies: the toes are rigid on the foot and count as sole. Idle stands on flat feet and upright. The weapon arm
     keeps 35 % of the swing.
   - handslot.r is held at a fixed turn from the hand that stands the game's light weapon upright at idle.
   - Renders: strips for Walk, Run, Idle, the attack and the skill (side and front), plus the gameplay views.
   - Exports `{out}/<Class>_Clips.fbx`.
5. **Into the game.** Move the old FBX to `Art/<Class>/_fallback~/`. Copy `<Class>.fbx` and `<Class>_Clips.fbx` into
   `Art/<Class>/`. Then set the ModelSpec: ClipSource `<Class>_Clips.fbx`, VertexColourFace true, no FaceTexture or
   FaceSlot, HeldWeaponInMesh false, StrideScale = the bake's `ratio`. Fix WeaponPropsTests if the class's weapon
   expectation changes. Then run `gh workflow run unity-ci.yml --ref <branch> -f jobs=tests`.
