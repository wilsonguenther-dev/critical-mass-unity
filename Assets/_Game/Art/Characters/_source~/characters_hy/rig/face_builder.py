# rig-builder-4 (2026-11-15): the Builder's face as placed geometry on the head bone, identical at every level.
#   blender -b -P face_builder.py -- <out> <Cls> <model.fbx> <clips.fbx>      (env FACE_JSON: '{"eye_x":..}' overrides)
# The method of rig-demolitions-1 (10e044a, rig_body.py face_features + clean_export.py face_features mode skin): the
# painted eyes and brows in the face box go to clean skin on every level (whole triangles cannot draw an eye at the LOD
# densities; the voxel LODs blur them), then eyes (8-sided dark ovals with a near-white highlight) and arched brows are
# added as small geometry: each vertex projected onto THAT level's skin from the front and lifted along the skin normal,
# 100 % on the head bone, dark brown #33241E, mask code 0 (never team-swapped). The same shapes go onto Hero, LOD0, LOD1
# and Shadow. Positions come from the Hero's painted features (dark code-0 samples on a front ray grid, the darkest
# blob pair on each side), or FACE_JSON. Then fbx_pair_check.py replays the clips from the separate clip file.
import bpy, bmesh, sys, os, json, math, runpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index('--') + 1:]
OUT, CLS, MODEL, CLIPS = argv[0], argv[1], argv[2], argv[3]
os.makedirs(OUT, exist_ok=True)
REP = {}
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
bpy.ops.import_scene.fbx(filepath=MODEL, use_anim=False, use_custom_normals=False, ignore_leaf_bones=False,
                         automatic_bone_orientation=False, primary_bone_axis='Y', secondary_bone_axis='X')
rig = next(o for o in scene.objects if o.type == 'ARMATURE')
LV = {lv: next(o for o in scene.objects if o.type == 'MESH' and o.name.startswith(f'{CLS}_{lv}'))
      for lv in ('Hero', 'LOD0', 'LOD1', 'Shadow')}
ntri = lambda o: sum(len(p.vertices) - 2 for p in o.data.polygons)
REP['tris_before'] = {lv: ntri(o) for lv, o in LV.items()}
B = rig.data.bones
hb = B['head']
H0, H1 = rig.matrix_world @ hb.head_local, rig.matrix_world @ hb.tail_local
toe = next((b for b in B if 'toe' in b.name.lower()), None)
foot = toe.parent if toe is not None else None
F = -1.0
if toe is not None and foot is not None:
    F = 1.0 if (rig.matrix_world @ toe.head_local).y > (rig.matrix_world @ foot.head_local).y else -1.0
REP['facing_y'] = F; REP['head_bone'] = [list(map(lambda v: round(v, 4), H0)), list(map(lambda v: round(v, 4), H1))]


def colattr(me):
    ca = me.color_attributes.active_color or (me.color_attributes[0] if len(me.color_attributes) else None)
    if ca is None:
        ca = me.color_attributes.new('Col', 'BYTE_COLOR', 'CORNER')
    return ca


def corner_cols(me):
    ca = colattr(me)
    a = np.empty(len(ca.data) * 4, np.float32); ca.data.foreach_get('color_srgb', a); a = a.reshape(-1, 4)
    if ca.domain != 'CORNER':
        vi = np.empty(len(me.loops), np.int64); me.loops.foreach_get('vertex_index', vi); a = a[vi]
    return ca, a


def world_bvh(o):
    M = o.matrix_world
    return BVHTree.FromPolygons([M @ v.co for v in o.data.vertices], [tuple(p.vertices) for p in o.data.polygons])


lum = lambda c: 0.299 * c[..., 0] + 0.587 * c[..., 1] + 0.114 * c[..., 2]
# ---- measure the Hero face: front ray grid in the head region
hero = LV['Hero']; me = hero.data
_, HC = corner_cols(me)
pl = np.array([p.loop_start for p in me.polygons]); pn = np.array([p.loop_total for p in me.polygons])
poly_col = np.array([HC[s:s + n].mean(0) for s, n in zip(pl, pn)])
bvh = world_bvh(hero)
xs = np.arange(-0.20, 0.2001, 0.004); zs = np.arange(H0.z - 0.10, H0.z + 0.50, 0.004)
G = np.full((len(zs), len(xs), 5), np.nan)   # r g b code depth
for i, z in enumerate(zs):
    for j, x in enumerate(xs):
        hit = bvh.ray_cast(Vector((x, F * 3.0, z)), Vector((0, -F, 0)))
        if hit[0] is not None:
            c = poly_col[hit[2]]
            G[i, j] = [c[0], c[1], c[2], round(c[3] * 8), abs(hit[0].y - F * 3.0)]
