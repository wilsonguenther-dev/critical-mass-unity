"""Miner render set + measurable checks (system python3 + Pillow + numpy, not Blender).

  python3 compare_miner.py <render_dir> <out_dir> [bible_ref_dir]

bible_ref_dir defaults to ~/critical-mass/loop/parity/ref (read-only). Writes into out_dir:
  gameplay-<team>.jpg, gameplay-2x-<team>.jpg, gameplay-barricade-<team>.jpg
  sbs-<framing>.jpg     bible crop LEFT, our render RIGHT (on the bible's paper), <= 1400 px wide, < 180 KB
  silhouette-64.jpg     8 facings as a Void Navy fill on Warm Cream, 64 px tall each
  checks.json           team-colour %, outline width, silhouette IoU (indicative), swatch dE2000, sizes
The bible mask is a "differs from paper" threshold (the crops include neighbouring art and are clipped at their
edges), so the IoU here is indicative, not a U-016 reviewed mask.
"""
import json
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

R = os.path.abspath(sys.argv[1])
OUT = os.path.abspath(sys.argv[2])
REF = sys.argv[3] if len(sys.argv) > 3 else os.path.expanduser("~/critical-mass/loop/parity/ref")
os.makedirs(OUT, exist_ok=True)
PAPER = (254, 248, 238)
NAVY = (0x18, 0x23, 0x3B)
CREAM = (0xF7, 0xF0, 0xE5)
BIBLE_ORDER = ["N", "NW", "W", "SW", "S", "SE", "E", "NE"]   # 09-miner-directions left to right (fig 3 faces left)
checks = {}
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import iou_gate  # noqa: E402  (v2: registered IoU against the clean bible masks)
GATE = iou_gate.run(R, os.path.join(OUT, "iou.json"), os.path.join(OUT, "iou-overlay.jpg"))
checks["iou"] = GATE
checks["iou_turnaround_min"] = min(GATE[k]["iou"] for k in iou_gate.TURNAROUND if k in GATE)


def font(sz):
    for f in ("/System/Library/Fonts/Supplemental/Arial.ttf", "/System/Library/Fonts/Helvetica.ttc"):
        try:
            return ImageFont.truetype(f, sz)
        except OSError:
            pass
    return ImageFont.load_default()


def save_jpg(im, path, cap=180_000, maxw=None):
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


def bible_mask(im):
    a = np.asarray(im.convert("RGB")).astype(int)
    paper = np.median(np.concatenate([a[:3].reshape(-1, 3), a[-3:].reshape(-1, 3)]), axis=0)
    return np.abs(a - paper).sum(2) > 45


def iou(bm, om):
    x0, y0, x1, y1 = bbox(bm)
    b = bm[y0:y1, x0:x1]
    X0, Y0, X1, Y1 = bbox(om)
    o = Image.fromarray((om[Y0:Y1, X0:X1] * 255).astype(np.uint8)).resize((x1 - x0, y1 - y0), Image.BILINEAR)
    o = np.asarray(o) > 127
    return float((b & o).sum() / max(1, (b | o).sum()))


def sbs(name, ref_imgs, ours, label_l, label_r, H=560, do_iou=True):
    """ref_imgs / ours: lists of (PIL RGB, mask or None). Concatenate each side, scale to H, bible left."""
    def strip(items):
        ims = [i for i, _ in items]
        hh = max(i.size[1] for i in ims)
        sc = [i.resize((max(1, round(i.size[0] * H / i.size[1])), H), Image.LANCZOS) for i in ims]
        w = sum(i.size[0] for i in sc) + 8 * (len(sc) - 1)
        out = Image.new("RGB", (w, H), PAPER)
        x = 0
        for i in sc:
            out.paste(i, (x, 0))
            x += i.size[0] + 8
        return out
    L, Rr = strip(ref_imgs), strip(ours)
    gap, top = 16, 30
    W = L.size[0] + Rr.size[0] + gap * 3
    canvas = Image.new("RGB", (W, H + top + gap), (255, 255, 255))
    canvas.paste(L, (gap, top))
    canvas.paste(Rr, (gap * 2 + L.size[0], top))
    d = ImageDraw.Draw(canvas)
    f = font(17)
    d.text((gap, 6), "bible: " + label_l, fill=(40, 40, 50), font=f)
    d.text((gap * 2 + L.size[0], 6), "ours: " + label_r, fill=(40, 40, 50), font=f)
    score = None
    if do_iou and name in GATE:
        score = GATE[name]["iou"]
        d.text((W - 220, 6), f"IoU (clean mask) {score:.2f}", fill=(40, 40, 50), font=f)
    path = os.path.join(OUT, f"sbs-{name}.jpg")
    size = save_jpg(canvas, path, maxw=1400)
    return {"file": os.path.basename(path), "bytes": size, "iou_indicative": score}


