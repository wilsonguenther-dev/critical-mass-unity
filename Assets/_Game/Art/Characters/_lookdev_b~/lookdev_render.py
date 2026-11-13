"""Blender run 2 (one long headless run): put the processed meshes on the shared KayKit 41-joint rig, measure how they
deform in clips, render the look-dev set. Read-only on warrior.blend (never saved).

  cm-heavy blender perl -e 'alarm 1500; exec @ARGV' Blender -b --factory-startup --python-exit-code 1 \
      -P lookdev_render.py -- <warrior.blend> <processed_dir> <render_dir>

Looks: soft toon ramp from one key light at the camera's upper left, (a) no outline + rim light, (b) thin
Void Navy (#18233B) inverted-hull outline of 1.25 px. Textures: <variant>_albedo.png (sRGB) and <variant>_mask.png
(R team mask, G emission, B AO). Renders are at most 1024 px.
"""
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
BLEND, IN, OUT = argv[0], argv[1], argv[2]
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=BLEND)
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "None"
scene.render.film_transparent = True
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.render.resolution_percentage = 100
try:
    scene.eevee.taa_render_samples = 16
except AttributeError:
    pass
arm = bpy.data.objects["Rig"]
for ob in list(bpy.data.objects):
    if ob.type == "MESH":
        bpy.data.objects.remove(ob, do_unlink=True)
LOG = {"clips": {}, "deform": {}}


def srgb(h):
    h = h.lstrip("#")
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple((x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4) for x in c) + (1.0,)


# ---------------------------------------------------------------- materials
def toon_material(name, albedo, maskimg):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    N = nt.nodes
    L = nt.links
    N.clear()
    out = N.new("ShaderNodeOutputMaterial")
    em = N.new("ShaderNodeEmission")
    L.new(em.outputs[0], out.inputs["Surface"])
    ta = N.new("ShaderNodeTexImage")
    ta.image = bpy.data.images.load(albedo)
    tm = N.new("ShaderNodeTexImage")
    tm.image = bpy.data.images.load(maskimg)
    tm.image.colorspace_settings.name = "Non-Color"
    sep = N.new("ShaderNodeSeparateColor")
    L.new(tm.outputs["Color"], sep.inputs[0])
    geo = N.new("ShaderNodeNewGeometry")
    ld = N.new("ShaderNodeCombineXYZ")
    ld.name = "LIGHT_DIR"
    dot = N.new("ShaderNodeVectorMath")
    dot.operation = "DOT_PRODUCT"
    L.new(geo.outputs["Normal"], dot.inputs[0])
    L.new(ld.outputs[0], dot.inputs[1])
    mr = N.new("ShaderNodeMapRange")
    mr.inputs["From Min"].default_value = -1.0
    L.new(dot.outputs["Value"], mr.inputs["Value"])
    ramp = N.new("ShaderNodeValToRGB")          # SOFT toon ramp: two bands joined by a short eased blend
    ramp.color_ramp.interpolation = "EASE"
    e = ramp.color_ramp.elements
    e[0].position, e[0].color = 0.40, (0.62, 0.62, 0.70, 1)
    e[1].position, e[1].color = 0.56, (1.0, 1.0, 1.0, 1)
    L.new(mr.outputs["Result"], ramp.inputs["Fac"])
    mul = N.new("ShaderNodeMix")
    mul.data_type, mul.blend_type = "RGBA", "MULTIPLY"
    mul.inputs[0].default_value = 1.0
    L.new(ta.outputs["Color"], mul.inputs[6])
    L.new(ramp.outputs["Color"], mul.inputs[7])
    # rim light: facing ratio -> narrow band, scaled by RIM (0 = off)
    lw = N.new("ShaderNodeLayerWeight")
    lw.inputs["Blend"].default_value = 0.35
    rr = N.new("ShaderNodeValToRGB")
    rr.color_ramp.elements[0].position = 0.55
    rr.color_ramp.elements[1].position = 0.80
    L.new(lw.outputs["Facing"], rr.inputs["Fac"])
    rim = N.new("ShaderNodeValue")
    rim.name = "RIM"
    rim.outputs[0].default_value = 0.0
    rmul = N.new("ShaderNodeMath")
    rmul.operation = "MULTIPLY"
    L.new(rr.outputs["Color"], rmul.inputs[0])
    L.new(rim.outputs[0], rmul.inputs[1])
    radd = N.new("ShaderNodeMix")
    radd.data_type, radd.blend_type = "RGBA", "SCREEN"
    L.new(rmul.outputs[0], radd.inputs[0])
    L.new(mul.outputs[2], radd.inputs[6])
    radd.inputs[7].default_value = (0.95, 0.97, 1.0, 1)
    # emission: blade edge / shield ring glow from the mask's G channel
    eadd = N.new("ShaderNodeMix")
    eadd.data_type, eadd.blend_type = "RGBA", "ADD"
    L.new(sep.outputs[1], eadd.inputs[0])
    L.new(radd.outputs[2], eadd.inputs[6])
    L.new(ta.outputs["Color"], eadd.inputs[7])
    L.new(eadd.outputs[2], em.inputs["Color"])
    return m


