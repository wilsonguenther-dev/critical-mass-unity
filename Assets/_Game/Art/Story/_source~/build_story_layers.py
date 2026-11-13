#!/usr/bin/env python3
"""Build the motion-comic layers for the story intro from the Visual Asset Bible (real concept art) plus procedural
backdrops painted in the bible palette. Deterministic, re-runnable, Pillow + numpy + scipy only.

Source: Critical-Mass-Visual-Asset-Bible.pdf. Its pages carry the artist's native rasters (1536 x 1024; the cover and
p12 maps are wider); this script pulls them losslessly with `pdfimages -png -p` (poppler) into a temp folder, so no
re-rendering or resampling happens before the cut.
  p01 cover key art (1672 x 941): the hill battle (HUD-free middle crop)
  p03 moodboard: central environment painting (basin, shrine arch with the egg, waterfalls) + six side panels
  p06 Warrior, p07 Farmer, p08 Miner sheets: front views and action poses
  p10 Kaiju states: egg, hatchling, neutral / Crimson / berserk adults
  p11 adult Kaiju turnaround + poses (roar)
  p14 props: Azure / Crimson banners, biomass plants, corruption growth, loose orb
  p04 logo: already cut by Art/UI/Logo/_source~/cut_bible_logo.py; the player loads it from Resources/UI/Logo.

Cut-out method (same as the logo cutter): model the uneven cream page as a smooth background, alpha from the colour
distance to it (smoothstep 10..46), solid inside the hard silhouette, large enclosed cream holes re-opened, colour
decontaminated at the edge (C = B + (P - B) / a), small debris dropped. Upscales are Lanczos on premultiplied colour
with the alpha edge re-sharpened and a light unsharp mask (never a blur). Depth-of-field copies (<name>_blur) are made
offline here and stored at half size: they are blurred anyway, so they cost a quarter of the memory.

Usage: python3 build_story_layers.py [--pdf PATH]   -> Assets/_Game/Resources/Story/*.png
"""
import argparse, glob, math, os, subprocess, sys, tempfile
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", "..", "..", "..", ".."))
OUT = os.path.join(REPO, "Assets", "_Game", "Resources", "Story")
PDF = os.path.expanduser("~/Library/Mobile Documents/com~apple~CloudDocs/Critical-Mass-Visual-Asset-Bible.pdf")

HEX = dict(navy="#18233B", azure="#16C7D9", crimson="#FF5A5F", cream="#F7F0E5", biomass="#B7F238",
           corruption="#8E3FD1", stone="#6F7282", brass="#C79245", skytop="#6FCBEA", skyhor="#CDEFF8",
           seam="#4FDDC3", panel="#1B314D", orange="#F9A858")
def rgb(name): h = HEX[name].lstrip("#"); return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], np.float32)

RNG = np.random.default_rng(20261112)
MANIFEST = []   # (name, source, note)

# ---------------------------------------------------------------- pages
def extract_pages(pdf):
    tmp = tempfile.mkdtemp(prefix="cm-story-pages-")
    subprocess.run(["pdfimages", "-png", "-p", pdf, os.path.join(tmp, "img")], check=True)
    pages = {}
    for f in sorted(glob.glob(os.path.join(tmp, "img-*.png"))):
        n = int(os.path.basename(f).split("-")[1])
        pages.setdefault(n, f)
    return pages

def page(pages, n):
    return np.asarray(Image.open(pages[n]).convert("RGB")).astype(np.float32)

# ---------------------------------------------------------------- helpers
def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)

def background(rgbimg):
    h, w, _ = rgbimg.shape
    border = np.concatenate([rgbimg[:4].reshape(-1, 3), rgbimg[-4:].reshape(-1, 3), rgbimg[:, :4].reshape(-1, 3), rgbimg[:, -4:].reshape(-1, 3)])
    cream = np.median(border, axis=0)
    near = np.sqrt(((rgbimg - cream) ** 2).sum(-1)) < 14
    g = 16
    gh, gw = (h + g - 1) // g, (w + g - 1) // g
    grid = np.zeros((gh, gw, 3)); have = np.zeros((gh, gw), bool)
    for j in range(gh):
        for i in range(gw):
            m = near[j * g:(j + 1) * g, i * g:(i + 1) * g]
            if m.sum() > g * g * 0.25:
                grid[j, i] = np.median(rgbimg[j * g:(j + 1) * g, i * g:(i + 1) * g][m], axis=0); have[j, i] = True
    if not have.any():
        grid[:] = cream; have[:] = True
    _, (iy, ix) = ndimage.distance_transform_edt(~have, return_indices=True)
    grid = grid[iy, ix]
    for c in range(3): grid[..., c] = ndimage.gaussian_filter(grid[..., c], 1.0, mode="nearest")
    big = np.stack([np.asarray(Image.fromarray(grid[..., c].astype(np.float32), "F").resize((w, h), Image.BILINEAR)) for c in range(3)], -1)
    return big

