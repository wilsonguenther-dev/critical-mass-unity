"""v4: bible_fit framings for the Farmer, inside the same Blender run as the build and the other renders.

  (step of run_all.py)  fit_farmer.py -- <out_dir>

For each guide view (front, three-quarter, profile, back): the capture pose of capture_poses.json, the tool's orthographic
guide camera (project_paint.setup_guide_camera, 720 px tall), a CM/Toon render with a transparent film and an all-white
ID mask (outline hull included), then project_paint.project_view (bloat / missing mass per height band, part, bone).
Also: hip height above the lowest foot in Idle and in the two hand-made lunge poses (pose_metrics.json).
Then: python3 fit_report.py guide-07-<view>.json <out>/renders/<view>.png <report> --mask <out>/renders/<view>.mask.png
"""
import json
import os
import runpy
import sys
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, "..", "..", "_tools~", "bible_fit"))
OUT = os.path.abspath(sys.argv[sys.argv.index("--") + 1])
VIEWS = ["front", "three-quarter", "profile", "back"]
OUTLINE_BIBLE = 0.016          # the p07 crops draw ~0.8 % of figure height
os.makedirs(os.path.join(OUT, "renders"), exist_ok=True)
sys.argv = [sys.argv[0], "--", os.path.join(OUT, "unused"), "none"]
g = runpy.run_path(os.path.join(HERE, "render_farmer.py"), run_name="render_farmer")
sys.path.insert(0, TOOLS)
import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402
import project_paint as pp  # noqa: E402

scene, meshes, arm = g["scene"], g["meshes"], g["arm"]
white = bpy.data.materials.new("FitMaskWhite")
white.use_nodes = True
nt = white.node_tree
nt.nodes.clear()
em = nt.nodes.new("ShaderNodeEmission")
em.inputs["Color"].default_value = (1, 1, 1, 1)
o = nt.nodes.new("ShaderNodeOutputMaterial")
nt.links.new(em.outputs[0], o.inputs["Surface"])
vl = scene.view_layers[0]
log = {"views": {}}
g["show_only"](meshes)
for v in VIEWS:
    try:
        act, fr, extra, az, el = g["capture"](v, ("Idle", 1, g["BIBLE_STANCE"], 0, 4))
        g["set_pose"](act, fr, extra)
        g["outline"](OUTLINE_BIBLE)
        cam = pp.setup_guide_camera(scene, meshes, az, el, height_px=720)
        g["light_world"](cam.matrix_world.to_3x3() @ Vector((-0.5, 0.6, 0.62)))
        scene.render.film_transparent = True
        g["team"](False)
        g["maskview"](False)
        vl.material_override = None
        g["render"](os.path.join(OUT, "renders", f"{v}.png"))
        vl.material_override = white
        scene.render.filter_size = 0.01
        g["render"](os.path.join(OUT, "renders", f"{v}.mask.png"))
        vl.material_override = None
        scene.render.filter_size = 1.5
        if json.load(open(g["CAPTURE"])).get(v, {}).get("mirror"):
            log["views"][v] = {"az": az, "el": el, "mirror": True}      # post step flips the PNGs; no projection
            continue
        res = pp.project_view(meshes, os.path.join(TOOLS, "guides", f"guide-07-{v}.json"), az, el,
                              outline_m=OUTLINE_BIBLE)
        pp.save_result(res, os.path.join(OUT, f"project-{v}"))
        log["views"][v] = {"az": az, "el": el, "summary": res["report"]["summary"]}
        print("PROJECT", v, " | ".join(res["report"]["summary"])[:900])
    except Exception:
        traceback.print_exc()
        log["views"][v] = "FAILED: " + traceback.format_exc().splitlines()[-1]
scene.camera = bpy.data.objects["Cam"]
# lunge depth: hips above the lowest point of the body, as a fraction of the posed height
pm = {}
body = [m for m in meshes if m.name.endswith("_Body")]
for name, spec in [("idle", ("Idle", 1, [], 0, 0))] + list(g["HAND_POSES"].items()):
    try:
        act, fr, extra, _, _ = spec
        g["set_pose"](act, fr, extra)
        pts = g["points"](body)
        z0, z1 = min(p.z for p in pts), max(p.z for p in pts)
        hz = (arm.matrix_world @ arm.pose.bones["hips"].head).z
        jz = max((arm.matrix_world @ arm.pose.bones[b].head).z for b in ("upperleg.l", "upperleg.r"))
        low = min(pts, key=lambda p: p.z)
        pm[name] = {"hips_above_feet_m": round(hz - z0, 3), "hipjoint_above_feet_m": round(jz - z0, 3),
                    "height_m": round(z1 - z0, 3), "hips_fraction": round((hz - z0) / (z1 - z0), 3),
                    "lowest_point": [round(x, 3) for x in low]}
    except Exception:
        pm[name] = "FAILED: " + traceback.format_exc().splitlines()[-1]
log["pose_metrics"] = pm
print("POSE_METRICS", json.dumps(pm))
with open(os.path.join(OUT, "fit_run.json"), "w") as fh:
    json.dump(log, fh, indent=1)
print("FIT_DONE")
