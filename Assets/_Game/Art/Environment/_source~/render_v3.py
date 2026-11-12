"""Kit v2: assemble Skybreak Basin + a field demo + lineups from the kit-v2 pieces, run the checks, render.

    B=/Applications/Blender.app/Contents/MacOS/Blender
    $B -b --factory-startup --python-exit-code 1 -P render_v2.py -- <out_dir> [--only a,b] [--no-render] [--scale 2]

Writes <out_dir>/renders/*.png (gameplay framings at 1920x1080 x scale) and <out_dir>/checks.json.
The Blender materials here are a PREVIEW of the Unity shaders (CM/Toon 3-band + CM_Ground); see README.
"""
import json
import math
import os
import sys
import time

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from env_art import kit2, layout2, palette  # noqa: E402
from env_art import layout as L1  # noqa: E402
from env_art import pieces as v1p  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0]) if argv and not argv[0].startswith("--") else os.path.join(HERE, "_out")
ONLY = argv[argv.index("--only") + 1].split(",") if "--only" in argv else None
NO_RENDER = "--no-render" in argv
SCALE = float(argv[argv.index("--scale") + 1]) if "--scale" in argv else 2.0
os.makedirs(os.path.join(OUT, "renders"), exist_ok=True)
T0 = time.time()
CHECKS = {}

