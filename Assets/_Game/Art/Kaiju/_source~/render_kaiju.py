"""Kaiju render set: gameplay-camera shots and orthographic bible framings (CM/Toon preview).

    /Applications/Blender.app/Contents/MacOS/Blender -b "Assets/_Game/Art/Kaiju/_source~/Kaiju.blend" \
        --python-exit-code 1 --python "Assets/_Game/Art/Kaiju/_source~/render_kaiju.py" -- <out_dir> <warrior.fbx> [job ...]

Preview of the CM/Toon contract (the Unity shader is the real thing):
  * colour from vertex colour "Col"; alpha 0.75 = team glow: self-lit, out = allegiance glow x (max(rgb)/max(default))
  * three bands from N.L against the key light: > 0.35 lit x1.00, -0.05..0.35 mid x0.84, below (or in cast shadow)
    x0.66 mixed 12 % toward Void Navy (ART-DIRECTION 2.2); hard-edged cast shadows from one sun
  * ink: inverted hull (displaced, flipped copy, back-face culled) at 2.0 px on everything but team glow, plus the
    outer silhouette taken to 3.0 px in compose_kaiju.py from a mask pass (+1 px dilation). Glow and crystals unlined.
Gameplay camera (ART-DIRECTION 2.1): perspective, pitch 55, vertical FOV 35, 23.4 u to the target, yaw 45, sun from
screen upper-left at 50 degrees elevation, plain mid-grey ground. Writes <job>.png (+ <job>_mask.png for gameplay
shots) and meta.json (screen boxes per object, pixel scale) into out_dir.
"""
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector
from bpy_extras.object_utils import world_to_camera_view

HERE = os.path.dirname(os.path.abspath(bpy.data.filepath))
sys.path.insert(0, HERE)
import kaiju_palette as PAL  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:]
OUT, WARRIOR = argv[0], argv[1]
ONLY = set(argv[2:])
os.makedirs(OUT, exist_ok=True)
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "None"
scene.render.resolution_percentage = 100
try:
    scene.eevee.taa_render_samples = 8
    scene.eevee.shadow_resolution_scale = 1.0
except AttributeError:
    pass
world = bpy.data.worlds.new("Dark")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.0
scene.world = world
NAVY = (*PAL.lin(PAL.VOID_NAVY), 1.0)


def node(nt, t, **kw):
    n = nt.nodes.new(t)
    for k, v in kw.items():
        setattr(n, k, v)
    return n


def math_node(nt, op, a, b=None, c=None):
    m = node(nt, "ShaderNodeMath", operation=op)
    for i, v in enumerate((a, b, c)):
        if v is None:
            continue
        if isinstance(v, (int, float)):
            m.inputs[i].default_value = v
        else:
            nt.links.new(v, m.inputs[i])
    return m.outputs[0]


def band_factor(nt, L_node):
    """Returns (lit_factor_socket 1/0.84/0.66, shadow_mask_socket 1 in the shadow band) from N.L and cast shadow."""
    geo = node(nt, "ShaderNodeNewGeometry")
    dot = node(nt, "ShaderNodeVectorMath", operation="DOT_PRODUCT")
    nt.links.new(geo.outputs["Normal"], dot.inputs[0])
    nt.links.new(L_node.outputs[0], dot.inputs[1])
    ndl = dot.outputs["Value"]
    dif = node(nt, "ShaderNodeBsdfDiffuse")
    s2r = node(nt, "ShaderNodeShaderToRGB")
    nt.links.new(dif.outputs[0], s2r.inputs[0])
    bw = node(nt, "ShaderNodeRGBToBW")
    nt.links.new(s2r.outputs["Color"], bw.inputs[0])
    # lit fraction = received / expected; < 0.5 where N.L > 0 means a cast shadow
    expected = math_node(nt, "MAXIMUM", ndl, 0.02)
    ratio = math_node(nt, "DIVIDE", bw.outputs[0], expected)
    inshadow = math_node(nt, "LESS_THAN", ratio, 0.5)
    v = math_node(nt, "SUBTRACT", ndl, math_node(nt, "MULTIPLY", inshadow, 2.0))   # shadowed -> deep in the shadow band
    is_lit = math_node(nt, "GREATER_THAN", v, 0.35)
    is_mid = math_node(nt, "MULTIPLY", math_node(nt, "GREATER_THAN", v, -0.05), math_node(nt, "SUBTRACT", 1.0, is_lit))
    is_sh = math_node(nt, "SUBTRACT", math_node(nt, "SUBTRACT", 1.0, is_lit), is_mid)
    f = math_node(nt, "ADD", math_node(nt, "ADD", is_lit, math_node(nt, "MULTIPLY", is_mid, 0.84)),
                  math_node(nt, "MULTIPLY", is_sh, 0.66))
    return f, is_sh


