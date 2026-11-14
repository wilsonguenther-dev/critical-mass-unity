# rig-method-2 trial (2026-11-14). Blender 5.2.3, headless, on GitHub: loop/gates/remote blender (tools/ci/offload.sh).
#   args: {out} {repo} [CLASS_FBX]   default CLASS_FBX = Assets/_Game/Art/Characters/Warrior.fbx
# What it does (one process):
#   0. Inventory of the repo's character FBX files (meshes, triangles, bones, finger bones, clips).
#   1. Takes the class's Hero meshes in their bind shape, drops the old skin, grounds the soles at z = 0.
#   2. Finds landmarks by geometry (landmarks.py, no hand placement) and builds ONE standard skeleton:
#      the shared clip pack's bone names + 15 finger bones per hand (53 bones; a mitt mesh binds three fingers to the
#      middle chain, so the skeleton stays identical across classes).
#   3. Weights: bone heat on a watertight voxel proxy, transferred to the real mesh, head top held rigid,
#      fingers from segment distance inside each finger piece, smoothed, max 4 influences, normalised.
#      Props become separate objects parented to the hand-slot bones (never skinned).
#   4. Pose test (rest, arms down, arms up, deep squat, big stride; fist, open, point) with numbers and renders.
#   5. Retargets two shared clips (walk, attack) by world-space rotation deltas (no foot IK here) and renders strips.
# Writes {out}/rm2_report.json, PNG sheets, and {out}/<Class>_RM2.fbx (not used by the game).
import bpy, bmesh, sys, os, json, math, time, traceback
import numpy as np
from mathutils import Vector, Matrix, Quaternion

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = argv[0] if argv else '/tmp/rm2_out'
REPO = argv[1] if len(argv) > 1 else '.'
CLASS_FBX = argv[2] if len(argv) > 2 else 'Assets/_Game/Art/Characters/Warrior.fbx'
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import landmarks as LM

os.makedirs(OUT, exist_ok=True)


def _hook(t, v, tb):
    REP['fatal'] = ''.join(traceback.format_exception(t, v, tb))[-3000:]
    print(REP['fatal'], flush=True)
    save_report()
    sys.__excepthook__(t, v, tb)


sys.excepthook = _hook
REP = {'class_fbx': CLASS_FBX, 'blender': bpy.app.version_string, 'log': []}
T0 = time.time()


def log(*a):
    s = ' '.join(str(x) for x in a)
    print('[rm2]', s, flush=True)
    REP['log'].append(f'{time.time() - T0:6.1f}s {s}')


def save_report():
    json.dump(REP, open(os.path.join(OUT, 'rm2_report.json'), 'w'), indent=1, default=lambda o: o.tolist() if hasattr(o, 'tolist') else str(o))


def imp(path, anim=False):
    bo, ba = set(bpy.data.objects), set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=os.path.join(REPO, path), use_anim=anim, use_custom_normals=False,
                             ignore_leaf_bones=False, automatic_bone_orientation=False,
                             primary_bone_axis='Y', secondary_bone_axis='X')
    return [o for o in bpy.data.objects if o not in bo], [a for a in bpy.data.actions if a not in ba]


def upd():
    bpy.context.view_layer.update()


def tris(ob):
    me = ob.data
    me.calc_loop_triangles()
    return len(me.loop_triangles)


FINGER_WORDS = ('finger', 'thumb', 'index', 'middle', 'ring', 'pinky', 'little')

# ---------------------------------------------------------------- 0. inventory of the repo FBX files
inv = {}
for path in ('Assets/_Game/Art/Characters/Warrior.fbx', 'Assets/_Game/Art/Farmer/Farmer.fbx', 'Assets/_Game/Art/Miner/Miner.fbx',
             'Assets/_Game/Art/Kaiju/Kaiju_Adult.fbx', 'Assets/_Game/Art/Kaiju/Kaiju_Hatchling.fbx',
             'Assets/_Game/Art/Characters/KayKit_Clips.fbx'):
    try:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        objs, acts = imp(path, anim=True)
        rec = {'meshes': {}, 'armatures': {}, 'clips': len(acts), 'clip_names': sorted(a.name.split('|')[-1] for a in acts)[:60]}
        for o in objs:
            if o.type == 'MESH':
                me = o.data
                rec['meshes'][o.name] = {'tris': tris(o), 'shape_keys': len(me.shape_keys.key_blocks) if me.shape_keys else 0,
                                         'vgroups': len(o.vertex_groups), 'color_attrs': [c.name for c in me.color_attributes],
                                         'size': [round(v, 3) for v in o.dimensions]}
            elif o.type == 'ARMATURE':
                names = [b.name for b in o.data.bones]
                rec['armatures'][o.name] = {'bones': len(names), 'deform': sum(b.use_deform for b in o.data.bones),
                                            'finger_bones': [n for n in names if any(w in n.lower() for w in FINGER_WORDS)],
                                            'names': names,
                                            'heads': {b.name: [round(v, 4) for v in (o.matrix_world @ b.head_local)] for b in o.data.bones}}
        inv[path] = rec
        log('inventory', path, 'meshes', len(rec['meshes']), 'arm', {k: v['bones'] for k, v in rec['armatures'].items()}, 'clips', len(acts))
    except Exception as e:
        inv[path] = {'error': repr(e)}
        log('inventory failed', path, repr(e))
REP['inventory'] = inv
save_report()

