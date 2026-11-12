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
    kit.wall_run(pc, p, random.Random(seed), thick=0.9, team_caps=team_caps, damage=state, warm=warm,
                 moss_p=0.3 if warm else 0.12)
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
    nu, nv = 6, 10
    ct, st = math.cos(math.radians(tilt)), math.sin(math.radians(tilt))
    grid = []
    for j in range(nv):
        row = []
        for i in range(nu):
            u = i / (nu - 1)
            vb = 1.0 - 0.21 * (1.0 - abs(2 * u - 1))      # swallow-tail: centre is shorter
            v = j / (nv - 1) * vb
            wave = 0.045 * math.sin(2 * math.pi * u * 1.3 + v * 4.0 + seed) * v
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
    o = top_c + d * (length * 0.42) + n * 0.03

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
    """Bible p14 respawn pad, flat enough to walk (top 0.22 u, crystals 0.3 u): block ring r 2.7..3.75, inner disc,
    team glow ring and cross glyph, four low team crystals."""
    pc = Piece(name, team=True)
    rng = random.Random(seed)
    n = 14
    for k in range(n):
        t0, t1 = 2 * math.pi * k / n + 0.01, 2 * math.pi * (k + 1) / n - 0.01
        ri, ro = 2.72, 3.75 + rng.uniform(-0.06, 0.06)
        h = 0.15 + rng.uniform(0.0, 0.02)
        cs = [Vector((ri * math.cos(t0), ri * math.sin(t0), 0)), Vector((ri * math.cos(t1), ri * math.sin(t1), 0)),
              Vector((ro * math.cos(t1), ro * math.sin(t1), 0)), Vector((ro * math.cos(t0), ro * math.sin(t0), 0))]
        cs += [Vector((v.x, v.y, h)) for v in cs]
        pc.block(cs, *kit.stone_side(rng, warm=True), bev=0.05)
    pc.lathe([(2.7, 0.0), (2.7, 0.09), (0.0, 0.09)], 32, topside(pal.vc("body_c"), pal.vc("pave_d")))
    pc.lathe([(2.0, 0.09), (2.42, 0.09), (2.42, 0.12), (2.0, 0.12)], 40, solid(pal.vc("team_glow")))
    pc.lathe([(1.88, 0.09), (1.88, 0.1), (0.0, 0.1)], 32, solid(pal.vc("pave")))
    for rot in (0.0, math.pi / 2):
        pc.box(0, 0, 0.1, 1.7, 0.5, 0.025, pal.vc("team_glow"), rot=rot, bev=0.0)
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        kit.crystal_cluster(pc, 3.2 * math.cos(a), 3.2 * math.sin(a), 0.03, rng, height=0.26, n=4, spread=0.22,
                            base=False)
    return pc


# ------------------------------------------------------------------------------ hill
def hill_dais(name="cm_hill_dais", seed=17):
    """Flat (max 0.2 u) concentric stone dais inside the 6 u hill circle: kerb ring, paving ring, cream ring,
    dark inner disc, cream medallion with the brass crown. The rim halves are separate (team mask)."""
    pc = Piece(name)
    rng = random.Random(seed)
    for (ri, ro, n, h) in ((6.05, 6.7, 30, 0.2), (4.85, 5.45, 24, 0.12), (2.9, 4.85, 18, 0.1)):
        for k in range(n):
            t0, t1 = 2 * math.pi * k / n + 0.006, 2 * math.pi * (k + 1) / n - 0.006
            cs = [Vector((ri * math.cos(t0), ri * math.sin(t0), 0)), Vector((ri * math.cos(t1), ri * math.sin(t1), 0)),
                  Vector((ro * math.cos(t1), ro * math.sin(t1), 0)), Vector((ro * math.cos(t0), ro * math.sin(t0), 0))]
            cs += [Vector((v.x, v.y, h + rng.uniform(-0.01, 0.01))) for v in cs]
            pc.block(cs, *kit.stone_side(rng, warm=True), bev=0.04)
    pc.lathe([(5.45, 0.0), (5.45, 0.06), (6.05, 0.06), (6.05, 0.0)], 48, solid(pal.vc("pave_d")))
    pc.lathe([(2.45, 0.0), (2.45, 0.12), (2.9, 0.12), (2.9, 0.0)], 48, solid(pal.vc("cream")))
    pc.lathe([(2.45, 0.0), (2.45, 0.1), (0.0, 0.1)], 40, topside(pal.vc("body_a"), pal.vc("body_c")))
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
         plane_dir=None, base_off=0.0, segs=8, width=0.95):
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
            lat = base_off + 0.55 * math.sin(math.pi * t * 0.8) - 0.75 * t ** 2
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


