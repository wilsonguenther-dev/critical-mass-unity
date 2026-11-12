"""Critical Mass bases and structures kit: build, export, check, render (headless Blender 5.2).

  B=/Applications/Blender.app/Contents/MacOS/Blender
  $B -b --factory-startup --python-exit-code 1 --python Assets/_Game/Art/Bases/_source~/build_bases.py -- \
      --out <render_dir> [--no-render] [--only name1,name2]
"""
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from bases_art import kit, layouts, pal, pieces  # noqa: E402  (layouts fits pieces.RHO on import)
from bases_art import simgrid as G  # noqa: E402
from bases_art.kit import S2B  # noqa: E402

ART = os.path.dirname(HERE)
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def arg(name, default=None):
    return ARGS[ARGS.index(name) + 1] if name in ARGS else default


OUT = os.path.expanduser(arg("--out", os.path.join(HERE, "renders~")))
ONLY = set(arg("--only", "").split(",")) - {""}
RENDER = "--no-render" not in ARGS
FBX_OPTS = dict(
    use_selection=True, object_types={"MESH"}, use_mesh_modifiers=False, apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL", global_scale=1.0, axis_forward="-Z", axis_up="Y",
    bake_space_transform=True, mesh_smooth_type="FACE", use_tspace=False, colors_type="SRGB",
    prioritize_active_color=True, add_leaf_bones=False, bake_anim=False, use_custom_props=False,
    path_mode="AUTO", embed_textures=False,
)
FOV_Y, PITCH, DIST = 35.0, 55.0, 23.4
CAM_OFF = Vector((-math.sin(math.radians(45)), math.cos(math.radians(45)), 0)) * DIST * math.cos(math.radians(PITCH)) \
    + Vector((0, 0, DIST * math.sin(math.radians(PITCH))))
TO_SUN = Vector((math.cos(math.radians(50)), 0.0, math.sin(math.radians(50))))   # light from screen upper-left


# ============================================================================ kit
def marker():
    pc = kit.Piece("cm_marker_diamond", team=True, kind="prop")
    pc.lathe([(0.0, 0.0), (0.42, 0.62), (0.0, 1.5)], 4, lambda f: pal.vc("team") if f.normal.x + f.normal.y > -0.2
             else pal.vc("team_dark"))
    pc.lathe([(0.5, -0.35), (0.62, -0.35), (0.62, -0.3), (0.5, -0.3)], 20, kit.solid(pal.vc("cream")), closed=True)
    return pc


def kit_builders():
    b = {}
    for s in ("intact", "cracked", "destroyed"):
        b[f"wall_segment_{s}"] = (lambda s=s: pieces.wall_segment(s))
        b[f"hill_breakable_{s}"] = (lambda s=s: pieces.hill_arc(f"cm_hill_breakable_{s}", 37, 53, 63, warm=True, state=s))
        b[f"shrine_breakable_{s}"] = (lambda s=s: pieces.shrine_arc(f"cm_shrine_breakable_{s}", [(37, 53)], 72, state=s))
    b.update({
        "hill_arc_a": lambda: pieces.hill_arc("cm_hill_arc_a", 12, 37, 61),
        "hill_arc_b": lambda: pieces.hill_arc("cm_hill_arc_b", 53, 78, 62),
        "shrine_yard": lambda: pieces.shrine_arc("cm_shrine_yard_walls", [(0, 37), (53, 75), (105, 127), (143, 180)], 71),
        "gate_post": lambda: pieces.gate_post(),
        "gate_post_ring": lambda: pieces.gate_post("cm_gate_post_ring", tilt=6.0, arm=0.5, w=0.84),
        "banner": lambda: pieces.banner_post(),
        "tower": lambda: pieces.tower(),
        "landmark": lambda: pieces.landmark_tower(),
        "brazier": lambda: pieces.brazier(),
        "pool": lambda: pieces.pool_decal(),
        "egg_pool": lambda: pieces.pool_decal("cm_egg_pool", radius=3.0, hexc="#4FDDC3", alpha=0.4),
        "crystal_lamp": lambda: pieces.crystal_lamp(),
        "crystal_lamp_low": lambda: pieces.crystal_lamp("cm_crystal_lamp_low", height=0.26),
        "stairs": lambda: pieces.stairs(),
        "roof_pitched": lambda: pieces.roof_pitched(),
        "pad": lambda: pieces.respawn_pad(),
        "hill_dais": lambda: pieces.hill_dais(),
        "hill_rim_w": lambda: pieces.hill_rim_half(),
        "hill_horn_l": lambda: pieces.hill_horn("cm_hill_horn_l", 81, base_ang=236, tip_ang=256),
        "hill_horn_r": lambda: pieces.hill_horn("cm_hill_horn_r", 82, base_ang=304, tip_ang=284),
        "shrine_core": lambda: pieces.shrine_core(),
        "shrine_egg": lambda: pieces.shrine_egg(),
        "marker": marker,
        "crate": lambda: pieces.crate(),
        "barrel": lambda: pieces.barrel(),
        "blocks": lambda: pieces.block_stack(),
        "rubble_low": lambda: pieces.rubble_low(),
        "planks_low": lambda: pieces.planks_low(),
        "flagstones": lambda: pieces.flag_stones(),
    })
    return b


