"""L2a: Blender bone-heat weights for prepared variants (mesh in its bind pose + bone segments in the same space).

blender -b --factory-startup --python-exit-code 1 -P heat.py -- <in_dir> <out_dir>
Each <in_dir>/heat_in_<name>.npz: V (n,3), T (m,3), names (k,), heads (k,3), tails (k,3). Writes heat_out_<name>.npz: W (n,k).
"""
import glob
import json
import os
import sys
import time

import bpy
import numpy as np

T0 = time.time()
IN, OUT = sys.argv[sys.argv.index("--") + 1:][:2]
os.makedirs(OUT, exist_ok=True)
rep = {}
for path in sorted(glob.glob(os.path.join(IN, "heat_in_*.npz"))):
    name = os.path.basename(path)[8:-4]
    z = np.load(path)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    V, T = z["V"], z["T"]
    names = [str(n) for n in z["names"]]
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(map(float, v)) for v in V], [], [tuple(map(int, t)) for t in T])
    me.update()
    ob = bpy.data.objects.new(name, me)
    scene.collection.objects.link(ob)
    ad = bpy.data.armatures.new("A")
    arm = bpy.data.objects.new("A", ad)
    scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for n, h, t in zip(names, z["heads"], z["tails"]):
        e = ad.edit_bones.new(n)
        e.head = tuple(map(float, h))
        e.tail = tuple(map(float, t))
        e.use_deform = True
    bpy.ops.object.mode_set(mode="OBJECT")
    for o in scene.objects:
        o.select_set(o in (ob, arm))
    bpy.context.view_layer.objects.active = arm
    t1 = time.time()
    with bpy.context.temp_override(active_object=arm, object=arm, selected_objects=[ob, arm], selected_editable_objects=[ob, arm]):
        r = bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    W = np.zeros((len(V), len(names)), np.float32)
    gi = {g.index: names.index(g.name) for g in ob.vertex_groups if g.name in names}
    for v in me.vertices:
        for e in v.groups:
            if e.group in gi:
                W[v.index, gi[e.group]] = e.weight
    np.savez_compressed(os.path.join(OUT, f"heat_out_{name}.npz"), W=W)
    rep[name] = dict(result=str(r), secs=round(time.time() - t1, 1), unweighted=int((W.sum(1) < 1e-6).sum()), verts=len(V),
                     per_bone={n: int((W.argmax(1) == i).sum()) for i, n in enumerate(names)})
    print("HEAT", name, json.dumps(rep[name]), flush=True)
json.dump(rep, open(os.path.join(OUT, "heat_report.json"), "w"), indent=1)
print("HEAT_ALL_DONE", round(time.time() - T0, 1), flush=True)
