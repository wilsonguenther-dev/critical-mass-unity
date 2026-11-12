"""Kit v2 placement rules: turn a Sim cell grid into kit-v2 pieces (pure Python; numpy only in `fields`).

Written so the Unity view can port it as is (README "How the Unity side places v2 pieces"). Placements are dicts:
    piece, x, y, z (SIM coordinates: x east, z south, y = height), yaw (Unity Euler Y degrees, about the pivot),
    cat, team, and for bridge tiles a/b (sim points with heights) instead of x/y/z + yaw.
Unity position = SimSpace.ToWorld(x, y, z) = (x, y, -z).

Yaw rule for edge pieces: local +X runs along the rim edge direction d = (dx, dz) in sim; yaw = atan2(dz, dx) in degrees.
Edges are oriented so the outward normal is n = (-dz, dx) (sim), i.e. local -Z points out of the island.
"""
import math
from collections import defaultdict

from . import layout as L1
from .layout import BRIDGE, VOID, h32, solid_ground

SQ2 = math.sqrt(2.0)
STEP = 0.75
PATH_W = 1.9            # nominal path half-width (shader varies it +-0.3); main routes 3.2 to 4.4 u wide


class Grid:
    def __init__(self, cells, hgt, W, H):
        self.cells, self.hgt, self.W, self.H = cells, hgt, W, H

    def t(self, x, z):
        return self.cells[z * self.W + x] if 0 <= x < self.W and 0 <= z < self.H else VOID

    def land(self, x, z):
        return solid_ground(self.t(x, z))

    def h(self, x, z):
        return self.hgt[z * self.W + x]


def yaw_of(d):
    return math.degrees(math.atan2(d[1], d[0]))


def fills(g):
    """Void cells with land on exactly one corner (two adjacent sides + the diagonal, equal heights) get a half-cell
    fill triangle on that corner: (cx, cz, dx, dz, h)."""
    out = []
    for cz in range(g.H):
        for cx in range(g.W):
            if g.t(cx, cz) != VOID:
                continue
            cand = []
            for dx in (-1, 1):
                for dz in (-1, 1):
                    if g.land(cx + dx, cz) and g.land(cx, cz + dz) and g.land(cx + dx, cz + dz):
                        h = g.h(cx + dx, cz)
                        if abs(g.h(cx, cz + dz) - h) < 1e-6 and abs(g.h(cx + dx, cz + dz) - h) < 1e-6:
                            cand.append((dx, dz, h))
            if len(cand) == 1:
                out.append((cx, cz) + cand[0])
    return out


def ledge_fills(g):
    """v3: the same smoothing for ledges INSIDE islands. A walkable low cell with higher ground on two adjacent
    sides + the diagonal (all one height, a real step > 0.3 u) gets a half-cell triangle at the HIGH height on that
    corner, so a 45-degree terrace staircase reads as one straight diagonal ledge: (cx, cz, dx, dz, h_high).
    Trade-off (README): half of that walkable cell draws one step up; the Sim height of the cell is unchanged."""
    out = []
    for cz in range(g.H):
        for cx in range(g.W):
            if not L1.walkable(g.t(cx, cz)) or not g.land(cx, cz):
                continue
            hl = g.h(cx, cz)
            cand = []
            for dx in (-1, 1):
                for dz in (-1, 1):
                    if g.land(cx + dx, cz) and g.land(cx, cz + dz) and g.land(cx + dx, cz + dz):
                        hh = g.h(cx + dx, cz)
                        if hh - hl > 0.3 and abs(g.h(cx, cz + dz) - hh) < 1e-6 and abs(g.h(cx + dx, cz + dz) - hh) < 1e-6:
                            cand.append((dx, dz, hh))
            if len(cand) == 1:
                out.append((cx, cz) + cand[0])
    return out


def fill_tri(f):
    cx, cz, dx, dz, h = f
    Cx, Cz = cx + (1 if dx > 0 else 0), cz + (1 if dz > 0 else 0)
    return (Cx, Cz), (Cx, Cz - dz), (Cx - dx, Cz)


def _orient(p, q, n):
    d = (n[1], -n[0])
    if (q[0] - p[0]) * d[0] + (q[1] - p[1]) * d[1] < 0:
        p, q = q, p
    return p, q


