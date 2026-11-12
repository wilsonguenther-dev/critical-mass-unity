"""Build the Critical Mass Kaiju (egg, hatchling, adult) from scratch, headless.

    /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
        --python-exit-code 1 --python "Assets/_Game/Art/Kaiju/_source~/build_kaiju.py"

Writes, next to this script, Kaiju.blend (source), and one level up Kaiju_Egg.fbx, Kaiju_Hatchling.fbx,
Kaiju_Adult.fbx (ARCHITECTURE.md section 6, panel revision P-08). Deterministic: fixed seeds, no time input.

Reads: kaiju_palette.py (this folder). Reference: bible pp 10-11 (crops in ~/critical-mass/loop/parity/ref/1[01]-*).
Reused from the browser repo's CM-69 script (critical-mass-loop af23782 scripts/sculpt/kaiju_adult.py, read-only):
the metaball -> voxel remesh -> decimate body route, the surface ray-cast kit-bashing of plates and crystals, and the
"rotate about an armature-space axis" keying helper. Everything else (proportions to p11, material slots instead of
vertex colours, plate tiling, face, rig, clips, FBX) is new.

Conventions: Blender Z up, the creature faces -Y, 1 unit = 1 m. Feet on z = 0, origin between the feet.
"""
import json
import math
import os
import random
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import kaiju_palette as PAL  # noqa: E402
import kaiju_face as FACE  # noqa: E402

ART = os.path.dirname(HERE)
FPS = 24
SLOT = {s: i for i, s in enumerate(PAL.SLOTS)}
ADULT_HEIGHT = 5.0                  # Render.KaijuHeight in the ported config (GameConfigTests: 5.0), crest included
HATCH_HEIGHT = 0.447 * ADULT_HEIGHT  # D-058, measured on the sheet
EGG_HEIGHT = 0.34 * ADULT_HEIGHT     # sheet: egg ~2.9x the figure, adult ~8.6x the same figure
OUTLINE_WIDTH = {"Egg": 0.018, "Hatchling": 0.022, "Adult": 0.035}  # metres, per asset (U-008); stored as a property
TRI_CAP = {"Egg": 4000, "Hatchling": 10000, "Adult": 15000}
# Silhouette tunables fitted against the bible crops with iou_map.py (KJ_TUNE='{"sh_x": 1.05}' overrides one run;
# the committed defaults below are the fitted values, so a plain run rebuilds the committed asset).
TUNE = {"sh_x": 1.12, "fist_x": 1.68, "fist_r": 0.64, "crown_k": 1.05, "fan_k": 1.2, "head_dy": -0.25, "head_dz": -0.2,
        "hip_z": 1.38, "thigh_x": 0.68, "fist_y": -0.8, "tail_k": 0.6, "arm_k": 1.38, "cry_k": 1.12, "rock_k": 1.18, "facets": 11, "crack_k": 1.42, "rim_k": 2.2, "moss_k": 0.6, "rim_d": 0.09}
TUNE.update(json.loads(os.environ.get("KJ_TUNE", "{}")))


def lin(hexs):
    h = hexs.lstrip("#")
    out = []
    for i in (0, 2, 4):
        c = int(h[i:i + 2], 16) / 255.0
        out.append(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4)
    return tuple(out)


# ------------------------------------------------------------------------------------------------ scene
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = FPS
scene.unit_settings.system = "METRIC"
scene.unit_settings.scale_length = 1.0
COLL = scene.collection


def make_material():
    """The one CM_Toon material every Kaiju mesh carries (ART-DIRECTION 3.4). Colour is in the vertex colour layer
    "Col" (RGB lit colour, ALPHA mask code); the Unity CM/Toon shader reads it. render_kaiju.py swaps in a node tree
    that previews the three-band ramp; the FBX only needs the name."""
    m = bpy.data.materials.new(PAL.MATERIAL)
    m.diffuse_color = (0.5, 0.5, 0.5, 1.0)
    m.roughness = 1.0
    m.metallic = 0.0
    # the painted face decal (kaiju_face.py) rides on the material as its base-colour texture, so the FBX carries the
    # Kaiju_Face.png reference (as the Warrior's Warrior_Face.png); vertex colour stays the colour source
    m.use_nodes = True
    nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    uv = nt.nodes.new("ShaderNodeUVMap")
    uv.uv_map = "UVMap"
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.name = "FACE"
    tex.image = FACE.make_image(bpy, "Kaiju_Face", os.path.join(ART, "Kaiju_Face.png"))
    tex.extension = "CLIP"
    nt.links.new(uv.outputs[0], tex.inputs[0])
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    return m


MAT = make_material()
CHAMFER = [0.04]          # current chamfer in DESIGN units; set per stage from metres / expected scale


# ------------------------------------------------------------------------------------------------ geometry
class Geo:
    """Accumulates pieces: verts, faces, per-face slot, per-vertex rigid bone (None = skin by distance)."""

    def __init__(self):
        self.V, self.F, self.M, self.SM, self.B, self.G, self.D = [], [], [], [], [], [], []
        self.FS, self.GR = [], []      # v3: per-face value factor (three-step boulder read); graded team faces

    def add(self, verts, faces, mats, bone=None, smooth=False, shade=None, fshade=None, graded=False):
        """shade: per-vertex value factor baked into the vertex colour (+6 % toward the top of a part, -10 % in
        crevices, ART-DIRECTION 3.5). None = 1.0."""
        o = len(self.V)
        self.V += [Vector(v) for v in verts]
        self.B += [bone] * len(verts)
        self.G += list(shade) if shade is not None else [1.0] * len(verts)
        for k, f in enumerate(faces):
            self.F.append(tuple(i + o for i in f))
            self.M.append(mats[k] if isinstance(mats, (list, tuple)) else mats)
            self.SM.append(smooth)
            self.D.append(False)
            self.FS.append(fshade[k] if fshade is not None else 1.0)
            self.GR.append(graded)


def frame(n, spin=0.0):
    z = Vector(n).normalized()
    x = Vector((1, 0, 0)) if abs(z.x) < 0.9 else Vector((0, 1, 0))
    y = z.cross(x).normalized()
    x = y.cross(z).normalized()
    m = Matrix((x, y, z)).transposed()
    return m @ Matrix.Rotation(spin, 3, "Z")


def hull(pts):
    bm = bmesh.new()
    for p in pts:
        bm.verts.new(p)
    res = bmesh.ops.convex_hull(bm, input=bm.verts[:])
    dead = list({g for g in res["geom_interior"] + res["geom_unused"] if isinstance(g, bmesh.types.BMVert)})
    if dead:
        bmesh.ops.delete(bm, geom=dead, context="VERTS")
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.verts.index_update()
    V = [v.co.copy() for v in bm.verts]
    F = [[v.index for v in f.verts] for f in bm.faces]
    N = [f.normal.copy() for f in bm.faces]
    bm.free()
    return V, F, N


def slab(geo, p, n, M, outline, thick, base_slot, rng, mossy=False, bone=None, sink=0.3, moss_dot=0.45,
         surf=None, slot_fn=None, flare=0.0, halo=True):
    """One plate: a separate ROUNDED boulder (v2; bible plates are pillowed stones, not flat slabs).
    outline: convex 2-D polygon (counter-clockwise) in the tangent frame M at p (normal n). Rings, bottom to top:
    footing (sunk into the body), a short wall (the crack wall: team-glow HALO when halo=True, so every crack reads as a
    bright core with a dimmer glow either side, ART-DIRECTION 2.3 "a bright rim instead"), then two shoulder rings
    inset toward the centre and an apex: a domed, smooth-shaded top that takes the three bands as a rounded stone.
    The first shoulder ring sits CHAMFER in from the wall, so every rim still catches a light line (3.5 bevels).
    surf(q) -> surface point for a tangent-plane point (conforms the footing to the body), or None.
    flare: the walls lean out by this much at the top, so on a curved body neighbouring plates still close up."""
    n = Vector(n).normalized()
    ch = CHAMFER[0]
    k = len(outline)
    cen2 = sum((Vector(q) for q in outline), Vector((0.0, 0.0))) / k
    rad = sum((Vector(q) - cen2).length for q in outline) / k

    def at(q2):
        w = p + M @ Vector((q2.x, q2.y, 0.0))
        return surf(w) if surf else w

    rows = []
    for q in outline:
        q = Vector(q)
        d = q - cen2
        dn = d.normalized() if d.length > 1e-6 else Vector((0.0, 0.0))
        out = M @ Vector((dn.x, dn.y, 0.0))
        base = at(q)
        ia = cen2 + d * max(0.55, 1.0 - (ch * 1.2) / max(1e-6, d.length))      # first shoulder: a chamfer in
        ib = cen2 + d * 0.5
        rows.append((base - n * (sink * thick),
                     base + n * (0.12 * thick) + out * (0.2 * flare),
                     at(ia) + n * (0.8 * thick) + out * (0.75 * flare),
                     at(ib) + n * (1.02 * thick) + out * (0.45 * flare)))
    apex = at(cen2) + n * (thick * (1.1 + 0.08 * min(1.0, rad / max(1e-6, 4 * thick)))) \
        + M @ Vector((rng.uniform(-0.1, 0.1) * thick, rng.uniform(-0.1, 0.1) * thick, 0))
    V, G = [], []
    for r4 in rows:
        V += list(r4)
        G += [0.90, 0.94, 1.0, 1.04]
    A = len(V)
    V.append(apex)
    G.append(1.06)
    F, kinds = [], []
    for i in range(k):
        j = (i + 1) % k
        F.append([4 * i, 4 * j, 4 * j + 1, 4 * i + 1]); kinds.append("side")
        F.append([4 * i + 1, 4 * j + 1, 4 * j + 2, 4 * i + 2]); kinds.append("chamfer")
        F.append([4 * i + 2, 4 * j + 2, 4 * j + 3, 4 * i + 3]); kinds.append("top")
        F.append([4 * i + 3, 4 * j + 3, A]); kinds.append("cap")
    cen = sum(V, Vector()) / len(V)
    out = []
    for f, kind in zip(F, kinds):
        a, b, c = V[f[0]], V[f[1]], V[f[2]]
        nrm = (b - a).cross(c - a)
        if nrm.dot((a + b + c) / 3 - cen) < 0:
            f.reverse()
            nrm = -nrm
        nrm.normalize()
        if kind == "side" and halo:
            out.append(SLOT["GlowHalo"])
        elif slot_fn:
            out.append(SLOT[slot_fn(kind, nrm)])
        elif mossy and kind == "cap" and nrm.z > moss_dot * 0.4:
            out.append(SLOT["Moss"])      # moss sits on the crown of an up-facing boulder (bible), not the whole plate
        elif mossy and kind == "top" and nrm.z > 0.8:
            out.append(SLOT["Moss"])
        else:
            out.append(SLOT[base_slot])
    geo.add(V, F, out, bone, smooth=True, shade=G)


def rock(geo, p, n, r, thick, base_slot, rng, moss=0.0, bone=None, elong=(0.8, 1.2), sink=0.3, moss_dot=0.45):
    """A free-standing boulder plate (knuckles, toes, fist fronts): a jittered k-gon slab."""
    M = frame(n, rng.uniform(0, math.tau))
    ex, ey = rng.uniform(*elong), rng.uniform(0.85, 1.05)
    k = rng.choice((6, 7))
    outline = []
    for i in range(k):
        a = math.tau * i / k + rng.uniform(-0.2, 0.2)
        rr = r * rng.uniform(0.85, 1.0)
        outline.append((math.cos(a) * rr * ex, math.sin(a) * rr * ey))
    slab(geo, Vector(p), n, M, outline, thick, base_slot, rng, mossy=rng.random() < moss, bone=bone, sink=sink,
         moss_dot=moss_dot, halo=False)


def clip_poly(poly, nx, ny, c):
    """Keep the part of a 2-D polygon with nx*x + ny*y <= c (Sutherland-Hodgman)."""
    out = []
    for i in range(len(poly)):
        a, b = poly[i], poly[(i + 1) % len(poly)]
        da, db = nx * a[0] + ny * a[1] - c, nx * b[0] + ny * b[1] - c
        if da <= 0:
            out.append(a)
        if (da < 0) != (db < 0) and abs(da - db) > 1e-12:
            t = da / (da - db)
            out.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t))
    return out


