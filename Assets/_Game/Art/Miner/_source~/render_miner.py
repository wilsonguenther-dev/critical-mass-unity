"""Headless EEVEE renders of miner.blend with the miner_art.toon3 CM/Toon preview (3 bands, Void Navy hull).

  Blender -b --factory-startup --python-exit-code 1 -P render_miner.py -- <out_dir> [ortho_height_px]

Writes PNGs to <out_dir> (outside the repo) plus measure.json:
  gameplay_<team>.png         1920x1080, art-direction camera (perspective, pitch 55, vFOV 35, 23.4 u, yaw 45), mid-grey
                              ground, contact blob, a 2.0 u reference silhouette; character faces S (the camera)
  gameplay2x_<team>.png       the same camera at 3840x2160, cropped to the character (true 2x zoom)
  gameplay_barricade_<team>.png  the three barricade states at the gameplay camera
  facing_<dir>.png / facing_<dir>_mask.png   8 facings at the gameplay camera (transparent; mask = team pixels white)
  ortho_<framing>_<team>.png  bible framings, orthographic, fitted to the subject, transparent
Outline width is set in SCREEN PIXELS per render (art direction 2.3): characters 3.0 px at 1080p, props 2.5 px.
"""
import json
import math
import os
import sys

sys.dont_write_bytecode = True
import bpy  # noqa: E402
from bpy_extras.object_utils import world_to_camera_view  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(HERE)), "Characters", "_source~"))
from miner_art import mpal, toon3  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0]) if argv else "/tmp/miner-renders"
OH = int(argv[1]) if len(argv) > 1 else 600
ONLY = set(argv[2].split(",")) if len(argv) > 2 else None
os.makedirs(OUT, exist_ok=True)

PITCH, FOV, DIST, YAW = 55.0, 35.0, 23.4, 45.0
CHAR_PX, PROP_PX, ORTHO_PX = 3.0, 2.5, 5.0
FACINGS = {"N": 135, "NE": 90, "E": 45, "SE": 0, "S": -45, "SW": -90, "W": -135, "NW": 180}
L_GAME = Vector((0.0, math.cos(math.radians(50)), math.sin(math.radians(50))))   # from screen upper-left, elev 50

bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE, "miner.blend"))
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "None"
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.render.resolution_percentage = 100
try:
    scene.eevee.taa_render_samples = 16
except AttributeError:
    pass
world = bpy.data.worlds.new("W")
world.color = (0.5, 0.5, 0.5)
scene.world = world

arm = bpy.data.objects["Rig"]
arm.rotation_mode = "XYZ"          # glTF import leaves the rig in quaternion mode
body, pick, hammer = (bpy.data.objects[n] for n in ("Miner_Body", "Miner_PickHammer", "Miner_PickHammer_Hammer"))
bars = {k: bpy.data.objects["Miner_Barricade_" + k.capitalize()] for k in ("folded", "deployed", "damaged")}
mat = bpy.data.materials["CM_Toon"]
prop_mat = mat.copy()
prop_mat.name = "CM_Toon_PropPreview"
toon3.set_param(prop_mat, "RIM", 0.0)
for ob in bars.values():
    ob.data.materials[0] = prop_mat
for ob in [body, pick, hammer] + list(bars.values()):
    ob.hide_set(False)
arm.animation_data_create()
arm.animation_data.action = bpy.data.actions["Idle"]
scene.frame_set(1)

# ---------------------------------------------------------------- scene helpers
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
    r, g, b = mpal.lin(hexc)
    e.inputs["Color"].default_value = (r, g, b, 1)
    o = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(e.outputs[0], o.inputs["Surface"])
    return m


def mix_hex(a, b, t):
    ca, cb = mpal.hex_rgb(a), mpal.hex_rgb(b)
    return "#" + "".join("%02X" % round(255 * (x * (1 - t) + y * t)) for x, y in zip(ca, cb))


GROUND_HEX = "#808080"
bpy.ops.mesh.primitive_plane_add(size=80, location=(0, 0, 0))
ground = bpy.context.active_object
ground.name = "Ground"
ground.data.materials.append(flat_mat("Ground", GROUND_HEX))
blob_mat = flat_mat("Blob", mix_hex(GROUND_HEX, mpal.VOID_NAVY, 0.40))


