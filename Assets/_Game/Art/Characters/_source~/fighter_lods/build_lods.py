# Critical Mass fighter LODs (contract C4, FighterLod.cs on feat/perf-and-feel), Blender 5.2 headless, ONE process.
# For every model: import today's FBX, join its meshes into <Base>_Hero (unchanged geometry), derive <Base>_LOD0,
# <Base>_LOD1 and <Base>_Shadow by planar + collapse decimation with protection weights (face decal region, hands,
# team-mask and colour borders, sharp silhouette edges), then resample colour (alpha mask code snapped, never blended),
# UVs, corner normals, material slots and skin weights (top 4) from the Hero surface by nearest point. Export with the
# original export settings, render each level at the gameplay camera, dump raw measurements (edge stretch per pose,
# mask areas) for analyse_lods.py, and re-import every output for a round-trip check.
# Run: cm-heavy blender perl -e 'alarm 1700; exec @ARGV' Blender -b --factory-startup -P build_lods.py -- <scratch>
import bpy, bmesh, sys, os, json, math, time, traceback
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

ROOT = sys.argv[sys.argv.index("--") + 1]
ONLY = sys.argv[sys.argv.index("--") + 2].split(",") if len(sys.argv) > sys.argv.index("--") + 2 else None
SRC, OUT, REN, DATA = (os.path.join(ROOT, d) for d in ("src", "out", "renders", "data"))
for d in (OUT, REN, DATA):
    os.makedirs(d, exist_ok=True)

FIGHTER = dict(LOD0=(4450, 4500), LOD1=(1490, 1500), Shadow=(780, 800))
MODELS = [
    dict(key="Warrior", base="Warrior", fbx="Warrior.fbx", face="Warrior_Face.png", rel="Characters/Warrior.fbx", kind="fighter"),
    dict(key="Miner", base="Miner", fbx="Miner.fbx", face="MinerFace.png", face_slot="Face", rel="Miner/Miner.fbx", kind="fighter"),
    dict(key="Farmer", base="Farmer", fbx="Farmer.fbx", face="Farmer_Face.png", blend="farmer.blend",
         rel="Farmer/Farmer.fbx", kind="fighter"),
    dict(key="KaijuAdult", base="Kaiju", fbx="Kaiju_Adult.fbx", face="Kaiju_Face.png", rel="Kaiju/Kaiju_Adult.fbx", kind="kaiju",
         budget=dict(LOD0=(8000, 12000), LOD1=(4000, 4500), Shadow=(1400, 1500)), crop=440, big=2),
    dict(key="KaijuHatchling", base="Kaiju", fbx="Kaiju_Hatchling.fbx", face="Kaiju_Face.png", rel="Kaiju/Kaiju_Hatchling.fbx",
         kind="kaiju", crop=220, big=3),
    dict(key="Demolitions", base="Demolitions", fbx="Demolitions.fbx", face="DemolitionsFace.png", rel="Demolitions/Demolitions.fbx", kind="fighter"),
    dict(key="Builder", base="Builder", fbx="Builder.fbx", face="BuilderFace.png", rel="Builder/Builder.fbx", kind="fighter"),
    dict(key="Healer", base="Healer", fbx="Healer.fbx", face="HealerFace.png", rel="Healer/Healer.fbx", kind="fighter"),
]

# The existing exports' settings (cm_art/export.py for the fighters, build_kaiju.py export() for the Kaiju).
FIGHTER_FBX = dict(object_types={"ARMATURE", "MESH"}, use_mesh_modifiers=False, apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL", global_scale=1.0, axis_forward="-Z", axis_up="Y", bake_space_transform=True,
    mesh_smooth_type="FACE", use_tspace=False, colors_type="SRGB", prioritize_active_color=True, add_leaf_bones=False,
    primary_bone_axis="Y", secondary_bone_axis="X", use_armature_deform_only=False, armature_nodetype="NULL",
    bake_anim=False, use_custom_props=False, path_mode="AUTO", embed_textures=False)
KAIJU_FBX = dict(object_types={"ARMATURE", "MESH"}, global_scale=1.0, apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=False,
    use_mesh_modifiers=False, mesh_smooth_type="FACE", use_tspace=False, colors_type="SRGB", prioritize_active_color=True,
    add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X", use_armature_deform_only=False, bake_anim=True,
    bake_anim_use_all_bones=True, bake_anim_use_nla_strips=True, bake_anim_use_all_actions=False,
    bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=1.0, path_mode="STRIP",
    embed_textures=False)

# Protection weights per level: (face, mask border, hand, colour border, sharp edge).
# Binary: soft weights made the additive vertex-group cost outrank the shape error and shattered LOD1 (run 1).
# LOD1 (beyond 25 u, face about 2 to 3 px): no face protection, every triangle goes to the silhouette (run 3/4 gate).
PROTECT = dict(LOD0=(1.0, 1.0, 0.0, 0.0, 0.0), LOD1=(0.0, 0.0, 0.0, 0.0, 0.0), Shadow=(0.0, 0.0, 0.0, 0.0, 0.0))
# Fighters are segmented parts (many islands): LOD1 and Shadow can also start from a voxel shell (fingers merge).
# METHOD[kind][level] lists the variants built; the first is exported, the others are rendered for comparison only.
# Run 2 measured: the voxel LOD1 lost colour and shape (overlap 0.30 to 0.90); the voxel Shadow won (0.93 to 0.96).
# Run 5 (posed): the voxel Shadow tore at the joints (850 to 1,170 edges, bridges between parts); welded decimation cannot
# bridge parts, so it ships and the voxel shell is rendered as a comparison only.
METHOD = dict(fighter=dict(LOD0=["dec"], LOD1=["dec"], Shadow=["dec", "vox"]),
              kaiju=dict(LOD0=["dec"], LOD1=["dec"], Shadow=["dec"]))
VOXEL_M = dict(LOD1=0.012, Shadow=0.02)
PLANAR_DEG = dict(LOD0=2.0, LOD1=5.0, Shadow=10.0)
DROP_ISLAND_M = dict(LOD0=0.0, LOD1=0.045, Shadow=0.09)
VG_FACTOR = 1.0
HAND_BONES = ("hand.l", "hand.r")
ISLANDS = {}


def log(*a):
    print("[LOD]", " ".join(str(x) for x in a), flush=True)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_fbx(path, bake, anim):
    before_o, before_a = set(bpy.data.objects), set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=path, bake_space_transform=bake, use_anim=anim, use_custom_normals=True,
                             colors_type="SRGB", ignore_leaf_bones=False, automatic_bone_orientation=False,
                             primary_bone_axis="Y", secondary_bone_axis="X")
    return [o for o in bpy.data.objects if o not in before_o], [a for a in bpy.data.actions if a not in before_a]


