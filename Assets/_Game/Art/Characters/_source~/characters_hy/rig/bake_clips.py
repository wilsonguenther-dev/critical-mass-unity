"""Per-class clip bake (rigs-2, 2026-11-13): retarget the shared KayKit clip pack onto one class's own skeleton with
foot IK, so the soles track the ground through every frame, then export <Cls>_Clips.fbx (armature + one take per clip).

Why: Unity applies the KayKit clips' rotations to each class's own joints. A class whose legs are shorter and whose
upper-leg joints sit lower (HY Farmer: legs 0.714 of KayKit, pelvis 0.149 m lower in the hips frame) lifts its feet
0.12 to 0.19 m in Walk. Scaling the hips translation alone does not fix that.

Per frame:
  1. Every bone copies the KayKit pose (matrix_basis; the rest orientations match). Deform-bone translations are dropped.
  2. Hips head = class rest + (KayKit hips motion) x leg ratio + ground lift (puts the bind-pose soles on z = 0).
  3. Each ankle target = class rest ankle + (KayKit ankle motion) x leg ratio + lift. The foot turns as KayKit's foot
     turns from its rest. While KayKit's foot is planted the class's sole is put exactly on the ground; otherwise it is
     only kept from going under it.
  4. If a target is out of reach the hips come down by the shortfall (never a straight-leg snap).
  5. Analytic two-bone IK, with the knee kept in the plane of the FK knee (no knee pop).
Measures (Idle, Walk, Run): per frame and per foot, the lowest foot vertex of the Hero mesh, the KayKit contact
flag, and planted-foot slide over the cycle at the class's ground speed. Renders: front and side Walk strips with a
ground line. Usage: blender -b --python bake_clips.py -- <out> <repo> <Class>
"""
import bpy, sys, os, json, math
import numpy as np
from mathutils import Vector, Matrix, Quaternion

argv = sys.argv[sys.argv.index("--") + 1:]
OUT, REPO = argv[0], argv[1]
CLS = argv[2] if len(argv) > 2 else "Farmer"
FBX = {"Farmer": "Assets/_Game/Art/Farmer/Farmer.fbx", "Miner": "Assets/_Game/Art/Miner/_hy~/Miner.fbx",
       "Warrior": "Assets/_Game/Art/Characters/Warrior.fbx"}[CLS]   # rig-warrior-1: the HY Warrior (art/characters-hy-rig ef9d2eb)
CLIPS = "Assets/_Game/Art/Characters/KayKit_Clips.fbx"
GROUND_SPEED = {}   # clip -> KayKit ground speed (m/s), from Resources/Anim/clip_facts.json
os.makedirs(OUT, exist_ok=True)
try:
    _cf = json.load(open(os.path.join(REPO, "Assets/_Game/Resources/Anim/clip_facts.json")))
    _cf = _cf.get("clips", _cf) if isinstance(_cf, dict) else _cf
    for _e in _cf:
        if isinstance(_e, dict) and _e.get("groundSpeed", 0) > 0.05:
            GROUND_SPEED[_e["clip"]] = float(_e["groundSpeed"])
except Exception as _x:
    print("[bake] clip facts unreadable", _x)
REP = {"class": CLS}
LOG = []


def log(*a):
    s = " ".join(str(x) for x in a)
    print("[bake]", s, flush=True)
    LOG.append(s)


def imp(path, anim):
    bo, ba = set(bpy.data.objects), set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=os.path.join(REPO, path), use_anim=anim, use_custom_normals=False,
                             ignore_leaf_bones=False, automatic_bone_orientation=False,
                             primary_bone_axis="Y", secondary_bone_axis="X")
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
kobjs, kacts = imp(CLIPS, True)
kk = next(o for o in kobjs if o.type == "ARMATURE")
for o in kobjs:
    if o.type != "ARMATURE":
        bpy.data.objects.remove(o, do_unlink=True)
fps = scene.render.fps / scene.render.fps_base
fobjs, _ = imp(FBX, False)
fa = next(o for o in fobjs if o.type == "ARMATURE")
meshes = [o for o in fobjs if o.type == "MESH"]
hero = next(o for o in meshes if o.name.startswith(CLS + "_Hero"))
for o in meshes:
    if o is not hero:
        bpy.data.objects.remove(o, do_unlink=True)
