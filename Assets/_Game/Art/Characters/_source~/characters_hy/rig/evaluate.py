"""Bone-heat weights -> final weights (props, head top and crystals rigid, 4 influences), before/after metrics, fix npz.

python3 evaluate.py <cls> <variant: cur|refit> [every] [--save]
"""
import json
import sys
import time

import numpy as np
from scipy.spatial import cKDTree

import riglib as rl
import measure as ms
import fix
import refit
import prep_heat as ph

HI = "/Users/wilsonguenther/cm-rig-scratch/heat_in"
HO = "/Users/wilsonguenther/cm-rig-scratch/heat_out"
OUT = "/Users/wilsonguenther/cm-rig-scratch/out"
HEAD_LOCK = 0.9
INIT_MIX = 0.0
CRYSTAL_MAX = 0.45
KAIJU_ARM_XMIN = 0.8
SMOOTH_IT = 0
KAIJU_GROUPS = [
    ("shoulder", ["Forearm_L", "Fist_L"], ["Chest", "Spine", "Hips"]), ("shoulder", ["Forearm_R", "Fist_R"], ["Chest", "Spine", "Hips"]),
    ("elbow", ["Fist_L"], ["Arm_L"]), ("elbow", ["Fist_R"], ["Arm_R"]),
    ("hip", ["Foot_L"], ["Foot_R", "Thigh_R"]), ("hip", ["Foot_R"], ["Foot_L", "Thigh_L"]),
    ("knee", ["Foot_L"], ["Thigh_L"]), ("knee", ["Foot_R"], ["Thigh_R"]),
    ("neck", ["Head", "Jaw"], ["Chest"]), ("tail", ["Tail2", "Tail3"], ["Thigh_L", "Thigh_R", "Foot_L", "Foot_R"]),
]


def kaiju_crystals(d, tag="Hero"):
    V, T, TP, COL, E = d[f"{tag}_V"], d[f"{tag}_T"], d[f"{tag}_TP"], d[f"{tag}_COL"], d[f"{tag}_E"]
    g = np.round(COL[TP][:, 3] * 8) == 6
    vm = np.zeros(len(V), bool)
    vm[T[g].ravel()] = True
    vn = np.zeros(len(V), bool)
    vn[T[~g].ravel()] = True
    core = vm & ~vn
    dist = cKDTree(V[vn]).query(V[core])[0]
    deep = np.zeros(len(V), bool)
    deep[np.nonzero(core)[0][dist > 0.04]] = True
    grow = deep.copy()
    for _ in range(2):                       # two rings around the deep part: the crystal's flanks and base
        m = grow[E[:, 0]] | grow[E[:, 1]]
        grow[E[m].ravel()] = True
    grow &= vm | deep
    lab = rl.components(len(V), E, grow)
    ids, cnt = np.unique(lab[lab >= 0], return_counts=True)
    out = []
    for i, c in zip(ids, cnt):
        v = np.nonzero(lab == i)[0]
        if c >= 6 and np.linalg.norm(V[v].max(0) - V[v].min(0)) <= CRYSTAL_MAX:
            out.append(v)
    return out