def col_attr(me):
    a = me.color_attributes.get("Col")
    if a is None and len(me.color_attributes):
        a = me.color_attributes[me.color_attributes.active_color_index] if me.color_attributes.active_color_index >= 0 else me.color_attributes[0]
    return a


def lin2srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def srgb2lin(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.04045, c / 12.92, np.power((c + 0.055) / 1.055, 2.4))


def get_col(me, LV):
    a = col_attr(me)
    if a is None:
        return None, None
    buf = np.empty(len(a.data) * 4, np.float32)
    try:
        a.data.foreach_get("color_srgb", buf)
        buf = buf.reshape(-1, 4)
    except Exception:
        a.data.foreach_get("color", buf)
        buf = buf.reshape(-1, 4)
        buf[:, :3] = lin2srgb(buf[:, :3])
    if a.domain == "POINT":
        buf = buf[LV]
    return buf, a.name


def set_col(me, name, cols):
    a = me.color_attributes.get(name)
    if a is not None and (a.domain != "CORNER" or a.data_type != "BYTE_COLOR"):
        me.color_attributes.remove(a)
        a = None
    if a is None:
        a = me.color_attributes.new(name, "BYTE_COLOR", "CORNER")
    flat = np.ascontiguousarray(cols, np.float32).ravel()
    try:
        a.data.foreach_set("color_srgb", flat)
    except Exception:
        c = cols.copy()
        c[:, :3] = srgb2lin(c[:, :3])
        a.data.foreach_set("color", np.ascontiguousarray(c, np.float32).ravel())
    try:
        me.color_attributes.active_color_name = name
        me.color_attributes.render_color_index = me.color_attributes.find(name)
    except Exception:
        pass


def arr(coll, attr, n, width, dtype=np.float32):
    b = np.empty(n * width, dtype)
    coll.foreach_get(attr, b)
    return b.reshape(-1, width) if width > 1 else b


def components(nv, edges):
    """Connected component label per vertex (label propagation with pointer jumping)."""
    lab = np.arange(nv)
    if len(edges) == 0:
        return lab
    for _ in range(5000):
        old = lab.copy()
        mn = np.minimum(lab[edges[:, 0]], lab[edges[:, 1]])
        np.minimum.at(lab, edges[:, 0], mn); np.minimum.at(lab, edges[:, 1], mn)
        lab = lab[lab]
        if np.array_equal(lab, old):
            break
    return lab


class Ref:
    """The Hero surface as arrays + BVH, for nearest-point resampling."""
    def __init__(self, ob):
        me = ob.data
        me.calc_loop_triangles()
        nv, nl, nt, npo = len(me.vertices), len(me.loops), len(me.loop_triangles), len(me.polygons)
        self.V = arr(me.vertices, "co", nv, 3)
        self.TV = arr(me.loop_triangles, "vertices", nt, 3, np.int32)
        self.TL = arr(me.loop_triangles, "loops", nt, 3, np.int32)
        self.TP = arr(me.loop_triangles, "polygon_index", nt, 1, np.int32)
        self.LV = arr(me.loops, "vertex_index", nl, 1, np.int32)
        act = me.color_attributes.active_color
        self.colname = act.name if act is not None else (col_attr(me).name if col_attr(me) else None)
        self.cols = {}
        for a in list(me.color_attributes):
            buf = np.empty(len(a.data) * 4, np.float32)
            try:
                a.data.foreach_get("color_srgb", buf); buf = buf.reshape(-1, 4)
            except Exception:
                a.data.foreach_get("color", buf); buf = buf.reshape(-1, 4); buf[:, :3] = lin2srgb(buf[:, :3])
            self.cols[a.name] = buf[self.LV] if a.domain == "POINT" else buf
        self.col = self.cols.get(self.colname)
        self.uvs = {l.name: arr(l.data, "uv", nl, 2) for l in me.uv_layers}
        self.nor = arr(me.corner_normals, "vector", nl, 3)
        self.PM = arr(me.polygons, "material_index", npo, 1, np.int32)
        self.groups = [g.name for g in ob.vertex_groups]
        self.W = np.zeros((nv, max(1, len(self.groups))), np.float32)
        for v in me.vertices:
            for g in v.groups:
                self.W[v.index, g.group] = g.weight
        self.bvh = BVHTree.FromPolygons([tuple(map(float, v)) for v in self.V], [tuple(map(int, t)) for t in self.TV],
                                        all_triangles=True)
        # welded parts: vertices split along hard edges belong to the same part
        _, weld = np.unique(np.round(self.V / 1e-5).astype(np.int64), axis=0, return_inverse=True)
        weld = weld.ravel()
        E = arr(me.edges, "vertices", len(me.edges), 2, np.int32)
        comp = components(int(weld.max()) + 1, weld[E])
        self.isl = comp[weld]
        self.tri_isl = self.isl[self.TV[:, 0]]
        self._ibvh = {}
        self.kd = KDTree(nv)
        for i, v in enumerate(self.V):
            self.kd.insert(Vector(v), i)
        self.kd.balance()

    def island_bvh(self, i):
        if i not in self._ibvh:
            tris = np.nonzero(self.tri_isl == i)[0]
            self._ibvh[i] = (BVHTree.FromPolygons([tuple(map(float, v)) for v in self.V],
                                                  [tuple(map(int, t)) for t in self.TV[tris]], all_triangles=True), tris)
        return self._ibvh[i]

    def bary(self, p, isl=-1, nrm=None):
        if isl is None or isl < 0:
            b, tris = self.bvh, None
        else:
            b, tris = self.island_bvh(int(isl))
        loc, n, li, d = b.find_nearest(p)
        if nrm is not None:
            best = None
            for (l2, n2, t2, d2) in b.find_nearest_range(p, d * 1.5 + 0.004):
                pen = d2 + (0.02 if n2.dot(nrm) < 0.2 else 0.0)
                if best is None or pen < best[0]:
                    best = (pen, l2, n2, t2, d2)
            if best is not None:
                _, loc, n, li, d = best
        ti = int(li) if tris is None else int(tris[li])
        a, b, c = (Vector(self.V[i]) for i in self.TV[ti])
        v0, v1, v2 = b - a, c - a, loc - a
        d00, d01, d11, d20, d21 = v0.dot(v0), v0.dot(v1), v1.dot(v1), v2.dot(v0), v2.dot(v1)
        den = d00 * d11 - d01 * d01
        if abs(den) < 1e-20:
            w = np.array([1 / 3, 1 / 3, 1 / 3])
        else:
            y = (d11 * d20 - d01 * d21) / den
            z = (d00 * d21 - d01 * d20) / den
            w = np.clip(np.array([1 - y - z, y, z]), 0, 1)
            w = w / max(w.sum(), 1e-9)
        return ti, w, d


