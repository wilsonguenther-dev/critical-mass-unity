"""Kaiju painted face decals, v4 (2026-11-12): expression states as decal swaps.

Same approach and same atlas LAYOUT as v2/v3 (ART-DIRECTION 3.4, the Warrior's cm_art/face.py route): one square RGBA
atlas shared by every Kaiju mesh through the one CM_Toon material,
    upper half (v 0.5-1.0)  adult face        lower half (v 0.0-0.5)  hatchling face
front planar projection of the head in DESIGN units (u = (x - cx) / (2 hw) + 0.5, v_local = (z - cz) / (2 hh) + 0.5),
transparent outside the features, the corner texel block at (0, 0) transparent (every face that takes no decal maps
there). CM/Toon mixes the decal over vertex colour BEFORE the three bands.

v4 changes:
  * 512 x 512 instead of 256 x 256 (same UVs, sharper close-up; the layout did not move).
  * EXPRESSION STATES. One atlas file per state, all with the identical layout, so the game swaps the texture and
    nothing else (no mesh, UV or vertex-colour change):
        Kaiju_Face.png           neutral-goofy  DEFAULT: curious, a bit dopey, tongue out (adult) / happy grin (hatchling)
        Kaiju_Face_Happy.png     happy-fed      allied: closed smiling eyes, big open grin, blush
        Kaiju_Face_Angry.png     angry-roar     attack: heavy slanted brows, small pupils, upper teeth (the Jaw bone opens)
        Kaiju_Face_Berserk.png   berserk        corrupted: magenta glowing wild eyes, pin pupils, jagged grin
        Kaiju_Face_Hurt.png      hurt           squeezed > < eyes, worried brows, wobbly mouth, tear
        Kaiju_Face_Sleepy.png    sleepy         egg-fresh: half lids, tiny smile, drool bubble (hatchling: just hatched)
        Kaiju_Face_Blink.png     blink          the default with the lids shut (swap for 3 frames every few seconds)
The adult face is authored in HEAD-LOCAL design units (origin = the decal rect centre on the head front), the
hatchling in its absolute design units as before.
"""
import numpy as np

SIZE = 512
ADULT_RECT = (0.0, 3.17, 1.08, 0.54)      # cx, cz, hw, hh; build_kaiju.adult_params re-centres it on the v4 head
HATCH_RECT = (0.0, 1.70, 0.80, 0.40)      # hatch_params
ADULT_DROP = 0.07
ADULT_SCALE = 1.1                # the adult face is authored at 1.0 and painted this much larger
HALF = {"Adult": 0.5, "Hatchling": 0.0}
RECT = {"Adult": ADULT_RECT, "Hatchling": HATCH_RECT}
STATES = ["Neutral", "Happy", "Angry", "Berserk", "Hurt", "Sleepy", "Blink"]
FILES = {s: ("Kaiju_Face.png" if s == "Neutral" else "Kaiju_Face_%s.png" % s) for s in STATES}


def _rgba(h, a=1.0):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)] + [a], dtype=np.float32)


NAVY = _rgba("#18233B")
TAN = _rgba("#D3C8B0")          # muzzle and cheeks (PAL Belly, a touch lighter so the muzzle reads as the face's light)
TAN_DARK = _rgba("#A89D86")
TAN_LIGHT = _rgba("#E6DCC8")
LID = _rgba("#4A5874")          # eyelid skin: lighter than the head hide so a lid reads as a lid
LID_LIGHT = _rgba("#6B7A96")
BROW = _rgba("#27324A")         # heavy brows: dark slate
BROW_LIGHT = _rgba("#5D6C88")
EYE = _rgba("#DDF24A")          # bible lime-yellow eye
EYE_RIM = _rgba("#A7C52E")
WHITE = _rgba("#FFFFFF")
MOUTH = _rgba("#7A2E3A")
MOUTH_DARK = _rgba("#4E1B28")
TONGUE = _rgba("#F0808A")
TONGUE_DARK = _rgba("#C95A6A")
TEETH = _rgba("#FBF5EA")
TEETH_SHADE = _rgba("#D9CFBD")
BLUSH = _rgba("#F58C7E", 0.55)
TEAR = _rgba("#8FE3F5")
MAGENTA = _rgba("#F04FD0")      # berserk eye glow (PAL GLOW Berserk, lighter)
MAGENTA_HOT = _rgba("#FFC4F2")
MAGENTA_HALO = _rgba("#EE2CC0", 0.55)
NOSE_LIGHT = _rgba("#4A5874")


