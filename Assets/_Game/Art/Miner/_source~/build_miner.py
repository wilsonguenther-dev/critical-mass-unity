"""Build the Miner (bible p08, CM-CHR-003) from nothing.

  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python-exit-code 1 -P build_miner.py

Deterministic (factory startup, fixed seed). Reuses, unmodified, the shared character helpers on this branch:
Assets/_Game/Art/Characters/_source~/cm_art (KayKit 41-joint rig loader, mesh kit, FBX export settings) and the rig
source kaykit/Knight.glb there. Miner-only code lives in miner_art/.

Outputs (relative to this folder):
  miner.blend              Rig + Miner_Body + Miner_PickHammer (pick state) + Miner_PickHammer_Hammer (hammer state),
                           and the barricade collection (Miner_Barricade_Folded / _Deployed / _Damaged)
  ../Miner.fbx             character: Rig + body + both pick-hammer states (Unity toggles the state objects)
  ../MinerBarricade.fbx    barricade prop: three authored static meshes, one per state
  build_report.json        triangle counts, dimensions, feature groups

Space: Blender rest pose (KayKit T-pose), Z up, faces -Y, character left = +X, metres. Total height 2.0 u from the
boot soles to the tip of the helmet crystal (the helmet dome itself tops out at 1.755, crystal rises 0.25 above it).
"""
import json
import math
import os
import random
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
SHARED = os.path.join(os.path.dirname(os.path.dirname(HERE)), "Characters", "_source~")
sys.dont_write_bytecode = True
sys.path.insert(0, HERE)
sys.path.insert(0, SHARED)

from cm_art import export, kit, rig  # noqa: E402  (shared, read-only)
from miner_art import mkit, mpal, toon3  # noqa: E402

SEED = 803
OUT_BLEND = os.path.join(HERE, "miner.blend")
OUT_FBX = os.path.join(os.path.dirname(HERE), "Miner.fbx")
OUT_BARR_FBX = os.path.join(os.path.dirname(HERE), "MinerBarricade.fbx")
OUT_REPORT = os.path.join(HERE, "build_report.json")
DISPLAY_ACTION, DISPLAY_FRAME = "Idle", 1

rng = random.Random(SEED)
B = None
BODY, PICK, HAMMER = "Miner_Body", "Miner_PickHammer", "Miner_PickHammer_Hammer"
BAR = {"folded": "Miner_Barricade_Folded", "deployed": "Miner_Barricade_Deployed", "damaged": "Miner_Barricade_Damaged"}
M = kit.mat_from
BV = 0.03          # standard bevel (art direction 3.5: 0.02 to 0.04 u)


HEAD_WIDEN = Matrix.Diagonal((1.18, 1.18, 1.0, 1.0))   # bible p08: helmet about half the body width
_post = None


def add(obj, bm, color, bones, feat, smooth=True, m=None, **kw):
    if _post is not None and "post" not in kw:
        kw["post"] = _post
    B.add(obj, bm, color, bones, feat=feat, smooth=smooth, m=m, **kw)


def head_crystal(*a, **kw):
    kw.setdefault("post", _post)
    B.add_crystal(*a, **kw)


def box(size, bevel=BV, seg=1):
    return B.box(size, bevel, seg)


def strap(a, b, width, thick, normal_hint, bevel=None):
    """Box from point a to point b (length axis), `thick` along normal_hint, `width` across."""
    a, b = Vector(a), Vector(b)
    L = (b - a).length
    bm = B.box((width, thick, L), min(0.012, thick * 0.4) if bevel is None else bevel, 1)
    return bm, kit.frame((a + b) / 2, b - a, normal_hint)


def ellipse_pos(theta_deg, rx, ry, z):
    t = math.radians(theta_deg)
    return (rx * math.sin(t), -ry * math.cos(t), z)


# ===================================================================================================== body
def build_legs():
    for s, sd in ((1, "l"), (-1, "r")):
        lx = 0.19 * s
        up, lo, ft, to = f"upperleg.{sd}", f"lowerleg.{sd}", f"foot.{sd}", f"toes.{sd}"
        prof = [(0.12, 0.19), (0.165, 0.25), (0.19, 0.35), (0.195, 0.46), (0.175, 0.58), (0.12, 0.65)]
        add(BODY, B.lathe(prof, 12), "navy_dark", ["hips", up, lo], "trousers", m=M((lx, 0.0, 0)))
        # brass/brown knee guard: brass plate, leather inset, rivet
        kg = M((lx, -0.165, 0.335), (-10, 0, 8 * s))
        add(BODY, box((0.21, 0.09, 0.18), 0.03, 2), "brass", lo, "knee_guard", smooth=False, m=kg)
        add(BODY, box((0.13, 0.03, 0.10), 0.012, 1), "stone_light", lo, "knee_guard", smooth=False,
            m=kg @ M((0, -0.045, 0)))
        B.add_crystal(BODY, lo, "team_glow", kg @ M((0, -0.06, 0), (90, 0, 30)), 0.055, 0.045, seg=6, waist=0.8,
                      cap=0.55)
                # heavy brown boots with brass toe caps
        add(BODY, B.cyl((lx, 0.02, 0.06), (lx, 0.02, 0.25), 0.17, 0.165, 12), "leather", [lo, ft], "boots")
        add(BODY, B.torus(0.172, 0.04, 12, 5), "kerchief", lo, "team_cloth", m=M((lx, 0.02, 0.25)))   # team trim
        add(BODY, box((0.33, 0.44, 0.16), 0.05, 2), "leather", [ft, to], "boots", m=M((lx, -0.08, 0.085)))
        add(BODY, box((0.335, 0.15, 0.135), 0.035, 2), "brass", to, "boots", smooth=False,
            m=M((lx, -0.245, 0.078)))
        add(BODY, box((0.34, 0.46, 0.045), 0.015, 1), "leather_dark", [ft, to], "boots", smooth=False,
            m=M((lx, -0.085, 0.022)))
        add(BODY, box((0.05, 0.03, 0.05), 0.012, 1), "brass", [lo, ft], "boots", smooth=False,
            m=M((lx + 0.0, -0.155, 0.15)))


