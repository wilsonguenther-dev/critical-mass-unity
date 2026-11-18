# Critical Mass Kaiju (adult) rebuilt from scripted rigid parts on the EXISTING 46-bone KaijuRig (rig-kaiju-2).
# One base body + one swap-in part set per type (ELDER GROVE BRUTE TIMBER MIRE). Every primitive is bound 100% to ONE bone.
# env: CM_TYPE = Elder|Grove|Brute|Timber|Mire   CM_LOD = Hero|LOD0|LOD1
# blender -b --factory-startup --python build_kaiju.py      -> work/<Type>/<LOD>/Kaiju.blend + build_report.json
import bpy, bmesh, math, os, sys, json, random
from mathutils import Vector, Matrix
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kkit as K
from kkit import V, ellipsoid, rbox, tube, capsule, ring_loft, box, frame_from, basis, path_tube, leaf, cylinder, slab
import klod as LH

TYPE = os.environ.get("CM_TYPE", "Elder"); LOD = os.environ.get("CM_LOD", "Hero")
ROOT = os.path.expanduser("~/cm-art-trial/parts-kaiju")
WORK = f"{ROOT}/work/{TYPE}/{LOD}"; os.makedirs(WORK, exist_ok=True)
ORIG = ROOT + "/work/Kaiju_Adult_ORIG.fbx"

# ------------------------------------------------------------------ palettes (sRGB). Same names in every type.
# team / tglow are authored in the Kaiju's default swatch (GlowNeutral #B8E833, ArtMapping.KaijuDefault): the shader swaps
# them to the allegiance (or, neutral, the type's own colour) keeping the authored value ratio.
NAMES = ["hide", "hide_dk", "plate", "plate_lt", "plate_dk", "belly", "belly_dk", "claw", "socket", "eye", "teeth", "mouth",
         "nostril", "trim", "accent", "accent_dk", "glow", "team", "team_dk", "tglow"]
PAL = {
    # ELDER: ancient warm stone, gold trim, gold diamond inlays
    "Elder":  dict(hide="#6E6A63", hide_dk="#55524D", plate="#9C978A", plate_lt="#B7B1A2", plate_dk="#7A766C", belly="#CDB894",
                   belly_dk="#A8916C", claw="#E9E1CB", accent="#C99A3A", accent_dk="#8E6A2A", trim="#D8AA44", glow="#FFD24A"),
    # GROVE: bark plates, moss, leaves
    "Grove":  dict(hide="#7E6C54", hide_dk="#5F5040", plate="#94704A", plate_lt="#AE8A60", plate_dk="#6E5238", belly="#C8B98E",
                   belly_dk="#A2936A", claw="#E4D9B8", accent="#86C24E", accent_dk="#5E9A38", trim="#A8D862", glow="#86F25A"),
    # BRUTE: dark basalt armour, bone horns, amber vents
    "Brute":  dict(hide="#5C4B47", hide_dk="#463835", plate="#866F67", plate_lt="#9D857A", plate_dk="#685650", belly="#C4A88E",
                   belly_dk="#9E836C", claw="#EDE3CC", accent="#DCCFB3", accent_dk="#B3A68B", trim="#4A3B37", glow="#FF9A2E"),
    # TIMBER: pale banded bark, logs, antlers
    "Timber": dict(hide="#8C7B5C", hide_dk="#6E5F45", plate="#CDBA92", plate_lt="#E3D3AE", plate_dk="#9E8862", belly="#D9C9A4",
                   belly_dk="#B5A47E", claw="#EFE6CF", accent="#E8DDBE", accent_dk="#7A6446", trim="#A88D63", glow="#F3DDA8"),
    # MIRE: smooth wet hide, blue-violet, fins
    "Mire":   dict(hide="#6E7EA4", hide_dk="#56638A", plate="#7F8DB4", plate_lt="#97A4C8", plate_dk="#5E6A92", belly="#AEB9D6",
                   belly_dk="#8E99BA", claw="#E2E4EE", accent="#8F7FC6", accent_dk="#6B5DA2", trim="#A69AD8", glow="#9AB0FF"),
}[TYPE]
COMMON = dict(socket="#1C1A22", eye="#B8E833", teeth="#F2EEDF", mouth="#5A2630", nostril="#2A2228",
              team="#8AAE26", team_dk="#6A861D", tglow="#B8E833")
PALETTE = [(n, (PAL.get(n) or COMMON[n]).lstrip("#")) for n in NAMES]
TEAM = {"team": ("8AAE26", "8AAE26"), "team_dk": ("6A861D", "6A861D"), "tglow": ("B8E833", "B8E833"), "eye": ("B8E833", "B8E833")}
P = K.Palette(PALETTE, TEAM, team_glow=("tglow", "eye"), fixed_glow=("glow",),
              bright=tuple(n for n in NAMES if n not in TEAM))

# ------------------------------------------------------------------ rig: the game file, meshes removed, rest pose untouched
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=ORIG, use_anim=False, use_custom_normals=False, ignore_leaf_bones=False,
                         automatic_bone_orientation=False, primary_bone_axis="Y", secondary_bone_axis="X")
rig = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
for o in list(bpy.data.objects):
    if o.type != 'ARMATURE': bpy.data.objects.remove(o, do_unlink=True)
for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.actions):
    for x in list(coll): coll.remove(x)
for pb in rig.pose.bones: pb.matrix_basis = Matrix.Identity(4)
kit = K.Kit(rig, P)
OBJ = "Kaiju"
def E(bone, bm, col): kit.emit(OBJ, bone, bm, col)
def bh(n): return kit.bh(n)
def bt(n): return kit.bt(n)
RNG = random.Random(hash(TYPE) & 0xffff)
SIDES = (("L", 1), ("R", -1))
def mx(p, s): return V(p.x * s, p.y, p.z)

def rot_to(n, up_hint=V(0, -1, 0)):
    """3x3 basis whose local Z is n (unit), X roughly along up_hint projected."""
    z = n.normalized(); x = up_hint - z * up_hint.dot(z)
    if x.length < 1e-4: x = V(1, 0, 0) - z * z.x
    x.normalize(); y = z.cross(x)
    return basis(x, y, z)

def hullrock(c, n, rx, ry, th, seed, top=0.6, sides=9, jit=0.18, up_hint=V(0, -1, 0), lean=0.0):
    """Faceted boulder plate: convex hull of jittered points, flat bottom ring at -th/2, smaller top ring at +th/2."""
    R = random.Random(seed); M = rot_to(n, up_hint); bm = bmesh.new()
    k = max(5, LH.seg(sides) if LOD != "Hero" else sides)
    for ring, (zz, sc) in enumerate(((-th * 0.5, 1.0), (th * 0.15, 0.92), (th * 0.5, top))):
        ph = R.random() * 6.283
        for i in range(k):
            a = ph + 6.283 * i / k; j = 1 - jit + 2 * jit * R.random()
            p = V(math.cos(a) * rx * sc * j, math.sin(a) * ry * sc * j + lean * (zz / th), zz)
            bm.verts.new(c + M @ p)
    bmesh.ops.convex_hull(bm, input=list(bm.verts))
    return bm

