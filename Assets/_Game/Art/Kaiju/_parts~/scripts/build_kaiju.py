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
seg_body = 22
MASS = {   # bone: (centre, radii, rot)   torso boulders overlapping ~40% so the spine can bend without gaps
    "Hips":   (V(0, 0.86, 1.62), V(0.98, 0.86, 0.74)),
    "Spine":  (V(0, 0.42, 2.14), V(1.12, 0.92, 0.82)),
    "Spine2": (V(0, 0.04, 2.55), V(1.22, 0.95, 0.88)),
    "Spine3": (V(0, -0.30, 2.94), V(1.28, 0.90, 0.86)),
    "Chest":  (V(0, -0.64, 2.80), V(1.18, 0.74, 0.80)),
}
for b, (c, r) in MASS.items():
    E(b, ellipsoid(c, r, seg_body, 12), "hide")
# back hump that lifts the top line (the crest grows out of it)
E("Spine3", ellipsoid(V(0, -0.18, 3.28), V(0.98, 0.82, 0.56), 18, 9), "hide")
E("Spine2", ellipsoid(V(0, 0.30, 3.02), V(0.92, 0.72, 0.50), 18, 9), "hide")
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
    E(sh, ellipsoid(mx(V(1.10, -0.62, 2.92), s), V(0.64, 0.64, 0.60), 16, 9), "hide")
    a0, a1 = bh(ar), bt(ar)
    E(ar, tube(a0, a1, [(-0.12, 0.47, 0.47), (0.0, 0.48, 0.48), (0.5, 0.45, 0.45), (1.0, 0.40, 0.40), (1.08, 0.36, 0.36)], 14), "hide")
    f0, f1 = bh(fa), bt(fa)
    E(fa, ellipsoid(f0, V(0.43, 0.43, 0.43), 12, 7), "hide")   # elbow collar (child side)
    E(fa, tube(f0, f1, [(-0.05, 0.40, 0.40), (0.3, 0.48, 0.46), (0.75, 0.58, 0.55), (1.02, 0.56, 0.54), (1.12, 0.46, 0.44)], 14), "hide")
    # fist: big block along the bone, knuckle fingers on their own bones
    k0, k1 = bh(fi), bt(fi); fd = (k1 - k0).normalized()
    E(fi, rbox((k0 + k1) / 2 + V(0, -0.06, 0), (0.92, 0.80, 0.78), rot_to(fd, V(0, -1, 0)), 2.6, 14, 6), "hide")
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
TR = [0.58, 0.50, 0.42, 0.34, 0.27, 0.20, 0.14, 0.06]
for i in range(1, 8):
    n = f"Tail{i}"; p0, p1 = bh(n), bt(n); L = (p1 - p0).length
    E(n, ellipsoid(p0, V(TR[i - 1] * 1.03, TR[i - 1] * 1.03, TR[i - 1] * 1.03), 12, 7), "hide")
    E(n, tube(p0, p1, [(0, TR[i - 1], TR[i - 1] * 0.95), (1.0, TR[i], TR[i] * 0.95), (1.1, TR[i] * 0.92, TR[i] * 0.88)], 12,
              hint=V(0, 0, 1), round1=(i == 7)), "hide")

# ------------------------------------------------------------------ plate anchors (shared placement, type style)
ANCH = []   # (bone, point, normal, size)
for b, (c, r) in MASS.items():
    for az, el, sz in ((0, 58, .55), (180, 58, .55), (90, 62, .55), (40, 30, .5), (140, 30, .5)):
        if b == "Chest" and az == 90: continue
        if b == "Hips" and el < 40: continue
        p, nn = on_ell(c, r, dirz(az, el)); ANCH.append((b, p, nn, sz * (1.15 if b in ("Spine2", "Spine3") else 1.0)))
for b, (c, r) in (("Spine3", (V(0, -0.18, 3.28), V(0.98, 0.82, 0.56))), ("Spine2", (V(0, 0.30, 3.02), V(0.92, 0.72, 0.50)))):
    for az in (25, 155):
        p, nn = on_ell(c, r, dirz(az, 55)); ANCH.append((b, p, nn, 0.55))
