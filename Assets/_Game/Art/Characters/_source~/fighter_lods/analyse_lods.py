# Measures the LOD build outside Blender (plain Python + numpy + Pillow): silhouette overlap and colour difference of each
# level against the Hero at gameplay size, team-mask area share drift, stretched (torn) edges per pose against the Hero,
# triangle counts from the re-imported FBX, projected scene totals; writes a comparison sheet per character.
# Run: python3 analyse_lods.py <scratch> <sheet_dir>
import json, os, sys
import numpy as np
from PIL import Image, ImageDraw

ROOT, SHEETS = sys.argv[1], sys.argv[2]
DATA, REN = os.path.join(ROOT, "data"), os.path.join(ROOT, "renders")
os.makedirs(SHEETS, exist_ok=True)
LEVELS = ("Hero", "LOD0", "LOD1", "Shadow")
TEAM = {3, 4, 5, 6, 7, 8}
GATE = {"LOD0": 0.97, "LOD1": 0.93}
STRETCH = 1.5


def load(p):
    return np.asarray(Image.open(p).convert("RGBA")).astype(np.float32)


def overlap(a, b):
    ma, mb = a[..., 3] > 127, b[..., 3] > 127
    inter, union = (ma & mb).sum(), (ma | mb).sum()
    solid = (a[..., 3] > 200) & (b[..., 3] > 200)
    d = np.abs(a[..., :3] - b[..., :3]).max(axis=2)[solid]
    return float(inter / max(union, 1)), float(d.mean()) if d.size else 0.0, float((d > 48).mean()) if d.size else 0.0, int(ma.sum())


REGION_GATE = {"LOD0": 16.0, "LOD1": 24.0}


