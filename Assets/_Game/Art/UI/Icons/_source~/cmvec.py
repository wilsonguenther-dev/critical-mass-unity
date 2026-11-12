"""Shared vector toolkit for the Critical Mass UI art (icons, portraits, logo).

Every drawing is shapely geometry turned into SVG paths, in the bible's sticker language: one thick Void Navy
silhouette outline (constant screen px at the size the HUD shows it), thinner navy inner lines, flat fills in three tone
steps (base, a shade crescent on the lower right, a light sliver on the upper left: light from the upper left), plus
explicit glints. No gradients. Rasterised with rsvg-convert (Homebrew librsvg).
"""
import math
import subprocess

from shapely import affinity
from shapely.geometry import LineString, MultiPolygon, Point, Polygon, box, GeometryCollection
from shapely.ops import unary_union

NAVY = '#18233B'
CREAM = '#F7F0E5'
WHITE = '#FFFFFF'

# Tone triples: (base, shade, light). Team and swatch bases are the p05 hexes (Sim/Config/Palette.cs).
TEAM = {
    'azure': dict(base='#16C7D9', shade='#0B8DB0', deep='#0A587C', light='#A2F2FB', tint='#CFF3F7', glow='#7FF0FF'),
    'crimson': dict(base='#FF5A5F', shade='#C9343F', deep='#86202F', light='#FFB8B1', tint='#FBDAD5', glow='#FFC2A8'),
}
METAL = ('#C9D3DF', '#8494A9', '#F6F9FC')
STEEL_DARK = ('#5D6A80', '#3E4A5E', '#8C9AB0')
WOOD = ('#B9824E', '#7E5332', '#E2AE76')
BRASS = ('#C79245', '#8C5F24', '#F3CF83')
CREAMT = (CREAM, '#D6C8B0', WHITE)
STONE = ('#6F7282', '#4D5060', '#9A9DAA')
STRAW = ('#EBC66A', '#B98E37', '#FFE7A6')
BIOMASS = ('#8ED83A', '#4F9A2A', '#DDFF8A')     # body around the p05 Biomass #B7F238 (used as the light band)
CORRUPT = ('#8E3FD1', '#5A2390', '#C88CF2')      # p05 Corruption #8E3FD1
MAGENTA = ('#D63C8E', '#9A2264', '#FF8CC6')
SLATE = ('#3D4864', '#28304A', '#6C7A99')
LEAF = ('#7CC63A', '#4A8A26', '#C8F06A')
EGG = ('#3A4052', '#2A2F3F', '#59617A')
SEAM_TEAL = '#4FDDC3'
SEAM_RED = '#FF5A5F'
KAIJU_N = ('#6E9C94', '#4A726E', '#A9D6CB')
BONE = ('#EADFC6', '#BFAF8E', '#FFFFFF')
BLUSH = '#F49A86'


def hexrgb(h):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def mix(a, b, t):
    ra, rb = hexrgb(a), hexrgb(b)
    return '#%02X%02X%02X' % tuple(round(ra[i] + (rb[i] - ra[i]) * t) for i in range(3))


# ---------------------------------------------------------------- geometry helpers

def P(pts):
    return Polygon(pts).buffer(0)


def circ(cx, cy, r):
    return Point(cx, cy).buffer(r, quad_segs=24)


def ell(cx, cy, rx, ry, rot=0):
    g = affinity.scale(Point(0, 0).buffer(1, quad_segs=24), rx, ry)
    if rot:
        g = affinity.rotate(g, rot, origin=(0, 0))
    return affinity.translate(g, cx, cy)


def rrect(x, y, w, h, r):
    r = min(r, w / 2 - 0.01, h / 2 - 0.01)
    return box(x + r, y + r, x + w - r, y + h - r).buffer(r, quad_segs=12)


def cub(p0, p1, p2, p3, n=18):
    out = []
    for i in range(n + 1):
        t = i / n
        u = 1 - t
        out.append((u ** 3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t ** 3 * p3[0],
                    u ** 3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t ** 3 * p3[1]))
    return out


