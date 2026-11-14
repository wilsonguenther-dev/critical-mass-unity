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
FBX = {"Farmer": "Assets/_Game/Art/Farmer/Farmer.fbx", "Miner": "Assets/_Game/Art/Miner/Miner.fbx",
       "Warrior": "Assets/_Game/Art/Characters/Warrior.fbx", "Builder": "Assets/_Game/Art/Builder/Builder.fbx",
       "Healer": "Assets/_Game/Art/Healer/Healer.fbx", "Demolitions": "Assets/_Game/Art/Demolitions/Demolitions.fbx"}[CLS]
if len(argv) > 3 and argv[3]:
    FBX = argv[3]   # rig-batch-b: a freshly rigged model straight from rig_body.py (absolute path)
NEW_BODY = CLS in ("Builder", "Healer", "Demolitions")   # rig-batch-b: generated bodies rigged by rig_body.py   # HY models (Miner lifted 0.10 m, lift_rig_fbx.py; Warrior art/characters-hy-rig ef9d2eb)
CLIPS = "Assets/_Game/Art/Characters/KayKit_Clips.fbx"
GROUND_SPEED = {}   # clip -> KayKit ground speed (m/s), from Resources/Anim/clip_facts.json
CLIP_LEN, CLIP_CONTACT = {}, {}   # rig-builder-3: clip length and hit-event (contact) time, seconds
os.makedirs(OUT, exist_ok=True)
try:
    _cf = json.load(open(os.path.join(REPO, "Assets/_Game/Resources/Anim/clip_facts.json")))
    _cf = _cf.get("clips", _cf) if isinstance(_cf, dict) else _cf
    for _e in _cf:
        if isinstance(_e, dict) and _e.get("groundSpeed", 0) > 0.05:
            GROUND_SPEED[_e["clip"]] = float(_e["groundSpeed"])
        if isinstance(_e, dict) and _e.get("length", 0) > 0:
            CLIP_LEN[_e["clip"]] = float(_e["length"])
            CLIP_CONTACT[_e["clip"]] = float(_e.get("contact", -1))
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
FLIGHT_CAP = 0.32      # metres: lowest foot/toe joint never above this (game limit 0.35 u at scale 1)
CAPPED = {}
PLANT_N = 0.04         # rig-builder-2: new bodies hold their locomotion stance a little longer (the game needs 0.3 u of planted run)
PLANT = 0.025          # KayKit ankle within 2.5% of leg length of its rest height = planted
# rig-builder-3: the Builder's run and strafe stance is held longer (the game read 0.23 u/u of planted-toe slide in Run:
# KayKit's run stance is 2 frames, and the toe joint is within the game's 0.03 u stance band on the frames either side,
# where the foot still swings). The anchored, flat stance covers those frames instead.
PLANT_RUN = {"Builder": float(os.environ.get("BAKE_PLANT_RUN", "0.10")), "Healer": 0.14}.get(CLS)   # rig-healer-3: Healer too (0.36 u/u run slide in the game; 0.205 at 0.10 by check_sheet)


def plant_n(clip):
    if PLANT_RUN and clip in ("Run", "RunBack", "StrafeL", "StrafeR"):
        return PLANT_RUN
    return PLANT_N
MAX_LOWER = 0.12       # hips never come down more than this to reach a target (DeathB-style clips keep FK reach)
LOWER_PARTIAL = CLS in ("Builder", "Healer")
MAX_LOWER_CLS = float(os.environ.get("BAKE_MAX_LOWER", "0.12")) if LOWER_PARTIAL else MAX_LOWER
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
# rig-warrior-1: the hips and ankle motion is measured from KayKit's BIND (its model FBX: hips 0.406 m, ankles at
# +-0.171 m side, 0.145 m up, 0.019 m back; Blender axes), not from the pack armature's rest, which is a pose: measured from
# that pose the Warrior's StrafeR crossing step put his lower foot 0.54 m from the hip, out of reach, and it hung level
# with the other foot (the game read him turned around).
log("pack rest hips", tuple(round(x, 3) for x in K0["hips"]), "ankles", tuple(round(x, 3) for x in K0["Al"]), tuple(round(x, 3) for x in K0["Ar"]))
K0["hips"] = Vector((0.0, 0.0, 0.406))
K0["Al"], K0["Ar"] = Vector((0.171, 0.019, 0.145)), Vector((-0.171, 0.019, 0.145))
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
if NEW_BODY:   # rig-batch-b: the toes stay rigid on the foot (see the copy loop), so the boot tips count as sole
    SOLEV = {s: np.array([i for i in range(nv) if vg.get(dom[i], "") in ("foot." + s, "toes." + s)]) for s in SIDES}
