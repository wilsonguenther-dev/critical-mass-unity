"""The bases and structures pieces (art direction section 4). Each builder returns a kit.Piece in local space."""
import math
import random

from mathutils import Matrix, Vector

from . import kit, pal
from .kit import COURSE, GAP, Piece, Path, arc_pts, solid, topside

HILL_R = 10.5
SHRINE_R = 4.5


RHO = {}    # optional radius-by-angle functions fitted to the Sim cell chains: RHO["hill"], RHO["shrine"]


def sim_arc(r, a0, a1, step=0.08, rho=None):
    """Arc of the sim angle convention (0 = east, 90 = south) around a local origin, in Blender XY.
    rho(angle_deg) -> radius overrides r (follows the Sim's rasterised ring)."""
    n = max(2, int(abs(math.radians(a1 - a0)) * r / step))
    out = []
    for i in range(n + 1):
        ad = a0 + (a1 - a0) * i / n
        a = math.radians(ad)
        rr = rho(ad) if rho else r
        out.append((-rr * math.cos(a), rr * math.sin(a)))
    return out


def outward_path(pts, centre=(0.0, 0.0)):
    p = Path(pts, 1.0)
    m = p.L / 2
    if (p.pos(m) - Vector(centre)).dot(p.nrm(m)) < 0:
        p.sign = -1.0
    return p


# ------------------------------------------------------------------------------ walls
def wall_segment(state, seed=11, team_caps=True):
    pc = Piece(f"cm_wall_segment_{state}", team=team_caps)
    r = 14.0
    half = math.degrees(2.1 / r)
    pts = sim_arc(r, 90 - half, 90 + half)
    pts = [(x, y - r) for x, y in pts]          # centre the segment on the origin
    p = outward_path(pts, (0, -r))
    kit.wall_run(pc, p, random.Random(seed), team_caps=team_caps, damage=state, warm=True, moss_p=0.35)
    return pc


def hill_arc(name, a0, a1, seed, warm=False, state="intact", team_caps=True):
    pc = Piece(name, team=team_caps)
    p = outward_path(sim_arc(HILL_R, a0, a1, rho=RHO.get("hill")))
    kit.wall_run(pc, p, random.Random(seed), thick=0.86, team_caps=team_caps, damage=state, warm=warm,
                 moss_p=0.35 if warm else 0.25)
    return pc


def shrine_arc(name, ranges, seed, state="intact", team_caps=True):
    pc = Piece(name, team=team_caps)
    rng = random.Random(seed)
    for a0, a1 in ranges:
        p = outward_path(sim_arc(SHRINE_R, a0, a1, rho=RHO.get("shrine")))
        kit.wall_run(pc, p, rng, thick=0.8, courses=1, merlons=True, walkway=False, team_caps=team_caps,
                     damage=state, warm=True, moss_p=0.4, merlon_h=0.9, sink=0.15)
    return pc


# ------------------------------------------------------------------------------ gate post + banner cloth
def banner_cloth(pc, top_c, width=1.0, length=1.8, tilt=15.0, seed=0):
    """Cloth hanging from top_c (Vector), facing local -Y, tilted `tilt` deg toward -Y, swallow-tail hem,
    white shield with a team sword."""
    rng = random.Random(seed)
    nu, nv = 7, 10
    ct, st = math.cos(math.radians(tilt)), math.sin(math.radians(tilt))
    grid = []
    for j in range(nv):
        row = []
        for i in range(nu):
            u = i / (nu - 1)
            vb = 1.0 - 0.14 * (1.0 - abs(2 * u - 1))      # swallow-tail: centre is shorter
            v = j / (nv - 1) * vb
            wave = 0.028 * math.sin(2 * math.pi * u * 1.3 + v * 4.0 + seed) * v
            x = (u - 0.5) * width * (1.0 + 0.04 * v)
            y = -v * length * st + wave
            z = -v * length * ct
            row.append(top_c + Vector((x, y, z)))
        grid.append(row)
    pc.sheet(grid, 0.035, lambda f: pal.vc("team") if f.normal.z > -0.5 else pal.vc("team_dark"))
    # emblem frame on the cloth (normal n points toward -Y and up)
    n = Vector((0, -ct, st))
    d = Vector((0, -st, -ct))
    xax = Vector((1, 0, 0))
    o = top_c + d * (length * 0.42) + n * 0.065

    def P(x, yv, lift=0.0):
        return o + xax * x + d * yv + n * lift
    shield = [P(-0.3, -0.33), P(0.3, -0.33), P(0.3, 0.05), P(0.17, 0.25), P(0.0, 0.36), P(-0.17, 0.25), P(-0.3, 0.05)]
    pc.poly(list(reversed(shield)) if _facing(shield, n) < 0 else shield, pal.vc("cream"), thick=0.012)
    blade = [P(-0.045, -0.27, 0.02), P(0.045, -0.27, 0.02), P(0.045, 0.2, 0.02), P(0.0, 0.29, 0.02),
             P(-0.045, 0.2, 0.02)]
    guard = [P(-0.17, -0.12, 0.025), P(0.17, -0.12, 0.025), P(0.17, -0.06, 0.025), P(-0.17, -0.06, 0.025)]
    for poly in (blade, guard):
        pc.poly(list(reversed(poly)) if _facing(poly, n) < 0 else poly, pal.vc("team_dark"), thick=0.01)


def _facing(pts, n):
    a, b, c = Vector(pts[0]), Vector(pts[1]), Vector(pts[2])
    return (b - a).cross(c - a).dot(n)


def gate_post(name="cm_gate_post", seed=3, banner=True, tilt=15.0, arm=0.98, w=1.1):
    """3.75 u: 0.15 plinth + 4 courses + 0.15 light cap slab + 0.45 team cone cap; banner on the local -Y face."""
    pc = Piece(name, team=True)
    rng = random.Random(seed)
    pc.box(0, 0, 0, w + 0.24, w + 0.24, 0.15, pal.vc("body_a"), pal.vc("cap_side"), bev=0.04)
    for c in range(4):
        side, top = kit.stone_side(rng)
        pc.box(rng.uniform(-0.02, 0.02), rng.uniform(-0.02, 0.02), 0.15 + c * COURSE + GAP / 2, w, w,
               COURSE - GAP, side, top, rot=math.radians(rng.uniform(-2.5, 2.5)), mark="post_course")
        if c == 1:
            pass
    pc.box(0, 0, 3.15, w + 0.16, w + 0.16, 0.15, pal.vc("cap_side"), pal.vc("cap"), bev=0.04, mark="post_cap")
    pc.lathe([(0.78 * w, 3.30), (0.78 * w, 3.36), (0.0, 3.75)], 4, lambda f: pal.vc("team") if f.normal.z > -0.3 else
             pal.vc("team_dark"), rot=math.pi / 4, mark="post_top")
    if banner:
        # iron arm and the cloth
        pc.box(0, -(w / 2 + arm) / 2, 2.92, 0.08, max(0.1, arm - w / 2), 0.08, pal.vc("iron"), bev=0.015)
        pc.box(0, -arm, 2.84, 1.12, 0.07, 0.07, pal.vc("wood_d"), bev=0.015)
        banner_cloth(pc, Vector((0, -arm, 2.8)), width=1.0, length=1.8, tilt=tilt, seed=seed)
    return pc


