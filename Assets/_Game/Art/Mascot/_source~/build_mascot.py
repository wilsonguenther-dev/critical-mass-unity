"""Hatch, the Critical Mass tutorial mascot (the adult Kaiju's hatchling): model, rig, FBX and every render, by script.

ONE headless Blender run builds and renders everything (machine rule: one Blender start per job):
    ~/bin/cm-heavy blender perl -e 'alarm 1500; exec @ARGV' /Applications/Blender.app/Contents/MacOS/Blender -b \
        --factory-startup --python-exit-code 1 --python build_mascot.py -- <render_dir> <faces_dir> <Kaiju.blend> <Warrior.fbx>
then `python3 compose_mascot.py <render_dir> <repo> <report_dir>` (plain Python) makes the PNGs the game loads and the sheets.

Contract (same as the Kaiju, kaiju_palette.py on art/kaiju-blender): ONE material "CM_Toon"; colour in the vertex colour
layer "Col" (corner domain, sRGB bytes): RGB = lit colour of the part (top +6 % / under -8 % baked), ALPHA = mask code
0.0 fixed colour, 0.75 team glow (the lime crystals, Neutral allegiance baked); painted face decal on "UVMap"
(front planar projection of the head, paint_faces.py), file Mascot_Face.png next to the FBX (neutral face).
Rig "MascotRig" (13 bones, every part rigid on one bone) so a posed mesh per expression costs nothing.
Character faces -Y in Blender, feet at the origin, 1 unit = 1 m, 1.20 m tall (fits in the 1.70 m Kaiju egg).
"""
import json
import math
import os
import random
import shutil
import sys

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector
from bpy_extras.object_utils import world_to_camera_view

argv = sys.argv[sys.argv.index("--") + 1:]
OUT, FACES, KAIJU_BLEND, WARRIOR_FBX = argv[:4]
os.makedirs(OUT, exist_ok=True)
HERE = os.path.dirname(os.path.abspath(__file__))
ART = os.path.dirname(HERE)
EXT = 1.05                     # decal half-extent in head units (paint_faces.py)
TARGET_H = 1.20
TRI_CAP = 6000
random.seed(1207)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = "METRIC"
scene.unit_settings.scale_length = 1.0
COLL = scene.collection


def srgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def lin1(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def lin(h):
    return tuple(lin1(c) for c in srgb(h))


# ------------------------------------------------------------------------------------------------ palette
VOID_NAVY = "#18233B"
FIXED = {
    "Skin": "#5B6B86",        # baby hide: PAL Slate #415066 lit x1.3 (adult PAL Skin #36425A vanished on the Void Navy HUD panel)
    "Slate": "#415066",       # PAL Slate: boulder plates (shoulders, back, tail ridge)
    "SlateLight": "#ADA59B",  # PAL SlateLight: pale stone fists and horn buds (the hatchling's pale fists)
    "Moss": "#B4CF37",        # PAL Moss on up-facing plate facets
    "Belly": "#C9BFAA",       # PAL Belly: tan belly plates, chin
    "ShellOut": "#EFE7DA",    # eggshell outside: Warm Cream a step down (the top silhouette at 64 px)
    "ShellIn": "#83868C",     # inside of the shell: the Egg stage light plate
    "Seam": "#4FDDC3",        # CMTokens.EggSeam: the shell's cracked rim, the sweat drop
    "Claw": "#F7F0E5",        # Warm Cream claws and toes
}
GLOW = "#B8E833"              # PAL GLOW Neutral (hatchling lime)
GLOW_K = {"CrystalTip": 1.00, "Crystal": 0.86, "CrystalDark": 0.64, "CrystalBase": 0.50}
SLOTS = list(FIXED) + list(GLOW_K)
SI = {s: i for i, s in enumerate(SLOTS)}


def slot_rgba(slot):
    if slot in GLOW_K:
        return tuple(c * GLOW_K[slot] for c in srgb(GLOW)) + (0.75,)
    return srgb(FIXED[slot]) + (0.0,)


# ------------------------------------------------------------------------------------------------ geometry kit
class Geo:
    def __init__(self):
        self.V, self.F, self.slot, self.smooth, self.fs, self.decal, self.bone = [], [], [], [], [], [], []

    def add(self, verts, faces, slots, bone, smooth=False, fs=None, decal=None):
        o = len(self.V)
        self.V += [Vector(v) for v in verts]
        self.bone += [bone] * len(verts)
        for k, f in enumerate(faces):
            self.F.append(tuple(i + o for i in f))
            self.slot.append(slots[k] if isinstance(slots, (list, tuple)) else slots)
            self.smooth.append(smooth)
            self.fs.append(fs[k] if fs is not None else 1.0)
            self.decal.append(decal[k] if decal is not None else False)


def unit_sphere(seg, rings):
    V = [Vector((0, 0, 1))]
    for i in range(1, rings):
        th = math.pi * i / rings
        for j in range(seg):
            ph = 2 * math.pi * j / seg
            V.append(Vector((math.sin(th) * math.cos(ph), math.sin(th) * math.sin(ph), math.cos(th))))
    V.append(Vector((0, 0, -1)))
    F = []
    last = len(V) - 1
    for j in range(seg):
        F.append((0, 1 + j, 1 + (j + 1) % seg))
    for i in range(rings - 2):
        a, b = 1 + i * seg, 1 + (i + 1) * seg
        for j in range(seg):
            F.append((a + j, b + j, b + (j + 1) % seg, a + (j + 1) % seg))
    a = 1 + (rings - 2) * seg
    for j in range(seg):
        F.append((a + j, last, a + (j + 1) % seg))
    return V, F


def frame(n):
    z = Vector(n).normalized()
    x = Vector((1, 0, 0)) if abs(z.x) < 0.9 else Vector((0, 1, 0))
    y = z.cross(x).normalized()
    x = y.cross(z).normalized()
    return Matrix((x, y, z)).transposed()


def ellipsoid(geo, c, r, slot, bone, seg=14, rings=9, rot=None, slot_fn=None, smooth=True):
    U, F = unit_sphere(seg, rings)
    R = rot or Matrix.Identity(3)
    V = [Vector(c) + R @ Vector((u.x * r[0], u.y * r[1], u.z * r[2])) for u in U]
    slots, fs = [], []
    for f in F:
        d = sum((U[i] for i in f), Vector()).normalized()
        wc = sum((V[i] for i in f), Vector()) / len(f)
        s, k = slot_fn(R @ d, wc) if slot_fn else (slot, 1.0)
        slots.append(s)
        fs.append(k)
    geo.add(V, F, slots, bone, smooth=smooth, fs=fs)


def along(geo, a, b, rad, slot, bone, seg=12, rings=8, pad=0.06):
    a, b = Vector(a), Vector(b)
    L = (b - a).length
    ellipsoid(geo, (a + b) / 2, (rad, rad, L / 2 + pad), slot, bone, seg, rings, rot=frame(b - a))


def hull_piece(geo, pts, bone, slot_top="Moss", slot_side="Slate", moss_dot=0.55):
    bm = bmesh.new()
    for p in pts:
        bm.verts.new(p)
    bmesh.ops.convex_hull(bm, input=bm.verts[:])
    bm.normal_update()
    verts = [v.co.copy() for v in bm.verts]
    idx = {v: i for i, v in enumerate(bm.verts)}
    F, slots, fs = [], [], []
    cen = sum(verts, Vector()) / len(verts)
    for f in bm.faces:
        F.append(tuple(idx[v] for v in f.verts))
        n = f.normal if (f.calc_center_median() - cen).dot(f.normal) > 0 else -f.normal
        if n.z > moss_dot and slot_top:
            slots.append(slot_top)
            fs.append(1.0)
        else:
            slots.append(slot_side)
            fs.append(1.12 if n.z > 0.3 else (0.95 if n.z > -0.3 else 0.72))
    bm.free()
    geo.add(verts, F, slots, bone, smooth=False, fs=fs)


def boulder(geo, c, r, bone, up=(0, 0, 1), flat=0.55, n=14, moss=True):
    R = frame(up)
    pts = []
    for _ in range(n):
        d = Vector((random.gauss(0, 1), random.gauss(0, 1), random.gauss(0, 1))).normalized()
        d.z = min(d.z, flat)
        pts.append(Vector(c) + R @ Vector((d.x * r[0], d.y * r[1], d.z * r[2])) * random.uniform(0.92, 1.05))
    hull_piece(geo, pts, bone, "Moss" if moss else None)


def crystal(geo, base, d, length, radius, bone, sides=6, spin=0.0):
    d = Vector(d).normalized()
    R = frame(d) @ Matrix.Rotation(spin, 3, "Z")
    base = Vector(base) - d * length * 0.15
    V = []
    for k in range(sides):
        a = 2 * math.pi * k / sides
        V.append(base + R @ Vector((math.cos(a) * radius * 0.85, math.sin(a) * radius * 0.85, 0)))
    for k in range(sides):
        a = 2 * math.pi * k / sides
        V.append(base + R @ Vector((math.cos(a) * radius, math.sin(a) * radius, length * 0.72)))
    V.append(base + d * length)
    tip = len(V) - 1
    F, S = [], []
    F.append(tuple(reversed(range(sides))))
    S.append("CrystalBase")
    for k in range(sides):
        k2 = (k + 1) % sides
        F.append((k, k2, sides + k2, sides + k))
        S.append("Crystal" if k % 2 == 0 else "CrystalDark")
        F.append((sides + k, sides + k2, tip))
        S.append("CrystalTip" if k % 2 == 0 else "Crystal")
    geo.add(V, F, S, bone)


def cone(geo, base, d, length, radius, slot, bone, sides=7, mid=0.62, round_tip=0.35):
    d = Vector(d).normalized()
    R = frame(d)
    base = Vector(base)
    V = []
    for k in range(sides):
        a = 2 * math.pi * k / sides
        V.append(base + R @ Vector((math.cos(a) * radius, math.sin(a) * radius, 0)))
    for k in range(sides):
        a = 2 * math.pi * k / sides
        V.append(base + R @ Vector((math.cos(a) * radius * round_tip * 1.6, math.sin(a) * radius * round_tip * 1.6,
                                    length * mid)))
    V.append(base + d * length)
    tip = len(V) - 1
    F = [tuple(reversed(range(sides)))]
    for k in range(sides):
        k2 = (k + 1) % sides
        F.append((k, k2, sides + k2, sides + k))
        F.append((sides + k, sides + k2, tip))
    fs = [0.8] + [1.0] * (2 * sides)
    geo.add(V, F, slot, bone, smooth=False, fs=fs)


def loft(geo, path, radii, slot_fn, bone, sides=9):
    path = [Vector(p) for p in path]
    V, F, S, FSx = [], [], [], []
    rings = []
    for i, p in enumerate(path):
        t = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
        R = frame(t)
        ring = []
        for k in range(sides):
            a = 2 * math.pi * k / sides + math.pi / 2
            ring.append(len(V))
            V.append(p + R @ Vector((math.cos(a) * radii[i], math.sin(a) * radii[i], 0)))
        rings.append(ring)
    for i in range(len(rings) - 1):
        for k in range(sides):
            k2 = (k + 1) % sides
            f = (rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k])
            F.append(f)
            c = sum((V[j] for j in f), Vector()) / 4
            s, fsv = slot_fn(c, c - path[i])
            S.append(s)
            FSx.append(fsv)
    F.append(tuple(reversed(rings[0])))
    S.append("Skin")
    FSx.append(1.0)
    tipv = len(V)
    V.append(path[-1] + (path[-1] - path[-2]).normalized() * radii[-1] * 1.2)
    for k in range(sides):
        F.append((rings[-1][k], rings[-1][(k + 1) % sides], tipv))
        S.append("Skin")
        FSx.append(1.0)
    geo.add(V, F, S, bone, smooth=True, fs=FSx)


# ------------------------------------------------------------------------------------------------ the design (units: head radius 1)
H = Vector((0.0, -0.05, 2.08))            # head centre
HS = Vector((1.0, 0.90, 0.86))            # head scale
NECK = Vector((0.0, 0.0, 1.40))


def head_point(d):
    d = Vector(d).normalized()
    p = Vector((d.x * HS.x, d.y * HS.y, d.z * HS.z))
    if d.z < 0.2:
        p.x *= 1.0 + 0.07 * min(1.0, (0.2 - d.z) / 0.8)     # chubby jowls
    w = math.exp(-((d.x / 0.50) ** 2 + ((d.z + 0.40) / 0.34) ** 2)) * max(0.0, -d.y) ** 1.5
    p.y -= 0.20 * w                                        # short blunt muzzle
    return H + p


def build_head(geo):
    U, F = unit_sphere(32, 20)
    V = [head_point(u) for u in U]
    slots, decal = [], []
    for f in F:
        d = sum((U[i] for i in f), Vector()).normalized()
        slots.append("Skin")
        decal.append(d.y < -0.25 and d.z > -0.80)
    geo.add(V, F, slots, "Head", smooth=True, decal=decal)


