"""Finish the Kaiju render set and measure it (system Python 3 with Pillow, numpy, scipy; not Blender).

    python3 "Assets/_Game/Art/Kaiju/_source~/compose_kaiju.py" <render_dir> <bible_ref_dir> <out_dir>

* gameplay shots: adds the outer silhouette line (mask pass dilated 1 px on top of the 2.0 px hull = 3.0 px, Void
  Navy), writes <job>.jpg (1920x1080) and, for the adult shots, <job>-2x.jpg (a 2x nearest-neighbour crop).
* bible framings: sbs-<crop>.jpg, bible crop LEFT, ours RIGHT, on the bible cream, <= 1400 px wide, < 180 KB.
* metrics.json: glow and moss pixel share of the Kaiju at the gameplay camera, silhouette IoU per framing,
  dE2000 of the named bible swatches against the nearest colour cluster of our render.
"""
import glob
import json
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
from scipy.cluster.vq import kmeans2

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import kaiju_palette as PAL  # noqa: E402

REN, REF, OUT = sys.argv[1:4]
os.makedirs(OUT, exist_ok=True)
meta = json.load(open(os.path.join(REN, "meta.json"))) if os.path.exists(os.path.join(REN, "meta.json")) else {}
NAVY = tuple(int(PAL.VOID_NAVY[i:i + 2], 16) for i in (1, 3, 5))
CREAM = tuple(int(PAL.BACKGROUND[i:i + 2], 16) for i in (1, 3, 5))
metrics = {"gameplay": {}, "bible": {}, "swatches": {}}


def save_jpeg(im, path, cap_kb=178, max_w=None):
    if max_w and im.width > max_w:
        im = im.resize((max_w, round(im.height * max_w / im.width)), Image.LANCZOS)
    for q in (88, 82, 76, 70, 64, 58, 52, 46, 40):
        im.save(path, "JPEG", quality=q, optimize=True)
        if os.path.getsize(path) <= cap_kb * 1024:
            return q
    return q


def hsv(a):
    a = a.astype(float) / 255.0
    mx, mn = a.max(-1), a.min(-1)
    d = mx - mn + 1e-9
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    h = np.where(mx == r, (g - b) / d % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    return h, np.where(mx > 0, d / (mx + 1e-9), 0), mx


def hue_of(hexs):
    h, s, v = hsv(np.array([[[int(hexs[i:i + 2], 16) for i in (1, 3, 5)]]]))
    return float(h[0, 0])


def hue_dist(h, h0):
    d = np.abs(h - h0) % 360
    return np.minimum(d, 360 - d)


# ------------------------------------------------------------------------------------------------ CIEDE2000
def to_lab(rgb):
    c = np.asarray(rgb, float) / 255.0
    c = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    M = np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]])
    xyz = c @ M.T / np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > 0.008856, np.cbrt(xyz), 7.787 * xyz + 16 / 116)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], -1)


def de2000(l1, l2):
    L1, a1, b1 = l1
    L2, a2, b2 = l2
    C1, C2 = math.hypot(a1, b1), math.hypot(a2, b2)
    Cb = (C1 + C2) / 2
    G = 0.5 * (1 - math.sqrt(Cb ** 7 / (Cb ** 7 + 25 ** 7)))
    a1p, a2p = a1 * (1 + G), a2 * (1 + G)
    C1p, C2p = math.hypot(a1p, b1), math.hypot(a2p, b2)
    h1p = math.degrees(math.atan2(b1, a1p)) % 360
    h2p = math.degrees(math.atan2(b2, a2p)) % 360
    dLp, dCp = L2 - L1, C2p - C1p
    dh = h2p - h1p
    if C1p * C2p == 0:
        dh = 0
    elif dh > 180:
        dh -= 360
    elif dh < -180:
        dh += 360
    dHp = 2 * math.sqrt(C1p * C2p) * math.sin(math.radians(dh / 2))
    Lbp, Cbp = (L1 + L2) / 2, (C1p + C2p) / 2
    if C1p * C2p == 0:
        hbp = h1p + h2p
    elif abs(h1p - h2p) <= 180:
        hbp = (h1p + h2p) / 2
    else:
        hbp = (h1p + h2p + 360) / 2 if h1p + h2p < 360 else (h1p + h2p - 360) / 2
    T = (1 - 0.17 * math.cos(math.radians(hbp - 30)) + 0.24 * math.cos(math.radians(2 * hbp))
         + 0.32 * math.cos(math.radians(3 * hbp + 6)) - 0.20 * math.cos(math.radians(4 * hbp - 63)))
    dth = 30 * math.exp(-(((hbp - 275) / 25) ** 2))
    Rc = 2 * math.sqrt(Cbp ** 7 / (Cbp ** 7 + 25 ** 7))
    Sl = 1 + 0.015 * (Lbp - 50) ** 2 / math.sqrt(20 + (Lbp - 50) ** 2)
    Sc, Sh = 1 + 0.045 * Cbp, 1 + 0.015 * Cbp * T
    Rt = -math.sin(math.radians(2 * dth)) * Rc
    return math.sqrt((dLp / Sl) ** 2 + (dCp / Sc) ** 2 + (dHp / Sh) ** 2 + Rt * (dCp / Sc) * (dHp / Sh))


