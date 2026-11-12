"""Look-dev renders for the props set (render only; nothing here is exported).

  Blender -b --factory-startup --python-exit-code 1 --python render_props.py -- <out_dir> [--only <shot>]

Shots: lineup (gameplay camera, every prop + Warrior silhouette), district, field (gameplay camera, density),
and one orthographic bible framing per p14 crop plus the p03 strip (transparent film, fitted to the bible crop's
foreground box so the silhouettes can be compared pixel for pixel). Writes <out_dir>/renders/*.png and
<out_dir>/renders/*.json (projected positions, counts) for compose_sheets.py.
"""
import json
import math
import os
import subprocess
import sys

import bpy
import numpy as np
from bpy_extras.object_utils import world_to_camera_view
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from props_art import core  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, "..", "..", "..", "..", ".."))
ENV_DIR = os.path.join(REPO, "Assets", "_Game", "Art", "Environment")
BIBLE = os.path.expanduser("~/critical-mass/loop/parity/ref")
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0] if argv else os.path.join(HERE, "_out"))
ONLY = argv[argv.index("--only") + 1] if "--only" in argv else None
RD = os.path.join(OUT, "renders")
os.makedirs(RD, exist_ok=True)

PITCH, FOV, DIST, YAW_FACE = 55.0, 35.0, 23.4, 135.0      # YAW_FACE: prop front (-Y) turned toward the camera
L_GAME = Vector((0.0, -math.cos(math.radians(50)), math.sin(math.radians(50))))   # sun from screen upper-left
L_BIBLE = Vector((-0.5, -0.6, 0.75)).normalized()
GROUND = "#8E8E8E"


# ---------------------------------------------------------------- scene plumbing
def reset():
    bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE, "props.blend"))
    sc = bpy.context.scene
    for eng in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
        try:
            sc.render.engine = eng
            break
        except TypeError:
            continue
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    sc.eevee.taa_render_samples = 16
    w = bpy.data.worlds.new("Black")
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0, 0, 0, 1)
    w.node_tree.nodes["Background"].inputs[1].default_value = 0.0
    sc.world = w
    src = bpy.data.collections["Props"]
    src.hide_render = True
    src.hide_viewport = True
    shot = bpy.data.collections.new("Shot")
    sc.collection.children.link(shot)
    mats = dict(azure=core.make_toon("CM_Toon"), crimson=core.make_toon("CM_Toon_Crimson", crimson=True),
                ground=core.make_toon("CM_Toon_Ground", flat=True))
    return sc, shot, mats


def sun(sc, L):
    ld = bpy.data.lights.new("Sun", "SUN")
    ld.energy = 1.0
    ld.angle = 0.0
    try:
        ld.use_shadow = True
        ld.shadow_jitter = False
    except AttributeError:
        pass
    ob = bpy.data.objects.new("Sun", ld)
    sc.collection.objects.link(ob)
    ob.rotation_euler = (-L).to_track_quat("-Z", "Y").to_euler()
    core.set_ldir(tuple(L))
    return ob


def gameplay_cam(sc, target=(0, 0, 0), zoom=1.0):
    d = DIST * zoom
    t = Vector(target)
    loc = t + Vector((1, 1, 0)).normalized() * d * math.cos(math.radians(PITCH)) \
        + Vector((0, 0, d * math.sin(math.radians(PITCH))))
    cd = bpy.data.cameras.new("Cam")
    cd.type = "PERSP"
    cd.sensor_fit = "VERTICAL"
    cd.angle_y = math.radians(FOV)
    cd.clip_end = 200
    cam = bpy.data.objects.new("Cam", cd)
    sc.collection.objects.link(cam)
    cam.location = loc
    cam.rotation_euler = (t - loc).to_track_quat("-Z", "Y").to_euler()
    sc.camera = cam
    sc.render.resolution_x, sc.render.resolution_y = 1920, 1080
    sc.render.film_transparent = False
    return cam


