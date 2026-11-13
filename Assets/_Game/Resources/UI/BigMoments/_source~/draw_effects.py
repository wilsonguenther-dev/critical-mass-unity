#!/usr/bin/env python3
"""Big Moments: effect sheets, stinger plate and TEXT-FREE title plates, drawn in plain Python (no image generator).

  python3 draw_effects.py            # writes the PNGs one folder up (Assets/_Game/Resources/UI/BigMoments/)

Spec: Obsidian Kaiju-Demo/unity-port/briefs/big-moments-art.md, sections 3 and 4.
Every file is TINTABLE: white and light greys only, straight alpha, transparent pixels carry the colour of the nearest
art pixel (edge bleed), so bilinear filtering and the game's tint never show a dark or light fringe.
Shapes are drawn 4x supersampled (polygons in Pillow, fields in numpy) and box-filtered down to the exact size.
Deterministic: fixed seeds, no clock, no randomness outside numpy.random.default_rng(seed).
Percent sizes in the spec (radius 22%, radius 44%, 70%) are read as percent of the file's WIDTH.
"""
import math
import os

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

SS = 4
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, ".."))


# ---------------------------------------------------------------- helpers
def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def canvas(w, h):
    im = Image.new("L", (w * SS, h * SS), 0)
    return im, ImageDraw.Draw(im)


def down(im, w, h):
    """4x Pillow mask -> float [0,1] at the final size (box filter = exact area coverage)."""
    return np.asarray(im.resize((w, h), Image.BOX), np.float32) / 255.0


def down_np(a, k=SS):
    h, w = a.shape[0] // k, a.shape[1] // k
    return a[:h * k, :w * k].reshape(h, k, w, k).mean(axis=(1, 3))


def S(pts):
    return [(x * SS, y * SS) for x, y in pts]


def over(dv, da, sv, sa):
    """Straight-alpha 'over' of a grey layer (value sv, alpha sa) onto (dv, da)."""
    oa = sa + da * (1 - sa)
    ov = np.where(oa > 1e-6, (sv * sa + dv * da * (1 - sa)) / np.maximum(oa, 1e-6), 0.0)
    return ov, oa


def save(name, value, alpha):
    alpha = np.clip(alpha, 0, 1)
    value = np.clip(value, 0, 1) * np.ones_like(alpha)
    has = alpha >= 1.0 / 255.0
    if has.any() and (~has).any():
        _, (iy, ix) = ndimage.distance_transform_edt(~has, return_indices=True)
        value = np.where(has, value, value[iy, ix])   # edge bleed under transparent pixels
    v8 = np.clip(value * 255 + 0.5, 0, 255).astype(np.uint8)
    a8 = np.clip(alpha * 255 + 0.5, 0, 255).astype(np.uint8)
    path = os.path.join(OUT, name)
    Image.fromarray(np.dstack([v8, v8, v8, a8]), "RGBA").save(path, optimize=True)
    print(f"{name:28s} {a8.shape[1]}x{a8.shape[0]}  {os.path.getsize(path) / 1024:7.1f} KB  cover {np.mean(alpha > 0.5):.3f}")


def taper_poly(x0, y0, x1, y1, w0, w1, n=10, shape=None):
    """A stroke from (x0,y0) to (x1,y1) whose width goes w0 -> w1 (shape(s) overrides the profile)."""
    dx, dy = x1 - x0, y1 - y0
    L = math.hypot(dx, dy) or 1.0
    nx, ny = -dy / L, dx / L
    left, right = [], []
    for i in range(n + 1):
        s = i / n
        w = shape(s) if shape else w0 + (w1 - w0) * s
        px, py = x0 + dx * s, y0 + dy * s
        left.append((px + nx * w / 2, py + ny * w / 2))
        right.append((px - nx * w / 2, py - ny * w / 2))
    return left + right[::-1]


