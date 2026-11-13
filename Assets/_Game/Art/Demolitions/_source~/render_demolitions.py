"""Headless EEVEE renders of demolitions.blend with the demo_art.toon3 CM/Toon preview (3 bands, Void Navy hull).
Same cameras and conventions as render_miner.py.

  cm-heavy blender ... Blender -b --factory-startup --python-exit-code 1 -P render_demolitions.py -- <out_dir> [ortho_px] [only]

Writes PNGs to <out_dir> (outside the repo) plus measure.json:
  gameplay_<team>.png, gameplay2x_<team>.png   1920x1080 art-direction camera (perspective, pitch 55, vFOV 35, 23.4 u,
                                               yaw 45), mid-grey ground, 2.0 u reference silhouette; true 2x crop
  gameplay_props_<team>.png                    mine buried / armed (+ owner ring) / triggered, TNT in flight, crater,
                                               explosion, at the gameplay camera, the character for scale
  facing_<dir>.png / facing_<dir>_mask.png     8 facings at the gameplay camera (mask: team pixels white, ink red)
  ortho_<framing>_<team>.png                   turnaround (front, three-quarter, profile, back), throw, plant, accent
  ortho_prop_<name>.png                        each prop state, three-quarter from above
"""
import json
import math
import os
import sys

sys.dont_write_bytecode = True
import bpy  # noqa: E402
from bpy_extras.object_utils import world_to_camera_view  # noqa: E402
from mathutils import Vector  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(HERE)), "Characters", "_source~"))
from demo_art import dpal, toon3  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0]) if argv else "/tmp/demolitions-renders"
OH = int(argv[1]) if len(argv) > 1 else 600
ONLY = set(argv[2].split(",")) if len(argv) > 2 else None
os.makedirs(OUT, exist_ok=True)

PITCH, FOV, DIST, YAW = 55.0, 35.0, 23.4, 45.0
CHAR_PX, PROP_PX, ORTHO_PX = 3.0, 2.5, 5.0
FACINGS = {"N": 135, "NE": 90, "E": 45, "SE": 0, "S": -45, "SW": -90, "W": -135, "NW": 180}
L_GAME = Vector((0.0, math.cos(math.radians(50)), math.sin(math.radians(50))))

bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE, "demolitions.blend"))
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "None"
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.render.resolution_percentage = 100
scene.eevee.taa_render_samples = 16
world = bpy.data.worlds.new("W")
world.color = (0.5, 0.5, 0.5)
scene.world = world

arm = bpy.data.objects["Rig"]
arm.rotation_mode = "XYZ"
body, tnt = bpy.data.objects["Demolitions_Body"], bpy.data.objects["Demolitions_TNTHand"]
PN = ("Mine_Buried", "Mine_Armed", "Mine_Armed_OwnerRing", "Mine_Triggered", "TNT_Flight", "CraterRim", "Explosion")
props = {k: bpy.data.objects["Demolitions_" + k] for k in PN}
mat = bpy.data.materials["CM_Toon"]
prop_mat = mat.copy()
prop_mat.name = "CM_Toon_PropPreview"
toon3.set_param(prop_mat, "RIM", 0.0)
for ob in props.values():
    ob.data.materials[0] = prop_mat
    ob.hide_set(False)
for ob in (body, tnt):
    ob.hide_set(False)
arm.animation_data_create()


def set_pose(action, frame):
    arm.animation_data.action = bpy.data.actions[action]
    if len(bpy.data.actions[action].slots):
        arm.animation_data.action_slot = bpy.data.actions[action].slots[0]
    scene.frame_set(frame)
    bpy.context.view_layer.update()


set_pose("Idle", 1)
cam_data = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam


def flat_mat(name, hexc):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    e = nt.nodes.new("ShaderNodeEmission")
    e.inputs["Color"].default_value = (*dpal.lin(hexc), 1)
    o = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(e.outputs[0], o.inputs["Surface"])
    return m


def mix_hex(a, b, t):
    ca, cb = dpal.hex_rgb(a), dpal.hex_rgb(b)
    return "#" + "".join("%02X" % round(255 * (x * (1 - t) + y * t)) for x, y in zip(ca, cb))


