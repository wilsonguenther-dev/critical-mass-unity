"""Core helpers for the Critical Mass props set: palette, mesh builder, CM/Toon preview material, FBX export.

Conventions (shared with art/environment-blender env_art and art/warrior-blender cm_art):
  * 1 Blender unit = 1 m = 1 Unity unit = 1 cell. Blender Z up; props stand on z = 0, pivot at the footprint centre,
    front faces Blender -Y.
  * One colour attribute named `Col` on face corners: RGB = bible hex (stored linear, the FBX exporter writes sRGB),
    ALPHA = mask code. No textures; one CM_Toon material reads the vertex colour.
  * Mask codes: 0.0 fixed colour (shaded), 0.25 fixed-colour self-lit glow (biomass, corruption, flame, neutral marker),
    0.5 team dark (shaded), 0.75 team glow (self-lit), 1.0 team cloth (shaded). The hull outline is collapsed to zero
    where the mask is a glow code (0.25 / 0.75): glows are unlined.
  * Vertex gradient baked into `Col`: +6% at the top of each prop, -10% at the bottom / crevices (glows exempt).
"""
import math
import random

import bmesh
import bpy
from mathutils import Matrix, Vector, noise

# ---------------------------------------------------------------- palette
NAVY = "#18233B"          # Void Navy: every ink line
WOOD, WOOD_B, WOOD_D = "#C78B50", "#B27440", "#633F22"
STONE, STONE_D, STONE_CORE = "#9C9480", "#5B5E58", "#6F7282"
BRASS = "#C79245"
DIRT, DIRT_D = "#D39A5C", "#A8743F"
GRASS, GRASS_D, MOSS = "#A3C150", "#6F9A3E", "#A7C86A"
ROPE = "#D9B98A"
LEATHER = "#5A3B22"
CREAM = "#F7F0E5"
BIOMASS, BIO_HI, BIO_LO = "#B7F238", "#E9F93E", "#93D82A"
STALK, STALK_L = "#2F6E4C", "#3E9146"
CORRUPT, CORRUPT_D, CORRUPT_GLOW, CORRUPT_TIP = "#652968", "#450E46", "#D23F8E", "#8E3FD1"
LAVA_ROCK, LAVA_GLOW = "#5E4A48", "#FF8A3D"
FLAME, FLAME_CORE = "#FFC27A", "#F7F0E5"
TEAM_GLOW, TEAM_DARK, TEAM_CLOTH = "#4FE3F0", "#1D6A8C", "#127EC0"       # azure values
TEAM_SWAP = {0.5: "#9B4132", 0.75: "#FF7461", 1.0: "#FC725A"}            # crimson values per mask code
NEUTRAL_HI, NEUTRAL_LO = "#D9DAC9", "#9A9CA6"

MASK_FIXED, MASK_GLOW, MASK_TEAM_DARK, MASK_TEAM_GLOW, MASK_TEAM_CLOTH = 0.0, 0.25, 0.5, 0.75, 1.0
GLOW_CODES = (MASK_GLOW, MASK_TEAM_GLOW)

# three-band ramp (ART-DIRECTION 2.2)
BAND_MID, BAND_SHADOW, SHADOW_NAVY_MIX = 0.84, 0.66, 0.12
T_LIT, T_MID = 0.35, -0.05


def srgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def to_lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def lin(h):
    return tuple(to_lin(c) for c in srgb(h))


def M(loc=(0, 0, 0), rot=(0, 0, 0), scale=None):
    """Matrix from location, XYZ euler degrees, optional scale (float or 3-tuple)."""
    m = Matrix.Translation(Vector(loc))
    r = Matrix.Rotation(math.radians(rot[2]), 4, "Z") @ Matrix.Rotation(math.radians(rot[1]), 4, "Y") \
        @ Matrix.Rotation(math.radians(rot[0]), 4, "X")
    s = Matrix.Identity(4)
    if scale is not None:
        sc = (scale,) * 3 if isinstance(scale, (int, float)) else scale
        s = Matrix.Diagonal((sc[0], sc[1], sc[2], 1.0))
    return m @ r @ s