def banner_post(name="cm_banner", seed=5):
    """Bible p14 banner: 3.0 u wooden T-post on a stone base, 1.0 x 1.8 cloth tilted 15 deg toward local -Y."""
    pc = Piece(name, team=True, kind="prop")
    rng = random.Random(seed)
    for (x, y, sx, sy, h, r) in ((0.0, 0.05, 0.62, 0.55, 0.42, 0.1), (-0.28, -0.2, 0.42, 0.38, 0.3, 0.5),
                                 (0.3, -0.12, 0.36, 0.34, 0.24, -0.3)):
        side, top = kit.stone_side(rng, warm=True)
        pc.box(x, y, 0, sx, sy, h, side, top, rot=r, bev=0.05)
    kit.moss(pc, rng, 0.1, 0.1, 0.42, 0.2)
    pc.box(0, 0.05, 0.2, 0.17, 0.17, 2.8, pal.vc("wood_b"), pal.vc("wood"), bev=0.03)
    pc.box(0, 0.05, 2.78, 1.35, 0.16, 0.16, pal.vc("wood_b"), pal.vc("wood"), bev=0.03)
    for x in (-0.55, 0.55):
        pc.box(x, 0.05, 2.74, 0.12, 0.2, 0.24, pal.vc("rope"), bev=0.02)
    pc.lathe([(0.09, 3.0), (0.09, 3.06), (0.0, 3.14)], 6, solid(pal.vc("brass")), cy=0.05)
    banner_cloth(pc, Vector((0, -0.06, 2.74)), seed=seed)
    return pc


# ------------------------------------------------------------------------------ towers
def tower(name="cm_tower", seed=21, door_dir=-math.pi / 2):
    """Squat drum 6.0 u (merlon top) x 4.0 u diameter, 8 merlons, arched door 2.6 u with lit interior, braziers
    either side, team cone roof inside the crenellation."""
    pc = Piece(name, team=True)
    rng = random.Random(seed)
    r = 2.0
    top = kit.drum(pc, rng, r, 6, door=(door_dir, 1.3, 2.6))
    kit.corbel_ring(pc, rng, r, top, h=0.6)
    pc.lathe([(r - 0.1, top + 0.5), (r - 0.1, top + 0.6)], 16, solid(pal.vc("pave_d")))
    kit.merlon_ring(pc, rng, r + 0.12, top + 0.6, n=8)
    pc.marks["tower_top"] = [((0, 0, 0), (0, 0, top + 0.6 + 0.9))]
    kit.cone_roof(pc, 1.4, top + 0.6, 1.5, n=16, rows=6)
    d = Vector((math.cos(door_dir), math.sin(door_dir), 0))
    t = Vector((-d.y, d.x, 0))
    for s in (-1, 1):
        p = d * (r - 0.22) + t * (s * 1.32)
        kit.brazier_geo(pc, p.x, p.y, 0.0, rng)
    for k in range(3):
        a = door_dir + math.radians(70 + 70 * k)
        kit.moss(pc, rng, (r + 0.05) * math.cos(a), (r + 0.05) * math.sin(a), 3 * COURSE - 0.02, 0.25)
    return pc


def landmark_tower(name="cm_tower_landmark", seed=31, door_dir=-math.pi / 2):
    """10 u x 4.5 u, team cone roof 3 u with pennant, lit door, slit windows, braziers, team banner on the face."""
    pc = Piece(name, team=True)
    rng = random.Random(seed)
    r = 2.25
    top = kit.drum(pc, rng, r, 9, door=(door_dir, 1.3, 2.6))
    kit.corbel_ring(pc, rng, r, top, h=0.25, out=0.2)
    zr = top + 0.25
    kit.cone_roof(pc, r + 0.5, zr, 10.0 - zr, n=24)
    pc.marks["landmark_top"] = [((0, 0, 0), (0, 0, 10.0))]
    pc.lathe([(0.12, 9.95), (0.16, 10.12), (0.0, 10.3)], 8, solid(pal.vc("brass")))
    pc.box(0, 0, 10.1, 0.07, 0.07, 1.5, pal.vc("wood_d"), bev=0.015)
    # pennant: team swallow-tail triangle
    d = Vector((math.cos(door_dir), math.sin(door_dir), 0))
    t = Vector((-d.y, d.x, 0))
    base = Vector((0, 0, 11.5))
    tip = base + t * 1.1 + Vector((0, 0, -0.25))
    pts = [base, base + Vector((0, 0, -0.5)), tip + Vector((0, 0, -0.1)), base + t * 0.75 + Vector((0, 0, -0.27)),
           tip + Vector((0, 0, 0.12))]
    pc.poly(pts, pal.vc("team"), thick=0.04)
    # slit windows (self-lit) on the door side and the two flanks
    for da in (0, -0.9, 0.9):
        a = door_dir + da
        dd = Vector((math.cos(a), math.sin(a), 0))
        tt = Vector((-dd.y, dd.x, 0))
        c = dd * (r + 0.02)
        z0, z1 = 4.6, 5.5
        w = 0.14
        pc.poly([c - tt * w + Vector((0, 0, z0)), c + tt * w + Vector((0, 0, z0)), c + tt * w + Vector((0, 0, z1)),
                 c + Vector((0, 0, z1 + 0.14)), c - tt * w + Vector((0, 0, z1))], pal.vc("door_hi"), thick=0.03)
        pc.lights.append((c + Vector((0, 0, 5.0)), "window"))
    # big team banner hung under the corbel, above the door
    banner_cloth(pc, d * (r + 0.14) + Vector((0, 0, top - 0.15)), width=1.2, length=2.0, seed=4)
    for s in (-1, 1):
        p = d * (r - 0.22) + t * (s * 1.38)
        kit.brazier_geo(pc, p.x, p.y, 0.0, rng)
    return pc


# ------------------------------------------------------------------------------ small kit
def brazier(name="cm_brazier"):
    pc = Piece(name, kind="prop")
    kit.brazier_geo(pc, 0, 0, 0, random.Random(1))
    return pc


