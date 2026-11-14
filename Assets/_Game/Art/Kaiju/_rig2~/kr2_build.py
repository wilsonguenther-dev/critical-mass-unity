# rig-kaiju-2 (2026-11-14): rig, mouth, weights, clips and export of the green-crystal Kaiju adult. Headless Blender 5.2.3
# on GitHub (loop/gates/remote blender). One process:
#   1. Imports the static gen-models-2 Kaiju (Hero, LOD0, LOD1, Shadow), recentres it on its body (midline x, support y).
#   2. Builds the skeleton (old game names kept: Root Hips Spine Chest Neck Head Jaw Tail1.. Arm Forearm Fist Thigh Shin Foot).
#   3. Cuts the mouth on Hero and LOD0 (lower jaw split from the head along the lip line), adds a dark mouth pouch,
#      teeth and two small glow eyes (glow code 6: they take the allegiance colour).
#   4. Weights: bone heat on a watertight voxel proxy, transferred to every level, limb masking (no cross-limb leaks),
#      rigid head, rigid jaw, rigid crystals (one bone per crystal cluster), rigid rock plates, smoothed, max 4, normalised.
#   5. Clips by script (own FK + analytic two-bone IK, baked to keys every frame, spring wobble baked on the crystals).
#   6. Stress poses with edge-stretch numbers, contact strips (front, side, 55 degree game camera), FBX export, re-import check.
#   args: {out} [quick]
import bpy, bmesh, sys, os, json, math, time, traceback
import numpy as np
from mathutils import Vector, Matrix, Quaternion, Euler
from mathutils.bvhtree import BVHTree
from bpy_extras import anim_utils

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import kr2_common as C

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = argv[0] if argv else '/tmp/kr2'
QUICK = 'quick' in argv
os.makedirs(OUT, exist_ok=True)
REP = {'log': [], 'started': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())}
T0 = time.time()


def log(*a):
    s = ' '.join(str(x) for x in a)
    REP['log'].append(f'{time.time() - T0:7.1f} {s}')
    print('[KR2]', s, flush=True)


def save():
    json.dump(REP, open(os.path.join(OUT, 'kr2_report.json'), 'w'), indent=1, default=str)


def _hook(t, v, tb):
    REP['fatal'] = ''.join(traceback.format_exception(t, v, tb))[-4000:]
    print(REP['fatal'], flush=True)
    save()


sys.excepthook = _hook

# ------------------------------------------------------------------------------------------------ joints (recentred metres)
SHIFT = Vector((0.48, 1.25, 0.0))     # body midline x -0.48 -> 0; body/support centre y -1.25 -> 0 (probe 2026-11-14)
J = {
    'hips': (0, 0.70, 1.70), 'spine': (0, 0.50, 2.20), 'spine2': (0, 0.22, 2.62), 'spine3': (0, -0.12, 2.98),
    'chest': (0, -0.50, 3.15), 'neck': (0, -0.92, 3.02), 'head': (0, -1.18, 2.92), 'head_tip': (0, -1.95, 2.92),
    'jaw': (0, -1.24, 2.74), 'chin': (0, -1.86, 2.56),
    'clav_L': (0.32, -0.62, 3.02), 'arm_L': (1.26, -0.60, 2.80), 'elbow_L': (1.83, -0.18, 2.05),
    'wrist_L': (1.93, -0.70, 1.22), 'fist_L': (1.85, -0.78, 0.66),
    'knuck_L': [(1.60, -1.18, 0.36), (1.85, -1.24, 0.34), (2.10, -1.18, 0.36)],
    'thigh_L': (0.80, 0.66, 1.66), 'knee_L': (0.88, 0.02, 1.06), 'ankle_L': (0.85, 0.46, 0.40),
    'heel_L': (0.85, 0.46, 0.40), 'ball_L': (0.85, -0.06, 0.06), 'toe_L': (0.85, -0.40, 0.05),   # Foot = ankle -> ball (stable while planted)
    'tail': [(-0.02, 1.20, 1.25), (0.03, 1.60, 0.85), (0.18, 2.00, 0.55), (0.43, 2.35, 0.40), (0.78, 2.70, 0.32),
             (1.23, 3.00, 0.27), (1.78, 3.23, 0.25), (2.58, 3.40, 0.24)],
}
LIP = {'front': (-2.05, 2.60), 'corner': (-1.44, 2.69), 'hinge_y': -1.22, 'hinge_z': 2.75, 'hw': 0.50, 'zmin': 2.30}
CRYSTAL_Y = [-1.62, -1.22, -0.84, -0.46, -0.08, 0.30, 0.68, 1.06]
FPS = 30
V_DESIGN = 2.6


def mir(p):
    return (-p[0], p[1], p[2])


# ------------------------------------------------------------------------------------------------ 0. today's adult footprint
def footprint_of_imported():
    """Width (x extent, m) of the Hero mesh without tail vertices: all, low (z < 1.2: fists and feet), feet only."""
    res = {}
    for o in bpy.context.scene.objects:
        if o.type != 'MESH' or not o.name.startswith('Kaiju_Hero'):
            continue
        dg = bpy.context.evaluated_depsgraph_get(); ev = o.evaluated_get(dg); me = ev.to_mesh()
        X = np.array([o.matrix_world @ v.co for v in me.vertices]); ev.to_mesh_clear()
        gi = {g.index: g.name for g in o.vertex_groups}
        dom = []
        for v in o.data.vertices:
            gs = sorted(v.groups, key=lambda g: -g.weight)
            dom.append(gi[gs[0].group] if gs else '')
        dom = np.array(dom)
        keep = np.array([not d.startswith('Tail') for d in dom])
        low = keep & (X[:, 2] < 1.2)
        feet = np.array([d.startswith('Foot') or d.startswith('Shin') or d.startswith('Toes') for d in dom])
        res = {'all': round(float(np.ptp(X[keep, 0])), 3), 'low': round(float(np.ptp(X[low, 0])), 3),
               'low_half_max': round(float(np.abs(X[low, 0] - np.median(X[keep, 0])).max()), 3),
               'feet': round(float(np.ptp(X[feet, 0])), 3) if feet.any() else None, 'height': round(float(np.ptp(X[:, 2])), 3)}
    return res


C.clear()
OLD_FBX = os.path.join(HERE, '..', '_fallback~', 'Kaiju_Adult.fbx')
REP['old_footprint'] = None
if os.path.exists(OLD_FBX):
    bpy.ops.import_scene.fbx(filepath=OLD_FBX, use_anim=True)
    for o in bpy.context.scene.objects:
        if o.type == 'ARMATURE':
            o.data.pose_position = 'REST'
    bpy.context.view_layer.update()
    REP['old_footprint'] = {'rest': footprint_of_imported()}
    for o in bpy.context.scene.objects:
        if o.type == 'ARMATURE':
            o.data.pose_position = 'POSE'
            idle = next((a for a in bpy.data.actions if a.name.endswith('Idle')), None)
            if idle and o.animation_data:
                o.animation_data.action = idle
                try:
                    o.animation_data.action_slot = idle.slots[0]
                except Exception:
                    pass
    bpy.context.scene.frame_set(1)
    REP['old_footprint']['idle'] = footprint_of_imported()
    log('old adult footprint', REP['old_footprint'])

# ------------------------------------------------------------------------------------------------ 1. import, recentre
C.clear()
sc = bpy.context.scene
sc.render.fps = FPS
obs = C.import_static()
for lv, o in obs.items():
    o.data.transform(Matrix.Translation(SHIFT))
    o.name = 'Kaiju_' + lv; o.data.name = 'Kaiju_' + lv
    o.data.update()
LV = ['Hero', 'LOD0', 'LOD1', 'Shadow']
REP['input_tris'] = {lv: int(len(C.tris(obs[lv]))) for lv in LV}
V0 = C.verts(obs['Hero'])
REP['recentred_bbox'] = [V0.min(0).round(3).tolist(), V0.max(0).round(3).tolist()]
_low = (V0[:, 2] < 1.2) & (V0[:, 1] < 1.0)
NEW_LOW_HALF = float(np.abs(V0[_low, 0]).max())
OLD_LOW_HALF = (REP['old_footprint'] or {}).get('idle', {}).get('low_half_max') or NEW_LOW_HALF
# keep the planted fists no wider than today's adult (bridges): pull them in by the difference (at most 0.7 m)
FIST_IN = max(0.0, min(0.7, NEW_LOW_HALF - OLD_LOW_HALF))
REP['footprint'] = {'new_rest_low_half': round(NEW_LOW_HALF, 3), 'old_idle_low_half': round(OLD_LOW_HALF, 3), 'fist_in': round(FIST_IN, 3)}
log('footprint', REP['footprint'])
log('imported', REP['input_tris'], 'bbox', REP['recentred_bbox'])

# ------------------------------------------------------------------------------------------------ 2. skeleton
arm_d = bpy.data.armatures.new('KaijuRig')
arm = bpy.data.objects.new('KaijuRig', arm_d)
sc.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
for o in sc.objects:
    o.select_set(o == arm)
bpy.ops.object.mode_set(mode='EDIT')
EB = arm_d.edit_bones
PARENT = {}


def bone(name, h, t, parent=None, connect=False, roll=(0, 0, 1)):
    b = EB.new(name)
    b.head = Vector(h); b.tail = Vector(t)
    d = (b.tail - b.head).normalized()
    r = Vector(roll)
    if abs(d.dot(r)) > 0.9:
        r = Vector((0, -1, 0)) if abs(d.dot(Vector((0, -1, 0)))) < 0.9 else Vector((1, 0, 0))
    b.align_roll(r)
    if parent:
        b.parent = EB[parent]; b.use_connect = connect
    b.use_deform = True
    PARENT[name] = parent
    return b


