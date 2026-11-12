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
from bpy_extras import anim_utils
from mathutils import Matrix, Quaternion, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.dont_write_bytecode = True
sys.path.insert(0, HERE)

from cm_art import export, face, kit, palette, rig, toon  # noqa: E402

SEED = 606
OUT_BLEND = os.path.join(HERE, "warrior.blend")
OUT_FBX = os.path.join(os.path.dirname(HERE), "Warrior.fbx")
OUT_FACE = os.path.join(os.path.dirname(HERE), "Warrior_Face.png")   # 256 x 256 painted face decal (Unity imports it)
OUT_JOINTS = os.path.join(HERE, "rig_kaykit41.json")
OUT_REPORT = os.path.join(HERE, "build_report.json")
DISPLAY_ACTION, DISPLAY_FRAME = "Idle", 1

rng = random.Random(SEED)
B = None  # kit.Builder
BODY, SHIELD, CLEAVER = "Warrior_Body", "Warrior_Shield", "Warrior_Cleaver"
M = kit.mat_from


def add(obj, bm, color, bones, feat, smooth=True, m=None, face_uv=None, grad=kit.GRADIENT):
    B.add(obj, bm, color, bones, feat=feat, smooth=smooth, m=m, face_uv=face_uv, grad=grad)


# ===================================================================================================== body
FACE_UV = (0.0, -0.06, 1.46, 0.25, 0.25)    # cx, cy, cz, half width, half height of the painted face projection


def groove(name):
    """Colour of a modelled inner crease: the region colour 25 % darker (reads as a 1 to 1.5 px line)."""
    return palette.vcol_scaled(name, 0.75)


def build_legs():
    for s, sd in ((1, "l"), (-1, "r")):
        lx = 0.171 * s
        up, lo, ft, to = f"upperleg.{sd}", f"lowerleg.{sd}", f"foot.{sd}", f"toes.{sd}"
        # baggy cream trousers: wide bulge at the thigh, gathered into the shin wrap (bottom-heavy read)
        prof = [(0.13, 0.25), (0.19, 0.30), (0.225, 0.39), (0.22, 0.49), (0.19, 0.61)]
        add(BODY, B.lathe(prof, 14), "trouser", ["hips", up, lo], "clothes", m=M((lx * 1.10, 0.0, 0)))
        add(BODY, B.cyl((lx * 1.08, 0.01, 0.31), (lx * 1.08, 0.02, 0.17), 0.125, 0.12, 12), "wrap_dark", [lo, ft],
            "clothes")
        # grey stone knee guard: rounded slab + brass rivet
        add(BODY, B.chipped(B.slab((0.27, 0.14, 0.24), 0.5, 12, 8), rng, chips=2, lump=0.006), "stone_armour", lo,
            "stone_armour", m=M((lx * 1.10, -0.15, 0.33), (-10, 0, 8 * s)))
        add(BODY, B.sphere((0.042, 0.03, 0.042), 8, 5), "brass", lo, "stone_armour", m=M((lx * 1.10, -0.215, 0.335)))
        # oversized brown boot: shaft, cuff, strap + buckle, rounded foot, grey stone toe cap, dark sole
        add(BODY, B.cyl((lx * 1.10, 0.02, 0.07), (lx * 1.10, 0.02, 0.25), 0.165, 0.155, 14), "boot", [lo, ft],
            "boots")
        add(BODY, B.torus(0.158, 0.036, 14, 6), "leather_dark", lo, "boots", m=M((lx * 1.10, 0.02, 0.245)))
        add(BODY, B.torus(0.168, 0.02, 14, 5), "leather_dark", [lo, ft], "boots", m=M((lx * 1.10, 0.02, 0.14)))
        add(BODY, B.box((0.05, 0.035, 0.055), 0.012), "brass", [lo, ft], "boots", smooth=False,
            m=M((lx * 1.10 + 0.165 * s, 0.0, 0.14)))
        add(BODY, B.slab((0.34, 0.50, 0.18), 0.55, 12, 8), "boot", [ft, to], "boots",
            m=M((lx * 1.12, -0.11, 0.09)))
        add(BODY, B.chipped(B.slab((0.345, 0.18, 0.15), 0.5, 12, 8), rng, chips=1, lump=0.005), "stone_armour", to,
            "stone_armour",
            m=M((lx * 1.12, -0.29, 0.078)))
        add(BODY, B.box((0.34, 0.52, 0.045), 0.02), "leather_dark", [ft, to], "boots", smooth=False,
            m=M((lx * 1.12, -0.115, 0.0225)))


