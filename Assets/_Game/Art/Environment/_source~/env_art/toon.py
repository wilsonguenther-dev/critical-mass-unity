"""Blender preview of the CM/Toon contract (ARCHITECTURE.md section 5) for headless EEVEE renders.

Mirrors cm_art/toon.py on branch art/warrior-blender (same mask codes, same SHADE / RAMP_STEP, same inverted-hull
outline), extended with the environment's fixed-glow code 0.25. Render-time only; nothing here is exported.
"""
import bpy

from . import palette


def _rgba(h):
    r, g, b = palette.lin(h)
    return (r, g, b, 1.0)


def make_toon_material(name="CM_Toon_Env", crimson=False):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    N, L = nt.nodes, nt.links

    def node(t, **kw):
        n = N.new(t)
        for k, v in kw.items():
            setattr(n, k, v)
        return n

    col = node("ShaderNodeVertexColor", layer_name="Col")

    def is_code(code):
        c = node("ShaderNodeMath", operation="COMPARE")
        c.inputs[1].default_value = code
        c.inputs[2].default_value = 0.1
        L.new(col.outputs["Alpha"], c.inputs[0])
        return c

    cur = col.outputs["Color"]
    if crimson:
        for key, code in (("team_cloth", palette.MASK_TEAM_CLOTH), ("team_glow", palette.MASK_TEAM_GLOW),
                          ("team_dark", palette.MASK_TEAM_DARK)):
            c = is_code(code)
            mix = node("ShaderNodeMix", data_type="RGBA", blend_type="MIX")
            L.new(c.outputs[0], mix.inputs["Factor"])
            L.new(cur, mix.inputs[6])
            mix.inputs[7].default_value = _rgba(palette.TEAM_SWAP[key][0])
            cur = mix.outputs[2]
    glow_t = is_code(palette.MASK_TEAM_GLOW)
    glow_f = is_code(palette.MASK_FIXED_GLOW)
    glow = node("ShaderNodeMath", operation="MAXIMUM")
    L.new(glow_t.outputs[0], glow.inputs[0])
    L.new(glow_f.outputs[0], glow.inputs[1])

    diff = node("ShaderNodeBsdfDiffuse")
    diff.inputs["Color"].default_value = (1, 1, 1, 1)
    s2r = node("ShaderNodeShaderToRGB")
    L.new(diff.outputs[0], s2r.inputs[0])
    bw = node("ShaderNodeRGBToBW")
    L.new(s2r.outputs["Color"], bw.inputs[0])
    ramp = node("ShaderNodeValToRGB")
    ramp.color_ramp.interpolation = "CONSTANT"
    ramp.color_ramp.elements[0].position = 0.0
    ramp.color_ramp.elements[0].color = (palette.SHADE,) * 3 + (1,)
    ramp.color_ramp.elements[1].position = palette.RAMP_STEP
    ramp.color_ramp.elements[1].color = (1, 1, 1, 1)
    L.new(bw.outputs[0], ramp.inputs[0])
    band = node("ShaderNodeMix", data_type="RGBA", blend_type="MIX")
    L.new(glow.outputs[0], band.inputs["Factor"])
    L.new(ramp.outputs["Color"], band.inputs[6])
    band.inputs[7].default_value = (1, 1, 1, 1)
    mul = node("ShaderNodeMix", data_type="RGBA", blend_type="MULTIPLY")
    mul.inputs["Factor"].default_value = 1.0
    L.new(cur, mul.inputs[6])
    L.new(band.outputs[2], mul.inputs[7])
    emit = node("ShaderNodeEmission")
    emit.inputs["Strength"].default_value = 1.0
    L.new(mul.outputs[2], emit.inputs["Color"])
    out = node("ShaderNodeOutputMaterial")
    L.new(emit.outputs[0], out.inputs["Surface"])
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


def apply(ob, mat, outline=0.0):
    ob.data.materials.clear()
    ob.data.materials.append(mat)
    if outline > 0:
        ob.data.materials.append(make_outline_material())
        sol = ob.modifiers.new("Outline", "SOLIDIFY")
        sol.thickness = outline
        sol.offset = 1.0
        sol.use_flip_normals = True
        sol.use_rim = False
        sol.use_even_offset = True
        sol.material_offset = 1
