"""Textured Warrior pilot (batch 12 item AB, part B). No generation: the texture comes from the generator's ORIGINAL
download (raw/body-1-pro31-mv.glb, restored from cf0e3c6 warrior_v8/hy3d_raw, 4K base colour), which the old pipeline
threw away by baking one flat colour per triangle (build_hy3d.py).

  blender -b --python build_warrior_tex.py -- <out> <repo>

Steps (all printed, report in <out>/warrior_tex_report.json):
  1. Import today's Warrior.fbx (41-bone rig, Warrior_LOD0 in bind pose) with the repo's FBX import settings.
  2. Import the raw GLB (500k triangles + its base colour), bake its node transforms into the data, then align it to
     today's LOD0 exactly as build_hy3d.py did (uniform scale to the height, feet on the ground, centred), then a
     scale + translation ICP refinement against LOD0 (yaw 0 and 180 both tried; the lower residual wins).
  3. LOD0: a copy of the raw, UVs dropped (the generator atlas has hundreds of seams the collapse cannot cross, so a
     seam-preserving decimation stalls far above budget), welded, specks dropped, collapse-decimated to ~6,000
     triangles, shaded smooth with 60 degree sharp edges, Smart UV Project, then the 4K original baked raw -> new
     into a 1024 x 1024 base colour (Cycles, selected to active, DIFFUSE colour only).
  4. Team mask in the texture ALPHA: today's LOD0 carries its mask code per triangle in its vertex colour alpha
     (eighths: 0 fixed, 8 team cloth, 5/6 team glow). An emission bake from today's LOD0 onto the new UVs writes
     0 fixed, 0.5 team cloth, 1.0 team glow (CMToonCommon.hlsl CMToonBaseMapColour decodes it).
  5. Skin weights transferred from today's LOD0 (nearest face, interpolated), max 4 per vertex, normalised; LOD1
     (~1,500) decimated from the new LOD0 keeping its UVs and weights; Warrior_Shadow kept as WarriorTex_Shadow.
  6. Per-clip stretch on Warrior_Clips.fbx (edge length deformed / rest, max and min over sampled frames), renders,
     export WarriorTex.fbx (the class recipe: fix_lods.py settings), re-import and compare the 41 bind matrices.
"""
import bpy, bmesh, sys, os, json, math, time
import numpy as np
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

argv = sys.argv[sys.argv.index("--") + 1:]
OUT, REPO = argv[0], argv[1]
os.makedirs(OUT, exist_ok=True)
WARRIOR = os.path.join(REPO, "Assets/_Game/Art/Characters/Warrior.fbx")
CLIPS = os.path.join(REPO, "Assets/_Game/Art/Characters/Warrior_Clips.fbx")
RAW = os.path.join(REPO, "Assets/_Game/Art/Characters/_source~/characters_hy/tex_pilot/raw/body-1-pro31-mv.glb")
LOD0_TRIS, LOD1_TRIS, TEX = int(os.environ.get("WT_LOD0", "6000")), int(os.environ.get("WT_LOD1", "1500")), 1024
REP = {"inputs": {"warrior": WARRIOR, "clips": CLIPS, "raw": RAW}}
T0 = time.time()


def log(*a):
    print("[warrior_tex %6.1fs]" % (time.time() - T0), *a, flush=True)


def save():
    json.dump(REP, open(os.path.join(OUT, "warrior_tex_report.json"), "w"), indent=1, default=str)


def ntri(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def world_verts(o):
    co = np.zeros(len(o.data.vertices) * 3)
    o.data.vertices.foreach_get("co", co)
    M = np.array(o.matrix_world)
    V = co.reshape(-1, 3)
    return V @ M[:3, :3].T + M[:3, 3]


def override(o):
    return dict(object=o, active_object=o, selected_objects=[o], selected_editable_objects=[o])


def apply_mod(o, name):
    with bpy.context.temp_override(**override(o)):
        bpy.ops.object.modifier_move_to_index(modifier=name, index=0)
        bpy.ops.object.modifier_apply(modifier=name)


def import_fbx(path, anim):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path, use_anim=anim, use_custom_normals=False, ignore_leaf_bones=False,
                             automatic_bone_orientation=False, primary_bone_axis="Y", secondary_bone_axis="X")
    return [o for o in bpy.data.objects if o not in before]


bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

# ------------------------------------------------------------------------------------------------ 1. today's Warrior
for o in import_fbx(WARRIOR, False):
    pass
rig = next(o for o in scene.objects if o.type == "ARMATURE")
rig.data.pose_position = "REST"
mesh_of = lambda tag: next((o for o in scene.objects if o.type == "MESH" and o.name.startswith("Warrior_" + tag)), None)
old0, old1, oldH, oldS = mesh_of("LOD0"), mesh_of("LOD1"), mesh_of("Hero"), mesh_of("Shadow")
REP["old"] = {o.name: dict(tris=ntri(o), verts=len(o.data.vertices)) for o in (old0, old1, oldH, oldS) if o}
REP["bones"] = len(rig.data.bones)
REST = {b.name: [list(r) for r in b.matrix_local] for b in rig.data.bones}
W = world_verts(old0)
log("today's Warrior", REP["old"], "bones", REP["bones"], "LOD0 bounds", W.min(0).round(3).tolist(), W.max(0).round(3).tolist())

# ------------------------------------------------------------------------------------------------ 2. raw GLB, align
before = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=RAW)
new = [o for o in bpy.data.objects if o not in before]
rm = [o for o in new if o.type == "MESH"]
for o in rm:
    M = o.matrix_world.copy()
    o.parent = None
    o.data.transform(M)
    o.matrix_world = Matrix.Identity(4)
for o in new:
    if o.type != "MESH":
        bpy.data.objects.remove(o, do_unlink=True)
raw = rm[0]
if len(rm) > 1:
    with bpy.context.temp_override(active_object=raw, object=raw, selected_editable_objects=rm, selected_objects=rm):
        bpy.ops.object.join()
raw.name = "Raw_HY"
img = None
for m in raw.data.materials:
    if m and m.use_nodes:
        for nd in m.node_tree.nodes:
            if nd.type == "TEX_IMAGE" and nd.image:
                img = img or nd.image
