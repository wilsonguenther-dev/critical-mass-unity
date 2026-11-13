"""Clean silhouette masks of the bible p07 crops (system python3 + numpy, Pillow, scipy).

  python3 bible_masks.py <ref_dir> <out_dir>     -> <out_dir>/mask-<crop>.npy (bool, crop resolution) + mask-<crop>.png

Why: the v1 compare took the crop's pixel (1, 1) as the paper colour. Several crops (profile, back, front-staff) have a
neighbouring view's line art in that corner, so the "differs from paper" mask covered almost the whole crop and the IoU
numbers for those framings measured nothing. Here: difference from the fixed bible paper colour, then only the
character is kept (largest connected component plus big or interior pieces; pieces of neighbouring views that touch the
crop border are dropped), and small enclosed holes (cream jacket and highlights, which are paper-coloured) are filled.
Large enclosed gaps (between the legs, arm and body) stay open.
"""
import os
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

PAPER = np.array((254, 247, 236), dtype=np.float32)
THRESH = 22.0
HOLE_MAX = 0.004          # holes smaller than this fraction of the crop area are filled
CROPS = ["front", "three-quarter", "profile", "back", "backpack-tank", "front-staff", "staff-views",
         "pose-harvest", "pose-root-seed"]


def clean_mask(path):
    rgb = np.asarray(Image.open(path).convert("RGB"), dtype=np.float32)
    diff = np.abs(rgb - PAPER).mean(axis=2)
    m = diff > THRESH
    m = ndimage.binary_closing(m, iterations=1)
    lab, n = ndimage.label(m)
    if n == 0:
        return m
    area = ndimage.sum(m, lab, range(1, n + 1))
    big = area.max()
    border = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))) - {0}
    keep = np.zeros(n + 1, bool)
    for i in range(1, n + 1):
        a = area[i - 1]
        if a == big or (i not in border and a >= 0.004 * big):
            keep[i] = True
    m = keep[lab]
    holes = ndimage.binary_fill_holes(m) & ~m
    hl, hn = ndimage.label(holes)
    if hn:
        ha = ndimage.sum(holes, hl, range(1, hn + 1))
        small = np.zeros(hn + 1, bool)
        small[1:] = ha < HOLE_MAX * m.size
        m = m | small[hl]
    return m


def main():
    ref, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    for c in CROPS:
        m = clean_mask(os.path.join(ref, f"07-{c}.png"))
        np.save(os.path.join(out, f"mask-{c}.npy"), m)
        Image.fromarray((m * 255).astype(np.uint8)).save(os.path.join(out, f"mask-{c}.png"))
        print(c, m.shape, round(float(m.mean()), 3))


if __name__ == "__main__":
    main()
