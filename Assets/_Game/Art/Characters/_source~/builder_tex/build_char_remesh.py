"""Method 2 (batch 12 Builder pilot): a NEW clean surface instead of decimating the generated mesh.

  cm-heavy blender blender -b --factory-startup -P build_char_remesh.py -- <config.json> <repo>

The generated mesh (open edges, inside-out pieces, thousands of UV islands) is used only as the shape and colour
source. Shared steps come from build_char_tex.py (executed in this process): rig + clips, raw import, alignment to the
skeleton, A-pose fit, part split (pack, items, specks); and at the end its clip proof, toon renders, FBX export and
round trip. This file does the middle, all in the mesh's own A-pose:
  1. Regions on the raw: arm/leg shells after the joint cut (the shoulder/hip spheres are zones), head, torso, pack.
     Smallest arm-to-body and leg-to-leg gap measured; voxel = gap / 2.2 (clamped). A gap under 2 x voxel_min gets a
     separation slab (the body vertices are pushed off the limb to slab_m) BEFORE remeshing; reported.
  2. Body and pack voxel-remeshed separately -> watertight. Checks: open edges, non-manifold edges, signed volume,
     cavity shells removed, and the label test: no arm vertex shares an edge with (or lies within the gap of) torso,
     head or leg vertices; legs likewise (zones excepted).
  3. Collapse decimation of the clean surfaces: body (head protected by a vertex group) + pack = LOD0; LOD1 and
     Shadow decimated from LOD0 (LOD1 keeps the LOD0 UVs).
  4. UVs by region: seams on region borders plus one seam down the back of each region; angle-based unwrap; pack by
     Smart UV; head islands scaled; packed. Base colour baked raw -> LOD0 in Cycles, selected to active (cage, ray
     distance, 8 px margin) at 1024.
  5. Team mask by region: navy shirt in torso/sleeve regions + teal trim anywhere = team cloth (alpha 0.5); those
     texels are re-lit to the authored cloth colour times their own relative brightness, because CMToonSwap is
     target * max(rgb) / max(authored): a navy base would otherwise come out near-black in both teams.
  6. Weights: bone heat on LOD0 against a copy of the rig whose A-pose is its rest, then region limits, smoothing,
     max 4, normalised; LOD1/Shadow re-limited. Unskinned to the rest through the armature (the Warrior method).
  7. The mallet (WeaponProps: handle along handslot.r +Y, 0.62 m, gripped 0.12 m from its end) is parented for the
     renders only. Then build_char_tex.py's proof + export (BT_NO_REPO_COPY: nothing is copied into the repo here).
"""
import bpy, bmesh, sys, os, json, math, time
import numpy as np
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

_argv = sys.argv[sys.argv.index("--") + 1:]
_C = json.load(open(_argv[0]))
_C["out_dir"] = _C["out_dir_remesh"]
_out = os.path.expanduser(_C["out_dir"]); os.makedirs(_out, exist_ok=True)
_cfg = os.path.join(_out, "config_used.json"); json.dump(_C, open(_cfg, "w"), indent=1)
sys.argv[sys.argv.index("--") + 1] = _cfg
os.environ.pop("BT_FROM_BLEND", None); os.environ["BT_NO_REPO_COPY"] = "1"
P1 = os.path.join(os.path.dirname(os.path.abspath(__file__)), "build_char_tex.py")
_src = open(P1).read()
_m_lod0 = "    # " + "=" * 92 + " LOD0\n"
_m_proof = "# " + "=" * 96 + " clip proof\n"
assert _m_lod0 in _src and _m_proof in _src
exec(compile(_src[:_src.index(_m_lod0)], P1, "exec"), globals())          # rig, raw, align, A-pose, parts
RM = C["remesh"]
REG = ["torso", "head", "arm.l", "arm.r", "leg.l", "leg.r", "pack", "zarm.l", "zarm.r", "zleg.l", "zleg.r", "item"]
RI = {r: i for i, r in enumerate(REG)}
UVREG = {"torso": "torso", "zarm.l": "torso", "zarm.r": "torso", "zleg.l": "torso", "zleg.r": "torso"}


def faces_of(o):
    F = np.zeros(len(o.data.polygons) * 3, int); o.data.polygons.foreach_get("vertices", F); return F.reshape(-1, 3)


def sel_only(objs, active):
    for x in scene.objects: x.select_set(x in objs)
    bpy.context.view_layer.objects.active = active


# ================================================================================================ 1. regions on the raw
rep_c, cut = joint_cut(Vr, Er, JA, item | speck | pack, "raw regions (A-pose)")
Dr = bone_dists(Vr, JA); near_r = Dr.argmin(1)
L = np.full(len(Vr), RI["torso"])
for s in ("l", "r"):
    L[cut["rem"]["sh" + s]] = RI["zarm." + s]; L[cut["rem"]["hip" + s]] = RI["zleg." + s]
for s in ("l", "r"):
    for kind, chain in (("arm", ["upperarm", "lowerarm", "wrist", "hand"]), ("leg", ["upperleg", "lowerleg", "foot", "toes"])):
        ch = [BI[b + "." + s] for b in chain]
        reg = cut["keep"] & np.isin(cut["lab"], np.unique(cut["lab"][cut["probes"][kind + "." + s]]))
        reg &= Vr[:, 0] * np.sign(JA[chain[0] + "." + s][0][0]) > 0
        if rep_c[kind + "." + s]["welded"]: reg &= np.isin(near_r, ch)
        L[reg & ~item & ~pack] = RI[kind + "." + s]
# the hip spheres are wide (x1.75): shell that falls inside one but hangs well below the hip joint (the piece behind
# the left calf) belongs to that leg, not to the hip blend zone
for s in ("l", "r"):
    low_zone = (L == RI["zleg." + s]) & (Vr[:, 2] < JA["upperleg." + s][0][2] - RM["zone_leg_below_m"])
    L[low_zone] = RI["leg." + s]
    REP.setdefault("v2_zone_to_leg", {})[s] = int(low_zone.sum())
hz_ = JA["head"][0][2] - 0.04
L[(near_r == BI["head"]) & (Vr[:, 2] > hz_) & (L == RI["torso"])] = RI["head"]
for u in np.unique(lab[item]):                    # loose items: region of the part they hang on
    m = lab == u; c = Vr[m].mean(0)
    dd = {b: float(segdist(c[None], *JA[b])[0]) for b in DEFORM}; bb = min(dd, key=dd.get)
    if bb.split(".")[0] in ("upperarm", "lowerarm", "wrist", "hand") and dd[bb] < 0.15: L[m] = RI["arm." + bb.split(".")[1]]
    elif bb.split(".")[0] in ("lowerleg", "foot", "toes") and dd[bb] < 0.12: L[m] = RI["leg." + bb.split(".")[1]]
    elif bb == "head": L[m] = RI["head"]
    else: L[m] = RI["torso"]
