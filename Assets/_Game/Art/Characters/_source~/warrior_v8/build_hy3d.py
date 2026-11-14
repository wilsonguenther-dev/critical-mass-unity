"""Warrior from Tencent HY 3D Global meshes -> game-ready Hero + match-size (LOD0) on the KayKit 41-joint rig.

  ~/bin/cm-heavy blender perl -e 'alarm 1500; exec @ARGV' /Applications/Blender.app/Contents/MacOS/Blender -b \
      --factory-startup --python-exit-code 1 -P build_hy3d.py -- <raw_dir> <work_dir> <export_dir> <render_dir>

<raw_dir> holds body.glb, shield.glb, cleaver.glb (the service's textured output, any scale/orientation: glTF +Z front).
Rig, clips and the current in-game (v7) meshes come from look-dev A's warrior.blend (never written).
Steps: import + weld + drop specks -> fit to the v7 height, feet on z=0 -> decimate (Hero, LOD0) -> bake the generated
texture into per-face vertex colour 'Col' (alpha = team-mask code, palette.py) -> pose the rig into the mesh's A-pose,
transfer v7 weights by proximity in that pose, smooth, then un-skin the mesh back into the rig's T rest pose -> shield and
cleaver rigid on handslot.l / handslot.r, placed like v7's in the Idle pose -> FBX (look-dev A export settings) -> renders,
deformation numbers, FBX round trip. Output names: <BASE>_Hero, <BASE>_LOD0 (FighterLod naming), BASE = Warrior_HY.
"""
import json
import math
import os
import sys
import time

import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

T0 = time.time()
argv = sys.argv[sys.argv.index("--") + 1:]
RAW, WORK, EXP, REN = (os.path.abspath(os.path.expanduser(a)) for a in argv[:4])
for d in (WORK, EXP, REN):
    os.makedirs(d, exist_ok=True)
LDSRC = os.path.expanduser("~/critical-mass-unity-art-lookdev-a/Assets/_Game/Art/Characters/_source~")
sys.dont_write_bytecode = True
sys.path.insert(0, LDSRC)
from cm_art import export as cm_export  # noqa: E402

BASE = os.environ.get("HY_BASE", "Warrior_HY")
# Other fighters: HY_SRC_FBX=<the class's current FBX on the same KayKit rig> (weight source, "current" renders),
# HY_PROPS="<prop>:<slot>,..." (raw_dir/<prop>.glb per prop), HY_PROP_SCALE (long props, x the current prop's length).
SRC_FBX = os.environ.get("HY_SRC_FBX")
PROP_SLOTS = dict(kv.split(":") for kv in os.environ.get("HY_PROPS", "shield:handslot.l,cleaver:handslot.r").split(",") if kv)
PROP_SCALE = float(os.environ.get("HY_PROP_SCALE", "1.0"))
HERO = dict(body=int(os.environ.get("HY_HERO_BODY", 19000)), shield=1500, cleaver=1000)
LOD0 = dict(body=3700, shield=450, cleaver=300)
for _k in PROP_SLOTS:
    HERO.setdefault(_k, 1200)
    LOD0.setdefault(_k, 400)
LOD0["body"] = 4450 - sum(v for k, v in LOD0.items() if k != "body")
HERO["body"] = min(HERO["body"], 21500 - sum(v for k, v in HERO.items() if k != "body"))
SHIELD_THICK = 0.16          # shield depth / diameter (the single-view generation came back drum-thick, 0.55)
CLEAVER_THICK = 0.5          # keep half of the generated blade thickness
SHIELD_SCALE, SHIELD_TURN = 0.85, 35.0    # vs v7's shield: smaller, turned outward about the hand so the torso shows (bible)
CLEAVER_SCALE = 1.25         # vs v7's cleaver: the bible blade is longer
SMOOTH_IT = 80               # offline sweep on the dumped weights: k16/p2/80 halves the stretched edges vs k4/p4/16
CAM_PITCH, CAM_FOV, CAM_DIST, CAM_YAW = 55.0, 35.0, 23.4, 45.0
NAVY = (0x18 / 255, 0x23 / 255, 0x3B / 255)
REPORT = {}


def log(*a):
    print("HY", round(time.time() - T0, 1), *a, flush=True)


# --------------------------------------------------------------------------------------------- rig + v7
bpy.ops.wm.open_mainfile(filepath=os.path.join(LDSRC, "warrior.blend"))
scene = bpy.context.scene
arm = bpy.data.objects["Rig"]
assert arm.matrix_world == Matrix.Identity(4), "rig object must be at identity"
if not SRC_FBX:
    OLD = {k: bpy.data.objects[f"Warrior_{k.capitalize()}"] for k in ("body", "shield", "cleaver")}
    OLD_RENDER = list(OLD.values())
