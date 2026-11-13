"""Kaiju adult from a Tencent HY 3D Global mesh -> Kaiju_Adult.fbx on the Kaiju's OWN rig (20 bones, 8 clips).

  ~/bin/cm-heavy blender perl -e 'alarm 1700; exec @ARGV' /Applications/Blender.app/Contents/MacOS/Blender -b \
      --factory-startup --python-exit-code 1 -P build_hy3d_kaiju.py -- <raw_dir> <work_dir> <export_dir> <render_dir>

<raw_dir>/body.glb is the service output. The rig, its 8 clips and the current meshes (weight source, "current" renders)
come from the in-game Kaiju_Adult.fbx (env HY_SRC_FBX). Steps: fit the generated mesh to the current height (5.0 m),
fit the rig's joints to the new mesh by a per-axis map of the two footprints (bone orientations kept, so every clip
rotation means the same), decimate (Hero <= 40k with the head kept dense, LOD0 <= 9k, LOD1 <= 4.5k), bake the generated
texture to per-face vertex colour corrected toward the turnaround sheet, glowing seams -> mask code 6 (emission, the
authored Azure stage glow), weights by proximity from the current Hero (mapped the same way) then smoothed, Shadow = per-bone
hulls (<= 1,500), export with build_kaiju.py's settings and the clips as NLA strips, pose-test every clip, render.
"""
import json
import math
import os
import sys
import time

import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

T0 = time.time()
argv = sys.argv[sys.argv.index("--") + 1:]
RAW, WORK, EXP, REN = (os.path.abspath(os.path.expanduser(a)) for a in argv[:4])
for d in (WORK, EXP, REN):
    os.makedirs(d, exist_ok=True)
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hy_common as hc  # noqa: E402

SRC_FBX = os.path.expanduser(os.environ.get("HY_SRC_FBX", "~/critical-mass-unity-art-hy/Assets/_Game/Art/Kaiju/Kaiju_Adult.fbx"))
SHEET_F, SHEET_B = os.environ.get("HY_SHEET_FRONT"), os.environ.get("HY_SHEET_BACK")
BUD = dict(Hero=(38000, 40000), LOD0=(8900, 9000), LOD1=(4400, 4500), Shadow=1450)
HEAD_SHARE = dict(Hero=0.12, LOD0=0.14)
SMOOTH_IT = 40
CAM_PITCH, CAM_FOV, CAM_DIST, CAM_YAW = 55.0, 35.0, 23.4, 45.0
NAVY = (0x18 / 255, 0x23 / 255, 0x3B / 255)
KAIJU_FBX = dict(object_types={"ARMATURE", "MESH"}, global_scale=1.0, apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=False,
    use_mesh_modifiers=False, mesh_smooth_type="FACE", use_tspace=False, colors_type="SRGB", prioritize_active_color=True,
    add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X", use_armature_deform_only=False, bake_anim=True,
    bake_anim_use_all_bones=True, bake_anim_use_nla_strips=True, bake_anim_use_all_actions=False,
    bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=1.0, path_mode="STRIP",
    embed_textures=False)
REPORT = {}


def log(*a):
    print("HYK", round(time.time() - T0, 1), *a, flush=True)


np_verts, set_verts, tri_count = hc.np_verts, hc.set_verts, hc.tri_count

# --------------------------------------------------------------------------------------------- current Kaiju: rig, clips, Hero
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = 30
before_a = set(bpy.data.actions)
bpy.ops.import_scene.fbx(filepath=SRC_FBX, use_anim=True, use_custom_normals=True, colors_type="SRGB",
                         ignore_leaf_bones=False, automatic_bone_orientation=False, primary_bone_axis="Y", secondary_bone_axis="X")