# ---------------------------------------------------------------- 1. radial speed lines
def speedlines_radial():
    W = 2048
    c = W / 2
    rng = np.random.default_rng(101)
    n = 124
    starts = np.linspace(0.22 * W, 0.37 * W, 8)          # clear disc radius 22% of the width
    rmax = 1.55 * c * math.sqrt(2)                       # past the corners
    yy, xx = np.mgrid[0:W, 0:W].astype(np.float32)
    rr = np.hypot(xx + 0.5 - c, yy + 0.5 - c)
    alpha = np.zeros((W, W), np.float32)
    lines = []
    for i in range(n):
        th = 2 * math.pi * (i + rng.uniform(-0.42, 0.42)) / n
        k = rng.random()
        w = rng.uniform(5, 13) if k < 0.62 else rng.uniform(15, 27) if k < 0.92 else rng.uniform(30, 44)
        lines.append((th, w, int(rng.integers(0, len(starts))), rng.uniform(0.7, 1.0)))
    for b, r0 in enumerate(starts):
        for lvl in (0.7, 0.85, 1.0):
            im, d = canvas(W, W)
            any_ = False
            for th, w, bb, a in lines:
                if bb != b or not (lvl - 0.15 < a <= lvl + 1e-6):
                    continue
                any_ = True
                ha = (w / 2) / (0.5 * W)                 # width measured at r = W/2
                p0 = (c + r0 * math.cos(th), c + r0 * math.sin(th))
                p1 = (c + rmax * math.cos(th - ha), c + rmax * math.sin(th - ha))
                p2 = (c + rmax * math.cos(th + ha), c + rmax * math.sin(th + ha))
                d.polygon(S([p0, p1, p2]), fill=255)
            if not any_:
                continue
            ramp = smoothstep(r0, r0 + 0.16 * W, rr)      # alpha 0 at the centre end of each line
            alpha = np.maximum(alpha, down(im, W, W) * ramp * lvl)
    save("bm-speedlines-radial.png", 1.0, alpha)


# ---------------------------------------------------------------- 2. band speed lines (tileable in x)
def speedlines_band():
    W, H = 2048, 512
    rng = np.random.default_rng(202)

    def build(count):
        layers = {0.6: canvas(W, H), 0.8: canvas(W, H), 1.0: canvas(W, H)}
        r = np.random.default_rng(203)
        for _ in range(count):
            t = float(np.clip(2 + r.gamma(1.6, 3.2), 2, 18))
            L = r.uniform(260, 1300)
            x = r.uniform(0, W)
            y = r.uniform(10 + t, H - 10 - t)
            lvl = r.choice([0.6, 0.8, 1.0], p=[0.25, 0.3, 0.45])
            prof = lambda s, t=t: t * (max(s, 0.0) ** 0.35) * (max(1 - s, 0.0) ** 0.9) * 1.6
            for off in (-W, 0, W):                       # wrap: tileable left-right
                pts = taper_poly(x + off, y, x + off + L, y, 0, 0, n=24, shape=prof)
                layers[lvl][1].polygon(S(pts), fill=255)
        v = np.zeros((H, W), np.float32); a = np.zeros((H, W), np.float32)
        for lvl, (im, _) in sorted(layers.items()):
            a = np.maximum(a, down(im, W, H) * lvl)
        return a

    count = 230
    for _ in range(3):                                   # aim at 40% streak density (area with alpha > 0.25)
        a = build(count)
        dens = float(np.mean(a > 0.25))
        if abs(dens - 0.40) < 0.015:
            break
        count = int(count * 0.40 / max(dens, 1e-3))
    save("bm-speedlines-band.png", 1.0, a)
    return dens


