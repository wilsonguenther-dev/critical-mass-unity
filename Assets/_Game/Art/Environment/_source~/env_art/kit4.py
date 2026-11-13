"""Skybreak Basin environment kit, version 4 (owner 2026-11-12: "the world needs more detail"; bridges, map, every piece).

kit4 is a PATCH on kit2 (v3): `patch()` swaps the builders of existing pieces IN PLACE (same names, pivots and
dimensions, so the Unity map dresser needs no change) and appends the new pieces to kit2.PIECES / kit2.BY_NAME.
Nothing changes until patch() is called (render_v4 renders the v3 "before" first, then patches).

Upgraded in place (names, pivots, bounds kept; bridge bounds are clamped to the v3 boxes):
  env_lip_rim_* (grass overhang roll + drips), env_cliff_s1..s6 / d1..d4 (paler strata with cracks, hanging roots,
  vines; embedded crystals and Kaiju-bone fossils only on the long pieces so they never repeat 100 times),
  env_waterfall (wider ribbon, two stepped foam ledges, mist puffs, splash ring), env_bridge_span / post / pier / cell.
New pieces (see NEW): ground patches, worn ruts, edge stones, puddle, grass / flower clumps, moss rocks, mushrooms,
reeds, berry bush, 3 tree species, floating chunks, damaged span, bridge anchor, lanterns, torch, fence, signpost +
3 sign boards, neutral worship banner, campfire, camp sacks, woodpile, Kaiju statue, Kaiju skull, bone ribs, altar,
rubble.

Vertex-colour contract unchanged: RGB = palette sRGB stored linear, alpha = mask code (0 fixed, 0.25 fixed self-lit).
Flames, embers, lantern glass and the neutral crystal lit facets use 0.25 (self-lit); nothing here uses team codes.
Painted finish (env_art/finish.py, copied from art/warrior-blender) is applied to stone, wood, bone and metal pieces by
apply_finish(): it multiplies the loop colours by the finish factor, then removes its helper layers (export unchanged).
"""
import math
import random

import bmesh
from mathutils import Matrix, Vector

from . import finish as pfinish
from . import geo, kit2, palette
from .geo import M, Mesh

F, UP, OUT, BAND, CH_OUT, CH_DROP, TILE, SQ2 = kit2.F, kit2.UP, kit2.OUT, kit2.BAND, kit2.CH_OUT, kit2.CH_DROP, kit2.TILE, kit2.SQ2

NEW_COLOURS = {
    "patch_light": ("#AECB58", "v4 painted light ground patch"),
    "patch_dark": ("#8DAA44", "v4 painted dark ground patch"),
    "moss_patch": ("#7FA040", "v4 moss patch"),
    "dirt_worn": ("#C9955A", "v4 worn dirt (P14 dirt tile #D99855, one step darker)"),
    "dirt_edge": ("#B5A55A", "v4 dirt-to-grass blend ring"),
    "rut": ("#9E6E3E", "v4 cart rut / mud"),
    "stone_pale": ("#C8C0AA", "v4 edge stones, statue (P12 pale stone)"),
    "statue": ("#ADA58E", "v4 Kaiju statue stone"),
    "bone": ("#EADFC4", "v4 Kaiju bone (lore: fossils in the cliffs)"),
    "bone_dark": ("#B9A985", "v4 bone shade / socket rim"),
    "socket": ("#3D3833", "v4 skull socket / crack dark"),
    "root": ("#6E4934", "v4 hanging roots"),
    "bark_old": ("#6F5B4B", "v4 gnarled tree bark"),
    "berry": ("#7B4FA6", "v4 berries, violet (team-neutral, not azure, not crimson)"),
    "shroom": ("#D9A441", "v4 mushroom cap, ochre"),
    "stem": ("#EFE4CB", "v4 mushroom stem"),
    "flower_v": ("#B9A0E3", "v4 flower heads, lilac"),
    "iron": ("#4B4F5A", "v4 lantern iron, nails, hooks"),
    "sack": ("#C9B08A", "v4 camp sack canvas"),
    "cloth_n": ("#E6D5B0", "v4 neutral worship banner cloth (Warm Cream, aged)"),
    "cloth_trim": ("#8A5A3C", "v4 banner trim, bedroll"),
    "emblem": ("#2B3654", "v4 Kaiju-eye emblem, carved glyphs (Void Navy lifted, lit)"),
    "crystal_n": ("#A6ECEF", "v4 neutral raw crystal lit facet (self-lit)"),
    "crystal_nd": ("#4A9DB3", "v4 neutral raw crystal body"),
    "flame": ("#FFC27A", "AD 2.7 brazier warm (self-lit)"),
    "flame_hot": ("#FFEBB5", "v4 flame core (self-lit)"),
    "ember": ("#FF9248", "v4 embers (self-lit)"),
    "glass": ("#FFD58C", "v4 lantern glass (self-lit)"),
    "puddle": ("#68AFD8", "v4 puddle water (self-lit, like water_f)"),
}
SELF_LIT = ("crystal_n", "flame", "flame_hot", "ember", "glass", "puddle")

# colour name -> painted-finish kind (finish.KINDS)
COLOUR_KIND = {"rock": "stone", "rock_mid": "stone", "rock_dark": "stone", "rim_rock": "stone", "rim_rock_b": "stone",
               "wall": "stone", "wall_b": "stone", "stone_pale": "stone", "statue": "stone", "bone": "stone",
               "bone_dark": "stone", "wood": "wood", "wood_b": "wood", "wood_dark": "wood", "trunk": "wood",
               "root": "wood", "bark_old": "wood", "iron": "metal", "brass": "metal", "rope": "cloth",
               "cloth_n": "cloth", "cloth_trim": "cloth", "sack": "cloth"}
REG = {}          # rounded linear RGBA -> palette name (filled by the tracking C below)


def _key(c):
    return tuple(round(x, 4) for x in c)


def C(name, mul=1.0):
    c = palette.vcol(name, mul)
    REG.setdefault(_key(c), name)
    return c


# ------------------------------------------------------------------------------------------------- small helpers
def grad(m, f, y0, y1, c0, c1):
    """Vertex-colour gradient on face f along Unity y (base colour c0 at y0, c1 at y1): painted light for free."""
    if f is None:
        return
    for lp in f.loops:
        t = max(0.0, min(1.0, (lp.vert.co.z - y0) / max(1e-6, y1 - y0)))
        lp[m.col] = tuple(c0[i] * (1 - t) + c1[i] * t for i in range(3)) + (c0[3],)


def blade(m, bx, bz, h, w, lean, ang, c0, c1, y=0.0):
    """Two-sided grass blade (2 tris) with a base->tip colour gradient."""
    dx, dz = math.cos(ang + 1.5708) * w, math.sin(ang + 1.5708) * w
    tip = (bx + math.cos(ang) * lean, y + h, bz + math.sin(ang) * lean)
    for s in (1, -1):
        f = F(m, [(bx - dx, y, bz - dz), (bx + dx, y, bz + dz), tip], c0, (-dz * s, 0.2, dx * s))
        grad(m, f, y, y + h, c0, c1)


def tube(m, pts, r, col, n=4, taper=1.0, col_end=None):
    pts = [Vector(p) for p in pts]
    rings = []
    for i, p in enumerate(pts):
        d = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        a = Vector((0, 1, 0)) if abs(d.y) < 0.9 else Vector((1, 0, 0))
        s = d.cross(a).normalized()
        t = d.cross(s).normalized()
        rr = r * (1 - (1 - taper) * i / max(1, len(pts) - 1))
        rings.append([p + (s * math.cos(2 * math.pi * k / n) + t * math.sin(2 * math.pi * k / n)) * rr for k in range(n)])
    for i in range(len(pts) - 1):
        c = (pts[i] + pts[i + 1]) / 2
        cc = col if col_end is None or i < len(pts) - 2 else col_end
        for k in range(n):
            j = (k + 1) % n
            q = [rings[i][k], rings[i][j], rings[i + 1][j], rings[i + 1][k]]
            F(m, q, cc, tuple(sum(q, Vector()) / 4 - c))
    return rings


def stone(m, cx, cz, r, h, col, top, rot=0.0, n=5, y0=-0.02):
    m.prism(cx, cz, r, y0, h, col, n=n, top=top, rot=rot, r_top=r * 0.78, ch=min(0.035, h * 0.3))


def spike(m, base, dirv, h, r, body, lit, n=5):
    d = Vector(dirv).normalized()
    a = Vector((0, 1, 0)) if abs(d.y) < 0.9 else Vector((1, 0, 0))
    s = d.cross(a).normalized()
    t = d.cross(s).normalized()
    b = Vector(base)
    lo = [b + (s * math.cos(2 * math.pi * k / n) + t * math.sin(2 * math.pi * k / n)) * r for k in range(n)]
    mid = [b + d * h * 0.62 + (p - b) * 0.85 for p in lo]
    tip = b + d * h
    for k in range(n):
        j = (k + 1) % n
        o = tuple(((lo[k] + lo[j]) / 2 - b).normalized())
        F(m, [lo[k], lo[j], mid[j], mid[k]], lit if k == 0 else body, o)
        F(m, [mid[k], mid[j], tip], lit if k in (0, 1) else body, tuple((Vector(o) + d).normalized()))


def blob_pts(rng, r, n, jit=0.16, sx=1.0, sz=1.0):
    out = []
    for i in range(n):
        a = 2 * math.pi * i / n + rng.uniform(-0.18, 0.18)
        k = rng.uniform(1 - jit, 1 + jit)
        out.append((math.cos(a) * r * sx * k, math.sin(a) * r * sz * k))
    return out


def gpatch(m, pts, core, ring, y=0.006, ring_w=0.30, cx=0.0, cz=0.0):
    inner = [(cx + (x - cx) * (1 - ring_w), cz + (z - cz) * (1 - ring_w)) for x, z in pts]
    F(m, [(x, y + 0.002, z) for x, z in inner], core, UP)
    n = len(pts)
    for k in range(n):
        j = (k + 1) % n
        F(m, [(pts[k][0], y, pts[k][1]), (pts[j][0], y, pts[j][1]), (inner[j][0], y, inner[j][1]),
              (inner[k][0], y, inner[k][1])], ring, UP)


def decal_at(m, cx, cz, r, col, y=0.008, n=16):
    for i in range(n):
        a0, a1 = 2 * math.pi * i / n, 2 * math.pi * (i + 1) / n
        F(m, [(cx, y, cz), (cx + math.cos(a0) * r, y, cz + math.sin(a0) * r), (cx + math.cos(a1) * r, y, cz + math.sin(a1) * r)],
          col, UP, mat=1, uv=[(0.5, 0.5), (0.5 + 0.5 * math.cos(a0), 0.5 + 0.5 * math.sin(a0)),
                              (0.5 + 0.5 * math.cos(a1), 0.5 + 0.5 * math.sin(a1))])


def flame(m, cx, y, cz, s=1.0):
    m.prism(cx, cz, 0.11 * s, y, y + 0.34 * s, C("flame"), n=5, r_top=0.0, rot=0.3)
    m.prism(cx, cz - 0.03 * s, 0.065 * s, y + 0.01, y + 0.24 * s, C("flame_hot"), n=4, r_top=0.0, rot=0.8)


def nails(m, xs, z0, z1, y):
    for x in xs:
        for z in (z0 + 0.07, z1 - 0.07):
            F(m, [(x - 0.026, y, z - 0.026), (x + 0.026, y, z - 0.026), (x + 0.026, y, z + 0.026), (x - 0.026, y, z + 0.026)],
              C("iron"), UP)


