# Render helpers for the Kaiju parts work (EEVEE toon approximation of CM/Toon + flat silhouettes).
# Colour: vertex "Col" (sRGB, alpha = code/8). Codes 2 and 6 self-lit; 6/8 swapped to the allegiance like CMToonSwap.
import bpy, math, os
from mathutils import Vector, Matrix

GLOW = {"neutral_elder": "B8E833", "azure": "13F1F4", "crimson": "F2563F", "berserk": "EE2CC0",
        "Grove": "5FAE3C", "Brute": "FF8A1F", "Timber": "9A6A3A", "Mire": "4E9A94", "Elder": "B8E833"}
AUTH = "B8E833"
def hx(h): return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))

def scene_setup(res=400):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE'
    try: sc.eevee.taa_render_samples = 8
    except Exception: pass
    sc.view_settings.view_transform = 'Standard'; sc.view_settings.look = 'None'
    sc.render.film_transparent = False
    sc.render.image_settings.file_format = 'PNG'; sc.render.image_settings.color_mode = 'RGBA'
    sc.render.resolution_x = sc.render.resolution_y = res
    w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
    bg = w.node_tree.nodes["Background"]; bg.inputs[0].default_value = (0.25, 0.27, 0.32, 1); bg.inputs[1].default_value = 1.0
    sd = bpy.data.lights.new("sun", 'SUN'); sd.energy = 3.0; sd.angle = 0.05
    sun = bpy.data.objects.new("sun", sd); sc.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(40), 0, math.radians(-35))
    cd = bpy.data.cameras.new("cam"); cam = bpy.data.objects.new("cam", cd); sc.collection.objects.link(cam); sc.camera = cam
    cd.clip_end = 400
    return sc, cam

TINT = None
def toon_material():
    global TINT
    m = bpy.data.materials.get("KToon")
    if m: return m
    m = bpy.data.materials.new("KToon"); m.use_nodes = True; nt = m.node_tree; N = nt.nodes; L = nt.links
    for n in list(N): N.remove(n)
    out = N.new("ShaderNodeOutputMaterial")
    vc = N.new("ShaderNodeVertexColor"); vc.layer_name = "Col"
    dif = N.new("ShaderNodeBsdfDiffuse"); dif.inputs[0].default_value = (1, 1, 1, 1)
    s2r = N.new("ShaderNodeShaderToRGB"); L.new(dif.outputs[0], s2r.inputs[0])
    bw = N.new("ShaderNodeRGBToBW"); L.new(s2r.outputs[0], bw.inputs[0])
    ramp = N.new("ShaderNodeValToRGB"); cr = ramp.color_ramp; cr.interpolation = 'CONSTANT'
    cr.elements[0].position = 0.0; cr.elements[0].color = (0.50, 0.50, 0.56, 1)
    cr.elements[1].position = 0.30; cr.elements[1].color = (0.78, 0.78, 0.80, 1)
    e = cr.elements.new(0.62); e.color = (1, 1, 1, 1)
    L.new(bw.outputs[0], ramp.inputs[0])
    mul = N.new("ShaderNodeMix"); mul.data_type = 'RGBA'; mul.blend_type = 'MULTIPLY'; mul.inputs[0].default_value = 1.0
    L.new(vc.outputs[0], mul.inputs[6]); L.new(ramp.outputs[0], mul.inputs[7])
    tint = N.new("ShaderNodeRGB"); tint.name = "TINT"; tint.outputs[0].default_value = (1, 1, 1, 1); TINT = tint
    mul2 = N.new("ShaderNodeMix"); mul2.data_type = 'RGBA'; mul2.blend_type = 'MULTIPLY'; mul2.inputs[0].default_value = 1.0
    L.new(mul.outputs[2], mul2.inputs[6]); L.new(tint.outputs[0], mul2.inputs[7])
    # self-lit: code 2 or 6
    m8 = N.new("ShaderNodeMath"); m8.operation = 'MULTIPLY'; m8.inputs[1].default_value = 8.0; L.new(vc.outputs[1], m8.inputs[0])
    c2 = N.new("ShaderNodeMath"); c2.operation = 'COMPARE'; c2.inputs[1].default_value = 2.0; c2.inputs[2].default_value = 0.4; L.new(m8.outputs[0], c2.inputs[0])
    c6 = N.new("ShaderNodeMath"); c6.operation = 'COMPARE'; c6.inputs[1].default_value = 6.0; c6.inputs[2].default_value = 0.4; L.new(m8.outputs[0], c6.inputs[0])
    mx = N.new("ShaderNodeMath"); mx.operation = 'MAXIMUM'; L.new(c2.outputs[0], mx.inputs[0]); L.new(c6.outputs[0], mx.inputs[1])
    sel = N.new("ShaderNodeMix"); sel.data_type = 'RGBA'; L.new(mx.outputs[0], sel.inputs[0])
    L.new(mul2.outputs[2], sel.inputs[6]); L.new(vc.outputs[0], sel.inputs[7])
    em = N.new("ShaderNodeEmission"); L.new(sel.outputs[2], em.inputs[0]); L.new(em.outputs[0], out.inputs[0])
    return m

def ink_material():
    m = bpy.data.materials.get("KInk")
    if m: return m
    m = bpy.data.materials.new("KInk"); m.use_nodes = True; nt = m.node_tree
    for n in list(nt.nodes): nt.nodes.remove(n)
    o = nt.nodes.new("ShaderNodeOutputMaterial"); e = nt.nodes.new("ShaderNodeEmission"); e.inputs[0].default_value = (0.07, 0.08, 0.13, 1)
    nt.links.new(e.outputs[0], o.inputs[0]); m.use_backface_culling = True
    return m

