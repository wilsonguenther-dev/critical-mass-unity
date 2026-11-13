"""Painted finish baked into vertex colour: the Kaiju's copy of cm_art/finish.py (Warrior clone, v6, 2026-11-12),
EXTENDED for creature materials (Kaiju v4). The class-character part below is unchanged; the additions are:

  NEW KINDS (DEFAULTS below; same parameters as the originals plus the ones named here)
    boulder   the Kaiju's rock masses: the stone treatment (chips, crack bands) with a stronger top light and deeper
              crease darkening, plus `strata`: faint horizontal value bands so a big facet is not one flat tone
    hide      smooth creature skin (head, core between rocks): soft top light, `mottle` = darker blotches by 3-D cell
              (cell size `mottle_size` metres), the bible's dappled hide
    scales    as hide with a tighter, regular `mottle` (rows of darker scale centres); for scaled patches
    belly     tan belly / chest plates: each plate lit from its top edge, `groove` darkening on the midline
    moss      soft clumps: strong crown-to-underside gradient and a fine light/dark `speckle`
    horn      teeth, claws, horn tips: root dark to tip light (the `top` gradient over the part) and a `hot` spot
    vein      glowing biomass veins / crystals: SELF-LIT (mask alpha 0.75), so nothing is baked here by design: the
              contract carries only a value k per vertex (max(rgb) / max(default glow)), which build_kaiju grades
              itself (crystal base 0.5 -> tip 1.0, crack floor 1.0, crack wall 0.72). Listed so the kind is explicit.
  NEW ENTRY geo_factors(V, F, face_kind, face_team, ...): runs painted_finish on a plain vertex / face list (the
            Kaiju builder's Geo) and returns ONE sRGB value factor per vertex; build_kaiju.paint multiplies the slot
            colour by it. Team-glow faces (alpha 0.75) are never touched, so the mask contract is unchanged.
  UNITS     the Kaiju is 5 m tall, so ao_dist and the crack band width are scaled by `scale` (default 1.0 = the
            2 m characters; the adult passes 2.5).

Original module text follows.

Painted finish baked into vertex colour (Critical Mass characters, v6, 2026-11-12). Shared by every class script.

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

KINDS = ("other", "stone", "metal", "leather", "wood", "cloth", "skin", "hair", "glow",
         "boulder", "hide", "scales", "belly", "moss", "horn", "vein")
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
    # ---- creature kinds (Kaiju v4)
    "boulder": {"top": 0.20, "ao": 0.34, "ao_dist": 0.16, "edge": 0.16, "chips": 0.22, "crack": 0.20, "strata": 0.05},
    "hide":    {"top": 0.12, "ao": 0.20, "ao_dist": 0.12, "edge": 0.00, "mottle": 0.07, "mottle_size": 0.16},
    "scales":  {"top": 0.10, "ao": 0.20, "ao_dist": 0.10, "edge": 0.06, "mottle": 0.12, "mottle_size": 0.07},
    "belly":   {"top": 0.16, "ao": 0.28, "ao_dist": 0.12, "edge": 0.10, "groove": 0.10},
    "moss":    {"top": 0.22, "ao": 0.22, "ao_dist": 0.10, "edge": 0.00, "speckle": 0.08},
    "horn":    {"top": 0.26, "ao": 0.14, "ao_dist": 0.06, "edge": 0.08, "hot": 0.20, "rim": 0.0},
    "vein":    {},
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


def painted_finish(bm, col, kind, lco=None, skip=None, seed=606, params=None, scale=1.0):
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
            hit = sum(1 for d in dirs if tree.ray_cast(o, q @ d, p["ao_dist"] * scale)[0] is not None)
            f *= 1.0 - p["ao"] * hit / len(dirs)
        convex = 0.0
        if v.link_edges:
            convex = sum(n.dot((v.co - e.other_vert(v).co).normalized()) for e in v.link_edges) / len(v.link_edges)
        if p.get("edge", 0.0) > 0 and convex > 0.18 and n.z > 0.15:        # crisp upper-edge highlight
            f *= 1.0 + p["edge"]
            stats["edge"] += 1
        if kname in ("stone", "boulder"):
            x, y, z = v.co
            if convex > 0.12 and _hash01(x, y, z, seed) < p["chips"]:     # lighter chipped corners
                f *= 1.0 + 0.12
                stats["chips"] += 1
            for pn, off in crack_planes:                                    # thin dark crack bands
                if abs(v.co.dot(pn) - off * scale) < 0.012 * scale and _hash01(z, x, y, seed + 7) < 0.5:
                    f *= 1.0 - p["crack"]
                    stats["crack"] += 1
                    break
            if kname == "boulder":                                           # faint strata bands
                f *= 1.0 + p["strata"] * math.sin(z * 9.0 / scale + 3.0 * _hash01(round(x), round(y), 0, seed))
        elif kname in ("hide", "scales"):
            ms = p["mottle_size"] * scale
            cx_, cy_, cz_ = (math.floor(v.co.x / ms), math.floor(v.co.y / ms), math.floor(v.co.z / ms))
            if kname == "scales":
                f *= 1.0 - p["mottle"] * (0.5 + 0.5 * math.sin(v.co.x / ms * 6.283) * math.sin(v.co.z / ms * 6.283 + (cx_ % 2) * 3.14))
            elif _hash01(cx_, cy_, cz_, seed + 3) > 0.62:
                f *= 1.0 - p["mottle"]
        elif kname == "belly":
            f *= 1.0 - p["groove"] * math.exp(-(v.co.x / (0.06 * scale)) ** 2)
        elif kname == "moss":
            f *= 1.0 + p["speckle"] * (2.0 * _hash01(v.co.x * 40, v.co.y * 40, v.co.z * 40, seed + 11) - 1.0)
        elif kname == "horn":
            f *= 1.0 + p["hot"] * max(0.0, n.dot(KEY)) ** 4
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


# ------------------------------------------------------------------------------------------------ Kaiju v4 entry
SLOT_KIND = {"Slate": "boulder", "SlateLight": "boulder", "Skin": "hide", "Moss": "moss", "Belly": "belly",
             "Teeth": "horn", "Hide": "hide", "Tongue": "other", "Eye": "other", "Pupil": "other", "Mouth": "other", "Telegraph": "other"}


def geo_factors(V, F, face_kind, face_team, seed=606, params=None, scale=1.0, skip_faces=None):
    """V: vertex positions (metres), F: faces (index tuples), face_kind: kind NAME per face, face_team: True for
    team-glow faces (never finished). skip_faces: faces whose vertices keep factor 1 (the painted face decal area).
    Returns one sRGB value factor per vertex (1.0 = unchanged) and the effect counts."""
    import bmesh
    bm = bmesh.new()
    col = bm.verts.layers.float_color.new("c")       # layers first: adding a layer invalidates existing BMVerts
    kind = bm.verts.layers.int.new("kind")
    skip = bm.verts.layers.int.new("skip")
    vs = [bm.verts.new(v) for v in V]
    vk, vteam, vfix = {}, set(), set()
    for fi, f in enumerate(F):
        try:
            bm.faces.new([vs[i] for i in f])
        except ValueError:
            pass
        for i in f:
            if face_team[fi]:
                vteam.add(i)
            else:
                vfix.add(i)
                vk.setdefault(i, KIND.get(face_kind[fi], 0))
            if skip_faces is not None and skip_faces[fi]:
                vs[i][skip] = 1
    for i, v in enumerate(vs):
        v[kind] = vk.get(i, 0)
        v[col] = (1.0, 1.0, 1.0, 0.75 if (i in vteam and i not in vfix) else 0.0)
    bm.normal_update()
    stats = painted_finish(bm, col, kind, skip=skip, seed=seed, params=params, scale=scale)
    out = [max(1e-6, v[col][0]) ** (1 / 2.2) for v in vs]
    bm.free()
    return out, stats
