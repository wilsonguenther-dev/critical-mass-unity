"""Where the kit goes on Skybreak Basin (sim cells from simgrid, the 1:1 port of SkybreakBasinMap).

Rules: anything taller than 0.3 u stands on non-walkable cells only (Sim Wall / Breakable cells, or VOID cells just
outside an island edge); every bridge head, ring gap, yard gap and the CM-31 pillar ramp stays open.
A placement is (piece_key, sim_x, sim_z, height, blender_yaw_rad, extra dict). Unity yaw = -degrees(blender yaw).
"""
import math
import random

from mathutils import Vector

from . import kit, pal, pieces, simgrid as G
from .kit import COURSE, Path, Piece, S2B

MAP = G.build_map()
CELLS, HGT = MAP["cells"], MAP["hgt"]
CAM_DIR_B = Vector((-1.0, 1.0)).normalized()     # horizontal direction toward the gameplay camera (Blender XY)


def cell(cx, cz):
    if 0 <= cx < G.W and 0 <= cz < G.H:
        return CELLS[cz * G.W + cx]
    return G.VOID


def hgt(cx, cz):
    if 0 <= cx < G.W and 0 <= cz < G.H:
        return HGT[cz * G.W + cx]
    return 0.0


def walk(cx, cz):
    return G.walkable(cell(cx, cz))


def face_cam_yaw(tangent_angle):
    """Yaw (Blender) among tangent + k*90 deg that best turns local -Y toward the camera."""
    best, by = -9, 0.0
    for k in range(4):
        y = tangent_angle + k * math.pi / 2
        d = Vector((math.sin(y), -math.cos(y)))
        if d.dot(CAM_DIR_B) > best:
            best, by = d.dot(CAM_DIR_B), y
    return by


# ------------------------------------------------------------------------------ base district (Azure; Crimson mirrors)
ISL = (10.0, 36.0, 9.0, 11.0)
PAD = (8.0, 36.0)
WALL_SECTORS = [(24, 40), (80, 262), (327, 347)]     # sim degrees around the base island (0 east, 90 south)
TOWERS = [(116, "tower"), (162, "landmark"), (228, "tower")]


def _edge_offset():
    """Per-degree radial offset k so the wall's inner face clears every walkable cell square (+0.04 margin)."""
    cx, cz, rx, rz = ISL
    raw = {}
    for d in range(360):
        th = math.radians(d)
        k = -0.5
        while k < 4:
            x, z = cx + (rx + k) * math.cos(th), cz + (rz + k) * math.sin(th)
            if all(not walk(math.floor(x + dx), math.floor(z + dz))
                   for dx, dz in ((0, 0), (0.04, 0), (-0.04, 0), (0, 0.04), (0, -0.04))):
                break
            k += 0.02
        raw[d] = k
    mx = {d: max(raw[(d + j) % 360] for j in range(-5, 6)) for d in range(360)}
    return {d: sum(mx[(d + j) % 360] for j in range(-3, 4)) / 7 + 0.02 for d in range(360)}


KOFF = _edge_offset()


def _k(deg):
    d0 = math.floor(deg) % 360
    t = deg - math.floor(deg)
    return KOFF[d0] * (1 - t) + KOFF[(d0 + 1) % 360] * t


def isl_pt(deg, extra, mirror=False):
    cx, cz, rx, rz = ISL
    th = math.radians(deg)
    k = _k(deg) + extra
    x, z = cx + (rx + k) * math.cos(th), cz + (rz + k) * math.sin(th)
    if mirror:
        x = G.MIRROR_X - x
    return x, z


SHELF = 0.95                 # v2: a dressing shelf (VOID edge cells) between the wall's inner face and the island
WALL_OFF = 0.5 + SHELF       # wall centre line beyond the walkable clearance line


def _clear(x, z, r):
    """No walkable cell within r of sim point (x, z) (sampled on two rings and the centre)."""
    for rr in (0.0, r * 0.5, r):
        for k in range(16 if rr else 1):
            a = 2 * math.pi * k / 16
            if walk(math.floor(x + rr * math.cos(a)), math.floor(z + rr * math.sin(a))):
                return False
    return True


def yaw_front(d):
    """Blender yaw that turns a piece's local -Y toward the Blender XY direction d."""
    return math.atan2(d.x, -d.y)


