"""Skybreak Basin environment kit, version 2 (ART-DIRECTION-2026-11-12 sections 2.1-2.7, 5; brief A-05).

What changed from v1 (pieces.py, kept for the structure pieces another agent owns and for the ruins):
  * Built for the perspective gameplay camera (pitch 55, FOV 35, 23.4 u, yaw 45): only downward faces are dropped,
    so every piece may be yawed. Edge pieces are authored along local +X and placed by yaw from the rim outline.
  * Ground pieces carry height and material only (material CM_Ground). The pattern (Voronoi grass cells, macro bands,
    flagstones, kerbs, dirt) is a world-space shader reading a baked field texture, so the 1 m grid never shows.
  * Every drop gets a LIP: sawtooth grass fringe (teeth 0.10-0.18 u, pitch 0.125 u, overhang 0.08 u, grass dark),
    a Void Navy ink strip (0.065 u + 0.018 u curtain = about 4 px at the look target) and a 0.75 u dark face band.
  * Island rims are continuous CLIFF SKIRTS 1-6 cells long (straight) or 1-4 diagonal steps (45 degrees, along the
    rim fill triangles), built from 0.8-2.0 u facets with strata every 0.75 u, moss caps on the strata ledges and a
    jagged taper into the height fog; corner and bend columns close the joints.
  * Waterfalls: pool decal with a 0.15 u foam band and a 0.04 u ink line on the land side, foam collar at the lip,
    curved 0.3 u thick ribbon with three tone streaks (UV v = metres along the fall, for a 2 u/s scroll), splash puffs.
  * Rope bridge tile of 6 planks (0.30 u, 0.04 u gaps, plank 6 offset or chipped), posts every 2.04 u, sagging ropes.
  * Trees 2 species x 2 sizes (3-5 lobes, trunk tilt 3-8 degrees), faceted bevelled boulders with moss caps, rock
    clusters, crystal clusters with a lit facet, a self-lit tip and a 1.0 u ground glow decal, small field props.

All coordinates are Unity numbers (x east, y up, z = -sim z); geo.Mesh converts to Blender. 1 unit = 1 m = 1 cell.
"""
import math
import random

import bmesh
from mathutils import Matrix, Vector

from . import pieces as v1
from .geo import C, Mesh

SQ2 = math.sqrt(2.0)
PX = 2 * 23.4 * math.tan(math.radians(17.5)) / 1080    # metres per 1080p pixel at the look target (0.01365)
INK_W = 0.065           # ink strip width beyond the fringe outline
INK_DROP = 0.018        # ink curtain hanging down the face (keeps ~4 px when the lip faces the camera)
TOOTH = 0.125           # fringe tooth pitch (0.12 u in the brief; 0.125 tiles 8 per metre)
OVERHANG = 0.08
BAND = 0.75             # one Sim height step
FOAM_W = 0.15           # AD 2.5: 0.15 u (11 px across, 9 px foreshortened at the look target)
TILE = 2.04             # bridge tile: 6 x (0.30 plank + 0.04 gap)
UP = (0.0, 1.0, 0.0)
OUT = (0.0, 0.0, -1.0)  # outward for edge pieces


def newell(pts):
    n = Vector((0.0, 0.0, 0.0))
    for i in range(len(pts)):
        a, b = pts[i], pts[(i + 1) % len(pts)]
        n.x += (a[1] - b[1]) * (a[2] + b[2])
        n.y += (a[2] - b[2]) * (a[0] + b[0])
        n.z += (a[0] - b[0]) * (a[1] + b[1])
    return n


def F(m, pts, col, want, **kw):
    """Polygon facing the approximate direction `want` (Unity numbers)."""
    pts = [tuple(p) for p in pts]
    if newell(pts).dot(Vector(want)) < 0:
        pts.reverse()
        if kw.get("uv") is not None:
            kw["uv"] = list(reversed(kw["uv"]))
    return m.poly(pts, col, **kw)


def _n2(ax, az, bx, bz):
    """Outward (-z side) unit normal of the 2D segment a->b in the xz plane."""
    tx, tz = bx - ax, bz - az
    ln = math.hypot(tx, tz) or 1.0
    nx, nz = tz / ln, -tx / ln
    if nz > 0:
        nx, nz = -nx, -nz
    return nx, nz


def _offset_line(pts, dist, normals):
    return [(p[0] + n[0] * dist, p[1], p[2] + n[1] * dist) for p, n in zip(pts, normals)]


def _ink(m, outline, normals, y_drop=0.012):
    ink = C("ink")
    off = [(p[0] + n[0] * INK_W, p[1] - y_drop, p[2] + n[1] * INK_W) for p, n in zip(outline, normals)]
    for k in range(len(outline) - 1):
        p0, p1, q0, q1 = outline[k], outline[k + 1], off[k], off[k + 1]
        F(m, [(p0[0], p0[1] - 0.003, p0[2]), (p1[0], p1[1] - 0.003, p1[2]), q1, q0], ink, UP)
        F(m, [q0, q1, (q1[0], q1[1] - INK_DROP, q1[2]), (q0[0], q0[1] - INK_DROP, q0[2])], ink,
          (normals[k][0], 0.0, normals[k][1]))


def _miter_normals(outline, radial=None):
    """Per-point outward normals of a fringe outline (mitered, clamped)."""
    out = []
    n = len(outline)
    for k in range(n):
        if radial is not None:
            out.append(radial[k])
            continue
        segs = []
        if k > 0:
            segs.append(_n2(outline[k - 1][0], outline[k - 1][2], outline[k][0], outline[k][2]))
        if k < n - 1:
            segs.append(_n2(outline[k][0], outline[k][2], outline[k + 1][0], outline[k + 1][2]))
        sx, sz = sum(s[0] for s in segs), sum(s[1] for s in segs)
        ln = math.hypot(sx, sz) or 1.0
        mx, mz = sx / ln, sz / ln
        c = max(0.62, mx * segs[0][0] + mz * segs[0][1])
        out.append((mx / c, mz / c))
    return out