def stroke_geom(pts, w, cap='round'):
    cs = {'round': 'round', 'flat': 'flat', 'square': 'square'}[cap]
    return LineString(pts).buffer(w / 2, cap_style=cs, join_style='round', quad_segs=10)


def taper(pts, w0, w1):
    """A brush stroke whose width goes from w0 to w1 along the polyline (union of circles + hull segments)."""
    gs = []
    n = len(pts)
    for i in range(n - 1):
        a, b = pts[i], pts[i + 1]
        ra = (w0 + (w1 - w0) * i / (n - 1)) / 2
        rb = (w0 + (w1 - w0) * (i + 1) / (n - 1)) / 2
        gs.append(unary_union([circ(a[0], a[1], max(ra, 0.05)), circ(b[0], b[1], max(rb, 0.05))]).convex_hull)
    return unary_union(gs)


def rot(g, deg, origin):
    return affinity.rotate(g, deg, origin=origin)


def mv(g, dx, dy):
    return affinity.translate(g, dx, dy)


def sc(g, sx, sy=None, origin='center'):
    return affinity.scale(g, sx, sy if sy is not None else sx, origin=origin)


def mirror_x(g, cx):
    return affinity.scale(g, -1, 1, origin=(cx, 0))


def polys(g):
    if g is None or g.is_empty:
        return []
    if isinstance(g, Polygon):
        return [g]
    if isinstance(g, (MultiPolygon, GeometryCollection)):
        out = []
        for x in g.geoms:
            out += polys(x)
        return out
    return []


def path_d(g):
    parts = []
    for p in polys(g):
        for ring in [p.exterior] + list(p.interiors):
            c = list(ring.coords)
            if len(c) < 3:
                continue
            parts.append('M' + ' L'.join(f'{x:.2f},{y:.2f}' for x, y in c[:-1]) + 'Z')
    return ' '.join(parts)


# ---------------------------------------------------------------- the drawing

class Art:
    """One square (or w x h) vector drawing in a viewBox; px = the size it is shown at in the HUD at 1080p."""

    def __init__(self, name, px, vb=128, out_px=None, vbh=None):
        self.name, self.px, self.vb, self.vbh = name, px, vb, vbh or vb
        self.unit = vb / px
        op = out_px if out_px is not None else (3.0 if px >= 40 else 2.0 if px >= 22 else 1.5)
        self.OUT = op * self.unit
        self.IN = max(1.0, op * 0.5) * self.unit
        self.layers = []           # (kind, payload)
        self.sil = []
        self.defs = []
        self.clip = None

    # --- primitives
    def fill(self, g, color, sil=True, opacity=None):
        if g is None or g.is_empty:
            return
        op = f' fill-opacity="{opacity}"' if opacity is not None else ''
        self.layers.append(f'<path d="{path_d(g)}" fill="{color}" fill-rule="evenodd"{op}/>')
        if sil:
            self.sil.append(g)

    def line(self, g, w=None, color=NAVY):
        if g is None or g.is_empty:
            return
        w = self.IN * 2 if w is None else w
        self.layers.append(f'<path d="{path_d(g)}" fill="none" stroke="{color}" stroke-width="{w:.2f}" '
                           f'stroke-linejoin="round"/>')

    def ink(self, g, sil=False):
        """A solid navy shape (brow, pupil, crack) with no outline of its own."""
        self.fill(g, NAVY, sil=sil)

    def toon(self, g, tones, k=0.13, hl=0.07, line=True, sil=True, shade_dir=(-1, -1), glint=None):
        """Base fill, shade crescent lower right, light sliver upper left, navy inner line."""
        if g is None or g.is_empty:
            return
        base, shade, light = tones
        self.fill(g, base, sil=sil)
        size = math.sqrt(max(g.area, 1.0))
        if k:
            d = k * size
            sh = g.difference(mv(g, shade_dir[0] * d, shade_dir[1] * d))
            self.fill(sh, shade, sil=False)
        if hl:
            gi = g.buffer(-self.IN * 1.6)
            if not gi.is_empty:
                d = hl * size
                self.fill(gi.difference(mv(gi, d, d)), light, sil=False)
        if glint is not None:
            self.fill(glint.intersection(g.buffer(-self.IN)), light if glint is True else glint, sil=False)
        if line:
            self.line(g)

    def glint(self, cx, cy, rx, ry, rot_deg=-35, color=WHITE):
        self.fill(ell(cx, cy, rx, ry, rot_deg), color, sil=False)

    # --- output
    def svg(self):
        sil = unary_union(self.sil) if self.sil else None
        out = []
        if sil is not None and not sil.is_empty:
            out.append(f'<path d="{path_d(sil.buffer(self.OUT, join_style="round", quad_segs=10))}" fill="{NAVY}"/>')
        body = ''.join(out + self.layers)
        zoom = getattr(self, 'zoom', None)
        if zoom:
            k, zx, zy = zoom
            body = f'<g transform="translate({zx} {zy}) scale({k}) translate({-zx} {-zy})">{body}</g>'
        if self.clip is not None:
            self.defs.append(f'<clipPath id="clip_{self.name}"><path d="{path_d(self.clip)}"/></clipPath>')
            body = f'<g clip-path="url(#clip_{self.name})">{body}</g>'
        pre = ''.join(getattr(self, 'under', []))
        return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {self.vb} {self.vbh}" '
                f'width="{self.vb}" height="{self.vbh}"><defs>{"".join(self.defs)}</defs>{pre}{body}</svg>')


