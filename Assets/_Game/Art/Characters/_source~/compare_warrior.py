"""Side-by-side sheets: bible crop (left) next to our render (right), per framing, plus one contact sheet.

  python3 compare_warrior.py <render_dir> <out_dir> [bible_ref_dir]

Needs Pillow (system python3, not Blender). bible_ref_dir defaults to the browser repo's committed crops
(~/critical-mass/loop/parity/ref, read-only). Renders are composited on the bible's warm-cream paper (#FEF8EE).
Also prints a silhouette IoU per framing (both masks scaled to the same bounding-box height and centred, the
alignment PARITY.md recommends). The bible mask here is a quick "differs from paper" threshold, NOT a U-016 reviewed
mask, so the IoU is indicative only.
"""
import json
import os
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFont

RENDERS, OUT = sys.argv[1], sys.argv[2]
REF = sys.argv[3] if len(sys.argv) > 3 else os.path.expanduser("~/critical-mass/loop/parity/ref")
PAPER = (254, 248, 238)
H = 520

# framing -> (bible crop file, optional crop box inside it, render file)
PAIRS = [
    ("front", "06-front.png", None, "front_azure.png"),
    ("three-quarter", "06-three-quarter.png", None, "three-quarter_azure.png"),
    ("side", "06-profile.png", None, "side_azure.png"),
    ("back", "06-back.png", None, "back_azure.png"),
    ("oblique", "09-warrior-directions.png", (288, 0, 384, 140), "oblique_azure.png"),
    ("crimson-front", "06-accent-swap.png", (0, 0, 160, 148), "three-quarter_crimson.png"),
    ("crimson-oblique", "06-accent-swap.png", (0, 0, 160, 148), "oblique_crimson.png"),
]


def font(sz):
    for p in ("/System/Library/Fonts/Supplemental/Arial.ttf", "/System/Library/Fonts/Helvetica.ttc"):
        if os.path.exists(p):
            return ImageFont.truetype(p, sz)
    return ImageFont.load_default()


def on_paper(path):
    im = Image.open(path).convert("RGBA")
    bg = Image.new("RGBA", im.size, PAPER + (255,))
    bg.alpha_composite(im)
    return bg.convert("RGB"), im.split()[3].point(lambda a: 255 if a > 127 else 0)


def bible_mask(im):
    diff = ImageChops.difference(im.convert("RGB"), Image.new("RGB", im.size, PAPER)).convert("L")
    return diff.point(lambda v: 255 if v > 22 else 0)


def fit_h(im, h):
    return im.resize((max(1, round(im.width * h / im.height)), h), Image.LANCZOS)


def norm_mask(m, h=256):
    bb = m.getbbox()
    m = m.crop(bb)
    m = m.resize((max(1, round(m.width * h / m.height)), h), Image.NEAREST)
    canvas = Image.new("L", (h * 2, h), 0)
    canvas.paste(m, ((h * 2 - m.width) // 2, 0))
    return canvas


def iou(a, b):
    a, b = norm_mask(a), norm_mask(b)
    pa, pb = a.load(), b.load()
    inter = union = 0
    for y in range(a.height):
        for x in range(a.width):
            u, v = pa[x, y] > 0, pb[x, y] > 0
            inter += u and v
            union += u or v
    return inter / max(union, 1)


os.makedirs(OUT, exist_ok=True)
f_big, f_small = font(26), font(18)
sheets, results = [], {}
for name, ref, box, ren in PAIRS:
    rp, pp = os.path.join(REF, ref), os.path.join(RENDERS, ren)
    if not (os.path.exists(rp) and os.path.exists(pp)):
        print("SKIP", name, rp if not os.path.exists(rp) else pp)
        continue
    b = Image.open(rp).convert("RGB")
    if box:
        b = b.crop(box)
    r, rmask = on_paper(pp)
    score = iou(bible_mask(b), rmask)
    results[name] = round(score, 3)
    bl, rl = fit_h(b, H), fit_h(r, H)
    gap, top = 24, 44
    sheet = Image.new("RGB", (bl.width + rl.width + gap * 3, H + top + 16), PAPER)
    sheet.paste(bl, (gap, top))
    sheet.paste(rl, (gap * 2 + bl.width, top))
    d = ImageDraw.Draw(sheet)
    d.text((gap, 10), f"bible: {ref}", fill=(40, 40, 50), font=f_small)
    d.text((gap * 2 + bl.width, 10), f"ours: {ren}  (silhouette IoU {score:.2f}, indicative)", fill=(40, 40, 50),
           font=f_small)
    out = os.path.join(OUT, f"compare-{name}.png")
    sheet.save(out)
    sheets.append((name, sheet))
    print("SHEET", out, "IoU", round(score, 3))

# contact sheet: comparisons in two columns, then a row with every render (both teams)
renders = sorted(f for f in os.listdir(RENDERS) if f.endswith(".png"))
row = [fit_h(on_paper(os.path.join(RENDERS, f))[0], 300) for f in renders]
row_w = sum(i.width for i in row) + 12 * (len(row) + 1)
cols = [sheets[0::2], sheets[1::2]]
col_w = [max([s.width for _, s in c] + [0]) for c in cols]
col_h = [sum(s.height + 10 for _, s in c) for c in cols]
width = max(sum(col_w) + 10, row_w)
height = 70 + max(col_h) + 340
cs = Image.new("RGB", (width, height), PAPER)
d = ImageDraw.Draw(cs)
d.text((24, 18), "Critical Mass Warrior v1 (scripted Blender, KayKit 41-joint rig, CM/Toon preview) vs asset bible p06/p09",
       fill=(24, 35, 59), font=f_big)
x0 = 0
for c, w in zip(cols, col_w):
    y = 70
    for name, s in c:
        cs.paste(s, (x0, y))
        y += s.height + 10
    x0 += w + 10
y = 70 + max(col_h)
x = 12
for im in row:
    cs.paste(im, (x, y + 20))
    x += im.width + 12
cs.save(os.path.join(OUT, "contact-sheet.png"))
with open(os.path.join(OUT, "iou.json"), "w") as f:
    json.dump(results, f, indent=1)
print("CONTACT", os.path.join(OUT, "contact-sheet.png"), json.dumps(results))