# ---------------------------------------------------------------- 1. the class's Hero meshes in bind shape
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
if CLASS_FBX.lower().endswith('.glb'):
    # a raw generated model (no rig): import, scale to the 2.0 m class height
    bo = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=CLASS_FBX if os.path.isabs(CLASS_FBX) else os.path.join(REPO, CLASS_FBX))
    objs = [o for o in bpy.data.objects if o not in bo]
    for o in objs:
        if o.type == 'MESH':
            o.name = 'Generated_Hero_Body'
            mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
            o.data.transform(o.matrix_world); o.matrix_world = Matrix.Identity(4)
            hgt = o.dimensions.z
            o.data.transform(Matrix.Scale(2.0 / hgt, 4))
            log('generated mesh', len(o.data.vertices), 'verts, scaled', round(2.0 / hgt, 3))
else:
    objs, _ = imp(CLASS_FBX)
old_arm = next((o for o in objs if o.type == 'ARMATURE'), None)
old_heads = {b.name: (old_arm.matrix_world @ b.head_local) for b in old_arm.data.bones} if old_arm else {}
template_names = [b.name for b in old_arm.data.bones] if old_arm else []
meshes = [o for o in objs if o.type == 'MESH']
hero = [o for o in meshes if 'hero' in o.name.lower()] or meshes
body = next((o for o in hero if 'body' in o.name.lower()), max(hero, key=lambda o: len(o.data.vertices)))
props = [o for o in hero if o is not body]
# props welded into the Hero mesh but skinned 100% to a hand slot: split them off as their own objects
def split_mask(ob, mask, name):
    me2 = ob.data.copy()
    bm = bmesh.new(); bm.from_mesh(me2); bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not mask[v.index]], context='VERTS')
    bm.to_mesh(me2); bm.free()
    o2 = bpy.data.objects.new(name, me2); scene.collection.objects.link(o2); o2.matrix_world = ob.matrix_world.copy()
    bm = bmesh.new(); bm.from_mesh(ob.data); bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if mask[v.index]], context='VERTS')
    bm.to_mesh(ob.data); bm.free()
    return o2


for gname in [g.name for g in body.vertex_groups if g.name.startswith('handslot')]:
    gidx = body.vertex_groups[gname].index
    mask = np.zeros(len(body.data.vertices), bool)
    for v in body.data.vertices:
        for g in v.groups:
            if g.group == gidx and g.weight > 0.5:
                mask[v.index] = True
    if mask.sum() > 20:
        props.append(split_mask(body, mask, 'Prop_' + gname))
        log('split prop', gname, int(mask.sum()), 'vertices')
log('class', CLASS_FBX, 'body', body.name, len(body.data.vertices), 'verts', 'props', [p.name for p in props], 'template bones', len(template_names))
for o in [body] + props:
    mw = o.matrix_world.copy()
    for m in list(o.modifiers):
        o.modifiers.remove(m)
    o.parent = None
    o.matrix_world = mw
    o.vertex_groups.clear()
    o.data.transform(o.matrix_world)
    o.matrix_world = Matrix.Identity(4)
for o in objs:
    if o not in [body] + props:
        bpy.data.objects.remove(o, do_unlink=True)


def verts(ob):
    a = np.empty(len(ob.data.vertices) * 3)
    ob.data.vertices.foreach_get('co', a)
    return a.reshape(-1, 3)


def faces(ob):
    me = ob.data
    me.calc_loop_triangles()
    a = np.empty(len(me.loop_triangles) * 3, dtype=np.int64)
    me.loop_triangles.foreach_get('vertices', a)
    return a.reshape(-1, 3)


V = verts(body)
zmin = V[:, 2].min()
xc = (V[:, 0].max() + V[:, 0].min()) / 2
shift = Matrix.Translation((-xc, 0, -zmin))
for o in [body] + props:
    o.data.transform(shift)
old_heads = {k: shift @ v for k, v in old_heads.items()}
V = verts(body)
F = faces(body)
np.save(os.path.join(OUT, 'body_V.npy'), V); np.save(os.path.join(OUT, 'body_F.npy'), F)
Vw, Fw, inv_w = LM.weld(V, F)
lmk = LM.find(Vw, Fw)
if np.mean([lmk['joints']['_toetip.l'][1] - lmk['joints']['foot.l'][1]]) > 0:
    log('toes point +Y: turning the model 180 degrees about Z')
    R = Matrix.Rotation(math.pi, 4, 'Z')
    for o in [body] + props:
        o.data.transform(R)
    V = verts(body); Vw, Fw, inv_w = LM.weld(V, F); lmk = LM.find(Vw, Fw)
# arms: if the automatic arm landmarks are missing or more than 0.12 m from the previous rig's joints, seed the
# shoulder, elbow and wrist from that rig (hands and fingers are still found by geometry) and say so.
seed = {}
auto_arm = {}
for s_ in ('l', 'r'):
    jj = lmk['joints']
    keys = (f'upperarm.{s_}', f'lowerarm.{s_}', f'wrist.{s_}')
    if all(k in old_heads for k in keys):
        d = [round((Vector(jj[k]) - old_heads[k]).length, 3) if k in jj else None for k in keys]
        auto_arm[s_] = d
        if any(x is None or x > 0.12 for x in d):
            seed[s_] = [list(old_heads[k]) for k in keys]
REP['auto_arm_distance_to_old_rig_m'] = auto_arm
if seed:
    log('arm landmarks seeded from the previous rig for sides', sorted(seed), 'auto distances', auto_arm)
    lmk = LM.find(Vw, Fw, arm_seed=seed)
