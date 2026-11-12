"""Skybreak Basin cell grid (a 1:1 Python port of browser src/sim/map.ts at 09e0904, the source F-03 ports) and the
VIEW-SIDE placement rules that turn the grid into kit pieces. Pure Python: no bpy, so it can be unit-checked.

The Sim owns cells and heights. Everything else here (which ground variant a cell gets, where cliffs hang, trees,
banners, waterfalls, sky rocks) is decoration the Unity view derives deterministically from the grid; the rules are
written so they can be ported to C# as they are (see README "How the Unity side places pieces").
"""
import math

VOID, FLOOR, WALL, B_INTACT, B_CRACKED, B_DESTROYED, WATER, BRIDGE, BARRICADE = range(9)
W, H = 112, 72
MIRROR_X = 112

ISLANDS = [
    (10, 36, 9, 11, 0.0, True), (28, 18, 8, 6, 0.0, True), (28, 56, 8, 6, 0.0, True),
    (56, 40, 17, 14, 0.75, False), (56, 10, 12, 7, 1.5, False), (56, 65, 9, 5, 0.0, False),
]
OVERLOOK = (11, 18, 4.5, 4, 0.0, True)
BRIDGE_HALF_WIDTH = 1.5
BRIDGES = [
    (18, 37, 40, 40, True), (13, 27, 23, 21, True), (35, 20, 44, 30, True), (13, 45, 23, 52, True),
    (35, 55, 44, 50, True), (35, 15, 45, 11, True), (56, 27, 56, 16, False), (56, 53, 56, 61, False),
    (35, 59, 48, 64, True),
]
HILL = (56, 40, 6.0)
RING_R = 10.5
RING_GAP_HALF_DEG = 12
RING_BREAKABLE = [45, 135, 225, 315]
RING_BREAKABLE_HALF = 8
SHRINE = (56, 8, 1.5)
YARD_INNER, YARD_OUTER = 4, 5
YARD_GAP_CENTRE, YARD_GAP_HALF = 90, 15
YARD_BREAKABLE = [45, 135]
YARD_BREAKABLE_HALF = 8
PAD_R = 3
PADS = {"azure": (8, 36), "crimson": (MIRROR_X - 8, 36)}
WEST_BIOMASS = [(24, 16), (31, 20), (24, 58), (31, 54)]
CORRUPTION = [(52, 65), (56, 67), (60, 65)]
WEST_DECORATIONS = [dict(cx=12, cz=26, ramp=(0, -1), block=(1, 0))]
TOPPLE_LEN, TOPPLE_W = 4, 3


def _inside(is_, x, z):
    u = (x - is_[0]) / is_[2]
    v = (z - is_[1]) / is_[3]
    return u * u + v * v <= 1


def _endpoint_h(x, z):
    best, h = float("inf"), 0.0
    for is_ in ISLANDS:
        u = (x - is_[0]) / is_[2]
        v = (z - is_[1]) / is_[3]
        d = u * u + v * v
        if d < best:
            best, h = d, is_[4]
    return h


def _bridge_t(b, x, z):
    ex, ez = b[2] - b[0], b[3] - b[1]
    t = ((x - b[0]) * ex + (z - b[1]) * ez) / (ex * ex + ez * ez)
    t = min(1.0, max(0.0, t))
    px, pz = b[0] + ex * t - x, b[1] + ez * t - z
    return t if px * px + pz * pz <= BRIDGE_HALF_WIDTH ** 2 else -1


def _ang(dx, dz):
    a = math.degrees(math.atan2(dz, dx))
    return a + 360 if a < 0 else a


def _angdiff(a, b):
    d = abs(a - b) % 360
    return 360 - d if d > 180 else d


def _near(a, cs, half):
    return any(_angdiff(a, c) <= half for c in cs)


def _js_round(v):
    return math.floor(v + 0.5)   # exact for the angle range used here (a / 90 in [0, 4])


def _on_ring(dx, dz):
    ax, az = abs(dx), abs(dz)
    r2 = RING_R * RING_R
    if az <= ax and az < RING_R and ax == math.floor(math.sqrt(r2 - az * az)) + 0.5:
        return True
    if ax <= az and ax < RING_R and az == math.floor(math.sqrt(r2 - ax * ax)) + 0.5:
        return True
    return False


