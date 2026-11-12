"""Build the Warrior (bible p06, CM-CHR-001) from nothing: warrior.blend + ../Warrior.fbx.

  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python-exit-code 1 -P build_warrior.py

Deterministic: factory startup, fixed seed, no wall clock. Inputs: kaykit/Knight.glb (rig + 76 clips, CC0) and the
modules in cm_art/. Outputs (paths relative to this folder):
  warrior.blend          source scene: Rig + Warrior_Body + Warrior_Shield + Warrior_Cleaver, KayKit clips kept
  ../Warrior.fbx         the only file Unity imports (P-08)
  rig_kaykit41.json      the 41 joints and 76 clip names, for checks and for the next character
  build_report.json      triangle counts, bone count, feature groups

Modelling space: Blender rest pose (T-pose), Z up, faces -Y, character left = +X, metres. Bible "viewer's right in
front view" = character left = +X. Held items (shield, cleaver) are placed where the bible shows them in the KayKit
"Idle" pose and mapped back to rest through their hand-slot bone, so they sit right in Idle and follow every clip.
"""
import json
import math
import os
import random
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.dont_write_bytecode = True
sys.path.insert(0, HERE)

from cm_art import export, kit, palette, rig, toon  # noqa: E402

SEED = 606
OUT_BLEND = os.path.join(HERE, "warrior.blend")
OUT_FBX = os.path.join(os.path.dirname(HERE), "Warrior.fbx")
OUT_JOINTS = os.path.join(HERE, "rig_kaykit41.json")
OUT_REPORT = os.path.join(HERE, "build_report.json")
DISPLAY_ACTION, DISPLAY_FRAME = "Idle", 1

rng = random.Random(SEED)
B = None  # kit.Builder
BODY, SHIELD, CLEAVER = "Warrior_Body", "Warrior_Shield", "Warrior_Cleaver"
M = kit.mat_from


def add(obj, bm, color, bones, feat, smooth=True, m=None):
    B.add(obj, bm, color, bones, feat=feat, smooth=smooth, m=m)


# ===================================================================================================== body
def build_legs():
    for s, sd in ((1, "l"), (-1, "r")):
        lx = 0.171 * s
        up, lo, ft, to = f"upperleg.{sd}", f"lowerleg.{sd}", f"foot.{sd}", f"toes.{sd}"
        # baggy cream trousers: wide bulge at the thigh, gathered into the knee wraps (bottom-heavy read)
        prof = [(0.12, 0.24), (0.17, 0.29), (0.205, 0.38), (0.20, 0.48), (0.175, 0.60)]
        add(BODY, B.lathe(prof, 14), "trouser", ["hips", up, lo], "clothes", m=M((lx * 1.12, 0.0, 0)))
        # dark shin wraps
        add(BODY, B.cyl((lx * 1.08, 0.01, 0.30), (lx * 1.08, 0.02, 0.15), 0.118, 0.11, 12), "wrap_dark", [lo, ft],
            "clothes")
        # grey stone knee guard (rounded chunky block, flat shaded stone)
        add(BODY, B.box((0.21, 0.11, 0.17), 0.035, 1), "stone_armour", lo, "stone_armour", smooth=False,
            m=M((lx * 1.10, -0.13, 0.30), (-8, 0, 6 * s)))
        # oversized brown boot: shaft, cuff, strap + brass buckle, foot block, grey stone toe cap, dark sole
        add(BODY, B.cyl((lx * 1.08, 0.02, 0.07), (lx * 1.08, 0.02, 0.23), 0.15, 0.145, 14), "boot", [lo, ft], "boots")
        add(BODY, B.torus(0.148, 0.034, 14, 6), "leather_dark", lo, "boots", m=M((lx * 1.08, 0.02, 0.225)))
        add(BODY, B.torus(0.152, 0.02, 14, 5), "leather_dark", [lo, ft], "boots", m=M((lx * 1.08, 0.02, 0.13)))
        add(BODY, B.box((0.05, 0.03, 0.05), 0.01), "brass", [lo, ft], "boots", smooth=False,
            m=M((lx * 1.08 + 0.15 * s, 0.0, 0.13)))
        add(BODY, B.box((0.29, 0.42, 0.15), 0.06, 2), "boot", [ft, to], "boots", m=M((lx * 1.12, -0.08, 0.08)))
        add(BODY, B.box((0.30, 0.15, 0.135), 0.035, 1), "stone_armour", to, "stone_armour", smooth=False,
            m=M((lx * 1.12, -0.235, 0.07)))
        add(BODY, B.box((0.30, 0.44, 0.04), 0.012, 1), "leather_dark", [ft, to], "boots", smooth=False,
            m=M((lx * 1.12, -0.085, 0.02)))