L[pack] = RI["pack"]
IB = -np.ones(len(Vr), int)
rigid_item = np.zeros(len(Vr), bool)
REP["v2_rigid_items"] = []
for u in np.unique(lab[item]):
    m = (lab == u) & item & ~pack
    if not m.any() or L[m][0] in (RI["arm.l"], RI["arm.r"]): continue        # held / glove pieces stay with the hand
    c = Vr[m].mean(0); top = Vr[m, 2].max()
    if top > JA["hips"][0][2] - 0.25 and c[2] < JA["spine"][1][2]: bname = "hips"     # hangs from the belt
    else:
        dd = {b: float(segdist(c[None], *JA[b])[0]) for b in DEFORM}; bname = min(dd, key=dd.get)
    IB[m] = BI[bname]; rigid_item |= m; L[m] = RI["item"]
    REP["v2_rigid_items"].append(dict(verts=int(m.sum()), centroid=np.round(c, 3).tolist(), bone=bname))
# dangling body geometry: torso-labelled pieces of the main shell below the knee line (tools hung from the belt, the
# piece behind the left calf) are cut off too, rigid on the hips, so the leg never drags them
dang = (L == RI["torso"]) & ~pack & ~speck & (Vr[:, 2] < JA["hips"][0][2] - RM["dangle_below_hips_m"])
IB[dang] = BI["hips"]; rigid_item |= dang; L[dang] = RI["item"]
REP["v2_dangling_cut"] = dict(verts=int(dang.sum()), bbox=[Vr[dang].min(0).round(3).tolist(), Vr[dang].max(0).round(3).tolist()] if dang.any() else None)
log("v2 dangling cut", REP["v2_dangling_cut"])
log("v2 rigid items", len(REP["v2_rigid_items"]), json.dumps(REP["v2_rigid_items"])[:1500])
LIMB_OTHERS = {"arm.l": ["torso", "head", "leg.l", "leg.r", "arm.r", "zleg.l", "zleg.r"],
               "arm.r": ["torso", "head", "leg.l", "leg.r", "arm.l", "zleg.l", "zleg.r"],
               "leg.l": ["leg.r", "arm.l", "arm.r"], "leg.r": ["leg.l", "arm.l", "arm.r"]}


def gaps(V, Lx, E=None):
    out = {}
    for k, oth in LIMB_OTHERS.items():
        a = Lx == RI[k]; b = np.isin(Lx, [RI[o] for o in oth])
        if a.sum() == 0 or b.sum() == 0: out[k] = None; continue
        kd = KDTree(int(b.sum())); idx = np.nonzero(b)[0]
        for i, p in enumerate(V[b]): kd.insert(p, i)
        kd.balance()
        ai = np.nonzero(a)[0]; d = np.array([kd.find(V[i])[2] for i in ai])
        j = d.argmin()
        rec = dict(min_m=round(float(d.min()), 4), p01_m=round(float(np.percentile(d, 1)), 4), at=np.round(V[ai[j]], 3).tolist(),
                   other=REG[Lx[idx[kd.find(V[ai[j]])[1]]]])
        if E is not None:
            la, lb = Lx[E[:, 0]], Lx[E[:, 1]]
            ob = [RI[o] for o in oth]
            rec["edges_to_others"] = int((((la == RI[k]) & np.isin(lb, ob)) | ((lb == RI[k]) & np.isin(la, ob))).sum())
        out[k] = rec
    return out


body_raw = ~speck & ~pack & ~rigid_item
G0 = gaps(Vr[body_raw], L[body_raw])
gmin = min(v["min_m"] for v in G0.values() if v)
REP["v2_raw_gaps"] = G0
slab = {}
if gmin < 2 * RM["voxel_min"]:
    # separation slab: body (non-limb) raw vertices within slab_m of a limb are pushed off it, in place
    for k, oth in LIMB_OTHERS.items():
        a = (L == RI[k]) & body_raw; b = np.isin(L, [RI[o] for o in oth]) & body_raw
        kd = KDTree(int(a.sum())); ai = np.nonzero(a)[0]
        for i, p in enumerate(Vr[a]): kd.insert(p, i)
        kd.balance()
        moved = 0
        for i in np.nonzero(b)[0]:
            co, j, d = kd.find(Vr[i])
            if d < RM["slab_m"]:
                dirn = (Vr[i] - np.array(co)); dirn /= max(np.linalg.norm(dirn), 1e-9)
                Vr[i] = np.array(co) + dirn * RM["slab_m"]; moved += 1
        slab[k] = moved
    raw.data.vertices.foreach_set("co", Vr.astype(np.float32).ravel()); raw.data.update()
    G0 = gaps(Vr[body_raw], L[body_raw]); gmin = min(v["min_m"] for v in G0.values() if v)
VOX = float(np.clip(gmin / 2.2, RM["voxel_min"], RM["voxel_max"]))
REP["v2_slab_vertices_moved"] = slab; REP["v2_raw_gaps_after_slab"] = G0; REP["v2_voxel_m"] = VOX
log("v2 gaps", json.dumps(G0), "slab", slab, "voxel", VOX)
kdr = KDTree(len(Vr))
for i, p in enumerate(Vr): kdr.insert(p, i)
kdr.balance()


bodyonly = ~pack & ~rigid_item & ~speck
kdb = KDTree(int(bodyonly.sum())); _bi = np.nonzero(bodyonly)[0]
for i, p in enumerate(Vr[bodyonly]): kdb.insert(p, i)
kdb.balance()


def labels_of(o):
    """Nearest raw vertex's region, decided per connected piece: a piece that is mostly pack/item (it came from the pack
    or items object) is all pack/item; in any other piece a vertex that merely lies near a pack strap or a belt item
    takes the nearest BODY region instead."""
    Vx, Ex = arrays(o)
    Lx = np.array([L[kdr.find(p)[1]] for p in Vx])
    lc = components(len(Vx), Ex)
    for u in np.unique(lc):
        m = lc == u; rg = np.isin(Lx[m], [RI["pack"], RI["item"]])
        if rg.mean() >= 0.5:
            Lx[m] = RI["pack"] if (Lx[m] == RI["pack"]).sum() >= (Lx[m] == RI["item"]).sum() else RI["item"]
        else:
            for i in np.nonzero(m)[0]:
                if Lx[i] in (RI["pack"], RI["item"]): Lx[i] = L[_bi[kdb.find(Vx[i])[1]]]
    return Lx


