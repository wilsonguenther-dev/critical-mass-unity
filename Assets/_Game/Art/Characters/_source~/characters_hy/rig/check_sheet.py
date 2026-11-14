# rig-demolitions-1 (2026-11-14): one check sheet for a rigged generated body, rendered on GitHub (Workbench).
#   blender -b -P check_sheet.py -- <out> <Cls> [<Cls>.fbx] [<Cls>_Clips.fbx]
# Rows, top to bottom: bind pose front | side | back | three-quarter; Idle first frame in the same four views;
# Walk strip (side, 6 frames); Run strip (side, 6); attack strip (three-quarter, 6); skill strip (side, 6);
# reactions (three-quarter): Hit mid, HitHeavy mid, KnockDown end, DeathB end (knockout), GetUp mid, Death end.
# The clips play on the clip file's own skeleton and the model copies it in world space (what Unity does).
# The game's light weapon is stood in by the same 0.62 u stick bake_clips.py uses, on handslot.r.
# Writes <out>/check_<Cls>.png and <out>/check_<Cls>.json (idle sole heights, lean, weapon direction, height).
import bpy, sys, os, json, math
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index('--') + 1:]
OUT, CLS = argv[0], argv[1]
FBX = argv[2] if len(argv) > 2 and argv[2] else os.path.join(OUT, CLS + '.fbx')
CLIPS = argv[3] if len(argv) > 3 and argv[3] else os.path.join(OUT, CLS + '_Clips.fbx')
ATTACK = {'Demolitions': 'Attack', 'Builder': 'Attack', 'Healer': 'Cast'}.get(CLS, 'Attack')
SKILL = {'Demolitions': 'PickUp', 'Builder': 'UseItem', 'Healer': 'UseItem'}.get(CLS, 'UseItem')
REP = {'class': CLS, 'fbx': FBX, 'clips': CLIPS}


def save():
    json.dump(REP, open(os.path.join(OUT, f'check_{CLS}.json'), 'w'), indent=1, default=str)


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


def upd():
    bpy.context.view_layer.update()


bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
if not (os.path.exists(FBX) and os.path.exists(CLIPS)):
    REP['error'] = 'missing input'; save(); raise SystemExit(0)
cobjs, cacts = imp(CLIPS, True)
ca = next(o for o in cobjs if o.type == 'ARMATURE')   # the clip file's own skeleton plays the clips
for o in cobjs:
    if o is not ca:
        bpy.data.objects.remove(o, do_unlink=True)
acts = {a.name.split('|')[-1]: a for a in cacts}
fobjs, _ = imp(FBX, False)
fa = next(o for o in fobjs if o.type == 'ARMATURE')
hero = next(o for o in fobjs if o.type == 'MESH' and o.name.startswith(CLS + '_Hero'))
for o in fobjs:
    if o.type == 'MESH' and o is not hero:
        bpy.data.objects.remove(o, do_unlink=True)
for a in acts.values():
    a.use_fake_user = True
assign(fa, None)
assign(ca, None)
# Unity plays a clip file's curves as ABSOLUTE local transforms on the model's bones (matched by path), not as offsets from
# the model's own rest. To see what the game sees, the model's bones copy the clip skeleton's bones in world space.
ca.matrix_world = fa.matrix_world.copy()
CONS = []
for pb in fa.pose.bones:
    if pb.name in ca.pose.bones:
        c_ = pb.constraints.new('COPY_TRANSFORMS'); c_.target = ca; c_.subtarget = pb.name
        c_.target_space = c_.owner_space = 'WORLD'
        c_.mute = True; CONS.append(c_)   # off for the bind row (the model's own bind pose)
ca.hide_render = True
REP['rest_diff_deg'] = {}
for b_ in fa.data.bones:
    if b_.name in ca.data.bones:
        q1 = (fa.matrix_world @ b_.matrix_local).to_quaternion(); q2 = (ca.matrix_world @ ca.data.bones[b_.name].matrix_local).to_quaternion()
        d_ = math.degrees(q1.rotation_difference(q2).angle)
        if d_ > 1:
            REP['rest_diff_deg'][b_.name] = round(d_, 1)