def wpp(cam, sc, at):
    """World units per pixel at point `at`."""
    if cam.data.type == "ORTHO":
        return cam.data.ortho_scale / max(sc.render.resolution_x, sc.render.resolution_y)
    fwd = cam.matrix_world.to_quaternion() @ Vector((0, 0, -1))
    d = (Vector(at) - cam.location).dot(fwd)
    return 2 * d * math.tan(cam.data.angle_y / 2) / sc.render.resolution_y


def place(shot, mats, name, loc, rotz=YAW_FACE, crimson=False):
    src = bpy.data.objects[name]
    ob = src.copy()
    if crimson:
        ob.data = src.data.copy()
    ob.location = loc
    ob.rotation_euler = (0, 0, math.radians(rotz))
    shot.objects.link(ob)
    core.apply_toon(ob, mats["crimson" if crimson else "azure"])
    return ob


def outline_all(sc, cam, shot, px=2.5):
    for ob in shot.objects:
        if ob.type != "MESH" or ob.get("no_outline"):
            continue
        p = ob.get("outline_px", px)
        if p <= 0:
            continue
        mat = ob.data.materials[0] if ob.data.materials else None
        core.apply_toon(ob, mat, p * wpp(cam, sc, ob.matrix_world.translation))
        if "lined" not in ob.vertex_groups:
            ob.modifiers["Outline"].vertex_group = ""


def ground_plane(shot, mats, size=80.0, hex_=GROUND, z=0.0):
    import bmesh
    bm = bmesh.new()
    c = bm.loops.layers.float_color.new("Col")
    s = size / 2
    f = bm.faces.new([bm.verts.new(v) for v in ((-s, -s, z), (s, -s, z), (s, s, z), (-s, s, z))])
    for loop in f.loops:
        loop[c] = core.lin(hex_) + (0.0,)
    me = bpy.data.meshes.new("Ground")
    bm.to_mesh(me)
    ob = bpy.data.objects.new("Ground", me)
    ob["no_outline"] = 1
    shot.objects.link(ob)
    core.apply_toon(ob, mats["ground"])
    return ob


def pool(shot, loc, r, hex_, alpha):
    bpy.ops.mesh.primitive_circle_add(vertices=24, radius=r, fill_type="TRIFAN", location=(loc[0], loc[1], 0.006))
    ob = bpy.context.active_object
    for c in ob.users_collection:
        c.objects.unlink(ob)
    shot.objects.link(ob)
    ob["no_outline"] = 1
    ob.data.materials.append(core.make_flat(f"Pool_{hex_}_{alpha}", hex_, alpha))
    return ob


_warrior_meshes = None


def _span(meshes):
    dg = bpy.context.evaluated_depsgraph_get()
    xs, ys, zs = [], [], []
    for o in meshes:
        e = o.evaluated_get(dg)
        me = e.to_mesh()
        for v in list(me.vertices)[::7]:
            w = e.matrix_world @ v.co
            xs.append(w.x)
            ys.append(w.y)
            zs.append(w.z)
        e.to_mesh_clear()
    return max(max(xs) - min(xs), max(ys) - min(ys)), max(zs)


def _rest_z(meshes):
    dg = bpy.context.evaluated_depsgraph_get()
    zs = []
    for o in meshes:
        e = o.evaluated_get(dg)
        me = e.to_mesh()
        zs += [(e.matrix_world @ v.co).z for v in me.vertices]
        e.to_mesh_clear()
    return min(zs), max(zs)


def _lower_arms(arms, meshes):
    """The FBX is in rest (T) pose; drop the upper arms so the silhouette reads as a standing character."""
    for arm in arms:
        arm.data.pose_position = "POSE"
        z_rest = _span(meshes)[1]
        for side in ("l", "r"):
            pbs = [pb for pb in arm.pose.bones if "upperarm" in pb.name.lower() and pb.name.lower().rstrip(".").endswith(side)]
            for pb in pbs:
                pb.rotation_mode = "XYZ"
                best = (None, 1e9)
                for ax in range(3):
                    for sgn in (1, -1):
                        pb.rotation_euler = (0, 0, 0)
                        pb.rotation_euler[ax] = sgn * math.radians(70)
                        bpy.context.view_layer.update()
                        sp, zt = _span(meshes)
                        if zt <= z_rest + 0.02 and sp < best[1]:
                            best = ((ax, sgn), sp)
                pb.rotation_euler = (0, 0, 0)
                if best[0]:
                    pb.rotation_euler[best[0][0]] = best[0][1] * math.radians(70)
                bpy.context.view_layer.update()
                print("ARM", pb.name, best)


