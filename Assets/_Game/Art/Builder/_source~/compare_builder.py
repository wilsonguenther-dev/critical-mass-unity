"""Builder render set + measurable checks (system python3 + Pillow + numpy + scipy, not Blender).
Adapted from the Miner's compare_miner.py.

  python3 compare_builder.py <render_dir> <out_dir> [bible_ref_dir]

There is no bible page for the Builder, so the references are the bible's three class pages (06 Warrior, 07 Farmer,
08 Miner) and the other classes' current 64 px gameplay silhouettes. Writes into out_dir:
  gameplay-<team>.jpg, gameplay-2x-<team>.jpg, gameplay-props-<team>.jpg, gameplay-2x-warrior-ladder.jpg
  turnaround.jpg            front | three-quarter | profile | back (for the artist's approval)
  sbs-lineup.jpg            bible 06 / 07 / 08 fronts LEFT, ours RIGHT, same figure height
  sbs-<view>.jpg            the bible's three classes in that view LEFT, ours RIGHT
  sbs-bridge.jpg, sbs-ramp-ladder.jpg   p14 props LEFT, our bridge / ladder states RIGHT
  sbs-poses.jpg, sbs-team-swap.jpg, silhouette-64.jpg, checks.json
"""
import json
import math
import os
import re
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage as nd

R = os.path.abspath(sys.argv[1])
OUT = os.path.abspath(sys.argv[2])
REF = sys.argv[3] if len(sys.argv) > 3 else os.path.expanduser("~/critical-mass/loop/parity/ref")
PORT = os.path.dirname(OUT)
os.makedirs(OUT, exist_ok=True)
PAPER = (254, 248, 238)
NAVY = (0x18, 0x23, 0x3B)
CREAM = (0xF7, 0xF0, 0xE5)
ORDER = ["N", "NW", "W", "SW", "S", "SE", "E", "NE"]
checks = {}


def font(sz):
    for f in ("/System/Library/Fonts/Supplemental/Arial.ttf", "/System/Library/Fonts/Helvetica.ttc"):
        try:
            return ImageFont.truetype(f, sz)
        except OSError:
            pass
    return ImageFont.load_default()


def save_jpg(im, path, cap=180_000, maxw=1400):
    if maxw and im.size[0] > maxw:
        im = im.resize((maxw, round(im.size[1] * maxw / im.size[0])), Image.LANCZOS)
    for q in (88, 82, 76, 70, 64, 58, 52, 46, 40):
        im.save(path, "JPEG", quality=q, optimize=True)
        if os.path.getsize(path) < cap:
            break
    return os.path.getsize(path)


def on_paper(path, bg=PAPER):
    im = Image.open(path).convert("RGBA")
    b = Image.new("RGBA", im.size, bg + (255,))
    b.alpha_composite(im)
    return b.convert("RGB"), np.asarray(im)[..., 3] > 8


def bbox(mask):
    ys, xs = np.nonzero(mask)
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1


def crop_to(im, mask, pad=6):
    x0, y0, x1, y1 = bbox(mask)
    return im.crop((max(0, x0 - pad), max(0, y0 - pad), min(im.size[0], x1 + pad), min(im.size[1], y1 + pad)))


def bible_mask(im):
    """Clean figure mask of a bible crop: differs from paper, main component group, holes filled, ground shadow cut."""
    a = np.asarray(im.convert("RGB")).astype(int)
    border = np.concatenate([a[:3].reshape(-1, 3), a[-3:].reshape(-1, 3), a[:, :3].reshape(-1, 3),
                             a[:, -3:].reshape(-1, 3)])
    paper = np.median(border, axis=0)
    fg = np.abs(a - paper).sum(2) > 40
    light_grey = (a.max(2) - a.min(2) < 20) & (a.mean(2) > 150)
    ys = np.nonzero(fg.any(1))[0]
    if len(ys):
        cut = ys[0] + int(0.82 * (ys[-1] - ys[0]))
        fg[cut:] &= ~light_grey[cut:]
    lab, n = nd.label(nd.binary_dilation(fg, iterations=2))
    if n:
        sizes = nd.sum(fg, lab, range(1, n + 1))
        fg &= lab == (1 + int(np.argmax(sizes)))
    fg = nd.binary_fill_holes(fg)
    return nd.binary_opening(fg, iterations=1)