# ------------------------------------------------------------------------------------------------ 1. ground pieces
def ground_patch(m, seed, kind):
    rng = random.Random(seed)
    if kind == "light":
        gpatch(m, blob_pts(rng, 1.25, 11, sx=1.25), C("patch_light"), C("grass", 1.03))
        for k in range(4):
            a, r = rng.uniform(0, 6.28), rng.uniform(0.2, 0.8)
            F(m, [(math.cos(a) * r + x, 0.0105, math.sin(a) * r + z) for x, z in blob_pts(rng, 0.15, 5)], C("leaf_hi", 0.96), UP)
    elif kind == "dark":
        gpatch(m, blob_pts(rng, 1.4, 12, sx=1.35, sz=0.85), C("patch_dark"), C("grass_d"))
        for k in range(3):
            a, r = rng.uniform(0, 6.28), rng.uniform(0.2, 0.8)
            F(m, [(math.cos(a) * r + x, 0.0105, math.sin(a) * r + z) for x, z in blob_pts(rng, 0.2, 5)], C("grass_dark"), UP)
    elif kind == "moss":
        gpatch(m, blob_pts(rng, 0.75, 9), C("moss_patch"), C("patch_dark"))
        for k in range(3):
            a, r = rng.uniform(0, 6.28), rng.uniform(0.1, 0.45)
            stone(m, math.cos(a) * r, math.sin(a) * r, rng.uniform(0.06, 0.11), rng.uniform(0.05, 0.09), C("rock", 1.12),
                  C("moss"), rot=rng.uniform(0, 6))
    else:   # worn dirt
        gpatch(m, blob_pts(rng, 0.9, 10, sx=1.2), C("dirt_worn"), C("dirt_edge"), ring_w=0.34)
        for k in range(4):
            a, r = rng.uniform(0, 6.28), rng.uniform(0.1, 0.6)
            stone(m, math.cos(a) * r, math.sin(a) * r, rng.uniform(0.04, 0.07), rng.uniform(0.03, 0.05), C("stone_pale"),
                  C("stone_pale", 1.08), rot=rng.uniform(0, 6), n=4)
        for k in range(5):
            a = rng.uniform(0, 6.28)
            blade(m, math.cos(a) * 1.0, math.sin(a) * 0.82, rng.uniform(0.12, 0.2), 0.045, 0.05, a, C("grass_dark"), C("grass_l"))


def path_ruts(m, seed):
    """Worn shoulder of a route, 2.2 u along local +Z (centred), 1.4 u wide: dirt, two ruts, edge stones both sides."""
    rng = random.Random(seed)
    gpatch(m, blob_pts(rng, 1.0, 12, jit=0.12, sx=0.62, sz=1.18), C("dirt_worn"), C("dirt_edge"), ring_w=0.32)
    for sx in (-0.2, 0.2):
        z = -0.85
        while z < 0.8:
            L = rng.uniform(0.2, 0.42)
            x = sx + rng.uniform(-0.03, 0.03)
            F(m, [(x - 0.045, 0.0115, z), (x + 0.045, 0.0115, z), (x + 0.04, 0.0115, z + L), (x - 0.04, 0.0115, z + L)], C("dirt_worn", 0.86), UP)
            z += L + rng.uniform(0.08, 0.2)
    for sx in (-0.62, 0.62):
        for k in range(3):
            z = -0.7 + k * 0.6 + rng.uniform(-0.12, 0.12)
            r = rng.uniform(0.06, 0.1)
            stone(m, sx * rng.uniform(0.9, 1.1), z, r, rng.uniform(0.04, 0.08), C("stone_pale", rng.uniform(0.92, 1.0)),
                  C("stone_pale", 1.08) if rng.random() < 0.6 else C("moss"), rot=rng.uniform(0, 6))
    for k in range(4):
        blade(m, rng.choice((-0.5, 0.5)) * rng.uniform(0.8, 1.2), rng.uniform(-0.9, 0.9), 0.15, 0.04, 0.05, rng.uniform(0, 6), C("grass_dark"), C("grass_l"))


def edge_stones(m, seed):
    """Row of 4 kerb stones along local +X (0..1.2), under 0.12 u."""
    rng = random.Random(seed)
    x = 0.08
    for k in range(4):
        r = rng.uniform(0.08, 0.13)
        stone(m, x + r, rng.uniform(-0.04, 0.04), r, rng.uniform(0.06, 0.12), C("stone_pale", rng.uniform(0.9, 1.0)),
              C("moss") if k % 3 == 1 else C("stone_pale", 1.08), rot=rng.uniform(0, 6))
        x += 2 * r + rng.uniform(0.02, 0.08)
    for k in range(3):
        blade(m, rng.uniform(0.1, 1.1), rng.uniform(0.08, 0.15), 0.16, 0.04, 0.05, rng.uniform(0, 6), C("grass_dark"), C("grass_l"))


def puddle(m, seed):
    rng = random.Random(seed)
    pts = blob_pts(rng, 0.5, 10, sx=1.3, sz=0.8)
    gpatch(m, [(x * 1.25, z * 1.25) for x, z in pts], C("rut"), C("dirt_edge"), y=0.005)
    gpatch(m, pts, C("puddle"), C("water_f"), y=0.0085, ring_w=0.1)
    F(m, [(x * 0.35 - 0.12, 0.0125, z * 0.25 - 0.05) for x, z in pts], C("water_lf"), UP)   # sky glint


def grass_clump(m, seed):
    rng = random.Random(seed)
    for i in range(14):
        a, r = rng.uniform(0, 6.28), rng.uniform(0.0, 0.2)
        blade(m, math.cos(a) * r, math.sin(a) * r, rng.uniform(0.16, 0.29), 0.05, rng.uniform(0.04, 0.1), a,
              C("grass_dark", 0.92), C("leaf_hi") if i % 3 == 0 else C("grass_l"))


def flower_clump(m, seed, heads=("flower_y", "flower")):
    rng = random.Random(seed)
    for i in range(8):
        a, r = rng.uniform(0, 6.28), rng.uniform(0.0, 0.2)
        blade(m, math.cos(a) * r, math.sin(a) * r, rng.uniform(0.12, 0.2), 0.045, 0.06, a, C("grass_dark", 0.92), C("grass_l"))
    for i in range(5):
        a, r = rng.uniform(0, 6.28), rng.uniform(0.03, 0.22)
        x, z, y = math.cos(a) * r, math.sin(a) * r, rng.uniform(0.14, 0.22)
        col = C(heads[i % len(heads)])
        s = 0.075
        for (p, q) in (((s, 0), (0, s)), ((0, s), (-s, 0)), ((-s, 0), (0, -s)), ((0, -s), (s, 0))):
            F(m, [(x, y + s * 0.5, z), (x + p[0], y, z + p[1]), (x + q[0], y, z + q[1])], col, UP)


def moss_rocks(m, seed):
    rng = random.Random(seed)
    for k in range(3):
        a, r = 2.1 * k + rng.uniform(-0.3, 0.3), 0.18 if k else 0.0
        stone(m, math.cos(a) * r, math.sin(a) * r, rng.uniform(0.1, 0.17) * (1.3 if k == 0 else 1.0), rng.uniform(0.12, 0.24),
              C("rock", rng.uniform(1.06, 1.16)), C("moss"), rot=rng.uniform(0, 6), n=6)
    for k in range(4):
        blade(m, rng.uniform(-0.3, 0.3), rng.uniform(-0.3, 0.3), 0.15, 0.04, 0.05, rng.uniform(0, 6), C("grass_dark"), C("grass_l"))


def mushrooms(m, seed):
    rng = random.Random(seed)
    for k in range(4):
        a, r = 1.7 * k + rng.uniform(-0.3, 0.3), 0.16 if k else 0.0
        cx, cz = math.cos(a) * r, math.sin(a) * r
        h = rng.uniform(0.07, 0.17) * (1.25 if k == 0 else 1.0)
        rc = h * 0.7 + 0.03
        m.prism(cx, cz, rc * 0.28, -0.01, h, C("stem"), n=5, r_top=rc * 0.22)
        m.prism(cx, cz, rc, h - 0.01, h + rc * 0.55, C("shroom", rng.uniform(0.95, 1.05)), n=7, r_top=rc * 0.32, rot=rng.uniform(0, 6))
        for s in range(3):
            b = rng.uniform(0, 6.28)
            px, pz, py = cx + math.cos(b) * rc * 0.62, cz + math.sin(b) * rc * 0.62, h + rc * 0.24
            F(m, [(px - 0.02, py + 0.012, pz), (px + 0.02, py + 0.004, pz - 0.015), (px + 0.012, py + 0.018, pz + 0.02)],
              C("stem"), UP)


def reeds(m, seed):
    rng = random.Random(seed)
    for i in range(10):
        a, r = rng.uniform(0, 6.28), rng.uniform(0.0, 0.22)
        blade(m, math.cos(a) * r, math.sin(a) * r, rng.uniform(0.42, 0.72), 0.035, rng.uniform(0.05, 0.14), a,
              C("grass_dark", 0.88), C("grass_l"))
    for i in range(3):
        a, r = rng.uniform(0, 6.28), rng.uniform(0.05, 0.18)
        x, z, h = math.cos(a) * r, math.sin(a) * r, rng.uniform(0.5, 0.68)
        m.prism(x, z, 0.012, 0.0, h, C("grass_dark"), n=3)
        m.prism(x, z, 0.035, h, h + 0.13, C("trunk"), n=5, r_top=0.03)


def bush_berry(m, seed):
    rng = random.Random(seed)
    for k in range(3):
        a = 2 * math.pi * k / 3 + rng.uniform(-0.4, 0.4)
        r = rng.uniform(0.28, 0.36)
        c = (math.cos(a) * 0.3, 0.06, math.sin(a) * 0.3)
        kit2.lobe(m, c, (r, 0.36, r), seed + k, 0.95, flat_bottom=0.0)
        for b in range(3):
            u, v = rng.uniform(0, 6.28), rng.uniform(0.35, 0.8)
            p = Vector((c[0] + math.cos(u) * r * math.cos(v) * 1.02, c[1] + math.sin(v) * 0.36 * 1.02, c[2] + math.sin(u) * r * math.cos(v) * 1.02))
            for d in ((0.045, 0, 0), (-0.03, 0.01, 0.04), (-0.02, 0.0, -0.045)):
                q = p + Vector(d)
                m.prism(q.x, q.z, 0.035, q.y - 0.03, q.y + 0.035, C("berry"), n=4, r_top=0.012, rot=u)
    for f in m.bm.faces:     # berries carry material slot 1 (CM_Toon), leaves stay CM_Toon_Foliage
        if abs(f.loops[0][m.col][0] - palette.vcol("berry")[0]) < 1e-4 and abs(f.loops[0][m.col][2] - palette.vcol("berry")[2]) < 1e-4:
            f.material_index = 1


