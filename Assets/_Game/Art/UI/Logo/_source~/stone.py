"""Carved-stone wordmark and faceted egg for the Critical Mass logo (v2, bible p04).

Letters: Bungee outlines (SIL OFL 1.1) flattened to polygons, the edges chiselled (segmentised, jittered, simplified, so
each letter reads as a hewn block rather than a font outline), then layered as stone: a deep extruded side under each
letter in the darkest tone with facet lines, the face in the base tone, a lighter top plane along the upper-left edges,
a darker bevel along the lower-right edges, two or three lighter facet planes, cracks, and moss caps with drips on the
top edges (the bible's accent detail). Around the whole block a Warm Cream inner rim and a thick Void Navy outline.
Deterministic (seeded); our own letter treatment, never the reference's letterforms.
"""
import math
import os
import random
import sys

from fontTools.pens.basePen import BasePen
from shapely.geometry import LineString, Polygon

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.abspath(os.path.join(HERE, '..', '..', 'Icons', '_source~')))
from cmvec import (NAVY, CREAM, P, box, circ, mv, path_d, polys, unary_union, stroke_geom, taper, mix,  # noqa: E402
                   SEAM_TEAL, SEAM_RED)

# (face, top plane, bevel shade, side, side dark, facet)
CRITICAL = dict(face='#3FD6EE', top='#A5F1FA', shade='#16AFCF', side='#0E7F98', side_dark='#0A5A75', facet='#6FE3F4')
MASS = dict(face='#FF7A57', top='#FFB08F', shade='#E0502F', side='#A8321F', side_dark='#7A2216', facet='#FF9673')
MOSS = ('#8DBF3A', '#5E8F2A', '#C2F04E')


class FlatPen(BasePen):
    def __init__(self, glyphset, xf):
        super().__init__(glyphset)
        self.xf, self.contours, self.cur = xf, [], []

    def _moveTo(self, p):
        self.cur = [self.xf(p)]

    def _lineTo(self, p):
        self.cur.append(self.xf(p))

    def _curveToOne(self, p1, p2, p3):
        p0 = self._getCurrentPoint()
        for i in range(1, 9):
            t = i / 8
            u = 1 - t
            self.cur.append(self.xf((u ** 3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t ** 3 * p3[0],
                                     u ** 3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t ** 3 * p3[1])))

    def _qCurveToOne(self, p1, p2):
        p0 = self._getCurrentPoint()
        for i in range(1, 7):
            t = i / 6
            u = 1 - t
            self.cur.append(self.xf((u * u * p0[0] + 2 * u * t * p1[0] + t * t * p2[0], u * u * p0[1] + 2 * u * t * p1[1] + t * t * p2[1])))

    def _closePath(self):
        if len(self.cur) > 2:
            self.contours.append(self.cur)
        self.cur = []

    _endPath = _closePath


def glyph_geom(glyphs, cmap, cap, ch, x, y, size, tilt_deg):
    g = glyphs[cmap[ord(ch)]]
    s = size / cap
    adv = g.width * s
    cx, cy = x + adv / 2, y - size / 2
    a = math.radians(tilt_deg)
    ca, sa = math.cos(a), math.sin(a)

    def xf(p):
        X, Y = x + p[0] * s - cx, y - p[1] * s - cy
        return (ca * X - sa * Y + cx, sa * X + ca * Y + cy)

    pen = FlatPen(glyphs, xf)
    g.draw(pen)
    geom = None
    for c in pen.contours:
        poly = Polygon(c).buffer(0)
        geom = poly if geom is None else geom.symmetric_difference(poly)
    return geom, adv


def chisel(g, size, rng):
    step, amp = size * 0.16, size * 0.035
    out = []
    for p in polys(g):
        rings = []
        for ring in [p.exterior] + list(p.interiors):
            seg = LineString(ring.coords).segmentize(step)
            pts = [(x + rng.uniform(-amp, amp), y + rng.uniform(-amp, amp)) for x, y in list(seg.coords)[:-1]]
            rings.append(Polygon(pts).buffer(0).simplify(amp * 0.6))
        shell = rings[0]
        for h in rings[1:]:
            shell = shell.difference(h)
        out.append(shell)
    return unary_union(out).buffer(-size * 0.01).buffer(size * 0.01)


