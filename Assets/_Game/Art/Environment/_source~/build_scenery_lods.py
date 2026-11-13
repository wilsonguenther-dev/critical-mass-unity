"""Scenery LODs (2026-11-12): one Blender run makes the lighter levels of the environment and bases kits.

Run (from the repo root, through the machine lock):
  ~/bin/cm-heavy blender perl -e 'alarm 2400; exec @ARGV' /Applications/Blender.app/Contents/MacOS/Blender -b \
      --factory-startup -P Assets/_Game/Art/Environment/_source~/build_scenery_lods.py -- <config.json> <out_dir>

config.json (written by scenery_lod_plan.py): {"pieces": [{"name", "path", "count", "tris", "layout", "breakable"}...]}.

Naming rule: <piece>_lod1.fbx (about 45% of the triangles, silhouette overlap >= 0.95 at the gameplay camera) and
<piece>_lod2.fbx (about 15%, overlap >= 0.80), written beside <piece>.fbx. Object names, material slot names, vertex
colour ("Col"), pivots and bounds are kept: vertices on the six bounding planes are pinned (never collapsed), so kit
pieces still meet their neighbours and the sway weights (taken from the mesh bounds) do not move. Layout files keep only
their cm_district_* children (the only part WorldView draws from a layout). A piece whose largest dimension is under
TINY_M gets no far level: the game culls it at the far distance (far_cull in the manifest).
Outputs: the FBX files, <out_dir>/manifest.json (triangles, overlap, dims, source sha256 per piece) and <out_dir>/renders.
"""
import bpy, bmesh, sys, os, json, math, hashlib
import numpy as np
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view

ARGS = sys.argv[sys.argv.index("--") + 1:]
CFG = json.load(open(ARGS[0]))
OUT = ARGS[1]
os.makedirs(os.path.join(OUT, "renders"), exist_ok=True)

FBX_OPTS = dict(  # identical to the kits' build scripts (build_env_kit.py, build_bases.py)
    use_selection=True, object_types={"MESH"}, use_mesh_modifiers=False, apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL", global_scale=1.0, axis_forward="-Z", axis_up="Y",
    bake_space_transform=True, mesh_smooth_type="FACE", use_tspace=False, colors_type="SRGB",
    prioritize_active_color=True, add_leaf_bones=False, bake_anim=False, use_custom_props=False,
    path_mode="AUTO", embed_textures=False,
)
FOV_Y, PITCH, DIST = 35.0, 55.0, 23.4          # the owner's gameplay camera
LOD1_RATIO, LOD2_RATIO = 0.45, 0.15
LOD1_GATE, LOD2_GATE = 0.95, 0.80
LOD1_RETRY, LOD2_RETRY = (0.45, 0.6, 0.75), (0.15, 0.25, 0.35)
TINY_M = 0.9
MIN_TRIS = 60
FLAT = ("env_ground_", "env_ledge_", "env_cloud_floor", "cm_light_pool", "cm_egg_pool")
# Kept whole at every level: the lip and rim fringe's dark teeth are art-critical and vanished at 45% (sheet review,
# 2026-11-12), although the silhouette overlap passed.
KEEP_FULL = ("env_lip_",)
# Never culled at the far level even when small: rim joints (gaps in the fringe) and the authored bases props.
NEVER_CULL = ("env_lip_", "cm_")


def tri_count(objs):
    return sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs)


def sha(path):
    return hashlib.sha256(open(path, "rb").read()).hexdigest()


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path, use_custom_normals=False, bake_space_transform=True)
    return [o for o in bpy.data.objects if o not in before and o.type == "MESH"]


def world_bounds(objs):
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


def palette(me):
    """Distinct corner colours of the 'Col' attribute (the kits paint flat palette colours)."""
    ca = me.color_attributes.get("Col") or (me.color_attributes[0] if len(me.color_attributes) else None)
    if ca is None or len(ca.data) == 0:
        return None
    a = np.empty(len(ca.data) * 4, dtype=np.float32)
    ca.data.foreach_get("color", a)
    u = np.unique(np.round(a.reshape(-1, 4), 3), axis=0)
    return u if len(u) <= 96 else None


def snap_colours(me, pal):
    """Collapse interpolates colours; snap every corner back to the nearest source palette colour (team mask alpha too)."""
    ca = me.color_attributes.get("Col") or (me.color_attributes[0] if len(me.color_attributes) else None)
    if ca is None or pal is None or len(ca.data) == 0:
        return
    a = np.empty(len(ca.data) * 4, dtype=np.float32)
    ca.data.foreach_get("color", a)
    c = a.reshape(-1, 4)
    d = ((c[:, None, :] - pal[None, :, :]) ** 2).sum(-1)
    ca.data.foreach_set("color", pal[d.argmin(1)].astype(np.float32).ravel())