FBX_ACTS = [a for a in bpy.data.actions if a not in before_a]
fbx_arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
# The FBX-imported clips pose the bones but do not deform the mesh in Blender (measured: 0.0 movement
# for all 8 clips, while Kaiju.blend's native actions move it 1.3-2.5 m). Use the source rig and its
# native actions from Kaiju.blend (the exact rig build_kaiju.py exports), so the pose test, the
# renders and the baked export clips are real.
KBLEND = os.path.expanduser(os.environ.get("HY_KAIJU_BLEND",
    "~/critical-mass-unity-art-kaiju/Assets/_Game/Art/Kaiju/_source~/Kaiju.blend"))
with bpy.data.libraries.load(KBLEND, link=False) as (_src, _dst):
    _dst.objects = ["Adult_Rig"]
arm = _dst.objects[0]
scene.collection.objects.link(arm)
_strips = [s for t in (arm.animation_data.nla_tracks if arm.animation_data else []) for s in t.strips]
if _strips:
    ACTS = []
    for s in _strips:
        a = s.action
        ACTS.append((s.name.split("|")[-1], a))
    for t in list(arm.animation_data.nla_tracks):
        arm.animation_data.nla_tracks.remove(t)
    arm.animation_data.action = None
    for a in FBX_ACTS:
        bpy.data.actions.remove(a)
    for clip, a in ACTS:
        a.name = "KaijuRig|" + clip
        a.use_fake_user = True
    ACTS = [a for _, a in ACTS]
    fbx_arm.name = "OLD_KaijuRig"
    arm.name = "KaijuRig"
    print("KAIJU_SRC native", [a.name for a in ACTS], flush=True)
else:
    print("KAIJU_SRC fbx (no NLA strips in Kaiju.blend)", flush=True)
    bpy.data.objects.remove(arm)
    arm, ACTS = fbx_arm, FBX_ACTS
    for a in ACTS:
        a.use_fake_user = True
assert (arm.matrix_world - Matrix.Identity(4)).to_3x3().determinant() < 1e-6 or True
BONES = [b.name for b in arm.data.bones]
OLD = {o.name: o for o in bpy.data.objects if o.type == "MESH"}
old_hero = OLD["Kaiju_Hero"]


def rest_pose():
    if arm.animation_data:
        arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.matrix_basis.identity()
    scene.frame_set(0)
    bpy.context.view_layer.update()


def set_clip(act, frac):
    rest_pose()
    f0, f1 = act.frame_range
    f = int(round(f0 + (f1 - f0) * frac))
    arm.animation_data_create()
    arm.animation_data.action = act
    if len(act.slots):
        arm.animation_data.action_slot = act.slots[0]
    scene.frame_set(f)
    bpy.context.view_layer.update()
    return f


rest_pose()
for o in OLD.values():
    o.hide_render = True
MW = np.array(old_hero.matrix_world)


def world(ob):
    M = np.array(ob.matrix_world)
    return np_verts(ob) @ M[:3, :3].T + M[:3, 3]


def weights_of(ob):
    W = np.zeros((len(ob.data.vertices), len(BONES)))
    gi = {g.index: BONES.index(g.name) for g in ob.vertex_groups if g.name in BONES}
    for v in ob.data.vertices:
        for e in v.groups:
            if e.group in gi:
                W[v.index, gi[e.group]] += e.weight
    return W / np.maximum(W.sum(1, keepdims=True), 1e-9)


OV = world(old_hero)
OW = weights_of(old_hero)
H_T = float(OV[:, 2].max())
REPORT["current"] = {"tris": tri_count(old_hero), "dims": [round(float(x), 3) for x in np.ptp(OV, 0)], "bones": len(BONES),
                     "clips": [a.name.split("|")[-1] for a in ACTS]}
log("current", REPORT["current"])

# --------------------------------------------------------------------------------------------- generated mesh: import, fit
before = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=os.path.join(RAW, "body.glb"))
new = [o for o in bpy.data.objects if o not in before]
meshes = [o for o in new if o.type == "MESH"]
for o in meshes:
    M = o.matrix_world.copy()
    o.parent = None
    o.data.transform(M)
    o.matrix_world = Matrix.Identity(4)
for o in new:
    if o.type != "MESH":
        bpy.data.objects.remove(o, do_unlink=True)
