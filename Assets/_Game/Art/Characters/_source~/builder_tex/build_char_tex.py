"""Textured, game-ready character from a generated high-detail mesh (batch 12, Builder pilot; reusable per class).

  cm-heavy blender blender -b --factory-startup -P build_char_tex.py -- <config.json> <repo>
  (BT_FROM_BLEND=<out>/stage_skinned.blend skips to the clip proof, renders and export.)

What it does, every step measured into <out>/<key>_report.json (config: builder_tex.json):
  1. Rig: the class's 41-bone rig (config rig_fbx) in its rest pose; the class clips (clips_fbx) on a second armature.
     Clips are applied as ABSOLUTE armature-space bone matrices (what Unity does with a Generic clip: each bone's local
     TRS comes from the clip file), never by copying matrix_basis: the clip file's rest differs from the model rig's.
  2. Raw: the generated GLB (one UV map, 2048 base colour), merged by exact position only (never by distance), aligned
     to the SKELETON: soles on z = 0, crown height = the rig's own body (old Hero cap crown), feet centred in x, shins
     centred on the shin bones in depth.
  3. A-pose: the rig is posed into the raw's own pose (upper arms toward the gloves, upper legs toward the boot shafts),
     as the Warrior pilot (fix/b12-ab run 5) proved: cut, bake and weight in the mesh's pose, unskin to the rest last.
  4. Parts: specks (< speck_diag_m) dropped; detached items rigid on one bone; the back pack (chair) found by depth
     behind the spine and colour (not shirt navy), grown from far-behind seeds, rigid on the chest.
  5. LODs: collapse decimation only (never merge-by-distance, never remesh / shrinkwrap): LOD0, LOD1, Shadow. Head
     vertices protected so the face keeps triangles. Joint-cut connectivity check after every step: with spheres cut
     out at the shoulder and hip joints, the forearm/glove and shin/boot shells must share no component with the torso.
  6. UVs: Smart UV Project trials, islands averaged, head islands scaled by head_uv_scale, packed concave with rotation.
     Base colour baked raw -> LOD0 at 2x (2048) then box-filtered to 1024: per texel a short outside-in ray along the
     LOD0 normal (front-facing hits only), nearest surface as fallback; bilinear on the raw 2048 texture; dilated.
  7. Team mask in alpha (CMToonBaseMapColour contract: 0 fixed, 0.5 team cloth, 1 glow): teal by HSV, opened, closed,
     specks under 20 px removed. Azure and Crimson previews computed with CMToonSwap.
  8. Weights per region (A-pose): arm region (after the shoulder cut) only its own chain, shoulder zone chest + upper
     arm, legs likewise with the hips, torso hips/spine/chest/head (+ upper legs damped below the hips: the apron),
     pack and items rigid. Inverse-distance^power, 3 smoothing passes inside the allowed sets, max 4, normalised.
  9. Unskin to the rest (linear blend inverse). Rest-pose joint-cut check (the numbers the Unity test asserts).
 10. Proof: every clip, every frame, edge stretch (posed / rest length, edges > 2 mm) on LOD0, LOD1 and the current
     game LOD0 as control; toon renders (flat bands + ink hull) of idle, run mid-stride, attack raise, attack contact,
     hit, from the gameplay pitch (55 deg) and front / side at 10 deg, both teams, new and current side by side; face.
 11. Export FBX (the class recipe: -Z forward, Y up, FBX_SCALE_ALL, bake_space_transform, no leaf bones, all 41
     bones), round trip, copy FBX + texture into the repo.
Repeat for another class: copy builder_tex.json, change key/raw_glb/rig_fbx/rig_prefix/clips_fbx/repo paths/team colours
(and clips names), run, read the report and the renders.
"""
import bpy, bmesh, sys, os, json, math, time, struct, zlib
import numpy as np
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

argv = sys.argv[sys.argv.index("--") + 1:]
CFG_PATH, REPO = argv[0], os.path.expanduser(argv[1])
C = json.load(open(CFG_PATH))
X = lambda p: os.path.expanduser(p) if p.startswith("~") else os.path.join(REPO, p)
KEY = C["key"]
OUT = X(C["out_dir"]); os.makedirs(OUT, exist_ok=True)
REP = {"config": C}
T0 = time.time()
DEFORM = ["hips", "spine", "chest", "head"] + [b + s for s in (".l", ".r") for b in ("upperarm", "lowerarm", "wrist", "hand")] + \
         [b + s for s in (".l", ".r") for b in ("upperleg", "lowerleg", "foot", "toes")]
BI = {b: i for i, b in enumerate(DEFORM)}
TORSO = ["hips", "spine", "chest"]


def log(*a):
    print("[%s %6.1fs]" % (KEY, time.time() - T0), *a, flush=True)


def save():
    json.dump(REP, open(os.path.join(OUT, KEY + "_report.json"), "w"), indent=1, default=lambda o: o.tolist() if hasattr(o, "tolist") else str(o))


def hexrgb(h):
    h = h.lstrip("#"); return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)])


def write_png(path, arr):
    a = (np.clip(arr, 0, 1) * 255 + 0.5).astype(np.uint8)[::-1]
    h, w, c = a.shape
    raw = b"".join(b"\x00" + a[y].tobytes() for y in range(h))
    ch = lambda t, d: struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    open(path, "wb").write(b"\x89PNG\r\n\x1a\n" + ch(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6 if c == 4 else 2, 0, 0, 0))
                           + ch(b"IDAT", zlib.compress(raw, 9)) + ch(b"IEND", b""))


def load_px(path):
    im = bpy.data.images.load(path); w, h = im.size
    a = np.array(im.pixels[:], np.float32).reshape(h, w, 4); bpy.data.images.remove(im); return a


def ntri(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def override(o):
    return dict(object=o, active_object=o, selected_objects=[o], selected_editable_objects=[o])


def apply_mod(o, name):
    with bpy.context.temp_override(**override(o)):
        bpy.ops.object.modifier_move_to_index(modifier=name, index=0)
        bpy.ops.object.modifier_apply(modifier=name)


def imp_fbx(p, anim):
    b = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=p, use_anim=anim, use_custom_normals=False, ignore_leaf_bones=False,
                             automatic_bone_orientation=False, primary_bone_axis="Y", secondary_bone_axis="X")
    return [o for o in bpy.data.objects if o not in b]


def arrays(o):
    me = o.data
    V = np.zeros(len(me.vertices) * 3); me.vertices.foreach_get("co", V); V = V.reshape(-1, 3)
    M = np.array(o.matrix_world); V = V @ M[:3, :3].T + M[:3, 3]
    E = np.zeros(len(me.edges) * 2, int); me.edges.foreach_get("vertices", E)
    return V, E.reshape(-1, 2)


def components(n, E, keep=None):
    if keep is not None:
        E = E[keep[E[:, 0]] & keep[E[:, 1]]]
    lab = np.arange(n)
    while True:
        a, b = lab[E[:, 0]], lab[E[:, 1]]
        m = np.minimum(a, b)
        new = lab.copy()
        np.minimum.at(new, a, m); np.minimum.at(new, b, m)
        while True:
            n2 = new[new]
            if (n2 == new).all(): break
            new = n2
        if (new == lab).all(): return lab
        lab = new


def joints(rig):
    A = rig.matrix_world
    return {pb.name: (np.array(A @ pb.head), np.array(A @ pb.tail)) for pb in rig.pose.bones}


def segdist(X_, a, b):
    ab = b - a; t = np.clip(((X_ - a) @ ab) / max(1e-9, ab @ ab), 0, 1)
    return np.linalg.norm(X_ - (a + t[:, None] * ab), axis=1)


def bone_dists(V, J):
    return np.stack([segdist(V, *J[b]) for b in DEFORM], 1)


JC_SCALE = [1.0]


