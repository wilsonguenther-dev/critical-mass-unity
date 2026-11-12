"""FBX export with Unity-correct axes and scale.

Settings (the widely used Blender -> Unity recipe; recorded here because ARCHITECTURE.md section 6 leaves them OPEN):
  apply_scale_options = FBX_SCALE_ALL   -> Unity sees scale 1 with "Convert Units" on: 1 Blender unit = 1 m = 1 Unity unit
  axis_forward = -Z, axis_up = Y        -> Blender +Z becomes Unity +Y (up); the rig faces Blender -Y, which lands on
                                           Unity +Z (forward)
  bake_space_transform = True           -> the axis change is baked into the data, so the root has no -90 X rotation
  add_leaf_bones = False                -> exactly the 41 KayKit joints, no extra "_end" bones
  bake_anim = False                     -> mesh + rig only; the 76 clips stay in the KayKit sources (see README)
  colors_type = SRGB                    -> vertex colours arrive as the bible hex; alpha carries the team-mask code
  mesh_smooth_type = FACE               -> per-face smoothing (flat stone, smooth cloth/skin) survives import
"""
import bpy

FBX_OPTS = dict(
    use_selection=True,
    object_types={"ARMATURE", "MESH"},
    use_mesh_modifiers=False,
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL",
    global_scale=1.0,
    axis_forward="-Z",
    axis_up="Y",
    bake_space_transform=True,
    mesh_smooth_type="FACE",
    use_tspace=False,
    colors_type="SRGB",
    prioritize_active_color=True,
    add_leaf_bones=False,
    primary_bone_axis="Y",
    secondary_bone_axis="X",
    use_armature_deform_only=False,
    armature_nodetype="NULL",
    bake_anim=False,
    use_custom_props=False,
    path_mode="AUTO",
    embed_textures=False,
)


def export_fbx(path, arm, meshes):
    for ob in bpy.context.view_layer.objects:
        ob.select_set(False)
    arm.select_set(True)
    for m in meshes:
        m.select_set(True)
    bpy.context.view_layer.objects.active = arm
    prev = arm.data.pose_position
    arm.data.pose_position = "REST"
    known = set(bpy.ops.export_scene.fbx.get_rna_type().properties.keys())
    missing = [k for k in FBX_OPTS if k not in known]
    if missing:
        raise SystemExit(f"FBX exporter lacks options {missing}; Blender version drift?")
    res = bpy.ops.export_scene.fbx(filepath=path, **FBX_OPTS)
    arm.data.pose_position = prev
    if "FINISHED" not in res:
        raise SystemExit(f"FBX export failed: {res}")
