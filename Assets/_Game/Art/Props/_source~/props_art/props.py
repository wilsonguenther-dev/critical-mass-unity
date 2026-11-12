"""Every prop in the set, one builder each, and the registry.

Bible p14 (props and interactables) and the p03 prop strip, plus neutral dressing for density. Sizes follow
props.md ("oversized for the high camera"); budgets: small 300, medium 800, large / interactable 1,500,
barricade 2,500 per state. Each builder returns a finished Blender object (one mesh, `Col` colour attribute).
"""
import math

from mathutils import Vector, noise

from .core import (BIO_HI, BIO_LO, BIOMASS, BRASS, CORRUPT, CORRUPT_D, CORRUPT_GLOW, CORRUPT_TIP, CREAM, DIRT,
                   DIRT_D, FLAME, FLAME_CORE, GRASS, GRASS_D, LAVA_GLOW, LAVA_ROCK, LEATHER, MASK_FIXED, MASK_GLOW, MASK_GLOW_LINED, MASK_TEAM_GLOW_LINED, MASK_MARK_LIGHT, MASK_MARK_DARK, MARK_LIGHT, MARK_DARK,
                   MASK_TEAM_CLOTH, MASK_TEAM_DARK, MASK_TEAM_GLOW, MOSS, NEUTRAL_HI, NEUTRAL_LO, ROPE, STALK,
                   STALK_L, STONE, STONE_CORE, STONE_D, TEAM_CLOTH, TEAM_DARK, TEAM_GLOW, WOOD, WOOD_B, WOOD_D, M,
                   Prop, box, cloth, cyl, ico, jitter, torus, tube, uvs)

SMALL, MEDIUM, LARGE, BARRICADE = 300, 800, 1500, 2500


def by_nz(hi, mid, lo, t1=0.45, t0=-0.25, mask=None):
    def f(face):
        z = face.normal.z
        h = hi if z > t1 else (mid if z > t0 else lo)
        return (h, mask) if mask is not None else h
    return f


def facet_light(lit, dark, mask_lit, mask_dark, lvec=(-0.55, -0.35, 0.75)):
    lv = Vector(lvec).normalized()

    def f(face):
        return (lit, mask_lit) if face.normal.dot(lv) > 0.15 else (dark, mask_dark)
    return f


def rock(P, r, loc, seed, col=STONE, sub=2, squash=0.7, rot=0.0, moss=False):
    bm = jitter(ico(r, sub), r * 0.28, 2.2 / r, seed)
    if col == STONE:
        top = MOSS if moss else STONE
        P.add(bm, lambda f: top if f.normal.z > 0.62 else STONE, mat=M(loc, (0, 0, rot), (1.0, 0.9, squash)))
    else:
        P.add(bm, col, mat=M(loc, (0, 0, rot), (1.0, 0.9, squash)))


def tuft(P, loc, h=0.09, n=3, seed=0, col=GRASS_D):
    for i in range(n):
        a = 2 * math.pi * i / n + seed
        P.add(cyl(0.025, h, 3, r2=0.0, bevel=0), col, mat=M(loc, (18 * math.cos(a), 18 * math.sin(a), a * 50)))


def dirt_base(P, r, seed, h=0.05):
    bm = jitter(cyl(r, h, 10, r2=r * 0.82, bevel=0.02), 0.03 * r / 0.3, 3.0, seed, zscale=0.2)
    P.add(bm, by_nz(DIRT, DIRT_D, DIRT_D, 0.6, -2))


# ---------------------------------------------------------------- p14 biomass plants
STALK_F = by_nz(STALK_L, STALK_L, STALK, 0.2, -0.35)   # v2: more lit mid-green (bible #419335)
VIEW_DIR = Vector((0.0, -0.8, 0.6)).normalized()
HL_DIR = Vector((-0.45, -0.5, 0.74)).normalized()     # painted glassy highlight sits upper left, facing the camera


def bio_glow(f):
    """Biomass blob: lime body, yellow top, deeper lime underside, and a painted highlight patch (v2: outlined)."""
    n = f.normal
    if n.dot(HL_DIR) > 0.88:
        return ("#F6FD58", MASK_GLOW_LINED)
    v = n.dot(VIEW_DIR)                     # per-blob rim so every lump reads inside the crown (bible cells)
    if v < 0.28:
        return ("#7DBF26" if n.z < 0.2 else "#93D82A", MASK_GLOW_LINED)
    return (BIO_HI if v > 0.8 and n.z > 0.2 else (BIOMASS if n.z > -0.3 else BIO_LO), MASK_GLOW_LINED)


def cells_glow(seed, ncell=14):
    """Biomass orb surface: lighter cells with darker borders (bible loose orb), plus a painted glassy highlight:
    a crescent along the upper-left rim and a round dot inside it (v2, sits inside the toon bands, never lit)."""
    import random as _r
    rr = _r.Random(seed)
    pts = []
    for _ in range(ncell):
        v = Vector((rr.gauss(0, 1), rr.gauss(0, 1), rr.gauss(0, 1))).normalized()
        pts.append(v)
    dot_dir = Vector((-0.35, -0.75, 0.56)).normalized()

    def f(face):
        c = face.calc_center_median()
        if c.length < 1e-6:
            return (BIOMASS, MASK_GLOW_LINED)
        n = face.normal
        if glassy(n):
            return ("#E4F29A", MASK_GLOW_LINED)
        d = sorted((c.normalized() - p).length for p in pts)
        if d[1] - d[0] < 0.09:
            return ("#66D119", MASK_GLOW_LINED)
        return ("#D3F73C" if n.z > 0.3 else (BIOMASS if n.z > -0.45 else "#A2CB5E"), MASK_GLOW_LINED)
    return f


def glassy(n):
    """Painted glassy highlight (v2): a crescent hugging the upper-left rim plus a round dot inside it, in face colour,
    so it sits inside the toon bands and never moves with the light. Bible p14 loose orb / p03 orbs and crystals."""
    dot_dir = Vector((-0.35, -0.75, 0.56)).normalized()
    h = n.dot(HL_DIR)
    return n.dot(dot_dir) > 0.965 or (0.5 < h < 0.72 and n.y < -0.05)


def fib_dirs(n, zmin=-1.0):
    out = []
    for i in range(n * 3):
        z = 1 - 2 * (i + 0.5) / (n * 3)
        if z < zmin:
            break
        a = i * 2.39996
        r = math.sqrt(max(0.0, 1 - z * z))
        out.append(Vector((r * math.cos(a), r * math.sin(a), z)))
        if len(out) == n:
            break
    return out