SIDE = (1, -1)                              # +1 = the mascot's left (screen right in front view)
SH = {s: Vector((0.72 * s, -0.02, 1.32)) for s in SIDE}
EL = {s: Vector((0.88 * s, -0.10, 1.00)) for s in SIDE}
TP = {s: Vector((0.92 * s, -0.24, 0.74)) for s in SIDE}
SHELL_O = H + Vector((-0.08, 0.10, 0.50))
SHELL_R = Euler((math.radians(-10), math.radians(-16), 0.0)).to_matrix()
HORN = {s: (head_point((0.74 * s, -0.30, 0.52)), Vector((0.55 * s, -0.12, 0.82)).normalized()) for s in SIDE}
TAIL = [(0, 0.55, 0.40), (0, 0.92, 0.30), (0.05, 1.22, 0.30), (0.12, 1.44, 0.43), (0.18, 1.56, 0.64)]
TAIL_R = [0.30, 0.24, 0.18, 0.12, 0.07]


def hand(geo, e, t, s, bone, scale=1.0):
    e, t = Vector(e), Vector(t)
    d = (t - e).normalized()
    c = e + (t - e) * 0.55
    ellipsoid(geo, c, (0.19 * scale, 0.18 * scale, 0.21 * scale), "SlateLight", bone, 12, 8, rot=frame(d),
              slot_fn=lambda n, w: ("SlateLight", 1.06 if n.z > 0.4 else (0.9 if n.z < -0.4 else 1.0)))
    fwd = Vector((0, -1, 0))
    side = d.cross(fwd).normalized() if abs(d.dot(fwd)) < 0.95 else Vector((1, 0, 0))
    for k, (off, ln) in enumerate(((-0.09, 0.12), (0.0, 0.16), (0.09, 0.12))):
        b = c + d * 0.16 * scale + side * off * scale + fwd * (0.07 if k == 1 else 0.03) * scale
        cone(geo, b, (d * 0.8 + fwd * 0.45).normalized(), ln * scale, 0.045 * scale, "Claw", bone, sides=5)


def shell(geo):
    """Cracked eggshell cap: dome outside (egg grey), cream inside, a zigzag rim with an egg-seam band."""
    K, Ro, Hd = 18, 0.84, 0.44
    zig = [(-0.13 if j % 2 == 0 else 0.0) + random.uniform(-0.025, 0.025) for j in range(K)]
    ts = [0.30, 0.60, 0.86, 1.0]

    def P(t, j, sc=1.0, hs=1.0):
        a = t * math.pi / 2
        th = 2 * math.pi * j / K
        z = Hd * hs * math.cos(a) + (zig[j] * (t - 0.72) / 0.28 if t > 0.72 else 0.0)
        return SHELL_O + SHELL_R @ Vector((Ro * sc * math.sin(a) * math.cos(th), Ro * sc * math.sin(a) * math.sin(th), z))

    V, F, S, FSx = [], [], [], []
    for layer, (sc, hs) in enumerate(((1.0, 1.0), (0.90, 0.86))):
        top = len(V)
        V.append(SHELL_O + SHELL_R @ Vector((0, 0, Hd * hs)))
        rings = []
        for t in ts:
            rings.append([len(V) + j for j in range(K)])
            V += [P(t, j, sc, hs) for j in range(K)]
        flip = layer == 1
        slot_of = (lambda i: "Seam" if i == 2 else "ShellOut") if not flip else (lambda i: "ShellIn")
        for j in range(K):
            f = (top, rings[0][j], rings[0][(j + 1) % K])
            F.append(tuple(reversed(f)) if flip else f)
            S.append("ShellOut" if not flip else "ShellIn")
            FSx.append(1.06)
        for i in range(len(ts) - 1):
            for j in range(K):
                j2 = (j + 1) % K
                f = (rings[i][j], rings[i + 1][j], rings[i + 1][j2], rings[i][j2])
                F.append(tuple(reversed(f)) if flip else f)
                S.append(slot_of(i))
                FSx.append(1.0 if i < 2 else 0.95)
        if layer == 0:
            outer_rim = rings[-1]
        else:
            inner_rim = rings[-1]
    for j in range(K):
        j2 = (j + 1) % K
        F.append((outer_rim[j], inner_rim[j], inner_rim[j2], outer_rim[j2]))
        S.append("Seam")
        FSx.append(1.0)
    geo.add(V, F, S, "Shell", smooth=False, fs=FSx)


def body_slot(n, w):
    if n.y < -0.40 and -0.75 < n.z < 0.35:
        return "Belly", (0.84 if (w.z % 0.30) < 0.045 else (1.04 if n.z > 0.2 else 1.0))
    return "Skin", (1.05 if n.z > 0.6 else 1.0)


def tail_slot(c, n):
    return ("Belly", 0.95) if n.z < -0.55 * n.length else ("Skin", 1.0)


def build():
    geo = Geo()
    ellipsoid(geo, (0, 0.05, 0.84), (0.76, 0.68, 0.80), "Skin", "Body", 26, 16, slot_fn=body_slot)
    build_head(geo)
    shell(geo)
    for s in SIDE:
        hb, hd = HORN[s]
        cone(geo, hb - hd * 0.08, hd, 0.40, 0.11, "SlateLight", "Horn.%s" % ("L" if s > 0 else "R"), sides=8)
        along(geo, SH[s], EL[s], 0.17, "Skin", "UpperArm.%s" % ("L" if s > 0 else "R"))
        hand(geo, EL[s], TP[s], s, "Hand.%s" % ("L" if s > 0 else "R"))
        lb = "Leg.%s" % ("L" if s > 0 else "R")
        ellipsoid(geo, (0.40 * s, -0.12, 0.20), (0.28, 0.34, 0.22), "Skin", lb, 14, 9,
                  slot_fn=lambda n, w: ("Skin", 1.05 if n.z > 0.5 else 1.0))
        for k in (-1, 0, 1):
            cone(geo, (0.40 * s + k * 0.12, -0.38, 0.11), (k * 0.2, -1, -0.15), 0.12, 0.055, "Claw", lb, sides=5)
        # shoulder plates with moss (the Kaiju kit) and the lime crystals rising behind the shoulders
        boulder(geo, (0.62 * s, 0.14, 1.40), (0.24, 0.22, 0.15), "Body", up=(0.45 * s, 0.1, 0.9))
        boulder(geo, (0.40 * s, 0.55, 0.85), (0.26, 0.22, 0.18), "Body", up=(0.3 * s, 0.9, 0.3))
        crystal(geo, (0.52 * s, 0.42, 1.24), (0.80 * s, 0.28, 0.56), 0.88, 0.13, "Body", spin=0.3)
        crystal(geo, (0.34 * s, 0.56, 1.30), (0.35 * s, 0.35, 0.88), 0.62, 0.11, "Body", spin=0.1)
    boulder(geo, (0.0, 0.68, 1.15), (0.30, 0.20, 0.24), "Body", up=(0, 0.9, 0.45))
    # crest of three lime crystals behind the head: the adult's crown, baby-sized (family read in the HUD bust)
    crystal(geo, H + Vector((0.0, 0.60, 0.42)), (0.0, 0.50, 0.87), 0.92, 0.12, "Head", spin=0.2)
    for s in SIDE:
        crystal(geo, H + Vector((0.34 * s, 0.56, 0.30)), (0.45 * s, 0.50, 0.74), 0.70, 0.10, "Head", spin=0.4)
    crystal(geo, (0.0, 0.70, 1.00), (0.0, 0.75, 0.66), 0.50, 0.11, "Body")
    loft(geo, TAIL, TAIL_R, tail_slot, "Tail")
    boulder(geo, (0.03, 1.00, 0.52), (0.16, 0.18, 0.10), "Tail", up=(0, 0.2, 1))
    crystal(geo, TAIL[-1], (0.3, 0.4, 0.85), 0.30, 0.08, "Tail")
    crystal(geo, TAIL[-2], (0.05, 0.55, 0.85), 0.24, 0.07, "Tail", spin=0.5)
    return geo


