"""Sheets and measured checks from render_props.py output (system python3 with Pillow + numpy).

  python3 compose_sheets.py <out_dir>

Writes <out_dir>/lineup.jpg, district.jpg, field.jpg (labelled, counts on the sheet), sbs-<framing>.jpg (bible LEFT,
ours RIGHT, each under 180 KB and 1400 px wide) and <out_dir>/checks.json (IoU, dE2000 swatches, outline px).
"""
import io
import json
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from props_art.bible import fg_mask  # noqa: E402

OUT = os.path.abspath(sys.argv[1])
RD = os.path.join(OUT, "renders")
BIBLE = os.path.expanduser("~/critical-mass/loop/parity/ref")
NAVY = np.array([0x18, 0x23, 0x3B])


def font(sz):
    for p in ("/System/Library/Fonts/Supplemental/Arial Bold.ttf", "/System/Library/Fonts/Supplemental/Arial.ttf",
              "/Library/Fonts/Arial.ttf"):
        if os.path.exists(p):
            return ImageFont.truetype(p, sz)
    return ImageFont.load_default()


def save_jpg(im, path, limit=180_000, max_w=None):
    if max_w and im.width > max_w:
        im = im.resize((max_w, round(im.height * max_w / im.width)), Image.LANCZOS)
    for q in (88, 82, 76, 70, 64, 58, 52, 46, 40):
        buf = io.BytesIO()
        im.convert("RGB").save(buf, "JPEG", quality=q, optimize=True)
        if buf.tell() < limit:
            break
    open(path, "wb").write(buf.getvalue())
    return buf.tell(), q


def label(d, xy, text, f, fill=(255, 255, 255), anchor="mt"):
    x, y = xy
    d.text((x, y), text, font=f, fill=fill, anchor=anchor, stroke_width=3, stroke_fill=(24, 35, 59))


# ---------------------------------------------------------------- CIEDE2000
def srgb_to_lab(rgb):
    c = np.asarray(rgb, dtype=float) / 255.0
    c = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    m = np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]])
    xyz = c @ m.T / np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > 216 / 24389, np.cbrt(xyz), (24389 / 27 * xyz + 16) / 116)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], -1)


def de2000(l1, l2):
    L1, a1, b1 = l1
    L2, a2, b2 = l2
    C1, C2 = np.hypot(a1, b1), np.hypot(a2, b2)
    Cb = (C1 + C2) / 2
    G = 0.5 * (1 - np.sqrt(Cb ** 7 / (Cb ** 7 + 25 ** 7)))
    a1p, a2p = a1 * (1 + G), a2 * (1 + G)
    C1p, C2p = np.hypot(a1p, b1), np.hypot(a2p, b2)
    h1p, h2p = np.degrees(np.arctan2(b1, a1p)) % 360, np.degrees(np.arctan2(b2, a2p)) % 360
    dLp, dCp = L2 - L1, C2p - C1p
    dh = h2p - h1p
    if C1p * C2p == 0:
        dh = 0
    elif dh > 180:
        dh -= 360
    elif dh < -180:
        dh += 360
    dHp = 2 * np.sqrt(C1p * C2p) * np.sin(np.radians(dh / 2))
    Lbp, Cbp = (L1 + L2) / 2, (C1p + C2p) / 2
    hs = h1p + h2p
    if C1p * C2p == 0:
        hbp = hs
    elif abs(h1p - h2p) <= 180:
        hbp = hs / 2
    else:
        hbp = (hs + 360) / 2 if hs < 360 else (hs - 360) / 2
    T = 1 - 0.17 * np.cos(np.radians(hbp - 30)) + 0.24 * np.cos(np.radians(2 * hbp)) \
        + 0.32 * np.cos(np.radians(3 * hbp + 6)) - 0.2 * np.cos(np.radians(4 * hbp - 63))
    dth = 30 * np.exp(-(((hbp - 275) / 25) ** 2))
    Rc = 2 * np.sqrt(Cbp ** 7 / (Cbp ** 7 + 25 ** 7))
    Sl = 1 + 0.015 * (Lbp - 50) ** 2 / np.sqrt(20 + (Lbp - 50) ** 2)
    Sc, Sh = 1 + 0.045 * Cbp, 1 + 0.015 * Cbp * T
    Rt = -np.sin(np.radians(2 * dth)) * Rc
    return float(np.sqrt((dLp / Sl) ** 2 + (dCp / Sc) ** 2 + (dHp / Sh) ** 2 + Rt * (dCp / Sc) * (dHp / Sh)))