def bio_crown(P, c, rx, ry, rz, br, n, rng, seg=(7, 5), extra=()):
    """Cauliflower crown: n round blobs on an ellipsoid shell plus a filling core; the envelope is (rx, ry, rz)."""
    c = Vector(c)
    ix, iy, iz = max(rx - br, 0.3 * rx), max(ry - br, 0.3 * ry), max(rz - br, 0.35 * rz)
    P.add(uvs(1.0, *seg), bio_glow, smooth=True, mat=M(c, scale=(ix + br * 0.55, iy + br * 0.55, iz + br * 0.55)))
    for d in fib_dirs(n, zmin=-0.45):
        r = br * rng.uniform(0.86, 1.08)
        p = c + Vector((d.x * ix, d.y * iy, d.z * iz))
        P.add(uvs(r, *seg), bio_glow, smooth=True, mat=M(p, (0, 0, rng.uniform(0, 90))))
    for (dx, dz, r) in extra:                          # drooping side lobes (bible: crown hangs low on one side)
        P.add(uvs(r, *seg), bio_glow, smooth=True, mat=M(c + Vector((dx, -0.02, dz)), (0, 0, rng.uniform(0, 90))))


def bio_hook(P, root, ang, reach, z1, r0, rng, curl=1.0):
    """Fern-hook stalk: rises and leans out, then curls over and down at the tip (bible fiddleheads)."""
    pts, rad = [], []
    ca, sa = math.cos(ang), math.sin(ang)
    for k in range(8):
        t = k / 7
        d = reach * math.sin(t * math.pi * 0.5)
        z = root[2] + z1 * math.sin(t * math.pi * 0.62)
        if k >= 5:                                   # curl over: forward then back down and in
            q = (k - 4) / 3
            d += reach * 0.25 * q * curl
            z -= z1 * 0.22 * q * q * curl
        pts.append((root[0] + d * ca, root[1] + d * sa, z))
        rad.append(r0 * (1 - 0.6 * t) + 0.008)
    P.add(tube(pts, rad, 4, tip=True), STALK_F, smooth=True)


def bio_pod(P, root, top, r, rng):
    """Small round biomass pod on its own curved stalk (bible side globes)."""
    mid = ((root[0] + top[0]) / 2 * 1.1, (root[1] + top[1]) / 2, (root[2] + top[2]) / 2)
    P.add(tube([root, mid, (top[0], top[1], top[2] - r * 0.6)], [0.045, 0.034, 0.026], 5),
          STALK_F, smooth=True)
    P.add(uvs(r, 7, 5), bio_glow, smooth=True, mat=M(top, (0, 0, rng.uniform(0, 90)), (1, 1, 0.9)))


def biomass_plant(name, H, spec, seed):
    """spec: base (rx, ry), crown (cx, cz, rx, ry, rz, blob_r, n), trunk (n stalks, radius), hooks [(angle deg, reach,
    rise)], pods [(x, y, z, r)]. Proportions measured from the bible p14 crop (crown 0.6 x H wide on the large one)."""
    P = Prop(name, LARGE, "interactable", {"height": H}, seed)
    rng = P.rng
    bx, by = spec["base"]
    bm = jitter(cyl(1.0, 0.05, 12, r2=0.86, bevel=0.0), 0.04, 3.0, seed, zscale=0.2)
    P.add(bm, by_nz(DIRT, DIRT_D, DIRT_D, 0.6, -2), mat=M(scale=(bx, by, 1.0)))
    cx, cz, crx, cry, crz, br, nb = spec["crown"]
    ns, sr = spec["trunk"]
    for i in range(ns):                                   # thick trunk stalks, flaring into the crown
        a = 2 * math.pi * (i + 0.5) / ns + rng.uniform(-0.2, 0.2)
        r0, r1 = bx * 0.32, crx * 0.45
        top = cz - crz * 0.55
        pts = []
        for k in range(6):
            t = k / 5
            rr = r0 + (r1 - r0) * t ** 2.2 - 0.04 * math.sin(t * math.pi)
            pts.append((cx * t ** 1.5 + rr * math.cos(a), rr * math.sin(a) * 0.8, 0.02 + top * t))
        rad = [sr * (1.15 - 0.45 * k / 5) for k in range(6)]
        P.add(tube(pts, rad, 5), STALK_F, smooth=True)
    for (adeg, reach, rise) in spec["hooks"]:
        a = math.radians(adeg)
        bio_hook(P, (bx * 0.25 * math.cos(a), bx * 0.2 * math.sin(a), 0.02), a, reach, rise, sr * 0.75, rng)
    bio_crown(P, (cx, 0.0, cz), crx, cry, crz, br, nb, rng, extra=spec.get("lobes", ()))
    for (x, y, z, r) in spec["pods"]:
        bio_pod(P, (x * 0.35, y * 0.3, 0.02), (x, y, z), r, rng)
    rock(P, bx * 0.2, (bx * 0.35, -by * 0.75, 0.02), seed + 1, sub=1)
    rock(P, bx * 0.12, (-bx * 0.55, -by * 0.6, 0.02), seed + 2, sub=1)
    for j in range(4):
        a = 1.6 * j + 0.7
        tuft(P, (bx * 0.8 * math.cos(a), by * 0.85 * math.sin(a), 0.03), h=0.1, seed=j + seed, col="#47963A")
    return P.finish(fit_height=H)


BIO_SPECS = {   # measured off the bible crop: 1 col = 0.024 m (see props-v2/REPORT.md)
    "s": dict(base=(0.24, 0.17), crown=(0.03, 0.40, 0.15, 0.12, 0.11, 0.075, 7), trunk=(3, 0.04),
              hooks=[(200, 0.1, 0.15), (330, 0.11, 0.14)], pods=[(0.16, -0.04, 0.3, 0.07), (-0.13, -0.06, 0.2, 0.06)]),
    "m": dict(base=(0.31, 0.21), crown=(0.16, 0.63, 0.25, 0.2, 0.16, 0.12, 8), trunk=(4, 0.055),
              hooks=[(190, 0.13, 0.26), (330, 0.18, 0.26), (10, 0.3, 0.45)],
              pods=[(-0.15, 0.0, 0.48, 0.085), (0.3, -0.06, 0.3, 0.075), (-0.08, -0.1, 0.15, 0.065)]),
    "l": dict(base=(0.40, 0.27), crown=(0.08, 0.89, 0.40, 0.3, 0.29, 0.17, 8), trunk=(4, 0.075),
              lobes=[(-0.29, -0.14, 0.12), (0.3, -0.3, 0.11), (-0.2, -0.32, 0.1)],
              hooks=[(185, 0.13, 0.3), (340, 0.2, 0.36), (15, 0.2, 0.42), (160, 0.18, 0.32)],
              pods=[(0.17, -0.06, 0.42, 0.11), (-0.22, -0.1, 0.16, 0.08), (0.27, -0.05, 0.14, 0.06)]),
}


