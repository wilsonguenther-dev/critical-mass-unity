"""Geometry kit for bases and structures. Authored in BLENDER space (z up, 1 unit = 1 m).

Sim to Blender: blender = (-sim_x, sim_z, height); the FBX export (axis_forward -Z, axis_up Y, bake_space_transform)
gives unity = (-bx, bz, -by) = (sim_x, height, -sim_z) = SimSpace.ToWorld. A Blender yaw phi is Unity yaw -phi.

Every stone block is its own closed, chamfered shell (bmesh bevel, 0.05 u) inside the piece mesh, so the inverted
hull draws a line around each block and the 0.03 u gaps read as seams. Top faces take a colour one band lighter.
"""
import math
import random

import bmesh
from mathutils import Matrix, Vector

from . import pal

GAP = 0.03
CH = 0.05
COURSE = 0.75
CHIP_STATS = {"kept": 0, "rejected": 0}
QUADS = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]


def S2B(sx, sz, h=0.0):
    return Vector((-sx, sz, h))


class Piece:
    def __init__(self, name, team=False, kind="structure"):
        self.name = name
        self.kind = kind            # structure | prop | decal | glow
        self.team = team            # carries team-masked regions
        self.bm = bmesh.new()
        self.col = self.bm.loops.layers.float_color.new("Col")
        self.blocks = 0
        self.marks = {}             # named measured boxes: name -> (min, max)
        self.lights = []            # (Vector local, kind) brazier | crystal | door
        self.pools = []             # (Vector local, radius) warm ground pools

    # ------------------------------------------------------------------ core
    def add(self, t, colfn, M=None, mark=None):
        if M is not None:
            bmesh.ops.transform(t, matrix=M, verts=t.verts[:])
        t.normal_update()
        if mark:
            lo = Vector((min(v.co.x for v in t.verts), min(v.co.y for v in t.verts), min(v.co.z for v in t.verts)))
            hi = Vector((max(v.co.x for v in t.verts), max(v.co.y for v in t.verts), max(v.co.z for v in t.verts)))
            self.marks.setdefault(mark, []).append((tuple(lo), tuple(hi)))
        vmap = {v: self.bm.verts.new(v.co) for v in t.verts}
        for f in t.faces:
            nf = self.bm.faces.new([vmap[v] for v in f.verts])
            nf.smooth = False
            c = colfn(f)
            for lp in nf.loops:
                lp[self.col] = c
        t.free()

    def block(self, corners, side, top=None, bev=CH, segs=1, top_thresh=0.55, mark=None, M=None, chips=None):
        t = bmesh.new()
        vs = [t.verts.new(c) for c in corners]
        for q in QUADS:
            t.faces.new([vs[i] for i in q])
        bmesh.ops.recalc_face_normals(t, faces=t.faces[:])
        if chips:
            t2 = t.copy()
            if _chip(t2, corners, chips):
                t.free()
                t = t2
                CHIP_STATS["kept"] += 1
            else:
                t2.free()
                CHIP_STATS["rejected"] += 1
        if bev > 0:
            bmesh.ops.bevel(t, geom=t.edges[:], offset=bev, offset_type="OFFSET", segments=segs, profile=0.5,
                            affect="EDGES", clamp_overlap=True)
        top = top or side
        self.add(t, lambda f: top if f.normal.z > top_thresh else side, M=M, mark=mark)
        self.blocks += 1

    def box(self, cx, cy, z0, sx, sy, h, side, top=None, rot=0.0, bev=CH, segs=1, mark=None, M=None, jit=None,
            chips=None):
        self.block(box_corners(cx, cy, z0, sx, sy, h, rot, jit), side, top, bev, segs, mark=mark, M=M, chips=chips)

    def lathe(self, prof, n, colfn, cx=0.0, cy=0.0, rot=0.0, M=None, mark=None, bev=0.0, closed=False):
        """prof: [(r, z)] bottom to top; r == 0 at an end makes an apex. Flat-shaded closed solid."""
        t = bmesh.new()
        rings = []
        for r, z in prof:
            if r <= 1e-6:
                rings.append([t.verts.new((cx, cy, z))])
            else:
                rings.append([t.verts.new((cx + r * math.cos(rot + 2 * math.pi * i / n),
                                           cy + r * math.sin(rot + 2 * math.pi * i / n), z)) for i in range(n)])
        pairs = list(zip(rings, rings[1:])) + ([(rings[-1], rings[0])] if closed else [])
        for a, b in pairs:
            for i in range(n):
                j = (i + 1) % n
                if len(a) == 1 and len(b) == 1:
                    continue
                if len(a) == 1:
                    t.faces.new([a[0], b[i], b[j]] if True else None)
                elif len(b) == 1:
                    t.faces.new([a[i], a[j], b[0]])
                else:
                    t.faces.new([a[i], a[j], b[j], b[i]])
        if len(rings[0]) > 1 and not closed:
            t.faces.new(list(reversed(rings[0])))
        if len(rings[-1]) > 1 and not closed:
            t.faces.new(rings[-1])
        bmesh.ops.recalc_face_normals(t, faces=t.faces[:])
        if bev > 0:
            bmesh.ops.bevel(t, geom=t.edges[:], offset=bev, offset_type="OFFSET", segments=1, profile=0.5,
                            affect="EDGES", clamp_overlap=True)
        self.add(t, colfn, M=M, mark=mark)

    def blob(self, c, rx, ry, rz, col, sub=1, seed=0, jitter=0.0, flat_below=None, M=None):
        t = bmesh.new()
        bmesh.ops.create_icosphere(t, subdivisions=sub, radius=1.0)
        rng = random.Random(seed)
        for v in t.verts:
            k = 1.0 + rng.uniform(-jitter, jitter)
            v.co = Vector((c[0] + v.co.x * rx * k, c[1] + v.co.y * ry * k, c[2] + v.co.z * rz * k))
            if flat_below is not None and v.co.z < flat_below:
                v.co.z = flat_below
        self.add(t, (lambda f: col) if not callable(col) else col, M=M)

    def poly(self, pts, col, thick=0.0, M=None, mark=None):
        """Flat polygon (pts in 3D, CCW seen from its front). thick > 0 extrudes it backwards into a slab."""
        t = bmesh.new()
        vs = [t.verts.new(p) for p in pts]
        f = t.faces.new(vs)
        f.normal_update()
        if thick > 0:
            n = f.normal.copy()
            r = bmesh.ops.extrude_face_region(t, geom=[f])
            nv = [e for e in r["geom"] if isinstance(e, bmesh.types.BMVert)]
            bmesh.ops.translate(t, verts=nv, vec=-n * thick)
            bmesh.ops.recalc_face_normals(t, faces=t.faces[:])
        self.add(t, (lambda f: col) if not callable(col) else col, M=M, mark=mark)

    def sheet(self, grid, thick, colfn, M=None):
        """Closed cloth slab from a grid of front points grid[v][u] (v down rows, u across)."""
        t = bmesh.new()
        nv, nu = len(grid), len(grid[0])
        F = [[t.verts.new(grid[j][i]) for i in range(nu)] for j in range(nv)]
        # back offset along the average normal of the sheet
        a = Vector(grid[0][-1]) - Vector(grid[0][0])
        b = Vector(grid[-1][0]) - Vector(grid[0][0])
        n = a.cross(b).normalized()
        B = [[t.verts.new(Vector(grid[j][i]) - n * thick) for i in range(nu)] for j in range(nv)]
        for j in range(nv - 1):
            for i in range(nu - 1):
                t.faces.new([F[j][i], F[j + 1][i], F[j + 1][i + 1], F[j][i + 1]])
                t.faces.new([B[j][i], B[j][i + 1], B[j + 1][i + 1], B[j + 1][i]])
        ring = [(0, i) for i in range(nu)] + [(j, nu - 1) for j in range(1, nv)] + \
               [(nv - 1, i) for i in range(nu - 2, -1, -1)] + [(j, 0) for j in range(nv - 2, 0, -1)]
        for k in range(len(ring)):
            p, q = ring[k], ring[(k + 1) % len(ring)]
            t.faces.new([F[p[0]][p[1]], F[q[0]][q[1]], B[q[0]][q[1]], B[p[0]][p[1]]])
        bmesh.ops.recalc_face_normals(t, faces=t.faces[:])
        self.add(t, colfn, M=M)

    # ------------------------------------------------------------------ finish
    def to_object(self, bpy):
        me = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(me)
        self.bm.free()
        ca = me.color_attributes.get("Col")
        if ca is not None:
            me.color_attributes.active_color = ca
        ob = bpy.data.objects.new(self.name, me)
        ob["kind"] = self.kind
        ob["team"] = int(self.team)
        ob["blocks"] = self.blocks
        return ob


