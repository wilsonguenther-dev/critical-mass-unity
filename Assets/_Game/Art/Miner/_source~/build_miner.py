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
from miner_art import face_decal, mkit, mpal, toon3  # noqa: E402

SEED = 803
OUT_BLEND = os.path.join(HERE, "miner.blend")
OUT_FBX = os.path.join(os.path.dirname(HERE), "Miner.fbx")
OUT_BARR_FBX = os.path.join(os.path.dirname(HERE), "MinerBarricade.fbx")
OUT_REPORT = os.path.join(HERE, "build_report.json")
OUT_FACE = os.path.join(os.path.dirname(HERE), "MinerFace.png")
DISPLAY_ACTION, DISPLAY_FRAME = "Idle", 1

rng = random.Random(SEED)
B = None
BODY, PICK, HAMMER = "Miner_Body", "Miner_PickHammer", "Miner_PickHammer_Hammer"
BAR = {"folded": "Miner_Barricade_Folded", "deployed": "Miner_Barricade_Deployed", "damaged": "Miner_Barricade_Damaged"}
M = kit.mat_from
BV = 0.03          # standard bevel (art direction 3.5: 0.02 to 0.04 u)
LEG_X = 0.255      # v2: wider stance (bible p08 front, boots about 1.2 u across)
HS = 1.28          # v2: helmet dome stretched up from the brim (bible: dome top about 8 % below the crystal tip)


def hz(z):
    return 1.545 + (z - 1.545) * HS


HEAD_K, HEAD_PIVOT = 0.93, 1.30   # v2: head + helmet scaled about the neck (bible legs read longer; brief head share 33-38 %)
HEAD_POST = (Matrix.Translation((0, 0, HEAD_PIVOT)) @ Matrix.Diagonal((HEAD_K, HEAD_K, HEAD_K, 1.0)) @
             Matrix.Translation((0, 0, -HEAD_PIVOT)))
HEAD_WIDEN = Matrix.Diagonal((1.02, 1.02, 1.0, 1.0))   # v2: bible p08 front measures the helmet 0.76 u, face 0.70 u
_post = None


def add(obj, bm, color, bones, feat, smooth=True, m=None, **kw):
    if _post is not None and "post" not in kw:
        kw["post"] = _post
    B.add(obj, bm, color, bones, feat=feat, smooth=smooth, m=m, **kw)


def head_crystal(*a, **kw):
    kw.setdefault("post", _post)
    B.add_crystal(*a, **kw)


BV_MIN = 0.02      # art direction 3.5 minimum bevel
BEVELS = {"boxes": 0, "at_min_or_more": 0, "groove_strips_exempt": 0, "hidden_cores_unbevelled": 0, "min_bevel": 9.0}


def box(size, bevel=BV, seg=1):
    """v2: every bevelled box gets at least BV_MIN; pieces too thin to carry it (1 px groove strips, < 0.04 u) are
    counted as exempt; bevel 0 marks a hidden core. Tallied into build_report.json."""
    BEVELS["boxes"] += 1
    thin = min(size)
    if bevel <= 0:
        BEVELS["hidden_cores_unbevelled" if thin > 0.04 else "groove_strips_exempt"] += 1
        return B.box(size, 0.0, seg)
    if 0.025 <= thin < 2 * BV_MIN:      # boards, braces, plates: thicken to 0.04 so they carry the full bevel
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
    """Box from point a to point b (length axis), `thick` along normal_hint, `width` across."""
    a, b = Vector(a), Vector(b)
    L = (b - a).length
    bm = box((width, thick, L), BV_MIN if bevel is None else bevel, 1)
    return bm, kit.frame((a + b) / 2, b - a, normal_hint)


def ellipse_pos(theta_deg, rx, ry, z):
    t = math.radians(theta_deg)
    return (rx * math.sin(t), -ry * math.cos(t), z)