# ---------------------------------------------------------------------------------------------- trees (3 species)
def tree_oak(m, seed):
    rng = random.Random(seed)
    Dc, th = 3.4, 1.45
    top = (0.12, th, -0.06)
    kit2._trunk(m, (0, -0.05, 0), top, 0.27, 0.17, C("trunk"), n=7)
    for k in range(5):
        a = 2 * math.pi * k / 5 + rng.uniform(-0.3, 0.3)
        kit2._trunk(m, (math.cos(a) * 0.08, 0.32, math.sin(a) * 0.08), (math.cos(a) * 0.62, -0.05, math.sin(a) * 0.62),
                    0.11, 0.035, C("trunk", 0.92), n=5)
    kit2._trunk(m, (0.08, 1.0, 0.0), (0.6, 1.55, 0.25), 0.09, 0.05, C("trunk", 0.95), n=5)
    lob = []
    for k in range(5):
        a = 2 * math.pi * k / 5 + rng.uniform(-0.25, 0.25)
        lob.append(((top[0] + math.cos(a) * 0.40 * Dc, th + 0.2 + rng.uniform(-0.05, 0.1), top[2] + math.sin(a) * 0.40 * Dc),
                    rng.uniform(0.24, 0.29) * Dc, 0.82))
    for k in range(3):
        a = 2 * math.pi * k / 3 + 0.6 + rng.uniform(-0.25, 0.25)
        lob.append(((top[0] + math.cos(a) * 0.2 * Dc, th + 0.85, top[2] + math.sin(a) * 0.2 * Dc), 0.25 * Dc, 0.93))
    lob.append(((top[0], th + 1.35, top[2]), 0.22 * Dc, 1.0))
    for i, (c, r, mul) in enumerate(lob):
        kit2.lobe(m, c, (r, r * 0.8, r), seed * 11 + i, mul, flat_bottom=th - 0.2)


def tree_pine(m, seed):
    rng = random.Random(seed)
    kit2._trunk(m, (0, -0.05, 0), (0.03, 1.0, 0.0), 0.13, 0.07, C("trunk"), n=6)
    for k in range(3):
        a = 2.1 * k
        kit2._trunk(m, (0, 0.18, 0), (math.cos(a) * 0.32, -0.05, math.sin(a) * 0.32), 0.07, 0.03, C("trunk", 0.92), n=4)
    n = 10
    for t in range(4):
        y0 = 0.55 + t * 0.62
        R = 1.05 - t * 0.21
        hgt = 1.05 if t < 3 else 1.25
        rot = rng.uniform(0, 1)
        low = [(math.cos(rot + 2 * math.pi * i / n) * R * (1.0 if i % 2 == 0 else 0.8), y0 - (0.12 if i % 2 == 0 else 0.0),
                math.sin(rot + 2 * math.pi * i / n) * R * (1.0 if i % 2 == 0 else 0.8)) for i in range(n)]
        midr = [(math.cos(rot + 2 * math.pi * (i + 0.5) / n) * R * 0.52, y0 + hgt * 0.42,
                 math.sin(rot + 2 * math.pi * (i + 0.5) / n) * R * 0.52) for i in range(n)]
        apex = (0.0, y0 + hgt, 0.0)
        mul = 0.86 + 0.05 * t
        for i in range(n):
            j = (i + 1) % n
            q = [low[i], low[j], midr[i]]
            nrm = (sum((Vector(p) for p in q), Vector()) / 3 - Vector((0, y0 + 0.2, 0))).normalized()
            F(m, q, C("leaf_hi") if nrm.dot(kit2.HI_DIR) > 0.72 else C("leaf_light", mul), tuple(nrm), smooth=True)
            q2 = [midr[i], midr[(i + 1) % n], low[j]]
            nrm2 = (sum((Vector(p) for p in q2), Vector()) / 3 - Vector((0, y0 + 0.2, 0))).normalized()
            F(m, q2, C("leaf_light", mul * 0.96), tuple(nrm2), smooth=True)
            q3 = [midr[i], apex, midr[(i + 1) % n]]
            nrm3 = (sum((Vector(p) for p in q3), Vector()) / 3 - Vector((0, y0 + hgt * 0.4, 0))).normalized()
            F(m, q3, C("leaf_hi") if nrm3.dot(kit2.HI_DIR) > 0.80 else C("leaf_light", mul * 1.04), tuple(nrm3), smooth=True)


def tree_gnarl(m, seed):
    rng = random.Random(seed)
    path = [(0, -0.05, 0), (0.18, 0.5, 0.05), (0.02, 1.0, -0.08), (-0.25, 1.45, 0.0), (-0.32, 1.85, 0.1)]
    tube(m, path, 0.32, C("bark_old"), n=6, taper=0.6)
    for k in range(3):
        a = 2.3 * k + 0.4
        tube(m, [(math.cos(a) * 0.12, 0.3, math.sin(a) * 0.12), (math.cos(a) * 0.45, 0.22, math.sin(a) * 0.45),
                 (math.cos(a) * 0.8, -0.06, math.sin(a) * 0.8)], 0.09, C("bark_old", 0.92), n=4, taper=0.4)
    br = [[(0.0, 1.05, -0.06), (0.45, 1.45, -0.18), (0.75, 1.75, -0.25)], [(-0.25, 1.5, 0.0), (-0.6, 1.8, 0.22)]]
    for b in br:
        tube(m, b, 0.09, C("bark_old"), n=5, taper=0.5)
    cl = [((0.75, 2.0, -0.25), 0.66, 0.9), ((0.35, 2.3, 0.1), 0.6, 0.95), ((0.9, 2.2, 0.2), 0.5, 0.88),
          ((-0.6, 2.05, 0.25), 0.62, 0.9), ((-0.3, 2.4, 0.05), 0.58, 0.97), ((0.15, 2.7, 0.0), 0.55, 1.0)]
    for i, (c, r, mul) in enumerate(cl):
        kit2.lobe(m, c, (r, r * 0.78, r), seed * 13 + i, mul * 0.97, flat_bottom=c[1] - r * 0.45)
    for (x, y, z) in ((0.55, 1.45, -0.2), (-0.6, 1.66, 0.18), (0.95, 1.65, -0.3)):
        f = F(m, [(x - 0.05, y, z - 0.03), (x + 0.05, y, z - 0.03), (x + 0.01, y - 0.45, z - 0.03)], C("moss"), (0, 0, -1))
        grad(m, f, y - 0.45, y, C("grass_dark"), C("moss"))


# ------------------------------------------------------------------------------------------------ 2. cliffs (patch)
def _zf(c, x, y):
    """z of course c's face at (x, y) (outward is -z)."""
    x0, xf, x1, yt, yb, o, ob, fold = c
    fx = fold * ((x - x0) / max(1e-6, xf - x0) if x <= xf else (x1 - x) / max(1e-6, x1 - xf))
    t = max(0.0, min(1.0, (yt - y) / max(1e-6, yt - yb)))
    return -((o + fx) * (1 - t) + (ob + fx) * t)


def _course_at(cs, y):
    for c in cs:
        if c[4] <= y <= c[3]:
            return c
    return cs[-1]


def facet_v4(m, x0, x1, rng, y_top, back=0.6, rare=False):
    """v3 facet (strata courses, moss shelves, jagged taper) in paler stone, plus cracks, hanging roots, vines and,
    on long pieces only, an embedded raw crystal or a Kaiju-bone fossil."""
    moss, mossd = C("moss"), C("moss", 0.93)
    D = rng.uniform(3.8, 6.8)
    o = CH_OUT + rng.uniform(-0.03, 0.07)
    y = y_top
    F(m, [(x0, y, back), (x1, y, back), (x1, y, -o), (x0, y, -o)], moss, UP)
    xf = x0 + (x1 - x0) * rng.uniform(0.35, 0.65)
    ra, rb = C("rock", rng.uniform(1.07, 1.14)), C("rock_mid", rng.uniform(1.16, 1.24))
    k = 0
    cs = []
    while y - BAND > -(D - 1.3):
        rock = ra if k % 2 == 0 else rb
        k += 1
        yb = y - BAND
        ob = o + rng.uniform(-0.05, 0.02)
        fold = rng.uniform(0.03, 0.09)
        F(m, [(x0, y, -o), (xf, y, -o - fold), (xf, yb, -ob - fold), (x0, yb, -ob)], rock, (-0.3, 0, -1))
        F(m, [(xf, y, -o - fold), (x1, y, -o), (x1, yb, -ob), (xf, yb, -ob - fold)], C("rock", rng.uniform(0.98, 1.03)) if k % 2 else rock, (0.3, 0, -1))
        F(m, [(x0, y, -o), (x0, yb, -ob), (x0, yb, back), (x0, y, back)], rock, (-1, 0, 0))
        F(m, [(x1, y, -o), (x1, y, back), (x1, yb, back), (x1, yb, -ob)], rock, (1, 0, 0))
        F(m, [(x0, y, back), (x1, y, back), (x1, yb, back), (x0, yb, back)], rock, (0, 0, 1))
        cs.append((x0, xf, x1, y, yb, o, ob, fold))
        on = ob - rng.uniform(0.08, 0.18)
        F(m, [(x0, yb, -ob), (xf, yb, -ob - fold), (x1, yb, -ob), (x1, yb, -on), (x0, yb, -on)],
          moss if rng.random() < 0.8 else mossd, UP)
        y, o = yb, on
    xm = x0 + (x1 - x0) * rng.uniform(0.3, 0.7)
    tip = (xm, -D, -o + 0.45)
    rock = C("rock", 0.98)
    F(m, [(x0, y, -o), (x1, y, -o), tip], rock, (0, -0.3, -1))
    F(m, [(x0, y, -o), tip, (x0, y, back)], rock, (-1, -0.3, 0))
    F(m, [(x1, y, -o), (x1, y, back), tip], rock, (1, -0.3, 0))
    F(m, [(x0, y, back), tip, (x1, y, back)], rock, (0, -0.3, 1))
    if not cs:
        return
    W = x1 - x0
    # cracks: a dark zig-zag across one or two courses
    if rng.random() < 0.6 and W > 0.6:
        xc = rng.uniform(x0 + 0.2, x1 - 0.2)
        yc = cs[0][3] - rng.uniform(0.1, 0.5) - BAND * rng.randint(0, max(0, len(cs) - 2))
        pts = []
        for i in range(4):
            yy = yc - i * 0.26
            xx = xc + (rng.uniform(-0.09, 0.09) if i else 0.0)
            pts.append((xx, yy))
        for i in range(3):
            (xa, ya), (xb, yb_) = pts[i], pts[i + 1]
            wa, wb = 0.024 - 0.005 * i, 0.019 - 0.005 * i
            za, zb = _zf(_course_at(cs, ya), xa, ya) - 0.012, _zf(_course_at(cs, yb_), xb, yb_) - 0.012
            F(m, [(xa - wa, ya, za), (xa + wa, ya, za), (xb + wb, yb_, zb), (xb - wb, yb_, zb)], C("socket"), (0, 0, -1))
    # hanging roots from under the rim band
    for r in range(rng.choice((0, 1, 1, 2))):
        xr = rng.uniform(x0 + 0.1, x1 - 0.1)
        Lr = rng.uniform(0.6, 1.5)
        pts = [(xr + rng.uniform(-0.06, 0.06) * i, cs[0][3] + 0.02 - Lr * i / 3) for i in range(4)]
        for i in range(3):
            (xa, ya), (xb, yb_) = pts[i], pts[i + 1]
            wa, wb = 0.045 * (1 - i / 3.5), 0.045 * (1 - (i + 1) / 3.5)
            za, zb = _zf(_course_at(cs, ya), xa, ya) - 0.035, _zf(_course_at(cs, yb_), xb, yb_) - 0.035
            f = F(m, [(xa - wa, ya, za), (xa + wa, ya, za), (xb + wb, yb_, zb), (xb - wb, yb_, zb)], C("root"), (0, 0, -1))
            grad(m, f, yb_, ya, C("root", 0.8), C("root", 1.08))
    # vines with leaf diamonds
    if rng.random() < 0.38:
        xv = rng.uniform(x0 + 0.15, x1 - 0.15)
        Lv = rng.uniform(1.0, min(2.4, BAND * len(cs)))
        n = 4
        pts = [(xv + 0.07 * math.sin(i * 1.7), cs[0][3] + 0.02 - Lv * i / n) for i in range(n + 1)]
        for i in range(n):
            (xa, ya), (xb, yb_) = pts[i], pts[i + 1]
            za, zb = _zf(_course_at(cs, ya), xa, ya) - 0.03, _zf(_course_at(cs, yb_), xb, yb_) - 0.03
            F(m, [(xa - 0.02, ya, za), (xa + 0.02, ya, za), (xb + 0.02, yb_, zb), (xb - 0.02, yb_, zb)], C("leaf"), (0, 0, -1))
            s = 1 if i % 2 else -1
            lx, ly, lz = (xa + xb) / 2, (ya + yb_) / 2, (za + zb) / 2 - 0.01
            F(m, [(lx, ly + 0.07, lz), (lx + s * 0.11, ly + 0.02, lz - 0.02), (lx, ly - 0.06, lz), (lx + s * 0.03, ly, lz - 0.03)],
              C("leaf_hi") if i % 3 == 0 else C("leaf_light"), (0, 0.3, -1))
    # embedded raw crystal or Kaiju-bone fossil, long pieces only
    if rare and len(cs) >= 3:
        roll = rng.random()
        c = cs[rng.randint(1, len(cs) - 2)]
        xe = rng.uniform(x0 + 0.3, x1 - 0.3)
        ye = (c[3] + c[4]) / 2
        ze = _zf(c, xe, ye)
        if roll < 0.32:
            F(m, [(xe + 0.22 * math.cos(a), ye + 0.17 * math.sin(a), ze - 0.01) for a in [2 * math.pi * i / 6 for i in range(6)]],
              C("socket"), (0, 0, -1))
            for s in range(3):
                d = (rng.uniform(-0.5, 0.5), rng.uniform(0.1, 0.6), -1.0)
                spike(m, (xe + rng.uniform(-0.08, 0.08), ye + rng.uniform(-0.06, 0.06), ze + 0.04), d,
                      rng.uniform(0.32, 0.55), rng.uniform(0.06, 0.09), C("crystal_nd"), C("crystal_n"))
        elif roll < 0.58:
            F(m, [(xe + 0.42 * math.cos(a), ye + 0.3 * math.sin(a), ze - 0.008) for a in [2 * math.pi * i / 8 for i in range(8)]],
              C("rock_mid", 0.9), (0, 0, -1))
            for r in range(4):
                xs = xe - 0.27 + r * 0.18
                tube(m, [(xs, ye + 0.2, ze - 0.02), (xs + 0.04, ye + 0.05, ze - 0.11), (xs + 0.03, ye - 0.15, ze - 0.09),
                         (xs, ye - 0.27, ze - 0.02)], 0.035, C("bone"), n=4, taper=0.6)
            tube(m, [(xe - 0.38, ye + 0.24, ze - 0.03), (xe + 0.38, ye + 0.22, ze - 0.03)], 0.06, C("bone_dark", 1.08), n=5)