# ---------------------------------------------------------------- 3. burst
def burst():
    W = 1024
    c = W / 2
    rng = np.random.default_rng(303)
    n = 17
    ang = np.sort((np.arange(n) + rng.uniform(-0.3, 0.3, n)) / n * 2 * math.pi)
    tips = 0.47 * W * rng.uniform(0.66, 1.0, n)
    tips[rng.integers(0, n, 3)] = 0.47 * W              # a few full-length spikes
    pts, inner = [], []
    for i in range(n):
        a0, a1 = ang[i], ang[(i + 1) % n] + (2 * math.pi if i == n - 1 else 0)
        am = (a0 + a1) / 2 + rng.uniform(-0.06, 0.06)
        rv = 0.2 * W * rng.uniform(0.85, 1.15)
        pts += [(c + tips[i] * math.cos(a0), c + tips[i] * math.sin(a0)), (c + rv * math.cos(am), c + rv * math.sin(am))]
        inner += [(c + 0.62 * tips[i] * math.cos(a0), c + 0.62 * tips[i] * math.sin(a0)),
                  (c + 0.62 * rv * math.cos(am), c + 0.62 * rv * math.sin(am))]
    im, d = canvas(W, W); d.polygon(S(pts), fill=255); outer_m = down(im, W, W)
    im, d = canvas(W, W); d.polygon(S(inner), fill=255); inner_m = down(im, W, W)
    yy, xx = np.mgrid[0:W, 0:W]
    rr = np.hypot(xx + 0.5 - c, yy + 0.5 - c)
    fade = 1.0 - 0.4 * smoothstep(0.22 * W, 0.47 * W, rr)  # solid core, 60% at the tips
    value = 0.86 + 0.14 * inner_m                          # two toon value steps
    save("bm-burst.png", value, outer_m * fade)


# ---------------------------------------------------------------- 4. band fill (tileable in x)
def periodic_noise(n, rng, terms):
    x = np.arange(n) / n
    out = np.zeros(n)
    for k, amp in terms:
        out += amp * np.sin(2 * math.pi * k * x + rng.uniform(0, 2 * math.pi))
    return out


def torn_edge(n, rng, depth_min, depth_max):
    e = periodic_noise(n, rng, [(3, 1.0), (7, 0.8), (17, 0.6), (41, 0.45), (97, 0.35), (211, 0.25)])
    jag = ndimage.gaussian_filter1d(rng.normal(0, 1, n), 1.5 * SS, mode="wrap")
    e = e + 0.9 * jag / (jag.std() + 1e-6)
    e = (e - e.min()) / (e.max() - e.min())
    return depth_min + (depth_max - depth_min) * e


def band():
    W, H = 2048, 512
    rng = np.random.default_rng(404)
    top = torn_edge(W * SS, rng, 10, 24) * SS
    bot = torn_edge(W * SS, rng, 10, 24) * SS
    Y = np.arange(H * SS, dtype=np.float32)[:, None]
    m = ((Y >= top[None, :]) & (Y <= H * SS - bot[None, :])).astype(np.float32)
    # dry-brush gaps near the long edges (wrapped, so the tile stays seamless)
    im, d = canvas(W, H)
    for _ in range(46):
        y = rng.uniform(26, 44) if rng.random() < 0.5 else H - rng.uniform(26, 44)
        x, L, t = rng.uniform(0, W), rng.uniform(40, 220), rng.uniform(1.5, 4.5)
        prof = lambda s, t=t: t * math.sin(math.pi * s) ** 0.6
        for off in (-W, 0, W):
            d.polygon(S(taper_poly(x + off, y, x + off + L, y, 0, 0, 12, prof)), fill=255)
    gaps = np.asarray(im, np.float32) / 255.0
    alpha = down_np(m * (1 - gaps))
    # brushed-ink texture: long horizontal fibres, wrapped in x, three toon steps 85 / 92.5 / 100 %
    f = ndimage.gaussian_filter(rng.normal(0, 1, (H, W)), sigma=(1.2, 48), mode=("reflect", "wrap"))
    f = (f - f.mean()) / (f.std() + 1e-6)
    value = np.where(f < -0.7, 0.85, np.where(f < 0.35, 0.925, 1.0))
    value = ndimage.uniform_filter(value, size=(1, 3), mode=("reflect", "wrap"))
    save("bm-band.png", value, alpha)


