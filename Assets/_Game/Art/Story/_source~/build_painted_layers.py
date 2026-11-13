#!/usr/bin/env python3
"""Replace the story intro's soft bible crops and procedural backdrops with NEW full-resolution paintings made in the
bible's style (image model guided by the artist's own pages; masters kept as high-quality JPG, see --src).
Run AFTER build_story_layers.py (which writes the procedural effects layers this script keeps), then write_metas.py:

    python3 build_story_layers.py && python3 build_painted_layers.py [--src MASTERS] && python3 make_shots.py && python3 write_metas.py

Masters: ~/Obsidian/Wilson-Brain/Projects/Kaiju-Demo/unity-port/story-paintings-v1/masters/ (outside the repo: 30 MB).

Opaque plates are written as JPG (paintings compress 6x better than PNG; Unity imports them the same way, and
Resources.Load<Texture2D> ignores the extension); any PNG of the same name is removed so a name never resolves twice.
Cut-outs: subjects were painted on a flat key colour (magenta or green); the bible cutter's colour-distance matte
(cut() in build_story_layers.py) separates them, then a navy-ink hull removes any glow halo outside the outline.
Glow passes are pulled from the subject's own crystal or seam hue, the same way as before. Deterministic."""
import json, math, os, sys
import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import build_story_layers as B   # noqa: E402  (helpers only: cut, trim, bleed, mult4, glow_from, light_shafts, ...)

# masters (the generated paintings, high-quality JPG) live outside the repo to keep the art budget: pass --src
SRC = next((a for a in [sys.argv[sys.argv.index("--src") + 1] if "--src" in sys.argv else "",
                        os.path.join(HERE, "paintings"),
                        os.path.expanduser("~/Obsidian/Wilson-Brain/Projects/Kaiju-Demo/unity-port/story-paintings-v1/masters")]
            if a and os.path.isdir(a)), os.path.join(HERE, "paintings"))
OUT = B.OUT
GEOM = os.path.join(HERE, "painted-geometry.json")   # measured points make_shots.py reads (crack origin, egg in shrine)
ROWS = []

def load(name):
    return np.asarray(Image.open(os.path.join(SRC, name + ".jpg")).convert("RGB")).astype(np.float32)