def island_assign(ref, ob, method):
    me = ob.data
    nv = len(me.vertices)
    if method == "vox" or nv == 0:
        return np.full(nv, -1)
    co = arr(me.vertices, "co", nv, 3)
    lab = components(nv, arr(me.edges, "vertices", len(me.edges), 2, np.int32))
    hit = np.array([ref.tri_isl[ref.bvh.find_nearest(Vector(v))[2]] for v in co])
    out = np.empty(nv, np.int64)
    for l in np.unique(lab):
        sel = lab == l
        vals, cnt = np.unique(hit[sel], return_counts=True)
        out[sel] = vals[np.argmax(cnt)]
    return out


def resample(ref, ob, isl=None):
    """Colour, UVs, normals, material per face and skin weights of a decimated mesh, from the Hero surface."""
    me = ob.data
    nv, nl, npo = len(me.vertices), len(me.loops), len(me.polygons)
    co = arr(me.vertices, "co", nv, 3)
    LV = arr(me.loops, "vertex_index", nl, 1, np.int32)
    LS = arr(me.polygons, "loop_start", npo, 1, np.int32)
    LT = arr(me.polygons, "loop_total", npo, 1, np.int32)
    cols = {k: np.zeros((nl, 4), np.float32) for k in ref.cols}
    uvs = {k: np.zeros((nl, 2), np.float32) for k in ref.uvs}
    nor = np.zeros((nl, 3), np.float32)
    mats = np.zeros(npo, np.int32)
    PN = arr(me.polygons, "normal", npo, 3)
    for p in range(npo):
        ls = range(LS[p], LS[p] + LT[p])
        fn = Vector(PN[p])
        c = Vector(co[LV[list(ls)]].mean(axis=0))
        ti, w, d = ref.bary(c, isl[LV[LS[p]]], fn)
        mats[p] = ref.PM[ref.TP[ti]]
        for l in ls:
            q = Vector(co[LV[l]]) * 0.75 + c * 0.25
            ti, w, d = ref.bary(q, isl[LV[l]], fn)
            L3 = ref.TL[ti]
            for k, rc in ref.cols.items():
                cols[k][l, :3] = w @ rc[L3, :3]
                cols[k][l, 3] = round(float(rc[L3[int(np.argmax(w))], 3]) * 8.0) / 8.0
            for k in uvs:
                uvs[k][l] = w @ ref.uvs[k][L3]
            n = w @ ref.nor[L3]
            nor[l] = n / max(np.linalg.norm(n), 1e-9)
    me.polygons.foreach_set("material_index", mats)
    for a in list(me.color_attributes):
        if a.name not in cols:
            me.color_attributes.remove(a)
    for k in [n for n in ref.cols if n != ref.colname] + ([ref.colname] if ref.colname in cols else []):
        set_col(me, k, cols[k])
    for k, u in uvs.items():
        if k not in me.uv_layers:
            me.uv_layers.new(name=k)
        me.uv_layers[k].data.foreach_set("uv", u.ravel())
    # skin weights: barycentric blend of the Hero triangle under each vertex, top 4, normalised
    for g in list(ob.vertex_groups):
        ob.vertex_groups.remove(g)
    vgs = [ob.vertex_groups.new(name=n) for n in ref.groups]
    maxinf = 0
    for v in range(nv):
        ti, w, d = ref.bary(Vector(co[v]), isl[v])
        wv = w @ ref.W[ref.TV[ti]]
        idx = np.argsort(-wv)[:4]
        idx = [i for i in idx if wv[i] > 1e-4]
        if not idx:
            idx = [int(np.argmax(ref.W[ref.TV[ti][int(np.argmax(w))]]))]
            wv = np.zeros_like(wv); wv[idx[0]] = 1.0
        s = sum(wv[i] for i in idx)
        maxinf = max(maxinf, len(idx))
        for i in idx:
            vgs[i].add([v], float(wv[i] / s), "REPLACE")
    # normals: every face smooth, corner normals carried from the Hero (flat facets stay flat where the Hero is flat)
    for a in ("custom_normal",):
        if a in me.attributes:
            me.attributes.remove(me.attributes[a])
    me.polygons.foreach_set("use_smooth", np.ones(npo, bool))
    me.normals_split_custom_set([tuple(map(float, n)) for n in nor])
    me.update()
    return maxinf


def decal_alpha(img_px, uv):
    h, w = img_px.shape[:2]
    x = np.clip((uv[:, 0] * w).astype(int), 0, w - 1)
    y = np.clip((uv[:, 1] * h).astype(int), 0, h - 1)
    return img_px[y, x, 3]


def load_px(path):
    img = bpy.data.images.load(path, check_existing=True)
    px = np.empty(img.size[0] * img.size[1] * 4, np.float32)
    img.pixels.foreach_get(px)
    return img, px.reshape(img.size[1], img.size[0], 4)


SIDE_FRAMES = [(p, s) for p in ((0, 2, 1), (0, 1, 2)) for s in ((1, 1, 1), (-1, 1, -1), (1, 1, -1), (-1, 1, 1),
                                                                 (1, -1, 1), (-1, -1, -1), (1, -1, -1), (-1, -1, 1))]


def sidecar_unlined(ref, path):
    """Hero vertices listed unlined in the outline_w sidecar (frame found by best match)."""
    side = json.load(open(path))
    pts = np.concatenate([np.array(e["unlined"], np.float32).reshape(-1, 3) for e in side["meshes"]] or [np.zeros((0, 3))])
    if not len(pts):
        return set(), None, 0.0
    best = (-1, None, set())
    for perm, sg in SIDE_FRAMES:
        mapped = ref.V[:, perm] * np.array(sg, np.float32)
        kd = KDTree(len(mapped))
        for i, v in enumerate(mapped):
            kd.insert(Vector(v), i)
        kd.balance()
        hit, idx = 0, set()
        for p in pts:
            for (_, i, d) in kd.find_range(Vector(p), 0.002):
                idx.add(i)
            if kd.find(Vector(p))[2] < 0.002:
                hit += 1
        if hit > best[0]:
            best = (hit, (perm, sg), idx)
    return best[2], best[1], best[0] / len(pts)


