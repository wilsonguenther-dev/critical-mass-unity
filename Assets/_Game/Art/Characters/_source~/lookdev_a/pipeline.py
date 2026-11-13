"""Look-development A pipeline, plain Python (numpy, scipy, scikit-image, fast-simplification, xatlas). No Blender.

  python pipeline.py fields <work>     distance fields of every group on a dense lattice (the "sculpt")
  python pipeline.py preview <work>    painted orthographic previews straight from the fields (rest pose)
  python pipeline.py mesh <work>       high mesh (marching cubes), game mesh (decimated, smooth normals, UVs)
  python pipeline.py bake <work> [res] albedo / mask textures for the game mesh
"""
import json
import os
import sys
import time

import numpy as np
from scipy import ndimage as ndi

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import sdfkit  # noqa: E402
import warrior_sdf as W  # noqa: E402

F = np.float32


# ------------------------------------------------------------------------------------------------ fields
class Obj:
    """Loaded fields of one object: per-group distance, nearest group, global distance (with far extension)."""

    def __init__(self, work, name):
        z = np.load(os.path.join(work, f"fields_{name}.npz"))
        self.name = name
        self.lo, self.h = z["lo"].astype(F), float(z["h"])
        self.gids = [int(g) for g in z["gids"]]
        self.fld = {g: z[f"g{g}"].astype(F) for g in self.gids}
        self.d1, self.id1, self.d2, self.far = z["d1"].astype(F), z["id1"], z["d2"].astype(F), z["far"].astype(F)

    def ijk(self, P):
        return ((P - self.lo) / self.h).T

    def s(self, vol, P):
        return ndi.map_coordinates(vol, self.ijk(P), order=1, mode="nearest").astype(F)

    def grad(self, vol, P, e=None):
        e = e or self.h
        g = np.stack([self.s(vol, P + np.array(o, F) * e) - self.s(vol, P - np.array(o, F) * e)
                      for o in ((1, 0, 0), (0, 1, 0), (0, 0, 1))], 1)
        return g / np.maximum(np.linalg.norm(g, axis=1, keepdims=True), 1e-9)

    def group_sample(self, P, gid, fn):
        """Evaluate fn(vol, P_subset) per group id and scatter back."""
        out = None
        for g in np.unique(gid):
            m = gid == g
            v = fn(self.fld[int(g)], P[m])
            if out is None:
                out = np.zeros((len(P),) + v.shape[1:], F)
            out[m] = v
        return out


FLOW_SOFT, FLOW_K = ("skin", "navy", "cream", "trouser", "leather"), 0.045     # flesh and cloth: one continuous body
FLOW_DRAPE, DRAPE_K = ("teal",), 0.020                                           # scarf and tabard drape onto it
FLOW_SHELL = 0.035


def flow(g):
    """ONE BODY, NOT STACKED PARTS. The flesh and cloth groups are smooth-unioned into a single continuous surface
    (4.5 cm fillets: neck into shoulders, arm into bracer, hips into torso), the scarf and tabard join it with a
    2 cm fillet, and then that one surface is cut back into its materials along the bisector between the original
    parts. So materials meet in a clean seam ON one form instead of being separate lumps that touch."""
    ids = [W.GID[n] for n in FLOW_SOFT + FLOW_DRAPE if W.GID[n] in g.fields]
    raw = {i: g.fields[i].copy() for i in ids}
    U = None
    for n in FLOW_SOFT:
        i = W.GID[n]
        if i in raw:
            U = raw[i].copy() if U is None else sdfkit.smin(U, raw[i], FLOW_K)
    for n in FLOW_DRAPE:
        i = W.GID[n]
        if i in raw:
            U = sdfkit.smin(U, raw[i], DRAPE_K)
    for i in ids:
        other = np.minimum.reduce([raw[j] for j in ids if j != i])
        bis = (raw[i] - other) * 0.5
        g.fields[i] = np.maximum(U, np.minimum(bis, U + FLOW_SHELL)).astype(F)