def voronoi_plates(geo, bvh, seeds, gap, thick_fn, slot_fn_for, rng, rmax_fn, sink=0.45, min_edge=0.0):
    """Tile the body with plates: each seed's Voronoi cell in its own tangent plane, shrunk by gap/2 on every side
    so the glowing body shows as an even crack between plates (brief: crack inlays as channels in team glow).
    seeds: [(p, n, d)]. thick_fn(p, n, d) -> plate height; slot_fn_for(p, n, d) -> (base_slot, mossy, bone)."""
    P = np.array([tuple(s[0]) for s in seeds])
    NN = np.array([tuple(Vector(s[1]).normalized()) for s in seeds])
    count = 0
    for i, (p, n, d) in enumerate(seeds):
        n = Vector(n).normalized()
        M = frame(n, 0.0)
        Mi = M.transposed()
        rmax = rmax_fn(p, n, d)
        poly = [(math.cos(math.tau * a / 10) * rmax, math.sin(math.tau * a / 10) * rmax) for a in range(10)]
        dist = np.sqrt(((P - np.array(tuple(p))) ** 2).sum(1))
        for j in np.argsort(dist)[1:18]:
            if dist[j] > 2.6 * rmax + gap:
                break
            q = Mi @ (Vector(P[j]) - p)
            L = math.hypot(q.x, q.y)
            if L < 1e-6:
                continue
            poly = clip_poly(poly, q.x / L, q.y / L, L / 2 - gap / 2)
            if len(poly) < 3:
                break
        if len(poly) < 3:
            continue
        # drop very short edges (fewer triangles, cleaner polygons)
        clean = []
        for q in poly:
            if not clean or math.hypot(q[0] - clean[-1][0], q[1] - clean[-1][1]) > min_edge:
                clean.append(q)
        if len(clean) > 3 and math.hypot(clean[0][0] - clean[-1][0], clean[0][1] - clean[-1][1]) < min_edge:
            clean.pop()
        if len(clean) < 3:
            continue
        area = 0.5 * abs(sum(clean[a][0] * clean[(a + 1) % len(clean)][1] - clean[(a + 1) % len(clean)][0] * clean[a][1]
                             for a in range(len(clean))))
        if area < (0.6 * gap) ** 2:
            continue

        def surf(w, n=n):
            hit, _, _, _ = bvh.ray_cast(w + n * 0.3, -n, 0.7)
            if hit is None:
                hit = bvh.find_nearest(w)[0]
            return hit if hit is not None else w

        base_slot, mossy, bone = slot_fn_for(p, n, d)
        th = thick_fn(p, n, d)
        near = np.argsort(dist)[1:5]
        cosang = np.clip(NN[near] @ np.array(tuple(n)), -1, 1)
        theta = float(np.mean(np.arccos(cosang)))
        flare = min(0.75 * th, 1.8 * th * math.tan(theta / 2) + 0.15 * gap)     # tops of neighbouring plates meet on convex parts
        slab(geo, Vector(p), n, M, clean, th, base_slot, rng, mossy=mossy, bone=bone, sink=sink,
             surf=surf, flare=flare)
        count += 1
    return count


def confine_glow(geo, seeds, crack_w):
    """v2: the body under the plates glows ONLY along the crack network (bible: glow in the cracks, never in wide
    patches). A body face keeps the Glow slot when its centre lies within crack_w of the bisector between its two
    nearest plate seeds (where two plates meet); every other Glow face (under a plate, or a hole the plates leave on a
    tight curve) becomes dark Skin, so a gap shows as a shadowed crevice, not a lit patch."""
    if not seeds:
        return
    P = np.array([tuple(sd[0]) for sd in seeds])
    glow, dark = SLOT["Glow"], SLOT["Skin"]
    kept = 0
    for fi, (f, m) in enumerate(zip(geo.F, geo.M)):
        if m != glow:
            continue
        c = sum((geo.V[i] for i in f), Vector()) / len(f)
        d2 = ((P - np.array(tuple(c))) ** 2).sum(1)
        i1, i2 = np.argsort(d2)[:2]
        sep = math.sqrt(((P[i1] - P[i2]) ** 2).sum())
        bis = (d2[i2] - d2[i1]) / (2 * max(1e-6, sep))          # distance to the bisector plane
        reach = math.sqrt(d2[i1]) < 1.6 * seeds[i1][2]            # inside some plate's reach
        if bis < crack_w and reach:
            kept += 1
        else:
            geo.M[fi] = dark
    print("GLOW body faces kept in cracks", kept)


def tile(geo, p, n, w, h, thick, slot, rng, bone=None, spin=0.0):
    """A flat rounded-rectangle belly segment: an octagon slab (chamfered corners in plan and on the top edge)."""
    M = frame(n, spin)
    outline = []
    for sx, sy in ((1, 1), (-1, 1), (-1, -1), (1, -1)):
        pts = [(sx * w, sy * h * 0.72), (sx * w * 0.72, sy * h)]
        outline += pts if sx * sy > 0 else pts[::-1]
    # order counter-clockwise by angle
    outline.sort(key=lambda q: math.atan2(q[1], q[0]))
    slab(geo, Vector(p), n, M, outline, thick, slot, rng, sink=0.35, halo=False)


def crystal(geo, base, d, length, radius, rng, bone=None, base_frac=0.3, sides=6, cap=True):
    """Hexagonal crystal, self-lit team glow. Facets are painted (unlit): root ring CrystalBase, side facets
    alternating Crystal / CrystalDark, tip facets CrystalTip."""
    d = Vector(d).normalized()
    M = frame(d, rng.uniform(0, math.tau))
    base = Vector(base) - d * (0.18 * length)
    h1 = length * 0.74
    rings = [(0.0, radius), (length * base_frac, radius * 0.98), (h1, radius * 0.9)]
    V = []
    for z, r in rings:
        for i in range(sides):
            a = math.tau * i / sides
            V.append(base + M @ Vector((math.cos(a) * r, math.sin(a) * r, z)))
    tip = base + M @ Vector((rng.uniform(-0.2, 0.2) * radius, rng.uniform(-0.2, 0.2) * radius, length))
    V.append(tip)
    F, mats = [], []
    if cap:
        F.append(list(range(sides))[::-1]); mats.append(SLOT["CrystalBase"])
    for ring in (0, 1):
        for i in range(sides):
            a, b = ring * sides + i, ring * sides + (i + 1) % sides
            F.append([a, b, b + sides, a + sides])
            mats.append(SLOT["CrystalBase"] if ring == 0 else SLOT["Crystal" if i % 2 == 0 else "CrystalDark"])
    t = len(V) - 1
    for i in range(sides):
        F.append([2 * sides + i, 2 * sides + (i + 1) % sides, t]); mats.append(SLOT["CrystalTip" if i % 2 == 0 else "Crystal"])
    geo.add(V, F, mats, bone)


def cluster(geo, p, n, size, count, rng, bone=None, spread=0.45):
    n = Vector(n).normalized()
    M = frame(n)
    for i in range(count):
        if i == 0:
            d, off, ln = n, Vector((0, 0, 0)), size
        else:
            a = math.tau * i / max(1, count - 1) + rng.uniform(-0.4, 0.4)
            tilt = spread * rng.uniform(0.6, 1.0)
            d = (n + M @ Vector((math.cos(a) * tilt, math.sin(a) * tilt, 0))).normalized()
            off = M @ Vector((math.cos(a), math.sin(a), 0)) * (0.16 * size)
            ln = size * rng.uniform(0.5, 0.78)
        crystal(geo, Vector(p) + off, d, ln, max(0.035, 0.27 * ln), rng, bone)


# ------------------------------------------------------------------------------------------------ v3 boulder kit
# v3 (2026-11-12 review of sbs-10-azure-adult): the bible creature is a few LARGE faceted boulders per mass with deep
# glowing gaps, not a mosaic of small plates. Every boulder is its own convex rock: a jittered, flat-topped point
# cloud -> convex hull -> near-coplanar facets merged -> one chamfer. Value is baked per facet (three steps: light top
# plane, mid side, dark underside) so it survives the flat toon shader; facets touching the glowing core take the
# GlowHalo team slot (the soft cyan rim the glow spills onto boulder edges).
STEP_TOP, STEP_MID, STEP_UNDER = 1.2, 0.95, 0.68


def _hull_bm(pts):
    bm = bmesh.new()
    for p in pts:
        bm.verts.new(p)
    res = bmesh.ops.convex_hull(bm, input=bm.verts[:])
    dead = list({g for g in res["geom_interior"] + res["geom_unused"] if isinstance(g, bmesh.types.BMVert)})
    if dead:
        bmesh.ops.delete(bm, geom=dead, context="VERTS")
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm


def _bm_out(bm):
    bm.verts.index_update()
    bm.normal_update()
    V = [v.co.copy() for v in bm.verts]
    F = [[v.index for v in f.verts] for f in bm.faces]
    C = [f.calc_center_median() for f in bm.faces]
    N = [f.normal.copy() for f in bm.faces]
    return V, F, C, N


def boulder_pts(c, radii, up, rng, npts=16, flat_top=0.6, spin=None):
    up = Vector(up).normalized()
    M = frame(up, rng.uniform(0, math.tau) if spin is None else spin)
    ga = math.pi * (3 - math.sqrt(5))
    ph = rng.uniform(0, math.tau)
    pts = []
    for i in range(npts):
        z = 1 - 2 * (i + 0.5) / npts
        r = math.sqrt(max(0.0, 1 - z * z))
        a = i * ga + ph
        q = Vector((math.cos(a) * r, math.sin(a) * r, z)) * rng.uniform(0.9, 1.07)
        if q.z > flat_top:                  # broad top plane (the lighter facet the bible paints on every rock)
            q.z = flat_top + (q.z - flat_top) * 0.25
        if q.z < -0.72:
            q.z = -0.72 + (q.z + 0.72) * 0.4
        pts.append(Vector(c) + M @ Vector((q.x * radii[0], q.y * radii[1], q.z * radii[2])))
    return pts, M


def boulder(geo, b, rng, core_bvh=None, rim_d=0.0, near=()):
    """b: dict c, r (3 radii along the boulder frame, r[2] along 'up'), up, slot, bone, moss (0..1 cover), n, rim."""
    c, radii = Vector(b["c"]), b["r"]
    up = Vector(b.get("up", (0, 0, 1)))
    pts, M = boulder_pts(c, radii, up, rng, b.get("n", 16), b.get("flat", 0.6), b.get("spin"))
    bm = _hull_bm(pts)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(9), verts=bm.verts[:], edges=bm.edges[:])
    ch = min(CHAMFER[0], 0.12 * min(radii))
    res = bmesh.ops.bevel(bm, geom=bm.edges[:] + bm.verts[:], offset=ch, offset_type="OFFSET", segments=1,
                          profile=0.5, affect="EDGES", clamp_overlap=True)
    for f in bm.faces:
        f.tag = False
    for f in res["faces"]:                 # the new chamfer strips and corners: the only rim candidates
        f.tag = True
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bevel_f = [f.tag for f in bm.faces]
    V, F, C, N = _bm_out(bm)
    bm.free()
    mats, fs = [], []
    for fc, n, bev in zip(C, N, bevel_f):
        nz = n.z
        if b.get("rim", True) and (bev or nz < -0.15) and nz < 0.5:
            rim = False
            if core_bvh is not None:
                hit = core_bvh.find_nearest(fc)
                rim = hit[0] is not None and (hit[0] - fc).length < rim_d and (fc - hit[0]).dot(hit[1]) > 0
            # the facet faces a neighbouring boulder across a gap: the glow in that gap spills onto it
            if not rim and near and min(ell_dist(fc, o) for o in near) > 1.0:
                rim = any(ell_dist(fc + n * TUNE["rim_k"] * rim_d, o) < 1.0 for o in near)
            if rim:
                mats.append(SLOT["GlowHalo"])
                fs.append(1.0)
                continue
        mats.append(SLOT[b["slot"]])
        fs.append(STEP_TOP if nz > 0.55 else (STEP_UNDER if nz < -0.2 else STEP_MID))
    geo.add(V, F, mats, b["bone"], smooth=False, fshade=fs)
    if b.get("moss", 0) > 0:
        moss_cap(geo, pts, c, up, radii, b["moss"] * TUNE["moss_k"], rng, b["bone"])


def moss_cap(geo, pts, c, up, radii, cover, rng, bone):
    """Moss as a lumpy carpet of soft clumps draped over the boulder's top: squashed, smooth-shaded little domes
    centred on the rock's upper points (so they follow the top and spill over its upper edges), each lit on its
    crown and dark olive underneath (bible: soft rounded caps over shoulders, head and forearms)."""
    up = Vector(up).normalized()
    rz = radii[2]
    rr = 0.5 * (radii[0] + radii[1])
    tops = sorted(pts, key=lambda p: -(p - c).dot(up))
    n = 1 + int(cover * 3.2)
    chosen = [tops[0]]
    for p in tops[1:]:
        if len(chosen) >= n:
            break
        if (p - c).dot(up) < (0.62 - 0.5 * cover) * rz:
            break
        if all((p - q).length > 0.2 * rr for q in chosen):
            chosen.append(p)
    # one broad cushion over the crown ties the clumps into a single draped carpet
    chosen.insert(0, c + up * (0.72 * rz))
    seg, rings = 7, 4
    for k, p in enumerate(chosen):
        nrm = up if k == 0 else ((p - c).normalized() + up * 1.2).normalized()
        R = frame(nrm, rng.uniform(0, math.tau))
        cr = max(0.085, rr * (0.62 if k == 0 else rng.uniform(0.3, 0.4)) * (0.7 + 0.3 * cover))
        q0 = p + nrm * (0.05 * cr) - (p - c).normalized() * (0.25 * cr)
        sq = (rng.uniform(1.0, 1.3), rng.uniform(0.85, 1.05), 0.5)
        V, F = [], []
        for j in range(1, rings):
            th = math.pi * j / rings
            for i in range(seg):
                ph = math.tau * i / seg + 0.4 * j
                V.append(q0 + R @ Vector((math.sin(th) * math.cos(ph) * sq[0], math.sin(th) * math.sin(ph) * sq[1],
                                          math.cos(th) * sq[2])) * cr)
        V.append(q0 + R @ Vector((0, 0, sq[2])) * cr)
        V.append(q0 - R @ Vector((0, 0, sq[2])) * cr)
        t, b_ = len(V) - 2, len(V) - 1
        for j in range(rings - 2):
            for i in range(seg):
                a0, a1 = j * seg + i, j * seg + (i + 1) % seg
                F.append([a0, a1, a1 + seg, a0 + seg])
        for i in range(seg):
            F.append([t, (i + 1) % seg, i])
            o = (rings - 2) * seg
            F.append([b_, o + i, o + (i + 1) % seg])
        G = [max(0.55, min(1.04, 0.78 + 0.6 * (v - q0).dot(up) / (sq[2] * cr))) for v in V]
        geo.add(V, F, SLOT["Moss"], bone, smooth=True, shade=G)


