# hud-art-1 team recolour: moves one hue band of a cut-out to a target colour (alpha untouched), so both teams' cards and
# every Kaiju allegiance share one drawing. usage: recolor.py IN.png OUT.png MODE    MODE: crimson | kaiju-azure | kaiju-crimson
import sys, colorsys
import numpy as np
from PIL import Image
MODES = {
    # source hue band (degrees), min saturation, min value, target hue, saturation scale, value scale
    "crimson": ((160, 210), 0.28, 0.22, 358, 0.78, 1.10),
    "kaiju-azure": ((55, 115), 0.40, 0.40, 181, 1.00, 1.05),
    "kaiju-crimson": ((55, 115), 0.40, 0.40, 8, 0.95, 1.02),
}
def rgb_to_hsv(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mx = a.max(-1); mn = a.min(-1); d = mx - mn + 1e-6
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    s = np.where(mx > 0, (mx - mn) / (mx + 1e-6), 0); return h, s, mx
def hsv_to_rgb(h, s, v):
    c = v * s; x = c * (1 - np.abs((h / 60) % 2 - 1)); m = v - c
    z = np.zeros_like(h); k = (h // 60).astype(int) % 6
    r = np.choose(k, [c, x, z, z, x, c]); g = np.choose(k, [x, c, c, x, z, z]); b = np.choose(k, [z, z, x, c, c, x])
    return np.stack([r + m, g + m, b + m], -1)
def recolor(src, dst, mode):
    (h0, h1), smin, vmin, ht, ss, vs = MODES[mode]
    im = np.asarray(Image.open(src).convert("RGBA")).astype(np.float32) / 255
    h, s, v = rgb_to_hsv(im[..., :3])
    hw = 12.0
    wh = np.clip((h - h0) / hw, 0, 1) * np.clip((h1 - h) / hw, 0, 1)
    w = wh * np.clip((s - smin) / 0.12, 0, 1) * np.clip((v - vmin) / 0.1, 0, 1)
    nh = np.full_like(h, ht); ns = np.clip(s * ss, 0, 1); nv = np.clip(v * vs, 0, 1)
    new = hsv_to_rgb(nh, ns, nv)
    out = im.copy(); out[..., :3] = im[..., :3] * (1 - w[..., None]) + new * w[..., None]
    Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8), "RGBA").save(dst, optimize=True)
    print(dst, "recoloured", round(float((w > 0.5).mean() / max(1e-6, (im[..., 3] > 0.5).mean())), 4), "of the figure")
if __name__ == "__main__":
    recolor(sys.argv[1], sys.argv[2], sys.argv[3])