assign(fa, None)
acts = {a.name.split("|")[-1]: a for a in kacts}
log("fps", fps, "kk", kk.name, "class rig", fa.name, "clips", len(acts), sorted(acts)[:8])

SIDES = ("l", "r")
PLANT = 0.025          # KayKit ankle within 2.5% of leg length of its rest height = planted
MAX_LOWER = 0.12       # hips never come down more than this to reach a target (DeathB-style clips keep FK reach)
LEG = lambda s: ("upperleg." + s, "lowerleg." + s, "foot." + s, "toes." + s)
W = lambda arm, p: arm.matrix_world @ p


def rest(arm):
    assign(arm, None)
    for pb in arm.pose.bones:
        pb.matrix_basis.identity()
    upd()


def snap(arm):
    P = arm.pose.bones
    d = {"hips": W(arm, P["hips"].head)}
    for s in SIDES:
        up, lo, ft, _ = LEG(s)
        d["U" + s] = W(arm, P[up].head)
        d["K" + s] = W(arm, P[lo].head)
        d["A" + s] = W(arm, P[ft].head)
        d["R" + s] = (arm.matrix_world @ P[ft].matrix).to_3x3().normalized()
        d["M" + s] = arm.matrix_world @ P[ft].matrix
    return d


rest(kk)
K0 = snap(kk)
rest(fa)
F0 = snap(fa)
Lt = {s: (F0["K" + s] - F0["U" + s]).length for s in SIDES}
Ls = {s: (F0["A" + s] - F0["K" + s]).length for s in SIDES}
leg_k = sum((K0["A" + s] - K0["U" + s]).length for s in SIDES) / 2
leg_f = sum((F0["A" + s] - F0["U" + s]).length for s in SIDES) / 2
RATIO = leg_f / leg_k

# Foot vertex sets of the Hero (dominant group), bind == rest.
vg = {g.index: g.name for g in hero.vertex_groups}
nv = len(hero.data.vertices)
dom = np.full(nv, -1, np.int32)
for v in hero.data.vertices:
    best, bw = -1, 0.0
    for g in v.groups:
        if g.weight > bw:
            best, bw = g.group, g.weight
    dom[v.index] = best
# The mesh as the armature REST pose deforms it (the Miner's rest differs from its A-pose bind), so the ground lift and
# the sole offsets hold for whatever the rest does to the legs.
rest(fa)
_eo = hero.evaluated_get(bpy.context.evaluated_depsgraph_get())
_me = _eo.to_mesh()
co = np.empty(len(_me.vertices) * 3, np.float32)
_me.vertices.foreach_get("co", co)
_eo.to_mesh_clear()
co = co.reshape(-1, 3)
mw = np.array(hero.matrix_world)
wco = co @ mw[:3, :3].T + mw[:3, 3]
FOOTV = {s: np.array([i for i in range(nv) if vg.get(dom[i], "") in ("foot." + s, "toes." + s)]) for s in SIDES}
SOLEV = {s: np.array([i for i in range(nv) if vg.get(dom[i], "") == "foot." + s]) for s in SIDES}
LIFT = -float(min(wco[FOOTV[s]][:, 2].min() for s in SIDES))
OFF = {}
for s in SIDES:
    inv = np.array(F0["M" + s].inverted())
    p = wco[SOLEV[s]]
    OFF[s] = p @ inv[:3, :3].T + inv[:3, 3]
def _fwd(arm):
    P = arm.pose.bones
    t = W(arm, P["toes.l"].head) - W(arm, P["foot.l"].head)
    h = (arm.matrix_world @ P["hips"].matrix).to_3x3() @ Vector((0, 0, 1))
    return [round(x, 3) for x in t.normalized()], [round(x, 3) for x in h.normalized()]
rest(kk); _kf = _fwd(kk); rest(fa); _ff = _fwd(fa)
REP["facing"] = dict(kk_world=[list(map(lambda v: round(v, 3), r)) for r in kk.matrix_world],
                     class_world=[list(map(lambda v: round(v, 3), r)) for r in fa.matrix_world],
                     kk_toe_dir=_kf[0], class_toe_dir=_ff[0], kk_hips_z=_kf[1], class_hips_z=_ff[1])