# ================================================================================================ 2. remesh
def part_copy(name, keep_mask):
    o = raw.copy(); o.data = raw.data.copy(); scene.collection.objects.link(o); o.name = o.data.name = name
    for a in [a.name for a in o.data.color_attributes]: o.data.color_attributes.remove(o.data.color_attributes[a])
    while o.data.uv_layers: o.data.uv_layers.remove(o.data.uv_layers[0])
    o.data.materials.clear()
    bm = bmesh.new(); bm.from_mesh(o.data); bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.verts[i] for i in np.nonzero(~keep_mask)[0]], context="VERTS")
    bm.to_mesh(o.data); bm.free(); return o


def remesh(o, vs, close_r=0.0):
    def vox():
        m = o.modifiers.new("R", "REMESH"); m.mode = "VOXEL"; m.voxel_size = vs; m.adaptivity = 0.0; m.use_smooth_shade = False
        apply_mod(o, "R")
        bm_ = bmesh.new(); bm_.from_mesh(o.data); bmesh.ops.recalc_face_normals(bm_, faces=bm_.faces); bm_.to_mesh(o.data); bm_.free()
    vox()
    if close_r > 0:
        # morphological closing: perforations and tunnels left by thin cloth layers (each one a handle the collapse
        # cannot remove, and a bone-heat failure) are filled by inflating close_r, remeshing, deflating close_r
        for sgn in (1.0, -1.0):
            d = o.modifiers.new("D", "DISPLACE"); d.direction = "NORMAL"; d.mid_level = 0.0; d.strength = sgn * close_r
            apply_mod(o, "D"); vox()
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(o.data); bm.free()
    # cavity shells (closed surfaces inside the main one) are deleted
    V, E = arrays(o); lb = components(len(V), E); u, c = np.unique(lb, return_counts=True); mn = u[c.argmax()]
    F = faces_of(o); bvh = BVHTree.FromPolygons(V.tolist(), F[lb[F[:, 0]] == mn].tolist())
    inside = []
    for uu in u:
        if uu == mn: continue
        p = Vector(V[np.nonzero(lb == uu)[0][0]]); hits = 0; q = p
        for _ in range(64):
            r = bvh.ray_cast(q + Vector((0, 0, 1e-5)), Vector((0, 0, 1)))
            if r[0] is None: break
            hits += 1; q = r[0]
        if hits % 2 == 1: inside.append(uu)
    if inside:
        bm = bmesh.new(); bm.from_mesh(o.data); bm.verts.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[bm.verts[i] for i in np.nonzero(np.isin(lb, inside))[0]], context="VERTS")
        bm.to_mesh(o.data); bm.free()
    return dict(cavity_shells_removed=len(inside))


def surface_check(o):
    bm = bmesh.new(); bm.from_mesh(o.data)
    op = sum(1 for e in bm.edges if len(e.link_faces) == 1); nm = sum(1 for e in bm.edges if len(e.link_faces) > 2)
    nmv = sum(1 for v in bm.verts if not v.is_manifold)
    flips = 0
    for e in bm.edges:                                       # consistent winding: the shared edge runs opposite ways
        if len(e.link_faces) == 2:
            f1, f2 = e.link_faces
            l1 = [l for l in f1.loops if l.edge is e][0]; l2 = [l for l in f2.loops if l.edge is e][0]
            if l1.vert is l2.vert: flips += 1
    vol = bm.calc_volume(signed=True); bm.free()
    V, E = arrays(o); ncomp = int(len(np.unique(components(len(V), E))))
    genus = int(round(ncomp - (len(V) - len(E) + ntri(o)) / 2.0))
    return dict(genus_total=genus, tris=ntri(o), verts=len(V), open_edges=op, nonmanifold_edges=nm, nonmanifold_verts=nmv, winding_flips=flips,
                signed_volume_m3=round(vol, 4), components=int(len(np.unique(components(len(V), E)))))


body = part_copy("Body", body_raw); pk = part_copy("PackPart", pack)
CR = RM["close_r"]; VOXC = float(np.clip((gmin - 2 * CR) / 2.4, RM["voxel_min"], VOX))
REP["v2_close"] = dict(close_r=CR, voxel_m=VOXC)
REP["v2_remesh"] = {"body": remesh(body, VOXC, CR), "pack": remesh(pk, RM["pack_voxel"], RM["pack_close_r"])}
REP["v2_remesh"]["body"].update(surface_check(body)); REP["v2_remesh"]["pack"].update(surface_check(pk))
itm = part_copy("Items", rigid_item)
REP["v2_remesh"]["items"] = remesh(itm, RM["item_voxel"], RM["item_close_r"]); REP["v2_remesh"]["items"].update(surface_check(itm))
Vb, Eb = arrays(body); Lb = labels_of(body)
REP["v2_remesh_gaps"] = gaps(Vb, Lb, Eb)
log("v2 remesh", json.dumps(REP["v2_remesh"]), "gaps", json.dumps(REP["v2_remesh_gaps"]))
save()


# ================================================================================================ 3. decimate
def decimate_to(o, target, vg=None):
    hist = []
    for _ in range(10):
        t0 = ntri(o)
        if t0 <= target * 1.03: break
        d = o.modifiers.new("Dec", "DECIMATE"); d.decimate_type = "COLLAPSE"; d.use_collapse_triangulate = True
        d.ratio = max(0.005, min(1.0, target / t0 * (0.97 if vg else 1.0)))
        if vg: d.vertex_group = vg; d.vertex_group_factor = 1.0
        apply_mod(o, "Dec"); hist.append(ntri(o))
        if ntri(o) >= t0 * 0.995: break
    return hist


Lb = labels_of(body)
dg_ = body.vertex_groups.new(name="dense")
dg_.add(list(range(len(Lb))), 1.0, "REPLACE")
dg_.add(np.nonzero(Lb == RI["head"])[0].tolist(), RM["head_decimate_weight"], "REPLACE")
REP["v2_decimate"] = {"body": decimate_to(body, RM["body_lod0_tris"], "dense"), "pack": decimate_to(pk, RM["pack_lod0_tris"]),
                     "items": decimate_to(itm, RM["items_lod0_tris"])}
