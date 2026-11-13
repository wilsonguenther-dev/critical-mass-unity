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
OUT_OUTLINE_W = os.path.join(os.path.dirname(HERE), "Warrior_OutlineW.json")   # v5 sidecar: unlined hair vertices
OUT_REPORT = os.path.join(HERE, "build_report.json")
DISPLAY_ACTION, DISPLAY_FRAME = "Idle", 1

rng = random.Random(SEED)
B = None  # kit.Builder
BODY, SHIELD, CLEAVER = "Warrior_Body", "Warrior_Shield", "Warrior_Cleaver"
M = kit.mat_from


STONE_STEPS = (0.12, 0.30)     # v4: stone reads as form under the flat toon shader: light top, mid, dark underside
HAIR_STEPS = (0.18, 0.28)      # v5: a clearly lighter top plane and darker underside on the locks


# v4: head and hair are modelled at "design size" and then scaled about the chin (smaller head, 3-head proportion as the
# bible draws it); the topknot is lifted after the scale so the figure stays 2.0 u tall. face.paint uses the same map.
HEAD_SCALE = (0.90, 0.90, (0.0, -0.02, 1.27))      # xy factor, z factor, pivot
TOPKNOT_LIFT = 0.09


def head_space():
    kxy, kz, (px, py, pz) = HEAD_SCALE
    return (Matrix.Translation((px, py, pz)) @ Matrix.Diagonal((kxy, kxy, kz, 1.0))
            @ Matrix.Translation((-px, -py, -pz)))


def add(obj, bm, color, bones, feat, smooth=True, m=None, face_uv=None, grad=kit.GRADIENT, steps=None, noao=False):
    if bones == "head":
        m = head_space() @ (m if m is not None else Matrix.Identity(4))
        if feat == "topknot":
            m = Matrix.Translation((0, 0, TOPKNOT_LIFT)) @ m
    if steps is None:
        steps = STONE_STEPS if color in ("stone_armour", "stone_dark", "stone_toe") else (HAIR_STEPS if color == "hair" else True)
    B.add(obj, bm, color, bones, feat=feat, smooth=smooth, m=m, face_uv=face_uv, grad=grad, steps=steps, noao=noao)


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
        add(BODY, B.chipped(B.slab((0.27, 0.14, 0.24), 0.5, 12, 8), rng, chips=2, lump=0.006), "stone_dark", lo,
            "stone_armour", m=M((lx * 1.10, -0.15, 0.33), (-10, 0, 8 * s)))
        add(BODY, brass_disc(0.058, 0.035), "brass", lo, "stone_armour",
            m=kit.frame((lx * 1.10, -0.215, 0.335), (0, -1, 0.15), (0, 0, 1)))
        # oversized brown boot: shaft, cuff, strap + buckle, rounded foot, grey stone toe cap, dark sole
        add(BODY, B.cyl((lx * 1.10, 0.02, 0.07), (lx * 1.10, 0.02, 0.25), 0.165, 0.155, 14), "boot", [lo, ft],
            "boots")
        add(BODY, B.torus(0.158, 0.036, 14, 6), "leather_dark", lo, "boots", m=M((lx * 1.10, 0.02, 0.245)))
        add(BODY, B.torus(0.168, 0.02, 14, 5), "leather_dark", [lo, ft], "boots", m=M((lx * 1.10, 0.02, 0.14)))
        add(BODY, B.box((0.05, 0.035, 0.055), 0.012), "brass", [lo, ft], "boots", smooth=False,
            m=M((lx * 1.10 + 0.165 * s, 0.0, 0.14)))
        add(BODY, B.slab((0.37, 0.53, 0.20), 0.55, 12, 8), "boot", [ft, to], "boots",
            m=M((lx * 1.12, -0.115, 0.10)))
        add(BODY, B.chipped(B.slab((0.375, 0.19, 0.165), 0.5, 12, 8), rng, chips=2, lump=0.005), "stone_toe", to,
            "stone_armour",
            m=M((lx * 1.12, -0.305, 0.085)))
        add(BODY, B.box((0.37, 0.55, 0.045), 0.02), "leather_dark", [ft, to], "boots", smooth=False,
            m=M((lx * 1.12, -0.12, 0.0225)))