def raster(svg_path, png_path, w, h=None):
    subprocess.run(['rsvg-convert', '-w', str(w), '-h', str(h or w), '-o', png_path, svg_path], check=True)


# ---------------------------------------------------------------- shared pieces

def eye(a, cx, cy, rx, ry, look=(0.0, 0.0), lid=0.0, lid_side=0, iris=None, skin=None):
    """A big cartoon eye: white, navy pupil (or coloured iris with a navy pupil), two catchlights.
    lid: 0..1 how far an angled upper lid comes down; lid_side -1 slants down to the inner (left eye: right) corner."""
    white = ell(cx, cy, rx, ry)
    a.fill(white, WHITE)
    px, py = cx + look[0] * rx * 0.32, cy + look[1] * ry * 0.28
    if iris:
        ir = ell(px, py, rx * 0.72, ry * 0.74).intersection(white)
        a.fill(ir, iris, sil=False)
        a.ink(ell(px, py + ry * 0.04, rx * 0.4, ry * 0.44).intersection(white))
    else:
        a.ink(ell(px, py, rx * 0.64, ry * 0.7).intersection(white))
    a.fill(circ(px - rx * 0.24, py - ry * 0.3, rx * 0.26), WHITE, sil=False)
    a.fill(circ(px + rx * 0.22, py + ry * 0.26, rx * 0.11), WHITE, sil=False)
    if lid > 0 and skin is not None:
        top = cy - ry * 1.3
        hi = cy - ry + 2 * ry * lid * (0.55 if lid_side else 1.0)
        lo = cy - ry + 2 * ry * lid * (1.25 if lid_side else 1.0)
        l, r = (lo, hi) if lid_side < 0 else (hi, lo)
        cut = P([(cx - rx * 1.4, top), (cx + rx * 1.4, top), (cx + rx * 1.4, r), (cx - rx * 1.4, l)])
        a.fill(cut.intersection(white.buffer(a.IN * 0.6)), skin, sil=False)
        a.ink(LineString([(cx - rx * 1.05, l), (cx + rx * 1.05, r)]).buffer(a.IN * 1.25, cap_style='round')
              .intersection(white.buffer(a.IN * 1.6)))
    a.line(white)


def brow(a, x0, y0, x1, y1, w, bend=0.0):
    mx, my = (x0 + x1) / 2, (y0 + y1) / 2 - bend
    a.ink(taper(cub((x0, y0), (mx, my), (mx, my), (x1, y1), 8), w * 0.9, w * 1.25), sil=True)