def cliff_skirt_v4(m, seed, L):
    rng = random.Random(seed)
    xs = [0.0]
    for w in kit2._partition(L, 0.8, 2.0, rng):
        xs.append(xs[-1] + w)
    xs[-1] = L
    for i in range(len(xs) - 1):
        facet_v4(m, xs[i], xs[i + 1], rng, -BAND, rare=L >= 2.9)


# ------------------------------------------------------------------------------------- grass overhang lip (patch)
def rim_lip_v4(m, seed, L):
    """v3 rim lip + a lit grass roll just inland of the fringe (the overhang highlight) + grass drips over the band."""
    rng = random.Random(seed)
    kit2.fringe(m, 0.0, L, rng)
    kit2.rim_band(m, 0.0, L, rng)
    n = max(2, round(L / 0.125))
    xs = [L * i / n for i in range(n + 1)]
    zi = [0.11 + rng.uniform(-0.03, 0.05) for _ in xs]
    for i in range(n):
        f = F(m, [(xs[i], 0.0085, 0.008), (xs[i + 1], 0.0085, 0.008), (xs[i + 1], 0.0085, zi[i + 1]), (xs[i], 0.0085, zi[i])],
              C("grass_l"), UP)
    k = max(2, round(L / 0.3))
    for i in range(k):
        x = L * (i + rng.uniform(0.25, 0.75)) / k
        w = rng.uniform(0.06, 0.1)
        dl = rng.uniform(0.12, 0.26)
        tz = -0.06 - dl * 0.75
        ty = -0.03 - dl * 0.5
        f = F(m, [(x - w, -0.002, -0.05), (x + w, -0.002, -0.05), (x + rng.uniform(-0.03, 0.03), ty, tz)], C("grass_dark"), (0, 0.85, -0.5))
        grad(m, f, ty, 0.0, C("grass_dark", 0.85), C("grass_dark", 1.08))