# ===================================================================================================== body
def build_legs():
    for s, sd in ((1, "l"), (-1, "r")):
        lx = LEG_X * s
        up, lo, ft, to = f"upperleg.{sd}", f"lowerleg.{sd}", f"foot.{sd}", f"toes.{sd}"
        prof = [(0.13, 0.19), (0.165, 0.25), (0.185, 0.35), (0.19, 0.46), (0.185, 0.58), (0.15, 0.66)]
        add(BODY, B.lathe(prof, 12), "navy_dark", ["hips", up, lo], "trousers", m=M((lx, 0.0, 0)))
        # brass/brown knee guard: brass plate, leather inset, rivet
        kg = M((lx, -0.165, 0.335), (-10, 0, 8 * s))
        add(BODY, box((0.23, 0.07, 0.19), 0.03, 2), "brass", lo, "knee_guard", smooth=False, m=kg)
        add(BODY, box((0.13, 0.03, 0.10), 0.012, 1), "stone_light", lo, "knee_guard", smooth=False,
            m=kg @ M((0, -0.045, 0)))
        B.add_crystal(BODY, lo, "team_glow", kg @ M((0, -0.045, 0), (90, 0, 30)), 0.05, 0.035, seg=6, waist=0.8,
                      cap=0.55)
                # heavy brown boots with brass toe caps
        add(BODY, B.cyl((lx, 0.02, 0.06), (lx, 0.02, 0.27), 0.185, 0.175, 12), "leather", [lo, ft], "boots")
        add(BODY, B.torus(0.18, 0.045, 12, 5), "kerchief", lo, "team_cloth", m=M((lx, 0.02, 0.27)))   # team trim
        add(BODY, box((0.37, 0.42, 0.17), 0.05, 2), "leather", [ft, to], "boots", m=M((lx + 0.02 * s, -0.065, 0.09)))
        add(BODY, box((0.385, 0.16, 0.145), 0.035, 2), "brass", to, "boots", smooth=False,
            m=M((lx + 0.02 * s, -0.23, 0.082)))
        add(BODY, box((0.38, 0.44, 0.05), 0.02, 1), "leather_dark", [ft, to], "boots", smooth=False,
            m=M((lx + 0.02 * s, -0.07, 0.025)))
        add(BODY, box((0.06, 0.04, 0.06), 0.02, 1), "brass", [lo, ft], "boots", smooth=False,
            m=M((lx + 0.0, -0.18, 0.16)))


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
            bm, mm = strap(a, b, 0.11, 0.045, n)
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
        add(BODY, B.sphere((0.20, 0.195, 0.205), 12, 8), "leather_dark", hd, "gloves", m=M((0.91 * s, -0.01, 1.09)))
        add(BODY, box((0.11, 0.23, 0.12), 0.04, 1), "leather_dark", hd, "gloves", m=M((1.01 * s, -0.03, 1.12)))
        add(BODY, B.sphere((0.08, 0.075, 0.095), 8, 6), "leather_dark", hd, "gloves", m=M((0.84 * s, -0.18, 1.13)))


def build_neck():
    add(BODY, B.cyl((0, 0, 1.17), (0, 0, 1.32), 0.11, 0.105, 12), "skin", ["chest", "head"], "skin")
    # blue neckerchief (team cloth): thick roll, front knot, hanging point
    add(BODY, B.torus(0.255, 0.11, 14, 7), "kerchief", "chest", "team_cloth",
        m=M((0, -0.015, 1.245), (8, 0, 0), (1.06, 0.95, 0.8)))
    add(BODY, B.sphere((0.10, 0.07, 0.07), 10, 6), "kerchief", "chest", "team_cloth", m=M((0.14, -0.25, 1.15)))