def boundary(g, fl, lfl=()):
    fill_at = {(f[0], f[1]): f for f in fl}
    lfill_at = {(f[0], f[1]): f for f in lfl}
    rim, ledge = [], []
    for cz in range(g.H):
        for cx in range(g.W):
            if not g.land(cx, cz):
                continue
            h = g.h(cx, cz)
            for sx, sz in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, nz = cx + sx, cz + sz
                if sx == 1:
                    p, q = (cx + 1, cz), (cx + 1, cz + 1)
                elif sx == -1:
                    p, q = (cx, cz), (cx, cz + 1)
                elif sz == 1:
                    p, q = (cx, cz + 1), (cx + 1, cz + 1)
                else:
                    p, q = (cx, cz), (cx + 1, cz)
                n = (float(sx), float(sz))
                if g.land(nx, nz):
                    dh = h - g.h(nx, nz)
                    lf = lfill_at.get((nx, nz))
                    if dh > 0.3 and lf and ((nx + lf[2], nz) == (cx, cz) or (nx, nz + lf[3]) == (cx, cz)):
                        continue        # replaced by the diagonal ledge of the fill triangle below
                    if dh > 0.3:
                        p2, q2 = _orient(p, q, n)
                        ledge.append(dict(p=p2, q=q2, n=n, h=h, low=g.h(nx, nz), kind="ax", cell=(cx, cz)))
                    continue
                f = fill_at.get((nx, nz))
                if f and ((nx + f[2], nz) == (cx, cz) or (nx, nz + f[3]) == (cx, cz)):
                    continue
                p2, q2 = _orient(p, q, n)
                rim.append(dict(p=p2, q=q2, n=n, h=h, kind="ax", cell=(cx, cz), bridge=g.t(nx, nz) == BRIDGE))
    for f in fl:
        _, P, Q = fill_tri(f)
        n = (-f[2] / SQ2, -f[3] / SQ2)
        p2, q2 = _orient(P, Q, n)
        rim.append(dict(p=p2, q=q2, n=n, h=f[4], kind="dg", cell=(f[0], f[1]), bridge=False, fill=True))
    for f in lfl:
        _, P, Q = fill_tri(f)
        n = (-f[2] / SQ2, -f[3] / SQ2)
        p2, q2 = _orient(P, Q, n)
        ledge.append(dict(p=p2, q=q2, n=n, h=f[4], low=g.h(f[0], f[1]), kind="dg", cell=(f[0], f[1]), fill=True))
    return rim, ledge


def _key(pt, h):
    return (round(pt[0], 4), round(pt[1], 4), round(h, 4))


def loops(edges):
    by_start = defaultdict(list)
    for i, e in enumerate(edges):
        by_start[_key(e["p"], e["h"])].append(i)
    used = [False] * len(edges)
    out = []
    for i in range(len(edges)):
        if used[i]:
            continue
        lp, j = [], i
        while j is not None and not used[j]:
            used[j] = True
            lp.append(edges[j])
            nxt = [k for k in by_start.get(_key(edges[j]["q"], edges[j]["h"]), []) if not used[k]]
            j = nxt[0] if nxt else None
        out.append(lp)
    return out


def _dir(e):
    dx, dz = e["q"][0] - e["p"][0], e["q"][1] - e["p"][1]
    ln = math.hypot(dx, dz)
    return (round(dx / ln, 6), round(dz / ln, 6))


def runs(lp):
    n = len(lp)
    start = 0
    for k in range(n):
        if _dir(lp[k]) != _dir(lp[k - 1]):
            start = k
            break
    seq = lp[start:] + lp[:start]
    out, cur = [], [seq[0]]
    for e in seq[1:]:
        if _dir(e) == _dir(cur[-1]) and _key(e["p"], e["h"]) == _key(cur[-1]["q"], cur[-1]["h"]):
            cur.append(e)
        else:
            out.append(cur)
            cur = [e]
    out.append(cur)
    closed = _key(seq[-1]["q"], seq[-1]["h"]) == _key(seq[0]["p"], seq[0]["h"])
    return out, closed


def partition_units(n, choices, salt):
    out, r, i = [], n, 0
    mx, mn = max(choices), min(choices)
    while r > mx:
        opts = [c for c in choices if r - c == 0 or r - c >= mn]
        c = opts[h32(salt & 0xFFFF, i, 41) % len(opts)]
        out.append(c)
        r -= c
        i += 1
    if r:
        out.append(r)
    return out