for pb in fa.pose.bones:
    pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.rotation_euler = (0, 0, 0); pb.scale = (1, 1, 1)
upd()
REP['clips'] = sorted(acts)

# stand-in weapon on handslot.r (handle along the slot's +Y, 0.62 long, held 0.12 from its end), head cube at the far end
bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.025, depth=0.62)
stick = bpy.context.active_object
bpy.ops.mesh.primitive_cube_add(size=0.12, location=(0, 0, 0.31))
hd = bpy.context.active_object
for o in scene.objects:
    o.select_set(o in (stick, hd))
bpy.context.view_layer.objects.active = stick
bpy.ops.object.join()
stick.data.transform(Matrix.Rotation(-math.pi / 2, 4, 'X'))
stick.parent = fa; stick.parent_type = 'BONE'; stick.parent_bone = 'handslot.r'
upd()
stick.matrix_world = (fa.matrix_world @ fa.pose.bones['handslot.r'].matrix) @ Matrix.Translation((0, 0.31 - 0.12, 0))
upd()

bpy.ops.mesh.primitive_plane_add(size=8, location=(0, 0, 0))
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
for ct in ('VERTEX', 'ATTRIBUTE', 'MATERIAL'):
    try:
        scene.display.shading.color_type = ct
        break
    except TypeError:
        continue
scene.display.shading.show_cavity = False
scene.display.shading.background_type = 'VIEWPORT'
scene.display.shading.background_color = (0.55, 0.6, 0.65)
scene.render.resolution_x, scene.render.resolution_y = 260, 360
cd = bpy.data.cameras.new('cam'); cd.type = 'ORTHO'; cd.ortho_scale = 2.7
cam = bpy.data.objects.new('cam', cd); scene.collection.objects.link(cam); scene.camera = cam


def view(deg):
    t = math.radians(deg)
    cam.location = Vector((6 * math.sin(t), -6 * math.cos(t), 1.95))
    cam.rotation_euler = (math.radians(82), 0, t)


VIEWS = {'front': 0, 'side': -90, 'back': 180, 'q34': -45}   # side and q34 from the weapon (right) side


def shot(tag):
    p = os.path.join(OUT, f'_c_{tag}.png')
    scene.render.filepath = p
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(p)
    px = np.array(img.pixels[:], np.float32).reshape(img.size[1], img.size[0], 4)
    bpy.data.images.remove(img); os.remove(p)
    return px


def frames(act, n):
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    return [f0 + int(round((f1 - f0) * k / max(1, n - 1))) for k in range(n)] if n > 1 else [f0]


def eval_verts():
    dg = bpy.context.evaluated_depsgraph_get()
    eo = hero.evaluated_get(dg)
    me = eo.to_mesh()
    a = np.empty(len(me.vertices) * 3); me.vertices.foreach_get('co', a)
    eo.to_mesh_clear()
    a = a.reshape(-1, 3)
    M = np.array(hero.matrix_world)
    return a @ M[:3, :3].T + M[:3, 3]


# dominant group per vertex (for the sole check)
gi = {g.index: g.name for g in hero.vertex_groups}
dom = np.full(len(hero.data.vertices), '', dtype=object)
for v in hero.data.vertices:
    if v.groups:
        g = max(v.groups, key=lambda x: x.weight)
        dom[v.index] = gi.get(g.group, '')


