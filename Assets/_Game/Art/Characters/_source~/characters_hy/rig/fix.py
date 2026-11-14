"""Weight solver for the HY fighters and the Kaiju: part labels by capsule distance in the bind (generated) pose,
connectivity cleanup, rigid parts locked to one bone, harmonic blending only inside joint zones, 4 influences."""
import json
import sys

import numpy as np
from scipy.spatial import cKDTree

import riglib as rl
import measure as ms

CHAIN = {  # child used as the end of each bone's segment (fighters)
    "hips": "spine", "spine": "chest", "chest": "head",
    "upperarm.l": "lowerarm.l", "lowerarm.l": "wrist.l", "wrist.l": "hand.l",
    "upperarm.r": "lowerarm.r", "lowerarm.r": "wrist.r", "wrist.r": "hand.r",
    "upperleg.l": "lowerleg.l", "lowerleg.l": "foot.l", "foot.l": "toes.l",
    "upperleg.r": "lowerleg.r", "lowerleg.r": "foot.r", "foot.r": "toes.r",
}
ZONE_K = {  # joint zone radius = k x the child part's radius (fighters)
    "spine": 1.0, "chest": 1.0, "head": 1.6, "upperarm": 1.9, "lowerarm": 1.5, "wrist": 1.1, "hand": 1.2,
    "upperleg": 1.5, "lowerleg": 1.5, "foot": 1.2, "toes": 0.9,
}


def side_of(name):
    return 1 if name.endswith(".l") else (-1 if name.endswith(".r") else 0)


def bind_shape(d, tag):
    """Fighters: the generated (A-pose) shape recovered from the shipped rest mesh and its weights."""
    idx, wt = rl.top4(d[f"{tag}_W"])
    if d["cls"] == "Kaiju":
        return d[f"{tag}_V"], idx, wt
    return rl.apply(rl.blend(idx, wt, d["S_P"]), d[f"{tag}_V"]), idx, wt


def head_mask(d, A, HA):
    bi = d["bones"].index("head")
    neck = HA[bi]
    Hh = A[:, 2].max()
    top = A[A[:, 2] > neck[2] + 0.75 * (Hh - neck[2])]
    c = np.median(top[:, :2], 0)
    R = 0.85 * (Hh - neck[2])
    m = (A[:, 2] > neck[2] + 0.02) & (np.hypot(A[:, 0] - c[0], A[:, 1] - c[1]) < R)
    return m


def segments(d, S_bind, deform, tails_extra=None):
    bones = d["bones"]
    HA = rl.joint_heads(d, S_bind)
    TA = np.einsum("bij,bj->bi", S_bind[:, :3, :3], d["tails"]) + S_bind[:, :3, 3]
    seg = {}
    for b in deform:
        i = bones.index(b)
        end = None
        ch = d.get("chain", CHAIN).get(b)
        if ch in bones and (ch in deform or b == "chest" or ch.startswith("head")):
            end = HA[bones.index(ch)]
        if end is None:
            end = TA[i] if tails_extra is None or b not in tails_extra else tails_extra[b]
        seg[b] = (HA[i], end)
    return seg, HA


def label_parts(A, seg, deform, init_lab, allow, n_iter=4):
    names = list(deform)
    D = np.stack([rl.seg_dist(A, *seg[b])[0] for b in names], 1)
    D0 = D.copy()
    D0[~allow] = 9.0
    lab0 = np.argmin(D0, 1)
    r = np.array([np.median(D[lab0 == i, i]) if (lab0 == i).sum() > 10 else 0.05 for i in range(len(names))])
    for _ in range(n_iter):
        score = D - 0.6 * r[None]
        score[~allow] = 9.0
        lab = np.argmin(score, 1)
        r = np.array([np.median(D[lab == i, i]) if (lab == i).sum() > 10 else r[i] for i in range(len(names))])
    return lab, r, D


def cleanup(n, E, lab, locked, passes=4):
    """Islands of a label that are not its largest component take the most common neighbouring label."""
    lab = lab.copy()
    for _ in range(passes):
        changed = 0
        for b in np.unique(lab):
            comp = rl.components(n, E, lab == b)
            ids, cnt = np.unique(comp[comp >= 0], return_counts=True)
            if len(ids) <= 1:
                continue
            main = ids[np.argmax(cnt)]
            for cid in ids[ids != main]:
                vs = np.nonzero(comp == cid)[0]
                if locked[vs].any():
                    continue
                m = np.isin(E[:, 0], vs) ^ np.isin(E[:, 1], vs)
                nb = np.where(np.isin(E[m][:, 0], vs), E[m][:, 1], E[m][:, 0])
                nl = lab[nb]
                nl = nl[nl != b]
                if len(nl):
                    lab[vs] = np.bincount(nl).argmax()
                    changed += 1
        if not changed:
            break
    return lab