def region_diff(h, x, n=3):
    """Largest per-region (n x n cells over the Hero silhouette box) difference of mean colour, 0-255."""
    # colour only where both renders cover the pixel (silhouette loss is the overlap gate's job); a region counts when
    # it holds at least 3 % of the silhouette, so a few pixels of a tool tip cannot decide it
    both = (h[..., 3] > 200) & (x[..., 3] > 200)
    mh = mx = both
    ys, xs = np.nonzero(h[..., 3] > 127)
    min_px = max(25, int(0.03 * len(ys)))
    if not len(ys):
        return 0.0
    y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    worst = 0.0
    for i in range(n):
        for j in range(n):
            sl = (slice(y0 + (y1 - y0) * i // n, y0 + (y1 - y0) * (i + 1) // n),
                  slice(x0 + (x1 - x0) * j // n, x0 + (x1 - x0) * (j + 1) // n))
            a, b = mh[sl], mx[sl]
            if a.sum() < min_px:
                continue
            ca = h[sl][a][:, :3].mean(0); cb = x[sl][b][:, :3].mean(0)
            worst = max(worst, float(np.abs(ca - cb).max()))
    return worst


def height_px(a):
    rows = np.nonzero((a[..., 3] > 127).any(axis=1))[0]
    return int(rows[-1] - rows[0] + 1) if len(rows) else 0


out = {}
for fn in sorted(os.listdir(DATA)):
    if not fn.endswith(".json"):
        continue
    r = json.load(open(os.path.join(DATA, fn)))
    key = r["key"]
    if not r.get("ok"):
        out[key] = dict(ok=False, error=r.get("error", "")[-400:])
        continue
    raw = np.load(os.path.join(DATA, key + ".npz"))
    rd = os.path.join(REN, key)
    res = dict(ok=True, levels={})
    tris_out = {n.rsplit("_", 1)[-1]: v["tris"] for n, v in r["out_stats"]["meshes"].items()}
    shares = {}
    ALL = list(r["levels"].keys())
    ALL = ["Hero"] + [k for k in ALL if k != "Hero"]
    scale = r.get("scale", 1.0)
    for lv in ALL:
        area, code = raw[lv + "_area"], raw[lv + "_code"]
        tot = float(area.sum())
        shares[lv] = {int(c): float(area[code == c].sum() / tot) for c in np.unique(code)}
    for lv in ALL:
        L = dict(tris=tris_out.get(lv, r["levels"][lv].get("tris")), tris_build=r["levels"][lv].get("tris"))
        team = sum(v for c, v in shares[lv].items() if c in TEAM)
        hteam = sum(v for c, v in shares["Hero"].items() if c in TEAM)
        L["team_share_pct"] = round(100 * team, 2)
        L["team_drift_pp"] = round(100 * abs(team - hteam), 2)
        codes = set(shares[lv]) | set(shares["Hero"])
        L["max_code_drift_pp"] = round(100 * max(abs(shares[lv].get(c, 0) - shares["Hero"].get(c, 0)) for c in codes), 2)
        ov, cd, cbad, rg = [], [], [], []
        for tag in ("front", "back"):
            h = load(os.path.join(rd, "Hero_%s_game.png" % tag))
            x = load(os.path.join(rd, "%s_%s_game.png" % (lv, tag)))
            o, c, b, _ = overlap(h, x)
            ov.append(o); cd.append(c); cbad.append(b); rg.append(region_diff(h, x))
            if lv == "Hero" and tag == "front":
                res["hero_height_px"] = height_px(h)
        L["overlap"] = round(min(ov), 4)
        L["colour_mean_diff"] = round(max(cd), 2)
        L["colour_bad_px_pct"] = round(100 * max(cbad), 2)
        L["region_colour_diff"] = round(max(rg), 1)
        base_lv = lv.split("_")[0]
        if base_lv in REGION_GATE:
            L["region_pass"] = L["region_colour_diff"] <= REGION_GATE[base_lv]
        if lv in GATE:
            L["overlap_pass"] = L["overlap"] >= GATE[lv]
        # torn: an edge stretched 25 % and 2 cm (x scale) beyond the Hero surface between the same two points, or a
        # bridge edge (endpoints on different Hero parts) stretched over 1.5x; raw: any edge over 1.5x its rest length
        torn, raw_n, worst, bridges = 0, 0, 0.0, int(raw[lv + "_bridge"].sum())
        for k in raw.files:
            if k.startswith(lv + "_l0_"):
                tag = k[len(lv) + 4:]
                l0, l1 = raw[k], raw["%s_l1_%s" % (lv, tag)]
                r0, r1 = raw["%s_r0_%s" % (lv, tag)], raw["%s_r1_%s" % (lv, tag)]
                exp = l0 * r1 / np.maximum(r0, 1e-6)
                t = ((l1 > 1.25 * exp) & (l1 - exp > 0.02 * scale)) | (raw[lv + "_bridge"] & (l1 > 1.5 * l0))
                torn += int(t.sum()); raw_n += int((l1 > STRETCH * l0).sum())
                worst = max(worst, float((l1 / np.maximum(exp, 1e-6)).max()))
        L["torn_edges"] = torn
        L["stretched_edges"] = raw_n
        L["bridge_edges"] = bridges
        L["max_stretch_vs_hero"] = round(worst, 2)
        L["deviation_p99_cm"] = max(r.get("deviation_cm", {}).get(lv, [0]))
        for k in ("survival", "decal_mismatch", "dropped_island_verts", "max_influences", "planar_tris", "method", "islands",
                  "shard_islands", "parts"):
            if k in r["levels"][lv]:
                L[k] = r["levels"][lv][k]
        res["levels"][lv] = L
    so, oo = r["source_stats"], r["out_stats"]
    res["roundtrip"] = dict(
        bones_src=so.get("bones"), bones_out=oo.get("bones"), bone_head_max_delta_m=r.get("bone_head_max_delta"),
        bones_missing=r.get("bones_missing"), clips_src=so.get("actions"), clips_out=oo.get("actions"),
        meshes_out={n: dict(tris=v["tris"], colours=v["colours"], codes=v["codes"], uvs=v["uvs"], materials=v["materials"],
                            max_inf=v["max_influences"], bbox=[v["bbox_min"], v["bbox_max"]]) for n, v in oo["meshes"].items()},
        source_meshes={n: v["tris"] for n, v in so["meshes"].items()},
        source_bbox=[[min(v["bbox_min"][i] for v in so["meshes"].values()) for i in range(3)],
                     [max(v["bbox_max"][i] for v in so["meshes"].values()) for i in range(3)]])
    for k in ("vgroup_invert", "survival_other_sense", "sidecar_match", "sidecar_out", "protect_counts", "poses",
              "render_engine", "seconds", "joined", "orientation_proof", "screen_y", "screen_y_rest", "idle_leg_dir",
              "idle_spine_dir", "decal_outside_uv"):
        res[k] = r.get(k)
    out[key] = res

    # sheet: row 1 gameplay size (native pixels), row 2 enlarged, front three-quarter; labels with numbers
    cell, gap = 330, 10
    big = [Image.open(os.path.join(rd, "%s_front_big.png" % lv)).convert("RGBA") for lv in LEVELS]
    game = [Image.open(os.path.join(rd, "%s_front_game.png" % lv)).convert("RGBA") for lv in LEVELS]
    W = 4 * cell + 5 * gap
    gh = max(g.height for g in game)
    H = 40 + gh + 30 + cell + 50
    sheet = Image.new("RGB", (W, H), (128, 128, 128))
    d = ImageDraw.Draw(sheet)
    d.text((gap, 8), "%s  gameplay camera (pitch 55, vFOV 35, 23.4 u): top row at in-game pixel size, bottom enlarged" % key,
           fill=(255, 255, 255))
    for i, lv in enumerate(LEVELS):
        x0 = gap + i * (cell + gap)
        g = game[i]
        sheet.paste(g, (x0 + (cell - g.width) // 2, 40), g)
        b = big[i]
        s = cell / max(b.width, b.height)
        b = b.resize((int(b.width * s), int(b.height * s)), Image.LANCZOS)
        sheet.paste(b, (x0 + (cell - b.width) // 2, 40 + gh + 30), b)
        L = res["levels"][lv]
        lab = "%s  %s tris" % (lv, L["tris"])
        if lv != "Hero":
            lab += "  overlap %.3f" % L["overlap"]
        d.text((x0, 40 + gh + 8), lab, fill=(255, 255, 255))
        d.text((x0, H - 40), "team %.1f%% (drift %.2f pp)  torn %d" % (
            L["team_share_pct"], L["team_drift_pp"], L["torn_edges"]), fill=(20, 20, 20))
    p = os.path.join(SHEETS, "sheet-%s.jpg" % key)
    q = 82
    while True:
        sheet.save(p, "JPEG", quality=q, optimize=True)
        if os.path.getsize(p) < 180_000 or q < 40:
            break
        q -= 8

# projected in-match scene totals (fighter + Kaiju share only): lit pass + outline hull pass + shadow pass
fighters = [k for k, v in out.items() if v.get("ok") and not k.startswith("Kaiju")]
if fighters:
    def avg(lv):
        return float(np.mean([out[k]["levels"][lv]["tris"] for k in fighters]))
    ka = out.get("KaijuAdult", {}).get("levels")
    kh, k0, ks = (ka["Hero"]["tris"], ka["LOD0"]["tris"], ka["Shadow"]["tris"]) if ka else (0, 0, 0)
    H, L0, L1, S = avg("Hero"), avg("LOD0"), avg("LOD1"), avg("Shadow")
    out["_scene"] = dict(
        fighter_avg=dict(Hero=round(H), LOD0=round(L0), LOD1=round(L1), Shadow=round(S)), fighters=fighters,
        today_32_plus_kaiju=round(32 * H * 3 + kh * 3),
        high_all_near=round(32 * (2 * L0 + S) + 2 * k0 + ks),
        high_typical=round(8 * (2 * L0 + S) + 24 * (2 * L1 + S) + 2 * k0 + ks),
        medium_typical=round(8 * (2 * L0 + S) + 24 * (L1 + S) + 2 * k0 + ks),
        low=round(1 * 2 * L0 + 31 * L1 + 2 * k0),
        note="lit + hull + shadow per fighter today (Hero mesh draws in match, casts itself); after: LOD0 lit+hull, LOD1 lit "
             "(+hull at High), Shadow proxy casts at Medium/High; typical = 8 within 25 u of the camera target, 24 beyond; "
             "Low = own fighter LOD0, everyone else LOD1 without hull, no shadow casters")
# method choice for the final run: LOD0 best overlap among region-clean variants; LOD1 the best variant that is closed
# and coherent (overlap >= 0.93, torn <= 50, shards <= 10, region colour clean) or else LOD1 = LOD0; Shadow = hull proxy
choice = {}
for k, v in out.items():
    if k.startswith("_") or not v.get("ok"):
        continue
    lv = v["levels"]
    def cands(base):
        return [(n, L) for n, L in lv.items() if n == base or n.startswith(base + "_")]
    c0 = sorted(cands("LOD0"), key=lambda t: (t[1].get("region_pass", True), t[1]["overlap"]), reverse=True)
    c1 = [t for t in cands("LOD1") if t[1]["overlap"] >= 0.93 and t[1]["torn_edges"] <= 50 and
          t[1].get("shard_islands", 0) <= 10 and t[1].get("region_pass", True)]
    c1 = sorted(c1, key=lambda t: t[1]["overlap"], reverse=True)
    cs = [t for t in cands("Shadow") if t[1].get("method") == "hull"]
    choice[k] = dict(LOD0=c0[0][1].get("method"), LOD1=c1[0][1].get("method") if c1 else "lod0",
                     Shadow="hull" if cs else lv["Shadow"].get("method"))
json.dump(choice, open(os.path.join(ROOT, "choice.json"), "w"), indent=1)
print("CHOICE", json.dumps(choice))
json.dump(out, open(os.path.join(ROOT, "analysis.json"), "w"), indent=1, default=str)
for k, v in out.items():
    if k.startswith("_"):
        print(k, json.dumps(v)[:600])
    elif not v.get("ok"):
        print(k, "FAILED", v.get("error", "")[-300:])
    else:
        print(k, "h%dpx" % v.get("hero_height_px", 0), " | ".join(
            "%s %s ov%.3f rg%.0f team%.2f torn%d sh%s/%s dec%s" % (
                lv, L["tris"], L["overlap"], L["region_colour_diff"], L["team_drift_pp"],
                L["torn_edges"], L.get("shard_islands", "-"), L.get("islands", "-"), L.get("decal_mismatch", "-"))
            for lv, L in v["levels"].items()), "rt", v["roundtrip"]["bones_src"],
            v["roundtrip"]["bones_out"], v["roundtrip"]["bone_head_max_delta_m"], len(v["roundtrip"]["clips_src"] or []),
            len(v["roundtrip"]["clips_out"] or []))