def face_plane(rows=4, cols=8):
    """UV-less grid hugging the front of the head sphere (head space, before HEAD_WIDEN), 8 mm proud of it, covering the
    face_decal extent. UVs and the decal material are assigned after finalize (assign_face_decal)."""
    import bmesh
    cx, cy, cz, rx, ry, rz = 0.0, -0.01, 1.44, 0.28, 0.26, 0.27
    bm = bmesh.new()
    grid = []
    for j in range(rows + 1):
        z = face_decal.Z0 + (face_decal.Z1 - face_decal.Z0) * j / rows
        row = []
        for i in range(cols + 1):
            x = face_decal.X0 + (face_decal.X1 - face_decal.X0) * i / cols
            k = 1.0 - (x / rx) ** 2 - ((z - cz) / rz) ** 2
            y = cy - ry * math.sqrt(max(k, 0.02)) - 0.008
            row.append(bm.verts.new((x, y, z)))
        grid.append(row)
    back = [[bm.verts.new(v.co + Vector((0, 0.004, 0))) for v in row] for row in grid]   # closed thin slab
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
    global _post
    _post = HEAD_POST @ HEAD_WIDEN
    H = "head"
    add(BODY, B.sphere((0.28, 0.26, 0.27), 16, 11), "skin", H, "skin", m=M((0, -0.01, 1.44)))
    for s in (1, -1):
        add(BODY, B.sphere((0.035, 0.05, 0.065), 8, 6), "skin_shadow", H, "skin", m=M((0.262 * s, 0.03, 1.45)))
        # v2: brow ridge in skin (form under the painted heavy brow) instead of v1's dark boxes
        add(BODY, B.sphere((0.085, 0.035, 0.03), 8, 5), "skin", H, "skin", m=M((0.10 * s, -0.235, 1.515), (0, 8 * s, 0)))
    # v2 face: painted 256 px decal (eyes, lids, heavy brows, blush) on a plane hugging the head front
    add(BODY, face_plane(), "skin", H, "face_decal", smooth=True, grad=False)
    # big round red nose
    add(BODY, B.sphere((0.088, 0.078, 0.078), 12, 8), "nose", H, "face", m=M((0, -0.30, 1.405)))
    # v2 beard: a shaped, two-tone form (mid-brown mass, lighter clump crowns and mustache), short (tip at z 1.19)
    add(BODY, B.sphere((0.275, 0.205, 0.125), 14, 7), "beard", H, "beard", m=M((0, -0.035, 1.33)))
    clumps = [   # base, tip, radius, bend, tone
        ((0.23, -0.08, 1.42), (0.24, -0.14, 1.27), 0.09, (0.02, -0.02, 0.0), "beard"),
        ((-0.23, -0.08, 1.42), (-0.24, -0.14, 1.27), 0.09, (-0.02, -0.02, 0.0), "beard"),
        ((0.24, 0.0, 1.53), (0.25, -0.06, 1.38), 0.065, (0.015, 0, 0), "beard"),
        ((-0.24, 0.0, 1.53), (-0.25, -0.06, 1.38), 0.065, (-0.015, 0, 0), "beard"),
        ((0.18, -0.17, 1.33), (0.20, -0.25, 1.21), 0.10, (0.02, -0.03, 0), "beard_light"),
        ((-0.18, -0.17, 1.33), (-0.20, -0.25, 1.21), 0.10, (-0.02, -0.03, 0), "beard_light"),
        ((0.075, -0.21, 1.31), (0.08, -0.30, 1.19), 0.10, (0.0, -0.03, 0), "beard"),
        ((-0.075, -0.21, 1.31), (-0.08, -0.30, 1.19), 0.10, (0.0, -0.03, 0), "beard"),
        ((0.0, -0.21, 1.30), (0.0, -0.30, 1.18), 0.09, (0, -0.03, 0), "beard_light"),
    ]
    for b, t, r, bend, tone in clumps:
        add(BODY, B.spike(b, t, r, 8, bend), tone, H, "beard", smooth=True)
    for s in (1, -1):   # mustache: two thick lighter curls sweeping out from under the nose
        add(BODY, B.spike((0.025 * s, -0.295, 1.36), (0.185 * s, -0.25, 1.325), 0.055, 8, (0.0, -0.025, 0.018)),
            "beard_light", H, "beard", smooth=True)
    # wide open grin under the mustache: dark mouth, cream teeth band
    grin = [(-0.10, 0.016), (-0.05, -0.036), (0.05, -0.036), (0.10, 0.016), (0.0, 0.0)]
    add(BODY, B.prism(grin, 0.03), "mouth", H, "face", smooth=False, m=M((0, -0.278, 1.285), (90 - 12, 0, 0)))
    teeth = [(-0.088, 0.012), (0.088, 0.012), (0.07, -0.009), (-0.07, -0.009)]
    add(BODY, B.prism(teeth, 0.03), "teeth", H, "face", smooth=False, m=M((0, -0.284, 1.285), (90 - 12, 0, 0)))


