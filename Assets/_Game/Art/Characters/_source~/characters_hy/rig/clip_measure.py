"""Clip measure (b12-x2, 2026-11-16): numbers for a baked <Cls>_Clips.fbx, read the way the game plays it (bone joints of
the exported armature, one sample per key frame). Evidence for the builder only: done means looked at in the game.

  blender -b --factory-startup --python clip_measure.py -- <out> <repo> [plan]
  plan: "b12" (default) = measure the committed Healer, Demolitions and reference clips, and bake the candidate variants
        listed in BAKES in parallel child Blender processes (bake_clips.py, env switches), then measure and compare those;
        "final" = bake with the committed defaults only (Healer, Demolitions) and measure/compare; "none" = no bakes.
        CM_MEASURE_BAKES='[{"label":..,"cls":..,"env":{..}}]' overrides the bake list.

Locomotion (Run, Walk): per frame hips height; planted-toe slide per unit of body travel (the game's rule: a toe is planted
while within 0.03 u of its own lowest height, body speed = KayKit ground speed x leg ratio); left/right stride symmetry
(stance frames, touchdown phase, fore-aft toe excursion, swing lift); pops = frame-to-frame local bone turns over 25
degrees, spikes = turns over 3x the bone's median step and over 8 degrees; foot jerk = largest frame-to-frame change of
toe velocity; the loop seam (last frame back to the first) for every bone and toe.
Attack: the weapon hand's (hand.r) and slot's (handslot.r) travel along the body's forward axis from the first frame,
at the contact frame (clip_facts contact time) and over the contact window (contact +-0.1 s); a tip proxy (slot origin +
TIP_LEN along the slot's +Y, the handle axis the game hangs the light weapon on); which bones are keyed.
Every measured case prints one plain line: what, value, limit.
"""
import bpy, sys, os, json, math, subprocess, time
from mathutils import Vector, Quaternion

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else os.environ.get("CI_OUT", "/tmp/clip_measure")
REPO = argv[1] if len(argv) > 1 else os.environ.get("CI_REPO", os.getcwd())
PLAN = argv[2] if len(argv) > 2 else "b12"
os.makedirs(OUT, exist_ok=True)
RIG = os.path.join(REPO, "Assets/_Game/Art/Characters/_source~/characters_hy/rig")
POP_DEG, SPIKE_X, SPIKE_MIN = 25.0, 3.0, 8.0
PLANT_BAND, SLIDE_LIMIT = 0.03, 0.2
TIP_LEN = 0.55   # u: pry-bar-length proxy along the slot's +Y (the handle axis); the game's prop is the truth
WIN_S = 0.1
REP = {"plan": PLAN, "files": {}, "bakes": {}, "compare": {}}
LINES = []

BAKES = {
    "b12": [dict(label="healer_today", cls="Healer", env={}),
            dict(label="healer_seam", cls="Healer", env={"BAKE_LOOP_ANCHOR": "1"}),
            dict(label="healer_seam_rel3", cls="Healer", env={"BAKE_LOOP_ANCHOR": "1", "BAKE_RELEASE_BLEND": "3"}),
            dict(label="demo_today", cls="Demolitions", env={}),
            dict(label="demo_strike", cls="Demolitions", env={"BAKE_DEMO_STRIKE": "1"})],
    "final": [dict(label="healer_final", cls="Healer", env={}),
              dict(label="demo_final", cls="Demolitions",
                   env={"BAKE_TAKES": "Attack", "BAKE_SPLICE_INTO": "Assets/_Game/Art/Demolitions/Demolitions_Clips.fbx"})],
    "splice": [dict(label="demo_splice", cls="Demolitions",
                    env={"BAKE_TAKES": "Attack", "BAKE_SPLICE_INTO": "Assets/_Game/Art/Demolitions/Demolitions_Clips.fbx"})],
    "none": [],
}
COMMITTED = [  # label, path, clips
    ("healer_committed", "Assets/_Game/Art/Healer/Healer_Clips.fbx", ("Run", "Walk", "StrafeL", "StrafeR")),
    ("demo_committed", "Assets/_Game/Art/Demolitions/Demolitions_Clips.fbx", ("Attack", "Run")),
    ("warrior_ref", "Assets/_Game/Art/Characters/Warrior_Clips.fbx", ("Attack", "Run")),
    ("kaykit_ref", "Assets/_Game/Art/Characters/KayKit_Clips.fbx", ("Attack", "Run")),
]
CLS_OF = {"healer": "Healer", "demo": "Demolitions"}


