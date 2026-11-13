"""Headless EEVEE renders of builder.blend with the builder_art.toon3 CM/Toon preview (3 bands, Void Navy hull).
Adapted from the Miner's render_miner.py (same cameras, outline widths, team-mask debug pass).

  Blender -b --factory-startup --python-exit-code 1 -P render_builder.py -- <out_dir> [ortho_px] [only,comma,list]

Writes PNGs to <out_dir> (outside the repo) plus measure.json:
  gameplay_<team>.png, gameplay2x_<team>.png   art-direction camera (perspective, pitch 55, vFOV 35, 23.4 u, yaw 45),
                                               mid-grey ground, contact blob, 2.0 u reference silhouette; faces S
  gameplay_props_<team>.png                    ladder and bridge states on the 1 m grid with a 0.75 u step and a gap
  gameplay_warrior_ladder.png                  the Warrior (warrior.blend on this branch) carrying the ladder
  facing_<dir>.png / facing_<dir>_mask.png     8 facings at the gameplay camera (mask = team pixels white, ink red)
  ortho_<framing>_<team>.png                   turnaround + poses + props, orthographic, transparent
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
SHARED = os.path.join(os.path.dirname(os.path.dirname(HERE)), "Characters", "_source~")
sys.path.insert(0, HERE)
sys.path.insert(0, SHARED)
from builder_art import bpal, toon3  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0]) if argv else "/tmp/builder-renders"
OH = int(argv[1]) if len(argv) > 1 else 600
ONLY = set(argv[2].split(",")) if len(argv) > 2 else None
os.makedirs(OUT, exist_ok=True)

PITCH, FOV, DIST, YAW = 55.0, 35.0, 23.4, 45.0
CHAR_PX, PROP_PX, ORTHO_PX = 3.0, 2.5, 5.0
FACINGS = {"N": 135, "NE": 90, "E": 45, "SE": 0, "S": -45, "SW": -90, "W": -135, "NW": 180}
L_GAME = Vector((0.0, math.cos(math.radians(50)), math.sin(math.radians(50))))

bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE, "builder.blend"))
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
arm.rotation_mode = "XYZ"
body, mallet = bpy.data.objects["Builder_Body"], bpy.data.objects["Builder_Mallet"]
LAD = {k: bpy.data.objects["BuilderLadder_" + k.capitalize()] for k in ("carried", "placed", "damaged")}
BRI = {k: bpy.data.objects["BuilderRopeBridge_" + k.capitalize()] for k in ("carried", "placed", "damaged")}
GLOW = bpy.data.objects["BuilderBuildGlow"]
PROPS = list(LAD.values()) + list(BRI.values()) + [GLOW]
mat = bpy.data.materials["CM_Toon"]
prop_mat = mat.copy()
prop_mat.name = "CM_Toon_PropPreview"
toon3.set_param(prop_mat, "RIM", 0.0)
for ob in PROPS:
    ob.data.materials[0] = prop_mat
for ob in [body, mallet] + PROPS:
    ob.hide_set(False)
arm.animation_data_create()
arm.animation_data.action = bpy.data.actions["Idle"]
scene.frame_set(1)

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
    r, g, b = bpal.lin(hexc)
    e.inputs["Color"].default_value = (r, g, b, 1)
    o = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(e.outputs[0], o.inputs["Surface"])
    return m


def mix_hex(a, b, t):
    ca, cb = bpal.hex_rgb(a), bpal.hex_rgb(b)
    return "#" + "".join("%02X" % round(255 * (x * (1 - t) + y * t)) for x, y in zip(ca, cb))


GROUND_HEX = "#808080"
bpy.ops.mesh.primitive_plane_add(size=80, location=(0, 0, 0))
ground = bpy.context.active_object
ground.name = "Ground"
ground.data.materials.append(flat_mat("Ground", GROUND_HEX))
blob_mat = flat_mat("Blob", mix_hex(GROUND_HEX, bpal.VOID_NAVY, 0.40))
grid_mat = flat_mat("Grid", mix_hex(GROUND_HEX, bpal.VOID_NAVY, 0.18))
block_mat = flat_mat("Block", "#9A9A9A")
block_side = flat_mat("BlockSide", "#6E6E6E")


def make_blob(name, loc, r=0.55):
    bpy.ops.mesh.primitive_circle_add(vertices=32, radius=r, fill_type="NGON", location=(loc[0], loc[1], 0.003))
    ob = bpy.context.active_object
    ob.name = name
    ob.data.materials.append(blob_mat)
    return ob


blob = make_blob("Blob", (0, 0))
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
ref_blob = make_blob("RefBlob", REF_LOC[:2], 0.4)
gameplay_extras = [ground, blob, ref_blob] + refsil

# grid context for the props: 1 m grid lines, a 0.75 u step block, two bridge abutments with a gap between
ctx = []
for i in range(-6, 9):
    for axis in (0, 1):
        bpy.ops.mesh.primitive_plane_add(size=1, location=(i, 1.5, 0.002) if axis == 0 else (1.5, i, 0.002))
        g = bpy.context.active_object
        g.scale = (0.015, 15, 1) if axis == 0 else (15, 0.015, 1)
        g.data.materials.append(grid_mat)
        ctx.append(g)


def block(name, x0, y0, x1, y1, z1):
    bpy.ops.mesh.primitive_cube_add(size=1, location=((x0 + x1) / 2, (y0 + y1) / 2, z1 / 2))
    b = bpy.context.active_object
    b.name = name
    b.scale = (x1 - x0, y1 - y0, z1)
    b.data.materials.append(block_mat)
    return b


for ob in gameplay_extras + ctx:
    ob.hide_render = True

INNER = {"feat_apron_detail", "feat_team_trim", "feat_face", "feat_moustache", "feat_ladder_detail",
         "feat_cap_detail", "feat_pauldron_detail", "feat_mallet_detail", "feat_belt", "feat_bridge_detail"}


def outline_weights(ob):
    if "outline_w" in ob.vertex_groups:
        return
    g = ob.vertex_groups.new(name="outline_w")
    inner_idx = {vg.index for vg in ob.vertex_groups if vg.name in INNER}
    none_idx = {vg.index for vg in ob.vertex_groups if vg.name in ("feat_face_decal", "feat_glow")}
    full, half, zero = [], [], []
    for v in ob.data.vertices:
        gs = {e.group for e in v.groups}
        (zero if gs & none_idx else half if gs & inner_idx else full).append(v.index)
    g.add(full, 1.0, "REPLACE")
    if half:
        g.add(half, 0.5, "REPLACE")
    if zero:
        g.add(zero, 0.0, "REPLACE")


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
    if "CM_FaceDecal" in bpy.data.materials:
        bpy.data.materials["CM_FaceDecal"].node_tree.nodes["DEBUG"].outputs[0].default_value = 1.0 if on else 0.0
    ink = bpy.data.materials["CM_Toon_Outline"].node_tree.nodes["INK"]
    ink.inputs["Color"].default_value = (1, 0, 0, 1) if on else tuple(list(bpal.lin(bpal.VOID_NAVY)) + [1])


ALLM = [body, mallet] + PROPS


def show(objs):
    for ob in ALLM:
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


def px_height(base, top, res_y):
    a = world_to_camera_view(scene, cam, Vector(base))
    b = world_to_camera_view(scene, cam, Vector(top))
    return abs(b.y - a.y) * res_y


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
    xs, ys = [], []
    for p in pts:
        c = world_to_camera_view(scene, cam, p)
        xs.append(c.x)
        ys.append(c.y)
    rx, ry = scene.render.resolution_x, scene.render.resolution_y
    scene.render.use_border = True
    scene.render.use_crop_to_border = True
    scene.render.border_min_x = max(0, min(xs) - pad_px / rx)
    scene.render.border_max_x = min(1, max(xs) + pad_px / rx)
    scene.render.border_min_y = max(0, min(ys) - pad_px / ry)
    scene.render.border_max_y = min(1, max(ys) + pad_px / ry)


def no_border():
    scene.render.use_border = False
    scene.render.use_crop_to_border = False


def want(key):
    return ONLY is None or key in ONLY


def pose(action, frac):
    act = bpy.data.actions[action]
    f0, f1 = act.frame_range
    arm.animation_data.action = act
    if getattr(act, "slots", None) and len(act.slots):
        arm.animation_data.action_slot = act.slots[0]
    scene.frame_set(int(round(f0 + (f1 - f0) * frac)))
    bpy.context.view_layer.update()


def feat_px(groups):
    dg = bpy.context.evaluated_depsgraph_get()
    gi = {body.vertex_groups[g].index for g in groups if g in body.vertex_groups}
    idx = [v.index for v in body.data.vertices if any(e.group in gi for e in v.groups)]
    ev = body.evaluated_get(dg)
    me = ev.to_mesh()
    cs = [world_to_camera_view(scene, cam, ev.matrix_world @ me.vertices[i].co) for i in idx]
    ev.to_mesh_clear()
    return [round((max(c.x for c in cs) - min(c.x for c in cs)) * 1920, 1),
            round((max(c.y for c in cs) - min(c.y for c in cs)) * 1080, 1)]


measure = {}
char_objs = [body, mallet]
arm.rotation_euler = (0, 0, 0)
bpy.context.view_layer.update()
pp_ = posed_points([body])
measure["idle_body_bounds"] = [[round(min(p[i] for p in pp_), 3) for i in range(3)],
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
    measure["gameplay_px_height_silhouette_with_ladder"] = round((max(ys) - min(ys)) * 1080, 2)
    nl = [p for p, y in zip(pts, ys)]
    pb = posed_points([body])
    gi = {body.vertex_groups[g].index for g in ("feat_ladder", "feat_ladder_detail")}
    lad_idx = {v.index for v in body.data.vertices if any(e.group in gi for e in v.groups)}
    pb_nl = [p for i, p in enumerate(pb) if i not in lad_idx] + posed_points([mallet])
    ys2 = [world_to_camera_view(scene, cam, p).y for p in pb_nl]
    measure["gameplay_px_height_silhouette_without_ladder"] = round((max(ys2) - min(ys2)) * 1080, 2)
    for fname in ("S", "N", "SE", "E"):
        arm.rotation_euler = (0, 0, math.radians(FACINGS[fname]))
        bpy.context.view_layer.update()
        measure[f"gameplay_px_wh_{fname}"] = {"cap": feat_px(["feat_cap", "feat_cap_detail"]),
                                              "ladder": feat_px(["feat_ladder", "feat_ladder_detail"]),
                                              "rope_coil": feat_px(["feat_rope_coil"])}
    arm.rotation_euler = (0, 0, math.radians(FACINGS["S"]))
    bpy.context.view_layer.update()
    for team in ("azure", "crimson"):
        set_team(team == "crimson")
        no_border()
        render(os.path.join(OUT, f"gameplay_{team}.png"))
        scene.render.resolution_x, scene.render.resolution_y = 3840, 2160
        border_around(pts, 60)
        render(os.path.join(OUT, f"gameplay2x_{team}.png"))
        scene.render.resolution_x, scene.render.resolution_y = 1920, 1080
    no_border()
    for ob in gameplay_extras:
        ob.hide_render = True

if want("props"):
    # props on the 1 m grid: placed ladder against a 0.75 u step, damaged ladder beside it, carried ladder and roll
    # on the ground, the bridge spanning a 4-cell gap between two 0.75 u abutments, the damaged span beside it
    for ob in [ground] + ctx:
        ob.hide_render = False
    blocks = [block("Step", -3.0, 0.5, 1.0, 3.0, 0.75),
              block("AbutN", 2.0, 0.5, 6.0, 2.5, 0.75), block("AbutS", 2.0, -6.08, 6.0, -3.58, 0.75)]
    place = {LAD["placed"]: ((-2.0, 0.0, 0.0), 0), LAD["damaged"]: ((-0.6, 0.0, 0.0), 0),
             LAD["carried"]: ((-1.4, -2.4, 0.09), 0), BRI["carried"]: ((0.4, -2.6, 0.0), 0),
             BRI["placed"]: ((3.0, 0.5, 0.70), 0), BRI["damaged"]: ((5.0, 0.5, 0.70), 0)}
    for ob, (loc, rz) in place.items():
        ob.location = loc
        ob.rotation_euler = (0, 0, math.radians(rz))
    GLOW.location = (-2.0, -0.3, 0.0)
    show(list(place.keys()) + [GLOW])
    body.hide_render = mallet.hide_render = False
    arm.location = (-3.2, -1.2, 0)
    arm.rotation_euler = (0, 0, math.radians(FACINGS["S"]))
    bpy.context.view_layer.update()
    wpp = gameplay_camera((1.3, -1.6, 0.4))
    scene.render.film_transparent = False
    set_outline(list(place.keys()), PROP_PX * wpp)
    set_outline(char_objs, CHAR_PX * wpp)
    toon3.add_outline(GLOW, 0.0001, None)
    set_light_all(L_GAME)
    for team in ("azure", "crimson"):
        set_team(team == "crimson")
        render(os.path.join(OUT, f"gameplay_props_{team}.png"))
    for ob in place:
        ob.location = (0, 0, 0)
        ob.rotation_euler = (0, 0, 0)
    GLOW.location = (0, 0, 0)
    arm.location = (0, 0, 0)
    for b in blocks:
        bpy.data.objects.remove(b, do_unlink=True)
    for ob in [ground] + ctx:
        ob.hide_render = True

# ---------------------------------------------------------------- 8 facings at the gameplay camera
if want("facings"):
    pose("Idle", 0.0)
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

# ---------------------------------------------------------------- orthographic framings
ORTHO = {
    "front": ("char", 0, 6, ("azure",), ("Idle", 0.0)),
    "three-quarter": ("char", -32, 6, ("azure",), ("Idle", 0.0)),
    "profile": ("char", -90, 4, ("azure",), ("Idle", 0.0)),
    "back": ("char", 180, 6, ("azure",), ("Idle", 0.0)),
    "accent": ("char", -20, 6, ("crimson", "azure"), ("Idle", 0.0)),
    "pose-build": ("char", -35, 8, ("azure",), ("1H_Melee_Attack_Chop", 0.38)),
    "pose-interact": ("char", -35, 8, ("azure",), ("Interact", 0.5)),
    "top": ("char", 0, 89, ("azure",), ("Idle", 0.0)),
    "mallet": ("mallet", 0, 4, ("azure",), ("Idle", 0.0)),
    "ladder-carried": ("lad_carried", -28, 30, ("azure",), None),
    "ladder-placed": ("lad_placed", -55, 14, ("azure",), None),
    "ladder-damaged": ("lad_damaged", -55, 14, ("azure",), None),
    "bridge-carried": ("bri_carried", -28, 22, ("azure",), None),
    "bridge-placed": ("bri_placed", -62, 22, ("azure", "crimson"), None),
    "bridge-damaged": ("bri_damaged", -62, 22, ("azure",), None),
}
if want("ortho"):
    cam_data.type = "ORTHO"
    cam_data.sensor_fit = "AUTO"
    scene.render.film_transparent = True
    arm.rotation_euler = (0, 0, 0)
    for name, (what, az, el, teams, ps) in ORTHO.items():
        if ONLY is not None and "ortho" not in ONLY and name not in ONLY:
            continue
        if ps:
            pose(*ps)
        if what == "char":
            objs = char_objs
        elif what == "mallet":
            objs = [mallet]
        else:
            kind, st = what.split("_")
            objs = [(LAD if kind == "lad" else BRI)[st]]
        show(objs)
        bpy.context.view_layer.update()
        a, e = math.radians(az), math.radians(el)
        d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
        pts = posed_points(objs)
        center = sum(pts, Vector()) / len(pts)
        cam.location = center + d * 14.0
        up_hint = "Y" if el < 80 else "Y"
        cam.rotation_euler = (-d).to_track_quat("-Z", up_hint).to_euler()
        bpy.context.view_layer.update()
        inv = cam.matrix_world.inverted()
        cp = [inv @ p for p in pts]
        xs, ys = [p.x for p in cp], [p.y for p in cp]
        w, h = max(xs) - min(xs), max(ys) - min(ys)
        pad = 0.04 * max(w, h) + 0.02
        w, h = w + 2 * pad, h + 2 * pad
        cx, cy = (max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2
        cam.location = cam.matrix_world @ Vector((cx, cy, 0))
        hh = OH if what != "mallet" else int(OH * 0.6)
        scene.render.resolution_y = hh
        scene.render.resolution_x = max(16, int(round(hh * w / h)))
        if scene.render.resolution_x > 2.2 * OH:
            scene.render.resolution_x = int(2.2 * OH)
            scene.render.resolution_y = max(16, int(round(scene.render.resolution_x * h / w)))
        cam_data.ortho_scale = max(w, h)
        wpp = cam_data.ortho_scale / max(scene.render.resolution_x, scene.render.resolution_y)
        set_outline([o for o in objs if o in (body, mallet)], ORTHO_PX * wpp)
        set_outline([o for o in objs if o in PROPS], ORTHO_PX * 0.83 * wpp)
        ld = (cam.matrix_world.to_3x3() @ Vector((-0.55, 0.65, 0.75))).normalized()
        set_light_all(ld)
        for team in teams:
            set_team(team == "crimson")
            render(os.path.join(OUT, f"ortho_{name}_{team}.png"))
    pose("Idle", 0.0)

# ---------------------------------------------------------------- the Warrior carrying the ladder (any class moves it)
if want("warrior"):
    wb = os.path.join(SHARED, "warrior.blend")
    with bpy.data.libraries.load(wb, link=False) as (src, dst):
        dst.objects = [n for n in src.objects if n in ("Rig", "Warrior_Body", "Warrior_Shield", "Warrior_Cleaver")]
    wobjs = [o for o in dst.objects if o is not None]
    wrig = [o for o in wobjs if o.type == "ARMATURE"][0]
    for o in wobjs:
        scene.collection.objects.link(o)
    wrig.location = (0, 0, 0)
    wrig.rotation_mode = "XYZ"
    wrig.animation_data_create()
    wrig.animation_data.action = bpy.data.actions["2H_Melee_Idle"] if "2H_Melee_Idle" in bpy.data.actions else \
        bpy.data.actions["Idle"]
    scene.frame_set(1)
    shown = [o for o in wobjs if o.type == "MESH" and o.name.startswith("Warrior_Body")]
    lad = LAD["carried"]
    # ladder on his right shoulder, running front to back, rails tipped up a little
    lad.parent = wrig
    lad.parent_type = "BONE"
    lad.parent_bone = "chest"
    bpy.context.view_layer.update()
    lad.matrix_world = Matrix.Translation((-0.30, 0.05, 1.42)) @ Matrix.Rotation(math.radians(90), 4, "Z") @ \
        Matrix.Rotation(math.radians(-8), 4, "Y") @ Matrix.Rotation(math.radians(70), 4, "X")
    show([lad])
    body.hide_render = mallet.hide_render = True
    for o in shown:
        o.hide_render = False
    for o in wobjs:
        if o.type == "MESH" and o not in shown:
            o.hide_render = True
    for ob in gameplay_extras:
        ob.hide_render = False
    wrig.rotation_euler = (0, 0, math.radians(FACINGS["SE"]))
    bpy.context.view_layer.update()
    wpp = gameplay_camera((0, 0, 0.9))
    scene.render.film_transparent = False
    set_outline([lad] + refsil, PROP_PX * wpp)
    set_outline(shown, CHAR_PX * wpp)
    set_light_all(L_GAME)
    set_team(False)
    pts = posed_points(shown + [lad])
    scene.render.resolution_x, scene.render.resolution_y = 3840, 2160
    border_around(pts, 80)
    render(os.path.join(OUT, "gameplay2x_warrior_ladder.png"))
    no_border()

with open(os.path.join(OUT, "measure.json"), "w") as f:
    json.dump(measure, f, indent=1)
print("MEASURE " + json.dumps(measure))
