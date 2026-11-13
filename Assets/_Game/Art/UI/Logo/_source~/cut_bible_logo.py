#!/usr/bin/env python3
"""Cut the bible's primary logo (Visual Asset Bible p04) out of its cream page and export it for the title screen.

Source: bible-p04-primary-logo.png, the native 1536 x 1024 page-4 raster embedded in
Critical-Mass-Visual-Asset-Bible.pdf (pdfimages -png -f 4 -l 4), cropped to the logo panel (x 0..808, y 4..640).
That raster is the largest copy of the logo that exists in the repo, the bible PDF and the vault.

Steps (deterministic, re-runnable):
  1. Background model: the page is a slightly uneven cream. Pixels close to the border cream are sampled on a 24 px
     grid (block median), holes filled from the nearest sampled block, smoothed: B(x, y).
  2. Alpha from the colour distance to B (CIE-ish weighted RGB), smoothstep between 10 and 46; everything inside the
     logo's hard silhouette (distance > 30, holes filled) is fully opaque, so pale highlights inside the art stay solid.
  3. Colour decontamination: C = B + (P - B) / alpha at the soft edge, so no cream halo survives on a dark background.
  4. Components that are not the logo are dropped: the "PRIMARY LOGO" caption (top below y 585) and the next panel's
     debris touching the right edge.
  5. Exports, trimmed with an 8 px margin:
       @1x = the native pixels (no resampling at all),
       @2x / @4x = Lanczos-3 on premultiplied colour, then the alpha edge re-sharpened (smoothstep around the 0.5
       crossing, which keeps the anti-aliased edge one output pixel wide instead of 2 / 4) and a light unsharp mask on
       colour (radius 0.8 px of the output, 50%).
     A glow mask of the egg's teal and red seams is exported the same way (critical-mass-bible-logo-glow@Nx.png) for
     the title screen's egg pulse.

Usage: python3 cut_bible_logo.py   (writes Assets/_Game/Resources/UI/Logo/critical-mass-bible-logo*@{1,2,4}x.png)
"""
import json
import os

import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "..", "..", "Resources", "UI", "Logo"))
SRC = os.path.join(HERE, "bible-p04-primary-logo.png")
CAPTION_TOP = 609   # source crop y: the lowest logo rock ends at y 604, the caption glyphs run y 615..624
RIGHT_EDGE = 800    # anything reaching past this belongs to the compact-badge panel


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def background(rgb):
    h, w, _ = rgb.shape
    border = np.concatenate([rgb[:6].reshape(-1, 3), rgb[-6:].reshape(-1, 3), rgb[:, :6].reshape(-1, 3)])
    cream = np.median(border, axis=0)
    near = np.sqrt(((rgb - cream) ** 2).sum(-1)) < 14
    g = 24
    gh, gw = (h + g - 1) // g, (w + g - 1) // g
    grid = np.zeros((gh, gw, 3))
    have = np.zeros((gh, gw), bool)
    for j in range(gh):
        for i in range(gw):
            blk = rgb[j * g:(j + 1) * g, i * g:(i + 1) * g]
            m = near[j * g:(j + 1) * g, i * g:(i + 1) * g]
            if m.sum() > g * g * 0.25:
                grid[j, i] = np.median(blk[m], axis=0)
                have[j, i] = True
    _, (iy, ix) = ndimage.distance_transform_edt(~have, return_indices=True)
    grid = grid[iy, ix]
    for c in range(3):
        grid[..., c] = ndimage.gaussian_filter(grid[..., c], 1.0, mode="nearest")
    big = np.stack([np.asarray(Image.fromarray(grid[..., c].astype(np.float32), "F").resize((w, h), Image.BILINEAR))
                    for c in range(3)], -1)
    return big, cream


def cut(rgb, caption_top=None, right_edge=None):
    caption_top = CAPTION_TOP if caption_top is None else caption_top
    right_edge = RIGHT_EDGE if right_edge is None else right_edge
    bg, cream = background(rgb)
    diff = rgb - bg
    dist = np.sqrt(((diff * np.array([0.9, 1.2, 1.0])) ** 2).sum(-1))
    soft = smoothstep(10.0, 46.0, dist)
    hard = dist > 30.0
    lab, n = ndimage.label(hard)
    keep = np.zeros(n + 1, bool)
    for k, sl in enumerate(ndimage.find_objects(lab), start=1):
        y0, y1, x0, x1 = sl[0].start, sl[0].stop, sl[1].start, sl[1].stop
        if y0 >= caption_top or x1 >= right_edge + 8 or x0 <= 1 or (y1 - y0) * (x1 - x0) < 6:
            continue
        keep[k] = True
    solid = keep[lab]
    solid = ndimage.binary_fill_holes(solid)
    near_solid = ndimage.binary_dilation(solid, iterations=4)
    interior = ndimage.binary_erosion(solid, iterations=2)
    a = np.where(near_solid, soft, 0.0)
    a = np.maximum(a, interior.astype(float))
    a[caption_top:] = 0.0
    safe = np.maximum(a, 1e-3)[..., None]
    col = np.clip(bg + diff / safe, 0, 255)
    col = np.where(a[..., None] > 0.004, col, 0.0)
    return col, a, cream


