#!/usr/bin/env python3
"""Story intro, round 2 (intro-film-2): the pre-rendered film, every layer new art (Assets/_Game/Art/Story/_film2~,
made by tools/film/prep_film2.py from the round-2 generated stills). Grid, cuts and timings are round 1's (plan
intro-film-1); the compositing engine below is round 1's, extended.

Round 1 docstring follows.
Story intro 3: the pre-rendered film (plan intro-film-1, sections 1, 2 and 4).

Composites the story paintings and their cut-out layers (Assets/_Game/Resources/Story) into one locked film: fixed
black 2.0:1 bars, one camera language with velocity carried across cuts, 2 masked light events, depth-scaled parallax,
per-layer motion blur, one grade (exposure match + LUT), bloom on glow layers only, vignette and grain.

  python3 tools/film/render_intro.py OUT.mp4 [--scale 0.5] [--audio mix.wav] [--preview] [--frames a:b]
  python3 tools/film/render_intro.py --table intro-film.json      # write the shot table only

--preview draws the engine's subtitles and the title logo into the frames (evidence only; the shipped film carries
neither: the engine draws both so the logo is pixel-locked to the title screen and subtitles follow the setting).
Deterministic: every random draw is seeded by frame number.
"""
import json, math, os, subprocess, sys
from functools import lru_cache
import numpy as np
import cv2
from PIL import Image, ImageDraw, ImageFont
cv2.setNumThreads(1)          # forked workers: an OpenCV thread pool made before fork() deadlocks them

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
ART = os.path.join(REPO, 'Assets/_Game/Resources/Story')
LOGO = os.path.join(REPO, 'Assets/_Game/Resources/UI/Logo')
FONT = os.path.join(REPO, 'Assets/_Game/Resources/UI/Fonts/NunitoSans-ExtraBold.ttf')
FPS = 30
FRAMES = 1420
DUR = FRAMES / FPS
BEAT, BAR = 0.75, 3.0
SCALE = 1.0
W0, H0, PIC0, BAR0 = 2560, 1440, 1280, 80     # container, picture height, bar height (full res)


def dims():
    W = int(round(W0 * SCALE)) // 2 * 2
    H = int(round(H0 * SCALE)) // 2 * 2
    P = int(round(PIC0 * SCALE)) // 2 * 2
    return W, H, P, (H - P) // 2


# ---------------------------------------------------------------- easing + keys
def ease(name, u):
    u = min(1.0, max(0.0, u))
    if name == 'linear':
        return u
    if name == 'outCubic':
        return 1 - (1 - u) ** 3
    if name == 'inCubic':
        return u ** 3
    if name == 'inOutCubic':
        return 4 * u ** 3 if u < 0.5 else 1 - (-2 * u + 2) ** 3 / 2
    if name == 'outExpo':
        return 1 if u >= 1 else 1 - 2 ** (-10 * u)
    if name == 'outSine':
        return math.sin(u * math.pi / 2)
    if name == 'inOutSine':
        return -(math.cos(math.pi * u) - 1) / 2
    if name == 'outBack':
        c = 1.70158 * 0.6
        return 1 + (c + 1) * (u - 1) ** 3 + c * (u - 1) ** 2
    if name == 'step':
        return 1.0 if u >= 1 else 0.0
    raise ValueError(name)


def K(t, keys):
    """keys: [(t, v), (t, v, ease_into_this_key), ...]; holds outside."""
    if t <= keys[0][0]:
        return keys[0][1]
    for i in range(1, len(keys)):
        if t <= keys[i][0]:
            t0, v0 = keys[i - 1][0], keys[i - 1][1]
            t1, v1 = keys[i][0], keys[i][1]
            e = keys[i][2] if len(keys[i]) > 2 else 'linear'
            return v0 + (v1 - v0) * ease(e, (t - t0) / max(1e-6, t1 - t0))
    return keys[-1][1]


def spring(t, t0, amp, freq=9.0, damp=7.0):
    """Damped shake (2D) after t0, deterministic."""
    if t < t0:
        return 0.0, 0.0
    u = t - t0
    e = amp * math.exp(-damp * u)
    return e * math.sin(2 * math.pi * freq * u), e * 0.6 * math.sin(2 * math.pi * freq * 1.37 * u + 1.1)


# ---------------------------------------------------------------- textures
@lru_cache(maxsize=48)
def tex(name, width_px):
    """Premultiplied RGBA uint8 texture, resized (area) to width_px once."""
    if name in _PROC:
        return tex_proc(name, width_px)
    p = os.path.join(ART, name) if os.path.exists(os.path.join(ART, name)) else name
    if not os.path.exists(p):
        for ext in ('.png', '.jpg'):
            if os.path.exists(os.path.join(ART, name + ext)):
                p = os.path.join(ART, name + ext)
                break
    im = cv2.imread(p, cv2.IMREAD_UNCHANGED)
    if im is None:                                   # a layer not made yet: transparent (drafts only)
        return np.zeros((8, max(8, int(width_px)), 4), np.uint8)
    if im.ndim == 2:
        im = cv2.cvtColor(im, cv2.COLOR_GRAY2BGRA)
    if im.shape[2] == 3:
        im = cv2.cvtColor(im, cv2.COLOR_BGR2BGRA)
    im = cv2.cvtColor(im, cv2.COLOR_BGRA2RGBA)
    h, w = im.shape[:2]
    width_px = max(8, int(width_px))
    if width_px < w:
        im = cv2.resize(im, (width_px, max(1, int(round(h * width_px / w)))), interpolation=cv2.INTER_AREA)
    elif width_px > w * 1.02:
        im = cv2.resize(im, (width_px, max(1, int(round(h * width_px / w)))), interpolation=cv2.INTER_LANCZOS4)
        # light unsharp after the 2x-class upscale (plan: Lanczos + light USM)
        bl = cv2.GaussianBlur(im, (0, 0), 1.0)
        im = cv2.addWeighted(im, 1.25, bl, -0.25, 0)
    a = im[..., 3:4].astype(np.float32) / 255.0
    im[..., :3] = (im[..., :3].astype(np.float32) * a).astype(np.uint8)
    return im


def tex_aspect(name):
    t = tex(name, 64)
    return t.shape[0] / t.shape[1]


# ---------------------------------------------------------------- compositing
class Canvas:
    def __init__(self):
        W, H, P, _ = dims()
        self.W, self.H = W, P
        self.rgb = np.zeros((P, W, 3), np.float32)
        self.glow = np.zeros((P, W, 3), np.float32)     # glow layers also feed the bloom
        self.S = W / 1920.0                               # px per stage unit

    def to_px(self, x, y):
        return self.W / 2 + x * self.S, self.H / 2 - y * self.S

    def draw(self, name, cx, cy, w_units, alpha=1.0, add=False, tint=None, rot=0.0, blur_vec=None, mask=None,
             glow=False, flipx=False, gain=1.0, crop=None):
        """Draw texture centred at stage (cx, cy) with width w_units (stage). mask: fn(xx, yy) -> 0..1 over the
        destination box in stage units (for wipes and reveals)."""
        if alpha <= 0.002 or w_units <= 1:
            return
        wpx = w_units * self.S
        t = tex(name, min(4096, int(2 ** (math.ceil(math.log2(max(8.0, wpx)) * 6) / 6))))
        if crop is not None:
            # crop = (top, bottom) fraction kept, for mask reveals of banners
            pass
        th, tw = t.shape[:2]
        f = wpx / tw
        hpx = th * f
        px, py = self.to_px(cx, cy)
        # destination bbox (with rotation margin)
        r = math.radians(rot)
        c, s = math.cos(r), math.sin(r)
        hw, hh = wpx / 2, hpx / 2
        ex = abs(hw * c) + abs(hh * s)
        ey = abs(hw * s) + abs(hh * c)
        pad = 2
        if blur_vec is not None:
            pad += int(abs(blur_vec[0]) + abs(blur_vec[1])) + 2
        x0, x1 = int(max(0, px - ex - pad)), int(min(self.W, px + ex + pad + 1))
        y0, y1 = int(max(0, py - ey - pad)), int(min(self.H, py + ey + pad + 1))
        if x1 <= x0 or y1 <= y0:
            return
        sx = -f if flipx else f
        # dst = R * S * (src - src_centre) + (p - box origin)
        M = np.array([[c * sx, s * f, 0], [-s * sx, c * f, 0]], np.float64)
        M[:, 2] = np.array([px - x0, py - y0]) - M[:, :2] @ np.array([tw / 2, th / 2])
        out = cv2.warpAffine(t, M, (x1 - x0, y1 - y0), flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT)
        if blur_vec is not None:
            out = motion_blur(out, blur_vec)
        o = out.astype(np.float32) / 255.0
        rgb, a = o[..., :3], o[..., 3:4]
        if tint is not None:
            rgb = rgb * np.array(tint, np.float32)
        if gain != 1.0:
            rgb = rgb * gain
        k = alpha
        if mask is not None:
            yy, xx = np.mgrid[y0:y1, x0:x1].astype(np.float32)
            sxu = (xx - self.W / 2) / self.S
            syu = (self.H / 2 - yy) / self.S
            k = alpha * mask(sxu, syu)[..., None]
        dst = self.rgb[y0:y1, x0:x1]
        if add:
            dst += rgb * k
        else:
            dst *= (1 - a * k)
            dst += rgb * k
        if glow or add:
            self.glow[y0:y1, x0:x1] += rgb * k * (1.0 if add else 0.6)

    def fill(self, col, alpha=1.0):
        self.rgb = self.rgb * (1 - alpha) + np.array(col, np.float32) * alpha


def motion_blur(img, v):
    """Directional blur along the screen velocity (px per frame); kernel length = 0.5 x speed (180 deg shutter)."""
    vx, vy = v
    L = 0.5 * math.hypot(vx, vy)
    if L < 1.0:
        return img
    n = int(math.ceil(L)) | 1
    k = np.zeros((n, n), np.float32)
    cx = cy = n // 2
    ang = math.atan2(vy, vx)
    for i in range(n * 3):
        u = (i / (n * 3 - 1) - 0.5) * L
        x = int(round(cx + u * math.cos(ang)))
        y = int(round(cy + u * math.sin(ang)))
        if 0 <= x < n and 0 <= y < n:
            k[y, x] = 1
    k /= k.sum()
    return cv2.filter2D(img, -1, k, borderType=cv2.BORDER_CONSTANT)


