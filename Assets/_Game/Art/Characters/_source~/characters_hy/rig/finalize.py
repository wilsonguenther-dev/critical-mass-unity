"""Write the L3 plan (exports, render frames) and the Kaiju face-decal UV fit into its fix npz."""
import json
import os

import numpy as np

import riglib as rl

H = os.path.expanduser("~")
OUT = H + "/cm-rig-scratch/out"
ART = H + "/critical-mass-unity-art-hy/Assets/_Game/Art"
EXP = H + "/cm-rig-scratch/export"
FACE = dict(cx=0.0, cz=3.05, w=1.15, h=0.75, ymax=-2.25, nmin=0.25)


def face_uv(d, tag, f=FACE):
    V, T = d[f"{tag}_V"], d[f"{tag}_T"]
    n, a = rl.tri_normals(V, T)
    vn = np.zeros_like(V)
    np.add.at(vn, T.ravel(), np.repeat(n * a[:, None], 3, 0))
    vn /= np.linalg.norm(vn, axis=1, keepdims=True) + 1e-12
    s = 0.5 + (V[:, 0] - f["cx"]) / f["w"]
    t = 0.5 + (V[:, 2] - f["cz"]) / f["h"]
    inside = (V[:, 1] < f["ymax"]) & (-vn[:, 1] > f["nmin"]) & (s > -0.05) & (s < 1.05) & (t > -0.05) & (t < 1.05)
    return np.stack([s, t, inside.astype(float)], 1).astype(np.float32)


def plan(chars):
    p = {"render_dir": H + "/cm-rig-scratch/renders", "chars": {}}
    fbx = {"Warrior": ART + "/Characters/Warrior.fbx", "Farmer": ART + "/Farmer/Farmer.fbx", "Miner": ART + "/Miner/_hy~/Miner.fbx",
           "Kaiju": ART + "/Kaiju/_hy~/Kaiju_Adult.fbx"}
    for cls in chars:
        met = json.load(open(f"{OUT}/metrics_{cls}.json"))
        b, a = met["Hero_before"], met["Hero_after"]
        worst = {c: {"before": b[c]["worst_frame"], "after": a[c]["worst_frame"]} for c in b}
        hard = sorted(b, key=lambda c: -(b[c]["over15"] + 5 * b[c]["over2"] + 20 * b[c]["inv"]))[:2]
        strips = [[c, [int(round(x)) for x in np.linspace(0, b[c]["frames"] - 1, 6)]] for c in hard]
        k = cls == "Kaiju"
        p["chars"][cls] = dict(
            fbx=fbx[cls], fix=f"{OUT}/fix_{cls}.npz",
            export=f"{EXP}/{cls}/{'Kaiju_Adult' if k else cls}.fbx",
            fallback=ART + "/Farmer/_fallback~/Farmer.fbx" if cls == "Farmer" else None,
            renders=dict(worst=worst, strips=strips, centre=(0, -0.3, 2.5) if k else (0, 0, 1.0), ortho=7.2 if k else 2.9,
                         crop=480 if k else 230, roar_frame=18))
    json.dump(p, open(f"{OUT}/plan.json", "w"), indent=1)
    return p


if __name__ == "__main__":
    import sys
    chars = sys.argv[1:] or ["Warrior", "Farmer", "Miner", "Kaiju"]
    if "Kaiju" in chars:
        d = rl.load("Kaiju")
        z = dict(np.load(f"{OUT}/fix_Kaiju.npz"))
        for t in ("Hero", "LOD0", "LOD1"):
            z[f"FUV_{t}"] = face_uv(d, t)
            print("face verts", t, int(z[f"FUV_{t}"][:, 2].sum()))
        np.savez_compressed(f"{OUT}/fix_Kaiju.npz", **z)
    print(json.dumps(plan(chars)["chars"], indent=0)[:1500])
