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
from . import finish
from . import fpalette as FP

# v4: painted finish (copied from the Warrior branch's cm_art/finish.py, unmodified) - Farmer colour -> finish kind
F_KIND = {"f_navy": "cloth", "f_navy_dark": "cloth", "f_cream": "cloth", "f_cream_dark": "cloth",
          "f_team_cloth": "cloth", "f_patch": "cloth", "f_brown": "leather", "f_glove": "leather",
          "f_leather": "leather", "f_boot": "leather", "f_sole": "leather", "f_hose": "leather",
          "f_brass": "metal", "f_frame_dark": "metal", "f_shaft": "wood", "f_wood": "wood", "f_wood_dark": "wood",
          "f_skin": "skin", "f_hair": "hair"}
# the Farmer already bakes a +6/-10 % part gradient (and the hair its top light), so the finish's own top gradient is
# kept small; the rest are the shared defaults
FINISH_PARAMS = {"cloth": {"top": 0.03}, "leather": {"top": 0.03}, "metal": {"top": 0.03}, "wood": {"top": 0.03},
                 "hair": {"ao": 0.16}}

GRAD_TOP, GRAD_BOT = 1.06, 0.90
TRI_LOG = {}     # (object, feature, colour) -> [triangles, primitives], for budget work


class FarmerBuilder(kit.Builder):
    def _get(self, obj):
        new = obj not in self.objs
        d = super()._get(obj)
        if new:
            bm = d["bm"]
            d["uv"] = bm.loops.layers.uv.new("UVMap")
            d["kind"] = bm.verts.layers.int.new("kind")
            d["lco"] = bm.verts.layers.float_vector.new("lco")
            d["skip"] = bm.verts.layers.int.new("fskip")
        return d

    def add(self, obj, src, color, bones, feat=None, smooth=True, m=None, grad=True, face_uv=None, top_light=None):
        """face_uv = (cx, cy, cz, half_w, half_h): front planar projection for the painted face decal on faces whose
        centre lies in front of cy (y < cy); every other face samples UV (0.002, 0.002), a transparent texel."""
        local = {v: v.co.copy() for v in src.verts}
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
        if top_light:                       # v3 hair: light top planes, dark undersides (painted by vertex normal)
            src.normal_update()
        for v in src.verts:
            k = GRAD_BOT + (GRAD_TOP - GRAD_BOT) * ((v.co.z - z0) / span) if use_grad else 1.0
            if top_light:
                t = max(0.0, min(1.0, (v.normal.z + 0.35) / 1.0))
                k = top_light[1] + (top_light[0] - top_light[1]) * t
            nv = bm.verts.new(v.co)
            nv[d["col"]] = FP.vcol(color, k)
            nv[d["ws"]] = wi
            nv[d["ft"]] = fi
            nv[d["kind"]] = finish.KIND[F_KIND.get(color, "other")]
            nv[d["lco"]] = local[v]
            nv[d["skip"]] = 1 if (glow or face_uv is not None or feat == "face") else 0
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

    def finalize(self, coll, material, painted=False):
        """v4: painted=True bakes cm_art finish.painted_finish (top light, crease AO, edge highlights, brass hot spots,
        grain, folds) into the vertex RGB before the meshes are written; alpha (team mask) is never touched."""
        self.finish_stats = {}
        for name, d in self.objs.items():
            if painted:
                bm = d["bm"]
                alpha = [v[d["col"]][3] for v in bm.verts]
                self.finish_stats[name] = finish.painted_finish(bm, d["col"], d["kind"], lco=d["lco"], skip=d["skip"],
                                                                seed=707, params=FINISH_PARAMS)
                assert all(abs(v[d["col"]][3] - a) < 1e-6 for v, a in zip(bm.verts, alpha)), "finish touched alpha"
        out = super().finalize(coll, material)
        for ob in out.values():
            for a in ("kind", "lco", "fskip"):
                if a in ob.data.attributes:
                    ob.data.attributes.remove(ob.data.attributes[a])
        return out

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
