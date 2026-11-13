"""Hatch (Critical Mass tutorial mascot): painted face decals, one per expression (plain Python: numpy + Pillow).

Same route as the Kaiju's kaiju_face.py: the face is a decal projected from the front onto the head, mixed over the
vertex colour BEFORE the three toon bands, so it is lit like the head. Here every expression gets its own 1024 x 1024
decal (faces/face-<stem>.png); the FBX carries the neutral one as Mascot_Face.png.

Decal space = a front planar projection of the head in HEAD units (head sphere radius 1, x right = the mascot's left,
z up): pixel (0, 0) is (x, z) = (-EXT, +EXT), pixel (N, N) is (+EXT, -EXT). build_mascot.py uses the same EXT.
Everything outside the features is fully transparent, so the vertex colour (slate skin) shows through.

Usage: python3 paint_faces.py <out_dir> [--sheet sheet.png]
"""
import math
import os
import sys

from PIL import Image, ImageChops, ImageDraw

N = 1024            # output size
SS = 2              # supersampling
EXT = 1.05          # half-extent of the decal in head units
S = N * SS

# bible palette (kaiju_palette.py / kaiju_face.py on art/kaiju-blender) + CMTokens
NAVY = (0x18, 0x23, 0x3B)      # Void Navy, every ink line
TAN = (0xC9, 0xBF, 0xAA)       # PAL Belly: the face plate (muzzle, cheeks, eye lobes)
TAN_DARK = (0xA8, 0x9D, 0x86)  # lids and creases on the face plate
EYE = (0xCF, 0xF2, 0x40)       # v3 lime-yellow eyes
WHITE = (0xFF, 0xFF, 0xFF)
CREAM = (0xF7, 0xF0, 0xE5)     # Warm Cream: teeth, fangs
MOUTH = (0x7A, 0x2E, 0x3A)
TONGUE = (0xE0, 0x70, 0x7C)
BLUSH = (0xF2, 0x86, 0x7A)
SEAM = (0x4F, 0xDD, 0xC3)      # CMTokens.EggSeam: tears

EYE_X, EYE_Z = 0.37, 0.10
INK = 0.05                      # ink line width in head units (about 1.6 px at the 128 px medallion)


def P(x, z):
    return ((x + EXT) / (2 * EXT) * S, (EXT - z) / (2 * EXT) * S)


def U(d):
    return d / (2 * EXT) * S


