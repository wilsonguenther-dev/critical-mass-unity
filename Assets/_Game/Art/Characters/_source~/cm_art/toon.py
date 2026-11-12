"""Blender preview of the CM/Toon contract (ART-DIRECTION 2.2 / 2.3, U-008), for headless EEVEE renders.

The Unity shader is the real thing (a separate work item); this mirrors the contract so the art can be judged first:
  * flat bible colour from the vertex colour RGB (which already carries the +6 % top gradient and -10 % crevices)
  * team swap from the vertex colour ALPHA code (palette.MASK_*), material value node "TEAM" (0 Azure, 1 Crimson)
  * three hard bands from N.L against the key light (node "LIGHT_DIR", world space, set by the render script):
      N.L > 0.35 lit x1.00 | -0.05..0.35 mid x0.84 | below shadow x0.66 mixed 12 % toward Void Navy
  * team rim light (width 0.25, strength 0.6) toward the team colour, on the side away from the key light
  * team-glow parts unlit; the painted face decal (UVMap, image "Warrior_Face") over the skin
  * inverted-hull outline in Void Navy: Solidify with flipped normals, render time only, never exported
"""
import bpy

from . import palette

MID, SHADE, NAVY_MIX = 0.84, 0.66, 0.12
T_LIT, T_MID = 0.35, -0.05
RIM_WIDTH, RIM_STRENGTH = 0.25, 0.6
TEAM_RIM = {0: "#16C7D9", 1: "#FF5A5F"}


def _rgba(h):
    r, g, b = palette.lin(h)
    return (r, g, b, 1.0)


def make_toon_material(name="CM_Toon", face_image=None):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    N, L = nt.nodes, nt.links

    def node(t, x, y, **kw):
        n = N.new(t)
        n.location = (x, y)
        for k, v in kw.items():
            setattr(n, k, v)
        return n

    def math_(op, a, b=None, x=0, y=0):
        n = node("ShaderNodeMath", x, y, operation=op)
        for i, v in enumerate((a, b)):
            if v is None:
                continue
            if isinstance(v, (int, float)):
                n.inputs[i].default_value = v
            else:
                L.new(v, n.inputs[i])
        return n.outputs[0]

    def mix(fac, a, b, x=0, y=0, blend="MIX"):
        n = node("ShaderNodeMix", x, y, data_type="RGBA", blend_type=blend)
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

    col = node("ShaderNodeVertexColor", -1600, 200, layer_name="Col")
    team = node("ShaderNodeValue", -1600, -200, name="TEAM", label="TEAM (0 Azure, 1 Crimson)")
    team.outputs[0].default_value = 0.0
    alpha = col.outputs["Alpha"]

    cur = col.outputs["Color"]
    if face_image is not None:
        uv = node("ShaderNodeUVMap", -1600, 500, uv_map="UVMap")
        tex = node("ShaderNodeTexImage", -1400, 500, name="FACE", image=face_image, interpolation="Linear",
                   extension="CLIP")
        L.new(uv.outputs[0], tex.inputs[0])
        cur = mix(tex.outputs["Alpha"], cur, tex.outputs["Color"], -1200, 400)
    masks = {}
    yy = 300
    for key, code in (("team_cloth", palette.MASK_TEAM_CLOTH), ("team_glow", palette.MASK_TEAM_GLOW),
                      ("team_dark", palette.MASK_TEAM_DARK)):
        c = node("ShaderNodeMath", -1150, yy, operation="COMPARE")
        c.inputs[1].default_value = code
        c.inputs[2].default_value = 0.1
        L.new(alpha, c.inputs[0])
        masks[key] = c.outputs[0]
        cur = mix(math_("MULTIPLY", c.outputs[0], team.outputs[0], -950, yy), cur,
                  _rgba(palette.TEAM_SWAP[key][0]), -750, yy)
        yy -= 220

    # N.L against LIGHT_DIR (world space, points from the surface toward the light)
    geo = node("ShaderNodeNewGeometry", -1400, -600)
    ldir = node("ShaderNodeCombineXYZ", -1400, -800, name="LIGHT_DIR")
    ldir.inputs[0].default_value, ldir.inputs[1].default_value, ldir.inputs[2].default_value = (-0.5, -0.5, 0.7)
    dot = node("ShaderNodeVectorMath", -1200, -700, operation="DOT_PRODUCT")
    L.new(geo.outputs["Normal"], dot.inputs[0])
    L.new(ldir.outputs[0], dot.inputs[1])
    ndl = dot.outputs["Value"]
    lit = math_("GREATER_THAN", ndl, T_LIT, -1000, -650)
    mid = math_("GREATER_THAN", ndl, T_MID, -1000, -800)
    navy = _rgba(palette.OUTLINE)
    shade = mix(NAVY_MIX, (SHADE, SHADE, SHADE, 1.0), navy, -800, -900)
    # band colour multiplier: shadow -> mid -> lit
    band = mix(mid, shade, (MID, MID, MID, 1.0), -600, -850)
    band = mix(lit, band, (1.0, 1.0, 1.0, 1.0), -400, -800)
    # shadow band = base x 0.66 then 12 % toward navy: do the navy mix on the product, not the multiplier
    shaded = mix(1.0, cur, band, -200, -300, blend="MULTIPLY")
    shadow_mask = math_("SUBTRACT", 1.0, mid, -400, -1000)
    base_x066 = mix(1.0, cur, (SHADE, SHADE, SHADE, 1.0), -200, -500, blend="MULTIPLY")
    shadow_col = mix(NAVY_MIX, base_x066, navy, 0, -500)
    shaded = mix(shadow_mask, shaded, shadow_col, 150, -300)
    # team rim: facing > 1 - width, away from the key light
    lw = node("ShaderNodeLayerWeight", -800, -1150)
    lw.inputs["Blend"].default_value = 0.5
    rim = math_("GREATER_THAN", lw.outputs["Facing"], 1.0 - RIM_WIDTH, -600, -1150)
    rim = math_("MULTIPLY", rim, math_("LESS_THAN", ndl, T_LIT, -600, -1300), -400, -1200)
    rimc = mix(team.outputs[0], _rgba(TEAM_RIM[0]), _rgba(TEAM_RIM[1]), -200, -1200)
    rim_amt = math_("MULTIPLY", rim, RIM_STRENGTH * 0.5, -200, -1350)
    shaded = mix(rim_amt, shaded, rimc, 300, -400)
    # team glow parts ignore light and rim
    out_col = mix(masks["team_glow"], shaded, cur, 450, 0)
    emit = node("ShaderNodeEmission", 650, 0)
    emit.inputs["Strength"].default_value = 1.0
    L.new(out_col, emit.inputs["Color"])
    out = node("ShaderNodeOutputMaterial", 850, 0)
    L.new(emit.outputs[0], out.inputs["Surface"])
    mat.diffuse_color = (0.5, 0.5, 0.5, 1)
    mat.metallic = 0.0
    mat.roughness = 1.0
    return mat