def wedge(c, n, w, d, h, seed, tip=0.25, up_hint=V(0, -1, 0), lean=V(0, 0, 0)):
    """Tall angular wedge / horned plate (BRUTE): base w x d, apex ridge narrowed by tip, leaning by lean (local)."""
    R = random.Random(seed); M = rot_to(n, up_hint); bm = bmesh.new()
    for (x, y) in ((-1, -1), (1, -1), (1, 1), (-1, 1), (0, -1.15), (0, 1.15)):
        bm.verts.new(c + M @ V(x * w * 0.5 * (0.9 + 0.2 * R.random()), y * d * 0.5, -h * 0.15))
    for (x, y) in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
        bm.verts.new(c + M @ (V(x * w * 0.5 * tip, y * d * 0.5 * 0.55, h * 0.85) + lean))
    bm.verts.new(c + M @ (V(0, 0, h) + lean * 1.15))
    bmesh.ops.convex_hull(bm, input=list(bm.verts))
    return bm

def octa(c, n, s, up_hint=V(0, -1, 0), flat=0.35):
    """Diamond motif (flattened octahedron) lying on a surface with normal n."""
    M = rot_to(n, up_hint); bm = bmesh.new()
    pts = [V(s, 0, 0), V(-s, 0, 0), V(0, s * 1.35, 0), V(0, -s * 1.35, 0), V(0, 0, s * flat), V(0, 0, -s * flat)]
    for p in pts: bm.verts.new(c + M @ p)
    bmesh.ops.convex_hull(bm, input=list(bm.verts))
    return bm

def cone(p0, p1, r, n=6, hint=V(1, 0, 0)):
    return tube(p0, p1, [(0, r, r), (0.5, r * 0.62, r * 0.62), (1, r * 0.06, r * 0.06)], n, 2.0, hint, False, False)

def horn(p0, d, L, r, bend, n=8):
    """Curved tapering horn: base at p0 along d, bend vector added quadratically."""
    pts, rad = [], []
    for t, rr in ((-.15, r * 1.05), (0, r), (.3, r * .8), (.6, r * .55), (.85, r * .3), (1.0, r * .08)):
        pts.append(p0 + d.normalized() * (L * t) + bend * (max(t, 0) ** 2)); rad.append(rr)
    return K.round_path(pts, rad, n)

def on_ell(c, r, d):
    """Point and outward normal on ellipsoid (c, r) in direction d."""
    d = d.normalized(); p = c + V(d.x * r.x, d.y * r.y, d.z * r.z)
    nn = V(d.x / r.x, d.y / r.y, d.z / r.z).normalized()
    return p, nn

def dirz(az_deg, el_deg):
    """Direction from azimuth (0 = up the back toward +Y? no: 0 = character's left +X, 90 = back +Y) and elevation."""
    a, e = math.radians(az_deg), math.radians(el_deg)
    return V(math.cos(a) * math.cos(e), math.sin(a) * math.cos(e), math.sin(e))

# ------------------------------------------------------------------ BODY (shared, coloured per type)
SMOOTH = TYPE == "Mire"
seg_body = 32 if TYPE == "Brute" else 22
# round 2: the types differ in MASS, not only crests (gameplay-camera silhouette IoU). w = torso width, hz = top-line height
# (hump), arm / fist / sh = forearm, fist and shoulder bulk, tr = tail thickness, tn = last tail bone that carries geometry
# (the bones still animate; BRUTE's short thick tail simply ends at Tail4).
BX = {"Elder": dict(w=1.06, hz=1.0, arm=0.95, fist=0.95, sh=1.0, tr=1.25, tn=7),
      "Grove": dict(w=0.84, hz=0.85, arm=0.86, fist=0.88, sh=0.86, tr=0.72, tn=7),
      "Brute": dict(w=1.32, hz=1.0, arm=1.60, fist=1.40, sh=1.60, tr=1.60, tn=4),
      "Timber": dict(w=0.86, hz=0.50, arm=0.86, fist=0.90, sh=0.85, tr=1.0, tn=7),
      "Mire": dict(w=1.26, hz=0.9, arm=1.0, fist=0.95, sh=1.10, tr=1.05, tn=7)}[TYPE]
ARMN = 20 if TYPE == "Brute" else 14
MASS = {   # bone: (centre, radii, rot)   torso boulders overlapping ~40% so the spine can bend without gaps
    "Hips":   (V(0, 0.86, 1.62), V(0.98, 0.86, 0.74)),
    "Spine":  (V(0, 0.42, 2.14), V(1.12, 0.92, 0.82)),
    "Spine2": (V(0, 0.04, 2.55), V(1.22, 0.95, 0.88)),
    "Spine3": (V(0, -0.30, 2.94), V(1.28, 0.90, 0.86)),
    "Chest":  (V(0, -0.64, 2.80), V(1.18, 0.74, 0.80)),
}
MASS = {b: (c, V(r.x * BX["w"], r.y, r.z)) for b, (c, r) in MASS.items()}
for b, (c, r) in MASS.items():
    E(b, ellipsoid(c, r, seg_body, 12), "hide")
# back hump that lifts the top line (the crest grows out of it); TIMBER's is low (a long low log-stacked back)
HUMP = (("Spine3", V(0, -0.18, 3.28), V(0.98, 0.82, 0.56)), ("Spine2", V(0, 0.30, 3.02), V(0.92, 0.72, 0.50)))
HUMP = tuple((b, V(c.x, c.y, 2.75 + (c.z - 2.75) * BX["hz"]), V(r.x * BX["w"], r.y, r.z * (0.6 + 0.4 * BX["hz"]))) for b, c, r in HUMP)
for b, c, r in HUMP: E(b, ellipsoid(c, r, 18, 9), "hide")
# belly: tan rounded plates down the front (chest / spine2 / spine / hips), banded
for b, c, s in (("Chest", V(0, -1.18, 2.55), (1.30, 0.42, 0.85)), ("Spine2", V(0, -0.72, 2.12), (1.34, 0.46, 0.72)),
                ("Spine", V(0, -0.32, 1.78), (1.18, 0.44, 0.62)), ("Hips", V(0, 0.12, 1.42), (1.00, 0.42, 0.46))):
    tilt = Matrix.Rotation(math.radians(-38), 3, 'X')
    E(b, rbox(c, s, tilt, 2.4, 14, 5), "belly")
    if not SMOOTH:
        E(b, rbox(c + V(0, -0.16, -s[2] * 0.36), (s[0] * 0.84, 0.16, 0.06), tilt, 2.2, 10, 3), "belly_dk")