bone('Root', (0, 0, 0), (0, -0.6, 0))
bone('Hips', J['hips'], J['spine'], 'Root')
bone('Spine', J['spine'], J['spine2'], 'Hips', True)
bone('Spine2', J['spine2'], J['spine3'], 'Spine', True)
bone('Spine3', J['spine3'], J['chest'], 'Spine2', True)
bone('Chest', J['chest'], J['neck'], 'Spine3', True)
bone('Neck', J['neck'], J['head'], 'Chest', True)
bone('Head', J['head'], J['head_tip'], 'Neck', True)
bone('Jaw', J['jaw'], J['chin'], 'Head')
for i in range(len(J['tail']) - 1):
    bone(f'Tail{i + 1}', J['tail'][i], J['tail'][i + 1], 'Hips' if i == 0 else f'Tail{i}', i > 0)
for s, f in (('L', lambda p: p), ('R', mir)):
    bone(f'Shoulder_{s}', f(J['clav_L']), f(J['arm_L']), 'Chest')
    bone(f'Arm_{s}', f(J['arm_L']), f(J['elbow_L']), f'Shoulder_{s}', True)
    bone(f'Forearm_{s}', f(J['elbow_L']), f(J['wrist_L']), f'Arm_{s}', True)
    bone(f'Fist_{s}', f(J['wrist_L']), f(J['fist_L']), f'Forearm_{s}', True)
    for k, kp in enumerate(J['knuck_L']):
        bone(f'Knuckle{k + 1}_{s}', f(J['fist_L']), f(kp), f'Fist_{s}')
    bone(f'Thigh_{s}', f(J['thigh_L']), f(J['knee_L']), 'Hips')
    bone(f'Shin_{s}', f(J['knee_L']), f(J['ankle_L']), f'Thigh_{s}', True)
    bone(f'Foot_{s}', f(J['ankle_L']), f(J['ball_L']), f'Shin_{s}', True)
    bone(f'Toes_{s}', f(J['ball_L']), f(J['toe_L']), f'Foot_{s}', True)
bpy.ops.object.mode_set(mode='OBJECT')
arm_d.display_type = 'STICK'

# ------------------------------------------------------------------------------------------------ crystal analysis (Hero)


def code_of(col):
    return np.round(col[:, 3] * 8).astype(int)


def dorsal_profile(V, col):
    """Max z of non-glow (plate) vertices near the midline per 0.12 m y-bin."""
    lum = col[:, :3] @ np.array([0.3, 0.59, 0.11])
    m = (lum < 0.2) & (np.abs(V[:, 0]) < 0.7)
    bins = {}
    for y, z in zip(V[m, 1], V[m, 2]):
        k = int(math.floor(y / 0.12))
        bins[k] = max(bins.get(k, -1), z)
    return bins


def greenish(col):
    return (col[:, 1] > col[:, 0] * 1.15) & (col[:, 1] > col[:, 2] * 1.6) & (col[:, 1] > 0.12)


def plate_top(bins, y):
    k = int(math.floor(y / 0.12))
    vals = [bins[j] for j in (k - 1, k, k + 1) if j in bins]
    return max(vals) if vals else 0.0


hero = obs['Hero']
VH = C.verts(hero); CH = C.vcol(hero)
BINS = dorsal_profile(VH, CH)
codeH = code_of(CH)
CRYS = []      # (name, head, tail, parent)
for i, y in enumerate(CRYSTAL_Y):
    top = plate_top(BINS, y)
    m = greenish(CH) & (np.abs(VH[:, 0]) < 0.8) & (np.abs(VH[:, 1] - y) < 0.22) & (VH[:, 2] > top + 0.02)
    if m.sum() < 5:
        log('crystal cluster', i, 'empty at y', y); continue
    P = VH[m]
    tip = P[np.argmax(P[:, 2])]
    base = np.array([np.median(P[:, 0]), np.median(P[:, 1]), top])
    CRYS.append((f'Crystal{i + 1}', tuple(base), tuple(tip), int(m.sum())))
REP['crystals'] = [(n, np.round(h, 2).tolist(), np.round(t, 2).tolist(), c) for n, h, t, c in CRYS]
log('crystal clusters', REP['crystals'])
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
EB = arm_d.edit_bones
SPINE_CHAIN = ['Hips', 'Spine', 'Spine2', 'Spine3', 'Chest', 'Neck', 'Head']


def nearest_spine(p):
    best, bn = 1e9, 'Chest'
    for n in SPINE_CHAIN:
        b = EB[n]
        d = seg_dist_v(Vector(p), b.head, b.tail)
        if d < best:
            best, bn = d, n
    return bn


