"""The Skybreak Basin floating-ruins kit: every piece, authored in Unity space (see geo.py).

Grid facts (loop/items/F-03.md, browser src/sim/map.ts at 09e0904):
  * 1 cell = 1 world unit = 1 m; map 112 x 72 cells; cell (cx, cz) spans sim [cx, cx+1) x [cz, cz+1).
  * Unity position of a cell's top centre = SimSpace.ToWorld(cx + 0.5, Height01, cz + 0.5) = (cx+.5, h, -(cz+.5)).
  * Height steps: islands at 0, 0.75 (central/hill) and 1.5 (shrine); bridges lerp between their endpoint islands.
  * F-08b box heights: floor top h (bottom h-0.5), bridge h-0.2, wall h+1.5, breakable cracked h+0.75.

Pivot rule: every CELL piece has its pivot at the centre of the cell's top face (y = 0 is the cell's Height01), so
the game places it at (cx+0.5, h, -(cz+0.5)) with no rotation. Feature pieces (hill, pad, shrine) pivot on the feature
centre from map.ts, at the island height. Spans and fallen columns pivot where their doc string says.
"""
import math
import random

from mathutils import Vector

from .geo import C, M, Mesh

H_STEP = 0.75          # one height step (central island)
SKIRT = 0.75           # ground cell side depth: closes a one-step drop and a dug neighbour
CELL_H = 0.5


def rng_for(name):
    return random.Random(sum(ord(c) * (i + 1) for i, c in enumerate(name)))


# ----------------------------------------------------------------------------------------------------------- ground
def _skirt(m, y_top, lip_col, body_col, lip=0.16, y_bot=-SKIRT):
    """East (+X) and south (-Z) cell sides, the only ones the camera can see."""
    for (a, b) in (((0.5, -0.5), (0.5, 0.5)), ((0.5, -0.5), (-0.5, -0.5))):
        (x0, z0), (x1, z1) = a, b
        if x0 == x1:   # +X face
            m.quad((0.5, y_top - lip, -0.5), (0.5, y_top, -0.5), (0.5, y_top, 0.5), (0.5, y_top - lip, 0.5), lip_col)
            m.quad((0.5, y_bot, -0.5), (0.5, y_top - lip, -0.5), (0.5, y_top - lip, 0.5), (0.5, y_bot, 0.5), body_col)
        else:          # -Z face
            m.quad((-0.5, y_top - lip, -0.5), (-0.5, y_top, -0.5), (0.5, y_top, -0.5), (0.5, y_top - lip, -0.5), lip_col)
            m.quad((-0.5, y_bot, -0.5), (-0.5, y_top - lip, -0.5), (0.5, y_top - lip, -0.5), (0.5, y_bot, -0.5), body_col)


def _tuft(m, x, z, s, col):
    h = 0.12 * s
    w = 0.06 * s
    m.tri((x - w, 0, z - w * 0.6), (x + w, 0, z - w * 0.6), (x, h, z), col)
    m.tri((x + w, 0, z - w * 0.6), (x + w * 0.2, 0, z + w), (x, h, z), col)


def _slab_poly(m, x0, z0, x1, z1, y, depth, col, rng, ch=0.05, jag=0.07):
    """Irregular flagstone: four jittered corners, a chamfer band, sides down to y - depth."""
    pts = [(x0 + rng.uniform(0, jag), z0 + rng.uniform(0, jag)), (x1 - rng.uniform(0, jag), z0 + rng.uniform(0, jag)),
           (x1 - rng.uniform(0, jag), z1 - rng.uniform(0, jag)), (x0 + rng.uniform(0, jag), z1 - rng.uniform(0, jag))]
    cx, cz = sum(p[0] for p in pts) / 4, sum(p[1] for p in pts) / 4
    inner = [(p[0] + (cx - p[0]) * ch / max(0.05, abs(cx - p[0]) + 0.05), p[1] + (cz - p[1]) * ch / max(0.05, abs(cz - p[1]) + 0.05)) for p in pts]
    order = [0, 3, 2, 1]                       # +Y face, wound so its normal is up
    m.poly([(inner[i][0], y, inner[i][1]) for i in order], col)
    yb = y - ch * 0.8
    for i in range(4):
        j = (i + 1) % 4
        a, b, ia, ib = pts[i], pts[j], inner[i], inner[j]
        m.poly([(a[0], yb, a[1]), (ia[0], y, ia[1]), (ib[0], y, ib[1]), (b[0], yb, b[1])], col)
        m.poly([(a[0], y - depth, a[1]), (a[0], yb, a[1]), (b[0], yb, b[1]), (b[0], y - depth, b[1])], col)


def _slabs(m, rects, rng, cols, y_top=0.0, gap=0.018, ch=0.045, depth=0.12, jit=0.014):
    for (x0, z0, x1, z1) in rects:
        y = y_top - rng.uniform(0, jit)
        col = C(rng.choice(cols), rng.uniform(0.93, 1.05))
        _slab_poly(m, x0 + gap, z0 + gap, x1 - gap, z1 - gap, y, depth, col, rng, ch=ch)


def ground_grass(m, seed):
    rng = random.Random(seed)
    m.quad((-0.5, 0, -0.5), (-0.5, 0, 0.5), (0.5, 0, 0.5), (0.5, 0, -0.5), C("grass"))
    _skirt(m, 0.0, C("grass_dark"), C("rock"))
    for _ in range(rng.randint(1, 3)):
        _tuft(m, rng.uniform(-0.35, 0.35), rng.uniform(-0.35, 0.35), rng.uniform(0.8, 1.4), C("grass_dark"))
    if rng.random() < 0.5:   # a pebble
        x, z = rng.uniform(-0.3, 0.3), rng.uniform(-0.3, 0.3)
        m.cbox(x - 0.07, -0.02, z - 0.05, x + 0.07, 0.05, z + 0.05, C("rock"), ch=0.03)


PATH_LAYOUTS = [
    [(-0.5, -0.5, 0.12, 0.08), (0.12, -0.5, 0.5, 0.08), (-0.5, 0.08, 0.5, 0.5)],
    [(-0.5, -0.5, 0.5, 0.5)],
    [(-0.5, -0.5, -0.05, 0.5), (-0.05, -0.5, 0.5, -0.02), (-0.05, -0.02, 0.5, 0.5)],
]


def ground_path(m, layout, seed):
    rng = random.Random(seed)
    m.quad((-0.5, -0.06, -0.5), (-0.5, -0.06, 0.5), (0.5, -0.06, 0.5), (0.5, -0.06, -0.5), C("grout"))
    _slabs(m, PATH_LAYOUTS[layout], rng, ["path", "path", "path_b", "path_c"])
    _skirt(m, -0.06, C("path_b", 0.85), C("rock"), lip=0.1)


