# rig-builder-4 (2026-11-14): check a model FBX against its separate clip FBX the way Unity uses them.
#   blender -b -P fbx_pair_check.py -- <out> <Cls> <model.fbx> <clips.fbx>
# Unity binds the mesh to the MODEL file's skeleton and plays the CLIP file's curves on it as absolute local transforms,
# matched by bone name. This script loads the two files into one empty scene (nothing from a baking scene) and reports:
#   bones: names, order and parents in each file, rest (bind) matrices that differ, bones missing on either side;
#   clips: per clip the largest scale away from 1, the largest deform-bone translation from the rest, any NaN or Inf key;
#   meshes: per mesh vertices, faces, weight sums, influences, groups that name no bone, and for every vertex how far it
#     sits from the bones it is weighted to (a LOD vertex weighted to a far bone tears away when that bone moves);
#   renders: every mesh (Hero, LOD0, LOD1, Shadow) side by side in bind, Idle and Run frames, the model's bones set to
#     the clip skeleton's LOCAL transforms (matrix_basis copied bone by bone, Unity's absolute-local playback).
import bpy, sys, os, json, math
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index('--') + 1:]
OUT, CLS, MODEL, CLIPS = argv[0], argv[1], argv[2], argv[3]
os.makedirs(OUT, exist_ok=True)
REP = {'model': MODEL, 'clips': CLIPS}


def save():
    json.dump(REP, open(os.path.join(OUT, f'pair_{CLS}.json'), 'w'), indent=1, default=str)


def imp(path, anim):
    bo, ba = set(bpy.data.objects), set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=path, use_anim=anim, use_custom_normals=False, ignore_leaf_bones=False,
                             automatic_bone_orientation=False, primary_bone_axis='Y', secondary_bone_axis='X')
    return [o for o in bpy.data.objects if o not in bo], [a for a in bpy.data.actions if a not in ba]


def assign(arm, act):
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = act
    if act is not None:
        try:
            if ad.action_slot is None and len(act.slots):
                ad.action_slot = act.slots[0]
        except Exception:
            pass


bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
mobjs, mact = imp(MODEL, True)
ma = next(o for o in mobjs if o.type == 'ARMATURE')
meshes = sorted([o for o in mobjs if o.type == 'MESH'], key=lambda o: o.name)
for a in mact:
    a.use_fake_user = True
REP['model_actions'] = [a.name for a in mact]
assign(ma, None)
cobjs, cacts = imp(CLIPS, True)
ca = next(o for o in cobjs if o.type == 'ARMATURE')
for o in cobjs:
    if o is not ca:
        bpy.data.objects.remove(o, do_unlink=True)
acts = {a.name.split('|')[-1]: a for a in cacts}
for a in cacts:
    a.use_fake_user = True
assign(ca, None)

# ---- bones
mb, cb = [b.name for b in ma.data.bones], [b.name for b in ca.data.bones]
REP['bones'] = dict(model_count=len(mb), clips_count=len(cb), same_order=mb == cb,
                    only_model=[n for n in mb if n not in cb], only_clips=[n for n in cb if n not in mb],
                    parent_diff=[n for n in mb if n in cb and ((ma.data.bones[n].parent.name if ma.data.bones[n].parent else None)
                                                             != (ca.data.bones[n].parent.name if ca.data.bones[n].parent else None))])
rd = {}
for n in mb:
    if n not in cb:
        continue
    A = ma.data.bones[n].matrix_local; B = ca.data.bones[n].matrix_local
    ang = math.degrees(A.to_quaternion().rotation_difference(B.to_quaternion()).angle)
    dt = (A.translation - B.translation).length
    sc = max(abs(x - y) for x, y in zip(A.to_scale(), B.to_scale()))
    if ang > 0.05 or dt > 1e-4 or sc > 1e-4:
        rd[n] = [round(ang, 3), round(dt, 5), round(sc, 5)]
REP['bones']['rest_diff_deg_m_scale'] = rd
REP['armature_world'] = dict(model=[list(map(lambda v: round(v, 4), r)) for r in ma.matrix_world],
                             clips=[list(map(lambda v: round(v, 4), r)) for r in ca.matrix_world])
save()

# ---- clips: scale, translation, NaN
cl = {}
for name, a in acts.items():
    mx_s, mx_t, bad, bt = 0.0, 0.0, 0, ''
    for fc in a.fcurves if hasattr(a, 'fcurves') else []:
        pass
    # slotted actions (5.x): walk the channelbag curves
    curves = []
    try:
        for lay in a.layers:
            for st in lay.strips:
                for cbg in st.channelbags:
                    curves += list(cbg.fcurves)
    except Exception:
        curves = list(getattr(a, 'fcurves', []))
    for fc in curves:
        dp = fc.data_path
        if '"' not in dp:
            continue
        bn = dp.split('"')[1]
        vals = np.array([k.co[1] for k in fc.keyframe_points], float)
        if len(vals) == 0:
            continue
        if not np.all(np.isfinite(vals)):
            bad += 1
        if dp.endswith('scale'):
            d = float(np.abs(vals - 1).max())
            if d > mx_s:
                mx_s, bs = d, bn
        if dp.endswith('location') and bn not in ('root', 'hips'):
            d = float(np.abs(vals).max())
            if d > mx_t:
                mx_t, bt = d, bn
    cl[name] = dict(curves=len(curves), max_scale_off=round(mx_s, 5), max_loc=round(mx_t, 4), max_loc_bone=bt, nonfinite=bad)
