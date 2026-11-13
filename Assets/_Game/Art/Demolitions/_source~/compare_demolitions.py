"""Sheets and numbers for the Demolitions Expert (system python3 + Pillow + numpy; no Blender).

  python3 compare_demolitions.py <render_dir> <silhouette_dir> <out_dir> [bible_ref_dir]

Writes into <out_dir> (JPEG, each under 180 KB, at most 1400 px wide):
  turnaround.jpg               front, three-quarter, profile, back (for the artist's approval)
  sheet-poses.jpg              throw, plant-mine, accent swap (Crimson | Azure)
  sbs-lineup.jpg               bible 06 Warrior, 07 Farmer, 08 Miner fronts (LEFT) | ours at the same height (RIGHT)
  gameplay-*.jpg               gameplay camera renders, 2x crops, props at the gameplay camera
  facings-8.jpg                8 facings at the gameplay camera
  silhouette-64-classes.jpg    64 px Void Navy fills, 8 facings, Demolitions vs Warrior / Farmer / Miner
  props-states.jpg             mine buried / armed (+ owner ring) / triggered, TNT in flight, crater rim, explosion
  checks.json                  every number below
"""
import json
import math
import os
import re
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

R, SIL, OUT = (os.path.abspath(a) for a in sys.argv[1:4])
REF = sys.argv[4] if len(sys.argv) > 4 else os.path.expanduser("~/critical-mass/loop/parity/ref")
HERE = os.path.dirname(os.path.abspath(__file__))
os.makedirs(OUT, exist_ok=True)
PAPER = (254, 248, 238)
NAVY = (0x18, 0x23, 0x3B)
CREAM = (0xF7, 0xF0, 0xE5)
ORDER = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"]
CLASSES = ["Demolitions", "Warrior", "Farmer", "Miner"]
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
    return [im.size[0], os.path.getsize(path)]


def on_paper(path, bg=PAPER):
    im = Image.open(path).convert("RGBA")
    b = Image.new("RGBA", im.size, bg + (255,))
    b.alpha_composite(im)
    return b.convert("RGB")


def bbox(mask):
    ys, xs = np.nonzero(mask)
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1


def row(images, labels, H, pad=16, title=None, bg=PAPER):
    ims = [im.resize((max(1, round(im.size[0] * H / im.size[1])), H), Image.LANCZOS) for im in images]
    top = 34 if title else 6
    W = sum(i.size[0] for i in ims) + pad * (len(ims) + 1)
    sheet = Image.new("RGB", (W, H + top + 30), bg)
    d = ImageDraw.Draw(sheet)
    if title:
        d.text((pad, 8), title, fill=NAVY, font=font(18))
    x = pad
    for im, lb in zip(ims, labels):
        sheet.paste(im, (x, top))
        d.text((x + 4, top + H + 6), lb, fill=NAVY, font=font(15))
        x += im.size[0] + pad
    return sheet


def rp(f):
    return os.path.join(R, f)


# ---------------------------------------------------------------- turnaround and pose sheets
turn = [on_paper(rp(f"ortho_{n}_azure.png")) for n in ("front", "three-quarter", "profile", "back")]
checks.setdefault("files", {})["turnaround.jpg"] = save_jpg(
    row(turn, ["front", "three-quarter", "profile", "back"], 560,
        title="Demolitions Expert (DG-07, proposed, female) - turnaround for approval - Azure"),
    os.path.join(OUT, "turnaround.jpg"))
poses = [on_paper(rp("ortho_throw_azure.png")), on_paper(rp("ortho_plant_azure.png")),
         on_paper(rp("ortho_accent_crimson.png")), on_paper(rp("ortho_accent_azure.png"))]
checks["files"]["sheet-poses.jpg"] = save_jpg(
    row(poses, ["throw (KayKit Throw)", "plant mine (KayKit PickUp)", "accent: Crimson", "accent: Azure"], 520,
        title="Action poses and team accent swap (only team-masked parts change)"), os.path.join(OUT, "sheet-poses.jpg"))

# ---------------------------------------------------------------- line-up: bible class fronts LEFT, ours RIGHT
bible = [Image.open(os.path.join(REF, f)).convert("RGB") for f in ("06-front.png", "07-front.png", "08-front.png")]
H = 420
left = row(bible, ["bible 06 Warrior", "bible 07 Farmer", "bible 08 Miner"], H, bg=(255, 255, 255))
right = row([on_paper(rp("ortho_front_azure.png"))], ["ours: Demolitions (proposed)"], H)
lu = Image.new("RGB", (left.size[0] + right.size[0], max(left.size[1], right.size[1])), PAPER)
lu.paste(left, (0, 0))
lu.paste(right, (left.size[0], 0))
checks["files"]["sbs-lineup.jpg"] = save_jpg(lu, os.path.join(OUT, "sbs-lineup.jpg"))

