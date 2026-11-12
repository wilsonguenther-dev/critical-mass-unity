"""Mesh builder that is authored in UNITY space and written into Blender space.

Unity axes: +X right (sim east), +Y up, +Z forward (sim NORTH, because SimSpace.ToWorld negates sim z).
The FBX export (axis_forward=-Z, axis_up=Y, bake_space_transform) maps Blender -> Unity as
    unity = (-bx, bz, -by)      so      blender = (-ux, -uz, uy).
Every helper here takes Unity coordinates, so the pieces can be reasoned about in the game's own frame.

Camera culling (the follow camera is fixed: pitch 38, yaw 45, orthographic, never rotates):
    TO_CAM (Unity) = (0.557, 0.616, -0.557)
A face whose outward normal has dot(TO_CAM) <= CULL_EPS can never be seen and is not emitted ("cam" mode). That drops
bottoms and the faces pointing -X (west) and +Z (north). Pieces the game rotates (bridge spans, fallen columns,
waterfalls, banners) use "down" mode: only faces pointing down are dropped.
"""
import math
import random

import bmesh
import bpy
from mathutils import Vector

from . import palette

TO_CAM = Vector((math.cos(math.radians(38)) * math.sin(math.radians(45)), math.sin(math.radians(38)),
                 -math.cos(math.radians(38)) * math.cos(math.radians(45))))
CULL_EPS = 0.0


def U(x, y, z):
    """Unity point -> Blender Vector."""
    return Vector((-x, -z, y))


def UN(n):
    return Vector((-n[0], -n[2], n[1]))


