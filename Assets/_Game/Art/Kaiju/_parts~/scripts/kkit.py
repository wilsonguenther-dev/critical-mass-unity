# Shared kit for "rigid parts on the existing skeleton" characters (adapted from builder-parts-pilot/scripts/build.py).
# Every part is a closed scripted primitive bound 100% to ONE bone of the class's own game rig; the result is joined
# into ONE mesh object with one palette texture, a team mask, and a vertex colour "Col" (alpha = mask code / 8:
# 8 team, 6 glow, 0 plain) so it also fits the game's vertex-colour path.
import bpy, bmesh, math, os, json
from mathutils import Vector, Matrix

H = os.path.expanduser
ROOT = H("~/cm-art-trial/parts-kaiju")
import sys as _sys; _sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import klod as LH
GAME = H("~/cm-editor/Assets/_Game/Art/")

def V(*a): return Vector(a)
def lerp(a, b, t): return a + (b - a) * t
def hexrgb(h): return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))
def basis(x, y, z): return Matrix((x, y, z)).transposed()

# ---------------------------------------------------------------- palette
class Palette:
    """team = names swapped per team ({name: (azure, crimson)}). Mask codes in vertex alpha (code/8), as the game's
    CM/Toon vertex-colour path reads them: 8 team cloth, 6 team glow (names in team AND team_glow), 2 fixed (non-team) glow."""
    def __init__(self, pal, team, glow=(), team_glow=(), fixed_glow=(), bright=()):
        # bright (r2): fixed code-0 names kept at full value (no night dim), e.g. the Miner's lamp, which may not glow
        # inside the face box (ArtImportTests: no code 5/6/8 on the face) and must not take the team colour.
        self.pal = pal; self.team = team; self.team_glow = set(team_glow); self.fixed_glow = set(fixed_glow); self.bright = set(bright)
        self.glow = set(glow) | self.team_glow | self.fixed_glow
        self.idx = {n: i for i, (n, _) in enumerate(pal)}
        assert len(self.idx) == len(pal), "duplicate palette names"
        self.cols = 8; self.rows = (len(pal) + 7) // 8; self.cell = 16

    def uv(self, name):
        i = self.idx[name]; c, r = i % self.cols, i // self.cols
        return ((c + 0.5) / self.cols, 1 - (r + 0.5) / self.rows)

    def rgb(self, name, team="azure"):
        if name in self.team: return hexrgb(self.team[name][0 if team == "azure" else 1])
        c = hexrgb(dict(self.pal)[name])
        if name in self.glow or name in self.bright or name in ("eye", "eyew"): return c
        import colorsys          # pilot r2 night(): neutrals S x0.9, V x0.9 so team colour and glow pop
        h, s_, v = colorsys.rgb_to_hsv(*c); return colorsys.hsv_to_rgb(h, s_ * 0.9, v * 0.9)

    def code(self, name):
        if name in self.team: return 6 if name in self.team_glow else 8
        if name in self.fixed_glow: return 2
        return 6 if name in self.glow else 0

    def image(self, name, path, team="azure", mode="rgb"):
        W, Hh = self.cols * self.cell, self.rows * self.cell
        img = bpy.data.images.new(name, W, Hh, alpha=(mode == "rgba"))
        px = [0.0] * (W * Hh * 4)
        for i, (n, hx) in enumerate(self.pal):
            if mode == "mask": rgb = (1, 1, 1) if n in self.team else (0, 0, 0); a = 1.0
            else:
                rgb = self.rgb(n, team); a = (1.0 if n in self.team else 0.0) if mode == "rgba" else 1.0
            c, r = i % self.cols, i // self.cols
            for y in range(self.cell):
                yy = Hh - 1 - (r * self.cell + y)
                for x in range(self.cell):
                    o = (yy * W + c * self.cell + x) * 4; px[o:o + 4] = [rgb[0], rgb[1], rgb[2], a]
        img.pixels[:] = px
        img.filepath_raw = path; img.file_format = 'PNG'; img.save()
        return img

