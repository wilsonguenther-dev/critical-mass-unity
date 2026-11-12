"""Fit a pose to a bible crop by silhouette IoU, inside one Blender session (no clip rebuild per try).

    Blender -b "Assets/_Game/Art/Kaiju/_source~/Kaiju.blend" --python ".../pose_fit.py" -- <spec.json> <out.json>

spec.json: {"stage": "Adult", "az": 60, "el": 8, "mask": "<bible mask .npy (bool, page pixels)>",
            "params": {"name": [init, step, lo, hi], ...}, "pose": "<python expr of P -> {"rot": .., "loc": ..}>",
            "rounds": 3, "az_range": [lo, hi]}
The pose expression uses the same convention as build_kaiju.key_clips (rotations about armature-space axes, degrees;
locations in design units times the stage scale). The silhouette is measured as compose_kaiju.py does: our mask
(+1 px outer line, holes filled) scaled to the bible subject's height and bottom-centred on it. Writes the best
parameters and IoU to out.json; build_kaiju.py carries the fitted numbers as literals (this tool is not run by the build).
"""
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Quaternion, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
SPEC = json.load(open(argv[0]))
OUTP = argv[1]
X, Y, Z = (1, 0, 0), (0, 1, 0), (0, 0, 1)
scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.render.film_transparent = True
scene.render.resolution_x = scene.render.resolution_y = 300
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
ob = next(o for o in bpy.data.objects if o.get("cm_stage") == SPEC["stage"] and o.type == "MESH")
for o in bpy.data.objects:
    o.hide_render = o is not ob
rig = ob.parent
rig.location = (0, 0, 0)
rig.rotation_euler = (0, 0, 0)
if rig.animation_data:
    rig.animation_data.action = None
    for tr in rig.animation_data.nla_tracks:
        tr.mute = True
SCALE = rig.get("cm_scale", 1.0)
REST = {pb.name: pb.bone.matrix_local.to_3x3().to_quaternion() for pb in rig.pose.bones}
for pb in rig.pose.bones:
    pb.rotation_mode = "QUATERNION"