def decimate_once(ob, ratio, eps):
    me = ob.data
    lo = Vector([min(v.co[i] for v in me.vertices) for i in range(3)])
    hi = Vector([max(v.co[i] for v in me.vertices) for i in range(3)])
    free = [not any(abs(v.co[i] - lo[i]) < eps or abs(v.co[i] - hi[i]) < eps for i in range(3)) for v in me.vertices]
    for v, f in zip(me.vertices, free):
        v.select = f
    for e in me.edges:
        e.select = free[e.vertices[0]] and free[e.vertices[1]]
    for p in me.polygons:
        p.select = all(free[i] for i in p.vertices)
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.decimate(ratio=ratio)
    bpy.ops.mesh.select_all(action="DESELECT")
    bpy.ops.object.mode_set(mode="OBJECT")


def make_level(src_objs, target_ratio, smooth_of, pal_of, tag):
    """Copies of src_objs decimated so the total lands near target_ratio of the source triangles."""
    src = tri_count(src_objs)
    goal = max(4, int(round(src * target_ratio)))
    out = []
    for o in src_objs:
        n = o.copy()
        n.data = o.data.copy()
        n.name = o.name + "__" + tag
        bpy.context.scene.collection.objects.link(n)
        out.append(n)
    for o, s in zip(out, src_objs):
        own = sum(len(p.vertices) - 2 for p in s.data.polygons)
        if own < 8:
            continue
        size = max(s.dimensions) or 1.0
        eps = max(1e-4, size * 1e-3)
        keep = o.data.copy()
        lo_r, hi_r, best = 0.02, 1.0, None
        r = target_ratio
        for _ in range(7):
            o.data = keep.copy()
            decimate_once(o, r, eps)
            t = sum(len(p.vertices) - 2 for p in o.data.polygons)
            want = own * target_ratio
            if best is None or abs(t - want) < abs(best[1] - want):
                best = (o.data, t, r)
            if abs(t - want) <= max(2, 0.08 * want):
                break
            if t > want:
                hi_r = r
            else:
                lo_r = r
            r = (lo_r + hi_r) / 2
        o.data = best[0]
        me = o.data
        # Edge collapse may place a merged vertex outside the source box; clamp into it so bounds (kit joins, sway
        # weights, culling bounds) never grow.
        n = len(me.vertices)
        co = np.empty(n * 3, dtype=np.float32)
        me.vertices.foreach_get("co", co)
        sco = np.empty(len(s.data.vertices) * 3, dtype=np.float32)
        s.data.vertices.foreach_get("co", sco)
        sco = sco.reshape(-1, 3)
        co = np.clip(co.reshape(-1, 3), sco.min(0), sco.max(0))
        me.vertices.foreach_set("co", co.ravel())
        me.update()
        smooth = smooth_of[s.name]
        me.polygons.foreach_set("use_smooth", [smooth] * len(me.polygons))
        if smooth and hasattr(me, "set_sharp_from_angle"):
            me.set_sharp_from_angle(angle=math.radians(75))

        snap_colours(me, pal_of[s.name])
    for m in list(bpy.data.meshes):
        if m.users == 0:
            bpy.data.meshes.remove(m)
    return out, tri_count(out), goal


# ------------------------------------------------------------------------------------------------- silhouettes
def setup_render():
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage = 1920, 1080, 100
    sc.render.film_transparent = True
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA"
    sc.display.render_aa = "OFF"
    sh = sc.display.shading
    sh.light, sh.color_type, sh.single_color = "FLAT", "SINGLE", (1, 1, 1)
    sh.show_object_outline = False
    cam_data = bpy.data.cameras.new("GameCam")
    cam_data.sensor_fit = "VERTICAL"
    cam_data.angle_y = math.radians(FOV_Y)
    cam_data.clip_end = 500
    cam = bpy.data.objects.new("GameCam", cam_data)
    sc.collection.objects.link(cam)
    sc.camera = cam
    return cam


def aim(cam, target, yaw_deg):
    yaw = math.radians(yaw_deg)
    off = Vector((-math.sin(yaw), math.cos(yaw), 0)) * DIST * math.cos(math.radians(PITCH)) + \
        Vector((0, 0, DIST * math.sin(math.radians(PITCH))))
    cam.location = target + off
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    bpy.context.view_layer.update()