def pool_decal(name="cm_light_pool", radius=2.0, hexc="#FFC27A", alpha=0.35):
    """2.0 u radius warm ground pool (35% #FFC27A at the centre, fading out). Alpha of Col = opacity here."""
    pc = Piece(name, kind="decal")
    import bmesh
    n = 24
    t = bmesh.new()
    c = t.verts.new((0, 0, 0.02))
    mid = [t.verts.new((radius / 2 * math.cos(2 * math.pi * i / n), radius / 2 * math.sin(2 * math.pi * i / n), 0.02)) for i in range(n)]
    ring = [t.verts.new((radius * math.cos(2 * math.pi * i / n), radius * math.sin(2 * math.pi * i / n), 0.02)) for i in range(n)]
    for i in range(n):
        j = (i + 1) % n
        t.faces.new([c, mid[i], mid[j]])
        t.faces.new([mid[i], ring[i], ring[j], mid[j]])
    t.normal_update()
    r, g, b = pal.lin(hexc)
    vm = {v: pc.bm.verts.new(v.co) for v in t.verts}
    for f in t.faces:
        nf = pc.bm.faces.new([vm[v] for v in f.verts])
        for lp, ov in zip(nf.loops, f.verts):
            d = ov.co.xy.length / radius
            lp[pc.col] = (r, g, b, alpha * max(0.0, 1 - d) ** 1.2)
    t.free()
    return pc


def crystal_lamp(name="cm_crystal_lamp", height=0.9, seed=7):
    pc = Piece(name, team=True, kind="prop")
    kit.crystal_cluster(pc, 0, 0, 0, random.Random(seed), height=height, n=5 if height > 0.5 else 4,
                        spread=0.22 if height > 0.5 else 0.3, base=height > 0.5)
    return pc


def stairs(name="cm_stairs", width=2.5, steps=6, seed=9):
    """Rise 0.25, tread 0.35, each step its own block with a light nose; block cheek walls both sides."""
    pc = Piece(name)
    rng = random.Random(seed)
    depth = steps * 0.35
    for k in range(steps):
        y0 = k * 0.35
        side, _ = kit.stone_side(rng, warm=True)
        pc.box(0, (y0 + depth) / 2, k * 0.25, width, depth - y0 - GAP, 0.25 - GAP / 2, side, pal.vc("cap"),
               mark="step")
    for sx in (-1, 1):
        for k in range(steps):
            y0 = k * 0.35
            side, top = kit.stone_side(rng)
            pc.box(sx * (width / 2 + 0.27), y0 + 0.175, 0, 0.5, 0.35 - GAP, (k + 1) * 0.25 + 0.3, side, top, bev=0.04)
    return pc


def roof_pitched(name="cm_roof_pitched", span=3.0, length=4.0, pitch=50.0):
    """Gable roof, team colour, tile rows as 0.25 u steps with darker undersides, ridge cap."""
    pc = Piece(name, team=True)
    h = span / 2 * math.tan(math.radians(pitch))
    slope = math.hypot(span / 2, h)
    rows = int(slope / 0.25)
    for sgn in (-1, 1):
        for k in range(rows):
            t0, t1 = k / rows, (k + 1) / rows
            x0, z0 = sgn * (span / 2 + 0.2) * (1 - t0), h * t0
            x1, z1 = sgn * (span / 2 + 0.2) * (1 - t1), h * t1
            off = 0.07
            cs = [Vector((x0 + sgn * off, -length / 2 - 0.2, z0 - 0.06)), Vector((x0 + sgn * off, length / 2 + 0.2, z0 - 0.06)),
                  Vector((x1, length / 2 + 0.2, z1)), Vector((x1, -length / 2 - 0.2, z1))]
            cs += [c + Vector((0, 0, 0.08)) for c in cs]
            pc.block(cs, pal.vc("team_dark"), pal.vc("team"), bev=0.015, top_thresh=0.3, mark="roof")
    pc.box(0, 0, h - 0.02, 0.24, length + 0.5, 0.16, pal.vc("team_dark"), pal.vc("team_dark"), bev=0.03)
    return pc


def respawn_pad(name="cm_respawn_pad", seed=13):
    """Bible p14 respawn pad, walkable (nothing above 0.3 u): an outer ring of 12 chunky chipped blocks of varied width
    (r 2.72..3.75), a light inner kerb, the team glow channel with a dark lip, a dark slate disc laid in tiles, the
    glowing cross, low team crystal clusters in the ring, mossy tops, grass and boulders outside."""
    pc = Piece(name, team=True)
    rng = random.Random(seed)
    _ring_blocks(pc, rng, 2.74, 3.75, 0.22, 12, hj=0.03, moss_p=0.6, tuft_p=0.9, vary=0.3, a0=0.2, moss_size=0.3)
    _ring_blocks(pc, rng, 2.44, 2.72, 0.17, 18, hj=0.02, moss_p=0.0, chips=False, vary=0.2)
    # glow channel: dark lip, team glow ring, bright inner line
    pc.lathe([(2.36, 0.0), (2.36, 0.11), (2.44, 0.11), (2.44, 0.0)], 48, solid(pal.vc("team_dark")), closed=True)
    pc.lathe([(1.98, 0.0), (1.98, 0.12), (2.36, 0.12), (2.36, 0.0)], 48, solid(pal.vc("team_glow")), closed=True)
    pc.lathe([(1.9, 0.0), (1.9, 0.105), (1.98, 0.105), (1.98, 0.0)], 48, solid(pal.vc("team_dark")), closed=True)
    # slate disc: centre slab + a ring of 10 tiles of varied width (seams are the gaps)
    pc.lathe([(0.98, 0.0), (0.98, 0.1), (0.0, 0.1)], 32, topside(pal.vc("slate_d"), pal.vc("slate")))
    ws = [rng.uniform(0.8, 1.2) for _ in range(10)]
    t = 0.3
    for w in ws:
        span = 2 * math.pi * w / sum(ws)
        t0, t1 = t + 0.015, t + span - 0.015
        t += span
        cs = [Vector((1.02 * math.cos(t0), 1.02 * math.sin(t0), 0)), Vector((1.02 * math.cos(t1), 1.02 * math.sin(t1), 0)),
              Vector((1.88 * math.cos(t1), 1.88 * math.sin(t1), 0)), Vector((1.88 * math.cos(t0), 1.88 * math.sin(t0), 0))]
        cs += [Vector((v.x, v.y, 0.1 + rng.uniform(-0.01, 0.005))) for v in cs]
        k = rng.uniform(0.92, 1.1)
        pc.block(cs, pal.vc("slate_d", k), pal.vc(rng.choice(("slate", "slate_l")), k), bev=0.025)
    # glowing cross (raised, bevelled) with a dark rim
    for rot in (0.0, math.pi / 2):
        pc.box(0, 0, 0.1, 1.86, 0.62, 0.02, pal.vc("team_dark"), rot=rot, bev=0.0)
        pc.box(0, 0, 0.11, 1.7, 0.48, 0.035, pal.vc("team_glow"), rot=rot, bev=0.012)
    # low team crystal clusters (<= 0.3 u) nested in the outer ring, two big ones as the bible's
    for k, (a, n, sp) in enumerate(((math.radians(120), 11, 0.4), (math.radians(300), 11, 0.4),
                                    (math.radians(35), 6, 0.24), (math.radians(215), 6, 0.24))):
        kit.crystal_cluster(pc, 4.12 * math.cos(a), 4.12 * math.sin(a), 0.0, rng, height=0.29, n=n, spread=sp,
                            base=False, lamp=k < 2)
    # boulders and grass outside the ring (<= 0.28 u)
    for k in range(5):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(3.95, 4.3)
        pc.box(r * math.cos(a), r * math.sin(a), -0.04, rng.uniform(0.35, 0.55), rng.uniform(0.3, 0.45),
               rng.uniform(0.24, 0.32), *kit.stone_side(rng, warm=True), rot=a + rng.uniform(-0.4, 0.4), bev=0.06,
               segs=2, chips=kit.rand_chips(rng, 0.7, 0.3, 0.06, 0.12))
    for k in range(10):
        a = rng.uniform(0, 2 * math.pi)
        kit.grass_tuft(pc, rng, 3.95 * math.cos(a), 3.95 * math.sin(a), 0.0, 0.24)
    return pc