def edge_pieces(rim, ledge):
    """Lips on every rim and ledge unit edge; cliff skirts along rim runs; corner and bend columns."""
    out, skirts = [], []
    for kind_set, is_rim in ((rim, True), (ledge, False)):
        for e in kind_set:
            d = _dir(e)
            pre = "env_lip_rim_" if is_rim else "env_lip_"
            if e["kind"] == "ax":
                nm = pre + "straight_" + "abc"[h32(int(e["p"][0] * 2), int(e["p"][1] * 2), 3) % 3]
            else:
                nm = pre + "diag_" + "ab"[h32(int(e["p"][0] * 2), int(e["p"][1] * 2), 5) % 2]
            out.append(dict(piece=nm, x=e["p"][0], y=e["h"], z=e["p"][1], yaw=yaw_of(d), cat="edge"))
            if not is_rim:
                steps = max(1, round((e["h"] - e["low"]) / STEP))
                sfx = "_diag" if e["kind"] == "dg" else ""
                for k in range(1, steps):
                    out.append(dict(piece="env_ledge_band" + sfx, x=e["p"][0], y=e["h"] - STEP * k, z=e["p"][1], yaw=yaw_of(d), cat="edge"))
                out.append(dict(piece="env_ledge_shadow" + sfx, x=e["p"][0], y=e["h"] - STEP * (steps - 1), z=e["p"][1],
                                yaw=yaw_of(d), cat="edge"))
        for lp in loops(kind_set):
            rs, closed = runs(lp)
            for ri, run in enumerate(rs):
                d = _dir(run[0])
                if is_rim:
                    ax = run[0]["kind"] == "ax"
                    parts = partition_units(len(run), [3, 4, 5, 6] if ax else [2, 3, 4],
                                            h32(int(run[0]["p"][0]), int(run[0]["p"][1]), 43))
                    s = 0
                    for k in parts:
                        P = run[s]["p"]
                        nm = f"env_cliff_{'s' if ax else 'd'}{k}"
                        out.append(dict(piece=nm, x=P[0], y=run[0]["h"], z=P[1], yaw=yaw_of(d), cat="cliff"))
                        skirts.append((P, d, k * (1.0 if ax else SQ2), run[0]["h"]))
                        s += k
                if ri + 1 < len(rs) or closed:
                    nxt = rs[(ri + 1) % len(rs)]
                    d2 = _dir(nxt[0])
                    nin = run[-1]["n"]
                    conv = d2[0] * nin[0] + d2[1] * nin[1] < -1e-6
                    if not conv:
                        continue
                    ang = math.degrees(math.acos(max(-1.0, min(1.0, d[0] * d2[0] + d[1] * d2[1]))))
                    V = run[-1]["q"]
                    if is_rim:      # v3: rock fan under the rim band + column on the corner bisector, both by incoming yaw
                        out.append(dict(piece="env_lip_rim_corner90" if ang > 60 else "env_lip_rim_corner45", x=V[0],
                                        y=run[-1]["h"], z=V[1], yaw=yaw_of(d), cat="edge"))
                        out.append(dict(piece="env_cliff_corner" if ang > 60 else "env_cliff_bend", x=V[0],
                                        y=run[-1]["h"], z=V[1], yaw=yaw_of(d), cat="cliff"))
                    elif ang > 60:
                        out.append(dict(piece="env_lip_corner", x=V[0], y=run[-1]["h"], z=V[1], yaw=yaw_of(d), cat="edge"))
    return out, skirts


# ------------------------------------------------------------------------------------------------- paths, decor
def map_paths():
    segs = L1.road_segments()
    discs = [(56.0, 40.0, 10.0), (56.0, 8.0, 4.0), (56.0, 11.5, 2.0)] + [(float(p[0]), float(p[1]), L1.PAD_R + 2.5) for p in L1.PADS.values()]
    for isl in L1.ISLANDS:
        for mx in ((isl[0],) + ((L1.MIRROR_X - isl[0],) if isl[5] else ())):
            if isl[4] == 0.0:
                discs.append((float(mx), float(isl[1]), 3.0))
    return dict(segs=segs, discs=discs)