def norm_mask(m, size=(128, 160)):
    x0, y0, x1, y1 = bbox(m)
    c = m[y0:y1, x0:x1]
    h, w = c.shape
    s = min(size[0] / w, size[1] / h)
    im = Image.fromarray((c * 255).astype(np.uint8)).resize((max(1, round(w * s)), max(1, round(h * s))),
                                                             Image.BILINEAR)
    canvas = np.zeros((size[1], size[0]), bool)
    a = np.asarray(im) > 127
    oy, ox = size[1] - a.shape[0], (size[0] - a.shape[1]) // 2        # feet on the bottom edge, centred
    canvas[oy:oy + a.shape[0], ox:ox + a.shape[1]] = a
    return canvas


def iou(a, b):
    return float((a & b).sum() / max(1, (a | b).sum()))


def ref(f):
    return Image.open(os.path.join(REF, f)).convert("RGB")


def rp(f):
    return os.path.join(R, f)


def strip(items, H, gap=8, bg=PAPER):
    sc = [i.resize((max(1, round(i.size[0] * H / i.size[1])), H), Image.LANCZOS) for i in items]
    out = Image.new("RGB", (sum(i.size[0] for i in sc) + gap * (len(sc) - 1), H), bg)
    x = 0
    for i in sc:
        out.paste(i, (x, 0))
        x += i.size[0] + gap
    return out


def sbs(name, left, right, label_l, label_r, H=480, note=None):
    L, Rr = strip(left, H), strip(right, H)
    gap, top = 16, 30
    W = L.size[0] + Rr.size[0] + gap * 3
    canvas = Image.new("RGB", (W, H + top + gap), (255, 255, 255))
    canvas.paste(L, (gap, top))
    canvas.paste(Rr, (gap * 2 + L.size[0], top))
    d = ImageDraw.Draw(canvas)
    f = font(17)
    d.text((gap, 6), "bible: " + label_l, fill=(40, 40, 50), font=f)
    d.text((gap * 2 + L.size[0], 6), "ours: " + label_r, fill=(40, 40, 50), font=f)
    if note:
        d.text((W - 12 - d.textlength(note, font=f), 6), note, fill=(40, 40, 50), font=f)
    path = os.path.join(OUT, f"sbs-{name}.jpg")
    return {"file": os.path.basename(path), "bytes": save_jpg(canvas, path)}


# ---------------------------------------------------------------- gameplay renders
for src, dst in (("gameplay_azure.png", "gameplay-azure.jpg"), ("gameplay_crimson.png", "gameplay-crimson.jpg"),
                 ("gameplay2x_azure.png", "gameplay-2x-azure.jpg"), ("gameplay2x_crimson.png", "gameplay-2x-crimson.jpg"),
                 ("gameplay_props_azure.png", "gameplay-props-azure.jpg"),
                 ("gameplay_props_crimson.png", "gameplay-props-crimson.jpg"),
                 ("gameplay2x_warrior_ladder.png", "gameplay-2x-warrior-ladder.jpg")):
    if os.path.exists(rp(src)):
        checks.setdefault("gameplay_files", {})[dst] = save_jpg(Image.open(rp(src)).convert("RGB"),
                                                               os.path.join(OUT, dst), maxw=1920)

# ---------------------------------------------------------------- facings: team %, height, outline, 64 px silhouettes
fac, ours64 = {}, {}
sil_tiles = []
for d in ORDER:
    rgba = np.asarray(Image.open(rp(f"facing_{d}.png")).convert("RGBA"))
    msk = np.asarray(Image.open(rp(f"facing_{d}_mask.png")).convert("RGBA"))
    alpha = rgba[..., 3] > 8
    team = (msk[..., 1] > 128) & alpha
    x0, y0, x1, y1 = bbox(alpha)
    ink = (msk[..., 0] > 100) & (msk[..., 1] < 90) & (msk[..., 3] > 100)
    cur, width = alpha.copy(), 0.0
    for _ in range(10):
        er = cur.copy()
        er[1:, :] &= cur[:-1, :]
        er[:-1, :] &= cur[1:, :]
        er[:, 1:] &= cur[:, :-1]
        er[:, :-1] &= cur[:, 1:]
        layer = cur & ~er
        width += (ink & layer).sum() / max(1, layer.sum())
        cur = er
    fac[d] = {"team_pct": round(100 * team.sum() / alpha.sum(), 2), "silhouette_px_h": int(y1 - y0),
              "silhouette_px_w": int(x1 - x0), "outline_px_mean_peel": round(float(width), 2)}
    a = Image.fromarray((alpha[y0:y1, x0:x1] * 255).astype(np.uint8))
    w64 = max(1, round(a.size[0] * 64 / a.size[1]))
    a = a.resize((w64, 64), Image.LANCZOS)
    ours64[d] = np.asarray(a) > 127
    tile = Image.new("RGB", (w64, 64), CREAM)
    tile.paste(Image.new("RGB", (w64, 64), NAVY), (0, 0), a)
    sil_tiles.append((d, tile))