def build_torso():
    add(BODY, B.sphere((0.34, 0.25, 0.21), 16, 10), "trouser", ["hips", "spine"], "clothes", m=M((0, 0.0, 0.62)))
    # navy padded doublet, a barrel that overhangs the belt (fixed navy: the accent-swap keeps it navy)
    add(BODY, B.sphere((0.36, 0.25, 0.31), 18, 12), "navy", ["spine", "chest"], "clothes", m=M((0, 0.01, 0.99)))
    # cream quilted undershirt: a broad panel down the chest, with two quilting grooves
    add(BODY, B.sphere((0.20, 0.13, 0.25), 14, 10), "cream", ["spine", "chest"], "clothes", m=M((0, -0.16, 0.99)))
    # brown leather belt with crease grooves at both edges + big round brass buckle disc
    W = {"hips": 0.5, "spine": 0.5}
    add(BODY, B.cyl((0, 0, 0.75), (0, 0, 0.85), 0.33, 0.34, 22), "leather_dark", W, "belt",
        m=M((0, 0, 0), (0, 0, 0), (1, 0.84, 1)))
    for z, r in ((0.752, 0.333), (0.848, 0.343)):
        add(BODY, B.torus(r, 0.008, 22, 4), groove("leather_dark"), W, "belt", m=M((0, 0, z), (0, 0, 0), (1, 0.84, 1)))
    add(BODY, brass_disc(0.13, 0.06), "brass", W, "belt", m=kit.frame((0, -0.275, 0.80), (0, -1, 0), (0, 0, 1)))
    add(BODY, B.torus(0.092, 0.012, 18, 4), groove("brass"), W, "belt", m=M((0, -0.318, 0.80), (90, 0, 0)))
    add(BODY, brass_disc(0.075, 0.035), "brass", W, "belt",
        m=kit.frame((-0.27, -0.19, 0.79), (-0.62, -0.78, 0), (0, 0, 1)))
    add(BODY, B.slab((0.14, 0.11, 0.15), 0.55, 10, 6), "leather", {"hips": 0.6, "spine": 0.4}, "belt",
        m=M((0.25, -0.21, 0.73), (0, 0, -25)))
    add(BODY, B.box((0.145, 0.115, 0.055), 0.02), "brass", {"hips": 0.6, "spine": 0.4}, "belt", smooth=False,
        m=M((0.25, -0.215, 0.79), (0, 0, -25)))
    # teal tabard strips, front and back, zigzag hem (team cloth), wide enough to read from above
    hem = [(-0.17, 0.0), (0.17, 0.0), (0.18, -0.45), (0.12, -0.53), (0.06, -0.45), (0.0, -0.54),
           (-0.06, -0.45), (-0.12, -0.53), (-0.18, -0.45)]
    for y, ry in ((-0.275, 0), (0.265, 180)):
        add(BODY, B.prism(hem, 0.03, bevel=0.012, seg=1), "team_cloth", "hips", "team_cloth", smooth=False,
            m=M((0, y, 0.82), (90, 0, ry)) @ M((0, 0, 0), (-8, 0, 0)))
    add(BODY, B.cyl((0, 0, 1.15), (0, 0, 1.31), 0.10, 0.095, 12), "skin", ["chest", "head"], "skin")
    # v3 teal scarf (bible p06): a bunched roll of puffy folds around the neck, sitting on the shoulders under the
    # chin (the smirk stays visible), a knot at the front with a tapered end hanging down the chest, a wide back tail
    for k in range(8):
        a = 2 * math.pi * k / 8 + 0.2
        cx, cy = 0.215 * math.cos(a) * 1.08, 0.012 + 0.208 * math.sin(a)
        front = cy < -0.05
        r = (0.138 * rng.uniform(0.92, 1.08), 0.125 * rng.uniform(0.92, 1.08), 0.092 * rng.uniform(0.9, 1.1))
        add(BODY, B.sphere(r, 10, 7), "team_cloth", "chest", "team_cloth",
            m=M((cx, cy, (1.165 if front else 1.205) + 0.015 * math.sin(3 * a)), (0, 0, math.degrees(a))))
    add(BODY, B.torus(0.37, 0.010, 22, 4), groove("team_cloth"), "chest", "team_cloth",
        m=M((0, 0.012, 1.115), (0, 0, 0), (1.08, 1.0, 1.0)))
    add(BODY, B.sphere((0.10, 0.075, 0.085), 10, 6), "team_cloth", "chest", "team_cloth", m=M((0.05, -0.33, 1.13)))
    add(BODY, B.clump((0.06, -0.33, 1.11), (0.12, -0.36, 1.00), (0.15, -0.33, 0.90), 0.17, 0.05, 7,
                      up=(0, -1, 0), prof=(1.0, 1.05, 0.85, 0.5)), "team_cloth", "chest", "team_cloth")
    tail = [(-0.22, 0.0), (0.22, 0.0), (0.21, -0.44), (0.11, -0.53), (0.0, -0.43), (-0.11, -0.53), (-0.21, -0.44)]
    add(BODY, B.prism(tail, 0.035, bevel=0.012, seg=1), "team_cloth", "chest", "team_cloth", smooth=False,
        m=M((0, 0.35, 1.20), (90, 0, 180)) @ M((0, 0, 0), (-6, 0, 0)))


