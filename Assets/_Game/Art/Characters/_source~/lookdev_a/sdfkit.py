"""Tiny signed-distance modelling kit (numpy only). Look-development approach A, 2026-11-12.

A character is a list of (group, Shape). A Shape is a function P (N,3 float32, metres) -> signed distance (N,).
Forms are exact or near-exact distance fields, so silhouettes are smooth curves and every bevel has a stated radius.
Boolean operators: hard union `a | b`, smooth union `a.su(b, k)`, cut `a.cut(b, k)`, intersection `a & b`.
"""
import math

import numpy as np

F = np.float32


def _rot(rx, ry, rz):
    """Blender Euler XYZ (degrees) -> 3x3."""
    a, b, c = (math.radians(v) for v in (rx, ry, rz))
    Rx = np.array([[1, 0, 0], [0, math.cos(a), -math.sin(a)], [0, math.sin(a), math.cos(a)]])
    Ry = np.array([[math.cos(b), 0, math.sin(b)], [0, 1, 0], [-math.sin(b), 0, math.cos(b)]])
    Rz = np.array([[math.cos(c), -math.sin(c), 0], [math.sin(c), math.cos(c), 0], [0, 0, 1]])
    return Rz @ Ry @ Rx


def M(loc=(0, 0, 0), rot=(0, 0, 0), s=1.0):
    m = np.eye(4)
    m[:3, :3] = _rot(*rot) * s
    m[:3, 3] = loc
    return m


def frame(origin, z_axis, y_hint):
    z = np.array(z_axis, float)
    z /= np.linalg.norm(z)
    y = np.array(y_hint, float)
    y = y - z * y.dot(z)
    y /= np.linalg.norm(y)
    x = np.cross(y, z)
    m = np.eye(4)
    m[:3, 0], m[:3, 1], m[:3, 2], m[:3, 3] = x, y, z, origin
    return m


def smin(a, b, k):
    if k <= 0:
        return np.minimum(a, b)
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0, 1)
    return b + (a - b) * h - k * h * (1 - h)


def smax(a, b, k):
    return -smin(-a, -b, k)


class Shape:
    def __init__(self, f):
        self.f = f

    def __call__(self, P):
        return self.f(P).astype(F, copy=False)

    def at(self, m):
        """Place with a 4x4 (rotation, uniform scale, translation)."""
        m = np.asarray(m, float)
        s = abs(np.linalg.det(m[:3, :3])) ** (1 / 3)
        Ri = (m[:3, :3] / s).astype(F)        # orthonormal: local = (P - t) @ R / s
        t = m[:3, 3].astype(F)
        f = self.f
        return Shape(lambda P: f(((P - t) @ Ri) / F(s)) * F(s))

    def scaled(self, sx, sy, sz):
        """Non-uniform scale about the origin (distance is approximate: use mild ratios)."""
        sc = np.array([sx, sy, sz], F)
        f = self.f
        return Shape(lambda P: f(P / sc) * F(min(sx, sy, sz)))

    def round(self, r):
        f = self.f
        return Shape(lambda P: f(P) - F(r))

    def __or__(self, o):
        return Shape(lambda P: np.minimum(self.f(P), o.f(P)))

    def __and__(self, o):
        return Shape(lambda P: np.maximum(self.f(P), o.f(P)))

    def su(self, o, k):
        return Shape(lambda P: smin(self.f(P), o.f(P), k))

    def cut(self, o, k=0.004):
        return Shape(lambda P: smax(self.f(P), -o.f(P), k))

    def isect(self, o, k=0.004):
        return Shape(lambda P: smax(self.f(P), o.f(P), k))


def union(shapes, k=0.0):
    shapes = list(shapes)

    def f(P):
        d = shapes[0].f(P)
        for s in shapes[1:]:
            d = smin(d, s.f(P), k)
        return d
    return Shape(f)


# ------------------------------------------------------------------------------------------------ primitives
def ell(c, r, rot=None):
    """Ellipsoid, centre c, radii r (good near the surface)."""
    r = np.array(r, F)

    def f(P):
        k0 = np.linalg.norm(P / r, axis=1)
        k1 = np.linalg.norm(P / (r * r), axis=1)
        return np.where(k1 > 1e-9, k0 * (k0 - 1.0) / np.maximum(k1, 1e-9), -r.min())
    return Shape(f).at(M(c, rot or (0, 0, 0)))


