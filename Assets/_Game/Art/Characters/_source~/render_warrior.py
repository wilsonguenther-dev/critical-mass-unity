"""Headless EEVEE renders of warrior.blend with the CM/Toon preview (3 bands + team rim + Void Navy hull).

  Blender -b --factory-startup --python-exit-code 1 -P render_warrior.py -- <mode> <out_dir> [height_px]

modes
  bible    orthographic bible framings (U-017: one fixed key light from the camera's upper left), Azure, plus the
           Crimson accent-swap: front, three-quarter, profile, back, cleave, shield-charge, front-shield, crimson.
           <framing>.png (transparent) + <framing>.mask.png (ID mask: white = the asset)
  game     the gameplay camera (perspective, pitch 55, vertical FOV 35, 23.4 u from the target, yaw 45), 1920x1080,
           on grass with a 2.0 u reference silhouette, Azure and Crimson: game_<team>.png
  facings  8 facings at the gameplay camera (screen directions N..NW, S = front), Azure colour + team ID mask:
           facing_<dir>.png, facing_<dir>.id.png (white = team colour, grey = rest of the character), cropped
           300x300 around screen centre (pixel scale unchanged: 1080p)

Capture poses are render-time only: a KayKit clip frame plus world-axis joint rotations (POSES). Nothing here
touches the mesh, the rig rest pose or the FBX.
"""
import math
import os
import sys

sys.dont_write_bytecode = True
import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from cm_art import palette, toon  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
MODE = argv[0] if argv else "bible"
OUT = os.path.abspath(argv[1]) if len(argv) > 1 else os.path.join(HERE, "renders")
HEIGHT = int(argv[2]) if len(argv) > 2 else 720
ONLY = set(argv[3].split(",")) if len(argv) > 3 else None

# Gameplay camera (ART-DIRECTION 2.1, decided by the owner)
CAM_PITCH, CAM_FOV, CAM_DIST, CAM_YAW = 55.0, 35.0, 23.4, 45.0
OUTLINE_GAME_PX = 3.0
OUTLINE_BIBLE = 0.016        # metres on the 2.0 u figure: the bible crops draw ~0.8 % of figure height

LEGS = [("upperleg.l", (0, 1, 0), -9), ("upperleg.r", (0, 1, 0), 9), ("foot.l", (0, 1, 0), 9), ("foot.r", (0, 1, 0), -9)]
STANCE = LEGS + [("upperarm.l", (0, 1, 0), 8), ("upperarm.r", (0, 1, 0), -10)]


def shield_turn(deg):
    """Turn the shield about the vertical through the left wrist (deg > 0 turns its face toward +X)."""
    return [("hand.l", (0, 0, 1), deg)]


# framing: (clip, frame, joint rotations, camera azimuth deg, camera elevation deg, team, fit)
# azimuth 0 = camera in front (-Y); -90 = camera on the character's right (he faces screen right)
CLEAVE = LEGS + [("upperleg.l", (1, 0, 0), 25), ("upperleg.r", (1, 0, 0), -25), ("spine", (0, 1, 0), 10),
                 ("upperarm.r", (0, 1, 0), -95), ("upperarm.r", (1, 0, 0), 30), ("lowerarm.r", (0, 1, 0), -20)]
CHARGE = LEGS + [("upperleg.l", (1, 0, 0), 30), ("upperleg.r", (1, 0, 0), -30), ("spine", (1, 0, 0), 12)]
POSES = {
    "front": ("Idle", 1, STANCE + shield_turn(19), 0, 6, "azure", "all"),
    "three-quarter": ("Idle", 1, STANCE + shield_turn(-17), -25, 6, "azure", "all"),
    "profile": ("Idle", 1, STANCE + shield_turn(-50), -90, 4, "azure", "all"),
    "back": ("Idle", 1, STANCE, 180, 6, "azure", "all"),
    "cleave": ("Idle", 1, CLEAVE, -20, 6, "azure", "all"),
    "shield-charge": ("Block", 10, CHARGE, -30, 6, "azure", "all"),
    "front-shield": ("Idle", 1, STANCE, 0, 6, "azure", "shield"),
    "crimson": ("Idle", 1, STANCE + shield_turn(19), -12, 6, "crimson", "all"),
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
mat = bpy.data.materials["CM_Toon"]
outline_mat = toon.make_outline_material()
id_team = toon.make_flat_material("ID_Team", "#FFFFFF")
id_rest = toon.make_flat_material("ID_Rest", "#808080")
id_mat = toon.make_flat_material("ID_TeamMask", team_white=True)
hulls = {ob.name: toon.add_outline(ob, OUTLINE_BIBLE, outline_mat) for ob in meshes}

cam_data = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
world = bpy.data.worlds.new("W")
world.color = (0.02, 0.02, 0.03)
scene.world = world


def pose(clip, frame, rots):
    arm.animation_data_create()
    arm.animation_data.action = bpy.data.actions[clip]
    scene.frame_set(frame)
    bases = {pb.name: pb.matrix_basis.copy() for pb in arm.pose.bones}
    arm.animation_data.action = None          # freeze the clip frame so the edits below survive the render
    for pb in arm.pose.bones:
        pb.matrix_basis = bases[pb.name]
    bpy.context.view_layer.update()
    for bone, axis, deg in rots:
        pb = arm.pose.bones[bone]
        head = pb.matrix.translation.copy()
        rot = Matrix.Translation(head) @ Matrix.Rotation(math.radians(deg), 4, Vector(axis)) @ Matrix.Translation(-head)
        pb.matrix = rot @ pb.matrix
        bpy.context.view_layer.update()
    # ground the feet (some clips lift the hips): lowest posed vertex at z = 0
    zmin = min(p.z for p in posed_points([bpy.data.objects["Warrior_Body"]]))
    arm.location.z -= zmin
    bpy.context.view_layer.update()


def posed_points(obs):
    dg = bpy.context.evaluated_depsgraph_get()
    pts = []
    for ob in obs:
        hull = hulls.get(ob.name)
        if hull:
            hull.show_render = hull.show_viewport = False
        bpy.context.view_layer.update()
        ev = ob.evaluated_get(bpy.context.evaluated_depsgraph_get())
        me = ev.to_mesh()
        pts.extend(ev.matrix_world @ v.co for v in me.vertices)
        ev.to_mesh_clear()
        if hull:
            hull.show_render = hull.show_viewport = True
    bpy.context.view_layer.update()
    del dg
    return pts


def view_dir(az, el):
    a, e = math.radians(az), math.radians(el)
    return Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))