def set_light(mat, d):
    n = mat.node_tree.nodes["LIGHT_DIR"]
    for i in range(3):
        n.inputs[i].default_value = d[i]


def make_flat_material(name, rgb_hex=None, team_white=False):
    """ID / silhouette material: flat colour, or white on team regions and black elsewhere (team-pixel count)."""
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    e = nt.nodes.new("ShaderNodeEmission")
    o = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(e.outputs[0], o.inputs["Surface"])
    if team_white:
        col = nt.nodes.new("ShaderNodeVertexColor")
        col.layer_name = "Col"
        c = nt.nodes.new("ShaderNodeMath")
        c.operation = "GREATER_THAN"
        c.inputs[1].default_value = 0.6
        nt.links.new(col.outputs["Alpha"], c.inputs[0])
        nt.links.new(c.outputs[0], e.inputs["Color"])
    else:
        e.inputs["Color"].default_value = _rgba(rgb_hex)
    return mat


def make_outline_material(name="CM_Toon_Outline", rgb_hex=None):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    e = nt.nodes.new("ShaderNodeEmission")
    e.inputs["Color"].default_value = _rgba(rgb_hex or palette.OUTLINE)
    o = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(e.outputs[0], o.inputs["Surface"])
    mat.use_backface_culling = True
    return mat


def add_outline(ob, width, mat=None):
    """Render-time inverted hull. Never in the export path (the FBX is written before this runs)."""
    om = mat or make_outline_material()
    if om.name not in [m.name for m in ob.data.materials if m]:
        ob.data.materials.append(om)
    sol = ob.modifiers.new("Outline", "SOLIDIFY")
    sol.thickness = width
    sol.offset = 1.0
    sol.use_flip_normals = True
    sol.use_rim = False
    sol.use_even_offset = True
    sol.material_offset = len(ob.data.materials) - 1
    return sol


def set_team(mat, crimson):
    mat.node_tree.nodes["TEAM"].outputs[0].default_value = 1.0 if crimson else 0.0