ok = ~np.isnan(G[..., 0])
code0 = ok & (G[..., 3] == 0)
warm = code0 & (G[..., 0] - G[..., 2] > 0.08) & (lum(G[..., :3]) > 0.3) & (np.abs(xs)[None, :] < 0.09)
q = np.round(G[warm][:, :3] * 255).astype(int)
vals, cnt = np.unique(q, axis=0, return_counts=True)
skin = vals[cnt.argmax()] / 255.0 if len(vals) else np.array([0.8, 0.6, 0.5])
REP['skin'] = '#%02X%02X%02X' % tuple(int(round(v * 255)) for v in skin)
dark = code0 & (lum(G[..., :3]) < 0.62 * lum(skin)) & (np.abs(xs)[None, :] < 0.17)
# ascii map, top row first: '#' dark code 0, 's' skin-ish code 0, 'c' cloth code 8, 'o' other, ' ' miss
amap = []
for i in range(len(zs) - 1, -1, -1):
    row = ''
    for j in range(len(xs)):
        if not ok[i, j]: row += ' '
        elif dark[i, j]: row += '#'
        elif G[i, j, 3] == 8: row += 'c'
        elif code0[i, j] and abs(lum(G[i, j, :3]) - lum(skin)) < 0.08: row += 's'
        else: row += 'o'
    amap.append('%.3f %s' % (zs[i], row))
REP['face_map'] = amap
REP['face_map_x'] = [round(float(xs[0]), 3), round(float(xs[-1]), 3), 0.004]
# blobs of dark samples (4-neighbour)
lab = -np.ones(dark.shape, int); blobs = []
for i in range(dark.shape[0]):
    for j in range(dark.shape[1]):
        if dark[i, j] and lab[i, j] < 0:
            st = [(i, j)]; lab[i, j] = len(blobs); pts = []
            while st:
                a, b = st.pop(); pts.append((a, b))
                for da, db in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    u, v = a + da, b + db
                    if 0 <= u < dark.shape[0] and 0 <= v < dark.shape[1] and dark[u, v] and lab[u, v] < 0:
                        lab[u, v] = len(blobs); st.append((u, v))
            P = np.array([(xs[b], zs[a]) for a, b in pts])
            blobs.append(dict(n=len(pts), cx=float(P[:, 0].mean()), cz=float(P[:, 1].mean()), w=float(np.ptp(P[:, 0]) + 0.004),
                              h=float(np.ptp(P[:, 1]) + 0.004)))
REP['dark_blobs'] = [{k: round(v, 3) if isinstance(v, float) else v for k, v in b.items()} for b in blobs if b['n'] >= 2]
FF = dict(eye_w=0.034, eye_h=0.044, sides=8, brow_w=0.08, brow_t=0.014, arch=0.006, lift=0.0025, brow_lift=0.012, brow_cols=5,
          colour='#33241E', highlight='#F4F1EA')
side = [b for b in blobs if b['n'] >= 3 and 0.02 < abs(b['cx']) < 0.15]
pairs = []
for b in side:
    m = [c for c in side if c is not b and c['cx'] * b['cx'] < 0 and abs(abs(c['cx']) - abs(b['cx'])) < 0.03
         and abs(c['cz'] - b['cz']) < 0.02]
    if m:
        pairs.append((b['n'] + m[0]['n'], b, m[0]))
pairs.sort(key=lambda t: -t[0])
used = []
for _, a, b in pairs:
    if any(a is u or b is u for u in used):
        continue
    used += [a, b]
feat = sorted({(round((abs(a['cx']) + abs(b['cx'])) / 2, 4), round((a['cz'] + b['cz']) / 2, 4),
                round(max(a['w'], b['w']), 3), round(max(a['h'], b['h']), 3)) for _, a, b in pairs
               if a in used and b in used}, key=lambda t: t[1])
REP['paired_features_x_z_w_h'] = feat
if feat:
    ex, ez, ew, eh = feat[0]
    FF.update(eye_x=ex, eye_z=ez)
    above = [t for t in feat[1:] if 0.025 < t[1] - ez < 0.11]
    if above:
        FF.update(brow_x=above[0][0], brow_z=above[0][1] - FF['brow_t'] / 2)
FF.setdefault('eye_x', 0.075); FF.setdefault('eye_z', float(H0.z + 0.16))
if CLS == 'Builder':   # read on face-1's close-up: the detected dark pair at z 1.926 is his nostrils, the eyes sit 5 cm higher
    FF.update(eye_x=0.08, eye_z=1.978, brow_x=0.085, brow_z=2.028)