LIMB_ANCH = []   # forearm / thigh / shin outer plates
for S, s in SIDES:
    f0, f1 = bh("Forearm_" + S), bt("Forearm_" + S)
    for t, sz in ((0.35, .5), (0.72, .58)):
        c = f0.lerp(f1, t); LIMB_ANCH.append(("Forearm_" + S, c + V(s * 0.46, 0.02, 0.10), V(s, 0.0, 0.45).normalized(), sz))
    t0, t1 = bh("Thigh_" + S), bt("Thigh_" + S)
    LIMB_ANCH.append(("Thigh_" + S, t0.lerp(t1, 0.45) + V(s * 0.58, 0.05, 0.12), V(s, 0.15, 0.55).normalized(), 0.56))
    a0, a1 = bh("Arm_" + S), bt("Arm_" + S)
    LIMB_ANCH.append(("Arm_" + S, a0.lerp(a1, 0.5) + V(s * 0.38, 0.0, 0.18), V(s, 0, 0.7).normalized(), 0.44))

def tail_top(i, t=0.5):
    n = f"Tail{i}"; p0, p1 = bh(n), bt(n); c = p0.lerp(p1, t); r = TR[i - 1] + (TR[i] - TR[i - 1]) * t
    return n, c + V(0, 0, r * 0.88), (p1 - p0).normalized(), r

CREST = [f"Crystal{i}" for i in range(2, 9)]
def crest_base(cb):
    """Crest element base on the back surface under the crystal bone, and its up direction (bone axis)."""
    h, t = bh(cb), bt(cb); d = (t - h).normalized()
    return h - d * 0.42, d, (t - h).length

# ------------------------------------------------------------------ TYPE PART SETS
def shoulder_plate(S, s, style):
    b = "Shoulder_" + S; c = mx(V(1.18, -0.62, 3.36), s); n = mx(V(0.45, 0, 1), s).normalized()
    if style == "rock":
        c = c + mx(V(0.30, 0, 0.02), s); n = mx(V(0.55, 0, 1), s).normalized()
        E(b, hullrock(c + n * 0.10, n, 0.62, 0.55, 0.26, 11 + s, top=0.7), "team")
        E(b, hullrock(c - n * 0.02, n, 0.95, 0.80, 0.20, 21 + s, top=0.92, jit=0.08), "team_dk")
        E(b, hullrock(c - n * 0.12, n, 1.05, 0.88, 0.10, 31 + s, top=0.97, jit=0.05), "trim")
    elif style == "wedge":
        E(b, wedge(c, n, 0.95, 0.80, 0.42, 7 + s, tip=0.5), "team")
    elif style == "bark":
        E(b, rbox(c, (0.95, 0.85, 0.24), rot_to(n), 2.2, 12, 4), "team")
    elif style == "log":
        for k in (-1, 0, 1):
            q = c + V(0, k * 0.27, -0.04 * abs(k))
            E(b, cylinder(q + V(-s * 0.40, 0, 0.0), q + V(s * 0.40, 0, -0.18), 0.15, 10), "team")
    elif style == "smooth":
        E(b, ellipsoid(c - n * 0.04, V(0.60, 0.55, 0.20), 14, 7, rot_to(n)), "team")