def outline_material():
    m = bpy.data.materials.new("LDB_Outline")
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    e = nt.nodes.new("ShaderNodeEmission")
    e.inputs["Color"].default_value = srgb("#18233B")
    o = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(e.outputs[0], o.inputs["Surface"])
    m.use_backface_culling = True
    return m


OUTLINE_MAT = outline_material()


# ---------------------------------------------------------------- build the variants
def build(variant):
    d = np.load(os.path.join(IN, variant + ".npz"))
    V, F, UV, W, part = d["V_rest"], d["F"], d["UV"], d["W"], d["part"]
    bones = [str(b) for b in d["bones"]]
    me = bpy.data.meshes.new("LDB_" + variant)
    me.from_pydata(V.tolist(), [], F.tolist())
    uv = me.uv_layers.new(name="UVMap")
    uv.data.foreach_set("uv", UV[F.ravel()].ravel())
    try:
        me.shade_smooth()
    except AttributeError:
        me.polygons.foreach_set("use_smooth", [True] * len(me.polygons))
    ob = bpy.data.objects.new("LDB_" + variant, me)
    scene.collection.objects.link(ob)
    for bi, b in enumerate(bones):
        idx = np.nonzero(W[:, bi] > 1e-4)[0]
        if len(idx) == 0:
            continue
        vg = ob.vertex_groups.new(name=b)
        for i in idx:
            vg.add([int(i)], float(W[i, bi]), "REPLACE")
    mod = ob.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    me.materials.append(toon_material("LDB_Toon_" + variant, os.path.join(IN, variant + "_albedo.png"),
                                      os.path.join(IN, variant + "_mask.png")))
    me.materials.append(OUTLINE_MAT)
    sol = ob.modifiers.new("Outline", "SOLIDIFY")
    sol.offset, sol.use_flip_normals, sol.use_rim, sol.use_even_offset = 1.0, True, False, True
    sol.material_offset = 1
    sol.thickness = 0.0
    return ob, d


VARIANTS = [v for v in ("parts", "fused") if os.path.exists(os.path.join(IN, v + ".npz"))]
OBS = {v: build(v) for v in VARIANTS}


def show_only(variant):
    for v, (ob, _) in OBS.items():
        ob.hide_render = v != variant


def set_style(style, m_per_px):
    for ob, _ in OBS.values():
        ob.modifiers["Outline"].show_render = style == "b"
        ob.modifiers["Outline"].thickness = 1.25 * m_per_px
        for mat in ob.data.materials:
            if mat and "RIM" in mat.node_tree.nodes:
                mat.node_tree.nodes["RIM"].outputs[0].default_value = 0.55 if style == "a" else 0.0


def set_light(d):
    for ob, _ in OBS.values():
        n = ob.data.materials[0].node_tree.nodes["LIGHT_DIR"]
        for i in range(3):
            n.inputs[i].default_value = d[i]