def ref(f):
    return Image.open(os.path.join(REF, f)).convert("RGB")


def rp(f):
    return os.path.join(R, f)


# ---------------------------------------------------------------- gameplay renders
for team in ("azure", "crimson"):
    for src, dst in ((f"gameplay_{team}.png", f"gameplay-{team}.jpg"), (f"gameplay2x_{team}.png", f"gameplay-2x-{team}.jpg"),
                     (f"gameplay_barricade_{team}.png", f"gameplay-barricade-{team}.jpg")):
        if os.path.exists(rp(src)):
            im = Image.open(rp(src)).convert("RGB")
            checks.setdefault("gameplay_files", {})[dst] = save_jpg(im, os.path.join(OUT, dst))

# ---------------------------------------------------------------- facings: team %, height, outline, silhouette
fac = {}
sil_tiles = []
for d in BIBLE_ORDER:
    rgba = np.asarray(Image.open(rp(f"facing_{d}.png")).convert("RGBA"))
    msk = np.asarray(Image.open(rp(f"facing_{d}_mask.png")).convert("RGBA"))
    alpha = rgba[..., 3] > 8
    team = (msk[..., 1] > 128) & alpha
    x0, y0, x1, y1 = bbox(alpha)
    # outline width: run length of ink-coloured pixels inward from the left silhouette edge, per row
    ink = (msk[..., 0] > 100) & (msk[..., 1] < 90) & (msk[..., 3] > 100)      # debug render: ink is pure red
    # mean outer-outline width: share of ink in each 1 px layer peeled from the silhouette edge, summed
    cur = alpha.copy()
    width = 0.0
    for _ in range(10):
        er = cur.copy()
        er[1:, :] &= cur[:-1, :]
        er[:-1, :] &= cur[1:, :]
        er[:, 1:] &= cur[:, :-1]
        er[:, :-1] &= cur[:, 1:]
        layer = cur & ~er
        width += (ink & layer).sum() / max(1, layer.sum())
        cur = er
    runs = []
    for y in range(y0, y1):
        row = np.nonzero(alpha[y])[0]
        if len(row) == 0:
            continue
        x = row[0]
        while x < rgba.shape[1] and not ink[y, x] and x < row[0] + 2:
            x += 1
        n = 0
        while x + n < rgba.shape[1] and ink[y, x + n]:
            n += 1
        if n:
            runs.append(n)
    fac[d] = {"team_pct": round(100 * team.sum() / alpha.sum(), 2), "silhouette_px_h": int(y1 - y0),
              "silhouette_px_w": int(x1 - x0), "outline_px_median": float(np.median(runs)) if runs else None,
              "outline_px_mean_peel": round(float(width), 2)}
    # 64 px Void Navy fill on Warm Cream
    a = Image.fromarray((alpha[y0:y1, x0:x1] * 255).astype(np.uint8))
    w64 = max(1, round(a.size[0] * 64 / a.size[1]))
    a = a.resize((w64, 64), Image.LANCZOS)
    tile = Image.new("RGB", (w64, 64), CREAM)
    tile.paste(Image.new("RGB", (w64, 64), NAVY), (0, 0), a)
    sil_tiles.append((d, tile))
