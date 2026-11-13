"""High mesh, game mesh, UV atlas and texture bake (plain Python; called from pipeline.py)."""
import json
import os
import time

import numpy as np
from scipy import ndimage as ndi
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components

import warrior_sdf as W
from pipeline import Obj

F = np.float32
# game-mesh triangle budgets and per-material weights (more where the eye goes: face, hair, shield, brass)
BUDGET = {"body": 15500, "shield": 4200, "cleaver": 1500}
WEIGHT = {"skin": 3.2, "hair": 1.25, "brass": 1.0, "wrap": 0.6, "trouser": 0.8, "navy": 0.75, "wood_seam": 0.3, "leather_dark": 0.6, "wood": 1.3,
          "glow": 1.2, "stone": 1.5}
TEXEL = {"skin_head": 2.6, "shield": 0.85, "cleaver": 1.0}
HARD = {"stone", "stone_blade", "wood", "iron", "glow", "brass", "wood_seam"}     # flat planes keep flat shading normals


def clean(v, f):
    used = np.unique(f)
    remap = np.full(len(v), -1, np.int64)
    remap[used] = np.arange(len(used))
    return v[used], remap[f]


def components(nv, f):
    a = coo_matrix((np.ones(len(f) * 3), (np.concatenate([f[:, 0], f[:, 1], f[:, 2]]),
                                          np.concatenate([f[:, 1], f[:, 2], f[:, 0]]))), shape=(nv, nv))
    return connected_components(a, directed=False)[1]


def area(v, f):
    return 0.5 * np.linalg.norm(np.cross(v[f[:, 1]] - v[f[:, 0]], v[f[:, 2]] - v[f[:, 0]]), axis=1)


def hidden(o, vol, v):
    d1, d2 = o.s(o.d1, v), o.s(o.d2, v)
    other = np.where(np.abs(o.s(vol, v) - d1) < o.h * 0.6, d2, d1)
    return other < -0.020