BONES = [  # name, head, tail, parent, deform
    ("Root", (0, 0, 0), (0, 0.4, 0), None, False),
    ("Body", (0, 0, 0.35), (0, 0, 1.35), "Root", True),
    ("Head", tuple(NECK), (0, 0, 2.5), "Body", True),
    ("Shell", tuple(SHELL_O), tuple(SHELL_O + SHELL_R @ Vector((0, 0, 0.45))), "Head", True),
    ("Horn.L", tuple(HORN[1][0]), tuple(HORN[1][0] + HORN[1][1] * 0.34), "Head", True),
    ("Horn.R", tuple(HORN[-1][0]), tuple(HORN[-1][0] + HORN[-1][1] * 0.34), "Head", True),
    ("UpperArm.L", tuple(SH[1]), tuple(EL[1]), "Body", True),
    ("Hand.L", tuple(EL[1]), tuple(TP[1]), "UpperArm.L", True),
    ("UpperArm.R", tuple(SH[-1]), tuple(EL[-1]), "Body", True),
    ("Hand.R", tuple(EL[-1]), tuple(TP[-1]), "UpperArm.R", True),
    ("Leg.L", (0.40, -0.12, 0.50), (0.40, -0.12, 0.05), "Root", True),
    ("Leg.R", (-0.40, -0.12, 0.50), (-0.40, -0.12, 0.05), "Root", True),
    ("Tail", TAIL[0], TAIL[-1], "Body", True),
]


# ------------------------------------------------------------------------------------------------ mesh, rig, material
def make_fbx_material(face_path):
    m = bpy.data.materials.new("CM_Toon")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    uv = nt.nodes.new("ShaderNodeUVMap")
    uv.uv_map = "UVMap"
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.name = "FACE"
    tex.image = bpy.data.images.load(face_path)
    tex.extension = "CLIP"
    nt.links.new(uv.outputs[0], tex.inputs[0])
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    return m


def to_mesh(name, geo, s, mat, design_V):
    V = [v * s for v in geo.V]
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in V], [], [list(f) for f in geo.F])
    me.validate(clean_customdata=False)
    assert len(me.polygons) == len(geo.F), ("validate dropped faces", len(me.polygons), len(geo.F))
    me.polygons.foreach_set("use_smooth", geo.smooth)
    bm = bmesh.new()
    bm.from_mesh(me)
    seen = set()
    for f0 in bm.faces:
        if f0.index in seen:
            continue
        isl, stack = [], [f0]
        seen.add(f0.index)
        while stack:
            f = stack.pop()
            isl.append(f)
            for e in f.edges:
                for g in e.link_faces:
                    if g.index not in seen:
                        seen.add(g.index)
                        stack.append(g)
        bmesh.ops.recalc_face_normals(bm, faces=isl)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mat)
    col = me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    me.color_attributes.active_color = col
    me.color_attributes.render_color_index = me.color_attributes.active_color_index
    uvl = me.uv_layers.new(name="UVMap")
    vals = []
    for poly in me.polygons:
        r, g, b, a = slot_rgba(geo.slot[poly.index])
        k = geo.fs[poly.index]
        if a < 0.5:
            nz = poly.normal.z
            k *= 1.06 if nz > 0.6 else (0.92 if nz < -0.6 else 1.0)
        for li in poly.loop_indices:
            vals.append((min(1, r * k), min(1, g * k), min(1, b * k), a))
            if geo.decal[poly.index]:
                p = design_V[me.loops[li].vertex_index] - H
                uvl.data[li].uv = ((p.x / EXT + 1) / 2, (p.z / EXT + 1) / 2)
            else:
                uvl.data[li].uv = (0.002, 0.002)
    for li, v in enumerate(vals):
        col.data[li].color_srgb = v
    return me


def make_rig(s):
    ad = bpy.data.armatures.new("MascotRigData")
    arm = bpy.data.objects.new("MascotRig", ad)
    COLL.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for bn, h, t, parent, deform in BONES:
        eb = ad.edit_bones.new(bn)
        eb.head, eb.tail, eb.roll = Vector(h) * s, Vector(t) * s, 0.0
        eb.use_deform = deform
        if parent:
            eb.parent = ad.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    arm.select_set(False)
    return arm


def attach(ob, rig, bones):
    groups = {}
    for i, bn in enumerate(bones):
        if bn not in groups:
            groups[bn] = ob.vertex_groups.new(name=bn)
        groups[bn].add([i], 1.0, "REPLACE")
    ob.parent = rig
    mod = ob.modifiers.new("Armature", "ARMATURE")
    mod.object = rig


geo = build()
zmin = min(v.z for v in geo.V)
zmax = max(v.z for v in geo.V)
assert abs(zmin) < 0.05, zmin
S = TARGET_H / (zmax - zmin)
design_V = list(geo.V)
shutil.copyfile(os.path.join(FACES, "face-neutral.png"), os.path.join(ART, "Mascot_Face.png"))
MAT = make_fbx_material(os.path.join(ART, "Mascot_Face.png"))
me = to_mesh("MascotMesh", geo, S, MAT, design_V)
MESH = bpy.data.objects.new("MascotMesh", me)
COLL.objects.link(MESH)
RIG = make_rig(S)
attach(MESH, RIG, geo.bone)
MESH["cm_outline_px"] = 3.0
TRIS = sum(len(p.vertices) - 2 for p in me.polygons)
print("MASCOT tris=%d verts=%d bones=%d height=%.3f scale=%.4f" % (TRIS, len(me.vertices), len(RIG.data.bones),
                                                                 (zmax - zmin) * S, S))