def seg_dist_v(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
    return (p - (a + ab * t)).length


for n, h, t, c in CRYS:
    bone(n, h, t, nearest_spine(h))
bpy.ops.object.mode_set(mode='OBJECT')
BONES = [b.name for b in arm_d.bones]
REP['bones'] = BONES
REP['bone_count'] = len(BONES)
log('bones', len(BONES))

# ------------------------------------------------------------------------------------------------ 3. mouth cut


def lip_z(y):
    (yf, zf), (yc, zc) = LIP['front'], LIP['corner']
    if y <= yc:
        return zf + (zc - zf) * (y - yf) / (yc - yf)
    return zc + (LIP['hinge_z'] - zc) * min(1.0, (y - yc) / (LIP['hinge_y'] - yc))


def in_jaw(p):
    return abs(p[0]) < LIP['hw'] and p[1] < LIP['hinge_y'] and LIP['zmin'] < p[2] < lip_z(p[1])


def cut_mouth(ob):
    bm = bmesh.new(); bm.from_mesh(ob.data)
    bm.faces.ensure_lookup_table()
    jaw_face = {f.index: in_jaw(f.calc_center_median()) for f in bm.faces}
    rip = []
    for e in bm.edges:
        if len(e.link_faces) != 2:
            continue
        a, b = e.link_faces
        if jaw_face[a.index] == jaw_face[b.index]:
            continue
        mid = (e.verts[0].co + e.verts[1].co) / 2
        if mid.y < LIP['corner'][0] + 0.03 and mid.z > LIP['zmin'] + 0.12 and abs(mid.z - lip_z(mid.y)) < 0.12:
            rip.append(e)
    jaw_faces = [f for f in bm.faces if jaw_face[f.index]]
    upper_rim = set(v for e in rip for v in e.verts)
    bmesh.ops.split_edges(bm, edges=rip)
    bm.verts.ensure_lookup_table()
    jaw_verts = set(v.index for f in jaw_faces for v in f.verts)
    # rim vertices after the split: upper side = rim verts not in any jaw face; lower side = rim verts in jaw faces only
    rim_up, rim_lo = [], []
    for v in bm.verts:
        if not v.is_boundary:
            continue
        fl = [jaw_face.get(f.index, False) for f in v.link_faces]
        if all(fl):
            rim_lo.append(v.co.copy())
        elif not any(fl):
            rim_up.append(v.co.copy())
    bm.to_mesh(ob.data); bm.free(); ob.data.update()
    return {'rip_edges': len(rip), 'jaw_faces': len(jaw_faces), 'rim_up': rim_up, 'rim_lo': rim_lo}


MOUTH = {}
for lv in ('Hero', 'LOD0'):
    MOUTH[lv] = cut_mouth(obs[lv])
    log('mouth', lv, 'rip edges', MOUTH[lv]['rip_edges'], 'jaw faces', MOUTH[lv]['jaw_faces'],
        'rim up/lo', len(MOUTH[lv]['rim_up']), len(MOUTH[lv]['rim_lo']))
REP['mouth'] = {lv: {k: v for k, v in m.items() if k in ('rip_edges', 'jaw_faces')} | {'rim_up': len(m['rim_up']), 'rim_lo': len(m['rim_lo'])}
                for lv, m in MOUTH.items()}

# ------------------------------------------------------------------------------------------------ 4. weights
DEF = BONES[:]
BI = {n: i for i, n in enumerate(DEF)}
REST = {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in arm_d.bones}
PART = {}
for n in DEF:
    if n.endswith('_L') and any(n.startswith(p) for p in ('Shoulder', 'Arm', 'Forearm', 'Fist', 'Knuckle')):
        PART[n] = 'armL'
    elif n.endswith('_R') and any(n.startswith(p) for p in ('Shoulder', 'Arm', 'Forearm', 'Fist', 'Knuckle')):
        PART[n] = 'armR'
    elif n.endswith('_L'):
        PART[n] = 'legL'
    elif n.endswith('_R'):
        PART[n] = 'legR'
    elif n.startswith('Tail'):
        PART[n] = 'tail'
    elif n.startswith('Crystal'):
        PART[n] = 'crystal'
    else:
        PART[n] = 'axial'
ALLOW = {
    'armL': {'armL'}, 'armR': {'armR'}, 'legL': {'legL'}, 'legR': {'legR'}, 'tail': {'tail'},
    'axial': {'axial'},
}
ATTACH = {'armL': ['Chest', 'Spine3'], 'armR': ['Chest', 'Spine3'], 'legL': ['Hips', 'Spine'], 'legR': ['Hips', 'Spine'],
          'tail': ['Hips'], 'axial': ['Shoulder_L', 'Shoulder_R', 'Arm_L', 'Arm_R', 'Thigh_L', 'Thigh_R', 'Tail1']}
SEG_BONES = [n for n in DEF if n != 'Root' and not n.startswith('Crystal') and not n.startswith('Knuckle')]


def seg_dists(P, names):
    D = np.zeros((len(P), len(names)))
    for j, n in enumerate(names):
        a = np.array(REST[n][0]); b = np.array(REST[n][1]); ab = b - a
        t = np.clip(((P - a) @ ab) / max(ab @ ab, 1e-9), 0, 1)
        D[:, j] = np.linalg.norm(P - (a + t[:, None] * ab), axis=1)
    return D


def mesh_adj(ob):
    me = ob.data
    e = np.empty(len(me.edges) * 2, dtype=np.int64); me.edges.foreach_get('vertices', e)
    return e.reshape(-1, 2)


def smooth_weights(W, E, fixed, iters=4, lam=0.5):
    nv = len(W)
    for _ in range(iters):
        acc = np.zeros_like(W); cnt = np.zeros(nv)
        np.add.at(acc, E[:, 0], W[E[:, 1]]); np.add.at(acc, E[:, 1], W[E[:, 0]])
        np.add.at(cnt, E[:, 0], 1); np.add.at(cnt, E[:, 1], 1)
        avg = acc / np.maximum(cnt, 1)[:, None]
        Wn = W * (1 - lam) + avg * lam
        Wn[fixed] = W[fixed]
        Wn[cnt == 0] = W[cnt == 0]
        W = Wn
    return W


def limit4(W):
    idx = np.argsort(-W, axis=1)[:, 4:]
    np.put_along_axis(W, idx, 0, axis=1)
    W[W < 0.01] = 0
    s = W.sum(1, keepdims=True)
    return W / np.maximum(s, 1e-9)


def dark_components(ob, col):
    """Connected dark-rock faces (plates), as vertex index lists."""
    me = ob.data
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    lum = col[:, :3] @ np.array([0.3, 0.59, 0.11])
    code = code_of(col)
    dark = np.array([all(lum[v.index] < 0.2 and code[v.index] != 6 for v in f.verts) for f in bm.faces])
    seen = np.zeros(len(bm.faces), bool); comps = []
    for i in range(len(bm.faces)):
        if not dark[i] or seen[i]:
            continue
        st = [i]; seen[i] = True; vs = set()
        while st:
            k = st.pop(); f = bm.faces[k]
            vs.update(v.index for v in f.verts)
            for e in f.edges:
                for g in e.link_faces:
                    if dark[g.index] and not seen[g.index]:
                        seen[g.index] = True; st.append(g.index)
        comps.append(sorted(vs))
    bm.free()
    return comps


# proxy: watertight voxel rebuild of the Hero shell, heat weights on it
proxy = hero.copy(); proxy.data = hero.data.copy(); proxy.name = 'Proxy'
sc.collection.objects.link(proxy)
for m in list(proxy.modifiers):
    proxy.modifiers.remove(m)
proxy.data.remesh_voxel_size = 0.06
with bpy.context.temp_override(active_object=proxy, object=proxy, selected_objects=[proxy], selected_editable_objects=[proxy]):
    bpy.ops.object.voxel_remesh()
log('proxy verts', len(proxy.data.vertices))
for n in ('Root',) + tuple(n for n in DEF if n.startswith('Knuckle') or n.startswith('Crystal')):
    arm_d.bones[n].use_deform = False
for o in sc.objects:
    o.select_set(False)
proxy.select_set(True); arm.select_set(True)
bpy.context.view_layer.objects.active = arm
heat_ok = True
try:
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
except Exception as ex:
    heat_ok = False; log('heat failed', ex)
for n in DEF:
    arm_d.bones[n].use_deform = True
PV = C.verts(proxy)
PW = np.zeros((len(PV), len(DEF)))
gi = {g.index: g.name for g in proxy.vertex_groups}
for v in proxy.data.vertices:
    for g in v.groups:
        nm = gi[g.group]
        if nm in BI:
            PW[v.index, BI[nm]] = g.weight
empty = PW.sum(1) < 1e-6
REP['proxy'] = {'verts': len(PV), 'heat_ok': heat_ok, 'unweighted': int(empty.sum())}
if empty.any():
    # distance fallback for anything heat left empty
    D = seg_dists(PV[empty], SEG_BONES)
    w = 1.0 / np.maximum(D, 0.03) ** 4
    w /= w.sum(1, keepdims=True)
    for j, n in enumerate(SEG_BONES):
        PW[np.where(empty)[0], BI[n]] = w[:, j]
log('proxy weights', REP['proxy'])
PKD = None
from mathutils.kdtree import KDTree
kd = KDTree(len(PV))
for i, p in enumerate(PV):
    kd.insert(p, i)
kd.balance()


def weights_for(ob, lv):
    V = C.verts(ob); col = C.vcol(ob); code = code_of(col)
    nv = len(V)
    # transfer: inverse-distance blend of the 4 nearest proxy vertices
    W = np.zeros((nv, len(DEF)))
    for i, p in enumerate(V):
        res = kd.find_n(p, 4)
        ws = np.array([1.0 / max(d, 1e-3) ** 2 for _, _, d in res])
        ws /= ws.sum()
        for (co, j, d), w in zip(res, ws):
            W[i] += w * PW[j]
    # limb masking by nearest bone segment (axial bones favoured for ambiguous vertices)
    D = seg_dists(V, SEG_BONES)
    bias = np.array([0.85 if PART[n] == 'axial' else 1.0 for n in SEG_BONES])
    near = np.array(SEG_BONES)[np.argmin(D * bias, axis=1)]
    part = np.array([PART[n] for n in near])
    for p in ('armL', 'armR', 'legL', 'legR', 'tail', 'axial'):
        rows = np.where(part == p)[0]
        if not len(rows):
            continue
        keep = np.array([PART[n] == p or n in ATTACH[p] for n in DEF])
        W[np.ix_(rows, np.where(~keep)[0])] = 0
    fixed = np.zeros(nv, bool)
    # rigid head (not the jaw), with the neck blend kept within 0.18 m of the head pivot
    hp = np.array(J['head'])
    headrows = (near == 'Head') | (near == 'Jaw')
    jawm = np.array([in_jaw(p) for p in V])
    if lv in MOUTH:
        # after the split, jaw-side vertices are exactly the ones whose faces are jaw faces: use the face test
        me = ob.data
        fj = np.zeros(nv, bool); fu = np.zeros(nv, bool)
        for f in me.polygons:
            c = Vector(f.center)
            if in_jaw(c):
                fj[list(f.vertices)] = True
            else:
                fu[list(f.vertices)] = True
        jawm = fj & ~fu | (fj & jawm)
    far = np.linalg.norm(V - hp, axis=1) > 0.18
    rh = headrows & far & ~jawm
    W[rh] = 0; W[rh, BI['Head']] = 1; fixed |= rh
    # jaw: rigid in front of the mouth corner, ramp to the head/neck behind it and at the throat
    yc = LIP['corner'][0]; yh = LIP['hinge_y']
    ramp_y = np.clip((yh + 0.08 - V[:, 1]) / (yh + 0.08 - yc), 0, 1)
    ramp_z = np.clip((V[:, 2] - LIP['zmin']) / 0.12, 0, 1)
    jw = np.where(jawm, ramp_y * ramp_z, 0.0)
    W[jawm] *= (1 - jw[jawm])[:, None]
    W[jawm, BI['Jaw']] += jw[jawm]
    fixed |= jawm & (jw > 0.99)
    # crystals: rigid to their cluster bone (back) or to the dominant tail bone (tail)
    nC = 0
    bins = dorsal_profile(V, col)
    cr_names = [c[0] for c in CRYS]
    cr_y = np.array([REST[n][0][1] for n in cr_names]) if cr_names else np.zeros(0)
    gr = greenish(col)
    for i in np.where(gr)[0]:
        x, y, z = V[i]
        if abs(x) < 0.8 and len(cr_y) and y < 1.3 and z > plate_top(bins, y) + 0.02 and not jawm[i]:
            k = int(np.argmin(np.abs(cr_y - y)))
            W[i] = 0; W[i, BI[cr_names[k]]] = 1; fixed[i] = True; nC += 1
    tail_cols = [BI[n] for n in DEF if PART[n] == 'tail']
    tailrows = np.where((part == 'tail') & gr & (V[:, 2] > 0.35))[0]
    for i in tailrows:
        j = tail_cols[int(np.argmax(W[i, tail_cols]))]
        W[i] = 0; W[i, j] = 1; fixed[i] = True
    # rock plates: rigid to their dominant bone when one bone already holds most of the plate
    nP = 0; nPv = 0
    for comp in dark_components(ob, col):
        comp = np.array(comp)
        if len(comp) < 4:
            continue
        ext = np.ptp(V[comp], axis=0).max()
        if ext > 1.1:
            continue
        s = W[comp].sum(0); tot = s.sum()
        if tot <= 0:
            continue
        j = int(np.argmax(s))
        if s[j] / tot < 0.6 or DEF[j] in ('Jaw',):
            continue
        free = comp[~fixed[comp]]
        W[free] = 0; W[free, j] = 1; fixed[free] = True
        nP += 1; nPv += len(free)
    # knuckle chunks: the fist's front-bottom lumps follow their knuckle bone (70 % of the fist weight)
    for s in 'LR':
        names = [f'Fist_{s}'] + [f'Knuckle{k}_{s}' for k in (1, 2, 3)]
        nn = np.argmin(seg_dists(V, names), axis=1)
        fc = np.array(REST[f'Fist_{s}'][1])
        sel = (part == f'arm{s}') & (nn > 0) & (np.linalg.norm(V - fc, axis=1) > 0.18) & (W[:, BI[f'Fist_{s}']] > 0.5)
        for k in (1, 2, 3):
            rows = np.where(sel & (nn == k))[0]
            mv = W[rows, BI[f'Fist_{s}']] * 0.7
            W[rows, BI[f'Fist_{s}']] -= mv; W[rows, BI[f'Knuckle{k}_{s}']] += mv
    E = mesh_adj(ob)
    W = smooth_weights(W, E, fixed, iters=6 if lv in ('Hero', 'LOD0') else 3)
    W = limit4(W)
    # write groups
    for g in list(ob.vertex_groups):
        ob.vertex_groups.remove(g)
    for j, n in enumerate(DEF):
        g = ob.vertex_groups.new(name=n)
        rows = np.where(W[:, j] > 0)[0]
        for r in rows:
            g.add([int(r)], float(W[r, j]), 'REPLACE')
    st = {'verts': nv, 'rigid_head': int(rh.sum()), 'jaw': int(jawm.sum()), 'crystal_verts': nC, 'tail_crystal_verts': len(tailrows),
          'rigid_plates': nP, 'plate_verts': nPv, 'max_infl': int((W > 0).sum(1).max()), 'unweighted': int((W.sum(1) < 0.99).sum())}
    log('weights', lv, st)
    return W, st


WEIGHTS = {}
REP['weights'] = {}
for lv in LV:
    WEIGHTS[lv], REP['weights'][lv] = weights_for(obs[lv], lv)
bpy.data.objects.remove(proxy, do_unlink=True)
save()

# ------------------------------------------------------------------------------------------------ mouth pouch, teeth, eyes


def set_col(ob, rgba):
    me = ob.data
    for a in list(me.color_attributes):
        me.color_attributes.remove(a)
    a = me.color_attributes.new('Col', 'BYTE_COLOR', 'CORNER')
    a.data.foreach_set('color', np.tile(np.array(rgba, np.float32), len(a.data)))
    me.color_attributes.active_color_name = 'Col'


def hexc(h, code):
    h = h.lstrip('#')
    srgb = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    lin = [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in srgb]
    return lin + [code / 8.0]


def part_obj(name, bm, rgba, groups):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); sc.collection.objects.link(ob)
    set_col(ob, rgba)
    V = C.verts(ob)
    for n in DEF:
        ob.vertex_groups.new(name=n)
    for i, p in enumerate(V):
        for n, w in groups(p):
            if w > 0:
                ob.vertex_groups[n].add([i], float(w), 'REPLACE')
    if ob.data.uv_layers:
        pass
    ob.data.uv_layers.new(name='UVMap')
    return ob


