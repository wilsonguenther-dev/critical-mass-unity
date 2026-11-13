"""Environment v4 measurements in plain Python (no Blender): cost table, projected scene triangles, per-screen densities,
step-over check for the new placements, piece sizes before/after, proposed world-plan rows, JPEGs for the report.

    python3 measure_v4.py <run_out_dir> <report_dir> <v3_manifest.json> <world-plan.tsv>
"""
import json
import math
import os
import sys
from collections import Counter

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from env_art import layout as L1  # noqa: E402
from env_art import layout2, layout4  # noqa: E402

RUN, REP, MAN3, PLAN = sys.argv[1:5]
os.makedirs(REP, exist_ok=True)
m3 = {r["piece"]: r for r in json.load(open(MAN3))["pieces"]}
m4 = {r["piece"]: r for r in json.load(open(os.path.join(HERE, "manifest.json")))["pieces"]}
plan = {}
for ln in open(PLAN):
    f = ln.rstrip("\n").split("\t")
    if len(f) >= 3 and f[0] == "piece":
        plan[f[1]] = int(f[2])
lay = layout2.place_map()
new, subs = layout4.dress(lay)
newc = Counter(p["piece"] for p in new)
nsub = len(subs)
out = {}

# ------------------------------------------------------------------------------------------------ cost table
rows = []
d_exist = 0
env3 = env4 = 0
for nm, n in sorted(plan.items()):
    if not nm.startswith("env_") or nm not in m3:
        continue
    t3, t4 = m3[nm]["tris"], m4.get(nm, m3[nm])["tris"]
    cnt4 = n - (nsub if nm == "env_bridge_span" else 0)
    env3 += n * t3
    env4 += cnt4 * t4
    if t4 != t3 or nm == "env_bridge_span":
        rows.append((nm, "upgraded", t3, t4, n, cnt4, cnt4 * t4 - n * t3))
for nm, n in sorted(newc.items()):
    t4 = m4[nm]["tris"]
    env4 += n * t4
    rows.append((nm, "new", 0, t4, 0, n, n * t4))
if nsub:
    t4 = m4["env_bridge_span_damaged"]["tris"]
    env4 += nsub * t4
    rows.append(("env_bridge_span_damaged", "new (replaces span tiles)", 0, t4, 0, nsub, nsub * t4))
delta = env4 - env3
out["env_tris_v3_plan"] = env3
out["env_tris_v4_plan"] = env4
out["env_delta"] = delta
out["scene_projected"] = 950000 + delta
with open(os.path.join(REP, "cost-table.tsv"), "w") as f:
    f.write("piece\tstatus\ttris_v3\ttris_v4\tcount_v3\tcount_v4\tscene_delta_tris\n")
    for r in rows:
        f.write("\t".join(str(x) for x in r) + "\n")
    f.write(f"TOTAL env (world-plan counts)\t\t\t\t\t\t{env3} -> {env4} (delta {delta:+d}); scene ~950000 -> ~{950000 + delta}\n")

# ------------------------------------------------------------------------------------------------ sizes before / after
sz = []
for nm in sorted(m4):
    r4 = m4[nm]
    if r4.get("v4") != "upgraded":
        continue
    r3 = m3[nm]
    same = all(abs(a - b) < 0.011 for a, b in zip(r3["bounds_unity_min"] + r3["bounds_unity_max"], r4["bounds_unity_min"] + r4["bounds_unity_max"]))
    sz.append(dict(piece=nm, pivot=r4["pivot"], v3_min=r3["bounds_unity_min"], v3_max=r3["bounds_unity_max"],
                   v4_min=r4["bounds_unity_min"], v4_max=r4["bounds_unity_max"], same_box=same, tris=[r3["tris"], r4["tris"]]))
out["sizes"] = sz

# ------------------------------------------------------------------------------------------------ step-over check
g = lay["g"]
viol, edge_trees = [], 0
for p in new:
    if "a" in p or p["piece"] in ("env_lantern_hang",):
        continue
    top = m4[p["piece"]]["bounds_unity_max"][1]
    cell = g.t(int(math.floor(p["x"])), int(math.floor(p["z"])))
    if top > 0.3 and L1.walkable(cell):
        if p.get("cat") == "tree":
            edge_trees += 1
        else:
            viol.append((p["piece"], round(p["x"], 2), round(p["z"], 2), round(top, 2)))
out["tall_on_walkable"] = viol
out["new_edge_trees_on_rim_cells"] = edge_trees

# ------------------------------------------------------------------------------------------------ per-screen counts
shots = json.load(open(os.path.join(RUN, "shots.json")))["shots"]
D, P, Y, FOV = 23.4, 55.0, 45.0, 35.0
th = math.tan(math.radians(FOV / 2))