def assign_face_decal(ob, image):
    """UVs (planar, from the rest position, exactly the decal's x/z extent) and the CM_FaceDecal material on the faces
    of the face plane; everything else keeps CM_Toon (slot 0)."""
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
    head_c = Vector((0.0, -0.01, 1.44))
    for poly in me.polygons:
        # front sheet only: outward-facing (back sheet faces the head, rim walls are tangent)
        if all(i in vin for i in poly.vertices) and poly.normal.dot((poly.center - head_c).normalized()) > 0.5:
            poly.material_index = slot
            n += 1
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uv.data[li].uv = ((co.x - x0) / (x1 - x0), (co.z - z0) / (z1 - z0)) if poly.material_index == slot \
                else (0.0, 0.0)
    return n


HELMET_SCALE = Matrix.Diagonal((0.92, 0.95, 1.0, 1.0))   # v2: bible p08 front helmet is narrower than v1's widened dome


def build_helmet():
    global _post
    _post = HEAD_POST @ HELMET_SCALE
    H = "head"
    # v2 dome: taller than v1 but tapering to a narrower crown (bible front: 0.44 u across at z 1.8)
    prof = [(0.29, 1.545), (0.325, 1.58), (0.318, 1.64), (0.29, 1.70), (0.235, 1.755), (0.15, 1.795), (0.03, 1.812)]
    add(BODY, B.lathe(prof, 16), "helmet", H, "helmet", m=M((0, 0.005, 0)))
    # brim (rounded, a step lighter), groove line, brass trim band + rivets
    add(BODY, B.torus(0.322, 0.05, 16, 5), "stone_light", H, "helmet", m=M((0, -0.012, 1.55), (0, 0, 0),
                                                                            (1.0, 1.07, 0.62)))
    add(BODY, B.torus(0.32, 0.010, 16, 3), "helmet_seam", H, "helmet_detail", m=M((0, 0.004, 1.562)))
    add(BODY, B.torus(0.325, 0.026, 16, 4), "brass", H, "helmet", m=M((0, 0.004, hz(1.59))))
    for k in range(6):
        a = 2 * math.pi * (k + 0.5) / 6
        add(BODY, B.sphere((0.016, 0.016, 0.016), 5, 3), "brass_dark", H, "helmet_detail",
            m=M((0.347 * math.cos(a), 0.004 + 0.347 * math.sin(a), hz(1.59))))
    # plate grooves (segmented stone hard hat): 4 meridians as thin strips riding the dome
    pp = [(0.324, 1.60), (0.318, 1.655), (0.282, 1.715)]
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
    add(BODY, box((0.24, 0.10, 0.17), 0.03, 2), "brass", H, "helmet_lamp", smooth=False,
        m=M((0, -0.255, 1.68), (-34, 0, 0)))
    add(BODY, B.torus(0.13, 0.032, 12, 5), "brass", H, "helmet_lamp", m=M((0, -0.185, 1.755), (-17, 0, 0)))
    # big glowing crystal lamp (v2: a chunky hex gem, not a thin spike): tip at z 2.0, 0.17 above the dome
    axis = Vector((0, -0.30, 0.954)).normalized()
    base = Vector((0, -0.17, 1.74))
    tip_z = HEAD_PIVOT + (2.0 - HEAD_PIVOT) / HEAD_K       # tip lands on z 2.0 after HEAD_POST
    h_main = (tip_z - base.z) / axis.z
    head_crystal(BODY, H, "team_glow", kit.frame(base, axis, (0, 1, 0)), 0.17, h_main, seg=6, waist=0.94,
                 cap=0.28)
    # v2: no side shards (the bible front shows one gem); a small back shard keeps the gem cluster readable from above
    head_crystal(BODY, H, "team_glow", kit.frame((0.0, -0.08, 1.77), Vector((0, 0.35, 0.94)).normalized(), (0, 1, 0)),
                 0.06, 0.12, seg=6, waist=0.8, cap=0.38)