def ground_mossy(m, seed):
    """Paving breaking up into grass: the island-edge / path-border transition (bible p14 'mossy stone' tile)."""
    rng = random.Random(seed)
    m.quad((-0.5, -0.02, -0.5), (-0.5, -0.02, 0.5), (0.5, -0.02, 0.5), (0.5, -0.02, -0.5), C("grass"))
    rects = [(-0.5, -0.5, 0.0, 0.0), (0.0, -0.5, 0.5, 0.0), (-0.5, 0.0, 0.0, 0.5), (0.0, 0.0, 0.5, 0.5)]
    keep = rng.sample(rects, 2)
    _slabs(m, keep, rng, ["path_b", "path", "wall_b"], jit=0.02)
    for _ in range(2):
        _tuft(m, rng.uniform(-0.35, 0.35), rng.uniform(-0.35, 0.35), 1.1, C("moss"))
    _skirt(m, -0.02, C("grass_dark"), C("rock"))


def ground_dirt(m, seed):
    rng = random.Random(seed)
    m.quad((-0.5, 0, -0.5), (-0.5, 0, 0.5), (0.5, 0, 0.5), (0.5, 0, -0.5), C("dirt"))
    for _ in range(3):
        x, z = rng.uniform(-0.35, 0.35), rng.uniform(-0.35, 0.35)
        r = rng.uniform(0.06, 0.12)
        m.quad((x - r, 0.004, z - r * 0.6), (x - r * 0.6, 0.004, z + r), (x + r, 0.004, z + r * 0.5),
               (x + r * 0.7, 0.004, z - r), C("dirt_dark"))
    _tuft(m, rng.uniform(-0.4, 0.4), rng.uniform(-0.4, 0.4), 1.2, C("grass_dark"))
    _skirt(m, 0.0, C("dirt_dark"), C("rock"))


def ground_corrupt(m, seed):
    rng = random.Random(seed)
    m.quad((-0.5, -0.06, -0.5), (-0.5, -0.06, 0.5), (0.5, -0.06, 0.5), (0.5, -0.06, -0.5), C("corrupt_glow"))
    _slabs(m, PATH_LAYOUTS[seed % 3], rng, ["corrupt", "corrupt", "corrupt_dark"], gap=0.035)
    _skirt(m, -0.06, C("corrupt_dark"), C("rock_dark"), lip=0.1)


def ground_placed(m, seed):
    """CM-30 Miner-placed block: one carved stone block filling the cell (reads as 'built', not grown)."""
    m.cbox(-0.48, -SKIRT, -0.48, 0.48, 0.0, 0.48, C("wall"), top=C("wall"), ch=0.07)
    m.quad((-0.5, -SKIRT, -0.5), (-0.5, -SKIRT, 0.5), (0.5, -SKIRT, 0.5), (0.5, -SKIRT, -0.5), C("crevice"))
    m.cbox(-0.3, 0.0, 0.05, 0.05, 0.05, 0.4, C("moss"), ch=0.03)


def ground_fill(m, seed):
    """One extra height step of cell side (E and S faces only), stacked under a raised cell."""
    _skirt(m, 0.0, C("rock"), C("rock_mid"), lip=0.25, y_bot=-H_STEP)


# ----------------------------------------------------------------------------------------------------------- cliffs
def cliff(m, depth, seed):
    """Rock column hanging under an island-edge cell, from y=-SKIRT down to about -depth. Full 1x1 footprint so
    neighbouring columns are flush; tall vertical stone slabs (the bible's columnar cliffs) bulge outward on the two
    visible faces over a dark crevice core; the bottom tapers to a point."""
    rng = random.Random(seed)
    taper_from = -depth + depth * 0.2
    for face in ("E", "S"):
        if face == "E":
            m.quad((0.46, taper_from, -0.5), (0.46, -SKIRT, -0.5), (0.46, -SKIRT, 0.5), (0.46, taper_from, 0.5), C("crevice"))
        else:
            m.quad((-0.5, taper_from, -0.46), (-0.5, -SKIRT, -0.46), (0.5, -SKIRT, -0.46), (0.5, taper_from, -0.46), C("crevice"))
        cuts = [-0.5]
        while cuts[-1] < 0.5 - 0.2:
            cuts.append(min(0.5, cuts[-1] + rng.uniform(0.26, 0.5)))
        cuts[-1] = 0.5
        for k in range(len(cuts) - 1):
            a, b = cuts[k] + 0.012, cuts[k + 1] - 0.012
            y = -SKIRT
            first = True
            while y > taper_from + 0.15:
                hgt = min(rng.uniform(1.0, 2.3), y - taper_from)
                y0 = y - hgt
                p = rng.uniform(0.0, 0.09)
                col = C(rng.choice(["rock", "rock_mid", "rock_mid", "wall_b"]))
                top = C("grass_dark") if first and rng.random() < 0.45 else col
                if face == "E":
                    m.cbox(0.25, y0 + 0.02, a, 0.5 + p, y - 0.01, b, col, top=top, ch=0.05)
                else:
                    m.cbox(a, y0 + 0.02, -0.5 - p, b, y - 0.01, -0.25, col, top=top, ch=0.05)
                y = y0
                first = False
    # moss / grass drips under the lip
    for _ in range(rng.randint(1, 3)):
        L = rng.uniform(0.25, 0.8)
        if rng.random() < 0.5:
            z = rng.uniform(-0.45, 0.3)
            m.cbox(0.5, -SKIRT - L, z, 0.6, -SKIRT + 0.02, z + rng.uniform(0.12, 0.25), C("grass_dark"), ch=0.02)
        else:
            x = rng.uniform(-0.45, 0.3)
            m.cbox(x, -SKIRT - L, -0.6, x + rng.uniform(0.12, 0.25), -SKIRT + 0.02, -0.5, C("grass_dark"), ch=0.02)
    tip = (rng.uniform(-0.3, 0.2), -depth + rng.uniform(0, depth * 0.08), rng.uniform(-0.2, 0.3))
    y = taper_from
    a, b, c, d = (0.5, y, -0.5), (0.5, y, 0.5), (-0.5, y, 0.5), (-0.5, y, -0.5)
    m.tri(a, b, tip, C("rock_dark"))
    m.tri(d, a, tip, C("rock_mid"))
    m.tri(b, c, tip, C("rock_dark"))
    m.tri(c, d, tip, C("rock_dark"))


def edge_stones(m, seed):
    """Low chunky rim blocks along a cell's two outer (east / south) edges: the bible's stone island borders.
    Decoration on a walkable edge cell; kept under 0.5 m and in the outer 0.3 m of the cell."""
    rng = random.Random(seed)
    for face in ("E", "S"):
        x = -0.5
        while x < 0.45:
            w = rng.uniform(0.28, 0.5)
            h = rng.uniform(0.25, 0.55)
            col = C(rng.choice(["wall", "wall_b", "rock"]))
            top = C("moss") if rng.random() < 0.4 else col
            if face == "E":
                m.cbox(0.2, -0.05, x + 0.02, 0.48, h, min(0.48, x + w), col, top=top, ch=0.05)
            else:
                m.cbox(x + 0.02, -0.05, -0.48, min(0.48, x + w), h, -0.2, col, top=top, ch=0.05)
            x += w + rng.uniform(0.02, 0.2)