def crystal3(geo, base, d, length, radius, rng, bone=None, sides=6):
    """v3 crystal: a tall separate hexagonal prism with a graded core (deep base -> bright tip, baked per vertex in the
    team-glow value k, so the allegiance swap keeps it) and alternating facets one step darker (visible facet edge)."""
    d = Vector(d).normalized()
    M = frame(d, rng.uniform(0, math.tau))
    base = Vector(base) - d * (0.22 * length)
    rings = [(0.0, radius * 0.92, 0.5), (length * 0.4, radius, 0.74), (length * 0.72, radius * 0.94, 0.94)]
    V, G = [], []
    for z, r, k in rings:
        for i in range(sides):
            a = math.tau * i / sides
            V.append(base + M @ Vector((math.cos(a) * r, math.sin(a) * r, z)))
            G.append(k)
    V.append(base + M @ Vector((rng.uniform(-0.15, 0.15) * radius, rng.uniform(-0.15, 0.15) * radius, length)))
    G.append(1.0)
    F, mats = [], []
    for ring in (0, 1):
        for i in range(sides):
            a, b = ring * sides + i, ring * sides + (i + 1) % sides
            F.append([a, b, b + sides, a + sides])
            mats.append(SLOT["CrystalTip" if i % 2 == 0 else "Crystal"])
    t = len(V) - 1
    for i in range(sides):
        F.append([2 * sides + i, 2 * sides + (i + 1) % sides, t])
        mats.append(SLOT["CrystalTip" if i % 2 == 0 else "Crystal"])
    geo.add(V, F, mats, bone, shade=G, graded=True)


def ell_dist(p, b):
    """normalized ellipsoid distance of point p from boulder b (1.0 = on its surface)"""
    M = frame(Vector(b.get("up", (0, 0, 1))), 0.0)
    q = M.transposed() @ (Vector(p) - Vector(b["c"]))
    r = b["r"]
    rr = max(r[0], r[1])
    return math.sqrt((q.x / rr) ** 2 + (q.y / rr) ** 2 + (q.z / r[2]) ** 2)


def spike(geo, p, d, length, radius, slot, bone=None, sides=4, rng=None):
    d = Vector(d).normalized()
    M = frame(d, rng.uniform(0, math.tau) if rng else 0.0)
    base = Vector(p) - d * (0.25 * length)
    V = [base + M @ Vector((math.cos(math.tau * i / sides) * radius, math.sin(math.tau * i / sides) * radius, 0))
         for i in range(sides)]
    V.append(base + d * length)
    F = [list(range(sides))[::-1]] + [[i, (i + 1) % sides, sides] for i in range(sides)]
    geo.add(V, F, SLOT[slot], bone)


def ellipsoid(geo, c, r, scale, slot, bone=None, seg=10, rings=7, smooth=True, rot=None):
    V, F = [], []
    R = rot or Matrix.Identity(3)
    for j in range(1, rings):
        th = math.pi * j / rings
        for i in range(seg):
            ph = math.tau * i / seg
            q = Vector((math.sin(th) * math.cos(ph) * scale[0], math.sin(th) * math.sin(ph) * scale[1], math.cos(th) * scale[2])) * r
            V.append(Vector(c) + R @ q)
    top = len(V); V.append(Vector(c) + R @ Vector((0, 0, r * scale[2])))
    bot = len(V); V.append(Vector(c) + R @ Vector((0, 0, -r * scale[2])))
    for j in range(rings - 2):
        for i in range(seg):
            a, b = j * seg + i, j * seg + (i + 1) % seg
            F.append([a, b, b + seg, a + seg])
    for i in range(seg):
        F.append([top, (i + 1) % seg, i])
        o = (rings - 2) * seg
        F.append([bot, o + i, o + (i + 1) % seg])
    zs = [(R.transposed() @ (v - Vector(c))).z / (r * scale[2]) for v in V]
    geo.add(V, F, SLOT[slot], bone, smooth, shade=[1.0 + 0.06 * z if z > 0 else 1.0 + 0.08 * z for z in zs])


def metaball_body(balls, limbs, voxel, tri_target):
    """Union of ellipsoids (balls) and tapered sphere chains (limbs), voxel-remeshed into one closed skin and
    smoothed so the joins blend. Radii are the real surface radii (a metaball field shrinks them to ~0.58 r, which
    made the first pass's limbs thin; the CM-69 metaball route was replaced here for that reason)."""
    src = bmesh.new()

    def ball(co, r, s=(1, 1, 1)):
        m = Matrix.Translation(Vector(co)) @ Matrix.Diagonal((r * s[0], r * s[1], r * s[2], 1.0))
        bmesh.ops.create_uvsphere(src, u_segments=20, v_segments=12, radius=1.0, matrix=m)

    for b in balls:
        ball(*b)
    for a, b, r0, r1 in limbs:
        a, b = Vector(a), Vector(b)
        n = max(3, int((b - a).length / (0.3 * min(r0, r1))) + 1)
        for i in range(n):
            t = i / (n - 1)
            ball(a.lerp(b, t), r0 + (r1 - r0) * t)
    me = bpy.data.meshes.new("Src")
    src.to_mesh(me)
    src.free()
    work = bpy.data.objects.new("Work", me)
    COLL.objects.link(work)
    rm = work.modifiers.new("Remesh", "REMESH")
    rm.mode = "VOXEL"
    rm.voxel_size = voxel
    sm = work.modifiers.new("Smooth", "SMOOTH")
    sm.factor = 0.8
    sm.iterations = 6
    dg = bpy.context.evaluated_depsgraph_get()
    me2 = bpy.data.meshes.new_from_object(work.evaluated_get(dg))
    work.modifiers.clear()
    work.data = me2
    tris = sum(len(p.vertices) - 2 for p in me2.polygons)
    dec = work.modifiers.new("Decimate", "DECIMATE")
    dec.ratio = min(1.0, tri_target / max(1, tris))
    dec.use_collapse_triangulate = True
    dg = bpy.context.evaluated_depsgraph_get()
    me3 = bpy.data.meshes.new_from_object(work.evaluated_get(dg))
    bpy.data.objects.remove(work, do_unlink=True)
    bpy.data.meshes.remove(me)
    bpy.data.meshes.remove(me2)
    bm = bmesh.new()
    bm.from_mesh(me3)
    bpy.data.meshes.remove(me3)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.normal_update()
    return bm


def add_body(geo, bm, face_slot, smooth_fn=lambda c, n: False, decal_fn=lambda c, n: False):
    bm.verts.index_update()
    V = [v.co.copy() for v in bm.verts]
    F, mats, sm = [], [], []
    o = len(geo.V)
    geo.V += V
    geo.B += [None] * len(V)
    geo.G += [1.0] * len(V)
    for f in bm.faces:
        c, n = f.calc_center_median(), f.normal
        geo.F.append(tuple(v.index + o for v in f.verts))
        geo.M.append(SLOT[face_slot(c, n)])
        geo.SM.append(smooth_fn(c, n))
        geo.D.append(bool(decal_fn(c, n)))
        geo.FS.append(1.0)
        geo.GR.append(False)


def surface_samples(bm, count, rng):
    # sorted so the draw does not depend on bmesh's internal face order (create_uvsphere's order varies per run)
    faces = sorted(bm.faces, key=lambda f: tuple(round(c, 5) for c in f.calc_center_median()))
    areas = np.array([f.calc_area() for f in faces])
    cdf = np.cumsum(areas) / areas.sum()
    out = []
    for _ in range(count):
        f = faces[int(np.searchsorted(cdf, rng.random()))]
        vs = sorted((v.co for v in f.verts), key=lambda c: (round(c.x, 5), round(c.y, 5), round(c.z, 5)))
        a, b = rng.random(), rng.random()
        if a + b > 1:
            a, b = 1 - a, 1 - b
        p = vs[0] + (vs[1] - vs[0]) * a + (vs[2] - vs[0]) * b
        out.append((p, f.normal.copy()))
    return out


def poisson(samples, dmin_fn, rng):
    """Greedy Poisson-disk selection; dmin varies by region. Returns [(p, n, dmin)]."""
    order = list(range(len(samples)))
    rng.shuffle(order)
    acc, accd, out = np.zeros((0, 3)), np.zeros(0), []
    for i in order:
        p, n = samples[i]
        d = dmin_fn(p, n)
        if d is None:
            continue
        if len(acc):
            dist = np.sqrt(((acc - np.array(p)) ** 2).sum(1))
            if (dist < np.maximum(accd, d)).any():
                continue
        acc = np.vstack([acc, np.array(p)])
        accd = np.append(accd, d)
        out.append((p, n, d))
    return out


def ray(bvh, origin, direction):
    hit = bvh.ray_cast(Vector(origin), Vector(direction).normalized())
    return (hit[0], hit[1]) if hit[0] is not None else (None, None)


# ------------------------------------------------------------------------------------------------ rig + skin
def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(1e-9, ab.length_squared)))
    return (p - (a + ab * t)).length


def make_rig(name, bones):
    ad = bpy.data.armatures.new(name + "_RigData")
    arm = bpy.data.objects.new(name + "_Rig", ad)
    COLL.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for bn, h, t, parent in bones:
        eb = ad.edit_bones.new(bn)
        eb.head, eb.tail, eb.roll = Vector(h), Vector(t), 0.0
        if parent:
            eb.parent = ad.edit_bones[parent]
            eb.use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    arm.select_set(False)
    ad.display_type = "STICK"
    return arm


def skin(geo, bones, skip=("Root", "Jaw")):
    segs = [(bn, Vector(h), Vector(t)) for bn, h, t, _ in bones if bn not in skip]
    W = []
    for v, b in zip(geo.V, geo.B):
        if b is not None:
            W.append({b: 1.0})
            continue
        ds = []
        for bn, h, t in segs:
            if bn.endswith("_L") and v.x < -0.08:
                continue
            if bn.endswith("_R") and v.x > 0.08:
                continue
            ds.append((seg_dist(v, h, t), bn))
        ds.sort()
        top = ds[:2]
        ws = [(1.0 / max(1e-4, d) ** 4, bn) for d, bn in top]
        if len(top) == 1 or top[0][0] < 0.6 * top[1][0]:
            ws = ws[:1]
        tot = sum(w for w, _ in ws)
        W.append({bn: w / tot for w, bn in ws})
    return W


def rigid_bone_for(geo_body_count, W, kd_points, p):
    """Bone with the largest weight at the nearest skinned body vertex."""
    d = np.sqrt(((kd_points - np.array(p)) ** 2).sum(1))
    i = int(d.argmin())
    return max(W[i].items(), key=lambda kv: kv[1])[0]


def finish(name, geo, bones, target_h):
    """Ground, scale to target height, build mesh + rig, skin, return (mesh_obj, rig, scale)."""
    zmin = min(v.z for v in geo.V)
    zmax = max(v.z for v in geo.V)
    s = target_h / (zmax - zmin)
    print("SCALE %s design_h=%.3f s=%.4f chamfer=%.3f m" % (name, zmax - zmin, s, CHAMFER[0] * s))
    off = Vector((0, 0, zmin))
    geo.V = [(v - off) * s for v in geo.V]
    bones = [(bn, (Vector(h) - off) * s, (Vector(t) - off) * s, p) for bn, h, t, p in bones]
    # rigid pieces flagged "AUTO" take the dominant bone of the nearest skinned body vertex
    body_idx = [i for i, b in enumerate(geo.B) if b is None]
    W = skin(geo, bones)
    body_pts = np.array([tuple(geo.V[i]) for i in body_idx])
    body_W = [W[i] for i in body_idx]
    cache = {}
    for i, b in enumerate(geo.B):
        if b and b.startswith("AUTO:"):
            key = b
            if key not in cache:
                cx, cy, cz = (float(x) for x in b[5:].split(","))
                c = (Vector((cx, cy, cz)) - off) * s
                cache[key] = rigid_bone_for(0, body_W, body_pts, c)
            W[i] = {cache[key]: 1.0}
    me = bpy.data.meshes.new(name + "_Mesh")
    me.from_pydata([tuple(v) for v in geo.V], [], [list(f) for f in geo.F])
    me.materials.append(MAT)
    me.polygons.foreach_set("use_smooth", geo.SM)
    me.validate(clean_customdata=False)
    me.update()
    bm = bmesh.new()
    bm.from_mesh(me)
    # re-orient closed pieces only; open pieces (plate slabs, the egg's ground splash) are built facing out and a
    # whole-mesh recalc can flip them
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
        if all(e.is_manifold for f in isl for e in f.edges):
            bmesh.ops.recalc_face_normals(bm, faces=isl)
    bm.to_mesh(me)
    bm.free()
    paint(me, name, geo)
    face_uvs(me, name, geo, off, s)
    ob = bpy.data.objects.new(name + "_Mesh", me)
    COLL.objects.link(ob)
    rig = make_rig(name, bones)
    groups = {bn: ob.vertex_groups.new(name=bn) for bn, *_ in bones}
    for i, w in enumerate(W):
        for bn, val in w.items():
            groups[bn].add([i], val, "REPLACE")
    ob.parent = rig
    mod = ob.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    ob["cm_outline_width"] = OUTLINE_WIDTH[name]
    ob["cm_stage"] = name
    rig["cm_stage"] = name
    rig["cm_scale"] = s          # design units -> metres (pose_fit.py keys locations like key_clips)
    return ob, rig, s