def build_torso():
    add(BODY, B.sphere((0.34, 0.25, 0.21), 16, 10), "trouser", ["hips", "spine"], "clothes", m=M((0, 0.0, 0.62)))
    # navy padded doublet, a barrel that overhangs the belt (fixed navy: the accent-swap keeps it navy)
    add(BODY, B.sphere((0.40, 0.27, 0.31), 18, 12), "navy", ["spine", "chest"], "clothes", m=M((0, 0.01, 0.99)))   # v5 broader chest
    # cream quilted undershirt: a broad panel down the chest, with two quilting grooves
    add(BODY, B.sphere((0.165, 0.13, 0.24), 14, 10), "cream", ["spine", "chest"], "clothes", m=M((0, -0.16, 0.99)))
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
    hem = [(-0.12, 0.0), (0.12, 0.0), (0.125, -0.42), (0.06, -0.50), (0.0, -0.43), (-0.06, -0.50),
           (-0.125, -0.42)]      # v4: the bible's narrow tabard
    for y, ry in ((-0.275, 0), (0.265, 180)):
        add(BODY, B.prism(hem, 0.03, bevel=0.012, seg=1), "team_cloth", "hips", "team_cloth", smooth=False,
            m=M((0, y, 0.82), (90, 0, ry)) @ M((0, 0, 0), (-8, 0, 0)))
    add(BODY, B.cyl((0, 0, 1.15), (0, 0, 1.31), 0.10, 0.095, 12), "skin", ["chest", "head"], "skin")
    # v4 teal scarf (bible p06): a compact roll wrapped round the neck, tucked under the chin, a knot at the front on
    # the character's right and ONE tail hanging down the chest. No folds over the pauldrons (no cape, no hood).
    for k in range(8):
        a = 2 * math.pi * k / 8 + 0.2
        cx, cy = 0.250 * math.cos(a), 0.0 + 0.195 * math.sin(a)
        r = (0.145 * rng.uniform(0.94, 1.06), 0.115 * rng.uniform(0.94, 1.06), 0.088 * rng.uniform(0.92, 1.08))
        add(BODY, B.sphere(r, 10, 7), "team_cloth", "chest", "team_cloth",
            m=M((cx, cy, 1.195 + 0.012 * math.sin(3 * a)), (0, 0, math.degrees(a))))
    add(BODY, B.sphere((0.085, 0.065, 0.075), 10, 6), "team_cloth", "chest", "team_cloth", m=M((-0.08, -0.20, 1.18)))
    add(BODY, B.clump((-0.085, -0.22, 1.15), (-0.15, -0.28, 1.03), (-0.19, -0.27, 0.90), 0.14, 0.045, 7,
                      up=(0, -1, 0), prof=(1.0, 1.08, 0.95, 0.6)), "team_cloth", "chest", "team_cloth")


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
        add(BODY, B.slab((0.33, 0.32, 0.34), 0.72, 12, 8), "leather", hd, "gloves", m=M((0.87 * s, -0.005, 1.09)))
        # v4: brass disc on the outside of the elbow (bible)
        add(BODY, brass_disc(0.062, 0.035), "brass", [ua, la], "bracers", m=M((0.46 * s, 0.012, 1.215)))
        for kz in (-0.07, 0.0, 0.07):        # knuckle row: the fist reads as a hand, not a mitten
            add(BODY, B.sphere((0.055, 0.06, 0.045), 8, 5), "leather", hd, "gloves",
                m=M((1.02 * s, -0.04, 1.09 + kz)))
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
        top = M((0.30 * s, 0.0, 1.32), (0, -24 * s, 0))
        add(BODY, stone((0.40, 0.47, 0.235), chips=3), "stone_armour", w, "stone_armour", m=top.copy())
        mid = M((0.44 * s, 0.01, 1.15), (0, -60 * s, 0))
        add(BODY, stone((0.31, 0.42, 0.19), chips=2), "stone_armour", w, "stone_armour", m=mid.copy())
        # v4: small teal chips in the stone (bible), not long glowing stripes
        crack(top @ M((0, 0, 0.124)), [(-0.10 * s, -0.12), (-0.05 * s, -0.08), (-0.01 * s, -0.11)], 0.05, wset=w)
        crack(mid @ M((0, 0, 0.100)), [(0.02 * s, 0.06), (0.06 * s, 0.10)], 0.045, wset=w)
        # big domed brass plate on the front of the shoulder (bible: one per side, the brightest warm accent)
        disc_m = kit.frame((0.34 * s, -0.215, 1.16), (0.42 * s, -0.90, 0.10), (0, 0, 1))
        add(BODY, brass_disc(0.115, 0.06), "brass", w, "stone_armour", m=disc_m)
        add(BODY, B.torus(0.088, 0.009, 18, 4), groove("brass"), w, "stone_armour", m=disc_m @ M((0, 0, 0.040)))
        # rivets: brass domes along the top block's outer edge and one on the arm block
        for rx, ry in ((0.13, -0.13), (0.14, 0.12)):
            add(BODY, B.sphere((0.032, 0.032, 0.022), 8, 5), "brass", w, "stone_armour",
                m=top @ M((rx * s, ry, 0.10)))
        add(BODY, B.sphere((0.03, 0.03, 0.02), 8, 5), "brass", w, "stone_armour", m=mid @ M((0.08 * s, 0.10, 0.085)))