# ---------------------------------------------------------------- rig
def load_rig(fbx):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=fbx, use_anim=False, use_custom_normals=False, ignore_leaf_bones=False,
                             automatic_bone_orientation=False, primary_bone_axis="Y", secondary_bone_axis="X")
    rig = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    for o in list(bpy.data.objects):
        if o.type != 'ARMATURE': bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.actions):
        for x in list(coll): coll.remove(x)
    rig.name = "Rig"
    for pb in rig.pose.bones: pb.matrix_basis = Matrix.Identity(4)
    return rig

TRI_LINES = {}
class Kit:
    def __init__(self, rig, palette):
        self.rig = rig; self.P = palette; self.parts = {}
        MW = rig.matrix_world.copy(); self.MW = MW
        self.B = {b.name: (MW @ b.head_local, MW @ b.tail_local, MW @ b.matrix_local) for b in rig.data.bones}

    def bh(self, n): return self.B[n][0].copy()
    def bt(self, n): return self.B[n][1].copy()
    def bdir(self, n): return (self.bt(n) - self.bh(n)).normalized()
    def baxis(self, n, i): return Vector(self.B[n][2].col[i][:3]).normalized()

    def target(self, obj, bone):
        k = (obj, bone)
        if k not in self.parts:
            bm = bmesh.new(); bm.loops.layers.uv.new("UVMap"); bm.faces.layers.int.new("pal"); self.parts[k] = bm
        return self.parts[k]

    def emit(self, obj, bone, t, col, recalc=True):
        assert bone in self.B, bone
        if not LH.keep(t, col): t.free(); return
        uvl = t.loops.layers.uv.get("UVMap") or t.loops.layers.uv.new("UVMap")
        pl = t.faces.layers.int.get("pal") or t.faces.layers.int.new("pal")
        if recalc: bmesh.ops.recalc_face_normals(t, faces=list(t.faces))
        u = self.P.uv(col); ci = self.P.idx[col]
        import inspect
        fr = inspect.stack()[1]; fr2 = inspect.stack()[2] if fr.filename.endswith("lib.py") else fr
        key = f"{os.path.basename(fr2.filename)}:{fr2.lineno}"
        TRI_LINES[key] = TRI_LINES.get(key, 0) + sum(len(f.verts) - 2 for f in t.faces)
        for f in t.faces:
            f.smooth = True; f[pl] = ci
            for l in f.loops: l[uvl].uv = u
        me = bpy.data.meshes.new("_tmp"); t.to_mesh(me); t.free()
        self.target(obj, bone).from_mesh(me); bpy.data.meshes.remove(me)

    def recolour(self, obj, bone, src, dst, pred):
        """r2: repaint the faces of one part already emitted in palette colour src whose centre satisfies pred(centre)."""
        bm = self.parts[(obj, bone)]; pl = bm.faces.layers.int["pal"]; uvl = bm.loops.layers.uv["UVMap"]
        si, di, u = self.P.idx[src], self.P.idx[dst], self.P.uv(dst); n = 0
        for f in bm.faces:
            if f[pl] == si and pred(f.calc_center_median()):
                f[pl] = di; n += 1
                for l in f.loops: l[uvl].uv = u
        return n

    # ------------------------------------------------------------ assemble ONE mesh object
    def assemble(self, name, out_dir, work_dir):
        P = self.P
        pal = P.image(name + "_Palette", out_dir + f"/{name}_Palette.png", "azure")
        P.image(name + "_Palette_crimson", work_dir + f"/{name}_Palette_crimson.png", "crimson")
        P.image(name + "_TeamMask", out_dir + f"/{name}_TeamMask.png", mode="mask")
        P.image(name + "_PaletteTeamA", out_dir + f"/{name}_PaletteTeamA.png", mode="rgba")
        mat = bpy.data.materials.new(name); mat.use_nodes = True
        nt = mat.node_tree; bsdf = nt.nodes.get("Principled BSDF")
        tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = pal; tex.interpolation = 'Closest'
        nt.links.new(tex.outputs[0], bsdf.inputs["Base Color"]); bsdf.inputs["Roughness"].default_value = 1.0
        nt.nodes.active = tex
        bm = bmesh.new(); bm.loops.layers.uv.new("UVMap"); bm.faces.layers.int.new("pal")
        ranges = []; per_obj = {}
        for (obj, bone), pb in sorted(self.parts.items()):
            n0 = len(bm.verts); f0 = len(bm.faces)
            me = bpy.data.meshes.new("_t"); pb.to_mesh(me); bm.from_mesh(me); bpy.data.meshes.remove(me)
            ranges.append((bone, n0, len(bm.verts)))
            per_obj.setdefault(obj, {"faces": 0, "bones": set()}); per_obj[obj]["faces"] += len(bm.faces) - f0
            per_obj[obj]["bones"].add(bone)
        bmesh.ops.transform(bm, matrix=self.MW.inverted(), verts=list(bm.verts))
        pl = bm.faces.layers.int["pal"]
        face_pal = [f[pl] for f in bm.faces]
        me = bpy.data.meshes.new(name + "_LOD0"); bm.to_mesh(me); bm.free()
        for poly in me.polygons: poly.use_smooth = True
        me.set_sharp_from_angle(angle=math.radians(38))
        me.materials.append(mat)
        ca = me.color_attributes.new("Col", 'BYTE_COLOR', 'CORNER')
        names = [n for n, _ in P.pal]
        for poly, pi in zip(me.polygons, face_pal):
            n = names[pi]; rgb = P.rgb(n); a = P.code(n) / 8
            for li in poly.loop_indices: ca.data[li].color_srgb = (*rgb, a)
        me.color_attributes.active_color = ca
        if "pal" in me.attributes: me.attributes.remove(me.attributes["pal"])
        ob = bpy.data.objects.new(name + "_LOD0", me); bpy.context.scene.collection.objects.link(ob)
        ob.parent = self.rig; ob.matrix_parent_inverse = Matrix.Identity(4); ob.matrix_basis = Matrix.Identity(4)
        for bone, a, b in ranges:
            vg = ob.vertex_groups.get(bone) or ob.vertex_groups.new(name=bone)
            vg.add(list(range(a, b)), 1.0, 'REPLACE')
        mod = ob.modifiers.new("Armature", 'ARMATURE'); mod.object = self.rig
        for pb in self.parts.values(): pb.free()
        # checks
        me.calc_loop_triangles()
        bad = sum(1 for v in me.vertices if len([g for g in v.groups if g.weight > 0]) != 1)
        vol = 0.0
        for tri in me.loop_triangles:
            a_, b_, c_ = (me.vertices[i].co for i in tri.vertices); vol += a_.dot(b_.cross(c_)) / 6
        pts = [self.MW @ v.co for v in me.vertices]
        rep = {"object": ob.name, "tris": len(me.loop_triangles), "verts": len(me.vertices), "verts_not_rigid": bad,
               "signed_volume": round(vol, 4), "bones_used": sorted({r[0] for r in ranges}), "rig_bones": len(self.rig.data.bones),
               "bbox": [[round(min(p[i] for p in pts), 3) for i in range(3)], [round(max(p[i] for p in pts), 3) for i in range(3)]],
               "parts": {k: {"faces": v["faces"], "bones": sorted(v["bones"])} for k, v in per_obj.items()},
               "team_faces": sum(1 for p in face_pal if names[p] in P.team), "glow_faces": sum(1 for p in face_pal if names[p] in P.glow),
               "code_faces": {c: sum(1 for p in face_pal if P.code(names[p]) == c) for c in (0, 2, 6, 8)}}
        return ob, rep