def build_arms():
    for s, sd in ((1, "l"), (-1, "r")):
        ua, la, wr, hd = f"upperarm.{sd}", f"lowerarm.{sd}", f"wrist.{sd}", f"hand.{sd}"
        add(BODY, B.sphere((0.15, 0.15, 0.15), 12, 8), "navy", ["chest", ua], "clothes", m=M((0.25 * s, 0, 1.11)))
        add(BODY, B.cyl((0.22 * s, 0, 1.107), (0.47 * s, 0.01, 1.107), 0.13, 0.115, 12), "navy", ["chest", ua, la],
            "clothes")
        add(BODY, B.sphere((0.115, 0.115, 0.115), 10, 6), "navy", [ua, la], "clothes", m=M((0.46 * s, 0.012, 1.107)))
        add(BODY, B.cyl((0.47 * s, 0.012, 1.107), (0.735 * s, 0, 1.107), 0.12, 0.14, 12), "leather", [la, wr],
            "bracers")
        add(BODY, B.torus(0.138, 0.022, 14, 5), "brass", la, "bracers", m=M((0.70 * s, 0, 1.107), (0, 90, 0)))
        add(BODY, B.torus(0.121, 0.01, 14, 4), groove("leather"), la, "bracers", m=M((0.53 * s, 0.01, 1.107), (0, 90, 0)))
        # big gloved fist + thumb + cuff
        add(BODY, B.slab((0.29, 0.29, 0.31), 0.72, 12, 8), "leather", hd, "gloves", m=M((0.855 * s, -0.005, 1.09)))
        for kz in (-0.07, 0.0, 0.07):        # knuckle row: the fist reads as a hand, not a mitten
            add(BODY, B.sphere((0.05, 0.055, 0.04), 8, 5), "leather", hd, "gloves",
                m=M((0.985 * s, -0.04, 1.09 + kz)))
        add(BODY, B.sphere((0.075, 0.065, 0.09), 8, 6), "leather", hd, "gloves", m=M((0.81 * s, -0.15, 1.12)))
        add(BODY, B.cyl((0.73 * s, 0, 1.105), (0.78 * s, 0, 1.105), 0.14, 0.135, 12), "leather_dark", [wr, hd],
            "gloves")


def crack(m, pts, w=0.045, feat="team_glow", wset=None):
    """Cyan crack inlay: a zigzag ribbon at least 0.04 u wide, standing 0.012 proud of the surface."""
    poly = [(x, y + w / 2) for x, y in pts] + [(x, y - w / 2) for x, y in reversed(pts)]
    add(BODY, B.prism(poly, 0.02), "team_glow", wset, feat, smooth=False, m=m)


def stone(size, chips=2, lump=0.007, e=0.6, u=12, v=8):
    """Chunky hewn stone slab: rounded superellipsoid with chipped corners and a hand-cut lumpiness."""
    return B.chipped(B.slab(size, e, u, v), rng, chips=chips, lump=lump)