def path_dist(paths, x, z):
    """Distance field the ground shader uses: path where < PATH_W (+-0.3 noise). Discs pave their whole radius."""
    d = 1e9
    for s in paths["segs"]:
        d = min(d, L1._seg_dist(x, z, *s))
    for (a, b, r) in paths["discs"]:
        d = min(d, max(0.0, math.hypot(x - a, z - b) - (r - PATH_W)))
    return d


def waterfalls(g, rim, keep, anchors, rate=6):
    """Waterfalls on straight camera-side rim edges (outward south or east), never at bridge mouths."""
    by_cell = defaultdict(list)
    for e in rim:
        by_cell[e["cell"]].append(e)
    cand = []
    for e in rim:
        if e["kind"] != "ax" or e["bridge"] or e["n"] not in ((0.0, 1.0), (1.0, 0.0)):
            continue
        mx, mz = (e["p"][0] + e["q"][0]) / 2, (e["p"][1] + e["q"][1]) / 2
        if keep(mx, mz) or len(by_cell[e["cell"]]) != 1:
            continue
        cand.append((e, mx, mz))
    pick = set()
    for (ax_, az_) in anchors:
        best = min(cand, key=lambda c: math.hypot(c[1] - ax_, c[2] - az_), default=None)
        if best and math.hypot(best[1] - ax_, best[2] - az_) < 5:
            pick.add(id(best[0]))
    for (e, mx, mz) in cand:
        if h32(e["cell"][0], e["cell"][1], 13) % 100 < rate:
            pick.add(id(e))
    out = []
    for (e, mx, mz) in cand:
        if id(e) in pick:
            out.append(dict(piece="env_waterfall", x=mx, y=e["h"], z=mz, yaw=yaw_of(_dir(e)), cat="water", cell=e["cell"]))
    return out


SMALL = ["env_pebbles", "env_grass_tuft", "env_flowers", "env_stump", "env_bush"]
TREES = ["env_tree_round_l", "env_tree_round_s", "env_tree_tall_l", "env_tree_tall_s"]


