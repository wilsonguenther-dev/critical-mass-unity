"""MinerBuilder: the shared cm_art.kit.Builder plus
  * baked painted gradient per primitive (+6 % value at the top, -10 % at the bottom / crevices; art direction 3.5),
    skipped on team-mask parts so team colours stay exact
  * faceted crystals with one lighter facet plate on the lit side
  * finalize_static() for un-rigged props (the barricade)
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector

from . import mpal
from cm_art import kit

TOP_GAIN = 0.06
BOTTOM_LOSS = 0.10


class MinerBuilder(kit.Builder):
    def __init__(self, arm=None):
        if arm is not None:
            super().__init__(arm)
        else:
            self.arm = None
            self.bones = {}
            self.objs = {}

    def add(self, obj, src, color, bones, feat=None, smooth=True, m=None, grad=True, up=None, post=None):
        """post: matrix applied AFTER the gradient is computed (held items: gradient in item space)."""
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
        rgba = mpal.vcol(color) if isinstance(color, str) else color
        upv = Vector(up) if up is not None else Vector((0, 0, 1))
        hs = [v.co.dot(upv) for v in src.verts]
        h0, h1 = (min(hs), max(hs)) if hs else (0, 1)
        span = max(h1 - h0, 1e-6)
        use_grad = grad and rgba[3] == mpal.MASK_FIXED and span > 0.02
        vmap = {}
        for v, h in zip(src.verts, hs):
            nv = bm.verts.new(v.co)
            if use_grad:
                t = (h - h0) / span
                k = (1.0 - BOTTOM_LOSS) + (BOTTOM_LOSS + TOP_GAIN) * (t ** 0.8)
                nv[d["col"]] = (rgba[0] * k, rgba[1] * k, rgba[2] * k, rgba[3])
            else:
                nv[d["col"]] = rgba
            nv[d["ws"]] = wi
            nv[d["ft"]] = fi
            vmap[v] = nv
        for f in src.faces:
            nf = bm.faces.new([vmap[v] for v in f.verts])
            nf.smooth = smooth
        if post is not None:
            bmesh.ops.transform(bm, matrix=post, verts=list(vmap.values()))
        src.free()

    # ------------------------------------------------------------------ extra primitives
    @staticmethod
    def crystal(r, h, seg=6, waist=0.85, cap=0.32, base=0.12):
        """Closed faceted crystal along local +Z from z=0: short base taper, prism body, pointed cap."""
        prof = [(r * 0.55, 0.0), (r, h * base), (r * waist, h * (1 - cap)), (0.002, h)]
        bm = kit.Builder.lathe(prof, seg)
        return bm

    def add_crystal(self, obj, bones, feat, M4, r, h, light=(-0.5, -0.6, 0.6), seg=6, waist=0.85, cap=0.32, post=None):
        """Crystal at matrix M4 (local +Z = crystal axis) plus a lighter facet plate on the face nearest `light`."""
        self.add(obj, self.crystal(r, h, seg, waist, cap), "crystal", bones, feat, smooth=False, m=M4, post=post)
        # pick the body facet whose outward normal (in world) is closest to the light
        L = Vector(light).normalized()
        best, ba = -9, 0.0
        rot = M4.to_3x3()
        for i in range(seg):
            a = 2 * math.pi * (i + 0.5) / seg
            n = (rot @ Vector((math.cos(a), math.sin(a), 0.0))).normalized()
            if n.dot(L) > best:
                best, ba = n.dot(L), a
        # facet plate: the chosen body facet's four corners, inset 30 % toward its centre, as a thin closed slab
        a0, a1 = ba - math.pi / seg, ba + math.pi / seg
        zb, zt = h * 0.12, h * (1 - cap)
        corners = [Vector((r * math.cos(a0), r * math.sin(a0), zb)), Vector((r * math.cos(a1), r * math.sin(a1), zb)),
                   Vector((r * waist * math.cos(a1), r * waist * math.sin(a1), zt)),
                   Vector((r * waist * math.cos(a0), r * waist * math.sin(a0), zt))]
        c = sum(corners, Vector()) / 4
        corners = [c + (p - c) * 0.62 for p in corners]
        n = (corners[1] - corners[0]).cross(corners[3] - corners[0]).normalized()
        if n.dot(Vector((math.cos(ba), math.sin(ba), 0))) < 0:
            n = -n
        slab = bmesh.new()
        outer = [slab.verts.new(p + n * 0.006) for p in corners]
        inner = [slab.verts.new(p - n * 0.004) for p in corners]
        slab.faces.new(outer)
        slab.faces.new(list(reversed(inner)))
        for k in range(4):
            q = (k + 1) % 4
            slab.faces.new((inner[k], inner[q], outer[q], outer[k]))
        bmesh.ops.recalc_face_normals(slab, faces=slab.faces)
        self.add(obj, slab, "crystal_lit", bones, feat, smooth=False, m=M4, post=post)

    # ------------------------------------------------------------------ static finalize (props)
    def finalize_static(self, coll, material):
        out = {}
        for name, d in self.objs.items():
            bm = d["bm"]
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
            me = bpy.data.meshes.new(name)
            bm.to_mesh(me)
            ob = bpy.data.objects.new(name, me)
            coll.objects.link(ob)
            ca = me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
            cl = bm.verts.layers.float_color["Col"]
            ft = bm.verts.layers.int["feat"]
            bm.verts.ensure_lookup_table()
            for i, v in enumerate(bm.verts):
                ca.data[i].color = v[cl]
            me.color_attributes.active_color = ca
            me.color_attributes.render_color_index = me.color_attributes.find("Col")
            for a in ("wset", "feat"):
                if a in me.attributes:
                    me.attributes.remove(me.attributes[a])
            fgroups = {fi: ob.vertex_groups.new(name="feat_" + f) for fi, f in enumerate(d["feats"])}
            for i, v in enumerate(bm.verts):
                fgroups[v[ft]].add([i], 1.0, "REPLACE")
            me.materials.append(material)
            bm.free()
            out[name] = ob
        return out