def build_map():
    cells = [VOID] * (W * H)
    hgt = [0.0] * (W * H)
    for is_ in ISLANDS + [OVERLOOK]:
        for cz in range(H):
            for cx in range(W):
                xc, zc = cx + 0.5, cz + 0.5
                if _inside(is_, xc, zc) or (is_[5] and _inside(is_, MIRROR_X - xc, zc)):
                    cells[cz * W + cx] = FLOOR
                    hgt[cz * W + cx] = is_[4]
    for b in BRIDGES:
        h0, h1 = _endpoint_h(b[0], b[1]), _endpoint_h(b[2], b[3])
        for cz in range(H):
            for cx in range(W):
                i = cz * W + cx
                if cells[i] != VOID:
                    continue
                xc, zc = cx + 0.5, cz + 0.5
                t = _bridge_t(b, xc, zc)
                if t < 0 and b[4]:
                    t = _bridge_t(b, MIRROR_X - xc, zc)
                if t < 0:
                    continue
                cells[i] = BRIDGE
                hgt[i] = h0 + (h1 - h0) * t
    for cz in range(H):
        dz = cz + 0.5 - HILL[1]
        for cx in range(W):
            dx = abs(cx + 0.5 - HILL[0])
            if not _on_ring(dx, dz):
                continue
            a = _ang(dx, dz)
            if _angdiff(a, _js_round(a / 90) * 90) <= RING_GAP_HALF_DEG:
                continue
            cells[cz * W + cx] = B_INTACT if _near(a, RING_BREAKABLE, RING_BREAKABLE_HALF) else WALL
    for cz in range(H):
        zc = cz + 0.5
        if not zc > SHRINE[1]:
            continue
        dz = zc - SHRINE[1]
        for cx in range(W):
            dx = abs(cx + 0.5 - SHRINE[0])
            d = math.sqrt(dx * dx + dz * dz)
            if d < YARD_INNER or d >= YARD_OUTER:
                continue
            a = _ang(dx, dz)
            i = cz * W + cx
            if _angdiff(a, YARD_GAP_CENTRE) <= YARD_GAP_HALF:
                cells[i] = FLOOR
            elif _near(a, YARD_BREAKABLE, YARD_BREAKABLE_HALF):
                cells[i] = B_INTACT
            else:
                cells[i] = WALL
    for cz in range(H):
        dz = cz + 0.5 - SHRINE[1]
        for cx in range(W):
            dx = abs(cx + 0.5 - SHRINE[0])
            if dx * dx + dz * dz <= SHRINE[2] ** 2:
                cells[cz * W + cx] = WALL
    decos = [dict(d) for d in WEST_DECORATIONS]
    for d in WEST_DECORATIONS:
        decos.append(dict(cx=W - 1 - d["cx"], cz=d["cz"], ramp=(-d["ramp"][0], d["ramp"][1]),
                          block=(-d["block"][0], d["block"][1])))
    return dict(cells=cells, hgt=hgt, decorations=decos)


# ------------------------------------------------------------------------------------------- view-side placement
def h32(cx, cz, salt=0):
    """Deterministic integer hash (portable to C#: unchecked uint arithmetic)."""
    x = (cx * 73856093) ^ (cz * 19349663) ^ (salt * 83492791)
    x &= 0xFFFFFFFF
    x ^= x >> 13
    x = (x * 0x5BD1E995) & 0xFFFFFFFF
    x ^= x >> 15
    return x


def walkable(t):
    return t in (FLOOR, BRIDGE, B_DESTROYED)


def solid_ground(t):
    """Cells that draw island ground (and so may need a cliff below)."""
    return t in (FLOOR, WALL, B_INTACT, B_CRACKED, B_DESTROYED, BARRICADE)


def _seg_dist(px, pz, ax, az, bx, bz):
    ex, ez = bx - ax, bz - az
    t = max(0.0, min(1.0, ((px - ax) * ex + (pz - az) * ez) / (ex * ex + ez * ez)))
    return math.hypot(ax + ex * t - px, az + ez * t - pz)


def _island_of(x, z):
    best, c = float("inf"), None
    for is_ in ISLANDS:
        for mx in ((is_[0],) + ((MIRROR_X - is_[0],) if is_[5] else ())):
            u = (x - mx) / is_[2]
            v = (z - is_[1]) / is_[3]
            d = u * u + v * v
            if d < best:
                best, c = d, (mx, is_[1])
    return c


def road_segments():
    """Paved roads: every bridge end runs to its island's hub (pad, hill, shrine or island centre)."""
    hubs = {(10, 36): PADS["azure"], (102, 36): PADS["crimson"], (56, 40): (56, 40), (56, 10): (56, 13)}
    segs = []
    for b in BRIDGES:
        for mir in ((False, True) if b[4] else (False,)):
            x0, z0, x1, z1 = b[:4]
            if mir:
                x0, x1 = MIRROR_X - x0, MIRROR_X - x1
            for (x, z) in ((x0, z0), (x1, z1)):
                isl = _island_of(x, z)
                hub = hubs.get(isl, isl)
                segs.append((x, z, hub[0], hub[1]))
    return segs