def build_torso():
    W3 = ["hips", "spine", "chest"]
    add(BODY, B.sphere((0.34, 0.27, 0.17), 16, 8), "navy_dark", ["hips", "spine"], "trousers", m=M((0, 0.0, 0.63)))
    # barrel torso (widest class): navy shirt
    prof = [(0.30, 0.64), (0.40, 0.73), (0.445, 0.86), (0.44, 1.00), (0.39, 1.13), (0.27, 1.235), (0.10, 1.27)]
    add(BODY, B.lathe(prof, 18), "navy", W3, "shirt", m=M((0, 0.0, 0), (0, 0, 0), (1, 0.80, 1)))
    # belt, buckle, pouches
    add(BODY, B.cyl((0, 0, 0.63), (0, 0, 0.765), 0.41, 0.42, 18), "leather", {"hips": 0.5, "spine": 0.5}, "belt",
        m=M((0, 0, 0), (0, 0, 0), (1, 0.82, 1)))
    add(BODY, box((0.15, 0.05, 0.12), 0.02, 1), "brass", {"hips": 0.5, "spine": 0.5}, "belt", smooth=False,
        m=M((0, -0.345, 0.70)))
    B.add_crystal(BODY, {"hips": 0.5, "spine": 0.5}, "team_glow", M((0, -0.365, 0.70), (90, 0, 30)), 0.066, 0.05,
                  seg=6, waist=0.8, cap=0.55)
    for th, sz in ((42, (0.14, 0.11, 0.15)), (-42, (0.14, 0.11, 0.15)), (88, (0.12, 0.10, 0.14)),
                   (-88, (0.12, 0.10, 0.14))):
        p = ellipse_pos(th, 0.44, 0.36, 0.665)
        pm = M(p, (0, 0, th))
        add(BODY, box(sz, 0.028, 1), "leather", {"hips": 0.6, "spine": 0.4}, "belt", m=pm)
        add(BODY, box((sz[0] + 0.01, sz[1] + 0.012, 0.05), 0.014, 1), "brass", {"hips": 0.6, "spine": 0.4}, "belt",
            smooth=False, m=pm @ M((0, -0.002, sz[2] / 2 - 0.02)))
    # chest plate: brass frame, 1 px dark groove, cream plate, hex crystal in a brass bezel
    P = M((0, -0.322, 0.965), (-11, 0, 0))
    add(BODY, box((0.37, 0.07, 0.37), 0.03, 2), "brass", "chest", "chest_plate", smooth=False, m=P)
    add(BODY, box((0.318, 0.07, 0.318), 0.0, 1), "wood_dark", "chest", "chest_plate", smooth=False,
        m=P @ M((0, -0.012, 0)))
    add(BODY, box((0.30, 0.07, 0.30), 0.03, 2), "cream", "chest", "chest_plate", smooth=False,
        m=P @ M((0, -0.024, 0)))
    for x, z in ((0.155, 0.155), (-0.155, 0.155), (0.155, -0.155), (-0.155, -0.155)):
        add(BODY, B.sphere((0.022, 0.016, 0.022), 6, 4), "brass_dark", "chest", "chest_plate",
            m=P @ M((x, -0.038, z)))
    add(BODY, B.lathe([(0.135, 0.0), (0.14, 0.025), (0.122, 0.04)], 6), "brass", "chest", "chest_plate",
        smooth=False, m=P @ M((0, -0.058, 0), (90, 0, 30)))
    B.add_crystal(BODY, "chest", "team_glow", P @ M((0, -0.075, 0), (90, 0, 30)), 0.112, 0.08, seg=6, waist=0.82,
                  cap=0.55)
    # shoulder straps (leather, brass shoulder plate, team gem studs), over the shoulder to the crate
    for s, sd in ((1, "l"), (-1, "r")):
        w = {"chest": 0.8, f"upperarm.{sd}": 0.2}
        pts = [(0.17 * s, -0.355, 1.10), (0.21 * s, -0.25, 1.23), (0.22 * s, -0.02, 1.29), (0.22 * s, 0.22, 1.25),
               (0.21 * s, 0.33, 1.12)]
        for a, b in zip(pts[:-1], pts[1:]):
            n = Vector((0, -(b[2] - a[2]), b[1] - a[1])).normalized()
            if n.z < 0:
                n = -n
            bm, mm = strap(a, b, 0.10, 0.035, n)
            add(BODY, bm, "leather", w, "straps", smooth=False, m=mm)
        add(BODY, box((0.13, 0.17, 0.035), 0.012, 1), "brass", w, "straps", smooth=False,
            m=M((0.22 * s, -0.10, 1.295), (-8, 0, 0)))
        for (y, z, tilt) in ((-0.12, 1.32, -20), (-0.285, 1.195, -55)):
            B.add_crystal(BODY, w, "team_glow", M((0.215 * s, y, z), (tilt, 0, 30)), 0.058, 0.05, seg=6,
                          waist=0.8, cap=0.55)