def key_light_for(d):
    """U-017 capture light: from the camera's upper left and a little in front."""
    fwd = -d
    right = fwd.cross(Vector((0, 0, 1))).normalized()
    up = right.cross(fwd).normalized()
    return (-right * 0.55 + up * 0.75 - fwd * 0.55).normalized()


def set_outline(width):
    for h in hulls.values():
        h.thickness = width


def set_materials(kind):
    """kind: 'toon' | 'mask' (all white) | 'id' (team white, rest grey)."""
    for ob in meshes:
        slots = ob.data.materials
        if kind == "toon":
            slots[0], slots[1] = mat, outline_mat
        elif kind == "mask":
            slots[0], slots[1] = id_team, id_team
        else:
            slots[0], slots[1] = id_mat, id_rest


def render(path):
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("RENDERED", path, scene.render.resolution_x, scene.render.resolution_y)


def flat_obj(name, bm_fn, hex_, alpha=1.0):
    me = bpy.data.meshes.new(name)
    bm_fn(me)
    ob = bpy.data.objects.new(name, me)
    m = bpy.data.materials.new(name + "_M")
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    e = nt.nodes.new("ShaderNodeEmission")
    e.inputs["Color"].default_value = toon._rgba(hex_)
    o = nt.nodes.new("ShaderNodeOutputMaterial")
    if alpha < 1.0:
        tr = nt.nodes.new("ShaderNodeBsdfTransparent")
        mx = nt.nodes.new("ShaderNodeMixShader")
        mx.inputs[0].default_value = alpha
        nt.links.new(tr.outputs[0], mx.inputs[1])
        nt.links.new(e.outputs[0], mx.inputs[2])
        nt.links.new(mx.outputs[0], o.inputs["Surface"])
        try:
            m.surface_render_method = "BLENDED"
        except AttributeError:
            pass
    else:
        nt.links.new(e.outputs[0], o.inputs["Surface"])
    me.materials.append(m)
    scene.collection.objects.link(ob)
    return ob


def disc_mesh(r, z, n=48):
    import bmesh

    def f(me):
        bm = bmesh.new()
        bmesh.ops.create_circle(bm, cap_ends=True, radius=r, segments=n)
        bmesh.ops.translate(bm, vec=(0, 0, z), verts=bm.verts)
        bm.to_mesh(me)
        bm.free()
    return f


def ref_figure_mesh(x, y):
    """2.0 u reference character silhouette: capsule body + head, flat Void Navy."""
    import bmesh

    def f(me):
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=16, radius1=0.30, radius2=0.26, depth=1.3,
                              matrix=Matrix.Translation((x, y, 0.65)))
        bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=0.35,
                                  matrix=Matrix.Translation((x, y, 1.65)))
        bm.to_mesh(me)
        bm.free()
    return f


os.makedirs(OUT, exist_ok=True)
SUN_EL = math.radians(50)