def ground_variant(m, cx, cz, roads):
    t = m["cells"][cz * W + cx]
    xc, zc = cx + 0.5, cz + 0.5
    if t == B_DESTROYED:
        return "env_wall_breakable_destroyed"
    for (nx, nz) in CORRUPTION:
        if math.hypot(xc - nx, zc - nz) < 2.4:
            return "env_ground_corrupt"
    if math.hypot(xc - 56, zc - 40) <= 10.0 or math.hypot(xc - 56, zc - 8) <= 4.0:
        if math.hypot(xc - 56, zc - 40) > 7.0 and h32(cx, cz, 31) % 100 < 18:
            return "env_ground_mossy"
        return "env_ground_path_" + "abc"[h32(cx, cz, 3) % 3]
    for p in PADS.values():
        if math.hypot(xc - p[0], zc - p[1]) <= PAD_R + 2.5:
            return "env_ground_path_" + "abc"[h32(cx, cz, 3) % 3]
    d = min(_seg_dist(xc, zc, *s) for s in roads)
    for isl in ISLANDS:      # courtyard / corruption island centres are paved yards
        for mx in ((isl[0],) + ((MIRROR_X - isl[0],) if isl[5] else ())):
            if isl[4] == 0.0 and math.hypot(xc - mx, zc - isl[1]) <= 3.0:
                d = 0.0
    if d <= 2.2:
        if d > 0.8 and h32(cx, cz, 29) % 100 < 14:     # grass breaking through the paving (bible p12/p15)
            return "env_ground_mossy"
        return "env_ground_path_" + "abc"[h32(cx, cz, 3) % 3]
    if d <= 3.4 or (d <= 4.2 and h32(cx, cz, 9) % 2):
        return "env_ground_mossy"
    for (bx, bz) in WEST_BIOMASS + [(MIRROR_X - x, z) for (x, z) in WEST_BIOMASS]:
        if math.hypot(xc - bx, zc - bz) < 1.6:
            return "env_ground_dirt"
    return "env_ground_grass_" + "abc"[h32(cx, cz, 5) % 3]


def is_edge(m, cx, cz):
    """True when the camera can see below this cell: its east (+x), south (+z) or south-east neighbour is not ground
    at the same or a greater height (void, bridge, out of map or lower ground)."""
    c, h = m["cells"], m["hgt"]
    me = h[cz * W + cx]
    for (dx, dz) in ((1, 0), (0, 1), (1, 1)):
        x, z = cx + dx, cz + dz
        if not (0 <= x < W and 0 <= z < H):
            return True
        j = z * W + x
        if not solid_ground(c[j]) or h[j] < me - 1e-6:
            return True
    return False