KIT = {}      # key -> dict(mesh, piece meta)


def piece_meta(pc, me):
    return {"mesh": me, "name": pc.name, "kind": pc.kind, "team": pc.team, "lights": pc.lights, "pools": pc.pools,
            "marks": pc.marks, "blocks": pc.blocks}


def build_kit():
    for key, fn in kit_builders().items():
        pc = fn()
        ob = pc.to_object(bpy)
        KIT[key] = piece_meta(pc, ob.data)
        bpy.data.objects.remove(ob)
    KIT["hill_rim_e"] = KIT["hill_rim_w"]


def link(ob, coll):
    coll.objects.link(ob)
    return ob


def export_fbx(objs, path):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    res = bpy.ops.export_scene.fbx(filepath=path, **FBX_OPTS)
    if "FINISHED" not in res:
        raise SystemExit(f"FBX export failed: {path}")


def export_kit(scn):
    tmp = bpy.data.collections.new("export_tmp")
    scn.collection.children.link(tmp)
    rows = []
    for key, m in KIT.items():
        if key == "hill_rim_e":
            continue
        ob = link(bpy.data.objects.new(m["name"], m["mesh"]), tmp)
        path = os.path.join(ART, m["name"] + ".fbx")
        export_fbx([ob], path)
        lo = [min(v.co[i] for v in m["mesh"].vertices) for i in range(3)]
        hi = [max(v.co[i] for v in m["mesh"].vertices) for i in range(3)]
        rows.append({"key": key, "fbx": os.path.basename(path), "tris": sum(len(p.vertices) - 2 for p in m["mesh"].polygons),
                     "blocks": m["blocks"], "team_masked": m["team"], "kind": m["kind"],
                     "size_m": [round(hi[i] - lo[i], 3) for i in range(3)], "height_m": round(hi[2], 3)})
        bpy.data.objects.remove(ob)
    bpy.data.collections.remove(tmp)
    return rows


# ============================================================================ layouts
LAYOUT_OBJS = {}     # layout name -> list of objects (world placed)
PLACEMENTS = {}      # layout name -> list of dicts (Unity frame)


def place_layout(scn, name, world_pieces, place, anchor):
    coll = bpy.data.collections.new(name)
    scn.collection.children.link(coll)
    objs, rows = [], []
    for pc in world_pieces:
        ob = pc.to_object(bpy)
        KIT[pc.name] = piece_meta(pc, ob.data)
        ob["key"] = pc.name
        link(ob, coll)
        objs.append(ob)
    for key, sx, sz, h, yaw, extra in place:
        if key == "__piece__":
            pc = extra["piece"]
            o = pc.to_object(bpy)
            KIT[pc.name] = piece_meta(pc, o.data)
            o.location = S2B(sx, sz, h)
            o["key"] = pc.name
            link(o, coll)
            objs.append(o)
            continue
        m = KIT[key]
        ob = bpy.data.objects.new(f"{name}:{m['name']}", m["mesh"])
        ob.location = S2B(sx, sz, h)
        ob.rotation_euler = (0, 0, yaw)
        ob["key"] = key
        link(ob, coll)
        objs.append(ob)
        u = (sx - anchor[0], h - anchor[2], -(sz - anchor[1]))
        rows.append({"piece": m["name"], "unity_local_pos": [round(c, 4) for c in u],
                     "unity_yaw_deg": round(-math.degrees(yaw), 3)})
    LAYOUT_OBJS[name] = objs
    PLACEMENTS[name] = {"anchor_sim": anchor, "anchor_unity_world": [anchor[0], anchor[2], -anchor[1]], "items": rows}
    return coll


def build_layouts(scn):
    for team in ("azure", "crimson"):
        world, place, info = layouts.district(team)
        padx = layouts.PAD[0] if team == "azure" else G.MIRROR_X - layouts.PAD[0]
        place_layout(scn, f"district_{team}", world, place, (padx, layouts.PAD[1], 0.0))
    for nm, st in (("hill", ("intact",) * 4), ("hill_damaged", ("cracked", "intact", "destroyed", "intact"))):
        place_layout(scn, nm, [], layouts.hill(st), layouts.HILL)
    place_layout(scn, "shrine", [], layouts.shrine(), layouts.SHRINE)
    # kit lineup + bible framing singles (not exported as layouts)
    single = [("wall_segment_intact", 0), ("wall_segment_cracked", 6), ("wall_segment_destroyed", 12),
              ("banner", 18), ("pad", 26), ("gate_post", 34), ("tower", 40), ("landmark", 48), ("stairs", 56),
              ("roof_pitched", 63), ("brazier", 69), ("crystal_lamp", 72)]
    for key, dx in single:
        coll = bpy.data.collections.new(f"single_{key}")
        scn.collection.children.link(coll)
        ob = link(bpy.data.objects.new(f"single:{key}", KIT[key]["mesh"]), coll)
        ob.location = Vector((200 + dx, -200, 0))
        if key == "banner":
            ob.rotation_euler = (0, 0, math.pi)      # cloth front toward the south-facing bible camera
        ob["key"] = key
        LAYOUT_OBJS[f"single_{key}"] = [ob]


