"""Re-import ../Farmer.fbx into a fresh headless Blender and check it (adapted from the Warrior's verify_warrior.py). Exit code 1 on any failed check.

  Blender -b --factory-startup --python-exit-code 1 -P verify_farmer.py

Checks: FBX header axes/scale; objects; 41 joints with KayKit names (vs rig_kaykit41.json); material slots; triangle
budget; loose / non-manifold geometry; skin weights (<= 4 influences, every vertex weighted); team-mask codes in the
vertex-colour alpha; the Farmer bible points (staff, globe tank + leaves, bun, goggles, team trim, fixed biomass glow)
as named objects or vertex groups in farmer.blend; and a clip carry-over probe: the KayKit "Idle" action, applied by bone name
to the re-imported FBX rig, must put the hands and head where it puts them on the original KayKit rig.
"""
import json
import os
import re
import math
import struct
import sys

sys.dont_write_bytecode = True
import bmesh  # noqa: E402
import bpy  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
SHARED = os.path.normpath(os.path.join(HERE, "..", "..", "Characters", "_source~"))
FBX = os.path.join(os.path.dirname(HERE), "Farmer.fbx")
BLEND = os.path.join(HERE, "farmer.blend")
GLB = os.path.join(SHARED, "kaykit", "Knight.glb")
TRI_BUDGET = 12000
EXPECTED = {"Farmer_Body", "Farmer_Staff"}
results, failed = [], []


def check(name, ok, detail):
    results.append({"check": name, "ok": bool(ok), "detail": detail})
    (failed.append(name) if not ok else None)
    print(("PASS " if ok else "FAIL ") + name + ": " + str(detail))


def fbx_prop(data, key):
    """Value of a binary FBX P-record: "key" then three strings (type, label, flags) then one typed value."""
    k = key.encode()
    i = data.find(struct.pack("<I", len(k)) + k)
    if i < 0:
        return None
    p = i + 4 + len(k)
    for _ in range(3):
        if data[p:p + 1] != b"S":
            return None
        n = struct.unpack("<I", data[p + 1:p + 5])[0]
        p += 5 + n
    t = data[p:p + 1]
    if t == b"I":
        return struct.unpack("<i", data[p + 1:p + 5])[0]
    if t == b"D":
        return struct.unpack("<d", data[p + 1:p + 9])[0]
    return None