# ------------------------------------------------------------------------------------------------------------ walls
def _ashlar(m, rng, y0, y1, rows, cols_names, inset=0.0, crack=False):
    """Courses of chunky blocks filling the cell from y0 to y1 (plus a dark mortar core)."""
    m.box(-0.47 + inset, y0, -0.47 + inset, 0.47 - inset, y1 - 0.03, 0.47 - inset, C("crevice"))
    h = (y1 - y0) / rows
    for r in range(rows):
        ya, yb = y0 + r * h, y0 + (r + 1) * h
        if r % 2 == 0:
            cuts = [-0.5, rng.uniform(-0.1, 0.15), 0.5]
        else:
            cuts = [-0.5, rng.uniform(-0.35, -0.15), rng.uniform(0.15, 0.3), 0.5]
        for k in range(len(cuts) - 1):
            p = rng.uniform(0.0, 0.012)     # stay (almost) inside the cell: neighbours' corners stay clear
            col = C(rng.choice(cols_names))
            top = C("moss") if (r == rows - 1 and rng.random() < 0.6) else col
            # blocks run along x on even rows and along z on odd rows (bond pattern on both visible faces)
            if r % 2 == 0:
                m.cbox(cuts[k] + 0.015, ya + 0.015, -0.5 - p + inset, cuts[k + 1] - 0.015, yb - 0.015, 0.5 + p - inset,
                       col, top=top, ch=0.05)
            else:
                m.cbox(-0.5 - p + inset, ya + 0.015, cuts[k] + 0.015, 0.5 + p - inset, yb - 0.015, cuts[k + 1] - 0.015,
                       col, top=top, ch=0.05)
    if crack:
        # hairline cracks on the two visible faces: the read for 'a Miner can break this'
        for (x, z, w, hh) in ((0.531, 0.1, 0.05, 0.6), (0.531, -0.25, 0.04, 0.35)):
            m.quad((x, 0.3, z), (x, 0.3 + hh, z + 0.08), (x, 0.3 + hh, z + 0.08 + w), (x, 0.3, z + w), C("crevice"))
        for (x, w, hh) in ((-0.15, 0.05, 0.55), (0.25, 0.04, 0.4)):
            m.quad((x, 0.35, -0.531), (x + w, 0.35, -0.531), (x + w + 0.07, 0.35 + hh, -0.531),
                   (x + 0.07, 0.35 + hh, -0.531), C("crevice"))


def wall(m, seed):
    rng = random.Random(seed)
    _ashlar(m, rng, -SKIRT, 1.5, 4, ["wall", "wall_b", "wall"])
    # moss cap
    m.cbox(-0.3, 1.48, -0.2, 0.25, 1.56, 0.3, C("moss"), ch=0.04)


def breakable_intact(m, seed):
    rng = random.Random(seed)
    _ashlar(m, rng, -SKIRT, 1.5, 4, ["path_b", "wall", "path_b"], crack=True)


def breakable_cracked(m, seed):
    rng = random.Random(seed)
    _ashlar(m, rng, -SKIRT, 0.75, 3, ["path_b", "wall", "wall_b"])
    # jagged broken top: a leaning half block and chips
    m.push(M((0.1, 0.75, 0.05), yaw=20, roll=12))
    m.cbox(-0.25, 0.0, -0.18, 0.2, 0.32, 0.2, C("path_b"), ch=0.05)
    m.pop()
    for (x, z) in ((-0.3, -0.25), (0.3, 0.28)):
        m.cbox(x - 0.1, 0.75, z - 0.08, x + 0.1, 0.86, z + 0.08, C("wall_b"), ch=0.03)
    for (x, z) in ((0.42, -0.3), (-0.2, -0.44)):
        m.cbox(x - 0.06, 0.73, z - 0.05, x + 0.06, 0.8, z + 0.05, C("crevice"))


def breakable_destroyed(m, seed):
    """Walkable rubble (F-08b draws BreakableDestroyed as floor)."""
    rng = random.Random(seed)
    ground_path(m, 1, seed)
    for _ in range(5):
        x, z = rng.uniform(-0.35, 0.35), rng.uniform(-0.35, 0.35)
        s = rng.uniform(0.07, 0.14)
        m.push(M((x, 0.0, z), yaw=rng.uniform(0, 90), roll=rng.uniform(-15, 15)))
        m.cbox(-s, -0.02, -s * 0.8, s, s * 1.2, s * 0.8, C(rng.choice(["path_b", "wall", "wall_b"])), ch=0.03)
        m.pop()


# ---------------------------------------------------------------------------------------------------------- bridges
BRIDGE_W = 3.0   # 2 x BRIDGE_HALF_WIDTH (map.ts)


def bridge_span(m, seed):
    """1 m of rope-bridge deck, 3 m wide, running along local +Z from z=0 to z=1; pivot at the deck-top centre of the
    near end. The game tiles it along each bridge segment (yaw = segment heading, pitch = height lerp)."""
    rng = random.Random(seed)
    z = 0.0
    k = 0
    while z < 0.999:
        d = 1.0 / 3
        jx = rng.uniform(-0.06, 0.06)
        col = C(["wood", "wood_b", "wood"][k % 3])
        y = -rng.uniform(0, 0.015)
        m.cbox(-1.42 + jx, y - 0.08, z + 0.018, 1.42 + jx, y, z + d - 0.018, col, ch=0.03)
        z += d
        k += 1
    m.quad((-1.42, -0.085, 0.0), (-1.42, -0.085, 1.0), (1.42, -0.085, 1.0), (1.42, -0.085, 0.0), C("wood_dark"))
    for x in (-1.15, 1.15):  # stringers
        m.box(x - 0.09, -0.24, 0.0, x + 0.09, -0.08, 1.0, C("wood_dark"))
    for x in (-1.5, 1.5):    # hand ropes
        m.box(x - 0.035, 0.66, 0.0, x + 0.035, 0.73, 1.0, C("rope"))


def bridge_cell(m, seed):
    """One BRIDGE cell of axis-aligned planks (for bridge cells not under a span: placed by edits)."""
    rng = random.Random(seed)
    for k in range(3):
        z0 = -0.5 + k / 3
        m.cbox(-0.5, -0.08, z0 + 0.018, 0.5, -rng.uniform(0, 0.012), z0 + 1 / 3 - 0.018,
               C(["wood", "wood_b", "wood"][k]), ch=0.03)
    m.quad((-0.5, -0.085, -0.5), (-0.5, -0.085, 0.5), (0.5, -0.085, 0.5), (0.5, -0.085, -0.5), C("wood_dark"))
    m.box(-0.5, -0.2, -0.3, 0.5, -0.08, -0.15, C("wood_dark"))
    m.box(-0.5, -0.2, 0.15, 0.5, -0.08, 0.3, C("wood_dark"))


def bridge_post(m, seed):
    m.cbox(-0.11, -0.3, -0.11, 0.11, 0.98, 0.11, C("wood_b"), top=C("wood"), ch=0.03)
    m.box(-0.125, 0.62, -0.125, 0.125, 0.76, 0.125, C("rope"))


