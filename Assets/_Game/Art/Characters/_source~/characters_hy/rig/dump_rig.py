"""L1: dump every HY character's rig, meshes, weights and per-frame skinning matrices for offline weight work.

blender -b --factory-startup --python-exit-code 1 -P dump_rig.py -- <out_dir>
"""
import json
import math
import os
import sys
import time
import traceback

import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

T0 = time.time()
OUT = os.path.abspath(sys.argv[sys.argv.index("--") + 1])
os.makedirs(OUT, exist_ok=True)
H = os.path.expanduser("~")
REPO = H + "/critical-mass-unity-art-hy"
ART = REPO + "/Assets/_Game/Art"
LDSRC = H + "/critical-mass-unity-art-lookdev-a/Assets/_Game/Art/Characters/_source~"
KBLEND = H + "/critical-mass-unity-art-kaiju/Assets/_Game/Art/Kaiju/_source~/Kaiju.blend"
RAWD = ART + "/Characters/_source~/characters_hy/hy3d_raw"
CLIPS = [l.split("\t")[0] for l in open(REPO + "/tools/art/kaykit_clip_map.tsv") if l.strip() and not l.startswith("#")]
FIGHTERS = {
    "Warrior": dict(fbx=ART + "/Characters/Warrior.fbx", H_T=2.0008, scale=1.869,
                    raw=[H + "/Desktop/CM-3D-outputs/Warrior/body-1-pro31-mv.glb"]),
    "Farmer": dict(fbx=ART + "/Farmer/Farmer.fbx", H_T=1.975, scale=1.8657,
                   raw=[RAWD + "/Farmer/body-pro31-mv-f55a74d6-d26f-4e99-9561-264b5e4696a6_0.glb"]),
    "Miner": dict(fbx=ART + "/Miner/_hy~/Miner.fbx", H_T=2.0002, scale=1.8673,
                  raw=[RAWD + "/Miner/body-pro31-mv-86047937-ce04-4e39-9e59-b8856df24681_0.glb"]),
}
SUMMARY = {}


def log(*a):
    print("DUMP", round(time.time() - T0, 1), *a, flush=True)


def world_verts(ob):
    co = np.zeros(len(ob.data.vertices) * 3)
    ob.data.vertices.foreach_get("co", co)
    M = np.array(ob.matrix_world)
    return co.reshape(-1, 3) @ M[:3, :3].T + M[:3, 3]


def mesh_arrays(ob, bones):
    me = ob.data
    me.calc_loop_triangles()
    n = len(me.loop_triangles)
    tv = np.zeros(n * 3, np.int32)
    me.loop_triangles.foreach_get("vertices", tv)
    tp = np.zeros(n, np.int32)
    me.loop_triangles.foreach_get("polygon_index", tp)
    ed = np.zeros(len(me.edges) * 2, np.int32)
    me.edges.foreach_get("vertices", ed)
    W = np.zeros((len(me.vertices), len(bones)))
    gi = {g.index: bones.index(g.name) for g in ob.vertex_groups if g.name in bones}
    for v in me.vertices:
        for e in v.groups:
            if e.group in gi:
                W[v.index, gi[e.group]] += e.weight
    unweighted = int((W.sum(1) < 1e-6).sum())
    W = W / np.maximum(W.sum(1, keepdims=True), 1e-9)
    col = None
    if me.color_attributes:
        ca = me.color_attributes.get("Col") or me.color_attributes[0]
        c = np.zeros(len(ca.data) * 4, np.float32)
        ca.data.foreach_get("color_srgb", c)
        c = c.reshape(-1, 4)
        if ca.domain == "CORNER":
            pl = np.zeros(len(me.polygons), np.int32)
            me.polygons.foreach_get("loop_start", pl)
            col = c[pl]
        else:
            col = c
    return dict(V=world_verts(ob), T=tv.reshape(-1, 3), TP=tp, E=ed.reshape(-1, 2), W=W, COL=col,
                unweighted=np.array(unweighted))


def import_raw(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == "MESH"]
    Vs, Ts, off = [], [], 0
    for o in meshes:
        V = world_verts(o)
        me = o.data
        me.calc_loop_triangles()
        tv = np.zeros(len(me.loop_triangles) * 3, np.int32)
        me.loop_triangles.foreach_get("vertices", tv)
        Vs.append(V)
        Ts.append(tv.reshape(-1, 3) + off)
        off += len(V)
    for o in new:
        bpy.data.objects.remove(o, do_unlink=True)
    return np.concatenate(Vs), np.concatenate(Ts)


def set_action(arm, act):
    arm.animation_data_create()
    arm.animation_data.action = act
    try:
        if arm.animation_data.action_slot is None and len(act.slots):
            arm.animation_data.action_slot = act.slots[0]
    except Exception:
        pass


