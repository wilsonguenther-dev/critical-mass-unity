"""Deformation metrics per character / clip / frame (offline). Used for the baseline and after every weight change."""
import json
import sys

import numpy as np

import riglib as rl

FIGHTER_GROUPS = [
    ("shoulder", ["lowerarm.{s}", "wrist.{s}", "hand.{s}"], ["chest", "spine", "hips"]),
    ("elbow", ["wrist.{s}", "hand.{s}"], ["upperarm.{s}"]),
    ("hip", ["upperleg.{s}", "lowerleg.{s}", "foot.{s}"], ["upperleg.{o}", "lowerleg.{o}"]),
    ("knee", ["foot.{s}", "toes.{s}"], ["upperleg.{s}"]),
    ("neck", ["head"], ["chest"]),
]


def child_of(d, b):
    kids = [i for i, p in enumerate(d["parents"]) if p == b]
    return kids


def capsules(d, mesh, S_bind):
    """Per bone: bind-pose segment and inner-core radius (35th percentile of its dominant vertices' distances)."""
    H = rl.joint_heads(d, S_bind)
    T = np.einsum("bij,bj->bi", S_bind[:, :3, :3], d["tails"][:, :3] if d["tails"].shape[1] == 3 else d["tails"][:, :3, 3]) + S_bind[:, :3, 3]
    caps = {}
    for b, name in enumerate(d["bones"]):
        sel = mesh.dom == b
        if sel.sum() < 8:
            continue
        kids = [k for k in child_of(d, b) if (mesh.dom == k).sum() >= 8]
        end = H[kids[0]] if len(kids) == 1 else T[b]
        dist, _ = rl.seg_dist(mesh.A[sel], H[b], end)
        caps[name] = (b, H[b], end, float(np.percentile(dist, 35)))
    return caps


def groups_for(d):
    if d["cls"] == "Kaiju":
        return d.get("kaiju_groups", [])
    out = []
    for g, probe, target in FIGHTER_GROUPS:
        for s, o in (("l", "r"), ("r", "l")):
            out.append((g, [p.format(s=s, o=o) for p in probe], [t.format(s=s, o=o) for t in target]))
    return out


def measure(d, tag, idx, wt, S_bind, A, rigid_parts, clips=None, every=1, follow=None):
    T, E = d[f"{tag}_T"], d[f"{tag}_E"]
    mesh = rl.Mesh(A, T, E, idx, wt, S_bind)
    caps = capsules(d, mesh, S_bind)
    bi = {b: i for i, b in enumerate(d["bones"])}
    grp = []
    for g, probe, target in groups_for(d):
        pm = np.isin(mesh.dom, [bi[p] for p in probe if p in bi])
        tg = [caps[t] for t in target if t in caps]
        if pm.sum() and tg:
            ex = None
            top = [bi[p] for p in probe if p in bi]
            par = d["parents"][top[0]]
            if par >= 0 and d["bones"][par] in target:
                ex = (top[0], max(c[3] for c in tg))
            grp.append((g, np.nonzero(pm)[0], tg, ex))
    Sb_inv = np.linalg.inv(S_bind)
    res = {}
    for clip in (clips or d["clip_names"]):
        S_all = d[f"S_{clip}"].astype(np.float64)
        rows = []
        for fi in range(0, len(S_all), every):
            S = S_all[fi]
            P, r, ninv, ncol = mesh.frame_metrics(S, S_bind)
            l1 = np.linalg.norm(P[E[mesh.ok][:, 0]] - P[E[mesh.ok][:, 1]], axis=1)
            vis = int(((r > 1.5) & (np.abs(l1 - mesh.l0[mesh.ok]) > 0.015)).sum())
            rel = np.einsum("bij,bjk->bik", S, Sb_inv)
            pen = {}
            for g, pv, tg, ex in grp:
                X = P[pv]
                keep = np.ones(len(X), bool)
                if ex is not None:
                    hb = rl.joint_heads(d, S)[ex[0]]
                    keep = np.linalg.norm(X - hb, axis=1) > ex[1]
                depth = 0.0
                for (b, h0, e0, rad) in tg:
                    h = rel[b, :3, :3] @ h0 + rel[b, :3, 3]
                    e = rel[b, :3, :3] @ e0 + rel[b, :3, 3]
                    dist, _ = rl.seg_dist(X[keep], h, e) if keep.any() else (np.array([9.0]), None)
                    depth = max(depth, float(np.max(rad - dist, initial=0.0)))
                pen[g] = max(pen.get(g, 0.0), depth)
            rig = {}
            for name, vids in rigid_parts.items():
                if isinstance(vids, list):
                    rig[name] = max((rl.procrustes_resid(A[v], P[v]) for v in vids if len(v) >= 4), default=0.0)
                elif len(vids) >= 4:
                    rig[name] = rl.procrustes_resid(A[vids], P[vids])
            fol = {}
            for name, (vids, b) in (follow or {}).items():
                Q = rel[b, :3, :3] @ A[vids].T
                err = np.linalg.norm(P[vids] - (Q.T + rel[b, :3, 3]), axis=1)
                fol[name] = float(np.percentile(err, 95))
            rows.append(dict(fol=fol, max=float(r.max()), p99=float(np.percentile(r, 99)), over15=int((r > 1.5).sum()),
                             over2=int((r > 2.0).sum()), vis=vis, inv=ninv, col=ncol, pen=pen, rig=rig))
        worst = int(np.argmax([x["over15"] + 5 * x["over2"] + 20 * x["inv"] for x in rows])) * every
        res[clip] = dict(
            frames=len(S_all), worst_frame=worst,
            max=round(max(x["max"] for x in rows), 3), p99=round(max(x["p99"] for x in rows), 3),
            over15=max(x["over15"] for x in rows), over2=max(x["over2"] for x in rows), vis=max(x["vis"] for x in rows),
            inv=max(x["inv"] for x in rows), col=max(x["col"] for x in rows),
            pen_mm={g: round(1000 * max(x["pen"].get(g, 0) for x in rows), 1) for g in {g for x in rows for g in x["pen"]}},
            rigid_mm={k: round(1000 * max(x["rig"].get(k, 0) for x in rows), 2) for k in rigid_parts},
            follow_p95_mm={k: round(1000 * max(x["fol"].get(k, 0) for x in rows), 1) for k in (follow or {})})
    return res