def paint(me, stage, geo):
    """Vertex colour layer "Col" (corner domain, sRGB bytes): RGB = slot lit colour x baked gradient, A = mask code.
    Team-glow slots are self-lit, so they get no gradient. Also stores the slot per face as an int attribute
    "cm_slot" (Blender-side only, for checks; FBX ignores it)."""
    names = PAL.SLOTS
    assert len(me.polygons) == len(geo.M), ("validate dropped faces", len(me.polygons), len(geo.M))
    rgb = {s: PAL.slot_rgb(stage, s) for s in names}
    col = me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    me.color_attributes.active_color = col
    me.color_attributes.render_color_index = me.color_attributes.active_color_index
    slot_attr = me.attributes.new("cm_slot", "INT", "FACE")
    vals = []
    for poly, mi in zip(me.polygons, geo.M):
        sname = names[mi]
        slot_attr.data[poly.index].value = mi
        base, a = rgb[sname], PAL.slot_alpha(sname)
        for li in poly.loop_indices:
            g = geo.G[me.loops[li].vertex_index]
            if sname in PAL.TEAM_SLOTS and not geo.GR[poly.index]:
                g = 1.0
            g *= geo.FS[poly.index]
            vals.append((min(1.0, base[0] * g), min(1.0, base[1] * g), min(1.0, base[2] * g), a))
    for li, v in enumerate(vals):
        col.data[li].color_srgb = v


def face_uvs(me, stage, geo, off=None, s=1.0):
    """UV map "UVMap" for the painted face decal: faces flagged in geo.D take the front planar projection of their
    stage's rect (kaiju_face.py, design units); a flagged face with any corner outside the rect, and every other face,
    sits on the transparent corner texel, so only the head front shows paint."""
    uvl = me.uv_layers.new(name="UVMap")
    n_dec = 0
    for poly in me.polygons:
        uvs = None
        if stage in FACE.RECT and geo.D[poly.index]:
            uvs = []
            for li in poly.loop_indices:
                v = me.vertices[me.loops[li].vertex_index].co
                d = v / s + off
                uv = FACE.uv_for(stage, d.x, d.z)
                if uv is None:
                    uvs = None
                    break
                uvs.append(uv)
        if uvs:
            n_dec += 1
            for li, uv in zip(poly.loop_indices, uvs):
                uvl.data[li].uv = uv
        else:
            for li in poly.loop_indices:
                uvl.data[li].uv = FACE.CORNER_UV
    print("FACE_UV %s decal faces %d" % (stage, n_dec))


def auto(p):
    return "AUTO:%f,%f,%f" % tuple(p)


# ------------------------------------------------------------------------------------------------ clips
def smooth(x):
    x = max(0.0, min(1.0, x))
    return x * x * (3 - 2 * x)


def key_clips(rig, clips, scale):
    """clips: [(name, frames, fn(t) -> {"rot": {bone: [(axis, deg)]}, "loc": {bone: (x,y,z) in metres/scale}})].
    Rotations are about armature-space axes (X = lateral pitch, Y = forward roll, Z = yaw), as in the CM-69 helper.
    Each clip becomes its own action and NLA track (the FBX exporter writes one take per strip)."""
    ad = rig.animation_data_create()
    rest = {pb.name: pb.bone.matrix_local.to_3x3().to_quaternion() for pb in rig.pose.bones}
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"
    for name, frames, fn in clips:
        act = bpy.data.actions.new(name)
        ad.action = act
        step = 2
        fr = list(range(0, frames + 1, step))
        if fr[-1] != frames:
            fr.append(frames)
        for f in fr:
            pose = fn(f / frames)
            for pb in rig.pose.bones:
                q = Quaternion()
                for axis, deg in pose.get("rot", {}).get(pb.name, []):
                    wq = Quaternion(Vector(axis), math.radians(deg))
                    q = q @ (rest[pb.name].inverted() @ wq @ rest[pb.name])
                pb.rotation_quaternion = q
                loc = pose.get("loc", {}).get(pb.name)
                pb.location = (rest[pb.name].inverted() @ (Vector(loc) * scale)) if loc else Vector((0, 0, 0))
                pb.keyframe_insert("rotation_quaternion", frame=f + 1)
                pb.keyframe_insert("location", frame=f + 1)
        ad.action = None
        tr = ad.nla_tracks.new()
        tr.name = name
        st = tr.strips.new(name, 1, act)
        st.name = name
    for pb in rig.pose.bones:
        pb.rotation_quaternion = Quaternion()
        pb.location = Vector((0, 0, 0))


X, Y, Z = (1, 0, 0), (0, 1, 0), (0, 0, 1)
PIVOT_Y = [0.95]   # hind-foot contact (design y) of the stage being keyed, for the rearing pivot


# Poses fitted to the bible crops with pose_fit.py (silhouette IoU; specs in the v2 REPORT). Degrees about armature
# axes as in key_clips; hz = hips lift in design units. Clips reach these values at full weight.
ROAR_FIT = {"hz": 0.06, "ay": 16.0, "ax": 8.0, "fy": -8.0, "fx": -8.0, "sp": -10, "ch": -10, "nk": -10.5, "hd": -34.0, "tx": -6.0, "ty": 12.0, "tl": -24.0}
WINDUP_FIT = {"arx": 195.0, "ary": 2.0, "frx": -40.0, "fkx": -20.0, "hz": 0.06, "sp": 12.0, "chx": 5.0, "chy": -6, "hd": -32.0, "alx": 18.0, "aly": 0, "flx": 4.0}
BERSERK_FIT = {"ra": -6.0, "ax": 12.0, "ay": 6, "sp": -1.0, "ch": -8, "nk": -2.0, "hd": -22, "fx": 6, "fk": -8.0, "tx": 18.0}
HATCH_IDLE_FIT = {"nk": -34.0, "hd": 18.0, "hy": 0.0, "sp": -11.25, "ch": 5.0, "ax": 12.0, "ay": -12.0, "tx": 30, "ty": -6.0, "tl": -14.0}


def creature_clips(stage="Adult"):
    """Clips the docs name (ARCHITECTURE.md section 6, PARITY p10/p11): idle, walk, roar, slam wind-up, slam, berserk,
    KO. Loops are in place (no root motion). Locations are design units (multiplied by the stage scale)."""

    def idle(t):
        s = math.sin(math.tau * t)
        rot = {"Spine": [(X, -1.5 * s)], "Chest": [(X, -1.0 * s)], "Head": [(X, 2.5 * math.sin(math.tau * t + 1))],
               "Arm_L": [(X, 2.0 * s)], "Arm_R": [(X, 2.0 * s)], "Tail1": [(Z, 5 * s)], "Tail2": [(Z, 6 * s)]}
        if stage == "Hatchling":        # bible p10 hatchling: head up and turned to the viewer, chest lifted
            H = HATCH_IDLE_FIT
            base = {"Spine": [(X, H["sp"])], "Chest": [(X, H["ch"])], "Neck": [(X, H["nk"])],
                    "Head": [(X, H["hd"]), (Z, H["hy"])], "Arm_L": [(X, H["ax"]), (Y, -H["ay"])],
                    "Arm_R": [(X, H["ax"]), (Y, H["ay"])], "Thigh_L": [(X, H["tx"]), (Y, -H["ty"])],
                    "Thigh_R": [(X, H["tx"]), (Y, H["ty"])], "Tail1": [(Z, H["tl"])]}
            for k, v in base.items():
                rot[k] = v + rot.get(k, [])
        return {"rot": rot, "loc": {"Hips": (0, 0, 0.015 * s)}}

    def walk(t):
        p = math.tau * t
        s, c = math.sin(p), math.cos(p)
        liftL, liftR = max(0.0, -s), max(0.0, s)
        return {"rot": {"Arm_L": [(X, -6 - 20 * s)], "Arm_R": [(X, -6 + 20 * s)],
                        "Forearm_L": [(X, 18 * liftL)], "Forearm_R": [(X, 18 * liftR)],
                        "Fist_L": [(X, -20 * liftL)], "Fist_R": [(X, -20 * liftR)],
                        "Thigh_L": [(X, 16 * s)], "Thigh_R": [(X, -16 * s)],
                        "Foot_L": [(X, -10 * max(0.0, s))], "Foot_R": [(X, -10 * max(0.0, -s))],
                        "Spine": [(X, 6), (Z, 4 * s), (Y, 3 * s)], "Chest": [(Y, 4 * s)], "Head": [(X, -6), (Z, -4 * s)],
                        "Tail1": [(Z, 9 * s)], "Tail2": [(Z, 10 * c)]},
                "loc": {"Hips": (0.04 * s, 0, 0.05 * abs(math.sin(2 * p)))}}

    def roar(t):
        """Bible p11 roar: wide planted stance, arms splayed, head thrown up, jaw wide (ROAR_FIT)."""
        e = smooth(t / 0.3) * (1 - smooth((t - 0.8) / 0.2))
        sh = 2.5 * math.sin(math.tau * 9 * t) * e
        R = ROAR_FIT
        rot = {"Spine": [(X, R["sp"] * e)], "Chest": [(X, R["ch"] * e)], "Neck": [(X, R["nk"] * e)],
               "Head": [(X, R["hd"] * e + sh)], "Jaw": [(X, 42 * e)],
               "Arm_L": [(X, R["ax"] * e), (Y, -R["ay"] * e)], "Arm_R": [(X, R["ax"] * e), (Y, R["ay"] * e)],
               "Forearm_L": [(X, R["fx"] * e), (Y, -R["fy"] * e)], "Forearm_R": [(X, R["fx"] * e), (Y, R["fy"] * e)],
               "Thigh_L": [(X, R["tx"] * e), (Y, -R["ty"] * e)], "Thigh_R": [(X, R["tx"] * e), (Y, R["ty"] * e)],
               "Tail1": [(X, R["tl"] * e)]}
        return {"rot": rot, "loc": {"Hips": (0, 0, R["hz"] * e)}}

    def windup_pose(e, arc_back=True):
        """Bible p11 slam wind-up (WINDUP_FIT): the right fist swung back over the shoulder, body pitched down."""
        W = WINDUP_FIT
        arx = W["arx"] if arc_back else (W["arx"] - 360 if W["arx"] > 180 else W["arx"])
        return {"Spine": [(X, W["sp"] * e)], "Chest": [(X, W["chx"] * e), (Y, W["chy"] * e)], "Head": [(X, W["hd"] * e)],
                "Arm_R": [(X, arx * e), (Y, W["ary"] * e)], "Forearm_R": [(X, W["frx"] * e)],
                "Fist_R": [(X, W["fkx"] * e)], "Arm_L": [(X, W["alx"] * e), (Y, -W["aly"] * e)],
                "Forearm_L": [(X, W["flx"] * e)], "Jaw": [(X, 12 * e)]}

    def windup(t):
        e = smooth(t)
        return {"rot": windup_pose(e), "loc": {"Hips": (0, 0, WINDUP_FIT["hz"] * e)}}

    def slam(t):
        """From the wind-up pose the fist comes down IN FRONT (forward arc) onto the telegraph centre."""
        s = smooth(t / 0.55) ** 2
        rot = windup_pose(1 - s, arc_back=False)
        for b, add in (("Spine", 10 * s), ("Chest", 10 * s), ("Head", 4 * s)):
            rot[b] = rot.get(b, []) + [(X, add)]
        rot["Jaw"] = [(X, 12 + 10 * s)]
        return {"rot": rot, "loc": {"Hips": (0, 0, WINDUP_FIT["hz"] * (1 - s) - 0.08 * s)}}

    def berserk(t):
        """Bible p10 berserk: a hunched-up roar. The body rears a little about the hind feet (Root turn plus the
        matching lift, pivot = hind-foot contact at y = PIVOT_Y design units), fists stay planted, head thrown up,
        jaw wide, a shudder through the spine."""
        p = math.tau * t
        sh = math.sin(2 * p)
        Bf = BERSERK_FIT
        a = Bf["ra"] - 1.5 + 1.5 * math.sin(p)
        ar = math.radians(a)
        lift = (0, PIVOT_Y[0] * (1 - math.cos(ar)), -PIVOT_Y[0] * math.sin(ar))
        osc = 3 * (math.sin(p) - 1)
        return {"rot": {"Root": [(X, a)], "Spine": [(X, Bf["sp"]), (Y, 3 * sh)], "Chest": [(X, Bf["ch"])],
                        "Neck": [(X, Bf["nk"])], "Head": [(X, Bf["hd"] + 4 * math.sin(4 * p))],
                        "Jaw": [(X, 38 + 4 * math.sin(6 * p + math.pi))],
                        "Arm_L": [(X, Bf["ax"] + osc), (Y, -Bf["ay"])], "Arm_R": [(X, Bf["ax"] - osc), (Y, Bf["ay"])],
                        "Forearm_L": [(X, Bf["fx"])], "Forearm_R": [(X, Bf["fx"])], "Fist_L": [(X, Bf["fk"])],
                        "Fist_R": [(X, Bf["fk"])], "Thigh_L": [(X, Bf["tx"])], "Thigh_R": [(X, Bf["tx"])],
                        "Foot_L": [(X, 2)], "Foot_R": [(X, 2)], "Tail1": [(Z, 12 * sh), (X, 8)]},
                "loc": {"Root": lift}}

    def ko(t):
        e = smooth(t / 0.7)
        b = math.sin(min(1.0, max(0.0, (t - 0.7) / 0.3)) * math.pi) * 0.04
        return {"rot": {"Root": [(Y, 72 * e)], "Spine": [(X, 10 * e)], "Head": [(X, 22 * e), (Z, 10 * e)], "Jaw": [(X, 18 * e)],
                        "Arm_L": [(Y, -35 * e), (X, -15 * e)], "Arm_R": [(Y, 15 * e)], "Thigh_L": [(Y, -20 * e)],
                        "Tail1": [(Z, 20 * e)]},
                "loc": {"Hips": (0, 0, -0.25 * e + b)}}

    return [("Idle", 48, idle), ("Walk", 32, walk), ("Roar", 36, roar), ("SlamWindup", 20, windup), ("Slam", 14, slam),
            ("Berserk", 36, berserk), ("KO", 36, ko)]


