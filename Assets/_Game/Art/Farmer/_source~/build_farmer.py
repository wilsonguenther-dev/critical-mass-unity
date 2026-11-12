"""Build the Farmer (bible p07, CM-CHR-002) from nothing: farmer.blend + ../Farmer.fbx.

  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python-exit-code 1 -P build_farmer.py

Reuses, by import and unmodified, the Warrior branch's shared kit in ../../Characters/_source~/cm_art (KayKit 41-joint
rig loader, mesh kit, FBX export settings, palette). Farmer-only additions live in farmer_art/.

Modelling space: Blender rest pose (KayKit T-pose), Z up, faces -Y, character left = +X, metres. The bible's staff hand
is the character's RIGHT (-X); the globe tank rides behind the RIGHT shoulder. Held item (staff) is placed where the
bible shows it in the KayKit "Idle" pose and mapped back to rest through handslot.r, so it follows every clip.
Deterministic: factory startup, no randomness, no wall clock.
"""
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
SHARED = os.path.normpath(os.path.join(HERE, "..", "..", "Characters", "_source~"))
sys.dont_write_bytecode = True
sys.path.insert(0, SHARED)
sys.path.insert(0, HERE)

from cm_art import export, kit, rig  # noqa: E402
from farmer_art import fkit, toon3  # noqa: E402
from farmer_art.fkit import bezier  # noqa: E402

OUT_BLEND = os.path.join(HERE, "farmer.blend")
OUT_FBX = os.path.join(os.path.dirname(HERE), "Farmer.fbx")
OUT_REPORT = os.path.join(HERE, "build_report.json")
DISPLAY_ACTION, DISPLAY_FRAME = "Idle", 1
BODY_H = 2.0
STAFF_LEN = 0.972 * BODY_H            # bible p07 measured ratio

B = None
BODY, STAFF = "Farmer_Body", "Farmer_Staff"
M = kit.mat_from
TANK_C = Vector((-0.06, 0.41, 1.19))  # globe centre (behind the right shoulder, riding above the shoulder line)
TANK_R = 0.225                        # 0.45 u diameter (ART-DIRECTION 3.1)


def add(obj, bm, color, bones, feat, smooth=True, m=None, grad=True):
    B.add(obj, bm, color, bones, feat=feat, smooth=smooth, m=m, grad=grad)


def rot_cols(x, y, z, t=(0, 0, 0)):
    """Matrix from column vectors (local X, Y, Z -> world) plus translation."""
    m = Matrix((x, y, z)).transposed().to_4x4()
    m.translation = Vector(t)
    return m


# ===================================================================================================== legs / boots
def build_legs():
    for s, sd in ((1, "l"), (-1, "r")):
        lx = 0.178 * s
        up, lo, ft, to = f"upperleg.{sd}", f"lowerleg.{sd}", f"foot.{sd}", f"toes.{sd}"
        # navy baggy overall legs, one lathe from the boot cuff to the hip
        prof = [(0.125, 0.20), (0.150, 0.26), (0.165, 0.36), (0.165, 0.47), (0.152, 0.60)]
        add(BODY, B.lathe(prof, 12), "f_navy", ["hips", up, lo], "overalls", m=M((lx, 0.0, 0)))
        # grey knee patch (bevelled), stitched look via a darker groove frame
        add(BODY, B.box((0.15, 0.06, 0.14), 0.03, 1), "f_patch", [up, lo], "overalls", smooth=False,
            m=M((lx * 1.02, -0.142, 0.315), (-6, 0, 4 * s)))
        # boot: shaft, teal trim, folded cream cuff, chunky foot, dark sole, strap + brass buckle
        add(BODY, B.bcyl((lx, 0.02, 0.06), (lx, 0.02, 0.215), 0.138, 0.135, 12, 0.02), "f_boot", [lo, ft], "boots")
        add(BODY, B.torus(0.142, 0.022, 12, 4), "f_team_cloth", lo, "team_cloth", m=M((lx, 0.02, 0.205)))
        add(BODY, B.bcyl((lx, 0.02, 0.215), (lx, 0.02, 0.29), 0.158, 0.165, 12, 0.025), "f_cream", lo, "boots")
        add(BODY, B.box((0.25, 0.37, 0.13), 0.04, 2), "f_boot", [ft, to], "boots", m=M((lx, -0.075, 0.075)))
        add(BODY, B.box((0.265, 0.395, 0.045), 0.015, 1), "f_sole", [ft, to], "boots", smooth=False,
            m=M((lx, -0.08, 0.022)))
        add(BODY, B.box((0.05, 0.03, 0.045), 0.012, 1), "f_brass", [lo, ft], "boots", smooth=False,
            m=M((lx + 0.137 * s, 0.0, 0.12)))