# ---------------------------------------------------------------------- helpers
def box_corners(cx, cy, z0, sx, sy, h, rot=0.0, jit=None):
    dx, dy = sx / 2, sy / 2
    c, s = math.cos(rot), math.sin(rot)
    base = [(cx + x * c - y * s, cy + x * s + y * c) for x, y in ((-dx, -dy), (dx, -dy), (dx, dy), (-dx, dy))]
    tops = [h] * 4 if jit is None else [h + j for j in jit]
    return [Vector((x, y, z0)) for x, y in base] + [Vector((x, y, z0 + tz)) for (x, y), tz in zip(base, tops)]


def _chip(t, corners, chips):
    """Knock corners off a block: each chip = (corner index, size). A plane through the corner region, normal pointing
    out of that corner (diagonal, tilted up for top corners), cuts the corner away and the hole is capped flat."""
    cen = sum((Vector(c) for c in corners), Vector()) / len(corners)
    for idx, size in chips:
        c = Vector(corners[idx])
        d = c - cen
        if d.length < 1e-6:
            continue
        n = Vector((d.x, d.y, d.z * 1.6)).normalized()
        co = c - n * size
        geom = t.verts[:] + t.edges[:] + t.faces[:]
        try:
            bmesh.ops.bisect_plane(t, geom=geom, dist=1e-5, plane_co=co, plane_no=n, clear_outer=True)
            edges = [e for e in t.edges if e.is_boundary]
            if edges:
                bmesh.ops.holes_fill(t, edges=edges, sides=0)
        except Exception:
            return False
    bmesh.ops.recalc_face_normals(t, faces=t.faces[:])
    # keep the chip only if the shell is still closed, convex-oriented and has no slivers
    if any(not e.is_manifold for e in t.edges):
        return False
    for f in t.faces:
        if f.calc_area() < 1e-5 or (f.calc_center_median() - cen).dot(f.normal) <= 0.0:
            return False
    return True


def rand_chips(rng, p1=0.5, p2=0.18, lo=0.09, hi=0.2, top_only=True):
    """0, 1 or 2 chipped corners (indices into the 8 hexahedron corners; 4..7 = top)."""
    r = rng.random()
    k = 2 if r < p2 else (1 if r < p1 else 0)
    pool = [4, 5, 6, 7] if top_only else list(range(8))
    rng.shuffle(pool)
    return [(i, rng.uniform(lo, hi)) for i in pool[:k]]