def rest_pose(arm, scene):
    if arm.animation_data:
        arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.matrix_basis.identity()
    scene.frame_set(0)
    bpy.context.view_layer.update()


def world_pose_mats(arm, bones):
    AM = arm.matrix_world
    return np.array([np.array(AM @ arm.pose.bones[n].matrix) for n in bones])


def sample_clips(arm, scene, bones, acts, rest_w):
    inv = np.linalg.inv(rest_w)
    out = {}
    for name, act in acts:
        rest_pose(arm, scene)
        set_action(arm, act)
        f0, f1 = (int(round(x)) for x in act.frame_range)
        S = []
        for f in range(f0, f1 + 1):
            scene.frame_set(f)
            P = world_pose_mats(arm, bones)
            S.append(np.einsum("bij,bjk->bik", P, inv))
        out[name] = np.array(S, np.float32)
    rest_pose(arm, scene)
    return out


def fit_heads(arm, heads_w):
    """Move the rig's bone heads to world positions (orientation, roll and length kept)."""
    AMi = arm.matrix_world.inverted()
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    moved = {}
    for e in arm.data.edit_bones:
        if e.name not in heads_w:
            continue
        h = AMi @ Vector(heads_w[e.name])
        if (h - e.head).length > 1e-4:
            d = e.tail - e.head
            roll = e.roll
            e.use_connect = False
            moved[e.name] = round((h - e.head).length, 4)
            e.head = h
            e.tail = h + d
            e.roll = roll
    bpy.ops.object.mode_set(mode="OBJECT")
    return moved