def fit(arr, max_w=0, max_h=0):
    h, w = arr.shape[:2]
    k = min((max_w / w) if max_w else 9, (max_h / h) if max_h else 9, 1.0)
    if k >= 0.999: return arr
    W, H = int(round(w * k)), int(round(h * k))
    if arr.shape[2] == 4:
        a = arr[..., 3:4] / 255.0
        pre = np.concatenate([arr[..., :3] * a, arr[..., 3:4]], -1)
        img = Image.fromarray(np.clip(pre, 0, 255).astype(np.uint8), "RGBA").resize((W, H), Image.LANCZOS)
        p = np.asarray(img).astype(np.float32); A = p[..., 3:4] / 255.0
        return np.concatenate([p[..., :3] / np.maximum(A, 1e-3), p[..., 3:4]], -1)
    return np.asarray(Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGB").resize((W, H), Image.LANCZOS)).astype(np.float32)

def save_png(name, rgba, source, note=""):
    img = B.save(name, rgba, source, note)
    j = os.path.join(OUT, name + ".jpg")
    if os.path.exists(j): os.remove(j)
    ROWS.append((name, img.size, source, note)); return img

def save_jpg(name, rgb, source, note="", q=90):
    rgb = B.mult4(np.clip(rgb, 0, 255))
    img = Image.fromarray(rgb.astype(np.uint8), "RGB")
    img.save(os.path.join(OUT, name + ".jpg"), quality=q, subsampling=0, optimize=True)
    p = os.path.join(OUT, name + ".png")
    if os.path.exists(p): os.remove(p)
    ROWS.append((name, img.size, source, note)); return img

def save_blur_jpg(name, rgb, radius, source):
    h, w = rgb.shape[:2]
    img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), "RGB").resize((w // 2, h // 2), Image.LANCZOS)
    save_jpg(name + "_blur", np.asarray(img.filter(ImageFilter.GaussianBlur(radius / 2))).astype(np.float32), source,
             "depth-of-field copy, half size", q=85)

def ink_hull(rgb, alpha, close=4):
    """Filled region inside the navy ink outline (drops a glow halo the painter put around the subject). Falls back to
    the plain matte if the outline does not close (hull much smaller than the matte)."""
    lum = rgb @ np.array([0.299, 0.587, 0.114], np.float32)
    ink = ndimage.binary_closing((lum < 85) & (alpha > 0.5), iterations=close)
    hull = ndimage.binary_dilation(ndimage.binary_fill_holes(ink), iterations=2)
    solid = alpha > 0.5
    if hull.sum() < 0.55 * solid.sum(): return solid
    return hull

def purge(rgba, key_green):
    """Key-colour contamination inside a subject (light painted over the key): magenta tints become warm cream-gold,
    pure-green tints become neutral. The subjects' own palettes never use these hues."""
    h, s, v = B.hsv(rgba)
    win = ((h > 105) & (h < 145) & (s > 0.45)) if key_green else ((h > 290) & (h < 342) & (s > 0.28))
    if not win.any(): return rgba
    nh = np.where(win, 48.0, h); ns = np.where(win, s * 0.55, s)
    out = rgba.copy(); out[..., :3] = np.where(win[..., None], B.hsv2rgb(nh, ns, v), rgba[..., :3]); return out

def keycolour(arr):
    b = np.concatenate([arr[:3].reshape(-1, 3), arr[-3:].reshape(-1, 3), arr[:, :3].reshape(-1, 3), arr[:, -3:].reshape(-1, 3)])
    sat = b.max(1) - b.min(1)
    return np.median(b[sat > 150], 0) if (sat > 150).sum() > 50 else np.median(b, 0)

def cutout(src, zero=(), halo=False, keep_largest=0, min_area=1500, hole_area=600, drop_touching=False):
    """src: RGB painted on a flat key colour. zero: list of (x0, y0, x1, y1) fractions forced to the key colour first
    (drops stray marks the model left in a corner). Returns straight RGBA, trimmed."""
    arr = src.copy(); h, w = arr.shape[:2]
    key = keycolour(arr)
    for x0, y0, x1, y1 in zero: arr[int(y0 * h):int(y1 * h), int(x0 * w):int(x1 * w)] = key
    rgba = B.cut(arr, min_area=min_area, keep_largest=keep_largest, hole_area=hole_area)
    if halo:
        a = rgba[..., 3] / 255.0
        hull = ndimage.gaussian_filter(ink_hull(rgba[..., :3], a).astype(np.float32), 1.0)
        rgba[..., 3] = rgba[..., 3] * np.clip(hull * 1.2, 0, 1)
    if drop_touching and zero:   # anything cut flat by a cleared band goes too (no straight edges left behind)
        lab, n = ndimage.label(rgba[..., 3] > 20)
        band = np.zeros((h, w), bool)
        for x0, y0, x1, y1 in zero:
            band[max(0, int(y0 * h) - 1):min(h, int(y1 * h) + 4), max(0, int(x0 * w) - 1):min(w, int(x1 * w) + 1)] = True
        bad = np.unique(lab[band & (lab > 0)])
        rgba[..., 3] *= ~np.isin(lab, bad)
    rgba = purge(rgba, key[1] > key[0])
    # despill: pull any remaining key-hued edge pixels toward neutral
    a = rgba[..., 3:4] / 255.0
    edge = (a > 0.02) & (a < 0.9)
    if key[1] > key[0]:   # green key
        g = rgba[..., 1:2]; lim = np.maximum(rgba[..., 0:1], rgba[..., 2:3])
        rgba[..., 1:2] = np.where(edge, np.minimum(g, lim + 10), g)
    else:                 # magenta key
        lim = rgba[..., 1:2] + 10
        rgba[..., 0:1] = np.where(edge, np.minimum(rgba[..., 0:1], np.maximum(lim, rgba[..., 0:1] * 0.6)), rgba[..., 0:1])
        rgba[..., 2:3] = np.where(edge, np.minimum(rgba[..., 2:3], np.maximum(lim, rgba[..., 2:3] * 0.6)), rgba[..., 2:3])
    return B.trim(rgba, 8)

def light_from_black(rgb, gamma=0.8):
    """A painting of light on black -> straight-alpha light layer (alpha = brightness)."""
    m = rgb.max(-1) / 255.0
    a = np.clip((m - 0.04) / 0.96, 0, 1) ** gamma
    col = np.clip(rgb / np.maximum(m, 1e-3)[..., None], 0, 255)
    return np.concatenate([col, (a * 255)[..., None]], -1)

def brightest(rgb, sigma=30):
    lum = ndimage.gaussian_filter(rgb.mean(-1), sigma)
    y, x = np.unravel_index(np.argmax(lum), lum.shape)
    return x / rgb.shape[1], y / rgb.shape[0]

def opaque(rgba): return np.concatenate([rgba[..., :3], rgba[..., 3:4]], -1)

def place(rgba, W, H, box):
    """Paste rgba into a W x H transparent canvas at the box's top-left (box from a common crop)."""
    out = np.zeros((H, W, 4), np.float32); y0, x0 = box
    h, w = rgba.shape[:2]; out[y0:y0 + h, x0:x0 + w] = rgba[:H - y0, :W - x0]; return out

def main():
    geom = {}
    # ---- skies and plates (opaque, JPG)
    save_jpg("sky_day", fit(load("sky_day"), 2560), "painted plate: calm day sky over a cloud sea")
    storm = load("sky_storm")
    save_jpg("sky_storm", fit(storm, 2560), "painted plate: storm sky spiralling round the break point")
    shrine = fit(load("shrine_night"), 2560)
    save_jpg("shrine", shrine, "painted plate: the shrine at night, the egg in its arch")
    save_blur_jpg("shrine", shrine, 14, "painted plate")
    sh = np.concatenate([shrine, np.full(shrine.shape[:2] + (1,), 255, np.float32)], -1)
    save_png("shrine_glow", B.glow_from(sh, 62, 110, "biomass", sat=0.55, val=0.75, radius=18, gain=0.7), "painted plate", "egg seam glow, aligned to shrine")
    ex, ey = brightest(shrine * (np.abs(B.hsv(sh)[0] - 82) < 25)[..., None], 24)
    geom["egg_in_shrine_uv"] = [round(ex, 4), round(ey, 4)]
    for name, src, note in [("sky_split", "plate_peoples", "two camps under a teal and coral sky, the hill between"),
                            ("hill", "plate_hill", "the hill battle, closer and lower than the cover"),
                            ("hero", "plate_hero", "sunset hero plate, the Kaiju looming behind the hill"),
                            ("sky_choice", "plate_choice", "biomass garden | corrupted island split")]:
        if not os.path.exists(os.path.join(SRC, src + ".jpg")): continue
        p = fit(load(src), 2560); save_jpg(name, p, "painted plate", note)
        if name in ("hill", "hero"): save_blur_jpg(name, p, 12, "painted plate")

    # ---- the crack: one painted crack of light, revealed in six stages from its burst point
    if os.path.exists(os.path.join(SRC, "tear.jpg")):   # round 2: the sky tears open (painted rip + light, on magenta)
        crack = fit(cutout(load("tear"), min_area=400, hole_area=200000), 1600)
        crack = place(crack, crack.shape[1], crack.shape[0], (0, 0))
    else:
        crack = light_from_black(fit(load("crack"), 1600))
    h0, w0 = crack.shape[:2]   # fade every border so the overlay never shows a cut edge
    fy = np.clip(np.minimum(np.arange(h0), h0 - 1 - np.arange(h0)) / (0.07 * h0), 0, 1)
    fx = np.clip(np.minimum(np.arange(w0), w0 - 1 - np.arange(w0)) / (0.07 * w0), 0, 1)
    crack[..., 3] *= (fy[:, None] * fx[None, :]) ** 0.8
    ox, oy = brightest(crack[..., :3] * (crack[..., 3:4] / 255.0), 20)
    geom["crack_origin_uv"] = [round(ox, 4), round(oy, 4)]
    h, w = crack.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    d = np.sqrt((xx - ox * w) ** 2 + (yy - oy * h) ** 2); dmax = d[crack[..., 3] > 20].max()
    for i, reach in enumerate([0.10, 0.22, 0.38, 0.56, 0.78, 1.02], 1):
        st = crack.copy(); st[..., 3] *= np.clip((reach * dmax - d) / (0.05 * dmax), 0, 1)
        save_png("crack_%d" % i, st, "painted crack of light", "revealed from the burst point, stage %d of 6" % i)
    lum = crack[..., :3].mean(-1) / 255.0   # glow only from the light pouring through, not the torn cloud flaps
    g = crack[..., 3] / 255.0 * np.clip((lum - 0.72) / 0.2, 0, 1) * 0.8
    g = ndimage.gaussian_filter(fit(np.repeat(g[..., None], 3, -1) * 255, 960)[..., 0] / 255.0, 14) * 1.6
    glow = np.zeros(g.shape + (4,), np.float32); glow[..., :3] = B.rgb("seam") * 0.5 + 0.5 * np.array([247, 240, 229.0]); glow[..., 3] = np.clip(g, 0, 1) * 255
    save_png("crack_glow", glow, "painted crack of light", "soft glow, aligned to the crack")
    save_png("shafts", B.light_shafts(960, 540, (ox, oy), B.rgb("cream"), count=11), "procedural", "light shafts from the painted crack's burst point")

    # ---- land, debris, framing rocks
    basin = cutout(load("basin"))
    save_png("basin", fit(basin, 2400), "painted cut-out: Skybreak Basin before the banners")
    deb = cutout(load("debris"), zero=[(0, 0, 1, 0.40)], min_area=600, drop_touching=True)   # reference scraps in the top band
    save_png("debris", fit(deb, 1920), "painted cut-out: rising rock debris (parallax overlay)")
    rocks = cutout(load("rocks_fg"), keep_largest=2)
    hv = B.hsv(rocks); rocks[..., 3] *= ~((hv[0] > 250) & (hv[0] < 340) & (hv[1] > 0.25))   # stray model marks (no purple in rock)
    hh, ww = rocks.shape[:2]; mid = ww // 2
    save_png("rocks_l", fit(B.trim(rocks[:, :mid].copy(), 6), 1200), "painted cut-out: foreground cliff, left")
    save_png("rocks_r", fit(B.trim(rocks[:, mid:].copy(), 6), 1200), "painted cut-out: foreground pillar, right")

    # ---- the egg and its hatch, on one shared canvas so every frame lines up
    eggs = {}
    for n, zero in [("egg", ()), ("hatch_a", ()), ("hatch_b", ()), ]:
        if os.path.exists(os.path.join(SRC, n + ".jpg")):
            src = load(n)
            rgba = B.cut(src, min_area=800, hole_area=900)
            a = rgba[..., 3] / 255.0
            hull = ink_hull(rgba[..., :3], a)
            if n == "hatch_b" and "egg" in eggs:   # the burst breaks the outline: the egg's own (eroded) matte stays solid too
                hull |= ndimage.binary_erosion(eggs["egg"][..., 3] > 128, iterations=12) & (a > 0.5)
            rgba[..., 3] *= np.clip(ndimage.gaussian_filter(hull.astype(np.float32), 1.0) * 1.2, 0, 1)
            eggs[n] = purge(rgba, False)
    if "hatch_a" in eggs:   # the burst of light breaks the outline, so the aligned egg's own silhouette is the hull
        m = ndimage.binary_erosion(eggs["egg"][..., 3] > 128, iterations=12).astype(np.float32)   # the egg matte carries its own thin rim glow
        eggs["hatch_a"][..., 3] *= ndimage.gaussian_filter(m, 1.0)
    ys, xs = np.where(sum(e[..., 3] for e in eggs.values()) > 8)
    pad_all = 70   # room for the seam glow on the shared canvas
    eggs = {k: B.pad(v, pad_all) for k, v in eggs.items()}; ys, xs = ys + pad_all, xs + pad_all
    y0, y1, x0, x1 = ys.min() - 60, ys.max() + 61, xs.min() - 60, xs.max() + 61
    egg_ys, egg_xs = np.where(eggs["egg"][..., 3] > 8)   # (already in padded coordinates)
    geom["egg_canvas"] = dict(w=int(x1 - x0), h=int(y1 - y0), egg_w=int(egg_xs.max() - egg_xs.min()),
                              egg_cx=float((egg_xs.mean() - x0) / (x1 - x0)), egg_cy=float((egg_ys.mean() - y0) / (y1 - y0)))
    crop = {k: fit(v[y0:y1, x0:x1], 1024) for k, v in eggs.items()}
    save_png("egg", crop["egg"], "painted cut-out: the Kaiju egg", "shared hatch canvas")
    save_png("egg_glow", B.glow_from(crop["egg"], 62, 110, "biomass", sat=0.5, val=0.6, radius=14), "painted egg", "seam glow")
    a, b = crop.get("hatch_a", crop["egg"]), crop.get("hatch_b", crop["egg"])
    bright = a.copy(); hv = B.hsv(a); m = ((np.abs(hv[0] - 75) < 30) & (hv[1] > 0.3)).astype(np.float32)
    bright[..., :3] = np.clip(a[..., :3] * (1 + 0.35 * m[..., None]) + 30 * m[..., None], 0, 255)
    for i, fr in enumerate([a, bright, b, b], 1):
        save_png("egg_crack_%d" % i, fr, "painted hatch frames", "hatch stage %d of 4 (full egg, shared canvas)" % i)
    hat = cutout(load("hatch_c"), zero=[(0, 0.55, 0.12, 1.0), (0.88, 0.55, 1.0, 1.0)], halo=True, keep_largest=0, min_area=900)
    hat = fit(hat, 1100, 1100)
    save_png("hatchling", hat, "painted cut-out: the hatchling in its shell")
    save_png("hatchling_glow", B.glow_from(hat, 62, 110, "biomass", sat=0.5, val=0.6, radius=14), "painted hatchling", "seam glow")

    # ---- Kaiju, three alignments, one pose
    for n, hue, col, zero in [("kaiju_neutral", (160, 200), "seam", []),      # the bible's reptile: the tail is canon
                              ("kaiju_crimson", (0, 32), "crimson", []),
                              ("kaiju_berserk", (265, 320), "corruption", [])]:
        if not os.path.exists(os.path.join(SRC, n + ".jpg")): continue
        k = fit(cutout(load(n), zero=zero, halo=True), 1280, 1536)
        save_png(n, k, "painted cut-out: adult Kaiju rising, low angle")
        if n == "kaiju_neutral":
            ys, xs = np.where(k[..., 3] > 128); band = ys < ys.min() + 0.15 * (ys.max() - ys.min())
            geom["kaiju_head_uv"] = [round(float(xs[band].mean() / k.shape[1]), 4), round(float(ys[band].mean() / k.shape[0]), 4)]
        save_png(n + "_glow", B.glow_from(k, hue[0], hue[1], col, sat=0.5, val=0.6, radius=16), "painted Kaiju", "crystal glow")

    # ---- the two peoples, native poses (Crimson drawn as Crimson, facing left; Azure facing right)
    for n in ["warrior", "farmer", "miner", "warrior_crimson", "farmer_crimson", "miner_crimson", "warrior_charge", "warrior_cleave_crimson"]:
        if not os.path.exists(os.path.join(SRC, n + ".jpg")): continue
        kl = 1 if n == "warrior_cleave_crimson" else 0   # drop the loose swoosh strokes the ink hull leaves behind
        c = fit(cutout(load(n), halo=True, keep_largest=kl), 1100, 1100)
        save_png(n, c, "painted cut-out: native %s pose" % n.replace("_", " "))
    for n, hue, col, key_g in [("farmer_harvest", (62, 110), "biomass", False), ("biomass_plants", (62, 110), "biomass", False),
                               ("corruption_growth", (265, 320), "corruption", True)]:
        if not os.path.exists(os.path.join(SRC, n + ".jpg")): continue
        c = B.pad(fit(cutout(load(n), min_area=900), 1400, 1100), 40)   # room for the glow so it never meets the border
        save_png(n, c, "painted cut-out: %s" % n.replace("_", " "))
        save_png(n + "_glow", B.glow_from(c, hue[0], hue[1], col, sat=0.5, val=0.6, radius=14), "painted %s" % n, "glow")
    fh = [r for r in ROWS if r[0] == "farmer"]
    if fh:
        f = np.asarray(Image.open(os.path.join(OUT, "farmer.png")).convert("RGBA")).astype(np.float32)
        save_png("farmer_glow", B.glow_from(f, 62, 110, "biomass", sat=0.5, val=0.6, radius=12), "painted farmer", "tank glow")

    # ---- cut-in portraits for the big-moment takeovers
    for n in sorted(os.listdir(SRC)):
        if n.startswith("cutin_") and n.endswith(".jpg"):
            save_jpg(n[:-4], fit(load(n[:-4]), 1920), "painted cut-in portrait (16:9, takeover system)")

    json.dump(geom, open(GEOM, "w"), indent=1)
    man = os.path.join(HERE, "layers-manifest.tsv")
    rows = [l.rstrip("\n").split("\t") for l in open(man)] if os.path.exists(man) else [["name", "width", "height", "source", "note"]]
    keep = [r for r in rows if r[0] not in {x[0] for x in ROWS}]
    keep += [[n, str(s[0]), str(s[1]), src, note] for n, s, src, note in ROWS]
    open(man, "w").write("\n".join("\t".join(r) for r in keep) + "\n")
    print("painted layers", len(ROWS), "geometry", geom)

if __name__ == "__main__":
    main()
