# Writes REPORT.md for the fighter LOD build from analysis.json + choice.json (plain Python).
# Run: python3 report_md.py <scratch> <report_dir> <commit> <stamp>
import json, os, subprocess, sys

ROOT, OUT, COMMIT, STAMP = sys.argv[1:5]
a = json.load(open(os.path.join(ROOT, "analysis.json")))
choice = json.load(open(os.path.join(ROOT, "choice.json"))) if os.path.exists(os.path.join(ROOT, "choice.json")) else {}
here = os.path.dirname(os.path.abspath(__file__))
tables = subprocess.run([sys.executable, os.path.join(here, "report_table.py"), os.path.join(ROOT, "analysis.json")],
                        capture_output=True, text=True).stdout
L = []
w = L.append
w("# Fighter LODs v1, fix pass (contract C4), %s\n" % STAMP)
w("Branch `art/fighter-lods` in ~/critical-mass-unity-art-lods, commit %s, pushed. Scripts in "
  "`Assets/_Game/Art/Characters/_source~/fighter_lods/`. Sheets `sheet-<Model>.jpg` here show Hero, LOD0, LOD1 and "
  "Shadow at in-game pixel size (top row) and enlarged (bottom row), Idle pose, gameplay camera. Raw numbers are in "
  "`analysis.json`.\n" % COMMIT)
w("## What changed in the fix pass (coordinator review)")
w("1. **Colour:** colour, UVs, normals and material are taken per face corner from the ORIGINAL Hero surface by "
  "nearest point, from any part, preferring triangles that face the same way. A decal guard gives a triangle the "
  "decal's transparent texel unless all its corners lie on Hero triangles that touch the face decal: large LOD "
  "triangles were smearing face paint across the body (the white on the Healer). A new check splits the Hero "
  "silhouette into 3 x 3 regions at gameplay size and gates the largest difference of mean colour per region "
  "(LOD0 at most 16, LOD1 at most 24, on 0-255).")
w("2. **Shards:** vertices are welded by distance before decimating (1.5 mm in the `decw` method), with no seam "
  "protection on LOD1 and the Shadow. The Shadow is now a closed proxy: the convex hull of each bone's vertices, "
  "decimated to budget, each hull rigid to its bone, so it cannot tear. LOD1 ships only if a closed, coherent "
  "variant reaches overlap 0.93 with at most 50 torn edges, at most 10 shard islands (6 triangles or fewer) and "
  "clean region colour. Otherwise LOD1 is identical to LOD0 (method `lod0` below).")
w("3. **Orientation:** see the proof section below.")
w("4. **LOD0:** the best of two variants per model (`dec`: exact weld, 2 degree planar; `decw`: 1.5 mm weld, 0.5 "
  "degree planar), with face, hands-free and mask-border locks as before.\n")
w("Methods shipped per model: " + "; ".join("%s %s" % (k, ", ".join("%s=%s" % kv for kv in v.items())) for k, v in choice.items()) + ".\n")
w("## Results (re-imported FBX; gameplay camera pitch 55, vFOV 35 at 1080 px, 23.4 u; worst of front and back three-quarter)")
part = tables.split("\n\n")
w(part[0] + "\n")
w("Region colour difference per level (largest 3 x 3 region mean difference, 0-255) and shards:\n")
w("| Model | LOD0 region | LOD1 region | LOD1 islands (shards) | Shadow islands (shards) |\n|---|---|---|---|---|")
for k, v in a.items():
    if k.startswith("_") or not v.get("ok"):
        continue
    lv = v["levels"]
    w("| %s | %.1f %s | %.1f %s | %s (%s) | %s (%s) |" % (
        k, lv["LOD0"]["region_colour_diff"], "pass" if lv["LOD0"].get("region_pass") else "FAIL",
        lv["LOD1"]["region_colour_diff"], "pass" if lv["LOD1"].get("region_pass") else "FAIL",
        lv["LOD1"].get("islands"), lv["LOD1"].get("shard_islands"), lv["Shadow"].get("islands"), lv["Shadow"].get("shard_islands")))
w("\n## Orientation proof (original FBX and LOD FBX imported into one Blender scene with the export's axis settings)\n")
w("| Model | File | Tallest axis | Bbox min | Bbox max | head bone | foot.l bone | Rig rotation |\n|---|---|---|---|---|---|---|---|")
for k, v in a.items():
    if k.startswith("_") or not v.get("ok") or not v.get("orientation_proof"):
        continue
    for tag, d in v["orientation_proof"].items():
        w("| %s | %s | %s | %s | %s | %s | %s | %s |" % (k, tag, d["tallest_axis"], d["bbox_min"], d["bbox_max"],
                                                     d.get("head", "-"), d.get("foot.l", "-"), d.get("rig_rotation_deg", "-")))
legs = {k: (v.get("idle_spine_dir"), v.get("idle_leg_dir"), v.get("screen_y"), v.get("screen_y_rest"))
        for k, v in a.items() if not k.startswith("_") and v.get("ok") and v.get("idle_leg_dir")}
w("\nIdle pose directions (world, Z up) and screen heights of bones (0 bottom, 1 top) at the gameplay camera:\n")
w("| Model | spine hips to head | leg hip to foot | screen y Idle front (head/hips/foot) | screen y rest |\n|---|---|---|---|---|")
for k, (sp, lg, sy, sr) in legs.items():
    f = (sy or {}).get("front", {})
    w("| %s | %s | %s | %s / %s / %s | %s |" % (k, sp, lg, f.get("head"), f.get("hips"), f.get("foot.l"), sr))
w("\n" + (part[1] if len(part) > 1 else ""))
sc = a.get("_scene", {})
f = sc.get("fighter_avg", {})
w("\n## Projected scene triangles (32 fighters plus Kaiju adult; fighter and Kaiju share only; lit + outline hull + shadow passes)\n")
w("| Case | Triangles |\n|---|---|")
for lab, key in (("Today (full mesh drawn lit, with hull, and casting; Hero avg %s)" % f.get("Hero"), "today_32_plus_kaiju"),
                 ("High, all 32 within 25 u", "high_all_near"), ("High, typical (8 near, 24 far)", "high_typical"),
                 ("Medium, typical", "medium_typical"), ("Low", "low")):
    w("| %s | {:,} |".format(sc.get(key, 0)) % lab)
w("\nFighter averages after the fix pass: LOD0 %s, LOD1 %s, Shadow %s." % (f.get("LOD0"), f.get("LOD1"), f.get("Shadow")))
open(os.path.join(OUT, "REPORT.md"), "w").write("\n".join(L) + "\n")
print("REPORT", len(L), "blocks")