# ------------------------------------------------------------------------------------------------ gameplay
for jid, m in sorted(meta.items()):
    if m["kind"] != "gameplay" or "--sweep" in sys.argv:
        continue
    im = np.asarray(Image.open(os.path.join(REN, jid + ".png")).convert("RGB")).copy()
    mask = np.asarray(Image.open(os.path.join(REN, jid + "_mask.png")).convert("RGBA"))[..., 3] > 127
    ring = ndimage.binary_dilation(mask, structure=ndimage.generate_binary_structure(2, 1)) & ~mask
    im[ring] = NAVY
    out = Image.fromarray(im)
    save_jpeg(out, os.path.join(OUT, jid + ".jpg"))
    x0, y0, x1, y1 = [int(round(v)) for v in m["kaiju_box"]]
    cx, cy = (x0 + x1) // 2, (y0 + y1) // 2
    if "adult" in jid or jid in ("gp-roar", "gp-slam-windup"):
        bx0 = max(0, min(1920 - 960, cx - 480))
        by0 = max(0, min(1080 - 540, cy - 270))
        crop = out.crop((bx0, by0, bx0 + 960, by0 + 540)).resize((1920, 1080), Image.NEAREST)
        save_jpeg(crop, os.path.join(OUT, jid + "-2x.jpg"))
    # Kaiju pixels: mask inside the Kaiju's screen box, minus the warrior's box
    km = np.zeros_like(mask)
    km[max(0, y0 - 2):y1 + 3, max(0, x0 - 2):x1 + 3] = True
    wx0, wy0, wx1, wy1 = [int(round(v)) for v in m["warrior_box"]]
    km[max(0, wy0 - 4):wy1 + 5, max(0, wx0 - 4):wx1 + 5] = False
    km &= ndimage.binary_dilation(mask, iterations=1)
    h, s, v = hsv(im)
    alleg = "Azure" if "crimson" not in jid and "berserk" not in jid else ("Crimson" if "crimson" in jid else "Berserk")
    if jid == "gp-egg-hatchling":
        alleg = "Neutral"
    glow = (hue_dist(h, hue_of(PAL.GLOW[alleg])) < 22) & (s > 0.45) & (v > 0.40) & km
    moss = (hue_dist(h, hue_of(PAL.BASE["Moss"])) < 16) & (s > 0.45) & (v > 0.30) & km & ~glow
    metrics["gameplay"][jid] = {"kaiju_px": int(km.sum()), "glow_share": round(float(glow.sum() / max(1, km.sum())), 3),
                                "moss_share": round(float(moss.sum() / max(1, km.sum())), 3),
                                "kaiju_box_px": [x1 - x0, y1 - y0], "px_per_m": round(m["px_per_m"], 1)}
    print("GAME", jid, metrics["gameplay"][jid])


# ------------------------------------------------------------------------------------------------ bible framings
def bible_mask(ref):
    a = np.asarray(ref.convert("RGB")).astype(int)
    bg = np.median(np.concatenate([a[0], a[-1], a[:, 0], a[:, -1]]), axis=0)
    diff = np.abs(a - bg).max(2) > 28
    lab, n = ndimage.label(ndimage.binary_closing(diff, iterations=2))
    sizes = ndimage.sum(np.ones_like(lab), lab, range(1, n + 1))
    return ndimage.binary_fill_holes(lab == int(np.argmax(sizes)) + 1)


def bbox(m):
    ys, xs = np.nonzero(m)
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1


def iou_only(ref_p, our_p):
    bm = bible_mask(Image.open(ref_p))
    oa = np.asarray(Image.open(our_p).convert("RGBA"))[..., 3] > 127
    bx0, by0, bx1, by1 = bbox(bm)
    ox0, oy0, ox1, oy1 = bbox(oa)
    sc = (by1 - by0) / (oy1 - oy0)
    sub = Image.fromarray(oa[oy0:oy1, ox0:ox1].astype(np.uint8) * 255).resize(
        (max(1, round((ox1 - ox0) * sc)), by1 - by0), Image.NEAREST)
    sub = np.asarray(sub) > 127
    om = np.zeros(bm.shape, bool)
    px = (bx0 + bx1) // 2 - sub.shape[1] // 2
    ys, xs = np.nonzero(sub)
    ys2, xs2 = ys + by0, xs + px
    ok = (xs2 >= 0) & (xs2 < bm.shape[1]) & (ys2 >= 0) & (ys2 < bm.shape[0])
    om[ys2[ok], xs2[ok]] = True
    return float((om & bm).sum() / max(1, (om | bm).sum()))


# allowed azimuth ranges: the sweep may only fine-tune the view the bible page shows (front stays front)
AZ_RANGE = {"11-front": (-15, 15), "11-three-quarter": (15, 60), "11-profile": (60, 100), "11-rear": (150, 210),
            "10-azure-adult": (15, 60), "10-crimson-adult": (15, 60), "10-berserk-adult": (0, 75),
            "10-hatchling": (30, 75), "10-egg": (-30, 30), "11-roar": (0, 75), "11-slam-windup": (0, 75)}
