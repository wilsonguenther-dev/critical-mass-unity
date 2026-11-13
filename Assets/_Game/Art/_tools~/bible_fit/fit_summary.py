"""Merge several fit_report JSONs (one per view) into ONE ranked to-do list (system python3).

  python3 fit_summary.py <out.json> report-front.json report-three-quarter.json ...

The same difference seen in several views (for example filled mass between the legs) is merged: highest score kept,
views listed, and it gets a small boost per extra view that confirms it.
"""
import json
import re
import sys


def key(it):
    t = it["text"]
    if it["kind"] == "colour":
        return ("colour", t.split(" (")[0])
    if it["kind"] == "landmark":
        return ("landmark", t.split(" at ")[0].split(" ")[0])
    zone = t.split(":")[0]
    direction = re.search(r"too wide|too narrow|too much|too little|sticks out|falls short", t)
    return (it["kind"], zone.split(" to ")[-1], direction.group(0) if direction else "")


def main():
    out, paths = sys.argv[1], sys.argv[2:]
    groups = {}
    ious = {}
    for p in paths:
        with open(p) as fh:
            r = json.load(fh)
        view = r["name"]
        ious[view] = r["iou"]
        for it in r["all_items"]:
            k = key(it)
            g = groups.setdefault(k, {"best": None, "views": []})
            g["views"].append(view)
            if g["best"] is None or it["score"] > g["best"]["score"]:
                g["best"] = dict(it, view=view)
    ranked = []
    for k, g in groups.items():
        it = g["best"]
        views = sorted(set(g["views"]))
        ranked.append({"score": round(it["score"] * (1 + 0.25 * (len(views) - 1)), 4), "kind": it["kind"],
                       "text": it["text"], "worst_view": it["view"], "seen_in": views})
    ranked.sort(key=lambda r: -r["score"])
    res = {"iou": ious, "todo": ranked[:8], "all": ranked}
    with open(out, "w") as fh:
        json.dump(res, fh, indent=1)
    print("IoU", ious)
    for i, r in enumerate(res["todo"]):
        print(f"{i + 1}. [{r['kind']} {r['score']:.3f}] ({r['worst_view']}; seen in {len(r['seen_in'])}) {r['text']}")


if __name__ == "__main__":
    main()