checks["facings"] = fac
checks["team_pct_min"] = min(v["team_pct"] for v in fac.values())
checks["team_pct_mean"] = round(sum(v["team_pct"] for v in fac.values()) / 8, 2)


def tiles_from_sheet(path):
    """Navy-on-cream 64 px silhouette sheets of the other classes: each tall dark component is one facing."""
    a = np.asarray(Image.open(path).convert("L")).astype(int)
    dark = a < 110
    lab, n = nd.label(dark)
    out = []
    objs = nd.find_objects(lab)
    hs = [s[0].stop - s[0].start for s in objs]
    if not hs:
        return out
    hmax = max(hs)
    for i, s in enumerate(objs):
        if hs[i] >= 0.6 * hmax:
            out.append(lab[s] == i + 1)
    return out


others = {"warrior-v3": "silhouette-64px.jpg", "farmer-v1": "silhouettes-64px.jpg", "miner-v3": "silhouette-64.jpg"}
ov = {}
for folder, fn in others.items():
    p = os.path.join(PORT, folder, fn)
    if not os.path.exists(p):
        continue
    tl = [norm_mask(t, (96, 96)) for t in tiles_from_sheet(p)]
    if not tl:
        continue
    per = {}
    for d in ORDER:
        o = norm_mask(ours64[d], (96, 96))
        per[d] = round(max(iou(o, t) for t in tl), 3)
    ov[folder] = {"tiles_found": len(tl), "max_iou_per_our_facing": per, "worst": max(per.values()),
                  "mean": round(sum(per.values()) / len(per), 3)}
checks["gameplay_64px_overlap_vs_other_classes"] = ov

