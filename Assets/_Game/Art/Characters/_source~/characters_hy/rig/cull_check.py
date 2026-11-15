# rig-builder-4 (2026-11-15): what the game draws, not a two-sided render. For each <fbx> <Cls> <clips> triple:
#   per mesh: winding consistency (manifold edges walked the same way by both faces), faces whose normal points INTO
#   the body (ray parity from the face centre along its normal), non-manifold and open edges, file normals against the
#   winding, the game's outline direction (CM/Toon _OutlineSource 3: the sum of the normals at one position, 0.1 mm,
#   WorldMeshBake.SmoothOutlineTangents) that is shorter than 0.2 per normal (the hull collapses there) or points into
#   the body, colour alpha codes, material slots;
#   render: LOD0, LOD1, Shadow posed (Idle and Run frames from the separate clip file, matrix_basis copied), back faces
#   culled, plus the inverted-hull shell (a copy pushed 2 cm along the averaged normals, normals flipped, flat dark),
#   from the high gameplay camera, then Hero for reference.
#   blender -b -P cull_check.py -- <out> <fbx> <Cls> <clips> [<fbx> <Cls> <clips>]...
import bpy, bmesh, sys, os, json, math
import numpy as np
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
argv = sys.argv[sys.argv.index('--') + 1:]
OUT = argv[0]; os.makedirs(OUT, exist_ok=True); REP = {}


def imp(path, anim):
    bo, ba = set(bpy.data.objects), set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=path, use_anim=anim, use_custom_normals=True, ignore_leaf_bones=False,
                             automatic_bone_orientation=False, primary_bone_axis='Y', secondary_bone_axis='X')
    return [o for o in bpy.data.objects if o not in bo], [a for a in bpy.data.actions if a not in ba]


def measure(o):
    me = o.data
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    incons = nonman = openE = 0
    for e in bm.edges:
        lf = e.link_faces
        if len(lf) == 1:
            openE += 1
        elif len(lf) > 2:
            nonman += 1
        elif len(lf) == 2:
            l0 = next(l for l in e.link_loops if l.face is lf[0]); l1 = next(l for l in e.link_loops if l.face is lf[1])
            if l0.vert is l1.vert:
                incons += 1
    co = [v.co.copy() for v in bm.verts]
    bvh = BVHTree.FromBMesh(bm)
    inward = 0; inward_at = []
    for f in bm.faces:
        if f.calc_area() < 1e-9:
            continue
        n = f.normal; p = f.calc_center_median() + n * 1e-4; hits = 0
        for _ in range(40):
            h = bvh.ray_cast(p, n, 5.0)
            if h[0] is None:
                break
            hits += 1; p = h[0] + n * 1e-4
        if hits % 2 == 1:
            inward += 1
            if len(inward_at) < 8:
                inward_at.append([round(x, 3) for x in f.calc_center_median()])
    # file normals vs winding
    me.calc_normals_split() if hasattr(me, 'calc_normals_split') else None
    try:
        cn = np.array([list(l.vector) for l in me.corner_normals]) if hasattr(me, 'corner_normals') else np.array([list(l.normal) for l in me.loops])
    except Exception as ex:
        print('[cull] corner normals', ex, flush=True); cn = None
    fn = np.repeat(np.array([list(p.normal) for p in me.polygons]), [p.loop_total for p in me.polygons], axis=0)
    against = int(((cn * fn).sum(1) < 0).sum()) if cn is not None and len(cn) == len(fn) else None
    against_hi = int(((cn * fn).sum(1) < -0.5).sum()) if cn is not None and len(cn) == len(fn) else None
    smooth = float(np.mean((cn * fn).sum(1))) if cn is not None and len(cn) == len(fn) else None
    # outline direction: per position, the sum of the corner normals there (Unity splits per corner normal)
    vi = np.empty(len(me.loops), np.int64); me.loops.foreach_get('vertex_index', vi)
    V = np.array([list(v.co) for v in me.vertices])
    key = np.round(V[vi] / 1e-4).astype(np.int64)
    _, inv = np.unique(key, axis=0, return_inverse=True); inv = inv.ravel()
    nn = cn if cn is not None and len(cn) == len(fn) else fn
    S = np.zeros((inv.max() + 1, 3)); np.add.at(S, inv, nn); cnt = np.bincount(inv)
    weak = int(((np.linalg.norm(S, axis=1) / cnt) < 0.2).sum())
    ca = me.color_attributes.active_color or (me.color_attributes[0] if len(me.color_attributes) else None)
    codes = {}
    if ca is not None:
        a = np.empty(len(ca.data) * 4, np.float32); ca.data.foreach_get('color_srgb', a); a = a.reshape(-1, 4)[:, 3]
        u, c = np.unique(np.round(a * 8).astype(int), return_counts=True); codes = {str(int(k)): int(x) for k, x in zip(u, c)}
    bm.free()
    return dict(tris=sum(len(p.vertices) - 2 for p in me.polygons), faces=len(me.polygons), winding_inconsistent_edges=incons,
                nonmanifold_edges=nonman, open_edges=openE, faces_facing_inward=inward, inward_at=inward_at,
                file_normals_against_winding=against, file_normals_against_over60deg=against_hi, file_normal_dot_face_mean=smooth, corners=len(fn), outline_dirs_weak=weak, positions=int(len(cnt)), alpha_codes=codes,
                material_slots=[m.name if m else None for m in me.materials], color_layers=[c.name for c in me.color_attributes],
                has_custom_normals=bool(getattr(me, 'has_custom_normals', False)))


