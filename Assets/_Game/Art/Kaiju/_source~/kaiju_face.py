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
    """Smug adult (bible 11-details head crop). Eyes at x = +-0.21, z = 3.34 on the head front; muzzle below."""
    # tan muzzle and cheeks with a wavy top edge under the eyes
    m = np.maximum.reduce([cv.ell(0.0, 3.02, 0.40, 0.17), cv.ell(0.30, 3.10, 0.19, 0.12),
                           cv.ell(-0.30, 3.09, 0.19, 0.12), cv.ell(0.0, 2.92, 0.44, 0.12)])
    cv.over(m, TAN)
    for s in (1, -1):
        ex, ez = 0.21 * s, 3.335
        cv.over(cv.ell(ex, ez, 0.112, 0.098), NAVY)                               # thick dark rim
        cv.over(cv.ell(ex, ez, 0.094, 0.080), EYE)                                # big yellow eye
        cv.over(cv.ell(ex - 0.022 * s, ez - 0.012, 0.042, 0.054), NAVY)           # pupil, looking in and down
        cv.over(cv.ell(ex - 0.040 * s, ez + 0.006, 0.016, 0.018), WHITE)          # highlight
        # heavy half-closed upper lid: slants down toward the snout (smug, self-satisfied)
        inner, outer = (ex - 0.11 * s, ez + 0.005), (ex + 0.11 * s, ez + 0.040)
        lid = cv.ell(ex, ez, 0.112, 0.098) * (1 - cv.below_line(*inner, *outer))
        cv.over(lid, LID)
        cv.over(cv.stroke([(ex - 0.115 * s, ez + 0.000), (ex, ez + 0.024), (ex + 0.118 * s, ez + 0.044)],
                          [0.020, 0.026, 0.018]), NAVY)                           # lid line
        # brow: one cocked higher (creature's left, +x), the other flat and low
        hi = 0.03 if s > 0 else 0.0
        cv.over(cv.stroke([(ex - 0.11 * s, ez + 0.11), (ex, ez + 0.13 + hi), (ex + 0.12 * s, ez + 0.12 + 1.5 * hi)],
                          [0.030, 0.036, 0.024]), NAVY)
    # long closed smirk: low on the creature's right, curling up into the left cheek, with a dimple
    smile = [(-0.31, 3.035), (-0.18, 2.975), (0.0, 2.955), (0.16, 2.970), (0.28, 3.020), (0.345, 3.085)]
    cv.over(cv.stroke(smile, [0.016, 0.022, 0.024, 0.024, 0.022, 0.016]), NAVY)
    cv.over(cv.stroke([(0.335, 3.10), (0.375, 3.07)], [0.014, 0.010]), NAVY)       # dimple tick
    cv.over(cv.stroke([(-0.10, 2.925), (0.05, 2.918), (0.16, 2.932)], [0.010, 0.012, 0.008]), TAN_DARK)  # lower lip
    cv.over(cv.ell(0.06, 2.948, 0.018, 0.026), TEETH)                              # one fang peeking under the lip
    cv.over(cv.stroke(smile[1:4], [0.016, 0.018, 0.016]), NAVY)


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
    bowl = cv.ell(0.0, 1.60, 0.24, 0.15) * top
    up = np.maximum(cv.ell(-0.235, 1.625, 0.05, 0.045), cv.ell(0.235, 1.625, 0.05, 0.045))
    mouth = np.maximum(bowl, up * cv.below_line(-0.3, 1.66, 0.3, 1.66))
    cv.over(np.clip(mouth * 1.0, 0, 1), NAVY)
    inner = cv.ell(0.0, 1.605, 0.215, 0.125) * cv.below_line(-0.3, 1.592, 0.3, 1.592)
    cv.over(inner, MOUTH)
    cv.over(cv.ell(0.0, 1.47, 0.12, 0.06) * inner, TONGUE)
    for x in (-0.09, 0.09):
        cv.over(cv.ell(x, 1.585, 0.035, 0.024) * inner, TEETH)                     # two top teeth
    cv.over(cv.stroke([(-0.27, 1.62), (-0.20, 1.60), (0.0, 1.597), (0.20, 1.60), (0.27, 1.62)],
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