def warrior(shot, loc, rotz=YAW_FACE):
    """Void Navy silhouette of the Warrior from branch art/warrior-blender (LFS FBX), 2.0 u tall."""
    global _warrior_meshes
    path = os.path.join(HERE, "_out", "Warrior.fbx")
    if not os.path.exists(path):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        try:
            ptr = subprocess.run(["git", "-C", REPO, "show",
                                  "origin/art/warrior-blender:Assets/_Game/Art/Characters/Warrior.fbx"],
                                 capture_output=True, check=True).stdout
            data = subprocess.run(["git", "-C", REPO, "lfs", "smudge"], input=ptr, capture_output=True,
                                  check=True).stdout
            open(path, "wb").write(data)
        except Exception as e:  # noqa: BLE001
            print("WARRIOR fetch failed", e)
    before = set(bpy.data.objects)
    navy = core.make_flat("Silhouette", core.NAVY)
    if os.path.exists(path) and os.path.getsize(path) > 10000:
        bpy.ops.import_scene.fbx(filepath=path)
        new = [o for o in bpy.data.objects if o not in before]
    else:
        bpy.ops.mesh.primitive_cylinder_add(radius=0.35, depth=1.5, location=(0, 0, 0.75))
        a = bpy.context.active_object
        bpy.ops.mesh.primitive_uv_sphere_add(radius=0.3, location=(0, 0, 1.7))
        new = [a, bpy.context.active_object]
    root = bpy.data.objects.new("WarriorRoot", None)
    shot.objects.link(root)
    for o in new:
        for c in o.users_collection:
            c.objects.unlink(o)
        shot.objects.link(o)
        if o.parent is None:
            o.parent = root
        if o.type == "MESH":
            o["no_outline"] = 1
            o.data.materials.clear()
            o.data.materials.append(navy)
    bpy.context.view_layer.update()
    z0, z1 = _rest_z([o for o in new if o.type == "MESH"])       # feet to top of head in the rest pose
    h = z1 - z0
    _lower_arms([o for o in new if o.type == "ARMATURE"], [o for o in new if o.type == "MESH"])
    root.scale = (2.0 / h,) * 3 if abs(h - 2.0) > 0.05 else (1, 1, 1)
    root.location = loc
    root.rotation_euler = (0, 0, math.radians(rotz))
    bpy.context.view_layer.update()
    z0, z1 = _rest_z([o for o in new if o.type == "MESH"])
    return root, h, z1 - loc[2]


def proj(sc, cam, p):
    v = world_to_camera_view(sc, cam, Vector(p))
    return v.x * sc.render.resolution_x, (1 - v.y) * sc.render.resolution_y, v.z


def obj_screen(sc, cam, ob):
    pts = [proj(sc, cam, ob.matrix_world @ Vector(c)) for c in ob.bound_box]
    xs, ys = [p[0] for p in pts], [p[1] for p in pts]
    base = proj(sc, cam, ob.matrix_world.translation)
    top = proj(sc, cam, ob.matrix_world.translation + Vector((0, 0, ob.dimensions.z)))
    return dict(x0=min(xs), x1=max(xs), y0=min(ys), y1=max(ys), bx=base[0], by=base[1], height_px=base[1] - top[1])


def render(sc, name):
    sc.render.filepath = os.path.join(RD, name + ".png")
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA"
    bpy.ops.render.render(write_still=True)
    print("RENDERED", name)


def screen_grid(sx, sy):
    """World point for screen-aligned offsets (sx right, sy up the screen) around the origin."""
    r = Vector((-1, 1, 0)).normalized()
    f = Vector((-1, -1, 0)).normalized()
    return tuple(r * sx + f * sy)


