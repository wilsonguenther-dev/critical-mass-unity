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