# neck
E("Neck", ellipsoid(V(0, -1.0, 2.98), V(0.64, 0.52, 0.54), 16, 9), "hide")
# ---------------- HEAD (bone Head; jaw on Jaw)
E("Head", rbox(V(0, -1.46, 3.10), (1.16, 0.98, 0.78), Matrix.Rotation(math.radians(6), 3, 'X'), 2.6, 18, 7), "hide")   # skull
E("Head", rbox(V(0, -1.90, 2.93), (0.88, 0.62, 0.44), Matrix.Rotation(math.radians(-4), 3, 'X'), 2.4, 16, 6), "belly")  # muzzle
E("Head", rbox(V(0, -1.98, 3.08), (0.70, 0.40, 0.18), Matrix.Rotation(math.radians(-14), 3, 'X'), 2.4, 12, 4), "hide")   # snout top
for s in (1, -1):
    E("Head", ellipsoid(V(0.16 * s, -2.19, 3.02), V(0.07, 0.05, 0.05), 8, 5), "nostril")
    # eyes: dark socket + glowing team eye, under the brow, on the top-front so the 55 deg camera sees them
    E("Head", ellipsoid(V(0.34 * s, -1.84, 3.22), V(0.20, 0.10, 0.15), 12, 7), "socket")
    E("Head", ellipsoid(V(0.35 * s, -1.90, 3.22), V(0.15, 0.08, 0.115), 12, 7), "eye")
    # cheek boulders
    E("Head", ellipsoid(V(0.55 * s, -1.62, 2.88), V(0.24, 0.34, 0.26), 10, 6), "hide_dk")
# brow ridge (heavy, angry-goofy): two plates meeting at the centre
for s in (1, -1):
    E("Head", rbox(V(0.28 * s, -1.84, 3.40), (0.56, 0.30, 0.17), Matrix.Rotation(math.radians(-16 * s), 3, 'Y') @ Matrix.Rotation(math.radians(-20), 3, 'X'), 2.4, 12, 4), "plate_dk")
# upper teeth (tusk pair + small)
for s in (1, -1):
    E("Head", cone(V(0.30 * s, -2.06, 2.80), V(0.31 * s, -2.10, 2.56), 0.065, 6), "teeth")
    E("Head", cone(V(0.14 * s, -2.14, 2.78), V(0.14 * s, -2.16, 2.65), 0.04, 5), "teeth")
# jaw
E("Jaw", rbox(V(0, -1.74, 2.56), (0.84, 0.86, 0.30), Matrix.Rotation(math.radians(8), 3, 'X'), 2.4, 14, 5), "belly_dk")
E("Jaw", rbox(V(0, -1.70, 2.70), (0.66, 0.66, 0.05), Matrix.Identity(3), 2.2, 10, 3), "mouth")
for s in (1, -1):
    E("Jaw", cone(V(0.25 * s, -2.02, 2.68), V(0.26 * s, -2.06, 2.86), 0.05, 5), "teeth")
E("Jaw", ellipsoid(V(0, -2.08, 2.52), V(0.30, 0.16, 0.16), 10, 6), "belly_dk")   # chin

# ---------------- ARMS
for S, s in SIDES:
    sh, ar, fa, fi = "Shoulder_" + S, "Arm_" + S, "Forearm_" + S, "Fist_" + S
    q = BX["sh"]; A = BX["arm"]; F = BX["fist"]
    E(sh, ellipsoid(mx(V(1.10 + 0.55 * (q - 1), -0.62, 2.92 + 0.25 * (q - 1)), s), V(0.64 * q, 0.64 * (1 + 0.5 * (q - 1)), 0.60 * q), 16, 9), "hide")
    a0, a1 = bh(ar), bt(ar)
    E(ar, tube(a0, a1, [(-0.12, 0.47 * A, 0.47 * A), (0.0, 0.48 * A, 0.48 * A), (0.5, 0.45 * A, 0.45 * A), (1.0, 0.40 * A, 0.40 * A), (1.08, 0.36 * A, 0.36 * A)], ARMN), "hide")
    f0, f1 = bh(fa), bt(fa)
    E(fa, ellipsoid(f0, V(0.43, 0.43, 0.43) * A, 12, 7), "hide")   # elbow collar (child side)
    E(fa, tube(f0, f1, [(-0.05, 0.40 * A, 0.40 * A), (0.3, 0.48 * A, 0.46 * A), (0.75, 0.58 * A, 0.55 * A), (1.02, 0.56 * A, 0.54 * A), (1.12, 0.46 * A, 0.44 * A)], ARMN), "hide")
    # fist: big block along the bone, knuckle fingers on their own bones
    k0, k1 = bh(fi), bt(fi); fd = (k1 - k0).normalized()
    E(fi, rbox((k0 + k1) / 2 + V(0, -0.06, 0), (0.92 * F, 0.80 * F, 0.78 * F), rot_to(fd, V(0, -1, 0)), 2.6, 14, 6), "hide")
    for i in (1, 2, 3):
        kn = f"Knuckle{i}_{S}"; q0, q1 = bh(kn), bt(kn); d = (q1 - q0).normalized()
        a = q0 + d * 0.22 * (q1 - q0).length
        E(kn, capsule(a, q1 - d * 0.04, 0.20 if i == 2 else 0.18, 10, V(0, 0, 1)), "hide_dk")
        E(kn, cone(q1 - d * 0.02 + V(0, 0, 0.02), q1 + d * 0.22 + V(0, 0, -0.10), 0.10, 6), "claw")

# ---------------- LEGS
for S, s in SIDES:
    th, shn, ft, to = "Thigh_" + S, "Shin_" + S, "Foot_" + S, "Toes_" + S
    t0, t1 = bh(th), bt(th)
    E(th, ellipsoid((t0 + t1) / 2 + mx(V(0.08, 0.02, 0.02), s), V(0.58, 0.64, 0.66), 16, 9, rot_to((t1 - t0).normalized(), V(0, -1, 0))), "hide")
    s0, s1 = bh(shn), bt(shn)
    E(shn, ellipsoid(s0, V(0.40, 0.40, 0.40), 12, 7), "hide")   # knee collar
    E(shn, tube(s0, s1, [(-0.05, 0.38, 0.40), (0.5, 0.36, 0.38), (1.0, 0.33, 0.34), (1.12, 0.30, 0.30)], 12), "hide")
    E(ft, rbox(mx(V(0.86, 0.18, 0.22), s), (0.66, 0.86, 0.44), Matrix.Identity(3), 2.6, 12, 5), "hide")
    E(ft, ellipsoid(bh(ft), V(0.33, 0.33, 0.33), 10, 6), "hide")   # ankle collar
    for k in (-1, 0, 1):
        p0 = mx(V(0.86 + 0.20 * k, -0.08, 0.14), s); p1 = mx(V(0.86 + 0.24 * k, -0.42, 0.12), s)
        E(to, capsule(p0, p1, 0.13, 8, V(0, 0, 1)), "hide_dk")
        E(to, cone(p1 + V(0, 0.02, 0.0), p1 + V(0, -0.20, -0.08), 0.08, 5), "claw")

