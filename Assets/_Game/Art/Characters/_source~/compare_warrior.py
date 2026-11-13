"""Measure and compare Warrior renders against the bible crops (system python3 + Pillow + numpy + scipy).

  python3 compare_warrior.py bible  <render_dir> <out_dir> [--profile]   # IoU, swatches, sbs-<framing>.jpg
  python3 compare_warrior.py game   <render_dir> <out_dir>               # pixel height, sbs-gameplay*.jpg
  python3 compare_warrior.py facings <render_dir> <out_dir>              # team %, 8-facing + 64 px silhouette sheets

Bible crops: ~/critical-mass/loop/parity/ref (read-only). Our renders are composited on the bible's paper (#FEF8EE)
and BOTH silhouettes are extracted the same way: pixels differing from paper by more than 22 (max channel), holes
filled, specks under 2 % of the largest part dropped. IoU alignment: scale to the same bounding-box height, centre
on the bounding boxes (PARITY.md recommendation). `iou_v1` repeats the v1 method (luminance difference, no hole
fill) for continuity. Indicative until the U-016 reviewed masks exist.
"""
import json
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage as nd

MODE, RENDERS, OUT = sys.argv[1], sys.argv[2], sys.argv[3]
PROFILE = "--profile" in sys.argv
REF = os.path.expanduser("~/critical-mass/loop/parity/ref")
PAPER = np.array([254, 248, 238])
CREAM = (247, 240, 229)
NAVY = (24, 35, 59)

# framing -> (bible crop, crop box or None)
PAIRS = {
    "front": ("06-front.png", None),
    "three-quarter": ("06-three-quarter.png", None),
    "profile": ("06-profile.png", None),
    "back": ("06-back.png", None),
    "cleave": ("06-pose-cleave.png", None),
    "shield-charge": ("06-pose-shield-charge.png", None),
    "front-shield": ("06-front-shield.png", None),
    "crimson": ("06-accent-swap.png", (0, 0, 160, 144)),     # drop the page's label bar under the figure
}
SWATCHES = {
    "azure": {"navy": "#2A374D", "teal": "#2A95AE", "cream": "#F2E5D5", "rust brown": "#AB6844",
              "brass/tan": "#BB9358"},
    "crimson": {"maroon": "#652D2F", "coral": "#E46753", "cream": "#F2E5D5", "brown": "#7B5238",
                "brass": "#C99C64"},
}


def font(sz):
    for p in ("/System/Library/Fonts/Supplemental/Arial.ttf", "/System/Library/Fonts/Helvetica.ttc"):
        if os.path.exists(p):
            return ImageFont.truetype(p, sz)
    return ImageFont.load_default()


def on_paper(path, bg=tuple(PAPER)):
    im = Image.open(path).convert("RGBA")
    base = Image.new("RGBA", im.size, bg + (255,))
    base.alpha_composite(im)
    return base.convert("RGB")


def paper_mask(rgb, fill=True, v1=False):
    a = np.asarray(rgb).astype(int)
    if v1:
        m = np.abs(a - PAPER).mean(axis=2) * 1.0 > 22 / 1.0
    else:
        m = np.abs(a - PAPER).max(axis=2) > 22
    if fill:
        m = nd.binary_fill_holes(m)
        lab, n = nd.label(m)
        if n > 1:
            sz = nd.sum(m, lab, range(1, n + 1))
            m = np.isin(lab, [i + 1 for i, s in enumerate(sz) if s >= 0.02 * sz.max()])
    return m


def norm(m, h=256):
    ys, xs = np.nonzero(m)
    c = m[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    w = max(1, round(c.shape[1] * h / c.shape[0]))
    a = np.asarray(Image.fromarray((c * 255).astype(np.uint8)).resize((w, h), Image.NEAREST)) > 0
    cv = np.zeros((h, 2 * h), bool)
    if w > 2 * h:
        a, w = a[:, (w - 2 * h) // 2:(w - 2 * h) // 2 + 2 * h], 2 * h
    cv[:, (2 * h - w) // 2:(2 * h - w) // 2 + w] = a
    return cv


def iou(a, b):
    a, b = norm(a), norm(b)
    return float((a & b).sum() / max((a | b).sum(), 1))


def profile_diff(b, r, n=20):
    """Per-row left/right extent (in % of height, from each bbox's centre) bible vs ours."""
    out = []
    for m in (norm(b, 200), norm(r, 200)):
        rows = []
        for k in range(n):
            y = int((k + 0.5) * 200 / n)
            xs = np.nonzero(m[y])[0]
            rows.append((xs.min() - 200, xs.max() - 200) if len(xs) else (0, 0))
        out.append(rows)
    return " ".join(f"{k*5:02d}:{bl:+d}/{br:+d}|{rl:+d}/{rr:+d}" for k, ((bl, br), (rl, rr)) in enumerate(zip(*out)))


# ---------------------------------------------------------------------- colour (dE2000 against swatches)
def srgb_to_lab(rgb):
    c = np.asarray(rgb, float) / 255.0
    c = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    M = np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]])
    xyz = c @ M.T / np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > 0.008856, np.cbrt(xyz), 7.787 * xyz + 16 / 116)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], -1)