def build_arms():
    for s, sd in ((1, "l"), (-1, "r")):
        ua, la, wr, hd = f"upperarm.{sd}", f"lowerarm.{sd}", f"wrist.{sd}", f"hand.{sd}"
        add(BODY, B.sphere((0.155, 0.155, 0.155), 10, 6), "navy", ["chest", ua], "shirt", m=M((0.27 * s, 0, 1.11)))
        add(BODY, B.cyl((0.24 * s, 0, 1.107), (0.47 * s, 0.012, 1.107), 0.145, 0.125, 12), "navy",
            ["chest", ua, la], "shirt")
        add(BODY, B.sphere((0.125, 0.125, 0.125), 8, 6), "navy", [ua, la], "shirt", m=M((0.465 * s, 0.012, 1.107)))
        add(BODY, B.cyl((0.47 * s, 0.012, 1.107), (0.70 * s, 0, 1.107), 0.13, 0.145, 12), "leather_dark", [la, wr], "gloves")
        # cream rolled sleeve on the upper arm (bible: light upper arms), brass stud band
        add(BODY, B.cyl((0.30 * s, 0.004, 1.107), (0.43 * s, 0.01, 1.107), 0.16, 0.15, 10), "cream", [ua, "chest"],
            "sleeve", m=None)
        add(BODY, B.torus(0.155, 0.042, 12, 5), "kerchief", ua, "team_cloth", m=M((0.42 * s, 0.01, 1.107), (0, 90, 0)))
        # huge dark-brown gloves: flared cuff, brass band, fist, knuckle ridge, thumb
        add(BODY, B.cyl((0.62 * s, 0, 1.107), (0.80 * s, 0, 1.107), 0.15, 0.18, 12), "leather_dark", [la, wr, hd],
            "gloves")
        add(BODY, B.torus(0.158, 0.045, 12, 4), "kerchief", [la, wr], "team_cloth", m=M((0.61 * s, 0, 1.107), (0, 90, 0)))
        add(BODY, B.sphere((0.175, 0.17, 0.18), 12, 8), "leather_dark", hd, "gloves", m=M((0.90 * s, -0.01, 1.09)))
        add(BODY, box((0.10, 0.20, 0.10), 0.04, 1), "leather_dark", hd, "gloves", m=M((0.99 * s, -0.03, 1.12)))
        add(BODY, B.sphere((0.07, 0.065, 0.085), 8, 6), "leather_dark", hd, "gloves", m=M((0.84 * s, -0.16, 1.13)))


def build_neck():
    add(BODY, B.cyl((0, 0, 1.17), (0, 0, 1.32), 0.11, 0.105, 12), "skin", ["chest", "head"], "skin")
    # blue neckerchief (team cloth): thick roll, front knot, hanging point
    add(BODY, B.torus(0.255, 0.11, 18, 7), "kerchief", "chest", "team_cloth",
        m=M((0, -0.015, 1.245), (8, 0, 0), (1.06, 0.95, 0.8)))
    add(BODY, B.sphere((0.10, 0.07, 0.07), 10, 6), "kerchief", "chest", "team_cloth", m=M((0.14, -0.25, 1.15)))