def joint_cut(V, E, J, item=None, tag=""):
    """Shells welded across limbs? Cut spheres at the shoulder and hip joints; the forearm/glove and shin/boot probes
    must share no component with the torso probe (hips/spine/chest-nearest, above the hips or in front: belly, apron,
    back, pack). Also arm vs leg. Returns the report and the cut labels (used for the weight regions)."""
    n = len(V)
    jc = C["joint_cut"]
    rem = {}
    for s in ("l", "r"):
        rem["sh" + s] = np.linalg.norm(V - J["upperarm." + s][0], axis=1) < jc["shoulder_r"] * JC_SCALE[0]
        rem["hip" + s] = np.linalg.norm(V - J["upperleg." + s][0], axis=1) < jc["hip_r"] * JC_SCALE[0]
    cut = rem["shl"] | rem["shr"] | rem["hipl"] | rem["hipr"]
    keep = ~cut if item is None else (~cut & ~item)
    lab = components(n, E, keep)
    D = bone_dists(V, J)
    near = D.argmin(1)
    hz, hy = J["hips"][0][2], J["hips"][0][1]
    torso = keep & np.isin(near, [BI[b] for b in TORSO]) & ((V[:, 2] > hz + 0.05) | (V[:, 1] < hy - 0.12))
    probes = {}
    for s in ("l", "r"):
        ch = [BI[b + "." + s] for b in ("lowerarm", "wrist", "hand")]
        probes["arm." + s] = keep & np.isin(near, ch) & (D[:, ch].min(1) < 0.16)
        lg = [BI[b + "." + s] for b in ("lowerleg", "foot", "toes")]
        probes["leg." + s] = keep & np.isin(near, lg)
    rep = {"tag": tag, "radii_m": dict(shoulder=round(jc["shoulder_r"] * JC_SCALE[0], 4), hip=round(jc["hip_r"] * JC_SCALE[0], 4)), "verts": n, "cut_verts": int(cut.sum()), "components_after_cut": int(len(np.unique(lab[keep]))) if keep.any() else 0}
    kd = KDTree(int(torso.sum()))
    for i, p in enumerate(V[torso]): kd.insert(p, i)
    kd.balance()
    for k, pm in probes.items():
        comps = np.unique(lab[pm])
        shared = int((torso & np.isin(lab, comps)).sum())
        others = {k2: int((p2 & np.isin(lab, comps)).sum()) for k2, p2 in probes.items() if k2 != k and k2.split(".")[0] != k.split(".")[0]}
        dmin = min((kd.find(p)[2] for p in V[pm][:: max(1, int(pm.sum()) // 400)]), default=None)
        rep[k] = dict(probe_verts=int(pm.sum()), components=len(comps), torso_verts_in_its_component=shared,
                      other_limb_verts_in_its_component=others, welded=bool(shared > 0 or any(others.values())),
                      nearest_torso_m=None if dmin is None else round(float(dmin), 4))
    for k, pm in probes.items():
        if not rep[k]["welded"] or len(V) > 60000: continue
        adj = [[] for _ in range(n)]
        for a_, b_ in E[keep[E[:, 0]] & keep[E[:, 1]]]: adj[a_].append(b_); adj[b_].append(a_)
        prev = -np.ones(n, int); seen_ = pm.copy(); fr = list(np.nonzero(pm)[0]); hitv = -1
        while fr and hitv < 0:
            nf = []
            for u in fr:
                for w in adj[u]:
                    if not seen_[w]:
                        seen_[w] = True; prev[w] = u; nf.append(w)
                        if torso[w]: hitv = w; break
                if hitv >= 0: break
            fr = nf
        path = []
        while hitv >= 0: path.append(hitv); hitv = prev[hitv]
        rep[k]["bridge_path_m"] = [np.round(V[i], 3).tolist() for i in path[:: max(1, len(path) // 8)]]
    rep["any_welded"] = any(v["welded"] for k, v in rep.items() if isinstance(v, dict) and "welded" in v)
    log("joint-cut", tag, json.dumps(rep))
    return rep, dict(lab=lab, rem=rem, probes=probes, keep=keep)


# ================================================================================================ scene
FROM_BLEND = os.environ.get("BT_FROM_BLEND")
if not FROM_BLEND:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    ref = imp_fbx(X(C["rig_fbx"]), False)
    rig = next(o for o in ref if o.type == "ARMATURE")
    rig.name = "Rig"
    rig.data.pose_position = "POSE"
    pre = C["rig_prefix"]
    hero = next(o for o in ref if o.name.startswith(pre + "Hero"))
    old0 = next(o for o in ref if o.name.startswith(pre + "LOD0"))
    REST = {b.name: [list(r) for r in (rig.matrix_world @ b.matrix_local)] for b in rig.data.bones}
    REP["rig"] = dict(bones=len(rig.data.bones), old={o.name: ntri(o) for o in ref if o.type == "MESH"})
    cl = imp_fbx(X(C["clips_fbx"]), True)
    crig = next(o for o in cl if o.type == "ARMATURE"); crig.name = "ClipRig"
    for o in cl:
        if o.type == "MESH": bpy.data.objects.remove(o, do_unlink=True)
    for a in bpy.data.actions: a.use_fake_user = True
    REP["clips"] = {a.name: list(a.frame_range) for a in bpy.data.actions}

    # ============================================================================================ raw + align
    b = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=X(C["raw_glb"]), merge_vertices=True)
    newo = [o for o in bpy.data.objects if o not in b]
    raw = next(o for o in newo if o.type == "MESH")
    for o in newo:
        if o is not raw: bpy.data.objects.remove(o, do_unlink=True)
    raw.data.transform(raw.matrix_world); raw.parent = None; raw.matrix_world = Matrix.Identity(4); raw.name = "Raw"
    bm = bmesh.new(); bm.from_mesh(raw.data)
    if any(len(f.verts) > 3 for f in bm.faces): bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.to_mesh(raw.data); bm.free()
    Vt, Et = arrays(raw); Ft = np.zeros(len(raw.data.polygons) * 3, int); raw.data.polygons.foreach_get("vertices", Ft); Ft = Ft.reshape(-1, 3)
    lt = components(len(Vt), Et); fl = lt[Ft[:, 0]]
    vol = np.einsum("ij,ij->i", Vt[Ft[:, 0]], np.cross(Vt[Ft[:, 1]], Vt[Ft[:, 2]])) / 6.0
    cv = {}
    for u, v_ in zip(fl, vol): cv[u] = cv.get(u, 0.0) + v_
    flip_c = {u for u, v_ in cv.items() if v_ < 0}
    if flip_c:
        bm = bmesh.new(); bm.from_mesh(raw.data); bm.faces.ensure_lookup_table()
        bmesh.ops.reverse_faces(bm, faces=[bm.faces[i] for i in np.nonzero(np.isin(fl, list(flip_c)))[0]], flip_multires=False)
        bm.to_mesh(raw.data); bm.free()
    REP["winding"] = dict(components=len(cv), flipped_inside_out=len(flip_c), main_volume_before=round(float(max(cv.values(), key=abs)), 4))
    log("winding", REP["winding"])
    img = next(nd.image for m in raw.data.materials for nd in m.node_tree.nodes if nd.type == "TEX_IMAGE" and nd.image
               and nd.image.colorspace_settings.name == "sRGB")
    SRC = np.array(img.pixels[:], np.float32).reshape(img.size[1], img.size[0], 4)[:, :, :3]
    V, _ = arrays(raw)
    if C.get("raw_yaw_deg"):
        a_ = math.radians(C["raw_yaw_deg"]); Rz = np.array([[math.cos(a_), -math.sin(a_), 0], [math.sin(a_), math.cos(a_), 0], [0, 0, 1]])
        V = V @ Rz.T
    HW, _ = arrays(hero)

    def crown(P):
        front = P[:, 1] < P[:, 1].min() + C["crown_front_fraction"] * (P[:, 1].max() - P[:, 1].min())
        return float(np.percentile(P[front & (np.abs(P[:, 0] - np.median(P[:, 0])) < 0.2 * (P[:, 2].max() - P[:, 2].min()) / 2.2 * 2.2), 2], 99.95))
    V[:, 2] -= V[:, 2].min()
    HW[:, 2] -= HW[:, 2].min()
    target = C["crown_m"] or crown(HW)
    s = target / crown(V)
    V *= s
    feet = V[V[:, 2] < 0.12]
    V[:, 0] -= (feet[:, 0].max() + feet[:, 0].min()) / 2
    J0 = joints(rig)
    shin = (V[:, 2] > 0.25) & (V[:, 2] < 0.42) & (np.abs(V[:, 0]) > 0.08) & (np.abs(V[:, 0]) < 0.5)
    ys = []
    for sg in (1, -1):
        m = shin & (sg * V[:, 0] > 0)
        ys.append((V[m, 1].min() + V[m, 1].max()) / 2)
    a_, b_ = J0["lowerleg.l"]
    zt = 0.335; bone_y = a_[1] + (b_[1] - a_[1]) * (a_[2] - zt) / (a_[2] - b_[2])
    dy = bone_y - float(np.mean(ys))
    V[:, 1] += dy
    raw.data.vertices.foreach_set("co", V.astype(np.float32).ravel()); raw.data.update()
    REP["align"] = dict(crown_target_m=round(target, 4), scale=round(s, 5), shin_centre_y=[round(float(y), 4) for y in ys],
                        shin_bone_y=round(float(bone_y), 4), shift_y=round(dy, 4), bounds=[V.min(0).round(3).tolist(), V.max(0).round(3).tolist()])
    log("align", REP["align"])

    # ============================================================================================ A-pose fit
    A_ = rig.matrix_world.copy()
    Jr = {bb.name: A_ @ bb.head_local for bb in rig.data.bones}
    Jt = {bb.name: A_ @ bb.tail_local for bb in rig.data.bones}
    REP["pose_fit_deg"] = {}
    for sg, side in ((1, "l"), (-1, "r")):
        if Jr["hand." + side].x * sg < 0: sg = -sg
        glove = V[(sg * V[:, 0] > 0.55) & (V[:, 2] < 1.15) & (V[:, 1] < 0.2)]
        boot = V[(V[:, 2] > 0.17) & (V[:, 2] < 0.27) & (sg * V[:, 0] > 0.05) & (sg * V[:, 0] < 0.5)]
        for bone, tip, tgt in (("upperarm." + side, (Jr["hand." + side] + Jt["hand." + side]) / 2, glove.mean(0)),
                               ("upperleg." + side, Jr["foot." + side], (boot.min(0) + boot.max(0)) / 2)):
            q = (tip - Jr[bone]).normalized().rotation_difference((Vector(tuple(tgt)) - Jr[bone]).normalized())
            pb = rig.pose.bones[bone]
            Mw = Matrix.Translation(Jr[bone]) @ q.to_matrix().to_4x4() @ Matrix.Translation(-Jr[bone]) @ (A_ @ pb.matrix)
            pb.matrix = A_.inverted() @ Mw
            bpy.context.view_layer.update()
            REP["pose_fit_deg"][bone] = dict(deg=round(math.degrees(q.angle), 1), target=np.round(tgt, 3).tolist())
    JA = joints(rig)
    log("A-pose fit", REP["pose_fit_deg"])

    # ============================================================================================ parts on the raw
    Vr, Er = arrays(raw)
    lab = components(len(Vr), Er)
    ul, cnt = np.unique(lab, return_counts=True)
    main = ul[cnt.argmax()]
    speck, item = np.zeros(len(Vr), bool), np.zeros(len(Vr), bool)
    for u in ul:
        if u == main: continue
        m = lab == u
        P = Vr[m]
        if np.linalg.norm(P.max(0) - P.min(0)) < C["speck_diag_m"]: speck[m] = True
        else: item[m] = True
    # pack: behind the spine line (depth relative to the hips->head axis) and not shirt navy; grown from far-behind seeds
    me = raw.data
    uv = np.zeros(len(me.loops) * 2); me.uv_layers[0].data.foreach_get("uv", uv); uv = uv.reshape(-1, 2)
    lv = np.zeros(len(me.loops), int); me.loops.foreach_get("vertex_index", lv)
    vuv = np.zeros((len(Vr), 2)); vuv[lv] = uv
    H_, W_ = SRC.shape[:2]
    col = SRC[np.clip((vuv[:, 1] * H_).astype(int), 0, H_ - 1), np.clip((vuv[:, 0] * W_).astype(int), 0, W_ - 1)]
    navy = (col[:, 2] > col[:, 0] + 0.04) & (col[:, 2] >= col[:, 1]) & (col.max(1) < 0.55)
    hy0, hy1 = JA["hips"][0], JA["head"][1]
    spine_y = hy0[1] + (hy1[1] - hy0[1]) * np.clip((Vr[:, 2] - hy0[2]) / (hy1[2] - hy0[2]), 0, 1)
    behind = Vr[:, 1] - spine_y
    pc = C["pack"]
    band = navy & (Vr[:, 2] > JA["spine"][0][2]) & (Vr[:, 2] < JA["head"][0][2]) & (np.abs(Vr[:, 0]) < 0.25)
    back_surface = float(np.percentile(behind[band], 98)) if band.sum() > 50 else pc["grow_behind_m"]
    grow_t = max(pc["grow_behind_m"], back_surface + 0.03)
    cand = (behind > grow_t) & (Vr[:, 2] > pc["z_min"]) & (lab == main)
    seed = cand & (behind > max(pc["seed_behind_m"], grow_t + 0.15))
    REP["pack_threshold"] = dict(shirt_back_surface_behind_spine_m=round(back_surface, 4), grow_behind_m=round(grow_t, 4))
    pl = components(len(Vr), Er, cand)
    pack = cand & np.isin(pl, np.unique(pl[seed]))
    # items behind the back (loose chair pieces) belong to the pack too
    for u in ul:
        if u == main: continue
        m = lab == u
        if item[m].all() and np.median(behind[m]) > grow_t and np.median(Vr[m, 2]) > pc["z_min"]:
            pack |= m; item &= ~m
    REP["parts_raw"] = dict(components=int(len(ul)), main_verts=int(cnt.max()), speck_components=int(len(np.unique(lab[speck]))),
                            item_components=int(len(np.unique(lab[item]))), pack_verts=int(pack.sum()), raw_tris=ntri(raw))
    log("parts", REP["parts_raw"], REP["pack_threshold"])
    # evidence: the raw coloured by part (pack red, rigid items blue, specks black, skinned body grey), back and side
    lc = raw.data.color_attributes.new("Part", "BYTE_COLOR", "POINT")
    pc_ = np.tile([0.7, 0.7, 0.7, 1.0], (len(Vr), 1)); pc_[pack] = [0.9, 0.15, 0.1, 1]; pc_[item] = [0.15, 0.3, 0.9, 1]; pc_[speck] = [0, 0, 0, 1]
    lc.data.foreach_set("color", pc_.astype(np.float32).ravel())
    raw.data.color_attributes.active_color_name = "Part"
    scene.render.engine = "BLENDER_WORKBENCH"; scene.display.shading.light = "FLAT"; scene.display.shading.color_type = "VERTEX"
    scene.render.resolution_x = scene.render.resolution_y = 500
    for o in scene.objects:
        if o.type == "MESH": o.hide_render = o is not raw
    pcam = bpy.data.objects.new("pcam", bpy.data.cameras.new("pcam")); scene.collection.objects.link(pcam); scene.camera = pcam
    pcam.data.type = "ORTHO"; pcam.data.ortho_scale = 2.6
    tiles = []
    for nm, loc in (("back", (0, 6, 1.2)), ("side", (6, 0, 1.2)), ("front", (0, -6, 1.2))):
        pcam.location = loc; pcam.rotation_euler = (Vector((0, 0, 1.2)) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(OUT, "parts_%s.png" % nm); bpy.ops.render.render(write_still=True)
        tiles.append(load_px(scene.render.filepath)[:, :, :3])
    write_png(os.path.join(OUT, "parts_raw.png"), np.concatenate(tiles, 1))
    bpy.data.objects.remove(pcam, do_unlink=True); scene.camera = None
    for o in scene.objects: o.hide_render = False
    # Radii: the configured spheres first; a chunky shoulder or hip that a small sphere does not sever is cut wider
    # (x1.25, x1.5, x1.75; reported). The chosen radii are used for every later check and the weight regions.
    REP["joint_cut_raw_apose_trials"] = []
    for sc_ in (1.0, 1.25, 1.5, 1.75):
        JC_SCALE[0] = sc_
        r_, _ = joint_cut(Vr, Er, JA, item | speck | pack, "raw (A-pose) x%.2f" % sc_)
        REP["joint_cut_raw_apose_trials"].append(r_)
        if not r_["any_welded"]: break
    REP["joint_cut_raw_apose"] = r_; REP["joint_cut_scale"] = JC_SCALE[0]
    kd_raw = KDTree(len(Vr))
    for i, p in enumerate(Vr): kd_raw.insert(p, i)
    kd_raw.balance()
    RAWLAB = np.where(pack, 2, np.where(item, 1, 0))
    save()

    # ============================================================================================ LOD0
    low = raw.copy(); low.data = raw.data.copy(); scene.collection.objects.link(low); low.name = low.data.name = KEY + "_LOD0"
    while low.data.uv_layers: low.data.uv_layers.remove(low.data.uv_layers[0])
    low.data.materials.clear()
    bm = bmesh.new(); bm.from_mesh(low.data); bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.verts[i] for i in np.nonzero(speck)[0]], context="VERTS")
    nm_ = [e for e in bm.edges if len(e.link_faces) > 2]
    bmesh.ops.split_edges(bm, edges=nm_)                 # 3+ faces on an edge stop the collapse; split, never weld
    bmesh.ops.dissolve_degenerate(bm, dist=1e-7, edges=bm.edges)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bnd0 = [e for e in bm.edges if len(e.link_faces) == 1]
    # open borders stop the collapse (it keeps boundaries) and fragment the UVs: fill hole loops up to 96 edges. A fill
    # closes a loop inside its own piece; it never joins two pieces.
    if C.get("fill_holes_sides", 0) > 0:      # off by default: run 5 showed a fill loop through a bowtie vertex welding arm.r
        bmesh.ops.holes_fill(bm, edges=bnd0, sides=C["fill_holes_sides"])
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
    bnd1 = sum(1 for e in bm.edges if len(e.link_faces) == 1)
    REP["lod0_prep"] = dict(nonmanifold_edges_split=len(nm_), boundary_edges_before_fill=len(bnd0), boundary_edges_after_fill=bnd1)
    log("LOD0 prep", REP["lod0_prep"])
    bm.to_mesh(low.data); bm.free()
    Vl, _ = arrays(low)
    headz = JA["head"][0][2] - 0.04
    dense = low.vertex_groups.new(name="dense")
    hv = np.nonzero((Vl[:, 2] > headz) & (np.abs(Vl[:, 0]) < 0.3) & (Vl[:, 1] < JA["head"][0][1] + 0.25))[0]
    dense.add(list(range(len(Vl))), 1.0, "REPLACE")                # weight 0 = never collapsed, 1 = free: graded
    dense.add(hv.tolist(), C["head_decimate_weight"], "REPLACE")
    base_data = low.data.copy()

    def decimate(o, target, vg=None, inv=False):
        ratio = target / max(1, ntri(o))
        keep = o.data.copy()
        for att in range(6):
            d = o.modifiers.new("Dec", "DECIMATE")
            d.decimate_type, d.use_collapse_triangulate, d.ratio = "COLLAPSE", True, min(1.0, ratio)
            if vg:
                d.vertex_group, d.invert_vertex_group, d.vertex_group_factor = vg, inv, 1.0
            apply_mod(o, "Dec")
            t = ntri(o)
            if abs(t - target) <= 0.04 * target or att == 5: return t
            if t > target: ratio = target / t                      # stalled above: decimate the result again
            else: o.data = keep.copy(); ratio = target / max(1, ntri(o)) * 1.02
        return ntri(o)

    def head_tris(o):
        Vx, _ = arrays(o)
        return sum(1 for p in o.data.polygons if Vx[list(p.vertices)].mean(0)[2] > headz)
    decimate(low, C["lod_tris"][0], "dense", False)
    low.vertex_groups.clear()
    REP["lod0_tris"] = ntri(low); REP["lod0_head_tris"] = head_tris(low)
    log("LOD0", ntri(low), "head tris", REP["lod0_head_tris"])
    V0, E0 = arrays(low)
    REP["joint_cut_lod0_apose"], _ = joint_cut(V0, E0, JA, None, "LOD0 decimated (A-pose)")

    # ============================================================================================ UVs
    def faces_tris(o):
        F = np.zeros(len(o.data.polygons) * 3, int); o.data.polygons.foreach_get("vertices", F); return F.reshape(-1, 3)

    def head_face_mask(o):
        Vx, _ = arrays(o); F = faces_tris(o); c = Vx[F].mean(1)
        return (c[:, 2] > headz) & (np.abs(c[:, 0]) < 0.3) & (c[:, 1] < JA["head"][0][1] + 0.25)

    def uv_stats(o):
        me_ = o.data; F = faces_tris(o)
        u = np.zeros(len(me_.loops) * 2); me_.uv_layers[0].data.foreach_get("uv", u); u = u.reshape(-1, 3, 2)
        a = np.abs((u[:, 1, 0] - u[:, 0, 0]) * (u[:, 2, 1] - u[:, 0, 1]) - (u[:, 1, 1] - u[:, 0, 1]) * (u[:, 2, 0] - u[:, 0, 0])) / 2
        Vx, _ = arrays(o); P = Vx[F]
        a3 = np.linalg.norm(np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0]), axis=1) / 2
        hm = head_face_mask(o)
        return dict(coverage=round(float(a.sum()), 4), head_uv_share=round(float(a[hm].sum() / a.sum()), 4),
                    head_area_share=round(float(a3[hm].sum() / a3.sum()), 4),
                    head_texel_density_vs_body=round(float((a[hm].sum() / a3[hm].sum()) / (a[~hm].sum() / a3[~hm].sum())), 3),
                    uv_min=u.reshape(-1, 2).min(0).round(4).tolist(), uv_max=u.reshape(-1, 2).max(0).round(4).tolist())

    def scale_head_islands(o, k):
        bm_ = bmesh.new(); bm_.from_mesh(o.data); ul_ = bm_.loops.layers.uv.active
        bm_.faces.ensure_lookup_table()
        par = list(range(len(bm_.faces)))
        def fnd(i):
            while par[i] != i: par[i] = par[par[i]]; i = par[i]
            return i
        for e in bm_.edges:
            if len(e.link_faces) != 2: continue
            f1, f2 = e.link_faces
            l1 = [l for l in f1.loops if l.edge is e][0]; l2 = [l for l in f2.loops if l.edge is e][0]
            # l1 runs v0->v1, l2 runs v1->v0
            if (l1[ul_].uv - l2.link_loop_next[ul_].uv).length < 1e-5 and (l1.link_loop_next[ul_].uv - l2[ul_].uv).length < 1e-5:
                par[fnd(f1.index)] = fnd(f2.index)
        hm = head_face_mask(o)
        isl = {}
        for f in bm_.faces: isl.setdefault(fnd(f.index), []).append(f)
        n_sc = 0
        for fs in isl.values():
            if np.mean([hm[f.index] for f in fs]) < 0.5: continue
            ls = [l for f in fs for l in f.loops]
            c = sum((l[ul_].uv for l in ls), Vector((0, 0))) / len(ls)
            for l in ls: l[ul_].uv = c + (l[ul_].uv - c) * k
            n_sc += 1
        bm_.to_mesh(o.data); bm_.free()
        return len(isl), n_sc

    def unwrap(o, ang, mg):
        while o.data.uv_layers: o.data.uv_layers.remove(o.data.uv_layers[0])
        o.data.uv_layers.new(name="UVMap")
        bpy.context.view_layer.objects.active = o
        for x in scene.objects: x.select_set(x is o)
        scene.tool_settings.use_uv_select_sync = True
        bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.object.mode_set(mode="OBJECT")
        tw = o.copy(); tw.data = o.data.copy(); scene.collection.objects.link(tw)
        sm_ = tw.modifiers.new("S", "SMOOTH"); sm_.factor, sm_.iterations = 1.0, 15; apply_mod(tw, "S")
        bpy.context.view_layer.objects.active = tw
        for x in scene.objects: x.select_set(x is tw)
        bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(angle_limit=math.radians(ang), island_margin=mg, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
        bpy.ops.object.mode_set(mode="OBJECT")
        uvt = np.zeros(len(tw.data.loops) * 2); tw.data.uv_layers[0].data.foreach_get("uv", uvt)
        o.data.uv_layers[0].data.foreach_set("uv", uvt); bpy.data.objects.remove(tw, do_unlink=True)
        bpy.context.view_layer.objects.active = o
        for x in scene.objects: x.select_set(x is o)
        bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT"); bpy.ops.uv.select_all(action="SELECT")
        bpy.ops.uv.average_islands_scale()
        bpy.ops.object.mode_set(mode="OBJECT")
        n_isl, n_head = scale_head_islands(o, C["head_uv_scale"])
        bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT"); bpy.ops.uv.select_all(action="SELECT")
        bpy.ops.uv.pack_islands(rotate=True, margin=mg, margin_method="FRACTION", shape_method="CONCAVE")
        bpy.ops.object.mode_set(mode="OBJECT")
        st = uv_stats(o); st.update(angle=ang, margin=mg, islands=n_isl, head_islands_scaled=n_head)
        return st
    trials = [unwrap(low, a, m) for a, m in ((66.0, 0.002), (75.0, 0.002), (85.0, 0.002))]
    REP["uv_trials"] = trials
    bt = max(trials, key=lambda t: t["coverage"])
    REP["uv"] = unwrap(low, bt["angle"], bt["margin"])
    log("UV", REP["uv"])
    save()

    # ============================================================================================ bake
    S2, TEX = 2 * C["tex"], C["tex"]
    me = low.data; F0 = faces_tris(low); V0, E0 = arrays(low)
    me.update()
    N0 = np.zeros(len(me.vertices) * 3); me.vertices.foreach_get("normal", N0); N0 = N0.reshape(-1, 3)
    U0 = np.zeros(len(me.loops) * 2); me.uv_layers[0].data.foreach_get("uv", U0); U0 = U0.reshape(-1, 3, 2)
    pos = np.zeros((S2, S2, 3), np.float32); nor = np.zeros((S2, S2, 3), np.float32); cov = np.zeros((S2, S2), bool)
    for t in range(len(F0)):
        P = U0[t] * S2 - 0.5
        x0, y0 = np.floor(P.min(0)).astype(int); x1, y1 = np.ceil(P.max(0)).astype(int)
        x0, y0 = max(x0, 0), max(y0, 0); x1, y1 = min(x1, S2 - 1), min(y1, S2 - 1)
        if x1 < x0 or y1 < y0: continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
        a, b, c = P
        den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(den) < 1e-12: continue
        w0 = ((b[1] - c[1]) * (gx - c[0]) + (c[0] - b[0]) * (gy - c[1])) / den
        w1 = ((c[1] - a[1]) * (gx - c[0]) + (a[0] - c[0]) * (gy - c[1])) / den
        w2 = 1 - w0 - w1
        ins = (w0 >= -0.02) & (w1 >= -0.02) & (w2 >= -0.02)
        if not ins.any(): continue
        W3 = np.stack([w0[ins], w1[ins], w2[ins]], 1).clip(0, 1); W3 /= W3.sum(1, keepdims=True)
        pos[gy[ins], gx[ins]] = W3 @ V0[F0[t]]
        nor[gy[ins], gx[ins]] = W3 @ N0[F0[t]]
        cov[gy[ins], gx[ins]] = True
    Fr = faces_tris(raw)
    ur = np.zeros(len(raw.data.loops) * 2); raw.data.uv_layers[0].data.foreach_get("uv", ur); ur = ur.reshape(-1, 3, 2)
    bvh = BVHTree.FromPolygons(Vr.tolist(), Fr.tolist())
    ii = np.argwhere(cov)
    hitp = np.zeros((len(ii), 3)); hitf = np.zeros(len(ii), int); nray = nfall = 0
    CAGE = 0.008
    for k, (y, x) in enumerate(ii):
        p = Vector(pos[y, x]); n = Vector(nor[y, x])
        if n.length > 1e-6: n.normalize()
        r = bvh.ray_cast(p + n * CAGE, -n, 2 * CAGE)
        if r[0] is not None and abs(r[1].dot(n)) > 0.2:
            hitp[k] = r[0]; hitf[k] = r[2]; nray += 1
        else:
            r = bvh.find_nearest(p); hitp[k] = r[0]; hitf[k] = r[2]; nfall += 1
    T3 = Vr[Fr[hitf]]
    v0_, v1_, v2_ = T3[:, 1] - T3[:, 0], T3[:, 2] - T3[:, 0], hitp - T3[:, 0]
    d00 = (v0_ * v0_).sum(1); d01 = (v0_ * v1_).sum(1); d11 = (v1_ * v1_).sum(1); d20 = (v2_ * v0_).sum(1); d21 = (v2_ * v1_).sum(1)
    den = np.maximum(d00 * d11 - d01 * d01, 1e-18)
    bv = (d11 * d20 - d01 * d21) / den; bw = (d00 * d21 - d01 * d20) / den; bu = 1 - bv - bw
    UVh = bu[:, None] * ur[hitf, 0] + bv[:, None] * ur[hitf, 1] + bw[:, None] * ur[hitf, 2]
    sx = UVh[:, 0] * W_ - 0.5; sy = UVh[:, 1] * H_ - 0.5
    ix, iy = np.floor(sx).astype(int), np.floor(sy).astype(int); fx, fy = (sx - ix)[:, None], (sy - iy)[:, None]
    g = lambda yy, xx: SRC[np.clip(yy, 0, H_ - 1), np.clip(xx, 0, W_ - 1)]
    rgb = (g(iy, ix) * (1 - fx) * (1 - fy) + g(iy, ix + 1) * fx * (1 - fy) + g(iy + 1, ix) * (1 - fx) * fy + g(iy + 1, ix + 1) * fx * fy)
    big = np.zeros((S2, S2, 3), np.float32); big[ii[:, 0], ii[:, 1]] = rgb
    c4 = cov.reshape(TEX, 2, TEX, 2).sum((1, 3)).astype(np.float32)
    small = big.reshape(TEX, 2, TEX, 2, 3).sum((1, 3)) / np.maximum(c4, 1)[:, :, None]
    covs = c4 > 0
    REP["bake"] = dict(samples=int(len(ii)), outside_in_ray_hits=nray, nearest_fallback=nfall, texels_covered=round(float(covs.mean()), 4))
    log("bake", REP["bake"])

    # ============================================================================================ team mask
    def shift(a, dy, dx):
        return np.roll(np.roll(a, dy, 0), dx, 1)

    def erode(m): return m & shift(m, 1, 0) & shift(m, -1, 0) & shift(m, 0, 1) & shift(m, 0, -1)
    def dilate(m): return m | shift(m, 1, 0) | shift(m, -1, 0) | shift(m, 0, 1) | shift(m, 0, -1)
    mx = small.max(2); mn = small.min(2); sat = (mx - mn) / np.maximum(mx, 1e-6)
    hue = np.zeros_like(mx)
    r_, g_, b_ = small[:, :, 0], small[:, :, 1], small[:, :, 2]
    dd = np.maximum(mx - mn, 1e-6)
    hue = np.where(mx == r_, (g_ - b_) / dd % 6, np.where(mx == g_, (b_ - r_) / dd + 2, (r_ - g_) / dd + 4)) * 60
    tc = C["team"]
    teal = covs & (hue >= tc["hue_deg"][0]) & (hue <= tc["hue_deg"][1]) & (sat >= tc["sat_min"]) & (mx >= tc["val_min"])
    raw_frac = float(teal.sum() / covs.sum())
    m2 = dilate(erode(teal)); m2 = erode(dilate(m2)) & covs
    pix = np.argwhere(m2); idx = -np.ones(m2.shape, int); idx[m2] = np.arange(len(pix))
    Ep = []
    for dy_, dx_ in ((0, 1), (1, 0)):
        nb = m2 & shift(m2, -dy_, -dx_)
        a = idx[nb]; b2 = shift(idx, -dy_, -dx_)[nb]; Ep.append(np.stack([a, b2], 1))
    Ep = np.concatenate(Ep) if len(pix) else np.zeros((0, 2), int)
    pl_ = components(len(pix), Ep)
    u_, cn_ = np.unique(pl_, return_counts=True)
    small_blobs = u_[cn_ < 20]
    m2[tuple(pix[np.isin(pl_, small_blobs)].T)] = False
    mask = m2.astype(np.float32) * 0.5
    REP["team_mask"] = dict(teal_raw_fraction_of_covered=round(raw_frac, 4), team_cloth_fraction_of_covered=round(float(m2.sum() / covs.sum()), 4),
                            blobs_removed=int(len(small_blobs)), blobs_kept=int(len(u_) - len(small_blobs)))
    log("mask", REP["team_mask"])
    img4 = np.concatenate([small, mask[:, :, None]], 2)
    c_ = covs.copy()
    for it in range(32):
        acc = np.zeros_like(img4); n_ = np.zeros(c_.shape, np.float32)
        for dy_, dx_ in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1)):
            sc = shift(c_, dy_, dx_); acc += shift(img4, dy_, dx_) * sc[:, :, None]; n_ += sc
        grow = ~c_ & (n_ > 0)
        img4[grow] = acc[grow] / n_[grow][:, None]; c_ |= grow
    img4[~c_] = np.array([0.5, 0.5, 0.5, 0.0])
    write_png(os.path.join(OUT, KEY + "_Base.png"), img4)
    write_png(os.path.join(OUT, KEY + "_mask.png"), np.concatenate([np.repeat(img4[:, :, 3:4] * 2, 3, 2),
                                                                    np.where(img4[:, :, 3:4] > 0.25, np.array([1.0, 0.0, 1.0]), img4[:, :, :3])], 1))

    def swap(rgb_, target, authored):
        k = rgb_.max(-1, keepdims=True) / max(1e-4, authored.max())
        return np.clip(target * k, 0, 1)
    auth = hexrgb(tc["cloth_authored"])
    for team in ("azure", "crimson"):
        cl_ = np.clip(img4[:, :, 3:4] * 2, 0, 1)
        v = img4[:, :, :3] * (1 - cl_) + swap(img4[:, :, :3], hexrgb(tc[team]), auth) * cl_
        write_png(os.path.join(OUT, KEY + "_preview_%s.png" % team), v)
    save()

    # ============================================================================================ weights
    def compute_weights(o, tag):
        Vx, Ex = arrays(o); n = len(Vx)
        labx = components(n, Ex); ux, cx = np.unique(labx, return_counts=True); mainx = ux[cx.argmax()]
        _, ri, _ = zip(*[kd_raw.find(p) for p in Vx]); ri = np.array(ri)
        packx = (RAWLAB[ri] == 2)
        itemx = (labx != mainx) & ~packx
        rep, cut = joint_cut(Vx, Ex, JA, itemx | packx, tag + " weights regions")
        D = bone_dists(Vx, JA)
        allow = np.zeros((n, len(DEFORM)), bool)
        fac = np.ones((n, len(DEFORM)))
        torso_b = [BI[b] for b in ("hips", "spine", "chest", "head")]
        allow[:, torso_b] = True
        below = Vx[:, 2] < JA["hips"][0][2]
        for s in ("l", "r"):
            allow[below, BI["upperleg." + s]] = True
            fac[below, BI["upperleg." + s]] = C["weights"]["apron_leg_factor"]
        regions = {}
        for s in ("l", "r"):
            arm = [BI[b + "." + s] for b in ("upperarm", "lowerarm", "wrist", "hand")]
            leg = [BI[b + "." + s] for b in ("upperleg", "lowerleg", "foot", "toes")]
            for kind, chain, zone, root, chain_names in (("arm", arm, cut["rem"]["sh" + s], [BI["chest"], arm[0]], ["upperarm"]),
                                                         ("leg", leg, cut["rem"]["hip" + s], [BI["hips"], leg[0]], ["upperleg"])):
                pr = cut["probes"][kind + "." + s]
                comps = np.unique(cut["lab"][pr])
                reg = cut["keep"] & np.isin(cut["lab"], comps)
                reg &= Vx[:, 0] * np.sign(JA[chain_names[0] + "." + s][0][0]) > 0     # the legs join at the crotch: own side only
                if rep[kind + "." + s]["welded"]:
                    reg &= np.isin(D.argmin(1), chain)          # welded: Euclid fallback, reported
                reg &= ~packx & ~itemx
                zn = zone & ~packx & ~itemx & ~reg
                allow[reg] = False; allow[reg][:, chain] = True
                rr = np.nonzero(reg)[0]; allow[np.ix_(rr, chain)] = True; fac[rr] = 1
                zz = np.nonzero(zn)[0]; allow[zz] = False; allow[np.ix_(zz, root)] = True; fac[zz] = 1
                regions[kind + "." + s] = int(reg.sum()); regions[kind + "_zone." + s] = int(zn.sum())
        in_limb = np.zeros(n, bool)
        for s in ("l", "r"):
            for kind in ("arm", "leg"):
                pr = cut["probes"][kind + "." + s]
                in_limb |= np.isin(cut["lab"], np.unique(cut["lab"][pr])) & cut["keep"]
        zone_any = cut["rem"]["shl"] | cut["rem"]["shr"] | cut["rem"]["hipl"] | cut["rem"]["hipr"]
        stray = ~in_limb & ~zone_any & ~packx & ~itemx & (Vx[:, 2] < JA["hips"][0][2] - 0.2) & (Vx[:, 1] > JA["hips"][0][1] - 0.15)
        for s in ("l", "r"):
            leg = [BI[b + "." + s] for b in ("upperleg", "lowerleg", "foot", "toes")]
            ss = np.nonzero(stray & (Vx[:, 0] * np.sign(JA["upperleg." + s][0][0]) > 0))[0]
            allow[ss] = False; allow[np.ix_(ss, leg)] = True; fac[ss] = 1
        regions["stray_low_to_legs"] = int(stray.sum())
        Wt = np.where(allow, fac / np.maximum(D, 0.01) ** C["weights"]["power"], 0.0)
        fixed = np.zeros(n, bool); fixW = np.zeros_like(Wt)
        pi = np.nonzero(packx)[0]; fixW[pi, BI["chest"]] = 1; fixed[pi] = True
        for u in np.unique(labx[itemx]):
            m = labx == u; c = Vx[m].mean(0)
            dh = {s: np.linalg.norm(c - (JA["hand." + s][0] + JA["hand." + s][1]) / 2) for s in ("l", "r")}
            sbest = min(dh, key=dh.get)
            limb = [b for b in DEFORM if b not in ("hips", "spine", "chest", "head")]
            dl_ = {b: float(segdist(c[None], *JA[b])[0]) for b in limb}
            lb = min(dl_, key=dl_.get)
            if dh[sbest] < 0.15: bname = "hand." + sbest
            elif dl_[lb] < 0.12 and not lb.startswith("upperarm"): bname = lb
            elif c[2] > JA["head"][0][2]: bname = "head"
            elif c[2] > JA["spine"][1][2]: bname = "chest"
            else: bname = "hips"
            fixW[m, BI[bname]] = 1; fixed[m] = True
        Wt[fixed] = fixW[fixed]

        def top4(Wm):
            o_ = np.argsort(-Wm, 1)[:, 4:]
            np.put_along_axis(Wm, o_, 0, 1)
            return Wm / np.maximum(Wm.sum(1, keepdims=True), 1e-12)
        Wt = top4(Wt)
        deg = np.zeros(n); np.add.at(deg, Ex[:, 0], 1); np.add.at(deg, Ex[:, 1], 1)
        for it in range(C["weights"]["smooth_iters"]):
            acc = np.zeros_like(Wt); np.add.at(acc, Ex[:, 0], Wt[Ex[:, 1]]); np.add.at(acc, Ex[:, 1], Wt[Ex[:, 0]])
            Wt = 0.5 * Wt + 0.5 * acc / np.maximum(deg, 1)[:, None]
            Wt = np.where(allow | fixed[:, None], Wt, 0)
            Wt[fixed] = fixW[fixed]
            Wt = top4(Wt)
        R_ = int(C["weights"]["pack_feather_rings"])
        ring = np.full(n, 99); ring[packx] = 0
        for k_ in range(1, R_ + 1):
            a_, b_ = Ex[:, 0], Ex[:, 1]
            nb_ = np.zeros(n, bool); nb_[a_[ring[b_] == k_ - 1]] = True; nb_[b_[ring[a_] == k_ - 1]] = True
            ring[nb_ & (ring == 99)] = k_
        fe = (ring > 0) & (ring <= R_) & ~fixed
        tch = np.zeros(len(DEFORM)); tch[BI["chest"]] = 1
        al_ = (1.0 - ring[fe] / (R_ + 1.0))[:, None]
        Wt[fe] = top4(Wt[fe] * (1 - al_) + tch * al_)
        Wt[Wt < 1e-3] = 0; Wt = Wt / np.maximum(Wt.sum(1, keepdims=True), 1e-12)
        o.vertex_groups.clear()
        vgs = [o.vertex_groups.new(name=bname) for bname in DEFORM]
        for vi, bi in zip(*np.nonzero(Wt > 0)):
            vgs[bi].add([int(vi)], float(Wt[vi, bi]), "REPLACE")
        ninf = (Wt > 0).sum(1)
        wr = dict(max_influences=int(ninf.max()), mean_influences=round(float(ninf.mean()), 2), unweighted=int((ninf == 0).sum()),
                  sum_err=float(np.abs(Wt.sum(1) - 1).max()), pack_verts=int(packx.sum()), item_verts=int(itemx.sum()),
                  item_components=int(len(np.unique(labx[itemx]))), regions=regions)
        log("weights", tag, wr)
        return wr, rep
    REP["weights"] = {}
    REP["weights"]["LOD0"], REP["joint_cut_lod0_weights"] = compute_weights(low, "LOD0")
    lod1 = low.copy(); lod1.data = low.data.copy(); scene.collection.objects.link(lod1); lod1.name = lod1.data.name = KEY + "_LOD1"
    decimate(lod1, C["lod_tris"][1])
    shd = lod1.copy(); shd.data = lod1.data.copy(); scene.collection.objects.link(shd); shd.name = shd.data.name = KEY + "_Shadow"
    while shd.data.uv_layers: shd.data.uv_layers.remove(shd.data.uv_layers[0])   # shadow caster: no seams to keep
    decimate(shd, C["lod_tris"][2])
    shd.data.uv_layers.new(name="UVMap")                                          # UV0 present (zeros), as the material samples it
    for o in (lod1, shd):
        REP["weights"][o.name.split("_")[-1]], REP["joint_cut_%s_apose" % o.name.split("_")[-1].lower()] = compute_weights(o, o.name)
    REP["tris"] = {o.name: ntri(o) for o in (low, lod1, shd)}
    log("tris", REP["tris"])

    # ============================================================================================ unskin to the rest
    LODS = [low, lod1, shd]
    SK = {pb.name: A_ @ pb.matrix @ pb.bone.matrix_local.inverted() @ A_.inverted() for pb in rig.pose.bones}
    REP["unskin_max_move_m"] = {}
    for o in LODS:
        gname = {g.index: g.name for g in o.vertex_groups}
        mv = 0.0
        for v in o.data.vertices:
            M = Matrix.Diagonal((0.0, 0.0, 0.0, 0.0)); tot = 0.0
            for g_ in v.groups:
                if g_.weight > 0: M = M + SK[gname[g_.group]] * g_.weight; tot += g_.weight
            if tot <= 0: continue
            rp = (M * (1.0 / tot)).inverted_safe() @ v.co
            mv = max(mv, (rp - v.co).length); v.co = rp
        o.data.update(); REP["unskin_max_move_m"][o.name] = round(mv, 4)
    for pb in rig.pose.bones: pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    JR = joints(rig)
    for o in LODS:
        o.parent = rig; o.matrix_parent_inverse = rig.matrix_world.inverted()
        am = o.modifiers.new("Armature", "ARMATURE"); am.object = rig
        for p in o.data.polygons: p.use_smooth = True
        o.data.set_sharp_from_angle(angle=math.radians(60.0))
        ca = o.data.color_attributes.new("Col", "BYTE_COLOR", "CORNER")      # code 0 (fixed, lined): outline mask path
        ca.data.foreach_set("color", np.tile([1.0, 1.0, 1.0, 0.0], len(ca.data)).astype(np.float32))
        o.data.color_attributes.active_color_name = "Col"
        o.data.materials.clear()
    mat = bpy.data.materials.new(KEY); mat.use_nodes = True
    for o in LODS: o.data.materials.append(mat)
    REP["joint_cut_rest"] = {}
    for o in LODS:
        Vx, Ex = arrays(o)
        REP["joint_cut_rest"][o.name], _ = joint_cut(Vx, Ex, JR, None, o.name + " rest")
    Vx, _ = arrays(low)
    REP["height_lod0_rest_m"] = round(float(Vx[:, 2].max()), 4)
    REP["joint_cut_rest_spheres"] = {s: dict(shoulder=JR["upperarm." + s][0].round(4).tolist(), hip=JR["upperleg." + s][0].round(4).tolist()) for s in ("l", "r")}
    for o in list(scene.objects):
        if o.type == "MESH" and o not in LODS and o is not old0: bpy.data.objects.remove(o, do_unlink=True)
    REP["REST"] = REST
    save()
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "stage_skinned.blend"))
    log("saved stage_skinned.blend")
