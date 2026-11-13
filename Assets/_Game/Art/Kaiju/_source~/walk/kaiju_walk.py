"""Kaiju adult walk, re-authored at the source (fix/kaiju-alive-1, kaiju-walk-1, 2026-11-13).

    blender -b --factory-startup --python kaiju_walk.py -- IN.fbx OUT_DIR [--no-render]

Why. The shipped Walk was a sinusoidal stomp in place: its planted feet swept back at ~0.9 m/s while the game moves the
Kaiju at up to 2.6 m/s (KaijuConfig.MoveSpeed), and the game's "Foot_L"/"Foot_R" transforms were the KNEES (the rig's
Foot bone ran knee -> sole), so nothing that measured or pinned "the foot" ever touched the ground point.

What this does, in one Blender process:
  A. Import IN.fbx (the shipped Kaiju_Adult.fbx: LOD-pin meshes + the Kaiju pin's clips), measure the old Walk (contact
     paths per limb, implied stride vs the stride the sim speed needs, the in-game slide the fixed-route test would read)
     and render the "before" contact sheets.
  B. Rig: each hind leg Foot_X (knee -> sole) is renamed Shin_X and a new Foot_X bone (ankle at the sole -> toes, level)
     is added under it, carrying the lower foot mass (height ramp over the old Foot weights, max 4 influences, all four
     meshes: Hero, LOD0, LOD1, Shadow). Every other bone name, the other 7 clips and the meshes are unchanged; the old
     clips' Foot_X curves follow the rename to Shin_X, so they pose exactly as before.
  C. Walk: a lateral-sequence knuckle-walk (LH, LF, RH, RF) authored in world space with analytic two-bone IK per limb
     (Thigh+Shin to the planted ankle, Arm+Forearm to the planted wrist, Fist aimed at the planted knuckles) and baked to
     FK keys every frame. Each planted contact holds its world point through stance (it moves back relative to the body
     at exactly V_DESIGN); swing = clear lift, reach, heavy plant. Body: crouched, bob with a drop on each hind plant,
     sway and roll over the stance side, hip yaw, chest counter-yaw and lagged pitch (shell and spines ride it), head
     steadied with a small lead, tail S-wave. Cycle length chosen so playback rate 1.0 = V_DESIGN.
  D. Export with the LOD pipeline's Kaiju FBX settings (pose_position POSE: never the rest-pose bake), re-import the file
     in a fresh scene, verify bones, clips, lengths, motion per clip, influences, then measure and render "after".
Writes OUT_DIR/Kaiju_Adult.fbx, walk_report.json, walk-*.jpg, blender.log lines tagged [WALK].
"""
import bpy, bmesh, sys, os, math, json, time
import numpy as np
from mathutils import Vector, Matrix, Quaternion

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SRC, OUT = os.path.abspath(argv[0]), os.path.abspath(argv[1])
RENDER = "--no-render" not in argv
os.makedirs(OUT, exist_ok=True)
REPORT = {"src": SRC, "started": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}

V_DESIGN = 2.6          # KaijuConfig.MoveSpeed (u/s): playback rate 1.0 walks exactly this fast
V_ROUTE = 2.2           # the fixed-route test's straight-leg speed
HEIGHT = 5.0            # ModelSpec Kaiju_Adult Height
TOL = 0.015 * HEIGHT    # KaijuAnimator.Measure stance tolerance
BAND = 0.3              # NothingGlides RouteStanceBand
UP = Vector((0, 0, 1))

KAIJU_FBX = dict(object_types={"ARMATURE", "MESH"}, global_scale=1.0, apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=False,
    use_mesh_modifiers=False, mesh_smooth_type="FACE", use_tspace=False, colors_type="SRGB", prioritize_active_color=True,
    add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X", use_armature_deform_only=False, bake_anim=True,
    bake_anim_use_all_bones=True, bake_anim_use_nla_strips=True, bake_anim_use_all_actions=False,
    bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=1.0, path_mode="STRIP",
    embed_textures=False)


def log(*a):
    print("[WALK]", " ".join(str(x) for x in a), flush=True)


def save_report():
    with open(os.path.join(OUT, "walk_report.json"), "w") as f:
        json.dump(REPORT, f, indent=1, default=lambda o: list(o) if isinstance(o, Vector) else str(o))


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_fbx(path):
    bpy.ops.import_scene.fbx(filepath=path, bake_space_transform=False, use_anim=True, use_custom_normals=True,
                             colors_type="SRGB", ignore_leaf_bones=False, automatic_bone_orientation=False,
                             primary_bone_axis="Y", secondary_bone_axis="X")
    rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    acts = {a.name.split("|")[-1]: a for a in bpy.data.actions}
    return rig, meshes, acts


def clip_of(a):
    return a.name.split("|")[-1]


def use_action(rig, act):
    ad = rig.animation_data or rig.animation_data_create()
    for t in ad.nla_tracks:
        t.mute = True
    ad.action = act
    if act is not None:
        try:
            if ad.action_slot is None and len(act.slots):
                ad.action_slot = act.slots[0]
        except Exception as e:
            log("slot", e)


def frames_of(act):
    f0, f1 = act.frame_range
    return int(round(f0)), int(round(f1))


