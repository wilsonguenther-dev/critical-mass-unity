"""Build the Demolitions Expert (DG-07, proposed; no bible page; the FEMALE class by the bible artist's 2026-11-12 decision) and his props from nothing.

  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python-exit-code 1 -P build_demolitions.py

Same route as the Miner v3 (Assets/_Game/Art/Miner/_source~/build_miner.py): the shared, read-only cm_art helpers
(KayKit 41-joint rig loader, mesh kit, FBX recipe), a 256 px painted face decal on its own material slot, light and
dark value steps baked into the vertex colour, one CM_Toon material, team mask in the vertex-colour alpha.

Outputs (relative to this folder):
  demolitions.blend           Rig + Demolitions_Body + Demolitions_TNTHand, and the DemolitionsProps collection
  ../Demolitions.fbx          character: Rig (KayKit, unmodified) + body + TNT in hand (rigid on handslot.r)
  ../DemolitionsProps.fbx     Mine_Buried, Mine_Armed, Mine_Armed_OwnerRing, Mine_Triggered, TNT_Flight, CraterRim,
                              Explosion: authored static meshes, origin on the ground
  ../DemolitionsFace.png      256 x 256 face decal (material CM_FaceDecal, Body slot 1)
  build_report.json           triangles, bounds, feature groups, bevel tally

Space: rest pose (KayKit T-pose), Z up, faces -Y, character left = +X, metres. 2.0 u from the boot soles to the top of
the fuse spark; the cap crown is at about 1.87.
"""
import json
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
SHARED = os.path.join(os.path.dirname(os.path.dirname(HERE)), "Characters", "_source~")
sys.dont_write_bytecode = True
sys.path.insert(0, HERE)
sys.path.insert(0, SHARED)

from cm_art import export, kit, rig  # noqa: E402  (shared, read-only)
from demo_art import dkit, dpal, face_decal, toon3  # noqa: E402

SEED = 707
OUT_BLEND = os.path.join(HERE, "demolitions.blend")
OUT_FBX = os.path.join(os.path.dirname(HERE), "Demolitions.fbx")
OUT_PROPS_FBX = os.path.join(os.path.dirname(HERE), "DemolitionsProps.fbx")
OUT_REPORT = os.path.join(HERE, "build_report.json")
OUT_FACE = os.path.join(os.path.dirname(HERE), "DemolitionsFace.png")
DISPLAY_ACTION, DISPLAY_FRAME = "Idle", 1

rng = random.Random(SEED)
B = None
BODY, TNTH = "Demolitions_Body", "Demolitions_TNTHand"
PROPS = {"buried": "Demolitions_Mine_Buried", "armed": "Demolitions_Mine_Armed",
         "ring": "Demolitions_Mine_Armed_OwnerRing", "triggered": "Demolitions_Mine_Triggered",
         "flight": "Demolitions_TNT_Flight", "crater": "Demolitions_CraterRim", "boom": "Demolitions_Explosion"}
M = kit.mat_from
BV = 0.03
BV_MIN = 0.02
LEG_X = 0.18                 # lean: narrowest stance of the four built classes (Miner 0.255)
SY = 0.78                    # torso depth / width
BEVELS = {"boxes": 0, "at_min_or_more": 0, "groove_strips_exempt": 0, "hidden_cores_unbevelled": 0, "min_bevel": 9.0}


def add(obj, bm, color, bones, feat, smooth=True, m=None, **kw):
    B.add(obj, bm, color, bones, feat=feat, smooth=smooth, m=m, **kw)


def box(size, bevel=BV, seg=1):
    """Same bevel discipline as the Miner: at least BV_MIN on every visible box, thin strips exempt, tallied."""
    BEVELS["boxes"] += 1
    thin = min(size)
    if bevel <= 0:
        BEVELS["hidden_cores_unbevelled" if thin > 0.04 else "groove_strips_exempt"] += 1
        return B.box(size, 0.0, seg)
    if 0.025 <= thin < 2 * BV_MIN:
        size = tuple(2 * BV_MIN if d == thin else d for d in size)
        thin = 2 * BV_MIN
    if thin < 2 * BV_MIN:
        BEVELS["groove_strips_exempt"] += 1
        return B.box(size, min(bevel, thin * 0.45), seg)
    bevel = max(bevel, BV_MIN)
    BEVELS["at_min_or_more"] += 1
    BEVELS["min_bevel"] = min(BEVELS["min_bevel"], bevel)
    return B.box(size, bevel, max(seg, 1))


def strap(a, b, width, thick, normal_hint, bevel=None):
    a, b = Vector(a), Vector(b)
    bm = box((width, thick, (b - a).length), BV_MIN if bevel is None else bevel, 1)
    return bm, kit.frame((a + b) / 2, b - a, normal_hint)


def rivet(r=0.016, h=0.012):
    return B.cyl((0, 0, 0), (0, 0, h), r, r * 0.7, 5)


def pivot(rot_deg, axis, p):
    p = Vector(p)
    return Matrix.Translation(p) @ Matrix.Rotation(math.radians(rot_deg), 4, axis) @ Matrix.Translation(-p)