def cut(crop, min_area=900, keep_largest=0, hole_area=260):
    """crop: float RGB on cream. Returns float RGBA (0..255, straight alpha)."""
    bg = background(crop)
    diff = crop - bg
    dist = np.sqrt(((diff * np.array([0.9, 1.2, 1.0])) ** 2).sum(-1))
    soft = smoothstep(10.0, 46.0, dist)
    hard = dist > 30.0
    lab, n = ndimage.label(ndimage.binary_closing(hard, iterations=1))
    sizes = ndimage.sum(np.ones_like(lab), lab, index=np.arange(1, n + 1)) if n else np.array([])
    keep = np.zeros(n + 1, bool)
    order = np.argsort(-sizes)
    for rank, k in enumerate(order):
        if sizes[k] < min_area: break
        if keep_largest and rank >= keep_largest: break
        keep[k + 1] = True
    solid = keep[lab]
    filled = ndimage.binary_fill_holes(solid)
    # re-open big enclosed page-coloured holes (between legs, under arms); small pale highlights stay solid
    holes = filled & ~solid & (dist < 16)
    hl, hn = ndimage.label(holes)
    if hn:
        hs = ndimage.sum(np.ones_like(hl), hl, index=np.arange(1, hn + 1))
        big_holes = np.isin(hl, np.where(hs >= hole_area)[0] + 1)
        filled &= ~big_holes
    near_solid = ndimage.binary_dilation(filled, iterations=3)
    interior = ndimage.binary_erosion(filled, iterations=2)
    a = np.where(near_solid, soft, 0.0)
    a = np.maximum(a, interior.astype(float))
    a = np.clip(a, 0, 1)
    safe = np.maximum(a, 1e-3)[..., None]
    col = np.clip(bg + diff / safe, 0, 255)
    col = np.where(a[..., None] > 0.02, col, bg)
    out = np.concatenate([col, (a * 255.0)[..., None]], -1)
    return out

def trim(rgba, margin=6):
    ys, xs = np.where(rgba[..., 3] > 4)
    if len(xs) == 0: return rgba
    y0, y1 = max(0, ys.min() - margin), min(rgba.shape[0], ys.max() + margin + 1)
    x0, x1 = max(0, xs.min() - margin), min(rgba.shape[1], xs.max() + margin + 1)
    return rgba[y0:y1, x0:x1]

def upscale(rgba, k):
    if abs(k - 1) < 1e-6: return rgba
    h, w = rgba.shape[:2]
    W, H = int(round(w * k)), int(round(h * k))
    a = rgba[..., 3:4] / 255.0
    pre = rgba[..., :3] * a
    def rs(ch): return np.asarray(Image.fromarray(ch.astype(np.float32), "F").resize((W, H), Image.LANCZOS))
    P = np.stack([rs(pre[..., c]) for c in range(3)], -1)
    A = np.clip(rs(a[..., 0]), 0, 1)
    A2 = np.where((A > 0.02) & (A < 0.98), smoothstep(0.5 - 0.5 / k, 0.5 + 0.5 / k, A), A)
    col = np.clip(P / np.maximum(A, 1e-3)[..., None], 0, 255)
    img = Image.fromarray(col.astype(np.uint8), "RGB").filter(ImageFilter.UnsharpMask(radius=0.8 * k, percent=45, threshold=1))
    col = np.asarray(img).astype(np.float32)
    return np.concatenate([col, (A2 * 255)[..., None]], -1)

def upscale_rgb(arr, k):
    h, w = arr.shape[:2]
    img = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGB").resize((int(w * k), int(h * k)), Image.LANCZOS)
    img = img.filter(ImageFilter.UnsharpMask(radius=0.9 * k, percent=40, threshold=1))
    return np.asarray(img).astype(np.float32)

def bleed(rgba):
    """Fully transparent pixels take the colour of the nearest opaque one (no dark fringe under filtering)."""
    a = rgba[..., 3]
    empty = a < 2
    if empty.all() or not empty.any(): return rgba
    _, (iy, ix) = ndimage.distance_transform_edt(empty, return_indices=True)
    out = rgba.copy()
    out[..., :3] = rgba[iy, ix, :3]
    out[..., 3] = a
    return out