REP["raw"] = dict(tris=ntri(raw), verts=len(raw.data.vertices), uv_layers=len(raw.data.uv_layers),
                  texture=list(img.size) if img else None, texture_name=img.name if img else None)
log("raw", REP["raw"])
if img is None or not raw.data.uv_layers:
    REP["stop"] = "the raw GLB carries no base colour texture or no UVs"
    save()
    raise SystemExit(REP["stop"])

V = world_verts(raw)
# The reference is today's Warrior_Hero (21.5k: body 19k + shield + cleaver; LOD0 is its decimation): its props sit in
# the same mesh, so the fit is build_hy3d.py's own (body height = the Hero's head top, soles on z = 0, x centred on the
# bounds, y on the torso median), never a bounds or ICP fit that the shield and cleaver would drag. Then a translation-only
# refinement on the closest pairs (props excluded by the distance trim), kept only if it lowers the median distance.
REF = oldH or old0
WR = world_verts(REF)
H_T = float(WR[:, 2].max())
s = H_T / float(V[:, 2].max() - V[:, 2].min())
V = (V - [0, 0, V[:, 2].min()]) * s
torso = V[(V[:, 2] > 0.5 * H_T) & (V[:, 2] < 0.6 * H_T)]
V -= [(V[:, 0].max() + V[:, 0].min()) / 2, float(np.median(torso[:, 1])), 0]
bvh_old = BVHTree.FromPolygons([tuple(v) for v in WR], [tuple(p.vertices) for p in REF.data.polygons])
rng = np.random.default_rng(12)
sample_i = rng.choice(len(V), min(5000, len(V)), replace=False)


def dists(P):
    out = np.zeros(len(P))
    near = np.zeros((len(P), 3))
    for k, p in enumerate(P):
        r = bvh_old.find_nearest(Vector(p))
        out[k], near[k] = r[3], tuple(r[0])
    return out, near


d0, _ = dists(V[sample_i])
tr = np.zeros(3)
for _ in range(8):
    d, near = dists(V[sample_i] + tr)
    keep = d < max(0.004, 3.0 * float(np.median(d)))
    tr = tr + (near[keep] - (V[sample_i] + tr)[keep]).mean(0)
d1, _ = dists(V[sample_i] + tr)
use = float(np.median(d1)) < float(np.median(d0))
REP["align"] = dict(reference=REF.name, height_m=round(H_T, 4), fit_scale_vs_glb=round(s, 4),
                    median_m_before=round(float(np.median(d0)), 5), p90_m_before=round(float(np.percentile(d0, 90)), 5),
                    refine_translate=np.round(tr, 4).tolist(), median_m_after=round(float(np.median(d1)), 5),
                    p90_m_after=round(float(np.percentile(d1, 90)), 5), refinement_kept=use, chosen_yaw=0.0)
best = (0, 0.0, V + (tr if use else 0))
raw.data.vertices.foreach_set("co", np.ascontiguousarray(best[2], np.float32).ravel())
raw.data.update()
log("aligned", REP["align"])

# ------------------------------------------------------------------------------------------------ 2b. A-pose
# Run 4 showed the real mismatch: today's Hero is the raw UNSKINNED to the rig's rest (build_hy3d.py posed the rig into
# the raw's A-pose, upper arms 32.1 and upper legs 15.2 degrees, then took the mesh back to the rest), so the raw's arms
# and legs sit away from the skeleton. Pose the rig into the raw's A-pose the same way (fit_pose), work in that pose
# (cull, bake, weight transfer), then unskin the new mesh to the rest with the same bones, as the Hero was.
rig.data.pose_position = "POSE"
A_ = rig.matrix_world.copy()
J = {b.name: A_ @ b.head_local for b in rig.data.bones}
PR = best[2]
REP["pose_fit_deg"] = {}
for side in ("l", "r"):
    sg = 1.0 if J["hand." + side].x > 0 else -1.0
    ext = float(np.max(sg * PR[:, 0]))
    hand = PR[sg * PR[:, 0] > 0.8 * ext].mean(0)
    ank = PR[(PR[:, 2] > 0.10) & (PR[:, 2] < 0.20) & (sg * PR[:, 0] > 0.03)].mean(0)
    for bone, tip, tgt in (("upperarm." + side, "hand." + side, hand), ("upperleg." + side, "foot." + side, ank)):
        q = (J[tip] - J[bone]).normalized().rotation_difference((Vector(tuple(tgt)) - J[bone]).normalized())
        pb = rig.pose.bones[bone]
        Mw = Matrix.Translation(J[bone]) @ q.to_matrix().to_4x4() @ Matrix.Translation(-J[bone]) @ (A_ @ pb.matrix)
        pb.matrix = A_.inverted() @ Mw
        bpy.context.view_layer.update()
        REP["pose_fit_deg"][bone] = round(math.degrees(q.angle), 1)
dg0 = bpy.context.evaluated_depsgraph_get()
evr = REF.evaluated_get(dg0)
mer = evr.to_mesh()
WP = np.array([tuple(REF.matrix_world @ v.co) for v in mer.vertices])
evr.to_mesh_clear()
bvh_old = BVHTree.FromPolygons([tuple(v) for v in WP], [tuple(p.vertices) for p in REF.data.polygons])
dp, _ = dists(PR[sample_i])
REP["pose_fit"] = dict(median_m=round(float(np.median(dp)), 5), p90_m=round(float(np.percentile(dp, 90)), 5),
                       p99_m=round(float(np.percentile(dp, 99)), 5), build_hy3d_reported_deg={"upperarm": 32.1, "upperleg": 15.2})
log("A-pose", REP["pose_fit_deg"], REP["pose_fit"])

# ------------------------------------------------------------------------------------------------ 3. LOD0 geometry
low = raw.copy()
low.data = raw.data.copy()
scene.collection.objects.link(low)
low.name = low.data.name = "WT_build"
while low.data.uv_layers:
    low.data.uv_layers.remove(low.data.uv_layers[0])
for a in list(low.data.color_attributes):
    low.data.color_attributes.remove(a)
low.data.materials.clear()
bm = bmesh.new()
bm.from_mesh(low.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005)
bm.verts.ensure_lookup_table()
seen, dropped = set(), 0
for v0 in list(bm.verts):
    if v0.index in seen or not v0.is_valid:
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
    P = np.array([tuple(v.co) for v in isl])
    if np.linalg.norm(P.max(0) - P.min(0)) < 0.03:
        bmesh.ops.delete(bm, geom=isl, context="VERTS")
        dropped += 1