else:
    bpy.ops.wm.open_mainfile(filepath=FROM_BLEND)
    REP = json.load(open(os.path.join(OUT, KEY + "_report.json")))
    scene = bpy.context.scene
    rig = bpy.data.objects["Rig"]; crig = bpy.data.objects["ClipRig"]
    old0 = next(o for o in scene.objects if o.type == "MESH" and o.name.startswith(C["rig_prefix"] + "LOD0"))
    LODS = [bpy.data.objects[KEY + s] for s in ("_LOD0", "_LOD1", "_Shadow")]
    low, lod1, shd = LODS
    REST = REP["REST"]

# ================================================================================================ clip proof
A_ = rig.matrix_world.copy(); Ai = A_.inverted()
depth = {}
for pb in rig.pose.bones:
    d, p = 0, pb.parent
    while p: d += 1; p = p.parent
    depth.setdefault(d, []).append(pb)


def act_by(name):
    return next(a for a in bpy.data.actions if a.name.split("|")[-1] == name)


def pose_clip(act, f):
    ad = crig.animation_data or crig.animation_data_create()
    if ad.action is not act:
        ad.action = act
        if getattr(ad, "action_slot", None) is None and act.slots: ad.action_slot = act.slots[0]
    scene.frame_set(int(f))
    for d in sorted(depth):
        for pb in depth[d]:
            cpb = crig.pose.bones.get(pb.name)
            if cpb is not None: pb.matrix = Ai @ crig.matrix_world @ cpb.matrix
        bpy.context.view_layer.update()