def build_torso():
    # trouser seat / hips
    add(BODY, B.sphere((0.32, 0.24, 0.20), 16, 10), "trouser", ["hips", "spine"], "clothes", m=M((0, 0.0, 0.60)))
    # navy padded doublet, a barrel that overhangs the belt (fixed navy: the accent-swap crop keeps it navy)
    add(BODY, B.sphere((0.35, 0.26, 0.31), 20, 12), "navy", ["spine", "chest"], "clothes", m=M((0, 0.01, 0.97)))
    # cream quilted undershirt: a broad V panel down the chest
    add(BODY, B.sphere((0.19, 0.13, 0.26), 16, 10), "cream", ["spine", "chest"], "clothes", m=M((0, -0.15, 0.96)))
    # brown leather belt + big round brass buckle disc
    add(BODY, B.cyl((0, 0, 0.73), (0, 0, 0.82), 0.315, 0.325, 22), "leather_dark", {"hips": 0.5, "spine": 0.5},
        "belt", m=M((0, 0, 0), (0, 0, 0), (1, 0.84, 1)))
    add(BODY, B.cyl((0, -0.24, 0.775), (0, -0.30, 0.775), 0.105, 0.10, 18), "brass", {"hips": 0.5, "spine": 0.5},
        "belt", smooth=False)
    add(BODY, B.torus(0.07, 0.018, 16, 5), "leather_dark", {"hips": 0.5, "spine": 0.5}, "belt",
        m=M((0, -0.302, 0.775), (90, 0, 0)))
    # belt pouch with brass flap (bible: pouch on the belt, viewer's right)
    add(BODY, B.box((0.13, 0.10, 0.14), 0.03, 1), "leather", {"hips": 0.6, "spine": 0.4}, "belt",
        m=M((0.24, -0.20, 0.71), (0, 0, -25)))
    add(BODY, B.box((0.135, 0.105, 0.055), 0.015, 1), "brass", {"hips": 0.6, "spine": 0.4}, "belt", smooth=False,
        m=M((0.24, -0.205, 0.765), (0, 0, -25)))
    # teal tabard strips, front and back, zigzag hem (team cloth)
    hem = [(-0.11, 0.0), (0.11, 0.0), (0.12, -0.36), (0.08, -0.43), (0.04, -0.36), (0.0, -0.44),
           (-0.04, -0.36), (-0.08, -0.43), (-0.12, -0.36)]
    for y, ry in ((-0.262, 0), (0.262, 180)):
        add(BODY, B.prism(hem, 0.025), "team_cloth", "hips", "team_cloth", smooth=False,
            m=M((0, y, 0.79), (90, 0, ry)) @ M((0, 0, 0), (-8, 0, 0)))
    # neck
    add(BODY, B.cyl((0, 0, 1.14), (0, 0, 1.30), 0.095, 0.09, 12), "skin", ["chest", "head"], "skin")
    # teal scarf: thick neck roll, puffy front fold, wide tail down the back with zigzag end (team cloth)
    add(BODY, B.torus(0.175, 0.10, 18, 8), "team_cloth", "chest", "team_cloth",
        m=M((0, -0.01, 1.22), (0, 0, 0), (1.05, 0.95, 0.9)))
    add(BODY, B.sphere((0.17, 0.09, 0.12), 12, 8), "team_cloth", "chest", "team_cloth",
        m=M((0.04, -0.19, 1.13), (0, 18, 0)))
    tail = [(-0.16, 0.0), (0.16, 0.0), (0.15, -0.34), (0.08, -0.41), (0.0, -0.33), (-0.08, -0.41), (-0.15, -0.34)]
    add(BODY, B.prism(tail, 0.03), "team_cloth", "chest", "team_cloth", smooth=False,
        m=M((0, 0.305, 1.21), (90, 0, 180)) @ M((0, 0, 0), (-4, 0, 0)))