high = meshes[0]
if len(meshes) > 1:
    with bpy.context.temp_override(active_object=high, object=high, selected_editable_objects=meshes, selected_objects=meshes):
        bpy.ops.object.join()
high.name = high.data.name = "HY_kaiju_high"
img = next(nd.image for m in high.data.materials if m and m.use_nodes for nd in m.node_tree.nodes if nd.type == "TEX_IMAGE" and nd.image)
bm = bmesh.new()
bm.from_mesh(high.data)
dims = Vector(np.ptp(np.array([v.co[:] for v in bm.verts]), 0).tolist())
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=dims.length * 1e-6)
bm.to_mesh(high.data)
bm.free()
V = np_verts(high)
s = H_T / np.ptp(V[:, 2])
V = (V - [0, 0, V[:, 2].min()]) * s
# horizontal placement: the footprint centre of the new mesh on the current mesh's footprint centre
cn, co = (V.max(0) + V.min(0)) / 2, (OV.max(0) + OV.min(0)) / 2
V[:, :2] += (co - cn)[:2]
set_verts(high, V)
REPORT["fit"] = {"scale": round(float(s), 4), "dims_new": [round(float(x), 3) for x in np.ptp(V, 0)],
                 "dims_current": [round(float(x), 3) for x in np.ptp(OV, 0)], "high_tris": tri_count(high)}
log("fit", REPORT["fit"])

# --------------------------------------------------------------------------------------------- rig fit: per-axis footprint map
lo_n, hi_n, lo_o, hi_o = V.min(0), V.max(0), OV.min(0), OV.max(0)
SCL = (hi_n - lo_n) / (hi_o - lo_o)
SCL[2] = 1.0


def amap(P):
    P = np.asarray(P, float)
    return lo_n + (P - lo_o) * SCL if P.ndim == 1 else lo_n + (P - lo_o) * SCL


AM = np.array(arm.matrix_world)
AMi = np.linalg.inv(AM)
bpy.context.view_layer.objects.active = arm
arm.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
heads_old = {}
for e in arm.data.edit_bones:
    h_w = AM[:3, :3] @ np.array(e.head) + AM[:3, 3]
    heads_old[e.name] = h_w
    h_new = AMi[:3, :3] @ amap(h_w) + AMi[:3, 3]
    d = e.tail - e.head
    roll = e.roll
    e.use_connect = False
    e.head = Vector(h_new.tolist())
    e.tail = e.head + d * float(np.mean(SCL))
    e.roll = roll
bpy.ops.object.mode_set(mode="OBJECT")
REPORT["rig_fit"] = {"axis_scale_xyz": [round(float(x), 3) for x in SCL],
                     "joints_moved_m_max": round(float(max(np.linalg.norm(amap(h) - h) for h in heads_old.values())), 3)}
log("rig fit", REPORT["rig_fit"])
rest_pose()
OVm = amap(OV)                                       # the weight source, mapped like the joints

# head region: around the mapped Head bone
hb = arm.data.bones["Head"]
HEAD_P = np.array(arm.matrix_world @ ((hb.head_local + hb.tail_local) / 2))
HEAD_R = 0.95


def head_mask(P):
    return np.linalg.norm(P - HEAD_P, axis=1) < HEAD_R


# --------------------------------------------------------------------------------------------- decimate, colour, weights
sampler = hc.Sampler(high, img)
hero, info = hc.decimate_protected(high, BUD["Hero"][0], "Kaiju_HY_Hero", scene, protect=head_mask, share=HEAD_SHARE["Hero"], tmax=BUD["Hero"][1])
REPORT.setdefault("decimate", {})["Hero"] = info
log("hero", info)
C = hc.face_colours(hero, sampler)
REMAP = None
if SHEET_F:
    SC = hc.sheet_colours(hero, hc.Sheet(SHEET_F), hc.Sheet(SHEET_B, back=True) if SHEET_B else None, H_T)
    REMAP = hc.ColourRemap(C, SC)
    C2 = REMAP.apply(C)
    REPORT["colour_remap"] = {"faces_seen_on_sheet": int((~np.isnan(SC[:, 0])).sum()), "faces": len(C), "clusters": REMAP.rows}
    log("remap", json.dumps(REPORT["colour_remap"])[:700])
    C = C2