def swatches(pixels, k=6):
    """Median-cut palette of foreground pixels (N x 3) -> list of (rgb, share), ink and white highlights dropped."""
    lab = srgb_to_lab(pixels)
    keep = (lab[:, 0] > 22) & (lab[:, 0] < 97)
    px = pixels[keep]
    if len(px) < 10:
        return []
    im = Image.fromarray(px.reshape(-1, 1, 3).astype(np.uint8)).quantize(k, method=Image.Quantize.MEDIANCUT)
    pal = np.array(im.getpalette()[:3 * k]).reshape(k, 3)
    idx = np.asarray(im).ravel()
    out = [(pal[i], (idx == i).mean()) for i in range(k) if (idx == i).any()]
    return sorted(out, key=lambda t: -t[1])


# ---------------------------------------------------------------- sheets
def lineup():
    im = Image.open(os.path.join(RD, "lineup.png")).convert("RGB")
    data = json.load(open(os.path.join(RD, "lineup.json")))
    d = ImageDraw.Draw(im)
    f = font(13)
    for it in data["items"]:
        nm = it["name"].replace("prop_", "")
        label(d, (it["bx"], it["by"] + 14), nm, f)
    w = data["warrior"]
    label(d, (w["bx"], w["by"] + 14), "Warrior 2.0 u", f, fill=(255, 230, 160))
    label(d, (16, 16), f"Critical Mass props v1: {len(data['items'])} props, gameplay camera (pitch 55, FOV 35, "
                       f"23.4 u, yaw 45). Centre character {data['centre_character_px']:.0f} px tall.",
          font(20), anchor="lt")
    size, q = save_jpg(im, os.path.join(OUT, "lineup.jpg"))
    # outline width: navy runs across the crate's silhouette on its mid row
    crate = [it for it in data["items"] if it["name"] == "prop_crate"][0]
    arr = np.asarray(Image.open(os.path.join(RD, "lineup.png")).convert("RGB")).astype(int)
    runs = []
    for y in range(int(crate["y0"] + 0.35 * (crate["y1"] - crate["y0"])), int(crate["y0"] + 0.65 * (crate["y1"] - crate["y0"]))):
        row = arr[y, int(crate["x0"]) - 6:int(crate["x1"]) + 6]
        navy = np.abs(row - NAVY).sum(1) < 75
        if navy.any():
            first = np.argmax(navy)
            n = 0
            while first + n < len(navy) and navy[first + n]:
                n += 1
            runs.append(n)
    return dict(lineup_jpg_bytes=size, centre_character_px=data["centre_character_px"],
                warrior_px=w["height_px"], warrior_raw_height_u=w["raw_height_u"],
                crate_left_outline_run_px=float(np.median(runs)) if runs else None,
                outline_m_at_crate=data["outline_m_crate"])


def density(name, title, band):
    im = Image.open(os.path.join(RD, name + ".png")).convert("RGB")
    data = json.load(open(os.path.join(RD, name + ".json")))
    d = ImageDraw.Draw(im)
    lines = [title, f"small props in frame: {data['small_props']} (band {band[0]} to {band[1]})"]
    if "warm_light_pools" in data:
        lines.append(f"warm light pools: {data['warm_light_pools']} (band 10 to 14); crystal lamps: {data['crystal_lamps']}; "
                     f"tall props on walkable cells: {len(data['tall_on_walkable'])}")
    if "trees_big_rocks" in data:
        lines.append(f"trees / big rocks: {data['trees_big_rocks']} (band 4 to 8); lights: {data['lights']} (band 0 to 2)")
    lines.append(", ".join(f"{k.replace('prop_', '')} {v}" for k, v in sorted(data["kinds"].items())))
    y = 14
    for i, t in enumerate(lines):
        label(d, (16, y), t, font(20 if i < len(lines) - 1 else 14), anchor="lt")
        y += 28
    size, q = save_jpg(im, os.path.join(OUT, name + ".jpg"))
    return dict(data, jpg_bytes=size)


