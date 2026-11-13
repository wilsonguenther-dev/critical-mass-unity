"""Build the Healer (proposed sixth class, no bible page) from nothing.

  ~/bin/cm-heavy blender /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python-exit-code 1 \
      -P build_healer.py

Design: a field medic / shrine-keeper who heals with living biomass. Silver-white double buns with team ribbons and a
lime sprout, a cream shoulder mantle open at the front, a team stole with a gem brooch, a navy coat with a BELL SKIRT
(the only skirt of the six classes: an A-line silhouette against five pairs of trouser legs), a linen medic apron with
a lime leaf, bandage-wrapped forearms, a leather satchel of glowing lime sprouts on the right hip.
  Heal prop      Healer_BloomCan  (right hand): oversized brass watering can full of glowing lime biomass, sprouts
                                                growing out of it, rose on the spout
  Cleanse prop   Healer_Lantern   (left hand):  brass hand lantern on a short grip, warm cream-gold light (the opposite
                                                of corruption violet), team gem on the cap

Deterministic (factory startup, no randomness). Reuses UNCHANGED, read-only, from this branch:
  ../../Characters/_source~/cm_art     KayKit 41-joint rig loader (U-009), mesh kit, FBX export recipe
  ../../Miner/_source~/miner_art       mkit (painted value gradient, crystals, static finalize) and toon3 (CM/Toon
                                       preview); their palette module is swapped for healer_art.hpal at import, so the
                                       vertex-colour contract is the Miner's exactly ("Col", FLOAT_COLOR, POINT; alpha
                                       0 fixed / 1.0 team cloth / 0.75 team glow)
Outputs: healer.blend, ../Healer.fbx, ../HealerProps.fbx, ../HealerFace.png, build_report.json
Space: KayKit T-pose rest, Z up, faces -Y, character left = +X, metres; 2.0 u from boot soles to the sprout tip.
"""
import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ART = os.path.dirname(os.path.dirname(HERE))
SHARED = os.path.join(ART, "Characters", "_source~")
MINER = os.path.join(ART, "Miner", "_source~")
sys.dont_write_bytecode = True
for p in (HERE, MINER, SHARED):
    sys.path.insert(0, p)

from cm_art import export, kit, rig  # noqa: E402
from healer_art import face_decal, hpal  # noqa: E402
from miner_art import mkit, toon3  # noqa: E402

mkit.mpal = hpal          # same contract, Healer colours
toon3.mpal = hpal

OUT_BLEND = os.path.join(HERE, "healer.blend")
OUT_FBX = os.path.join(os.path.dirname(HERE), "Healer.fbx")
OUT_PROPS_FBX = os.path.join(os.path.dirname(HERE), "HealerProps.fbx")
OUT_REPORT = os.path.join(HERE, "build_report.json")
OUT_FACE = os.path.join(os.path.dirname(HERE), "HealerFace.png")
DISPLAY_ACTION, DISPLAY_FRAME = "Idle", 1

B = None
BODY, CAN, LANTERN = "Healer_Body", "Healer_BloomCan", "Healer_Lantern"
PROP_CAN, PROP_LANTERN = "Healer_BloomCan_Prop", "Healer_Lantern_Prop"
M = kit.mat_from
BV = 0.03
BV_MIN = 0.02
LEG_X = 0.19
TOP_Z = 2.0
BEVELS = {"boxes": 0, "bevelled": 0, "thin_exempt": 0}


def add(obj, bm, color, bones, feat, smooth=True, m=None, **kw):
    B.add(obj, bm, color, bones, feat=feat, smooth=smooth, m=m, **kw)


def box(size, bevel=BV, seg=1):
    BEVELS["boxes"] += 1
    thin = min(size)
    if thin < 2 * BV_MIN:
        BEVELS["thin_exempt"] += 1
        return B.box(size, min(bevel, thin * 0.45), seg)
    BEVELS["bevelled"] += 1
    return B.box(size, max(bevel, BV_MIN), seg)


def strap(a, b, width, thick, normal_hint, bevel=BV_MIN):
    a, b = Vector(a), Vector(b)
    return box((width, thick, (b - a).length), bevel, 1), kit.frame((a + b) / 2, b - a, normal_hint)


def interp(prof, z):
    """Radius of a (r, z) profile at height z (profile may run up or down)."""
    pts = sorted(prof, key=lambda rz: rz[1])
    if z <= pts[0][1]:
        return pts[0][0]
    for (r0, z0), (r1, z1) in zip(pts[:-1], pts[1:]):
        if z0 <= z <= z1:
            return r0 + (r1 - r0) * (z - z0) / max(z1 - z0, 1e-9)
    return pts[-1][0]