if MODE == "bible":
    cam_data.type = "ORTHO"
    blob = flat_obj("ContactBlob", disc_mesh(0.55, 0.002), "#18233B", 0.40)   # drawn by the game; the bible draws one too
    set_outline(OUTLINE_BIBLE)
    for name, (clip, frame, rots, az, el, team, fit) in POSES.items():
        if ONLY and name not in ONLY:
            continue
        arm.location.z = 0.0
        pose(clip, frame, rots)
        d = view_dir(az, el)
        fit_obs = [o for o in meshes if o.name == "Warrior_Shield"] if fit == "shield" else meshes
        pts = posed_points(fit_obs)
        if fit == "shield":           # look straight at the shield face
            sh = bpy.data.objects["Warrior_Shield"]
            n = (sh.matrix_world.to_3x3() @ (arm.pose.bones["handslot.l"].matrix.to_3x3() @ Vector((0, 0, 1))))
            d = Vector((0.15, -1.0, 0.1)).normalized()
        center = sum(pts, Vector()) / len(pts)
        cam.location = center + d * 12.0
        cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
        bpy.context.view_layer.update()
        inv = cam.matrix_world.inverted()
        cp = [inv @ p for p in pts]
        xs, ys = [p.x for p in cp], [p.y for p in cp]
        pad = OUTLINE_BIBLE * 2 + 0.02
        w, h = max(xs) - min(xs) + pad, max(ys) - min(ys) + pad
        cx, cy = (max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2
        cam.location = cam.matrix_world @ Vector((cx, cy, 0))
        scene.render.resolution_y = HEIGHT
        scene.render.resolution_x = max(16, int(round(HEIGHT * w / h)))
        cam_data.ortho_scale = max(w, h)
        for o in meshes:
            pp = posed_points([o])
            print("BBOX", name, o.name, " ".join("%.2f..%.2f" % (min(v[i] for v in pp), max(v[i] for v in pp))
                                                 for i in range(3)))
        for bn in ("hand.l", "hand.r", "head"):
            print("JOINT", name, bn, tuple(round(c, 2) for c in (arm.matrix_world @ arm.pose.bones[bn].head)))
        toon.set_light(mat, key_light_for(d))
        toon.set_team(mat, team == "crimson")
        blob.hide_render = fit == "shield"
        set_materials("toon")
        render(os.path.join(OUT, f"{name}.png"))
        blob.hide_render = True
        set_materials("mask")
        render(os.path.join(OUT, f"{name}.mask.png"))
        set_materials("toon")

elif MODE in ("game", "facings"):
    cam_data.type = "PERSP"
    cam_data.sensor_fit = "VERTICAL"
    cam_data.angle_y = math.radians(CAM_FOV)
    cam_data.clip_start, cam_data.clip_end = 1.0, 200.0
    scene.render.resolution_x, scene.render.resolution_y = 1920, 1080
    m_per_px = 2 * (CAM_DIST - 0.8) * math.tan(math.radians(CAM_FOV / 2)) / 1080
    set_outline(OUTLINE_GAME_PX * m_per_px)
    pose("Idle", 1, [])

    def place_camera(az):
        d = view_dir(az, CAM_PITCH)
        cam.location = d * CAM_DIST
        cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
        fwd_h = Vector((-d.x, -d.y, 0)).normalized()
        right_h = fwd_h.cross(Vector((0, 0, 1))).normalized()
        L = (fwd_h - right_h).normalized() * math.cos(SUN_EL)      # sun from screen upper-left, elevation 50
        L.z = math.sin(SUN_EL)
        toon.set_light(mat, L.normalized())
        bpy.context.view_layer.update()

    if MODE == "game":
        ground = flat_obj("Ground", disc_mesh(30.0, 0.0, 64), "#A3C150")
        blob = flat_obj("ContactBlob", disc_mesh(0.55, 0.003), "#18233B", 0.40)
        ref = flat_obj("RefFigure2u", ref_figure_mesh(1.6, 1.6), "#18233B")
        scene.render.film_transparent = False
        set_materials("toon")
        place_camera(0.0)       # the unit stands facing the camera (screen S); the world yaw 45 is the camera's
        for team in ("azure", "crimson"):
            if ONLY and team not in ONLY:
                continue
            toon.set_team(mat, team == "crimson")
            render(os.path.join(OUT, f"game_{team}.png"))
        # unit alone (no ground, no ref) for the pixel-height measurement
        ground.hide_render = blob.hide_render = ref.hide_render = True
        scene.render.film_transparent = True
        set_materials("mask")
        render(os.path.join(OUT, "game_mask.png"))
    else:
        dirs = {"N": 180, "NE": -135, "E": -90, "SE": -45, "S": 0, "SW": 45, "W": 90, "NW": 135}
        scene.render.use_border = True
        scene.render.use_crop_to_border = True
        bw, bh = 300 / 1920, 300 / 1080
        scene.render.border_min_x, scene.render.border_max_x = 0.5 - bw / 2, 0.5 + bw / 2
        scene.render.border_min_y, scene.render.border_max_y = 0.5 - bh * 0.30, 0.5 + bh * 0.70
        toon.set_team(mat, False)
        for k, az in dirs.items():
            place_camera(az)
            set_materials("toon")
            render(os.path.join(OUT, f"facing_{k}.png"))
            set_materials("id")
            render(os.path.join(OUT, f"facing_{k}.id.png"))
        set_materials("toon")
else:
    raise SystemExit(f"unknown mode {MODE}")