def solve(d, tag="Hero", zone_k=None, extra_fixed=None, smooth_it=0):
    """Returns (W_new (n,B), labels, info)."""
    bones = d["bones"]
    B = len(bones)
    is_k = d["cls"] == "Kaiju"
    S_bind = np.repeat(np.eye(4)[None], B, 0) if is_k else d["S_P"]
    A, idx0, wt0 = bind_shape(d, tag)
    n = len(A)
    E = d[f"{tag}_E"]
    W0 = d[f"{tag}_W"]
    dom0 = np.argmax(W0, 1)
    deform = d.get("deform") or [b for b in bones if W0[:, bones.index(b)].sum() > 0 and not b.startswith("handslot")]
    prop_bones = [bones.index(b) for b in bones if b.startswith("handslot")]
    props = np.isin(dom0, prop_bones)
    seg, HA = segments(d, S_bind, deform, d.get("tails_extra"))
    allow = np.ones((n, len(deform)), bool)
    for j, b in enumerate(deform):
        s = d.get("side_fn", side_of)(b)
        if s:
            allow[:, j] = s * A[:, 0] > -0.01
    init = np.array([deform.index(bones[i]) if bones[i] in deform else 0 for i in dom0])
    lab, rad, D = label_parts(A, seg, deform, init, allow, n_iter=d.get("label_iter", 4))
    if d.get("label_mode", "geo") == "old":
        lab = init.copy()
        rad = np.array([np.median(D[lab == i, i]) if (lab == i).sum() > 10 else 0.05 for i in range(len(deform))])
    locked = props.copy()
    rigid = {}
    if not is_k:
        hm = head_mask(d, A, HA) & ~props
        lab[hm] = deform.index("head")
        locked |= hm
        rigid["head_top"] = hm
    for name, (mask, bone) in (extra_fixed or {}).items():
        mask = mask & ~props
        lab[mask] = deform.index(bone)
        locked |= mask
        rigid[name] = mask
    body = ~props
    lab = cleanup(n, E[body[E[:, 0]] & body[E[:, 1]]], lab, locked)
    zk = dict(ZONE_K, **(zone_k or {}))
    zone = np.zeros(n, bool)
    zinfo = {}
    par = d["parents"]
    for j, b in enumerate(deform):
        bi = bones.index(b)
        p = par[bi]
        while p >= 0 and bones[p] not in deform:
            p = par[p]
        if p < 0:
            continue
        pj = deform.index(bones[p])
        key = b.split(".")[0]
        k = zk.get(key, zk.get(b, 1.2))
        R = k * rad[j]
        m = np.isin(lab, [j, pj]) & (np.linalg.norm(A - HA[bi], axis=1) < R) & ~locked
        zone |= m
        zinfo[b] = dict(R=round(float(R), 3), verts=int(m.sum()))
    Wf = np.zeros((n, B))
    Wf[np.arange(n), [bones.index(deform[l]) for l in lab]] = 1.0
    Wf[props] = 0.0
    Wf[props, dom0[props]] = 1.0
    Wb = rl.harmonic(n, E, A, ~zone, Wf)
    Wb = rl.limit4(Wb)
    info = dict(radii={b: round(float(rad[j]), 3) for j, b in enumerate(deform)}, zones=zinfo,
                labels={b: int((lab == j).sum()) for j, b in enumerate(deform)}, zone_verts=int(zone.sum()))
    return Wb, lab, rigid, info, A


def transfer(A_src, W_src, A_dst):
    t = cKDTree(A_src)
    dist, ii = t.query(A_dst, k=4)
    w = 1.0 / np.maximum(dist, 1e-5) ** 2
    lab_src = np.argmax(W_src, 1)
    same = lab_src[ii] == lab_src[ii[:, :1]]
    w = w * same
    W = np.einsum("nk,nkb->nb", w, W_src[ii]) / w.sum(1, keepdims=True)
    return rl.limit4(W)