# ---------------------------------------------------------------- primitives (from the pilot)
def frame_from(axis, hint=V(1, 0, 0)):
    a = axis.normalized(); u = hint - a * hint.dot(a)
    if u.length < 1e-5: u = V(0, 1, 0) - a * a.y
    if u.length < 1e-5: u = V(0, 0, 1) - a * a.z
    u.normalize(); v = a.cross(u)
    return a, u, v

def se(ang, p):
    c, s = math.cos(ang), math.sin(ang)
    return math.copysign(abs(c) ** (2 / p), c), math.copysign(abs(s) ** (2 / p), s)

def ring_loft(rings, n=16, p=2.0, cap0=None, cap1=None, phase=0.0):
    """rings: (center, u, v, ru, rv). cap: None=flat fan at centre, Vector=apex, False=open."""
    rings = LH.rings(rings); n = LH.seg(n)
    t = bmesh.new(); R = []
    for (c, u, v, ru, rv) in rings:
        row = []
        for k in range(n):
            x, y = se(2 * math.pi * k / n + phase, p)
            row.append(t.verts.new(c + u * (x * ru) + v * (y * rv)))
        R.append(row)
    for i in range(len(R) - 1):
        for k in range(n):
            t.faces.new((R[i][k], R[i][(k + 1) % n], R[i + 1][(k + 1) % n], R[i + 1][k]))
    for cap, row, ring in ((cap0, R[0], rings[0]), (cap1, R[-1], rings[-1])):
        if cap is False: continue
        ap = t.verts.new(ring[0] if cap is None else cap)
        for k in range(n): t.faces.new((row[k], row[(k + 1) % n], ap))
    return t