def make_blob(name, loc, r=0.55):
    bpy.ops.mesh.primitive_circle_add(vertices=32, radius=r, fill_type="NGON", location=(loc[0], loc[1], 0.003))
    ob = bpy.context.active_object
    ob.name = name
    ob.data.materials.append(blob_mat)
    return ob


blob = make_blob("Blob", (0, 0))
# 2.0 u reference silhouette (scale figure, flat navy-grey, outlined like a character)
ref_mat = flat_mat("RefSil", "#3B4560")
REF_LOC = Vector((-1.25, 1.25, 0))
parts = []
bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.26, depth=1.05, location=REF_LOC + Vector((0, 0, 0.78)))
parts.append(bpy.context.active_object)
bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=10, radius=0.30, location=REF_LOC + Vector((0, 0, 1.70)))
parts.append(bpy.context.active_object)
for x in (-0.13, 0.13):
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.11, depth=0.30, location=REF_LOC + Vector((x, 0, 0.15)))
    parts.append(bpy.context.active_object)
for p in parts:
    p.data.materials.append(ref_mat)
refsil = parts
ref_blob = make_blob("RefBlob", REF_LOC[:2], 0.4)

gameplay_extras = [ground, blob, ref_blob] + refsil
for ob in gameplay_extras:
    ob.hide_render = True


# inner detail drawn at half the outer width (art direction 2.3: outer 3.0 px, inner creases 1.5 px)
INNER = {"feat_chest_plate", "feat_straps", "feat_belt", "feat_face", "feat_beard", "feat_team_cloth",
         "feat_knee_guard", "feat_crate_detail", "feat_helmet_detail", "feat_helmet_lamp"}


def outline_weights(ob):
    if "outline_w" in ob.vertex_groups:
        return
    g = ob.vertex_groups.new(name="outline_w")
    inner_idx = {vg.index for vg in ob.vertex_groups if vg.name in INNER}
    full, half = [], []
    for v in ob.data.vertices:
        (half if any(e.group in inner_idx for e in v.groups) else full).append(v.index)
    g.add(full, 1.0, "REPLACE")
    if half:
        g.add(half, 0.5, "REPLACE")


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
    ink = bpy.data.materials["CM_Toon_Outline"].node_tree.nodes["INK"]
    c = (1, 0, 0, 1) if on else tuple(list(mpal.lin(mpal.VOID_NAVY)) + [1])   # debug: ink red, team white, rest black
    ink.inputs["Color"].default_value = c


def show(objs):
    allm = [body, pick, hammer] + list(bars.values())
    for ob in allm:
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
    return 2 * DIST * math.tan(math.radians(FOV) / 2) / 1080.0      # metres per 1080p pixel at the target


def px_height(base, top, res_y):
    a = world_to_camera_view(scene, cam, Vector(base))
    b = world_to_camera_view(scene, cam, Vector(top))
    return abs(b.y - a.y) * res_y


def posed_points(objs):
    dg = bpy.context.evaluated_depsgraph_get()
    pts = []
    for ob in objs:
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        mw = ev.matrix_world
        pts.extend(mw @ v.co for v in me.vertices)
        ev.to_mesh_clear()
    return pts


def border_around(pts, pad_px=24):
    xs, ys = [], []
    for p in pts:
        c = world_to_camera_view(scene, cam, p)
        xs.append(c.x)
        ys.append(c.y)
    rx, ry = scene.render.resolution_x, scene.render.resolution_y
    pdx, pdy = pad_px / rx, pad_px / ry
    scene.render.use_border = True
    scene.render.use_crop_to_border = True
    scene.render.border_min_x = max(0, min(xs) - pdx)
    scene.render.border_max_x = min(1, max(xs) + pdx)
    scene.render.border_min_y = max(0, min(ys) - pdy)
    scene.render.border_max_y = min(1, max(ys) + pdy)
    return (min(ys), max(ys))


def no_border():
    scene.render.use_border = False
    scene.render.use_crop_to_border = False


def want(key):
    return ONLY is None or key in ONLY