def find_action(cands):
    names = [a.name for a in bpy.data.actions]
    for c in cands:
        for n in names:
            if n == c:
                return n
    for c in cands:
        for n in names:
            if c.lower() in n.lower():
                return n
    return None


def pose(clip, frame):
    arm.animation_data_create()
    arm.animation_data.action = bpy.data.actions[clip]
    scene.frame_set(frame)
    bpy.context.view_layer.update()


def posed(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    mods = ob.modifiers["Outline"].show_viewport
    ob.modifiers["Outline"].show_viewport = False
    dg.update()
    ev = ob.evaluated_get(dg)
    me = ev.to_mesh()
    P = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get("co", P)
    ev.to_mesh_clear()
    ob.modifiers["Outline"].show_viewport = mods
    return (np.c_[P.reshape(-1, 3), np.ones(len(P) // 3)] @ np.array(ob.matrix_world).T)[:, :3]


def view_dir(az, el):
    a, e = math.radians(az), math.radians(el)
    return Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))


def key_light_for(d):
    fwd = -d
    right = fwd.cross(Vector((0, 0, 1))).normalized()
    up = right.cross(fwd).normalized()
    return (-right * 0.55 + up * 0.75 - fwd * 0.55).normalized()


cam_data = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
world = bpy.data.worlds.new("W")
world.color = (0.02, 0.02, 0.03)
scene.world = world


def ortho_shot(path, pts, az, el, height, style):
    cam_data.type = "ORTHO"
    d = view_dir(az, el)
    c = Vector(pts.mean(0))
    cam.location = c + d * 12.0
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    bpy.context.view_layer.update()
    inv = np.array(cam.matrix_world.inverted())
    cp = (np.c_[pts, np.ones(len(pts))] @ inv.T)[:, :3]
    pad = 0.04
    w, h = np.ptp(cp[:, 0]) + pad, np.ptp(cp[:, 1]) + pad
    cx, cy = (cp[:, 0].max() + cp[:, 0].min()) / 2, (cp[:, 1].max() + cp[:, 1].min()) / 2
    cam.location = cam.matrix_world @ Vector((cx, cy, 0))
    scene.render.resolution_y = height
    scene.render.resolution_x = min(1024, max(16, int(round(height * w / h))))
    rx, ry = scene.render.resolution_x, scene.render.resolution_y
    cam_data.ortho_scale = h if ry >= rx else max(w, h * rx / ry)
    m_per_px = cam_data.ortho_scale / max(scene.render.resolution_x, scene.render.resolution_y)
    set_style(style, m_per_px)
    set_light(key_light_for(d))
    scene.render.use_border = False
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("RENDERED", os.path.basename(path), scene.render.resolution_x, scene.render.resolution_y, flush=True)


# ---------------------------------------------------------------- deformation numbers per clip frame
CLIPS = {
    "Idle": find_action(["Idle"]),
    "Walk": find_action(["Walking_A", "Walking_B", "Walk"]),
    "Attack": find_action(["1H_Melee_Attack_Chop", "1H_Melee_Attack_Slice_Diagonal", "Melee_Attack", "Attack"]),
}
LOG["clips"] = CLIPS
print("CLIPS", CLIPS, flush=True)


def skin_mats():
    return {pb.name: np.array(pb.matrix) @ np.linalg.inv(np.array(pb.bone.matrix_local)) for pb in arm.pose.bones}


def deform_numbers(variant):
    ob, d = OBS[variant]
    part = d["part"]
    Vr = d["V_rest"].astype(np.float64)
    F = d["F"]
    edges = np.unique(np.sort(np.vstack([F[:, [0, 1]], F[:, [1, 2]], F[:, [2, 0]]]), 1), axis=0)
    bones = [str(b) for b in d["bones"]]
    W = d["W"]
    armb = [bones.index(b) for b in bones if b.startswith(("upperarm", "lowerarm", "wrist", "hand"))]
    armv = W[:, armb].sum(1) > 0.5
    arm_e = armv[edges[:, 0]] & armv[edges[:, 1]] & (part[edges[:, 0]] == 0)
    pose(CLIPS["Idle"], 1)
    P0 = posed(ob)
    L0 = np.linalg.norm(P0[edges[:, 0]] - P0[edges[:, 1]], axis=1) + 1e-9
    res = {}
    for clip in ("Idle", "Walk", "Attack"):
        act = bpy.data.actions[CLIPS[clip]]
        f0, f1 = [int(x) for x in act.frame_range]
        worst = {"shield_dev_cm_max": 0, "cleaver_dev_cm_max": 0, "arm_stretch_p95": 0, "shield_dev_cm_median": 0,
                 "cleaver_dev_cm_median": 0}
        for fr in np.linspace(f0, f1, 9).round().astype(int):
            pose(CLIPS[clip], int(fr))
            P = posed(ob)
            S = skin_mats()
            for pi, slot, key in ((1, "handslot.l", "shield"), (2, "handslot.r", "cleaver")):
                sel = part == pi
                if not sel.any():
                    continue
                rig = (np.c_[Vr[sel], np.ones(sel.sum())] @ S[slot].T)[:, :3]
                dev = np.linalg.norm(P[sel] - rig, axis=1) * 100
                # rigid-body error relative to the best rigid fit is what matters visually; use the hand-slot as the
                # reference the game uses (a held item IS rigid in the hand)
                worst[key + "_dev_cm_max"] = max(worst[key + "_dev_cm_max"], round(float(dev.max()), 2))
                worst[key + "_dev_cm_median"] = max(worst[key + "_dev_cm_median"], round(float(np.median(dev)), 2))
            Lt = np.linalg.norm(P[edges[:, 0]] - P[edges[:, 1]], axis=1)
            if arm_e.any():
                worst["arm_stretch_p95"] = max(worst["arm_stretch_p95"],
                                               round(float(np.percentile(np.abs(Lt[arm_e] / L0[arm_e] - 1), 95)), 3))
        res[clip] = worst
    LOG["deform"][variant] = res
    print("DEFORM", variant, json.dumps(res), flush=True)


for v in VARIANTS:
    deform_numbers(v)

# ---------------------------------------------------------------- renders
main = "parts" if "parts" in OBS else VARIANTS[0]
show_only(main)
pose(CLIPS["Idle"], 1)
P = posed(OBS[main][0])
LOG["idle_bbox"] = [P.min(0).round(3).tolist(), P.max(0).round(3).tolist()]
for name, az, el in (("front", 8, 6), ("three-quarter", -35, 6), ("profile", -90, 4), ("back", 180, 6)):
    for style in ("a", "b"):
        ortho_shot(os.path.join(OUT, f"turn_{name}_{style}.png"), P, az, el, 720, style)
head = P[P[:, 2] > P[:, 2].min() + 0.62 * np.ptp(P[:, 2])]
ortho_shot(os.path.join(OUT, "face_a.png"), head, -10, 4, 640, "a")
ortho_shot(os.path.join(OUT, "face_b.png"), head, -10, 4, 640, "b")

# clip frames, both variants: Idle, Walk (contact), Attack (wind-up, impact)
for v in VARIANTS:
    show_only(v)
    for clip, fracs in (("Idle", (0.0,)), ("Walk", (0.25, 0.75)), ("Attack", (0.35, 0.6))):
        act = bpy.data.actions[CLIPS[clip]]
        f0, f1 = act.frame_range
        for fr_ in fracs:
            fr = int(round(f0 + (f1 - f0) * fr_))
            pose(CLIPS[clip], fr)
            Pc = posed(OBS[v][0])
            ortho_shot(os.path.join(OUT, f"clip_{v}_{clip}_{fr:03d}.png"), Pc, -30, 12, 480, "b")

# gameplay camera (pitch 55, FOV 35, 23.4 u), 1080p pixel scale, rendered as a 300x300 crop around the unit
show_only(main)
pose(CLIPS["Idle"], 1)
CAM_PITCH, CAM_FOV, CAM_DIST = 55.0, 35.0, 23.4
cam_data.type = "PERSP"
cam_data.sensor_fit = "VERTICAL"
cam_data.angle_y = math.radians(CAM_FOV)
cam_data.clip_start, cam_data.clip_end = 1.0, 200.0
scene.render.resolution_x, scene.render.resolution_y = 1920, 1080
d = view_dir(0.0, CAM_PITCH)
cam.location = d * CAM_DIST + Vector((0, 0, 0.8))
cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
import bmesh  # noqa: E402


def disc(name, r, z, hexc, alpha=1.0):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_circle(bm, cap_ends=True, radius=r, segments=64)
    for v in bm.verts:
        v.co.z = z
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    m = bpy.data.materials.new(name + "_M")
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    e = nt.nodes.new("ShaderNodeEmission")
    e.inputs["Color"].default_value = srgb(hexc)
    o = nt.nodes.new("ShaderNodeOutputMaterial")
    if alpha < 1:
        tr = nt.nodes.new("ShaderNodeBsdfTransparent")
        mx = nt.nodes.new("ShaderNodeMixShader")
        mx.inputs[0].default_value = alpha
        nt.links.new(tr.outputs[0], mx.inputs[1])
        nt.links.new(e.outputs[0], mx.inputs[2])
        nt.links.new(mx.outputs[0], o.inputs["Surface"])
        m.surface_render_method = "BLENDED"
    else:
        nt.links.new(e.outputs[0], o.inputs["Surface"])
    me.materials.append(m)
    scene.collection.objects.link(ob)
    return ob


disc("Ground", 30.0, 0.0, "#A3C150")
disc("Blob", 0.55, 0.003, "#18233B", 0.40)
scene.render.film_transparent = False
m_per_px = 2 * (CAM_DIST - 0.8) * math.tan(math.radians(CAM_FOV / 2)) / 1080
fwd_h = Vector((-d.x, -d.y, 0)).normalized()
right_h = fwd_h.cross(Vector((0, 0, 1))).normalized()
Ls = (fwd_h - right_h).normalized() * math.cos(math.radians(50))
Ls.z = math.sin(math.radians(50))
set_light(Ls.normalized())
bpy.context.view_layer.update()
from bpy_extras.object_utils import world_to_camera_view  # noqa: E402
top = Vector(P[P[:, 2].argmax()])
yt = world_to_camera_view(scene, cam, top).y * 1080
yf = world_to_camera_view(scene, cam, Vector((top.x, top.y, 0))).y * 1080
LOG["game_feet_to_top_px"] = round(yt - yf, 1)
print("FEET_TO_TOP_PX", round(yt - yf, 1), flush=True)
c2 = world_to_camera_view(scene, cam, Vector((0, 0, 0.9)))
scene.render.use_border = True
scene.render.use_crop_to_border = True
bw, bh = 300 / 1920, 300 / 1080
scene.render.border_min_x, scene.render.border_max_x = c2.x - bw / 2, c2.x + bw / 2
scene.render.border_min_y, scene.render.border_max_y = c2.y - bh / 2, c2.y + bh / 2
for style in ("a", "b"):
    set_style(style, m_per_px)
    scene.render.filepath = os.path.join(OUT, f"game_{style}.png")
    bpy.ops.render.render(write_still=True)
    print("RENDERED game", style, flush=True)

# export the clean asset for the record (rest pose, skinned, textures beside it, no clips; never the live FBX)
show_only(main)
arm.animation_data.action = None
for pb in arm.pose.bones:
    pb.matrix_basis = Matrix()
ob = OBS[main][0]
ob.modifiers["Outline"].show_viewport = ob.modifiers["Outline"].show_render = False
ob.data.materials.pop(index=1)
ob.modifiers.remove(ob.modifiers["Outline"])
bpy.ops.object.select_all(action="DESELECT")
ob.select_set(True)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, f"Warrior_LDB_{main}.glb"), export_format="GLB",
                          use_selection=True, use_active_scene=True, export_yup=True, export_apply=False,
                          export_skins=True, export_def_bones=True, export_animations=False,
                          export_draco_mesh_compression_enable=False)
json.dump(LOG, open(os.path.join(OUT, "render_log.json"), "w"), indent=1)
print("DONE")
