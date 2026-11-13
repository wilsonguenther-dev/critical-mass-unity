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
          "glow": 1.2, "stone": 1.25}
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


def decimate(v, f, target):
    """MeshLab quadric edge collapse. (Fast-Quadric-Mesh-Simplification stalls on flat faces and tears them when
    forced: the first shield rim came out as shards, silhouette IoU against the sculpt 0.76; this gives 0.99.)"""
    import pymeshlab
    if len(f) <= target * 1.05:
        return v, f
    ms = pymeshlab.MeshSet()
    ms.add_mesh(pymeshlab.Mesh(v.astype(np.float64), f.astype(np.int32)))
    ms.meshing_decimation_quadric_edge_collapse(targetfacenum=int(max(4, target)), qualitythr=0.5, preserveboundary=True,
                                                preservenormal=True, preservetopology=True, optimalplacement=True,
                                                planarquadric=True, planarweight=0.001, autoclean=True)
    mm = ms.current_mesh()
    return clean(mm.vertex_matrix().astype(F), mm.face_matrix().astype(np.int64))


def shading(o, vol, v, f, hard):
    """Vertex normals from the sculpt; on hard materials each corner takes the sculpt normal a third of the way
    into its face, so big flat faces shade flat and bevels stay smooth."""
    n = o.grad(vol, v, o.h * 1.5)
    if not hard:
        return n, n[f]
    tri = v[f]
    q = (tri * 0.62 + tri.mean(1, keepdims=True) * 0.38).reshape(-1, 3).astype(F)
    q = q - o.grad(vol, q) * o.s(vol, q)[:, None]
    return n, o.grad(vol, q, o.h * 1.5).reshape(-1, 3, 3)


def closest_on_tris(P, A, B, C):
    """Closest point of P (N,k,3) on triangles A,B,C (N,k,3): returns squared distance (N,k) and barycentrics (N,k,3)."""
    e0, e1 = B - A, C - A
    P = np.broadcast_to(P, A.shape)
    nrm = np.cross(e0, e1)
    nn = np.maximum((nrm * nrm).sum(-1), 1e-20)
    w = P - A
    g = (np.cross(e0, w) * nrm).sum(-1) / nn
    b_ = (np.cross(w, e1) * nrm).sum(-1) / nn
    bary = np.stack([1 - b_ - g, b_, g], -1)
    inside = (bary >= 0).all(-1)
    P = np.broadcast_to(P, A.shape)
    best_d = np.full(A.shape[:-1], 1e18)
    best_b = np.zeros(A.shape[:-1] + (3,))
    for (X, Y, i, j) in ((A, B, 0, 1), (B, C, 1, 2), (C, A, 2, 0)):
        d = Y - X
        t = np.clip(((P - X) * d).sum(-1) / np.maximum((d * d).sum(-1), 1e-20), 0, 1)
        q = X + d * t[..., None]
        dd = ((P - q) ** 2).sum(-1)
        bb = np.zeros_like(best_b)
        bb[..., i] = 1 - t
        bb[..., j] = t
        m = dd < best_d
        best_d = np.where(m, dd, best_d)
        best_b = np.where(m[..., None], bb, best_b)
    q = A * bary[..., :1] + B * bary[..., 1:2] + C * bary[..., 2:]
    din = ((P - q) ** 2).sum(-1)
    return np.where(inside, din, best_d), np.where(inside[..., None], bary, best_b)


LOD0_BUDGET = {"body": 3300, "shield": 850, "cleaver": 350}       # 4,500 in-match triangles in all