checks["facings"] = fac
checks["team_pct_min"] = min(v["team_pct"] for v in fac.values())
pad = 18
W = sum(t.size[0] for _, t in sil_tiles) + pad * (len(sil_tiles) + 1)
sheet = Image.new("RGB", (W, 64 + 2 * pad + 18), CREAM)
d = ImageDraw.Draw(sheet)
x = pad
for nm, t in sil_tiles:
    sheet.paste(t, (x, pad))
    d.text((x + t.size[0] // 2 - 6, 64 + pad + 2), nm, fill=NAVY, font=font(13))
    x += t.size[0] + pad
big = sheet.resize((sheet.size[0] * 2, sheet.size[1] * 2), Image.NEAREST)   # 2x nearest so the 64 px read is visible
save_jpg(big, os.path.join(OUT, "silhouette-64.jpg"), maxw=1400)

# directions sheet side by side (bible order), at gameplay camera
ours = []
for dname in BIBLE_ORDER:
    im, m = on_paper(rp(f"facing_{dname}.png"))
    ours.append((im, m))
res = {}
res["directions"] = sbs("directions", [(ref("09-miner-directions.png"), None)], ours,
                        "09-miner-directions (N NW W SW S SE E NE)", "gameplay camera 55/35/23.4, same order",
                        H=300, do_iou=False)

# ---------------------------------------------------------------- bible framings (orthographic)
FR = [
    ("front", "08-front.png", "ortho_front_azure.png"),
    ("three-quarter", "08-three-quarter.png", "ortho_three-quarter_azure.png"),
    ("profile", "08-profile.png", "ortho_profile_azure.png"),
    ("back", "08-back.png", "ortho_back_azure.png"),
    ("accent-crimson", "08-accent-swap.png", "ortho_accent_crimson.png"),
    ("front-pick", "08-front-pick.png", "ortho_front-pick_azure.png"),
    ("barricade-folded", "08-barricade-folded.png", "ortho_barricade-folded_azure.png"),
    ("barricade-deployed", "08-barricade-deployed.png", "ortho_barricade-deployed_azure.png"),
    ("barricade-damaged", "08-barricade-damaged.png", "ortho_barricade-damaged_azure.png"),
    ("pose-barricade", "08-pose-barricade.png", "ortho_pose-barricade_azure.png"),
]
for name, rf, of in FR:
    if not os.path.exists(rp(of)):
        continue
    im, m = on_paper(rp(of))
    res[name] = sbs(name, [(ref(rf), None)], [(im, m)], rf, of)
    os.makedirs(os.path.join(OUT, "views"), exist_ok=True)
    save_jpg(im, os.path.join(OUT, "views", f"{name}.jpg"), maxw=900)
# pick-hammer: pick state -> hammer state
pk, pm = on_paper(rp("ortho_pick_azure.png"))
hm, hmm = on_paper(rp("ortho_hammer_azure.png"))
res["pick-hammer"] = sbs("pick-hammer", [(ref("08-pick-hammer.png"), None)], [(pk, pm), (hm, hmm)],
                         "08-pick-hammer (pick -> fold -> hammer)", "pick state | hammer state", H=420, do_iou=False)
# team swap pair
az, am = on_paper(rp("ortho_accent_azure.png"))
cr, cm = on_paper(rp("ortho_accent_crimson.png"))
res["team-swap"] = sbs("team-swap", [(ref("08-front.png"), None), (ref("08-accent-swap.png"), None)],
                       [(az, am), (cr, cm)], "08-front | 08-accent-swap", "Azure | Crimson", H=420, do_iou=False)
checks["sbs"] = res

# Crimson swap changes only team-masked pixels: compare azure vs crimson accent renders
a = np.asarray(Image.open(rp("ortho_accent_azure.png")).convert("RGBA")).astype(int)
c = np.asarray(Image.open(rp("ortho_accent_crimson.png")).convert("RGBA")).astype(int)
changed = (np.abs(a[..., :3] - c[..., :3]).sum(2) > 24) & (a[..., 3] > 200)
checks["crimson_changed_pct_of_character"] = round(100 * changed.sum() / (a[..., 3] > 200).sum(), 2)


# ---------------------------------------------------------------- swatches, dE2000 (bible P08 strip vs our palette)
def srgb2lab(h):
    h = h.lstrip("#")
    rgb = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    lin = [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in rgb]
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


import re  # noqa: E402
src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "miner_art", "mpal.py")).read()
P = {m.group(1): m.group(2) for m in re.finditer(r'"(\w+)":\s+\("(#[0-9A-Fa-f]{6})"', src)}
SW = [("navy (P08 #152A53 sampled / #1B2D52 printed)", "#1B2D52", "navy_dark"),
      ("navy shirt vs P08 #1B2D52", "#1B2D52", "navy"),
      ("cream #E9DECF", "#E9DECF", "cream"),
      ("stone grey #807470", "#807470", "stone"),
      ("blue #2A8ECB (crystal)", "#2A8ECB", "crystal"),
      ("blue #2A8ECB (neckerchief)", "#2A8ECB", "kerchief"),
      ("brown/brass #AF723B", "#AF723B", "brass"),
      ("red #EF674E (Crimson cloth)", "#EF674E", "cloth"),
      ("orange #FA743D (Crimson glow)", "#FA743D", "glow")]
sw = {}
for label, bible, ours_key in SW:
    oh = P[ours_key]
    sw[label] = {"bible": bible, "ours": oh, "dE2000": round(de2000(bible, oh), 2)}
checks["swatches"] = sw
checks["swatch_dE_max"] = max(v["dE2000"] for v in sw.values())
mj = os.path.join(R, "measure.json")
if os.path.exists(mj):
    checks["render_measure"] = json.load(open(mj))
with open(os.path.join(OUT, "checks.json"), "w") as f:
    json.dump(checks, f, indent=1)
print("TEAM", {k: v["team_pct"] for k, v in fac.items()})
print("OUTLINE", {k: v["outline_px_mean_peel"] for k, v in fac.items()})
print("IOU", {k: v.get("iou_indicative") for k, v in res.items()})
print("SIZES", {k: v["bytes"] for k, v in res.items()})
print("SWATCH_MAX", checks["swatch_dE_max"], "CRIMSON_CHANGED", checks["crimson_changed_pct_of_character"])