# ===================================================================================================== torso
def build_torso():
    add(BODY, B.sphere((0.27, 0.20, 0.17), 14, 8), "f_navy", ["hips", "spine"], "overalls", m=M((0, 0.0, 0.62)))
    add(BODY, B.sphere((0.25, 0.185, 0.27), 14, 8), "f_navy", ["spine", "chest"], "overalls", m=M((0, 0.0, 0.92)))
    # cream short work jacket (open front), collar and lapels
    add(BODY, B.sphere((0.27, 0.215, 0.255), 14, 9), "f_cream", ["spine", "chest"], "jacket", m=M((0, 0.02, 1.02)))
    add(BODY, B.box((0.20, 0.07, 0.30), 0.03, 1), "f_navy", ["spine", "chest"], "overalls", m=M((0, -0.180, 0.95)))
    add(BODY, B.box((0.12, 0.02, 0.07), 0.01, 1), "f_navy_dark", "chest", "overalls", smooth=False,
        m=M((0, -0.217, 0.98)))
    for s in (1, -1):
        lapel = [(0.085 * s, 1.20), (0.21 * s, 1.12), (0.12 * s, 0.86), (0.09 * s, 0.86)]
        if s < 0:
            lapel = list(reversed(lapel))
        add(BODY, B.prism(lapel, 0.04, bevel=0.012), "f_cream", "chest", "jacket", smooth=False,
            m=M((0, -0.205, 0), (90, 0, 0)) @ M((0, 0, 0), (0, 0, 0)))
        add(BODY, B.bcyl((0.075 * s, -0.215, 1.04), (0.075 * s, -0.235, 1.04), 0.022, 0.022, 10, 0.006), "f_brass",
            "chest", "overalls", smooth=False)
    add(BODY, B.torus(0.15, 0.055, 12, 4), "f_cream", "chest", "jacket",
        m=M((0, 0.01, 1.205), (-14, 0, 0), (1.12, 1.0, 0.85)))
    # neck
    add(BODY, B.cyl((0, 0, 1.17), (0, 0, 1.34), 0.072, 0.07, 10), "f_skin", ["chest", "head"], "skin")
    # tank shoulder straps (flat dark-brown bands over the jacket)
    for s in (1, -1):
        path = [(0.14 * s, 0.25, 1.27), (0.16 * s, 0.06, 1.305), (0.16 * s, -0.15, 1.19), (0.145 * s, -0.215, 1.00),
                (0.14 * s, -0.205, 0.80)]
        add(BODY, B.tube(path, 0.03, 6, 0.4, up=(0, -1, 0)), "f_brown", ["chest", "spine"], "belt")
    # belt, buckle, pouches
    add(BODY, B.bcyl((0, 0, 0.71), (0, 0, 0.79), 0.272, 0.278, 16, 0.015), "f_brown", {"hips": 0.5, "spine": 0.5},
        "belt", m=M((0, 0, 0), (0, 0, 0), (1, 0.80, 1)))
    add(BODY, B.box((0.115, 0.035, 0.09), 0.015, 1), "f_brass", {"hips": 0.5, "spine": 0.5}, "belt", smooth=False,
        m=M((0, -0.226, 0.75)))
    add(BODY, B.box((0.06, 0.02, 0.045), 0.006, 1), "f_brown", {"hips": 0.5, "spine": 0.5}, "belt", smooth=False,
        m=M((0, -0.243, 0.75)))
    for (x, y, z, rz) in ((0.20, -0.165, 0.665, -32), (-0.255, -0.04, 0.665, 72), (-0.10, 0.205, 0.68, 172)):
        w = {"hips": 0.7, "spine": 0.3}
        add(BODY, B.box((0.12, 0.08, 0.13), 0.025, 1), "f_leather", w, "belt", m=M((x, y, z), (0, 0, rz)))
        add(BODY, B.box((0.125, 0.085, 0.045), 0.012, 1), "f_brown", w, "belt", smooth=False,
            m=M((x, y, z + 0.05), (0, 0, rz)))
    # root-seed pod carried on the left hip: round wooden pod, dark seams, lime leaves, lime glow crack
    P = Vector((0.285, 0.03, 0.60))
    w = {"hips": 1.0}
    add(BODY, B.sphere((0.075, 0.075, 0.072), 10, 7), "f_wood", w, "seed_pod", m=M(P))
    for rz in (0, 90):
        add(BODY, B.torus(0.074, 0.008, 10, 3), "f_wood_dark", w, "seed_pod", m=M(P, (90, 0, rz)))
    add(BODY, B.sphere((0.03, 0.02, 0.03), 6, 4), "f_biomass", w, "seed_pod", m=M(P + Vector((0.035, -0.055, 0.0))))
    for k, rz in enumerate((20, 140, 260)):
        add(BODY, B.leaf(0.07, 0.04, 0.012), "f_leaf", w, "seed_pod", smooth=False,
            m=M(P + Vector((0, 0, 0.06)), (0, 0, rz)) @ M((0, 0, 0), (50, 0, 0)))
    add(BODY, B.bcyl((0.285, 0.03, 0.66), (0.285, 0.03, 0.73), 0.012, 0.012, 6, 0.003), "f_brown", w, "seed_pod")


