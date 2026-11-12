"""Fit the render-only capture stance and view angle of each bible framing to its clean bible mask.

  Blender -b --factory-startup --python-exit-code 1 -P fit_stance.py -- [views] [evals_per_view]

Coordinate descent over miner_art.stance parameters (view azimuth/elevation, arm aim points, pick direction and roll,
leg spread), scored by the same registered IoU as iou_gate.py (uniform scale + translation, crop window open on the
sides where the bible figure is not clipped), computed with numpy FFTs on Workbench silhouettes ~170 px tall.
Only the pose and the camera angle move; proportions are the model's. Writes stances.json next to this script
(render_miner.py reads it).
"""
import json
import math
import os
import random
import sys

sys.dont_write_bytecode = True
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from miner_art.stance import ARM_BONES, Poser  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
VIEWS = argv[0].split(",") if argv and argv[0] else ["front", "three-quarter", "profile", "back"]
EVALS = int(argv[1]) if len(argv) > 1 else 260
SJ = os.path.join(HERE, "stances.json")
MASK = {"front": "08-front", "three-quarter": "08-three-quarter", "profile": "08-profile", "back": "08-back",
        "pose-barricade": "08-pose-barricade", "front-pick": "08-front-pick"}
START = {
    "front": {"az": 0, "el": 6, "upperarm.r": [-0.50, 0.03, 0.90], "lowerarm.r": [-0.62, -0.14, 0.98],
              "upperarm.l": [0.47, 0.05, 0.90], "lowerarm.l": [0.57, -0.08, 0.84], "pick_up": [-0.10, -0.04, 1.0],
              "pick_roll": 0.0, "leg_spread": 0.0},
}
START["three-quarter"] = dict(START["front"], az=-32)
START["profile"] = dict(START["front"], az=-90, el=4)
START["back"] = dict(START["front"], az=180)
TMP = os.path.join(bpy.app.tempdir or "/tmp", "fit_sil.png")

bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE, "miner.blend"))
scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "FLAT"
scene.display.shading.color_type = "SINGLE"
scene.render.film_transparent = True
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.render.resolution_percentage = 100
arm = bpy.data.objects["Rig"]
arm.rotation_mode = "XYZ"
arm.rotation_euler = (0, 0, 0)
body, pick = bpy.data.objects["Miner_Body"], bpy.data.objects["Miner_PickHammer"]
for ob in scene.objects:
    if ob.type == "MESH":
        ob.hide_render = ob not in (body, pick)
        ob.hide_set(False)