def bone_points(rig, names):
    """World head/tail of pose bones (current evaluated pose)."""
    M = rig.matrix_world
    return {n: (M @ rig.pose.bones[n].head, M @ rig.pose.bones[n].tail) for n in names if n in rig.pose.bones}


def axes(rig):
    pb = rig.pose.bones
    M = rig.matrix_world
    hips = M @ rig.data.bones["Hips"].head_local
    head = M @ rig.data.bones["Head"].head_local
    f = head - hips
    f.z = 0
    f.normalize()
    return f, f.cross(UP).normalized()      # forward, right


def sample_clip(rig, act, spec):
    """spec: {label: (bone, 'head'|'tail')}. Returns frames list and {label: [Vector per frame]} (world, rig static)."""
    use_action(rig, act)
    sc = bpy.context.scene
    f0, f1 = frames_of(act)
    out = {k: [] for k in spec}
    for f in range(f0, f1 + 1):
        sc.frame_set(f)
        bpy.context.view_layer.update()
        pts = bone_points(rig, {b for b, _ in spec.values()})
        for k, (b, end) in spec.items():
            out[k].append(pts[b][0 if end == "head" else 1].copy())
    return list(range(f0, f1 + 1)), out


def interp(track, t, fps):
    n = len(track) - 1
    L = n / fps
    x = (t % L) * fps
    i = min(int(x), n - 1)
    a = x - i
    return track[i].lerp(track[i + 1], a)


def unity_measure(track_l, track_r, fps, fwd):
    """KaijuAnimator.Measure: 48 samples, stance = y < min + TOL at both ends, mean horizontal speed (u/s)."""
    n = 48
    L = (len(track_l) - 1) / fps
    sp, cnt, contacts = 0.0, 0, []
    for tr in (track_l, track_r):
        p = [interp(tr, L * k / n, fps) for k in range(n + 1)]
        y = [q.z for q in p]
        mn, mx = min(y), max(y)
        if mx - mn < TOL:
            continue
        for k in range(n):
            a, b = y[k] < mn + TOL, y[k + 1] < mn + TOL
            if a and b:
                d = p[k + 1] - p[k]
                sp += math.hypot(d.x, d.y) / (L / n)
                cnt += 1
            if not a and b:
                contacts.append((k + 1) / n)
    return (sp / cnt if cnt else 0.0), sorted(contacts)


def stance_stats(track, fps, fwd):
    """Contact path during stance (lowest TOL band): back-sweep speed relative to the body, sweep length, duty."""
    y = [p.z for p in track[:-1]]
    mn = min(y)
    st = [i for i, v in enumerate(y) if v < mn + TOL]
    n = len(y)
    speeds, back = [], []
    for i in st:
        j = (i + 1) % n
        if j in st:
            d = track[j] - track[i]
            speeds.append(math.hypot(d.x, d.y) * fps)
            back.append(-d.dot(fwd) * fps)
    along = [track[i].dot(fwd) for i in st] or [0]
    return dict(duty=round(len(st) / n, 3), back_speed=round(float(np.mean(back)) if back else 0.0, 3),
                speed=round(float(np.mean(speeds)) if speeds else 0.0, 3), sweep=round(max(along) - min(along), 3),
                lift=round(max(y) - mn, 3), min_z=round(mn, 3))


def sim_slide(tl, tr, fps, fwd, v, rate, seconds=12.0, dt=1 / 30.0, window=30):
    """The fixed-route test's rule on a straight walk at v: 30-tick windows, a foot counts while it is within BAND of its
    window minimum (relative to the body root) at both ticks AND is the lower foot; slide = foot travel / body travel."""
    T = int(seconds / dt)
    P = [[], []]
    B = []
    for k in range(T):
        t = k * dt
        body = fwd * (v * t)
        B.append(body)
        for f, tr_ in enumerate((tl, tr)):
            P[f].append(body + interp(tr_, rate * t, fps))
    foot = bodyt = 0.0
    for w0 in range(0, T - window + 1, window):
        for f in range(2):
            p, o = P[f], P[1 - f]
            mn = min(p[t].z for t in range(w0, w0 + window))
            for t in range(w0 + 1, w0 + window):
                if p[t].z > mn + BAND or p[t - 1].z > mn + BAND:
                    continue
                if p[t].z > o[t].z or p[t - 1].z > o[t - 1].z:
                    continue
                df, db = p[t] - p[t - 1], B[t] - B[t - 1]
                foot += math.hypot(df.x, df.y)
                bodyt += math.hypot(db.x, db.y)
    return round(foot / bodyt, 3) if bodyt > 0 else None, round(bodyt, 2)


def analyse(rig, act, spec, tag):
    fps = bpy.context.scene.render.fps / bpy.context.scene.render.fps_base
    fwd, right = axes(rig)
    frames, tr = sample_clip(rig, act, spec)
    n = len(frames) - 1
    T = n / fps
    res = dict(frames=n, fps=fps, seconds=round(T, 4))
    for k in tr:
        res[k] = stance_stats(tr[k], fps, fwd)
    vU, contacts = unity_measure(tr["unityL"], tr["unityR"], fps, fwd)
    res["unity_walk_speed"] = round(vU, 3)
    res["unity_stride_m"] = round(vU * T, 3)
    res["needed_stride_m_at_2.6"] = round(V_DESIGN * T, 3)
    res["contacts"] = [round(c, 3) for c in contacts]
    for v in (V_ROUTE, V_DESIGN):
        rate = max(0.25, min(3.2, v / vU)) if vU > 1e-3 else 1.0
        sU, bt = sim_slide(tr["unityL"], tr["unityR"], fps, fwd, v, rate)
        sS, _ = sim_slide(tr["soleL"], tr["soleR"], fps, fwd, v, rate)
        res["slide_at_%.1f" % v] = dict(rate=round(rate, 3), unity_foot=sU, sole=sS, body_m=bt)
    REPORT[tag] = res
    log(tag, json.dumps(res))
    save_report()
    return tr, fps, fwd, right, vU