def biomass_sprout():
    P = Prop("prop_biomass_sprout", SMALL, "small", {"height": 0.25}, 7, walkable=True)
    glow = by_nz(BIO_HI, BIOMASS, BIO_LO, 0.5, -0.3, MASK_GLOW)
    P.add(jitter(cyl(0.13, 0.03, 7, r2=0.11, bevel=0.01), 0.01, 4, 7, 0.2), DIRT)
    for (x, y, h, r) in ((0.0, 0.0, 0.16, 0.075), (0.08, -0.05, 0.09, 0.055)):
        P.add(tube([(x, y, 0.02), (x + 0.01, y, h * 0.6), (x, y, h)], [0.022, 0.018, 0.015], 4), STALK)
        P.add(uvs(r, 8, 5), glow, smooth=True, mat=M((x, y, h + r * 0.6), scale=(1, 1, 0.85)))
    for i in range(3):
        a = 2.1 * i + 0.3
        P.add(tube([(0, 0, 0.02), (0.07 * math.cos(a), 0.07 * math.sin(a), 0.06),
                    (0.12 * math.cos(a), 0.12 * math.sin(a), 0.04)], [0.025, 0.02, 0], 3, tip=True), STALK_L)
    return P.finish(fit_height=0.25)


# ---------------------------------------------------------------- orbs (pickups) and carried bundle
def orb_biomass():
    P = Prop("prop_orb_biomass", LARGE, "interactable", {"height": 0.45, "width": 0.45}, 3)
    P.add(uvs(0.225, 16, 10), cells_glow(3), smooth=True, mat=M((0, 0, 0.225)))
    return P.finish(fit_height=0.45)


def orb_stone():
    P = Prop("prop_orb_stone", LARGE, "interactable", {"height": 0.45, "width": 0.45}, 4)
    bm = jitter(ico(0.215, 3), 0.014, 9, 4)
    P.add(bm, lambda f: MOSS if (f.normal.z > 0.62 and noise.noise(f.calc_center_median() * 9) > -0.25)
          else (STONE if f.normal.z > -0.3 else STONE_D), smooth=False, mat=M((0, 0, 0.225)))
    return P.finish(fit_height=0.45)


def orb_lava():
    P = Prop("prop_orb_lava", LARGE, "interactable", {"height": 0.45, "width": 0.45}, 5)
    bm = jitter(ico(0.215, 3), 0.014, 9, 5)

    def col(f):
        c = f.calc_center_median()
        return (LAVA_GLOW, MASK_GLOW) if abs(noise.noise(c * 7.0 + Vector((3, 1, 2)))) < 0.11 else LAVA_ROCK
    P.add(bm, col, mat=M((0, 0, 0.225)))
    return P.finish(fit_height=0.45)


def orb_corruption():
    P = Prop("prop_orb_corruption", LARGE, "interactable", {"height": 0.45, "width": 0.45}, 6)

    def col(f):
        c = f.calc_center_median()
        s = math.sin(math.atan2(c.y, c.x) * 2 + c.z * 22)
        h = "#B57BE6" if s > 0.55 else (CORRUPT_TIP if s > -0.3 else "#5E2391")
        if f.normal.z > 0.7:
            h = "#C99AF0"
        if glassy(f.normal):
            h = "#EAD6FA"
        return (h, MASK_GLOW_LINED)
    P.add(uvs(0.225, 14, 9), col, smooth=True, mat=M((0, 0, 0.225)))
    return P.finish(fit_height=0.45)


def orb_brass():
    P = Prop("prop_orb_brass", LARGE, "interactable", {"height": 0.45, "width": 0.45}, 8)
    c = (0, 0, 0.225)
    P.add(uvs(0.2, 12, 8), by_nz("#B07F3E", "#9E7038", "#86602F", 0.5, -0.4), smooth=True, mat=M(c))
    P.add(torus(0.205, 0.024, 16, 4), BRASS, mat=M(c))
    P.add(torus(0.205, 0.024, 16, 4), BRASS, mat=M(c, (90, 0, 20)))
    for a in (20, 110, 200, 290):
        P.add(uvs(0.035, 6, 4), "#E0B060", smooth=True,
              mat=M((0.205 * math.cos(math.radians(a)), 0.205 * math.sin(math.radians(a)), 0.225)))
    return P.finish(fit_height=0.45)


def carried_bundle():
    P = Prop("prop_carried_bundle", LARGE, "interactable", {"height": 0.55, "width": 0.5}, 9)
    c = (0, 0, 0.26)
    P.add(uvs(0.215, 14, 9), cells_glow(9, 10), smooth=True, mat=M(c))

    def planks_(f):
        a = math.atan2(f.calc_center_median().z - 0.26, f.calc_center_median().x)
        return WOOD if int((a + 4) * 2.2) % 2 else WOOD_B
    P.add(torus(0.215, 0.034, 16, 5), planks_, mat=M(c, (90, 0, 0)))          # plank ring around the orb
    P.add(torus(0.205, 0.026, 16, 4), WOOD_D, mat=M((0, 0, 0.19)))            # band round the lower middle
    P.add(torus(0.22, 0.018, 16, 4), LEATHER, mat=M(c, (90, 0, 90)))          # strap over the top
    for (x, z) in ((-0.205, 0.19), (0.205, 0.19)):
        P.add(box(0.07, 0.09, 0.08, bevel=0.02, base=False), BRASS, mat=M((x, 0, z)))
    P.add(box(0.09, 0.09, 0.06, bevel=0.02, base=False), BRASS, mat=M((0, 0, 0.485)))
    return P.finish(fit_height=0.55)


# ---------------------------------------------------------------- corruption growth
CORR_HL = "#CDA8C4"          # v2: pale lilac glassy highlight on the plates (sampled from the bible crop, #CAA9B9)


def corr_plates(seed, H):
    """Dark plates (lit magenta-purple / shadow indigo) with glowing magenta cracks between them, plus a painted
    lilac highlight on the upper-left of the plates. Bible p14: scaled flame of crystal plates, glow in the seams."""
    import random as _r
    rr = _r.Random(seed)
    pts = [Vector((rr.uniform(-0.4, 0.4), rr.uniform(-0.25, 0.25), rr.uniform(0, 1.25))) * (H / 1.2) for _ in range(46)]

    def f(face):
        c = face.calc_center_median()
        n = face.normal
        d = sorted((c - p).length for p in pts)
        if d[1] - d[0] < 0.016 * H / 1.2 and n.y < 0.3:
            return (CORRUPT_GLOW, MASK_GLOW)
        if n.dot(HL_DIR) > 0.7:
            return (CORR_HL, MASK_FIXED)
        return ("#4E3D85" if n.x > 0.15 or n.z < -0.3 else "#6B1761", MASK_FIXED)   # shadow side indigo, lit side plum
    return f