# ===================================================================================================== arms
def build_arms():
    for s, sd in ((1, "l"), (-1, "r")):
        ua, la, wr, hd = f"upperarm.{sd}", f"lowerarm.{sd}", f"wrist.{sd}", f"hand.{sd}"
        add(BODY, B.sphere((0.118, 0.12, 0.118), 10, 7), "f_cream", ["chest", ua], "jacket", m=M((0.228 * s, 0, 1.12)))
        add(BODY, B.bcyl((0.22 * s, 0, 1.107), (0.385 * s, 0.01, 1.107), 0.104, 0.098, 12, 0.02), "f_cream",
            ["chest", ua, la], "jacket")
        add(BODY, B.torus(0.097, 0.033, 12, 4), "f_team_cloth", ua, "team_cloth", m=M((0.385 * s, 0.01, 1.107), (0, 90, 0)))
        add(BODY, B.box((0.11, 0.11, 0.024), 0.008, 1), "f_team_cloth", ["chest", ua], "team_cloth", smooth=False,
            m=M((0.30 * s, 0.0, 1.207), (0, -6 * s, 0)))
        add(BODY, B.bcyl((0.40 * s, 0.01, 1.107), (0.575 * s, 0, 1.107), 0.07, 0.066, 10, 0.01), "f_skin",
            [ua, la, wr], "skin")
        # big work glove gauntlet with teal cuff, fist, thumb
        add(BODY, B.bcyl((0.565 * s, 0, 1.107), (0.745 * s, 0, 1.107), 0.097, 0.122, 10, 0.025), "f_glove", [la, wr],
            "gloves")
        add(BODY, B.torus(0.103, 0.032, 12, 4), "f_team_cloth", la, "team_cloth", m=M((0.58 * s, 0, 1.107), (0, 90, 0)))
        add(BODY, B.sphere((0.13, 0.125, 0.13), 10, 7), "f_glove", hd, "gloves", m=M((0.845 * s, -0.005, 1.095)))
        add(BODY, B.sphere((0.05, 0.045, 0.065), 6, 5), "f_glove", hd, "gloves", m=M((0.80 * s, -0.12, 1.12)))