def brass_disc(r, h=0.05):
    """Domed brass shoulder/hip plate: a rim, a dome and a centre boss (canonical: face +Z)."""
    return B.lathe([(r, -0.02), (r, h * 0.2), (r * 0.80, h * 0.62), (r * 0.40, h * 0.90), (r * 0.30, h * 1.05),
                    (0.001, h * 1.12)], 13)


def build_pauldrons():
    """v3: two chunky chipped stone blocks per shoulder (a big cap on top, a second block over the upper arm),
    a big domed brass plate on the front of the shoulder, brass rivets on the top block, cyan crack inlays."""
    for s, sd in ((1, "l"), (-1, "r")):
        w = {"chest": 0.55, f"upperarm.{sd}": 0.45}
        top = M((0.31 * s, 0.0, 1.30), (0, -24 * s, 0))
        add(BODY, stone((0.40, 0.47, 0.235), chips=3), "stone_armour", w, "stone_armour", m=top.copy())
        mid = M((0.44 * s, 0.01, 1.15), (0, -60 * s, 0))
        add(BODY, stone((0.31, 0.42, 0.19), chips=2), "stone_armour", w, "stone_armour", m=mid.copy())
        crack(top @ M((0, 0, 0.113)), [(-0.14 * s, -0.16), (-0.05 * s, -0.08), (0.0, -0.13), (0.09 * s, 0.0),
                                       (0.05 * s, 0.08), (0.14 * s, 0.17)], 0.07, wset=w)
        crack(top @ M((0, 0, 0.110)), [(0.13 * s, -0.18), (0.08 * s, -0.11), (0.14 * s, -0.04)], 0.06, wset=w)
        crack(mid @ M((0, 0, 0.093)), [(-0.09 * s, -0.15), (0.0, -0.05), (0.03 * s, 0.05), (0.10 * s, 0.15)], 0.08,
              wset=w)
        # big domed brass plate on the front of the shoulder (bible: one per side, the brightest warm accent)
        disc_m = kit.frame((0.34 * s, -0.215, 1.16), (0.42 * s, -0.90, 0.10), (0, 0, 1))
        add(BODY, brass_disc(0.092), "brass", w, "stone_armour", m=disc_m)
        add(BODY, B.torus(0.070, 0.008, 18, 4), groove("brass"), w, "stone_armour", m=disc_m @ M((0, 0, 0.034)))
        # rivets: brass domes along the top block's outer edge and one on the arm block
        for rx, ry in ((0.13, -0.13), (0.14, 0.12)):
            add(BODY, B.sphere((0.032, 0.032, 0.022), 8, 5), "brass", w, "stone_armour",
                m=top @ M((rx * s, ry, 0.10)))
        add(BODY, B.sphere((0.03, 0.03, 0.02), 8, 5), "brass", w, "stone_armour", m=mid @ M((0.08 * s, 0.10, 0.085)))


def build_head():
    """v3 head (bible p06 front): a wider, softer face. Round skull, a broad jaw, modelled cheek volumes (blush is painted
    on them), a small modelled button nose low between the cheeks, big ears, and modelled brows that sit low enough to
    overlap the top of the painted eyes. Eye and mouth detail stay in the face decal."""
    H = "head"
    add(BODY, B.sphere((0.262, 0.238, 0.212), 24, 16), "skin", H, "skin", m=M((0, -0.01, 1.525)), face_uv=FACE_UV)
    add(BODY, B.sphere((0.245, 0.212, 0.150), 18, 10), "skin", H, "skin", m=M((0, -0.04, 1.415)), face_uv=FACE_UV)
    for s, k in ((1, 1.06), (-1, 1.0)):    # cheeks: the smirk side (character's left) is fuller and higher
        add(BODY, B.sphere((0.098 * k, 0.056, 0.076 * k), 12, 8), "skin", H, "skin",
            m=M((0.136 * s, -0.182, 1.388 + (0.008 if s > 0 else 0.0))), face_uv=FACE_UV)
    add(BODY, B.sphere((0.044, 0.030, 0.031), 10, 6), "skin", H, "skin", m=M((0, -0.246, 1.392)), face_uv=FACE_UV)
    for s in (1, -1):
        add(BODY, B.sphere((0.034, 0.058, 0.074), 8, 6), "skin", H, "skin", m=M((0.262 * s, 0.03, 1.462)))
        add(BODY, B.sphere((0.016, 0.03, 0.04), 6, 4), groove("skin"), H, "skin", m=M((0.284 * s, 0.025, 1.462)))
        # brows: thick tapered dark-brown wedges, inner ends low and close (over the inner lid), outer ends up
        add(BODY, B.clump((0.046 * s, -0.254, 1.534), (0.11 * s, -0.238, 1.562), (0.19 * s, -0.175, 1.576), 0.056,
                          0.032, 7, up=(0, -1, 0.2), prof=(0.85, 1.0, 0.92, 0.62)), "brow", H, "skin", grad=0.0)