# ----------------------------------------------------------------------------------------------- lips and ledges
def fringe(m, a, b, rng, y0=0.0):
    """Sawtooth grass fringe along x in [a, b] on the lip z = 0 (outward -Z), with its ink line."""
    gd, gm = C("grass_dark"), C("grass_dark", 1.07)
    n = max(1, round((b - a) / TOOTH))
    p = (b - a) / n
    outline = []
    for i in range(n):
        x0, x1 = a + i * p, a + (i + 1) * p
        ln = rng.uniform(0.10, 0.18)
        tip = OVERHANG * rng.uniform(0.75, 1.15)
        base_in = max(0.025, ln - tip)
        xt = x0 + p * rng.uniform(0.3, 0.7)
        ty = y0 - rng.uniform(0.025, 0.055)
        col = gm if i % 3 == 0 else gd
        F(m, [(x0, y0 + 0.006, base_in), (x1, y0 + 0.006, base_in), (x1, y0 + 0.005, 0.0), (x0, y0 + 0.005, 0.0)], col, UP)
        F(m, [(x0, y0 + 0.005, 0.0), (x1, y0 + 0.005, 0.0), (xt, ty, -tip)], col, (0, 1, -0.6))
        outline += [(x0, y0 + 0.004, 0.0), (xt, ty, -tip)]
    outline.append((b, y0 + 0.004, 0.0))
    _ink(m, outline, _miter_normals(outline))


def face_band(m, a, b, rng, y_top=0.0, depth=BAND, extra=0.06):
    col = C("band")
    n = max(1, round((b - a) / 0.25))
    p = (b - a) / n
    ys = [y_top - depth - extra - (rng.uniform(0.0, 0.07) if 0 < i < n else 0.0) for i in range(n + 1)]
    for i in range(n):
        x0, x1 = a + i * p, a + (i + 1) * p
        F(m, [(x0, y_top, 0.0), (x1, y_top, 0.0), (x1, ys[i + 1], 0.0), (x0, ys[i], 0.0)], col, OUT)


def lip(m, seed, L):
    rng = random.Random(seed)
    fringe(m, 0.0, L, rng)
    face_band(m, 0.0, L, rng)


def lip_corner(m, seed):
    """Convex 90-degree corner at the origin: incoming lip along +X ends here (outward -Z), outgoing lip leaves
    along +Z (outward +X). Fills the quadrant x > 0, z < 0 with a fan of teeth and its ink."""
    rng = random.Random(seed)
    gd = C("grass_dark")
    u = lambda deg, r: (math.sin(math.radians(deg)) * r, -math.cos(math.radians(deg)) * r)
    outline, radial = [], []
    angs = [0, 15, 30, 45, 60, 75, 90]
    for k, a in enumerate(angs):
        r = OVERHANG * rng.uniform(0.8, 1.1) if k % 2 else (0.0 if k in (0, len(angs) - 1) else 0.025)
        x, z = u(a, r)
        outline.append((x, 0.004 - (0.04 if k % 2 else 0.0), z))
        radial.append(u(a, 1.0))
    for k in range(0, len(angs) - 2, 2):
        F(m, [(0.0, 0.006, 0.0), outline[k], outline[k + 1], outline[k + 2]], gd, (0, 1, 0))
    # valleys of a corner sit on the corner point; push them out a hair so the ink has a direction
    _ink(m, outline, _miter_normals(outline, radial))


def ledge_band(m, seed):
    """An extra 0.75 u face band for a multi-step ledge (stack at y - 0.75 per extra step)."""
    face_band(m, 0.0, 1.0, random.Random(seed), extra=0.04)


def ledge_shadow(m, seed):
    """Soft 0.5 u shadow band on the LOWER ground in front of a 1 u ledge lip (decal material, UV v = 0 at the
    ledge foot, 1 at 0.5 u out). Place with the lip (same pivot and yaw); local y = -0.75 + 0.004."""
    y = -BAND + 0.004
    F(m, [(0.0, y, 0.0), (1.0, y, 0.0), (1.0, y, -0.5), (0.0, y, -0.5)], C("grass_dark"), UP, mat=1,
      uv=[(0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0)])


# ------------------------------------------------------------------------------------------------------ ground
def ground_cell(m, seed):
    F(m, [(-0.5, 0.0, -0.5), (0.5, 0.0, -0.5), (0.5, 0.0, 0.5), (-0.5, 0.0, 0.5)], C("grass"), UP)


def ground_fill_tri(m, seed):
    """Rim fill: the half of a VOID cell on its (+X, -Z) corner (sim +x, +z), so a 45-degree staircase reads as a
    straight diagonal rim. Placed in void cells only (never on walkable ground); yaw = atan2(dz, dx) - 45."""
    F(m, [(0.5, 0.0, -0.5), (0.5, 0.0, 0.5), (-0.5, 0.0, -0.5)], C("grass"), UP)


# ------------------------------------------------------------------------------------------------------- cliffs
def _partition(L, lo, hi, rng):
    if L <= hi:
        return [L]
    out, r = [], L
    while r > hi:
        w = rng.uniform(lo, min(hi, r - lo))
        out.append(w)
        r -= w
    out.append(r)
    return out


def _facet(m, x0, x1, rng, y_top, back=0.6):
    rock = C("rock", rng.uniform(0.95, 1.04))
    moss, mossd = C("moss"), C("moss", 0.93)
    D = rng.uniform(3.8, 6.8)
    o = rng.uniform(-0.03, 0.07)
    y = y_top
    F(m, [(x0, y, back), (x1, y, back), (x1, y, -o), (x0, y, -o)], moss, UP)     # closed top (moss shelf outside)
    xf = x0 + (x1 - x0) * rng.uniform(0.35, 0.65)
    rock_a, rock_b = rock, C("rock_mid", rng.uniform(1.02, 1.08))
    k = 0
    while y - BAND > -(D - 1.3):
        rock = rock_a if k % 2 == 0 else rock_b      # strata read as alternating courses every 0.75 u
        k += 1
        yb = y - BAND
        ob = o + rng.uniform(-0.05, 0.02)
        fold = rng.uniform(0.02, 0.07)
        # two-plane face with a vertical fold (catches the light like a rock facet)
        F(m, [(x0, y, -o), (xf, y, -o - fold), (xf, yb, -ob - fold), (x0, yb, -ob)], rock, (-0.3, 0, -1))
        F(m, [(xf, y, -o - fold), (x1, y, -o), (x1, yb, -ob), (xf, yb, -ob - fold)], rock, (0.3, 0, -1))
        F(m, [(x0, y, -o), (x0, yb, -ob), (x0, yb, back), (x0, y, back)], rock, (-1, 0, 0))
        F(m, [(x1, y, -o), (x1, y, back), (x1, yb, back), (x1, yb, -ob)], rock, (1, 0, 0))
        F(m, [(x0, y, back), (x1, y, back), (x1, yb, back), (x0, yb, back)], rock, (0, 0, 1))
        on = ob - rng.uniform(0.07, 0.16)
        F(m, [(x0, yb, -ob), (xf, yb, -ob - fold), (x1, yb, -ob), (x1, yb, -on), (x0, yb, -on)],
          moss if rng.random() < 0.8 else mossd, UP)
        y, o = yb, on
    xm = x0 + (x1 - x0) * rng.uniform(0.3, 0.7)
    tip = (xm, -D, -o + 0.45)
    F(m, [(x0, y, -o), (x1, y, -o), tip], rock, (0, -0.3, -1))
    F(m, [(x0, y, -o), tip, (x0, y, back)], rock, (-1, -0.3, 0))
    F(m, [(x1, y, -o), (x1, y, back), tip], rock, (1, -0.3, 0))
    F(m, [(x0, y, back), tip, (x1, y, back)], rock, (0, -0.3, 1))


