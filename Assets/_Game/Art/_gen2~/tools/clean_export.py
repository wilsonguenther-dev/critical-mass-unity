# gen-models-2 (2026-11-14): clean a generated raw mesh to the game's budgets and export a holding FBX (static mesh,
# NOT rigged, NOT wired into the game). Headless Blender 5.x.
#   blender -b --factory-startup -P clean_export.py -- <out_dir> <repo> <inputs> <manifest.json>
# Per model: import, join, drop small floating parts, read the generated colour (vertex colour or base-colour texture),
# yaw / scale to the class height / origin at the feet centre / front facing Blender -Y (the fighters' export recipe),
# collapse-decimate one copy per level (Hero, LOD0, LOD1, Shadow), flat per-face colour transferred from the raw mesh by
# nearest point, quantised to a k-colour palette, team cloth tagged code 8 (vertex alpha 8/8), glow code 6 (6/8), into a
# BYTE_COLOR corner attribute "Col"; constant UV; one CM_Toon material; FBX with the fighter settings (build_lods.py).
# Also renders every level (front, three-quarter, gameplay 55 deg) and writes <out>/holding/<Name>/<Name>_facts.json.
import bpy, bmesh, json, math, os, sys
import numpy as np
from mathutils import Vector, kdtree

argv = sys.argv[sys.argv.index("--") + 1:]
OUT, REPO, INP, MAN = argv[0], argv[1], argv[2], argv[3]
man = json.load(open(MAN if os.path.isabs(MAN) else os.path.join(REPO, MAN)))
PX = 640
FBX = dict(object_types={"MESH"}, use_mesh_modifiers=False, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
           global_scale=1.0, axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE",
           use_tspace=False, colors_type="SRGB", prioritize_active_color=True, add_leaf_bones=False,
           use_custom_props=False, path_mode="AUTO", embed_textures=False)


def resolve(p):
    k, _, rest = p.partition(":")
    return {"repo": REPO, "in": INP, "out": OUT}[k] + "/" + rest


def hex2rgb(h):
    h = h.lstrip("#"); return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)])


def lin2srgb(c):
    c = np.clip(c, 0, 1); return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def srgb2lin(c):
    c = np.clip(c, 0, 1); return np.where(c <= 0.04045, c / 12.92, np.power((c + 0.055) / 1.055, 2.4))


def sel_only(objs, active):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active