REP['arm_seeded_sides'] = sorted(seed)
H = lmk['height']
J = {k: Vector(v) for k, v in lmk['joints'].items()}
REP['landmarks'] = {'notes': lmk['notes'], 'joints': lmk['joints'], 'height': H,
                    'hands': {s: {k: v for k, v in h.items() if k not in ('finger_vids',)} for s, h in lmk['hands'].items()}}
for n in lmk['notes']:
    log('landmark:', n)
cmp_ = {}
for k, v in J.items():
    if k in old_heads:
        cmp_[k] = round((old_heads[k] - v).length, 4)
REP['landmark_vs_old_rig_m'] = cmp_
log('landmark distance to the old rig joints (m):', cmp_)
save_report()

# ---------------------------------------------------------------- 2. one standard skeleton
SIDES = (('l', 1), ('r', -1))
FING = ('thumb', 'index', 'middle', 'ring', 'pinky')
arm_d = bpy.data.armatures.new('Rig')
arm = bpy.data.objects.new('Rig', arm_d)
scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
for o in scene.objects:
    o.select_set(False)
arm.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
eb = arm_d.edit_bones


def bone(name, head, tail, parent=None, deform=True, roll_to=None, connect=False):
    b = eb.new(name)
    b.head = Vector(head); b.tail = Vector(tail)
    if (b.tail - b.head).length < 1e-4:
        b.tail = b.head + Vector((0, 0, 0.02))
    if parent:
        b.parent = eb[parent]
        b.use_connect = connect
    b.use_deform = deform
    if roll_to is not None:
        b.align_roll(Vector(roll_to))
    return b


FWD = Vector((0, -1, 0))
bone('root', (0, 0, 0), (0, -0.3, 0), deform=False)
bone('hips', J['hips'], J['spine'], 'root', roll_to=FWD)
bone('spine', J['spine'], J['chest'], 'hips', roll_to=FWD)
bone('chest', J['chest'], J['head'], 'spine', roll_to=FWD)
bone('head', J['head'], J['_headtop'], 'chest', roll_to=FWD)
palm = {}
for s, sg in SIDES:
    bone(f'upperleg.{s}', J[f'upperleg.{s}'], J[f'lowerleg.{s}'], 'hips', roll_to=FWD)
    bone(f'lowerleg.{s}', J[f'lowerleg.{s}'], J[f'foot.{s}'], f'upperleg.{s}', roll_to=FWD)
    bone(f'foot.{s}', J[f'foot.{s}'], J[f'toes.{s}'], f'lowerleg.{s}', roll_to=Vector((0, 0, 1)))
    bone(f'toes.{s}', J[f'toes.{s}'], J[f'_toetip.{s}'], f'foot.{s}', roll_to=Vector((0, 0, 1)))
    if f'upperarm.{s}' not in J:
        continue
    bone(f'upperarm.{s}', J[f'upperarm.{s}'], J[f'lowerarm.{s}'], 'chest', roll_to=FWD)
    bone(f'lowerarm.{s}', J[f'lowerarm.{s}'], J[f'wrist.{s}'], f'upperarm.{s}', roll_to=FWD)
    h = lmk['hands'].get(s, {})
    wr, hd = J[f'wrist.{s}'], J[f'hand.{s}']
    hdir = (J[f'handslot.{s}'] - wr).normalized()
    bone(f'wrist.{s}', wr, hd, f'lowerarm.{s}', roll_to=FWD)
    # palm normal: least-spread axis of the hand's vertices, pointing toward the body's midline
    hv = Vw[(Vw - np.array(wr)) @ np.array(hdir) > 0]
    hv = hv[np.linalg.norm(hv - np.array(J[f'handslot.{s}']), axis=1) < 0.12 * H]
    inward = Vector((0, J['hips'].y, J['hips'].z)) - Vector(J[f'handslot.{s}'])
    n = inward.normalized()
    if len(hv) > 20:
        w, ev = np.linalg.eigh(np.cov((hv - hv.mean(0)).T))
        n = Vector(ev[:, 0])
        if n.dot(inward) < 0:   # the palm faces the hips: down in a T-pose, inward in an A-pose
            n = -n
    n = (n - hdir * n.dot(hdir)).normalized()
    side_dir = hdir.cross(n).normalized()
    palm[s] = (n, side_dir, hdir)
    tip_all = Vector(h['mitten'][1] if 'mitten' in h else h.get('middle', h.get('index', [None, wr + hdir * 0.1 * H]))[1])
    bone(f'hand.{s}', hd, hd + hdir * max(0.3 * (tip_all - hd).length, 0.02), f'wrist.{s}', roll_to=n)
    bone(f'handslot.{s}', J[f'handslot.{s}'], J[f'handslot.{s}'] + hdir * 0.05, f'hand.{s}', deform=False, roll_to=n)
    ch = {}
    for k in ('thumb', 'index', 'middle', 'ring', 'pinky', 'mitten'):
        if k in h:
            ch[k] = (Vector(h[k][0]), Vector(h[k][1]))
    if 'mitten' in ch and 'middle' not in ch:
        ch['middle'] = ch['mitten']
    # make sure the side direction points from the index toward the pinky
    if 'index' in ch and 'middle' in ch and (ch['middle'][0] - ch['index'][0]).dot(side_dir) < 0:
        side_dir = -side_dir
    off = 0.012 * H
    if 'index' not in ch:
        ch['index'] = (ch['middle'][0] - side_dir * off, ch['middle'][1] - side_dir * off)
    if 'ring' not in ch:
        ch['ring'] = (ch['middle'][0] + side_dir * off, ch['middle'][1] + side_dir * off)
    if 'pinky' not in ch:
        ch['pinky'] = (ch['ring'][0] + side_dir * off, ch['ring'][1] + side_dir * off)
    for f in FING:
        base, tip = ch[f]
        if f == 'thumb':
            p0 = hd.lerp(base, 0.5)
            pts = [p0, base, base.lerp(tip, 0.55), tip]
            roll = (Vector(J[f'handslot.{s}']) - base)
            roll = (roll - (tip - base).normalized() * roll.dot((tip - base).normalized())).normalized()
        else:
            pts = [base, base.lerp(tip, 0.45), base.lerp(tip, 0.75), tip]
            roll = n
        par = f'hand.{s}'
        for i in range(3):
            nm = f'{f}_0{i + 1}.{s}'
            bone(nm, pts[i], pts[i + 1], par, roll_to=roll, connect=(i > 0))
            par = nm
    REP.setdefault('finger_mode', {})[s] = h.get('mode')