body.vertex_groups.clear()
Lb = labels_of(body); Vb_, _ = arrays(body)
headish = (Lb == RI["head"]) | (np.linalg.norm(Vb_ - JA["head"][0], axis=1) < RM["neck_band_m"])
bm = bmesh.new(); bm.from_mesh(body.data); bm.verts.ensure_lookup_table(); bm.faces.ensure_lookup_table()
hv_ = [bm.verts[i] for i in np.nonzero(headish)[0]]
for _ in range(RM["head_smooth_iters"]):
    bmesh.ops.smooth_vert(bm, verts=hv_, factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
hf_ = [f for f in bm.faces if all(headish[v.index] for v in f.verts)]
he_ = list({e for f in hf_ for e in f.edges if all(headish[v.index] for v in e.verts)})
bmesh.ops.beautify_fill(bm, faces=hf_, edges=he_)
bm.to_mesh(body.data); bm.free()
REP["v2_head_cleanup"] = dict(verts=int(headish.sum()), faces=len(hf_), smooth_iters=RM["head_smooth_iters"])
REP["v2_lod0_parts"] = {"body": surface_check(body), "pack": surface_check(pk), "items": surface_check(itm)}
Lb = labels_of(body); Fb = faces_of(body)
REP["v2_lod0_head_tris"] = int((np.array([np.bincount(Lb[f], minlength=len(REG)).argmax() for f in Fb]) == RI["head"]).sum())
log("v2 LOD0 parts", json.dumps(REP["v2_lod0_parts"]), "head tris", REP["v2_lod0_head_tris"])


# ================================================================================================ 4. UVs by region + bake
def region_axis(r):
    if r in ("arm.l", "arm.r"):
        s = r[-1]; return JA["upperarm." + s][0], JA["hand." + s][1]
    if r in ("leg.l", "leg.r"):
        s = r[-1]; return JA["upperleg." + s][0], JA["foot." + s][1]
    if r == "head": return JA["head"][0], JA["head"][1]
    return JA["hips"][0], JA["head"][0]


def unwrap_regions(o):
    Vx, _ = arrays(o); Lx = labels_of(o)
    bm = bmesh.new(); bm.from_mesh(o.data); bm.faces.ensure_lookup_table()
    fr = []
    for f in bm.faces:
        r = REG[np.bincount([Lx[v.index] for v in f.verts], minlength=len(REG)).argmax()]
        fr.append(UVREG.get(r, r))
    fr = np.array(fr, dtype=object)
    nbr = [[l.link_loop_radial_next.face.index for l in f.loops if l.link_loop_radial_next.face is not f] for f in bm.faces]
    for _ in range(RM["region_smooth_iters"]):
        new = fr.copy()
        for i, nb in enumerate(nbr):
            vals = [fr[j] for j in nb] + [fr[i]]
            best = max(set(vals), key=vals.count)
            if vals.count(best) >= 3: new[i] = best
        fr = new
    fr = list(fr)
    fth = np.zeros(len(bm.faces))
    for i, f in enumerate(bm.faces):
        a, b = region_axis(fr[i]); ax = (b - a) / max(np.linalg.norm(b - a), 1e-9)
        c = np.array(f.calc_center_median()); d = c - a; d -= ax * (d @ ax)
        w = np.array([0, 1.0, 0]) - ax * ax[1]; w /= max(np.linalg.norm(w), 1e-9); u = np.cross(ax, w)
        fth[i] = math.atan2(d @ u, d @ w)                    # 0 = straight behind the axis
    nseam = 0
    for e in bm.edges:
        e.seam = False
        if len(e.link_faces) != 2: continue
        f1, f2 = e.link_faces
        if fr[f1.index] != fr[f2.index]: e.seam = True
        elif abs(fth[f1.index]) < math.pi / 2 and abs(fth[f2.index]) < math.pi / 2 and fth[f1.index] * fth[f2.index] < 0: e.seam = True
        nseam += e.seam
    bm.to_mesh(o.data); bm.free()
    o.data.uv_layers.new(name="UVMap")
    sel_only([o], o)
    bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.unwrap(method="ANGLE_BASED", fill_holes=True, correct_aspect=True, margin=RM["uv_margin"])
    bpy.ops.object.mode_set(mode="OBJECT")
    return np.array(fr), nseam


fr_body, nseam = unwrap_regions(body)
for o_ in (pk, itm):                   # rigid pieces: few big islands (the chair's slats merged by the wide angle)
    o_.data.uv_layers.new(name="UVMap"); sel_only([o_], o_)
    bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(RM["rigid_uv_angle"]), island_margin=RM["uv_margin"], area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")
with bpy.context.temp_override(active_object=body, object=body, selected_objects=[body, pk, itm], selected_editable_objects=[body, pk, itm]):
    bpy.ops.object.join()
low = body; low.name = low.data.name = KEY + "_LOD0"
_Ll = labels_of(low)
fr_all = np.array([UVREG.get(REG[np.bincount(_Ll[list(p_.vertices)], minlength=len(REG)).argmax()], REG[np.bincount(_Ll[list(p_.vertices)], minlength=len(REG)).argmax()]) for p_ in low.data.polygons])
fr_all = np.where(fr_all == "item", "pack", fr_all)     # rigid pieces share the pack's UV/mask class (fixed colour)
sel_only([low], low)
bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT"); bpy.ops.uv.select_all(action="SELECT")
bpy.ops.uv.average_islands_scale()
bpy.ops.object.mode_set(mode="OBJECT")
# head islands scaled up (texel density), then packed
uvl = np.zeros(len(low.data.loops) * 2); low.data.uv_layers[0].data.foreach_get("uv", uvl); uvl = uvl.reshape(-1, 3, 2)
hm = fr_all == "head"
if hm.any():
    cen = uvl[hm].reshape(-1, 2).mean(0); uvl[hm] = cen + (uvl[hm] - cen) * C["head_uv_scale"]
low.data.uv_layers[0].data.foreach_set("uv", uvl.ravel())
bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT"); bpy.ops.uv.select_all(action="SELECT")
try:
    bpy.ops.uv.pack_islands(rotate=True, rotate_method="ANY", margin=RM["uv_margin"], margin_method="FRACTION", shape_method="CONCAVE")
except TypeError:
    bpy.ops.uv.pack_islands(rotate=True, margin=RM["uv_margin"], margin_method="FRACTION", shape_method="CONCAVE")
bpy.ops.object.mode_set(mode="OBJECT")
uvl = np.zeros(len(low.data.loops) * 2); low.data.uv_layers[0].data.foreach_get("uv", uvl); uvl = uvl.reshape(-1, 3, 2)
F0 = faces_of(low); V0, E0 = arrays(low)
a_uv = np.abs((uvl[:, 1, 0] - uvl[:, 0, 0]) * (uvl[:, 2, 1] - uvl[:, 0, 1]) - (uvl[:, 1, 1] - uvl[:, 0, 1]) * (uvl[:, 2, 0] - uvl[:, 0, 0])) / 2
a_3d = np.linalg.norm(np.cross(V0[F0[:, 1]] - V0[F0[:, 0]], V0[F0[:, 2]] - V0[F0[:, 0]]), axis=1) / 2
bm = bmesh.new(); bm.from_mesh(low.data); ul_ = bm.loops.layers.uv.active; bm.faces.ensure_lookup_table()
par = list(range(len(bm.faces)))


def fnd(i):
    while par[i] != i: par[i] = par[par[i]]; i = par[i]
    return i


for e in bm.edges:
    if len(e.link_faces) != 2: continue
    f1, f2 = e.link_faces
    l1 = [l for l in f1.loops if l.edge is e][0]; l2 = [l for l in f2.loops if l.edge is e][0]
    if (l1[ul_].uv - l2.link_loop_next[ul_].uv).length < 1e-6 and (l1.link_loop_next[ul_].uv - l2[ul_].uv).length < 1e-6:
        par[fnd(f1.index)] = fnd(f2.index)
n_isl = len({fnd(i) for i in range(len(bm.faces))}); bm.free()
REP["uv"] = dict(coverage=round(float(a_uv.sum()), 4), islands=n_isl, seams=nseam,
                 head_texel_density_vs_body=round(float((a_uv[hm].sum() / a_3d[hm].sum()) / (a_uv[~hm].sum() / a_3d[~hm].sum())), 3) if hm.any() else None,
                 uv_min=uvl.reshape(-1, 2).min(0).round(4).tolist(), uv_max=uvl.reshape(-1, 2).max(0).round(4).tolist())
log("v2 UV", REP["uv"])

TEX = C["tex"]
img = bpy.data.images.new(KEY + "_bake", TEX, TEX, alpha=True, float_buffer=False); img.colorspace_settings.name = "sRGB"
mat = bpy.data.materials.new("bake_target"); mat.use_nodes = True
tn = mat.node_tree.nodes.new("ShaderNodeTexImage"); tn.image = img; mat.node_tree.nodes.active = tn
low.data.materials.clear(); low.data.materials.append(mat)
scene.render.engine = "CYCLES"; scene.cycles.device = "CPU"; scene.cycles.samples = RM["bake_samples"]
raw.hide_render = False; raw.hide_set(False)
with bpy.context.temp_override(active_object=low, object=low, selected_objects=[low, raw], selected_editable_objects=[low, raw]):
    r = bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"}, use_selected_to_active=True, cage_extrusion=RM["cage_extrusion"],
                            max_ray_distance=RM["max_ray_m"], margin=RM["bake_margin_px"], margin_type="EXTEND",
                            target="IMAGE_TEXTURES", use_clear=True)