# ---------------------------------------------------------------- shots
LINEUP_EXTRA = [("prop_banner", True), ("prop_marker_team", True)]


def shot_lineup():
    sc, shot, mats = reset()
    sun(sc, L_GAME)
    cam = gameplay_cam(sc)
    ground_plane(shot, mats)
    names = [o.name for o in sorted(bpy.data.collections["Props"].objects, key=lambda o: o["order"])]
    items = [(n, False) for n in names] + LINEUP_EXTRA
    cols, dx, dy = 7, 3.0, 3.1
    rows_y = [6.4, 3.3, 0.2, -2.9, -5.6]
    slots = [(c, r) for r in range(5) for c in range(cols)]
    centre = (3, 2)
    slots.remove(centre)
    info = []
    for (n, crim), (c, r) in zip(items, slots):
        loc = screen_grid((c - 3) * dx, rows_y[r])
        ob = place(shot, mats, n, loc, crimson=crim)
        info.append((n + (" (crimson)" if crim else ""), ob))
    outline_all(sc, cam, shot)
    root, h_raw, h = warrior(shot, screen_grid(0, rows_y[2]))
    bpy.context.view_layer.update()
    data = dict(items=[dict(name=n, **obj_screen(sc, cam, ob)) for n, ob in info])
    top = proj(sc, cam, root.matrix_world.translation + Vector((0, 0, h)))
    base = proj(sc, cam, root.matrix_world.translation)
    data["warrior"] = dict(name="Warrior (2.0 u)", bx=base[0], by=base[1], height_px=base[1] - top[1],
                           height_u=h, raw_height_u=h_raw)
    centre_top = proj(sc, cam, (0, 0, 2.0))
    data["centre_character_px"] = 540 - centre_top[1]
    data["outline_m_crate"] = 2.5 * wpp(cam, sc, [o for n, o in info if n == "prop_crate"][0].location)
    json.dump(data, open(os.path.join(RD, "lineup.json"), "w"), indent=1)
    render(sc, "lineup")


def _env(name):
    """Import one environment-kit FBX once; return the object (hidden source)."""
    key = "ENV_" + name
    if key in bpy.data.objects:
        return bpy.data.objects[key]
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(ENV_DIR, name + ".fbx"))
    new = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    ob = new[0]
    ob.name = key
    for c in ob.users_collection:
        c.objects.unlink(ob)
    if ob.data.color_attributes:
        ob.data.color_attributes[0].name = "Col"
    ob.hide_render = True
    return ob


def env_place(shot, mats, name, loc, rotz=0.0, outline_px=0.0):
    src = _env(name)
    ob = src.copy()
    ob.hide_render = False
    ob.location = loc
    ob.rotation_euler = (src.rotation_euler.x, src.rotation_euler.y, math.radians(rotz))
    shot.objects.link(ob)
    core.apply_toon(ob, mats["azure"])
    if outline_px <= 0:
        ob["no_outline"] = 1
    else:
        ob["outline_px"] = outline_px
    return ob


def in_frame(sc, cam, ob):
    x, y, z = proj(sc, cam, ob.matrix_world.translation)
    return 0 <= x <= sc.render.resolution_x and 0 <= y <= sc.render.resolution_y and z > 0