cam_data = bpy.data.cameras.new("FitCam")
cam_data.type = "ORTHO"
cam = bpy.data.objects.new("FitCam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
poser = Poser(arm, scene)


def points():
    dg = bpy.context.evaluated_depsgraph_get()
    pts = []
    for ob in (body, pick):
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        mw = ev.matrix_world
        co = np.empty(len(me.vertices) * 3, np.float32)
        me.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        M = np.array(mw)
        pts.append(co @ M[:3, :3].T + M[:3, 3])
        ev.to_mesh_clear()
    return np.concatenate(pts)


def silhouette(st, H=170):
    poser.apply(st)
    a, e = math.radians(st["az"]), math.radians(st["el"])
    d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
    P = points()
    c = Vector(P.mean(0))
    cam.location = c + d * 12.0
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    bpy.context.view_layer.update()
    inv = np.array(cam.matrix_world.inverted())
    cp = P @ inv[:3, :3].T + inv[:3, 3]
    w, h = np.ptp(cp[:, 0]), np.ptp(cp[:, 1])
    pad = 0.03 * max(w, h)
    w, h = w + 2 * pad, h + 2 * pad
    cx, cy = (cp[:, 0].max() + cp[:, 0].min()) / 2, (cp[:, 1].max() + cp[:, 1].min()) / 2
    cam.location = cam.matrix_world @ Vector((cx, cy, 0))
    scene.render.resolution_y = H
    scene.render.resolution_x = max(16, int(round(H * w / h)))
    cam_data.ortho_scale = max(w, h)
    scene.render.filepath = TMP
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(TMP, check_existing=False)
    px = np.empty(img.size[0] * img.size[1] * 4, np.float32)
    img.pixels.foreach_get(px)
    W_, H_ = img.size
    bpy.data.images.remove(img)
    m = px.reshape(H_, W_, 4)[::-1, :, 3] > 0.5
    ys, xs = np.nonzero(m)
    return m[ys.min():ys.max() + 1, xs.min():xs.max() + 1]


def load_bible(name, target_h=150):
    img = bpy.data.images.load(os.path.join(HERE, "bible_masks", name + ".png"))
    W_, H_ = img.size
    px = np.empty(W_ * H_ * 4, np.float32)
    img.pixels.foreach_get(px)
    m = px.reshape(H_, W_, 4)[::-1, :, 0] > 0.5
    ys, xs = np.nonzero(m)
    k = target_h / (ys.max() - ys.min() + 1)
    nh, nw = max(2, int(round(H_ * k))), max(2, int(round(W_ * k)))
    yi = np.minimum((np.arange(nh) / k).astype(int), H_ - 1)
    xi = np.minimum((np.arange(nw) / k).astype(int), W_ - 1)
    return m[yi][:, xi]


def resize(m, h, w):
    yi = np.minimum((np.arange(h) * m.shape[0] / h).astype(int), m.shape[0] - 1)
    xi = np.minimum((np.arange(w) * m.shape[1] / w).astype(int), m.shape[1] - 1)
    return m[yi][:, xi]


def reg_iou(bm, om, n_scales=13):
    H_, W_ = bm.shape
    ys, xs = np.nonzero(bm)
    by0, by1, bx0, bx1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    A = bm.sum()
    base = (by1 - by0) / om.shape[0]
    best = 0.0
    for s in np.linspace(base * 0.8, base * 1.2, n_scales):
        oh, ow = max(2, int(round(om.shape[0] * s))), max(2, int(round(om.shape[1] * s)))
        o = resize(om, oh, ow).astype(np.float32)
        P = max(oh, ow) + 1
        Bp = np.zeros((H_ + 2 * P, W_ + 2 * P), np.float32)
        Bp[P:P + H_, P:P + W_] = bm
        Wp = np.zeros_like(Bp)
        r0 = 0 if by0 > 1 else P
        r1 = H_ + 2 * P if by1 < H_ - 1 else P + H_
        c0 = 0 if bx0 > 1 else P
        c1 = W_ + 2 * P if bx1 < W_ - 1 else P + W_
        Wp[r0:r1, c0:c1] = 1.0
        G = np.conj(np.fft.rfft2(o, Bp.shape))
        inter = np.fft.irfft2(np.fft.rfft2(Bp) * G, Bp.shape)
        inwin = np.fft.irfft2(np.fft.rfft2(Wp) * G, Bp.shape)
        vy, vx = Bp.shape[0] - oh + 1, Bp.shape[1] - ow + 1
        inter, inwin = inter[:vy, :vx], inwin[:vy, :vx]
        iou = inter / np.maximum(1.0, A + inwin - inter)
        best = max(best, float(iou.max()))
    return best


def vec_params(st):
    keys = []
    for k in ("az", "el", "pick_roll", "leg_spread", "lean", "twist", "head_yaw", "head_tilt"):
        keys.append((k, None))
    for b in ARM_BONES:
        for i in range(3):
            keys.append((b, i))
    for i in range(3):
        keys.append(("pick_up", i))
    return keys


STEP = {"az": 6.0, "el": 4.0, "pick_roll": 20.0, "leg_spread": 4.0, "pick_up": 0.25, "lean": 4.0, "twist": 6.0,
        "head_yaw": 8.0, "head_tilt": 5.0}
LIMIT = {"el": (-5, 20), "leg_spread": (0, 14), "lean": (-12, 12), "twist": (-25, 25), "head_yaw": (-30, 30),
         "head_tilt": (-15, 15)}


def fit(view, st, evals):
    rng = random.Random(803)
    bm = load_bible(MASK[view])
    cache = {}

    def score(s):
        key = json.dumps(s, sort_keys=True)
        if key not in cache:
            cache[key] = reg_iou(bm, silhouette(s))
        return cache[key]

    best = score(st)
    start = best
    steps = {k: STEP.get(k[0], 0.12) for k in vec_params(st)}
    n = 1
    while n < evals:
        improved = False
        for key in vec_params(st):
            k, i = key
            for sgn in (1, -1):
                cand = json.loads(json.dumps(st))
                if i is None:
                    v = cand.get(k, 0.0) + sgn * steps[key]
                    if k in LIMIT:
                        v = min(max(v, LIMIT[k][0]), LIMIT[k][1])
                    cand[k] = round(v, 3)
                else:
                    cand[k][i] = round(cand[k][i] + sgn * steps[key], 4)
                sc = score(cand)
                n += 1
                if sc > best + 1e-4:
                    best, st, improved = sc, cand, True
                    break
            if n >= evals:
                break
        if not improved:   # before shrinking the steps, try a few joint random moves (escapes ridge-shaped optima)
            for _ in range(8):
                cand = json.loads(json.dumps(st))
                for (k, i), sz in steps.items():
                    if i is None:
                        v = cand.get(k, 0.0) + rng.uniform(-1, 1) * sz
                        if k in LIMIT:
                            v = min(max(v, LIMIT[k][0]), LIMIT[k][1])
                        cand[k] = round(v, 3)
                    else:
                        cand[k][i] = round(cand[k][i] + rng.uniform(-1, 1) * sz, 4)
                sc = score(cand)
                n += 1
                if sc > best + 1e-4:
                    best, st, improved = sc, cand, True
                    break
        if not improved:
            steps = {k: v * 0.5 for k, v in steps.items()}
            if max(steps.values()) < 0.01:
                break
    print(f"FIT {view} {start:.3f} -> {best:.3f} after {n} evals")
    return st, best


def main():
    data = json.load(open(SJ)) if os.path.exists(SJ) else {}
    for v in VIEWS:
        st0 = data.get(v, {}).get("stance") or START.get(v) or START["front"]
        st, best = fit(v, st0, EVALS)
        data[v] = {"stance": st, "fit_iou_150px": round(best, 3)}
        with open(SJ, "w") as f:
            json.dump(data, f, indent=1)


main()