def build_hair():
    """v3 hair (bible p06): spiky volume made of separate tapered spikes over a small hidden cap, not a helmet:
    a fringe of six locks curling down over the forehead, side spikes flaring over the ears, crown and nape spikes,
    and a tall topknot spray in a brass ring that leans to the character's left and sweeps back."""
    H = "head"
    add(BODY, B.sphere((0.250, 0.245, 0.150), 13, 7), "hair", H, "hair", m=M((0, 0.035, 1.605)), grad=0.14)
    add(BODY, B.sphere((0.240, 0.185, 0.200), 11, 7), "hair", H, "hair", m=M((0, 0.105, 1.49)), grad=0.14)
    SP = (0.92, 1.0, 0.68, 0.30)          # spike profile: full at the root, sharp tip
    C = [  # (base, control, tip, width, thickness, up)
        # fringe: six locks curling forward and down, tips staggered, two kicking sideways
        ((-0.17, -0.05, 1.715), (-0.24, -0.25, 1.695), (-0.26, -0.24, 1.655), 0.15, 0.075, (0, -1, 0.3)),
        ((-0.10, -0.08, 1.735), (-0.14, -0.28, 1.715), (-0.15, -0.26, 1.665), 0.15, 0.075, (0, -1, 0.3)),
        ((-0.03, -0.09, 1.745), (-0.04, -0.29, 1.725), (-0.07, -0.27, 1.68), 0.15, 0.075, (0, -1, 0.3)),
        ((0.04, -0.09, 1.745), (0.06, -0.29, 1.725), (0.05, -0.27, 1.66), 0.15, 0.075, (0, -1, 0.3)),
        ((0.11, -0.08, 1.735), (0.16, -0.28, 1.715), (0.17, -0.26, 1.67), 0.15, 0.075, (0, -1, 0.3)),
        ((0.17, -0.05, 1.715), (0.25, -0.24, 1.685), (0.27, -0.22, 1.65), 0.15, 0.075, (0, -1, 0.3)),
        # sides: spikes flaring out over the temples and ears
        ((0.20, 0.00, 1.74), (0.31, -0.04, 1.70), (0.31, -0.08, 1.58), 0.15, 0.08, (1, 0, 0.2)),
        ((-0.20, 0.00, 1.74), (-0.31, -0.04, 1.70), (-0.31, -0.08, 1.58), 0.15, 0.08, (-1, 0, 0.2)),
        ((0.20, 0.15, 1.70), (0.30, 0.22, 1.62), (0.30, 0.22, 1.47), 0.15, 0.08, (1, 0.6, 0)),
        ((-0.20, 0.15, 1.70), (-0.30, 0.22, 1.62), (-0.30, 0.22, 1.47), 0.15, 0.08, (-1, 0.6, 0)),
        # nape: three spikes down and back
        ((0.10, 0.22, 1.62), (0.14, 0.34, 1.50), (0.15, 0.34, 1.35), 0.15, 0.08, (0.3, 1, 0)),
        ((-0.10, 0.22, 1.62), (-0.14, 0.34, 1.50), (-0.16, 0.33, 1.35), 0.15, 0.08, (-0.3, 1, 0)),
        ((0.0, 0.25, 1.64), (0.0, 0.37, 1.52), (0.01, 0.36, 1.33), 0.15, 0.08, (0, 1, 0)),
        # back of the crown: two spikes sweeping back under the topknot (the bible's full round back of the head)
        ((0.09, 0.18, 1.72), (0.13, 0.36, 1.71), (0.15, 0.44, 1.62), 0.15, 0.08, (0.2, 1, 0.3)),
        ((-0.09, 0.18, 1.72), (-0.13, 0.36, 1.71), (-0.16, 0.43, 1.61), 0.15, 0.08, (-0.2, 1, 0.3)),
    ]
    for p0, p1, p2, w, t, up in C:
        add(BODY, B.clump(p0, p1, p2, w, t, 6, up=up, prof=SP), "hair", H, "hair", grad=0.18)
    T = "topknot"          # base set 0.08 back of the crown: the spray sweeps back in profile (bible p06)
    add(BODY, B.sphere((0.10, 0.10, 0.08), 12, 8), "hair", H, T, m=M((0, 0.15, 1.74)))
    add(BODY, B.torus(0.080, 0.032, 16, 6), "brass", H, T, m=M((0, 0.155, 1.795), (-20, 0, 0)))
    tuft = [((-0.02, 0.15, 1.830), (-0.05, 0.13, 1.910), (-0.09, 0.15, 1.935), 0.15, 0.085, (0, -1, 0)),
            ((0.01, 0.15, 1.840), (0.04, 0.12, 1.950), (0.08, 0.11, 1.998), 0.16, 0.09, (0, -1, 0)),
            ((0.02, 0.15, 1.840), (0.10, 0.13, 1.940), (0.16, 0.14, 1.945), 0.15, 0.085, (0, -1, 0)),
            ((0.03, 0.15, 1.830), (0.14, 0.16, 1.910), (0.21, 0.18, 1.880), 0.14, 0.08, (0, -1, 0.2)),
            ((0.00, 0.17, 1.820), (0.04, 0.30, 1.930), (0.07, 0.40, 1.920), 0.15, 0.085, (0, 0, 1)),
            ((0.00, 0.18, 1.800), (0.00, 0.34, 1.850), (0.00, 0.44, 1.770), 0.14, 0.08, (0, 0, 1))]
    for p0, p1, p2, w, t, up in tuft:
        add(BODY, B.clump(p0, p1, p2, w, t, 6, up=up, prof=SP), "hair", H, T, grad=0.18)