# ------------------------------------------------------------------------------ hill
def hill_dais(name="cm_hill_dais", seed=17):
    """Bible p14 hill dais, flat enough for the 6 u circle (inside 6 u nothing above 0.13 u): chunky kerb ring of
    varied blocks (0.3 u, just outside 6 u) with moss and grass, a grass band set with pavers, two rows of irregular
    flagstones, a cream ring, the dark slate disc and a cream medallion with the brass crown. The team rim halves are
    separate pieces (team mask)."""
    pc = Piece(name)
    rng = random.Random(seed)
    _ring_blocks(pc, rng, 6.06, 6.85, 0.25, 26, hj=0.04, moss_p=0.4, tuft_p=0.7)
    # grass band r 4.85..5.45 with a few pavers in it
    pc.lathe([(4.85, 0.0), (4.85, 0.07), (5.45, 0.07), (5.45, 0.0)], 56,
             lambda f: pal.vc("grass_d") if f.normal.z > 0.5 else pal.vc("moss_d"), closed=True)
    for k in range(11):
        a0 = 2 * math.pi * k / 11 + rng.uniform(-0.1, 0.1)
        sp = rng.uniform(0.12, 0.2)
        ri, ro = 4.92 + rng.uniform(0, 0.06), 5.38 - rng.uniform(0, 0.06)
        cs = [Vector((ri * math.cos(a0), ri * math.sin(a0), 0.0)), Vector((ri * math.cos(a0 + sp), ri * math.sin(a0 + sp), 0)),
              Vector((ro * math.cos(a0 + sp), ro * math.sin(a0 + sp), 0)), Vector((ro * math.cos(a0), ro * math.sin(a0), 0))]
        cs += [Vector((v.x, v.y, 0.1)) for v in cs]
        pc.block(cs, *kit.stone_side(rng, warm=True), bev=0.03)
    for k in range(16):
        a0 = rng.uniform(0, 2 * math.pi)
        kit.moss(pc, rng, 5.15 * math.cos(a0), 5.15 * math.sin(a0), 0.06, 0.16)
    # two rows of flagstones of varied widths r 2.9..4.85
    for (ri, ro, n) in ((3.92, 4.85, 22), (2.9, 3.88, 17)):
        _ring_blocks(pc, rng, ri, ro, 0.11, n, hj=0.02, chips=False, moss_p=0.12, vary=0.45)
    pc.lathe([(2.45, 0.0), (2.45, 0.12), (2.9, 0.12), (2.9, 0.0)], 48, solid(pal.vc("cream")), closed=True)
    pc.lathe([(2.45, 0.0), (2.45, 0.1), (0.0, 0.1)], 40, topside(pal.vc("body_a"), pal.vc("body_c")))
    # slate segments on the dark disc (radial joints as thin dark inlays)
    for k in range(8):
        a0 = 2 * math.pi * k / 8 + math.pi / 8
        c = Vector((1.75 * math.cos(a0), 1.75 * math.sin(a0), 0.1))
        pc.box(c.x, c.y, 0.1, 1.3, 0.05, 0.008, pal.vc("navy", 1.6), rot=a0, bev=0.0)
    pc.lathe([(1.12, 0.1), (1.12, 0.125), (1.05, 0.125), (1.05, 0.1)], 40, solid(pal.vc("cap")), closed=True)
    pc.lathe([(1.05, 0.1), (1.05, 0.13), (0.0, 0.13)], 32, solid(pal.vc("cream")))
    # crown emblem (brass): band + three points, laid flat, facing up
    pts = [(-0.6, -0.35), (0.6, -0.35), (0.62, 0.15), (0.38, -0.02), (0.22, 0.42), (0.0, 0.05), (-0.22, 0.42),
           (-0.38, -0.02), (-0.62, 0.15)]
    pc.poly([Vector((-x, y, 0.145)) for x, y in pts][::-1], pal.vc("brass"), thick=0.015)
    for x in (-0.38, 0.0, 0.38):
        pc.lathe([(0.07, 0.13), (0.07, 0.15), (0.0, 0.16)], 6, solid(pal.vc("brass_d")), cx=-x,
                 cy=0.5 if x == 0 else 0.45)
    return pc


def hill_rim_half(name="cm_hill_rim_half"):
    """Half of the hill rim ring (r 5.45..6.05, 0.09 u), team glow: neutral = brass, owner = team, contested =
    alternating stripes (shader). Authored as the WEST (Azure-side) half; yaw 180 for the east half."""
    pc = Piece(name, team=True)
    n = 24
    for k in range(n):
        t0 = -math.pi / 2 + math.pi * k / n + 0.004
        t1 = -math.pi / 2 + math.pi * (k + 1) / n - 0.004
        ri, ro = 5.47, 6.03
        cs = [Vector((ri * math.cos(t0), ri * math.sin(t0), 0)), Vector((ri * math.cos(t1), ri * math.sin(t1), 0)),
              Vector((ro * math.cos(t1), ro * math.sin(t1), 0)), Vector((ro * math.cos(t0), ro * math.sin(t0), 0))]
        cs += [Vector((v.x, v.y, 0.09)) for v in cs]
        pc.block(cs, pal.vc("team_glow"), pal.vc("team_glow"), bev=0.0)
    return pc