# ------------------------------------------------------------------------------ shrine
def shrine_core(name="cm_shrine_core", seed=41):
    """Upper dais on the 2 x 2 solid cells (0.6 u), lower walkable step ring (0.25 u) r 1.5..3.0, two horns rising in
    the plane facing the gameplay camera, team crystals at the horn feet, waterfall on the camera side."""
    pc = Piece(name, team=True)
    rng = random.Random(seed)
    n = 16
    for k in range(n):
        t0, t1 = 2 * math.pi * k / n + 0.01, 2 * math.pi * (k + 1) / n - 0.01
        ri, ro = 1.45, 3.0 + rng.uniform(-0.05, 0.05)
        cs = [Vector((ri * math.cos(t0), ri * math.sin(t0), 0)), Vector((ri * math.cos(t1), ri * math.sin(t1), 0)),
              Vector((ro * math.cos(t1), ro * math.sin(t1), 0)), Vector((ro * math.cos(t0), ro * math.sin(t0), 0))]
        cs += [Vector((v.x, v.y, 0.25)) for v in cs]
        pc.block(cs, *kit.stone_side(rng, warm=True))
    for ix in (-0.5, 0.5):
        for iy in (-0.5, 0.5):
            side, top = kit.stone_side(rng, warm=True)
            pc.box(ix, iy, 0.0, 0.97, 0.97, 0.6, side, top, rot=math.radians(rng.uniform(-2, 2)))
    pc.lathe([(0.95, 0.6), (0.98, 0.66), (0.0, 0.66)], 8, topside(pal.vc("cap_side"), pal.vc("cap")),
             rot=math.pi / 8)
    # horns in the vertical plane of screen-right (sim NE-SW): blender direction (-1, -1)/sqrt2 = sim (1, -1)
    pdir = Vector((-1, -1, 0)).normalized()
    for sgn in (-1, 1):
        h = horn("tmp", seed + (3 if sgn > 0 else 7), plane_dir=pdir * sgn, base_off=0.78, height=4.1, z0=0.62,
                 segs=7, width=0.6, warm=True)
        _merge(pc, h)
        foot = pdir * sgn * 0.62
        kit.crystal_cluster(pc, foot.x - 0.25 * sgn, foot.y + 0.3 * sgn, 0.66, rng, height=0.75, n=4, spread=0.18,
                            base=False)
    # waterfall: from the upper tier down its sim-south face (+y blender) onto the step ring and into a pool
    w = 0.7
    pc.box(0, 0.98, 0.25, w, 0.08, 0.38, pal.vc("water"), pal.vc("foam"), bev=0.02)
    pc.box(0, 1.5, 0.25, w + 0.15, 1.0, 0.03, pal.vc("water"), pal.vc("water"), bev=0.0)
    pc.box(0, 1.0, 0.6, w + 0.1, 0.1, 0.05, pal.vc("foam"), pal.vc("foam"), bev=0.01)
    pc.box(0, 2.05, 0.0, w + 0.4, 0.5, 0.27, pal.vc("water"), pal.vc("foam"), bev=0.02)
    pc.lathe([(0.9, 0.0), (0.9, 0.04), (0.0, 0.04)], 16, solid(pal.vc("water")), cy=3.3)
    pc.lathe([(0.95, 0.0), (0.95, 0.03), (0.88, 0.05), (0.88, 0.0)], 16, solid(pal.vc("foam")), cy=3.3)
    return pc


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
    """Bible p14 faceted egg (1.5 x 2.3 u): dark panels with Void Navy lines over a self-lit #4FDDC3 core."""
    import bmesh
    pc = Piece(name, kind="glow")
    rx, rz = 0.74, 1.15
    pc.blob((0, 0, rz), rx * 0.97, rx * 0.97, rz * 0.97, pal.vc("egg_glow"), sub=2)
    t = bmesh.new()
    bmesh.ops.create_icosphere(t, subdivisions=1, radius=1.0)
    rng = random.Random(seed)
    for f in list(t.faces):
        cen = f.calc_center_median()
        pts = []
        for v in f.verts:
            p = cen + (v.co - cen) * 0.84
            p = p.normalized() * 1.0 if False else p
            pts.append(Vector((p.x * rx, p.y * rx, p.z * rz * (1.0 + 0.08 * max(0, p.z)) + rz)))
        c = pal.vc("egg") if rng.random() < 0.6 else pal.vc("egg_d")
        nrm = Vector((cen.x, cen.y, cen.z)).normalized()
        if _facing(pts, nrm) < 0:
            pts = pts[::-1]
        pc.poly([p + nrm * 0.015 for p in pts], c, thick=0.07)
    t.free()
    pc.lights.append((Vector((0, 0, 1.2)), "egg"))
    return pc


# ------------------------------------------------------------------------------ district dressing (stand-ins)
def crate(name="cm_dress_crate", s=0.7):
    pc = Piece(name, kind="prop")
    pc.box(0, 0, 0, s, s, s, pal.vc("wood_b"), pal.vc("wood"), bev=0.04)
    for z in (0.02, s - 0.1):
        pc.box(0, 0, z, s + 0.03, s + 0.03, 0.08, pal.vc("wood_d"), pal.vc("wood_d"), bev=0.015)
    return pc


def barrel(name="cm_dress_barrel"):
    pc = Piece(name, kind="prop")
    pc.lathe([(0.27, 0), (0.33, 0.25), (0.33, 0.55), (0.27, 0.8), (0.0, 0.8)], 10,
             topside(pal.vc("wood_b"), pal.vc("wood")), bev=0.0)
    for z in (0.15, 0.62):
        pc.lathe([(0.335, z), (0.335, z + 0.07)], 10, solid(pal.vc("brass_d")))
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