# ---------------------------------------------------------------- camera
class Cam:
    def __init__(self, x=0.0, y=0.0, z=1.0, shake=(0.0, 0.0)):
        self.x, self.y, self.z, self.shake = x, y, z, shake

    def place(self, x, y, w, d):
        """World (stage units) at depth d -> screen stage units and width. Parallax and zoom scale with depth."""
        pd = 0.15 + 0.85 * d
        zd = 1 + (self.z - 1) * (0.35 + 0.65 * d)
        sx, sy = self.shake
        return ((x - self.x * pd) * zd + sx * (0.3 + 0.7 * d), (y - self.y * pd) * zd + sy * (0.3 + 0.7 * d), w * zd)


def layer(cv, cam, cam_next, name, x, y, w, d, **kw):
    """Place and draw a layer; motion blur from the screen velocity to the next frame's camera / position."""
    px, py, pw = cam.place(x, y, w, d)
    nx = kw.pop('next_xy', None)
    blur = None
    if cam_next is not None:
        qx, qy, _ = cam_next.place(nx[0] if nx else x, nx[1] if nx else y, w, d)
        vx, vy = (qx - px) * cv.S, -(qy - py) * cv.S
        if math.hypot(vx, vy) > 2.0:
            blur = (vx, vy)
    cv.draw(name, px, py, pw, blur_vec=blur, **kw)
    return px, py, pw


# ---------------------------------------------------------------- particles
def motes(cv, cam, t, seed, n, region, size, d, col=(1.0, 0.95, 0.8), alpha=0.5, drift=(6, 10), add=True,
          twinkle=0.0, sizes=2):
    r = np.random.default_rng(seed)
    x0, x1, y0, y1 = region
    xs = r.uniform(x0, x1, n)
    ys = r.uniform(y0, y1, n)
    ph = r.uniform(0, 2 * math.pi, n)
    sz = r.choice([size, size * 0.55] if sizes == 2 else [size], n)
    for i in range(n):
        x = xs[i] + drift[0] * t + 14 * math.sin(0.4 * t + ph[i])
        y = ys[i] + drift[1] * t * 0.3 + 10 * math.sin(0.55 * t + ph[i] * 1.7)
        x = (x - x0) % (x1 - x0) + x0
        a = alpha * (1 - twinkle + twinkle * (0.5 + 0.5 * math.sin(2.3 * t + ph[i] * 3)))
        px, py, pw = cam.place(x, y, sz[i], d)
        cv.draw(DOT, px, py, pw, alpha=a, add=add, tint=col)


# ---------------------------------------------------------------- shot table (plan section 2; global seconds)
SHOTS = [
    dict(id='calm', t0=0.00, t1=3.75, out='cut'),
    dict(id='crack_rise', t0=3.75, t1=9.75, out='cut'),       # shots 2 and 3: one continuous move (no cut at 6.00)
    dict(id='fall', t0=9.75, t1=12.00, out='match-dissolve', dur=0.80),
    dict(id='shrine', t0=12.00, t1=17.25, out='glow-bloom'),
    dict(id='peoples', t0=17.25, t1=19.50, out='cut'),
    dict(id='hill', t0=19.50, t1=23.25, out='cut'),
    dict(id='choice', t0=23.25, t1=28.50, out='match-cut'),
    dict(id='hatch', t0=28.50, t1=32.25, out='glow-match-cut'),
    dict(id='kaiju', t0=32.25, t1=40.50, out='cut'),
    dict(id='hero', t0=40.50, t1=DUR, out='engine-dissolve', dur=DUR - 46.50),
]
CUTS = [3.75, 9.75, 17.25, 19.50, 23.25, 28.50, 32.25, 40.50]
HARD_CUTS = [3.75, 9.75, 19.50, 23.25, 28.50, 32.25, 40.50]
LIGHT_EVENTS = [dict(t=4.50, src='crack', peak=0.45, up=3, down=8), dict(t=31.50, src='egg', peak=0.6, up=3, down=10)]
SHAKES = [dict(t=4.50, px=8, dur=0.4), dict(t=22.50, px=6, dur=0.35), dict(t=31.50, px=10, dur=0.45),
          dict(t=34.50, px=8, dur=1.5)]
TITLES = [
    dict(text='LONG AGO', t_in=1.00, t_out=3.30, x=-560, y=-250, size=64, align='left', plate=True),
    dict(text='SKYBREAK BASIN', t_in=7.40, t_out=9.70, x=0, y=330, size=84, plate=True),
    dict(text='SOMETHING FELL THROUGH.', t_in=10.05, t_out=12.35, x=0, y=320, size=64, plate=True),
    dict(text='FEED IT', t_in=23.70, t_out=26.00, x=-430, y=300, size=84, plate=True, cam_locked=True),
    dict(text='OR CORRUPT IT', t_in=26.05, t_out=28.35, x=380, y=300, size=84, plate=True, cam_locked=True),
    dict(text='IT PICKS A SIDE.', t_in=35.40, t_out=37.70, x=-500, y=210, size=76, plate=True),
    dict(text='OR NO SIDE AT ALL.', t_in=37.80, t_out=40.10, x=-500, y=210, size=76, plate=True),
]
SUBS = [
    dict(text='The sky cracked. And the land rose to meet it.', t_in=5.05, t_out=8.39, clip='story-card-1'),
    dict(text='Far away, a Kaiju egg sleeps. It remembers every meal.', t_in=12.55, t_out=16.85, clip='story-card-3'),
    dict(text='Two banners. One hill. Neither has learned to share.', t_in=18.28, t_out=22.03, clip='story-card-2'),
    dict(text='Feed it, and it fights for you. Corrupt it, and it fights everyone.', t_in=23.67, t_out=28.17,
         clip='story-card-4'),
    dict(text='Hold the hill. Raise the Kaiju.', t_in=40.80, t_out=43.37, clip='story-card-5'),
]
LOGO_KEYS = dict(t_in=43.17, land=43.50, settle=44.00, s_from=1.6, s_land=1.04, s_end=1.0)
EXPOSURE = dict(calm=1.0, crack_rise=1.0, fall=1.0, shrine=1.08, peoples=0.97, hill=0.97, choice=1.0, hatch=0.95,
                kaiju=1.0, hero=0.98)


def shot_at(t):
    for s in SHOTS:
        if s['t0'] <= t < s['t1']:
            return s
    return SHOTS[-1]


def shake_at(t):
    x = y = 0.0
    for s in SHAKES:
        if s['t'] <= t <= s['t'] + s['dur'] + 0.3:
            freq = 9.0 if s['dur'] < 1 else 3.2
            damp = 5.0 / s['dur']
            dx, dy = spring(t, s['t'], s['px'] / (1080 / 1920 * 1.333) * 0.75, freq, damp)
            x += dx
            y += dy
    return x, y


def light(t, t0, up, down, peak):
    f = (t - t0) * FPS
    if f < -up or f > down:
        return 0.0
    if f < 0:
        return peak * (1 + f / up)
    return peak * (1 - f / down) ** 1.6


# ---------------------------------------------------------------- round 2 art and procedural elements
A2 = os.path.join(REPO, 'Assets/_Game/Art/Story/_film2~')


def L(n):
    return os.path.join(A2, n)


_PROC = {}


def proc(name, fn):
    if name not in _PROC:
        _PROC[name] = fn()
    return name


def _soft_dot():
    s = 256
    yy, xx = np.mgrid[0:s, 0:s].astype(np.float32)
    r = np.hypot(xx - s / 2, yy - s / 2) / (s / 2)
    a = np.clip(1 - r, 0, 1) ** 2.2
    return np.dstack([np.ones((s, s, 3), np.float32), a])


def _rays():
    W_, H_ = 1600, 900
    img = np.zeros((H_, W_), np.float32)
    r = np.random.default_rng(5)
    for k in range(9):
        a0 = r.uniform(-0.55, 0.55)
        wd = r.uniform(0.02, 0.07)
        pts = np.array([[W_ / 2, -40], [W_ / 2 + math.tan(a0 - wd) * H_ * 1.1, H_ * 1.1],
                        [W_ / 2 + math.tan(a0 + wd) * H_ * 1.1, H_ * 1.1]], np.int32)
        m = np.zeros_like(img)
        cv2.fillPoly(m, [pts], r.uniform(0.35, 1.0))
        img = np.maximum(img, m)
    img = cv2.GaussianBlur(img, (0, 0), 18)
    fall = np.linspace(1, 0.15, H_)[:, None]
    a = img * fall
    return np.dstack([np.ones((H_, W_, 3), np.float32), a])


def _puff():
    s = 256
    yy, xx = np.mgrid[0:s, 0:s].astype(np.float32)
    r = np.random.default_rng(9)
    a = np.zeros((s, s), np.float32)
    for k in range(7):
        cx, cy, rr = r.uniform(80, 176), r.uniform(90, 170), r.uniform(40, 70)
        a = np.maximum(a, np.clip(1 - np.hypot(xx - cx, yy - cy) / rr, 0, 1) ** 0.8)
    a = cv2.GaussianBlur(a, (0, 0), 6)
    shade = np.clip(1.15 - (yy / s) * 0.5, 0, 1)
    rgb = np.dstack([shade, shade * 0.95, shade * 0.88])
    return np.dstack([rgb, a])


def _star():
    s = 512
    img = np.zeros((s, s), np.float32)
    c = s / 2
    r = np.random.default_rng(13)
    for k in range(12):
        ang = k * math.pi / 6 + r.uniform(-0.12, 0.12)
        ln = r.uniform(0.55, 1.0) * c
        p = np.array([[c, c], [c + math.cos(ang - 0.08) * ln * 0.25, c + math.sin(ang - 0.08) * ln * 0.25],
                      [c + math.cos(ang) * ln, c + math.sin(ang) * ln],
                      [c + math.cos(ang + 0.08) * ln * 0.25, c + math.sin(ang + 0.08) * ln * 0.25]], np.int32)
        cv2.fillPoly(img, [p], 1.0)
    cv2.circle(img, (int(c), int(c)), int(c * 0.22), 1.0, -1)
    img = cv2.GaussianBlur(img, (0, 0), 2)
    return np.dstack([np.ones((s, s, 3), np.float32), img])