def place(m):
    """Return a list of (piece, sim_x, height, sim_z, unity_yaw_deg, extra) placements for the whole map.
    unity_yaw_deg is Unity's Euler Y (Quaternion.Euler(0, yaw, 0)); None = a span (extra holds its far end)."""
    out = []
    cells, hgt = m["cells"], m["hgt"]
    roads = road_segments()
    keep_out = [(56, 40, 11.2), (56, 8, 5.4)] + [(p[0], p[1], PAD_R + 1.5) for p in PADS.values()]

    def kept(xc, zc):
        return any(math.hypot(xc - a, zc - b) < r for (a, b, r) in keep_out)

    spans = all_spans(m)
    for cz in range(H):
        for cx in range(W):
            i = cz * W + cx
            t, h = cells[i], hgt[i]
            xc, zc = cx + 0.5, cz + 0.5
            if t == VOID:
                continue
            if t == BRIDGE:
                # Diagonal crossings: the rasterised bridge is stair-stepped, so cell corners stick out of the 3 m
                # span. Any bridge cell not fully under a span gets a plank cell just below the span deck, so every
                # walkable cell reads as walkable (Travis must-have: bridges are crossed diagonally).
                if not _cell_under_span(cx, cz, spans):
                    out.append(("env_bridge_cell", xc, h - 0.06, zc, 0, None))
                continue
            if t == FLOOR or t == B_DESTROYED:
                out.append((ground_variant(m, cx, cz, roads), xc, h, zc, 0, None))
            elif t == WALL:
                in_shrine = math.hypot(xc - 56, zc - 8) <= SHRINE[2] + 0.01
                out.append(("env_ground_path_a" if in_shrine else "env_wall", xc, h, zc, 0, None))
            elif t == B_INTACT:
                out.append(("env_wall_breakable_intact", xc, h, zc, 0, None))
            elif t == B_CRACKED:
                out.append(("env_wall_breakable_cracked", xc, h, zc, 0, None))
            if is_edge(m, cx, cz):
                out.append(("env_cliff_" + "abc"[h32(cx, cz, 7) % 3], xc, h, zc, 0, None))
                # foliage, rim stones and ruins hug island edges (bible p12) and stay off keep-outs
                if t == FLOOR and not kept(xc, zc):
                    gv = ground_variant(m, cx, cz, roads)
                    r = h32(cx, cz, 11) % 100
                    team_side = xc < 36 or xc > 76
                    if gv.startswith("env_ground_grass") or gv == "env_ground_mossy" or gv == "env_ground_dirt":
                        if r < 14:
                            out.append(("env_tree_" + "ab"[r % 2], xc, h, zc, 0, None))
                        elif r < 26:
                            out.append(("env_bush", xc, h, zc, 0, None))
                        elif r < 33:
                            out.append(("env_rocks", xc, h, zc, 0, None))
                        elif r < 39:
                            out.append(("env_ruin_block", xc, h, zc, 0, None))
                        elif r < 42:
                            out.append(("env_ruin_stub", xc, h, zc, 0, None))
                        elif r < 48 and team_side:
                            out.append(("env_crystal_cluster_small", xc, h, zc, 0, "azure" if xc < 56 else "crimson"))
                        elif r < 75:
                            out.append(("env_edge_stones", xc, h, zc, 0, None))
                    elif gv.startswith("env_ground_path") and r < 45:
                        out.append(("env_edge_stones", xc, h, zc, 0, None))
                # waterfalls pour off south and east edges at a few deterministic cells
                if t == FLOOR and h32(cx, cz, 13) % 100 < 8:
                    s = cz + 1 >= H or cells[(cz + 1) * W + cx] == VOID
                    e = cx + 1 >= W or cells[cz * W + cx + 1] == VOID
                    if s:
                        out.append(("env_waterfall", xc, h, zc, 0, None))
                    elif e:
                        out.append(("env_waterfall", xc, h, zc, 270, None))
            elif t == FLOOR and not kept(xc, zc):
                # sparse small dressing on interior grass (never anything taller than a bush)
                gv = ground_variant(m, cx, cz, roads)
                r = h32(cx, cz, 23) % 100
                if gv.startswith("env_ground_grass") and r < 7:
                    out.append((("env_bush", "env_rocks")[r % 2], xc, h, zc, 0, None))
    # objectives
    out.append(("env_hill_plaza", 56.0, 0.75, 40.0, 0, None))
    out.append(("env_hill_rim_half", 56.0, 0.75, 40.0, 90, "azure"))      # west half (bible: blue left)
    out.append(("env_hill_rim_half", 56.0, 0.75, 40.0, 270, "crimson"))   # east half (red right)
    for team, (px, pz) in PADS.items():
        out.append(("env_spawn_pad", float(px), 0.0, float(pz), 0, team))
        sx = 1 if team == "azure" else -1
        for (dx, dz) in ((2.5, -3.6), (2.5, 3.6), (-3.2, -2.2), (-3.2, 2.2)):
            out.append(("env_banner", px + sx * dx, 0.0, pz + dz, 0, team))
        for (dx, dz, n) in ((-5.0, -5.5, "env_crystal_cluster"), (-5.5, 5.0, "env_crystal_cluster"),
                            (4.0, -6.5, "env_crystal_cluster_small"), (4.5, 6.5, "env_crystal_cluster_small"),
                            (-6.5, 0.5, "env_crystal_cluster_small")):
            out.append((n, px + sx * dx, 0.0, pz + dz, 0, team))
    out.append(("env_shrine_dais", 56.0, 1.5, 8.0, 0, None))
    out.append(("env_shrine_horns", 56.0, 1.5, 8.0, 0, None))
    out.append(("env_shrine_egg_proxy", 56.0, 1.5, 8.0, 0, None))
    # CM-31 knock-down pillars, standing
    for d in m["decorations"]:
        out.append(("env_ruin_column_intact", d["cx"] + 0.5, hgt[d["cz"] * W + d["cx"]], d["cz"] + 0.5, 0, d))
    # bridges: spans over the void run of each segment, posts at both ends
    for sp in spans:
        out.extend(span_placements(sp))
    # sky: floating rocks and cloud puffs around and under the arena
    for k in range(70):
        hx = h32(k, 1, 17)
        a = (hx % 3600) / 3600.0 * 2 * math.pi
        rr = 1.0 + ((hx >> 12) % 100) / 100.0 * 0.35
        x = 56 + math.cos(a) * 62 * rr
        z = 36 + math.sin(a) * 44 * rr
        size = "sml"[(hx >> 20) % 3]
        y = -2.0 - ((hx >> 8) % 7)
        out.append(("env_sky_rock_" + size, x, y, z, 0, None))
    for k in range(130):
        hx = h32(k, 2, 19)
        x = -20 + (hx % 1520) / 10.0
        z = -20 + ((hx >> 11) % 1120) / 10.0
        out.append(("env_cloud_puff", x, -9.0 - (hx >> 24) % 3, z, (hx >> 5) % 360, None))
    for gx in range(-1, 4):
        for gz in range(-1, 3):
            out.append(("env_cloud_floor", gx * 64 + 32 - 40, -11.0, gz * 64 + 32 - 28, 0, None))
    return out


