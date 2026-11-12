"""Kaiju painted face decal (ART-DIRECTION 3.4: one 256 x 256 painted face per class), drawn procedurally
(deterministic numpy, no hand edits), the same route as the Warrior's cm_art/face.py.

One 256 x 256 RGBA atlas, Kaiju_Face.png, shared by every Kaiju mesh (they all carry the one CM_Toon material):
    rows 128-255 (v 0.5-1.0)  adult: smug face (bible p10/p11 + 11-details): heavy half-closed lids over big yellow
                              eyes, a long closed smirk curling up at one end with a dimple, tan muzzle and cheeks
    rows   0-127 (v 0.0-0.5)  hatchling: cute grin (p10): big round yellow eyes with two highlights, open D grin with
                              teeth and tongue, cheek blush, tan muzzle
Texture space = a front planar projection of the head in DESIGN units (build_kaiju.py): u = (x - cx) / (2 hw) + 0.5,
v_local = (z - cz) / (2 hh) + 0.5, v = half + 0.5 v_local. Everything outside the features is fully transparent, so the
vertex colour shows through; the 4 x 4 texel corner at (0, 0) stays transparent: every face that does not take the
decal is mapped there. The CM/Toon shader mixes the decal over the vertex colour BEFORE the three-band ramp, so the
painted face is lit like the rest of the head.
"""
import numpy as np

SIZE = 256
ADULT_RECT = (0.0, 3.17, 0.72, 0.36)      # cx, cz, hw, hh (design units of adult_params); hw = 2 hh (256 x 128 half)
HATCH_RECT = (0.0, 1.70, 0.80, 0.40)      # hatch_params
HALF = {"Adult": 0.5, "Hatchling": 0.0}
RECT = {"Adult": ADULT_RECT, "Hatchling": HATCH_RECT}


def _rgba(h, a=1.0):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)] + [a], dtype=np.float32)


NAVY = _rgba("#18233B")
TAN = _rgba("#C9BFAA")          # PAL Belly (muzzle and cheeks)
TAN_DARK = _rgba("#A89D86")     # muzzle crease under the smile
LID = _rgba("#2C3850")          # lid skin, a touch darker than PAL Skin #36425A
EYE = _rgba("#E4F046")          # PAL Eye
EYE_V3 = _rgba("#CFF240")       # v3: brighter lime-yellow, toward the bible's green eyes
WHITE = _rgba("#FFFFFF")
MOUTH = _rgba("#7A2E3A")        # PAL Mouth
TONGUE = _rgba("#E0707C")
TEETH = _rgba("#F7F0E5")        # PAL Teeth (Warm Cream)
BLUSH = _rgba("#F2867A", 0.5)