def bridge_pier(m, seed):
    """Mossy stone pier with a post, placed at a bridge mouth on the island edge (bible p14 bridge)."""
    rng = random.Random(seed)
    m.cbox(-0.42, -1.4, -0.42, 0.42, 0.32, 0.42, C("wall"), top=C("moss"), ch=0.07)
    m.cbox(-0.44, -1.4, -0.44, 0.1, -0.5, 0.1, C("wall_b"), ch=0.05)
    m.cbox(-0.1, 0.32, -0.1, 0.12, 1.1, 0.12, C("wood_b"), top=C("wood"), ch=0.03)
    m.box(-0.115, 0.72, -0.115, 0.135, 0.86, 0.135, C("rope"))


# ------------------------------------------------------------------------------------------------------- objectives
def _sector(m, cx, cz, r0, r1, a0, a1, y0, y1, col, top=None, n=2):
    top = top or col
    for i in range(n):
        t0 = a0 + (a1 - a0) * i / n
        t1 = a0 + (a1 - a0) * (i + 1) / n
        P = lambda r, t, y: (cx + r * math.cos(t), y, cz + r * math.sin(t))
        m.quad(P(r0, t0, y1), P(r0, t1, y1), P(r1, t1, y1), P(r1, t0, y1), top)
        m.quad(P(r1, t0, y0), P(r1, t0, y1), P(r1, t1, y1), P(r1, t1, y0), col)
        m.quad(P(r0, t1, y0), P(r0, t1, y1), P(r0, t0, y1), P(r0, t0, y0), col)
    for (t, sgn) in ((a0, 1), (a1, -1)):
        P0 = (cx + r0 * math.cos(t), cz + r0 * math.sin(t))
        P1 = (cx + r1 * math.cos(t), cz + r1 * math.sin(t))
        pts = [(P0[0], y0, P0[1]), (P1[0], y0, P1[1]), (P1[0], y1, P1[1]), (P0[0], y1, P0[1])]
        m.poly(list(reversed(pts)) if sgn > 0 else pts, col)


def _paved_disc(m, rng, rings, y=0.06, gap_a=0.035, cols=("path", "path_b", "path_c")):
    for (r0, r1, n) in rings:
        off = rng.uniform(0, 1)
        for i in range(n):
            a0 = 2 * math.pi * (i + off) / n
            a1 = 2 * math.pi * (i + 1 + off) / n
            ga = gap_a / max(r1, 0.5)
            yy = y - rng.uniform(0, 0.012)
            _sector(m, 0, 0, r0 + 0.03, r1 - 0.03, a0 + ga, a1 - ga, -0.03, yy, C(rng.choice(cols)), n=2)


def hill_plaza(m, seed):
    """Hill plaza, radius 6 (config hill.radius), pivot at the hill centre (sim 56, 40: a cell corner) at the central
    island height. Paved rings around a cream medallion with the brass crown. The team rim is env_hill_rim_half."""
    rng = random.Random(seed)
    m.ring(0, 0, 0.0, 6.05, 0.008, C("grout"), n=48)
    _paved_disc(m, rng, [(1.35, 2.7, 10), (2.7, 4.1, 16), (4.1, 5.55, 22)])
    m.prism(0, 0, 1.3, -0.03, 0.08, C("dais_dark"), n=20, top=C("cream"), ch=0.03)
    # crown emblem (raised 0.07, flat-topped so it reads from the high camera): band + three wide points
    b = C("brass")
    m.cbox(-0.75, 0.08, -0.55, 0.75, 0.16, -0.2, b, ch=0.03)
    for (x, h, w) in ((-0.62, 0.62, 0.26), (0.0, 0.85, 0.3), (0.62, 0.62, 0.26)):
        tip = (x, 0.16, -0.2 + h)
        m.poly([(x - w, 0.16, -0.2), tip, (x + w, 0.16, -0.2)][::-1], b)
        m.poly([(x - w, 0.08, -0.2), (x - w, 0.16, -0.2), tip, (x, 0.08, -0.2 + h)], b)
        m.poly([(x + w, 0.08, -0.2), (x, 0.08, -0.2 + h), tip, (x + w, 0.16, -0.2)], b)
        m.prism(x, -0.2 + h + 0.06, 0.09, 0.08, 0.2, C("cream"), n=6)


def hill_rim_half(m, seed):
    """Half of the hill's team rim (180 degrees, from local +X through -Z to -X: the half nearer the camera).
    Mask = team glow; the game colours each half (neutral grey / Azure / Crimson / split when contested)."""
    m.ring(0, 0, 5.55, 6.0, 0.0, C("team_glow"), n=24, a0=math.pi, a1=2 * math.pi, h=0.1, side=C("team_dark"))


def spawn_pad(m, seed):
    """Team respawn pad, radius 3 (map.ts PAD_R), pivot at the pad centre (sim 8, 36 for Azure; 104, 36 Crimson).
    Stone ring dais, team-glow ring and cross glyph (bible p14 respawn pad), two crystal clusters at the back."""
    rng = random.Random(seed)
    m.ring(0, 0, 0.0, 3.05, 0.008, C("grout"), n=32)
    for i in range(14):
        a0 = 2 * math.pi * i / 14 + 0.02
        a1 = 2 * math.pi * (i + 1) / 14 - 0.02
        _sector(m, 0, 0, 2.3, 3.0, a0, a1, -0.05, 0.2 - rng.uniform(0, 0.03), C("dais_dark"),
                top=C(rng.choice(["dais", "path_b"])), n=2)
    _paved_disc(m, rng, [(0.0, 1.05, 6), (1.05, 2.25, 12)], y=0.1, cols=("dais", "path", "path_b"))
    m.ring(0, 0, 1.75, 2.0, 0.1, C("team_glow"), n=32, h=0.02)
    g = C("team_glow")
    m.box(-0.75, 0.1, -0.2, 0.75, 0.14, 0.2, g)
    m.box(-0.2, 0.1, -0.75, 0.2, 0.14, 0.75, g)
    for (a, s) in ((115, 1.0), (160, 0.75)):
        t = math.radians(a)
        crystal_cluster(m, seed + a, base=(2.65 * math.cos(t), 0.18, 2.65 * math.sin(t)), scale=s * 0.8)


def crystal(m, base, h, r, tilt_yaw, tilt, seed):
    rng = random.Random(seed)
    m.push(M(base, yaw=tilt_yaw, pitch=tilt))
    n = 6
    rot = rng.uniform(0, 1)
    pts = [(r * math.cos(2 * math.pi * (i + rot) / n), r * math.sin(2 * math.pi * (i + rot) / n)) for i in range(n)]
    ys = h * 0.72
    for i in range(n):
        j = (i + 1) % n
        col = C("team_glow") if i % 2 == 0 else C("team_dark")
        m.quad((pts[i][0], -0.1, pts[i][1]), (pts[i][0], ys, pts[i][1]), (pts[j][0], ys, pts[j][1]),
               (pts[j][0], -0.1, pts[j][1]), col)
        m.tri((pts[i][0], ys, pts[i][1]), (0, h, 0), (pts[j][0], ys, pts[j][1]),
              C("team_glow") if i % 2 else C("team_dark"))
    m.pop()