CM = Matrix(((-1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
CMi = CM.inverted()
DEMO_DX = 200.0       # demo terrain sits at sim x + 200
LINE_DZ = -160.0      # lineups sit at sim z - 160
PITCH, FOV, DIST, YAW = 55.0, 35.0, 23.4, 45.0


def lin(h):
    return palette.lin(h)


def bl(x, y, z):
    return Vector((-x, z, y))


def pmat(p, dx=0.0, dz=0.0):
    if "a" in p:
        a = Vector((p["a"][0] + dx, p["a"][1], -(p["a"][2] + dz)))
        b = Vector((p["b"][0] + dx, p["b"][1], -(p["b"][2] + dz)))
        f = b - a
        ln = f.length
        fz = f / ln
        X = Vector((0, 1, 0)).cross(fz).normalized() * p.get("w", 1.0)
        Y = fz.cross(X)
        s = ln / kit2.TILE
        Mu = Matrix(((X.x, Y.x, fz.x * s, a.x), (X.y, Y.y, fz.y * s, a.y), (X.z, Y.z, fz.z * s, a.z), (0, 0, 0, 1)))
    else:
        Mu = Matrix.Translation((p["x"] + dx, p["y"], -(p["z"] + dz))) @ Matrix.Rotation(math.radians(p["yaw"]), 4, "Y")
    return CM @ Mu @ CMi


# ------------------------------------------------------------------------------------------------- materials
def _nt(name):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    return mat, nt


def _set(nt, sock, v):
    if v is None:
        return
    if isinstance(v, (int, float)):
        sock.default_value = v
    elif isinstance(v, tuple):
        sock.default_value = v
    else:
        nt.links.new(v, sock)


def M_(nt, op, a, b=None, clamp=False):
    n = nt.nodes.new("ShaderNodeMath")
    n.operation = op
    n.use_clamp = clamp
    _set(nt, n.inputs[0], a)
    if b is not None:
        _set(nt, n.inputs[1], b)
    return n.outputs[0]


def MIX(nt, fac, a, b, blend="MIX"):
    n = nt.nodes.new("ShaderNodeMix")
    n.data_type = "RGBA"
    n.blend_type = blend
    _set(nt, n.inputs[0], fac)
    _set(nt, n.inputs[6], a)
    _set(nt, n.inputs[7], b)
    return n.outputs[2]


def RGB(h):
    return tuple(lin(h)) + (1.0,)


def _light_ratio(nt):
    """N.L (0..1) and a visibility flag from two diffuse probes (independent of sun strength)."""
    d1 = nt.nodes.new("ShaderNodeBsdfDiffuse")
    d2 = nt.nodes.new("ShaderNodeBsdfDiffuse")
    sun = nt.nodes.new("ShaderNodeCombineXYZ")
    to_l = Vector((math.cos(math.radians(50)), 0.0, math.sin(math.radians(50))))
    for i, c in enumerate(to_l):
        sun.inputs[i].default_value = c
    nt.links.new(sun.outputs[0], d2.inputs["Normal"])
    vals = []
    for d in (d1, d2):
        s = nt.nodes.new("ShaderNodeShaderToRGB")
        nt.links.new(d.outputs[0], s.inputs[0])
        bw = nt.nodes.new("ShaderNodeRGBToBW")
        nt.links.new(s.outputs[0], bw.inputs[0])
        vals.append(bw.outputs[0])
    vis = M_(nt, "GREATER_THAN", vals[1], 0.05)
    ndl = M_(nt, "MULTIPLY", M_(nt, "DIVIDE", vals[0], M_(nt, "MAXIMUM", vals[1], 1e-4)), vis)
    return ndl, vis


def _fog(nt, col):
    g = nt.nodes.new("ShaderNodeNewGeometry")
    sp = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(g.outputs["Position"], sp.inputs[0])
    f = M_(nt, "MULTIPLY", M_(nt, "DIVIDE", M_(nt, "SUBTRACT", -3.5, sp.outputs[2]), 6.0, clamp=True), 0.85)
    return MIX(nt, f, col, RGB("#CDEFF8"))


def _emit(nt, mat, col, alpha=None):
    e = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(col, e.inputs["Color"])
    o = nt.nodes.new("ShaderNodeOutputMaterial")
    if alpha is None:
        nt.links.new(e.outputs[0], o.inputs["Surface"])
    else:
        tr = nt.nodes.new("ShaderNodeBsdfTransparent")
        ms = nt.nodes.new("ShaderNodeMixShader")
        _set(nt, ms.inputs[0], alpha)
        nt.links.new(tr.outputs[0], ms.inputs[1])
        nt.links.new(e.outputs[0], ms.inputs[2])
        nt.links.new(ms.outputs[0], o.inputs["Surface"])
        mat.surface_render_method = "BLENDED"


# v3 band sets: per-channel multipliers (linear) of the lit colour for the mid and shadow bands, and the N.L cuts.
def _ratio(a, b):
    la, lb = lin(a), lin(b)
    return tuple(la[i] / lb[i] for i in range(3))


BANDS = {
    "default": dict(mid=(0.84,) * 3, dark=(0.66,) * 3, t_lit=0.35, t_mid=0.02),
    # trees: exactly the art-direction canopy bands #86BD4E / #5E9A45 / #3F6E36
    "foliage": dict(mid=_ratio("#5E9A45", "#86BD4E"), dark=_ratio("#3F6E36", "#86BD4E"), t_lit=0.30, t_mid=0.02, analytic=True),
    # crystal bodies: team dark lit, 0.62 mid, a navy-leaning shadow (lit facets and the highlight streak are self-lit)
    "crystal": dict(lit=(1.9,) * 3, mid=(1.0,) * 3, dark=(0.46, 0.48, 0.56), t_lit=0.18, t_mid=0.02, analytic=True),
}


def _shade3(nt, col, bands="default"):
    B = BANDS[bands]
    ndl, vis = _light_ratio(nt)
    if B.get("analytic"):
        # canopies and crystals band on their own N.L only (no received cast shadow), so a crown lobe never
        # blacks out the lobes under it; they still CAST shadows on the ground
        geo = nt.nodes.new("ShaderNodeNewGeometry")
        dp = nt.nodes.new("ShaderNodeVectorMath")
        dp.operation = "DOT_PRODUCT"
        nt.links.new(geo.outputs["Normal"], dp.inputs[0])
        dp.inputs[1].default_value = (math.cos(math.radians(50)), 0.0, math.sin(math.radians(50)))
        ndl = M_(nt, "MAXIMUM", dp.outputs["Value"], 0.0)
    lit = M_(nt, "GREATER_THAN", ndl, B["t_lit"])
    mid = M_(nt, "GREATER_THAN", ndl, B["t_mid"])
    dark_c = MIX(nt, 1.0, col, tuple(B["dark"]) + (1.0,), "MULTIPLY")
    mid_c = MIX(nt, 1.0, col, tuple(B["mid"]) + (1.0,), "MULTIPLY")
    lit_c = MIX(nt, 1.0, col, tuple(B["lit"]) + (1.0,), "MULTIPLY") if "lit" in B else col
    sh = MIX(nt, lit, MIX(nt, mid, dark_c, mid_c), lit_c)
    navy = M_(nt, "MULTIPLY", M_(nt, "LESS_THAN", ndl, B["t_mid"]), 0.12)
    return MIX(nt, navy, sh, RGB("#18233B"))


def toon_mat(name, crimson=False, bands="default"):
    mat, nt = _nt(name)
    vc = nt.nodes.new("ShaderNodeVertexColor")
    vc.layer_name = "Col"
    col, a = vc.outputs["Color"], vc.outputs["Alpha"]
    if crimson:
        for key, code in (("team_cloth", 1.0), ("team_glow", 0.75), ("team_dark", 0.5)):
            c = M_(nt, "COMPARE", a, code)
            c.node.inputs[2].default_value = 0.05
            col = MIX(nt, c, col, RGB(palette.TEAM_SWAP[key][0]))
    shaded = _shade3(nt, col, bands)
    g1 = M_(nt, "COMPARE", a, 0.25)
    g1.node.inputs[2].default_value = 0.05
    g2 = M_(nt, "COMPARE", a, 0.75)
    g2.node.inputs[2].default_value = 0.05
    glow = M_(nt, "MAXIMUM", g1, g2)
    _emit(nt, mat, _fog(nt, MIX(nt, glow, shaded, col)))
    return mat


def grey_mat():
    mat, nt = _nt("Grey_Ground")
    rgb = nt.nodes.new("ShaderNodeRGB")
    rgb.outputs[0].default_value = RGB("#808080")
    _emit(nt, mat, _shade3(nt, rgb.outputs[0]))
    return mat


def outline_mat(name, far=False):
    mat, nt = _nt(name)
    c = RGB("#18233B")
    if far:
        a, b = lin("#18233B"), lin("#CDEFF8")
        c = tuple((a[i] + b[i]) / 2 for i in range(3)) + (1.0,)
    rgb = nt.nodes.new("ShaderNodeRGB")
    rgb.outputs[0].default_value = c
    _emit(nt, mat, _fog(nt, rgb.outputs[0]))
    mat.use_backface_culling = True
    return mat


def decal_mat(name, radial):
    mat, nt = _nt(name)
    vc = nt.nodes.new("ShaderNodeVertexColor")
    vc.layer_name = "Col"
    uv = nt.nodes.new("ShaderNodeUVMap")
    sp = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(uv.outputs[0], sp.inputs[0])
    if radial:
        r = M_(nt, "MULTIPLY", M_(nt, "SQRT", M_(nt, "ADD", M_(nt, "POWER", M_(nt, "SUBTRACT", sp.outputs[0], 0.5), 2.0),
                                                    M_(nt, "POWER", M_(nt, "SUBTRACT", sp.outputs[1], 0.5), 2.0))), 2.0)
        alpha = M_(nt, "MULTIPLY", M_(nt, "SUBTRACT", 1.0, r, clamp=True), 0.5)
    else:
        alpha = M_(nt, "MULTIPLY", M_(nt, "SUBTRACT", 1.0, sp.outputs[1], clamp=True), 0.5)
    _emit(nt, mat, vc.outputs["Color"], alpha)
    return mat


def ground_mat(name, img, x0, z0, wdt, hgt):
    """PREVIEW of CM_Ground: world-space Voronoi grass cells (mean 2.5 u, 3 tones, 1 px seams), 8 u macro bands
    along the feature distance, flagstones (1.0 u cells) and kerb stones (0.5 u) where the path field says so,
    5% missing stones, dirt blend, ledge shadow band; flat lighting, cast shadows only."""
    mat, nt = _nt(name)
    g = nt.nodes.new("ShaderNodeNewGeometry")
    sp = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(g.outputs["Position"], sp.inputs[0])
    sx, sz = M_(nt, "MULTIPLY", sp.outputs[0], -1.0), sp.outputs[1]

    def vec(a, b):
        c = nt.nodes.new("ShaderNodeCombineXYZ")
        _set(nt, c.inputs[0], a)
        _set(nt, c.inputs[1], b)
        return c.outputs[0]

    P = vec(sx, sz)
    nz = nt.nodes.new("ShaderNodeTexNoise")
    nz.noise_dimensions = "2D"
    nz.inputs["Scale"].default_value = 0.55
    nz.inputs["Detail"].default_value = 1.0
    nt.links.new(P, nz.inputs["Vector"])
    vm = nt.nodes.new("ShaderNodeVectorMath")
    vm.operation = "SUBTRACT"
    nt.links.new(nz.outputs["Color"], vm.inputs[0])
    vm.inputs[1].default_value = (0.5, 0.5, 0.0)
    vs = nt.nodes.new("ShaderNodeVectorMath")
    vs.operation = "SCALE"
    nt.links.new(vm.outputs[0], vs.inputs[0])
    vs.inputs["Scale"].default_value = 0.28
    va = nt.nodes.new("ShaderNodeVectorMath")
    va.operation = "ADD"
    nt.links.new(P, va.inputs[0])
    nt.links.new(vs.outputs[0], va.inputs[1])
    Pw = va.outputs[0]

    def field(xs, zs):
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = img
        t.interpolation = "Linear"
        t.extension = "EXTEND"
        nt.links.new(vec(M_(nt, "DIVIDE", M_(nt, "SUBTRACT", xs, x0), wdt), M_(nt, "DIVIDE", M_(nt, "SUBTRACT", zs, z0), hgt)), t.inputs[0])
        s = nt.nodes.new("ShaderNodeSeparateColor")
        nt.links.new(t.outputs["Color"], s.inputs[0])
        return s.outputs[0], s.outputs[1], s.outputs[2], t.outputs["Alpha"]

    def vor(scale, feature="F1"):
        v = nt.nodes.new("ShaderNodeTexVoronoi")
        v.voronoi_dimensions = "2D"
        v.feature = feature
        v.inputs["Scale"].default_value = scale
        v.inputs["Randomness"].default_value = 1.0
        nt.links.new(Pw, v.inputs["Vector"])
        return v

    def ramp3(fac, cols):
        r = nt.nodes.new("ShaderNodeValToRGB")
        r.color_ramp.interpolation = "CONSTANT"
        els = r.color_ramp.elements
        els.new(0.5)
        for e, pos, c in zip(els, (0.0, 0.34, 0.67), cols):
            e.position = pos
            e.color = RGB(c)
        nt.links.new(fac, r.inputs[0])
        return r.outputs[0]

    def seam(v_edge, scale, half=0.008, aa=0.004):
        d = M_(nt, "DIVIDE", v_edge.outputs["Distance"], scale)
        return M_(nt, "SUBTRACT", 1.0, M_(nt, "DIVIDE", M_(nt, "SUBTRACT", d, half - aa), 2 * aa, clamp=True))

    R, Gf, B, A = field(sx, sz)
    vA, vAe = vor(0.4), vor(0.4, "DISTANCE_TO_EDGE")
    hA = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(vA.outputs["Color"], hA.inputs[0])
    grass = MIX(nt, seam(vAe, 0.4), ramp3(hA.outputs[0], ("#A0BE4E", "#98B64A", "#90AE46")), RGB("#7A9540"))
    n2 = nt.nodes.new("ShaderNodeTexNoise")
    n2.noise_dimensions = "2D"
    n2.inputs["Scale"].default_value = 0.07
    nt.links.new(P, n2.inputs["Vector"])
    band = M_(nt, "ADD", 1.0, M_(nt, "MULTIPLY", 0.04, M_(nt, "SINE", M_(nt, "ADD", M_(nt, "MULTIPLY", Gf, 2 * math.pi / 8.0),
                                                                          M_(nt, "MULTIPLY", n2.outputs["Fac"], 6.0)))))
    cb = nt.nodes.new("ShaderNodeCombineColor")
    for i in range(3):
        _set(nt, cb.inputs[i], band)
    grass = MIX(nt, 1.0, grass, cb.outputs[0], "MULTIPLY")
    grass = MIX(nt, B, grass, RGB("#D39A5C"))
    grass = MIX(nt, M_(nt, "MULTIPLY", A, 0.55), grass, RGB("#6F9A3E"))
    n3 = nt.nodes.new("ShaderNodeTexNoise")
    n3.noise_dimensions = "2D"
    n3.inputs["Scale"].default_value = 0.18
    nt.links.new(P, n3.inputs["Vector"])
    w = M_(nt, "ADD", layout2.PATH_W - 0.3, M_(nt, "MULTIPLY", n3.outputs["Fac"], 0.6))
    vF, vFe = vor(1.0), vor(1.0, "DISTANCE_TO_EDGE")
    pF = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(vF.outputs["Position"], pF.inputs[0])
    RF = field(pF.outputs[0], pF.outputs[1])[0]
    n4 = nt.nodes.new("ShaderNodeTexNoise")
    n4.noise_dimensions = "2D"
    n4.inputs["Scale"].default_value = 0.33
    nt.links.new(vF.outputs["Position"], n4.inputs["Vector"])
    # grass breaks through the paving in patches (about 12% of a plaza), whole stones at a time
    stoneF = M_(nt, "MULTIPLY", M_(nt, "LESS_THAN", RF, M_(nt, "SUBTRACT", w, 0.5)), M_(nt, "LESS_THAN", n4.outputs["Fac"], 0.6))
    vK, vKe = vor(2.0), vor(2.0, "DISTANCE_TO_EDGE")
    pK = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(vK.outputs["Position"], pK.inputs[0])
    RK = field(pK.outputs[0], pK.outputs[1])[0]
    stoneK = M_(nt, "MULTIPLY", M_(nt, "MULTIPLY", M_(nt, "LESS_THAN", RK, w), M_(nt, "GREATER_THAN", RK, M_(nt, "SUBTRACT", w, 0.5))),
                M_(nt, "GREATER_THAN", R, M_(nt, "SUBTRACT", w, 0.8)))
    hF = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(vF.outputs["Color"], hF.inputs[0])
    colF = MIX(nt, M_(nt, "LESS_THAN", hF.outputs[1], 0.05), ramp3(hF.outputs[0], ("#EDD3A6", "#E0C195", "#F3DDB2")), RGB("#D39A5C"))
    colF = MIX(nt, seam(vFe, 1.0), colF, RGB("#8C7556"))
    hK = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(vK.outputs["Color"], hK.inputs[0])
    colK = MIX(nt, seam(vKe, 2.0), ramp3(hK.outputs[0], ("#C9AD84", "#BDA078", "#D6BA90")), RGB("#8C7556"))
    out = MIX(nt, M_(nt, "MULTIPLY", stoneF, M_(nt, "SUBTRACT", 1.0, stoneK)), grass, colF)
    out = MIX(nt, stoneK, out, colK)
    ndl, vis = _light_ratio(nt)
    shadow = MIX(nt, 0.16, MIX(nt, 1.0, out, (0.60, 0.60, 0.60, 1.0), "MULTIPLY"), RGB("#18233B"))
    _emit(nt, mat, MIX(nt, M_(nt, "SUBTRACT", 1.0, vis), out, shadow))
    return mat


# ---------------------------------------------------------------------------------------------------- library
def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scn = bpy.context.scene
    scn.render.engine = "BLENDER_EEVEE"
    scn.eevee.taa_render_samples = 16
    scn.view_settings.view_transform = "Standard"
    scn.view_settings.look = "None"
    w = bpy.data.worlds.new("W")
    scn.world = w
    w.use_nodes = True
    nt = w.node_tree
    nt.nodes.clear()
    bg = nt.nodes.new("ShaderNodeBackground")
    bg.inputs[0].default_value = RGB("#CDEFF8")
    lp = nt.nodes.new("ShaderNodeLightPath")
    blk = nt.nodes.new("ShaderNodeBackground")
    blk.inputs[0].default_value = (0, 0, 0, 1)
    ms = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(lp.outputs["Is Camera Ray"], ms.inputs[0])
    nt.links.new(blk.outputs[0], ms.inputs[1])
    nt.links.new(bg.outputs[0], ms.inputs[2])
    o = nt.nodes.new("ShaderNodeOutputWorld")
    nt.links.new(ms.outputs[0], o.inputs[0])
    sd = bpy.data.lights.new("Sun", "SUN")
    sd.energy = 3.0
    sd.angle = math.radians(0.4)
    sun = bpy.data.objects.new("Sun", sd)
    scn.collection.objects.link(sun)
    to_l = Vector((math.cos(math.radians(50)), 0.0, math.sin(math.radians(50))))
    sun.rotation_euler = (-to_l).to_track_quat("-Z", "Y").to_euler()
    cd = bpy.data.cameras.new("Cam")
    cam = bpy.data.objects.new("Cam", cd)
    scn.collection.objects.link(cam)
    scn.camera = cam
    return scn, cam


MATS = {}
LIB = {}
WF = []          # (object, placement) of every waterfall: foam is refitted to 11 px per shot (Unity: constant-pixel foam)
WFM = {}


def wf_mesh(w):
    """env_waterfall with a foam band of w metres (preview of the constant-pixel foam the Unity shader must draw)."""
    key = round(w * 1000)
    if key not in WFM:
        m = kit2.Mesh("env_waterfall_f%03d" % key, cull="down")
        kit2.waterfall(m, kit2.BY_NAME["env_waterfall"]["seed"], foam_w=key / 1000.0)
        ob = m.finish(bpy.data.collections.get("Lib"))
        for mt in lib_piece("env_waterfall")["me"].materials:
            ob.data.materials.append(mt)
        WFM[key] = ob.data
    return WFM[key]


def fit_foam(cam):
    for ob, p in WF:
        P = ob.matrix_world.translation
        c = proj(cam, P)
        if not (-300 < c.x < 2220 and -300 < c.y < 1380 and c.z > 0):
            continue
        a = c.to_2d()
        px = ((proj(cam, P + Vector((0.15, 0, 0))).to_2d() - a).length + (proj(cam, P + Vector((0, 0.15, 0))).to_2d() - a).length) / 2
        w = max(0.08, min(0.24, 0.15 * 11.0 / max(px, 1e-6)))
        ob.data = wf_mesh(w)
        p["foam_w"] = round(w * 1000) / 1000.0


def lib_piece(name):
    if name in LIB:
        return LIB[name]
    lib = bpy.data.collections.get("Lib")
    if name in kit2.BY_NAME:
        spec = kit2.BY_NAME[name]
        ob = kit2.build_piece(spec, lib)
        mats, olpx, far, cat = spec["mats"], spec["outline_px"], spec["far"], spec["cat"]
    else:
        spec = next(s for s in v1p.PIECES if s["name"] == name)
        ob = v1p.build_piece(spec, lib)
        mats, olpx, far, cat = ["CM_Toon"], 2.0, False, "struct"
    me = ob.data
    for mname in mats:
        me.materials.append(MATS[mname])
    if olpx > 0:
        me.materials.append(MATS["Outline_Far" if far else "Outline"])
    zs = [v.co.z for v in me.vertices]
    LIB[name] = dict(me=me, nmats=len(mats), olpx=olpx, cat=cat, top=max(zs) if zs else 0.0, mat0=mats[0])
    return LIB[name]


def instance(p, coll, dx=0.0, dz=0.0):
    L = lib_piece(p["piece"])
    ob = bpy.data.objects.new(p["piece"], L["me"])
    coll.objects.link(ob)
    ob.matrix_world = pmat(p, dx, dz)
    if p.get("team") == "crimson":
        ob.material_slots[0].link = "OBJECT"
        ob.material_slots[0].material = MATS[L["mat0"] + "_Crimson"]
    if L["olpx"] > 0:
        s = ob.modifiers.new("Outline", "SOLIDIFY")
        s.offset = 1.0
        s.use_flip_normals = True
        s.use_rim = False
        s.use_even_offset = False      # even offset spikes on sharp rock folds
        s.material_offset = L["nmats"]
        s.thickness = 0.02
        ob["olpx"] = L["olpx"]
    return ob


def ground_mesh(name, placements, mat, coll, dx=0.0, dz=0.0):
    bm = bmesh.new()
    for p in placements:
        if p["piece"] == "env_ground_cell":
            x, z, y = p["x"] + dx, p["z"] + dz, p["y"]
            pts = [(x - .5, z - .5), (x + .5, z - .5), (x + .5, z + .5), (x - .5, z + .5)]
        elif p["piece"] == "env_ground_fill_tri":
            Mx = pmat(p, dx, dz)
            pts = []
            for (ux, uz) in ((0.5, -0.5), (0.5, 0.5), (-0.5, -0.5)):
                v = Mx @ Vector((-ux, -uz, 0.0))
                pts.append((-v.x, v.y))
            y = p["y"]
        else:
            continue
        f = bm.faces.new([bm.verts.new(bl(a, y, b)) for (a, b) in pts])
        f.normal_update()
        if f.normal.z < 0:
            f.normal_flip()
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    coll.objects.link(ob)
    return ob


def field_image(name, arr):
    rows, cols = arr.shape[:2]
    img = bpy.data.images.new(name, cols, rows, alpha=True, float_buffer=True)
    img.colorspace_settings.name = "Non-Color"
    img.pixels.foreach_set(arr.reshape(-1))
    return img


# ------------------------------------------------------------------------------------------------------ camera
def cam_game(cam, target):
    tx, ty, tz = target
    c55, s55 = math.cos(math.radians(PITCH)), math.sin(math.radians(PITCH))
    off = (DIST * c55 * math.sin(math.radians(YAW)), DIST * s55, DIST * c55 * math.cos(math.radians(YAW)))
    pos = bl(tx + off[0], ty + off[1], tz + off[2])
    cam.data.type = "PERSP"
    cam.data.sensor_fit = "VERTICAL"
    cam.data.angle = math.radians(FOV)
    cam.data.clip_start, cam.data.clip_end = 0.5, 400
    cam.location = pos
    cam.rotation_euler = (bl(tx, ty, tz) - pos).to_track_quat("-Z", "Y").to_euler()
    bpy.context.view_layer.update()


def upp_at(cam, wp):
    """Metres per 1080p pixel at world point wp for the current camera."""
    if cam.data.type == "ORTHO":
        return cam.data.ortho_scale / 1920.0
    d = (wp - cam.location).length
    return 2 * d * math.tan(math.radians(FOV) / 2) / 1080.0


def set_outlines(cam, objs):
    for ob in objs:
        if "olpx" in ob and ob.modifiers:
            c = ob.matrix_world.translation
            ob.modifiers[0].thickness = ob["olpx"] * upp_at(cam, c)


def proj(cam, wp, res=(1920, 1080)):
    from bpy_extras.object_utils import world_to_camera_view
    v = world_to_camera_view(bpy.context.scene, cam, wp)
    return Vector((v.x * res[0], (1 - v.y) * res[1], v.z))


def render(cam, name, res):
    scn = bpy.context.scene
    scn.render.resolution_x, scn.render.resolution_y = int(res[0] * SCALE), int(res[1] * SCALE)
    scn.render.resolution_percentage = 100
    scn.render.filepath = os.path.join(OUT, "renders", name + ".png")
    if NO_RENDER or (ONLY and name not in ONLY):
        return False
    bpy.ops.render.render(write_still=True)
    print("RENDERED", name, round(time.time() - T0), "s")
    return True


# -------------------------------------------------------------------------------------------------- assembly
def main():
    scn, cam = reset()
    lib = bpy.data.collections.new("Lib")
    scn.collection.children.link(lib)
    bpy.context.view_layer.layer_collection.children["Lib"].exclude = True
    MATS.update({"CM_Toon": toon_mat("CM_Toon"), "CM_Toon_Crimson": toon_mat("CM_Toon_Crimson", True),
                 "CM_Toon_Foliage": toon_mat("CM_Toon_Foliage", bands="foliage"),
                 "CM_Toon_Crystal": toon_mat("CM_Toon_Crystal", bands="crystal"),
                 "CM_Toon_Crystal_Crimson": toon_mat("CM_Toon_Crystal_Crimson", True, bands="crystal"),
                 "Outline": outline_mat("Outline"), "Outline_Far": outline_mat("Outline_Far", True),
                 "CM_GlowDecal": decal_mat("CM_GlowDecal", True), "CM_ShadowDecal": decal_mat("CM_ShadowDecal", False)})

    # ---- the real map
    lay = layout2.place_map()
    pl = lay["placements"]
    sites = [(x, z, 1.6) for (x, z, r, nm) in layout2.map_sites() if nm == "env_crystal_biomass"]
    fx0, fz0, fw, fh = -8.0, -8.0, 128.0, 88.0
    fimg = field_image("Field_Map", layout2.fields(lay, fx0, fz0, fw, fh, 4, sites))
    MATS["CM_Ground"] = ground_mat("CM_Ground", fimg, fx0, fz0, fw, fh)
    world = bpy.data.collections.new("Map")
    scn.collection.children.link(world)
    ground = ground_mesh("Ground_Map", pl, MATS["CM_Ground"], world)
    objs = []
    for p in pl:
        if p["piece"] in ("env_ground_cell", "env_ground_fill_tri") or p.get("cat") == "struct":
            continue
        objs.append(instance(p, world))
        if p["piece"] == "env_waterfall":
            WF.append((objs[-1], p))
    print("MAP objects", len(objs), round(time.time() - T0), "s")

    # ---- demo terrain (field shot)
    demo = layout2.place_demo()
    dimg = field_image("Field_Demo", layout2.fields(demo, -6.0, -6.0, 58.0, 48.0, 4))
    MATS["CM_Ground_Demo"] = ground_mat("CM_Ground_Demo", dimg, -6.0 + DEMO_DX, -6.0, 58.0, 48.0)
    dcol = bpy.data.collections.new("Demo")
    scn.collection.children.link(dcol)
    gdemo = ground_mesh("Ground_Demo", demo["placements"], MATS["CM_Ground_Demo"], dcol, DEMO_DX)
    for p in demo["placements"]:
        if p["piece"] not in ("env_ground_cell", "env_ground_fill_tri"):
            objs.append(instance(p, dcol, DEMO_DX))
            if p["piece"] == "env_waterfall":
                WF.append((objs[-1], p))

    # ---- reference character (2.0 u) helper
    def ref_char(coll, x, y, z):
        m = kit2.Mesh("RefChar", cull="none")
        stone = kit2.C("rock_dark", 1.15)
        m.prism(0, 0, 0.3, 0.0, 1.35, stone, n=12, r_top=0.26)
        m.blob(0, 1.68, 0, 0.32, 0.32, 0.32, stone, sub=2, smooth=True)
        ob = m.finish(coll)
        ob.data.materials.append(MATS["CM_Toon"])
        ob.data.materials.append(MATS["Outline"])
        ob.location = bl(x, y, z)
        s = ob.modifiers.new("Outline", "SOLIDIFY")
        s.offset, s.use_flip_normals, s.use_rim, s.material_offset = 1.0, True, False, 1
        ob["olpx"] = 3.0
        objs.append(ob)
        return ob

    # ---- lineups (gameplay camera, grey ground)
    lcol = bpy.data.collections.new("Lineup")
    scn.collection.children.link(lcol)
    R_ = (1 / math.sqrt(2), -1 / math.sqrt(2))      # screen right in sim
    U_ = (-1 / math.sqrt(2), -1 / math.sqrt(2))     # screen up in sim
    frames = {
        "lineup_1_edges_cliffs": [("env_cliff_s%d" % i, 4.2) for i in range(1, 7)] + [("env_cliff_d%d" % i, 4.2) for i in range(1, 5)]
        + [("env_cliff_corner", 4.2), ("env_cliff_bend", 4.2)],
        "lineup_2_foliage_rocks_crystals": [(n, 0.0) for n in ("env_tree_round_l", "env_tree_round_s", "env_tree_tall_l", "env_tree_tall_s",
                                                                "env_boulder_l", "env_boulder_m", "env_boulder_s", "env_rocks",
                                                                "env_crystal_cluster", "env_crystal_cluster_small", "env_crystal_biomass",
                                                                "env_crystal_corrupt", "env_pebbles", "env_grass_tuft", "env_flowers",
                                                                "env_stump", "env_bush")],
        "lineup_3_water_bridge_ground_sky": [("env_waterfall", 4.2), ("env_bridge_span", 0.0), ("env_bridge_pier", 0.0),
                                             ("env_bridge_cell", 0.0), ("env_ground_placed", 0.0), ("env_sky_rock_s", 2.5),
                                             ("env_sky_rock_m", 2.5), ("env_sky_rock_l", 2.5), ("env_ruin_column_intact", 0.0),
                                             ("env_ruin_column_fallen_block", 0.0), ("env_ruin_block", 0.0), ("env_ruin_stub", 0.0),
                                             ("env_ruin_arch", 0.0)],
    }
    targets = {}
    gm = grey_mat()
    for fi, (fname, items) in enumerate(frames.items()):
        cx, cz = 30.0 * fi, LINE_DZ
        ncol = 6 if len(items) > 12 else 4
        gy = -3.0 if fi != 1 else 0.0
        bm = bmesh.new()
        q = [bm.verts.new(bl(cx + a, gy, cz + b)) for (a, b) in ((-30, -30), (30, -30), (30, 30), (-30, 30))]
        bm.faces.new(q)
        me = bpy.data.meshes.new("grey")
        bm.to_mesh(me)
        me.materials.append(gm)
        gob = bpy.data.objects.new("GreyGround", me)
        lcol.objects.link(gob)
        for k, (nm, y) in enumerate(items):
            col, row = k % ncol, k // ncol
            a = (col - (ncol - 1) / 2) * (24.0 / ncol)
            b = ((len(items) - 1) // ncol / 2 - row) * 4.6
            x, z = cx + R_[0] * a + U_[0] * b, cz + R_[1] * a + U_[1] * b
            yaw = -45.0 if nm.startswith("env_cliff_") or nm in ("env_waterfall", "env_bridge_span") else 0.0
            p = dict(piece=nm, x=x, y=y + (0 if fi != 1 else 0), z=z, yaw=yaw, team="crimson" if nm == "env_crystal_cluster_small" else None)
            if nm == "env_bridge_span":
                d = (math.cos(math.radians(-45)), math.sin(math.radians(-45)))
                for t in range(2):
                    p2 = dict(piece=nm, a=(x + d[0] * 2.04 * t, 0.0, z + d[1] * 2.04 * t), b=(x + d[0] * 2.04 * (t + 1), 0.0, z + d[1] * 2.04 * (t + 1)))
                    objs.append(instance(p2, lcol))
                continue
            objs.append(instance(p, lcol))
            if nm.startswith("env_cliff_s") or nm.startswith("env_cliff_d") or nm == "env_waterfall":
                L = float(nm[-1]) * (math.sqrt(2) if "_d" in nm else 1.0) if nm != "env_waterfall" else 1.0
                xs = -0.5 if nm == "env_waterfall" else 0.0
                for s_ in range(int(round(L)) if "_s" in nm or nm == "env_waterfall" else 0):
                    # a strip of ground behind the lip (local +Z = inland)
                    pass
                lp = dict(piece="env_lip_rim_diag_a" if "_d" in nm else "env_lip_rim_straight_a", x=x, y=y, z=z, yaw=yaw)
                if nm == "env_waterfall":
                    lp["x"], lp["z"] = x - 0.5 * math.cos(math.radians(yaw)), z - 0.5 * math.sin(math.radians(yaw))
                    objs.append(instance(dict(piece="env_cliff_s1", x=lp["x"], y=y, z=lp["z"], yaw=yaw), lcol))
                nseg = int(nm[-1]) if nm != "env_waterfall" else 1
                step = math.sqrt(2) if "_d" in nm else 1.0
                for s_ in range(nseg):
                    lps = dict(lp)
                    lps["x"] += math.cos(math.radians(yaw)) * step * s_
                    lps["z"] += math.sin(math.radians(yaw)) * step * s_
                    objs.append(instance(lps, lcol))
                # ground strip on top (inland is sim direction (sin yaw, -cos yaw))
                gbm = bmesh.new()
                ux, uz = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
                ix, iz = uz, -ux
                L = nseg * step
                pts = [(lp["x"], lp["z"]), (lp["x"] + ux * L, lp["z"] + uz * L), (lp["x"] + ux * L + ix * 1.2, lp["z"] + uz * L + iz * 1.2),
                       (lp["x"] + ix * 1.2, lp["z"] + iz * 1.2)]
                f = gbm.faces.new([gbm.verts.new(bl(a, y, b)) for (a, b) in pts])
                f.normal_update()
                if f.normal.z < 0:
                    f.normal_flip()
                gme = bpy.data.meshes.new("strip")
                gbm.to_mesh(gme)
                gme.materials.append(MATS["CM_Ground"])
                lcol.objects.link(bpy.data.objects.new("strip", gme))
        # a little terrace (ledge kit) and ground pieces in frame 3
        if fi == 2:
            tx, tz = cx + R_[0] * 9 + U_[0] * -6, cz + R_[1] * 9 + U_[1] * -6
            g = layout2.Grid([L1.FLOOR] * 64, [0.75 if (2 <= i % 8 <= 5 and 2 <= i // 8 <= 4) else 0.0 for i in range(64)], 8, 8)
            rim_, led_ = layout2.boundary(g, [])
            e_, _ = layout2.edge_pieces([], led_)
            gp = [dict(piece="env_ground_cell", x=c % 8 + 0.5, y=g.hgt[c], z=c // 8 + 0.5, yaw=0.0) for c in range(64)]
            ground_mesh("terrace", gp, MATS["CM_Ground"], lcol, tx - 4, tz - 4)
            for p in e_:
                objs.append(instance(p, lcol, tx - 4, tz - 4))
        mid = (cx, 1.5 if fi != 1 else 0.6, cz)
        targets[fname] = mid
        ref_char(lcol, cx + U_[0] * -7.5 + R_[0] * -10.5, gy if fi == 1 else 0.0, cz + U_[1] * -7.5 + R_[1] * -10.5)
    ref_char(world, 63.0, 0.75, 45.5)

    if "--probe" in argv:
        cam_game(cam, (23.0 + DEMO_DX, 0.3, 17.0))
        k = 0
        for ob in objs:
            if ob.name.startswith("env_cliff_s") and ob.users_collection[0].name == "Demo":
                cs = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
                ps = [proj(cam, c) for c in cs]
                print("PROBE", ob.name, "z", round(min(c.z for c in cs), 2), round(max(c.z for c in cs), 2),
                      "scr x", round(min(p.x for p in ps)), round(max(p.x for p in ps)), "y", round(min(p.y for p in ps)), round(max(p.y for p in ps)),
                      "hide", ob.hide_render, len(ob.data.polygons), [m.name for m in ob.data.materials])
                k += 1
                if k > 6:
                    break
        return
    # ------------------------------------------------------------------------------------------------- checks
    checks(lay, pl, ground, objs, cam)
    CHECKS["tiling_demo_terrace"] = tiling_rays(demo, gdemo, DEMO_DX)

    # ------------------------------------------------------------------------------------------------ renders
    shots = []
    wfs = [p for p in pl if p["piece"] == "env_waterfall"]
    wf = min(wfs, key=lambda p: math.hypot(p["x"] - 41.5, p["z"] - 47.0))
    crys = [p for p in pl if p.get("cat") == "crystal" and p["x"] < 40 and p.get("team")]
    best = max(crys, key=lambda p: sum(1 for q in pl if q.get("cat") in ("crystal", "tree", "rock_big") and math.hypot(q["x"] - p["x"], q["z"] - p["z"]) < 7))
    shots += [("key_map_hill_gameplay", (63.0, 0.75, 45.5)), ("field_ledge_path_bridge", (23.0 + DEMO_DX, 0.3, 17.0)),
              ("rim_waterfall", (wf["x"], wf["y"], wf["z"])), ("route_crystals", (best["x"], best["y"], best["z"]))]
    shots += [(k, v) for k, v in targets.items()]
    CHECKS["shots"] = {k: [round(c, 2) for c in v] for k, v in shots}
    for name, tgt in shots:
        cam_game(cam, tgt)
        set_outlines(cam, objs)
        fit_foam(cam)
        measure(name, cam, pl if not name.startswith("field") else [dict(q, x=q.get("x", 0) + DEMO_DX) for q in demo["placements"]], tgt)
        render(cam, name, (1920, 1080))
    # overview, orthographic north-up (bible p12 framing)
    cam.data.type = "ORTHO"
    cam.data.sensor_fit = "AUTO"
    cam.data.ortho_scale = 124.0
    cam.location = bl(56.0, 60.0, 36.0)
    cam.rotation_euler = (0.0, 0.0, math.pi)
    bpy.context.view_layer.update()
    for ob in objs:
        if "olpx" in ob and ob.modifiers:
            ob.modifiers[0].thickness = ob["olpx"] * 124.0 / 1240.0 * 0.6
    sv = SCALE
    globals()["SCALE"] = 1.0
    render(cam, "p12_overview_north_up", (1240, 800))
    globals()["SCALE"] = sv
    with open(os.path.join(OUT, "checks.json"), "w") as f:
        json.dump(CHECKS, f, indent=1)
    print("V3_DONE", round(time.time() - T0), "s")


# ------------------------------------------------------------------------------------------------------- checks
def checks(lay, pl, ground, objs, cam):
    g = lay["g"]
    # tiling: 5 rays per walkable land cell into the ground mesh; bridges into decks
    dg = bpy.context.evaluated_depsgraph_get()
    bvh = BVHTree.FromObject(ground, dg)
    miss = mism = n = 0
    ltris = [(layout2.fill_tri(f), f[4]) for f in lay.get("lfills", [])]

    def expect(px, pz, h):
        for (A, B_, C_), hh in ltris:
            d1 = (px - B_[0]) * (A[1] - B_[1]) - (A[0] - B_[0]) * (pz - B_[1])
            d2 = (px - C_[0]) * (B_[1] - C_[1]) - (B_[0] - C_[0]) * (pz - C_[1])
            d3 = (px - A[0]) * (C_[1] - A[1]) - (C_[0] - A[0]) * (pz - A[1])
            if not ((d1 < 0 or d2 < 0 or d3 < 0) and (d1 > 0 or d2 > 0 or d3 > 0)):
                return hh
        return h
    for cz in range(g.H):
        for cx in range(g.W):
            if not g.land(cx, cz):
                continue
            h = g.h(cx, cz)
            for (px, pz) in ((cx + .02, cz + .02), (cx + .98, cz + .02), (cx + .02, cz + .98), (cx + .98, cz + .98), (cx + .5, cz + .5)):
                n += 1
                hit = bvh.ray_cast(bl(px, h + 2.0, pz), Vector((0, 0, -1)), 10.0)
                if hit[0] is None:
                    miss += 1
                elif abs(hit[0].z - expect(px, pz, h)) > 0.12:
                    mism += 1
    # bridges: rays onto span/bridge-cell decks
    verts, polys = [], []
    for ob in objs:
        if ob.name.startswith("env_bridge_span") or ob.name.startswith("env_bridge_cell"):
            if ob.users_collection[0].name != "Map":
                continue
            base = len(verts)
            verts += [ob.matrix_world @ v.co for v in ob.data.vertices]
            polys += [[base + i for i in p.vertices] for p in ob.data.polygons]
    bb = BVHTree.FromPolygons(verts, polys)
    bmiss = bn = 0
    BMISS = []
    for cz in range(g.H):
        for cx in range(g.W):
            if g.t(cx, cz) != L1.BRIDGE:
                continue
            bn += 1
            h = g.h(cx, cz)
            hit = bb.ray_cast(bl(cx + .5, h + 2.0, cz + .5), Vector((0, 0, -1)), 10.0)
            if hit[0] is None or abs(hit[0].z - h) > 0.2:
                bmiss += 1
                BMISS.append((cx, cz, round(h, 2), None if hit[0] is None else round(hit[0].z, 2)))
    # rim coverage: every rim unit edge has exactly one lip and one skirt over it
    lips = [(p["x"], p["z"], p["yaw"], 1.0 if "straight" in p["piece"] else math.sqrt(2)) for p in pl if p["piece"].startswith("env_lip_") and "corner" not in p["piece"]]
    lipkeys = {(round(x, 3), round(z, 3), round(y % 360, 1)) for (x, z, y, L) in lips}
    nolip = noskirt = 0
    for e in lay["rim"]:
        d = layout2._dir(e)
        if (round(e["p"][0], 3), round(e["p"][1], 3), round(layout2.yaw_of(d) % 360, 1)) not in lipkeys:
            nolip += 1
        mx, mz = (e["p"][0] + e["q"][0]) / 2, (e["p"][1] + e["q"][1]) / 2
        c = 0
        for (P, dd, Lk, h) in lay["skirts"]:
            t = (mx - P[0]) * dd[0] + (mz - P[1]) * dd[1]
            if 0 < t < Lk and abs((mx - P[0]) * dd[1] - (mz - P[1]) * dd[0]) < 1e-6 and abs(h - e["h"]) < 1e-6:
                c += 1
        noskirt += c != 1
    sk = lay["skirts"]
    long_ = sum(L for (_, _, L, _) in sk if L >= 2.99) / max(1e-9, sum(L for (_, _, L, _) in sk))
    CHECKS["tiling"] = dict(rays=n, holes=miss, height_mismatch=mism, bridge_cells=bn, bridge_misses=bmiss,
                            rim_unit_edges=len(lay["rim"]), edges_without_lip=nolip, edges_not_covered_once_by_skirt=noskirt,
                            fill_triangles=len(lay["fills"]), ledge_fill_triangles=len(lay.get("lfills", [])), bridge_miss_cells=BMISS[:8], skirt_pieces=len(sk), share_of_rim_in_3to6u_pieces=round(long_, 3),
                            ground_inside_map=all(0 <= p["x"] <= L1.W and 0 <= p["z"] <= L1.H for p in pl if p["cat"] == "ground"))
    # nothing over 0.3 u on walkable cells (except edge trees)
    viol = {}
    for p in pl:
        if p.get("cat") in ("ground", "edge", "cliff", "struct", "deco") or "a" in p:
            continue
        cx, cz = int(math.floor(p["x"])), int(math.floor(p["z"]))
        t = g.t(cx, cz)
        if not L1.walkable(t):
            continue
        top = lib_piece(p["piece"])["top"]
        if top > 0.3 and not (p.get("cat") == "tree" and p.get("edge")):
            viol[p["piece"]] = viol.get(p["piece"], 0) + 1
    CHECKS["tall_on_walkable_cells"] = viol
    CHECKS["boulder_facets_before_bevel"] = {k: v for k, v in kit2.FACET_LOG.items()}
    # seam statistics of the grass cell pattern (reference: jittered-grid Voronoi, spacing 2.5, randomness 1)
    rng = np.random.default_rng(7)
    S = 2.5
    gx, gz = np.meshgrid(np.arange(40), np.arange(40))
    sites = np.stack([(gx + rng.random(gx.shape)) * S, (gz + rng.random(gz.shape)) * S], -1).reshape(-1, 2)
    lens = []
    areas = []
    for i, s in enumerate(sites):
        if not (8 * S < s[0] < 32 * S and 8 * S < s[1] < 32 * S):
            continue
        d = np.hypot(*(sites - s).T)
        nb = sites[np.argsort(d)[1:25]]
        poly = [(s[0] - 9, s[1] - 9), (s[0] + 9, s[1] - 9), (s[0] + 9, s[1] + 9), (s[0] - 9, s[1] + 9)]
        for o in nb:
            mvec = (o - s)
            c = (o + s) / 2
            newp = []
            for k in range(len(poly)):
                a, b = np.array(poly[k]), np.array(poly[(k + 1) % len(poly)])
                fa, fb = np.dot(a - c, mvec), np.dot(b - c, mvec)
                if fa <= 0:
                    newp.append(tuple(a))
                if fa * fb < 0:
                    newp.append(tuple(a + (b - a) * fa / (fa - fb)))
            poly = newp
        P_ = np.array(poly)
        e = np.hypot(*(np.roll(P_, -1, 0) - P_).T)
        lens += list(e[e > 1e-6])
        areas.append(0.5 * abs(np.dot(P_[:, 0], np.roll(P_[:, 1], -1)) - np.dot(P_[:, 1], np.roll(P_[:, 0], -1))))
    lens = np.array(lens)
    eq = np.sqrt(np.array(areas))
    CHECKS["grass_cells_reference"] = dict(cells=len(areas), mean_cell_across_u=round(float(eq.mean()), 2),
                                           p5_p95_across_u=[round(float(np.percentile(eq, 5)), 2), round(float(np.percentile(eq, 95)), 2)],
                                           max_seam_chord_u=round(float(lens.max()), 2), share_seams_over_3u=round(float((lens > 3).mean()), 4),
                                           note="seams are additionally domain-warped by noise (amplitude 0.14 u, ~1.8 u wavelength), so no chord is a straight line")
    print("CHECKS", json.dumps(CHECKS)[:900])


def tiling_rays(lay, ground, dx=0.0):
    """Holes / height mismatches over every land cell (5 rays each), ledge-fill triangles expected one step up."""
    g = lay["g"]
    bvh = BVHTree.FromObject(ground, bpy.context.evaluated_depsgraph_get())
    tris = [(layout2.fill_tri(f), f[4]) for f in lay.get("lfills", [])]

    def inside(px, pz, A, B_, C_):
        d1 = (px - B_[0]) * (A[1] - B_[1]) - (A[0] - B_[0]) * (pz - B_[1])
        d2 = (px - C_[0]) * (B_[1] - C_[1]) - (B_[0] - C_[0]) * (pz - C_[1])
        d3 = (px - A[0]) * (C_[1] - A[1]) - (C_[0] - A[0]) * (pz - A[1])
        if (d1 < 0 or d2 < 0 or d3 < 0) and (d1 > 0 or d2 > 0 or d3 > 0):
            return 0
        return 1 if min(abs(d1), abs(d2), abs(d3)) > 1e-9 else 2     # 2 = on the hypotenuse: either height is right
    n = miss = mism = raised = 0
    for cz in range(g.H):
        for cx in range(g.W):
            if not g.land(cx, cz):
                continue
            for (px, pz) in ((cx + .02, cz + .02), (cx + .98, cz + .02), (cx + .02, cz + .98), (cx + .98, cz + .98), (cx + .5, cz + .5)):
                n += 1
                h = g.h(cx, cz)
                ok = [h]
                for (A, B_, C_), hh in tris:
                    k = inside(px, pz, A, B_, C_)
                    if k:
                        ok = [hh] if k == 1 else [h, hh]
                        h = hh
                        raised += k == 1
                        break
                hit = bvh.ray_cast(bl(px + dx, h + 2.0, pz), Vector((0, 0, -1)), 10.0)
                if hit[0] is None:
                    miss += 1
                elif min(abs(hit[0].z - o) for o in ok) > 0.12:
                    mism += 1
    return dict(rays=n, holes=miss, height_mismatch=mism, ledge_fill_triangles=len(tris), rays_on_raised_half_cells=raised,
                ledge_edges=len(lay["ledge"]), diagonal_ledge_edges=sum(1 for e in lay["ledge"] if e["kind"] == "dg"))


def measure(name, cam, pl, tgt):
    """Pixel measurements at 1080p for the current camera."""
    T = bl(*tgt)
    a, b = proj(cam, T), proj(cam, T + Vector((0, 0, 2.0)))
    r = dict(char_px=round(abs(a.y - b.y), 1))
    inside = lambda q: 0 <= q.x <= 1920 and 0 <= q.y <= 1080 and q.z > 0
    cnt = {}
    for p in pl:
        if "a" in p or p.get("cat") in ("ground", "edge", "cliff", "struct", None):
            continue
        if inside(proj(cam, bl(p["x"], p["y"], p["z"]))):
            cnt[p["cat"]] = cnt.get(p["cat"], 0) + 1
    r["in_frame"] = cnt
    r["small_props"] = cnt.get("small", 0)
    r["trees_big_rocks"] = cnt.get("tree", 0) + cnt.get("rock_big", 0)
    # ink width (px) of lips near the screen centre
    ink = []
    for p in pl:
        if not (p["piece"].startswith("env_lip_") and "straight" in p["piece"]):
            continue
        yaw = math.radians(p["yaw"])
        d = (math.cos(yaw), math.sin(yaw))
        nrm = (-d[1], d[0])
        mx, mz = p["x"] + d[0] * 0.5, p["z"] + d[1] * 0.5
        c = proj(cam, bl(mx, p["y"], mz))
        if not (480 < c.x < 1440 and 270 < c.y < 810 and c.z > 0):
            continue
        e2 = proj(cam, bl(mx + d[0] * 0.3, p["y"], mz + d[1] * 0.3))
        sd = (e2 - c).to_2d().normalized()
        ns = Vector((-sd.y, sd.x))
        pts = [bl(mx, p["y"], mz), bl(mx + nrm[0] * kit2.INK_W, p["y"] - 0.012, mz + nrm[1] * kit2.INK_W),
               bl(mx + nrm[0] * kit2.INK_W, p["y"] - 0.012 - kit2.INK_DROP, mz + nrm[1] * kit2.INK_W)]
        ws = [proj(cam, q).to_2d().dot(ns) for q in pts]
        ink.append(max(ws) - min(ws))
    if ink:
        r["ink_px_central_lips"] = dict(n=len(ink), min=round(min(ink), 2), median=round(sorted(ink)[len(ink) // 2], 2), max=round(max(ink), 2))
    foam = []
    for p in pl:
        if p["piece"] != "env_waterfall":
            continue
        yaw = math.radians(p["yaw"])
        d = (math.cos(yaw), math.sin(yaw))
        nrm = (-d[1], d[0])
        c = proj(cam, bl(p["x"], p["y"], p["z"]))
        if not inside(c):
            continue
        for (u, v, du, dv) in ((0.44, -0.2, -1, 0), (-0.44, -0.2, 1, 0), (0.0, -0.70, 0, 1), (0.0, 0.015, 0, 1)):
            # (u along lip, v outward; foam goes inward by du/dv). The last row is the lip collar (0.17 u across).
            P0 = (p["x"] + d[0] * u + nrm[0] * v, p["z"] + d[1] * u + nrm[1] * v)
            wdt = p.get("foam_w", kit2.FOAM_W)
            P1 = (P0[0] + (d[0] * du + nrm[0] * dv) * wdt, P0[1] + (d[1] * du + nrm[1] * dv) * wdt)
            foam.append((proj(cam, bl(P0[0], p["y"], P0[1])).to_2d() - proj(cam, bl(P1[0], p["y"], P1[1])).to_2d()).length)
    if foam:
        r["foam_px"] = dict(n=len(foam), min=round(min(foam), 2), max=round(max(foam), 2))
    CHECKS.setdefault("measure", {})[name] = r
    print("MEASURE", name, r)


main()