GROUND_HEX = "#808080"
bpy.ops.mesh.primitive_plane_add(size=80, location=(0, 0, 0))
ground = bpy.context.active_object
ground.data.materials.append(flat_mat("Ground", GROUND_HEX))
blob_mat = flat_mat("Blob", mix_hex(GROUND_HEX, dpal.VOID_NAVY, 0.40))


def make_blob(loc, r=0.55):
    bpy.ops.mesh.primitive_circle_add(vertices=32, radius=r, fill_type="NGON", location=(loc[0], loc[1], 0.003))
    ob = bpy.context.active_object
    ob.data.materials.append(blob_mat)
    return ob


blob = make_blob((0, 0))
ref_mat = flat_mat("RefSil", "#3B4560")
REF_LOC = Vector((-1.25, 1.25, 0))
refsil = []
bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.26, depth=1.05, location=REF_LOC + Vector((0, 0, 0.78)))
refsil.append(bpy.context.active_object)
bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=10, radius=0.30, location=REF_LOC + Vector((0, 0, 1.70)))
refsil.append(bpy.context.active_object)
for x in (-0.13, 0.13):
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.11, depth=0.30, location=REF_LOC + Vector((x, 0, 0.15)))
    refsil.append(bpy.context.active_object)
for p in refsil:
    p.data.materials.append(ref_mat)
ref_blob = make_blob(REF_LOC[:2], 0.4)
extras = [ground, blob, ref_blob] + refsil
for ob in extras:
    ob.hide_render = True

# outline: inner detail at half width (art direction 2.3); no ink on the decal or on the self-lit sparks
INNER = {"feat_vest_detail", "feat_bandolier", "feat_tnt", "feat_keg_detail", "feat_goggles", "feat_cap_detail",
         "feat_team_cloth", "feat_buckles", "feat_straps", "feat_knee_patch", "feat_soot", "feat_fingers",
         "feat_fuse_coil", "feat_plate", "feat_dirt", "feat_pebbles", "feat_face"}
NONE = {"feat_face_decal", "feat_spark", "feat_crater", "feat_boom"}


def outline_weights(ob):
    if "outline_w" in ob.vertex_groups:
        return
    g = ob.vertex_groups.new(name="outline_w")
    inner_idx = {vg.index for vg in ob.vertex_groups if vg.name in INNER}
    none_idx = {vg.index for vg in ob.vertex_groups if vg.name in NONE}
    full, half, zero = [], [], []
    for v in ob.data.vertices:
        gs = {e.group for e in v.groups}
        (zero if gs & none_idx else half if gs & inner_idx else full).append(v.index)
    for lst, w in ((full, 1.0), (half, 0.5), (zero, 0.0)):
        if lst:
            g.add(lst, w, "REPLACE")


def set_outline(objs, width):
    for ob in objs:
        if ob.type == "MESH":
            if ob.vertex_groups:
                outline_weights(ob)
            toon3.add_outline(ob, width, "outline_w" if ob.vertex_groups else None)


def set_light_all(vec):
    for m in (mat, prop_mat):
        toon3.set_light(m, vec)


def set_team(crimson):
    for m in (mat, prop_mat):
        toon3.set_param(m, "TEAM", 1.0 if crimson else 0.0)


def set_debug(on):
    for m in (mat, prop_mat):
        toon3.set_param(m, "DEBUG", 1.0 if on else 0.0)
    bpy.data.materials["CM_FaceDecal"].node_tree.nodes["DEBUG"].outputs[0].default_value = 1.0 if on else 0.0
    ink = bpy.data.materials["CM_Toon_Outline"].node_tree.nodes["INK"]
    ink.inputs["Color"].default_value = (1, 0, 0, 1) if on else (*dpal.lin(dpal.VOID_NAVY), 1)


def show(objs):
    for ob in [body, tnt] + list(props.values()):
        ob.hide_render = ob not in objs


def render(path):
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def gameplay_camera(target, res=(1920, 1080)):
    cam_data.type = "PERSP"
    cam_data.sensor_fit = "VERTICAL"
    cam_data.angle = math.radians(FOV)
    cam_data.clip_start = 0.5
    cam_data.clip_end = 200
    p, y = math.radians(PITCH), math.radians(YAW)
    off = Vector((-math.sin(y) * math.cos(p), -math.cos(y) * math.cos(p), math.sin(p))) * DIST
    cam.location = Vector(target) + off
    cam.rotation_euler = (-off).to_track_quat("-Z", "Y").to_euler()
    scene.render.resolution_x, scene.render.resolution_y = res
    bpy.context.view_layer.update()
    return 2 * DIST * math.tan(math.radians(FOV) / 2) / 1080.0