def crystal_cluster(m, seed, base=(0, 0, 0), scale=1.0):
    rng = random.Random(seed)
    bx, by, bz = base
    m.push(M((bx, by, bz)))
    m.blob(0, -0.05, 0, 0.42 * scale, 0.16 * scale, 0.36 * scale, C("rock_mid"), sub=1, smooth=False, seed=seed,
           jitter=0.15, flat_bottom=-0.05)
    specs = [(0, 0, 1.25, 0.2, 0, 0), (0.22, 0.05, 0.8, 0.14, 300, 25), (-0.2, 0.08, 0.7, 0.13, 120, 28),
             (0.05, -0.2, 0.6, 0.12, 40, 30)]
    for k, (x, z, h, r, yaw, tilt) in enumerate(specs):
        crystal(m, (x * scale, 0.0, z * scale), h * scale * rng.uniform(0.85, 1.1), r * scale, yaw + rng.uniform(-20, 20),
                tilt, seed + k)
    m.pop()


def crystal_cluster_piece(m, seed):
    crystal_cluster(m, seed, scale=2.2)


def crystal_cluster_small(m, seed):
    crystal_cluster(m, seed, scale=1.35)


def banner(m, seed):
    """Team banner on a wooden T-post with a stone base (bible p14), 3.6 m tall. The cloth faces the camera diagonal;
    mask = team cloth; the cream emblem is fixed."""
    m.push(M((0, 0, 0), scale=1.3))
    m.cbox(-0.3, -0.05, -0.3, 0.3, 0.28, 0.3, C("wall"), top=C("moss"), ch=0.06)
    m.cbox(-0.07, 0.28, -0.07, 0.07, 2.75, 0.07, C("wood_b"), top=C("brass"), ch=0.02)
    m.push(M((0, 0, 0), yaw=45))     # face TO_CAM's horizontal direction (+X, -Z)
    m.cbox(-0.08, 2.5, -0.5, 0.08, 2.62, 0.5, C("wood"), ch=0.02)
    xs = 0.1
    cloth = C("team_cloth")
    m.poly([(xs, 2.5, -0.42), (xs, 2.5, 0.42), (xs, 1.15, 0.42), (xs, 1.35, 0.0), (xs, 1.15, -0.42)], cloth,
           force=True)
    e = C("cream")
    xe = xs + 0.012
    m.poly([(xe, 2.25, 0.0), (xe, 2.05, 0.2), (xe, 1.75, 0.0), (xe, 2.05, -0.2)], e, force=True)
    m.poly([(xe, 2.12, 0.0), (xe, 2.03, 0.09), (xe, 1.88, 0.0), (xe, 2.03, -0.09)], cloth, force=True)
    m.pop()
    m.pop()


# ------------------------------------------------------------------------------------------------------------ shrine
def shrine_dais(m, seed):
    """Egg shrine dais, pivot at the shrine centre (sim 56, 8: a cell corner) at the shrine island height (1.5).
    Covers the solid radius 1.5 (WALL cells) with a two-tier round stone dais; the egg sits on top at y=0.62."""
    rng = random.Random(seed)
    m.ring(0, 0, 0.0, 2.3, 0.008, C("grout"), n=32)
    for i in range(16):
        a0 = 2 * math.pi * i / 16 + 0.015
        a1 = 2 * math.pi * (i + 1) / 16 - 0.015
        _sector(m, 0, 0, 1.25, 2.2, a0, a1, -0.05, 0.3 - rng.uniform(0, 0.03), C("dais_dark"),
                top=C(rng.choice(["dais", "path_b", "wall"])), n=2)
    m.prism(0, 0, 1.3, 0.0, 0.62, C("wall_b"), n=16, top=C("dais"), ch=0.05)
    m.ring(0, 0, 0.8, 1.0, 0.62, C("egg_glow"), n=24, h=0.02)
    # little waterfall spout under the front step (bible p14 shrine)
    m.box(-0.25, 0.02, -2.32, 0.25, 0.26, -2.18, C("water_light"))


def _arc_blocks(m, side, seed):
    rng = random.Random(seed)
    # quarter-ish arc from the base (x=2.4) up and curling inward over the egg
    n = 7
    for i in range(n):
        a0 = math.radians(112 * i / n)
        a1 = math.radians(112 * (i + 1) / n)
        p0 = (1.0 + 1.4 * math.cos(a0), 4.3 * math.sin(a0))
        p1 = (1.0 + 1.4 * math.cos(a1), 4.3 * math.sin(a1))
        mx, my = (p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2
        L = math.hypot(p1[0] - p0[0], p1[1] - p0[1])
        roll = math.degrees(math.atan2(p1[1] - p0[1], p1[0] - p0[0])) - 90
        w = 0.95 - 0.07 * i
        m.push(M((side * mx, my, 0.4), roll=side * roll if side > 0 else -roll))
        col = C(rng.choice(["wall", "wall_b", "rock"]))
        m.cbox(-w / 2, -L / 2 - 0.03, -0.42, w / 2, L / 2 + 0.03, 0.42, col, top=C("moss") if i < 2 else col, ch=0.07)
        # teal glow seam on the inner face (bible p14: the horns glow with the egg)
        m.box(-w / 2 - 0.02, -L / 2 + 0.08, -0.43, -w / 2 + 0.06, L / 2 - 0.08, -0.3, C("egg_glow"))
        m.pop()


def shrine_horns(m, seed):
    """The two curved stone horns that flank the egg (bible p12, p14 shrine), about 6.7 m tall, pivot at the shrine
    centre. Scaled 1.55x so the shrine reads as the map's biggest landmark, as in the bible."""
    m.push(M((0, 0, 0.3), scale=1.55))
    _arc_blocks(m, 1, seed)
    _arc_blocks(m, -1, seed + 7)
    for s in (1, -1):
        m.cbox(s * 2.4 - 0.5, -0.05, -0.1, s * 2.4 + 0.5, 0.3, 0.9, C("dais_dark"), top=C("moss"), ch=0.06)
        crystal(m, (s * 2.75, 0.25, 0.2), 0.75, 0.12, 0, 15 * s, seed + s)
    m.pop()


def shrine_egg_proxy(m, seed):
    """STAND-IN egg so the shrine reads in renders: replace with the Kaiju egg (CM-KAI-001, the Kaiju art lane).
    Faceted slate egg with fixed teal glowing seams; pivot at the shrine centre, sits on the dais top (y 0.62)."""
    from mathutils import Vector as V
    rng = random.Random(seed)
    tmp_cols = []
    import bmesh
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=3, radius=1.0)
    seams = [V((math.cos(a), 0.35 * math.sin(3 * a), math.sin(a))).normalized() for a in (0.0, 1.1, 2.3, 3.7, 5.0)]
    for f in bm.faces:
        cen = f.calc_center_median()
        c = V((cen.x, cen.z, cen.y))
        seam = any(abs(c.normalized().dot(s.cross(V((0, 1, 0))).normalized())) < 0.045 for s in seams[:4]) or abs(c.y + 0.1) < 0.03
        col = C("egg_glow") if seam else C("egg" if rng.random() < 0.6 else "egg_dark")
        pts = []
        for v in reversed(f.verts):
            x, y, z = v.co.x, v.co.z, v.co.y
            yy = y * (1.25 if y > 0 else 0.95)
            pts.append((x * 1.2, 0.62 + 1.42 + yy * 1.45 - 0.05, z * 1.2))
        m.poly(pts, col)
    bm.free()