def rest_pose():
    for pb in rig.pose.bones: pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()


def deformed(o):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(dg); m = ev.to_mesh()
    co = np.zeros(len(m.vertices) * 3); m.vertices.foreach_get("co", co); ev.to_mesh_clear()
    return co.reshape(-1, 3)


MEAS = {"new_LOD0": low, "new_LOD1": lod1, "current_LOD0": old0}
if globals().get("LOW_OLD") is not None: MEAS["heat_LOD0"] = LOW_OLD          # method 2 transfer run: the heat weights
for c_ in globals().get("EXTRAS", []): MEAS[c_.name.split("_LOD0_")[1] + "_LOD0"] = c_
for o in MEAS.values():
    for m in o.modifiers:
        if m.type != "ARMATURE": m.show_viewport = False
rest_pose()
RL = {}
for k, o in MEAS.items():
    _, E = arrays(o); R0 = deformed(o)
    L = np.linalg.norm(R0[E[:, 0]] - R0[E[:, 1]], axis=1)
    RL[k] = (E, L, L > 0.002, R0)
JR = joints(rig)
stretch = {}
for act in sorted(bpy.data.actions, key=lambda a: a.name):
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    step = 1 if f1 - f0 <= 40 else 2
    row = {}
    for k in MEAS: row[k] = dict(max=1.0, p999=1.0, worst_frame=f0, worst_bone=None)
    for f in range(f0, f1 + 1, step):
        pose_clip(act, f)
        for k, o in MEAS.items():
            E, L, ok, R0 = RL[k]
            D = deformed(o)
            r = np.linalg.norm(D[E[:, 0]] - D[E[:, 1]], axis=1)[ok] / L[ok]
            mx_ = float(r.max())
            if mx_ > row[k]["max"]:
                wi = np.nonzero(ok)[0][r.argmax()]
                mid = (R0[E[wi, 0]] + R0[E[wi, 1]]) / 2
                bd = {b: float(segdist(mid[None], *JR[b])[0]) for b in DEFORM}
                gn_ = {g.index: g.name for g in o.vertex_groups}
                vb_ = [sorted(((gn_[g.group], round(g.weight, 2)) for g in o.data.vertices[int(E[wi, j])].groups if g.weight > 0.01), key=lambda t: -t[1]) for j in (0, 1)]
                row[k].update(max=round(mx_, 3), worst_frame=f, worst_bone=min(bd, key=bd.get), worst_at=np.round(mid, 3).tolist(), worst_edge_weights=vb_)
            row[k]["p999"] = round(max(row[k]["p999"], float(np.percentile(r, 99.9))), 3)
    stretch[act.name.split("|")[-1]] = row
    log("stretch %-14s" % act.name.split("|")[-1], " | ".join("%s max %.2f p99.9 %.2f (%s f%d)" % (k, v["max"], v["p999"], v["worst_bone"], v["worst_frame"]) for k, v in row.items()))