def flame(P, spine, radii, colf, depth=0.62, sides=8, inner=True):
    """One flowing flame tongue: a tapered tube along an S-curved spine, flattened front to back, with a glowing core
    pushed out of its front face (the magenta seam the bible shows up the middle of each tongue)."""
    pts = [(x, 0.0, z) for (x, z) in spine]
    P.add(tube(pts, radii, sides, tip=True), colf, smooth=True, mat=M(scale=(1, depth, 1)))
    if inner:
        n = len(pts)
        core = [(p[0], -radii[k] * depth * 0.55, p[2]) for k, p in enumerate(pts)][: n - 1]
        P.add(tube(core, [r * 0.3 for r in radii[: n - 1]], 5, tip=True),
              lambda f: (CORRUPT_TIP if f.calc_center_median().z > spine[-1][1] * 0.75 else CORRUPT_GLOW, MASK_GLOW),
              smooth=True)


# measured off the bible crop at H = 1.2 (1 col = 0.019 m): body half-width 0.30 to z 0.6, right edge upright to
# z 0.88, a thin tendril to z 1.2 whose tip hooks over to the right
CORR_MAIN = dict(
    spine=[(0.0, 0.03), (0.0, 0.3), (0.01, 0.55), (0.07, 0.74), (0.1, 0.88), (0.08, 0.97), (0.02, 1.05), (0.01, 1.13),
           (0.05, 1.19), (0.09, 1.18), (0.11, 1.13)],
    radii=[0.24, 0.27, 0.25, 0.17, 0.12, 0.08, 0.06, 0.05, 0.04, 0.03, 0.0],
    licks=[  # (spine, radii): side tongues that rise, lean out, and curl their tips back in
        ([(-0.1, 0.05), (-0.17, 0.3), (-0.2, 0.55), (-0.17, 0.74), (-0.12, 0.86), (-0.07, 0.9)], [0.11, 0.11, 0.09, 0.06, 0.035, 0.0]),
        ([(0.14, 0.05), (0.22, 0.35), (0.25, 0.62), (0.26, 0.82), (0.22, 0.93), (0.17, 0.95)], [0.1, 0.1, 0.085, 0.06, 0.035, 0.0]),
        ([(-0.18, 0.03), (-0.23, 0.14), (-0.24, 0.25), (-0.21, 0.32)], [0.06, 0.055, 0.035, 0.0]),
    ],
    rocks=[(-0.3, -0.04, 0.1), (-0.11, -0.2, 0.13), (0.1, -0.2, 0.1), (0.28, -0.06, 0.09), (0.02, 0.13, 0.12),
           (-0.22, 0.1, 0.08), (0.36, 0.05, 0.07)],
)


def corruption(name, H, seed, spec=CORR_MAIN):
    P = Prop(name, LARGE, "interactable", {"height": H}, seed)
    k = H / 1.2
    for i, (x, y, r) in enumerate(spec["rocks"]):
        rock(P, r * k, (x * k, y * k, r * k * 0.3), seed + i, col=STONE_D, sub=1, squash=0.7)
    colf = corr_plates(seed, H)
    sc = lambda pts: [(x * k, z * k) for (x, z) in pts]
    flame(P, sc(spec["spine"]), [r * k for r in spec["radii"]], colf)
    for (sp, rad) in spec["licks"]:
        flame(P, sc(sp), [r * k for r in rad], colf, sides=6, inner=len(sp) > 4)
    return P.finish(fit_height=H)


# ---------------------------------------------------------------- team crystals
def crystal(P, h, r, loc, lean=(0, 0), rot=0.0):
    bm = tube([(0, 0, -0.02), (0, 0, h * 0.68), (0, 0, h)], [r * 0.85, r, 0.0], 6, tip=True)
    lit = facet_light(TEAM_GLOW, TEAM_DARK, MASK_TEAM_GLOW_LINED, MASK_TEAM_DARK)
    top = loc[2] + h * 0.45

    def col(f):                     # v2: a painted pale highlight on the upper half of the most lit facet
        if f.normal.dot(HL_DIR) > 0.55 and f.calc_center_median().z > top:
            return ("#E6FCFF", MASK_GLOW_LINED)
        return lit(f)
    P.add(bm, col, mat=M(loc, (lean[0], lean[1], rot)))


def crystal_cluster(name, H, seed):
    P = Prop(name, LARGE, "interactable", {"height": H}, seed)
    k = H / 0.9
    rock(P, 0.17 * k, (0, 0.02, 0.03), seed, squash=0.55)
    crystal(P, 0.9 * k, 0.11 * k, (0, 0, 0), (0, -6, 0))
    for i, (a, s, lean) in enumerate(((200, 0.6, 28), (330, 0.5, 30), (95, 0.45, 32), (20, 0.38, 35))):
        ar = math.radians(a)
        crystal(P, 0.9 * k * s, 0.08 * k, (0.12 * k * math.cos(ar), 0.1 * k * math.sin(ar), 0.0),
                (-lean * math.sin(ar), lean * math.cos(ar)), a)
    return P.finish(fit_height=H)


# ---------------------------------------------------------------- barricade (Miner, one cell, three states)
# v2 palette additions sampled from the bible p14 barricade crops: the pale cream the bible paints on every plank top
# and bevel (#EACAA1 / #EDD6B6 / #E4BC8B) and the darker post brown (#724B2D after the mid band)
PLANK_CREAM, POST_BROWN = "#EACAA1", "#946036"


def wood_hl(base):
    return lambda f: PLANK_CREAM if f.normal.z > 0.7 else base
def _post(P, x, h=0.9, lean=0.0, broken=False):
    m = M((x, 0, 0), (0, lean, 0))
    P.add(box(0.16, 0.16, h), wood_hl(POST_BROWN), mat=m)
    P.add(box(0.36, 0.17, 0.16), by_nz(STONE_CORE, STONE_CORE, STONE_CORE, 0.5), mat=M((x + 0.03 * (1 if x > 0 else -1), 0.03, 0)))
    P.add(box(0.19, 0.19, 0.06), STONE_CORE, mat=m @ M((0, 0, h * 0.62)))
    if broken:
        P.add(box(0.16, 0.16, 0.1, bevel=0.02), wood_hl(WOOD_B), mat=m @ M((0.02, 0, h), (0, 25, 10)))
    else:
        P.add(box(0.2, 0.2, 0.08), STONE_CORE, mat=m @ M((0, 0, h)))