def waterfall(m, seed):
    """Waterfall off a cell's -Z (south) edge, pivot at the cell's top centre; rotate 90 to pour off the +X edge."""
    rng = random.Random(seed)
    w = 0.48
    top = -0.04
    m.quad((-w, top, -0.45), (-w, top, -0.62), (w, top, -0.62), (w, top, -0.45), C("water_light"), force=True)
    bands = 5
    for i in range(bands):
        x0 = -w + 2 * w * i / bands
        x1 = -w + 2 * w * (i + 1) / bands
        L = 5.2 + rng.uniform(-0.6, 0.8)
        col = C("water") if i % 2 == 0 else C("water_light")
        m.quad((x0, top - 0.25, -0.7), (x0, top, -0.62), (x1, top, -0.62), (x1, top - 0.25, -0.7), C("water_light"),
               force=True)
        m.quad((x0, top - L, -0.72), (x0, top - 0.25, -0.7), (x1, top - 0.25, -0.7), (x1, top - L, -0.72), col,
               force=True)
    for k in range(3):
        m.blob(rng.uniform(-0.3, 0.3), top - 5.6 + rng.uniform(-0.2, 0.2), -0.75 + rng.uniform(-0.1, 0.1),
               0.45, 0.3, 0.35, C("cloud"), sub=1, smooth=True, seed=seed + k)


# ------------------------------------------------------------------------------------------------- ruins & foliage
def ruin_column_intact(m, seed):
    """CM-31 knock-down pillar, STANDING (map.decorations, kind 'pillar' = env_ruin_column). Pivot at the base cell's
    top centre. Stacked drums on a plinth with a wide broken capital, about 4.6 m tall."""
    rng = random.Random(seed)
    m.cbox(-0.47, -0.05, -0.47, 0.47, 0.35, 0.47, C("wall_b"), top=C("moss"), ch=0.06)
    y = 0.35
    for k in range(4):
        h = 0.9
        dx, dz = rng.uniform(-0.04, 0.04), rng.uniform(-0.04, 0.04)
        m.push(M((dx, y, dz), yaw=rng.uniform(-6, 6)))
        col = C(rng.choice(["wall", "wall_b", "path_b"]))
        m.cbox(-0.36, 0.0, -0.36, 0.36, h - 0.03, 0.36, col, top=C("moss") if k == 1 else col, ch=0.07)
        m.pop()
        y += h
    m.cbox(-0.62, y, -0.55, 0.62, y + 0.36, 0.55, C("wall"), top=C("moss"), ch=0.07)
    m.cbox(-0.62, y + 0.36, -0.55, 0.05, y + 0.6, 0.2, C("wall_b"), top=C("moss"), ch=0.06)


def _drum(m, loc, yaw, pitch, roll, col, top=None, s=(0.72, 0.87, 0.72)):
    m.push(M(loc, yaw=yaw, pitch=pitch, roll=roll))
    m.cbox(-s[0] / 2, -s[1] / 2, -s[2] / 2, s[0] / 2, s[1] / 2, s[2] / 2, col, top=top, ch=0.07)
    m.pop()


def ruin_column_fallen_ramp(m, seed):
    """CM-31 pillar knocked along its RAMP direction: a 3-cell-wide causeway of the broken drums, lying along local +Z
    from z=0.5 to z=4.5 (topple lengthCells 4, widthCells 3), deck top at y=0 so it is walkable across the void.
    Pivot at the base cell's top centre; the game yaws it to the fall direction (0, 90, 180, 270)."""
    rng = random.Random(seed)
    m.cbox(-0.47, -0.05, -0.47, 0.47, 0.35, 0.47, C("wall_b"), top=C("moss"), ch=0.06)   # plinth stays
    m.cbox(-0.36, 0.35, -0.36, 0.36, 0.62, 0.1, C("wall"), ch=0.05)                       # broken stump
    for row in range(4):
        for col_i in range(3):
            x0 = -1.5 + col_i
            z0 = 0.5 + row
            y = -rng.uniform(0, 0.03)
            c = C(rng.choice(["wall", "wall_b", "path_b", "wall"]))
            m.cbox(x0 + 0.04, y - 0.4, z0 + 0.04, x0 + 0.96, y, z0 + 0.96, c, top=C("moss") if rng.random() < 0.2 else c,
                   ch=0.06)
    # the capital lies at the far end, half sunk
    m.push(M((0.2, -0.15, 4.05), yaw=8))
    m.cbox(-0.6, -0.3, -0.35, 0.6, 0.12, 0.35, C("wall"), top=C("moss"), ch=0.07)
    m.pop()
    # hanging rubble under the deck so it reads as a span across the void
    for k in range(4):
        _drum(m, (rng.uniform(-1, 1), -0.75, 1.0 + k), rng.uniform(0, 40), 0, rng.uniform(-10, 10), C("rock_mid"),
              s=(0.6, 0.5, 0.6))


def ruin_column_fallen_block(m, seed):
    """CM-31 pillar knocked along its BLOCK direction: the drums tumbled in a line with rubble either side, about
    1.1 m tall over the same 3 x 4 footprint (local +Z, z 0.5..4.5). It turns the footprint into WALL in the Sim."""
    rng = random.Random(seed)
    m.cbox(-0.47, -0.05, -0.47, 0.47, 0.35, 0.47, C("wall_b"), top=C("moss"), ch=0.06)
    m.cbox(-0.36, 0.35, -0.36, 0.36, 0.62, 0.1, C("wall"), ch=0.05)
    for k in range(4):
        z = 1.0 + k * 0.98
        _drum(m, (rng.uniform(-0.15, 0.15), 0.38, z), rng.uniform(-15, 15), 90, rng.uniform(-8, 8),
              C(rng.choice(["wall", "wall_b", "path_b"])), top=C("moss"), s=(0.74, 0.9, 0.74))
    for k in range(7):
        side = -1 if k % 2 else 1
        _drum(m, (side * rng.uniform(0.8, 1.25), 0.15, 0.8 + k * 0.52), rng.uniform(0, 60), rng.uniform(-10, 10), 0,
              C(rng.choice(["wall_b", "rock", "path_b"])), s=(0.42, 0.34, 0.4))
    m.push(M((-0.1, 0.3, 4.2), yaw=-12, roll=10))
    m.cbox(-0.6, -0.3, -0.3, 0.6, 0.28, 0.3, C("wall"), top=C("moss"), ch=0.07)
    m.pop()


def ruin_block(m, seed):
    rng = random.Random(seed)
    m.push(M((0, 0, 0), yaw=rng.uniform(-20, 20), roll=rng.uniform(-6, 6)))
    m.cbox(-0.38, -0.1, -0.32, 0.38, 0.5, 0.32, C("wall"), top=C("moss"), ch=0.07)
    m.pop()
    m.cbox(0.2, -0.05, -0.42, 0.42, 0.18, -0.2, C("wall_b"), ch=0.04)