def elder():
    # ancient plates with gold trim rims: a gold under-slab peeking out around each stone plate
    for i, (b, p, nn, sz) in enumerate(ANCH):
        E(b, hullrock(p + nn * 0.02, nn, sz, sz * 0.86, 0.26, 100 + i, top=0.62), "plate" if i % 3 else "plate_lt")
        E(b, hullrock(p - nn * 0.05, nn, sz * 1.10, sz * 0.96, 0.12, 300 + i, top=0.95, jit=0.08), "trim")
    for i, (b, p, nn, sz) in enumerate(LIMB_ANCH):
        E(b, hullrock(p, nn, sz, sz * 0.8, 0.24, 500 + i), "plate")
        E(b, hullrock(p - nn * 0.05, nn, sz * 1.1, sz * 0.9, 0.10, 520 + i, top=0.95, jit=0.08), "trim")
        if "Forearm" in b: E(b, octa(p + nn * 0.13, nn, 0.11), "glow")
    for S, s in SIDES: shoulder_plate(S, s, "rock")
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
        Ls = 0.85 + 1.05 * math.sin(a) ** 2
        E("Crystal1", cone(cc + u * 0.70, cc + u * (0.85 + Ls), 0.17 if k % 2 == 0 else 0.13, 6), "trim" if k % 2 == 0 else "accent")
    E("Crystal1", K.round_path(arc, [0.13] * len(arc), 6, V(0, 1, 0)), "accent_dk")
    E("Crystal1", octa(cc + Mc @ V(0, -0.12, 0.92), (Mc @ V(0, -1, 0.3)).normalized(), 0.13, up_hint=V(0, 0, 1)), "tglow")
    E("Head", octa(V(0, -1.86, 3.48), V(0, -0.6, 1).normalized(), 0.09), "glow")       # forehead diamond
    # tail: stone scutes with gold edge, diamond on tip
    for i in range(1, 8):
        n, c, d, r = tail_top(i)
        E(n, hullrock(c, V(0, 0, 1), r * 0.85, r * 0.95, max(0.08, r * 0.45), 900 + i, up_hint=d), "plate")
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
              "accent" if j % 2 else "trim")
    # tree crest: a trunk and a round canopy of faceted leaf clumps grows out of the upper back (the GROVE silhouette)
    t0 = V(0, -0.15, 3.55); t1 = V(0, 0.0, 4.35)
    E("Crystal5", tube(t0, t1, [(0, 0.34, 0.30), (0.6, 0.24, 0.22), (1.0, 0.26, 0.24)], 10, hint=V(1, 0, 0)), "plate")
    for s_ in (1, -1):
        E("Crystal5", K.round_path([t0.lerp(t1, 0.6), V(0.55 * s_, 0.05, 4.40), V(0.95 * s_, 0.10, 4.40)], [0.13, 0.10, 0.07], 6), "plate")
    CL = [(V(0, 0.0, 4.70), 0.70, "accent"), (V(1.00, -0.05, 4.40), 0.58, "trim"), (V(-1.00, -0.05, 4.40), 0.58, "accent_dk"),
          (V(0.55, 0.75, 4.42), 0.55, "accent_dk"), (V(-0.55, 0.75, 4.42), 0.55, "trim"), (V(0.0, -0.80, 4.40), 0.52, "trim"),
          (V(0.0, 0.18, 4.92), 0.42, "trim"), (V(0.62, -0.62, 4.36), 0.46, "accent"), (V(-0.62, -0.62, 4.36), 0.46, "accent")]
    for k, (c, r, col) in enumerate(CL):
        bone = "Crystal5" if k in (0, 5, 6) else ("Crystal4" if c.y < 0.2 else "Crystal6")
        c = V(c.x * 1.55, c.y * 1.25, c.z + 0.18); r = r * 1.30
        E(bone, hullrock(c, V(0, 0, 1), r, r * 0.92, r * 1.2, 600 + k, top=0.55, sides=9, jit=0.22), col)
    E("Crystal5", ellipsoid(V(0.25, -0.40, 4.85), V(0.10, 0.10, 0.10), 8, 5), "tglow")
    E("Crystal5", ellipsoid(V(-0.35, 0.30, 4.90), V(0.10, 0.10, 0.10), 8, 5), "tglow")
    E("Crystal4", ellipsoid(V(0.55, -0.35, 4.62), V(0.09, 0.09, 0.09), 8, 5), "glow")
    for k, cb in enumerate(CREST):
        if cb in ("Crystal4", "Crystal5", "Crystal6"): continue
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
        shoulder_plate(S, s, "wedge")
        # big shoulder horns sweeping out and forward: the BRUTE silhouette (team-glow tips)
        b = "Shoulder_" + S; h0 = mx(V(1.42, -0.55, 3.52), s)
        E(b, horn(h0, mx(V(0.9, -0.25, 0.55), s), 1.05, 0.20, mx(V(0.25, -0.35, 0.25), s), 8), "accent")
        tip = h0 + mx(V(0.9, -0.25, 0.55), s).normalized() * 1.05 + mx(V(0.25, -0.35, 0.25), s)
        E(b, ellipsoid(tip, V(0.07, 0.07, 0.07), 8, 5), "tglow")
        # thick forearm armour cuff
        f0, f1 = bh("Forearm_" + S), bt("Forearm_" + S)
        E("Forearm_" + S, tube(f0, f1, [(0.55, 0.66, 0.62), (0.95, 0.70, 0.66)], 12, hint=V(0, 0, 1)), "plate_dk")
    # crest: tall blunt horn-plates, team-glow tips
    for k, cb in enumerate(CREST):
        b0, d, L = crest_base(cb); hgt = L + 0.38
        E(cb, wedge(b0, d, 0.60, 0.52, hgt, 800 + k, tip=0.28, up_hint=V(1, 0, 0), lean=V(0, 0, 0)), "plate_lt" if k % 2 else "plate")
        E(cb, ellipsoid(b0 + d * (hgt * 0.98), V(0.07, 0.07, 0.07), 8, 5), "tglow")
        E(cb, box(b0 + d * 0.30 + V(0, 0.29, 0), (0.42, 0.06, 0.12), 0.0, 1), "glow")
    # head: two big curved bull horns + nose horn
    for s in (1, -1):
        E("Head", horn(V(0.46 * s, -1.30, 3.38), V(0.95 * s, -0.1, 0.35), 0.95, 0.16, V(0.0, -0.45, 0.35), 8), "accent")
    E("Crystal1", horn(bh("Crystal1") + V(0, -0.30, -0.12), V(0, -0.5, 1), 0.45, 0.13, V(0, -0.12, 0), 6), "accent")
    for i in range(1, 8):
        n, c, d, r = tail_top(i)
        E(n, wedge(c, V(0, 0, 1), r * 1.4, r * 1.2, 0.18 + r * 0.6, 950 + i, tip=0.3, up_hint=d), "plate")
    n, c, d, r = tail_top(7, 0.7)
    E(n, horn(bt("Tail7") - d * 0.15, d, 0.45, 0.12, V(0, 0, 0.12), 6), "accent")   # tail club spike