assert TRIS <= TRI_CAP, TRIS

# export (rest pose, Kaiju settings)
for o in bpy.data.objects:
    o.select_set(False)
MESH.select_set(True)
RIG.select_set(True)
bpy.context.view_layer.objects.active = RIG
FBX = os.path.join(ART, "Mascot_Hatch.fbx")
bpy.ops.export_scene.fbx(
    filepath=FBX, use_selection=True, object_types={"ARMATURE", "MESH"},
    global_scale=1.0, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z", axis_up="Y", bake_space_transform=False,
    use_mesh_modifiers=False, mesh_smooth_type="FACE", use_tspace=False, colors_type="SRGB",
    prioritize_active_color=True, add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
    use_armature_deform_only=False, bake_anim=False, path_mode="STRIP", embed_textures=False)
print("EXPORTED", FBX)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "Mascot.blend"), compress=True)
print("SAVED blend")

# round trip of the FBX in a scratch scene would reset this session; measured instead from the exported file size
METRICS = {"tris": TRIS, "verts": len(me.vertices), "bones": len(RIG.data.bones), "height_m": round((zmax - zmin) * S, 3),
           "dims_m": [round(x, 3) for x in MESH.dimensions], "materials": [m.name for m in me.materials],
           "color_attributes": [a.name for a in me.color_attributes], "uv_layers": [u.name for u in me.uv_layers],
           "alpha_codes": sorted({round(slot_rgba(s)[3], 2) for s in set(geo.slot)}), "fbx_bytes": os.path.getsize(FBX)}

# ------------------------------------------------------------------------------------------------ render setup
scene.render.engine = "BLENDER_EEVEE"
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.render.film_transparent = True
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "None"
scene.render.resolution_percentage = 100
try:
    scene.eevee.taa_render_samples = 16
except AttributeError:
    pass
world = bpy.data.worlds.new("Dark")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.0
scene.world = world
NAVY = (*lin(VOID_NAVY), 1.0)


def node(nt, t, **kw):
    n = nt.nodes.new(t)
    for k, v in kw.items():
        setattr(n, k, v)
    return n


def math_node(nt, op, a, b=None):
    m = node(nt, "ShaderNodeMath", operation=op)
    for i, v in enumerate((a, b)):
        if v is None:
            continue
        if isinstance(v, (int, float)):
            m.inputs[i].default_value = v
        else:
            nt.links.new(v, m.inputs[i])
    return m.outputs[0]


def band_factor(nt, L_node):
    """Kaiju render_kaiju.py three-band ramp: lit 1 / mid 0.84 / shadow 0.66, cast shadows into the shadow band."""
    g = node(nt, "ShaderNodeNewGeometry")
    dot = node(nt, "ShaderNodeVectorMath", operation="DOT_PRODUCT")
    nt.links.new(g.outputs["Normal"], dot.inputs[0])
    nt.links.new(L_node.outputs[0], dot.inputs[1])
    ndl = dot.outputs["Value"]
    dif = node(nt, "ShaderNodeBsdfDiffuse")
    s2r = node(nt, "ShaderNodeShaderToRGB")
    nt.links.new(dif.outputs[0], s2r.inputs[0])
    bw = node(nt, "ShaderNodeRGBToBW")
    nt.links.new(s2r.outputs["Color"], bw.inputs[0])
    ratio = math_node(nt, "DIVIDE", bw.outputs[0], math_node(nt, "MAXIMUM", ndl, 0.02))
    inshadow = math_node(nt, "LESS_THAN", ratio, 0.5)
    v = math_node(nt, "SUBTRACT", ndl, math_node(nt, "MULTIPLY", inshadow, 2.0))
    is_lit = math_node(nt, "GREATER_THAN", v, 0.35)
    is_mid = math_node(nt, "MULTIPLY", math_node(nt, "GREATER_THAN", v, -0.05), math_node(nt, "SUBTRACT", 1.0, is_lit))
    is_sh = math_node(nt, "SUBTRACT", math_node(nt, "SUBTRACT", 1.0, is_lit), is_mid)
    f = math_node(nt, "ADD", math_node(nt, "ADD", is_lit, math_node(nt, "MULTIPLY", is_mid, 0.84)),
                  math_node(nt, "MULTIPLY", is_sh, 0.66))
    return f, is_sh


LIGHT_NODES = []


def toon_material(name, image=None, team=GLOW, ref=GLOW, col_name="Col"):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    col = node(nt, "ShaderNodeVertexColor", layer_name=col_name)
    L = node(nt, "ShaderNodeCombineXYZ", name="LIGHT")
    LIGHT_NODES.append(L)
    f, is_sh = band_factor(nt, L)
    base = col.outputs["Color"]
    if image is not None:
        uv = node(nt, "ShaderNodeUVMap", uv_map="UVMap")
        tex = node(nt, "ShaderNodeTexImage", image=image, interpolation="Linear", extension="CLIP", name="FACE")
        nt.links.new(uv.outputs[0], tex.inputs[0])
        fm = node(nt, "ShaderNodeMix", data_type="RGBA", blend_type="MIX")
        nt.links.new(tex.outputs["Alpha"], fm.inputs["Factor"])
        nt.links.new(base, fm.inputs[6])
        nt.links.new(tex.outputs["Color"], fm.inputs[7])
        base = fm.outputs[2]
    mul = node(nt, "ShaderNodeMix", data_type="RGBA", blend_type="MULTIPLY")
    mul.inputs["Factor"].default_value = 1.0
    nt.links.new(base, mul.inputs[6])
    fc = node(nt, "ShaderNodeCombineColor")
    for i in range(3):
        nt.links.new(f, fc.inputs[i])
    nt.links.new(fc.outputs[0], mul.inputs[7])
    navy = node(nt, "ShaderNodeMix", data_type="RGBA", blend_type="MIX")
    nt.links.new(math_node(nt, "MULTIPLY", is_sh, 0.12), navy.inputs["Factor"])
    nt.links.new(mul.outputs[2], navy.inputs[6])
    navy.inputs[7].default_value = NAVY
    sep = node(nt, "ShaderNodeSeparateColor")
    nt.links.new(col.outputs["Color"], sep.inputs[0])
    mx = math_node(nt, "MAXIMUM", math_node(nt, "MAXIMUM", sep.outputs[0], sep.outputs[1]), sep.outputs[2])
    k = math_node(nt, "DIVIDE", mx, max(lin(ref)))
    tg = node(nt, "ShaderNodeMix", data_type="RGBA", blend_type="MULTIPLY")
    tg.inputs["Factor"].default_value = 1.0
    tg.inputs[6].default_value = (*lin(team), 1.0)
    kc = node(nt, "ShaderNodeCombineColor")
    for i in range(3):
        nt.links.new(k, kc.inputs[i])
    nt.links.new(kc.outputs[0], tg.inputs[7])
    is_team = math_node(nt, "GREATER_THAN", col.outputs["Alpha"], 0.5)
    out_mix = node(nt, "ShaderNodeMix", data_type="RGBA", blend_type="MIX")
    nt.links.new(is_team, out_mix.inputs["Factor"])
    nt.links.new(navy.outputs[2], out_mix.inputs[6])
    nt.links.new(tg.outputs[2], out_mix.inputs[7])
    em = node(nt, "ShaderNodeEmission")
    nt.links.new(out_mix.outputs[2], em.inputs[0])
    o = node(nt, "ShaderNodeOutputMaterial")
    nt.links.new(em.outputs[0], o.inputs[0])
    return m


