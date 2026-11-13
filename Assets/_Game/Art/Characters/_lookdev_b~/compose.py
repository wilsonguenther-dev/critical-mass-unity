"""Compose the look-dev B review sheets (plain Python + Pillow). Bible on the LEFT, ours on the RIGHT.
  python3 -I compose.py <render_dir> <ref_dir> <v7_dir> <out_dir> <tag>
<tag> is printed on every sheet ("PIPELINE PROXY ..." or "GENERATED ...") so nobody mistakes one for the other.
Also measures the front silhouette IoU against the bible crop (alpha > 0.5, height-matched, feet aligned, best x slide).
"""
import json
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

R, REF, V7, OUT, TAG = sys.argv[1:6]
os.makedirs(OUT, exist_ok=True)
PAPER = (255, 248, 238)
try:
    FONT = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 15)
except OSError:
    FONT = ImageFont.load_default()


def on_paper(im):
    im = im.convert("RGBA")
    bg = Image.new("RGBA", im.size, PAPER + (255,))
    bg.alpha_composite(im)
    return bg.convert("RGB")


def bbox_crop(im):
    a = np.asarray(im.convert("RGBA"))[..., 3]
    ys, xs = np.nonzero(a > 10)
    return im.crop((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1))


def fit(im, w, h, up=True):
    s = min(w / im.width, h / im.height)
    if not up:
        s = min(s, 1.0)
    return im.resize((max(1, int(im.width * s)), max(1, int(im.height * s))), Image.LANCZOS)


