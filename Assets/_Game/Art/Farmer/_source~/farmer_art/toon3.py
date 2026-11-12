"""Blender preview of CM/Toon at the ART-DIRECTION 2026-11-12 spec (section 2.2 / 2.3), for headless EEVEE renders.

  * colour = vertex colour RGB; team swap scales team parts by (crimson / azure) per channel, so painted gradients survive
  * three bands on N.L against the LIGHT vector (world space, set per render): > 0.35 lit x1.00, -0.05..0.35 mid x0.84,
    below -0.05 shadow x0.66 mixed 12 % toward Void Navy. Hard edges. No gloss, no metal.
  * team rim: where N.V < 0.15 (grazing), mix 0.45 toward the team core colour (Azure #16C7D9 / Crimson #FF5A5F),
    lit band only, never on glows. (The CMToon "width 0.25, strength 0.6" literally applied as N.V < 0.25 painted a
    teal band round every part in v1; this is the visual equivalent of a thin rim.)
  * mask code 0.75 (team glow) and 0.25 (fixed biomass glow) are unlit; the biomass gets a lighter rim facet
  * MASKVIEW = 1 replaces everything with ID colours for pixel counts: team -> magenta, biomass -> green, other -> red
  * inverted-hull outline in Void Navy #18233B (render only; width set per render in pixels -> metres)
The Unity shader is out of scope; this file is the visual contract the Farmer was judged with.
"""
import bpy

from cm_art import palette as P
from . import fpalette as FP


RIM_NDV, RIM_STRENGTH = 0.15, 0.45


def _rgba(h, a=1.0):
    r, g, b = P.lin(h)
    return (r, g, b, a)