def jaw_split_weight(p):
    """Pouch: upper half on the head, lower half on the jaw, a soft band between."""
    z0 = lip_z(p[1])
    w = min(1.0, max(0.0, (z0 - p[2]) / 0.06 + 0.5))
    return [('Jaw', w), ('Head', 1 - w)]


def build_extras(lv):
    ob = obs[lv]
    m = MOUTH[lv]
    bvh = BVHTree.FromObject(ob, bpy.context.evaluated_depsgraph_get())
    parts = []
    # pouch: rear half of an ellipsoid behind the lips, normals inward (seen from the open mouth)
    (yf, zf), (yc, zc) = LIP['front'], LIP['corner']
    cy = (yf + yc) / 2 + 0.12; cz = (zf + zc) / 2 - 0.02
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=14, v_segments=8, radius=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * (LIP['hw'] * 0.72), v.co.y * ((yc - yf) / 2 + 0.16), v.co.z * 0.11)) + Vector((0, cy, cz))
    kill = [v for v in bm.verts if v.co.y < cy - ((yc - yf) / 2 + 0.16) * 0.55]
    bmesh.ops.delete(bm, geom=kill, context='VERTS')
    for v in bm.verts:     # keep inside the head shell
        loc, nrm, idx, d = bvh.find_nearest(v.co)
        if loc is not None and (v.co - loc).dot(nrm) > -0.012:
            v.co = loc - nrm * 0.02
    bmesh.ops.reverse_faces(bm, faces=bm.faces)
    parts.append(part_obj(f'Pouch_{lv}', bm, hexc('#2A1012', 0), jaw_split_weight))
    # teeth: short cones just behind the rims (upper on the head pointing down, lower on the jaw pointing up)
    def teeth(rim, down, bone_name, fracs, rad, dep):
        if len(rim) < 3:
            return None
        R = sorted(rim, key=lambda c: math.atan2(c.x, -(c.y - LIP['hinge_y'])))
        pick = [R[int(round(f * (len(R) - 1)))] for f in fracs]
        bm = bmesh.new()
        for c in pick:
            inward = Vector((-c.x, LIP['hinge_y'] - c.y, 0)); inward.z = 0
            inward = inward.normalized() * 0.035 if inward.length > 1e-6 else Vector()
            base = c + inward
            r = bmesh.ops.create_cone(bm, cap_ends=True, segments=5, radius1=rad, radius2=rad * 0.45, depth=dep)
            for v in r['verts']:
                z = (-v.co.z - dep / 2) if down else (v.co.z + dep / 2)
                v.co = Vector((v.co.x, v.co.y, z)) + base
        return part_obj(f'Teeth_{bone_name}_{lv}', bm, hexc('#DCD3B8', 0), lambda p: [(bone_name, 1.0)])
    # goofy, not predatory: four stubby upper nubs and two little lower tusks (cartoon direction, art lead 2026-11-14)
    t1 = teeth(m['rim_up'], True, 'Head', (0.22, 0.4, 0.6, 0.78), 0.042, 0.06)
    t2 = teeth(m['rim_lo'], False, 'Jaw', (0.3, 0.7), 0.05, 0.09)
    parts += [t for t in (t1, t2) if t]
    # eyes: two small glow gems set into the brow shadow (glow code 6 = allegiance colour; magenta when berserk)
    bm = bmesh.new()
    for sx in (1, -1):
        guess = Vector((0.28 * sx, -1.70, 2.88))
        loc, nrm, idx, d = bvh.find_nearest(guess)
        r = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.085)
        for v in r['verts']:
            v.co = Vector((v.co.x, v.co.y * 0.6, v.co.z * 0.8)) + loc + nrm * 0.01
    parts.append(part_obj(f'Eyes_{lv}', bm, hexc('#C9F632', 6), lambda p: [('Head', 1.0)]))
    if lv == 'Hero':
        # a 1 mm triangle on the ground under the belly, weighted to Root, so every exported bone is a skin bone
        bm = bmesh.new()
        vs = [bm.verts.new(c) for c in ((0, 0, 0.001), (0.001, 0, 0.001), (0, 0.001, 0.001))]
        bm.faces.new(vs)
        parts.append(part_obj('RootAnchor', bm, hexc('#2A1012', 0), lambda p: [('Root', 1.0)]))
    # join into the level mesh
    with bpy.context.temp_override(active_object=ob, object=ob, selected_objects=[ob] + parts, selected_editable_objects=[ob] + parts):
        bpy.ops.object.join()
    return len(parts)


SHELL_N = {lv: len(obs[lv].data.vertices) for lv in LV}
for lv in ('Hero', 'LOD0'):
    n = build_extras(lv)
    log('extras', lv, n, 'tris now', len(C.tris(obs[lv])))
REP['tris'] = {lv: int(len(C.tris(obs[lv]))) for lv in LV}
for lv in LV:
    ob = obs[lv]
    ob.parent = arm
    md = ob.modifiers.new('Armature', 'ARMATURE'); md.object = arm
save()

# ------------------------------------------------------------------------------------------------ 5. pose maths (own FK + IK)
AB = arm_d.bones
MR = {b.name: b.matrix_local.copy() for b in AB}
RREL = {}
for b in AB:
    RREL[b.name] = (MR[b.parent.name].inverted() @ MR[b.name]) if b.parent else MR[b.name].copy()
ORDER = []


def _walk(b):
    ORDER.append(b.name)
    for c in b.children:
        _walk(c)


for b in AB:
    if b.parent is None:
        _walk(b)
LEN = {b.name: b.length for b in AB}


def arot(name, x=0, y=0, z=0):
    """Rotation in armature axes (degrees: x pitch, + = nose/limb tip down-forward; y roll, + = left side down; z yaw,
    + = turn to the character's left) at the bone's rest frame, as a basis quaternion."""
    R = Euler((math.radians(x), math.radians(y), math.radians(z)), 'XYZ').to_matrix()
    Rr = MR[name].to_3x3()
    return (Rr.inverted() @ R @ Rr).to_quaternion()


class Pose:
    def __init__(self):
        self.q = {}           # bone -> basis quaternion
        self.loc = {}         # bone -> basis location (armature axes delta, converted below)
        self.ik = {}          # chain -> (target armature-space Vector, pole Vector)
        self.end = {}         # end bone -> extra world rotation (Euler deg in armature axes) applied to its rest orientation
        self.aim = {}         # bone -> world direction (leaf bones: crystals)


CHAINS = {'arm_L': ('Arm_L', 'Forearm_L', 'Fist_L'), 'arm_R': ('Arm_R', 'Forearm_R', 'Fist_R'),
          'leg_L': ('Thigh_L', 'Shin_L', 'Foot_L'), 'leg_R': ('Thigh_R', 'Shin_R', 'Foot_R')}
UPPER = {v[0]: k for k, v in CHAINS.items()}
LOWER = {v[1]: k for k, v in CHAINS.items()}
ENDB = {v[2]: k for k, v in CHAINS.items()}
# leg IK targets the ANKLE (shin tail); the planted point (heel) is below it: offset in rest
ANKLE_OFF = {s: Vector(J['ankle_L'] if s == 'L' else mir(J['ankle_L'])) - Vector(J['heel_L'] if s == 'L' else mir(J['heel_L'])) for s in 'LR'}
WRIST_OFF = {s: Vector(J['wrist_L'] if s == 'L' else mir(J['wrist_L'])) - Vector(J['knuck_L'][1] if s == 'L' else mir(J['knuck_L'][1])) for s in 'LR'}


def basis_mat(p, n):
    q = p.q.get(n, Quaternion())
    M = q.to_matrix().to_4x4()
    if n in p.loc:
        l = MR[n].to_3x3().inverted() @ Vector(p.loc[n])
        M = Matrix.Translation(l) @ M
    return M


def swing_basis(Mpar_rel, want_dir):
    R0 = Mpar_rel.to_3x3()
    y0 = (R0 @ Vector((0, 1, 0))).normalized()
    q = y0.rotation_difference(want_dir.normalized())
    Rw = q.to_matrix() @ R0
    return (R0.inverted() @ Rw).to_quaternion()


def fk(p):
    """Pose-space matrices of every bone for a Pose (IK and aims solved in hierarchy order). Writes the solved basis
    quaternions back into p.q so the bake writes exactly what was evaluated."""
    M = {}
    for n in ORDER:
        par = PARENT.get(n)
        base = (M[par] @ RREL[n]) if par else RREL[n]
        if n in UPPER and UPPER[n] in p.ik:
            ch = CHAINS[UPPER[n]]
            T, pole = p.ik[UPPER[n]]
            S = base.translation
            a, b = LEN[ch[0]], LEN[ch[1]]
            d = T - S
            L = max(abs(a - b) + 1e-3, min(a + b - 1e-3, d.length))
            u = d.normalized()
            pv = (pole - S); pv = (pv - u * pv.dot(u)).normalized()
            ca = (a * a + L * L - b * b) / (2 * a * L)
            E = S + u * (a * ca) + pv * (a * math.sqrt(max(0.0, 1 - ca * ca)))
            p.q[n] = swing_basis(base, E - S)
            p.ik_elbow = getattr(p, 'ik_elbow', {}); p.ik_elbow[UPPER[n]] = (E, S + u * L)
        elif n in LOWER and LOWER[n] in p.ik:
            E, Tc = p.ik_elbow[LOWER[n]]
            p.q[n] = swing_basis(base, Tc - base.translation)
        elif n in ENDB and ENDB[n] in p.ik:
            ex = p.end.get(n, (0, 0, 0))
            Rw = Euler([math.radians(v) for v in ex], 'XYZ').to_matrix() @ MR[n].to_3x3()
            p.q[n] = (base.to_3x3().inverted() @ Rw).to_quaternion()
        elif n in p.aim:
            p.q[n] = swing_basis(base, p.aim[n])
        M[n] = base @ basis_mat(p, n)
    return M


