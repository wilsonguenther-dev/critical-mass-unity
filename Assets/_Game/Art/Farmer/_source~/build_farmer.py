"""Build the Farmer (bible p07, CM-CHR-002) from nothing: farmer.blend + ../Farmer.fbx.

  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python-exit-code 1 -P build_farmer.py

Reuses, by import and unmodified, the Warrior branch's shared kit in ../../Characters/_source~/cm_art (KayKit 41-joint
rig loader, mesh kit, FBX export settings, palette). Farmer-only additions live in farmer_art/.

Modelling space: Blender rest pose (KayKit T-pose), Z up, faces -Y, character left = +X, metres. The bible's staff hand
is the character's RIGHT (-X); the globe tank rides behind the LEFT shoulder (p07 front + back). Held item (staff) is placed where the
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
from farmer_art import face_decal, fkit, toon3  # noqa: E402
from farmer_art.fkit import bezier  # noqa: E402

OUT_BLEND = os.path.join(HERE, "farmer.blend")
OUT_FBX = os.path.join(os.path.dirname(HERE), "Farmer.fbx")
OUT_REPORT = os.path.join(HERE, "build_report.json")
OUT_FACE = os.path.join(os.path.dirname(HERE), "Farmer_Face.png")
DISPLAY_ACTION, DISPLAY_FRAME = "Idle", 1
BODY_H = 2.0
STAFF_LEN = 0.972 * BODY_H            # bible p07 measured ratio

B = None
BODY, STAFF = "Farmer_Body", "Farmer_Staff"
M = kit.mat_from
TANK_C = Vector((0.15, 0.40, 1.25))   # globe centre: behind the LEFT shoulder (+X) as p07 front and back draw it
TANK_R = 0.225                        # 0.45 u diameter (ART-DIRECTION 3.1)


def add(obj, bm, color, bones, feat, smooth=True, m=None, grad=True, face_uv=None):
    B.add(obj, bm, color, bones, feat=feat, smooth=smooth, m=m, grad=grad, face_uv=face_uv)


def rot_cols(x, y, z, t=(0, 0, 0)):
    """Matrix from column vectors (local X, Y, Z -> world) plus translation."""
    m = Matrix((x, y, z)).transposed().to_4x4()
    m.translation = Vector(t)
    return m


# ===================================================================================================== legs / boots
def build_legs():
    for s, sd in ((1, "l"), (-1, "r")):
        lx = 0.205 * s
        up, lo, ft, to = f"upperleg.{sd}", f"lowerleg.{sd}", f"foot.{sd}", f"toes.{sd}"
        # navy baggy overall legs ballooning at the knee (bible p07: very wide, soft trousers), gathered into the cuff
        prof = [(0.135, 0.24), (0.165, 0.30), (0.176, 0.38), (0.172, 0.48), (0.16, 0.62)]
        add(BODY, B.lathe(prof, 12), "f_navy", ["hips", up, lo], "overalls", m=M((lx, 0.0, 0)))
        add(BODY, B.box((0.17, 0.06, 0.16), 0.03, 1), "f_patch", [up, lo], "overalls", smooth=False,
            m=M((lx * 1.02, -0.188, 0.375), (-8, 0, 4 * s)))
        # chunky boot (bible: big, round-toed, a fat cream fold and a wide teal trim band), sole, strap + brass buckle
        add(BODY, B.bcyl((lx, 0.02, 0.06), (lx, 0.02, 0.19), 0.172, 0.166, 12, 0.022), "f_boot", [lo, ft], "boots")
        add(BODY, B.bcyl((lx, 0.02, 0.175), (lx, 0.02, 0.24), 0.18, 0.18, 12, 0.012), "f_team_cloth", lo,
            "team_cloth")
        add(BODY, B.bcyl((lx, 0.02, 0.245), (lx, 0.02, 0.335), 0.194, 0.205, 12, 0.028), "f_cream", lo, "boots")
        add(BODY, B.box((0.345, 0.48, 0.18), 0.06, 2), "f_boot", [ft, to], "boots", m=M((lx, -0.095, 0.088)))
        add(BODY, B.box((0.36, 0.50, 0.055), 0.018, 1), "f_sole", [ft, to], "boots", smooth=False,
            m=M((lx, -0.10, 0.026)))
        add(BODY, B.box((0.055, 0.03, 0.05), 0.012, 1), "f_brass", [lo, ft], "boots", smooth=False,
            m=M((lx + 0.168 * s, 0.0, 0.11)))


# ===================================================================================================== torso
def build_torso():
    add(BODY, B.sphere((0.29, 0.21, 0.18), 14, 8), "f_navy", ["hips", "spine"], "overalls", m=M((0, 0.0, 0.63)))
    add(BODY, B.sphere((0.26, 0.19, 0.27), 14, 8), "f_navy", ["spine", "chest"], "overalls", m=M((0, 0.0, 0.92)))
    # cream short work jacket, open at the front, stand-up collar, lapels, two flap chest pockets, side seams
    add(BODY, B.sphere((0.285, 0.225, 0.26), 14, 9), "f_cream", ["spine", "chest"], "jacket", m=M((0, 0.02, 1.02)))
    add(BODY, B.box((0.20, 0.07, 0.31), 0.03, 1), "f_navy", ["spine", "chest"], "overalls", m=M((0, -0.188, 0.95)))
    add(BODY, B.box((0.12, 0.02, 0.07), 0.01, 1), "f_navy_dark", "chest", "overalls", smooth=False,
        m=M((0, -0.226, 0.97)))
    for s in (1, -1):
        add(BODY, B.bcyl((0.075 * s, -0.222, 1.05), (0.075 * s, -0.244, 1.05), 0.022, 0.022, 8, 0.006), "f_brass",
            "chest", "overalls", smooth=False)
        # lapel: a cream-shade wedge turned back along the open front
        lap = [(0.0, 0.0), (0.055, 0.02), (0.075, 0.20), (0.03, 0.27), (0.0, 0.24)]
        lap = [(x * s, z) for x, z in lap] if s > 0 else [(-x, z) for x, z in reversed(lap)]
        add(BODY, B.prism(lap, 0.02), "f_cream_dark", "chest", "jacket", smooth=False,
            m=M((0.105 * s, -0.200, 0.94), (90, 0, -18 * s)))
        # chest pocket with a flap and a brass snap, on the jacket front beside the bib
        add(BODY, B.box((0.085, 0.03, 0.09), 0.012, 1), "f_cream_dark", "chest", "jacket", smooth=False,
            m=M((0.205 * s, -0.175, 1.0), (0, 0, 30 * s)))
        add(BODY, B.box((0.095, 0.035, 0.035), 0.01, 1), "f_cream", "chest", "jacket", smooth=False,
            m=M((0.205 * s, -0.185, 1.045), (-10, 0, 30 * s)))
        add(BODY, B.sphere((0.011, 0.011, 0.011), 6, 4), "f_brass", "chest", "jacket",
            m=M((0.198 * s, -0.205, 1.042)))
    add(BODY, B.torus(0.155, 0.06, 12, 4), "f_cream", "chest", "jacket",
        m=M((0, 0.01, 1.21), (-14, 0, 0), (1.14, 1.0, 0.9)))
    # neck
    add(BODY, B.cyl((0, 0, 1.17), (0, 0, 1.34), 0.074, 0.072, 10), "f_skin", ["chest", "head"], "skin")
    # tank shoulder straps (flat dark-brown bands over the jacket)
    for s in (1, -1):
        path = [(0.13 * s, 0.25, 1.28), (0.15 * s, 0.06, 1.322), (0.15 * s, -0.16, 1.20), (0.14 * s, -0.225, 1.00),
                (0.135 * s, -0.215, 0.80)]
        add(BODY, B.tube(path, 0.032, 6, 0.4, up=(0, -1, 0)), "f_brown", ["chest", "spine"], "belt")
    # belt, buckle, pouches
    add(BODY, B.bcyl((0, 0, 0.71), (0, 0, 0.80), 0.292, 0.298, 16, 0.015), "f_brown", {"hips": 0.5, "spine": 0.5},
        "belt", m=M((0, 0, 0), (0, 0, 0), (1, 0.78, 1)))
    add(BODY, B.box((0.12, 0.035, 0.095), 0.015, 1), "f_brass", {"hips": 0.5, "spine": 0.5}, "belt", smooth=False,
        m=M((0, -0.236, 0.755)))
    add(BODY, B.box((0.06, 0.02, 0.045), 0.006, 1), "f_brown", {"hips": 0.5, "spine": 0.5}, "belt", smooth=False,
        m=M((0, -0.253, 0.755)))
    for (x, y, z, rz) in ((0.215, -0.175, 0.665, -32), (-0.27, -0.04, 0.665, 72), (-0.10, 0.215, 0.68, 172)):
        w = {"hips": 0.7, "spine": 0.3}
        add(BODY, B.box((0.13, 0.085, 0.14), 0.025, 1), "f_leather", w, "belt", m=M((x, y, z), (0, 0, rz)))
        add(BODY, B.box((0.135, 0.09, 0.048), 0.012, 1), "f_brown", w, "belt", smooth=False,
            m=M((x, y, z + 0.055), (0, 0, rz)))
    # root-seed pod carried on the left hip: round wooden pod, dark seams, lime leaves, lime glow crack
    P = Vector((0.305, 0.03, 0.60))
    w = {"hips": 1.0}
    add(BODY, B.sphere((0.08, 0.08, 0.076), 8, 6), "f_wood", w, "seed_pod", m=M(P))
    for rz in (0, 90):
        add(BODY, B.torus(0.079, 0.008, 8, 3), "f_wood_dark", w, "seed_pod", m=M(P, (90, 0, rz)))
    add(BODY, B.sphere((0.03, 0.02, 0.03), 6, 4), "f_biomass", w, "seed_pod", m=M(P + Vector((0.035, -0.06, 0.0))))
    for k, rz in enumerate((20, 140, 260)):
        add(BODY, B.leaf(0.075, 0.042, 0.012), "f_leaf", w, "seed_pod", smooth=False,
            m=M(P + Vector((0, 0, 0.065)), (0, 0, rz)) @ M((0, 0, 0), (50, 0, 0)))
    add(BODY, B.bcyl((0.305, 0.03, 0.665), (0.305, 0.03, 0.735), 0.012, 0.012, 6, 0.003), "f_brown", w, "seed_pod")


# ===================================================================================================== arms
def build_arms():
    for s, sd in ((1, "l"), (-1, "r")):
        ua, la, wr, hd = f"upperarm.{sd}", f"lowerarm.{sd}", f"wrist.{sd}", f"hand.{sd}"
        # puffy cream short sleeve, a rolled teal sleeve band and a small teal shoulder patch (bible p07 sizes)
        add(BODY, B.sphere((0.13, 0.13, 0.13), 10, 7), "f_cream", ["chest", ua], "jacket", m=M((0.235 * s, 0, 1.12)))
        add(BODY, B.bcyl((0.22 * s, 0, 1.107), (0.37 * s, 0.01, 1.107), 0.112, 0.106, 12, 0.02), "f_cream",
            ["chest", ua, la], "jacket")
        add(BODY, B.bcyl((0.35 * s, 0.01, 1.107), (0.43 * s, 0.01, 1.107), 0.12, 0.12, 12, 0.016), "f_team_cloth",
            ua, "team_cloth")
        add(BODY, B.box((0.19, 0.2, 0.03), 0.01, 1), "f_team_cloth", ["chest", ua], "team_cloth", smooth=False,
            m=M((0.25 * s, 0.0, 1.236), (0, -16 * s, 0)))
        add(BODY, B.bcyl((0.43 * s, 0.01, 1.107), (0.56 * s, 0, 1.107), 0.072, 0.068, 10, 0.01), "f_skin",
            [ua, la, wr], "skin")
        # big work glove gauntlet (bible: oversized), a long flared teal cuff, fist, thumb, knuckle ridge
        add(BODY, B.bcyl((0.55 * s, 0, 1.107), (0.75 * s, 0, 1.107), 0.108, 0.135, 10, 0.028), "f_glove", [la, wr],
            "gloves")
        add(BODY, B.bcyl((0.535 * s, 0, 1.107), (0.645 * s, 0, 1.107), 0.13, 0.14, 12, 0.016), "f_team_cloth",
            [la, wr], "team_cloth")
        add(BODY, B.sphere((0.158, 0.15, 0.155), 10, 7), "f_glove", hd, "gloves", m=M((0.865 * s, -0.005, 1.095)))
        add(BODY, B.sphere((0.06, 0.055, 0.075), 6, 5), "f_glove", hd, "gloves", m=M((0.81 * s, -0.14, 1.12)))
        add(BODY, B.box((0.05, 0.2, 0.06), 0.02, 1), "f_brown", hd, "gloves", m=M((0.95 * s, -0.01, 1.14)))


# ===================================================================================================== head / face
FACE_UV = (0.0, -0.06, 1.47, 0.20, 0.20)      # decal box: centre x, front cut y, centre z, half width, half height


def build_head():
    H = "head"
    # wider, softer face: round skull, full cheeks and a soft jaw; features are the painted 256 px decal
    add(BODY, B.sphere((0.235, 0.215, 0.235), 16, 12), "f_skin", H, "skin", m=M((0, -0.01, 1.545)), face_uv=FACE_UV)
    add(BODY, B.sphere((0.195, 0.17, 0.135), 12, 8), "f_skin", H, "skin", m=M((0, -0.04, 1.43)), face_uv=FACE_UV)
    add(BODY, B.sphere((0.026, 0.02, 0.024), 6, 4), "f_skin", H, "face", m=M((0, -0.236, 1.45)))      # nose tip
    for s in (1, -1):
        add(BODY, B.sphere((0.03, 0.04, 0.05), 5, 4), "f_skin", H, "skin", m=M((0.228 * s, 0.0, 1.49)))  # ears


def lock(p0, p3, bend, wave, r, n=6, flat=0.55, side_hint=(0, 0, 1)):
    """Wavy, tapered, slightly flattened hair lock: a cubic sweep with a sine wave across it (volume at the root)."""
    p0, p3, bend = Vector(p0), Vector(p3), Vector(bend)
    pts = bezier(p0, p0.lerp(p3, 0.33) + bend, p0.lerp(p3, 0.66) + bend * 0.7, p3, n)
    ax = (p3 - p0).normalized()
    side = ax.cross(Vector(side_hint))
    if side.length < 1e-3:
        side = ax.cross(Vector((1, 0, 0)))
    side.normalize()
    pts = [p + side * (wave * math.sin(2 * math.pi * i / n) * (i / n)) for i, p in enumerate(pts)]
    radii = []
    for i in range(n + 1):
        t = i / n
        radii.append(r * (0.85 + 0.6 * t / 0.25) if t < 0.25 else r * 1.0 * (1.0 - 0.88 * ((t - 0.25) / 0.75) ** 1.4))
    radii = [min(x, r * 1.0) for x in radii]
    return B.tube(pts, radii, 5, flat)


def build_hair():
    H = "head"
    # big volume: cap over the crown, a full back mass, puffs at the sides (kept behind the face line, y > -0.08)
    add(BODY, B.sphere((0.285, 0.27, 0.20), 14, 8), "f_hair", H, "hair", m=M((0, 0.035, 1.70)))
    add(BODY, B.sphere((0.27, 0.235, 0.25), 12, 8), "f_hair", H, "hair", m=M((0, 0.095, 1.50)))
    for s in (1, -1):
        add(BODY, B.sphere((0.13, 0.15, 0.19), 8, 6), "f_hair", H, "hair", m=M((0.255 * s, 0.075, 1.52)))
    L = [  # 14 wavy locks: p0, p3, bend, wave, radius
        # swept bangs, parted at the character's right, falling over the forehead to the temples
        ((-0.06, -0.14, 1.86), (0.25, -0.17, 1.60), (0.06, -0.14, 0.02), 0.03, 0.095),
        ((-0.02, -0.15, 1.86), (-0.255, -0.15, 1.62), (-0.06, -0.13, 0.03), 0.03, 0.085),
        ((0.03, -0.17, 1.85), (0.10, -0.25, 1.70), (0.03, -0.08, 0.0), 0.02, 0.06),
        # temple curls framing the cheeks (behind the cheek line so the face stays clear in profile)
        ((0.25, -0.10, 1.66), (0.29, -0.08, 1.36), (0.05, -0.02, 0.0), 0.05, 0.075),
        ((-0.25, -0.10, 1.66), (-0.29, -0.08, 1.36), (-0.05, -0.02, 0.0), 0.05, 0.075),
        # side volume down to the shoulders, curling outwards
        ((0.27, 0.10, 1.64), (0.36, 0.18, 1.30), (0.09, 0.0, 0.0), 0.06, 0.095),
        ((-0.27, 0.10, 1.64), (-0.36, 0.18, 1.30), (-0.09, 0.0, 0.0), 0.06, 0.095),
        # back of the head to the nape
        ((0.0, 0.25, 1.66), (0.03, 0.31, 1.26), (0.0, 0.10, 0.0), 0.06, 0.10),
        ((0.16, 0.22, 1.66), (0.22, 0.27, 1.28), (0.04, 0.09, 0.0), 0.05, 0.09),
        ((-0.16, 0.22, 1.66), (-0.22, 0.27, 1.28), (-0.04, 0.09, 0.0), 0.05, 0.09),
        # messy strands escaping the bun
        ((0.10, 0.0, 1.93), (0.30, 0.04, 1.86), (0.0, 0.0, 0.08), 0.03, 0.06),
        ((-0.10, 0.06, 1.93), (-0.30, 0.10, 1.88), (0.0, 0.0, 0.08), 0.03, 0.06),
        ((0.0, 0.14, 1.92), (0.04, 0.36, 1.84), (0.0, 0.0, 0.08), 0.03, 0.06),
        ((0.0, -0.06, 1.925), (-0.05, -0.20, 1.95), (0.0, 0.0, 0.04), 0.02, 0.05),
    ]
    for p0, p3, bend, wave, r in L:
        add(BODY, lock(p0, p3, bend, wave, r), "f_hair", H, "hair")
    # big high messy bun: a main ball and four lobes, tied with a wide teal band
    add(BODY, B.sphere((0.20, 0.19, 0.13), 12, 7), "f_hair", H, "bun", m=M((0, 0.04, 1.85)))
    for k, (az, r, dz) in enumerate(((30, 0.10, 0.05), (130, 0.095, 0.06), (220, 0.105, 0.045), (310, 0.09, 0.07))):
        a = math.radians(az)
        add(BODY, B.sphere((r, r, r * 0.85), 7, 5), "f_hair", H, "bun",
            m=M((0.11 * math.cos(a), 0.04 + 0.11 * math.sin(a), 1.848 + dz)))
    add(BODY, B.torus(0.168, 0.056, 12, 4), "f_team_cloth", H, "team_cloth", m=M((0, 0.045, 1.775), (-8, 0, 0)))
    # brass goggles with teal lenses pushed up on top of the hair at the front
    add(BODY, B.torus(0.27, 0.02, 16, 4), "f_brass", H, "goggles", m=M((0, 0.03, 1.765), (-25, 0, 0), (1.04, 1.0, 1)))
    for s in (1, -1):
        n = Vector((0.26 * s, -0.62, 0.74)).normalized()
        c = Vector((0.12 * s, -0.215, 1.875))
        add(BODY, B.bcyl(c - n * 0.025, c + n * 0.03, 0.084, 0.078, 10, 0.012), "f_brass", H, "goggles")
        add(BODY, B.bcyl(c + n * 0.02, c + n * 0.042, 0.066, 0.064, 10, 0.008), "f_team_cloth", H, "team_cloth")
    add(BODY, B.box((0.07, 0.03, 0.035), 0.01, 1), "f_brass", H, "goggles", smooth=False,
        m=M((0, -0.238, 1.885), (-40, 0, 0)))


# ===================================================================================================== globe tank
def build_tank():
    T, W = "tank", "chest"
    G = TANK_C
    add(BODY, B.sphere((TANK_R, TANK_R, TANK_R), 18, 11), "f_biomass", W, "tank", m=M(G))
    add(BODY, B.torus(TANK_R + 0.008, 0.028, 16, 4), "f_brass", W, T, m=M(G, (90, 0, 0)))          # ring seen from behind
    add(BODY, B.torus(TANK_R + 0.006, 0.018, 14, 3), "f_brass", W, T, m=M(G, (0, 90, 0)))          # vertical frame band
    for d, ln, rot in (((0.05, 1.0, 0.05), 0.17, 0), ((-0.55, 0.8, -0.25), 0.14, 35), ((0.5, 0.8, -0.3), 0.14, -35)):
        n = Vector(d).normalized()
        add(BODY, B.leaf(ln, ln * 0.5, 0.012), "f_leaf_dark", W, "leaves", smooth=False,
            m=kit.frame(G + n * (TANK_R - 0.003), n, (0, 0, 1)) @ M((0, -ln * 0.45, 0), (0, 0, rot)))
    hn = Vector((-0.45, 0.72, 0.53)).normalized()
    add(BODY, B.sphere((0.075, 0.05, 0.02), 6, 4), "f_biomass_hi", W, "tank_glint",
        m=kit.frame(G + hn * (TANK_R - 0.004), hn, (0, 0, 1)))
    # top collar and 5 separate leaves
    add(BODY, B.bcyl(G + Vector((0, 0, 0.195)), G + Vector((0, 0, 0.255)), 0.078, 0.064, 12, 0.015), "f_brass", W, T)
    for k, (az, tilt, ln) in enumerate(((20, 38, 0.19), (95, 52, 0.17), (165, 42, 0.20), (240, 55, 0.16),
                                        (310, 46, 0.18))):
        add(BODY, B.leaf(ln, ln * 0.48, 0.018), "f_leaf", W, "leaves", smooth=False,
            m=M(G + Vector((0, 0, 0.245)), (0, 0, az)) @ M((0, 0, 0), (90 - tilt, 0, 0)))
    # bottom nozzle
    n0, n1 = G + Vector((0, 0.04, -0.20)), G + Vector((0, 0.10, -0.31))
    add(BODY, B.bcyl(n0, n1, 0.068, 0.054, 12, 0.015), "f_frame_dark", W, T)
    add(BODY, B.torus(0.058, 0.016, 10, 4), "f_brass", W, T, m=kit.frame(n1, n1 - n0, (1, 0, 0)))
    # teal side canisters (team, bible size), tucked low and forward against the globe so they never stick out like wings
    # from behind; brass caps and a bracket each
    for s in (1, -1):
        cx = G + Vector((0.205 * s, -0.085, -0.05))
        a, b = cx + Vector((0.012 * s, 0, -0.17)), cx + Vector((-0.012 * s, 0, 0.17))
        add(BODY, B.bcyl(a, b, 0.075, 0.075, 10, 0.016), "f_team_cloth", W, "team_cloth")
        for e in (a, b):
            add(BODY, B.bcyl(e - (b - a).normalized() * 0.022, e + (b - a).normalized() * 0.022, 0.081, 0.081, 10,
                             0.01), "f_brass", W, T)
        add(BODY, B.box((0.05, 0.04, 0.035), 0.01, 1), "f_brass", W, T, smooth=False,
            m=M(G + Vector((0.16 * s, -0.07, -0.05))))
    return n1


def build_tank_hose(n1, coupler):
    """Dark hose from the tank nozzle, down the back, round the right hip, along the right arm to the staff's lower
    coupler (bible p07: the tank feeds the staff). Three skinned spans: back (chest/spine/hips), arm (upper/lower arm,
    hand) and a last span rigid on handslot.r so it always meets the staff."""
    hip = Vector((-0.30, 0.06, 0.86))
    elbow = Vector((-0.50, 0.06, 1.02))
    wrist = Vector((-0.66, 0.06, 1.04))
    a = bezier(n1, n1 + Vector((-0.02, 0.06, -0.16)), Vector((-0.26, 0.30, 0.74)), hip, 6)
    add(BODY, B.tube(a, 0.027, 6), "f_hose", ["chest", "spine", "hips"], "hose")
    b = bezier(hip, hip + Vector((-0.06, 0.0, 0.08)), elbow + Vector((0.06, 0.0, -0.02)), wrist, 4)
    add(BODY, B.tube(b, 0.027, 6), "f_hose", ["upperarm.r", "lowerarm.r", "wrist.r"], "hose")
    for p, bones in ((hip, ["hips", "upperarm.r"]), (wrist, ["lowerarm.r", "wrist.r"])):
        add(BODY, B.sphere((0.036, 0.036, 0.036), 6, 4), "f_brass", bones, "hose", m=M(p))
    k = Vector(coupler)
    c = bezier(wrist, wrist + Vector((-0.04, 0.0, -0.12)), k + Vector((0.0, 0.0, -0.10)), k, 5)
    add(BODY, B.tube(c, 0.025, 6), "f_hose", "handslot.r", "hose")
    add(BODY, B.sphere((0.034, 0.034, 0.034), 6, 4), "f_brass", "handslot.r", "hose", m=M(k))


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
    for z in (-0.36, -0.12, 0.22, 0.60, 1.02):
        p(B.bcyl((0, 0, z - 0.022), (0, 0, z + 0.022), 0.042, 0.042, 8, 0.01), "f_brass")
    p(B.bcyl((0, 0, 0.245), (0, 0, 0.575), 0.038, 0.038, 8, 0.008), "f_team_cloth", "team_cloth")
    p(B.bcyl((0, 0, 0.625), (0, 0, 0.995), 0.038, 0.038, 8, 0.008), "f_team_cloth", "team_cloth")
    p(B.bcyl((0, 0, -0.335), (0, 0, -0.145), 0.037, 0.037, 8, 0.008), "f_team_cloth", "team_cloth")
    p(B.bcyl((0, 0, -0.095), (0, 0, 0.075), 0.038, 0.038, 8, 0.008), "f_brown")
    # head: socket, glowing lime bulb in a brass cage, cap
    p(B.bcyl((0, 0, 1.08), (0, 0, 1.165), 0.048, 0.062, 12, 0.012), "f_brass")
    p(B.sphere((0.09, 0.09, 0.09), 12, 8), "f_biomass", "bulb", m=M((0, 0, 1.24)))
    p(B.torus(0.091, 0.012, 10, 3), "f_brass", m=M((0, 0, 1.24), (90, 0, 0)))
    p(B.torus(0.091, 0.012, 10, 3), "f_brass", m=M((0, 0, 1.24), (0, 90, 0)))
    p(B.bcyl((0, 0, 1.325), (0, 0, 1.36), 0.042, 0.036, 10, 0.01), "f_brass")
    # brass hook arc (local YZ plane), cream crescent blade on its outer edge
    cy, cz, R = 0.18, 1.23, 0.18
    arc = [(0.0, cy + R * math.cos(math.radians(a)), cz + R * math.sin(math.radians(a)))
           for a in range(160, -71, -15)]
    radii = [0.036 - 0.014 * i / (len(arc) - 1) for i in range(len(arc))]
    p(B.tube(arc, radii, 7, 1.0, up=(1, 0, 0)), "f_brass", "hook")
    pts_out, pts_in = [], []
    a0, a1, nseg = 150.0, -78.0, 10
    for i in range(nseg + 1):
        t = i / nseg
        a = math.radians(a0 + (a1 - a0) * t)
        wdt = 0.14 * math.sin(math.pi * min(1.0, t * 1.05)) ** 0.85
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
    p(B.bcyl((0.0, 0.0, -0.33), (0.045, 0.0, -0.31), 0.024, 0.024, 8, 0.006), "f_brass", "hose")   # tank-hose port

    origin = poses[W].translation.copy()
    T = held_matrix(poses, W, origin, (-0.07, 0.02, 1.0), (-1, 0, 0))
    for bm, color, feat, smooth, m in parts:
        mm = T @ (m if m is not None else Matrix.Identity(4))
        add(STAFF, bm, color, W, feat, smooth=smooth, m=mm)
    return z0, T @ Vector((0.045, 0.0, -0.31))


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

    face_img = face_decal.make_image(bpy, "Farmer_Face", OUT_FACE, FACE_UV[0], FACE_UV[2], FACE_UV[3], FACE_UV[4])
    mat = toon3.make_material("CM_Toon", face_img)
    B = fkit.FarmerBuilder(arm)
    build_legs()
    build_torso()
    build_arms()
    build_head()
    build_hair()
    nozzle = build_tank()
    z0, coupler = build_staff(poses)
    build_tank_hose(nozzle, coupler)

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
    for ob in objs.values():
        g = ob.vertex_groups.new(name="outline_w")
        no_line = {ob.vertex_groups[n].index for n in ("feat_face", "feat_tank_glint") if n in ob.vertex_groups}
        for v in ob.data.vertices:
            g.add([v.index], 0.0 if any(gg.group in no_line for gg in v.groups) else 1.0, "REPLACE")
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