def toon_material():
    m = bpy.data.materials.get(PAL.MATERIAL)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    col = node(nt, "ShaderNodeVertexColor", layer_name="Col")
    L = node(nt, "ShaderNodeCombineXYZ", name="LIGHT")
    team = node(nt, "ShaderNodeRGB", name="TEAM")
    ref = node(nt, "ShaderNodeValue", name="REF")
    f, is_sh = band_factor(nt, L)
    # fixed colour: rgb * f, shadow band mixed 12 % toward navy
    mul = node(nt, "ShaderNodeMix", data_type="RGBA", blend_type="MULTIPLY")
    mul.inputs["Factor"].default_value = 1.0
    nt.links.new(col.outputs["Color"], mul.inputs[6])
    fc = node(nt, "ShaderNodeCombineColor")
    for i in range(3):
        nt.links.new(f, fc.inputs[i])
    nt.links.new(fc.outputs[0], mul.inputs[7])
    navy = node(nt, "ShaderNodeMix", data_type="RGBA", blend_type="MIX")
    nt.links.new(math_node(nt, "MULTIPLY", is_sh, 0.12), navy.inputs["Factor"])
    nt.links.new(mul.outputs[2], navy.inputs[6])
    navy.inputs[7].default_value = NAVY
    # team glow: team * max(rgb) / REF
    sep = node(nt, "ShaderNodeSeparateColor")
    nt.links.new(col.outputs["Color"], sep.inputs[0])
    mx = math_node(nt, "MAXIMUM", math_node(nt, "MAXIMUM", sep.outputs[0], sep.outputs[1]), sep.outputs[2])
    k = math_node(nt, "DIVIDE", mx, ref.outputs[0])
    tg = node(nt, "ShaderNodeMix", data_type="RGBA", blend_type="MULTIPLY")
    tg.inputs["Factor"].default_value = 1.0
    nt.links.new(team.outputs[0], tg.inputs[6])
    kc = node(nt, "ShaderNodeCombineColor")
    for i in range(3):
        nt.links.new(k, kc.inputs[i])
    nt.links.new(kc.outputs[0], tg.inputs[7])
    is_team = math_node(nt, "COMPARE", col.outputs["Alpha"], 0.75, 0.06)
    out_mix = node(nt, "ShaderNodeMix", data_type="RGBA", blend_type="MIX")
    nt.links.new(is_team, out_mix.inputs["Factor"])
    nt.links.new(navy.outputs[2], out_mix.inputs[6])
    nt.links.new(tg.outputs[2], out_mix.inputs[7])
    em = node(nt, "ShaderNodeEmission")
    nt.links.new(out_mix.outputs[2], em.inputs[0])
    o = node(nt, "ShaderNodeOutputMaterial")
    nt.links.new(em.outputs[0], o.inputs[0])
    return m


def flat_material(name, hexs, shaded=True):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    rgb = node(nt, "ShaderNodeRGB")
    rgb.outputs[0].default_value = (*PAL.lin(hexs), 1.0)
    em = node(nt, "ShaderNodeEmission")
    if shaded:   # ground: one band plus cast shadows in the shadow band
        L = node(nt, "ShaderNodeCombineXYZ", name="LIGHT")
        f, is_sh = band_factor(nt, L)
        mul = node(nt, "ShaderNodeMix", data_type="RGBA", blend_type="MIX")
        nt.links.new(is_sh, mul.inputs["Factor"])
        nt.links.new(rgb.outputs[0], mul.inputs[6])
        c = PAL.lin(hexs)
        mul.inputs[7].default_value = (*[(x * 0.66) * 0.88 + n * 0.12 for x, n in zip(c, NAVY[:3])], 1.0)
        nt.links.new(mul.outputs[2], em.inputs[0])
    else:
        nt.links.new(rgb.outputs[0], em.inputs[0])
    o = node(nt, "ShaderNodeOutputMaterial")
    nt.links.new(em.outputs[0], o.inputs[0])
    return m