# 64 px sheet: ours on top, the Miner's below for comparison
pad = 18
W = sum(t.size[0] for _, t in sil_tiles) + pad * (len(sil_tiles) + 1)
sheet = Image.new("RGB", (W, 64 + 2 * pad + 18), CREAM)
dr = ImageDraw.Draw(sheet)
x = pad
for nm, t in sil_tiles:
    sheet.paste(t, (x, pad))
    dr.text((x + t.size[0] // 2 - 6, 64 + pad + 2), nm, fill=NAVY, font=font(13))
    x += t.size[0] + pad
big = sheet.resize((sheet.size[0] * 2, sheet.size[1] * 2), Image.NEAREST)
save_jpg(big, os.path.join(OUT, "silhouette-64.jpg"))

# ---------------------------------------------------------------- turnaround, line-up, per-view side-by-sides
views = {}
for v in ("front", "three-quarter", "profile", "back"):
    im, m = on_paper(rp(f"ortho_{v}_azure.png"))
    views[v] = (crop_to(im, m), m)
H = 620
tr = strip([views[v][0] for v in ("front", "three-quarter", "profile", "back")], H, gap=30)
canvas = Image.new("RGB", (tr.size[0] + 40, H + 70), PAPER)
canvas.paste(tr, (20, 50))
dr = ImageDraw.Draw(canvas)
dr.text((20, 12), "BUILDER (DG-08, proposed) turnaround: front | three-quarter | profile | back. Azure. For approval.",
        fill=NAVY, font=font(22))
checks["turnaround"] = save_jpg(canvas, os.path.join(OUT, "turnaround.jpg"))

bfront = [ref(f) for f in ("06-front.png", "07-front.png", "08-front.png")]
bm = [bible_mask(i) for i in bfront]
bfront_c = [crop_to(i, m, 4) for i, m in zip(bfront, bm)]
res = {}
res["lineup"] = sbs("lineup", bfront_c, [views["front"][0]], "06 Warrior | 07 Farmer | 08 Miner (front)",
                    "Builder front, same figure height", H=520)
# silhouette overlap against the three bible silhouettes, view by view (bbox-normalised, feet aligned)
ovb = {}
for v, tag in (("front", "front"), ("three-quarter", "three-quarter"), ("profile", "profile"), ("back", "back")):
    ours = norm_mask(views[v][1])
    row = {}
    left = []
    for cls, pre in (("warrior", "06"), ("farmer", "07"), ("miner", "08")):
        f = f"{pre}-{tag}.png"
        if not os.path.exists(os.path.join(REF, f)):
            continue
        im = ref(f)
        mk = bible_mask(im)
        row[cls] = round(iou(ours, norm_mask(mk)), 3)
        left.append(crop_to(im, mk, 4))
    ovb[v] = row
    res[v] = sbs(v, left, [views[v][0]], f"06 | 07 | 08 {v}", f"Builder {v}", H=420,
                 note="silhouette IoU vs W/F/M " + " / ".join(f"{row[k]:.2f}" for k in row))
checks["bible_silhouette_iou"] = ovb
checks["bible_silhouette_iou_max"] = max(x for r in ovb.values() for x in r.values())

# poses and team swap
pl = []
for f in ("ortho_pose-build_azure.png", "ortho_pose-interact_azure.png", "ortho_top_azure.png"):
    if os.path.exists(rp(f)):
        im, m = on_paper(rp(f))
        pl.append(crop_to(im, m))
res["poses"] = sbs("poses", [crop_to(ref("08-pose-wall-break.png"), bible_mask(ref("08-pose-wall-break.png")), 4)],
                   pl, "08-pose-wall-break (structure only)", "build swing (1H chop) | interact | top view", H=420)
az, am = on_paper(rp("ortho_accent_azure.png"))
cr, cm = on_paper(rp("ortho_accent_crimson.png"))
res["team-swap"] = sbs("team-swap", [ref("08-front.png"), ref("08-accent-swap.png")], [crop_to(az, am), crop_to(cr, cm)],
                       "08-front | 08-accent-swap", "Azure | Crimson", H=420)
a = np.asarray(Image.open(rp("ortho_accent_azure.png")).convert("RGBA")).astype(int)
c = np.asarray(Image.open(rp("ortho_accent_crimson.png")).convert("RGBA")).astype(int)
changed = (np.abs(a[..., :3] - c[..., :3]).sum(2) > 24) & (a[..., 3] > 200)
checks["crimson_changed_pct_of_character"] = round(100 * changed.sum() / (a[..., 3] > 200).sum(), 2)

# props next to bible p14
def prop(name):
    im, m = on_paper(rp(f"ortho_{name}_azure.png"))
    return crop_to(im, m)


res["bridge"] = sbs("bridge", [ref("14-bridge.png")], [prop("bridge-carried"), prop("bridge-placed"),
                                                       prop("bridge-damaged")],
                    "14-bridge", "rope bridge: carried | placed (4 cells) | damaged", H=300)
res["ramp-ladder"] = sbs("ramp-ladder", [ref("14-ramp.png")], [prop("ladder-carried"), prop("ladder-placed"),
                                                               prop("ladder-damaged")],
                         "14-ramp (the bible's elevation prop)", "ladder: carried | placed | damaged", H=300)
checks["sbs"] = res


# ---------------------------------------------------------------- colour checks (dE2000)
def srgb2lab(h):
    if isinstance(h, str):
        h = h.lstrip("#")
        rgb = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    else:
        rgb = [x / 255 for x in h]
    lin = [c_ / 12.92 if c_ <= 0.04045 else ((c_ + 0.055) / 1.055) ** 2.4 for c_ in rgb]
    X = (0.4124 * lin[0] + 0.3576 * lin[1] + 0.1805 * lin[2]) / 0.95047
    Y = (0.2126 * lin[0] + 0.7152 * lin[1] + 0.0722 * lin[2])
    Z = (0.0193 * lin[0] + 0.1192 * lin[1] + 0.9505 * lin[2]) / 1.08883
    f = [t ** (1 / 3) if t > 0.008856 else 7.787 * t + 16 / 116 for t in (X, Y, Z)]
    return 116 * f[1] - 16, 500 * (f[0] - f[1]), 200 * (f[1] - f[2])


def de2000(h1, h2):
    L1, a1, b1 = srgb2lab(h1)
    L2, a2, b2 = srgb2lab(h2)
    C1, C2 = math.hypot(a1, b1), math.hypot(a2, b2)
    Cb = (C1 + C2) / 2
    G = 0.5 * (1 - math.sqrt(Cb ** 7 / (Cb ** 7 + 25 ** 7)))
    a1p, a2p = a1 * (1 + G), a2 * (1 + G)
    C1p, C2p = math.hypot(a1p, b1), math.hypot(a2p, b2)
    h1p = math.degrees(math.atan2(b1, a1p)) % 360
    h2p = math.degrees(math.atan2(b2, a2p)) % 360
    dL, dC = L2 - L1, C2p - C1p
    dh = h2p - h1p
    if C1p * C2p == 0:
        dh = 0
    elif dh > 180:
        dh -= 360
    elif dh < -180:
        dh += 360
    dH = 2 * math.sqrt(C1p * C2p) * math.sin(math.radians(dh / 2))
    Lb, Cbp = (L1 + L2) / 2, (C1p + C2p) / 2
    hb = (h1p + h2p) / 2 if abs(h1p - h2p) <= 180 else (h1p + h2p + 360) / 2
    if C1p * C2p == 0:
        hb = h1p + h2p
    T = (1 - 0.17 * math.cos(math.radians(hb - 30)) + 0.24 * math.cos(math.radians(2 * hb))
         + 0.32 * math.cos(math.radians(3 * hb + 6)) - 0.20 * math.cos(math.radians(4 * hb - 63)))
    SL = 1 + 0.015 * (Lb - 50) ** 2 / math.sqrt(20 + (Lb - 50) ** 2)
    SC, SH = 1 + 0.045 * Cbp, 1 + 0.015 * Cbp * T
    RT = (-2 * math.sqrt(Cbp ** 7 / (Cbp ** 7 + 25 ** 7))
          * math.sin(math.radians(60 * math.exp(-((hb - 275) / 25) ** 2))))
    return math.sqrt((dL / SL) ** 2 + (dC / SC) ** 2 + (dH / SH) ** 2 + RT * (dC / SC) * (dH / SH))


src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "builder_art", "bpal.py")).read()
P = {m.group(1): m.group(2) for m in re.finditer(r'"(\w+)":\s+\("(#[0-9A-Fa-f]{6})"', src)}
SW = [("Tool Brass (p05)", "#C79245", "brass"), ("Biomass (p05)", "#B7F238", "crystal"),
      ("Warm Cream (p05) vs cream", "#F7F0E5", "cream"), ("Stone (p05) vs knee pads", "#6F7282", "stone"),
      ("P08 navy #1B2D52 vs shirt", "#1B2D52", "navy"), ("P08 navy #1B2D52 vs trousers", "#1B2D52", "navy_dark"),
      ("P06 teal #2A95AE vs team cloth", "#2A95AE", "team"), ("P06 coral #E46753 vs Crimson cloth", "#E46753", "cloth"),
      ("P08 brown #AF723B vs leather", "#AF723B", "leather"), ("P08 cream #E9DECF vs canvas apron", "#E9DECF", "canvas")]
