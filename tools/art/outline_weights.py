# Outline weights (art refresh 2, 2026-11-12): reads the Blender-only `outline_w` vertex group of every mesh in the open
# .blend (the Farmer's face features and globe glint carry 0 so the inverted hull draws no rings there) and writes the
# rest positions of the unlined vertices, in the mesh's own space converted to Unity axes (FBX -Z forward, Y up,
# bake_space_transform; measured on the Farmer's skinned mesh: unity = (x, z, y)), as JSON. FBX drops non-bone vertex groups, so ArtImportPostprocessor bakes
# this sidecar into UV4.x (1 lined, 0 unlined) by position match. "unlined" is flat x,y,z triples (JsonUtility form).
# Run: blender -b farmer.blend --factory-startup --python outline_weights.py -- <out.json>
import bpy, json, sys

out = sys.argv[sys.argv.index("--") + 1]
meshes = {}
for ob in bpy.data.objects:
    if ob.type != 'MESH':
        continue
    g = ob.vertex_groups.get("outline_w")
    if g is None:
        continue
    pts = []
    for v in ob.data.vertices:
        w = 1.0
        for ge in v.groups:
            if ge.group == g.index:
                w = ge.weight
        if w < 0.5:
            pts += [round(v.co.x, 5), round(v.co.z, 5), round(v.co.y, 5)]
    meshes[ob.name] = {"vertices": len(ob.data.vertices), "unlined": pts}
with open(out, "w") as f:
    json.dump({"source": bpy.data.filepath.split("/")[-1], "group": "outline_w", "axes": "unity(x,z,y)",
               "meshes": [{"name": k, "vertices": v["vertices"], "unlined": v["unlined"]} for k, v in sorted(meshes.items())]},
              f, separators=(",", ":"))
print("OUTLINE_W_OK", out, {k: len(v["unlined"]) // 3 for k, v in meshes.items()})