bm.to_mesh(low.data)
bm.free()
REP["weld"] = dict(tris_after_weld=ntri(low), specks_dropped=dropped)
# Run 3 showed the raw keeps surfaces today's Hero does not have (p90 distance 0.12 m: the arm and leg membranes the
# HY build cleaned up), and their edges stretched 7x to 19x in the clips while today's LOD0 stays under 1.8x. Drop every
# raw vertex farther than WT_CULL_M (0.03 m) from today's Hero before the decimation.
CULL = float(os.environ.get("WT_CULL_M", "0.03"))


def open_edges(me_):
    b_ = bmesh.new(); b_.from_mesh(me_)
    n_ = sum(1 for e in b_.edges if len(e.link_faces) == 1); b_.free()
    return n_


def region_of(name):
    n_ = name.lower()
    if "head" in n_ or "neck" in n_: return "head"
    if any(k in n_ for k in ("arm", "hand", "shoulder", "wrist", "finger", "thumb", "slot", "elbow")): return "arms"
    if any(k in n_ for k in ("leg", "foot", "toe", "knee", "shin", "thigh", "ankle")): return "legs"
    return "torso"


gn = {g.index: g.name for g in REF.vertex_groups}
vreg = []
for v in REF.data.vertices:
    gs = sorted(((g.weight, g.group) for g in v.groups), reverse=True)
    vreg.append(region_of(gn.get(gs[0][1], "")) if gs else "torso")
preg = [vreg[p.vertices[0]] for p in REF.data.polygons]
REP["open_edges"] = {"raw_welded": open_edges(low.data)}
if CULL > 0:
    bm = bmesh.new()
    bm.from_mesh(low.data)
    far, by_region, all_region = [], {}, {}
    for v in bm.verts:
        loc, nrm, idx, dist = bvh_old.find_nearest(v.co)
        rg = preg[idx] if idx is not None else "torso"
        all_region[rg] = all_region.get(rg, 0) + 1
        if dist > CULL:
            far.append(v)
            by_region[rg] = by_region.get(rg, 0) + 1
    bmesh.ops.delete(bm, geom=far, context="VERTS")
    bnd = [e for e in bm.edges if len(e.link_faces) == 1]
    before_fill = len(bnd)
    bmesh.ops.holes_fill(bm, edges=bnd, sides=16)        # pinholes only; a long membrane cut stays open and is counted
    bm.to_mesh(low.data)
    bm.free()
    REP["cull"] = dict(metres=CULL, vertices_removed=len(far), removed_by_region=by_region, raw_vertices_by_region=all_region,
                       open_edges_after_cut=before_fill, open_edges_after_pinhole_fill=open_edges(low.data), tris_after=ntri(low))
    log("membrane cull", REP["cull"])
ratio = LOD0_TRIS / max(1, ntri(low))
for attempt in range(3):
    work = low.data.copy()
    d = low.modifiers.new("Dec", "DECIMATE")
    d.decimate_type, d.use_collapse_triangulate, d.ratio = "COLLAPSE", True, min(1.0, ratio)
    apply_mod(low, "Dec")
    t = ntri(low)
    log("decimate attempt", attempt, "ratio", round(ratio, 5), "tris", t)
    if 5600 <= t <= 6400 or attempt == 2:
        break
    low.data = work
    ratio *= LOD0_TRIS / max(1, t)

# ------------------------------------------------------------------------------------------------ 2c. shield and cleaver
# Taken from today's LOD0 unchanged: same vertices, same rigid hand-slot weights. Posed into the A-pose with those
# weights, joined as separate islands of the LOD0 (as today's LOD0 carries them, so ActorViews needs nothing new), baked
# into their own region of the 1024 texture from their own vertex colours and team mask codes, and given their original
# weights back after the body weight transfer, so the unskin returns them exactly to today's rest position.
BN = [b.name for b in rig.data.bones]
SKA = {pb.name: A_ @ pb.matrix @ pb.bone.matrix_local.inverted() @ A_.inverted() for pb in rig.pose.bones}
g0 = {g.index: g.name for g in old0.vertex_groups}
POLD = [v.index for v in old0.data.vertices
        if len(v.groups) and "slot" in g0.get(max(v.groups, key=lambda g: g.weight).group, "").lower()]
pset_old = set(POLD)
Pme = old0.data.copy()
Pob = bpy.data.objects.new("WT_props", Pme)
scene.collection.objects.link(Pob)
bm = bmesh.new()
bm.from_mesh(Pme)
bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.index not in pset_old], context="VERTS")
bm.to_mesh(Pme)
bm.free()
Pob.vertex_groups.clear()
while len(Pme.uv_layers):
    Pme.uv_layers.remove(Pme.uv_layers[0])
Pme.materials.clear()
pca = Pme.color_attributes.get("Col") or (Pme.color_attributes[0] if len(Pme.color_attributes) else None)
pca.name = "PropCol"
for c_ in [c_ for c_ in Pme.color_attributes if c_.name != "PropCol"]:
    Pme.color_attributes.remove(c_)
Pme.attributes.new("is_prop", "INT", "POINT").data.foreach_set("value", np.ones(len(Pme.vertices), np.int32))
pbk = np.full((len(Pme.vertices), 4), -1, np.int32)
pwk = np.zeros((len(Pme.vertices), 4), np.float32)
per_bone, second_w = {}, 0.0
Mw0 = old0.matrix_world.copy()
for i_, oi in enumerate(POLD):
    ov = old0.data.vertices[oi]
    ws = sorted(((g.weight, g0[g.group]) for g in ov.groups if g.weight > 1e-4), reverse=True)[:4]
    tot_ = sum(w_ for w_, _ in ws)
    if len(ws) > 1:
        second_w = max(second_w, ws[1][0] / tot_)
    M_ = Matrix.Diagonal((0.0, 0.0, 0.0, 0.0))
    for k_, (w_, nm) in enumerate(ws):
        pbk[i_, k_], pwk[i_, k_] = BN.index(nm), w_ / tot_
        M_ = M_ + SKA[nm] * (w_ / tot_)
    Pme.vertices[i_].co = M_ @ (Mw0 @ ov.co)
    per_bone[ws[0][1]] = per_bone.get(ws[0][1], 0) + 1