def build_arms():
    for s, sd in ((1, "l"), (-1, "r")):
        ua, la, wr, hd = f"upperarm.{sd}", f"lowerarm.{sd}", f"wrist.{sd}", f"hand.{sd}"
        add(BODY, B.sphere((0.14, 0.14, 0.14), 12, 8), "navy", ["chest", ua], "clothes", m=M((0.26 * s, 0, 1.11)))
        add(BODY, B.cyl((0.22 * s, 0, 1.107), (0.47 * s, 0.01, 1.107), 0.125, 0.11, 12), "navy", ["chest", ua, la],
            "clothes")
        add(BODY, B.sphere((0.11, 0.11, 0.11), 10, 6), "navy", [ua, la], "clothes", m=M((0.46 * s, 0.012, 1.107)))
        # thick brown leather bracer flaring to the wrist, brass trim
        add(BODY, B.cyl((0.47 * s, 0.012, 1.107), (0.735 * s, 0, 1.107), 0.115, 0.135, 12), "leather", [la, wr], "bracers")
        add(BODY, B.torus(0.133, 0.022, 14, 5), "brass", la, "bracers", m=M((0.70 * s, 0, 1.107), (0, 90, 0)))
        add(BODY, B.torus(0.115, 0.018, 14, 5), "leather_dark", la, "bracers", m=M((0.53 * s, 0.01, 1.107), (0, 90, 0)))
        # big gloved fist + thumb
        add(BODY, B.sphere((0.14, 0.14, 0.145), 12, 8), "leather", hd, "gloves", m=M((0.85 * s, -0.005, 1.09)))
        add(BODY, B.sphere((0.055, 0.05, 0.07), 8, 6), "leather", hd, "gloves", m=M((0.80 * s, -0.13, 1.12)))
        add(BODY, B.cyl((0.73 * s, 0, 1.105), (0.78 * s, 0, 1.105), 0.135, 0.13, 12), "leather_dark", [wr, hd], "gloves")


def build_pauldrons():
    for s, sd in ((1, "l"), (-1, "r")):
        w = {"chest": 0.55, f"upperarm.{sd}": 0.45}
        top = M((0.36 * s, 0.0, 1.27), (0, -20 * s, 0))
        add(BODY, B.box((0.36, 0.44, 0.19), 0.06, 1), "stone_armour", w, "stone_armour", smooth=False, m=top.copy())
        mid = M((0.50 * s, 0.0, 1.14), (0, -50 * s, 0))
        add(BODY, B.box((0.28, 0.40, 0.15), 0.05, 1), "stone_armour", w, "stone_armour", smooth=False, m=mid.copy())
        low = M((0.56 * s, 0.0, 1.00), (0, -72 * s, 0))
        add(BODY, B.box((0.18, 0.32, 0.12), 0.04, 1), "stone_armour", {f"upperarm.{sd}": 1.0}, "stone_armour",
            smooth=False, m=low.copy())
        # cyan crack inlays on the top and middle blocks
        for (cx, cy, ang, ln) in ((0.04, -0.10, 35, 0.14), (-0.06, 0.07, -20, 0.12), (0.09, 0.12, 70, 0.08)):
            add(BODY, B.box((ln, 0.026, 0.016), 0.0), "team_glow", w, "team_glow", smooth=False,
                m=top @ M((cx * s, cy, 0.096), (0, 0, ang * s)))
        add(BODY, B.box((0.10, 0.024, 0.016), 0.0), "team_glow", w, "team_glow", smooth=False,
            m=mid @ M((0.02 * s, -0.08, 0.076), (0, 0, 50 * s)))
        # brass ring rivets at the shoulder joint (front face)
        add(BODY, B.torus(0.06, 0.024, 14, 5), "brass", w, "stone_armour", m=M((0.40 * s, -0.235, 1.15), (90, 0, 0)))
        add(BODY, B.cyl((0.40 * s, -0.22, 1.15), (0.40 * s, -0.25, 1.15), 0.036, 0.036, 10), "brass", w,
            "stone_armour", smooth=False)