def framing(key):
    meta = json.load(open(os.path.join(RD, key + ".json")))
    bib = Image.open(os.path.join(BIBLE, meta["crop"] + ".png")).convert("RGB")
    W, H = bib.size
    barr = np.asarray(bib).astype(int)
    bg = barr[2, 2]
    bmask = fg_mask(np.asarray(bib))
    ours = Image.open(os.path.join(RD, key + ".png")).convert("RGBA").resize((W, H), Image.LANCZOS)
    oarr = np.asarray(ours).astype(float)
    omask = oarr[..., 3] > 127
    iou = float((bmask & omask).sum() / max(1, (bmask | omask).sum()))
    comp = Image.new("RGB", (W, H), tuple(int(v) for v in bg))
    comp.paste(ours, (0, 0), ours)
    carr = np.asarray(comp).astype(int)
    bsw = swatches(barr[bmask])
    osw = swatches(carr[omask])
    pairs = []
    for rgb, share in bsw:
        if share < 0.08:
            continue
        lb = srgb_to_lab(rgb)
        best = min(((de2000(lb, srgb_to_lab(o)), o) for o, _ in osw), key=lambda t: t[0]) if osw else (99, rgb)
        pairs.append(dict(bible="#%02X%02X%02X" % tuple(rgb), share=round(float(share), 2),
                          ours="#%02X%02X%02X" % tuple(best[1]), de2000=round(best[0], 1)))
    # side by side, both scaled to the same height
    th = 520 if W / H < 2.2 else 300
    sb = bib.resize((round(W * th / H), th), Image.LANCZOS)
    so = comp.resize((round(W * th / H), th), Image.LANCZOS)
    pad = 16
    sheet = Image.new("RGB", (sb.width * 2 + pad * 3, th + 70), (247, 240, 229))
    sheet.paste(sb, (pad, 50))
    sheet.paste(so, (sb.width + 2 * pad, 50))
    d = ImageDraw.Draw(sheet)
    f = font(16)
    d.text((pad, 14), f"BIBLE {meta['crop']}", font=f, fill=(24, 35, 59))
    d.text((sb.width + 2 * pad, 14), f"OURS {key}   IoU {iou:.2f}", font=f, fill=(24, 35, 59))
    worst = max((p["de2000"] for p in pairs), default=0)
    main3 = max((p["de2000"] for p in pairs[:3]), default=0)
    d.text((pad, th + 54), "swatch dE2000 (bible->ours): " + ", ".join(f"{p['bible']}->{p['ours']} {p['de2000']}" for p in pairs),
           font=font(11), fill=(24, 35, 59))
    size, q = save_jpg(sheet, os.path.join(OUT, f"sbs-{key}.jpg"), max_w=1400)
    return dict(iou=round(iou, 3), swatches=pairs, worst_de2000=worst, main3_de2000=main3, jpg_bytes=size, sheet_w=min(1400, sheet.width))


def main():
    checks = {}
    if os.path.exists(os.path.join(RD, "lineup.png")):
        checks["lineup"] = lineup()
    for nm, title, band in (("district", "Base district dressing, gameplay camera", (45, 60)),
                            ("field", "Open field dressing, gameplay camera", (20, 30))):
        if os.path.exists(os.path.join(RD, nm + ".png")):
            checks[nm] = density(nm, title, band)
    checks["framings"] = {}
    for fn in sorted(os.listdir(RD)):
        if fn.startswith("p") and fn.endswith(".png"):
            checks["framings"][fn[:-4]] = framing(fn[:-4])
    json.dump(checks, open(os.path.join(OUT, "checks.json"), "w"), indent=1)
    for k, v in checks["framings"].items():
        print(f"{k:26s} IoU {v['iou']:.2f}  main3 dE {v['main3_de2000']:5.1f}  worst dE {v['worst_de2000']:5.1f}  {v['jpg_bytes'] // 1000} KB")
    for k in ("lineup", "district", "field"):
        if k in checks:
            print(k, {a: b for a, b in checks[k].items() if a != "kinds"})


main()
