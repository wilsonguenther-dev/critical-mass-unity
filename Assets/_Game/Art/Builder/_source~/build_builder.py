"""Build the Builder (DG-08, proposed; no bible page) and his ladder and rope-bridge props from nothing.

  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python-exit-code 1 -P build_builder.py

Same route as the Miner (branch art/miner-blender): the shared cm_art kit (KayKit 41-joint rig loader, mesh kit, FBX
recipe) is used unmodified; Builder-only code lives in builder_art/ (palette, kit with baked value steps, toon preview,
painted face decal), copied from miner_art and adapted.

Outputs (relative to this folder):
  builder.blend              Rig + Builder_Body + Builder_Mallet, and the prop collections
  ../Builder.fbx             character: Rig + body + mallet (rigid on handslot.r)
  ../BuilderLadder.fbx       BuilderLadder_Carried / _Placed / _Damaged, authored static meshes
  ../BuilderRopeBridge.fbx   BuilderRopeBridge_Carried / _Placed / _Damaged, authored static meshes
  ../BuilderBuildGlow.fbx    BuilderBuildGlow: the short lime placement glow (ring and motes), static
  ../BuilderFace.png         256 px painted face decal (CM_FaceDecal on Builder_Body slot 1)
  build_report.json          triangle counts, dimensions, feature groups

Space: KayKit rest pose (T-pose), Z up, faces -Y, character left = +X, metres. Cap crown 1.93 u above the soles; the
carried ladder rises 0.30 u above the cap (tip 2.23 u).
Prop grid: 1 m cells, one elevation step = 0.75 u (environment kit v3). The placed ladder's origin is the centre of the
foot cell on the ground; the ledge face is the cell edge at y = +0.5, its lip at z = 0.75. The bridge's origin is the deck
centre of the near end (deck top z = +0.05, as the environment kit's env_bridge_span), span along -Y (Unity +Z).
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
from builder_art import bkit, bpal, face_decal, toon3  # noqa: E402

SEED = 804
OUT_BLEND = os.path.join(HERE, "builder.blend")
ART = os.path.dirname(HERE)
OUT_FBX = os.path.join(ART, "Builder.fbx")
OUT_LADDER = os.path.join(ART, "BuilderLadder.fbx")
OUT_BRIDGE = os.path.join(ART, "BuilderRopeBridge.fbx")
OUT_GLOW = os.path.join(ART, "BuilderBuildGlow.fbx")
OUT_FACE = os.path.join(ART, "BuilderFace.png")
OUT_REPORT = os.path.join(HERE, "build_report.json")
DISPLAY_ACTION, DISPLAY_FRAME = "Idle", 1

rng = random.Random(SEED)
B = None
BODY, MALLET = "Builder_Body", "Builder_Mallet"
LAD = {"carried": "BuilderLadder_Carried", "placed": "BuilderLadder_Placed", "damaged": "BuilderLadder_Damaged"}
BRI = {"carried": "BuilderRopeBridge_Carried", "placed": "BuilderRopeBridge_Placed",
       "damaged": "BuilderRopeBridge_Damaged"}
GLOW = "BuilderBuildGlow"
M = kit.mat_from
BV = 0.03
BV_MIN = 0.02
LEG_X = 0.215
BEVELS = {"boxes": 0, "at_min_or_more": 0, "groove_strips_exempt": 0, "hidden_cores_unbevelled": 0, "min_bevel": 9.0}

# ladder dimensions (shared by the back-carried ladder and the three prop states)
LAD_L, LAD_W, LAD_RUNGS = 1.45, 0.52, 6
STEP_H = 0.75          # environment kit elevation step
PLANK_PITCH, PLANK_D, PLANK_T = 0.34, 0.30, 0.08     # environment kit bridge planks (2.04 u tile = 6 planks)
BRIDGE_CELLS, BRIDGE_W = 4, 1.10


def add(obj, bm, color, bones, feat, smooth=True, m=None, **kw):
    B.add(obj, bm, color, bones, feat=feat, smooth=smooth, m=m, **kw)


def box(size, bevel=BV, seg=1):
    """Every bevelled box gets at least BV_MIN; pieces too thin to carry it are counted as exempt groove strips;
    bevel 0 on a thick box marks a hidden core (Miner rule, tallied into build_report.json)."""
    BEVELS["boxes"] += 1
    thin = min(size)
    if bevel <= 0:
        BEVELS["hidden_cores_unbevelled" if thin > 0.04 else "groove_strips_exempt"] += 1
        return B.box(size, 0.0, seg)
    if 0.025 <= thin < 2 * BV_MIN:
        size = tuple(2 * BV_MIN if d == thin else d for d in size)
        BEVELS["thickened_to_carry_bevel"] = BEVELS.get("thickened_to_carry_bevel", 0) + 1
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
    L = (b - a).length
    bm = box((width, thick, L), BV_MIN if bevel is None else bevel, 1)
    return bm, kit.frame((a + b) / 2, b - a, normal_hint)


def rivet(r=0.016, h=0.012):
    return B.cyl((0, 0, 0), (0, 0, h), r, r * 0.7, 5)


def chipped(w, h, chips):
    c0, c1, c2, c3 = chips
    x, y = w / 2, h / 2
    return [(-x + c0, -y), (x - c1, -y), (x, -y + c1), (x, y - c2), (x - c2, y), (-x + c3, y), (-x, y - c3),
            (-x, -y + c0)]


# ----------------------------------------------------------------------------------------------- rope (twisted tube)
def rope_tube(pts, r, seg=6, closed=False, strands=3, amp=0.22, twist=7.0):
    """Closed tube along a polyline whose ring radius ripples as `strands` lobes that spiral `twist` turns per metre:
    a twisted three-strand rope in real geometry. Vertices in the groove between strands are flagged in the int layer
    "alt" (drawn rope_dark). Open tubes get flat caps."""
    P = [Vector(p) for p in pts]
    n = len(P)
    bm = bmesh.new()
    alt = bm.verts.layers.int.new("alt")
    T = []
    for i in range(n):
        if closed:
            t = P[(i + 1) % n] - P[i - 1]
        else:
            t = P[min(i + 1, n - 1)] - P[max(i - 1, 0)]
        T.append(t.normalized())
    ref = Vector((0, 0, 1)) if abs(T[0].z) < 0.9 else Vector((1, 0, 0))
    nrm = (ref - T[0] * ref.dot(T[0])).normalized()
    s = 0.0
    rings = []
    for i in range(n):
        if i > 0:
            s += (P[i] - P[i - 1]).length
            nrm = (nrm - T[i] * nrm.dot(T[i])).normalized()
        bi = T[i].cross(nrm)
        ring = []
        for k in range(seg):
            th = 2 * math.pi * k / seg
            ph = strands * th + 2 * math.pi * twist * s
            rr = r * (1.0 + amp * math.cos(ph))
            v = bm.verts.new(P[i] + (nrm * math.cos(th) + bi * math.sin(th)) * rr)
            v[alt] = 1 if math.cos(ph) < -0.55 else 0
            ring.append(v)
        rings.append(ring)
    last = n if closed else n - 1
    for i in range(last):
        a, b = rings[i], rings[(i + 1) % n]
        for k in range(seg):
            k2 = (k + 1) % seg
            bm.faces.new((a[k], a[k2], b[k2], b[k]))
    if not closed:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def circle_pts(c, normal, R, n=18, a0=0.0):
    c, nz = Vector(c), Vector(normal).normalized()
    ref = Vector((0, 0, 1)) if abs(nz.z) < 0.9 else Vector((1, 0, 0))
    u = (ref - nz * ref.dot(nz)).normalized()
    w = nz.cross(u)
    return [c + (u * math.cos(a0 + 2 * math.pi * i / n) + w * math.sin(a0 + 2 * math.pi * i / n)) * R for i in range(n)]


def sag_pts(a, b, sag, n=12):
    a, b = Vector(a), Vector(b)
    return [a + (b - a) * (i / (n - 1)) - Vector((0, 0, sag * 4 * (i / (n - 1)) * (1 - i / (n - 1)))) for i in range(n)]


# ===================================================================================================== body
def build_legs():
    for s, sd in ((1, "l"), (-1, "r")):
        lx = LEG_X * s
        up, lo, ft, to = f"upperleg.{sd}", f"lowerleg.{sd}", f"foot.{sd}", f"toes.{sd}"
        prof = [(0.125, 0.19), (0.158, 0.25), (0.172, 0.35), (0.178, 0.46), (0.172, 0.58), (0.142, 0.66)]
        add(BODY, B.lathe(prof, 10), "navy_dark", ["hips", up, lo], "trousers", m=M((lx, 0.0, 0)))
        # stone knee pad on a leather strap (bible family: stone armour pieces on every class)
        KP = M((lx, -0.168, 0.345), (90 - 8, 0, 6 * s))
        add(BODY, B.prism(chipped(0.20, 0.17, (0.03, 0.012, 0.03, 0.012)), 0.05, 0.014), "stone_light", lo,
            "knee_pad", smooth=False, m=KP)
        add(BODY, B.torus(0.168, 0.022, 10, 3), "leather", lo, "knee_pad", m=M((lx, 0.0, 0.345), (0, 0, 0), (1, 1, 1.4)))
        add(BODY, rivet(0.018, 0.014), "brass", lo, "knee_pad", smooth=False, m=KP @ M((0, 0.0, 0.025)))
        # rolled trouser cuff, heavy brown boots, brass toe caps, dark soles, rope laces
        add(BODY, B.torus(0.158, 0.045, 10, 4), "navy_roll", lo, "trousers", m=M((lx, 0.02, 0.27)))
        add(BODY, B.cyl((lx, 0.02, 0.06), (lx, 0.02, 0.26), 0.168, 0.158, 12), "leather", [lo, ft], "boots")
        add(BODY, box((0.34, 0.40, 0.16), 0.05, 2), "leather", [ft, to], "boots", m=M((lx + 0.015 * s, -0.06, 0.085)))
        add(BODY, box((0.35, 0.15, 0.14), 0.035, 1), "brass", to, "boots", smooth=False,
            m=M((lx + 0.015 * s, -0.215, 0.078)))
        add(BODY, box((0.355, 0.42, 0.05), 0.02, 1), "leather_dark", [ft, to], "boots", smooth=False,
            m=M((lx + 0.015 * s, -0.065, 0.025)))
        for k, z in enumerate((0.13, 0.175, 0.22)):
            add(BODY, B.box((0.13 - 0.015 * k, 0.025, 0.018), 0.0), "rope", [lo, ft], "boots", smooth=False,
                grad=False, m=M((lx, -0.168 + 0.008 * k, z), (-12, 0, 0)))


def build_torso():
    W3 = ["hips", "spine", "chest"]
    add(BODY, B.sphere((0.32, 0.25, 0.16), 12, 6), "navy_dark", ["hips", "spine"], "trousers", m=M((0, 0.0, 0.63)))
    prof = [(0.28, 0.64), (0.36, 0.73), (0.40, 0.86), (0.40, 1.00), (0.36, 1.13), (0.25, 1.235), (0.10, 1.27)]
    add(BODY, B.lathe(prof, 18), "navy", W3, "shirt", m=M((0, 0.0, 0), (0, 0, 0), (1, 0.80, 1)))
    # open collar, a value step lighter
    add(BODY, B.torus(0.17, 0.035, 12, 4), "navy_roll", "chest", "shirt", m=M((0, 0.005, 1.245), (6, 0, 0), (1, 0.9, 1)))


def build_apron():
    """Canvas apron: bib and skirt panels with a team-colour trim border (a larger team plate behind each canvas
    plate), stitched pockets with brass rivets, a carpenter's square and a folding rule in the pockets, team neck strap
    and a team bow at the back."""
    F, FD = "apron", "apron_detail"
    # bib (chest)
    PB = M((0, -0.318, 0.985), (90 - 9, 0, 0))
    bib = [(-0.20, -0.17), (0.20, -0.17), (0.18, 0.15), (-0.18, 0.15)]
    add(BODY, B.prism([(x * 1.0 + (0.03 if x > 0 else -0.03), y + (0.03 if y > 0 else -0.0)) for x, y in bib], 0.03,
                      0.01), "team", "chest", "team_trim", smooth=False, m=PB)
    add(BODY, B.prism(bib, 0.03, 0.01), "canvas", "chest", F, smooth=False, m=PB @ M((0, 0, 0.018)))
    pocket = [(-0.10, -0.08), (0.10, -0.08), (0.10, 0.06), (-0.10, 0.06)]
    add(BODY, B.prism(pocket, 0.022, 0.006), "canvas_dark", "chest", FD, smooth=False, m=PB @ M((0.02, -0.04, 0.04)))
    for x in (-0.085, 0.125):
        add(BODY, rivet(0.014, 0.01), "brass", "chest", FD, smooth=False, m=PB @ M((x, 0.015, 0.05)))
    # carpenter's square sticking out of the bib pocket (brass L)
    sq = [(0, 0), (0.035, 0), (0.035, 0.17), (0.11, 0.17), (0.11, 0.205), (0, 0.205)]
    add(BODY, B.prism(sq, 0.018), "brass", "chest", FD, smooth=False, m=PB @ M((0.05, -0.03, 0.035)))
    # skirt (belly to mid-thigh), wider, tipped forward
    PS = M((0, -0.335, 0.555), (90 + 7, 0, 0))
    sk = [(-0.27, -0.21), (0.27, -0.21), (0.25, 0.20), (-0.25, 0.20)]
    skw = {"hips": 0.8, "upperleg.l": 0.1, "upperleg.r": 0.1}
    add(BODY, B.prism([(x + (0.03 if x > 0 else -0.03), y - (0.035 if y < 0 else 0.0)) for x, y in sk], 0.03, 0.01),
        "team", skw, "team_trim", smooth=False, m=PS)
    add(BODY, B.prism(sk, 0.03, 0.01), "canvas", skw, F, smooth=False, m=PS @ M((0, 0, 0.018)))
    # centre seam (1 px line) and two pockets with rivets, tools in them
    add(BODY, B.box((0.012, 0.36, 0.01), 0.0), "canvas_dark", skw, FD, smooth=False, grad=False,
        m=PS @ M((0, -0.01, 0.036)))
    for s in (1, -1):
        pk = [(-0.095, -0.08), (0.095, -0.08), (0.10, 0.075), (-0.10, 0.075)]
        PK = PS @ M((0.135 * s, -0.06, 0.04))
        add(BODY, B.prism(pk, 0.024, 0.006), "canvas_dark", skw, FD, smooth=False, m=PK)
        add(BODY, B.box((0.20, 0.03, 0.012), 0.0), "canvas", skw, FD, smooth=False, grad=False,
            m=PK @ M((0, 0.06, 0.012)))      # hem fold line along the pocket top
        for x in (-0.08, 0.08):
            add(BODY, rivet(0.014, 0.01), "brass", skw, FD, smooth=False, m=PK @ M((x, 0.06, 0.016)))
    # folding rule (wood with brass hinge) in his left pocket, a chisel handle in the right
    add(BODY, box((0.045, 0.03, 0.17), 0.012, 1), "pencil", skw, FD, smooth=False,
        m=PS @ M((0.15, 0.04, 0.035), (90, 0, -8)))
    add(BODY, B.cyl((-0.13, 0, 0), (-0.13, 0.15, 0), 0.026, 0.03, 6), "wood_b", skw, FD, smooth=False,
        m=PS @ M((0, 0.0, 0.03)))
    # team neck strap (bib corners up round the neck) and a waist bow at the back
    for s in (1, -1):
        bm, mm = strap((0.17 * s, -0.31, 1.13), (0.13 * s, -0.17, 1.29), 0.05, 0.025, (0, -1, 0.4))
        add(BODY, bm, "team", "chest", "team_trim", smooth=False, m=mm)
    add(BODY, B.torus(0.15, 0.024, 12, 4), "team", "chest", "team_trim", m=M((0, 0.01, 1.30), (-18, 0, 0)))
    add(BODY, B.sphere((0.06, 0.04, 0.045), 6, 4), "team", "spine", "team_trim", m=M((0, 0.30, 0.74)))
    for s in (1, -1):
        add(BODY, B.sphere((0.075, 0.03, 0.04), 6, 4), "team", "spine", "team_trim",
            m=M((0.07 * s, 0.305, 0.75), (0, 25 * s, 0)))
        add(BODY, B.spike((0.03 * s, 0.31, 0.72), (0.09 * s, 0.33, 0.60), 0.03, 5), "team", "spine", "team_trim")


def build_belt():
    """Leather tool belt over the apron: brass buckle, nail pouch on the right hip, hammer loop; rope coil on the left
    hip (three twisted loops tipped outward so the camera sees them round, two team wraps)."""
    W = {"hips": 0.5, "spine": 0.5}
    add(BODY, B.cyl((0, 0, 0.705), (0, 0, 0.80), 0.405, 0.41, 18), "leather", W, "belt",
        m=M((0, 0, 0), (0, 0, 0), (1, 0.86, 1)))
    BK = M((0.0, -0.36, 0.752))
    add(BODY, box((0.13, 0.04, 0.11), 0.02, 1), "brass", W, "belt", smooth=False, m=BK)
    add(BODY, box((0.07, 0.03, 0.05), 0.012, 1), "leather_dark", W, "belt", smooth=False, m=BK @ M((0, -0.012, 0)))
    # nail pouch, right hip (his right = -X)
    p = Vector((-0.40, -0.06, 0.68))
    add(BODY, box((0.12, 0.17, 0.17), 0.03, 1), "leather", W, "belt", smooth=False, m=M(p, (0, 0, 8)))
    add(BODY, box((0.135, 0.185, 0.05), 0.02, 1), "leather_dark", W, "belt", smooth=False,
        m=M(p + Vector((0, 0, 0.075)), (0, 0, 8)))
    for k in range(4):
        add(BODY, B.cyl((0, 0, 0), (0, 0, 0.07), 0.009, 0.009, 4), "brass_dark", W, "belt", smooth=False,
            m=M(p + Vector((-0.02 + 0.015 * (k % 2), -0.05 + 0.03 * k, 0.09)), (8 * (k - 1.5), 6, 0)))
    add(BODY, B.torus(0.05, 0.016, 8, 3), "brass", W, "belt", m=M((-0.39, 0.12, 0.70), (0, 90, 0)))   # hammer loop
    # rope coil
    RC = Vector((0.47, 0.06, 0.60))
    nrm = Vector((0.80, -0.10, 0.60)).normalized()
    Wc = {"hips": 0.7, "spine": 0.3}
    for k in range(3):
        c = RC + nrm * (0.03 * k) + Vector((0, 0, -0.012 * k))
        add(BODY, rope_tube(circle_pts(c, nrm, 0.16 - 0.006 * k, 18, 0.3 * k), 0.032, 6, closed=True, twist=6.0),
            "rope", Wc, "rope_coil", smooth=True, alt="rope_dark")
    # two team wraps binding the loops (short sleeves round the bundle, tangent to the loop)
    for a in (math.radians(75), math.radians(255)):
        ref = Vector((0, 0, 1)) if abs(nrm.z) < 0.9 else Vector((1, 0, 0))
        u = (ref - nrm * ref.dot(nrm)).normalized()
        w = nrm.cross(u)
        pos = RC + nrm * 0.03 + (u * math.cos(a) + w * math.sin(a)) * 0.155
        tang = (-u * math.sin(a) + w * math.cos(a)).normalized()
        add(BODY, B.cyl(pos - tang * 0.045, pos + tang * 0.045, 0.072, 0.072, 8), "team", Wc, "team_trim")
    bm, mm = strap((0.37, 0.0, 0.76), RC + Vector((-0.02, 0, 0.14)), 0.05, 0.03, (1, 0, 0.2))
    add(BODY, bm, "leather_dark", Wc, "belt", smooth=False, m=mm)


def build_arms():
    """Navy shirt with sleeves rolled above the elbow, bare chunky forearms, big work gloves. His left shoulder wears a
    plank pauldron (three bevelled boards on a brass band, nailed); his right a small leather pad (facing read)."""
    Z = 1.107
    for s, sd in ((1, "l"), (-1, "r")):
        ua, la, wr, hd = f"upperarm.{sd}", f"lowerarm.{sd}", f"wrist.{sd}", f"hand.{sd}"
        add(BODY, B.sphere((0.145, 0.145, 0.145), 8, 5), "navy", ["chest", ua], "shirt", m=M((0.26 * s, 0, Z)))
        add(BODY, B.cyl((0.23 * s, 0.0, Z), (0.42 * s, 0.008, Z), 0.145, 0.135, 10), "navy", ["chest", ua, la],
            "sleeve")
        add(BODY, B.torus(0.132, 0.05, 10, 4), "navy_roll", ua, "sleeve", m=M((0.425 * s, 0.008, Z), (0, 90, 0)))
        add(BODY, B.sphere((0.108, 0.108, 0.108), 8, 5), "skin", [ua, la], "skin", m=M((0.47 * s, 0.01, Z)))
        add(BODY, B.cyl((0.47 * s, 0.01, Z), (0.645 * s, 0.0, Z), 0.11, 0.125, 10), "skin", [la, wr], "skin")
        # work gloves: flared leather cuff with a stitched brass band, fist, knuckle ridge, thumb
        add(BODY, B.cyl((0.625 * s, 0, Z), (0.80 * s, 0, Z), 0.145, 0.165, 10), "leather", [la, wr, hd], "gloves")
        add(BODY, B.torus(0.15, 0.03, 10, 3), "brass", [la, wr], "gloves", m=M((0.635 * s, 0, Z), (0, 90, 0)))
        add(BODY, B.sphere((0.175, 0.17, 0.18), 9, 6), "leather_dark", hd, "gloves", m=M((0.90 * s, -0.01, 1.09)))
        add(BODY, box((0.11, 0.22, 0.115), 0.04, 1), "leather_dark", hd, "gloves", m=M((1.0 * s, -0.03, 1.12)))
        add(BODY, B.sphere((0.078, 0.072, 0.09), 6, 5), "leather_dark", hd, "gloves", m=M((0.835 * s, -0.175, 1.13)))
    # plank pauldron, his left
    W = {"upperarm.l": 0.65, "chest": 0.35}
    PADM = kit.frame((0.36, -0.02, 1.22), (0.55, -0.10, 0.83), (0, 0, 1))
    add(BODY, B.lathe([(0.15, -0.06), (0.16, 0.0), (0.13, 0.06), (0.03, 0.08)], 8), "leather", W, "pauldron",
        m=PADM)
    for k, (dx, rot) in enumerate(((-0.075, -8), (0.0, 0), (0.075, 8))):
        add(BODY, box((0.07, 0.28, 0.04), 0.02, 1), "wood" if k != 1 else "wood_b", W, "pauldron", smooth=False,
            m=PADM @ M((dx, 0.0, 0.085 - abs(dx) * 0.25), (0, rot, 0)))
        add(BODY, B.box((0.010, 0.24, 0.012), 0.0), "wood_dark", W, "pauldron_detail", smooth=False, grad=False,
            m=PADM @ M((dx + 0.036, 0.0, 0.09 - abs(dx) * 0.25), (0, rot, 0)))
    add(BODY, box((0.25, 0.05, 0.05), 0.02, 1), "brass", W, "pauldron", smooth=False, m=PADM @ M((0, -0.07, 0.10)))
    for x in (-0.075, 0.0, 0.075):
        add(BODY, rivet(0.013, 0.01), "brass_dark", W, "pauldron_detail", smooth=False,
            m=PADM @ M((x, -0.07, 0.125)))
    # leather pad, his right
    W = {"upperarm.r": 0.65, "chest": 0.35}
    add(BODY, B.lathe([(0.13, -0.05), (0.14, 0.0), (0.11, 0.05), (0.03, 0.07)], 8), "leather", W, "pauldron",
        m=kit.frame((-0.34, -0.01, 1.215), (-0.55, -0.08, 0.83), (0, 0, 1)))


def build_neck():
    add(BODY, B.cyl((0, 0, 1.17), (0, 0, 1.32), 0.11, 0.105, 8), "skin", ["chest", "head"], "skin")


HEAD_C, HEAD_R = (0.0, -0.01, 1.505), (0.31, 0.27, 0.30)


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
    add(BODY, B.sphere(HEAD_R, 14, 10), "skin", H, "skin", m=M(HEAD_C))
    for s in (1, -1):
        add(BODY, B.sphere((0.045, 0.06, 0.08), 6, 4), "skin_shadow", H, "skin", m=M((0.305 * s, 0.02, 1.50)))
        add(BODY, B.sphere((0.10, 0.06, 0.065), 7, 4), "skin", H, "skin", m=M((0.19 * s, -0.205, 1.415)))
        # brows: thick copper wedges; his left one cocked high (attitude), his right low and level
        cock = 1.0 if s > 0 else 0.0
        brow = [(0.04, -0.03), (0.21, 0.0 + 0.03 * cock), (0.24, 0.04 + 0.03 * cock), (0.15, 0.06 + 0.035 * cock),
                (0.05, 0.035)]
        brow = [(x * s, z) for x, z in brow]
        if s < 0:
            brow = list(reversed(brow))
        zb = 1.618 + 0.012 * cock
        add(BODY, B.prism(brow, 0.055, 0.012), "brow", H, "face", smooth=False,
            m=M((0.0, head_front_y(0.13 * s, zb, 0.0) + 0.008, zb), (90 - 14, 0, (-10 if s > 0 else 14) * s)))
        # sideburns into the cap
        add(BODY, box((0.05, 0.09, 0.13), 0.02, 1), "hair", H, "hair", m=M((0.29 * s, -0.06, 1.60), (0, 8 * s, 0)))
    add(BODY, face_plane(), "skin", H, "face_decal", smooth=True, grad=False)
    add(BODY, B.sphere((0.078, 0.072, 0.07), 10, 7), "nose", H, "face", m=M((0, -0.30, 1.455)))
    add(BODY, B.sphere((0.15, 0.10, 0.085), 8, 5), "skin", H, "skin", m=M((0, -0.185, 1.255)))   # strong chin
    # big copper walrus moustache with curled tips, a lighter crown tone on top
    for s in (1, -1):
        add(BODY, B.sphere((0.105, 0.06, 0.05), 8, 5), "hair", H, "moustache",
            m=M((0.082 * s, -0.305, 1.395), (0, 14 * s, 10 * s)))
        add(BODY, B.sphere((0.08, 0.035, 0.022), 7, 4), "hair_light", H, "moustache",
            m=M((0.078 * s, -0.33, 1.42), (0, 14 * s, 10 * s)))
        add(BODY, B.spike((0.16 * s, -0.285, 1.385), (0.26 * s, -0.235, 1.445), 0.042, 6, (0.02 * s, -0.01, -0.03)),
            "hair", H, "moustache")
    # open grin: dark crescent with a teeth band (his left corner higher: a smirk)
    smile = [(-0.115, 0.03), (-0.06, -0.035), (0.06, -0.035), (0.12, 0.045), (0.0, 0.0)]
    add(BODY, B.prism(smile, 0.03), "mouth", H, "face", smooth=False, m=M((0, -0.318, 1.335), (90 - 16, 0, 0)))
    teeth = [(-0.085, 0.012), (0.09, 0.02), (0.07, -0.006), (-0.065, -0.006)]
    add(BODY, B.prism(teeth, 0.03), "teeth", H, "face", smooth=False, m=M((0, -0.324, 1.34), (90 - 16, 0, 0)))
    # hair at the back of the head under the cap, and a copper curl escaping over the forehead
    add(BODY, B.sphere((0.30, 0.25, 0.15), 10, 6), "hair", H, "hair", m=M((0, 0.06, 1.64)))
    for (x, tip, bend) in ((0.12, (0.20, -0.31, 1.60), (0.0, -0.02, 0.03)), (0.04, (0.08, -0.32, 1.585),
                                                                              (0.01, -0.02, 0.02))):
        add(BODY, B.spike((x, -0.22, 1.71), tip, 0.045, 6, bend), "hair", H, "hair")
    # carpenter's pencil behind his right ear (yellow hex, graphite tip)
    add(BODY, B.cyl((-0.30, 0.10, 1.585), (-0.335, -0.17, 1.545), 0.022, 0.022, 6), "pencil", H, "pencil",
        smooth=False)
    add(BODY, B.cyl((-0.335, -0.17, 1.545), (-0.34, -0.215, 1.538), 0.022, 0.004, 6), "graphite", H, "pencil",
        smooth=False)


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


def dome_sector(prof, a0, a1, seg=2):
    bm = bmesh.new()
    rows = []
    for r, z in prof:
        rows.append([bm.verts.new((r * math.cos(a0 + (a1 - a0) * i / seg), r * math.sin(a0 + (a1 - a0) * i / seg), z))
                     for i in range(seg + 1)])
    for j in range(len(prof) - 1):
        for i in range(seg):
            bm.faces.new((rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i]))
    axis = [bm.verts.new((0, 0, prof[0][1])), bm.verts.new((0, 0, prof[-1][1]))]
    for side in (0, seg):
        wall = [rows[j][side] for j in range(len(prof))] + [axis[1], axis[0]]
        bm.faces.new(wall if side == 0 else list(reversed(wall)))
    bm.faces.new([rows[0][i] for i in range(seg, -1, -1)] + [axis[0]])
    bm.faces.new([rows[-1][i] for i in range(seg + 1)] + [axis[1]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ngons = [f for f in bm.faces if len(f.verts) > 4]
    bmesh.ops.triangulate(bm, faces=ngons, quad_method="BEAUTY", ngon_method="EAR_CLIP")
    return bm


CAP_BASE = 1.705


def build_cap():
    """Carpenter's flat cap: a wide crown of eight tweed panels (alternating tones, dark gaps = inner lines) that
    overhangs a short brim, a broad team band, a cloth button on top. Tipped forward 7 degrees."""
    H = "head"
    TIP = (Matrix.Translation((0, -0.02, CAP_BASE)) @ Matrix.Rotation(math.radians(7), 4, "X") @
           Matrix.Translation((0, 0, -CAP_BASE)))
    z0 = CAP_BASE + 0.035
    # v1.1: flatter and slung forward over the brim, so from above it reads as a flat cap (a forward-pointing
    # teardrop), never the Miner's round helmet dome
    prof = [(0.335, z0), (0.38, z0 + 0.035), (0.395, z0 + 0.07), (0.37, z0 + 0.10), (0.28, z0 + 0.125),
            (0.13, z0 + 0.138), (0.02, z0 + 0.14)]
    CROWN = M((0, -0.085, 0), (0, 0, 0), (1.0, 1.18, 1.0))
    add(BODY, B.lathe([(r * 0.975, z) for r, z in prof], 12), "brow", H, "cap_detail", grad=False, m=CROWN,
        post=TIP)
    n = 8
    for k in range(n):
        a0 = 2 * math.pi * k / n + 0.05
        a1 = 2 * math.pi * (k + 1) / n - 0.05
        add(BODY, dome_sector([(r * 1.01, z) for r, z in prof[:-1]] + [(0.03, prof[-1][1] - 0.004)], a0, a1, 2),
            "cap_light" if k % 2 == 0 else "cap", H, "cap", smooth=True, m=CROWN, post=TIP)
    add(BODY, B.sphere((0.05, 0.05, 0.03), 8, 4), "cap", H, "cap", m=M((0, -0.085, z0 + 0.14)), post=TIP)
    # team band round the crown base
    add(BODY, B.cyl((0, 0, CAP_BASE - 0.025), (0, 0, CAP_BASE + 0.06), 0.325, 0.335, 18), "team", H, "team_trim",
        m=M((0, -0.02, 0), (0, 0, 0), (1.0, 1.04, 1.0)), post=TIP)
    # brim: half-ellipse slab in front, tipped down a touch
    pts = [(0.30 * math.cos(math.pi * i / 10), -0.19 * math.sin(math.pi * i / 10)) for i in range(11)]
    add(BODY, B.prism(pts, 0.035, 0.012), "cap_light", H, "cap", smooth=False,
        m=M((0, -0.27, CAP_BASE - 0.005), (-6, 0, 0)), post=TIP)
    add(BODY, B.box((0.42, 0.012, 0.012), 0.0), "brow", H, "cap_detail", smooth=False, grad=False,
        m=M((0, -0.282, CAP_BASE + 0.016), (-6, 0, 0)), post=TIP)       # brim stitch line


# ----------------------------------------------------------------------------------------------- ladder (shared)
def ladder_parts(put, state):
    """Ladder in item space: length along +Z from 0 to LAD_L, width along X, faces -Y. put(bm, color, feat, m, **kw).
    Rails and rungs are separate bevelled pieces; each rail carries a 1 px groove strip on both faces; the rung tenons
    show through the rails' outer faces as team-colour tips."""
    L, W = LAD_L, LAD_W
    rx = W / 2 - 0.035
    broken = state == "damaged"
    for s in (1, -1):
        if broken and s > 0:
            # his right rail snapped at 0.62: lower piece, splintered upper piece kinked outward
            put(box((0.07, 0.06, 0.60), 0.02, 1), "wood", "ladder", M((s * rx, 0, 0.30)))
            put(box((0.07, 0.06, L - 0.70), 0.02, 1), "wood", "ladder",
                M((s * rx, 0, 0.70), (0, -6, 0)) @ M((0, 0, (L - 0.70) / 2)))
            jag = [(-0.035, 0.0), (0.035, 0.0), (0.03, 0.06), (0.01, 0.025), (-0.005, 0.08), (-0.02, 0.03),
                   (-0.035, 0.05)]
            put(B.prism(jag, 0.06), "wood_b", "ladder", M((s * rx, 0, 0.60), (90, 0, 0)))
            put(B.prism([(x, -y) for x, y in reversed(jag)], 0.06), "wood_b", "ladder",
                M((s * rx, 0, 0.70), (90, -6, 0)))
        else:
            put(box((0.07, 0.06, L), 0.02, 1), "wood", "ladder", M((s * rx, 0, L / 2)))
            for y in (-0.031, 0.031):
                put(B.box((0.012, 0.006, L - 0.12), 0.0), "wood_dark", "ladder_detail", M((s * rx, y, L / 2)),
                    grad=False)
    zs = [0.14 + k * (L - 0.30) / (LAD_RUNGS - 1) for k in range(LAD_RUNGS)]
    for k, z in enumerate(zs):
        if broken and k in (2, 4):
            continue
        put(box((W - 0.07, 0.045, 0.045), 0.016, 1), "wood_b", "ladder_detail", M((0, 0, z)))
        put(B.box((0.009, 0.005, 0.03), 0.0), "wood_dark", "ladder_detail", M((0.10, -0.024, z)), grad=False)
        for s in (1, -1):
            put(box((0.02, 0.05, 0.05), 0.008, 1), "team", "team_trim", M((s * (rx + 0.039), 0, z)))
    if broken:
        # rung 2 hangs from the left rail by one end; rung 4 lies on the ground (added by the caller)
        z = zs[2]
        put(box((W - 0.07, 0.045, 0.045), 0.016, 1), "wood_b", "ladder_detail",
            M((-rx + 0.03, 0, z), (0, 58, 0)) @ M((0.5 * (W - 0.07) - 0.03, 0, 0)))
    if state == "carried" or state == "back":
        # folded fittings: brass hooks laid flat along the top of the rails, leather feet folded in, two rope
        # lashings with team wraps holding the rails
        for s in (1, -1):
            put(box((0.05, 0.03, 0.20), 0.012, 1), "brass", "ladder", M((s * rx, -0.045, L - 0.12)))
            put(box((0.09, 0.07, 0.07), 0.02, 1), "leather_dark", "ladder", M((s * rx, 0, 0.035)))
        for z in (0.36, L - 0.40):
            ring = [Vector((x, y, z)) for x, y in ((-rx - 0.06, -0.06), (rx + 0.06, -0.06), (rx + 0.06, 0.06),
                                                    (-rx - 0.06, 0.06))]
            pts = []
            for i in range(4):
                a, b = ring[i], ring[(i + 1) % 4]
                pts.extend(a + (b - a) * (t / 4) for t in range(4))
            put(rope_tube(pts, 0.02, 5, closed=True, twist=8.0), "rope", "ladder", None, alt="rope_dark")
            put(B.cyl((rx + 0.06, -0.05, z), (rx + 0.06, 0.05, z), 0.034, 0.034, 6), "team", "team_trim", None)
    else:
        # deployed fittings: brass hooks over the lip, splayed brass feet on the ground
        for s in (1, -1):
            hook = [(0.0, 0.0), (0.04, 0.0), (0.04, 0.14), (-0.10, 0.20), (-0.13, 0.15), (-0.08, 0.13), (0.0, 0.10)]
            if broken and s > 0:
                continue
            put(B.prism(hook, 0.04, 0.008), "brass", "ladder", M((s * rx, 0.02, L - 0.16), (90, 0, 90)))
            put(box((0.12, 0.13, 0.03), 0.012, 1), "brass_dark", "ladder", M((s * rx, -0.02, 0.0)))