REP["clip_stretch"] = stretch
REP["stretch_summary"] = {k: dict(worst_max=max(s_[k]["max"] for s_ in stretch.values()),
                                  worst_clip=max(stretch, key=lambda c: stretch[c][k]["max"]),
                                  worst_p999=max(s_[k]["p999"] for s_ in stretch.values())) for k in MEAS}
log("stretch summary", REP["stretch_summary"])
save()

# ================================================================================================ renders
cc = C["clips"]


def bone_z(act, f, bname):
    pose_clip(act, f); return float((rig.matrix_world @ rig.pose.bones[bname].head)[2])


atk = act_by(cc["attack"]); fa0, fa1 = int(atk.frame_range[0]), int(atk.frame_range[1])
hz = {f: bone_z(atk, f, "hand.r") for f in range(fa0, fa1 + 1)}
f_raise = max(hz, key=hz.get)
after = {f: z for f, z in hz.items() if f > f_raise}
f_contact = min(after, key=after.get) if after else fa1
run = act_by(cc["run"]); fr0, fr1 = int(run.frame_range[0]), int(run.frame_range[1])
sep = {}
for f in range(fr0, fr1 + 1):
    pose_clip(run, f)
    sep[f] = abs(float((rig.pose.bones["foot.l"].head - rig.pose.bones["foot.r"].head).y))