def build_head():
    global _post
    _post = HEAD_WIDEN
    H = "head"
    add(BODY, B.sphere((0.28, 0.26, 0.27), 18, 12), "skin", H, "skin", m=M((0, -0.01, 1.44)))
    for s in (1, -1):
        add(BODY, B.sphere((0.035, 0.05, 0.065), 8, 6), "skin_shadow", H, "skin", m=M((0.262 * s, 0.03, 1.45)))
        # determined eyes under heavy brows
        add(BODY, B.sphere((0.062, 0.025, 0.066), 10, 6), "eye_white", H, "face", m=M((0.102 * s, -0.245, 1.468)))
        add(BODY, B.sphere((0.04, 0.018, 0.048), 8, 6), "pupil", H, "face", m=M((0.095 * s, -0.262, 1.462)))
        add(BODY, B.sphere((0.015, 0.007, 0.015), 6, 4), "eye_white", H, "face", m=M((0.082 * s, -0.279, 1.48)))
        add(BODY, box((0.15, 0.06, 0.06), 0.02, 1), "brow", H, "face", smooth=False,
            m=M((0.11 * s, -0.25, 1.53), (0, -16 * s, 0)))
    # big round red nose
    add(BODY, B.sphere((0.085, 0.075, 0.075), 12, 8), "nose", H, "face", m=M((0, -0.30, 1.405)))
    # beard: base mass + 9 sculpted tapered clumps; mustache 2 clumps; grin
    add(BODY, B.sphere((0.27, 0.20, 0.16), 12, 7), "beard", H, "beard", m=M((0, -0.03, 1.29)))
    clumps = [
        ((0.25, -0.07, 1.40), (0.275, -0.13, 1.23), 0.085, (0.03, -0.02, 0.0)),
        ((-0.25, -0.07, 1.40), (-0.275, -0.13, 1.23), 0.085, (-0.03, -0.02, 0.0)),
        ((0.255, 0.0, 1.52), (0.27, -0.06, 1.37), 0.06, (0.02, 0, 0)),
        ((-0.255, 0.0, 1.52), (-0.27, -0.06, 1.37), 0.06, (-0.02, 0, 0)),
        ((0.17, -0.18, 1.30), (0.19, -0.26, 1.14), 0.09, (0.02, -0.03, 0)),
        ((-0.17, -0.18, 1.30), (-0.19, -0.26, 1.14), 0.09, (-0.02, -0.03, 0)),
        ((0.065, -0.21, 1.27), (0.07, -0.30, 1.11), 0.09, (0.0, -0.03, 0)),
        ((-0.065, -0.21, 1.27), (-0.07, -0.30, 1.11), 0.09, (0.0, -0.03, 0)),
        ((0.0, -0.20, 1.25), (0.0, -0.28, 1.09), 0.085, (0, -0.03, 0)),
    ]
    for b, t, r, bend in clumps:
        add(BODY, B.spike(b, t, r, 8, bend), "beard", H, "beard", smooth=True)
    for s in (1, -1):
        add(BODY, B.spike((0.03 * s, -0.29, 1.355), (0.165 * s, -0.25, 1.315), 0.045, 7, (0.0, -0.02, 0.012)), "beard",
            H, "beard", smooth=True)
    grin = [(-0.085, 0.012), (-0.04, -0.03), (0.04, -0.03), (0.085, 0.012), (0.0, -0.002)]
    add(BODY, B.prism(grin, 0.03), "mouth", H, "face", smooth=False, m=M((0, -0.272, 1.29), (90 - 12, 0, 0)))
    teeth = [(-0.075, 0.010), (0.075, 0.010), (0.06, -0.008), (-0.06, -0.008)]
    add(BODY, B.prism(teeth, 0.03), "teeth", H, "face", smooth=False, m=M((0, -0.278, 1.29), (90 - 12, 0, 0)))


def build_helmet():
    global _post
    _post = HEAD_WIDEN
    H = "head"
    prof = [(0.30, 1.545), (0.335, 1.58), (0.325, 1.635), (0.29, 1.685), (0.22, 1.725), (0.115, 1.748),
            (0.02, 1.755)]
    add(BODY, B.lathe(prof, 20), "helmet", H, "helmet", m=M((0, 0.005, 0)))
    # brim (rounded, a step lighter), groove line, brass trim band + rivets
    add(BODY, B.torus(0.335, 0.05, 20, 5), "stone_light", H, "helmet", m=M((0, -0.012, 1.55), (0, 0, 0),
                                                                            (1.0, 1.07, 0.62)))
    add(BODY, B.torus(0.333, 0.010, 20, 3), "helmet_seam", H, "helmet_detail", m=M((0, 0.004, 1.562)))
    add(BODY, B.torus(0.336, 0.024, 20, 4), "brass", H, "helmet", m=M((0, 0.004, 1.59)))
    for k in range(8):
        a = 2 * math.pi * (k + 0.5) / 8
        add(BODY, B.sphere((0.016, 0.016, 0.016), 5, 3), "brass_dark", H, "helmet_detail",
            m=M((0.358 * math.cos(a), 0.004 + 0.358 * math.sin(a), 1.59)))
    # plate grooves (segmented stone hard hat): 4 meridians as thin strips riding the dome
    pp = [(0.333, 1.60), (0.325, 1.64), (0.29, 1.69)]
    for ang in (30, 90, 150, 210, 270, 330):
        a = math.radians(ang)
        d = Vector((math.cos(a), math.sin(a), 0))
        for (r0, z0), (r1, z1) in zip(pp[:-1], pp[1:]):
            p0 = d * (r0 + 0.003) + Vector((0, 0.005, z0))
            p1 = d * (r1 + 0.003) + Vector((0, 0.005, z1))
            n = (d * (z1 - z0) - Vector((0, 0, 1)) * (r1 - r0)).normalized()
            bm, mm = strap(p0, p1, 0.014, 0.012, n, bevel=0.0)
            add(BODY, bm, "helmet_seam", H, "helmet_detail", smooth=False, m=mm, grad=False)
    # brass lamp bracket on the front of the dome
    add(BODY, box((0.22, 0.09, 0.15), 0.03, 2), "brass", H, "helmet_lamp", smooth=False,
        m=M((0, -0.255, 1.655), (-38, 0, 0)))
    add(BODY, B.torus(0.10, 0.026, 12, 5), "brass", H, "helmet_lamp", m=M((0, -0.205, 1.705), (-12, 0, 0)))
    # big glowing crystal lamp: tall spike (tip at z 2.0, 0.25 above the dome) + two side shards + a back shard
    axis = Vector((0, -0.21, 0.978)).normalized()
    base = Vector((0, -0.172, 1.66))
    h_main = (2.0 - base.z) / axis.z
    head_crystal(BODY, H, "team_glow", kit.frame(base, axis, (0, 1, 0)), 0.125, h_main, seg=6, waist=0.9,
                 cap=0.3)
    for s in (1, -1):
        ax = Vector((0.42 * s, -0.25, 0.87)).normalized()
        head_crystal(BODY, H, "team_glow", kit.frame((0.08 * s, -0.20, 1.665), ax, (0, 1, 0)), 0.045, 0.13, seg=6,
                      waist=0.8, cap=0.38)