def solid(c):
    return lambda f: c


def topside(side, top, thresh=0.55):
    return lambda f: top if f.normal.z > thresh else side


def stone_side(rng, warm=False):
    """(side, top) colours for one block. v2: sides vary in value (a dark block now and then, as the bible's chunky
    stone), tops are a distinctly lighter warm tan so every block reads lit top / mid side / dark shadow side."""
    keys = ("warm_a", "warm_b", "warm_c", "warm_d") if warm else ("body_a", "body_b", "body_c", "body_d")
    k = rng.choice(keys)
    r = rng.random()
    m = rng.uniform(0.78, 0.86) if r < 0.14 else rng.uniform(0.93, 1.07)
    tk = rng.choice(("top_a", "top_b", "top_c")) if warm else rng.choice(("top_cool_a", "top_cool_b", "top_a"))
    side = pal.vc(k, m)
    top = pal.lerp_vc(pal.vc(k, m * pal.TOP_LIFT * 1.08), pal.vc(tk, rng.uniform(0.95, 1.04)), 0.6)
    return side, top


class Path:
    """Dense 2D polyline in Blender XY with arc length, tangent and a consistent OUTWARD normal."""

    def __init__(self, pts, outward_sign=1.0):
        self.p = [Vector((x, y)) for x, y in pts]
        self.s = [0.0]
        for a, b in zip(self.p, self.p[1:]):
            self.s.append(self.s[-1] + (b - a).length)
        self.L = self.s[-1]
        self.sign = outward_sign

    def _seg(self, s):
        s = max(0.0, min(self.L, s))
        lo, hi = 0, len(self.s) - 1
        while hi - lo > 1:
            m = (lo + hi) // 2
            if self.s[m] <= s:
                lo = m
            else:
                hi = m
        d = self.s[hi] - self.s[lo]
        t = 0.0 if d < 1e-9 else (s - self.s[lo]) / d
        return lo, hi, t

    def pos(self, s):
        lo, hi, t = self._seg(s)
        return self.p[lo].lerp(self.p[hi], t)

    def tan(self, s):
        e = 0.05
        d = self.pos(min(self.L, s + e)) - self.pos(max(0.0, s - e))
        return d.normalized()

    def nrm(self, s):
        t = self.tan(s)
        return Vector((t.y, -t.x)) * self.sign

    def at(self, s, o, z):
        p = self.pos(s) + self.nrm(s) * o
        return Vector((p.x, p.y, z))


def arc_pts(cx, cy, r, a0, a1, step=0.08):
    n = max(2, int(abs(a1 - a0) * r / step))
    return [(cx + r * math.cos(a0 + (a1 - a0) * i / n), cy + r * math.sin(a0 + (a1 - a0) * i / n)) for i in range(n + 1)]


def course_breaks(L, rng, half_first):
    """Block boundaries along a run: lengths 0.9..1.6 (running bond: odd courses start with a half block)."""
    xs = [0.0]
    first = rng.uniform(0.45, 0.8) if half_first else rng.uniform(0.9, 1.6)
    x = first
    while x < L - 0.001:
        xs.append(min(x, L))
        x += rng.uniform(0.9, 1.6)
    if xs[-1] < L:
        xs.append(L)
    if len(xs) > 2 and xs[-1] - xs[-2] < 0.5:
        xs.pop(-2)
    return xs


def hexa_on_path(path, s0, s1, o0, o1, z0, z1, jit=0.0, rng=None):
    js = [rng.uniform(-jit, jit) if rng else 0.0 for _ in range(4)]
    return [path.at(s0, o0, z0), path.at(s1, o0, z0), path.at(s1, o1, z0), path.at(s0, o1, z0),
            path.at(s0, o0, z1 + js[0]), path.at(s1, o0, z1 + js[1]), path.at(s1, o1, z1 + js[2]),
            path.at(s0, o1, z1 + js[3])]


def moss_strip(piece, rng, path, s, o_face, z, length, outward):
    """Moss growing in a horizontal joint: a thin flat lens along the course line, proud of the face by 0.04 u."""
    n = path.nrm(s) * outward
    p = path.at(s, o_face, z)
    t = path.tan(s)
    ang = math.atan2(t.y, t.x)
    M = Matrix.Translation(Vector((p.x - n.x * 0.03, p.y - n.y * 0.03, z))) @ Matrix.Rotation(ang, 4, "Z")
    piece.blob((0, 0, 0), length / 2, 0.05, 0.06, lambda f: pal.vc("moss") if f.normal.z > 0.2 else pal.vc("moss_d"),
               sub=1, seed=rng.randint(0, 9999), jitter=0.15, M=M)


def moss_drape(piece, rng, path, s, o_face, z, outward, size=0.26):
    """Moss cushion on a top edge with short strands hanging down the face (the bible's mossy block tops). Kept
    within 0.05 u of the face so it never reaches further into a walkable cell than the stone does."""
    t = path.tan(s)
    ang = math.atan2(t.y, t.x)
    p = path.at(s, o_face - 0.16 * outward, z)
    M = Matrix.Translation(Vector((p.x, p.y, z))) @ Matrix.Rotation(ang, 4, "Z")
    piece.blob((0, 0, 0.0), size * rng.uniform(0.9, 1.3), 0.2, size * 0.42,
               lambda f: pal.vc("grass") if f.normal.z > 0.45 else pal.vc("moss_d"), sub=1,
               seed=rng.randint(0, 9999), jitter=0.2, M=M)
    for k in range(rng.randint(1, 3)):
        sk = s + rng.uniform(-size, size) * 0.6
        q = path.at(sk, o_face + 0.01 * outward, z)
        hz = size * rng.uniform(0.5, 0.9)
        Mq = Matrix.Translation(Vector((q.x, q.y, z - hz * 0.8))) @ Matrix.Rotation(ang, 4, "Z")
        piece.blob((0, 0, 0), size * rng.uniform(0.3, 0.5), 0.045, hz,
                   lambda f: pal.vc("moss") if f.normal.z > 0.3 else pal.vc("moss_d"), sub=1,
                   seed=rng.randint(0, 9999), jitter=0.12, M=Mq)