def shot_district():
    import random
    rng = random.Random(7)
    sc, shot, mats = reset()
    sun(sc, L_GAME)
    cam = gameplay_cam(sc, (0, 0, 0))
    paving = ["env_ground_path_a", "env_ground_path_b", "env_ground_path_c"]
    W = 9    # walls on x = -W and y = -W (the screen-top corner), pad at the origin
    for x in range(-W, 17):
        for y in range(-W, 17):
            if x == -W or y == -W:
                env_place(shot, mats, "env_wall", (x + 0.5, y + 0.5, 0), outline_px=2.0)
            else:
                env_place(shot, mats, paving[(x * 7 + y * 13) % 3] if (x * x + y * y) < 90 else "env_ground_dirt",
                          (x + 0.5, y + 0.5, 0))
    env_place(shot, mats, "env_spawn_pad", (0, 0, 0), outline_px=2.0)
    small, lights = [], []
    # braziers every 3 u along both walls + gate pair; crystal lamps at the pad
    for t in range(-6, 13, 3):
        lights.append(place(shot, mats, "prop_brazier", (-W + 1.45, t + 0.5, 0)))
        lights.append(place(shot, mats, "prop_brazier", (t + 0.5, -W + 1.45, 0)))
    lamps = [place(shot, mats, "prop_crystal_lamp", (4.3 * math.cos(a), 4.3 * math.sin(a), 0))
             for a in (math.radians(d) for d in (45, 135, 225, 315))]
    for b in lights:
        pool(shot, b.location, 2.0, core.FLAME, 0.35)
    for lp in lamps:
        pool(shot, lp.location, 1.5, core.TEAM_GLOW, 0.25)
    place(shot, mats, "prop_banner", (-3.2, -4.6, 0))
    place(shot, mats, "prop_banner", (-4.6, -3.2, 0))
    edge = ["prop_crate", "prop_barrel", "prop_crate", "prop_barrel", "prop_stone_stack", "prop_log", "prop_stump",
            "prop_stone_block", "prop_crate"]
    # clusters against the walls (non-walkable edge strip, 0.6 to 1.1 u from the wall face)
    for k in range(26):
        along = -7.5 + k * 0.92 + rng.uniform(-0.2, 0.2)
        side = k % 2
        off = -W + 1.0 + 0.15 + rng.uniform(0, 0.45)
        loc = (off, along, 0) if side == 0 else (along, off, 0)
        if any((Vector(loc) - b.location).length < 0.7 for b in lights):
            continue
        small.append(place(shot, mats, edge[k % len(edge)], loc, rotz=rng.uniform(0, 360)))
    walk = ["prop_pebbles", "prop_planks", "prop_rope_coil", "prop_pebbles", "prop_biomass_sprout"]
    tries = 0
    while len([o for o in small if in_frame(sc, cam, o)]) < 52 and tries < 500:
        tries += 1
        p = screen_grid(rng.uniform(-12, 12), rng.uniform(-7, 10))
        if math.hypot(p[0], p[1]) < 4.9 or p[0] < -W + 1 or p[1] < -W + 1:
            continue
        o = place(shot, mats, walk[tries % len(walk)], (p[0], p[1], 0), rotz=rng.uniform(0, 360))
        if not in_frame(sc, cam, o):
            bpy.data.objects.remove(o)
            continue
        small.append(o)
    outline_all(sc, cam, shot)
    bpy.context.view_layer.update()
    vis_small = [o for o in small if in_frame(sc, cam, o)]
    data = dict(small_props=len(vis_small), warm_light_pools=len([b for b in lights if in_frame(sc, cam, b)]),
                crystal_lamps=len([lp for lp in lamps if in_frame(sc, cam, lp)]),
                tall_on_walkable=sum(1 for o in vis_small if o.dimensions.z >= 0.3 and
                                     min(o.location.x, o.location.y) > -W + 2.2),
                kinds={})
    for o in vis_small:
        k = o.name.split(".")[0]
        data["kinds"][k] = data["kinds"].get(k, 0) + 1
    json.dump(data, open(os.path.join(RD, "district.json"), "w"), indent=1)
    render(sc, "district")