def run(work):
    from skimage import measure
    import fast_simplification
    import xatlas
    stats = {"objects": {}}
    atlas = xatlas.Atlas()
    order = []
    low = {}
    for name in ("body", "shield", "cleaver"):
        t0 = time.time()
        o = Obj(work, name)
        hv, hf, parts = [], [], []
        nh = 0
        for gid in o.gids:
            vol = o.fld[gid]
            if vol.min() >= 0:
                continue
            v, f, _, _ = measure.marching_cubes(vol, 0.0, spacing=(o.h,) * 3)
            v = (v + o.lo).astype(F)
            f = f.astype(np.int64)
            fn = np.cross(v[f[:, 1]] - v[f[:, 0]], v[f[:, 2]] - v[f[:, 0]])
            g = o.grad(vol, v[f].mean(1))
            if (fn * g).sum() < 0:
                f = f[:, ::-1]
            hv.append(v)
            hf.append(f + nh)
            nh += len(v)
            # visible share of this shell (the rest is buried more than 2 cm inside another material)
            vis = area(v, f)[~(hidden(o, vol, v)[f]).all(1)].sum()
            parts.append((gid, v, f, float(vis), float(area(v, f).sum())))
        hv, hf = np.concatenate(hv), np.concatenate(hf)
        np.savez(os.path.join(work, f"high_{name}.npz"), v=hv, f=hf.astype(np.int32))
        wsum = sum(a * WEIGHT.get(W.GROUPS[g], 1.0) for g, _, _, a, _ in parts)
        V, Fc, Gd, Nn, Cp, CN = [], [], [], [], [], []
        nv = 0
        ncomp = 0
        for gid, v, f, a, atot in parts:
            if a <= 0:
                continue
            want = max(60, int(BUDGET[name] * a * WEIGHT.get(W.GROUPS[gid], 1.0) / wsum))
            # decimate the CLOSED shell (open borders collapse into spikes), then drop the buried faces
            target = int(want * min(1.6, atot / a))
            for agg in (5, 7, 9, 10, 10):
                if len(f) <= target * 1.08:
                    break
                v, f = fast_simplification.simplify(v.astype(F), f.astype(np.int32), target_count=target, agg=agg)
                v, f = clean(np.asarray(v, F), np.asarray(f, np.int64))
            if len(f) > target * 1.25:             # the first decimator stalls on thin slabs: second engine, same algorithm
                import pyfqmr
                ms = pyfqmr.Simplify()
                ms.setMesh(v.astype(np.float64), f.astype(np.int32))
                ms.simplify_mesh(target_count=target, aggressiveness=8, preserve_border=False, verbose=False, max_iterations=200)
                v2, f2, _ = ms.getMesh()
                v, f = clean(np.asarray(v2, F), np.asarray(f2, np.int64))
            vol = o.fld[gid]
            for _ in range(2):                     # pull the decimated vertices back onto the true surface
                v = v - o.grad(vol, v) * o.s(vol, v)[:, None]
            f = f[~(hidden(o, vol, v)[f]).all(1)]
            v, f = clean(v, f)
            comp = components(len(v), f)
            ca = np.bincount(comp[f[:, 0]], area(v, f), minlength=comp.max() + 1)
            f = f[(ca > 3e-4)[comp[f[:, 0]]]]
            v, f = clean(v, f)
            print(f"  {name}.{W.GROUPS[gid]} want {want} got {len(f)}", flush=True)
            n = o.grad(vol, v, o.h * 1.5)
            comp = components(len(v), f)
            if W.GROUPS[gid] in HARD:
                # shading normal of each corner = sculpt normal a third of the way into its face: big flat faces
                # shade flat, bevels and curves stay smooth (what a face-weighted normal modifier does, from the field)
                tri = v[f]
                q = (tri * 0.62 + tri.mean(1, keepdims=True) * 0.38).reshape(-1, 3).astype(F)
                q = q - o.grad(vol, q) * o.s(vol, q)[:, None]
                CN.append(o.grad(vol, q, o.h * 1.5).reshape(-1, 3, 3))
            else:
                CN.append(n[f])
            V.append(v.astype(F))
            Fc.append(f + nv)
            Gd.append(np.full(len(v), gid, np.int32))
            Nn.append(n)
            Cp.append(comp + ncomp)
            ncomp += comp.max() + 1
            nv += len(v)
        V, Fc, Gd, Nn, Cp = np.concatenate(V), np.concatenate(Fc), np.concatenate(Gd), np.concatenate(Nn), np.concatenate(Cp)
        low[name] = dict(v=V, f=Fc, gid=Gd, n=Nn, comp=Cp, cn=np.concatenate(CN))
        # atlas: head skin gets more texels; one xatlas mesh per density class
        fg = Gd[Fc[:, 0]]
        if name == "body":
            headf = (fg == W.GID["skin"]) & (V[Fc].mean(1)[:, 2] > 1.25)
            sets = [("skin_head", headf), ("body", ~headf)]
        else:
            sets = [(name, np.ones(len(Fc), bool))]
        for key, m in sets:
            atlas.add_mesh((V * TEXEL.get(key, 1.0)).astype(F), Fc[m].astype(np.uint32), Nn)
            order.append((name, np.where(m)[0]))
        stats["objects"][name] = {"high_tris": int(len(hf)), "high_verts": int(len(hv)), "game_tris": int(len(Fc)),
                                  "game_verts_before_uv_split": int(len(V)), "components": int(ncomp)}
        print(f"MESH {name} high {len(hf)} tris, game {len(Fc)} tris {len(V)} verts, {time.time() - t0:.1f}s", flush=True)
    co = xatlas.ChartOptions()
    co.max_cost = 9.0
    co.normal_deviation_weight = 1.0
    co.max_iterations = 2
    po = xatlas.PackOptions()
    po.resolution = 2048
    po.padding = 6
    po.bilinear = True
    po.create_image = False
    t0 = time.time()
    atlas.generate(co, po)
    print(f"ATLAS {atlas.width}x{atlas.height} charts {atlas.chart_count} util {atlas.utilization} {time.time() - t0:.1f}s", flush=True)
    out = {}
    for i, (name, fidx) in enumerate(order):
        vmap, idx, uv = atlas[i]
        d = out.setdefault(name, dict(v=[], f=[], uv=[], gid=[], n=[], comp=[], cn=[], nv=0))
        L = low[name]
        d["v"].append(L["v"][vmap])
        d["n"].append(L["n"][vmap])
        d["gid"].append(L["gid"][vmap])
        d["comp"].append(L["comp"][vmap])
        d["uv"].append(uv.astype(F))
        assert len(idx) == len(fidx)
        d["cn"].append(L["cn"][fidx])
        d["f"].append(idx.astype(np.int64) + d["nv"])
        d["nv"] += len(vmap)
    for name, d in out.items():
        arr = {k: np.concatenate(d[k]) for k in ("v", "f", "uv", "gid", "n", "comp", "cn")}
        np.savez(os.path.join(work, f"low_{name}.npz"), **arr)
        stats["objects"][name]["game_verts"] = int(len(arr["v"]))
    stats["atlas"] = {"charts": int(atlas.chart_count), "utilization": float(np.mean(atlas.utilization))}
    stats["game_tris_total"] = sum(s["game_tris"] for s in stats["objects"].values())
    stats["game_verts_total"] = sum(s["game_verts"] for s in stats["objects"].values())
    stats["high_tris_total"] = sum(s["high_tris"] for s in stats["objects"].values())
    json.dump(stats, open(os.path.join(work, "mesh_stats.json"), "w"), indent=1)
    print("MESH_STATS", json.dumps(stats))