def tube(p0, p1, prof, n=12, p=2.0, hint=V(1, 0, 0), round0=True, round1=True, phase=0.0):
    a, u, v = frame_from(p1 - p0, hint); L = (p1 - p0).length
    rings = [(p0 + a * (L * tt), u, v, ru, rv) for (tt, ru, rv) in prof]
    c0 = rings[0][0] - a * (min(prof[0][1], prof[0][2]) * 0.6) if round0 else None
    c1 = rings[-1][0] + a * (min(prof[-1][1], prof[-1][2]) * 0.6) if round1 else None
    return ring_loft(rings, n, p, c0, c1, phase)

def capsule(p0, p1, r, n=10, hint=V(1, 0, 0), r1=None):
    r1 = r if r1 is None else r1; L = max((p1 - p0).length, 1e-4)
    prof = [(-0.75 * r / L, r * 0.66, r * 0.66), (-0.3 * r / L, r * 0.95, r * 0.95), (0, r, r), (1, r1, r1),
            (1 + 0.3 * r1 / L, r1 * 0.95, r1 * 0.95), (1 + 0.75 * r1 / L, r1 * 0.66, r1 * 0.66)]
    return tube(p0, p1, prof, n, 2.0, hint)

def path_tube(pts, normals, w, th, n=4, p=6.0, caps=True):
    rings = []
    for i, P in enumerate(pts):
        tan = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        nn = normals[i] - tan * normals[i].dot(tan); nn.normalize()
        ww = w[i] if isinstance(w, (list, tuple)) else w; tt = th[i] if isinstance(th, (list, tuple)) else th
        rings.append((P, tan.cross(nn), nn, ww, tt))
    return ring_loft(rings, n, p, None if caps else False, None if caps else False, phase=math.pi / 4)

def ellipsoid(c, r, seg=12, rings=8, rot=Matrix.Identity(3)):
    t = bmesh.new(); seg, rings = LH.sphere(seg, rings)
    bmesh.ops.create_uvsphere(t, u_segments=seg, v_segments=rings, radius=1.0)
    M = Matrix.Translation(c) @ rot.to_4x4() @ Matrix.Diagonal((*r, 1))
    bmesh.ops.transform(t, matrix=M, verts=list(t.verts))
    return t

def box(c, size, bevel=0.0, segs=1, rot=Matrix.Identity(3)):
    t = bmesh.new(); bmesh.ops.create_cube(t, size=1.0)
    bmesh.ops.scale(t, vec=size, verts=list(t.verts))
    if bevel > 0 and LH.bevel_ok(size):
        bmesh.ops.bevel(t, geom=list(t.edges), offset=bevel, offset_type='OFFSET', segments=segs, profile=0.5,
                        affect='EDGES', clamp_overlap=True)
    bmesh.ops.transform(t, matrix=Matrix.Translation(c) @ rot.to_4x4(), verts=list(t.verts))
    return t