def build_crate():
    global _post
    _post = None
    W = "chest"
    C = Vector((0.0, 0.56, 1.24))
    SX, SY, SZ = 0.60, 0.52, 0.60
    F = "crate"
    FD = "crate_detail"      # boards, brace, lid, seams: 1.5 px inner line

    def at(x, y, z, rot=(0, 0, 0)):
        return M((C.x + x, C.y + y, C.z + z), rot)

    add(BODY, B.box((SX - 0.05, SY - 0.05, SZ - 0.05), 0.0), "wood_dark", W, F, smooth=False, m=at(0, 0, 0))
    # back face: three vertical boards with 1 px grooves between, X brace proud of them
    for x in (-0.193, 0.0, 0.193):
        add(BODY, box((0.183, 0.03, 0.50), 0.012, 1), "wood", W, FD, smooth=False, m=at(x, SY / 2 - 0.02, 0))
    for sgn in (1, -1):
        add(BODY, box((0.095, 0.034, 0.66), 0.014, 1), "wood", W, FD, smooth=False,
            m=at(0, SY / 2 + 0.008, 0, (0, 42 * sgn, 0)))
    # front-facing top lid boards and bottom
    for y in (-0.105, 0.105):
        add(BODY, box((0.50, 0.195, 0.03), 0.012, 1), "wood", W, FD, smooth=False, m=at(0, y, SZ / 2 - 0.02))
    # wooden frame posts along the vertical edges, rails along the horizontal edges
    for x in (-1, 1):
        for y in (-1, 1):
            if y < 0:
                continue          # body side, hidden against the back
            add(BODY, box((0.065, 0.065, SZ - 0.14), 0.02, 1), "wood", W, F, smooth=False,
                m=at(x * (SX / 2 - 0.03), y * (SY / 2 - 0.03), 0))
    for z in (-1, 1):
        for y in (-1, 1):
            if y < 0 and z < 0:
                continue
            add(BODY, box((SX - 0.14, 0.06, 0.06), 0.02, 1), "wood", W, F, smooth=False,
                m=at(0, y * (SY / 2 - 0.03), z * (SZ / 2 - 0.03)))
    # stone side panels (x faces)
    for x in (-1, 1):
        add(BODY, box((0.035, SY - 0.13, SZ - 0.15), 0.015, 1), "stone", W, F, smooth=False,
            m=at(x * (SX / 2 + 0.003), 0, 0))
        for z in (-0.12, 0.12):
            add(BODY, box((0.012, SY - 0.17, 0.012), 0.0, 1), "helmet_seam", W, FD, smooth=False, grad=False,
                m=at(x * (SX / 2 + 0.021), 0, z))
    # brass corner caps (8)
    for x in (-1, 1):
        for y in (-1, 1):
            for z in (-1, 1):
                if y < 0 and z < 0:
                    continue      # hidden against the back
        add(BODY, box((0.125, 0.125, 0.125), 0.03, 1), "brass", W, F, smooth=False,
                    m=at(x * (SX / 2 - 0.045), y * (SY / 2 - 0.045), z * (SZ / 2 - 0.045)))
    # rope coil on the right side, team crystals growing out of the top-left corner
    for (x, y, z, ax, r, h) in ((0.19, 0.08, SZ / 2 - 0.02, (0.30, 0.15, 0.94), 0.08, 0.32),
                                 (0.06, -0.02, SZ / 2 - 0.02, (-0.1, -0.25, 0.95), 0.06, 0.22),
                                 (0.21, -0.10, SZ / 2 - 0.02, (0.5, -0.35, 0.8), 0.05, 0.17),
                                 (-0.19, 0.10, SZ / 2 - 0.02, (-0.35, 0.1, 0.93), 0.07, 0.27),
                                 (SX / 2 + 0.01, 0.05, 0.08, (0.95, 0.1, 0.3), 0.06, 0.20),
                                 (-SX / 2 - 0.01, -0.06, 0.16, (-0.9, -0.1, 0.45), 0.055, 0.17)):
        B.add_crystal(BODY, W, "team_glow", kit.frame(C + Vector((x, y, z)), ax, (0, 1, 0)), r, h, seg=6,
                      waist=0.8, cap=0.36)