def say(s):
    print("[measure]", s, flush=True)
    LINES.append(s)


def r3(x):
    return None if x is None else round(float(x), 3)


def facts():
    try:
        cf = json.load(open(os.path.join(REPO, "Assets/_Game/Resources/Anim/clip_facts.json")))
        cf = cf.get("clips", cf) if isinstance(cf, dict) else cf
        return {e["clip"]: e for e in cf if isinstance(e, dict) and "clip" in e}
    except Exception as e:
        say("clip facts unreadable " + repr(e))
        return {}


FACTS = facts()


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


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path, use_anim=True, use_custom_normals=False, ignore_leaf_bones=False,
                             automatic_bone_orientation=False, primary_bone_axis="Y", secondary_bone_axis="X")
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    acts = {a.name.split("|")[-1]: a for a in bpy.data.actions}
    return arm, acts


def wpos(arm, n):
    return arm.matrix_world @ arm.pose.bones[n].head


def rest_frame(arm):
    assign(arm, None)
    for pb in arm.pose.bones:
        pb.matrix_basis.identity()
    upd()
    P = arm.pose.bones
    up = Vector((0, 0, 1))
    fwd = Vector((0, 0, 0))
    for s in ("l", "r"):
        if "toes." + s in P and "foot." + s in P:
            fwd += wpos(arm, "toes." + s) - wpos(arm, "foot." + s)
    fwd.z = 0
    fwd = fwd.normalized() if fwd.length > 1e-6 else Vector((0, -1, 0))
    right = fwd.cross(up)
    if "upperarm.r" in P and "upperarm.l" in P:
        d = wpos(arm, "upperarm.r") - wpos(arm, "upperarm.l")
        if d.dot(right) < 0:
            right = -right
    leg = sum((wpos(arm, "foot." + s) - wpos(arm, "upperleg." + s)).length for s in ("l", "r")) / 2
    hips = wpos(arm, "hips").copy()
    return fwd, right, up, leg, hips


def sample(arm, act, bones):
    assign(arm, None)
    for pb in arm.pose.bones:   # a bone with no curve in this take must not keep the previous take's pose
        pb.matrix_basis.identity()
    assign(arm, act)
    f0, f1 = int(round(act.frame_range[0])), int(round(act.frame_range[1]))
    sc = bpy.context.scene
    fr = []
    for f in range(f0, f1 + 1):
        sc.frame_set(f)
        upd()
        d = {"f": f, "q": {}, "p": {}, "y": {}}
        for pb in arm.pose.bones:
            d["q"][pb.name] = pb.matrix_basis.to_quaternion().normalized()
        for n in bones:
            if n in arm.pose.bones:
                M = arm.matrix_world @ arm.pose.bones[n].matrix
                d["p"][n] = M.translation.copy()
                d["y"][n] = M.to_3x3().normalized().col[1].copy()
        fr.append(d)
    return fr, f0, f1


def qang(a, b):
    return math.degrees(a.rotation_difference(b).angle) if a is not None and b is not None else 0.0


def keyed(act):
    rng = {}
    for fc in act.fcurves if hasattr(act, "fcurves") else []:
        if not fc.data_path.startswith('pose.bones["'):
            continue
        b = fc.data_path.split('"')[1]
        vs = [k.co[1] for k in fc.keyframe_points]
        if vs:
            rng[b] = max(rng.get(b, 0.0), max(vs) - min(vs))
    if not rng:   # Blender 5 layered actions: channels live in the slot's channelbag
        try:
            for ly in act.layers:
                for st in ly.strips:
                    for cb in st.channelbags:
                        for fc in cb.fcurves:
                            if fc.data_path.startswith('pose.bones["'):
                                b = fc.data_path.split('"')[1]
                                vs = [k.co[1] for k in fc.keyframe_points]
                                if vs:
                                    rng[b] = max(rng.get(b, 0.0), max(vs) - min(vs))
        except Exception:
            pass
    moving = sorted(b for b, v in rng.items() if v > 1e-4)
    return moving, sorted(b for b, v in rng.items() if v <= 1e-4)