def build_head():
    H = "head"
    add(BODY, B.sphere((0.295, 0.27, 0.29), 24, 16), "skin", H, "skin", m=M((0, -0.01, 1.52)))
    add(BODY, B.sphere((0.235, 0.22, 0.16), 18, 10), "skin", H, "skin", m=M((0, -0.05, 1.39)))
    for s in (1, -1):
        add(BODY, B.sphere((0.028, 0.045, 0.06), 8, 6), "skin", H, "skin", m=M((0.268 * s, 0.03, 1.47)))
    # eyes: big brown eyes, little sclera, white highlights
    for s in (1, -1):
        add(BODY, B.sphere((0.066, 0.022, 0.082), 12, 8), "eye_white", H, "face", m=M((0.098 * s, -0.238, 1.475)))
        add(BODY, B.sphere((0.054, 0.02, 0.07), 10, 6), "iris", H, "face", m=M((0.094 * s, -0.247, 1.47)))
        add(BODY, B.sphere((0.03, 0.012, 0.04), 8, 6), "pupil", H, "face", m=M((0.092 * s, -0.259, 1.465)))
        add(BODY, B.sphere((0.02, 0.008, 0.02), 6, 4), "eye_white", H, "face", m=M((0.075 * s, -0.266, 1.50)))
        # thick confident brows
        add(BODY, B.box((0.125, 0.035, 0.045), 0.012, 1), "brow", H, "face", smooth=False,
            m=M((0.105 * s, -0.258, 1.578), (0, -14 * s, 16 * s)))
    # closed-mouth smirk (higher on the character's left)
    smirk = [(-0.07, 0.006), (-0.025, -0.013), (0.03, -0.011), (0.08, 0.018), (0.03, 0.002), (-0.025, -0.002)]
    add(BODY, B.prism(smirk, 0.03), "mouth", H, "face", smooth=False, m=M((0.008, -0.262, 1.355), (90, 0, 0)))


def build_hair():
    H = "head"
    add(BODY, B.sphere((0.295, 0.30, 0.215), 18, 10), "hair", H, "hair", m=M((0, 0.085, 1.65)))
    add(BODY, B.sphere((0.28, 0.21, 0.20), 14, 8), "hair", H, "hair", m=M((0, 0.12, 1.47)))
    clumps = [  # (base, tip, radius, bend): big spiky clumps; bangs over the forehead, sides, back
        ((-0.12, -0.17, 1.80), (-0.25, -0.31, 1.585), 0.10, (-0.03, -0.04, 0.03)),
        ((-0.02, -0.21, 1.82), (-0.07, -0.335, 1.60), 0.10, (0.0, -0.04, 0.03)),
        ((0.09, -0.20, 1.81), (0.17, -0.32, 1.605), 0.10, (0.02, -0.04, 0.03)),
        ((0.20, -0.14, 1.78), (0.31, -0.25, 1.58), 0.095, (0.03, -0.03, 0.03)),
        ((0.24, -0.02, 1.74), (0.36, -0.06, 1.58), 0.085, (0.04, 0, 0.02)),
        ((-0.24, -0.02, 1.74), (-0.36, -0.06, 1.58), 0.085, (-0.04, 0, 0.02)),
        ((0.24, 0.15, 1.72), (0.38, 0.22, 1.56), 0.09, (0.02, 0.02, 0.03)),
        ((-0.24, 0.15, 1.72), (-0.38, 0.22, 1.56), 0.09, (-0.02, 0.02, 0.03)),
        ((0.12, -0.05, 1.84), (0.31, -0.10, 1.80), 0.09, (0.02, 0, 0.04)),
        ((-0.12, -0.05, 1.84), (-0.31, -0.11, 1.80), 0.09, (-0.02, 0, 0.04)),
        ((0.0, 0.30, 1.60), (0.0, 0.43, 1.42), 0.10, (0, 0.03, 0.02)),
        ((0.16, 0.26, 1.60), (0.24, 0.38, 1.43), 0.09, (0.02, 0.03, 0.0)),
        ((-0.16, 0.26, 1.60), (-0.24, 0.38, 1.43), 0.09, (-0.02, 0.03, 0.0)),
    ]
    for b, t, r, bend in clumps:
        add(BODY, B.spike(b, t, r, 6, bend), "hair", H, "hair", smooth=False)
    # high topknot: tuft on the crown, brass ring, big spiky flare fanning up and back
    T = "topknot"
    # (bible p06 three-quarter and profile: the ponytail sits at the back-top and flares up and back)
    add(BODY, B.sphere((0.12, 0.13, 0.11), 12, 8), "hair", H, T, m=M((0, 0.15, 1.82)))
    add(BODY, B.torus(0.088, 0.034, 16, 6), "brass", H, T, m=M((0, 0.17, 1.89), (-35, 0, 0)))
    tuft = [((0.0, 0.17, 1.91), (0.04, 0.06, 2.13), 0.08, (0.02, -0.04, 0.0)),
            ((-0.02, 0.19, 1.91), (-0.19, 0.22, 2.05), 0.075, (-0.03, 0, 0.03)),
            ((0.02, 0.19, 1.91), (0.20, 0.25, 2.04), 0.075, (0.03, 0, 0.03)),
            ((0.0, 0.21, 1.90), (0.02, 0.44, 1.98), 0.08, (0, 0.03, 0.06)),
            ((0.0, 0.20, 1.91), (-0.07, 0.32, 2.14), 0.075, (0, 0.0, 0.03)),
            ((0.01, 0.18, 1.91), (0.12, 0.13, 2.14), 0.07, (0.02, 0, 0.01))]
    for b, t, r, bend in tuft:
        add(BODY, B.spike(b, t, r, 6, bend), "hair", H, T, smooth=False)