def export_layouts(scn):
    for name, anchor_key in (("district_azure", None), ("district_crimson", None), ("hill", None), ("shrine", None)):
        objs = LAYOUT_OBJS[name]
        a = PLACEMENTS[name]["anchor_sim"]
        off = S2B(a[0], a[1], a[2])
        saved = [o.location.copy() for o in objs]
        for o in objs:
            o.location = o.location - off
        export_fbx(objs, os.path.join(ART, f"cm_layout_{name}.fbx"))
        for o, l in zip(objs, saved):
            o.location = l
    with open(os.path.join(ART, "bases_placements.json"), "w") as f:
        json.dump(PLACEMENTS, f, indent=1)


# ============================================================================ checks
def world_verts(ob):
    mw = ob.matrix_world
    return [mw @ v.co for v in ob.data.vertices]


def face_samples(ob):
    mw = ob.matrix_world
    pts = [mw @ v.co for v in ob.data.vertices]
    pts += [mw @ p.center for p in ob.data.polygons]
    return pts


def footprint_check(names):
    """Geometry over walkable cells: 'blocking' = 0.3 u < z - ground < 2.1 u; 'overhead' = above 2.1 u."""
    res = {"blocking_pts": 0, "max_intrusion_m": 0.0, "overhead_pts": 0, "route_blocking_pts": 0, "worst": []}
    routes = layouts.route_cells()
    for name in names:
        for ob in LAYOUT_OBJS[name]:
            m = KIT.get(ob.get("key"), {})
            if m.get("kind") in ("decal",):
                continue
            for p in face_samples(ob):
                sx, sz = -p.x, p.y
                cx, cz = math.floor(sx), math.floor(sz)
                if not layouts.walk(cx, cz):
                    continue
                dz = p.z - layouts.hgt(cx, cz)
                if dz <= 0.3:
                    continue
                if dz >= 2.1:
                    res["overhead_pts"] += 1
                    continue
                # depth into the walkable region: distance to the nearest non-walkable cell square
                best = 9.0
                for ox in range(-2, 3):
                    for oz in range(-2, 3):
                        if not layouts.walk(cx + ox, cz + oz):
                            qx = min(max(sx, cx + ox), cx + ox + 1)
                            qz = min(max(sz, cz + oz), cz + oz + 1)
                            best = min(best, math.hypot(sx - qx, sz - qz))
                res["blocking_pts"] += 1
                po = res.setdefault("per_object", {}).setdefault(ob.name, [0, 0.0])
                po[0] += 1
                po[1] = round(max(po[1], best), 2)
                if best > res["max_intrusion_m"]:
                    res["max_intrusion_m"] = round(best, 3)
                if best > 0.25 and len(res["worst"]) < 12:
                    res["worst"].append([ob.name, round(sx, 2), round(sz, 2), round(dz, 2), round(best, 2)])
                if (cx, cz) in routes:
                    inner = (cx + 0.2 < sx < cx + 0.8) and (cz + 0.2 < sz < cz + 0.8)
                    if inner:
                        res["route_blocking_pts"] += 1
    return res


def team_up_area(me):
    a = 0.0
    col = me.color_attributes.get("Col")
    for p in me.polygons:
        if p.normal.z <= 0.3:
            continue
        alpha = sum(col.data[li].color[3] for li in p.loop_indices) / len(p.loop_indices)
        if alpha >= 0.45:
            a += p.area * p.normal.z
    return a


def team_colour_leaks():
    """Faces with a team mask must carry a team RGB; faces with mask 0 must not carry a team RGB."""
    team_rgb = [pal.lin(pal.HEX[k]) for k in ("team", "team_dark", "team_glow")]
    bad = 0
    for key, m in KIT.items():
        col = m["mesh"].color_attributes.get("Col")
        if col is None:
            continue
        for c in col.data:
            r, g, b, a = c.color
            is_team_rgb = any(abs(r - t[0]) + abs(g - t[1]) + abs(b - t[2]) < 0.02 for t in team_rgb)
            if (a >= 0.45) != is_team_rgb and m["kind"] != "decal":
                bad += 1
    return bad


def measure_walls():
    m = KIT["wall_segment_intact"]["marks"]
    mer = m.get("merlon", [])
    tops = [b[1][2] for b in mer]
    mid = sorted(mer, key=lambda b: abs((b[0][0] + b[1][0]) / 2))[0]
    course = m.get("course", [])
    hz = [b[1][2] - b[0][2] for b in course]
    hl = KIT["hill_arc_a"]["marks"]
    return {
        "wall_top_with_merlons_m": round(max(tops), 3),
        "merlon_w_d_h_m": [round(mid[1][0] - mid[0][0], 3), round(mid[1][1] - mid[0][1], 3), round(mid[1][2] - mid[0][2], 3)],
        "course_block_height_m (course 0.75 minus 0.03 gap)": round(sum(hz) / len(hz), 3) if hz else None,
        "hill_wall_top_m": round(max(b[1][2] for b in hl.get("merlon", []) + hl.get("merlon_cap", [])), 3),
        "gate_post_top_m": round(max(v.co.z for v in KIT["gate_post"]["mesh"].vertices if v.co.z < 3.8), 3),
        "tower_merlon_top_m": round(max(b[1][2] for b in KIT["tower"]["marks"]["merlon"]), 3),
        "landmark_roof_apex_m": round(max(b[1][2] for b in KIT["landmark"]["marks"]["roof"]), 3),
        "landmark_diameter_m": 4.5,
        "stair_step_rise_m": 0.25, "stair_tread_m": 0.35,
        "blocks_per_piece": {k: v["blocks"] for k, v in KIT.items() if v["blocks"]},
    }