# ------------------------------------------------------------------------------------------------ clip authoring helpers


def ease(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def ease_in(t):
    t = max(0.0, min(1.0, t)); return t * t * t


def ease_out(t):
    t = max(0.0, min(1.0, t)); return 1 - (1 - t) ** 3


def lerp(a, b, t):
    return a + (b - a) * t


def vlerp(a, b, t):
    return Vector(a).lerp(Vector(b), t)


def keyed(keys, t, fn=ease):
    """keys: [(time, value)], piecewise eased interpolation (values float or tuple)."""
    if t <= keys[0][0]:
        return keys[0][1]
    for (t0, v0), (t1, v1) in zip(keys, keys[1:]):
        if t <= t1:
            u = fn((t - t0) / max(t1 - t0, 1e-9))
            if isinstance(v0, (tuple, list)):
                return tuple(lerp(a, b, u) for a, b in zip(v0, v1))
            return lerp(v0, v1, u)
    return keys[-1][1]


HEEL = {s: Vector(J['heel_L'] if s == 'L' else mir(J['heel_L'])) for s in 'LR'}
KNU = {s: Vector(J['knuck_L'][1] if s == 'L' else mir(J['knuck_L'][1])) for s in 'LR'}
for _s in 'LR':     # planted fists pulled in to today's footprint (bridges); the bind pose keeps the art's width
    KNU[_s].x -= FIST_IN * (1 if _s == 'L' else -1)
FIST_PLANT_Z = KNU['L'].z - 0.12


def plant_legs(p, hl=None, hr=None):
    for s, h in (('L', hl), ('R', hr)):
        c = Vector(h) if h is not None else HEEL[s]
        p.ik[f'leg_{s}'] = (c + ANKLE_OFF[s], c + ANKLE_OFF[s] + Vector((0, -2.0, 0.6)))


def plant_arms(p, kl=None, kr=None, pole_back=2.0):
    for s, k in (('L', kl), ('R', kr)):
        c = Vector(k) if k is not None else Vector((KNU[s].x, KNU[s].y, FIST_PLANT_Z))
        sx = 1 if s == 'L' else -1
        p.ik[f'arm_{s}'] = (c + WRIST_OFF[s], c + WRIST_OFF[s] + Vector((0.8 * sx, pole_back, 0.8)))


def spine_bend(p, x=0, y=0, z=0, w=(0.2, 0.25, 0.3, 0.25)):
    for n, k in zip(('Spine', 'Spine2', 'Spine3', 'Chest'), w):
        p.q[n] = arot(n, x * k, y * k, z * k)


TAILS = [n for n in BONES if n.startswith('Tail')]


def tail_wave(p, t, amp_yaw, amp_pitch, period, lag=0.12, base_yaw=0.0, base_pitch=0.0):
    for i, n in enumerate(TAILS):
        k = (i + 1) / len(TAILS)
        ph = 2 * math.pi * (t / period - lag * i)
        p.q[n] = arot(n, base_pitch + amp_pitch * math.sin(ph + 1.2) * (0.5 + k), 0, base_yaw * (0.4 + k) + amp_yaw * math.sin(ph) * (0.4 + k))


CRY = [c[0] for c in CRYS]


def crystal_springs(poses, loop, k=380.0, zeta=0.22, max_deg=14.0, gain=1.0):
    """Bake spring wobble: each crystal tip follows its rigid position through a damped spring (3 loops for cycles)."""
    n = len(poses)
    rigid = {c: [] for c in CRY}
    heads = {c: [] for c in CRY}
    for p in poses:
        M = fk(p)
        for c in CRY:
            heads[c].append(M[c].translation.copy())
            rigid[c].append((M[c] @ Vector((0, LEN[c], 0))).copy())
    dt = 1.0 / FPS; sub = 8; h = dt / sub
    cdamp = 2 * zeta * math.sqrt(k)
    for c in CRY:
        x = rigid[c][0].copy(); v = Vector()
        out = [None] * n
        for rep in range(3 if loop else 1):
            for f in range(n):
                tgt = rigid[c][f]
                for _ in range(sub):
                    a = (tgt - x) * k - v * cdamp
                    v += a * h; x += v * h
                out[f] = x.copy()
        for f, p in enumerate(poses):
            hd = heads[c][f]
            want = rigid[c][f] + (out[f] - rigid[c][f]) * gain - hd
            rest_dir = rigid[c][f] - hd
            ang = rest_dir.angle(want) if want.length > 1e-6 else 0
            if ang > math.radians(max_deg):
                axis = rest_dir.cross(want).normalized()
                want = Quaternion(axis, math.radians(max_deg)) @ rest_dir
            p.aim[c] = want


# ------------------------------------------------------------------------------------------------ the clips
CLIPS = {}


def clip(name, frames, loop=False):
    def deco(fn):
        CLIPS[name] = (frames, loop, fn)
        return fn
    return deco


def base_pose():
    p = Pose(); plant_legs(p); plant_arms(p)
    return p


@clip('Idle', 90, loop=True)
def idle(t, T):
    p = base_pose()
    # slightly dopey: big slow breaths, a lazy head tilt from side to side, mouth hanging a little open
    b = math.sin(2 * math.pi * t / T * 2)            # two breaths per loop
    w = 2 * math.pi * t / T
    p.loc['Hips'] = (0, 0, 0.05 * b)
    spine_bend(p, x=-4.0 * b)
    look = 10 * math.sin(w)
    tilt = 8 * math.sin(w + 0.9)
    p.q['Neck'] = arot('Neck', 3 * b, 0, look * 0.4)
    p.q['Head'] = arot('Head', -2.5 * b, tilt, look * 0.6)
    p.q['Jaw'] = arot('Jaw', 6.0 + 3.0 * (b * 0.5 + 0.5))
    tail_wave(p, t, 5, 2, T / 2)
    for s in 'LR':
        p.q[f'Shoulder_{s}'] = arot(f'Shoulder_{s}', 0, (1 if s == 'L' else -1) * 1.5 * b, 0)
    return p


WALK_N = 30
WALK_T = WALK_N / FPS
DUTY = 0.6
STRIDE = V_DESIGN * DUTY * WALK_T
PHASE = {'leg_L': 0.0, 'arm_L': 0.25, 'leg_R': 0.5, 'arm_R': 0.75}


def gait(u, lift):
    """u in [0,1): stance [0, DUTY) moves the contact back at V_DESIGN; swing lifts it forward."""
    if u < DUTY:
        return -STRIDE / 2 + STRIDE * (u / DUTY), 0.0, True
    s = (u - DUTY) / (1 - DUTY)
    e = 0.5 - 0.5 * math.cos(math.pi * s)
    return STRIDE / 2 - STRIDE * e, lift * math.sin(math.pi * min(1.0, s * 1.1)) ** 0.8, False


@clip('Walk', WALK_N, loop=True)
def walk(t, T):
    p = Pose()
    ph = t / T
    for ch, off in PHASE.items():
        s = ch[-1]
        u = (ph - off) % 1.0
        if ch.startswith('leg'):
            dy, dz, st = gait(u, 0.32)
            c = HEEL[s] + Vector((0, dy - 0.05, dz))
            p.ik[ch] = (c + ANKLE_OFF[s], c + ANKLE_OFF[s] + Vector((0, -2.0, 0.6)))
            # foot level when planted, toe-down peel in swing
            p.end[f'Foot_{s}'] = (0 if st else 18 * math.sin(math.pi * (u - DUTY) / (1 - DUTY)), 0, 0)
        else:
            dy, dz, st = gait(u, 0.45)
            c = Vector((KNU[s].x, KNU[s].y + dy + 0.1, FIST_PLANT_Z + dz))
            sx = 1 if s == 'L' else -1
            p.ik[ch] = (c + WRIST_OFF[s], c + WRIST_OFF[s] + Vector((0.8 * sx, 2.0, 0.8)))
            p.end[f'Fist_{s}'] = (0 if st else -20 * math.sin(math.pi * (u - DUTY) / (1 - DUTY)), 0, 0)
    w2 = 2 * math.pi * ph
    # body: drop on each hind plant, weight over the stance side, hips yaw with the hind legs, chest counter-yaw
    # bouncy, waddling weight (cartoon direction): a deep drop on each hind plant, a big roll over the stance side
    bob = -0.12 * (0.5 + 0.5 * math.cos(2 * w2 - 0.6))
    side = math.sin(w2 + 0.4)
    p.loc['Hips'] = (0.09 * side, 0, bob)
    p.q['Hips'] = arot('Hips', 2.0 * math.cos(2 * w2), -5.0 * side, 5.0 * math.sin(w2))
    spine_bend(p, x=2.0 * math.cos(2 * w2 + 0.8), y=2.0 * side, z=-7.0 * math.sin(w2 + 0.3))
    for s in 'LR':
        sx = 1 if s == 'L' else -1
        p.q[f'Shoulder_{s}'] = arot(f'Shoulder_{s}', 0, 0, 3.0 * math.sin(w2 + (0 if s == 'L' else math.pi)))
    # head steadied: counter the chest yaw and the bob
    p.q['Neck'] = arot('Neck', 3.0 * math.cos(2 * w2 - 1.6), 0, 2.5 * math.sin(w2 + 0.3))
    p.q['Head'] = arot('Head', 3.0 * math.cos(2 * w2 - 2.2), 3.0 * side, 1.5 * math.sin(w2 + 0.3))   # head bobs a beat late
    p.q['Jaw'] = arot('Jaw', 6.0 + 3.0 * (0.5 + 0.5 * math.cos(2 * w2 - 2.6)))                       # jaw flaps on the plants
    # tail counter-sways the hips yaw, with lag down the chain
    tail_wave(p, t, 7.0, 3.0, T, lag=0.1)
    return p


SQ_FOOT_X, SQ_FIST_X, SQ_FIST_Y = 0.42, 0.50, -1.30


@clip('WalkSqueeze', WALK_N, loop=True)
def walk_squeeze(t, T):
    """Bridge walk (blended over Walk by KaijuAnimator.Squeeze, feat/kaiju-bridges-1): same cycle and stride as Walk so the
    two stay in step, but tiptoeing along a plank: fists tucked in front of the chest, elbows in, feet narrowed under the
    body, body lowered, small careful steps up on the toes, head peering down, tail held straight out behind."""
    p = Pose()
    ph = t / T
    for ch, off in PHASE.items():
        s = ch[-1]; sx = 1 if s == 'L' else -1
        u = (ph - off) % 1.0
        if ch.startswith('leg'):
            dy, dz, st = gait(u, 0.2)
            c = Vector((SQ_FOOT_X * sx, HEEL[s].y + dy - 0.05, HEEL[s].z + dz + 0.06))
            p.ik[ch] = (c, c + Vector((0.4 * sx, -2.0, 0.6)))
            p.end[f'Foot_{s}'] = (16 if st else 16 + 14 * math.sin(math.pi * (u - DUTY) / (1 - DUTY)), 0, 0)   # on its toes
        else:
            dy, dz, st = gait(u, 0.3)
            c = Vector((SQ_FIST_X * sx, SQ_FIST_Y + dy, FIST_PLANT_Z + dz))
            p.ik[ch] = (c + WRIST_OFF[s], c + WRIST_OFF[s] + Vector((0.25 * sx, 2.0, 0.6)))
            p.end[f'Fist_{s}'] = (-10 if st else -10 - 15 * math.sin(math.pi * (u - DUTY) / (1 - DUTY)), 0, 0)
    w2 = 2 * math.pi * ph
    side = math.sin(w2 + 0.4)
    p.loc['Hips'] = (0.03 * side, 0, -0.24 - 0.05 * (0.5 + 0.5 * math.cos(2 * w2 - 0.6)))
    p.q['Hips'] = arot('Hips', 4.0, -2.0 * side, 2.0 * math.sin(w2))
    spine_bend(p, x=8.0 + 1.5 * math.cos(2 * w2 + 0.8), y=1.0 * side, z=-3.0 * math.sin(w2 + 0.3))
    for s in 'LR':
        sx = 1 if s == 'L' else -1
        p.q[f'Shoulder_{s}'] = arot(f'Shoulder_{s}', 0, 0, -6.0 * sx)     # shoulders rolled in
    p.q['Neck'] = arot('Neck', 6.0, 0, 6.0 * math.sin(w2 * 0.5))
    p.q['Head'] = arot('Head', 8.0, 4.0 * math.sin(w2 * 0.5), 0)       # peering down at the plank, a nervous wobble
    p.q['Jaw'] = arot('Jaw', 4.0)
    tail_wave(p, t, 1.5, 1.0, T, lag=0.1, base_pitch=9.0)            # held up and straight behind
    return p


def windup_pose(p, u):
    """Rear up, both arms overhead (u 0..1)."""
    p.loc['Hips'] = (0, 0.18 * u, 0.30 * u)          # stretch: up on its toes
    spine_bend(p, x=-24 * u)
    p.q['Neck'] = arot('Neck', 6 * u)
    p.q['Head'] = arot('Head', -10 * u)
    p.q['Jaw'] = arot('Jaw', 2 + 16 * u)
    for s in 'LR':
        sx = 1 if s == 'L' else -1
        rest = Vector((KNU[s].x, KNU[s].y, FIST_PLANT_Z)) + WRIST_OFF[s]
        top = Vector((0.8 * sx, -0.6, 4.35))
        p.ik[f'arm_{s}'] = (rest.lerp(top, u) + Vector((0.5 * sx, -0.6, 0)) * math.sin(math.pi * u), Vector((2.2 * sx, 1.5, 3.5)))
        p.end[f'Fist_{s}'] = (-120 * u, 0, 0)


@clip('SlamWindup', 30)
def slam_windup(t, T):
    p = base_pose()
    u = ease(min(1.0, t / (T * 0.7)))
    windup_pose(p, u)
    tr = math.sin(2 * math.pi * t * 9) * 0.6 * max(0.0, (t / T - 0.7) / 0.3)
    p.q['Chest'] = arot('Chest', -28 * 0.25 * u + tr, 0, tr)
    tail_wave(p, t, 3, 6 * u, 1.0, base_pitch=8 * u)
    return p


SLAM_HIT = 6


@clip('Slam', 36)
def slam(t, T):
    p = base_pose()
    f = t * FPS
    if f <= SLAM_HIT:
        u = ease_in(f / SLAM_HIT)            # accelerates into the ground
        hit = u
        windup_pose(p, 1 - u)
    else:
        hit = 1.0
    if f > SLAM_HIT:
        r = ease((f - SLAM_HIT - 6) / (36 - SLAM_HIT - 6))   # hold 6 frames, then recover
        hit = 1 - r
    # impact pose blended in by `hit` (fists on the ground in front, body pitched forward and dropped)
    if hit > 0:
        hb = min(1.0, hit)
        shake = math.sin(f * 2.2) * 0.04 * max(0.0, 1 - abs(f - SLAM_HIT - 2) / 6)
        # squash: the body drops hard and folds forward on the impact, then springs back
        p.loc['Hips'] = tuple(lerp(a, b, hb) for a, b in zip(p.loc.get('Hips', (0, 0, 0)), (0, -0.12, -0.32 + shake)))
        spine_bend(p, x=lerp(-24 * (1 - hb) if f <= SLAM_HIT else 0, 28, hb))
        p.q['Neck'] = arot('Neck', lerp(6, 14, hb)); p.q['Head'] = arot('Head', lerp(-10, -14, hb))
        p.q['Jaw'] = arot('Jaw', lerp(18, 26, hb) if f < 20 else lerp(2, 26, hb))
        for s in 'LR':
            sx = 1 if s == 'L' else -1
            ground = Vector((0.85 * sx, -1.75, FIST_PLANT_Z)) + WRIST_OFF[s]
            restw = Vector((KNU[s].x, KNU[s].y, FIST_PLANT_Z)) + WRIST_OFF[s]
            if f <= SLAM_HIT:
                T_, P_ = p.ik[f'arm_{s}']
                tgt = T_.lerp(ground, ease_in(f / SLAM_HIT))
            else:
                tgt = ground.lerp(restw, 1 - hb)
            p.ik[f'arm_{s}'] = (tgt, Vector((2.2 * sx, 1.8, 2.0)))
            p.end[f'Fist_{s}'] = (lerp(-120 * (1 - hb) if f <= SLAM_HIT else 0, 25, hb), 0, 0)
    tail_wave(p, t, 4, 10 * max(0.0, 1 - abs(f - SLAM_HIT - 3) / 12), 0.6)
    return p


@clip('Swipe', 36)
def swipe(t, T):
    """Right-arm claw: wind-up out and back (0-12), strike across the front (12-18, fastest at ~15), follow-through to
    the left (18-24), recover (24-36)."""
    p = base_pose()
    f = t * FPS
    restw = Vector((KNU['R'].x, KNU['R'].y, FIST_PLANT_Z)) + WRIST_OFF['R']
    wind = Vector((-2.6, 0.55, 3.3))
    mid = Vector((-0.6, -2.55, 2.1))
    thru = Vector((1.3, -1.6, 1.4))
    if f < 12:
        u = ease(f / 12); tgt = restw.lerp(wind, u)
        yaw = -22 * u; jaw = 2 + 10 * u; fist = -40 * u
    elif f < 18:
        u = (f - 12) / 6
        a = wind.lerp(mid, u); b = mid.lerp(thru, u); tgt = a.lerp(b, u)     # quadratic arc through the front
        yaw = lerp(-22, 24, ease(u)); jaw = 12 + 8 * u; fist = lerp(-40, 30, u)
    elif f < 24:
        u = ease_out((f - 18) / 6); tgt = thru.lerp(thru + Vector((0.3, 0.3, -0.5)), u)
        yaw = 24 + 4 * u; jaw = 20 - 6 * u; fist = 30
    else:
        u = ease((f - 24) / 12); tgt = (thru + Vector((0.3, 0.3, -0.5))).lerp(restw, u)
        yaw = 28 * (1 - u); jaw = 14 * (1 - u) + 2; fist = 30 * (1 - u)
    p.ik['arm_R'] = (tgt, tgt + Vector((-1.2, 2.0, 1.2)))
    p.end['Fist_R'] = (fist, 0, -fist * 0.5)
    spine_bend(p, x=-6 * math.sin(math.pi * min(1.0, f / 24)), y=-yaw * 0.25, z=yaw)
    p.loc['Hips'] = (-0.12 * math.sin(math.pi * min(1.0, f / 24)), 0, 0.05 * math.sin(math.pi * min(1.0, f / 24)))
    p.q['Neck'] = arot('Neck', 0, 0, -yaw * 0.3); p.q['Head'] = arot('Head', 4, 0, -yaw * 0.2)
    p.q['Jaw'] = arot('Jaw', jaw)
    tail_wave(p, t, 6, 3, 1.2, base_yaw=-yaw * 0.5)
    return p


@clip('Roar', 60)
def roar(t, T):
    p = base_pose()
    f = t * FPS
    ant = keyed([(0, 0.0), (10, 1.0), (16, 0.0)], f)
    up = keyed([(8, 0.0), (18, 1.0), (44, 1.0), (58, 0.0)], f)
    shake = math.sin(f * 2.4) * up * (1 if 18 < f < 44 else 0)
    p.loc['Hips'] = (0, 0.05 * up, -0.08 * ant + 0.14 * up)
    spine_bend(p, x=6 * ant - 20 * up, z=2.5 * shake)
    p.q['Neck'] = arot('Neck', 14 * ant - 12 * up, 0, 2 * shake)
    p.q['Head'] = arot('Head', 8 * ant - 22 * up, 2 * shake, 3 * shake)
    p.q['Jaw'] = arot('Jaw', 2 + 34 * up + 3 * shake)
    for s in 'LR':
        sx = 1 if s == 'L' else -1
        k = Vector((KNU[s].x + 0.25 * sx * up, KNU[s].y - 0.1 * up, FIST_PLANT_Z + 0.35 * up))
        p.ik[f'arm_{s}'] = (k + WRIST_OFF[s], k + WRIST_OFF[s] + Vector((0.8 * sx, 2.0, 0.8)))
        p.end[f'Fist_{s}'] = (-15 * up, 0, 0)
    tail_wave(p, t, 4 + 4 * up, 8 * up, 0.5, base_pitch=10 * up)
    return p


@clip('Berserk', 60, loop=True)
def berserk(t, T):
    """Chest-pounding rage loop: four alternating blows (L R L R), head up, jaw open, body bouncing on each blow."""
    p = base_pose()
    f = t * FPS
    p.loc['Hips'] = (0, 0.15, 0.2 + 0.05 * math.cos(2 * math.pi * f / 15))
    spine_bend(p, x=-22 + 3 * math.cos(2 * math.pi * f / 15), z=6 * math.sin(2 * math.pi * f / 30))
    p.q['Neck'] = arot('Neck', -4); p.q['Head'] = arot('Head', -14, 3 * math.sin(2 * math.pi * f / 7.5))
    p.q['Jaw'] = arot('Jaw', 22 + 6 * math.sin(2 * math.pi * f / 5))
    for s, off in (('L', 0.0), ('R', 0.5)):
        sx = 1 if s == 'L' else -1
        u = ((f / 30.0) - off) % 1.0          # one blow per arm per 30 frames
        hitc = Vector((0.45 * sx, -1.62, 2.55))
        back = Vector((1.55 * sx, -0.35, 2.7))
        if u < 0.25:
            tgt = back.lerp(hitc, ease_in(u / 0.25))
        elif u < 0.4:
            tgt = hitc
        else:
            tgt = hitc.lerp(back, ease((u - 0.4) / 0.6))
        p.ik[f'arm_{s}'] = (tgt, tgt + Vector((1.6 * sx, 1.6, -0.4)))
        p.end[f'Fist_{s}'] = (80, 0, 0)
    tail_wave(p, t, 9, 6, 1.0, base_pitch=8)
    return p


@clip('KO', 60)
def ko(t, T):
    """Stagger back, collapse onto the belly with a roll to the right, head on the ground, hold."""
    p = Pose()
    f = t * FPS
    stg = keyed([(0, 0.0), (8, 1.0), (16, 0.0)], f)
    down = keyed([(10, 0.0), (34, 1.0)], f, ease_in)
    settle = keyed([(34, 0.0), (40, 1.0), (60, 1.0)], f)
    bounce = 0.05 * math.sin(math.pi * settle) * (1 if f < 42 else 0)
    p.loc['Hips'] = (-0.25 * down, 0.12 * stg - 0.1 * down, 0.1 * stg - 0.72 * down + bounce)
    p.q['Hips'] = arot('Hips', 10 * down, -18 * down, 6 * down)
    spine_bend(p, x=-10 * stg + 24 * down, y=-10 * down)
    p.q['Neck'] = arot('Neck', -14 * stg + 22 * down, 0, 10 * down)
    p.q['Head'] = arot('Head', -10 * stg + 16 * down, -12 * down, 8 * down)
    p.q['Jaw'] = arot('Jaw', 2 + 16 * down)
    plant_legs(p)
    for s in 'LR':
        sx = 1 if s == 'L' else -1
        a = Vector((KNU[s].x, KNU[s].y, FIST_PLANT_Z))
        b = Vector((2.45 * sx, -1.45, 0.25 if s == 'L' else 0.12))
        k = a.lerp(b, down) + Vector((0, 0, 0.5 * stg))
        p.ik[f'arm_{s}'] = (k + WRIST_OFF[s], k + WRIST_OFF[s] + Vector((1.0 * sx, 1.0, 1.5)))
        p.end[f'Fist_{s}'] = (40 * down, -30 * sx * down, 0)
    tail_wave(p, t, 3 * (1 - down), 2, 0.8, base_pitch=8 * down, base_yaw=-10 * down)
    return p


@clip('Flinch', 15)
def flinch(t, T):
    p = base_pose()
    f = t * FPS
    k = keyed([(0, 0.0), (3, 1.0), (15, 0.0)], f)
    p.loc['Hips'] = (0, 0.1 * k, 0.03 * k)
    spine_bend(p, x=-10 * k)
    p.q['Neck'] = arot('Neck', -6 * k); p.q['Head'] = arot('Head', -14 * k, 4 * k)
    p.q['Jaw'] = arot('Jaw', 2 + 12 * k)
    tail_wave(p, t, 4 * k, 4 * k, 0.5)
    return p


# ------------------------------------------------------------------------------------------------ bake to actions
ad = arm.animation_data_create()
ACTS = {}
CLIP_FACTS = {}
for o in obs.values():
    o.modifiers['Armature'].show_viewport = False
for name, (N, loop, fn) in CLIPS.items():
    T = N / FPS
    frames = N + 1                      # last frame = first for loops (Unity loop time)
    poses = []
    for f in range(frames):
        t = (f % N) / FPS if loop else f / FPS
        poses.append(fn(t, T))
    crystal_springs(poses, loop, gain=2.0 if name in ('Walk', 'Slam', 'Berserk', 'Roar', 'KO') else 1.3)
    mats = [fk(p) for p in poses]
    act = bpy.data.actions.new(name); act.use_fake_user = True
    slot = act.slots.new(id_type='OBJECT', name=arm.name)
    ad.action = act
    try:
        ad.action_slot = slot
    except Exception:
        pass
    cb = anim_utils.action_ensure_channelbag_for_slot(act, slot)
    for bn in BONES:
        pb = arm.pose.bones[bn]
        pb.rotation_mode = 'QUATERNION'
        qs = []; ls = []
        prev = None
        for p in poses:
            q = p.q.get(bn, Quaternion())
            if prev is not None and prev.dot(q) < 0:
                q = Quaternion((-q.w, -q.x, -q.y, -q.z))
            prev = q; qs.append(q)
            M = basis_mat(p, bn); ls.append(M.translation)
        for idx in range(4):
            fc = cb.fcurves.ensure(f'pose.bones["{bn}"].rotation_quaternion', index=idx, group_name=bn)
            fc.keyframe_points.add(frames)
            co = []
            for f, q in enumerate(qs):
                co += [f, q[idx]]
            fc.keyframe_points.foreach_set('co', co)
            for kp in fc.keyframe_points:
                kp.interpolation = 'LINEAR'
            fc.update()
        for idx in range(3):
            fc = cb.fcurves.ensure(f'pose.bones["{bn}"].location', index=idx, group_name=bn)
            fc.keyframe_points.add(frames)
            co = []
            for f, l in enumerate(ls):
                co += [f, l[idx]]
            fc.keyframe_points.foreach_set('co', co)
            for kp in fc.keyframe_points:
                kp.interpolation = 'LINEAR'
            fc.update()
    act.frame_range = (0, N)
    ACTS[name] = (act, slot)
    # facts: foot heights, fist speed peak, lowest point of the feet
    fy = {s: [m[f'Foot_{s}'].translation.z for m in mats] for s in 'LR'}
    fr = [m['Fist_R'].translation for m in mats]
    sp = [(fr[i + 1] - fr[i]).length * FPS for i in range(len(fr) - 1)]
    CLIP_FACTS[name] = {'frames': N, 'loop': loop, 'foot_lift_L': round(max(fy['L']) - min(fy['L']), 3),
                        'foot_lift_R': round(max(fy['R']) - min(fy['R']), 3), 'fist_R_peak_frame': int(np.argmax(sp)),
                        'fist_R_peak_mps': round(max(sp), 2), 'hips_min_z': round(min(m['Hips'].translation.z for m in mats), 3)}
    if name == 'Walk':
        # planted heel speed (the game's Measure: Foot_L/Foot_R positions while low)
        spd = []
        for s in 'LR':
            P = [m[f'Foot_{s}'].translation for m in mats]
            mn = min(p.z for p in P)
            for i in range(len(P) - 1):
                if P[i].z < mn + 0.075 and P[i + 1].z < mn + 0.075:
                    spd.append(math.hypot(P[i + 1].x - P[i].x, P[i + 1].y - P[i].y) * FPS)
        CLIP_FACTS[name]['planted_speed'] = round(float(np.mean(spd)), 3) if spd else None
        CLIP_FACTS[name]['stride_m'] = round(STRIDE, 3)
    log('clip', name, CLIP_FACTS[name])
REP['clips'] = CLIP_FACTS
for _nm in ('Idle', 'Walk', 'WalkSqueeze'):
    _p = CLIPS[_nm][2](0.0, CLIPS[_nm][0] / FPS); _M = fk(_p)
    REP.setdefault('new_fists_x', {})[_nm] = [round(_M['Fist_L'].translation.x, 3), round(_M['Fist_R'].translation.x, 3)]
for o in obs.values():
    o.modifiers['Armature'].show_viewport = True
save()

# ------------------------------------------------------------------------------------------------ 6. measure + render
for lv in LV:
    obs[lv].hide_render = lv != 'Hero'
HM = obs['Hero']
EH = mesh_adj(HM)
EH = EH[(EH < SHELL_N['Hero']).all(1)]     # the shell only (pouch, teeth, eyes excluded)
VREST = C.verts(HM)
LREST = np.linalg.norm(VREST[EH[:, 0]] - VREST[EH[:, 1]], axis=1)
NSHELL = len(VH)            # shell vertex count before the rip duplicated some (extras come after)


def deformed(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    m_ = ev.to_mesh()
    a = np.empty(len(m_.vertices) * 3); m_.vertices.foreach_get('co', a)
    ev.to_mesh_clear()
    return a.reshape(-1, 3)


def stretch_metrics():
    X = deformed(HM)
    L = np.linalg.norm(X[EH[:, 0]] - X[EH[:, 1]], axis=1)
    ratio = np.maximum(L / (LREST + 1e-9), LREST / (L + 1e-9))
    tear = (ratio > 1.6) & (np.abs(L - LREST) > 0.03)
    worst = EH[np.argmax(ratio)]
    return {'p99': round(float(np.percentile(ratio, 99)), 3), 'max': round(float(ratio.max()), 2), 'torn_edges': int(tear.sum()),
            'worst_at': np.round(VREST[worst[0]], 2).tolist(), 'lowest_z': round(float(X[:, 2].min()), 3)}


def set_frame(act_name, f):
    act, slot = ACTS[act_name]
    ad.action = act
    try:
        ad.action_slot = slot
    except Exception:
        pass
    sc.frame_set(int(f))


def pose_static(p):
    """Show an authored Pose directly (stress poses) through a temporary one-frame action."""
    fk(p)
    ad.action = None
    for bn in BONES:
        pb = arm.pose.bones[bn]
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = p.q.get(bn, Quaternion())
        pb.location = basis_mat(p, bn).translation
    bpy.context.view_layer.update()


sc2, cam = C.scene_setup(360)
CEN = (0, 0.4, 2.4)
SCALE = {'front': 6.6, 'side': 7.6, 'game': 8.2, 'top': 7.6}

# stress poses
STRESS = {}
def s_arm_up():
    p = base_pose(); windup_pose(p, 1.0); return p
def s_slam():
    return slam(SLAM_HIT / FPS, 36 / FPS)
def s_swipe():
    return swipe(15 / FPS, 36 / FPS)
def s_tail():
    p = base_pose()
    for i, n in enumerate(TAILS):
        p.q[n] = arot(n, -4, 0, 22)
    return p
def s_jaw():
    p = base_pose(); p.q['Jaw'] = arot('Jaw', 36); p.q['Head'] = arot('Head', -12); return p
def s_crouch():
    p = base_pose(); p.loc['Hips'] = (0, 0.05, -0.42); spine_bend(p, x=12); p.q['Head'] = arot('Head', -8); return p
grid = []
for nm, fn in (('arm_raised', s_arm_up), ('slam', s_slam), ('swipe', s_swipe), ('tail_curl', s_tail), ('jaw_open', s_jaw), ('crouch', s_crouch)):
    pose_static(fn())
    STRESS[nm] = stretch_metrics()
    row = []
    for v in ('front', 'side'):
        pth = os.path.join(OUT, f'_st_{nm}_{v}.png')
        C.shoot(sc2, cam, pth, v, CEN, SCALE[v], px=420)
        row.append(pth)
    if nm == 'jaw_open':
        pth = os.path.join(OUT, f'_st_{nm}_close.png')
        C.shoot(sc2, cam, pth, 'front', (0, -1.6, 2.7), 1.6, px=420); row.append(pth)
        pth = os.path.join(OUT, f'_st_{nm}_close_side.png')
        C.shoot(sc2, cam, pth, 'side', (0, -1.6, 2.7), 1.6, px=420); row.append(pth)
    grid.append(row)
    log('stress', nm, STRESS[nm])
REP['stress'] = STRESS
C.sheet(grid, os.path.join(OUT, 'sheet_stress.png'))
# rest + dominant-bone weights (Hero copy with a temporary colour)
ad.action = None
for pb in arm.pose.bones:
    pb.rotation_quaternion = Quaternion(); pb.location = Vector()
bpy.context.view_layer.update()
row = []
for v in ('front', 'side', 'top', 'game'):
    pth = os.path.join(OUT, f'_rest_{v}.png'); C.shoot(sc2, cam, pth, v, CEN, SCALE[v], px=480); row.append(pth)
wc = HM.copy(); wc.data = HM.data.copy(); wc.name = 'WeightsView'; sc.collection.objects.link(wc)
HM.hide_render = True
Wd = np.zeros(len(wc.data.vertices), dtype=int)
best = np.zeros(len(wc.data.vertices))
gi2 = {g.index: g.name for g in wc.vertex_groups}
for vv in wc.data.vertices:
    for g in vv.groups:
        if g.weight > best[vv.index]:
            best[vv.index] = g.weight; Wd[vv.index] = BI.get(gi2[g.group], 0)
rng = np.random.default_rng(7)
pal = rng.uniform(0.15, 1.0, (len(DEF), 3))
me = wc.data
a = me.color_attributes.new('WDom', 'FLOAT_COLOR', 'POINT')
cols = np.concatenate([pal[Wd], np.ones((len(Wd), 1))], axis=1)
a.data.foreach_set('color', cols.astype(np.float32).ravel())
me.color_attributes.active_color_name = 'WDom'
me.color_attributes.render_color_index = me.color_attributes.find('WDom')
row2 = []
for v in ('front', 'side', 'top'):
    pth = os.path.join(OUT, f'_w_{v}.png'); C.shoot(sc2, cam, pth, v, CEN, SCALE[v], px=480); row2.append(pth)
bpy.data.objects.remove(wc, do_unlink=True)
HM.hide_render = False
C.sheet([row, row2], os.path.join(OUT, 'sheet_rest_weights.png'))

# clip strips: front, side, game rows x 6 frames
if not QUICK:
    for name, (N, loop, fn) in CLIPS.items():
        if name == 'Slam':
            fr = [0, 3, SLAM_HIT, 10, 20, N]
        elif name == 'Swipe':
            fr = [0, 8, 12, 15, 20, 30]
        elif name in ('Walk', 'WalkSqueeze'):
            fr = [0, 5, 10, 15, 20, 25]
        else:
            fr = [int(round(i * N / 5)) for i in range(6)]
        grid = []
        for v in ('front', 'side', 'game'):
            row = []
            for f in fr:
                set_frame(name, f)
                pth = os.path.join(OUT, f'_c_{name}_{v}_{f}.png')
                C.shoot(sc2, cam, pth, v, CEN, SCALE[v], px=300); row.append(pth)
            grid.append(row)
        C.sheet(grid, os.path.join(OUT, f'strip_{name}.png'))
        log('strip', name, fr)
save()

# ------------------------------------------------------------------------------------------------ 7. export + verify
ad.action = None
for t in list(ad.nla_tracks):
    ad.nla_tracks.remove(t)
for name in sorted(ACTS):
    act, slot = ACTS[name]
    tr = ad.nla_tracks.new(); tr.name = name
    st = tr.strips.new(name, 0, act); st.name = name
    try:
        if st.action_slot is None:
            st.action_slot = slot
    except Exception:
        pass
arm_d.pose_position = 'POSE'
for pb in arm.pose.bones:
    pb.rotation_quaternion = Quaternion(); pb.location = Vector()
KAIJU_FBX = dict(object_types={'ARMATURE', 'MESH'}, global_scale=1.0, apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_space_transform=False,
    use_mesh_modifiers=False, mesh_smooth_type='FACE', use_tspace=False, colors_type='SRGB', prioritize_active_color=True,
    add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', use_armature_deform_only=False, bake_anim=True,
    bake_anim_use_all_bones=True, bake_anim_use_nla_strips=True, bake_anim_use_all_actions=False,
    bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=1.0, path_mode='STRIP',
    embed_textures=False)
for o in bpy.context.view_layer.objects:
    o.select_set(o == arm or o in obs.values())
bpy.context.view_layer.objects.active = arm
out_fbx = os.path.join(OUT, 'Kaiju_Adult.fbx')
r = bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=True, **KAIJU_FBX)
REP['export'] = {'result': list(r), 'bytes': os.path.getsize(out_fbx)}
log('export', REP['export'])
save()
# verify in a fresh scene
C.clear()
bpy.ops.import_scene.fbx(filepath=out_fbx, use_anim=True)
ver = {'objects': {}, 'actions': {}}
for o in bpy.context.scene.objects:
    if o.type == 'ARMATURE':
        ver['bones'] = len(o.data.bones)
    if o.type == 'MESH':
        me = o.data
        me.calc_loop_triangles()
        mi = max((len(v.groups) for v in me.vertices), default=0)
        ver['objects'][o.name] = {'tris': len(me.loop_triangles), 'colors': [a.name for a in me.color_attributes],
                                  'groups': len(o.vertex_groups), 'max_infl': mi,
                                  'zmax': round(max(v.co.z for v in me.vertices) * o.matrix_world.to_scale().z, 3)}
for a in bpy.data.actions:
    ver['actions'][a.name] = list(a.frame_range)
REP['verify'] = ver
try:
    arms = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
    for o in arms:
        o.data.pose_position = 'REST'
    bpy.context.view_layer.update()
    REP['new_footprint'] = {'rest': footprint_of_imported()}
    for o in arms:
        o.data.pose_position = 'POSE'
        idle = next((a for a in bpy.data.actions if a.name.endswith('Idle')), None)
        o.animation_data_create().action = idle
        try:
            o.animation_data.action_slot = idle.slots[0]
        except Exception:
            pass
    bpy.context.scene.frame_set(1)
    REP['new_footprint']['idle'] = footprint_of_imported()
    sq = next((a for a in bpy.data.actions if a.name.endswith('WalkSqueeze')), None)
    if sq:
        for o in arms:
            o.animation_data.action = sq
            try:
                o.animation_data.action_slot = sq.slots[0]
            except Exception:
                pass
        bpy.context.scene.frame_set(1)
        REP['new_footprint']['squeeze'] = footprint_of_imported()
    log('new adult footprint', REP['new_footprint'])
except Exception as ex:
    log('new footprint failed', ex)
log('verify', json.dumps(ver)[:1500])
REP['seconds'] = round(time.time() - T0, 1)
save()
print('[KR2] done', REP['seconds'])