# ---------------------------------------------------------------- capture stances (render only; mesh, rig rest and FBX untouched)
# The bible sheets draw the Miner with his fists up and the pick held upright; "bible" aims the arm bones at these
# armature-space points (Idle frame 1 as the start), then turns the right hand so the pick shaft stands upright.
STANCES = {
    "bible": {"upperarm.r": (-0.50, 0.03, 0.90), "lowerarm.r": (-0.62, -0.14, 0.98),
              "upperarm.l": (0.47, 0.05, 0.90), "lowerarm.l": (0.57, -0.08, 0.84), "pick_up": (-0.10, -0.04, 1.0)},
    "barricade": {"upperarm.r": (-0.40, -0.20, 0.96), "lowerarm.r": (-0.30, -0.50, 0.88),
                  "upperarm.l": (0.40, -0.20, 0.96), "lowerarm.l": (0.30, -0.50, 0.88), "pick_up": None},
}
IDLE_PICK_UP = Vector((-0.10, -0.06, 1.0)).normalized()     # build_miner.build_pick display-pose shaft direction
_idle_bases = None


def freeze_idle():
    global _idle_bases
    arm.animation_data.action = bpy.data.actions["Idle"]
    scene.frame_set(1)
    _idle_bases = {pb.name: pb.matrix_basis.copy() for pb in arm.pose.bones}
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.matrix_basis = _idle_bases[pb.name]
    bpy.context.view_layer.update()


def reset_idle():
    for pb in arm.pose.bones:
        pb.matrix_basis = _idle_bases[pb.name]
    bpy.context.view_layer.update()


def _rot_about(pb, q):
    head = pb.head.copy()
    pb.matrix = Matrix.Translation(head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-head) @ pb.matrix
    bpy.context.view_layer.update()


def apply_stance(name):
    reset_idle()
    st = STANCES[name]
    slot_idle = arm.pose.bones["handslot.r"].matrix.copy()
    for bone in ("upperarm.r", "lowerarm.r", "upperarm.l", "lowerarm.l"):
        pb = arm.pose.bones[bone]
        _rot_about(pb, (pb.tail - pb.head).rotation_difference(Vector(st[bone]) - pb.head))
    if st["pick_up"] is not None:
        slot_now = arm.pose.bones["handslot.r"].matrix
        cur = ((slot_now @ slot_idle.inverted()).to_3x3() @ IDLE_PICK_UP).normalized()
        _rot_about(arm.pose.bones["hand.r"], cur.rotation_difference(Vector(st["pick_up"]).normalized()))

measure = {}
char_objs = [body, pick]
arm.rotation_euler = (0, 0, 0)
bpy.context.view_layer.update()
for nm, objs_ in (("idle_body", [body]), ("idle_body_and_pick", [body, pick])):
    pp_ = posed_points(objs_)
    measure[nm + "_bounds"] = [[round(min(p[i] for p in pp_), 3) for i in range(3)],
                               [round(max(p[i] for p in pp_), 3) for i in range(3)]]

# ---------------------------------------------------------------- gameplay camera
if want("gameplay"):
    wpp = gameplay_camera((0, 0, 0.9))
    show(char_objs)
    for ob in gameplay_extras:
        ob.hide_render = False
    scene.render.film_transparent = False
    arm.rotation_euler = (0, 0, math.radians(FACINGS["S"]))
    bpy.context.view_layer.update()
    set_outline(char_objs + refsil, CHAR_PX * wpp)
    set_light_all(L_GAME)
    measure["gameplay_px_per_unit_at_target"] = 1.0 / wpp
    measure["gameplay_px_height_0_to_2u"] = round(px_height((0, 0, 0), (0, 0, 2.0), 1080), 2)
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
    # barricade states at the gameplay camera (the miner stays for scale)
    show(char_objs + list(bars.values()))
    right = Vector((math.cos(math.radians(-45)), math.sin(math.radians(-45)), 0))   # screen right on the ground
    upv = Vector((math.cos(math.radians(45)), math.sin(math.radians(45)), 0))       # screen up on the ground
    for i, k in enumerate(("folded", "deployed", "damaged")):
        bars[k].location = right * (1.6 + i * 2.2) - upv * 0.4
        bars[k].rotation_euler = (0, 0, math.radians(-45))
    bpy.context.view_layer.update()
    set_outline(list(bars.values()), PROP_PX * wpp)
    gameplay_camera(right * 3.2 + Vector((0, 0, 0.5)))
    for team in ("azure", "crimson"):
        set_team(team == "crimson")
        render(os.path.join(OUT, f"gameplay_barricade_{team}.png"))
    for k in bars:
        bars[k].location = (0, 0, 0)
        bars[k].rotation_euler = (0, 0, 0)
    for ob in gameplay_extras:
        ob.hide_render = True