bpy.ops.object.mode_set(mode='OBJECT')
names = [b.name for b in arm_d.bones]
REP['skeleton'] = {'bones': len(names), 'deform': sum(b.use_deform for b in arm_d.bones), 'names': names,
                   'template_names_missing_here': [n for n in template_names if n not in names],
                   'new_here_not_in_template': [n for n in names if n not in template_names]}
log('skeleton bones', len(names), 'missing vs template', REP['skeleton']['template_names_missing_here'])
save_report()

# ---------------------------------------------------------------- 3. weights
DEF = [b.name for b in arm_d.bones if b.use_deform]
FINGER_B = [n for n in DEF if n.split('_0')[0] in FING]
proxy = body.copy(); proxy.data = body.data.copy(); proxy.name = 'Proxy'
scene.collection.objects.link(proxy)
proxy.data.remesh_voxel_size = H / 140
with bpy.context.temp_override(active_object=proxy, object=proxy, selected_objects=[proxy], selected_editable_objects=[proxy]):
    bpy.ops.object.voxel_remesh()
log('proxy voxel remesh', len(proxy.data.vertices), 'verts (voxel', round(H / 140, 4), 'm)')
for n in FINGER_B:
    arm_d.bones[n].use_deform = False
for o in scene.objects:
    o.select_set(False)
proxy.select_set(True); arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
for n in FINGER_B:
    arm_d.bones[n].use_deform = True
pg = {g.name for g in proxy.vertex_groups}
log('proxy heat groups', len(pg))
for n in DEF:
    body.vertex_groups.new(name=n)
m = body.modifiers.new('DT', 'DATA_TRANSFER')
m.object = proxy
m.use_vert_data = True
m.data_types_verts = {'VGROUP_WEIGHTS'}
m.vert_mapping = 'POLYINTERP_NEAREST'
m.layers_vgroup_select_src = 'ALL'
m.layers_vgroup_select_dst = 'NAME'
with bpy.context.temp_override(object=body, active_object=body, selected_objects=[body]):
    bpy.ops.object.modifier_apply(modifier=m.name)
bi = {n: i for i, n in enumerate(DEF)}
gi = {g.index: g.name for g in body.vertex_groups}
nv = len(body.data.vertices)
W = np.zeros((nv, len(DEF)))
for v in body.data.vertices:
    for g in v.groups:
        nm = gi[g.group]
        if nm in bi:
            W[v.index, bi[nm]] = g.weight
log('transferred: vertices with no weight', int((W.sum(1) < 1e-6).sum()), 'of', nv)

P = V
# head top held rigid (big heads must not bend with the neck)
dom = W.argmax(1)
headz = J['head'].z
rig_head = (P[:, 2] > headz + 0.06 * H) & np.isin(dom, [bi['head'], bi['chest'], bi['spine']])
W[rig_head] = 0; W[rig_head, bi['head']] = 1


def seg_dist(P, a, b):
    a = np.array(a); b = np.array(b); ab = b - a
    t = np.clip(((P - a) @ ab) / (ab @ ab + 1e-12), 0, 1)
    return np.linalg.norm(P - (a + t[:, None] * ab), axis=1), t