# ===================================================================================================== pick-hammer
def held_matrix(poses, slot, origin, z_axis, y_hint):
    rest = B.arm.data.bones[slot].matrix_local
    return rest @ poses[slot].inverted() @ kit.frame(origin, z_axis, y_hint)


def shaft_parts(p):
    """Shared banded shaft along +Z (pommel bottom -0.045, shaft top 0.98); build_pick shifts it 0.26 down so the grip
    sits 31 % up the item. Item height pommel to crystal tip 1.243 u = 0.62 of body height (bible 0.633)."""
    p(B.cyl((0, 0, 0.02), (0, 0, 0.98), 0.042, 0.04, 10), "wood", "pick_hammer", True)
    p(B.cyl((0, 0, 0.19), (0, 0, 0.34), 0.047, 0.047, 10), "navy_dark", "pick_hammer", True)
    p(B.cyl((0, 0, 0.55), (0, 0, 0.68), 0.05, 0.05, 10), "kerchief", "team_cloth", True)   # team trim wrap
    for z in (0.08, 0.40, 0.50, 0.75, 0.88):
        p(B.cyl((0, 0, z - 0.018), (0, 0, z + 0.018), 0.05, 0.05, 8), "brass", "pick_hammer", False)
    p(box((0.10, 0.10, 0.08), 0.022, 1), "brass", "pick_hammer", False, M((0, 0, -0.005)))


def build_pick(poses, obj, state):
    W = "handslot.r"
    parts = []

    def p(bm, color, feat, smooth=False, m=None):
        parts.append((bm, color, feat, smooth, m))

    shaft_parts(p)
    crystals = []
    if state == "pick":
        p(box((0.16, 0.15, 0.20), 0.03, 2), "brass", "pick_hammer", False, M((0, 0, 1.02)))
        p(box((0.135, 0.125, 0.04), 0.012, 1), "brass_dark", "pick_hammer", False, M((0, 0, 0.955)))
        # stone point (+X, toward the head), curving down, with a crystal edge strip: the second crystal point
        arc = [(0.06, 1.03, 0.085), (0.15, 1.02, 0.068), (0.23, 0.96, 0.048), (0.28, 0.88, 0.026), (0.30, 0.80, 0.004)]
        for (x0, z0, r0), (x1, z1, r1) in zip(arc[:-1], arc[1:]):
            p(B.cyl((x0, 0, z0), (x1, 0, z1), r0, r1, 8), "stone_light", "pick_hammer", False)
        crystals.append((kit.frame((0.07, 0, 1.06), (1.0, 0, -0.35), (0, 0, 1)), 0.03, 0.17))
        # crystal blade (-X, outward): the big glowing pick point, angled up and out
        crystals.append((kit.frame((-0.05, 0, 1.05), (-1.0, 0, 0.42), (0, 0, 1)), 0.15, 0.36))
        p(B.cyl((-0.03, 0, 1.03), (-0.12, 0, 1.07), 0.065, 0.05, 8), "stone", "pick_hammer", False)
    else:
        # hammer: stone block head with brass bands and crystals set into both faces and the top
        p(box((0.42, 0.25, 0.27), 0.04, 2), "stone", "pick_hammer", False, M((0, 0, 1.02)))
        for x in (-0.13, 0.13):
            p(box((0.045, 0.265, 0.285), 0.012, 1), "brass", "pick_hammer", False, M((x * 1.15, 0, 1.02)))
        for y in (-1, 1):
            crystals.append((M((0, y * 0.12, 1.02), (-90 * y, 0, 30)), 0.085, 0.065))
        crystals.append((kit.frame((0, 0, 1.14), (0, 0, 1), (0, 1, 0)), 0.05, 0.09))
        p(box((0.13, 0.12, 0.05), 0.012, 1), "brass_dark", "pick_hammer", False, M((0, 0, 0.89)))

    origin = poses[W].translation.copy()
    up = Vector((-0.10, -0.06, 1.0)).normalized()
    # grip sits about a third up the item (bible 08-front-pick: the fist holds the shaft below its middle)
    T = held_matrix(poses, W, origin, up, (0, 1, 0)) @ M((0, 0, -0.26))
    for bm, color, feat, smooth, m in parts:
        B.add(obj, bm, color, W, feat=feat, smooth=smooth, m=m, post=T)
    for m4, r, h in crystals:
        cap = 0.55 if state == "hammer" and h < 0.1 else 0.4
        B.add_crystal(obj, W, "team_glow", m4, r, h, light=(-0.6, -0.5, 0.6), seg=6, waist=0.72, cap=cap, post=T)


