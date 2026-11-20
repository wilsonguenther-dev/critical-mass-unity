# python3 -I iou.py <dir> [prefix]: pairwise silhouette IoU per yaw (same camera, same canvas), plus pixel height of each mask
import sys, itertools, json
import numpy as np
from PIL import Image
D = sys.argv[1]; P = sys.argv[2] if len(sys.argv) > 2 else "sil_"
T = ["Elder", "Grove", "Brute", "Timber", "Mire"]; Y = [45, 135, 225, 300]
M = {(t, y): np.array(Image.open(f"{D}/{P}{t}_{y}.png").convert("RGBA"))[..., 3] > 127 for t in T for y in Y}
out = {"px_height": {}, "px_area": {}, "iou_max": {}, "iou_mean": {}}
for t in T:
    hs = []; ar = []
    for y in Y:
        rows = np.where(M[(t, y)].any(1))[0]; hs.append(int(rows[-1] - rows[0] + 1)); ar.append(int(M[(t, y)].sum()))
    out["px_height"][t] = hs; out["px_area"][t] = ar
print("px height per yaw", Y, json.dumps(out["px_height"])); print("px area per yaw", json.dumps(out["px_area"]))
print("IoU max over yaws / mean")
print("        " + "".join(f"{t:>12}" for t in T))
for a in T:
    row = []
    for b in T:
        v = [float((M[(a, y)] & M[(b, y)]).sum() / (M[(a, y)] | M[(b, y)]).sum()) for y in Y]
        row.append(f"{max(v):.2f}/{np.mean(v):.2f}"); 
        if a < b: out["iou_max"][a + "-" + b] = round(max(v), 3); out["iou_mean"][a + "-" + b] = round(float(np.mean(v)), 3)
    print(f"{a:>8}" + "".join(f"{r:>12}" for r in row))
worst = max(out["iou_max"].items(), key=lambda kv: kv[1]); print("worst pair", worst)
json.dump(out, open(f"{D}/iou.json", "w"), indent=1)
