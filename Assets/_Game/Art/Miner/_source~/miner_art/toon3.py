"""Blender preview of CM/Toon at the 2026-11-12 art direction (section 2.2 / 2.3), for headless EEVEE renders only.
Unity's CM/Toon is the real shader; nothing here is exported (the FBX carries vertex colour + alpha mask only).

  * three hard bands on N.L (world light vector node LIGHT): N.L > 0.35 lit x1.00, -0.05..0.35 mid x0.84,
    below -0.05 shadow x0.66 then mixed 12 % toward Void Navy
  * team swap from the vertex-colour ALPHA (1.0 cloth, 0.75 glow); the Crimson colour keeps the Azure part's relative
    brightness, so the crystals' lighter facet stays lighter after the swap
  * team-glow parts are self-lit (no band)
  * rim: facing > 0.75 on the shadow side, mixed 30 % toward the team colour (characters only, node RIM)
  * DEBUG node: 1 = team pixels white / everything else black (team-coverage measurement)
  * inverted-hull outline in Void Navy; width set per render in screen pixels by the render script
"""
import bpy

from . import mpal


def _rgba(h):
    r, g, b = mpal.lin(h)
    return (r, g, b, 1.0)


def _lum(h):
    r, g, b = mpal.lin(h)
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def make_material(name="CM_Toon"):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    N, L = nt.nodes, nt.links

    def node(t, x=0, y=0, **kw):
        n = N.new(t)
        n.location = (x, y)
        for k, v in kw.items():
            setattr(n, k, v)
        return n

    def math_(op, a=None, b=None, c=None, x=0, y=0):
        n = node("ShaderNodeMath", x, y, operation=op)
        for i, v in enumerate((a, b, c)):
            if v is None:
                continue
            if isinstance(v, (int, float)):
                n.inputs[i].default_value = v
            else:
                L.new(v, n.inputs[i])
        return n.outputs[0]

    def mix(fac, a, b, blend="MIX"):
        n = node("ShaderNodeMix", data_type="RGBA", blend_type=blend)
        if isinstance(fac, (int, float)):
            n.inputs["Factor"].default_value = fac
        else:
            L.new(fac, n.inputs["Factor"])
        for sock, v in ((6, a), (7, b)):
            if isinstance(v, tuple):
                n.inputs[sock].default_value = v
            else:
                L.new(v, n.inputs[sock])
        return n.outputs[2]

    def value(nm, v):
        n = node("ShaderNodeValue", name=nm, label=nm)
        n.outputs[0].default_value = v
        return n.outputs[0]

    col = node("ShaderNodeVertexColor", layer_name="Col")
    team = value("TEAM", 0.0)
    debug = value("DEBUG", 0.0)
    rim_on = value("RIM", 1.0)
    light = node("ShaderNodeCombineXYZ", name="LIGHT", label="LIGHT")
    for i, v in enumerate((0.0, 0.643, 0.766)):
        light.inputs[i].default_value = v
    geo = node("ShaderNodeNewGeometry")
    base = col.outputs["Color"]
    alpha = col.outputs["Alpha"]
    is_cloth = math_("COMPARE", alpha, 1.0, 0.1)
    is_glow = math_("COMPARE", alpha, 0.75, 0.1)
    is_team = math_("MAXIMUM", is_cloth, is_glow)
    bw = node("ShaderNodeRGBToBW")
    L.new(base, bw.inputs[0])
    # Crimson: swap colour x (part brightness / Azure reference brightness)
    cur = base
    for key, mask in (("cloth", is_cloth), ("glow", is_glow)):
        ratio = math_("DIVIDE", bw.outputs[0], _lum(mpal.C[mpal.TEAM_REF[key]][0]))
        ratio = math_("MINIMUM", ratio, 1.6)
        sw = mix(1.0, _rgba(mpal.TEAM_SWAP[key][0]), (1, 1, 1, 1), "MULTIPLY")
        # multiply swap colour by ratio
        rn = node("ShaderNodeCombineColor")
        for i in range(3):
            L.new(ratio, rn.inputs[i])
        swc = mix(1.0, sw, rn.outputs[0], "MULTIPLY")
        f = math_("MULTIPLY", mask, team)
        cur = mix(f, cur, swc)
    # bands on N.L
    ndl = node("ShaderNodeVectorMath", operation="DOT_PRODUCT")
    L.new(geo.outputs["Normal"], ndl.inputs[0])
    L.new(light.outputs[0], ndl.inputs[1])
    nd = ndl.outputs["Value"]
    lit = math_("GREATER_THAN", nd, 0.35)
    notshadow = math_("GREATER_THAN", nd, -0.05)
    # multiplier: shadow .66, mid .84, lit 1.0
    mult = math_("ADD", 0.66, math_("ADD", math_("MULTIPLY", notshadow, 0.18), math_("MULTIPLY", lit, 0.16)))
    navy_f = math_("MULTIPLY", math_("SUBTRACT", 1.0, notshadow), 0.12)
    # glow: no band
    mult = math_("ADD", math_("MULTIPLY", mult, math_("SUBTRACT", 1.0, is_glow)), is_glow)
    navy_f = math_("MULTIPLY", navy_f, math_("SUBTRACT", 1.0, is_glow))
    mrgb = node("ShaderNodeCombineColor")
    for i in range(3):
        L.new(mult, mrgb.inputs[i])
    shaded = mix(1.0, cur, mrgb.outputs[0], "MULTIPLY")
    shaded = mix(navy_f, shaded, _rgba(mpal.VOID_NAVY))
    # rim toward team colour on the shadow side
    lw = node("ShaderNodeLayerWeight")
    lw.inputs["Blend"].default_value = 0.5
    rim = math_("GREATER_THAN", lw.outputs["Facing"], 0.75)
    rim = math_("MULTIPLY", rim, math_("LESS_THAN", nd, 0.35))
    rim = math_("MULTIPLY", math_("MULTIPLY", rim, rim_on), math_("SUBTRACT", 1.0, is_glow))
    rim = math_("MULTIPLY", rim, 0.30)
    rim_col = mix(team, _rgba("#16C7D9"), _rgba("#FF5A5F"))
    shaded = mix(rim, shaded, rim_col)
    # debug team mask
    dbg = mix(is_team, (0, 0, 0, 1), (1, 1, 1, 1))
    final = mix(debug, shaded, dbg)
    emit = node("ShaderNodeEmission")
    emit.inputs["Strength"].default_value = 1.0
    L.new(final, emit.inputs["Color"])
    out = node("ShaderNodeOutputMaterial")
    L.new(emit.outputs[0], out.inputs["Surface"])
    mat.diffuse_color = (0.5, 0.5, 0.5, 1)
    mat.metallic = 0.0
    mat.roughness = 1.0
    return mat


def make_outline_material(name="CM_Toon_Outline"):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    e = nt.nodes.new("ShaderNodeEmission")
    e.inputs["Color"].default_value = _rgba(mpal.VOID_NAVY)
    e.name = "INK"
    o = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(e.outputs[0], o.inputs["Surface"])
    mat.use_backface_culling = True
    return mat


def add_outline(ob, width, vgroup=None):
    om = make_outline_material()
    if om.name not in [m.name for m in ob.data.materials if m]:
        ob.data.materials.append(om)
    sol = ob.modifiers.get("Outline") or ob.modifiers.new("Outline", "SOLIDIFY")
    sol.thickness = width
    sol.offset = 1.0
    sol.use_flip_normals = True
    sol.use_rim = False
    sol.use_even_offset = True
    sol.material_offset = len(ob.data.materials) - 1
    if vgroup and vgroup in ob.vertex_groups:
        sol.vertex_group = vgroup
        sol.thickness_vertex_group = 0.0
    return sol


def set_param(mat, name, v):
    mat.node_tree.nodes[name].outputs[0].default_value = v


def set_light(mat, vec):
    n = mat.node_tree.nodes["LIGHT"]
    for i in range(3):
        n.inputs[i].default_value = vec[i]