# ---------------------------------------------------------------- gameplay renders
for src, dst in (("gameplay_azure.png", "gameplay-azure.jpg"), ("gameplay_crimson.png", "gameplay-crimson.jpg"),
                 ("gameplay2x_azure.png", "gameplay-2x-azure.jpg"), ("gameplay2x_crimson.png", "gameplay-2x-crimson.jpg"),
                 ("gameplay_props_azure.png", "gameplay-props-azure.jpg"),
                 ("gameplay_props_crimson.png", "gameplay-props-crimson.jpg")):
    im = Image.open(rp(src)).convert("RGB")
    checks["files"][dst] = save_jpg(im, os.path.join(OUT, dst), maxw=None)

# ---------------------------------------------------------------- facings: team share, height, outline
fac, tiles = {}, []
for d in ORDER:
    rgba = np.asarray(Image.open(rp(f"facing_{d}.png")).convert("RGBA"))
    msk = np.asarray(Image.open(rp(f"facing_{d}_mask.png")).convert("RGBA"))
    alpha = rgba[..., 3] > 8
    team = (msk[..., 1] > 128) & alpha
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
    x0, y0, x1, y1 = bbox(alpha)
    # spark pixels: warm cream / brass, bright (hazard; must not be team-masked)
    rgb = rgba[..., :3].astype(int)
    spark = alpha & (rgb[..., 0] > 225) & (rgb[..., 1] > 190) & (rgb[..., 2] > 110) & (rgb[..., 2] < 235)
    fac[d] = {"team_pct": round(100 * team.sum() / alpha.sum(), 2), "px_h": int(y1 - y0), "px_w": int(x1 - x0),
              "outline_px_peel": round(float(width), 2), "spark_px": int(spark.sum()),
              "spark_px_team_masked": int((spark & team).sum())}
    tiles.append(on_paper(rp(f"facing_{d}.png")))
checks["facings"] = fac
checks["team_pct_range"] = [min(v["team_pct"] for v in fac.values()), max(v["team_pct"] for v in fac.values())]
checks["files"]["facings-8.jpg"] = save_jpg(row(tiles, ORDER, 220, title="8 facings at the gameplay camera (Azure)"),
                                            os.path.join(OUT, "facings-8.jpg"))

# ---------------------------------------------------------------- silhouettes vs the other classes
def sil(c, d):
    return np.asarray(Image.open(os.path.join(SIL, f"sil_{c}_{d}.png")).convert("RGBA"))[..., 3] > 127