# ============================================================================ look (render only)
def toon_material(variant, stripe_centre=None):
    mat = bpy.data.materials.new(f"CM_Toon_{variant}")
    mat.use_nodes = True
    N, L = mat.node_tree.nodes, mat.node_tree.links
    N.clear()

    def node(t, **kw):
        n = N.new(t)
        for k, v in kw.items():
            setattr(n, k, v)
        return n

    def rgba(h):
        return pal.lin(h) + (1.0,)
    vc = node("ShaderNodeVertexColor", layer_name="Col")

    def is_code(code):
        c = node("ShaderNodeMath", operation="COMPARE")
        c.inputs[1].default_value = code
        c.inputs[2].default_value = 0.1
        L.new(vc.outputs["Alpha"], c.inputs[0])
        return c.outputs[0]
    cur = vc.outputs["Color"]
    sets = pal.TEAM_SETS
    if variant in ("crimson", "neutral", "contested"):
        base = "neutral" if variant == "contested" else variant
        for key, code in (("team", pal.MASK_TEAM), ("team_glow", pal.MASK_TEAM_GLOW), ("team_dark", pal.MASK_TEAM_DARK)):
            mix = node("ShaderNodeMix", data_type="RGBA", blend_type="MIX")
            L.new(is_code(code), mix.inputs["Factor"])
            L.new(cur, mix.inputs[6])
            if variant == "contested":
                # alternating Azure / Crimson stripes by angle around the hill centre (bible p15 contested stripe)
                geo = node("ShaderNodeNewGeometry")
                sub = node("ShaderNodeVectorMath", operation="SUBTRACT")
                sub.inputs[1].default_value = stripe_centre
                L.new(geo.outputs["Position"], sub.inputs[0])
                sep = node("ShaderNodeSeparateXYZ")
                L.new(sub.outputs[0], sep.inputs[0])
                at = node("ShaderNodeMath", operation="ARCTAN2")
                L.new(sep.outputs[1], at.inputs[0])
                L.new(sep.outputs[0], at.inputs[1])
                sc = node("ShaderNodeMath", operation="MULTIPLY")
                sc.inputs[1].default_value = 12.0 / math.pi
                L.new(at.outputs[0], sc.inputs[0])
                fl = node("ShaderNodeMath", operation="FLOORED_MODULO")
                fl.inputs[1].default_value = 2.0
                L.new(sc.outputs[0], fl.inputs[0])
                st = node("ShaderNodeMath", operation="GREATER_THAN")
                st.inputs[1].default_value = 1.0
                L.new(fl.outputs[0], st.inputs[0])
                sm = node("ShaderNodeMix", data_type="RGBA", blend_type="MIX")
                L.new(st.outputs[0], sm.inputs["Factor"])
                sm.inputs[6].default_value = rgba(sets["azure"][key])
                sm.inputs[7].default_value = rgba(sets["crimson"][key])
                L.new(sm.outputs[2], mix.inputs[7])
            else:
                mix.inputs[7].default_value = rgba(sets[base][key])
            cur = mix.outputs[2]
    glow = node("ShaderNodeMath", operation="MAXIMUM")
    L.new(is_code(pal.MASK_TEAM_GLOW), glow.inputs[0])
    L.new(is_code(pal.MASK_FIXED_GLOW), glow.inputs[1])
    # three bands from N.L, cast shadow from the sun-only diffuse
    diff = node("ShaderNodeBsdfDiffuse")
    s2r = node("ShaderNodeShaderToRGB")
    L.new(diff.outputs[0], s2r.inputs[0])
    bw = node("ShaderNodeRGBToBW")
    L.new(s2r.outputs["Color"], bw.inputs[0])
    lit = node("ShaderNodeMath", operation="GREATER_THAN")
    lit.inputs[1].default_value = 1e-4
    L.new(bw.outputs[0], lit.inputs[0])
    geo = node("ShaderNodeNewGeometry")
    dot = node("ShaderNodeVectorMath", operation="DOT_PRODUCT")
    dot.inputs[1].default_value = TO_SUN
    L.new(geo.outputs["Normal"], dot.inputs[0])
    add1 = node("ShaderNodeMath", operation="ADD")
    add1.inputs[1].default_value = 1.0
    L.new(dot.outputs["Value"], add1.inputs[0])
    m1 = node("ShaderNodeMath", operation="MULTIPLY")
    L.new(add1.outputs[0], m1.inputs[0])
    L.new(lit.outputs[0], m1.inputs[1])
    half = node("ShaderNodeMath", operation="MULTIPLY")
    half.inputs[1].default_value = 0.5
    L.new(m1.outputs[0], half.inputs[0])          # (ndl+1)/2 when lit by the sun, 0 in cast shadow
    ramp = node("ShaderNodeValToRGB")
    cr = ramp.color_ramp
    cr.interpolation = "CONSTANT"
    cr.elements[0].position = 0.0
    sh = pal.BAND_SHADOW
    cr.elements[0].color = (sh, sh, sh, pal.SHADOW_NAVY_MIX)
    e = cr.elements.new(0.475)
    e.color = (pal.BAND_MID,) * 3 + (0.0,)
    cr.elements[1].position = 0.675
    cr.elements[1].color = (1, 1, 1, 0.0)
    L.new(half.outputs[0], ramp.inputs[0])
    band = node("ShaderNodeMix", data_type="RGBA", blend_type="MIX")
    L.new(glow.outputs[0], band.inputs["Factor"])
    L.new(ramp.outputs["Color"], band.inputs[6])
    band.inputs[7].default_value = (1, 1, 1, 1)
    mul = node("ShaderNodeMix", data_type="RGBA", blend_type="MULTIPLY")
    mul.inputs["Factor"].default_value = 1.0
    L.new(cur, mul.inputs[6])
    L.new(band.outputs[2], mul.inputs[7])
    nav = node("ShaderNodeMix", data_type="RGBA", blend_type="MIX")
    navf = node("ShaderNodeMath", operation="MULTIPLY")
    L.new(ramp.outputs["Alpha"], navf.inputs[0])
    inv = node("ShaderNodeMath", operation="SUBTRACT")
    inv.inputs[0].default_value = 1.0
    L.new(glow.outputs[0], inv.inputs[1])
    L.new(inv.outputs[0], navf.inputs[1])
    L.new(navf.outputs[0], nav.inputs["Factor"])
    L.new(mul.outputs[2], nav.inputs[6])
    nav.inputs[7].default_value = rgba(pal.HEX["navy"])
    em = node("ShaderNodeEmission")
    L.new(nav.outputs[2], em.inputs["Color"])
    # Diffuse must reach the output for Shader-to-RGB to evaluate: add it at zero weight
    addsh = node("ShaderNodeAddShader")
    L.new(em.outputs[0], addsh.inputs[0])
    out = node("ShaderNodeOutputMaterial")
    L.new(em.outputs[0], out.inputs["Surface"])
    return mat