# ---------------------------------------------------------------- 5. corner cracks
def cracks_corner():
    W = 1024
    lim = 0.66 * W                                         # ends before 70% of width and height
    rng = np.random.default_rng(505)
    segs = []

    def grow(x, y, ang, w, depth):
        """Angular cracks: long straight runs with sharp kinks (zigzag), few branches, width tapering to a point."""
        zig = rng.choice([-1, 1])
        start = math.hypot(x, y)
        while True:
            L = rng.uniform(44, 96)
            zig = -zig
            ang += zig * rng.uniform(0.12, 0.38)
            nx, ny = x + L * math.cos(ang), y + L * math.sin(ang)
            r = math.hypot(nx, ny)
            if nx < 0 or ny < 0 or nx > lim or ny > lim or r > lim or w < 2.6:
                return
            nw = max(w * (1 - L / max(lim * (0.95 - 0.12 * depth) - math.hypot(x, y), L * 1.4)), 1.8)
            segs.append((x, y, nx, ny, w, nw))
            if depth < 2 and rng.random() < 0.24 and w > 7:
                side = rng.choice([-1, 1])
                grow(nx, ny, ang + side * rng.uniform(0.55, 1.0), nw * 0.6, depth + 1)
            x, y, w = nx, ny, nw

    for a0, w0, sx, sy in [(0.32, 30, 0, 22), (0.80, 36, 8, 8), (1.25, 28, 22, 0), (0.08, 16, 0, 90), (1.49, 16, 96, 0)]:
        grow(sx, sy, a0, w0, 0)
    def draw(offset):
        im, d = canvas(W, W)
        for x0, y0, x1, y1, w0, w1 in segs:
            x0o, y0o, x1o, y1o = x0 + offset, y0 + offset, x1 + offset, y1 + offset
            d.polygon(S(taper_poly(x0o, y0o, x1o, y1o, w0, w1, 2)), fill=255)
            for (px, py, pw) in ((x0o, y0o, w0), (x1o, y1o, w1)):
                d.ellipse(S([(px - pw / 2, py - pw / 2), (px + pw / 2, py + pw / 2)]), fill=255)
        return down(im, W, W)
    shade = draw(3.0)                                      # chiselled lower lip, darker value step
    face = draw(0.0)
    v, a = over(np.zeros((W, W)), np.zeros((W, W)), 0.55, shade)
    v, a = over(v, a, 1.0, face)
    save("bm-cracks-corner.png", v, a)
    return len(segs)


# ---------------------------------------------------------------- 6. aura ring
def aura_ring():
    W = 512
    c = W / 2
    R, T = 0.44 * W, 0.06 * W                              # radius 44%, thickness 6% of the width
    yy, xx = np.mgrid[0:W * SS, 0:W * SS].astype(np.float32)
    rr = np.hypot((xx + 0.5) / SS - c, (yy + 0.5) / SS - c)
    th = np.arctan2((yy + 0.5) / SS - c, (xx + 0.5) / SS - c)
    ring = (np.abs(rr - R) <= T / 2).astype(np.float32)
    # three flame tongues: swept along the ring (clockwise), rising off the outer edge to the file border
    licks = np.zeros_like(ring)
    room = (c - 2) - (R + T / 2)
    for a0, span, hgt in [(-1.9, 0.60, 1.0), (-1.2, 0.30, 0.6), (0.25, 0.55, 1.0), (0.90, 0.28, 0.55),
                          (2.35, 0.64, 1.0), (3.05, 0.30, 0.6)]:
        s = ((th - a0) % (2 * math.pi)) / span             # 0..1 along the tongue, swept clockwise
        rise = np.clip(s / 0.78, 0, 1) ** 1.5
        fall = np.clip((1 - s) / 0.22, 0, 1) ** 0.7
        prof = np.where(s < 1, np.minimum(rise, fall), 0.0) * hgt   # slow rise, sharp pointed tip
        top = R + T / 2 - 1 + room * prof
        licks = np.maximum(licks, ((rr >= R) & (rr <= top) & (s < 1)).astype(np.float32))
    a = down_np(np.maximum(ring, licks))
    a = ndimage.gaussian_filter(a, 1.6)                    # soft edge
    a = np.clip(a * 1.15, 0, 1)
    save("bm-aura-ring.png", 1.0, a)


