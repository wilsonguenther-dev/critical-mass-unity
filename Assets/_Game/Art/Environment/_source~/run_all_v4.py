"""Environment v4: ONE Blender process that renders the v3 "before", builds + exports the v4 kit, re-imports every new
or upgraded FBX to verify it, renders the v4 "after" at the same gameplay cameras, and renders two kit sheets.

    ~/bin/cm-heavy blender perl -e 'alarm 900; exec @ARGV' /Applications/Blender.app/Contents/MacOS/Blender -b \
        --factory-startup --python-exit-code 1 -P "Assets/_Game/Art/Environment/_source~/run_all_v4.py" -- <out_dir>

Writes <out>/renders/*.png, <out>/verify_fbx.json, <out>/shots.json. Rebuilds ../env_*.fbx, env_kit.blend, manifest.
Measuring (costs, densities, pixel checks) happens afterwards in plain Python: measure_v4.py.
"""
import json
import math
import os
import runpy
import sys
import time

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(ARGS[0]) if ARGS else os.path.join(HERE, "_out_v4")
os.makedirs(os.path.join(OUT, "renders"), exist_ok=True)
T0 = time.time()


def log(*a):
    print("V4", round(time.time() - T0, 1), "s", *a, flush=True)


# ---- render_v3 as a library (its module code parses argv, defines materials/instancing/camera; main() not run)
RV3 = os.path.join(HERE, "render_v3.py")
src = open(RV3).read().rstrip()
assert src.endswith("main()"), "render_v3.py layout changed"
sys.argv = [sys.argv[0], "--", OUT, "--scale", "1"]
ns = {"__name__": "render_v3_lib", "__file__": RV3}
exec(compile(src[:-len("main()")], RV3, "exec"), ns)

from env_art import kit2, kit4, layout2, layout4, palette  # noqa: E402

ERRS = kit4.preflight()
if ERRS:
    for e in ERRS:
        print("PREFLIGHT", e)
    raise SystemExit("PREFLIGHT_FAILED %d" % len(ERRS))
log("preflight ok")
lay = layout2.place_map()
corner = layout4.pick_corner(lay)
g = lay["g"]


def gh(x, z):
    return g.h(int(math.floor(x)), int(math.floor(z)))


SHOTS = [("corner", corner[1]), ("hill", (63.0, 0.75, 45.5)), ("base", (17.0, gh(17.0, 36.0), 36.0))]
json.dump({"shots": {k: list(v) for k, v in SHOTS}, "corner_waterfall": [corner[2]["x"], corner[2]["z"]],
           "corner_span_end": list(corner[3])}, open(os.path.join(OUT, "shots.json"), "w"), indent=1)


def setup_mats():
    MATS = ns["MATS"]
    MATS.clear()
    MATS.update({"CM_Toon": ns["toon_mat"]("CM_Toon"), "CM_Toon_Crimson": ns["toon_mat"]("CM_Toon_Crimson", True),
                 "CM_Toon_Foliage": ns["toon_mat"]("CM_Toon_Foliage", bands="foliage"),
                 "CM_Toon_Crystal": ns["toon_mat"]("CM_Toon_Crystal", bands="crystal"),
                 "CM_Toon_Crystal_Crimson": ns["toon_mat"]("CM_Toon_Crystal_Crimson", True, bands="crystal"),
                 "Outline": ns["outline_mat"]("Outline"), "Outline_Far": ns["outline_mat"]("Outline_Far", True),
                 "CM_GlowDecal": ns["decal_mat"]("CM_GlowDecal", True),
                 "CM_GlowDecal_Crimson": ns["decal_mat"]("CM_GlowDecal_Crimson", True, palette.TEAM_SWAP["team_glow"][0]),
                 "CM_ShadowDecal": ns["decal_mat"]("CM_ShadowDecal", False)})


def fresh_scene():
    for k in ("LIB", "WFM"):
        ns[k].clear()
    ns["WF"].clear()
    scn, cam = ns["reset"]()
    lib = bpy.data.collections.new("Lib")
    scn.collection.children.link(lib)
    bpy.context.view_layer.layer_collection.children["Lib"].exclude = True
    setup_mats()
    return scn, cam


def near(p, R=30.0):
    x, z = (p["a"][0], p["a"][2]) if "a" in p else (p["x"], p["z"])
    r = 48.0 if p.get("cat") == "sky" else R
    return any(math.hypot(x - t[0], z - t[2]) < r for _, t in SHOTS)