def stage_fields(work):
    for name, (lo, hi, parts) in W.models().items():
        t0 = time.time()
        g = sdfkit.Grid(lo, hi, W.H if name == "body" else W.H * 0.75)
        npts = 0
        for grp, shape in parts:
            npts += g.add(W.GID[grp], shape)
        if name == "body":
            flow(g)
        gids = sorted(g.fields)
        d1 = np.full(g.n, 9, F)
        d2 = np.full(g.n, 9, F)
        id1 = np.zeros(g.n, np.uint8)
        for gid in gids:
            f = g.fields[gid]
            closer = f < d1
            d2 = np.where(closer, d1, np.minimum(d2, f))
            id1[closer] = gid
            d1 = np.where(closer, f, d1)
        far = ndi.distance_transform_edt(d1 > 0).astype(F) * g.h
        far = np.where(d1 < 0.03, d1, np.maximum(far, 0.03))
        out = {"lo": g.lo, "h": g.h, "gids": np.array(gids), "d1": d1.astype(np.float16), "id1": id1,
               "d2": d2.astype(np.float16), "far": far.astype(np.float16)}
        for gid in gids:
            out[f"g{gid}"] = g.fields[gid].astype(np.float16)
        np.savez(os.path.join(work, f"fields_{name}.npz"), **out)
        print(f"FIELDS {name} grid {g.n} parts {len(parts)} evals {npts} solid {(d1 < 0).mean():.3f} {time.time() - t0:.1f}s", flush=True)


# ------------------------------------------------------------------------------------------------ preview
def view(o, axis, sign, up_axis):
    """First surface seen looking along `axis` (sign=+1: from the low side toward +axis). Returns P (h,w,3), mask."""
    d = np.moveaxis(o.d1, axis, 0)
    if sign < 0:
        d = d[::-1]
    solid = d < 0
    hit = solid.any(0)
    k = np.argmax(solid, 0)
    k0 = np.maximum(k - 1, 0)
    a = np.take_along_axis(d, k0[None], 0)[0]
    b = np.take_along_axis(d, k[None], 0)[0]
    t = np.where(a > b, a / np.maximum(a - b, 1e-6), 0.0)
    depth = (k0 + np.clip(t, 0, 1)).astype(F)
    if sign < 0:
        depth = d.shape[0] - 1 - depth
    rest = [i for i in range(3) if i != axis]
    ii, jj = np.meshgrid(np.arange(d.shape[1]), np.arange(d.shape[2]), indexing="ij")
    idx = np.zeros(d.shape[1:] + (3,), F)
    idx[..., axis] = depth
    idx[..., rest[0]], idx[..., rest[1]] = ii, jj
    P = idx * o.h + o.lo
    # image: rows = up axis descending, cols = the other axis
    other = [i for i in rest if i != up_axis][0]
    if rest[0] == up_axis:
        P, hit = P[::-1], hit[::-1]
    else:
        P, hit = np.transpose(P, (1, 0, 2))[::-1], hit.T[::-1]
    # looking along +Y the character's left (+X) is on screen right: already so. Looking along -Y or -X mirror.
    flip = (axis == 1 and sign < 0) or (axis == 0 and sign > 0) or (axis == 2 and sign > 0)
    if flip:
        P, hit = P[:, ::-1], hit[:, ::-1]
    return np.ascontiguousarray(P), np.ascontiguousarray(hit), other