def loco(tag, arm, act, clip, frame0, kk_leg):
    fwd, right, up, leg, hips0 = frame0
    bones = ("hips", "root", "toes.l", "toes.r", "foot.l", "foot.r")
    fr, f0, f1 = sample(arm, act, bones)
    n = len(fr)
    # Does the last key repeat the first (Unity then plays f1 == f0 and wraps)?
    seam_rot = max(qang(fr[0]["q"][b], fr[-1]["q"][b]) for b in fr[0]["q"])
    seam_toe = max((fr[0]["p"]["toes." + s] - fr[-1]["p"]["toes." + s]).length for s in ("l", "r"))
    dup = seam_rot < 0.5 and seam_toe < 0.002
    cyc = fr[:-1] if dup else fr
    P = len(cyc)
    fps = bpy.context.scene.render.fps / bpy.context.scene.render.fps_base
    dt = 1.0 / fps
    ratio = leg / kk_leg if kk_leg else 1.0
    v = float(FACTS.get(clip, {}).get("groundSpeed", 0.0)) * ratio
    res = dict(frames=[f0, f1], fps=fps, end_repeats_start=dup, leg=r3(leg), leg_ratio=r3(ratio), body_speed=r3(v))
    # hips height
    hz = [c["p"]["hips"].z for c in cyc]
    d1 = [hz[(i + 1) % P] - hz[i] for i in range(P)]
    d2 = [hz[(i + 1) % P] - 2 * hz[i] + hz[i - 1] for i in range(P)]
    i2 = max(range(P), key=lambda i: abs(d2[i]))
    res["hips_z"] = dict(min=r3(min(hz)), max=r3(max(hz)), range=r3(max(hz) - min(hz)), max_step=r3(max(abs(x) for x in d1)),
                         max_jerk=r3(abs(d2[i2])), jerk_frame=cyc[i2]["f"], per_frame=[r3(x) for x in hz])
    say(f"{tag} {clip} hips height range {res['hips_z']['range']} u, largest frame step {res['hips_z']['max_step']} u, "
        f"largest bob jerk {res['hips_z']['max_jerk']} u at frame {res['hips_z']['jerk_frame']} (references 0.08 to 0.10 u)")
    say(f"{tag} {clip} hips height per frame {res['hips_z']['per_frame']}")
    if "root" in cyc[0]["p"]:
        rz = [c["p"]["root"].z for c in cyc]
        res["root_z_range"] = r3(max(rz) - min(rz))
    # pops and spikes (cyclic, the seam included)
    pops, spikes, med = [], [], {}
    for b in cyc[0]["q"]:
        steps = [qang(cyc[i]["q"][b], cyc[(i + 1) % P]["q"][b]) for i in range(P)]
        srt = sorted(steps)
        m = srt[len(srt) // 2]
        med[b] = m
        for i, a in enumerate(steps):
            if a > POP_DEG:
                pops.append([b, cyc[i]["f"], cyc[(i + 1) % P]["f"], round(a, 1)])
            elif a > SPIKE_MIN and a > SPIKE_X * max(m, 0.5):
                spikes.append([b, cyc[i]["f"], cyc[(i + 1) % P]["f"], round(a, 1), round(m, 1)])
    spikes.sort(key=lambda x: -x[3])
    res["pops_over_25deg"] = pops
    res["spikes"] = spikes[:12]
    res["spike_count"] = len(spikes)
    say(f"{tag} {clip} pops (frame-to-frame bone turn > {POP_DEG:.0f} deg): {len(pops)} {pops[:4]} (limit 0)")
    say(f"{tag} {clip} spikes (> {SPIKE_X:.0f}x the bone's median step and > {SPIKE_MIN:.0f} deg): {len(spikes)} top {spikes[:4]}")
    # seam (last recorded frame back to the first): bones and toes
    last, first = (fr[-2], fr[0]) if dup else (fr[-1], fr[0])
    seam_b = sorted(((round(qang(last["q"][b], first["q"][b]), 1), b) for b in first["q"]), reverse=True)[:5]
    res["seam_worst_bones_deg"] = [[b, a, round(med.get(b, 0), 1)] for a, b in seam_b]
    if dup:
        res["seam_end_vs_start_deg"] = round(seam_rot, 2)
    # toes: planted band, slide, jerk, symmetry
    feet = {}
    for s in ("l", "r"):
        t = [c["p"]["toes." + s] for c in cyc]
        zmin = min(p.z for p in t)
        pl = [p.z <= zmin + PLANT_BAND for p in t]
        ft = bt = 0.0
        pairs = 0
        worst = (0.0, None)
        for i in range(P):
            j = (i + 1) % P
            if pl[i] and pl[j]:
                d = (t[j] - t[i]) + fwd * v * dt
                dd = math.hypot(d.x, d.y)
                ft += dd
                bt += v * dt
                pairs += 1
                if dd > worst[0]:
                    worst = (dd, cyc[i]["f"])
        vel = [t[(i + 1) % P] - t[i] for i in range(P)]
        acc = [(vel[i] - vel[i - 1]).length for i in range(P)]
        ia = max(range(P), key=lambda i: acc[i])
        downs = [cyc[i]["f"] for i in range(P) if pl[i] and not pl[i - 1]]
        exc = [p.dot(fwd) for p in t]
        feet[s] = dict(stance_frames=sum(pl), touchdown_frames=downs, slide=r3(ft / bt) if bt > 0 else None, planted_pairs=pairs,
                       worst_planted_step=r3(worst[0]), worst_step_frame=worst[1], lift_max=r3(max(p.z for p in t) - zmin),
                       excursion=r3(max(exc) - min(exc)), jerk_max=r3(acc[ia]), jerk_frame=cyc[ia]["f"],
                       seam_jump=r3((last["p"]["toes." + s] - first["p"]["toes." + s]).length),
                       seam_step_median=r3(sorted(x.length for x in vel)[P // 2]))
        say(f"{tag} {clip} toe {s}: planted slide {feet[s]['slide']} u/u (game limit {SLIDE_LIMIT}), stance {feet[s]['stance_frames']}/{P} frames, "
            f"touchdown {downs}, excursion {feet[s]['excursion']} u, lift {feet[s]['lift_max']} u, "
            f"jerk {feet[s]['jerk_max']} u/frame at {feet[s]['jerk_frame']}, seam jump {feet[s]['seam_jump']} u vs median step {feet[s]['seam_step_median']} u")
    res["toes"] = feet
    L_, R_ = feet["l"], feet["r"]
    sym = {}
    if L_["touchdown_frames"] and R_["touchdown_frames"]:
        ph = ((R_["touchdown_frames"][0] - L_["touchdown_frames"][0]) % P) / P
        sym["touchdown_phase_r_after_l"] = r3(ph)
    for k in ("stance_frames", "excursion", "lift_max"):
        a, b = L_[k] or 0, R_[k] or 0
        sym[k + "_lr_ratio"] = r3(min(a, b) / max(a, b)) if max(a, b) > 0 else None
    res["symmetry"] = sym
    say(f"{tag} {clip} left/right symmetry: {sym} (ideal: phase 0.5, ratios 1.0)")
    return res


def attack(tag, arm, act, clip, frame0):
    fwd, right, up, leg, hips0 = frame0
    bones = ("hips", "root", "hand.r", "lowerarm.r", "handslot.r", "hand.l")
    fr, f0, f1 = sample(arm, act, bones)
    fps = bpy.context.scene.render.fps / bpy.context.scene.render.fps_base
    ct = float(FACTS.get(clip, {}).get("contact", -1))
    cf = f0 + int(round(ct * fps)) if ct >= 0 else (f0 + f1) // 2
    cf = max(f0, min(f1, cf))
    w = int(round(WIN_S * fps))
    base = fr[0]
    org = Vector((hips0.x, hips0.y, 0.0))   # the body's ground point at bind (root origin)
    res = dict(frames=[f0, f1], fps=fps, contact_frame=cf, window=[cf - w, cf + w])

    def track(name, tip=False):
        if name not in base["p"]:
            return None
        pts = [(c["p"][name] + (c["y"][name] * TIP_LEN if tip else Vector())) for c in fr]
        p0 = pts[0]
        fw = [(p - p0).dot(fwd) for p in pts]
        ic = cf - f0
        win = fw[max(0, ic - w): ic + w + 1]
        pc = pts[ic] - org
        hip_c = fr[ic]["p"]["hips"]
        return dict(fwd_travel_at_contact=r3(fw[ic]), fwd_travel_max=r3(max(fw)), fwd_travel_max_frame=f0 + fw.index(max(fw)),
                    window_fwd_min_max=[r3(min(win)), r3(max(win))],
                    at_contact_body=dict(forward=r3(pc.dot(fwd)), right=r3(pc.dot(right)), up=r3(pc.dot(up))),
                    at_contact_vs_hips_height=r3(pts[ic].z - hip_c.z),
                    per_frame_fwd=[r3(x) for x in fw])
    res["hand_r"] = track("hand.r")
    res["slot_r"] = track("handslot.r")
    res["tip_proxy"] = track("handslot.r", True)
    if "handslot.r" in base["y"]:
        yc = fr[cf - f0]["y"]["handslot.r"]
        res["slot_y_at_contact"] = dict(forward=r3(yc.dot(fwd)), right=r3(yc.dot(right)), up=r3(yc.dot(up)))
    mv, st = keyed(act)
    res["keyed_moving_bones"], res["keyed_static_bones"] = mv, len(st)
    for k in ("hand_r", "tip_proxy"):
        t = res[k]
        if t:
            say(f"{tag} {clip} {k}: forward travel at contact (frame {cf}) {t['fwd_travel_at_contact']} u, max {t['fwd_travel_max']} u "
                f"(frame {t['fwd_travel_max_frame']}), contact window {t['window_fwd_min_max']} u; at contact {t['at_contact_body']} "
                f"from the body's ground point (Builder's strike reads 0.8 u forward in the game)")
    say(f"{tag} {clip} slot +Y (handle) at contact {res.get('slot_y_at_contact')}; keyed bones that move: {len(mv)} {mv}")
    return res


def measure(tag, path, clips, kk_leg):
    if not os.path.exists(path) or os.path.getsize(path) < 1000:
        say(f"{tag} missing or an LFS pointer: {path}")
        return None
    arm, acts = load(path)
    frame0 = rest_frame(arm)
    out = dict(path=os.path.relpath(path, REPO) if path.startswith(REPO) else path, clips_in_file=len(acts),
               forward=[r3(x) for x in frame0[0]], right=[r3(x) for x in frame0[1]])
    for c in clips:
        if c not in acts:
            say(f"{tag} has no clip {c}")
            continue
        try:
            out[c] = attack(tag, arm, acts[c], c, frame0) if c.startswith("Attack") else loco(tag, arm, acts[c], c, frame0, kk_leg)
        except Exception as e:
            import traceback
            out[c] = "failed " + traceback.format_exc()[-800:]
            say(f"{tag} {c} measure failed {e!r}")
    return out


def poses(path):
    """Per clip, per frame, local quaternions of every bone (for the bake-vs-committed comparison)."""
    arm, acts = load(path)
    d = {}
    for c, a in acts.items():
        fr, _, _ = sample(arm, a, ("hips", "toes.l", "toes.r", "hand.r", "handslot.r"))
        d[c] = fr
    return d


def compare(tag, pa, pb):
    A, B = poses(pa), poses(pb)
    diff = {}
    for c in sorted(set(A) & set(B)):
        if len(A[c]) != len(B[c]):
            diff[c] = dict(frames=[len(A[c]), len(B[c])])
            continue
        rot = pos = 0.0
        for x, y in zip(A[c], B[c]):
            for b in x["q"]:
                if b in y["q"]:
                    rot = max(rot, qang(x["q"][b], y["q"][b]))
            for b in x["p"]:
                if b in y["p"]:
                    pos = max(pos, (x["p"][b] - y["p"][b]).length)
        diff[c] = dict(max_bone_turn_deg=round(rot, 2), max_joint_move=r3(pos))
    changed = {c: v for c, v in diff.items() if v.get("max_bone_turn_deg", 99) > 0.5 or (v.get("max_joint_move") or 0) > 0.005}
    say(f"{tag}: clips that differ (> 0.5 deg or > 5 mm): {len(changed)} of {len(diff)} {sorted(changed)}; "
        f"only in one file: {sorted(set(A) ^ set(B))}")
    return dict(per_clip=diff, changed=sorted(changed), only_in_one=sorted(set(A) ^ set(B)))


# ---------------------------------------------------------------- bakes (parallel child processes)
bakes = json.loads(os.environ["CM_MEASURE_BAKES"]) if os.environ.get("CM_MEASURE_BAKES") else BAKES.get(PLAN, [])
procs = []
t0 = time.time()
for b in bakes:
    od = os.path.join(OUT, "bake_" + b["label"])
    os.makedirs(od, exist_ok=True)
    env = dict(os.environ)
    env.update({k: str(v) for k, v in b.get("env", {}).items()})
    lf = open(os.path.join(od, "bake.log"), "w")
    cmd = [bpy.app.binary_path, "-b", "--factory-startup", "--python-exit-code", "1", "--python",
           os.path.join(RIG, "bake_clips.py"), "--", od, REPO, b["cls"]]
    procs.append((b, od, subprocess.Popen(cmd, env=env, stdout=lf, stderr=subprocess.STDOUT), lf))
    say(f"bake started {b['label']} ({b['cls']}, env {b.get('env', {})})")

# ---------------------------------------------------------------- committed files (while the bakes run)
kk_leg = None
try:
    arm, _ = load(os.path.join(REPO, "Assets/_Game/Art/Characters/KayKit_Clips.fbx"))
    kk_leg = rest_frame(arm)[3]
    say(f"KayKit pack leg (upperleg to ankle, rest) {r3(kk_leg)} u")
except Exception as e:
    say("KayKit leg unreadable " + repr(e))
for tag, rel, clips in COMMITTED:
    REP["files"][tag] = measure(tag, os.path.join(REPO, rel), clips, kk_leg)

for b, od, p, lf in procs:
    try:
        rc = p.wait(timeout=max(60, 3000 - (time.time() - t0)))
    except subprocess.TimeoutExpired:
        p.kill()
        rc = "timeout"
    lf.close()
    fbx = os.path.join(od, b["cls"] + "_Clips.fbx")
    REP["bakes"][b["label"]] = dict(cls=b["cls"], env=b.get("env", {}), exit=rc, fbx_bytes=os.path.getsize(fbx) if os.path.exists(fbx) else 0,
                                    seconds=round(time.time() - t0))
    say(f"bake {b['label']} exit {rc}, {REP['bakes'][b['label']]['fbx_bytes']} bytes")
    if not os.path.exists(fbx):
        continue
    clips = ("Run", "Walk", "StrafeL", "StrafeR") if b["cls"] == "Healer" else ("Attack", "Run")
    REP["files"][b["label"]] = measure(b["label"], fbx, clips, kk_leg)
    committed = {"Healer": "Assets/_Game/Art/Healer/Healer_Clips.fbx", "Demolitions": "Assets/_Game/Art/Demolitions/Demolitions_Clips.fbx"}[b["cls"]]
    try:
        REP["compare"][b["label"] + "_vs_committed"] = compare(b["label"] + " vs committed", fbx, os.path.join(REPO, committed))
    except Exception as e:
        say(f"compare {b['label']} failed {e!r}")
REP["lines"] = LINES
with open(os.path.join(OUT, "clip_measure.json"), "w") as fh:
    json.dump(REP, fh, indent=1, default=str)
say("done; " + os.path.join(OUT, "clip_measure.json"))