def fighter(cls, cfg):
    bpy.ops.wm.open_mainfile(filepath=os.path.join(LDSRC, "warrior.blend"))
    scene = bpy.context.scene
    arm = bpy.data.objects["Rig"]
    for o in list(bpy.data.objects):
        if o.type == "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
    rest_pose(arm, scene)
    bones = [b.name for b in arm.data.bones]
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=cfg["fbx"], use_anim=False, colors_type="SRGB", ignore_leaf_bones=False,
                             automatic_bone_orientation=False, primary_bone_axis="Y", secondary_bone_axis="X")
    new = [o for o in bpy.data.objects if o not in before]
    farm = next(o for o in new if o.type == "ARMATURE")
    meshes = {o.name.split(".")[0]: o for o in new if o.type == "MESH"}
    log(cls, "imported", sorted(meshes))
    FW = farm.matrix_world
    heads_w = {b.name: tuple(FW @ b.head_local) for b in farm.data.bones}
    rot_err = max(math.degrees((FW.to_3x3() @ farm.data.bones[n].matrix_local.to_3x3()).to_quaternion().rotation_difference(
        arm.data.bones[n].matrix_local.to_3x3().to_quaternion()).angle) for n in bones if n in farm.data.bones)
    data = {}
    for tag in ("Hero", "LOD0", "LOD1", "Shadow"):
        ob = meshes[f"{cls}_{tag}"]
        for k, v in mesh_arrays(ob, bones).items():
            if v is not None:
                data[f"{tag}_{k}"] = v
    for o in new:
        bpy.data.objects.remove(o, do_unlink=True)
    moved = fit_heads(arm, heads_w)
    rest_pose(arm, scene)
    rest_w = np.array([np.array(arm.matrix_world @ b.matrix_local) for b in arm.data.bones])
    J = {b.name: b.head_local.copy() for b in arm.data.bones}
    # ---- the generated mesh (raw) in game space: the build's fit, then its A-pose fit
    raw = None
    for p in cfg["raw"]:
        if os.path.exists(p):
            raw = p
            break
    RV, RT = import_raw(raw)
    s = cfg["H_T"] / (RV[:, 2].max() - RV[:, 2].min())
    RV = (RV - [0, 0, RV[:, 2].min()]) * s
    torso = RV[(RV[:, 2] > 0.5 * cfg["H_T"]) & (RV[:, 2] < 0.6 * cfg["H_T"])]
    RV -= [(RV[:, 0].max() + RV[:, 0].min()) / 2, float(np.median(torso[:, 1])), 0]
    P = RV
    pose = {}
    xmax = P[:, 0].max()
    for side, sg in (("l", 1), ("r", -1)):
        hand = P[sg * P[:, 0] > 0.8 * xmax].mean(0)
        sh = J[f"upperarm.{side}"]
        pose[f"upperarm.{side}"] = (J[f"hand.{side}"] - sh).normalized().rotation_difference((Vector(hand) - sh).normalized())
        ank = P[(P[:, 2] > 0.10) & (P[:, 2] < 0.20) & (sg * P[:, 0] > 0.03)].mean(0)
        hp = J[f"upperleg.{side}"]
        pose[f"upperleg.{side}"] = (J[f"foot.{side}"] - hp).normalized().rotation_difference((Vector(ank) - hp).normalized())
    rest_pose(arm, scene)
    for name, q in pose.items():
        pb = arm.pose.bones[name]
        head = pb.matrix.translation.copy()
        pb.matrix = Matrix.Translation(head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-head) @ pb.matrix
        bpy.context.view_layer.update()
    S_P = np.einsum("bij,bjk->bik", world_pose_mats(arm, bones), np.linalg.inv(rest_w))
    rest_pose(arm, scene)
    # validate: the Hero in the recovered A-pose against the raw surface
    W = data["Hero_W"]
    M = np.einsum("nb,bij->nij", W, S_P)
    Vh = np.concatenate([data["Hero_V"], np.ones((len(W), 1))], 1)
    A = np.einsum("nij,nj->ni", M, Vh)[:, :3]
    bvh = BVHTree.FromPolygons([tuple(map(float, v)) for v in RV], [tuple(map(int, t)) for t in RT])
    idx = np.linspace(0, len(A) - 1, 4000).astype(int)
    d = np.array([(bvh.find_nearest(Vector(A[i]))[3] or 0.0) for i in idx])
    Mr = np.einsum("nb,bij->nij", W, np.repeat(np.eye(4)[None], len(bones), 0))
    d0 = np.array([(bvh.find_nearest(Vector(data["Hero_V"][i]))[3] or 0.0) for i in idx])
    val = {"raw": os.path.basename(raw), "scale": round(float(s), 4), "scale_report": cfg["scale"],
           "apose_to_raw_mm": [round(float(np.median(d)) * 1000, 2), round(float(np.percentile(d, 95)) * 1000, 2), round(float(d.max()) * 1000, 2)],
           "rest_to_raw_mm": [round(float(np.median(d0)) * 1000, 2), round(float(np.percentile(d0, 95)) * 1000, 2)],
           "pose_deg": {k: round(math.degrees(q.angle), 2) for k, q in pose.items()}, "bones_moved": moved,
           "fbx_bone_rot_err_deg": round(rot_err, 3)}
    log(cls, "validation", json.dumps(val))
    acts = [(c, bpy.data.actions[c]) for c in CLIPS if c in bpy.data.actions]
    S = sample_clips(arm, scene, bones, acts, rest_w)
    np.savez_compressed(os.path.join(OUT, f"{cls}.npz"), bones=np.array(bones), rest_w=rest_w, S_P=S_P,
                        parents=np.array([bones.index(b.parent.name) if b.parent else -1 for b in arm.data.bones]),
                        tails=np.array([np.array(arm.matrix_world @ b.tail_local) for b in arm.data.bones]),
                        clip_names=np.array([c for c, _ in acts]), raw_V=RV.astype(np.float32), raw_T=RT,
                        **{f"S_{c}": v for c, v in S.items()}, **data)
    SUMMARY[cls] = dict(val, clips={c: int(v.shape[0]) for c, v in S.items()},
                        verts={t: int(len(data[f"{t}_V"])) for t in ("Hero", "LOD0", "LOD1", "Shadow")},
                        unweighted={t: int(data[f"{t}_unweighted"]) for t in ("Hero", "LOD0", "LOD1", "Shadow")},
                        weighted_bones=[bones[i] for i in np.nonzero(data["Hero_W"].sum(0) > 0)[0]])


