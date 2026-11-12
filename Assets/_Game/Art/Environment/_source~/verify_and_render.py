"""Checks and look-dev renders for the environment kit (run after build_env_kit.py).

    Blender -b --factory-startup --python-exit-code 1 --python verify_and_render.py -- <out_dir> [--no-render] [--only NAME]

1. FBX re-import: every ../env_*.fbx imports headless; triangle count and Unity-axis bounds match manifest.json.
2. Assembles the Skybreak Basin test scene from the F-03 grid (env_art/layout.py, a port of map.ts) with the pieces
   from env_kit.blend, and checks programmatically that the pieces tile on the grid:
     a. every walkable cell is covered: 4 rays per cell (corners inset 0.02 and centre) hit kit geometry within
        0.12 m of the cell's Height01 (bridge cells: the deck);
     b. every cell piece's footprint is exactly its cell (ground variants: bounds == cell rect within 1e-4);
     c. every island-edge cell has a cliff column; all ground pieces sit inside the map bounds [0,112] x [0,72].
   Results go to <out_dir>/checks.json; any failure exits 1.
3. Renders the scene at the in-game camera (orthographic, pitch 38, yaw 45, 20 units tall) and at the bible framings
   into <out_dir>/renders/ (RGBA; compose_sheets.py puts the sky behind and builds the side-by-sides).
"""
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from env_art import layout, palette, toon  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0]) if argv else os.path.join(HERE, "_out")
RENDER = "--no-render" not in argv
ONLY = argv[argv.index("--only") + 1] if "--only" in argv else None
os.makedirs(os.path.join(OUT, "renders"), exist_ok=True)
FBX_DIR = os.path.dirname(HERE)
MANIFEST = json.load(open(os.path.join(HERE, "manifest.json")))
fails = []
report = {}