class Canvas:
    def __init__(self, img, row0, rect):
        cx, cz, hw, hh = rect
        self.img, self.row0 = img, row0
        n = SIZE
        ys, xs = np.mgrid[0:n // 2, 0:n].astype(np.float32) + 0.5
        self.X = (xs / n - 0.5) * 2 * hw + cx
        self.Z = (ys / (n // 2) - 0.5) * 2 * hh + cz
        self.px = 2 * hw / n             # design units per texel

    def over(self, mask, rgba):
        sub = self.img[self.row0:self.row0 + SIZE // 2]
        a = np.clip(mask, 0, 1)[..., None] * rgba[3]
        sub[..., :3] = sub[..., :3] * (1 - a) + rgba[:3] * a
        sub[..., 3:] = sub[..., 3:] * (1 - a) + a

    def ell(self, x0, z0, rx, rz, rot=0.0, soft=None):
        soft = soft or 1.2 * self.px
        c, s = np.cos(rot), np.sin(rot)
        dx, dz = self.X - x0, self.Z - z0
        u, v = (dx * c + dz * s) / rx, (-dx * s + dz * c) / rz
        d = (np.sqrt(u * u + v * v) - 1.0) * min(rx, rz)
        return np.clip(0.5 - d / soft, 0, 1)

    def stroke(self, pts, w, soft=None):
        soft = soft or 1.2 * self.px
        m = np.zeros_like(self.X)
        for (x0, z0), (x1, z1), (w0, w1) in zip(pts[:-1], pts[1:], zip(w[:-1], w[1:])):
            ax, az = x1 - x0, z1 - z0
            t = np.clip(((self.X - x0) * ax + (self.Z - z0) * az) / (ax * ax + az * az), 0, 1)
            d = np.hypot(self.X - (x0 + t * ax), self.Z - (z0 + t * az)) - (w0 + (w1 - w0) * t) / 2
            m = np.maximum(m, np.clip(0.5 - d / soft, 0, 1))
        return m

    def below_line(self, x0, z0, x1, z1):
        """1 below the line through (x0, z0)-(x1, z1) (extended), 0 above, anti-aliased."""
        nx, nz = -(z1 - z0), (x1 - x0)
        L = np.hypot(nx, nz)
        d = ((self.X - x0) * nx + (self.Z - z0) * nz) / L
        if nz < 0:
            d = -d
        return np.clip(0.5 - d / (1.2 * self.px), 0, 1)


def paint_adult(cv):
    """v3 cheeky adult (Jeisil 2026-11-12: cartoony and expressive like the bible, not a stern wolf). Big bright
    lime-yellow eyes with two highlights, only a light lid on the snout side, one brow cocked high, and a wide
    confident smirk that curls up hard into the left cheek with a fang and a dimple. Eyes at x = +-0.22, z = 3.34."""
    m = np.maximum.reduce([cv.ell(0.0, 3.0, 0.40, 0.16), cv.ell(0.30, 3.07, 0.19, 0.12),
                           cv.ell(-0.30, 3.06, 0.19, 0.12), cv.ell(0.0, 2.90, 0.44, 0.12)])
    cv.over(m, TAN)
    for s in (1, -1):
        ex, ez = 0.22 * s, 3.345
        cv.over(cv.ell(ex, ez, 0.142, 0.138), NAVY)                               # thick ink rim
        cv.over(cv.ell(ex, ez, 0.122, 0.118), EYE_V3)                             # big bright eye
        cv.over(cv.ell(ex - 0.018 * s, ez - 0.006, 0.062, 0.074), NAVY)           # big pupil, looking in
        cv.over(cv.ell(ex - 0.044 * s, ez + 0.032, 0.026, 0.028), WHITE)          # main highlight
        cv.over(cv.ell(ex + 0.010 * s, ez - 0.040, 0.012, 0.012), WHITE)          # small second highlight
        # light cocky lid on the snout side only (the raised-brow eye stays wide open)
        drop = 0.06 if s < 0 else 0.0
        inner, outer = (ex - 0.14 * s, ez + 0.07 - drop), (ex + 0.14 * s, ez + 0.13 - drop * 0.4)
        lid = cv.ell(ex, ez, 0.142, 0.138) * (1 - cv.below_line(*inner, *outer))
        cv.over(lid, LID)
        cv.over(cv.stroke([inner, (ex, 0.5 * (inner[1] + outer[1]) + 0.012), outer], [0.020, 0.026, 0.018]), NAVY)
        # brows: the creature's left one arched high (raised, cheeky), the right one low and flat
        if s > 0:
            pts = [(ex - 0.12, ez + 0.16), (ex - 0.01, ez + 0.235), (ex + 0.13, ez + 0.20)]
        else:
            pts = [(ex + 0.12, ez + 0.15), (ex, ez + 0.165), (ex - 0.13, ez + 0.175)]
        cv.over(cv.stroke(pts, [0.032, 0.040, 0.026]), NAVY)
    # wide confident smirk: starts low on the right, sweeps across and curls up high into the left cheek
    smile = [(-0.30, 3.015), (-0.17, 2.95), (0.0, 2.925), (0.15, 2.94), (0.27, 2.995), (0.34, 3.08), (0.35, 3.13)]
    cv.over(cv.stroke(smile, [0.016, 0.024, 0.028, 0.028, 0.026, 0.022, 0.014]), NAVY)
    cv.over(cv.stroke([(0.325, 3.15), (0.39, 3.11)], [0.016, 0.010]), NAVY)        # dimple tick
    cv.over(cv.stroke([(-0.12, 2.895), (0.05, 2.885), (0.18, 2.90)], [0.010, 0.012, 0.008]), TAN_DARK)
    for fx in (0.08, 0.20):                                                         # two fangs on the grinning side
        cv.over(cv.ell(fx, 2.925 - 0.01 * (fx > 0.1), 0.02, 0.03), TEETH)
    cv.over(cv.stroke(smile[1:5], [0.018, 0.022, 0.022, 0.018]), NAVY)


def paint_hatchling(cv):
    """Cute hatchling grin (bible p10). Eyes at x = +-0.24, z = 1.86; mouth on the muzzle front."""
    m = np.maximum.reduce([cv.ell(0.0, 1.57, 0.40, 0.21), cv.ell(0.33, 1.66, 0.18, 0.12),
                           cv.ell(-0.33, 1.66, 0.18, 0.12)])
    cv.over(m, TAN)
    for s in (1, -1):
        cv.over(cv.ell(0.37 * s, 1.68, 0.075, 0.045, soft=0.03), BLUSH)
        ex, ez = 0.24 * s, 1.86
        cv.over(cv.ell(ex, ez, 0.145, 0.150), NAVY)
        cv.over(cv.ell(ex, ez, 0.126, 0.131), EYE)
        cv.over(cv.ell(ex - 0.012 * s, ez - 0.010, 0.088, 0.098), NAVY)            # big round pupil
        cv.over(cv.ell(ex - 0.040 * s, ez + 0.040, 0.034, 0.036), WHITE)           # two highlights
        cv.over(cv.ell(ex + 0.030 * s, ez - 0.045, 0.016, 0.016), WHITE)
        cv.over(cv.stroke([(ex - 0.10 * s, ez + 0.20), (ex + 0.02 * s, ez + 0.225), (ex + 0.11 * s, ez + 0.205)],
                          [0.022, 0.028, 0.020]), NAVY)                            # small raised brows
    # open D grin: wide smiling top edge, round bottom
    top = cv.below_line(-0.3, 1.60, 0.3, 1.60)
    bowl = cv.ell(0.0, 1.60, 0.31, 0.17) * top
    up = np.maximum(cv.ell(-0.30, 1.64, 0.06, 0.055), cv.ell(0.30, 1.64, 0.06, 0.055))
    mouth = np.maximum(bowl, up * cv.below_line(-0.3, 1.66, 0.3, 1.66))
    cv.over(np.clip(mouth * 1.0, 0, 1), NAVY)
    inner = cv.ell(0.0, 1.605, 0.285, 0.145) * cv.below_line(-0.3, 1.592, 0.3, 1.592)
    cv.over(inner, MOUTH)
    cv.over(cv.ell(0.0, 1.47, 0.12, 0.06) * inner, TONGUE)
    for x in (-0.09, 0.09):
        cv.over(cv.ell(x, 1.585, 0.035, 0.024) * inner, TEETH)                     # two top teeth
    cv.over(cv.stroke([(-0.34, 1.67), (-0.24, 1.605), (0.0, 1.597), (0.24, 1.605), (0.34, 1.67)],
                      [0.014, 0.020, 0.022, 0.020, 0.014]), NAVY)                  # smiling top lip line


def paint():
    img = np.zeros((SIZE, SIZE, 4), dtype=np.float32)
    paint_adult(Canvas(img, SIZE // 2, ADULT_RECT))
    paint_hatchling(Canvas(img, 0, HATCH_RECT))
    img[:4, :4, 3] = 0.0
    img[:4, :4, :3] = 0.0
    # margins: never paint the outer 2 texels of a half (faces that straddle the rect take the transparent corner)
    for r0 in (0, SIZE // 2):
        img[r0:r0 + 2, :, 3] = 0.0
        img[r0 + SIZE // 2 - 2:r0 + SIZE // 2, :, 3] = 0.0
    img[:, :2, 3] = 0.0
    img[:, -2:, 3] = 0.0
    return img


def uv_for(stage, x, z):
    """Design-space point on the head front -> atlas UV, or None if outside the stage's rect."""
    cx, cz, hw, hh = RECT[stage]
    u = (x - cx) / (2 * hw) + 0.5
    vl = (z - cz) / (2 * hh) + 0.5
    if not (0.01 <= u <= 0.99 and 0.02 <= vl <= 0.98):
        return None
    return (u, HALF[stage] + 0.5 * vl)


CORNER_UV = (2.0 / SIZE, 2.0 / SIZE)


def make_image(bpy, name, path):
    px = paint()
    im = bpy.data.images.get(name) or bpy.data.images.new(name, SIZE, SIZE, alpha=True)
    im.colorspace_settings.name = "sRGB"
    im.pixels.foreach_set(px.ravel())
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()
    im.pack()
    return im
