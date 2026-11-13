"""Headless EEVEE renders of healer.blend with the CM/Toon preview (miner_art.toon3 with the Healer palette).

  ~/bin/cm-heavy blender perl -e 'alarm 1500; exec @ARGV' /Applications/Blender.app/Contents/MacOS/Blender -b \
      --factory-startup --python-exit-code 1 -P render_healer.py -- <out_dir> [ortho_height_px] [only,keys]

Keys: gameplay, facings, ortho, face, props. Pose: the KayKit Idle, frame 1 (what the game shows; no capture stance).
  gameplay_<team>.png / gameplay2x_<team>.png   1920x1080 art-direction camera (perspective, pitch 55, vFOV 35,
                                                23.4 u, yaw 45), mid-grey ground, 2.0 u reference figure; true 2x crop
  facing_<dir>.png                              8 facings at the gameplay camera, transparent
  ortho_<view>_<team>.png                       front / three-quarter / profile / back / accent, transparent
  face_closeup.png                              large orthographic head close-up
  props_<team>.png                              the heal (bloom gauntlet) and cleanse (lantern) props, three-quarter orthographic
Outline in screen pixels: characters 3.0 px at 1080p, props 2.5 px, ortho sheets 5.0 px.
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
ART = os.path.dirname(os.path.dirname(HERE))
for p in (HERE, os.path.join(ART, "Miner", "_source~"), os.path.join(ART, "Characters", "_source~")):
    sys.path.insert(0, p)
from healer_art import hpal  # noqa: E402
from miner_art import toon3  # noqa: E402
toon3.mpal = hpal
mpal = hpal

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0]) if argv else "/tmp/healer-renders"
OH = int(argv[1]) if len(argv) > 1 else 600
ONLY = set(argv[2].split(",")) if len(argv) > 2 else None
os.makedirs(OUT, exist_ok=True)

PITCH, FOV, DIST, YAW = 55.0, 35.0, 23.4, 45.0
CHAR_PX, PROP_PX, ORTHO_PX = 3.0, 2.5, 5.0
FACINGS = {"N": 135, "NE": 90, "E": 45, "SE": 0, "S": -45, "SW": -90, "W": -135, "NW": 180}
L_GAME = Vector((0.0, math.cos(math.radians(50)), math.sin(math.radians(50))))

bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE, "healer.blend"))
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
body, lantern = (bpy.data.objects[n] for n in ("Healer_Body", "Healer_Lantern"))
props = [bpy.data.objects[n] for n in ("Healer_BloomGauntlet_Prop", "Healer_Lantern_Prop")]
mat = bpy.data.materials["CM_Toon"]
prop_mat = mat.copy()
prop_mat.name = "CM_Toon_PropPreview"
toon3.set_param(prop_mat, "RIM", 0.0)
for ob in props:
    ob.data.materials[0] = prop_mat
for ob in [body, lantern] + props:
    ob.hide_set(False)
arm.animation_data_create()
arm.animation_data.action = bpy.data.actions["Idle"]
scene.frame_set(1)
char_objs = [body, lantern]

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
    e.inputs["Color"].default_value = (*mpal.lin(hexc), 1)
    o = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(e.outputs[0], o.inputs["Surface"])
    return m


def mix_hex(a, b, t):
    ca, cb = mpal.hex_rgb(a), mpal.hex_rgb(b)
    return "#" + "".join("%02X" % round(255 * (x * (1 - t) + y * t)) for x, y in zip(ca, cb))


GROUND_HEX = "#808080"
bpy.ops.mesh.primitive_plane_add(size=80, location=(0, 0, 0))
ground = bpy.context.active_object
ground.data.materials.append(flat_mat("Ground", GROUND_HEX))
blob_mat = flat_mat("Blob", mix_hex(GROUND_HEX, mpal.VOID_NAVY, 0.40))


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
for p_ in refsil:
    p_.data.materials.append(ref_mat)
ref_blob = make_blob(REF_LOC[:2], 0.4)
gameplay_extras = [ground, blob, ref_blob] + refsil
for ob in gameplay_extras:
    ob.hide_render = True

INNER = {"feat_wing_detail", "feat_bodice_trim", "feat_tabard_trim", "feat_face", "feat_team_cloth", "feat_tabard_detail", "feat_tasset_detail", "feat_greave_detail", "feat_pauldron_detail", "feat_breastplate_detail", "feat_gauntlet_detail", "feat_belt_detail", "feat_satchel_detail",
         "feat_bandage", "feat_mantle_detail", "feat_lantern_detail", "feat_brooch", "feat_biomass"}


def outline_weights(ob):
    if "outline_w" in ob.vertex_groups:
        return
    g = ob.vertex_groups.new(name="outline_w")
    inner_idx = {vg.index for vg in ob.vertex_groups if vg.name in INNER}
    none_idx = {vg.index for vg in ob.vertex_groups if vg.name == "feat_face_decal"}
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


def show(objs):
    for ob in [body, lantern] + props:
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


def want(k):
    return ONLY is None or k in ONLY


def ortho_fit(objs_fit, az, el, height_px, pad_k=0.04, pts=None):
    cam_data.type = "ORTHO"
    cam_data.sensor_fit = "AUTO"
    a, e = math.radians(az), math.radians(el)
    d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
    pts = pts if pts is not None else posed_points(objs_fit)
    center = sum(pts, Vector()) / len(pts)
    cam.location = center + d * 12.0
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    bpy.context.view_layer.update()
    inv = cam.matrix_world.inverted()
    cp = [inv @ p for p in pts]
    xs, ys = [p.x for p in cp], [p.y for p in cp]
    w, h = max(xs) - min(xs), max(ys) - min(ys)
    pad = pad_k * max(w, h) + 0.02
    w, h = w + 2 * pad, h + 2 * pad
    cam.location = cam.matrix_world @ Vector(((max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2, 0))
    scene.render.resolution_y = height_px
    scene.render.resolution_x = max(16, int(round(height_px * w / h)))
    cam_data.ortho_scale = max(w, h)
    bpy.context.view_layer.update()
    ld = (cam.matrix_world.to_3x3() @ Vector((-0.55, 0.65, 0.75))).normalized()
    set_light_all(ld)
    return cam_data.ortho_scale / max(scene.render.resolution_x, scene.render.resolution_y)


measure = {}
arm.rotation_euler = (0, 0, 0)
bpy.context.view_layer.update()
pp = posed_points([body])
measure["idle_body_bounds"] = [[round(min(p[i] for p in pp), 3) for i in range(3)],
                               [round(max(p[i] for p in pp), 3) for i in range(3)]]
pp = posed_points(char_objs)
measure["idle_with_props_bounds"] = [[round(min(p[i] for p in pp), 3) for i in range(3)],
                                     [round(max(p[i] for p in pp), 3) for i in range(3)]]

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
    pts = posed_points(char_objs)
    ys = [world_to_camera_view(scene, cam, p).y for p in pts]
    measure["gameplay_px_height_silhouette"] = round((max(ys) - min(ys)) * 1080, 2)
    a = world_to_camera_view(scene, cam, Vector((0, 0, 0)))
    b = world_to_camera_view(scene, cam, Vector((0, 0, 2.0)))
    measure["gameplay_px_height_0_to_2u"] = round(abs(b.y - a.y) * 1080, 2)
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
        render(os.path.join(OUT, f"facing_{name}.png"))
    no_border()
    arm.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()

ORTHO = {"front": (0, 6, ("azure",)), "three-quarter": (-32, 6, ("azure",)), "profile": (-90, 4, ("azure",)),
         "back": (180, 6, ("azure",)), "accent": (-14, 6, ("crimson",))}
if want("ortho"):
    scene.render.film_transparent = True
    show(char_objs)
    for name, (az, el, teams) in ORTHO.items():
        wpp = ortho_fit(char_objs, az, el, OH)
        set_outline(char_objs, ORTHO_PX * wpp)
        measure.setdefault("ortho", {})[name] = {"az": az, "el": el, "ortho_scale": round(cam_data.ortho_scale, 3)}
        for team in teams:
            set_team(team == "crimson")
            render(os.path.join(OUT, f"ortho_{name}_{team}.png"))
    set_team(False)

if want("face"):
    scene.render.film_transparent = True
    show([body])
    pts = [p for p in posed_points([body]) if p.z > 1.20]
    wpp = ortho_fit([body], -10, 3, 900, 0.03, pts)
    set_outline([body], 3.0 * wpp * 1.6)
    render(os.path.join(OUT, "face_closeup.png"))

if want("props"):
    scene.render.film_transparent = True
    show(props)
    wpp = ortho_fit(props, -30, 16, int(OH * 0.75))
    set_outline(props, ORTHO_PX * 0.83 * wpp)
    for team in ("azure", "crimson"):
        set_team(team == "crimson")
        render(os.path.join(OUT, f"props_{team}.png"))

with open(os.path.join(OUT, "measure.json"), "w") as f:
    json.dump(measure, f, indent=1)
print("MEASURE " + json.dumps(measure))