def build_map_scene(extra=(), subs=None):
    scn, cam = fresh_scene()
    MATS = ns["MATS"]
    pl = lay["placements"]
    sites = [(x, z, 1.6) for (x, z, r, nm) in layout2.map_sites() if nm == "env_crystal_biomass"]
    fx0, fz0, fw, fh = -8.0, -8.0, 128.0, 88.0
    fimg = ns["field_image"]("Field_Map", layout2.fields(lay, fx0, fz0, fw, fh, 4, sites))
    MATS["CM_Ground"] = ns["ground_mat"]("CM_Ground", fimg, fx0, fz0, fw, fh)
    world = bpy.data.collections.new("Map")
    scn.collection.children.link(world)
    ns["ground_mesh"]("Ground_Map", pl, MATS["CM_Ground"], world)
    subs = subs or {}
    objs = []
    for p in list(pl) + list(extra):
        if p["piece"] in ("env_ground_cell", "env_ground_fill_tri") or p.get("cat") == "struct" or not near(p):
            continue
        q = dict(p, piece=subs.get(id(p), p["piece"]))
        ob = ns["instance"](q, world)
        objs.append(ob)
        if q["piece"] == "env_waterfall":
            ns["WF"].append((ob, q))
    log("map scene", len(objs), "objects")
    return scn, cam, world, objs


def shoot(cam, objs, tag):
    for name, tgt in SHOTS:
        ns["cam_game"](cam, tgt)
        ns["set_outlines"](cam, objs)
        ns["fit_foam"](cam)
        ns["render"](cam, f"{name}_{tag}", (1920, 1080))
        log("rendered", name, tag)


# =========================================================================================== 1. BEFORE (v3 kit)
scn, cam, world, objs = build_map_scene()
shoot(cam, objs, "before")

# =========================================================================================== 2. BUILD v4 + export
runpy.run_path(os.path.join(HERE, "build_env_kit.py"), run_name="__main__")
log("build + export done")

# =========================================================================================== 3. VERIFY FBX re-import
man = json.load(open(os.path.join(HERE, "manifest.json")))
check = {}
for r in man["pieces"]:
    if not r.get("v4"):
        continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    path = os.path.join(os.path.dirname(HERE), r["piece"] + ".fbx")
    bpy.ops.import_scene.fbx(filepath=path)
    obs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    me = obs[0].data if obs else None
    if me is None:
        check[r["piece"]] = {"ok": False, "why": "no mesh"}
        continue
    vs = [obs[0].matrix_world @ v.co for v in me.vertices]
    ux = [-v.x for v in vs]
    uy = [v.z for v in vs]
    uz = [-v.y for v in vs]
    ca = me.color_attributes
    alphas = sorted({round(d.color[3], 2) for d in ca[0].data}) if len(ca) else []
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    bmin = [round(min(ux), 3), round(min(uy), 3), round(min(uz), 3)]
    bmax = [round(max(ux), 3), round(max(uy), 3), round(max(uz), 3)]
    ok = (len(ca) == 1 and tris == r["tris"] and all(a in (0.0, 0.25) for a in alphas)
          and all(abs(a - b) < 0.02 for a, b in zip(bmin + bmax, r["bounds_unity_min"] + r["bounds_unity_max"])))
    check[r["piece"]] = {"ok": ok, "tris": tris, "manifest_tris": r["tris"], "colour_layers": [c.name for c in ca],
                         "alphas": alphas, "mats": [m.name for m in me.materials], "bounds_min": bmin, "bounds_max": bmax,
                         "scale": [round(s, 4) for s in obs[0].scale]}
json.dump(check, open(os.path.join(OUT, "verify_fbx.json"), "w"), indent=1)
log("verify", sum(1 for v in check.values() if v.get("ok")), "of", len(check), "ok")

# =========================================================================================== 4. AFTER (v4 kit)
new, subs_l = layout4.dress(lay)
subs = {id(t): nm for t, nm in subs_l}
scn, cam, world, objs = build_map_scene(new, subs)
shoot(cam, objs, "after")

# =========================================================================================== 5. KIT SHEETS
world.hide_render = True
sheet = bpy.data.collections.new("Sheet")
scn.collection.children.link(sheet)
OX = 400.0