def decor(g, rim, fl, paths, keep, team_of, sites=(), wf_cells=(), skip=()):
    out = []
    rim_cells = defaultdict(list)
    for e in rim:
        if e["kind"] == "ax":
            rim_cells[e["cell"]].append(e)
    wf_only = set(wf_cells)
    wf_cells = wf_only | set(skip)
    for cz in range(g.H):
        for cx in range(g.W):
            if not g.land(cx, cz) or g.t(cx, cz) not in (L1.FLOOR,):
                continue
            xc, zc = cx + 0.5, cz + 0.5
            if keep(xc, zc) or (cx, cz) in wf_cells:
                continue
            h = g.h(cx, cz)
            jx = (h32(cx, cz, 71) % 100) / 100.0 - 0.5
            jz = (h32(cx, cz, 73) % 100) / 100.0 - 0.5
            yaw = float(h32(cx, cz, 75) % 360)
            pd = path_dist(paths, xc, zc)
            es = rim_cells.get((cx, cz))
            if es and not any(e["bridge"] for e in es):
                r = h32(cx, cz, 111) % 1000
                n = es[0]["n"]
                if r < 110 and pd > PATH_W + 0.6:
                    out.append(dict(piece=TREES[h32(cx, cz, 112) % 4], x=xc + n[0] * 0.28, y=h, z=zc + n[1] * 0.28,
                                    yaw=yaw, cat="tree", edge=True))
                elif r < 310 and pd < PATH_W + 4.5 and not any((cx + a_, cz + b_) in wf_only for a_ in (-2, -1, 0, 1, 2)
                                                                 for b_ in (-2, -1, 0, 1, 2)):
                    # v3: a big prop on the rim beside a route; its centre sits just past the lip (over the void cell,
                    # on the rim rock band), so no walkable cell holds anything taller than 0.3 u at its centre
                    k = h32(cx, cz, 122) % 100
                    x, z = xc + n[0] * 0.58 - n[1] * jx * 0.6, zc + n[1] * 0.58 + n[0] * jx * 0.6
                    site = next((s_ for s_ in sites if math.hypot(x - s_[0], z - s_[1]) < s_[2] + 2.0), None)
                    if site and k < 70:
                        out.append(dict(piece=site[3], x=x, y=h - 0.03, z=z, yaw=yaw, cat="crystal", edge=True))
                    elif team_of(x) is not None and k < 72:
                        out.append(dict(piece=("env_crystal_cluster", "env_crystal_cluster_small")[k % 2], x=x, y=h - 0.03, z=z,
                                        yaw=yaw, cat="crystal", team=team_of(x), edge=True))
                    else:
                        out.append(dict(piece=("env_boulder_l", "env_boulder_m", "env_rocks", "env_boulder_m")[k % 4], x=x, y=h - 0.10,
                                        z=z, yaw=yaw, cat="rock_big", edge=True))
                elif 310 <= r < 410 and pd > PATH_W - 0.2:
                    out.append(dict(piece=SMALL[h32(cx, cz, 114) % 3], x=xc + jx * 0.5, y=h, z=zc + jz * 0.5, yaw=yaw, cat="small"))
                continue
            if PATH_W + 0.1 < pd < PATH_W + 1.5:
                if h32(cx, cz, 117) % 100 < 18:
                    out.append(dict(piece=SMALL[h32(cx, cz, 118) % 5], x=xc + jx * 0.6, y=h, z=zc + jz * 0.6, yaw=yaw, cat="small"))
            elif pd >= PATH_W + 1.5 and h32(cx, cz, 119) % 100 < 3:
                out.append(dict(piece=SMALL[h32(cx, cz, 120) % 5], x=xc + jx * 0.6, y=h, z=zc + jz * 0.6, yaw=yaw, cat="small"))
    for f in fl:
        cx, cz, dx, dz, h = f
        C_, P, Q = fill_tri(f)
        x, z = (C_[0] + P[0] + Q[0]) / 3, (C_[1] + P[1] + Q[1]) / 3
        if keep(x, z):
            continue
        r = h32(cx, cz, 113) % 100
        pd = path_dist(paths, x, z)
        yaw = float(h32(cx, cz, 115) % 360)
        site = next((s for s in sites if math.hypot(x - s[0], z - s[1]) < s[2]), None)
        near = pd < 6.5
        if r < (16 if near else 32):
            out.append(dict(piece=TREES[h32(cx, cz, 116) % 4], x=x, y=h, z=z, yaw=yaw, cat="tree", edge=True))
        elif near and r >= 36 and r < 80 and (site or team_of(x) is not None):
            if site:
                out.append(dict(piece=site[3], x=x, y=h, z=z, yaw=yaw, cat="crystal", edge=True))
            else:
                out.append(dict(piece=("env_crystal_cluster", "env_crystal_cluster_small")[r % 2], x=x, y=h, z=z, yaw=yaw,
                                cat="crystal", team=team_of(x), edge=True))
        elif r < 60 or (near and r < 72):
            out.append(dict(piece=("env_boulder_l", "env_boulder_m", "env_rocks")[h32(cx, cz, 121) % 3], x=x, y=h, z=z,
                            yaw=yaw, cat="rock_big", edge=True))
        elif r < 80 and (site or (pd < 6.5 and team_of(x) is not None)):
            if site:
                out.append(dict(piece=site[3], x=x, y=h, z=z, yaw=yaw, cat="crystal", edge=True))
            else:
                out.append(dict(piece=("env_crystal_cluster", "env_crystal_cluster_small")[r % 2], x=x, y=h, z=z, yaw=yaw,
                                cat="crystal", team=team_of(x), edge=True))
        elif r < 90:
            out.append(dict(piece="env_grass_tuft", x=x, y=h, z=z, yaw=yaw, cat="small", edge=True))
    return out


def span_half_width(sp, g):
    """v3: half-width the deck must cover. Axis-aligned bridges on an integer axis paint FOUR cells (centres at
    +-0.5 and +-1.5 from the axis), so their deck widens to +-2.0 u (tile scale w = hw / 1.5 across); every other
    bridge keeps the 3.0 u deck."""
    ex, ez = sp["x1"] - sp["x0"], sp["z1"] - sp["z0"]
    if g is None or (abs(ex) > 1e-6 and abs(ez) > 1e-6):
        return 1.5
    best = 1.0
    for cz in range(g.H):
        for cx in range(g.W):
            if g.t(cx, cz) != BRIDGE:
                continue
            px, pz = cx + 0.5, cz + 0.5
            t = ((px - sp["x0"]) * ex + (pz - sp["z0"]) * ez) / (sp["L"] ** 2)
            perp = abs((px - sp["x0"]) * ez - (pz - sp["z0"]) * ex) / sp["L"]
            if sp["ta"] <= t <= sp["tb"] and perp <= 1.6:
                best = max(best, perp + 0.5)
    return best