def outline_material():
    mat = bpy.data.materials.new("CM_Outline")
    mat.use_nodes = True
    N = mat.node_tree.nodes
    N.clear()
    e = N.new("ShaderNodeEmission")
    e.inputs["Color"].default_value = pal.lin(pal.HEX["navy"]) + (1.0,)
    o = N.new("ShaderNodeOutputMaterial")
    mat.node_tree.links.new(e.outputs[0], o.inputs["Surface"])
    mat.use_backface_culling = True
    return mat


def decal_material():
    mat = bpy.data.materials.new("CM_Decal")
    mat.use_nodes = True
    N, L = mat.node_tree.nodes, mat.node_tree.links
    N.clear()
    vc = N.new("ShaderNodeVertexColor")
    vc.layer_name = "Col"
    em = N.new("ShaderNodeEmission")
    L.new(vc.outputs["Color"], em.inputs["Color"])
    tr = N.new("ShaderNodeBsdfTransparent")
    mx = N.new("ShaderNodeMixShader")
    L.new(vc.outputs["Alpha"], mx.inputs[0])
    L.new(tr.outputs[0], mx.inputs[1])
    L.new(em.outputs[0], mx.inputs[2])
    o = N.new("ShaderNodeOutputMaterial")
    L.new(mx.outputs[0], o.inputs["Surface"])
    try:
        mat.surface_render_method = "BLENDED"
    except Exception:
        mat.blend_method = "BLEND"
    return mat


MATS = {}


def ground_object(scn):
    """Plain mid-grey ground from the Sim cells (render reference only; the real ground is the environment kit)."""
    import bmesh
    bm = bmesh.new()
    col = bm.loops.layers.float_color.new("Col")

    def q(pts, c):
        f = bm.faces.new([bm.verts.new(p) for p in pts])
        for lp in f.loops:
            lp[col] = c
    top_c = pal.vc("#8C8C8C")
    br_c = pal.vc("#9A968F")
    side_c = pal.vc("#6A6A6A")
    for cz in range(G.H):
        for cx in range(G.W):
            t = layouts.cell(cx, cz)
            if t == G.VOID:
                continue
            h = layouts.hgt(cx, cz)
            x0, x1 = -cx, -(cx + 1)
            y0, y1 = cz, cz + 1
            q([(x0, y0, h), (x0, y1, h), (x1, y1, h), (x1, y0, h)], br_c if t == G.BRIDGE else top_c)
            for (nx, nz), pts in (((1, 0), ((x1, y0), (x1, y1))), ((-1, 0), ((x0, y1), (x0, y0))),
                                  ((0, 1), ((x1, y1), (x0, y1))), ((0, -1), ((x0, y0), (x1, y0)))):
                nt = layouts.cell(cx + nx, cz + nz)
                nh = layouts.hgt(cx + nx, cz + nz) if nt != G.VOID else (-1.5 if t == G.BRIDGE else -3.0)
                if t == G.BRIDGE and nt == G.VOID:
                    nh = h - 0.3
                if nh < h - 1e-4:
                    (ax, ay), (bx, by) = pts
                    q([(ax, ay, h), (ax, ay, nh), (bx, by, nh), (bx, by, h)], side_c)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new("ground")
    bm.to_mesh(me)
    ob = bpy.data.objects.new("ground", me)
    ob["key"] = "__ground__"
    scn.collection.objects.link(ob)
    return ob


