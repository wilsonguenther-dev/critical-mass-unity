"""The shared KayKit 41-joint rig (U-009), taken unmodified from the CC0 KayKit Adventurers 1.0 Knight.glb.

We import the GLB, keep only the armature object `Rig` and its 76 actions, and delete every KayKit mesh. Joint names,
parents and rest positions are therefore exactly KayKit's, so the 76 KayKit clips drive any mesh built on it.
Blender space: Z up, the rig faces -Y, the character's left is +X, 1 unit = 1 m.
"""
import json
import os

import bpy

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_GLB = os.path.join(HERE, "kaykit", "Knight.glb")
RIG_NAME = "Rig"
EXPECTED_JOINTS = 41


def load_kaykit_rig(glb_path=None):
    glb_path = glb_path or os.environ.get("CM_KAYKIT_GLB", DEFAULT_GLB)
    if not os.path.isfile(glb_path):
        raise SystemExit(f"KayKit rig source not found: {glb_path}")
    bpy.ops.import_scene.gltf(filepath=glb_path)
    arm = bpy.data.objects.get(RIG_NAME)
    if arm is None or arm.type != "ARMATURE":
        raise SystemExit("Knight.glb has no armature named 'Rig'")
    for act in bpy.data.actions:
        act.use_fake_user = True          # keep the 76 clips through the purge below
    for ob in list(bpy.data.objects):
        if ob is not arm:
            bpy.data.objects.remove(ob, do_unlink=True)
    bpy.data.orphans_purge(do_local_ids=True, do_linked_ids=True, do_recursive=True)
    if len(arm.data.bones) != EXPECTED_JOINTS:
        raise SystemExit(f"expected {EXPECTED_JOINTS} joints, got {len(arm.data.bones)}")
    if arm.animation_data:
        arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.matrix_basis.identity()
    return arm


def joints(arm):
    """Rest joints in armature space: name -> (head, tail, parent)."""
    out = {}
    for b in arm.data.bones:
        out[b.name] = (b.head_local.copy(), b.tail_local.copy(), b.parent.name if b.parent else None)
    return out


def dump_joints(arm, path):
    data = {
        "source": "KayKit Adventurers 1.0, Characters/gltf/Knight.glb (CC0), armature 'Rig'",
        "space": "Blender armature space: Z up, faces -Y, character left = +X, metres",
        "count": len(arm.data.bones),
        "joints": [
            {"name": b.name, "parent": b.parent.name if b.parent else None,
             "head": [round(v, 4) for v in b.head_local], "tail": [round(v, 4) for v in b.tail_local]}
            for b in arm.data.bones
        ],
        "clips": sorted(a.name for a in bpy.data.actions),
    }
    with open(path, "w") as f:
        json.dump(data, f, indent=1)
        f.write("\n")


def pose_matrices(arm, action_name, frame):
    """Armature-space pose matrices of every bone at `frame` of `action_name`; the rig is reset to rest after."""
    scene = bpy.context.scene
    arm.animation_data_create()
    arm.animation_data.action = bpy.data.actions[action_name]
    scene.frame_set(frame)
    bpy.context.view_layer.update()
    mats = {pb.name: pb.matrix.copy() for pb in arm.pose.bones}
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.matrix_basis.identity()
    scene.frame_set(0)
    bpy.context.view_layer.update()
    return mats