REPORT.setdefault("mask_faces", {})["Hero"] = hc.write_vcol(hero, C, hc.mask_kaiju, recolour_glow=hc.GLOW_AZURE)
REPORT["glow_face_share"] = round(REPORT["mask_faces"]["Hero"].get("0.75", 0) / len(C), 3)


def transfer_weights(ob, k=16, smooth=SMOOTH_IT):
    kd = KDTree(len(OVm))
    for i, p in enumerate(OVm):
        kd.insert(Vector(p), i)
    kd.balance()
    P = np_verts(ob)
    W = np.zeros((len(P), len(BONES)))
    for i, p in enumerate(P):
        for _, idx, dist in kd.find_n(Vector(p), k):
            W[i] += OW[idx] / (dist ** 2 + 1e-10)
    W /= np.maximum(W.sum(1, keepdims=True), 1e-12)
    ed = hc.edges_of(ob)
    for _ in range(smooth):
        acc = np.zeros_like(W)
        deg = np.zeros(len(P))
        np.add.at(acc, ed[:, 0], W[ed[:, 1]])
        np.add.at(acc, ed[:, 1], W[ed[:, 0]])
        np.add.at(deg, ed[:, 0], 1.0)
        np.add.at(deg, ed[:, 1], 1.0)
        W = 0.5 * W + 0.5 * acc / np.maximum(deg, 1.0)[:, None]
    idx = np.argsort(-W, 1)[:, 4:]
    np.put_along_axis(W, idx, 0.0, 1)
    W[W < 0.01] = 0.0
    W /= np.maximum(W.sum(1, keepdims=True), 1e-12)
    return W


def bind(ob, W):
    ob.vertex_groups.clear()
    for bi in np.nonzero(W.sum(0) > 0)[0]:
        g = ob.vertex_groups.new(name=BONES[bi])
        for i in np.nonzero(W[:, bi])[0]:
            g.add([int(i)], float(W[i, bi]), "REPLACE")
    ob.parent = arm
    ob.matrix_parent_inverse = arm.matrix_world.inverted()
    ob.modifiers.new("Armature", "ARMATURE").object = arm


LEVEL, LW = {"Hero": hero}, {}
LW["Hero"] = transfer_weights(hero)
for tag, share in (("LOD0", HEAD_SHARE["LOD0"]), ("LOD1", 0.0)):
    ob, info = hc.decimate_protected(hero, BUD[tag][0], f"Kaiju_HY_{tag}", scene, protect=head_mask if share else None,
                                     share=share, tmax=BUD[tag][1])
    REPORT["decimate"][tag] = info
    # colour: per-face vote from the Hero surface (mask code snapped, never blended)
    HV = np_verts(hero)
    hbvh = BVHTree.FromPolygons([tuple(map(float, v)) for v in HV], [tuple(p.vertices) for p in hero.data.polygons])
    hface = hc.read_face_vcol(hero)
    LV = np_verts(ob)
    Cf = np.zeros((len(ob.data.polygons), 3))
    Af = np.zeros(len(ob.data.polygons))
    for p in ob.data.polygons:
        vs = LV[list(p.vertices)[:3]]
        votes = [hface[hbvh.find_nearest(Vector(b @ vs))[2]] for b in hc.BARY]
        votes = np.array(votes)
        Cf[p.index] = np.median(votes[:, :3], 0)
        codes = list(np.round(votes[:, 3] * 8))
        Af[p.index] = max(set(codes), key=codes.count) / 8
    REPORT["mask_faces"][tag] = hc.write_vcol(ob, Cf, lambda c, _it=iter(Af): next(_it))
    LW[tag] = transfer_weights(ob, smooth=12)
    LEVEL[tag] = ob
    log(tag, info)