class Face:
    def __init__(self):
        self.img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    # --- masks
    @staticmethod
    def blank():
        return Image.new("L", (S, S), 0)

    def ell(self, x, z, rx, rz, rot=0.0, m=None):
        """Ellipse (rot in degrees, polygon so rotation works)."""
        m = m or self.blank()
        d = ImageDraw.Draw(m)
        c, s = math.cos(math.radians(rot)), math.sin(math.radians(rot))
        pts = []
        for i in range(96):
            t = 2 * math.pi * i / 96
            ex, ez = rx * math.cos(t), rz * math.sin(t)
            pts.append(P(x + ex * c - ez * s, z + ex * s + ez * c))
        d.polygon(pts, fill=255)
        return m

    def poly(self, pts, m=None):
        m = m or self.blank()
        ImageDraw.Draw(m).polygon([P(*p) for p in pts], fill=255)
        return m

    def stroke(self, pts, w, m=None):
        m = m or self.blank()
        d = ImageDraw.Draw(m)
        px = [P(*p) for p in pts]
        d.line(px, fill=255, width=max(1, int(round(U(w)))), joint="curve")
        r = U(w) / 2
        for q in (px[0], px[-1]):
            d.ellipse([q[0] - r, q[1] - r, q[0] + r, q[1] + r], fill=255)
        return m

    def taper(self, pts, w0, w1, m=None):
        """A stroke whose width goes from w0 to w1 (brows, lid lines): a chain of discs and quads."""
        m = m or self.blank()
        d = ImageDraw.Draw(m)
        n = len(pts)
        for i in range(n - 1):
            a, b = pts[i], pts[i + 1]
            wa = w0 + (w1 - w0) * i / (n - 1)
            wb = w0 + (w1 - w0) * (i + 1) / (n - 1)
            dx, dz = b[0] - a[0], b[1] - a[1]
            L = math.hypot(dx, dz) or 1e-6
            nx, nz = -dz / L, dx / L
            d.polygon([P(a[0] + nx * wa / 2, a[1] + nz * wa / 2), P(b[0] + nx * wb / 2, b[1] + nz * wb / 2),
                       P(b[0] - nx * wb / 2, b[1] - nz * wb / 2), P(a[0] - nx * wa / 2, a[1] - nz * wa / 2)], fill=255)
            for q, w in ((a, wa), (b, wb)):
                c = P(*q)
                r = U(w) / 2
                d.ellipse([c[0] - r, c[1] - r, c[0] + r, c[1] + r], fill=255)
        return m

    @staticmethod
    def AND(a, b):
        return ImageChops.multiply(a, b)

    @staticmethod
    def OR(a, b):
        return ImageChops.lighter(a, b)

    @staticmethod
    def SUB(a, b):
        return ImageChops.subtract(a, b)

    def fill(self, m, rgb, a=1.0):
        layer = Image.new("RGBA", (S, S), rgb + (0,))
        layer.putalpha(m if a >= 1.0 else m.point(lambda v: int(v * a)))
        self.img = Image.alpha_composite(self.img, layer)

    def half(self, x0, z0, x1, z1, above=True):
        """Mask of the half plane above (or below) the line through (x0,z0)-(x1,z1), extended far."""
        dx, dz = x1 - x0, z1 - z0
        L = math.hypot(dx, dz)
        ux, uz = dx / L * 9, dz / L * 9
        nz = 9 if above else -9
        return self.poly([(x0 - ux, z0 - uz), (x1 + ux, z1 + uz), (x1 + ux, z1 + uz + nz), (x0 - ux, z0 - uz + nz)])

    def out(self):
        return self.img.resize((N, N), Image.LANCZOS)


def arc(cx, cz, rx, rz, a0, a1, n=24):
    return [(cx + rx * math.cos(math.radians(a0 + (a1 - a0) * i / (n - 1))),
             cz + rz * math.sin(math.radians(a0 + (a1 - a0) * i / (n - 1)))) for i in range(n)]