def rbox(c, size, rot=Matrix.Identity(3), p=2.6, n=12, nr=6):
    """Soft 'pillow' box: superellipse cross-sections stacked along local z with a superellipse profile (no hard edges)."""
    sx, sy, sz = size[0] / 2, size[1] / 2, size[2] / 2; rs = []
    for i in range(nr + 1):
        t = -1 + 2 * i / nr
        k = max(0.0, 1 - abs(t) ** p) ** (1 / p)
        if 0 < i < nr: rs.append((V(0, 0, t * sz), V(1, 0, 0), V(0, 1, 0), sx * max(k, .05), sy * max(k, .05)))
    bm = ring_loft(rs, n, p, V(0, 0, -sz), V(0, 0, sz))
    bmesh.ops.transform(bm, matrix=Matrix.Translation(c) @ rot.to_4x4(), verts=list(bm.verts))
    return bm

def beam(p0, p1, w, d, hint=V(0, 1, 0), bevel=0.008):
    a = (p1 - p0); L = a.length; a.normalize()
    u = hint - a * hint.dot(a); u.normalize(); v = a.cross(u)
    return box((p0 + p1) / 2, (w, d, L), bevel, 1, Matrix((v, u, a)).transposed())

def cylinder(p0, p1, r, n=12, r1=None, hint=V(1, 0, 0)):
    r1 = r if r1 is None else r1
    return tube(p0, p1, [(0, r, r), (1, r1, r1)], n, 2.0, hint, False, False)

def disc_ring(c, axis, r_out, r_in, h, n=16, hint=V(1, 0, 0)):
    """A flat washer (brim) around axis: closed slab ring."""
    a, u, v = frame_from(axis, hint); t = bmesh.new(); rows = []
    for (rr, hh) in ((r_in, -h / 2), (r_out, -h / 2), (r_out, h / 2), (r_in, h / 2)):
        rows.append([t.verts.new(c + a * hh + (u * math.cos(2 * math.pi * k / n) + v * math.sin(2 * math.pi * k / n)) * rr) for k in range(n)])
    for i in range(4):
        A, Bq = rows[i], rows[(i + 1) % 4]
        for k in range(n): t.faces.new((A[k], A[(k + 1) % n], Bq[(k + 1) % n], Bq[k]))
    return t

def slab(outer, inner):
    t = bmesh.new(); R, C = len(outer), len(outer[0])
    O = [[t.verts.new(p) for p in row] for row in outer]
    I = [[t.verts.new(p) for p in row] for row in inner]
    for i in range(R - 1):
        for j in range(C - 1):
            t.faces.new((O[i][j], O[i][j + 1], O[i + 1][j + 1], O[i + 1][j]))
            t.faces.new((I[i][j], I[i + 1][j], I[i + 1][j + 1], I[i][j + 1]))
    edge = [(i, 0) for i in range(R)] + [(R - 1, j) for j in range(1, C)] + \
           [(i, C - 1) for i in range(R - 2, -1, -1)] + [(0, j) for j in range(C - 2, 0, -1)]
    for k in range(len(edge)):
        (a1, b1), (a2, b2) = edge[k], edge[(k + 1) % len(edge)]
        t.faces.new((O[a1][b1], O[a2][b2], I[a2][b2], I[a1][b1]))
    return t

def leaf(base, axis, side, length, width, th, n=7, bend=0.0, normal=None):
    """Flat pointed leaf slab from base along axis, widening along side. normal = slab thickness dir."""
    axis = axis.normalized(); side = (side - axis * side.dot(axis)).normalized()
    nrm = normal if normal is not None else axis.cross(side)
    outer, inner = [], []
    for i in range(n + 1):
        t = i / n; w = width * max(0.12, math.sin(math.pi * min(1, t * 1.08)) ** 0.8) if t < 1 else 0.0005
        c = base + axis * (length * t) + nrm * (bend * length * t * t)
        o = [c + side * (w * (j - 1)) + nrm * th * (1.0 if j == 1 else 0.55) for j in range(3)]
        q = [c + side * (w * (j - 1)) - nrm * th * (1.0 if j == 1 else 0.55) for j in range(3)]
        outer.append(o); inner.append(q)
    return slab(outer, inner)

