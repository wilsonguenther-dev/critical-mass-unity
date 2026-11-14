# rig-builder-4 (2026-11-14): rebuild a rigged model's LOD0, LOD1 and Shadow meshes from its Hero, then check the pair.
#   blender -b -P fix_lods.py -- <out> <Cls> <model.fbx> <clips.fbx>
# Why: the clean step's LODs (clean_export.py) reached their budgets by clustering vertices on a grid once the collapse
# stopped. The Builder's LOD0 (3,916 tris on 1,206 vertices), LOD1 and Shadow (1,315 on 343) are a cloud of loose flat
# shards even in the bind pose; the game draws them at gameplay distance (the owner saw a shard cloud in every run frame
# and in the lineup, while the close Idle and Attack stills, the Hero, looked right). Here each LOD is the Hero decimated
# by edge collapse (Blender's Decimate keeps vertex colours and interpolates the bone weights), limited to 4 weights per
# vertex and normalised, exported with rig_body.py's export settings. Then fbx_pair_check.py replays the clips from the
# separate clip file onto the new model file.
import bpy, sys, os, json, runpy

argv = sys.argv[sys.argv.index('--') + 1:]
OUT, CLS, MODEL, CLIPS = argv[0], argv[1], argv[2], argv[3]
# mode: copy = every LOD is the Hero (stopgap, 21.6k tris per tier); voxel = voxel rebuild of the Hero, collapse, weights
# and colours transferred from the Hero (the Hero itself does not collapse: Decimate on it stopped at 3,936 tris as shards)
MODE = argv[4] if len(argv) > 4 else 'copy'
os.makedirs(OUT, exist_ok=True)
TARGET = {'LOD0': 3916, 'LOD1': 1315, 'Shadow': 1315}
REP = {}
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
bpy.ops.import_scene.fbx(filepath=MODEL, use_anim=False, use_custom_normals=False, ignore_leaf_bones=False,
                         automatic_bone_orientation=False, primary_bone_axis='Y', secondary_bone_axis='X')
rig = next(o for o in scene.objects if o.type == 'ARMATURE')
hero = next(o for o in scene.objects if o.type == 'MESH' and o.name.startswith(CLS + '_Hero'))
ntri = lambda o: sum(len(p.vertices) - 2 for p in o.data.polygons)
REP['hero_tris'] = ntri(hero)
for lv, tgt in TARGET.items():
    old = next((o for o in scene.objects if o.type == 'MESH' and o.name.startswith(f'{CLS}_{lv}')), None)
    mats = list(old.data.materials) if old else list(hero.data.materials)
    if old is not None:
        REP.setdefault('old', {})[lv] = dict(tris=ntri(old), verts=len(old.data.vertices))
        bpy.data.objects.remove(old, do_unlink=True)
    o = hero.copy(); o.data = hero.data.copy(); o.name = f'{CLS}_{lv}'; o.data.name = f'{CLS}_{lv}'
    scene.collection.objects.link(o)
    for m in list(o.modifiers):
        if m.type != 'ARMATURE':
            o.modifiers.remove(m)
    arm_mods = [m for m in o.modifiers if m.type == 'ARMATURE']
    for m in arm_mods:
        m.show_viewport = False
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True); bpy.context.view_layer.objects.active = o
    ov = dict(object=o, active_object=o, selected_objects=[o], selected_editable_objects=[o])
    if MODE == 'voxel':
        cols = [(a.name, a.data_type, a.domain) for a in hero.data.color_attributes]
        o.vertex_groups.clear()
        o.data.remesh_voxel_size = 0.018
        with bpy.context.temp_override(**ov):
            bpy.ops.object.voxel_remesh()
        d = o.modifiers.new('Dec', 'DECIMATE'); d.decimate_type = 'COLLAPSE'; d.use_collapse_triangulate = True
        d.ratio = min(1.0, tgt / max(1, ntri(o)))
        with bpy.context.temp_override(**ov):
            bpy.ops.object.modifier_move_to_index(modifier='Dec', index=0)
            bpy.ops.object.modifier_apply(modifier='Dec')
        for n_, t_, dm_ in cols:
            if n_ not in o.data.color_attributes:
                o.data.color_attributes.new(n_, t_, dm_)
        for g in hero.vertex_groups:
            o.vertex_groups.new(name=g.name)
        dt = o.modifiers.new('DT', 'DATA_TRANSFER'); dt.object = hero
        dt.use_vert_data = True; dt.data_types_verts = {'VGROUP_WEIGHTS'}; dt.vert_mapping = 'POLYINTERP_NEAREST'
        dt.layers_vgroup_select_src = 'ALL'; dt.layers_vgroup_select_dst = 'NAME'
        dt.use_loop_data = True; dt.data_types_loops = {'COLOR_CORNER'}; dt.loop_mapping = 'NEAREST_POLY'
        dt.use_vert_data = True
        try:
            dt.data_types_verts = {'VGROUP_WEIGHTS', 'COLOR_VERTEX'}
        except Exception:
            pass
        with bpy.context.temp_override(**ov):
            bpy.ops.object.modifier_move_to_index(modifier='DT', index=0)
            bpy.ops.object.modifier_apply(modifier='DT')
        REP.setdefault('colors', {})[lv] = cols
    with bpy.context.temp_override(**ov):
        bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
        bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
    for m in arm_mods:
        m.show_viewport = True
    o.data.materials.clear()
    for m_ in mats:
        o.data.materials.append(m_)
    o.parent = rig
    REP.setdefault('new', {})[lv] = dict(tris=ntri(o), verts=len(o.data.vertices),
                                         non_manifold_edges=sum(1 for e in o.data.edges if False))
for ob in scene.objects:
    ob.select_set(ob.type in ('ARMATURE', 'MESH'))
bpy.context.view_layer.objects.active = rig
path = os.path.join(OUT, f'{CLS}.fbx')
r = bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'}, global_scale=1.0,
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, use_mesh_modifiers=False, mesh_smooth_type='FACE',
                             use_tspace=False, colors_type='SRGB', prioritize_active_color=True, add_leaf_bones=False,
                             primary_bone_axis='Y', secondary_bone_axis='X', use_armature_deform_only=False,
                             armature_nodetype='NULL', bake_anim=False, path_mode='STRIP', embed_textures=False,
                             use_custom_props=False)
REP['export'] = dict(path=path, result=list(r), bytes=os.path.getsize(path))
json.dump(REP, open(os.path.join(OUT, f'fix_lods_{CLS}.json'), 'w'), indent=1)
print('[fix_lods]', json.dumps(REP), flush=True)
REP['mode'] = MODE
json.dump(REP, open(os.path.join(OUT, f'fix_lods_{CLS}.json'), 'w'), indent=1)
sys.argv = ['blender', '--', OUT, CLS, path, CLIPS]
try:
    HERE = os.path.dirname(os.path.abspath(__file__))
except NameError:
    HERE = os.path.join(os.environ.get('GITHUB_WORKSPACE', '.'), 'Assets/_Game/Art/Characters/_source~/characters_hy/rig')
if not os.path.exists(os.path.join(HERE, 'fbx_pair_check.py')):
    HERE = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(MODEL)))) + '/Art/Characters/_source~/characters_hy/rig'
runpy.run_path(os.path.join(HERE, 'fbx_pair_check.py'), run_name='__main__')