def panel_sheet(items, cell_w, cell_h, path, top=30, gap=10, rows=1):
    cols = (len(items) + rows - 1) // rows
    W = cols * cell_w + (cols + 1) * gap
    H = rows * (cell_h + top) + gap * 2 + 18
    sh = Image.new("RGB", (W, H), PAPER)
    dr = ImageDraw.Draw(sh)
    for i, (label, im) in enumerate(items):
        r, c = divmod(i, cols)
        x0, y0 = gap + c * (cell_w + gap), gap + r * (cell_h + top)
        dr.text((x0 + 8, y0 + 4), label, fill=(30, 30, 30), font=FONT)
        if im is not None:
            t = fit(im, cell_w, cell_h - 6)
            sh.paste(t, (x0 + (cell_w - t.width) // 2, y0 + top + cell_h - t.height - 6))
    dr.text((gap + 8, H - 20), TAG, fill=(150, 60, 40), font=FONT)
    sh.save(path, quality=82, optimize=True)
    while os.path.getsize(path) > 180_000:
        q = 70
        sh.save(path, quality=q, optimize=True)
        if os.path.getsize(path) > 180_000:
            sh = sh.resize((int(sh.width * 0.9), int(sh.height * 0.9)), Image.LANCZOS)
    print("WROTE", os.path.basename(path), sh.size, os.path.getsize(path))


def ref(name):
    return Image.open(os.path.join(REF, name)).convert("RGB")


def ours(name):
    return on_paper(bbox_crop(Image.open(os.path.join(R, name))))


res = {}
# ---- silhouette IoU against the bible front (indicative; the bible crop's own mask from paper colour)
def bible_mask(im):
    a = np.asarray(im).astype(int)
    paper = np.median(np.concatenate([a[0], a[-1], a[:, 0], a[:, -1]]), 0)
    m = np.abs(a - paper).sum(2) > 40
    return m


def our_mask(path):
    return np.asarray(Image.open(path).convert("RGBA"))[..., 3] > 127


def iou_fit(bm, om):
    ys, xs = np.nonzero(bm)
    bm = bm[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    ys, xs = np.nonzero(om)
    om = om[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    s = bm.shape[0] / om.shape[0]
    om = np.asarray(Image.fromarray(om.astype(np.uint8) * 255).resize((max(1, int(om.shape[1] * s)), bm.shape[0]))) > 127
    W = max(bm.shape[1], om.shape[1]) + 40
    best = 0
    for dx in range(-20, 21):
        A = np.zeros((bm.shape[0], W), bool)
        B = np.zeros_like(A)
        A[:, 20:20 + bm.shape[1]] = bm
        o = 20 + (bm.shape[1] - om.shape[1]) // 2 + dx
        if o < 0 or o + om.shape[1] > W:
            continue
        B[:, o:o + om.shape[1]] = om
        best = max(best, (A & B).sum() / (A | B).sum())
    return round(float(best), 3)


for v in ("front", "three-quarter", "profile", "back"):
    p = os.path.join(R, f"turn_{v}_b.png")
    if os.path.exists(p):
        res[f"iou_{v}"] = iou_fit(bible_mask(ref(f"06-{v}.png")), our_mask(p))
print("IOU", res)

# ---- sbs-front (same layout as warrior-v7/sbs-front.jpg: 1028 x 572, labels on top)
def sbs(left, llab, right, rlab, path, W=1028, H=572):
    sh = Image.new("RGB", (W, H), PAPER)
    dr = ImageDraw.Draw(sh)
    half = W // 2
    for i, (im, lab) in enumerate(((left, llab), (right, rlab))):
        t = fit(im, half - 30, H - 70)
        sh.paste(t, (i * half + (half - t.width) // 2, H - 20 - t.height))
        dr.text((i * half + 18, 8), lab, fill=(30, 30, 30), font=FONT)
    dr.text((18, H - 18), TAG, fill=(150, 60, 40), font=FONT)
    sh.save(path, quality=84, optimize=True)
    print("WROTE", os.path.basename(path), os.path.getsize(path))


bf = ref("06-front.png")
sbs(bf, "bible p06: 06-front.png", ours("turn_front_b.png"),
    f"ours lookdev-B: front, 1.25 px outline (silhouette IoU {res.get('iou_front', 0):.2f})", os.path.join(OUT, "sbs-front.jpg"))
sbs(ours("turn_front_a.png"), "lookdev-B (a): soft toon ramp, no outline, rim light", ours("turn_front_b.png"),
    "lookdev-B (b): soft toon ramp, 1.25 px Void Navy outline", os.path.join(OUT, "look-a-vs-b.jpg"))
# face: bible head crop (top 40 % of the figure) beside our head render
bm = bible_mask(bf)
ys, xs = np.nonzero(bm)
h = ys.max() - ys.min()
head = bf.crop((max(0, xs.min() + int(0.18 * (xs.max() - xs.min()))), ys.min(),
                min(bf.width, xs.max() - int(0.22 * (xs.max() - xs.min()))), ys.min() + int(0.42 * h)))
sbs(head, "bible p06: head from 06-front.png", ours("face_b.png"), "ours lookdev-B: head, 1.25 px outline",
    os.path.join(OUT, "sbs-face.jpg"), W=1028, H=520)

# ---- three-way strip: bible | v7 | new at the same height
v7s = Image.open(os.path.join(V7, "sbs-front.jpg")).convert("RGB")
v7r = v7s.crop((v7s.width // 2, 30, v7s.width, v7s.height - 8))
a = np.asarray(v7r).astype(int)
m = np.abs(a - np.array(PAPER)).sum(2) > 40
m[:, :8] = False
ys, xs = np.nonzero(m)
v7r = v7r.crop((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1))
by_, bx_ = np.nonzero(bm)
bfc = bf.crop((bx_.min(), by_.min(), bx_.max() + 1, by_.max() + 1))
panel_sheet([("bible p06 front", bfc), ("version 7 (scripted)", v7r), ("look-dev B", ours("turn_front_b.png"))],
            330, 420, os.path.join(OUT, "strip-bible-v7-new.jpg"))

# ---- turnaround: bible row, ours row (outline), ours row (rim, no outline)
items = [(f"bible {v}", ref(f"06-{v}.png")) for v in ("front", "three-quarter", "profile", "back")]
items += [(f"B {v} (b outline)", ours(f"turn_{v}_b.png")) for v in ("front", "three-quarter", "profile", "back")]
items += [(f"B {v} (a rim)", ours(f"turn_{v}_a.png")) for v in ("front", "three-quarter", "profile", "back")]
panel_sheet(items, 250, 300, os.path.join(OUT, "turnaround.jpg"), rows=3)

# ---- gameplay camera: v7 crop beside ours at the same 1080p pixel scale, plus a 3x nearest-neighbour zoom
g7 = Image.open(os.path.join(V7, "game-azure-crop.jpg")).convert("RGB")
gb = Image.open(os.path.join(R, "game_b.png")).convert("RGB")
ga = Image.open(os.path.join(R, "game_a.png")).convert("RGB")
cw = 300
v7c = g7.crop((0, 0, min(g7.width, g7.height), g7.height))
sheet = Image.new("RGB", (3 * cw + 40 + 3 * cw + 40, 340), PAPER)
dr = ImageDraw.Draw(sheet)
xx = 10
for lab, im in (("version 7 (game crop, 1080p px)", v7c), ("B (b) outline, 1080p px", gb), ("B (a) rim, 1080p px", ga)):
    dr.text((xx, 6), lab, fill=(30, 30, 30), font=FONT)
    t = im if im.height <= 300 else im.crop((0, 0, im.width, 300))
    sheet.paste(t, (xx, 30))
    xx += t.width + 10
sheet = sheet.crop((0, 0, xx, 340))
sheet.save(os.path.join(OUT, "game-v7-vs-B.jpg"), quality=85)
print("WROTE game-v7-vs-B.jpg", sheet.size)

# ---- clip frames: rows = variant, columns = clip frames
files = sorted(f for f in os.listdir(R) if f.startswith("clip_"))
items = [(f.replace("clip_", "").replace(".png", "").replace("_", " "), ours(f)) for f in files]
rows = 2 if any("fused" in f for f in files) and any("parts" in f for f in files) else 1
panel_sheet(items, 210, 260, os.path.join(OUT, "clips.jpg"), rows=rows)
json.dump(res, open(os.path.join(OUT, "iou.json"), "w"), indent=1)