def protect_values(ob, ref, m, face_px):
    me = ob.data
    nv = len(me.vertices)
    face = np.zeros(nv, bool)
    uv0 = next(iter(ref.uvs.values())) if ref.uvs else None
    if face_px is not None and uv0 is not None:
        a = decal_alpha(face_px, uv0) > 0.05
        if m.get("face_slot"):
            slots = [i for i, s in enumerate(ob.material_slots) if s.material and m["face_slot"] in s.material.name]
            polys = arr(me.polygons, "material_index", len(me.polygons), 1, np.int32)
            for p in me.polygons:
                if polys[p.index] in slots:
                    face[list(p.vertices)] = True
        else:
            face[ref.LV[a]] = True
    E = arr(me.edges, "vertices", len(me.edges), 2, np.int32)
    grown = face.copy()
    grown[E[face[E[:, 0]], 1]] = True
    grown[E[face[E[:, 1]], 0]] = True
    face = grown
    hand = np.zeros(nv, bool)
    for hb in HAND_BONES:
        if hb in ref.groups:
            hand |= ref.W[:, ref.groups.index(hb)] > 0.5
    mask = np.zeros(nv, bool)
    colb = np.zeros(nv, bool)
    if ref.col is not None:
        code = np.round(ref.col[:, 3] * 8).astype(int)
        cmin = np.full(nv, 99); cmax = np.full(nv, -1)
        np.minimum.at(cmin, ref.LV, code); np.maximum.at(cmax, ref.LV, code)
        mask = cmax > cmin
        lum = ref.col[:, :3]
        rmin = np.full((nv, 3), 9.0); rmax = np.full((nv, 3), -9.0)
        np.minimum.at(rmin, ref.LV, lum); np.maximum.at(rmax, ref.LV, lum)
        colb = (rmax - rmin).max(axis=1) > 0.12
    sharp = np.zeros(nv, bool)
    bm = bmesh.new(); bm.from_mesh(me)
    for e in bm.edges:
        if len(e.link_faces) == 2 and e.calc_face_angle(0) > math.radians(50):
            sharp[e.verts[0].index] = sharp[e.verts[1].index] = True
    bm.free()
    return dict(face=face, mask=mask, hand=hand, colour=colb, sharp=sharp)


def level_protect(flags, level):
    f, mk, h, c, s = PROTECT[level]
    p = np.zeros(len(flags["face"]), np.float32)
    for w, k in ((f, "face"), (mk, "mask"), (h, "hand"), (c, "colour"), (s, "sharp")):
        p = np.maximum(p, np.where(flags[k], w, 0.0))
    return p


def tri_count(me):
    me.calc_loop_triangles()
    return len(me.loop_triangles)


def make_lod(hero, ref, rig, prot, level, budget, name, invert, method="dec", scale=1.0):
    target, tmax = budget
    ob = hero.copy()
    ob.data = hero.data.copy()
    ob.name = ob.data.name = name
    bpy.context.scene.collection.objects.link(ob)
    ob.modifiers.clear()
    me = ob.data
    hard = prot >= 0.99
    dropped = 0
    if method == "vox":
        # voxel shell of the whole character (one island; fingers and touching parts merge), then collapse
        md = ob.modifiers.new("rm", "REMESH"); md.mode = "VOXEL"; md.voxel_size = VOXEL_M[level] * scale
        md.adaptivity = 0.0
        dg = bpy.context.evaluated_depsgraph_get(); dg.update()
        new = bpy.data.meshes.new_from_object(ob.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
        ob.modifiers.remove(md); ob.data = new; bpy.data.meshes.remove(me); me = new; me.name = name
        if len(me.materials) == 0:
            for mt in hero.data.materials:
                me.materials.append(mt)
    else:
        bm = bmesh.new(); bm.from_mesh(me)
        bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-5)
        bm.verts.ensure_lookup_table()
        # 1) drop small islands (LOD1 / Shadow: accessories under a few cm); never an island carrying the face
        thr = DROP_ISLAND_M[level]
        if thr > 0:
            seen = set()
            kill = []
            for f in bm.faces:
                if f.index in seen:
                    continue
                stack, isl = [f], []
                seen.add(f.index)
                while stack:
                    g = stack.pop(); isl.append(g)
                    for e in g.edges:
                        for h in e.link_faces:
                            if h.index not in seen:
                                seen.add(h.index); stack.append(h)
                vs = {v for g in isl for v in g.verts}
                cs = np.array([v.co[:] for v in vs])
                if np.linalg.norm(cs.max(0) - cs.min(0)) < thr and not (level != "Shadow" and any(prot[v.index] >= 0.45 and v.index < len(prot) for v in vs)):
                    kill.extend(vs)
            if kill:
                dropped = len(kill)
                bmesh.ops.delete(bm, geom=list(set(kill)), context="VERTS")
        # 2) planar pass: seams on mask/colour/face borders so the dissolve never crosses them
        for e in bm.edges:
            e.seam = False
            if len(e.link_faces) == 2:
                a, b = e.verts
                if prot[a.index] >= 0.5 and prot[b.index] >= 0.5 and level != "Shadow":
                    e.seam = True
        bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(PLANAR_DEG[level]), use_dissolve_boundaries=False,
                                 verts=list(bm.verts), edges=list(bm.edges), delimit={"SEAM", "MATERIAL"})
        bmesh.ops.triangulate(bm, faces=list(bm.faces), quad_method="BEAUTY", ngon_method="BEAUTY")
        bm.to_mesh(me); bm.free()
        for a in ("custom_normal",):
            if a in me.attributes:
                me.attributes.remove(me.attributes[a])
    planar_tris = tri_count(me)
    # 3) collapse pass weighted by the decim group (weight 1 - protection; low weight = expensive collapse)
    vg = ob.vertex_groups.get("decim") or ob.vertex_groups.new(name="decim")
    co = arr(me.vertices, "co", len(me.vertices), 3)
    for i, v in enumerate(co):
        _, j, d = ref.kd.find(Vector(v))
        vg.add([i], float(1.0 - prot[j]), "REPLACE")
    work = me.copy()
    ratio = min(1.0, target / max(planar_tris, 1))
    tris = planar_tris
    for attempt in range(6):
        ob.data = work.copy()
        if ratio < 0.999:
            md = ob.modifiers.new("dec", "DECIMATE")
            md.decimate_type = "COLLAPSE"; md.ratio = ratio; md.use_collapse_triangulate = True
            md.vertex_group = "decim"; md.vertex_group_factor = VG_FACTOR; md.invert_vertex_group = invert
            dg = bpy.context.evaluated_depsgraph_get(); dg.update()
            new = bpy.data.meshes.new_from_object(ob.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
            ob.modifiers.remove(md)
            old = ob.data; ob.data = new; bpy.data.meshes.remove(old)
        tris = tri_count(ob.data)
        if tris <= tmax:
            break
        ratio *= (target / tris) * 0.98
    bpy.data.meshes.remove(work)
    if ob.data.name != name:
        bpy.data.meshes.remove(me) if me.users == 0 else None
        ob.data.name = name
    # survival of hard-protected Hero vertices (exact positions kept)
    kd = KDTree(len(ob.data.vertices))
    for i, v in enumerate(ob.data.vertices):
        kd.insert(v.co, i)
    kd.balance()
    hv = np.nonzero(hard)[0]
    surv = float(np.mean([kd.find(Vector(ref.V[i]))[2] < 1e-5 for i in hv])) if len(hv) else 1.0
    isl = island_assign(ref, ob, method)
    ISLANDS[ob.as_pointer()] = isl
    maxinf = resample(ref, ob, isl)
    am = ob.modifiers.new("Armature", "ARMATURE"); am.object = rig
    return ob, dict(planar_tris=planar_tris, tris=tris, ratio=round(ratio, 4), survival=round(surv, 4),
                    dropped_island_verts=dropped, max_influences=maxinf, invert=invert, method=method)


def mesh_stats(ob):
    me = ob.data
    nl = len(me.loops)
    LV = arr(me.loops, "vertex_index", nl, 1, np.int32)
    col, name = get_col(me, LV)
    area = arr(me.polygons, "area", len(me.polygons), 1)
    LS = arr(me.polygons, "loop_start", len(me.polygons), 1, np.int32)
    code = np.round(col[LS, 3] * 8).astype(np.int8) if col is not None else np.zeros(len(area), np.int8)
    inf = [sum(1 for g in v.groups if g.weight > 1e-4) for v in me.vertices]
    return area.astype(np.float32), code, max(inf) if inf else 0


def flat_material(name, colname, img, uvname):
    m = bpy.data.materials.new(name)
    try:
        m.use_nodes = True
    except Exception:
        pass
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    vc = nt.nodes.new("ShaderNodeVertexColor"); vc.layer_name = colname or ""
    em = nt.nodes.new("ShaderNodeEmission"); out = nt.nodes.new("ShaderNodeOutputMaterial")
    src = vc.outputs["Color"]
    if img is not None:
        tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = img; tex.extension = "CLIP"
        uvn = nt.nodes.new("ShaderNodeUVMap"); uvn.uv_map = uvname or ""
        nt.links.new(uvn.outputs["UV"], tex.inputs["Vector"])
        mix = nt.nodes.new("ShaderNodeMix"); mix.data_type = "RGBA"
        ia = [s for s in mix.inputs if s.name == "A" and s.type == "RGBA"][0]
        ib = [s for s in mix.inputs if s.name == "B" and s.type == "RGBA"][0]
        nt.links.new(tex.outputs["Alpha"], mix.inputs["Factor"])
        nt.links.new(vc.outputs["Color"], ia); nt.links.new(tex.outputs["Color"], ib)
        src = [s for s in mix.outputs if s.type == "RGBA"][0]
    nt.links.new(src, em.inputs["Color"]); em.inputs["Strength"].default_value = 1.0
    nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
    return m


def setup_render(scene):
    for eng in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT", "BLENDER_WORKBENCH"):
        try:
            scene.render.engine = eng
            break
        except Exception:
            continue
    try:
        scene.eevee.taa_render_samples = 16
    except Exception:
        pass
    scene.render.film_transparent = True
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.resolution_percentage = 100
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam); scene.camera = cam
    cam.data.sensor_fit = "VERTICAL"; cam.data.clip_start = 1.0; cam.data.clip_end = 200
    return cam, scene.render.engine