def post(d, Wheat, keep, names, A, extra_rigid=None):
    bones = d["bones"]
    B = len(bones)
    n = len(A)
    W = np.zeros((n, B))
    cols = [bones.index(x) for x in names]
    W[np.ix_(keep, cols)] = Wheat
    dom0 = np.argmax(d["Hero_W"], 1)
    props = np.isin(dom0, [i for i, b in enumerate(bones) if b.startswith("handslot")])
    W[props] = 0
    W[props, dom0[props]] = 1
    bad = W.sum(1) < 1e-6
    if bad.any():
        good = np.nonzero(~bad & ~props)[0]
        nn = cKDTree(A[good]).query(A[bad])[1]
        W[bad] = W[good[nn]]
    W = W / W.sum(1, keepdims=True)
    rigid = {}
    if d["cls"] != "Kaiju":
        HA = rl.joint_heads(d, d["S_P"])
        hb = bones.index("head")
        hm = fix.head_mask(d, A, HA) & ~props & (W[:, hb] > HEAD_LOCK)
        W[hm] = 0
        W[hm, bones.index("head")] = 1
        rigid["head_top"] = np.nonzero(hm)[0]
    else:
        # the shoulders sit right beside the jaw and neck: an arm bone may not reach the centre column, or a swipe
        # drags the chin with it
        cx = -0.09
        near = np.abs(A[:, 0] - cx) < KAIJU_ARM_XMIN
        for b in ("Arm_L", "Arm_R"):
            W[near, bones.index(b)] = 0
        s_ = W.sum(1)
        W[s_ < 1e-6, bones.index("Chest")] = 1
        W /= W.sum(1, keepdims=True)
        cr = kaiju_crystals(d)
        Wl = rl.limit4(W)
        for k, vids in enumerate(cr):
            b = int(np.argmax(Wl[vids].sum(0)))
            W[vids] = 0
            W[vids, b] = 1
        rigid["crystals"] = cr
    for name, vids in (extra_rigid or {}).items():
        b = int(np.argmax(W[vids].sum(0)))
        W[vids] = 0
        W[vids, b] = 1
        rigid[name] = vids
    for p in [i for i, b in enumerate(bones) if b.startswith("handslot")]:
        if (dom0 == p).sum() >= 4:
            rigid["prop_" + bones[p]] = np.nonzero(dom0 == p)[0]
    locked = props.copy()
    for v in rigid.values():
        for vids in (v if isinstance(v, list) else [v]):
            locked[vids] = True
    if SMOOTH_IT:
        E = d["Hero_E"]
        for _ in range(SMOOTH_IT):
            acc = np.zeros_like(W)
            deg = np.zeros(n)
            ok = ~(props[E[:, 0]] ^ props[E[:, 1]])
            e = E[ok]
            np.add.at(acc, e[:, 0], W[e[:, 1]])
            np.add.at(acc, e[:, 1], W[e[:, 0]])
            np.add.at(deg, e[:, 0], 1.0)
            np.add.at(deg, e[:, 1], 1.0)
            Wn = 0.5 * W + 0.5 * acc / np.maximum(deg, 1.0)[:, None]
            W = np.where(locked[:, None], W, Wn)
    return rl.limit4(W), rigid


def lod_rigid(rigid, A_hero, A_lod, props_lod=None):
    nn = cKDTree(A_hero).query(A_lod)[1]
    if props_lod is not None:
        nn = np.where(props_lod, -1, nn)
    out = {}
    inv = {}
    for k, v in rigid.items():
        if isinstance(v, list):
            lst = []
            for vids in v:
                m = np.zeros(len(A_hero) + 1, bool)
                m[vids] = True
                sel = np.nonzero(m[nn])[0]
                if len(sel) >= 4:
                    lst.append(sel)
            out[k] = lst
        else:
            m = np.zeros(len(A_hero) + 1, bool)
            m[v] = True
            out[k] = np.nonzero(m[nn])[0]
    return out


def setup(cls, variant):
    d = rl.load(cls)
    B = len(d["bones"])
    if cls == "Kaiju":
        d["kaiju_groups"] = KAIJU_GROUPS
        S_old = np.repeat(np.eye(4)[None], B, 0)
        if variant == "refit":
            heads, _, _ = ph.kaiju_rig()
            HR = d["rest_w"][:, :3, 3].copy()
            for k, v in heads.items():
                HR[d["bones"].index(k)] = v
            dd = refit.apply(d, HR, S_old)
        else:
            dd = d
        return d, dd, S_old, S_old
    if variant == "refit":
        z = np.load(f"{HI}/refit_{cls}.npz")
        dd = refit.apply(d, z["HR"], z["SP"])
        return d, dd, d["S_P"], z["SP"]
    return d, d, d["S_P"], d["S_P"]