log("facing", REP["facing"])
# The two rigs share bone frames but not joint offsets: KayKit's toes sit 19 degrees out from its ankle, the HY Farmer's
# straight ahead, so the same foot rotation pointed the Farmer's toes 19 degrees further in than KayKit's (Run toe-off read
# 36 to 48 degrees off forward in the game's facing tests). Turn each class foot at rest so its toes yaw as KayKit's do.
def _toe_yaw(arm, s):
    P = arm.pose.bones
    t = W(arm, P["toes." + s].head) - W(arm, P["foot." + s].head)
    return math.atan2(t.x, -t.y)
rest(kk); _yk = {s: _toe_yaw(kk, s) for s in SIDES}
rest(fa); _yf = {s: _toe_yaw(fa, s) for s in SIDES}
TOEFIX = {s: Matrix.Rotation(_yk[s] - _yf[s], 3, "Z") for s in SIDES}
REP["toe_yaw_rest_deg"] = {s: dict(kaykit=round(math.degrees(_yk[s]), 1), cls=round(math.degrees(_yf[s]), 1)) for s in SIDES}
log("toe yaw at rest", REP["toe_yaw_rest_deg"])
REP.update(ratio=RATIO, lift=LIFT, leg_class=leg_f, leg_kaykit=leg_k, thigh=Lt, shin=Ls,
           foot_vertices={s: int(len(FOOTV[s])) for s in SIDES})
log("leg ratio", round(RATIO, 4), "ground lift", round(LIFT, 4), "thigh", Lt, "shin", Ls)


def sole_z(s, A, R):
    S0 = F0["M" + s].to_scale()
    M = np.array(Matrix.LocRotScale(A, R.to_quaternion(), S0))
    p = OFF[s] @ M[:3, :3].T + M[:3, 3]
    return float(p[:, 2].min())


def set_world(arm, pb, Mw):
    pb.matrix = arm.matrix_world.inverted() @ Mw


def rotate_about(arm, pb, pivot, q):
    Mw = arm.matrix_world @ pb.matrix
    T = Matrix.Translation(pivot) @ q.to_matrix().to_4x4() @ Matrix.Translation(-pivot)
    set_world(arm, pb, T @ Mw)


KEEP_LOC = {"root", "hips"}
names = [pb.name for pb in fa.pose.bones]
kkP, faP = kk.pose.bones, fa.pose.bones


def kk_back_dir(act, f0, f1):
    """Direction the KayKit planted feet move in the clip (opposite to travel), or None."""
    acc, prev = Vector((0, 0, 0)), {s: None for s in SIDES}
    for f in range(f0, f1 + 1):
        scene.frame_set(f)
        for s in SIDES:
            a = W(kk, kkP["foot." + s].head)
            planted = (a.z - K0["A" + s].z) < PLANT * leg_k
            if planted and prev[s] is not None:
                acc += a - prev[s]
            prev[s] = a.copy() if planted else None
    acc.z = 0
    return acc.normalized() if acc.length > 1e-6 else None