BAKE = np.array(img.pixels[:], np.float32).reshape(TEX, TEX, 4)[:, :, :3].copy()
REP["bake"] = dict(result=list(r), engine="CYCLES selected-to-active", cage_extrusion_m=RM["cage_extrusion"], max_ray_m=RM["max_ray_m"],
                   margin_px=RM["bake_margin_px"])


# region raster (UV space), dilated into the bake margin
def raster_regions(o, fr):
    reg = -np.ones((TEX, TEX), int); zm = np.zeros((TEX, TEX)); Vq, _ = arrays(o); Fq = faces_of(o)
    u = np.zeros(len(o.data.loops) * 2); o.data.uv_layers[0].data.foreach_get("uv", u); u = u.reshape(-1, 3, 2)
    for t in range(len(u)):
        P = u[t] * TEX - 0.5
        x0, y0 = np.floor(P.min(0)).astype(int); x1, y1 = np.ceil(P.max(0)).astype(int)
        x0, y0, x1, y1 = max(x0, 0), max(y0, 0), min(x1, TEX - 1), min(y1, TEX - 1)
        if x1 < x0 or y1 < y0: continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1)); a, b, c = P
        den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(den) < 1e-12: continue
        w0 = ((b[1] - c[1]) * (gx - c[0]) + (c[0] - b[0]) * (gy - c[1])) / den
        w1 = ((c[1] - a[1]) * (gx - c[0]) + (a[0] - c[0]) * (gy - c[1])) / den
        ins = (w0 >= -0.01) & (w1 >= -0.01) & (1 - w0 - w1 >= -0.01)
        reg[gy[ins], gx[ins]] = fr[t]
        zz = Vq[Fq[t], 2]; zm[gy[ins], gx[ins]] = w0[ins] * zz[0] + w1[ins] * zz[1] + (1 - w0[ins] - w1[ins]) * zz[2]
    return reg, zm


RN = ["torso", "head", "arm.l", "arm.r", "leg.l", "leg.r", "pack"]
regmap, zmap = raster_regions(low, np.array([RN.index(x) for x in fr_all]))
covered = regmap >= 0


def shift(a, dy, dx): return np.roll(np.roll(a, dy, 0), dx, 1)
def erode(m): return m & shift(m, 1, 0) & shift(m, -1, 0) & shift(m, 0, 1) & shift(m, 0, -1)
def dilate(m): return m | shift(m, 1, 0) | shift(m, -1, 0) | shift(m, 0, 1) | shift(m, 0, -1)


rm2 = regmap.copy()
for _ in range(RM["bake_margin_px"]):
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        s_ = shift(rm2, dy, dx); rm2 = np.where((rm2 < 0) & (s_ >= 0), s_, rm2)

# ================================================================================================ 5. team mask by region
rr, gg, bb_ = BAKE[:, :, 0], BAKE[:, :, 1], BAKE[:, :, 2]
mx = BAKE.max(2); mn = BAKE.min(2); sat = (mx - mn) / np.maximum(mx, 1e-6); dd = np.maximum(mx - mn, 1e-6)
hue = np.where(mx == rr, (gg - bb_) / dd % 6, np.where(mx == gg, (bb_ - rr) / dd + 2, (rr - gg) / dd + 4)) * 60
tc = C["team"]
navy = (bb_ > rr + 0.04) & (bb_ >= gg) & (mx < 0.55) & (mx > 0.04)
SH = [RN.index(UVREG.get(x, x)) for x in RM["shirt_regions"]]
shirt_reg = np.isin(regmap, SH)
teal = (hue >= tc["hue_deg"][0]) & (hue <= tc["hue_deg"][1]) & (sat >= tc["sat_min"]) & (mx >= tc["val_min"])
Vh_, _ = arrays(low); head_top = float(Vh_[labels_of(low) == RI["head"], 2].max())
capband = (regmap == RN.index("head")) & teal & (zmap > head_top - RM["cap_band_depth_m"])
team = ((navy | teal) & shirt_reg) | capband
for _ in range(RM["mask_open_px"]): team = erode(team)
for _ in range(RM["mask_open_px"]): team = dilate(team) & covered & (shirt_reg | capband | (team & (regmap == RN.index("head"))))
team = erode(dilate(team)) & covered
pix = np.argwhere(team); idx = -np.ones(team.shape, int); idx[team] = np.arange(len(pix))
Ep = []
for dy, dx in ((0, 1), (1, 0)):
    nb = team & shift(team, -dy, -dx); Ep.append(np.stack([idx[nb], shift(idx, -dy, -dx)[nb]], 1))