def rbox(half, r):
    """Box with half extents `half` (outer), every edge rounded with radius r. Centred, axis aligned."""
    b = np.array(half, F) - F(r)

    def f(P):
        q = np.abs(P) - b
        return np.linalg.norm(np.maximum(q, 0), axis=1) + np.minimum(q.max(axis=1), 0) - F(r)
    return Shape(f)


def plane(n, off):
    """Half space dot(p, n) > off is OUTSIDE (positive)."""
    n = np.array(n, F)
    n /= np.linalg.norm(n)
    return Shape(lambda P: P @ n - F(off))


def chipped(shape, half, rng, chips=3, depth=0.03, k=0.003, lo=0.6):
    """Cut `chips` clean facets off corners of a centred block (crisp chamfers, k = edge softness)."""
    corners = [(sx, sy, sz) for sx in (1, -1) for sy in (1, -1) for sz in (1, -1)]
    rng.shuffle(corners)
    out = shape
    for c in corners[:chips]:
        n = np.array([c[0] * rng.uniform(lo, 1.0), c[1] * rng.uniform(lo, 1.0), c[2] * rng.uniform(lo, 1.0)])
        n /= np.linalg.norm(n)
        off = float(np.dot(np.array(c) * np.array(half), n)) - depth * rng.uniform(0.7, 1.3)
        out = out.isect(Shape(lambda P, n=n.astype(F), off=F(off): P @ n - off), k)
    return out


def cone(a, b, r1, r2, rnd=0.0, cap=True):
    """Capped cone from a (radius r1) to b (radius r2); rnd rounds the rims. cap=False: round ends (capsule)."""
    a, b = np.array(a, F), np.array(b, F)
    ab = b - a
    L = float(np.linalg.norm(ab))
    u = ab / F(L)

    def f(P):
        pa = P - a
        t = pa @ u
        tc = np.clip(t, 0, L)
        rr = r1 + (r2 - r1) * (tc / L)
        q = np.sqrt(np.maximum((pa * pa).sum(1) - t * t, 0))
        if not cap:
            return np.sqrt((t - tc) ** 2 + q * q) - rr
        dr = q - (rr - rnd)
        dz = np.abs(t - L / 2) - (L / 2 - rnd)
        return np.minimum(np.maximum(dr, dz), 0) + np.sqrt(np.maximum(dr, 0) ** 2 + np.maximum(dz, 0) ** 2) - rnd
    return Shape(f)


def torus(R, r, squash=1.0):
    """Torus in local XY around Z; squash < 1 flattens the tube along Z."""
    def f(P):
        q = np.sqrt(P[:, 0] ** 2 + P[:, 1] ** 2) - R
        return np.sqrt(q * q + (P[:, 2] / squash) ** 2) - r
    return Shape(f)


def loop_tube(R, Ry, zf, rf, y0=0.0, squash=0.85):
    """Closed roll round the vertical axis (scarf): centre (R cos a, y0 + Ry sin a, zf(a)), tube radius rf(a)."""
    def f(P):
        a = np.arctan2((P[:, 1] - y0) / Ry, P[:, 0] / R)
        cx, cy, cz = R * np.cos(a), y0 + Ry * np.sin(a), zf(a)
        dx, dy, dz = P[:, 0] - cx, P[:, 1] - cy, (P[:, 2] - cz) / squash
        return np.sqrt(dx * dx + dy * dy + dz * dz) - rf(a)
    return Shape(f)


def disc(r, h, boss=True):
    """Brass plate facing +Z: flat rim, raised inner dome, centre boss (bible: ring + dome)."""
    rim = cone((0, 0, -0.02), (0, 0, h * 0.42), r, r * 0.96, rnd=min(0.012, h * 0.2))
    dome = ell((0, 0, h * 0.30), (r * 0.70, r * 0.70, h * 0.78))
    s = rim | dome
    return s


def prism(points, half_t, rnd=0.01):
    """Polygon `points` [(x, y)...] in local XY extruded +-half_t along Z, edges rounded by rnd."""
    pts = np.array(points, F)
    a = pts
    b = np.roll(pts, -1, axis=0)
    e = b - a

    def f(P):
        p = P[:, None, :2]
        w = p - a[None]
        t = np.clip((w * e[None]).sum(2) / (e * e).sum(1)[None], 0, 1)
        d2 = ((w - e[None] * t[..., None]) ** 2).sum(2).min(1)
        c1 = a[None, :, 1] <= p[..., 1]
        c2 = b[None, :, 1] > p[..., 1]
        cr = e[None, :, 0] * w[..., 1] - e[None, :, 1] * w[..., 0]
        cross = (c1 & c2 & (cr > 0)) | (~c1 & ~c2 & (cr < 0))
        inside = (cross.sum(1) % 2) == 1
        d = np.sqrt(d2) * np.where(inside, -1, 1) + rnd
        dz = np.abs(P[:, 2]) - (half_t - rnd)
        return np.minimum(np.maximum(d, dz), 0) + np.sqrt(np.maximum(d, 0) ** 2 + np.maximum(dz, 0) ** 2) - rnd
    return Shape(f)