locked = rig_head.copy()
for s, sg in SIDES:
    h = lmk['hands'].get(s)
    if not h or f'hand.{s}' not in bi:
        continue
    n, side_dir, hdir = palm[s]
    fv = h.get('finger_vids', {})
    groups = {}
    for k, vids in fv.items():
        mask = np.isin(inv_w, vids)
        tgt = 'middle' if k == 'mitten' else k
        groups[tgt] = mask
    # mitt: split an index strip off the thumb side of the mitten
    if 'mitten' in fv and 'index' not in fv and 'middle' in groups:
        mk = groups['middle']
        base = np.array(arm_d.bones[f'middle_01.{s}'].head_local)
        sd = (P[mk] - base) @ np.array(side_dir)
        lo, hi = np.percentile(sd, 5), np.percentile(sd, 95)
        t = np.clip(((sd - lo) / (hi - lo + 1e-9) - 0.25) / 0.2, 0, 1)   # 0 = index side, 1 = mitten side
        idx_mask = np.zeros(nv, bool); idx_mask[np.nonzero(mk)[0][t < 0.5]] = True
        groups['index'] = idx_mask
        groups['middle'] = mk & ~idx_mask
    for f, mask in groups.items():
        if not mask.any():
            continue
        segs = [f'{f}_0{i}.{s}' for i in (1, 2, 3)]
        Pm = P[mask]
        D = []
        for sn in segs:
            bb = arm_d.bones[sn]
            d, _ = seg_dist(Pm, bb.head_local, bb.tail_local)
            D.append(d)
        D = np.array(D).T
        w = 1.0 / (D ** 4 + 1e-12)
        w /= w.sum(1, keepdims=True)
        # blend toward the hand near the knuckle so the palm does not tear
        b1 = arm_d.bones[segs[0]]
        _, t = seg_dist(Pm, b1.head_local, b1.tail_local)
        keep_hand = np.clip(0.5 - t, 0, 0.5)
        rows = np.nonzero(mask)[0]
        W[rows] = 0
        for j, sn in enumerate(segs):
            W[rows, bi[sn]] = w[:, j] * (1 - keep_hand)
        W[rows, bi[f'hand.{s}']] += keep_hand
        locked[rows] = True
    # one-piece hand (fist or mitt with fused thumb): thumb by distance to its chain, inside the hand only
    if 'thumb' not in fv:
        hand_mask = (dom == bi[f'hand.{s}']) | (dom == bi[f'wrist.{s}'])
        segs = [f'thumb_0{i}.{s}' for i in (1, 2, 3)]
        Pm = P[hand_mask]
        D = np.array([seg_dist(Pm, arm_d.bones[sn].head_local, arm_d.bones[sn].tail_local)[0] for sn in segs]).T
        near = D.min(1) < 0.025 * H
        rows = np.nonzero(hand_mask)[0][near]
        w = 1.0 / (D[near] ** 4 + 1e-12); w /= w.sum(1, keepdims=True)
        fall = np.clip(1 - D[near].min(1) / (0.025 * H), 0, 1)
        W[rows] *= (1 - fall)[:, None]
        for j, sn in enumerate(segs):
            W[rows, bi[sn]] += w[:, j] * fall
    REP.setdefault('finger_groups_vertices', {})[s] = {f: int(m_.sum()) for f, m_ in groups.items()}
# smooth (not the locked parts): two Laplacian passes over the mesh edges
me = body.data
E = np.empty(len(me.edges) * 2, dtype=np.int64); me.edges.foreach_get('vertices', E); E = E.reshape(-1, 2)
deg = np.bincount(E.ravel(), minlength=nv).astype(float)
for _ in range(2):
    acc = np.zeros_like(W)
    np.add.at(acc, E[:, 0], W[E[:, 1]]); np.add.at(acc, E[:, 1], W[E[:, 0]])
    avg = acc / np.maximum(deg, 1)[:, None]
    Wn = 0.5 * W + 0.5 * avg
    W = np.where(locked[:, None], W, Wn)
# max 4 influences, normalised
order = np.argsort(-W, axis=1)
W4 = np.zeros_like(W)
r = np.arange(nv)[:, None]
W4[r, order[:, :4]] = W[r, order[:, :4]]
W4[W4 < 0.01] = 0
sums = W4.sum(1)
zero = sums < 1e-9
if zero.any():
    W4[zero, bi['hips']] = 1; sums = W4.sum(1)
W = W4 / sums[:, None]
for n in DEF:
    g = body.vertex_groups[n]
    g.remove(list(range(nv)))
    col = W[:, bi[n]]
    for i in np.nonzero(col > 0)[0]:
        g.add([int(i)], float(col[i]), 'REPLACE')
REP['weights'] = {'vertices': nv, 'unweighted_before_fix': int(zero.sum()), 'max_influences': int((W > 0).sum(1).max()),
                  'sum_error_max': float(np.abs(W.sum(1) - 1).max()), 'rigid_head_vertices': int(rig_head.sum())}
log('weights', REP['weights'])
bpy.data.objects.remove(proxy, do_unlink=True)
am = body.modifiers.new('Armature', 'ARMATURE'); am.object = arm
body.parent = arm
# props on the hand slots
for p in props:
    c = Vector(np.mean(verts(p), 0))
    s = min(('l', 'r'), key=lambda s: (arm_d.bones[f'hand.{s}'].head_local - c).length if f'hand.{s}' in arm_d.bones else 1e9)
    mw = p.matrix_world.copy()
    p.parent = arm; p.parent_type = 'BONE'; p.parent_bone = f'handslot.{s}'
    upd(); p.matrix_world = mw
    REP.setdefault('props', {})[p.name] = f'handslot.{s}'
log('props', REP.get('props'))
save_report()

# ---------------------------------------------------------------- 4. poses, numbers, renders
Wdom = W.argmax(1)
F_all = faces(body)
E_rest = np.linalg.norm(P[E[:, 0]] - P[E[:, 1]], axis=1)


def tri_normals(X):
    n = np.cross(X[F_all[:, 1]] - X[F_all[:, 0]], X[F_all[:, 2]] - X[F_all[:, 0]])
    return n / (np.linalg.norm(n, axis=1, keepdims=True) + 1e-12)


N_rest = tri_normals(P)


def deformed():
    dg = bpy.context.evaluated_depsgraph_get()
    ev = body.evaluated_get(dg)
    m_ = ev.to_mesh()
    a = np.empty(len(m_.vertices) * 3); m_.vertices.foreach_get('co', a)
    ev.to_mesh_clear()
    return a.reshape(-1, 3)


