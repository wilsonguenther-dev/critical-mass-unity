"""Numeric angle check for a three-quarter candidate: silhouette IoU against the accepted front and side (feet-aligned, centred)
and a mirror-asymmetry score. A front-with-turned-head scores high front IoU and low asymmetry.
usage: python3 anglecheck.py front.png side.png cand.png [cand...]"""
import sys
import numpy as np
from PIL import Image
from scipy import ndimage
def mask(p):
    a = np.asarray(Image.open(p).convert("RGB").resize((512, 512))).astype(int)
    bg = np.median(a[:8, :8].reshape(-1, 3), axis=0); m = np.abs(a - bg).sum(2) > 40
    m = ndimage.binary_fill_holes(ndimage.binary_opening(m, iterations=1))
    ys, xs = np.where(m); m = m[ys.min():ys.max() + 1]; xs_c = (xs.min() + xs.max()) // 2
    out = np.zeros((512, 512), bool); h = m.shape[0]; sl = m[:, max(0, xs_c - 256):xs_c + 256]
    out[512 - h:, :sl.shape[1]] = sl; return out
def iou(a, b): return (a & b).sum() / max(1, (a | b).sum())
f, s = mask(sys.argv[1]), mask(sys.argv[2])
for c in sys.argv[3:]:
    m = mask(c); asym = 1 - iou(m, m[:, ::-1])
    print(c.split("/")[-1], "IoU front %.2f side %.2f asym %.2f width/front %.2f" % (iou(m, f), iou(m, s), asym, m.any(0).sum() / f.any(0).sum()))