def import_join(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if path.lower().endswith((".glb", ".gltf")):
        bpy.ops.import_scene.gltf(filepath=path)
    else:
        bpy.ops.import_scene.fbx(filepath=path)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    for o in meshes:
        me = o.data.copy() if o.data.users > 1 else o.data
        o.data = me
        me.transform(o.matrix_world)
        o.parent = None
        o.matrix_world.identity()
    for o in [o for o in bpy.context.scene.objects if o.type != "MESH"]:
        bpy.data.objects.remove(o, do_unlink=True)
    sel_only(meshes, meshes[0])
    if len(meshes) > 1:
        bpy.ops.object.join()
    return bpy.context.view_layer.objects.active


def drop_floaters(ob, frac):
    sel_only([ob], ob)
    n0 = len(ob.data.vertices)
    bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.separate(type="LOOSE"); bpy.ops.object.mode_set(mode="OBJECT")
    parts = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    parts.sort(key=lambda o: -len(o.data.vertices))
    keep = [parts[0]] + [o for o in parts[1:] if len(o.data.vertices) >= frac * n0]
    dropped = 0
    for o in parts:
        if o not in keep:
            dropped += len(o.data.vertices); bpy.data.objects.remove(o, do_unlink=True)
    sel_only(keep, keep[0])
    if len(keep) > 1:
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    return ob, {"parts_kept": len(keep), "parts_dropped": len(parts) - len(keep), "verts_dropped": dropped}


def source_colours(ob):
    me = ob.data; nv = len(me.vertices); nl = len(me.loops)
    lv = np.empty(nl, np.int32); me.loops.foreach_get("vertex_index", lv)
    acc = np.zeros((nv, 3)); cnt = np.zeros(nv)
    if len(me.color_attributes):
        a = me.color_attributes.active_color or me.color_attributes[0]
        n = len(a.data); buf = np.empty(n * 4, np.float32)
        try:
            a.data.foreach_get("color_srgb", buf); c = buf.reshape(-1, 4)[:, :3]
        except Exception:
            a.data.foreach_get("color", buf); c = lin2srgb(buf.reshape(-1, 4)[:, :3])
        if a.domain == "POINT":
            return c.astype(np.float64), "vertex colour"
        np.add.at(acc, lv, c); np.add.at(cnt, lv, 1)
        return acc / np.maximum(cnt, 1)[:, None], "corner colour"
    img = None
    for m in me.materials:
        if m and m.use_nodes:
            for nd in m.node_tree.nodes:
                if nd.type == "TEX_IMAGE" and nd.image:
                    img = nd.image; break
        if img:
            break
    if img is None or not me.uv_layers:
        return np.full((nv, 3), 0.6), "none"
    W, H = img.size; px = np.empty(W * H * 4, np.float32); img.pixels.foreach_get(px); px = px.reshape(H, W, 4)
    uv = np.empty(nl * 2, np.float32); me.uv_layers.active.data.foreach_get("uv", uv); uv = uv.reshape(-1, 2)
    x = np.clip((np.mod(uv[:, 0], 1.0) * W).astype(int), 0, W - 1); y = np.clip((np.mod(uv[:, 1], 1.0) * H).astype(int), 0, H - 1)
    c = px[y, x, :3].astype(np.float64)
    if img.colorspace_settings.name not in ("sRGB",) and img.is_float:
        c = lin2srgb(c)
    np.add.at(acc, lv, c); np.add.at(cnt, lv, 1)
    return acc / np.maximum(cnt, 1)[:, None], f"texture {W}x{H}"


def normalise(ob, height, yaw):
    me = ob.data; n = len(me.vertices); co = np.empty(n * 3); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
    if yaw:
        a = math.radians(yaw); R = np.array([[math.cos(a), -math.sin(a), 0], [math.sin(a), math.cos(a), 0], [0, 0, 1]])
        co = co @ R.T
    z0, z1 = co[:, 2].min(), co[:, 2].max(); s = height / (z1 - z0)
    co = (co - [0, 0, z0]) * s
    feet = co[co[:, 2] < 0.04 * height]
    cx, cy = (feet[:, 0].mean(), feet[:, 1].mean()) if len(feet) > 20 else (co[:, 0].mean(), co[:, 1].mean())
    co[:, 0] -= cx; co[:, 1] -= cy
    me.vertices.foreach_set("co", co.ravel()); me.update()
    return co, s


def tri_count(ob):
    me = ob.data; me.calc_loop_triangles(); return len(me.loop_triangles)


def decimated_copy(src, name, target):
    ob = src.copy(); ob.data = src.data.copy(); ob.name = ob.data.name = name
    bpy.context.scene.collection.objects.link(ob)
    t = tri_count(ob)
    for _ in range(3):
        if t <= target:
            break
        md = ob.modifiers.new("dec", "DECIMATE"); md.decimate_type = "COLLAPSE"; md.ratio = max(1e-4, target / t * 0.98)
        md.use_collapse_triangulate = True
        sel_only([ob], ob); bpy.ops.object.modifier_apply(modifier=md.name)
        t = tri_count(ob)
    bm = bmesh.new(); bm.from_mesh(ob.data)
    bmesh.ops.dissolve_degenerate(bm, dist=1e-6, edges=bm.edges[:])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(ob.data); bm.free(); ob.data.update()
    return ob


def kmeans(X, k, it=16, seed=0):
    rng = np.random.default_rng(seed)
    C = X[rng.choice(len(X), k, replace=False)].copy()
    for _ in range(it):
        d = ((X[:, None, :] - C[None]) ** 2).sum(-1); lab = d.argmin(1)
        for j in range(k):
            m = lab == j
            if m.any():
                C[j] = X[m].mean(0)
    return C


def face_colours(ob, kd, srccol):
    me = ob.data; nf = len(me.polygons); out = np.zeros((nf, 3))
    for i, p in enumerate(me.polygons):
        c = p.center; pts = [c] + [c.lerp(me.vertices[v].co, 0.5) for v in p.vertices]
        out[i] = np.mean([srccol[kd.find(q)[1]] for q in pts], axis=0)
    return out


def write_colours(ob, rgba_face):
    me = ob.data
    for a in list(me.color_attributes):
        me.color_attributes.remove(a)
    a = me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    lp = np.empty(len(me.loops), np.int32)
    tot = np.empty(len(me.polygons), np.int32); me.polygons.foreach_get("loop_total", tot)
    lp = np.repeat(np.arange(len(me.polygons)), tot)
    c = rgba_face[lp].astype(np.float32)
    try:
        a.data.foreach_set("color_srgb", c.ravel())
    except Exception:
        c2 = c.copy(); c2[:, :3] = srgb2lin(c2[:, :3]); a.data.foreach_set("color", c2.ravel())
    me.color_attributes.active_color_name = "Col"
    me.color_attributes.render_color_index = me.color_attributes.find("Col")
    for uvl in list(me.uv_layers):
        me.uv_layers.remove(uvl)
    uv = me.uv_layers.new(name="UVMap"); uv.data.foreach_set("uv", np.full(len(me.loops) * 2, 0.5, np.float32))


def toon_material():
    m = bpy.data.materials.get("CM_Toon") or bpy.data.materials.new("CM_Toon")
    m.use_nodes = True
    nt = m.node_tree; bsdf = nt.nodes.get("Principled BSDF")
    ca = nt.nodes.new("ShaderNodeVertexColor"); ca.layer_name = "Col"
    if bsdf:
        nt.links.new(ca.outputs["Color"], bsdf.inputs["Base Color"])
    return m


def render(objs, label, views=("front", "tq", "game"), mode="VERTEX"):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"; sc.render.resolution_x = sc.render.resolution_y = PX
    sc.render.film_transparent = True; sc.render.image_settings.file_format = "PNG"; sc.render.image_settings.color_mode = "RGBA"
    sh = sc.display.shading; sh.light = "STUDIO"; sh.color_type = mode; sh.show_object_outline = True
    try:
        sh.show_cavity = True; sh.cavity_type = "WORLD"; sc.view_settings.view_transform = "Standard"
    except Exception:
        pass
    vis = {o: o.hide_render for o in sc.objects}
    for o in sc.objects:
        o.hide_render = o not in objs
    lo = Vector((1e9,) * 3); hi = Vector((-1e9,) * 3)
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c); lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
    size = hi - lo; ctr = (hi + lo) / 2
    cam = sc.camera
    if cam is None:
        cd = bpy.data.cameras.new("cam"); cd.type = "ORTHO"; cam = bpy.data.objects.new("cam", cd)
        sc.collection.objects.link(cam); sc.camera = cam
    D = max(size) * 4 + 5
    for v in views:
        az, el = {"front": (0, 0), "tq": (40, 8), "game": (30, 55), "side": (90, 0), "back": (180, 0)}[v]
        a, b = math.radians(az), math.radians(el)
        cam.location = ctr + Vector((math.sin(a) * math.cos(b), -math.cos(a) * math.cos(b), math.sin(b))) * D
        cam.rotation_euler = (math.radians(90 - el), 0, a)
        cam.data.ortho_scale = max(size.z * math.cos(b) + size.y * math.sin(b), max(size.x, size.y)) * 1.12
        cam.data.clip_end = D * 3
        sc.render.filepath = os.path.join(OUT, "renders", f"{label}_{v}.png")
        bpy.ops.render.render(write_still=True)
    for o, h in vis.items():
        o.hide_render = h