def metrics():
    X = deformed()
    L = np.linalg.norm(X[E[:, 0]] - X[E[:, 1]], axis=1)
    ratio = np.maximum(L / (E_rest + 1e-9), E_rest / (L + 1e-9))
    tear = int(((ratio > 1.5) & (np.abs(L - E_rest) > 0.015)).sum())
    Rb = []
    for n in DEF:
        pb = arm.pose.bones[n]
        Rb.append(np.array((pb.matrix @ arm_d.bones[n].matrix_local.inverted()).to_3x3()))
    Rb = np.array(Rb)
    fdom = Wdom[F_all[:, 0]]
    expn = np.einsum('fij,fj->fi', Rb[fdom], N_rest)
    inverted = int((np.einsum('fi,fi->f', tri_normals(X), expn) < 0).sum())
    return {'stretch_p99': round(float(np.percentile(ratio, 99)), 3), 'stretch_max': round(float(ratio.max()), 2),
            'tear_edges': tear, 'inverted_faces': inverted, 'lowest_z': round(float(X[:, 2].min()), 4)}, X


def reset():
    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = Quaternion()
        pb.location = Vector()
    upd()


def set_world(name, loc, rot3):
    pb = arm.pose.bones[name]
    pb.matrix = Matrix.LocRotScale(loc, rot3.to_quaternion(), None)
    upd()


def aim(name, d):
    pb = arm.pose.bones[name]
    M = pb.matrix.copy()
    y = M.to_3x3().col[1].normalized()
    q = y.rotation_difference(Vector(d).normalized())
    set_world(name, M.translation, q.to_matrix() @ M.to_3x3())


def leg_ik(s, ankle, bend):
    hp = arm.pose.bones[f'upperleg.{s}'].head.copy()
    b1, b2 = arm_d.bones[f'upperleg.{s}'], arm_d.bones[f'lowerleg.{s}']
    l1, l2 = b1.length, b2.length
    d = ankle - hp; dl = min(d.length, l1 + l2 - 1e-4); dn = d.normalized()
    x = (l1 * l1 - l2 * l2 + dl * dl) / (2 * dl); y = math.sqrt(max(l1 * l1 - x * x, 0))
    perp = (bend - dn * bend.dot(dn)).normalized()
    knee = hp + dn * x + perp * y
    aim(f'upperleg.{s}', knee - hp)
    aim(f'lowerleg.{s}', hp + dn * dl - arm.pose.bones[f'lowerleg.{s}'].head)
    fr = arm_d.bones[f'foot.{s}'].matrix_local.to_3x3()
    set_world(f'foot.{s}', arm.pose.bones[f'foot.{s}'].head.copy(), fr)


LEG = (arm_d.bones['upperleg.l'].head_local - arm_d.bones['foot.l'].head_local).length


def pose(name):
    reset()
    if name == 'arms_down':
        for s, sg in SIDES:
            aim(f'upperarm.{s}', (0.10 * sg, 0.02, -1)); aim(f'lowerarm.{s}', (0.06 * sg, -0.05, -1))
    elif name == 'arms_up':
        for s, sg in SIDES:
            aim(f'upperarm.{s}', (0.35 * sg, 0, 1)); aim(f'lowerarm.{s}', (0.2 * sg, 0, 1))
    elif name in ('squat', 'stride'):
        drop = 0.38 * LEG if name == 'squat' else 0.10 * LEG
        hb = arm.pose.bones['hips']
        hrest = arm_d.bones['hips'].matrix_local
        lean = Matrix.Rotation(math.radians(15 if name == 'squat' else 5), 3, 'X')
        set_world('hips', hrest.translation + Vector((0, 0, -drop)), lean @ hrest.to_3x3())
        aim('spine', (0, -0.25 if name == 'squat' else -0.05, 1))
        for s, sg in SIDES:
            a = arm_d.bones[f'foot.{s}'].head_local.copy()
            if name == 'stride':
                a += Vector((0, -0.42 * LEG if s == 'l' else 0.42 * LEG, 0))
                bend = Vector((0, -1, 0))
            else:
                bend = Vector((0.35 * sg, -1, 0))
            leg_ik(s, a, bend)
        if name == 'squat':
            for s, sg in SIDES:
                aim(f'upperarm.{s}', (0.3 * sg, -1, -0.2)); aim(f'lowerarm.{s}', (0.1 * sg, -1, 0))


def hand_pose(kind):
    for s, _ in SIDES:
        for f in FING:
            for i in (1, 2, 3):
                pb = arm.pose.bones.get(f'{f}_0{i}.{s}')
                if pb is None:
                    continue
                if kind == 'fist':
                    a = (25, 45, 35)[i - 1] if f == 'thumb' else (80, 90, 70)[i - 1]
                elif kind == 'point':
                    a = 0 if f == 'index' else ((25, 45, 35)[i - 1] if f == 'thumb' else (80, 90, 70)[i - 1])
                else:
                    a = -8 if i == 1 else 0
                q = Quaternion((1, 0, 0), math.radians(a))
                if kind == 'open' and i == 1 and f != 'thumb':
                    q = Quaternion((0, 0, 1), math.radians({'index': 8, 'middle': 0, 'ring': -6, 'pinky': -12}[f])) @ q
                pb.rotation_quaternion = q
    upd()


# render setup
scene.render.engine = 'BLENDER_WORKBENCH'
sh = scene.display.shading
sh.light = 'STUDIO'
sh.show_cavity = True
orig_col = body.data.color_attributes.active_color
for ct in ('ATTRIBUTE', 'VERTEX'):
    try:
        sh.color_type = ct if orig_col is not None else 'MATERIAL'
        break
    except TypeError:
        pass
if scene.world is None:
    scene.world = bpy.data.worlds.new('W')