P = Matrix(((-1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))   # unity -> blender
PI = P.inverted()


def unity_to_blender_matrix(mu):
    return P @ mu @ PI


def yaw_matrix(pos, yaw):
    t = math.radians(yaw)
    x = Vector((math.cos(t), 0, -math.sin(t)))
    z = Vector((math.sin(t), 0, math.cos(t)))
    return basis_matrix(pos, x, Vector((0, 1, 0)), z)


def basis_matrix(pos, x, y, z):
    m = Matrix.Identity(4)
    for r in range(3):
        m[r][0], m[r][1], m[r][2], m[r][3] = x[r], y[r], z[r], pos[r]
    return m


def tri_count(me):
    return sum(len(p.vertices) - 2 for p in me.polygons)


# ------------------------------------------------------------------------------------------------ 1. FBX re-import
def check_fbx():
    rows = []
    for r in MANIFEST["pieces"]:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        path = os.path.join(FBX_DIR, r["piece"] + ".fbx")
        res = bpy.ops.import_scene.fbx(filepath=path)
        obs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
        ok = "FINISHED" in res and len(obs) == 1
        t = tri_count(obs[0].data) if obs else -1
        # the importer applies the inverse axis conversion, so Blender-space bounds come back; convert to Unity
        if obs:
            mw = obs[0].matrix_world
            vs = [mw @ v.co for v in obs[0].data.vertices]
            lo = [min(-v.x for v in vs), min(v.z for v in vs), min(-v.y for v in vs)]
            hi = [max(-v.x for v in vs), max(v.z for v in vs), max(-v.y for v in vs)]
            dev = max(abs(a - b) for a, b in zip(lo + hi, r["bounds_unity_min"] + r["bounds_unity_max"]))
            cols = len(obs[0].data.color_attributes)
        else:
            dev, cols = 99, 0
        ok = ok and t == r["tris"] and dev < 2e-3 and cols >= 1
        rows.append(dict(piece=r["piece"], ok=ok, tris=t, manifest_tris=r["tris"], bounds_dev=round(dev, 5),
                         color_sets=cols))
        if not ok:
            fails.append(f"fbx {r['piece']}: tris {t} vs {r['tris']}, bounds dev {dev:.4f}, colour sets {cols}")
    report["fbx_reimport"] = dict(count=len(rows), passed=sum(x["ok"] for x in rows), rows=rows)
    print(f"FBX_REIMPORT {sum(x['ok'] for x in rows)}/{len(rows)}")


# --------------------------------------------------------------------------------------------------- 2. assemble
GROUPS = {
    "ground": ("env_ground", "env_wall", "env_bridge_cell"),
    "cliff": ("env_cliff",),
    "bridge": ("env_bridge_span", "env_bridge_post", "env_bridge_pier"),
    "objective": ("env_hill", "env_spawn", "env_shrine", "env_banner", "env_crystal"),
    "deco": ("env_tree", "env_bush", "env_rocks", "env_ruin", "env_waterfall"),
    "sky": ("env_sky", "env_cloud"),
}


def group_of(name):
    for g, pre in GROUPS.items():
        if name.startswith(pre):
            return g
    return "deco"


def assemble():
    bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE, "env_kit.blend"))
    meshes = {o.name: o.data for o in bpy.data.objects if o.type == "MESH"}
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o)
    scn = bpy.context.scene
    m = layout.build_map()
    placements = layout.place(m)
    cols = {g: bpy.data.collections.new("Scene_" + g) for g in GROUPS}
    for c in cols.values():
        scn.collection.children.link(c)
    team_obs = []
    cell_pieces = {}
    span_rects = []
    for k, (name, sx, h, sz, yaw, extra) in enumerate(placements):
        me = meshes[name]
        ob = bpy.data.objects.new(f"{name}.{k}", me)
        pos = Vector((sx, h, -sz))
        if name == "env_bridge_span":
            end = Vector((extra["sx1"], extra["h1"], -extra["sz1"]))
            d = end - pos
            x = Vector((d.z, 0, -d.x)).normalized()
            y = d.normalized().cross(x).normalized()
            mu = basis_matrix(pos, x, y, d)
            span_rects.append((sx, sz, extra["sx1"], extra["sz1"], h, extra["h1"]))
        else:
            mu = yaw_matrix(pos, yaw)
        ob.matrix_world = unity_to_blender_matrix(mu)
        cols[group_of(name)].objects.link(ob)
        if extra in ("crimson",):
            ob["crimson"] = True
        if name.startswith(("env_ground", "env_wall", "env_bridge_cell")):
            cell_pieces[(int(math.floor(sx)), int(math.floor(sz)))] = (name, ob)
    report["placements"] = len(placements)
    return m, placements, cols, cell_pieces, span_rects


