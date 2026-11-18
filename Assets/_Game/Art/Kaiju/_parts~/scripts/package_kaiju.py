# Assemble the game Kaiju_Adult.fbx: the ORIGINAL KaijuRig (46 bones, rest pose) with its ten clips untouched, plus per type
# Hero / LOD0 / LOD1 / Shadow meshes (ELDER keeps the bare names Kaiju_Hero ...; others Kaiju_<Type>_<LOD>).
# blender -b --factory-startup --python package_kaiju.py
import bpy, bmesh, os, json, math, sys
from mathutils import Vector, Matrix
import numpy as np
ROOT = os.path.expanduser("~/cm-art-trial/parts-kaiju"); W = ROOT + "/work"; OUT = ROOT + "/out"; os.makedirs(OUT, exist_ok=True)
TYPES = ["Elder", "Grove", "Brute", "Timber", "Mire"]
TARGET = {"Hero": 24000, "LOD0": 9000, "LOD1": 1480, "Shadow": 1150}
PROTECT = {"Head", "Jaw"}
KW = dict(use_custom_normals=False, ignore_leaf_bones=False, automatic_bone_orientation=False, primary_bone_axis="Y", secondary_bone_axis="X")
def nm(t, lod): return "Kaiju" + ("" if t == "Elder" else "_" + t) + "_" + lod

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=W + "/Kaiju_Adult_ORIG.fbx", use_anim=True, **KW)
rig = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
for o in list(bpy.data.objects):
    if o.type == 'MESH': bpy.data.objects.remove(o, do_unlink=True)
BONES = [b.name for b in rig.data.bones]
rep = {"rig": rig.name, "bones": len(BONES), "meshes": {}}

def tris(ob): ob.data.calc_loop_triangles(); return len(ob.data.loop_triangles)

def append(t, lod):
    with bpy.data.libraries.load(f"{W}/{t}/{lod}/Kaiju.blend") as (src, dst):
        dst.objects = [n for n in src.objects if n.startswith("Kaiju")]
    ob = [o for o in dst.objects if o and o.type == 'MESH'][0]
    bpy.context.scene.collection.objects.link(ob)
    ob.name = ob.data.name = nm(t, lod)
    ob.parent = rig; ob.matrix_parent_inverse = Matrix.Identity(4); ob.matrix_basis = Matrix.Identity(4)
    for md in list(ob.modifiers): ob.modifiers.remove(md)
    md = ob.modifiers.new("Armature", 'ARMATURE'); md.object = rig
    return ob

def islands(me):
    par = list(range(len(me.vertices)))
    def f(i):
        while par[i] != i: par[i] = par[par[i]]; i = par[i]
        return i
    for e in me.edges:
        a_, b_ = f(e.vertices[0]), f(e.vertices[1])
        if a_ != b_: par[a_] = b_
    out = {}
    for i in range(len(me.vertices)): out.setdefault(f(i), []).append(i)
    return list(out.values())

def keep_small(ob):
    """Eyes, sockets, nostrils, teeth: small islands on Head/Jaw kept whole at every LOD (the face must survive)."""
    me = ob.data; col = me.color_attributes["Col"]; keep = set(); vl = {}
    gi = {ob.vertex_groups[n].index for n in PROTECT if n in ob.vertex_groups}
    for p in me.polygons:
        for li, vi in zip(p.loop_indices, p.vertices): vl[vi] = col.data[li].color_srgb
    for isl in islands(me):
        if len(isl) > 200 or me.vertices[isl[0]].groups[0].group not in gi: continue
        c = vl.get(isl[0]); code = round(c[3] * 8); lum = sum(c[:3]) / 3
        if code == 6 or lum < 0.2: keep.update(isl)
    return keep