def bez(p0, p1, p2, n=24):
    return [((1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t * t * p2[0],
             (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t * t * p2[1]) for t in (i / (n - 1) for i in range(n))]


# ------------------------------------------------------------------------------------------------ parts
def face_plate(f):
    """Tan heart-shaped face plate: two eye lobes merging into the muzzle (reads at 64 px on the slate head)."""
    m = f.ell(-EYE_X, EYE_Z + 0.02, 0.40, 0.39)
    m = f.OR(m, f.ell(EYE_X, EYE_Z + 0.02, 0.40, 0.39))
    m = f.OR(m, f.ell(0.0, -0.36, 0.66, 0.40))
    m = f.OR(m, f.poly([(-0.55, -0.15), (0.55, -0.15), (0.40, 0.10), (-0.40, 0.10)]))
    f.fill(m, TAN)


def eye_open(f, side, look=(0.0, 0.0), r=0.24, rz=0.265, pupil=0.13, lid=0.0, lid_tilt=0.0, low_lid=0.0,
             squint_line=False):
    """Big round eye: navy ring, lime-yellow iris, navy pupil, two white highlights.
    lid: 0..1 how far the upper lid comes down (tan-dark lid with a navy lid line), lid_tilt in head units (outer end
    lower when positive); low_lid: 0..1 how far a lower lid comes up."""
    x0 = side * EYE_X
    z0 = EYE_Z
    ring = f.ell(x0, z0, r + INK, rz + INK)
    inner = f.ell(x0, z0, r, rz)
    f.fill(ring, NAVY)
    f.fill(inner, EYE)
    px, pz = x0 + look[0] * (r - pupil) * 0.95, z0 + look[1] * (rz - pupil) * 0.95
    f.fill(f.AND(f.ell(px, pz, pupil, pupil * 1.12), inner), NAVY)
    hl = f.ell(px - pupil * 0.42, pz + pupil * 0.48, pupil * 0.42 + 0.012, pupil * 0.46 + 0.012)
    f.fill(f.AND(hl, inner), WHITE)
    f.fill(f.AND(f.ell(px + pupil * 0.50, pz - pupil * 0.45, pupil * 0.2 + 0.008, pupil * 0.2 + 0.008), inner), WHITE)
    if lid > 0:
        zl = z0 + rz - lid * 2 * rz
        a = (x0 - side * (r + 0.1), zl + lid_tilt * -1)
        b = (x0 + side * (r + 0.1), zl + lid_tilt)
        a, b = (a, b) if a[0] < b[0] else (b, a)
        cover = f.AND(f.half(a[0], a[1], b[0], b[1], True), inner)
        f.fill(cover, TAN_DARK)
        # lid line: the half-plane edge inside the eye, thick
        line = f.AND(f.SUB(f.half(a[0], a[1] - INK * 0.9, b[0], b[1] - INK * 0.9, True),
                           f.half(a[0], a[1] + INK * 0.2, b[0], b[1] + INK * 0.2, True)), ring)
        f.fill(line, NAVY)
    if low_lid > 0:
        zl = z0 - rz + low_lid * 2 * rz
        cover = f.AND(f.half(x0 - 1, zl - 0.04, x0 + 1, zl - 0.04, False), inner)
        cover = f.AND(f.SUB(inner, f.ell(x0, zl + rz * 0.9, r * 1.4, rz * 1.15)), f.half(x0 - 1, zl, x0 + 1, zl, False))
        f.fill(cover, TAN_DARK)
        f.fill(f.AND(f.SUB(f.ell(x0, zl + rz * 0.9, r * 1.4 + INK, rz * 1.15 + INK),
                           f.ell(x0, zl + rz * 0.9, r * 1.4, rz * 1.15)), inner), NAVY)


def eye_arc(f, side, up=True, w=None, r=0.21, dz=0.0):
    """Closed eye as a thick arc: up=True is the happy '^' bow, False the content 'u' bowl."""
    w = w or INK * 2.0
    x0, z0 = side * EYE_X, EYE_Z + dz
    if up:
        pts = arc(x0, z0 - 0.06, r, r * 0.95, 160, 20)
    else:
        pts = arc(x0, z0 + 0.10, r, r * 0.80, 200, 340)
    f.fill(f.taper(pts, w * 0.8, w * 0.8), NAVY)


def eye_chevron(f, side, w=None, size=0.20):
    """Shut-tight eye: '>' on the mascot's right (screen left), '<' on its left."""
    w = w or INK * 2.1
    x0, z0 = side * EYE_X, EYE_Z
    tip = (x0 + side * -size * 0.55, z0)
    pts = [(x0 + side * size * 0.6, z0 + size * 0.75), tip, (x0 + side * size * 0.6, z0 - size * 0.65)]
    f.fill(f.taper(pts, w, w), NAVY)


def brow(f, side, z_in, z_out, w=None, curve=0.0, x_in=0.17, x_out=0.58):
    """Brow stroke over an eye. side +1 = the mascot's left (screen right). z_in at the inner end."""
    w = w or INK * 1.9
    a = (side * x_in, z_in)
    b = (side * x_out, z_out)
    m = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2 + curve)
    f.fill(f.taper(bez(a, m, b), w * 1.15, w * 0.7), NAVY)


def nose(f):
    for s in (-1, 1):
        f.fill(f.ell(s * 0.075, -0.19, 0.032, 0.026, rot=s * 20), NAVY)


def blush(f, a=0.55, dz=0.0):
    for s in (-1, 1):
        f.fill(f.ell(s * 0.60, -0.20 + dz, 0.14, 0.075), BLUSH, a)


def fang(f, x, z, h=0.10, w=0.07, down=True):
    sgn = -1 if down else 1
    m = f.poly([(x - w / 2, z), (x + w / 2, z), (x + w * 0.1, z + sgn * h)])
    f.fill(m, CREAM)
    edge = f.SUB(f.poly([(x - w / 2 - 0.022, z + 0.01), (x + w / 2 + 0.022, z + 0.01),
                         (x + w * 0.1, z + sgn * (h + 0.035))]), m)
    f.fill(f.AND(edge, f.half(-9, z, 9, z, not down)), NAVY)


def open_mouth(f, x0, z0, w, h, top_curve=0.04, tongue=True, fangs=True, skew=0.0):
    """D-shaped open mouth: gently curved top lip, round bottom. Navy rim, mouth red, tongue, two fangs."""
    top = bez((x0 - w / 2, z0 + skew), (x0, z0 - top_curve), (x0 + w / 2, z0 - skew))
    bot = arc(x0, z0 - 0.02, w / 2, h, 0, -180, 32)
    shape = top + bot
    m = f.poly(shape)
    rim = f.SUB(f.stroke(shape + [shape[0]], INK * 1.6), m)
    f.fill(m, MOUTH)
    if tongue:
        f.fill(f.AND(f.ell(x0 + w * 0.06, z0 - h * 0.92, w * 0.30, h * 0.5), m), TONGUE)
    if fangs:
        for s in (-1, 1):
            fx = x0 + s * w * 0.30
            fz = z0 - top_curve * 0.55 + (-s * skew)
            fm = f.AND(f.poly([(fx - 0.045, fz + 0.03), (fx + 0.045, fz + 0.03), (fx + s * 0.008, fz - 0.10)]), m)
            f.fill(fm, CREAM)
    f.fill(rim, NAVY)


def smirk(f, x_left=-0.24, x_right=0.28, z=-0.44, curl=0.10, fang_x=0.10, dimple=True):
    """Closed-mouth smirk curling up at the mascot's left (screen right) end, small fang over the lip."""
    pts = bez((x_left, z + 0.02), (0.02, z - 0.07), (x_right, z + curl))
    if fang_x is not None:
        fang(f, fang_x, z - 0.035, h=0.085, w=0.062)
    f.fill(f.taper(pts, INK * 1.0, INK * 1.45), NAVY)
    if dimple:
        e = pts[-1]
        f.fill(f.taper([(e[0] - 0.02, e[1] + 0.06), (e[0] + 0.035, e[1] - 0.01), (e[0] + 0.0, e[1] - 0.07)],
                       INK * 0.9, INK * 0.6), NAVY)


def o_mouth(f, x, z, rx=0.085, rz=0.105):
    f.fill(f.ell(x, z, rx + INK * 0.85, rz + INK * 0.85), NAVY)
    m = f.ell(x, z, rx, rz)
    f.fill(m, MOUTH)
    f.fill(f.AND(f.ell(x, z - rz * 0.7, rx * 0.7, rz * 0.45), m), TONGUE)


def gritted(f, x0=0.0, z0=-0.44, w=0.50, h=0.17):
    """Gritted teeth: a wide rounded bar of cream teeth, navy rim, a bite line and tooth gaps."""
    def bar(pad):
        r = h / 2 + pad
        xa, xb = x0 - w / 2 + h / 2, x0 + w / 2 - h / 2
        m = f.poly([(xa, z0 + r), (xb, z0 + r), (xb, z0 - r), (xa, z0 - r)])
        return f.OR(m, f.OR(f.ell(xa, z0, r, r), f.ell(xb, z0, r, r)))
    m = bar(0.0)
    f.fill(bar(INK * 0.85), NAVY)
    f.fill(m, CREAM)
    f.fill(f.AND(f.stroke([(x0 - w / 2, z0 + 0.005), (x0 + w / 2, z0 - 0.005)], INK * 0.8), m), NAVY)
    for i in range(1, 5):
        x = x0 - w / 2 + w * i / 5
        f.fill(f.AND(f.stroke([(x, z0 + h / 2), (x, z0 - h / 2)], INK * 0.55), m), NAVY)


def tear(f, x, z, s=1.0):
    m = f.OR(f.ell(x, z, 0.05 * s, 0.06 * s), f.poly([(x - 0.045 * s, z + 0.02 * s), (x + 0.045 * s, z + 0.02 * s),
                                                     (x, z + 0.13 * s)]))
    ring = f.SUB(f.OR(f.ell(x, z, 0.05 * s + 0.025, 0.06 * s + 0.025),
                      f.poly([(x - 0.07 * s, z + 0.02 * s), (x + 0.07 * s, z + 0.02 * s), (x, z + 0.17 * s)])), m)
    f.fill(ring, NAVY)
    f.fill(m, SEAM)
    f.fill(f.ell(x - 0.015 * s, z + 0.01 * s, 0.014 * s, 0.018 * s), WHITE)


# ------------------------------------------------------------------------------------------------ expressions
def neutral(f):
    face_plate(f)
    eye_open(f, -1, look=(0.30, -0.05), lid=0.18, lid_tilt=-0.02)
    eye_open(f, 1, look=(0.30, -0.05), lid=0.08)
    brow(f, -1, 0.42, 0.44, curve=0.02)
    brow(f, 1, 0.48, 0.60, curve=0.05)          # his left brow cocked up
    nose(f)
    blush(f, 0.45)
    smirk(f)


def happy(f):
    face_plate(f)
    eye_arc(f, -1, up=True)
    eye_arc(f, 1, up=True)
    brow(f, -1, 0.50, 0.50, curve=0.05)
    brow(f, 1, 0.50, 0.50, curve=0.05)
    nose(f)
    blush(f, 0.7, dz=0.02)
    open_mouth(f, 0.0, -0.33, 0.62, 0.30, top_curve=0.06)


def wince(f):
    face_plate(f)
    eye_chevron(f, -1)
    eye_chevron(f, 1)
    brow(f, -1, 0.30, 0.46, curve=-0.01, x_in=0.12)
    brow(f, 1, 0.30, 0.46, curve=-0.01, x_in=0.12)
    nose(f)
    blush(f, 0.35)
    gritted(f, 0.0, -0.45, 0.52, 0.18)


def point(f, side=1):
    face_plate(f)
    look = (0.95 * side, 0.15)
    eye_open(f, -1, look=look, lid=0.05)
    eye_open(f, 1, look=look, lid=0.05)
    brow(f, side, 0.50, 0.60, curve=0.05)
    brow(f, -side, 0.44, 0.48, curve=0.03)
    nose(f)
    blush(f, 0.45)
    o_mouth(f, 0.10 * side, -0.45)


def surprised(f):
    face_plate(f)
    eye_open(f, -1, look=(0, 0.05), r=0.27, rz=0.30, pupil=0.075)
    eye_open(f, 1, look=(0, 0.05), r=0.27, rz=0.30, pupil=0.075)
    brow(f, -1, 0.56, 0.62, curve=0.07)
    brow(f, 1, 0.56, 0.62, curve=0.07)
    nose(f)
    blush(f, 0.35)
    o_mouth(f, 0.0, -0.47, 0.13, 0.17)


def thinking(f):
    face_plate(f)
    look = (-0.55, 0.75)
    eye_open(f, -1, look=look, lid=0.0, low_lid=0.30)
    eye_open(f, 1, look=look, lid=0.0)
    brow(f, -1, 0.40, 0.38, curve=-0.02)
    brow(f, 1, 0.50, 0.64, curve=0.06)
    nose(f)
    blush(f, 0.35)
    pts = bez((0.02, -0.46), (0.14, -0.40), (0.26, -0.47))
    pts += bez((0.26, -0.47), (0.31, -0.50), (0.30, -0.42))[1:]
    f.fill(f.taper(pts, INK * 1.3, INK * 1.0), NAVY)


def laughing(f):
    face_plate(f)
    for s in (-1, 1):
        x0 = s * EYE_X
        pts = [(x0 - 0.20, EYE_Z + 0.02), (x0 - 0.04, EYE_Z + 0.13), (x0 + 0.04, EYE_Z + 0.13), (x0 + 0.20, EYE_Z + 0.02)]
        f.fill(f.taper(bez(pts[0], (x0, EYE_Z + 0.24), pts[3]), INK * 2.0, INK * 2.0), NAVY)
        tear(f, s * (EYE_X + 0.24), EYE_Z - 0.08, 0.9)
    brow(f, -1, 0.52, 0.48, curve=0.05)
    brow(f, 1, 0.52, 0.48, curve=0.05)
    nose(f)
    blush(f, 0.75, dz=0.03)
    open_mouth(f, 0.0, -0.30, 0.72, 0.36, top_curve=0.08)


def proud(f):
    face_plate(f)
    eye_arc(f, -1, up=False, dz=-0.02)
    eye_arc(f, 1, up=False, dz=-0.02)
    brow(f, -1, 0.50, 0.42, curve=0.04)
    brow(f, 1, 0.50, 0.42, curve=0.04)
    nose(f)
    blush(f, 0.6)
    # broad closed grin, curling up at both ends, one fang
    pts = bez((-0.30, -0.36), (0.0, -0.56), (0.30, -0.36))
    fang(f, 0.12, -0.47, h=0.09, w=0.065)
    f.fill(f.taper(pts, INK * 1.4, INK * 1.4), NAVY)
    for s in (-1, 1):
        f.fill(f.taper([(s * 0.27, -0.30), (s * 0.33, -0.39)], INK * 0.9, INK * 0.6), NAVY)


def sleepy(f):
    face_plate(f)
    eye_open(f, -1, look=(0.0, -0.6), lid=0.62, lid_tilt=-0.04)
    eye_open(f, 1, look=(0.0, -0.6), lid=0.66, lid_tilt=-0.04)
    brow(f, -1, 0.40, 0.33, curve=0.0)
    brow(f, 1, 0.40, 0.33, curve=0.0)
    nose(f)
    blush(f, 0.45)
    o_mouth(f, 0.04, -0.46, 0.07, 0.07)


def peek(f):
    face_plate(f)
    look = (0.75, -0.25)
    eye_open(f, -1, look=look, lid=0.30, lid_tilt=0.03, low_lid=0.18)
    eye_open(f, 1, look=look, lid=0.26, lid_tilt=0.03, low_lid=0.18)
    brow(f, -1, 0.34, 0.47, curve=-0.01)        # sly V brows
    brow(f, 1, 0.34, 0.47, curve=-0.01)
    nose(f)
    blush(f, 0.45)
    smirk(f, curl=0.12)


EXPRESSIONS = {
    "neutral": neutral, "happy": happy, "wince": wince, "point": lambda f: point(f, 1),
    "point-left": lambda f: point(f, -1), "surprised": surprised, "thinking": thinking, "laughing": laughing,
    "proud": proud, "sleepy": sleepy, "peek": peek,
}


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    tiles = []
    for stem, fn in EXPRESSIONS.items():
        f = Face()
        fn(f)
        im = f.out()
        im.save(os.path.join(out, "face-%s.png" % stem), optimize=True)
        tiles.append((stem, im))
        print("FACE", stem)
    if "--sheet" in sys.argv:
        path = sys.argv[sys.argv.index("--sheet") + 1]
        t = 240
        cols = 4
        rows = (len(tiles) + cols - 1) // cols
        sheet = Image.new("RGB", (cols * t, rows * t), (0x36, 0x42, 0x5A))   # PAL Skin behind, as on the head
        for i, (stem, im) in enumerate(tiles):
            sheet.paste(im.resize((t, t), Image.LANCZOS), ((i % cols) * t, (i // cols) * t), im.resize((t, t), Image.LANCZOS))
        sheet.save(path, quality=60)


if __name__ == "__main__":
    main()