f_run = max(sep, key=sep.get)
hit = act_by(cc["hit"])
POSES = [("idle", act_by(cc["idle"]), int(act_by(cc["idle"]).frame_range[0])), ("run", run, f_run), ("attack_raise", atk, f_raise),
         ("attack_contact", atk, f_contact), ("hit", hit, int(hit.frame_range[0] + 0.4 * (hit.frame_range[1] - hit.frame_range[0])))]
if cc.get("walk"):                                     # walk mid-stride: largest foot separation, like the run
    wlk = act_by(cc["walk"]); sepw = {}
    for f in range(int(wlk.frame_range[0]), int(wlk.frame_range[1]) + 1):
        pose_clip(wlk, f); sepw[f] = abs(float((rig.pose.bones["foot.l"].head - rig.pose.bones["foot.r"].head).y))
    POSES.insert(1, ("walk", wlk, max(sepw, key=sepw.get)))
REP["render_poses"] = [(n, a.name.split("|")[-1], f) for n, a, f in POSES]
log("render poses", REP["render_poses"])

scene.render.engine = "BLENDER_EEVEE"
scene.eevee.taa_render_samples = 8
scene.view_settings.view_transform = "Standard"
scene.render.film_transparent = False
scene.render.resolution_x = scene.render.resolution_y = C["render_px"]
world = bpy.data.worlds.new("w"); scene.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.20, 0.22, 0.20, 1); world.node_tree.nodes["Background"].inputs[1].default_value = 0.6
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); scene.collection.objects.link(sun)
sun.data.energy = 3.0; sun.rotation_euler = Vector((1.0, 1.0, -1.6)).to_track_quat("-Z", "Y").to_euler()
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam); scene.camera = cam
cam.data.sensor_fit = "VERTICAL"; cam.data.angle = math.radians(35)
ink = bpy.data.materials.new("ink"); ink.use_nodes = True; ink.use_backface_culling = True
nt = ink.node_tree; [nt.nodes.remove(n) for n in list(nt.nodes)]
e = nt.nodes.new("ShaderNodeEmission"); e.inputs[0].default_value = (0.012, 0.016, 0.03, 1); o_ = nt.nodes.new("ShaderNodeOutputMaterial"); nt.links.new(e.outputs[0], o_.inputs[0])


