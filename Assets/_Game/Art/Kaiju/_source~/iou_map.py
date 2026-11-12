"""Silhouette diagnostics without looking at pictures (system Python 3 with Pillow, numpy, scipy).

    python3 "Assets/_Game/Art/Kaiju/_source~/iou_map.py" <render_dir> <bible_ref_dir> [crop ...] [--map]

For each bible framing: the IoU exactly as compose_kaiju.py measures it (our silhouette scaled to the bible subject's
height, bottom-centred on it), the width/height ratios, and with --map an ASCII diff on a 48 x 24 grid:
'#' both, 'B' bible only (we are missing mass there), 'o' ours only (we have extra mass there), '.' neither.
Rows run top to bottom, columns left to right, as on the page.
"""
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
_argv = list(sys.argv)
REN, REF = _argv[1], _argv[2]
ONLY = [a for a in _argv[3:] if not a.startswith("--")]
MAP = "--map" in _argv
src = open(os.path.join(HERE, "compose_kaiju.py")).read()
ns = {"__file__": os.path.join(HERE, "compose_kaiju.py")}
sys.argv = [_argv[0], REN, REF, os.path.join(REN, "_unused")]
exec(compile(src.split("# ------------------------------------------------------------------------------------------------ gameplay")[0],
             "compose_helpers", "exec"), ns)
exec(compile("def main_component(m):" + src.split("def main_component(m):")[1].split("# allowed azimuth ranges")[0],
             "compose_masks", "exec"), ns)


def masks(jid, path=None):
    bm = ns["bible_mask"](Image.open(os.path.join(REF, jid + ".png")))
    ours = Image.open(path or os.path.join(REN, jid + ".png")).convert("RGBA")
    from scipy import ndimage
    oa = np.asarray(ours)[..., 3] > 127
    oa = ns["main_component"](ndimage.binary_dilation(oa, structure=ndimage.generate_binary_structure(2, 1)))
    bx0, by0, bx1, by1 = ns["bbox"](bm)
    ox0, oy0, ox1, oy1 = ns["bbox"](oa)
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
    return bm, om, (bx0, by0, bx1, by1), (ox1 - ox0) / (oy1 - oy0)


def report(jid):
    bm, om, (bx0, by0, bx1, by1), our_wh = masks(jid)
    iou = (om & bm).sum() / max(1, (om | bm).sum())
    miss = (bm & ~om).sum() / max(1, bm.sum())
    extra = (om & ~bm).sum() / max(1, bm.sum())
    print("%-18s IoU %.3f  bible w/h %.3f  ours w/h %.3f  missing %.3f  extra %.3f"
          % (jid, iou, (bx1 - bx0) / (by1 - by0), our_wh, miss, extra))
    if MAP:
        ys, xs = np.nonzero(bm | om)
        y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
        W, H = 48, 24
        for r in range(H):
            line = ""
            for c in range(W):
                sl = (slice(y0 + (y1 - y0) * r // H, y0 + (y1 - y0) * (r + 1) // H),
                      slice(x0 + (x1 - x0) * c // W, x0 + (x1 - x0) * (c + 1) // W))
                b, o = bm[sl].mean() > 0.5, om[sl].mean() > 0.5
                line += "#" if b and o else ("B" if b else ("o" if o else "."))
            print("   " + line)
    return iou


if __name__ == "__main__":
    jobs = ONLY or sorted(os.path.basename(p)[:-4] for p in os.listdir(REN) if p[:3] in ("10-", "11-")
                          and p.endswith(".png") and "_mask" not in p)
    vals = [report(j) for j in jobs if os.path.exists(os.path.join(REF, j + ".png"))]
    print("PASS >= 0.85: %d of %d" % (sum(v >= 0.85 for v in vals), len(vals)))