def barricade_deployed():
    P = Prop("prop_barricade_deployed", BARRICADE, "barricade", {"height": 0.98, "width": 1.76}, 11)
    X = 0.68
    _post(P, -X)
    _post(P, X)
    P.add(box(1.22, 0.09, 0.09), wood_hl(WOOD), mat=M((0, 0, 0.66)))
    P.add(box(1.22, 0.08, 0.09), wood_hl(WOOD_B), mat=M((0, 0.03, 0.06)))
    for s in (1, -1):
        P.add(box(1.28, 0.06, 0.08), WOOD_D, mat=M((0, 0.05, 0.22), (0, s * 13, 0)))
    P.add(cloth(1.16, 0.44, 10, 4, lambda u, v: -0.06 * math.sin(math.pi * u) * v, notch=0.2), TEAM_CLOTH,
          mask=MASK_TEAM_CLOTH, smooth=True, mat=M((0, -0.06, 0.66)))
    pts = [(-X + 2 * X * t, -0.09, 0.84 - 0.18 * math.sin(math.pi * t)) for t in [i / 11 for i in range(12)]]
    P.add(tube(pts, [0.026] * 12, 4), ROPE, smooth=True)
    for x in (-X, X):
        P.add(torus(0.1, 0.024, 10, 4), ROPE, mat=M((x, 0, 0.8)))
    return P.finish()


def barricade_folded():
    P = Prop("prop_barricade_folded", BARRICADE, "barricade", {"height": 0.6, "width": 0.95}, 12)
    W, D, H = 0.95, 0.34, 0.56          # v2: long low stack of folded panels (bible p14 proportions)
    P.add(box(W - 0.1, D - 0.1, 0.1), TEAM_CLOTH, mask=MASK_TEAM_CLOTH, mat=M((0, 0, H - 0.08)))
    for sx in (-1, 1):
        for sy in (-1, 1):
            P.add(box(0.09, 0.09, H), WOOD_D, mat=M((sx * (W / 2 - 0.045), sy * (D / 2 - 0.045), 0)))
            for z in (0.0, H - 0.07):
                P.add(box(0.12, 0.12, 0.07, bevel=0.02), STONE_CORE,
                      mat=M((sx * (W / 2 - 0.045), sy * (D / 2 - 0.045), z)))
    for sy in (-1, 1):
        for j in range(3):
            P.add(box(W - 0.16, 0.06, 0.15), wood_hl(WOOD if j % 2 else WOOD_B),
                  mat=M((0, sy * (D / 2 - 0.03), 0.04 + j * 0.165)))
        for s in (1, -1):
            P.add(box(W - 0.06, 0.05, 0.07), WOOD_D, mat=M((0, sy * (D / 2 + 0.01), 0.25), (0, s * 23, 0)))
    for sx in (-1, 1):
        for j in range(3):
            P.add(box(0.06, D - 0.16, 0.15), wood_hl(WOOD_B), mat=M((sx * (W / 2 - 0.03), 0, 0.04 + j * 0.165)))
    P.add(tube([(-0.2, -D / 2 - 0.02, H + 0.0), (-0.2, 0, H + 0.04), (-0.2, D / 2 + 0.02, H)], [0.02] * 3, 4), ROPE)
    P.add(tube([(0.2, -D / 2 - 0.02, H + 0.0), (0.2, 0, H + 0.04), (0.2, D / 2 + 0.02, H)], [0.02] * 3, 4), ROPE)
    return P.finish()


def barricade_damaged():
    """v2, matched to the bible p14 crop: both posts still stand (the right one snapped at the top), the top beam hangs
    diagonally from the right post down to the left, a low beam sags across the middle, the cloth is torn and hangs
    from the diagonal, and the broken planks lie in front of the right post."""
    P = Prop("prop_barricade_damaged", BARRICADE, "barricade", {"height": 0.95}, 13)
    X = 0.64
    _post(P, -X, 0.86)
    _post(P, X, 0.84, broken=True)
    P.add(box(1.12, 0.09, 0.09), wood_hl(WOOD), mat=M((0.1, 0, 0.6), (0, -22, 0)))          # diagonal top beam
    P.add(box(0.78, 0.09, 0.09), wood_hl(WOOD_B), mat=M((-0.28, -0.02, 0.24), (0, -9, 0)))     # low sagging beam
    P.add(box(0.5, 0.06, 0.08), WOOD_D, mat=M((-0.4, 0.05, 0.42), (0, -30, 0)))
    rng = P.rng

    def torn(u, v):
        n = noise.noise(Vector((u * 4.0, v * 3.0, 1.7)))
        return n > 0.3 or (v > 0.75 and rng.random() < 0.45)
    P.add(cloth(0.95, 0.56, 10, 5, lambda u, v: -0.05 * math.sin(math.pi * u) * v, notch=0.12, cut=torn), TEAM_CLOTH,
          mask=MASK_TEAM_CLOTH, smooth=True, mat=M((0.06, -0.07, 0.66), (0, -22, 0)))
    for (x, y, rz, l) in ((0.45, -0.16, 18, 0.46), (0.62, -0.22, -24, 0.4), (0.78, -0.1, 40, 0.3)):
        P.add(box(l, 0.1, 0.045, bevel=0.015), wood_hl(WOOD if l > 0.4 else WOOD_B), mat=M((x, y, 0), (0, 0, rz)))
    for (x, ry, l) in ((-0.3, 38, 0.52), (-0.02, -42, 0.5), (0.24, 32, 0.46)):   # planks knocked against the low beam
        P.add(box(l, 0.1, 0.045, bevel=0.015), wood_hl(WOOD_B), mat=M((x, -0.12, 0.17), (0, ry, 0)))
    P.add(tube([(X - 0.05, -0.1, 0.8), (X - 0.12, -0.13, 0.56), (X - 0.14, -0.14, 0.36)], [0.024] * 3, 4), ROPE)
    rock(P, 0.06, (0.1, -0.2, 0.01), 31, col=STONE_CORE, squash=0.5, sub=1)
    return P.finish()


# ---------------------------------------------------------------- banner (team cloth)
# v2 palette additions sampled from the bible p14 banner crops (median-cut swatches of the crop foreground):
POLE_BROWN = "#6B4B2E"      # pole and crossbar body; lands on the bible's dark brown #5C513F after the mid band
BAR_BROWN_D = "#4F3A26"     # dark brown end grain, rope shadow (bible #694533 on the crimson crop)
ROPE_CREAM = "#E2D08E"      # rope wraps and tassels, bible highlight cream #D9C985 / #DDCE7C
EMBLEM_CREAM = "#F3EBCF"    # emblem, bible warm cream highlight