# ===================================================================================================== barricade
def bar_post(obj, x, chipped=False):
    """Stone post: three stacked chunky blocks (alternating light / mid stone) with brass bands, crystal inset."""
    F = "barricade"
    sgn = 1 if x > 0 else -1
    blocks = [(0.15, 0.40, 0.30, "stone"), (0.45, 0.37, 0.28, "stone_light"), (0.74, 0.39, 0.27, "stone")]
    if chipped:
        blocks = blocks[:2]
    for k, (z, w, h, col) in enumerate(blocks):
        add(obj, box((w, w * 0.92, h), 0.04, 2), col, None, F, smooth=False,
            m=M((x + rng.uniform(-0.015, 0.015), rng.uniform(-0.015, 0.015), z), (0, 0, rng.uniform(-4, 4))))
    for z in (0.30, 0.60) if not chipped else (0.30,):
        add(obj, box((0.42, 0.39, 0.05), 0.015, 1), "brass", None, F, smooth=False, m=M((x, 0, z)))
    if chipped:   # the top block knocked off, lying tilted at the foot of the post
        add(obj, box((0.33, 0.30, 0.22), 0.04, 2), "stone", None, F, smooth=False,
            m=M((x + 0.28 * sgn, -0.30, 0.175), (12, 25, 30)))
    B.add_crystal(obj, None, "team_glow", M((x + sgn * 0.20, 0, 0.45), (0, 90 * sgn, 0)), 0.07, 0.07, seg=6,
                  waist=0.8, cap=0.55)


def bar_plank(obj, x0, x1, z, tilt=0.0, h=0.25, y=0.0):
    add(obj, box((x1 - x0, 0.09, h), 0.025, 2), "wood", None, "barricade", smooth=False,
        m=M(((x0 + x1) / 2, y, z), (0, tilt, 0)))
    for x in (x0 + 0.07, x1 - 0.07):      # brass end brackets
        add(obj, box((0.09, 0.11, h + 0.03), 0.015, 1), "brass", None, "barricade", smooth=False,
            m=M((x, y, z), (0, tilt, 0)))
    add(obj, box(((x1 - x0) * 0.7, 0.02, 0.012), 0.0, 1), "wood_dark", None, "barricade", smooth=False, grad=False,
        m=M(((x0 + x1) / 2, y - 0.046, z + 0.02), (0, tilt, 0)))      # grain groove (1 px line)


def bar_diamond(obj, z, scale=1.0, tilt=0.0, x=0.0):
    """Big diamond crystal facing front in a diamond-shaped brass frame."""
    F = "barricade"
    face = kit.frame((x, -0.05, z), (0, -1, 0), (0, 0, 1)) @ M((0, 0, 0), (0, 0, 45 + tilt))
    add(obj, B.torus(0.24 * scale, 0.035, 4, 4), "brass", None, F, smooth=False, m=face @ M((0, 0, 0.0)))
    B.add_crystal(obj, None, "team_glow", face, 0.22 * scale, 0.14 * scale, seg=4, waist=0.92, cap=0.75,
                  light=(-0.5, -0.6, 0.6))


def build_barricade():
    # deployed: wide fence, two stone posts, two thick planks, central diamond crystal
    o = BAR["deployed"]
    for x in (-0.72, 0.72):
        bar_post(o, x)
    for z in (0.30, 0.62):
        bar_plank(o, -0.53, 0.53, z)
    bar_diamond(o, 0.46)
    # folded: compact upright crate (the posts folded in as stone side panels), X brace, brass corners, crystals
    o = BAR["folded"]
    SX, SY, SZ = 0.62, 0.44, 0.80
    add(o, B.box((SX - 0.14, SY - 0.05, SZ - 0.04), 0.0), "wood_dark", None, "barricade", smooth=False,
        m=M((0, 0, SZ / 2)))
    for x in (-1, 1):
        add(o, box((0.13, SY, SZ - 0.06), 0.04, 2), "stone_light", None, "barricade", smooth=False,
            m=M((x * (SX / 2 - 0.065), 0, SZ / 2)))
        B.add_crystal(o, None, "team_glow", M((x * (SX / 2 + 0.0), 0, SZ / 2), (0, 90 * x, 0)), 0.08, 0.08,
                      seg=6, waist=0.8, cap=0.55)
        for y in (-1, 1):
            for z in (-1, 1):
                add(o, box((0.12, 0.12, 0.12), 0.03, 1), "brass", None, "barricade", smooth=False,
                    m=M((x * (SX / 2 - 0.05), y * (SY / 2 - 0.05), SZ / 2 + z * (SZ / 2 - 0.05))))
    for y in (-1, 1):
        for xx in (-0.12, 0.0, 0.12):
            add(o, box((0.115, 0.03, SZ - 0.16), 0.012, 1), "wood", None, "barricade", smooth=False,
                m=M((xx, y * (SY / 2 - 0.005), SZ / 2)))
        for sg in (1, -1):
            add(o, box((0.075, 0.035, 0.72), 0.014, 1), "wood", None, "barricade", smooth=False,
                m=M((0, y * (SY / 2 + 0.014), SZ / 2), (0, 22 * sg, 0)))
    add(o, box((SX - 0.02, SY + 0.02, 0.05), 0.018, 2), "wood", None, "barricade", smooth=False,
        m=M((0, 0, SZ + 0.02)))
    B.add_crystal(o, None, "team_glow", kit.frame((0.14, 0.05, SZ + 0.04), (0.3, 0.1, 0.95), (0, 1, 0)), 0.06, 0.18,
                  seg=6, waist=0.8, cap=0.4)
    add(o, B.torus(0.06, 0.015, 10, 4), "brass_dark", None, "barricade", m=M((-0.10, 0, SZ + 0.06), (90, 0, 0)))
    # damaged: one post lost its top block, top plank snapped in two, lower plank cracked, crystal chipped, debris
    o = BAR["damaged"]
    bar_post(o, -0.72)
    bar_post(o, 0.72, chipped=True)
    bar_plank(o, -0.53, -0.04, 0.30, tilt=-2)
    bar_plank(o, 0.03, 0.53, 0.29, tilt=3)
    bar_plank(o, -0.53, -0.08, 0.66, tilt=6)
    jag = [(0.0, -0.125), (0.42, -0.125), (0.36, -0.04), (0.44, 0.03), (0.34, 0.125), (0.0, 0.125)]
    add(o, B.prism(jag, 0.09, 0.012), "wood", None, "barricade", smooth=False,
        m=M((0.08, 0.0, 0.58), (90, -14, 0)))
    bar_diamond(o, 0.47, scale=0.8, tilt=14, x=-0.05)
    for (x, y, rz, l) in ((0.15, -0.40, 25, 0.34), (-0.25, -0.45, -40, 0.24)):
        add(o, box((l, 0.08, 0.06), 0.015, 1), "wood", None, "barricade", smooth=False, m=M((x, y, 0.03), (0, 0, rz)))
    for (x, y, sz) in ((0.40, -0.45, 0.11), (0.95, -0.25, 0.08), (0.30, -0.25, 0.06)):
        add(o, box((sz, sz * 0.9, sz * 0.8), 0.02, 1), "stone_light", None, "barricade", smooth=False,
            m=M((x, y, sz * 0.4), (rng.uniform(-20, 20), rng.uniform(-20, 20), rng.uniform(0, 90))))
    B.add_crystal(o, None, "team_glow", kit.frame((0.05, -0.32, 0.0), (0.4, -0.2, 0.3), (0, 0, 1)), 0.045, 0.10,
                  seg=4, waist=0.9, cap=0.6)