Ep = np.concatenate(Ep) if len(pix) else np.zeros((0, 2), int)
pl_ = components(len(pix), Ep); u_, cn_ = np.unique(pl_, return_counts=True)
team[tuple(pix[np.isin(pl_, u_[cn_ < RM["mask_min_blob_px"]])].T)] = False
core = team.copy()
for _ in range(RM["bake_margin_px"]):         # into this island's own bake margin only (uncovered texels)
    team = team | (dilate(team) & ~covered & np.isin(rm2, SH + [RN.index("head")]))
# value of the current Builder's team cloth (its code-8 vertex colours): CMToonSwap keeps hue/saturation of the target
# and scales by max(rgb)/max(authored), so matching that ratio matches the colour
oc = old0.data.color_attributes.get("Col"); ocs = np.zeros(len(oc.data) * 4); oc.data.foreach_get("color_srgb", ocs); ocs = ocs.reshape(-1, 4)
ok8 = np.round(ocs[:, 3] * 8).astype(int) == 8
k_old = float(np.median(ocs[ok8, :3].max(1) / hexrgb(tc["cloth_authored"]).max())) if ok8.any() else 1.0
auth = hexrgb(tc["cloth_authored"])
v = mx; vmed = float(np.median(v[core])) if core.any() else 1.0
kk = np.clip(v / max(vmed, 1e-4), *RM["lighten_clip"]) * k_old
base = BAKE.copy(); base[team] = auth[None, :] * kk[team][:, None]
alpha = np.where(team, 0.5, 0.0).astype(np.float32)
img4 = np.concatenate([base, alpha[:, :, None]], 2)
write_png(os.path.join(OUT, KEY + "_Base.png"), img4)
write_png(os.path.join(OUT, KEY + "_bake_raw.png"), BAKE)
over = np.where(team[:, :, None], np.array([1.0, 0.0, 1.0]), BAKE)
write_png(os.path.join(OUT, KEY + "_mask.png"), np.concatenate([np.repeat(alpha[:, :, None] * 2, 3, 2), over], 1))
for t_ in ("azure", "crimson"):
    pv = base.copy(); pv[team] = np.clip(hexrgb(tc[t_])[None, :] * (base[team].max(1, keepdims=True) / auth.max()), 0, 1)
    write_png(os.path.join(OUT, KEY + "_preview_%s.png" % t_), pv)
def to_hex(c): return "#%02X%02X%02X" % tuple(int(round(x * 255)) for x in np.clip(c, 0, 1))
REP["team_colour_match"] = {t_: dict(current_builder=to_hex(hexrgb(tc[t_]) * k_old), new_median=to_hex(hexrgb(tc[t_]) * float(np.median(kk[core]))),
                                     k_current=round(k_old, 3), k_new_median=round(float(np.median(kk[core])), 3)) for t_ in ("azure", "crimson")}
log("v2 team colour", REP["team_colour_match"], "cap band texels", int(capband.sum()), "head_top", round(head_top, 3))
REP["team_mask"] = dict(method="region: navy/teal in shirt+sleeves, teal cap band; opened, no pack", team_fraction_of_covered=round(float(team[covered].mean()), 4),
                        shirt_median_value_srgb=round(vmed, 3), texels_covered=round(float(covered.mean()), 4),
                        by_region={RN[i]: round(float(team[regmap == i].mean()), 3) for i in range(len(RN)) if (regmap == i).any()})
log("v2 mask", REP["team_mask"])
save()

# ================================================================================================ 6. weights (bone heat in the A-pose)
rigA = rig.copy(); rigA.data = rig.data.copy(); scene.collection.objects.link(rigA); rigA.name = "RigA"
sel_only([rigA], rigA)
bpy.ops.object.mode_set(mode="POSE"); bpy.ops.pose.armature_apply(selected=False); bpy.ops.object.mode_set(mode="OBJECT")
for bn in rigA.data.bones: bn.use_deform = bn.name in BI
TORSO4 = [BI[b] for b in ("hips", "spine", "chest", "head")]


def allowed_for(Lx, Vx):
    n = len(Lx); al = np.zeros((n, len(DEFORM)), bool); fac = np.ones((n, len(DEFORM)))
    t = Lx == RI["torso"]; al[np.ix_(np.nonzero(t)[0], TORSO4)] = True
    al[t & (Vx[:, 2] < JA["head"][0][2] - RM["head_bone_reach_m"]), BI["head"]] = False   # heat leaks the head bone down the belly
    below = t & (Vx[:, 2] < JA["hips"][0][2])
    for s in ("l", "r"):
        al[below, BI["upperleg." + s]] = True; fac[below, BI["upperleg." + s]] = RM["apron_leg_factor"]
    h = np.nonzero(Lx == RI["head"])[0]; al[np.ix_(h, [BI["head"], BI["chest"]])] = True
    for s in ("l", "r"):
        for r_, bones in (("arm." + s, ["upperarm", "lowerarm", "wrist", "hand"]), ("leg." + s, ["upperleg", "lowerleg", "foot", "toes"])):
            ii = np.nonzero(Lx == RI[r_])[0]; al[np.ix_(ii, [BI[b + "." + s] for b in bones])] = True
        ii = np.nonzero(Lx == RI["zarm." + s])[0]; al[np.ix_(ii, [BI["chest"], BI["spine"], BI["upperarm." + s]])] = True
        ii = np.nonzero(Lx == RI["zleg." + s])[0]; al[np.ix_(ii, [BI["hips"], BI["upperleg." + s]])] = True
    p = np.nonzero(Lx == RI["pack"])[0]; al[p] = False; al[p, BI["chest"]] = True
    it = Lx == RI["item"]
    if it.any():
        ib = IBx_[0]; lc_ = components(len(Vx), Ex_[0])
        for u in np.unique(lc_[it]):
            m = it & (lc_ == u); bb = np.bincount(ib[m][ib[m] >= 0], minlength=len(DEFORM)).argmax() if (ib[m] >= 0).any() else BI["hips"]
            al[m] = False; al[m, bb] = True
    return al, fac


def top4(Wm):
    o_ = np.argsort(-Wm, 1)[:, 4:]; np.put_along_axis(Wm, o_, 0, 1)
    return Wm / np.maximum(Wm.sum(1, keepdims=True), 1e-12)


def read_groups(o):
    W = np.zeros((len(o.data.vertices), len(DEFORM))); gi = {g.index: g.name for g in o.vertex_groups}
    for vv in o.data.vertices:
        for g in vv.groups:
            nm_ = gi[g.group]
            if nm_ in BI: W[vv.index, BI[nm_]] = g.weight
    return W


IBx_, Ex_ = [None], [None]


