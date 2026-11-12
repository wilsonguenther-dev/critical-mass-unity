"""Re-import every Kaiju FBX headless and check what Unity will receive.

    /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python-exit-code 1 \
        --python "Assets/_Game/Art/Kaiju/_source~/check_kaiju.py"

Per stage: objects, bone count and names, material slots (names and order = kaiju_palette.SLOTS), triangle count,
every vertex skinned, height and footprint after the round trip, clips (takes) and their lengths, and a geometry
digest (rounded vertex positions) so two clean builds can be compared. Exits 1 on any failed expectation.
"""
import hashlib
import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import kaiju_palette as PAL  # noqa: E402

ART = os.path.dirname(HERE)
CREATURE_CLIPS = {"Idle", "Walk", "Roar", "SlamWindup", "Slam", "Berserk", "KO"}
EXPECT = {
    "Egg": {"bones": 2, "clips": {"Idle", "Shake"}, "height": 1.7, "tri_max": 4000},
    "Hatchling": {"bones": 20, "clips": CREATURE_CLIPS, "height": 2.235, "tri_max": 10000},
    "Adult": {"bones": 20, "clips": CREATURE_CLIPS, "height": 5.0, "tri_max": 15000},
}
fails = []


def check(cond, msg):
    print(("  ok   " if cond else "  FAIL ") + msg)
    if not cond:
        fails.append(msg)


for stage, exp in EXPECT.items():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    path = os.path.join(ART, "Kaiju_%s.fbx" % stage)
    print("== %s  %s  %.1f KB" % (stage, os.path.relpath(path, ART), os.path.getsize(path) / 1024))
    before = set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=path, use_anim=True)
    objs = {o.name: o.type for o in bpy.data.objects}
    print("  objects:", objs)
    arms = [o for o in bpy.data.objects if o.type == "ARMATURE"]
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    check(len(arms) == 1 and arms[0].name == "KaijuRig", "one armature named KaijuRig")
    check(len(meshes) == 1 and meshes[0].name == "KaijuMesh", "one mesh named KaijuMesh")
    arm, me_ob = arms[0], meshes[0]
    bones = [b.name for b in arm.data.bones]
    check(len(bones) == exp["bones"], "bones %d (expect %d): %s" % (len(bones), exp["bones"], " ".join(bones)))
    slots = [s.material.name if s.material else None for s in me_ob.material_slots]
    check(slots == [PAL.material_name(s) for s in PAL.SLOTS], "material slots: %s" % " ".join(map(str, slots)))
    me = me_ob.data
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    check(tris <= exp["tri_max"], "triangles %d (cap %d)" % (tris, exp["tri_max"]))
    unskinned = sum(1 for v in me.vertices if not any(g.weight > 0 for g in v.groups))
    check(unskinned == 0, "every vertex skinned (%d unskinned of %d)" % (unskinned, len(me.vertices)))
    used = sorted({p.material_index for p in me.polygons})
    print("  slots used by faces:", " ".join(PAL.SLOTS[i] for i in used))
    mods = [m.type for m in me_ob.modifiers]
    check("ARMATURE" in mods or me_ob.parent == arm, "mesh bound to the rig (modifiers %s, parent %s)" % (mods, me_ob.parent and me_ob.parent.name))
    ws = [me_ob.matrix_world @ v.co for v in me.vertices]
    zs = [w.z for w in ws]
    h = max(zs) - min(zs)
    check(abs(h - exp["height"]) < 0.02 and abs(min(zs)) < 0.02,
          "height %.3f m, lowest z %.3f (expect %.3f, 0)" % (h, min(zs), exp["height"]))
    xs, ys = [w.x for w in ws], [w.y for w in ws]
    print("  footprint x %.2f..%.2f  y %.2f..%.2f (Blender axes after round trip; front = -Y)" % (min(xs), max(xs), min(ys), max(ys)))
    acts = [a for a in bpy.data.actions if a not in before]
    names = {a.name.split("|")[-1] for a in acts}
    check(names == exp["clips"], "clips: %s" % ", ".join("%s [%d-%d]" % (a.name, *a.frame_range) for a in acts))
    # the takes really drive the bones after the round trip (the bone's local keyed rotation at a probe frame)
    probes = [("Idle", 0.5, "Shell", 0.5)] if stage == "Egg" else [("Roar", 0.5, "Jaw", 25.0), ("SlamWindup", 1.0, "Arm_R", 110.0),
                                                                  ("KO", 1.0, "Root", 60.0), ("Walk", 0.25, "Arm_L", 10.0)]
    ad = arm.animation_data or arm.animation_data_create()
    for clip, phase, bone, min_deg in probes:
        act = next(a for a in acts if a.name.split("|")[-1] == clip)
        ad.action = act
        if hasattr(ad, "action_slot") and ad.action_slot is None and len(getattr(act, "slots", [])):
            ad.action_slot = act.slots[0]
        f0, f1 = act.frame_range
        bpy.context.scene.frame_set(int(round(f0 + phase * (f1 - f0))))
        pb = arm.pose.bones[bone]
        deg = pb.matrix_basis.to_quaternion().angle * 57.2958   # the bone's own keyed turn, parents excluded
        check(deg >= min_deg, "%s at %.2f turns %s by %.1f deg (expect >= %.1f)" % (clip, phase, bone, deg, min_deg))
    ad.action = None
    dig = hashlib.sha1()
    for v in sorted((round(w.x, 4), round(w.y, 4), round(w.z, 4)) for w in ws):
        dig.update(("%.4f,%.4f,%.4f;" % v).encode())
    print("  geometry digest", dig.hexdigest()[:16], "verts", len(me.vertices))

print("RESULT", "PASS" if not fails else "FAIL (%d)" % len(fails))
sys.exit(1 if fails else 0)