TOON = toon_material()
OUTLINE = flat_material("Outline", PAL.VOID_NAVY, shaded=False)
OUTLINE.use_backface_culling = True
GROUND = flat_material("Ground", PAL.GROUND_GREY)
DECAL = flat_material("TelegraphDecal", PAL.TELEGRAPH_RED, shaded=False)
LIGHT_NODES = [TOON.node_tree.nodes["LIGHT"], GROUND.node_tree.nodes["LIGHT"]]

# ------------------------------------------------------------------------------------------------ scene objects
STAGES = {}
for ob in list(bpy.data.objects):
    if ob.type == "MESH" and "cm_stage" in ob:
        STAGES[ob["cm_stage"]] = ob
        if ob.parent:
            ob.parent.location = (0, 0, 0)


def make_hull(ob):
    """Inverted hull: a copy that deforms with the same armature, minus team-glow faces, pushed out along the
    normals by WIDTH metres (set per job), normals flipped, back faces culled. Never exported."""
    me = ob.data.copy()
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(me)
    lay = bm.faces.layers.int.get("cm_slot")
    team_idx = {i for i, s in enumerate(PAL.SLOTS) if s in PAL.TEAM_SLOTS}
    if lay is not None:
        dead = [f for f in bm.faces if f[lay] in team_idx]
        bmesh.ops.delete(bm, geom=dead, context="FACES")
    bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
    bm.to_mesh(me)
    bm.free()
    me.materials.clear()
    me.materials.append(OUTLINE)
    h = bpy.data.objects.new(ob.name + "_Hull", me)
    scene.collection.objects.link(h)
    h.parent = ob.parent
    h.matrix_parent_inverse = ob.matrix_parent_inverse.copy()
    for vg in ob.vertex_groups:
        h.vertex_groups.new(name=vg.name)
    # vertex groups were copied with the mesh data (weights live on the mesh)
    for mod in ob.modifiers:
        if mod.type == "ARMATURE":
            a = h.modifiers.new("Armature", "ARMATURE")
            a.object = mod.object
    d = h.modifiers.new("Push", "DISPLACE")
    d.direction = "NORMAL"
    d.mid_level = 0.0
    d.strength = -0.05
    h.visible_shadow = False
    return h


HULLS = {k: make_hull(o) for k, o in STAGES.items()}
for o in STAGES.values():
    o.visible_shadow = True

# warrior for scale (2.0 u, branch art/warrior-blender), toon material, own hull
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=WARRIOR)
W_OBJS = [o for o in bpy.data.objects if o not in before]
W_ROOT = next(o for o in W_OBJS if o.parent is None)
W_MESHES = [o for o in W_OBJS if o.type == "MESH"]
for o in W_MESHES:
    o.data.materials.clear()
    o.data.materials.append(TOON)
W_HULLS = []
for o in W_MESHES:
    hm = o.data.copy()
    hm.materials.clear()
    hm.materials.append(OUTLINE)
    import bmesh
    bm = bmesh.new(); bm.from_mesh(hm); bmesh.ops.reverse_faces(bm, faces=bm.faces[:]); bm.to_mesh(hm); bm.free()
    h = bpy.data.objects.new(o.name + "_Hull", hm)
    scene.collection.objects.link(h)
    h.parent, h.matrix_parent_inverse = o.parent, o.matrix_parent_inverse.copy()
    h.matrix_world = o.matrix_world.copy()
    for mod in o.modifiers:
        if mod.type == "ARMATURE":
            a = h.modifiers.new("Armature", "ARMATURE"); a.object = mod.object
    d = h.modifiers.new("Push", "DISPLACE"); d.direction = "NORMAL"; d.mid_level = 0.0
    h.visible_shadow = False
    W_HULLS.append(h)

ground_me = bpy.data.meshes.new("Ground")
ground_me.from_pydata([(-60, -60, 0), (60, -60, 0), (60, 60, 0), (-60, 60, 0)], [], [(0, 1, 2, 3)])
ground_me.materials.append(GROUND)
GROUND_OB = bpy.data.objects.new("Ground", ground_me)
scene.collection.objects.link(GROUND_OB)


def ring_decal(r0, r1, z, name, seg=96):
    V, F = [], []
    for i in range(seg):
        a = math.tau * i / seg
        V += [(math.cos(a) * r0, math.sin(a) * r0, z), (math.cos(a) * r1, math.sin(a) * r1, z)]
    for i in range(seg):
        j = (i + 1) % seg
        F.append((2 * i, 2 * j, 2 * j + 1, 2 * i + 1))
    me = bpy.data.meshes.new(name)
    me.from_pydata(V, [], F)
    me.materials.append(DECAL)
    ob = bpy.data.objects.new(name, me)
    scene.collection.objects.link(ob)
    ob.visible_shadow = False
    return ob


