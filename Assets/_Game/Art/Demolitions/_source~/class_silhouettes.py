"""Black-fill silhouettes of every built class at the gameplay camera, 8 facings, KayKit Idle frame 1, all registered
in the same 1920x1080 frame (character at the origin), for the silhouette-overlap check in compare_demolitions.py.

  cm-heavy blender ... Blender -b --factory-startup --python-exit-code 1 -P class_silhouettes.py -- <out_dir> \
      Warrior=<Warrior.fbx> Farmer=<Farmer.fbx> Miner=<Miner.fbx>

The Demolitions body comes from demolitions.blend (which also supplies the Idle action, applied by bone name to every
imported FBX rig; the KayKit joint names are shared). Held items are included (they are part of the gameplay read);
the Miner's hidden hammer state is excluded. Writes <out_dir>/sil_<Class>_<dir>.png and sil_widths.json.
"""
import json
import math
import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
argv = sys.argv[sys.argv.index("--") + 1:]
OUT = os.path.abspath(argv[0])
OTHERS = dict(a.split("=", 1) for a in argv[1:])
os.makedirs(OUT, exist_ok=True)
PITCH, FOV, DIST, YAW = 55.0, 35.0, 23.4, 45.0
FACINGS = {"N": 135, "NE": 90, "E": 45, "SE": 0, "S": -45, "SW": -90, "W": -135, "NW": 180}
SKIP = {"Miner_PickHammer_Hammer"}

bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE, "demolitions.blend"))
scene = bpy.context.scene
for c in list(bpy.data.collections):
    if c.name == "DemolitionsProps":
        for o in list(c.objects):
            bpy.data.objects.remove(o, do_unlink=True)
idle = bpy.data.actions["Idle"]
classes = {"Demolitions": bpy.data.objects["Rig"]}
for name, path in OTHERS.items():
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    arm = [o for o in new if o.type == "ARMATURE"][0]
    classes[name] = arm
    print("IMPORTED", name, [o.name for o in new], tuple(round(v, 3) for v in arm.rotation_euler))
blk = bpy.data.materials.new("BLK")
for arm in classes.values():
    arm.rotation_mode = "XYZ"
    arm.animation_data_create()
    arm.animation_data.action = idle
    if len(idle.slots):
        arm.animation_data.action_slot = idle.slots[0]
scene.frame_set(1)

scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "FLAT"
scene.display.shading.color_type = "SINGLE"
scene.display.shading.single_color = (0, 0, 0)
scene.display.shading.show_object_outline = False
scene.render.film_transparent = True
scene.render.resolution_x, scene.render.resolution_y = 1920, 1080
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.view_settings.view_transform = "Standard"
cam_data = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
cam_data.sensor_fit = "VERTICAL"
cam_data.angle = math.radians(FOV)
p, y = math.radians(PITCH), math.radians(YAW)
off = Vector((-math.sin(y) * math.cos(p), -math.cos(y) * math.cos(p), math.sin(p))) * DIST
cam.location = Vector((0, 0, 0.9)) + off
cam.rotation_euler = (-off).to_track_quat("-Z", "Y").to_euler()


def meshes_of(arm):
    return [o for o in bpy.data.objects if o.type == "MESH" and o.parent is arm and o.name not in SKIP]


widths = {}
for name, arm in classes.items():
    for other, a2 in classes.items():
        for o in meshes_of(a2):
            o.hide_render = other != name
            for m in o.modifiers:
                if m.name == "Outline":
                    m.show_render = False
    base = arm.rotation_euler.z
    # Idle body width (x extent at facing 0, held items excluded where they are separate objects)
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    body = max(meshes_of(arm), key=lambda o: len(o.data.vertices))
    ev = body.evaluated_get(dg)
    me = ev.to_mesh()
    xs = [(ev.matrix_world @ v.co).x for v in me.vertices]
    ys = [(ev.matrix_world @ v.co).y for v in me.vertices]
    widths[name] = {"body_object": body.name, "idle_width_x": round(max(xs) - min(xs), 3),
                    "idle_depth_y": round(max(ys) - min(ys), 3)}
    ev.to_mesh_clear()
    # lean measure: rest pose (T-pose, arms out of the way), body vertices between z 0.45 and 0.90, |y| < 0.35 (packs
    # and crates on the back excluded): the hip-to-waist width of the figure
    arm.data.pose_position = "REST"
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    ev = body.evaluated_get(dg)
    me = ev.to_mesh()
    ws = [ev.matrix_world @ v.co for v in me.vertices]
    tx = [w.x for w in ws if 0.45 <= w.z <= 0.90 and abs(w.y) < 0.35]
    widths[name]["rest_hip_waist_width_z0.45_0.90"] = round(max(tx) - min(tx), 3)
    ev.to_mesh_clear()
    arm.data.pose_position = "POSE"
    bpy.context.view_layer.update()
    for d, deg in FACINGS.items():
        arm.rotation_euler.z = base + math.radians(deg)
        bpy.context.view_layer.update()
        scene.render.filepath = os.path.join(OUT, f"sil_{name}_{d}.png")
        bpy.ops.render.render(write_still=True)
    arm.rotation_euler.z = base
with open(os.path.join(OUT, "sil_widths.json"), "w") as f:
    json.dump(widths, f, indent=1)
print("WIDTHS " + json.dumps(widths))