# ===================================================================================================== head / face
def build_head():
    H = "head"
    add(BODY, B.sphere((0.24, 0.225, 0.25), 16, 12), "f_skin", H, "skin", m=M((0, -0.01, 1.55)))
    add(BODY, B.sphere((0.19, 0.18, 0.14), 14, 8), "f_skin", H, "skin", m=M((0, -0.04, 1.43)))
    for s in (1, -1):
        add(BODY, B.sphere((0.06, 0.02, 0.074), 8, 5), "f_white", H, "face", grad=False,
            m=M((0.095 * s, -0.226, 1.525)))
        add(BODY, B.sphere((0.05, 0.02, 0.064), 8, 5), "f_iris", H, "face", grad=False,
            m=M((0.092 * s, -0.236, 1.518)))
        add(BODY, B.sphere((0.026, 0.012, 0.034), 6, 4), "f_pupil", H, "face", grad=False,
            m=M((0.09 * s, -0.248, 1.512)))
        add(BODY, B.sphere((0.018, 0.008, 0.018), 4, 3), "f_white", H, "face", grad=False,
            m=M((0.073 * s, -0.254, 1.545)))
        add(BODY, B.box((0.10, 0.025, 0.026), 0.01, 1), "f_brow", H, "face", smooth=False,
            m=M((0.10 * s, -0.236, 1.625), (0, 0, 10 * s)) @ M((0, 0, 0), (0, -9 * s, 0)))
        add(BODY, B.sphere((0.048, 0.014, 0.03), 6, 4), "f_cheek", H, "face", grad=False,
            m=M((0.15 * s, -0.192, 1.45), (0, 0, 28 * s)))
    add(BODY, B.sphere((0.03, 0.025, 0.025), 8, 5), "f_skin_dark", H, "face", m=M((0, -0.238, 1.465)))
    smile = [(-0.075, 0.018), (-0.04, -0.008), (0.0, -0.016), (0.04, -0.008), (0.075, 0.018), (0.04, 0.004),
             (0.0, -0.002), (-0.04, 0.004)]
    add(BODY, B.prism(smile, 0.026), "f_mouth", H, "face", smooth=False, m=M((0, -0.213, 1.398), (90, 0, 0)))


def clump(p0, p1, p2, p3, r, n=6, flat=0.62):
    pts = bezier(p0, p1, p2, p3, n)
    radii = [r * (0.82 + 0.5 * t) if t < 0.3 else r * (1.0 - 0.9 * ((t - 0.3) / 0.7) ** 1.25) for t in
             (i / n for i in range(n + 1))]
    return B.tube(pts, radii, 6, flat)


def build_hair():
    H = "head"
    add(BODY, B.sphere((0.262, 0.256, 0.215), 14, 8), "f_hair", H, "hair", m=M((0, 0.025, 1.645)))
    add(BODY, B.sphere((0.25, 0.21, 0.24), 12, 8), "f_hair", H, "hair", m=M((0, 0.085, 1.50)))
    C = [  # 14 wavy clumps with tapered tips: bangs, side locks, back, bun flare
        ((-0.04, -0.14, 1.84), (-0.10, -0.29, 1.80), (-0.21, -0.28, 1.70), (-0.205, -0.25, 1.60), 0.085),
        ((0.05, -0.15, 1.84), (0.13, -0.29, 1.79), (0.21, -0.27, 1.69), (0.235, -0.22, 1.60), 0.08),
        ((0.0, -0.17, 1.83), (-0.02, -0.30, 1.77), (0.06, -0.295, 1.71), (0.075, -0.265, 1.665), 0.06),
        ((0.22, -0.08, 1.71), (0.34, -0.11, 1.58), (0.30, -0.10, 1.42), (0.35, -0.04, 1.33), 0.09),
        ((0.22, 0.10, 1.69), (0.345, 0.12, 1.55), (0.32, 0.16, 1.40), (0.37, 0.21, 1.33), 0.09),
        ((-0.22, -0.08, 1.71), (-0.34, -0.11, 1.58), (-0.30, -0.10, 1.42), (-0.35, -0.04, 1.33), 0.09),
        ((-0.22, 0.10, 1.69), (-0.345, 0.12, 1.55), (-0.32, 0.16, 1.40), (-0.37, 0.21, 1.33), 0.09),
        ((0.0, 0.22, 1.63), (0.03, 0.34, 1.51), (-0.04, 0.33, 1.39), (0.02, 0.31, 1.29), 0.09),
        ((0.15, 0.19, 1.63), (0.21, 0.30, 1.50), (0.16, 0.30, 1.39), (0.22, 0.27, 1.30), 0.08),
        ((-0.15, 0.19, 1.63), (-0.21, 0.30, 1.50), (-0.16, 0.30, 1.39), (-0.22, 0.27, 1.30), 0.08),
        ((0.0, -0.01, 1.91), (0.06, -0.05, 1.98), (0.13, -0.01, 1.98), (0.18, 0.06, 1.93), 0.07),
        ((0.0, 0.04, 1.91), (-0.06, 0.02, 1.98), (-0.14, 0.06, 1.97), (-0.19, 0.13, 1.91), 0.07),
        ((0.0, 0.12, 1.88), (0.02, 0.23, 1.95), (0.0, 0.29, 1.91), (0.035, 0.31, 1.83), 0.065),
        ((0.02, 0.0, 1.90), (0.0, -0.07, 1.97), (-0.045, -0.11, 1.955), (-0.065, -0.13, 1.89), 0.055),
    ]
    for p0, p1, p2, p3, r in C:
        add(BODY, clump(p0, p1, p2, p3, r), "f_hair", H, "hair")
    # high messy bun tied with a teal band
    add(BODY, B.sphere((0.15, 0.14, 0.12), 12, 8), "f_hair", H, "bun", m=M((0, 0.02, 1.865)))
    add(BODY, B.torus(0.118, 0.032, 12, 4), "f_team_cloth", H, "team_cloth", m=M((0, 0.02, 1.80), (-10, 0, 0)))
    # brass goggles with teal lenses pushed up on the forehead
    add(BODY, B.torus(0.268, 0.02, 20, 4), "f_brass", H, "goggles", m=M((0, 0.02, 1.735), (-14, 0, 0), (1, 0.97, 1)))
    for s in (1, -1):
        n = Vector((0.28 * s, -0.80, 0.53)).normalized()
        c = Vector((0.098 * s, -0.235, 1.745))
        add(BODY, B.bcyl(c - n * 0.02, c + n * 0.03, 0.066, 0.062, 10, 0.012), "f_brass", H, "goggles")
        add(BODY, B.bcyl(c + n * 0.02, c + n * 0.042, 0.048, 0.046, 10, 0.008), "f_team_cloth", H, "team_cloth")
    add(BODY, B.box((0.07, 0.03, 0.03), 0.01, 1), "f_brass", H, "goggles", smooth=False, m=M((0, -0.255, 1.76), (-30, 0, 0)))


