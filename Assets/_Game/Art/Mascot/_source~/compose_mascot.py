"""Hatch: from the Blender renders to the files the game loads and the review sheets (plain Python: Pillow + numpy).

    python3 compose_mascot.py <render_dir> <repo_root> <report_dir>

* Portraits: the 512 px render (inner ink = inverted hull, 6 px) gets its outer silhouette line taken to 12 px
  (3 px at the 1x 128 px medallion, brief "Style") by dilating the alpha into a Void Navy backing; "sleepy" gets two
  cream Z letters first. Writes Assets/_Game/Resources/UI/Mascot/mascot-<stem>@4x.png (512) and @2x.png (256).
* Sheets into <report_dir>: turnaround.jpg, lineup.jpg, hud-read-sheet.png/.jpg (every expression on Void Navy at 128 px,
  64 px and as the 84 px chip), portraits-2x.jpg; measures.json (bounding boxes, edge contact, outline colour).
"""
import json
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

RD, REPO, REP = sys.argv[1:4]
OUTUI = os.path.join(REPO, "Assets/_Game/Resources/UI/Mascot")
os.makedirs(OUTUI, exist_ok=True)
os.makedirs(REP, exist_ok=True)
NAVY = (0x18, 0x23, 0x3B)
CREAM = (0xF7, 0xF0, 0xE5)
PAGE = (0xFE, 0xF7, 0xED)
SEAM = (0x4F, 0xDD, 0xC3)
EXPR = ["neutral", "happy", "wince", "point", "point-left", "surprised", "thinking", "laughing", "proud", "sleepy", "peek"]


def font(sz):
    try:
        return ImageFont.load_default(size=sz)
    except TypeError:
        return ImageFont.load_default()


def outline(im, px):
    """Outer ink: dilate alpha by px and put Void Navy behind (anti-aliased)."""
    a = im.getchannel("A")
    grow = a.filter(ImageFilter.MaxFilter(2 * px + 1)).filter(ImageFilter.GaussianBlur(0.6))
    back = Image.new("RGBA", im.size, NAVY + (0,))
    back.putalpha(grow)
    return Image.alpha_composite(back, im)


def zzz(im):
    d = ImageDraw.Draw(im)
    W = im.size[0]
    for cx, cy, s in ((0.76, 0.20, 0.07), (0.87, 0.09, 0.045)):
        x0, y0, x1, y1 = (cx - s) * W, (cy - s) * W, (cx + s) * W, (cy + s) * W
        w = max(3, int(s * W * 0.42))
        d.line([(x0, y0), (x1, y0), (x0, y1), (x1, y1)], fill=CREAM + (255,), width=w, joint="curve")
    return im


measures = {}
tiles2 = {}
for stem in EXPR:
    p = os.path.join(RD, "portrait-%s.png" % stem)
    if not os.path.exists(p):
        print("MISSING", stem)
        continue
    im = Image.open(p).convert("RGBA")
    if stem == "sleepy":
        im = zzz(im)
    im = outline(im, 6)
    im.save(os.path.join(OUTUI, "mascot-%s@4x.png" % stem), optimize=True)
    im2 = im.resize((256, 256), Image.LANCZOS)
    im2.save(os.path.join(OUTUI, "mascot-%s@2x.png" % stem), optimize=True)
    tiles2[stem] = im2
    A = np.asarray(im)[..., 3]
    ys, xs = np.nonzero(A > 128)
    rgb = np.asarray(im)[..., :3].astype(int)
    # outline colour: pixels just outside the original render's silhouette
    orig = np.asarray(Image.open(p).convert("RGBA"))[..., 3] > 128
    ring = (A > 250) & ~orig
    ring_rgb = rgb[ring].mean(axis=0).round().tolist() if ring.any() else None
    measures[stem] = {"bbox_512": [int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())],
                      "touches": {"left": bool(xs.min() == 0), "right": bool(xs.max() == 511), "top": bool(ys.min() == 0),
                                  "bottom": bool(ys.max() == 511)},
                      "coverage": round(float((A > 128).mean()), 3), "outline_rgb": ring_rgb}

# ---- HUD read sheet: Void Navy panel, each expression at 128 px, at 64 px and as the 84 px chip (68 px face)
cols, bw, bh = 6, 236, 262
rows = (len(tiles2) + cols - 1) // cols
sheet = Image.new("RGB", (cols * bw + 24, rows * bh + 56), NAVY)
d = ImageDraw.Draw(sheet)
d.text((16, 14), "Hatch on the HUD panel (Void Navy #18233B): 128 px medallion face | 64 px | 84 px chip with a 68 px face",
       fill=CREAM, font=font(17))