# ===================================================================================================== back gear
BACK_TOP = Vector((0.40, 0.50, 2.23))
BACK_DIR = Vector((0.45, 0.10, 1.0)).normalized()


def build_back():
    """A-frame pack (two converging wooden uprights, crossbars, a foot shelf) on broad team shoulder straps, with the
    folded ladder lashed across it on a diagonal: up over his left shoulder, 0.30 u above the cap."""
    W = "chest"
    for s in (1, -1):
        a, b = Vector((0.20 * s, 0.36, 0.74)), Vector((0.07 * s, 0.40, 1.42))
        bm, mm = strap(a, b, 0.07, 0.06, (0, 1, 0))
        add(BODY, bm, "wood", W, "aframe", smooth=False, m=mm)
    for z, w in ((0.84, 0.36), (1.26, 0.18)):
        add(BODY, box((w, 0.05, 0.06), 0.02, 1), "wood_b", W, "aframe", smooth=False, m=M((0, 0.39, z)))
    add(BODY, box((0.46, 0.20, 0.05), 0.02, 1), "wood", W, "aframe", smooth=False, m=M((0, 0.44, 0.74)))
    add(BODY, box((0.10, 0.08, 0.10), 0.025, 1), "brass", W, "aframe", smooth=False, m=M((0, 0.41, 1.44)))
    # broad team shoulder straps over the shoulders to the frame
    for s in (1, -1):
        w = {"chest": 0.85, f"upperarm.{'l' if s > 0 else 'r'}": 0.15}
        pts = [(0.17 * s, -0.27, 1.06), (0.19 * s, -0.20, 1.22), (0.20 * s, -0.02, 1.30), (0.19 * s, 0.22, 1.25),
               (0.17 * s, 0.35, 1.10), (0.18 * s, 0.37, 0.85)]
        for a, b in zip(pts[:-1], pts[1:]):
            n = Vector((0, -(b[2] - a[2]), b[1] - a[1])).normalized()
            if n.z < 0:
                n = -n
            if abs(b[2] - a[2]) > abs(b[1] - a[1]):
                n = Vector((0, 1 if a[1] > 0 else -1, 0))
            bm, mm = strap(a, b, 0.11, 0.035, n)
            add(BODY, bm, "team", w, "team_trim", smooth=False, m=mm)
        add(BODY, box((0.07, 0.04, 0.06), 0.015, 1), "brass", w, "aframe", smooth=False,
            m=M((0.17 * s, -0.285, 1.07), (-15, 0, 0)))
    bot = BACK_TOP - BACK_DIR * LAD_L
    T = kit.frame(bot, BACK_DIR, (0, -1, 0))

    def put(bm, color, feat, m, **kw):
        add(BODY, bm, color, W, feat, smooth=kw.pop("smooth", False), m=m, post=T, **kw)
    ladder_parts(put, "back")
    # rope lashing tying the ladder to the frame crossbar
    c = T @ Vector((0, 0.07, 0.62))
    add(BODY, rope_tube(circle_pts(c, (0.2, 0, 1), 0.07, 10), 0.018, 5, closed=True), "rope", W, "aframe",
        alt="rope_dark")


