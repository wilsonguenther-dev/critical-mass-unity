"""Shared steps for the HY 3D character builds (fighters: build_hy3d.py, Kaiju adult: build_hy3d_kaiju.py).

Colour: the generated texture is sampled per face (Sampler, bake), then remapped toward the turnaround sheet
(ColourRemap: faces of the decimated mesh that the sheet's front/back view sees are projected onto the sheet; per
colour cluster the Lab median shift sheet - generated is measured, capped, and applied softly to every colour of
the texture and the faces). Team-mask alpha per class (MASK_RULES, ArtMapping.MaskCodes). Decimation keeps the head
dense (decimate_protected: uniform pass until the head holds the wanted share, then the head is locked). Shadow
proxy: closed convex hulls per dominant bone, rigid (the fighter_lods 'hull' method).
"""
import colorsys
import math

import bpy
import bmesh
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree


# ------------------------------------------------------------------------------------------------ mesh basics
def np_verts(ob):
    co = np.zeros(len(ob.data.vertices) * 3)
    ob.data.vertices.foreach_get("co", co)
    return co.reshape(-1, 3)


def set_verts(ob, V):
    ob.data.vertices.foreach_set("co", np.ascontiguousarray(V, np.float32).ravel())
    ob.data.update()


def tri_count(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def edges_of(ob):
    ed = np.zeros(len(ob.data.edges) * 2, np.int32)
    ob.data.edges.foreach_get("vertices", ed)
    return ed.reshape(-1, 2)


def face_centroids(ob, V=None):
    V = np_verts(ob) if V is None else V
    return np.array([V[list(p.vertices)].mean(0) for p in ob.data.polygons])


# ------------------------------------------------------------------------------------------------ texture sampling
def tex_array(img):
    w, h = img.size
    px = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(px)
    return px.reshape(h, w, 4)


class Sampler:
    """Closest point on the high mesh -> UV -> texel (sRGB values as stored in the PNG)."""

    def __init__(self, high, img, tex=None):
        me = high.data
        me.calc_loop_triangles()
        n = len(me.loop_triangles)
        self.V = np_verts(high)
        tv = np.zeros(n * 3, np.int32)
        me.loop_triangles.foreach_get("vertices", tv)
        tl = np.zeros(n * 3, np.int32)
        me.loop_triangles.foreach_get("loops", tl)
        self.tv, self.tl = tv.reshape(-1, 3), tl.reshape(-1, 3)
        uv = np.zeros(len(me.loops) * 2)
        me.uv_layers[0].data.foreach_get("uv", uv)
        self.uv = uv.reshape(-1, 2)
        self.bvh = BVHTree.FromPolygons([tuple(map(float, v)) for v in self.V], [tuple(map(int, t)) for t in self.tv])
        self.tex = tex_array(img) if tex is None else tex

    def colour(self, P):
        out = np.zeros((len(P), 3))
        h, w = self.tex.shape[:2]
        for i, p in enumerate(P):
            loc, _, idx, _ = self.bvh.find_nearest(Vector(p))
            if idx is None:
                continue
            a, b, c = self.V[self.tv[idx]]
            v0, v1, v2 = b - a, c - a, np.array(loc) - a
            d00, d01, d11, d20, d21 = v0 @ v0, v0 @ v1, v1 @ v1, v2 @ v0, v2 @ v1
            den = d00 * d11 - d01 * d01 or 1e-12
            bv = (d11 * d20 - d01 * d21) / den
            bw = (d00 * d21 - d01 * d20) / den
            uvp = (1 - bv - bw) * self.uv[self.tl[idx, 0]] + bv * self.uv[self.tl[idx, 1]] + bw * self.uv[self.tl[idx, 2]]
            x = int(np.clip((uvp[0] % 1.0) * w, 0, w - 1))
            y = int(np.clip((uvp[1] % 1.0) * h, 0, h - 1))
            out[i] = self.tex[y, x, :3]
        return out


BARY = np.array([(1 / 3, 1 / 3, 1 / 3), (2 / 3, 1 / 6, 1 / 6), (1 / 6, 2 / 3, 1 / 6), (1 / 6, 1 / 6, 2 / 3),
                 (1 / 6, 5 / 12, 5 / 12), (5 / 12, 1 / 6, 5 / 12), (5 / 12, 5 / 12, 1 / 6)])


def face_colours(ob, sampler):
    """Per-face sRGB colour: median of 7 samples inside the face (a sample across a paint border does not muddy it)."""
    V = np_verts(ob)
    P = []
    for p in ob.data.polygons:
        vs = V[list(p.vertices)[:3]]
        for b in BARY:
            P.append(b @ vs)
    C = sampler.colour(np.array(P)).reshape(len(ob.data.polygons), len(BARY), 3)
    return np.median(C, 1)


# ------------------------------------------------------------------------------------------------ colour space
def _lin(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def _srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)


_M = np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]])
_WP = np.array([0.95047, 1.0, 1.08883])


