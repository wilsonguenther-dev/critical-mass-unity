# Builds Assets/_Game/Art/Characters/KayKit_Clips.fbx: the shared KayKit 41-joint rig "Rig" with the clips the
# character views play (32 of the 76 since the animation pass, 2026-11-12; was 8) (CC0, KayKit Adventurers 1.0 Knight.glb, copied into the art branches' _source~).
# Warrior.fbx and Miner.fbx (art v1) carry the rig but no clips; this file supplies them with the SAME FBX exporter
# recipe as the art builds (gltf-glb-fbx-pipeline), so bone axes and units match and the Generic clips bind by path.
# Usage: blender -b --factory-startup --python tools/art/build_kaykit_clips.py -- KNIGHT_GLB OUT_FBX
import sys
import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
src, out = argv[0], argv[1]
# KayKit clip name -> game clip name (the names the Unity views look up, ArtMapping clip constants). The table lives in
# kaykit_clip_map.tsv next to this script, shared with measure_clips.py (which writes Resources/Anim/clip_facts.json).
import os
_MAP = os.path.join(os.path.dirname(os.path.abspath(__file__)), "kaykit_clip_map.tsv")
CLIPS = {}
for _line in open(_MAP, encoding="utf-8"):
    if _line.strip() and not _line.startswith("#"):
        _k, _g = _line.rstrip("\n").split("\t")[:2]
        CLIPS[_k] = _g

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src, disable_bone_shape=True)
rig = bpy.data.objects.get("Rig")
assert rig is not None and rig.type == 'ARMATURE', "Knight.glb has no armature named Rig"
for o in list(bpy.context.scene.objects):
    if o is not rig:
        bpy.data.objects.remove(o, do_unlink=True)
for a in list(bpy.data.actions):
    if a.name not in CLIPS:
        bpy.data.actions.remove(a)
missing = [k for k in CLIPS if k not in bpy.data.actions]
assert not missing, f"Knight.glb lacks clips {missing}"
# Two-phase rename over a snapshot: renaming re-sorts bpy.data.actions, and a game name may equal another source name.
_acts = [(a, CLIPS[a.name]) for a in list(bpy.data.actions)]
for i, (a, _g) in enumerate(_acts):
    a.name = "__cm_clip_%02d" % i
for a, g in _acts:
    a.name = g
    a.use_fake_user = True
assert sorted(a.name for a in bpy.data.actions) == sorted(set(CLIPS.values())), "clip rename collided"
rig.animation_data_create()
rig.animation_data.action = bpy.data.actions["Idle"]
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(
    filepath=out, use_selection=True, object_types={'ARMATURE'},
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
    axis_forward='-Z', axis_up='Y', bake_space_transform=True,
    add_leaf_bones=False, use_armature_deform_only=False, armature_nodetype='NULL',
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, path_mode='AUTO', embed_textures=False)
print("KAYKIT_CLIPS_OK", out, sorted(a.name for a in bpy.data.actions))