def border_for(cam, objs):
    sc = bpy.context.scene
    lo, hi = world_bounds(objs)
    xs, ys = [], []
    for x in (lo.x, hi.x):
        for y in (lo.y, hi.y):
            for z in (lo.z, hi.z):
                p = world_to_camera_view(sc, cam, Vector((x, y, z)))
                xs.append(p.x)
                ys.append(p.y)
    m = 0.004
    b = (max(0.0, min(xs) - m), min(1.0, max(xs) + m), max(0.0, min(ys) - m), min(1.0, max(ys) + m))
    sc.render.use_border, sc.render.use_crop_to_border = True, True
    sc.render.border_min_x, sc.render.border_max_x, sc.render.border_min_y, sc.render.border_max_y = b


def shot(path, show, everything):
    for o in everything:
        o.hide_render = o not in show
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(path)
    w, h = img.size
    a = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(a)
    bpy.data.images.remove(img)
    return a.reshape(h, w, 4)


def iou(a, b):
    ma, mb = a[..., 3] > 0.5, b[..., 3] > 0.5
    u = np.logical_or(ma, mb).sum()
    return 1.0 if u == 0 else float(np.logical_and(ma, mb).sum()) / float(u)


def silhouettes(cam, levels, tmp):
    """Min IoU of each level against level 0 over four camera yaws (45 + k*90), at the gameplay camera size."""
    everything = [o for objs in levels for o in objs]
    lo, hi = world_bounds(levels[0])
    target = (lo + hi) / 2
    worst = [1.0] * len(levels)
    for k in range(4):
        aim(cam, target, 45 + 90 * k)
        border_for(cam, levels[0])
        base = shot(os.path.join(tmp, "l0.png"), set(levels[0]), everything)
        for li in range(1, len(levels)):
            if levels[li]:
                worst[li] = min(worst[li], iou(base, shot(os.path.join(tmp, "l%d.png" % li), set(levels[li]), everything)))
    return worst


def colour_cell(cam, show, everything, path, cell=(300, 220)):
    sc = bpy.context.scene
    sh = sc.display.shading
    sh.light, sh.color_type, sh.show_object_outline = "STUDIO", "VERTEX", True
    sc.display.render_aa = "8"
    lo, hi = world_bounds(show)
    aim(cam, (lo + hi) / 2, 45)
    border_for(cam, show)
    img = shot(path, set(show), everything)
    sh.light, sh.color_type, sh.show_object_outline = "FLAT", "SINGLE", False
    sc.display.render_aa = "OFF"
    h, w = img.shape[:2]
    s = min(cell[0] / w, cell[1] / h)
    nw, nh = max(1, int(w * s)), max(1, int(h * s))
    yi = (np.arange(nh) / s).astype(int).clip(0, h - 1)
    xi = (np.arange(nw) / s).astype(int).clip(0, w - 1)
    small = img[yi][:, xi]
    out = np.zeros((cell[1], cell[0], 4), dtype=np.float32)
    out[..., :3] = 0.42
    out[..., 3] = 1
    oy, ox = (cell[1] - nh) // 2, (cell[0] - nw) // 2
    al = small[..., 3:4]
    out[oy:oy + nh, ox:ox + nw, :3] = small[..., :3] * al + 0.42 * (1 - al)
    return out