# ===================================================================================================== held items
def held_matrix(poses, slot, origin, z_axis, y_hint):
    """Rest-space matrix for an item that must sit at frame(origin, z, y) in the display pose."""
    rest = B.arm.data.bones[slot].matrix_local
    return rest @ poses[slot].inverted() @ kit.frame(origin, z_axis, y_hint)


def build_shield(poses):
    """Round shield, the class identifier. Canonical: face normal +Z, up +Y; outer radius 0.625."""
    W = "handslot.l"
    parts = []

    def p(bm, color, feat, smooth=False, m=None):
        parts.append((bm, color, feat, smooth, m))

    # slightly domed wood face
    p(B.lathe([(0.53, -0.04), (0.53, 0.03), (0.40, 0.055), (0.20, 0.07)], 28), "wood", "shield")
    for x in (-0.33, -0.11, 0.11, 0.33):  # vertical plank seams
        h = 2 * math.sqrt(max(0.0, 0.5 ** 2 - x ** 2)) * 0.96
        p(B.box((0.022, h, 0.012), 0.0), "wood_seam", "shield", m=M((x, 0, 0.034 + 0.03 * (1 - abs(x) / 0.5))))
    n_seg = 9
    for k in range(n_seg):  # segmented grey stone rim: big chunky slabs
        a = 2 * math.pi * (k + 0.5) / n_seg
        r = 0.565
        sx = 2 * math.pi * r / n_seg * rng.uniform(0.86, 0.94)
        sz = rng.uniform(0.15, 0.18)
        tilt = rng.uniform(-6, 6)
        p(B.box((sx, 0.18, sz), 0.04, 1), "stone_armour", "shield",
          m=M((r * math.cos(a), r * math.sin(a), 0.0), (tilt, 0, math.degrees(a) + 90)))
        if k in (1, 4, 7):  # cyan crack inlays on the rim
            p(B.box((sx * 0.45, 0.018, 0.012), 0.0), "team_glow", "team_glow",
              m=M((r * math.cos(a), r * math.sin(a), sz / 2 + 0.002), (0, 0, math.degrees(a) + 90 + rng.uniform(-25, 25))))
    # core: navy ring, brass ring, glowing cyan core (team glow)
    p(B.cyl((0, 0, 0.03), (0, 0, 0.065), 0.215, 0.205, 24), "navy", "shield")
    p(B.torus(0.175, 0.034, 24, 6), "brass", "shield", True, M((0, 0, 0.07)))
    p(B.cyl((0, 0, 0.05), (0, 0, 0.10), 0.13, 0.12, 20), "team_glow", "team_glow")
    p(B.torus(0.075, 0.016, 16, 5), "brass", "shield", True, M((0, 0, 0.102)))
    # back straps
    for y in (-0.12, 0.12):
        p(B.box((0.34, 0.06, 0.03), 0.008), "leather_dark", "shield", m=M((0, y, -0.05)))

    # display pose (Idle): on the left arm, face turned ~55 deg from the front view toward the viewer's right
    origin = (0.665, -0.16, 0.80)
    normal = (0.76, -0.65, 0.0)
    T = held_matrix(poses, W, origin, normal, (0, 0, 1))
    for bm, color, feat, smooth, m in parts:
        mm = T @ (m if m is not None else Matrix.Identity(4))
        add(SHIELD, bm, color, W, feat, smooth=smooth, m=mm)