DECALS = [ring_decal(4.75, 5.0, 0.01, "TeleRingOuter"), ring_decal(3.2, 3.32, 0.01, "TeleRingMid"),
          ring_decal(1.7, 1.8, 0.01, "TeleRingInner")]

sun_data = bpy.data.lights.new("Key", "SUN")
sun_data.energy = math.pi
sun_data.angle = 0.0
sun_data.use_shadow = True
sun = bpy.data.objects.new("Key", sun_data)
scene.collection.objects.link(sun)
cam_data = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam


def set_light(src):
    """src: unit vector toward the light."""
    src = Vector(src).normalized()
    sun.rotation_mode = "QUATERNION"
    sun.rotation_quaternion = (-src).to_track_quat("-Z", "Y")
    for nd in LIGHT_NODES:
        for i in range(3):
            nd.inputs[i].default_value = src[i]


def set_team(stage_default, alleg):
    TOON.node_tree.nodes["TEAM"].outputs[0].default_value = (*PAL.lin(PAL.GLOW[alleg]), 1.0)
    TOON.node_tree.nodes["REF"].outputs[0].default_value = max(PAL.lin(PAL.GLOW[stage_default]))


def set_pose(ob, clip, phase):
    rig = ob.parent
    if rig is None or rig.animation_data is None:
        return
    ad = rig.animation_data
    for tr in ad.nla_tracks:
        tr.mute = True
    if clip is None:
        ad.action = None
        for pb in rig.pose.bones:
            pb.rotation_quaternion = (1, 0, 0, 0)
            pb.location = (0, 0, 0)
        return
    act = ad.nla_tracks[clip].strips[0].action
    ad.action = act
    if hasattr(ad, "action_slot") and ad.action_slot is None and len(getattr(act, "slots", [])):
        ad.action_slot = act.slots[0]
    f0, f1 = act.frame_range
    scene.frame_set(int(round(f0 + phase * (f1 - f0))))


def place(ob, loc, yaw_deg):
    tgt = ob.parent if ob.parent else ob
    tgt.location = loc
    tgt.rotation_mode = "XYZ"
    tgt.rotation_euler = (0, 0, math.radians(yaw_deg))


def show(objs):
    keep = set(objs)
    for o in list(STAGES.values()) + list(HULLS.values()) + W_MESHES + W_HULLS + DECALS:
        o.hide_render = o not in keep


def hull_width(px, px_per_m):
    w = px / px_per_m
    for k, h in HULLS.items():
        h.modifiers["Push"].strength = -w          # reversed normals point inward: push against them
    for h in W_HULLS:
        h.modifiers["Push"].strength = -w


def world_pts(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = ev.to_mesh()
    pts = [ob.matrix_world @ v.co for v in me.vertices]
    ev.to_mesh_clear()
    return pts


def screen_box(objs):
    xs, ys = [], []
    for o in objs:
        for w in world_pts(o)[::3]:
            c = world_to_camera_view(scene, cam, w)
            xs.append(c.x); ys.append(1 - c.y)
    if not xs:
        return None
    R = scene.render
    return [min(xs) * R.resolution_x, min(ys) * R.resolution_y, max(xs) * R.resolution_x, max(ys) * R.resolution_y]


# ------------------------------------------------------------------------------------------------ cameras
YAW = 45.0
F_H = Vector((math.sin(math.radians(YAW)), math.cos(math.radians(YAW)), 0))       # screen-up on the ground
R_H = Vector((F_H.y, -F_H.x, 0))                                                 # screen-right
SUN_GAME = (Vector((0, 0, 0)) + (F_H - R_H).normalized() * math.cos(math.radians(50)) + Vector((0, 0, math.sin(math.radians(50))))).normalized()


def gameplay_camera(target):
    cam_data.type = "PERSP"
    cam_data.sensor_fit = "VERTICAL"
    cam_data.angle_y = math.radians(35.0)
    cam_data.clip_start, cam_data.clip_end = 0.5, 200.0
    pitch = math.radians(55.0)
    d = (F_H * math.cos(pitch) + Vector((0, 0, -math.sin(pitch)))).normalized()   # view ray
    cam.location = Vector(target) - d * 23.4
    cam.rotation_mode = "QUATERNION"
    cam.rotation_quaternion = d.to_track_quat("-Z", "Y")
    scene.render.resolution_x, scene.render.resolution_y = 1920, 1080
    set_light(SUN_GAME)
    # px per metre at the target: 1080 / (2 * 23.4 * tan(17.5))
    return 1080 / (2 * 23.4 * math.tan(math.radians(17.5)))


def ortho_camera(objs, az, el, res=640):
    az, el = math.radians(az), math.radians(el)
    d = Vector((-math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el)))   # toward the camera
    rot = (-d).to_track_quat("-Z", "Y")
    R = rot.to_matrix()
    right, up = R.col[0], R.col[1]
    pts = [w for o in objs for w in world_pts(o)]
    xs = [w.dot(right) for w in pts]
    ys = [w.dot(up) for w in pts]
    cx, cy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
    centre = sum(pts, Vector()) / len(pts)
    cam_data.type = "ORTHO"
    cam_data.clip_start, cam_data.clip_end = 0.1, 300.0
    cam.location = right * cx + up * cy + d * (centre.dot(d) + 60.0)
    cam.rotation_mode = "QUATERNION"
    cam.rotation_quaternion = rot
    span = max(max(xs) - min(xs), max(ys) - min(ys)) * 1.08
    cam_data.ortho_scale = span
    scene.render.resolution_x = scene.render.resolution_y = res
    set_light(-(R @ Vector((0.55, -0.62, -0.56))))       # bible key light: from the frame's upper left
    return res / span


