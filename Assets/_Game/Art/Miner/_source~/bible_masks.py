"""Clean silhouette masks of the p08 bible crops (system python3 + numpy + scipy + Pillow, not Blender).

  python3 bible_masks.py [bible_ref_dir] [preview.jpg]

v1 used a raw "differs from paper" threshold, which kept the ground shadow, neighbouring art clipped into the crop,
next-row fragments and pin-holes from white highlights. Here, per crop:
  1. foreground = colour distance from the paper (median of the crop border) > 40;
  2. the ground shadow and paper-grey haze are dropped: low-chroma light pixels (max-min < 20, mean > 150) are paper;
  3. parts are grouped by a 2 px dilation; only the main figure group is kept (multi-object crops keep every group at
     least KEEP x the largest, so the three pick states / folded stages survive);
  4. holes are filled unless they are large AND paper-coloured (real gaps such as between the pick shaft and the arm);
  5. a 1 px open removes single-pixel spurs.
Writes bible_masks/<crop>.png (white = figure) next to this script. These are the masks the IoU gate uses.
"""
import os
import sys

import numpy as np
from PIL import Image
from scipy import ndimage as nd

HERE = os.path.dirname(os.path.abspath(__file__))
REF = sys.argv[1] if len(sys.argv) > 1 else os.path.expanduser("~/critical-mass/loop/parity/ref")
PREVIEW = sys.argv[2] if len(sys.argv) > 2 else None
OUT = os.path.join(HERE, "bible_masks")
os.makedirs(OUT, exist_ok=True)

# crop: (keep groups >= KEEP x largest, extra rows to cut at the bottom (next-row art), extra cols cut at the right)
CROPS = {
    "08-front.png": (1.0, 0, 0), "08-three-quarter.png": (1.0, 0, 0), "08-profile.png": (1.0, 0, 0),
    "08-back.png": (1.0, 0, 0), "08-front-pick.png": (1.0, 0, 0), "08-accent-swap.png": (1.0, 0, 0),
    "08-pose-barricade.png": (1.0, 0, 0), "08-barricade-deployed.png": (1.0, 0, 0),
    "08-barricade-damaged.png": (1.0, 0, 0), "08-barricade-folded.png": (0.3, 0, 0),
    "08-pick-hammer.png": (0.3, 0, 0),
}


def clean(path, keep, cut_b, cut_r):
    a = np.asarray(Image.open(path).convert("RGB")).astype(int)
    border = np.concatenate([a[0], a[-1], a[:, 0], a[:, -1]])
    paper = np.median(border, axis=0)
    dist = np.abs(a - paper).sum(2)
    chroma = a.max(2) - a.min(2)
    fg = (dist > 40) & ~((chroma < 20) & (a.mean(2) > 150))
    if cut_b:
        fg[-cut_b:] = False
    if cut_r:
        fg[:, -cut_r:] = False
    grp, n = nd.label(nd.binary_dilation(fg, iterations=2))
    sizes = nd.sum(fg, grp, range(1, n + 1))
    keep_ids = [i + 1 for i, s in enumerate(sizes) if s >= keep * sizes.max() - 1e-6]
    m = fg & np.isin(grp, keep_ids)
    # holes: fill unless large and paper-coloured
    holes, nh = nd.label(nd.binary_fill_holes(m) & ~m)
    area = m.sum()
    for h in range(1, nh + 1):
        hm = holes == h
        big = hm.sum() > 0.004 * area
        paperish = np.median(dist[hm]) < 30
        if not (big and paperish):
            m |= hm
    m = nd.binary_opening(m, iterations=1) | (m & nd.binary_erosion(m, iterations=1))
    # drop anything the open detached
    lab, n = nd.label(m)
    if n > 1:
        s = nd.sum(m, lab, range(1, n + 1))
        m = np.isin(lab, [i + 1 for i, v in enumerate(s) if v >= min(keep, 1.0) * 0.3 * s.max() or v > 400])
    return a.astype(np.uint8), m


def main():
    tiles = []
    stats = {}
    for f, (keep, cb, cr) in CROPS.items():
        rgb, m = clean(os.path.join(REF, f), keep, cb, cr)
        name = f[:-4]
        Image.fromarray((m * 255).astype(np.uint8)).save(os.path.join(OUT, name + ".png"))
        ys, xs = np.nonzero(m)
        stats[name] = {"px": int(m.sum()), "bbox": [int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1],
                       "size": [rgb.shape[1], rgb.shape[0]]}
        if PREVIEW:
            ov = rgb.copy()
            ov[~m] = (ov[~m] * 0.25 + np.array([255, 0, 255]) * 0.75).astype(np.uint8)
            tiles.append(Image.fromarray(ov))
    if PREVIEW:
        H = 230
        rows, row, w = [], [], 0
        for t in tiles:
            t = t.resize((max(1, round(t.size[0] * H / t.size[1])), H))
            if w + t.size[0] > 1900 and row:
                rows.append(row)
                row, w = [], 0
            row.append(t)
            w += t.size[0] + 6
        rows.append(row)
        W = max(sum(t.size[0] + 6 for t in r) for r in rows)
        sheet = Image.new("RGB", (W, len(rows) * (H + 6)), (255, 255, 255))
        for ri, r in enumerate(rows):
            x = 0
            for t in r:
                sheet.paste(t, (x, ri * (H + 6)))
                x += t.size[0] + 6
        sheet.thumbnail((960, 960))
        sheet.save(PREVIEW, "JPEG", quality=60)
    for k, v in stats.items():
        print("MASK", k, v)


if __name__ == "__main__":
    main()