# ---------------------------------------------------------------- 8 facings at the gameplay camera
if want("facings"):
    wpp = gameplay_camera((0, 0, 0.9))
    show(char_objs)
    scene.render.film_transparent = True
    set_outline(char_objs, CHAR_PX * wpp)
    set_light_all(L_GAME)
    set_team(False)
    fac = {}
    for name, deg in FACINGS.items():
        arm.rotation_euler = (0, 0, math.radians(deg))
        bpy.context.view_layer.update()
        border_around(posed_points(char_objs), 12)
        set_debug(False)
        render(os.path.join(OUT, f"facing_{name}.png"))
        set_debug(True)
        render(os.path.join(OUT, f"facing_{name}_mask.png"))
        set_debug(False)
        fac[name] = deg
    no_border()
    arm.rotation_euler = (0, 0, 0)
    measure["facings_world_rotation_deg"] = fac

# ---------------------------------------------------------------- orthographic bible framings
ORTHO = {
    # name: (objects, azimuth, elevation, teams, character outline?)
    "front": ("char", 0, 6, ("azure",)),
    "three-quarter": ("char", -32, 6, ("azure",)),
    "profile": ("char", -90, 4, ("azure",)),
    "back": ("char", 180, 6, ("azure",)),
    "accent": ("char", -14, 6, ("crimson", "azure")),
    "front-pick": ("pickzoom", 0, 4, ("azure",)),
    "pick": ("pick", 0, 0, ("azure", "crimson")),
    "hammer": ("hammer", 0, 0, ("azure", "crimson")),
    "barricade-folded": ("folded", -28, 16, ("azure",)),
    "barricade-deployed": ("deployed", -18, 14, ("azure", "crimson")),
    "barricade-damaged": ("damaged", -18, 14, ("azure",)),
    "pose-barricade": ("posebar", -28, 10, ("azure",)),
}
if want("ortho"):
    cam_data.type = "ORTHO"
    cam_data.sensor_fit = "AUTO"
    scene.render.film_transparent = True
    arm.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()
    freeze_idle()
    for name, (what, az, el, teams) in ORTHO.items():
        if what in ("char", "pickzoom"):
            apply_stance("bible")
        elif what == "posebar":
            apply_stance("barricade")
        else:
            reset_idle()
        if what in ("char", "pickzoom"):
            objs, fit = char_objs, char_objs
            if what == "pickzoom":
                fit = [pick]
        elif what == "pick":
            objs = fit = [pick]
        elif what == "hammer":
            objs = fit = [hammer]
        elif what == "posebar":
            bars["deployed"].location = (0.0, -0.80, 0)
            objs = fit = char_objs + [bars["deployed"]]
        else:
            objs = fit = [bars[what]]
        show(objs)
        bpy.context.view_layer.update()
        a, e = math.radians(az), math.radians(el)
        d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
        pts = posed_points(fit)
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
        cx, cy = (max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2
        cam.location = cam.matrix_world @ Vector((cx, cy, 0))
        hh = OH if what not in ("pick", "hammer") else int(OH * 0.6)
        scene.render.resolution_y = hh
        scene.render.resolution_x = max(16, int(round(hh * w / h)))
        cam_data.ortho_scale = max(w, h)
        wpp = cam_data.ortho_scale / max(scene.render.resolution_x, scene.render.resolution_y)
        char = what in ("char", "pickzoom", "posebar")
        set_outline([o for o in objs if o in (body, pick, hammer)], ORTHO_PX * wpp)
        set_outline([o for o in objs if o in bars.values()], ORTHO_PX * 0.83 * wpp)
        # key light rides with the camera, from its upper left
        ld = (cam.matrix_world.to_3x3() @ Vector((-0.55, 0.65, 0.75))).normalized()
        set_light_all(ld)
        for team in teams:
            set_team(team == "crimson")
            render(os.path.join(OUT, f"ortho_{name}_{team}.png"))
        if what == "posebar":
            bars["deployed"].location = (0, 0, 0)

with open(os.path.join(OUT, "measure.json"), "w") as f:
    json.dump(measure, f, indent=1)
print("MEASURE " + json.dumps(measure))