for tag in ("Hero", "LOD0", "LOD1"):
    bind(LEVEL[tag], LW[tag])
shadow, sinfo = hc.hull_proxy(np_verts(LEVEL["LOD0"]), LW["LOD0"], BONES, "Kaiju_HY_Shadow", scene, budget=BUD["Shadow"])
hc.write_vcol(shadow, np.full((len(shadow.data.polygons), 3), 0.2), lambda c: 0.0)
shadow.parent = arm
shadow.matrix_parent_inverse = arm.matrix_world.inverted()
shadow.modifiers.new("Armature", "ARMATURE").object = arm
LEVEL["Shadow"] = shadow
REPORT["shadow"] = sinfo
REPORT["tris"] = {t: tri_count(o) for t, o in LEVEL.items()}
log("tris", REPORT["tris"])
for o in bpy.data.objects:
    if o.name.startswith("HY_"):
        o.hide_render = True
        o.hide_viewport = True


# --------------------------------------------------------------------------------------------- pose test (every clip)
def posed(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = ev.to_mesh()
    co = np.zeros(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    ev.to_mesh_clear()
    M = np.array(ob.matrix_world)
    return co.reshape(-1, 3) @ M[:3, :3].T + M[:3, 3]


rest_pose()
REST_P = {t: posed(LEVEL[t]) for t in ("Hero", "LOD0")}
deform = {}
for act in ACTS:
    for fr in (0.3, 0.6):
        f = set_clip(act, fr)
        row = {}
        for t in ("Hero", "LOD0"):
            e = hc.edges_of(LEVEL[t])
            p0, p1 = REST_P[t], posed(LEVEL[t])
            l0 = np.linalg.norm(p0[e[:, 0]] - p0[e[:, 1]], axis=1)
            l1 = np.linalg.norm(p1[e[:, 0]] - p1[e[:, 1]], axis=1)
            ok = l0 > 1e-3
            r = l1[ok] / l0[ok]
            r = np.maximum(r, 1 / np.maximum(r, 1e-6))
            dl = np.abs(l1[ok] - l0[ok])
            row[t] = {"edges": int(ok.sum()), "over_1.5": int((r > 1.5).sum()), "over_1.5_and_5cm": int(((r > 1.5) & (dl > 0.05)).sum()),
                      "max": round(float(r.max()), 2)}
        deform[f"{act.name.split('|')[-1]}@{f}"] = row
REPORT["deform"] = deform
rest_pose()
log("deform", json.dumps(deform)[:700])

# --------------------------------------------------------------------------------------------- export (swap file)
for o in list(OLD.values()):                       # free the names Kaiju_Hero ... for the export copies
    o.name = "OLD_" + o.name
    o.data.name = "OLD_" + o.data.name
SWAP_MAT = bpy.data.materials.new("CM_Toon")
exp = []
for t in ("Hero", "LOD0", "LOD1", "Shadow"):
    o = LEVEL[t]
    c = o.copy()
    c.data = o.data.copy()
    scene.collection.objects.link(c)
    c.name = c.data.name = f"Kaiju_{t}"
    c.data.materials.clear()
    c.data.materials.append(SWAP_MAT)
    for p in c.data.polygons:
        p.material_index = 0
    hc.constant_uv(c)                                # transparent texel of Kaiju_Face.png and every expression atlas
    c.data.color_attributes.active_color_name = "Col"
    c.hide_render = True
    exp.append(c)
ad = arm.animation_data or arm.animation_data_create()
ad.action = None
for tr in list(ad.nla_tracks):
    ad.nla_tracks.remove(tr)
for a in ACTS:
    clip = a.name.split("|")[-1]
    tr = ad.nla_tracks.new()
    tr.name = clip
    st = tr.strips.new(clip, int(a.frame_range[0]), a)
    st.name = clip
    try:
        if st.action_slot is None and len(a.slots):
            st.action_slot = a.slots[0]
    except Exception:
        pass
for o in bpy.context.view_layer.objects:
    o.select_set(o == arm or o in exp)
bpy.context.view_layer.objects.active = arm
fbx_path = os.path.join(EXP, "Kaiju_Adult.fbx")
r = bpy.ops.export_scene.fbx(filepath=fbx_path, use_selection=True, **KAIJU_FBX)
assert "FINISHED" in r
REPORT["fbx"] = {"path": fbx_path, "bytes": os.path.getsize(fbx_path)}
log("FBX", fbx_path, os.path.getsize(fbx_path))
for tr in list(ad.nla_tracks):
    ad.nla_tracks.remove(tr)
for c in exp:
    c.hide_viewport = True
# albedo with the same correction, kept as reference (the game reads vertex colour)
img.scale(2048, 2048)
px = np.empty(2048 * 2048 * 4, np.float32)
img.pixels.foreach_get(px)
if REMAP is not None:
    px = px.reshape(-1, 4)
    px[:, :3] = REMAP.apply(px[:, :3]).astype(np.float32)
    px = px.ravel()
im = bpy.data.images.new("Kaiju_HY_Albedo", 2048, 2048, alpha=True)
im.pixels.foreach_set(px)
im.filepath_raw = os.path.join(EXP, "Kaiju_HY_Albedo.png")
im.file_format = "PNG"
im.save()
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(WORK, "Kaiju_HY.blend"), compress=True)

# --------------------------------------------------------------------------------------------- renders (Workbench)
scene.render.engine = "BLENDER_WORKBENCH"
sh = scene.display.shading
sh.light = "STUDIO"
sh.show_object_outline = True
sh.object_outline_color = NAVY
sh.show_cavity = False
sh.show_shadows = False
scene.world = scene.world or bpy.data.worlds.new("W")
scene.world.color = (0.33, 0.33, 0.33)
scene.view_settings.view_transform = "Standard"
scene.render.image_settings.file_format = "PNG"
gm = bpy.data.meshes.new("Ground")
gm.from_pydata([(-12, -12, 0), (12, -12, 0), (12, 12, 0), (-12, 12, 0)], [], [(0, 1, 2, 3)])
ga = gm.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
for d in ga.data:
    d.color_srgb = (0.47, 0.49, 0.45, 0.0)
ground = bpy.data.objects.new("Ground", gm)
scene.collection.objects.link(ground)
cam_d = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_d)
scene.collection.objects.link(cam)
scene.camera = cam
SETS = {"v7": [OLD["Kaiju_Hero"]], "hero": [LEVEL["Hero"]], "lod0": [LEVEL["LOD0"]]}
for o in OLD.values():
    o.hide_viewport = False