def glow_mask(col, a):
    """The egg's lit seams: saturated, bright teal or red pixels inside the egg's box, blurred into a soft glow."""
    h, w = a.shape
    r, g, b = col[..., 0], col[..., 1], col[..., 2]
    mx, mn = col.max(-1), col.min(-1)
    sat = (mx - mn) / np.maximum(mx, 1)
    teal = (g > 150) & (b > 150) & (r < 140)
    red = (r > 200) & (g < 140) & (b < 140)
    box = np.zeros_like(a, bool)
    box[0:250, 280:560] = True   # the egg and its plume in the source crop
    m = (teal | red) & (sat > 0.45) & (mx > 170) & box & (a > 0.5)
    m = ndimage.gaussian_filter(m.astype(float), 5.0)
    m = np.clip(m / max(m.max(), 1e-6) * 1.6, 0, 1)
    # glow colour: the seam colour spread outward
    cw = np.stack([ndimage.gaussian_filter(np.where((teal | red) & box, col[..., c], 0), 5.0) for c in range(3)], -1)
    wsum = ndimage.gaussian_filter(((teal | red) & box).astype(float), 5.0)[..., None]
    gc = np.clip(cw / np.maximum(wsum, 1e-4) * 1.15, 0, 255)
    return gc, m


def trim_box(a, pad=8):
    ys, xs = np.nonzero(a > 0.004)
    return max(0, ys.min() - pad), min(a.shape[0], ys.max() + 1 + pad), max(0, xs.min() - pad), min(a.shape[1], xs.max() + 1 + pad)


def upscale(col, a, k, sharpen_alpha=True):
    h, w = a.shape
    size = (w * k, h * k)
    pm = col * a[..., None]
    up = np.stack([np.asarray(Image.fromarray(pm[..., c].astype(np.float32), "F").resize(size, Image.LANCZOS)) for c in range(3)], -1)
    ua = np.asarray(Image.fromarray(a.astype(np.float32), "F").resize(size, Image.LANCZOS))
    ua = np.clip(ua, 0, 1)
    uc = np.clip(up / np.maximum(ua, 1e-3)[..., None], 0, 255)
    if sharpen_alpha:
        # the Lanczos edge ramp is k px wide; pull it back toward ~1 output px around the 0.5 crossing
        half = 0.5 / k + 0.08
        ua = smoothstep(0.5 - half, 0.5 + half, ua)
        img = Image.fromarray(uc.astype(np.uint8), "RGB").filter(ImageFilter.UnsharpMask(radius=0.8 * k / 2, percent=50, threshold=2))
        uc = np.asarray(img).astype(float)
    return uc, ua


def save(col, a, path):
    rgba = np.dstack([np.clip(col, 0, 255), np.clip(a * 255.0 + 0.5, 0, 255)]).astype(np.uint8)
    Image.fromarray(rgba, "RGBA").save(path, optimize=True)


def export(src, name, caption_top, right_edge, glow):
    rgb = np.asarray(Image.open(src).convert("RGB")).astype(float)
    col, a, cream = cut(rgb, caption_top, right_edge)
    gc, gm = glow_mask(col, a)
    y0, y1, x0, x1 = trim_box(a)
    col, a, gc, gm = col[y0:y1, x0:x1], a[y0:y1, x0:x1], gc[y0:y1, x0:x1], gm[y0:y1, x0:x1]
    os.makedirs(OUT, exist_ok=True)
    info = {"source": os.path.basename(src), "source_px": [rgb.shape[1], rgb.shape[0]], "trim": [int(x0), int(y0), int(x1), int(y1)],
            "px_1x": [int(a.shape[1]), int(a.shape[0])], "cream": [round(float(v), 1) for v in cream]}
    for k in (1, 2, 4):
        if k == 1:
            c1, a1, g1, m1 = col, a, gc, gm
        else:
            c1, a1 = upscale(col, a, k)
            if glow:
                g1, m1 = upscale(gc, gm, k, sharpen_alpha=False)
        save(c1, a1, os.path.join(OUT, f"{name}@{k}x.png"))
        if glow:
            save(g1, m1, os.path.join(OUT, f"{name}-glow@{k}x.png"))
        info[f"{k}x"] = [int(a1.shape[1]), int(a1.shape[0])]
    return info


def main():
    # primary logo: the title screen; compact badge: small sizes (corner badge, loading, icons)
    print(json.dumps(export(SRC, "critical-mass-bible-logo", CAPTION_TOP, RIGHT_EDGE, True)))
    print(json.dumps(export(os.path.join(HERE, "bible-p04-compact-badge.png"), "critical-mass-bible-badge", 440, 372, False)))


if __name__ == "__main__":
    main()
