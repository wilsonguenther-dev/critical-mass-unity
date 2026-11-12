"""Farmer kit: the shared cm_art.kit.Builder plus
  * painted vertical gradient per part (+6 % value at the top, -10 % at the bottom; ART-DIRECTION 3.5), glows stay flat
  * tube(): swept, tapered, optionally flattened tube along a path (hair clumps, hoses, straps, hook arc)
  * bcyl(): cylinder with chamfered (bevelled) cap edges, so no hard 90-degree rim catches the eye at 2x zoom
  * leaf(): bevelled leaf blade
All primitives are closed shells (the verify script rejects loose or non-manifold geometry).
"""
import math

import bmesh
from mathutils import Matrix, Vector

from cm_art import kit
from . import fpalette as FP

GRAD_TOP, GRAD_BOT = 1.06, 0.90
TRI_LOG = {}     # (object, feature, colour) -> [triangles, primitives], for budget work


class FarmerBuilder(kit.Builder):
    def _get(self, obj):
        new = obj not in self.objs
        d = super()._get(obj)
        if new:
            d["uv"] = d["bm"].loops.layers.uv.new("UVMap")
        return d

    def add(self, obj, src, color, bones, feat=None, smooth=True, m=None, grad=True, face_uv=None):
        """face_uv = (cx, cy, cz, half_w, half_h): front planar projection for the painted face decal on faces whose
        centre lies in front of cy (y < cy); every other face samples UV (0.002, 0.002), a transparent texel."""
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
        TRI_LOG.setdefault((obj, feat, color), [0, 0])
        TRI_LOG[(obj, feat, color)][0] += sum(len(f.verts) - 2 for f in src.faces)
        TRI_LOG[(obj, feat, color)][1] += 1
        glow = FP.mask_of(color) in (FP.MASK_FIXED_GLOW, 0.75)
        zs = [v.co.z for v in src.verts]
        z0, z1 = min(zs), max(zs)
        span = max(z1 - z0, 1e-6)
        use_grad = grad and not glow and span > 0.05
        vmap = {}
        for v in src.verts:
            k = GRAD_BOT + (GRAD_TOP - GRAD_BOT) * ((v.co.z - z0) / span) if use_grad else 1.0
            nv = bm.verts.new(v.co)
            nv[d["col"]] = FP.vcol(color, k)
            nv[d["ws"]] = wi
            nv[d["ft"]] = fi
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

    # ------------------------------------------------------------------ extra primitives
    @staticmethod
    def tube(points, radii, seg=8, flat=1.0, up=(0, 0, 1), cap_tip=True):
        """Closed tube along `points` with per-point radius. flat < 1 squashes the section along the frame's 2nd axis."""
        pts = [Vector(p) for p in points]
        if isinstance(radii, (int, float)):
            radii = [radii] * len(pts)
        bm = bmesh.new()
        rings = []
        upv = Vector(up)
        prev_x = None
        for i, c in enumerate(pts):
            if i == 0:
                t = pts[1] - pts[0]
            elif i == len(pts) - 1:
                t = pts[-1] - pts[-2]
            else:
                t = pts[i + 1] - pts[i - 1]
            t.normalize()
            if prev_x is None:
                x = upv.cross(t)
                if x.length < 1e-4:
                    x = Vector((1, 0, 0)).cross(t)
            else:
                x = prev_x - t * prev_x.dot(t)       # parallel transport
            x.normalize()
            y = t.cross(x).normalized()
            prev_x = x
            r = max(radii[i], 0.003)
            ring = [bm.verts.new(c + x * (r * math.cos(2 * math.pi * k / seg)) +
                                 y * (r * flat * math.sin(2 * math.pi * k / seg))) for k in range(seg)]
            rings.append(ring)
        for a in range(len(rings) - 1):
            for k in range(seg):
                j = (k + 1) % seg
                bm.faces.new((rings[a][k], rings[a][j], rings[a + 1][j], rings[a + 1][k]))
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        return bm

    @staticmethod
    def bcyl(a, b, r1, r2, seg=12, bev=0.02):
        """Closed cylinder a->b with 45-degree chamfered cap edges of size `bev`."""
        a, b = Vector(a), Vector(b)
        L = (b - a).length
        bev = min(bev, L * 0.3, r1 * 0.45, r2 * 0.45)
        prof = [(r1 - bev, 0.0), (r1, bev), (r2, L - bev), (r2 - bev, L)]
        bm = kit.Builder.lathe(prof, seg)
        q = (b - a).normalized().to_track_quat("Z", "Y")
        m = Matrix.Translation(a) @ q.to_matrix().to_4x4()
        bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
        return bm

    @staticmethod
    def leaf(length=0.16, width=0.07, thick=0.018, n=5):
        """Leaf blade in local XY (base at origin, tip at +Y), thickness along Z (thin blade, no bevel needed)."""
        right = []
        for i in range(1, n):
            t = i / n
            right.append((width * 0.5 * math.sin(math.pi * t) ** 0.8, length * t))
        pts = [(0.0, 0.0)] + right + [(0.0, length)] + [(-x, y) for x, y in reversed(right)]
        return kit.Builder.prism(pts, thick)


def bezier(p0, p1, p2, p3, n):
    p0, p1, p2, p3 = (Vector(p) for p in (p0, p1, p2, p3))
    out = []
    for i in range(n + 1):
        t = i / n
        u = 1 - t
        out.append(p0 * u ** 3 + p1 * 3 * u * u * t + p2 * 3 * u * t * t + p3 * t ** 3)
    return out