def decimate(ob, target):
    n = tris(ob)
    if n <= target: return n
    names = [g.name for g in ob.vertex_groups]
    prot = {ob.vertex_groups[x].index for x in PROTECT if x in ob.vertex_groups}
    small = keep_small(ob); me = ob.data
    def run(r_rest, r_prot):
        bm = bmesh.new(); bm.from_mesh(me); out = []
        for want, r in ((0, 1.0), (1, r_prot), (2, r_rest)):
            b2 = bm.copy(); d2 = b2.verts.layers.deform.active; b2.verts.ensure_lookup_table()
            def bucket(v): return 0 if v.index in small else (1 if next(iter(v[d2].keys())) in prot else 2)
            kill = [v for v in b2.verts if bucket(v) != want]
            bmesh.ops.delete(b2, geom=kill, context='VERTS')
            m2 = bpy.data.meshes.new("_d"); b2.to_mesh(m2); b2.free()
            o2 = bpy.data.objects.new("_d", m2); bpy.context.scene.collection.objects.link(o2)
            for x in names: o2.vertex_groups.new(name=x)
            if r < 0.999 and len(m2.polygons):
                md = o2.modifiers.new("dec", 'DECIMATE'); md.decimate_type = 'COLLAPSE'; md.ratio = r; md.use_collapse_triangulate = True
                dg = bpy.context.evaluated_depsgraph_get()
                mn = bpy.data.meshes.new_from_object(o2.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
                o2.modifiers.remove(md); old = o2.data; o2.data = mn; bpy.data.meshes.remove(old)
            out.append(o2)
        bm.free(); bm = bmesh.new()
        for o2 in out: bm.from_mesh(o2.data)
        res = bpy.data.meshes.new("_r"); bm.to_mesh(res); bm.free()
        for o2 in out: m2 = o2.data; bpy.data.objects.remove(o2); bpy.data.meshes.remove(m2)
        res.calc_loop_triangles(); return res
    ns = sum(1 for _ in small)
    r = target / n
    for it in range(10):
        res = run(r, min(1.0, r * 1.5)); k = len(res.loop_triangles)
        if k <= target or it == 9: break
        bpy.data.meshes.remove(res); r *= (target / k) * 0.97
    for p in res.polygons: p.use_smooth = True
    res.set_sharp_from_angle(angle=math.radians(38))
    old = ob.data; mats = list(old.materials); ob.data = res; res.name = ob.name
    if len(ob.vertex_groups) != len(names):          # group names live on the mesh: re-add them in the same order
        for x in names: ob.vertex_groups.new(name=x)
    assert [g.name for g in ob.vertex_groups] == names
    for m in mats: res.materials.append(m)
    bpy.data.meshes.remove(old)
    print("DECIMATE", ob.name, n, "->", k)
    return k

def shadow_from(src, name):
    """Shadow caster: per bone a convex hull of that bone's LOD1 parts, inset 3 cm, joined. The Head hull uses only the
    skull-sized parts that do not reach in front of the eyes, so the face never sits inside the caster (class lesson)."""
    INSET = 0.03; me = src.data; groups = {}
    head = src.vertex_groups["Head"].index
    for v in me.vertices:
        if v.groups[0].group != head: groups.setdefault(v.groups[0].group, []).append(v.co.copy())
    hp = []
    for isl in islands(me):
        if me.vertices[isl[0]].groups[0].group != head: continue
        P = [me.vertices[i].co for i in isl]
        miny = min(p.y for p in P); ext = max((max(p[k] for p in P) - min(p[k] for p in P)) for k in range(3))
        if ext > 0.5 and miny > -1.80 and max(p.z for p in P) < 3.9: hp += [p.copy() for p in P]
    if hp: groups[head] = hp
    bm = bmesh.new(); dl = bm.verts.layers.deform.verify()
    for gi, pts in groups.items():
        if len(pts) < 4: continue
        b2 = bmesh.new()
        for p in pts: b2.verts.new(p)
        r = bmesh.ops.convex_hull(b2, input=list(b2.verts))
        kill = list({g for g in r["geom_interior"] + r["geom_unused"] if isinstance(g, bmesh.types.BMVert)})
        if kill: bmesh.ops.delete(b2, geom=kill, context='VERTS')
        if not b2.verts: b2.free(); continue
        c = sum((v.co for v in b2.verts), Vector()) / len(b2.verts)
        for v in b2.verts:
            d = v.co - c; L = d.length
            if L > 1e-6: v.co = c + d * max(0.55, 1 - INSET / L)
        m2 = bpy.data.meshes.new("_h"); b2.to_mesh(m2); b2.free()
        n0 = len(bm.verts); bm.from_mesh(m2); bpy.data.meshes.remove(m2); bm.verts.ensure_lookup_table()
        for v in bm.verts[n0:]: v[dl][gi] = 1.0
    sm = bpy.data.meshes.new(name); bm.to_mesh(sm); bm.free()
    ob = bpy.data.objects.new(name, sm); bpy.context.scene.collection.objects.link(ob)
    ob.parent = rig; ob.matrix_parent_inverse = Matrix.Identity(4)
    for x in [g.name for g in src.vertex_groups]: ob.vertex_groups.new(name=x)
    for p in sm.polygons: p.use_smooth = False
    sm.uv_layers.new(name="UVMap")
    ca = sm.color_attributes.new("Col", 'BYTE_COLOR', 'CORNER')
    for d in ca.data: d.color_srgb = (0.35, 0.35, 0.38, 0.0)
    sm.color_attributes.active_color = ca
    md = ob.modifiers.new("Armature", 'ARMATURE'); md.object = rig
    return ob

mat = bpy.data.materials.new("CM_Toon")
ALL = []
for t in TYPES:
    obs = {lod: append(t, lod) for lod in ("Hero", "LOD0", "LOD1")}
    for lod in ("Hero", "LOD0", "LOD1"):
        rep["meshes"][obs[lod].name] = {"built": tris(obs[lod])}
        decimate(obs[lod], TARGET[lod])
    sh = shadow_from(obs["LOD1"], nm(t, "Shadow")); decimate(sh, TARGET["Shadow"]); obs["Shadow"] = sh
    for lod, ob in obs.items():
        ob.data.materials.clear(); ob.data.materials.append(mat)
        ca = ob.data.color_attributes["Col"]; ob.data.color_attributes.active_color = ca
        ob.data.color_attributes.render_color_index = ob.data.color_attributes.active_color_index
        for x in BONES:
            if x not in ob.vertex_groups: ob.vertex_groups.new(name=x)
        me = ob.data
        bad = sum(1 for v in me.vertices if len([g for g in v.groups if g.weight > 1e-4]) != 1 or abs(v.groups[0].weight - 1) > 1e-4)
        codes = {}
        for c in ca.data: q = round(c.color[3] * 8); codes[q] = codes.get(q, 0) + 1
        zs = [v.co.z for v in me.vertices]
        rep["meshes"].setdefault(ob.name, {}).update({"tris": tris(ob), "verts": len(me.vertices), "not_rigid": bad, "codes": codes,
                                                      "zmin": round(min(zs), 3), "zmax": round(max(zs), 3)})
        ALL.append(ob)
    copied = None
    import shutil
    for f in os.listdir(f"{W}/{t}/Hero"):
        if f.endswith("_Palette.png") or f.endswith("_TeamMask.png"): shutil.copy(f"{W}/{t}/Hero/{f}", f"{OUT}/{f}")

# clips: one NLA track/strip per action named as the clip (the take name Unity reads), exactly like kr2_build.py
ad = rig.animation_data or rig.animation_data_create(); ad.action = None
for tr in list(ad.nla_tracks): ad.nla_tracks.remove(tr)
clips = {}
for a in sorted(bpy.data.actions, key=lambda a: a.name):
    cn = a.name.split("|")[-1]; clips[cn] = list(a.frame_range)
    tr = ad.nla_tracks.new(); tr.name = cn
    st = tr.strips.new(cn, int(a.frame_range[0]), a); st.name = cn
    try:
        if a.slots and st.action_slot is None: st.action_slot = a.slots[0]
    except Exception: pass
rep["clips"] = clips
for pb in rig.pose.bones: pb.matrix_basis = Matrix.Identity(4)
KAIJU_FBX = dict(object_types={'ARMATURE', 'MESH'}, global_scale=1.0, apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_space_transform=False,
    use_mesh_modifiers=False, mesh_smooth_type='FACE', use_tspace=False, colors_type='SRGB', prioritize_active_color=True,
    add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', use_armature_deform_only=False, bake_anim=True,
    bake_anim_use_all_bones=True, bake_anim_use_nla_strips=True, bake_anim_use_all_actions=False,
    bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=1.0, path_mode='STRIP',
    embed_textures=False)
for o in bpy.context.view_layer.objects: o.select_set(o == rig or o in ALL)
bpy.context.view_layer.objects.active = rig
fbx = OUT + "/Kaiju_Adult.fbx"
r = bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, **KAIJU_FBX)
rep["export"] = {"result": list(r), "bytes": os.path.getsize(fbx)}
bpy.ops.wm.save_as_mainfile(filepath=W + "/Kaiju_game.blend")