def district(team="azure"):
    """Returns (pieces_world: list of Piece built in world Blender coords, placements, info).
    Crimson is the true mirror of Azure across sim x = 56 (same angles, same seeds, mirrored yaw jitter)."""
    mirror = team == "crimson"
    mj = (lambda j: -j) if mirror else (lambda j: j)
    rng = random.Random(101)
    world = []
    place = []
    info = {"wall_paths": [], "gates": []}
    cuts = []
    for deg, kind in TOWERS:
        R = 2.25 if kind == "landmark" else 2.0
        cuts.append((deg, math.degrees(R / 10.0) * 0.92, kind, R))
    runs = []
    for a0, a1 in WALL_SECTORS:
        segs = [(a0, a1)]
        for deg, half, _, _ in cuts:
            nxt = []
            for s0, s1 in segs:
                if s0 < deg < s1:
                    nxt += [(s0, deg - half), (deg + half, s1)]
                else:
                    nxt.append((s0, s1))
            segs = nxt
        runs += segs
    wall = Piece(f"cm_district_walls_{team}", team=True)
    found = Piece(f"cm_district_footing_{team}")
    cxm = G.MIRROR_X - ISL[0] if mirror else ISL[0]

    def put(key, q, h, yaw):
        place.append((key, -q.x, q.y, h, yaw, {}))
    for i, (a0, a1) in enumerate(runs):
        n = max(8, int((a1 - a0) * 2.5))
        pts = []
        for j in range(n + 1):
            d = a0 + (a1 - a0) * j / n
            x, z = isl_pt(d, WALL_OFF, mirror)
            pts.append((-x, z))
        p = pieces.outward_path(pts, (-cxm, ISL[1]))
        kit.wall_run(wall, p, random.Random(200 + i), team_caps=True, moss_p=0.3)
        _footing(found, p, random.Random(300 + i))
        info["wall_paths"].append(p)
        # braziers on the walkway every 5 u (pools fall on the ground inside the wall)
        s = 1.6
        while s < p.L - 1.2:
            put("brazier", p.at(s, -0.12, 2.31), 2.31, 0.0)
            put("pool", p.at(s, -2.1, 0.0), 0.0, 0.0)
            s += 5.0
        # walkway: a crate and a barrel between braziers (kept to the wall top)
        s = 3.0
        while s < p.L - 1.0:
            t = p.tan(s)
            put("crate", p.at(s, -0.12, 2.31), 2.31, math.atan2(t.y, t.x) + mj(rng.uniform(-0.2, 0.2)))
            put("barrel", p.at(s + 0.75, -0.15, 2.31), 2.31, 0.0)
            if rng.random() < 0.5:
                put("crate_small", p.at(s + 1.35, -0.12, 2.31), 2.31, math.atan2(t.y, t.x) + mj(rng.uniform(-0.5, 0.5)))
            s += 2.5
        # shelf vignettes against the inner face (on VOID edge cells): crate stacks, barrel groups, weapon racks
        inward = lambda ss: -p.nrm(ss)
        o = -(0.5 + SHELF / 2)
        s = 1.4 + rng.uniform(0.0, 0.6)
        while s < p.L - 2.0:
            kind = rng.choice(("crates", "barrels", "rack", "crates", "mixed"))
            t = p.tan(s)
            along = math.atan2(t.y, t.x)
            if kind == "crates":
                put("crate", p.at(s, o + 0.05, 0.0), 0.0, along + mj(rng.uniform(-0.12, 0.12)))
                put("crate", p.at(s + 0.76, o + 0.08, 0.0), 0.0, along + mj(rng.uniform(-0.2, 0.2)))
                put("crate_small", p.at(s + 0.1, o + 0.05, 0.7), 0.7, along + mj(rng.uniform(-0.5, 0.5)))
                put("crate_small", p.at(s + 1.36, o + 0.12, 0.0), 0.0, along + mj(rng.uniform(-0.6, 0.6)))
                s += 1.7
            elif kind == "barrels":
                for ds, do in ((0.0, 0.14), (0.62, 0.16), (0.32, -0.15), (1.0, -0.1)):
                    put("barrel", p.at(s + ds, o + do, 0.0), 0.0, rng.uniform(0, 6.28))
                s += 1.4
            elif kind == "rack":
                put("weapon_rack", p.at(s + 0.7, o + 0.1, 0.0), 0.0, yaw_front(inward(s + 0.7)))
                put("barrel", p.at(s + 1.7, o + 0.1, 0.0), 0.0, rng.uniform(0, 6.28))
                s += 2.1
            else:
                put("crate", p.at(s, o + 0.05, 0.0), 0.0, along + mj(rng.uniform(-0.3, 0.3)))
                put("barrel", p.at(s + 0.72, o + 0.12, 0.0), 0.0, 0.0)
                put("barrel", p.at(s + 1.25, o + 0.12, 0.0), 0.0, 0.0)
                s += 1.6
            s += rng.uniform(0.7, 1.4)          # rest areas between vignettes
    world += [wall, found]
    # gates at every route-side wall end: gate post with banner on the wall line, a supply cache on the shelf just
    # inside it, stakes jammed into the cliff lip outside the wall pointing out, a crystal lamp and brazier on top
    for a0, a1 in WALL_SECTORS:
        for deg, inward_sgn in ((a0, 1), (a1, -1)):
            d = deg + inward_sgn * 2.0
            x, z = isl_pt(d, WALL_OFF + 0.12, mirror)
            xo, zo = isl_pt(d, WALL_OFF + 2.0, mirror)
            out = Vector((-(xo - x), zo - z)).normalized()
            place.append(("gate_post", x, z, 0.0, math.atan2(out.x, -out.y), {}))
            info["gates"].append((x, z))
            xl, zl = isl_pt(deg + inward_sgn * 7.5, WALL_OFF - 0.08, mirror)
            place.append(("crystal_lamp", xl, zl, 2.31, 0.0, {}))
            xb, zb = isl_pt(deg + inward_sgn * 4.6, WALL_OFF - 0.12, mirror)
            place.append(("brazier", xb, zb, 2.31, 0.0, {}))
            xp, zp = isl_pt(deg + inward_sgn * 4.6, -1.0, mirror)
            place.append(("pool", xp, zp, 0.0, 0.0, {}))
            # supply cache on the shelf beside the post
            for k, (dd, key) in enumerate(((5.0, "barrel"), (6.6, "barrel"), (8.4, "crate"))):
                xc, zc = isl_pt(deg + inward_sgn * dd, WALL_OFF - 0.5 - SHELF / 2 + 0.05, mirror)
                place.append((key, xc, zc, 0.0, mj(rng.uniform(-0.3, 0.3)) + (0.0 if key == "barrel" else
                              math.atan2(-(zc - z), -(xc - x))), {}))
            # stakes outside the wall on a stone lip, pointing out of the base (only where no walkable cell is near:
            # some wall ends sit beside a bridge head)
            for dd in (6.0, 8.5, 11.0):
                xs_, zs_ = isl_pt(deg + inward_sgn * dd, WALL_OFF + 1.2, mirror)
                if _clear(xs_, zs_, 1.25):
                    break
            else:
                continue
            xs2, zs2 = isl_pt(deg + inward_sgn * dd, WALL_OFF + 3.0, mirror)
            out2 = Vector((-(xs2 - xs_), zs2 - zs_)).normalized()
            place.append(("stakes", xs_, zs_, 0.0, yaw_front(-out2), {}))
            lip = Piece(f"tmp_lip_{team}_{deg}")
            xs3, zs3 = isl_pt(deg + inward_sgn * dd, WALL_OFF + 1.1, mirror)
            ang = math.atan2(out2.y, out2.x) + math.pi / 2
            lip.box(0, 0, -0.75, 2.2, 1.0, 0.78, pal.vc("body_a", 0.9), pal.vc("pave_d"), rot=ang, bev=0.05,
                    chips=kit.rand_chips(rng, 0.6, 0.2, 0.08, 0.15))
            lip.name = f"cm_district_gate_lip_{team}_{deg}"
            place.append(("__piece__", xs3, zs3, 0.0, 0.0, {"piece": lip}))
    # towers, doors to the pad
    padx = G.MIRROR_X - PAD[0] if mirror else PAD[0]
    for deg, half, kind, R in cuts:
        x, z = isl_pt(deg, R + 0.1, mirror)
        dvec = S2B(padx, PAD[1]) - S2B(x, z)
        yaw = math.atan2(dvec.y, dvec.x) + math.pi / 2
        place.append(("landmark" if kind == "landmark" else "tower", x, z, 0.0, yaw, {}))
        tf = Piece(f"tmp_footing_{deg}")
        kit.drum(tf, random.Random(deg), R + 0.1, 3, z0=-3 * COURSE - 0.02, thick=0.6)
        tf.name = f"cm_district_tower_footing_{team}_{deg}"
        place.append(("__piece__", x, z, 0.0, 0.0, {"piece": tf}))
    info["landmark"] = [isl_pt(deg, R + 0.1, mirror) for deg, half, kind, R in cuts if kind == "landmark"][0]
    # the pad: lanes around it stay clear (no ground scatter in v2)
    place.append(("pad", padx, PAD[1], 0.0, 0.0, {}))
    return world, place, info