def grass_tuft(piece, rng, x, y, z, h=0.24, n=4):
    """Spiky grass clump (blades = thin 3-sided spikes), at most h tall."""
    for i in range(n):
        a = rng.uniform(0, 2 * math.pi)
        d = rng.uniform(0.0, 0.12)
        bx, by = x + d * math.cos(a), y + d * math.sin(a)
        hh = h * rng.uniform(0.6, 1.0)
        M = Matrix.Translation((bx, by, z)) @ Matrix.Rotation(a, 4, "Z") @ Matrix.Rotation(rng.uniform(0.15, 0.45), 4, "Y")
        c = pal.vc("grass") if i % 2 else pal.vc("moss")
        piece.lathe([(0.085, 0.0), (0.0, hh)], 4, lambda f, c=c: c, M=M)


def moss(piece, rng, x, y, z, size=0.25):
    piece.blob((x, y, z), size * rng.uniform(0.8, 1.3), size * rng.uniform(0.6, 1.0), size * 0.32,
               lambda f: pal.vc("moss") if f.normal.z > 0.3 else pal.vc("moss_d"), sub=1,
               seed=rng.randint(0, 9999), jitter=0.18, flat_below=z - 0.02)


# ---------------------------------------------------------------------- walls
def wall_run(piece, path, rng, thick=1.0, courses=3, z0=0.0, merlons=True, walkway=True, team_caps=False,
             warm=False, moss_p=0.12, damage="intact", top_courses_h=None, merlon_h=0.9, sink=0.15):
    """Ring wall along `path` (outward normal = merlon side). Body = courses x 0.75 u, merlons 0.9 x 0.9 x 0.6 every
    1.4 u (sunk `sink` into the top course so body + visible merlon = 3.0 u for 3 courses), plank walkway 0.8 u on the
    inner side. damage: intact | cracked | destroyed (rubble no taller than 0.3 u)."""
    L = path.L
    h0 = thick / 2
    top = z0 + courses * COURSE
    blocks = []
    for c in range(courses):
        xs = course_breaks(L, rng, c % 2 == 1)
        for a, b in zip(xs, xs[1:]):
            blocks.append((c, a, b))
    if damage == "destroyed":
        _rubble_run(piece, path, rng, thick, z0, warm)
        return {"top": z0 + 0.3, "L": L}
    split = set()
    if damage == "cracked":
        cand = [i for i, bl in enumerate(blocks) if bl[0] >= 1 and bl[2] - bl[1] > 0.8]
        rng.shuffle(cand)
        split = set(cand[:4])
    # v2 character: per block a variant (normal / split into two stacked stones / shifted in or out / missing face
    # stone), chipped corners, shorter tops; moss in joints and draped over top edges
    for i, (c, a, b) in enumerate(blocks):
        side, topc = stone_side(rng, warm)
        zb, zt = z0 + c * COURSE + GAP / 2, z0 + (c + 1) * COURSE - GAP / 2
        io = rng.uniform(-0.025, 0.025)
        if i in split:
            m = (a + b) / 2 + rng.uniform(-0.15, 0.15)
            d = 0.08
            for (sa, sb), dz in (((a + GAP / 2, m - 0.03), -0.02), ((m + 0.03, b - GAP / 2), -0.05)):
                cs = hexa_on_path(path, sa, sb, -h0 + io, h0 + io + 0.04, zb + dz, zt + dz)
                # slant the crack: shift the top corners at the cut side
                if sa > a + 0.1:
                    cs[0] = path.at(sa + d, -h0 + io, zb + dz); cs[3] = path.at(sa + d, h0 + io + 0.04, zb + dz)
                else:
                    cs[5] = path.at(sb - d, -h0 + io, zt + dz); cs[6] = path.at(sb - d, h0 + io + 0.04, zt + dz)
                piece.block(cs, side, topc, chips=rand_chips(rng, 0.6, 0.3, 0.1, 0.2))
            continue
        top_course = c == courses - 1
        r = rng.random()
        lo_o, hi_o = -h0 + io, h0 + io
        drop = 0.0 if (top_course or c == 0) else rng.choice((0.0, 0.0, 0.0, -0.04, -0.07))
        if r < 0.13 and b - a > 1.0 and not top_course:
            # two stacked stones in one course slot (0.36 u each), the upper one shorter and set back
            zm = (zb + zt) / 2
            cut = rng.uniform(0.25, 0.45) * (b - a)
            piece.block(hexa_on_path(path, a + GAP / 2, b - GAP / 2, lo_o, hi_o, zb, zm - GAP / 2, 0.02, rng), side,
                        topc, chips=rand_chips(rng, 0.4, 0.1, 0.07, 0.14))
            s2, t2 = stone_side(rng, warm)
            if rng.random() < 0.5:
                sa, sb = a + GAP / 2, b - cut - GAP / 2
            else:
                sa, sb = a + cut + GAP / 2, b - GAP / 2
            piece.block(hexa_on_path(path, sa, sb, lo_o + 0.03, hi_o - 0.03, zm + GAP / 2, zt + drop, 0.02, rng), s2,
                        t2, chips=rand_chips(rng, 0.5, 0.15, 0.08, 0.16))
            s3, t3 = stone_side(rng, warm)
            oa, ob = (b - cut + GAP / 2, b - GAP / 2) if sa == a + GAP / 2 else (a + GAP / 2, a + cut - GAP / 2)
            if ob - oa > 0.25:
                piece.block(hexa_on_path(path, oa, ob, lo_o + 0.05, hi_o - 0.05, zm + GAP / 2, zt - 0.05, 0.02, rng),
                            s3, t3, chips=rand_chips(rng, 0.5, 0.2, 0.06, 0.12))
            continue
        if 0.13 <= r < 0.21 and not top_course and c >= 0:
            # missing face stone: the block is set deep into the wall on one face (dark socket) -- a gap in the face
            if rng.random() < 0.5:
                lo_o += 0.24
            else:
                hi_o -= 0.24
            side = pal.vc("body_a", 0.8)
            topc = pal.vc("body_a", 0.92)
        elif 0.21 <= r < 0.36:
            # shifted stone: pushed out of line on one face and in on the other
            sh = rng.choice((-1, 1)) * rng.uniform(0.03, 0.045)
            lo_o += sh
            hi_o += sh
        piece.block(hexa_on_path(path, a + GAP / 2, b - GAP / 2, lo_o, hi_o, zb, zt + drop, 0.035, rng), side, topc,
                    mark="course" if c == 0 and i < 3 else None, chips=rand_chips(rng, 0.55, 0.22, 0.12, 0.26),
                    bev=0.075, segs=2)
        # moss: in the joint above this block (outer or inner face), draped on top-course edges
        if c < courses - 1 and rng.random() < moss_p * 0.9:
            sm = rng.uniform(a + 0.25, b - 0.25)
            face = rng.choice((-1, 1))
            moss_strip(piece, rng, path, sm, face * (h0 + 0.02), zt + GAP / 2, rng.uniform(0.35, 0.7), face)
        if top_course and rng.random() < moss_p * 1.6:
            sm = rng.uniform(a + 0.2, b - 0.2)
            moss_drape(piece, rng, path, sm, hi_o, zt, 1, rng.uniform(0.26, 0.4))
    if not merlons and walkway is False:
        # low walls (shrine yard): mossy tops
        s = rng.uniform(0.3, 0.8)
        while s < L - 0.3:
            if rng.random() < moss_p:
                p = path.at(s, rng.uniform(-0.2, 0.2), top)
                moss(piece, rng, p.x, p.y, top - 0.02, 0.22)
            s += rng.uniform(0.8, 1.4)
    s = rng.uniform(0.2, 0.9)
    while s < L - 0.2:
        for o in (-h0 - 0.08, h0 + 0.08):
            if rng.random() < moss_p * 2.2:
                p = path.at(s, o, z0)
                moss(piece, rng, p.x, p.y, z0 + 0.02, 0.24)
                if rng.random() < 0.6:
                    q = path.at(s + rng.uniform(-0.3, 0.3), o * 1.08, z0)
                    grass_tuft(piece, rng, q.x, q.y, z0, rng.uniform(0.18, 0.28))
        s += rng.uniform(0.6, 1.3)
    # walkway (inner side of the wall top): planks 0.25 u across the wall, beams every 2 u (flush beam ends)
    if walkway:
        s = 0.06
        k = 0
        while s + 0.22 < L - 0.04:
            col = pal.vc("wood" if k % 2 == 0 else "wood_b", rng.uniform(0.95, 1.05))
            cs = hexa_on_path(path, s, s + 0.22, -h0 - 0.0, -h0 + 0.8, top - 0.01, top + 0.06)
            piece.block(cs, col, col, bev=0.015)
            s += 0.25
            k += 1
        s = 1.0
        while s < L - 0.3:
            cs = hexa_on_path(path, s - 0.09, s + 0.09, -h0 - 0.02, -h0 + 0.12, top - 0.24, top - 0.04)
            piece.block(cs, pal.vc("wood_d"), pal.vc("wood_d"), bev=0.02)
            s += 2.0
    # merlons
    fallen = []
    if merlons:
        cnt = int((L - 0.9) // 1.4) + 1
        start = (L - (cnt - 1) * 1.4) / 2
        drop = set()
        if damage == "cracked" and cnt >= 3:
            drop = {1, cnt - 2} if cnt > 3 else {0, 2}
        for k in range(cnt):
            s = start + k * 1.4
            if k in drop:
                fallen.append(s)
                continue
            mz0 = top - sink
            capped = team_caps and k % 2 == 1
            body_top = mz0 + merlon_h - (0.14 if capped else 0.0)
            cs = hexa_on_path(path, s - 0.45, s + 0.45, h0 - 0.6, h0, mz0, body_top)
            sc = rng.uniform(0.97, 1.03)
            if not capped and rng.random() < 0.18:      # a merlon knocked slightly out of line
                M2 = Matrix.Translation(path.at(s, h0 - 0.3, 0)) @ Matrix.Rotation(rng.uniform(-0.12, 0.12), 4, "Z") \
                    @ Matrix.Translation(-path.at(s, h0 - 0.3, 0))
                cs = [M2 @ v for v in cs]
            piece.block(cs, pal.vc("cap_side", sc), pal.vc("cap", sc), bev=0.1 if not capped else 0.06, segs=2,
                        mark="merlon" if not capped else "merlon_body",
                        chips=None if capped else rand_chips(rng, 0.45, 0.1, 0.08, 0.15))
            if capped:
                cs = hexa_on_path(path, s - 0.48, s + 0.48, h0 - 0.63, h0 + 0.03, body_top + 0.01, mz0 + merlon_h)
                piece.block(cs, pal.vc("team"), pal.vc("team"), bev=0.04, segs=2, mark="merlon_cap")
            if not capped and rng.random() < moss_p * 1.3:
                p = path.at(s + rng.uniform(-0.2, 0.2), h0 - 0.3, mz0 + merlon_h)
                moss(piece, rng, p.x, p.y, p.z - 0.03, 0.2)
    for j, s in enumerate(fallen):
        if j == 0:   # tipped over onto the walkway
            c = path.at(s, -h0 + 0.35, top + 0.06)
            t = path.tan(s)
            ang = math.atan2(t.y, t.x)
            M = Matrix.Translation(c) @ Matrix.Rotation(ang, 4, "Z") @ Matrix.Rotation(math.radians(80), 4, "X")
            piece.box(0, 0, -0.3, 0.9, 0.6, 0.9, pal.vc("cap_side"), pal.vc("cap"), bev=0.1, segs=2, M=M)
        else:        # fallen in front of the wall, half sunk (top at 0.3 u)
            c = path.at(s + 0.3, h0 + 0.75, z0)
            t = path.tan(s)
            ang = math.atan2(t.y, t.x) + 0.5
            piece.box(c.x, c.y, z0 - 0.3, 0.9, 0.9, 0.6, pal.vc("cap_side"), pal.vc("cap"), rot=ang, bev=0.1, segs=2)
    if damage == "cracked":
        for _ in range(5):
            s = rng.uniform(0.3, L - 0.3)
            p = path.at(s, h0 + rng.uniform(0.2, 0.7), z0)
            piece.box(p.x, p.y, z0 - 0.02, rng.uniform(0.15, 0.3), rng.uniform(0.15, 0.3), rng.uniform(0.1, 0.17),
                      *stone_side(rng, warm), rot=rng.uniform(0, 3), bev=0.03)
    return {"top": top + merlon_h - sink if merlons else top, "L": L}


def _rubble_run(piece, path, rng, thick, z0, warm):
    """Destroyed: low broken stubs of the bottom course plus scattered chunks, all at most 0.3 u above z0."""
    xs = course_breaks(path.L, rng, False)
    h0 = thick / 2
    for a, b in zip(xs, xs[1:]):
        if rng.random() < 0.25:
            continue
        side, topc = stone_side(rng, warm)
        hgt = rng.uniform(0.12, 0.26)
        piece.block(hexa_on_path(path, a + 0.05, b - 0.05, -h0 + 0.05, h0 - 0.05, z0, z0 + hgt, 0.03, rng), side, topc)
    for _ in range(int(path.L * 3)):
        s = rng.uniform(0, path.L)
        p = path.at(s, rng.uniform(-h0 - 0.4, h0 + 0.5), z0)
        side, topc = stone_side(rng, warm)
        if rng.random() < 0.3:
            side, topc = pal.vc("cap_side"), pal.vc("cap")
        hgt = rng.uniform(0.08, 0.22)
        piece.box(p.x, p.y, z0 - 0.04, rng.uniform(0.2, 0.5), rng.uniform(0.2, 0.45), hgt + 0.04, side, topc,
                  rot=rng.uniform(0, 3.1), bev=0.04, jit=[rng.uniform(-0.04, 0.04) for _ in range(4)])
    for _ in range(int(path.L)):
        s = rng.uniform(0, path.L)
        p = path.at(s, rng.uniform(-0.3, 0.3), z0 + 0.15)
        moss(piece, rng, p.x, p.y, z0 + 0.12, 0.2)


# ---------------------------------------------------------------------- drum courses (towers)
def drum(piece, rng, r, courses, z0=0.0, thick=0.85, door=None, warm=False, merlon_ring=None):
    """Round tower body of curved block courses. door = (angle, width, height) cut out of the low courses."""
    core = r - thick + 0.02
    piece.lathe([(core, z0), (core, z0 + courses * COURSE - 0.05)], 16, solid(pal.vc("body_a", 0.8)))
    for c in range(courses):
        zb, zt = z0 + c * COURSE + GAP / 2, z0 + (c + 1) * COURSE - GAP / 2
        circ = 2 * math.pi * r
        if door and zb < door[2]:
            half = math.asin(min(0.99, (door[1] / 2) / r))
            a0, span = door[0] + half, 2 * math.pi - 2 * half
        else:
            a0, span = rng.uniform(0, 2 * math.pi) + (0.3 if c % 2 else 0), 2 * math.pi
        L = span * r
        xs = course_breaks(L, rng, False) if span < 2 * math.pi else _even_breaks(L, rng)
        for a, b in zip(xs, xs[1:]):
            t0, t1 = a0 + (a + GAP / 2) / r, a0 + (b - GAP / 2) / r
            ri, ro = r - thick, r + rng.uniform(-0.015, 0.015)
            cs = [Vector((ri * math.cos(t0), ri * math.sin(t0), zb)), Vector((ri * math.cos(t1), ri * math.sin(t1), zb)),
                  Vector((ro * math.cos(t1), ro * math.sin(t1), zb)), Vector((ro * math.cos(t0), ro * math.sin(t0), zb))]
            dz = rng.choice((0.0, 0.0, 0.0, -0.05)) if c < courses - 1 else 0.0
            cs += [Vector((v.x, v.y, zt + dz)) for v in cs]
            if rng.random() < 0.2:
                k = rng.uniform(1.012, 1.03)
                cs = [Vector((v.x * k, v.y * k, v.z)) if j in (2, 3, 6, 7) else v for j, v in enumerate(cs)]
            piece.block(cs, *stone_side(rng, warm), chips=rand_chips(rng, 0.45, 0.15, 0.08, 0.18))
            if c < courses - 1 and rng.random() < 0.12:
                tm = (t0 + t1) / 2
                kit_moss_ring(piece, rng, r, tm, zt + GAP / 2)
    top = z0 + courses * COURSE
    if door:
        _arch_door(piece, rng, r, door, z0)
    return top


def kit_moss_ring(piece, rng, r, ang, z):
    M = Matrix.Translation(Vector((r * math.cos(ang), r * math.sin(ang), z))) @ Matrix.Rotation(ang + math.pi / 2, 4, "Z")
    piece.blob((0, 0, 0), rng.uniform(0.25, 0.4), 0.07, 0.06, lambda f: pal.vc("moss") if f.normal.z > 0.2 else
               pal.vc("moss_d"), sub=1, seed=rng.randint(0, 9999), jitter=0.2, M=M)


def _even_breaks(L, rng):
    n = max(3, int(round(L / 1.2)))
    xs = [L * i / n + (rng.uniform(-0.12, 0.12) if 0 < i < n else 0) for i in range(n + 1)]
    return xs


def _arch_door(piece, rng, r, door, z0):
    ang, w, h = door
    spring = h - w / 2
    d = Vector((math.cos(ang), math.sin(ang), 0))
    tng = Vector((-d.y, d.x, 0))
    face = d * (r + 0.06)
    ri, ro, depth = w / 2, w / 2 + 0.34, 0.95
    n = 5
    for i in range(n):
        a0, a1 = math.pi * i / n + 0.012, math.pi * (i + 1) / n - 0.012
        pts = []
        for rr, aa in ((ri, a0), (ri, a1), (ro, a1), (ro, a0)):
            pts.append(face + tng * (rr * math.cos(aa)) + Vector((0, 0, z0 + spring + rr * math.sin(aa))))
        cs = [p - d * depth for p in pts] + pts
        # order: bottom ring = back face, top ring = front face (hexahedron is valid either way)
        piece.block([cs[0], cs[1], cs[2], cs[3], cs[4], cs[5], cs[6], cs[7]], pal.vc("cap_side"), pal.vc("cap"),
                    top_thresh=0.8)
    # spandrel fill above the arch up to the next course line
    ztop = z0 + math.ceil((h + 0.35) / COURSE) * COURSE
    c = face - d * 0.5
    piece.box(c.x, c.y, z0 + spring + 0.62, w + 0.75, 0.9, ztop - (z0 + spring + 0.62) - GAP / 2,
              *stone_side(rng), rot=ang + math.pi / 2)
    # jambs: light threshold stones either side
    for sgn in (-1, 1):
        p = face + tng * (sgn * (w / 2 + 0.17)) - d * 0.45
        piece.box(p.x, p.y, z0, 0.34, 0.95, spring - GAP, pal.vc("cap_side"), pal.vc("cap"), rot=ang + math.pi / 2)
    # self-lit doorway: Warm Cream at the floor to #FFC27A at the crown, recessed
    g = face - d * 0.4
    pts = []
    for i in range(13):
        a = math.pi * i / 12
        pts.append(g + tng * (ri * math.cos(a)) + Vector((0, 0, z0 + spring + ri * math.sin(a))))
    pts = [g + tng * ri + Vector((0, 0, z0 + 0.01))] + pts + [g - tng * ri + Vector((0, 0, z0 + 0.01))]
    zt = z0 + h

    def grad(f):
        zc = sum(v.co.z for v in f.verts) / len(f.verts)
        return pal.lerp_vc(pal.vc("door_lo"), pal.vc("door_hi"), max(0, min(1, (zc - z0) / (zt - z0))))
    t = __import__("bmesh").new()
    vs = [t.verts.new(p) for p in pts]
    cen = t.verts.new(g + Vector((0, 0, z0 + spring * 0.55)))
    for i in range(len(vs) - 1):
        t.faces.new([cen, vs[i], vs[i + 1]])
    t.faces.new([cen, vs[-1], vs[0]])
    t.normal_update()
    vcol = {}
    for v in t.verts:
        vcol[v] = pal.lerp_vc(pal.vc("door_lo"), pal.vc("door_hi"), max(0, min(1, (v.co.z - z0) / (zt - z0))))
    vmap = {v: piece.bm.verts.new(v.co) for v in t.verts}
    for f in t.faces:
        nf = piece.bm.faces.new([vmap[v] for v in f.verts])
        for lp, ov in zip(nf.loops, f.verts):
            lp[piece.col] = vcol[ov]
    t.free()
    piece.lights.append((face + Vector((0, 0, z0 + 1.0)), "door"))


def merlon_ring(piece, rng, r, z, n=8, team_caps=False, h=0.9, depth=0.6, width=0.9):
    for k in range(n):
        a = 2 * math.pi * k / n + math.pi / n
        c = Vector((math.cos(a), math.sin(a), 0)) * (r - depth / 2)
        capped = team_caps and k % 2 == 1
        bh = h - (0.14 if capped else 0)
        piece.box(c.x, c.y, z, depth, width, bh, pal.vc("cap_side"), pal.vc("cap"), rot=a, bev=0.1, segs=2,
                  mark="merlon")
        if capped:
            piece.box(c.x, c.y, z + bh + 0.01, depth + 0.06, width + 0.06, 0.13, pal.vc("team"), pal.vc("team"),
                      rot=a, bev=0.04, segs=2)


def corbel_ring(piece, rng, r, z, h=0.6, out=0.15):
    n = max(8, int(2 * math.pi * r / 0.9))
    for k in range(n):
        t0, t1 = 2 * math.pi * k / n + 0.008, 2 * math.pi * (k + 1) / n - 0.008
        ri, ro = r - 0.7, r + out
        cs = [Vector((ri * math.cos(t0), ri * math.sin(t0), z)), Vector((ri * math.cos(t1), ri * math.sin(t1), z)),
              Vector((ro * math.cos(t1), ro * math.sin(t1), z)), Vector((ro * math.cos(t0), ro * math.sin(t0), z))]
        cs += [Vector((v.x, v.y, z + h - GAP)) for v in cs]
        piece.block(cs, pal.vc("cap_side"), pal.vc("cap"))


def cone_roof(piece, r_eave, z0, height, n=20, rows=None, cx=0.0, cy=0.0, mark="roof"):
    """Team-coloured cone roof built as stepped tile rows (0.25 u each) with a darker underside line per row."""
    rows = rows or max(3, int(round(height / 0.25)))
    dz = height / rows
    for k in range(rows):
        za, zb = z0 + k * dz, z0 + (k + 1) * dz
        ra = r_eave * (1 - k / rows) + 0.07
        rb = r_eave * (1 - (k + 1) / rows) + (0.0 if k == rows - 1 else 0.02)
        prof = [(max(ra - 0.12, 0.05), za), (ra, za), (max(rb, 0.0), zb)]
        if rb <= 0.02:
            prof = [(max(ra - 0.12, 0.05), za), (ra, za), (0.0, zb)]

        def col(f, za=za):
            if f.normal.z < -0.2:
                return pal.vc("team_dark")
            return pal.vc("team")
        piece.lathe(prof, n, col, cx=cx, cy=cy, rot=(k % 2) * math.pi / n, mark=mark)


def brazier_geo(piece, x, y, z, rng, scale=1.0):
    """1.0 u: stone post + brass bowl + coals + flame. Records a light and a 2.0 u pool."""
    s = scale
    piece.box(x, y, z, 0.42 * s, 0.42 * s, 0.14 * s, pal.vc("body_c"), pal.vc("body_c", pal.TOP_LIFT), bev=0.03)
    piece.box(x, y, z + 0.14 * s, 0.3 * s, 0.3 * s, 0.5 * s, pal.vc("body_b"), pal.vc("body_b", pal.TOP_LIFT),
              bev=0.03, rot=0.0)
    piece.lathe([(0.08 * s, z + 0.64 * s), (0.22 * s, z + 0.70 * s), (0.30 * s, z + 0.86 * s), (0.32 * s, z + 0.92 * s),
                 (0.27 * s, z + 0.92 * s)], 8, topside(pal.vc("brass_d"), pal.vc("brass"), 0.3), cx=x, cy=y)
    piece.lathe([(0.27 * s, z + 0.86 * s), (0.2 * s, z + 0.93 * s), (0.0, z + 0.96 * s)], 7, solid(pal.vc("coal")),
                cx=x, cy=y)
    piece.lathe([(0.2 * s, z + 0.9 * s), (0.17 * s, z + 1.05 * s), (0.0, z + 1.38 * s)], 6, solid(pal.vc("flame_out")),
                cx=x, cy=y, rot=0.3)
    piece.lathe([(0.11 * s, z + 0.92 * s), (0.09 * s, z + 1.02 * s), (0.0, z + 1.22 * s)], 5, solid(pal.vc("flame")),
                cx=x + 0.02, cy=y - 0.03)
    piece.lights.append((Vector((x, y, z + 1.1 * s)), "brazier"))
    piece.pools.append((Vector((x, y, z)), 2.0))


def crystal_cluster(piece, x, y, z, rng, height=0.9, n=4, spread=0.25, base=True, lamp=True):
    if base:
        piece.lathe([(0.34, z), (0.38, z + 0.06), (0.3, z + 0.16), (0.0, z + 0.17)], 7,
                    topside(pal.vc("warm_b"), pal.vc("warm_c", pal.TOP_LIFT)), cx=x, cy=y, bev=0.0)
        z += 0.12
    for i in range(n):
        h = height * (1.0 if i == 0 else rng.uniform(0.45, 0.75))
        r = h * 0.17
        ang = rng.uniform(0, 2 * math.pi)
        d = 0 if i == 0 else spread * rng.uniform(0.6, 1.0)
        cx, cy = x + d * math.cos(ang), y + d * math.sin(ang)
        tilt = 0 if i == 0 else rng.uniform(0.25, 0.5)
        M = Matrix.Translation((cx, cy, z)) @ Matrix.Rotation(ang, 4, "Z") @ Matrix.Rotation(tilt, 4, "Y")

        def col(f):
            return pal.vc("team_glow") if f.normal.x + f.normal.z * 0.6 > 0.15 else pal.vc("team_dark")
        piece.lathe([(r * 0.6, -0.05), (r, h * 0.2), (r * 0.9, h * 0.72), (0.0, h)], 6, col, M=M)
    if lamp:
        piece.lights.append((Vector((x, y, z + height * 0.5)), "crystal"))