def de2000(l1, l2):
    L1, a1, b1 = l1[..., 0], l1[..., 1], l1[..., 2]
    L2, a2, b2 = l2[..., 0], l2[..., 1], l2[..., 2]
    C1, C2 = np.hypot(a1, b1), np.hypot(a2, b2)
    Cb = (C1 + C2) / 2
    G = 0.5 * (1 - np.sqrt(Cb ** 7 / (Cb ** 7 + 25 ** 7)))
    a1p, a2p = a1 * (1 + G), a2 * (1 + G)
    C1p, C2p = np.hypot(a1p, b1), np.hypot(a2p, b2)
    h1p, h2p = np.degrees(np.arctan2(b1, a1p)) % 360, np.degrees(np.arctan2(b2, a2p)) % 360
    dL, dC = L2 - L1, C2p - C1p
    dh = h2p - h1p
    dh = np.where(dh > 180, dh - 360, np.where(dh < -180, dh + 360, dh))
    dH = 2 * np.sqrt(C1p * C2p) * np.sin(np.radians(dh / 2))
    Lb, Cbp = (L1 + L2) / 2, (C1p + C2p) / 2
    hb = np.where(np.abs(h1p - h2p) > 180, (h1p + h2p + 360) / 2, (h1p + h2p) / 2)
    T = (1 - 0.17 * np.cos(np.radians(hb - 30)) + 0.24 * np.cos(np.radians(2 * hb))
         + 0.32 * np.cos(np.radians(3 * hb + 6)) - 0.20 * np.cos(np.radians(4 * hb - 63)))
    SL = 1 + 0.015 * (Lb - 50) ** 2 / np.sqrt(20 + (Lb - 50) ** 2)
    SC, SH = 1 + 0.045 * Cbp, 1 + 0.015 * Cbp * T
    RT = -2 * np.sqrt(Cbp ** 7 / (Cbp ** 7 + 25 ** 7)) * np.sin(np.radians(60 * np.exp(-((hb - 275) / 25) ** 2)))
    return np.sqrt((dL / SL) ** 2 + (dC / SC) ** 2 + (dH / SH) ** 2 + RT * (dC / SC) * (dH / SH))


def swatch_check(rgba_path, team):
    """Each swatch vs the closest colour cluster holding >= 0.5 % of the asset pixels (D-054 rule)."""
    im = Image.open(rgba_path).convert("RGBA")
    a = np.asarray(im)
    px = a[a[..., 3] > 250][:, :3]
    q = Image.fromarray(px.reshape(1, -1, 3).astype(np.uint8)).quantize(48, method=Image.Quantize.MEDIANCUT)
    pal = np.array(q.getpalette()[:48 * 3]).reshape(-1, 3)
    counts = np.bincount(np.asarray(q).ravel(), minlength=48)
    keep = pal[counts >= 0.005 * counts.sum()]
    lab = srgb_to_lab(keep)
    res = {}
    for name, hx in SWATCHES[team].items():
        s = srgb_to_lab(np.array([int(hx[i:i + 2], 16) for i in (1, 3, 5)]))
        d = de2000(np.broadcast_to(s, lab.shape), lab)
        i = int(np.argmin(d))
        res[name] = {"swatch": hx, "closest": "#%02X%02X%02X" % tuple(keep[i]), "dE2000": round(float(d[i]), 2)}
    return res


def fit_h(im, h):
    return im.resize((max(1, round(im.width * h / im.height)), h), Image.LANCZOS)