def bridge_tiles(spans, g=None):
    out = []
    for sp in spans:
        w = sp.get("hw", 1.5) / 1.5
        x0, z0, x1, z1, ha, hb, ta, tb, L = (sp[k] for k in ("x0", "z0", "x1", "z1", "ha", "hb", "ta", "tb", "L"))
        n = max(1, round((tb - ta) * L / 2.04))
        pt = lambda t: (x0 + (x1 - x0) * t, ha + (hb - ha) * t, z0 + (z1 - z0) * t)
        for k in range(n):
            out.append(dict(piece="env_bridge_span", a=pt(ta + (tb - ta) * k / n), b=pt(ta + (tb - ta) * (k + 1) / n), w=round(w, 4),
                            cat="bridge"))
        ang = math.atan2(z1 - z0, x1 - x0)
        ox, oz = -math.sin(ang), math.cos(ang)
        for side in (-1, 1):
            e = pt(tb)
            px_, pz_ = e[0] + ox * 1.58 * w * side, e[2] + oz * 1.58 * w * side
            if g is None or not L1.walkable(g.t(int(math.floor(px_)), int(math.floor(pz_)))):
                out.append(dict(piece="env_bridge_post", x=px_, y=e[1], z=pz_, yaw=0.0, cat="bridge"))
            for t, sgn in ((ta, 1), (tb, -1)):
                # mossy piers sit on the rim beside the mouth, on a NON-walkable cell (void beside the bridge)
                for dt in (0.6, 0.35, 0.85, 0.1):
                    p = pt(t + sgn * dt / L)
                    x, z = p[0] + ox * (2.0 * w) * side, p[2] + oz * (2.0 * w) * side
                    if g is None or not L1.walkable(g.t(int(math.floor(x)), int(math.floor(z)))):
                        out.append(dict(piece="env_bridge_pier", x=x, y=p[1], z=z, yaw=math.degrees(ang), cat="bridge"))
                        break
    return out


# ------------------------------------------------------------------------------------------------ the real map
STRUCT_FROM_V1 = {"env_hill_plaza", "env_hill_rim_half", "env_spawn_pad", "env_banner", "env_shrine_dais",
                  "env_shrine_horns", "env_shrine_egg_proxy", "env_wall", "env_wall_breakable_intact",
                  "env_wall_breakable_cracked", "env_wall_breakable_destroyed"}
KEEP_FROM_V1 = {"env_crystal_cluster", "env_crystal_cluster_small", "env_ruin_column_intact", "env_sky_rock_s",
                "env_sky_rock_m", "env_sky_rock_l", "env_cloud_puff", "env_cloud_floor"}
WATERFALL_ANCHORS = [(41.5, 47.0), (47.0, 52.5), (25.0, 23.5), (30.0, 61.5), (14.0, 46.5), (64.0, 15.0), (98.0, 46.5),
                     (86.0, 23.5), (60.0, 69.5), (69.0, 50.0)]


def map_keep(xc, zc):
    if math.hypot(xc - 56, zc - 40) < 11.2 and not (6.6 < math.hypot(xc - 56, zc - 40) < 9.8):
        return True
    if math.hypot(xc - 56, zc - 8) < 5.4:
        return True
    return any(math.hypot(xc - p[0], zc - p[1]) < L1.PAD_R + 1.5 for p in L1.PADS.values())


def map_team(x):
    return "azure" if x < 36 else ("crimson" if x > 76 else None)


def map_sites():
    s = [(x, z, 3.2, "env_crystal_biomass") for (x, z) in L1.WEST_BIOMASS + [(L1.MIRROR_X - x, z) for (x, z) in L1.WEST_BIOMASS]]
    s += [(x, z, 3.6, "env_crystal_corrupt") for (x, z) in L1.CORRUPTION]
    return s


