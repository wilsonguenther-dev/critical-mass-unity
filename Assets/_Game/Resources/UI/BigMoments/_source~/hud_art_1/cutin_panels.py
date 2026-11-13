# hud-art-1: the cut-in panel system, drawn by code (deterministic, no generation). Writes into Resources/UI/BigMoments/:
#   bm-band.png               2048x512, tileable in x: the slanted panel the character breaks out of. Greyscale, tinted by the
#                             team (or Kaiju) accent in BigMoments: a darker brushed core (value 0.38..0.46) between two bright
#                             energy rails (value 1.0), soft brushed edges (amplitude 5 px, never jagged).
#   bm-title-plate*.png       1536x384, tinted Void Navy: a clean slanted plate (12 degree lean, 10 px corner bevel) with a faint
#                             inner hairline; -impact (Kaiju) has a heavier lower lip, -ribbon (finisher) is longer and thinner.
#   bm-title-plate-accent.png 1536x384, tinted by the accent: the leading-edge bar and the underline of the plate.
# usage: python3 cutin_panels.py OUTDIR
import sys, os, math
import numpy as np
from PIL import Image
SS = 3
def save(path, v, a, w, h):
    rgb = np.repeat(np.clip(v, 0, 1)[..., None], 3, -1)
    big = np.dstack([rgb * a[..., None], a]).astype(np.float32)
    chans = [Image.fromarray(big[..., k], "F").resize((w, h), Image.LANCZOS) for k in range(4)]
    pm = np.dstack([np.asarray(c) for c in chans]); A = np.clip(pm[..., 3], 0, 1)
    rgb = np.where(A[..., None] > 1e-3, pm[..., :3] / np.maximum(A[..., None], 1e-3), 1.0)
    Image.fromarray((np.dstack([np.clip(rgb, 0, 1), A]) * 255).astype(np.uint8), "RGBA").save(path, optimize=True)
def ss(x0, x1, x): t = np.clip((x - x0) / (x1 - x0), 0, 1); return t * t * (3 - 2 * t)
def periodic(n, seed, terms, amp):
    rng = np.random.default_rng(seed); x = np.arange(n) / n; y = np.zeros(n)
    for k in range(1, terms + 1): y += rng.normal() / k * np.sin(2 * math.pi * (k * x + rng.random()))
    return y / (np.abs(y).max() + 1e-6) * amp
def band(out):
    w, h = 2048 * SS, 512 * SS
    y = np.arange(h)[:, None].astype(np.float32); x = np.arange(w)
    top = 96 * SS + periodic(w, 1, 9, 5 * SS)[None, :]; bot = (512 - 96) * SS + periodic(w, 2, 9, 5 * SS)[None, :]
    a = ss(top - 2 * SS, top + 2 * SS, y) * (1 - ss(bot - 2 * SS, bot + 2 * SS, y))
    rail = 22 * SS; gap = 9 * SS
    rails = ((y > top) & (y < top + rail)) | ((y < bot) & (y > bot - rail))
    inner = (y > top + rail + gap) & (y < bot - rail - gap)
    streak = 0.42 + periodic(w, 3, 40, 0.035)[None, :] * np.ones_like(y) + 0.03 * np.sin(y / (17 * SS) + periodic(w, 4, 6, 2.0)[None, :])
    v = np.where(rails, 1.0, np.where(inner, streak, 0.0))
    a = a * np.where(rails | inner, 0.96, 0.0)
    save(os.path.join(out, "bm-band.png"), v, a, 2048, 512)
def plate(out, name, x0, x1, y0, y1, lean=12, lip=0, accent=False):
    w, h = 1536 * SS, 384 * SS
    Y, X = np.mgrid[0:h, 0:w].astype(np.float32)
    k = math.tan(math.radians(lean))
    # slanted plate: left edge x = x0 + (y1 - y) k ... right edge x = x1 - (y - y0) k (parallelogram leaning right)
    L = x0 * SS + (y1 * SS - Y) * k; R = x1 * SS + (y1 * SS - Y) * k
    T = y0 * SS; B = (y1 + lip) * SS
    d = np.minimum.reduce([X - L, R - X, Y - T, B - Y])
    bevel = 10 * SS
    corner = np.minimum(X - L, Y - T) + np.minimum(R - X, B - Y)
    inside = ss(-1.5 * SS, 1.5 * SS, d)
    if not accent:
        hair = (np.abs(d - 9 * SS) < 1.2 * SS)
        v = np.where(hair, 1.0, 0.92 + 0.04 * np.sin(X / (41 * SS)))
        a = inside * np.where(hair, 1.0, 0.94)
        # lighten the hairline slightly by lowering its tint weight: navy tint on value 1.0 stays navy; it reads by alpha only
        v = np.where(hair, 0.55, v)
    else:
        bar = (X - L > -1 * SS) & (X - L < 18 * SS) & (Y > T) & (Y < B)
        under = (Y > B - 10 * SS) & (Y < B) & (X > L) & (X < R)
        a = np.where(bar | under, 1.0, 0.0) * ss(-1.5 * SS, 1.5 * SS, np.minimum(R - X, B - Y + 1))
        v = np.ones_like(X)
    save(os.path.join(out, name), v, a, 1536, 384)
if __name__ == "__main__":
    out = sys.argv[1]
    band(out)
    plate(out, "bm-title-plate.png", 150, 1360, 70, 300)
    plate(out, "bm-title-plate-impact.png", 130, 1380, 62, 300, lean=14, lip=12)
    plate(out, "bm-title-plate-ribbon.png", 70, 1440, 92, 288, lean=10)
    plate(out, "bm-title-plate-accent.png", 150, 1360, 70, 300, accent=True)
    print("panels written to", out)