# ===================================================================================================== mallet
def held_matrix(poses, slot, origin, z_axis, y_hint):
    rest = B.arm.data.bones[slot].matrix_local
    return rest @ poses[slot].inverted() @ kit.frame(origin, z_axis, y_hint)


GRIP = 0.24


def build_mallet(poses):
    """Oversized carpenter's mallet: banded wooden barrel head with dark end grain, brass collar, a Biomass gem set in a
    brass bezel on both faces, a leather grip with a team wrap, brass pommel."""
    W = "handslot.r"
    parts = []

    def p(bm, color, feat, smooth=False, m=None, **kw):
        parts.append((bm, color, feat, smooth, m, kw))
    p(B.cyl((0, 0, -0.02), (0, 0, 0.78), 0.045, 0.042, 10), "wood_b", "mallet", True)
    p(B.cyl((0, 0, 0.04), (0, 0, 0.32), 0.056, 0.056, 10), "leather", "mallet", True)
    p(B.cyl((0, 0, 0.34), (0, 0, 0.42), 0.06, 0.06, 10), "team", "team_trim", True)
    for z in (0.10, 0.18, 0.26):
        p(B.cyl((0, 0, z - 0.008), (0, 0, z + 0.008), 0.059, 0.059, 8), "leather_dark", "mallet_detail", False)
    p(box((0.12, 0.12, 0.08), 0.025, 1), "brass", "mallet", False, M((0, 0, -0.04)))
    HZ, HL, HR = 0.90, 0.58, 0.175
    p(B.cyl((-HL / 2, 0, HZ), (HL / 2, 0, HZ), HR, HR, 14), "wood", "mallet", True)
    for s in (1, -1):
        p(B.cyl((s * (HL / 2 - 0.004), 0, HZ), (s * (HL / 2 + 0.02), 0, HZ), HR - 0.012, HR - 0.03, 14), "wood_dark",
          "mallet_detail", False)
        p(B.cyl((s * 0.17, 0, HZ), (s * 0.235, 0, HZ), HR + 0.016, HR + 0.016, 14), "brass", "mallet", False)
        p(B.torus(0.07, 0.008, 10, 3), "wood_b", "mallet_detail", False, M((s * (HL / 2 + 0.021), 0, HZ), (0, 90, 0)))
    p(box((0.12, 0.12, 0.10), 0.03, 1), "brass", "mallet", False, M((0, 0, HZ - HR + 0.01)))
    gems = []
    for s in (1, -1):
        p(B.lathe([(0.075, 0.0), (0.085, 0.02), (0.07, 0.035)], 6), "brass", "mallet", False,
          M((0, s * (HR - 0.02), HZ), (-90 * s, 0, 0)))
        gems.append(M((0, s * (HR + 0.005), HZ), (-90 * s, 0, 30)))
    origin = poses[W].translation.copy()
    up = Vector((-0.10, -0.06, 1.0)).normalized()
    T = held_matrix(poses, W, origin, up, (0, 1, 0)) @ M((0, 0, -GRIP))
    for bm, color, feat, smooth, m, kw in parts:
        B.add(MALLET, bm, color, W, feat=feat, smooth=smooth, m=m, post=T, **kw)
    for m4 in gems:
        B.add_crystal(MALLET, W, "gem", m4, 0.065, 0.07, light=(-0.6, -0.5, 0.6), seg=6, waist=0.8, cap=0.55, post=T)