# ---------------------------------------------------------------- primitives (each returns a fresh bmesh)
def bevel_sharp(bm, offset, angle=50.0):
    edges = [e for e in bm.edges if e.is_manifold and e.calc_face_angle(0) > math.radians(angle)]
    if edges and offset > 0:
        bmesh.ops.bevel(bm, geom=edges, offset=offset, offset_type="OFFSET", segments=1, profile=0.5,
                        affect="EDGES", clamp_overlap=True)
    return bm


def box(sx, sy, sz, bevel=0.035, base=True):
    """Box centred in x/y, sitting on z=0 (base=True) or centred."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x *= sx
        v.co.y *= sy
        v.co.z = v.co.z * sz + (sz / 2 if base else 0)
    if bevel > 0:
        bevel_sharp(bm, min(bevel, 0.4 * min(sx, sy, sz)), 50)
    return bm


def cyl(r1, h, segs=8, r2=None, bevel=0.03, base=True):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs, radius1=r1,
                          radius2=r1 if r2 is None else r2, depth=h)
    if base:
        for v in bm.verts:
            v.co.z += h / 2
    if bevel > 0:
        bevel_sharp(bm, min(bevel, 0.3 * h, 0.3 * r1), 60)
    return bm


def ico(r, sub=2):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=sub, radius=r)
    return bm


def uvs(r, u=12, v=8):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=r)
    return bm


def jitter(bm, amp, freq=3.0, seed=0.0, zscale=1.0):
    off = Vector((seed * 13.1, seed * 7.7, seed * 3.3))
    for v in bm.verts:
        d = noise.noise_vector(v.co * freq + off)
        v.co += Vector((d.x, d.y, d.z * zscale)) * amp
    return bm


def tube(pts, radii, sides=6, tip=False, twist=0.0):
    """Closed tube along a polyline. radii per point. tip=True ends in a point."""
    bm = bmesh.new()
    pts = [Vector(p) for p in pts]
    n = len(pts)
    T = [(pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized() for i in range(n)]
    up = Vector((0, 0, 1)) if abs(T[0].z) < 0.9 else Vector((1, 0, 0))
    N = T[0].cross(up).normalized()
    rings = []
    for i in range(n):
        if i > 0:
            ax = T[i - 1].cross(T[i])
            if ax.length > 1e-6:
                N = Matrix.Rotation(T[i - 1].angle(T[i]), 3, ax.normalized()) @ N
        B = T[i].cross(N)
        if tip and i == n - 1:
            rings.append([bm.verts.new(pts[i])])
            continue
        r = radii[i]
        rings.append([bm.verts.new(pts[i] + r * (math.cos(2 * math.pi * k / sides + twist) * N
                                                 + math.sin(2 * math.pi * k / sides + twist) * B))
                      for k in range(sides)])
    s = sides
    for i in range(n - 1):
        A, Bn = rings[i], rings[i + 1]
        for k in range(s):
            if len(Bn) == 1:
                bm.faces.new((A[k], A[(k + 1) % s], Bn[0]))
            else:
                bm.faces.new((A[k], A[(k + 1) % s], Bn[(k + 1) % s], Bn[k]))
    bm.faces.new(list(reversed(rings[0])))
    if not tip:
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm


def torus(R, r, segs=16, sides=6, arc=360.0, start=0.0):
    """Torus in the XY plane (closed when arc=360)."""
    bm = bmesh.new()
    closed = arc >= 359.9
    nseg = segs if closed else segs + 1
    rings = []
    for i in range(nseg):
        a = math.radians(start + arc * i / segs)
        c = Vector((math.cos(a), math.sin(a), 0))
        rings.append([bm.verts.new(c * (R + r * math.cos(2 * math.pi * k / sides))
                                   + Vector((0, 0, r * math.sin(2 * math.pi * k / sides))))
                      for k in range(sides)])
    for i in range(segs if closed else segs):
        A, B = rings[i], rings[(i + 1) % nseg]
        for k in range(sides):
            bm.faces.new((A[k], A[(k + 1) % sides], B[(k + 1) % sides], B[k]))
    if not closed:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm


def cloth(w, h, nx, ny, offset_fn=None, notch=0.0, cut=None, thickness=0.025):
    """Hanging cloth in the XZ plane (faces -Y), top edge at z=0, hangs to -h; V notch of depth `notch` at the
    bottom (swallowtail); cut(u, v) -> True removes a quad (tears)."""
    bm = bmesh.new()
    grid = []
    for j in range(ny + 1):
        row = []
        v = j / ny
        for i in range(nx + 1):
            u = i / nx
            bottom = -h + notch * (1 - abs(2 * u - 1))
            z = v * bottom
            y = offset_fn(u, v) if offset_fn else 0.0
            row.append(bm.verts.new(((u - 0.5) * w, y, z)))
        grid.append(row)
    for j in range(ny):
        for i in range(nx):
            if cut and cut((i + 0.5) / nx, (j + 0.5) / ny):
                continue
            bm.faces.new((grid[j][i], grid[j + 1][i], grid[j + 1][i + 1], grid[j][i + 1]))
    for v in [v for v in bm.verts if not v.link_faces]:
        bm.verts.remove(v)
    bmesh.ops.solidify(bm, geom=bm.faces[:], thickness=thickness)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm


# ---------------------------------------------------------------- prop builder
class Prop:
    """Accumulates parts into one bmesh with the `Col` corner colour and a per-vertex `lined` flag."""

    def __init__(self, name, budget, size_class, target=None, seed=1, outline_px=2.5, walkable=False):
        self.name, self.budget, self.size_class = name, budget, size_class
        self.target = target or {}            # e.g. {"height": 0.5} or {"width": 0.5, "height": 0.7}
        self.outline_px, self.walkable = outline_px, walkable
        self.rng = random.Random(seed)
        self.bm = bmesh.new()
        self.c = self.bm.loops.layers.float_color.new("Col")
        self.ln = self.bm.verts.layers.float.new("lined")

    def add(self, src, col, mask=MASK_FIXED, lined=None, smooth=False, mat=None):
        """col: hex, or callable(face) -> hex or (hex, mask)."""
        if mat is not None:
            bmesh.ops.transform(src, matrix=mat, verts=src.verts[:])
        src.normal_update()
        vmap = {}
        for v in src.verts:
            nv = self.bm.verts.new(v.co)
            vmap[v] = nv
        for f in src.faces:
            try:
                nf = self.bm.faces.new([vmap[v] for v in f.verts])
            except ValueError:
                continue
            nf.smooth = smooth
            res = col(f) if callable(col) else col
            h, m = res if isinstance(res, tuple) else (res, mask)
            r, g, b = srgb(h)
            for loop in nf.loops:
                loop[self.c] = (r, g, b, m)
            is_lined = (m not in GLOW_CODES) if lined is None else lined
            for v in nf.verts:
                v[self.ln] = max(v[self.ln], 1.0 if is_lined else 0.0)
        src.free()
        return self

    def finish(self, collection=None, fit_height=None):
        bm = self.bm
        zs = [v.co.z for v in bm.verts]
        z0, z1 = min(zs), max(zs)
        if fit_height:                      # uniform scale about the pivot so the top lands exactly on the target
            k = fit_height / (z1 - min(0.0, z0))
            for v in bm.verts:
                v.co *= k
            z0, z1 = z0 * k, z1 * k
        span = max(z1 - z0, 1e-6)
        for f in bm.faces:
            for loop in f.loops:
                r, g, b, m = loop[self.c]
                k = 1.0 if m in GLOW_CODES else 0.90 + 0.16 * (loop.vert.co.z - z0) / span
                loop[self.c] = (to_lin(min(1.0, r * k)), to_lin(min(1.0, g * k)), to_lin(min(1.0, b * k)), m)
        me = bpy.data.meshes.new(self.name)
        bm.normal_update()
        bm.to_mesh(me)
        lined = [v[self.ln] for v in bm.verts]
        bm.free()
        ob = bpy.data.objects.new(self.name, me)
        (collection or bpy.context.scene.collection).objects.link(ob)
        vg = ob.vertex_groups.new(name="lined")
        idx = [i for i, w in enumerate(lined) if w > 0.5]
        if idx:
            vg.add(idx, 1.0, "REPLACE")
        attr = me.color_attributes.get("Col")
        if attr is not None:
            me.color_attributes.active_color = attr
            me.color_attributes.render_color_index = me.color_attributes.find("Col")
        ob["size_class"], ob["budget"], ob["outline_px"] = self.size_class, self.budget, self.outline_px
        ob["walkable"] = int(self.walkable)
        for k, v in self.target.items():
            ob["target_" + k] = v
        return ob


def tri_count(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


# ---------------------------------------------------------------- CM/Toon preview (render only; not exported)
def _rgba(h):
    return lin(h) + (1.0,)


def make_toon(name="CM_Toon", crimson=False, ldir=(0.0, -0.643, 0.766), flat=False):
    """Three-band toon: band from N.L against `ldir` (exact thresholds), forced to the shadow band where the sun is
    occluded (cast shadow); glow codes are emitted unshaded. flat=True gives one band plus cast shadow (ground)."""
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

    def math_(op, a, b=None, name=None):
        n = node("ShaderNodeMath", operation=op)
        if name:
            n.name = name
        for i, x in enumerate((a, b)):
            if x is None:
                continue
            if isinstance(x, (int, float)):
                n.inputs[i].default_value = x
            else:
                L.new(x, n.inputs[i])
        return n.outputs[0]

    def mix(fac, a, b, blend="MIX"):
        n = node("ShaderNodeMix", data_type="RGBA", blend_type=blend)
        for sock, x in ((n.inputs["Factor"], fac), (n.inputs[6], a), (n.inputs[7], b)):
            if isinstance(x, (int, float)):
                sock.default_value = x
            elif isinstance(x, tuple):
                sock.default_value = x
            else:
                L.new(x, sock)
        return n.outputs[2]

    col = node("ShaderNodeVertexColor", layer_name="Col")
    alpha = col.outputs["Alpha"]

    def is_code(code):
        c = node("ShaderNodeMath", operation="COMPARE")
        c.inputs[1].default_value = code
        c.inputs[2].default_value = 0.1
        L.new(alpha, c.inputs[0])
        return c.outputs[0]

    base = col.outputs["Color"]
    if crimson:
        for code, h in TEAM_SWAP.items():
            base = mix(is_code(code), base, _rgba(h))
    glow = math_("MAXIMUM", is_code(MASK_GLOW), is_code(MASK_TEAM_GLOW))

    geo = node("ShaderNodeNewGeometry")
    dot = node("ShaderNodeVectorMath", operation="DOT_PRODUCT")
    dot.name = "LDOT"
    dot.inputs[1].default_value = ldir
    L.new(geo.outputs["Normal"], dot.inputs[0])
    nl = dot.outputs["Value"]
    diff = node("ShaderNodeBsdfDiffuse")
    diff.inputs["Color"].default_value = (1, 1, 1, 1)
    s2r = node("ShaderNodeShaderToRGB")
    L.new(diff.outputs[0], s2r.inputs[0])
    bw = node("ShaderNodeRGBToBW")
    L.new(s2r.outputs["Color"], bw.inputs[0])
    sunlit = math_("GREATER_THAN", bw.outputs[0], 0.002)
    if flat:
        nl_eff = math_("SUBTRACT", math_("MULTIPLY", sunlit, 2.0), 1.0)      # lit -> 1, shadow -> -1
    else:
        nl_eff = math_("SUBTRACT", math_("MULTIPLY", math_("ADD", nl, 1.0), sunlit), 1.0)
    is_lit = math_("GREATER_THAN", nl_eff, T_LIT)
    is_mid = math_("GREATER_THAN", nl_eff, T_MID)
    mid = mix(1.0, base, (BAND_MID,) * 3 + (1,), "MULTIPLY")
    shd = mix(SHADOW_NAVY_MIX, mix(1.0, base, (BAND_SHADOW,) * 3 + (1,), "MULTIPLY"), _rgba(NAVY))
    band = mix(is_mid, shd, mix(is_lit, mid, base))
    final = mix(glow, band, base)
    emit = node("ShaderNodeEmission")
    emit.inputs["Strength"].default_value = 1.0
    L.new(final, emit.inputs["Color"])
    out = node("ShaderNodeOutputMaterial")
    L.new(emit.outputs[0], out.inputs["Surface"])
    return mat


def set_ldir(ldir):
    for m in bpy.data.materials:
        if m.use_nodes and m.node_tree and "LDOT" in m.node_tree.nodes:
            m.node_tree.nodes["LDOT"].inputs[1].default_value = ldir


def make_flat(name, hex_, alpha=1.0):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    e = nt.nodes.new("ShaderNodeEmission")
    e.inputs["Color"].default_value = _rgba(hex_)
    o = nt.nodes.new("ShaderNodeOutputMaterial")
    if alpha < 1.0:
        t = nt.nodes.new("ShaderNodeBsdfTransparent")
        mx = nt.nodes.new("ShaderNodeMixShader")
        mx.inputs[0].default_value = alpha
        nt.links.new(t.outputs[0], mx.inputs[1])
        nt.links.new(e.outputs[0], mx.inputs[2])
        nt.links.new(mx.outputs[0], o.inputs["Surface"])
        try:
            mat.surface_render_method = "BLENDED"
        except Exception:
            pass
    else:
        nt.links.new(e.outputs[0], o.inputs["Surface"])
    return mat


def make_outline():
    mat = make_flat("CM_Toon_Outline", NAVY)
    mat.use_backface_culling = True
    try:
        mat.use_backface_culling_shadow = True
    except Exception:
        pass
    return mat


def apply_toon(ob, mat, outline_m=0.0):
    ob.data.materials.clear()
    ob.data.materials.append(mat)
    for md in [m for m in ob.modifiers if m.name == "Outline"]:
        ob.modifiers.remove(md)
    if outline_m > 0:
        ob.data.materials.append(make_outline())
        sol = ob.modifiers.new("Outline", "SOLIDIFY")
        sol.thickness = outline_m
        sol.offset = 1.0
        sol.use_flip_normals = True
        sol.use_rim = False
        sol.use_even_offset = True
        sol.material_offset = 1
        sol.vertex_group = "lined"
        sol.thickness_vertex_group = 0.0
    return ob


# ---------------------------------------------------------------- FBX export (same recipe as cm_art/export.py)
FBX_OPTS = dict(
    use_selection=True, object_types={"MESH"}, use_mesh_modifiers=False, apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL", global_scale=1.0, axis_forward="-Z", axis_up="Y",
    bake_space_transform=True, mesh_smooth_type="FACE", use_tspace=False, colors_type="SRGB",
    prioritize_active_color=True, add_leaf_bones=False, bake_anim=False, use_custom_props=False,
    path_mode="AUTO", embed_textures=False,
)


def export_fbx(path, ob):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    res = bpy.ops.export_scene.fbx(filepath=path, **FBX_OPTS)
    if "FINISHED" not in res:
        raise SystemExit(f"FBX export failed for {ob.name}: {res}")
