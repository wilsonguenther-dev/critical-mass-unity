"""lineup-3d: the HY Warrior, Farmer, Miner (match meshes, Idle) and Kaiju adult (match mesh, Idle) at game scale, gameplay camera.

  cm-heavy blender ... -b --factory-startup -P lineup.py -- <out.png> <name>=<work .blend>:<rig>:<mesh>:<idle action> ...
"""
import math
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
OUT = argv[0]
ITEMS = [a.split("=", 1) for a in argv[1:]]
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
x = 0.0
GAP = {"Kaiju": 4.2}
placed = []
for name, spec in ITEMS:
    path, rig_name, mesh_name, idle = spec.split(":")
    with bpy.data.libraries.load(path, link=False) as (src, dst):
        dst.objects = [rig_name, mesh_name]
        dst.actions = [idle]
    rig, mesh = dst.objects
    for o in (rig, mesh):
        scene.collection.objects.link(o)
        o.hide_viewport = o.hide_render = False
    for md in mesh.modifiers:
        if md.type == "ARMATURE":
            md.object = rig
    rig.animation_data_create()
    act = dst.actions[0]
    rig.animation_data.action = act
    if len(act.slots):
        rig.animation_data.action_slot = act.slots[0]
    w = GAP.get(name, 1.6)
    x += w / 2
    rig.location.x = x
    x += w / 2
    placed.append((name, rig, mesh))
scene.frame_set(int(sum(a.frame_range) / 2) if False else 10)
cx = x / 2
for _, rig, _ in placed:
    rig.location.x -= cx
gm = bpy.data.meshes.new("Ground")
gm.from_pydata([(-20, -20, 0), (20, -20, 0), (20, 20, 0), (-20, 20, 0)], [], [(0, 1, 2, 3)])
ga = gm.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
for d in ga.data:
    d.color_srgb = (0.47, 0.49, 0.45, 0.0)
scene.collection.objects.link(bpy.data.objects.new("Ground", gm))
scene.render.engine = "BLENDER_WORKBENCH"
sh = scene.display.shading
sh.light, sh.color_type = "STUDIO", "VERTEX"
sh.show_object_outline = True
sh.object_outline_color = (0x18 / 255, 0x23 / 255, 0x3B / 255)
scene.view_settings.view_transform = "Standard"
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
scene.collection.objects.link(cam)
scene.camera = cam
cam.data.sensor_fit = "VERTICAL"
cam.data.angle_y = math.radians(35.0)
cam.data.clip_end = 300
a, e = math.radians(0.0), math.radians(55.0)       # yaw 0 so the row reads left to right; pitch, FOV, distance as in game
d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
tgt = Vector((0, 0, 1.2))
cam.location = tgt + d * 23.4
cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
scene.render.resolution_x, scene.render.resolution_y = 1920, 1080
scene.render.use_border = True
scene.render.use_crop_to_border = True
scene.render.border_min_x, scene.render.border_max_x = 0.30, 0.70
scene.render.border_min_y, scene.render.border_max_y = 0.30, 0.80
scene.render.filepath = OUT
bpy.ops.render.render(write_still=True)
print("LINEUP", OUT, [n for n, _, _ in placed])