def egg_clips():
    def idle(t):
        s = math.sin(math.tau * t)
        return {"rot": {"Shell": [(X, 1.5 * s), (Y, 1.2 * math.cos(math.tau * t))]}}

    def shake(t):
        e = math.sin(math.pi * t)
        return {"rot": {"Shell": [(X, 6 * e * math.sin(math.tau * 4 * t)), (Y, 5 * e * math.sin(math.tau * 6 * t + 1))]}}

    return [("Idle", 48, idle), ("Shake", 24, shake)]


# ------------------------------------------------------------------------------------------------ creature body kit
def creature(name, P, seed, target_h):
    """Shared quadruped builder for the hatchling and the adult. P is a dict of design-space proportions."""
    rng = random.Random(seed)
    geo = Geo()
    CHAMFER[0] = P["chamfer_m"] / (target_h / P["design_h"])
    bm = metaball_body(P["balls"], P["limbs"], P["voxel"], P["body_tris"])
    bvh = BVHTree.FromBMesh(bm)
    Hc = Vector(P["head"])
    RH = P["head_r"]

    def face_zone(c, n):
        return (c - Hc).length < RH * 1.45 and c.y < Hc.y + 0.05 * RH and n.y < -0.15

    def belly_zone(c, n):
        return P["belly"](c, n) and not (c - Hc).length < RH * 1.2

    fr = FACE.RECT[name]

    def decal_zone(c, n):
        """head front that takes the painted face (eyes, lids, smirk / grin, tan muzzle)"""
        return ((c - Hc).length < RH * 1.9 and c.y < Hc.y - 0.1 * RH and n.y < -0.2 and c.z > Hc.z - 0.75 * RH
                and FACE.uv_for(name, c.x, c.z) is not None)

    def body_slot(c, n):
        if decal_zone(c, n):
            return "Skin" if c.z > fr[1] - 0.15 * fr[3] / 0.36 else "Belly"   # paint lays the tan muzzle on top
        if face_zone(c, n):
            if n.z < -0.35 and c.z < P["muzzle"][2]:
                return "Belly"   # under the muzzle (v2: tan; the open mouth shows the interior piece behind the jaw)
            return "Belly" if c.z < P["muzzle"][2] + 0.03 else "Skin"
        if (c - Hc).length < RH * 1.9 and n.y < -0.25 and c.z < Hc.z - 0.3 * RH and abs(c.x) < RH:
            return "Belly"       # chin and throat run into the tan chest plate (p11 front)
        if (c - Hc).length < RH * 1.25:
            return "Skin"
        if belly_zone(c, n):
            return "Belly"
        return "Glow"

    if P.get("boulders"):
        return creature_v3(name, P, target_h, geo, bm, bvh, rng, Hc, RH, face_zone, decal_zone, body_slot)
    add_body(geo, bm, body_slot, lambda c, n: face_zone(c, n) or (c - Hc).length < RH * 1.25, decal_zone)

    # --- boulder plates over everything but the face and belly
    def dmin(p, n):
        if face_zone(p, n) or belly_zone(p, n) or decal_zone(p, n):
            return None
        if (p - Vector(P["muzzle"])).length < RH * 0.95:
            return None         # the snout stays clear so the painted smirk / grin reads
        if p.z < P["min_plate_z"]:
            return None
        if (p - Hc).length < RH * P.get("head_clear", 0.0):
            return None         # the head stays a smooth dark mass with a few authored plates (p11 face read)
        return P["dmin"](p, n)

    seeds = poisson(surface_samples(bm, P["samples"], rng), dmin, rng)
    confine_glow(geo, seeds, P["crack_w"])

    def slot_for(p, n, d):
        light = rng.random() < P["light_frac"](p)
        return ("SlateLight" if light else "Slate"), rng.random() < P["moss"](p, n), auto(p)

    nplates = voronoi_plates(geo, bvh, seeds, P["gap"], lambda p, n, d: d * P["plate_thick"] * rng.uniform(0.85, 1.1),
                             slot_for, rng, lambda p, n, d: d * 1.8, sink=0.5, min_edge=P["gap"] * 0.7)
    print("PLATES", name, nplates, "of", len(seeds), "seeds")

    # --- belly segments: a grid of tan tiles, ray-cast from the front
    for (bx, bz, w, h) in P["belly_tiles"]:
        p, n = ray(bvh, (bx, -6, bz), (0, 1, 0))
        if p is not None:
            tile(geo, p, n, w, h, 0.09 * P["unit"], "Belly", rng, bone=auto(p))

    # --- fists / forefeet: big lighter front boulder and knuckles on the ground
    for sx in (1, -1):
        fx, fy, fz = P["fist"]
        fx *= sx
        p, n = ray(bvh, (fx, -6, P["fist_plate_z"]), (0, 1, 0))
        if p is not None:
            rock(geo, p, (n + Vector((0, 0, 0.15))).normalized(), P["fist_plate_r"], P["fist_plate_r"] * 0.32,
                 "SlateLight", rng, moss=0.0, bone=auto(p), elong=(0.75, 0.8), sink=0.2)
        for k, dx in enumerate(P["knuckles"]):
            c = Vector((fx + dx * sx, fy - P["knuckle_fwd"], P["knuckle_r"] * 0.8))
            rock(geo, c, Vector((0, -0.35, 1)).normalized(), P["knuckle_r"], P["knuckle_r"] * 1.1, "Slate", rng,
                 bone=auto(Vector((fx, fy, fz))), sink=0.5)
    # --- hind toes with moss tips
    for sx in (1, -1):
        hx, hy, hz = P["foot"]
        hx *= sx
        for dx in P["toes"]:
            c = Vector((hx + dx * sx, hy - P["toe_fwd"], P["toe_r"] * 0.8))
            rock(geo, c, Vector((0, -0.4, 1)).normalized(), P["toe_r"], P["toe_r"] * 1.0, "Slate", rng,
                 bone=auto(Vector((hx, hy, hz))), sink=0.5)
            ellipsoid(geo, c + Vector((0, -P["toe_r"] * 0.75, -P["toe_r"] * 0.55)), P["toe_r"] * 0.55, (1.0, 0.8, 0.5),
                      "Moss", bone=auto(Vector((hx, hy, hz))), seg=6, rings=4, smooth=False)

    # --- face
    P["face"](geo, rng, Hc, RH)

    # --- crown and spine ridge (bible p10/p11): single tall hexagonal crystals, a midline row whose length peaks over
    # the shoulder hump, with side pairs that splay outward (the fan seen from the front), shrinking down the back to
    # the tail; crystal roots sink into the plates (no hidden base caps)
    ncr = 0
    for (cy, L, nfan) in P["crown"]:
        row = [(0.0, 0.0, 1.0)]
        for (xo, tilt, lf) in P["fans"][:nfan]:
            row += [(xo, tilt, lf), (-xo, -tilt, lf)]
        for (xo, tilt, lf) in row:
            p, n = ray(bvh, (xo, cy + rng.uniform(-0.03, 0.03), 20), (0, 0, -1))
            if p is None:
                continue
            t = math.radians(tilt + rng.uniform(-4, 4))
            lean = math.radians(P["crown_lean"] + rng.uniform(-4, 4))
            d = Vector((math.sin(t), math.sin(lean), math.cos(t) * math.cos(lean))).normalized()
            d = (d + n * 0.12).normalized()
            ln = L * lf * rng.uniform(0.9, 1.05)
            crystal(geo, p, d, ln, max(0.03, P["crystal_r"] * ln), rng, bone=auto(p), cap=False)
            ncr += 1
    print("CROWN", name, ncr, "crystals")
    for (cx, cy, ln) in P["spikes"]:
        p, n = ray(bvh, (cx, cy, 20), (0, 0, -1))
        if p is not None:
            spike(geo, p, (n + Vector((0, 0.35, 0.6))).normalized(), ln, ln * 0.42, "Slate", bone=auto(p), rng=rng)
    # tail-tip crystal
    tp, td = Vector(P["tail_tip"][0]), Vector(P["tail_tip"][1])
    cluster(geo, tp, td, P["tail_tip"][2], 4, rng, bone="Tail3", spread=0.5)

    bm.free()
    ob, rig, s = finish(name, geo, P["bones"], target_h)
    PIVOT_Y[0] = P["foot"][1]
    key_clips(rig, creature_clips(name), s)
    return ob, rig


def creature_v3(name, P, target_h, geo, bm, bvh, rng, Hc, RH, face_zone, decal_zone, body_slot):
    """v3 surface: the metaball body is only the dark core; each anatomical mass is its own few large boulders
    (P["boulders"]), the core glows only in the gaps between boulders of one mass and stays dark between masses."""
    BL = P["boulders"](bvh)
    rocks = []
    for b in BL:
        if b["slot"] == "Belly":
            continue
        M = frame(Vector(b.get("up", (0, 0, 1))), 0.0)
        rocks.append((np.array(M.transposed()), np.array(b["c"]), max(b["r"][0], b["r"][1]), b["r"][2]))
    RT = np.stack([r[0] for r in rocks])
    RC = np.stack([r[1] for r in rocks])
    RS = np.array([[r[2], r[2], r[3]] for r in rocks])

    def slot3(c, n):
        s = body_slot(c, n)
        if s != "Glow":
            return s
        q = np.einsum("kij,kj->ki", RT, np.array(tuple(c)) - RC) / RS
        d = np.sort(np.sqrt((q * q).sum(1)))
        return "Glow" if d[1] < P["crack_k"] else "Skin"

    add_body(geo, bm, slot3, lambda c, n: True, decal_zone)
    for b in BL:
        near = [o for o in BL if o is not b and o["slot"] != "Belly" and o["bone"] != "Head"
                and (Vector(o["c"]) - Vector(b["c"])).length < 1.4 * (max(o["r"]) + max(b["r"]))]
        boulder(geo, b, rng, core_bvh=bvh, rim_d=P["rim_d"], near=near)
    print("BOULDERS", name, len(BL))
    P["face"](geo, rng, Hc, RH)
    ncr = 0
    for (x, y, d, L, bone) in P["crystals"]:
        p, n = ray(bvh, (x, y, 20), (0, 0, -1))
        if p is None:           # beside the thin core (shoulder clusters): root on the nearest core point
            p = bvh.find_nearest(Vector((x, y, 3.2)))[0]
        crystal3(geo, p, Vector(d), L, P["crystal_r"] * L, rng, bone=bone or auto(p))
        ncr += 1
    tp, td, tl = P["tail_tip"]
    for k, (dx, dy, f) in enumerate(((0, 0, 1.0), (0.4, 0.3, 0.7))):
        dd = (Vector(td) + Vector((dx, dy, 0))).normalized()
        crystal3(geo, Vector(tp), dd, tl * f, P["crystal_r"] * tl * f, rng, bone="Tail3")
        ncr += 1
    print("CRYSTALS", name, ncr)
    bm.free()
    ob, rig, s = finish(name, geo, P["bones"], target_h)
    PIVOT_Y[0] = P["foot"][1]
    key_clips(rig, creature_clips(name), s)
    return ob, rig