for i, stem in enumerate([s for s in EXPR if s in tiles2]):
    im2 = tiles2[stem]
    x0, y0 = 16 + (i % cols) * bw, 48 + (i // cols) * bh
    t128 = im2.resize((128, 128), Image.LANCZOS)
    sheet.paste(t128, (x0, y0), t128)
    t64 = im2.resize((64, 64), Image.LANCZOS)
    sheet.paste(t64, (x0 + 140, y0), t64)
    # chip: 84 px Void Navy disc with a 3 px egg-seam rim, 68 px face centred
    cx, cy = x0 + 140 + 42, y0 + 128 - 42 + 4
    d.ellipse([cx - 42, cy - 42, cx + 42, cy + 42], fill=NAVY, outline=SEAM, width=3)
    t68 = im2.resize((68, 68), Image.LANCZOS)
    sheet.paste(t68, (cx - 34, cy - 34), t68)
    d.text((x0, y0 + 140), stem, fill=CREAM, font=font(18))
sheet.save(os.path.join(REP, "hud-read-sheet.png"), optimize=True)
sm = sheet.copy()
sm.thumbnail((960, 960))
sm.save(os.path.join(REP, "hud-read-sheet.jpg"), quality=70)

# ---- all portraits at 2x on a mid grey for the artist
g = Image.new("RGB", (6 * 266 + 10, 2 * 290 + 10), (0x8C, 0x8C, 0x8C))
dg = ImageDraw.Draw(g)
for i, stem in enumerate([s for s in EXPR if s in tiles2]):
    x0, y0 = 10 + (i % 6) * 266, 10 + (i // 6) * 290
    g.paste(tiles2[stem], (x0, y0), tiles2[stem])
    dg.text((x0 + 4, y0 + 258), stem, fill=NAVY, font=font(18))
g.thumbnail((1400, 1400))
g.save(os.path.join(REP, "portraits-2x.jpg"), quality=72)

# ---- turnaround
names = [("front", 0), ("three-quarter", 45), ("side", 90), ("three-quarter back", 135), ("back", 180)]
tt = [(n, os.path.join(RD, "turn-%03d.png" % a)) for n, a in names if os.path.exists(os.path.join(RD, "turn-%03d.png" % a))]
if tt:
    w0, h0 = Image.open(tt[0][1]).size
    ts = Image.new("RGB", (w0 * len(tt), h0 + 70), PAGE)
    dt = ImageDraw.Draw(ts)
    dt.text((16, 12), "Hatch, the Kaiju hatchling mascot: turnaround, 1.20 m (fits in the 1.70 m egg), Void Navy ink",
            fill=NAVY, font=font(24))
    for i, (n, p) in enumerate(tt):
        im = outline(Image.open(p).convert("RGBA"), 1)
        ts.paste(im, (i * w0, 50), im)
        dt.text((i * w0 + 16, h0 + 40), n, fill=NAVY, font=font(20))
    ts.thumbnail((1400, 1400))
    ts.save(os.path.join(REP, "turnaround.jpg"), quality=80)

# ---- line-up
lp = os.path.join(RD, "lineup.png")
if os.path.exists(lp):
    meta = json.load(open(os.path.join(RD, "build-metrics.json")))["renders"].get("lineup", {})
    im = outline(Image.open(lp).convert("RGBA"), 1)
    page = Image.new("RGBA", im.size, PAGE + (255,))
    page = Image.alpha_composite(page, im).convert("RGB")
    dl = ImageDraw.Draw(page)
    dl.text((20, 16), "Family line-up: grey Warrior silhouette %.1f m | adult Kaiju %.2f m | Hatch %.2f m (adult = %.1fx Hatch)"
            % (meta.get("warrior_h", 0), meta.get("adult_h", 0), meta.get("hatch_h", 0), meta.get("ratio_adult_to_hatch", 0)),
            fill=NAVY, font=font(26))
    page.thumbnail((1400, 1400))
    page.save(os.path.join(REP, "lineup.jpg"), quality=78)

json.dump(measures, open(os.path.join(REP, "measures.json"), "w"), indent=1)
for k, v in measures.items():
    print(k, v["bbox_512"], {a: b for a, b in v["touches"].items() if b}, v["outline_rgb"])