def check_tiling(m, placements, cell_pieces, span_rects):
    cells, hgt = m["cells"], m["hgt"]
    W, H = layout.W, layout.H
    dg = bpy.context.evaluated_depsgraph_get()
    scn = bpy.context.scene
    holes, height_bad, missing, foot_bad, cliff_missing, out_of_bounds = [], [], [], [], [], []
    cliffs = {(int(math.floor(p[1])), int(math.floor(p[3]))) for p in placements if p[0].startswith("env_cliff")}
    for cz in range(H):
        for cx in range(W):
            t = cells[cz * W + cx]
            if t == layout.VOID:
                continue
            hh = hgt[cz * W + cx]
            if t != layout.BRIDGE:
                if (cx, cz) not in cell_pieces:
                    missing.append((cx, cz))
                    continue
                name, ob = cell_pieces[(cx, cz)]
                bb = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
                xs = [-v.x for v in bb]
                zs = [v.y for v in bb]           # blender y = sim z
                if name.startswith("env_ground"):
                    if max(abs(min(xs) - cx), abs(max(xs) - cx - 1), abs(min(zs) - cz), abs(max(zs) - cz - 1)) > 1e-4:
                        foot_bad.append((cx, cz, name))
                else:
                    if min(xs) > cx + 1e-4 or max(xs) < cx + 1 - 1e-4 or min(xs) < cx - 0.09 or max(xs) > cx + 1.09:
                        foot_bad.append((cx, cz, name))
                if not (0 <= min(xs) and max(xs) <= W + 0.1 and 0 <= min(zs) and max(zs) <= H + 0.1):
                    out_of_bounds.append((cx, cz))
                if layout.is_edge(m, cx, cz) and (cx, cz) not in cliffs:
                    cliff_missing.append((cx, cz))
            if not layout.walkable(t):
                continue
            for (ox, oz) in ((0.02, 0.02), (0.98, 0.02), (0.02, 0.98), (0.98, 0.98), (0.5, 0.5)):
                sx, sz = cx + ox, cz + oz
                origin = Vector((-sx, sz, hh + 5.0))
                for _ in range(12):   # see through anything standing above the walking surface (rails, trees)
                    hit, loc, nrm, idx, ob, mat = scn.ray_cast(dg, origin, Vector((0, 0, -1)), distance=7.0)
                    if hit and loc.z > hh + 0.4:
                        origin = Vector((loc.x, loc.y, loc.z - 1e-3))
                        continue
                    break
                if not hit:
                    holes.append((cx, cz, ox, oz))
                elif abs(loc.z - hh) > (0.2 if t == layout.BRIDGE else 0.12) and not ob.name.startswith(
                        ("env_hill", "env_spawn", "env_shrine", "env_ruin", "env_rocks", "env_bush", "env_banner",
                         "env_crystal", "env_edge_stones")):
                    height_bad.append((cx, cz, ox, oz, round(loc.z, 3), round(hh, 3), ob.name.split(".")[0]))
    res = dict(walkable_cells=sum(1 for t in cells if layout.walkable(t)), holes=len(holes), holes_sample=holes[:20],
               height_mismatch=len(height_bad), height_sample=height_bad[:20], cells_without_piece=len(missing),
               footprint_not_cell=len(foot_bad), footprint_sample=foot_bad[:10], edge_cells_without_cliff=len(cliff_missing),
               ground_out_of_map=len(out_of_bounds), bridge_spans=len(span_rects))
    report["tiling"] = res
    for k in ("holes", "height_mismatch", "cells_without_piece", "footprint_not_cell", "edge_cells_without_cliff",
              "ground_out_of_map"):
        if res[k]:
            fails.append(f"tiling {k} = {res[k]}")
    print("TILING", json.dumps({k: v for k, v in res.items() if not k.endswith("sample")}))


# ----------------------------------------------------------------------------------------------------- 3. render
def join_groups(cols):
    """Join each group into one mesh per (outline width, team), so each keeps the manifest's outline width and the
    crimson-team pieces can take the crimson swap."""
    from env_art.pieces import outline_width
    out = []
    for g, c in cols.items():
        buckets = {}
        for o in list(c.objects):           # bucket BEFORE joining (joined objects get new names)
            buckets.setdefault((outline_width(o.name), bool(o.get("crimson"))), []).append(o)
        for (w, crimson), obs in sorted(buckets.items()):
            # make single-user copies so join does not touch the kit meshes
            for o in obs:
                o.data = o.data.copy()
            with bpy.context.temp_override(active_object=obs[0], selected_editable_objects=obs, object=obs[0]):
                bpy.ops.object.join()
            obs[0].name = f"{g}{'_crimson' if crimson else ''}_w{w}"
            obs[0]["outline"] = w
            out.append(obs[0])
    return out


def setup_render(objs):
    scn = bpy.context.scene
    scn.render.engine = "BLENDER_EEVEE"
    scn.render.film_transparent = True
    scn.view_settings.view_transform = "Standard"
    scn.view_settings.look = "None"
    scn.eevee.taa_render_samples = 16
    mat_a = toon.make_toon_material("CM_Toon_Env", crimson=False)
    mat_c = toon.make_toon_material("CM_Toon_Env_Crimson", crimson=True)
    for ob in objs:
        width = ob.get("outline", 0.06)
        toon.apply(ob, mat_c if "crimson" in ob.name else mat_a, outline=width)
    sun_d = bpy.data.lights.new("Sun", "SUN")
    sun_d.energy = 1.0
    sun_d.angle = math.radians(3)
    sun = bpy.data.objects.new("Sun", sun_d)
    scn.collection.objects.link(sun)
    to_light_u = Vector((-0.35, 0.75, -0.55)).normalized()
    to_light_b = Vector((-to_light_u.x, -to_light_u.z, to_light_u.y))
    sun.rotation_euler = (-to_light_b).to_track_quat("-Z", "Y").to_euler()
    world = bpy.data.worlds.new("W")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.0
    scn.world = world
    cam_d = bpy.data.cameras.new("Cam")
    cam = bpy.data.objects.new("Cam", cam_d)
    scn.collection.objects.link(cam)
    scn.camera = cam
    return cam