# ------------------------------------------------------------------------------------------------ jobs
# gameplay: (id, [(stage, allegiance-for-team, clip, phase, loc, yaw)], warrior loc, extra)
A_YAW = -20.0   # the adult turned a little toward the camera's left so the face and one flank read
GAME = [
    ("gp-adult-azure", [("Adult", "Azure", "Idle", 0.0, (0, 0, 0), A_YAW)], (2.9, -1.2, 0), None),
    ("gp-adult-crimson", [("Adult", "Crimson", "Idle", 0.0, (0, 0, 0), A_YAW)], (2.9, -1.2, 0), None),
    ("gp-adult-berserk", [("Adult", "Berserk", "Berserk", 0.25, (0, 0, 0), A_YAW)], (2.9, -1.2, 0), "shards"),
    ("gp-egg-hatchling", [("Egg", "Neutral", None, 0.0, (-2.4, 0.8, 0), 0.0),
                          ("Hatchling", "Neutral", "Idle", 0.0, (1.0, -0.4, 0), A_YAW)], (3.4, -2.0, 0), None),
    ("gp-roar", [("Adult", "Azure", "Roar", 0.55, (0, 0, 0), A_YAW)], (2.9, -1.2, 0), None),
    ("gp-slam-windup", [("Adult", "Azure", "SlamWindup", 1.0, (0, 0, 0), A_YAW)], (3.6, -2.2, 0), "telegraph"),
]
# bible framings (orthographic): (crop id, stage, allegiance, clip, phase, azimuth, elevation). Azimuths were tuned
# by the KJ_SWEEP silhouette search inside each page's view range (compose_kaiju.AZ_RANGE): front stays front.
BIBLE = [
    ("10-egg", "Egg", "Neutral", None, 0.0, 30, 10),
    ("10-hatchling", "Hatchling", "Neutral", "Idle", 0.0, 60, 12),
    ("10-azure-adult", "Adult", "Azure", "Idle", 0.0, 60, 6),
    ("10-crimson-adult", "Adult", "Crimson", "Idle", 0.0, 60, 6),
    ("10-berserk-adult", "Adult", "Berserk", "Berserk", 0.25, 0, 6),
    ("11-front", "Adult", "Azure", "Idle", 0.0, -15, 8),
    ("11-three-quarter", "Adult", "Azure", "Idle", 0.0, 60, 8),
    ("11-profile", "Adult", "Azure", "Idle", 0.0, 75, 8),
    ("11-rear", "Adult", "Azure", "Idle", 0.0, 195, 12),
    ("11-roar", "Adult", "Azure", "Roar", 0.55, 0, 8),
    ("11-slam-windup", "Adult", "Azure", "SlamWindup", 1.0, 45, 8),
]

meta = {}
meta_path = os.path.join(OUT, "meta.json")
if os.path.exists(meta_path):
    meta = json.load(open(meta_path))
SHARDS = STAGES.get("Kaiju_BerserkShards")
TELE = STAGES.get("Kaiju_TelegraphSpikes")