if len(sys.argv) > 4 and sys.argv[4] == "--sweep":
    best = {}
    for f in sorted(glob.glob(os.path.join(REN, "sweep", "*__*.png"))):
        jid, az = os.path.basename(f)[:-4].split("__")
        lo, hi = AZ_RANGE.get(jid, (-360, 360))
        if not lo <= float(az) <= hi:
            continue
        v = iou_only(os.path.join(REF, jid + ".png"), f)
        if jid not in best or v > best[jid][1]:
            best[jid] = (float(az), v)
    for jid, (az, v) in sorted(best.items()):
        print("BEST %-18s az %5.0f IoU %.3f" % (jid, az, v))
    sys.exit(0)

for jid, m in sorted(meta.items()):
    if m["kind"] != "bible":
        continue
    ref_p = os.path.join(REF, jid + ".png")
    if not os.path.exists(ref_p):
        continue
    ref = Image.open(ref_p).convert("RGB")
    ours = Image.open(os.path.join(REN, jid + ".png")).convert("RGBA")
    oa = np.asarray(ours)[..., 3] > 127
    # our silhouette gets the same outer line as the gameplay shots
    ring = ndimage.binary_dilation(oa, structure=ndimage.generate_binary_structure(2, 1)) & ~oa
    arr = np.asarray(ours).copy()
    arr[ring] = (*NAVY, 255)
    oa = oa | ring
    ours = Image.fromarray(arr)
    bm = bible_mask(ref)
    bx0, by0, bx1, by1 = bbox(bm)
    ox0, oy0, ox1, oy1 = bbox(oa)
    sc = (by1 - by0) / (oy1 - oy0)
    ow, oh = max(1, round((ox1 - ox0) * sc)), by1 - by0
    oc = ours.crop((ox0, oy0, ox1, oy1)).resize((ow, oh), Image.LANCZOS)
    # place ours with the bible subject's bottom-centre
    canvas = Image.new("RGB", ref.size, CREAM)
    px = (bx0 + bx1) // 2 - ow // 2
    canvas.paste(oc, (px, by0), oc)
    om = np.zeros(bm.shape, bool)
    sub = np.asarray(oc)[..., 3] > 127
    ys, xs = np.nonzero(sub)
    ys2, xs2 = ys + by0, xs + px
    ok = (xs2 >= 0) & (xs2 < bm.shape[1]) & (ys2 >= 0) & (ys2 < bm.shape[0])
    om[ys2[ok], xs2[ok]] = True
    iou = float((om & bm).sum() / max(1, (om | bm).sum()))
    metrics["bible"][jid] = {"iou": round(iou, 3), "bible_wh": round((bx1 - bx0) / (by1 - by0), 3),
                             "ours_wh": round((ox1 - ox0) / (oy1 - oy0), 3)}
    # side by side, bible LEFT, ours RIGHT, labelled
    H = 640
    L = ref.resize((round(ref.width * H / ref.height), H), Image.LANCZOS)
    Rr = canvas.resize((round(canvas.width * H / canvas.height), H), Image.LANCZOS)
    sbs = Image.new("RGB", (L.width + Rr.width + 12, H + 22), (255, 255, 255))
    sbs.paste(L, (0, 22))
    sbs.paste(Rr, (L.width + 12, 22))
    d = ImageDraw.Draw(sbs)
    d.text((4, 4), "bible " + jid, fill=(0, 0, 0))
    d.text((L.width + 16, 4), "ours (Blender v1)  IoU %.2f" % iou, fill=(0, 0, 0))
    save_jpeg(sbs, os.path.join(OUT, "sbs-%s.jpg" % jid), max_w=1400)
    print("BIBLE", jid, metrics["bible"][jid])

# ------------------------------------------------------------------------------------------------ swatches
SWATCH = {"slate": "#415066", "light stone": "#ADA59B", "moss": "#B7D139", "belly": "#C9BFAA", "azure glow": "#0FECF0"}
p = os.path.join(REN, "11-front.png")
if os.path.exists(p):
    a = np.asarray(Image.open(p).convert("RGBA"))
    px = a[a[..., 3] > 200][:, :3].astype(float)
    np.random.seed(1)
    cen, lab = kmeans2(px, 12, minit="++", seed=1)
    labs = to_lab(cen)
    for name, hexs in SWATCH.items():
        ref = to_lab(np.array([int(hexs[i:i + 2], 16) for i in (1, 3, 5)]))
        ds = [de2000(ref, l) for l in labs]
        i = int(np.argmin(ds))
        metrics["swatches"][name] = {"bible": hexs, "ours": "#%02X%02X%02X" % tuple(cen[i].astype(int)),
                                     "dE2000": round(ds[i], 2)}
    print("SWATCH", metrics["swatches"])
json.dump(metrics, open(os.path.join(OUT, "metrics.json"), "w"), indent=1)