def _footing(pc, p, rng):
    """Foundation under a wall standing on VOID edge cells: rock-dark courses down the cliff under the wall and the
    shelf, and a flat flagstone apron (0.05 u) over the shelf and the strip up to the island cells."""
    inner = 0.5 + SHELF
    for c in range(1, 4):
        z1 = -(c - 1) * COURSE
        xs = kit.course_breaks(p.L, rng, c % 2 == 1)
        for a, b in zip(xs, xs[1:]):
            side = pal.vc("body_a", rng.uniform(0.85, 0.95))
            inset = 0.06 * c
            pc.block(kit.hexa_on_path(p, a + 0.015, b - 0.015, -inner + inset, 0.5 - inset * 0.5, z1 - COURSE + 0.015,
                                      z1 - 0.015), side, side)
    s = 0.0
    while s < p.L:
        w = rng.uniform(0.5, 0.9)
        for o0, o1 in ((-inner - 0.75, -inner + 0.02), (-inner + 0.05, -0.5)):
            c = pal.vc(rng.choice(["pave", "pave_d", "cap_side"]), rng.uniform(0.94, 1.04))
            pc.block(kit.hexa_on_path(p, s + 0.02, min(p.L, s + w) - 0.02, o0, o1, -0.05, 0.03), c, c, bev=0.02)
        s += w