def dress(ob, ink=0.035):
    me = ob.data; me.materials.clear(); me.materials.append(toon_material()); me.materials.append(ink_material())
    for p in me.polygons: p.material_index = 0
    if ink > 0 and not any(md.type == 'SOLIDIFY' for md in ob.modifiers):
        sd = ob.modifiers.new("ink", 'SOLIDIFY'); sd.thickness = ink; sd.offset = 1.0; sd.use_flip_normals = True
        sd.use_rim = False; sd.material_offset = 1

def swap_colours(ob, glow_hex, cloth_hex=None):
    """CPU CMToonSwap on a copy of the authored colours: codes 6 (glow) and 8 (cloth) take the target hue at the
    authored value ratio. Original colours kept in a 'ColSrc' layer."""
    me = ob.data
    if "ColSrc" not in me.color_attributes:
        src = me.color_attributes.new("ColSrc", 'BYTE_COLOR', 'CORNER')
        for a, b in zip(me.color_attributes["Col"].data, src.data): b.color_srgb = a.color_srgb
    S = me.color_attributes["ColSrc"].data; D = me.color_attributes["Col"].data
    am = max(hx(AUTH)); g = hx(glow_hex); c = hx(cloth_hex or glow_hex)
    for a, b in zip(S, D):
        r = a.color_srgb; code = round(r[3] * 8)
        if code in (5, 6, 8):
            t = g if code != 8 else c; k = max(r[0], r[1], r[2]) / am
            b.color_srgb = (min(1, t[0] * k), min(1, t[1] * k), min(1, t[2] * k), r[3])
        else: b.color_srgb = r

def look(sc, mode):
    """mode: day | night: tint on the body, world, sun and the ground colour."""
    w = sc.world.node_tree.nodes["Background"]; sun = bpy.data.objects["sun"].data
    if mode == "night":
        TINT.outputs[0].default_value = (0.86, 0.90, 1.02, 1); w.inputs[0].default_value = (0.14, 0.16, 0.24, 1); sun.energy = 2.2
        return (0.11, 0.14, 0.19)
    TINT.outputs[0].default_value = (1, 1, 1, 1); w.inputs[0].default_value = (0.30, 0.31, 0.34, 1); sun.energy = 3.2
    return (0.36, 0.45, 0.30)

def ground(sc, col, size=40):
    g = bpy.data.objects.get("Ground")
    if g is None:
        me = bpy.data.meshes.new("Ground")
        s = size / 2; me.from_pydata([(-s, -s, 0), (s, -s, 0), (s, s, 0), (-s, s, 0)], [], [(0, 1, 2, 3)])
        g = bpy.data.objects.new("Ground", me); sc.collection.objects.link(g)
        m = bpy.data.materials.new("GroundM"); m.use_nodes = True; nt = m.node_tree
        for n in list(nt.nodes): nt.nodes.remove(n)
        o = nt.nodes.new("ShaderNodeOutputMaterial"); e = nt.nodes.new("ShaderNodeEmission"); e.name = "E"
        nt.links.new(e.outputs[0], o.inputs[0]); me.materials.append(m)
    g.data.materials[0].node_tree.nodes["E"].inputs[0].default_value = (*[c ** 2.2 for c in col], 1)
    return g

def cam_game(cam, target, px, yaw=45.0, pitch=55.0, dist=35.0, fov=35.0):
    """The gameplay camera (55 deg down, yaw, FOV 35 vertical at 1080p, ~35 m), cropped to px x px at TRUE 1080p scale."""
    sc = bpy.context.scene; cd = cam.data; cd.type = 'PERSP'; cd.sensor_fit = 'VERTICAL'
    cd.angle_y = 2 * math.atan(math.tan(math.radians(fov / 2)) * px / 1080.0)
    e, y = math.radians(pitch), math.radians(yaw)
    cam.location = target + Matrix.Rotation(y, 3, 'Z') @ Vector((0, -dist * math.cos(e), dist * math.sin(e)))
    cam.rotation_euler = (math.radians(90 - pitch), 0, y)
    sc.render.resolution_x = sc.render.resolution_y = px

def cam_ortho(cam, target, scale, pitch, yaw, res):
    sc = bpy.context.scene; cd = cam.data; cd.type = 'ORTHO'; cd.ortho_scale = scale
    e, y = math.radians(pitch), math.radians(yaw)
    cam.location = target + Matrix.Rotation(y, 3, 'Z') @ Vector((0, -60 * math.cos(e), 60 * math.sin(e)))
    cam.rotation_euler = (math.radians(90 - pitch), 0, y)
    sc.render.resolution_x = sc.render.resolution_y = res

def render(path):
    bpy.context.scene.render.filepath = path; bpy.ops.render.render(write_still=True)

def silhouette_mode(on, objs=()):
    """Flat black fill on white (workbench)."""
    sc = bpy.context.scene
    if on:
        sc.render.engine = 'BLENDER_WORKBENCH'; sh = sc.display.shading; sh.light = 'FLAT'; sh.color_type = 'SINGLE'
        sh.single_color = (0, 0, 0); sh.show_object_outline = False
        sc.render.film_transparent = True; sh.show_shadows = False; sh.show_cavity = False
    else:
        sc.render.engine = 'BLENDER_EEVEE'; sc.render.film_transparent = False