def horn(name, seed, sign=1.0, base_ang=240.0, tip_ang=257.0, r=HILL_R, height=6.0, z0=0.0, warm=True,
         plane_dir=None, base_off=0.0, segs=8, width=0.95, bulge=0.55):
    """Curved stone horn of stacked tapering blocks. Hill: rises from the ring wall cells along the ring (sim
    angles base_ang -> tip_ang). Shrine: plane_dir given, rises in that vertical plane."""
    pc = Piece(name)
    rng = random.Random(seed)
    pts = []
    for i in range(segs + 1):
        t = i / segs
        if plane_dir is None:
            ad = base_ang + (tip_ang - base_ang) * (t ** 1.6)
            a = math.radians(ad)
            rr = RHO["hill"](ad) if "hill" in RHO else r
            z = z0 + height * math.sin(t * math.pi / 2) ** 0.85
            pts.append(Vector((-rr * math.cos(a), rr * math.sin(a), z)))
        else:
            # lateral profile: out then in (bible shrine horns)
            lat = base_off + bulge * math.sin(math.pi * t * 0.8) - 0.45 * t ** 2
            z = z0 + height * t
            pts.append(plane_dir * lat + Vector((0, 0, z)))
    for i in range(segs):
        a, b = pts[i], pts[i + 1]
        w0 = width * (1 - 0.55 * i / segs)
        w1 = width * (1 - 0.55 * (i + 1) / segs)
        ax = (b - a).normalized()
        side = ax.cross(Vector((0, 0, 1)))
        if side.length < 1e-4:
            side = Vector((1, 0, 0))
        side.normalize()
        up = side.cross(ax).normalized()
        g = 0.03
        a2, b2 = a + ax * g, b - ax * g
        cs = [a2 - side * w0 / 2 - up * w0 / 2, a2 + side * w0 / 2 - up * w0 / 2, a2 + side * w0 / 2 + up * w0 / 2,
              a2 - side * w0 / 2 + up * w0 / 2,
              b2 - side * w1 / 2 - up * w1 / 2, b2 + side * w1 / 2 - up * w1 / 2, b2 + side * w1 / 2 + up * w1 / 2,
              b2 - side * w1 / 2 + up * w1 / 2]
        sc, tc = kit.stone_side(rng, warm=warm)
        pc.block(cs, sc, tc, bev=0.07, top_thresh=0.35)
        if rng.random() < 0.45:
            m = (a + b) / 2 + up * (w0 / 2)
            kit.moss(pc, rng, m.x, m.y, m.z - 0.02, 0.2)
    pc.marks["horn_top"] = [((0, 0, 0), tuple(pts[-1]))]
    return pc


def hill_horn(name, seed, base_ang, tip_ang):
    """Bible p14 hill horn at the scale the Sim allows: a plinth astride the ring wall top (on Wall cells), then five
    huge chipped blocks (1.7 -> 0.9 u) that rise to 9 u and curl inward toward the ring centre. Below 2.25 u it is
    the wall itself; everything over walkable cells is above 2.1 u (overhead), so no route is touched."""
    pc = Piece(name)
    rng = random.Random(seed)
    rho = RHO.get("hill", lambda a: HILL_R)

    def P(ad, dr, z):
        a = math.radians(ad)
        rr = rho(ad) + dr
        return Vector((-rr * math.cos(a), rr * math.sin(a), z))
    sgn = 1 if tip_ang > base_ang else -1
    # plinth: two big blocks on the wall top, wrapping the merlons there
    for k, (d0, d1) in enumerate(((-1.6, 0.1), (0.15, 1.7))):
        a0, a1 = base_ang + sgn * math.degrees(d0 / HILL_R), base_ang + sgn * math.degrees(d1 / HILL_R)
        p = outward_path(sim_arc(HILL_R, min(a0, a1), max(a0, a1), rho=rho))
        side, top = kit.stone_side(rng, warm=True)
        pc.block(kit.hexa_on_path(p, 0.0, p.L, -0.62, 0.62, 2.25, 3.2 - 0.12 * k), side, top, bev=0.08, segs=2,
                 chips=kit.rand_chips(rng, 0.8, 0.4, 0.1, 0.2))
    prof = [(0.0, 0.0, 3.15), (0.05, -0.1, 4.7), (0.15, -0.35, 6.2), (0.32, -0.85, 7.5), (0.55, -1.6, 8.5),
            (0.8, -2.4, 9.1)]
    pts = [P(base_ang + (tip_ang - base_ang) * f, dr, z) for f, dr, z in prof]
    _block_chain(pc, rng, pts, 1.9, 1.2, depth0=1.4, depth1=1.0, warm=True, moss_p=0.6, gap=0.04)
    pc.marks["horn_top"] = [((0, 0, 0), tuple(pts[-1]))]
    return pc


# ------------------------------------------------------------------------------ shrine
SHRINE_DAIS_TOP = 1.5     # two courses on the 2 x 2 solid Sim cells (the only solid cells at the shrine)


def _ring_blocks(pc, rng, ri, ro, h, n, warm=True, hj=0.0, a0=0.0, chips=True, moss_p=0.3, tuft_p=0.0, z0=0.0,
                 vary=0.35, moss_size=0.2):
    """A ring of chunky blocks with varied angular widths (each its own bevelled, chipped shell)."""
    ws = [rng.uniform(1 - vary, 1 + vary) for _ in range(n)]
    tot = sum(ws)
    t = a0
    for k in range(n):
        span = 2 * math.pi * ws[k] / tot
        t0, t1 = t + 0.012, t + span - 0.012
        t += span
        rr = ro + rng.uniform(-0.06, 0.06)
        hh = h + rng.uniform(-hj, 0.0)
        cs = [Vector((ri * math.cos(t0), ri * math.sin(t0), z0)), Vector((ri * math.cos(t1), ri * math.sin(t1), z0)),
              Vector((rr * math.cos(t1), rr * math.sin(t1), z0)), Vector((rr * math.cos(t0), rr * math.sin(t0), z0))]
        cs += [Vector((v.x, v.y, z0 + hh)) for v in cs]
        pc.block(cs, *kit.stone_side(rng, warm=warm), bev=0.06, segs=2,
                 chips=kit.rand_chips(rng, 0.55, 0.2, 0.07, min(0.16, hh * 0.6)) if chips else None)
        if rng.random() < moss_p:
            m = (cs[4] + cs[6]) / 2
            kit.moss(pc, rng, m.x, m.y, z0 + hh - 0.03, moss_size)
        if rng.random() < tuft_p:
            tm = (t0 + t1) / 2
            kit.grass_tuft(pc, rng, (rr + 0.12) * math.cos(tm), (rr + 0.12) * math.sin(tm), z0, min(0.26, 0.3 - z0))