HEAD_C, HEAD_R = (0.0, -0.01, 1.49), (0.250, 0.232, 0.234)


def head_mesh():
    """v4 head (bible p06): ONE smooth, finely divided round mesh, no glued-on cheeks, nose or brows, so the toon bands
    run in clean curves and the outline never draws a line inside the face. A big round cranium, full soft cheeks and
    a small round chin (the lower half narrows gently), the face front a little flatter for the painted decal."""
    import bmesh as _bm
    bm = _bm.new()
    _bm.ops.create_uvsphere(bm, u_segments=36, v_segments=24, radius=1.0)
    rx, ry, rz = HEAD_R
    for v in bm.verts:
        x, y, z = v.co
        if z < 0:                                     # lower half: narrower toward a small, round chin
            t = -z
            x *= 1.0 - 0.20 * t ** 2
            y *= 1.0 - 0.10 * t ** 2
        else:                                         # cranium: slightly fuller above the brow line
            x *= 1.0 + 0.03 * z
        if y < 0:
            y *= 0.92                                 # flatter face front
        v.co = Vector((HEAD_C[0] + x * rx, HEAD_C[1] + y * ry, HEAD_C[2] + z * rz))
    return bm


def build_head():
    H = "head"
    add(BODY, head_mesh(), "skin", H, "skin", face_uv=FACE_UV, grad=0.04, steps=False, noao=True)
    for s in (1, -1):        # simple round ears, set at eye height, the bible's visible ear
        add(BODY, B.sphere((0.050, 0.064, 0.070), 10, 8), "skin", H, "skin", m=M((0.245 * s, 0.035, 1.405)),
            steps=False)
        add(BODY, B.sphere((0.018, 0.034, 0.040), 8, 6), "skin_shadow", H, "skin", m=M((0.283 * s, 0.03, 1.405)),
            steps=False)