# ===================================================================================================== held items
def held_matrix(poses, slot, origin, z_axis, y_hint):
    """Rest-space matrix for an item that must sit at frame(origin, z, y) in the display pose."""
    rest = B.arm.data.bones[slot].matrix_local
    return rest @ poses[slot].inverted() @ kit.frame(origin, z_axis, y_hint)


def build_shield(poses):
    """Round shield, the class identifier. Canonical: face normal +Z, up +Y; outer radius 0.625 (1.25 u = 0.626 of
    the 2.0 u body). Glowing core diameter 0.44 = 0.35 of the shield diameter (ART-DIRECTION 3.2)."""
    W = "handslot.l"
    parts = []

    def p(bm, color, feat, smooth=False, m=None):
        parts.append((bm, color, feat, smooth, m))

    # slightly domed wood face of vertical planks with modelled seam grooves
    p(B.lathe([(0.50, -0.04), (0.50, 0.03), (0.38, 0.055), (0.19, 0.07), (0.001, 0.072)], 28), "wood", "shield", True)
    for x in (-0.315, -0.105, 0.105, 0.315):
        h = 2 * math.sqrt(max(0.0, 0.47 ** 2 - x ** 2)) * 0.96
        p(B.box((0.022, h, 0.014), 0.0), "wood_seam", "shield", m=M((x, 0, 0.036 + 0.03 * (1 - abs(x) / 0.5))))
    p(B.torus(0.485, 0.012, 32, 4), groove("wood"), "shield", True, M((0, 0, 0.034)))        # rim crease
    n_seg = 9
    for k in range(n_seg):  # segmented grey stone rim: big chunky rounded slabs
        a = 2 * math.pi * (k + 0.5) / n_seg
        r = 0.535
        sx = 2 * math.pi * r / n_seg * rng.uniform(0.86, 0.93)
        sz = rng.uniform(0.15, 0.18)
        tilt = rng.uniform(-6, 6)
        p(B.slab((sx, 0.18, sz), 0.4, 10, 6), "stone_armour", "shield", True,
          m=M((r * math.cos(a), r * math.sin(a), 0.0), (tilt, 0, math.degrees(a) + 90)))
        if True:  # cyan crack inlay on every rim slab (0.05 u wide), long and short alternating
            p(B.box((sx * (0.62 if k % 2 else 0.42), 0.05, 0.016), 0.0, 1), "team_glow", "team_glow",
              m=M((r * math.cos(a), r * math.sin(a), sz / 2 - 0.004), (0, 0, math.degrees(a) + 90 + rng.uniform(-25, 25))))
    # core: navy ring, brass ring, glowing core (team glow), brass boss ring
    p(B.cyl((0, 0, 0.03), (0, 0, 0.075), 0.30, 0.29, 28), "navy", "shield", True)
    p(B.torus(0.245, 0.028, 28, 6), "brass", "shield", True, M((0, 0, 0.08)))
    p(B.cyl((0, 0, 0.05), (0, 0, 0.105), 0.22, 0.215, 28), "team_glow", "team_glow", True)
    p(B.torus(0.10, 0.018, 18, 5), "brass", "shield", True, M((0, 0, 0.108)))
    for y in (-0.12, 0.12):  # back straps
        p(B.box((0.34, 0.06, 0.03), 0.01, 1), "leather_dark", "shield", m=M((0, y, -0.05)))

    # display pose (Idle): on the left arm, face turned ~55 deg from the front view toward the viewer's right
    origin = (0.53, -0.17, 0.80)
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

    blade = [(-0.07, -0.09), (0.10, -0.10), (0.19, -0.15), (0.205, -0.42), (0.20, -0.74), (0.14, -0.84),
             (0.04, -0.83), (0.0, -0.78), (-0.05, -0.80), (-0.075, -0.69), (-0.055, -0.60), (-0.085, -0.48),
             (-0.06, -0.39), (-0.09, -0.28), (-0.065, -0.18)]
    BL = 0.76        # blade length scale: the bible's fist-to-tip run is ~0.6 u (front view)
    blade = [(x, y * BL) for x, y in blade]
    p(B.prism(blade, 0.10, bevel=0.025, seg=2), "stone_blade", "cleaver")
    edge = [(0.17, -0.14), (0.27, -0.17), (0.26, -0.30), (0.29, -0.44), (0.27, -0.60), (0.28, -0.75),
            (0.17, -0.92), (0.08, -0.87), (0.18, -0.75), (0.19, -0.42), (0.185, -0.18)]
    edge = [(x, y * BL) for x, y in edge]
    p(B.prism(edge, 0.055, bevel=0.012, seg=1), "team_glow", "team_glow")
    for (x, y, ang) in ((0.05, -0.24, 30), (0.10, -0.43, -40), (0.0, -0.50, 70)):  # glowing chips in the faces
        for z in (0.051, -0.051):
            p(B.box((0.08, 0.04, 0.012), 0.0, 1), "team_glow", "team_glow", m=M((x, y, z), (0, 0, ang)))
    p(B.cyl((0, 0.13, 0), (0, -0.10, 0), 0.038, 0.038, 10), "leather", "cleaver", True)
    for y in (0.08, 0.02, -0.04):
        p(B.torus(0.040, 0.012, 10, 4), "leather_dark", "cleaver", True, M((0, y, 0), (90, 0, 0)))
    p(B.sphere((0.055, 0.055, 0.055), 10, 6), "brass", "cleaver", True, M((0, 0.155, 0)))
    p(B.box((0.19, 0.05, 0.11), 0.02), "brass", "cleaver", m=M((0.02, -0.095, 0)))

    origin = poses[W].translation.copy()
    down = Vector((-0.84, -0.30, -0.45)).normalized()       # blade points low, out to the viewer's left, forward
    y_axis = -down
    x_hint = Vector((-0.6, -0.2, -0.75))     # glowing edge faces down and out (seen from the high camera)
    x_axis = (x_hint - y_axis * x_hint.dot(y_axis)).normalized()  # cutting edge faces down
    z_axis = x_axis.cross(y_axis)
    T = held_matrix(poses, W, origin, z_axis, y_axis)
    for bm, color, feat, smooth, m in parts:
        mm = T @ (m if m is not None else Matrix.Identity(4))
        add(CLEAVER, bm, color, W, feat, smooth=smooth, m=mm)


