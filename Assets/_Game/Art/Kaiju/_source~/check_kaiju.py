"""Re-import every Kaiju FBX headless and check what Unity will receive.

    /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python-exit-code 1 \
        --python "Assets/_Game/Art/Kaiju/_source~/check_kaiju.py"

Per stage: objects, bone count and names, ONE material named CM_Toon, the vertex colour layer "Col" with only the
mask codes 0 (fixed) and 0.75 (team glow) in alpha, triangle count, every vertex skinned, height and footprint after
the round trip, clips (takes) and their lengths, and a geometry digest (rounded vertex positions) so two clean builds
can be compared. Then the two props (berserk shards, telegraph spikes) and the adult anatomy checks of the brief
(shoulders vs hips, forelimbs vs hind legs, head low and forward, hatchling / adult height). Exits 1 on any failure.
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


def check_colours(me, stage):
    ca = me.color_attributes.get("Col") or (me.color_attributes[0] if len(me.color_attributes) else None)
    check(ca is not None, "vertex colour layer present (%s)" % (ca.name if ca else None))
    if ca is None:
        return
    alphas = {}
    for d in ca.data:
        a = round(d.color[3], 2)
        alphas[a] = alphas.get(a, 0) + 1
    tot = sum(alphas.values())
    check(set(alphas) <= {0.0, 0.75}, "alpha mask codes %s" % {k: round(v / tot, 3) for k, v in sorted(alphas.items())})


ANAT = {}
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
    check(len(slots) == 1 and slots[0].startswith(PAL.MATERIAL), "one material CM_Toon: %s" % " ".join(map(str, slots)))
    me = me_ob.data
    check_colours(me, stage)
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
    if stage in ("Adult", "Hatchling"):
        B = {b.name: (arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local) for b in arm.data.bones}

        def width_at(z0, y0, dz=0.3, dy=0.7):
            xs_ = [w.x for w in ws if abs(w.z - z0) < dz and abs(w.y - y0) < dy]
            return max(xs_) - min(xs_) if xs_ else 0.0
        sh_h, hp_h = B["Arm_L"][0], B["Thigh_L"][0]
        fore = sum((B[n][1] - B[n][0]).length for n in ("Arm_L", "Forearm_L", "Fist_L"))
        hind = sum((B[n][1] - B[n][0]).length for n in ("Thigh_L", "Foot_L"))
        ANAT[stage] = {"height": h, "shoulder_w": width_at(sh_h.z, sh_h.y), "hip_w": width_at(hp_h.z, hp_h.y),
                       "fore": fore, "hind": hind, "head_z": B["Head"][0].z, "shoulder_z": sh_h.z,
                       "head_y": B["Head"][0].y, "chest_y": B["Chest"][0].y, "fist_y": B["Fist_L"][1].y}
    dig = hashlib.sha1()
    for v in sorted((round(w.x, 4), round(w.y, 4), round(w.z, 4)) for w in ws):
        dig.update(("%.4f,%.4f,%.4f;" % v).encode())
    print("  geometry digest", dig.hexdigest()[:16], "verts", len(me.vertices))

for prop, tri_max in (("Kaiju_BerserkShards", 1000), ("Kaiju_TelegraphSpikes", 1000)):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    path = os.path.join(ART, prop + ".fbx")
    print("== %s  %.1f KB" % (prop, os.path.getsize(path) / 1024))
    bpy.ops.import_scene.fbx(filepath=path)
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    check(len(meshes) == 1 and meshes[0].name == prop, "one mesh named %s" % prop)
    me = meshes[0].data
    slots = [s.material.name for s in meshes[0].material_slots if s.material]
    check(len(slots) == 1 and slots[0].startswith(PAL.MATERIAL), "one material CM_Toon: %s" % slots)
    check_colours(me, prop)
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    check(tris <= tri_max, "triangles %d (cap %d)" % (tris, tri_max))

print("== anatomy (adult, after the round trip)")
A = ANAT["Adult"]
r = A["shoulder_w"] / max(1e-6, A["hip_w"])
check(1.3 <= r <= 1.8, "shoulder width %.2f m / hip width %.2f m = %.2f (brief: about 1.5)" % (A["shoulder_w"], A["hip_w"], r))
check(A["fore"] > A["hind"] * 1.3, "forelimb chain %.2f m vs hind leg %.2f m (= %.2fx)" % (A["fore"], A["hind"], A["fore"] / A["hind"]))
check(A["head_z"] < A["shoulder_z"] + 0.25 * A["height"] and A["head_y"] < A["chest_y"],
      "head low and forward: head joint z %.2f (shoulder joint %.2f, height %.2f), head y %.2f ahead of chest y %.2f"
      % (A["head_z"], A["shoulder_z"], A["height"], A["head_y"], A["chest_y"]))
hr = ANAT["Hatchling"]["height"] / A["height"]
check(abs(hr - 0.447) < 0.005, "hatchling / adult height %.3f (brief 0.447)" % hr)
# v2 painted face decal (ART-DIRECTION 3.4): one 256 x 256 atlas next to the FBX, a UVMap in each creature FBX
_face = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Kaiju_Face.png")
_ok = os.path.isfile(_face)
if _ok:
    _im = bpy.data.images.load(_face)
    _ok = tuple(_im.size) == (256, 256)
check(_ok, "face decal Kaiju_Face.png next to the FBX, 256 x 256")
for _st in ("Adult", "Hatchling"):
    _fbx = os.path.join(os.path.dirname(_face), "Kaiju_%s.fbx" % _st)
    check(os.path.isfile(_fbx) and b"UVMap" in open(_fbx, "rb").read(), "UVMap for the face decal in Kaiju_%s.fbx" % _st)
print("RESULT", "PASS" if not fails else "FAIL (%d)" % len(fails))
sys.exit(1 if fails else 0)