def norm64(m, H=64, W=64):
    x0, y0, x1, y1 = bbox(m)
    im = Image.fromarray((m[y0:y1, x0:x1] * 255).astype(np.uint8))
    w = max(1, round(im.size[0] * H / im.size[1]))
    im = im.resize((w, H), Image.LANCZOS)
    can = Image.new("L", (max(W, w), H), 0)
    can.paste(im, ((can.size[0] - w) // 2, 0))
    if can.size[0] > W:
        can = can.crop(((can.size[0] - W) // 2, 0, (can.size[0] - W) // 2 + W, H))
    return np.asarray(can) > 127


def iou(a, b):
    return float((a & b).sum() / max(1, (a | b).sum()))


ov = {}
for c in CLASSES[1:]:
    same, n64 = [], []
    for d in ORDER:
        a, b = sil("Demolitions", d), sil(c, d)
        same.append(iou(a, b))
        n64.append(iou(norm64(a, 64, 80), norm64(b, 64, 80)))
    ov[c] = {"iou_same_scale_per_facing": dict(zip(ORDER, [round(v, 3) for v in same])),
             "iou_same_scale_mean": round(float(np.mean(same)), 3), "iou_same_scale_max": round(max(same), 3),
             "iou_64px_height_normalised_mean": round(float(np.mean(n64)), 3),
             "iou_64px_height_normalised_max": round(max(n64), 3)}
# reference: how much do the three existing classes overlap each other (the bar they already live with)
base = {}
for a_, b_ in (("Warrior", "Farmer"), ("Warrior", "Miner"), ("Farmer", "Miner")):
    base[f"{a_}-{b_}"] = round(float(np.mean([iou(sil(a_, d), sil(b_, d)) for d in ORDER])), 3)
checks["silhouette_overlap_vs_classes"] = ov
checks["silhouette_overlap_existing_pairs_mean"] = base
heights = {c: int(np.mean([bbox(sil(c, d))[3] - bbox(sil(c, d))[1] for d in ORDER])) for c in CLASSES}
checks["silhouette_mean_px_height_1080p"] = heights
checks["idle_widths"] = json.load(open(os.path.join(SIL, "sil_widths.json")))
# 64 px sheet, one row per class
pad, rowh = 14, 64 + 26
sheet = Image.new("RGB", (110 + 8 * (80 + pad) + pad, pad + len(CLASSES) * rowh), CREAM)
dr = ImageDraw.Draw(sheet)
for r_, c in enumerate(CLASSES):
    y = pad + r_ * rowh
    dr.text((8, y + 24), c, fill=NAVY, font=font(14))
    for k, d in enumerate(ORDER):
        m = norm64(sil(c, d), 64, 80)
        tile = Image.new("RGB", (80, 64), CREAM)
        tile.paste(Image.new("RGB", (80, 64), NAVY), (0, 0), Image.fromarray((m * 255).astype(np.uint8)))
        x = 110 + pad + k * (80 + pad)
        sheet.paste(tile, (x, y))
        if r_ == 0:
            dr.text((x + 34, y + 66), d, fill=NAVY, font=font(11))
big = sheet.resize((sheet.size[0] * 3 // 2, sheet.size[1] * 3 // 2), Image.NEAREST)
checks["files"]["silhouette-64-classes.jpg"] = save_jpg(big, os.path.join(OUT, "silhouette-64-classes.jpg"))

# ---------------------------------------------------------------- props
pn = [("Mine_Buried", "mine: buried (loose dirt)"), ("Mine_Armed", "mine: armed + owner ring"),
      ("Mine_Triggered", "mine: triggered"), ("TNT_Flight", "TNT in flight"), ("CraterRim", "crater rim decal"),
      ("Explosion", "explosion key frame")]
checks["files"]["props-states.jpg"] = save_jpg(
    row([on_paper(rp(f"ortho_prop_{k}_azure.png")) for k, _ in pn], [l for _, l in pn], 300,
        title="Demolitions props, authored states (separate static meshes, DemolitionsProps.fbx)"),
    os.path.join(OUT, "props-states.jpg"))


# ---------------------------------------------------------------- swatches vs bible p05 (CIEDE2000)
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


src = open(os.path.join(HERE, "demo_art", "dpal.py")).read()
P = {m.group(1): m.group(2) for m in re.finditer(r'"(\w+)":\s+\("(#[0-9A-Fa-f]{6})"', src)}
SW = [("brass vs p05 Tool Brass", "brass", "#C79245"), ("brass_light vs Tool Brass", "brass_light", "#C79245"),
      ("vest vs p05 Stone", "vest", "#6F7282"), ("vest_seam vs p05 Stone", "vest_seam", "#6F7282"),
      ("spark_core vs p05 Warm Cream", "spark_core", "#F7F0E5"), ("boom_core vs Warm Cream", "boom_core", "#F7F0E5"),
      ("crater vs p05 Void Navy", "crater", "#18233B"), ("smoke vs p05 Stone", "smoke", "#6F7282"),
      ("navy_dark vs P08 navy #1B2D52 (as the Miner)", "navy_dark", "#1B2D52"),
      ("kerchief (Azure cloth) vs P08 blue #2A8ECB", "kerchief", "#2A8ECB")]
checks["swatches_de2000"] = {lb: round(de2000(P[k], ref), 2) for lb, k, ref in SW}
checks["tnt_red_vs_crimson_de2000"] = {"vs #FF5A5F": round(de2000(P["tnt"], "#FF5A5F"), 2),
                                       "vs P08 red #EF674E": round(de2000(P["tnt"], "#EF674E"), 2),
                                       "lips vs #FF5A5F": round(de2000(P["lips"], "#FF5A5F"), 2)}
with open(os.path.join(OUT, "checks.json"), "w") as f:
    json.dump(checks, f, indent=1)
print(json.dumps({k: v for k, v in checks.items() if k not in ("facings", "files")}, indent=None)[:3000])
print("team", checks["team_pct_range"], "files", {k: v for k, v in checks["files"].items()})