def summarise(res):
    keys = list(res)
    worst_clip = max(keys, key=lambda c: res[c]["max"])
    return dict(clips=len(keys), max=max(res[c]["max"] for c in keys), worst_clip=worst_clip,
                p99=max(res[c]["p99"] for c in keys), inv=max(res[c]["inv"] for c in keys),
                inv_total=sum(res[c]["inv"] for c in keys), over15_sum=sum(res[c]["over15"] for c in keys),
                vis_sum=sum(res[c]["vis"] for c in keys), col=max(res[c]["col"] for c in keys),
                follow_mm=max([max(res[c]["follow_p95_mm"].values(), default=0) for c in keys]),
                pen_mm=max([max(res[c]["pen_mm"].values(), default=0) for c in keys]),
                rigid_mm=max([max(res[c]["rigid_mm"].values(), default=0) for c in keys]))


def follow_parts(d, A, S_bind, exclude=None):
    """Extremities that must ride their bone exactly: hands past the wrist, feet below the ankle, the head above the neck
    (fighters); fists, feet, snout and tail tip (Kaiju). Defined from the bind-pose joints, independent of any weights."""
    B = d["bones"]
    bi = {b: i for i, b in enumerate(B)}
    HA = rl.joint_heads(d, S_bind)
    out = {}
    if d["cls"] != "Kaiju":
        for s in ("l", "r"):
            h, w = HA[bi[f"hand.{s}"]], HA[bi[f"wrist.{s}"]]
            u = (h - w) / np.linalg.norm(h - w)
            sel = np.nonzero(((A - h) @ u > 0.03) & (np.linalg.norm(A - h, axis=1) < 0.35) & ((A[:, 0] * (1 if s == "l" else -1)) > 0))[0]
            if len(sel) > 10:
                out[f"hand.{s}"] = (sel, bi[f"hand.{s}"])
            f = HA[bi[f"foot.{s}"]]
            sel = np.nonzero((A[:, 2] < f[2] - 0.04) & (np.linalg.norm(A[:, :2] - f[:2], axis=1) < 0.25))[0]
            if len(sel) > 10:
                out[f"foot.{s}"] = (sel, bi[f"foot.{s}"])
        hd = HA[bi["head"]]
        sel = np.nonzero(A[:, 2] > hd[2] + 0.25)[0]
        out["head"] = (sel, bi["head"])
    else:
        for s, sg in (("L", 1), ("R", -1)):
            f = HA[bi[f"Fist_{s}"]]
            sel = np.nonzero((A[:, 2] < f[2] - 0.35) & (sg * A[:, 0] > 1.2) & (A[:, 1] < -0.6))[0]
            out[f"Fist_{s}"] = (sel, bi[f"Fist_{s}"])
            k = HA[bi[f"Foot_{s}"]]
            sel = np.nonzero((A[:, 2] < k[2] - 0.3) & (np.linalg.norm(A[:, :2] - k[:2], axis=1) < 0.4))[0]
            out[f"Foot_{s}"] = (sel, bi[f"Foot_{s}"])
        sel = np.nonzero((A[:, 1] < -2.45) & (A[:, 2] > 2.95))[0]
        out["snout"] = (sel, bi["Head"])
        t3 = HA[bi["Tail3"]]
        sel = np.nonzero((A[:, 0] > t3[0] + 0.5) & (A[:, 1] > 1.8) & (A[:, 2] < 1.0))[0]
        out["tail_tip"] = (sel, bi["Tail3"])
    if exclude is None and d["cls"] != "Kaiju":
        Wt = next((d[f"{t}_W"] for t in ("Hero", "LOD0", "LOD1") if len(d[f"{t}_W"]) == len(A)), None)
        dom = np.argmax(Wt, 1) if Wt is not None else None
        if dom is not None:
            exclude = np.isin(dom, [bi[b] for b in B if b.startswith("handslot")])
    if exclude is not None:
        out = {k: (v[0][~exclude[v[0]]], v[1]) for k, v in out.items()}
    return {k: v for k, v in out.items() if len(v[0]) >= 10}