cam_data = bpy.data.cameras.new("FitCam")
cam = bpy.data.objects.new("FitCam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
BM = np.load(SPEC["mask"])
ys, xs = np.nonzero(BM)
BB = (xs.min(), ys.min(), xs.max() + 1, ys.max() + 1)
TMP = os.path.join(os.path.dirname(OUTP), "_fit.png")


def apply_pose(pose):
    for pb in rig.pose.bones:
        q = Quaternion()
        for axis, deg in pose.get("rot", {}).get(pb.name, []):
            q = q @ (REST[pb.name].inverted() @ Quaternion(Vector(axis), math.radians(deg)) @ REST[pb.name])
        pb.rotation_quaternion = q
        loc = pose.get("loc", {}).get(pb.name)
        pb.location = (REST[pb.name].inverted() @ (Vector(loc) * SCALE)) if loc else Vector((0, 0, 0))
    bpy.context.view_layer.update()


def frame(az, el):
    a, e = math.radians(az), math.radians(el)
    d = Vector((-math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
    rot = (-d).to_track_quat("-Z", "Y")
    R = rot.to_matrix()
    right, up = R.col[0], R.col[1]
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = ev.to_mesh()
    pts = [ob.matrix_world @ v.co for v in me.vertices]
    ev.to_mesh_clear()
    xs_ = [w.dot(right) for w in pts]
    ys_ = [w.dot(up) for w in pts]
    cx, cy = (min(xs_) + max(xs_)) / 2, (min(ys_) + max(ys_)) / 2
    centre = sum(pts, Vector()) / len(pts)
    cam_data.type = "ORTHO"
    cam_data.clip_start, cam_data.clip_end = 0.1, 300.0
    cam.location = right * cx + up * cy + d * (centre.dot(d) + 60.0)
    cam.rotation_mode = "QUATERNION"
    cam.rotation_quaternion = rot
    cam_data.ortho_scale = max(max(xs_) - min(xs_), max(ys_) - min(ys_)) * 1.08


def fill_holes(m):
    bg = ~m
    reach = np.zeros_like(m)
    reach[0, :], reach[-1, :], reach[:, 0], reach[:, -1] = bg[0, :], bg[-1, :], bg[:, 0], bg[:, -1]
    while True:
        g = reach.copy()
        g[1:] |= reach[:-1]; g[:-1] |= reach[1:]; g[:, 1:] |= reach[:, :-1]; g[:, :-1] |= reach[:, 1:]
        g &= bg
        if (g == reach).all():
            return ~reach
        reach = g


def iou(az, el):
    frame(az, el)
    scene.render.filepath = TMP
    bpy.ops.render.render(write_still=True)
    im = bpy.data.images.load(TMP, check_existing=False)
    w, h = im.size
    a = np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4)[::-1, :, 3] > 0.5
    bpy.data.images.remove(im)
    d = a.copy()
    d[1:] |= a[:-1]; d[:-1] |= a[1:]; d[:, 1:] |= a[:, :-1]; d[:, :-1] |= a[:, 1:]
    m = fill_holes(d)
    yy, xx = np.nonzero(m)
    oy0, oy1, ox0, ox1 = yy.min(), yy.max() + 1, xx.min(), xx.max() + 1
    bx0, by0, bx1, by1 = BB
    sc = (by1 - by0) / (oy1 - oy0)
    W = max(1, round((ox1 - ox0) * sc))
    H = by1 - by0
    ri = np.minimum((np.arange(H) / sc).astype(int) + oy0, oy1 - 1)
    ci = np.minimum((np.arange(W) / sc).astype(int) + ox0, ox1 - 1)
    sub = m[ri][:, ci]
    om = np.zeros_like(BM)
    px = (bx0 + bx1) // 2 - W // 2
    x0, x1 = max(0, px), min(BM.shape[1], px + W)
    om[by0:by1, x0:x1] = sub[:, x0 - px:x1 - px]
    return float((om & BM).sum() / max(1, (om | BM).sum()))


POSE = eval("lambda P: " + SPEC["pose"])
params = {k: v[0] for k, v in SPEC["params"].items()}
az = SPEC["az"]
el = SPEC["el"]


def score(p, az_):
    apply_pose(POSE(p))
    return iou(az_, SPEC["el"])


best = score(params, az)
print("FIT start IoU %.3f" % best)
for rnd in range(SPEC.get("rounds", 3)):
    for k, (init, step, lo, hi) in SPEC["params"].items():
        st = step / (2 ** rnd)
        for d in (1, -1):
            improved = True
            while improved:
                improved = False
                t = dict(params)
                t[k] = min(hi, max(lo, params[k] + d * st))
                if t[k] == params[k]:
                    break
                v = score(t, az)
                if v > best + 1e-4:
                    best, params, improved = v, t, True
    if "az_range" in SPEC:
        for daz in (5, -5, 10, -10):
            a2 = min(SPEC["az_range"][1], max(SPEC["az_range"][0], az + daz / (rnd + 1)))
            v = score(params, a2)
            if v > best + 1e-4:
                best, az = v, a2
    if "el_range" in SPEC:
        for de in (2, -2, 4, -4):
            e2 = min(SPEC["el_range"][1], max(SPEC["el_range"][0], el + de / (rnd + 1)))
            SPEC["el"], keep = e2, SPEC["el"]
            v = score(params, az)
            if v > best + 1e-4:
                best, el = v, e2
            else:
                SPEC["el"] = keep
    SPEC["el"] = el
    print("FIT round %d IoU %.3f az %g el %g %s" % (rnd, best, az, el, json.dumps({k: round(v, 2) for k, v in params.items()})))
json.dump({"iou": best, "az": az, "el": el, "params": {k: round(v, 2) for k, v in params.items()}}, open(OUTP, "w"), indent=1)