# ===================================================================================================== attitude
# v3 swagger, baked into the KayKit "Idle" clip as a constant per-bone rotation layer (the rig, its rest pose and
# the other 75 clips are untouched): a wide planted stance, chest out, the shield arm off the body, the cleaver arm
# hanging loose and heavy, chin up and head cocked toward the shield side (+X). World-axis rotations about each joint
# head at Idle frame 1 (the same convention as render_warrior.py POSES), applied in order.
ATTITUDE = [
    ("upperleg.l", (0, 1, 0), -11), ("upperleg.r", (0, 1, 0), 11), ("foot.l", (0, 1, 0), 11), ("foot.r", (0, 1, 0), -11),
    ("hips", (0, 0, 1), 6), ("spine", (0, 0, 1), -8), ("chest", (1, 0, 0), -5), ("hips", (0, 1, 0), 2.5),
    ("spine", (0, 1, 0), 2),
    ("upperarm.l", (0, 1, 0), 10), ("upperarm.r", (0, 1, 0), -16), ("lowerarm.r", (1, 0, 0), -10),
    ("head", (1, 0, 0), -6), ("head", (0, 1, 0), 5), ("head", (0, 0, 1), 5),
]


def rotate_joints(arm, rots):
    for bone, axis, deg in rots:
        pb = arm.pose.bones[bone]
        head = pb.matrix.translation.copy()
        r = Matrix.Translation(head) @ Matrix.Rotation(math.radians(deg), 4, Vector(axis)) @ Matrix.Translation(-head)
        pb.matrix = r @ pb.matrix
        bpy.context.view_layer.update()