def cliff_skirt(m, seed, L):
    """Continuous rim skirt from local x = 0 to L under the lip (outward -Z), top at y = -0.75 (under the band)."""
    rng = random.Random(seed)
    xs = [0.0]
    for w in _partition(L, 0.8, 2.0, rng):
        xs.append(xs[-1] + w)
    xs[-1] = L
    for i in range(len(xs) - 1):
        _facet(m, xs[i], xs[i + 1], rng, -BAND)


def _column(m, rng, r0, n=8, y_top=-BAND, depth=(3.6, 5.8)):
    rock, moss = C("rock", rng.uniform(0.95, 1.03)), C("moss")
    D = rng.uniform(*depth)
    rot = rng.uniform(0, math.pi)
    ring = lambda r, y: [(r * math.cos(rot + 2 * math.pi * i / n) * rng.uniform(0.92, 1.06), y,
                          r * math.sin(rot + 2 * math.pi * i / n) * rng.uniform(0.92, 1.06)) for i in range(n)]
    y, r = y_top, r0
    top = ring(r, y)
    F(m, top, moss, UP)
    while y - BAND > -(D - 1.2):
        bot = ring(r * 0.97, y - BAND)
        for i in range(n):
            j = (i + 1) % n
            c = ((top[i][0] + top[j][0]) / 2, 0, (top[i][2] + top[j][2]) / 2)
            F(m, [top[i], top[j], bot[j], bot[i]], rock, c)
        r = r * rng.uniform(0.78, 0.9)
        nxt = ring(r, y - BAND)
        for i in range(n):
            j = (i + 1) % n
            F(m, [bot[i], bot[j], nxt[j], nxt[i]], moss, UP)
        top, y = nxt, y - BAND
    tip = (rng.uniform(-0.1, 0.1), -D, rng.uniform(-0.1, 0.1))
    for i in range(n):
        j = (i + 1) % n
        c = ((top[i][0] + top[j][0]) / 2, -0.4, (top[i][2] + top[j][2]) / 2)
        F(m, [top[i], top[j], tip], rock, c)


def cliff_corner(m, seed):
    _column(m, random.Random(seed), 0.36)


def cliff_bend(m, seed):
    _column(m, random.Random(seed), 0.24, n=7, depth=(3.4, 5.2))


# -------------------------------------------------------------------------------------------------------- water
def _dense(poly, step):
    out = []
    for i in range(len(poly) - 1):
        a, b = poly[i], poly[i + 1]
        k = max(1, int(math.hypot(b[0] - a[0], b[1] - a[1]) / step))
        out += [(a[0] + (b[0] - a[0]) * t / k, a[1] + (b[1] - a[1]) * t / k) for t in range(k)]
    out.append(poly[-1])
    return out