def shrine_core(name="cm_shrine_core", seed=41):
    """Bible p14 shrine on what the Sim gives it: a two-course dais (1.5 u) filling the 2 x 2 solid cells, a walkable
    chunky step ring (0.25 u) r 1.4..3.0, two big crescent horns whose feet stand in the dais corners and which
    swing outward only above 2.1 u (overhead), team crystals, mossy tops and a waterfall down the camera face."""
    pc = Piece(name, team=True)
    rng = random.Random(seed)
    _ring_blocks(pc, rng, 1.42, 3.0, 0.25, 13, hj=0.04, moss_p=0.35, tuft_p=0.45)
    # dais: two courses of chunky blocks inside the 2 x 2 square (x, y in [-1, 1])
    lay = [[(-1.0, -0.1, -1.0, 1.0), (-0.1, 1.0, -1.0, -0.05), (-0.1, 1.0, -0.05, 1.0)],
           [(-1.0, 1.0, -1.0, -0.25), (-1.0, 0.15, -0.25, 1.0), (0.15, 1.0, -0.25, 1.0)]]
    for c, row in enumerate(lay):
        for (x0, x1, y0, y1) in row:
            side, top = kit.stone_side(rng, warm=True)
            g = GAP / 2
            z0, z1 = c * COURSE + g, (c + 1) * COURSE - g - (0.0 if c else rng.uniform(0.0, 0.04))
            cs = [Vector((x0 + g, y0 + g, z0)), Vector((x1 - g, y0 + g, z0)), Vector((x1 - g, y1 - g, z0)),
                  Vector((x0 + g, y1 - g, z0))]
            cs += [Vector((v.x, v.y, z1)) for v in cs]
            pc.block(cs, side, top, bev=0.07, segs=2, chips=kit.rand_chips(rng, 0.7, 0.3, 0.1, 0.2))
    # dark rock collar the egg sits in (bible: dark stones around the egg foot)
    for k in range(7):
        a = 2 * math.pi * k / 7 + rng.uniform(-0.2, 0.2)
        r = rng.uniform(0.5, 0.66)
        pc.box(r * math.cos(a), r * math.sin(a), SHRINE_DAIS_TOP - 0.02, rng.uniform(0.3, 0.42), rng.uniform(0.26, 0.36),
               rng.uniform(0.16, 0.28), pal.vc("egg_d", 1.15), pal.vc("egg", 1.35), rot=a, bev=0.05,
               chips=kit.rand_chips(rng, 0.6, 0.2, 0.05, 0.1))
    # moss cushions and grass on the dais rim
    for k in range(9):
        a = 2 * math.pi * k / 9 + rng.uniform(-0.25, 0.25)
        x, y = max(-0.82, min(0.82, 1.2 * math.cos(a))), max(-0.82, min(0.82, 1.2 * math.sin(a)))
        kit.moss(pc, rng, x, y, SHRINE_DAIS_TOP - 0.03, rng.uniform(0.2, 0.3))
    # horns in the vertical plane of screen-right (sim NE-SW): blender direction (-1, -1)/sqrt2
    pdir = Vector((-1, -1, 0)).normalized()
    for sgn in (-1, 1):
        _shrine_horn(pc, rng, pdir * sgn)
    # team crystals on the two other dais corners and low ones (<= 0.3 u) on the step ring
    qdir = Vector((-1, 1, 0)).normalized()
    for sgn, hgt in ((1, 0.75), (-1, 1.05)):
        c = qdir * sgn * 1.0
        kit.crystal_cluster(pc, c.x, c.y, SHRINE_DAIS_TOP - 0.02, rng, height=hgt, n=5, spread=0.2, base=False)
    for a in (0.5, 2.3, 3.6, 5.2):
        kit.crystal_cluster(pc, 2.3 * math.cos(a), 2.3 * math.sin(a), 0.2, rng, height=0.1, n=5, spread=0.2, base=False,
                            lamp=False)
    # waterfall: from the dais top down its +y face (sim south, toward the camera) onto the step ring and a pool
    w = 0.66
    z1 = SHRINE_DAIS_TOP - 0.05
    cs = [Vector((-w / 2, 0.93, z1)), Vector((w / 2, 0.93, z1)), Vector((w / 2, 1.0, z1)), Vector((-w / 2, 1.0, z1)),
          Vector((-w / 2 - 0.05, 0.93, 0.27)), Vector((w / 2 + 0.05, 0.93, 0.27)), Vector((w / 2 + 0.05, 1.0, 0.27)),
          Vector((-w / 2 - 0.05, 1.0, 0.27))]
    pc.block([cs[4], cs[5], cs[6], cs[7], cs[0], cs[1], cs[2], cs[3]], pal.vc("water"), pal.vc("water"), bev=0.015)
    for k in range(4):
        x = -w / 2.6 + k * w / 3.9
        pc.box(x, 1.0, 0.4 + 0.25 * k, 0.05, 0.03, 0.18, pal.vc("foam"), pal.vc("foam"), bev=0.0)
    pc.box(0, 0.96, z1, w + 0.1, 0.12, 0.05, pal.vc("foam"), pal.vc("foam"), bev=0.015)
    pc.lathe([(0.6, 0.25), (0.6, 0.27), (0.0, 0.27)], 14, solid(pal.vc("water")), cy=1.6)
    pc.lathe([(0.68, 0.25), (0.68, 0.29), (0.6, 0.29), (0.6, 0.25)], 14, solid(pal.vc("foam")), cy=1.6, closed=True)
    pc.lathe([(0.9, 0.0), (0.9, 0.04), (0.0, 0.04)], 16, solid(pal.vc("water")), cy=3.3)
    pc.lathe([(0.95, 0.0), (0.95, 0.03), (0.88, 0.05), (0.88, 0.0)], 16, solid(pal.vc("foam")), cy=3.3, closed=True)
    return pc


def _shrine_horn(pc, rng, d):
    """Crescent horn in the vertical plane of direction d: the foot is a corner column of the dais (inside the solid
    square up to 2.15 u), then chunky tapering blocks swing out to 2.0 u and curl back over the egg to 6.0 u."""
    # corner column (square-aligned, the corner of the 2 x 2 cells)
    sx, sy = (1 if d.x > 0 else -1), (1 if d.y > 0 else -1)
    side, top = kit.stone_side(rng, warm=True)
    pc.box(sx * 0.7, sy * 0.7, SHRINE_DAIS_TOP, 0.56, 0.56, 0.92, side, top, rot=0.0, bev=0.07, segs=2,
           chips=[(4 + i, 0.1) for i in range(1)])
    prof = [(1.0, 2.5), (1.38, 3.05), (1.7, 3.62), (1.9, 4.25), (1.93, 4.88), (1.8, 5.45), (1.52, 5.95), (1.18, 6.22)]
    pts = [d * lat + Vector((0, 0, z)) for lat, z in prof]
    _block_chain(pc, rng, pts, 0.98, 0.7, depth0=0.8, depth1=0.6, warm=True, moss_p=0.5)


def _block_chain(pc, rng, pts, w0, w1, depth0=None, depth1=None, warm=True, moss_p=0.4, gap=0.035):
    """Chunky tapering blocks along a polyline (each block its own chipped shell, joints follow the curve)."""
    n = len(pts) - 1
    depth0 = depth0 or w0
    depth1 = depth1 or w1
    ups = []
    for i in range(n + 1):
        a = pts[max(0, i - 1)]
        b = pts[min(n, i + 1)]
        ax = (b - a).normalized()
        side = ax.cross(Vector((0, 0, 1)))
        if side.length < 1e-4:
            side = Vector((1, 0, 0))
        side.normalize()
        ups.append((ax, side, side.cross(ax).normalized()))
    for i in range(n):
        a, b = pts[i], pts[i + 1]
        ta, tb = i / n, (i + 1) / n
        wa, wb = w0 + (w1 - w0) * ta, w0 + (w1 - w0) * tb
        da, db = depth0 + (depth1 - depth0) * ta, depth0 + (depth1 - depth0) * tb
        axa, sa, ua = ups[i]
        axb, sb, ub = ups[i + 1]
        a2, b2 = a + axa * gap, b - axb * gap
        cs = [a2 - sa * da / 2 - ua * wa / 2, a2 + sa * da / 2 - ua * wa / 2, a2 + sa * da / 2 + ua * wa / 2,
              a2 - sa * da / 2 + ua * wa / 2,
              b2 - sb * db / 2 - ub * wb / 2, b2 + sb * db / 2 - ub * wb / 2, b2 + sb * db / 2 + ub * wb / 2,
              b2 - sb * db / 2 + ub * wb / 2]
        sc, tc = kit.stone_side(rng, warm=warm)
        pc.block(cs, sc, tc, bev=0.08, segs=2, top_thresh=0.35, chips=kit.rand_chips(rng, 0.7, 0.35, 0.1, 0.2, False))
        if rng.random() < moss_p:
            m = (a + b) / 2 + (ua + ub).normalized() * (wa + wb) / 4
            kit.moss(pc, rng, m.x, m.y, m.z - 0.04, 0.16 + 0.08 * (wa + wb) / 2)