# ===================================================================================================== props
def build_ladder_props():
    for state, obj in LAD.items():
        if state == "carried":
            # carried: the folded ladder lying along X (the carry axis), origin at its centre of mass
            T = kit.frame((-LAD_L / 2, 0, 0.0), (1, 0, 0), (0, 0, -1))
        else:
            # placed: feet on the ground at y = 0.05, rails resting on the 0.75 u lip at y = 0.5
            base = Vector((0, 0.05, 0.025))
            d = (Vector((0, 0.5, STEP_H + 0.03)) - base).normalized()
            T = kit.frame(base, d, (0, -1, 0.0))
            if state == "damaged":
                T = Matrix.Translation(base) @ Matrix.Rotation(math.radians(5), 4, "Y") @ Matrix.Translation(-base) @ T

        def put(bm, color, feat, m, _o=obj, _T=T, **kw):
            add(_o, bm, color, None, feat, smooth=kw.pop("smooth", False), m=m, post=_T, **kw)
        ladder_parts(put, "carried" if state == "carried" else state)
        if state == "damaged":
            add(obj, box((LAD_W - 0.07, 0.045, 0.045), 0.016, 1), "wood_b", None, "ladder_detail", smooth=False,
                m=M((0.30, -0.35, 0.03), (0, 0, 30)))       # fallen rung
            add(obj, box((0.16, 0.035, 0.03), 0.012, 1), "wood_b", None, "ladder", smooth=False,
                m=M((-0.30, -0.25, 0.015), (0, 0, -50)))     # splinter


