"""Build every prop, export one FBX each to Assets/_Game/Art/Props/, write props.blend and manifest.json.

Run from anywhere:
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python-exit-code 1 \
      --python "Assets/_Game/Art/Props/_source~/build_props.py"
"""
import json
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
for m in [m for m in sys.modules if m.startswith("props_art")]:
    del sys.modules[m]
from props_art import core, props  # noqa: E402

OUT = os.path.dirname(HERE)          # Assets/_Game/Art/Props


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    col = bpy.data.collections.new("Props")
    bpy.context.scene.collection.children.link(col)
    rows, fails = [], []
    for order, (name, fn, ref) in enumerate(props.REGISTRY):
        ob = fn()
        assert ob.name == name, (ob.name, name)
        ob["order"] = order
        for c in ob.users_collection:
            c.objects.unlink(ob)
        col.objects.link(ob)
        tris = core.tri_count(ob)
        xs = [v.co.x for v in ob.data.vertices]
        ys = [v.co.y for v in ob.data.vertices]
        zs = [v.co.z for v in ob.data.vertices]
        dims = (max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))
        checks = {"tris": tris <= ob["budget"]}
        for key, axis in (("height", 2),):
            t = ob.get("target_" + key)
            if t:
                checks[key] = abs(dims[axis] - t) <= 0.1 * t
        tw = ob.get("target_width")
        if tw:
            checks["width"] = abs(max(dims[0], dims[1]) - tw) <= 0.1 * tw
        if ob["walkable"]:
            checks["walkable_under_0.3"] = dims[2] < 0.3
        row = dict(prop=name, fbx=f"Props/{name}.fbx", bible=ref, size_class=ob["size_class"], tris=tris,
                   budget=ob["budget"], dims_blender_xyz=[round(d, 3) for d in dims],
                   targets={k[7:]: ob[k] for k in ob.keys() if k.startswith("target_")},
                   walkable=bool(ob["walkable"]), outline_px=ob["outline_px"],
                   team_mask=any(round(c.color[3], 2) in (0.5, 0.75, 1.0) for c in ob.data.color_attributes["Col"].data),
                   checks=checks)
        rows.append(row)
        if not all(checks.values()):
            fails.append((name, {k: v for k, v in checks.items() if not v}, tris, row["dims_blender_xyz"]))
        core.export_fbx(os.path.join(OUT, name + ".fbx"), ob)
        ob.location.x = 0
    # lay them out in a row in the .blend for browsing
    x = 0.0
    for ob in col.objects:
        ob.location.x = x
        x += max(1.0, ob.dimensions.x + 0.5)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "props.blend"), compress=True)
    man = dict(generated_by="build_props.py", blender=bpy.app.version_string, unit="1 unit = 1 m = 1 cell",
               pivot="footprint centre on the ground (z = 0); front faces Blender -Y = Unity +Z",
               colour="vertex colour set `Col`: RGB = bible hex (sRGB), A = mask code "
                      "(0 fixed, 0.25 fixed glow, 0.5 team dark, 0.75 team glow, 1.0 team cloth)",
               outline="2.5 px Void Navy #18233B constant screen width; hull width 0 where A is 0.25 or 0.75; "
                       "props with outline_px 0 draw no hull at all",
               props=rows)
    with open(os.path.join(HERE, "manifest.json"), "w") as f:
        json.dump(man, f, indent=1)
    print(f"BUILD: {len(rows)} props, {len(fails)} with failing checks")
    for f_ in fails:
        print("FAIL", f_)
    for r in rows:
        print(f"ROW {r['prop']:28s} tris {r['tris']:5d}/{r['budget']:<5d} dims {r['dims_blender_xyz']}")


main()
