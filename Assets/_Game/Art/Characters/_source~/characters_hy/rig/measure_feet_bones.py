"""Reproduce the game's feet test in Blender (GameplaySanityPlayTests.ModelsStandOnTheirGroundUprightToScaleAndUnderTheirRings):
per clip and frame, the LOWEST world height of the joints toes.l, toes.r, foot.l, foot.r (joint origins, not mesh), with
the model root on the ground (z 0); the test fails when that exceeds 0.35 (float) or is under -0.2 (sink).
Reports per clip the worst (highest) per-frame minimum and the frame, for each pack given as <Class>=<model fbx>:<clip fbx>.
Usage: blender -b --python measure_feet_bones.py -- <out> <repo> Warrior=Assets/..fbx:Assets/..fbx [more]
"""
import bpy, sys, os, json
from mathutils import Vector
FACE = {}
argv = sys.argv[sys.argv.index("--") + 1:]
OUT, REPO, PACKS = argv[0], argv[1], argv[2:]
os.makedirs(OUT, exist_ok=True)
REP = {}
BONES = ("toes.l", "toes.r", "foot.l", "foot.r")


def imp(path, anim):
    bo, ba = set(bpy.data.objects), set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=os.path.join(REPO, path), use_anim=anim, use_custom_normals=False,
                             ignore_leaf_bones=False, automatic_bone_orientation=False, primary_bone_axis="Y", secondary_bone_axis="X")
    return [o for o in bpy.data.objects if o not in bo], [a for a in bpy.data.actions if a not in ba]


for spec in PACKS:
    cls, paths = spec.split("=", 1)
    model, clips = paths.split(":")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    # The clips play on the clip pack's OWN armature: Unity applies a clip's local rotations as absolute values, while a
    # Blender action is relative to the rest of the armature it is put on, so putting the pack's actions on the model's
    # armature is only right when both rests match (they do not for a baked pack: the bake turns the feet at rest).
    cobjs, acts = imp(clips, True)
    arm = next(o for o in cobjs if o.type == "ARMATURE")
    for o in cobjs:
        if o is not arm:
            bpy.data.objects.remove(o, do_unlink=True)
    ad = arm.animation_data or arm.animation_data_create()
    rest = {}
    ad.action = None
    bpy.context.view_layer.update()
    FWD = sum((((arm.matrix_world @ arm.pose.bones["toes." + q].head) - (arm.matrix_world @ arm.pose.bones["foot." + q].head)).to_2d().normalized() for q in "lr"), Vector((0, 0))).normalized()
    print("[facing]", cls, "rest forward", tuple(round(x, 3) for x in FWD), flush=True)
    rest = min((arm.matrix_world @ arm.pose.bones[b].head).z for b in BONES if b in arm.pose.bones)
    res = {"rest_min_joint_z": round(rest, 4), "clips": {}}
    for a in acts:
        ad.action = a
        try:
            if ad.action_slot is None and len(a.slots):
                ad.action_slot = a.slots[0]
        except Exception:
            pass
        f0, f1 = int(a.frame_range[0]), int(a.frame_range[1])
        worst, wf, low = -9, f0, 9
        for f in range(f0, f1 + 1):
            bpy.context.scene.frame_set(f)
            m = min((arm.matrix_world @ arm.pose.bones[b].head).z for b in BONES if b in arm.pose.bones)
            if m > worst: worst, wf = m, f
            low = min(low, m)
        if a.name.split("|")[-1] in ("Run", "Idle"):
            tr = []
            for f in range(f0, f1 + 1, 2):
                bpy.context.scene.frame_set(f)
                pb = arm.pose.bones
                W = lambda b: round((arm.matrix_world @ pb[b].head).z, 3) if b in pb else None
                tr.append((f, W("hips"), W("foot.l"), W("foot.r"), W("toes.l"), W("toes.r")))
            print("[feet]", cls, a.name.split("|")[-1], "frame, hips, foot.l, foot.r, toes.l, toes.r:", tr, flush=True)
        # The game's facing read (SanityRig.TryFront): toes minus ankle of the foot with the LOWER ankle, flattened,
        # against the model's forward (the rest ankle-to-toe heading). Frames more than 90 degrees off read "turned around".
        cn = a.name.split("|")[-1]
        if cn in ("Run", "RunBack", "Walk", "WalkBack", "StrafeL", "StrafeR"):
            import math
            ad.action = None; bpy.context.view_layer.update()
            fw = sum(((arm.matrix_world @ arm.pose.bones["toes." + q].head) - (arm.matrix_world @ arm.pose.bones["foot." + q].head)).to_2d().normalized() for q in "lr").normalized() if False else None
            ad.action = a
            bad_f, gaps = [], []
            for f in range(f0, f1 + 1):
                bpy.context.scene.frame_set(f)
                pb = arm.pose.bones
                P = lambda b: arm.matrix_world @ pb[b].head
                q = "l" if P("foot.l").z <= P("foot.r").z else "r"
                d = (P("toes." + q) - P("foot." + q)).to_2d()
                ang = math.degrees(math.atan2(d.x * FWD.y - d.y * FWD.x, d.dot(FWD))) if d.length > 1e-6 else 0.0
                gaps.append(abs(P("foot.l").z - P("foot.r").z))
                if abs(ang) > 90: bad_f.append((f, q, round(ang), round(gaps[-1], 3)))
            FACE[cls + " " + cn] = (len(bad_f), f1 - f0 + 1)
            print("[facing]", cls, cn, "turned-around frames", len(bad_f), "of", f1 - f0 + 1, bad_f[:6], flush=True)
        res["clips"][a.name.split("|")[-1]] = dict(worst_float=round(worst, 3), at_frame=wf, lowest=round(low, 3), frames=f1 - f0 + 1)
    bad = {k: v for k, v in res["clips"].items() if v["worst_float"] > 0.35 or v["lowest"] < -0.2}
    res["fails_game_limits"] = bad
    print("[feet]", cls, "rest", res["rest_min_joint_z"], "FAILS:", json.dumps(bad), flush=True)
    print("[feet]", cls, "all:", json.dumps({k: v["worst_float"] for k, v in res["clips"].items()}), flush=True)
    REP[cls] = res
json.dump(REP, open(os.path.join(OUT, "feet_bones.json"), "w"), indent=1)