def geo_stats(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    loose_v = sum(1 for v in bm.verts if not v.link_edges)
    loose_e = sum(1 for e in bm.edges if not e.link_faces)
    nonman = sum(1 for e in bm.edges if len(e.link_faces) != 2)
    tris = sum(len(f.verts) - 2 for f in bm.faces)
    shells = 0
    seen = set()
    for v in bm.verts:
        if v.index in seen:
            continue
        shells += 1
        stack = [v]
        while stack:
            x = stack.pop()
            if x.index in seen:
                continue
            seen.add(x.index)
            stack.extend(e.other_vert(x) for e in x.link_edges)
    bm.free()
    return tris, loose_v, loose_e, nonman, shells


# ---------------------------------------------------------------- header
data = open(FBX, "rb").read()
hdr = {k: fbx_prop(data, k) for k in ("UpAxis", "UpAxisSign", "FrontAxis", "FrontAxisSign", "CoordAxis", "CoordAxisSign",
                                     "UnitScaleFactor")}
check("fbx header axes (Y up, Z front, X coord)", hdr["UpAxis"] == 1 and hdr["UpAxisSign"] == 1 and hdr["FrontAxis"] == 2
      and hdr["CoordAxis"] == 0, hdr)
check("fbx unit scale", hdr["UnitScaleFactor"] in (1.0, 100.0),
      f"UnitScaleFactor={hdr['UnitScaleFactor']}; with FBX_SCALE_ALL the vertex data is already in metres x this "
      "factor's inverse, so Unity 'Convert Units' yields scale 1 (UNVERIFIED in Unity)")
check("fbx has no baked animation", data.find(b"AnimationStack") < 0 or data.count(b"AnimationCurveNode") == 0,
      f"AnimationCurveNode records: {data.count(b'AnimationCurveNode')}")

# ---------------------------------------------------------------- re-import
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
objs = {o.name: o for o in bpy.data.objects}
arms = [o for o in objs.values() if o.type == "ARMATURE"]
meshes = {o.name: o for o in objs.values() if o.type == "MESH"}
check("objects", set(meshes) == EXPECTED and len(arms) == 1,
      {"armature": [a.name for a in arms], "meshes": sorted(meshes)})
arm = arms[0]
ref = json.load(open(os.path.join(SHARED, "rig_kaykit41.json")))
ref_names = [j["name"] for j in ref["joints"]]
names = [b.name for b in arm.data.bones]
check("bone count 41", len(names) == 41, len(names))
check("bone names = KayKit rig", sorted(names) == sorted(ref_names),
      {"missing": sorted(set(ref_names) - set(names)), "extra": sorted(set(names) - set(ref_names))})
parents_ok = all((arm.data.bones[j["name"]].parent.name if arm.data.bones[j["name"]].parent else None) == j["parent"]
                 for j in ref["joints"] if j["name"] in arm.data.bones)
check("bone hierarchy = KayKit rig", parents_ok, "every joint has its KayKit parent")
maxd = 0.0
for j in ref["joints"]:
    b = arm.data.bones.get(j["name"])
    if b:
        h = arm.matrix_world @ b.head_local
        maxd = max(maxd, max(abs(h[i] - j["head"][i]) for i in range(3)))
check("joint rest positions = KayKit (metres, after the axis round trip)", maxd < 1e-3, f"max error {maxd:.6f} m")
check("armature root transform clean", all(abs(v) < 1e-4 for v in arm.rotation_euler) and
      all(abs(s - 1) < 1e-4 for s in arm.scale), {"rot": list(arm.rotation_euler), "scale": list(arm.scale)})

total = 0
for name, ob in sorted(meshes.items()):
    tris, lv, le, nm, shells = geo_stats(ob)
    total += tris
    mats = [m.name for m in ob.data.materials if m]
    check(f"{name} material slots", mats == ["CM_Toon"], mats)
    check(f"{name} loose/non-manifold", lv == 0 and le == 0 and nm == 0,
          {"triangles": tris, "loose_verts": lv, "loose_edges": le, "non_manifold_edges": nm, "closed_shells": shells})
    mod = [m for m in ob.modifiers if m.type == "ARMATURE"]
    check(f"{name} skinned to the rig", mod and mod[0].object == arm, [m.name for m in mod])
    bone_groups = {g.index: g.name for g in ob.vertex_groups if g.name in arm.data.bones}
    unweighted = over4 = 0
    for v in ob.data.vertices:
        ws = [g for g in v.groups if g.group in bone_groups and g.weight > 1e-4]
        unweighted += not ws
        over4 += len(ws) > 4
    check(f"{name} weights", unweighted == 0 and over4 == 0,
          {"bones_used": len(bone_groups), "unweighted_verts": unweighted, "verts_over_4_influences": over4})
    ca = ob.data.color_attributes
    codes = set()
    if len(ca):
        for d in ca[0].data:
            codes.add(round(d.color[3], 2))
    check(f"{name} vertex colours + team mask", len(ca) >= 1, {"layers": [c.name for c in ca],
                                                                "mask_codes": sorted(codes)})
    if name in ("Farmer_Body", "Farmer_Staff"):
        check(f"{name} team trim (1.0) + fixed biomass glow (0.25) codes", {1.0, 0.25} <= codes, sorted(codes))
check(f"triangle budget <= {TRI_BUDGET}", total <= TRI_BUDGET, total)
body = meshes["Farmer_Body"]
zs = [(body.matrix_world @ v.co).z for v in body.data.vertices]
check("body height in metres after round trip (Blender Z = Unity Y)", 1.97 < max(zs) - min(zs) < 2.03 and abs(min(zs)) < 0.02,
      {"min_z": round(min(zs), 3), "max_z": round(max(zs), 3),
       "mesh_object_rot": [round(a, 4) for a in body.rotation_euler], "mesh_object_scale": list(body.scale)})

# ---------------------------------------------------------------- bone rest frames (informational)
# The KayKit clips are keyed in each bone's local rest frame. Joint POSITIONS survive the round trip (checked above);
# whether the joint ORIENTATIONS do decides if clips imported from the GLB play on this FBX as Generic clips.
fbx_frames = {b.name: b.matrix_local.to_quaternion() for b in arm.data.bones}
bpy.ops.wm.open_mainfile(filepath=BLEND)
src = bpy.data.objects["Rig"]
def _deg(q):
    a = math.degrees(q.angle) % 360.0
    return round(min(a, 360.0 - a), 3)       # q and -q are the same rotation


angles = {b.name: _deg(fbx_frames[b.name].rotation_difference(b.matrix_local.to_quaternion())) for b in src.data.bones}
worst = max(angles.values())
results.append({"check": "bone rest orientations FBX re-import vs farmer.blend (informational)", "ok": worst < 0.5,
                "detail": {"max_deg": worst, "bones_over_0.5_deg": sorted(k for k, v in angles.items() if v >= 0.5)}})
print(("INFO " if worst >= 0.5 else "PASS ") + f"bone rest orientations FBX vs .blend: max {worst} deg; "
      f"{sum(v >= 0.5 for v in angles.values())} of 41 bones differ by >= 0.5 deg")

# ---------------------------------------------------------------- clip carry-over probe (Blender side)
bpy.ops.import_scene.fbx(filepath=FBX)
fbx_arm = [o for o in bpy.data.objects if o.type == "ARMATURE" and o is not src][0]
act = bpy.data.actions["Idle"]
for a in (src, fbx_arm):
    a.animation_data_create()
    a.animation_data.action = act
    if getattr(act, "slots", None) and len(act.slots):
        a.animation_data.action_slot = act.slots[0]
bpy.context.scene.frame_set(1)
bpy.context.view_layer.update()
offs = {bn: round(((fbx_arm.matrix_world @ fbx_arm.pose.bones[bn].head) - (src.matrix_world @ src.pose.bones[bn].head)).length, 4)
        for bn in ("hand.l", "hand.r", "head", "foot.l", "foot.r", "handslot.l", "handslot.r")}
moved = (src.pose.bones["hand.l"].head - src.data.bones["hand.l"].head_local).length
check("KayKit 'Idle' on the re-imported FBX rig matches the source rig (Blender, by bone name)",
      max(offs.values()) < 0.01 and moved > 0.1, {"offsets_m": offs, "hand.l moved from rest by": round(moved, 3)})
for ob in [o for o in bpy.data.objects if o.parent is fbx_arm]:
    bpy.data.objects.remove(ob, do_unlink=True)
bpy.data.objects.remove(fbx_arm, do_unlink=True)

# ---------------------------------------------------------------- the Farmer bible points in farmer.blend
groups = {o.name: {g.name for g in o.vertex_groups} for o in bpy.data.objects if o.type == "MESH"}
body_g, staff_g = groups.get("Farmer_Body", set()), groups.get("Farmer_Staff", set())
check("bible point: hooked staff (object Farmer_Staff, groups feat_staff + feat_hook + feat_bulb)",
      {"feat_staff", "feat_hook", "feat_bulb"} <= staff_g, sorted(staff_g))
check("bible point: globe tank with leaves and hose (feat_tank, feat_leaves, feat_hose)",
      {"feat_tank", "feat_leaves", "feat_hose"} <= body_g, "Farmer_Body")
check("bible point: high messy bun + goggles (feat_bun, feat_goggles, feat_hair)",
      {"feat_bun", "feat_goggles", "feat_hair"} <= body_g, "Farmer_Body")
check("bible point: overalls, jacket, gloves, boots, belt, root-seed pod",
      {"feat_overalls", "feat_jacket", "feat_gloves", "feat_boots", "feat_belt", "feat_seed_pod"} <= body_g, "Farmer_Body")
check("bible point: team trim only (feat_team_cloth on body and staff, no team glow group)",
      "feat_team_cloth" in body_g and "feat_team_cloth" in staff_g and "feat_team_glow" not in body_g, "body+staff")
mat = bpy.data.materials["CM_Toon"]
glossy = [n.bl_idname for n in mat.node_tree.nodes if n.bl_idname in
          ("ShaderNodeBsdfPrincipled", "ShaderNodeBsdfGlossy", "ShaderNodeBsdfMetallic", "ShaderNodeBsdfAnisotropic")]
check("flat shading: no glossy/metal nodes in CM_Toon", not glossy, glossy or "Emission of N.L-banded vertex colour only")

out = os.path.join(HERE, "verify_report.json")
with open(out, "w") as f:
    json.dump({"failed": failed, "results": results}, f, indent=1)
    f.write("\n")
print(f"VERIFY {'OK' if not failed else 'FAILED'} {len(results) - len(failed)}/{len(results)} -> {out}")
if failed:
    sys.exit(1)