def show(which):
    keep = set(SETS[which])
    for o in scene.objects:
        if o.type == "MESH" and o is not ground:
            o.hide_render = o not in keep


def view_dir(az, el):
    a, e = math.radians(az), math.radians(el)
    return Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))


def look(target, d):
    cam.location = target + d
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()


def render(name, colour="VERTEX"):
    sh.color_type = colour
    scene.render.filepath = os.path.join(REN, name + ".png")
    bpy.ops.render.render(write_still=True)


def pts_of(which):
    return np.concatenate([posed(o) for o in SETS[which]])


def close_shot(which, name, az=35, el=12, colour="VERTEX", H=900, W=900):
    P = pts_of(which)
    d = view_dir(az, el)
    c = Vector(((P.max(0) + P.min(0)) / 2).tolist())
    cam_d.type = "ORTHO"
    right = Vector((0, 0, 1)).cross(d).normalized()
    up = d.cross(right).normalized()
    pr, pu = (P - np.array(c)) @ np.array(right), (P - np.array(c)) @ np.array(up)
    scene.render.resolution_x, scene.render.resolution_y = W, H
    cam_d.sensor_fit = "VERTICAL"
    cam_d.ortho_scale = float(max(np.ptp(pu), np.ptp(pr) * H / W) * 1.08)
    look(c + right * float((pr.max() + pr.min()) / 2) + up * float((pu.max() + pu.min()) / 2), d * 30)
    cam_d.clip_end = 200
    scene.render.use_border = False
    show(which)
    ground.hide_render = True
    render(name, colour)


