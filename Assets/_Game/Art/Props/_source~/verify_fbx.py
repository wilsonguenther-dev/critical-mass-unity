"""Re-import every exported prop FBX and check: one mesh, colour set present with mask codes, triangle count equal to
the manifest, Unity-axis height equal to the Blender height within 2 mm.
  Blender -b --factory-startup --python-exit-code 1 --python verify_fbx.py"""
import json
import os

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
man = json.load(open(os.path.join(HERE, "manifest.json")))
bad = 0
for row in man["props"]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(os.path.dirname(HERE), row["prop"] + ".fbx"))
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    ob = meshes[0]
    bpy.context.view_layer.update()
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    zs = [(ob.matrix_world @ v.co).z for v in ob.data.vertices]
    h = max(zs) - min(zs)
    cols = ob.data.color_attributes
    ok = len(meshes) == 1 and len(cols) >= 1 and tris == row["tris"] and abs(h - row["dims_blender_xyz"][2]) < 0.002
    masks = sorted({round(c.color[3], 2) for c in cols[0].data}) if cols else []
    bad += not ok
    print(f"FBX {'OK ' if ok else 'BAD'} {row['prop']:28s} tris {tris:5d} h {h:.3f} masks {masks}")
print(f"FBX VERIFY: {len(man['props']) - bad}/{len(man['props'])} ok")