def finalize(o, W, tag):
    Vx, Ex = arrays(o); Lx = labels_of(o); n = len(Vx)
    IBx_[0] = np.array([IB[kdr.find(p_)[1]] for p_ in Vx]); Ex_[0] = Ex
    al, fac = allowed_for(Lx, Vx)
    rigid = (Lx == RI["item"]) | (Lx == RI["pack"])
    heat_unweighted = int((W.sum(1) <= 1e-6).sum())
    W = W * al * fac
    W[rigid] = al[rigid].astype(float)
    D = bone_dists(Vx, JA)
    fb = W.sum(1) <= 1e-6
    W[fb] = (al * fac / np.maximum(D, 0.01) ** C["weights"]["power"])[fb]
    a_, b_ = Ex[:, 0], Ex[:, 1]

    def hop(src, within):
        d = np.full(n, np.inf); d[src] = 0; fr_ = src.copy(); k = 0
        while fr_.any():
            k += 1; nb = np.zeros(n, bool); nb[b_[fr_[a_]]] = True; nb[a_[fr_[b_]]] = True
            nb &= within & np.isinf(d); d[nb] = k; fr_ = nb
        return d
    zb = {}
    for s in ("l", "r"):
        for zone, limb, roots, lb in (("zarm." + s, "arm." + s, ["chest", "spine"], "upperarm." + s), ("zleg." + s, "leg." + s, ["hips"], "upperleg." + s)):
            z = Lx == RI[zone]
            if not z.any(): continue
            lm = Lx == RI[limb]; tor = ~z & ~lm & (Lx != RI["pack"]) & (Lx != RI["item"])
            da = hop(lm, z | lm)[z]; dt = hop(tor, z | tor)[z]
            t = np.where(np.isinf(da), 0.0, np.where(np.isinf(dt), 1.0, dt / np.maximum(da + dt, 1e-9)))
            t = t * t * (3 - 2 * t)
            ri = [BI[r_] for r_ in roots]; zi = np.nonzero(z)[0]
            rw = W[np.ix_(zi, ri)]; rw = np.where(rw.sum(1, keepdims=True) > 0, rw / np.maximum(rw.sum(1, keepdims=True), 1e-12), 1.0 / len(ri))
            W[zi] = 0; W[np.ix_(zi, ri)] = (1 - t)[:, None] * rw; W[zi, BI[lb]] += t
            zb[zone] = int(z.sum())
    W = top4(W)
    deg = np.zeros(n); np.add.at(deg, Ex[:, 0], 1); np.add.at(deg, Ex[:, 1], 1)
    # allowed sets grown along existing edges (rings): where two regions share an edge (zone borders, the apron fused
    # to the thigh) smoothing can blend across instead of leaving a weight cliff. Arms never share an edge with the
    # torso (label test), so nothing leaks between them.
    al_s = al.copy()
    for _ in range(RM["allowed_rings"]):
        g_ = al_s.copy(); g_[Ex[:, 0]] |= al_s[Ex[:, 1]]; g_[Ex[:, 1]] |= al_s[Ex[:, 0]]
        g_[rigid] = al[rigid]; al_s = g_
    for _ in range(RM["smooth_iters"]):
        acc = np.zeros_like(W); np.add.at(acc, Ex[:, 0], W[Ex[:, 1]]); np.add.at(acc, Ex[:, 1], W[Ex[:, 0]])
        W = np.where(al_s, 0.5 * W + 0.5 * acc / np.maximum(deg, 1)[:, None], 0); W = top4(W)
    W[W < 1e-3] = 0; W = W / np.maximum(W.sum(1, keepdims=True), 1e-12)
    o.vertex_groups.clear(); vgs = [o.vertex_groups.new(name=b) for b in DEFORM]
    for vi, bi in zip(*np.nonzero(W > 0)): vgs[bi].add([int(vi)], float(W[vi, bi]), "REPLACE")
    ninf = (W > 0).sum(1)
    wr = dict(max_influences=int(ninf.max()), mean_influences=round(float(ninf.mean()), 2), unweighted=int((ninf == 0).sum()),
              heat_unweighted=heat_unweighted, fallback_verts=int(fb.sum()), sum_err=float(np.abs(W.sum(1) - 1).max()),
              regions={REG[i]: int((Lx == i).sum()) for i in range(len(REG))})
    log("v2 weights", tag, wr)
    return wr


V0, E0 = arrays(low); lc0 = components(len(V0), E0); u0, c0 = np.unique(lc0, return_counts=True); keep0 = lc0 == u0[c0.argmax()]
hb = low.copy(); hb.data = low.data.copy(); scene.collection.objects.link(hb); hb.vertex_groups.clear()
bm = bmesh.new(); bm.from_mesh(hb.data); bm.verts.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[bm.verts[i] for i in np.nonzero(~keep0)[0]], context="VERTS"); bm.to_mesh(hb.data); bm.free()
sel_only([hb, rigA], rigA)
with bpy.context.temp_override(active_object=rigA, object=rigA, selected_objects=[hb, rigA], selected_editable_objects=[hb, rigA]):
    rh = bpy.ops.object.parent_set(type="ARMATURE_AUTO")
Wh = np.zeros((len(V0), len(DEFORM))); Wh[np.nonzero(keep0)[0]] = read_groups(hb)
REP["v2_heat"] = dict(verts=int(keep0.sum()), weighted=int((Wh[keep0].sum(1) > 1e-6).sum()))
log("v2 heat", REP["v2_heat"])
bpy.data.objects.remove(hb, do_unlink=True)
REP["weights"] = {"bone_heat_result": list(rh), "LOD0": finalize(low, Wh, "LOD0")}

lod1 = low.copy(); lod1.data = low.data.copy(); scene.collection.objects.link(lod1); lod1.name = lod1.data.name = KEY + "_LOD1"
REP["v2_decimate"]["lod1"] = decimate_to(lod1, RM["lod1_tris"])
shd = lod1.copy(); shd.data = lod1.data.copy(); scene.collection.objects.link(shd); shd.name = shd.data.name = KEY + "_Shadow"
shd.vertex_groups.clear()
while shd.data.uv_layers: shd.data.uv_layers.remove(shd.data.uv_layers[0])
mr = shd.modifiers.new("R", "REMESH"); mr.mode = "VOXEL"; mr.voxel_size = RM["shadow_voxel"]; mr.adaptivity = 0.0; apply_mod(shd, "R")
REP["v2_decimate"]["shadow"] = decimate_to(shd, RM["shadow_tris"])
shd.data.uv_layers.new(name="UVMap")
REP["weights"]["LOD1"] = finalize(lod1, read_groups(lod1), lod1.name)
V1, _ = arrays(lod1); W1 = read_groups(lod1); kd1 = KDTree(len(V1))
for i, p in enumerate(V1): kd1.insert(p, i)
kd1.balance()
Vs_, _ = arrays(shd)
REP["weights"]["Shadow"] = finalize(shd, np.array([W1[kd1.find(p)[1]] for p in Vs_]), shd.name)
LODS = [low, lod1, shd]
REP["tris"] = {o.name: ntri(o) for o in LODS}
REP["v2_lod_surfaces"] = {o.name: surface_check(o) for o in LODS}
REP["v2_gaps_apose"] = {o.name: gaps(*arrays(o)[:1], labels_of(o), arrays(o)[1]) for o in LODS}
REP["lod0_head_tris"] = REP["v2_lod0_head_tris"]
log("v2 tris", REP["tris"], json.dumps(REP["v2_gaps_apose"]))
LAB_REST = {o.name: labels_of(o) for o in LODS}
bpy.data.objects.remove(rigA, do_unlink=True)