def shell_sector(prof, a0, a1, thick, seg=2):
    """Closed curved stave: outer surface of revolution between angles a0..a1 (profile [(r, z)...]), an inner surface
    `thick` inside it, side walls and end caps. Keg staves and cap panels are built from these as separate pieces."""
    bm = bmesh.new()
    outer, inner = [], []
    for r, z in prof:
        outer.append([bm.verts.new((r * math.cos(a0 + (a1 - a0) * i / seg), r * math.sin(a0 + (a1 - a0) * i / seg), z))
                      for i in range(seg + 1)])
        ri = max(r - thick, 0.01)
        inner.append([bm.verts.new((ri * math.cos(a0 + (a1 - a0) * i / seg), ri * math.sin(a0 + (a1 - a0) * i / seg),
                                    z)) for i in range(seg + 1)])
    n = len(prof)
    for j in range(n - 1):
        for i in range(seg):
            bm.faces.new((outer[j][i], outer[j][i + 1], outer[j + 1][i + 1], outer[j + 1][i]))
            bm.faces.new((inner[j][i], inner[j + 1][i], inner[j + 1][i + 1], inner[j][i + 1]))
        for side in (0, seg):
            q = (outer[j][side], outer[j + 1][side], inner[j + 1][side], inner[j][side])
            bm.faces.new(q if side == 0 else tuple(reversed(q)))
    for j in (0, n - 1):
        for i in range(seg):
            q = (outer[j][i], inner[j][i], inner[j][i + 1], outer[j][i + 1])
            bm.faces.new(q if j == 0 else tuple(reversed(q)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def tube(obj, pts, r, color, bones, feat, seg=6, smooth=True, m=None, post=None, joints=True):
    """Polyline tube: one closed cylinder per segment plus a ball at each inner joint (fuse cord, curled)."""
    for k, (a, b) in enumerate(zip(pts[:-1], pts[1:])):
        bm = B.cyl(a, b, r, r, seg)
        if m is not None:
            bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
        add(obj, bm, color, bones, feat, smooth=smooth, post=post)
    for p in (pts[1:-1] if joints else []):
        bm = B.sphere((r * 1.02,) * 3, seg, 3)
        bmesh.ops.translate(bm, vec=Vector(p), verts=bm.verts)
        if m is not None:
            bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
        add(obj, bm, color, bones, feat, smooth=smooth, post=post)


def spark(obj, c, bones, scale=1.0, n_rays=8, post=None, feat="spark", seed=0):
    """Hazard spark: warm-cream core, cream-to-brass rays (never team colour). Rays spread over the upper half so the
    point reads from the 55-degree camera."""
    r_ = random.Random(seed)
    c = Vector(c)
    add(obj, B.sphere((0.045 * scale,) * 3, 8, 5), "spark_core", bones, feat, smooth=True, grad=False,
        m=M(c), post=post)
    for k in range(n_rays):
        az = 2 * math.pi * k / n_rays + r_.uniform(-0.2, 0.2)
        el = math.radians(r_.uniform(-15, 70) if k % 3 else 80)
        d = Vector((math.cos(az) * math.cos(el), math.sin(az) * math.cos(el), math.sin(el)))
        L = scale * (0.13 if k % 2 == 0 else 0.09)
        base = c + d * 0.02 * scale
        tip = c + d * L
        mid = base + (tip - base) * 0.62
        add(obj, B.cyl(base, mid, 0.024 * scale, 0.015 * scale, 4), "spark", bones, feat, smooth=False, grad=False,
            post=post)
        add(obj, B.cyl(mid, tip, 0.015 * scale, 0.002, 4), "spark_tip", bones, feat, smooth=False, grad=False,
            post=post)


def tnt_bundle(obj, bones, T, r=0.032, L=0.19, band_feat="team_cloth", fuse=True, feat="tnt", post=None,
               fuse_len=0.06, seg=6):
    """3-stick TNT bundle along local +Z, centred at the origin of T: brick-red sticks with cream end papers, a
    team-colour wrapper band, short fuse stubs. Sticks are separate bevel-free cylinders (round), the band is proud."""
    d = r * 1.08
    offs = [(d * math.cos(a), d * math.sin(a)) for a in (math.radians(90), math.radians(210), math.radians(330))]
    for x, y in offs:
        add(obj, B.cyl((x, y, -L / 2 + 0.012), (x, y, L / 2 - 0.012), r, r, seg), "tnt", bones, feat, m=T, post=post)
        for z0, z1 in ((-L / 2, -L / 2 + 0.014), (L / 2 - 0.014, L / 2)):
            add(obj, B.cyl((x, y, z0), (x, y, z1), r * 0.92, r * 0.92, seg), "cream", bones, feat, smooth=False,
                m=T, post=post)
    R = d + r + 0.006
    for zc in ((0.0,) if L < 0.3 else (-L * 0.24, L * 0.24)):
        add(obj, B.cyl((0, 0, zc - L * 0.11), (0, 0, zc + L * 0.11), R, R, 12), "kerchief", bones, band_feat,
            m=T, post=post)
    if fuse:
        x, y = offs[0]
        pts = [(x * 0.5, y * 0.5, L / 2), (x * 0.4, y * 0.6, L / 2 + fuse_len * 0.6), (0.0, y * 0.9, L / 2 + fuse_len)]
        tube(obj, pts, r * 0.32, "fuse", bones, feat, seg=5, m=T, post=post)
    return offs


# ===================================================================================================== body
def build_legs():
    for s, sd in ((1, "l"), (-1, "r")):
        lx = LEG_X * s
        up, lo, ft, to = f"upperleg.{sd}", f"lowerleg.{sd}", f"foot.{sd}", f"toes.{sd}"
        prof = [(0.105, 0.20), (0.128, 0.27), (0.142, 0.37), (0.148, 0.47), (0.150, 0.58), (0.142, 0.67)]
        add(BODY, B.lathe(prof, 10), "navy_dark", ["hips", up, lo], "trousers", m=M((lx, 0.0, 0)))
        # leather knee patch with stitch dashes (material detail as small geometry)
        kp = M((lx, -0.140, 0.36), (-6, 0, 6 * s))
        add(BODY, box((0.15, 0.04, 0.14), 0.02, 1), "leather", lo, "knee_patch", smooth=False, m=kp)
        for k in range(3):
            add(BODY, B.box((0.024, 0.012, 0.008), 0.0), "cream", lo, "knee_patch", smooth=False, grad=False,
                m=kp @ M(((k - 1) * 0.045, -0.022, 0.05)))
        # heavy boots: tall shaft, rolled cuff, chunky foot, dark sole, stone toe plate, cream laces
        add(BODY, B.cyl((lx, 0.02, 0.06), (lx, 0.02, 0.30), 0.150, 0.142, 12), "leather", [lo, ft], "boots")
        add(BODY, B.torus(0.148, 0.038, 10, 3), "leather_dark", lo, "boots", m=M((lx, 0.02, 0.30)))
        add(BODY, box((0.31, 0.42, 0.17), 0.05, 1), "leather", [ft, to], "boots", m=M((lx + 0.01 * s, -0.07, 0.09)))
        add(BODY, box((0.33, 0.45, 0.05), 0.02, 1), "leather_dark", [ft, to], "boots", smooth=False,
            m=M((lx + 0.01 * s, -0.075, 0.025)))
        add(BODY, box((0.30, 0.15, 0.12), 0.035, 1), "stone", to, "boots", smooth=False,
            m=M((lx + 0.01 * s, -0.23, 0.10)))
        for z in (0.14, 0.19, 0.24):
            add(BODY, B.box((0.10, 0.02, 0.018), 0.0), "cream", lo, "boots", smooth=False, grad=False,
                m=M((lx, -0.135, z), (-8, 0, 0)))


def torso_r(z):
    """Outer radius (x) of the vest at height z; depth is r * SY."""
    # female (owner decision 2026-11-12: Demolitions is the female class): hips over a pinched waist, fuller chest
    pts = [(0.66, 0.262), (0.74, 0.248), (0.82, 0.232), (0.92, 0.262), (1.00, 0.282), (1.08, 0.272), (1.16, 0.225),
           (1.235, 0.13)]
    if z <= pts[0][0]:
        return pts[0][1]
    for (z0, r0), (z1, r1) in zip(pts[:-1], pts[1:]):
        if z <= z1:
            return r0 + (r1 - r0) * (z - z0) / (z1 - z0)
    return pts[-1][1]


def on_torso(theta_deg, z, off=0.0):
    t = math.radians(theta_deg)
    r = torso_r(z) + off
    return Vector((r * math.sin(t), -r * SY * math.cos(t), z))


def build_torso():
    W3 = ["hips", "spine", "chest"]
    add(BODY, B.sphere((0.25, 0.20, 0.15), 12, 6), "navy_dark", ["hips", "spine"], "trousers", m=M((0, 0.0, 0.63)))
    # cream shirt core (shows at the collar and between the vest pads)
    prof = [(0.235, 0.64), (0.225, 0.74), (0.21, 0.82), (0.245, 0.94), (0.26, 1.00), (0.245, 1.10), (0.19, 1.19),
            (0.10, 1.25)]
    add(BODY, B.lathe(prof, 12), "navy", W3, "shirt", m=M((0, 0, 0), (0, 0, 0), (1, SY, 1)))
    # padded vest in Stone: a dark quilting core with four separate padded rolls proud of it (gaps = inner lines)
    vprof = [(torso_r(z) - 0.012, z) for z in (0.70, 0.80, 0.92, 1.04, 1.14, 1.20)]
    add(BODY, B.lathe(vprof, 12), "vest_seam", W3, "vest_detail", grad=False, m=M((0, 0, 0), (0, 0, 0), (1, SY, 1)))
    for z0, z1 in ((0.705, 0.815), (0.825, 0.935), (0.945, 1.055), (1.065, 1.165)):
        zm = (z0 + z1) / 2
        r0, rm, r1 = torso_r(z0) - 0.004, torso_r(zm) + 0.018, torso_r(z1) - 0.004
        pr = [(r0 - 0.012, z0), (r0 + 0.004, z0 + 0.016), (rm, zm), (r1 + 0.004, z1 - 0.016), (r1 - 0.012, z1)]
        add(BODY, B.lathe(pr, 12), "vest", W3, "vest", m=M((0, 0, 0), (0, 0, 0), (1, SY, 1)))
    # vest collar roll and padded shoulder caps
    add(BODY, B.torus(0.165, 0.045, 14, 5), "vest", "chest", "vest", m=M((0, 0.0, 1.195), (0, 0, 0), (1, 0.85, 0.8)))
    for s in (1, -1):
        add(BODY, B.sphere((0.12, 0.13, 0.07), 8, 4), "vest", "chest", "vest", m=M((0.19 * s, 0.0, 1.20), (0, -18 * s, 0)))
    # two front buckle straps with brass buckles (Tool Brass on Stone)
    for z, th0, th1 in ((0.875, -38, 38), (0.995, -36, 36)):
        pts = [on_torso(t, z, 0.026) for t in range(th0, th1 + 1, 18)]
        for a, b in zip(pts[:-1], pts[1:]):
            n = Vector((a.x + b.x, (a.y + b.y) / SY, 0)).normalized()
            bm, mm = strap(a, b, 0.045, 0.02, n, bevel=0.008)
            add(BODY, bm, "leather_dark", ["spine", "chest"], "vest_detail", smooth=False, m=mm)
        c = on_torso(14, z, 0.042)
        nrm = Vector((c.x, c.y / SY, 0)).normalized()
        BM = kit.frame(c, nrm, (0, 0, 1))
        add(BODY, box((0.075, 0.065, 0.025), 0.01, 1), "brass", ["spine", "chest"], "buckles", smooth=False, m=BM)
        add(BODY, box((0.040, 0.032, 0.03), 0.0), "brass_dark", ["spine", "chest"], "buckles", smooth=False,
            m=BM @ M((0, 0, 0.004)))
    # belt, buckle, fuse-coil on the right hip, small pouch on the left
    add(BODY, B.cyl((0, 0, 0.64), (0, 0, 0.73), 0.255, 0.262, 16), "leather", {"hips": 0.5, "spine": 0.5}, "belt",
        m=M((0, 0, 0), (0, 0, 0), (1, 0.84, 1)))
    add(BODY, box((0.12, 0.04, 0.10), 0.02, 1), "brass", {"hips": 0.5, "spine": 0.5}, "buckles", smooth=False,
        m=M((0, -0.222, 0.685)))
    add(BODY, box((0.06, 0.04, 0.05), 0.0), "leather_dark", {"hips": 0.5, "spine": 0.5}, "buckles", smooth=False,
        m=M((0, -0.232, 0.685)))
    add(BODY, B.torus(0.075, 0.022, 10, 3), "fuse", "hips", "fuse_coil", m=M((-0.255, -0.06, 0.62), (0, 90, 0)))
    pm = M((0.215, -0.15, 0.64), (0, 0, 35))
    add(BODY, box((0.12, 0.08, 0.13), 0.025, 1), "leather", {"hips": 0.6, "spine": 0.4}, "belt", m=pm)
    add(BODY, box((0.135, 0.095, 0.05), 0.02, 1), "leather_dark", {"hips": 0.6, "spine": 0.4}, "belt", smooth=False,
        m=pm @ M((0, -0.002, 0.05)))


def build_bandolier():
    """Bandolier from his left shoulder across the chest to his right hip, three 3-stick TNT bundles on it."""
    W = ["spine", "chest"]
    path = []
    for k in range(7):
        t = k / 6
        z = 1.215 - 0.47 * t
        th = 40 - 105 * t
        path.append(on_torso(th, z, 0.040))
    path.insert(0, Vector((0.17, 0.0, 1.265)))
    for a, b in zip(path[:-1], path[1:]):
        n = Vector((a.x + b.x, (a.y + b.y) / SY, 0.15)).normalized()
        bm, mm = strap(a, b, 0.085, 0.032, n, bevel=0.01)
        add(BODY, bm, "leather_dark", W, "bandolier", smooth=False, m=mm)
    back = [Vector((-0.25, 0.05, 0.75)), Vector((-0.10, 0.22, 0.88)), Vector((0.10, 0.24, 1.08)),
            Vector((0.17, 0.0, 1.265))]
    for a, b in zip(back[:-1], back[1:]):
        bm, mm = strap(a, b, 0.085, 0.032, Vector((a.x + b.x, (a.y + b.y), 0)).normalized(), bevel=0.01)
        add(BODY, bm, "leather_dark", W, "bandolier", smooth=False, m=mm)
    for t in (0.28, 0.52, 0.76):
        k = t * 6
        i = int(k)
        p = path[1 + i].lerp(path[2 + i], k - i)
        nrm = Vector((p.x, p.y / SY, 0)).normalized()
        c = p + nrm * 0.055
        T = kit.frame(c, (0.0, 0.0, 1.0), -nrm) @ M((0, 0, 0), (0, 0, 90))
        T = kit.frame(c, Vector((0.0, 0.0, 1.0)) + nrm * 0.15, (-nrm.x, -nrm.y, 0))
        tnt_bundle(BODY, W, T, r=0.033, L=0.20, fuse=True, fuse_len=0.05)
        # brass clip holding the bundle on the strap
        add(BODY, box((0.05, 0.03, 0.05), 0.012, 1), "brass", W, "bandolier", smooth=False,
            m=kit.frame(p + nrm * 0.02, nrm, (0, 0, 1)))


def build_arms():
    """Long lean arms (the longest reach of the four built classes): rolled cream sleeves, team armband, bare
    forearms with soot, fingerless gloves with chunky knuckles and bare finger stubs."""
    Z = 1.107
    for s, sd in ((1, "l"), (-1, "r")):
        ua, la, wr, hd = f"upperarm.{sd}", f"lowerarm.{sd}", f"wrist.{sd}", f"hand.{sd}"
        add(BODY, B.sphere((0.11, 0.11, 0.11), 8, 5), "navy", ["chest", ua], "shirt", m=M((0.22 * s, 0, Z)))
        add(BODY, B.cyl((0.21 * s, 0.0, Z), (0.43 * s, 0.006, Z), 0.108, 0.098, 10), "cream", ["chest", ua, la],
            "sleeve")
        add(BODY, B.torus(0.098, 0.036, 10, 4), "cream", ua, "sleeve", m=M((0.43 * s, 0.006, Z), (0, 90, 0)))
        if s > 0:   # team armband on his left upper arm
            add(BODY, B.cyl((0.30, 0.0, Z), (0.38, 0.0, Z), 0.112, 0.108, 10), "kerchief", ua, "team_cloth")
        add(BODY, B.sphere((0.078, 0.078, 0.078), 8, 5), "skin", [ua, la], "skin", m=M((0.46 * s, 0.008, Z)))
        add(BODY, B.cyl((0.46 * s, 0.008, Z), (0.71 * s, 0.0, Z), 0.074, 0.084, 10), "skin", [la, wr], "skin")
        add(BODY, B.sphere((0.04, 0.012, 0.03), 6, 3), "soot", la, "soot", smooth=False,
            m=M((0.58 * s, -0.08, Z + 0.01), (0, 0, 0)))
        # fingerless glove: flared cuff with team ring, padded back, knuckle ridge, bare finger stubs, thumb
        add(BODY, B.cyl((0.68 * s, 0, Z), (0.82 * s, 0, Z), 0.105, 0.122, 10), "leather_dark", [la, wr, hd], "gloves")
        add(BODY, B.torus(0.108, 0.026, 10, 3), "kerchief", [la, wr], "team_cloth", m=M((0.70 * s, 0, Z), (0, 90, 0)))
        add(BODY, B.sphere((0.13, 0.13, 0.125), 9, 6), "leather_dark", hd, "gloves", m=M((0.89 * s, -0.005, 1.095)))
        add(BODY, box((0.07, 0.23, 0.10), 0.03, 1), "leather", hd, "gloves", smooth=False, m=M((0.975 * s, -0.01, 1.11)))
        for k, y in enumerate((-0.078, -0.026, 0.026, 0.078)):
            add(BODY, box((0.11, 0.048, 0.06), 0.02, 1), "skin", hd, "fingers", smooth=False,
                m=M((1.045 * s, y, 1.075 - 0.006 * abs(k - 1.5)), (0, 28 * s, 0)))
        add(BODY, B.sphere((0.06, 0.055, 0.075), 6, 5), "skin", hd, "fingers", m=M((0.86 * s, -0.135, 1.12)))


def build_neck():
    add(BODY, B.cyl((0, 0, 1.15), (0, 0, 1.31), 0.085, 0.08, 8), "skin", ["chest", "head"], "skin")
    # team scarf: thick roll, knot at his right, two tails hanging over the vest
    add(BODY, B.torus(0.175, 0.07, 12, 5), "kerchief", "chest", "team_cloth",
        m=M((0, 0.0, 1.225), (6, 0, 0), (1.05, 0.9, 0.85)))
    add(BODY, B.sphere((0.085, 0.06, 0.07), 8, 5), "kerchief", "chest", "team_cloth", m=M((-0.10, -0.16, 1.165)))
    add(BODY, B.spike((-0.11, -0.19, 1.13), (-0.18, -0.24, 0.98), 0.06, 6, (0.0, -0.02, 0.0)), "kerchief", "chest",
        "team_cloth")
    add(BODY, B.spike((-0.07, -0.19, 1.13), (-0.04, -0.26, 1.01), 0.05, 6, (0.01, -0.02, 0.0)), "kerchief", "chest",
        "team_cloth")


HEAD_C, HEAD_R = (0.0, -0.01, 1.45), (0.27, 0.25, 0.28)


def head_front_y(x, z, proud=0.0):
    cx, cy, cz = HEAD_C
    rx, ry, rz = HEAD_R
    k = 1.0 - ((x - cx) / rx) ** 2 - ((z - cz) / rz) ** 2
    return cy - ry * math.sqrt(max(k, 0.02)) - proud


def head_normal(x, z):
    y = head_front_y(x, z)
    cx, cy, cz = HEAD_C
    rx, ry, rz = HEAD_R
    return Vector(((x - cx) / rx ** 2, (y - cy) / ry ** 2, (z - cz) / rz ** 2)).normalized(), y


def face_plane(rows=7, cols=9):
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


def surface_patch(x, z, rx, rz, color, feat, rot=0.0, proud=0.006):
    """Thin flattened lens hugging the head at (x, z): soot smudges as vertex-colour value steps in geometry."""
    n, y = head_normal(x, z)
    T = kit.frame(Vector((x, y, z)) + n * proud, n, (0, 0, 1)) @ M((0, 0, 0), (0, 0, rot))
    add(BODY, B.sphere((rx, rz, 0.008), 6, 3), color, "head", feat, smooth=True, grad=False, m=T)


def build_head():
    H = "head"
    add(BODY, B.sphere(HEAD_R, 20, 14), "skin", H, "skin", m=M(HEAD_C))
    for s in (1, -1):
        add(BODY, B.sphere((0.04, 0.05, 0.07), 6, 4), "skin_shadow", H, "skin", m=M((0.265 * s, 0.02, 1.45)))
    add(BODY, face_plane(), "skin", H, "face_decal", smooth=True, grad=False)
    # soot smudges (value steps): his right cheek, his left jaw, the chin, the left temple
    surface_patch(-0.215, 1.40, 0.04, 0.03, "soot", "soot", rot=20)
    # brows, eyes, the gap-tooth grin and the lips are PAINTED in the decal (smooth, round, clean: the artist's rule);
    # geometry keeps only the round head, cheeks, ears and a small button nose with a soot smudge on the tip
    add(BODY, B.sphere((0.042, 0.04, 0.038), 10, 7), "nose", H, "face", m=M((0, -0.255, 1.415)))
    add(BODY, B.sphere((0.022, 0.01, 0.016), 6, 3), "soot_dark", H, "soot", smooth=True, grad=False,
        m=M((0.008, -0.293, 1.425), (70, 0, 0)))
    # twin puffed pigtails from under the cap (her silhouette mark at head height), team-colour hair ties,
    # frizzy singed ends
    for s in (1, -1):
        root = Vector((0.235 * s, 0.10, 1.54))
        d = Vector((0.75 * s, 0.35, -0.55)).normalized()
        add(BODY, B.torus(0.062, 0.03, 8, 3), "kerchief", H, "team_cloth", m=kit.frame(root + d * 0.05, d, (0, 0, 1)))
        for k, (t, r) in enumerate(((0.10, 0.075), (0.17, 0.10), (0.26, 0.105), (0.34, 0.08))):
            c = root + d * t + Vector((0, 0, 0.012 * (k % 2)))
            add(BODY, B.sphere((r, r * 0.92, r * 0.95), 7, 5), "hair" if k % 2 == 0 else "hair_light", H, "hair",
                m=M(c, (0, 0, 25 * k)))
        tipc = root + d * 0.38
        for k in range(4):
            a = 2 * math.pi * k / 4 + 0.4
            side = Vector((0, 0, 1)).cross(d).normalized()
            up_ = d.cross(side).normalized()
            off = (side * math.cos(a) + up_ * math.sin(a)) * 0.05
            base = tipc + off
            tip = base + d * 0.09 + off * 0.9
            add(BODY, B.cyl(base, tip, 0.03, 0.004, 5), "hair_singed" if k % 2 else "hair", H, "hair", smooth=False,
                grad=False)
    # short fringe locks escaping the cap at her left temple
    for k, (x, z) in enumerate(((0.17, 1.585), (0.215, 1.57))):
        base = Vector((x, head_front_y(x, z, 0.02), z + 0.03))
        add(BODY, B.spike(base, base + Vector((0.04, -0.03, -0.11 + 0.02 * k)), 0.035, 5, (0.02, -0.01, 0)), "hair", H,
            "hair", smooth=False)
    # a few short tufts under the back of the cap rim, singed at the tips
    for k, a in enumerate((60, 90, 120)):
        if a in ():
            continue
        t = math.radians(a)
        if math.sin(t) < -0.6:
            continue          # none over the face
        bx, by = 0.255 * math.cos(t), 0.235 * math.sin(t) + 0.0
        if by < -0.15:
            continue
        base = Vector((bx, by, 1.585))
        out = Vector((math.cos(t), math.sin(t), 0)) * 0.10
        tip = base + out + Vector((0, 0, -0.085 + 0.02 * (k % 3)))
        add(BODY, B.spike(base, tip, 0.05, 5, (0, 0, 0.02)), "hair", H, "hair", smooth=False)
        add(BODY, B.cyl(tip - out * 0.25, tip + out * 0.12, 0.016, 0.003, 4), "hair_singed", H, "hair", smooth=False,
            grad=False)


CAP_T = pivot(-9, "X", (0, 0.0, 1.60))


def build_cap():
    """Short leather cap: six stitched panels in two leather tones over a dark core, rolled rim, brass button; big
    round goggles with team-colour lenses pushed up on the front, on a leather strap."""
    H = "head"
    prof = [(0.292, 1.555), (0.298, 1.605), (0.288, 1.665), (0.255, 1.735), (0.19, 1.80), (0.10, 1.84), (0.03, 1.855)]
    T = CAP_T @ M((0, 0.01, 0))
    add(BODY, B.lathe([(r * 0.965, z) for r, z in prof], 12), "leather_dark", H, "cap_detail", grad=False, m=T)
    n = 8
    for k in range(n):
        a0 = 2 * math.pi * k / n + 0.03
        a1 = 2 * math.pi * (k + 1) / n - 0.035
        tone = "leather_light" if k % 2 == 0 else "leather"
        add(BODY, shell_sector([(r * 1.005, z) for r, z in prof[:-1]] + [(0.05, prof[-1][1] - 0.004)], a0, a1, 0.03, 1),
            tone, H, "cap", m=T)
    add(BODY, B.torus(0.298, 0.03, 16, 4), "leather_dark", H, "cap", m=T @ M((0, 0, 1.56)))
    add(BODY, B.lathe([(0.04, 1.85), (0.045, 1.865), (0.03, 1.875), (0.005, 1.878)], 8), "brass", H, "cap",
        smooth=False, m=T)
    # goggle strap around the cap
    add(BODY, B.torus(0.302, 0.022, 16, 4), "leather_dark", H, "goggles", m=T @ M((0, 0, 1.655), (0, 0, 0), (1, 1, 1.4)))
    # goggles: two big brass-rimmed cups, team lenses, catch lights, bridge
    nrm = Vector((0, -1.0, 0.42)).normalized()
    for s in (1, -1):
        c = Vector((0.112 * s, -0.292, 1.665))
        G = T @ kit.frame(c, nrm, (0, 0, 1))
        add(BODY, B.cyl((0, 0, -0.05), (0, 0, 0.005), 0.088, 0.094, 12), "brass_dark", H, "goggles", m=G)
        add(BODY, B.torus(0.088, 0.024, 12, 3), "brass", H, "goggles", m=G @ M((0, 0, 0.008)))
        add(BODY, B.cyl((0, 0, -0.01), (0, 0, 0.008), 0.072, 0.072, 12), "crystal", H, "goggles", smooth=False, m=G)
        add(BODY, B.sphere((0.022, 0.014, 0.004), 6, 3), "crystal_lit", H, "goggles", smooth=False,
            m=G @ M((-0.028, 0.026, 0.011), (0, 0, 30)))
        for k in range(2):
            a = math.pi / 4 + k * math.pi
            add(BODY, rivet(0.011, 0.01), "brass_dark", H, "goggles", smooth=False,
                m=G @ M((0.088 * math.cos(a), 0.088 * math.sin(a), 0.028)))
    add(BODY, box((0.06, 0.035, 0.04), 0.012, 1), "brass", H, "goggles", smooth=False,
        m=T @ kit.frame(Vector((0, -0.296, 1.665)), nrm, (0, 0, 1)))


KEG_C = Vector((0.0, 0.50, 0.70))      # keg base centre (rest space)
KEG_T = Matrix.Translation(KEG_C) @ Matrix.Rotation(math.radians(-7), 4, "X")
KEG_H, KEG_R = 0.62, 0.25


def build_keg():
    """Round powder keg on the back: 12 separate bevelled staves over a dark core, brass top and bottom hoops, a team
    middle band, lid boards, a brass fuse socket and a curled fuse ending in a bright spark above and behind the head."""
    W = "chest"
    prof = [(KEG_R * 0.86, 0.0), (KEG_R * 0.95, 0.12), (KEG_R, 0.31), (KEG_R * 0.95, 0.50), (KEG_R * 0.86, KEG_H)]
    add(BODY, B.lathe([(r * 0.92, z) for r, z in prof], 12), "wood_dark", W, "keg_detail", grad=False, m=KEG_T)
    n = 12
    for k in range(n):
        a0 = 2 * math.pi * k / n + 0.022
        a1 = 2 * math.pi * (k + 1) / n - 0.022
        tone = "wood_light" if k % 2 == 0 else "wood"
        add(BODY, shell_sector(prof, a0, a1, 0.04, 1), tone, W, "keg", smooth=False, m=KEG_T)
    # hoops: brass near the ends, a wide team band in the middle (the keg's middle hoop stripe)
    for z, r_ in ((0.075, KEG_R * 0.905), (KEG_H - 0.075, KEG_R * 0.905)):
        add(BODY, B.torus(r_ + 0.012, 0.022, 16, 4), "brass", W, "keg", m=KEG_T @ M((0, 0, z), (0, 0, 0), (1, 1, 1.5)))
    add(BODY, B.lathe([(KEG_R - 0.004, 0.255), (KEG_R + 0.016, 0.268), (KEG_R + 0.018, 0.352),
                       (KEG_R - 0.004, 0.365)], 16), "kerchief", W, "team_cloth", m=KEG_T)
    for k in range(6):
        a = 2 * math.pi * (k + 0.5) / 6
        add(BODY, rivet(0.014, 0.01), "brass", W, "keg_detail", smooth=False,
            m=KEG_T @ kit.frame((math.cos(a) * (KEG_R + 0.018), math.sin(a) * (KEG_R + 0.018), 0.31),
                                (math.cos(a), math.sin(a), 0), (0, 0, 1)))
    # lid: three boards with grooves, brass socket off-centre toward the back
    rl = KEG_R * 0.80
    for k, x in enumerate((-rl * 0.62, 0.0, rl * 0.62)):
        w = rl * 0.6
        L = 2 * math.sqrt(max(rl ** 2 - x ** 2, 0.01)) * 0.96
        add(BODY, box((w - 0.01, L, 0.04), 0.012, 1), "wood_light" if k == 1 else "wood", W, "keg_detail",
            smooth=False, m=KEG_T @ M((x, 0, KEG_H - 0.01)))
    sock = Vector((0.04, 0.05, KEG_H + 0.01))
    add(BODY, B.lathe([(0.05, 0.0), (0.055, 0.03), (0.038, 0.05), (0.03, 0.06)], 8), "brass", W, "keg_detail",
        smooth=False, m=KEG_T @ Matrix.Translation(sock))
    # curled fuse: rises from the socket, one loop, up to the spark (tip of the top spark ray at z 2.0)
    ws = KEG_T @ (sock + Vector((0, 0, 0.055)))
    spark_c = Vector((0.10, 0.34, 1.875))
    pts = []
    N = 11
    for i in range(N + 1):
        t = i / N
        ang = 2 * math.pi * 1.15 * t
        rad = 0.085 * math.sin(math.pi * min(t * 1.2, 1.0))
        base = ws.lerp(spark_c, t ** 0.9)
        base.z = ws.z + (spark_c.z - ws.z) * (t ** 0.75)
        pts.append(base + Vector((rad * math.cos(ang), rad * math.sin(ang) * 0.8, 0)))
    pts[-1] = spark_c.copy()
    tube(BODY, pts, 0.021, "fuse", W, "fuse", seg=6, joints=False)
    spark(BODY, spark_c, W, scale=1.0, n_rays=7, seed=3)
    # shoulder straps holding the keg
    for s in (1, -1):
        sp = [(0.15 * s, -0.20, 1.15), (0.17 * s, -0.10, 1.255), (0.17 * s, 0.06, 1.27), (0.16 * s, 0.24, 1.18)]
        for a, b in zip(sp[:-1], sp[1:]):
            n_ = Vector((0, -(b[2] - a[2]), b[1] - a[1])).normalized()
            if n_.z < 0:
                n_ = -n_
            bm, mm = strap(a, b, 0.075, 0.03, n_, bevel=0.01)
            add(BODY, bm, "leather", {"chest": 0.85, f"upperarm.{'l' if s > 0 else 'r'}": 0.15}, "straps",
                smooth=False, m=mm)
        add(BODY, box((0.06, 0.04, 0.05), 0.012, 1), "brass", "chest", "buckles", smooth=False,
            m=M((0.15 * s, -0.215, 1.12), (-30, 0, 0)))


# ===================================================================================================== TNT in hand
def held_matrix(poses, slot, origin, z_axis, y_hint):
    rest = B.arm.data.bones[slot].matrix_local
    return rest @ poses[slot].inverted() @ kit.frame(origin, z_axis, y_hint)


def build_tnt_hand(poses):
    """Oversized 3-stick bundle in the throwing (right) hand, rigid on handslot.r, fuse lit."""
    W = "handslot.r"
    origin = poses[W].translation.copy()
    up = Vector((-0.12, -0.10, 1.0)).normalized()
    T = held_matrix(poses, W, origin, up, (0, 1, 0)) @ M((0, 0, 0.02))
    tnt_bundle(TNTH, W, Matrix.Identity(4), r=0.060, L=0.42, fuse=True, fuse_len=0.12, feat="tnt_hand", post=T,
               seg=8)
    spark(TNTH, (0.0, 0.065 * 1.08 * 0.9, 0.21 + 0.12), W, scale=0.75, n_rays=7, post=T, seed=11)


# ===================================================================================================== props
def mound_h(r, R=0.35, H=0.13):
    t = min(r / R, 1.0)
    return H * (1 - t ** 2) ** 0.8


def clods(o, n, R, H, seed, ring=(0.0, 1.0), colors=("dirt", "dirt_light", "dirt_dark"), size=(0.035, 0.06),
          zlift=0.0, scatter=None):
    r_ = random.Random(seed)
    for k in range(n):
        a = r_.uniform(0, 2 * math.pi)
        rr = R * math.sqrt(r_.uniform(ring[0] ** 2, ring[1] ** 2))
        sz = r_.uniform(*size)
        z = mound_h(rr, R, H) + zlift
        if scatter:
            z += r_.uniform(*scatter)
        add(o, B.sphere((sz, sz * r_.uniform(0.7, 1.0), sz * r_.uniform(0.5, 0.8)), 5, 3), colors[k % len(colors)],
            None, "dirt", smooth=False, m=M((rr * math.cos(a), rr * math.sin(a), z), (r_.uniform(-30, 30),
                                                                                    r_.uniform(-30, 30),
                                                                                    r_.uniform(0, 180))))


def mound(o, R=0.35, H=0.13, seg=16, color="dirt"):
    prof = [(R, 0.0)] + [(R * t, mound_h(R * t, R, H)) for t in (0.85, 0.62, 0.38, 0.14)] + [(0.01, H)]
    add(o, B.lathe(prof, seg), color, None, "mound", smooth=True)


def plate(o, lift=0.0, T=None):
    T = T or Matrix.Identity(4)
    add(o, B.cyl((0, 0, 0.0), (0, 0, 0.045), 0.15, 0.14, 14), "brass", None, "plate", smooth=False, m=T)
    add(o, B.cyl((0, 0, 0.045), (0, 0, 0.06), 0.11, 0.10, 12), "brass_light", None, "plate", smooth=False, m=T)
    for k in range(3):
        add(o, B.box((0.18 - 0.03 * abs(k - 1), 0.016, 0.01), 0.0), "brass_dark", None, "plate", smooth=False,
            grad=False, m=T @ M((0, (k - 1) * 0.045, 0.063)))
    add(o, B.cyl((0, 0, 0.06), (0, 0, 0.085), 0.03, 0.026, 8), "stone_light", None, "plate", smooth=False, m=T)
    for k in range(6):
        a = 2 * math.pi * k / 6
        add(o, rivet(0.012, 0.01), "brass_dark", None, "plate", smooth=False,
            m=T @ M((0.128 * math.cos(a), 0.128 * math.sin(a), 0.044)))


def build_props():
    # buried: the loose-dirt mound, visible to everyone (fairness)
    o = PROPS["buried"]
    mound(o)
    clods(o, 30, 0.35, 0.13, 1)
    for k in range(4):
        a = k * 1.7
        add(o, box((0.07, 0.06, 0.045), 0.018, 1), "stone", None, "pebbles", smooth=False,
            m=M((0.38 * math.cos(a), 0.38 * math.sin(a), 0.02), (0, 0, k * 40)))
    # armed: the same mound parted, brass pressure plate peeking out
    o = PROPS["armed"]
    mound(o)
    plate(o, T=M((0, 0, 0.075)))
    clods(o, 22, 0.35, 0.13, 2, ring=(0.48, 1.0))
    for k in range(3):
        a = k * 2.1 + 0.4
        add(o, box((0.07, 0.06, 0.045), 0.018, 1), "stone", None, "pebbles", smooth=False,
            m=M((0.38 * math.cos(a), 0.38 * math.sin(a), 0.02), (0, 0, k * 40)))
    # owner-only ring: 10 dashes of team colour (Unity shows it only to the owner's team)
    o = PROPS["ring"]
    n = 10
    for k in range(n):
        a0 = 2 * math.pi * k / n
        a1 = a0 + 2 * math.pi / n * 0.55
        add(o, shell_sector([(0.50, 0.0), (0.50, 0.02)], a0, a1, 0.05, 3), "kerchief", None, "team_cloth",
            smooth=False)
    # triggered: blown open, scorched, the plate flipped into the air, clods flung, hazard burst
    o = PROPS["triggered"]
    add(o, B.lathe([(0.42, 0.0), (0.40, 0.035), (0.30, 0.06), (0.18, 0.03), (0.06, 0.012), (0.01, 0.01)], 16),
        "dirt_dark", None, "mound", smooth=True)
    add(o, B.lathe([(0.20, 0.025), (0.19, 0.035), (0.08, 0.02), (0.01, 0.018)], 12), "char", None, "mound",
        smooth=True)
    plate(o, T=M((0.08, -0.05, 0.42), (52, 18, 30)))
    clods(o, 12, 0.42, 0.05, 3, ring=(0.6, 1.0))
    clods(o, 8, 0.55, 0.0, 4, ring=(0.3, 1.0), size=(0.03, 0.05), scatter=(0.15, 0.55))
    spark(o, (0.0, 0.0, 0.18), None, scale=1.8, n_rays=8, seed=5)
    # TNT bundle in flight, fuse lit
    o = PROPS["flight"]
    T = M((0, 0, 0.30), (0, 70, 20))
    offs = tnt_bundle(o, None, T, r=0.05, L=0.34, fuse=True, fuse_len=0.10, seg=8)
    spark(o, T @ Vector((0.0, offs[0][1] * 0.9, 0.17 + 0.10)), None, scale=0.7, n_rays=6, seed=9)
    # crater rim decal: jagged scorched ring (Void Navy, drawn at 30 % opacity in Unity)
    o = PROPS["crater"]
    bm = bmesh.new()
    r_ = random.Random(8)
    seg = 28
    inner, outer, inner_b, outer_b = [], [], [], []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        ri = 0.46 + r_.uniform(-0.02, 0.02)
        ro = 0.78 + r_.uniform(-0.08, 0.14) * (1 if i % 2 else 0.4)
        inner.append(bm.verts.new((ri * math.cos(a), ri * math.sin(a), 0.012)))
        outer.append(bm.verts.new((ro * math.cos(a), ro * math.sin(a), 0.012)))
        inner_b.append(bm.verts.new((ri * math.cos(a), ri * math.sin(a), 0.004)))
        outer_b.append(bm.verts.new((ro * math.cos(a), ro * math.sin(a), 0.004)))
    for i in range(seg):
        j = (i + 1) % seg
        bm.faces.new((inner[i], outer[i], outer[j], inner[j]))
        bm.faces.new((inner_b[j], outer_b[j], outer_b[i], inner_b[i]))
        bm.faces.new((outer[i], outer_b[i], outer_b[j], outer[j]))
        bm.faces.new((inner[j], inner_b[j], inner_b[i], inner[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    add(o, bm, "crater", None, "crater", smooth=False, grad=False)
    # explosion key frame (art direction 6.1): Warm Cream core, Tool Brass body, Meat rim, Stone smoke
    o = PROPS["boom"]
    r_ = random.Random(12)
    add(o, B.sphere((0.55, 0.55, 0.50), 12, 8), "boom_core", None, "boom", grad=False, m=M((0, 0, 0.55)))
    for k in range(6):
        a = 2 * math.pi * k / 6 + 0.3
        add(o, B.sphere((0.36,) * 3, 10, 6), "boom_body", None, "boom", grad=False,
            m=M((0.52 * math.cos(a), 0.52 * math.sin(a), 0.42 + r_.uniform(-0.08, 0.15))))
    for k in range(8):
        a = 2 * math.pi * k / 8
        add(o, B.sphere((0.2,) * 3, 8, 5), "meat", None, "boom", grad=False,
            m=M((0.86 * math.cos(a), 0.86 * math.sin(a), 0.22 + r_.uniform(0, 0.25))))
    for k in range(5):
        a = 2 * math.pi * k / 5 + 0.6
        add(o, B.sphere((0.3,) * 3, 8, 5), "smoke", None, "boom", m=M((0.3 * math.cos(a), 0.3 * math.sin(a), 1.05 +
                                                                          r_.uniform(0, 0.2))))


# ===================================================================================================== main
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
    head_c = Vector(HEAD_C)
    for poly in me.polygons:
        if all(i in vin for i in poly.vertices) and poly.normal.dot((poly.center - head_c).normalized()) > 0.5:
            poly.material_index = slot
            n += 1
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uv.data[li].uv = ((co.x - x0) / (x1 - x0), (co.z - z0) / (z1 - z0)) if poly.material_index == slot \
                else (0.0, 0.0)
    return n


def dims(ob):
    vs = [ob.matrix_world @ v.co for v in ob.data.vertices]
    return {"min": [round(min(v[i] for v in vs), 3) for i in range(3)],
            "max": [round(max(v[i] for v in vs), 3) for i in range(3)]}


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

    B = dkit.DemoBuilder(arm)
    build_legs()
    build_torso()
    build_bandolier()
    build_arms()
    build_neck()
    build_head()
    build_cap()
    build_keg()
    build_tnt_hand(poses)
    coll = bpy.data.collections.new("Demolitions")
    scene.collection.children.link(coll)
    for c in list(arm.users_collection):
        c.objects.unlink(arm)
    coll.objects.link(arm)
    objs = B.finalize(coll, mat)
    face_img = face_decal.save_png_blender(OUT_FACE)
    n_face = assign_face_decal(objs[BODY], face_img)
    TRI_FEAT = dict(sorted(B.tri_feat.items(), key=lambda kv: -kv[1]))

    B = dkit.DemoBuilder(None)
    build_props()
    pcoll = bpy.data.collections.new("DemolitionsProps")
    scene.collection.children.link(pcoll)
    pobjs = B.finalize_static(pcoll, mat)
    PROP_FEAT = dict(sorted(B.tri_feat.items(), key=lambda kv: -kv[1]))
    for ob in pobjs.values():
        ob.hide_render = True
        ob.hide_set(True)

    report = {"objects": {}, "bones": len(arm.data.bones), "display_pose": [DISPLAY_ACTION, DISPLAY_FRAME],
              "face_decal": {"png": os.path.basename(OUT_FACE), "size": face_decal.N, "faces": n_face}}
    for name, ob in list(objs.items()) + list(pobjs.items()):
        report["objects"][name] = {
            "triangles": tris(ob), "vertices": len(ob.data.vertices), "rest_bounds": dims(ob),
            "materials": [m.name for m in ob.data.materials],
            "feature_groups": sorted(g.name for g in ob.vertex_groups if g.name.startswith("feat_")),
        }
    report["bevels"] = BEVELS
    report["triangles_by_feature"] = TRI_FEAT
    report["triangles_props_by_feature"] = PROP_FEAT
    report["triangles_character"] = tris(objs[BODY]) + tris(objs[TNTH])
    report["triangles_props"] = {k: tris(pobjs[v]) for k, v in PROPS.items()}
    report["triangles_mine_armed_with_ring"] = tris(pobjs[PROPS["armed"]]) + tris(pobjs[PROPS["ring"]])

    arm.animation_data_create()
    arm.animation_data.action = bpy.data.actions[DISPLAY_ACTION]
    scene.frame_set(DISPLAY_FRAME)
    bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND, compress=True)

    export.export_fbx(OUT_FBX, arm, [objs[BODY], objs[TNTH]])
    for ob in bpy.context.view_layer.objects:
        ob.select_set(False)
    for ob in pobjs.values():
        ob.hide_set(False)
        ob.select_set(True)
    bpy.context.view_layer.objects.active = pobjs[PROPS["buried"]]
    opts = dict(export.FBX_OPTS)
    opts["object_types"] = {"MESH"}
    res = bpy.ops.export_scene.fbx(filepath=OUT_PROPS_FBX, **opts)
    if "FINISHED" not in res:
        raise SystemExit("props FBX export failed")
    with open(OUT_REPORT, "w") as f:
        json.dump(report, f, indent=1)
        f.write("\n")
    print("BUILD_REPORT " + json.dumps({k: v for k, v in report.items() if k not in ("objects",
                                                                                   "triangles_props_by_feature")}))
    for n_, o in report["objects"].items():
        print("OBJ", n_, o["triangles"], o["rest_bounds"])


main()