for k_ in range(4):
    Pme.attributes.new("pb%d" % k_, "INT", "POINT").data.foreach_set("value", pbk[:, k_].copy())
    Pme.attributes.new("pw%d" % k_, "FLOAT", "POINT").data.foreach_set("value", pwk[:, k_].copy())
REP["props"] = dict(source="today's Warrior_LOD0 (vertices whose strongest weight is a hand-slot bone)",
                    vertices=len(POLD), tris=ntri(Pob), max_second_weight=round(second_w, 4),
                    by_bone={nm: dict(vertices=n_, parent=rig.data.bones[nm].parent.name if rig.data.bones[nm].parent else None,
                                      side_x=round(float(np.mean([(Mw0 @ old0.data.vertices[oi].co).x for oi in POLD
                                                                   if g0[max(old0.data.vertices[oi].groups, key=lambda g: g.weight).group] == nm])), 3))
                             for nm, n_ in per_bone.items()},
                    colour_path="baked into the 1024 base texture (the _CMTOON_BASEMAP path replaces vertex colour; it does not mix)")
for o in scene.objects:
    o.select_set(o is low or o is Pob)
bpy.context.view_layer.objects.active = low
bpy.ops.object.join()
fl_ = np.zeros(len(low.data.vertices), np.int32)
low.data.attributes["is_prop"].data.foreach_get("value", fl_)
REP["props"]["joined_prop_vertices"] = int(fl_.sum())
REP["props"]["body_tris"], REP["props"]["lod0_tris_total"] = ntri(low) - REP["props"]["tris"], ntri(low)
log("props", REP["props"])
for p in low.data.polygons:
    p.use_smooth = True
low.data.set_sharp_from_angle(angle=math.radians(60.0))
bpy.context.view_layer.objects.active = low
for o in scene.objects:
    o.select_set(o is low)
scene.tool_settings.use_uv_select_sync = True


def uv_cov(me_):
    a_ = np.zeros(len(me_.loops) * 2); me_.uv_layers.active.data.foreach_get("uv", a_); a_ = a_.reshape(-1, 2)
    t_ = 0.0
    for p_ in me_.polygons:
        x_, y_, z_ = a_[p_.loop_start], a_[p_.loop_start + 1], a_[p_.loop_start + 2]
        t_ += abs((y_[0] - x_[0]) * (z_[1] - x_[1]) - (y_[1] - x_[1]) * (z_[0] - x_[0])) / 2
    return t_


def unwrap(ang, mg):
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(ang), island_margin=mg, area_weight=0.0, correct_aspect=True,
                             scale_to_bounds=False)
    try:
        bpy.ops.uv.select_all(action="SELECT")
        bpy.ops.uv.average_islands_scale()
        bpy.ops.uv.pack_islands(rotate=True, margin=mg, shape_method="CONCAVE")
    except Exception as x:
        log("pack failed", x)
    bpy.ops.object.mode_set(mode="OBJECT")
    return uv_cov(low.data)


trials = [(unwrap(a_, m_), a_, m_) for a_, m_ in ((80.0, 0.002), (66.0, 0.0015), (50.0, 0.0015))]
REP["uv_trials"] = [dict(angle=a_, margin=m_, coverage=round(c_, 4)) for c_, a_, m_ in trials]
bestuv = max(trials)
if bestuv is not trials[-1]:
    unwrap(bestuv[1], bestuv[2])
log("uv trials", REP["uv_trials"], "kept", bestuv[1:])
me = low.data
me.uv_layers[0].name = "UVMap"
uv = np.zeros(len(me.loops) * 2)
me.uv_layers[0].data.foreach_get("uv", uv)
uv = uv.reshape(-1, 2)
uv_area, flipped = 0.0, 0
for p in me.polygons:
    a, b, c = (uv[p.loop_start + i] for i in range(3))
    cr = (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])
    uv_area += abs(cr) / 2
    flipped += cr < 0
REP["lod0"] = dict(tris=ntri(low), verts=len(me.vertices), uv_coverage=round(uv_area, 4), uv_flipped_tris=int(flipped),
                   uv_min=uv.min(0).round(4).tolist(), uv_max=uv.max(0).round(4).tolist())
log("LOD0", REP["lod0"])
save()

# ------------------------------------------------------------------------------------------------ 3b. bake colour
scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = 4
scene.render.bake.margin = 8
scene.render.bake.margin_type = "EXTEND"
base = bpy.data.images.new("WarriorTex_Base", TEX, TEX, alpha=True)
maskimg = bpy.data.images.new("WarriorTex_Mask", TEX, TEX, alpha=False)
maskimg.colorspace_settings.name = "Non-Color"
mat = bpy.data.materials.new("WarriorTex")
mat.use_nodes = True
nt = mat.node_tree
tex = nt.nodes.new("ShaderNodeTexImage")
tex.image = base
bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
if bsdf is None:
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    o_ = next((n for n in nt.nodes if n.type == "OUTPUT_MATERIAL"), None) or nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(bsdf.outputs[0], o_.inputs["Surface"])
nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
me.materials.append(mat)


def bake(kind, source, image, **kw):
    tex.image = image
    nt.nodes.active = tex
    for o in scene.objects:
        o.select_set(o is low or o is source)
    bpy.context.view_layer.objects.active = low
    t = time.time()
    bpy.ops.object.bake(type=kind, use_selected_to_active=True, cage_extrusion=0.02, max_ray_distance=0.08,
                        margin=8, target="IMAGE_TEXTURES", **kw)
    log("baked", kind, "from", source.name, "in %.1f s" % (time.time() - t))


bake("DIFFUSE", raw, base, pass_filter={"COLOR"})

# ------------------------------------------------------------------------------------------------ 4. team mask bake
mm = bpy.data.materials.new("MaskFromCode")
mm.use_nodes = True
mt = mm.node_tree
for n in list(mt.nodes):
    mt.nodes.remove(n)
att = mt.nodes.new("ShaderNodeAttribute")
att.attribute_name = "Col"
mul = mt.nodes.new("ShaderNodeMath"); mul.operation = "MULTIPLY"; mul.inputs[1].default_value = 8.0
mt.links.new(att.outputs["Alpha"], mul.inputs[0])