def build_cleaver(poses):
    """Stone cleaver. Canonical: grip at origin, pommel +Y, blade toward -Y, cutting edge +X, faces +-Z."""
    W = "handslot.r"
    parts = []

    def p(bm, color, feat, smooth=False, m=None):
        parts.append((bm, color, feat, smooth, m))

    blade = [(-0.065, -0.09), (0.09, -0.10), (0.175, -0.15), (0.19, -0.42), (0.185, -0.74), (0.13, -0.83),
             (0.04, -0.82), (0.0, -0.77), (-0.045, -0.79), (-0.07, -0.68), (-0.05, -0.60), (-0.08, -0.48),
             (-0.055, -0.39), (-0.085, -0.28), (-0.06, -0.18)]
    p(B.prism(blade, 0.09, bevel=0.014), "stone_blade", "cleaver")
    edge = [(0.165, -0.14), (0.225, -0.17), (0.215, -0.30), (0.24, -0.44), (0.225, -0.60), (0.235, -0.74),
            (0.15, -0.88), (0.10, -0.85), (0.17, -0.74), (0.185, -0.42), (0.18, -0.18)]
    p(B.prism(edge, 0.05), "team_glow", "team_glow")
    for (x, y, ang) in ((0.05, -0.30, 30), (0.10, -0.56, -40), (0.0, -0.64, 70)):  # chips in the stone face
        for z in (0.047, -0.047):
            p(B.box((0.07, 0.02, 0.01), 0.0), "team_glow", "team_glow", m=M((x, y, z), (0, 0, ang)))
    p(B.cyl((0, 0.13, 0), (0, -0.10, 0), 0.036, 0.036, 10), "leather", "cleaver", True)
    for y in (0.08, 0.02, -0.04):
        p(B.torus(0.038, 0.012, 10, 4), "leather_dark", "cleaver", True, M((0, y, 0), (90, 0, 0)))
    p(B.sphere((0.05, 0.05, 0.05), 10, 6), "brass", "cleaver", True, M((0, 0.15, 0)))
    p(B.box((0.17, 0.045, 0.09), 0.012), "brass", "cleaver", m=M((0.02, -0.095, 0)))

    origin = poses[W].translation.copy()
    down = Vector((-0.50, -0.38, -0.78)).normalized()       # blade points low, out to the viewer's left, forward
    y_axis = -down
    x_hint = Vector((0, 0, -1))
    x_axis = (x_hint - y_axis * x_hint.dot(y_axis)).normalized()  # cutting edge faces down
    z_axis = x_axis.cross(y_axis)
    T = held_matrix(poses, W, origin, z_axis, y_axis)
    for bm, color, feat, smooth, m in parts:
        mm = T @ (m if m is not None else Matrix.Identity(4))
        add(CLEAVER, bm, color, W, feat, smooth=smooth, m=mm)


# ===================================================================================================== main
def main():
    global B
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version = 0      # no warrior.blend1
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    arm = rig.load_kaykit_rig()
    rig.dump_joints(arm, OUT_JOINTS)
    poses = rig.pose_matrices(arm, DISPLAY_ACTION, DISPLAY_FRAME)

    mat = toon.make_toon_material("CM_Toon")
    B = kit.Builder(arm)
    build_legs()
    build_torso()
    build_arms()
    build_pauldrons()
    build_head()
    build_hair()
    build_shield(poses)
    build_cleaver(poses)

    coll = bpy.data.collections.new("Warrior")
    scene.collection.children.link(coll)
    for c in list(arm.users_collection):
        c.objects.unlink(arm)
    coll.objects.link(arm)
    objs = B.finalize(coll, mat)

    # leave the source scene showing the display pose (the FBX is written in rest pose regardless)
    arm.animation_data_create()
    arm.animation_data.action = bpy.data.actions[DISPLAY_ACTION]
    scene.frame_set(DISPLAY_FRAME)

    report = {"objects": {}, "bones": len(arm.data.bones), "display_pose": [DISPLAY_ACTION, DISPLAY_FRAME]}
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

    bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND, compress=True)
    export.export_fbx(OUT_FBX, arm, [objs[BODY], objs[SHIELD], objs[CLEAVER]])
    with open(OUT_REPORT, "w") as f:
        json.dump(report, f, indent=1)
        f.write("\n")
    print("BUILD_REPORT " + json.dumps(report))


main()