LIFT = -float(min(wco[FOOTV[s]][:, 2].min() for s in SIDES))
# rig-warrior-1: the motion scale is the hip height above the soles against KayKit's (0.519 m, its bind), not the
# hip-to-ankle chain: after the Warrior's shins were lengthened (ankles at KayKit's height inside his boots) the chain
# ratio read 1.187 against a true size of 1.117, the hips rode too high, and in StrafeR his lower foot could not reach
# down (both ankles level where KayKit's differ by 7-10 cm; the game read him turned around).
KAYKIT_HIP = 0.519
if CLS in ("Warrior",) or NEW_BODY:
    RATIO = ((fa.matrix_world @ fa.data.bones["upperleg.l"].head_local).z + LIFT) / KAYKIT_HIP
    log("hip-height ratio", round(RATIO, 4))
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
# rig-builder-3: horizontal stride ratio (the game's StrideScale) apart from the hip-height ratio. The Builder's legs end
# in tall boots (ankle 0.17 u up), so the hip-height ratio (1.31) asked for a run stance his leg chain cannot reach: the
# IK fell short and the planted toe moved back at 79% of the ground speed (the game read 0.23 u/u of slide). A shorter
# stride at a higher cadence: feet and hips move horizontally by RATIO_H, and ModelSpec.StrideScale = RATIO_H.
RATIO_H = float(os.environ.get("BAKE_RATIO_H", str(RATIO))) if CLS == "Builder" else RATIO
# (measured after the anchor fix below: Run planted-toe slide 0.134 at the hip-height ratio, 0.172 at 1.12; kept = RATIO)
log("horizontal stride ratio", RATIO_H)
REP.update(ratio=RATIO, stride_scale=RATIO_H, lift=LIFT, leg_class=leg_f, leg_kaykit=leg_k, thigh=Lt, shin=Ls,
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



def rest_local(arm, name):
    b = arm.data.bones[name]
    return (b.parent.matrix_local.inverted() @ b.matrix_local) if b.parent else b.matrix_local.copy()


# rig-farmer-3: copy each bone's FULL local rotation (its rest included), not its rotation relative to the rest. The
# shared pack's armature rest is not the T-pose bind of the class rigs, so copying matrix_basis kept every class arm in
# the T-pose (Farmer_Clips.fbx read as the game reads it held the wrists at shoulder height). Legs hid it (IK).
RESTFIX = {pb.name: (rest_local(fa, pb.name).to_quaternion().inverted() @ rest_local(kk, pb.name).to_quaternion())
           for pb in fa.pose.bones if pb.name in kk.data.bones}

# A class that carries a long prop in one hand swings that arm less in locomotion: its arm bones are pulled toward their
# Idle (first frame) rotation, keeping HOLD_KEEP of the clip's swing (as on art/rig-farmer-3 for her staff). Miner: the
# pickaxe in his right hand rode sideways above his head in the walk with the full KayKit arm swing.
HOLD_ARM = {"Farmer": "r", "Miner": "r", "Builder": "r", "Healer": "r", "Demolitions": "r"}.get(CLS)
# rig-batch-b: finger bones (not in the pack) take one fixed pose per hand in every clip: the weapon hand closes round the
# light weapon's handle (the game hangs it on handslot.r), the other hand rests half open. Curl = turn about the finger
# bone's own X (its Z faces the palm, rig_body.py), so +angle closes the finger.
FINGER_CURL = {"grip": {"thumb": (25, 30, 25), "f": (70, 75, 55)}, "relaxed": {"thumb": (8, 10, 8), "f": (18, 22, 16)}}
GRIP_SIDE = "r"


def finger_basis(name):
    base, _, side = name.partition(".")
    f, _, i = base.partition("_0")
    if f not in ("thumb", "index", "middle", "ring", "pinky") or not i.isdigit():
        return None
    c = FINGER_CURL["grip" if side == GRIP_SIDE else "relaxed"]
    a = (c["thumb"] if f == "thumb" else c["f"])[int(i) - 1]
    return Matrix.Rotation(math.radians(a), 4, "X")
HOLD_KEEP = 0.35
HOLD_CLIPS = {"Walk", "WalkBack", "Run", "RunBack", "StrafeL", "StrafeR", "Carry"}
HOLD_BONES = tuple(n + "." + HOLD_ARM for n in ("upperarm", "lowerarm", "wrist", "hand")) if HOLD_ARM else ()
HOLD_REF = {}
if HOLD_ARM and "Idle" in acts:
    assign(kk, acts["Idle"])
    scene.frame_set(int(acts["Idle"].frame_range[0]))
    HOLD_REF = {n: RESTFIX[n] @ kkP[n].matrix_basis.to_quaternion() for n in HOLD_BONES if n in kkP}
    assign(kk, None)


# rig-batch-b: the game hangs the light weapon on handslot.r with its handle along the slot's +Y. KayKit's slot points
# forward at idle, so a new body's slot is held at a fixed turn relative to the hand (CREL) that stands the handle up
# at the first Idle frame; the weapon then follows the hand through every clip.
CREL = None


def copy_pose_only(clip_act, frame):
    assign(kk, clip_act); scene.frame_set(frame)
    for pb in faP:
        k = kkP.get(pb.name)
        if k is None:
            continue
        l_, q_, s_ = k.matrix_basis.decompose()
        if pb.name not in KEEP_LOC:
            l_ = Vector((0, 0, 0))
        pb.matrix_basis = Matrix.LocRotScale(l_, RESTFIX[pb.name] @ q_, s_)
    upd()


if NEW_BODY and "Idle" in acts and "handslot.r" in faP and "hand.r" in faP:
    copy_pose_only(acts["Idle"], int(acts["Idle"].frame_range[0]))
    Mh = (fa.matrix_world @ faP["hand.r"].matrix).to_3x3().normalized()
    Ms = (fa.matrix_world @ faP["handslot.r"].matrix).to_3x3().normalized()
    y0 = Ms.col[1]
    Rd = y0.rotation_difference(Vector((0, 0, 1))).to_matrix() @ Ms
    CREL = Mh.inverted() @ Rd
    REP["handslot_idle_y_before"] = [round(v, 3) for v in y0]
    log("handslot.r idle +Y before", REP["handslot_idle_y_before"], "-> stood up")
    assign(kk, None); rest(fa)


FOOTREF = {}
if False and NEW_BODY and "Idle" in acts:   # tried 2026-11-14 run 43637xxx: toe yaw read -137 degrees in Walk; off
    assign(kk, acts["Idle"]); scene.frame_set(int(acts["Idle"].frame_range[0])); upd()
    FOOTREF = {s: (kk.matrix_world @ kkP["foot." + s].matrix).to_3x3().normalized().inverted() for s in SIDES}
    assign(kk, None); upd()


# rig-miner-2: idle stands upright on flat feet. The full-local copy gave the Miner KayKit's idle lean and KayKit's
# steeper foot pitch (heels up, toes down in the Unity stills). In the idle clips the planted feet keep the bind's flat
# rotation, the toes stay as bound, and the hips turn once (constant per clip) so the head is straight above the hips.
IDLE_FLAT = CLS in ("Miner",) or NEW_BODY
IDLE_CLIPS = {"Idle", "IdleLook"}
UPRIGHT = {}
# rig-warrior-2: the held weapon's tip stays TIP_CLEAR above the ground in locomotion (the Warrior's sword tip dipped to
# the floor in Walk frames 4-6): the hand turns up about the wrist until the lowest prop vertex clears it.
TIP_CLEAR = {"Warrior": 0.18}.get(CLS)   # 0.15 u asked, 0.03 margin
TIP_CLIPS = {"Walk", "WalkBack", "Run", "RunBack", "StrafeL", "StrafeR", "Idle", "IdleLook", "Carry"}
TIP_LOCAL = None
TIP_MIN = {}
if TIP_CLEAR:
    _hs = [i for i in range(nv) if vg.get(dom[i], "").startswith("handslot.r")]
    if len(_hs) >= 20:
        rest(fa)
        _M = fa.matrix_world @ faP["handslot.r"].matrix
        _p = wco[np.array(_hs)]
        _far = _p[np.argmin(_p[:, 2])]          # the lowest prop vertex at rest = the tip that hangs down
        TIP_LOCAL = _M.inverted() @ Vector(_far.tolist())
        log("tip clearance on", len(_hs), "prop vertices, tip at rest", [round(v, 3) for v in _far])
    else:
        log("tip clearance: no prop on handslot.r (", len(_hs), "vertices)")


def tip_world():
    return (fa.matrix_world @ faP["handslot.r"].matrix) @ TIP_LOCAL


def clear_tip(clip):
    if TIP_LOCAL is None or clip not in TIP_CLIPS:
        return
    before = tip_world().z
    for _ in range(16):
        t = tip_world()
        if t.z >= TIP_CLEAR:
            break
        piv = W(fa, faP["wrist.r"].head)
        d = t - piv
        ax = d.cross(Vector((0, 0, 1)))
        if ax.length < 1e-6:
            break
        rotate_about(fa, faP["wrist.r"], piv, Quaternion(ax.normalized(), 0.08))
        upd()
    TIP_MIN[clip] = (min(TIP_MIN.get(clip, (9, 9))[0], before), min(TIP_MIN.get(clip, (9, 9))[1], tip_world().z))


# rig-builder-2: new bodies hang their arms closer: each upper arm turns about the shoulder (constant, about the forward
# axis) so that at the first Idle frame it is ARM_DOWN_DEG from straight down (KayKit's idle left the wide generated
# torsos with arms out like a gorilla). The mallet swing reads stronger: in Attack the right arm's turn from its first
# frame is scaled by ATTACK_GAIN.
ARM_DOWN_DEG = 28.0   # 22 put the Builder's gloves into his belly
ARM_ADD = {}
ATTACK_GAIN = {"Attack": 1.8}

# rig-builder-3: the Builder's Attack is a hand-keyed overhead mallet strike, not the scaled 1H chop. It keeps the chop's
# frame range and its hit-event (contact) time, so the game's attack timing is unchanged: 0 to 30 % wind-up (right hand
# up and behind the head, elbow bent, torso leans back and twists right), 30 % to contact strike (the arm comes down in
# front to below the waist, torso folds forward, knees bend), contact to the end recover to Idle. The legs stand on the
# Idle pose with flat planted feet (foot IK as Idle); only the hips come down. Angles in degrees, world space at each
# bone's head: pitch + folds forward (about +X, the body faces -Y), yaw + turns left (about +Z); arm angles turn about
# the shoulder line, - raises forward; th = mallet direction in the swing plane, 0 up, + toward the back.
MALLET = CLS == "Builder" and os.environ.get("BAKE_MALLET", "1") == "1"
MODE = {"mallet": MALLET}
MALLET_TGT = [None]
_CT = (CLIP_CONTACT.get("Attack", 0.5667) / CLIP_LEN.get("Attack", 1.0667)) if CLIP_LEN.get("Attack") else 0.531
_Z = dict(hips_p=0, hips_y=0, spine_p=0, spine_y=0, chest_p=0, chest_y=0, head_p=0, uar=0, lar=0, ual=0, lal=0,
          th=0, drop=0.0, back=0.0)


def _k(t, ease, **kw):
    d = dict(_Z); d.update(kw); d["t"] = t; d["ease"] = ease
    return d


_WIND = dict(hips_p=-4, hips_y=-5, spine_p=-5, spine_y=-6, chest_p=-6, chest_y=-6, head_p=8, uar=-176, lar=-28,
             ual=-30, lal=-25, th=80, drop=0.02, back=0.0)
_HIT = dict(hips_p=10, hips_y=3, spine_p=12, spine_y=3, chest_p=13, chest_y=2, head_p=-18, uar=-58, lar=-12,
            ual=-40, lal=-30, th=-140, drop=0.07, back=0.03)
MKEYS = [
    _k(0.0, "smooth"),
    _k(0.30, "smooth", **_WIND),                                                    # top of the wind-up
    _k(0.34, "lin", **dict(_WIND, uar=-179, th=86)),                               # a short hold before the blow
    _k(0.45, "in", hips_p=3, hips_y=0, spine_p=4, spine_y=1, chest_p=5, chest_y=1, head_p=-4, uar=-160, lar=-10,
       ual=-35, lal=-25, th=-15, drop=0.05, back=0.01),                             # mallet over the top
    _k(_CT, "lin", **_HIT),                                                         # contact (the clip's hit event)
    _k(min(0.99, _CT + 0.09), "out", **dict(_HIT, drop=0.075, uar=-60, th=-135)),   # settle
    _k(1.0, "smooth"),
]
MALLET_LOG = []
# rig-builder-3: the ladder rides the chest, and KayKit's run twists the chest 28 degrees from the hips (the ladder swung
# into a tangle in the side view). In locomotion the chest's turn about the vertical from the hips is damped by
# TWIST_DAMP (taken out at the spine). The Builder only.
TWIST_DAMP = {"Builder": 0.6}.get(CLS, 0.0)
TWIST_LOG = {}


def _yaw_from_rest(b):
    Rw = (fa.matrix_world @ faP[b].matrix).to_3x3().normalized()
    R0 = (fa.matrix_world @ fa.data.bones[b].matrix_local).to_3x3().normalized()
    v = (Rw @ R0.inverted()) @ Vector((0, -1, 0))
    return math.atan2(v.x, -v.y)


def damp_twist(clip):
    if not TWIST_DAMP or clip not in HOLD_CLIPS:
        return
    rel = _yaw_from_rest("chest") - _yaw_from_rest("hips")
    rel = (rel + math.pi) % (2 * math.pi) - math.pi
    pb = faP["spine"]
    rotate_about(fa, pb, W(fa, pb.head), Quaternion((0, 0, 1), -TWIST_DAMP * rel))
    upd()
    TWIST_LOG[clip] = max(TWIST_LOG.get(clip, 0.0), round(abs(math.degrees(rel)), 1))


def _ease(u, e):
    if e == "smooth":
        return u * u * (3 - 2 * u)
    if e == "in":
        return u * u
    if e == "out":
        return 1 - (1 - u) * (1 - u)
    return u


def mallet_keys(t):
    t = max(0.0, min(1.0, t))
    for a, b in zip(MKEYS, MKEYS[1:]):
        if a["t"] <= t <= b["t"]:
            u = _ease((t - a["t"]) / max(1e-6, b["t"] - a["t"]), b["ease"])
            return {n: a[n] + (b[n] - a[n]) * u for n in _Z}
    return dict(_Z)


def mallet_pose(t):
    """Pose the upper body for the mallet strike at clip fraction t (after the Idle copy, upright and arms_in)."""
    k = mallet_keys(t)
    for bn in ("hips", "spine", "chest"):
        pb = faP[bn]
        q = Quaternion((0, 0, 1), math.radians(k[bn + "_y"])) @ Quaternion((1, 0, 0), math.radians(k[bn + "_p"]))
        rotate_about(fa, pb, W(fa, pb.head), q)
        upd()
    rotate_about(fa, faP["head"], W(fa, faP["head"].head), Quaternion((1, 0, 0), math.radians(k["head_p"])))
    upd()
    A = (W(fa, faP["upperarm.l"].head) - W(fa, faP["upperarm.r"].head)).normalized()
    for bn, key in (("upperarm.r", "uar"), ("lowerarm.r", "lar"), ("upperarm.l", "ual"), ("lowerarm.l", "lal")):
        pb = faP[bn]
        rotate_about(fa, pb, W(fa, pb.head), Quaternion(A, math.radians(k[key])))
        upd()
    MALLET_TGT[0] = None
    if CREL is not None:
        th = math.radians(k["th"])
        yaw = math.radians(k["hips_y"] + k["spine_y"] + k["chest_y"])
        tgt = Quaternion((0, 0, 1), yaw) @ Vector((0.0, math.sin(th), math.cos(th)))
        d = ((fa.matrix_world @ faP["hand.r"].matrix).to_3x3().normalized() @ CREL).col[1]
        q = d.rotation_difference(tgt)
        half = Quaternion().slerp(q, 0.5)        # the wrist takes half the turn, the handle turns in the fist for the rest
        rotate_about(fa, faP["wrist.r"], W(fa, faP["wrist.r"].head), half)
        upd()
        MALLET_TGT[0] = tgt
        MALLET_LOG.append(dict(t=round(t, 3), wrist_deg=round(math.degrees(half.angle), 1),
                               full_deg=round(math.degrees(q.angle), 1)))
    return k["drop"], k["back"]


def _ua_angle(s):
    d = W(fa, faP["lowerarm." + s].head) - W(fa, faP["upperarm." + s].head)
    return math.degrees(math.atan2(abs(d.x), -d.z)), d


if NEW_BODY and "Idle" in acts:
    copy_pose_only(acts["Idle"], int(acts["Idle"].frame_range[0]))
    for s in SIDES:
        a, d = _ua_angle(s)
        sg = 1 if s == "l" else -1     # left arm is at +x: turning about -Y... sign measured below
        ARM_ADD[s] = math.radians(a - ARM_DOWN_DEG)
    REP["idle_upperarm_from_down_deg_before"] = {s: round(_ua_angle(s)[0], 1) for s in SIDES}
    assign(kk, None); rest(fa)


def arms_in(clip):
    for s in SIDES:
        ang = ARM_ADD.get(s, 0.0)
        if abs(ang) < 1e-4:
            continue
        pb = faP["upperarm." + s]
        piv = W(fa, pb.head)
        # turn about the forward axis toward the body: left arm (+x) turns about +Y, right arm (-x) about -Y
        ax = Vector((0, 1, 0)) if s == "l" else Vector((0, -1, 0))
        rotate_about(fa, pb, piv, Quaternion(ax, ang))
        upd()


if NEW_BODY and ARM_ADD and CREL is not None:
    # the slot turn is measured again with the arms brought in, so the mallet still stands upright at idle
    copy_pose_only(acts["Idle"], int(acts["Idle"].frame_range[0]))
    arms_in("Idle")
    Mh = (fa.matrix_world @ faP["hand.r"].matrix).to_3x3().normalized()
    Ms = (fa.matrix_world @ faP["handslot.r"].matrix).to_3x3().normalized()
    CREL = Mh.inverted() @ (Ms.col[1].rotation_difference(Vector((0, 0, 1))).to_matrix() @ Ms)
    REP["idle_upperarm_from_down_deg_after"] = {s: round(_ua_angle(s)[0], 1) for s in SIDES}
    log("upper arms from straight down at idle", REP["idle_upperarm_from_down_deg_before"], "->", REP["idle_upperarm_from_down_deg_after"])
    assign(kk, None); rest(fa)


def attack_gain(clip, q0s):
    g = ATTACK_GAIN.get(clip) if NEW_BODY and not (MODE["mallet"] and clip == "Attack") else None
    if not g:
        return
    for n in ("upperarm.r", "lowerarm.r"):
        pb = faP[n]
        l, q, sc = pb.matrix_basis.decompose()
        if n not in q0s:
            q0s[n] = q.copy()
        dq = q @ q0s[n].inverted()
        axis, ang = dq.to_axis_angle()
        pb.matrix_basis = Matrix.LocRotScale(l, Quaternion(axis, ang * g) @ q0s[n], sc)
    upd()


def upright_idle(clip, f0):
    """Constant hips turn per idle clip that puts the head straight above the hips at its first frame."""
    if MODE["mallet"] and clip == "Attack":
        clip = "Idle"   # rig-builder-3: the mallet strike starts and ends on the Idle pose, stood up the same way
    if not IDLE_FLAT or clip not in IDLE_CLIPS:
        return
    if clip not in UPRIGHT:
        v = W(fa, faP["head"].head) - W(fa, faP["hips"].head)
        UPRIGHT[clip] = v.rotation_difference(Vector((0, 0, v.length)).normalized() * 1.0 if False else Vector((0, 0, 1)))
        REP.setdefault("idle_lean_deg", {})[clip] = round(math.degrees(v.angle(Vector((0, 0, 1)))), 1)
    hp = faP["hips"]
    rotate_about(fa, hp, W(fa, hp.head), UPRIGHT[clip])
    upd()


def hold_slot():
    if CREL is None:
        return
    Mh = (fa.matrix_world @ faP["hand.r"].matrix)
    Rs = Mh.to_3x3().normalized() @ CREL
    if MALLET_TGT[0] is not None:
        # rig-builder-3: in the mallet strike the handle turns in the fist (about the grip, the slot origin) the rest of
        # the way to the keyed direction (the wrist took half of it, mallet_pose)
        Rs = Rs.col[1].rotation_difference(MALLET_TGT[0]).to_matrix() @ Rs
    p = (fa.matrix_world @ faP["handslot.r"].matrix).translation
    S = faP["handslot.r"].matrix.to_scale()
    set_world(fa, faP["handslot.r"], Matrix.LocRotScale(p, Rs.to_quaternion(), S))
    upd()


def kk_back_dir(act, f0, f1, thr=None):
    """Direction the KayKit planted feet move in the clip (opposite to travel), or None."""
    thr = PLANT if thr is None else thr
    acc, prev = Vector((0, 0, 0)), {s: None for s in SIDES}
    for f in range(f0, f1 + 1):
        scene.frame_set(f)
        for s in SIDES:
            a = W(kk, kkP["foot." + s].head)
            planted = (a.z - K0["A" + s].z) < thr * leg_k
            if planted and prev[s] is not None:
                acc += a - prev[s]
            prev[s] = a.copy() if planted else None
    acc.z = 0
    return acc.normalized() if acc.length > 1e-6 else None


ZPRE, PL, KUP = {}, {}, {}
TOE_ABS = CLS in ("Builder", "Healer") and os.environ.get("BAKE_TOE_ABS", "1") == "1"   # rig-healer-3: the anchor fix needs it
TOE_LOG = {}
IDLE_TOE = {}
ORDER_CLIPS = {"Walk", "WalkBack", "Run", "RunBack", "StrafeL", "StrafeR"}
ORDER_GAP = 0.004


def bake_action(clip, act):
    MAL = MODE["mallet"] and clip == "Attack" and "Idle" in acts
    assign(kk, acts["Idle"] if MAL else act)
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    fi0 = int(acts["Idle"].frame_range[0]) if MAL else 0
    MALLET_TGT[0] = None
    # Planted feet hold: while planted, a foot moves backward at exactly the class's ground speed (KayKit speed x leg
    # ratio, the speed the game moves it at), so it does not slide on the ground.
    vg_ = GROUND_SPEED.get(clip, 0.0) * RATIO_H
    # rig-builder-3: the direction is read with the same stance threshold the bake plants with. With KayKit's 2.5 % the
    # run had NO planted frame, bdir was None and the Builder's planted foot was never anchored: it followed KayKit's
    # foot x the ratio and moved back at 79 % of the ground speed (the game's 0.23 u/u skating read).
    _thr = plant_n(clip) if (TOE_ABS and NEW_BODY and clip in ORDER_CLIPS) else None
    bdir = kk_back_dir(act, f0, f1, _thr) if vg_ > 0 else None
    REP.setdefault("anchor_dir", {})[clip] = None if bdir is None else [round(bdir.x, 3), round(bdir.y, 3)]
    anchor = {s: None for s in SIDES}
    rec = {n: [] for n in names}
    contact = {s: [] for s in SIDES}
    prev_v = {s: None for s in SIDES}
    lowered = 0.0
    gain_q0 = {}
    for f in range(f0, f1 + 1):
        scene.frame_set(fi0 if MAL else f)
        for pb in faP:
            k = kkP.get(pb.name)
            if (NEW_BODY or (IDLE_FLAT and clip in IDLE_CLIPS)) and pb.name.startswith("toes."):
                pb.matrix_basis.identity()   # rig-batch-b: toes stay as bound; KayKit's toe pitch sank the boot tips 2 cm
                continue
            if k is None:
                fb = finger_basis(pb.name)
                if fb is not None:
                    pb.matrix_basis = fb
                else:
                    pb.matrix_basis.identity()
                continue
            l_, q_, s_ = k.matrix_basis.decompose()
            if pb.name not in KEEP_LOC:
                l_ = Vector((0, 0, 0))
            q_ = RESTFIX[pb.name] @ q_
            if clip in HOLD_CLIPS and pb.name in HOLD_REF:
                q_ = HOLD_REF[pb.name].slerp(q_, HOLD_KEEP)
            pb.matrix_basis = Matrix.LocRotScale(l_, q_, s_)
        upd()
        attack_gain(clip, gain_q0)
        upright_idle(clip, f0)
        damp_twist(clip)
        if NEW_BODY:
            arms_in(clip)
        m_drop, m_back = mallet_pose((f - f0) / max(1, f1 - f0)) if MAL else (0.0, 0.0)
        K = snap(kk)
        _dh = K["hips"] - K0["hips"]
        Hd = F0["hips"] + Vector((_dh.x * RATIO_H, _dh.y * RATIO_H, _dh.z * RATIO)) + Vector((0, m_back, LIFT - m_drop))
        targets, rots = {}, {}
        for s in SIDES:
            # The foot keeps the world rotation the clip gives it through the full-local copy (as on KayKit). The old
            # delta from the pack's rest (+ TOEFIX) was measured against a pose, not the bind: the Warrior's planted
            # right foot in StrafeR pointed 112 degrees off and the game read him "turned around".
            R = (fa.matrix_world @ faP[LEG(s)[2]].matrix).to_3x3().normalized()
            if (IDLE_FLAT and clip in IDLE_CLIPS) or MAL:
                R = F0["R" + s].copy()   # flat as bound (rest foot)
                if TOE_ABS:
                    # rig-builder-3 (7882e37): the idle feet stay flat but splay as KayKit's idle toes do (about 19 degrees out).
                    # FacingSanity read the Builder's straight idle toes (+-2 degrees) at the first frames of a move, when
                    # the idle pose still shows: 42 degrees from the leg yaw against a 30 degree tolerance (6 of 42).
                    R = Matrix.Rotation(_toe_yaw(kk, s) - _yf[s], 3, 'Z') @ R
                    IDLE_TOE.setdefault(clip, {})[s] = round(math.degrees(_toe_yaw(kk, s)), 1)
            elif FOOTREF:
                # rig-batch-b: KayKit's foot turn from its flat Idle foot, applied to this body's own flat rest foot (the
                # full-local copy pitched the Builder's boots by KayKit's 20 degree steeper foot bone)
                Rk_ = (kk.matrix_world @ kkP[LEG(s)[2]].matrix).to_3x3().normalized()
                R = Rk_ @ FOOTREF[s] @ F0["R" + s]
            # rig-builder-2: a new body's PLANTED locomotion foot lies flat as bound, turned only by KayKit's foot yaw. The
            # pack's heel-to-toe roll on the Builder's long boot lifted his toe joint more than 0.03 u through most of the
            # run stance (the game saw 2 planted frame pairs, 0.27 u, under its 0.3 u floor).
            if NEW_BODY and clip in ORDER_CLIPS and (K["A" + s].z - K0["A" + s].z) < plant_n(clip) * leg_k:
                fd = (K["R" + s] @ K0["R" + s].inverted()) @ Vector((0, -1, 0))
                if TOE_ABS:
                    # rig-builder-3: the planted toes point exactly where KayKit's toes point (its toe joint ahead of its
                    # ankle, absolute yaw), not KayKit's foot turn from the pack's rest pose: the Builder's planted toes
                    # read 45 degrees off the animator's leg yaw in a Run/StrafeL blend (FacingSanity, run 43639981260)
                    yk = _toe_yaw(kk, s)
                    R = Matrix.Rotation(yk - _yf[s], 3, 'Z') @ F0["R" + s]
                    TOE_LOG.setdefault(clip, []).append(round(math.degrees(yk), 1))
                elif fd.xy.length > 1e-4:
                    yaw = math.atan2(fd.y, fd.x) - math.atan2(-1.0, 0.0)
                    R = Matrix.Rotation(yaw, 3, 'Z') @ F0["R" + s]
            dk = K["A" + s] - K0["A" + s]
            # Foot motion scales with the legs (an unscaled swing lift was tried: Walk's lowest foot rose to 0.043).
            A = F0["A" + s] + Vector((dk.x * RATIO_H, dk.y * RATIO_H, dk.z * RATIO)) + Vector((0, 0, LIFT))
            planted = (K["A" + s].z - K0["A" + s].z) < (plant_n(clip) if NEW_BODY and clip in ORDER_CLIPS else PLANT) * leg_k
            if planted and bdir is not None:
                if anchor[s] is None:
                    anchor[s] = (A.copy(), f)
                a0, fa0 = anchor[s]
                A.x, A.y = (a0 + bdir * vg_ * (f - fa0) / fps).x, (a0 + bdir * vg_ * (f - fa0) / fps).y
            elif not planted:
                anchor[s] = None
            z = sole_z(s, A, R)
            ZPRE[s], PL[s], KUP[s] = z, planted, K["A" + s].z - K0["A" + s].z
            if planted:
                A.z -= z
            elif z < 0:
                A.z -= z
            contact[s].append(bool(planted))
            targets[s], rots[s] = A, R
        # The game's facing read (SanityRig.TryFront) takes the foot with the LOWER ankle. Pinning planted soles to z 0
        # flipped KayKit's ankle order on a few locomotion frames (Warrior StrafeR frame 18: 3 mm the other way, the
        # trailing foot read, toes 112 degrees off, "turned around"; KayKit itself: no such frame). In locomotion, when
        # KayKit has a clear lower ankle, the other ankle stays at least ORDER_GAP above it.
        if clip in ORDER_CLIPS:
            lo = min(SIDES, key=lambda q: K["A" + q].z)
            hi = "r" if lo == "l" else "l"
            if K["A" + hi].z - K["A" + lo].z > 0.0005 and targets[hi].z < targets[lo].z + ORDER_GAP:
                targets[hi].z = targets[lo].z + ORDER_GAP
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
            if LOWER_PARTIAL and clip in ORDER_CLIPS and short > 1e-5 and lowered_frame + short > MAX_LOWER_CLS:
                # rig-builder-3: a target out of reach by more than the cap still takes the hips down to the cap (the
                # old rule then did not lower them at all: the Builder's run toe-off fell 21% short of the anchored
                # stance, and the game read that as skating)
                short = MAX_LOWER_CLS - lowered_frame
                if short <= 1e-5:
                    break
            elif short <= 1e-5 or lowered_frame + short > MAX_LOWER:
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
        # rig-warrior-1: the game's feet test fails a model whose LOWEST foot or toe joint is more than 0.35 u above the
        # ground (GameplaySanityPlayTests). A run's flight frame scaled by a longer-legged class's ratio crossed it (HY
        # Warrior Run frame 1: 0.392, the game saw 0.35-0.39). On such a frame the whole pose comes down rigidly (hips
        # only) until that joint is at FLIGHT_CAP; frames under the cap are untouched.
        lowj = min(W(fa, faP[b].head).z for b in ("foot.l", "foot.r", "toes.l", "toes.r") if b in faP)
        if lowj > FLIGHT_CAP:
            hp = faP["hips"]
            set_world(fa, hp, Matrix.Translation((0, 0, FLIGHT_CAP - lowj)) @ (fa.matrix_world @ hp.matrix))
            upd()
            CAPPED[clip] = max(CAPPED.get(clip, 0.0), lowj - FLIGHT_CAP)
        if clip == "StrafeR" and CLS == "Warrior" and f in (2, 10, 18):
            log("trace", f, "kk ankle z l %.3f r %.3f" % (K["Al"].z, K["Ar"].z), "target z l %.3f r %.3f" % (targets["l"].z, targets["r"].z),
                "final z l %.3f r %.3f" % (W(fa, faP["foot.l"].head).z, W(fa, faP["foot.r"].head).z),
                "planted", PL.get("l"), PL.get("r"), "hips z %.3f" % W(fa, faP["hips"].head).z, "upperleg z %.3f" % W(fa, faP["upperleg.l"].head).z)
        if clip in ("Run", "StrafeR"):
            _a = W(fa, faP["foot.l"].head); _t = W(fa, faP["toes.l"].head)
            REP.setdefault("dbg_" + clip, []).append([f, bool(PL.get("l")), [round(targets["l"].x, 3), round(targets["l"].y, 3), round(targets["l"].z, 3)],
                                                     [round(_a.x, 3), round(_a.y, 3), round(_a.z, 3)], [round(_t.x, 3), round(_t.y, 3), round(_t.z, 3)],
                                                     None if anchor["l"] is None else anchor["l"][1]])
        clear_tip(clip)
        hold_slot()
        for pb in faP:
            l, q, sc = pb.matrix_basis.decompose()
            if rec[pb.name] and Quaternion(rec[pb.name][-1][1]).dot(q) < 0:
                q = -q
            rec[pb.name].append((tuple(l), tuple(q), tuple(sc)))
    MALLET_TGT[0] = None
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
REP["mallet"] = dict(on=MALLET, contact_fraction=round(_CT, 4), wrist=MALLET_LOG[::2])
REP["ground_speed"] = GROUND_SPEED
REP["fps"] = fps
REP["twist_damp"] = dict(keep_out=TWIST_DAMP, chest_from_hips_max_deg_before=TWIST_LOG)
REP["idle_toe_yaw_deg"] = IDLE_TOE
REP["planted_toe_yaw_kaykit_deg"] = {c: [min(v), max(v)] for c, v in TOE_LOG.items()}


# rig-builder-3: ladder diagnosis. (1) the back piece's weights as exported (islands of the Hero, the back region);
# (2) how far hips, spine and chest turn per frame in the new strike and in the old scaled chop (not exported).
def bone_track(act, bones=("hips", "spine", "chest")):
    assign(fa, act)
    f0_, f1_ = int(act.frame_range[0]), int(act.frame_range[1])
    res, q0 = {b: [] for b in bones}, {}
    for f in range(f0_, f1_ + 1):
        scene.frame_set(f)
        for b in bones:
            q = (fa.matrix_world @ faP[b].matrix).to_quaternion()
            q0.setdefault(b, q)
            d = q @ q0[b].inverted()
            if d.w < 0:
                d = -d
            res[b].append([round(math.degrees(d.angle), 1), round(math.degrees(2 * math.atan2(d.z, d.w)), 1)])
    assign(fa, None)
    return res


try:
    BB = os.environ.get("RIG_BACK_BONE", "chest")
    ng = len(hero.vertex_groups)
    Wm = np.zeros((nv, ng))
    for v in hero.data.vertices:
        for g in v.groups:
            Wm[v.index, g.group] = g.weight
    me_ = hero.data
    E_ = np.empty(len(me_.edges) * 2, np.int64); me_.edges.foreach_get("vertices", E_); E_ = E_.reshape(-1, 2)
    lab = np.arange(nv)
    for _ in range(5000):
        m_ = np.minimum(lab[E_[:, 0]], lab[E_[:, 1]])
        nl = lab.copy(); np.minimum.at(nl, E_[:, 0], m_); np.minimum.at(nl, E_[:, 1], m_)
        nl = nl[nl]
        if (nl == lab).all():
            break
        lab = nl
    chest_y = (fa.matrix_world @ fa.data.bones["chest"].head_local).y
    hips_z = (fa.matrix_world @ fa.data.bones["hips"].head_local).z
    Hh = float(wco[:, 2].max())
    backm = (wco[:, 1] > chest_y + 0.05 * Hh) & (wco[:, 2] > hips_z)
    bbi = next((g.index for g in hero.vertex_groups if g.name == BB), None)

    def wstats(sel):
        idx = np.nonzero(sel)[0]
        if len(idx) == 0 or bbi is None:
            return {"n": 0}
        rigid = Wm[idx, bbi] >= 0.999
        other = {}
        for gi_ in range(ng):
            if gi_ == bbi:
                continue
            w_ = Wm[idx, gi_]
            if (w_ > 1e-3).any():
                other[vg[gi_]] = [int((w_ > 1e-3).sum()), round(float(w_.max()), 3)]
        return {"n": int(len(idx)), "rigid_on_" + BB: int(rigid.sum()), "min_" + BB + "_weight": round(float(Wm[idx, bbi].min()), 3),
                "other_bones_count_maxw": other}
    isl = []
    labs, cnt = np.unique(lab, return_counts=True)
    for L_, c_ in sorted(zip(labs, cnt), key=lambda x: -x[1])[:12]:
        sel = lab == L_
        P_ = wco[sel]
        isl.append(dict(verts=int(c_), behind_back_frac=round(float(backm[sel].mean()), 3),
                        bbox_min=[round(float(x), 3) for x in P_.min(0)], bbox_max=[round(float(x), 3) for x in P_.max(0)],
                        weights=wstats(sel) if backm[sel].mean() > 0.5 else None))
    REP["ladder_diag"] = dict(back_bone=BB, islands_total=int(len(labs)), islands=isl, back_region=wstats(backm),
                              back_plane_y=round(chest_y + 0.05 * Hh, 3), hips_z=round(hips_z, 3), height=round(Hh, 3))
    log("ladder diag", json.dumps(REP["ladder_diag"])[:3000])
    tracks = {}
    for c_ in ("Attack", "UseItem", "Run", "Walk", "Hit", "HitHeavy"):
        if c_ in baked:
            tracks[c_] = bone_track(baked[c_])
    if MALLET and "Attack" in acts:
        for m in arm_mods:
            m.show_viewport = False
        MODE["mallet"] = False
        chop, _, _ = bake_action("Attack", acts["Attack"])
        MODE["mallet"] = MALLET
        chop.name = "AttackChop_diag"
        for m in arm_mods:
            m.show_viewport = True
        tracks["AttackChop_old"] = bone_track(chop)
    REP["torso_turn_per_frame_deg_total_yaw"] = {c_: t_ for c_, t_ in tracks.items() if c_.startswith("Attack")}
    REP["torso_turn_max_deg"] = {c_: {b: [max(x[0] for x in v), max(abs(x[1]) for x in v)] for b, v in t_.items()}
                                 for c_, t_ in tracks.items()}
    log("torso turn max (total, |yaw|) from each clip's first frame", json.dumps(REP["torso_turn_max_deg"]))
except Exception:
    import traceback as _tb
    log("ladder diag failed", _tb.format_exc()[-1500:])
REP["hips_lowered_max"] = {c: round(v, 4) for c, v in lows.items() if v > 0}
REP["tip_min_before_after"] = {c: [round(a, 3), round(b, 3)] for c, (a, b) in TIP_MIN.items()}
log("tip min z before/after", REP["tip_min_before_after"], "idle lean before (deg)", REP.get("idle_lean_deg"))
REP["flight_capped_max"] = {c: round(v, 4) for c, v in CAPPED.items()}
log("flight capped at", FLIGHT_CAP, REP["flight_capped_max"])

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
        v = GROUND_SPEED[clip] * RATIO_H
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
    SKILL = {"Builder": ("Attack", "UseItem"), "Healer": ("Cast", "UseItem"), "Demolitions": ("Throw", "PickUp")}.get(CLS, ())
    # the game's light weapon (WeaponProps: handle along handslot.r +Y, 0.62 u, held 0.12 u from its end): a stand-in stick
    stick = None
    if NEW_BODY and "handslot.r" in fa.pose.bones:
        rest(fa)
        bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.025, depth=0.62)
        stick = bpy.context.active_object
        bpy.ops.mesh.primitive_cube_add(size=0.14, location=(0, 0, 0.31))
        hd_ = bpy.context.active_object
        for o_ in scene.objects:
            o_.select_set(o_ in (stick, hd_))
        bpy.context.view_layer.objects.active = stick
        bpy.ops.object.join()
        stick.data.transform(Matrix.Rotation(-math.pi / 2, 4, "X"))   # cylinder axis Z -> +Y
        stick.parent = fa; stick.parent_type = "BONE"; stick.parent_bone = "handslot.r"
        upd()
        Mhs = fa.matrix_world @ fa.pose.bones["handslot.r"].matrix
        stick.matrix_world = Mhs @ Matrix.Translation((0, 0.31 - 0.12, 0))
        upd()
    for clip, picks in (("Walk", 6), ("Run", 6), ("Idle", 1)) + tuple((c, 6) for c in SKILL):
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
    if NEW_BODY and "Idle" in baked:
        assign(fa, baked["Idle"]); scene.frame_set(int(baked["Idle"].frame_range[0]))
        extra = {"gameplay": (Vector((0, -4.2, 2.0 + 4.2 * math.tan(math.radians(55)))), Vector((math.radians(35), 0, 0)), 2.6),
                 "gameplay34": (Vector((3.0, -3.0, 2.0 + 4.24 * math.tan(math.radians(55)))), Vector((math.radians(35), 0, math.radians(45))), 2.6)}
        hr = fa.matrix_world @ fa.pose.bones["hand.r"].head
        extra["hand_r"] = (Vector((hr.x - 0.15, hr.y - 2.0, hr.z)), Vector((math.radians(90), 0, math.radians(-4))), 0.5)
        hl = fa.matrix_world @ fa.pose.bones["hand.l"].head
        extra["hand_l"] = (Vector((hl.x + 0.15, hl.y - 2.0, hl.z)), Vector((math.radians(90), 0, math.radians(4))), 0.5)
        for vn, (loc, rot, osc) in extra.items():
            cam.location, cam.rotation_euler = loc, rot
            cam_d.ortho_scale = osc
            scene.render.filepath = os.path.join(OUT, f"{CLS}_Idle_{vn}.png")
            bpy.ops.render.render(write_still=True)
        cam_d.ortho_scale = 2.6
    for clip in ("Walk", "Run", "Idle") + tuple(SKILL):
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
    if stick is not None:
        bpy.data.objects.remove(stick, do_unlink=True)
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