def build_crate():
    global _post
    _post = None
    W = "chest"
    C = Vector((0.0, 0.45, 1.14))      # v2: bible profile crate spans z 0.77 to 1.51, close on the back
    SX, SY, SZ = 0.70, 0.60, 0.74      # v2: bible back view draws it shoulder-wide and tall; top-down 0.70 x 0.60
    F = "crate"
    FD = "crate_detail"      # boards, brace, lid, seams: 1.5 px inner line

    def at(x, y, z, rot=(0, 0, 0)):
        return M((C.x + x, C.y + y, C.z + z), rot)

    add(BODY, B.box((SX - 0.05, SY - 0.05, SZ - 0.05), 0.0), "wood_dark", W, F, smooth=False, m=at(0, 0, 0))
    # back face: three vertical boards with 1 px grooves between, X brace proud of them
    bw = (SX - 0.10) / 3
    for x in (-bw, 0.0, bw):
        add(BODY, box((bw - 0.012, 0.03, SZ - 0.14), 0.02, 1), "wood", W, FD, smooth=False, m=at(x, SY / 2 - 0.02, 0))
    diag = math.hypot(SX - 0.12, SZ - 0.12)
    ang = math.degrees(math.atan2(SX - 0.12, SZ - 0.12))
    for sgn in (1, -1):
        add(BODY, box((0.10, 0.036, diag), 0.02, 1), "wood", W, FD, smooth=False,
            m=at(0, SY / 2 + 0.008, 0, (0, ang * sgn, 0)))
    # front-facing top lid boards and bottom
    for y in (-(SY - 0.10) / 4, (SY - 0.10) / 4):
        add(BODY, box((SX - 0.10, (SY - 0.10) / 2 - 0.012, 0.03), 0.012, 1), "wood", W, FD, smooth=False,
            m=at(0, y, SZ / 2 - 0.02))
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
    for (x, y, z, ax, r, h) in ((0.21, 0.08, SZ / 2 - 0.02, (0.30, 0.15, 0.94), 0.085, 0.24),
                                 (0.07, -0.02, SZ / 2 - 0.02, (-0.1, -0.25, 0.95), 0.06, 0.16),
                                 (0.25, -0.10, SZ / 2 - 0.02, (0.5, -0.35, 0.8), 0.05, 0.13),
                                 (-0.22, 0.10, SZ / 2 - 0.02, (-0.35, 0.1, 0.93), 0.07, 0.19),
                                 (SX / 2 + 0.01, 0.05, 0.08, (0.95, 0.1, 0.3), 0.06, 0.20),
                                 (-SX / 2 - 0.01, -0.06, 0.16, (-0.9, -0.1, 0.45), 0.055, 0.17)):
        B.add_crystal(BODY, W, "team_glow", kit.frame(C + Vector((x, y, z)), ax, (0, 1, 0)), r, h, seg=6,
                      waist=0.8, cap=0.36)


# ===================================================================================================== pick-hammer
def held_matrix(poses, slot, origin, z_axis, y_hint):
    rest = B.arm.data.bones[slot].matrix_local
    return rest @ poses[slot].inverted() @ kit.frame(origin, z_axis, y_hint)


