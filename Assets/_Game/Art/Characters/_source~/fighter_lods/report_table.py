# Prints the REPORT.md tables from analysis.json (plain Python). Run: python3 report_table.py <scratch>/analysis.json
import json, sys

a = json.load(open(sys.argv[1]))
GATE = {"LOD0": 0.97, "LOD1": 0.93}
BUD = {"LOD0": 4500, "LOD1": 1500, "Shadow": 800}
KBUD = {"LOD0": 12000, "LOD1": 4500, "Shadow": 1500}
print("| Model | Level | Triangles (budget) | Overlap at game size | Colour diff (mean, 0-255) | Team-mask share (drift) | Torn edges | Decal UV mismatch |")
print("|---|---|---|---|---|---|---|---|")
for k, v in a.items():
    if k.startswith("_"):
        continue
    if not v.get("ok"):
        print("| %s | FAILED | | | | | | |" % k)
        continue
    for lv in ("Hero", "LOD0", "LOD1", "Shadow"):
        L = v["levels"][lv]
        bud = (KBUD if k == "KaijuAdult" else BUD).get(lv)
        tri = "%d" % L["tris"] + (" (%d) %s" % (bud, "pass" if L["tris"] <= bud else "FAIL") if bud else "")
        ov = "-" if lv == "Hero" else "%.3f" % L["overlap"] + (
            " %s" % ("pass" if L["overlap"] >= GATE[lv] else "FAIL") if lv in GATE else " (no gate)")
        print("| %s | %s | %s | %s | %s | %.1f %% (%.2f pp) | %d | %s |" % (
            k if lv == "Hero" else "", lv, tri, ov, "-" if lv == "Hero" else "%.1f" % L["colour_mean_diff"],
            L["team_share_pct"], L["team_drift_pp"], L["torn_edges"],
            "-" if "decal_mismatch" not in L else "%.1f %%" % (100 * L["decal_mismatch"])))
print()
print("| Model | Bones src/out | Bone head max delta (m) | Clips src/out | Meshes out | Colour layers | Max influences | Bbox src vs Hero out (m) |")
print("|---|---|---|---|---|---|---|---|")
for k, v in a.items():
    if k.startswith("_") or not v.get("ok"):
        continue
    rt = v["roundtrip"]
    hero = [m for n, m in rt["meshes_out"].items() if n.endswith("_Hero")][0]
    print("| %s | %s/%s | %.5f | %d/%d | %s | %s | %s | %s vs %s |" % (
        k, rt["bones_src"], rt["bones_out"], rt["bone_head_max_delta_m"], len(rt["clips_src"]), len(rt["clips_out"]),
        ", ".join("%s %d" % (n, m["tris"]) for n, m in rt["meshes_out"].items()),
        "/".join(sorted({c[0] for m in rt["meshes_out"].values() for c in m["colours"]})),
        max(m["max_inf"] for m in rt["meshes_out"].values()),
        [round(x, 3) for x in rt["source_bbox"][1]], [round(x, 3) for x in hero["bbox"][1]]))
print()
print(json.dumps(a.get("_scene"), indent=1))