def face_kit(eye_r, eye_off, brow, smile, open_jaw, teeth):
    """Small expressive face (p11: yellow eyes with black pupils, flat snout, smug mouth)."""

    def build(geo, rng, Hc, RH):
        mz = Hc + Vector(eye_off["muzzle"])
        # eyes, lids, brows' ink and the mouth are PAINTED (kaiju_face.py decal); geometry keeps the brow plates,
        # cheek spikes, nose, jaw and mouth interior
        for sx in (1, -1):
            e = Hc + Vector((sx * eye_off["x"], eye_off["y"], eye_off["z"]))
            # brow plate: inner end low, outer end high (adult: smug / determined); the hatchling has none (cute read)
            if brow:
                bw, bh = eye_r * brow[0], eye_r * brow[1]
                c = e + Vector((sx * 0.1 * eye_r, -0.35 * eye_r, eye_r * brow[2]))
                tilt = Matrix.Rotation(-sx * math.radians(brow[3]), 3, "Y")
                # a heavy arched ridge, thin at the inner end, thick and raised mid-way (not a brick)
                prof = [(-1.0, 0.45, 0.0), (-0.35, 0.95, 0.25), (0.35, 1.0, 0.3), (1.0, 0.7, 0.05)]
                pts = [c + tilt @ Vector((sx * dx * bw, dy * eye_r * (0.9 if dy < 0 else 0.6) * (0.75 + 0.25 * hgt),
                                          (dz * hgt + arch) * bh))
                       for dx, hgt, arch in prof for dy in (-1, 1) for dz in (-1, 1)]
                V, F, _ = hull(pts)
                geo.add(V, F, SLOT["Slate"], "Head")
            # cheek spike
            spike(geo, Hc + Vector((sx * RH * 0.92, eye_off["y"] * 0.5, -0.12 * RH)),
                  Vector((sx * 1.0, 0.45, 0.25)), RH * 0.42, RH * 0.17, "Slate", bone="Head", sides=4, rng=rng)
        # nose: dark flat wedge at the snout tip
        nose = mz + Vector((0, -eye_off["nose_fwd"], eye_off["nose_up"]))
        ellipsoid(geo, nose, eye_r * 0.75, (1.5, 0.7, 0.7), "Pupil", bone="Head", seg=8, rings=5, smooth=False)
        # jaw (own bone) with mouth interior behind it, teeth on the upper lip
        jc = mz + Vector(eye_off["jaw"])
        hinge = Hc + Vector((0, -0.1 * RH, -0.45 * RH))
        R = Matrix.Rotation(math.radians(open_jaw), 3, "X")
        js = eye_off["jaw_size"]
        ellipsoid(geo, hinge + R @ (jc - hinge), 1.0, (js[0], js[1] * 1.15, js[2] * 1.3), "Belly", bone="Jaw", seg=10, rings=6,
                  smooth=False, rot=R)
        ellipsoid(geo, mz + Vector((0, eye_off.get("mouth_back", 0.05) * RH * 2 + 0.3 * RH, -0.02 * RH)), RH * 0.26,
                  (1.0, 0.9, 0.5), "Mouth", bone="Head", seg=10, rings=6)   # hidden by the closed jaw
        for k in range(teeth):
            u = (k - (teeth - 1) / 2) / max(1, teeth - 1)
            tp = mz + Vector((u * smile[0] * 1.3, -smile[1] * 0.85, smile[2] - 0.02 * RH))
            spike(geo, tp, (0, -0.2, -1), eye_r * 0.7, eye_r * 0.28, "Teeth", bone="Head", sides=4, rng=rng)

    return build


# ------------------------------------------------------------------------------------------------ adult (p10 / p11)
def adult_params():
    """Proportions measured off p11 front/profile (total height incl. crest = 1): width 0.8, shoulder top 0.68,
    eyes 0.73, head top 0.79, fist centres +-0.24, fist half-width 0.13, belly half-width 0.12. Design height 4.2."""
    U = 1.0
    T = -1  # tail curls to the creature's right (p11 rear: tail tip to screen right)
    SX, FX, FR, HY, HZ = TUNE["sh_x"], TUNE["fist_x"], TUNE["fist_r"], TUNE["head_dy"], TUNE["head_dz"]
    HIPZ, THX, FY, TK = TUNE["hip_z"], TUNE["thigh_x"], TUNE["fist_y"], TUNE["tail_k"]

    def ty(y):          # tail length scale about its root
        return 1.45 + (y - 1.45) * TK
    FACE.RECT["Adult"] = (FACE.ADULT_RECT[0], FACE.ADULT_RECT[1] + HZ, FACE.ADULT_RECT[2], FACE.ADULT_RECT[3])
    balls = [
        ((0, -0.1, 2.6), 0.8, (1.12, 1.0, 0.78)),      # shoulder hump (crest base)
        ((0, 0.55, 2.05), 0.68, (1.0, 1.15, 0.8)),     # back sloping down to the hips (p11 profile)
        ((0, -0.5, 2.05), 0.76, (1.0, 0.82, 1.0)),     # chest
        ((0, 0.0, 1.5), 0.7, (1.0, 1.1, 0.9)),         # belly
        ((0, 1.0, HIPZ), 0.6, (1.1, 1.0, 0.85)),       # hips (small, high enough to leave the arch between the legs)
        ((0, -0.72 + HY / 2, 2.88 + HZ / 2), 0.46),                # neck
        ((0, -1.02 + HY, 3.25 + HZ), 0.43, (1.08, 1.0, 0.9)),      # small head low between the shoulders
        ((0, -1.29 + HY, 3.0 + HZ), 0.27, (1.25, 1.0, 0.74)),      # v3: short blunt muzzle, below the eyes
    ]
    for sx in (1, -1):
        balls += [((sx * SX, -0.42, 2.24), 0.62),                     # shoulders: far wider than the hips
                  ((sx * FX, FY, 0.52), FR, (1.0, 1.05, 0.92)),     # boulder fists on the ground, spread (A-frame)
                  ((sx * THX, 1.1, 1.0), 0.46),                        # thighs
                  ((sx * (THX + 0.04), 0.95, 0.38), 0.4, (1.0, 1.25, 0.75))]   # short hind feet
    limbs = []
    for sx in (1, -1):
        MX = (SX + FX) / 2 + 0.05
        MY = (FY - 0.55) / 2
        limbs += [((sx * (SX + 0.04), -0.48, 2.1), (sx * MX, MY, 1.55), 0.48, 0.5),
                  ((sx * MX, MY, 1.55), (sx * (FX - 0.02), FY + 0.03, 0.75), 0.52, 0.6 * FR / 0.6)]
    # tail: back and up, curling to the side at hip height (kept out of the arch between the legs, p11 front/rear)
    limbs += [((0, 1.45, 1.25), (0, ty(1.9), 1.02), 0.4, 0.3), ((0, ty(1.9), 1.02), (T * 0.32, ty(2.2), 0.92), 0.3, 0.22),
              ((T * 0.32, ty(2.2), 0.92), (T * 0.6, ty(2.24), 1.16), 0.22, 0.15)]
    bones = [
        ("Root", (0, 0, 0), (0, 0.5, 0), None),
        ("Hips", (0, 1.0, 1.25), (0, 0.3, 1.6), "Root"),
        ("Spine", (0, 0.3, 1.6), (0, -0.45, 2.1), "Hips"),
        ("Chest", (0, -0.45, 2.1), (0, -0.68, 2.72), "Spine"),
        ("Neck", (0, -0.68, 2.72), (0, -0.94 + HY, 3.2 + HZ), "Chest"),
        ("Head", (0, -0.94 + HY, 3.2 + HZ), (0, -1.47 + HY, 3.23 + HZ), "Neck"),
        ("Jaw", (0, -1.02 + HY, 2.95 + HZ), (0, -1.5 + HY, 2.91 + HZ), "Head"),
        ("Tail1", (0, 1.4, 1.25), (0, ty(1.9), 1.02), "Hips"),
        ("Tail2", (0, ty(1.9), 1.02), (T * 0.32, ty(2.2), 0.92), "Tail1"),
        ("Tail3", (T * 0.32, ty(2.2), 0.92), (T * 0.6, ty(2.24), 1.16), "Tail2"),
    ]
    for sx, sfx in ((1, "_L"), (-1, "_R")):
        MX, MY = (SX + FX) / 2 + 0.05, (FY - 0.55) / 2
        bones += [("Arm" + sfx, (sx * SX, -0.42, 2.28), (sx * MX, MY, 1.55), "Chest"),
                  ("Forearm" + sfx, (sx * MX, MY, 1.55), (sx * (FX - 0.02), FY + 0.03, 0.75), "Arm" + sfx),
                  ("Fist" + sfx, (sx * (FX - 0.02), FY + 0.03, 0.75), (sx * FX, FY - 0.05, 0.1), "Forearm" + sfx),
                  ("Thigh" + sfx, (sx * (THX - 0.02), 1.1, 1.2), (sx * (THX + 0.02), 1.0, 0.7), "Hips"),
                  ("Foot" + sfx, (sx * (THX + 0.02), 1.0, 0.7), (sx * (THX + 0.04), 0.7, 0.12), "Thigh" + sfx)]

    # ---- v3: separate masses, few large boulders (2026-11-12 review). Positions follow the v2 fitted skeleton.
    CORE = 0.62             # the metaball body shrinks to a dark core the boulders stand proud of
    balls = [(c, r * (CORE if abs(c[0]) > 0.3 else 0.9)) + tuple(rest) for c, r, *rest in balls]
    limbs = [(a, b, r0 * CORE, r1 * CORE) if abs(a[0]) > 0.3 or abs(b[0]) > 0.3 else (a, b, r0, r1)
             for a, b, r0, r1 in limbs]
    MX, MY = (SX + FX) / 2 + 0.05, (FY - 0.55) / 2

    def boulders(bvh):
        B = []

        def add(c, r, bone, slot="Slate", moss=0.0, up=(0, 0, 1), n=16, rim=True, flat=0.6, spin=None):
            if n > 12 and bone.startswith(("Arm", "Forearm", "Fist")):
                r = tuple(x * TUNE["arm_k"] for x in r)       # forelimb boulders: the bible's forearms are huge
            elif slot != "Belly" and bone not in ("Head",) and n > 12:
                r = tuple(x * TUNE["rock_k"] for x in r)
            n = min(n, TUNE["facets"]) if slot != "Belly" else n
            B.append({"c": tuple(c), "r": r, "bone": bone, "slot": slot, "moss": moss, "up": up, "n": n, "rim": rim,
                      "flat": flat, "spin": spin})
        for sx, sf in ((1, "_L"), (-1, "_R")):
            # shoulder: the big rounded mass, moss draped over its top; a second one behind it toward the hump
            add((sx * (SX + 0.06), -0.42, 2.44), (0.6, 0.58, 0.5), "Arm" + sf, moss=0.8, up=(sx * 0.3, 0, 1))
            add((sx * (SX - 0.3), 0.08, 2.62), (0.46, 0.46, 0.4), "Chest", moss=0.6, up=(sx * 0.3, 0.1, 1),
                slot="SlateLight" if sx > 0 else "Slate")
            # upper arm, angled down and out to the elbow
            add((sx * (SX + 0.18), -0.64, 1.94), (0.5, 0.48, 0.46), "Arm" + sf, up=(sx * 0.6, -0.3, 0.6))
            # forearm column: elbow block (moss on top), a big mid block (pale on one arm), an inner block
            add((sx * (MX + 0.02), MY - 0.04, 1.5), (0.55, 0.52, 0.44), "Forearm" + sf, moss=0.55, up=(sx * 0.2, -0.4, 1))
            add((sx * (FX - 0.05), FY + 0.0, 1.04), (0.55, 0.52, 0.4), "Forearm" + sf, up=(sx * 0.15, -0.7, 0.7))
            add((sx * (MX - 0.24), MY + 0.22, 1.12), (0.38, 0.38, 0.36), "Forearm" + sf, up=(-sx * 0.6, 0.2, 0.6))
            # fist: a big pale front boulder, an outer boulder, three knuckles on the ground
            add((sx * FX, FY - 0.14, 0.56), (0.56, 0.46, 0.48), "Fist" + sf, slot="SlateLight",
                up=(0, -1, 0.35))
            add((sx * (FX + 0.22), FY + 0.16, 0.5), (0.4, 0.4, 0.42), "Fist" + sf, up=(sx * 1, 0, 0.4))
            for dx in (-0.3, 0.0, 0.3):
                add((sx * (FX + dx), FY - 0.46, 0.17), (0.19, 0.17, 0.17), "Fist" + sf, n=12, rim=False, up=(0, -0.5, 1))
            # hind leg: thigh, foot block, toes
            add((sx * (THX + 0.06), 1.05, 1.12), (0.42, 0.44, 0.42), "Thigh" + sf, moss=0.5)
            add((sx * (THX + 0.08), 0.9, 0.48), (0.4, 0.44, 0.34), "Foot" + sf, up=(0, -0.5, 1),
                slot="SlateLight" if sx < 0 else "Slate")
            for dx in (-0.18, 0.0, 0.18):
                add((sx * (THX + 0.08 + dx), 0.55, 0.14), (0.14, 0.13, 0.13), "Foot" + sf, n=12, rim=False)
            # flank and hip blocks
            add((sx * 0.48, 0.58, 2.28), (0.42, 0.44, 0.38), "Spine", moss=0.45, up=(sx * 0.45, 0.2, 1))
            add((sx * 0.44, 1.12, 1.7), (0.4, 0.4, 0.36), "Hips", up=(sx * 0.5, 0.4, 1))
            # cranium blocks on the head top
            add((sx * 0.24, -0.86 + HY, 3.58 + HZ), (0.19, 0.19, 0.14), "Head", moss=0.0, n=14, up=(sx * 0.4, 0.3, 1))
        # spine line: hump block behind the head, then down the back to the hips
        add((0, -0.22, 3.02), (0.5, 0.48, 0.38), "Chest", moss=0.5)
        add((0, 0.32, 2.8), (0.48, 0.46, 0.38), "Spine", moss=0.4, up=(0, 0.2, 1))
        add((0, 0.86, 2.32), (0.44, 0.44, 0.36), "Spine", up=(0, 0.4, 1))
        add((0, 1.36, 1.66), (0.36, 0.38, 0.32), "Hips", up=(0, 0.7, 0.7))
        # tail: three shrinking blocks
        add((0, ty(1.75), 1.16), (0.32, 0.34, 0.28), "Tail1", up=(0, 0.4, 1))
        add((T * 0.18, ty(2.08), 0.98), (0.26, 0.27, 0.22), "Tail2", up=(T * 0.3, 0.3, 1))
        add((T * 0.42, ty(2.22), 1.02), (0.2, 0.2, 0.17), "Tail3", up=(T * 0.4, 0.2, 1), n=12)
        # heavy brow boulder over the painted eyes (stays above them), moss on its crown
        add((0, -1.25 + HY, 3.49 + HZ), (0.5, 0.26, 0.15), "Head", moss=0.3, up=(0, -0.6, 1), n=18, spin=0.0)
        # tan chest: two big pectoral plates, then three rows of belly segments (ray-cast onto the core)
        tiles = [(-0.29, 2.3, 0.33, 0.25), (0.29, 2.3, 0.33, 0.25)]
        tiles += [(sx * 0.17, z, 0.19, 0.13) for z in (1.92, 1.62, 1.34) for sx in (1, -1)]
        for (x, z, w, h) in tiles:
            p, n = ray(bvh, (x, -6, z), (0, 1, 0))
            if p is not None:
                add(p - n * 0.02, (w, h, 0.16), auto(p), slot="Belly", up=tuple(n), n=14, rim=False, flat=0.35,
                    spin=0.0)
        return B

    CL = TUNE["cry_k"]
    crystals = [(x, y, d, L * CL, b) for x, y, d, L, b in [
        (0.0, -0.3, (0, 0.12, 1), 1.6, None), (0.24, -0.18, (0.38, 0.18, 1), 1.18, None),
        (-0.24, -0.18, (-0.38, 0.18, 1), 1.18, None),
        (0.74, 0.02, (0.6, 0.15, 1), 1.0, None), (0.92, 0.24, (0.95, 0.3, 0.8), 0.72, None),
        (-0.74, 0.02, (-0.6, 0.15, 1), 1.0, None), (-0.92, 0.24, (-0.95, 0.3, 0.8), 0.72, None),
        (0.12, 0.58, (0.15, 0.4, 1), 1.0, None), (-0.15, 0.9, (-0.2, 0.5, 1), 0.8, None),
        (0.0, 1.22, (0, 0.7, 0.8), 0.62, None)]]

    def belly(c, n):
        return c.y < -0.25 and abs(c.x) < 0.56 and 0.95 < c.z < 2.62 and n.y < -0.3

    def dmin(p, n):
        ax = abs(p.x)
        if ax > 0.72 and p.z < 1.7:
            return 0.44         # forearms / fists: big boulders
        if ax > 0.68 and p.y < 0.1:
            return 0.44         # shoulders, upper arms
        if (p - Vector((0, -1.02 + HY, 3.25 + HZ))).length < 0.6:
            return 0.24         # head
        if p.y > 1.7:
            return 0.28         # tail
        if p.y > 0.7 and p.z < 1.4:
            return 0.32         # hind legs
        return 0.4

    def light_frac(p):
        return 0.4 if (abs(p.x) > 0.72 and p.z < 1.7) else 0.18

    def moss(p, n):
        return 0.6 if (p.z > 0.7 and n.z > 0.2) else 0.0

    # crown: (y, midline length, side pairs); fans: (x offset, outward tilt deg, length fraction)
    CK, FK = TUNE["crown_k"], TUNE["fan_k"]
    crown = [(-0.8, 0.7, 1), (-0.56, 1.12, 2), (-0.31, 1.42, 2), (-0.06, 1.52, 2), (0.19, 1.46, 2), (0.44, 1.32, 2),
             (0.69, 1.08, 2), (0.94, 0.8, 1), (1.18, 0.58, 1), (1.42, 0.44, 0), (1.66, 0.36, 0), (1.9, 0.3, 0)]
    crown = [(ty(y) if y > 1.45 else y, L * CK, f) for y, L, f in crown]
    fans = [(0.19, 17 * FK, 0.8), (0.38, 34 * FK, 0.64)]
    spikes = [(0.52, -0.2, 0.32), (-0.54, 0.05, 0.34), (0.5, 0.35, 0.3), (-0.48, -0.45, 0.28), (0.3, 0.8, 0.26), (-0.28, 1.05, 0.24)]
    belly_tiles = [(-0.2, 2.3, 0.2, 0.15), (0.2, 2.3, 0.2, 0.15)]                     # pecs
    for z, half in [(2.0, 0.27), (1.74, 0.26), (1.48, 0.24), (1.22, 0.2)]:              # abdominal segments
        belly_tiles += [(-half * 0.52, z, half * 0.47, 0.11), (half * 0.52, z, half * 0.47, 0.11)]
    face = face_kit(eye_r=0.1, eye_off={"x": 0.21, "y": -0.34, "z": 0.09, "muzzle": (0, -0.38, -0.25),
                                         "nose_fwd": 0.3, "nose_up": 0.07, "jaw": (0, -0.02, -0.15),
                                         "jaw_size": (0.17, 0.17, 0.09), "mouth_back": 0.12},
                    brow=None, smile=(0.13, 0.31, -0.06, 0.05, 0.03), open_jaw=0, teeth=0)
    return {
        "unit": U, "balls": balls, "limbs": limbs, "voxel": 0.05, "body_tris": 2600, "bones": bones,
        "gap": 0.024, "crack_w": 0.02, "chamfer_m": 0.05, "design_h": 4.2,
        "head": (0, -1.02 + HY, 3.25 + HZ), "head_r": 0.43, "muzzle": (0, -1.29 + HY, 3.0 + HZ), "head_clear": 1.12, "belly": belly, "dmin": dmin,
        "samples": 16000, "plate_r": 1.0, "plate_thick": 0.4, "light_frac": light_frac, "moss": moss, "min_plate_z": 0.15,
        "belly_tiles": belly_tiles, "fist": (FX, FY, 0.52), "fist_plate_z": 0.85, "fist_plate_r": 0.44 * FR / 0.6,
        "knuckles": (-0.33, -0.11, 0.11, 0.33), "knuckle_fwd": 0.42, "knuckle_r": 0.15,
        "foot": (0.64, 0.95, 0.38), "toes": (-0.2, 0.0, 0.2), "toe_fwd": 0.42, "toe_r": 0.12,
        "face": face, "crown": crown, "fans": fans, "crown_lean": 10.0, "crystal_r": 0.18, "spikes": spikes,
        "tail_tip": ((T * 0.6, ty(2.24), 1.16), (T * 0.35, 0.1, 0.9), 0.46),
        "boulders": boulders, "crystals": crystals, "crack_k": TUNE["crack_k"], "rim_d": TUNE["rim_d"],
    }