# ---------------------------------------------------------------- 7. spark
def spark():
    W = 64
    c = W / 2
    yy, xx = np.mgrid[0:W * SS, 0:W * SS].astype(np.float32)
    x, y = (xx + 0.5) / SS - c, (yy + 0.5) / SS - c
    r, th = np.hypot(x, y), np.arctan2(y, x)
    tip = np.where(np.abs(np.sin(th)) > 0.7071, 30.0, 25.0)   # vertical points a little longer
    rad = 3.2 + (tip - 3.2) * np.abs(np.cos(2 * th)) ** 7
    star = (r <= rad).astype(np.float32)
    a = down_np(star)
    yy2, xx2 = np.mgrid[0:W, 0:W]
    core = np.exp(-(((xx2 + 0.5 - c) ** 2 + (yy2 + 0.5 - c) ** 2) / (2 * 6.0 ** 2)))
    a = np.maximum(a, core)
    value = 0.88 + 0.12 * np.clip(core * 1.6, 0, 1)
    save("bm-spark.png", value, a)


# ---------------------------------------------------------------- 8. edge glow
def edge_glow():
    W = 512
    h, rc, fall = W / 2, 0.34 * W, 0.30 * W
    yy, xx = np.mgrid[0:W, 0:W].astype(np.float32)
    px, py = np.abs(xx + 0.5 - h), np.abs(yy + 0.5 - h)
    qx, qy = px - (h - rc), py - (h - rc)                  # signed distance to a rounded square
    sd = np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - rc
    d = -sd                                                # inward distance from the edge
    a = np.clip(1 - d / fall, 0, 1) ** 2.2
    save("bm-edge-glow.png", 1.0, a)


# ---------------------------------------------------------------- 9. stinger plate
def parallelogram(w, h, m, lean_deg):
    off = (h - 2 * m) * math.tan(math.radians(lean_deg))
    return [(m + off, m), (w - m, m), (w - m - off, h - m), (m, h - m)]


def inner_border(mask4, inset, width):
    dist = ndimage.distance_transform_edt(mask4 > 127) / SS
    return ((dist >= inset) & (dist < inset + width)).astype(np.float32)


def stinger_plate():
    W, H = 768, 192
    im, d = canvas(W, H)
    d.polygon(S(parallelogram(W, H, 8, 14)), fill=255)
    m4 = np.asarray(im)
    a = down(im, W, H)
    border = down_np(inner_border(m4, 9, 6))               # 6 px darker-grey inner border
    value = 1.0 - 0.38 * border
    save("bm-stinger-plate.png", value, a)


# ---------------------------------------------------------------- 10. title plates (no lettering: the game draws it)
def title_plate():
    """Slanted slab, torn long edges, brushed texture, inner border, hard offset shadow (8 px down-right)."""
    W, H = 1536, 384
    rng = np.random.default_rng(1001)
    off = (H - 2 * 30) * math.tan(math.radians(10))
    X = (np.arange(W * SS, dtype=np.float32)[None, :] + 0.5) / SS
    Y = (np.arange(H * SS, dtype=np.float32)[:, None] + 0.5) / SS
    top = 30 + torn_edge(W * SS, rng, 0, 14)[None, :]
    bot = H - 38 - torn_edge(W * SS, rng, 0, 14)[None, :]
    xl = 20 + off * (1 - (Y - 30) / (H - 68))             # slanted clean-cut ends
    xr = W - 28 - off * ((Y - 30) / (H - 68))
    slab = ((Y >= top) & (Y <= bot) & (X >= xl) & (X <= xr)).astype(np.float32)
    sh = np.roll(np.roll(slab, 8 * SS, 0), 8 * SS, 1)
    sh[:8 * SS] = 0; sh[:, :8 * SS] = 0
    border = inner_border((slab * 255).astype(np.uint8), 14, 6)
    a_slab, a_sh, a_bd = down_np(slab), down_np(sh), down_np(border)
    f = ndimage.gaussian_filter(rng.normal(0, 1, (H, W)), sigma=(1.0, 40), mode=("reflect", "wrap"))
    f = (f - f.mean()) / (f.std() + 1e-6)
    tex = np.where(f < -0.7, 0.86, np.where(f < 0.4, 0.93, 1.0))
    v, a = over(np.zeros((H, W)), np.zeros((H, W)), 0.30, a_sh)
    v, a = over(v, a, tex * (1 - 0.36 * a_bd), a_slab)
    save("bm-title-plate.png", v, a)