class Canvas:
    def __init__(self, img, row0, rect, local):
        cx, cz, hw, hh = rect
        self.img, self.row0 = img, row0
        n = SIZE
        ys, xs = np.mgrid[0:n // 2, 0:n].astype(np.float32) + 0.5
        self.X = (xs / n - 0.5) * 2 * hw + (0.0 if local else cx)
        self.Z = (ys / (n // 2) - 0.5) * 2 * hh + (0.0 if local else cz)
        self.px = 2 * hw / n

    def over(self, mask, rgba):
        sub = self.img[self.row0:self.row0 + SIZE // 2]
        a = np.clip(mask, 0, 1)[..., None] * rgba[3]
        sub[..., :3] = sub[..., :3] * (1 - a) + rgba[:3] * a
        sub[..., 3:] = sub[..., 3:] * (1 - a) + a

    def ell(self, x0, z0, rx, rz, rot=0.0, soft=None):
        soft = soft or 1.5 * self.px
        c, s = np.cos(rot), np.sin(rot)
        dx, dz = self.X - x0, self.Z - z0
        u, v = (dx * c + dz * s) / rx, (-dx * s + dz * c) / rz
        d = (np.sqrt(u * u + v * v) - 1.0) * min(rx, rz)
        return np.clip(0.5 - d / soft, 0, 1)

    def stroke(self, pts, w, soft=None):
        soft = soft or 1.5 * self.px
        if not isinstance(w, (list, tuple)):
            w = [w] * len(pts)
        m = np.zeros_like(self.X)
        for (x0, z0), (x1, z1), (w0, w1) in zip(pts[:-1], pts[1:], zip(w[:-1], w[1:])):
            ax, az = x1 - x0, z1 - z0
            t = np.clip(((self.X - x0) * ax + (self.Z - z0) * az) / (ax * ax + az * az + 1e-12), 0, 1)
            d = np.hypot(self.X - (x0 + t * ax), self.Z - (z0 + t * az)) - (w0 + (w1 - w0) * t) / 2
            m = np.maximum(m, np.clip(0.5 - d / soft, 0, 1))
        return m

    def below(self, x0, z0, x1, z1):
        """1 below the line through the two points (extended), 0 above, anti-aliased."""
        nx, nz = -(z1 - z0), (x1 - x0)
        L = np.hypot(nx, nz)
        d = ((self.X - x0) * nx + (self.Z - z0) * nz) / L
        if nz < 0:
            d = -d
        return np.clip(0.5 - d / (1.5 * self.px), 0, 1)

    def arc(self, x0, z0, hw, bow, w, n=9):
        """A stroke from (x0 - hw, z0) to (x0 + hw, z0) bowed by `bow` in z at the middle (bow > 0: a frown shape /
        closed happy eye; bow < 0: a smile)."""
        pts = [(x0 + hw * (2 * i / (n - 1) - 1), z0 + bow * (1 - (2 * i / (n - 1) - 1) ** 2)) for i in range(n)]
        ws = [w * (0.55 + 0.45 * (1 - abs(2 * i / (n - 1) - 1))) for i in range(n)]
        return self.stroke(pts, ws)


def eye(cv, ex, ez, r, s, style, k=1.0):
    """One big cartoon eye. s = +1 / -1 (which side of the face), r = radius, k = per-eye size factor (the goofy
    default has one eye a little bigger). Styles: open, cross (goofy: pupils drift in and one sits high), happy
    (closed smiling arc), angry (lid cut slanting down toward the nose, small pupil), berserk (magenta glow, pin
    pupil, ring), hurt (> < squeeze), sleepy (heavy lid over 60 %), blink."""
    r = r * k
    rx, rz = r, r * 1.08
    ink = 0.12 * r
    if style in ("happy", "blink"):
        bow = 0.5 * rz if style == "happy" else -0.22 * rz
        cv.over(cv.arc(ex, ez - 0.1 * rz, rx * 0.92, bow, 0.36 * r), NAVY)
        if style == "blink":
            cv.over(cv.ell(ex, ez + 0.2 * rz, rx, rz * 0.8) * (1 - cv.below(ex - rx, ez - 0.05 * rz, ex + rx, ez - 0.05 * rz)), LID)
            cv.over(cv.arc(ex, ez - 0.1 * rz, rx * 0.92, bow, 0.36 * r), NAVY)
            for i in (-1, 0, 1):                                          # lashes
                cv.over(cv.stroke([(ex + i * 0.5 * rx, ez - 0.2 * rz), (ex + i * 0.62 * rx, ez - 0.42 * rz)], 0.12 * r), NAVY)
        return
    if style == "hurt":
        tip = (ex - s * 0.55 * rx, ez)
        for dz in (0.7, -0.7):
            cv.over(cv.stroke([(ex + s * 0.75 * rx, ez + dz * rz), tip], [0.26 * r, 0.34 * r]), NAVY)
        return
    if style == "berserk":
        cv.over(cv.ell(ex, ez, rx * 1.75, rz * 1.7, soft=0.9 * r), MAGENTA_HALO)      # purple glow around the eye
    cv.over(cv.ell(ex, ez, rx + ink, rz + ink), NAVY)
    ball = cv.ell(ex, ez, rx, rz)
    if style == "berserk":
        cv.over(ball, MAGENTA)
        cv.over(cv.ell(ex, ez, rx * 0.72, rz * 0.72), MAGENTA_HOT)
        ring = cv.ell(ex, ez, rx * 0.5, rz * 0.5) * (1 - cv.ell(ex, ez, rx * 0.36, rz * 0.36))
        cv.over(ring, NAVY)
        pr = 0.14 if s > 0 else 0.22                                                   # mismatched pin pupils
        cv.over(cv.ell(ex + s * 0.05 * rx, ez + (0.08 if s > 0 else -0.06) * rz, rx * pr, rz * pr), NAVY)
        return
    cv.over(ball, EYE)
    cv.over(ball * (1 - cv.ell(ex, ez + 0.16 * rz, rx * 0.98, rz * 0.98)), EYE_RIM)   # painted lower shade
    if style == "cross":
        px_, pz_, pr = ex - s * 0.20 * rx, ez + (0.10 if s > 0 else -0.06) * rz, 0.56
    elif style == "angry":
        px_, pz_, pr = ex - s * 0.16 * rx, ez - 0.14 * rz, 0.36
    elif style == "sleepy":
        px_, pz_, pr = ex - s * 0.05 * rx, ez - 0.30 * rz, 0.5
    else:
        px_, pz_, pr = ex - s * 0.08 * rx, ez, 0.56
    cv.over(cv.ell(px_, pz_, rx * pr, rz * pr) * ball, NAVY)
    cv.over(cv.ell(px_ - 0.26 * rx * pr / 0.56, pz_ + 0.30 * rz * pr / 0.56, rx * 0.22, rz * 0.22) * ball, WHITE)
    cv.over(cv.ell(px_ + 0.24 * rx * pr / 0.56, pz_ - 0.30 * rz * pr / 0.56, rx * 0.10, rz * 0.10) * ball, WHITE)
    if style == "angry":       # lid slanting down toward the nose
        a, b = (ex - s * 1.2 * rx, ez + 0.05 * rz), (ex + s * 1.2 * rx, ez + 0.85 * rz)
        lid = cv.ell(ex, ez, rx + ink, rz + ink) * (1 - cv.below(*a, *b))
        cv.over(lid, LID)
        cv.over(cv.stroke([a, b], 0.2 * r) * cv.ell(ex, ez, rx + ink, rz + ink), NAVY)
    elif style == "sleepy":
        a, b = (ex - rx * 1.2, ez - 0.02 * rz), (ex + rx * 1.2, ez - 0.02 * rz)
        lid = cv.ell(ex, ez, rx + ink, rz + ink) * (1 - cv.below(*a, *b))
        cv.over(lid, LID)
        cv.over(lid * (1 - cv.below(ex - rx, ez + 0.55 * rz, ex + rx, ez + 0.55 * rz)), LID_LIGHT)
        cv.over(cv.stroke([a, b], 0.2 * r) * cv.ell(ex, ez, rx + ink, rz + ink), NAVY)
    else:                      # a sliver of upper lid so the eye looks lidded and movable
        a, b = (ex - rx * 1.2, ez + 0.80 * rz), (ex + rx * 1.2, ez + 0.80 * rz)
        lid = cv.ell(ex, ez, rx + ink, rz + ink) * (1 - cv.below(*a, *b))
        cv.over(lid, LID)


def brow(cv, ex, ez, r, s, inner_dz, outer_dz, lift, thick=0.42):
    """Heavy chunky brow: a fat dark-slate stroke with a lighter top edge. inner / outer = height offsets of the two
    ends (inner end is toward the nose), lift = height of the whole brow above the eye centre, all in eye radii."""
    pi = (ex - s * 0.95 * r, ez + (lift + inner_dz) * r)
    pm = (ex, ez + (lift + 0.5 * (inner_dz + outer_dz) + 0.12) * r)
    po = (ex + s * 1.05 * r, ez + (lift + outer_dz) * r)
    cv.over(cv.stroke([pi, pm, po], [thick * r * 1.1 + 0.03, thick * r * 1.25 + 0.03, thick * r * 0.8 + 0.03]), NAVY)
    cv.over(cv.stroke([pi, pm, po], [thick * r * 1.1, thick * r * 1.25, thick * r * 0.8]), BROW)
    up = 0.1 * r
    cv.over(cv.stroke([(pi[0], pi[1] + up), (pm[0], pm[1] + up * 1.3), (po[0], po[1] + up)], [0.1 * r, 0.14 * r, 0.08 * r]),
            BROW_LIGHT)


def tooth(cv, x, z, w, h, clip=None, up=False):
    """A chunky rounded tooth hanging down from z (or standing up from z when up=True)."""
    zc = z + (h / 2 if up else -h / 2)
    m = np.maximum(cv.ell(x, zc, w / 2, h / 2), cv.ell(x, z + (0.15 * h if up else -0.15 * h), w / 2, h * 0.3))
    ink = np.maximum(cv.ell(x, zc, w / 2 + 0.012, h / 2 + 0.012), cv.ell(x, z, w / 2 + 0.012, h * 0.3 + 0.012))
    if clip is not None:
        m, ink = m * clip, ink * clip
    cv.over(ink, NAVY)
    cv.over(m, TEETH)
    cv.over(m * cv.ell(x + 0.3 * w, zc, w * 0.3, h * 0.5), TEETH_SHADE)


def open_mouth(cv, x0, z0, hw, depth, teeth_top=0, teeth_bottom=0, tongue=True, lip=0.022, jag=False):
    """A wide open D mouth: flat-ish smiling top edge at z0 curling up at the corners, round bowl below."""
    top = cv.below(x0 - hw, z0, x0 + hw, z0)
    bowl = cv.ell(x0, z0, hw, depth) * top
    cv.over(np.clip(np.maximum(cv.ell(x0, z0, hw + lip, depth + lip) * cv.below(x0 - hw, z0 + lip, x0 + hw, z0 + lip), 0), 0, 1), NAVY)
    cv.over(bowl, MOUTH)
    cv.over(bowl * cv.ell(x0, z0 - 0.15 * depth, hw * 0.8, depth * 0.45), MOUTH_DARK)
    if tongue:
        tg = cv.ell(x0 + 0.08 * hw, z0 - 0.82 * depth, hw * 0.5, depth * 0.42) * bowl
        cv.over(tg, TONGUE)
        cv.over(cv.stroke([(x0 + 0.08 * hw, z0 - 0.55 * depth), (x0 + 0.08 * hw, z0 - 0.95 * depth)], 0.012) * tg, TONGUE_DARK)
    for i in range(teeth_top):
        u = (i - (teeth_top - 1) / 2) / max(1, teeth_top - 1) if teeth_top > 1 else 0.0
        w = 1.5 * hw / max(3, teeth_top + 1)
        tooth(cv, x0 + u * 1.45 * hw * (0.5 if teeth_top == 2 else 1), z0 + 0.004, w, w * (1.25 if jag else 1.0), clip=bowl)
    for i in range(teeth_bottom):
        u = (i - (teeth_bottom - 1) / 2) / max(1, teeth_bottom - 1) if teeth_bottom > 1 else 0.0
        w = 1.2 * hw / max(3, teeth_bottom + 1)
        zb = z0 - depth * np.sqrt(max(0.0, 1 - (u * 1.1) ** 2)) * 0.98
        tooth(cv, x0 + u * 1.1 * hw, float(zb), w, w * 1.0, clip=bowl, up=True)
    cv.over(cv.stroke([(x0 - hw - 0.03, z0 + 0.06), (x0 - hw + 0.02, z0 + 0.005), (x0, z0 - 0.004), (x0 + hw - 0.02, z0 + 0.005),
                       (x0 + hw + 0.03, z0 + 0.06)], [lip * 0.7, lip, lip * 1.1, lip, lip * 0.7]), NAVY)


def cheeks(cv, x, z, rx, rz, strength=1.0):
    for s in (1, -1):
        cv.over(cv.ell(x * s, z, rx, rz, soft=0.05) * strength, BLUSH)
        cv.over(cv.ell(x * s - 0.25 * rx, z + 0.3 * rz, rx * 0.22, rz * 0.2), _rgba("#FFFFFF", 0.55 * strength))


def paint_adult(cv, state):
    """Head-local design units: x right of the face centre, z up; the head front is about 1.4 wide and 0.8 tall here."""
    ER, EX, EZ = 0.175, 0.265, 0.115
    # tan muzzle: a big soft mask shape around nose and mouth with two cheek lobes (bible: tan muzzle and jaw)
    m = np.maximum.reduce([cv.ell(0.0, -0.20, 0.47, 0.19), cv.ell(0.0, -0.09, 0.30, 0.16),
                           cv.ell(0.33, -0.13, 0.20, 0.15), cv.ell(-0.33, -0.13, 0.20, 0.15)])
    cv.over(np.maximum.reduce([cv.ell(0.0, -0.20, 0.49, 0.21), cv.ell(0.0, -0.09, 0.32, 0.18),
                               cv.ell(0.33, -0.13, 0.22, 0.17), cv.ell(-0.33, -0.13, 0.22, 0.17)]), NAVY)
    cv.over(m, TAN)
    cv.over(m * cv.ell(-0.06, -0.06, 0.30, 0.10, soft=0.06), TAN_LIGHT)                # painted top light on the muzzle
    cv.over(m * (1 - cv.ell(0.0, -0.10, 0.60, 0.22, soft=0.08)), TAN_DARK)             # shade under the chin line
    styles = {"Neutral": "cross", "Happy": "happy", "Angry": "angry", "Berserk": "berserk", "Hurt": "hurt",
              "Sleepy": "sleepy", "Blink": "blink"}
    st = styles[state]
    for s in (1, -1):
        k = 1.10 if (s > 0 and state in ("Neutral", "Blink", "Berserk")) else 1.0      # goofy: one eye bigger
        eye(cv, EX * s, EZ, ER, s, st, k)
    # brows: (inner_dz, outer_dz, lift) per side, in eye radii
    B = {"Neutral": {1: (0.25, 0.10, 1.62), -1: (-0.05, 0.25, 1.30)},                 # one cocked high: curious, dopey
         "Blink": {1: (0.25, 0.10, 1.55), -1: (-0.05, 0.25, 1.25)},
         "Happy": {1: (0.15, -0.05, 1.55), -1: (0.15, -0.05, 1.55)},
         "Angry": {1: (-0.55, 0.45, 1.12), -1: (-0.55, 0.45, 1.12)},
         "Berserk": {1: (-0.75, 0.75, 1.30), -1: (-0.45, 0.95, 1.18)},
         "Hurt": {1: (0.55, -0.35, 1.35), -1: (0.55, -0.35, 1.35)},
         "Sleepy": {1: (0.05, -0.15, 1.12), -1: (0.05, -0.15, 1.12)}}[state]
    for s in (1, -1):
        brow(cv, EX * s, EZ, ER, s, *B[s])
    # nose: wide soft navy wedge with a highlight and two nostrils
    cv.over(cv.ell(0.0, -0.045, 0.10, 0.058), NAVY)
    cv.over(cv.ell(-0.03, -0.022, 0.045, 0.018), NOSE_LIGHT)
    if state in ("Neutral", "Blink"):
        # wide lopsided closed smile, one buck tooth, tongue lolling out of the creature's left corner
        sm = [(-0.34, -0.165), (-0.22, -0.225), (-0.05, -0.25), (0.12, -0.24), (0.26, -0.20), (0.36, -0.125)]
        tg = np.maximum(cv.ell(0.20, -0.275, 0.085, 0.075), cv.ell(0.20, -0.235, 0.075, 0.05))
        cv.over(np.maximum(cv.ell(0.20, -0.275, 0.10, 0.09), cv.ell(0.20, -0.235, 0.09, 0.062)), NAVY)
        cv.over(tg, TONGUE)
        cv.over(cv.stroke([(0.20, -0.24), (0.20, -0.31)], 0.012), TONGUE_DARK)
        cv.over(cv.ell(0.175, -0.25, 0.022, 0.016), _rgba("#FFFFFF", 0.6))
        tooth(cv, -0.10, -0.238, 0.085, 0.085)
        cv.over(cv.stroke(sm, [0.018, 0.028, 0.032, 0.032, 0.028, 0.018]), NAVY)
        cv.over(cv.stroke([(0.34, -0.10), (0.405, -0.135)], [0.02, 0.012]), NAVY)       # dimple
        cv.over(cv.stroke([(-0.33, -0.14), (-0.385, -0.17)], [0.02, 0.012]), NAVY)
        cheeks(cv, 0.50, -0.085, 0.095, 0.06)
    elif state == "Happy":
        open_mouth(cv, 0.0, -0.165, 0.30, 0.17, teeth_top=2, teeth_bottom=0)
        cheeks(cv, 0.50, -0.07, 0.11, 0.07, 1.25)
    elif state == "Angry":
        open_mouth(cv, 0.0, -0.20, 0.33, 0.15, teeth_top=5, teeth_bottom=0, tongue=True)
        cv.over(cv.stroke([(-0.08, 0.02), (0.0, 0.0), (0.08, 0.02)], 0.016), NAVY)       # snarl wrinkle over the nose
        cv.over(cv.stroke([(-0.06, 0.055), (0.0, 0.04), (0.06, 0.055)], 0.012), NAVY)
    elif state == "Berserk":
        # clenched jagged grin: a wide band of zigzag teeth
        band = cv.ell(0.0, -0.21, 0.36, 0.085)
        cv.over(cv.ell(0.0, -0.21, 0.385, 0.108), NAVY)
        cv.over(band, TEETH)
        zz = [(-0.36 + 0.06 * i, -0.21 + (0.045 if i % 2 else -0.045)) for i in range(13)]
        cv.over(cv.stroke(zz, 0.02) * band, NAVY)
        cv.over(cv.ell(0.0, -0.36, 0.2, 0.05, soft=0.06), MAGENTA_HALO)                # drool glow
        cv.over(cv.stroke([(0.38, -0.17), (0.45, -0.10)], [0.022, 0.012]), NAVY)
        cv.over(cv.stroke([(-0.38, -0.17), (-0.45, -0.10)], [0.022, 0.012]), NAVY)
    elif state == "Hurt":
        wob = [(-0.2, -0.235), (-0.12, -0.20), (-0.04, -0.235), (0.04, -0.20), (0.12, -0.235), (0.2, -0.20)]
        cv.over(cv.ell(0.0, -0.235, 0.15, 0.075), NAVY)
        cv.over(cv.ell(0.0, -0.235, 0.13, 0.056), MOUTH)
        tooth(cv, 0.05, -0.185, 0.07, 0.06, clip=cv.ell(0.0, -0.235, 0.13, 0.056))
        cv.over(cv.stroke(wob, 0.02), NAVY)
        cv.over(np.maximum(cv.ell(0.50, -0.02, 0.04, 0.05), cv.stroke([(0.50, 0.0), (0.485, 0.06)], [0.05, 0.012])), NAVY)
        cv.over(np.maximum(cv.ell(0.50, -0.02, 0.028, 0.038), cv.stroke([(0.50, 0.0), (0.487, 0.045)], [0.036, 0.008])), TEAR)
        cheeks(cv, 0.50, -0.11, 0.08, 0.05, 0.8)
    elif state == "Sleepy":
        cv.over(cv.arc(-0.03, -0.21, 0.16, -0.035, 0.03), NAVY)
        cv.over(cv.ell(0.19, -0.25, 0.062, 0.062), NAVY)                               # drool bubble
        cv.over(cv.ell(0.19, -0.25, 0.048, 0.048), TEAR)
        cv.over(cv.ell(0.175, -0.235, 0.014, 0.014), WHITE)
        cheeks(cv, 0.50, -0.085, 0.095, 0.06)


def paint_hatchling(cv, state):
    """Hatchling (absolute design units, eyes at x = +-0.24, z = 1.86; mouth on the muzzle front)."""
    m = np.maximum.reduce([cv.ell(0.0, 1.57, 0.40, 0.21), cv.ell(0.33, 1.66, 0.18, 0.12), cv.ell(-0.33, 1.66, 0.18, 0.12)])
    cv.over(m, TAN)
    styles = {"Neutral": "open", "Happy": "happy", "Angry": "angry", "Berserk": "berserk", "Hurt": "hurt",
              "Sleepy": "sleepy", "Blink": "blink"}
    for s in (1, -1):
        eye(cv, 0.24 * s, 1.835, 0.112, s, styles[state])
        cv.over(cv.ell(0.37 * s, 1.68, 0.075, 0.045, soft=0.03), BLUSH)
    B = {"Neutral": (0.1, 0.0, 1.42), "Blink": (0.1, 0.0, 1.38), "Happy": (0.1, 0.0, 1.45), "Angry": (-0.45, 0.35, 1.2),
         "Berserk": (-0.55, 0.5, 1.22), "Hurt": (0.45, -0.3, 1.3), "Sleepy": (0.0, -0.15, 1.15)}[state]
    for s in (1, -1):
        brow(cv, 0.24 * s, 1.835, 0.112, s, *B, thick=0.24)
    if state in ("Neutral", "Happy", "Blink"):
        open_mouth(cv, 0.0, 1.60, 0.30, 0.16, teeth_top=2, lip=0.02)
    elif state == "Angry":
        open_mouth(cv, 0.0, 1.58, 0.26, 0.13, teeth_top=4, lip=0.02)
    elif state == "Berserk":
        band = cv.ell(0.0, 1.56, 0.28, 0.07)
        cv.over(cv.ell(0.0, 1.56, 0.30, 0.09), NAVY)
        cv.over(band, TEETH)
        cv.over(cv.stroke([(-0.28 + 0.07 * i, 1.56 + (0.04 if i % 2 else -0.04)) for i in range(9)], 0.018) * band, NAVY)
    elif state == "Hurt":
        cv.over(cv.ell(0.0, 1.55, 0.12, 0.065), NAVY)
        cv.over(cv.ell(0.0, 1.55, 0.10, 0.048), MOUTH)
        cv.over(np.maximum(cv.ell(0.40, 1.74, 0.03, 0.04), cv.stroke([(0.40, 1.76), (0.39, 1.81)], [0.04, 0.01])), TEAR)
    elif state == "Sleepy":       # just hatched: tiny content smile and a drool bubble
        cv.over(cv.arc(-0.02, 1.585, 0.13, -0.035, 0.028), NAVY)
        cv.over(cv.ell(0.17, 1.545, 0.058, 0.058), NAVY)
        cv.over(cv.ell(0.17, 1.545, 0.045, 0.045), TEAR)
        cv.over(cv.ell(0.157, 1.558, 0.013, 0.013), WHITE)


def paint(state="Neutral"):
    img = np.zeros((SIZE, SIZE, 4), dtype=np.float32)
    cv = Canvas(img, SIZE // 2, ADULT_RECT, True)
    cv.X = cv.X / ADULT_SCALE
    cv.px = cv.px / ADULT_SCALE
    cv.Z = cv.Z / ADULT_SCALE + ADULT_DROP          # the whole face sits a little below the rect centre (brows need the room)
    paint_adult(cv, state)
    paint_hatchling(Canvas(img, 0, HATCH_RECT, False), state)
    c = 8
    img[:c, :c, 3] = 0.0
    img[:c, :c, :3] = 0.0
    for r0 in (0, SIZE // 2):       # margins: never paint the outer texels of a half
        img[r0:r0 + 4, :, 3] = 0.0
        img[r0 + SIZE // 2 - 4:r0 + SIZE // 2, :, 3] = 0.0
    img[:, :4, 3] = 0.0
    img[:, -4:, 3] = 0.0
    return img


def uv_for(stage, x, z):
    """Design-space point on the head front -> atlas UV, or None if outside the stage's rect."""
    cx, cz, hw, hh = RECT[stage]
    u = (x - cx) / (2 * hw) + 0.5
    vl = (z - cz) / (2 * hh) + 0.5
    if not (0.01 <= u <= 0.99 and 0.02 <= vl <= 0.98):
        return None
    return (u, HALF[stage] + 0.5 * vl)


CORNER_UV = (4.0 / SIZE, 4.0 / SIZE)


def make_image(bpy, name, path, state="Neutral"):
    px = paint(state)
    im = bpy.data.images.get(name) or bpy.data.images.new(name, SIZE, SIZE, alpha=True)
    im.colorspace_settings.name = "sRGB"
    im.pixels.foreach_set(px.ravel())
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()
    im.pack()
    return im


def write_states(bpy, art_dir):
    """Every non-default expression atlas next to the FBX (the default is written by make_image with the material)."""
    import os
    for st in STATES:
        if st == "Neutral":
            continue
        im = bpy.data.images.new("Kaiju_Face_" + st, SIZE, SIZE, alpha=True)
        im.colorspace_settings.name = "sRGB"
        im.pixels.foreach_set(paint(st).ravel())
        im.filepath_raw = os.path.join(art_dir, FILES[st])
        im.file_format = "PNG"
        im.save()
        im.pack()


if __name__ == "__main__":      # preview outside Blender: python3 kaiju_face.py <out.png> (a sheet of every state)
    import sys
    from PIL import Image
    rows = []
    for st in STATES:
        a = paint(st)[::-1]
        bg = np.array([0.21, 0.26, 0.35], dtype=np.float32)
        rgb = a[..., :3] * a[..., 3:] + bg * (1 - a[..., 3:])
        rows.append((rgb * 255).astype(np.uint8))
    top = np.concatenate([r[:SIZE // 2] for r in rows[:4]], axis=1)
    mid = np.concatenate([r[:SIZE // 2] for r in rows[4:]] + [np.zeros_like(rows[0][:SIZE // 2])], axis=1)
    bot = np.concatenate([r[SIZE // 2:] for r in rows[:4]], axis=1)
    bot2 = np.concatenate([r[SIZE // 2:] for r in rows[4:]] + [np.zeros_like(rows[0][:SIZE // 2])], axis=1)
    Image.fromarray(np.concatenate([top, mid, bot, bot2], axis=0)).save(sys.argv[1])
