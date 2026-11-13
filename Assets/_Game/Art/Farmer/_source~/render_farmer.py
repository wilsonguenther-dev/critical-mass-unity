"""Headless EEVEE renders of farmer.blend with the 3-band CM/Toon preview and a Void Navy outline of constant pixel width.

  Blender -b --factory-startup --python-exit-code 1 -P render_farmer.py -- <out_dir> <modes> [blend]

modes (comma list):
  game      gameplay camera (perspective, pitch 55, vertical FOV 35, 23.4 u, yaw 45), 1920x1080: Azure and Crimson on
            grass and Azure on mid-grey, each with a 2.0 u grey reference silhouette; plus measure.json (pixel heights)
  facings   the 8 screen facings at the gameplay camera (cropped renders, same camera), colour + ID mask, both teams
  bible     orthographic bible framings (front, three-quarter, profile, back, backpack-tank, staff views), transparent
  poses     harvest (staff hooking a lime biomass bush) and root-seed (seed pod in front of the bulb), transparent
  sil       8 facings at the gameplay camera, alpha only, for the 64 px silhouette sheet; [blend] may name another
            character's .blend (the Warrior's), which is opened read-only and never saved
Outline: the inverted hull is 1.5 px (inner creases); compare_farmer.py adds 1.5 px outside the silhouette, so the
outer line is 3.0 px. Raw renders are 2x (SS) and are halved by compare_farmer.py.
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
SHARED = os.path.normpath(os.path.join(HERE, "..", "..", "Characters", "_source~"))
sys.path.insert(0, SHARED)
sys.path.insert(0, HERE)
from cm_art import kit, palette as P  # noqa: E402
from farmer_art import fpalette as FP, toon3  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0]) if argv else os.path.join(HERE, "renders")
MODES = (argv[1] if len(argv) > 1 else "game,facings,bible,poses").split(",")
BLEND = os.path.abspath(argv[2]) if len(argv) > 2 else os.path.join(HERE, "farmer.blend")
FOREIGN = BLEND != os.path.join(HERE, "farmer.blend")
OUTLINE_PX = 3.0      # outer silhouette at 1x (hull INNER_PX + a post dilation of OUTLINE_PX - INNER_PX)
INNER_PX = 1.5        # inner creases: the inverted hull itself
SS = 2                # supersampling: raw renders at 2x; compare_farmer.py dilates the silhouette and halves them
PITCH, FOV, DIST, YAW = 55.0, 35.0, 23.4, 45.0
F_H = Vector((math.sin(math.radians(YAW)), -math.cos(math.radians(YAW)), 0.0))   # camera forward on the ground
R_H = F_H.cross(Vector((0, 0, 1)))                                                 # screen right on the ground
# screen facings -> armature Z rotation (rig faces -Y at 0).
FACINGS = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"]
# The camera sits at -F_H (screen-down), so facing the camera (S) is rig yaw -135 deg; E (screen right) is -45.
FACE_DEG = {"S": 225, "SE": 270, "E": 315, "NE": 0, "N": 45, "NW": 90, "W": 135, "SW": 180}
BIBLE_STANCE = [("upperleg.l", (0, 1, 0), -7), ("upperleg.r", (0, 1, 0), 7),
                ("foot.l", (0, 1, 0), 7), ("foot.r", (0, 1, 0), -7), ("upperarm.l", (0, 1, 0), -8),
                ("head", (0, 1, 0), -6), ("head", (1, 0, 0), -5)]      # head tilt + chin up: attitude

os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=BLEND)
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
arm = bpy.data.objects["Rig"]
arm.rotation_mode = "XYZ"          # the glTF import leaves QUATERNION; facings rotate the rig about Z
meshes = [o for o in bpy.data.objects if o.type == "MESH" and o.parent is arm]
body = [o for o in meshes if o.name.endswith("_Body")][0]
staff = [o for o in meshes if o.name.endswith("_Staff")]
mat = bpy.data.materials["CM_Toon"]
IS_T3 = "LIGHT" in mat.node_tree.nodes
world = bpy.data.worlds.new("W")
world.color = (0, 0, 0)
scene.world = world
cam_data = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
sun_data = bpy.data.lights.new("Key", "SUN")
sun_data.energy = 4.0
sun_data.angle = 0.0
sun = bpy.data.objects.new("Key", sun_data)
scene.collection.objects.link(sun)


def set_pose(action="Idle", frame=1, extra=()):
    arm.animation_data_create()
    arm.animation_data.action = bpy.data.actions[action]
    scene.frame_set(frame)
    bases = {pb.name: pb.matrix_basis.copy() for pb in arm.pose.bones}
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.matrix_basis = bases[pb.name]
    bpy.context.view_layer.update()
    for bone, axis, deg in extra:
        if bone == "@aim":                 # v3: turn hand.r so the staff (grip -> hook) points along `axis`
            sp = points(staff)
            pb = arm.pose.bones["hand.r"]
            grip = (arm.matrix_world @ arm.pose.bones["handslot.r"].matrix).translation
            tip = max(sp, key=lambda p: (p - grip).length)
            q = (tip - grip).normalized().rotation_difference(Vector(axis).normalized())
            head = pb.matrix.translation.copy()
            pb.matrix = Matrix.Translation(head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-head) @ pb.matrix
            bpy.context.view_layer.update()
            continue
        pb = arm.pose.bones[bone]
        head = pb.matrix.translation.copy()
        r = Matrix.Translation(head) @ Matrix.Rotation(math.radians(deg), 4, Vector(axis)) @ Matrix.Translation(-head)
        pb.matrix = r @ pb.matrix
        bpy.context.view_layer.update()


def outline(width):
    for ob in meshes:
        if IS_T3:
            toon3.add_outline(ob, width)
        else:
            sol = ob.modifiers.get("Outline") or ob.modifiers.new("Outline", "SOLIDIFY")
            sol.thickness, sol.offset, sol.use_flip_normals, sol.use_rim = width, 1.0, True, False


def no_outline():
    for ob in meshes:
        m = ob.modifiers.get("Outline")
        if m:
            ob.modifiers.remove(m)


def points(objs):
    dg = bpy.context.evaluated_depsgraph_get()
    pts = []
    for ob in objs:
        mods = [m for m in ob.modifiers if m.name == "Outline"]
        for m in mods:
            m.show_render = m.show_viewport = False
        bpy.context.view_layer.update()
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        pts.extend(ev.matrix_world @ v.co for v in me.vertices)
        ev.to_mesh_clear()
        for m in mods:
            m.show_render = m.show_viewport = True
    return pts


def light_world(v):
    v = Vector(v).normalized()
    if IS_T3:
        toon3.set_light(mat, v)
    sun.rotation_euler = (-v).to_track_quat("-Z", "Y").to_euler()


def team(crimson):
    if IS_T3:
        toon3.set_team(mat, crimson)


def maskview(on):
    if IS_T3:
        toon3.set_maskview(mat, on)
    scene.render.filter_size = 0.01 if on else 1.5


def render(path):
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


# --------------------------------------------------------------------------------------------- gameplay camera
def game_cam(res=(1920 * SS, 1080 * SS)):
    cam_data.type = "PERSP"
    cam_data.sensor_fit = "VERTICAL"
    cam_data.angle_y = math.radians(FOV)
    cam_data.clip_start, cam_data.clip_end = 0.5, 200
    p = math.radians(PITCH)
    cam.location = -F_H * DIST * math.cos(p) + Vector((0, 0, DIST * math.sin(p)))
    cam.rotation_euler = (-cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.resolution_x, scene.render.resolution_y = res
    bpy.context.view_layer.update()
    px_per_m = (1080 / 2) / math.tan(math.radians(FOV) / 2) / DIST      # at 1x
    light_world((F_H - R_H).normalized() * math.cos(math.radians(50)) + Vector((0, 0, math.sin(math.radians(50)))))
    return px_per_m


def px_box(objs):
    res = Vector((scene.render.resolution_x, scene.render.resolution_y))
    pp = [world_to_camera_view(scene, cam, p) for p in points(objs)]
    xs, ys = [p.x * res.x for p in pp], [(1 - p.y) * res.y for p in pp]
    return min(xs), min(ys), max(xs), max(ys)


def flat_mat(name, hexcol):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    e = nt.nodes.new("ShaderNodeEmission")
    e.inputs["Color"].default_value = (*P.lin(hexcol), 1)
    o = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(e.outputs[0], o.inputs["Surface"])
    return m


def ground_mat(hexcol):
    """Flat ground, one band; cast shadows take the shadow band (x0.66, 12 % toward Void Navy), never black."""
    m = bpy.data.materials.new("Ground_" + hexcol)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    d = nt.nodes.new("ShaderNodeBsdfDiffuse")
    s2r = nt.nodes.new("ShaderNodeShaderToRGB")
    nt.links.new(d.outputs[0], s2r.inputs[0])
    bw = nt.nodes.new("ShaderNodeRGBToBW")
    nt.links.new(s2r.outputs[0], bw.inputs[0])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.interpolation = "CONSTANT"
    lit = P.lin(hexcol)
    sh = [c * 0.66 * 0.88 + n * 0.12 for c, n in zip(P.lin(hexcol), P.lin(FP.VOID_NAVY))]
    ramp.color_ramp.elements[0].color = (*sh, 1)
    ramp.color_ramp.elements[1].position = 0.05
    ramp.color_ramp.elements[1].color = (*lit, 1)
    nt.links.new(bw.outputs[0], ramp.inputs[0])
    e = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(ramp.outputs[0], e.inputs[0])
    o = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(e.outputs[0], o.inputs["Surface"])
    return m


def add_ground(hexcol):
    bpy.ops.mesh.primitive_plane_add(size=80, location=(0, 0, 0))
    g = bpy.context.active_object
    g.name = "Ground"
    g.data.materials.append(ground_mat(hexcol))
    blob = (0.4 * c for c in P.lin(FP.VOID_NAVY))
    bpy.ops.mesh.primitive_circle_add(vertices=32, radius=0.55, fill_type="NGON", location=(0, 0, 0.003))
    b = bpy.context.active_object
    b.name = "ContactBlob"
    mix = "#%02X%02X%02X" % tuple(int(round(255 * (0.6 * x + 0.4 * y))) for x, y in
                                   zip(P.hex_rgb(hexcol), P.hex_rgb(FP.VOID_NAVY)))
    b.data.materials.append(flat_mat("Blob", mix))
    b.visible_shadow = False
    # 2.0 u reference silhouette, 1.8 u to screen-left
    loc = -R_H * 1.8
    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=0.26, depth=1.5, location=(loc.x, loc.y, 0.75))
    c1 = bpy.context.active_object
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=0.26, location=(loc.x, loc.y, 1.74))
    c2 = bpy.context.active_object
    ref = flat_mat("RefGrey", "#7F7F7F")
    for c in (c1, c2):
        c.data.materials.append(ref)
        c.name = "RefSilhouette"
    return [g, b, c1, c2]


def do_game():
    set_pose("Idle", 1)
    ppm = game_cam()
    outline(INNER_PX / ppm)
    arm.rotation_euler = (0, 0, math.radians(FACE_DEG["SE"]))
    bpy.context.view_layer.update()
    scene.render.film_transparent = False
    measure = {"px_per_metre_at_target": round(ppm, 2), "outline_metres": round(OUTLINE_PX / ppm, 4)}
    for gname, hexcol in (("grass", "#A3C150"), ("grey", "#8C8C8C")):
        extra = add_ground(hexcol)
        teams = ("azure", "crimson") if gname == "grass" else ("azure",)
        for t in teams:
            team(t == "crimson")
            render(os.path.join(OUT, f"game_{gname}_{t}.png"))
        for e in extra:
            bpy.data.objects.remove(e, do_unlink=True)
    scene.render.film_transparent = True          # character-only pass: its alpha drives the outer outline
    for t in ("azure", "crimson"):
        team(t == "crimson")
        render(os.path.join(OUT, f"gchar_{t}.png"))
    scene.render.film_transparent = False
    # proportions in the Idle pose (geometry, metres), facing front
    arm.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()
    if not FOREIGN:
        bp = points([body])
        top = max(p.z for p in bp)
        head = [p for p in bp if p.z > 1.345 and p.y < 0.17]
        sh = [p for p in bp if 1.08 < p.z < 1.25 and p.y < 0.20]
        tank = [p for p in bp if p.y > 0.20 and 0.95 < p.z < 1.62 and abs(p.x + 0.06) < 0.24]
        sp = points(staff)
        hw = max(p.x for p in head) - min(p.x for p in head)
        sw = max(p.x for p in sh) - min(p.x for p in sh)
        measure["proportions_idle_m"] = {
            "height": round(top, 3), "chin_z": 1.345, "head_plus_hair_fraction": round((top - 1.345) / top, 3),
            "head_with_hair_width": round(hw, 3), "shoulder_line_width": round(sw, 3),
            "shoulder_over_head": round(sw / hw, 3),
            "tank_top_z": round(max(p.z for p in tank), 3), "shoulder_top_z": round(max(p.z for p in sh), 3),
            "staff_extent": round((max(sp, key=lambda p: p.z) - min(sp, key=lambda p: p.z)).length, 3),
            "staff_top_z": round(max(p.z for p in sp), 3),
            "pure_height_px": round(top * math.cos(math.radians(PITCH)) * ppm, 1)}
    # pixel heights per facing, geometry only (feet to top of hair), body without the staff
    for f in FACINGS:
        arm.rotation_euler = (0, 0, math.radians(FACE_DEG[f]))
        bpy.context.view_layer.update()
        x0, y0, x1, y1 = px_box([body])
        sx0, sy0, sx1, sy1 = px_box(meshes)
        top = max(points([body]), key=lambda p: p.z)
        res_y = scene.render.resolution_y / SS
        feet_y = (1 - world_to_camera_view(scene, cam, Vector((0, 0, 0))).y) * res_y
        top_y = (1 - world_to_camera_view(scene, cam, top).y) * res_y
        measure[f] = {"feet_to_hair_top_px": round(feet_y - top_y, 1),
                      "body_bbox_px_h": round((y1 - y0) / SS, 1), "body_px_w": round((x1 - x0) / SS, 1),
                      "with_staff_px_h": round((sy1 - sy0) / SS, 1), "with_staff_px_w": round((sx1 - sx0) / SS, 1)}
    with open(os.path.join(OUT, "measure.json"), "w") as fh:
        json.dump(measure, fh, indent=1)
    print("MEASURE", json.dumps({f: measure[f]["feet_to_hair_top_px"] for f in FACINGS}),
          json.dumps(measure.get("proportions_idle_m")))


def do_facings(alpha_only=False):
    set_pose("Idle", 1)
    ppm = game_cam()
    outline(INNER_PX / ppm)
    scene.render.film_transparent = True
    scene.render.use_border = True
    scene.render.use_crop_to_border = True
    tag = "sil" if alpha_only else "facing"
    for f in FACINGS:
        arm.rotation_euler = (0, 0, math.radians(FACE_DEG[f]))
        bpy.context.view_layer.update()
        x0, y0, x1, y1 = px_box(meshes)
        m = 10 * SS
        rx, ry = scene.render.resolution_x, scene.render.resolution_y
        scene.render.border_min_x = max(0, (x0 - m) / rx)
        scene.render.border_max_x = min(1, (x1 + m) / rx)
        scene.render.border_min_y = max(0, 1 - (y1 + m) / ry)
        scene.render.border_max_y = min(1, 1 - (y0 - m) / ry)
        if alpha_only:
            render(os.path.join(OUT, f"{tag}_{f}.png"))
            continue
        for t in ("azure", "crimson"):
            team(t == "crimson")
            maskview(False)
            render(os.path.join(OUT, f"{tag}_{f}_{t}.png"))
            maskview(True)
            render(os.path.join(OUT, f"mask_{f}_{t}.png"))
        maskview(False)
    scene.render.use_border = False
    arm.rotation_euler = (0, 0, 0)


# --------------------------------------------------------------------------------------------- orthographic bible framings
def ortho_shot(name, az, el, objs, height=720, teams=("azure",), cam_dist=12.0):
    a, e = math.radians(az), math.radians(el)
    d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
    cam_data.type = "ORTHO"
    cam_data.sensor_fit = "AUTO"
    pts = points(objs)
    c = sum(pts, Vector()) / len(pts)
    cam.location = c + d * cam_dist
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    bpy.context.view_layer.update()
    inv = cam.matrix_world.inverted()
    cp = [inv @ p for p in pts]
    xs, ys = [p.x for p in cp], [p.y for p in cp]
    w0, h0 = max(xs) - min(xs), max(ys) - min(ys)
    height *= SS
    ppm = height / (h0 * 1.03)
    pad = 2 * (OUTLINE_PX * SS / ppm) + 0.02
    w, h = w0 + pad, h0 + pad
    cam.location = cam.matrix_world @ Vector(((max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2, 0))
    scene.render.resolution_y = height
    scene.render.resolution_x = max(16, int(round(height * w / h)))
    cam_data.ortho_scale = max(w, h)
    ppm = (height / h if h >= w else scene.render.resolution_x / w) / SS     # pixels per metre at 1x
    outline(INNER_PX / ppm)
    light_world(cam.matrix_world.to_3x3() @ Vector((-0.5, 0.6, 0.62)))
    scene.render.film_transparent = True
    for t in teams:
        team(t == "crimson")
        render(os.path.join(OUT, f"{name}_{t}.png"))


def isolate_tank():
    """Temporary copy of the body keeping only the globe tank (its feature groups; the hose above the hip)."""
    import bmesh
    me = body.data.copy()
    ob = body.copy()
    ob.data = me
    scene.collection.objects.link(ob)
    bm = bmesh.new()
    bm.from_mesh(me)
    keep_g = {body.vertex_groups[n].index for n in ("feat_tank", "feat_leaves", "feat_tank_glint")
              if n in body.vertex_groups}
    team_g = body.vertex_groups["feat_team_cloth"].index
    dl = bm.verts.layers.deform.active
    keep = set()
    for v in bm.verts:
        gs = v[dl].keys()
        if any(g in keep_g for g in gs) and v.co.z > 0.86 or (team_g in gs and v.co.y > 0.25):
            keep.add(v)
    kill = [v for v in bm.verts if v not in keep]
    bmesh.ops.delete(bm, geom=kill, context="VERTS")
    bm.to_mesh(me)
    bm.free()
    return ob


def show_only(objs):
    for ob in bpy.data.objects:
        if ob.type == "MESH":
            ob.hide_render = ob not in objs


# --------------------------------------------------------------------------------------------- capture poses (fit)
CAPTURE = os.path.join(HERE, "capture_poses.json")
# world-axis rotations at bone heads, applied parent first (same mechanism as set_pose extra)
FIT_PARAMS = [("spine", (1, 0, 0)), ("spine", (0, 1, 0)), ("spine", (0, 0, 1)),
              ("head", (1, 0, 0)), ("head", (0, 1, 0)), ("head", (0, 0, 1)),
              ("upperarm.r", (1, 0, 0)), ("upperarm.r", (0, 1, 0)), ("upperarm.r", (0, 0, 1)),
              ("lowerarm.r", (1, 0, 0)), ("lowerarm.r", (0, 0, 1)), ("hand.r", (1, 0, 0)), ("hand.r", (0, 1, 0)),
              ("upperarm.l", (1, 0, 0)), ("upperarm.l", (0, 1, 0)), ("upperarm.l", (0, 0, 1)),
              ("lowerarm.l", (1, 0, 0)), ("lowerarm.l", (0, 0, 1)),
              ("upperleg.l", (1, 0, 0)), ("upperleg.l", (0, 1, 0)), ("upperleg.r", (1, 0, 0)), ("upperleg.r", (0, 1, 0)),
              ("lowerleg.l", (1, 0, 0)), ("lowerleg.r", (1, 0, 0))]
# natural ranges only (an unbounded fit folds the spine and neck to game the overlap number)
FIT_LIMIT = {"spine": 12, "head": 18, "upperarm.r": 130, "lowerarm.r": 120, "hand.r": 60, "upperarm.l": 90,
             "lowerarm.l": 90, "upperleg.l": 25, "upperleg.r": 25, "lowerleg.l": 25, "lowerleg.r": 25}
RAISED = {("upperarm.r", (1, 0, 0)): -45.0, ("lowerarm.r", (1, 0, 0)): -70.0}
ACROSS = {("upperarm.r", (1, 0, 0)): -50.0, ("upperarm.r", (0, 0, 1)): 30.0, ("lowerarm.r", (1, 0, 0)): -60.0}
FIT_SET = {
    "front": dict(base=("Idle", 1), az=[0], el=4, rng=14, seeds=[RAISED]),
    "three-quarter": dict(base=("Idle", 1), az=[-35], el=4, rng=25, seeds=[RAISED, ACROSS]),
    "profile": dict(base=("Idle", 1), az=[-90], el=2, rng=14, seeds=[RAISED]),
    "back": dict(base=("Idle", 1), az=[180], el=4, rng=14, seeds=[RAISED]),
    "pose-harvest": dict(base=("Dualwield_Melee_Attack_Slice", 19), az=[-62, -100], el=6, rng=40, props="harvest"),
    "pose-root-seed": dict(base=("1H_Melee_Attack_Slice_Horizontal", 11), az=[-62, -95], el=6, rng=40, props="seed"),
}
BUSH = [((0, 0, 0.18), 0.17), ((0.16, 0.05, 0.12), 0.12), ((-0.15, -0.04, 0.12), 0.13), ((0.03, -0.14, 0.30), 0.11),
        ((-0.04, 0.12, 0.30), 0.10)]


def base_mats(action, frame):
    set_pose(action, frame)
    return {pb.name: pb.matrix_basis.copy() for pb in arm.pose.bones}


def pose_from(bases, extra):
    for pb in arm.pose.bones:
        pb.matrix_basis = bases[pb.name]
    bpy.context.view_layer.update()
    for bone, axis, deg in extra:
        pb = arm.pose.bones[bone]
        head = pb.matrix.translation.copy()
        r = Matrix.Translation(head) @ Matrix.Rotation(math.radians(deg), 4, Vector(axis)) @ Matrix.Translation(-head)
        pb.matrix = r @ pb.matrix
        bpy.context.view_layer.update()


def prop_spheres(kind):
    """Render-only props of the action poses, from the posed staff: (centre, radius) spheres + optional vine segment."""
    sp = points(staff)
    grip = (arm.matrix_world @ arm.pose.bones["handslot.r"].matrix).translation
    tip = max(sp, key=lambda p: (p - grip).length)
    if kind == "harvest":
        hook = tip
        return [(Vector((hook.x + x, hook.y + y, z)), r) for (x, y, z), r in BUSH], None
    d = (tip - grip).normalized()
    pod = tip + d * 0.30
    return [(pod, 0.17)], (tip, pod)


def view_basis(az, el):
    a, e = math.radians(az), math.radians(el)
    d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
    f = -d
    right = f.cross(Vector((0, 0, 1))).normalized()
    up = right.cross(f).normalized()
    return d, right, up


def tri_world(objs):
    import numpy as np
    dg = bpy.context.evaluated_depsgraph_get()
    out = []
    for ob in objs:
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        me.calc_loop_triangles()
        co = np.empty(len(me.vertices) * 3, np.float32)
        me.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        mw = np.array(ev.matrix_world, dtype=np.float32)
        co = co @ mw[:3, :3].T + mw[:3, 3]
        t = np.empty(len(me.loop_triangles) * 3, np.int32)
        me.loop_triangles.foreach_get("vertices", t)
        out.append(co[t])
        ev.to_mesh_clear()
    return np.concatenate(out)


class Raster:
    W, H = 512, 256

    def __init__(self):
        import gpu
        try:
            gpu.init()
        except Exception:
            pass
        self.gpu = gpu
        self.off = gpu.types.GPUOffScreen(self.W, self.H)
        self.sh = gpu.shader.from_builtin("UNIFORM_COLOR")

    def mask(self, tris3d, extra2d, right, up):
        """Silhouette normalised like compare_farmer.iou: bbox height -> H-2 rows, centred by bbox width."""
        import numpy as np
        from gpu_extras.batch import batch_for_shader
        R = np.array(right, np.float32)
        U = np.array(up, np.float32)
        P = np.stack([tris3d @ R, tris3d @ U], 1)
        if extra2d is not None and len(extra2d):
            P = np.concatenate([P, extra2d])
        u0, u1, v0, v1 = P[:, 0].min(), P[:, 0].max(), P[:, 1].min(), P[:, 1].max()
        s = (self.H - 2) / (v1 - v0)
        col = (P[:, 0] - (u0 + u1) / 2) * s + self.W / 2
        row = (v1 - P[:, 1]) * s + 1
        ndc = np.stack([2 * col / self.W - 1, 1 - 2 * row / self.H], 1).astype(np.float32)
        gpu = self.gpu
        with self.off.bind():
            fb = gpu.state.active_framebuffer_get()
            fb.clear(color=(0, 0, 0, 0))
            with gpu.matrix.push_pop():
                gpu.matrix.load_matrix(Matrix.Identity(4))
                gpu.matrix.load_projection_matrix(Matrix.Identity(4))
                b = batch_for_shader(self.sh, "TRIS", {"pos": ndc})
                self.sh.uniform_float("color", (1, 1, 1, 1))
                b.draw(self.sh)
            buf = fb.read_color(0, 0, self.W, self.H, 1, 0, "UBYTE")
        a = np.array(buf.to_list(), dtype=np.uint8).reshape(self.H, self.W)
        return a[::-1] > 127


def disk_tris(c2, r, n=20):
    import numpy as np
    out = []
    for k in range(n):
        a0, a1 = 2 * math.pi * k / n, 2 * math.pi * (k + 1) / n
        out += [c2, (c2[0] + r * math.cos(a0), c2[1] + r * math.sin(a0)), (c2[0] + r * math.cos(a1), c2[1] + r * math.sin(a1))]
    return np.array(out, np.float32)


def norm_bible(m, W=512, H=256):
    import numpy as np
    ys, xs = np.nonzero(m)
    m = m[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    h, w = m.shape
    s = (H - 2) / h
    nw = max(1, int(round(w * s)))
    yi = np.clip((np.arange(H - 2) / s).astype(int), 0, h - 1)
    xi = np.clip((np.arange(nw) / s).astype(int), 0, w - 1)
    r = m[yi][:, xi]
    out = np.zeros((H, W), bool)
    x0 = (W - nw) // 2
    lo, hi = max(0, x0), min(W, x0 + nw)
    out[1:H - 1, lo:hi] = r[:, lo - x0:hi - x0]
    return out


def do_fit():
    import numpy as np
    import time
    rast = Raster()
    show_only(meshes)
    result = {}
    only = os.environ.get("FIT_ONLY", "")
    if os.path.exists(CAPTURE):
        with open(CAPTURE) as fh:
            result = json.load(fh)
    for name, cfg in FIT_SET.items():
        if only and name not in only.split(","):
            continue
        bib = norm_bible(np.load(os.path.join(OUT, f"mask-{name}.npy")))
        bib_m = bib[:, ::-1]
        bases = base_mats(*cfg["base"])
        props = cfg.get("props")
        t0 = time.time()
        nev = [0]
        lastm = [None]

        def score(x, az, el, ref=bib):
            nev[0] += 1
            pose_from(bases, [(b, ax, d) for (b, ax), d in zip(FIT_PARAMS, x) if abs(d) > 1e-6])
            _d, right, up = view_basis(az, el)
            tris = tri_world(meshes)
            extra = None
            if props:
                sph, vine = prop_spheres(props)
                parts = []
                for c, r in sph:
                    parts.append(disk_tris((c.dot(right), c.dot(up)), r))
                if vine:
                    a, b = vine
                    pa, pb_ = np.array((a.dot(right), a.dot(up))), np.array((b.dot(right), b.dot(up)))
                    n = pb_ - pa
                    n = np.array((-n[1], n[0])) / max(np.linalg.norm(n), 1e-6) * 0.05
                    parts.append(np.array([pa + n, pa - n, pb_ + n, pa - n, pb_ - n, pb_ + n], np.float32))
                extra = np.concatenate(parts)
            m = rast.mask(tris, extra, right, up)
            lastm[0] = m
            return float((m & ref).sum()) / max(float((m | ref).sum()), 1.0)

        def descend(x, az, el, az0, steps):
            cur = score(x, az, el)
            for step in steps:
                for _sweep in range(2):
                    improved = False
                    for i in range(len(x) + 2):
                        for sgn in (1, -1):
                            if i < len(x):
                                lim = FIT_LIMIT.get(FIT_PARAMS[i][0], 25)
                                nx = list(x)
                                nx[i] = max(-lim, min(lim, nx[i] + sgn * step))
                                if nx[i] == x[i]:
                                    continue
                                v = score(nx, az, el)
                                if v > cur + 1e-4:
                                    x, cur, improved = nx, v, True
                            elif i == len(x):
                                naz = max(az0 - cfg["rng"], min(az0 + cfg["rng"], az + sgn * step / 2))
                                v = score(x, naz, el)
                                if v > cur + 1e-4:
                                    az, cur, improved = naz, v, True
                            else:
                                nel = max(0.0, min(30.0, el + sgn * step / 2))
                                v = score(x, az, nel)
                                if v > cur + 1e-4:
                                    el, cur, improved = nel, v, True
                    if not improved:
                        break
            return cur, x, az, el

        import random
        rng = random.Random(7)
        starts = [([0.0] * len(FIT_PARAMS), float(a), float(cfg["el"]), float(a)) for a in cfg["az"]]
        for sd in cfg.get("seeds", ()):           # hand-placed starting poses (staff raised / held across)
            sx = [0.0] * len(FIT_PARAMS)
            for (b, ax), d in sd.items():
                sx[FIT_PARAMS.index((b, ax))] = d
            starts += [(sx, float(a), float(cfg["el"]), float(a)) for a in cfg["az"]]
        prev = result.get(name) if os.environ.get("FIT_WARM", "1") == "1" else None
        if prev:
            px = [0.0] * len(FIT_PARAMS)
            for b, ax, d in prev["extra"]:
                k = FIT_PARAMS.index((b, tuple(ax)))
                px[k] = d
            near = min(cfg["az"], key=lambda a: abs(((prev["az"] - a + 180) % 360) - 180))
            starts.append((px, float(prev["az"]), float(prev["el"]), float(near)))
        best = None
        for x0, az, el, az0 in starts:
            r = descend(list(x0), az, el, az0, (32, 16, 8, 4, 2))
            print(f"FIT {name} start az0={az0} iou={r[0]:.3f} az={r[2]:.1f} el={r[3]:.1f} evals={nev[0]} "
                  f"t={time.time() - t0:.0f}s", flush=True)
            if best is None or r[0] > best[0]:
                best = r + (az0,)
        for kick in range(int(os.environ.get("FIT_KICKS", "4"))):
            cur, x, az, el, az0 = best
            nx = list(x)
            for i in rng.sample(range(len(x)), 6):
                nx[i] += rng.choice((-1, 1)) * rng.choice((12, 24, 40))
            r = descend(nx, az, el, az0, (8, 4, 2))
            if r[0] > best[0]:
                best = r + (az0,)
            print(f"FIT {name} kick {kick} iou={r[0]:.3f} best={best[0]:.3f} t={time.time() - t0:.0f}s", flush=True)
        best = best[:4]
        cur, x, az, el = best
        mir = score(x, az, el, bib_m)
        np.save(os.path.join(OUT, f"fit-{name}.npy"), np.stack([lastm[0], bib]))
        result[name] = {"base": list(cfg["base"]), "az": round(az, 2), "el": round(el, 2),
                        "extra": [[b, list(ax), round(d, 2)] for (b, ax), d in zip(FIT_PARAMS, x) if abs(d) > 1e-6],
                        "iou_fit_raster": round(cur, 3), "iou_vs_mirrored_bible": round(mir, 3)}
        with open(CAPTURE, "w") as fh:
            json.dump(result, fh, indent=1)
            fh.write("\n")


def capture(name, fallback):
    if os.path.exists(CAPTURE):
        with open(CAPTURE) as fh:
            c = json.load(fh).get(name)
        if c:
            return c["base"][0], c["base"][1], [(b, tuple(ax), d) for b, ax, d in c["extra"]], c["az"], c["el"]
    return fallback


def do_bible():
    show_only(meshes)
    for name, az in (("front", 0), ("three-quarter", -32), ("profile", -90), ("back", 180)):
        act, fr, extra, az, el = capture(name, ("Idle", 1, BIBLE_STANCE, az, 0))
        set_pose(act, fr, extra)
        ortho_shot(name, az, el, meshes, teams=("azure", "crimson") if name in ("front", "back") else ("azure",))
    set_pose("Idle", 1, BIBLE_STANCE)
    tank = isolate_tank()
    show_only([tank])
    ortho_shot("backpack-tank", 180, 0, [tank], height=560)
    meshes_saved = list(meshes)
    meshes.append(tank)
    show_only(staff)
    set_pose("Idle", 1)
    sp = points(staff)                                   # stand the staff upright for its own views (bible p07)
    grip = (arm.matrix_world @ arm.pose.bones["handslot.r"].matrix).translation
    tip = max(sp, key=lambda p: (p - grip).length)
    q = (tip - grip).normalized().rotation_difference(Vector((0, 0, 1)))
    saved = arm.matrix_world.copy()
    arm.matrix_world = Matrix.Translation(grip) @ q.to_matrix().to_4x4() @ Matrix.Translation(-grip) @ saved
    bpy.context.view_layer.update()
    for name, az in (("staff-front", 0), ("staff-side", 90), ("staff-back", 180)):
        ortho_shot(name, az, 0, staff, height=720)
    arm.matrix_world = saved
    bpy.context.view_layer.update()
    bpy.data.objects.remove(tank, do_unlink=True)
    meshes[:] = meshes_saved
    show_only(meshes)


def prop_mesh(name, spheres, vine, hexcol):
    import bmesh
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    for c, r in spheres:
        g = bmesh.ops.create_uvsphere(bm, u_segments=14, v_segments=8, radius=r)
        bmesh.ops.translate(bm, vec=c, verts=g["verts"])
    if vine:
        a, b = vine
        g = bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.05, radius2=0.05, depth=(b - a).length)
        q = (b - a).normalized().to_track_quat("Z", "Y")
        bmesh.ops.transform(bm, matrix=Matrix.Translation((a + b) / 2) @ q.to_matrix().to_4x4(), verts=g["verts"])
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    scene.collection.objects.link(ob)
    ob.data.materials.append(flat_mat(name + "Mat", hexcol))
    ob.data.materials.append(toon3.make_outline_material())
    sol = ob.modifiers.new("Outline", "SOLIDIFY")
    sol.offset, sol.use_flip_normals, sol.use_rim, sol.material_offset = 1.0, True, False, 1
    return ob


# v3: hand-made action poses (confident, readable, head up toward the camera), NOT overlap-fitted.
# (bone, world axis, degrees) applied in order at bone heads on top of Idle frame 1. Rig faces -Y; camera az -62 sees the
# character's right/front, facing screen right as p07 draws both actions.
HAND_POSES = {
    "pose-harvest": ("Idle", 1, [
        ("upperleg.r", (1, 0, 0), -78), ("lowerleg.r", (1, 0, 0), 88), ("upperleg.l", (1, 0, 0), 52),
        ("lowerleg.l", (1, 0, 0), 12), ("upperleg.l", (0, 1, 0), -8), ("upperleg.r", (0, 1, 0), 8),
        ("spine", (1, 0, 0), 32), ("spine", (0, 0, 1), -15),
        ("upperarm.r", (1, 0, 0), -50), ("lowerarm.r", (1, 0, 0), -20),
        ("upperarm.l", (1, 0, 0), -55), ("upperarm.l", (0, 0, 1), -35), ("lowerarm.l", (1, 0, 0), -35),
        ("@aim", (-0.15, -0.75, -0.65), 0),
        ("head", (1, 0, 0), -30), ("head", (0, 0, 1), -28), ("head", (0, 1, 0), 10)], -62, 6),
    "pose-root-seed": ("Idle", 1, [
        ("upperleg.r", (1, 0, 0), -75), ("lowerleg.r", (1, 0, 0), 84), ("upperleg.l", (1, 0, 0), 54),
        ("lowerleg.l", (1, 0, 0), 8), ("upperleg.l", (0, 1, 0), -8), ("upperleg.r", (0, 1, 0), 8),
        ("spine", (1, 0, 0), 20), ("spine", (0, 0, 1), -20),
        ("upperarm.r", (1, 0, 0), -82), ("lowerarm.r", (1, 0, 0), -5),
        ("upperarm.l", (1, 0, 0), 40), ("lowerarm.l", (1, 0, 0), -35),
        ("@aim", (-0.05, -1.0, 0.08), 0),
        ("head", (1, 0, 0), -22), ("head", (0, 0, 1), -30), ("head", (0, 1, 0), -8)], -62, 6),
}


def do_poses():
    show_only(meshes)
    fall = {"pose-harvest": ("Dualwield_Melee_Attack_Slice", 19, [("lowerarm.r", (1, 0, 0), 28)], -62, 0),
            "pose-root-seed": ("1H_Melee_Attack_Slice_Horizontal", 11, [], -62, 0)}
    for name, kind, col in (("pose-harvest", "harvest", FP.FARMER["f_biomass"][0]),
                            ("pose-root-seed", "seed", FP.FARMER["f_wood"][0])):
        act, fr, extra, az, el = HAND_POSES[name]
        set_pose(act, fr, extra)
        sph, vine = prop_spheres(kind)
        ob = prop_mesh("Prop", sph, vine, col)
        meshes.append(ob)
        ortho_shot(name, az, el, meshes, height=600)
        meshes.remove(ob)
        bpy.data.objects.remove(ob, do_unlink=True)


def do_face():
    """Large face close-up, orthographic front, head held level (for sbs-face beside the bible's head)."""
    show_only(meshes)
    set_pose("Idle", 1)
    hb = arm.pose.bones["head"]
    c = (arm.matrix_world @ hb.matrix).translation + Vector((0, 0, 0.30))
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = 0.95
    cam.location = c + Vector((0, -12.0, 0))
    cam.rotation_euler = Vector((0, 1, 0)).to_track_quat("-Z", "Y").to_euler()
    scene.render.resolution_x = scene.render.resolution_y = 640 * SS
    ppm = 640 / 0.95
    outline(INNER_PX / ppm)
    light_world(cam.matrix_world.to_3x3() @ Vector((-0.5, 0.6, 0.62)))
    scene.render.film_transparent = True
    team(False)
    render(os.path.join(OUT, "face_azure.png"))


if "fit" in MODES:
    do_fit()
if "face" in MODES:
    do_face()


if "game" in MODES:
    do_game()
if "facings" in MODES:
    do_facings()
if "bible" in MODES:
    do_bible()
if "poses" in MODES:
    do_poses()
if "sil" in MODES:
    do_facings(alpha_only=True)
print("RENDER_DONE", OUT, MODES, "foreign" if FOREIGN else "farmer")
