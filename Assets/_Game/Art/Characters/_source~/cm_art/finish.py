"""Painted finish baked into vertex colour (Critical Mass characters, v6, 2026-11-12). Shared by every class script.

The bible's characters are painted: light falls from the top, creases and the undersides of overlaps go dark, the
upper edges of stone, metal and leather catch a crisp light rim, stone is chipped and worn, wood has grain, brass has a
hot spot and a darker rim, cloth has soft folds. CM/Toon then lays its three hard bands over that, so the toon read
stays and the surfaces stop looking like flat plastic. No textures (the face decal is the only one).

USAGE (any class script; bmesh with a linear float colour layer, the team mask in alpha):

    from cm_art import finish
    kind = bm.verts.layers.int.new("kind")          # per vertex: finish.KIND[...] (finish.kind_of(colour_name))
    lco = bm.verts.layers.float_vector.new("lco")    # optional: part-local position (wood grain, cloth folds)
    ...when adding a part: v[kind] = finish.kind_of("stone_armour"); v[lco] = co_before_the_part_transform
    finish.painted_finish(bm, col_layer, kind, lco=lco, skip=skip_layer, seed=606, params=None)

kit.Builder does all of this for you: every part added with Builder.add gets its kind from the colour name and its
local position; Builder.finalize(coll, mat, finish_params=None) runs painted_finish after the crevice bake. Pass
finish_params={"stone": {...}, ...} to override any value below for one class. Glow vertices (alpha 0.75) and
vertices flagged in `skip` (for example the face skin) are left alone. Every factor is applied to sRGB value and
clamped to [CLAMP_LO, CLAMP_HI] of the authored colour, so the result stays inside the bible's swatch ranges.

PARAMETERS per kind (finish.DEFAULTS[kind]):
  top        top-light gradient: +top/2 at the part's top, -top/2 at its bottom (world z over the connected part)
  ao         ambient-occlusion darkening in creases and under overlaps (0.20 = up to 20 % darker); ao_dist = ray length
  edge       crisp lighter highlight on convex, upward-facing edges (stone, metal, leather bevels)
  chips      stone only: fraction of convex corner vertices lightened as chips; crack = darkening on thin crack bands
  grain      wood only: depth of the grain bands across the planks (part-local x), grain_freq bands per metre
  hot, rim   metal only: bright hot spot toward the key light (upper left front) and darker rim on turned-away sides
  folds      cloth only: soft fold shading bands (part-local), folds_freq per metre
"""
import math
import random

from mathutils import Vector
from mathutils.bvhtree import BVHTree

KINDS = ("other", "stone", "metal", "leather", "wood", "cloth", "skin", "hair", "glow")
KIND = {k: i for i, k in enumerate(KINDS)}
CLAMP_LO, CLAMP_HI = 0.62, 1.22
KEY = Vector((-0.45, -0.55, 0.70)).normalized()      # painted key light: upper left, in front (bible U-017 capture light)

DEFAULTS = {
    "other":   {"top": 0.06, "ao": 0.14, "ao_dist": 0.10, "edge": 0.00},
    "stone":   {"top": 0.08, "ao": 0.22, "ao_dist": 0.12, "edge": 0.12, "chips": 0.18, "crack": 0.22},
    "metal":   {"top": 0.10, "ao": 0.16, "ao_dist": 0.08, "edge": 0.12, "hot": 0.35, "rim": 0.22},
    "leather": {"top": 0.10, "ao": 0.20, "ao_dist": 0.10, "edge": 0.10},
    "wood":    {"top": 0.08, "ao": 0.18, "ao_dist": 0.10, "edge": 0.06, "grain": 0.12, "grain_freq": 10.0},
    "cloth":   {"top": 0.10, "ao": 0.22, "ao_dist": 0.12, "edge": 0.00, "folds": 0.08, "folds_freq": 9.0},
    "skin":    {"top": 0.03, "ao": 0.08, "ao_dist": 0.06, "edge": 0.00},
    "hair":    {"top": 0.00, "ao": 0.18, "ao_dist": 0.10, "edge": 0.08},
    "glow":    {},
}

COLOUR_KIND = {   # palette colour name -> kind (class scripts may extend this dict)
    "stone_armour": "stone", "stone_dark": "stone", "stone_toe": "stone", "stone_blade": "stone", "stone_gap": "stone", "knee_dark": "stone",
    "brass": "metal", "iron": "metal",
    "leather": "leather", "leather_dark": "leather", "boot": "leather", "wrap_dark": "leather",
    "wood": "wood", "wood_seam": "wood",
    "navy": "cloth", "cream": "cloth", "trouser": "cloth", "team_cloth": "cloth", "team_dark": "cloth",
    "skin": "skin", "skin_shadow": "skin", "brow": "hair", "hair": "hair",
    "team_glow": "glow",
}


def kind_of(colour_name):
    """Finish kind index for a palette colour name (unknown names and raw RGBA tuples -> 'other')."""
    return KIND[COLOUR_KIND.get(colour_name, "other")] if isinstance(colour_name, str) else KIND["other"]