def cmp(code):
    c = mt.nodes.new("ShaderNodeMath"); c.operation = "COMPARE"
    mt.links.new(mul.outputs[0], c.inputs[0]); c.inputs[1].default_value = code; c.inputs[2].default_value = 0.5
    return c


c8, c6, c5 = cmp(8.0), cmp(6.0), cmp(5.0)
half = mt.nodes.new("ShaderNodeMath"); half.operation = "MULTIPLY"; half.inputs[1].default_value = 0.5
mt.links.new(c8.outputs[0], half.inputs[0])
g = mt.nodes.new("ShaderNodeMath"); g.operation = "ADD"
mt.links.new(c6.outputs[0], g.inputs[0]); mt.links.new(c5.outputs[0], g.inputs[1])
tot = mt.nodes.new("ShaderNodeMath"); tot.operation = "ADD"; tot.use_clamp = True
mt.links.new(half.outputs[0], tot.inputs[0]); mt.links.new(g.outputs[0], tot.inputs[1])
em = mt.nodes.new("ShaderNodeEmission")
mt.links.new(tot.outputs[0], em.inputs["Color"])
outn = mt.nodes.new("ShaderNodeOutputMaterial")
mt.links.new(em.outputs[0], outn.inputs["Surface"])
codes = {}
ca = REF.data.color_attributes.get("Col") or (REF.data.color_attributes[0] if REF.data.color_attributes else None)
if ca is not None:
    al = np.zeros(len(ca.data) * 4)
    ca.data.foreach_get("color", al)
    for k, n in zip(*np.unique(np.round(al.reshape(-1, 4)[:, 3] * 8).astype(int), return_counts=True)):
        codes[int(k)] = int(n)
    att.attribute_name = ca.name
REP["reference_mask_codes_per_corner"] = codes
REF_MATS = list(REF.data.materials)
REF.data.materials.clear()
REF.data.materials.append(mm)
for p_ in REF.data.polygons:
    p_.material_index = 0
bake("EMIT", REF, maskimg)
tex.image = base


def lin2srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.0031308, 12.92 * c, 1.055 * np.power(c, 1 / 2.4) - 0.055)


PT = low.copy()
PT.data = low.data.copy()
scene.collection.objects.link(PT)
flp = np.zeros(len(PT.data.vertices), np.int32)
PT.data.attributes["is_prop"].data.foreach_get("value", flp)
bm = bmesh.new()
bm.from_mesh(PT.data)
bmesh.ops.delete(bm, geom=[f for f in bm.faces if not all(flp[v.index] for v in f.verts)], context="FACES")
bm.to_mesh(PT.data)
bm.free()
pcm = bpy.data.materials.new("PropColour")
pcm.use_nodes = True
for n in list(pcm.node_tree.nodes):
    pcm.node_tree.nodes.remove(n)
pa_ = pcm.node_tree.nodes.new("ShaderNodeAttribute"); pa_.attribute_name = "PropCol"
pe_ = pcm.node_tree.nodes.new("ShaderNodeEmission")
po_ = pcm.node_tree.nodes.new("ShaderNodeOutputMaterial")
pcm.node_tree.links.new(pa_.outputs["Color"], pe_.inputs["Color"])
pcm.node_tree.links.new(pe_.outputs[0], po_.inputs["Surface"])
pmm = mm.copy()
for n in pmm.node_tree.nodes:
    if n.type == "ATTRIBUTE":
        n.attribute_name = "PropCol"


def prop_bake(name, m_):
    im_ = bpy.data.images.new(name, TEX, TEX, alpha=True, float_buffer=True)
    im_.pixels.foreach_set(np.full(TEX * TEX * 4, -1.0, np.float32))     # sentinel: pixels the bake did not write
    tn = m_.node_tree.nodes.new("ShaderNodeTexImage")
    tn.image = im_
    m_.node_tree.nodes.active = tn
    PT.data.materials.clear()
    PT.data.materials.append(m_)
    for p_ in PT.data.polygons:
        p_.material_index = 0
    for o in scene.objects:
        o.select_set(o is PT)
    bpy.context.view_layer.objects.active = PT
    bpy.ops.object.bake(type="EMIT", use_selected_to_active=False, margin=1, target="IMAGE_TEXTURES", use_clear=False)
    return np.array(im_.pixels[:], np.float32).reshape(TEX, TEX, 4)


PCv = prop_bake("PropC", pcm)
PMv = prop_bake("PropM", pmm)
bpy.data.objects.remove(PT, do_unlink=True)

px = np.array(base.pixels[:]).reshape(TEX, TEX, 4)
mk = np.array(maskimg.pixels[:]).reshape(TEX, TEX, 4)[:, :, 0]
covp = PCv[:, :, 0] > -0.5
px[covp, :3] = lin2srgb(PCv[covp, :3])
mk[covp] = PMv[covp, 0]
REP["props"]["texture"] = dict(pixels=int(covp.sum()), fraction=round(float(covp.mean()), 4),
                               team_cloth_pixels=int(((mk > 0.25) & (mk < 0.75) & covp).sum()),
                               team_glow_pixels=int(((mk >= 0.75) & covp).sum()),
                               rgb_mean_srgb=px[covp, :3].mean(0).round(3).tolist() if covp.any() else None)
log("prop texture", REP["props"]["texture"])
px[:, :, 3] = mk
base.pixels.foreach_set(px.ravel().astype(np.float32))
base.filepath_raw = os.path.join(OUT, "WarriorTex_Base.png")
base.file_format = "PNG"
base.save()
covered = mk > -1
REP["texture"] = dict(size=[TEX, TEX], path=base.filepath_raw, alpha_team_cloth_fraction=round(float(np.mean((mk > 0.25) & (mk < 0.75))), 4),
                      alpha_team_glow_fraction=round(float(np.mean(mk >= 0.75)), 4),
                      rgb_mean=px[:, :, :3].reshape(-1, 3).mean(0).round(3).tolist())
log("texture", REP["texture"])
save()