def bake_action(clip, act):
    assign(kk, act)
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    # Planted feet hold: while planted, a foot moves backward at exactly the class's ground speed (KayKit speed x leg
    # ratio, the speed the game moves it at), so it does not slide on the ground.
    vg_ = GROUND_SPEED.get(clip, 0.0) * RATIO
    bdir = kk_back_dir(act, f0, f1) if vg_ > 0 else None
    anchor = {s: None for s in SIDES}
    rec = {n: [] for n in names}
    contact = {s: [] for s in SIDES}
    prev_v = {s: None for s in SIDES}
    lowered = 0.0
    for f in range(f0, f1 + 1):
        scene.frame_set(f)
        for pb in faP:
            k = kkP.get(pb.name)
            if k is None:
                pb.matrix_basis.identity()
                continue
            mb = k.matrix_basis.copy()
            if pb.name not in KEEP_LOC:
                mb.translation = Vector((0, 0, 0))
            pb.matrix_basis = mb
        upd()
        K = snap(kk)
        Hd = F0["hips"] + (K["hips"] - K0["hips"]) * RATIO + Vector((0, 0, LIFT))
        targets, rots = {}, {}
        for s in SIDES:
            R = K["R" + s] @ K0["R" + s].inverted() @ TOEFIX[s] @ F0["R" + s]
            dk = K["A" + s] - K0["A" + s]
            # Foot motion scales with the legs (an unscaled swing lift was tried: Walk's lowest foot rose to 0.043).
            A = F0["A" + s] + dk * RATIO + Vector((0, 0, LIFT))
            planted = (K["A" + s].z - K0["A" + s].z) < PLANT * leg_k
            if planted and bdir is not None:
                if anchor[s] is None:
                    anchor[s] = (A.copy(), f)
                a0, fa0 = anchor[s]
                A.x, A.y = (a0 + bdir * vg_ * (f - fa0) / fps).x, (a0 + bdir * vg_ * (f - fa0) / fps).y
            elif not planted:
                anchor[s] = None
            z = sole_z(s, A, R)
            if planted:
                A.z -= z
            elif z < 0:
                A.z -= z
            contact[s].append(bool(planted))
            targets[s], rots[s] = A, R
        lowered_frame = 0.0
        for it in range(3):
            hp = faP["hips"]
            Mw = fa.matrix_world @ hp.matrix
            Mw.translation = Hd
            set_world(fa, hp, Mw)
            upd()
            short = 0.0
            for s in SIDES:
                U = W(fa, faP["upperleg." + s].head)
                short = max(short, (targets[s] - U).length - 0.995 * (Lt[s] + Ls[s]))
            if short <= 1e-5 or lowered_frame + short > MAX_LOWER:
                break
            lowered_frame += short
            Hd = Hd - Vector((0, 0, short + 1e-4))
            lowered = max(lowered, short)
        for s in SIDES:
            up, lo, ft, _ = LEG(s)
            H = W(fa, faP[up].head)
            Kfk = W(fa, faP[lo].head)
            T = targets[s]
            d = max(abs(Lt[s] - Ls[s]) + 1e-4, min((T - H).length, Lt[s] + Ls[s] - 1e-4))
            u = (T - H).normalized()
            w = Kfk - H
            v = w - u * w.dot(u)
            if v.length < 1e-6:
                v = prev_v[s] if prev_v[s] is not None else Vector((0, -1, 0))
            v = v.normalized()
            prev_v[s] = v
            ca = max(-1.0, min(1.0, (Lt[s] ** 2 + d * d - Ls[s] ** 2) / (2 * Lt[s] * d)))
            Kn = H + Lt[s] * (ca * u + math.sqrt(max(0.0, 1 - ca * ca)) * v)
            rotate_about(fa, faP[up], H, (Kfk - H).rotation_difference(Kn - H))
            upd()
            Kc = W(fa, faP[lo].head)
            Ac = W(fa, faP[ft].head)
            rotate_about(fa, faP[lo], Kc, (Ac - Kc).rotation_difference(T - Kc))
            upd()
            Ah = W(fa, faP[ft].head)
            set_world(fa, faP[ft], Matrix.LocRotScale(Ah, rots[s].to_quaternion(), F0["M" + s].to_scale()))
            upd()
        for pb in faP:
            l, q, sc = pb.matrix_basis.decompose()
            if rec[pb.name] and Quaternion(rec[pb.name][-1][1]).dot(q) < 0:
                q = -q
            rec[pb.name].append((tuple(l), tuple(q), tuple(sc)))
    new = bpy.data.actions.new(clip)
    new.use_fake_user = True
    assign(fa, new)   # fcurve_ensure_for_datablock needs the action on the datablock
    frames = list(range(f0, f1 + 1))
    for n in names:
        pb = faP[n]
        pb.rotation_mode = "QUATERNION"
        for path, idx, n_ch in (("location", 0, 3), ("rotation_quaternion", 1, 4), ("scale", 2, 3)):
            for c in range(n_ch):
                fc = new.fcurve_ensure_for_datablock(fa, f'pose.bones["{n}"].{path}', index=c)
                fc.keyframe_points.add(len(frames))
                kv = np.empty(len(frames) * 2, np.float32)
                kv[0::2] = frames
                kv[1::2] = [r[idx][c] for r in rec[n]]
                fc.keyframe_points.foreach_set("co", kv)
                for kp in fc.keyframe_points:
                    kp.interpolation = "LINEAR"
                fc.update()
    assign(fa, None)
    return new, contact, lowered


arm_mods = [m for m in hero.modifiers if m.type == "ARMATURE"]
for m in arm_mods:
    m.show_viewport = False   # no skinning while baking (speed); back on for the measures
