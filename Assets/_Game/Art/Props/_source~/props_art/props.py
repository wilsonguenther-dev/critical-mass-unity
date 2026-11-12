"""Every prop in the set, one builder each, and the registry.

Bible p14 (props and interactables) and the p03 prop strip, plus neutral dressing for density. Sizes follow
props.md ("oversized for the high camera"); budgets: small 300, medium 800, large / interactable 1,500,
barricade 2,500 per state. Each builder returns a finished Blender object (one mesh, `Col` colour attribute).
"""
import math

from mathutils import Vector, noise

from .core import (BIO_HI, BIO_LO, BIOMASS, BRASS, CORRUPT, CORRUPT_D, CORRUPT_GLOW, CORRUPT_TIP, CREAM, DIRT,
                   DIRT_D, FLAME, FLAME_CORE, GRASS, GRASS_D, LAVA_GLOW, LAVA_ROCK, LEATHER, MASK_GLOW,
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


def rock(P, r, loc, seed, col=STONE, sub=2, squash=0.7, rot=0.0):
    bm = jitter(ico(r, sub), r * 0.28, 2.2 / r, seed)
    P.add(bm, by_nz(col, col, STONE_D, 0.5, -0.4) if col == STONE else col,
          mat=M(loc, (0, 0, rot), (1.0, 0.9, squash)))


def tuft(P, loc, h=0.09, n=3, seed=0, col=GRASS_D):
    for i in range(n):
        a = 2 * math.pi * i / n + seed
        P.add(cyl(0.025, h, 3, r2=0.0, bevel=0), col, mat=M(loc, (18 * math.cos(a), 18 * math.sin(a), a * 50)))


def dirt_base(P, r, seed, h=0.05):
    bm = jitter(cyl(r, h, 10, r2=r * 0.82, bevel=0.02), 0.03 * r / 0.3, 3.0, seed, zscale=0.2)
    P.add(bm, by_nz(DIRT, DIRT_D, DIRT_D, 0.6, -2))


# ---------------------------------------------------------------- p14 biomass plants
def biomass_plant(name, H, nstalk, nhead, nmush, seed):
    P = Prop(name, LARGE, "interactable", {"height": H}, seed)
    rng = P.rng
    base_r = 0.30 + 0.12 * nstalk / 3
    dirt_base(P, base_r, seed)
    for i in range(nstalk):                                       # curling fern-like stalks
        a = 2 * math.pi * (i + rng.uniform(-0.2, 0.2)) / nstalk + 0.4
        reach = rng.uniform(0.35, 0.55)
        hs = rng.uniform(0.55, 0.8) if i else 0.62
        pts, rad = [], []
        for k in range(8):
            t = k / 7
            d = reach * math.sin(t * math.pi * 0.62) * 0.75
            z = hs * t ** 0.85
            if k >= 6:                                            # tip curls outward and down
                d += 0.08 * (k - 5)
                z -= 0.07 * (k - 5) ** 1.5
            pts.append((d * math.cos(a), d * math.sin(a), z + 0.03))
            rad.append(0.075 * (1 - 0.75 * t) + 0.012)
        P.add(tube(pts, rad, 5, tip=True), by_nz(STALK_L, STALK, STALK, 0.35, -2), smooth=True)
    P.add(tube([(0, 0, 0.02), (0.02, 0.0, 0.35), (-0.02, 0.02, 0.62)], [0.10, 0.08, 0.06], 6), STALK, smooth=True)
    glow = by_nz(BIO_HI, BIOMASS, BIO_LO, 0.5, -0.3, MASK_GLOW)
    cr = 0.36                                                     # head cluster
    for i in range(nhead):
        if i == 0:
            c, r = Vector((0, 0, 0.78)), 0.27
        else:
            a = 2 * math.pi * i / max(1, nhead - 1) + rng.uniform(-0.3, 0.3)
            c = Vector((cr * 0.62 * math.cos(a), cr * 0.62 * math.sin(a), 0.70 + rng.uniform(-0.06, 0.08)))
            r = rng.uniform(0.15, 0.2)
        P.add(uvs(r, 10, 7), glow, smooth=True, mat=M(c, (0, 0, rng.uniform(0, 90)), (1, 1, 0.86)))
    for i in range(nmush):                                        # small biomass puffs at the base
        a = 2 * math.pi * i / max(1, nmush) + 1.1
        d = base_r * 0.78
        h = rng.uniform(0.10, 0.2)
        c = (d * math.cos(a), d * math.sin(a), 0.0)
        P.add(cyl(0.025, h, 4, bevel=0), STALK, mat=M(c))
        P.add(uvs(rng.uniform(0.07, 0.1), 8, 5), glow, smooth=True, mat=M((c[0], c[1], h), scale=(1, 1, 0.8)))
    rock(P, 0.09, (base_r * 0.7, -base_r * 0.55, 0.03), seed + 1)
    rock(P, 0.06, (-base_r * 0.75, -base_r * 0.4, 0.02), seed + 2, sub=1)
    tuft(P, (-base_r * 0.2, -base_r * 0.85, 0.02), seed=seed)
    return P.finish(fit_height=H)


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
    glow = by_nz(BIO_HI, BIOMASS, BIO_LO, 0.45, -0.35, MASK_GLOW)
    P.add(uvs(0.2, 14, 9), glow, smooth=True, mat=M((0, 0, 0.22)))
    rng = P.rng
    for i in range(7):
        a, el = 2 * math.pi * i / 7 + rng.uniform(-0.3, 0.3), rng.uniform(-0.2, 0.9)
        d = Vector((math.cos(a) * math.cos(el), math.sin(a) * math.cos(el), math.sin(el)))
        r = rng.uniform(0.06, 0.09)
        P.add(uvs(r, 8, 6), glow, smooth=True, mat=M(Vector((0, 0, 0.22)) + d * (0.2 - r * 0.25)))
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
        return (h, MASK_GLOW)
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
    c = (0, 0, 0.27)
    P.add(uvs(0.175, 12, 8), by_nz(BIO_HI, BIOMASS, BIO_LO, 0.45, -0.35, MASK_GLOW), smooth=True, mat=M(c))
    seg_col = lambda f: WOOD if f.normal.z > 0.2 or f.normal.y < -0.5 else WOOD_B
    P.add(torus(0.215, 0.045, 14, 5), seg_col, mat=M(c, (90, 0, 0)))        # front ring (faces camera)
    P.add(torus(0.215, 0.035, 14, 4), WOOD_D, mat=M(c, (90, 0, 90)))        # side ring
    P.add(torus(0.225, 0.022, 16, 4), LEATHER, mat=M(c))                    # strap
    for (x, z) in ((0, 0.27 + 0.215), (0, 0.27 - 0.215), (0.215, 0.27), (-0.215, 0.27)):
        P.add(box(0.1, 0.11, 0.08, bevel=0.02, base=False), BRASS, mat=M((x, 0, z), (0, 90 if x else 0, 0)))
    P.add(tube([(-0.12, -0.05, 0.47), (0, -0.07, 0.56), (0.12, -0.05, 0.47)], [0.02, 0.02, 0.02], 4), LEATHER)
    return P.finish(fit_height=0.55)


# ---------------------------------------------------------------- corruption growth
def corruption(name, H, nsp, seed):
    P = Prop(name, LARGE, "interactable", {"height": H}, seed)
    rng = P.rng
    for i, (x, y, r) in enumerate(((0.0, 0.05, 0.16), (0.2, -0.12, 0.11), (-0.22, -0.1, 0.12), (0.05, -0.25, 0.08))):
        rock(P, r, (x, y, r * 0.35), seed + i, col=STONE_D, squash=0.75)
    gl = Vector((-0.4, -0.8, 0.45)).normalized()

    def col(f):
        c = f.calc_center_median()
        t = c.z / H
        if t > 0.82:
            return (CORRUPT_TIP, MASK_GLOW)
        if f.normal.dot(gl) > 0.15 and abs(noise.noise(c * 6.5 + Vector((seed, 0, 0)))) < 0.2 and t > 0.12:
            return (CORRUPT_GLOW, MASK_GLOW)
        return CORRUPT if f.normal.z > -0.1 else CORRUPT_D
    for i in range(nsp):
        if i == 0:
            x0, y0, h, r0, ph = 0.0, 0.0, 1.0, 0.16, 0.0
        else:
            a = 2 * math.pi * i / (nsp - 1) + rng.uniform(-0.3, 0.3)
            x0, y0 = 0.17 * math.cos(a), 0.12 * math.sin(a)
            h, r0, ph = rng.uniform(0.45, 0.7), rng.uniform(0.07, 0.11), rng.uniform(0, 6)
        pts, rad = [], []
        for k in range(9):
            t = k / 8
            sway = 0.12 * math.sin(t * math.pi * 1.4 + ph) * t
            out = 0.25 * t * t if i else 0.0
            pts.append((x0 * (1 + out * 3) + sway, y0 * (1 + out * 3), 0.05 + h * t))
            rad.append(r0 * (1.0 - t) ** 0.7 * (1 + 0.35 * math.sin(t * math.pi)))
        P.add(tube(pts, rad, 6, tip=True, twist=i), col)
    return P.finish(fit_height=H)


# ---------------------------------------------------------------- team crystals
def crystal(P, h, r, loc, lean=(0, 0), rot=0.0):
    bm = tube([(0, 0, -0.02), (0, 0, h * 0.68), (0, 0, h)], [r * 0.85, r, 0.0], 6, tip=True)
    P.add(bm, facet_light(TEAM_GLOW, TEAM_DARK, MASK_TEAM_GLOW, MASK_TEAM_DARK), lined=False,
          mat=M(loc, (lean[0], lean[1], rot)))


def crystal_cluster(name, H, seed):
    P = Prop(name, LARGE, "interactable", {"height": H}, seed, outline_px=0.0)
    k = H / 0.9
    rock(P, 0.17 * k, (0, 0.02, 0.03), seed, squash=0.55)
    crystal(P, 0.9 * k, 0.11 * k, (0, 0, 0), (0, -6, 0))
    for i, (a, s, lean) in enumerate(((200, 0.6, 28), (330, 0.5, 30), (95, 0.45, 32), (20, 0.38, 35))):
        ar = math.radians(a)
        crystal(P, 0.9 * k * s, 0.08 * k, (0.12 * k * math.cos(ar), 0.1 * k * math.sin(ar), 0.0),
                (-lean * math.sin(ar), lean * math.cos(ar)), a)
    return P.finish(fit_height=H)


# ---------------------------------------------------------------- barricade (Miner, one cell, three states)
def _post(P, x, h=1.0, lean=0.0, broken=False):
    m = M((x, 0, 0), (0, lean, 0))
    P.add(box(0.15, 0.15, h), WOOD_B, mat=m)
    P.add(box(0.25, 0.25, 0.22), by_nz(STONE, STONE_CORE, STONE_CORE, 0.5), mat=M((x, 0, 0)))
    P.add(box(0.18, 0.18, 0.06), STONE_CORE, mat=m @ M((0, 0, h * 0.78)))
    if broken:
        P.add(box(0.15, 0.15, 0.12, bevel=0.02), WOOD_B, mat=m @ M((0.02, 0, h), (0, 25, 10)))
    else:
        P.add(cyl(0.085, 0.07, 8, r2=0.03, bevel=0.015), WOOD_B, mat=m @ M((0, 0, h)))


def barricade_deployed():
    P = Prop("prop_barricade_deployed", BARRICADE, "barricade", {"height": 1.05, "width": 1.0}, 11)
    _post(P, -0.42)
    _post(P, 0.42)
    P.add(box(0.76, 0.09, 0.09), WOOD, mat=M((0, 0, 0.84)))
    P.add(box(0.76, 0.08, 0.1), WOOD_B, mat=M((0, 0.02, 0.12)))
    for s in (1, -1):
        P.add(box(0.9, 0.06, 0.08), WOOD_D, mat=M((0, 0.05, 0.42), (0, s * 33, 0)))
    P.add(cloth(0.74, 0.5, 8, 4, lambda u, v: -0.05 * math.sin(math.pi * u) * v, notch=0.16), TEAM_CLOTH,
          mask=MASK_TEAM_CLOTH, smooth=True, mat=M((0, -0.06, 0.82)))
    pts = [(-0.42 + 0.84 * t, -0.09, 0.96 - 0.16 * math.sin(math.pi * t)) for t in [i / 9 for i in range(10)]]
    P.add(tube(pts, [0.025] * 10, 4), ROPE, smooth=True)
    for x in (-0.42, 0.42):
        P.add(torus(0.09, 0.022, 10, 4), ROPE, mat=M((x, 0, 0.93)))
    return P.finish()


def barricade_folded():
    P = Prop("prop_barricade_folded", BARRICADE, "barricade", {"height": 0.6, "width": 0.8}, 12)
    W, D, H = 0.8, 0.42, 0.56
    P.add(box(W - 0.1, D - 0.1, 0.1), TEAM_CLOTH, mask=MASK_TEAM_CLOTH, mat=M((0, 0, H - 0.08)))
    for sx in (-1, 1):
        for sy in (-1, 1):
            P.add(box(0.09, 0.09, H), WOOD_D, mat=M((sx * (W / 2 - 0.045), sy * (D / 2 - 0.045), 0)))
            for z in (0.0, H - 0.07):
                P.add(box(0.12, 0.12, 0.07, bevel=0.02), STONE_CORE,
                      mat=M((sx * (W / 2 - 0.045), sy * (D / 2 - 0.045), z)))
    for sy in (-1, 1):
        for j in range(3):
            P.add(box(W - 0.16, 0.06, 0.15), WOOD if j % 2 else WOOD_B,
                  mat=M((0, sy * (D / 2 - 0.03), 0.04 + j * 0.165)))
        for s in (1, -1):
            P.add(box(0.72, 0.05, 0.07), WOOD_D, mat=M((0, sy * (D / 2 + 0.01), 0.25), (0, s * 30, 0)))
    for sx in (-1, 1):
        for j in range(3):
            P.add(box(0.06, D - 0.16, 0.15), WOOD_B, mat=M((sx * (W / 2 - 0.03), 0, 0.04 + j * 0.165)))
    P.add(tube([(-0.2, -D / 2 - 0.02, H + 0.0), (-0.2, 0, H + 0.04), (-0.2, D / 2 + 0.02, H)], [0.02] * 3, 4), ROPE)
    P.add(tube([(0.2, -D / 2 - 0.02, H + 0.0), (0.2, 0, H + 0.04), (0.2, D / 2 + 0.02, H)], [0.02] * 3, 4), ROPE)
    return P.finish()


def barricade_damaged():
    P = Prop("prop_barricade_damaged", BARRICADE, "barricade", {"height": 0.95}, 13)
    _post(P, -0.42, 0.92, broken=False)
    _post(P, 0.42, 0.72, lean=10, broken=True)
    P.add(box(0.5, 0.09, 0.09), WOOD, mat=M((-0.18, 0, 0.78), (0, 6, 0)))
    P.add(box(0.42, 0.09, 0.09), WOOD, mat=M((0.18, -0.06, 0.03), (0, 0, 14)))
    P.add(box(0.9, 0.06, 0.08), WOOD_D, mat=M((0, 0.05, 0.36), (0, 33, 0)))
    rng = P.rng

    def torn(u, v):
        n = noise.noise(Vector((u * 4.0, v * 3.0, 1.7)))
        return n > 0.18 or (v > 0.75 and rng.random() < 0.45) or (u > 0.6 and v > 0.5)
    P.add(cloth(0.56, 0.48, 8, 5, lambda u, v: -0.05 * math.sin(math.pi * u) * v, notch=0.08, cut=torn), TEAM_CLOTH,
          mask=MASK_TEAM_CLOTH, smooth=True, mat=M((-0.12, -0.06, 0.76), (0, 6, 0)))
    for (x, y, rz, l) in ((-0.15, -0.35, 20, 0.5), (0.3, -0.3, -35, 0.42), (0.05, -0.5, 70, 0.36), (-0.45, -0.3, -10, 0.3)):
        P.add(box(l, 0.1, 0.045, bevel=0.015), WOOD if l > 0.4 else WOOD_B, mat=M((x, y, 0), (0, 0, rz)))
    P.add(tube([(-0.42, -0.09, 0.9), (-0.3, -0.12, 0.6), (-0.25, -0.14, 0.35)], [0.022] * 3, 4), ROPE)
    rock(P, 0.06, (0.1, -0.2, 0.01), 31, col=STONE_CORE, squash=0.5)
    return P.finish()


# ---------------------------------------------------------------- banner (team cloth)
def banner():
    P = Prop("prop_banner", LARGE, "interactable", {"height": 1.9}, 14)
    for i, (x, y, r) in enumerate(((0.0, 0.08, 0.17), (0.17, -0.02, 0.11), (-0.16, 0.0, 0.12), (0.06, -0.14, 0.08))):
        rock(P, r, (x, y, r * 0.3), 40 + i, squash=0.7)
    tuft(P, (-0.2, -0.15, 0.03), seed=1)
    tuft(P, (0.22, -0.12, 0.03), seed=2)
    P.add(box(0.11, 0.11, 1.84), WOOD_B, mat=M((0, 0.06, 0)))
    P.add(box(0.74, 0.09, 0.09), WOOD, mat=M((0, 0.0, 1.7)))
    for x in (-0.37, 0.37):
        P.add(box(0.08, 0.11, 0.11, bevel=0.02, base=False), BRASS, mat=M((x, 0, 1.745)))
        P.add(torus(0.05, 0.018, 8, 4), ROPE, mat=M((x * 0.8, 0, 1.745), (0, 90, 0)))
    P.add(cyl(0.07, 0.08, 8, r2=0.03, bevel=0.015), BRASS, mat=M((0, 0.06, 1.84)))
    P.add(cloth(0.6, 1.12, 6, 8, lambda u, v: -0.03 * math.sin(u * math.pi * 3) * v - 0.02 * v, notch=0.24),
          TEAM_CLOTH, mask=MASK_TEAM_CLOTH, smooth=True, mat=M((0, -0.06, 1.68)))
    P.add(torus(0.1, 0.022, 14, 4), CREAM, mat=M((0, -0.1, 1.22), (90, 0, 0)))
    P.add(tube([(0, -0.1, 1.04), (0, -0.1, 1.22), (0, -0.1, 1.4)], [0.0, 0.05, 0.0], 4), CREAM)
    return P.finish(fit_height=1.9)


# ---------------------------------------------------------------- markers (diamond over ring)
def _diamond(P, z0, h, r, colf):
    bm = tube([(0, 0, z0), (0, 0, z0 + h * 0.42), (0, 0, z0 + h)], [0.0001, r, 0.0], 4, tip=True, twist=math.pi / 4)
    P.add(bm, colf, lined=False)


def marker(team=True):
    name = "prop_marker_team" if team else "prop_marker_neutral"
    P = Prop(name, LARGE, "interactable", {"height": 1.0}, 15, outline_px=0.0)
    if team:
        lit, dark, ml, md = TEAM_GLOW, TEAM_DARK, MASK_TEAM_GLOW, MASK_TEAM_DARK
    else:
        lit, dark, ml, md = NEUTRAL_HI, NEUTRAL_LO, MASK_GLOW, 0.0
    P.add(torus(0.3, 0.04, 20, 4), (lit, ml), lined=False, mat=M((0, 0, 0.04), scale=(1, 1, 0.6)))
    P.add(cyl(0.2, 0.01, 12, bevel=0), (dark, md), lined=False)
    _diamond(P, 0.4, 0.6, 0.19, facet_light(lit, dark, ml, md, (-0.7, -0.4, 0.3)))
    return P.finish(fit_height=1.0)


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
    ("prop_biomass_plant_s", lambda: biomass_plant("prop_biomass_plant_s", 0.5, 3, 2, 1, 31), "p14 biomass plants (small); p03"),
    ("prop_biomass_plant_m", lambda: biomass_plant("prop_biomass_plant_m", 0.8, 4, 3, 2, 32), "p14 biomass plants (medium)"),
    ("prop_biomass_plant_l", lambda: biomass_plant("prop_biomass_plant_l", 1.2, 6, 5, 3, 33), "p14 biomass plants (large); p03"),
    ("prop_biomass_sprout", biomass_sprout, "p03 small biomass sprouts"),
    ("prop_orb_biomass", orb_biomass, "p14 loose orb; p03 biomass orb"),
    ("prop_orb_stone", orb_stone, "p03 stone orb"),
    ("prop_orb_lava", orb_lava, "p03 lava orb"),
    ("prop_orb_corruption", orb_corruption, "p03 corruption orb"),
    ("prop_orb_brass", orb_brass, "p03 brass orb"),
    ("prop_carried_bundle", carried_bundle, "p14 carried bundle"),
    ("prop_corruption_growth_l", lambda: corruption("prop_corruption_growth_l", 1.2, 6, 41), "p14 corruption growth (large); p03"),
    ("prop_corruption_growth_s", lambda: corruption("prop_corruption_growth_s", 0.6, 4, 42), "p14 corruption growth (small)"),
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
