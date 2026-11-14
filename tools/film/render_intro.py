#!/usr/bin/env python3
"""Story intro 3: the pre-rendered film (plan intro-film-1, sections 1, 2 and 4).

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
    p = os.path.join(ART, name) if os.path.exists(os.path.join(ART, name)) else name
    if not os.path.exists(p):
        for ext in ('.png', '.jpg'):
            if os.path.exists(os.path.join(ART, name + ext)):
                p = os.path.join(ART, name + ext)
                break
    im = cv2.imread(p, cv2.IMREAD_UNCHANGED)
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
        cv.draw('mote', px, py, pw, alpha=a, add=add, tint=col)


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
    dict(text='IT PICKS A SIDE.', t_in=35.40, t_out=37.70, x=0, y=330, size=76, plate=True),
    dict(text='OR NO SIDE AT ALL.', t_in=37.80, t_out=40.10, x=0, y=330, size=76, plate=True),
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


# ---------------------------------------------------------------- shots
def cam_calm(t):
    y = K(t, [(0.0, -230), (0.40, -230), (2.60, -20, 'outCubic'), (3.75, 40)])
    z = K(t, [(0.0, 1.00), (3.75, 1.05)])
    return Cam(K(t, [(0, -20), (3.75, 20)]), y, z, shake_at(t))


def shot_calm(cv, t, cam, cn):
    sky_a = K(t, [(2.3, 0.0), (3.75, 0.55, 'inOutSine')])
    layer(cv, cam, cn, 'sky_day', 6 * t, 0, 2300, 0.1)
    if sky_a > 0:
        layer(cv, cam, cn, 'sky_storm', 0, 40, 2300, 0.12, alpha=sky_a)
    # lightning glow inside the cloud band only (a soft additive flicker masked to the upper sky)
    for lt in (2.95, 3.35):
        k = light(t, lt, 1, 5, 0.18)
        if k > 0:
            layer(cv, cam, None, 'crack_glow', 380, 260, 1300, 0.12, alpha=k, add=True, tint=(0.85, 0.9, 1.0))
    layer(cv, cam, cn, 'basin', 0, -250, 1900, 0.6, glow=False)
    motes(cv, cam, t, 11, 22, (-1100, 1100, -500, 450), 18, 0.55, alpha=0.35, twinkle=0.5)
    # cloud shadow sweep (multiply gradient) 2.6-3.6
    u = (t - 2.6) / 1.0
    if 0 < u < 1.3:
        xs = np.linspace(-1, 1, cv.W)[None, :, None]
        c = -1.4 + 2.8 * u
        sh = 1 - 0.28 * np.exp(-((xs - c) / 0.45) ** 2)
        cv.rgb *= sh
    layer(cv, cam, cn, 'cloudsea', -10 * t, -690, 2700, 0.85)


def cam_crack_rise(t):
    # upward tilt continues from calm (velocity matched ~55 u/s), the inhale and snap at 4.50, then the 16-frame tilt
    # down into the rising land at 6.00, settling into a push that never stops.
    y = K(t, [(3.75, 40), (4.50, 80), (6.00, 120), (6.53, -170, 'inOutCubic'), (9.75, -205)])
    z = K(t, [(3.75, 1.05), (4.30, 1.08), (4.48, 1.06, 'outSine'), (4.68, 1.12, 'outExpo'), (6.00, 1.10),
              (6.53, 1.04, 'inOutCubic'), (9.75, 1.08)])
    return Cam(K(t, [(3.75, 20), (9.75, 60)]), y, z, shake_at(t))


def shot_crack_rise(cv, t, cam, cn):
    layer(cv, cam, cn, 'sky_storm', 0, 40, 2300, 0.15)
    rip = (166, 59)
    # the rip draws on twos from the 4.50 hit
    if t >= 4.50:
        fi = min(5, int((t - 4.50) / (2 / 15)))
        k_open = K(t, [(4.50, 0.6), (4.60, 1.0)])
        layer(cv, cam, cn, f'crack_{fi + 1}', rip[0], rip[1], 2112, 0.2, alpha=k_open)
        g = K(t, [(4.50, 0.0), (4.80, 0.45), (6.0, 0.32), (9.75, 0.28)])
        layer(cv, cam, cn, 'crack_glow', rip[0], rip[1], 2112, 0.2, alpha=g * (0.9 + 0.1 * math.sin(t * 7)),
              add=True, tint=(1.0, 0.94, 0.80))
        # beams arrive through a radial light wipe from the rip (4.55 - 4.90)
        R = K(t, [(4.55, 0.0), (4.90, 1500.0, 'outCubic')])
        sxp, syp, _ = cam.place(rip[0], rip[1], 1, 0.25)
        mask = lambda xx, yy: np.clip((R - np.hypot(xx - sxp, yy - syp)) / 260.0, 0, 1)
        layer(cv, cam, cn, 'shafts', rip[0], rip[1] - 40, 2200, 0.25, alpha=0.38, add=True, mask=mask)
    # light event 1: additive glow masked to the rip, warm, 3 frames up 8 down
    k = light(t, 4.50, 3, 8, 0.45)
    if k > 0:
        layer(cv, cam, None, 'crack_glow', rip[0], rip[1], 2600, 0.2, alpha=k * 1.8, add=True,
              tint=(1.0, 0.913, 0.76))
    # the land rises to meet it (60 u, 6.30 - 8.00) and rocks / debris pass close
    by = K(t, [(3.75, -560), (6.30, -560), (8.00, -500, 'outCubic'), (9.75, -494)])
    layer(cv, cam, cn, 'basin', 0, by, 1900, 0.6)
    layer(cv, cam, cn, 'cloudsea_storm', -12 * t, -760, 2700, 0.85)
    if t > 6.2:
        ry = K(t, [(6.2, -1000), (7.6, -520, 'outCubic'), (9.75, -500)])
        layer(cv, cam, cn, 'rocks_r', 860, ry, 480, 0.9, next_xy=(860, K(t + 1 / FPS, [(6.2, -1000), (7.6, -520, 'outCubic'), (9.75, -500)])))
        ly = K(t, [(6.4, -1100), (7.9, -560, 'outCubic'), (9.75, -540)])
        layer(cv, cam, cn, 'rocks_r', -900, ly, 420, 0.92, flipx=True, tint=(0.75, 0.78, 0.9),
              next_xy=(-900, K(t + 1 / FPS, [(6.4, -1100), (7.9, -560, 'outCubic'), (9.75, -540)])))
    if 7.9 < t < 8.9:
        dk = [(7.9, 650), (8.9, -900, 'inCubic')]
        layer(cv, cam, cn, 'debris', 420, K(t, dk), 1300, 0.97, next_xy=(420, K(t + 1 / FPS, dk)), alpha=0.9)
    motes(cv, cam, t, 23, 16, (-1100, 1100, -500, 450), 16, 0.5, col=(1.0, 0.85, 0.6), alpha=0.3, drift=(4, -20))


def bez(u):
    hx, hy = horizon_xy()
    p = [(-620, 360), (-200, 300), (hx + 260, hy + 160), (hx, hy)]   # screen-space Bezier, entry upper left
    a = (1 - u) ** 3, 3 * u * (1 - u) ** 2, 3 * u ** 2 * (1 - u), u ** 3
    return sum(a[i] * p[i][0] for i in range(4)), sum(a[i] * p[i][1] for i in range(4))


def horizon_xy():
    """Screen position of the shrine egg at the middle of the match dissolve: the comet sinks to exactly there."""
    c = cam_shrine(12.40)
    x, y, _ = c.place(SHRINE_EGG[0], SHRINE_EGG[1], 1, 0.5)
    return x, y


def cam_fall(t):
    # a hard cut-in to 1.25x pivoted near the comet's entry, then tracking along its arc as it recedes
    u = ease('outCubic', (t - 9.75) / 2.25)
    return Cam(60 + 180 * u, -60 - 80 * u, 1.10 + 0.03 * (t - 9.75) / 2.25, shake_at(t))


def shot_fall(cv, t, cam, cn):
    z = 1.25
    sub = Cam(cam.x, cam.y, cam.z * z, cam.shake)
    subn = Cam(cn.x, cn.y, cn.z * z, cn.shake) if cn else None
    layer(cv, sub, subn, 'sky_storm', 0, 40, 2300, 0.15)
    layer(cv, sub, subn, 'crack_6', 166, 59, 2112, 0.2)
    layer(cv, sub, subn, 'crack_glow', 166, 59, 2112, 0.2, alpha=0.28, add=True, tint=(1.0, 0.94, 0.8))
    layer(cv, sub, subn, 'cloudsea_storm', -12 * t, -420, 2700, 0.4)
    layer(cv, sub, subn, 'basin', -120, -494, 1900, 0.6)
    # the comet: crosses the beam 9.90-10.40, arcs away toward the horizon, slowing and shrinking
    u = K(t, [(9.75, 0.0), (10.50, 1.0, 'outCubic')])
    un = K(t + 1 / FPS, [(9.75, 0.0), (10.50, 1.0, 'outCubic')])
    if t < 10.55:
        x, y = bez(u)
        xn, yn = bez(un)
        sc = 1.0 - 0.8 * u
        v = ((xn - x) * cv.S, -(yn - y) * cv.S)
        for k in range(10, 0, -1):                      # ember trail along the path
            uu = max(0.0, u - k * 0.03)
            ex, ey = bez(uu)
            cv.draw('mote', ex, ey, 200 * sc * (1 - k / 12), alpha=0.7 * (1 - k / 11), add=True, tint=(1.0, 0.7, 0.4))
        cv.draw('mote', x, y, 520 * sc, alpha=1.0, add=True, tint=(1.0, 0.85, 0.55), blur_vec=v)
        cv.draw('egg', x, y, 95 * sc, alpha=0.95, gain=1.5, blur_vec=v)
    # the horizon glow at the 10.50 downbeat: small, masked, not a flash; then it keeps a low ember
    hx, hy = horizon_xy()
    g = K(t, [(10.40, 0.0), (10.55, 0.9, 'outCubic'), (11.2, 0.45), (12.8, 0.6)])
    if g > 0:
        cv.draw('egg_glow', hx, hy, SHRINE_EGG_W * 1.25 * 1.0, alpha=g, add=True, tint=(0.75, 1.0, 0.7))
    if t > 10.5:
        cv.draw('mote', hx, hy + 30 * (t - 10.5), 260 * min(1, (t - 10.5)), alpha=0.25 * max(0, 1 - (t - 10.5) / 1.5),
                add=True, tint=(0.8, 0.8, 0.9))


SHRINE_EGG = (-4, -104)          # the painted egg's centre in the shrine plate (stage units at w2300, plate at 0,0)
SHRINE_EGG_W = 270


def cam_shrine(t):
    u = (t - 11.2) / (17.25 - 11.2)
    return Cam(-10 + 20 * u, -40 + 10 * u, 1.00 + 0.07 * u, shake_at(t))


def shot_shrine(cv, t, cam, cn, storm=False):
    layer(cv, cam, cn, 'shrine', 0, 0, 2300, 0.5, gain=0.72 if storm else 1.0,
          tint=(0.82, 0.86, 1.05) if storm else None)
    if storm:
        layer(cv, cam, cn, 'shafts', 0, 120, 2200, 0.25, alpha=0.45, add=True, tint=(0.8, 0.9, 1.0))
    breathe = 0.85 + 0.15 * math.sin(2 * math.pi * (t - 12.0) / 3.0)
    rem = K(t, [(14.6, 0.0), (15.2, 0.25), (16.4, 0.0)])          # brightens on "remembers"
    if not storm:
        layer(cv, cam, cn, 'egg_glow', SHRINE_EGG[0], SHRINE_EGG[1], SHRINE_EGG_W * 1.25, 0.5,
              alpha=(0.55 + rem) * breathe, add=True, tint=(0.8, 1.0, 0.75))
        layer(cv, cam, cn, 'egg', SHRINE_EGG[0], SHRINE_EGG[1], SHRINE_EGG_W, 0.5)
        motes(cv, cam, t, 51, 26, (-1000, 1000, -350, 300), 22, 0.75, col=(0.75, 1.0, 0.55), alpha=0.6,
              drift=(3, 4), twinkle=0.8)
    layer(cv, cam, cn, 'rocks_r', 1010, -470, 430, 1.0, tint=(0.16, 0.2, 0.32))
    layer(cv, cam, cn, 'rocks_r', -1060, -520, 380, 1.0, flipx=True, tint=(0.14, 0.17, 0.28))


def cam_peoples(t):
    u = (t - 17.25) / 2.25
    return Cam(-90 + 180 * ease('linear', u), -10, 1.06, shake_at(t))


def unfurl(t0, cy, h):
    """Mask-reveal from the pole top downward."""
    def m(xx, yy):
        u = ease('outCubic', (T[0] - t0) / 0.45)
        edge = cy + h / 2 - u * h * 1.05
        return np.clip((yy - edge) / 30.0, 0, 1)
    return m


T = [0.0]


def shot_peoples(cv, t, cam, cn):
    layer(cv, cam, cn, 'sky_split', 0, 0, 2300, 0.15)
    W1, W2 = 18.33, 18.62
    for name, x, t0 in (('banner_azure', -600, W1), ('banner_crimson', 600, W2)):
        if t >= t0 - 0.02:
            h = 300 * tex_aspect(name)
            T[0] = t
            sway = 2.0 * math.sin(2 * math.pi * 0.6 * t + x)
            layer(cv, cam, cn, name, x, 150, 300, 0.6, rot=sway, mask=unfurl(t0, 150, h))
    rows = [('farmer', -610, -175, 405, 0.72, 0), ('farmer_crimson', 610, -175, 428, 0.72, 1),
            ('warrior', -250, -225, 460, 0.8, 4), ('warrior_crimson', 250, -225, 473, 0.8, 5),
            ('miner', -455, -265, 398, 0.82, 2), ('miner_crimson', 455, -265, 358, 0.82, 3)]
    for name, x, y, w, d, i in sorted(rows, key=lambda r: r[4]):
        t0 = 18.70 + 0.12 * (i // 2) * 1.0 + (0.06 if i % 2 else 0)
        side = -1 if x < 0 else 1
        def xpos(tt):
            u = (tt - t0) / 0.5
            return x + side * 700 * (1 - ease('outBack', u)) if u < 1 else x
        br = 1 + 0.015 * math.sin(2 * math.pi * 0.3 * t + i)
        if t >= t0 - 0.05:
            layer(cv, cam, cn, name, xpos(t), y + w * tex_aspect(name) * (br - 1) / 2, w, d,
                  next_xy=(xpos(t + 1 / FPS), y))


def cam_hill(t):
    # opens on the portal, pulls down and out to the champions (20.10-21.20), locks, snaps in 4% on contact
    y = K(t, [(19.50, 70), (20.10, 64), (21.20, -140, 'outCubic'), (22.48, -150)])
    z = K(t, [(19.50, 1.10), (20.10, 1.09), (21.20, 1.02, 'outCubic'), (22.48, 1.03), (22.68, 1.07, 'outExpo'),
              (23.25, 1.075)])
    return Cam(K(t, [(19.5, 20), (23.25, 0)]), y, z, shake_at(t))


def shot_hill(cv, t, cam, cn):
    layer(cv, cam, cn, 'hill', 0, -235, 2600, 0.5)
    # champions approach, coil back 0.15 s at 22.15, lunge, contact on the 22.50 downbeat, hold 80 ms
    def lx(tt):
        return K(tt, [(19.5, -980), (22.15, -560), (22.30, -600, 'outCubic'), (22.50, -330, 'inCubic'),
                      (22.58, -330), (23.25, -350, 'outCubic')])
    def rx(tt):
        return K(tt, [(19.5, 1000), (22.15, 590), (22.30, 630, 'outCubic'), (22.50, 360, 'inCubic'),
                      (22.58, 360), (23.25, 380, 'outCubic')])
    layer(cv, cam, cn, 'warrior_cleave_crimson', rx(t), -300, 620, 0.9, next_xy=(rx(t + 1 / FPS), -300))
    layer(cv, cam, cn, 'warrior_charge', lx(t), -330, 860, 0.9, next_xy=(lx(t + 1 / FPS), -330))
    if 22.50 <= t < 23.25:                                           # painted-style dust burst (soft puffs, no ring)
        u = (t - 22.50) / 0.27
        r = np.random.default_rng(7)
        for i in range(14):
            ang = r.uniform(0, 2 * math.pi)
            dist = 40 + 260 * ease('outCubic', u) * r.uniform(0.5, 1.0)
            px, py = 15 + dist * math.cos(ang), -330 + dist * 0.45 * math.sin(ang) + 40 * u
            layer(cv, cam, None, 'mote', px, py, 160 + 120 * u, 0.92, alpha=0.32 * max(0, 1 - u / 2.8),
                  tint=(0.85, 0.75, 0.6), add=False)



def cam_choice(t):
    # framed on the feed half, drifting left; an 8-frame whip right on "Corrupt" (25.80), then a slow drift
    x = K(t, [(23.25, -250), (25.80, -290), (26.07, 250, 'inOutCubic'), (28.50, 290)])
    return Cam(x, -20, 1.12, shake_at(t))


CHOICE_EGG = (0, -60)
CHOICE_EGG_W = 366


def shot_choice(cv, t, cam, cn):
    layer(cv, cam, cn, 'sky_choice', 0, 0, 2300, 0.15)
    feed = K(t, [(23.8, 0.0), (25.2, 1.0)])
    pulse = 1 + 0.04 * math.sin(math.pi * min(1, max(0, (t - 25.0) / 0.5)))
    jit = 6 * math.sin(60 * t) if 26.6 < t < 26.75 else 0.0
    glow_col = (0.7 + 0.3 * feed, 1.0, 0.55) if t < 25.85 else (0.8, 0.55, 1.0)
    flick = 1.0
    for ft in (26.3, 26.9):
        if ft < t < ft + 0.12:
            flick = 0.35
    layer(cv, cam, cn, 'egg_glow', CHOICE_EGG[0] + jit, CHOICE_EGG[1], CHOICE_EGG_W * pulse, 0.5,
          alpha=(0.4 + 0.5 * feed) * flick, add=True, tint=glow_col)
    layer(cv, cam, cn, 'egg', CHOICE_EGG[0] + jit, CHOICE_EGG[1], CHOICE_EGG_W * pulse, 0.5)
    layer(cv, cam, cn, 'biomass_plants_glow', -560, -365, 726, 0.8, alpha=0.5 + 0.3 * feed, add=True)
    layer(cv, cam, cn, 'biomass_plants', -560, -365, 726, 0.8)
    creep = K(t, [(25.9, 0.0), (27.4, 110, 'outCubic')])
    layer(cv, cam, cn, 'corruption_growth_glow', 560 - creep, -365, 737, 0.8, alpha=0.55, add=True)
    layer(cv, cam, cn, 'corruption_growth', 560 - creep, -365, 737, 0.8)
    if 23.8 < t < 25.4:                                              # lime motes stream from the plants into the egg
        r = np.random.default_rng(3)
        for i in range(12):
            ph = r.uniform(0, 1)
            u = ((t - 23.8) * 0.9 + ph) % 1.0
            sx, sy = -520 + r.uniform(-120, 120), -280 + r.uniform(-60, 80)
            x = sx + (CHOICE_EGG[0] - sx) * ease('inOutSine', u)
            y = sy + (CHOICE_EGG[1] - sy) * u + 60 * math.sin(math.pi * u)
            layer(cv, cam, None, 'mote', x, y, 26, 0.6, alpha=0.8 * math.sin(math.pi * u), add=True,
                  tint=(0.7, 1.0, 0.35))
    layer(cv, cam, cn, 'farmer_harvest', -490, -235, 549, 0.95)


def egg_screen_at_choice_end():
    cam = cam_choice(28.4999)
    return cam.place(CHOICE_EGG[0], CHOICE_EGG[1], CHOICE_EGG_W, 0.5)


def hatch_frame():
    """The shrine plate placed so its plinth egg sits exactly where the choice egg was (match cut)."""
    ex, ey, ew = egg_screen_at_choice_end()
    k = ew / SHRINE_EGG_W                    # plate scale so the painted egg matches the choice egg's size
    return ex, ey, ew, k


def shot_hatch(cv, t, cam, cn):
    ex, ey, ew, k = hatch_frame()
    creep = 1 + K(t, [(30.30, 0.0), (31.45, 0.03, 'inCubic'), (32.25, 0.035)])
    sx, sy = shake_at(t)
    def P(x, y, w):       # plate-local stage units -> screen, scaled about the egg with the creep
        return ex + (x - SHRINE_EGG[0]) * k * creep + sx, ey + (y - SHRINE_EGG[1]) * k * creep + sy, w * k * creep
    x, y, w = P(0, 0, 2300)
    cv.draw('shrine', x, y, w, gain=0.62, tint=(0.8, 0.85, 1.08))
    x, y, w = P(0, 160, 2200)
    cv.draw('shafts', x, y, w, alpha=0.5, add=True, tint=(0.8, 0.9, 1.0))
    x, y, w = P(SHRINE_EGG[0], SHRINE_EGG[1], SHRINE_EGG_W)
    steps = [29.25, 30.00, 30.75, 31.13]
    ci = sum(1 for s in steps if t >= s)
    if t < 31.50:
        jit = 5 * math.sin(80 * t) * (1 if any(s <= t < s + 0.1 for s in steps) else 0)
        leak = 0.25 + 0.18 * ci
        cv.draw('egg_glow', x + jit, y, w * 1.2, alpha=leak, add=True, tint=(0.75, 1.0, 0.7))
        cv.draw('egg', x + jit, y, w)
        if ci:
            cv.draw(f'egg_crack_{ci}', x + jit, y, w, glow=True)
    else:
        u = (t - 31.50) / 0.75
        # shells burst outward with blur, the hatchling is revealed behind them
        hw = w * 1.42 * (0.92 + 0.08 * ease('outBack', min(1, u * 2)))
        cv.draw('hatchling_glow', x, y + 10 * k, hw * 1.05, alpha=0.45, add=True)
        cv.draw('hatchling', x, y + 10 * k, hw)
        shells(cv, x, y, w, t)
    kk = light(t, 31.50, 3, 10, 0.6)
    if kk > 0:
        cv.draw('mote', x, y, w * 3.6, alpha=kk * 1.1, add=True, tint=(0.85, 1.0, 0.8))
    # glow-match: the hatchling glow flares over the last 4 frames
    fl = K(t, [(32.25 - 4 / FPS, 0.0), (32.25, 0.8)])
    if fl > 0:
        cv.draw('hatchling_glow', x, y + 10 * k, w * 1.6, alpha=fl, add=True)
    T_HATCH_HEAD[0] = (x, y + 120 * k * 0.0)


T_HATCH_HEAD = [(0.0, 0.0)]


@lru_cache(maxsize=4)
def shell_pieces(width_px):
    """Cut egg.png into 6 wedge shards (polygon masks) for the burst."""
    t = tex('egg', width_px).copy()
    h, w = t.shape[:2]
    pieces = []
    cx, cy = w / 2, h * 0.55
    for i in range(6):
        a0, a1 = i * math.pi / 3 + 0.2, (i + 1) * math.pi / 3 + 0.2
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
        name = f'__shell_{wpx}_{i}'
        _SHELL[name] = crop
        d = 1 + 5 * u + 4 * u * u
        px = x + ox * w * d
        py = y - oy * w * d * 1.1 - 250 * u * u
        v = ((ox * w * 9 + ox * w * 12 * u) * cv.S / FPS, (oy * w * 10) * cv.S / FPS)
        draw_raw(cv, crop, px, py, rot=200 * u * (1 if i % 2 else -1), alpha=max(0, 1 - u / 0.9),
                 blur=v)


_SHELL = {}


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


KAIJU_FORMS = [('kaiju_neutral', 1216, 1536), ('kaiju_crimson', 1280, 1448), ('kaiju_berserk', 1280, 1336)]


def cam_kaiju(t):
    y = K(t, [(32.25, -560), (34.40, 250, 'outCubic'), (40.50, 290)])
    z = K(t, [(32.25, 1.00), (34.50, 1.00), (36.00, 1.06, 'outCubic'), (40.50, 1.075)])
    return Cam(K(t, [(32.25, 10), (40.5, -20)]), y, z, shake_at(t))


def shot_kaiju(cv, t, cam, cn):
    # sky_storm flipped and lifted to a different region (not the crack framing again)
    roar = K(t, [(34.50, 0.0), (34.56, 1.0), (35.30, 0.0, 'outCubic')])
    if roar > 0.02:                                   # roar: radial zoom blur on the background only
        for k_, sc_ in enumerate((1.0, 1.025, 1.05, 1.075)):
            layer(cv, cam, None, 'sky_storm', 0, -60, 2400 * (1 + (sc_ - 1) * roar), 0.12, flipx=True, gain=0.85,
                  alpha=1.0 if k_ == 0 else 0.5 / k_)
    else:
        layer(cv, cam, cn, 'sky_storm', 0, -60, 2400, 0.12, flipx=True, gain=0.85)
    layer(cv, cam, cn, 'shafts', 0, 120, 2200, 0.25, alpha=0.55, add=True)
    # one Kaiju, three states: Azure (35.25), Crimson (36.00), berserk (37.50); same feet line, same height
    feet, height = -720, 1450
    a_cr = K(t, [(36.02, 0.0), (36.22, 1.0, 'inOutSine')])
    a_be = K(t, [(37.52, 0.0), (37.72, 1.0, 'inOutSine')])
    azure = K(t, [(35.25, 0.0), (35.55, 1.0, 'outCubic'), (36.02, 1.0), (36.22, 0.0)])
    surge = max(K(t, [(35.90, 0.0), (36.10, 1.0), (36.45, 0.0)]), K(t, [(37.40, 0.0), (37.60, 1.0), (37.95, 0.0)]))
    breathe = 1 + 0.012 * math.sin(2 * math.pi * 0.21 * t) + 0.03 * roar
    for name, tw, th in KAIJU_FORMS:
        a = {'kaiju_neutral': 1.0, 'kaiju_crimson': a_cr, 'kaiju_berserk': a_be}[name]
        if a <= 0:
            continue
        w = height * tw / th
        y = feet + height * breathe / 2
        gk = 0.55 + 0.2 * math.sin(2 * math.pi * 1.2 * t)
        tint = None
        if name == 'kaiju_neutral':
            tint = (1 - 0.75 * azure, 1 - 0.34 * azure, 1.0) if azure > 0 else None
            gk += 0.45 * azure
        if name == 'kaiju_berserk':
            for ft in (37.95, 38.25):
                if ft < t < ft + 0.08:
                    gk *= 0.3
            tint = (0.85, 0.6, 1.0)
        # glow-match from the hatchling: the Kaiju's glow is lit for the first 6 frames
        gk += K(t, [(32.25, 0.9), (32.25 + 6 / FPS, 0.0)])
        layer(cv, cam, cn, name + '_glow', 0, y, w, 0.7, alpha=gk * a, add=True, tint=tint)
        layer(cv, cam, cn, name, 0, y, w * breathe, 0.7, alpha=a)
        if name == 'kaiju_neutral' and azure > 0:
            layer(cv, cam, None, name + '_glow', 0, y, w * 1.03, 0.7, alpha=1.1 * azure * (1 - a_cr), add=True,
                  tint=(0.25, 0.66, 0.96))
    if surge > 0:                                    # the change happens under a surge of the new colour's glow
        name, tw, th = KAIJU_FORMS[1] if t < 37.0 else KAIJU_FORMS[2]
        w = height * tw / th
        layer(cv, cam, None, name + '_glow', 0, feet + height / 2, w * 1.02, 0.7, alpha=1.3 * surge, add=True)
    layer(cv, cam, cn, 'cloudsea_storm', -15 * t, -820, 2900, 0.9, alpha=0.92)
    if 34.8 < t < 35.9:
        dk = [(34.8, 650), (35.9, -900, 'inCubic')]
        layer(cv, cam, cn, 'debris', -520, K(t, dk), 1200, 0.95, next_xy=(-520, K(t + 1 / FPS, dk)), alpha=0.85)


def cam_hero(t):
    y = K(t, [(40.50, -40), (42.45, -20), (43.25, 60, 'inOutCubic'), (DUR, 70)])
    z = K(t, [(40.50, 1.00), (42.45, 1.03), (43.25, 1.08, 'inOutCubic'), (DUR, 1.09)])
    return Cam(0, y, z, (0.0, 0.0))


def shot_hero(cv, t, cam, cn):
    layer(cv, cam, cn, 'hero', 0, 0, 2300, 0.4)
    layer(cv, cam, cn, 'shafts_warm', 0, 60, 2200, 0.2, alpha=0.32 + 0.1 * min(1, max(0, (t - 42.5) / 1.0)),
          add=True)
    motes(cv, cam, t, 91, 20, (-1100, 1100, -480, 450), 16, 0.6, col=(1.0, 0.8, 0.5), alpha=0.35, drift=(-5, 8))


SHOT_FN = dict(calm=(cam_calm, shot_calm), crack_rise=(cam_crack_rise, shot_crack_rise), fall=(cam_fall, shot_fall),
               shrine=(cam_shrine, shot_shrine), peoples=(cam_peoples, shot_peoples), hill=(cam_hill, shot_hill),
               choice=(cam_choice, shot_choice), hatch=(cam_shrine, shot_hatch), kaiju=(cam_kaiju, shot_kaiju),
               hero=(cam_hero, shot_hero))


def render_shot(sid, t):
    cv = Canvas()
    camf, shotf = SHOT_FN[sid]
    cam = camf(t)
    cn = camf(t + 1 / FPS)
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
                                 radius=int(60 * S), fill=int(255 * 0.42 * a))
            plate = plate.filter(__import__('PIL.ImageFilter', fromlist=['x']).GaussianBlur(28 * S))
            shade = Image.new('RGBA', (W, H), (8, 14, 24, 0))
            shade.putalpha(plate)
            layer = Image.alpha_composite(layer, shade)
            dr = ImageDraw.Draw(layer)
        xx = x0
        for ch, cw in zip(ti['text'], widths):
            dr.text((xx + 2 * S, y + 3 * S), ch, font=f, fill=(6, 10, 18, int(150 * a)), anchor='ls')
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
# C19 exposure bridge: across each hard cut the outgoing shot eases its exposure toward the incoming one over its last
# 0.5 s and the incoming shot eases back to its own grade over 0.6 s, so the mean-luma step at the cut is 15 or less
# (measured on the encoded film by check_film.py). (gain on the last frame before the cut, gain on the first frame after)
CUT_GAIN = {3.75: (0.95, 1.05), 9.75: (1.05, 0.95), 19.50: (1.09, 0.91), 23.25: (0.96, 1.04),
            28.50: (0.82, 1.24), 32.25: (1.15, 0.86), 40.50: (1.11, 0.89)}


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