# ------------------------------------------------------------------------------------------------ hatchling (p10)
def hatch_params():
    U = 1.0
    balls = [
        ((0, 0.15, 1.0), 0.95, (1.0, 1.25, 0.82)),     # domed turtle shell body
        ((0, -0.5, 1.0), 0.66, (1.05, 0.85, 1.0)),     # chest
        ((0.72, -0.55, 0.95), 0.42), ((-0.72, -0.55, 0.95), 0.42),   # shoulders
        ((0.84, -0.8, 0.34), 0.47, (1.0, 1.0, 0.85)), ((-0.84, -0.8, 0.34), 0.47, (1.0, 1.0, 0.85)),  # big front feet
        ((0.58, 0.72, 0.6), 0.4), ((-0.58, 0.72, 0.6), 0.4),
        ((0.62, 0.62, 0.22), 0.34, (1.0, 1.2, 0.7)), ((-0.62, 0.62, 0.22), 0.34, (1.0, 1.2, 0.7)),
        ((0, -1.1, 1.75), 0.56, (1.0, 0.95, 0.92)),    # big round head, raised
        ((0, -1.5, 1.56), 0.33, (1.2, 1.0, 0.8)),      # muzzle
        ((0, -0.75, 1.45), 0.45),                      # neck
    ]
    limbs = []
    for sx in (1, -1):
        limbs += [((sx * 0.75, -0.55, 0.95), (sx * 0.84, -0.78, 0.36), 0.38, 0.42)]
    # tail sticks out behind (bible p10 hatchling), crystal-tipped
    limbs += [((0, 1.0, 0.75), (0, 1.6, 0.42), 0.34, 0.24), ((0, 1.6, 0.42), (0.15, 2.3, 0.26), 0.24, 0.14)]
    bones = [
        ("Root", (0, 0, 0), (0, 0.5, 0), None),
        ("Hips", (0, 0.65, 0.8), (0, 0.0, 1.0), "Root"),
        ("Spine", (0, 0.0, 1.0), (0, -0.45, 1.1), "Hips"),
        ("Chest", (0, -0.45, 1.1), (0, -0.7, 1.3), "Spine"),
        ("Neck", (0, -0.7, 1.3), (0, -1.0, 1.68), "Chest"),
        ("Head", (0, -1.0, 1.68), (0, -1.65, 1.73), "Neck"),
        ("Jaw", (0, -1.15, 1.42), (0, -1.65, 1.37), "Head"),
        ("Tail1", (0, 0.95, 0.78), (0, 1.6, 0.42), "Hips"),
        ("Tail2", (0, 1.6, 0.42), (0.15, 2.3, 0.26), "Tail1"),
        ("Tail3", (0.15, 2.3, 0.26), (0.22, 2.5, 0.22), "Tail2"),
    ]
    for sx, sfx in ((1, "_L"), (-1, "_R")):
        bones += [("Arm" + sfx, (sx * 0.72, -0.52, 1.05), (sx * 0.79, -0.66, 0.7), "Chest"),
                  ("Forearm" + sfx, (sx * 0.79, -0.66, 0.7), (sx * 0.84, -0.78, 0.38), "Arm" + sfx),
                  ("Fist" + sfx, (sx * 0.84, -0.78, 0.38), (sx * 0.84, -0.84, 0.05), "Forearm" + sfx),
                  ("Thigh" + sfx, (sx * 0.56, 0.72, 0.75), (sx * 0.6, 0.65, 0.4), "Hips"),
                  ("Foot" + sfx, (sx * 0.6, 0.65, 0.4), (sx * 0.62, 0.4, 0.08), "Thigh" + sfx)]

    # ---- v3 (same kit as the adult): a thin dark core under few large boulders; the shell is 6 big plates
    balls = [(c, r * (0.74 if abs(c[0]) > 0.3 else 0.86)) + tuple(rest) for c, r, *rest in balls]
    limbs = [(a, b, r0 * 0.74, r1 * 0.74) if abs(a[0]) > 0.3 else (a, b, r0 * 0.86, r1 * 0.86) for a, b, r0, r1 in limbs]

    def boulders(bvh):
        B = []

        def add(c, r, bone, slot="Slate", moss=0.0, up=(0, 0, 1), n=11, rim=True, flat=0.6, spin=None):
            B.append({"c": tuple(c), "r": r, "bone": bone, "slot": slot, "moss": moss, "up": up, "n": n, "rim": rim,
                      "flat": flat, "spin": spin})
        add((0, 0.1, 1.62), (0.5, 0.55, 0.32), "Spine", moss=0.7)                     # shell crown plate
        add((0, -0.45, 1.52), (0.42, 0.4, 0.3), "Chest", moss=0.6, up=(0, -0.3, 1))
        add((0, 0.75, 1.36), (0.42, 0.42, 0.3), "Hips", moss=0.5, up=(0, 0.4, 1))
        for sx, sf in ((1, "_L"), (-1, "_R")):
            add((sx * 0.5, -0.08, 1.42), (0.42, 0.45, 0.32), "Spine", moss=0.6, up=(sx * 0.5, 0, 1))
            add((sx * 0.48, 0.55, 1.3), (0.4, 0.42, 0.3), "Hips", moss=0.45, up=(sx * 0.5, 0.3, 1),
                slot="SlateLight" if sx > 0 else "Slate")
            add((sx * 0.78, 0.22, 0.98), (0.34, 0.4, 0.3), "Spine", up=(sx, 0, 0.4))   # shell rim
            add((sx * 0.8, -0.6, 0.86), (0.34, 0.32, 0.32), "Arm" + sf, moss=0.45)    # front leg
            add((sx * 0.88, -0.86, 0.32), (0.48, 0.42, 0.34), "Fist" + sf, slot="SlateLight", up=(0, -0.6, 1))
            for dx in (-0.16, 0.16):
                add((sx * (0.86 + dx), -1.2, 0.1), (0.12, 0.11, 0.1), "Fist" + sf, n=10, rim=False)
            add((sx * 0.62, 0.78, 0.62), (0.34, 0.36, 0.32), "Thigh" + sf)
            add((sx * 0.64, 0.62, 0.22), (0.32, 0.36, 0.24), "Foot" + sf, up=(0, -0.5, 1))
            add((sx * 0.22, -0.92, 2.12), (0.15, 0.15, 0.1), "Head", moss=0.5, n=10, up=(sx * 0.5, 0.4, 1))
        add((0, 1.5, 0.56), (0.32, 0.34, 0.27), "Tail1", up=(0, 0.5, 1))
        add((0.1, 1.98, 0.38), (0.24, 0.26, 0.2), "Tail2", up=(0, 0.5, 1), n=10)
        add((0.18, 2.36, 0.28), (0.17, 0.18, 0.14), "Tail3", up=(0, 0.6, 1), n=10)
        for x in (-0.17, 0.17):
            for z in (1.2, 0.98, 0.76):
                p, n = ray(bvh, (x, -6, z), (0, 1, 0))
                if p is not None:
                    add(p - n * 0.02, (0.16, 0.1, 0.09), auto(p), slot="Belly", up=tuple(n), n=12, rim=False, flat=0.35,
                        spin=0.0)
        return B

    crystals = [(0.0, 0.25, (0, 0.35, 1), 0.8, None), (0.25, 0.5, (0.4, 0.45, 1), 0.66, None),
                (-0.25, 0.5, (-0.4, 0.45, 1), 0.66, None), (0.0, 0.8, (0, 0.6, 1), 0.66, None),
                (0.2, 1.05, (0.3, 0.7, 1), 0.52, None), (-0.2, 1.05, (-0.3, 0.7, 1), 0.52, None),
                (0.0, 1.3, (0, 0.9, 0.8), 0.45, None)]

    def belly(c, n):
        return (c.y < -0.35 and abs(c.x) < 0.5 and 0.4 < c.z < 1.35 and n.y < -0.3) or (n.z < -0.55 and abs(c.x) < 0.6)

    def dmin(p, n):
        if (p - Vector((0, -1.1, 1.75))).length < 0.75:
            return 0.24
        if abs(p.x) > 0.6 and p.y < -0.2:
            return 0.34
        return 0.42

    def light_frac(p):
        return 0.25

    def moss(p, n):
        return 0.7 if (p.z > 0.6 and n.z > 0.25) else 0.0

    crown = [(-0.62, 0.55, 1), (-0.36, 0.95, 2), (-0.08, 1.2, 2), (0.2, 1.22, 2), (0.48, 1.0, 2), (0.76, 0.62, 1),
             (1.05, 0.42, 1), (1.4, 0.32, 0), (1.75, 0.3, 0), (2.05, 0.28, 0)]
    fans = [(0.24, 18, 0.82), (0.48, 36, 0.64)]
    face = face_kit(eye_r=0.15, eye_off={"x": 0.24, "y": -0.43, "z": 0.1, "muzzle": (0, -0.4, -0.16),
                                         "nose_fwd": 0.3, "nose_up": 0.1, "jaw": (0, -0.04, -0.2),
                                         "jaw_size": (0.24, 0.2, 0.08)},
                    brow=None, smile=(0.18, 0.31, -0.07, 0.07, 0.0), open_jaw=6, teeth=0)
    belly_tiles = [(x, z, 0.16, 0.09) for z in (1.2, 1.0, 0.8, 0.6) for x in (-0.17, 0.17)]
    return {
        "unit": U, "balls": balls, "limbs": limbs, "voxel": 0.045, "body_tris": 2000, "bones": bones,
        "gap": 0.022, "crack_w": 0.02, "chamfer_m": 0.035, "design_h": 2.6,
        "head": (0, -1.1, 1.75), "head_r": 0.56, "muzzle": (0, -1.5, 1.56), "head_clear": 1.1, "belly": belly, "dmin": dmin,
        "samples": 12000, "plate_r": 1.0, "plate_thick": 0.4, "light_frac": light_frac, "moss": moss, "min_plate_z": 0.1,
        "belly_tiles": belly_tiles, "fist": (0.84, -0.8, 0.34), "fist_plate_z": 0.45, "fist_plate_r": 0.26,
        "knuckles": (-0.18, 0.0, 0.18), "knuckle_fwd": 0.36, "knuckle_r": 0.11,
        "foot": (0.62, 0.62, 0.22), "toes": (-0.14, 0.0, 0.14), "toe_fwd": 0.34, "toe_r": 0.1,
        "face": face, "crown": crown, "fans": fans, "crown_lean": 8.0, "crystal_r": 0.18,
        "spikes": [(0.62, 0.25, 0.26), (-0.62, -0.1, 0.26)],
        "tail_tip": ((0.2, 2.4, 0.25), (0.3, 0.6, 0.6), 0.4),
        "boulders": boulders, "crystals": crystals, "crack_k": TUNE["crack_k"], "rim_d": TUNE["rim_d"] * 0.6,
    }