def measure(tag):
    P = eval_verts()
    m = {'top_z': round(float(P[:, 2].max()), 3), 'min_z': round(float(P[:, 2].min()), 3)}
    for s in ('l', 'r'):
        sel = np.isin(dom, [f'foot.{s}', f'toes.{s}'])
        if sel.sum() < 10:
            continue
        F = P[sel]
        y0, y1 = np.percentile(F[:, 1], 25), np.percentile(F[:, 1], 75)
        m[f'toe_min_z.{s}'] = round(float(F[F[:, 1] <= y0, 2].min()), 3)   # -Y = front
        m[f'heel_min_z.{s}'] = round(float(F[F[:, 1] >= y1, 2].min()), 3)
    W = lambda n: fa.matrix_world @ fa.pose.bones[n].head
    h, c, hd_ = W('hips'), W('chest'), W('head')
    m['lean_deg'] = round(math.degrees(math.atan2(math.hypot(hd_.x - h.x, hd_.y - h.y), hd_.z - h.z)), 1)
    Ms = stick.matrix_world
    a, b = Ms @ Vector((0, -0.31, 0)), Ms @ Vector((0, 0.31, 0))
    d = (b - a).normalized()
    m['weapon_up_deg'] = round(math.degrees(math.acos(max(-1, min(1, d.z)))), 1)   # 0 = straight up
    m['grip_to_hand_m'] = round(((Ms @ Vector((0, -0.31 + 0.12, 0))) - (fa.matrix_world @ fa.pose.bones['hand.r'].head)).length, 3)
    REP.setdefault('measure', {})[tag] = m


rows = []
row = []
for vn, deg in VIEWS.items():
    view(deg); row.append(shot('bind_' + vn))
rows.append(row)
measure('bind')
for c_ in CONS:
    c_.mute = False
upd()
if 'Idle' in acts:
    assign(ca, acts['Idle']); scene.frame_set(int(acts['Idle'].frame_range[0])); upd()
    measure('Idle')
    row = []
    for vn, deg in VIEWS.items():
        view(deg); row.append(shot('idle_' + vn))
    rows.append(row)
for clip, vn in (('Walk', 'side'), ('Run', 'side'), (ATTACK, 'q34'), (SKILL, 'side')):
    if clip not in acts:
        REP.setdefault('missing', []).append(clip); continue
    assign(ca, acts[clip]); view(VIEWS[vn])
    row = []
    for k, f in enumerate(frames(acts[clip], 6)):
        scene.frame_set(f); upd()
        if k == 3:
            measure(clip + '_mid')
        row.append(shot(f'{clip}_{k}'))
    rows.append(row)
row = []
view(VIEWS['q34'])
for clip, at in (('Hit', 0.5), ('HitHeavy', 0.5), ('KnockDown', 1.0), ('DeathB', 1.0), ('GetUp', 0.5), ('Death', 1.0)):
    if clip not in acts:
        REP.setdefault('missing', []).append(clip); continue
    assign(ca, acts[clip])
    f0, f1 = acts[clip].frame_range
    scene.frame_set(int(f0 + (f1 - f0) * at)); upd()
    row.append(shot(f'react_{clip}'))
if row:
    rows.append(row)
save()

# compose: rows top to bottom (Blender pixels are bottom-up), a dark gap between tiles
gap = 6
strips = []
for r in rows:
    parts = []
    for t in r:
        parts += [t, np.zeros((t.shape[0], gap, 4), np.float32) + np.array([0.1, 0.1, 0.1, 1], np.float32)]
    strips.append(np.concatenate(parts[:-1], axis=1))
wmax = max(s.shape[1] for s in strips)
pad = []
for s in strips:
    if s.shape[1] < wmax:
        s = np.concatenate([s, np.ones((s.shape[0], wmax - s.shape[1], 4), np.float32) * np.array([0.1, 0.1, 0.1, 1], np.float32)], axis=1)
    pad += [s, np.ones((gap, wmax, 4), np.float32) * np.array([0.1, 0.1, 0.1, 1], np.float32)]
sheet = np.concatenate(pad[:-1][::-1], axis=0)
img = bpy.data.images.new(f'check_{CLS}', sheet.shape[1], sheet.shape[0])
img.pixels = sheet.ravel()
img.filepath_raw = os.path.join(OUT, f'check_{CLS}.png')
img.file_format = 'PNG'
img.save()
REP['sheet'] = img.filepath_raw
save()
print('[check]', json.dumps(REP.get('measure', {}))[:3000], flush=True)