# ---------------------------------------------------------------------------------------------------------- rendering
def col_mesh(name, bm, rgb):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ca = me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    for d in ca.data:
        d.color_srgb = (rgb[0], rgb[1], rgb[2], 1.0)
    me.color_attributes.active_color = ca
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def ground(z, x0, x1, fwd, right):
    me = bpy.data.meshes.new("Ground")
    bm = bmesh.new()
    s = 0.5
    cols = []
    for i in range(int((x1 - x0) / s)):
        for j in range(-12, 12):
            o = fwd * (x0 + i * s) + right * (j * s)
            vs = [bm.verts.new(o + fwd * a + right * b + Vector((0, 0, z))) for a, b in ((0, 0), (s, 0), (s, s), (0, s))]
            bm.faces.new(vs)
            dark = ((i + j) % 2 == 0)
            major = (i % 2 == 0 and abs(int(x0 / s) + i) % 4 == 0)
            cols.append((0.32, 0.36, 0.30) if dark else (0.45, 0.50, 0.42))
    bm.to_mesh(me)
    bm.free()
    ca = me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    for poly, c in zip(me.polygons, cols):
        for li in poly.loop_indices:
            ca.data[li].color_srgb = (c[0], c[1], c[2], 1.0)
    me.color_attributes.active_color = ca
    ob = bpy.data.objects.new("Ground", me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def discs(name, pts, r, rgb, z_off=0.01, flat=True):
    bm = bmesh.new()
    for p in pts:
        if flat:
            bmesh.ops.create_circle(bm, cap_ends=True, segments=10, radius=r,
                                    matrix=Matrix.Translation(Vector((p.x, p.y, p.z + z_off))))
        else:
            bmesh.ops.create_uvsphere(bm, u_segments=10, v_segments=6, radius=r, matrix=Matrix.Translation(p))
    return col_mesh(name, bm, rgb)


def setup_render():
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light = "STUDIO"
    sh.color_type = "VERTEX"
    sh.show_shadows = True
    sh.shadow_intensity = 0.35
    sh.show_object_outline = False
    sc.render.resolution_x, sc.render.resolution_y = 560, 420
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = "PNG"
    if sc.world is None:
        sc.world = bpy.data.worlds.new("W")
    sc.world.color = (0.62, 0.68, 0.72)
    try:
        sc.view_settings.view_transform = "Standard"
    except Exception:
        pass


def compose(tiles, cols, path):
    imgs = []
    for p in tiles:
        im = bpy.data.images.load(p)
        w, h = im.size
        a = np.empty(w * h * 4, dtype=np.float32)
        im.pixels.foreach_get(a)
        imgs.append(a.reshape(h, w, 4))
        bpy.data.images.remove(im)
    h, w = imgs[0].shape[:2]
    rows = (len(imgs) + cols - 1) // cols
    g = 6
    W, H = cols * w + (cols + 1) * g, rows * h + (rows + 1) * g
    big = np.ones((H, W, 4), dtype=np.float32) * 0.12
    big[..., 3] = 1
    for k, a in enumerate(imgs):
        r, c = divmod(k, cols)
        y0 = H - (r + 1) * (h + g)          # Blender pixels are bottom-up: first tile top-left
        x0 = g + c * (w + g)
        big[y0:y0 + h, x0:x0 + w] = a
    out = bpy.data.images.new("sheet", W, H, alpha=False)
    out.pixels.foreach_set(big.ravel())
    out.filepath_raw = path
    out.file_format = "JPEG"
    bpy.context.scene.render.image_settings.quality = 88
    out.save()
    bpy.data.images.remove(out)
    for p in tiles:
        os.remove(p)


def render_sheets(rig, meshes, act, tracks, fps, fwd, right, vU, tag):
    """8 frames over one cycle at the game's walk speed (V_DESIGN, playback rate = V / measured stride speed), body moved
    through the world, camera following; ground checker 0.5 m; red/blue = left/right hind contact (sole), yellow =
    knuckles; discs = every place a contact touched the ground so far (a stationary plant = one disc, a slide = a smear)."""
    sc = bpy.context.scene
    setup_render()
    for m in meshes:
        m.hide_render = "Hero" not in m.name
    hero = next(m for m in meshes if "Hero" in m.name)
    ca = hero.data.color_attributes.get("Col")
    if ca is not None:
        hero.data.color_attributes.active_color = ca
    rate = max(0.25, min(3.2, V_DESIGN / vU)) if vU > 1e-3 else 1.0
    L = (len(tracks["soleL"]) - 1) / fps
    cycle = L / rate
    M0 = rig.matrix_world.copy()
    zg = min(min(p.z for p in tracks[k]) for k in ("soleL", "soleR")) - 0.12
    zg = min(zg, min((hero.matrix_world @ Vector(c)).z for c in hero.bound_box))
    g = ground(zg, -6.0, V_DESIGN * cycle * 1.6 + 8.0, fwd, right)
    # dense contact history (30 Hz) over 1.25 cycles before the sheet's first frame so the planted feet show discs
    hist = {k: [] for k in ("soleL", "soleR", "knuckL", "knuckR")}
    dt = 1 / 60.0
    t = -0.25 * cycle
    while t <= cycle + 1e-6:
        body = fwd * (V_DESIGN * t)
        for k in hist:
            tr = tracks[k]
            mn = min(p.z for p in tr)
            p = body + interp(tr, rate * t, fps)
            if p.z < mn + TOL:
                hist[k].append((t, Vector((p.x, p.y, zg))))
        t += dt
    cam_d = bpy.data.cameras.new("Cam")
    cam_d.type = "ORTHO"
    cam = bpy.data.objects.new("Cam", cam_d)
    sc.collection.objects.link(cam)
    sc.camera = cam
    paths = {}
    for view, elev, ortho in (("game", 55.0, 9.0), ("side", 6.0, 7.5)):
        cam_d.ortho_scale = ortho
        tiles = []
        for k in range(8):
            tk = cycle * k / 8.0
            body = fwd * (V_DESIGN * tk)
            ft = rate * tk
            rig.matrix_world = Matrix.Translation(body) @ M0
            # the clip is sampled at frame f0 + ft*fps: evaluate the action at that (fractional) frame
            f0, _ = frames_of(act)
            fr = f0 + (ft % L) * fps
            sc.frame_set(int(math.floor(fr)), subframe=fr - math.floor(fr))
            bpy.context.view_layer.update()
            tmp = []
            cols = {"soleL": (0.95, 0.15, 0.12), "soleR": (0.15, 0.35, 0.98), "knuckL": (0.98, 0.85, 0.1),
                    "knuckR": (0.98, 0.6, 0.05)}
            for key, c in cols.items():
                pts = [p for (tt, p) in hist[key] if tt <= tk + 1e-6]
                if pts:
                    tmp.append(discs("d_" + key, pts, 0.11, c))
                cur = body + interp(tracks[key], ft, fps)
                tmp.append(discs("m_" + key, [cur], 0.13, c, flat=False))
            centre = body + Vector((0, 0, zg + 1.8)) + fwd * 0.6
            back = -right   # camera on the Kaiju's left side looking across: walking to screen-left/right
            e = math.radians(elev)
            pos = centre + (back * math.cos(e) + UP * math.sin(e)) * 30.0
            cam.location = pos
            cam.rotation_euler = (centre - pos).to_track_quat("-Z", "Y").to_euler()
            p = os.path.join(OUT, "_tile_%s_%s_%d.png" % (tag, view, k))
            sc.render.filepath = p
            bpy.ops.render.render(write_still=True)
            tiles.append(p)
            for o in tmp:
                me = o.data
                bpy.data.objects.remove(o)
                bpy.data.meshes.remove(me)
        path = os.path.join(OUT, "walk-%s-%s.jpg" % (tag, view))
        compose(tiles, 4, path)
        paths[view] = path
    rig.matrix_world = M0
    bpy.data.objects.remove(g)
    bpy.data.objects.remove(cam)
    REPORT.setdefault("sheets", {})[tag] = dict(paths=paths, rate=round(rate, 3), cycle_s=round(cycle, 3))
    log("sheets", tag, paths)


# --------------------------------------------------------------------------------------------------------- phase A
reset()
rig, meshes, acts = import_fbx(SRC)
sc = bpy.context.scene
log("fps", sc.render.fps, sc.render.fps_base, "rig", rig.name, tuple(round(x, 4) for x in rig.matrix_world.to_scale()),
    "meshes", [m.name for m in meshes], "actions", {k: frames_of(a) for k, a in acts.items()})
REPORT["input"] = dict(fps=sc.render.fps, bones=[b.name for b in rig.data.bones],
                       parents={b.name: (b.parent.name if b.parent else None) for b in rig.data.bones},
                       actions={k: frames_of(a) for k, a in acts.items()}, meshes=[m.name for m in meshes])
Mw = rig.matrix_world
REPORT["input"]["rest"] = {b.name: [[round(x, 3) for x in Mw @ b.head_local], [round(x, 3) for x in Mw @ b.tail_local]]
                           for b in rig.data.bones}
hero = next(m for m in meshes if "Hero" in m.name)
bb = [hero.matrix_world @ Vector(c) for c in hero.bound_box]
REPORT["input"]["hero_z"] = [round(min(p.z for p in bb), 3), round(max(p.z for p in bb), 3)]
log("rest", json.dumps(REPORT["input"]["rest"]))
OLD = dict(unityL=("Foot_L", "head"), unityR=("Foot_R", "head"), soleL=("Foot_L", "tail"), soleR=("Foot_R", "tail"),
           knuckL=("Fist_L", "tail"), knuckR=("Fist_R", "tail"))
walk_old = acts["Walk"]
trA, fps, fwd, right, vA = analyse(rig, walk_old, OLD, "before")
if RENDER:
    try:
        render_sheets(rig, meshes, walk_old, trA, fps, fwd, right, vA, "before")
    except Exception as e:
        import traceback; traceback.print_exc(); log("render before failed", e)
        REPORT["render_before_error"] = str(e)

# --------------------------------------------------------------------------------------------------------- phase B: rig
use_action(rig, None)
for pb in rig.pose.bones:
    pb.matrix_basis = Matrix.Identity(4)
bpy.context.view_layer.update()
Mw = rig.matrix_world.copy()
Mi = Mw.inverted()
fwd, right = axes(rig)
bpy.context.view_layer.objects.active = rig
for o in bpy.context.view_layer.objects:
    o.select_set(o == rig)
bpy.ops.object.mode_set(mode="EDIT")
eb = rig.data.edit_bones
ankle_w = {}
for s in ("_L", "_R"):
    old = eb["Foot" + s]
    old.name = "Shin" + s
    shin = eb["Shin" + s]
    a_w = Mw @ shin.tail
    ankle_w[s] = a_w.copy()
    nb = eb.new("Foot" + s)
    nb.head = shin.tail.copy()
    nb.tail = Mi @ (a_w + fwd * 0.4)
    nb.align_roll(Mi.to_3x3() @ UP)
    nb.parent = shin
    nb.use_connect = False
    nb.use_deform = True
bpy.ops.object.mode_set(mode="OBJECT")
# animation paths and vertex groups follow the rename (done by Blender for linked data; enforced here for all actions)
renamed = 0
for a in bpy.data.actions:
    try:
        bags = [cb for l in a.layers for st in l.strips for cb in st.channelbags]
    except Exception:
        bags = []
    for cb in bags:
        for fc in cb.fcurves:
            for s in ("_L", "_R"):
                o, n = 'pose.bones["Foot%s"]' % s, 'pose.bones["Shin%s"]' % s
                if fc.data_path.startswith(o):
                    fc.data_path = n + fc.data_path[len(o):]
                    renamed += 1
        for gr in cb.groups:
            for s in ("_L", "_R"):
                if gr.name == "Foot" + s:
                    gr.name = "Shin" + s
REPORT["renamed_curves"] = renamed
wstats = {}
for m in meshes:
    vg = m.vertex_groups
    for s in ("_L", "_R"):
        if vg.get("Shin" + s) is None and vg.get("Foot" + s) is not None:
            vg["Foot" + s].name = "Shin" + s
        gs = vg.get("Shin" + s)
        if gs is None:
            continue
        gf = vg.get("Foot" + s) or vg.new(name="Foot" + s)
        za = ankle_w[s].z
        moved = 0
        for v in m.data.vertices:
            w = next((g.weight for g in v.groups if g.group == gs.index), 0.0)
            if w <= 0:
                continue
            dz = (m.matrix_world @ v.co).z - za
            u = min(1.0, max(0.0, (0.42 - dz) / (0.42 - 0.16)))
            a = u * u * (3 - 2 * u)          # 1 below 16 cm over the sole, 0 above 42 cm
            if a <= 0:
                continue
            gf.add([v.index], w * a, "REPLACE")
            if a >= 1:
                gs.remove([v.index])
            else:
                gs.add([v.index], w * (1 - a), "REPLACE")
            moved += 1
        wstats[m.name + s] = moved
    # at most 4 influences, normalised
    for v in m.data.vertices:
        gl = sorted(((g.weight, g.group) for g in v.groups if g.weight > 0), reverse=True)
        if len(gl) > 4:
            for w, gi in gl[4:]:
                m.vertex_groups[gi].remove([v.index])
            gl = gl[:4]
        tot = sum(w for w, _ in gl)
        if tot > 0 and abs(tot - 1) > 1e-4:
            for w, gi in gl:
                m.vertex_groups[gi].add([v.index], w / tot, "REPLACE")
REPORT["foot_weights_moved"] = wstats
log("rig: renamed curves", renamed, "foot verts", wstats)

# --------------------------------------------------------------------------------------------------------- phase C: walk
pbs = rig.pose.bones
for m in meshes:
    m.hide_viewport = True             # no mesh evaluation while posing (speed); restored before the export
for pb in pbs:
    pb.rotation_mode = "QUATERNION"
    pb.matrix_basis = Matrix.Identity(4)
bpy.context.view_layer.update()
R3 = Mw.to_3x3()
R3i = R3.inverted()
def W(b, end="head"):
    bo = rig.data.bones[b]
    return Mw @ (bo.head_local if end == "head" else bo.tail_local)
rest = {}
for s in ("_L", "_R"):
    rest["hip" + s], rest["knee" + s], rest["ankle" + s] = W("Thigh" + s), W("Shin" + s), W("Shin" + s, "tail")
    rest["sho" + s], rest["elb" + s], rest["wri" + s], rest["knu" + s] = W("Arm" + s), W("Forearm" + s), W("Fist" + s), W("Fist" + s, "tail")
L = {}
for s in ("_L", "_R"):
    L["th" + s] = (rest["knee" + s] - rest["hip" + s]).length
    L["sh" + s] = (rest["ankle" + s] - rest["knee" + s]).length
    L["ar" + s] = (rest["elb" + s] - rest["sho" + s]).length
    L["fa" + s] = (rest["wri" + s] - rest["elb" + s]).length
fist_vec = {s: rest["knu" + s] - rest["wri" + s] for s in ("_L", "_R")}
hips_rest = W("Hips")
def along(p):
    return (p - hips_rest).dot(fwd)
def lat(p):
    return (p - hips_rest).dot(right)

# Gait: lateral sequence, touchdown phases. Hind duty > 0.5 so a hind foot is always down (the lower-foot rule).
PH = {"LH": 0.0, "LF": 0.25, "RH": 0.5, "RF": 0.75}
BETA_H, BETA_F = 0.58, 0.64
LIFT_H, LIFT_F = 0.42, 0.46
DROP, BOB = 0.30, 0.055
CENTRE_H = {s: along(rest["hip" + s]) + 0.12 for s in ("_L", "_R")}
CENTRE_F = {s: along(rest["knu" + s]) - 0.15 for s in ("_L", "_R")}


def smooth(x):
    x = min(1.0, max(0.0, x))
    return x * x * (3 - 2 * x)


def limb_path(u, phase, beta, S, centre, lift):
    """Contact point offset along fwd (relative to the body) and lift height, for cycle phase u."""
    s = (u - phase) % 1.0
    if s < beta:
        return centre + S * beta / 2 - S * s, 0.0, -1.0
    q = (s - beta) / (1 - beta)
    x0, x1 = centre - S * beta / 2, centre + S * beta / 2
    m = -S * (1 - beta)
    h00, h10, h01, h11 = 2 * q ** 3 - 3 * q ** 2 + 1, q ** 3 - 2 * q ** 2 + q, -2 * q ** 3 + 3 * q ** 2, q ** 3 - q ** 2
    x = h00 * x0 + h10 * m + h01 * x1 + h11 * m
    qq = q ** 0.8
    z = lift * math.sin(math.pi * qq) ** 0.75
    return x, z, q


def body_offsets(u):
    """Hips offset (world vector) and rotations (axis, deg) list for the body bones."""
    p = math.tau * u
    bob = -DROP - BOB * math.cos(2 * p - math.tau * 2 * 0.06)       # lowest just after each hind plant (u .06, .56)
    impact = sum(-0.03 * math.exp(-((u - ph) % 1.0) / 0.035) for ph in (PH["LH"], PH["RH"]))
    sway = 0.07 * math.sin(p - 0.4)                                  # over the left hind while it bears (u 0 - .5)
    surge = 0.03 * math.sin(2 * p - 0.9)
    off = UP * (bob + impact) - right * sway + fwd * surge
    rot = {
        "Hips": [(fwd, -3.0 * math.sin(p - 0.4)), (UP, 4.0 * math.sin(p + 1.2))],
        "Spine": [(right, 2.0 * math.cos(2 * p - 1.0)), (UP, -2.5 * math.sin(p + 1.2))],
        "Chest": [(right, 2.5 * math.cos(2 * p - 2.0)), (UP, -4.0 * math.sin(p + 0.6)), (fwd, 2.0 * math.sin(p - 1.2))],
        "Neck": [(right, -2.0 * math.cos(2 * p - 2.4)), (UP, 3.5 * math.sin(p + 0.2))],
        "Head": [(right, -2.5 * math.cos(2 * p - 2.9)), (UP, 3.0 * math.sin(p - 0.2)), (fwd, -2.0 * math.sin(p - 1.2))],
        "Jaw": [(right, 3.0 + 2.5 * max(0.0, math.cos(2 * p - 0.5)))],
        "Tail1": [(UP, 9.0 * math.sin(p - 0.9)), (right, 3.0 * math.cos(2 * p - 1.2))],
        "Tail2": [(UP, 11.0 * math.sin(p - 1.6)), (right, 3.0 * math.cos(2 * p - 1.9))],
        "Tail3": [(UP, 13.0 * math.sin(p - 2.3)), (right, 3.5 * math.cos(2 * p - 2.6))],
    }
    return off, rot


def set_dir(pb, want_world):
    """Rotate pose bone pb (minimal rotation) so its Y axis points along want_world; keeps its head where it is."""
    M = pb.matrix.copy()
    y = M.col[1].xyz.normalized()
    d = (R3i @ want_world).normalized()
    q = y.rotation_difference(d)
    R = q.to_matrix() @ M.to_3x3()
    N = R.to_4x4()
    N.translation = M.translation
    pb.matrix = N
    bpy.context.view_layer.update()


def set_world_rot(pb, q_world_from_rest):
    """Bone orientation = world rotation applied to its REST orientation (feet: level, toes forward)."""
    M = pb.matrix.copy()
    Rrest = rig.data.bones[pb.name].matrix_local.to_3x3()
    Rw = R3 @ Rrest                         # world rest orientation (with object scale)
    Rn = q_world_from_rest.to_matrix() @ Rw
    N = (R3i @ Rn).to_4x4()
    N.translation = M.translation
    pb.matrix = N
    bpy.context.view_layer.update()


def two_bone(root, target, l1, l2, pole):
    v = target - root
    d = v.length
    dmax = (l1 + l2) * 0.999
    reach = d / (l1 + l2)
    d = min(max(d, abs(l1 - l2) + 1e-4), dmax)
    di = v.normalized()
    ca = max(-1.0, min(1.0, (l1 * l1 + d * d - l2 * l2) / (2 * l1 * d)))
    a = math.acos(ca)
    pp = pole - di * pole.dot(di)
    pp = pp.normalized() if pp.length > 1e-6 else fwd.copy()
    mid = root + di * (l1 * math.cos(a)) + pp * (l1 * math.sin(a))
    return mid, root + di * d, reach


def pole_of(a, b, c):
    di = (c - a).normalized()
    v = (b - a) - di * (b - a).dot(di)
    return v.normalized() if v.length > 1e-4 else fwd.copy()


POLE = {}
for s in ("_L", "_R"):
    POLE["leg" + s] = pole_of(rest["hip" + s], rest["knee" + s], rest["ankle" + s])
    POLE["arm" + s] = pole_of(rest["sho" + s], rest["elb" + s], rest["wri" + s])


def pose_frame(u, S, record):
    for pb in pbs:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    off, rot = body_offsets(u)
    hp = pbs["Hips"]
    loc_arm = R3i @ off
    hp.location = rig.data.bones["Hips"].matrix_local.to_3x3().inverted() @ loc_arm
    for b, lst in rot.items():
        if b not in pbs:
            continue
        q = Quaternion()
        for ax, deg in lst:
            q = Quaternion(ax, math.radians(deg)) @ q
        Rrest = rig.data.bones[b].matrix_local.to_3x3()
        qa = (R3i @ q.to_matrix() @ R3)                     # world-axis rotation in armature space
        pbs[b].rotation_quaternion = (Rrest.inverted() @ qa @ Rrest).to_quaternion()
    bpy.context.view_layer.update()
    worst = 0.0
    contacts = {}
    for s, (kh, kf) in (("_L", ("LH", "LF")), ("_R", ("RH", "RF"))):
        # hind leg: ankle target
        x, z, q = limb_path(u, PH[kh], BETA_H, S, CENTRE_H[s], LIFT_H)
        tgt = hips_rest + fwd * x + right * lat(rest["ankle" + s]) + UP * ((rest["ankle" + s] - hips_rest).z + z)
        hip = rig.matrix_world @ pbs["Thigh" + s].head
        mid, end, reach = two_bone(hip, tgt, L["th" + s], L["sh" + s], POLE["leg" + s])
        worst = max(worst, reach)
        set_dir(pbs["Thigh" + s], mid - hip)
        set_dir(pbs["Shin" + s], end - (rig.matrix_world @ pbs["Shin" + s].head))
        pitch = 0.0 if q < 0 else 16.0 * math.sin(math.pi * q) - 6.0 * math.sin(math.pi * min(1.0, q * 2))
        set_world_rot(pbs["Foot" + s], Quaternion(right, math.radians(-pitch)))
        contacts["ankle" + s] = tgt
        # fore limb: knuckle path, wrist above it (fist curls back in swing)
        x, z, q = limb_path(u, PH[kf], BETA_F, S, CENTRE_F[s], LIFT_F)
        knu = hips_rest + fwd * x + right * lat(rest["knu" + s]) + UP * ((rest["knu" + s] - hips_rest).z + z)
        curl = 0.0 if q < 0 else 38.0 * math.sin(math.pi * q)
        fv = Quaternion(right, math.radians(-curl)) @ fist_vec[s]
        wri = knu - fv
        sho = rig.matrix_world @ pbs["Arm" + s].head
        mid, end, reach = two_bone(sho, wri, L["ar" + s], L["fa" + s], POLE["arm" + s])
        worst = max(worst, reach)
        set_dir(pbs["Arm" + s], mid - sho)
        set_dir(pbs["Forearm" + s], end - (rig.matrix_world @ pbs["Forearm" + s].head))
        set_dir(pbs["Fist" + s], knu - (rig.matrix_world @ pbs["Fist" + s].head))
        contacts["knu" + s] = knu
    if record is not None:
        record.append({pb.name: (pb.rotation_quaternion.copy(), pb.location.copy()) for pb in pbs})
    return worst, contacts


fps = sc.render.fps / sc.render.fps_base
# stride: the longest that every limb reaches (<= 96 % of its length at every frame), then whole frames at V_DESIGN
best = None
for S in [x * 0.1 for x in range(40, 14, -1)]:
    w = max(pose_frame(k / 16.0, S, None)[0] for k in range(16))
    if w <= 0.96:
        best = S
        break
S0 = best or 1.6
N = max(16, int(round(S0 / V_DESIGN * fps)))
S = V_DESIGN * N / fps
worst = max(pose_frame(k / N, S, None)[0] for k in range(N))
if worst > 0.985:
    N -= 1
    S = V_DESIGN * N / fps
    worst = max(pose_frame(k / N, S, None)[0] for k in range(N))
REPORT["design"] = dict(stride_m=round(S, 3), frames=N, seconds=round(N / fps, 4), v=V_DESIGN, worst_reach=round(worst, 3),
                        beta_h=BETA_H, beta_f=BETA_F, lift_h=LIFT_H, lift_f=LIFT_F, drop=DROP, bob=BOB, lengths={k: round(v, 3) for k, v in L.items()})
log("design", json.dumps(REPORT["design"]))
rec = []
contact_log = []
for k in range(N + 1):
    wst, c = pose_frame((k % N) / N, S, rec)
    contact_log.append(c)
# write the new Walk action (keys every frame, quaternion signs continuous)
from bpy_extras import anim_utils
old_name = walk_old.name
walk_old.name = old_name + "_old"
act = bpy.data.actions.new(old_name)
act.use_fake_user = True
slot = act.slots.new(id_type="OBJECT", name=rig.name)
cb = anim_utils.action_ensure_channelbag_for_slot(act, slot)
for pb in pbs:
    qs = [r[pb.name][0] for r in rec]
    for i in range(1, len(qs)):
        if qs[i].dot(qs[i - 1]) < 0:
            qs[i] = -qs[i]
    ls = [r[pb.name][1] for r in rec]
    for idx in range(4):
        fc = cb.fcurves.ensure('pose.bones["%s"].rotation_quaternion' % pb.name, index=idx, group_name=pb.name)
        fc.keyframe_points.add(len(qs))
        fc.keyframe_points.foreach_set("co", [c for k, q in enumerate(qs) for c in (float(k), q[idx])])
        for kp in fc.keyframe_points:
            kp.interpolation = "LINEAR"
        fc.update()
    for idx in range(3):
        fc = cb.fcurves.ensure('pose.bones["%s"].location' % pb.name, index=idx, group_name=pb.name)
        fc.keyframe_points.add(len(ls))
        fc.keyframe_points.foreach_set("co", [c for k, l in enumerate(ls) for c in (float(k), l[idx])])
        for kp in fc.keyframe_points:
            kp.interpolation = "LINEAR"
        fc.update()
bpy.data.actions.remove(walk_old)
for pb in pbs:
    pb.matrix_basis = Matrix.Identity(4)

# --------------------------------------------------------------------------------------------------------- export
for m in meshes:
    m.hide_viewport = False
ad = rig.animation_data or rig.animation_data_create()
ad.action = None
for t in list(ad.nla_tracks):
    ad.nla_tracks.remove(t)
for a in sorted(bpy.data.actions, key=lambda a: clip_of(a)):
    clip = clip_of(a)
    tr = ad.nla_tracks.new(); tr.name = clip
    st = tr.strips.new(clip, int(a.frame_range[0]), a); st.name = clip
    try:
        if st.action_slot is None and len(a.slots):
            st.action_slot = a.slots[0]
    except Exception:
        pass
rig.data.pose_position = "POSE"        # NEVER export at REST: that bakes every take at the rest pose
for o in bpy.context.view_layer.objects:
    o.select_set(o == rig or o in meshes)
bpy.context.view_layer.objects.active = rig
out_fbx = os.path.join(OUT, "Kaiju_Adult.fbx")
r = bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=True, **KAIJU_FBX)
REPORT["export"] = dict(path=out_fbx, result=list(r), bytes=os.path.getsize(out_fbx))
log("export", REPORT["export"])
save_report()