def clean(e):
    name = e["name"]; kind = e.get("kind", "fighter")
    hold = os.path.join(OUT, "holding", e.get("dir", name)); os.makedirs(hold, exist_ok=True)
    facts = {"name": name, "source": e["src"], "kind": kind}
    ob = import_join(resolve(e["src"]))
    facts["raw_tris"] = tri_count(ob)
    ob, facts["floaters"] = drop_floaters(ob, e.get("floater_frac", 0.004))
    if e.get("trim_below"):
        co0 = np.empty(len(ob.data.vertices) * 3); ob.data.vertices.foreach_get("co", co0); co0 = co0.reshape(-1, 3)
        zc = co0[:, 2].min() + e["trim_below"] * (co0[:, 2].max() - co0[:, 2].min())
        bm = bmesh.new(); bm.from_mesh(ob.data)
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < zc], context="VERTS")
        bm.to_mesh(ob.data); bm.free(); ob.data.update(); facts["trimmed_below_frac"] = e["trim_below"]
    srccol, facts["colour_source"] = source_colours(ob)
    co, facts["scale_applied"] = normalise(ob, e["height"], e.get("yaw", 0))
    kd = kdtree.KDTree(len(co))
    for i, p in enumerate(co):
        kd.insert(p, i)
    kd.balance()
    ob.name = name + "_RAW"
    lods = e.get("lods", {"Hero": 22000, "LOD0": 4000, "LOD1": 1400, "Shadow": 750})
    prefix = e.get("prefix", name)
    levels = {}; cols = {}
    for lv, tgt in lods.items():
        o = decimated_copy(ob, f"{prefix}_{lv}", tgt); levels[lv] = o
        cols[lv] = face_colours(o, kd, srccol)
    pal = kmeans(cols["Hero"], e.get("k", 14))
    codes = np.zeros(len(pal), int)
    for h in e.get("cloth", []):
        codes[np.where(np.linalg.norm(pal - hex2rgb(h), axis=1) < e.get("cloth_tol", 0.16))] = 8
    for h in e.get("glow", []):
        codes[np.where((np.linalg.norm(pal - hex2rgb(h), axis=1) < e.get("glow_tol", 0.2)) & (codes == 0))] = 6
    facts["palette"] = [{"hex": "#%02X%02X%02X" % tuple(int(round(x * 255)) for x in c), "code": int(k)} for c, k in zip(pal, codes)]
    mat = toon_material(); facts["levels"] = {}
    for lv, o in levels.items():
        lab = ((cols[lv][:, None, :] - pal[None]) ** 2).sum(-1).argmin(1)
        rgba = np.concatenate([pal[lab], (codes[lab] / 8.0)[:, None]], axis=1)
        if lv == "Shadow":
            rgba[:, :3] = 0.2; rgba[:, 3] = 0
        write_colours(o, rgba)
        o.data.materials.clear(); o.data.materials.append(mat)
        sel_only([o], o)
        bpy.ops.object.shade_flat()
        try:
            bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
        except Exception:
            pass
        bb = np.array([o.matrix_world @ Vector(c) for c in o.bound_box])
        facts["levels"][lv] = {"tris": tri_count(o), "verts": len(o.data.vertices),
                               "size_m": [round(float(x), 3) for x in bb.max(0) - bb.min(0)],
                               "min_z": round(float(bb[:, 2].min()), 4),
                               "faces_per_code": {str(int(k)): int((codes[lab] == k).sum()) for k in np.unique(codes[lab])}}
    bpy.data.objects.remove(ob, do_unlink=True)
    for lv, o in levels.items():
        if lv in ("Hero", "LOD0"):
            render([o], f"clean_{e.get('dir', name)}_{lv}")
    sel_only(list(levels.values()), levels["Hero"])
    bpy.ops.export_scene.fbx(filepath=os.path.join(hold, name + ".fbx"), use_selection=True, **FBX)
    facts["fbx"] = os.path.join(e.get("dir", name), name + ".fbx")
    json.dump(facts, open(os.path.join(hold, name + "_facts.json"), "w"), indent=1)
    return facts