baked, contacts, lows = {}, {}, {}
for clip in sorted(acts):
    try:
        new, contact, lowered = bake_action(clip, acts[clip])
        baked[clip], contacts[clip], lows[clip] = new, contact, lowered
    except Exception as e:
        log("bake failed", clip, repr(e))
assign(kk, None)
for m in arm_mods:
    m.show_viewport = True
log("baked", len(baked), "of", len(acts))
REP["hips_lowered_max"] = {c: round(v, 4) for c, v in lows.items() if v > 0}

# ---------------------------------------------------------------- measure
dg = bpy.context.evaluated_depsgraph_get()


def foot_heights():
    eo = hero.evaluated_get(bpy.context.evaluated_depsgraph_get())
    me = eo.to_mesh()
    c = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get("co", c)
    eo.to_mesh_clear()
    c = c.reshape(-1, 3)
    m = np.array(eo.matrix_world)
    c = c @ m[:3, :3].T + m[:3, 3]
    return {s: c[FOOTV[s]] for s in SIDES}


meas = {}
for clip in ("Idle", "Walk", "Run", "StrafeL", "StrafeR"):
    if clip not in baked:
        meas[clip] = "not baked"
        continue
    assign(fa, baked[clip])
    a = baked[clip]
    f0, f1 = int(a.frame_range[0]), int(a.frame_range[1])
    H = {s: [] for s in SIDES}
    XY = {s: [] for s in SIDES}
    ANK = {s: [] for s in SIDES}
    YAW = {s: [] for s in SIDES}
    for f in range(f0, f1 + 1):
        scene.frame_set(f)
        fh = foot_heights()
        for s in SIDES:
            a = W(fa, fa.pose.bones["foot." + s].head); t = W(fa, fa.pose.bones["toes." + s].head)
            ANK[s].append(round(a.z, 3)); YAW[s].append(round(math.degrees(math.atan2(t.x - a.x, -(t.y - a.y))), 1))
        for s in SIDES:
            H[s].append(round(float(fh[s][:, 2].min()), 4))
            z = fh[s][:, 2]
            patch = fh[s][z < z.min() + 0.01]   # the contact patch: the sole within 1 cm of the foot's lowest point
            XY[s].append(patch[:, :2].mean(axis=0))
    con = contacts[clip]
    n = len(H["l"])
    lowest = [min(H["l"][i], H["r"][i]) for i in range(n)]
    planted_err = {s: max([abs(H[s][i]) for i in range(n) if con[s][i]] or [0.0]) for s in SIDES}
    res = dict(frames=n, foot_l=H["l"], foot_r=H["r"], contact_l=con["l"], contact_r=con["r"],
               lowest_per_frame=[min(lowest), max(lowest)], planted_err=planted_err,
               min_any=min(min(H["l"]), min(H["r"])), ankle_l=ANK["l"], ankle_r=ANK["r"], toe_yaw_l=YAW["l"],
               toe_yaw_r=YAW["r"])
    if clip in GROUND_SPEED:
        v = GROUND_SPEED[clip] * RATIO
        dt = 1.0 / fps
        back = Vector((0, 0))
        for s in SIDES:
            for i in range(n - 1):
                if con[s][i] and con[s][i + 1]:
                    back += Vector(XY[s][i + 1] - XY[s][i])
        fwd = -back.normalized() if back.length > 1e-9 else Vector((0, 1))
        slip, cycles = 0.0, 8
        for _ in range(cycles):
            for s in SIDES:
                for i in range(n - 1):
                    if con[s][i] and con[s][i + 1]:
                        slip += (Vector(XY[s][i + 1] - XY[s][i]) + fwd * v * dt).length
        res.update(ground_speed=v, slide=slip / (v * (n - 1) * dt * cycles))
    meas[clip] = res
    log(clip, "lowest foot per frame", res["lowest_per_frame"], "planted err", planted_err, "min", res["min_any"],
        "slide", res.get("slide"))
REP["measure"] = meas