GRIP = 0.62      # v2: grip height on the item (bible 08-front: fist a little below the middle of the pick)


def shaft_parts(p):
    """v2 shaft along +Z: brass pommel block at 0, thick banded shaft to 1.30, head socket above. Item length pommel to
    the top of the head 1.50 u = 0.75 of body height (the p08 drawings, front and front-pick, show the pick from the
    helmet brim to the boots; the 0.633 number in PARITY.md undersells what the drawing shows)."""
    p(B.cyl((0, 0, 0.06), (0, 0, 1.30), 0.056, 0.052, 10), "wood", "pick_hammer", True)
    for z0, z1 in ((0.20, 0.36), (0.80, 0.94)):
        p(B.cyl((0, 0, z0), (0, 0, z1), 0.064, 0.064, 10), "navy_dark", "pick_hammer", True)
    p(B.cyl((0, 0, 0.44), (0, 0, 0.56), 0.066, 0.066, 10), "kerchief", "team_cloth", True)   # team trim wrap
    for z in (0.17, 0.39, 0.61, 0.77, 0.97, 1.16):
        p(B.cyl((0, 0, z - 0.024), (0, 0, z + 0.024), 0.068, 0.068, 8), "brass", "pick_hammer", False)
    p(box((0.15, 0.15, 0.12), 0.03, 2), "brass", "pick_hammer", False, M((0, 0, 0.05)))
    p(box((0.10, 0.10, 0.04), 0.02, 1), "brass_dark", "pick_hammer", False, M((0, 0, -0.025)))


def arc_arm(p, pts, color, seg=8):
    """Curved tapering arm through [(x, z, r), ...] in the item XZ plane (r = half thickness)."""
    for (x0, z0, r0), (x1, z1, r1) in zip(pts[:-1], pts[1:]):
        p(B.cyl((x0, 0, z0), (x1, 0, z1), r0, r1, seg), color, "pick_hammer", False)


def build_pick(poses, obj, state):
    W = "handslot.r"
    parts = []

    def p(bm, color, feat, smooth=False, m=None):
        parts.append((bm, color, feat, smooth, m))

    shaft_parts(p)
    crystals = []
    if state == "pick":
        # head socket: brass collar, stone block, brass cap (heavy, about 0.26 x 0.22 x 0.30)
        p(box((0.27, 0.22, 0.30), 0.035, 2), "brass", "pick_hammer", False, M((0, 0, 1.36)))
        p(box((0.20, 0.235, 0.16), 0.03, 2), "stone", "pick_hammer", False, M((0, 0, 1.39)))
        p(box((0.14, 0.24, 0.06), 0.02, 1), "brass_dark", "pick_hammer", False, M((0, 0, 1.25)))
        # stone point (+X), a wide crescent arm drooping to a point (bible: the hooked grey arm on the right)
        arc_arm(p, [(0.10, 1.40, 0.10), (0.24, 1.38, 0.085), (0.36, 1.31, 0.065), (0.45, 1.20, 0.042),
                    (0.50, 1.07, 0.012)], "stone_light")
        # crystal arm (-X): a broad stone blade carrying a long crystal edge, drooping down and out
        arc_arm(p, [(-0.10, 1.40, 0.10), (-0.24, 1.37, 0.09), (-0.36, 1.29, 0.075), (-0.44, 1.18, 0.05)], "stone")
        crystals.append((kit.frame((-0.14, 0, 1.38), (-0.62, 0, -1.0), (0, 0, 1)), 0.12, 0.58))
        crystals.append((kit.frame((-0.22, 0, 1.43), (-1.0, 0, -0.30), (0, 0, 1)), 0.08, 0.28))
    else:
        # hammer: a massive stone block head with brass bands, crystals set into both faces and the top
        p(box((0.56, 0.32, 0.36), 0.045, 2), "stone", "pick_hammer", False, M((0, 0, 1.40)))
        for x in (-0.17, 0.17):
            p(box((0.06, 0.34, 0.38), 0.02, 1), "brass", "pick_hammer", False, M((x, 0, 1.40)))
        for x in (-0.27, 0.27):
            p(box((0.05, 0.30, 0.30), 0.02, 1), "stone_light", "pick_hammer", False, M((x * 1.03, 0, 1.40)))
        for y in (-1, 1):
            crystals.append((M((0, y * 0.15, 1.40), (-90 * y, 0, 30)), 0.11, 0.08))
        crystals.append((kit.frame((0, 0, 1.57), (0, 0, 1), (0, 1, 0)), 0.07, 0.12))
        p(box((0.16, 0.16, 0.06), 0.02, 1), "brass_dark", "pick_hammer", False, M((0, 0, 1.20)))

    origin = poses[W].translation.copy()
    up = Vector((-0.10, -0.06, 1.0)).normalized()
    T = held_matrix(poses, W, origin, up, (0, 1, 0)) @ M((0, 0, -GRIP))
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
    folded_crate(BAR["folded"], damaged=False)
    # damaged (bible p08 barricade-damaged): the FOLDED crate, battered: knocked over onto its long side and tilted,
    # one face board torn out, one brace snapped, a brass corner off, a stone panel cracked, the crystal chipped,
    # splinters and a stone chip on the ground. Authored geometry, no procedural destruction.
    folded_crate(BAR["damaged"], damaged=True)