def word_geoms(glyphs, cmap, cap, text, x0, base, size, tilts, lifts, tracking, seed=1):
    rng = random.Random(seed)
    x, out = x0, []
    for i, ch in enumerate(text):
        g, adv = glyph_geom(glyphs, cmap, cap, ch, x, base - lifts[i % len(lifts)], size, tilts[i % len(tilts)])
        out.append(chisel(g.buffer(size * 0.035, join_style='mitre', mitre_limit=2.5), size, rng))   # bolder block letters
        x += adv + tracking
    return out


def F(g, color):
    if g is None or g.is_empty:
        return ''
    return f'<path d="{path_d(g)}" fill="{color}" fill-rule="evenodd"/>'


def L(g, w, color=NAVY):
    if g is None or g.is_empty:
        return ''
    return f'<path d="{path_d(g)}" fill="none" stroke="{color}" stroke-width="{w:.1f}" stroke-linejoin="round"/>'


def carve(letters, sizes, tones, seed=5):
    rng = random.Random(seed)
    out_under, out = [], []
    sides, faces = [], []
    for g, size, tn in zip(letters, sizes, tones):
        depth = size * 0.16
        steps = 8
        side = unary_union([mv(g, depth * 0.18 * k / steps, depth * k / steps) for k in range(steps + 1)])
        sides.append((side, g, size, tn))
        faces.append(g)
    block = unary_union([s for s, _, _, _ in sides])
    ref = sizes[0]
    out_under.append(F(block.buffer(ref * 0.2, join_style='round'), NAVY))
    out_under.append(F(block.buffer(ref * 0.075, join_style='round'), CREAM))
    out_under.append(F(block.buffer(ref * 0.035, join_style='round'), NAVY))
    for side, g, size, tn in sides:
        # extruded side with a darker lower band and vertical facet seams
        sd = side.difference(g)
        out.append(F(side, tn['side']))
        out.append(F(sd.difference(mv(g, size * 0.03, size * 0.1 + size * 0.05)), tn['side_dark']))
        seams = []
        for p in polys(g):
            for x, y in list(p.exterior.coords)[::3]:
                seams.append(LineString([(x, y), (x + size * 0.03, y + size * 0.16)]).buffer(size * 0.012))
        out.append(F(unary_union(seams).intersection(sd), tn['side_dark']))
        out.append(L(side, size * 0.03))
    for side, g, size, tn in sides:
        out.append(F(g, tn['face']))
        # faceted planes: a few lighter chunks cut by random lines
        minx, miny, maxx, maxy = g.bounds
        for _ in range(3):
            if rng.random() < 0.35:
                continue
            x0 = rng.uniform(minx, maxx)
            ang = rng.uniform(-0.9, 0.9)
            pl = P([(x0, miny - 50), (x0 + 400, miny - 50), (x0 + 400 + ang * 400, maxy + 50), (x0 + ang * 400, maxy + 50)])
            if rng.random() < 0.5:
                pl = P([(x0, miny - 50), (x0 - 400, miny - 50), (x0 - 400 + ang * 400, maxy + 50), (x0 + ang * 400, maxy + 50)])
            out.append(F(g.intersection(pl).intersection(mv(g, size * 0.05, size * 0.05)), tn['facet']))
            break
        d = size * 0.075
        out.append(F(g.difference(mv(g, -d * 0.7, -d)), tn['shade']))       # lower-right bevel
        out.append(F(g.difference(mv(g, d * 0.6, d)), tn['top']))            # upper-left top plane
        hl = g.buffer(-size * 0.035)
        out.append(F(hl.difference(mv(hl, size * 0.012, size * 0.018)), mix(tn['top'], '#FFFFFF', 0.45)))
        # cracks
        inner = g.buffer(-size * 0.06)
        cr = []
        for _ in range(2):
            if inner.is_empty:
                break
            ix0, iy0, ix1, iy1 = inner.bounds
            px, py = rng.uniform(ix0, ix1), rng.uniform(iy0, iy1)
            pts = [(px, py)]
            ang = rng.uniform(0, math.pi * 2)
            for k in range(3):
                ang += rng.uniform(-0.9, 0.9)
                ln = size * rng.uniform(0.08, 0.16)
                pts.append((pts[-1][0] + ln * math.cos(ang), pts[-1][1] + ln * math.sin(ang)))
            cr.append(taper(pts, size * 0.035, size * 0.008))
            br = pts[1]
            cr.append(taper([br, (br[0] + size * 0.08 * math.cos(ang + 1.2), br[1] + size * 0.08 * math.sin(ang + 1.2))],
                            size * 0.022, size * 0.006))
        out.append(F(unary_union(cr).intersection(inner), NAVY))
        out.append(L(g, size * 0.028))
    # moss caps with drips on the top edges of some letters
    for i, (side, g, size, tn) in enumerate(sides):
        if rng.random() < 0.45:
            continue
        minx, miny, maxx, maxy = g.bounds
        topband = g.difference(mv(g, 0, size * 0.13))
        cx = rng.uniform(minx + (maxx - minx) * 0.2, maxx - (maxx - minx) * 0.2)
        blob = unary_union([circ(cx + rng.uniform(-size * 0.2, size * 0.2), miny + size * rng.uniform(0.0, 0.08), size * rng.uniform(0.14, 0.22))
                            for _ in range(3)])
        moss = topband.buffer(size * 0.025).intersection(blob)
        drips = []
        for _ in range(2):
            dx = cx + rng.uniform(-size * 0.15, size * 0.15)
            drips.append(stroke_geom([(dx, miny + size * 0.05), (dx, miny + size * rng.uniform(0.14, 0.24))], size * 0.06))
        moss = unary_union([moss, unary_union(drips).intersection(g.buffer(size * 0.01)).intersection(blob.buffer(size * 0.12))])
        if moss.is_empty:
            continue
        out.append(F(moss, MOSS[0]))
        out.append(F(moss.difference(mv(moss, -size * 0.02, -size * 0.04)), MOSS[1]))
        mh = moss.buffer(-size * 0.02)
        out.append(F(mh.difference(mv(mh, size * 0.02, size * 0.03)), MOSS[2]))
        out.append(L(moss, size * 0.024))
    return ''.join(out_under + out)