else:
    for o in list(bpy.data.objects):            # drop warrior.blend's meshes; keep the rig and its 76 clips
        if o.type == "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.abspath(os.path.expanduser(SRC_FBX)), use_anim=False, colors_type="SRGB",
                             ignore_leaf_bones=False, automatic_bone_orientation=False, primary_bone_axis="Y", secondary_bone_axis="X")
    new = [o for o in bpy.data.objects if o not in before]
    OLD_RENDER = [o for o in new if o.type == "MESH"]
    for o in OLD_RENDER:
        M = o.matrix_world.copy()
        o.parent = None
        o.data.transform(M)
        o.matrix_world = Matrix.Identity(4)
        for m in list(o.modifiers):
            o.modifiers.remove(m)
        o.parent = arm
        o.modifiers.new("Armature", "ARMATURE").object = arm
    for o in new:
        if o.type != "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
    OLD = None


def rest_pose():
    arm.location = (0, 0, 0)
    if arm.animation_data:
        arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.matrix_basis.identity()
    scene.frame_set(0)
    bpy.context.view_layer.update()


def set_clip(clip, frac):
    rest_pose()
    act = bpy.data.actions[clip]
    f0, f1 = act.frame_range
    f = int(round(f0 + (f1 - f0) * frac)) if frac < 1 else int(frac)
    arm.animation_data_create()
    arm.animation_data.action = act
    scene.frame_set(f)
    bpy.context.view_layer.update()
    return f


rest_pose()
BONES = [b.name for b in arm.data.bones]
REST = {b.name: b.matrix_local.copy() for b in arm.data.bones}


def skin_mats():
    """Armature-space skinning matrices of the current pose, in BONES order (rig is at identity)."""
    return np.array([np.array(arm.pose.bones[n].matrix @ REST[n].inverted()) for n in BONES])


def weights_of(ob):
    n = len(ob.data.vertices)
    W = np.zeros((n, len(BONES)))
    gi = {g.index: BONES.index(g.name) for g in ob.vertex_groups if g.name in BONES}
    for v in ob.data.vertices:
        for e in v.groups:
            if e.group in gi:
                W[v.index, gi[e.group]] += e.weight
    s = W.sum(1, keepdims=True)
    return W / np.maximum(s, 1e-9)


def verts_of(ob):
    co = np.zeros(len(ob.data.vertices) * 3)
    ob.data.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    M = np.array(ob.matrix_world)
    return co @ M[:3, :3].T + M[:3, 3]


def lbs(V, W, S, inverse=False):
    M = np.einsum("nb,bij->nij", W, S)
    Vh = np.concatenate([V, np.ones((len(V), 1))], 1)
    if inverse:
        M = np.linalg.inv(M)
    return np.einsum("nij,nj->ni", M, Vh)[:, :3]


if OLD:
    OLDV = {k: verts_of(o) for k, o in OLD.items()}
    OLDW = {k: weights_of(o) for k, o in OLD.items()}
else:                                            # split the source by dominant bone: hand-slot parts are the props
    _V = np.concatenate([verts_of(o) for o in OLD_RENDER])
    _W = np.concatenate([weights_of(o) for o in OLD_RENDER])
    _dom = np.array(BONES)[np.argmax(_W, 1)]
    OLDV, OLDW = {}, {}
    _props = np.isin(_dom, list(PROP_SLOTS.values()))
    OLDV["body"], OLDW["body"] = _V[~_props], _W[~_props]
    for _k, _slot in PROP_SLOTS.items():
        _m = _dom == _slot
        OLDV[_k], OLDW[_k] = _V[_m], _W[_m]
H_T = float(OLDV["body"][:, 2].max())
REPORT["v7"] = {k: {"verts": len(v), "dims": [round(float(x), 3) for x in v.max(0) - v.min(0)]} for k, v in OLDV.items()}
REPORT["target_height"] = round(H_T, 4)
log("v7 height", H_T, "bones", len(BONES))


# --------------------------------------------------------------------------------------------- import + clean
def import_glb(path, name):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == "MESH"]
    for o in meshes:
        M = o.matrix_world.copy()
        o.parent = None
        o.data.transform(M)
        o.matrix_world = Matrix.Identity(4)
    for o in new:
        if o.type != "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
    ob = meshes[0]
    if len(meshes) > 1:
        with bpy.context.temp_override(active_object=ob, object=ob, selected_editable_objects=meshes, selected_objects=meshes):
            bpy.ops.object.join()
    ob.name = ob.data.name = name
    if ob.data.uv_layers:
        ob.data.uv_layers[0].name = "UVMap"
    img = None
    for m in ob.data.materials:
        if m and m.use_nodes:
            for nd in m.node_tree.nodes:
                if nd.type == "TEX_IMAGE" and nd.image:
                    img = nd.image
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    dims = Vector([max(v.co[i] for v in bm.verts) - min(v.co[i] for v in bm.verts) for i in range(3)])
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=dims.length * 1e-6)
    # drop specks: islands whose bounding box is under 1.5 % of the object's size
    bm.verts.ensure_lookup_table()
    seen, dropped = set(), 0
    for v0 in bm.verts:
        if v0.index in seen:
            continue
        stack, isl = [v0], []
        seen.add(v0.index)
        while stack:
            v = stack.pop()
            isl.append(v)
            for e in v.link_edges:
                w = e.other_vert(v)
                if w.index not in seen:
                    seen.add(w.index)
                    stack.append(w)
        P = np.array([v.co for v in isl])
        if np.linalg.norm(P.max(0) - P.min(0)) < 0.015 * dims.length:
            bmesh.ops.delete(bm, geom=isl, context="VERTS")
            dropped += 1
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.update()
    log("imported", name, "verts", len(ob.data.vertices), "polys", len(ob.data.polygons), "specks dropped", dropped,
        "image", img.size[:] if img else None)
    return ob, img


