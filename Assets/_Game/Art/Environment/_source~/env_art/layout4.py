"""Environment v4 placement rules for the new pieces (pure Python, no bpy: ports to C# like layout2).

dress(lay) takes layout2.place_map() (or place_demo()) and returns (new_placements, substitutions). Every rule is a
deterministic hash (layout.h32) per cell or edge with a density cap; tall pieces (over 0.3 u) only ever stand on VOID
cells (rim fill triangles, or 0.12 to 0.2 u past a rim lip on the rock band), so nothing new over the step-over
height lands on a walkable cell. The proposed world-plan rows in the v4 report mirror RULES below one to one.
Coordinates are SIM (x, y, z); yaw in degrees as layout2 (Unity Euler Y; local +X -> sim (cos, sin)).
"""
import math

from . import layout as L1
from . import layout2 as L2
from .layout import h32

PW = L2.PATH_W
HILL, SHRINE = (56.0, 40.0), (56.0, 8.0)

# piece -> (where, cap). cap = the most a single 26 x 19 u gameplay screen may show (checked in measure_v4.py).
RULES = {
    "env_ground_patch_light": ("open grass cell, 3.0% (salt 201, r < 30); jitter 0.3, hash yaw", 6),
    "env_ground_patch_dark": ("open grass cell, 3.0% (salt 201, 30 <= r < 60)", 6),
    "env_moss_patch": ("open grass cell, 1.5% (salt 201, 60 <= r < 75)", 4),
    "env_puddle": ("open grass 0.5% (r 75-80) and paved cells 0.4% (salt 202)", 3),
    "env_grass_clump": ("open grass 7% (r 80-150); rim cells +10% (salt 203)", 22),
    "env_flower_clump_a": ("open grass 1.75% (r 150-167)", 5),
    "env_flower_clump_b": ("open grass 1.8% (r 167-185)", 5),
    "env_moss_rocks": ("open grass 1.5% (r 185-200)", 4),
    "env_mushrooms": ("open grass 1% (r 200-210), +6% within 2.5 u of a tree", 5),
    "env_path_ruts": ("route shoulder cell (PATH_W-0.2 < d < PATH_W+1.2) 7% (salt 204), yaw along the route", 4),
    "env_edge_stones": ("route shoulder 8%, pushed to the paving edge, yaw along the route", 6),
    "env_dirt_patch": ("route shoulder 4%", 3),
    "env_ruin_rubble": ("open grass 0.6% (salt 205) and pad ring 4%", 3),
    "env_campfire": ("pad ring cell (PAD_R+1.5 < d < PAD_R+6) 4% (salt 231)", 2),
    "env_camp_sacks": ("pad ring cell 6%", 3),
    "env_torch": ("rim edge (axis) within PATH_W+2.5 of a route 7% (salt 211), 0.18 u past the lip", 4),
    "env_fence": ("rim edge (axis) within PATH_W+3 of a route, 3x3-cell blocks 22% (salt 212), on the rock band", 8),
    "env_reeds": ("rim edge within 2.6 u of a waterfall (not its own edge), 0.15 u past the lip", 4),
    "env_bush_berry": ("rim edge 3% (salt 213) past the lip; fills away from routes 6%", 4),
    "env_float_chunk_s": ("rim edge facing the camera (n.(1,1) > 0) 4% (salt 214): 1.8-3.2 u out, 2.5-5 u down", 3),
    "env_float_chunk_m": ("as _s, 2% (salt 214)", 2),
    "env_lantern_post": ("fill near a route 10% (salt 221)", 2),
    "env_signpost": ("fill near a route 6%, 12 u apart; carries the three boards", 1),
    "env_sign_board_hill": ("on each signpost, y +1.25, yaw to the hill", 1),
    "env_sign_board_shrine": ("on each signpost, y +1.02, yaw to the shrine", 1),
    "env_sign_board_base": ("on each signpost, y +0.79, yaw to the nearer base", 1),
    "env_banner_worship": ("fill near a route 8%", 2),
    "env_ruin_altar": ("fill near a route 4%", 1),
    "env_woodpile": ("fill within 16 u of a pad 6%, else fill 5%", 2),
    "env_tree_oak": ("fill away from routes 14% split oak / pine / gnarl by hash", 2),
    "env_tree_pine": ("as oak", 2),
    "env_tree_gnarl": ("as oak", 2),
    "env_kaiju_skull": ("fill away from routes 4%, 20 u apart", 1),
    "env_bone_ribs": ("fill away from routes 5%, 10 u apart", 1),
    "env_kaiju_statue": ("fill away from routes 2%, 25 u apart, faces the nearest route", 1),
    "env_bridge_anchor": ("every span, both ends (like a span tile; scale w across)", 2),
    "env_bridge_span_damaged": ("REPLACES env_bridge_span on inner tiles (not first or last) 14% (salt 241)", 2),
    "env_lantern_hang": ("every third span tile (k % 3 == 1), on the +X post hook", 3),
}