def build_hair():
    """v5 hair (bible p06): a few BIG soft chunky locks instead of thin shards. A fat swept fringe that overlaps the
    forehead down to the brows, round locks curling out at the temples in front of the ears, a full round crown and
    nape, and a tall full topknot of six thick leaf-shaped locks fanning from a wide brass band. Warm brown with a
    lighter top plane and a darker underside (HAIR_STEPS + gradient). Interior hull lines are switched off per vertex
    (outline_w, see outline_weights) so lock overlaps do not read as black cracks."""
    H = "head"
    add(BODY, B.sphere((0.268, 0.255, 0.175), 16, 10), "hair", H, "hair", m=M((0, 0.03, 1.62)), grad=0.24)
    add(BODY, B.sphere((0.255, 0.210, 0.220), 14, 9), "hair", H, "hair", m=M((0, 0.10, 1.49)), grad=0.24)
    FP = (1.0, 1.10, 0.86, 0.46)              # chunky lock: full, swelling, then a clear point
    F = (0, -1, 0.35)
    C = [  # (base, control, tip, width, thickness, up)
        # fringe: one big lock sweeping across the forehead to the character's right, one to the left, two outer locks
        ((0.06, -0.12, 1.775), (-0.06, -0.30, 1.720), (-0.17, -0.255, 1.555), 0.26, 0.095, F),
        ((0.08, -0.10, 1.770), (0.17, -0.29, 1.700), (0.22, -0.235, 1.565), 0.22, 0.09, F),
        ((-0.12, -0.06, 1.765), (-0.25, -0.24, 1.690), (-0.30, -0.17, 1.565), 0.20, 0.09, F),
        ((0.17, -0.05, 1.755), (0.28, -0.21, 1.680), (0.31, -0.14, 1.570), 0.18, 0.085, F),
        # round locks curling out at the temples, in front of the ears
        ((0.20, -0.06, 1.69), (0.33, -0.12, 1.62), (0.37, -0.06, 1.53), 0.16, 0.10, (1, 0, 0.2)),
        ((-0.20, -0.06, 1.69), (-0.33, -0.12, 1.62), (-0.37, -0.06, 1.53), 0.16, 0.10, (-1, 0, 0.2)),
        # behind the ears
        ((0.21, 0.12, 1.70), (0.32, 0.19, 1.62), (0.33, 0.20, 1.48), 0.20, 0.13, (1, 0.6, 0)),
        ((-0.21, 0.12, 1.70), (-0.32, 0.19, 1.62), (-0.33, 0.20, 1.48), 0.20, 0.13, (-1, 0.6, 0)),
        # nape and back of the crown
        ((0.08, 0.22, 1.62), (0.13, 0.34, 1.50), (0.14, 0.33, 1.36), 0.20, 0.13, (0.3, 1, 0)),
        ((-0.08, 0.22, 1.62), (-0.13, 0.34, 1.50), (-0.14, 0.33, 1.36), 0.20, 0.13, (-0.3, 1, 0)),
        ((0.09, 0.17, 1.74), (0.13, 0.35, 1.71), (0.15, 0.42, 1.60), 0.20, 0.12, (0.2, 1, 0.3)),
        ((-0.09, 0.17, 1.74), (-0.13, 0.35, 1.71), (-0.15, 0.42, 1.60), 0.20, 0.12, (-0.2, 1, 0.3)),
    ]
    for p0, p1, p2, w, t, up in C:
        add(BODY, B.clump(p0, p1, p2, w, t, 6, up=up, prof=FP), "hair", H, "hair", grad=0.32)
    for sx in (1, -1, 0):     # crown locks flowing from the topknot toward the temples (seen from the high camera)
        add(BODY, B.clump((0.0, 0.07, 1.80), (0.13 * sx, -0.06, 1.81), (0.23 * sx, -0.12, 1.73) if sx else
                          (0.02, -0.17, 1.76), 0.20, 0.08, 6, up=(0, 0, 1), prof=FP), "hair", H, "hair", grad=0.32)
    T = "topknot"
    add(BODY, B.sphere((0.09, 0.09, 0.06), 12, 8), "hair", H, T, m=M((0, 0.10, 1.765)), grad=0.24)
    add(BODY, B.cyl((0, 0.10, 1.752), (0, 0.10, 1.822), 0.092, 0.086, 18), "brass", H, T)     # wide brass band
    add(BODY, B.torus(0.090, 0.007, 18, 4), groove("brass"), H, T, m=M((0, 0.10, 1.787)))
    LP = (0.55, 0.95, 1.05, 0.85, 0.55, 0.28)       # leaf: narrow root, full middle, long pointed tip
    b = (0.0, 0.10, 1.815)
    tuft = [((-0.12, 0.08, 1.880), (-0.26, 0.10, 1.905), 0.17, 0.075, (0, -1, 0)),
            ((-0.06, 0.07, 1.930), (-0.13, 0.07, 1.975), 0.18, 0.08, (0, -1, 0)),
            ((0.02, 0.07, 1.950), (0.03, 0.06, 1.982), 0.18, 0.08, (0, -1, 0)),
            ((0.09, 0.08, 1.930), (0.17, 0.08, 1.972), 0.18, 0.08, (0, -1, 0)),
            ((0.15, 0.10, 1.880), (0.29, 0.13, 1.895), 0.16, 0.075, (0, -1, 0.2)),
            ((0.01, 0.25, 1.880), (0.04, 0.34, 1.840), 0.16, 0.075, (0, 0, 1))]
    for p1, p2, w, t, up in tuft:
        add(BODY, B.clump(b, p1, p2, w, t, 6, up=up, prof=LP), "hair", H, T, grad=0.32)