def mult4(arr):
    """Block-compressed formats (BC7 / DXT5) want sides that are multiples of 4: pad RGBA evenly, crop RGB."""
    h, w = arr.shape[:2]
    if arr.shape[2] == 3: return arr[:h - h % 4, :w - w % 4]
    ph, pw = (-h) % 4, (-w) % 4
    if ph == 0 and pw == 0: return arr
    out = np.zeros((h + ph, w + pw, 4), np.float32)
    out[ph // 2:ph // 2 + h, pw // 2:pw // 2 + w] = arr
    return out

def save(name, arr, source, note=""):
    os.makedirs(OUT, exist_ok=True)
    arr = mult4(np.clip(arr, 0, 255))
    if arr.shape[2] == 4:
        arr = bleed(arr)
        img = Image.fromarray(arr.astype(np.uint8), "RGBA")
    else:
        img = Image.fromarray(arr.astype(np.uint8), "RGB")
    path = os.path.join(OUT, name + ".png")
    img.save(path, optimize=True)
    MANIFEST.append((name, img.size, source, note))
    return img

def save_blur(name, arr, radius, source):
    h, w = arr.shape[:2]
    mode = "RGBA" if arr.shape[2] == 4 else "RGB"
    if mode == "RGBA":
        a = arr[..., 3:4] / 255.0
        pre = np.concatenate([arr[..., :3] * a, a * 255], -1)
        img = Image.fromarray(np.clip(pre, 0, 255).astype(np.uint8), "RGBA").resize((w // 2, h // 2), Image.LANCZOS)
        img = img.filter(ImageFilter.GaussianBlur(radius / 2))
        p = np.asarray(img).astype(np.float32)
        A = p[..., 3:4] / 255.0
        col = p[..., :3] / np.maximum(A, 1e-3)
        out = np.concatenate([col, p[..., 3:4]], -1)
    else:
        img = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGB").resize((w // 2, h // 2), Image.LANCZOS)
        out = np.asarray(img.filter(ImageFilter.GaussianBlur(radius / 2))).astype(np.float32)
    save(name + "_blur", out, source, "depth-of-field copy, half size")

def hsv(arr):
    r, g, b = [arr[..., i] / 255.0 for i in range(3)]
    mx = np.maximum(np.maximum(r, g), b); mn = np.minimum(np.minimum(r, g), b); d = mx - mn
    h = np.zeros_like(mx)
    m = d > 1e-6
    rc = m & (mx == r); gc = m & (mx == g) & ~rc; bc = m & ~rc & ~gc
    h[rc] = ((g - b)[rc] / d[rc]) % 6; h[gc] = (b - r)[gc] / d[gc] + 2; h[bc] = (r - g)[bc] / d[bc] + 4
    h = h * 60.0
    s = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0)
    return h, s, mx

def hsv2rgb(h, s, v):
    c = v * s; hp = (h % 360) / 60.0; x = c * (1 - np.abs(hp % 2 - 1)); z = np.zeros_like(h)
    conds = [(hp < 1), (hp < 2), (hp < 3), (hp < 4), (hp < 5), (hp <= 6)]
    rs = [(c, x, z), (x, c, z), (z, c, x), (z, x, c), (x, z, c), (c, z, x)]
    r = np.select(conds, [t[0] for t in rs]); g = np.select(conds, [t[1] for t in rs]); b = np.select(conds, [t[2] for t in rs])
    m = v - c
    return np.stack([(r + m), (g + m), (b + m)], -1) * 255.0

def team_swap(rgba):
    """Azure team channels (teal / sky-blue cloth, scarf, shield core) to Crimson, as the bible's accent-swap panels do.
    Outlines and dark navy stay (value < 0.36); skin, brass, wood and stone are outside the hue window."""
    h, s, v = hsv(rgba)
    win = (h > 165) & (h < 238) & (s > 0.28) & (v > 0.36)
    w = smoothstep(0.28, 0.42, s) * win
    target_h = 357.0 + (h - 190.0) * 0.25
    newrgb = hsv2rgb(np.where(win, target_h, h), np.clip(s * 1.02, 0, 1), np.clip(v * 1.0, 0, 1))
    out = rgba.copy()
    out[..., :3] = rgba[..., :3] * (1 - w[..., None]) + newrgb * w[..., None]
    return out

def glow_from(rgba, hue_lo, hue_hi, color, sat=0.45, val=0.55, radius=14, gain=1.0):
    h, s, v = hsv(rgba)
    m = ((h >= hue_lo) & (h <= hue_hi) & (s > sat) & (v > val)).astype(np.float32) * (rgba[..., 3] / 255.0)
    g = ndimage.gaussian_filter(m, radius) * 2.2 + ndimage.gaussian_filter(m, radius * 0.35) * 0.8
    g = np.clip(g * gain, 0, 1)
    out = np.zeros(rgba.shape[:2] + (4,), np.float32)
    out[..., :3] = rgb(color)
    out[..., 3] = g * 255
    return out

def pad(rgba, p):
    h, w = rgba.shape[:2]
    out = np.zeros((h + 2 * p, w + 2 * p, 4), np.float32)
    out[p:p + h, p:p + w] = rgba
    return out

# ---------------------------------------------------------------- procedural paint (bible palette)
def fbm(h, w, octaves=5, base=4, seed=0):
    rng = np.random.default_rng(seed)
    acc = np.zeros((h, w), np.float32); amp = 1.0; tot = 0
    for o in range(octaves):
        n = base * (2 ** o)
        g = rng.random((n + 1, int(n * w / h) + 2)).astype(np.float32)
        up = np.asarray(Image.fromarray(g, "F").resize((w, h), Image.BICUBIC))
        acc += up * amp; tot += amp; amp *= 0.5
    acc /= tot
    return (acc - acc.min()) / (acc.max() - acc.min() + 1e-6)

def vgrad(h, w, stops):
    y = np.linspace(0, 1, h)[:, None]
    out = np.zeros((h, w, 3), np.float32)
    ys = [s[0] for s in stops]
    for c in range(3):
        out[..., c] = np.interp(y, ys, [s[1][c] for s in stops]) * np.ones((1, w))
    return out

def mix(a, b, t): return a * (1 - t) + b * t

def sky(w, h, top, mid, hor, sun=None, sun_r=0.0, sun_col=None, clouds=0.0, cloud_col=None, seed=1):
    img = vgrad(h, w, [(0.0, top), (0.55, mid), (1.0, hor)])
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    if sun is not None:
        d = np.sqrt((xx - sun[0] * w) ** 2 + (yy - sun[1] * h) ** 2) / h
        glow = np.exp(-(d / max(sun_r, 1e-3)) ** 2)
        img = mix(img, np.broadcast_to(sun_col, img.shape), glow[..., None] * 0.85)
    if clouds > 0:
        n = fbm(h, w, 6, 3, seed)
        band = smoothstep(0.35, 0.95, yy / h)
        c = smoothstep(0.55, 0.8, n) * (0.35 + 0.65 * band)
        img = mix(img, np.broadcast_to(cloud_col, img.shape), (c * clouds)[..., None])
    return img

def cloud_layer(w, h, col, shade, y0, y1, seed, density=0.6, soft=0.12):
    n = fbm(h, w, 6, 2, seed)
    yy = np.mgrid[0:h, 0:w][0].astype(np.float32) / h
    band = smoothstep(y0, y0 + 0.08, yy) * (1 - smoothstep(y1 - 0.06, y1, yy))
    a = smoothstep(1 - density, 1 - density + soft, n * band + band * 0.25)
    shadeT = smoothstep(0.4, 1.0, (1 - n)) * 0.55
    c = mix(np.broadcast_to(col, (h, w, 3)), np.broadcast_to(shade, (h, w, 3)), shadeT[..., None])
    return np.concatenate([c, (a * 255)[..., None]], -1)

def cloud_sea(w, h, col, shade, top, seed):
    n = fbm(h, w, 6, 3, seed)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    yy /= h
    edge = top + (n - 0.5) * 0.12 + 0.03 * np.sin(xx / w * 9.0 + seed)
    a = smoothstep(edge - 0.015, edge + 0.035, yy)
    shadeT = np.clip((yy - top) * 1.6, 0, 1) * 0.45 + (1 - n) * 0.25
    c = mix(np.broadcast_to(col, (h, w, 3)), np.broadcast_to(shade, (h, w, 3)), shadeT[..., None])
    return np.concatenate([c, (a * 255)[..., None]], -1)

def puff_clouds(w, h, base, shade, rows, seed, S=2):
    """Cel-shaded cartoon cumulus in the bible's manner: unions of round puffs, a lit cap and a hard shade boundary."""
    rng = np.random.default_rng(seed)
    M = Image.new("L", (w * S, h * S), 0); Lt = Image.new("L", (w * S, h * S), 0)
    dm, dl = ImageDraw.Draw(M), ImageDraw.Draw(Lt)
    for (yc, count, rmin, rmax, flat) in rows:
        for i in range(count):
            cx = rng.uniform(-0.05, 1.05) * w * S; r = rng.uniform(rmin, rmax) * h * S
            cy = yc * h * S + rng.uniform(-0.5, 0.2) * r
            dm.ellipse([cx - r, cy - r, cx + r, cy + r], fill=255)
            if flat: dm.rectangle([cx - r, cy, cx + r, cy + r * 3], fill=255)
            q = r * 0.86
            dl.ellipse([cx - r * 0.16 - q, cy - r * 0.24 - q, cx - r * 0.16 + q, cy - r * 0.24 + q], fill=255)
    m = np.asarray(M.resize((w, h), Image.LANCZOS)).astype(np.float32) / 255
    lt = np.asarray(Lt.resize((w, h), Image.LANCZOS)).astype(np.float32) / 255
    lit = np.minimum(m, lt)
    col = mix(np.broadcast_to(shade, (h, w, 3)), np.broadcast_to(base, (h, w, 3)), lit[..., None])
    yy = np.linspace(0, 1, h)[:, None, None]
    col = mix(col, np.broadcast_to(shade * 0.82, (h, w, 3)), np.clip((yy - 0.6) * 1.4, 0, 0.55))
    return np.concatenate([col, (m * 255)[..., None]], -1)

def islands_far(w, h, col, seed, count=7, y_lo=0.45, y_hi=0.7):
    """Distant floating islands as soft value shapes: flat mossy top, tapered stone underside (the bible silhouette)."""
    img = Image.new("L", (w * 2, h * 2), 0); d = ImageDraw.Draw(img)
    rng = np.random.default_rng(seed)
    for i in range(count):
        cx = (i + 0.5 + rng.uniform(-0.3, 0.3)) / count * w * 2
        cy = rng.uniform(y_lo, y_hi) * h * 2
        iw = rng.uniform(0.06, 0.13) * w * 2
        top = [(cx - iw / 2 + rng.uniform(-8, 8), cy + rng.uniform(-6, 6)) for _ in range(1)]
        pts = [(cx - iw / 2, cy)]
        for k in range(1, 6): pts.append((cx - iw / 2 + iw * k / 6, cy - rng.uniform(4, 22)))
        pts.append((cx + iw / 2, cy))
        depth = iw * rng.uniform(0.55, 0.95)
        pts += [(cx + iw * 0.33, cy + depth * 0.35), (cx + iw * 0.12, cy + depth * 0.7), (cx + rng.uniform(-0.06, 0.06) * iw, cy + depth),
                (cx - iw * 0.18, cy + depth * 0.6), (cx - iw * 0.38, cy + depth * 0.3)]
        d.polygon(pts, fill=255)
    m = np.asarray(img.resize((w, h), Image.LANCZOS)).astype(np.float32) / 255.0
    m = ndimage.gaussian_filter(m, 1.2)
    out = np.zeros((h, w, 4), np.float32); out[..., :3] = col; out[..., 3] = m * 255
    return out

def fg_rocks(w, h, side, seed):
    """Foreground framing rocks: near-black Void Navy silhouettes with a cool rim on the top edge."""
    S = 2
    img = Image.new("L", (w * S, h * S), 0); d = ImageDraw.Draw(img)
    rng = np.random.default_rng(seed)
    pts = [(0, h * S)]
    x = 0.0; y = h * S * rng.uniform(0.35, 0.5)
    while x < w * S:
        pts.append((x, y)); x += rng.uniform(40, 120) * S / 2; y += rng.uniform(-30, 55) * S / 2
        y = min(max(y, h * S * 0.18), h * S * 0.95)
    pts += [(w * S, h * S * 1.0), (w * S, h * S)]
    d.polygon(pts, fill=255)
    m = np.asarray(img.resize((w, h), Image.LANCZOS)).astype(np.float32) / 255.0
    taper = 1 - smoothstep(0.55, 1.0, np.linspace(0, 1, w))[None, :]
    m = m * taper
    if side == "r": m = m[:, ::-1]
    edge = np.clip(m - ndimage.uniform_filter(np.roll(m, 4, 0), 3), 0, 1)
    col = np.broadcast_to(rgb("navy") * 0.72, (h, w, 3)).copy()
    col = mix(col, np.broadcast_to(rgb("skyhor") * 0.75, (h, w, 3)), np.clip(edge * 3, 0, 1)[..., None])
    n = fbm(h, w, 4, 6, seed + 9)
    col = col * (0.9 + 0.2 * n[..., None])
    return np.concatenate([col, (m * 255)[..., None]], -1)

def crack_paths(w, h, seed=7):
    rng = np.random.default_rng(seed)
    origin = np.array([w * 0.56, h * 0.16])
    segs = []   # (p0, p1, dist0, width)
    def walk(p, ang, length, width, dist, depth):
        steps = int(length / 28)
        for i in range(steps):
            ang += rng.uniform(-0.55, 0.55)
            q = p + np.array([math.cos(ang), math.sin(ang)]) * rng.uniform(18, 40)
            segs.append((p.copy(), q.copy(), dist, width))
            dist += np.linalg.norm(q - p); p = q
            width *= 0.985
            if depth < 3 and rng.random() < 0.13:
                walk(p.copy(), ang + rng.choice([-1, 1]) * rng.uniform(0.6, 1.2), length * rng.uniform(0.25, 0.45), width * 0.55, dist, depth + 1)
            if p[0] < -60 or p[0] > w + 60 or p[1] < -60 or p[1] > h + 60: break
    walk(origin.copy(), math.pi + 0.15, w * 0.9, 13.0, 0.0, 0)
    walk(origin.copy(), -0.1, w * 0.85, 12.0, 0.0, 0)
    walk(origin.copy(), math.pi * 0.55, h * 0.5, 7.0, 0.0, 1)
    return segs

def crack_stage(w, h, segs, reach, S=2):
    core = Image.new("L", (w * S, h * S), 0); rim = Image.new("L", (w * S, h * S), 0)
    dc, dr = ImageDraw.Draw(core), ImageDraw.Draw(rim)
    for p0, p1, d0, wd in segs:
        if d0 > reach: continue
        q1 = p1
        seglen = np.linalg.norm(p1 - p0)
        if d0 + seglen > reach: q1 = p0 + (p1 - p0) * (reach - d0) / max(seglen, 1e-3)
        a = tuple((p0 * S).tolist()); b = tuple((q1 * S).tolist())
        dr.line([a, b], fill=255, width=max(2, int(wd * 2.2 * S)))
        dc.line([a, b], fill=255, width=max(1, int(wd * 0.9 * S)))
    c = np.asarray(core.resize((w, h), Image.LANCZOS)).astype(np.float32) / 255
    r = np.asarray(rim.resize((w, h), Image.LANCZOS)).astype(np.float32) / 255
    col = mix(np.broadcast_to(rgb("skyhor"), (h, w, 3)), np.broadcast_to(np.array([255, 253, 246.0]), (h, w, 3)), c[..., None])
    a = np.clip(np.maximum(r * 0.92, c), 0, 1)
    return np.concatenate([col, (a * 255)[..., None]], -1), np.maximum(r, c)

def light_shafts(w, h, origin, col, count=9, seed=3):
    S = 1
    img = Image.new("L", (w, h), 0); d = ImageDraw.Draw(img)
    rng = np.random.default_rng(seed)
    ox, oy = origin[0] * w, origin[1] * h
    for i in range(count):
        ang = math.radians(rng.uniform(35, 145)); spread = math.radians(rng.uniform(2.5, 6.0)); L = h * 1.6
        p1 = (ox + math.cos(ang - spread) * L, oy + math.sin(ang - spread) * L)
        p2 = (ox + math.cos(ang + spread) * L, oy + math.sin(ang + spread) * L)
        d.polygon([(ox, oy), p1, p2], fill=int(rng.uniform(90, 200)))
    m = np.asarray(img).astype(np.float32) / 255
    m = ndimage.gaussian_filter(m, 10)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    fall = np.exp(-(np.sqrt((xx - ox) ** 2 + (yy - oy) ** 2) / (h * 0.9)) ** 2)
    out = np.zeros((h, w, 4), np.float32); out[..., :3] = col; out[..., 3] = np.clip(m * fall, 0, 1) * 255
    return out

def speed_lines(w, h, radial=True, seed=5):
    S = 2
    img = Image.new("L", (w * S, h * S), 0); d = ImageDraw.Draw(img)
    rng = np.random.default_rng(seed)
    cx, cy = w * S / 2, h * S / 2
    for i in range(150 if radial else 90):
        if radial:
            ang = rng.uniform(0, 2 * math.pi); r0 = rng.uniform(0.42, 0.62) * h * S; r1 = h * S * 1.3; wd = rng.uniform(0.004, 0.012)
            pts = [(cx + math.cos(ang - wd) * r1, cy + math.sin(ang - wd) * r1), (cx + math.cos(ang) * r0, cy + math.sin(ang) * r0),
                   (cx + math.cos(ang + wd) * r1, cy + math.sin(ang + wd) * r1)]
            d.polygon(pts, fill=int(rng.uniform(140, 255)))
        else:
            x = rng.uniform(0, w * S); y0 = rng.uniform(-0.2, 0.7) * h * S; L = rng.uniform(0.2, 0.6) * h * S; wd = rng.uniform(1.5, 5) * S
            d.polygon([(x - wd, y0), (x + wd, y0), (x, y0 + L)], fill=int(rng.uniform(120, 230)))
    m = np.asarray(img.resize((w, h), Image.LANCZOS)).astype(np.float32) / 255
    out = np.zeros((h, w, 4), np.float32); out[..., :3] = 255; out[..., 3] = m * 255
    return out

def sprite_dot(n, hard=0.35):
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
    d = np.sqrt((xx - n / 2 + 0.5) ** 2 + (yy - n / 2 + 0.5) ** 2) / (n / 2)
    a = np.clip(1 - smoothstep(hard, 1.0, d), 0, 1)
    out = np.zeros((n, n, 4), np.float32); out[..., :3] = 255; out[..., 3] = a * 255
    return out

def sprite_ember(n):
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
    u, v = (xx - n / 2 + 0.5) / (n / 2), (yy - n / 2 + 0.5) / (n / 2)
    d = np.abs(u) * 1.0 + np.abs(v) * 0.55
    a = np.clip(1 - smoothstep(0.35, 1.0, d), 0, 1)
    out = np.zeros((n, n, 4), np.float32); out[..., :3] = 255; out[..., 3] = a * 255
    return out

def sweep_band(w, h):
    x = np.linspace(-1, 1, w)[None, :]
    a = np.exp(-(x / 0.28) ** 2) * 0.9 + np.exp(-(x / 0.06) ** 2) * 0.35
    out = np.zeros((h, w, 4), np.float32); out[..., :3] = 255; out[..., 3] = np.clip(a, 0, 1) * 255 * np.ones((h, 1))
    return out

def vignette(w, h, col, strength=0.8):
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    d = np.sqrt(((xx / w - 0.5) * 1.15) ** 2 + ((yy / h - 0.5) * 1.0) ** 2) / 0.72
    a = smoothstep(0.55, 1.15, d) * strength
    out = np.zeros((h, w, 4), np.float32); out[..., :3] = col; out[..., 3] = a * 255
    return out

def fade_top(arr, y0, y1):
    h = arr.shape[0]
    yy = np.arange(h)[:, None].astype(np.float32) / h
    a = smoothstep(y0, y1, yy)
    if arr.shape[2] == 3:
        arr = np.concatenate([arr, np.full(arr.shape[:2] + (1,), 255, np.float32)], -1)
    arr = arr.copy(); arr[..., 3] *= a
    return arr

def edge_soften(arr, px):
    h, w = arr.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    e = np.minimum(np.minimum(xx, w - 1 - xx), np.minimum(yy, h - 1 - yy))
    a = smoothstep(0, px, e)
    if arr.shape[2] == 3: arr = np.concatenate([arr, np.full(arr.shape[:2] + (1,), 255, np.float32)], -1)
    arr = arr.copy(); arr[..., 3] *= a
    return arr

# ---------------------------------------------------------------- build
def egg_cracks(egg):
    """Four stages of the egg splitting: glowing cracks drawn inside the egg's own silhouette (alpha-masked), same size."""
    h, w = egg.shape[:2]
    inside = ndimage.binary_erosion(egg[..., 3] > 200, iterations=int(w * 0.04))
    ys, xs = np.where(inside)
    rng = np.random.default_rng(99)
    S = 2
    segs = []
    def walk(p, ang, length, width, dist, depth):
        for i in range(int(length / 10)):
            ang += rng.uniform(-0.5, 0.5)
            q = p + np.array([math.cos(ang), math.sin(ang)]) * rng.uniform(7, 15)
            segs.append((p.copy(), q.copy(), dist, width)); dist += np.linalg.norm(q - p); p = q; width *= 0.97
            if depth < 2 and rng.random() < 0.12:
                walk(p.copy(), ang + rng.choice([-1, 1]) * rng.uniform(0.7, 1.2), length * 0.35, width * 0.6, dist, depth + 1)
    top = np.array([xs.mean(), ys.min() + (ys.max() - ys.min()) * 0.12])
    walk(top.copy(), math.pi / 2 + 0.25, h * 0.9, 9.0, 0, 0)
    walk(top.copy(), math.pi / 2 - 0.5, h * 0.55, 7.0, 30, 1)
    walk(np.array([xs.mean() - w * 0.1, ys.mean()]), math.pi + 0.2, w * 0.45, 6.0, 120, 1)
    walk(np.array([xs.mean() + w * 0.05, ys.mean() + h * 0.05]), -0.15, w * 0.45, 6.0, 150, 1)
    total = max(sg[2] for sg in segs) + 10
    for k, r in enumerate([0.22, 0.45, 0.72, 1.0]):
        core = Image.new("L", (w * S, h * S), 0); rim = Image.new("L", (w * S, h * S), 0)
        dc, dr = ImageDraw.Draw(core), ImageDraw.Draw(rim)
        for p0, p1, d0, wd in segs:
            if d0 > r * total: continue
            a, b = tuple((p0 * S).tolist()), tuple((p1 * S).tolist())
            dr.line([a, b], fill=255, width=max(2, int(wd * 2.4 * S))); dc.line([a, b], fill=255, width=max(1, int(wd * 0.9 * S)))
        c = np.asarray(core.resize((w, h), Image.LANCZOS)).astype(np.float32) / 255 * inside
        m = np.asarray(rim.resize((w, h), Image.LANCZOS)).astype(np.float32) / 255 * inside
        glow = np.clip(ndimage.gaussian_filter(np.maximum(m, c), 6) * 2.5, 0, 1) * (egg[..., 3] / 255)
        col = mix(np.broadcast_to(rgb("biomass"), (h, w, 3)), np.broadcast_to(np.array([255, 255, 240.0]), (h, w, 3)), c[..., None])
        col = mix(col, np.broadcast_to(rgb("navy"), (h, w, 3)), np.clip(m - c, 0, 1)[..., None] * 0.6)
        a = np.clip(np.maximum(np.maximum(m * 0.95, c), glow * 0.65), 0, 1)
        save("egg_crack_%d" % (k + 1), np.concatenate([col, (a * 255)[..., None]], -1), "p10 egg + procedural", "egg splitting, stage %d of 4 (masked to the egg)" % (k + 1))

def cutout(pages, name, pg, box, k, source, glow=None, swap=False, keep_largest=0, min_area=900, note=""):
    P = page(pages, pg)
    x0, y0, x1, y1 = box
    rgba = trim(cut(P[y0:y1, x0:x1], min_area=min_area, keep_largest=keep_largest))
    rgba = upscale(pad(rgba, 4), k)
    if swap != "only": save(name, rgba, source, note)
    if swap: save(name + "_crimson", team_swap(rgba), source, "accent swap Azure -> Crimson (bible p06 item 07 rule)")
    if glow:
        lo, hi, col, rad = glow
        save(name + "_glow", glow_from(rgba, lo, hi, col, radius=rad * k), source, "seam / crystal rim glow")
    return rgba

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--pdf", default=PDF); args = ap.parse_args()
    if not os.path.exists(args.pdf): sys.exit("bible PDF not found: " + args.pdf)
    pages = extract_pages(args.pdf)
    for f in glob.glob(os.path.join(OUT, "*.png")): os.remove(f)

    # ---- real art: paintings
    p3 = page(pages, 3)
    basin = p3[9:628, 290:1250]                               # moodboard central painting, 960 x 619
    basin2 = upscale_rgb(basin, 2.0)
    low = fade_top(basin2[int(0.36 * basin2.shape[0]):], 0.0, 0.16)   # the land only: arena, waterfalls (no shrine, no sky)
    save("basin", low, "p03 moodboard central painting, lower 64% x2, top edge feathered")
    shrine = p3[9 + 10:9 + 300, 290 + 300:290 + 680]            # shrine arch with the egg and the sky beam
    shrine3 = upscale_rgb(shrine, 3.0)
    save("shrine", shrine3, "p03 moodboard central, shrine crop x3")
    save_blur("shrine", shrine3, 14, "p03")
    save("shrine_glow", glow_from(np.concatenate([shrine3, np.full(shrine3.shape[:2] + (1,), 255, np.float32)], -1), 60, 175, "seam",
                                  sat=0.5, val=0.7, radius=16, gain=0.8), "p03", "egg and beam glow, aligned to shrine")
    p1 = page(pages, 1)
    hill = p1[262:766, 522:1376]                                # cover key art: the hill battle, HUD-free middle
    hill2 = upscale_rgb(hill, 2.0)
    save("hill", hill2, "p01 cover key art middle crop x2")
    save_blur("hill", hill2, 12, "p01")

    # ---- real art: cut-outs (box = native page pixels)
    teal = (165, 200, "seam", 10)
    lime = (65, 110, "biomass", 9)
    egg = cutout(pages, "egg", 10, (14, 318, 178, 492), 3.0, "p10 egg", glow=lime, keep_largest=1)
    egg_cracks(bleed(egg))
    cutout(pages, "hatchling", 10, (186, 288, 432, 500), 2.5, "p10 hatchling", glow=lime, keep_largest=1)
    cutout(pages, "kaiju_neutral", 10, (440, 56, 828, 500), 2.0, "p10 neutral adult", glow=teal, keep_largest=1)
    cutout(pages, "kaiju_crimson", 10, (826, 56, 1162, 500), 2.0, "p10 Crimson-aligned adult", glow=(0, 40, "orange", 10), keep_largest=1)
    cutout(pages, "kaiju_berserk", 10, (1160, 30, 1536, 512), 2.0, "p10 berserk adult", glow=(270, 320, "corruption", 10), keep_largest=0, min_area=400)
    cutout(pages, "warrior", 6, (15, 71, 448, 541), 2.0, "p06 Warrior front", swap=True, keep_largest=1)
    cutout(pages, "warrior_cleave", 6, (9, 563, 590, 948), 2.0, "p06 Warrior pose (cleave)", swap="only", keep_largest=0, min_area=1500)
    cutout(pages, "warrior_charge", 6, (587, 601, 1093, 958), 2.0, "p06 Warrior pose (shield charge)", swap=False, keep_largest=0, min_area=1500)
    cutout(pages, "farmer", 7, (7, 9, 368, 532), 2.0, "p07 Farmer front", swap=True, glow=lime, keep_largest=1)
    cutout(pages, "farmer_harvest", 7, (0, 549, 486, 957), 2.0, "p07 Farmer pose (harvest)", swap=False, glow=lime, keep_largest=0, min_area=1500)
    cutout(pages, "miner", 8, (15, 30, 434, 435), 2.0, "p08 Miner front", swap=True, keep_largest=1)
    cutout(pages, "banner_azure", 14, (873, 522, 1034, 763), 2.5, "p14 Azure banner", keep_largest=1)
    cutout(pages, "banner_crimson", 14, (1038, 524, 1208, 765), 2.5, "p14 Crimson banner", keep_largest=1)
    cutout(pages, "biomass_plants", 14, (9, 14, 639, 318), 1.6, "p14 biomass plants", glow=lime, min_area=600)
    cutout(pages, "corruption_growth", 14, (755, 40, 1526, 326), 1.6, "p14 corruption growth", glow=(270, 330, "corruption", 9), min_area=600)
    cutout(pages, "orb", 14, (627, 155, 774, 310), 2.0, "p14 loose biomass orb", glow=lime, keep_largest=1)

    # ---- procedural backdrops (bible palette), stored small where they are smooth
    W, H = 1280, 720
    save("sky_day", sky(W, H, rgb("skytop") * 0.92, rgb("skytop"), rgb("skyhor"), sun=(0.62, 0.22), sun_r=0.45, sun_col=rgb("cream"),
                        clouds=0.55, cloud_col=rgb("cream"), seed=11), "procedural", "Sky Top -> Sky Horizon, cream sun, soft cloud")
    save("sky_storm", sky(W, H, rgb("navy") * 1.1, mix(rgb("panel"), rgb("stone"), 0.35), mix(rgb("stone"), rgb("skyhor"), 0.35),
                          sun=(0.56, 0.16), sun_r=0.35, sun_col=mix(rgb("skyhor"), rgb("cream"), 0.5), clouds=0.6,
                          cloud_col=mix(rgb("stone"), rgb("navy"), 0.4), seed=12), "procedural", "darkened sky under the crack")
    night = sky(W, H, rgb("navy") * 0.85, rgb("navy"), mix(rgb("panel"), rgb("seam"), 0.18), sun=(0.42, 0.58), sun_r=0.4,
                sun_col=mix(rgb("panel"), rgb("seam"), 0.45), clouds=0.25, cloud_col=rgb("panel"), seed=13)
    stars = (RNG.random((H, W)) > 0.9985).astype(np.float32)
    stars = np.clip(ndimage.gaussian_filter(stars, 0.7) * 9, 0, 1) * (1 - smoothstep(0.45, 0.8, np.linspace(0, 1, H)))[:, None]
    night = mix(night, np.broadcast_to(rgb("cream"), night.shape), stars[..., None])
    save("sky_night", night, "procedural", "Void Navy night, cream stars, teal ground glow")
    yy = np.linspace(0, 1, W)[None, :]
    left = sky(W, H, mix(rgb("navy"), rgb("azure"), 0.35), mix(rgb("azure"), rgb("skyhor"), 0.3), rgb("skyhor"), seed=14, clouds=0.3, cloud_col=rgb("cream"))
    right = sky(W, H, mix(rgb("navy"), rgb("crimson"), 0.35), mix(rgb("crimson"), rgb("cream"), 0.3), rgb("cream"), seed=15, clouds=0.3, cloud_col=rgb("cream"))
    xx, yyy = np.meshgrid(np.linspace(0, 1, W), np.linspace(0, 1, H))
    seam = smoothstep(-0.02, 0.02, xx - 0.5 - (yyy - 0.5) * 0.18)
    split = mix(left, right, seam[..., None])
    line = np.exp(-((xx - 0.5 - (yyy - 0.5) * 0.18) / 0.004) ** 2)
    split = mix(split, np.broadcast_to(rgb("cream"), split.shape), np.clip(line * 0.9, 0, 1)[..., None])
    save("sky_split", split, "procedural", "Azure | Crimson split, diagonal cream seam")
    bm = sky(W, H, mix(rgb("navy"), rgb("biomass"), 0.25), mix(rgb("biomass"), rgb("navy"), 0.45), mix(rgb("biomass"), rgb("cream"), 0.3), seed=16)
    co = sky(W, H, mix(rgb("navy"), rgb("corruption"), 0.3), mix(rgb("corruption"), rgb("navy"), 0.35), mix(rgb("corruption"), rgb("crimson"), 0.35), seed=17)
    seam2 = smoothstep(-0.06, 0.06, xx - 0.5)
    save("sky_choice", mix(bm, co, seam2[..., None]), "procedural", "Biomass | Corruption split")
    save("sky_warm", sky(W, H, mix(rgb("skytop"), rgb("navy"), 0.25), mix(rgb("orange"), rgb("cream"), 0.45), rgb("cream"),
                         sun=(0.5, 0.62), sun_r=0.32, sun_col=np.array([255, 246, 228.0]), clouds=0.4, cloud_col=mix(rgb("orange"), rgb("cream"), 0.6), seed=18),
         "procedural", "late-afternoon hero sky")
    sea_rows = [(0.42, 16, 0.07, 0.13, True), (0.6, 18, 0.09, 0.16, True), (0.8, 16, 0.12, 0.2, True)]
    save("cloudsea", puff_clouds(1600, 540, rgb("cream"), mix(rgb("skyhor"), rgb("stone"), 0.38), sea_rows, 23), "procedural", "cloud sea, lower frame")
    save("cloudsea_warm", puff_clouds(1600, 540, mix(rgb("cream"), rgb("orange"), 0.18), mix(rgb("orange"), rgb("stone"), 0.5), sea_rows, 24), "procedural")
    save("cloudsea_storm", puff_clouds(1600, 540, mix(rgb("stone"), rgb("skyhor"), 0.35), mix(rgb("navy"), rgb("stone"), 0.45), sea_rows, 25), "procedural")
    save("rocks_l", fg_rocks(900, 640, "l", 41), "procedural", "foreground framing rock, left")
    save("rocks_r", fg_rocks(900, 640, "r", 42), "procedural", "foreground framing rock, right")
    segs = crack_paths(1920, 1080)
    total = max(s[2] for s in segs) + 40
    reach = [0.05, 0.13, 0.26, 0.44, 0.68, 1.0]
    last = None
    for i, r in enumerate(reach):
        layer, m = crack_stage(1920, 1080, segs, r * total)
        save("crack_%d" % (i + 1), layer, "procedural", "spreading crack, stage %d of 6" % (i + 1))
        last = m
    g = ndimage.gaussian_filter(np.asarray(Image.fromarray((last * 255).astype(np.uint8)).resize((960, 540), Image.LANCZOS)).astype(np.float32) / 255, 9) * 3.2
    cg = np.zeros((540, 960, 4), np.float32); cg[..., :3] = mix(rgb("skyhor"), rgb("cream"), 0.5); cg[..., 3] = np.clip(g, 0, 1) * 255
    save("crack_glow", cg, "procedural", "light pouring through the crack")
    save("shafts", light_shafts(960, 540, (0.56, 0.16), mix(rgb("cream"), rgb("skyhor"), 0.4)), "procedural", "light shafts from the crack")
    save("shafts_warm", light_shafts(960, 540, (0.5, 0.6), np.array([255, 240, 214.0]), seed=4), "procedural")
    save("speed_radial", speed_lines(1280, 720, True), "procedural", "radial speed lines")
    save("speed_down", speed_lines(1280, 720, False), "procedural", "falling speed lines")
    save("mote", sprite_dot(48, 0.25), "procedural", "soft dot (dust motes, sparks)")
    save("ember", sprite_ember(48), "procedural", "ember / shard")
    save("sweep", sweep_band(512, 16), "procedural", "light sweep band")
    save("vignette", vignette(960, 540, rgb("navy"), 0.85), "procedural", "Void Navy vignette")
    ring = sprite_dot(256, 0.0); yy2, xx2 = np.mgrid[0:256, 0:256]; dd = np.sqrt((xx2 - 127.5) ** 2 + (yy2 - 127.5) ** 2) / 128
    ring[..., 3] = np.clip(np.exp(-((dd - 0.82) / 0.07) ** 2), 0, 1) * 255
    save("ring", ring, "procedural", "impact shock ring")

    with open(os.path.join(HERE, "layers-manifest.tsv"), "w") as f:
        f.write("name\twidth\theight\tsource\tnote\n")
        for n, sz, s, note in MANIFEST: f.write("%s\t%d\t%d\t%s\t%s\n" % (n, sz[0], sz[1], s, note))
    tot = sum(os.path.getsize(p) for p in glob.glob(os.path.join(OUT, "*.png")))
    print("layers", len(MANIFEST), "total MB %.1f" % (tot / 1e6))

if __name__ == "__main__":
    main()