def raster(uv, f, res):
    """Texel -> (face, barycentric). uv in [0,1], origin bottom-left; image row 0 = v 0 (Blender order)."""
    fid = np.full((res, res), -1, np.int32)
    bary = np.zeros((res, res, 3), F)
    p = uv * res - 0.5
    for i in range(len(f)):
        a, b, c = p[f[i]]
        x0, x1 = int(max(0, np.floor(min(a[0], b[0], c[0])))), int(min(res - 1, np.ceil(max(a[0], b[0], c[0]))))
        y0, y1 = int(max(0, np.floor(min(a[1], b[1], c[1])))), int(min(res - 1, np.ceil(max(a[1], b[1], c[1]))))
        if x1 < x0 or y1 < y0:
            continue
        den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(den) < 1e-12:
            continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
        w0 = ((b[1] - c[1]) * (xs - c[0]) + (c[0] - b[0]) * (ys - c[1])) / den
        w1 = ((c[1] - a[1]) * (xs - c[0]) + (a[0] - c[0]) * (ys - c[1])) / den
        w2 = 1 - w0 - w1
        e = -0.6 / max(1.0, np.sqrt(abs(den)))        # a little conservative so chart borders are covered
        m = (w0 >= e) & (w1 >= e) & (w2 >= e)
        if m.any():
            yy, xx = ys[m], xs[m]
            fid[yy, xx] = i
            bary[yy, xx] = np.stack([w0[m], w1[m], w2[m]], 1)
    return fid, bary


def bake(work, res=2048):
    import paint
    from PIL import Image
    t0 = time.time()
    alb = np.zeros((res, res, 3), F)
    msk = np.zeros((res, res, 4), F)
    filled = np.zeros((res, res), bool)
    for name in ("body", "shield", "cleaver"):
        o = Obj(work, name)
        z = np.load(os.path.join(work, f"low_{name}.npz"))
        fid, bary = raster(z["uv"], z["f"], res)
        m = fid >= 0
        tri = z["f"][fid[m]]
        bc = np.clip(bary[m], 0, 1)
        bc /= bc.sum(1, keepdims=True)
        P = (z["v"][tri] * bc[..., None]).sum(1).astype(F)
        gid = z["gid"][tri[:, 0]]
        for g in np.unique(gid):                     # texel centre -> the true sculpt surface
            mm = gid == g
            vol = o.fld[int(g)]
            for _ in range(2):
                P[mm] = P[mm] - o.grad(vol, P[mm]) * o.s(vol, P[mm])[:, None]
        r = {k: [] for k in ("albedo", "cloth", "glow", "emis", "ao")}
        for i in range(0, len(P), 400000):
            rr = paint.paint(o, P[i:i + 400000], gid[i:i + 400000])
            for k in r:
                r[k].append(rr[k])
        r = {k: np.concatenate(v) for k, v in r.items()}
        alb[m] = r["albedo"]
        msk[m] = np.stack([r["cloth"], r["glow"], r["emis"], r["ao"]], 1)
        filled |= m
        print(f"BAKE {name} texels {int(m.sum())} {time.time() - t0:.1f}s", flush=True)
    idx = ndi.distance_transform_edt(~filled, return_distances=False, return_indices=True)
    alb = alb[idx[0], idx[1]]
    msk = msk[idx[0], idx[1]]
    for nm, arr in (("Warrior_LD_Albedo", alb), ("Warrior_LD_Mask", msk)):
        im = Image.fromarray((np.clip(arr[::-1], 0, 1) * 255 + 0.5).astype(np.uint8))
        im.save(os.path.join(work, f"{nm}_{res}.png"))
        im.resize((res // 2, res // 2), Image.LANCZOS).save(os.path.join(work, f"{nm}_{res // 2}.png"))
    json.dump({"res": res, "coverage": float(filled.mean())}, open(os.path.join(work, "bake_stats.json"), "w"))
    print("BAKED coverage %.3f %.1fs" % (filled.mean(), time.time() - t0))