# ------------------------------------------------------------------ round trip: new file vs original, rest + animated bones
def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path, use_anim=True, **KW)
    rg = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    return rg
def sample(rg):
    acts = {a.name.split("|")[-1]: a for a in bpy.data.actions}; out = {}
    ad = rg.animation_data
    for cn, a in sorted(acts.items()):
        ad.action = a
        if a.slots: ad.action_slot = a.slots[0]
        f0, f1 = int(a.frame_range[0]), int(a.frame_range[1])
        for f in sorted({f0, (f0 + f1) // 3, 2 * (f0 + f1) // 3, f1}):
            bpy.context.scene.frame_set(f)
            out[(cn, f)] = {b.name: (rg.matrix_world @ b.head).copy() for b in rg.pose.bones}
    return out, {a.split("|")[-1]: list(acts[a].frame_range) for a in acts}
ro = load(W + "/Kaiju_Adult_ORIG.fbx"); so, ro_clips = sample(ro)
rest_o = {b.name: b.matrix_local.copy() for b in ro.data.bones}
rn = load(fbx); sn, rn_clips = sample(rn)
rest_n = {b.name: b.matrix_local.copy() for b in rn.data.bones}
mx = 0.0
for k in so:
    if k not in sn: mx = 99; continue
    for b in so[k]: mx = max(mx, (so[k][b] - sn[k][b]).length)
rep["roundtrip"] = {"clips_orig": ro_clips, "clips_new": rn_clips, "anim_bone_maxdiff_m": round(mx, 6),
                    "rest_maxdiff": round(max(max(abs(x - y) for ra, rb in zip(rest_o[n], rest_n[n]) for x, y in zip(ra, rb)) for n in rest_o), 6),
                    "bones_new": len(rn.data.bones), "meshes_new": {}}
for o in bpy.data.objects:
    if o.type == 'MESH':
        o.data.calc_loop_triangles()
        rep["roundtrip"]["meshes_new"][o.name] = {"tris": len(o.data.loop_triangles), "groups": len(o.vertex_groups),
                                                  "cols": [c.name for c in o.data.color_attributes], "parent": o.parent.name if o.parent else None}
json.dump(rep, open(OUT + "/package_report.json", "w"), indent=1)
print("PACKAGE", json.dumps({k: v for k, v in rep.items() if k not in ("meshes", "roundtrip")}))
print("ROUNDTRIP", json.dumps({k: v for k, v in rep["roundtrip"].items() if k != "meshes_new"}))
for k, v in rep["meshes"].items(): print("MESH", k, json.dumps(v))