# ------------------------------------------------------------------------------------------------ 5. skin, LOD1, shadow
# Weight source: the Hero without its props (shield and cleaver are rigid on the hand slots; a body vertex near the hand
# must not take a hand-slot weight from them).
REFB = REF.copy()
REFB.data = REF.data.copy()
scene.collection.objects.link(REFB)
gname = {g.index: g.name for g in REFB.vertex_groups}
bm = bmesh.new()
bm.from_mesh(REFB.data)
dl = bm.verts.layers.deform.active
kill = [v for v in bm.verts if dl is not None and len(v[dl]) and "slot" in gname.get(max(v[dl].items(), key=lambda kv: kv[1])[0], "").lower()]
bmesh.ops.delete(bm, geom=kill, context="VERTS")
bm.to_mesh(REFB.data)
bm.free()
REP["weight_source"] = dict(object=REF.name, prop_vertices_removed=len(kill), tris=ntri(REFB))


def limit4(o):
    vg = o.vertex_groups
    for v in o.data.vertices:
        ws = sorted(((g.weight, g.group) for g in v.groups), reverse=True)
        keep = [x for x in ws[:4] if x[0] > 1e-4]
        for w_, gi in ws:
            if (w_, gi) not in keep:
                vg[gi].remove([v.index])
        tot = sum(w_ for w_, _ in keep)
        for w_, gi in keep:
            vg[gi].add([v.index], w_ / tot, "REPLACE")


newo = old0.copy()                                  # same parent and armature modifier as LOD0
newo.data = low.data.copy()
scene.collection.objects.link(newo)
Minv = old0.matrix_world.inverted()
newo.data.transform(Minv @ low.matrix_world)
newo.name = newo.data.name = "WarriorTex_LOD0"
for m in list(newo.modifiers):
    if m.type != "ARMATURE":
        newo.modifiers.remove(m)
arms = [m for m in newo.modifiers if m.type == "ARMATURE"]
for m in arms:
    m.show_viewport = False
for g in REF.vertex_groups:                         # Blender keeps group names on the mesh data: recreate them
    if newo.vertex_groups.get(g.name) is None:
        newo.vertex_groups.new(name=g.name)
dt = newo.modifiers.new("DT", "DATA_TRANSFER")
dt.object = REFB
dt.use_vert_data = True
dt.data_types_verts = {"VGROUP_WEIGHTS"}
dt.vert_mapping = "POLYINTERP_NEAREST"
dt.layers_vgroup_select_src = "ALL"
dt.layers_vgroup_select_dst = "NAME"
apply_mod(newo, "DT")
limit4(newo)
bpy.data.objects.remove(REFB, do_unlink=True)
nmv = len(newo.data.vertices)
fl_ = np.zeros(nmv, np.int32)
newo.data.attributes["is_prop"].data.foreach_get("value", fl_)
PNEW = np.nonzero(fl_)[0]
pbk = np.zeros((nmv, 4), np.int32)
pwk = np.zeros((nmv, 4), np.float32)
for k_ in range(4):
    a_ = np.zeros(nmv, np.int32); newo.data.attributes["pb%d" % k_].data.foreach_get("value", a_); pbk[:, k_] = a_
    b_ = np.zeros(nmv, np.float32); newo.data.attributes["pw%d" % k_].data.foreach_get("value", b_); pwk[:, k_] = b_
vg = newo.vertex_groups
for i_ in PNEW:
    i_ = int(i_)
    for gi in [g.group for g in newo.data.vertices[i_].groups]:
        vg[gi].remove([i_])
    for k_ in range(4):
        if pbk[i_, k_] >= 0 and pwk[i_, k_] > 0:
            nm = BN[pbk[i_, k_]]
            (vg.get(nm) or vg.new(name=nm)).add([i_], float(pwk[i_, k_]), "REPLACE")
for nm in ["is_prop"] + ["pb%d" % k_ for k_ in range(4)] + ["pw%d" % k_ for k_ in range(4)]:
    newo.data.attributes.remove(newo.data.attributes[nm])
newo.data.color_attributes.remove(newo.data.color_attributes["PropCol"])
REP["props"]["weights_restored_vertices"] = len(PNEW)
# Unskin to the rest (linear blend, as the armature modifier deforms): rest = (sum w S_b)^-1 posed.
Mo = newo.matrix_world.copy()
Moi = Mo.inverted()
SK = {pb.name: A_ @ pb.matrix @ pb.bone.matrix_local.inverted() @ A_.inverted() for pb in rig.pose.bones}
gname2 = {g.index: g.name for g in newo.vertex_groups}
unsk = 0.0
for v in newo.data.vertices:
    M = Matrix.Diagonal((0.0, 0.0, 0.0, 0.0))
    tot = 0.0
    for g in v.groups:
        nm = gname2.get(g.group)
        if nm in SK and g.weight > 0:
            M = M + SK[nm] * g.weight
            tot += g.weight
    if tot <= 0:
        continue
    wp = Mo @ v.co
    rp = (M * (1.0 / tot)).inverted_safe() @ wp
    unsk = max(unsk, (rp - wp).length)
    v.co = Moi @ rp
newo.data.update()
for pb in rig.pose.bones:
    pb.matrix_basis = Matrix.Identity(4)
bpy.context.view_layer.update()
REP["unskin_max_move_m"] = round(unsk, 4)
col = newo.data.color_attributes.new("Col", "BYTE_COLOR", "CORNER")   # code 0 (fixed, lined): the outline mask path
col.data.foreach_set("color", np.tile([1.0, 1.0, 1.0, 0.0], len(col.data)).astype(np.float32))
newo.data.color_attributes.active_color_name = "Col"
bpy.data.objects.remove(low, do_unlink=True)
newo.name = newo.data.name = "WarriorTex_LOD0"

lod1 = newo.copy()
lod1.data = newo.data.copy()
scene.collection.objects.link(lod1)
lod1.name = lod1.data.name = "WarriorTex_LOD1"
d = lod1.modifiers.new("Dec", "DECIMATE")
d.decimate_type, d.use_collapse_triangulate, d.ratio = "COLLAPSE", True, LOD1_TRIS / max(1, ntri(newo))
apply_mod(lod1, "Dec")
limit4(lod1)
for m in arms:
    m.show_viewport = True
for m in lod1.modifiers:
    if m.type == "ARMATURE":
        m.show_viewport = True
if oldS:
    oldS.name = oldS.data.name = "WarriorTex_Shadow"


def weights_report(o):
    n_inf, unw = [], 0
    for v in o.data.vertices:
        ws = [g.weight for g in v.groups if g.weight > 1e-4]
        n_inf.append(len(ws))
        unw += not ws
    return dict(max_influences=max(n_inf), unweighted_verts=unw, groups=len(o.vertex_groups))


