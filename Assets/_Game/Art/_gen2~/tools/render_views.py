# gen-models-2 (2026-11-14): verification renders for the generated-model contact sheets. Headless Blender 5.x.
#   blender -b --factory-startup -P render_views.py -- <out_dir> <repo> <inputs> <manifest.json>
# manifest: [{"label": "...", "path": "repo:Assets/..fbx" | "in:file.glb" | "out:file.glb", "views": ["front","tq","game"]}]
# For each entry: clear the scene, import, keep the Hero / unsuffixed meshes (drop _LOD0/_LOD1/_Shadow), rest pose,
# frame an orthographic camera on the mesh, render Workbench (texture if the mesh has one, else vertex colour, else
# material) on a transparent film at 640 px. Writes <label>_<view>.png and stats.json (triangles, size, colour mode).
import bpy, json, math, os, sys
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
OUT, REPO, INP, MAN = argv[0], argv[1], argv[2], argv[3]
os.makedirs(OUT, exist_ok=True)
man = json.load(open(MAN if os.path.isabs(MAN) else os.path.join(REPO, MAN)))
PX = int(os.environ.get("GEN2_PX", "640"))
DROP = ("_LOD0", "_LOD1", "_Shadow", "_shadow")


def resolve(p):
    k, _, rest = p.partition(":")
    return {"repo": REPO, "in": INP, "out": OUT}[k] + "/" + rest


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def setup_scene():
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.render.resolution_x = sc.render.resolution_y = PX
    sc.render.film_transparent = True
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA"
    sh = sc.display.shading
    sh.light = "STUDIO"
    try:
        sh.show_cavity = True; sh.cavity_type = "WORLD"
    except Exception:
        pass
    sh.show_object_outline = True
    try:
        sc.view_settings.view_transform = "Standard"
    except Exception:
        pass
    return sc


def import_any(path):
    ext = os.path.splitext(path)[1].lower()
    if ext in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=path)
    elif ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=path)
    elif ext == ".obj":
        bpy.ops.wm.obj_import(filepath=path)
    else:
        raise RuntimeError("unknown ext " + ext)


def colour_mode(meshes):
    for o in meshes:
        for m in o.data.materials:
            if m and m.use_nodes and any(n.type == "TEX_IMAGE" and n.image for n in m.node_tree.nodes):
                return "TEXTURE"
    for o in meshes:
        if len(o.data.color_attributes):
            return "VERTEX"
    return "MATERIAL"


def main():
    stats = {}
    for e in man:
        label = e["label"]
        try:
            clear()
            sc = setup_scene()
            import_any(resolve(e["path"]))
            for o in list(sc.objects):
                if o.type == "ARMATURE":
                    o.data.pose_position = "REST"
                o.animation_data_clear() if o.animation_data else None
            meshes = [o for o in sc.objects if o.type == "MESH"]
            keep = [o for o in meshes if not any(s in o.name for s in DROP)] or meshes
            for o in meshes:
                if o not in keep:
                    o.hide_render = True
            bpy.context.view_layer.update()
            dg = bpy.context.evaluated_depsgraph_get()
            lo = Vector((1e9,) * 3); hi = Vector((-1e9,) * 3); tris = 0
            for o in keep:
                ev = o.evaluated_get(dg)
                for c in ev.bound_box:
                    w = ev.matrix_world @ Vector(c)
                    lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
                me = ev.to_mesh()
                me.calc_loop_triangles(); tris += len(me.loop_triangles)
                ev.to_mesh_clear()
            size = hi - lo; ctr = (hi + lo) / 2
            mode = e.get("colour") or colour_mode(keep)
            try:
                sc.display.shading.color_type = mode
            except Exception:
                sc.display.shading.color_type = "MATERIAL"
            cam_d = bpy.data.cameras.new("cam"); cam_d.type = "ORTHO"
            cam = bpy.data.objects.new("cam", cam_d); sc.collection.objects.link(cam); sc.camera = cam
            D = max(size) * 4 + 5
            for v in e.get("views", ["front", "tq", "game"]):
                az, el = {"front": (0, 0), "tq": (40, 8), "game": (30, 55), "side": (90, 0), "back": (180, 0)}[v]
                a, b = math.radians(az), math.radians(el)
                dirv = Vector((math.sin(a) * math.cos(b), -math.cos(a) * math.cos(b), math.sin(b)))
                cam.location = ctr + dirv * D
                cam.rotation_euler = (math.radians(90 - el), 0, a)
                ext = max(size.z * math.cos(b) + size.y * math.sin(b), max(size.x, size.y))
                cam_d.ortho_scale = ext * 1.12
                cam_d.clip_end = D * 3
                sc.render.filepath = os.path.join(OUT, f"{label}_{v}.png")
                bpy.ops.render.render(write_still=True)
            stats[label] = {"tris": tris, "size_m": [round(x, 3) for x in size], "min_z": round(lo.z, 3),
                            "colour": mode, "objects": [o.name for o in keep]}
        except Exception as ex:
            stats[label] = {"error": f"{type(ex).__name__}: {ex}"[:300]}
        print("GEN2", label, json.dumps(stats[label]))
    json.dump(stats, open(os.path.join(OUT, "stats.json"), "w"), indent=1)


main()