sw = {}
for label, bible, key in SW:
    oh = P[key]
    sw[label] = {"bible": bible, "ours": oh, "dE2000": round(de2000(bible, oh), 2)}
checks["swatches"] = sw
checks["swatch_dE_max"] = max(v["dE2000"] for v in sw.values())

# bridge prop vs 14-bridge.png: k-means clusters of the bible crop's wood/rope pixels, nearest cluster per our entry
b = np.asarray(ref("14-bridge.png")).reshape(-1, 3).astype(float)
warm = (b[:, 0] > b[:, 2] + 25) & (b.sum(1) < 700)
pts = b[warm]
rs = np.random.RandomState(0)
cent = pts[rs.choice(len(pts), 8, replace=False)]
for _ in range(25):
    lab = np.argmin(((pts[:, None, :] - cent[None]) ** 2).sum(2), 1)
    cent = np.array([pts[lab == k].mean(0) if (lab == k).any() else cent[k] for k in range(len(cent))])
clusters = ["#%02X%02X%02X" % tuple(int(round(x)) for x in c_) for c_ in cent]
br = {}
for key in ("wood", "wood_b", "wood_dark", "rope"):
    best = min(clusters, key=lambda h: de2000(h, P[key]))
    br[key] = {"ours": P[key], "nearest_14_bridge_cluster": best, "dE2000": round(de2000(best, P[key]), 2)}
checks["bridge_vs_14_bridge"] = br
checks["bridge_dE_max"] = max(v["dE2000"] for v in br.values())
mj = os.path.join(R, "measure.json")
if os.path.exists(mj):
    checks["render_measure"] = json.load(open(mj))
with open(os.path.join(OUT, "checks.json"), "w") as f:
    json.dump(checks, f, indent=1)
print("TEAM", {k: v["team_pct"] for k, v in fac.items()})
print("OUTLINE", {k: v["outline_px_mean_peel"] for k, v in fac.items()})
print("BIBLE_IOU", ovb)
print("GAME64", {k: (v["worst"], v["mean"], v["tiles_found"]) for k, v in ov.items()})
print("SWATCH", {k: v["dE2000"] for k, v in sw.items()})
print("BRIDGE", {k: v["dE2000"] for k, v in br.items()}, "CRIMSON_CHANGED", checks["crimson_changed_pct_of_character"])
print("SIZES", {k: v["bytes"] for k, v in res.items()})