def ruin_stub(m, seed):
    m.cbox(-0.42, -0.05, -0.42, 0.42, 0.3, 0.42, C("wall_b"), top=C("moss"), ch=0.05)
    m.cbox(-0.32, 0.3, -0.32, 0.32, 1.25, 0.32, C("wall"), ch=0.06)
    m.cbox(-0.32, 1.25, -0.32, 0.05, 1.5, 0.1, C("wall"), top=C("moss"), ch=0.05)


def ruin_arch(m, seed):
    """Broken arch (bible p03 ruin pieces): two jambs, one keeping a half lintel. Footprint 3 x 1 cells along X."""
    for x in (-1.0, 1.0):
        m.cbox(x - 0.4, -0.05, -0.4, x + 0.4, 0.3, 0.4, C("wall_b"), top=C("moss"), ch=0.05)
        m.cbox(x - 0.3, 0.3, -0.3, x + 0.3, 2.4 if x < 0 else 1.6, 0.3, C("wall"), top=C("moss"), ch=0.06)
    m.cbox(-1.4, 2.4, -0.33, 0.3, 2.85, 0.33, C("wall"), top=C("moss"), ch=0.07)


def tree(m, seed, big=True):
    rng = random.Random(seed)
    s = 1.4 if big else 1.1
    m.prism(0, 0, 0.17 * s, -0.05, 1.1 * s, C("trunk"), n=6, r_top=0.11 * s)
    lobes = [(0, 1.75, 0, 0.95), (0.45, 1.5, -0.25, 0.7), (-0.4, 1.55, 0.2, 0.68), (0.05, 2.3, 0.1, 0.62)]
    for k, (x, y, z, r) in enumerate(lobes):
        col = C(["leaf", "leaf_light", "leaf", "leaf_light"][k])
        m.blob(x * s, y * s, z * s, r * s, r * s * 0.85, r * s, col, sub=3, smooth=True, seed=seed + k, jitter=0.06)


def bush(m, seed):
    for k, (x, z, r) in enumerate(((0, 0, 0.38), (0.25, -0.15, 0.27), (-0.22, 0.12, 0.25))):
        m.blob(x, r * 0.55, z, r, r * 0.8, r, C("leaf_light" if k == 1 else "leaf"), sub=1, smooth=True, seed=seed + k,
               flat_bottom=-0.02)


def rocks(m, seed):
    rng = random.Random(seed)
    for k, (x, z, r) in enumerate(((0, 0, 0.32), (0.3, -0.22, 0.2), (-0.25, -0.2, 0.16))):
        m.blob(x, r * 0.4, z, r, r * 0.75, r * 0.9, C(["rock", "rock_mid", "wall_b"][k]), sub=1, smooth=False,
               seed=seed + k, jitter=0.18, flat_bottom=-0.02)


# --------------------------------------------------------------------------------------------------------------- sky
def sky_rock(m, seed, s=1.0):
    """Floating rock spire with a grass cap (the small islands around the arena in bible p12 / p15)."""
    rng = random.Random(seed)
    r = 1.1 * s
    m.prism(0, 0, r, -0.3 * s, 0.0, C("grass_dark"), n=7, top=C("grass"), rot=rng.uniform(0, 1), ch=0.08 * s)
    y = -0.3 * s
    rr = r * 0.97
    for k in range(4):
        h = rng.uniform(0.9, 1.4) * s
        m.prism(rng.uniform(-0.08, 0.08) * s, rng.uniform(-0.08, 0.08) * s, rr, y - h, y,
                C(["rock", "rock_mid", "rock", "rock_dark"][k]), n=7, r_top=rr, rot=rng.uniform(0, 1))
        y -= h
        rr *= rng.uniform(0.72, 0.85)
    m.prism(0, 0, rr * 0.2, y - 1.4 * s, y, C("rock_dark"), n=7, r_top=rr, rot=0.3)
    if s >= 1.0:
        m.push(M((0.3 * s, 0.0, 0.2 * s), scale=0.7 * s))
        tree(m, seed + 3)
        m.pop()


def cloud_puff(m, seed):
    rng = random.Random(seed)
    for k in range(6):
        x, z = rng.uniform(-2.2, 2.2), rng.uniform(-1.2, 1.2)
        r = rng.uniform(0.9, 1.6)
        m.blob(x, 0.0, z, r * 1.2, r * 0.55, r, C("cloud"), sub=2, smooth=True, seed=seed + k, flat_bottom=-0.1)


def cloud_floor(m, seed):
    """Flat cloud-sea backdrop, 64 x 64 m, pivot at its centre (the game scales / repeats it far below the islands)."""
    m.quad((-32, 0, -32), (-32, 0, 32), (32, 0, 32), (32, 0, -32), C("sky_floor"), force=True)


# ------------------------------------------------------------------------------------------------------- registry
def _p(name, fn, cells, pivot, covers, cull="cam", seed=None, **kw):
    return dict(name=name, fn=fn, cells=cells, pivot=pivot, covers=covers, cull=cull, seed=seed, kw=kw)


CELL_PIVOT = "cell top centre; place at (cx+0.5, Height01, -(cz+0.5)), no rotation"

# Recommended CM/Toon outline width per piece family (metres of inverted hull). Ground cells get none: a hull on every
# flagstone draws a dark grid over the paving (seen in iteration 2 renders); the bible outlines objects and big masses.
OUTLINE_BY_PREFIX = [("env_ground", 0.0), ("env_wall_breakable_destroyed", 0.0), ("env_bridge_cell", 0.0),
                     ("env_cloud", 0.0), ("env_sky", 0.03), ("env_cliff", 0.03), ("env_edge_stones", 0.04),
                     ("env_hill_plaza", 0.0), ("env_bridge", 0.04), ("", 0.06)]


def outline_width(name):
    for pre, w in OUTLINE_BY_PREFIX:
        if name.startswith(pre):
            return w
    return 0.06