FF.setdefault('brow_x', FF['eye_x'] + 0.004); FF.setdefault('brow_z', FF['eye_z'] + 0.052)
FF.update(json.loads(os.environ.get('FACE_JSON', '{}') or '{}'))
REP['face_features_params'] = FF
# ---- every level: face box to clean skin, then the geometry
hexc = lambda h: tuple(int(h.lstrip('#')[i:i + 2], 16) / 255.0 for i in (0, 2, 4))
dark_c, hi_c = hexc(FF['colour']), hexc(FF['highlight'])
xb = FF['eye_x'] + 0.6 * FF['eye_w'] + 0.025
z0b, z1b = FF['eye_z'] - 0.9 * FF['eye_h'], FF['brow_z'] + FF['brow_t'] + 0.02
REP['skin_box'] = [round(-xb, 3), round(xb, 3), round(z0b, 3), round(z1b, 3)]
def drop_loose(o, nmax=3):
    # LOD voxel remnants: islands of 1-3 faces (vertices welded by position at 0.1 mm) are deleted before the face goes on
    me = o.data
    co = np.empty(len(me.vertices) * 3); me.vertices.foreach_get('co', co)
    _, key = np.unique(np.round(co.reshape(-1, 3) / 1e-4).astype(np.int64), axis=0, return_inverse=True)
    key = key.ravel(); par = list(range(int(key.max()) + 1))

    def fnd(a):
        while par[a] != a:
            par[a] = par[par[a]]; a = par[a]
        return a
    for p in me.polygons:
        r0 = fnd(int(key[p.vertices[0]]))
        for v in p.vertices[1:]:
            r1 = fnd(int(key[v]))
            if r1 != r0:
                par[r1] = r0
    grp = {}
    for p in me.polygons:
        grp.setdefault(fnd(int(key[p.vertices[0]])), []).append(p.index)
    kill = sorted(i for g in grp.values() if len(g) <= nmax for i in g)
    if kill:
        bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[bm.faces[i] for i in kill], context='FACES')
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
        bm.to_mesh(me); bm.free(); me.update()
    return len(kill)