def banner():
    """Bible p14 banner: the pole stands at the left third, the crossbar (1.12 m) and the cloth hang centred 0.22 m to
    its right; rope wraps and tassels at both bar ends; tattered tails; wide mossy rock pile (1.2 m) at the foot."""
    P = Prop("prop_banner", LARGE, "interactable", {"height": 1.9}, 14)
    for i, (x, y, r) in enumerate(((-0.02, 0.08, 0.24), (0.32, -0.02, 0.2), (-0.3, -0.04, 0.21), (0.12, -0.24, 0.13),
                                   (0.45, 0.04, 0.1), (-0.5, 0.06, 0.1))):
        rock(P, r, (x, y, r * 0.25), 40 + i, squash=0.62, moss=True, sub=1)
    for j, (x, y) in enumerate(((-0.42, -0.2), (0.45, -0.18), (0.2, -0.32))):
        tuft(P, (x, y, 0.02), h=0.1, seed=j, col=GRASS)
    P.add(box(0.13, 0.13, 1.8), by_nz(POLE_BROWN, POLE_BROWN, BAR_BROWN_D, 0.5), mat=M((0.0, 0.06, 0)))
    bx, bz, L = 0.22, 1.76, 1.08
    cx = bx + 0.03                                        # cloth centre
    P.add(cyl(0.075, L, 8, bevel=0.025, base=False),
          lambda f: BAR_BROWN_D if abs(f.normal.x) > 0.9 else POLE_BROWN, mat=M((bx, 0.0, bz), (0, 90, 0)))
    for s in (-1, 1):
        x = cx + s * 0.37
        for w in (-0.035, 0.035):
            P.add(torus(0.074, 0.018, 10, 4), ROPE_CREAM, mat=M((x + w, 0, bz), (0, 90, 0)))
        P.add(tube([(x, -0.07, bz - 0.04), (x + 0.01 * s, -0.08, bz - 0.18), (x, -0.08, bz - 0.3)], [0.024, 0.022, 0.02], 4),
              ROPE_CREAM)
        P.add(cyl(0.035, 0.08, 6, r2=0.012, bevel=0.0), BAR_BROWN_D, mat=M((x, -0.08, bz - 0.38)))
    P.add(cyl(0.08, 0.07, 8, r2=0.04, bevel=0.015), BRASS, mat=M((0.0, 0.06, 1.8)))
    rag = lambda u: 0.3 * (1 - math.exp(-((u - 0.5) / 0.2) ** 2)) - 0.12 * max(0.0, u - 0.8) / 0.2 + 0.06 * abs(math.sin(u * math.pi * 5))
    cl = cloth(0.78, 1.42, 8, 11, lambda u, v: -0.035 * math.sin(u * math.pi * 3) * v - 0.03 * v, notch=0.0, bottom_fn=rag)
    for v in cl.verts:                                    # taper: 0.78 m at the bar, about 0.6 m at the tails
        v.co.x *= 1 + 0.22 * v.co.z / 1.42
    P.add(cl, lambda f: TEAM_CLOTH, mask=MASK_TEAM_CLOTH, smooth=True, mat=M((cx, -0.1, bz - 0.04)))
    P.add(box(0.84, 0.05, 0.1, bevel=0.02), TEAM_CLOTH, mask=MASK_TEAM_CLOTH, mat=M((bx, -0.09, bz - 0.14)))  # sleeve hem
    ez, ey = 1.2, -0.15
    P.add(torus(0.15, 0.04, 16, 4), EMBLEM_CREAM, mat=M((bx, ey, ez), (90, 0, 0)))
    for k in range(6):
        a = 360 * k / 6 + 30
        P.add(cyl(0.04, 0.09, 3, r2=0.0, bevel=0), EMBLEM_CREAM,
              mat=M((bx + 0.18 * math.cos(math.radians(a)), ey, ez + 0.18 * math.sin(math.radians(a))), (0, 90 - a, 0)))
    for dx in (-0.035, 0.035):
        P.add(tube([(bx + dx, ey, ez - 0.09), (bx + dx * 1.6, ey, ez), (bx + dx, ey, ez + 0.09)], [0.0, 0.03, 0.0], 4),
              EMBLEM_CREAM)
    return P.finish(fit_height=1.9)


# ---------------------------------------------------------------- markers (diamond over ring)
def marker(team=True):
    """Bible p14 marker, measured off the crop (1 col = 0.028 m): a four-facet diamond 0.37 m wide with a ridge to the
    camera, tip 0.19 m above the ground, widest at 0.5, top 0.87; a broken flat ring (r 0.24) with a centre dot on the
    ground below it. Left facets light, right facets dark, a pale star highlight at the widest point."""
    name = "prop_marker_team" if team else "prop_marker_neutral"
    P = Prop(name, LARGE, "interactable", {"height": 0.9}, 15, outline_px=0.0)
    if team:
        lit, dark, ml, md = MARK_LIGHT, MARK_DARK, MASK_MARK_LIGHT, MASK_MARK_DARK
    else:
        lit, dark, ml, md = NEUTRAL_HI, NEUTRAL_LO, MASK_GLOW, MASK_GLOW
    for k in range(4):                                    # broken ring: four arcs with gaps
        P.add(torus(0.15, 0.05, 6, 4, arc=76, start=90 * k + 7), (dark, md), lined=False,
              mat=M((0, -0.28, 0.025), scale=(1, 1, 0.45)))
    P.add(cyl(0.055, 0.012, 10, bevel=0), (lit, ml), lined=False, mat=M((0, -0.28, 0)))
    z0, zw, z1, r = 0.1, 0.47, 0.9, 0.19
    bm = tube([(0, 0, z0), (0, 0, zw), (0, 0, z1)], [0.0001, r, 0.0], 4, tip=True, twist=0.0)

    def facet(f):
        n, c = f.normal, f.calc_center_median()
        return (lit, ml) if n.x < -0.05 else (dark, md)
    P.add(bm, facet, lined=False, mat=M((0, 0, 0), (0, 0, 0), (1.0, 0.75, 1)))
    hl = "#FFFFFF" if team else "#F4F1EC"
    for (sx, sz) in ((0.06, 0.012), (0.012, 0.09)):      # four-point star highlight on the front ridge
        P.add(box(sx, 0.01, sz, bevel=0, base=False), (hl, MASK_GLOW), lined=False, mat=M((-0.012, -r * 0.75 * 0.72 - 0.004, zw)))
    return P.finish(fit_height=0.9)