def shell_sector(prof, a0, a1, seg, thick, sy=1.0, off=0.0, cx=0.0):
    """Closed curved panel: the band of a surface of revolution between angles a0..a1 (degrees, -90 = front/-Y),
    from radius r+off to r+off+thick at each (r, z) row, Y scaled by sy. Used for the apron, mantle and hems."""
    bm = bmesh.new()
    outer, inner = [], []
    for r, z in prof:
        ro, ri = [], []
        for i in range(seg + 1):
            a = math.radians(a0 + (a1 - a0) * i / seg)
            c, s = math.cos(a), math.sin(a)
            ro.append(bm.verts.new((cx + (r + off + thick) * c, (r + off + thick) * s * sy, z)))
            ri.append(bm.verts.new((cx + (r + off) * c, (r + off) * s * sy, z)))
        outer.append(ro)
        inner.append(ri)
    n = len(prof)
    for j in range(n - 1):
        for i in range(seg):
            bm.faces.new((outer[j][i], outer[j][i + 1], outer[j + 1][i + 1], outer[j + 1][i]))
            bm.faces.new((inner[j][i], inner[j + 1][i], inner[j + 1][i + 1], inner[j][i + 1]))
    for j in (0, n - 1):
        for i in range(seg):
            bm.faces.new((outer[j][i], inner[j][i], inner[j][i + 1], outer[j][i + 1]))
    for i in (0, seg):
        for j in range(n - 1):
            bm.faces.new((outer[j][i], outer[j + 1][i], inner[j + 1][i], inner[j][i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def leaf_shape(L, W):
    """Pointed leaf outline in local XY, tip at +Y."""
    pts = []
    for k in range(9):
        t = k / 8
        pts.append((W * math.sin(math.pi * t) * (1 - 0.35 * t), L * t - L * 0.1))
    for k in range(7, 0, -1):
        t = k / 8
        pts.append((-W * math.sin(math.pi * t) * (1 - 0.35 * t), L * t - L * 0.1))
    return pts


def sprout(p, base, height, lean=(0, 0, 0), s=1.0, bud=True):
    """Biomass sprout: stem, two leaves at the top, glowing bud. p(bm, color, smooth, m, grad)."""
    base = Vector(base)
    tip = base + Vector((lean[0], lean[1], height))
    p(B.cyl(base, tip, 0.018 * s, 0.013 * s, 5), "leaf", True, None, True)
    d = (tip - base).normalized()
    for k, side in enumerate((1, -1)):
        out = Vector((side, 0.35 * side, 0.0)).normalized()
        axis = (out * 0.75 + d * 0.65).normalized()
        c = tip - d * 0.02 * s + axis * 0.05 * s
        p(B.sphere((0.042 * s, 0.075 * s, 0.012 * s), 7, 4), "lime", True,
          kit.frame(c, Vector((0, 0, 1)).cross(axis).normalized() if abs(axis.z) < 0.99 else (1, 0, 0), axis), False)
    if bud:
        p(B.sphere((0.032 * s, 0.032 * s, 0.04 * s), 7, 5), "lime_light", True, M(tuple(tip + d * 0.025 * s)), False)


# ===================================================================================================== body
def build_legs():
    for s, sd in ((1, "l"), (-1, "r")):
        lx = LEG_X * s
        up, lo, ft, to = f"upperleg.{sd}", f"lowerleg.{sd}", f"foot.{sd}", f"toes.{sd}"
        add(BODY, B.lathe([(0.09, 0.20), (0.11, 0.30), (0.125, 0.42), (0.12, 0.56)], 10), "navy_dark",
            ["hips", up, lo], "leggings", m=M((lx, 0.0, 0)))
        # short leather boots, rounded toe, fold-down cuff with a linen bandage wrap
        add(BODY, B.cyl((lx, 0.01, 0.05), (lx, 0.01, 0.25), 0.13, 0.12, 12), "leather", [lo, ft], "boots")
        add(BODY, B.torus(0.125, 0.035, 12, 4), "leather_dark", lo, "boots", m=M((lx, 0.01, 0.255)))
        add(BODY, B.torus(0.118, 0.022, 12, 3), "linen", lo, "bandage", m=M((lx, 0.01, 0.205), (6 * s, 0, 0)))
        add(BODY, box((0.25, 0.30, 0.13), 0.05, 2), "leather", [ft, to], "boots", m=M((lx + 0.01 * s, -0.06, 0.075)))
        add(BODY, B.sphere((0.125, 0.10, 0.075), 10, 6), "leather", to, "boots", m=M((lx + 0.01 * s, -0.20, 0.075)))
        add(BODY, box((0.26, 0.40, 0.04), 0.018, 1), "leather_dark", [ft, to], "boots", smooth=False,
            m=M((lx + 0.01 * s, -0.09, 0.02)))
        add(BODY, box((0.07, 0.03, 0.05), 0.012, 1), "brass", lo, "boots", smooth=False,
            m=M((lx + 0.105 * s, -0.04, 0.17), (0, 0, 75 * s)))


SKIRT = [(0.25, 0.835), (0.29, 0.77), (0.35, 0.655), (0.42, 0.525), (0.47, 0.42), (0.497, 0.36), (0.49, 0.33)]
SKIRT_SY = 0.86
TORSO = [(0.235, 0.78), (0.255, 0.86), (0.285, 0.97), (0.305, 1.06), (0.285, 1.16), (0.205, 1.235), (0.08, 1.27)]
TORSO_SY = 0.80


def build_torso():
    SK = ["hips", "upperleg.l", "upperleg.r"]
    add(BODY, B.lathe(list(reversed(SKIRT)), 20), "navy", SK, "skirt", m=M((0, 0, 0), (0, 0, 0), (1, SKIRT_SY, 1)))
    # cream hem band: the bell outline gets a light rim from every angle
    add(BODY, B.torus(0.492, 0.03, 20, 4), "cream", SK, "skirt_hem", m=M((0, 0, 0.345), (0, 0, 0), (1, SKIRT_SY, 1)))
    add(BODY, B.lathe(TORSO, 16), "navy", ["hips", "spine", "chest"], "bodice",
        m=M((0, 0, 0), (0, 0, 0), (1, TORSO_SY, 1)))
    # belt and buckle
    add(BODY, B.cyl((0, 0, 0.785), (0, 0, 0.865), 0.262, 0.262, 18), "leather", {"hips": 0.5, "spine": 0.5}, "belt",
        m=M((0, 0, 0), (0, 0, 0), (1, 0.84, 1)))
    add(BODY, box((0.10, 0.04, 0.085), 0.015, 1), "brass", {"hips": 0.5, "spine": 0.5}, "belt", smooth=False,
        m=M((0, -0.225, 0.825)))
    add(BODY, box((0.05, 0.03, 0.04), 0.0), "leather_dark", {"hips": 0.5, "spine": 0.5}, "belt_detail", smooth=False,
        m=M((0, -0.243, 0.825)))
    # linen medic apron: skirt panel and chest bib, leather pockets, lime leaf
    ap_rows = [(interp(SKIRT, z), z) for z in (0.80, 0.70, 0.60, 0.50, 0.44)]
    add(BODY, shell_sector(ap_rows, -90 - 46, -90 + 46, 8, 0.02, SKIRT_SY, 0.008), "linen", SK, "apron",
        smooth=True)
    bib_rows = [(interp(TORSO, z), z) for z in (0.87, 0.95, 1.03, 1.10)]
    add(BODY, shell_sector(bib_rows, -90 - 36, -90 + 36, 6, 0.02, TORSO_SY, 0.006), "linen", ["spine", "chest"],
        "apron", smooth=True)
    for s in (1, -1):
        z = 0.60
        r = interp(SKIRT, z)
        a = math.radians(-90 + 24 * s)
        pos = (r * math.cos(a) * 1.0, r * math.sin(a) * SKIRT_SY - 0.035, z)
        pm = M(pos, (-25, 0, 24 * s))
        add(BODY, box((0.13, 0.04, 0.11), 0.02, 1), "leather", SK, "apron_detail", smooth=False, m=pm)
        if s < 0:   # bandage roll peeking out of her right pocket
            add(BODY, B.cyl((-0.035, 0, 0.05), (0.035, 0, 0.05), 0.035, 0.035, 8), "linen", SK, "bandage", m=pm)
        else:       # small glass vial of lime biomass in the left pocket
            add(BODY, B.cyl((0, 0, 0.0), (0, 0, 0.10), 0.022, 0.022, 6), "lime", SK, "biomass", m=pm, grad=False)
            add(BODY, B.cyl((0, 0, 0.10), (0, 0, 0.125), 0.016, 0.016, 6), "wood", SK, "apron_detail", m=pm)
    z = 0.68
    lf = M((0, -(interp(SKIRT, z) + 0.028) * SKIRT_SY - 0.012, z), (90 - 28, 0, 0))
    add(BODY, B.prism(leaf_shape(0.19, 0.075), 0.024), "lime", SK, "biomass", smooth=False, m=lf, grad=False)
    add(BODY, B.prism(leaf_shape(0.14, 0.010), 0.034), "leaf", SK, "apron_detail", smooth=False, m=lf)
    # apron strings tied in a bow at the back waist (back read)
    W = {"hips": 0.5, "spine": 0.5}
    for s in (1, -1):
        add(BODY, B.sphere((0.075, 0.03, 0.05), 8, 5), "linen", W, "apron", m=M((0.07 * s, 0.245, 0.835), (0, 25 * s, 0)))
        add(BODY, B.spike((0.02 * s, 0.24, 0.81), (0.07 * s, 0.25, 0.66), 0.03, 5, (0.01 * s, 0.01, 0)), "linen", W,
            "apron")
    add(BODY, B.sphere((0.03, 0.03, 0.03), 6, 4), "linen", W, "apron", m=M((0, 0.25, 0.83)))
    # satchel of glowing sprouts on her right hip, hung from the belt
    SW = {"hips": 0.7, "spine": 0.3}
    SM = M((-0.33, -0.02, 0.69), (0, 0, 20))
    add(BODY, box((0.22, 0.13, 0.19), 0.035, 2), "leather", SW, "satchel", m=SM)
    add(BODY, box((0.235, 0.145, 0.07), 0.025, 1), "leather_dark", SW, "satchel", m=SM @ M((0, -0.004, 0.075)))
    add(BODY, box((0.045, 0.02, 0.05), 0.0), "brass", SW, "satchel_detail", smooth=False, m=SM @ M((0, -0.08, 0.05)))
    add(BODY, box((0.04, 0.03, 0.10), 0.0), "leather_dark", SW, "satchel_detail", smooth=False,
        m=SM @ M((0.07, 0.0, 0.14)))

    def sp(bm, color, smooth, m, grad):
        add(BODY, bm, color, SW, "biomass", smooth=smooth, m=m, grad=grad)
    for (dx, dy, h, lx, ly) in ((-0.06, 0.02, 0.17, -0.04, 0.0), (0.0, -0.02, 0.22, 0.0, -0.02),
                                (0.06, 0.03, 0.15, 0.04, 0.01)):
        b0 = SM @ Vector((dx, dy, 0.09))
        sprout(sp, tuple(b0), h, (lx, ly, 0), 1.0)


def build_arms():
    Z = 1.107
    for s, sd in ((1, "l"), (-1, "r")):
        ua, la, wr, hd = f"upperarm.{sd}", f"lowerarm.{sd}", f"wrist.{sd}", f"hand.{sd}"
        add(BODY, B.sphere((0.13, 0.13, 0.13), 8, 5), "navy", ["chest", ua], "sleeve", m=M((0.25 * s, 0, Z)))
        add(BODY, B.cyl((0.24 * s, 0, Z), (0.44 * s, 0.008, Z), 0.13, 0.12, 10), "navy", ["chest", ua, la], "sleeve")
        add(BODY, B.torus(0.12, 0.035, 10, 4), "cream", ua, "sleeve", m=M((0.445 * s, 0.008, Z), (0, 90, 0)))
        add(BODY, B.cyl((0.45 * s, 0.008, Z), (0.72 * s, 0, Z), 0.092, 0.085, 10), "skin", [ua, la, wr], "skin")
        for k, x in enumerate((0.52, 0.585, 0.65)):      # bandage wraps, tilted like a spiral
            add(BODY, B.torus(0.092, 0.02, 10, 3), "linen", [la, wr], "bandage",
                m=M((x * s, 0.004, Z), (0, 90, (12 if k % 2 else -12))))
        add(BODY, B.sphere((0.12, 0.105, 0.115), 9, 6), "skin", hd, "skin", m=M((0.85 * s, -0.01, 1.095)))
        add(BODY, B.sphere((0.045, 0.045, 0.055), 6, 4), "skin", hd, "skin", m=M((0.80 * s, -0.11, 1.12)))
        add(BODY, B.torus(0.10, 0.02, 10, 3), "linen", [wr, hd], "bandage", m=M((0.775 * s, -0.005, Z), (0, 90, 0)))


MANTLE = [(0.19, 1.285), (0.29, 1.25), (0.37, 1.18), (0.415, 1.09), (0.435, 0.985)]
MANTLE_SY = 0.84


def build_mantle():
    W = "chest"
    add(BODY, shell_sector(MANTLE, -90 + 42, 270 - 42, 24, 0.035, MANTLE_SY), "cream", W, "mantle")
    hem = [(0.432, 0.995), (0.44, 0.965)]
    add(BODY, shell_sector(hem, -90 + 40, 270 - 40, 24, 0.03, MANTLE_SY, 0.0), "linen", W, "mantle_detail")
    # lime leaf sigil on the back of the mantle
    z = 1.13
    lf = M((0, (interp(MANTLE, z) + 0.035) * MANTLE_SY + 0.012, z), (-90 + 30, 0, 180))
    add(BODY, B.prism(leaf_shape(0.23, 0.095), 0.024), "lime", W, "biomass", smooth=False, m=lf, grad=False)
    add(BODY, B.prism(leaf_shape(0.17, 0.011), 0.034), "leaf", W, "mantle_detail", smooth=False, m=lf)


def build_neck():
    add(BODY, B.cyl((0, 0, 1.17), (0, 0, 1.33), 0.088, 0.085, 8), "skin", ["chest", "head"], "skin")
    # team stole: collar roll over the mantle, two tails down the apron bib, gem brooch
    add(BODY, B.torus(0.215, 0.085, 16, 6), "kerchief", "chest", "team_cloth",
        m=M((0, 0.0, 1.285), (6, 0, 0), (1.0, 0.86, 0.75)))
    for s in (1, -1):
        pts = [(0.075 * s, -0.215, 1.25), (0.095 * s, -0.255, 1.12), (0.105 * s, -0.262, 1.00), (0.115 * s, -0.26, 0.90)]
        for a, b in zip(pts[:-1], pts[1:]):
            bm, mm = strap(a, b, 0.10, 0.035, (0, -1, 0.15))
            add(BODY, bm, "kerchief", ["chest", "spine"], "team_cloth", smooth=False, m=mm)
        add(BODY, box((0.11, 0.04, 0.03), 0.012, 1), "cream", ["chest", "spine"], "team_cloth", smooth=False,
            m=M((0.115 * s, -0.262, 0.905)))   # fringed end band
    add(BODY, B.lathe([(0.065, 0.0), (0.072, 0.02), (0.06, 0.035)], 6), "brass", "chest", "brooch", smooth=False,
        m=M((0, -0.245, 1.215), (90 - 10, 0, 0)))
    B.add_crystal(BODY, "chest", "team_glow", M((0, -0.255, 1.215), (90 - 10, 0, 30)), 0.055, 0.045, seg=6,
                  waist=0.8, cap=0.55)


HEAD_C, HEAD_R = (0.0, -0.01, 1.50), (0.30, 0.265, 0.285)


def head_front_y(x, z, proud=0.0):
    cx, cy, cz = HEAD_C
    rx, ry, rz = HEAD_R
    k = 1.0 - ((x - cx) / rx) ** 2 - ((z - cz) / rz) ** 2
    return cy - ry * math.sqrt(max(k, 0.02)) - proud


def face_plane(rows=4, cols=8):
    bm = bmesh.new()
    grid = []
    for j in range(rows + 1):
        z = face_decal.Z0 + (face_decal.Z1 - face_decal.Z0) * j / rows
        grid.append([bm.verts.new((x, head_front_y(x, z, 0.008), z)) for x in
                     (face_decal.X0 + (face_decal.X1 - face_decal.X0) * i / cols for i in range(cols + 1))])
    back = [[bm.verts.new(v.co + Vector((0, 0.004, 0))) for v in row] for row in grid]
    for j in range(rows):
        for i in range(cols):
            bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
            bm.faces.new((back[j][i], back[j + 1][i], back[j + 1][i + 1], back[j][i + 1]))
    ring = ([(0, i) for i in range(cols)] + [(j, cols) for j in range(rows)] +
            [(rows, i) for i in range(cols, 0, -1)] + [(j, 0) for j in range(rows, 0, -1)])
    for k, (j, i) in enumerate(ring):
        j2, i2 = ring[(k + 1) % len(ring)]
        bm.faces.new((grid[j][i], back[j][i], back[j2][i2], grid[j2][i2]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def build_head():
    H = "head"
    add(BODY, B.sphere(HEAD_R, 24, 16), "skin", H, "skin", m=M(HEAD_C))
    for s in (1, -1):
        add(BODY, B.sphere((0.035, 0.05, 0.065), 6, 4), "skin_shadow", H, "skin", m=M((0.295 * s, 0.02, 1.48)))
        # simple soft brows: thin arcs, silver like the hair (light on brown skin reads)
        brow = [(0.05, -0.006), (0.12, 0.016), (0.195, 0.004), (0.19, -0.012), (0.12, 0.0), (0.055, -0.02)]
        brow = [(x * s, z) for x, z in brow]
        if s < 0:
            brow = list(reversed(brow))
        add(BODY, B.prism(brow, 0.03, 0.006), "hair_shade", H, "face", smooth=False,
            m=M((0.0, head_front_y(0.12 * s, 1.598, 0.0) + 0.006, 1.598), (90 - 16, 0, -14 * s)))
    add(BODY, face_plane(), "skin", H, "face_decal", smooth=True, grad=False)
    add(BODY, B.sphere((0.04, 0.034, 0.032), 8, 6), "nose", H, "face", m=M((0, head_front_y(0, 1.432) - 0.004, 1.432)))
    zm = 1.345
    ym = head_front_y(0, zm) - 0.006
    smile = [(-0.062, 0.022), (-0.032, -0.014), (0.032, -0.014), (0.062, 0.022), (0.0, 0.004)]
    add(BODY, B.prism(smile, 0.03), "mouth", H, "face", smooth=False, m=M((0, ym, zm), (90 - 24, 0, 0)))
    teeth = [(-0.042, 0.011), (0.042, 0.011), (0.032, 0.002), (-0.032, 0.002)]
    add(BODY, B.prism(teeth, 0.03), "teeth", H, "face", smooth=False, m=M((0, ym - 0.005, zm + 0.001), (90 - 24, 0, 0)))


def lock(pts, widths, thick_k=0.62, seg=8, center=(0.0, 0.02, 1.52)):
    """Smooth hair lock: a flattened tube through pts (width across, thickness_k * width along the outward normal from
    the head centre) with a rounded end. Closed shell; start cap hidden inside the hair cap."""
    pts = [Vector(p) for p in pts]
    c = Vector(center)
    bm = bmesh.new()
    rings = []
    n = len(pts)
    for i, (p, w) in enumerate(zip(pts, widths)):
        t = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        nn = (p - c)
        nn = (nn - t * nn.dot(t)).normalized()
        b = t.cross(nn).normalized()
        rings.append([bm.verts.new(p + b * w * math.cos(2 * math.pi * k / seg) +
                                   nn * w * thick_k * math.sin(2 * math.pi * k / seg)) for k in range(seg)])
    t = (pts[-1] - pts[-2]).normalized()
    p, w = pts[-1], widths[-1]
    nn = (p - c)
    nn = (nn - t * nn.dot(t)).normalized()
    b = t.cross(nn).normalized()
    rings.append([bm.verts.new(p + t * w * 0.55 + b * w * 0.72 * math.cos(2 * math.pi * k / seg) +
                               nn * w * 0.72 * thick_k * math.sin(2 * math.pi * k / seg)) for k in range(seg)])
    tip = bm.verts.new(p + t * w * 0.95)
    for j in range(len(rings) - 1):
        for k in range(seg):
            q = (k + 1) % seg
            bm.faces.new((rings[j][k], rings[j][q], rings[j + 1][q], rings[j + 1][k]))
    for k in range(seg):
        bm.faces.new((rings[-1][k], rings[-1][(k + 1) % seg], tip))
    bm.faces.new(list(reversed(rings[0])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def build_hair():
    H = "head"
    add(BODY, B.sphere((0.322, 0.292, 0.282), 20, 12), "hair", H, "hair", m=M((0, 0.062, 1.56)))
    # v1.1 fringe: three smooth swept locks from a side part on her left, sweeping to her right, rounded ends
    add(BODY, lock([(0.11, -0.10, 1.82), (0.02, -0.235, 1.75), (-0.10, -0.282, 1.675), (-0.20, -0.262, 1.635)],
                   [0.10, 0.095, 0.075, 0.05]), "hair", H, "hair")
    add(BODY, lock([(0.12, -0.12, 1.81), (0.08, -0.255, 1.72), (0.045, -0.288, 1.66)], [0.075, 0.07, 0.05]), "hair",
        H, "hair")
    add(BODY, lock([(0.17, -0.10, 1.79), (0.215, -0.225, 1.70), (0.235, -0.25, 1.645)], [0.075, 0.065, 0.045]),
        "hair", H, "hair")
    # side locks framing the face, in front of the ears, ends curling out a little
    for s in (1, -1):
        add(BODY, lock([(0.24 * s, -0.04, 1.72), (0.285 * s, -0.135, 1.56), (0.28 * s, -0.17, 1.42),
                        (0.30 * s, -0.16, 1.32)], [0.075, 0.07, 0.06, 0.045]), "hair", H, "hair")
    # short braid down the back with a team ribbon tie (back read)
    for k, (z, r) in enumerate(((1.47, 0.085), (1.39, 0.078), (1.315, 0.07), (1.245, 0.06))):
        add(BODY, B.sphere((r * 1.05, r * 0.85, r), 10, 6), "hair", H, "hair",
            m=M((0.012 * (1 if k % 2 else -1), 0.31 + 0.02 * k, z), (0, 18 * (1 if k % 2 else -1), 0)))
    add(BODY, B.torus(0.045, 0.02, 10, 4), "kerchief", H, "team_cloth", m=M((0, 0.37, 1.205), (12, 0, 0)))
    add(BODY, lock([(0, 0.37, 1.20), (0, 0.39, 1.13), (0.0, 0.395, 1.08)], [0.05, 0.045, 0.03], center=(0, 0, 1.2)),
        "hair", H, "hair")
    # double buns with team ribbons
    for s in (1, -1):
        c = Vector((0.205 * s, 0.05, 1.795))
        d = (c - Vector((0, 0.02, 1.52))).normalized()
        add(BODY, B.sphere((0.135, 0.135, 0.135), 14, 9), "hair", H, "hair", m=M(tuple(c)))
        add(BODY, B.torus(0.105, 0.03, 12, 4), "kerchief", H, "team_cloth",
            m=kit.frame(tuple(c - d * 0.075), d, (0, 1, 0)))
        add(BODY, B.spike(tuple(c - d * 0.08 + Vector((0.03 * s, 0.09, -0.02))),
                          tuple(c - d * 0.08 + Vector((0.07 * s, 0.17, -0.16))), 0.035, 5, (0.02 * s, 0.01, 0)),
            "kerchief", H, "team_cloth")   # ribbon tail
    # lime sprout tucked into her left bun; tip lands exactly on TOP_Z

    def sp(bm, color, smooth, m, grad):
        add(BODY, bm, color, H, "biomass", smooth=smooth, m=m, grad=grad)
    base = Vector((0.24, 0.02, 1.88))
    sprout(sp, tuple(base), TOP_Z - base.z - 0.075, (0.03, -0.01, 0), 1.0)


# ===================================================================================================== held props
def held_matrix(poses, slot, origin, z_axis, y_hint):
    rest = B.arm.data.bones[slot].matrix_local
    return rest @ poses[slot].inverted() @ kit.frame(origin, z_axis, y_hint)


def can_parts(p):
    """Bloom can (heal), item space: grip on the rear handle at the origin, +Z up, spout toward -Y."""
    C = Vector((0, -0.30, 0))
    prof = [(0.13, -0.17), (0.172, -0.15), (0.182, -0.03), (0.17, 0.05), (0.13, 0.095), (0.115, 0.105)]
    p(B.lathe(prof, 14), "brass", True, M(tuple(C)), True)
    for z in (-0.13, 0.03):
        p(B.torus(0.182, 0.018, 14, 3), "brass_dark", True, M(tuple(C + Vector((0, 0, z)))), False)
    p(B.lathe([(0.14, 0.0), (0.135, 0.02)], 14), "brass_dark", False, M(tuple(C + Vector((0, 0, -0.175)))), False)
    # rear handle (wood grip in a brass loop) and top handle
    p(B.torus(0.11, 0.026, 12, 4), "brass", True, M((0, -0.115, 0.0), (0, 90, 0)), True)
    p(B.cyl((0, -0.03, -0.06), (0, -0.03, 0.06), 0.034, 0.034, 8), "wood", True, None, True)
    # glowing biomass liquid at the mouth, sprouts growing out of it
    p(B.cyl(tuple(C + Vector((0, 0, 0.09))), tuple(C + Vector((0, 0, 0.115))), 0.105, 0.105, 12), "lime_light", True,
      None, False)
    # spout and rose
    a, b = C + Vector((0, -0.14, -0.08)), C + Vector((0, -0.37, 0.15))
    p(B.cyl(tuple(a), tuple(b), 0.05, 0.03, 8), "brass", True, None, True)
    d = (b - a).normalized()
    p(B.cyl(tuple(b - d * 0.01), tuple(b + d * 0.06), 0.045, 0.075, 10), "brass_dark", False, None, False)
    p(B.cyl(tuple(b + d * 0.06), tuple(b + d * 0.075), 0.068, 0.068, 10), "lime_light", False, None, False)
    return C


def build_can(obj, bones, T, gem=True):
    def p(bm, color, smooth, m, grad):
        B.add(obj, bm, color, bones, feat="bloom_can" if color not in ("lime", "lime_light", "leaf") else "biomass",
              smooth=smooth, m=m, grad=grad, post=T)
    C = can_parts(p)
    for (dx, dy, h, lx, ly) in ((-0.045, 0.02, 0.17, -0.05, 0.02), (0.03, -0.03, 0.22, 0.02, -0.04),
                                (0.05, 0.045, 0.14, 0.05, 0.03)):
        sprout(p, tuple(C + Vector((dx, dy, 0.10))), h, (lx, ly, 0), 1.05)
    if gem:
        for s in (1, -1):
            B.add_crystal(obj, bones, "team_glow", kit.frame(tuple(C + Vector((0.178 * s, 0, -0.05))), (s, 0, 0),
                                                             (0, 0, 1)), 0.045, 0.035, seg=6, waist=0.8, cap=0.55,
                          post=T)


def build_lantern(obj, bones, T):
    """Cleansing lantern, item space: grip at the origin, +Z up, lantern body above the fist."""
    def p(bm, color, feat="lantern", smooth=False, m=None, grad=True):
        B.add(obj, bm, color, bones, feat=feat, smooth=smooth, m=m, grad=grad, post=T)
    p(B.cyl((0, 0, -0.09), (0, 0, 0.13), 0.034, 0.036, 8), "wood", smooth=True)
    p(B.sphere((0.05, 0.05, 0.045), 8, 5), "brass", smooth=True, m=M((0, 0, -0.10)))
    p(B.lathe([(0.04, 0.10), (0.14, 0.13), (0.145, 0.17), (0.12, 0.18)], 6), "brass", m=M((0, 0, 0), (0, 0, 30)))
    p(B.lathe([(0.105, 0.175), (0.12, 0.25), (0.115, 0.36), (0.095, 0.40)], 6), "lamp_glass", feat="lantern_light",
      m=M((0, 0, 0), (0, 0, 30)), grad=False)
    for k in range(6):
        a = math.radians(30 + 60 * k + 30)
        r0, r1 = 0.125, 0.11
        bm, mm = strap((r0 * math.cos(a), r0 * math.sin(a), 0.175), (r1 * math.cos(a), r1 * math.sin(a), 0.405), 0.03,
                       0.03, (math.cos(a), math.sin(a), 0), bevel=0.008)
        p(bm, "brass_dark", feat="lantern_detail", m=mm)
    p(B.lathe([(0.135, 0.395), (0.13, 0.43), (0.085, 0.48), (0.035, 0.51)], 6), "brass", m=M((0, 0, 0), (0, 0, 30)))
    p(B.torus(0.055, 0.016, 10, 4), "brass_dark", m=M((0, 0, 0.55), (90, 0, 0)))
    B.add_crystal(obj, bones, "team_glow", M((0, 0, 0.49), (0, 0, 30)), 0.05, 0.075, seg=6, waist=0.85, cap=0.45,
                  post=T)


# ===================================================================================================== main
def dims(ob):
    vs = [ob.matrix_world @ v.co for v in ob.data.vertices]
    return {"min": [round(min(v[i] for v in vs), 3) for i in range(3)],
            "max": [round(max(v[i] for v in vs), 3) for i in range(3)]}


def tris(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def assign_face_decal(ob, image):
    me = ob.data
    gi = ob.vertex_groups["feat_face_decal"].index
    vin = {v.index for v in me.vertices if any(g.group == gi for g in v.groups)}
    uv = me.uv_layers.new(name="UVMap")
    xs = [me.vertices[i].co.x for i in vin]
    zs = [me.vertices[i].co.z for i in vin]
    x0, x1, z0, z1 = min(xs), max(xs), min(zs), max(zs)
    mat = toon3.make_decal_material("CM_FaceDecal", image)
    me.materials.append(mat)
    slot = len(me.materials) - 1
    n = 0
    hc = Vector(HEAD_C)
    for poly in me.polygons:
        if not all(i in vin for i in poly.vertices):
            for li in poly.loop_indices:
                uv.data[li].uv = (0.0, 0.0)
            continue
        # v1.1: every face of the face slab takes the decal material; the back sheet and rim map to the transparent
        # corner texel, so the head shows through (v1 drew the back sheet in flat skin: a visible rectangle)
        front = poly.normal.dot((poly.center - hc).normalized()) > 0.5
        poly.material_index = slot
        n += int(front)
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uv.data[li].uv = ((co.x - x0) / (x1 - x0), (co.z - z0) / (z1 - z0)) if front else (0.002, 0.002)
    return n


def main():
    global B
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version = 0
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    arm = rig.load_kaykit_rig()
    poses = rig.pose_matrices(arm, DISPLAY_ACTION, DISPLAY_FRAME)
    mat = toon3.make_material("CM_Toon")
    print("IDLE_SLOTS", {k: [round(x, 3) for x in poses[k].translation] for k in ("handslot.r", "handslot.l")})

    B = mkit.MinerBuilder(arm)
    build_legs()
    build_torso()
    build_arms()
    build_mantle()
    build_neck()
    build_head()
    build_hair()
    o_r = poses["handslot.r"].translation.copy()
    o_l = poses["handslot.l"].translation.copy()
    build_can(CAN, "handslot.r", held_matrix(poses, "handslot.r", o_r, (0, 0, 1), (0, 1, 0)))
    build_lantern(LANTERN, "handslot.l", held_matrix(poses, "handslot.l", o_l, (0, 0, 1), (0, 1, 0)))
    coll = bpy.data.collections.new("Healer")
    scene.collection.children.link(coll)
    for c in list(arm.users_collection):
        c.objects.unlink(arm)
    coll.objects.link(arm)
    objs = B.finalize(coll, mat)
    face_img = face_decal.save_png_blender(OUT_FACE)
    n_face = assign_face_decal(objs[BODY], face_img)
    TRI_FEAT = dict(sorted(B.tri_feat.items(), key=lambda kv: -kv[1]))

    # static prop versions (separate FBX): origin on the ground at the base centre
    B = mkit.MinerBuilder(None)
    B.arm = None
    build_can(PROP_CAN, None, M((-0.45, 0.0, 0.18)))
    build_lantern(PROP_LANTERN, None, M((0.45, 0.0, 0.15)))
    pcoll = bpy.data.collections.new("HealerProps")
    scene.collection.children.link(pcoll)
    pobjs = B.finalize_static(pcoll, mat)
    for ob in pobjs.values():
        ob.hide_render = True
        ob.hide_set(True)

    report = {"objects": {}, "bones": len(arm.data.bones), "display_pose": [DISPLAY_ACTION, DISPLAY_FRAME],
              "face_decal": {"png": os.path.basename(OUT_FACE), "size": face_decal.N, "faces": n_face},
              "idle_handslots": {k: [round(x, 3) for x in poses[k].translation] for k in ("handslot.r", "handslot.l")}}
    for name, ob in list(objs.items()) + list(pobjs.items()):
        report["objects"][name] = {
            "triangles": tris(ob), "vertices": len(ob.data.vertices), "rest_bounds": dims(ob),
            "materials": [m.name for m in ob.data.materials],
            "feature_groups": sorted(g.name for g in ob.vertex_groups if g.name.startswith("feat_")),
        }
    report["bevels"] = BEVELS
    report["triangles_by_feature"] = TRI_FEAT
    report["triangles_character"] = sum(tris(objs[n]) for n in (BODY, CAN, LANTERN))
    report["triangles_props"] = {n: tris(pobjs[n]) for n in (PROP_CAN, PROP_LANTERN)}

    arm.animation_data_create()
    arm.animation_data.action = bpy.data.actions[DISPLAY_ACTION]
    scene.frame_set(DISPLAY_FRAME)
    bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND, compress=True)

    export.export_fbx(OUT_FBX, arm, [objs[BODY], objs[CAN], objs[LANTERN]])
    for ob in bpy.context.view_layer.objects:
        ob.select_set(False)
    for ob in pobjs.values():
        ob.hide_set(False)
        ob.select_set(True)
    bpy.context.view_layer.objects.active = pobjs[PROP_CAN]
    opts = dict(export.FBX_OPTS)
    opts["object_types"] = {"MESH"}
    res = bpy.ops.export_scene.fbx(filepath=OUT_PROPS_FBX, **opts)
    if "FINISHED" not in res:
        raise SystemExit("props FBX export failed")
    with open(OUT_REPORT, "w") as f:
        json.dump(report, f, indent=1)
        f.write("\n")
    print("BUILD_REPORT " + json.dumps({k: v for k, v in report.items() if k not in ("objects", "triangles_by_feature")}))
    for n, o in report["objects"].items():
        print("OBJ", n, o["triangles"], o["rest_bounds"])


main()