def _merge(dst, src):
    import bmesh
    me_bm = src.bm
    vm = {v: dst.bm.verts.new(v.co) for v in me_bm.verts}
    col_s = me_bm.loops.layers.float_color["Col"]
    for f in me_bm.faces:
        nf = dst.bm.faces.new([vm[v] for v in f.verts])
        for lp, sl in zip(nf.loops, f.loops):
            lp[dst.col] = sl[col_s]
    dst.blocks += src.blocks
    src.bm.free()


def shrine_egg(name="cm_shrine_egg", seed=43):
    """Bible p14 faceted egg (1.5 x 2.3 u): big dark stone plates (pentagons and hexagons, the dual of an icosphere)
    with self-lit #4FDDC3 seams between them, the brightest thing in the district."""
    import bmesh
    pc = Piece(name, kind="glow")
    rx, rz = 0.75, 1.15
    pc.blob((0, 0, rz), rx * 0.93, rx * 0.93, rz * 0.93, pal.vc("egg_glow"), sub=3)
    t = bmesh.new()
    bmesh.ops.create_icosphere(t, subdivisions=2, radius=1.0)
    rng = random.Random(seed)

    def egg_pt(u):
        u = u.normalized()
        k = 1.0 + 0.1 * max(0.0, -u.z) - 0.06 * max(0.0, u.z)     # broader bottom, narrower top
        return Vector((u.x * rx * k, u.y * rx * k, rz + u.z * rz))
    for v in t.verts:
        cents = [f.calc_center_median().normalized() for f in v.link_faces]
        n = v.co.normalized()
        ref = (cents[0] - n * cents[0].dot(n)).normalized()
        oth = n.cross(ref)
        cents.sort(key=lambda c: math.atan2((c - n).dot(oth), (c - n).dot(ref)))
        shrink = rng.uniform(0.8, 0.87)
        pts = [egg_pt(n + (c - n) * shrink) for c in cents]
        cen = egg_pt(n)
        nrm = (cen - Vector((0, 0, rz))).normalized()
        push = rng.uniform(0.02, 0.06)
        pts = [p + nrm * push for p in pts]
        if _facing(pts, nrm) < 0:
            pts = pts[::-1]
        c = pal.vc("egg") if rng.random() < 0.55 else pal.vc("egg_d")
        top = pal.vc("egg", 1.45)
        pc.poly(pts, lambda f, c=c, top=top, nrm=nrm: top if f.normal.dot(nrm) > 0.5 and nrm.z > 0.15 else c,
                thick=0.11)
    t.free()
    pc.lights.append((Vector((0, 0, 1.2)), "egg"))
    return pc


# ------------------------------------------------------------------------------ district dressing (stand-ins)
def crate(name="cm_dress_crate", s=0.7, seed=0):
    """Plank crate: dark frame edges, board faces with grooves, a diagonal brace on two sides."""
    pc = Piece(name, kind="prop")
    rng = random.Random(seed)
    pc.box(0, 0, 0, s, s, s, pal.vc("wood_b"), pal.vc("wood"), bev=0.035)
    e = 0.09
    for z in (0.0, s - e):                                   # top and bottom frame rails
        for (cx, cy, sx, sy) in ((0, -s / 2, s + 0.02, e), (0, s / 2, s + 0.02, e), (-s / 2, 0, e, s + 0.02),
                                 (s / 2, 0, e, s + 0.02)):
            pc.box(cx, cy, z, sx, sy, e, pal.vc("wood_d", 1.15), pal.vc("wood_b"), bev=0.015)
    for cx, cy in ((-s / 2, -s / 2), (s / 2, -s / 2), (s / 2, s / 2), (-s / 2, s / 2)):   # corner posts
        pc.box(cx, cy, 0, e + 0.01, e + 0.01, s, pal.vc("wood_d", 1.15), pal.vc("wood_b"), bev=0.015)
    for sgn, axis in ((-1, "y"), (1, "x")):                  # diagonal braces on two faces
        L = s * 1.2
        if axis == "y":
            M = Matrix.Translation((0, sgn * (s / 2 + 0.01), s / 2)) @ Matrix.Rotation(math.radians(45), 4, "Y")
        else:
            M = Matrix.Translation((sgn * (s / 2 + 0.01), 0, s / 2)) @ Matrix.Rotation(math.radians(45), 4, "X")
        pc.box(0, 0, -0.04, L if axis == "y" else 0.04, 0.04 if axis == "y" else L, 0.08, pal.vc("wood_d", 1.2),
               bev=0.01, M=M)
    for k in (-1, 1):                                        # board grooves on the lid
        pc.box(k * s / 6, 0, s - 0.004, 0.025, s - 0.18, 0.012, pal.vc("wood_d"), bev=0.0)
    return pc


def barrel(name="cm_dress_barrel"):
    pc = Piece(name, kind="prop")
    pc.lathe([(0.27, 0), (0.33, 0.25), (0.33, 0.55), (0.27, 0.8), (0.0, 0.8)], 12,
             topside(pal.vc("wood_b"), pal.vc("wood")), bev=0.0)
    for z in (0.12, 0.62):
        pc.lathe([(0.325, z), (0.34, z + 0.02), (0.34, z + 0.07), (0.325, z + 0.09)], 12,
                 topside(pal.vc("brass_d"), pal.vc("brass")))
    pc.lathe([(0.22, 0.8), (0.22, 0.81), (0.0, 0.81)], 10, solid(pal.vc("wood_d", 1.2)))
    return pc