def make_material(name="CM_Toon", face_image=None):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    N, L = nt.nodes, nt.links
    xy = [0]

    def node(t, **kw):
        n = N.new(t)
        n.location = (xy[0] * 180, 0)
        xy[0] += 1
        for k, v in kw.items():
            setattr(n, k, v)
        return n

    def math_(op, a, b=None, c=None):
        n = node("ShaderNodeMath", operation=op)
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
        for slot, v in ((6, a), (7, b)):
            if isinstance(v, tuple):
                n.inputs[slot].default_value = v
            else:
                L.new(v, n.inputs[slot])
        return n.outputs[2]

    def val(nm, v):
        n = node("ShaderNodeValue", name=nm, label=nm)
        n.outputs[0].default_value = v
        return n.outputs[0]

    col = node("ShaderNodeVertexColor", layer_name="Col")
    team = val("TEAM", 0.0)
    maskview = val("MASKVIEW", 0.0)
    a = col.outputs["Alpha"]
    vcol = col.outputs["Color"]
    if face_image is not None:          # painted face decal (UVMap) over the skin vertex colour
        uvn = node("ShaderNodeUVMap", uv_map="UVMap")
        tex = node("ShaderNodeTexImage", name="FACE", image=face_image, interpolation="Linear", extension="CLIP")
        L.new(uvn.outputs[0], tex.inputs[0])
        vcol = mix(tex.outputs["Alpha"], vcol, tex.outputs["Color"])
    is_cloth = math_("COMPARE", a, P.MASK_TEAM_CLOTH, 0.1)
    is_tglow = math_("COMPARE", a, P.MASK_TEAM_GLOW, 0.1)
    is_fglow = math_("COMPARE", a, FP.MASK_FIXED_GLOW, 0.1)
    is_team = math_("MAXIMUM", is_cloth, is_tglow)
    is_glow = math_("MAXIMUM", is_tglow, is_fglow)

    # team swap: multiply team parts by crimson/azure ratio (linear, per channel)
    az = P.lin(FP.FARMER["f_team_cloth"][0])
    cr = P.lin(FP.FARMER_SWAP["f_team_cloth"][0])
    ratio = tuple(c / max(z, 1e-4) for c, z in zip(cr, az)) + (1.0,)
    swapped = mix(1.0, vcol, ratio, "MULTIPLY")
    base = mix(math_("MULTIPLY", is_team, team), vcol, swapped)

    # N.L bands
    geo = node("ShaderNodeNewGeometry")
    light = node("ShaderNodeCombineXYZ", name="LIGHT", label="LIGHT (world, toward the light)")
    light.inputs[0].default_value, light.inputs[1].default_value, light.inputs[2].default_value = (-0.4, -0.6, 0.7)
    dot = node("ShaderNodeVectorMath", operation="DOT_PRODUCT")
    L.new(geo.outputs["Normal"], dot.inputs[0])
    L.new(light.outputs[0], dot.inputs[1])
    ndl = dot.outputs["Value"]
    g_lit = math_("GREATER_THAN", ndl, 0.35)
    g_mid = math_("GREATER_THAN", ndl, -0.05)
    lit = base
    mid = mix(1.0, base, (0.84, 0.84, 0.84, 1.0), "MULTIPLY")
    shd = mix(0.12, mix(1.0, base, (0.66, 0.66, 0.66, 1.0), "MULTIPLY"), _rgba(FP.VOID_NAVY))
    banded = mix(g_mid, shd, mix(g_lit, mid, lit))

    # team rim (grazing, lit/mid side, not glows)
    ndv = node("ShaderNodeVectorMath", operation="DOT_PRODUCT")
    L.new(geo.outputs["Normal"], ndv.inputs[0])
    L.new(geo.outputs["Incoming"], ndv.inputs[1])
    rim = math_("LESS_THAN", ndv.outputs["Value"], RIM_NDV)
    rim = math_("MULTIPLY", rim, g_lit)
    rim = math_("MULTIPLY", rim, math_("SUBTRACT", 1.0, is_glow))
    rim = math_("MULTIPLY", rim, RIM_STRENGTH)
    rimcol = mix(team, _rgba(P.CORE["azure_team"]), _rgba(P.CORE["crimson_team"]))
    shaded = mix(rim, banded, rimcol)

    # glows: unlit; biomass gets a lighter rim facet where N.V < 0.45
    facet = math_("MULTIPLY", math_("LESS_THAN", ndv.outputs["Value"], 0.45), is_fglow)
    glowcol = mix(facet, base, _rgba(FP.FARMER["f_biomass_hi"][0]))
    out_col = mix(is_glow, shaded, glowcol)

    # mask view
    idcol = mix(is_team, _rgba("#FF0000"), _rgba("#FF00FF"))
    idcol = mix(is_fglow, idcol, _rgba("#00FF00"))
    final = mix(maskview, out_col, idcol)

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
    e.inputs["Color"].default_value = _rgba(FP.VOID_NAVY)
    o = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(e.outputs[0], o.inputs["Surface"])
    mat.use_backface_culling = True
    return mat


def add_outline(ob, width):
    om = make_outline_material()
    if om.name not in [m.name for m in ob.data.materials if m]:
        ob.data.materials.append(om)
    sol = ob.modifiers.get("Outline") or ob.modifiers.new("Outline", "SOLIDIFY")
    sol.thickness = width
    sol.offset = 1.0
    sol.use_flip_normals = True
    sol.use_rim = False
    sol.use_even_offset = False      # even offset spikes at sharp leaf / blade tips
    sol.material_offset = len(ob.data.materials) - 1
    if "outline_w" in ob.vertex_groups:          # no hull on face features (eyes, brows, mouth)
        sol.vertex_group = "outline_w"
        sol.thickness_vertex_group = 0.0
    return sol


def set_team(mat, crimson):
    mat.node_tree.nodes["TEAM"].outputs[0].default_value = 1.0 if crimson else 0.0


def set_maskview(mat, on):
    mat.node_tree.nodes["MASKVIEW"].outputs[0].default_value = 1.0 if on else 0.0


def set_light(mat, v):
    n = mat.node_tree.nodes["LIGHT"]
    for i in range(3):
        n.inputs[i].default_value = v[i]