# --------------------------------------------------------------------------------------------- 4. water (patch)
def waterfall_v4(m, seed, fall=6.5, foam_w=kit2.FOAM_W):
    """v3 pool, foam band and ink kept; ribbon wider (0.88 u), two stepped foam ledges, mist puffs and a splash ring."""
    rng = random.Random(seed)
    water, foam, ink = C("water_f"), C("foam"), C("ink")
    edge = [(0.44, -0.06), (0.44, 0.42), (0.26, 0.70), (-0.26, 0.70), (-0.44, 0.42), (-0.44, -0.06)]
    F(m, [(x, 0.012, z) for (x, z) in edge], water, UP)
    pts = kit2._dense(edge, 0.05)
    nin = []
    for k in range(len(pts)):
        a, b = pts[max(0, k - 1)], pts[min(len(pts) - 1, k + 1)]
        tx, tz = b[0] - a[0], b[1] - a[1]
        ln = math.hypot(tx, tz) or 1.0
        nx, nz = -tz / ln, tx / ln
        if (0.0 - pts[k][0]) * nx + (0.3 - pts[k][1]) * nz < 0:
            nx, nz = -nx, -nz
        nin.append((nx, nz))
    inner = [(p[0] + n[0] * foam_w * rng.uniform(0.93, 1.07), p[1] + n[1] * foam_w * rng.uniform(0.93, 1.07)) for p, n in zip(pts, nin)]
    outer = [(p[0] - n[0] * 0.04, p[1] - n[1] * 0.04) for p, n in zip(pts, nin)]
    for k in range(len(pts) - 1):
        F(m, [(pts[k][0], 0.015, pts[k][1]), (pts[k + 1][0], 0.015, pts[k + 1][1]),
              (inner[k + 1][0], 0.015, inner[k + 1][1]), (inner[k][0], 0.015, inner[k][1])], foam, UP)
        F(m, [(outer[k][0], 0.013, outer[k][1]), (outer[k + 1][0], 0.013, outer[k + 1][1]),
              (pts[k + 1][0], 0.013, pts[k + 1][1]), (pts[k][0], 0.013, pts[k][1])], ink, UP)
    xs = [-0.46 + 0.92 * i / 12 for i in range(13)]
    za = [0.07 + rng.uniform(-0.02, 0.02) for _ in xs]
    zb = [-0.11 + rng.uniform(-0.03, 0.02) for _ in xs]
    for i in range(12):
        F(m, [(xs[i], 0.016, za[i]), (xs[i + 1], 0.016, za[i + 1]), (xs[i + 1], 0.016, zb[i + 1]), (xs[i], 0.016, zb[i])], foam, UP)
    rows = 16
    cols = [C("water_lf"), C("water_f"), C("water_df"), C("water_f"), C("water_lf"), C("water_df")]
    nc = len(cols)
    steps = (6, 11)        # rows where the fall hits a rock step: foam band + 0.14 u kick outward

    def cpt(t):
        if t < 0.125:
            f = t / 0.125
            return -f * (CH_DROP - 0.02) + 0.02 * (1 - f), -0.08 - f * (CH_OUT - 0.02)
        t2 = (t - 0.125) / 0.875
        kick = sum(0.14 for s in steps if t >= (s + 1) / rows)
        return -(CH_DROP - 0.02) - t2 * fall, -0.08 - (CH_OUT - 0.02) - 0.55 * math.sqrt(t2) - kick

    for r in range(rows):
        t0, t1 = r / rows, (r + 1) / rows
        (y0, z0), (y1, z1) = cpt(t0), cpt(t1)
        w0, w1 = 0.44 - 0.05 * t0, 0.44 - 0.05 * t1
        for c in range(nc):
            u0, u1 = c / nc, (c + 1) / nc
            xa0, xb0 = -w0 + 2 * w0 * u0, -w0 + 2 * w0 * u1
            xa1, xb1 = -w1 + 2 * w1 * u0, -w1 + 2 * w1 * u1
            col = foam if r == 0 or r in steps else cols[(c + r // 5) % nc]
            uv = [(u0, t0 * fall), (u1, t0 * fall), (u1, t1 * fall), (u0, t1 * fall)]
            F(m, [(xa0, y0, z0), (xb0, y0, z0), (xb1, y1, z1), (xa1, y1, z1)], col, (0, 0.15, -1), uv=uv)
            F(m, [(xa0, y0 - 0.3, z0 + 0.12), (xb0, y0 - 0.3, z0 + 0.12), (xb1, y1 - 0.3, z1 + 0.3),
                  (xa1, y1 - 0.3, z1 + 0.3)], cols[c], (0, 0, 1))
        for sx in (-1, 1):
            F(m, [(sx * w0, y0, z0), (sx * w1, y1, z1), (sx * w1, y1 - 0.3, z1 + 0.3), (sx * w0, y0 - 0.3, z0 + 0.12)],
              cols[2], (sx, 0, 0))
        if r in steps:     # jagged foam skirt hanging off the step, in front of the sheet
            ww = w1 + 0.05
            for i in range(7):
                xa, xb = -ww + 2 * ww * i / 7, -ww + 2 * ww * (i + 1) / 7
                F(m, [(xa, y1 + 0.02, z1 - 0.05), (xb, y1 + 0.02, z1 - 0.05), ((xa + xb) / 2 + rng.uniform(-0.03, 0.03),
                      y1 - rng.uniform(0.14, 0.26), z1 - 0.06)], foam, (0, 0.2, -1))
    yb, zb_ = cpt(1.0)
    # splash ring (jagged foam annulus) + crown spikes
    nr = 12
    for i in range(nr):
        a0, a1 = 2 * math.pi * i / nr, 2 * math.pi * (i + 1) / nr
        ro0, ro1 = 0.72 * rng.uniform(0.85, 1.15), 0.72 * rng.uniform(0.85, 1.15)
        F(m, [(math.cos(a0) * 0.34, yb + 0.06, zb_ + math.sin(a0) * 0.3), (math.cos(a1) * 0.34, yb + 0.06, zb_ + math.sin(a1) * 0.3),
              (math.cos(a1) * ro1, yb, zb_ + math.sin(a1) * ro1 * 0.8), (math.cos(a0) * ro0, yb, zb_ + math.sin(a0) * ro0 * 0.8)], foam, UP)
    for i in range(7):
        a = 2 * math.pi * i / 7 + 0.3
        x, z = math.cos(a) * 0.42, zb_ + math.sin(a) * 0.36
        F(m, [(x - 0.07, yb + 0.04, z), (x + 0.07, yb + 0.04, z), (x * 1.15, yb + rng.uniform(0.22, 0.4), z - 0.04)], foam, (0, 0.2, -1))
    # mist puffs (low-poly, cheap) around the base
    for k in range(5):
        a = 2 * math.pi * k / 5 + rng.uniform(-0.3, 0.3)
        m.blob(math.cos(a) * 0.55, yb + rng.uniform(-0.1, 0.25), zb_ + math.sin(a) * 0.45, rng.uniform(0.32, 0.5), 0.26,
               rng.uniform(0.3, 0.42), C("cloud") if k % 2 == 0 else C("cloud_shade", 1.06), sub=1, smooth=True, seed=seed + k, jitter=0.1)


def float_chunk(m, seed, s):
    """Floating earth chunk beneath the islands: grass cap with fringe, two rock tiers, a tip and a hanging root."""
    rng = random.Random(seed)
    n = 8
    top = [(math.cos(2 * math.pi * i / n) * s * rng.uniform(0.85, 1.1), 0.0, math.sin(2 * math.pi * i / n) * s * rng.uniform(0.85, 1.1)) for i in range(n)]
    F(m, list(reversed(top)), C("grass"), UP)
    for i in range(n):
        j = (i + 1) % n
        mx, mz = (top[i][0] + top[j][0]) / 2 * 1.12, (top[i][2] + top[j][2]) / 2 * 1.12
        F(m, [top[i], top[j], (mx, -0.12 * s, mz)], C("grass_dark"), (mx, 0.4, mz))
    r1 = [(p[0] * 0.92, -0.45 * s, p[2] * 0.92) for p in top]
    r2 = [(p[0] * 0.5 + rng.uniform(-0.05, 0.05), -1.0 * s, p[2] * 0.5) for p in top]
    tip = (rng.uniform(-0.1, 0.1) * s, -1.9 * s, rng.uniform(-0.1, 0.1) * s)
    for i in range(n):
        j = (i + 1) % n
        o = ((top[i][0] + top[j][0]) / 2, 0, (top[i][2] + top[j][2]) / 2)
        F(m, [top[i], top[j], r1[j], r1[i]], C("rock", 1.1 if i % 2 else 1.02), o)
        F(m, [r1[i], r1[j], r2[j], r2[i]], C("rock_mid", 1.15), (o[0], -0.3, o[2]))
        F(m, [r2[i], r2[j], tip], C("rock", 0.98), (o[0], -0.6, o[2]))
    x = top[2][0] * 0.8
    f = F(m, [(x - 0.04, -0.4 * s, top[2][2] * 0.95), (x + 0.04, -0.4 * s, top[2][2] * 0.95), (x, -1.3 * s, top[2][2] * 0.7)], C("root"), (0, 0, 1))


# ------------------------------------------------------------------------------------------------- 3. bridges
SPAN_BOX = ((-1.66, -0.3, -0.08), (1.66, 0.8, 2.0465))
POST_BOX = ((-0.08, -0.3, -0.08), (0.08, 0.8, 0.08))
PIER_BOX = ((-0.32, -0.9, -0.32), (0.32, 0.86, 0.32))
CELL_BOX = ((-0.5, -0.2, -0.5), (0.5, -0.0051, 0.5))


def _plank(m, rng, xl, xr, z0, z1, ytop, ybot, col, yaw=0.0):
    zc, xc = (z0 + z1) / 2, (xl + xr) / 2
    m.push(M((xc, 0, zc), yaw=yaw))
    m.cbox(xl - xc, ybot, z0 - zc, xr - xc, ytop, z1 - zc, col, top=col, ch=0.03)
    m.pop()


def bridge_span_v4(m, seed, damaged=False):
    """2.04 u tile, deck top at +0.05 (unchanged), 6 thick uneven planks with gaps, nails over the stringers, one cracked
    plank; chunky posts with caps, rope wraps and a lantern hook on the +X post; thick sagging ropes with knots and
    hanger ropes. damaged=True: plank 2 missing, plank 3 snapped and hanging, the -X top rope snapped."""
    rng = random.Random(seed)
    zc = 0.02
    for i in range(6):
        w = rng.uniform(0.27, 0.31)
        z0, z1 = zc, min(zc + w, 2.02)
        zc = z1 + rng.uniform(0.035, 0.05)
        if damaged and i == 2:
            continue
        dx, cut = 0.0, 0.0
        if i == 5:
            dx = rng.choice((-0.1, 0.1))
        xl = -1.5 - rng.uniform(0.0, 0.06) + dx
        xr = 1.5 + rng.uniform(0.0, 0.06) + dx - cut
        col = C("wood", rng.uniform(0.94, 1.06)) if i % 2 else C("wood_b", rng.uniform(0.94, 1.06))
        ytop = 0.05 - rng.uniform(0.0, 0.014)
        ybot = -0.035 - rng.uniform(0.0, 0.03)
        if damaged and i == 3:
            m.push(M((0.0, 0.0, z0), roll=0.0, pitch=0.0))
            _plank(m, rng, xl, -0.1, 0.0, z1 - z0, ytop, ybot, col)
            m.pop()
            m.push(M((0.05, 0.0, z0), roll=-28.0))
            _plank(m, rng, 0.0, xr - 0.05, 0.0, z1 - z0, ytop, ybot, col)
            m.pop()
            continue
        _plank(m, rng, xl, xr, z0, z1, ytop, ybot, col, yaw=rng.uniform(-2.0, 2.0))
        nails(m, (-1.15, 1.15), z0, z1, ytop + 0.002)
        if i == 1:     # crack along one plank
            F(m, [(-0.6, ytop + 0.003, (z0 + z1) / 2 - 0.012), (0.3, ytop + 0.003, (z0 + z1) / 2 + 0.01),
                  (0.3, ytop + 0.003, (z0 + z1) / 2 + 0.03), (-0.6, ytop + 0.003, (z0 + z1) / 2 + 0.006)], C("wood_dark"), UP)
    wd = C("wood_dark")
    F(m, [(-1.45, -0.06, 0.0), (1.45, -0.06, 0.0), (1.45, -0.06, TILE), (-1.45, -0.06, TILE)], wd, UP)
    for x0 in (-1.25, 1.05):
        m.box(x0, -0.2, 0.0, x0 + 0.2, -0.06, TILE, wd)
    for sx in (-1, 1):
        xo, xi = 1.66 * sx, 1.44 * sx
        a, b = min(xo, xi), max(xo, xi)
        m.cbox(a + 0.01, -0.3, -0.08, b - 0.01, 0.72, 0.12, C("wood_dark", 1.12), top=C("wood"), ch=0.035)
        m.cbox(a, 0.72, -0.08, b, 0.80, 0.12, C("wood_dark"), top=C("wood", 0.9), ch=0.03)
        for (y0, y1) in ((0.6, 0.68), (0.33, 0.4)):
            m.box(a - 0.005, y0, -0.085, b + 0.005, y1, 0.125, C("rope"))
        top_y, sag = 0.64, 0.17
        if damaged and sx < 0:
            kit2._rope(m, (1.56 * sx, top_y, 0.0), (1.56 * sx, 0.1, 0.65), 0.05, C("rope"), segs=4, r=0.04)
            kit2._rope(m, (1.56 * sx, top_y, TILE), (1.56 * sx, 0.0, TILE - 0.55), 0.05, C("rope"), segs=4, r=0.04)
        else:
            kit2._rope(m, (1.56 * sx, top_y, 0.0), (1.56 * sx, top_y, TILE), sag, C("rope"), segs=8, r=0.04)
            for t in (0.33, 0.66):        # knots on the sagging rope
                y = top_y - sag * 4 * t * (1 - t)
                m.prism(1.56 * sx, t * TILE, 0.062, y - 0.05, y + 0.05, C("rope", 0.86), n=5, r_top=0.05)
            for t in (0.33, 0.66):        # hanger ropes rail -> deck edge
                y = top_y - sag * 4 * t * (1 - t)
                kit2._rope(m, (1.56 * sx, y - 0.04, t * TILE), (1.5 * sx, 0.03, t * TILE), 0.0, C("rope", 0.92), segs=1, r=0.016)
        kit2._rope(m, (1.56 * sx, 0.36, 0.0), (1.56 * sx, 0.36, TILE), 0.1, C("rope"), segs=6, r=0.03)
    # lantern hook on the +X post: iron bracket reaching over the deck (lanterns hang here: env_lantern_hang)
    m.box(1.40, 0.70, 0.0, 1.45, 0.78, 0.04, C("iron"))
    m.box(1.30, 0.745, 0.0, 1.45, 0.78, 0.04, C("iron"))
    m.box(1.30, 0.69, 0.005, 1.32, 0.75, 0.035, C("iron"))


def bridge_post_v4(m, seed):
    c = C("wood_dark", 1.12)
    m.cbox(-0.07, -0.3, -0.07, 0.07, 0.72, 0.07, c, top=C("wood"), ch=0.03)
    m.cbox(-0.08, 0.72, -0.08, 0.08, 0.80, 0.08, C("wood_dark"), top=C("wood", 0.9), ch=0.03)
    for (y0, y1) in ((0.56, 0.64), (0.3, 0.37)):
        m.box(-0.078, y0, -0.078, 0.078, y1, 0.078, C("rope"))
    m.prism(0.0, -0.075, 0.03, 0.5, 0.58, C("rope", 0.86), n=4)


def bridge_pier_v4(m, seed):
    """Stacked mossy stones with moss drapes (p14 bridge piers), inside the v3 box."""
    rng = random.Random(seed)
    m.cbox(-0.32, -0.9, -0.32, 0.32, 0.12, 0.32, C("wall_b", 1.02), top=C("wall"), ch=0.06)
    m.push(M((0.0, 0.0, 0.0), yaw=rng.uniform(5, 9)))
    m.cbox(-0.25, 0.12, -0.25, 0.25, 0.5, 0.25, C("wall", rng.uniform(0.98, 1.05)), top=C("stone_pale"), ch=0.05)
    m.pop()
    m.cbox(-0.21, 0.5, -0.21, 0.21, 0.8, 0.21, C("wall_b"), top=C("moss"), ch=0.05)
    m.cbox(-0.22, 0.78, -0.22, 0.22, 0.86, 0.22, C("moss"), top=C("moss", 1.08), ch=0.03)
    for (x, z, n) in ((-0.33, 0.0, (-1, 0, 0)), (0.0, -0.33, (0, 0, -1)), (0.2, -0.33, (0, 0, -1))):
        L = rng.uniform(0.3, 0.6)
        if n[0]:
            f = F(m, [(x - 0.005, 0.12, z - 0.09), (x - 0.005, 0.12, z + 0.09), (x - 0.005, 0.12 - L, z + 0.02)], C("moss"), n)
        else:
            f = F(m, [(x - 0.09, 0.12, z - 0.005), (x + 0.09, 0.12, z - 0.005), (x + 0.02, 0.12 - L, z - 0.005)], C("moss"), n)
        grad(m, f, 0.12 - L, 0.12, C("grass_dark"), C("moss"))


def bridge_cell_v4(m, seed):
    rng = random.Random(seed)
    for k in range(3):
        z0 = -0.5 + k / 3
        col = C(["wood", "wood_b", "wood"][k], rng.uniform(0.94, 1.06))
        top = -rng.uniform(0.006, 0.02)
        m.cbox(-0.5, -0.09, z0 + 0.02, 0.5, top, z0 + 1 / 3 - 0.02, col, ch=0.03)
        nails(m, (-0.32, 0.32), z0 + 0.02, z0 + 1 / 3 - 0.02, top + 0.002)
    m.quad((-0.5, -0.095, -0.5), (-0.5, -0.095, 0.5), (0.5, -0.095, 0.5), (0.5, -0.095, -0.5), C("wood_dark"))
    m.box(-0.5, -0.2, -0.3, 0.5, -0.09, -0.15, C("wood_dark"))
    m.box(-0.5, -0.2, 0.15, 0.5, -0.09, 0.3, C("wood_dark"))


def bridge_anchor(m, seed):
    """Where a span meets land. Pivot: deck centre at the span end, local +Z onto the bridge (land is -Z); place like a
    span tile (scale w across). Timber sill flush with the deck, low threshold stones (0.2 u, step-over) on the land
    side, two anchor stakes outside the deck with rope ties to the rails."""
    rng = random.Random(seed)
    m.cbox(-1.62, -0.12, -0.22, 1.62, 0.05, 0.04, C("wood_dark", 1.15), top=C("wood_b"), ch=0.03)
    nails(m, (-1.15, -0.4, 0.4, 1.15), -0.22, 0.04, 0.052)
    for sx in (-1, 1):
        x = 1.62 * sx
        m.cbox(x - 0.1, -0.35, -0.36, x + 0.1, 0.62, -0.16, C("wood_dark", 1.1), top=C("wood"), ch=0.035)
        m.box(x - 0.105, 0.44, -0.365, x + 0.105, 0.52, -0.155, C("rope"))
        kit2._rope(m, (x, 0.48, -0.16), (1.56 * sx, 0.62, 0.0), 0.0, C("rope"), segs=1, r=0.035)
        for k in range(3):
            xx = sx * (1.05 + k * 0.24 + rng.uniform(-0.03, 0.03))
            stone(m, xx, -0.5 - rng.uniform(0, 0.12), rng.uniform(0.12, 0.16), rng.uniform(0.12, 0.2), C("wall", rng.uniform(0.96, 1.04)),
                  C("moss") if k == 1 else C("stone_pale"), rot=rng.uniform(0, 6), n=5)


# --------------------------------------------------------------------------------------- 5. set dressing
def fence(m, seed):
    """1 u fence along a rim (edge piece: pivot = rim edge start, local +X along, outward -Z), on the rock band."""
    rng = random.Random(seed)
    z = -0.2
    for x in (0.12, 0.88):
        tilt = rng.uniform(-4, 4)
        m.push(M((x, -0.12, z), roll=tilt))
        m.cbox(-0.05, -0.25, -0.05, 0.05, 0.62, 0.05, C("wood_b", rng.uniform(0.95, 1.05)), top=C("wood"), ch=0.02)
        m.prism(0, 0, 0.068, 0.62, 0.72, C("wood_dark", 1.15), n=4, r_top=0.0, rot=0.785)
        m.pop()
    m.push(M((0.5, 0.3, z - 0.06), roll=rng.uniform(-3, 3)))
    m.cbox(-0.5, -0.04, -0.025, 0.5, 0.04, 0.025, C("wood", 0.96), top=C("wood", 1.05), ch=0.015)
    m.pop()
    m.push(M((0.5, 0.06, z - 0.06), roll=rng.uniform(5, 9)))
    m.cbox(-0.48, -0.035, -0.025, 0.48, 0.035, 0.025, C("wood_b"), top=C("wood"), ch=0.015)
    m.pop()


def signpost(m, seed):
    stone(m, 0, 0, 0.22, 0.12, C("wall"), C("moss"), n=6)
    m.cbox(-0.06, 0.0, -0.06, 0.06, 1.45, 0.06, C("wood_b"), top=C("wood"), ch=0.02)
    m.prism(0, 0, 0.085, 1.45, 1.58, C("wood_dark", 1.15), n=4, r_top=0.0, rot=0.785)


def sign_board(m, seed, icon):
    """Arrow board pointing local +X (yaw it toward the target); pivot = post centre at the mount height."""
    x0, x1, h, t = 0.07, 0.8, 0.11, 0.028
    tipx = x1 + 0.13
    outline = [(x0, -h), (x1, -h), (tipx, 0.0), (x1, h), (x0, h)]
    wood = C("wood", 1.04)
    for s in (1, -1):
        F(m, [(x, y, -t * s) for x, y in outline], wood, (0, 0, -s))
    for i in range(5):
        a, b = outline[i], outline[(i + 1) % 5]
        F(m, [(a[0], a[1], -t), (b[0], b[1], -t), (b[0], b[1], t), (a[0], a[1], t)], C("wood_dark", 1.1),
          ((a[0] + b[0]) / 2 - 0.45, (a[1] + b[1]) / 2, 0))
    m.box(-0.075, -0.06, -0.075, 0.075, 0.06, 0.075, C("rope"))
    cx, cy = 0.44, 0.0
    if icon == "hill":       # crown
        ic = [(-0.12, -0.06), (0.12, -0.06), (0.13, 0.07), (0.06, 0.0), (0.0, 0.08), (-0.06, 0.0), (-0.13, 0.07)]
    elif icon == "shrine":   # egg
        ic = [(0.065 * math.cos(a), 0.085 * math.sin(a) + (0.01 if math.sin(a) > 0 else 0)) for a in [2 * math.pi * i / 10 for i in range(10)]]
    else:                    # base: shield
        ic = [(-0.08, 0.07), (0.08, 0.07), (0.08, 0.0), (0.0, -0.08), (-0.08, 0.0)]
    for s in (1, -1):
        F(m, [(cx + x * s, cy + y, -(t + 0.003) * s) for x, y in ic], C("cream"), (0, 0, -s))


def banner_worship(m, seed):
    """Team-neutral worship banner (old Kaiju cult): aged cream cloth, brown trim, navy Kaiju-eye emblem, tattered hem."""
    rng = random.Random(seed)
    stone(m, 0, 0, 0.2, 0.14, C("wall"), C("stone_pale"), n=6)
    m.prism(0, 0, 0.05, 0.0, 2.55, C("wood_dark", 1.12), n=6, r_top=0.04)
    m.cbox(-0.42, 2.3, -0.03, 0.42, 2.37, 0.03, C("wood_dark", 1.12), top=C("wood"), ch=0.015)
    m.prism(0, 0, 0.07, 2.55, 2.7, C("brass"), n=6, r_top=0.0)
    xs = [-0.38, -0.19, 0.0, 0.19, 0.38]
    zw = [0.0, 0.04, -0.02, 0.05, 0.0]
    ys_top, ys_mid = 2.3, 1.7
    hem = [1.12, 0.98, 1.16, 1.0, 1.14]
    for s in (1, -1):
        col = C("cloth_n") if s == 1 else C("cloth_n", 0.88)
        for i in range(4):
            a, b = i, i + 1
            F(m, [(xs[a], ys_top, zw[a] - 0.04 - 0.002 * s), (xs[b], ys_top, zw[b] - 0.04 - 0.002 * s),
                  (xs[b], ys_mid, zw[b] - 0.06), (xs[a], ys_mid, zw[a] - 0.06)], col, (0, 0, -s))
            F(m, [(xs[a], ys_mid, zw[a] - 0.06), (xs[b], ys_mid, zw[b] - 0.06), (xs[b], hem[b], zw[b] - 0.08),
                  ((xs[a] + xs[b]) / 2, hem[a] - 0.12 if i % 2 else hem[a] + 0.05, (zw[a] + zw[b]) / 2 - 0.07),
                  (xs[a], hem[a], zw[a] - 0.08)], col, (0, 0, -s))
        F(m, [(-0.38, 2.3, -0.045 * s - 0.04 * (s < 0)), (0.38, 2.3, -0.045 * s - 0.04 * (s < 0)),
              (0.38, 2.18, -0.05 * s - 0.04 * (s < 0)), (-0.38, 2.18, -0.05 * s - 0.04 * (s < 0))], C("cloth_trim"), (0, 0, -s))
    z = -0.105
    F(m, [(0.0, 1.96, z), (0.2, 1.72, z), (0.0, 1.48, z), (-0.2, 1.72, z)], C("emblem"), (0, 0, -1))
    F(m, [(0.0, 1.85, z - 0.004), (0.045, 1.72, z - 0.004), (0.0, 1.59, z - 0.004), (-0.045, 1.72, z - 0.004)], C("brass"), (0, 0, -1))


def torch(m, seed):
    m.prism(0, 0, 0.055, -0.02, 1.05, C("wood_dark", 1.15), n=5, r_top=0.045)
    m.prism(0, 0, 0.062, 0.72, 0.8, C("rope"), n=5)
    m.prism(0, 0, 0.09, 1.0, 1.14, C("iron"), n=6, r_top=0.16)
    m.prism(0, 0, 0.13, 1.1, 1.13, C("ember"), n=6)
    flame(m, 0.0, 1.11, 0.0)
    decal_at(m, 0, 0, 1.1, C("flame"))


def lantern_body(m, x, y, z):
    m.box(x - 0.008, y - 0.1, z - 0.008, x + 0.008, y, z + 0.008, C("iron"))
    m.prism(x, z, 0.1, y - 0.16, y - 0.1, C("iron"), n=4, r_top=0.02, rot=0.785)
    m.cbox(x - 0.065, y - 0.34, z - 0.065, x + 0.065, y - 0.16, z + 0.065, C("glass"), ch=0.01)
    for (dx, dz) in ((-0.07, -0.07), (0.07, -0.07), (0.07, 0.07), (-0.07, 0.07)):
        m.box(x + dx - 0.012, y - 0.34, z + dz - 0.012, x + dx + 0.012, y - 0.16, z + dz + 0.012, C("iron"))
    m.cbox(x - 0.085, y - 0.38, z - 0.085, x + 0.085, y - 0.34, z + 0.085, C("iron"), ch=0.01)


def lantern_post(m, seed):
    stone(m, 0, 0, 0.18, 0.1, C("wall"), C("stone_pale"), n=6)
    m.cbox(-0.06, 0.0, -0.06, 0.06, 1.55, 0.06, C("wood_b"), top=C("wood"), ch=0.02)
    m.cbox(-0.05, 1.42, -0.04, 0.46, 1.5, 0.04, C("wood_dark", 1.12), top=C("wood"), ch=0.015)
    lantern_body(m, 0.38, 1.42, 0.0)
    decal_at(m, 0.3, 0.0, 1.0, C("glass"))


def lantern_hang(m, seed):
    lantern_body(m, 0.0, 0.0, 0.0)


def campfire(m, seed):
    rng = random.Random(seed)
    for k in range(8):
        a = 2 * math.pi * k / 8 + rng.uniform(-0.12, 0.12)
        stone(m, math.cos(a) * 0.38, math.sin(a) * 0.38, rng.uniform(0.07, 0.095), rng.uniform(0.07, 0.12),
              C("rock", 1.08), C("stone_pale"), rot=rng.uniform(0, 6), n=5)
    m.prism(0, 0, 0.24, -0.005, 0.02, C("ember"), n=7)
    for k in range(3):
        a = 2 * math.pi * k / 3 + 0.4
        tube(m, [(math.cos(a) * 0.3, 0.04, math.sin(a) * 0.3), (math.cos(a) * 0.02, 0.15, math.sin(a) * 0.02)], 0.045,
             C("trunk"), n=5, col_end=C("socket"))
    flame(m, 0.0, 0.05, 0.0, s=0.7)
    decal_at(m, 0, 0, 1.0, C("flame"))


def camp_sacks(m, seed):
    rng = random.Random(seed)
    for (x, z, s) in ((-0.25, 0.05, 1.0), (-0.02, 0.2, 0.85)):
        m.blob(x, 0.12 * s, z, 0.17 * s, 0.13 * s, 0.15 * s, C("sack", rng.uniform(0.95, 1.04)), sub=1, smooth=False, seed=seed, jitter=0.08, flat_bottom=0.0)
        m.prism(x, z, 0.045 * s, 0.23 * s, 0.29 * s, C("rope"), n=5, r_top=0.03)
    tube(m, [(0.05, 0.09, -0.2), (0.6, 0.09, -0.2)], 0.09, C("cloth_trim"), n=6)
    for x in (0.18, 0.47):
        tube(m, [(x, 0.09, -0.2), (x + 0.04, 0.09, -0.2)], 0.096, C("rope"), n=6)
    m.cbox(0.25, 0.0, 0.05, 0.55, 0.27, 0.35, C("wood_b"), top=C("wood"), ch=0.025)
    m.box(0.24, 0.1, 0.04, 0.56, 0.14, 0.36, C("wood_dark"))


def woodpile(m, seed):
    rng = random.Random(seed)
    rows = [(-0.2, 0.0), (0.0, 0.0), (0.2, 0.0), (-0.1, 0.16), (0.1, 0.16), (0.0, 0.32)]
    for (z, y) in rows:
        L = rng.uniform(0.82, 0.95)
        dx = rng.uniform(-0.05, 0.05)
        rings = tube(m, [(-L / 2 + dx, y + 0.09, z), (L / 2 + dx, y + 0.09, z)], 0.088, C("trunk", rng.uniform(0.92, 1.05)), n=6)
        for ring, s in ((rings[0], -1), (rings[-1], 1)):
            F(m, ring, C("wood", 1.1), (s, 0, 0))
    for sx in (-0.5, 0.5):
        m.cbox(sx - 0.03, -0.1, -0.33, sx + 0.03, 0.5, -0.27, C("wood_dark", 1.1), ch=0.01)
        m.cbox(sx - 0.03, -0.1, 0.27, sx + 0.03, 0.5, 0.33, C("wood_dark", 1.1), ch=0.01)


def kaiju_statue(m, seed):
    """Weathered idol of the old Kaiju cult: stepped plinth with a carved glyph band, horned bust facing local -Z,
    crystal eyes (self-lit, neutral), moss, a crack."""
    rng = random.Random(seed)
    st, st2 = C("statue"), C("statue", 0.9)
    m.cbox(-0.55, 0.0, -0.55, 0.55, 0.32, 0.55, st2, top=st, ch=0.05)
    m.cbox(-0.44, 0.32, -0.44, 0.44, 0.62, 0.44, st, top=C("moss"), ch=0.05)
    if True:
        F(m, [(-0.3, 0.42, -0.445), (0.3, 0.42, -0.445), (0.3, 0.5, -0.445), (-0.3, 0.5, -0.445)], C("emblem"), (0, 0, -1))
    m.cbox(-0.24, 0.6, -0.22, 0.24, 1.05, 0.22, st2, top=st, ch=0.04)
    m.push(M((0.0, 0.0, 0.0), pitch=-6.0))
    m.cbox(-0.33, 1.0, -0.36, 0.33, 1.55, 0.3, st, top=C("statue", 1.08), ch=0.06)
    m.cbox(-0.23, 1.04, -0.72, 0.23, 1.32, -0.3, st, top=C("statue", 1.06), ch=0.05)
    m.cbox(-0.35, 1.36, -0.42, 0.35, 1.46, -0.3, st2, top=st, ch=0.03)
    for s in (-1, 1):
        F(m, [(s * 0.2, 1.27, -0.365), (s * 0.08, 1.27, -0.365), (s * 0.08, 1.34, -0.365), (s * 0.2, 1.34, -0.365)], C("crystal_n"), (0, 0, -1))
        for k in range(2):
            x = s * (0.07 + 0.09 * k)
            F(m, [(x - 0.03, 1.05, -0.722), (x + 0.03, 1.05, -0.722), (x, 0.97, -0.722)], C("bone"), (0, 0, -1))
        tube(m, [(s * 0.24, 1.5, 0.0), (s * 0.42, 1.82, 0.1), (s * 0.48, 2.12, 0.3), (s * 0.4, 2.32, 0.5)], 0.15, C("statue", 1.04),
             n=5, taper=0.3)
    F(m, [(-0.3, 1.556, -0.3), (0.25, 1.556, -0.3), (0.2, 1.556, 0.25), (-0.28, 1.556, 0.2)], C("moss"), UP)
    m.pop()
    F(m, [(0.1, 0.9, -0.225), (0.14, 0.9, -0.225), (0.05, 0.62, -0.225), (0.02, 0.62, -0.225)], C("socket"), (0, 0, -1))


def kaiju_skull(m, seed):
    rng = random.Random(seed)
    m.blob(0.0, 0.25, 0.1, 0.62, 0.62, 0.72, C("bone"), sub=2, smooth=False, seed=seed, jitter=0.05, flat_bottom=0.0)
    m.cbox(-0.27, 0.0, -1.15, 0.27, 0.34, -0.45, C("bone", 0.97), top=C("bone", 1.04), ch=0.06)
    for s in (-1, 1):
        F(m, [(s * 0.3 + 0.13 * math.cos(a), 0.55 + 0.1 * math.sin(a), -0.5 - 0.04 * math.cos(a) * s) for a in [2 * math.pi * i / 7 for i in range(7)]],
          C("socket"), (s * 0.2, 0.25, -1))
        for k in range(3):
            z = -1.08 + k * 0.17
            F(m, [(s * 0.275, 0.05, z), (s * 0.275, 0.05, z + 0.1), (s * 0.29, -0.02, z + 0.05)], C("bone_dark"), (s, -0.2, 0))
    tube(m, [(0.32, 0.72, 0.2), (0.55, 1.05, 0.35), (0.62, 1.35, 0.62)], 0.13, C("bone_dark", 1.1), n=5, taper=0.2)
    tube(m, [(-0.32, 0.72, 0.2), (-0.45, 0.9, 0.3)], 0.12, C("bone_dark", 1.1), n=5, taper=0.8)
    F(m, [(0.0, 0.88, 0.0), (0.12, 0.85, 0.25), (0.02, 0.8, 0.5)], C("socket"), (0, 1, 0))
    kit2.tuft(m, seed + 3, (0.55, 0, -0.3))
    kit2.tuft(m, seed + 4, (-0.6, 0, 0.5))


def bone_ribs(m, seed):
    rng = random.Random(seed)
    for k in range(5):
        z = -0.6 + k * 0.3
        stone(m, 0.0, z, 0.09, 0.12, C("bone_dark", 1.05), C("bone"), rot=0.3, n=5)
    for k in range(4):
        z = -0.45 + k * 0.3
        h = 1.05 - abs(k - 1.5) * 0.12
        tube(m, [(0.08, 0.0, z), (0.5, 0.45 * h, z + 0.03), (0.42, 0.9 * h, z + 0.08), (0.1, 1.05 * h, z + 0.12)], 0.055,
             C("bone"), n=4, taper=0.45)
    kit2.tuft(m, seed, (0.45, 0, -0.2))


def ruin_altar(m, seed):
    rng = random.Random(seed)
    m.cbox(-0.45, 0.0, -0.3, 0.45, 0.32, 0.3, C("wall_b"), top=C("wall"), ch=0.04)
    m.cbox(-0.5, 0.32, -0.36, 0.5, 0.42, 0.36, C("wall"), top=C("stone_pale"), ch=0.03)
    F(m, [(0.0, 0.424, -0.2), (0.18, 0.424, 0.0), (0.0, 0.424, 0.2), (-0.18, 0.424, 0.0)], C("emblem"), UP)
    m.prism(0.0, 0.0, 0.07, 0.42, 0.48, C("brass"), n=6, r_top=0.1)
    for (x, z, h) in ((-0.36, -0.22, 0.12), (0.35, -0.2, 0.08), (0.38, 0.22, 0.14)):
        m.prism(x, z, 0.03, 0.42, 0.42 + h, C("stem"), n=5)
        m.prism(x, z, 0.022, 0.42 + h, 0.42 + h + 0.06, C("flame"), n=4, r_top=0.0)
    F(m, [(-0.46, 0.25, -0.305), (-0.05, 0.25, -0.305), (-0.05, 0.29, -0.305), (-0.46, 0.29, -0.305)], C("moss"), (0, 0, -1))


def ruin_rubble(m, seed):
    rng = random.Random(seed)
    for k in range(5):
        a, r = 2 * math.pi * k / 5 + rng.uniform(-0.3, 0.3), rng.uniform(0.15, 0.5)
        s = rng.uniform(0.12, 0.2)
        h = rng.uniform(0.08, 0.22)
        m.push(M((math.cos(a) * r, 0.0, math.sin(a) * r), yaw=rng.uniform(0, 90), roll=rng.uniform(-6, 6)))
        m.cbox(-s, -0.03, -s * 0.8, s, h, s * 0.8, C("wall_b" if k % 2 else "wall"), top=C("moss") if k == 2 else C("stone_pale"), ch=0.03)
        m.pop()


# ------------------------------------------------------------------------------------------- painted finish adapter
def apply_finish(m, seed=606):
    """Run finish.painted_finish on a geo.Mesh: weld, derive a per-vertex kind from the colour name of its first loop,
    compute the factor on a helper vertex colour (white), multiply the loop colours by it, drop the helper layers."""
    bm = m.bm
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    kind = bm.verts.layers.int.new("fin_kind")
    fcol = bm.verts.layers.float_color.new("fin_col")
    lco = bm.verts.layers.float_vector.new("fin_lco")
    for v in bm.verts:
        if not v.link_loops:
            v[fcol] = (1, 1, 1, 0.75)
            continue
        c = v.link_loops[0][m.col]
        nm = REG.get(_key(c))
        kn = COLOUR_KIND.get(nm, "other")
        v[kind] = pfinish.KIND[kn]
        skip = abs(c[3] - 0.25) < 0.05 or c[3] > 0.45 or nm in ("ink", "band", "foam")
        v[fcol] = (1.0, 1.0, 1.0, 0.75 if skip else 0.0)
        v[lco] = Vector((v.co.y, v.co.x, v.co.z))
    stats = pfinish.painted_finish(bm, fcol, kind, lco=lco, seed=seed,
                                   params={"stone": {"edge": 0.14, "ao": 0.18}, "wood": {"grain": 0.14}})
    for f in bm.faces:
        for lp in f.loops:
            k = lp.vert[fcol]
            c = lp[m.col]
            if abs(c[3] - 0.25) < 0.05 or c[3] > 0.45:
                continue
            lp[m.col] = (c[0] * k[0], c[1] * k[1], c[2] * k[2], c[3])
    bm.verts.layers.int.remove(kind)
    bm.verts.layers.float_color.remove(fcol)
    bm.verts.layers.float_vector.remove(lco)
    return stats


def clamp_box(m, box):
    (x0, y0, z0), (x1, y1, z1) = box
    for v in m.bm.verts:          # Blender = (-ux, -uz, uy)
        ux, uy, uz = -v.co.x, v.co.z, -v.co.y
        ux, uy, uz = min(max(ux, x0), x1), min(max(uy, y0), y1), min(max(uz, z0), z1)
        v.co = Vector((-ux, -uz, uy))


def build_piece_v4(spec, collection=None):
    m = Mesh(spec["name"], cull="down")
    spec["fn"](m, spec["seed"])
    if spec.get("clamp"):
        clamp_box(m, spec["clamp"])
    if spec.get("finish"):
        apply_finish(m, seed=spec["seed"])
    return m.finish(collection)


# ------------------------------------------------------------------------------------------------- registry
CELL, EDGE = kit2.CELL, kit2.EDGE
SPAN_PIV = "deck centre of the near end; local +Z along the bridge, stretch to fit (same as env_bridge_span)"


def _n(name, fn, cat, pivot, covers, outline_px=0.0, mats=("CM_Toon",), seed=0, fin=False, far=False):
    d = kit2._p(name, fn, cat, pivot, covers, outline_px, mats, seed, far)
    d["finish"] = fin
    d["v4"] = "new"
    return d


NEW = [
    _n("env_ground_patch_light", lambda m, s: ground_patch(m, s, "light"), "ground_decal", CELL + "; any yaw (hash)", "v4 painted light grass patch, 2.6 x 2.1 u, flat", seed=701),
    _n("env_ground_patch_dark", lambda m, s: ground_patch(m, s, "dark"), "ground_decal", CELL + "; any yaw (hash)", "v4 painted dark grass patch, 3.2 x 2.0 u, flat", seed=702),
    _n("env_moss_patch", lambda m, s: ground_patch(m, s, "moss"), "ground_decal", CELL + "; any yaw", "v4 moss patch with 3 pebbles (under 0.1 u)", seed=703, fin=True),
    _n("env_dirt_patch", lambda m, s: ground_patch(m, s, "dirt"), "ground_decal", CELL + "; any yaw", "v4 worn dirt patch with pebbles and grass edge", seed=704, fin=True),
    _n("env_path_ruts", path_ruts, "ground_decal", CELL + "; local +Z along the route tangent", "v4 worn route shoulder: dirt, two ruts, edge stones (under 0.1 u)", seed=705, fin=True),
    _n("env_edge_stones", edge_stones, "small", "first stone end; local +X along the path edge", "v4 kerb stones (under 0.12 u)", outline_px=2.0, seed=706, fin=True),
    _n("env_puddle", puddle, "ground_decal", CELL + "; any yaw", "v4 puddle (self-lit water, flat)", seed=707),
    _n("env_grass_clump", grass_clump, "small", CELL + "; any yaw", "v4 grass clump, blades shaded base to tip (0.29 u)", seed=711),
    _n("env_flower_clump_a", lambda m, s: flower_clump(m, s, ("flower_y", "flower")), "small", CELL + "; any yaw", "v4 flower clump yellow/cream (0.25 u)", seed=712),
    _n("env_flower_clump_b", lambda m, s: flower_clump(m, s, ("flower_v", "flower")), "small", CELL + "; any yaw", "v4 flower clump lilac/cream (0.25 u)", seed=713),
    _n("env_moss_rocks", moss_rocks, "small", CELL + "; any yaw", "v4 three mossy stones (0.24 u)", outline_px=2.0, seed=714, fin=True),
    _n("env_mushrooms", mushrooms, "small", CELL + "; any yaw", "v4 mushroom cluster (0.27 u)", outline_px=2.0, seed=715),
    _n("env_reeds", reeds, "small_tall", "rim edge midpoint pushed 0.15 u out (non-walkable)", "v4 reeds with cattails by water (0.8 u)", seed=716),
    _n("env_bush_berry", bush_berry, "small_tall", CELL + " of a fill or past a lip (non-walkable)", "v4 bush with violet berries (0.45 u)", outline_px=2.5, mats=("CM_Toon_Foliage", "CM_Toon"), seed=717),
    _n("env_tree_oak", tree_oak, "tree", CELL + " (fill or edge, like edge trees)", "v4 broadleaf oak, layered canopy, root flare, 3.4 u", outline_px=2.5, mats=("CM_Toon_Foliage",), seed=721),
    _n("env_tree_pine", tree_pine, "tree", CELL + " (fill or edge)", "v4 conifer, 4 scalloped tiers, 3.6 u tall", outline_px=2.5, mats=("CM_Toon_Foliage",), seed=722),
    _n("env_tree_gnarl", tree_gnarl, "tree", CELL + " (fill or edge)", "v4 gnarled ancient tree, exposed roots, moss strands", outline_px=2.5, mats=("CM_Toon_Foliage",), seed=723),
    _n("env_float_chunk_s", lambda m, s: float_chunk(m, s, 0.55), "sky", "grass top centre", "v4 floating earth chunk 1.1 u under the islands", outline_px=2.0, seed=731, fin=True),
    _n("env_float_chunk_m", lambda m, s: float_chunk(m, s, 1.0), "sky", "grass top centre", "v4 floating earth chunk 2.0 u under the islands", outline_px=2.0, seed=732, fin=True),
    _n("env_bridge_span_damaged", lambda m, s: bridge_span_v4(m, s, True), "bridge", SPAN_PIV, "v4 damaged span tile (missing + snapped plank, snapped rope); same box as env_bridge_span", outline_px=2.0, seed=741, fin=True),
    _n("env_bridge_anchor", bridge_anchor, "bridge", "span end deck centre; local +Z onto the bridge; scale w across like a span", "v4 stone-and-timber bridge end anchor", outline_px=2.0, seed=742, fin=True),
    _n("env_lantern_hang", lantern_hang, "light", "hook point (span +X post bracket: local (1.31, 0.69, 0.02) of a span tile)", "v4 hanging lantern, self-lit glass", outline_px=2.0, seed=743),
    _n("env_fence", fence, "dress_tall", EDGE + " (rim edges only, on the rock band)", "v4 1 u timber fence on a rim", outline_px=2.0, seed=751, fin=True),
    _n("env_signpost", signpost, "dress_tall", CELL + " of a fill (non-walkable)", "v4 signpost (1.58 u)", outline_px=2.0, seed=752, fin=True),
    _n("env_sign_board_hill", lambda m, s: sign_board(m, s, "hill"), "dress_tall", "post centre at y 1.25; yaw = atan2(dz, dx) to the hill", "v4 arrow board, crown icon", outline_px=2.0, seed=753, fin=True),
    _n("env_sign_board_shrine", lambda m, s: sign_board(m, s, "shrine"), "dress_tall", "post centre at y 1.02; yaw to the shrine", "v4 arrow board, egg icon", outline_px=2.0, seed=754, fin=True),
    _n("env_sign_board_base", lambda m, s: sign_board(m, s, "base"), "dress_tall", "post centre at y 0.79; yaw to the nearer base", "v4 arrow board, shield icon", outline_px=2.0, seed=755, fin=True),
    _n("env_banner_worship", banner_worship, "dress_tall", CELL + " of a fill (non-walkable)", "v4 neutral Kaiju-cult banner (2.7 u)", outline_px=2.0, seed=756, fin=True),
    _n("env_torch", torch, "light", "rim edge midpoint pushed 0.18 u out, or a fill", "v4 torch, self-lit flame + glow decal", outline_px=2.0, mats=("CM_Toon", "CM_GlowDecal"), seed=761, fin=True),
    _n("env_lantern_post", lantern_post, "light", CELL + " of a fill (non-walkable)", "v4 lantern post, self-lit glass + glow decal", outline_px=2.0, mats=("CM_Toon", "CM_GlowDecal"), seed=762, fin=True),
    _n("env_campfire", campfire, "small", CELL + "; any yaw", "v4 campfire, stone ring, embers (0.29 u)", outline_px=2.0, mats=("CM_Toon", "CM_GlowDecal"), seed=763, fin=True),
    _n("env_camp_sacks", camp_sacks, "small", CELL + "; any yaw", "v4 sacks, bedroll, small crate (0.29 u)", outline_px=2.0, seed=764, fin=True),
    _n("env_woodpile", woodpile, "dress_tall", CELL + " of a fill or past a lip", "v4 log stack (0.5 u)", outline_px=2.0, seed=765, fin=True),
    _n("env_kaiju_statue", kaiju_statue, "landmark", CELL + " of a fill; yaw so local -Z faces the nearest route", "v4 old Kaiju-cult idol (2.4 u)", outline_px=2.0, seed=771, fin=True),
    _n("env_kaiju_skull", kaiju_skull, "landmark", CELL + " of a fill; any yaw", "v4 half-buried Kaiju skull (1.35 u)", outline_px=2.0, seed=772, fin=True),
    _n("env_bone_ribs", bone_ribs, "dress_tall", CELL + " of a fill; any yaw", "v4 Kaiju rib cage remains (1.1 u)", outline_px=2.0, seed=773, fin=True),
    _n("env_ruin_altar", ruin_altar, "dress_tall", CELL + " of a fill", "v4 offering altar, candles self-lit (0.56 u)", outline_px=2.0, seed=774, fin=True),
    _n("env_ruin_rubble", ruin_rubble, "small", CELL + "; any yaw", "v4 low rubble (under 0.25 u)", outline_px=2.0, seed=775, fin=True),
]

UPGRADES = {   # existing name -> (builder, finish, clamp box)
    "env_bridge_span": (bridge_span_v4, True, SPAN_BOX),
    "env_bridge_post": (bridge_post_v4, True, POST_BOX),
    "env_bridge_pier": (bridge_pier_v4, True, PIER_BOX),
    "env_bridge_cell": (bridge_cell_v4, True, CELL_BOX),
    "env_waterfall": (waterfall_v4, False, None),
}
_PATCHED = []


def add_palette():
    for k, v in NEW_COLOURS.items():
        palette.ENV.setdefault(k, v)
    for k in SELF_LIT:
        palette.MASK_FOR[k] = palette.MASK_FIXED_GLOW


def preflight():
    """Build every v4 builder once into a throwaway mesh (kit2 untouched); returns a list of error strings."""
    import traceback
    add_palette()
    jobs = [(sp["name"], sp["fn"], sp["finish"]) for sp in NEW]
    jobs += [(nm, fn, fin) for nm, (fn, fin, box) in UPGRADES.items()]
    jobs += [("cliff_s4", lambda m, s: cliff_skirt_v4(m, s, 4.0), True), ("cliff_s1", lambda m, s: cliff_skirt_v4(m, s, 1.0), True),
             ("rim_lip", lambda m, s: rim_lip_v4(m, s, 1.0), False)]
    errs = []
    for nm, fn, fin in jobs:
        m = Mesh("pf_" + nm, cull="down")
        try:
            fn(m, 5)
            if fin:
                apply_finish(m)
        except Exception:
            errs.append(nm + ": " + traceback.format_exc().strip().splitlines()[-1] + " @ " +
                        [l for l in traceback.format_exc().splitlines() if "kit4.py" in l][-1].strip())
        m.bm.free()
    return errs


def patch():
    """Apply v4 to kit2 in place (idempotent)."""
    if _PATCHED:
        return
    _PATCHED.append(True)
    for k, v in NEW_COLOURS.items():
        palette.ENV.setdefault(k, v)
    for k in SELF_LIT:
        palette.MASK_FOR[k] = palette.MASK_FIXED_GLOW
    kit2.C = C                                  # name tracking for the painted finish (same colours)
    kit2.cliff_skirt = cliff_skirt_v4           # PIECES lambdas look these up at call time
    kit2.rim_lip = rim_lip_v4
    kit2.waterfall = waterfall_v4               # render_v3.wf_mesh calls kit2.waterfall(foam_w=...)
    kit2.build_piece = build_piece_v4
    for nm, (fn, fin, box) in UPGRADES.items():
        sp = kit2.BY_NAME[nm]
        sp["fn"], sp["finish"], sp["clamp"], sp["v4"] = fn, fin, box, "upgraded"
    for sp in kit2.PIECES:
        if sp["name"].startswith("env_cliff_s") or sp["name"].startswith("env_cliff_d"):
            sp["finish"], sp["v4"] = True, "upgraded"
        if sp["name"].startswith("env_lip_rim_straight") or sp["name"].startswith("env_lip_rim_diag"):
            sp["v4"] = "upgraded"
    for sp in NEW:
        kit2.PIECES.append(sp)
        kit2.BY_NAME[sp["name"]] = sp