def weapon_rack(name="cm_dress_weapon_rack"):
    """Wooden rack (1.4 x 0.45 x 1.3 u) with four spears, two swords and a brass-bossed round shield leaning on it."""
    pc = Piece(name, kind="prop")
    for x in (-0.62, 0.62):
        pc.box(x, 0, 0, 0.12, 0.42, 0.08, pal.vc("wood_d", 1.2), pal.vc("wood_b"), bev=0.015)
        pc.box(x, 0.0, 0.0, 0.11, 0.11, 1.25, pal.vc("wood_b"), pal.vc("wood"), bev=0.02)
    for z in (0.35, 1.05):
        pc.box(0, 0.0, z, 1.36, 0.1, 0.09, pal.vc("wood_b"), pal.vc("wood"), bev=0.02)
    for k, x in enumerate((-0.42, -0.14, 0.14, 0.42)):       # spears leaning on the top bar
        M = Matrix.Translation((x, 0.12, 0.0)) @ Matrix.Rotation(math.radians(-12), 4, "X")
        pc.box(0, 0, 0, 0.065, 0.065, 1.55, pal.vc("wood_b", 0.95), pal.vc("wood"), bev=0.01, M=M)
        pc.lathe([(0.075, 1.55), (0.0, 1.85)], 4, solid(pal.vc("iron", 1.5)), M=M)
    for x in (-0.3, 0.3):                                     # swords hanging point down from the low bar
        pc.box(x, -0.09, 0.08, 0.07, 0.025, 0.62, pal.vc("iron", 1.7), pal.vc("cream"), bev=0.008)
        pc.box(x, -0.09, 0.7, 0.24, 0.05, 0.05, pal.vc("brass_d"), pal.vc("brass"), bev=0.008)
        pc.box(x, -0.09, 0.75, 0.05, 0.05, 0.18, pal.vc("wood_d", 1.2), bev=0.008)
    M = Matrix.Translation((0.78, -0.18, 0.42)) @ Matrix.Rotation(math.radians(70), 4, "X") @ \
        Matrix.Rotation(math.radians(-20), 4, "Z")
    pc.lathe([(0.42, 0.0), (0.42, 0.06), (0.36, 0.09), (0.0, 0.09)], 12, topside(pal.vc("wood_b"), pal.vc("wood")), M=M)
    pc.lathe([(0.11, 0.09), (0.09, 0.14), (0.0, 0.15)], 8, solid(pal.vc("brass")), M=M)
    pc.lathe([(0.43, 0.0), (0.43, 0.065), (0.39, 0.07), (0.39, 0.0)], 12, solid(pal.vc("brass_d")), M=M, closed=True)
    return pc


def stakes(name="cm_dress_stakes"):
    """Cheval-de-frise (1.8 u): a log on crossed feet with five sharpened stakes pointing out (local -Y) and up."""
    pc = Piece(name, kind="prop")
    M = Matrix.Translation((0, 0, 0.38)) @ Matrix.Rotation(math.pi / 2, 4, "Y")
    pc.lathe([(0.13, -0.9), (0.13, 0.9)], 8, topside(pal.vc("wood_b"), pal.vc("wood")), M=M)
    for x in (-0.75, 0.75):
        for sgn in (-1, 1):
            Mf = Matrix.Translation((x, 0, 0)) @ Matrix.Rotation(sgn * math.radians(35), 4, "X")
            pc.box(0, 0, 0, 0.1, 0.1, 0.62, pal.vc("wood_b"), pal.vc("wood"), bev=0.015, M=Mf)
    for k in range(5):
        x = -0.72 + k * 0.36
        Ms = Matrix.Translation((x, 0.25, 0.12)) @ Matrix.Rotation(math.radians(-38 if k % 2 else -50), 4, "X")
        pc.lathe([(0.075, 0.0), (0.075, 0.95), (0.0, 1.25)], 6,
                 lambda f: pal.vc("wood") if f.normal.z > 0.2 else pal.vc("wood_b"), M=Ms)
        pc.lathe([(0.08, 0.6), (0.08, 0.66)], 6, solid(pal.vc("rope")), M=Ms)
    return pc


def block_stack(name="cm_dress_blocks", seed=51):
    pc = Piece(name, kind="prop")
    rng = random.Random(seed)
    pc.box(0, 0, 0, 1.1, 0.7, 0.45, *kit.stone_side(rng, True))
    pc.box(0.5, 0.55, 0, 0.8, 0.6, 0.4, *kit.stone_side(rng, True), rot=0.4)
    pc.box(0.1, 0.05, 0.45, 0.8, 0.55, 0.4, *kit.stone_side(rng, True), rot=-0.2)
    return pc


def rubble_low(name="cm_dress_rubble_low", seed=53):
    pc = Piece(name, kind="prop")
    rng = random.Random(seed)
    for _ in range(6):
        pc.box(rng.uniform(-0.5, 0.5), rng.uniform(-0.4, 0.4), -0.02, rng.uniform(0.18, 0.4), rng.uniform(0.15, 0.35),
               rng.uniform(0.1, 0.24), *kit.stone_side(rng, True), rot=rng.uniform(0, 3), bev=0.035)
    return pc


def planks_low(name="cm_dress_planks_low", seed=55):
    pc = Piece(name, kind="prop")
    rng = random.Random(seed)
    for k in range(4):
        c = pal.vc("wood" if k % 2 else "wood_b")
        pc.box(rng.uniform(-0.2, 0.2), k * 0.05 + rng.uniform(-0.1, 0.1), k * 0.07, 1.4, 0.24, 0.06, c, c,
               rot=rng.uniform(-0.35, 0.35), bev=0.015)
    return pc


def flag_stones(name="cm_dress_flagstones", seed=57):
    pc = Piece(name, kind="prop")
    rng = random.Random(seed)
    for _ in range(5):
        c = pal.vc(rng.choice(["pave", "pave_d", "cap_side"]))
        pc.box(rng.uniform(-0.7, 0.7), rng.uniform(-0.6, 0.6), 0.0, rng.uniform(0.4, 0.8), rng.uniform(0.35, 0.7),
               0.06, c, c, rot=rng.uniform(0, 3), bev=0.02)
    return pc


def warrior_stand_in(team="azure"):
    """2.0 u reference figure (silhouette-proportioned stand-in; the real Warrior lives on art/warrior-blender)."""
    pc = Piece("ref_warrior", team=True, kind="char")
    pc.box(-0.17, 0, 0, 0.24, 0.3, 0.75, pal.vc("navy", 2.2), bev=0.05)
    pc.box(0.17, 0, 0, 0.24, 0.3, 0.75, pal.vc("navy", 2.2), bev=0.05)
    pc.box(0, 0, 0.7, 0.72, 0.46, 0.65, pal.vc("team"), pal.vc("team"), bev=0.1, segs=2)
    pc.box(0, 0, 1.33, 0.52, 0.5, 0.42, pal.vc("cream", 0.9), bev=0.12, segs=2)
    pc.lathe([(0.36, 1.62), (0.38, 1.7), (0.28, 1.9), (0.0, 2.0)], 10, solid(pal.vc("team_dark")))
    pc.lathe([(0.34, 0.75), (0.34, 0.8)], 10, solid(pal.vc("brass")), cx=0.0, cy=0.0)
    pc.box(-0.5, -0.05, 0.75, 0.12, 0.7, 0.75, pal.vc("brass"), pal.vc("brass"), bev=0.04)
    pc.box(0.52, 0.0, 0.8, 0.1, 0.1, 0.9, pal.vc("iron", 1.6), bev=0.02)
    return pc