def posed_points(objs):
    hulls = [m for ob in objs for m in ob.modifiers if m.name == "Outline" and m.show_viewport]
    for m in hulls:
        m.show_viewport = False
    dg = bpy.context.evaluated_depsgraph_get()
    pts = []
    for ob in objs:
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        pts.extend(ev.matrix_world @ v.co for v in me.vertices)
        ev.to_mesh_clear()
    for m in hulls:
        m.show_viewport = True
    return pts


def border_around(pts, pad_px=24):
    cs = [world_to_camera_view(scene, cam, p) for p in pts]
    rx, ry = scene.render.resolution_x, scene.render.resolution_y
    scene.render.use_border = True
    scene.render.use_crop_to_border = True
    scene.render.border_min_x = max(0, min(c.x for c in cs) - pad_px / rx)
    scene.render.border_max_x = min(1, max(c.x for c in cs) + pad_px / rx)
    scene.render.border_min_y = max(0, min(c.y for c in cs) - pad_px / ry)
    scene.render.border_max_y = min(1, max(c.y for c in cs) + pad_px / ry)


def no_border():
    scene.render.use_border = False
    scene.render.use_crop_to_border = False


def want(key):
    return ONLY is None or key in ONLY


def key_frame(action, bone, pick):
    """Frame of `action` where `bone` (pose head, armature space) is highest (pick='max') or lowest ('min')."""
    act = bpy.data.actions[action]
    f0, f1 = (int(round(v)) for v in act.frame_range)
    best = None
    for f in range(f0, f1 + 1):
        set_pose(action, f)
        z = arm.pose.bones[bone].head.z
        if best is None or (z > best[1] if pick == "max" else z < best[1]):
            best = (f, z)
    return best[0]


measure = {}
char_objs = [body, tnt]
arm.rotation_euler = (0, 0, 0)
set_pose("Idle", 1)
pp_ = posed_points([body])
measure["idle_body_bounds"] = [[round(min(p[i] for p in pp_), 3) for i in range(3)],
                               [round(max(p[i] for p in pp_), 3) for i in range(3)]]

if want("gameplay"):
    wpp = gameplay_camera((0, 0, 0.9))
    show(char_objs)
    for ob in extras:
        ob.hide_render = False
    scene.render.film_transparent = False
    arm.rotation_euler = (0, 0, math.radians(FACINGS["S"]))
    bpy.context.view_layer.update()
    set_outline(char_objs + refsil, CHAR_PX * wpp)
    set_light_all(L_GAME)
    pts = posed_points(char_objs)
    ys = [world_to_camera_view(scene, cam, p).y for p in pts]
    measure["gameplay_px_height_silhouette"] = round((max(ys) - min(ys)) * 1080, 2)
    for team in ("azure", "crimson"):
        set_team(team == "crimson")
        no_border()
        render(os.path.join(OUT, f"gameplay_{team}.png"))
        scene.render.resolution_x, scene.render.resolution_y = 3840, 2160
        border_around(pts, 60)
        render(os.path.join(OUT, f"gameplay2x_{team}.png"))
        scene.render.resolution_x, scene.render.resolution_y = 1920, 1080
    no_border()
    # props at the gameplay camera, laid out along screen-right, the character for scale
    right = Vector((math.cos(math.radians(-45)), math.sin(math.radians(-45)), 0))
    upv = Vector((math.cos(math.radians(45)), math.sin(math.radians(45)), 0))
    lay = [("Mine_Buried", 1.5), ("Mine_Armed", 3.0), ("Mine_Armed_OwnerRing", 3.0), ("Mine_Triggered", 4.6),
           ("TNT_Flight", 6.1), ("CraterRim", 7.7), ("Explosion", 10.0)]
    for k, x in lay:
        props[k].location = right * x - upv * 0.3
    show(char_objs + list(props.values()))
    set_outline([o for k, o in props.items() if k not in ("CraterRim", "Explosion", "Mine_Armed_OwnerRing")],
                PROP_PX * wpp)
    gameplay_camera(right * 5.2 + Vector((0, 0, 0.4)))
    for team in ("azure", "crimson"):
        set_team(team == "crimson")
        render(os.path.join(OUT, f"gameplay_props_{team}.png"))
    for o in props.values():
        o.location = (0, 0, 0)
    for ob in extras:
        ob.hide_render = True