def srgb_to_lab(rgb):
    xyz = (_lin(np.asarray(rgb, np.float64)) @ _M.T) / _WP
    f = np.where(xyz > 0.008856, np.cbrt(xyz), 7.787 * xyz + 16 / 116)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], -1)


def lab_to_srgb(lab):
    lab = np.asarray(lab, np.float64)
    fy = (lab[..., 0] + 16) / 116
    fx, fz = fy + lab[..., 1] / 500, fy - lab[..., 2] / 200
    f = np.stack([fx, fy, fz], -1)
    xyz = np.where(f ** 3 > 0.008856, f ** 3, (f - 16 / 116) / 7.787) * _WP
    return _srgb(xyz @ np.linalg.inv(_M).T)


def hexc(rgb):
    r, g, b = (int(round(float(x) * 255)) for x in np.clip(rgb, 0, 1))
    return f"#{r:02X}{g:02X}{b:02X}"


# ------------------------------------------------------------------------------------------------ sheet projection
class Sheet:
    """A transparent turnaround view (front or back) aligned to the fitted mesh by its alpha bounding box."""

    def __init__(self, path, back=False):
        img = bpy.data.images.load(path, check_existing=True)
        self.px = tex_array(img)                  # rows bottom-up
        self.h, self.w = self.px.shape[:2]
        a = self.px[:, :, 3] > 0.5
        ys, xs = np.nonzero(a)
        self.x0, self.x1, self.y0, self.y1 = xs.min(), xs.max(), ys.min(), ys.max()   # y0 = bottom row (feet)
        self.back = back

    def project(self, P, xmin, xmax, height):
        k = (self.y1 - self.y0) / height
        cx_img, cx_m = (self.x0 + self.x1) / 2, (xmin + xmax) / 2
        sx = -1.0 if self.back else 1.0
        x = cx_img + sx * (P[:, 0] - cx_m) * k
        y = self.y0 + P[:, 2] * k
        return x, y

    def sample(self, x, y, r=2):
        out = np.full((len(x), 3), np.nan)
        for i, (xi, yi) in enumerate(zip(x, y)):
            xi, yi = int(round(xi)), int(round(yi))
            if xi < r or yi < r or xi >= self.w - r or yi >= self.h - r:
                continue
            patch = self.px[yi - r:yi + r + 1, xi - r:xi + r + 1].reshape(-1, 4)
            patch = patch[patch[:, 3] > 0.9]
            if len(patch) >= 5:
                out[i] = np.median(patch[:, :3], 0)
        return out


def sheet_colours(ob, front, back, height):
    """Per-face sheet colour (nan where neither view sees the face). ob in the fitted generated pose, facing -Y."""
    V = np_verts(ob)
    me = ob.data
    bvh = BVHTree.FromPolygons([tuple(map(float, v)) for v in V], [tuple(p.vertices) for p in me.polygons])
    Cn = face_centroids(ob, V)
    N = np.array([p.normal[:] for p in me.polygons])
    xmin, xmax = V[:, 0].min(), V[:, 0].max()
    out = np.full((len(Cn), 3), np.nan)
    for sheet, d in ((front, (0, -1, 0)), (back, (0, 1, 0))):
        if sheet is None:
            continue
        dv = Vector(d)
        facing = N @ np.array(d) > 0.35
        vis = np.zeros(len(Cn), bool)
        for i in np.nonzero(facing)[0]:
            o = Vector(Cn[i]) + dv * 0.002
            hit, _, _, _ = bvh.ray_cast(o, dv, 10.0)
            vis[i] = hit is None
        sel = np.nonzero(vis & np.isnan(out[:, 0]))[0]
        x, y = sheet.project(Cn[sel], xmin, xmax, height)
        out[sel] = sheet.sample(x, y)
    return out


