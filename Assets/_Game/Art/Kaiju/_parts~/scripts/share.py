# python3 -I share.py <dir>: team-region share of the silhouette per type and yaw (mask renders: red = team code 6/8)
import sys, json
import numpy as np
from PIL import Image
D = sys.argv[1]; T = ["Elder", "Grove", "Brute", "Timber", "Mire"]; Y = [45, 135, 225, 300]; out = {}
for t in T:
    v = []
    for y in Y:
        a = np.array(Image.open(f"{D}/mask_{t}_{y}.png").convert("RGBA")).astype(int)
        sil = a[..., 3] > 127; team = sil & (a[..., 0] > 128) & (a[..., 2] < 128)
        v.append(round(100.0 * team.sum() / sil.sum(), 1))
    out[t] = v; print(f"{t:>7} team share % at yaws {Y}: {v}  min {min(v)}")
json.dump(out, open(f"{D}/team_share.json", "w"))