def place_camera(scene, cam, target, az, crop, res):
    """Gameplay camera (pitch 55, vFOV 35 at 1080 px, 23.4 u) cropped to `crop` screen pixels around the target."""
    fov = 2 * math.atan(math.tan(math.radians(17.5)) * crop / 1080.0)
    cam.data.lens_unit = "FOV"; cam.data.angle_y = fov
    p, a = math.radians(55), math.radians(az)
    d = Vector((math.cos(p) * math.sin(a), -math.cos(p) * math.cos(a), math.sin(p)))
    cam.location = target + d * 23.4
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    scene.render.resolution_x = scene.render.resolution_y = res


def find_action(acts, want):
    for a in acts:
        if a.name.split("|")[-1] == want:
            return a
    for a in acts:
        if want.lower() in a.name.lower():
            return a
    return None


KR = {}


def retarget(rig, krig):
    """Our pose = KayKit pose delta (posed x rest^-1, world space) applied to our rest, parents first."""
    for pb in rig.pose.bones:
        pb.matrix_basis.identity()
    bpy.context.view_layer.update()
    depth = {}
    for pb in rig.pose.bones:
        d, q = 0, pb.parent
        while q is not None:
            d += 1; q = q.parent
        depth.setdefault(d, []).append(pb)
    for d in sorted(depth):
        for pb in depth[d]:
            kb = krig.pose.bones.get(pb.name)
            if kb is None:
                continue
            kp = krig.matrix_world @ kb.matrix
            kr = krig.matrix_world @ kb.bone.matrix_local
            want = kp @ kr.inverted() @ (rig.matrix_world @ pb.bone.matrix_local)
            pb.matrix = rig.matrix_world.inverted() @ want
        bpy.context.view_layer.update()


def set_pose(rig, act, frac, scene):
    ad = rig.animation_data or rig.animation_data_create()
    ad.action = act
    try:
        if ad.action_slot is None and len(act.slots):
            ad.action_slot = act.slots[0]
    except Exception:
        pass
    f0, f1 = act.frame_range
    f = int(round(f0 + (f1 - f0) * frac))
    scene.frame_set(f)
    if rig is KR.get("krig"):
        retarget(KR["rig"], rig)
    return f


def fbx_stats(path, bake, anim):
    reset()
    objs, acts = import_fbx(path, bake, anim)
    out = dict(meshes={}, actions=sorted(a.name.split("|")[-1] for a in acts))
    for o in objs:
        if o.type == "ARMATURE":
            out["armature"] = o.name
            out["bones"] = len(o.data.bones)
            out["bone_heads"] = {b.name: [round(x, 5) for x in (o.matrix_world @ b.head_local)] for b in o.data.bones}
        elif o.type == "MESH":
            me = o.data
            nl = len(me.loops)
            LV = arr(me.loops, "vertex_index", nl, 1, np.int32)
            col, cname = get_col(me, LV)
            bb = np.array([o.matrix_world @ Vector(c) for c in o.bound_box])
            inf = [sum(1 for g in v.groups if g.weight > 1e-4) for v in me.vertices]
            out["meshes"][o.name] = dict(
                tris=tri_count(me), verts=len(me.vertices), colours=[(a.name, a.domain, a.data_type) for a in me.color_attributes],
                codes=sorted(set(np.round(col[:, 3] * 8).astype(int).tolist())) if col is not None else [],
                uvs=[l.name for l in me.uv_layers], materials=[s.material.name if s.material else None for s in o.material_slots],
                groups=len(o.vertex_groups), max_influences=max(inf) if inf else 0, parent=o.parent.name if o.parent else None,
                bbox_min=[round(float(x), 4) for x in bb.min(0)], bbox_max=[round(float(x), 4) for x in bb.max(0)])
    return out