def flat_material(name, hexs):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    em = node(nt, "ShaderNodeEmission")
    em.inputs[0].default_value = (*lin(hexs), 1.0)
    o = node(nt, "ShaderNodeOutputMaterial")
    nt.links.new(em.outputs[0], o.inputs[0])
    return m


FACE_IMG = {}
for fn in sorted(os.listdir(FACES)):
    if fn.startswith("face-") and fn.endswith(".png"):
        FACE_IMG[fn[5:-4]] = bpy.data.images.load(os.path.join(FACES, fn))
TOON = toon_material("CM_Toon_Preview", FACE_IMG["neutral"])
me.materials[0] = TOON
OUTLINE = flat_material("Outline", VOID_NAVY)
OUTLINE.use_backface_culling = True
SILHOUETTE = flat_material("RefSilhouette", "#6E7480")
HULLS = []


def make_hull(ob):
    m2 = ob.data.copy()
    bm = bmesh.new()
    bm.from_mesh(m2)
    bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
    bm.to_mesh(m2)
    bm.free()
    m2.materials.clear()
    m2.materials.append(OUTLINE)
    h = bpy.data.objects.new(ob.name + "_Hull", m2)
    COLL.objects.link(h)
    h.parent = ob.parent
    h.matrix_parent_inverse = ob.matrix_parent_inverse.copy()
    for vg in ob.vertex_groups:
        h.vertex_groups.new(name=vg.name)
    for mod in ob.modifiers:
        if mod.type == "ARMATURE":
            a = h.modifiers.new("Armature", "ARMATURE")
            a.object = mod.object
    d = h.modifiers.new("Push", "DISPLACE")
    d.direction = "NORMAL"
    d.mid_level = 0.0
    d.strength = -0.01
    h.visible_shadow = False
    HULLS.append(h)
    ob["hull"] = h.name
    return h


def hull_width(px, ppm):
    for h in HULLS:
        h.modifiers["Push"].strength = -px / ppm


make_hull(MESH)

# props that only appear in portraits (never exported): sweat drop on the head, peek claws
sg = Geo()
sp = H + Vector((0.98, -0.30, 0.42))
ellipsoid(sg, sp, (0.10, 0.08, 0.11), "Seam", "Head", 10, 7)
cone(sg, sp + Vector((0, 0, 0.06)), (0.15, 0, 1), 0.17, 0.085, "Seam", "Head", sides=8, round_tip=0.2)
ellipsoid(sg, sp + Vector((-0.035, -0.07, 0.03)), (0.03, 0.02, 0.035), "ShellIn", "Head", 6, 4)
SWEAT = bpy.data.objects.new("SweatProp", to_mesh("SweatProp", sg, S, TOON, list(sg.V)))
COLL.objects.link(SWEAT)
attach(SWEAT, RIG, sg.bone)
make_hull(SWEAT)
pg = Geo()
for s in SIDE:
    hand(pg, Vector((0.55 * s, 0.20, 0.30)), Vector((0.55 * s, -0.10, -0.05)), s, "Root")
PEEK = bpy.data.objects.new("PeekClaws", to_mesh("PeekClaws", pg, S, TOON, list(pg.V)))
COLL.objects.link(PEEK)
make_hull(PEEK)

# key light
sun_data = bpy.data.lights.new("Key", "SUN")
sun_data.energy = math.pi
sun_data.angle = 0.0
sun_data.use_shadow = True
sun = bpy.data.objects.new("Key", sun_data)
COLL.objects.link(sun)
cam_data = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_data)
COLL.objects.link(cam)
scene.camera = cam


def set_light(src):
    src = Vector(src).normalized()
    sun.rotation_mode = "QUATERNION"
    sun.rotation_quaternion = (-src).to_track_quat("-Z", "Y")
    for nd in LIGHT_NODES:
        for i in range(3):
            nd.inputs[i].default_value = src[i]


def world_pts(ob, every=1, bones=None):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    m = ev.to_mesh()
    if bones is not None:
        names = {g.index: g.name for g in ob.vertex_groups}
        pts = [ob.matrix_world @ v.co for v in m.vertices if v.groups and names[v.groups[0].group] in bones]
    else:
        pts = [ob.matrix_world @ v.co for v in list(m.vertices)[::every]]
    ev.to_mesh_clear()
    return pts


def cam_basis(az, el):
    az, el = math.radians(az), math.radians(el)
    d = Vector((-math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el)))   # toward the camera
    rot = (-d).to_track_quat("-Z", "Y")
    R = rot.to_matrix()
    return d, rot, R.col[0], R.col[1]


def place_cam(az, el, cx, cy, span, resx, resy):
    d, rot, right, up = cam_basis(az, el)
    cam_data.type = "ORTHO"
    cam_data.clip_start, cam_data.clip_end = 0.05, 200.0
    cam_data.sensor_fit = "HORIZONTAL" if resx >= resy else "VERTICAL"
    cam.location = right * cx + up * cy + d * 40.0
    cam.rotation_mode = "QUATERNION"
    cam.rotation_quaternion = rot
    cam_data.ortho_scale = span
    scene.render.resolution_x, scene.render.resolution_y = resx, resy
    set_light(-(rot.to_matrix() @ Vector((0.55, -0.62, -0.56))))     # key from the frame's upper left (as the Kaiju)
    return max(resx, resy) / span