# ---------------- TAIL
TR = [r * BX["tr"] for r in (0.58, 0.50, 0.42, 0.34, 0.27, 0.20, 0.14, 0.06)]
TN = BX["tn"]
if TN < 7: TR[TN] = TR[TN - 1] * 0.82                       # a short tail ends blunt
for i in range(1, TN + 1):
    n = f"Tail{i}"; p0, p1 = bh(n), bt(n); L = (p1 - p0).length
    E(n, ellipsoid(p0, V(TR[i - 1] * 1.03, TR[i - 1] * 1.03, TR[i - 1] * 1.03), 12, 7), "hide")
    E(n, tube(p0, p1, [(0, TR[i - 1], TR[i - 1] * 0.95), (1.0, TR[i], TR[i] * 0.95), (1.1, TR[i] * 0.92, TR[i] * 0.88)], 12,
              hint=V(0, 0, 1), round1=(i == TN)), "hide")

# ------------------------------------------------------------------ plate anchors (shared placement, type style)
ANCH = []   # (bone, point, normal, size)
for b, (c, r) in MASS.items():
    for az, el, sz in ((0, 58, .55), (180, 58, .55), (90, 62, .55), (40, 30, .5), (140, 30, .5)):
        if b == "Chest" and az == 90: continue
        if b == "Hips" and el < 40: continue
        p, nn = on_ell(c, r, dirz(az, el)); ANCH.append((b, p, nn, sz * (1.15 if b in ("Spine2", "Spine3") else 1.0)))
for b, c, r in HUMP:
    for az in (25, 155):
        p, nn = on_ell(c, r, dirz(az, 55)); ANCH.append((b, p, nn, 0.55))
LIMB_ANCH = []   # forearm / thigh / shin outer plates
for S, s in SIDES:
    f0, f1 = bh("Forearm_" + S), bt("Forearm_" + S)
    for t, sz in ((0.35, .5), (0.72, .58)):
        c = f0.lerp(f1, t); LIMB_ANCH.append(("Forearm_" + S, c + V(s * 0.46 * BX["arm"], 0.02, 0.10 * BX["arm"]), V(s, 0.0, 0.45).normalized(), sz * BX["arm"] ** 0.5))
    t0, t1 = bh("Thigh_" + S), bt("Thigh_" + S)
    LIMB_ANCH.append(("Thigh_" + S, t0.lerp(t1, 0.45) + V(s * 0.58, 0.05, 0.12), V(s, 0.15, 0.55).normalized(), 0.56))
    a0, a1 = bh("Arm_" + S), bt("Arm_" + S)
    LIMB_ANCH.append(("Arm_" + S, a0.lerp(a1, 0.5) + V(s * 0.38 * BX["arm"], 0.0, 0.18 * BX["arm"]), V(s, 0, 0.7).normalized(), 0.44))

def tail_top(i, t=0.5):
    n = f"Tail{i}"; p0, p1 = bh(n), bt(n); c = p0.lerp(p1, t); r = TR[i - 1] + (TR[i] - TR[i - 1]) * t
    return n, c + V(0, 0, r * 0.88), (p1 - p0).normalized(), r

CREST = [f"Crystal{i}" for i in range(2, 9)]
def top_z(y):
    z = 0.0
    for c, r in [v for v in MASS.values()] + [(c, r) for _, c, r in HUMP] + [(V(0, -1.0, 2.98), V(0.64, 0.52, 0.54))]:
        u = (y - c.y) / r.y
        if abs(u) < 1: z = max(z, c.z + r.z * math.sqrt(1 - u * u))
    return z
def crest_base(cb):
    """Crest element base on the back surface under the crystal bone, and its up direction (bone axis)."""
    h, t = bh(cb), bt(cb); d = (t - h).normalized()
    return h - d * 0.42, d, (t - h).length

# ------------------------------------------------------------------ TYPE PART SETS
def shoulder_plate(S, s, style, k=1.0):
    q = BX["sh"]; b = "Shoulder_" + S; c = mx(V(1.18 + 0.55 * (q - 1), -0.62, 3.36 + 0.45 * (q - 1)), s); n = mx(V(0.45, 0, 1), s).normalized()
    if style == "rock":
        c = c + mx(V(0.30, 0, 0.02), s); n = mx(V(0.55, 0, 1), s).normalized()
        E(b, hullrock(c + n * 0.10, n, 0.62, 0.55, 0.26, 11 + s, top=0.7), "team")
        E(b, hullrock(c - n * 0.02, n, 0.95, 0.80, 0.20, 21 + s, top=0.92, jit=0.08), "team_dk")
        E(b, hullrock(c - n * 0.12, n, 1.05, 0.88, 0.10, 31 + s, top=0.97, jit=0.05), "trim")
    elif style == "wedge":
        E(b, wedge(c, n, 0.95 * k, 0.80 * k, 0.42 * k, 7 + s, tip=0.5), "team")
    elif style == "bark":
        E(b, rbox(c, (0.95 * k, 0.85 * k, 0.24), rot_to(n), 2.2, 12, 4), "team")
    elif style == "log":
        for j in (-1, 0, 1):
            q = c + V(0, j * 0.27 * k, -0.04 * abs(j))
            E(b, cylinder(q + V(-s * 0.40 * k, 0, 0.0), q + V(s * 0.40 * k, 0, -0.18), 0.15 * k, 10), "team")
    elif style == "smooth":
        E(b, ellipsoid(c - n * 0.04, V(0.60 * k, 0.55 * k, 0.20), 14, 7, rot_to(n)), "team")