def folded_crate(o, damaged):
    F = "barricade"
    SX, SY, SZ = 0.62, 0.44, 0.80
    if damaged:
        # lying on its long side (crate z -> world x), rocked 9 degrees onto one edge, resting on the ground
        base = M((0.0, 0.0, SX / 2 + 0.035), (0, 0, 0)) @ M((0, 0, 0), (0, 81, 0)) @ M((0, 0, -SZ / 2))
    else:
        base = M((0, 0, 0))

    def put(bm, col, m, smooth=False, grad=True):
        add(o, bm, col, None, F, smooth=smooth, m=base @ m, grad=grad)

    def gem(m4, r, h, **kw):
        B.add_crystal(o, None, "team_glow", base @ m4, r, h, seg=6, waist=0.8, **kw)

    put(B.box((SX - 0.14, SY - 0.05, SZ - 0.04), 0.0), "wood_dark", M((0, 0, SZ / 2)))
    for x in (-1, 1):
        if damaged and x > 0:      # cracked stone side panel: two pieces, the upper one slipped and turned
            put(box((0.13, SY, SZ * 0.55), 0.04, 2), "stone_light", M((x * (SX / 2 - 0.065), 0, SZ * 0.29)))
            put(box((0.13, SY * 0.9, SZ * 0.36), 0.04, 2), "stone", M((x * (SX / 2 - 0.05), -0.03, SZ * 0.79), (6, 0, 9)))
        else:
            put(box((0.13, SY, SZ - 0.06), 0.04, 2), "stone_light", M((x * (SX / 2 - 0.065), 0, SZ / 2)))
        if damaged and x > 0:      # chipped crystal, smaller and knocked askew
            gem(M((x * (SX / 2 + 0.0), 0.02, SZ * 0.45), (0, 90 * x, 25)), 0.065, 0.06, cap=0.6)
        else:
            gem(M((x * (SX / 2 + 0.0), 0, SZ / 2), (0, 90 * x, 0)), 0.08, 0.08, cap=0.55)
        for y in (-1, 1):
            for z in (-1, 1):
                if damaged and (x, y, z) == (1, -1, 1):
                    continue       # this brass corner is off (lies on the ground below)
                put(box((0.12, 0.12, 0.12), 0.03, 1), "brass",
                    M((x * (SX / 2 - 0.05), y * (SY / 2 - 0.05), SZ / 2 + z * (SZ / 2 - 0.05))))
    for y in (-1, 1):
        for k, xx in enumerate((-0.12, 0.0, 0.12)):
            if damaged and y < 0 and k == 1:
                continue           # the middle front board is torn out: the dark backing shows through
            tilt = (0, 4 if damaged and k == 2 and y < 0 else 0, 0)
            put(box((0.115, 0.03, SZ - 0.16), 0.02, 1), "wood", M((xx, y * (SY / 2 - 0.005), SZ / 2), tilt))
        for sg in (1, -1):
            if damaged and y < 0 and sg < 0:
                # snapped brace: two short pieces at broken angles with a jagged end
                put(box((0.075, 0.035, 0.36), 0.02, 1), "wood", M((-0.07, y * (SY / 2 + 0.014), SZ * 0.30), (0, -22, 0)))
                put(box((0.075, 0.035, 0.30), 0.02, 1), "wood", M((0.10, y * (SY / 2 + 0.03), SZ * 0.72), (0, -40, 0)))
                jag = [(0.0, -0.04), (0.05, -0.04), (0.03, 0.0), (0.06, 0.04), (0.0, 0.04)]
                put(B.prism(jag, 0.035, 0.0), "wood_dark", M((0.0, y * (SY / 2 + 0.02), SZ * 0.5), (90, 0, 0)),
                    grad=False)
            else:
                put(box((0.075, 0.035, 0.72), 0.02, 1), "wood", M((0, y * (SY / 2 + 0.014), SZ / 2), (0, 22 * sg, 0)))
    if damaged:   # lid knocked loose, sitting crooked
        put(box((SX - 0.02, SY + 0.02, 0.05), 0.02, 2), "wood", M((0.03, 0.01, SZ + 0.03), (0, 7, 4)))
    else:
        put(box((SX - 0.02, SY + 0.02, 0.05), 0.02, 2), "wood", M((0, 0, SZ + 0.02)))
        gem(kit.frame((0.14, 0.05, SZ + 0.04), (0.3, 0.1, 0.95), (0, 1, 0)), 0.06, 0.18, cap=0.4)
        put(B.torus(0.06, 0.02, 10, 4), "brass_dark", M((-0.10, 0, SZ + 0.06), (90, 0, 0)), smooth=True)
    if damaged:   # ground debris in world space: the lost brass corner, splinters, a stone chip, a crystal shard
        add(o, box((0.12, 0.12, 0.12), 0.03, 1), "brass", None, F, smooth=False, m=M((0.52, -0.36, 0.06), (10, 0, 35)))
        for (x, y, rz, L) in ((0.15, -0.42, 25, 0.30), (-0.30, -0.40, -40, 0.22), (0.45, 0.30, 70, 0.18)):
            add(o, box((L, 0.06, 0.045), 0.02, 1), "wood", None, F, smooth=False, m=M((x, y, 0.025), (0, 0, rz)))
        add(o, box((0.11, 0.10, 0.08), 0.025, 1), "stone_light", None, F, smooth=False,
            m=M((-0.52, -0.25, 0.04), (12, 8, 30)))
        B.add_crystal(o, None, "team_glow", kit.frame((0.30, -0.30, 0.0), (0.4, -0.2, 0.3), (0, 0, 1)), 0.04, 0.09,
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
    face_img = face_decal.save_png_blender(OUT_FACE)
    n_face = assign_face_decal(objs[BODY], face_img)
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

    report = {"objects": {}, "bones": len(arm.data.bones), "display_pose": [DISPLAY_ACTION, DISPLAY_FRAME],
              "face_decal": {"png": os.path.basename(OUT_FACE), "size": face_decal.N, "faces": n_face}}
    for name, ob in list(objs.items()) + list(bobjs.items()):
        report["objects"][name] = {
            "triangles": tris(ob), "vertices": len(ob.data.vertices), "rest_bounds": dims(ob),
            "materials": [m.name for m in ob.data.materials],
            "feature_groups": sorted(g.name for g in ob.vertex_groups if g.name.startswith("feat_")),
        }
    report["bevels"] = BEVELS
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
