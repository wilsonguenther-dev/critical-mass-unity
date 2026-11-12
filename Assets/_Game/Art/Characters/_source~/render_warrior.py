"""Headless EEVEE turnaround of warrior.blend with the CM/Toon preview (two-band ramp + inverted-hull outline).

  Blender -b --factory-startup --python-exit-code 1 -P render_warrior.py -- <out_dir> [height_px] [action] [frame]

Writes <out_dir>/<framing>_<team>.png (transparent background) for framings front, three-quarter, side, back and
oblique (the in-game camera: orthographic, 38 deg pitch, U-005), teams azure and crimson. Each frame is an orthographic
camera fitted tightly to the posed character, like the bible crops. The key light rides with the camera (upper left).
"""
import math
import os
import sys

sys.dont_write_bytecode = True
import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from cm_art import toon  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0]) if argv else os.path.join(HERE, "renders")
HEIGHT = int(argv[1]) if len(argv) > 1 else 720
ACTION = argv[2] if len(argv) > 2 else "Idle"
FRAME = int(argv[3]) if len(argv) > 3 else 1
STANCE = (argv[4] if len(argv) > 4 else "bible") == "bible"
# Capture pose. The bible sheets draw a wide stance with the arms held out from the body. "bible" = the KayKit clip
# frame above plus these world-axis rotations about each joint (armature space, degrees), applied parent first.
# It is a render pose only: nothing here touches the mesh, the rig rest pose or the FBX.
BIBLE_STANCE = [
    ("upperarm.l", (0, 1, 0), -10), ("upperarm.r", (0, 1, 0), 13),
    ("upperleg.l", (0, 1, 0), -9), ("upperleg.r", (0, 1, 0), 9),
    ("foot.l", (0, 1, 0), 9), ("foot.r", (0, 1, 0), -9),
]
OUTLINE_WIDTH = 0.012     # metres on a ~2.1 m figure; per-asset width (U-008)

# name: (azimuth deg, elevation deg); azimuth 0 = camera in front (-Y), +90 = camera on the character's left (+X)
FRAMINGS = {
    "front": (0, 0),
    "three-quarter": (-25, 0),   # bible 06-three-quarter: face turned to the viewer's right
    "side": (-90, 0),
    "back": (180, 0),
    "oblique": (0, 38),
}

bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE, "warrior.blend"))
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
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
arm.animation_data_create()
arm.animation_data.action = bpy.data.actions[ACTION]
scene.frame_set(FRAME)
if STANCE:
    bases = {pb.name: pb.matrix_basis.copy() for pb in arm.pose.bones}
    arm.animation_data.action = None          # freeze the clip frame so the edits below survive the render
    for pb in arm.pose.bones:
        pb.matrix_basis = bases[pb.name]
    bpy.context.view_layer.update()
    for bone, axis, deg in BIBLE_STANCE:
        pb = arm.pose.bones[bone]
        head = pb.matrix.translation.copy()
        rot = Matrix.Translation(head) @ Matrix.Rotation(math.radians(deg), 4, Vector(axis)) @ Matrix.Translation(-head)
        pb.matrix = rot @ pb.matrix
        bpy.context.view_layer.update()
for ob in meshes:
    toon.add_outline(ob, OUTLINE_WIDTH)
mat = bpy.data.materials["CM_Toon"]

cam_data = bpy.data.cameras.new("Cam")
cam_data.type = "ORTHO"
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
sun_data = bpy.data.lights.new("Key", "SUN")
sun_data.energy = 3.0
sun = bpy.data.objects.new("Key", sun_data)
scene.collection.objects.link(sun)
sun.parent = cam
# light travels from the camera's upper left, toward the subject
ld = Vector((0.55, -0.65, -1.0)).normalized()
sun.matrix_parent_inverse = Matrix.Identity(4)
sun.rotation_euler = ld.to_track_quat("-Z", "Y").to_euler()
world = bpy.data.worlds.new("W")
world.color = (0.02, 0.02, 0.03)
scene.world = world


def posed_points():
    dg = bpy.context.evaluated_depsgraph_get()
    pts = []
    for ob in meshes:
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        mw = ev.matrix_world
        pts.extend(mw @ v.co for v in me.vertices)
        ev.to_mesh_clear()
    return pts


os.makedirs(OUT, exist_ok=True)
pts = posed_points()
center = sum(pts, Vector()) / len(pts)
for name, (az, el) in FRAMINGS.items():
    a, e = math.radians(az), math.radians(el)
    d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
    cam.location = center + d * 12.0
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    bpy.context.view_layer.update()
    inv = cam.matrix_world.inverted()
    cp = [inv @ p for p in pts]
    xs, ys = [p.x for p in cp], [p.y for p in cp]
    pad = OUTLINE_WIDTH * 2 + 0.02
    w, h = max(xs) - min(xs) + pad, max(ys) - min(ys) + pad
    cx, cy = (max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2
    cam.location = cam.matrix_world @ Vector((cx, cy, 0))
    scene.render.resolution_y = HEIGHT
    scene.render.resolution_x = max(16, int(round(HEIGHT * w / h)))
    cam_data.ortho_scale = max(w, h)
    cam_data.sensor_fit = "AUTO"
    for team in ("azure", "crimson"):
        toon.set_team(mat, team == "crimson")
        scene.render.filepath = os.path.join(OUT, f"{name}_{team}.png")
        bpy.ops.render.render(write_still=True)
        print("RENDERED", scene.render.filepath, scene.render.resolution_x, scene.render.resolution_y)
