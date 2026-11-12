"""Blender preview of the CM/Toon contract (ARCHITECTURE.md section 5, U-008), for headless EEVEE renders.

The Unity shader is the real thing; this mirrors it so the art can be judged before CM/Toon exists:
  * flat bible colour from the vertex colour RGB
  * team swap from the vertex colour ALPHA code (palette.MASK_*), driven by the material value node "TEAM"
    (0 = Azure, 1 = Crimson)
  * two-band light ramp (lit / shade, hard step), no specular, no metal
  * team-glow parts are unlit (self-lit glow)
  * inverted-hull outline: Solidify with flipped normals + a back-face-culled outline material (render time only,
    never exported; in Unity the outline is a CM/Toon pass)
"""
import bpy

from . import palette

SHADE = 0.66          # shade band = lit colour x 0.66 (stone_armour x 0.66 ~= core Stone #6F7282)
RAMP_STEP = 0.42      # light threshold between the two bands


def _rgba(h):
    r, g, b = palette.lin(h)
    return (r, g, b, 1.0)


def make_toon_material(name="CM_Toon"):
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

    col = node("ShaderNodeVertexColor", -1400, 200, layer_name="Col")
    team = node("ShaderNodeValue", -1400, -200, name="TEAM", label="TEAM (0 Azure, 1 Crimson)")
    team.outputs[0].default_value = 0.0

    def is_code(code, y):
        c = node("ShaderNodeMath", -1150, y, operation="COMPARE")
        c.inputs[1].default_value = code
        c.inputs[2].default_value = 0.1
        L.new(col.outputs["Alpha"], c.inputs[0])
        return c

    cur = col.outputs["Color"]
    y = 300
    for key, code in (("team_cloth", palette.MASK_TEAM_CLOTH), ("team_glow", palette.MASK_TEAM_GLOW),
                      ("team_dark", palette.MASK_TEAM_DARK)):
        c = is_code(code, y)
        f = node("ShaderNodeMath", -950, y, operation="MULTIPLY")
        L.new(c.outputs[0], f.inputs[0])
        L.new(team.outputs[0], f.inputs[1])
        mix = node("ShaderNodeMix", -750, y, data_type="RGBA", blend_type="MIX")
        L.new(f.outputs[0], mix.inputs["Factor"])
        L.new(cur, mix.inputs[6])
        mix.inputs[7].default_value = _rgba(palette.TEAM_SWAP[key][0])
        cur = mix.outputs[2]
        if key == "team_glow":
            glow_mask = c
        y -= 220

    # two-band light ramp
    diff = node("ShaderNodeBsdfDiffuse", -950, -500)
    diff.inputs["Color"].default_value = (1, 1, 1, 1)
    s2r = node("ShaderNodeShaderToRGB", -750, -500)
    L.new(diff.outputs[0], s2r.inputs[0])
    bw = node("ShaderNodeRGBToBW", -600, -500)
    L.new(s2r.outputs["Color"], bw.inputs[0])
    ramp = node("ShaderNodeValToRGB", -450, -500)
    ramp.color_ramp.interpolation = "CONSTANT"
    ramp.color_ramp.elements[0].position = 0.0
    ramp.color_ramp.elements[0].color = (SHADE, SHADE, SHADE, 1)
    ramp.color_ramp.elements[1].position = RAMP_STEP
    ramp.color_ramp.elements[1].color = (1, 1, 1, 1)
    L.new(bw.outputs[0], ramp.inputs[0])
    # glow parts ignore the ramp
    band = node("ShaderNodeMix", -150, -400, data_type="RGBA", blend_type="MIX")
    L.new(glow_mask.outputs[0], band.inputs["Factor"])
    L.new(ramp.outputs["Color"], band.inputs[6])
    band.inputs[7].default_value = (1, 1, 1, 1)
    mul = node("ShaderNodeMix", 50, 0, data_type="RGBA", blend_type="MULTIPLY")
    mul.inputs["Factor"].default_value = 1.0
    L.new(cur, mul.inputs[6])
    L.new(band.outputs[2], mul.inputs[7])
    emit = node("ShaderNodeEmission", 250, 0)
    emit.inputs["Strength"].default_value = 1.0
    L.new(mul.outputs[2], emit.inputs["Color"])
    out = node("ShaderNodeOutputMaterial", 450, 0)
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
    e.inputs["Color"].default_value = _rgba(palette.OUTLINE)
    o = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(e.outputs[0], o.inputs["Surface"])
    mat.use_backface_culling = True
    return mat


def add_outline(ob, width):
    """Render-time inverted hull. Never saved into the export path (the FBX is written before this runs)."""
    om = make_outline_material()
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