class Mesh:
    def __init__(self, name, cull="cam"):
        self.name = name
        self.cull = cull
        self.bm = bmesh.new()
        self.col = self.bm.loops.layers.float_color.new("Col")
        self.xf = []          # stack of 4x4 Unity-space matrices applied to every point

    def push(self, mat):
        self.xf.append(mat)

    def pop(self):
        self.xf.pop()

    def _apply(self, p):
        v = Vector(p)
        for m in reversed(self.xf):
            v = m @ v
        return v

    # ----------------------------------------------------------------- culling
    def visible(self, n):
        n = Vector(n)
        if n.length < 1e-9:
            return False
        n = n.normalized()
        if self.cull == "cam":
            return n.dot(TO_CAM) > CULL_EPS
        if self.cull == "down":
            return n.y > -0.6
        return True

    # ----------------------------------------------------------------- faces
    def poly(self, pts, color, smooth=False, force=False):
        """pts: Unity-space points, counter-clockwise seen from OUTSIDE in a right-handed sense is not required:
        the outward normal is taken from the Newell normal of pts (Unity space) and the winding is fixed after the
        axis change so Blender's normal points the same way."""
        n = Vector((0, 0, 0))
        P = [self._apply(p) for p in pts]
        for i in range(len(P)):
            a, b = P[i], P[(i + 1) % len(P)]
            n.x += (a.y - b.y) * (a.z + b.z)
            n.y += (a.z - b.z) * (a.x + b.x)
            n.z += (a.x - b.x) * (a.y + b.y)
        # Newell gives the normal for a counter-clockwise loop in a right-handed frame; callers pass loops
        # counter-clockwise when viewed from outside in Unity coordinates treated as right-handed math space.
        if not force and not self.visible(n):
            return None
        vs = [self.bm.verts.new(U(*p)) for p in P]
        f = self.bm.faces.new(vs)
        f.normal_update()
        want = UN(n.normalized())
        if f.normal.dot(want) < 0:
            f.normal_flip()
        f.smooth = smooth
        for lp in f.loops:
            lp[self.col] = color
        return f

    def quad(self, a, b, c, d, color, **kw):
        return self.poly([a, b, c, d], color, **kw)

    # ----------------------------------------------------------------- primitives
    def box(self, x0, y0, z0, x1, y1, z1, color, top=None, side_mul=1.0):
        """Axis-aligned box, faces wound outward. top: colour for the +Y face (defaults to color)."""
        top = top or color
        c = color
        # +Y
        self.quad((x0, y1, z0), (x0, y1, z1), (x1, y1, z1), (x1, y1, z0), top)
        # -Y
        self.quad((x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1), c)
        # +X
        self.quad((x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1), c)
        # -X
        self.quad((x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0), c)
        # +Z
        self.quad((x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1), c)
        # -Z
        self.quad((x0, y0, z0), (x0, y1, z0), (x1, y1, z0), (x1, y0, z0), c)

    def cbox(self, x0, y0, z0, x1, y1, z1, color, top=None, ch=0.05):
        """Chunky toy block: a box whose top edges are chamfered by ch (the top face is inset)."""
        top = top or color
        ch = min(ch, (x1 - x0) * 0.45, (z1 - z0) * 0.45, (y1 - y0) * 0.9)
        yt = y1 - ch
        a0, a1, b0, b1 = x0 + ch, x1 - ch, z0 + ch, z1 - ch
        self.quad((a0, y1, b0), (a0, y1, b1), (a1, y1, b1), (a1, y1, b0), top)
        # chamfers
        self.quad((x1, yt, z0), (a1, y1, b0), (a1, y1, b1), (x1, yt, z1), top)     # +X
        self.quad((x0, yt, z0), (x0, yt, z1), (a0, y1, b1), (a0, y1, b0), top)     # -X
        self.quad((x0, yt, z1), (x1, yt, z1), (a1, y1, b1), (a0, y1, b1), top)     # +Z
        self.quad((x0, yt, z0), (a0, y1, b0), (a1, y1, b0), (x1, yt, z0), top)     # -Z
        # sides
        self.quad((x1, y0, z0), (x1, yt, z0), (x1, yt, z1), (x1, y0, z1), color)
        self.quad((x0, y0, z0), (x0, y0, z1), (x0, yt, z1), (x0, yt, z0), color)
        self.quad((x0, y0, z1), (x1, y0, z1), (x1, yt, z1), (x0, yt, z1), color)
        self.quad((x0, y0, z0), (x0, yt, z0), (x1, yt, z0), (x1, y0, z0), color)
        self.quad((x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1), color)

    def prism(self, cx, cz, r, y0, y1, color, n=8, top=None, rot=0.0, r_top=None, smooth=False, ch=0.0):
        """Vertical n-gon prism (cylinder stand-in). r_top tapers it; ch chamfers the top rim."""
        top = top or color
        r_top = r if r_top is None else r_top
        ang = [rot + 2 * math.pi * i / n for i in range(n)]
        yt = y1 - ch
        rb = [(cx + r * math.cos(a), cz + r * math.sin(a)) for a in ang]
        rt = [(cx + r_top * math.cos(a), cz + r_top * math.sin(a)) for a in ang]
        ri = [(cx + (r_top - ch) * math.cos(a), cz + (r_top - ch) * math.sin(a)) for a in ang]
        for i in range(n):
            j = (i + 1) % n
            self.quad((rb[i][0], y0, rb[i][1]), (rt[i][0], yt, rt[i][1]), (rt[j][0], yt, rt[j][1]),
                      (rb[j][0], y0, rb[j][1]), color, smooth=smooth)
            if ch > 0:
                self.quad((rt[i][0], yt, rt[i][1]), (ri[i][0], y1, ri[i][1]), (ri[j][0], y1, ri[j][1]),
                          (rt[j][0], yt, rt[j][1]), top)
        cap = ri if ch > 0 else rt
        self.poly([(p[0], y1, p[1]) for p in reversed(cap)], top)
        self.poly([(p[0], y0, p[1]) for p in rb], color)

    def ring(self, cx, cz, r0, r1, y, color, n=48, a0=0.0, a1=2 * math.pi, h=0.0, side=None):
        """Flat annulus (optionally raised by h with an outer side wall) between angles a0..a1."""
        side = side or color
        for i in range(n):
            t0 = a0 + (a1 - a0) * i / n
            t1 = a0 + (a1 - a0) * (i + 1) / n
            p = lambda r, t, yy: (cx + r * math.cos(t), yy, cz + r * math.sin(t))
            self.quad(p(r0, t0, y + h), p(r0, t1, y + h), p(r1, t1, y + h), p(r1, t0, y + h), color)
            if h > 0:
                self.quad(p(r1, t0, y), p(r1, t0, y + h), p(r1, t1, y + h), p(r1, t1, y), side)

    def blob(self, cx, cy, cz, rx, ry, rz, color, sub=2, smooth=True, seed=0, jitter=0.0, flat_bottom=None):
        """Icosphere ellipsoid (canopies, clouds, egg). flat_bottom clips it at that Unity y."""
        tmp = bmesh.new()
        bmesh.ops.create_icosphere(tmp, subdivisions=sub, radius=1.0)
        rng = random.Random(seed)
        for v in tmp.verts:
            k = 1.0 + (rng.uniform(-jitter, jitter) if jitter else 0.0)
            x, y, z = v.co.x * k, v.co.z * k, v.co.y * k     # treat ico z as Unity y
            yy = cy + y * ry
            if flat_bottom is not None and yy < flat_bottom:
                yy = flat_bottom
            v.co = Vector((cx + x * rx, yy, cz + z * rz))
        for f in tmp.faces:
            pts = [tuple(v.co) for v in reversed(f.verts)]   # the y/z swap mirrors, so reverse the loop
            self.poly(pts, color, smooth=smooth)
        tmp.free()

    def tri(self, a, b, c, color, **kw):
        return self.poly([a, b, c], color, **kw)

    # ----------------------------------------------------------------- output
    def finish(self, collection=None, weld=True):
        bm = self.bm
        if weld:
            bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        me = bpy.data.meshes.new(self.name)
        bm.to_mesh(me)
        bm.free()
        # Corner-domain float colour named Col (the FBX writer exports it; alpha = mask code)
        me.color_attributes.active_color = me.color_attributes["Col"]
        ob = bpy.data.objects.new(self.name, me)
        (collection or bpy.context.scene.collection).objects.link(ob)
        return ob


def C(name, mul=1.0):
    return palette.vcol(name, mul)


def M(loc=(0, 0, 0), yaw=0.0, pitch=0.0, roll=0.0, scale=1.0):
    """Unity-space matrix: translate * rotY(yaw) * rotX(pitch) * rotZ(roll) * scale (degrees; math right-handed
    rotations in the Unity coordinate numbers, which is all the builder needs)."""
    from mathutils import Matrix
    m = Matrix.Translation(Vector(loc))
    m = m @ Matrix.Rotation(math.radians(yaw), 4, "Y") @ Matrix.Rotation(math.radians(pitch), 4, "X")
    m = m @ Matrix.Rotation(math.radians(roll), 4, "Z")
    if scale != 1.0:
        s = scale if isinstance(scale, (tuple, list)) else (scale, scale, scale)
        m = m @ Matrix.Diagonal((s[0], s[1], s[2], 1.0))
    return m