def outline_weights(ob, hair_feats=("feat_hair", "feat_topknot")):
    """v5: per-vertex outline weight (Blender group outline_w, 1 lined / 0 unlined; Unity bakes it into UV4.x from the
    Warrior_OutlineW.json sidecar). A hair vertex is unlined when, for the views where it lies on a silhouette edge
    (front and the four 55-degree gameplay yaws), what sits right behind it is other hair: its hull would only draw an
    interior crack between locks. Hair edges against skin or empty space keep their line."""
    import bmesh as _bm
    from mathutils.bvhtree import BVHTree
    me = ob.data
    gids = {ob.vertex_groups[n].index for n in hair_feats if n in ob.vertex_groups}
    hair = [any(e.group in gids for e in v.groups) for v in me.vertices]
    bm = _bm.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    _bm.ops.delete(bm, geom=[f for f in bm.faces if not all(hair[v.index] for v in f.verts)], context="FACES_ONLY")
    tree = BVHTree.FromBMesh(bm)
    bm.free()
    c55, s55 = math.cos(math.radians(55)), math.sin(math.radians(55))
    views = [Vector((0, 1, 0))] + [Vector((math.sin(a) * c55, math.cos(a) * c55, -s55)).normalized()
                                   for a in (math.radians(y) for y in (45, 135, 225, 315))]
    g = ob.vertex_groups.new(name="outline_w")
    g.add(list(range(len(me.vertices))), 1.0, "REPLACE")
    unlined = []
    for v in me.vertices:
        if not hair[v.index]:
            continue
        n = v.normal
        edge = [d for d in views if abs(n.dot(d)) < 0.45]
        if not edge:
            continue
        hits = sum(1 for d in edge if tree.ray_cast(v.co + n * 0.03, d, 0.6)[0] is not None)
        if hits >= 0.6 * len(edge):
            unlined.append(v.index)
    g.add(unlined, 0.0, "REPLACE")
    return unlined


def write_outline_sidecar(ob, idx, path):
    """Same format as critical-mass-unity tools/art/outline_weights.py: rest positions of unlined vertices, unity(x,z,y)."""
    pts = []
    for i in idx:
        co = ob.data.vertices[i].co
        pts += [round(co.x, 5), round(co.z, 5), round(co.y, 5)]
    with open(path, "w") as f:
        json.dump({"source": "warrior.blend", "group": "outline_w", "axes": "unity(x,z,y)",
                   "meshes": [{"name": ob.name, "vertices": len(ob.data.vertices), "unlined": pts}]}, f,
                  separators=(",", ":"))


