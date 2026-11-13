"""Figure measurements on generated views (plain light grey background). usage: python3 measure.py img [img...]
Prints figure bbox height/width in px, top and bottom rows, and the background grey sampled at the corners."""
import sys
import numpy as np
from PIL import Image
from scipy import ndimage
def fig(path):
    a = np.asarray(Image.open(path).convert("RGB")).astype(int)
    bg = np.median(np.concatenate([a[:20, :20].reshape(-1, 3), a[:20, -20:].reshape(-1, 3)]), axis=0)
    m = np.abs(a - bg).sum(2) > 40
    m = ndimage.binary_opening(m, iterations=2)
    lab, n = ndimage.label(m)
    if not n: return None
    sizes = ndimage.sum(m, lab, range(1, n + 1)); keep = np.isin(lab, 1 + np.where(sizes > sizes.max() * 0.02)[0])
    ys, xs = np.where(keep)
    return dict(h=int(ys.max() - ys.min()), w=int(xs.max() - xs.min()), top=int(ys.min()), bottom=int(ys.max()),
                cx=int((xs.max() + xs.min()) / 2), W=a.shape[1], H=a.shape[0], bg=[int(x) for x in bg])
if __name__ == "__main__":
    for p in sys.argv[1:]:
        print(p.split("/")[-1], fig(p))
