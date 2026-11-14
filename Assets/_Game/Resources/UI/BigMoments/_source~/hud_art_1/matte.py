# hud-art-1 matte: generated art on flat white -> true alpha cut-out with a decontaminated edge.
# usage: python3 matte.py IN.png OUT.png SIZE [--report]
# 1. background = near-white pixels connected to the image border (flood fill), plus large enclosed pure-white holes
#    (between an arm and the body);
# 2. a band of up to 5 px inside that boundary is un-blended against white: each band pixel C = a F + (1 - a) W, with F the
#    colour of the nearest solid foreground pixel, so a = the projection of (W - C) on (W - F); its colour becomes F
#    (no white fringe; the dark ink outline keeps a soft anti-aliased edge);
# 3. resampled to SIZE in premultiplied space, then the colour of transparent pixels is bled outward from the edge so
#    bilinear filtering and mipmaps never pull white into the rim. Straight alpha in the file (Unity's UI expects it;
#    alphaIsTransparency is set on import).
# Prints one JSON line: coverage, corner alpha, fringe ratio (edge pixels brighter than their solid neighbour), holes.
import sys, json
import numpy as np
from PIL import Image
from scipy import ndimage as ndi

def matte(src, size, bg_t=26, hole_t=8, hole_min=0.0004, band=5):
    im = np.asarray(Image.open(src).convert("RGB")).astype(np.float32)
    h, w, _ = im.shape
    d = 255.0 - im.min(axis=2)                     # distance from white (any channel)
    nearw = d < bg_t
    lab, n = ndi.label(nearw)
    border = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))) - {0}
    bg = np.isin(lab, list(border))
    holes = 0
    purew = (d < hole_t) & ~bg
    hl, hn = ndi.label(purew)
    if hn:
        sizes = ndi.sum(np.ones_like(d), hl, range(1, hn + 1))
        for i, s in enumerate(sizes, 1):
            if s > hole_min * h * w:
                bg |= ndi.binary_dilation(hl == i, iterations=2) & nearw; holes += 1
    fg = ~bg
    dist_in = ndi.distance_transform_edt(fg)
    solid = fg & (dist_in > band)
    # nearest solid pixel for every pixel
    _, (iy, ix) = ndi.distance_transform_edt(~solid, return_indices=True)
    F = im[iy, ix]
    Wc = np.full(3, 255.0, np.float32)
    num = ((Wc - im) * (Wc - F)).sum(axis=2)
    den = ((Wc - F) ** 2).sum(axis=2) + 1e-3
    a = np.clip(num / den, 0.0, 1.0)
    a[solid] = 1.0
    outer = bg & (ndi.distance_transform_edt(bg) <= 2)   # faint anti-alias just outside the flood line
    a[bg & ~outer] = 0.0
    a[outer] = np.where(a[outer] > 0.08, a[outer], 0.0)
    # colours: solid keep, band and outer take F
    col = im.copy()
    edge = ~solid
    col[edge] = F[edge]
    # light dark-colour weighting: a band pixel whose own colour is darker than F keeps its own (inner ink detail)
    rgba = np.dstack([col, a * 255.0])
    # premultiplied resample
    pm = np.dstack([col * a[..., None], a * 255.0]).astype(np.float32)
    img = Image.fromarray(np.clip(pm, 0, 255).astype(np.uint8), "RGBA")
    if size and size != w:
        # resize each channel in float to stay premultiplied-correct
        chans = [Image.fromarray(pm[..., k], "F").resize((size, size), Image.LANCZOS) for k in range(4)]
        pm = np.dstack([np.asarray(c) for c in chans])
    A = np.clip(pm[..., 3], 0, 255)
    an = A / 255.0
    rgb = np.where(an[..., None] > 1e-3, pm[..., :3] / np.maximum(an[..., None], 1e-3), 0)
    rgb = np.clip(rgb, 0, 255)
    # bleed colour into transparent pixels (nearest visible pixel)
    vis = an > 0.02
    _, (by, bx) = ndi.distance_transform_edt(~vis, return_indices=True)
    rgb = np.where(vis[..., None], rgb, rgb[by, bx])
    out = np.dstack([rgb, A]).astype(np.uint8)
    # report
    hh, ww = A.shape
    k = max(2, hh // 64)
    corners = [float(A[:k, :k].max()), float(A[:k, -k:].max()), float(A[-k:, :k].max()), float(A[-k:, -k:].max())]
    part = (an > 0.02) & (an < 0.98)
    lum = rgb.mean(axis=2)
    solid2 = an >= 0.98
    _, (sy, sx) = ndi.distance_transform_edt(~solid2, return_indices=True)
    fringe = part & (lum > lum[sy, sx] + 40)
    edgeband = ndi.binary_dilation(an > 0.5, iterations=1) & ~ndi.binary_erosion(an > 0.5, iterations=1)
    ys, xs = np.nonzero(an > 0.5)
    bbox = [int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())] if len(xs) else None
    rep = {"src": src.split("/")[-1], "size": [ww, hh], "coverage": round(float((an > 0.5).mean()), 4), "corner_alpha_max": max(corners),
           "fringe_ratio": round(float(fringe.sum()) / max(1, int(part.sum())), 4), "edge_px": int(edgeband.sum()), "holes_cut": holes,
           "bbox": bbox, "touches_edge": bool(bbox and (bbox[0] <= 1 or bbox[1] <= 1 or bbox[2] >= ww - 2 or bbox[3] >= hh - 2))}
    return out, rep

if __name__ == "__main__":
    src, dst, size = sys.argv[1], sys.argv[2], int(sys.argv[3])
    out, rep = matte(src, size)
    Image.fromarray(out, "RGBA").save(dst, optimize=True)
    print(json.dumps(rep))