def elder():
    # ancient plates with gold trim rims: a gold under-slab peeking out around each stone plate
    for i, (b, p, nn, sz) in enumerate(ANCH):
        if nn.z > 0.6 and b != "Chest": continue          # under the back shield
        E(b, hullrock(p + nn * 0.02, nn, sz, sz * 0.86, 0.26, 100 + i, top=0.62), "plate" if i % 3 else "plate_lt")
        E(b, hullrock(p - nn * 0.05, nn, sz * 1.10, sz * 0.96, 0.12, 300 + i, top=0.95, jit=0.08), "trim")
    for i, (b, p, nn, sz) in enumerate(LIMB_ANCH):
        E(b, hullrock(p, nn, sz, sz * 0.8, 0.24, 500 + i), "plate")
        E(b, hullrock(p - nn * 0.05, nn, sz * 1.1, sz * 0.9, 0.10, 520 + i, top=0.95, jit=0.08), "trim")
        if "Forearm" in b: E(b, octa(p + nn * 0.13, nn, 0.11), "glow")
    for S, s in SIDES: shoulder_plate(S, s, "rock")
    # round 2 MASS: a broad stone back SHIELD (a carapace wider than the body, gold rim) under the plate row; a team-painted
    # inlay on both flanks of each section so ownership reads on the broad top
    for j, (b, y, tilt, rx, ry) in enumerate((("Spine3", -0.30, -0.15, 2.45, 0.76), ("Spine2", 0.25, 0.10, 2.65, 0.76),
                                             ("Spine", 0.80, 0.45, 2.40, 0.68), ("Hips", 1.28, 0.85, 1.90, 0.60))):
        n = V(0, tilt, 1).normalized(); c = V(0, y, top_z(y) - 0.02)
        E(b, hullrock(c, n, rx, ry, 0.34, 1200 + j, top=0.80, sides=12, jit=0.05, up_hint=V(1, 0, 0)), "plate_lt" if j % 2 else "plate")
        E(b, hullrock(c - n * 0.10, n, rx * 1.06, ry * 1.14, 0.16, 1210 + j, top=0.96, sides=12, jit=0.03, up_hint=V(1, 0, 0)), "trim")
        for sg in (1, -1):
            E(b, hullrock(c + n * 0.16 + V(rx * 0.52 * sg, 0, -0.08), n, 0.80, 0.50, 0.10, 1220 + j * 2 + (sg > 0), top=0.9, sides=8, jit=0.05, up_hint=V(1, 0, 0)), "team")
    # crest: a DOUBLE row of big stone dorsal plates splayed outward (stegosaur-like, staggered), broad along the body and
    # thin across it: a jagged crenellated top line from every side, gaps between plates (the opposite of MIRE's one smooth
    # sail); gold rim behind each; a team-glow diamond on each outer face
    for k, cb in enumerate(CREST):
        b0, d, L = crest_base(cb); hgt = L + 0.42
        big = k in (1, 2, 3, 4)
        for sgn in (1, -1):
            hh = hgt * (0.95 if (k + (sgn > 0)) % 2 else 0.78)
            n = (d + V(0.55 * sgn, 0, 0)).normalized(); base = b0 + V(0.20 * sgn, 0.08 * sgn, -0.05)
            E(cb, wedge(base, n, 0.18, 0.92 if big else 0.72, hh, 700 + k * 2 + (sgn > 0), tip=0.28, up_hint=V(1, 0, 0)), "plate_lt")
            E(cb, wedge(base - n * 0.10, n, 0.12, (0.92 if big else 0.72) * 1.16, hh * 0.86, 740 + k * 2 + (sgn > 0), tip=0.34, up_hint=V(1, 0, 0)), "trim")
            ox = V(1, 0, 0).cross(n).cross(n).normalized() * -sgn if False else (V(sgn, 0, 0) - n * n.x * sgn).normalized()
            E(cb, octa(base + n * hh * 0.48 + ox * 0.10, ox, 0.14 if big else 0.11, up_hint=n), "tglow")
    # CROWN on Crystal1 (head): a gold sunburst arc of spikes standing behind the brow (the ELDER silhouette), centre gem
    cc = V(0, -1.30, 3.30); Mc = Matrix.Rotation(math.radians(-22), 3, 'X')   # arc plane tilted back
    arc = []
    for k in range(9):
        a = math.radians(-72 + k * 18); u = Mc @ V(math.sin(a), 0, math.cos(a))
        arc.append(cc + u * 0.85)
        Ls = 1.05 + 1.25 * math.sin(a) ** 2
        E("Crystal1", cone(cc + u * 0.70, cc + u * (0.85 + Ls), 0.24 if k % 2 == 0 else 0.18, 6), "trim" if k % 2 == 0 else "accent")
    E("Crystal1", K.round_path(arc, [0.13] * len(arc), 6, V(0, 1, 0)), "accent_dk")
    E("Crystal1", octa(cc + Mc @ V(0, -0.12, 0.92), (Mc @ V(0, -1, 0.3)).normalized(), 0.13, up_hint=V(0, 0, 1)), "tglow")
    E("Head", octa(V(0, -1.86, 3.48), V(0, -0.6, 1).normalized(), 0.09), "glow")       # forehead diamond
    # tail: stone scutes with gold edge, diamond on tip
    for i in range(1, 8):
        n, c, d, r = tail_top(i)
        E(n, hullrock(c, V(0, 0, 1), r * 0.85, r * 0.95, max(0.08, r * 0.45), 900 + i, up_hint=d), "team" if i in (1, 3, 5) else "plate")
    n, c, d, r = tail_top(7, 0.9)
    E(n, octa(bt("Tail7") + d * 0.1, d, 0.16, up_hint=V(0, 0, 1), flat=0.5), "trim")