def title_plate_impact():
    """Jagged burst-edged plate for the heavy events (Kaiju, quake, berserk): two value steps plus shadow."""
    W, H = 1536, 384
    rng = np.random.default_rng(1002)
    cx, cy = W / 2, H / 2
    def jag(sx, sy, n, amp_lo, amp_hi):
        pts = []
        for i in range(n):
            t = 2 * math.pi * i / n
            k = 1.0 if i % 2 == 0 else rng.uniform(amp_lo, amp_hi)
            ex, ey = math.cos(t), math.sin(t)
            r = 1.0 / (abs(ex) ** 6 + abs(ey) ** 6) ** (1 / 6)   # squircle base so the plate stays wide
            pts.append((cx + sx * r * ex * k, cy + sy * r * ey * k))
        return pts
    outer = jag(W / 2 - 22, H / 2 - 24, 64, 0.80, 0.90)
    innr = [(cx + (x - cx) * 0.93, cy + (y - cy) * 0.80) for x, y in outer]
    im, d = canvas(W, H); d.polygon(S(outer), fill=255); a_o = down(im, W, H)
    im, d = canvas(W, H); d.polygon(S([(x + 8, y + 8) for x, y in outer]), fill=255); a_s = down(im, W, H)
    im, d = canvas(W, H); d.polygon(S(innr), fill=255); a_i = down(im, W, H)
    v, a = over(np.zeros((H, W)), np.zeros((H, W)), 0.30, a_s)
    v, a = over(v, a, 0.80, a_o)
    v, a = over(v, a, 1.0, a_i)
    save("bm-title-plate-impact.png", v, a)


def title_plate_ribbon():
    """Slim slanted ribbon with swallowtail ends, for the lighter callouts; shadow and inner border."""
    W, H = 1536, 384
    y0, y1, off, notch = 112, 272, 28, 70
    pts = [(40 + off, y0), (W - 40, y0), (W - 40 - notch, (y0 + y1) / 2), (W - 40 - off, y1),
           (40, y1), (40 + notch, (y0 + y1) / 2)]
    pts = [(x, y) for x, y in pts]
    im, d = canvas(W, H); d.polygon(S(pts), fill=255); m4 = np.asarray(im); a_o = down(im, W, H)
    im2, d2 = canvas(W, H); d2.polygon(S([(x + 8, y + 8) for x, y in pts]), fill=255); a_s = down(im2, W, H)
    a_b = down_np(inner_border(m4, 10, 6))
    v, a = over(np.zeros((H, W)), np.zeros((H, W)), 0.30, a_s)
    v, a = over(v, a, 1.0 - 0.36 * a_b, a_o)
    save("bm-title-plate-ribbon.png", v, a)


if __name__ == "__main__":
    speedlines_radial()
    dens = speedlines_band()
    print("band streak density", round(dens, 3))
    burst()
    band()
    print("crack segments", cracks_corner())
    aura_ring()
    spark()
    edge_glow()
    stinger_plate()
    title_plate()
    title_plate_impact()
    title_plate_ribbon()
