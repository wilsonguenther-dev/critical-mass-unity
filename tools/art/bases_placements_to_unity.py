"""Convert the bases kit's bases_placements.json (a dict keyed by site) into the array form Unity's JsonUtility reads:
{"sites": [{"name", "anchorSim": [x, z, h], "anchorWorld": [x, y, z], "items": [{"piece", "pos": [x, y, z], "yaw"}]}]}.
Sites keep the kit's order; values are copied unchanged (Unity frame, positions relative to the anchor)."""
import json
import sys

src, dst = sys.argv[1], sys.argv[2]
with open(src) as f:
    d = json.load(f)
sites = []
for name, s in d.items():
    sites.append({"name": name, "anchorSim": s["anchor_sim"], "anchorWorld": s["anchor_unity_world"],
                  "items": [{"piece": it["piece"], "pos": it["unity_local_pos"], "yaw": it["unity_yaw_deg"]} for it in s["items"]]})
with open(dst, "w") as f:
    json.dump({"sites": sites}, f, indent=1)
    f.write("\n")
print(f"bases placements: {len(sites)} sites, {sum(len(s['items']) for s in sites)} items -> {dst}")