def setup_render(scn):
    scn.render.engine = "BLENDER_EEVEE"
    scn.render.film_transparent = True
    scn.view_settings.view_transform = "Standard"
    scn.view_settings.look = "None"
    scn.eevee.taa_render_samples = 16
    try:
        scn.eevee.use_shadows = True
        scn.eevee.shadow_resolution_scale = 1.0
    except Exception:
        pass
    w = bpy.data.worlds.new("W")
    w.use_nodes = True
    bg = w.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0, 0, 0, 1)
    bg.inputs[1].default_value = 0.0
    scn.world = w
    sd = bpy.data.lights.new("Sun", "SUN")
    sd.energy = 3.0
    sd.angle = 0.0
    sd.use_shadow = True
    sun = bpy.data.objects.new("Sun", sd)
    sun.rotation_euler = (-TO_SUN).to_track_quat("-Z", "Y").to_euler()
    scn.collection.objects.link(sun)
    for v in ("azure", "crimson", "neutral"):
        MATS[v] = toon_material(v)
    MATS["contested"] = toon_material("contested", stripe_centre=S2B(*layouts.HILL[:2], 0))
    MATS["outline"] = outline_material()
    MATS["decal"] = decal_material()
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scn.collection.objects.link(cam)
    scn.camera = cam
    return cam


def set_mats(ob, variant):
    me = ob.data
    if ob.get("key") and KIT.get(ob["key"], {}).get("kind") == "decal":
        if not me.materials:
            me.materials.append(MATS["decal"])
        return
    while len(me.materials) < 2:
        me.materials.append(MATS["azure"])
    for i, sl in enumerate(ob.material_slots):
        sl.link = "OBJECT"
    ob.material_slots[0].material = MATS[variant]
    ob.material_slots[1].material = MATS["outline"]


def set_outline(ob, px, px_per_m_fn):
    sol = ob.modifiers.get("Outline")
    if px <= 0:
        if sol:
            ob.modifiers.remove(sol)
        return
    if sol is None:
        sol = ob.modifiers.new("Outline", "SOLIDIFY")
        sol.offset = 1.0
        sol.use_flip_normals = True
        sol.use_rim = False
        sol.use_even_offset = True
        sol.material_offset = 1
    c = ob.matrix_world @ (sum((Vector(b) for b in ob.bound_box), Vector()) / 8)
    sol.thickness = px / px_per_m_fn(c)


def frame_gameplay(cam, target, crop=1.0):
    cam.data.type = "PERSP"
    cam.data.sensor_fit = "VERTICAL"
    cam.data.angle_y = 2 * math.atan(math.tan(math.radians(FOV_Y / 2)) / crop)
    cam.data.clip_start, cam.data.clip_end = 0.5, 300
    cam.location = target + CAM_OFF
    cam.rotation_euler = (-CAM_OFF).to_track_quat("-Z", "Y").to_euler()
    res_y = bpy.context.scene.render.resolution_y

    def ppm(p):
        d = (p - cam.location).length
        return res_y / (2 * d * math.tan(cam.data.angle_y / 2))
    return ppm


def frame_ortho(cam, centre, scale, elev=30.0, yaw_from_south=0.0):
    yaw_from_south = SHOT_YAW[0]
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = scale
    cam.data.clip_start, cam.data.clip_end = 0.1, 400
    e = math.radians(elev)
    a = math.radians(yaw_from_south)
    off = Vector((-math.sin(a) * math.cos(e), math.cos(a) * math.cos(e), math.sin(e))) * 80
    cam.location = centre + off
    cam.rotation_euler = (-off).to_track_quat("-Z", "Y").to_euler()
    sc = bpy.context.scene
    long_side = max(sc.render.resolution_x, sc.render.resolution_y)
    return lambda p: long_side / scale


SHOTS = []
SHOT_YAW = [0.0]


def shot(name, layouts_on, variant, mode, target, res=(1920, 1080), crop=1.0, scale=20.0, elev=30.0, px=2.0,
         warrior=None, hide=None, rim_variant=None, ground=True, yaw=0.0):
    SHOTS.append(dict(yaw=yaw, name=name, layouts=layouts_on, variant=variant, mode=mode, target=target, res=res, crop=crop,
                      scale=scale, elev=elev, px=px, warrior=warrior, hide=hide, rim_variant=rim_variant, ground=ground))


