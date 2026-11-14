# hud-art-1 round 2 (art lead: "the character icons don't match the character looks"). No new image calls were possible
# (the Vertex login had expired), so the two clear mismatches against today's in-game models are fixed by recolouring the
# existing drawings: the Healer's skin to the model's warm brown, the Farmer's hair to the model's brown.
# usage: python3 match_ingame.py IN.png OUT.png healer-skin|farmer-hair|kaiju-navy [top_frac]
import sys
import numpy as np
from PIL import Image
sys.path.insert(0, __import__("os").path.dirname(__file__))
from recolor import rgb_to_hsv, hsv_to_rgb

def soft(x, a, b):  # 0 below a, 1 above b
    return np.clip((x - a) / max(1e-6, b - a), 0, 1)

def edge_guard(al, px=7):
    # keeps the outer navy outline: no recolour within px of the transparent edge
    from scipy import ndimage as ndi
    inside = ndi.binary_erosion(al > 0.5, iterations=px)
    return ndi.gaussian_filter(inside.astype(np.float32), 1.5)

def apply(im, mode, top_frac=0.32):
    a = im.astype(np.float32) / 255; h, s, v = rgb_to_hsv(a[..., :3]); al = a[..., 3]
    if mode == "kaiju-navy":
        pass
    elif mode == "healer-skin":
        # pale peach skin -> warm brown (#9A6448-ish), shading kept by scaling value; white hair (s < 0.1) and darker
        # leather or brass (v < 0.7 or s > 0.5) stay.
        w = soft(h, 4, 10) * (1 - soft(h, 32, 38)) * soft(s, 0.10, 0.16) * (1 - soft(s, 0.46, 0.54)) * soft(v, 0.68, 0.76)
        nh = np.full_like(h, 21.0); ns = np.clip(0.50 + (s - 0.22) * 0.6, 0.38, 0.66); nv = v * 0.63
    elif mode == "farmer-hair":
        ys, xs = np.nonzero(al > 0.5); y0, y1 = ys.min(), ys.max(); cut = y0 + (y1 - y0) * top_frac
        rows = np.arange(a.shape[0], dtype=np.float32)[:, None] * np.ones((1, a.shape[1]), np.float32)
        region = 1 - soft(rows, cut - 20, cut + 20)
        # dark blue-teal hair -> chestnut brown; bright teal scarf (v > 0.62) stays
        w = soft(h, 188, 196) * (1 - soft(h, 238, 246)) * soft(s, 0.18, 0.26) * (1 - soft(v, 0.56, 0.64)) * soft(v, 0.08, 0.12) * region * edge_guard(al)
        nh = np.full_like(h, 24.0); ns = np.clip(s * 1.05 + 0.08, 0, 0.85); nv = np.clip(v * 1.08 + 0.04, 0, 1)
    if mode == "kaiju-navy":
        # grey stone plates -> the in-game model's navy-slate rock; glow (saturated), teeth and claws (v > 0.82),
        # the tan belly (hue 20-50, s > 0.18) and the outline (v < 0.2) stay
        w = (1 - soft(s, 0.16, 0.24)) * soft(v, 0.20, 0.26) * (1 - soft(v, 0.80, 0.86))
        nh = np.full_like(h, 224.0); ns = np.clip(0.30 + s * 0.6, 0, 0.55); nv = np.clip(v * 0.80, 0, 1)
    new = hsv_to_rgb(nh, ns, nv)
    out = a.copy(); out[..., :3] = a[..., :3] * (1 - w[..., None]) + new * w[..., None]
    share = float(((w > 0.5) & (al > 0.5)).sum() / max(1, (al > 0.5).sum()))
    return (np.clip(out, 0, 1) * 255).astype(np.uint8), share

if __name__ == "__main__":
    im = np.asarray(Image.open(sys.argv[1]).convert("RGBA"))
    out, share = apply(im, sys.argv[3], float(sys.argv[4]) if len(sys.argv) > 4 else 0.32)
    Image.fromarray(out, "RGBA").save(sys.argv[2], optimize=True); print(sys.argv[2], sys.argv[3], round(share, 4))
