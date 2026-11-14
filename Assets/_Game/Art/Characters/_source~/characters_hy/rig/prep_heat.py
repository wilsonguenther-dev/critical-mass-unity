"""Prepare bone-heat inputs: each fighter with its current joints and with the arm refit; the Kaiju with the refit rig."""
import json
import os

import numpy as np

import riglib as rl
import fix
import refit

IN = "/Users/wilsonguenther/cm-rig-scratch/heat_in"
os.makedirs(IN, exist_ok=True)

KAIJU_HEADS = {  # rest = bind (generated mesh); measured on the HY mesh (leg columns, arm columns, tail centreline)
    "Hips": (0, -0.45, 1.50), "Spine": (0, -0.65, 2.00), "Chest": (0, -0.90, 2.50), "Neck": (0, -1.30, 2.95),
    "Head": (0, -1.80, 3.12), "Jaw": (0, -2.02, 2.88),
    "Arm_L": (1.15, -1.35, 3.05), "Forearm_L": (1.70, -1.35, 1.95), "Fist_L": (1.72, -1.50, 1.08),
    "Thigh_L": (0.62, -0.62, 1.55), "Foot_L": (0.76, -0.75, 0.85),
    "Tail1": (0.0, 0.50, 0.90), "Tail2": (0.35, 1.25, 0.47), "Tail3": (1.15, 1.99, 0.34),
}
KAIJU_ENDS = {"Head": (0, -2.75, 3.08), "Jaw": (0, -2.80, 2.74), "Fist_L": (1.80, -1.62, 0.15), "Foot_L": (0.79, -0.92, 0.10),
              "Tail3": (2.45, 2.55, 0.36)}
KAIJU_CHAIN = {"Hips": "Spine", "Spine": "Chest", "Chest": "Neck", "Neck": "Head", "Arm_L": "Forearm_L", "Forearm_L": "Fist_L",
               "Thigh_L": "Foot_L", "Tail1": "Tail2", "Tail2": "Tail3"}


# The generated Kaiju is not mirror-symmetric: its right limbs sit further out than the left ones. Measured on the
# HY mesh (outer extent and centre of each limb column, left vs right), metres the right joint moves outward.
KAIJU_R_OUT = {"Arm": 0.22, "Forearm": 0.19, "Fist": 0.16, "Thigh": 0.19, "Foot": 0.25}


def mirror(dct):
    out = dict(dct)
    for k, v in dct.items():
        if k.endswith("_L"):
            out[k[:-2] + "_R"] = (-v[0] - KAIJU_R_OUT.get(k[:-2], 0.0), v[1], v[2])
    return out


def kaiju_rig():
    heads = mirror(KAIJU_HEADS)
    ends = mirror(KAIJU_ENDS)
    chain = dict(KAIJU_CHAIN)
    for k, v in KAIJU_CHAIN.items():
        if k.endswith("_L"):
            chain[k[:-2] + "_R"] = v[:-2] + "_R"
    return heads, ends, chain


def fighter_segments(d, S_bind, deform, A):
    seg, HA = fix.segments(d, S_bind, deform)
    hb = d["bones"].index("head")
    top = np.array([HA[hb][0], HA[hb][1], A[:, 2].max() * 0.93])
    seg["head"] = (HA[hb], top)
    return seg


def write(name, V, T, seg):
    names = list(seg)
    np.savez_compressed(f"{IN}/heat_in_{name}.npz", V=V, T=T, names=np.array(names),
                        heads=np.array([seg[n][0] for n in names]), tails=np.array([seg[n][1] for n in names]))


def body_only(d, A, tag="Hero"):
    dom = np.argmax(d[f"{tag}_W"], 1)
    props = np.isin(dom, [i for i, b in enumerate(d["bones"]) if b.startswith("handslot")])
    keep = np.nonzero(~props)[0]
    remap = -np.ones(len(A), int)
    remap[keep] = np.arange(len(keep))
    T = d[f"{tag}_T"]
    T = T[(~props[T]).all(1)]
    return A[keep], remap[T], keep


if __name__ == "__main__":
    info = {}
    for cls in ("Warrior", "Farmer", "Miner"):
        d = rl.load(cls)
        A, idx, wt = fix.bind_shape(d, "Hero")
        deform = [b for b in d["bones"] if d["Hero_W"][:, d["bones"].index(b)].sum() > 0 and not b.startswith("handslot")]
        Vb, Tb, keep = body_only(d, A)
        write(f"{cls}_cur", Vb, Tb, fighter_segments(d, d["S_P"], deform, A))
        HR, SP, inf = refit.compute(d, A, idx[:, 0])
        fk = dict(d)
        fk["rest_w"] = d["rest_w"].copy()
        fk["rest_w"][:, :3, 3] = HR
        write(f"{cls}_refit", Vb, Tb, fighter_segments(fk, SP, deform, A))
        np.savez_compressed(f"{IN}/refit_{cls}.npz", HR=HR, SP=SP)
        info[cls] = inf
    d = rl.load("Kaiju")
    heads, ends, chain = kaiju_rig()
    seg = {}
    for b, h in heads.items():
        e = heads[chain[b]] if b in chain else ends[b]
        seg[b] = (np.array(h, float), np.array(e, float))
    write("Kaiju_refit", d["Hero_V"], d["Hero_T"], seg)
    # also the current Kaiju joints (heat on the shipped rig) for comparison
    B = d["bones"]
    H = d["rest_w"][:, :3, 3]
    segc = {}
    for b in heads:
        i = B.index(b)
        e = H[B.index(chain[b])] if b in chain else d["tails"][i]
        segc[b] = (H[i], e)
    write("Kaiju_cur", d["Hero_V"], d["Hero_T"], segc)
    json.dump(dict(fighters=info, kaiju_heads=heads), open(f"{IN}/prep_info.json", "w"), indent=1, default=list)
    print(json.dumps(info))