def define_shots():
    pa = S2B(6.6, 34.6, 0)            # player on the pad + the 2.0 u screen-up look offset (art direction 2.1)
    pc_ = S2B(G.MIRROR_X - 8.0 - 1.4, 34.6, 0)   # the look offset is screen-up (NW) for both teams
    hc = S2B(56, 40.6, 0.75)
    sc = S2B(56, 9.6, 1.5)
    shot("gp_base_azure", ["district_azure"], "azure", "gp", pa, warrior=(8.6, 36.9, 0.1))
    shot("gp_base_crimson", ["district_crimson"], "crimson", "gp", pc_, warrior=(G.MIRROR_X - 8.0 + 0.6, 36.9, 0.1))
    shot("gp_hill_neutral", ["hill"], "neutral", "gp", hc, warrior=(57.5, 43.5, 0.95))
    shot("gp_hill_azure", ["hill_damaged"], "azure", "gp", hc, warrior=(57.5, 43.5, 0.95))
    shot("gp_hill_contested", ["hill"], "neutral", "gp", hc, warrior=(57.5, 43.5, 0.95), rim_variant="contested")
    shot("gp_shrine", ["shrine"], "azure", "gp", sc, warrior=(58.5, 12.5, 1.5))
    a = math.radians(25)
    shot("crop2x_wall", ["hill"], "azure", "gp", S2B(56 + 10.5 * math.cos(a), 40 + 10.5 * math.sin(a), 1.5), crop=2.0,
         warrior=(56 + 8.6 * math.cos(a), 40 + 8.6 * math.sin(a), 0.75))
    shot("crop2x_gate", ["hill"], "azure", "gp", S2B(56, 50.2, 1.5), crop=2.0, warrior=(56.2, 49.0, 0.75))
    tx, tz = layouts.isl_pt(116, 2.1)
    shot("crop2x_tower", ["district_azure"], "azure", "gp", S2B(tx, tz, 2.5), crop=2.0, warrior=(tx + 2.6, tz - 2.2, 0.0))
    # bible framings (orthographic, front view from the south, 30 deg elevation)
    shot("bf_shrine", ["shrine"], "azure", "ortho", S2B(56, 8, 4.0), res=(1000, 1000), scale=8.6, elev=25,
         hide=lambda o: ("yard" in o.name or "breakable" in o.name or "gate_post" in o.name or "brazier" in o.name
                         or "pool" in o.name or "marker" in o.name), ground=False, yaw=45.0)
    for v in ("neutral", "azure", "crimson"):
        shot(f"bf_hill_{v}", ["hill"], v, "ortho", S2B(56, 40, 4.4), res=(1200, 900), scale=24, elev=28,
             hide=lambda o: ((o.matrix_world @ (sum((Vector(b) for b in o.bound_box), Vector()) / 8)).y > 41.5
                             and "dais" not in o.name and "rim" not in o.name) or "marker" in o.name, ground=False)
    for s in ("intact", "cracked", "destroyed"):
        shot(f"bf_wall_{s}", [f"single_wall_segment_{s}"], "azure", "ortho", Vector((200 + {"intact": 0, "cracked": 6, "destroyed": 12}[s], -200, 1.3)),
             res=(1000, 750), scale=5.6, elev=22, ground=False)
    shot("bf_banner", ["single_banner"], "azure", "ortho", Vector((218, -200, 1.6)), res=(700, 1000), scale=4.0, elev=15,
         ground=False)
    shot("bf_pad", ["single_pad"], "azure", "ortho", Vector((226, -200, 0.2)), res=(1000, 820), scale=9.0, elev=35,
         ground=False)
    shot("bf_p12", ["district_azure", "district_crimson", "hill", "shrine"], "azure", "top", S2B(56, 36, 0),
         res=(1100, 720), scale=114, px=1.0)
    shot("kit_lineup", [k for k in LAYOUT_OBJS if k.startswith("single_")], "azure", "ortho", Vector((236, -200, 2.5)),
         res=(1920, 700), scale=82, elev=18, ground=False)