def tex_proc(name, width_px):
    a = _PROC[name]
    h, w = a.shape[:2]
    width_px = max(8, int(width_px))
    im = cv2.resize(a, (width_px, max(1, int(round(h * width_px / w)))), interpolation=cv2.INTER_AREA)
    im = im.copy()
    im[..., :3] *= im[..., 3:4]
    return (np.clip(im, 0, 1) * 255).astype(np.uint8)


DOT = proc('__dot', _soft_dot)


def _contact():
    w, h = 512, 96
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    r = np.hypot((xx - w / 2) / (w / 2), (yy - h / 2) / (h / 2))
    a = np.clip(1 - r, 0, 1) ** 1.4 * 0.85
    return np.dstack([np.full((h, w, 3), (0.06, 0.07, 0.1), np.float32), a])


CONTACT = proc('__contact', _contact)
RAYS = proc('__rays', _rays)
PUFF = proc('__puff', _puff)
STAR = proc('__star', _star)


def lh(cv, cam, cn, name, x, feet, h, d, **kw):
    """Place a cut-out by height with its feet at y=feet."""
    w = h / tex_aspect(name)
    sh = kw.pop('shadow', 0.0)
    if sh > 0:                                    # contact shadow / occlusion where the figure meets the ground
        layer(cv, cam, None, CONTACT, x, feet + h * 0.015, min(w, h * 0.55) * 1.15, d, alpha=sh)
    nx = kw.pop('next_feet', None)
    if nx is not None:
        kw['next_xy'] = (nx[0], nx[1] + h / 2)
    return layer(cv, cam, cn, name, x, feet + h / 2, w, d, **kw)


def dot(cv, x, y, w, col, a):
    cv.draw(DOT, x, y, w, alpha=a, add=True, tint=col)


# ---------------------------------------------------------------- the crack (one shape for the sky and the egg)
def crack_pts(seed=4):
    """A jagged trunk with three forks, in unit coordinates (-1..1), drawn in arc-length order."""
    r = np.random.default_rng(seed)
    trunk = [(-0.15, 0.95)]
    x, y = -0.15, 0.95
    for i in range(7):
        x += r.uniform(-0.06, 0.12)
        y -= r.uniform(0.11, 0.16)
        trunk.append((x, y))
    forks = []
    for k, ang in enumerate((-0.9, 0.0, 0.85)):
        fx, fy = trunk[-1]
        f = [(fx, fy)]
        for i in range(4):
            fx += math.sin(ang) * r.uniform(0.07, 0.11) + r.uniform(-0.03, 0.03)
            fy -= math.cos(ang) * r.uniform(0.06, 0.1)
            f.append((fx, fy))
        forks.append(f)
    side = []
    for j in (2, 4):
        sx, sy = trunk[j]
        s = [(sx, sy)]
        d = 1 if j == 2 else -1
        for i in range(3):
            sx += d * r.uniform(0.06, 0.1)
            sy -= r.uniform(0.02, 0.06)
            s.append((sx, sy))
        side.append((j, s))
    return trunk, forks, side


CRACK = crack_pts()


def crack_polys(u):
    """Polylines visible at draw-on progress u (0..1): trunk first, then the side cracks, then the forks."""
    trunk, forks, side = CRACK
    out = []
    n = len(trunk) - 1
    ut = min(1.0, u / 0.55)
    k = ut * n
    seg = trunk[:int(k) + 1]
    if int(k) < n:
        a, b = trunk[int(k)], trunk[int(k) + 1]
        f = k - int(k)
        seg = seg + [(a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f)]
    out.append(seg)
    for j, s in side:
        us = (ut * n - j) / 2.0
        if us > 0:
            m = max(2, int(round(min(1, us) * len(s))))
            out.append(s[:m])
    uf = (u - 0.55) / 0.45
    if uf > 0:
        for f in forks:
            m = max(2, int(round(min(1, uf) * len(f))))
            out.append(f[:m])
    return out


def draw_crack(cv, to_px, u, width_px, core=(1.0, 0.97, 1.0), halo=(0.75, 0.55, 1.0), alpha=1.0, clip=None, dark=True):
    """to_px maps unit crack coords to canvas px. Core line plus a violet-white halo, added (light)."""
    if u <= 0 or alpha <= 0:
        return
    H, W = cv.rgb.shape[:2]
    m = np.zeros((H, W), np.float32)
    for pi_, poly in enumerate(crack_polys(u)):
        pts = np.array([to_px(x, y) for x, y in poly], np.float32)
        if dark and pi_ == 0 and len(pts) >= 3:
            # the trunk is a torn gap, not a line: a tapered lens of light, widest in the middle of the full trunk
            n = len(CRACK[0]) - 1
            d = np.gradient(pts, axis=0)
            nrm = np.stack([-d[:, 1], d[:, 0]], 1)
            nrm /= np.linalg.norm(nrm, axis=1, keepdims=True) + 1e-6
            prof = np.array([width_px * 4.5 * math.sin(math.pi * min(i, n) / n) ** 0.6 + 1 for i in range(len(pts))],
                            np.float32)
            jag = np.where(np.arange(len(pts)) % 2 == 0, 1.0, 0.7)[:, None]
            left = pts + nrm * (prof[:, None] * jag)
            right = pts - nrm * (prof[:, None] * jag[::-1])
            cv2.fillPoly(m, [np.round(np.concatenate([left, right[::-1]])).astype(np.int32)], 1.0, cv2.LINE_AA)
            continue
        for i in range(len(pts) - 1):
            th = max(1, int(width_px * (1.0 - 0.5 * i / max(1, len(pts)))))
            cv2.line(m, tuple(np.round(pts[i]).astype(int)), tuple(np.round(pts[i + 1]).astype(int)), 1.0, th, cv2.LINE_AA)
    if clip is not None:
        m *= clip
    if dark:
        tear = cv2.GaussianBlur(cv2.dilate(m, np.ones((3, 3), np.float32), iterations=max(1, int(width_px * 0.8))), (0, 0),
                                max(2.0, width_px * 1.6))
        cv.rgb *= (1 - 0.75 * np.clip(tear * 1.4, 0, 1) * alpha)[..., None]
    halo_m = cv2.GaussianBlur(m, (0, 0), max(2.0, width_px * 2.5))
    wide = cv2.GaussianBlur(m, (0, 0), max(6.0, width_px * 9))
    add = (m[..., None] * np.array(core, np.float32) * 1.2 + halo_m[..., None] * np.array(halo, np.float32) * 1.4 +
           wide[..., None] * np.array(halo, np.float32) * 0.8) * alpha
    cv.rgb += add
    cv.glow += add * 0.7


# ---------------------------------------------------------------- shots
def far_islands(cv, cam, cn, t, d, tint, y=0, drift=-5, scale=1.0):
    """Distant floating islands: the round-2 rock sprites, small, hazed toward the sky (no extra plate)."""
    for n, x, yy, w, fl in (('rock_01.png', -760, 40, 260, False), ('rock_02.png', -260, -10, 170, True),
                            ('rock_02.png', 420, 60, 300, False), ('rock_01.png', 860, -20, 190, True)):
        layer(cv, cam, cn, L(n), x * scale + drift * t, y + yy * scale, w * scale, d, tint=tint, flipx=fl, alpha=0.9)


def cam_calm(t):
    y = K(t, [(0.0, -230), (0.40, -230), (2.60, -20, 'outCubic'), (3.75, 40)])
    z = K(t, [(0.0, 1.00), (3.75, 1.05)])
    return Cam(K(t, [(0, -20), (3.75, 20)]), y, z, shake_at(t))


def shot_calm(cv, t, cam, cn):
    layer(cv, cam, cn, L('sky_dawn.jpg'), 4 * t, 60, 2600, 0.08)
    far_islands(cv, cam, cn, t, 0.3, (0.62, 0.78, 0.86), y=20)
    layer(cv, cam, cn, L('clouds_day.png'), 12 * t - 60, -330, 2900, 0.4, tint=(0.95, 0.97, 1.0), alpha=0.95)
    layer(cv, cam, cn, L('arena_calm.png'), 0, -60, 1500, 0.6)
    motes(cv, cam, t, 11, 26, (-1100, 1100, -500, 450), 14, 0.7, alpha=0.35, twinkle=0.5)
    layer(cv, cam, cn, L('clouds_day.png'), -16 * t, -700, 3300, 0.88, tint=(0.9, 0.94, 1.02))


def cam_crack_rise(t):
    y = K(t, [(3.75, 40), (4.50, 80), (6.00, 120), (6.53, -170, 'inOutCubic'), (9.75, -205)])
    z = K(t, [(3.75, 1.05), (4.30, 1.08), (4.48, 1.06, 'outSine'), (4.68, 1.12, 'outExpo'), (6.00, 1.10),
              (6.53, 1.04, 'inOutCubic'), (9.75, 1.08)])
    return Cam(K(t, [(3.75, 20), (9.75, 60)]), y, z, shake_at(t))


SKY_CRACK = dict(x=140, y=200, w=1000, h=640, d=0.14)


def sky_crack_px(cv, cam):
    def f(ux, uy):
        sx, sy, _ = cam.place(SKY_CRACK['x'] + ux * SKY_CRACK['w'] / 2, SKY_CRACK['y'] + uy * SKY_CRACK['h'] / 2, 1,
                              SKY_CRACK['d'])
        return cv.to_px(sx, sy)
    return f