def _hash01(x, y, z, seed):
    h = math.sin(x * 127.1 + y * 311.7 + z * 74.7 + seed * 0.618) * 43758.5453
    return h - math.floor(h)


def painted_finish(bm, col, kind, lco=None, skip=None, seed=606, params=None):
    """Bake the painted finish into `col` (float colour layer, linear RGB, alpha untouched). See the module docstring.
    Returns a dict of vertex counts per effect (for build reports)."""
    P = {k: dict(v) for k, v in DEFAULTS.items()}
    for k, v in (params or {}).items():
        P.setdefault(k, {}).update(v)
    bm.verts.index_update()
    bm.verts.ensure_lookup_table()
    bm.normal_update()
    tree = BVHTree.FromBMesh(bm)
    rng = random.Random(seed)
    crack_planes = [(Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))).normalized(),
                     rng.uniform(-0.25, 0.25)) for _ in range(5)]
    # connected parts (one primitive each): z range for the top-light gradient
    part = {}
    zr = []
    for v in bm.verts:
        if v.index in part:
            continue
        pid = len(zr)
        stack, lo, hi = [v], v.co.z, v.co.z
        part[v.index] = pid
        while stack:
            a = stack.pop()
            lo, hi = min(lo, a.co.z), max(hi, a.co.z)
            for e in a.link_edges:
                b = e.other_vert(a)
                if b.index not in part:
                    part[b.index] = pid
                    stack.append(b)
        zr.append((lo, hi))
    dirs = []
    for i in range(12):
        a = 2 * math.pi * i / 12 + (0.4 if i % 2 else 0.0)
        el = math.radians(25 if i % 2 else 55)
        dirs.append(Vector((math.cos(a) * math.cos(el), math.sin(a) * math.cos(el), math.sin(el))))
    dirs.append(Vector((0, 0, 1)))
    stats = {"edge": 0, "chips": 0, "crack": 0, "hot": 0}
    new = {}
    for v in bm.verts:
        c = v[col]
        if abs(c[3] - 0.75) < 0.01 or (skip is not None and v[skip]):
            continue
        kname = KINDS[v[kind]] if v[kind] < len(KINDS) else "other"
        p = P.get(kname) or {}
        if not p:
            continue
        n = v.normal
        f = 1.0
        lo, hi = zr[part[v.index]]
        t = (v.co.z - lo) / max(hi - lo, 1e-6)
        f *= 1.0 + p.get("top", 0.0) * (t - 0.5)
        if p.get("ao", 0.0) > 0 and n.length > 0.5:                       # creases and under overlaps
            q = n.to_track_quat("Z", "Y")
            o = v.co + n * 0.004
            hit = sum(1 for d in dirs if tree.ray_cast(o, q @ d, p["ao_dist"])[0] is not None)
            f *= 1.0 - p["ao"] * hit / len(dirs)
        convex = 0.0
        if v.link_edges:
            convex = sum(n.dot((v.co - e.other_vert(v).co).normalized()) for e in v.link_edges) / len(v.link_edges)
        if p.get("edge", 0.0) > 0 and convex > 0.18 and n.z > 0.15:        # crisp upper-edge highlight
            f *= 1.0 + p["edge"]
            stats["edge"] += 1
        if kname == "stone":
            x, y, z = v.co
            if convex > 0.12 and _hash01(x, y, z, seed) < p["chips"]:     # lighter chipped corners
                f *= 1.0 + 0.12
                stats["chips"] += 1
            for pn, off in crack_planes:                                    # thin dark crack bands
                if abs(v.co.dot(pn) - off) < 0.012 and _hash01(z, x, y, seed + 7) < 0.5:
                    f *= 1.0 - p["crack"]
                    stats["crack"] += 1
                    break
        elif kname == "metal":
            hot = max(0.0, n.dot(KEY)) ** 6
            f *= 1.0 + p["hot"] * hot - p["rim"] * max(0.0, 0.35 - n.dot(KEY)) / 1.35
            stats["hot"] += 1 if hot > 0.5 else 0
        elif kname == "wood" and lco is not None:
            lx, ly, _ = v[lco]
            g = 0.5 + 0.5 * math.sin(lx * p["grain_freq"] * 2 * math.pi + 2.2 * math.sin(ly * 6.0))
            f *= 1.0 - p["grain"] * g
        elif kname == "cloth" and lco is not None:
            lx, ly, lz = v[lco]
            fo = 0.5 + 0.5 * math.sin((lx * 0.6 + lz) * p["folds_freq"] * 2 * math.pi / 3.0)
            f *= 1.0 - p["folds"] * fo
        f = max(CLAMP_LO, min(CLAMP_HI, f))
        k = f ** 2.2
        new[v.index] = (c[0] * k, c[1] * k, c[2] * k, c[3])
    for i, c in new.items():
        bm.verts[i][col] = c
    return stats
