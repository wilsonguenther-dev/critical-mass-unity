"""Reproduce the game's feet test in Blender (GameplaySanityPlayTests.ModelsStandOnTheirGroundUprightToScaleAndUnderTheirRings):
per clip and frame, the LOWEST world height of the joints toes.l, toes.r, foot.l, foot.r (joint origins, not mesh), with
the model root on the ground (z 0); the test fails when that exceeds 0.35 (float) or is under -0.2 (sink).
Reports per clip the worst (highest) per-frame minimum and the frame, for each pack given as <Class>=<model fbx>:<clip fbx>.
Usage: blender -b --python measure_feet_bones.py -- <out> <repo> Warrior=Assets/..fbx:Assets/..fbx [more]
"""
import bpy, sys, os, json
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
    mobjs, _ = imp(model, False)
    arm = next(o for o in mobjs if o.type == "ARMATURE")
    cobjs, acts = imp(clips, True)
    for o in cobjs:
        bpy.data.objects.remove(o, do_unlink=True)
    ad = arm.animation_data or arm.animation_data_create()
    rest = {}
    ad.action = None
    bpy.context.view_layer.update()
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
        res["clips"][a.name.split("|")[-1]] = dict(worst_float=round(worst, 3), at_frame=wf, lowest=round(low, 3), frames=f1 - f0 + 1)
    bad = {k: v for k, v in res["clips"].items() if v["worst_float"] > 0.35 or v["lowest"] < -0.2}
    res["fails_game_limits"] = bad
    print("[feet]", cls, "rest", res["rest_min_joint_z"], "FAILS:", json.dumps(bad), flush=True)
    print("[feet]", cls, "all:", json.dumps({k: v["worst_float"] for k, v in res["clips"].items()}), flush=True)
    REP[cls] = res
json.dump(REP, open(os.path.join(OUT, "feet_bones.json"), "w"), indent=1)