def shot_crack_rise(cv, t, cam, cn):
    layer(cv, cam, cn, L('sky_storm.jpg'), 0, 60, 2600, 0.12, gain=0.85)
    if t >= 4.50:
        u = min(1.0, (int((t - 4.50) * 15) / 15.0) / 0.6)             # drawn on twos
        g = K(t, [(4.50, 1.6), (4.80, 1.0), (6.5, 0.75), (9.75, 0.7)])
        draw_crack(cv, sky_crack_px(cv, cam), u, 15 * cv.S, alpha=g * (0.92 + 0.08 * math.sin(t * 9)))
        R = K(t, [(4.55, 0.0), (5.0, 1600.0, 'outCubic')])
        sxp, syp, _ = cam.place(SKY_CRACK['x'], SKY_CRACK['y'] - 60, 1, 0.2)
        mask = lambda xx, yy: np.clip((R - np.hypot(xx - sxp, yy - syp)) / 300.0, 0, 1)
        layer(cv, cam, cn, RAYS, SKY_CRACK['x'], SKY_CRACK['y'] - 420, 2100, 0.2, alpha=0.30, add=True,
              tint=(0.85, 0.8, 1.0), mask=mask)
    k = light(t, 4.50, 3, 8, 0.45)
    if k > 0:
        sxp, syp, _ = cam.place(SKY_CRACK['x'], SKY_CRACK['y'], 1, 0.14)
        dot(cv, sxp, syp, 1500, (1.0, 0.93, 1.0), k * 1.6)
    # the land rises to meet it
    by = K(t, [(3.75, -400), (6.30, -400), (8.00, -300, 'outCubic'), (9.75, -292)])
    layer(cv, cam, cn, L('arena_calm.png'), 0, by, 1500, 0.6, tint=(0.62, 0.66, 0.85), gain=1.0)
    layer(cv, cam, cn, L('clouds_storm.png'), -12 * t, -700, 3100, 0.8)
    rocks = [('rock_01.png', 760, 420, 0.86, 6.2, 7.7), ('rock_02.png', -820, 380, 0.9, 6.4, 7.9),
             ('rock_03.png', 420, 230, 0.7, 6.6, 8.1), ('rock_04.png', -380, 200, 0.68, 6.8, 8.3)]
    for n, x, w, d, t0, t1 in rocks:
        if t > t0 and os.path.exists(L(n)):
            ky = [(t0, -1050), (t1, -470 + (0.9 - d) * 500, 'outCubic'), (9.75, -455 + (0.9 - d) * 500)]
            layer(cv, cam, cn, L(n), x, K(t, ky), w, d, next_xy=(x, K(t + 1 / FPS, ky)),
                  tint=(0.66, 0.7, 0.88), rot=2 * math.sin(t + x))
    debris(cv, cam, t, 7.85, 9.75, 21, d0=0.9, region=(-900, 900), tint=(0.6, 0.62, 0.78))
    motes(cv, cam, t, 23, 16, (-1100, 1100, -500, 450), 12, 0.5, col=(0.85, 0.8, 1.0), alpha=0.3, drift=(4, -20))


_DEBRIS = None


def debris_names():
    global _DEBRIS
    if _DEBRIS is None:
        _DEBRIS = sorted(n for n in os.listdir(A2) if n.startswith('debris_')) if os.path.isdir(A2) else []
    return _DEBRIS


def debris(cv, cam, t, t0, t1, seed, d0=0.9, region=(-900, 900), tint=None, n=12, size=(40, 120), top=620):
    """Separate painted rock sprites falling past, each with its own spin and a motion blur from its own speed."""
    names = debris_names()
    if not names or not (t0 <= t <= t1):
        return
    r = np.random.default_rng(seed)
    for i in range(n):
        nm = names[i % len(names)]
        x = r.uniform(*region)
        st = t0 + r.uniform(0, 0.8)
        g = r.uniform(900, 1500)
        w = r.uniform(*size)
        d = d0 + r.uniform(-0.12, 0.08)
        spin = r.uniform(-260, 260)
        vx = r.uniform(-60, 60)
        def pos(tt):
            u = tt - st
            return x + vx * u, top - 0.5 * g * u * u
        if t < st:
            continue
        px, py = pos(t)
        if py < -700:
            continue
        nx, ny = pos(t + 1 / FPS)
        layer(cv, cam, cn_hold[0], L(nm), px, py, w * (0.6 + 0.6 * d), d, rot=spin * (t - st), tint=tint,
              next_xy=(nx, ny))


cn_hold = [None]


def bez(u):
    hx, hy = horizon_xy()
    p = [(-1050, 420), (-420, 380), (hx - 120, hy + 260), (hx, hy)]
    a = (1 - u) ** 3, 3 * u * (1 - u) ** 2, 3 * u ** 2 * (1 - u), u ** 3
    return sum(a[i] * p[i][0] for i in range(4)), sum(a[i] * p[i][1] for i in range(4))


def horizon_xy():
    c = cam_shrine(12.40)
    x, y, _ = c.place(SHRINE_EGG[0], SHRINE_EGG[1], 1, 0.5)
    return x, y


def cam_fall(t):
    u = ease('outCubic', (t - 9.75) / 2.25)
    return Cam(60 + 180 * u, -60 - 80 * u, 1.10 + 0.03 * (t - 9.75) / 2.25, shake_at(t))


def shot_fall(cv, t, cam, cn):
    sub = Cam(cam.x, cam.y, cam.z * 1.15, cam.shake)
    subn = Cam(cn.x, cn.y, cn.z * 1.15, cn.shake) if cn else None
    layer(cv, sub, subn, L('sky_storm.jpg'), 0, 60, 2600, 0.12)
    draw_crack(cv, sky_crack_px(cv, sub), 1.0, 5 * cv.S, alpha=0.55)
    far_islands(cv, sub, subn, t, 0.35, (0.36, 0.4, 0.56), y=-170, scale=0.8)
    layer(cv, sub, subn, L('clouds_storm.png'), -12 * t, -560, 3100, 0.5, tint=(0.85, 0.85, 0.95))
    u = K(t, [(9.75, 0.0), (10.50, 1.0, 'outCubic')])
    hx, hy = horizon_xy()
    if t < 10.55:
        x, y = bez(u)
        sc = 1.0 - 0.82 * u
        # light on the clouds under the comet
        dot(cv, x, y - 160 * sc, 900 * sc + 200, (1.0, 0.65, 0.35), 0.35)
        # the trail: a tapered streak along the path behind the body (light), plus embers peeling off it
        H_, W_ = cv.rgb.shape[:2]
        m = np.zeros((H_, W_), np.float32)
        us = np.linspace(max(0.0, u - 0.45), u, 40)
        pts = [cv.to_px(*bez(q)) for q in us]
        for i in range(len(pts) - 1):
            f = (i + 1) / len(pts)
            th = max(1, int((4 + 46 * f ** 1.5) * cv.S * (1.0 - 0.75 * us[i])))
            cv2.line(m, tuple(np.round(pts[i]).astype(int)), tuple(np.round(pts[i + 1]).astype(int)), f ** 1.3, th,
                     cv2.LINE_AA)
        streak = cv2.GaussianBlur(m, (0, 0), 6 * cv.S + 1)
        halo = cv2.GaussianBlur(m, (0, 0), 30 * cv.S + 1)
        add = streak[..., None] * np.array([1.0, 0.72, 0.38], np.float32) * 1.3 + halo[..., None] * np.array([1.0, 0.45, 0.2], np.float32) * 0.9
        cv.rgb += add
        cv.glow += add * 0.6
        r = np.random.default_rng(int(t * 30))
        for k in range(10):
            q = max(0.0, u - r.uniform(0.02, 0.35))
            ex, ey = bez(q)
            dot(cv, ex + r.uniform(-30, 30), ey + r.uniform(-40, 10), r.uniform(14, 34) * (1 - 0.7 * q), (1.0, 0.7, 0.35),
                0.8 * (1 - (u - q) / 0.35))
        dot(cv, x, y, 700 * sc, (1.0, 0.75, 0.4), 1.0)
        dot(cv, x, y, 300 * sc, (0.85, 1.0, 0.6), 1.0)
        un = K(t + 1 / FPS, [(9.75, 0.0), (10.50, 1.0, 'outCubic')])
        xn, yn = bez(un)
        cv.draw(L('egg.png'), x, y, 120 * sc, alpha=0.9, gain=1.6, blur_vec=((xn - x) * cv.S, -(yn - y) * cv.S),
                rot=-30 * u)
    g = K(t, [(10.42, 0.0), (10.55, 1.0, 'outCubic'), (11.2, 0.5), (12.8, 0.65)])
    if g > 0:
        dot(cv, hx, hy, 380, (0.7, 1.0, 0.55), g)
        dot(cv, hx, hy, 120, (1.0, 1.0, 0.9), g * 0.8)
    if t > 10.5:
        e = t - 10.5
        cv.draw(PUFF, hx, hy + 20 + 40 * e, 140 + 220 * min(1, e), alpha=0.45 * max(0, 1 - e / 1.6),
                tint=(0.55, 0.55, 0.65))


SHRINE_EGG = (0, 62)
SHRINE_EGG_W = 230
SHRINE = dict(x=0, y=-20, w=1900)


def cam_shrine(t):
    u = (t - 11.2) / (17.25 - 11.2)
    e = ease('inOutCubic', min(1.0, max(0.0, u)))
    return Cam(-40 + 80 * e, -40 + 24 * e, 1.00 + 0.13 * e, shake_at(t))