def evaluate(cls, variant, every=2, save=False, tags=("Hero", "LOD0"), extra_rigid_fn=None, weights_fn=None):
    t0 = time.time()
    d, dd, S_old, S_new = setup(cls, variant)
    binds = {t: fix.bind_shape(d, t) for t in ("Hero", "LOD0", "LOD1")}
    A_h = binds["Hero"][0]
    hz = np.load(f"{HI}/heat_in_{cls}_{variant}.npz")
    Wheat = np.load(f"{HO}/heat_out_{cls}_{variant}.npz")["W"]
    names = [str(x) for x in hz["names"]]
    if cls == "Kaiju":
        keep = np.arange(len(A_h))
    else:
        _, _, keep = ph.body_only(d, A_h)
    W, rigid = post(d, Wheat, keep, names, A_h, extra_rigid_fn(d, A_h) if extra_rigid_fn else None)
    nb = len(d["bones"])
    hs = [i for i, b in enumerate(d["bones"]) if b.startswith("handslot")]
    Wt = {"Hero": W}
    for t in ("Hero", "LOD0", "LOD1"):
        if t != "Hero":
            Wt[t] = fix.transfer(A_h, W, binds[t][0])
        _, i0, w0 = binds[t]
        W0 = np.zeros((len(i0), nb)); np.put_along_axis(W0, i0, w0, 1)
        pl = np.isin(np.argmax(W0 if t != "Hero" else W, 1), hs)
        if INIT_MIX > 0 and cls != "Kaiju":
            hb = list(d["bones"]).index("head")
            Wc = Wt[t].copy(); Wc[binds[t][0][:, 2] < dd["rest_w"][hb][2, 3], hb] = 0
            sc = Wc.sum(1); Wc[sc < 1e-6] = W0[sc < 1e-6]; Wc /= Wc.sum(1, keepdims=True)
            Wm = INIT_MIX * W0 + (1 - INIT_MIX) * Wc
            Wm[pl] = Wt[t][pl]
            if t == "Hero":
                for v in rigid.values():
                    for vids in (v if isinstance(v, list) else [v]):
                        Wm[vids] = W[vids]
            Wt[t] = rl.limit4(Wm)
        if t != "Hero":
            Wt[t][pl] = W0[pl]
    if weights_fn:
        for t in ("Hero", "LOD0", "LOD1"):
            if t == "Hero":
                lk = np.zeros(len(A_h), bool)
                for v in rigid.values():
                    for vids in (v if isinstance(v, list) else [v]):
                        lk[vids] = True
            else:
                lk = np.isin(np.argmax(Wt[t], 1), hs)
                for v in lod_rigid({k: v for k, v in rigid.items() if not k.startswith("prop_")}, A_h, binds[t][0], lk).values():
                    for vids in (v if isinstance(v, list) else [v]):
                        lk[vids] = True
            Wt[t] = weights_fn(d, dd, Wt[t], binds[t][0], S_new, t, lk, binds[t])
    W = Wt["Hero"]
    res = {}
    for t in tags:
        A, idx0, wt0 = binds[t]
        pl = np.isin(np.argmax(d[f"{t}_W"], 1), [i for i, b in enumerate(d["bones"]) if b.startswith("handslot")])
        rp = rigid if t == "Hero" else lod_rigid({k: v for k, v in rigid.items() if not k.startswith("prop_")}, A_h, A, pl)
        if t != "Hero":
            dom_t = np.argmax(d[f"{t}_W"], 1)
            for p in [i for i, b in enumerate(d["bones"]) if b.startswith("handslot")]:
                if (dom_t == p).sum() >= 4:
                    rp["prop_" + d["bones"][p]] = np.nonzero(dom_t == p)[0]
        res[f"{t}_before"] = ms.measure(d, t, idx0, wt0, S_old, A, rp, every=every, follow=ms.follow_parts(d, A, S_old))
        idx1, wt1 = rl.top4(Wt[t])
        res[f"{t}_after"] = ms.measure(dd, t, idx1, wt1, S_new, A, rp, every=every, follow=ms.follow_parts(dd, A, S_new))
        for k in ("before", "after"):
            res[f"{t}_{k}_sum"] = ms.summarise(res[f"{t}_{k}"])
        print(cls, variant, t, "\n  before", res[f"{t}_before_sum"], "\n  after ", res[f"{t}_after_sum"], round(time.time() - t0, 1), flush=True)
    if save:
        out = {}
        for t in ("Hero", "LOD0", "LOD1"):
            idx1, wt1 = rl.top4(Wt[t])
            out[f"R_{t}"] = rl.unskin(binds[t][0], idx1, wt1, S_new).astype(np.float32)
            Wd = np.zeros((len(idx1), len(d["bones"])))
            np.put_along_axis(Wd, idx1, wt1, 1)
            out[f"W_{t}"] = Wd.astype(np.float32)
        out["heads_w"] = dd["rest_w"][:, :3, 3]
        np.savez_compressed(f"{OUT}/fix_{cls}.npz", **out)
        json.dump(res, open(f"{OUT}/metrics_{cls}.json", "w"), indent=1, default=lambda o: o.tolist() if hasattr(o, "tolist") else str(o))
    return res, d, dd, W, rigid


if __name__ == "__main__":
    import os
    os.makedirs(OUT, exist_ok=True)
    cls, variant = sys.argv[1], sys.argv[2]
    every = int(sys.argv[3]) if len(sys.argv) > 3 else 2
    evaluate(cls, variant, every, "--save" in sys.argv)
