"""Blender run 1 (read-only on warrior.blend): dump the v7 Warrior in its Idle display pose for plain-Python fitting.
  cm-heavy blender ... Blender -b --factory-startup --python-exit-code 1 -P export_v7_pose.py -- <warrior.blend> <out.npz>
Writes per mesh: posed vertices (world), faces, deform weights (bone name, weight); per bone: rest matrix_local and the
Idle-frame-1 pose matrix (armature space); the armature world matrix. Nothing is saved back to the .blend.
"""
import sys
import bpy
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
blend, out = argv[0], argv[1]
bpy.ops.wm.open_mainfile(filepath=blend)
scene = bpy.context.scene
arm = bpy.data.objects["Rig"]
arm.animation_data.action = bpy.data.actions["Idle"]
scene.frame_set(1)
bpy.context.view_layer.update()
deform = [b.name for b in arm.data.bones if b.use_deform]
bones = [b.name for b in arm.data.bones]
data = {"bones": np.array(bones), "deform": np.array(deform),
        "arm_world": np.array(arm.matrix_world),
        "rest": np.array([np.array(arm.data.bones[n].matrix_local) for n in bones]),
        "pose": np.array([np.array(arm.pose.bones[n].matrix) for n in bones]),
        "parents": np.array([arm.data.bones[n].parent.name if arm.data.bones[n].parent else "" for n in bones])}
dg = bpy.context.evaluated_depsgraph_get()
names = []
for ob in bpy.data.objects:
    if ob.type != "MESH":
        continue
    ev = ob.evaluated_get(dg)
    me = ev.to_mesh()
    me.calc_loop_triangles()
    V = np.array([ev.matrix_world @ v.co for v in me.vertices], np.float32)
    F = np.array([t.vertices[:] for t in me.loop_triangles], np.int32)
    col = None
    src = ob.data   # colours from the original mesh (same vertex order; the evaluated copy may drop attributes)
    ca = src.color_attributes.get("Col") or (src.color_attributes[0] if len(src.color_attributes) else None)
    if ca is not None:
        raw = np.array([c.color_srgb[:] for c in ca.data], np.float32)
        if ca.domain == "CORNER":
            vi = np.array([l.vertex_index for l in src.loops])
            c = np.zeros((len(src.vertices), 4), np.float32)
            np.add.at(c, vi, raw)
            col = c / np.maximum(np.bincount(vi, minlength=len(src.vertices)), 1)[:, None]
        else:
            col = raw
        print("COLOURS", ob.name, ca.name, ca.domain, ca.data_type)
    gname = {g.index: g.name for g in ob.vertex_groups}
    W = np.zeros((len(ob.data.vertices), len(bones)), np.float32)
    for v in ob.data.vertices:
        for g in v.groups:
            nm = gname.get(g.group)
            if nm in deform:
                W[v.index, bones.index(nm)] = g.weight
    if len(me.vertices) != len(ob.data.vertices):
        print("WARN topology changed by modifiers on", ob.name)
    ev.to_mesh_clear()
    names.append(ob.name)
    data["V_" + ob.name], data["F_" + ob.name], data["W_" + ob.name] = V, F, W
    if col is not None:
        data["C_" + ob.name] = col
    print("MESH", ob.name, len(V), len(F), "weighted", int((W.sum(1) > 0).sum()))
data["meshes"] = np.array(names)
np.savez_compressed(out, **data)
print("ACTIONS", len(bpy.data.actions), [a.name for a in bpy.data.actions if any(k in a.name for k in ("Idle", "Walk", "Attack", "Chop", "Slice"))][:20])
print("DONE", out)