def grove():
    # bark plates (long, grooved) with a few moss caps; leafy crest; leaf bushes on the shoulders; vine tail with leaves
    for i, (b, p, nn, sz) in enumerate(ANCH):
        M = rot_to(nn); E(b, rbox(p, (sz * 1.6, sz * 0.9, 0.18), M, 2.2, 10, 4), "plate" if i % 2 else "plate_dk")
        E(b, rbox(p + nn * 0.08, (sz * 1.2, 0.07, 0.08), M, 2.0, 6, 3), "plate_dk")   # bark groove ridge
        if i % 4 == 0 and nn.z > 0.6: E(b, hullrock(p + nn * 0.12, nn, sz * 0.62, sz * 0.5, 0.16, 40 + i, top=0.5, jit=0.3), "accent")
    for i, (b, p, nn, sz) in enumerate(LIMB_ANCH):
        E(b, rbox(p, (sz * 1.4, sz * 0.9, 0.16), rot_to(nn), 2.2, 10, 4), "plate")
    for S, s in SIDES:
        shoulder_plate(S, s, "bark")
        # leaf bush over each shoulder: 7 broad leaves fanning out and up (the GROVE silhouette: soft, scalloped, wide)
        c0 = mx(V(1.22, -0.55, 3.40), s)
        for j in range(7):
            a = math.radians(-70 + j * 25)
            ax = mx(V(math.cos(a) * 0.9, math.sin(a) * 0.9, 0.55), s).normalized()
            E("Shoulder_" + S, leaf(c0, ax, V(0, 0, 1).cross(ax).normalized(), 0.95 if j % 2 else 0.8, 0.36, 0.035, 7, bend=-0.18),
              "team" if j % 2 else "trim")
    # tree crest: a trunk and a round canopy of faceted leaf clumps grows out of the upper back (the GROVE silhouette)
    t0 = V(0, 0.30, 3.30); t1 = V(0, 0.75, 3.75)
    E("Crystal5", tube(t0, t1, [(0, 0.34, 0.30), (0.6, 0.24, 0.22), (1.0, 0.26, 0.24)], 10, hint=V(1, 0, 0)), "plate")
    for s_ in (1, -1):
        E("Crystal5", K.round_path([t0.lerp(t1, 0.6), V(0.55 * s_, 0.05, 4.00), V(0.95 * s_, 0.10, 4.00)], [0.13, 0.10, 0.07], 6), "plate")
    # bark collar: a team-painted bark ring round the trunk foot and a team collar plate over the neck (seen from above)
    E("Crystal5", tube(t0 - V(0, 0, 0.05), t0 + V(0, 0, 0.22), [(0, 0.52, 0.50), (1.0, 0.48, 0.46)], 12, hint=V(1, 0, 0)), "team")
    E("Neck", hullrock(V(0, -0.98, 3.42), V(0, -0.3, 1).normalized(), 0.62, 0.85, 0.16, 77, top=0.9, sides=10, jit=0.06, up_hint=V(1, 0, 0)), "team")
    CL = [(V(0, 0.0, 4.70), 0.70, "accent"), (V(1.00, -0.05, 4.40), 0.58, "trim"), (V(-1.00, -0.05, 4.40), 0.58, "accent_dk"),
          (V(0.55, 0.75, 4.42), 0.55, "accent_dk"), (V(-0.55, 0.75, 4.42), 0.55, "trim"), (V(0.0, -0.80, 4.40), 0.52, "trim"),
          (V(0.0, 0.18, 4.92), 0.42, "trim"), (V(0.62, -0.62, 4.36), 0.46, "accent"), (V(-0.62, -0.62, 4.36), 0.46, "accent")]
    for k, (c, r, col) in enumerate(CL):
        c = V(c.x * 1.30, c.y * 1.10 + 0.75, c.z - 0.50); r = r * 1.10
        bone = "Crystal6" if c.y < 0.35 else ("Crystal7" if c.y < 0.95 else "Crystal8")
        E(bone, hullrock(c, V(0, 0, 1), r, r * 0.92, r * 1.2, 600 + k, top=0.55, sides=9, jit=0.22), col)
        # team blossom on top of every clump: a broad flat flower (ownership reads through the canopy), glowing centre
        if k != 6:
            q = c + V(0.18 * math.copysign(1, c.x or 1), 0.10, r * 0.56)
            E(bone, hullrock(q, V(0, 0, 1), 0.34, 0.34, 0.09, 640 + k, top=0.75, sides=5, jit=0.10), "team")
            E(bone, ellipsoid(q + V(0, 0, 0.06), V(0.09, 0.09, 0.06), 8, 5), "tglow")
    E("Crystal4", ellipsoid(V(0.55, -0.35, 4.20), V(0.09, 0.09, 0.09), 8, 5), "glow")
    for k, cb in enumerate(CREST):
        if cb not in ("Crystal2", "Crystal3"): continue        # the tree stands on the rear crest bones; leaf fans in front only
        b0, d, L = crest_base(cb); Lf = L + 0.30
        for j, (sx, col) in enumerate(((-0.9, "accent"), (0.0, "trim"), (0.9, "accent_dk"))):
            ax = (d + V(sx, 0, 0) * 0.75).normalized()
            E(cb, leaf(b0 + d * 0.15, ax, V(0, 1, 0), Lf * (1.0 if j == 1 else 0.82), 0.34 if j == 1 else 0.28, 0.035, 7, bend=-0.12), col)
    # head: a leafy tuft and a branch-horn pair (bark)
    c0 = bh("Crystal1")
    for j, sx in enumerate((-1, -0.35, 0.35, 1)):
        ax = V(sx * 0.7, 0.35, 1).normalized()
        E("Crystal1", leaf(c0 - V(0, 0, 0.1), ax, V(1, 0, 0), 0.72, 0.24, 0.03, 6, bend=-0.1), "accent" if j % 2 else "trim")
    for s in (1, -1):
        E("Head", horn(V(0.42 * s, -1.42, 3.36), V(0.6 * s, 0.2, 0.8), 0.6, 0.09, V(0.15 * s, 0.1, 0.0), 6), "plate")
    # vine tail: twisting vine coil + leaves along the tail, glowing berries near the tip
    for i in range(1, 8):
        n, c, d, r = tail_top(i)
        side = V(0, 0, 1).cross(d).normalized()
        E(n, leaf(c - V(0, 0, r * 0.2), (V(0, 0, 1) + side * (0.8 if i % 2 else -0.8)).normalized(), d, 0.35 + r, 0.18 + r * 0.3, 0.03, 6, bend=-0.15), "accent" if i % 2 else "trim")
        E(n, K.round_path([c - side * r * 0.95 - V(0, 0, r * 0.3), c + V(0, 0, r * 0.15), c + side * r * 0.95 - V(0, 0, r * 0.3)],
                          [0.06, 0.07, 0.06], 6), "accent_dk")
    for i in (6, 7):
        n, c, d, r = tail_top(i, 0.6); E(n, ellipsoid(c + V(0, 0, 0.06), V(0.08, 0.08, 0.08), 8, 5), "glow")

def brute():
    # heavy horned plates, thick forearm armour, amber vents in the gaps
    for i, (b, p, nn, sz) in enumerate(ANCH):
        E(b, wedge(p, nn, sz * 1.25, sz * 1.0, 0.34 + 0.12 * (i % 2), 200 + i, tip=0.45), "plate" if i % 3 else "plate_lt")
        if i % 2 == 0:
            M = rot_to(nn); E(b, box(p - nn * 0.02 + M @ V(0, sz * 0.62, 0), (sz * 0.9, 0.07, 0.16), 0.0, 1, M), "glow")   # vent slit
    for i, (b, p, nn, sz) in enumerate(LIMB_ANCH):
        big = "Forearm" in b
        E(b, wedge(p, nn, sz * (1.5 if big else 1.15), sz * 1.2, 0.36 if big else 0.28, 400 + i, tip=0.45), "plate_lt" if big else "plate")
        if big: E(b, box(p - nn * 0.03 + rot_to(nn) @ V(0, sz * 0.72, 0), (sz * 1.0, 0.07, 0.14), 0.0, 1, rot_to(nn)), "glow")
    for S, s in SIDES:
        shoulder_plate(S, s, "wedge", k=1.45)
        # big shoulder horns sweeping out and forward: the BRUTE silhouette (team-glow tips); thick enough to survive 1080p
        b = "Shoulder_" + S; h0 = mx(V(1.42 + 0.55 * (BX["sh"] - 1), -0.55, 3.52 + 0.45 * (BX["sh"] - 1)), s)
        E(b, horn(h0, mx(V(0.9, -0.25, 0.55), s), 1.05, 0.30, mx(V(0.25, -0.35, 0.25), s), 8), "accent")
        tip = h0 + mx(V(0.9, -0.25, 0.55), s).normalized() * 1.05 + mx(V(0.25, -0.35, 0.25), s)
        E(b, ellipsoid(tip, V(0.12, 0.12, 0.12), 8, 5), "tglow")
        # thick forearm armour cuff, team-painted (big top-facing team area on the huge forearms)
        f0, f1 = bh("Forearm_" + S), bt("Forearm_" + S); A = BX["arm"]
        E("Forearm_" + S, tube(f0, f1, [(0.50, 0.66 * A, 0.62 * A), (0.92, 0.70 * A, 0.66 * A)], 14, hint=V(0, 0, 1)), "team")
    # crest: tall blunt horn-plates, team-glow tips
    for k, cb in enumerate(CREST):
        b0, d, L = crest_base(cb); hgt = L + 0.38
        E(cb, wedge(b0, d, 0.60, 0.52, hgt, 800 + k, tip=0.28, up_hint=V(1, 0, 0), lean=V(0, 0, 0)), "plate_lt" if k % 2 else "plate")
        E(cb, ellipsoid(b0 + d * (hgt * 0.98), V(0.11, 0.11, 0.11), 8, 5), "tglow")
        E(cb, box(b0 + d * 0.30 + V(0, 0.29, 0), (0.42, 0.06, 0.12), 0.0, 1), "glow")
    # head: two big curved bull horns + nose horn
    for s in (1, -1):
        E("Head", horn(V(0.46 * s, -1.30, 3.38), V(0.95 * s, -0.1, 0.35), 0.95, 0.24, V(0.0, -0.45, 0.35), 8), "accent")
    E("Crystal1", horn(bh("Crystal1") + V(0, -0.30, -0.12), V(0, -0.5, 1), 0.45, 0.13, V(0, -0.12, 0), 6), "accent")
    for i in range(1, TN + 1):
        n, c, d, r = tail_top(i)
        E(n, wedge(c, V(0, 0, 1), r * 1.4, r * 1.2, 0.18 + r * 0.6, 950 + i, tip=0.3, up_hint=d), "plate")
    # short thick tail: ends at Tail4 in a spiked stone mace
    n = f"Tail{TN}"; d = (bt(n) - bh(n)).normalized(); e = bt(n); e = V(e.x, e.y, max(e.z, 0.50))
    E(n, hullrock(e, d, 0.62, 0.62, 0.70, 990, top=0.7, sides=9, jit=0.12, up_hint=V(0, 0, 1)), "plate_lt")
    for k2, dv in enumerate((V(0, 0, 1), V(1, 0, 0.3), V(-1, 0, 0.3), V(d.x, d.y, 0.25))):
        E(n, horn(e + dv.normalized() * 0.40, dv, 0.40, 0.14, V(0, 0, 0), 6), "accent")