class ColourRemap:
    """Per-cluster Lab shift (sheet median - generated median), applied softly by distance to the cluster centres."""

    def __init__(self, gen_rgb, sheet_rgb, k=20, sigma=10.0, cap=(15.0, 12.0), min_samples=60, seed=7):
        L = srgb_to_lab(gen_rgb)
        rng = np.random.default_rng(seed)
        # k-means++ init, 20 Lloyd iterations
        C = [L[rng.integers(len(L))]]
        for _ in range(k - 1):
            d = np.min(((L[:, None, :] - np.array(C)[None]) ** 2).sum(-1), 1)
            C.append(L[rng.choice(len(L), p=d / d.sum())])
        C = np.array(C)
        for _ in range(20):
            lab = np.argmin(((L[:, None, :] - C[None]) ** 2).sum(-1), 1)
            C = np.array([L[lab == j].mean(0) if (lab == j).any() else C[j] for j in range(k)])
        lab = np.argmin(((L[:, None, :] - C[None]) ** 2).sum(-1), 1)
        S = srgb_to_lab(np.nan_to_num(sheet_rgb))
        ok = ~np.isnan(sheet_rgb[:, 0])
        self.C, self.D, self.sigma, self.rows = C, np.zeros_like(C), sigma, []
        for j in range(k):
            m = (lab == j) & ok
            row = dict(cluster=j, faces=int((lab == j).sum()), seen=int(m.sum()), generated=hexc(lab_to_srgb(np.median(L[lab == j], 0))))
            if m.sum() >= min_samples:
                g, s = np.median(L[m], 0), np.median(S[m], 0)
                d = s - g
                d[0] = np.clip(d[0], -cap[0], cap[0])
                d[1:] = np.clip(d[1:], -cap[1], cap[1])
                self.D[j] = d
                row.update(sheet=hexc(lab_to_srgb(s)), shift_lab=[round(float(x), 1) for x in d])
            self.rows.append(row)
        self.labels = lab

    def apply(self, rgb, chunk=262144):
        rgb = np.asarray(rgb, np.float64)
        flat = rgb.reshape(-1, 3)
        out = np.empty_like(flat)
        for s in range(0, len(flat), chunk):
            L = srgb_to_lab(flat[s:s + chunk])
            d2 = ((L[:, None, :] - self.C[None]) ** 2).sum(-1)
            w = np.exp(-d2 / (2 * self.sigma ** 2))
            shift = (w @ self.D) / np.maximum(1.0, w.sum(1))[:, None]
            out[s:s + chunk] = lab_to_srgb(L + shift)
        return out.reshape(rgb.shape)


# ------------------------------------------------------------------------------------------------ team mask
def _hsv(rgb):
    h, s, v = colorsys.rgb_to_hsv(*np.clip(rgb, 0, 1))
    return h * 360, s, v


def mask_warrior(rgb):
    hue, s, v = _hsv(rgb)
    if 170 <= hue <= 205 and s > 0.30 and v > 0.80:
        return 0.75                                     # team glow: gems, blade edge
    if 178 <= hue <= 205 and s > 0.45 and 0.35 < v <= 0.80:
        return 1.0                                      # team cloth: scarf, tabard
    return 0.0


def mask_farmer(rgb):
    hue, s, v = _hsv(rgb)
    if 60 <= hue <= 110 and s > 0.50 and v > 0.55:
        return 0.25                                     # fixed biomass glow (orb, never swapped)
    if 172 <= hue <= 205 and s > 0.30 and 0.35 < v <= 0.90:
        return 1.0                                      # team cloth: teal trims
    return 0.0


def mask_miner(rgb):
    hue, s, v = _hsv(rgb)
    if 190 <= hue <= 230 and s > 0.45 and v > 0.75:
        return 0.75                                     # team glow: crystals (helmet, chest)
    if 172 <= hue <= 225 and s > 0.35 and 0.42 < v <= 0.75:
        return 1.0                                      # team cloth: teal / blue trims (navy overalls stay below v 0.42)
    return 0.0


GLOW_AZURE = (0x13 / 255, 0xF1 / 255, 0xF4 / 255)        # ArtMapping.GlowAzure: the adult's authored stage glow


def mask_kaiju(rgb):
    hue, s, v = _hsv(rgb)
    if 55 <= hue <= 115 and s > 0.45 and v > 0.55:
        return 0.75                                     # glowing seams and crystals (emission mask)
    return 0.0


MASK_RULES = dict(Warrior=mask_warrior, Farmer=mask_farmer, Miner=mask_miner, Kaiju=mask_kaiju)