def text(body, x, y, z, size=0.32):
    cu = bpy.data.curves.new("T_" + body, "FONT")
    cu.body = body
    cu.size = size
    cu.align_x = "CENTER"
    ob = bpy.data.objects.new("T_" + body, cu)
    sheet.objects.link(ob)
    ob.location = ns["bl"](x, y, z)
    ob.rotation_euler = cam.rotation_euler
    mt = bpy.data.materials.get("LabelInk") or bpy.data.materials.new("LabelInk")
    if not mt.use_nodes:
        mt.use_nodes = True
        nt = mt.node_tree
        nt.nodes.clear()
        em = nt.nodes.new("ShaderNodeEmission")
        em.inputs[0].default_value = (0.009, 0.017, 0.044, 1)
        o = nt.nodes.new("ShaderNodeOutputMaterial")
        nt.links.new(em.outputs[0], o.inputs[0])
    cu.materials.append(mt)


def lineup(names, ground, tag, cols, sp, y0=0.0):
    sobjs = []
    cx, cz = OX, 0.0
    ns["cam_game"](cam, (cx, y0, cz))
    R = Vector((1, -1)).normalized()
    Fw = Vector((-1, -1)).normalized()
    rows = (len(names) + cols - 1) // cols
    if ground:
        m = kit2.Mesh("SheetGround_" + tag, cull="none")
        S = sp * max(cols, rows) * 0.9
        kit2.F(m, [(-S, -0.002, -S), (S, -0.002, -S), (S, -0.002, S), (-S, -0.002, S)], kit2.C("rock_mid", 1.35), (0, 1, 0))
        gob = m.finish(sheet)
        gob.data.materials.append(ns["MATS"]["CM_Toon"])
        gob.matrix_world = ns["pmat"](dict(x=cx, y=y0, z=cz, yaw=0.0))
        ref = kit2.Mesh("RefChar_" + tag, cull="none")
        ref.prism(0, 0, 0.32, 0.0, 1.45, kit2.C("rock_dark", 1.15), n=10)
        ref.prism(0, 0, 0.24, 1.45, 2.0, kit2.C("rock_dark", 1.15), n=10, r_top=0.18)
        rob = ref.finish(sheet)
        rob.data.materials.append(ns["MATS"]["CM_Toon"])
    for i, nm in enumerate(names):
        c, r = i % cols, i // cols
        off = R * (c - (cols - 1) / 2) * sp + Fw * (r - (rows - 1) / 2) * sp * 0.95
        x, z = cx + off.x, cz + off.y
        p = dict(piece=nm, x=x, y=y0, z=z, yaw=0.0)
        if nm.startswith("env_cliff") or nm.startswith("env_lip") or nm == "env_fence" or nm == "env_waterfall":
            p.update(x=x - 0.7, z=z + 0.7, yaw=-45.0)     # outward face (local -Z) toward the camera
        if nm.startswith("env_bridge_span") or nm == "env_bridge_anchor":
            p.update(z=z - 1.0, yaw=0.0)
        sobjs.append(ns["instance"](p, sheet))
        text(nm.replace("env_", ""), x - Fw.x * 1.0 + 0.0, y0 + 0.02, z - Fw.y * 1.0, size=0.28)
    if ground:
        last = R * ((cols - 1) / 2 + 1) * sp + Fw * (-(rows - 1) / 2) * sp * 0.95
        rob.matrix_world = ns["pmat"](dict(x=cx + last.x, y=y0, z=cz + last.y, yaw=0.0))
        text("2.0 u character", cx + last.x - Fw.x, y0 + 0.02, cz + last.y - Fw.y, size=0.28)
    cam.data.type = "ORTHO"
    cam.data.sensor_fit = "HORIZONTAL"
    cam.data.ortho_scale = sp * (cols + 1.6)
    ns["set_outlines"](cam, sobjs)
    ns["render"](cam, "kit_sheet_" + tag, (1920, 1080))
    for o in sobjs:
        o.hide_render = True
    for o in sheet.objects:
        o.hide_render = True
    log("sheet", tag)


standing = [s["name"] for s in kit4.NEW if s["cat"] not in ("sky", "bridge") and s["name"] != "env_lantern_hang"]
lineup(standing, True, "props", 8, 3.3)
hanging = ["env_cliff_s4", "env_cliff_d2", "env_lip_rim_straight_a", "env_waterfall", "env_float_chunk_s", "env_float_chunk_m",
           "env_bridge_span", "env_bridge_span_damaged", "env_bridge_anchor", "env_bridge_post", "env_bridge_pier", "env_bridge_cell",
           "env_lantern_hang"]
lineup(hanging, False, "edges_bridges", 5, 4.6, y0=0.0)
log("RUN_ALL_V4_DONE")