def shot_field():
    import random
    rng = random.Random(11)
    sc, shot, mats = reset()
    sun(sc, L_GAME)
    cam = gameplay_cam(sc, (0, 0, 0))
    grass = ["env_ground_grass_a", "env_ground_grass_b", "env_ground_grass_c"]
    path_cells = set()
    for t in range(-20, 21):                        # a path running up the screen
        for w in (-1, 0, 1):
            path_cells.add((t + w, t))
    for x in range(-16, 17):
        for y in range(-16, 17):
            nm = ("env_ground_path_" + "abc"[(x + y) % 3]) if (x, y) in path_cells else grass[(x * 5 + y * 11) % 3]
            env_place(shot, mats, nm, (x + 0.5, y + 0.5, 0))
    big = []
    for (sx, sy, nm) in ((-10, 7, "env_tree_a"), (9, 8, "env_tree_b"), (-11, -4, "env_tree_b"), (11, -3, "env_tree_a"),
                         (-6, 10, "env_rocks"), (6, -6, "env_rocks"), (12, 3, "env_tree_a")):
        p = screen_grid(sx, sy)
        big.append(env_place(shot, mats, nm, (p[0], p[1], 0), rotz=rng.uniform(0, 360), outline_px=2.5))
    small = []
    kinds = ["prop_biomass_plant_l", "prop_biomass_plant_m", "prop_biomass_plant_s", "prop_biomass_sprout",
             "prop_pebbles", "prop_stump", "prop_log", "prop_pebbles", "prop_corruption_growth_s", "prop_planks",
             "prop_biomass_sprout", "prop_stone_block", "prop_crate", "prop_barrel"]
    tries = 0
    while len(small) < 26 and tries < 800:
        tries += 1
        # clusters along both path sides (2.2 to 3.4 u off the path centre line) and near the trees
        t = rng.uniform(-9, 11)
        side = rng.choice((-1, 1))
        off = rng.uniform(2.2, 3.4) * side
        p = Vector((t, t, 0)) + Vector((1, -1, 0)).normalized() * off
        if rng.random() < 0.3:
            b = rng.choice(big).location
            p = Vector((b.x + rng.uniform(-1.6, 1.6), b.y + rng.uniform(-1.6, 1.6), 0))
        nm = kinds[len(small) % len(kinds)]
        o = place(shot, mats, nm, (p.x, p.y, 0), rotz=rng.uniform(0, 360))
        if not in_frame(sc, cam, o) or any((o.location - s.location).length < 0.8 for s in small):
            bpy.data.objects.remove(o)
            continue
        small.append(o)
    b = place(shot, mats, "prop_brazier", tuple(Vector((1.0, 1.0, 0)) + Vector((1, -1, 0)).normalized() * 2.3))
    pool(shot, b.location, 2.0, core.FLAME, 0.35)
    outline_all(sc, cam, shot)
    bpy.context.view_layer.update()
    data = dict(small_props=len([o for o in small if in_frame(sc, cam, o)]),
                trees_big_rocks=len([o for o in big if in_frame(sc, cam, o)]), lights=1, kinds={})
    for o in small:
        k = o.name.split(".")[0]
        data["kinds"][k] = data["kinds"].get(k, 0) + 1
    json.dump(data, open(os.path.join(RD, "field.json"), "w"), indent=1)
    render(sc, "field")


# ---------------------------------------------------------------- orthographic bible framings
FRAMINGS = {
    "p14-biomass-plants": ("14-biomass-plants", [("prop_biomass_plant_s", (-1.0, 0)), ("prop_biomass_plant_m", (-0.15, 0)),
                                                 ("prop_biomass_plant_l", (1.05, 0))]),
    "p14-loose-orb": ("14-loose-orb", [("prop_orb_biomass", (0, 0))]),
    "p14-carried-bundle": ("14-carried-bundle", [("prop_carried_bundle", (0, 0))]),
    "p14-corruption-growth": ("14-corruption-growth", [("prop_corruption_growth_l", (0, 0)),
                                                       ("prop_corruption_growth_s", (0.62, 0))]),
    "p14-barricade-folded": ("14-barricade-folded", [("prop_barricade_folded", (0, 0))]),
    "p14-barricade-deployed": ("14-barricade-deployed", [("prop_barricade_deployed", (0, 0))]),
    "p14-barricade-damaged": ("14-barricade-damaged", [("prop_barricade_damaged", (0, 0))]),
    "p14-banner-azure": ("14-banner-azure", [("prop_banner", (0, 0))]),
    "p14-banner-crimson": ("14-banner-crimson", [("prop_banner", (0, 0), True)]),
    "p14-markers": ("14-markers", [("prop_marker_team", (-0.8, 0)), ("prop_marker_neutral", (0, 0)),
                                   ("prop_marker_team", (0.8, 0), True)]),
    "p03-props-strip": ("03-props-strip", [
        ("prop_biomass_plant_l", (-2.9, 0)), ("prop_biomass_sprout", (-2.05, 0.55)), ("prop_biomass_sprout", (-2.05, 0)),
        ("prop_crystal_cluster_l", (-1.45, 0.45)), ("prop_crystal_cluster_s", (-1.5, 0)),
        ("prop_biomass_sprout", (-1.0, 0)), ("prop_corruption_growth_l", (-0.35, 0)),
        ("prop_orb_stone", (0.45, 0.62)), ("prop_orb_lava", (1.0, 0.62)), ("prop_orb_biomass", (1.55, 0.62)),
        ("prop_orb_corruption", (2.1, 0.62)), ("prop_crate", (0.45, 0)), ("prop_stone_block", (1.0, 0)),
        ("prop_orb_brass", (1.55, 0.05)), ("prop_log_section", (2.1, 0))]),
}