def lock(p0, p1, p2, w, t, up=(0, 0, 1), prof=(1.0, 1.1, 0.86, 0.46), tip=0.10, n=20):
    """Hair lock / cloth tail: an elliptical section (width w, thickness t) swept along the quadratic Bezier
    p0 -> p1 -> p2; `prof` scales the section along the way and it closes to a soft point (tip) at p2."""
    P0, P1, P2 = (np.array(p, F) for p in (p0, p1, p2))
    ts = np.linspace(0, 1, n).astype(F)
    C = ((1 - ts) ** 2)[:, None] * P0 + (2 * ts * (1 - ts))[:, None] * P1 + (ts ** 2)[:, None] * P2
    prof = list(prof) + [tip]
    px = np.linspace(0, 1, len(prof))
    upv = np.array(up, F)

    def f(P):
        best = np.full(len(P), 1e9, F)
        for i in range(n - 1):
            a, b = C[i], C[i + 1]
            ab = b - a
            L2 = float(ab @ ab)
            pa = P - a
            tt = np.clip((pa @ ab) / L2, 0, 1)
            o = pa - tt[:, None] * ab
            tg = ab / math.sqrt(L2)
            u_ = upv - tg * float(upv @ tg)
            u_ /= max(1e-6, float(np.linalg.norm(u_)))
            sd = np.cross(tg, u_)
            tc = ts[i] + (ts[i + 1] - ts[i]) * tt
            sc = np.interp(tc, px, prof).astype(F)
            rw, rt = (w / 2) * sc, (t / 2) * sc
            ax = o @ tg                                          # beyond the segment ends (o keeps the overshoot)
            x, y = o @ sd.astype(F), o @ u_
            k0 = np.sqrt((x / rw) ** 2 + (y / rt) ** 2 + (ax / rt) ** 2)
            k1 = np.sqrt((x / rw ** 2) ** 2 + (y / rt ** 2) ** 2 + (ax / rt ** 2) ** 2)
            d = np.where(k1 > 1e-9, k0 * (k0 - 1) / np.maximum(k1, 1e-9), -rt)
            best = np.minimum(best, d)
        return best
    return Shape(f)


# ------------------------------------------------------------------------------------------------ grid builder
class Grid:
    """Dense lattice over a box. fields[g] = float32 distance of group g (valid within ~MARGIN of its surface)."""
    MARGIN = 0.06

    def __init__(self, lo, hi, h):
        self.lo = np.array(lo, F)
        self.h = F(h)
        self.n = tuple(int(math.ceil((hi[i] - lo[i]) / h / 4) * 4) for i in range(3))
        self.fields = {}

    def pts(self, idx):
        return (idx.astype(F) * self.h + self.lo).astype(F)

    def add(self, gid, shape, chunk=300000):
        n = self.n
        c = 4
        nc = tuple(v // c for v in n)
        ci = np.stack(np.meshgrid(*[np.arange(v) for v in nc], indexing="ij"), -1).reshape(-1, 3)
        Pc = (ci.astype(F) * (c * self.h) + self.lo + F(c * self.h * 0.5 - self.h * 0.5))
        dc = np.concatenate([shape(Pc[i:i + chunk]) for i in range(0, len(Pc), chunk)])
        near = (dc < self.MARGIN + c * self.h * 0.9).reshape(nc)
        if not near.any():
            raise SystemExit(f"part of group {gid} is outside the grid")
        fine = np.repeat(np.repeat(np.repeat(near, c, 0), c, 1), c, 2)
        idx = np.argwhere(fine)
        fld = self.fields.get(gid)
        if fld is None:
            fld = self.fields[gid] = np.full(n, 0.25, F)
        for i in range(0, len(idx), chunk):
            ii = idx[i:i + chunk]
            d = shape(self.pts(ii))
            cur = fld[ii[:, 0], ii[:, 1], ii[:, 2]]
            fld[ii[:, 0], ii[:, 1], ii[:, 2]] = np.minimum(cur, d)
        return len(idx)