def timber():
    # pale banded bark: transverse log plates across the back (alternating pale / dark bands), log spines, antler crest
    for b, (c, r) in MASS.items():
        for k, az in enumerate((-60, -20, 20, 60)):   # bands that run over the top of each boulder, side to side
            dvec = V(0, math.sin(math.radians(az)), math.cos(math.radians(az)))
            p, nn = on_ell(c, r, dvec)
            ax = V(1, 0, 0); E(b, rbox(p - nn * 0.05, (r.x * 1.85, 0.30, 0.20), rot_to(nn, ax), 2.6, 12, 4), "plate_lt" if k % 2 else "plate_dk")
    for i, (b, p, nn, sz) in enumerate(LIMB_ANCH):
        E(b, rbox(p, (sz * 1.3, sz * 0.9, 0.16), rot_to(nn), 2.6, 10, 4), "plate")
    for S, s in SIDES: shoulder_plate(S, s, "log")
    # log spines: upright cut logs (pale bark sides, ring-end caps, sap glow cores on three)
    for k, cb in enumerate(CREST):
        b0, d, L = crest_base(cb); hgt = L + 0.30; r = 0.24 if k in (2, 3, 4) else 0.20
        top = b0 + d * hgt + V(0, 0.06 * (k % 2), 0)
        E(cb, cylinder(b0, top, r, 9, r * 0.92), "plate_lt")
        E(cb, cylinder(b0 + d * hgt * 0.33, b0 + d * (hgt * 0.33 + 0.07), r * 1.06, 9), "plate_dk")
        E(cb, cylinder(top - d * 0.02, top + d * 0.04, r * 0.82, 9), "accent")
        if k in (1, 3, 5): E(cb, cylinder(top + d * 0.03, top + d * 0.06, r * 0.36, 7), "glow")
    # ANTLERS on Crystal1: branching tines, pale, team-glow tips
    c0 = bh("Crystal1") + V(0, 0.05, -0.1)
    for s in (1, -1):
        base = c0 + V(0.22 * s, 0, 0)
        main = [base, base + V(0.50 * s, 0.12, 0.40), base + V(1.10 * s, 0.30, 0.62), base + V(1.62 * s, 0.45, 0.95), base + V(1.95 * s, 0.55, 1.35)]
        E("Crystal1", K.round_path(main, [0.16, 0.15, 0.13, 0.10, 0.06], 7), "accent")
        for (a, b2) in ((1, V(0.05 * s, -0.35, 0.70)), (2, V(0.05 * s, -0.30, 0.75)), (3, V(0.25 * s, -0.25, 0.55)), (3, V(0.55 * s, 0.05, 0.05))):
            E("Crystal1", K.round_path([main[a], main[a] + b2 * 0.55, main[a] + b2], [0.10, 0.08, 0.045], 6), "accent")
        E("Crystal1", ellipsoid(main[-1], V(0.07, 0.07, 0.07), 8, 5), "tglow")
    # tail: stacked log rings
    for i in range(1, 8):
        n, c, d, r = tail_top(i)
        p0, p1 = bh(n), bt(n)
        E(n, tube(p0, p1, [(0.40, TR[i - 1] * 1.08, TR[i - 1] * 1.04), (0.55, TR[i - 1] * 1.08, TR[i - 1] * 1.04)], 12, hint=V(0, 0, 1), round0=False, round1=False), "plate_dk")
        if i <= 5: E(n, cylinder(c - V(0, 0, 0.05), c + V(0, 0, 0.18 + r * 0.5), r * 0.45, 8), "plate_lt")

