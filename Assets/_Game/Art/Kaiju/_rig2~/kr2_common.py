# rig-kaiju-2 (2026-11-14): shared helpers for the green-crystal Kaiju rig scripts. Headless Blender 5.2.3 on GitHub.
import bpy, os, math, json
import numpy as np
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
SRC_FBX = os.path.join(HERE, 'Kaiju_Adult_gen2_static.fbx')
LEVELS = ('Hero', 'LOD0', 'LOD1', 'Shadow')


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_static():
    bpy.ops.import_scene.fbx(filepath=SRC_FBX)
    obs = {}
    for o in list(bpy.context.scene.objects):
        if o.type != 'MESH':
            continue
        for lv in LEVELS:
            if o.name.endswith(lv) or o.name.endswith(lv + '.001'):
                obs[lv] = o
    # apply transforms so vertex data is in world metres
    for o in obs.values():
        o.parent = None
    for o in bpy.context.scene.objects:
        o.select_set(o.type == 'MESH')
    for o in obs.values():
        bpy.context.view_layer.objects.active = o
        with bpy.context.temp_override(active_object=o, object=o, selected_objects=[o], selected_editable_objects=[o]):
            bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in list(bpy.context.scene.objects):
        if o.type != 'MESH':
            bpy.data.objects.remove(o, do_unlink=True)
    return obs


def verts(ob):
    a = np.empty(len(ob.data.vertices) * 3); ob.data.vertices.foreach_get('co', a)
    return a.reshape(-1, 3)


def tris(ob):
    me = ob.data
    me.calc_loop_triangles()
    a = np.empty(len(me.loop_triangles) * 3, dtype=np.int64); me.loop_triangles.foreach_get('vertices', a)
    return a.reshape(-1, 3)


def vcol(ob):
    """Per-vertex RGBA (corner colours averaged)."""
    me = ob.data
    ca = me.color_attributes.active_color or (me.color_attributes[0] if len(me.color_attributes) else None)
    nv = len(me.vertices)
    if ca is None:
        return np.ones((nv, 4)) * 0.5
    c = np.empty(len(ca.data) * 4); ca.data.foreach_get('color', c); c = c.reshape(-1, 4)
    if ca.domain == 'POINT':
        return c
    li = np.empty(len(me.loops), dtype=np.int64); me.loops.foreach_get('vertex_index', li)
    out = np.zeros((nv, 4)); cnt = np.zeros(nv)
    np.add.at(out, li, c); np.add.at(cnt, li, 1)
    return out / np.maximum(cnt, 1)[:, None]


def scene_setup(px=640):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.render.resolution_x = sc.render.resolution_y = px
    sc.render.film_transparent = False
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA'
    sh = sc.display.shading
    sh.light = 'STUDIO'; sh.color_type = 'VERTEX'
    try:
        sh.show_cavity = True; sh.cavity_type = 'WORLD'
    except Exception:
        pass
    sh.show_object_outline = True
    try:
        sc.view_settings.view_transform = 'Standard'
    except Exception:
        pass
    w = bpy.data.worlds.new('W'); sc.world = w
    try:
        w.color = (0.42, 0.44, 0.44)
    except Exception:
        pass
    cam_d = bpy.data.cameras.new('Cam'); cam_d.type = 'ORTHO'; cam_d.clip_end = 200
    cam = bpy.data.objects.new('Cam', cam_d); sc.collection.objects.link(cam); sc.camera = cam
    return sc, cam


VIEWS = {
    # name: (direction the camera looks FROM relative to centre, euler)
    'front': (Vector((0, -30, 0)), (math.radians(90), 0, 0)),
    'side': (Vector((30, 0, 0)), (math.radians(90), 0, math.radians(90))),
    'top': (Vector((0, 0, 30)), (0, 0, 0)),
    'back': (Vector((0, 30, 0)), (math.radians(90), 0, math.radians(180))),
}


def shoot(sc, cam, path, view, centre, scale, px=None):
    centre = Vector(centre)
    if view == 'game':
        # 55 degree gameplay camera, looking from the front-left like the match camera (pitch 55 from horizontal)
        pitch = math.radians(55); yaw = math.radians(-30)
        d = Vector((math.sin(yaw) * math.cos(pitch), -math.cos(yaw) * math.cos(pitch), math.sin(pitch)))
        cam.location = centre + d * 30
        cam.rotation_euler = (math.radians(90) - pitch, 0, yaw)
    else:
        off, eul = VIEWS[view]
        cam.location = centre + off; cam.rotation_euler = eul
    cam.data.ortho_scale = scale
    if px:
        sc.render.resolution_x = sc.render.resolution_y = px
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path


def load_px(p):
    im = bpy.data.images.load(p)
    w, h = im.size
    a = np.empty(w * h * 4, dtype=np.float32); im.pixels.foreach_get(a)
    bpy.data.images.remove(im)
    return a.reshape(h, w, 4)


def save_px(a, out):
    img = bpy.data.images.new('sheet', a.shape[1], a.shape[0], alpha=True)
    img.pixels.foreach_set(a.astype(np.float32).ravel())
    img.filepath_raw = out; img.file_format = 'PNG'; img.save()
    bpy.data.images.remove(img)


def grid_overlay(p, view, centre, scale, step=0.5):
    """Draw metre lines on a render (bottom-up image). Red = world axis 0, white = each metre, grey = each step."""
    a = load_px(p); h, w = a.shape[:2]
    ppm = w / scale
    cx, cy, cz = centre
    if view == 'front':
        hc, vc = cx, cz
    elif view == 'side':
        hc, vc = cy, cz    # side camera looks along -X: screen right = +Y
    elif view == 'back':
        hc, vc = -cx, cz
    else:
        hc, vc = cx, cy
    def put_v(val, col):
        x = int(round(w / 2 + (val - hc) * ppm))
        if 0 <= x < w:
            a[:, x, :3] = a[:, x, :3] * 0.4 + np.array(col) * 0.6
    def put_h(val, col):
        y = int(round(h / 2 + (val - vc) * ppm))
        if 0 <= y < h:
            a[y, :, :3] = a[y, :, :3] * 0.4 + np.array(col) * 0.6
    n = int(scale / step) + 2
    for i in range(-n, n + 1):
        v = round(i * step, 3)
        col = (1, 0, 0) if v == 0 else ((1, 1, 1) if abs(v - round(v)) < 1e-6 else (0.6, 0.6, 0.6))
        put_v(v, col); put_h(v, col)
    save_px(a, p)


def sheet(grid, out, pad=4):
    rows = []
    for row in grid:
        ims = [load_px(p) for p in row]
        hmax = max(i.shape[0] for i in ims)
        ims = [np.pad(i, ((0, hmax - i.shape[0]), (0, pad), (0, 0)), constant_values=1.0) for i in ims]
        rows.append(np.concatenate(ims, axis=1))
    wmax = max(r.shape[1] for r in rows)
    rows = [np.pad(r, ((0, pad), (0, wmax - r.shape[1]), (0, 0)), constant_values=1.0) for r in rows]
    save_px(np.concatenate(rows[::-1], axis=0), out)
    for row in grid:
        for p in row:
            try:
                os.remove(p)
            except OSError:
                pass