for jid, members, wloc, extra in GAME:
    if ONLY and jid not in ONLY:
        continue
    ppm = gameplay_camera((0.6, 0.0, 1.2))
    vis, kaiju = [], []
    for stage, alleg, clip, phase, loc, yaw in members:
        ob = STAGES[stage]
        place(ob, loc, yaw)
        set_pose(ob, clip, phase)
        set_team(PAL.STAGE_DEFAULT_GLOW[stage], alleg)
        vis += [ob, HULLS[stage]]
        kaiju.append(ob)
    if extra == "shards":
        place(SHARDS, (0, 0, 0), A_YAW)
        vis += [SHARDS]
        kaiju.append(SHARDS)
    if extra == "telegraph":
        place(TELE, (0, 0, 0), 0)
        vis += [TELE, HULLS["Kaiju_TelegraphSpikes"]] + DECALS
    W_ROOT.location = wloc
    W_ROOT.rotation_euler = (W_ROOT.rotation_euler.x, 0, math.radians(200))
    vis += W_MESHES + W_HULLS
    show(vis)
    hull_width(2.0, ppm)
    bpy.context.view_layer.update()
    scene.render.film_transparent = False
    GROUND_OB.hide_render = False
    GROUND_OB.is_holdout = False
    scene.render.filepath = os.path.join(OUT, jid + ".png")
    bpy.ops.render.render(write_still=True)
    # mask pass: ground as holdout, transparent film -> alpha = everything that gets the 3.0 px outer line
    GROUND_OB.is_holdout = True
    for dcl in DECALS:
        dcl.hide_render = True
    scene.render.film_transparent = True
    scene.render.filepath = os.path.join(OUT, jid + "_mask.png")
    bpy.ops.render.render(write_still=True)
    GROUND_OB.is_holdout = False
    meta[jid] = {"kind": "gameplay", "px_per_m": ppm, "kaiju_box": screen_box([k for k in kaiju if k is not SHARDS]),
                 "warrior_box": screen_box(W_MESHES), "warrior_px": None}
    f0 = world_to_camera_view(scene, cam, Vector(wloc))
    f1 = world_to_camera_view(scene, cam, Vector(wloc) + Vector((0, 0, 2.0)))
    meta[jid]["warrior_px"] = (f1.y - f0.y) * scene.render.resolution_y     # a 2.0 u character, feet to crown
    print("RENDERED", jid, "warrior px %.1f" % meta[jid]["warrior_px"])

GROUND_OB.hide_render = True
SWEEP = [float(x) for x in os.environ.get("KJ_SWEEP", "").split(",") if x]
if SWEEP:   # angle search for the bible framings: KJ_SWEEP="0,10,..." writes sweep/<job>__<az>.png at 320 px
    for jid, stage, alleg, clip, phase, az0, el in BIBLE:
        if ONLY and jid not in ONLY:
            continue
        ob = STAGES[stage]
        place(ob, (0, 0, 0), 0)
        set_pose(ob, clip, phase)
        set_team(PAL.STAGE_DEFAULT_GLOW[stage], alleg)
        show([ob, HULLS[stage]])
        bpy.context.view_layer.update()
        for az in SWEEP:
            ppm = ortho_camera([ob], az, el, res=320)
            hull_width(2.0, ppm)
            scene.render.film_transparent = True
            scene.render.filepath = os.path.join(OUT, "sweep", "%s__%g.png" % (jid, az))
            bpy.ops.render.render(write_still=True)
    sys.exit(0)
for jid, stage, alleg, clip, phase, az, el in BIBLE:
    if ONLY and jid not in ONLY:
        continue
    ob = STAGES[stage]
    place(ob, (0, 0, 0), 0)
    set_pose(ob, clip, phase)
    set_team(PAL.STAGE_DEFAULT_GLOW[stage], alleg)
    objs = [ob]
    vis = [ob, HULLS[stage]]
    if stage == "Adult" and clip == "Berserk":
        place(SHARDS, (0, 0, 0), 0)
        vis.append(SHARDS)
    show(vis)
    bpy.context.view_layer.update()
    ppm = ortho_camera(objs, az, el)
    hull_width(2.0, ppm)
    scene.render.film_transparent = True
    scene.render.filepath = os.path.join(OUT, jid + ".png")
    bpy.ops.render.render(write_still=True)
    meta[jid] = {"kind": "bible", "px_per_m": ppm}
    print("RENDERED", jid)

json.dump(meta, open(meta_path, "w"), indent=1)