# ------------------------------------------------------------------------------ ring fits
def ring_cells(cx0, cz0, rmin, rmax, south_only=False):
    out = []
    for cz in range(G.H):
        for cx in range(G.W):
            if cell(cx, cz) not in (G.WALL, G.B_INTACT, G.B_CRACKED):
                continue
            dx, dz = cx + 0.5 - cx0, cz + 0.5 - cz0
            r = math.hypot(dx, dz)
            if rmin <= r <= rmax and (dz > 0 or not south_only):
                out.append((math.degrees(math.atan2(abs(dz), abs(dx))), r, cx, cz))
    return sorted(out)


def _rho_fn(cells, win=7.0):
    def f(a):
        a = a % 360
        a = a % 180
        a = 180 - a if a > 90 else a
        ws = [(max(0.0, 1 - abs(c[0] - a) / win), c[1]) for c in cells if abs(c[0] - a) < win]
        if not ws:
            near = min(cells, key=lambda c: abs(c[0] - a))
            return near[1]
        return sum(w * r for w, r in ws) / sum(w for w, _ in ws)
    return f


HILL_CELLS = ring_cells(56, 40, 9, 12)
SHRINE_CELLS = ring_cells(56, 8, 3.5, 5.5, south_only=True)
pieces.RHO["hill"] = _rho_fn(HILL_CELLS)
pieces.RHO["shrine"] = _rho_fn(SHRINE_CELLS, 12.0)