REP["skin"] = {o.name: weights_report(o) for o in (newo, lod1)}
REP["tris"] = {o.name: ntri(o) for o in (newo, lod1, oldS) if o}
log("skin", REP["skin"], "tris", REP["tris"])
save()

# ------------------------------------------------------------------------------------------------ renders (evidence)
base.alpha_mode = "NONE"                            # display only: the saved PNG keeps its alpha mask
try:
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "FLAT"
    scene.render.resolution_x, scene.render.resolution_y = 768, 768
    scene.world = bpy.data.worlds.new("w")
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = "ORTHO"
    zc = float(W[:, 2].min() + W[:, 2].max()) / 2

    def shot(name, show, ctype, yaw, z, ortho):
        for o in scene.objects:
            if o.type == "MESH":
                o.hide_render = o not in show
        scene.display.shading.color_type = ctype
        a = math.radians(yaw)
        dvec = Vector((math.sin(a), -math.cos(a), 0)) * 6
        cam.location = Vector((0, 0, z)) + dvec
        cam.rotation_euler = (math.radians(90), 0, a)
        cam.data.ortho_scale = ortho
        scene.render.filepath = os.path.join(OUT, name + ".png")
        bpy.ops.render.render(write_still=True)

    yaw = 180.0 if REP["align"]["chosen_yaw"] == 0.0 else 0.0
    for y_ in (yaw, yaw + 180.0):
        shot("old_lod0_%d" % y_, [old0], "VERTEX", y_, zc, 2.3)
        shot("new_lod0_%d" % y_, [newo], "TEXTURE", y_, zc, 2.3)
    for y_ in (0.0, 180.0):
        shot("new_face_%d" % y_, [newo], "TEXTURE", y_, float(W[:, 2].max()) - 0.25, 0.7)
        shot("old_face_%d" % y_, [old0], "VERTEX", y_, float(W[:, 2].max()) - 0.25, 0.7)
        shot("new_threequarter_%d" % (y_ + 45), [newo], "TEXTURE", y_ + 45.0, zc, 2.3)
    shot("new_lod1", [lod1], "TEXTURE", yaw, zc, 2.3)
except Exception as x:
    REP["render_error"] = repr(x)
    log("render skipped", x)

# ------------------------------------------------------------------------------------------------ 6. clip stretch
for o in (old1, oldH):
    if o:
        bpy.data.objects.remove(o, do_unlink=True)
rig.data.pose_position = "POSE"
clip_objs = import_fbx(CLIPS, True)
carm = next((o for o in clip_objs if o.type == "ARMATURE"), None)
edges = np.array([tuple(e.vertices) for e in newo.data.edges])
dg = bpy.context.evaluated_depsgraph_get()


def deformed(o):
    ev = o.evaluated_get(dg)
    m = ev.to_mesh()
    co = np.zeros(len(m.vertices) * 3)
    m.vertices.foreach_get("co", co)
    ev.to_mesh_clear()
    return co.reshape(-1, 3)


PSET_NEW = set(int(i_) for i_ in PNEW)
NB_POLYS = [tuple(p.vertices) for p in newo.data.polygons if not any(v in PSET_NEW for v in p.vertices)]
OB_POLYS = [tuple(p.vertices) for p in old0.data.polygons if not any(v in pset_old for v in p.vertices)]
POLD_A = np.array(POLD)
props_check = {}


def prop_frame(Dn, Do, rec):
    kd = KDTree(len(POLD_A))
    for j_, q_ in enumerate(Do[POLD_A]):
        kd.insert(Vector(tuple(q_)), j_)
    kd.balance()
    rec["attach_max_m"] = max(rec.get("attach_max_m", 0.0), max(kd.find(Vector(tuple(q_)))[2] for q_ in Dn[PNEW]))
    for key, Dx, pidx, polys in (("new", Dn, PNEW, NB_POLYS), ("today", Do, POLD_A, OB_POLYS)):
        bv = BVHTree.FromPolygons([tuple(q_) for q_ in Dx], polys)
        dep = []
        for q_ in Dx[pidx]:
            q_ = Vector(tuple(q_))
            loc, nrm, _, dd = bv.find_nearest(q_)
            if loc is not None and dd < 0.05 and (q_ - loc).dot(nrm) < 0:
                dep.append(dd)
        rec[key + "_inside_max"] = max(rec.get(key + "_inside_max", 0), len(dep))
        rec[key + "_depth_max_m"] = max(rec.get(key + "_depth_max_m", 0.0), max(dep) if dep else 0.0)


ARMS = {"new": (newo, edges), "control_today_lod0": (old0, np.array([tuple(e.vertices) for e in old0.data.edges]))}
REST0 = {}
for k_, (o_, e_) in ARMS.items():
    r_ = deformed(o_)
    l_ = np.linalg.norm(r_[e_[:, 0]] - r_[e_[:, 1]], axis=1)
    REST0[k_] = (l_, l_ > 0.002)