def view_hi(o, axis, sign, up_axis, K=3):
    """view() upsampled K times: depth is interpolated, then each ray is refined against the field (sphere tracing)."""
    P, hit, _ = view(o, axis, sign, up_axis)
    Pu = np.stack([ndi.zoom(np.where(hit, P[..., c], np.nan if c == axis else P[..., c]), K, order=1) for c in range(3)], -1)
    # fill depth outside the silhouette with the nearest valid depth so edge rays can still converge
    dep = np.where(hit, P[..., axis], np.nan)
    idx = ndi.distance_transform_edt(~hit, return_distances=False, return_indices=True)
    dep = dep[idx[0], idx[1]]
    Pu[..., axis] = ndi.zoom(dep, K, order=1)
    for c in range(3):
        if c != axis:
            Pu[..., c] = ndi.zoom(P[..., c], K, order=1)
    shp = Pu.shape[:2]
    pts = Pu.reshape(-1, 3).astype(F)
    vd = np.zeros(3, F)
    vd[axis] = sign
    pts = pts - vd * 0.03
    for _ in range(14):
        pts = pts + vd * np.clip(o.s(o.d1, pts), -0.01, 0.02)[:, None] * 0.9
    ok = np.abs(o.s(o.d1, pts)) < 0.0015
    return pts.reshape(shp + (3,)), ok.reshape(shp), vd


def stage_preview(work, K=3):
    import paint
    from PIL import Image
    bg = np.array([0.992, 0.965, 0.925], F)
    imgs = {}
    for name, views in (("body", ((1, 1, 2), (0, -1, 2), (1, -1, 2))), ("shield", ((2, -1, 1),)), ("cleaver", ((2, -1, 1),))):
        o = Obj(work, name)
        for axis, sign, up in views:
            P, hit, vd = view_hi(o, axis, sign, up, K)
            pts = P[hit]
            gid = ndi.map_coordinates(o.id1, o.ijk(pts), order=0, mode="nearest")
            col = np.zeros((len(pts), 3), F)
            for i in range(0, len(pts), 300000):
                res = paint.paint(o, pts[i:i + 300000], gid[i:i + 300000])
                col[i:i + 300000] = paint.preview_shade(o, pts[i:i + 300000], gid[i:i + 300000], res, vd)
            img = np.ones(P.shape[:2] + (3,), F) * bg
            img[hit] = col
            imgs[f"{name}_{axis}{sign}"] = Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8))
            imgs[f"{name}_{axis}{sign}"].save(os.path.join(work, f"prev_{name}_{axis}{sign}.png"))
    # one 960 px contact sheet: front (arms cropped), head close-up, side, back, shield, cleaver
    f = imgs["body_11"]
    w, h = f.size
    front = f.crop((int(w * 0.20), 0, int(w * 0.80), h))
    head = f.crop((int(w * 0.36), int(h * 0.02), int(w * 0.64), int(h * 0.45)))
    side = imgs["body_0-1"]
    back = imgs["body_1-1"].crop((int(w * 0.20), 0, int(w * 0.80), h))

    def fit(im, hh):
        return im.resize((max(1, int(im.width * hh / im.height)), hh), Image.LANCZOS)
    top = [fit(front, 620), fit(head, 620)]
    bot = [fit(side, 420), fit(back, 420), fit(imgs["shield_2-1"], 420), fit(imgs["cleaver_2-1"], 420)]
    W_ = max(sum(t.width for t in top), sum(t.width for t in bot))
    sheet = Image.new("RGB", (W_, 1040), (253, 246, 236))
    x = 0
    for t in top:
        sheet.paste(t, (x, 0))
        x += t.width
    x = 0
    for t in bot:
        sheet.paste(t, (x, 620))
        x += t.width
    if sheet.width > 960:
        sheet = sheet.resize((960, int(sheet.height * 960 / sheet.width)), Image.LANCZOS)
    sheet.save(os.path.join(work, "preview_sheet.jpg"), quality=62)
    print("PREVIEW", sheet.size)


if __name__ == "__main__":
    stage, work = sys.argv[1], sys.argv[2]
    os.makedirs(work, exist_ok=True)
    if stage == "fields":
        stage_fields(work)
    elif stage == "preview":
        stage_preview(work)
    elif stage == "mesh":
        import meshing
        meshing.run(work)
    elif stage == "bake":
        import meshing
        meshing.bake(work, int(sys.argv[3]) if len(sys.argv) > 3 else 2048)