# ================================================================================================ unskin to the rest
SK = {pb.name: A_ @ pb.matrix @ pb.bone.matrix_local.inverted() @ A_.inverted() for pb in rig.pose.bones}
REP["unskin_max_move_m"] = {}
for o in LODS:
    gname = {g.index: g.name for g in o.vertex_groups}; mv = 0.0
    for vv in o.data.vertices:
        M = Matrix.Diagonal((0.0, 0.0, 0.0, 0.0)); tot = 0.0
        for g_ in vv.groups:
            if g_.weight > 0: M = M + SK[gname[g_.group]] * g_.weight; tot += g_.weight
        if tot <= 0: continue
        rp = (M * (1.0 / tot)).inverted_safe() @ vv.co; mv = max(mv, (rp - vv.co).length); vv.co = rp
    o.data.update(); REP["unskin_max_move_m"][o.name] = round(mv, 4)
for pb in rig.pose.bones: pb.matrix_basis = Matrix.Identity(4)
bpy.context.view_layer.update()
JR = joints(rig)
for o in LODS:
    o.parent = rig; o.matrix_parent_inverse = rig.matrix_world.inverted()
    am = o.modifiers.new("Armature", "ARMATURE"); am.object = rig
    for p in o.data.polygons: p.use_smooth = True
    o.data.set_sharp_from_angle(angle=math.radians(60.0))
    lh = LAB_REST[o.name] == RI["head"]; ev = np.zeros(len(o.data.edges) * 2, int); o.data.edges.foreach_get("vertices", ev); ev = ev.reshape(-1, 2)
    sa = o.data.attributes.get("sharp_edge")
    if sa is not None:
        sv = np.zeros(len(o.data.edges), bool); sa.data.foreach_get("value", sv); sv[lh[ev[:, 0]] & lh[ev[:, 1]]] = False; sa.data.foreach_set("value", sv)
    for a in [a.name for a in o.data.color_attributes]: o.data.color_attributes.remove(o.data.color_attributes[a])
    ca = o.data.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    ca.data.foreach_set("color", np.tile([1.0, 1.0, 1.0, 0.0], len(ca.data)).astype(np.float32))
    o.data.color_attributes.active_color_name = "Col"
    o.data.materials.clear()
kmat = bpy.data.materials.get(KEY) or bpy.data.materials.new(KEY)
for o in LODS: o.data.materials.append(kmat)
REP["joint_cut_rest"] = {}; REP["v2_gaps_rest"] = {}
for o in LODS:
    Vx, Ex = arrays(o)
    REP["joint_cut_rest"][o.name], _ = joint_cut(Vx, Ex, JR, None, o.name + " rest")
    REP["v2_gaps_rest"][o.name] = gaps(Vx, LAB_REST[o.name], Ex)
Vx, _ = arrays(low); REP["height_lod0_rest_m"] = round(float(Vx[:, 2].max()), 4)
REP["joint_cut_rest_spheres"] = {s: dict(shoulder=JR["upperarm." + s][0].round(4).tolist(), hip=JR["upperleg." + s][0].round(4).tolist()) for s in ("l", "r")}
log("v2 rest gaps", json.dumps(REP["v2_gaps_rest"]))
for o in list(scene.objects):
    if o.type == "MESH" and o not in LODS and o is not old0: bpy.data.objects.remove(o, do_unlink=True)
REP["REST"] = REST
save()
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "stage_skinned.blend"))
log("saved stage_skinned.blend")

ad = crig.animation_data or crig.animation_data_create()
ia = next(a for a in bpy.data.actions if a.name.split("|")[-1] == C["clips"]["idle"])
ad.action = ia
if getattr(ad, "action_slot", None) is None and ia.slots: ad.action_slot = ia.slots[0]
scene.frame_set(int(ia.frame_range[0])); bpy.context.view_layer.update()
off = {}
for b in DEFORM:
    pb, cb = rig.pose.bones[b], crig.pose.bones.get(b)
    if pb.parent is None or cb is None: continue
    lr = ((rig.matrix_world @ pb.bone.head_local) - (rig.matrix_world @ pb.parent.bone.head_local)).length
    lc = ((crig.matrix_world @ cb.head) - (crig.matrix_world @ cb.parent.head)).length
    off[b] = dict(rig_m=round(lr, 3), clip_idle_m=round(lc, 3), diff_m=round(lc - lr, 3))
REP["v2_clip_offset_check"] = off
log("v2 clip offsets (parent->child head distance, rig rest vs Idle clip)", json.dumps(off))

# ================================================================================================ 7. mallet for the renders
mc = RM["mallet"]; Lm = mc["length"]
bm = bmesh.new()
bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=0.0225, radius2=0.0225, depth=0.8 * Lm,
                      matrix=Matrix.Translation((0, 0.4 * Lm, 0)) @ Matrix.Rotation(math.radians(-90), 4, "X"))
bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation((0, 0.88 * Lm, 0)) @ Matrix.Diagonal((0.14, 0.14, 0.26, 1)))
mme = bpy.data.meshes.new("Mallet"); bm.to_mesh(mme); bm.free()
mallet = bpy.data.objects.new("Mallet", mme); scene.collection.objects.link(mallet)
wm = bpy.data.materials.new("mallet_wood"); wm.use_nodes = True
bs = wm.node_tree.nodes.get("Principled BSDF"); bs.inputs["Base Color"].default_value = (*[c ** 2.2 for c in hexrgb(mc["wood"])], 1)
mme.materials.append(wm)
pbh = rig.pose.bones["handslot.r"]
Mb = rig.matrix_world @ pbh.matrix
R3 = Mb.to_3x3().normalized().to_4x4()
Mw = Matrix.Translation(Mb.to_translation()) @ R3 @ Matrix.Translation((0, -mc["grip_from_end"], 0))
mallet.parent = rig; mallet.parent_type = "BONE"; mallet.parent_bone = "handslot.r"
Ptail = rig.matrix_world @ pbh.matrix @ Matrix.Translation((0, pbh.bone.length, 0))
mallet.matrix_parent_inverse = Ptail.inverted() @ Mw; mallet.matrix_basis = Matrix.Identity(4)

exec(compile(_src[_src.index(_m_proof):], P1, "exec"), globals())         # proof, renders, export, round trip