def np_verts(ob):
    co = np.zeros(len(ob.data.vertices) * 3)
    ob.data.vertices.foreach_get("co", co)
    return co.reshape(-1, 3)


def set_verts(ob, V):
    ob.data.vertices.foreach_set("co", np.ascontiguousarray(V, np.float32).ravel())
    ob.data.update()


def tri_count(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def decimated_copy(ob, target, name):
    mod = ob.modifiers.new("dec", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = min(1.0, target / max(1, tri_count(ob)))
    mod.use_collapse_triangulate = True
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    ob.modifiers.remove(mod)
    me.name = name
    new = bpy.data.objects.new(name, me)
    scene.collection.objects.link(new)
    for p in me.polygons:
        p.use_smooth = True
    log("decimated", name, tri_count(new))
    return new


# --------------------------------------------------------------------------------------------- texture -> vertex colour
def tex_array(img):
    w, h = img.size
    px = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(px)
    return px.reshape(h, w, 4)


class Sampler:
    """Closest point on the high mesh -> UV -> texel (sRGB values as stored in the PNG)."""

    def __init__(self, high, img):
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
        self.tex = tex_array(img)

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


def mask_code(rgb):
    """Team-mask alpha from the sampled colour: teal cloth -> 1.0, bright cyan glow (gems, blade edge) -> 0.75."""
    import colorsys
    hh, ss, vv = colorsys.rgb_to_hsv(*np.clip(rgb, 0, 1))
    hue = hh * 360
    if 170 <= hue <= 205 and ss > 0.30 and vv > 0.80:
        return 0.75
    if 178 <= hue <= 205 and ss > 0.45 and 0.35 < vv <= 0.80:
        return 1.0
    return 0.0


def bake_vcol(ob, sampler):
    me = ob.data
    me.calc_loop_triangles()
    V = np_verts(ob)
    polys = list(me.polygons)
    P, owner = [], []
    for p in polys:
        vs = V[list(p.vertices)[:3]]
        for b in BARY:
            P.append(b @ vs)
            owner.append(p.index)
    C = sampler.colour(np.array(P)).reshape(len(polys), len(BARY), 3)
    C = np.median(C, 1)                         # median: a sample across a paint border does not muddy the face
    attr = me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    cols = np.zeros((len(me.loops), 4), np.float32)
    codes = {}
    for p in polys:
        a = mask_code(C[p.index])
        codes[a] = codes.get(a, 0) + 1
        for li in p.loop_indices:
            cols[li] = (*C[p.index], a)
    attr.data.foreach_set("color_srgb", cols.ravel())
    me.color_attributes.active_color_name = "Col"
    me.color_attributes.render_color_index = me.color_attributes.find("Col")
    return {str(k): v for k, v in sorted(codes.items())}


# --------------------------------------------------------------------------------------------- body: import, fit
body_hi, body_img = import_glb(os.path.join(RAW, "body.glb"), "HY_body_high")
V = np_verts(body_hi)
s = H_T / (V[:, 2].max() - V[:, 2].min())
V = (V - [0, 0, V[:, 2].min()]) * s
torso = V[(V[:, 2] > 0.5 * H_T) & (V[:, 2] < 0.6 * H_T)]
V -= [(V[:, 0].max() + V[:, 0].min()) / 2, float(np.median(torso[:, 1])), 0]
set_verts(body_hi, V)
REPORT["body_fit"] = {"scale": round(float(s), 4), "dims": [round(float(x), 3) for x in V.max(0) - V.min(0)],
                      "high_tris": tri_count(body_hi)}
log("body fitted", REPORT["body_fit"])

# A-pose of the generated mesh -> pose P of the rig (upper arms, upper legs rotated to the mesh's limbs)
J = {b.name: b.head_local.copy() for b in arm.data.bones}
xmax = V[:, 0].max()
POSE_P = {}
for side, sg in (("l", 1), ("r", -1)):
    hand = V[sg * V[:, 0] > 0.8 * xmax].mean(0)
    sh = J[f"upperarm.{side}"]
    d0 = (J[f"hand.{side}"] - sh).normalized()
    d1 = (Vector(hand) - sh).normalized()
    POSE_P[f"upperarm.{side}"] = d0.rotation_difference(d1)
    ank = V[(V[:, 2] > 0.10) & (V[:, 2] < 0.20) & (sg * V[:, 0] > 0.03)].mean(0)
    hp = J[f"upperleg.{side}"]
    e0 = (J[f"foot.{side}"] - hp).normalized()
    e1 = (Vector(ank) - hp).normalized()
    POSE_P[f"upperleg.{side}"] = e0.rotation_difference(e1)
REPORT["pose_fit_deg"] = {k: round(math.degrees(q.angle), 1) for k, q in POSE_P.items()}
log("pose fit (deg)", REPORT["pose_fit_deg"])


def apply_pose_p():
    rest_pose()
    for name, q in POSE_P.items():
        pb = arm.pose.bones[name]
        head = pb.matrix.translation.copy()
        pb.matrix = Matrix.Translation(head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-head) @ pb.matrix
        bpy.context.view_layer.update()


apply_pose_p()
S_P = skin_mats()
V7P = lbs(OLDV["body"], OLDW["body"], S_P)        # v7 body in the generated mesh's A-pose
KD7 = KDTree(len(V7P))
for i, p in enumerate(V7P):
    KD7.insert(Vector(p), i)
KD7.balance()
rest_pose()
HEAD_Z = J["head"].z

sampler = Sampler(body_hi, body_img)
LEVEL = {}
for tag, budget in (("Hero", HERO["body"]),):
    ob = decimated_copy(body_hi, budget, f"{BASE}_{tag}_Body")
    REPORT.setdefault("mask_faces", {})[ob.name] = bake_vcol(ob, sampler)
    Vb = np_verts(ob)
    # ---- weights: inverse-distance from the posed v7 body, then smoothed along this mesh's edges
    W = np.zeros((len(Vb), len(BONES)))
    for i, p in enumerate(Vb):
        for co, idx, dist in KD7.find_n(Vector(p), 16):
            W[i] += OLDW["body"][idx] / (dist ** 2 + 1e-10)
    W /= np.maximum(W.sum(1, keepdims=True), 1e-12)
    ed = np.zeros(len(ob.data.edges) * 2, np.int32)
    ob.data.edges.foreach_get("vertices", ed)
    ed = ed.reshape(-1, 2)
    for _ in range(SMOOTH_IT):
        acc = np.zeros_like(W)
        deg = np.zeros(len(Vb))
        np.add.at(acc, ed[:, 0], W[ed[:, 1]])
        np.add.at(acc, ed[:, 1], W[ed[:, 0]])
        np.add.at(deg, ed[:, 0], 1.0)
        np.add.at(deg, ed[:, 1], 1.0)
        W = 0.5 * W + 0.5 * acc / np.maximum(deg, 1.0)[:, None]
    # top 4, normalised
    idx = np.argsort(-W, 1)[:, 4:]
    np.put_along_axis(W, idx, 0.0, 1)
    W[W < 0.01] = 0.0
    W /= np.maximum(W.sum(1, keepdims=True), 1e-12)
    # ---- un-skin from the A-pose into the rig's rest pose
    Vr = lbs(Vb, W, S_P, inverse=True)
    set_verts(ob, Vr)
    LEVEL["Hero_A"] = Vb.copy()
    back = lbs(Vr, W, S_P)
    REPORT.setdefault("unskin_roundtrip_max_m", {})[tag] = float(np.abs(back - Vb).max())
    for bi in np.nonzero(W.sum(0) > 0)[0]:
        g = ob.vertex_groups.new(name=BONES[bi])
        nz = np.nonzero(W[:, bi])[0]
        for i in nz:
            g.add([int(i)], float(W[i, bi]), "REPLACE")
    LEVEL[tag] = ob
    LEVEL[tag + "_W"] = W
    fc = np.array([tuple(p.vertices) for p in ob.data.polygons], np.int32)
    DUMP = dict(VA=Vb, ed=ed, f=fc, W=W, V7P=V7P, W7=OLDW["body"], V7=OLDV["body"], S_P=S_P, bones=np.array(BONES),
                heads=np.array([list(J[b]) for b in BONES]))
    log(tag, "body", tri_count(ob), "groups", len(ob.vertex_groups))

# LOD0 body: decimated from the un-skinned Hero (rest pose); colour and weights are resampled from the Hero further down.
LEVEL["LOD0"] = decimated_copy(LEVEL["Hero"], LOD0["body"], f"{BASE}_LOD0_Body")
del sampler

# --------------------------------------------------------------------------------------------- props
set_clip("Idle", 1)
S_IDLE = skin_mats()
hsl, hsr = BONES.index("handslot.l"), BONES.index("handslot.r")
HS = {k: np.array(arm.pose.bones[sl].matrix.translation) for k, sl in PROP_SLOTS.items()}
OLDI = {k: lbs(OLDV[k], OLDW[k], S_IDLE) for k in PROP_SLOTS}
rest_pose()


def pca(P):
    c = P.mean(0)
    w, U = np.linalg.eigh(np.cov((P - c).T))
    return c, U[:, ::-1].T, w[::-1]            # axes largest -> smallest


def prop(key):
    hi, img = import_glb(os.path.join(RAW, f"{key}.glb"), f"HY_{key}_high")
    P = np_verts(hi)
    c, ax, _ = pca(P)
    P0 = P - c
    o = OLDI[key]
    oc, oax, _ = pca(o)
    if key == "shield":
        # generated: decorated face toward -Y (front camera), up +Z. Squash the depth to SHIELD_THICK x diameter.
        n_new, u_new = np.array([0, -1.0, 0]), np.array([0, 0, 1.0])
        diam = max(np.ptp(P0[:, 0]), np.ptp(P0[:, 2]))
        P0[:, 1] *= SHIELD_THICK * diam / max(np.ptp(P0[:, 1]), 1e-9)
        n_old = oax[2] * (1 if oax[2] @ (oc - HS[key]) > 0 else -1)
        up = np.array([0, 0, 1.0])
        u_old = up - n_old * (up @ n_old)
        u_old /= np.linalg.norm(u_old)
        A_new = np.stack([np.cross(u_new, n_new), u_new, n_new], 1)
        A_old = np.stack([np.cross(u_old, n_old), u_old, n_old], 1)
        R = A_old @ A_new.T
        od = (o - oc)
        k = max(np.ptp(od @ u_old), np.ptp(od @ np.cross(u_old, n_old))) / diam
        Q = (P0 * k) @ R.T
        back_old = (od @ n_old).min()
        back_new = (Q @ n_old).min()
        Q += oc + n_old * (back_old - back_new)
        Q = (Q - oc) * SHIELD_SCALE + oc
        hs = HS[key]
        ct, st = math.cos(math.radians(SHIELD_TURN)), math.sin(math.radians(SHIELD_TURN))
        Rz = np.array([[ct, -st, 0], [st, ct, 0], [0, 0, 1.0]])
        Q = (Q - hs) @ Rz.T + hs
        k *= SHIELD_SCALE
    else:
        a_new = ax[0]
        t = P0 @ a_new
        f_new = ax[2]
        w_new = np.cross(f_new, a_new)
        width = lambda sel: np.ptp(P0[sel] @ w_new) if sel.any() else 0.0
        lo, hi_ = t < np.percentile(t, 25), t > np.percentile(t, 75)
        if width(lo) > width(hi_):             # make +a run handle -> blade tip
            a_new, t = -a_new, -t
            w_new = np.cross(f_new, a_new)
        blade = t > np.percentile(t, 50)
        if (P0[blade] @ w_new).mean() < 0:     # blade bulk on +w
            f_new = -f_new
            w_new = np.cross(f_new, a_new)
        if key == "cleaver":
            P0 = P0 - np.outer(P0 @ f_new, f_new) * (1 - CLEAVER_THICK)
        od = o - oc
        a_old = oax[0] * (1 if oax[0] @ (oc - HS[key]) > 0 else -1)
        f_old = oax[2]
        w_old = np.cross(f_old, a_old)
        to = od @ a_old
        if ((od[to > np.median(to)]) @ w_old).mean() < 0:
            f_old = -f_old
            w_old = np.cross(f_old, a_old)
        k = np.ptp(to) / np.ptp(t)
        R = np.stack([a_old, w_old, f_old], 1) @ np.stack([a_new, w_new, f_new], 1).T
        Q = (P0 * k) @ R.T
        # handle end of the new blade sits where v7's handle end sits
        Q += oc + a_old * (to.min() - (Q @ a_old).min())
        grip = oc + a_old * to.min()
        sc = CLEAVER_SCALE if key == "cleaver" else PROP_SCALE
        Q = (Q - grip) * sc + grip
        k *= sc
    set_verts(hi, Q)                            # high mesh in the Idle-pose world, where the sampler reads it
    smp = Sampler(hi, img)
    out = {}
    for tag in ("Hero", "LOD0"):
        ob = decimated_copy(hi, (HERO if tag == "Hero" else LOD0)[key], f"{BASE}_{tag}_{key.capitalize()}")
        REPORT.setdefault("mask_faces", {})[ob.name] = bake_vcol(ob, smp)
        slot = BONES.index(PROP_SLOTS[key])
        Wp = np.zeros((len(ob.data.vertices), len(BONES)))
        Wp[:, slot] = 1.0
        set_verts(ob, lbs(np_verts(ob), Wp, S_IDLE, inverse=True))
        g = ob.vertex_groups.new(name=BONES[slot])
        g.add(list(range(len(ob.data.vertices))), 1.0, "REPLACE")
        out[tag] = ob
    REPORT.setdefault("props", {})[key] = {"scale_vs_generated": round(float(k), 4),
                                           "dims_idle": [round(float(x), 3) for x in np.ptp(Q, 0)]}
    return out, img


PROPS = {k: prop(k) for k in PROP_SLOTS}

# --------------------------------------------------------------------------------------------- LOD0 body fix-up
# LOD0 body lives in rest space like the Hero: colour (per-face vote) and weights come from the Hero surface.
lod = LEVEL["LOD0"]
hero = LEVEL["Hero"]
hero.data.calc_loop_triangles()
HV = np_verts(hero)
hbvh = BVHTree.FromPolygons([tuple(map(float, v)) for v in HV], [tuple(p.vertices) for p in hero.data.polygons])
hcol = np.zeros(len(hero.data.loops) * 4, np.float32)
hero.data.color_attributes["Col"].data.foreach_get("color_srgb", hcol)
hcol = hcol.reshape(-1, 4)
hface = np.array([hcol[p.loop_indices[0]] for p in hero.data.polygons])
LV = np_verts(lod)
lcol = np.zeros((len(lod.data.loops), 4), np.float32)
HWt = LEVEL["Hero_W"]
LW = np.zeros((len(LV), len(BONES)))
for p in lod.data.polygons:
    vs = LV[list(p.vertices)[:3]]
    votes = []
    for b in BARY:
        _, _, idx, _ = hbvh.find_nearest(Vector(b @ vs))
        if idx is not None:
            votes.append(hface[idx])
    c = np.median(np.array(votes), 0) if votes else np.array([0.5, 0.5, 0.5, 0.0])
    a = max(set(float(v[3]) for v in votes), key=[float(v[3]) for v in votes].count) if votes else 0.0
    for li in p.loop_indices:
        lcol[li] = (*c[:3], a)
for i, p in enumerate(LV):                       # weights from the Hero (same rest space)
    loc, _, idx, _ = hbvh.find_nearest(Vector(p))
    vs = list(hero.data.polygons[idx].vertices)
    d = np.linalg.norm(HV[vs] - np.array(loc), axis=1)
    wv = 1.0 / (d + 1e-6)
    LW[i] = (HWt[vs] * wv[:, None]).sum(0) / wv.sum()
led = np.zeros(len(lod.data.edges) * 2, np.int32)
lod.data.edges.foreach_get("vertices", led)
led = led.reshape(-1, 2)
for _ in range(10):                              # LOD0 edges are long: a few smoothing passes stop single-edge spikes
    acc = np.zeros_like(LW)
    deg = np.zeros(len(LV))
    np.add.at(acc, led[:, 0], LW[led[:, 1]])
    np.add.at(acc, led[:, 1], LW[led[:, 0]])
    np.add.at(deg, led[:, 0], 1.0)
    np.add.at(deg, led[:, 1], 1.0)
    LW = 0.5 * LW + 0.5 * acc / np.maximum(deg, 1.0)[:, None]
idx = np.argsort(-LW, 1)[:, 4:]
np.put_along_axis(LW, idx, 0.0, 1)
LW[LW < 0.01] = 0.0
LW /= np.maximum(LW.sum(1, keepdims=True), 1e-12)
if "Col" not in lod.data.color_attributes:
    lod.data.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
lod.data.color_attributes["Col"].data.foreach_set("color_srgb", lcol.ravel())
lod.data.color_attributes.active_color_name = "Col"
lod.vertex_groups.clear()
for bi in np.nonzero(LW.sum(0) > 0)[0]:
    g = lod.vertex_groups.new(name=BONES[bi])
    for i in np.nonzero(LW[:, bi])[0]:
        g.add([int(i)], float(LW[i, bi]), "REPLACE")
LEVEL["LOD0_W"] = LW
log("LOD0 body recoloured and reweighted from the Hero")

# --------------------------------------------------------------------------------------------- materials, textures
TEXDIR = EXP
tex_paths = {}
for key, img, res in [("Body", body_img, 2048)] + [(k.capitalize(), PROPS[k][1], 1024) for k in PROPS]:
    img.scale(res, res)
    px = np.empty(res * res * 4, np.float32)
    img.pixels.foreach_get(px)
    im = bpy.data.images.new(f"{BASE}_{key}_Albedo", res, res, alpha=True)
    im.pixels.foreach_set(px)
    pth = os.path.join(TEXDIR, f"{BASE}_{key}_Albedo.png")
    im.filepath_raw = pth
    im.file_format = "PNG"
    im.save()
    tex_paths[key] = pth
    mat = bpy.data.materials.new(f"{BASE}_{key}")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    tn = nt.nodes.new("ShaderNodeTexImage")
    tn.image = bpy.data.images.load(pth, check_existing=True)
    nt.links.new(tn.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.9
    nt.nodes.active = tn
    for tag in ("Hero", "LOD0"):
        ob = LEVEL[tag] if key == "Body" else PROPS[key.lower()][0][tag]
        ob.data.materials.clear()
        ob.data.materials.append(mat)
        ob.data.uv_layers[0].name = "UVMap"

PARTS = {tag: [LEVEL[tag]] + [PROPS[k][0][tag] for k in PROPS] for tag in ("Hero", "LOD0")}
for tag, obs in PARTS.items():
    for ob in obs:
        ob.parent = arm
        m = ob.modifiers.new("Armature", "ARMATURE")
        m.object = arm
REPORT["tris"] = {tag: {o.name: tri_count(o) for o in obs} | {"total": sum(tri_count(o) for o in obs)} for tag, obs in PARTS.items()}
log("tris", REPORT["tris"])

# hide the import leftovers from renders
for o in bpy.data.objects:
    if o.name.startswith("HY_"):
        o.hide_render = True
        o.hide_viewport = True


# --------------------------------------------------------------------------------------------- deformation numbers
def posed(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = ev.to_mesh()
    co = np.zeros(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    ev.to_mesh_clear()
    M = np.array(ob.matrix_world)
    return co.reshape(-1, 3) @ M[:3, :3].T + M[:3, 3]


CLIPS = (("Idle", 0.5), ("Running_A", 0.3), ("Running_A", 0.8), ("1H_Melee_Attack_Chop", 0.45), ("1H_Melee_Attack_Chop", 0.7))
rest_pose()
REST_P = {o.name: posed(o) for obs in PARTS.values() for o in obs}
APOSE = {PARTS["Hero"][0].name: LEVEL["Hero_A"], PARTS["LOD0"][0].name: lbs(REST_P[PARTS["LOD0"][0].name], LEVEL["LOD0_W"], S_P)}
deform = {}
for clip, fr in CLIPS:
    f = set_clip(clip, fr)
    row = {}
    for tag, obs in PARTS.items():
        o = obs[0]
        e = np.zeros(len(o.data.edges) * 2, np.int32)
        o.data.edges.foreach_get("vertices", e)
        e = e.reshape(-1, 2)
        p0, p1 = REST_P[o.name], posed(o)
        l0 = np.linalg.norm(p0[e[:, 0]] - p0[e[:, 1]], axis=1)
        l1 = np.linalg.norm(p1[e[:, 0]] - p1[e[:, 1]], axis=1)
        pa = APOSE[o.name]
        la = np.linalg.norm(pa[e[:, 0]] - pa[e[:, 1]], axis=1)
        oka = la > 1e-3
        ra = l1[oka] / la[oka]
        ra = np.maximum(ra, 1 / np.maximum(ra, 1e-6))          # stretch or squash, vs the generated shape
        dla = np.abs(l1[oka] - la[oka])
        ok = l0 > 1e-3
        r = l1[ok] / l0[ok]
        bad = e[ok][r > 1.5]
        zs = p0[bad[:, 0], 2] if len(bad) else np.zeros(0)
        row[tag] = {"edges": int(ok.sum()), "over_1.5": int((r > 1.5).sum()), "over_2": int((r > 2).sum()),
                    "max": round(float(r.max()), 2), "p99": round(float(np.percentile(r, 99)), 3),
                    "bad_z_hist": np.histogram(zs, bins=[0, 0.4, 0.8, 1.0, 1.2, 1.4, 2.1])[0].tolist(),
                    "vs_generated_over_1.5": int((ra > 1.5).sum()), "vs_generated_over_2": int((ra > 2).sum()),
                    "vs_generated_max": round(float(ra.max()), 2),
                    "visible_over_1.5_and_15mm": int(((ra > 1.5) & (dla > 0.015)).sum()),
                    "visible_over_1.5_and_30mm": int(((ra > 1.5) & (dla > 0.03)).sum())}
    deform[f"{clip}@{f}"] = row
REPORT["deform"] = deform
for clip, fr in CLIPS:
    f = set_clip(clip, fr)
    DUMP[f"S_{clip}@{f}"] = skin_mats()
np.savez_compressed(os.path.join(WORK, "hero_dump.npz"), **DUMP)
rest_pose()
log("deform", json.dumps(deform)[:600])

# --------------------------------------------------------------------------------------------- export
fbx_path = os.path.join(EXP, f"{BASE}.fbx")
exp_obs = []
for tag, obs in PARTS.items():
    cps = []
    for o in obs:
        c = o.copy()
        c.data = o.data.copy()
        c.modifiers.clear()
        scene.collection.objects.link(c)
        cps.append(c)
    with bpy.context.temp_override(active_object=cps[0], object=cps[0], selected_editable_objects=cps, selected_objects=cps):
        bpy.ops.object.join()
    j = cps[0]
    j.name = j.data.name = f"{BASE}_{tag}"
    j.parent = arm
    j.modifiers.new("Armature", "ARMATURE").object = arm
    j.data.color_attributes.active_color_name = "Col"
    j.hide_render = True
    exp_obs.append(j)
cm_export.export_fbx(fbx_path, arm, exp_obs)
REPORT["fbx"] = {"path": fbx_path, "bytes": os.path.getsize(fbx_path), "textures": tex_paths}
log("FBX", fbx_path, os.path.getsize(fbx_path))
for o in exp_obs:
    o.hide_viewport = True
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(WORK, f"{BASE}.blend"), compress=True)

# --------------------------------------------------------------------------------------------- renders (Workbench)
scene.render.engine = "BLENDER_WORKBENCH"
sh = scene.display.shading
sh.light = "STUDIO"
sh.show_object_outline = True
sh.object_outline_color = NAVY
sh.show_cavity = False
sh.show_shadows = False
scene.render.film_transparent = False
if scene.world is None:
    scene.world = bpy.data.worlds.new("W")
scene.world.color = (0.33, 0.33, 0.33)
scene.view_settings.view_transform = "Standard"
scene.render.image_settings.file_format = "PNG"

gm = bpy.data.meshes.new("Ground")
gm.from_pydata([(-6, -6, 0), (6, -6, 0), (6, 6, 0), (-6, 6, 0)], [], [(0, 1, 2, 3)])
ga = gm.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
for d in ga.data:
    d.color_srgb = (0.47, 0.49, 0.45, 0.0)
ground = bpy.data.objects.new("Ground", gm)
gmat = bpy.data.materials.new("GroundMat")
gmat.diffuse_color = (0.19, 0.21, 0.18, 1.0)
gmat.use_nodes = True
gmat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.19, 0.21, 0.18, 1.0)
gm.materials.append(gmat)
scene.collection.objects.link(ground)

cam_d = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_d)
scene.collection.objects.link(cam)
scene.camera = cam
SETS = {"v7": OLD_RENDER, "hero": PARTS["Hero"], "lod0": PARTS["LOD0"]}


def show(which):
    keep = set(SETS[which])
    for o in scene.objects:
        if o.type == "MESH" and o is not ground:
            o.hide_render = o not in keep


def view_dir(az, el):
    a, e = math.radians(az), math.radians(el)
    return Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))