# ---------------------------------------------------------------- body profile helpers
def prof_at(P, z):
    if z <= P[0][0]: return P[0][1:]
    for a, b in zip(P, P[1:]):
        if a[0] <= z <= b[0]:
            t = (z - a[0]) / (b[0] - a[0]); return tuple(a[i] + (b[i] - a[i]) * t for i in (1, 2, 3))
    return P[-1][1:]

def surf_y(P, x, z, p, side=-1, off=0.0):
    rx, ry, cy = prof_at(P, z); rx += off; ry += off
    q = max(0.0, 1 - min(abs(x) / rx, 0.999) ** p)
    return cy + side * ry * q ** (1 / p)

def loft_profile(P, p, n, scale=1.0, add=0.0, cap0=None, cap1=None):
    rings = [(V(0, cy, z), V(1, 0, 0), V(0, 1, 0), rx * scale + add, ry * scale + add) for (z, rx, ry, cy) in P]
    return ring_loft(rings, n, p, cap0, cap1)

def band(P, p, z0, z1, add, n=24, add_edge=None):
    """A belt/trim ring hugging a loft profile between z0 and z1."""
    ae = add * 0.45 if add_edge is None else add_edge
    rings = []
    for z, ad in ((z0, ae), (z0 + (z1 - z0) * 0.15, add), (z1 - (z1 - z0) * 0.15, add), (z1, ae)):
        rx, ry, cy = prof_at(P, z); rings.append((V(0, cy, z), V(1, 0, 0), V(0, 1, 0), rx + ad, ry + ad))
    return ring_loft(rings, n, p)

# ---------------------------------------------------------------- hands
def fist(kit, obj, bone, hc, a, d, G, col, right=True, knuck=None, thumb_col=None):
    """Closed fist wrapped around a handle through hc along axis a. d = wrist->knuckle direction.
    Everything goes on `bone` (the hand-slot bone for the tool hand, so the grip can never slip)."""
    a = a.normalized(); d = (d - a * d.dot(a)).normalized()
    pn = a.cross(d) if right else d.cross(a)      # palm normal: from palm toward the handle
    rh = 0.034 * G; ft = 0.046 * G; R = rh + ft * 0.55; fw = 0.05 * G
    # palm block behind the handle (toward -pn), from the wrist side to the knuckles
    pc = hc - pn * (R + 0.035 * G) - d * 0.012 * G
    kit.emit(obj, bone, rbox(pc, (0.21 * G, 0.10 * G, 0.16 * G), basis(a, pn, d), 2.4, 12, 6), col)
    # rounded back of the hand / heel so the fist reads as one soft mass, not a plate
    kit.emit(obj, bone, ellipsoid(pc - pn * 0.012 * G - d * 0.02 * G, (0.098 * G, 0.05 * G, 0.078 * G), 6, 4, basis(a, pn, d)), col)
    kc = pc + d * 0.064 * G - pn * 0.018 * G
    kit.emit(obj, bone, capsule(kc - a * 0.075 * G, kc + a * 0.075 * G, 0.022 * G, 8, pn), knuck or col)
    # four fingers: arcs around the handle, knuckles -> over the far side -> tips tucked into the palm
    for k in range(4):
        o = a * ((k - 1.5) * fw)
        ths = [-55, 0, 60, 120, 175, 215]
        pts = [hc + o + (d * math.cos(math.radians(t)) + pn * math.sin(math.radians(t))) * (R + (0.012 * G if t < 0 else 0)) for t in ths]
        nrm = [(d * math.cos(math.radians(t)) + pn * math.sin(math.radians(t))) for t in ths]
        w = [ft * 0.56 * s for s in (1.05, 1.06, 0.99, 0.95, 0.9, 0.82)]
        kit.emit(obj, bone, path_tube(pts, nrm, w, [x * 0.92 for x in w], 5, 2.0), col)
    # thumb wraps the other way over the index finger
    so = a * (1.5 * fw + 0.042 * G) if right else a * (1.5 * fw + 0.042 * G)
    ths = [250, 215, 180, 150, 120]
    pts = [hc + so * (1.0 if t > 200 else 0.82) + (d * math.cos(math.radians(t)) + pn * math.sin(math.radians(t))) * (R + 0.022 * G) for t in ths]
    pts[0] = pts[0] - pn * 0.02 * G
    nrm = [(d * math.cos(math.radians(t)) + pn * math.sin(math.radians(t))) for t in ths]
    w = [0.031 * G, 0.03 * G, 0.028 * G, 0.026 * G, 0.023 * G]
    kit.emit(obj, bone, path_tube(pts, nrm, w, w, 6, 2.0), thumb_col or col)
    return pn