trip = list(zip(argv[1::3], argv[2::3], argv[3::3]))
sc = bpy.context.scene
for fbx, CLS, CLIPS in trip:
    bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene
    mob, _ = imp(fbx, False)
    ma = next(o for o in mob if o.type == 'ARMATURE')
    meshes = {lv: next((o for o in mob if o.type == 'MESH' and o.name.startswith(f'{CLS}_{lv}')), None) for lv in ('Hero', 'LOD0', 'LOD1', 'Shadow')}
    REP[CLS] = {lv: measure(o) for lv, o in meshes.items() if o is not None}
    json.dump(REP, open(os.path.join(OUT, 'cull.json'), 'w'), indent=1)
    print('[cull]', CLS, json.dumps(REP[CLS]), flush=True)
    cob, cact = imp(CLIPS, True)
    ca_ = next(o for o in cob if o.type == 'ARMATURE')
    for o in cob:
        if o is not ca_:
            bpy.data.objects.remove(o, do_unlink=True)
    acts = {a.name.split('|')[-1]: a for a in cact}
    for o in meshes.values():
        if o is not None and not any(m.type == 'ARMATURE' for m in o.modifiers):
            m = o.modifiers.new('Armature', 'ARMATURE'); m.object = ma
    # hull shells
    shells = {}
    for lv, o in meshes.items():
        if o is None:
            continue
        s = o.copy(); s.data = o.data.copy(); s.name = o.name + '_hull'; sc.collection.objects.link(s)
        c = s.data.color_attributes.active_color or s.data.color_attributes[0]
        a = np.tile(np.array([0.06, 0.05, 0.07, 0.0], np.float32), len(c.data)); c.data.foreach_set('color_srgb', a)
        so = s.modifiers.new('Hull', 'SOLIDIFY'); so.thickness = 0.02; so.offset = 1.0; so.use_flip_normals = True; so.use_rim = False
        so.use_even_offset = False; so.use_quality_normals = True
        try:
            so.solidify_mode = 'EXTRUDE'
        except Exception:
            pass
        so.use_rim_only = False
        shells[lv] = s
    sc.render.engine = 'BLENDER_WORKBENCH'; sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'VERTEX'
    sc.display.shading.show_backface_culling = True
    sc.display.shading.background_type = 'VIEWPORT'; sc.display.shading.background_color = (0.55, 0.6, 0.65)
    sc.render.resolution_x, sc.render.resolution_y = 300, 360
    cd = bpy.data.cameras.new('cam'); cd.type = 'ORTHO'; cd.ortho_scale = 3.2
    cam = bpy.data.objects.new('cam', cd); sc.collection.objects.link(cam); sc.camera = cam
    FY = -1.0
    loc, tgt = Vector((4.2, FY * 4.2, 5.2)), Vector((0, 0, 1.0))
    cam.location = loc; cam.rotation_euler = (tgt - loc).to_track_quat('-Z', 'Y').to_euler()
    rows = []
    for clip in ('Idle', 'Run'):
        a = acts.get(clip)
        ad = ca_.animation_data or ca_.animation_data_create(); ad.action = a
        try:
            if ad.action_slot is None and len(a.slots):
                ad.action_slot = a.slots[0]
        except Exception:
            pass
        f0, f1 = int(a.frame_range[0]), int(a.frame_range[1]); sc.frame_set(f0 + (f1 - f0) // 3); bpy.context.view_layer.update()
        for pb in ma.pose.bones:
            c = ca_.pose.bones.get(pb.name); pb.matrix_basis = c.matrix_basis.copy() if c else Matrix()
        bpy.context.view_layer.update()
        row = []
        for lv in ('LOD0', 'LOD1', 'Shadow', 'Hero'):
            if meshes.get(lv) is None:
                continue
            for o in list(meshes.values()) + list(shells.values()):
                if o is not None:
                    o.hide_render = o not in (meshes[lv], shells[lv])
            p = os.path.join(OUT, f'_c_{CLS}_{clip}_{lv}.png'); sc.render.filepath = p; bpy.ops.render.render(write_still=True)
            img = bpy.data.images.load(p); row.append(np.array(img.pixels[:], np.float32).reshape(img.size[1], img.size[0], 4))
            bpy.data.images.remove(img); os.remove(p)
        rows.append(np.concatenate(row, axis=1))
    sh = np.concatenate(rows[::-1], axis=0)
    im = bpy.data.images.new('c', sh.shape[1], sh.shape[0]); im.pixels = sh.ravel()
    im.filepath_raw = os.path.join(OUT, f'{CLS}_culled_hull_high.png'); im.file_format = 'PNG'; im.save()
print('[cull] done', flush=True)