def look(cam, target, d):
    cam.location = target + d
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()


def render(name, colour="VERTEX"):
    sh.color_type = colour
    scene.render.filepath = os.path.join(REN, name + ".png")
    bpy.ops.render.render(write_still=True)


def pts_of(which):
    return np.concatenate([posed(o) for o in SETS[which]])


def close_shot(which, name, az=35, el=12, colour="VERTEX", H=900, W=760):
    P = pts_of(which)
    d = view_dir(az, el)
    c = Vector(((P.max(0) + P.min(0)) / 2).tolist())
    cam_d.type = "ORTHO"
    right = Vector((0, 0, 1)).cross(d).normalized()
    up = d.cross(right).normalized()
    pr, pu = (P - np.array(c)) @ np.array(right), (P - np.array(c)) @ np.array(up)
    scene.render.resolution_x, scene.render.resolution_y = W, H
    cam_d.sensor_fit = "VERTICAL"
    cam_d.ortho_scale = float(max(np.ptp(pu), np.ptp(pr) * H / W) * 1.08)
    look(cam, c + right * float((pr.max() + pr.min()) / 2) + up * float((pu.max() + pu.min()) / 2), d * 12)
    scene.render.use_border = False
    show(which)
    ground.hide_render = True
    render(name, colour)


def game_shot(which, name, colour="VERTEX", crop=220):
    cam_d.type = "PERSP"
    cam_d.sensor_fit = "VERTICAL"
    cam_d.angle_y = math.radians(CAM_FOV)
    cam_d.clip_start, cam_d.clip_end = 1.0, 200.0
    tgt = Vector((0, 0, 1.0))
    look(cam, tgt, view_dir(CAM_YAW, CAM_PITCH) * CAM_DIST)   # front three-quarter from above, as a unit walking at the camera
    scene.render.resolution_x, scene.render.resolution_y = 1920, 1080
    from bpy_extras.object_utils import world_to_camera_view
    bpy.context.view_layer.update()
    P = pts_of(which)
    uv = np.array([world_to_camera_view(scene, cam, Vector(p))[:2] for p in P[:: max(1, len(P) // 2000)]])
    cx, cy = (uv.max(0) + uv.min(0)) / 2
    REPORT.setdefault("game_px", {})[name] = round(float(np.ptp(uv[:, 1]) * 1080), 1)
    scene.render.use_border = True
    scene.render.use_crop_to_border = True
    hw, hh = crop / 2 / 1920, crop / 2 / 1080
    scene.render.border_min_x, scene.render.border_max_x = cx - hw, cx + hw
    scene.render.border_min_y, scene.render.border_max_y = cy - hh, cy + hh
    show(which)
    ground.hide_render = False
    render(name, colour)
    scene.render.use_border = False


rest_pose()
set_clip("Idle", 0.5)
for which, colour, tag in (("v7", "VERTEX", "v7"), ("hero", "VERTEX", "hero_vcol"), ("hero", "TEXTURE", "hero_tex"), ("lod0", "VERTEX", "lod0_vcol")):
    close_shot(which, f"tq_{tag}", colour=colour)
    game_shot(which, f"game_{tag}", colour=colour)
close_shot("hero", "front_hero_vcol", az=0, el=5)
for clip, fr, nm in (("Idle", 0.5, "idle"), ("Running_A", 0.3, "run"), ("1H_Melee_Attack_Chop", 0.45, "attack")):
    set_clip(clip, fr)
    close_shot("hero", f"pose_{nm}_hero", colour="VERTEX", H=700, W=560)
    close_shot("lod0", f"pose_{nm}_lod0", colour="VERTEX", H=700, W=560)
rest_pose()
close_shot("hero", "rest_hero", az=0, el=5, H=700, W=900)
log("renders done")

# --------------------------------------------------------------------------------------------- FBX round trip
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx_path)
rt = {"objects": {}, "armature_bones": None}
for o in bpy.data.objects:
    if o.type == "MESH":
        rt["objects"][o.name] = {"tris": sum(len(p.vertices) - 2 for p in o.data.polygons), "groups": len(o.vertex_groups),
                                 "colors": [a.name for a in o.data.color_attributes], "uv": [u.name for u in o.data.uv_layers],
                                 "dims": [round(x, 3) for x in o.dimensions]}
    if o.type == "ARMATURE":
        rt["armature_bones"] = len(o.data.bones)
REPORT["fbx_round_trip"] = rt
json.dump(REPORT, open(os.path.join(REN, "hy3d_report.json"), "w"), indent=1)
log("round trip", json.dumps(rt))
log("DONE")