stretch = {}
if carm is not None:
    if carm.animation_data is None:
        carm.animation_data_create()
    acts = sorted({a for a in bpy.data.actions}, key=lambda a: a.name)
    for act in acts:
        carm.animation_data.action = act
        if getattr(carm.animation_data, "action_slot", None) is None and getattr(act, "slots", None):
            carm.animation_data.action_slot = act.slots[0]
        f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
        step = max(1, (f1 - f0) // 12)
        hi, lo, chi = 1.0, 1.0, 1.0
        for f in range(f0, f1 + 1, step):
            scene.frame_set(f)
            for pb in rig.pose.bones:
                cb = carm.pose.bones.get(pb.name)
                if cb is not None:
                    pb.matrix_basis = cb.matrix_basis.copy()
            bpy.context.view_layer.update()
            dg = bpy.context.evaluated_depsgraph_get()
            Ds = {}
            for k_, (o_, e_) in ARMS.items():
                D = deformed(o_)
                Ds[k_] = D
                l_, ok_ = REST0[k_]
                r = np.linalg.norm(D[e_[:, 0]] - D[e_[:, 1]], axis=1)[ok_] / l_[ok_]
                if k_ == "new":
                    hi, lo = max(hi, float(np.percentile(r, 99.9))), min(lo, float(np.percentile(r, 0.1)))
                else:
                    chi = max(chi, float(np.percentile(r, 99.9)))
            if any(t_ in act.name for t_ in ("Attack", "Block")):
                prop_frame(Ds["new"], Ds["control_today_lod0"], props_check.setdefault(act.name.split("|")[-1], {}))
        stretch[act.name] = dict(max=round(hi, 3), min=round(lo, 3), control_today_lod0_max=round(chi, 3),
                                 frames=len(range(f0, f1 + 1, step)))
        print("[warrior_tex] clip %-28s edge stretch new max %.3f min %.3f | today's LOD0 max %.3f (99.9th / 0.1th pct, edges > 2 mm)"
              % (act.name, hi, lo, chi), flush=True)
        if act.name.split("|")[-1] in props_check:
            print("[warrior_tex] props %-22s %s" % (act.name.split("|")[-1], json.dumps({k: round(v, 4) for k, v in props_check[act.name.split("|")[-1]].items()})), flush=True)
        if "shot" in globals() and act.name.split("|")[-1] in ("AttackSlice", "Run", "Block"):
            fm = int(f0 + (0.5 if act.name.endswith("Run") else 0.45) * (f1 - f0))
            scene.frame_set(fm)
            for pb in rig.pose.bones:
                cb = carm.pose.bones.get(pb.name)
                if cb is not None:
                    pb.matrix_basis = cb.matrix_basis.copy()
            bpy.context.view_layer.update()
            tag = act.name.split("|")[-1].lower()
            for y_ in (0.0, 180.0, 135.0):
                shot("new_%s_%d" % (tag, y_), [newo], "TEXTURE", y_, zc, 2.6)
            shot("old_%s_0" % tag, [old0], "VERTEX", 0.0, zc, 2.6)
REP["clip_stretch"] = stretch
REP["props_check"] = {k: {a: round(b, 4) for a, b in v.items()} for k, v in props_check.items()}
if stretch:
    REP["stretch_summary"] = dict(new_max=max(v["max"] for v in stretch.values()),
                                  today_lod0_max=max(v["control_today_lod0_max"] for v in stretch.values()),
                                  new_min=min(v["min"] for v in stretch.values()),
                                  verdict="pass" if max(v["max"] for v in stretch.values()) <= 2.5 else "FAIL (over 2.5x)")
REP["open_edges"].update({"new_lod0": open_edges(newo.data), "new_lod1": open_edges(lod1.data), "today_lod0": open_edges(old0.data)})
log("stretch", REP.get("stretch_summary"), "open edges", REP["open_edges"])
for pb in rig.pose.bones:
    pb.matrix_basis = Matrix.Identity(4)
rig.data.pose_position = "REST"
if carm is not None:
    for o in clip_objs:
        bpy.data.objects.remove(o, do_unlink=True)
bpy.data.objects.remove(raw, do_unlink=True)
bpy.data.objects.remove(old0, do_unlink=True)
save()

# ------------------------------------------------------------------------------------------------ export + round trip
for o in scene.objects:
    o.select_set(o is rig or (o.type == "MESH" and o.name.startswith("WarriorTex_")))
bpy.context.view_layer.objects.active = rig
path = os.path.join(OUT, "WarriorTex.fbx")
r = bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"}, global_scale=1.0,
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, use_mesh_modifiers=False, mesh_smooth_type="FACE",
                             use_tspace=False, colors_type="SRGB", prioritize_active_color=True, add_leaf_bones=False,
                             primary_bone_axis="Y", secondary_bone_axis="X", use_armature_deform_only=False,
                             armature_nodetype="NULL", bake_anim=False, path_mode="STRIP", embed_textures=False,
                             use_custom_props=False)
REP["export"] = dict(path=path, result=list(r), bytes=os.path.getsize(path))
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
objs = import_fbx(path, False)
rig2 = next(o for o in objs if o.type == "ARMATURE")
diff = 0.0
for b in rig2.data.bones:
    if b.name in REST:
        diff = max(diff, float(np.abs(np.array(b.matrix_local) - np.array(REST[b.name])).max()))
meshes2 = {o.name: dict(tris=ntri(o), uv=len(o.data.uv_layers), colours=len(o.data.color_attributes)) for o in objs if o.type == "MESH"}
REP["round_trip"] = dict(bones=len(rig2.data.bones), bones_missing=sorted(set(REST) - {b.name for b in rig2.data.bones}),
                         max_bind_matrix_diff=round(diff, 6), meshes=meshes2)
log("round trip", REP["round_trip"])
save()
print("[warrior_tex] SUMMARY " + json.dumps(dict(raw=REP["raw"], align=REP["align"], lod0=REP["lod0"], texture=REP["texture"],
                                                 tris=REP["tris"], bones=REP["round_trip"]["bones"],
                                                 bind_diff=REP["round_trip"]["max_bind_matrix_diff"], skin=REP["skin"])), flush=True)
try:
    names = sorted(f for f in os.listdir(OUT) if f.endswith(".png") and (f.startswith("new_") or f.startswith("old_")))
    tiles = []
    for f in names:
        im = bpy.data.images.load(os.path.join(OUT, f))
        w_, h_ = im.size
        tiles.append(np.array(im.pixels[:], np.float32).reshape(h_, w_, 4)[::2, ::2])
    cols = 4
    th, tw = tiles[0].shape[:2]
    rows = (len(tiles) + cols - 1) // cols
    sheet = np.zeros((rows * th, cols * tw, 4), np.float32)
    sheet[..., 3] = 1.0
    for k, a_ in enumerate(tiles):
        r_, c_ = divmod(k, cols)
        sheet[(rows - 1 - r_) * th:(rows - r_) * th, c_ * tw:(c_ + 1) * tw] = a_
    sh = bpy.data.images.new("sheet", cols * tw, rows * th)
    sh.pixels.foreach_set(sheet.ravel())
    sh.filepath_raw = os.path.join(OUT, "sheet.png")
    sh.file_format = "PNG"
    sh.save()
    REP["sheet_order_left_to_right_top_down"] = names
except Exception as x:
    REP["sheet_error"] = repr(x)
save()
print("[warrior_tex] SHEET " + json.dumps(REP.get("sheet_order_left_to_right_top_down")), flush=True)
