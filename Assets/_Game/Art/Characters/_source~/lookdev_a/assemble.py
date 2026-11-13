"""Look-development A, the only Blender step: rig the game mesh, render every comparison, export the FBX.

  ~/bin/cm-heavy blender perl -e 'alarm 1500; exec @ARGV' /Applications/Blender.app/Contents/MacOS/Blender -b \
      --factory-startup --python-exit-code 1 -P assemble.py -- <work> <renders_out> <export_out>

Inputs: ../warrior.blend (KayKit 41-joint rig + 76 clips + the v7 meshes: weight source and the v7 reference renders),
<work>/low_*.npz and high_*.npz (pipeline.py mesh), <work>/Warrior_LD_Albedo_1024.png and _Mask_ (pipeline.py bake).
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
from mathutils.kdtree import KDTree

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.dirname(HERE)
sys.dont_write_bytecode = True
sys.path.insert(0, SRC)
sys.path.insert(0, HERE)
from cm_art import export, kit, palette, rig, toon  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:]
WORK, OUT, EXP = (os.path.abspath(a) for a in argv[:3])
SKIP = set(argv[3].split(",")) if len(argv) > 3 else set()
os.makedirs(OUT, exist_ok=True)
os.makedirs(EXP, exist_ok=True)
T0 = time.time()
GROUPS = ["skin", "hair", "teal", "navy", "cream", "trouser", "leather", "leather_dark", "boot", "brass", "stone",
          "stone_dark", "wrap", "wood", "iron", "glow", "stone_blade", "wood_seam"]
GID = {n: i for i, n in enumerate(GROUPS)}
RIGID = {GID[n] for n in ("stone", "stone_dark", "brass")}
CAM_PITCH, CAM_FOV, CAM_DIST = 55.0, 35.0, 23.4
THIN_BIBLE, THICK_BIBLE = 0.0055, 0.0135          # metres on the 2.0 u figure (v7 used 0.0135)
THIN_PX, THICK_PX = 1.25, 2.5                     # at the gameplay camera, 1080p
REPORT = {}


def log(*a):
    print("LD", round(time.time() - T0, 1), *a, flush=True)


bpy.ops.wm.open_mainfile(filepath=os.path.join(SRC, "warrior.blend"))
scene = bpy.context.scene
arm = bpy.data.objects["Rig"]
OLD = [bpy.data.objects[n] for n in ("Warrior_Body", "Warrior_Shield", "Warrior_Cleaver")]
if arm.animation_data:
    arm.animation_data.action = None
for pb in arm.pose.bones:
    pb.matrix_basis.identity()
scene.frame_set(0)
bpy.context.view_layer.update()
poses = rig.pose_matrices(arm, "Idle", 1)


def held(slot, origin, z_axis, y_hint):
    return arm.data.bones[slot].matrix_local @ poses[slot].inverted() @ kit.frame(origin, z_axis, y_hint)


T_SHIELD = held("handslot.l", (0.58, -0.16, 0.84), (0.84, -0.54, 0.0), (0, 0, 1)) @ Matrix.Diagonal((0.85, 0.85, 0.85, 1.0))
_down = Vector((-0.50, -0.50, -0.70)).normalized()
_n = Vector((0.707, -0.707, 0.0))
_z = (_n - (-_down) * _n.dot(-_down)).normalized()
T_CLEAVER = held("handslot.r", poses["handslot.r"].translation.copy(), _z, -_down)


def mesh_from_np(name, v, f, uv=None, n=None):
    me = bpy.data.meshes.new(name)
    me.vertices.add(len(v))
    me.vertices.foreach_set("co", np.ascontiguousarray(v, np.float32).ravel())
    me.loops.add(len(f) * 3)
    me.loops.foreach_set("vertex_index", np.ascontiguousarray(f, np.int32).ravel())
    me.polygons.add(len(f))
    me.polygons.foreach_set("loop_start", np.arange(0, len(f) * 3, 3, dtype=np.int32))
    try:
        me.polygons.foreach_set("loop_total", np.full(len(f), 3, np.int32))
    except Exception:
        pass                                   # derived from loop_start on current Blender
    me.update(calc_edges=True)
    me.validate()
    me.polygons.foreach_set("use_smooth", np.ones(len(me.polygons), bool))
    if uv is not None:
        li = np.zeros(len(me.loops), np.int32)
        me.loops.foreach_get("vertex_index", li)
        layer = me.uv_layers.new(name="UVMap")
        layer.data.foreach_set("uv", np.ascontiguousarray(uv[li], np.float32).ravel())
    if n is not None:
        me.normals_split_custom_set_from_vertices([tuple(x) for x in n.tolist()])
    return me


def xform(T, v, n=None):
    M4 = np.array(T, np.float64)
    v2 = v @ M4[:3, :3].T + M4[:3, 3]
    if n is None:
        return v2.astype(np.float32)
    R = M4[:3, :3] / (abs(np.linalg.det(M4[:3, :3])) ** (1 / 3))
    return v2.astype(np.float32), (n @ R.T).astype(np.float32)


coll = bpy.data.collections.new("LookdevA")
scene.collection.children.link(coll)

# ------------------------------------------------------------------------------------------ game meshes + weights
old_body = OLD[0]
bone_names = {b.name for b in arm.data.bones}
ogroups = {g.index: g.name for g in old_body.vertex_groups if g.name in bone_names}
kd = KDTree(len(old_body.data.vertices))
oldw = []
for vtx in old_body.data.vertices:
    kd.insert(vtx.co, vtx.index)
    oldw.append({ogroups[e.group]: e.weight for e in vtx.groups if e.group in ogroups and e.weight > 1e-4})
kd.balance()
log("old body verts", len(oldw), "world identity", old_body.matrix_world == Matrix.Identity(4))

NEW, HULL = [], []
for key, obname, T, slot in (("body", "WarriorLD_Body", None, None), ("shield", "WarriorLD_Shield", T_SHIELD, "handslot.l"),
                             ("cleaver", "WarriorLD_Cleaver", T_CLEAVER, "handslot.r")):
    z = np.load(os.path.join(WORK, f"low_{key}.npz"))
    v, n, f, uv, gid, comp = z["v"], z["n"], z["f"], z["uv"], z["gid"], z["comp"]
    if key == "body":
        BODY_GID = gid
    if T is not None:
        v, n = xform(T, v, n)
    me = mesh_from_np(obname, v, f, uv, n)
    ob = bpy.data.objects.new(obname, me)
    coll.objects.link(ob)
    # ---- weights
    W = [None] * len(v)
    if slot:
        for i in range(len(v)):
            W[i] = {slot: 1.0}
    else:
        for i in range(len(v)):
            acc = {}
            for co, idx, dist in kd.find_n(Vector(v[i]), 5):
                wgt = 1.0 / (dist * dist + 1e-5)
                for b, w in oldw[idx].items():
                    acc[b] = acc.get(b, 0.0) + w * wgt
            W[i] = acc
        head = (gid == GID["hair"]) | ((gid == GID["skin"]) & (v[:, 2] > 1.31)) | ((gid == GID["brass"]) & (v[:, 2] > 1.6))
        for i in np.where(head)[0]:
            W[i] = {"head": 1.0}
        for c in np.unique(comp[np.isin(gid, list(RIGID))]):          # rigid parts: one weight set per piece
            idx = np.where(comp == c)[0]
            acc = {}
            for i in idx:
                tot = sum(W[i].values()) or 1.0
                for b, w in W[i].items():
                    acc[b] = acc.get(b, 0.0) + w / tot
            top = sorted(acc.items(), key=lambda kv: -kv[1])[:2]
            if len(top) == 2 and top[1][1] < 0.25 * top[0][1]:
                top = top[:1]
            for i in idx:
                W[i] = dict(top)
    vgs = {}
    unweighted = 0
    for i, acc in enumerate(W):
        top = sorted(acc.items(), key=lambda kv: -kv[1])[:4]
        tot = sum(w for _, w in top)
        if tot <= 0:
            unweighted += 1
            top, tot = [("hips", 1.0)], 1.0
        for b, w in top:
            if w / tot > 0.01:
                vgs.setdefault(b, ob.vertex_groups.new(name=b) if b not in ob.vertex_groups else ob.vertex_groups[b]).add([i], w / tot, "REPLACE")
    ob.parent = arm
    mod = ob.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    NEW.append(ob)
    REPORT[obname] = {"triangles": int(len(f)), "vertices": int(len(v)), "bone_groups": sorted(vgs), "unweighted": unweighted}
    # ---- hull object: welded copy, faces flipped, pushed out along smooth normals by a Displace modifier
    hm = me.copy()
    bm = bmesh.new()
    bm.from_mesh(hm)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    border = {vv.index for e in bm.edges if e.is_boundary for vv in e.verts}
    bm.verts.index_update()
    border = [vv.index for vv in bm.verts if any(e.is_boundary for e in vv.link_edges)]
    bmesh.ops.reverse_faces(bm, faces=bm.faces)
    bm.to_mesh(hm)
    bm.free()
    hob = bpy.data.objects.new(obname + "_Hull", hm)
    coll.objects.link(hob)
    kd2 = KDTree(len(v))
    for i in range(len(v)):
        kd2.insert(Vector(v[i]), i)
    kd2.balance()
    for b in vgs:
        hob.vertex_groups.new(name=b)
    ow = hob.vertex_groups.new(name="outline_w")
    ow.add(list(range(len(hm.vertices))), 1.0, "REPLACE")
    ow.add(border, 0.0, "REPLACE")
    for hv in hm.vertices:
        _, idx, _ = kd2.find(hv.co)
        for e in me.vertices[idx].groups:
            hob.vertex_groups[ob.vertex_groups[e.group].name].add([hv.index], e.weight, "REPLACE")
    hob.parent = arm
    hmod = hob.modifiers.new("Armature", "ARMATURE")
    hmod.object = arm
    dm = hob.modifiers.new("Hull", "DISPLACE")
    dm.direction = "NORMAL"
    dm.mid_level = 0.0
    dm.strength = -THIN_BIBLE                  # faces are flipped, so the normals point inward
    dm.vertex_group = "outline_w"
    HULL.append(hob)
    REPORT[obname]["hull_border_verts"] = len(border)
log("meshes", {k: (v["triangles"], v["vertices"]) for k, v in REPORT.items()})

# ------------------------------------------------------------------------------------------ materials
def img(path, cs):
    im = bpy.data.images.load(path)
    im.colorspace_settings.name = cs
    return im


def make_ld_material(name, albedo=None, mask=None, flat=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    N = nt.nodes.new
    L = nt.links.new

    def val(v, nm=None):
        n = N("ShaderNodeValue")
        n.outputs[0].default_value = v
        if nm:
            n.name = nm
        return n.outputs[0]

    def rgb(c, nm=None):
        n = N("ShaderNodeRGB")
        n.outputs[0].default_value = (*c, 1.0)
        if nm:
            n.name = nm
        return n.outputs[0]

    def math_(op, a, b=None, clamp=False):
        n = N("ShaderNodeMath")
        n.operation = op
        n.use_clamp = clamp
        for i, x in enumerate((a, b)):
            if x is None:
                continue
            if isinstance(x, (int, float)):
                n.inputs[i].default_value = x
            else:
                L(x, n.inputs[i])
        return n.outputs[0]

    def mix(fac, a, b, blend="MIX"):
        n = N("ShaderNodeMix")
        n.data_type = "RGBA"
        n.blend_type = blend
        n.clamp_result = True
        for sock, x in ((0, fac), (6, a), (7, b)):
            if isinstance(x, (int, float)):
                n.inputs[sock].default_value = x
            elif isinstance(x, tuple):
                n.inputs[sock].default_value = (*x, 1.0)
            else:
                L(x, n.inputs[sock])
        return n.outputs[2]

    def dot(a, b):
        n = N("ShaderNodeVectorMath")
        n.operation = "DOT_PRODUCT"
        L(a, n.inputs[0])
        L(b, n.inputs[1])
        return n.outputs["Value"]

    geo = N("ShaderNodeNewGeometry")
    ld = N("ShaderNodeCombineXYZ")
    ld.name = "LIGHT_DIR"
    ld.inputs[2].default_value = 1.0
    if flat is not None:
        base = rgb(flat)
        emis_k = val(0.0)
    else:
        ta = N("ShaderNodeTexImage")
        ta.image = albedo
        tm = N("ShaderNodeTexImage")
        tm.image = mask
        sep = N("ShaderNodeSeparateColor")
        L(tm.outputs["Color"], sep.inputs[0])
        team = val(0.0, "TEAM")
        lum = dot(ta.outputs["Color"], rgb((0.30, 0.59, 0.11)))
        az_c, az_g = palette.lin("#2A95AE"), palette.lin("#4FE3F0")
        cr_c, cr_g = palette.lin(palette.TEAM_SWAP["team_cloth"][0]), palette.lin(palette.TEAM_SWAP["team_glow"][0])
        lc = 0.30 * az_c[0] + 0.59 * az_c[1] + 0.11 * az_c[2]
        lg = 0.30 * az_g[0] + 0.59 * az_g[1] + 0.11 * az_g[2]
        cloth = mix(1.0, rgb(cr_c), math_("DIVIDE", lum, lc), "MULTIPLY")
        glowc = mix(1.0, rgb(cr_g), math_("DIVIDE", lum, lg), "MULTIPLY")
        glowc = mix(math_("SMOOTHSTEP", 0.75, 1.0, None) if False else math_("MULTIPLY", math_("SUBTRACT", math_("DIVIDE", lum, lg), 0.9, True), 1.2, True), glowc, (1.0, 0.97, 0.93))
        swapped = mix(sep.outputs[0], ta.outputs["Color"], cloth)
        swapped = mix(sep.outputs[1], swapped, glowc)
        base = mix(team, ta.outputs["Color"], swapped)
        emis_k = sep.outputs[2]
    ndl = dot(geo.outputs["Normal"], ld.outputs[0])
    hl = math_("ADD", math_("MULTIPLY", ndl, 0.5), 0.5)
    ramp = N("ShaderNodeValToRGB")
    ramp.name = "RAMP"
    ramp.color_ramp.interpolation = "EASE"
    ramp.color_ramp.elements[0].position = 0.36
    ramp.color_ramp.elements[0].color = (0.45, 0.48, 0.71, 1.0)      # linear of sRGB (0.70, 0.72, 0.86): cool shadow
    ramp.color_ramp.elements[1].position = 0.60
    ramp.color_ramp.elements[1].color = (1, 1, 1, 1)
    L(hl, ramp.inputs[0])
    lit = mix(1.0, base, ramp.outputs[0], "MULTIPLY")
    fres = math_("POWER", math_("SUBTRACT", 1.0, dot(geo.outputs["Normal"], geo.outputs["Incoming"]), True), 3.0)
    mr = N("ShaderNodeMapRange")
    mr.interpolation_type = "SMOOTHSTEP"
    mr.inputs[1].default_value, mr.inputs[2].default_value = 0.45, 0.80
    L(hl, mr.inputs[0])
    rim = math_("MULTIPLY", math_("MULTIPLY", fres, mr.outputs[0]), val(0.22, "RIM"))
    col = mix(rim, lit, (1.0, 0.91, 0.75), "ADD")
    col = mix(math_("MULTIPLY", emis_k, 0.35), col, base, "ADD")
    em = N("ShaderNodeEmission")
    L(col, em.inputs["Color"])
    out = N("ShaderNodeOutputMaterial")
    L(em.outputs[0], out.inputs["Surface"])
    return m


albedo = img(os.path.join(WORK, "Warrior_LD_Albedo_1024.png"), "sRGB")
maskim = img(os.path.join(WORK, "Warrior_LD_Mask_1024.png"), "Non-Color")
MAT = make_ld_material("CM_ToonTex", albedo, maskim)
CLAY = make_ld_material("LD_Clay", flat=(0.52, 0.47, 0.42))
INK = toon.make_outline_material("LD_Ink")
INK.use_backface_culling = True
for ob in NEW:
    ob.data.materials.append(MAT)
for hob in HULL:
    hob.data.materials.clear()
    hob.data.materials.append(INK)

# v7 reference (old meshes): its own toon material and solidify hull
OLDMAT = bpy.data.materials["CM_Toon"]
old_outline = toon.make_outline_material()
old_hulls = [toon.add_outline(o, THICK_BIBLE, old_outline) for o in OLD]

# high "sculpt" meshes (rest pose), clay
HIGH = []
if "clay" not in SKIP:
    for key, T in (("body", None), ("shield", T_SHIELD), ("cleaver", T_CLEAVER)):
        z = np.load(os.path.join(WORK, f"high_{key}.npz"))
        v = z["v"] if T is None else xform(T, z["v"])
        ob = bpy.data.objects.new("High_" + key, mesh_from_np("High_" + key, v, z["f"]))
        ob.data.materials.append(CLAY)
        coll.objects.link(ob)
        HIGH.append(ob)
    log("high meshes", [len(o.data.polygons) for o in HIGH])

# ------------------------------------------------------------------------------------------ scene
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
cam_data = bpy.data.cameras.new("LDCam")
cam = bpy.data.objects.new("LDCam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
if scene.world is None:
    scene.world = bpy.data.worlds.new("W")


def show(which, line="thin"):
    """which: 'new' | 'old' | 'high'; line: 'none' | 'thin' | 'thick' (new only)."""
    for o in NEW:
        o.hide_render = which != "new"
    for o in HULL:
        o.hide_render = which != "new" or line == "none"
    for o in OLD:
        o.hide_render = which != "old"
    for o in HIGH:
        o.hide_render = which != "high"


def set_hull(width):
    for h in HULL:
        h.modifiers["Hull"].strength = -width


def posed_points(obs):
    dg = bpy.context.evaluated_depsgraph_get()
    pts = []
    for ob in obs:
        mods = [m for m in ob.modifiers if m.type in ("SOLIDIFY", "DISPLACE")]
        st = [m.show_viewport for m in mods]
        for m in mods:
            m.show_viewport = False
        bpy.context.view_layer.update()
        ev = ob.evaluated_get(bpy.context.evaluated_depsgraph_get())
        me = ev.to_mesh()
        co = np.zeros(len(me.vertices) * 3, np.float32)
        me.vertices.foreach_get("co", co)
        mw = np.array(ev.matrix_world)
        pts.append(co.reshape(-1, 3) @ mw[:3, :3].T + mw[:3, 3])
        ev.to_mesh_clear()
        for m, s in zip(mods, st):
            m.show_viewport = s
    bpy.context.view_layer.update()
    del dg
    return np.concatenate(pts)


def pose(clip, frame, rots, ground_obs):
    arm.location.z = 0.0
    arm.animation_data_create()
    arm.animation_data.action = bpy.data.actions[clip]
    scene.frame_set(frame)
    bases = {pb.name: pb.matrix_basis.copy() for pb in arm.pose.bones}
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.matrix_basis = bases[pb.name]
    bpy.context.view_layer.update()
    for bone, axis, deg in rots:
        pb = arm.pose.bones[bone]
        head = pb.matrix.translation.copy()
        rot = Matrix.Translation(head) @ Matrix.Rotation(math.radians(deg), 4, Vector(axis)) @ Matrix.Translation(-head)
        pb.matrix = rot @ pb.matrix
        bpy.context.view_layer.update()
    zmin = float(posed_points(ground_obs)[:, 2].min())
    arm.location.z -= zmin
    bpy.context.view_layer.update()


def rest_pose():
    arm.location.z = 0.0
    if arm.animation_data:
        arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.matrix_basis.identity()
    scene.frame_set(0)
    bpy.context.view_layer.update()


def view_dir(az, el):
    a, e = math.radians(az), math.radians(el)
    return Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))


def key_light_for(d):
    fwd = -d
    right = fwd.cross(Vector((0, 0, 1))).normalized()
    up = right.cross(fwd).normalized()
    return (-right * 0.55 + up * 0.75 - fwd * 0.55).normalized()


def set_light(d):
    for m in (MAT, CLAY):
        n = m.node_tree.nodes["LIGHT_DIR"]
        for i in range(3):
            n.inputs[i].default_value = d[i]
    toon.set_light(OLDMAT, d)


def render(name):
    scene.render.filepath = os.path.join(OUT, name + ".png")
    bpy.ops.render.render(write_still=True)
    log("rendered", name, scene.render.resolution_x, scene.render.resolution_y)


def frame_ortho(pts, d, height, pad=0.05):
    cam_data.type = "ORTHO"
    scene.render.use_border = False
    center = Vector(pts.mean(0).tolist())
    cam.location = center + d * 12.0
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    bpy.context.view_layer.update()
    inv = np.array(cam.matrix_world.inverted())
    cp = pts @ inv[:3, :3].T + inv[:3, 3]
    w, h = cp[:, 0].max() - cp[:, 0].min() + pad, cp[:, 1].max() - cp[:, 1].min() + pad
    cx, cy = (cp[:, 0].max() + cp[:, 0].min()) / 2, (cp[:, 1].max() + cp[:, 1].min()) / 2
    cam.location = cam.matrix_world @ Vector((cx, cy, 0))
    if height * w / h > 1024:
        height = int(1024 * h / w)
    scene.render.resolution_y = height
    scene.render.resolution_x = max(16, int(round(height * w / h)))
    cam_data.ortho_scale = max(w, h)
    bpy.context.view_layer.update()


def shield_turn(deg):
    return [("hand.l", (0, 0, 1), deg)]


def cleaver_turn(deg):
    return [("hand.r", (0, 0, 1), deg)]


POSES = {     # the v7 bible framings (render_warrior.py), unchanged, so v7 and the new model stand the same way
    "front": ("Idle", 1, [], 8, 6, "all"),
    "three-quarter": ("Idle", 1, shield_turn(-60) + cleaver_turn(-50), -35, 6, "all"),
    "profile": ("Idle", 1, [("upperarm.l", (1, 0, 0), -10)] + shield_turn(-50), -90, 4, "all"),
    "back": ("Idle", 1, [], 180, 6, "all"),
    "face": ("Idle", 1, [], -10, 4, "head"),
}
set_team = lambda crimson: setattr(MAT.node_tree.nodes["TEAM"].outputs[0], "default_value", 1.0 if crimson else 0.0)


def bible_shot(name, which, line, height, tag, crimson=False):
    clip, frame, rots, az, el, fit = POSES[name]
    obs = NEW if which in ("new", "high") else OLD
    pose(clip, frame, rots, [obs[0]])
    d = view_dir(az, el)
    pts = posed_points(obs if fit == "all" else [obs[0]])
    if fit == "head":
        pts = pts[pts[:, 2] > 1.24]
    frame_ortho(pts, d, height, pad=THICK_BIBLE * 2 + 0.02)
    set_light(key_light_for(d))
    set_team(crimson)
    toon.set_team(OLDMAT, False)
    set_hull({"thin": THIN_BIBLE, "thick": THICK_BIBLE}.get(line, 0.0))
    show(which, line)
    render(tag)


# ------------------------------------------------------------------------------------------ deformation check
def deform_stats():
    out = {}
    rest_pose()
    rest = {o.name: posed_points([o]) for o in NEW}
    edges = {}
    for o in NEW:
        e = np.zeros(len(o.data.edges) * 2, np.int32)
        o.data.edges.foreach_get("vertices", e)
        edges[o.name] = e.reshape(-1, 2)
    for clip, fr in (("Idle", 1), ("Walking_A", 0.25), ("Walking_A", 0.75), ("1H_Melee_Attack_Chop", 0.45), ("Running_A", 0.3)):
        act = bpy.data.actions[clip]
        f = fr if fr >= 1 else int(round(act.frame_range[0] + (act.frame_range[1] - act.frame_range[0]) * fr))
        pose(clip, f, [], [NEW[0]])
        row = {}
        for o in NEW:
            p = posed_points([o])
            e = edges[o.name]
            l0 = np.linalg.norm(rest[o.name][e[:, 0]] - rest[o.name][e[:, 1]], axis=1)
            l1 = np.linalg.norm(p[e[:, 0]] - p[e[:, 1]], axis=1)
            r = l1 / np.maximum(l0, 1e-6)
            ok = l0 > 2e-3
            r = r[ok]
            row[o.name] = {"edge_ratio_p01": round(float(np.percentile(r, 1)), 3), "edge_ratio_p99": round(float(np.percentile(r, 99)), 3),
                           "edge_ratio_max": round(float(r.max()), 3), "stretched_over_1.5": int((r > 1.5).sum()), "edges": int(len(r))}
            if o is NEW[0]:
                bad = e[ok][r > 1.5]
                gids = BODY_GID[bad[:, 0]]
                row[o.name]["stretched_by_material"] = {GROUPS[g]: int((gids == g).sum()) for g in np.unique(gids)}
        out[f"{clip}@{f}"] = row
    return out


# ------------------------------------------------------------------------------------------ renders
H = 900
for nm in ("front", "three-quarter", "profile", "back", "face"):
    bible_shot(nm, "new", "thin", H, f"new_{nm}")
bible_shot("front", "new", "none", H, "new_front_line_none")
bible_shot("front", "new", "thick", H, "new_front_line_thick")
bible_shot("face", "new", "none", H, "new_face_line_none")
bible_shot("front", "new", "thin", 600, "new_front_crimson", crimson=True)
bible_shot("front", "old", "thick", H, "v7_front")
bible_shot("face", "old", "thick", H, "v7_face")
if HIGH:                                       # the sculpt, rest pose, clay
    rest_pose()
    dd = view_dir(8, 6)
    frame_ortho(posed_points(HIGH), dd, 800, pad=0.04)
    set_light(key_light_for(dd))
    show("high")
    render("sculpt_rest")

# gameplay camera: 1080p pixel scale, cropped 300 x 300 round the unit (never a full 1080p frame)
cam_data.type = "PERSP"
cam_data.sensor_fit = "VERTICAL"
cam_data.angle_y = math.radians(CAM_FOV)
cam_data.clip_start, cam_data.clip_end = 1.0, 200.0
scene.render.resolution_x, scene.render.resolution_y = 1920, 1080
scene.render.use_border = True
scene.render.use_crop_to_border = True
bw, bh = 300 / 1920, 300 / 1080
scene.render.border_min_x, scene.render.border_max_x = 0.5 - bw / 2, 0.5 + bw / 2
scene.render.border_min_y, scene.render.border_max_y = 0.5 - bh * 0.30, 0.5 + bh * 0.70
m_per_px = 2 * (CAM_DIST - 0.8) * math.tan(math.radians(CAM_FOV / 2)) / 1080
d = view_dir(0.0, CAM_PITCH)
cam.location = d * CAM_DIST
cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
fwd_h = Vector((-d.x, -d.y, 0)).normalized()
right_h = fwd_h.cross(Vector((0, 0, 1))).normalized()
sun = (fwd_h - right_h).normalized() * math.cos(math.radians(50))
sun.z = math.sin(math.radians(50))
set_light(sun.normalized())
set_team(False)
for which, line, tag in (("new", "none", "game_new_none"), ("new", "thin", "game_new_thin"), ("new", "thick", "game_new_thick"),
                         ("old", "thick", "game_v7")):
    pose("Idle", 1, [], [(NEW if which == "new" else OLD)[0]])
    set_hull({"thin": THIN_PX * m_per_px, "thick": THICK_PX * m_per_px}.get(line, 0.0))
    for hmod in old_hulls:
        hmod.thickness = THICK_PX * m_per_px
    show(which, line)
    render(tag)
for hmod in old_hulls:
    hmod.thickness = THICK_BIBLE
from bpy_extras.object_utils import world_to_camera_view  # noqa: E402
pts = posed_points([NEW[0]])
top = pts[pts[:, 2].argmax()]
yt = world_to_camera_view(scene, cam, Vector(top.tolist())).y * 1080
yf = world_to_camera_view(scene, cam, Vector((float(top[0]), float(top[1]), 0.0))).y * 1080
REPORT["game_feet_to_top_px"] = round(yt - yf, 1)

# animation frames: walk and attack, three-quarter view
for clip, fr, tag in (("Walking_A", 0.25, "anim_walk"), ("1H_Melee_Attack_Chop", 0.45, "anim_chop"), ("Running_A", 0.3, "anim_run")):
    act = bpy.data.actions[clip]
    f = int(round(act.frame_range[0] + (act.frame_range[1] - act.frame_range[0]) * fr))
    pose(clip, f, [], [NEW[0]])
    dd = view_dir(-35, 8)
    frame_ortho(posed_points(NEW), dd, 640, pad=0.06)
    set_light(key_light_for(dd))
    set_hull(THIN_BIBLE)
    show("new", "thin")
    render(tag)
REPORT["deform"] = deform_stats()

# ------------------------------------------------------------------------------------------ export
rest_pose()
show("new", "none")
fbx = os.path.join(EXP, "WarriorLD.fbx")
for o in NEW:
    o.data.materials.clear()
    o.data.materials.append(MAT)
export.export_fbx(fbx, arm, NEW)
REPORT["fbx"] = {"path": fbx, "bytes": os.path.getsize(fbx), "options": {k: (sorted(v) if isinstance(v, set) else v) for k, v in export.FBX_OPTS.items()}}
# facing proof: the nose (lowest Blender Y on the head) through the exporter's axis matrix
from bpy_extras.io_utils import axis_conversion  # noqa: E402
conv = axis_conversion(to_forward="-Z", to_up="Y").to_4x4()
nose = Vector((0.0, -1.0, 0.0))
left = Vector((1.0, 0.0, 0.0))
REPORT["facing"] = {"blender_forward": [0, -1, 0], "fbx_forward": [round(c, 3) for c in (conv @ nose)],
                    "fbx_character_left": [round(c, 3) for c in (conv @ left)], "fbx_up": [round(c, 3) for c in (conv @ Vector((0, 0, 1)))]}
REPORT["weights_total_tris"] = sum(REPORT[o.name]["triangles"] for o in NEW)
REPORT["bones"] = len(arm.data.bones)
with open(os.path.join(OUT, "assemble_report.json"), "w") as fh:
    json.dump(REPORT, fh, indent=1)
bpy.ops.wm.read_factory_settings(use_empty=True)
try:
    bpy.ops.wm.fbx_import(filepath=fbx)
except Exception as ex:
    log("wm.fbx_import failed", repr(ex)[:200])
    bpy.ops.import_scene.fbx(filepath=fbx)
rt = {"objects": {}, "armature_bones": 0}
for o in bpy.data.objects:
    if o.type == "MESH":
        o.data.calc_loop_triangles()
        co = np.zeros(len(o.data.vertices) * 3, np.float32)
        o.data.vertices.foreach_get("co", co)
        mw = np.array(o.matrix_world)
        wc = co.reshape(-1, 3) @ mw[:3, :3].T + mw[:3, 3]
        rt["objects"][o.name] = {"tris": len(o.data.loop_triangles), "uv_layers": len(o.data.uv_layers), "vertex_groups": len(o.vertex_groups),
                                 "custom_normals": o.data.has_custom_normals, "bbox_min": [round(float(x), 3) for x in wc.min(0)],
                                 "bbox_max": [round(float(x), 3) for x in wc.max(0)], "materials": [m.name for m in o.data.materials if m]}
    elif o.type == "ARMATURE":
        rt["armature_bones"] = len(o.data.bones)
REPORT["fbx_round_trip"] = rt
with open(os.path.join(OUT, "assemble_report.json"), "w") as fh:
    json.dump(REPORT, fh, indent=1)
log("DONE")
print("LD_REPORT " + json.dumps({k: v for k, v in REPORT.items() if k != "fbx"})[:6000])