PIECES = [
    _p("env_ground_grass_a", ground_grass, "1x1", CELL_PIVOT, "p12/p15 grass island top; p14 grass tile", seed=11),
    _p("env_ground_grass_b", ground_grass, "1x1", CELL_PIVOT, "p12/p15 grass island top", seed=12),
    _p("env_ground_grass_c", ground_grass, "1x1", CELL_PIVOT, "p12/p15 grass island top", seed=13),
    _p("env_ground_path_a", lambda m, s: ground_path(m, 0, s), "1x1", CELL_PIVOT, "p14 stone flagstone tile; p12 roads", seed=21),
    _p("env_ground_path_b", lambda m, s: ground_path(m, 1, s), "1x1", CELL_PIVOT, "p14 stone flagstone tile; p12 roads", seed=22),
    _p("env_ground_path_c", lambda m, s: ground_path(m, 2, s), "1x1", CELL_PIVOT, "p14 stone flagstone tile; p12 roads", seed=23),
    _p("env_ground_mossy", ground_mossy, "1x1", CELL_PIVOT, "p14 mossy stone tile (path edge)", seed=31),
    _p("env_ground_dirt", ground_dirt, "1x1", CELL_PIVOT, "p14 dirt/grass tile", seed=41),
    _p("env_ground_corrupt", ground_corrupt, "1x1", CELL_PIVOT, "p14 corrupted violet-cracked tile", seed=51),
    _p("env_ground_placed", ground_placed, "1x1", CELL_PIVOT, "CM-30 Miner-placed ground block (p03 stone block)", seed=61),
    _p("env_ground_fill", ground_fill, "1x1", CELL_PIVOT + "; stack at y-0.75 per extra step", "cell side, one extra height step", seed=62),
    _p("env_cliff_a", lambda m, s: cliff(m, 4.5, s), "1x1", CELL_PIVOT + " (hangs below the skirt)", "p12/p15 floating-island cliff", seed=71),
    _p("env_cliff_b", lambda m, s: cliff(m, 6.0, s), "1x1", CELL_PIVOT + " (hangs below the skirt)", "p12/p15 floating-island cliff", seed=72),
    _p("env_cliff_c", lambda m, s: cliff(m, 7.5, s), "1x1", CELL_PIVOT + " (hangs below the skirt)", "p12/p15 floating-island cliff", seed=73),
    _p("env_edge_stones", edge_stones, "1x1", CELL_PIVOT, "p12/p15 stone rim blocks along island edges", seed=75),
    _p("env_wall", wall, "1x1", CELL_PIVOT, "WALL cell: hill ring / shrine yard (p14 wall, mossy ashlar)", seed=81),
    _p("env_wall_breakable_intact", breakable_intact, "1x1", CELL_PIVOT, "p14 wall intact (BREAKABLE_INTACT)", seed=82),
    _p("env_wall_breakable_cracked", breakable_cracked, "1x1", CELL_PIVOT, "p14 wall cracked (BREAKABLE_CRACKED)", seed=83),
    _p("env_wall_breakable_destroyed", breakable_destroyed, "1x1", CELL_PIVOT, "p14 wall destroyed / rubble (BREAKABLE_DESTROYED)", seed=84),
    _p("env_bridge_span", bridge_span, "3 wide x 1 long (along +Z)", "deck-top centre of the near end; yaw to the bridge heading, tile along it", "p12/p15/p14 wooden rope bridge, diagonal crossings", cull="down", seed=91),
    _p("env_bridge_cell", bridge_cell, "1x1", CELL_PIVOT, "BRIDGE cell outside a span (edits)", seed=92),
    _p("env_bridge_post", bridge_post, "0.25x0.25", "post foot centre at deck height", "p14 bridge rope post", seed=93),
    _p("env_bridge_pier", bridge_pier, "1x1", CELL_PIVOT + " (bridge mouth cell)", "p14 bridge stone pier", seed=94),
    _p("env_hill_plaza", hill_plaza, "12x12 (r 6)", "hill centre (sim 56,40, a cell corner) at island height", "p12/p15 hill plaza + crown; p14 hill dais", seed=101),
    _p("env_hill_rim_half", hill_rim_half, "12x6 (half ring r 5.55-6.0)", "hill centre; second half at yaw 180", "p12/p15 hill team rim (split when contested)", seed=102),
    _p("env_spawn_pad", spawn_pad, "6x6 (r 3)", "pad centre (sim 8,36 / 104,36) at island height", "p14 respawn pad; p12 team bases", seed=111),
    _p("env_crystal_cluster", crystal_cluster_piece, "1x1", "cluster base centre", "p12/p14/p15 team crystal clusters", seed=121),
    _p("env_crystal_cluster_small", crystal_cluster_small, "1x1", "cluster base centre", "p12/p14 team crystal clusters", seed=122),
    _p("env_banner", banner, "1x1", "post foot centre", "p14 team banner; p12/p15 base banners", seed=131),
    _p("env_shrine_dais", shrine_dais, "5x5 (r 2.2)", "shrine centre (sim 56,8, a cell corner) at island height 1.5", "p12/p14 egg shrine dais", seed=141),
    _p("env_shrine_horns", shrine_horns, "6x2", "shrine centre", "p12/p14 shrine horn arches", seed=142),
    _p("env_shrine_egg_proxy", shrine_egg_proxy, "2x2", "shrine centre", "STAND-IN for the Kaiju egg (p10/p14)", seed=143),
    _p("env_waterfall", waterfall, "1x1 edge", CELL_PIVOT + "; pours off -Z, yaw 90 for +X", "p12/p15 waterfalls off island edges", cull="down", seed=151),
    _p("env_ruin_column_intact", ruin_column_intact, "1x1 (cap 1.25)", CELL_PIVOT + " (decoration base cell)", "CM-31 knock-down pillar standing; p03 ruin pillar", seed=161),
    _p("env_ruin_column_fallen_ramp", ruin_column_fallen_ramp, "1x1 base + 3x4 along +Z", "base cell top centre; yaw to the ramp direction", "CM-31 pillar knocked to ramp (walkable across void)", cull="down", seed=162),
    _p("env_ruin_column_fallen_block", ruin_column_fallen_block, "1x1 base + 3x4 along +Z", "base cell top centre; yaw to the block direction", "CM-31 pillar knocked to block a path", cull="down", seed=163),
    _p("env_ruin_block", ruin_block, "1x1", CELL_PIVOT, "p03/p12 tumbled ruin blocks", seed=171),
    _p("env_ruin_stub", ruin_stub, "1x1", CELL_PIVOT, "p03/p12 broken pillar stub", seed=172),
    _p("env_ruin_arch", ruin_arch, "3x1", "centre cell top centre", "p03 broken arch", seed=173),
    _p("env_tree_a", lambda m, s: tree(m, s, True), "1x1 (canopy 2.2)", CELL_PIVOT, "p12/p15 round green tree", seed=181),
    _p("env_tree_b", lambda m, s: tree(m, s, False), "1x1 (canopy 1.8)", CELL_PIVOT, "p12/p15 round green tree", seed=182),
    _p("env_bush", bush, "1x1", CELL_PIVOT, "p12/p15 shrubs", seed=183),
    _p("env_rocks", rocks, "1x1", CELL_PIVOT, "p12/p15 loose rocks", seed=184),
    _p("env_sky_rock_s", lambda m, s: sky_rock(m, s, 0.6), "~1.3 dia", "cap top centre", "p12/p15 small floating rocks", seed=191),
    _p("env_sky_rock_m", lambda m, s: sky_rock(m, s, 1.0), "~2.2 dia", "cap top centre", "p12/p15 floating rock spires", seed=192),
    _p("env_sky_rock_l", lambda m, s: sky_rock(m, s, 1.6), "~3.5 dia", "cap top centre", "p12/p15 floating rock spires", seed=193),
    _p("env_cloud_puff", cloud_puff, "~5x3", "puff base centre", "p12/p15 cloud sea puffs", seed=201),
    _p("env_cloud_floor", cloud_floor, "64x64", "centre", "p12/p15 cloud sea backdrop", cull="none", seed=202),
]


def build_piece(spec, collection=None):
    m = Mesh(spec["name"], cull=spec["cull"])
    spec["fn"](m, spec["seed"])
    return m.finish(collection)
