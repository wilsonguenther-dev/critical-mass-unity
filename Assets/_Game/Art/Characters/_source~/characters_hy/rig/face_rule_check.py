# rig-builder-4 (2026-11-15): ArtImportTests.HyFacesArePaintedInVertexColourAndNeverTeamSwapped, replayed in Blender on
# an imported FBX (Unity mesh x = Blender x, Unity y = Blender z (up), Unity z = Blender y (depth); one Unity vertex per
# distinct (vertex, corner colour) pair, as the FBX splits them).   blender -b -P face_rule_check.py -- <out> <fbx> <mesh>...
import bpy, sys, os, json
import numpy as np
argv = sys.argv[sys.argv.index('--') + 1:]
OUT = argv[0]; os.makedirs(OUT, exist_ok=True); REP = {}
for fbx, mname in zip(argv[1::2], argv[2::2]):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=fbx, use_anim=False)
    o = next(x for x in bpy.data.objects if x.type == 'MESH' and x.name.startswith(mname))
    me = o.data; M = o.matrix_world
    ca = me.color_attributes.active_color or me.color_attributes[0]
    gi = {g.index: g.name for g in o.vertex_groups}
    hw = np.zeros(len(me.vertices))
    for v in me.vertices:
        for g in v.groups:
            if gi.get(g.group) == 'head':
                hw[v.index] = g.weight
    pts = {}
    for p in me.polygons:
        for li in p.loop_indices:
            vi = me.loops[li].vertex_index
            c = tuple(round(x, 3) for x in (ca.data[li].color_srgb if ca.domain == 'CORNER' else ca.data[vi].color_srgb))
            pts[(vi, c)] = 1
    V = np.array([list(M @ me.vertices[vi].co) for vi, c in pts]); C = np.array([c for vi, c in pts]); W = np.array([hw[vi] for vi, c in pts])
    X, Y, Z = V[:, 0], V[:, 2], V[:, 1]
    skin = (C[:, 0] > 0.45) & (C[:, 0] > C[:, 1]) & (C[:, 1] > C[:, 2]) & (C[:, 0] - C[:, 2] > 0.12)
    luma = C[:, :3].mean(1)
    on = W > 0.5; cz = Z[on].mean()
    sf, sb = int((on & (Z > cz) & skin).sum()), int((on & (Z <= cz) & skin).sum())
    front = sf >= sb
    half = on & ((Z > cz) == front)
    sk = half & skin
    x0, x1, y0, y1 = X[sk].min(), X[sk].max(), Y[sk].min(), Y[sk].max()
    dark = half & (luma < 0.25)
    feat = dark & (X > x0) & (X < x1) & (Y > y0) & (Y < y1)
    darkall = on & (luma < 0.25)
    REP[os.path.basename(fbx)] = dict(on_head=int(on.sum()), cz=round(float(cz), 4), skin_front=sf, skin_back=sb, front_is_plus_depth=bool(front),
        skin=int(sk.sum()), skin_box_x=[round(float(x0), 3), round(float(x1), 3)], skin_box_up=[round(float(y0), 3), round(float(y1), 3)],
        dark_in_half=int(dark.sum()), features=int(feat.sum()), dark_on_head=int(darkall.sum()),
        dark_on_head_depth=[round(float(Z[darkall].min()), 3), round(float(Z[darkall].max()), 3)] if darkall.any() else None,
        dark_on_head_up=[round(float(Y[darkall].min()), 3), round(float(Y[darkall].max()), 3)] if darkall.any() else None,
        head_depth_range=[round(float(Z[on].min()), 3), round(float(Z[on].max()), 3)],
        contrast=round(float(luma[sk].mean() - luma[feat].mean()), 3) if feat.any() else None,
        face_codes=sorted(set(int(round(a * 8)) for a in C[sk | feat][:, 3])))
json.dump(REP, open(os.path.join(OUT, 'face_rule.json'), 'w'), indent=1)
print('[face_rule]', json.dumps(REP), flush=True)