def save_sheet(rows, path):
    """rows: list of [cell, cell, cell] (top row first); Blender images are bottom-up."""
    grid = np.concatenate([np.concatenate(r, axis=1) for r in rows], axis=0)[::-1]
    h, w = grid.shape[:2]
    img = bpy.data.images.new("sheet", w, h, alpha=False)
    img.pixels.foreach_set(np.ascontiguousarray(grid).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


# ------------------------------------------------------------------------------------------------------ main
def wanted(p):
    n = p["name"]
    if n.startswith(FLAT + KEEP_FULL) or n.endswith(("_lod1", "_lod2")):
        return False
    if n.startswith("cm_layout_"):
        return p["tris"] >= 1000
    if p.get("breakable"):
        return p["tris"] >= MIN_TRIS
    if p["count"] == 0:
        return p["tris"] >= 300
    return p["tris"] >= 100 or (p["tris"] >= MIN_TRIS and p["count"] * p["tris"] >= 4000)


def export(objs, path):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    res = bpy.ops.export_scene.fbx(filepath=path, **FBX_OPTS)
    if "FINISHED" not in res:
        raise SystemExit("export failed " + path)


manifest = {"rule": "<piece>_lod1.fbx ~45% (IoU>=0.95), <piece>_lod2.fbx ~15% (IoU>=0.80); far_cull: no far level",
            "camera": [FOV_Y, PITCH, DIST], "pieces": {}}
sheet_cells = []
tmp = os.path.join(OUT, "tmp")
os.makedirs(tmp, exist_ok=True)
order = sorted(CFG["pieces"], key=lambda p: -p["count"] * p["tris"])
for p in order:
    name, path = p["name"], p["path"]
    reset()
    objs = import_fbx(path)
    if p["name"].startswith("cm_layout_"):
        objs = [o for o in objs if "cm_district_" in o.name]
    if not objs:
        continue
    lo, hi = world_bounds(objs)
    dims = [round(hi[i] - lo[i], 3) for i in range(3)]
    row = dict(tris0=tri_count(objs), count=p["count"], dims=dims, sha256=sha(path),
               far_cull=max(dims) < TINY_M and not name.startswith(NEVER_CULL), lod1=None, lod2=None, iou1=None, iou2=None)

    manifest["pieces"][name] = row
    if not wanted(p):
        continue
    smooth_of = {o.name: (sum(1 for f in o.data.polygons if f.use_smooth) > len(o.data.polygons) / 2) for o in objs}
    pal_of = {o.name: palette(o.data) for o in objs}
    names = {o.name: o.name for o in objs}
    cam = setup_render()
    l1 = l2 = None
    for r1 in LOD1_RETRY:
        l1, t1, _ = make_level(objs, r1, smooth_of, pal_of, "l1")
        s = silhouettes(cam, [objs, l1], tmp)[1]
        if s >= LOD1_GATE or r1 == LOD1_RETRY[-1]:
            break
        for o in l1:
            bpy.data.objects.remove(o)
    if s < LOD1_GATE or t1 > 0.8 * row["tris0"]:
        for o in l1:
            bpy.data.objects.remove(o)
        l1 = None
        row["note"] = "lod1 rejected (IoU %.3f, tris %d)" % (s, t1)
    else:
        row.update(lod1=t1, iou1=round(s, 4), ratio1=r1)
    if l1 is not None and not row["far_cull"]:
        for r2 in LOD2_RETRY:
            l2, t2, _ = make_level(objs, r2, smooth_of, pal_of, "l2")
            s2 = silhouettes(cam, [objs, l2], tmp)[1]
            if s2 >= LOD2_GATE or r2 == LOD2_RETRY[-1]:
                break
            for o in l2:
                bpy.data.objects.remove(o)
        if s2 < LOD2_GATE or t2 >= 0.7 * t1:
            for o in l2:
                bpy.data.objects.remove(o)
            l2 = None
            row["note2"] = "lod2 rejected (IoU %.3f, tris %d): far uses lod1" % (s2, t2)
        else:
            row.update(lod2=t2, iou2=round(s2, 4), ratio2=r2)
    folder = os.path.dirname(path)
    for lvl, objsL in ((1, l1), (2, l2)):
        if not objsL:
            continue
        for o in objsL:  # export under the source object names
            src = o.name.rsplit("__", 1)[0]
            bpy.data.objects[src].name = src + "__src"
            o.name = src
            o.data.name = src
        export(objsL, os.path.join(folder, "%s_lod%d.fbx" % (name, lvl)))
        for o in objsL:
            o.name = o.name + "__l%d" % lvl
            bpy.data.objects[o.name.rsplit("__", 1)[0] + "__src"].name = o.name.rsplit("__", 1)[0]
    if len(sheet_cells) < 16 and l1:
        every = objs + (l1 or []) + (l2 or [])
        cells = [colour_cell(cam, objs, every, os.path.join(tmp, "c0.png")),
                 colour_cell(cam, l1, every, os.path.join(tmp, "c1.png")),
                 colour_cell(cam, l2, every, os.path.join(tmp, "c2.png")) if l2 else np.full((220, 300, 4), 0.2, np.float32)]
        sheet_cells.append((name, cells))
    print("LOD %-32s %6d -> %s / %s  iou %s / %s" % (name, row["tris0"], row["lod1"], row["lod2"], row["iou1"], row["iou2"]), flush=True)

for i in range(0, len(sheet_cells), 8):
    save_sheet([c for _, c in sheet_cells[i:i + 8]], os.path.join(OUT, "renders", "sheet-%d.png" % (i // 8 + 1)))
manifest["sheets"] = [[n for n, _ in sheet_cells[i:i + 8]] for i in range(0, len(sheet_cells), 8)]
json.dump(manifest, open(os.path.join(OUT, "manifest.json"), "w"), indent=1, sort_keys=True)
print("DONE", len(manifest["pieces"]), flush=True)