def plank(obj, x0, x1, y0, y1, z, col, tilt=(0, 0, 0)):
    add(obj, box((x1 - x0, y1 - y0, PLANK_T), 0.025, 1), col, None, "bridge", smooth=False,
        m=M(((x0 + x1) / 2, (y0 + y1) / 2, z - PLANK_T / 2 + 0.05), tilt))
    add(obj, B.box(((x1 - x0) * 0.75, 0.012, 0.006), 0.0), "wood_dark", None, "bridge_detail", smooth=False,
        grad=False, m=M(((x0 + x1) / 2, (y0 + y1) / 2 + 0.03, z + 0.052), tilt))


def bridge_post(obj, x, y, lean=0.0):
    add(obj, box((0.16, 0.16, 1.10), 0.03, 1), "wood_dark", None, "bridge", smooth=False,
        m=M((x, y, 0.25), (lean, 0, 0)))
    add(obj, box((0.20, 0.20, 0.06), 0.02, 1), "wood", None, "bridge", smooth=False,
        m=M((x, y + 0.0, 0.81), (lean, 0, 0)))
    add(obj, B.cyl((x, y, 0.62), (x, y, 0.72), 0.095, 0.095, 8), "team", None, "team_trim", m=M((0, 0, 0)))


def build_bridge_props():
    L = BRIDGE_CELLS * 1.02          # 4.08 u = two environment tiles (12 planks at the kit's 0.34 u pitch)
    hw = BRIDGE_W / 2
    for state in ("placed", "damaged"):
        o = BRI[state]
        dmg = state == "damaged"
        missing = {4, 5, 9} if dmg else set()
        n = int(round(L / PLANK_PITCH))
        sag = 0.06 if not dmg else 0.16
        for i in range(n):
            y0 = -(i * PLANK_PITCH + 0.02)
            t = (i + 0.5) / n
            z = -sag * 4 * t * (1 - t)
            if i in missing:
                continue
            col = "wood" if i % 2 else "wood_b"
            dx = rng.uniform(-0.03, 0.03)
            tilt = (0, rng.uniform(-2, 2), 0)
            if dmg and i == 6:
                tilt = (28, 0, 8)            # snapped plank hanging nose-down
                z -= 0.12
            plank(o, -hw + dx, hw + dx, y0 - PLANK_D, y0, z, col, tilt)
        # two stringers under the deck (sagging beams)
        for x in (-hw + 0.18, hw - 0.18):
            pts = sag_pts((x, 0.0, -0.02), (x, -L, -0.02), sag, 10)
            for a, b in zip(pts[:-1], pts[1:]):
                if dmg and x > 0 and a.y < -1.4 and b.y > -2.1:
                    continue                 # stringer broken under the gap
                bm, mm = strap(a, b, 0.10, 0.06, (0, 0, 1), 0.02)
                add(o, bm, "wood_dark", None, "bridge", smooth=False, m=mm)
        # end posts, twisted handrail ropes, vertical ties
        for y in (0.08, -L - 0.08):
            for x in (-hw - 0.08, hw + 0.08):
                lean = -12 if (dmg and y < -1 and x > 0) else 0
                bridge_post(o, x, y, lean)
        for x in (-hw - 0.08, hw + 0.08):
            a, b = Vector((x, 0.08, 0.74)), Vector((x, -L - 0.08, 0.74))
            if dmg and x > 0:
                # snapped handrail: both halves hang down from their posts
                add(o, rope_tube([a, a + Vector((0.0, -0.5, -0.30)), a + Vector((0.03, -0.75, -0.62))], 0.032, 6),
                    "rope", None, "bridge", smooth=True, alt="rope_dark")
                add(o, rope_tube([b, b + Vector((0.0, 0.6, -0.35)), b + Vector((-0.02, 0.85, -0.66))], 0.032, 6),
                    "rope", None, "bridge", smooth=True, alt="rope_dark")
            else:
                add(o, rope_tube(sag_pts(a, b, 0.14 if not dmg else 0.22, 16), 0.034, 6), "rope", None, "bridge",
                    smooth=True, alt="rope_dark")
                for k in range(1, 5):
                    t = k / 5
                    y = 0.08 - (L + 0.16) * t
                    zt = 0.74 - (0.14 if not dmg else 0.22) * 4 * t * (1 - t)
                    zd = -sag * 4 * t * (1 - t) + 0.05
                    add(o, rope_tube([(x, y, zt), (x * 0.96, y, zd)], 0.018, 5), "rope", None, "bridge_detail",
                        smooth=True, alt="rope_dark")
        if not dmg:
            # small team pennants on the near posts (structure banners)
            for x in (-hw - 0.08, hw + 0.08):
                tri = [(0.0, 0.0), (0.0, 0.16), (0.22 * (1 if x > 0 else -1), 0.08)]
                add(o, B.prism(tri, 0.015), "team", None, "team_trim", smooth=False, m=M((x, 0.08, 0.94), (90, 0, 0)))
                add(o, B.cyl((x, 0.08, 0.84), (x, 0.08, 1.12), 0.012, 0.012, 5), "wood_dark", None, "bridge")
        else:
            for (x, y, rz) in ((0.20, -1.55, 25), (-0.10, -3.1, -40)):
                add(o, box((0.42, 0.10, 0.05), 0.02, 1), "wood_b", None, "bridge", smooth=False,
                    m=M((x, y, -0.55), (0, 12, rz)))          # fallen plank halves below the gap (shown hanging)
    # carried roll: the planks rolled up on their ropes, strapped, a team wrap and a rope carry loop
    o = BRI["carried"]
    n = 12
    for i in range(n):
        a = i * 2.4 * math.pi / n
        r = 0.30 - 0.11 * i / n
        c = Vector((0, r * math.cos(a), 0.36 + r * math.sin(a)))
        add(o, box((BRIDGE_W + 0.02, PLANK_D * 0.62, PLANK_T), 0.025, 1), "wood" if i % 2 else "wood_b", None,
            "bridge", smooth=False, m=M(c, (math.degrees(a) + 90, 0, 0)))
    for x in (-0.38, 0.38):
        add(o, rope_tube(circle_pts((x, 0, 0.36), (1, 0, 0), 0.36, 20), 0.03, 6, closed=True), "rope", None,
            "bridge", smooth=True, alt="rope_dark")
    add(o, B.cyl((-0.08, 0, 0.36), (0.08, 0, 0.36), 0.37, 0.37, 18), "team", None, "team_trim")
    add(o, rope_tube([(-0.38, 0, 0.72), (-0.2, 0, 0.98), (0.2, 0, 0.98), (0.38, 0, 0.72)], 0.025, 5), "rope", None,
        "bridge", smooth=True, alt="rope_dark")


