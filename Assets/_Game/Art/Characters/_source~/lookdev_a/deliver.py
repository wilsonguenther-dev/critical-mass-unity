"""Copy the deliverables to the Obsidian folder and print the numbers the report quotes. deliver.py <out> <work> <renders> <dest>"""
import json
import os
import shutil
import sys

OUT, WORK, REN, DEST = (os.path.abspath(os.path.expanduser(a)) for a in sys.argv[1:5])
os.makedirs(DEST, exist_ok=True)
for n in sorted(os.listdir(OUT)):
    if n.endswith((".jpg", ".json")):
        shutil.copy2(os.path.join(OUT, n), os.path.join(DEST, n))
for n in ("mesh_stats.json",):
    shutil.copy2(os.path.join(WORK, n), os.path.join(DEST, n))
shutil.copy2(os.path.join(REN, "assemble_report.json"), os.path.join(DEST, "assemble_report.json"))
m = json.load(open(os.path.join(WORK, "mesh_stats.json")))
a = json.load(open(os.path.join(REN, "assemble_report.json")))
i = json.load(open(os.path.join(OUT, "iou.json")))
print("MESH", json.dumps({k: v for k, v in m.items() if k != "objects"}))
for k, v in m["objects"].items():
    print("  ", k, json.dumps(v))
print("TRIS", a.get("tris"), "bones", a.get("bones"), "game px", a.get("game_feet_to_top_px"))
print("FACING", a.get("facing"))
print("FBX", a["fbx"]["bytes"], {k: (v["tris"], v["uv_layers"], v["custom_normals"], v["vertex_groups"]) for k, v in a.get("fbx_round_trip", {}).get("objects", {}).items()},
      "bones", a.get("fbx_round_trip", {}).get("armature_bones"))
for tag, d in a["deform"].items():
    for clip, row in d.items():
        b = [v for k, v in row.items() if k.endswith("_Body")][0]
        print("DEFORM", tag, clip, {k: b[k] for k in ("edge_ratio_p01", "edge_ratio_p99", "edge_ratio_max", "stretched_over_1.5", "edges")}, b.get("stretched_by_material"))
print("IOU", {k: v.get("iou") for k, v in i.items() if isinstance(v, dict) and "iou" in v}, "game px", i.get("game_px"))
for k in ("front", "three-quarter", "profile", "back"):
    for t in (i.get(k, {}).get("todo") or [])[:2]:
        print("TODO", k, t.get("text", "")[:200])
print("FILES", sorted(os.listdir(DEST)))