# ---------------------------------------------------------------- neutral dressing
def crate():
    P = Prop("prop_crate", MEDIUM, "medium", {"height": 0.6, "width": 0.6}, 16)
    S, g, t = 0.6, 0.016, 0.08
    P.add(box(S - 0.1, S - 0.1, S - 0.04, bevel=0), WOOD_D)
    bh = (S - 2 * g) / 3
    for j in range(3):
        z = j * (bh + g)
        c = WOOD if j % 2 == 0 else WOOD_B
        for sy in (-1, 1):
            P.add(box(S, t, bh), c, mat=M((0, sy * (S / 2 - t / 2), z)))
        for sx in (-1, 1):
            P.add(box(t, S - 2 * t - 0.01, bh), WOOD_B if j % 2 == 0 else WOOD, mat=M((sx * (S / 2 - t / 2), 0, z)))
    bw = (S - 2 * t - 2 * g) / 3
    for i in range(3):
        P.add(box(S - 2 * t, bw, 0.05), WOOD if i != 1 else WOOD_B, mat=M((0, (i - 1) * (bw + g), S - 0.07)))
    return P.finish(fit_height=0.6)


def barrel():
    P = Prop("prop_barrel", MEDIUM, "medium", {"height": 0.7, "width": 0.5}, 17)
    import bmesh
    n, H = 10, 0.7
    rz = lambda z: 0.215 + 0.035 * math.sin(math.pi * z / H)
    P.add(cyl(0.2, H - 0.02, 10, bevel=0), WOOD_D)
    for k in range(n):
        a0, a1 = 2 * math.pi * (k + 0.06) / n, 2 * math.pi * (k + 0.94) / n
        bm = bmesh.new()
        rings = []
        for z in (0.0, H / 2, H):
            ro, ri = rz(z), rz(z) - 0.045
            rings.append([bm.verts.new((r * math.cos(a), r * math.sin(a), z))
                          for (r, a) in ((ro, a0), (ro, a1), (ri, a1), (ri, a0))])
        for i in range(2):
            A, B = rings[i], rings[i + 1]
            for q in range(4):
                bm.faces.new((A[q], A[(q + 1) % 4], B[(q + 1) % 4], B[q]))
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        P.add(bm, WOOD if k % 2 else WOOD_B)
    for z in (0.14, H - 0.14):
        P.add(torus(rz(z) + 0.012, 0.022, 12, 4), BRASS, mat=M((0, 0, z), scale=(1, 1, 1.4)))
    P.add(cyl(0.185, 0.03, 10, bevel=0), WOOD_B, mat=M((0, 0, H - 0.07)))
    for i in (-1, 0, 1):
        P.add(box(0.11, 0.3 - abs(i) * 0.08, 0.025, bevel=0.01), WOOD if i else WOOD_B,
              mat=M((i * 0.12, 0, H - 0.045)))
    return P.finish(fit_height=0.7)


def stone_block():
    P = Prop("prop_stone_block", MEDIUM, "medium", {"height": 0.6, "width": 0.6}, 18)
    import bmesh
    bm = box(0.6, 0.6, 0.56, bevel=0.05)
    bmesh.ops.subdivide_edges(bm, edges=[e for e in bm.edges if e.calc_length() > 0.3], cuts=1, use_grid_fill=True)
    jitter(bm, 0.018, 6, 18)
    P.add(bm, by_nz(STONE, STONE, STONE_D, 0.5, -0.5))
    P.add(jitter(cyl(0.26, 0.05, 8, r2=0.22, bevel=0.015), 0.03, 5, 3, 0.3), MOSS, mat=M((0.03, 0.02, 0.55)))
    return P.finish(fit_height=0.6)


def stone_stack():
    P = Prop("prop_stone_stack", MEDIUM, "medium", {"height": 0.85}, 19)
    P.add(box(0.5, 0.45, 0.4, bevel=0.045), by_nz(STONE, STONE, STONE_D, 0.5), mat=M((-0.26, 0, 0), (0, 0, 4)))
    P.add(box(0.48, 0.45, 0.4, bevel=0.045), by_nz(STONE_CORE, STONE_CORE, STONE_D, 0.5), mat=M((0.25, 0.02, 0), (0, 0, -3)))
    P.add(box(0.52, 0.42, 0.42, bevel=0.045), by_nz(STONE, STONE, STONE_D, 0.5), mat=M((0.02, 0.03, 0.41), (0, 0, 9)))
    P.add(jitter(cyl(0.16, 0.04, 7, bevel=0.01), 0.02, 5, 2, 0.3), MOSS, mat=M((0.06, 0.04, 0.82)))
    return P.finish(fit_height=0.85)


def log_section():
    P = Prop("prop_log_section", MEDIUM, "medium", {"height": 0.5, "width": 0.5}, 20)
    P.add(cyl(0.23, 0.46, 12, bevel=0.035), lambda f: WOOD if f.normal.z > 0.9 else
          (WOOD_B if int((math.atan2(f.normal.y, f.normal.x) + 4) * 1.9) % 2 else "#9A6233"))
    P.add(torus(0.12, 0.012, 12, 3), WOOD_B, mat=M((0, 0, 0.462)))
    P.add(cyl(0.035, 0.012, 6, bevel=0), WOOD_D, mat=M((0.02, 0.01, 0.46)))
    P.add(box(0.16, 0.03, 0.08, bevel=0.012), WOOD_D, mat=M((0.1, -0.215, 0.18), (0, 0, -18)))
    return P.finish(fit_height=0.5)


def log():
    P = Prop("prop_log", MEDIUM, "medium", {"height": 0.3, "length": 1.1}, 21)
    P.add(cyl(0.15, 1.1, 10, bevel=0.035, base=False), lambda f: WOOD if abs(f.normal.x) > 0.9 else
          (WOOD_B if f.normal.z > 0.3 else "#9A6233"), mat=M((0, 0, 0.15), (0, 90, 0)))
    P.add(tube([(0.1, 0, 0.24), (0.16, -0.06, 0.28), (0.22, -0.1, 0.29)], [0.045, 0.035, 0.02], 5), WOOD_B)
    return P.finish()


def stump():
    P = Prop("prop_stump", SMALL, "small", {"height": 0.3}, 22)
    P.add(cyl(0.2, 0.28, 8, r2=0.18, bevel=0.03), lambda f: WOOD if f.normal.z > 0.9 else WOOD_B)
    for i in range(3):
        a = 2.1 * i + 0.5
        P.add(tube([(0.12 * math.cos(a), 0.12 * math.sin(a), 0.1), (0.25 * math.cos(a), 0.25 * math.sin(a), 0.02),
                    (0.3 * math.cos(a), 0.3 * math.sin(a), 0.0)], [0.06, 0.04, 0.0], 4, tip=True), WOOD_B)
    return P.finish(fit_height=0.3)


def planks():
    P = Prop("prop_planks", SMALL, "small", {}, 23, walkable=True)
    P.add(box(0.9, 0.14, 0.035, bevel=0.012), WOOD, mat=M((0, 0, 0), (0, 0, 8)))
    P.add(box(0.8, 0.13, 0.035, bevel=0.012), WOOD_B, mat=M((0.02, 0.16, 0), (0, 0, -4)))
    P.add(box(0.7, 0.13, 0.035, bevel=0.012), WOOD, mat=M((0.05, 0.07, 0.035), (0, 0, 30)))
    return P.finish()