def build_glow():
    """Short lime Biomass placement glow (art only; the timing/VFX code is out of scope): a flat ring and rising
    diamond motes, Biomass colour, fixed (never team)."""
    add(GLOW, B.torus(0.62, 0.035, 32, 4), "crystal", None, "glow", m=M((0, 0, 0.02), (0, 0, 0), (1, 1, 0.4)))
    for k in range(8):
        a = 2 * math.pi * k / 8
        h = 0.18 + 0.12 * (k % 3)
        B.add_crystal(GLOW, None, "glow", M((0.5 * math.cos(a), 0.5 * math.sin(a), h)), 0.045, 0.11, seg=4,
                      waist=0.9, cap=0.5)


# ===================================================================================================== main
def dims(ob):
    vs = [ob.matrix_world @ v.co for v in ob.data.vertices]
    lo = [min(v[i] for v in vs) for i in range(3)]
    hi = [max(v[i] for v in vs) for i in range(3)]
    return {"min": [round(x, 3) for x in lo], "max": [round(x, 3) for x in hi]}


def tris(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def export_static(path, objs):
    for ob in bpy.context.view_layer.objects:
        ob.select_set(False)
    for ob in objs:
        ob.hide_set(False)
        ob.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    opts = dict(export.FBX_OPTS)
    opts["object_types"] = {"MESH"}
    res = bpy.ops.export_scene.fbx(filepath=path, **opts)
    if "FINISHED" not in res:
        raise SystemExit(f"FBX export failed: {path}")
    for ob in objs:
        ob.hide_set(True)


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

    B = bkit.BuilderKit(arm)
    build_legs()
    build_torso()
    build_apron()
    build_belt()
    build_arms()
    build_neck()
    build_head()
    build_cap()
    build_back()
    build_mallet(poses)
    coll = bpy.data.collections.new("Builder")
    scene.collection.children.link(coll)
    for c in list(arm.users_collection):
        c.objects.unlink(arm)
    coll.objects.link(arm)
    objs = B.finalize(coll, mat)
    face_img = face_decal.save_png_blender(OUT_FACE)
    n_face = assign_face_decal(objs[BODY], face_img)
    TRI_FEAT = dict(sorted(B.tri_feat.items(), key=lambda kv: -kv[1]))

    props = {}
    for cname, builder_fn in (("BuilderLadder", build_ladder_props), ("BuilderRopeBridge", build_bridge_props),
                              ("BuilderBuildGlow", build_glow)):
        B = bkit.BuilderKit(None)
        builder_fn()
        pc = bpy.data.collections.new(cname)
        scene.collection.children.link(pc)
        po = B.finalize_static(pc, mat)
        for ob in po.values():
            ob.hide_render = True
            ob.hide_set(True)
        props[cname] = po

    report = {"objects": {}, "bones": len(arm.data.bones), "display_pose": [DISPLAY_ACTION, DISPLAY_FRAME],
              "face_decal": {"png": os.path.basename(OUT_FACE), "size": face_decal.N, "faces": n_face}}
    allp = {k: v for po in props.values() for k, v in po.items()}
    for name, ob in list(objs.items()) + list(allp.items()):
        report["objects"][name] = {
            "triangles": tris(ob), "vertices": len(ob.data.vertices), "rest_bounds": dims(ob),
            "materials": [m.name for m in ob.data.materials],
            "feature_groups": sorted(g.name for g in ob.vertex_groups if g.name.startswith("feat_")),
        }
    report["bevels"] = BEVELS
    report["triangles_by_feature"] = TRI_FEAT
    report["triangles_character"] = tris(objs[BODY]) + tris(objs[MALLET])
    report["triangles_props"] = {k: tris(v) for k, v in allp.items()}

    arm.animation_data_create()
    arm.animation_data.action = bpy.data.actions[DISPLAY_ACTION]
    scene.frame_set(DISPLAY_FRAME)
    bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND, compress=True)

    export.export_fbx(OUT_FBX, arm, [objs[BODY], objs[MALLET]])
    export_static(OUT_LADDER, [props["BuilderLadder"][n] for n in LAD.values()])
    export_static(OUT_BRIDGE, [props["BuilderRopeBridge"][n] for n in BRI.values()])
    export_static(OUT_GLOW, [props["BuilderBuildGlow"][GLOW]])
    with open(OUT_REPORT, "w") as f:
        json.dump(report, f, indent=1)
        f.write("\n")
    print("BUILD_REPORT " + json.dumps({k: v for k, v in report.items() if k not in ("objects", "triangles_by_feature")}))
    for n, o in report["objects"].items():
        print("OBJ", n, o["triangles"], o["rest_bounds"])


main()