# ===================================================================================================== globe tank
def build_tank():
    T, W = "tank", "chest"
    G = TANK_C
    add(BODY, B.sphere((TANK_R, TANK_R, TANK_R), 18, 11), "f_biomass", W, "tank", m=M(G))
    add(BODY, B.torus(TANK_R + 0.008, 0.026, 18, 4), "f_brass", W, T, m=M(G, (90, 0, 0)))          # ring seen from behind
    add(BODY, B.torus(TANK_R + 0.006, 0.02, 16, 4), "f_brass", W, T, m=M(G, (0, 90, 0)))           # vertical band
    add(BODY, B.torus(TANK_R + 0.01, 0.028, 20, 4), "f_frame_dark", W, T, m=M(G + Vector((0, 0, -0.04)), (0, 0, 0),
                                                                              (1, 1, 1)))
    for k in range(4):
        a = math.radians(45 + 90 * k)
        p = G + Vector((math.cos(a) * (TANK_R + 0.01), 0.0, math.sin(a) * (TANK_R + 0.01)))
        add(BODY, B.sphere((0.024, 0.024, 0.024), 6, 4), "f_brass", W, T, m=M(p + Vector((0, 0.03, 0))))
    # top collar and 5 separate leaves
    add(BODY, B.bcyl(G + Vector((0, 0, 0.195)), G + Vector((0, 0, 0.255)), 0.075, 0.062, 12, 0.015), "f_brass", W, T)
    for k, (az, tilt, ln) in enumerate(((20, 38, 0.19), (95, 52, 0.17), (165, 42, 0.20), (240, 55, 0.16),
                                        (310, 46, 0.18))):
        add(BODY, B.leaf(ln, ln * 0.48, 0.018), "f_leaf", W, "leaves", smooth=False,
            m=M(G + Vector((0, 0, 0.245)), (0, 0, az)) @ M((0, 0, 0), (90 - tilt, 0, 0)))
    # bottom nozzle
    n0, n1 = G + Vector((0, 0.04, -0.20)), G + Vector((0, 0.10, -0.31))
    add(BODY, B.bcyl(n0, n1, 0.065, 0.052, 12, 0.015), "f_frame_dark", W, T)
    add(BODY, B.torus(0.056, 0.016, 10, 4), "f_brass", W, T, m=kit.frame(n1, n1 - n0, (1, 0, 0)))
    # teal side canisters with brass caps and brackets
    for s in (1, -1):
        cx = G + Vector((0.262 * s, -0.02, 0.0))
        a, b = cx + Vector((0, 0, -0.12)), cx + Vector((0, 0, 0.12))
        add(BODY, B.bcyl(a, b, 0.056, 0.056, 8, 0.015), "f_team_cloth", W, "team_cloth")
        for e in (a, b):
            add(BODY, B.bcyl(e - Vector((0, 0, 0.022)), e + Vector((0, 0, 0.022)), 0.062, 0.062, 8, 0.01), "f_brass",
                W, T)
        add(BODY, B.box((0.06, 0.04, 0.035), 0.01, 1), "f_brass", W, T, smooth=False,
            m=M(G + Vector((0.215 * s, -0.02, 0.0))))
    # dark hose from the nozzle down to a brass coupler on the right of the belt
    pts = bezier(n1, n1 + Vector((-0.04, 0.03, -0.14)), Vector((-0.30, 0.22, 0.70)), Vector((-0.262, -0.02, 0.76)), 10)
    add(BODY, B.tube(pts, 0.026, 6), "f_hose", ["chest", "spine", "hips"], "hose")
    add(BODY, B.bcyl((-0.25, 0.0, 0.76), (-0.29, -0.04, 0.76), 0.035, 0.035, 10, 0.008), "f_brass", "hips", "hose")


