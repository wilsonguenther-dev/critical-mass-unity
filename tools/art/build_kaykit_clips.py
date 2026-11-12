# Builds Assets/_Game/Art/Characters/KayKit_Clips.fbx: the shared KayKit 41-joint rig "Rig" with the clips the
# Warrior and Miner views play (CC0, KayKit Adventurers 1.0 Knight.glb, copied into the art branches' _source~).
# Warrior.fbx and Miner.fbx (art v1) carry the rig but no clips; this file supplies them with the SAME FBX exporter
# recipe as the art builds (gltf-glb-fbx-pipeline), so bone axes and units match and the Generic clips bind by path.
# Usage: blender -b --factory-startup --python tools/art/build_kaykit_clips.py -- KNIGHT_GLB OUT_FBX
import sys
import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
src, out = argv[0], argv[1]
# KayKit clip name -> game clip name (the names the Unity views look up, ArtCatalog.Clip*).
CLIPS = {
    "Idle": "Idle",
    "Walking_A": "Walk",
    "Running_A": "Run",
    "1H_Melee_Attack_Chop": "Attack",
    "Interact": "Gather",
    "Hit_A": "Hit",
    "Death_A": "Death",
    "Cheer": "Cheer",
}

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
for a in bpy.data.actions:
    a.name = CLIPS[a.name]
    a.use_fake_user = True
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