def rope_coil():
    P = Prop("prop_rope_coil", SMALL, "small", {"height": 0.12}, 24, walkable=True)
    pts, n = [], 28
    for i in range(n):
        t = i / (n - 1)
        a = t * 2.4 * 2 * math.pi
        r = 0.17 - 0.06 * t
        pts.append((r * math.cos(a), r * math.sin(a), 0.035 + 0.05 * t))
    P.add(tube(pts, [0.035] * n, 4), ROPE, smooth=True)
    return P.finish()


def pebbles():
    P = Prop("prop_pebbles", SMALL, "small", {"height": 0.12}, 25, walkable=True)
    rock(P, 0.11, (0, 0, 0.03), 1, squash=0.55)
    rock(P, 0.08, (0.17, -0.06, 0.02), 2, squash=0.55)
    rock(P, 0.06, (-0.13, -0.1, 0.015), 3, col=STONE_CORE, squash=0.6)
    return P.finish()


def brazier():
    P = Prop("prop_brazier", MEDIUM, "medium", {"height": 1.0}, 26)
    P.add(box(0.42, 0.42, 0.12), STONE_CORE)
    P.add(box(0.28, 0.28, 0.56), by_nz(STONE, STONE, STONE, 0.5), mat=M((0, 0, 0.12)))
    P.add(box(0.36, 0.36, 0.08), STONE_CORE, mat=M((0, 0, 0.66)))
    P.add(cyl(0.12, 0.13, 10, r2=0.24, bevel=0.02), BRASS, mat=M((0, 0, 0.73)))
    P.add(torus(0.235, 0.025, 12, 4), BRASS, mat=M((0, 0, 0.86)))
    P.add(cyl(0.2, 0.02, 8, bevel=0), WOOD_D, mat=M((0, 0, 0.84)))
    for (x, y, h, r, c) in ((0, 0, 0.16, 0.12, FLAME), (0.07, -0.04, 0.1, 0.08, FLAME), (-0.07, 0.03, 0.11, 0.08, FLAME),
                            (0, -0.03, 0.1, 0.07, FLAME_CORE)):
        P.add(tube([(x, y, 0.84), (x, y, 0.84 + h * 0.4), (x * 0.5, y, 0.84 + h)], [r, r * 0.8, 0.0], 5, tip=True),
              c, mask=MASK_GLOW, smooth=True, mat=M((0, -0.02 if c == FLAME_CORE else 0, 0)))
    return P.finish(fit_height=1.0)


def crystal_lamp():
    P = Prop("prop_crystal_lamp", MEDIUM, "medium", {"height": 0.95}, 27)
    P.add(box(0.34, 0.34, 0.1), STONE_CORE)
    P.add(box(0.22, 0.22, 0.5), STONE, mat=M((0, 0, 0.1)))
    P.add(cyl(0.15, 0.07, 8, r2=0.12, bevel=0.02), BRASS, mat=M((0, 0, 0.6)))
    crystal(P, 0.3, 0.075, (0, 0, 0.64))
    crystal(P, 0.16, 0.045, (0.07, -0.02, 0.64), (0, 30, 0))
    crystal(P, 0.13, 0.04, (-0.07, 0.02, 0.64), (0, -32, 0))
    return P.finish(fit_height=0.95)


# ---------------------------------------------------------------- registry
REGISTRY = [
    # name, builder, bible ref
    ("prop_biomass_plant_s", lambda: biomass_plant("prop_biomass_plant_s", 0.5, BIO_SPECS["s"], 31), "p14 biomass plants (small); p03"),
    ("prop_biomass_plant_m", lambda: biomass_plant("prop_biomass_plant_m", 0.8, BIO_SPECS["m"], 32), "p14 biomass plants (medium)"),
    ("prop_biomass_plant_l", lambda: biomass_plant("prop_biomass_plant_l", 1.2, BIO_SPECS["l"], 33), "p14 biomass plants (large); p03"),
    ("prop_biomass_sprout", biomass_sprout, "p03 small biomass sprouts"),
    ("prop_orb_biomass", orb_biomass, "p14 loose orb; p03 biomass orb"),
    ("prop_orb_stone", orb_stone, "p03 stone orb"),
    ("prop_orb_lava", orb_lava, "p03 lava orb"),
    ("prop_orb_corruption", orb_corruption, "p03 corruption orb"),
    ("prop_orb_brass", orb_brass, "p03 brass orb"),
    ("prop_carried_bundle", carried_bundle, "p14 carried bundle"),
    ("prop_corruption_growth_l", lambda: corruption("prop_corruption_growth_l", 1.2, 41), "p14 corruption growth (large); p03"),
    ("prop_corruption_growth_s", lambda: corruption("prop_corruption_growth_s", 0.6, 42), "p14 corruption growth (small)"),
    ("prop_crystal_cluster_l", lambda: crystal_cluster("prop_crystal_cluster_l", 0.9, 51), "p03 blue crystals (large), team"),
    ("prop_crystal_cluster_s", lambda: crystal_cluster("prop_crystal_cluster_s", 0.45, 52), "p03 blue crystals (small), team"),
    ("prop_barricade_folded", barricade_folded, "p14 barricade folded"),
    ("prop_barricade_deployed", barricade_deployed, "p14 barricade deployed"),
    ("prop_barricade_damaged", barricade_damaged, "p14 barricade damaged"),
    ("prop_banner", banner, "p14 banners azure / crimson (team mask)"),
    ("prop_marker_team", lambda: marker(True), "p14 markers azure / crimson (team mask)"),
    ("prop_marker_neutral", lambda: marker(False), "p14 marker neutral (grey)"),
    ("prop_crate", crate, "p03 crate; dressing"),
    ("prop_barrel", barrel, "dressing"),
    ("prop_stone_block", stone_block, "p03 stone block; dressing"),
    ("prop_stone_stack", stone_stack, "dressing (stacked stone blocks)"),
    ("prop_log_section", log_section, "p03 log"),
    ("prop_log", log, "dressing"),
    ("prop_stump", stump, "dressing"),
    ("prop_planks", planks, "dressing (walkable)"),
    ("prop_rope_coil", rope_coil, "dressing (walkable)"),
    ("prop_pebbles", pebbles, "dressing (walkable)"),
    ("prop_brazier", brazier, "dressing light: brazier (pool #FFC27A r 2.0 u)"),
    ("prop_crystal_lamp", crystal_lamp, "dressing light: team crystal lamp (r 1.5 u)"),
]