def render_all(scn, cam, ground):
    os.makedirs(OUT, exist_ok=True)
    warrior_pc = pieces.warrior_stand_in()
    wob = warrior_pc.to_object(bpy)
    scn.collection.objects.link(wob)
    stats = {}
    from bpy_extras.object_utils import world_to_camera_view
    for sh in SHOTS:
        if ONLY and sh["name"] not in ONLY:
            continue
        scn.render.resolution_x, scn.render.resolution_y = sh["res"]
        scn.render.resolution_percentage = 100
        visible = set()
        for name, objs in LAYOUT_OBJS.items():
            on = name in sh["layouts"]
            for o in objs:
                hid = (not on) or (sh["hide"] is not None and sh["hide"](o))
                o.hide_render = hid
                if not hid:
                    visible.add(o.name)
        ground.hide_render = not sh["ground"]
        if sh["mode"] == "gp":
            ppm = frame_gameplay(cam, sh["target"], sh["crop"])
        elif sh["mode"] == "top":
            cam.data.type = "ORTHO"
            cam.data.ortho_scale = sh["scale"]
            cam.location = sh["target"] + Vector((0, 0, 120))
            cam.rotation_euler = (0, 0, math.pi)       # sim north (-y blender) up, sim east (-x blender) right
            ppm = (lambda p: max(sh["res"]) / sh["scale"])
        else:
            SHOT_YAW[0] = sh["yaw"]
            ppm = frame_ortho(cam, sh["target"], sh["scale"], sh["elev"])
        if sh["warrior"]:
            wob.hide_render = False
            wob.location = S2B(*sh["warrior"])
            wob.rotation_euler = (0, 0, math.radians(225))
            set_mats(wob, sh["variant"])
            set_outline(wob, 3.0 * (sh["px"] / 2.0), ppm)
        else:
            wob.hide_render = True
        for name in sh["layouts"]:
            for o in LAYOUT_OBJS[name]:
                if o.hide_render:
                    continue
                v = sh["variant"]
                if sh["rim_variant"] and "rim" in o.name:
                    v = sh["rim_variant"]
                set_mats(o, v)
                kind = KIT.get(o.get("key"), {}).get("kind")
                if kind != "decal":
                    set_outline(o, sh["px"] * (1.25 if kind == "prop" else 1.0), ppm)
        set_mats(ground, "neutral")
        set_outline(ground, 0, ppm)
        scn.render.filepath = os.path.join(OUT, sh["name"] + ".png")
        bpy.ops.render.render(write_still=True)
        # counts in frame (props, light pools, lamps, landmark), structure outline px
        if sh["mode"] == "gp":
            c = {"props": 0, "pools": 0, "lamps": 0, "landmark_visible": False, "outline_px": []}
            for name in sh["layouts"]:
                for o in LAYOUT_OBJS[name]:
                    if o.hide_render:
                        continue
                    k = o.get("key", "")
                    p = world_to_camera_view(scn, cam, o.matrix_world.translation)
                    inside = 0 <= p.x <= 1 and 0 <= p.y <= 1 and p.z > 0
                    m = KIT.get(k, {})
                    if inside and m.get("kind") == "prop":
                        c["props"] += 1
                    if inside and k in ("gate_post", "gate_post_ring"):
                        c["props"] += 1          # its banner
                    if k in ("pool",) and inside:
                        c["pools"] += 1
                    if inside and k in ("crystal_lamp",):
                        c["lamps"] += 1
                    for lp, lk in m.get("lights", []):
                        wp = o.matrix_world @ lp
                        q = world_to_camera_view(scn, cam, wp)
                        if 0 <= q.x <= 1 and 0 <= q.y <= 1:
                            if lk == "brazier" and k != "brazier":
                                c["pools"] += 1    # tower-door braziers carry their own pool
                            if lk in ("door", "crystal"):
                                c["lamps"] += 1
                    if k == "landmark":       # visible = at least half of 9 probe points (body, eave, apex) in frame
                        probes = [Vector((dx, dy, z)) for z in (3.0, 7.0, 9.5) for dx, dy in ((0, 0), (1.8, 0), (-1.8, 0))]
                        hits = 0
                        for pr in probes:
                            q = world_to_camera_view(scn, cam, o.matrix_world @ pr)
                            hits += (0 <= q.x <= 1 and 0 <= q.y <= 1)
                        c["landmark_probe_hits_of_9"] = hits
                        c["landmark_visible"] = c["landmark_visible"] or hits >= 5
                    sol = o.modifiers.get("Outline")
                    if sol and m.get("kind") == "structure":
                        cen = o.matrix_world @ (sum((Vector(b) for b in o.bound_box), Vector()) / 8)
                        c["outline_px"].append(round(sol.thickness * ppm(cen), 2))
            c["outline_px"] = [min(c["outline_px"]), max(c["outline_px"])] if c["outline_px"] else []
            if sh["warrior"]:
                top = world_to_camera_view(scn, cam, wob.location + Vector((0, 0, 2.0)))
                bot = world_to_camera_view(scn, cam, wob.location)
                c["warrior_px"] = round((top.y - bot.y) * sh["res"][1], 1)
            stats[sh["name"]] = c
    return stats


# ============================================================================ main
def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scn = bpy.context.scene
    build_kit()
    rows = export_kit(scn)
    build_layouts(scn)
    export_layouts(scn)
    bpy.context.view_layer.update()
    checks = {"walls": measure_walls()}
    fp = {}
    for nm in ("district_azure", "district_crimson", "hill", "hill_damaged", "shrine"):
        fp[nm] = footprint_check([nm])
    checks["footprint"] = fp
    owned = {}
    for k in ("gate_post", "tower", "landmark", "hill_arc_a", "hill_arc_b", "shrine_yard", "pad", "hill_rim_w",
              "shrine_core", "banner", "wall_segment_intact", "cm_district_walls_azure"):
        owned[k] = round(team_up_area(KIT[k]["mesh"]), 2)
    hill_total = 4 * (owned["hill_arc_a"] + owned["hill_arc_b"]) + 2 * owned["hill_rim_w"]
    owned["hill_whole_ring_plus_rim"] = round(hill_total, 2)
    owned["shrine_whole"] = round(owned["shrine_yard"] + owned["shrine_core"] + 2 * owned["gate_post"], 2)
    checks["team_up_area_m2"] = owned
    checks["team_colour_leak_corners"] = team_colour_leaks()
    checks["damage_state_fbx"] = {fam: sorted(f for f in os.listdir(ART) if f.startswith(fam) and f.endswith(".fbx"))
                                  for fam in ("cm_wall_segment_", "cm_hill_breakable_", "cm_shrine_breakable_")}
    checks["kit"] = rows
    checks["solid_cell_proposal"] = layouts.solid_cell_proposal()
    checks["chipped_blocks"] = dict(kit.CHIP_STATS)
    os.makedirs(OUT, exist_ok=True)
    blend = os.path.join(HERE, "bases_kit.blend")
    if RENDER:
        ground = ground_object(scn)
        cam = setup_render(scn)
        define_shots()
        checks["shots"] = render_all(scn, cam, ground)
    with open(os.path.join(OUT, "checks.json"), "w") as f:
        json.dump(checks, f, indent=1)
    bpy.ops.wm.save_as_mainfile(filepath=blend)
    print("BASES_DONE", json.dumps({k: checks[k] for k in ("walls", "footprint", "team_up_area_m2")})[:3000])


main()