# --------------------------------------------------------------------------------------------------------- verify + after
reset()
rig, meshes, acts = import_fbx(out_fbx)
sc = bpy.context.scene
ver = dict(fps=sc.render.fps, bones=len(rig.data.bones), names=[b.name for b in rig.data.bones],
           actions={k: frames_of(a) for k, a in acts.items()}, meshes={})
for m in meshes:
    mi = max((len([g for g in v.groups if g.weight > 1e-6]) for v in m.data.vertices), default=0)
    unw = sum(1 for v in m.data.vertices if not any(g.weight > 1e-6 for g in v.groups))
    ver["meshes"][m.name] = dict(verts=len(m.data.vertices), tris=sum(len(p.vertices) - 2 for p in m.data.polygons),
                                 max_influences=mi, unweighted=unw, colors=[c.name for c in m.data.color_attributes],
                                 uvs=[u.name for u in m.data.uv_layers], groups=len(m.vertex_groups))
# motion per clip: the largest rotation range of any bone (a rest-pose bake reads ~0)
motion = {}
for k, a in acts.items():
    use_action(rig, a)
    f0, f1 = frames_of(a)
    base = {}
    rng = {}
    for f in range(f0, f1 + 1):
        sc.frame_set(f)
        for pb in rig.pose.bones:
            q = pb.matrix.to_quaternion()
            if pb.name not in base:
                base[pb.name] = q
            rng[pb.name] = max(rng.get(pb.name, 0.0), math.degrees(base[pb.name].rotation_difference(q).angle))
    top = sorted(rng.items(), key=lambda kv: -kv[1])[:3]
    motion[k] = [(n, round(v, 1)) for n, v in top]
ver["motion_deg"] = motion
REPORT["verify"] = ver
log("verify", json.dumps(ver))
save_report()
NEW = dict(unityL=("Foot_L", "head"), unityR=("Foot_R", "head"), soleL=("Foot_L", "head"), soleR=("Foot_R", "head"),
           knuckL=("Fist_L", "tail"), knuckR=("Fist_R", "tail"))
trB, fps, fwd, right, vB = analyse(rig, acts["Walk"], NEW, "after")
if RENDER:
    try:
        render_sheets(rig, meshes, acts["Walk"], trB, fps, fwd, right, vB, "after")
    except Exception as e:
        import traceback; traceback.print_exc(); log("render after failed", e)
        REPORT["render_after_error"] = str(e)
REPORT["finished"] = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
save_report()
log("WALK_OK", json.dumps({k: REPORT[k] for k in ("design",)}), REPORT["before"]["slide_at_2.2"], REPORT["after"]["slide_at_2.2"])