def process(m):
    t0 = time.time()
    key, base = m["key"], m["base"]
    kind = m["kind"]
    budget = m.get("budget", FIGHTER)
    bake = kind == "fighter"
    src = os.path.join(SRC, m["fbx"])
    res = dict(key=key, source=m["fbx"], levels={})
    blend_pts = None
    if m.get("blend"):
        bpy.ops.wm.open_mainfile(filepath=os.path.join(SRC, m["blend"]), load_ui=False)
        blend_pts = []
        for o in bpy.data.objects:
            g = o.vertex_groups.get("outline_w") if o.type == "MESH" else None
            if g is None:
                continue
            for v in o.data.vertices:
                w = next((x.weight for x in v.groups if x.group == g.index), 1.0)
                if w < 0.5:
                    blend_pts.append(tuple(o.matrix_world @ v.co))
    res["source_stats"] = fbx_stats(src, bake, kind == "kaiju")
    reset()
    scene = bpy.context.scene
    scene.render.fps = 30
    objs, acts = import_fbx(src, bake, kind == "kaiju")
    rig = next(o for o in objs if o.type == "ARMATURE")
    meshes = [o for o in objs if o.type == "MESH"]
    rig.data.pose_position = "REST"
    for a in acts:
        a.use_fake_user = True
    if rig.animation_data:
        rig.animation_data.action = None
    # weapons parented to a bone (not skinned) get a full weight on that bone before the join
    for o in meshes:
        if o.parent_type == "BONE" and o.parent_bone:
            g = o.vertex_groups.get(o.parent_bone) or o.vertex_groups.new(name=o.parent_bone)
            g.add(list(range(len(o.data.vertices))), 1.0, "REPLACE")
            mw = o.matrix_world.copy(); o.parent = rig; o.parent_type = "OBJECT"; o.matrix_world = mw
            if not any(md.type == "ARMATURE" for md in o.modifiers):
                o.modifiers.new("Armature", "ARMATURE").object = rig
    body = max(meshes, key=lambda o: len(o.data.polygons))
    res["joined"] = [o.name for o in meshes]
    if len(meshes) > 1:
        for o in bpy.context.view_layer.objects:
            o.select_set(o in meshes)
        bpy.context.view_layer.objects.active = body
        with bpy.context.temp_override(active_object=body, object=body, selected_objects=meshes,
                                       selected_editable_objects=meshes):
            bpy.ops.object.join()
    hero = body
    hero.name = hero.data.name = base + "_Hero"
    ref = Ref(hero)
    face_px = None
    if m.get("face") and os.path.exists(os.path.join(SRC, m["face"])):
        face_img, face_px = load_px(os.path.join(SRC, m["face"]))
    else:
        face_img = None
    if blend_pts is not None:
        hw0 = np.array([hero.matrix_world @ Vector(v) for v in ref.V])
        best = (-1, None, None)
        for perm, sg in SIDE_FRAMES:
            hw = hw0[:, perm] * np.array(sg)
            kdw = KDTree(len(hw))
            for i, v in enumerate(hw):
                kdw.insert(Vector(v), i)
            kdw.balance()
            idx, hit = set(), 0
            for p_ in blend_pts:
                near = kdw.find_range(Vector(p_), 0.002)
                hit += bool(near)
                idx.update(i for (_, i, d) in near)
            if hit > best[0]:
                best = (hit, idx, (perm, sg))
        hit, idx, bframe = best
        res["blend_frame"] = str(bframe)
        m["_unlined"], m["_frame"] = idx, ((0, 2, 1), (1, 1, 1))
        res["sidecar_match"] = dict(blend_points=len(blend_pts), matched=hit, hero_vertices=len(idx))
    flags = protect_values(hero, ref, m, face_px)
    res["protect_counts"] = {k: int(v.sum()) for k, v in flags.items()}
    res["hero_tris"] = tri_count(hero.data)
    # LOD0 decides the protection sense of the decimate vertex group by measurement (survival of the hard set)
    lods = {}
    p0 = level_protect(flags, "LOD0")
    trial = {}
    for inv in (False, True):
        ob, st = make_lod(hero, ref, rig, p0, "LOD0", budget["LOD0"], base + "_LOD0", inv)
        trial[inv] = (ob, st)
    inv = max(trial, key=lambda k: trial[k][1]["survival"])
    for k, (ob, st) in trial.items():
        if k != inv:
            me = ob.data; bpy.data.objects.remove(ob); bpy.data.meshes.remove(me)
    lods["LOD0"] = trial[inv]
    lods["LOD0"][0].name = base + "_LOD0"
    res["vgroup_invert"] = inv
    res["survival_other_sense"] = trial[not inv][1]["survival"]
    scale = max(1.0, hero.dimensions.z / 2.0)
    extras = {}
    for lvl in ("LOD1", "Shadow"):
        for vi, meth in enumerate(METHOD[kind][lvl]):
            nm = base + "_" + lvl + ("" if vi == 0 else "_v" + meth)
            r_ = make_lod(hero, ref, rig, level_protect(flags, lvl), lvl, budget[lvl], nm, inv, meth, scale)
            if vi == 0:
                lods[lvl] = r_
            else:
                extras[lvl + "_" + meth] = r_
    levels = {"Hero": hero, **{k: v[0] for k, v in lods.items()}}
    for k, (ob, st) in lods.items():
        ob.name = ob.data.name = base + "_" + k
        res["levels"][k] = st
    for k, (ob, st) in extras.items():
        res["levels"][k] = st
    allobs = {**levels, **{k: v[0] for k, v in extras.items()}}
    res["levels"]["Hero"] = dict(tris=res["hero_tris"])
    # outline_w sidecar for the new mesh names (Farmer)
    if m.get("_frame"):
        perm, sg = m["_frame"]
        hero_unl = np.zeros(len(ref.V), bool); hero_unl[list(m["_unlined"])] = True
        entries = []
        for k in ("Hero", "LOD0", "LOD1"):
            ob = levels[k]
            co = arr(ob.data.vertices, "co", len(ob.data.vertices), 3)
            pts = []
            for v in co:
                _, j, d = ref.kd.find(Vector(v))
                if hero_unl[j] and d < (1e-4 if k == "Hero" else 0.003):
                    pts += [round(float(x), 5) for x in (v[list(perm)] * np.array(sg))]
            entries.append(dict(name=ob.name, vertices=len(co), unlined=pts))
        side_out = os.path.join(OUT, os.path.dirname(m["rel"]), base + "_OutlineW.json")
        os.makedirs(os.path.dirname(side_out), exist_ok=True)
        json.dump(dict(source="fighter_lods/build_lods.py from " + m["fbx"], group="outline_w", axes="unity(x,z,y)",
                       meshes=entries), open(side_out, "w"), separators=(",", ":"))
        res["sidecar_out"] = {e["name"]: len(e["unlined"]) // 3 for e in entries}
    # decal UV integrity: Hero face-region points vs the LOD's interpolated UV at the same surface spot
    if face_px is not None and ref.uvs:
        uvk = next(iter(ref.uvs))
        fv = np.nonzero(flags["face"])[0]
        tri_face = [t for t in range(len(ref.TV)) if flags["face"][ref.TV[t]].all()]
        rng = np.random.default_rng(7)
        for k in ("LOD0", "LOD1"):
            lr = Ref(levels[k])
            bad = tot = 0
            for t in tri_face[::max(1, len(tri_face) // 400)]:
                for _ in range(3):
                    w = rng.dirichlet((1, 1, 1))
                    p = Vector(w @ ref.V[ref.TV[t]])
                    uh = w @ ref.uvs[uvk][ref.TL[t]]
                    ti, wl, d = lr.bary(p)
                    ul = wl @ lr.uvs[uvk][lr.TL[ti]]
                    ah, al = decal_alpha(face_px, np.array([uh]))[0], decal_alpha(face_px, np.array([ul]))[0]
                    tot += 1
                    bad += int((ah > 0.5) != (al > 0.5))
            res["levels"][k]["decal_mismatch"] = round(bad / max(tot, 1), 4)
            res["levels"][k]["decal_samples"] = tot
    # export (rest pose, original settings)
    out = os.path.join(OUT, m["rel"])
    os.makedirs(os.path.dirname(out), exist_ok=True)
    for o in bpy.context.view_layer.objects:
        o.select_set(o == rig or o in levels.values())
    bpy.context.view_layer.objects.active = rig
    opts = FIGHTER_FBX if kind == "fighter" else KAIJU_FBX
    if kind == "kaiju":
        ad = rig.animation_data or rig.animation_data_create()
        ad.action = None
        for t in list(ad.nla_tracks):
            ad.nla_tracks.remove(t)
        for a in acts:
            clip = a.name.split("|")[-1]
            tr = ad.nla_tracks.new(); tr.name = clip
            s = tr.strips.new(clip, int(a.frame_range[0]), a); s.name = clip
            try:
                if s.action_slot is None and len(a.slots):
                    s.action_slot = a.slots[0]
            except Exception:
                pass
    r = bpy.ops.export_scene.fbx(filepath=out, use_selection=True, **opts)
    res["export"] = dict(path=m["rel"], result=list(r))
    if kind == "kaiju":
        for t in rig.animation_data.nla_tracks:
            t.mute = True
    # raw measurements for analyse_lods.py
    raw = {}
    for k, ob in allobs.items():
        area, code, mi = mesh_stats(ob)
        raw[k + "_area"], raw[k + "_code"] = area, code
        res["levels"][k]["max_influences_out"] = int(mi)
    rig.data.pose_position = "POSE"
    KR.clear(); KR["rig"] = rig
    if kind == "fighter":
        # Run 4: the FBX-imported KayKit_Clips actions did not play in Blender (Walk frames came out exactly at rest).
        # Pose from KayKit Knight.glb (the rig the fighters were built on, as render_warrior.py does) and retarget each
        # bone's world-space delta onto our rig (robust to bone-roll differences between the two imports).
        before_o, before_a = set(bpy.data.objects), set(bpy.data.actions)
        try:
            bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, "Knight.glb"), disable_bone_shape=True)
        except TypeError:
            bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, "Knight.glb"))
        kobjs = [o for o in bpy.data.objects if o not in before_o]
        kacts = [a for a in bpy.data.actions if a not in before_a]
        for a in kacts:
            a.use_fake_user = True
        krig = next(o for o in kobjs if o.type == "ARMATURE")
        for o in list(bpy.data.objects):
            if o not in allobs.values() and o not in (rig, krig):
                bpy.data.objects.remove(o)
        KR["krig"] = krig
        pose_rig = krig
        pool = kacts
        poses = [("Idle", 0.5), ("Walking_A", 0.25), ("Walking_A", 0.75), ("1H_Melee_Attack_Chop", 0.4),
                 ("1H_Melee_Attack_Chop", 0.6), ("1H_Melee_Attack_Chop", 0.8)]
    else:
        pose_rig = rig
        pool = acts
        atk = "Slam" if find_action(acts, "Slam") else "Swipe"
        poses = [("Idle", 0.5), ("Walk", 0.25), ("Walk", 0.75), (atk, 0.4), (atk, 0.6), (atk, 0.8)]
    res["poses"] = []
    E = {k: arr(ob.data.edges, "vertices", len(ob.data.edges), 2, np.int32) for k, ob in allobs.items()}
    he = arr(hero.data.edges, "vertices", len(hero.data.edges), 2, np.int32)
    lab = np.arange(len(ref.V))
    for _ in range(2000):
        mn = np.minimum(lab[he[:, 0]], lab[he[:, 1]])
        old = lab.copy()
        np.minimum.at(lab, he[:, 0], mn); np.minimum.at(lab, he[:, 1], mn)
        lab = lab[lab]
        if np.array_equal(lab, old):
            break
    MAP = {}
    for k, ob in allobs.items():
        co = arr(ob.data.vertices, "co", len(ob.data.vertices), 3)
        tri = np.zeros(len(co), np.int32); bw = np.zeros((len(co), 3), np.float32)
        il = ref.isl if k == "Hero" else ISLANDS.get(ob.as_pointer(), np.full(len(co), -1))
        for i, v in enumerate(co):
            tri[i], bw[i], _ = ref.bary(Vector(v), il[i])
        MAP[k] = (tri, bw)
        isl = lab[ref.TV[tri, 0]]
        raw[k + "_bridge"] = isl[E[k][:, 0]] != isl[E[k][:, 1]]
    res["deviation_cm"] = {}
    for clip, frac in poses:
        act = find_action(pool, clip)
        if act is None:
            res["poses"].append([clip, frac, "missing"])
            continue
        f = set_pose(pose_rig, act, frac, scene)
        dg = bpy.context.evaluated_depsgraph_get(); dg.update()
        ang = max(math.degrees((rig.matrix_world @ pb.matrix).to_quaternion().rotation_difference(
            (rig.matrix_world @ pb.bone.matrix_local).to_quaternion()).angle) for pb in rig.pose.bones)
        res["poses"].append([clip, frac, act.name, f, round(ang, 1)])
        evh = hero.evaluated_get(dg); emh = evh.to_mesh()
        PH = arr(emh.vertices, "co", len(emh.vertices), 3); evh.to_mesh_clear()
        tag = "%s_%d" % (clip, int(frac * 100))
        for k, ob in allobs.items():
            rest = arr(ob.data.vertices, "co", len(ob.data.vertices), 3)
            ev = ob.evaluated_get(dg); em = ev.to_mesh()
            posed = arr(em.vertices, "co", len(em.vertices), 3)
            ev.to_mesh_clear()
            tri, bw = MAP[k]
            M0 = (bw[:, :, None] * ref.V[ref.TV[tri]]).sum(1)
            M1 = (bw[:, :, None] * PH[ref.TV[tri]]).sum(1)
            e = E[k]
            raw["%s_l0_%s" % (k, tag)] = np.linalg.norm(rest[e[:, 0]] - rest[e[:, 1]], axis=1).astype(np.float32)
            raw["%s_l1_%s" % (k, tag)] = np.linalg.norm(posed[e[:, 0]] - posed[e[:, 1]], axis=1).astype(np.float32)
            raw["%s_r0_%s" % (k, tag)] = np.linalg.norm(M0[e[:, 0]] - M0[e[:, 1]], axis=1).astype(np.float32)
            raw["%s_r1_%s" % (k, tag)] = np.linalg.norm(M1[e[:, 0]] - M1[e[:, 1]], axis=1).astype(np.float32)
            dev = np.linalg.norm(posed - M1, axis=1) - np.linalg.norm(rest - M0, axis=1)
            res["deviation_cm"].setdefault(k, []).append(round(float(np.percentile(np.abs(dev), 99)) * 100, 2))
    np.savez_compressed(os.path.join(DATA, key + ".npz"), **raw)
    # renders: Idle pose, gameplay size and enlarged, front and back three-quarter
    act = find_action(pool, "Idle")
    if act:
        set_pose(pose_rig, act, 0.5, scene)
    res["rest_dims"] = [round(x, 3) for x in hero.dimensions]
    res["rig_matrix"] = [[round(x, 3) for x in r_] for r_ in rig.matrix_world]
    turn = bpy.data.objects.new("Turn", None); scene.collection.objects.link(turn)
    for o in [rig, pose_rig] + list(allobs.values()):
        if o.parent is None:
            mw = o.matrix_world.copy(); o.parent = turn; o.matrix_world = mw
    dg = bpy.context.evaluated_depsgraph_get(); dg.update()
    if "head" in rig.pose.bones and "hips" in rig.pose.bones:
        up = (rig.matrix_world @ rig.pose.bones["head"].head) - (rig.matrix_world @ rig.pose.bones["hips"].head)
    else:
        up = Vector((0, 0, 1))   # the Kaiju imports upright (its export does not bake the axis change)
    res["up_vector"] = [round(x, 3) for x in up.normalized()]
    if up.normalized().dot(Vector((0, 0, 1))) < 0.9:
        turn.rotation_mode = "QUATERNION"
        turn.rotation_quaternion = up.normalized().rotation_difference(Vector((0, 0, 1)))
    dg = bpy.context.evaluated_depsgraph_get(); dg.update()
    cam, eng = setup_render(scene)
    res["render_engine"] = eng
    uvname = next(iter(ref.uvs)) if ref.uvs else None
    m_plain = flat_material("R_plain", ref.colname, None, uvname)
    m_decal = flat_material("R_decal", ref.colname, face_img, uvname) if face_img else m_plain
    for k, ob in allobs.items():
        for i, s in enumerate(ob.material_slots):
            orig = s.material.name if s.material else ""
            use_decal = (m.get("face_slot") in orig) if m.get("face_slot") else True
            ob.material_slots[i].material = m_decal if use_decal else m_plain
    dg = bpy.context.evaluated_depsgraph_get(); dg.update()
    ev = hero.evaluated_get(dg); em = ev.to_mesh()
    pts = np.array([hero.matrix_world @ v.co for v in em.vertices]); ev.to_mesh_clear()
    target = Vector(((pts.min(0) + pts.max(0)) / 2).tolist())
    crop = m.get("crop", 200); big = m.get("big", 3)
    rdir = os.path.join(REN, key); os.makedirs(rdir, exist_ok=True)
    for k, ob in allobs.items():
        for o in allobs.values():
            o.hide_render = o is not ob
        for az, tag in ((45, "front"), (225, "back")):
            for size, res_px in (("game", crop), ("big", crop * big)):
                place_camera(scene, cam, target, az, crop, res_px)
                if k == "Hero" and size == "game" and "head" in rig.pose.bones:
                    from bpy_extras.object_utils import world_to_camera_view
                    dg = bpy.context.evaluated_depsgraph_get(); dg.update()
                    res.setdefault("screen_y", {})[tag] = {b: round(world_to_camera_view(scene, cam, rig.matrix_world @ rig.pose.bones[b].head).y, 3)
                                                           for b in ("head", "hips", "foot.l") if b in rig.pose.bones}
                scene.render.filepath = os.path.join(rdir, "%s_%s_%s.png" % (k, tag, size))
                bpy.ops.render.render(write_still=True)
    res["crop_px"] = crop; res["scale"] = scale
    res["seconds_build"] = round(time.time() - t0, 1)
    # round trip
    res["out_stats"] = fbx_stats(out, bake, kind == "kaiju")
    sb, ob_ = res["source_stats"].get("bone_heads", {}), res["out_stats"].get("bone_heads", {})
    res["bone_head_max_delta"] = max([float(np.max(np.abs(np.array(sb[b]) - np.array(ob_[b])))) for b in sb if b in ob_] or [-1])
    res["bones_missing"] = sorted(set(sb) - set(ob_))
    for st in (res["source_stats"], res["out_stats"]):
        st.pop("bone_heads", None)
    res["seconds"] = round(time.time() - t0, 1)
    return res


def main():
    summary = {}
    for m in MODELS:
        if ONLY and m["key"] not in ONLY:
            continue
        try:
            r = process(m)
            r["ok"] = True
        except Exception:
            r = dict(key=m["key"], ok=False, error=traceback.format_exc()[-2500:])
        json.dump(r, open(os.path.join(DATA, m["key"] + ".json"), "w"), indent=1, default=str)
        summary[m["key"]] = r.get("ok")
        log(m["key"], "OK" if r.get("ok") else "FAILED", r.get("seconds", ""), {k: v.get("tris") for k, v in r.get("levels", {}).items()})
        if not r.get("ok"):
            log(r["error"][-600:])
    log("SUMMARY", summary)


main()