def _yaw_dir(ux, uz):
    return math.degrees(math.atan2(uz, ux))


def _grad(paths, x, z):
    e = 0.25
    gx = L2.path_dist(paths, x + e, z) - L2.path_dist(paths, x - e, z)
    gz = L2.path_dist(paths, x, z + e) - L2.path_dist(paths, x, z - e)
    ln = math.hypot(gx, gz) or 1.0
    return gx / ln, gz / ln


def _j(cx, cz, salt, amp):
    return ((h32(cx, cz, salt) % 1000) / 1000.0 - 0.5) * 2 * amp


def dress(lay, keep=None, pads=None):
    g, paths = lay["g"], lay["paths"]
    keep = keep or L2.map_keep
    pads = pads if pads is not None else list(L1.PADS.values())
    pl = lay["placements"]
    occ = set()
    trees = []
    for p in pl:
        if p.get("cat") in ("tree", "rock_big", "crystal", "small", "water", "struct", "deco") and "x" in p:
            occ.add((int(math.floor(p["x"])), int(math.floor(p["z"]))))
            if p.get("cat") == "tree":
                trees.append((p["x"], p["z"]))
    wfs = [p for p in pl if p["piece"] == "env_waterfall"]
    out, subs = [], []

    def add(piece, x, y, z, yaw, cat, **kw):
        d = dict(piece=piece, x=x, y=y, z=z, yaw=yaw, cat=cat, v4=True)
        d.update(kw)
        out.append(d)

    voidn = lambda cx, cz: any(not g.land(cx + dx, cz + dz) for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1)))
    # ---------------------------------------------------------------- land cells
    for cz in range(g.H):
        for cx in range(g.W):
            if not g.land(cx, cz):
                continue
            xc, zc = cx + 0.5, cz + 0.5
            if keep(xc, zc):
                continue
            h = g.h(cx, cz)
            x, z = xc + _j(cx, cz, 301, 0.3), zc + _j(cx, cz, 302, 0.3)
            yaw = float(h32(cx, cz, 303) % 360)
            pd = L2.path_dist(paths, xc, zc)
            dpad = min(math.hypot(xc - a, zc - b) for a, b in pads) if pads else 99
            taken = (cx, cz) in occ
            if pd < PW - 0.2:
                if h32(cx, cz, 202) % 1000 < 4:
                    add("env_puddle", x, h, z, yaw, "ground_decal")
                continue
            if pd < PW + 1.2:
                r = h32(cx, cz, 204) % 1000
                gx, gz = _grad(paths, xc, zc)
                tyaw = _yaw_dir(-gz, gx)
                if r < 70:
                    add("env_path_ruts", xc, h, zc, math.degrees(math.atan2(-gz, -gx)), "ground_decal")
                elif r < 150 and not taken:
                    sh = pd - (PW + 0.1)
                    add("env_edge_stones", xc - gx * sh + gz * 0.6, h, zc - gz * sh - gx * 0.6, tyaw, "small")
                elif r < 190:
                    add("env_dirt_patch", x, h, z, yaw, "ground_decal")
                continue
            if PAD_RING(dpad):
                r = h32(cx, cz, 231) % 100
                if r < 4 and not taken:
                    add("env_campfire", xc, h, zc, yaw, "small")
                    occ.add((cx, cz))
                elif r < 10 and not taken:
                    add("env_camp_sacks", xc, h, zc, yaw, "small")
                    occ.add((cx, cz))
                elif r < 14:
                    add("env_ruin_rubble", x, h, z, yaw, "small")
                continue
            r = h32(cx, cz, 201) % 1000
            near_tree = any(abs(tx - xc) < 2.5 and abs(tz - zc) < 2.5 for tx, tz in trees)
            if r < 55:
                add("env_ground_patch_light", x, h, z, yaw, "ground_decal")
            elif r < 110:
                add("env_ground_patch_dark", x, h, z, yaw, "ground_decal")
            elif r < 130:
                add("env_moss_patch", x, h, z, yaw, "ground_decal")
            elif r < 135:
                add("env_puddle", x, h, z, yaw, "ground_decal")
            elif taken:
                continue
            elif r < 235:
                add("env_grass_clump", x, h, z, yaw, "small")
            elif r < 257:
                add("env_flower_clump_a", x, h, z, yaw, "small")
            elif r < 280:
                add("env_flower_clump_b", x, h, z, yaw, "small")
            elif r < 295:
                add("env_moss_rocks", x, h, z, yaw, "small")
            elif r < 305 or (near_tree and r < 365):
                add("env_mushrooms", x, h, z, yaw, "small")
            elif r < 371:
                add("env_ruin_rubble", x, h, z, yaw, "small")
            elif voidn(cx, cz) and h32(cx, cz, 203) % 100 < 10:
                add("env_grass_clump", x, h, z, yaw, "small")
    placed = {"env_signpost": [], "env_kaiju_skull": [], "env_bone_ribs": [], "env_kaiju_statue": []}
    spacing = {"env_signpost": 12.0, "env_kaiju_skull": 20.0, "env_bone_ribs": 10.0, "env_kaiju_statue": 25.0}

    def far(nm, x, z):
        return all(math.hypot(x - a, z - b) >= spacing[nm] for a, b in placed[nm])

    # ---------------------------------------------------------------- rim edges
    for e in lay["rim"]:
        if e.get("bridge") or e["kind"] != "ax":
            continue
        (px, pz), (qx, qz), (nx, nz), h = e["p"], e["q"], e["n"], e["h"]
        mx, mz = (px + qx) / 2, (pz + qz) / 2
        if keep(mx - nx * 0.5, mz - nz * 0.5):
            continue
        cx, cz = e["cell"]
        dx, dz = qx - px, qz - pz
        eyaw = _yaw_dir(dx, dz)
        pd = L2.path_dist(paths, mx - nx * 0.5, mz - nz * 0.5)
        wfd = min([math.hypot(w["x"] - mx, w["z"] - mz) for w in wfs] or [99])
        if wfd < 0.3:
            continue
        if wfd < 2.6 and h32(cx, cz, 215) % 100 < 70:
            add("env_reeds", mx + nx * 0.15, h - 0.06, mz + nz * 0.15, eyaw, "small_tall")
            continue
        if pd < PW + 2.5 and h32(cx, cz, 211) % 100 < 7:
            add("env_torch", mx + nx * 0.18, h - 0.1, mz + nz * 0.18, eyaw, "light")
        elif pd < PW + 3.0 and h32(cx // 3, cz // 3, 212) % 100 < 22:
            add("env_fence", px, h, pz, eyaw, "dress_tall")
        elif h32(cx, cz, 213) % 100 < 4:
            add("env_bush_berry", mx + nx * 0.12, h - 0.04, mz + nz * 0.12, eyaw, "small_tall")
        elif pd > PW + 3.0 and h32(cx, cz, 219) % 100 < 6:
            nm = ("env_kaiju_skull", "env_bone_ribs", "env_kaiju_statue")[h32(cx, cz, 224) % 3]
            lx, lz = mx + nx * 0.35, mz + nz * 0.35
            if far(nm, lx, lz):
                placed[nm].append((lx, lz))
                add(nm, lx, h - 0.12, lz, _yaw_dir(nz, -nx) if nm == "env_kaiju_statue" else float(h32(cx, cz, 225) % 360),
                    "landmark")
        elif pd > PW + 1.5 and (cx, cz) not in occ and h32(cx, cz, 220) % 100 < 7:
            sp_ = ("env_tree_oak", "env_tree_pine", "env_tree_gnarl")[h32(cx, cz, 223) % 3]
            add(sp_, mx - nx * 0.3, h, mz - nz * 0.3, float(h32(cx, cz, 226) % 360), "tree", edge=True)
            occ.add((cx, cz))
        if nx + nz > 0.1:
            r = h32(cx, cz, 214) % 100
            if r < 6:
                d = 1.8 + (h32(cx, cz, 216) % 100) / 100.0 * 1.4
                dy = 2.5 + (h32(cx, cz, 217) % 100) / 100.0 * 2.5
                add("env_float_chunk_m" if r < 2 else "env_float_chunk_s", mx + nx * d, h - dy, mz + nz * d,
                    float(h32(cx, cz, 218) % 360), "sky")
    # ---------------------------------------------------------------- fill triangles
    for f in lay["fills"]:
        cx, cz, fdx, fdz, h = f
        xc, zc = cx + 0.5, cz + 0.5
        if keep(xc, zc) or (cx, cz) in occ:
            continue
        x, z = xc + fdx * 0.12, zc + fdz * 0.12
        yaw = float(h32(cx, cz, 222) % 360)
        pd = L2.path_dist(paths, xc, zc)
        dpad = min(math.hypot(xc - a, zc - b) for a, b in pads) if pads else 99
        r = h32(cx, cz, 221) % 100
        if pd < PW + 3.0:
            if r < 10:
                add("env_lantern_post", x, h, z, yaw, "light")
            elif r < 16 and far("env_signpost", x, z):
                placed["env_signpost"].append((x, z))
                add("env_signpost", x, h, z, 0.0, "dress_tall")
                tb = min(pads, key=lambda p: math.hypot(p[0] - x, p[1] - z)) if pads else HILL
                for nm, dy, tgt in (("env_sign_board_hill", 1.25, HILL), ("env_sign_board_shrine", 1.02, SHRINE),
                                    ("env_sign_board_base", 0.79, tb)):
                    add(nm, x, h + dy, z, _yaw_dir(tgt[0] - x, tgt[1] - z), "dress_tall")
            elif r < 24:
                add("env_banner_worship", x, h, z, yaw, "dress_tall")
            elif r < 28:
                add("env_ruin_altar", x, h, z, yaw, "dress_tall")
            elif r < 34 and dpad < 16:
                add("env_woodpile", x, h, z, yaw, "dress_tall")
        else:
            if r < 14:
                sp = ("env_tree_oak", "env_tree_pine", "env_tree_gnarl")[h32(cx, cz, 223) % 3]
                add(sp, x, h, z, yaw, "tree", edge=True)
            elif r < 20:
                add("env_bush_berry", x, h, z, yaw, "small_tall")
            elif r < 24 and far("env_kaiju_skull", x, z):
                placed["env_kaiju_skull"].append((x, z))
                add("env_kaiju_skull", x, h, z, yaw, "landmark")
            elif r < 29 and far("env_bone_ribs", x, z):
                placed["env_bone_ribs"].append((x, z))
                add("env_bone_ribs", x, h, z, yaw, "dress_tall")
            elif r < 31 and far("env_kaiju_statue", x, z):
                placed["env_kaiju_statue"].append((x, z))
                gx, gz = _grad(paths, xc, zc)
                add("env_kaiju_statue", x, h, z, math.degrees(math.atan2(gx, -gz)), "landmark")
            elif r < 36:
                add("env_woodpile", x, h, z, yaw, "dress_tall")
        occ.add((cx, cz))
    # ---------------------------------------------------------------- bridges
    for sp in lay["spans"]:
        x0, z0, x1, z1, ha, hb, ta, tb, L = (sp[k] for k in ("x0", "z0", "x1", "z1", "ha", "hb", "ta", "tb", "L"))
        w = round(sp.get("hw", 1.5) / 1.5, 4)
        pt = lambda t: (x0 + (x1 - x0) * t, ha + (hb - ha) * t, z0 + (z1 - z0) * t)
        dt = 2.04 / L
        out.append(dict(piece="env_bridge_anchor", a=pt(ta), b=pt(ta + dt), w=w, cat="bridge", v4=True))
        out.append(dict(piece="env_bridge_anchor", a=pt(tb), b=pt(tb - dt), w=w, cat="bridge", v4=True))
    tiles = [p for p in pl if p["piece"] == "env_bridge_span"]
    # tiles are emitted per span in order: k-th tile of a run is detected by chaining a -> b
    starts = {}
    for p in tiles:
        starts[(round(p["a"][0], 3), round(p["a"][2], 3))] = p
    ends = {(round(p["b"][0], 3), round(p["b"][2], 3)) for p in tiles}
    for p in tiles:
        key = (round(p["a"][0], 3), round(p["a"][2], 3))
        if key in ends:
            continue
        run, q = [], p
        while q is not None and len(run) < 64:
            run.append(q)
            q = starts.get((round(q["b"][0], 3), round(q["b"][2], 3)))
        for k, t in enumerate(run):
            a, b, w = t["a"], t["b"], t.get("w", 1.0)
            if 0 < k < len(run) - 1 and h32(int(math.floor(a[0] * 4)), int(math.floor(a[2] * 4)), 241) % 100 < 14:
                subs.append((t, "env_bridge_span_damaged"))
            if k % 3 == 1:
                fx, fz = b[0] - a[0], b[2] - a[2]
                ln = math.hypot(fx, fz) or 1.0
                fx, fz = fx / ln, fz / ln
                add("env_lantern_hang", a[0] - fz * 1.31 * w + fx * 0.02, a[1] + 0.69, a[2] + fx * 1.31 * w + fz * 0.02,
                    _yaw_dir(fx, fz), "light")
    return out, subs


def PAD_RING(d):
    return L1.PAD_R + 1.5 < d < L1.PAD_R + 6.0


def pick_corner(lay):
    """Representative island corner for the before/after shot: the waterfall closest to a span end (both in frame)."""
    best = None
    for w in [p for p in lay["placements"] if p["piece"] == "env_waterfall"]:
        for sp in lay["spans"]:
            for t in (sp["ta"], sp["tb"]):
                x = sp["x0"] + (sp["x1"] - sp["x0"]) * t
                z = sp["z0"] + (sp["z1"] - sp["z0"]) * t
                d = math.hypot(w["x"] - x, w["z"] - z)
                if 3.0 < d and (best is None or d < best[0]):
                    best = (d, ((w["x"] + x) / 2, w["y"], (w["z"] + z) / 2), w, (x, z))
    return best