if want("facings"):
    wpp = gameplay_camera((0, 0, 0.9))
    show(char_objs)
    scene.render.film_transparent = True
    set_outline(char_objs, CHAR_PX * wpp)
    set_light_all(L_GAME)
    set_team(False)
    for name, deg in FACINGS.items():
        arm.rotation_euler = (0, 0, math.radians(deg))
        bpy.context.view_layer.update()
        border_around(posed_points(char_objs), 12)
        set_debug(False)
        render(os.path.join(OUT, f"facing_{name}.png"))
        set_debug(True)
        render(os.path.join(OUT, f"facing_{name}_mask.png"))
        set_debug(False)
    no_border()
    arm.rotation_euler = (0, 0, 0)

ORTHO = {   # name: (objects, pose, azimuth, elevation, teams)
    "front": ("char", ("Idle", 1), 0, 6, ("azure",)),
    "three-quarter": ("char", ("Idle", 1), -32, 6, ("azure",)),
    "profile": ("char", ("Idle", 1), -90, 4, ("azure",)),
    "back": ("char", ("Idle", 1), 180, 6, ("azure",)),
    "accent": ("char", ("Idle", 1), -20, 6, ("crimson", "azure")),
    "throw": ("char", ("Throw", "max"), -35, 8, ("azure",)),
    "plant": ("char", ("PickUp", "min"), -35, 12, ("azure",)),
}
for k in PN:
    if k != "Mine_Armed_OwnerRing":
        ORTHO["prop_" + k] = (k, None, -30, 38 if k not in ("CraterRim",) else 70, ("azure",))
if want("ortho"):
    cam_data.type = "ORTHO"
    cam_data.sensor_fit = "AUTO"
    scene.render.film_transparent = True
    arm.rotation_euler = (0, 0, 0)
    frames = {}
    for name, (what, pose, az, el, teams) in ORTHO.items():
        if pose:
            act, f = pose
            if not isinstance(f, int):
                f = frames.setdefault(act, key_frame(act, "handslot.r", f))
            set_pose(act, f)
            measure.setdefault("pose_frames", {})[name] = [act, f]
        if what == "char":
            objs = char_objs
        elif what == "Mine_Armed":
            objs = [props["Mine_Armed"], props["Mine_Armed_OwnerRing"]]
        else:
            objs = [props[what]]
        show(objs)
        bpy.context.view_layer.update()
        a, e = math.radians(az), math.radians(el)
        d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
        pts = posed_points(objs)
        center = sum(pts, Vector()) / len(pts)
        cam.location = center + d * 12.0
        cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
        bpy.context.view_layer.update()
        inv = cam.matrix_world.inverted()
        cp = [inv @ p for p in pts]
        xs, ys = [p.x for p in cp], [p.y for p in cp]
        w, h = max(xs) - min(xs), max(ys) - min(ys)
        pad = 0.04 * max(w, h) + 0.02
        w, h = w + 2 * pad, h + 2 * pad
        cam.location = cam.matrix_world @ Vector(((max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2, 0))
        hh = OH if what == "char" else int(OH * 0.6)
        scene.render.resolution_y = hh
        scene.render.resolution_x = max(16, int(round(hh * w / h)))
        cam_data.ortho_scale = max(w, h)
        wpp = cam_data.ortho_scale / max(scene.render.resolution_x, scene.render.resolution_y)
        set_outline([o for o in objs if o in char_objs], ORTHO_PX * wpp)
        set_outline([o for o in objs if o not in char_objs and o.name.split("_", 1)[1] not in
                     ("CraterRim", "Explosion", "Mine_Armed_OwnerRing")], ORTHO_PX * 0.83 * wpp)
        set_light_all((cam.matrix_world.to_3x3() @ Vector((-0.55, 0.65, 0.75))).normalized())
        for team in teams:
            set_team(team == "crimson")
            render(os.path.join(OUT, f"ortho_{name}_{team}.png"))
    set_pose("Idle", 1)

with open(os.path.join(OUT, "measure.json"), "w") as f:
    json.dump(measure, f, indent=1)
print("MEASURE " + json.dumps(measure))
