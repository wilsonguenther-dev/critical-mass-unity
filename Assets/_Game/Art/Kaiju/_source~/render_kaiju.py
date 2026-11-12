"""Parity renders of the Kaiju: bible framings plus the in-game oblique camera, per stage and allegiance.

    /Applications/Blender.app/Contents/MacOS/Blender -b "Assets/_Game/Art/Kaiju/_source~/Kaiju.blend" \
        --python-exit-code 1 --python "Assets/_Game/Art/Kaiju/_source~/render_kaiju.py" -- <out_dir> [job_id ...]

Preview only: it stands in for CM/Toon (two flat bands from the palette's lit/shade pair, unlit glow, inverted-hull
outline at the asset's cm_outline_width, one key light, no ambient, Standard view transform so colours are the
palette's). Writes <out_dir>/<job>.png with a transparent background; compose_kaiju.py builds the comparison sheets.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(bpy.data.filepath))
sys.path.insert(0, HERE)
import kaiju_palette as PAL  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0]
ONLY = set(argv[1:])
os.makedirs(OUT, exist_ok=True)
RES = 640

# (job id, stage, allegiance, clip or None, clip phase 0..1, camera azimuth deg (0 = in front, + = creature's right),
#  elevation deg). Bible framings first (ids = the reference crop names), then oblique and three-quarter per variant.
JOBS = [
    ("10-egg", "Egg", "Neutral", None, 0.0, 0, 10),
    ("10-hatchling", "Hatchling", "Neutral", "Idle", 0.0, 50, 12),
    ("10-azure-adult", "Adult", "Azure", "Idle", 0.0, 35, 6),
    ("10-crimson-adult", "Adult", "Crimson", "Idle", 0.0, 35, 6),
    ("10-berserk-adult", "Adult", "Berserk", "Berserk", 0.25, 40, 6),
    ("11-front", "Adult", "Azure", "Idle", 0.0, 0, 8),
    ("11-three-quarter", "Adult", "Azure", "Idle", 0.0, 40, 8),
    ("11-profile", "Adult", "Azure", "Idle", 0.0, 62, 8),
    ("11-rear", "Adult", "Azure", "Idle", 0.0, 168, 12),
    ("11-idle", "Adult", "Azure", "Idle", 0.25, 55, 10),
    ("11-walk", "Adult", "Azure", "Walk", 0.25, 60, 10),
    ("11-roar", "Adult", "Azure", "Roar", 0.55, 35, 8),
    ("11-slam-windup", "Adult", "Azure", "SlamWindup", 1.0, 20, 8),
]
for stage in ("Egg", "Hatchling", "Adult"):
    for a in PAL.ALLEGIANCES:
        JOBS.append(("oblique-%s-%s" % (stage.lower(), a.lower()), stage, a, "Idle" if stage != "Egg" else None, 0.0, 45, 38))
        JOBS.append(("tq-%s-%s" % (stage.lower(), a.lower()), stage, a, "Idle" if stage != "Egg" else None, 0.0, 40, 8))


def lin(hexs):
    h = hexs.lstrip("#")
    out = []
    for i in (0, 2, 4):
        c = int(h[i:i + 2], 16) / 255.0
        out.append(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4)
    return (*out, 1.0)


scene = bpy.context.scene
for eng in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
    try:
        scene.render.engine = eng
        break
    except TypeError:
        continue
scene.render.resolution_x = scene.render.resolution_y = RES
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "None"
try:
    scene.eevee.taa_render_samples = 16
except AttributeError:
    pass
world = bpy.data.worlds.new("Black")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.0
scene.world = world

# ---- toon preview materials (replace the FBX-facing Principled trees in memory only; the .blend is not saved)
TOON = {}
for s in PAL.SLOTS:
    m = bpy.data.materials[PAL.material_name(s)]
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(em.outputs[0], out.inputs[0])
    lit = nt.nodes.new("ShaderNodeRGB"); lit.name = "LIT"
    shade = nt.nodes.new("ShaderNodeRGB"); shade.name = "SHADE"
    if s == "Glow":
        nt.links.new(lit.outputs[0], em.inputs[0])
    else:
        dif = nt.nodes.new("ShaderNodeBsdfDiffuse")
        s2r = nt.nodes.new("ShaderNodeShaderToRGB")
        ramp = nt.nodes.new("ShaderNodeValToRGB")
        ramp.color_ramp.interpolation = "CONSTANT"
        ramp.color_ramp.elements[0].position = 0.0
        ramp.color_ramp.elements[0].color = (0, 0, 0, 1)
        ramp.color_ramp.elements[1].position = 0.32      # two-step ramp threshold (preview value)
        ramp.color_ramp.elements[1].color = (1, 1, 1, 1)
        mix = nt.nodes.new("ShaderNodeMix"); mix.data_type = "RGBA"
        nt.links.new(dif.outputs[0], s2r.inputs[0])
        nt.links.new(s2r.outputs["Color"], ramp.inputs["Fac"])
        nt.links.new(ramp.outputs["Color"], mix.inputs[0])
        nt.links.new(shade.outputs[0], mix.inputs[6])
        nt.links.new(lit.outputs[0], mix.inputs[7])
        nt.links.new(mix.outputs[2], em.inputs[0])
    TOON[s] = (lit, shade)

outline = bpy.data.materials.new("KJ_OutlinePreview")
outline.use_nodes = True if outline.node_tree is None else outline.use_nodes
ont = outline.node_tree
ont.nodes.clear()
oo = ont.nodes.new("ShaderNodeOutputMaterial")
oe = ont.nodes.new("ShaderNodeEmission")
oe.inputs[0].default_value = lin(PAL.OUTLINE)
ont.links.new(oe.outputs[0], oo.inputs[0])
outline.use_backface_culling = True

STAGES = {}
for ob in bpy.data.objects:
    if ob.type == "MESH" and "cm_stage" in ob:
        rig = ob.parent
        rig.location = (0, 0, 0)
        ob.data.materials.append(outline)
        sol = ob.modifiers.new("OutlineHull", "SOLIDIFY")
        sol.thickness = ob["cm_outline_width"]
        sol.offset = 1.0
        sol.use_flip_normals = True
        sol.use_rim = False
        sol.material_offset = 100
        ob.visible_shadow = False   # flat two-band look: no cast shadows (and the hull must not shadow the body)
        STAGES[ob["cm_stage"]] = (ob, rig)

sun_data = bpy.data.lights.new("Key", "SUN")
sun_data.energy = math.pi
sun_data.angle = 0.0
sun_data.use_shadow = False
sun = bpy.data.objects.new("Key", sun_data)
scene.collection.objects.link(sun)
cam_data = bpy.data.cameras.new("Cam")
cam_data.type = "ORTHO"
cam_data.clip_start, cam_data.clip_end = 0.1, 200.0
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam


def set_variant(a):
    cols = PAL.colours(a)
    for s, (lit, shade) in TOON.items():
        lit.outputs[0].default_value = lin(cols[s][0])
        shade.outputs[0].default_value = lin(cols[s][1])


def set_pose(rig, clip, phase):
    ad = rig.animation_data
    for tr in ad.nla_tracks:
        tr.mute = True
    if clip is None:
        ad.action = None
        for pb in rig.pose.bones:
            pb.rotation_quaternion = (1, 0, 0, 0)
            pb.location = (0, 0, 0)
        scene.frame_set(1)
        return
    tr = ad.nla_tracks[clip]
    act = tr.strips[0].action
    ad.action = act
    if hasattr(ad, "action_slot") and ad.action_slot is None and len(getattr(act, "slots", [])):
        ad.action_slot = act.slots[0]
    f0, f1 = act.frame_range
    scene.frame_set(int(round(f0 + phase * (f1 - f0))))


def frame_camera(ob, az, el):
    az, el = math.radians(az), math.radians(el)
    d = Vector((-math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el)))
    rot = (-d).to_track_quat("-Z", "Y")
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = ev.to_mesh()
    mw = ob.matrix_world
    R = rot.to_matrix()
    right, up = R.col[0], R.col[1]
    xs, ys, pts = [], [], []
    for v in me.vertices:
        w = mw @ v.co
        xs.append(w.dot(right)); ys.append(w.dot(up)); pts.append(w)
    ev.to_mesh_clear()
    cx, cy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
    centre = sum(pts, Vector()) / len(pts)
    cam.location = right * cx + up * cy + d * (centre.dot(d) + 60.0)
    cam.rotation_mode = "QUATERNION"
    cam.rotation_quaternion = rot
    cam_data.ortho_scale = max(max(xs) - min(xs), max(ys) - min(ys)) * 1.06
    # key light from the upper left of the frame (the bible's light), in camera space
    travel = R @ Vector((0.55, -0.62, -0.56))
    sun.rotation_mode = "QUATERNION"
    sun.rotation_quaternion = travel.to_track_quat("-Z", "Y")


for job in JOBS:
    jid, stage, alleg, clip, phase, az, el = job
    if ONLY and jid not in ONLY:
        continue
    for st, (ob, rig) in STAGES.items():
        ob.hide_render = st != stage
    ob, rig = STAGES[stage]
    set_variant(alleg)
    set_pose(rig, clip, phase)
    frame_camera(ob, az, el)
    scene.render.filepath = os.path.join(OUT, jid + ".png")
    bpy.ops.render.render(write_still=True)
    print("RENDERED", jid)