def open_hand(kit, obj, bone, h0, d, side_s, G, col, col_dk=None, curl=0.35, fwd=V(0, -1, 0)):
    """Relaxed glove (pilot style) with fingers half-curled toward the palm."""
    d = d.normalized(); f = fwd - d * fwd.dot(d); f.normalize()
    pn = d.cross(f)
    if pn.x * side_s > 0: pn = -pn
    pc = h0 + d * 0.095 * G / 1.22
    kit.emit(obj, bone, rbox(pc + d * 0.02 * G, (.25 * G, .15 * G, .18 * G), basis(f, pn, d), 2.4, 12, 6), col)
    kit.emit(obj, bone, rbox(pc - pn * .03 * G, (.19 * G, .09 * G, .14 * G), basis(f, pn, d), 2.4, 10, 4), col_dk or col)
    for k in range(4):
        o = (-.085 + .057 * k) * G
        a = pc + f * o + d * (.08 * G) + pn * .01 * G
        L = (.10 - abs(k - 1.5) * .012) * G
        m = a + d * L * 0.55 + pn * L * 0.25 * (1 + curl)
        b = m + d * L * 0.5 * (1 - curl) + pn * L * 0.5 * (0.4 + curl)
        e = b + (b - m).normalized() * .012 * G
        kit.emit(obj, bone, path_tube([a - d * .01 * G, m, b, e], [f] * 4, [.040 * G, .038 * G, .034 * G, .02 * G],
                                      [.038 * G, .036 * G, .032 * G, .018 * G], 6, 2.0), col)
    ta = pc + f * (.11 * G) - d * .02 * G + pn * .04 * G
    tb = ta + f * .04 * G + d * .06 * G + pn * .07 * G
    kit.emit(obj, bone, capsule(ta, tb, .043 * G, 6, pn), col)
    return pn, f

def eyes(kit, obj, bone, face_y, x0, z0, scale, col_w, col_p, col_brow, brow_ang=14, brow_dz=0.07, iris=None, look=0.0):
    for s in (-1, 1):
        x = x0 * s; y = face_y(x, z0)
        kit.emit(obj, bone, ellipsoid(V(x, y + .004 * scale, z0), (.042 * scale, .02 * scale, .05 * scale), 8, 5), col_w)
        if iris:
            kit.emit(obj, bone, ellipsoid(V(x - s * .004 * scale, y - .008 * scale, z0 - .004 * scale), (.03 * scale, .012 * scale, .036 * scale), 6, 4), iris)
        kit.emit(obj, bone, ellipsoid(V(x - s * .004 * scale, y - (.014 if iris else .012) * scale, z0 - .004 * scale), (.02 * scale, .012 * scale, .026 * scale), 6, 4), col_p)
        rot = Matrix.Rotation(math.radians(-brow_ang * s), 3, 'Y')
        zb = z0 + brow_dz * scale
        kit.emit(obj, bone, box(V(x + s * .005 * scale, face_y(x, zb) - .012 * scale, zb), (.105 * scale, .035 * scale, .036 * scale), .012 * scale, 1, rot), col_brow)