def bake_attitude(arm, clip, rots):
    """Pre-multiply every rotation key of `clip` by the local delta that `rots` produce at frame 1."""
    scene = bpy.context.scene
    act = bpy.data.actions[clip]
    arm.animation_data_create()
    arm.animation_data.action = act
    scene.frame_set(1)
    bpy.context.view_layer.update()
    old = {pb.name: pb.matrix_basis.copy() for pb in arm.pose.bones}
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.matrix_basis = old[pb.name]
    bpy.context.view_layer.update()
    rotate_joints(arm, rots)
    deltas = {}
    for pb in arm.pose.bones:
        d = pb.matrix_basis.to_quaternion() @ old[pb.name].to_quaternion().inverted()
        if d.angle > 1e-5:
            deltas[pb.name] = d
    cb = anim_utils.action_get_channelbag_for_slot(act, act.slots[0])
    for name, d in deltas.items():
        path = f'pose.bones["{name}"].rotation_quaternion'
        fcs = [cb.fcurves.find(path, index=i) for i in range(4)]
        if any(fc is None for fc in fcs):
            q0 = old[name].to_quaternion()
            fr = act.frame_range
            for i in range(4):
                if fcs[i] is None:
                    fcs[i] = cb.fcurves.ensure(path, index=i, group_name=name)
                    for f in (fr[0], fr[1]):
                        fcs[i].keyframe_points.insert(f, q0[i])
        frames = sorted({round(k.co.x, 4) for fc in fcs for k in fc.keyframe_points})
        newq = {f: d @ Quaternion([fc.evaluate(f) for fc in fcs]) for f in frames}
        for i, fc in enumerate(fcs):
            for k in fc.keyframe_points:
                v = newq[round(k.co.x, 4)][i]
                dv = v - k.co.y
                k.co.y = v
                k.handle_left.y += dv
                k.handle_right.y += dv
            fc.update()
    for pb in arm.pose.bones:
        pb.matrix_basis.identity()
    scene.frame_set(0)
    bpy.context.view_layer.update()
    return sorted(deltas)


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
    baked = bake_attitude(arm, DISPLAY_ACTION, ATTITUDE)
    poses = rig.pose_matrices(arm, DISPLAY_ACTION, DISPLAY_FRAME)

    face_img = face.make_image(bpy, "Warrior_Face", OUT_FACE, FACE_UV[0], FACE_UV[2], FACE_UV[3], FACE_UV[4])
    mat = toon.make_toon_material("CM_Toon", face_img)
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

    report = {"objects": {}, "bones": len(arm.data.bones), "display_pose": [DISPLAY_ACTION, DISPLAY_FRAME],
              "idle_attitude_bones": baked}
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
    zs = [v.co.z for v in objs[BODY].data.vertices]
    report["rest_height_m"] = round(max(zs) - min(zs), 4)
    report["rest_min_z"] = round(min(zs), 4)

    bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND, compress=True)
    export.export_fbx(OUT_FBX, arm, [objs[BODY], objs[SHIELD], objs[CLEAVER]])
    with open(OUT_REPORT, "w") as f:
        json.dump(report, f, indent=1)
        f.write("\n")
    print("BUILD_REPORT " + json.dumps(report))


main()
