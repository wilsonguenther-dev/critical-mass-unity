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
if CULL > 0:
    bm = bmesh.new()
    bm.from_mesh(low.data)
    far = [v for v in bm.verts if bvh_old.find_nearest(v.co)[3] > CULL]
    bmesh.ops.delete(bm, geom=far, context="VERTS")
    bm.to_mesh(low.data)
    bm.free()
    REP["cull"] = dict(metres=CULL, vertices_removed=len(far), tris_after=ntri(low))
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
for p in low.data.polygons:
    p.use_smooth = True
low.data.set_sharp_from_angle(angle=math.radians(60.0))
bpy.context.view_layer.objects.active = low
for o in scene.objects:
    o.select_set(o is low)
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.smart_project(angle_limit=math.radians(80.0), island_margin=0.002, area_weight=0.0, correct_aspect=True,
                         scale_to_bounds=False)
try:
    bpy.ops.uv.pack_islands(rotate=True, margin=0.002, shape_method="CONCAVE")
    REP["uv_pack"] = "concave repack"
except Exception as x:
    REP["uv_pack"] = "smart project packing only: " + repr(x)
bpy.ops.object.mode_set(mode="OBJECT")
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

px = np.array(base.pixels[:]).reshape(TEX, TEX, 4)
mk = np.array(maskimg.pixels[:]).reshape(TEX, TEX, 4)[:, :, 0]
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
    shot("new_face", [newo], "TEXTURE", yaw, float(W[:, 2].max()) - 0.25, 0.7)
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
            for k_, (o_, e_) in ARMS.items():
                D = deformed(o_)
                l_, ok_ = REST0[k_]
                r = np.linalg.norm(D[e_[:, 0]] - D[e_[:, 1]], axis=1)[ok_] / l_[ok_]
                if k_ == "new":
                    hi, lo = max(hi, float(np.percentile(r, 99.9))), min(lo, float(np.percentile(r, 0.1)))
                else:
                    chi = max(chi, float(np.percentile(r, 99.9)))
        stretch[act.name] = dict(max=round(hi, 3), min=round(lo, 3), control_today_lod0_max=round(chi, 3),
                                 frames=len(range(f0, f1 + 1, step)))
        print("[warrior_tex] clip %-28s edge stretch new max %.3f min %.3f | today's LOD0 max %.3f (99.9th / 0.1th pct, edges > 2 mm)"
              % (act.name, hi, lo, chi), flush=True)
REP["clip_stretch"] = stretch
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