def save_report(rep, path):
    rep["tris_by_line"] = dict(sorted(TRI_LINES.items(), key=lambda kv: -kv[1]))
    json.dump(rep, open(path, "w"), indent=1); print("BUILD_REPORT", json.dumps({k: v for k, v in rep.items() if k != "parts"}))

def surf_pt(Pr, p, z, phi, off):
    """Point on a superellipse loft profile at height z, angle phi (0 = front -Y, +90 deg = +X = character's left)."""
    rx, ry, cy = prof_at(Pr, z); x, y = se(phi - math.pi / 2, p)
    return V(x * (rx + off), cy + y * (ry + off), z)

def wrap_panel(Pr, p, z0, z1, ph0, ph1, off_out, off_in, nz=4, nph=8, flare=0.0):
    """Slab hugging the profile between z0..z1 and angles ph0..ph1; flare pushes the lower edge outward."""
    outer, inner = [], []
    for i in range(nz + 1):
        z = lerp(z0, z1, i / nz); fl = flare * ((z1 - z) / (z1 - z0)) ** 1.3 if flare else 0.0
        outer.append([surf_pt(Pr, p, z, lerp(ph0, ph1, j / nph), off_out + fl) for j in range(nph + 1)])
        inner.append([surf_pt(Pr, p, z, lerp(ph0, ph1, j / nph), off_in + fl) for j in range(nph + 1)])
    return slab(outer, inner)

def round_path(pts, radii, n=8, hint=V(0, 0, 1)):
    """A round tube along a polyline with per-point radius, capped with apexes."""
    rings = []
    for i, P in enumerate(pts):
        tan = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        a, u, v = frame_from(tan, hint); rings.append((P, u, v, radii[i], radii[i]))
    e0 = pts[0] - (pts[1] - pts[0]).normalized() * radii[0] * 0.6
    e1 = pts[-1] + (pts[-1] - pts[-2]).normalized() * radii[-1] * 0.6
    return ring_loft(rings, n, 2.0, e0, e1)

def spike(base, d, L, r, n=5, hint=V(0, 0, 1), r_mid=None, bend=None):
    """Tapered hair lock / spike from base along d (unit), optional bend vector added quadratically."""
    d = d.normalized(); r_mid = r * 0.75 if r_mid is None else r_mid
    pts = []; rad = []
    for t, rr in ((0, r), (.35, r_mid), (.7, r * .42), (1.0, r * .08)):
        p = base + d * (L * t)
        if bend is not None: p = p + bend * (t * t)
        pts.append(p); rad.append(rr)
    return round_path(pts, rad, n, hint)

def crystal(base, d, L, r, n=5, hint=V(1, 0, 0)):
    """Faceted crystal shard: hexagonal-ish prism with a pointed tip."""
    d = d.normalized(); a, u, v = frame_from(d, hint)
    return ring_loft([(base, u, v, r * .8, r * .7), (base + d * L * .55, u, v, r, r * .85)], n, 2.0, base - d * r * .3, base + d * L)

def lock(base, d, L, w, th, nrm, bend=None, steps=4, n=5, p=2.2, curl=0.0):
    """Broad, flattened hair lock: width w across, thickness th along nrm, tapering to a point, optional bend
    (added quadratically) and curl (tip rotates toward nrm)."""
    d = d.normalized(); nrm = (nrm - d * nrm.dot(d)).normalized()
    pts, ns, ws, ts = [], [], [], []
    for i in range(steps + 1):
        t = i / steps
        q = base + d * (L * t) + nrm * (curl * L * t * t)
        if bend is not None: q = q + bend * (t * t)
        pts.append(q); ns.append(nrm)
        k = (1 - t) ** 0.8 if t < 1 else 0.0
        ws.append(max(w * (0.75 + 0.5 * math.sin(math.pi * min(1, t + .25))) * k, w * .06))
        ts.append(max(th * (1 - t * .7), th * .2))
    return path_tube(pts, ns, ws, ts, n, p)