def run(work):
    """Sculpt -> Hero mesh (close cameras) -> LOD0 (in-match, <= 4,500 tris). The atlas is unwrapped on LOD0 and the
    Hero takes its UVs by closest-point projection onto LOD0, so every level shares one painted texture."""
    from skimage import measure
    from scipy.spatial import cKDTree
    import xatlas
    stats = {"objects": {}}
    atlas = xatlas.Atlas()
    order = []
    hero, lod = {}, {}
    objs = {}
    for name in ("body", "shield", "cleaver"):
        t0 = time.time()
        o = objs[name] = Obj(work, name)
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
            if (fn * o.grad(vol, v[f].mean(1))).sum() < 0:
                f = f[:, ::-1]
            hv.append(v)
            hf.append(f + nh)
            nh += len(v)
            vis = area(v, f)[~(hidden(o, vol, v)[f]).all(1)].sum()
            parts.append((gid, v, f, float(vis), float(area(v, f).sum())))
        hv, hf = np.concatenate(hv), np.concatenate(hf)
        np.savez(os.path.join(work, f"high_{name}.npz"), v=hv, f=hf.astype(np.int32))
        wsum = sum(a * WEIGHT.get(W.GROUPS[g], 1.0) for g, _, _, a, _ in parts)
        H = dict(v=[], f=[], gid=[], n=[], cn=[], comp=[], nv=0, nc=0)
        per_group = []
        for gid, v, f, a, atot in parts:
            if a <= 0:
                continue
            vol = o.fld[gid]
            want = max(60, int(BUDGET[name] * a * WEIGHT.get(W.GROUPS[gid], 1.0) / wsum))
            # decimate the CLOSED shell (open borders collapse badly), then drop the buried faces
            v, f = decimate(v, f, int(want * min(1.6, atot / a)))
            for _ in range(2):                     # pull the decimated vertices back onto the true surface
                v = v - o.grad(vol, v) * o.s(vol, v)[:, None]
            f = f[~(hidden(o, vol, v)[f]).all(1)]
            v, f = clean(v, f)
            comp = components(len(v), f)
            ca = np.bincount(comp[f[:, 0]], area(v, f), minlength=comp.max() + 1)
            f = f[(ca > 3e-4)[comp[f[:, 0]]]]
            v, f = clean(v, f)
            comp = components(len(v), f)
            n, cn = shading(o, vol, v, f, W.GROUPS[gid] in HARD)
            per_group.append((gid, v.astype(F), f))
            H["v"].append(v.astype(F)); H["f"].append(f + H["nv"]); H["gid"].append(np.full(len(v), gid, np.int32))
            H["n"].append(n); H["cn"].append(cn); H["comp"].append(comp + H["nc"])
            H["nv"] += len(v); H["nc"] += comp.max() + 1
        hero[name] = {k: np.concatenate(H[k]) for k in ("v", "f", "gid", "n", "cn", "comp")}
        nhero = len(hero[name]["f"])
        # ---- LOD0: the Hero pieces decimated again to the in-match budget
        k = LOD0_BUDGET[name] / nhero
        for attempt in range(4):
            Lp = []
            for gid, v, f in per_group:
                vol = o.fld[gid]
                v2, f2 = decimate(v, f, max(8, int(len(f) * k)))
                for _ in range(2):
                    v2 = v2 - o.grad(vol, v2) * o.s(vol, v2)[:, None]
                Lp.append((gid, v2.astype(F), f2))
            tot = sum(len(f2) for _, _, f2 in Lp)
            if tot <= LOD0_BUDGET[name]:
                break
            k *= LOD0_BUDGET[name] / tot * 0.96
        Ld = dict(v=[], f=[], gid=[], n=[], cn=[], comp=[], nv=0, nc=0)
        for gid, v, f in Lp:
            n, cn = shading(o, o.fld[gid], v, f, W.GROUPS[gid] in HARD)
            comp = components(len(v), f)
            Ld["v"].append(v); Ld["f"].append(f + Ld["nv"]); Ld["gid"].append(np.full(len(v), gid, np.int32))
            Ld["n"].append(n); Ld["cn"].append(cn); Ld["comp"].append(comp + Ld["nc"])
            Ld["nv"] += len(v); Ld["nc"] += comp.max() + 1
        lod[name] = {k_: np.concatenate(Ld[k_]) for k_ in ("v", "f", "gid", "n", "cn", "comp")}
        V, Fc, Gd, Nn = lod[name]["v"], lod[name]["f"], lod[name]["gid"], lod[name]["n"]
        fg = Gd[Fc[:, 0]]
        if name == "body":
            headf = (fg == W.GID["skin"]) & (V[Fc].mean(1)[:, 2] > 1.25)
            sets = [("skin_head", headf), ("body", ~headf)]
        else:
            sets = [(name, np.ones(len(Fc), bool))]
        for key, m in sets:
            atlas.add_mesh((V * TEXEL.get(key, 1.0)).astype(F), Fc[m].astype(np.uint32), Nn)
            order.append((name, np.where(m)[0]))
        stats["objects"][name] = {"sculpt_tris": int(len(hf)), "hero_tris": int(nhero), "hero_verts": int(len(hero[name]["v"])),
                                  "lod0_tris": int(len(Fc)), "pieces": int(H["nc"])}
        print(f"MESH {name} sculpt {len(hf)} hero {nhero} lod0 {len(Fc)} tris, {time.time() - t0:.1f}s", flush=True)
    co = xatlas.ChartOptions()
    co.max_cost = 9.0
    co.normal_deviation_weight = 1.0
    co.max_iterations = 2
    po = xatlas.PackOptions()
    po.resolution = 2048
    po.padding = 8
    po.bilinear = True
    po.create_image = False
    atlas.generate(co, po)
    print(f"ATLAS {atlas.width}x{atlas.height} charts {atlas.chart_count}", flush=True)
    out = {}
    for i, (name, fidx) in enumerate(order):
        vmap, idx, uv = atlas[i]
        assert len(idx) == len(fidx)
        d = out.setdefault(name, dict(v=[], f=[], uv=[], gid=[], n=[], comp=[], cn=[], nv=0))
        L = lod[name]
        for k_ in ("v", "n", "gid", "comp"):
            d[k_].append(L[k_][vmap])
        d["uv"].append(uv.astype(F))
        d["cn"].append(L["cn"][fidx])
        d["f"].append(idx.astype(np.int64) + d["nv"])
        d["nv"] += len(vmap)
    rng = np.random.default_rng(0)
    for name, d in out.items():
        o = objs[name]
        L = {k_: np.concatenate(d[k_]) for k_ in ("v", "f", "uv", "gid", "n", "comp", "cn")}
        np.savez(os.path.join(work, f"lod0_{name}.npz"), **L)
        stats["objects"][name]["lod0_verts_with_uv_seams"] = int(len(L["v"]))
        # ---- Hero UVs: closest point on LOD0 (same material), kept inside one chart per Hero face
        chart = components(len(L["v"]), L["f"])[L["f"][:, 0]]
        Hm = hero[name]
        uvl = np.zeros((len(Hm["f"]), 3, 2), F)
        crossing = 0
        hg = Hm["gid"][Hm["f"][:, 0]]
        lg = L["gid"][L["f"][:, 0]]
        for gid in np.unique(hg):
            lt = np.where(lg == gid)[0]
            A, B, C = (L["v"][L["f"][lt, c_]].astype(np.float64) for c_ in range(3))
            b = rng.random((len(lt), 24, 2))
            fl = b.sum(2) > 1
            b[fl] = 1 - b[fl]
            smp = (A[:, None] + (B - A)[:, None] * b[..., :1] + (C - A)[:, None] * b[..., 1:]).reshape(-1, 3)
            smp = np.concatenate([smp, (A + B + C) / 3, A, B, C])
            sid = np.concatenate([np.repeat(np.arange(len(lt)), 24)] + [np.arange(len(lt))] * 4)
            tree = cKDTree(smp)
            hfi = np.where(hg == gid)[0]
            tri = Hm["v"][Hm["f"][hfi]].astype(np.float64)                 # (n,3,3)
            c0 = chart[lt[sid[tree.query(tri.mean(1))[1]]]]               # chart under each Hero face centre
            kq = min(24, len(smp))
            _, nb = tree.query(tri.reshape(-1, 3), k=kq)
            cand = sid[nb.reshape(len(hfi) * 3, kq)]                        # candidate LOD0 triangles per corner
            Pq = tri.reshape(-1, 1, 3)
            d2, bary = closest_on_tris(Pq, A[cand], B[cand], C[cand])
            same = chart[lt[cand]] == np.repeat(c0, 3)[:, None]
            pick = np.argmin(d2 + np.where(same, 0.0, 1e3), 1)
            rows = np.arange(len(pick))
            crossing += int((~same[rows, pick]).reshape(-1, 3).any(1).sum())
            tsel = lt[cand[rows, pick]]
            bsel = bary[rows, pick]
            uvc = (L["uv"][L["f"][tsel]] * bsel[..., None]).sum(1)
            uvl[hfi] = uvc.reshape(-1, 3, 2)
        np.savez(os.path.join(work, f"hero_{name}.npz"), uvl=uvl, **Hm)
        stats["objects"][name]["hero_faces_crossing_a_chart"] = crossing
        # ---- GATE: both meshes must keep the sculpt's silhouette (three axis views)
        for tag, v, f in (("hero", Hm["v"], Hm["f"]), ("lod0", L["v"], L["f"])):
            b = rng.random((len(f), 300, 2))
            fl = b.sum(2) > 1
            b[fl] = 1 - b[fl]
            p = (v[f[:, 0]][:, None] + (v[f[:, 1]] - v[f[:, 0]])[:, None] * b[..., :1] + (v[f[:, 2]] - v[f[:, 0]])[:, None] * b[..., 1:]).reshape(-1, 3)
            ious = []
            for ax in range(3):
                keep = [i for i in range(3) if i != ax]
                ms = (o.d1 < 0).any(ax)
                ij = np.round((p[:, keep] - o.lo[keep]) / o.h).astype(int)
                m = np.zeros_like(ms)
                m[np.clip(ij[:, 0], 0, ms.shape[0] - 1), np.clip(ij[:, 1], 0, ms.shape[1] - 1)] = True
                ious.append(round(float((m & ms).sum() / (m | ms).sum()), 3))
            stats["objects"][name][f"{tag}_silhouette_iou_vs_sculpt_xyz"] = ious
            if min(ious) < (0.95 if tag == "hero" else 0.90):
                print(f"GATE FAIL {name} {tag} silhouette vs sculpt {ious}", flush=True)
    stats["atlas_charts"] = int(atlas.chart_count)
    for k_ in ("sculpt_tris", "hero_tris", "lod0_tris", "hero_faces_crossing_a_chart"):
        stats[k_ + "_total"] = sum(s_[k_] for s_ in stats["objects"].values())
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
        z = np.load(os.path.join(work, f"lod0_{name}.npz"))       # the atlas lives on LOD0; the Hero mesh borrows it
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
            for _ in range(4):
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