def shot_shrine(cv, t, cam, cn, storm=False):
    gk = (0.55, 0.62, 0.85) if storm else None
    layer(cv, cam, cn, L('sky_night.jpg'), 3 * t, 40, 2600, 0.08, tint=(0.7, 0.75, 0.9) if storm else None)
    far_islands(cv, cam, cn, t, 0.25, (0.2, 0.3, 0.42), y=-40, drift=-3)
    layer(cv, cam, cn, L('shrine.png'), SHRINE['x'], SHRINE['y'], SHRINE['w'], 0.5, tint=gk)
    breathe = 0.82 + 0.18 * math.sin(2 * math.pi * (t - 12.0) / 3.0)
    rem = K(t, [(14.6, 0.0), (15.2, 0.35), (16.4, 0.05)])
    if not storm:
        ex, ey, _ = cam.place(SHRINE_EGG[0], SHRINE_EGG[1], 1, 0.5)
        dot(cv, ex, ey, SHRINE_EGG_W * 5.5, (0.55, 1.0, 0.45), (0.30 + rem) * breathe)      # light on the stones
        dot(cv, ex, ey + 10, SHRINE_EGG_W * 2.2, (0.75, 1.0, 0.6), (0.45 + rem) * breathe)
        layer(cv, cam, cn, L('egg.png'), SHRINE_EGG[0], SHRINE_EGG[1], SHRINE_EGG_W, 0.5)
        for s_, d_, n_ in ((51, 0.35, 16), (52, 0.6, 18), (53, 0.92, 8)):
            motes(cv, cam, t, s_, n_, (-1000, 1000, -380, 320), 10 + 14 * d_, d_, col=(0.75, 1.0, 0.5), alpha=0.65,
                  drift=(3, 5), twinkle=0.85)
    sway = 6 * math.sin(2 * math.pi * 0.25 * t)
    layer(cv, cam, cn, L('shrine_fg.png'), sway, -40, 2350, 0.95, tint=(0.55, 0.6, 0.75) if not storm else (0.4, 0.45, 0.6))


def cam_peoples(t):
    u = (t - 17.25) / 2.25
    return Cam(-90 + 180 * u, -10, 1.06 + 0.02 * u, shake_at(t))


def unfurl(t0, cy, h):
    def m(xx, yy):
        u = ease('outCubic', (T[0] - t0) / 0.45)
        edge = cy + h / 2 - u * h * 1.05
        return np.clip((yy - edge) / 30.0, 0, 1)
    return m


T = [0.0]