def render_only(e):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    p = resolve(e["path"])
    bpy.ops.import_scene.gltf(filepath=p) if p.lower().endswith(".glb") else bpy.ops.import_scene.fbx(filepath=p)
    for o in bpy.context.scene.objects:
        if o.type == "ARMATURE":
            o.data.pose_position = "REST"
    objs = [o for o in bpy.context.scene.objects if o.type == "MESH" and not any(s in o.name for s in ("_LOD0", "_LOD1", "_Shadow"))]
    render(objs, e["label"], e.get("views", ("front", "tq", "game")), e.get("colour", "VERTEX"))


def main():
    os.makedirs(os.path.join(OUT, "renders"), exist_ok=True)
    allf = {}
    for e in man.get("clean", []):
        try:
            allf[e.get("dir", e["name"])] = clean(e)
        except Exception as ex:
            import traceback; traceback.print_exc()
            allf[e.get("dir", e["name"])] = {"error": f"{type(ex).__name__}: {ex}"[:400]}
        print("GEN2CLEAN", json.dumps(allf[e.get("dir", e["name"])])[:1500])
    for e in man.get("render", []):
        try:
            render_only(e)
        except Exception as ex:
            print("GEN2RENDER fail", e["label"], ex)
    json.dump(allf, open(os.path.join(OUT, "clean_facts.json"), "w"), indent=1)


main()