def gap_end_cells(cells, cx0, cz0, gap_centres, half):
    """For each gap and side, the ring cell nearest the gap edge (true sim angles, 0 east, 90 south)."""
    out = []
    full = []
    for cz in range(G.H):
        for cx in range(G.W):
            if cell(cx, cz) in (G.WALL, G.B_INTACT) and (cx, cz) in {(c[2], c[3]) for c in cells}:
                a = math.degrees(math.atan2(cz + 0.5 - cz0, cx + 0.5 - cx0)) % 360
                full.append((a, cx, cz))
    for g in gap_centres:
        for sgn in (-1, 1):
            cand = [(abs(((a - g + 180) % 360) - 180), a, cx, cz) for a, cx, cz in full
                    if 0 < sgn * (((a - g + 180) % 360) - 180) < half + 14]
            if cand:
                _, a, cx, cz = min(cand)
                out.append((a, cx + 0.5, cz + 0.5))
    return out


# ------------------------------------------------------------------------------ hill
HILL = (56.0, 40.0, 0.75)
HILL_ARCS = [(12, 37), (53, 78)]


def hill(states=("intact",) * 4):
    """Placements relative to the hill centre (pieces authored around a local origin at the hill centre)."""
    place = []
    cx, cz, h = HILL
    for q in range(4):
        yaw = -math.radians(90 * q)       # sim angle +90 = Blender rotation -90 (blender y = sim z, x mirrored)
        place.append(("hill_arc_a", cx, cz, h, yaw, {}))
        place.append(("hill_arc_b", cx, cz, h, yaw, {}))
        place.append((f"hill_breakable_{states[q]}", cx, cz, h, yaw, {}))
    rho = pieces.RHO["hill"]
    for ad, x, z in gap_end_cells(HILL_CELLS, cx, cz, (0, 90, 180, 270), 12):
        a = math.radians(ad)
        place.append(("gate_post_ring", x, z, h, math.atan2(-math.cos(a), -math.sin(a)), {}))
    for card in (0, 90, 180, 270):
        for sgn in (-1, 1):
            bd = card + sgn * 21.5
            b = math.radians(bd)
            bx, bz = cx + (rho(bd) - 0.12) * math.cos(b), cz + (rho(bd) - 0.12) * math.sin(b)
            place.append(("brazier", bx, bz, h + 2.31, 0.0, {}))
            px, pz = cx + 8.6 * math.cos(b), cz + 8.6 * math.sin(b)
            place.append(("pool", px, pz, h, 0.0, {}))
    place.append(("hill_horn_l", cx, cz, h, 0.0, {}))
    place.append(("hill_horn_r", cx, cz, h, 0.0, {}))
    for a in (243, 297):
        r = math.radians(a)
        place.append(("crystal_lamp", cx + (rho(a) - 0.1) * math.cos(r), cz + (rho(a) - 0.1) * math.sin(r), h + 2.31, 0.0, {}))
    place.append(("hill_dais", cx, cz, h, 0.0, {}))
    place.append(("hill_rim_w", cx, cz, h, 0.0, {}))
    place.append(("hill_rim_e", cx, cz, h, math.pi, {}))
    place.append(("marker", cx, cz, h + 5.2, 0.0, {}))
    return place


# ------------------------------------------------------------------------------ shrine
SHRINE = (56.0, 8.0, 1.5)


def shrine(state="intact"):
    place = []
    cx, cz, h = SHRINE
    place.append(("shrine_core", cx, cz, h, 0.0, {}))
    place.append(("shrine_egg", cx, cz, h + pieces.SHRINE_DAIS_TOP - 0.05, 0.0, {}))
    place.append(("shrine_yard", cx, cz, h, 0.0, {}))
    place.append((f"shrine_breakable_{state}", cx, cz, h, 0.0, {}))
    place.append((f"shrine_breakable_{state}", cx, cz, h, -math.pi / 2, {}))
    rho = pieces.RHO["shrine"]
    for ad, x, z in gap_end_cells(SHRINE_CELLS, cx, cz, (90,), 15):
        r = math.radians(ad)
        place.append(("gate_post_ring", x, z, h, math.atan2(-math.cos(r), -math.sin(r)), {}))
    for a in (8, 26, 62, 118, 154, 172):
        r = math.radians(a)
        place.append(("brazier", cx + rho(a) * math.cos(r), cz + rho(a) * math.sin(r), h + 0.75, 0.0, {}))
        place.append(("pool", cx + 3.4 * math.cos(r), cz + 3.4 * math.sin(r), h, 0.0, {}))
    place.append(("egg_pool", cx, cz, h, 0.0, {}))
    place.append(("marker", cx, cz, h + 7.4, 0.0, {}))
    return place