def place_map():
    m = L1.build_map()
    g = Grid(m["cells"], m["hgt"], L1.W, L1.H)
    fl = fills(g)
    lfl = ledge_fills(g)
    rim, ledge = boundary(g, fl, lfl)
    edges, skirts = edge_pieces(rim, ledge)
    paths = map_paths()
    wf = waterfalls(g, rim, map_keep, WATERFALL_ANCHORS)
    spans = L1.all_spans(m)
    for sp in spans:
        sp["hw"] = span_half_width(sp, g)
    out = []
    for cz in range(g.H):
        for cx in range(g.W):
            t = g.t(cx, cz)
            if g.land(cx, cz):
                out.append(dict(piece="env_ground_cell", x=cx + 0.5, y=g.h(cx, cz), z=cz + 0.5, yaw=0.0, cat="ground"))
            elif t == BRIDGE and not L1._cell_under_span(cx, cz, spans):
                out.append(dict(piece="env_bridge_cell", x=cx + 0.5, y=g.h(cx, cz) - 0.06, z=cz + 0.5, yaw=0.0, cat="bridge"))
    for f in fl:
        out.append(dict(piece="env_ground_fill_tri", x=f[0] + 0.5, y=f[4], z=f[1] + 0.5, yaw=yaw_of((f[2], f[3])) - 45.0, cat="ground"))
    for f in lfl:
        out.append(dict(piece="env_ground_fill_tri", x=f[0] + 0.5, y=f[4], z=f[1] + 0.5, yaw=yaw_of((f[2], f[3])) - 45.0, cat="ground",
                        ledge_fill=True))
    out += edges + wf + bridge_tiles(spans, g)
    out += decor(g, rim, fl, paths, map_keep, map_team, map_sites(), [w["cell"] for w in wf], [(f[0], f[1]) for f in lfl])
    for (nm, x, y, z, yaw, extra) in L1.place(m):
        if nm in STRUCT_FROM_V1 or nm in KEEP_FROM_V1:
            team = extra if isinstance(extra, str) else None
            out.append(dict(piece=nm, x=x, y=y, z=z, yaw=float(yaw or 0.0), cat="struct" if nm in STRUCT_FROM_V1 else "deco",
                            team=team))
    return dict(m=m, g=g, fills=fl, lfills=lfl, rim=rim, ledge=ledge, skirts=skirts, paths=paths, spans=spans, placements=out)


# ------------------------------------------------------------------------------------------- field demo terrain
def place_demo():
    """A small synthetic island for the field shot: a raised terrace (one 0.75 u ledge), a path, a bridge to a
    second island, rims with a waterfall and crystals on the route. Same rules as the map."""
    W, H = 46, 36
    cells, hgt = [VOID] * (W * H), [0.0] * (W * H)
    ins = lambda x, z, cx, cz, rx, rz: ((x - cx) / rx) ** 2 + ((z - cz) / rz) ** 2 <= 1
    for cz in range(H):
        for cx in range(W):
            x, z = cx + 0.5, cz + 0.5
            if ins(x, z, 16, 18, 14, 12) or ins(x, z, 39, 17, 5.5, 7):
                cells[cz * W + cx] = L1.FLOOR
                if ins(x, z, 17, 10.5, 7, 4.2):
                    hgt[cz * W + cx] = 0.75
    br = (28.0, 17.5, 35.0, 17.5)
    for cz in range(H):
        for cx in range(W):
            i = cz * W + cx
            if cells[i] == VOID and abs(cz + 0.5 - br[1]) <= 1.5 and br[0] <= cx + 0.5 <= br[2]:
                cells[i] = BRIDGE
    g = Grid(cells, hgt, W, H)
    fl = fills(g)
    lfl = ledge_fills(g)
    rim, ledge = boundary(g, fl, lfl)
    edges, skirts = edge_pieces(rim, ledge)
    paths = dict(segs=[(31.0, 17.5, 22.0, 20.0), (22.0, 20.0, 6.0, 21.0), (22.0, 20.0, 18.0, 29.0), (35.0, 17.5, 40.0, 17.0)],
                 discs=[(40.0, 17.0, 2.5)])
    keep = lambda x, z: False
    wf = waterfalls(g, rim, keep, [(14.0, 30.0), (40.5, 24.0)], rate=0)
    ts = []
    for cx in range(W):
        if cells[17 * W + cx] == BRIDGE:
            ts.append(cx)
    sp = dict(x0=br[0], z0=br[1], x1=br[2], z1=br[3], ha=0.0, hb=0.0, L=br[2] - br[0])
    sp["ta"] = max(0.0, (min(ts) - br[0] - 0.6) / sp["L"])
    sp["tb"] = min(1.0, (max(ts) + 1 - br[0] + 0.6) / sp["L"])
    out = []
    for cz in range(H):
        for cx in range(W):
            if g.land(cx, cz):
                out.append(dict(piece="env_ground_cell", x=cx + 0.5, y=g.h(cx, cz), z=cz + 0.5, yaw=0.0, cat="ground"))
    for f in fl:
        out.append(dict(piece="env_ground_fill_tri", x=f[0] + 0.5, y=f[4], z=f[1] + 0.5, yaw=yaw_of((f[2], f[3])) - 45.0, cat="ground"))
    for f in lfl:
        out.append(dict(piece="env_ground_fill_tri", x=f[0] + 0.5, y=f[4], z=f[1] + 0.5, yaw=yaw_of((f[2], f[3])) - 45.0, cat="ground",
                        ledge_fill=True))
    out += edges + wf + bridge_tiles([sp], g)
    out += decor(g, rim, fl, paths, keep, lambda x: "azure", [], [w["cell"] for w in wf], [(f[0], f[1]) for f in lfl])
    return dict(g=g, fills=fl, lfills=lfl, rim=rim, ledge=ledge, skirts=skirts, paths=paths, spans=[sp], placements=out)