# ===================================================================================================== staff
def held_matrix(poses, slot, origin, z_axis, y_hint):
    rest = B.arm.data.bones[slot].matrix_local
    return rest @ poses[slot].inverted() @ kit.frame(origin, z_axis, y_hint)


def build_staff(poses):
    """Hooked staff. Canonical: grip at origin, shaft up +Z, hook opens toward +Y; bottom z0, hook top z0 + STAFF_LEN."""
    W = "handslot.r"
    z0 = -0.53
    parts = []

    def p(bm, color, feat="staff", smooth=True, m=None):
        parts.append((bm, color, feat, smooth, m))

    p(B.bcyl((0, 0, z0 + 0.02), (0, 0, 1.10), 0.031, 0.031, 8, 0.01), "f_shaft")
    p(B.bcyl((0, 0, z0), (0, 0, z0 + 0.075), 0.036, 0.042, 10, 0.012), "f_brass")
    for z in (-0.36, -0.12, 0.22, 0.56, 1.02):
        p(B.bcyl((0, 0, z - 0.022), (0, 0, z + 0.022), 0.042, 0.042, 8, 0.01), "f_brass")
    p(B.bcyl((0, 0, 0.245), (0, 0, 0.535), 0.037, 0.037, 8, 0.008), "f_team_cloth", "team_cloth")
    p(B.bcyl((0, 0, -0.335), (0, 0, -0.145), 0.037, 0.037, 8, 0.008), "f_team_cloth", "team_cloth")
    p(B.bcyl((0, 0, -0.095), (0, 0, 0.075), 0.038, 0.038, 8, 0.008), "f_brown")
    # head: socket, glowing lime bulb in a brass cage, cap
    p(B.bcyl((0, 0, 1.08), (0, 0, 1.165), 0.048, 0.062, 12, 0.012), "f_brass")
    p(B.sphere((0.078, 0.078, 0.078), 12, 8), "f_biomass", "bulb", m=M((0, 0, 1.235)))
    p(B.torus(0.079, 0.012, 10, 3), "f_brass", m=M((0, 0, 1.235), (90, 0, 0)))
    p(B.torus(0.079, 0.012, 10, 3), "f_brass", m=M((0, 0, 1.235), (0, 90, 0)))
    p(B.bcyl((0, 0, 1.30), (0, 0, 1.335), 0.042, 0.036, 10, 0.01), "f_brass")
    # brass hook arc (local YZ plane), cream crescent blade on its outer edge
    cy, cz, R = 0.12, 1.27, 0.12
    arc = [(0.0, cy + R * math.cos(math.radians(a)), cz + R * math.sin(math.radians(a)))
           for a in range(160, -71, -15)]
    radii = [0.03 - 0.012 * i / (len(arc) - 1) for i in range(len(arc))]
    p(B.tube(arc, radii, 7, 1.0, up=(1, 0, 0)), "f_brass", "hook")
    pts_out, pts_in = [], []
    a0, a1, nseg = 150.0, -78.0, 10
    for i in range(nseg + 1):
        t = i / nseg
        a = math.radians(a0 + (a1 - a0) * t)
        wdt = 0.085 * math.sin(math.pi * min(1.0, t * 1.05)) ** 0.85
        r_in = R + 0.012
        pts_in.append((r_in * math.cos(a), r_in * math.sin(a)))
        pts_out.append(((r_in + wdt) * math.cos(a), (r_in + wdt) * math.sin(a)))
    tip = math.radians(a1 - 10)
    crescent = pts_in + [((R + 0.03) * math.cos(tip), (R + 0.03) * math.sin(tip))] + list(reversed(pts_out[1:-1]))
    p(B.prism(crescent, 0.024), "f_blade", "hook", False,
      rot_cols((0, 1, 0), (0, 0, 1), (1, 0, 0), (0, cy, cz)))
    # hose loop: out of the socket, a loop behind the staff, clipped back on below the grip
    hose = bezier((0.04, 0.0, 1.06), (0.26, -0.02, 0.92), (0.30, -0.03, -0.05), (0.04, 0.0, -0.24), 8)
    p(B.tube(hose, 0.021, 6), "f_hose", "hose")
    p(B.bcyl((0.0, 0.0, -0.265), (0.0, 0.0, -0.215), 0.045, 0.045, 10, 0.01), "f_brass")

    origin = poses[W].translation.copy()
    T = held_matrix(poses, W, origin, (-0.07, 0.02, 1.0), (-1, 0, 0))
    for bm, color, feat, smooth, m in parts:
        mm = T @ (m if m is not None else Matrix.Identity(4))
        add(STAFF, bm, color, W, feat, smooth=smooth, m=mm)
    return z0