REP['clips'] = cl
save()

# ---- meshes: weights and distance to the weighted bones (bind)
def seg_d(P, a, b):
    ab = b - a; t = np.clip(((P - a) @ ab) / max(1e-12, ab @ ab), 0, 1)
    return np.linalg.norm(P - (a + t[:, None] * ab), axis=1)


segs = {b.name: (np.array(ma.matrix_world @ b.head_local), np.array(ma.matrix_world @ b.tail_local)) for b in ma.data.bones}
mr = {}
for o in meshes:
    me = o.data
    co = np.empty(len(me.vertices) * 3); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
    M = np.array(o.matrix_world); P = co @ M[:3, :3].T + M[:3, 3]
    gi = {g.index: g.name for g in o.vertex_groups}
    sums, infl, far, worst, nob = [], [], 0, (0, ''), set()
    for v in me.vertices:
        s_ = 0; n_ = 0
        for g in v.groups:
            if g.weight <= 1e-4:
                continue
            nm = gi.get(g.group, '?')
            if nm not in segs:
                nob.add(nm); continue
            s_ += g.weight; n_ += 1
            if g.weight > 0.2:
                d = float(seg_d(P[v.index:v.index + 1], *segs[nm])[0])
                if d > 0.45:
                    far += 1
                if d > worst[0]:
                    worst = (round(d, 3), nm)
        sums.append(s_); infl.append(n_)
    sums = np.array(sums)
    mods = [(m.type, getattr(m, 'object', None) and m.object.name) for m in o.modifiers]
    mr[o.name] = dict(verts=len(me.vertices), faces=len(me.polygons), weight_sum_min=round(float(sums.min()), 3),
                      weight_sum_max=round(float(sums.max()), 3), zero_weight=int((sums < 1e-4).sum()),
                      max_infl=int(max(infl)), groups_not_bones=sorted(nob), far_weighted_verts_over_045=far,
                      worst_far=worst, parent=o.parent.name if o.parent else None, modifiers=mods,
                      matrix_world_scale=[round(x, 4) for x in o.matrix_world.to_scale()])
REP['meshes'] = mr
save()

# ---- renders: Unity-style absolute local playback from the clip file onto the model's bones
for o in meshes:
    if not any(m.type == 'ARMATURE' for m in o.modifiers):
        am = o.modifiers.new('Armature', 'ARMATURE'); am.object = ma
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
for ct in ('VERTEX', 'ATTRIBUTE', 'MATERIAL'):
    try:
        scene.display.shading.color_type = ct; break
    except TypeError:
        continue
scene.display.shading.background_type = 'VIEWPORT'
scene.display.shading.background_color = (0.55, 0.6, 0.65)
scene.render.resolution_x, scene.render.resolution_y = 300, 360
cd = bpy.data.cameras.new('cam'); cd.type = 'ORTHO'; cd.ortho_scale = 2.8
cam = bpy.data.objects.new('cam', cd); scene.collection.objects.link(cam); scene.camera = cam


def pose_from_clip(act, f):
    assign(ca, act); scene.frame_set(f); bpy.context.view_layer.update()
    for pb in ma.pose.bones:
        c = ca.pose.bones.get(pb.name)
        pb.matrix_basis = c.matrix_basis.copy() if c is not None else Matrix()
    bpy.context.view_layer.update()


def shot(p):
    scene.render.filepath = p; bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(p); px = np.array(img.pixels[:], np.float32).reshape(img.size[1], img.size[0], 4)
    bpy.data.images.remove(img); os.remove(p); return px


rows = []
states = [('bind', None, 0)]
for c_ in ('Idle', 'Run', 'Walk'):
    if c_ in acts:
        a = acts[c_]; f0, f1 = int(a.frame_range[0]), int(a.frame_range[1])
        states.append((c_, a, f0 + (f1 - f0) // 3))
for tag, act, f in states:
    if act is None:
        for pb in ma.pose.bones:
            pb.matrix_basis = Matrix()
        bpy.context.view_layer.update()
    else:
        pose_from_clip(act, f)
    row = []
    for o in meshes:
        for o2 in meshes:
            o2.hide_render = o2 is not o
        cam.location = Vector((-6, 0, 1.15)); cam.rotation_euler = (math.radians(90), 0, math.radians(-90))
        row.append(shot(os.path.join(OUT, f'_p_{tag}_{o.name}.png')))
    rows.append(np.concatenate(row, axis=1))
sheet = np.concatenate(rows[::-1], axis=0)
img = bpy.data.images.new('pair', sheet.shape[1], sheet.shape[0]); img.pixels = sheet.ravel()
img.filepath_raw = os.path.join(OUT, f'pair_{CLS}.png'); img.file_format = 'PNG'; img.save()
REP['sheet'] = dict(path=img.filepath_raw, rows=[s[0] for s in states], columns=[o.name for o in meshes])
save()
print('[pair]', json.dumps({k: REP[k] for k in ('bones', 'clips', 'meshes')})[:4000], flush=True)