def sbs(left, right, title_l, title_r, path, h=520, max_w=1400):
    bl, rl = fit_h(left, h), fit_h(right, h)
    gap, top = 20, 40
    sheet = Image.new("RGB", (bl.width + rl.width + gap * 3, h + top + 12), tuple(PAPER))
    sheet.paste(bl, (gap, top))
    sheet.paste(rl, (gap * 2 + bl.width, top))
    d = ImageDraw.Draw(sheet)
    f = font(17)
    d.text((gap, 10), title_l, fill=NAVY, font=f)
    d.text((gap * 2 + bl.width, 10), title_r, fill=NAVY, font=f)
    if sheet.width > max_w:
        sheet = sheet.resize((max_w, round(sheet.height * max_w / sheet.width)), Image.LANCZOS)
    q = 88
    while True:
        sheet.save(path, "JPEG", quality=q, optimize=True)
        if os.path.getsize(path) < 180_000 or q < 40:
            break
        q -= 6
    return path


os.makedirs(OUT, exist_ok=True)
results = {}
if MODE == "bible":
    for name, (ref, box) in PAIRS.items():
        rp, pp = os.path.join(REF, ref), os.path.join(RENDERS, f"{name}.png")
        if not os.path.exists(pp):
            continue
        b = Image.open(rp).convert("RGB")
        if box:
            b = b.crop(box)
        r = on_paper(pp)
        if name == "front-shield":      # the bible panel is the right-hand part of the shield, cut by the page
            ys, xs = np.nonzero(paper_mask(r))
            hh = ys.max() - ys.min() + 1
            r = r.crop((max(0, xs.max() + 1 - round(hh * 0.533)), ys.min(), xs.max() + 1, ys.max() + 1))
        if name == "crimson":           # the bible accent-swap panel is cut by the page at the crown and both sides
            ys, xs = np.nonzero(paper_mask(r))
            fh = ys.max() - ys.min() + 1
            top = ys.min() + round(0.125 * fh)                 # crown of the hair (topknot above the cut)
            h = round((ys.max() + 1 - top) * 1.13)             # feet sit at 88 % of the panel height
            w = round(h * 160 / 144)
            head = np.nonzero(paper_mask(r)[top + 2])[0]
            cx = (head.min() + head.max()) // 2 + round(0.03 * w)
            big = Image.new("RGB", (r.width + 2 * w, r.height + 2 * h), tuple(PAPER))
            big.paste(r, (w, h))
            r = big.crop((w + cx - w // 2, h + top, w + cx - w // 2 + w, h + top + h))
        bm, rm = paper_mask(b), paper_mask(r)
        res = {"iou": round(iou(bm, rm), 3),
               "iou_v1": round(iou(paper_mask(b, False, True), paper_mask(r, False, True)), 3)}
        ys, xs = np.nonzero(bm)
        ys2, xs2 = np.nonzero(rm)
        res["aspect_bible"] = round((xs.max() - xs.min() + 1) / (ys.max() - ys.min() + 1), 3)
        res["aspect_ours"] = round((xs2.max() - xs2.min() + 1) / (ys2.max() - ys2.min() + 1), 3)
        team = "crimson" if name == "crimson" else "azure"
        if name in ("front", "crimson"):
            res["swatches"] = swatch_check(pp, team)
        results[name] = res
        print(f"{name:14s} IoU {res['iou']:.3f} (v1 method {res['iou_v1']:.3f})  aspect bible {res['aspect_bible']}"
              f" ours {res['aspect_ours']}")
        if PROFILE:
            print("   ", profile_diff(bm, rm))
        if "swatches" in res:
            print("    swatches", {k: v["dE2000"] for k, v in res["swatches"].items()})
        sbs(b, r, f"bible p06: {ref}", f"ours v5: {name}  (silhouette IoU {res['iou']:.2f})",
            os.path.join(OUT, f"sbs-{name}.jpg"))
    fp = os.path.join(RENDERS, "face.png")
    if os.path.exists(fp):          # v4: large face-only side by side (bible head from 06-front, upscaled)
        bh = Image.open(os.path.join(REF, "06-front.png")).convert("RGB").crop((100, 0, 232, 112))
        sbs(bh, on_paper(fp), "bible p06: 06-front.png (head, upscaled)", "ours v5: face (same light)",
            os.path.join(OUT, "sbs-face.jpg"), h=600)
    with open(os.path.join(OUT, "iou.json"), "w") as f:
        json.dump(results, f, indent=1)

elif MODE == "game":
    m = np.asarray(Image.open(os.path.join(RENDERS, "game_mask.png")).convert("RGBA"))[..., 3] > 127
    ys, xs = np.nonzero(m)
    res = {"pixel_height": int(ys.max() - ys.min() + 1), "pixel_width": int(xs.max() - xs.min() + 1),
           "bbox": [int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())]}
    print("gameplay camera: unit", res["pixel_height"], "px tall,", res["pixel_width"], "px wide, bbox", res["bbox"])
    x0, y0, x1, y1 = res["bbox"]
    cx, cy = (x0 + x1) // 2, (y0 + y1) // 2
    for team in ("azure", "crimson"):
        full = Image.open(os.path.join(RENDERS, f"game_{team}.png")).convert("RGB")
        full.save(os.path.join(OUT, f"game-{team}-1920x1080.jpg"), "JPEG", quality=80)
        crop = full.crop((cx - 240, cy - 135, cx + 240, cy + 135))     # 480x270 at native 1080p scale
        zoom = crop.crop((120, 67, 360, 202)).resize((480, 270), Image.NEAREST)   # 2x zoom
        sheet = Image.new("RGB", (980, 290), tuple(PAPER))
        sheet.paste(crop, (6, 14))
        sheet.paste(zoom, (494, 14))
        d = ImageDraw.Draw(sheet)
        d.text((8, 0), f"{team}: gameplay camera crop (1080p pixels)", fill=NAVY, font=font(12))
        d.text((496, 0), "2x zoom (nearest)", fill=NAVY, font=font(12))
        sheet.save(os.path.join(OUT, f"game-{team}-crop.jpg"), "JPEG", quality=85)
    results = res
    with open(os.path.join(OUT, "game.json"), "w") as f:
        json.dump(res, f, indent=1)

elif MODE == "facings":
    order = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"]
    cells, sils, team = [], [], {}
    for k in order:
        idm = np.asarray(Image.open(os.path.join(RENDERS, f"facing_{k}.id.png")).convert("RGBA")).astype(int)
        char = idm[..., 3] > 127
        tm = char & (idm[..., :3].min(axis=2) > 200)
        team[k] = round(float(tm.sum() / max(char.sum(), 1)), 4)
        ys, xs = np.nonzero(char)
        box = (xs.min() - 6, ys.min() - 6, xs.max() + 7, ys.max() + 7)
        cells.append(on_paper(os.path.join(RENDERS, f"facing_{k}.png")).crop(box))
        s = Image.fromarray(np.where(char[..., None], np.array(NAVY), np.array(CREAM)).astype(np.uint8)).crop(box)
        hh = ys.max() - ys.min() + 1
        sils.append(s.resize((max(1, round(s.width * 64 / hh)), round(s.height * 64 / hh)), Image.LANCZOS))
    print("team-colour share per facing:", team, "min", min(team.values()))
    H = max(c.height for c in cells)
    row = Image.new("RGB", (sum(c.width + 8 for c in cells) + 8, H + 22), tuple(PAPER))
    x = 8
    d = ImageDraw.Draw(row)
    for k, c in zip(order, cells):
        row.paste(c, (x, 20 + H - c.height))
        d.text((x, 2), f"{k} {team[k]*100:.1f}%", fill=NAVY, font=font(11))
        x += c.width + 8
    row.save(os.path.join(OUT, "facings-ours.png"))
    bible = Image.open(os.path.join(REF, "09-warrior-directions.png")).convert("RGB")
    sbs(bible, row, "bible p09: 09-warrior-directions.png", "ours v5: 8 facings at the gameplay camera, 1080p pixels",
        os.path.join(OUT, "sbs-directions.jpg"), h=260)
    SH = max(s.height for s in sils)
    sheet = Image.new("RGB", (sum(s.width + 16 for s in sils) + 16, SH + 30), CREAM)
    x = 16
    d = ImageDraw.Draw(sheet)
    for k, s in zip(order, sils):
        sheet.paste(s, (x, 24 + SH - s.height))
        d.text((x, 4), k, fill=NAVY, font=font(12))
        x += s.width + 16
    sheet.save(os.path.join(OUT, "silhouette-64px.png"))
    results = {"team_share": team}
    with open(os.path.join(OUT, "facings.json"), "w") as f:
        json.dump(results, f, indent=1)