scene.world.color = (0.55, 0.57, 0.6)
scene.render.resolution_x = scene.render.resolution_y = 420
scene.render.film_transparent = False
cam_d = bpy.data.cameras.new('Cam'); cam_d.type = 'ORTHO'
cam = bpy.data.objects.new('Cam', cam_d); scene.collection.objects.link(cam); scene.camera = cam
cam_d.clip_start = 0.01; cam_d.clip_end = 100


def shoot(path, view, centre, scale):
    centre = Vector(centre)
    if view == 'front':
        cam.location = centre + Vector((0, -10, 0)); cam.rotation_euler = (math.radians(90), 0, 0)
    elif view == 'side':
        cam.location = centre + Vector((10, 0, 0)); cam.rotation_euler = (math.radians(90), 0, math.radians(90))
    elif view == 'side_r':
        cam.location = centre + Vector((-10, 0, 0)); cam.rotation_euler = (math.radians(90), 0, math.radians(-90))
    cam_d.ortho_scale = scale
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path


def sheet(paths_grid, out):
    rows = []
    for row in paths_grid:
        ims = []
        for p in row:
            im = bpy.data.images.load(p)
            w, h = im.size
            a = np.empty(w * h * 4, dtype=np.float32); im.pixels.foreach_get(a)
            ims.append(a.reshape(h, w, 4)); bpy.data.images.remove(im)
        rows.append(np.concatenate(ims, axis=1))
    wmax = max(r.shape[1] for r in rows)
    rows = [np.pad(r, ((0, 0), (0, wmax - r.shape[1]), (0, 0)), constant_values=1.0) for r in rows]
    a = np.concatenate(rows[::-1], axis=0)   # Blender images are bottom-up: first row on top
    img = bpy.data.images.new('sheet', a.shape[1], a.shape[0], alpha=True)
    img.pixels.foreach_set(a.ravel())
    img.filepath_raw = out; img.file_format = 'PNG'; img.save()
    bpy.data.images.remove(img)


def dom_colours(on):
    me = body.data
    if 'DomBone' not in me.color_attributes:
        rng = np.random.default_rng(3)
        pal = rng.uniform(0.15, 0.95, (len(DEF), 3))
        for n in DEF:   # fingers in strong distinct hues
            f = n.split('_0')[0]
            if f in FING:
                base = {'thumb': (1, .1, .1), 'index': (1, .8, 0), 'middle': (.1, .8, .2), 'ring': (.1, .4, 1), 'pinky': (.8, .2, 1)}[f]
                k = int(n.split('_0')[1][0])
                pal[bi[n]] = np.array(base) * (1.0 - 0.22 * (k - 1))
        c = np.ones((nv, 4)); c[:, :3] = (W[:, :, None] * pal[None]).sum(1)
        at = me.color_attributes.new('DomBone', 'FLOAT_COLOR', 'POINT')
        at.data.foreach_set('color', c.astype(np.float32).ravel())
    if on or orig_col is not None:
        me.color_attributes.active_color = me.color_attributes['DomBone'] if on else orig_col
        try:
            sh.color_type = 'ATTRIBUTE'
        except TypeError:
            sh.color_type = 'VERTEX'
    else:
        sh.color_type = 'MATERIAL'


POSES = ['rest', 'arms_down', 'arms_up', 'squat', 'stride']
REP['poses'] = {}
grid = {'front': [], 'side': []}
for pn in POSES:
    pose(pn) if pn != 'rest' else reset()
    mt, X = metrics()
    REP['poses'][pn] = mt
    log('pose', pn, mt)
    for vw in ('front', 'side'):
        grid[vw].append(shoot(os.path.join(OUT, f'pose_{pn}_{vw}.png'), vw, (0, 0, H * 0.5), H * 1.45))
sheet([grid['front'], grid['side']], os.path.join(OUT, 'sheet_poses.png'))
# weights view (dominant bone colours), rest pose
reset(); dom_colours(True)
wpaths = [shoot(os.path.join(OUT, f'weights_rest_{vw}.png'), vw, (0, 0, H * 0.5), H * 1.25) for vw in ('front', 'side')]
for vw in ('front', 'side'):
    pose('arms_up')
    wpaths.append(shoot(os.path.join(OUT, f'weights_armsup_{vw}.png'), vw, (0, 0, H * 0.5), H * 1.45))
    pose('squat')
    wpaths.append(shoot(os.path.join(OUT, f'weights_squat_{vw}.png'), vw, (0, 0, H * 0.5), H * 1.45))
sheet([wpaths[:2], wpaths[2:]], os.path.join(OUT, 'sheet_weights.png'))
# hands: fist, open, point; both hands close up, front and side, in true colours and in weight colours
hrows = []
REP['hands_posed'] = {}
for colours in (False, True):
    dom_colours(colours)
    for vw in ('front', 'side'):
        row = []
        for kind in ('open', 'fist', 'point'):
            reset(); hand_pose(kind)
            if not colours and vw == 'front':
                mt, X = metrics()
                # fingertip to palm-centre distance as a share of hand length
                d = {}
                for s, _ in SIDES:
                    hc = arm.pose.bones[f'handslot.{s}'].head
                    hl = (arm_d.bones[f'middle_03.{s}'].tail_local - arm_d.bones[f'hand.{s}'].head_local).length
                    d[s] = {f: round((arm.pose.bones[f'{f}_03.{s}'].tail - hc).length / hl, 2) for f in FING}
                REP['hands_posed'][kind] = {'metrics': mt, 'tip_to_palm_over_hand_length': d}
                log('hand', kind, mt, d)
            for s, sg in SIDES:
                hc = arm.pose.bones[f'hand.{s}'].head
                v = vw if vw == 'front' else ('side' if sg > 0 else 'side_r')
                row.append(shoot(os.path.join(OUT, f'hand_{kind}_{s}_{vw}{"_w" if colours else ""}.png'), v, hc, 0.30 * H))
        hrows.append(row)