def route_cells():
    """Cells that must stay open: ring gaps, yard gap, bridge heads near the districts, the pillar ramp strip."""
    out = set()
    for cz in range(G.H):
        for cx in range(G.W):
            t = cell(cx, cz)
            if t == G.BRIDGE:
                out.add((cx, cz))
            dx, dz = cx + 0.5 - 56, cz + 0.5 - 40
            r = math.hypot(dx, dz)
            if 9.5 < r < 11.5 and t == G.FLOOR:
                a = math.degrees(math.atan2(dz, abs(dx))) % 360
                if min(abs(a - c) % 360 for c in (0, 90, 270, 360)) <= 12:
                    out.add((cx, cz))
            dx, dz = cx + 0.5 - 56, cz + 0.5 - 8
            if 3.5 < math.hypot(dx, dz) < 5.5 and dz > 0 and abs(math.degrees(math.atan2(dz, abs(dx))) - 90) <= 15:
                out.add((cx, cz))
    for mx in (False, True):
        for cz in range(23, 27):
            for cx in range(11, 14):
                out.add((G.W - 1 - cx, cz) if mx else (cx, cz))
    return out


# ------------------------------------------------------------------------------ proposal for the game code (not applied)
def _cells_where(cx0, cz0, pred):
    out = []
    for cz in range(G.H):
        for cx in range(G.W):
            dx, dz = cx + 0.5 - cx0, cz + 0.5 - cz0
            r = math.hypot(dx, dz)
            a = math.degrees(math.atan2(dz, dx)) % 360
            if pred(r, a) and cell(cx, cz) in (G.FLOOR,):
                out.append([cx, cz])
    return out


def solid_cell_proposal():
    """Which FLOOR cells would have to become Sim Wall (solid, non-walkable) so the shrine and the hill could be built
    at full bible p14 size. A PROPOSAL for SkybreakBasinMap: nothing here changes the game."""
    near = lambda a, c, h: min(abs(a - c) % 360, 360 - abs(a - c) % 360) <= h
    sx, sz = 56, 8
    shrine_dais = _cells_where(sx, sz, lambda r, a: r <= 1.6)
    shrine_feet = [c for c in ([57, 6], [54, 9]) if cell(*c) == G.FLOOR]
    hx, hz = 56, 40
    horn_feet = _cells_where(hx, hz, lambda r, a: 6.2 <= r <= 7.6 and (near(a, 236, 7) or near(a, 304, 7)))
    stones = []
    for ang in (30, 60, 120, 150, 210, 330):
        a = math.radians(ang)
        stones.append([math.floor(hx + 7.4 * math.cos(a)), math.floor(hz + 7.4 * math.sin(a))])
    kerb = _cells_where(hx, hz, lambda r, a: 6.0 <= r <= 6.9 and not any(near(a, c, 16) for c in (0, 90, 180, 270)))
    return {
        "note": "Sim cell coords (x, z) of SkybreakBasinMap; FLOOR -> WALL. Proposal only, game code untouched.",
        "shrine_full_size": {
            "why": "bible dais is about 3.6 u across (2.4 x the egg) with the horn feet at +-2.1 u; the Sim gives a 2 x 2 "
                   "solid square, so today the dais is 2 x 2 x 1.5 u and the horns swing out only above 2.1 u",
            "dais_cells_r_le_1_6": shrine_dais, "horn_foot_cells": shrine_feet,
            "count": len(shrine_dais) + len(shrine_feet),
            "lane_left_m": round(G.YARD_INNER - 2.1, 2)},
        "hill_full_size": {
            "why": "bible horns and standing stones stand on the dais edge (r 6 to 7.5) and the kerb is a raised step; "
                   "today everything inside the ring is walkable, so the horns grow from the ring wall (r 10.5) and the "
                   "kerb is held to 0.3 u",
            "horn_foot_cells_236_304_deg": horn_feet, "standing_stone_cells": stones,
            "raised_kerb_cells_optional": kerb, "kerb_openings_deg": "0, 90, 180, 270 +-16 (aligned with the ring gaps)",
            "count_minimum": len(horn_feet) + len(stones), "count_with_kerb": len(horn_feet) + len(stones) + len(kerb)},
    }