def apply_pose(spec):
    """spec: bone -> {"rot": (x, y, z) degrees about character axes, "dir": target direction, "loc": design offset}."""
    deltas = {}
    for pb in RIG.pose.bones:
        b = pb.bone
        P = deltas[b.parent.name] if b.parent else Matrix.Identity(4)
        h = P @ b.head_local
        sp = spec.get(b.name, {})
        Q = Matrix.Identity(4)
        if "dir" in sp:
            cur = (P.to_3x3() @ (b.tail_local - b.head_local)).normalized()
            Q = cur.rotation_difference(Vector(sp["dir"]).normalized()).to_matrix().to_4x4()
        if "rot" in sp:
            Q = Euler([math.radians(a) for a in sp["rot"]], "XYZ").to_matrix().to_4x4() @ Q
        t = Vector(sp.get("loc", (0, 0, 0))) * S
        D = Matrix.Translation(h + t) @ Q @ Matrix.Translation(-h) @ P
        deltas[b.name] = D
        pb.matrix = D @ b.matrix_local
        bpy.context.view_layer.update()


def mirror(spec):
    out = {}
    for bn, sp in spec.items():
        nb = bn.replace(".L", ".X").replace(".R", ".L").replace(".X", ".R")
        q = {}
        for k, v in sp.items():
            if k == "dir" or k == "loc":
                q[k] = (-v[0], v[1], v[2])
            elif k == "rot":
                q[k] = (v[0], -v[1], -v[2])
        out[nb] = q
    return out


def arms(l_up, l_hand, r_up=None, r_hand=None):
    r_up = r_up or (-l_up[0], l_up[1], l_up[2])
    r_hand = r_hand or (-l_hand[0], l_hand[1], l_hand[2])
    return {"UpperArm.L": {"dir": l_up}, "Hand.L": {"dir": l_hand}, "UpperArm.R": {"dir": r_up}, "Hand.R": {"dir": r_hand}}


REST_ARMS = arms((0.40, -0.25, -0.88), (0.15, -0.65, -0.70))
POINT = dict(arms((0.60, -0.70, 0.30), (0.70, -0.55, 0.42), (-0.40, -0.25, -0.88), (-0.15, -0.65, -0.70)))
POINT.update({"Head": {"rot": (-3, 6, 22)}})
POSES = {
    "neutral": dict(REST_ARMS, Head={"rot": (0, 5, 4)}),
    "happy": dict(arms((0.62, -0.25, 0.74), (0.30, -0.25, 0.92)), Head={"rot": (-7, 0, 0)}, Body={"loc": (0, 0, 0.04)}),
    "wince": dict(arms((0.45, -0.70, 0.30), (-0.35, -0.55, 0.70)), Head={"rot": (9, -6, -5), "loc": (0, 0, -0.12)},
                  Shell={"rot": (0, -8, 0)}, **{"Horn.L": {"rot": (0, 25, 0)}, "Horn.R": {"rot": (0, -25, 0)}}),
    "point": POINT,
    "point-left": mirror(POINT),
    "surprised": dict(arms((0.75, -0.45, -0.10), (0.45, -0.55, 0.70)), Head={"rot": (-8, 0, 0), "loc": (0, 0, 0.03)},
                      Shell={"loc": (0.04, 0.0, 0.17), "rot": (0, 8, 0)},
                      **{"Horn.L": {"dir": (0.25, -0.1, 1.0)}, "Horn.R": {"dir": (-0.25, -0.1, 1.0)}}),
    "thinking": dict(arms((0.15, -0.45, -0.88), (-0.65, -0.62, -0.20), (0.05, -0.55, -0.40), (0.38, -0.40, 0.85)),
                     Head={"rot": (-5, -9, -8)}),
    "laughing": dict(arms((0.32, -0.42, -0.85), (-0.55, -0.72, -0.30)), Head={"rot": (-13, 7, 0)}, Body={"rot": (-5, 0, 0)}),
    "proud": dict(arms((0.86, 0.15, -0.48), (-0.55, -0.15, -0.82)), Head={"rot": (-11, 0, 5)}, Body={"rot": (-4, 0, 0)}),
    "sleepy": dict(arms((0.20, -0.05, -1.0), (0.10, -0.15, -1.0)), Head={"rot": (11, -11, 0), "loc": (0, 0, -0.05)},
                   Shell={"rot": (6, -10, 0)}),
    "peek": dict(REST_ARMS, Head={"rot": (0, 0, 12)}),
}
EXPR = ["neutral", "happy", "wince", "point", "point-left", "surprised", "thinking", "laughing", "proud", "sleepy", "peek"]

TOON_FACE = TOON.node_tree.nodes["FACE"]


def show(objs):
    keep = set(objs)
    for o in bpy.data.objects:
        if o.type == "MESH":
            o.hide_render = o not in keep


# ------------------------------------------------------------------------------------------------ portraits
P_AZ, P_EL, P_YAW = 0.0, 5.0, 14.0
RES = 512                                    # the @4x size; compose_mascot.py makes @2x
RIG.rotation_euler = (0, 0, math.radians(P_YAW))
apply_pose(POSES["neutral"])
d, rot, right, up = cam_basis(P_AZ, P_EL)
hp = world_pts(MESH, bones={"Head", "Shell", "Horn.L", "Horn.R"})
headonly = world_pts(MESH, bones={"Head"})
xs = [p.dot(right) for p in headonly]
tops = [p.dot(up) for p in hp]
head_w = max(xs) - min(xs)
SPAN = head_w / 0.76
CX = (max(xs) + min(xs)) / 2 - 0.025 * head_w / 0.76
CY = max(tops) + SPAN * 0.045 - SPAN / 2
meta = {"portrait": {"span_m": SPAN, "head_w_m": head_w, "head_fill": head_w / SPAN, "yaw": P_YAW, "el": P_EL}}
YAW_OVR = {"point": 0.0, "point-left": 0.0}    # body square to the camera so the pointing arm is in front
for stem in EXPR:
    RIG.location = (0, 0, 0)
    RIG.rotation_euler = (0, 0, math.radians(YAW_OVR.get(stem, P_YAW)))
    apply_pose(POSES[stem])
    TOON_FACE.image = FACE_IMG[stem]
    objs = [MESH, bpy.data.objects[MESH["hull"]]]
    if stem == "wince":
        objs += [SWEAT, bpy.data.objects[SWEAT["hull"]]]
    cy = CY
    if stem == "peek":
        # the image's bottom row is the panel edge: lower the mascot so the edge cuts just under the eyes, and hook two
        # claws over it in front
        edge_world = H * S + Vector((0, 0, -0.27 * S))
        hc = RIG.matrix_world @ (RIG.pose.bones["Head"].matrix @ RIG.data.bones["Head"].matrix_local.inverted()) @ edge_world
        bottom = CY - SPAN / 2
        dz = (bottom - hc.dot(up)) / up.z
        RIG.location = (0, 0, dz)
        bpy.context.view_layer.update()
        base = right * CX + up * (bottom + 0.035 * SPAN) + d * 2.0
        R = Matrix((right, -d, up)).transposed()       # claw model: x right, -y toward the viewer, z up
        PEEK.matrix_world = Matrix.Translation(base) @ R.to_4x4()
        bpy.data.objects[PEEK["hull"]].matrix_world = PEEK.matrix_world.copy()
        objs += [PEEK, bpy.data.objects[PEEK["hull"]]]
    show(objs)
    ppm = place_cam(P_AZ, P_EL, CX, cy, SPAN, RES, RES)
    hull_width(6.0, ppm)                     # 1.5 px at the 1x (128 px) medallion; the outer line goes to 3 px in compose
    bpy.context.view_layer.update()
    scene.render.filepath = os.path.join(OUT, "portrait-%s.png" % stem)
    bpy.ops.render.render(write_still=True)
    print("RENDERED portrait", stem)