# ---------------------------------------------------------------- renders: front and side Walk strips, idle
try:
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    for ct in ("VERTEX", "ATTRIBUTE", "MATERIAL"):
        try:
            scene.display.shading.color_type = ct
            break
        except TypeError:
            continue
    scene.render.resolution_x, scene.render.resolution_y = 260, 360
    scene.render.film_transparent = False
    bpy.ops.mesh.primitive_plane_add(size=6, location=(0, 0, 0))
    gp = bpy.context.active_object
    gp.display_type = "SOLID"
    cam_d = bpy.data.cameras.new("cam")
    cam_d.type = "ORTHO"
    cam_d.ortho_scale = 2.6
    cam = bpy.data.objects.new("cam", cam_d)
    scene.collection.objects.link(cam)
    scene.camera = cam
    views = {"front": (Vector((0, -6, 1.85)), Vector((math.radians(82), 0, 0))),
             "side": (Vector((6, 0, 1.85)), Vector((math.radians(82), 0, math.radians(90))))}
    scene.display.shading.background_type = "VIEWPORT"
    scene.display.shading.background_color = (0.55, 0.6, 0.65)
    tiles = {}
    for clip, picks in (("Walk", 6), ("Run", 6), ("Idle", 1)):
        if clip not in baked:
            continue
        assign(fa, baked[clip])
        a = baked[clip]
        f0, f1 = int(a.frame_range[0]), int(a.frame_range[1])
        for vn, (loc, rot) in views.items():
            cam.location, cam.rotation_euler = loc, rot
            row = []
            for k in range(picks):
                f = f0 + int(round((f1 - f0) * k / max(1, picks)))
                scene.frame_set(f)
                p = os.path.join(OUT, f"_t_{clip}_{vn}_{k}.png")
                scene.render.filepath = p
                bpy.ops.render.render(write_still=True)
                img = bpy.data.images.load(p)
                px = np.array(img.pixels[:], np.float32).reshape(img.size[1], img.size[0], 4)
                bpy.data.images.remove(img)
                os.remove(p)
                row.append(px)
            tiles[(clip, vn)] = np.concatenate(row, axis=1)
    for clip in ("Walk", "Run", "Idle"):
        rows = [tiles[(clip, vn)] for vn in ("side", "front") if (clip, vn) in tiles]
        if not rows:
            continue
        sheet = np.concatenate(rows, axis=0)
        h, w = sheet.shape[:2]
        img = bpy.data.images.new(f"{CLS}_{clip}_strip", w, h)
        img.pixels = sheet.ravel()
        img.filepath_raw = os.path.join(OUT, f"{CLS}_{clip}_strip.png")
        img.file_format = "PNG"
        img.save()
    bpy.data.objects.remove(gp, do_unlink=True)
except Exception as e:
    log("render failed", repr(e))

# ---------------------------------------------------------------- export <Cls>_Clips.fbx (armature + NLA takes)
bpy.data.objects.remove(kk, do_unlink=True)
bpy.data.objects.remove(hero, do_unlink=True)
fa.name = "Rig"
ad = fa.animation_data or fa.animation_data_create()
ad.action = None
for t in list(ad.nla_tracks):
    ad.nla_tracks.remove(t)
for clip, a in sorted(baked.items()):
    tr = ad.nla_tracks.new()
    tr.name = clip
    st = tr.strips.new(clip, int(a.frame_range[0]), a)
    st.name = clip
    try:
        if st.action_slot is None and len(a.slots):
            st.action_slot = a.slots[0]
    except Exception:
        pass
for ob in list(scene.objects):
    if ob is not None:
        ob.select_set(ob is fa)
bpy.context.view_layer.objects.active = fa
rest(fa)
assign(fa, None)
path = os.path.join(OUT, f"{CLS}_Clips.fbx")
r = bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE"}, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", global_scale=1.0, axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, add_leaf_bones=False, primary_bone_axis="Y",
                             secondary_bone_axis="X", use_armature_deform_only=False, armature_nodetype="NULL",
                             bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=True,
                             bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True, bake_anim_step=1.0,
                             bake_anim_simplify_factor=0.0, use_custom_props=False, path_mode="STRIP")
REP["export"] = dict(path=path, result=list(r), bytes=os.path.getsize(path) if os.path.exists(path) else 0,
                     takes=len(baked))
REP["log"] = LOG
with open(os.path.join(OUT, f"bake_{CLS}.json"), "w") as fh:
    json.dump(REP, fh, indent=1, default=lambda o: list(o) if hasattr(o, "__iter__") else str(o))
log("done", REP["export"])