def shot_peoples(cv, t, cam, cn):
    layer(cv, cam, cn, L('sky_split.jpg'), 0, 60, 2600, 0.1)
    layer(cv, cam, cn, L('meadow.png'), 0, -270, 2400, 0.42)
    W1, W2 = 17.90, 18.25
    for name, x, t0 in ((L('banner_azure.png'), -700, W1), (L('banner_crimson.png'), 700, W2)):
        h = 560
        w = h / tex_aspect(name)
        T[0] = t
        sway = 1.2 * math.sin(2 * math.pi * 0.5 * t + x)
        cy = -230 + h / 2
        a = K(t, [(t0 - 0.3, 0.0), (t0, 1.0)])
        layer(cv, cam, cn, name, x, cy, w, 0.55, rot=sway, alpha=a)
    # (cut-out, x, feet, height, depth, entry order, mirrored): every figure faces the centre of the frame
    rows = [(L('miner_stand.png'), -470, -250, 300, 0.62, 2, -1), (L('demolitions_stand_cr.png'), 470, -250, 285, 0.62, 3, -1),
            (L('healer_stand.png'), -760, -225, 265, 0.56, 6, 1), (L('healer_stand_cr.png'), 760, -225, 265, 0.56, 7, -1),
            (L('farmer_stand.png'), -640, -300, 340, 0.72, 0, 1), (L('builder_stand_cr.png'), 640, -300, 345, 0.72, 1, -1),
            (L('warrior_stand.png'), -300, -370, 420, 0.82, 4, 1), (L('warrior_stand2_cr.png'), 300, -370, 430, 0.82, 5, -1)]
    for name, x, feet, h, d, i, face in rows:
        if not os.path.exists(name):
            continue
        t0 = 17.55 + 0.10 * (i // 2) + (0.05 if i % 2 else 0)
        side = -1 if x < 0 else 1
        def xpos(tt):
            u = (tt - t0) / 0.55
            return x + side * 650 * (1 - ease('outCubic', u)) if u < 1 else x
        br = 0.012 * math.sin(2 * math.pi * 0.35 * t + i)
        if t >= t0 - 0.05:
            lh(cv, cam, cn, name, xpos(t), feet, h * (1 + br), d, flipx=(face < 0),
               next_feet=(xpos(t + 1 / FPS), feet), shadow=0.55)


def cam_hill(t):
    y = K(t, [(19.50, 70), (20.10, 64), (21.20, -60, 'outCubic'), (22.48, -66)])
    z = K(t, [(19.50, 1.10), (20.10, 1.09), (21.20, 1.02, 'outCubic'), (22.48, 1.03), (22.68, 1.07, 'outExpo'),
              (23.25, 1.075)])
    return Cam(K(t, [(19.5, 20), (23.25, 0)]), y, z, shake_at(t))


def shot_hill(cv, t, cam, cn):
    imp = 22.50 <= t < 22.50 + 2 / FPS
    layer(cv, cam, cn, L('hill.jpg'), 0, -20, 2500, 0.45, gain=0.42 if imp else 1.0)
    def lx(tt):
        return K(tt, [(19.5, -1150), (22.15, -470), (22.30, -520, 'outCubic'), (22.50, -238, 'inCubic'),
                      (22.60, -238), (23.25, -262, 'outCubic')])
    def rx(tt):
        return K(tt, [(19.5, 1150), (22.15, 470), (22.30, 520, 'outCubic'), (22.50, 238, 'inCubic'),
                      (22.60, 238), (23.25, 262, 'outCubic')])
    def capped(tt, f):
        v = (f(tt + 1 / FPS) - f(tt))
        return f(tt) + max(-18, min(18, v))      # limited smear: at most an 18-unit blur per frame
    feet = -300
    for name, f, flip in ((L('warrior_charge2_cr.png'), rx, True), (L('warrior_charge.png'), lx, False)):
        lh(cv, cam, cn, name, f(t), feet, 470, 0.85, flipx=flip, next_feet=(capped(t, f), feet), shadow=0.5,
           gain=1.25 if imp else 1.0)
    if 22.50 <= t < 22.50 + 3 / FPS:
        px, py, _ = cam.place(0, feet + 290, 1, 0.85)
        cv.draw(STAR, px, py, 230 * (1 + 2 * (t - 22.5)), alpha=0.7, add=True, tint=(1.0, 0.92, 0.7))
    if 22.50 <= t < 23.25:
        u = (t - 22.50) / 0.75
        r = np.random.default_rng(7)
        for i in range(16):
            ang = r.uniform(math.pi * 0.05, math.pi * 0.95) * (1 if i % 2 else -1)
            dist = 30 + 300 * ease('outCubic', u) * r.uniform(0.5, 1.0)
            px, py = dist * math.cos(ang), feet + 40 + abs(dist * 0.35 * math.sin(ang)) + 60 * u
            layer(cv, cam, None, PUFF, px, py, 90 + 130 * u, 0.88, alpha=0.45 * max(0, 1 - u ** 1.3),
                  tint=(0.93, 0.9, 0.82))


def cam_choice(t):
    x = K(t, [(23.25, -250), (25.80, -290), (26.07, -40, 'inOutCubic'), (28.50, 0)])
    return Cam(x, -20, 1.10, shake_at(t))


CHOICE_EGG = (-26, 105)
CHOICE_EGG_W = 240


def shot_choice(cv, t, cam, cn):
    layer(cv, cam, cn, L('choice.jpg'), 0, 30, 2600, 0.5)
    feed = K(t, [(23.8, 0.0), (25.2, 1.0)])
    pulse = 1 + 0.04 * math.sin(math.pi * min(1, max(0, (t - 25.0) / 0.5)))
    jit = 6 * math.sin(60 * t) if 26.6 < t < 26.75 else 0.0
    glow_col = (0.6 + 0.2 * feed, 1.0, 0.45) if t < 25.85 else (0.75, 0.45, 1.0)
    flick = 1.0
    for ft in (26.3, 26.9):
        if ft < t < ft + 0.12:
            flick = 0.35
    ex, ey, ew = cam.place(CHOICE_EGG[0] + jit, CHOICE_EGG[1], CHOICE_EGG_W, 0.5)
    dot(cv, ex, ey, ew * 3.2, glow_col, (0.35 + 0.5 * feed) * flick)
    layer(cv, cam, cn, L('egg.png'), CHOICE_EGG[0] + jit, CHOICE_EGG[1], CHOICE_EGG_W * pulse, 0.5)
    creep = K(t, [(25.9, 0.0), (27.4, 120, 'outCubic')])
    layer(cv, cam, cn, L('corruption.png'), 600 - creep, -330, 720, 0.75)
    layer(cv, cam, cn, L('biomass.png'), -300, -300, 520, 0.72, gain=1.0 + 0.12 * feed)
    sway = 1 + 0.01 * math.sin(2 * math.pi * 0.4 * t)
    back = K(t, [(26.0, 0.0), (26.5, 60, 'outCubic')])          # the Farmer recoils as the corruption reaches the egg
    lh(cv, cam, cn, L('farmer_feed.png'), -600 - back, -440, 470 * sway, 0.9)
    if t > 25.95:
        # the corruption acts on the egg: violet thorn-light crawls from the crystals up into the shell
        r = np.random.default_rng(11)
        reach = K(t, [(25.95, 0.0), (27.2, 1.0, 'outCubic')])
        for i in range(18):
            ph = r.uniform(0, 1)
            u = ((t - 25.95) * 0.8 + ph) % 1.0
            u = min(u, reach)
            sx, sy = 470 - creep + r.uniform(-60, 60), -230 + r.uniform(-40, 40)
            x = sx + (CHOICE_EGG[0] + 60 - sx) * ease('inOutSine', u)
            y = sy + (CHOICE_EGG[1] - 40 - sy) * u + 50 * math.sin(math.pi * u) * (1 if i % 2 else -0.4)
            px, py, _ = cam.place(x, y, 1, 0.7)
            dot(cv, px, py, 64, (0.72, 0.3, 1.0), 1.0 * math.sin(math.pi * max(0.05, u)))
            dot(cv, px, py, 22, (0.95, 0.8, 1.0), 0.9 * math.sin(math.pi * max(0.05, u)))
        dot(cv, ex + ew * 0.18, ey, ew * 1.3, (0.7, 0.3, 1.0), 0.55 * reach * flick)
    if 23.8 < t < 25.4:
        r = np.random.default_rng(3)
        for i in range(14):
            ph = r.uniform(0, 1)
            u = ((t - 23.8) * 0.9 + ph) % 1.0
            sx, sy = -400 + r.uniform(-40, 40), -170 + r.uniform(-30, 30)
            x = sx + (CHOICE_EGG[0] - sx) * ease('inOutSine', u)
            y = sy + (CHOICE_EGG[1] - sy) * u + 70 * math.sin(math.pi * u)
            px, py, _ = cam.place(x, y, 1, 0.7)
            dot(cv, px, py, 34, (0.65, 1.0, 0.3), 0.9 * math.sin(math.pi * u))


def egg_screen_at_choice_end():
    cam = cam_choice(28.4999)
    return cam.place(CHOICE_EGG[0], CHOICE_EGG[1], CHOICE_EGG_W, 0.5)


def hatch_frame():
    ex, ey, ew = egg_screen_at_choice_end()
    k = ew / SHRINE_EGG_W
    return ex, ey, ew, k


def shot_hatch(cv, t, cam, cn):
    ex, ey, ew, k = hatch_frame()
    creep = 1 + K(t, [(28.5, 0.0), (30.30, 0.012), (31.45, 0.04, 'inCubic'), (32.25, 0.045)])
    sx, sy = shake_at(t)
    def P(x, y, w):
        return ex + (x - SHRINE_EGG[0]) * k * creep + sx, ey + (y - SHRINE_EGG[1]) * k * creep + sy, w * k * creep
    x, y, w = P(0, 40, 2600)
    cv.draw(L('sky_night.jpg'), x, y, w, tint=(0.6, 0.65, 0.85))
    x, y, w = P(SHRINE['x'], SHRINE['y'], SHRINE['w'])
    cv.draw(L('shrine.png'), x, y, w, tint=(0.5, 0.56, 0.8))
    x, y, w = P(0, 300, 2000)
    cv.draw(RAYS, x, y - 200 * k, w, alpha=0.22, add=True, tint=(0.7, 0.8, 1.0))
    x, y, w = P(SHRINE_EGG[0], SHRINE_EGG[1], SHRINE_EGG_W)
    steps = [29.25, 30.00, 30.75, 31.13]
    ci = sum(1 for s in steps if t >= s)
    if t < 31.50:
        jit = 4 * math.sin(80 * t) * (1 if any(s <= t < s + 0.1 for s in steps) else 0)
        dot(cv, x + jit, y, w * 2.4, (0.6, 1.0, 0.5), 0.18 + 0.1 * ci)
        cv.draw(L('egg.png'), x + jit, y, w, gain=0.8)
        if ci:
            px, py = cv.to_px(x + jit, y)
            hpx = w * tex_aspect(L('egg.png')) * cv.S
            wpx = w * cv.S
            H_, W_ = cv.rgb.shape[:2]
            yy, xx = np.ogrid[0:H_, 0:W_]
            clip = (((xx - px) / (wpx * 0.42)) ** 2 + ((yy - py) / (hpx * 0.45)) ** 2 <= 1).astype(np.float32)
            u = [0.35, 0.6, 0.85, 1.0][ci - 1]
            draw_crack(cv, lambda ux, uy: (px + ux * wpx * 0.36, py - uy * hpx * 0.4), u, max(2, 3 * k * cv.S),
                       core=(0.95, 1.0, 0.8), halo=(0.55, 1.0, 0.4), alpha=0.8 + 0.1 * ci, clip=clip)
    else:
        u = (t - 31.50) / 0.75
        rise = 60 * ease('outBack', min(1, u * 1.6))
        hw = w * 1.35 * (0.85 + 0.15 * ease('outBack', min(1, u * 2)))
        dot(cv, x, y + rise * k, hw * 2.4, (0.6, 1.0, 0.5), 0.35 * max(0.25, 1 - u))
        cv.draw(L('hatchling.png'), x, y + (rise - 20) * k, hw)
        shells(cv, x, y, w, t)
    kk = light(t, 31.50, 3, 10, 0.6)
    if kk > 0:
        dot(cv, x, y, w * 7, (0.85, 1.0, 0.75), kk * 1.4)
    fl = K(t, [(32.25 - 4 / FPS, 0.0), (32.25, 0.8)])
    if fl > 0:
        dot(cv, x, y, w * 4, (0.7, 1.0, 0.55), fl)


@lru_cache(maxsize=4)
def shell_pieces(width_px):
    t = tex(L('egg.png'), width_px).copy()
    h, w = t.shape[:2]
    pieces = []
    cx, cy = w / 2, h * 0.55
    for i in range(7):
        a0, a1 = i * 2 * math.pi / 7 + 0.2, (i + 1) * 2 * math.pi / 7 + 0.2
        poly = np.array([[cx, cy], [cx + 2 * w * math.cos(a0), cy + 2 * h * math.sin(a0)],
                         [cx + 2 * w * math.cos(a1), cy + 2 * h * math.sin(a1)]], np.int32)
        m = np.zeros((h, w), np.uint8)
        cv2.fillPoly(m, [poly], 1)
        p = t * m[..., None]
        ys, xs = np.nonzero(p[..., 3])
        if len(xs) == 0:
            continue
        crop = p[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
        pieces.append((crop, ((xs.min() + xs.max()) / 2 - cx) / w, ((ys.min() + ys.max()) / 2 - cy) / h))
    return pieces


def shells(cv, x, y, w, t):
    u = t - 31.50
    if u > 0.9:
        return
    wpx = int(w * cv.S)
    for i, (crop, ox, oy) in enumerate(shell_pieces(max(16, wpx))):
        d = 1 + 5 * u + 4 * u * u
        px = x + ox * w * d
        py = y - oy * w * d * 1.1 - 250 * u * u
        v = ((ox * w * 9 + ox * w * 12 * u) * cv.S / FPS, (oy * w * 10) * cv.S / FPS)
        draw_raw(cv, crop, px, py, rot=200 * u * (1 if i % 2 else -1), alpha=max(0, 1 - u / 0.9), blur=v)


def draw_raw(cv, img, cx, cy, rot=0.0, alpha=1.0, blur=None):
    th, tw = img.shape[:2]
    px, py = cv.to_px(cx, cy)
    r = math.radians(rot)
    c, s = math.cos(r), math.sin(r)
    ext = int(math.hypot(tw, th) / 2 + 4 + (abs(blur[0]) + abs(blur[1]) if blur else 0))
    x0, x1 = int(max(0, px - ext)), int(min(cv.W, px + ext))
    y0, y1 = int(max(0, py - ext)), int(min(cv.H, py + ext))
    if x1 <= x0 or y1 <= y0:
        return
    M = np.array([[c, s, 0], [-s, c, 0]], np.float64)
    M[:, 2] = np.array([px - x0, py - y0]) - M[:, :2] @ np.array([tw / 2, th / 2])
    out = cv2.warpAffine(img, M, (x1 - x0, y1 - y0), flags=cv2.INTER_LINEAR)
    if blur:
        out = motion_blur(out, blur)
    o = out.astype(np.float32) / 255
    d = cv.rgb[y0:y1, x0:x1]
    d *= 1 - o[..., 3:4] * alpha
    d += o[..., :3] * alpha


def cam_kaiju(t):
    y = K(t, [(32.25, -520), (34.40, 30, 'outCubic'), (40.50, 70)])
    z = K(t, [(32.25, 1.00), (34.50, 1.00), (36.00, 1.06, 'outCubic'), (40.50, 1.08)])
    return Cam(K(t, [(32.25, 10), (40.5, -25)]), y, z, shake_at(t))


KAIJU_STATES = ('neutral', 'azure', 'crimson', 'violet')
KX = 300


def shot_kaiju(cv, t, cam, cn):
    roar = K(t, [(34.50, 0.0), (34.56, 1.0), (35.30, 0.0, 'outCubic')])
    if roar > 0.02:
        for k_, sc_ in enumerate((1.0, 1.03, 1.06, 1.09)):
            layer(cv, cam, None, L('sky_storm.jpg'), 0, 0, 2700 * (1 + (sc_ - 1) * roar), 0.1, flipx=True, gain=0.8,
                  alpha=1.0 if k_ == 0 else 0.5 / k_)
    else:
        layer(cv, cam, cn, L('sky_storm.jpg'), 0, 0, 2700, 0.1, flipx=True, gain=0.8)
    # the crack scar from the opening, still glowing behind the creature
    def scar(ux, uy):
        sx, sy, _ = cam.place(-80 + ux * 520, 470 + uy * 380, 1, 0.12)
        return cv.to_px(sx, sy)
    draw_crack(cv, scar, 1.0, 5 * cv.S, alpha=0.5 + 0.1 * math.sin(t * 2))
    layer(cv, cam, cn, RAYS, -80, 300, 2200, 0.2, alpha=0.22, add=True, tint=(0.8, 0.8, 1.0))
    debris(cv, cam, t, 34.65, 36.2, 77, d0=0.55, region=(-800, 800), tint=(0.55, 0.56, 0.66), n=6, size=(40, 90),
           top=900)
    feet, height = -440, 940
    az = K(t, [(35.25, 0.0), (35.45, 1.0, 'outCubic')])
    cr = K(t, [(36.00, 0.0), (36.20, 1.0, 'outCubic')])
    vi = K(t, [(37.50, 0.0), (37.66, 1.0, 'outCubic')])
    wts = dict(neutral=1.0, azure=az, crimson=cr, violet=vi)
    breathe = 1 + 0.012 * math.sin(2 * math.pi * 0.25 * t) + 0.025 * roar
    surge = max(K(t, [(35.20, 0.0), (35.30, 1.0), (35.7, 0.0)]), K(t, [(35.95, 0.0), (36.05, 1.0), (36.45, 0.0)]),
                K(t, [(37.45, 0.0), (37.55, 1.0), (38.0, 0.0)]))
    pulse = 0.65 + 0.2 * math.sin(2 * math.pi * 1.1 * t)
    flick = 0.3 if any(ft < t < ft + 0.08 for ft in (37.95, 38.25)) else 1.0
    top = 'neutral'
    for st in KAIJU_STATES:
        a = wts[st]
        if a <= 0:
            continue
        top = st
        name = L(f'kaiju_{st}.png')
        h = height * breathe
        w = h / tex_aspect(name)
        y = feet + h / 2
        layer(cv, cam, cn, L(f'kaiju_{st}_glow.png'), KX, y, w, 0.7, alpha=(0.6 * pulse + 0.5 * surge) * a * flick, add=True)
        layer(cv, cam, cn, name, KX, y, w, 0.7, alpha=a)
        layer(cv, cam, None, L(f'kaiju_{st}_glow.png'), KX, y, w, 0.7, alpha=0.35 * a * flick * (0.6 + surge), add=True)
    g0 = K(t, [(32.25, 1.0), (32.25 + 6 / FPS, 0.0)])
    if g0 > 0:
        name = L('kaiju_neutral_glow.png')
        w = height / tex_aspect(name)
        layer(cv, cam, None, name, KX, feet + height / 2, w, 0.7, alpha=g0 * 1.5, add=True)
    layer(cv, cam, cn, L('clouds_storm.png'), -15 * t, -820, 3300, 0.9, alpha=0.95)


def cam_hero(t):
    y = K(t, [(40.50, 40), (42.45, 60), (43.25, 95, 'inOutCubic'), (DUR, 105)])
    z = K(t, [(40.50, 1.00), (42.45, 1.03), (43.25, 1.04, 'inOutCubic'), (DUR, 1.05)])
    return Cam(K(t, [(40.5, -40), (DUR, 40)]), y, z, (0.0, 0.0))


def shot_hero(cv, t, cam, cn):
    layer(cv, cam, cn, L('hero.jpg'), 0, 20, 2400, 0.4)
    rise = K(t, [(40.5, -70), (42.45, -50), (43.4, 0, 'inOutCubic'), (DUR, 10)])
    name = L('kaiju_hero_neutral.png')
    h = 820 * (1 + 0.015 * math.sin(2 * math.pi * 0.3 * (t - 40.5)))
    w = h / tex_aspect(name)
    x, feet = 600, -600
    haze = (1.0, 0.84, 0.66)
    layer(cv, cam, cn, L('kaiju_hero_neutral_glow.png'), x, feet + rise + h / 2, w, 0.3, alpha=0.45 + 0.15 * math.sin(t * 3),
          add=True, tint=(1.0, 0.9, 0.55))
    layer(cv, cam, cn, name, x, feet + rise + h / 2, w, 0.3, tint=haze)
    layer(cv, cam, cn, name, x, feet + rise + h / 2, w, 0.3, alpha=0.18, add=True, tint=(0.55, 0.4, 0.25))   # air between
    layer(cv, cam, cn, L('clouds_day.png'), 420 - 14 * t, -520, 3300, 0.34, tint=(1.0, 0.8, 0.58), alpha=0.9)
    layer(cv, cam, cn, RAYS, -500, 200, 2200, 0.2, alpha=0.18 + 0.05 * math.sin(t * 0.9), add=True,
          tint=(1.0, 0.85, 0.55), rot=-18)
    motes(cv, cam, t, 91, 22, (-1100, 1100, -480, 450), 12, 0.6, col=(1.0, 0.82, 0.5), alpha=0.4, drift=(-5, 8))


SHOT_FN = dict(calm=(cam_calm, shot_calm), crack_rise=(cam_crack_rise, shot_crack_rise), fall=(cam_fall, shot_fall),
               shrine=(cam_shrine, shot_shrine), peoples=(cam_peoples, shot_peoples), hill=(cam_hill, shot_hill),
               choice=(cam_choice, shot_choice), hatch=(cam_shrine, shot_hatch), kaiju=(cam_kaiju, shot_kaiju),
               hero=(cam_hero, shot_hero))


def render_shot(sid, t):
    cv = Canvas()
    camf, shotf = SHOT_FN[sid]
    cam = camf(t)
    cn = camf(t + 1 / FPS)
    cn_hold[0] = cn
    shotf(cv, t, cam, cn)
    rgb = cv.rgb * EXPOSURE.get(sid, 1.0)
    return rgb, cv.glow


# ---------------------------------------------------------------- finish: grade, bloom, vignette, grain, type
def lut(x):
    """One grade for the whole film: S-curve, shadows toward deep teal, warm highlights, saturation cap."""
    x = np.clip(x, 0, None)
    x = x / (1 + 0.15 * x)                                        # soft shoulder for additive light
    x = np.clip(x * 1.06, 0, 1)
    x = x + 0.08 * np.sin(2 * np.pi * x) * (-0.25) * 0.0 + (3 * x ** 2 - 2 * x ** 3 - x) * 0.18
    l = (0.2126 * x[..., 0] + 0.7152 * x[..., 1] + 0.0722 * x[..., 2])[..., None]
    sh = np.clip(1 - l / 0.35, 0, 1) ** 2
    x = x + sh * (np.array([0.055, 0.165, 0.2], np.float32) - x) * 0.10
    hi = np.clip((l - 0.7) / 0.3, 0, 1)
    x = x * (1 + hi * np.array([0.03, 0.0, -0.02], np.float32))
    sat = 0.94
    x = l + (x - l) * sat
    return np.clip(x, 0, 1)


def write_cube(path, n=33):
    g = np.linspace(0, 1, n, dtype=np.float32)
    with open(path, 'w') as f:
        f.write('TITLE "critical-mass intro-film-1"\nLUT_3D_SIZE %d\n' % n)
        b, gg, r = np.meshgrid(g, g, g, indexing='ij')
        rgb = np.stack([r, gg, b], -1).reshape(-1, 3)
        out = lut(rgb.reshape(1, -1, 3)).reshape(-1, 3)
        for v in out:
            f.write('%.6f %.6f %.6f\n' % tuple(v))


@lru_cache(maxsize=2)
def vignette(W, H):
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    r = np.hypot((xx - W / 2) / (W / 2), (yy - H / 2) / (H / 2)) / math.sqrt(2)
    return (1 - 0.12 * np.clip((r - 0.35) / 0.65, 0, 1) ** 1.8 * 2.2).clip(0.7, 1)[..., None]


@lru_cache(maxsize=8)
def font(size):
    return ImageFont.truetype(FONT, size)


def title_alpha(t, ti, to):
    """0.4 s fade in with an 8 px rise, hold, 0.4 s fade out."""
    a = min(1, max(0, (t - ti) / 0.4)) * min(1, max(0, (to - t) / 0.4))
    rise = 8 * (1 - ease('outCubic', (t - ti) / 0.4))
    track = 80 - 40 * ease('outCubic', (t - ti) / 1.2)
    return a, rise, track


TEXT_OFF = [False]


def draw_titles(img, t, cam_x=0.0):
    W, H = img.shape[1], img.shape[0]
    S = W / 1920.0
    layer = None
    for ti in TITLES:
        a, rise, track = title_alpha(t, ti['t_in'], ti['t_out'])
        if a <= 0:
            continue
        if layer is None:
            layer = Image.new('RGBA', (W, H), (0, 0, 0, 0))
            dr = ImageDraw.Draw(layer)
        f = font(int(ti['size'] * S))
        tr = track / 1000 * ti['size'] * S
        widths = [dr.textlength(ch, font=f) for ch in ti['text']]
        tw = sum(widths) + tr * (len(widths) - 1)
        x = ti['x']
        if ti.get('cam_locked'):
            x = ti['x'] - (cam_x - (-250 if ti['x'] < 0 else 250)) * 0.6
        cx = W / 2 + x * S
        y = H / 2 - ti['y'] * S + rise * S
        x0 = cx - tw / 2 if ti.get('align') != 'left' else cx
        asc, desc = f.getmetrics()
        if ti.get('plate'):
            pad = 40 * S
            plate = Image.new('L', (W, H), 0)
            pd = ImageDraw.Draw(plate)
            pd.rounded_rectangle([x0 - pad * 1.6, y - asc * 0.62 - pad, x0 + tw + pad * 1.6, y + asc * 0.55 + pad],
                                 radius=int(60 * S), fill=int(255 * PLATE_ALPHA * a))
            plate = plate.filter(__import__('PIL.ImageFilter', fromlist=['x']).GaussianBlur(28 * S))
            shade = Image.new('RGBA', (W, H), (8, 14, 24, 0))
            shade.putalpha(plate)
            layer = Image.alpha_composite(layer, shade)
            dr = ImageDraw.Draw(layer)
        xx = x0
        for ch, cw in zip(ti['text'], widths):
            if not TEXT_OFF[0]:
                for ox, oy in ((2, 3), (3, 4), (0, 2)):
                    dr.text((xx + ox * S, y + oy * S), ch, font=f, fill=(4, 8, 14, int(170 * a)), anchor='ls')
                dr.text((xx, y), ch, font=f, fill=(247, 240, 229, int(255 * a)), anchor='ls')
            xx += cw + tr
    if layer is None:
        return img
    L = np.asarray(layer).astype(np.float32) / 255
    return img * (1 - L[..., 3:4]) + L[..., :3] * L[..., 3:4]


def draw_preview_overlays(frame, t, bar):
    """Evidence only: the engine's subtitle (in the bottom bar area) and the title logo."""
    H, W = frame.shape[:2]
    S = W / 1920.0
    im = Image.fromarray(frame)
    dr = ImageDraw.Draw(im, 'RGBA')
    for s in SUBS:
        a = min(1, max(0, (t - s['t_in']) / 0.15)) * min(1, max(0, (s['t_out'] - t) / 0.15))
        if a > 0:
            f = font(int(30 * S))
            dr.text((W / 2, H - bar + 8 * S + 30 * S), s['text'], font=f, fill=(240, 228, 205, int(255 * a)),
                    anchor='ms')
    if t >= LOGO_KEYS['t_in']:
        lg = Image.open(os.path.join(LOGO, 'critical-mass-bible-logo@2x.png')).convert('RGBA')
        u = (t - LOGO_KEYS['t_in']) / (LOGO_KEYS['land'] - LOGO_KEYS['t_in'])
        if u < 1:
            s_ = LOGO_KEYS['s_from'] + (LOGO_KEYS['s_land'] - LOGO_KEYS['s_from']) * ease('inCubic', u)
            al = ease('outCubic', u)
        else:
            s_ = LOGO_KEYS['s_land'] + (1.0 - LOGO_KEYS['s_land']) * ease('outCubic', (t - LOGO_KEYS['land']) / 0.5)
            al = 1.0
        lw = int(766 * S * s_)
        lg = lg.resize((lw, int(lg.size[1] * lw / lg.size[0])), Image.LANCZOS)
        a = np.asarray(lg).copy()
        a[..., 3] = (a[..., 3] * al).astype(np.uint8)
        lg = Image.fromarray(a)
        cy = H / 2 - 70 * S
        im.alpha_composite(lg, (int(W / 2 - lg.size[0] / 2), int(cy - lg.size[1] / 2))) if im.mode == 'RGBA' else \
            im.paste(lg, (int(W / 2 - lg.size[0] / 2), int(cy - lg.size[1] / 2)), lg)
    return np.asarray(im)


PREVIEW = False
PLATE_ALPHA = float(os.environ.get('FILM_PLATE', '0.62'))
# C19 exposure bridge: across each hard cut the outgoing shot eases its exposure toward the incoming one over its last
# 0.5 s and the incoming shot eases back to its own grade over 0.6 s, so the mean-luma step at the cut is 15 or less
# (measured on the encoded film by check_film.py). (gain on the last frame before the cut, gain on the first frame after)
CUT_GAIN = {19.50: (1.09, 0.93), 28.50: (0.62, 1.6), 32.25: (1.2, 0.83), 40.50: (1.17, 0.86)}


def cut_gain(t):
    g = 1.0
    for c, (go, gi) in CUT_GAIN.items():
        if c - 0.5 <= t < c:
            u = (t - (c - 0.5)) / 0.5
            g *= 1 + (go - 1) * u * u * (3 - 2 * u)
        elif c <= t < c + 0.6:
            u = 1 - (t - c) / 0.6
            g *= 1 + (gi - 1) * u * u * (3 - 2 * u)
    return g


# Grain: luminance only. The plan's 2.5% / 1.2 px does not fit a 60 MB file at 2560x1440 (395 MB at CRF 18); the
# shipped film uses the plan's fallback direction (less, softer grain) so the encoder spends bits on the paintings.
GRAIN = float(os.environ.get('FILM_GRAIN', '0.012'))
GRAIN_BLUR = float(os.environ.get('FILM_GRAIN_BLUR', '1.5'))


def render_frame(i):
    t = i / FPS
    W, H, P, bar = dims()
    s = shot_at(t)
    rgb, glow = render_shot(s['id'], t)
    # transitions
    if 12.00 <= t < 12.80:                         # match dissolve fall -> shrine (horizon glow = shrine egg glow)
        a, ga = render_shot('fall', t)
        w = ease('inOutSine', (t - 12.00) / 0.80)
        rgb = a * (1 - w) + rgb * w
        glow = ga * (1 - w) + glow * w
    if 17.05 <= t < 17.25:                         # glow-bloom into day
        k = ease('inCubic', (t - 17.05) / 0.20)
        rgb = rgb + glow * 0.9 * k + 0.04 * k
    if 17.25 <= t < 17.45:                         # peoples opens slightly over-exposed and settles within 0.2 s
        k = 1 - ease('outCubic', (t - 17.25) / 0.20)
        rgb = rgb * (1 + 0.22 * k) + 0.03 * k
    # bloom on glow layers only (threshold, ~24 px blur, added at 0.3)
    small = cv2.resize(glow, (W // 4, P // 4), interpolation=cv2.INTER_AREA)
    small = cv2.GaussianBlur(small, (0, 0), 24 * SCALE / 4 * 1.5)
    bloom = cv2.resize(small, (W, P), interpolation=cv2.INTER_LINEAR)
    rgb = rgb + bloom * 0.3
    if s['id'] == 'kaiju' and 34.5 <= t < 35.3:     # roar: a radial-ish zoom blur feel, background only via soft glow
        pass
    img = lut(rgb)
    img = img * vignette(W, P)
    # sound before picture: 0.40 s of black, then a 1.2 s fade up (OutSine)
    if t < 1.50:
        img = img * K(t, [(0.33, 0.0), (1.50, 1.0, 'outSine')])
    img = img * cut_gain(t)
    cam_x = cam_choice(t).x if s['id'] == 'choice' else 0.0
    img = draw_titles(img, t, cam_x)
    # grain: luminance only, sigma 2.5% of range, new seed every frame, 1.2 px blur
    r = np.random.default_rng(1000 + i)
    g = r.standard_normal((P, W)).astype(np.float32)
    g = cv2.GaussianBlur(g, (0, 0), max(0.5, GRAIN_BLUR * SCALE))
    g *= GRAIN / (g.std() + 1e-6)
    img = np.clip(img + g[..., None], 0, 1)
    frame = np.zeros((H, W, 3), np.uint8)
    frame[bar:bar + P] = (img * 255 + 0.5).astype(np.uint8)
    if PREVIEW:
        frame = draw_preview_overlays(frame, t, bar)
    return frame


def rel_lum(c):
    c = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    return 0.2126 * c[..., 0] + 0.7152 * c[..., 1] + 0.0722 * c[..., 2]


def measure_titles():
    """WCAG contrast of every title against the pixels right behind its letters (plate and shadow included), at three
    moments of its full-opacity hold; the worst (90th percentile background) is reported."""
    global PREVIEW
    out = []
    for ti in TITLES:
        worst = 99.0
        for u in (0.2, 0.5, 0.8):
            t = ti['t_in'] + 0.45 + u * (ti['t_out'] - ti['t_in'] - 0.9)
            i = int(round(t * FPS))
            TEXT_OFF[0] = True
            a = render_frame(i).astype(np.float32) / 255
            TEXT_OFF[0] = False
            b = render_frame(i).astype(np.float32) / 255
            m = (np.abs(b - a).max(-1) > 0.25) & (b.min(-1) > 0.85)          # the cream letter faces
            if m.sum() < 50:
                continue
            ring = cv2.dilate(m.astype(np.uint8), np.ones((5, 5), np.uint8)).astype(bool)
            Lbg = np.percentile(rel_lum(a)[ring], 90)
            Lt = np.median(rel_lum(b)[m])
            worst = min(worst, (Lt + 0.05) / (Lbg + 0.05))
        out.append(dict(text=ti['text'], contrast=round(worst, 2), ok=worst >= 4.5))
    return out


def table():
    return dict(schema='cm.story.introfilm/1', fps=FPS, frames=FRAMES, seconds=round(DUR, 4), width=2560,
                height=1440, picture=dict(width=2560, height=1280, bar=80, aspect=2.0), bpm=80, beat=BEAT,
                bar_seconds=BAR, downbeats=[1.5 + 3 * k for k in range(16)],
                hero_hits=dict(crack=4.50, horizon=10.50, clash=22.50, hatch=31.50, roar=34.50, berserk=37.50,
                               hero_cut=40.50, logo=43.50),
                shots=SHOTS, cuts=CUTS, hard_cuts=HARD_CUTS, light_events=LIGHT_EVENTS, shakes=SHAKES,
                titles=[dict(text=t['text'], t_in=t['t_in'], t_out=t['t_out'], size=t['size'], x=t['x'], y=t['y'],
                             style='fade-track') for t in TITLES],
                subtitles=SUBS, logo=LOGO_KEYS, skip_lock_seconds=1.5, handoff_dissolve=round(DUR - 46.50, 4))


def main():
    global SCALE, PREVIEW
    a = sys.argv[1:]
    if '--measure-titles' in a:
        SCALE = float(a[a.index('--scale') + 1]) if '--scale' in a else 0.5
        print(json.dumps(measure_titles(), indent=1, default=float))
        return
    if '--table' in a:
        json.dump(table(), open(a[a.index('--table') + 1], 'w'), indent=1)
        return
    out = a[0]
    if '--scale' in a:
        SCALE = float(a[a.index('--scale') + 1])
    PREVIEW = '--preview' in a
    audio = a[a.index('--audio') + 1] if '--audio' in a else None
    fr = range(FRAMES)
    if '--frames' in a:
        x, y = a[a.index('--frames') + 1].split(':')
        fr = range(int(x), int(y))
    step = int(a[a.index('--step') + 1]) if '--step' in a else 1
    fr = list(fr)[::step]
    W, H, _, _ = dims()
    if out.endswith('/'):
        os.makedirs(out, exist_ok=True)
        import multiprocessing as mp
        with mp.get_context('fork').Pool(int(os.environ.get('FILM_WORKERS', '4'))) as pool:
            for i, f in zip(fr, pool.imap(render_frame, fr, chunksize=2)):
                cv2.imwrite(os.path.join(out, 't%06.2f_f%04d.jpg' % (i / FPS, i)), cv2.cvtColor(f, cv2.COLOR_RGB2BGR),
                            [cv2.IMWRITE_JPEG_QUALITY, 88])
        return
    cmd = ['ffmpeg', '-v', 'error', '-y', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', f'{W}x{H}', '-r', str(FPS),
           '-i', '-']
    if audio:
        cmd += ['-i', audio]
    cmd += ['-c:v', 'libx264', '-profile:v', 'high', '-level', '4.2' if W <= 2560 else '5.1', '-pix_fmt', 'yuv420p',
            '-crf', os.environ.get('FILM_CRF', '18'), '-preset', os.environ.get('FILM_PRESET', 'slow'), '-r', str(FPS),
            '-colorspace', 'bt709', '-color_primaries', 'bt709', '-color_trc', 'bt709', '-color_range', 'tv']
    if audio:
        cmd += ['-c:a', 'aac', '-b:a', '256k', '-ar', '48000', '-shortest']
    cmd += ['-movflags', '+faststart', out]
    p = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    import multiprocessing as mp
    with mp.get_context('fork').Pool(int(os.environ.get('FILM_WORKERS', '4'))) as pool:
        for k, f in enumerate(pool.imap(render_frame, fr, chunksize=2)):
            p.stdin.write(f.tobytes())
            if k % 150 == 0:
                print('frame', k, flush=True)
    p.stdin.close()
    p.wait()


if __name__ == '__main__':
    main()