def timber():
    # pale banded bark: transverse log plates across the back (alternating pale / dark bands), log spines, antler crest
    for b, (c, r) in MASS.items():
        for k, az in enumerate((-60, -20, 20, 60)):   # bands that run over the top of each boulder, side to side
            dvec = V(0, math.sin(math.radians(az)), math.cos(math.radians(az)))
            p, nn = on_ell(c, r, dvec)
            ax = V(1, 0, 0); E(b, rbox(p - nn * 0.05, (r.x * 1.85, 0.30, 0.20), rot_to(nn, ax), 2.6, 12, 4), ("team" if k == 1 else "plate_lt") if k % 2 else "plate_dk")
    for i, (b, p, nn, sz) in enumerate(LIMB_ANCH):
        E(b, rbox(p, (sz * 1.3, sz * 0.9, 0.16), rot_to(nn), 2.6, 10, 4), "plate")
    for S, s in SIDES: shoulder_plate(S, s, "log")
    # round 2 MASS: a long LOW LOG STACK along the back (three logs below, two on top) cut into one section per bone so
    # the spine bends between sections; ring-end caps; a team-painted band round the top logs of every section
    SEG = [("Neck", -1.45, -1.00), ("Chest", -1.00, -0.55), ("Spine3", -0.55, -0.05), ("Spine2", -0.05, 0.45), ("Spine", 0.45, 0.95), ("Hips", 0.95, 1.45)]
    RL = 0.25
    for j, (b, y0, y1) in enumerate(SEG):
        z0, z1 = top_z(y0) - 0.10, top_z(y1) - 0.10
        for (x, dz, col) in ((-0.56, 0, "plate_lt"), (0.0, 0.04, "plate"), (0.56, 0, "plate_lt"), (-0.28, 0.44, "plate"), (0.28, 0.44, "plate_lt")):
            p0 = V(x, y0 + 0.03, z0 + RL + dz); p1 = V(x, y1 - 0.03, z1 + RL + dz)
            E(b, cylinder(p0, p1, RL, 10), col)
            for pe in (p0, p1): E(b, cylinder(pe - (p1 - p0).normalized() * 0.02, pe + (p1 - p0).normalized() * 0.02, RL * 0.86, 10), "accent")
            if dz > 0.3: E(b, cylinder(p0.lerp(p1, 0.38), p0.lerp(p1, 0.62), RL * 1.08, 10), "team")
        if j in (1, 3): E(b, cylinder(V(0, (y0 + y1) / 2, z0 + RL * 2 + 0.50), V(0, (y0 + y1) / 2, z0 + RL * 2 + 0.56), 0.10, 7), "glow")
    for i in (1, 2):   # the stack runs on over the tail root
        n, c, d, r = tail_top(i)
        for x in (-0.30, 0.30):
            E(n, cylinder(bh(n) + V(x, 0, r * 0.9), bt(n) + V(x, 0, TR[i] * 0.9), 0.20 - 0.03 * i, 9), "plate_lt")
    # ANTLERS on Crystal1: branching tines, pale, team-glow tips
    c0 = bh("Crystal1") + V(0, 0.05, -0.1)
    for s in (1, -1):
        base = c0 + V(0.22 * s, 0, 0)
        main = [base, base + V(0.50 * s, 0.12, 0.40), base + V(1.10 * s, 0.30, 0.62), base + V(1.62 * s, 0.45, 0.95), base + V(1.95 * s, 0.55, 1.35)]
        E("Crystal1", K.round_path(main, [0.24, 0.22, 0.19, 0.15, 0.10], 7), "accent")
        for (a, b2) in ((1, V(0.05 * s, -0.35, 0.70)), (2, V(0.05 * s, -0.30, 0.75)), (3, V(0.25 * s, -0.25, 0.55)), (3, V(0.55 * s, 0.05, 0.05))):
            E("Crystal1", K.round_path([main[a], main[a] + b2 * 0.55, main[a] + b2], [0.15, 0.12, 0.08], 6), "accent")
            E("Crystal1", ellipsoid(main[a] + b2, V(0.10, 0.10, 0.10), 8, 5), "team")       # team-painted tine tips
        E("Crystal1", ellipsoid(main[-1], V(0.15, 0.15, 0.15), 8, 5), "tglow")
    # tail: stacked log rings
    for i in range(1, 8):
        n, c, d, r = tail_top(i)
        p0, p1 = bh(n), bt(n)
        E(n, tube(p0, p1, [(0.40, TR[i - 1] * 1.08, TR[i - 1] * 1.04), (0.55, TR[i - 1] * 1.08, TR[i - 1] * 1.04)], 12, hint=V(0, 0, 1), round0=False, round1=False), "plate_dk")
    # club tail: a big knotted burl on the tail tip
    d = (bt("Tail7") - bh("Tail7")).normalized()
    ce = bt("Tail7") - d * 0.10; ce = V(ce.x, ce.y, max(ce.z, 0.50))
    E("Tail7", hullrock(ce, d, 0.62, 0.58, 0.85, 995, top=0.72, sides=9, jit=0.15, up_hint=V(0, 0, 1)), "plate_lt")
    E("Tail7", cylinder(ce - d * 0.22, ce - d * 0.10, 0.62, 10), "team")
    c6 = bt("Tail6"); E("Tail6", hullrock(V(c6.x, c6.y, max(c6.z, 0.34)), d, 0.36, 0.34, 0.40, 996, top=0.8, sides=8, jit=0.12, up_hint=V(0, 0, 1)), "plate")