# ===================================================================================================== main
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
    B = fkit.FarmerBuilder(arm)
    build_legs()
    build_torso()
    build_arms()
    build_head()
    build_hair()
    build_tank()
    z0 = build_staff(poses)

    coll = bpy.data.collections.new("Farmer")
    scene.collection.children.link(coll)
    for c in list(arm.users_collection):
        c.objects.unlink(arm)
    coll.objects.link(arm)
    objs = B.finalize(coll, mat)

    arm.animation_data_create()
    arm.animation_data.action = bpy.data.actions[DISPLAY_ACTION]
    scene.frame_set(DISPLAY_FRAME)

    body = objs[BODY]
    zs = [v.co.z for v in body.data.vertices]
    feats = {}
    for g in body.vertex_groups:
        if g.name.startswith("feat_"):
            idx = g.index
            vs = [v.co for v in body.data.vertices if any(gg.group == idx for gg in v.groups)]
            feats[g.name] = [round(min(c.z for c in vs), 3), round(max(c.z for c in vs), 3)]
    report = {"objects": {}, "bones": len(arm.data.bones), "display_pose": [DISPLAY_ACTION, DISPLAY_FRAME],
              "body_height_rest": round(max(zs) - min(zs), 4), "body_min_z": round(min(zs), 4),
              "staff_length_local": round(STAFF_LEN, 4), "staff_bottom_local": z0,
              "tank": {"centre": [round(v, 3) for v in TANK_C], "diameter": 2 * TANK_R,
                       "top_z": round(TANK_C.z + TANK_R, 3)},
              "feature_z_ranges_rest": feats}
    total = 0
    for name, ob in objs.items():
        tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
        total += tris
        report["objects"][name] = {
            "triangles": tris, "vertices": len(ob.data.vertices),
            "materials": [m.name for m in ob.data.materials],
            "feature_groups": sorted(g.name for g in ob.vertex_groups if g.name.startswith("feat_")),
            "bone_groups": len([g for g in ob.vertex_groups if g.name in arm.data.bones]),
        }
    report["triangles_total"] = total
    report["triangles_by_part"] = {f"{o}/{f}/{c}": v for (o, f, c), v in
                                   sorted(fkit.TRI_LOG.items(), key=lambda kv: -kv[1][0])}

    bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND, compress=True)
    export.export_fbx(OUT_FBX, arm, [objs[BODY], objs[STAFF]])
    with open(OUT_REPORT, "w") as f:
        json.dump(report, f, indent=1)
        f.write("\n")
    print("BUILD_REPORT " + json.dumps({k: report[k] for k in ("triangles_total", "body_height_rest", "body_min_z")}),
          {n: o["triangles"] for n, o in report["objects"].items()})


main()