def waterfall(m, seed, fall=6.5):
    """Spring pool on the rim cell + ribbon off the lip. Pivot: lip midpoint at ground height; lip along local X
    from -0.5 to 0.5, outward -Z. Pool and foam lie 0.012-0.016 u above the ground (decal layer)."""
    rng = random.Random(seed)
    water, foam, ink = C("water_f"), C("foam"), C("ink")
    # pool (z > 0 is inland)
    edge = [(0.44, -0.06), (0.44, 0.42), (0.26, 0.70), (-0.26, 0.70), (-0.44, 0.42), (-0.44, -0.06)]
    F(m, [(x, 0.012, z) for (x, z) in edge], water, UP)
    pts = _dense(edge, 0.05)
    nin = []
    for k in range(len(pts)):
        a, b = pts[max(0, k - 1)], pts[min(len(pts) - 1, k + 1)]
        tx, tz = b[0] - a[0], b[1] - a[1]
        ln = math.hypot(tx, tz) or 1.0
        nx, nz = -tz / ln, tx / ln          # left normal of a clockwise-in-(x,z) walk = towards the pool centre
        cx, cz = 0.0, 0.3
        if (cx - pts[k][0]) * nx + (cz - pts[k][1]) * nz < 0:
            nx, nz = -nx, -nz
        nin.append((nx, nz))
    inner = [(p[0] + n[0] * FOAM_W * rng.uniform(0.85, 1.15), p[1] + n[1] * FOAM_W * rng.uniform(0.85, 1.15))
             for p, n in zip(pts, nin)]
    outer = [(p[0] - n[0] * 0.04, p[1] - n[1] * 0.04) for p, n in zip(pts, nin)]
    for k in range(len(pts) - 1):
        F(m, [(pts[k][0], 0.015, pts[k][1]), (pts[k + 1][0], 0.015, pts[k + 1][1]),
              (inner[k + 1][0], 0.015, inner[k + 1][1]), (inner[k][0], 0.015, inner[k][1])], foam, UP)
        F(m, [(outer[k][0], 0.013, outer[k][1]), (outer[k + 1][0], 0.013, outer[k + 1][1]),
              (pts[k + 1][0], 0.013, pts[k + 1][1]), (pts[k][0], 0.013, pts[k][1])], ink, UP)
    # foam collar on the lip (jagged both sides), z from +0.07 to -0.10
    xs = [-0.44 + 0.88 * i / 12 for i in range(13)]
    za = [0.07 + rng.uniform(-0.02, 0.02) for _ in xs]
    zb = [-0.10 + rng.uniform(-0.025, 0.02) for _ in xs]
    for i in range(12):
        F(m, [(xs[i], 0.016, za[i]), (xs[i + 1], 0.016, za[i + 1]), (xs[i + 1], 0.016, zb[i + 1]), (xs[i], 0.016, zb[i])], foam, UP)
    # ribbon: centre curve leaves the lip horizontally then falls; front sheet + back sheet 0.3 behind
    rows = 16
    cols = [C("water_lf"), C("water_f"), C("water_df"), C("water_f"), C("water_lf"), C("water_df")]
    nc = len(cols)

    def cpt(t):
        s = t * fall
        return -s, -0.08 - 0.62 * math.sqrt(t)

    for r in range(rows):
        t0, t1 = r / rows, (r + 1) / rows
        (y0, z0), (y1, z1) = cpt(t0), cpt(t1)
        w0, w1 = 0.36 - 0.05 * t0, 0.36 - 0.05 * t1
        for c in range(nc):
            u0, u1 = c / nc, (c + 1) / nc
            xa0, xb0 = -w0 + 2 * w0 * u0, -w0 + 2 * w0 * u1
            xa1, xb1 = -w1 + 2 * w1 * u0, -w1 + 2 * w1 * u1
            col = foam if r == 0 else cols[(c + r // 5) % nc]
            uv = [(u0, t0 * fall), (u1, t0 * fall), (u1, t1 * fall), (u0, t1 * fall)]
            F(m, [(xa0, y0, z0), (xb0, y0, z0), (xb1, y1, z1), (xa1, y1, z1)], col, (0, 0.15, -1), uv=uv)
            F(m, [(xa0, y0 - 0.3, z0 + 0.12), (xb0, y0 - 0.3, z0 + 0.12), (xb1, y1 - 0.3, z1 + 0.3),
                  (xa1, y1 - 0.3, z1 + 0.3)], cols[c], (0, 0, 1))
        for sx, w_0, w_1 in ((-1, w0, w1), (1, w0, w1)):
            F(m, [(sx * w_0, y0, z0), (sx * w_1, y1, z1), (sx * w_1, y1 - 0.3, z1 + 0.3), (sx * w_0, y0 - 0.3, z0 + 0.12)],
              cols[2], (sx, 0, 0))
    # splash puffs where it meets the cloud sea
    yb, zb_ = cpt(1.0)
    for k in range(3):
        m.blob(rng.uniform(-0.3, 0.3), yb + rng.uniform(-0.2, 0.2), zb_ + rng.uniform(-0.3, 0.1), 0.55, 0.32, 0.5,
               C("cloud"), sub=2, smooth=True, seed=seed + k, jitter=0.12)


# ------------------------------------------------------------------------------------------------------ bridges
def _rope(m, a, b, sag, col, segs=8, r=0.03):
    pts = []
    for i in range(segs + 1):
        t = i / segs
        pts.append(Vector((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t - sag * 4 * t * (1 - t),
                           a[2] + (b[2] - a[2]) * t)))
    for i in range(segs):
        p, q = pts[i], pts[i + 1]
        d = (q - p).normalized()
        side = d.cross(Vector(UP)).normalized()
        up = side.cross(d).normalized()
        offs = [side * r + up * r, -side * r + up * r, -side * r - up * r, side * r - up * r]
        for k in range(4):
            o0, o1 = offs[k], offs[(k + 1) % 4]
            F(m, [p + o0, p + o1, q + o1, q + o0], col, tuple((o0 + o1).normalized()))


def bridge_span(m, seed):
    """One 2.04 u tile of rope bridge along local +Z (3.0 u wide), deck top at y = +0.05 above the pivot (so the
    rim ink never draws over the deck). Pivot: deck centre of the near end; tile and stretch along the bridge."""
    rng = random.Random(seed)
    for i in range(6):
        z0 = i * 0.34 + 0.02
        z1 = z0 + 0.30
        dx, cut = 0.0, 0.0
        if i == 5:
            if rng.random() < 0.5:
                dx = rng.choice((-0.14, 0.14))
            else:
                cut = 0.10
        xl = -1.52 - rng.uniform(0.0, 0.05) + dx
        xr = 1.52 + rng.uniform(0.0, 0.05) + dx - cut
        col = C("wood", rng.uniform(0.95, 1.05)) if i % 2 else C("wood_b", rng.uniform(0.95, 1.05))
        m.cbox(xl, -0.03, z0, xr, 0.05, z1, col, top=col, ch=0.025)
    wd = C("wood_dark")
    F(m, [(-1.45, -0.026, 0.0), (1.45, -0.026, 0.0), (1.45, -0.026, TILE), (-1.45, -0.026, TILE)], wd, UP)
    for x0 in (-1.25, 1.05):
        m.box(x0, -0.17, 0.0, x0 + 0.2, -0.03, TILE, wd)
    for sx in (-1.58, 1.58):
        m.cbox(sx - 0.08, -0.3, -0.08, sx + 0.08, 0.80, 0.08, wd, top=C("wood"), ch=0.03)
        _rope(m, (sx, 0.72, 0.0), (sx, 0.72, TILE), 0.13, C("rope"))
        _rope(m, (sx, 0.40, 0.0), (sx, 0.40, TILE), 0.08, C("rope"), r=0.025)


def bridge_post(m, seed):
    m.cbox(-0.08, -0.3, -0.08, 0.08, 0.80, 0.08, C("wood_dark"), top=C("wood"), ch=0.03)


def bridge_pier(m, seed):
    """Mossy stone pier flanking a bridge mouth (bible p14 bridge): two bevelled blocks on a rock foot."""
    rng = random.Random(seed)
    st, st2, moss = C("wall", rng.uniform(0.96, 1.03)), C("wall_b"), C("moss")
    m.cbox(-0.32, -0.9, -0.32, 0.32, 0.42, 0.32, st2, top=st, ch=0.05)
    m.cbox(-0.26, 0.42, -0.26, 0.26, 0.86, 0.26, st, top=moss, ch=0.05)


# -------------------------------------------------------------------------------------------------------- trees
def _trunk(m, base, top, r0, r1, col, n=7):
    a, b = Vector(base), Vector(top)
    d = (b - a).normalized()
    s = d.cross(Vector((0, 0, 1))).normalized()
    t = d.cross(s).normalized()
    ring = lambda c, r: [c + (s * math.cos(2 * math.pi * i / n) + t * math.sin(2 * math.pi * i / n)) * r for i in range(n)]
    lo, hi = ring(a, r0), ring(b, r1)
    for i in range(n):
        j = (i + 1) % n
        F(m, [lo[i], lo[j], hi[j], hi[i]], col, tuple(((lo[i] + lo[j]) / 2 - a).normalized()))


def tree(m, seed, species="round", size="l"):
    rng = random.Random(seed)
    Dc = {("round", "l"): 3.0, ("round", "s"): 2.0, ("tall", "l"): 2.6, ("tall", "s"): 1.7}[(species, size)]
    th = Dc * (0.5 if species == "round" else 0.7)
    tilt, ta = math.radians(rng.uniform(3, 8)), rng.uniform(0, 2 * math.pi)
    top = (math.sin(tilt) * th * math.cos(ta), th, math.sin(tilt) * th * math.sin(ta))
    _trunk(m, (0, -0.05, 0), top, 0.075 * Dc, 0.045 * Dc, C("trunk"))
    _trunk(m, (0, -0.05, 0), (top[0] * 0.3 + 0.05 * Dc, 0.25 * Dc, top[2] * 0.3), 0.06 * Dc, 0.03 * Dc, C("trunk", 0.95), n=5)
    leaf = C("leaf_light")
    if species == "round":
        lobes = [(0.0, 0.36 * Dc, 0.0, 0.34 * Dc)]
        k = rng.choice((3, 4))
        for i in range(k):
            a = 2 * math.pi * i / k + rng.uniform(-0.3, 0.3)
            lobes.append((math.cos(a) * 0.36 * Dc, 0.12 * Dc + rng.uniform(-0.05, 0.08) * Dc, math.sin(a) * 0.36 * Dc,
                          rng.uniform(0.27, 0.33) * Dc))
    else:
        lobes = []
        k = rng.choice((3, 4))
        for i in range(k):
            f = i / (k - 1)
            lobes.append((rng.uniform(-0.06, 0.06) * Dc, (0.05 + 0.62 * f) * Dc, rng.uniform(-0.06, 0.06) * Dc,
                          (0.40 - 0.16 * f) * Dc))
    for i, (x, y, z, r) in enumerate(lobes):
        m.blob(top[0] + x, top[1] + y, top[2] + z, r, r * 0.86, r, C("leaf_light", 0.93 + 0.035 * (i % 3)), sub=2, smooth=True, seed=seed * 7 + i,
               jitter=0.05, flat_bottom=top[1] - 0.15 * Dc)


# -------------------------------------------------------------------------------------------------------- rocks
FACET_LOG = {}


def boulder(m, seed, size, at=(0.0, 0.0, 0.0), flat=0.62, log=None):
    rng = random.Random(seed)
    rx, ry, rz = size / 2, size * flat / 2, size / 2 * rng.uniform(0.8, 1.1)
    tmp = bmesh.new()
    vs = []
    for _ in range(rng.randint(8, 10)):
        th, ph = rng.uniform(0, 2 * math.pi), math.acos(rng.uniform(-0.35, 1.0))
        vs.append(tmp.verts.new((math.sin(ph) * math.cos(th) * rx, math.cos(ph) * ry + ry * 0.55,
                                 math.sin(ph) * math.sin(th) * rz)))
    for k in range(4):     # a flat footprint
        a = 2 * math.pi * k / 4 + 0.4
        vs.append(tmp.verts.new((math.cos(a) * rx * 0.8, -0.04, math.sin(a) * rz * 0.8)))
    res = bmesh.ops.convex_hull(tmp, input=vs)
    bmesh.ops.delete(tmp, geom=[g for g in res["geom_interior"] + res["geom_unused"] if isinstance(g, bmesh.types.BMVert)], context="VERTS")
    bmesh.ops.dissolve_limit(tmp, angle_limit=math.radians(15), verts=list(tmp.verts), edges=list(tmp.edges))
    if log is not None:
        log.append(len(tmp.faces))
    bmesh.ops.bevel(tmp, geom=list(tmp.edges), offset=0.035 * size, segments=1, affect="EDGES", profile=0.5)
    tmp.normal_update()
    rock, moss = C("rock", rng.uniform(0.96, 1.05)), C("moss")
    for f in tmp.faces:
        pts = [(v.co.x + at[0], v.co.y + at[1], v.co.z + at[2]) for v in f.verts]
        n = f.normal
        F(m, pts, moss if n.y > 0.78 and f.calc_area() > 0.004 * size * size else rock, (n.x, n.y, n.z))
    tmp.free()


def rock_cluster(m, seed):
    rng = random.Random(seed)
    k = rng.randint(3, 5)
    for i in range(k):
        a = 2 * math.pi * i / k + rng.uniform(-0.4, 0.4)
        r = 0.0 if i == 0 else rng.uniform(0.35, 0.6)
        boulder(m, seed * 13 + i, (0.9 if i == 0 else rng.uniform(0.35, 0.6)), (math.cos(a) * r, 0, math.sin(a) * r))
    for i in range(5):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(0.5, 0.85)
        boulder(m, seed * 31 + i, rng.uniform(0.1, 0.17), (math.cos(a) * r, 0, math.sin(a) * r), flat=0.5)


def pebbles(m, seed):
    rng = random.Random(seed)
    for i in range(rng.randint(4, 6)):
        a, r = rng.uniform(0, 2 * math.pi), rng.uniform(0.05, 0.32)
        boulder(m, seed * 17 + i, rng.uniform(0.09, 0.19), (math.cos(a) * r, 0, math.sin(a) * r), flat=0.5)


# ----------------------------------------------------------------------------------------------------- crystals
TO_SUN = Vector((-math.cos(math.radians(50)), math.sin(math.radians(50)), 0.0))


def _spike(m, base, h, r, yaw, tilt, body, lit):
    n = 6
    R = Matrix.Rotation(yaw, 3, "Y") @ Matrix.Rotation(tilt, 3, "Z")
    b = Vector(base)
    ring = lambda rr, y: [b + R @ Vector((rr * math.cos(2 * math.pi * i / n), y, rr * math.sin(2 * math.pi * i / n))) for i in range(n)]
    lo, hi = ring(r, -0.05), ring(r * 0.9, h * 0.72)
    tip = b + R @ Vector((0, h, 0))
    best, bi = -9, 0
    for i in range(n):
        j = (i + 1) % n
        nrm = ((lo[i] + lo[j]) / 2 - b - R @ Vector((0, 0.3, 0))).normalized()
        if nrm.dot(TO_SUN) > best:
            best, bi = nrm.dot(TO_SUN), i
    for i in range(n):
        j = (i + 1) % n
        out = ((lo[i] + lo[j]) / 2 - (b + R @ Vector((0, (h * 0.36), 0)))).normalized()
        F(m, [lo[i], lo[j], hi[j], hi[i]], lit if i == bi else body, tuple(out))
        F(m, [hi[i], hi[j], tip], lit if i in (bi, (bi + 1) % n, (bi - 1) % n) else body, tuple((out + R @ Vector((0, 0.8, 0))).normalized()))


def _decal(m, r, col, y=0.008, n=20):
    for i in range(n):
        a0, a1 = 2 * math.pi * i / n, 2 * math.pi * (i + 1) / n
        F(m, [(0, y, 0), (math.cos(a0) * r, y, math.sin(a0) * r), (math.cos(a1) * r, y, math.sin(a1) * r)], col, UP, mat=1,
          uv=[(0.5, 0.5), (0.5 + 0.5 * math.cos(a0), 0.5 + 0.5 * math.sin(a0)), (0.5 + 0.5 * math.cos(a1), 0.5 + 0.5 * math.sin(a1))])


def crystal_cluster(m, seed, hmax=1.8, k=6, kind="team"):
    rng = random.Random(seed)
    body, lit = {"team": (C("team_dark"), C("team_glow")), "biomass": (C("biomass_cd"), C("biomass_c")),
                 "corrupt": (C("corrupt_cd"), C("corrupt_c"))}[kind]
    _spike(m, (0, 0, 0), hmax, 0.09 * hmax + 0.08, rng.uniform(0, 6.28), math.radians(rng.uniform(2, 7)), body, lit)
    for i in range(k - 1):
        a = 2 * math.pi * i / (k - 1) + rng.uniform(-0.3, 0.3)
        rr = rng.uniform(0.14, 0.32) * (hmax / 1.8 + 0.4)
        h = hmax * rng.uniform(0.45, 0.8)
        _spike(m, (math.cos(a) * rr, 0, math.sin(a) * rr), h, 0.07 * hmax + 0.06, -a, math.radians(rng.uniform(12, 28)), body, lit)
    _decal(m, 1.0, lit)


# ------------------------------------------------------------------------------------------------- small props
def tuft(m, seed, at=(0, 0, 0), flowers=False):
    rng = random.Random(seed)
    for i in range(rng.randint(6, 9)):
        a, r = rng.uniform(0, 2 * math.pi), rng.uniform(0.0, 0.1)
        bx, bz = at[0] + math.cos(a) * r, at[2] + math.sin(a) * r
        h, w, lean = rng.uniform(0.12, 0.24), 0.035, rng.uniform(0.02, 0.07)
        ca = rng.uniform(0, math.pi)
        dx, dz = math.cos(ca) * w, math.sin(ca) * w
        tip = (bx + math.cos(a) * lean, h, bz + math.sin(a) * lean)
        col = C("grass_dark") if i % 2 else C("grass_l")
        F(m, [(bx - dx, 0, bz - dz), (bx + dx, 0, bz + dz), tip], col, (-dz, 0.2, dx))
        F(m, [(bx - dx, 0, bz - dz), (bx + dx, 0, bz + dz), tip], col, (dz, 0.2, -dx))
    if flowers:
        for i in range(rng.randint(3, 5)):
            a, r = rng.uniform(0, 2 * math.pi), rng.uniform(0.04, 0.2)
            x, z, y = at[0] + math.cos(a) * r, at[2] + math.sin(a) * r, rng.uniform(0.11, 0.17)
            col = C("flower") if i % 2 else C("flower_y")
            s = 0.045
            for (p, q) in (((s, 0), (0, s)), ((0, s), (-s, 0)), ((-s, 0), (0, -s)), ((0, -s), (s, 0))):
                F(m, [(x, y + s * 0.6, z), (x + p[0], y, z + p[1]), (x + q[0], y, z + q[1])], col, UP)


def grass_tuft(m, seed):
    rng = random.Random(seed)
    for k in range(3):
        tuft(m, seed * 5 + k, (rng.uniform(-0.25, 0.25), 0, rng.uniform(-0.25, 0.25)))


def flowers(m, seed):
    tuft(m, seed, flowers=True)
    tuft(m, seed + 1, (0.22, 0, -0.12), flowers=True)


def stump(m, seed):
    rng = random.Random(seed)
    m.prism(0, 0, 0.21, -0.05, 0.24, C("trunk"), n=8, top=C("wood"), rot=rng.uniform(0, 1), r_top=0.18, ch=0.03)
    for k in range(3):
        a = 2 * math.pi * k / 3 + rng.uniform(-0.3, 0.3)
        m.prism(math.cos(a) * 0.2, math.sin(a) * 0.2, 0.06, -0.03, 0.07, C("trunk", 0.92), n=5, r_top=0.03)


def bush_low(m, seed):
    rng = random.Random(seed)
    for k in range(3):
        a = 2 * math.pi * k / 3 + rng.uniform(-0.4, 0.4)
        r = rng.uniform(0.16, 0.24)
        m.blob(math.cos(a) * 0.2, 0.0, math.sin(a) * 0.2, r, 0.28, r, C("leaf_light"), sub=2, smooth=True,
               seed=seed + k, jitter=0.06, flat_bottom=0.0)


# ---------------------------------------------------------------------------------------------------------- sky
def sky_rock(m, seed, s):
    """Floating sky rock: grassy cap with a lip, banded taper into the fog. Pivot: cap top centre."""
    rng = random.Random(seed)
    n = 9
    radii = [s * rng.uniform(0.85, 1.1) for _ in range(n)]
    cap = [(math.cos(2 * math.pi * i / n) * radii[i], 0.0, math.sin(2 * math.pi * i / n) * radii[i]) for i in range(n)]
    F(m, cap, C("grass"), UP)
    band = [(p[0] * 1.0, -0.3, p[2] * 1.0) for p in cap]
    for i in range(n):
        j = (i + 1) % n
        F(m, [cap[i], cap[j], band[j], band[i]], C("band"), (cap[i][0] + cap[j][0], 0, cap[i][2] + cap[j][2]))
    top, y, k = band, -0.3, 1.0
    depth = 3.4 * s + 0.6
    while y - BAND > -(depth - s):
        k *= rng.uniform(0.8, 0.9)
        bot = [(p[0] * 0.97, y - BAND, p[2] * 0.97) for p in top]
        for i in range(n):
            j = (i + 1) % n
            F(m, [top[i], top[j], bot[j], bot[i]], C("rock", rng.uniform(0.95, 1.04)), (top[i][0] + top[j][0], 0, top[i][2] + top[j][2]))
        nxt = [(math.cos(2 * math.pi * i / n) * radii[i] * k, y - BAND, math.sin(2 * math.pi * i / n) * radii[i] * k) for i in range(n)]
        for i in range(n):
            j = (i + 1) % n
            F(m, [bot[i], bot[j], nxt[j], nxt[i]], C("moss"), UP)
        top, y = nxt, y - BAND
    tip = (rng.uniform(-0.2, 0.2) * s, -depth, rng.uniform(-0.2, 0.2) * s)
    for i in range(n):
        j = (i + 1) % n
        F(m, [top[i], top[j], tip], C("rock"), (top[i][0] + top[j][0], -0.5, top[i][2] + top[j][2]))


# ----------------------------------------------------------------------------------------------------- registry
CELL = "cell top centre (cx+0.5, h, -(cz+0.5))"
EDGE = "lip start point at ground height; local +X runs along the rim, outward -Z; yaw = atan2(dz, dx) in sim"


def _p(name, fn, cat, pivot, covers, outline_px=0.0, mats=("CM_Toon",), seed=0, far=False):
    return dict(name=name, fn=fn, cat=cat, pivot=pivot, covers=covers, outline_px=outline_px, mats=list(mats),
                seed=seed, far=far, cull="down")


PIECES = [
    _p("env_ground_cell", ground_cell, "ground", CELL, "p12/p14/p15 ground: pattern by CM_Ground (world space)", mats=("CM_Ground",), seed=1),
    _p("env_ground_fill_tri", ground_fill_tri, "ground", "void cell centre; yaw = atan2(dz,dx)-45 to its land corner", "rim smoothing (45-degree rims)", mats=("CM_Ground",), seed=2),
    _p("env_ground_placed", v1.ground_placed, "ground", CELL, "CM-30 Miner-placed block", outline_px=2.0, seed=61),
    _p("env_lip_straight_a", lambda m, s: lip(m, s, 1.0), "edge", EDGE, "every ledge and rim: fringe + ink + face band", seed=301),
    _p("env_lip_straight_b", lambda m, s: lip(m, s, 1.0), "edge", EDGE, "lip variant", seed=302),
    _p("env_lip_straight_c", lambda m, s: lip(m, s, 1.0), "edge", EDGE, "lip variant", seed=303),
    _p("env_lip_diag_a", lambda m, s: lip(m, s, SQ2), "edge", EDGE + " (1.414 u, along a fill hypotenuse)", "diagonal rim lip", seed=311),
    _p("env_lip_diag_b", lambda m, s: lip(m, s, SQ2), "edge", EDGE + " (1.414 u)", "diagonal rim lip", seed=312),
    _p("env_lip_corner", lip_corner, "edge", "convex 90-degree corner point; yaw of the incoming edge", "lip outer corner", seed=321),
    _p("env_ledge_band", ledge_band, "edge", EDGE + "; stack at y-0.75 per extra step", "multi-step ledge face", seed=331),
    _p("env_ledge_shadow", ledge_shadow, "edge", EDGE + " (upper lip); decal on the lower ground", "ledge soft shadow 0.5 u", mats=("CM_Toon", "CM_ShadowDecal"), seed=332),
]
for _n in range(1, 7):
    PIECES.append(_p(f"env_cliff_s{_n}", (lambda L: (lambda m, s: cliff_skirt(m, s, L)))(float(_n)), "cliff",
                     EDGE + f"; {_n} cell(s) long", "p12/p15 continuous island cliff skirt", outline_px=2.0, seed=400 + _n))
for _n in range(1, 5):
    PIECES.append(_p(f"env_cliff_d{_n}", (lambda L: (lambda m, s: cliff_skirt(m, s, L)))(_n * SQ2), "cliff",
                     EDGE + f"; {_n} diagonal step(s), {_n * SQ2:.3f} u", "p12 cliff skirt on a 45-degree rim", outline_px=2.0, seed=420 + _n))
PIECES += [
    _p("env_cliff_corner", cliff_corner, "cliff", "rim corner point, island height", "cliff outer corner column", outline_px=2.0, seed=441),
    _p("env_cliff_bend", cliff_bend, "cliff", "rim 45-degree bend point", "cliff bend column", outline_px=2.0, seed=442),
    _p("env_waterfall", waterfall, "water", "rim edge midpoint (1 u axis edge); yaw = atan2(dz, dx) of the edge", "p12/p15 waterfall + pool + foam", seed=151),
    _p("env_bridge_span", bridge_span, "bridge", "deck centre of the near end; local +Z along the bridge, stretch to fit", "p14 rope bridge tile (6 planks, posts, ropes)", outline_px=2.0, seed=91),
    _p("env_bridge_post", bridge_post, "bridge", "post foot at deck height", "p14 bridge end post", outline_px=2.0, seed=93),
    _p("env_bridge_pier", bridge_pier, "bridge", "lip point beside a bridge mouth", "p14 mossy stone pier", outline_px=2.0, seed=94),
    _p("env_bridge_cell", v1.bridge_cell, "bridge", CELL, "BRIDGE cell outside a span (stair-step of diagonal bridges)", outline_px=0.0, seed=92),
    _p("env_tree_round_l", lambda m, s: tree(m, s, "round", "l"), "tree", CELL, "p12/p15 round tree, 3.0 u canopy", outline_px=2.5, seed=181),
    _p("env_tree_round_s", lambda m, s: tree(m, s, "round", "s"), "tree", CELL, "round tree, 2.0 u", outline_px=2.5, seed=182),
    _p("env_tree_tall_l", lambda m, s: tree(m, s, "tall", "l"), "tree", CELL, "tall stacked tree, 2.6 u", outline_px=2.5, seed=183),
    _p("env_tree_tall_s", lambda m, s: tree(m, s, "tall", "s"), "tree", CELL, "tall stacked tree, 1.7 u", outline_px=2.5, seed=184),
    _p("env_boulder_s", lambda m, s: boulder(m, s, 0.5, log=FACET_LOG.setdefault("env_boulder_s", [])), "rock_big", CELL, "boulder 0.5 u", outline_px=2.0, seed=501),
    _p("env_boulder_m", lambda m, s: boulder(m, s, 0.95, log=FACET_LOG.setdefault("env_boulder_m", [])), "rock_big", CELL, "boulder 0.95 u", outline_px=2.0, seed=502),
    _p("env_boulder_l", lambda m, s: boulder(m, s, 1.45, log=FACET_LOG.setdefault("env_boulder_l", [])), "rock_big", CELL, "boulder 1.45 u", outline_px=2.0, seed=503),
    _p("env_rocks", rock_cluster, "rock_big", CELL, "rock cluster 3-5 + pebbles", outline_px=2.0, seed=184),
    _p("env_crystal_cluster", lambda m, s: crystal_cluster(m, s, 1.8, 6), "crystal", CELL, "p12/p14 team crystal cluster (game sets team)", outline_px=2.5, mats=("CM_Toon", "CM_GlowDecal"), seed=121),
    _p("env_crystal_cluster_small", lambda m, s: crystal_cluster(m, s, 0.95, 4), "crystal", CELL, "small team crystal cluster", outline_px=2.5, mats=("CM_Toon", "CM_GlowDecal"), seed=122),
    _p("env_crystal_biomass", lambda m, s: crystal_cluster(m, s, 1.2, 5, "biomass"), "crystal", CELL, "Biomass crystal cluster", outline_px=2.5, mats=("CM_Toon", "CM_GlowDecal"), seed=123),
    _p("env_crystal_corrupt", lambda m, s: crystal_cluster(m, s, 1.3, 5, "corrupt"), "crystal", CELL, "Corruption crystal cluster", outline_px=2.5, mats=("CM_Toon", "CM_GlowDecal"), seed=124),
    _p("env_pebbles", pebbles, "small", CELL, "pebbles (under 0.12 u)", outline_px=2.5, seed=601),
    _p("env_grass_tuft", grass_tuft, "small", CELL, "grass tufts (under 0.25 u)", seed=602),
    _p("env_flowers", flowers, "small", CELL, "flower tuft (under 0.25 u)", seed=603),
    _p("env_stump", stump, "small", CELL, "tree stump (0.24 u)", outline_px=2.5, seed=604),
    _p("env_bush", bush_low, "small", CELL, "low bush (0.28 u)", outline_px=2.5, seed=605),
    _p("env_sky_rock_s", lambda m, s: sky_rock(m, s, 0.7), "sky", "cap top centre", "p12 floating rock", outline_px=1.0, seed=191, far=True),
    _p("env_sky_rock_m", lambda m, s: sky_rock(m, s, 1.2), "sky", "cap top centre", "p12 floating rock spire", outline_px=1.0, seed=192, far=True),
    _p("env_sky_rock_l", lambda m, s: sky_rock(m, s, 2.0), "sky", "cap top centre", "p12 floating rock spire", outline_px=1.0, seed=193, far=True),
    _p("env_cloud_puff", v1.cloud_puff, "sky", "puff base centre", "p12 cloud sea puff", seed=201),
    _p("env_cloud_floor", v1.cloud_floor, "sky", "centre", "p12 cloud sea floor", seed=202),
    _p("env_ruin_column_intact", v1.ruin_column_intact, "ruin", CELL, "CM-31 pillar standing", outline_px=2.0, seed=161),
    _p("env_ruin_column_fallen_ramp", v1.ruin_column_fallen_ramp, "ruin", "base cell; yaw to the ramp direction", "CM-31 pillar as ramp", outline_px=2.0, seed=162),
    _p("env_ruin_column_fallen_block", v1.ruin_column_fallen_block, "ruin", "base cell; yaw to the block direction", "CM-31 pillar as block", outline_px=2.0, seed=163),
    _p("env_ruin_block", v1.ruin_block, "ruin", CELL, "tumbled ruin blocks", outline_px=2.0, seed=171),
    _p("env_ruin_stub", v1.ruin_stub, "ruin", CELL, "broken pillar stub", outline_px=2.0, seed=172),
    _p("env_ruin_arch", v1.ruin_arch, "ruin", "centre cell top centre", "broken arch", outline_px=2.0, seed=173),
]
BY_NAME = {p["name"]: p for p in PIECES}

# v1 pieces that belong to the bases-and-structures agent: not exported by this kit; built only as stand-ins in renders.
STRUCTURE_STANDINS = ["env_wall", "env_wall_breakable_intact", "env_wall_breakable_cracked", "env_wall_breakable_destroyed",
                      "env_hill_plaza", "env_hill_rim_half", "env_spawn_pad", "env_banner", "env_shrine_dais",
                      "env_shrine_horns", "env_shrine_egg_proxy"]
# v1 FBX files this kit retires (replaced by v2 pieces above)
RETIRED = ["env_ground_grass_a", "env_ground_grass_b", "env_ground_grass_c", "env_ground_path_a", "env_ground_path_b",
           "env_ground_path_c", "env_ground_mossy", "env_ground_dirt", "env_ground_corrupt", "env_ground_fill",
           "env_cliff_a", "env_cliff_b", "env_cliff_c", "env_edge_stones", "env_tree_a", "env_tree_b"]


def build_piece(spec, collection=None):
    m = Mesh(spec["name"], cull="down")
    spec["fn"](m, spec["seed"])
    return m.finish(collection)