sheet(hrows, os.path.join(OUT, 'sheet_hands.png'))
dom_colours(False)
save_report()

# export the rig (not used by the game; for review and the next step)
reset()
for o in scene.objects:
    o.select_set(o in (arm, body) or o in props)
try:
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, os.path.basename(CLASS_FBX).replace('.fbx', '_RM2.fbx')), use_selection=True,
                             object_types={'MESH', 'ARMATURE'}, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True, mesh_smooth_type='FACE',
                             add_leaf_bones=False, use_armature_deform_only=False, bake_anim=False)
except Exception as e:
    log('export failed', repr(e))

# ---------------------------------------------------------------- 5. retarget two shared clips (no foot IK)
try:
    kobjs, kacts = imp('Assets/_Game/Art/Characters/KayKit_Clips.fbx', anim=True)
    kk = next(o for o in kobjs if o.type == 'ARMATURE')
    for o in kobjs:
        if o.type != 'ARMATURE':
            bpy.data.objects.remove(o, do_unlink=True)
    acts = {a.name.split('|')[-1]: a for a in kacts}
    walk = acts.get('Walking_A') or next(a for n, a in acts.items() if 'walk' in n.lower())
    att = next((acts[n] for n in ('1H_Melee_Attack_Chop', '1H_Melee_Attack_Slice_Diagonal', '2H_Melee_Attack_Chop') if n in acts),
               next(a for n, a in acts.items() if 'attack' in n.lower()))
    BODY = [n for n in ('hips', 'spine', 'chest', 'head', 'upperarm.l', 'lowerarm.l', 'wrist.l', 'hand.l', 'upperarm.r', 'lowerarm.r',
                        'wrist.r', 'hand.r', 'upperleg.l', 'lowerleg.l', 'foot.l', 'toes.l', 'upperleg.r', 'lowerleg.r', 'foot.r', 'toes.r')
            if n in kk.data.bones and n in arm_d.bones]
    log('retarget bones', len(BODY), 'walk', walk.name, 'attack', att.name)
    Rk0 = {n: (kk.matrix_world @ kk.data.bones[n].matrix_local).to_3x3().normalized() for n in BODY}
    Rn0 = {n: arm_d.bones[n].matrix_local.to_3x3().normalized() for n in BODY}
    kleg = ((kk.matrix_world @ kk.data.bones['upperleg.l'].head_local) - (kk.matrix_world @ kk.data.bones['foot.l'].head_local)).length
    ratio = LEG / kleg
    Pk0 = kk.matrix_world @ kk.data.bones['hips'].head_local
    Pn0 = arm_d.bones['hips'].head_local.copy()
    log('leg ratio class/shared', round(ratio, 3))
    REP['retarget'] = {'leg_ratio': ratio, 'clips': {}}
    for act in (walk, att):
        ad = kk.animation_data or kk.animation_data_create()
        ad.action = act
        if ad.action_slot is None and len(act.slots):
            ad.action_slot = act.slots[0]
        f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
        reset()
        arm.animation_data_create(); arm.animation_data.action = None
        frames = list(range(f0, f1 + 1))
        lows, strs, tears, invs = [], [], [], []
        snaps = {}
        pick = set(frames[int(i * (len(frames) - 1) / 5)] for i in range(6))
        for f in frames:
            scene.frame_set(f)
            Rk = {n: (kk.matrix_world @ kk.pose.bones[n].matrix).to_3x3().normalized() for n in BODY}
            Pk = kk.matrix_world @ kk.pose.bones['hips'].head
            for n in BODY:
                R = Rk[n] @ Rk0[n].inverted() @ Rn0[n]
                loc = (Pn0 + (Pk - Pk0) * ratio) if n == 'hips' else arm.pose.bones[n].head.copy()
                set_world(n, loc, R)
            for n in BODY:
                pb = arm.pose.bones[n]
                pb.keyframe_insert('rotation_quaternion', frame=f)
                if n == 'hips':
                    pb.keyframe_insert('location', frame=f)
            mt, X = metrics()
            lows.append(mt['lowest_z']); strs.append(mt['stretch_p99']); tears.append(mt['tear_edges']); invs.append(mt['inverted_faces'])
            if f in pick:
                snaps[f] = [shoot(os.path.join(OUT, f'clip_{act.name.split("|")[-1]}_{f:03d}_{vw}.png'), vw, (0, 0, H * 0.5), H * 1.45)
                            for vw in ('front', 'side')]
        nm = act.name.split('|')[-1]
        arm.animation_data.action.name = 'RM2_' + nm
        REP['retarget']['clips'][nm] = {'frames': len(frames), 'lowest_z_min': min(lows), 'lowest_z_max': max(lows),
                                        'stretch_p99_worst': max(strs), 'tear_edges_worst': max(tears), 'inverted_worst': max(invs)}
        log('clip', nm, REP['retarget']['clips'][nm])
        ks = sorted(snaps)
        sheet([[snaps[k][0] for k in ks], [snaps[k][1] for k in ks]], os.path.join(OUT, f'strip_{nm}.png'))
        save_report()
except Exception:
    log('retarget failed', traceback.format_exc()[-1500:])
REP['seconds'] = round(time.time() - T0, 1)
save_report()
log('done')