# Team colour: the blue trims (cuffs, collar, cap brim, straps) were authored code 0, so a Crimson Builder kept Azure
# trims and the voxel LODs got blue smears on a red shirt. Every saturated blue code-0 corner becomes code 8 (team cloth):
# CMToonSwap scales the target by the corner's brightness, so a darker trim stays a darker band of the team colour.
TEAM_BLUE = os.environ.get('FACE_TEAM_BLUE', '1') == '1'
for lv, o in LV.items():
    if lv != 'Hero':
        REP.setdefault('loose_faces_dropped', {})[lv] = drop_loose(o)
    me = o.data; M = o.matrix_world; Mi = M.inverted()
    ca, C = corner_cols(me)
    if TEAM_BLUE:
        mx, mn = C[:, :3].max(1), C[:, :3].min(1)
        blue = (np.round(C[:, 3] * 8) == 0) & (C[:, 2] - C[:, 0] > 0.12) & (C[:, 2] >= C[:, 1] - 0.02) & (mx - mn > 0.15)
        C[blue, 3] = 1.0
        REP.setdefault('blue_corners_to_code8', {})[lv] = int(blue.sum())
    if lv != 'Hero' and os.environ.get('FACE_FLAT', '1') == '1':
        # one colour per face on the rebuilt LODs (VertexColourFace, as the Hero is painted): the nearest-face transfer
        # gave a big LOD triangle corners from different parts, which the GPU blends into smears (blue-grey on a red shirt)
        # each LOD face takes the colour (and code) of the Hero face nearest its centre, after the Hero's own fixes
        nfl = 0
        for p_ in me.polygons:
            hit = bvh.find_nearest(M @ p_.center)
            if hit[2] is not None:
                C[p_.loop_start:p_.loop_start + p_.loop_total] = HPC[hit[2]]; nfl += 1
        REP.setdefault('faces_flattened', {})[lv] = nfl
    nsk = 0
    for p in me.polygons:
        c = M @ p.center; n = (M.to_3x3() @ p.normal).normalized()
        if abs(c.x) < xb and z0b < c.z < z1b and n.y * F > 0.2:
            s, k = p.loop_start, p.loop_total
            cc = C[s:s + k]
            m = (np.round(cc[:, 3] * 8) == 0) & ((lum(cc[:, :3]) < 0.62 * lum(skin)) | (cc[:, 0] - cc[:, 2] < 0.05))
            if m.any():
                cc[m, :3] = skin; cc[m, 3] = 0.0; C[s:s + k] = cc; nsk += int(m.sum())
    if lv == 'Hero':
        HPC = np.array([C[p.loop_start] for p in me.polygons])   # the Hero is painted one colour per face
    lb = world_bvh(o); miss = [0]

    def on_skin(x, z, lift):
        hit = lb.ray_cast(Vector((x, F * 3.0, z)), Vector((0, -F, 0)))
        if hit[0] is None:
            miss[0] += 1; return Vector((x, F * 0.2, z))
        nn = hit[1] if hit[1].y * F > 0 else -hit[1]
        return hit[0] + nn * lift
    polys = []
    L = FF['lift']; ns = int(FF['sides'])
    for sx in (-1, 1):
        ex, ez, ew, eh = sx * FF['eye_x'], FF['eye_z'], FF['eye_w'], FF['eye_h']
        c0 = on_skin(ex, ez, L)
        ring = [on_skin(ex + 0.5 * ew * math.cos(2 * math.pi * k / ns), ez + 0.5 * eh * math.sin(2 * math.pi * k / ns), L)
                for k in range(ns)]
        for k in range(ns):
            polys.append(([c0, ring[k], ring[(k + 1) % ns]], dark_c))
        hx, hz, hs = ex + F * 0.25 * ew, ez + 0.22 * eh, 0.3 * ew
        polys.append(([on_skin(hx - hs / 2, hz - hs / 3, L + 0.001), on_skin(hx + hs / 2, hz - hs / 3, L + 0.001),
                       on_skin(hx, hz + hs * 0.6, L + 0.001)], hi_c))
        bx, bz, bw, bt, ar = sx * FF['brow_x'], FF['brow_z'], FF['brow_w'], FF['brow_t'], FF['arch']
        nc = int(FF.get('brow_cols', 3))   # more columns follow the brow ridge (the 3-column brow broke up on his left)
        xs3 = [bx - bw / 2 + bw * i / (nc - 1) for i in range(nc)]
        zb = [bz + ar * (1 - ((xx - bx) / (bw / 2)) ** 2) for xx in xs3]
        LB = FF['brow_lift']
        B_ = [on_skin(xs3[i], zb[i], LB) for i in range(nc)]; T_ = [on_skin(xs3[i], zb[i] + bt, LB) for i in range(nc)]
        for i in range(nc - 1):
            polys.append(([B_[i], B_[i + 1], T_[i + 1], T_[i]], dark_c))
    if F > 0:   # wind toward the viewer (Unity culls back faces)
        polys = [(pts[::-1], col) for pts, col in polys]
    nl0 = len(me.loops)
    bm = bmesh.new(); bm.from_mesh(me)
    nv0, nf0 = len(bm.verts), len(bm.faces)
    for pts, col in polys:
        bm.faces.new([bm.verts.new(Mi @ pt) for pt in pts])
    bm.to_mesh(me); bm.free(); me.update()
    ca = colattr(me)
    A = np.empty(len(ca.data) * 4, np.float32); ca.data.foreach_get('color_srgb', A); A = A.reshape(-1, 4)
    if ca.domain == 'CORNER':
        A[:nl0] = C
        for k, (pts, col) in enumerate(polys):
            pg = me.polygons[nf0 + k]
            for li in pg.loop_indices:
                A[li] = (col[0], col[1], col[2], 0.0)
            pg.material_index = 0
    ca.data.foreach_set('color_srgb', A.ravel()); me.update()
    new_v = list(range(nv0, len(me.vertices)))
    for g in list(o.vertex_groups):
        g.remove(new_v)
    o.vertex_groups['head'].add(new_v, 1.0, 'REPLACE')
    REP.setdefault('face_features', {})[lv] = dict(tris_added=sum(len(p_) - 2 for p_, _ in polys), verts_added=len(new_v),
                                                   ray_misses=miss[0], corners_to_skin=nsk)
REP['tris_after'] = {lv: ntri(o) for lv, o in LV.items()}
for ob in scene.objects:
    ob.select_set(ob.type in ('ARMATURE', 'MESH'))
bpy.context.view_layer.objects.active = rig
path = os.path.join(OUT, f'{CLS}.fbx')
r = bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'}, global_scale=1.0,
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, use_mesh_modifiers=False, mesh_smooth_type='FACE',
                             use_tspace=False, colors_type='SRGB', prioritize_active_color=True, add_leaf_bones=False,
                             primary_bone_axis='Y', secondary_bone_axis='X', use_armature_deform_only=False,
                             armature_nodetype='NULL', bake_anim=False, path_mode='STRIP', embed_textures=False,
                             use_custom_props=False)
REP['export'] = dict(path=path, result=list(r), bytes=os.path.getsize(path))
json.dump(REP, open(os.path.join(OUT, f'face_{CLS}.json'), 'w'), indent=1)
print('[face]', json.dumps({k: v for k, v in REP.items() if k != 'face_map'}), flush=True)
sys.argv = ['blender', '--', OUT, CLS, path, CLIPS]
os.environ['PAIR_TEAMS'] = '1'
try:
    HERE = os.path.dirname(os.path.abspath(__file__))
except NameError:
    HERE = os.path.join(os.environ.get('GITHUB_WORKSPACE', '.'), 'Assets/_Game/Art/Characters/_source~/characters_hy/rig')
runpy.run_path(os.path.join(HERE, 'fbx_pair_check.py'), run_name='__main__')