def frame(cam, name, target_sim, pitch, yaw, view_h, res=(1280, 720), ortho=True, dist=60.0):
    if not ortho:
        dist = 34.0
    scn = bpy.context.scene
    scn.render.resolution_x, scn.render.resolution_y = res
    tx, ty, tz = target_sim
    target_u = Vector((tx, ty, -tz))
    p, y = math.radians(pitch), math.radians(yaw)
    fwd_u = Vector((math.cos(p) * math.sin(y), -math.sin(p), math.cos(p) * math.cos(y)))
    pos_u = target_u - fwd_u * dist
    fwd_b = Vector((-fwd_u.x, -fwd_u.z, fwd_u.y))
    cam.location = Vector((-pos_u.x, -pos_u.z, pos_u.y))
    cam.rotation_euler = fwd_b.to_track_quat("-Z", "Y").to_euler()
    cam.data.type = "ORTHO" if ortho else "PERSP"
    cam.data.clip_start, cam.data.clip_end = 0.5, 400
    if ortho:
        cam.data.ortho_scale = view_h * max(res) / res[1] if res[0] >= res[1] else view_h
    else:
        cam.data.lens = 35
    scn.render.filepath = os.path.join(OUT, "renders", name + ".png")
    bpy.ops.render.render(write_still=True)
    print("RENDERED", scn.render.filepath)


# The camera's yaw in this formula: Unity Euler Y. The game camera is Euler(38, 315, 0) (SimSpace.CameraRotation).
FRAMINGS = [
    # name, target (sim x, height, sim z), pitch, yaw, view height, resolution, ortho
    ("ingame_azure_base", (14, 0.0, 36), 38, 315, 20, (1280, 720), True),
    ("ingame_hill", (54, 0.75, 38), 38, 315, 20, (1280, 720), True),
    ("ingame_shrine", (56, 1.5, 12), 38, 315, 20, (1280, 720), True),
    ("p15_wide_ingame_angle", (36, 0.4, 36), 38, 315, 34, (1280, 720), True),
    ("p12_overview_north_up", (56, 0.0, 37), 58, 0, 88, (1200, 1000), True),
    ("p03_hero_hill_shrine", (56, 1.5, 28), 30, 0, 0, (1280, 720), False),
]


def render_lineup():
    """Every kit piece in the .blend lineup, toon-shaded, at the in-game camera angle."""
    bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE, "env_kit.blend"))
    obs = [o for o in bpy.data.objects if o.type == "MESH" and not o.name.startswith(("env_cloud_floor", "env_sky"))]
    for o in bpy.data.objects:
        if o not in obs:
            bpy.data.objects.remove(o)
    cam = setup_render(obs)
    xs = [-o.location.x for o in obs]
    zs = [-o.location.y for o in obs]
    cx, cz = (min(xs) + max(xs)) / 2, (min(zs) + max(zs)) / 2
    frame(cam, "kit_lineup", (cx, 0.5, -cz), 38, 315, 46, (1920, 1200), True)


def main():
    if not ONLY:
        check_fbx()
    m, placements, cols, cell_pieces, span_rects = assemble()
    if not ONLY:
        check_tiling(m, placements, cell_pieces, span_rects)
    with open(os.path.join(OUT, "checks.json"), "w") as f:
        json.dump(dict(fails=fails, **report), f, indent=1)
    if RENDER:
        objs = join_groups(cols)
        cam = setup_render(objs)
        for fr in FRAMINGS:
            if ONLY and fr[0] != ONLY:
                continue
            frame(cam, fr[0], fr[1], fr[2], fr[3], fr[4], fr[5], fr[6])
    if RENDER and (not ONLY or ONLY == "kit_lineup"):
        render_lineup()
    print("CHECK_FAILS", len(fails))
    for f in fails:
        print("  FAIL", f)
    if fails:
        sys.exit(1)


main()