def toon_mat(name, source):
    m = bpy.data.materials.new(name); m.use_nodes = True; t = m.node_tree; [t.nodes.remove(n) for n in list(t.nodes)]
    if isinstance(source, str) and source.endswith(".png"):
        c = t.nodes.new("ShaderNodeTexImage"); c.image = bpy.data.images.load(source); c.interpolation = "Linear"; out_c = c.outputs[0]
    else:
        c = t.nodes.new("ShaderNodeAttribute"); c.attribute_name = source; out_c = c.outputs["Color"]
    d = t.nodes.new("ShaderNodeBsdfDiffuse"); s2r = t.nodes.new("ShaderNodeShaderToRGB")
    ramp = t.nodes.new("ShaderNodeValToRGB"); ramp.color_ramp.interpolation = "CONSTANT"
    els = ramp.color_ramp.elements; els[0].position = 0.0; els[0].color = (0.50, 0.52, 0.62, 1); els[1].position = 0.08; els[1].color = (0.78, 0.78, 0.82, 1)
    e3 = els.new(0.32); e3.color = (1, 1, 1, 1)
    mix = t.nodes.new("ShaderNodeMix"); mix.data_type = "RGBA"; mix.blend_type = "MULTIPLY"; mix.inputs[0].default_value = 1.0
    em = t.nodes.new("ShaderNodeEmission"); out = t.nodes.new("ShaderNodeOutputMaterial")
    t.links.new(d.outputs[0], s2r.inputs[0]); t.links.new(s2r.outputs[0], ramp.inputs[0])
    t.links.new(out_c, mix.inputs[6]); t.links.new(ramp.outputs[0], mix.inputs[7]); t.links.new(mix.outputs[2], em.inputs[0]); t.links.new(em.outputs[0], out.inputs[0])
    return m


# old LOD0 team colour attributes (vertex colour, code 8 swapped as CMToonSwap does)
ca = old0.data.color_attributes.get("Col")
cs = np.zeros(len(ca.data) * 4); ca.data.foreach_get("color_srgb", cs); cs = cs.reshape(-1, 4)
code = np.round(cs[:, 3] * 8).astype(int)
tc = C["team"]; auth = hexrgb(tc["cloth_authored"])
for team in ("azure", "crimson"):
    k = cs[:, :3].max(1, keepdims=True) / auth.max()
    rgb = np.where((code == 8)[:, None], np.clip(hexrgb(tc[team]) * k, 0, 1), cs[:, :3])
    a2 = old0.data.color_attributes.new("T_" + team, "BYTE_COLOR", "CORNER")
    a2.data.foreach_set("color_srgb", np.concatenate([rgb, np.ones((len(rgb), 1))], 1).astype(np.float32).ravel())
MATS = {("new", t): toon_mat("new_" + t, os.path.join(OUT, KEY + "_preview_%s.png" % t)) for t in ("azure", "crimson")}
MATS.update({("old", t): toon_mat("old_" + t, "T_" + t) for t in ("azure", "crimson")})
MODELS = {"new": low, "old": old0}
for o in MODELS.values():
    o.data.materials.clear(); o.data.materials.append(MATS[("new" if o is low else "old", "azure")]); o.data.materials.append(ink)
    for p in o.data.polygons: p.material_index = 0
    sm = o.modifiers.new("hull", "SOLIDIFY"); sm.thickness = 0.014; sm.offset = 1.0; sm.use_flip_normals = True; sm.use_rim = False
    sm.material_offset = 1; sm.show_viewport = True