def kaiju():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.fps = 30
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=ART + "/Kaiju/_hy~/Kaiju_Adult.fbx", use_anim=False, colors_type="SRGB",
                             ignore_leaf_bones=False, automatic_bone_orientation=False, primary_bone_axis="Y", secondary_bone_axis="X")
    new = [o for o in bpy.data.objects if o not in before]
    farm = next(o for o in new if o.type == "ARMATURE")
    meshes = {o.name.split(".")[0]: o for o in new if o.type == "MESH"}
    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)
    with bpy.data.libraries.load(KBLEND, link=False) as (_src, _dst):
        _dst.objects = ["Adult_Rig"]
    arm = _dst.objects[0]
    scene.collection.objects.link(arm)
    strips = [s for t in (arm.animation_data.nla_tracks if arm.animation_data else []) for s in t.strips]
    acts = [(s.name.split("|")[-1], s.action) for s in strips]
    for t in list(arm.animation_data.nla_tracks):
        arm.animation_data.nla_tracks.remove(t)
    arm.animation_data.action = None
    bones = [b.name for b in arm.data.bones]
    FW = farm.matrix_world
    heads_w = {b.name: tuple(FW @ b.head_local) for b in farm.data.bones}
    old_heads = {b.name: tuple(arm.matrix_world @ b.head_local) for b in arm.data.bones}
    data = {}
    for tag in ("Hero", "LOD0", "LOD1", "Shadow"):
        for k, v in mesh_arrays(meshes[f"Kaiju_{tag}"], bones).items():
            if v is not None:
                data[f"{tag}_{k}"] = v
    # head close-ups of the HY Hero (vertex colour) before deleting it
    hero = meshes["Kaiju_Hero"]
    hero.modifiers.clear()
    hero.parent = None
    hero.matrix_world = Matrix.Identity(4)
    hero.data.transform(Matrix.Identity(4))
    set_v = data["Hero_V"]
    hero.data.vertices.foreach_set("co", set_v.astype(np.float32).ravel())
    hero.data.update()
    for o in new:
        if o is not hero:
            bpy.data.objects.remove(o, do_unlink=True)
    moved = fit_heads(arm, heads_w)
    rest_pose(arm, scene)
    rest_w = np.array([np.array(arm.matrix_world @ b.matrix_local) for b in arm.data.bones])
    hb = bones.index("Head") if "Head" in bones else 0
    render_head(scene, hero, rest_w[hb][:3, 3], data["Hero_V"])
    bpy.data.objects.remove(hero, do_unlink=True)
    S = sample_clips(arm, scene, bones, acts, rest_w)
    np.savez_compressed(os.path.join(OUT, "Kaiju.npz"), bones=np.array(bones), rest_w=rest_w,
                        parents=np.array([bones.index(b.parent.name) if b.parent else -1 for b in arm.data.bones]),
                        tails=np.array([np.array(arm.matrix_world @ b.tail_local) for b in arm.data.bones]),
                        clip_names=np.array([c for c, _ in acts]), **{f"S_{c}": v for c, v in S.items()}, **data)
    SUMMARY["Kaiju"] = dict(bones=bones, parents={b.name: (b.parent.name if b.parent else None) for b in arm.data.bones},
                            heads_moved_vs_blend={k: round(float(np.linalg.norm(np.subtract(heads_w[k], old_heads[k]))), 3) for k in heads_w if k in old_heads},
                            fit_moved=moved, arm_matrix=[list(r) for r in arm.matrix_world],
                            clips={c: int(v.shape[0]) for c, v in S.items()},
                            verts={t: int(len(data[f"{t}_V"])) for t in ("Hero", "LOD0", "LOD1", "Shadow")},
                            unweighted={t: int(data[f"{t}_unweighted"]) for t in ("Hero", "LOD0", "LOD1", "Shadow")},
                            weighted_bones=[bones[i] for i in np.nonzero(data["Hero_W"].sum(0) > 0)[0]],
                            heads_w={k: [round(x, 3) for x in v] for k, v in heads_w.items()})


def render_head(scene, ob, head, V):
    scene.render.engine = "BLENDER_WORKBENCH"
    sh = scene.display.shading
    sh.light = "STUDIO"
    sh.color_type = "VERTEX"
    sh.show_object_outline = False
    scene.view_settings.view_transform = "Standard"
    if scene.world is None:
        scene.world = bpy.data.worlds.new("W")
    scene.world.color = (0.33, 0.33, 0.33)
    ob.data.color_attributes.active_color_name = ob.data.color_attributes[0].name
    cam_d = bpy.data.cameras.new("Cam")
    cam = bpy.data.objects.new("Cam", cam_d)
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam_d.type = "ORTHO"
    cam_d.clip_end = 200
    scene.render.resolution_x = scene.render.resolution_y = 700
    tgt = Vector(head)
    for name, az, el, sc in (("kaiju_head_front", 0, 5, 2.6), ("kaiju_head_tq", 35, 20, 2.6), ("kaiju_side", 90, 5, 7.5),
                             ("kaiju_front", 0, 5, 7.5)):
        a, e = math.radians(az), math.radians(el)
        d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
        cam_d.ortho_scale = sc
        t = tgt if sc < 5 else Vector((0, float(np.mean(V[:, 1])), 2.6))
        cam.location = t + d * 30
        cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(OUT, name + ".png")
        bpy.ops.render.render(write_still=True)


for cls, cfg in FIGHTERS.items():
    try:
        fighter(cls, cfg)
        log(cls, "done")
    except Exception:
        traceback.print_exc()
        SUMMARY[cls] = {"error": traceback.format_exc()[-1500:]}
try:
    kaiju()
    log("Kaiju done")
except Exception:
    traceback.print_exc()
    SUMMARY["Kaiju"] = {"error": traceback.format_exc()[-1500:]}
json.dump(SUMMARY, open(os.path.join(OUT, "summary.json"), "w"), indent=1, default=str)
log("ALL_DONE")