def mire():
    # smooth wet hide: no plates; dorsal sail fin with a team-glow rim; head frill; glowing sacs
    for i, (b, p, nn, sz) in enumerate(ANCH):
        if i % 3 == 0: E(b, ellipsoid(p + nn * 0.05, V(0.13, 0.13, 0.13), 10, 6), "glow")   # sacs
    for i, (b, p, nn, sz) in enumerate(LIMB_ANCH):
        if "Forearm" in b:   # arm fins
            d = (bt(b) - bh(b)).normalized()
            E(b, leaf(p - nn * 0.1, (nn + d * 0.3).normalized(), d, 0.75, 0.42, 0.035, 7, bend=0.15), "accent")
    for S, s in SIDES: shoulder_plate(S, s, "smooth")
    # sail: one membrane panel per crest bone; the top follows ONE arch over the whole back so the panels read as one fin
    def ztop(y): return 3.55 + 1.85 * max(0.0, 1 - ((y + 0.25) / 1.60) ** 2) ** 0.6
    for k, cb in enumerate(CREST):
        b0, d, L = crest_base(cb)
        y0, y1 = b0.y - 0.44, b0.y + 0.44; NJ = 5
        ys = [y0 + (y1 - y0) * j / NJ for j in range(NJ + 1)]
        outer = [[V(0.04, y, b0.z - 0.25) for y in ys], [V(0.04, y, max(b0.z + 0.1, ztop(y))) for y in ys]]
        inner = [[q - V(0.08, 0, 0) for q in row] for row in outer]
        E(cb, slab(outer, inner), "accent")
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
    for i in range(2, 8):
        n, c, d, r = tail_top(i)
        E(n, leaf(c - V(0, 0, r * 0.3), V(0, 0, 1), d, 0.35 + r * 1.2, 0.22 + r * 0.4, 0.03, 6), "accent")
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