def soften_face_normals(ob, blend=0.85):
    """v4: anime-face shading normals. The face's corner normals lean toward the direction from a point behind the
    head, so the whole face falls in the lit band in the bible framings (no shadow band blotching the cheeks), while
    the silhouette and the sides of the head keep their true normals. Custom normals are written to the FBX."""
    me = ob.data
    hs = head_space()
    far = hs @ Vector((HEAD_C[0], HEAD_C[1] + 0.40, HEAD_C[2] - 0.02))
    c = hs @ Vector(HEAD_C)
    g = ob.vertex_groups.get("feat_skin")
    skin = set()
    for v in me.vertices:
        if g and any(e.group == g.index for e in v.groups):
            p = v.co
            if p.z > 1.25 and abs(p.x) < 0.23 and p.y < c.y + 0.02 and (p - c).length < 0.29:
                skin.add(v.index)
    normals = [n.vector.copy() for n in me.corner_normals]
    for lp in me.loops:
        if lp.vertex_index in skin:
            p = me.vertices[lp.vertex_index].co
            n = normals[lp.index]
            fwd = max(0.0, min(1.0, (c.y - p.y) / 0.20))      # full effect on the face front, fading to the sides
            tgt = ((p - far).normalized() * 0.35 + Vector((0.0, -1.0, 0.30)).normalized()).normalized()
            normals[lp.index] = n.lerp(tgt, blend * fwd).normalized()
    me.normals_split_custom_set(normals)
    return len(skin)


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
    p(B.lathe([(0.50, -0.04), (0.50, 0.03), (0.38, 0.055), (0.19, 0.07), (0.001, 0.072)], 24), "wood", "shield", True)
    for x in (-0.315, -0.105, 0.105, 0.315):
        h = 2 * math.sqrt(max(0.0, 0.47 ** 2 - x ** 2)) * 0.96
        p(B.box((0.022, h, 0.014), 0.0), "wood_seam", "shield", m=M((x, 0, 0.036 + 0.03 * (1 - abs(x) / 0.5))))
    p(B.torus(0.485, 0.012, 32, 4), groove("wood"), "shield", True, M((0, 0, 0.034)))        # rim crease
    n_seg = 9
    p(B.torus(0.535, 0.075, 22, 4), "stone_gap", "shield", True, M((0, 0, -0.01)))     # v5: dark between the rim stones
    for k in range(n_seg):  # segmented grey stone rim: big chunky rounded slabs
        a = 2 * math.pi * (k + 0.5) / n_seg
        r = 0.535
        sx = 2 * math.pi * r / n_seg * rng.uniform(0.76, 0.82)    # v5: separate stones with gaps
        sz = rng.uniform(0.19, 0.22)
        tilt = rng.uniform(-6, 6)
        p(B.chipped(B.slab((sx, 0.20, sz), 0.22, 8, 5), rng, chips=2, lump=0.004), "stone_armour", "shield", False,
          m=M((r * math.cos(a), r * math.sin(a), 0.0), (tilt, 0, math.degrees(a) + 90)))
        if k in (1, 4, 7):  # v4: a small teal chip on three of the nine rim stones (bible), not a stripe on each
            p(B.box((0.075, 0.05, 0.016), 0.0, 1), "team_glow", "team_glow",
              m=M((r * math.cos(a), r * math.sin(a), sz / 2 - 0.004), (0, 0, math.degrees(a) + 90 + rng.uniform(-25, 25))))
    # core: navy ring, brass ring, glowing core (team glow), brass boss ring
    # v4 centre boss (bible): dark iron plate, a glowing teal ring, an iron dome with a small teal centre
    p(B.cyl((0, 0, 0.03), (0, 0, 0.078), 0.26, 0.25, 22), "iron", "shield", True)
    p(B.torus(0.180, 0.030, 22, 5), "team_glow", "team_glow", True, M((0, 0, 0.082)))
    p(brass_disc(0.13, 0.075), "iron", "shield", True, M((0, 0, 0.078)))
    p(B.sphere((0.042, 0.042, 0.022), 10, 6), "team_glow", "team_glow", True, M((0, 0, 0.160)))
    for y in (-0.12, 0.12):  # back straps
        p(B.box((0.34, 0.06, 0.03), 0.01, 1), "leather_dark", "shield", m=M((0, y, -0.05)))

    # display pose (Idle): on the left arm, face turned ~55 deg from the front view toward the viewer's right
    origin = (0.68, -0.12, 0.75)      # v4: out at the side, clear of the torso in the front view (bible)
    normal = (0.80, -0.60, 0.0)
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
    BL = 0.88        # v4: longer blade, tip near the ground as the bible draws it. blade length scale: the bible's fist-to-tip run is ~0.6 u (front view)
    blade = [(x, y * BL) for x, y in blade]
    p(B.prism(blade, 0.10, bevel=0.025, seg=2), "stone_blade", "cleaver")
    edge = [(0.17, -0.14), (0.27, -0.17), (0.26, -0.30), (0.29, -0.44), (0.27, -0.60), (0.28, -0.75),
            (0.17, -0.92), (0.08, -0.87), (0.18, -0.75), (0.19, -0.42), (0.185, -0.18)]
    edge = [(x, y * BL) for x, y in edge]     # v4: the bible's glowing edge only (v3 widened it for a team-% rule)
    p(B.prism(edge, 0.055, bevel=0.012, seg=1), "team_glow", "team_glow")
    p(B.cyl((0, 0.13, 0), (0, -0.10, 0), 0.038, 0.038, 10), "leather", "cleaver", True)
    for y in (0.08, 0.02, -0.04):
        p(B.torus(0.040, 0.012, 10, 4), "leather_dark", "cleaver", True, M((0, y, 0), (90, 0, 0)))
    p(B.sphere((0.055, 0.055, 0.055), 10, 6), "brass", "cleaver", True, M((0, 0.155, 0)))
    p(B.box((0.19, 0.05, 0.11), 0.02), "brass", "cleaver", m=M((0.02, -0.095, 0)))

    origin = poses[W].translation.copy()
    down = Vector((-0.80, -0.25, -0.54)).normalized()       # blade points low, out to the viewer's left, forward
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
    ("upperleg.l", (0, 1, 0), -15), ("upperleg.r", (0, 1, 0), 15), ("foot.l", (0, 1, 0), 15), ("foot.r", (0, 1, 0), -15),
    ("hips", (0, 0, 1), 6), ("spine", (0, 0, 1), -8), ("chest", (1, 0, 0), -5), ("hips", (0, 1, 0), 2.5),
    ("spine", (0, 1, 0), 2),
    ("upperarm.l", (0, 1, 0), 10), ("upperarm.r", (0, 1, 0), -18), ("lowerarm.r", (1, 0, 0), -10),
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

    face_img = face.make_image(bpy, "Warrior_Face", OUT_FACE, FACE_UV[0], FACE_UV[2], FACE_UV[3], FACE_UV[4],
                               head_scale=HEAD_SCALE)
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
    face_verts = soften_face_normals(objs[BODY])
    unlined = outline_weights(objs[BODY])
    write_outline_sidecar(objs[BODY], unlined, OUT_OUTLINE_W)

    # leave the source scene showing the display pose (the FBX is written in rest pose regardless)
    arm.animation_data_create()
    arm.animation_data.action = bpy.data.actions[DISPLAY_ACTION]
    scene.frame_set(DISPLAY_FRAME)

    report = {"objects": {}, "bones": len(arm.data.bones), "display_pose": [DISPLAY_ACTION, DISPLAY_FRAME],
              "idle_attitude_bones": baked, "face_normal_verts": face_verts, "outline_unlined_verts": len(unlined)}
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
