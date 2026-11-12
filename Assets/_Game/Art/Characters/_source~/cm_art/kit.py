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
                "wsets": [],
                "feats": [],
            }
        return self.objs[obj]

    def add(self, obj, src, color, bones, feat=None, smooth=True, m=None):
        """Merge temp bmesh `src` (transformed by m) into object `obj`."""
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
        vmap = {}
        for v in src.verts:
            nv = bm.verts.new(v.co)
            nv[d["col"]] = rgba
            nv[d["ws"]] = wi
            nv[d["ft"]] = fi
            vmap[v] = nv
        for f in src.faces:
            nf = bm.faces.new([vmap[v] for v in f.verts])
            nf.smooth = smooth
        src.free()

    # ------------------------------------------------------------------ primitives (return temp bmesh)
    @staticmethod
    def sphere(radii=(1, 1, 1), u=16, v=10):
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=1.0)
        bmesh.ops.scale(bm, vec=Vector(radii), verts=bm.verts)
        return bm

    @staticmethod
    def box(size=(1, 1, 1), bevel=0.0, seg=1):
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
        bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r1, radius2=r2,
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
    def prism(points, thickness, bevel=0.0):
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
            bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, offset_type="OFFSET", segments=1,
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

    # ------------------------------------------------------------------ finalise
    def finalize(self, coll, material):
        out = {}
        for name, d in self.objs.items():
            bm = d["bm"]
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
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
            for a in ("wset", "feat"):
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