def proj(t, p):
    c, s = math.cos(math.radians(P)), math.sin(math.radians(P))
    pos = np.array([t[0] + D * c * math.sin(math.radians(Y)), t[1] + D * s, t[2] + D * c * math.cos(math.radians(Y))])
    f = np.array(t) - pos
    f /= np.linalg.norm(f)
    r = np.cross(f, [0, 1, 0])
    r /= np.linalg.norm(r)
    u = np.cross(r, f)
    v = np.array(p) - pos
    z = v @ f
    return 960 + (v @ r) / z / th * 540, 540 - (v @ u) / z / th * 540


old = lay["placements"]
groups = {"small": ("small",), "small_tall": ("small_tall",), "ground_decal": ("ground_decal",), "trees_big": ("tree", "rock_big", "landmark"),
          "lights": ("light",), "dress_tall": ("dress_tall",), "crystal": ("crystal",)}
dens = {}
for name, t in shots.items():
    res = {}
    for tag, pls in (("v3", old), ("v4", old + new)):
        cnt = Counter()
        for p in pls:
            if "x" not in p:
                continue
            sx, sy = proj(t, (p["x"], p["y"], p["z"]))
            if 0 <= sx <= 1920 and 0 <= sy <= 1080:
                for k, cats in groups.items():
                    if p.get("cat") in cats:
                        cnt[k] += 1
        res[tag] = dict(cnt)
    dens[name] = res
out["per_screen"] = dens

# ------------------------------------------------------------------------------------------------ image metrics
def edge_density(path):
    im = np.asarray(Image.open(path).convert("L").resize((960, 540)), dtype=np.float32)
    gx = np.abs(np.diff(im, axis=1))[:-1, :]
    gy = np.abs(np.diff(im, axis=0))[:, :-1]
    gm = np.hypot(gx, gy)
    return round(float((gm > 18).mean()), 4), round(float(im.std()), 1)


img = {}
for name in shots:
    b, a = os.path.join(RUN, "renders", f"{name}_before.png"), os.path.join(RUN, "renders", f"{name}_after.png")
    if os.path.exists(b) and os.path.exists(a):
        img[name] = {"before_edge_density_luma_std": edge_density(b), "after_edge_density_luma_std": edge_density(a)}
out["image_metrics"] = img

# ------------------------------------------------------------------------------------------------ JPEGs
def jpg(src, dst, w, q):
    im = Image.open(src).convert("RGB")
    if im.width > w:
        im = im.resize((w, round(im.height * w / im.width)), Image.LANCZOS)
    im.save(dst, quality=q, optimize=True)


R = os.path.join(RUN, "renders")
for f in sorted(os.listdir(R)):
    if f.endswith(".png"):
        jpg(os.path.join(R, f), os.path.join(REP, f[:-4] + ".jpg"), 1920, 82)
for name in shots:
    b, a = os.path.join(R, f"{name}_before.png"), os.path.join(R, f"{name}_after.png")
    if os.path.exists(b) and os.path.exists(a):
        A, B = Image.open(b).convert("RGB").resize((700, 394), Image.LANCZOS), Image.open(a).convert("RGB").resize((700, 394), Image.LANCZOS)
        S = Image.new("RGB", (1400, 394))
        S.paste(A, (0, 0))
        S.paste(B, (700, 0))
        S.save(os.path.join(REP, f"sbs-{name}-before-after.jpg"), quality=78, optimize=True)

# ------------------------------------------------------------------------------------------------ proposed world-plan rows
with open(os.path.join(REP, "proposed-world-plan-rows.tsv"), "w") as f:
    f.write("# Proposed rows for tools/art/world-plan.tsv (environment v4). Format of the existing rows: piece<TAB>name<TAB>count.\n")
    f.write("# Extra columns (comment for WorldBake.cs): placement rule | cap per 26x19 u gameplay screen | pivot | top height (u).\n")
    f.write("# Rules are layout4.RULES / layout4.dress (pure Python, ported like layout2). Counts are for Skybreak Basin.\n")
    for nm in sorted(set(newc) | ({"env_bridge_span_damaged"} if nsub else set())):
        rule, cap = layout4.RULES.get(nm, ("", ""))
        n = newc.get(nm, 0) or (nsub if nm == "env_bridge_span_damaged" else 0)
        f.write(f"piece\t{nm}\t{n}\t# {rule} | cap {cap}/screen | {m4[nm]['pivot']} | top {m4[nm]['bounds_unity_max'][1]}\n")
    if nsub:
        f.write(f"piece\tenv_bridge_span\t{plan.get('env_bridge_span', 0) - nsub}\t# was {plan.get('env_bridge_span', 0)}: {nsub} inner tiles become env_bridge_span_damaged\n")

json.dump(out, open(os.path.join(REP, "checks.json"), "w"), indent=1)
print(json.dumps({k: out[k] for k in ("env_tris_v3_plan", "env_tris_v4_plan", "env_delta", "scene_projected")}))
print("tall_on_walkable", len(viol), viol[:5], "edge_trees", edge_trees)
print("per_screen", json.dumps(dens))
print("image", json.dumps(img))
print("sizes_same_box", [(s["piece"], s["same_box"]) for s in sz if not s["same_box"]])
