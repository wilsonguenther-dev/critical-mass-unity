"""ONE Blender run for the Warrior: bible-framing renders (render_warrior.py, its tuned poses and cameras) for the four
guide views, the project_paint report per view, a guide-camera render, and a team-mask safety self-test.

  ~/bin/cm-heavy blender perl -e 'alarm 900; exec @ARGV' /Applications/Blender.app/Contents/MacOS/Blender -b \
      --factory-startup --python-exit-code 1 -P examples/warrior_fit_run.py -- <source_dir> <guide_dir> <out_dir>

<source_dir> = Assets/_Game/Art/Characters/_source~ (render_warrior.py + warrior.blend), <guide_dir> = output of
bible_guide.py for prefix 06, <out_dir> gets renders/<view>.png + .mask.png and project-<view>.json/.npz.
Nothing is saved back to warrior.blend.
"""
import json
import os
import runpy
import sys
import traceback

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, GUIDES, OUT = [os.path.abspath(a) for a in argv[:3]]
VIEWS = ["front", "three-quarter", "profile", "back"]
TOOLS = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, TOOLS)
os.makedirs(os.path.join(OUT, "renders"), exist_ok=True)
log = {"views": {}}

sys.argv = [sys.argv[0], "--", "bible", os.path.join(OUT, "renders"), "720", ",".join(VIEWS)]
g = runpy.run_path(os.path.join(SRC, "render_warrior.py"), run_name="render_warrior")
import bpy  # noqa: E402
import numpy as np  # noqa: E402
import project_paint as pp  # noqa: E402

meshes, arm = g["meshes"], g["arm"]
for v in VIEWS:
    try:
        clip, frame, rots, az, el, team, fit = g["POSES"][v]
        arm.location.z = 0.0
        g["pose"](clip, frame, rots)
        res = pp.project_view(meshes, os.path.join(GUIDES, f"guide-06-{v}.json"), az, el, outline_m=g["OUTLINE_BIBLE"])
        pp.save_result(res, os.path.join(OUT, f"project-{v}"))
        log["views"][v] = res["report"]["summary"]
        print("PROJECT", v, " | ".join(res["report"]["summary"]))
        if v == "front":
            gj = json.load(open(os.path.join(GUIDES, "guide-06-front.json")))
            dp = pp.depth_profile(meshes, az, el, chin=gj["landmarks"].get("chin", {}).get("y", 0.62))
            log["depth_ours"] = dp
            print("DEPTH", json.dumps({k: x for k, x in dp.items() if k != "core_depth"}))
            pp.write_guide_attributes(res)
            body = bpy.data.objects["Warrior_Body"]
            names = [a.name for a in body.data.attributes]
            ca = body.data.color_attributes["Col"]
            n = len(body.data.vertices)
            before = np.empty(n * 4, np.float32)
            ca.data.foreach_get("color", before)
            k = pp.apply_guide_paint(body, res, min_conf=0.6)
            after = np.empty(n * 4, np.float32)
            ca.data.foreach_get("color", after)
            same_alpha = bool(np.array_equal(before.reshape(-1, 4)[:, 3], after.reshape(-1, 4)[:, 3]))
            ca.data.foreach_set("color", before)            # undo: this run only tests the helper
            pp.clear_guide_attributes(meshes)
            left = [a.name for a in body.data.attributes if a.name.startswith("_bible")]
            colour_sets = [a.name for a in body.data.color_attributes]
            log["selftest"] = {"attrs_written": [a for a in names if a.startswith("_bible")], "painted_verts": k,
                               "alpha_unchanged": same_alpha, "attrs_left_after_clear": left,
                               "colour_attributes": colour_sets}
            print("SELFTEST", json.dumps(log["selftest"]))
            # guide camera helper: one toon + one mask render of the same pose
            g["set_outline"](g["OUTLINE_BIBLE"])
            cam = pp.setup_guide_camera(bpy.context.scene, meshes, az, el, height_px=720)
            bpy.data.objects["ContactBlob"].hide_render = True
            g["set_materials"]("toon")
            g["render"](os.path.join(OUT, "renders", "guidecam-front.png"))
            g["set_materials"]("mask")
            g["render"](os.path.join(OUT, "renders", "guidecam-front.mask.png"))
            g["set_materials"]("toon")
            bpy.context.scene.camera = bpy.data.objects["Cam"]
    except Exception:
        traceback.print_exc()
        log["views"][v] = "FAILED: " + traceback.format_exc().splitlines()[-1]
with open(os.path.join(OUT, "blender_run.json"), "w") as fh:
    json.dump(log, fh, indent=1)
print("DONE_RUN")