def write_vcol(ob, C, mask_fn, recolour_glow=None):
    """Per-face colour C (sRGB) + mask alpha into the corner attribute 'Col'. Returns face counts per code."""
    me = ob.data
    if "Col" in me.color_attributes:
        me.color_attributes.remove(me.color_attributes["Col"])
    attr = me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    cols = np.zeros((len(me.loops), 4), np.float32)
    codes = {}
    for p in me.polygons:
        a = mask_fn(C[p.index])
        rgb = C[p.index]
        if recolour_glow is not None and a == 0.75:
            rgb = recolour_glow
        codes[a] = codes.get(a, 0) + 1
        for li in p.loop_indices:
            cols[li] = (*rgb, a)
    attr.data.foreach_set("color_srgb", cols.ravel())
    me.color_attributes.active_color_name = "Col"
    me.color_attributes.render_color_index = me.color_attributes.find("Col")
    return {str(k): v for k, v in sorted(codes.items())}


def read_face_vcol(ob):
    a = ob.data.color_attributes["Col"]
    c = np.zeros(len(a.data) * 4, np.float32)
    a.data.foreach_get("color_srgb", c)
    c = c.reshape(-1, 4)
    return np.array([c[p.loop_indices[0]] for p in ob.data.polygons])


# ------------------------------------------------------------------------------------------------ decimation
def _decimate_once(ob, ratio, group=None, invert=False):
    mod = ob.modifiers.new("dec", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = ratio
    mod.use_collapse_triangulate = True
    if group:
        mod.vertex_group = group
        mod.vertex_group_factor = 1.0
        mod.invert_vertex_group = invert
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
    ob.modifiers.remove(mod)
    old = ob.data
    ob.data = me
    bpy.data.meshes.remove(old)


def decimate_protected(src, target, name, scene, protect=None, share=0.0, tmax=None):
    """Copy of src decimated to about `target` triangles. protect(V) -> bool per vertex (rest coordinates of this
    mesh): when given, a uniform pass first brings the protected region to `share` of the target, then the region is
    locked (decim weight 0) while the rest collapses to the target. Returns (object, info)."""
    tmax = tmax or target
    me = src.data.copy()
    me.name = name
    ob = bpy.data.objects.new(name, me)
    scene.collection.objects.link(ob)
    ob.vertex_groups.clear()      # bone groups of a skinned source broke the head lock (run 2: LOD0 head 298 of 1,183 tris)
    info = dict(start=tri_count(ob))

    def region_tris():
        P = protect(np_verts(ob))
        return sum(1 for p in ob.data.polygons if all(P[i] for i in p.vertices)), P

    def locked_pass(ratio, lock):
        """One collapse pass with the vertices in `lock` locked (decim weight 0, not inverted; measured: keeps them)."""
        g = ob.vertex_groups.get("decim") or ob.vertex_groups.new(name="decim")
        g.add(list(range(len(ob.data.vertices))), 1.0, "REPLACE")
        idx = [int(i) for i in np.nonzero(lock)[0]]
        if idx:
            g.add(idx, 0.0, "REPLACE")
        _decimate_once(ob, ratio, "decim")

    if protect is not None and share > 0:
        # A uniform pass eats the dense head first (run 3: LOD0 head fell from 29 % to 8 %). So: stage A collapses
        # only the region (rest locked) down to its share of the target; stage B collapses only the rest (region locked).
        rt, P = region_tris()
        info["region_frac_before"] = round(rt / max(1, tri_count(ob)), 3)
        want = int(share * target)
        if rt > want:
            n = tri_count(ob)
            locked_pass(max(0.01, (n - (rt - want)) / n), ~P)
        info["stageA_tris"] = tri_count(ob)
        info["stageA_region"] = region_tris()[0]
    ratio = target / max(1, tri_count(ob))
    for attempt in range(6):
        if ratio >= 0.999:
            break
        work = ob.data.copy()
        if protect is not None and share > 0:
            locked_pass(ratio, region_tris()[1])
        else:
            _decimate_once(ob, ratio)
        got = tri_count(ob)
        if got <= tmax:
            bpy.data.meshes.remove(work)
            break
        old = ob.data
        ob.data = work
        bpy.data.meshes.remove(old)
        ratio *= 0.98 * target / got
    g = ob.vertex_groups.get("decim")
    if g:
        ob.vertex_groups.remove(g)
    for p in ob.data.polygons:
        p.use_smooth = True
    if protect is not None and share > 0:
        rt, _ = region_tris()
        info["region_tris"] = rt
        info["region_share"] = round(rt / max(1, tri_count(ob)), 3)
    info["tris"] = tri_count(ob)
    return ob, info


# ------------------------------------------------------------------------------------------------ shadow proxy
def hull_proxy(V, W, bones, name, scene, budget=780, min_verts=8):
    """Closed convex hull per dominant bone of the given rest vertices, collapsed to the budget; rigid weights.
    Returns an object with vertex groups named after the bones (no modifiers)."""
    dom = W.argmax(1)
    bm = bmesh.new()
    hb = bm.verts.layers.int.new("hb")
    for g in np.unique(dom):
        idx = np.nonzero(dom == g)[0]
        if len(idx) < min_verts:
            continue
        vs = []
        for p in V[idx]:
            v = bm.verts.new(tuple(map(float, p)))
            v[hb] = int(g)
            vs.append(v)
        r = bmesh.ops.convex_hull(bm, input=vs)
        junk = list({e for e in r["geom_interior"] + r["geom_unused"] if isinstance(e, bmesh.types.BMVert) and e.is_valid})
        if junk:
            bmesh.ops.delete(bm, geom=junk, context="VERTS")
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    scene.collection.objects.link(ob)
    full = tri_count(ob)
    ratio = min(1.0, budget / max(1, full))
    work = ob.data.copy()
    for attempt in range(6):
        if ratio < 0.999:
            _decimate_once(ob, ratio)
        got = tri_count(ob)
        if got <= budget:
            break
        old = ob.data
        ob.data = work.copy()
        bpy.data.meshes.remove(old)
        ratio *= 0.98 * budget / got
    bpy.data.meshes.remove(work)
    ob.data.name = name
    hbv = np.zeros(len(ob.data.vertices), np.int32)
    ob.data.attributes["hb"].data.foreach_get("value", hbv)
    ob.data.attributes.remove(ob.data.attributes["hb"])
    for g in np.unique(hbv):
        vg = ob.vertex_groups.new(name=bones[int(g)])
        vg.add(np.nonzero(hbv == g)[0].tolist(), 1.0, "REPLACE")
    for p in ob.data.polygons:
        p.use_smooth = False
    return ob, dict(hull_tris=full, tris=tri_count(ob))


def constant_uv(ob, uv=(0.01, 0.01), name="UVMap"):
    """UV0 on one transparent texel of the face decal: no decal draws on this mesh and every vertex stays lined."""
    me = ob.data
    while len(me.uv_layers):
        me.uv_layers.remove(me.uv_layers[0])
    lay = me.uv_layers.new(name=name)
    lay.data.foreach_set("uv", np.tile(np.array(uv, np.float32), len(me.loops)))


def drop_membrane(ob, img, head_rgb_test, keep_head_frac=0.3):
    """Delete the pale, unsaturated faces away from the prop's head end. The Farmer staff input enclosed the background
    inside the hose loop (only border-connected background was cut), and the generator built it as a pale sheet.
    head_rgb_test(rgb) marks faces of the head end (the lime orbs). Returns the number of faces removed."""
    me = ob.data
    tex = tex_array(img)
    h, w = tex.shape[:2]
    uv = np.zeros(len(me.loops) * 2)
    me.uv_layers[0].data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    V = np_verts(ob)
    cen = np.array([V[list(p.vertices)].mean(0) for p in me.polygons])
    col = np.array([tex[int(np.clip((uv[list(p.loop_indices)].mean(0)[1] % 1) * h, 0, h - 1)),
                        int(np.clip((uv[list(p.loop_indices)].mean(0)[0] % 1) * w, 0, w - 1)), :3] for p in me.polygons])
    c = cen.mean(0)
    _, U = np.linalg.eigh(np.cov((cen - c).T))
    ax = U[:, -1]
    t = (cen - c) @ ax
    head = np.array([head_rgb_test(x) for x in col])
    if head.any() and t[head].mean() < 0:
        t = -t
    t = (t.max() - t) / max(np.ptp(t), 1e-9)              # 0 at the head end, 1 at the butt
    hsv = np.array([colorsys.rgb_to_hsv(*np.clip(x, 0, 1)) for x in col])
    kill = (hsv[:, 1] < 0.18) & (hsv[:, 2] > 0.62) & (t > keep_head_frac)
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in np.nonzero(kill)[0]], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(me)
    bm.free()
    me.update()
    return int(kill.sum())
