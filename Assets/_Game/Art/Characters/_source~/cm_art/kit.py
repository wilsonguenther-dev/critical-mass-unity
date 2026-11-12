"""Mesh kit: closed, manifold primitives written into per-object bmeshes with bible colour, team mask, feature tags
and skin weights. Every primitive is a closed shell, so the result has no loose or non-manifold geometry.

Weights: each primitive names its bones.
  "bone"              rigid, 100 % on that bone
  ["a", "b", ...]     blended by distance to each bone segment (falloff power 4), at most 4 influences
  {"a": 0.6, ...}     fixed weights
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector

from . import palette

MAX_INFLUENCES = 4
GRADIENT = 0.06        # +6 % value toward the top of each part (ART-DIRECTION 3.5)
CREVICE = 0.10         # -10 % value in crevices (baked ray occlusion, finalize)
SEG = 0.76             # global segment scale for round primitives (the 12,000-triangle budget knob)
STEP_TOP, STEP_UNDER = 0.08, 0.12   # v3 value steps from the surface normal: +8 % on top faces, -12 % on undersides


def _s(n, lo=4):
    return max(lo, int(round(n * SEG)))


def mat_from(loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1)):
    """loc + XYZ Euler degrees + scale -> Matrix."""
    from mathutils import Euler
    e = Euler([math.radians(a) for a in rot], "XYZ")
    m = Matrix.Translation(Vector(loc)) @ e.to_matrix().to_4x4()
    s = Matrix.Diagonal((scale[0], scale[1], scale[2], 1.0))
    return m @ s


def frame(origin, z_axis, y_hint):
    """Matrix whose local Z points along z_axis and local Y as close to y_hint as possible."""
    z = Vector(z_axis).normalized()
    y = Vector(y_hint)
    y = (y - z * y.dot(z)).normalized()
    x = y.cross(z).normalized()
    m = Matrix((x, y, z)).transposed().to_4x4()
    m.translation = Vector(origin)
    return m


def _seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-12)))
    return (p - (a + ab * t)).length


class Builder:
    def __init__(self, arm):
        self.arm = arm
        self.bones = {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in arm.data.bones}
        self.objs = {}

    # ------------------------------------------------------------------ storage
    def _get(self, obj):
        if obj not in self.objs:
            bm = bmesh.new()
            self.objs[obj] = {
                "bm": bm,
                "col": bm.verts.layers.float_color.new("Col"),
                "ws": bm.verts.layers.int.new("wset"),
                "ft": bm.verts.layers.int.new("feat"),
                "uv": bm.loops.layers.uv.new("UVMap"),
                "nogo": bm.verts.layers.int.new("noao"),
                "wsets": [],
                "feats": [],
            }
        return self.objs[obj]

    def add(self, obj, src, color, bones, feat=None, smooth=True, m=None, face_uv=None, grad=GRADIENT, steps=True):
        """Merge temp bmesh `src` (transformed by m) into object `obj`.

        grad: value gradient across the part's height (bottom x(1-grad/2) .. top x(1+grad/2)), the "+6 % toward the
        top" painted-light rule. steps: v3 value steps from the world normal (lighter tops, darker undersides, the
        bible's painted form shadow), stepped at n.z > 0.45 and n.z < -0.35. face_uv = (cx, cy, cz, half_w, half_h): planar front projection for the painted face
        decal on faces whose centre is in front of cy; every other face samples UV (0.002, 0.002), a transparent texel.
        """
        if m is not None:
            bmesh.ops.transform(src, matrix=m, verts=src.verts)
        d = self._get(obj)
        bm = d["bm"]
        key = repr(bones)
        if key not in [repr(w) for w in d["wsets"]]:
            d["wsets"].append(bones)
        wi = [repr(w) for w in d["wsets"]].index(key)
        feat = feat or "misc"
        if feat not in d["feats"]:
            d["feats"].append(feat)
        fi = d["feats"].index(feat)
        rgba = palette.vcol(color) if isinstance(color, str) else color
        glow = abs(rgba[3] - palette.MASK_TEAM_GLOW) < 0.01
        zs = [v.co.z for v in src.verts]
        z0, z1 = min(zs), max(zs)
        vmap = {}
        src.normal_update()
        for v in src.verts:
            nv = bm.verts.new(v.co)
            t = (v.co.z - z0) / max(z1 - z0, 1e-6)
            k = 1.0 + grad * (t - 0.5)
            if steps:
                nz = v.normal.z
                k *= (1.0 + STEP_TOP) if nz > 0.45 else ((1.0 - STEP_UNDER) if nz < -0.35 else 1.0)
            f = 1.0 if glow else k ** 2.2      # value step in sRGB, applied in linear
            nv[d["col"]] = (rgba[0] * f, rgba[1] * f, rgba[2] * f, rgba[3])
            nv[d["ws"]] = wi
            nv[d["ft"]] = fi
            nv[d["nogo"]] = 1 if glow else 0
            vmap[v] = nv
        uvl = d["uv"]
        for f in src.faces:
            nf = bm.faces.new([vmap[v] for v in f.verts])
            nf.smooth = smooth
            for lp in nf.loops:
                lp[uvl].uv = (0.002, 0.002)
            if face_uv is not None:
                cx, cy, cz, hw, hh = face_uv
                if nf.calc_center_median().y < cy:
                    for lp in nf.loops:
                        co = lp.vert.co
                        lp[uvl].uv = ((co.x - cx) / (2 * hw) + 0.5, (co.z - cz) / (2 * hh) + 0.5)
        src.free()

    # ------------------------------------------------------------------ primitives (return temp bmesh)
    @staticmethod
    def sphere(radii=(1, 1, 1), u=16, v=10):
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=_s(u, 6), v_segments=_s(v, 4), radius=1.0)
        bmesh.ops.scale(bm, vec=Vector(radii), verts=bm.verts)
        return bm

    @staticmethod
    def box(size=(1, 1, 1), bevel=0.0, seg=2):
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
        if bevel > 0:
            bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, offset_type="OFFSET", segments=seg,
                            profile=0.5, affect="EDGES", clamp_overlap=True)
        return bm

    @staticmethod
    def cyl(a, b, r1, r2, seg=12):
        """Closed truncated cone from point a (radius r1) to point b (radius r2)."""
        a, b = Vector(a), Vector(b)
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=_s(seg, 6), radius1=r1, radius2=r2,
                              depth=(b - a).length)
        d = (b - a).normalized()
        q = d.to_track_quat("Z", "Y")
        m = Matrix.Translation((a + b) * 0.5) @ q.to_matrix().to_4x4()
        bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
        return bm

    @staticmethod
    def lathe(profile, seg=16):
        """Closed surface of revolution about local Z. profile = [(r, z), ...] from bottom to top; r>0 inside."""
        bm = bmesh.new()
        rings = []
        seg = _s(seg, 8)
        for r, z in profile:
            ring = [bm.verts.new((r * math.cos(2 * math.pi * i / seg), r * math.sin(2 * math.pi * i / seg), z))
                    for i in range(seg)]
            rings.append(ring)
        for k in range(len(rings) - 1):
            for i in range(seg):
                j = (i + 1) % seg
                bm.faces.new((rings[k][i], rings[k][j], rings[k + 1][j], rings[k + 1][i]))
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        return bm

    @staticmethod
    def torus(R, r, seg=16, ring=6):
        """Torus in local XY plane around Z."""
        seg, ring = _s(seg, 8), max(3, int(round(ring * SEG)))
        bm = bmesh.new()
        grid = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            row = []
            for j in range(ring):
                b = 2 * math.pi * j / ring
                rr = R + r * math.cos(b)
                row.append(bm.verts.new((rr * math.cos(a), rr * math.sin(a), r * math.sin(b))))
            grid.append(row)
        for i in range(seg):
            for j in range(ring):
                i2, j2 = (i + 1) % seg, (j + 1) % ring
                bm.faces.new((grid[i][j], grid[i2][j], grid[i2][j2], grid[i][j2]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        return bm

    @staticmethod
    def prism(points, thickness, bevel=0.0, seg=2):
        """Closed prism: polygon `points` [(x, y), ...] in local XY, extruded +-thickness/2 along Z."""
        bm = bmesh.new()
        h = thickness / 2
        bot = [bm.verts.new((x, y, -h)) for x, y in points]
        top = [bm.verts.new((x, y, h)) for x, y in points]
        n = len(points)
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((bot[i], bot[j], top[j], top[i]))
        bm.faces.new(list(reversed(bot)))
        bm.faces.new(top)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        if bevel > 0:
            bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, offset_type="OFFSET", segments=seg,
                            profile=0.5, affect="EDGES", clamp_overlap=True)
        ngons = [f for f in bm.faces if len(f.verts) > 4]
        if ngons:
            bmesh.ops.triangulate(bm, faces=ngons, quad_method="BEAUTY", ngon_method="EAR_CLIP")
        return bm

    @staticmethod
    def spike(base, tip, r, seg=6, bend=None):
        """Hair spike: a closed cone from base (radius r) to tip; optional bend vector moves the middle ring."""
        base, tip = Vector(base), Vector(tip)
        if bend is None:
            bm = Builder.cyl(base, tip, r, 0.004, seg)
            return bm
        mid = (base + tip) * 0.5 + Vector(bend)
        bm = bmesh.new()
        rings = []
        pts = [(base, r), (mid, r * 0.62), (tip, 0.004)]
        for k, (c, rad) in enumerate(pts):
            nxt = pts[min(k + 1, 2)][0] if k < 2 else c
            prv = pts[max(k - 1, 0)][0] if k > 0 else c
            d = (nxt - prv).normalized()
            q = d.to_track_quat("Z", "Y")
            ring = []
            for i in range(seg):
                a = 2 * math.pi * i / seg
                ring.append(bm.verts.new(c + q @ Vector((rad * math.cos(a), rad * math.sin(a), 0))))
            rings.append(ring)
        for k in range(2):
            for i in range(seg):
                j = (i + 1) % seg
                bm.faces.new((rings[k][i], rings[k][j], rings[k + 1][j], rings[k + 1][i]))
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[2])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        return bm

    @staticmethod
    def slab(size, e=0.5, u=14, v=9):
        """Rounded slab: a superellipsoid (exponent e < 1 squares it off) with full extents `size`. No hard corner
        anywhere, so it needs no bevel and catches all three tone bands (pauldrons, knee guards, toe caps)."""
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=_s(u, 8), v_segments=_s(v, 5), radius=1.0)
        for vert in bm.verts:
            vert.co = Vector(tuple(math.copysign(abs(c) ** e, c) * s / 2 for c, s in zip(vert.co, size)))
        return bm

    @staticmethod
    def chipped(bm, rng, chips=2, depth=0.20, lump=0.0):
        """Hewn stone: cut `chips` corners off a centred primitive with flat facets (bisect + fill), then optional
        lumpiness along the normal. Deterministic through `rng`. Returns the same bmesh."""
        xs = [v.co for v in bm.verts]
        ext = Vector(tuple(max(abs(c[i]) for c in xs) for i in range(3)))
        corners = [(sx, sy, sz) for sx in (1, -1) for sy in (1, -1) for sz in (1, -1)]
        rng.shuffle(corners)
        for sx, sy, sz in corners[:chips]:
            sz = abs(sz) if rng.random() < 0.7 else sz          # most chips on the upper edge, where the camera looks
            n = Vector((sx / ext.x, sy / ext.y, sz / ext.z * 0.6)).normalized()
            far = max(v.co.dot(n) for v in bm.verts)
            co = n * far * (1.0 - depth * rng.uniform(0.7, 1.2))
            bmesh.ops.bisect_plane(bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces), dist=1e-5,
                                   plane_co=co, plane_no=n, clear_outer=True)
            bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
        if lump > 0:
            bm.normal_update()
            for v in bm.verts:
                v.co += v.normal * lump * rng.uniform(-1.0, 1.0)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        return bm

    @staticmethod
    def clump(p0, p1, p2, w, t, seg=7, rings=5, up=(0, 0, 1), prof=(1.0, 1.0, 0.82, 0.5)):
        """Sculpted hair clump: an elliptical section (width w, thickness t) swept along the quadratic curve
        p0 -> p1 -> p2, swelling then tapering to a point at p2. `up` orients the flat side."""
        P0, P1, P2 = Vector(p0), Vector(p1), Vector(p2)
        bm = bmesh.new()
        rr = []
        n = len(prof)
        for k in range(n):
            s = k / n
            c = (1 - s) ** 2 * P0 + 2 * (1 - s) * s * P1 + s * s * P2
            d = (2 * (1 - s) * (P1 - P0) + 2 * s * (P2 - P1)).normalized()
            side = d.cross(Vector(up))
            if side.length < 1e-4:
                side = d.orthogonal()
            side.normalize()
            nrm = side.cross(d).normalized()
            f = prof[k]
            rr.append([bm.verts.new(c + side * (w / 2 * f * math.cos(2 * math.pi * i / seg))
                                    + nrm * (t / 2 * f * math.sin(2 * math.pi * i / seg))) for i in range(seg)])
        tip = bm.verts.new(P2)
        for k in range(n - 1):
            for i in range(seg):
                j = (i + 1) % seg
                bm.faces.new((rr[k][i], rr[k][j], rr[k + 1][j], rr[k + 1][i]))
        for i in range(seg):
            bm.faces.new((rr[-1][i], rr[-1][(i + 1) % seg], tip))
        bm.faces.new(list(reversed(rr[0])))
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        return bm

    @staticmethod
    def bake_crevices(bm, col, skip, dist=0.09, strength=CREVICE):
        """-strength value where a vertex is enclosed by other geometry (ray occlusion over the hemisphere)."""
        from mathutils.bvhtree import BVHTree
        bm.normal_update()
        tree = BVHTree.FromBMesh(bm)
        dirs = []
        for i in range(12):                       # fixed, deterministic hemisphere set (z = up along the normal)
            a = 2 * math.pi * i / 12 + (0.4 if i % 2 else 0.0)
            el = math.radians(28 if i % 2 else 55)
            dirs.append(Vector((math.cos(a) * math.cos(el), math.sin(a) * math.cos(el), math.sin(el))))
        dirs.append(Vector((0, 0, 1)))
        for v in bm.verts:
            if v[skip]:
                continue
            n = v.normal
            if n.length < 0.5:
                continue
            q = n.to_track_quat("Z", "Y")
            o = v.co + n * 0.004
            hit = sum(1 for d in dirs if tree.ray_cast(o, q @ d, dist)[0] is not None)
            k = (1.0 - strength * hit / len(dirs)) ** 2.2
            c = v[col]
            v[col] = (c[0] * k, c[1] * k, c[2] * k, c[3])

    # ------------------------------------------------------------------ finalise
    def finalize(self, coll, material):
        out = {}
        for name, d in self.objs.items():
            bm = d["bm"]
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
            self.bake_crevices(bm, d["col"], d["nogo"])
            me = bpy.data.meshes.new(name)
            bm.to_mesh(me)
            ob = bpy.data.objects.new(name, me)
            coll.objects.link(ob)
            # colour attribute (point domain, float, linear); alpha = team mask code
            ca = me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
            cl = bm.verts.layers.float_color["Col"]
            ws = bm.verts.layers.int["wset"]
            ft = bm.verts.layers.int["feat"]
            bm.verts.ensure_lookup_table()
            for i, v in enumerate(bm.verts):
                ca.data[i].color = v[cl]
            me.color_attributes.active_color = ca
            me.color_attributes.render_color_index = me.color_attributes.find("Col")
            for a in ("wset", "feat", "noao"):
                if a in me.attributes:
                    me.attributes.remove(me.attributes[a])
            # feature tags as non-deform vertex groups (FBX ignores them; the .blend keeps them)
            fgroups = {fi: ob.vertex_groups.new(name="feat_" + f) for fi, f in enumerate(d["feats"])}
            # skin weights
            groups = {}
            for i, v in enumerate(bm.verts):
                fgroups[v[ft]].add([i], 1.0, "REPLACE")
                spec = d["wsets"][v[ws]]
                for bone, w in self._weights(v.co, spec).items():
                    if bone not in groups:
                        groups[bone] = ob.vertex_groups.new(name=bone)
                    groups[bone].add([i], w, "REPLACE")
            me.materials.append(material)
            ob.parent = self.arm
            mod = ob.modifiers.new("Armature", "ARMATURE")
            mod.object = self.arm
            mod.use_vertex_groups = True
            bm.free()
            out[name] = ob
        return out

    def _weights(self, p, spec):
        if isinstance(spec, str):
            return {spec: 1.0}
        if isinstance(spec, dict):
            s = sum(spec.values())
            return {k: v / s for k, v in spec.items()}
        raw = {}
        for b in spec:
            h, t = self.bones[b]
            raw[b] = 1.0 / (_seg_dist(p, h, t) + 0.02) ** 4
        top = sorted(raw.items(), key=lambda kv: -kv[1])[:MAX_INFLUENCES]
        s = sum(w for _, w in top)
        top = [(b, w / s) for b, w in top if w / s >= 0.04]
        s = sum(w for _, w in top)
        return {b: w / s for b, w in top}