# ------------------------------------------------------------------------------------------- shader field bake
def fields(lay, x0, z0, wdt, hgt_, res=4, sites=()):
    """Bake the ground shader's field texture (numpy). Channels: R = path distance (u), G = feature distance (u, to
    rims and hub discs: the macro bands follow it), B = dirt mask 0..1 (0.4 u soft edge), A = ledge shadow 0..1.
    Row 0 is the z0 side. The Unity view bakes the same 4 texel/u RGBAHalf texture from the grid."""
    import numpy as np
    cols, rows = int(wdt * res), int(hgt_ * res)
    X = x0 + (np.arange(cols) + 0.5) / res
    Z = z0 + (np.arange(rows) + 0.5) / res
    XX, ZZ = np.meshgrid(X, Z)

    def segd(segs):
        d = np.full(XX.shape, 1e9, dtype=np.float64)
        for (ax, az, bx, bz) in segs:
            ex, ez = bx - ax, bz - az
            ll = ex * ex + ez * ez or 1e-9
            t = np.clip(((XX - ax) * ex + (ZZ - az) * ez) / ll, 0, 1)
            d = np.minimum(d, np.hypot(ax + ex * t - XX, az + ez * t - ZZ))
        return d

    P = lay["paths"]
    R = segd(P["segs"])
    for (a, b, r) in P["discs"]:
        R = np.minimum(R, np.maximum(0.0, np.hypot(XX - a, ZZ - b) - (r - PATH_W)))
    G = segd([(e["p"][0], e["p"][1], e["q"][0], e["q"][1]) for e in lay["rim"] + lay["ledge"]])
    for (a, b, r) in P["discs"]:
        G = np.minimum(G, np.abs(np.hypot(XX - a, ZZ - b) - r))
    B = np.zeros(XX.shape)
    for s in sites:
        d = np.hypot(XX - s[0], ZZ - s[1])
        B = np.maximum(B, 1 - np.clip((d - (s[2] - 0.2)) / 0.4, 0, 1))
    A = np.zeros(XX.shape)
    for e in lay["ledge"]:
        px, pz = e["p"]
        dx, dz = e["q"][0] - px, e["q"][1] - pz
        t = ((XX - px) * dx + (ZZ - pz) * dz) / (dx * dx + dz * dz)
        s = (XX - px) * e["n"][0] + (ZZ - pz) * e["n"][1]
        A = np.maximum(A, np.where((t >= -0.05) & (t <= 1.05) & (s >= 0) & (s <= 0.5), 1 - s / 0.5, 0.0))
    return np.stack([R, G, B, A], axis=-1).astype(np.float32)