def bible_box(crop):
    img = bpy.data.images.load(os.path.join(BIBLE, crop + ".png"))
    w, h = img.size
    a = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)[::-1, :, :3]
    bg = a[2, 2]
    fg = np.abs(a - bg).sum(2) > 60 / 255
    ys, xs = np.nonzero(fg)
    return w, h, xs.min(), xs.max() + 1, ys.min(), ys.max() + 1


def shot_ortho(key, scale=3):
    crop, items = FRAMINGS[key]
    sc, shot, mats = reset()
    sun(sc, L_BIBLE)
    obs = []
    for it in items:
        n, (x, z) = it[0], it[1]
        obs.append(place(shot, mats, n, (x, 0, z), rotz=0.0, crimson=len(it) > 2 and it[2]))
    bpy.context.view_layer.update()
    pitch = math.radians(20)
    d = Vector((0, math.cos(pitch), -math.sin(pitch)))
    right = Vector((1, 0, 0))
    up = right.cross(d)
    pr = [((o.matrix_world @ v.co).dot(right), (o.matrix_world @ v.co).dot(up)) for o in obs for v in o.data.vertices]
    xs, ys = [p[0] for p in pr], [p[1] for p in pr]
    ex, ey = max(xs) - min(xs), max(ys) - min(ys)
    cxw, cyw = (max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2
    cw, ch, bx0, bx1, by0, by1 = bible_box(crop)
    W, H = cw * scale, ch * scale
    s = max(ex / ((bx1 - bx0) * scale), ey / ((by1 - by0) * scale))
    cd = bpy.data.cameras.new("Ortho")
    cd.type = "ORTHO"
    cd.ortho_scale = s * max(W, H)
    cam = bpy.data.objects.new("Ortho", cd)
    sc.collection.objects.link(cam)
    dxp = (bx0 + bx1) / 2 * scale - W / 2
    dyp = (by0 + by1) / 2 * scale - H / 2
    centre = right * (cxw - dxp * s) + up * (cyw + dyp * s)
    cam.location = centre - d * 30
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    cd.clip_end = 100
    sc.camera = cam
    sc.render.resolution_x, sc.render.resolution_y = W, H
    sc.render.film_transparent = True
    for o in obs:
        p = o.get("outline_px", 2.5)
        if p > 0:
            core.apply_toon(o, o.data.materials[0], p * scale * s)
    json.dump(dict(crop=crop, scale=scale, world_per_px=s), open(os.path.join(RD, key + ".json"), "w"))
    render(sc, key)


SHOTS = dict(lineup=shot_lineup, district=shot_district, field=shot_field,
             **{k: (lambda k=k: shot_ortho(k)) for k in FRAMINGS})
for k, fn in SHOTS.items():
    if ONLY and k != ONLY and not (ONLY == "ortho" and k.startswith("p")):
        continue
    fn()
print("RENDER DONE")
