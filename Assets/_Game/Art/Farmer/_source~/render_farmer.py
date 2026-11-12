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
                ("foot.l", (0, 1, 0), 7), ("foot.r", (0, 1, 0), -7), ("upperarm.l", (0, 1, 0), -8)]

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
        head = [p for p in bp if p.z > 1.30 and p.y < 0.17]
        sh = [p for p in bp if 1.08 < p.z < 1.25 and p.y < 0.20]
        tank = [p for p in bp if p.y > 0.20 and 0.95 < p.z < 1.62 and abs(p.x + 0.06) < 0.24]
        sp = points(staff)
        hw = max(p.x for p in head) - min(p.x for p in head)
        sw = max(p.x for p in sh) - min(p.x for p in sh)
        measure["proportions_idle_m"] = {
            "height": round(top, 3), "chin_z": 1.29, "head_plus_hair_fraction": round((top - 1.29) / top, 3),
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
    keep_g = {body.vertex_groups[n].index for n in ("feat_tank", "feat_leaves", "feat_tank_glint", "feat_hose")
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


def do_bible():
    set_pose("Idle", 1, BIBLE_STANCE)
    show_only(meshes)
    for name, az in (("front", 0), ("three-quarter", -32), ("profile", -90), ("back", 180)):
        ortho_shot(name, az, 0, meshes, teams=("azure", "crimson") if name in ("front", "back") else ("azure",))
    tank = isolate_tank()
    show_only([tank])
    ortho_shot("backpack-tank", 180, 0, [tank], height=560)
    meshes_saved = list(meshes)
    meshes.append(tank)
    show_only(staff)
    set_pose("Idle", 1)
    for name, az in (("staff-front", 0), ("staff-side", 90), ("staff-back", 180)):
        ortho_shot(name, az, 0, staff, height=720)
    bpy.data.objects.remove(tank, do_unlink=True)
    meshes[:] = meshes_saved
    show_only(meshes)


def biomass_bush(at):
    """Render-only lime bush (props are out of scope): self-lit lime blobs + dark leaves around them."""
    import bmesh
    me = bpy.data.meshes.new("Bush")
    bm = bmesh.new()
    lime = bpy.data.materials.new("BushLime")
    lime.use_nodes = True
    blobs = [((0, 0, 0.18), 0.17), ((0.16, 0.05, 0.12), 0.12), ((-0.15, -0.04, 0.12), 0.13), ((0.03, -0.14, 0.30), 0.11),
             ((-0.04, 0.12, 0.30), 0.10)]
    for (x, y, z), r in blobs:
        g = bmesh.ops.create_uvsphere(bm, u_segments=14, v_segments=8, radius=r)
        bmesh.ops.translate(bm, vec=Vector((at.x + x, at.y + y, z)), verts=g["verts"])
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new("Bush", me)
    scene.collection.objects.link(ob)
    ob.data.materials.append(flat_mat("BushLime", FP.FARMER["f_biomass"][0]))
    sol = ob.modifiers.new("Outline", "SOLIDIFY")
    sol.offset, sol.use_flip_normals, sol.use_rim = 1.0, True, False
    ob.data.materials.append(bpy.data.materials["CM_Toon_Outline"])
    sol.material_offset = 1
    return ob, sol


def do_poses():
    show_only(meshes)
    # harvest: dual-wield slice frame 19 puts the staff level in front; the elbow tips it down onto a bush
    set_pose("Dualwield_Melee_Attack_Slice", 19, [("lowerarm.r", (1, 0, 0), 28), ("upperleg.l", (0, 1, 0), -8),
                                                    ("upperleg.r", (0, 1, 0), 8)])
    hook = max(points(staff), key=lambda p: -p.y)
    bush, sol = biomass_bush(Vector((hook.x, hook.y + 0.05, 0)))
    meshes.append(bush)
    sol.thickness = 0.01
    ortho_shot("pose-harvest", -62, 0, meshes, height=600)
    meshes.remove(bush)
    bpy.data.objects.remove(bush, do_unlink=True)
    # root-seed: horizontal slice frame 11 holds the staff forward; the pod sits in front of the bulb
    set_pose("1H_Melee_Attack_Slice_Horizontal", 11, [("upperleg.l", (0, 1, 0), -10), ("upperleg.r", (0, 1, 0), 10)])
    tip = max(points(staff), key=lambda p: -p.y)
    import bmesh
    me = bpy.data.meshes.new("Pod")
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=0.17)
    bmesh.ops.translate(bm, vec=Vector((tip.x, tip.y - 0.45, tip.z + 0.05)), verts=bm.verts)
    bm.to_mesh(me)
    bm.free()
    pod = bpy.data.objects.new("Pod", me)
    scene.collection.objects.link(pod)
    pod.data.materials.append(flat_mat("PodWood", FP.FARMER["f_wood"][0]))
    meshes.append(pod)
    ortho_shot("pose-root-seed", -62, 0, meshes, height=600)
    meshes.remove(pod)


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