def egg(cx, cy, rx, ry, seed=3):
    """Faceted stone egg: hex cells with per-cell tone steps over glowing seams, teal left and red right, a bright crack."""
    rng = random.Random(seed)
    pts = []
    for i in range(64):
        t = 2 * math.pi * i / 64
        k = 1.0 - 0.18 * max(0.0, -math.sin(t))
        pts.append((cx + rx * k * math.cos(t), cy + ry * math.sin(t) * (1.0 if math.sin(t) > 0 else 1.08)))
    g = P(pts)
    out = [F(g.buffer(14), NAVY), F(g.intersection(box(0, 0, cx, 4000)), SEAM_TEAL), F(g.difference(box(0, 0, cx, 4000)), SEAM_RED)]
    glow_l, glow_r = '#B8FFF0', '#FFC1B8'
    cw, ch = rx * 0.5, ry * 0.36
    for row in range(-4, 5):
        for col in range(-4, 5):
            px = cx + col * cw + (cw / 2 if row % 2 else 0)
            py = cy + row * ch
            hexp = [(px + cw * 0.56 * math.cos(math.radians(60 * k + 30)) + rng.uniform(-8, 8),
                     py + ch * 0.66 * math.sin(math.radians(60 * k + 30)) + rng.uniform(-8, 8)) for k in range(6)]
            c = P(hexp).buffer(-3.5).intersection(g.buffer(-4))
            if c.area < 200:
                continue
            dark = rng.random() < 0.35
            base, sh, lt = ('#2A2F3F', '#1E2232', '#3E4558') if dark else ('#3A4052', '#2A2F3F', '#59617A')
            out.append(F(c, base))
            size = math.sqrt(c.area)
            out.append(F(c.difference(mv(c, -size * 0.18, -size * 0.18)), sh))
            ci = c.buffer(-4)
            out.append(F(ci.difference(mv(ci, size * 0.12, size * 0.12)), lt))
            # seam glow rim inside each cell edge facing the light
            out.append(L(c, 2, glow_l if px < cx else glow_r))
    crack = taper([(cx - 10, cy - ry * 1.05), (cx + 12, cy - ry * 0.6), (cx - 8, cy - ry * 0.25), (cx + 10, cy + ry * 0.1)], 16, 5)
    out.append(F(crack.intersection(g), '#E9FFF9'))
    out.append(L(g, 12))
    return ''.join(out)