# ===================================================================================================== main
def dims(ob):
    vs = [ob.matrix_world @ v.co for v in ob.data.vertices]
    lo = [min(v[i] for v in vs) for i in range(3)]
    hi = [max(v[i] for v in vs) for i in range(3)]
    return {"min": [round(x, 3) for x in lo], "max": [round(x, 3) for x in hi]}


def tris(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


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

    B = mkit.MinerBuilder(arm)
    build_legs()
    build_torso()
    build_arms()
    build_neck()
    build_head()
    build_helmet()
    build_crate()
    build_pick(poses, PICK, "pick")
    build_pick(poses, HAMMER, "hammer")
    coll = bpy.data.collections.new("Miner")
    scene.collection.children.link(coll)
    for c in list(arm.users_collection):
        c.objects.unlink(arm)
    coll.objects.link(arm)
    objs = B.finalize(coll, mat)
    objs[HAMMER].hide_render = True
    objs[HAMMER].hide_set(True)

    B = mkit.MinerBuilder(None)
    build_barricade()
    bcoll = bpy.data.collections.new("MinerBarricade")
    scene.collection.children.link(bcoll)
    bobjs = B.finalize_static(bcoll, mat)
    for ob in bobjs.values():
        ob.hide_render = True
        ob.hide_set(True)

    report = {"objects": {}, "bones": len(arm.data.bones), "display_pose": [DISPLAY_ACTION, DISPLAY_FRAME]}
    for name, ob in list(objs.items()) + list(bobjs.items()):
        report["objects"][name] = {
            "triangles": tris(ob), "vertices": len(ob.data.vertices), "rest_bounds": dims(ob),
            "materials": [m.name for m in ob.data.materials],
            "feature_groups": sorted(g.name for g in ob.vertex_groups if g.name.startswith("feat_")),
        }
    report["triangles_character"] = tris(objs[BODY]) + tris(objs[PICK]) + tris(objs[HAMMER])
    report["triangles_barricade_per_state"] = {k: tris(bobjs[v]) for k, v in BAR.items()}

    arm.animation_data_create()
    arm.animation_data.action = bpy.data.actions[DISPLAY_ACTION]
    scene.frame_set(DISPLAY_FRAME)
    bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND, compress=True)

    objs[HAMMER].hide_set(False)
    for ob in bobjs.values():
        ob.hide_set(True)
    export.export_fbx(OUT_FBX, arm, [objs[BODY], objs[PICK], objs[HAMMER]])
    # barricade: static meshes only, same axis/scale recipe
    for ob in bpy.context.view_layer.objects:
        ob.select_set(False)
    for ob in bobjs.values():
        ob.hide_set(False)
        ob.select_set(True)
    bpy.context.view_layer.objects.active = bobjs[BAR["deployed"]]
    opts = dict(export.FBX_OPTS)
    opts["object_types"] = {"MESH"}
    res = bpy.ops.export_scene.fbx(filepath=OUT_BARR_FBX, **opts)
    if "FINISHED" not in res:
        raise SystemExit("barricade FBX export failed")
    with open(OUT_REPORT, "w") as f:
        json.dump(report, f, indent=1)
        f.write("\n")
    print("BUILD_REPORT " + json.dumps({k: v for k, v in report.items() if k != "objects"}))
    for n, o in report["objects"].items():
        print("OBJ", n, o["triangles"], o["rest_bounds"])


main()