VIEWS = [("game55", 35.0, 55.0, 6.2), ("front10", 0.0, 10.0, 5.2), ("side10", 90.0, 10.0, 5.2)]


def shot(path, az, el, dist, tz=1.05):
    a, e_ = math.radians(az), math.radians(el)
    tgt = Vector((0, 0, tz))
    cam.location = tgt + Vector((math.cos(e_) * math.sin(a), -math.cos(e_) * math.cos(a), math.sin(e_))) * dist
    cam.rotation_euler = (tgt - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = path; bpy.ops.render.render(write_still=True)


RD = os.path.join(OUT, "tiles"); os.makedirs(RD, exist_ok=True)
lod1.hide_render = shd.hide_render = True
if globals().get("LOW_OLD") is not None: LOW_OLD.hide_render = True
for c_ in globals().get("EXTRAS", []): c_.hide_render = True
for which, o in MODELS.items():
    for x in MODELS.values(): x.hide_render = x is not o
    grid_rows = []
    for pname, act, f in POSES:
        pose_clip(act, f)
        row = []
        for vname, az, el, dist in VIEWS:
            for team in ("azure", "crimson"):
                o.data.materials[0] = MATS[(which, team)]
                p = os.path.join(RD, "%s_%s_%s_%s.png" % (which, pname, vname, team)); shot(p, az, el, dist)
                row.append(load_px(p)[:, :, :3])
        grid_rows.append(np.concatenate(row, 1))
    write_png(os.path.join(OUT, "grid_%s.png" % which), np.concatenate(grid_rows[::-1], 0))
    rest_pose()
    o.data.materials[0] = MATS[(which, "azure")]
    hz_ = float(np.array(rig.matrix_world @ rig.pose.bones["head"].head)[2]) + 0.2
    scene.render.resolution_x = scene.render.resolution_y = 600
    faces = []
    for az in (0.0, 30.0):
        p = os.path.join(RD, "%s_face_%d.png" % (which, az)); shot(p, az, 5.0, 1.25, hz_); faces.append(load_px(p)[:, :, :3])
    write_png(os.path.join(OUT, "face_%s.png" % which), np.concatenate(faces, 1))
    scene.render.resolution_x = scene.render.resolution_y = C["render_px"]
for x in MODELS.values(): x.hide_render = False
gn, go = load_px(os.path.join(OUT, "grid_new.png"))[:, :, :3], load_px(os.path.join(OUT, "grid_old.png"))[:, :, :3]
# side by side per pose row: new (left 6 tiles) | current (right 6 tiles), gameplay columns only for a compact sheet
tile = C["render_px"]
write_png(os.path.join(OUT, "side_by_side_game55.png"), np.concatenate([gn[:, :2 * tile], go[:, :2 * tile]], 1))
write_png(os.path.join(OUT, "side_by_side_front_side10.png"), np.concatenate([gn[:, 2 * tile:], go[:, 2 * tile:]], 1))
if globals().get("LOW_OLD") is not None and C.get("closeups"):
    # close views of named spots, heat weights (left) vs transferred weights (right), same mesh, same texture, mallet on
    CCOLS = [("heat", LOW_OLD)] + [(c_.name.split("_LOD0_")[1], c_) for c_ in globals().get("EXTRAS", [])] + [("final_" + str(C.get("weights_transfer", {}).get("arm_mode", "transfer")), low)]
    for _, ob_ in CCOLS[:-1]:
        ob_.data.materials.clear(); ob_.data.materials.append(MATS[("new", "azure")]); ob_.data.materials.append(ink)
        for p_ in ob_.data.polygons: p_.material_index = 0
        sm = ob_.modifiers.new("hull", "SOLIDIFY"); sm.thickness = 0.014; sm.offset = 1.0; sm.use_flip_normals = True; sm.use_rim = False; sm.material_offset = 1
    low.data.materials[0] = MATS[("new", "azure")]
    for x in MODELS.values(): x.hide_render = True
    mal_ = bpy.data.objects.get("Mallet")
    if mal_ is not None: mal_.hide_render = not C["closeups"].get("show_mallet", False)    # it covers the spots
    cu = C["closeups"]; scene.render.resolution_x = scene.render.resolution_y = cu.get("px", 380)
    rest_pose(); Rw = {}
    for nm_, sp in cu["spots"].items():
        h_, t_ = joints(rig)[sp["bone"]]; Rw[nm_] = h_ + sp["t"] * (t_ - h_) + np.array(sp.get("offset_m", [0, 0, 0]))
    REP["closeups"] = {}
    for nm_, sp in cu["spots"].items():
        rows = []
        for pname, act, f in POSES:
            if pname not in cu["poses"]: continue
            pose_clip(act, f); pb = rig.pose.bones[sp["bone"]]
            Mp = np.array(rig.matrix_world @ pb.matrix @ pb.bone.matrix_local.inverted() @ rig.matrix_world.inverted())
            tg = Mp[:3, :3] @ Rw[nm_] + Mp[:3, 3]
            a, e_ = math.radians(sp["az"]), math.radians(sp["el"]); tv = Vector(tuple(tg))
            cam.location = tv + Vector((math.cos(e_) * math.sin(a), -math.cos(e_) * math.cos(a), math.sin(e_))) * sp["dist"]
            cam.rotation_euler = (tv - cam.location).to_track_quat("-Z", "Y").to_euler()
            row = []
            for which, o in CCOLS:
                o.hide_render = False
                pth = os.path.join(RD, "close_%s_%s_%s.png" % (nm_, pname, which)); scene.render.filepath = pth; bpy.ops.render.render(write_still=True)
                o.hide_render = True; row.append(load_px(pth)[:, :, :3])
            rows.append(np.concatenate(row, 1))
        fn_ = os.path.join(OUT, "closeup_%s.png" % nm_); write_png(fn_, np.concatenate(rows[::-1], 0))
        REP["closeups"][nm_] = dict(file=fn_, rows_top_to_bottom=[p_[0] for p_ in POSES if p_[0] in cu["poses"]], cols=" | ".join(c_[0] for c_ in CCOLS))
    scene.render.resolution_x = scene.render.resolution_y = C["render_px"]
    for x in MODELS.values(): x.hide_render = False
    if mal_ is not None: mal_.hide_render = False
    rest_pose()
REP["renders"] = dict(grid_cols="game55 azure, game55 crimson, front10 azure, front10 crimson, side10 azure, side10 crimson",
                      grid_rows=[p[0] for p in POSES], files=sorted(f for f in os.listdir(OUT) if f.endswith(".png")))
save()

# ================================================================================================ export + round trip
rest_pose()
for o in LODS:
    for m in list(o.modifiers):
        if m.type != "ARMATURE": o.modifiers.remove(m)
    o.data.materials.clear(); o.data.materials.append(bpy.data.materials.get(KEY) or bpy.data.materials.new(KEY))
for o in list(scene.objects):
    if o.type == "MESH" and o not in LODS: bpy.data.objects.remove(o, do_unlink=True)
bpy.data.objects.remove(crig, do_unlink=True)
for o in scene.objects: o.select_set(o is rig or o in LODS)
bpy.context.view_layer.objects.active = rig
path = os.path.join(OUT, KEY + ".fbx")
r = bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"}, global_scale=1.0,
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, use_mesh_modifiers=False, mesh_smooth_type="FACE",
                             use_tspace=False, colors_type="SRGB", prioritize_active_color=True, add_leaf_bones=False,
                             primary_bone_axis="Y", secondary_bone_axis="X", use_armature_deform_only=False,
                             armature_nodetype="NULL", bake_anim=False, path_mode="STRIP", embed_textures=False, use_custom_props=False)
REP["export"] = dict(path=path, result=list(r), bytes=os.path.getsize(path))
bpy.ops.wm.read_factory_settings(use_empty=True)
objs = imp_fbx(path, False)
rig2 = next(o for o in objs if o.type == "ARMATURE")
diff = max(float(np.abs(np.array(rig2.matrix_world @ b_.matrix_local) - np.array(REST[b_.name])).max()) for b_ in rig2.data.bones if b_.name in REST)
REP["round_trip"] = dict(armature=rig2.name, bones=len(rig2.data.bones), bones_missing=sorted(set(REST) - {b_.name for b_ in rig2.data.bones}),
                         max_bind_matrix_diff=round(diff, 6),
                         meshes={o.name: dict(tris=ntri(o), uv=len(o.data.uv_layers), colours=[a.name for a in o.data.color_attributes],
                                              groups=len(o.vertex_groups), parent=o.parent.name if o.parent else None) for o in objs if o.type == "MESH"})
log("round trip", REP["round_trip"])
import shutil
for src, dst in ((path, X(C["repo_fbx"])), (os.path.join(OUT, KEY + "_Base.png"), X(C["repo_tex"]))):
    if os.environ.get("BT_NO_REPO_COPY"): break           # method 2 copies by hand, only after the renders are clean
    os.makedirs(os.path.dirname(dst), exist_ok=True); shutil.copyfile(src, dst)
REP.pop("REST", None)
save()
print("[%s] SUMMARY %s" % (KEY, json.dumps(dict(tris=REP.get("tris"), uv=REP.get("uv"), stretch=REP.get("stretch_summary"),
                                                  welded_rest={k: v["any_welded"] for k, v in REP.get("joint_cut_rest", {}).items()},
                                                  round_trip=REP["round_trip"]["bones"]))), flush=True)