def all_spans(m):
    """Every bridge segment (mirrored copies included) with the deck's t-range: where the segment crosses BRIDGE
    cells, plus 0.6 m onto each island. Heights come from the UNMIRRORED endpoints, as map.ts paints them."""
    cells = m["cells"]
    out = []
    for (x0, z0, x1, z1, mir) in BRIDGES:
        ha, hb = _endpoint_h(x0, z0), _endpoint_h(x1, z1)
        for mm in ((False, True) if mir else (False,)):
            ax, bx = (MIRROR_X - x0, MIRROR_X - x1) if mm else (x0, x1)
            L = math.hypot(bx - ax, z1 - z0)
            steps = int(L * 20)
            ts = []
            for k in range(steps + 1):
                t = k / steps
                x, z = ax + (bx - ax) * t, z0 + (z1 - z0) * t
                cx, cz = int(math.floor(x)), int(math.floor(z))
                if 0 <= cx < W and 0 <= cz < H and cells[cz * W + cx] == BRIDGE:
                    ts.append(t)
            if ts:
                out.append(dict(x0=ax, z0=z0, x1=bx, z1=z1, ha=ha, hb=hb, L=L,
                                ta=max(0.0, min(ts) - 0.6 / L), tb=min(1.0, max(ts) + 0.6 / L)))
    return out


DECK_HALF = 1.42


def _cell_under_span(cx, cz, spans):
    """True when all four corners of the cell lie on one placed span's plank deck."""
    for sp in spans:
        ex, ez = sp["x1"] - sp["x0"], sp["z1"] - sp["z0"]
        ok = True
        for (px, pz) in ((cx, cz), (cx + 1, cz), (cx, cz + 1), (cx + 1, cz + 1)):
            t = ((px - sp["x0"]) * ex + (pz - sp["z0"]) * ez) / (sp["L"] ** 2)
            perp = abs((px - sp["x0"]) * ez - (pz - sp["z0"]) * ex) / sp["L"]
            if not (sp["ta"] <= t <= sp["tb"] and perp <= sp.get("hw", 1.5) - (1.5 - DECK_HALF)):
                ok = False
                break
        if ok:
            return True
    return False


def span_placements(sp):
    """Tile env_bridge_span along the deck range; n 1 m tiles are stretched to the exact length."""
    x0, z0, x1, z1, ha, hb, ta, tb, L = (sp[k] for k in ("x0", "z0", "x1", "z1", "ha", "hb", "ta", "tb", "L"))
    n = max(1, round((tb - ta) * L))
    out = []
    heading = math.atan2(z1 - z0, x1 - x0)
    for k in range(n):
        t0 = ta + (tb - ta) * k / n
        t1 = ta + (tb - ta) * (k + 1) / n
        out.append(("env_bridge_span", x0 + (x1 - x0) * t0, ha + (hb - ha) * t0, z0 + (z1 - z0) * t0, None,
                    dict(sx1=x0 + (x1 - x0) * t1, sz1=z0 + (z1 - z0) * t1, h1=ha + (hb - ha) * t1)))
    for t in (ta, tb):
        px, pz = x0 + (x1 - x0) * t, z0 + (z1 - z0) * t
        for side in (-1, 1):
            ox, oz = -math.sin(heading) * 1.5 * side, math.cos(heading) * 1.5 * side
            out.append(("env_bridge_post", px + ox, ha + (hb - ha) * t, pz + oz, 0, None))
    return out