RIG.location = (0, 0, 0)
meta["portrait"]["ppm"] = ppm

# ------------------------------------------------------------------------------------------------ turnaround (full body)
apply_pose(POSES["neutral"])
TOON_FACE.image = FACE_IMG["neutral"]
show([MESH, bpy.data.objects[MESH["hull"]]])
TURN = [0, 45, 90, 135, 180]
allx, ally = [], []
d, rot, right, up = cam_basis(0, 5)
for yaw in TURN:
    RIG.rotation_euler = (0, 0, math.radians(yaw))
    bpy.context.view_layer.update()
    for p in world_pts(MESH, every=2):
        allx.append(p.dot(right))
        ally.append(p.dot(up))
span_y = (max(ally) - min(ally)) * 1.10
span_x = (max(allx) - min(allx)) * 1.06
TW, TH = 520, 640
span = max(span_y, span_x * TH / TW)          # VERTICAL sensor fit: ortho_scale is the frame height
for yaw in TURN:
    RIG.rotation_euler = (0, 0, math.radians(yaw))
    bpy.context.view_layer.update()
    ppm = place_cam(0, 5, 0.0, (max(ally) + min(ally)) / 2, span, TW, TH)
    hull_width(2.5, ppm)
    scene.render.filepath = os.path.join(OUT, "turn-%03d.png" % yaw)
    bpy.ops.render.render(write_still=True)
    print("RENDERED turn", yaw)
meta["turn"] = {"ppm": ppm, "height_m": METRICS["height_m"]}

# ------------------------------------------------------------------------------------------------ line-up with the adult Kaiju
adult = None
try:
    with bpy.data.libraries.load(KAIJU_BLEND, link=False) as (src, dst):
        dst.objects = [n for n in src.objects if n in ("Adult_Mesh", "Adult_Rig")]
    for o in dst.objects:
        COLL.objects.link(o)
        if o.type == "MESH":
            adult = o
        else:
            o.location = (0, 0, 0)
            o.animation_data_clear()
            for pb in o.pose.bones:
                pb.matrix_basis = Matrix.Identity(4)
    kimg = bpy.data.images.load(os.path.join(os.path.dirname(os.path.dirname(KAIJU_BLEND)), "Kaiju_Face.png"))
    cname = adult.data.color_attributes[0].name if adult.data.color_attributes else "Col"
    AMAT = toon_material("CM_Toon_Adult", kimg, team=GLOW, ref="#13F1F4", col_name=cname)
    adult.data.materials.clear()
    adult.data.materials.append(AMAT)
    make_hull(adult)
except Exception as e:   # noqa: BLE001 - the line-up is optional; the report says if it is missing
    print("ADULT LOAD FAILED", repr(e))
    adult = None
warrior = []
try:
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=WARRIOR_FBX)
    warrior = [o for o in bpy.data.objects if o not in before]
    for o in warrior:
        if o.type == "MESH":
            o.data.materials.clear()
            o.data.materials.append(SILHOUETTE)
except Exception as e:   # noqa: BLE001
    print("WARRIOR LOAD FAILED", repr(e))
    warrior = []
if adult is not None:
    bpy.context.view_layer.update()
    ap = world_pts(adult, every=3)
    ax0, ax1 = min(p.x for p in ap), max(p.x for p in ap)
    ah = max(p.z for p in ap)
    RIG.rotation_euler = (0, 0, math.radians(-12))
    RIG.location = (ax1 + 0.9, -1.2, 0)
    wroot = [o for o in warrior if o.parent is None]
    for o in wroot:
        o.location = (ax0 - 1.1, -0.6, 0)
    bpy.context.view_layer.update()
    vis = [adult, bpy.data.objects[adult["hull"]], MESH, bpy.data.objects[MESH["hull"]]] + [o for o in warrior if o.type == "MESH"]
    show(vis)
    pts = ap + world_pts(MESH, every=2) + [p for o in warrior if o.type == "MESH" for p in world_pts(o, every=5)]
    d, rot, right, up = cam_basis(18, 8)
    xs = [p.dot(right) for p in pts]
    ys = [p.dot(up) for p in pts]
    LW, LH = 1600, 1000
    span = max((max(xs) - min(xs)) * 1.08, (max(ys) - min(ys)) * 1.12 * LW / LH)
    ppm = place_cam(18, 8, (max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2, span, LW, LH)
    hull_width(2.5, ppm)
    scene.render.filepath = os.path.join(OUT, "lineup.png")
    bpy.ops.render.render(write_still=True)
    wh = 0.0
    for o in warrior:
        if o.type == "MESH":
            wh = max(wh, max(p.z for p in world_pts(o, every=5)))
    meta["lineup"] = {"adult_h": round(ah, 3), "hatch_h": METRICS["height_m"], "warrior_h": round(wh, 3),
                      "ratio_adult_to_hatch": round(ah / METRICS["height_m"], 2)}
    print("RENDERED lineup", meta["lineup"])
METRICS["renders"] = meta
json.dump(METRICS, open(os.path.join(OUT, "build-metrics.json"), "w"), indent=1)
json.dump(METRICS, open(os.path.join(HERE, "metrics.json"), "w"), indent=1)
print("DONE")