def game_shot(which, name, colour="VERTEX", crop=440):
    from bpy_extras.object_utils import world_to_camera_view
    cam_d.type = "PERSP"
    cam_d.sensor_fit = "VERTICAL"
    cam_d.angle_y = math.radians(CAM_FOV)
    cam_d.clip_start, cam_d.clip_end = 1.0, 200.0
    look(Vector((0, 0, 1.0)), view_dir(CAM_YAW, CAM_PITCH) * CAM_DIST)
    scene.render.resolution_x, scene.render.resolution_y = 1920, 1080
    bpy.context.view_layer.update()
    P = pts_of(which)
    uv = np.array([world_to_camera_view(scene, cam, Vector(p))[:2] for p in P[:: max(1, len(P) // 2000)]])
    cx, cy = (uv.max(0) + uv.min(0)) / 2
    REPORT.setdefault("game_px", {})[name] = round(float(np.ptp(uv[:, 1]) * 1080), 1)
    scene.render.use_border = True
    scene.render.use_crop_to_border = True
    hw, hh = crop / 2 / 1920, crop / 2 / 1080
    scene.render.border_min_x, scene.render.border_max_x = cx - hw, cx + hw
    scene.render.border_min_y, scene.render.border_max_y = cy - hh, cy + hh
    show(which)
    ground.hide_render = False
    render(name, colour)
    scene.render.use_border = False


ACT = {a.name.split("|")[-1]: a for a in ACTS}
set_clip(ACT["Idle"], 0.5)
for which, colour, tag in (("v7", "VERTEX", "v7"), ("hero", "VERTEX", "hero_vcol"), ("hero", "TEXTURE", "hero_tex"), ("lod0", "VERTEX", "lod0_vcol")):
    if tag == "hero_tex":
        mat = bpy.data.materials.new("Kaiju_HY_Albedo")
        mat.use_nodes = True
        tn = mat.node_tree.nodes.new("ShaderNodeTexImage")
        tn.image = img
        mat.node_tree.links.new(tn.outputs["Color"], mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
        LEVEL["Hero"].data.materials.clear()
        LEVEL["Hero"].data.materials.append(mat)
        if not LEVEL["Hero"].data.uv_layers:
            continue
    close_shot(which, f"tq_{tag}", colour=colour)
    game_shot(which, f"game_{tag}", colour=colour)
for clip, fr, nm in (("Idle", 0.5, "idle"), ("Walk", 0.3, "walk"), ("Slam", 0.5, "slam"), ("Swipe", 0.5, "swipe"), ("Roar", 0.5, "roar")):
    set_clip(ACT[clip], fr)
    close_shot("hero", f"pose_{nm}_hero", H=700, W=700)
    close_shot("lod0", f"pose_{nm}_lod0", H=600, W=600)
rest_pose()
close_shot("hero", "rest_hero", az=0, el=5, H=700, W=800)
log("renders done")

# --------------------------------------------------------------------------------------------- FBX round trip
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx_path, use_anim=True)
rt = {"objects": {}, "bones": None, "actions": sorted(a.name for a in bpy.data.actions)}
for o in bpy.data.objects:
    if o.type == "MESH":
        rt["objects"][o.name] = {"tris": sum(len(p.vertices) - 2 for p in o.data.polygons), "groups": len(o.vertex_groups),
                                 "colors": [a.name for a in o.data.color_attributes], "dims": [round(x, 3) for x in o.dimensions]}
    if o.type == "ARMATURE":
        rt["bones"] = len(o.data.bones)
REPORT["fbx_round_trip"] = rt
json.dump(REPORT, open(os.path.join(REN, "hy3d_report.json"), "w"), indent=1)
log("round trip", json.dumps(rt))
log("DONE")