# ------------------------------------------------------------------------------------------------ egg (p10)
def build_egg(seed):
    rng = random.Random(seed)
    geo = Geo()
    H, R = 1.0, 0.39
    # ovoid core: glowing lime underneath; plates leave seams and a bright window on the front
    bm = bmesh.new()
    seg, rings = 28, 20
    ring_v = []
    for j in range(1, rings):
        th = math.pi * j / rings
        ring_v.append([bm.verts.new((math.sin(th) * math.cos(math.tau * i / seg), math.sin(th) * math.sin(math.tau * i / seg),
                                     math.cos(th))) for i in range(seg)])
    top, bot = bm.verts.new((0, 0, 1)), bm.verts.new((0, 0, -1))
    for j in range(rings - 2):
        for i in range(seg):
            a, b = ring_v[j][i], ring_v[j][(i + 1) % seg]
            bm.faces.new((a, ring_v[j + 1][i], ring_v[j + 1][(i + 1) % seg], b))
    for i in range(seg):
        bm.faces.new((top, ring_v[0][i], ring_v[0][(i + 1) % seg]))
        bm.faces.new((bot, ring_v[-1][(i + 1) % seg], ring_v[-1][i]))
    for v in bm.verts:
        z = v.co.z                     # -1..1
        k = 1.0 - 0.18 * z             # wider low, narrower high
        v.co = Vector((v.co.x * R * k, v.co.y * R * k, (z + 1) * 0.5 * H))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.normal_update()
    add_body(geo, bm, lambda c, n: "Glow", lambda c, n: True)
    window = Vector((0.0, -R, 0.45))
    CHAMFER[0] = 0.03 / (EGG_HEIGHT / H)

    def dmin(p, n):
        if (p - window).length < 0.17:
            return None         # the glowing core window on the front (p10)
        return 0.2

    samples = surface_samples(bm, 6000, rng)
    picks = poisson(samples, dmin, rng)
    bvh = BVHTree.FromBMesh(bm)
    voronoi_plates(geo, bvh, picks, 0.026, lambda p, n, d: d * 0.36,
                   lambda p, n, d: ("SlateLight" if rng.random() < 0.25 else "Slate", False, "Shell"), rng,
                   lambda p, n, d: d * 1.45, sink=0.5, min_edge=0.025)
    # lime ground glow: a flat ragged splash on the ground, part of the shell mesh (bible p10 egg)
    ring, k = [], 28
    for i in range(k):
        a = math.tau * i / k
        rr = R * (1.75 + 0.35 * math.sin(3 * a + 0.7) + rng.uniform(-0.15, 0.15))
        ring.append(Vector((math.cos(a) * rr, math.sin(a) * rr * 0.8, 0.0)))
    inner = [Vector((v.x * 0.55, v.y * 0.55, 0.004)) for v in ring]
    V = ring + inner + [Vector((0, 0, 0.006))]
    F = [[i, (i + 1) % k, k + (i + 1) % k, k + i] for i in range(k)] + [[k + i, k + (i + 1) % k, 2 * k] for i in range(k)]
    geo.add(V, F, [SLOT["Crystal"]] * k + [SLOT["Glow"]] * k, "Root")
    bm.free()
    bones = [("Root", (0, 0, 0), (0, 0.3, 0), None), ("Shell", (0, 0, 0.02), (0, 0, H), "Root")]
    ob, rig, s = finish("Egg", geo, bones, EGG_HEIGHT)
    key_clips(rig, egg_clips(), s)
    return ob, rig


# ------------------------------------------------------------------------------------------------ props (metres)
def prop_mesh(name, stage, geo):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in geo.V], [], [list(f) for f in geo.F])
    me.materials.append(MAT)
    me.validate(clean_customdata=False)
    me.update()
    paint(me, stage, geo)
    face_uvs(me, stage, geo)
    ob = bpy.data.objects.new(name, me)
    COLL.objects.link(ob)
    ob["cm_stage"] = name
    return ob


def build_shards(seed):
    """Berserk floating shards (bible p10): 12 broken crystal shards orbiting the adult, 1.4-4.6 m up, 1.9-2.7 m out.
    Team glow (baked in the adult's Azure default, so the berserk magenta swap applies the same way). A separate
    rigid prop: the game parents it to the Kaiju root and spins it only in the berserk state."""
    rng = random.Random(seed)
    geo = Geo()
    CHAMFER[0] = 0.04
    for i in range(12):
        a = math.tau * i / 12 + rng.uniform(-0.2, 0.2)
        r = rng.uniform(1.9, 2.7)
        c = Vector((math.cos(a) * r, math.sin(a) * r * 0.9 + 0.3, rng.uniform(1.4, 4.6)))
        d = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(0.3, 1.2))).normalized()
        ln = rng.uniform(0.35, 0.7)
        crystal(geo, c - d * ln * 0.5, d, ln, ln * 0.24, rng, bone=None, sides=5)
    return prop_mesh("Kaiju_BerserkShards", "Shards", geo)


def build_telegraph(seed, radius=5.0):
    """Slam telegraph spike ring (bible p10; brief: model the spike ring only, the red decal and its 2 Hz pulse are
    game side). 28 red spikes on the slam radius (config Kaiju.SlamRadius 5.0), leaning outward. Fixed colour."""
    rng = random.Random(seed)
    geo = Geo()
    n = 28
    for i in range(n):
        a = math.tau * i / n
        p = Vector((math.cos(a) * radius, math.sin(a) * radius, 0.0))
        out = Vector((math.cos(a), math.sin(a), 0.0))
        ln = 0.42 if i % 2 == 0 else 0.28
        d = (Vector((0, 0, 1)) + out * 0.45).normalized()
        sides = 4
        M = frame(d, a)
        base = p
        V = [base + M @ Vector((math.cos(math.tau * k / sides) * ln * 0.3, math.sin(math.tau * k / sides) * ln * 0.3, 0))
             for k in range(sides)]
        V.append(base + d * ln)
        F = [list(range(sides))[::-1]] + [[k, (k + 1) % sides, sides] for k in range(sides)]
        geo.add(V, F, SLOT["Telegraph"], shade=[0.92] * sides + [1.06])
    return prop_mesh("Kaiju_TelegraphSpikes", "Adult", geo)


def export_prop(ob):
    for o in bpy.data.objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    path = os.path.join(ART, ob.name + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"}, global_scale=1.0, apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=False,
        use_mesh_modifiers=False, mesh_smooth_type="FACE", use_tspace=False, colors_type="SRGB",
        prioritize_active_color=True, bake_anim=False, path_mode="STRIP", embed_textures=False)
    print("EXPORTED", path, "tris", sum(len(p.vertices) - 2 for p in ob.data.polygons))


# ------------------------------------------------------------------------------------------------ build + export
def export(stage, ob, rig):
    for o in bpy.data.objects:
        o.select_set(False)
    old = (ob.name, rig.name)
    rig.name, ob.name = "KaijuRig", "KaijuMesh"   # same node paths in every stage's FBX so clips line up
    ob.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    path = os.path.join(ART, "Kaiju_%s.fbx" % stage)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"},
        global_scale=1.0, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=False,
        use_mesh_modifiers=False, mesh_smooth_type="FACE", use_tspace=False, colors_type="SRGB",
        prioritize_active_color=True, add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X", use_armature_deform_only=False,
        bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=True, bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=1.0,
        path_mode="STRIP", embed_textures=False)
    rig.name, ob.name = old[1], old[0]
    print("EXPORTED", path)


def main():
    stages = []
    stages.append(("Egg",) + build_egg(1001))
    stages.append(("Hatchling",) + creature("Hatchling", hatch_params(), 2002, HATCH_HEIGHT))
    stages.append(("Adult",) + creature("Adult", adult_params(), 3003, ADULT_HEIGHT))
    xoff = {"Egg": -6.0, "Hatchling": -3.0, "Adult": 0.0}
    for stage, ob, rig in stages:
        tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
        h = max((rig.matrix_world @ ob.matrix_world @ Vector(c)).z for c in ob.bound_box)
        print("STAGE %s tris=%d bones=%d height=%.3f materials=%d" % (stage, tris, len(rig.data.bones), h, len(ob.data.materials)))
        assert tris <= TRI_CAP[stage], (stage, tris)
        export(stage, ob, rig)
    for prop in (build_shards(4004), build_telegraph(5005)):
        export_prop(prop)
    for stage, ob, rig in stages:      # spread out in the .blend for viewing; FBX roots stay at the origin
        rig.location.x = xoff[stage]
    blend = os.path.join(HERE, "Kaiju.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend, compress=True)
    print("SAVED", blend)


main()