def mire():
    # smooth wet hide: no plates; dorsal sail fin with a team-glow rim; head frill; glowing sacs
    for i, (b, p, nn, sz) in enumerate(ANCH):
        if i % 3 == 0: E(b, ellipsoid(p + nn * 0.05, V(0.13, 0.13, 0.13), 10, 6), "glow")   # sacs
    for i, (b, p, nn, sz) in enumerate(LIMB_ANCH):
        if "Forearm" in b:   # arm fins
            d = (bt(b) - bh(b)).normalized()
            E(b, leaf(p - nn * 0.1, (nn + d * 0.3).normalized(), d, 0.75, 0.42, 0.035, 7, bend=0.15), "accent")
    for S, s in SIDES: shoulder_plate(S, s, "smooth", k=1.45)
    # sail: one membrane panel per crest bone; the top follows ONE arch over the whole back so the panels read as one fin
    def ztop(y): return 3.55 + 1.85 * max(0.0, 1 - ((y + 0.25) / 1.60) ** 2) ** 0.6
    for k, cb in enumerate(CREST):
        b0, d, L = crest_base(cb)
        y0, y1 = b0.y - 0.44, b0.y + 0.44; NJ = 5
        ys = [y0 + (y1 - y0) * j / NJ for j in range(NJ + 1)]
        outer = [[V(0.04, y, b0.z - 0.25) for y in ys], [V(0.04, y, max(b0.z + 0.1, ztop(y))) for y in ys]]
        inner = [[q - V(0.08, 0, 0) for q in row] for row in outer]
        E(cb, slab(outer, inner), "accent")
        # team band along the upper sail (wraps the membrane, reads from both sides and above)
        tb_o = [[V(0.075, y, max(b0.z + 0.1, ztop(y)) - 0.52) for y in ys], [V(0.075, y, max(b0.z + 0.1, ztop(y)) - 0.10) for y in ys]]
        tb_i = [[q - V(0.15, 0, 0) for q in row] for row in tb_o]
        if ztop(b0.y) - b0.z > 0.7: E(cb, slab(tb_o, tb_i), "team")
        top = [V(0, y, max(b0.z + 0.1, ztop(y))) for y in ys]
        E(cb, K.round_path(top, [0.055] * len(top), 6, V(1, 0, 0)), "tglow")
        E(cb, K.round_path([V(0, b0.y, b0.z - 0.1), V(0, b0.y, ztop(b0.y) - 0.05)], [0.06, 0.03], 5), "accent_dk")
    # gill stalks (axolotl-like): three feathery stalks each side of the head sweeping back and out (the MIRE silhouette)
    for s_ in (1, -1):
        for j, (up, ln) in enumerate(((1.10, 1.95), (0.62, 2.25), (0.22, 1.85))):
            base = V(0.42 * s_, -1.30, 3.24 + 0.10 * up)
            dvec = V(0.85 * s_, -0.10, up).normalized(); bend = V(0.0, 0.30, 0.10)
            pts = [base + dvec * (ln * t) + bend * (t * t) for t in (0, 0.33, 0.66, 1.0)]
            E("Head", K.round_path(pts, [0.12, 0.10, 0.075, 0.045], 6), "accent")
            for q in (0.35, 0.55, 0.75, 0.92):
                pq = base + dvec * (ln * q) + bend * (q * q)
                for f in (1, -1):
                    fd = (V(0, 0, 1) * f + V(0, 0.6, 0)).normalized()
                    E("Head", leaf(pq, fd, dvec, 0.42, 0.13, 0.025, 4), "trim")
            E("Head", ellipsoid(pts[-1], V(0.07, 0.07, 0.07), 8, 5), "glow")
    # side sacs on the flanks and a glowing tail-tip bulb; tail fin
    for s in (1, -1):
        E("Spine2", ellipsoid(V(1.10 * s, 0.10, 2.60), V(0.16, 0.20, 0.18), 10, 6), "glow")
        E("Spine", ellipsoid(V(1.00 * s, 0.50, 2.20), V(0.14, 0.17, 0.15), 10, 6), "glow")
    for i in range(2, 5):
        n, c, d, r = tail_top(i)
        E(n, leaf(c - V(0, 0, r * 0.3), V(0, 0, 1), d, 0.35 + r * 1.2, 0.22 + r * 0.4, 0.03, 6), "accent")
    # round 2 MASS: a wide FLAT tail fluke (horizontal, whale-like) over Tail4..Tail7, team-painted trailing tips
    for i, wdt in ((4, 1.50), (5, 2.40), (6, 3.20), (7, 3.70)):
        n = f"Tail{i}"; p0, p1 = bh(n), bt(n); d = (p1 - p0).normalized(); L = (p1 - p0).length
        side = V(0, 0, 1).cross(d).normalized(); cm = p0.lerp(p1, 0.5); cm = V(cm.x, cm.y, max(cm.z, 0.12))
        for sg in (1, -1):
            E(n, leaf(cm, side * sg, d, wdt * 0.5, L * 1.15, 0.05, 7), "accent" if i % 2 else "accent_dk")
            if i >= 6: E(n, leaf(cm + side * sg * (wdt * 0.5 - 0.55) + V(0, 0, 0.03), side * sg, d, 0.55, L * 0.8, 0.06, 5), "team")
    E("Tail7", ellipsoid(bt("Tail7"), V(0.14, 0.14, 0.14), 10, 6), "glow")

{"Elder": elder, "Grove": grove, "Brute": brute, "Timber": timber, "Mire": mire}[TYPE]()

# ------------------------------------------------------------------ assemble ONE mesh, vertex groups for every bone
ob, rep = kit.assemble(f"Kaiju_{TYPE}", WORK, WORK)
name = "Kaiju" + ("" if TYPE == "Elder" else "_" + TYPE) + "_" + LOD
ob.name = ob.data.name = name
for b in rig.data.bones:
    if b.name not in ob.vertex_groups: ob.vertex_groups.new(name=b.name)
rep.update({"type": TYPE, "lod": LOD, "name": name, "dropped": LH.DROPPED})
bpy.ops.wm.save_as_mainfile(filepath=WORK + "/Kaiju.blend")
K.save_report(rep, WORK + "/build_report.json")
