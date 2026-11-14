"""Farmer look fix (art/rig-farmer-2, 2026-11-14): three problems the batch-8.2 captures showed, fixed on the HY Farmer
FBX itself. The skeleton is not touched, so Farmer_Clips.fbx (bake_clips.py) stays valid.

  1. TEAM COLOUR. The toon shader recolours only vertices whose alpha code is 8 (cloth). The old Farmer had 9.5% of
     its Hero corners on code 8 (a teal #4999A5 tunic); the HY Farmer has 0.4%, most on the staff, and her lime outfit
     is code 2 (fixed, self-lit, never team coloured). Every non-staff code-2 corner becomes code 8 with the authored
     Azure cloth hue at the same brightness (the shader swaps hue and keeps the brightness ratio).
  2. STAFF. The staff is a rigid island on handslot.r, 2.03 m long, with the grip 0.52 m from one end, so after the arm
     drops 1.5 m of it runs up the forearm line through her body; its dark slate shaft (#2E2D3B) reads as a thin dark
     (and, with the Crimson rim, red) line. It is recentred on the grip along its own axis (shape and length kept: a 0.72
     shortening squashed the crook), and the shaft is recoloured light wood at the same relative values (its code-8 team accents are kept).
  3. FACE. Checked, not changed: the first run (GitHub 43610826584) rendered her face; eyes, brows and mouth are painted
     in the vertex colour on Hero and LOD0 (dark brown, which is why a near-black colour scan missed them). The face
     shell code stays behind ADD_FACE = False for a model that has none.

Writes <out>/Farmer.fbx (same objects, names, bones and export recipe as the class FBX), renders of the head and
the whole body before and after, and <out>/fix_farmer_report.json. Usage: blender -b --python fix_farmer_look.py --
<out> <repo>
"""
import bpy, sys, os, json, math
import numpy as np
import bmesh
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:]
OUT, REPO = argv[0], argv[1]
FBX = "Assets/_Game/Art/Farmer/Farmer.fbx"
os.makedirs(OUT, exist_ok=True)
REP = {}
LOG = []
AUTHORED_CLOTH = (0x49 / 255, 0x99 / 255, 0xA5 / 255)   # ArtMapping.FarmerAzure cloth
WOOD = (0xA8 / 255, 0x76 / 255, 0x45 / 255)
EYE = (0x24 / 255, 0x17 / 255, 0x12 / 255)
SHINE = (1.0, 1.0, 1.0)
MOUTH = (0x6E / 255, 0x2C / 255, 0x2A / 255)
STAFF_SCALE = 1.0      # run 43610826584: 0.72 squashed the crook into a whip shape; recentre only
ADD_FACE = False       # run 43610826584: the HY Farmer HAS a face painted in vertex colour (eyes, brows, mouth)


def log(*a):
    s = " ".join(str(x) for x in a)
    print("[fix]", s, flush=True)
    LOG.append(s)


bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=os.path.join(REPO, FBX), use_anim=False, use_custom_normals=False,
                         ignore_leaf_bones=False, automatic_bone_orientation=False,
                         primary_bone_axis="Y", secondary_bone_axis="X")
objs = [o for o in bpy.data.objects if o not in before]
arm = next(o for o in objs if o.type == "ARMATURE")
meshes = {o.name: o for o in objs if o.type == "MESH"}
arm.data.pose_position = "REST"
bpy.context.view_layer.update()
log("objects", sorted(o.name for o in objs), "bones", len(arm.data.bones))
REP["objects"] = sorted(o.name for o in objs)
REP["bones"] = len(arm.data.bones)


def colour_attr(me):
    ca = me.color_attributes
    a = ca.active_color if ca.active_color is not None else (ca[0] if len(ca) else None)
    return a


def corners(me):
    a = colour_attr(me)
    n = len(a.data)
    buf = np.zeros(n * 4, np.float32)
    a.data.foreach_get("color_srgb", buf)
    return a, buf.reshape(n, 4)


def put(a, arr):
    a.data.foreach_set("color_srgb", arr.reshape(-1).astype(np.float32))


def group_weights(ob, name):
    g = ob.vertex_groups.get(name)
    w = np.zeros(len(ob.data.vertices), np.float32)
    if g is None:
        return w
    gi = g.index
    for v in ob.data.vertices:
        for e in v.groups:
            if e.group == gi:
                w[v.index] = e.weight
    return w


# ------------------------------------------------------------------ renders (Workbench, vertex colour)
def setup_render():
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    for ct in ("VERTEX", "ATTRIBUTE", "MATERIAL"):
        try:
            scene.display.shading.color_type = ct
            break
        except TypeError:
            continue
    scene.display.shading.background_type = "VIEWPORT"
    scene.display.shading.background_color = (0.62, 0.64, 0.66)
    scene.render.film_transparent = False
    cam_d = bpy.data.cameras.new("cam")
    cam_d.type = "ORTHO"
    cam = bpy.data.objects.new("cam", cam_d)
    scene.collection.objects.link(cam)
    scene.camera = cam
    return cam


CAM = setup_render()


def shoot(tag, show):
    for n, o in meshes.items():
        o.hide_render = n != show
    shots = {"head_front": ((0, -6, 1.62), (90, 0, 0), 0.75, (420, 420)),
             "head_34": ((4.2, -4.2, 1.62), (90, 0, 45), 0.75, (420, 420)),
             "body_front": ((0, -6, 1.0), (90, 0, 0), 2.9, (420, 420)),
             "body_side": ((6, 0, 1.0), (90, 0, 90), 2.9, (420, 420))}
    for k, (loc, rot, ortho, res) in shots.items():
        CAM.location = loc
        CAM.rotation_euler = tuple(math.radians(r) for r in rot)
        CAM.data.ortho_scale = ortho
        scene.render.resolution_x, scene.render.resolution_y = res
        scene.render.filepath = os.path.join(OUT, f"{tag}_{show}_{k}.png")
        bpy.ops.render.render(write_still=True)


shoot("before", "Farmer_Hero")
shoot("before", "Farmer_LOD0")

# ------------------------------------------------------------------ face probe: rays from the front at x = 0
hero = meshes["Farmer_Hero"]


def bvh_of(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.transform(ob.matrix_world)
    t = BVHTree.FromBMesh(bm)
    return t, bm


def face_colour(ob, poly_index):
    me = ob.data
    a, c = corners(me)
    p = me.polygons[poly_index]
    return c[p.loop_start:p.loop_start + p.loop_total].mean(0)


def is_skin(c):
    return c[0] > 0.70 and 0.42 < c[1] < 0.82 and c[2] < 0.70 and c[0] > c[1] > c[2]


def probe(ob, xs=(0.0,)):
    t, bm = bvh_of(ob)
    prof = []
    for z10 in range(128, 197):
        z = z10 / 100.0
        for x in xs:
            hit = t.ray_cast(Vector((x, -5, z)), Vector((0, 1, 0)))
            if hit[0] is None:
                continue
            c = face_colour(ob, hit[2])
            prof.append(dict(x=x, z=z, y=round(hit[0].y, 4), skin=bool(is_skin(c)),
                             hex="#%02X%02X%02X" % tuple(int(round(v * 255)) for v in c[:3]), code=int(round(c[3] * 8))))
    bm.free()
    return prof


prof = probe(hero)
skin_z = [p["z"] for p in prof if p["skin"]]
REP["face_probe_centre"] = [(p["z"], p["y"], p["hex"], p["code"]) for p in prof]
log("face probe (z, front y, colour, code):", [(p["z"], p["y"], p["hex"], p["code"]) for p in prof[::3]])
if skin_z:
    # longest run of skin hits on the centre line = the visible face
    runs, cur = [], [skin_z[0]]
    for z in skin_z[1:]:
        if z - cur[-1] <= 0.015:
            cur.append(z)
        else:
            runs.append(cur); cur = [z]
    runs.append(cur)
    band = max(runs, key=len)
    z0, z1 = band[0], band[-1]
else:
    z0, z1 = 1.40, 1.72
    log("WARNING: no skin on the centre line; face band assumed", z0, z1)
REP["face_band_z"] = [z0, z1]
EYE_Z = z0 + 0.58 * (z1 - z0)
MOUTH_Z = z0 + 0.22 * (z1 - z0)
BAND = max(0.12, z1 - z0)
EYE_X = 0.30 * BAND
EYE_H, EYE_W = 0.30 * BAND, 0.20 * BAND
log("face band", z0, z1, "eyes at z", round(EYE_Z, 3), "x +-", round(EYE_X, 3), "size", round(EYE_W, 3), round(EYE_H, 3))
REP["eyes"] = dict(z=round(EYE_Z, 4), x=round(EYE_X, 4), w=round(EYE_W, 4), h=round(EYE_H, 4), mouth_z=round(MOUTH_Z, 4))


# ------------------------------------------------------------------ fixes per mesh
def add_shell(bm, centre, normal, w, h, d, segs, rings):
    """A closed flattened ellipsoid on the surface: thin along the normal, w wide (x), h tall (z)."""
    res = bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=1.0)
    vs = res["verts"]
    q = normal.to_track_quat("-Y", "Z")
    m = Matrix.Translation(centre) @ q.to_matrix().to_4x4() @ Matrix.Diagonal((w * 0.5, d * 0.5, h * 0.5, 1.0))
    bmesh.ops.transform(bm, matrix=m, verts=vs)
    return vs


stats = {}
for name, ob in meshes.items():
    me = ob.data
    nv0, nl0 = len(me.vertices), len(me.loops)
    s = dict(verts_before=nv0)
    # 1 + 2a: colours
    a, c = corners(me)
    codes = np.round(c[:, 3] * 8).astype(int)
    loop_v = np.zeros(len(me.loops), np.int64)
    me.loops.foreach_get("vertex_index", loop_v)
    staff_w = group_weights(ob, "handslot.r")
    staff_v = staff_w > 0.5
    staff_l = staff_v[loop_v]
    s["codes_before"] = {int(k): int(v) for k, v in zip(*np.unique(codes, return_counts=True))}
    if name != "Farmer_Shadow":
        cloth = (codes == 2) & ~staff_l
        k = c[cloth, :3].max(1) / max(AUTHORED_CLOTH)
        c[cloth, :3] = np.clip(np.outer(k, AUTHORED_CLOTH), 0, 1)
        c[cloth, 3] = 1.0
        wood = staff_l & (codes == 0)
        if wood.any():
            v = c[wood, :3].max(1)
            rel = v / max(1e-4, float(np.median(v)))
            c[wood, :3] = np.clip(np.outer(np.clip(rel, 0.6, 1.4), WOOD), 0, 1)
        put(a, c)
        s["cloth_corners_now"] = int(cloth.sum() + ((codes == 8)).sum())
        s["wood_corners"] = int(wood.sum())
    # 2b: staff geometry, recentred on the grip and shortened along its own axis
    if staff_v.any():
        co = np.zeros(nv0 * 3, np.float32)
        me.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        P = co[staff_v]
        mu = P.mean(0)
        _, _, vt = np.linalg.svd(P - mu, full_matrices=False)
        ax = vt[0] / np.linalg.norm(vt[0])
        slot = arm.data.bones["handslot.r"]
        grip_w = arm.matrix_world @ slot.head_local
        grip = np.array(ob.matrix_world.inverted() @ grip_w)
        t = (P - grip) @ ax
        tc = 0.5 * (t.min() + t.max())
        t2 = (t - tc) * STAFF_SCALE
        co[staff_v] = P + np.outer(t2 - t, ax)
        me.vertices.foreach_set("co", co.reshape(-1))
        s["staff"] = dict(verts=int(staff_v.sum()), length_before=round(float(t.max() - t.min()), 3),
                          from_grip_before=[round(float(t.min()), 3), round(float(t.max()), 3)],
                          length_after=round(float(t2.max() - t2.min()), 3),
                          from_grip_after=[round(float(t2.min()), 3), round(float(t2.max()), 3)],
                          axis=[round(float(x), 3) for x in ax])
    me.update()
    # 3: face shells (not on the shadow proxy)
    if ADD_FACE and name != "Farmer_Shadow":
        segs, rings = {"Farmer_Hero": (12, 8), "Farmer_LOD0": (8, 6), "Farmer_LOD1": (6, 4)}.get(name, (6, 4))
        t, bmt = bvh_of(ob)
        bm = bmesh.new()
        bm.from_mesh(me)
        added = []
        parts = [("eye", -EYE_X, EYE_Z, EYE_W, EYE_H, 0.010, EYE), ("eye", EYE_X, EYE_Z, EYE_W, EYE_H, 0.010, EYE),
                 ("mouth", 0.0, MOUTH_Z, 0.20 * BAND, 0.05 * BAND, 0.006, MOUTH)]
        if name != "Farmer_LOD1":
            parts += [("shine", -EYE_X + 0.22 * EYE_W, EYE_Z + 0.22 * EYE_H, 0.32 * EYE_W, 0.32 * EYE_W, 0.010, SHINE),
                      ("shine", EYE_X + 0.22 * EYE_W, EYE_Z + 0.22 * EYE_H, 0.32 * EYE_W, 0.32 * EYE_W, 0.010, SHINE)]
        placed = []
        for kind, x, z, w, h, d, col in parts:
            hit = t.ray_cast(Vector((x, -5, z)), Vector((0, 1, 0)))
            if hit[0] is None:
                log(name, kind, "no surface hit at", x, z); continue
            nrm = hit[1].normalized()
            lift = {"eye": 0.002, "mouth": 0.002, "shine": 0.006}[kind]
            centre = ob.matrix_world.inverted() @ (hit[0] + nrm * lift)
            vs = add_shell(bm, centre, (ob.matrix_world.inverted().to_3x3() @ nrm).normalized(), w, h, d,
                           segs if kind != "shine" else max(6, segs // 2), rings if kind != "shine" else 4)
            added.append((vs, col, kind))
            placed.append(dict(kind=kind, x=x, z=z, front_y=round(hit[0].y, 4)))
        bmt.free()
        new_faces = set()
        for vs, _, _ in added:
            for v in vs:
                for f in v.link_faces:
                    new_faces.add(f)
        for f in new_faces:
            f.smooth = True
            f.material_index = 0
        bm.to_mesh(me)
        bm.free()
        me.update()
        # colours and weights of the new verts and corners (indices past the old counts)
        a, c = corners(me)
        loop_v = np.zeros(len(me.loops), np.int64)
        me.loops.foreach_get("vertex_index", loop_v)
        off = nv0
        vcol = {}
        for vs, col, kind in added:
            for i in range(len(vs)):
                vcol[off + i] = (col, 1 if kind == "shine" else 0)
            off += len(vs)
        for li in range(nl0, len(me.loops)):
            colc = vcol.get(int(loop_v[li]))
            if colc is not None:
                c[li, :3] = colc[0]
                c[li, 3] = colc[1] / 8.0
        put(a, c)
        g = ob.vertex_groups.get("head") or ob.vertex_groups.new(name="head")
        g.add(list(range(nv0, len(me.vertices))), 1.0, "REPLACE")
        for other in ob.vertex_groups:
            if other.name != "head":
                other.remove(list(range(nv0, len(me.vertices))))
        s["face_parts"] = placed
        s["verts_added"] = len(me.vertices) - nv0
        me.calc_loop_triangles()
        s["tris_after"] = len(me.loop_triangles)
    a, c = corners(me)
    codes = np.round(c[:, 3] * 8).astype(int)
    s["codes_after"] = {int(k): int(v) for k, v in zip(*np.unique(codes, return_counts=True))}
    stats[name] = s
    log(name, json.dumps(s))
REP["meshes"] = stats

shoot("after", "Farmer_Hero")
shoot("after", "Farmer_LOD0")

# a posed check: the staff and arms with the hand down (upperarm rotated about the body's forward axis)
try:
    arm.data.pose_position = "POSE"
    for side, sign in (("r", -1), ("l", 1)):
        pb = arm.pose.bones.get("upperarm." + side)
        if pb is not None:
            pb.rotation_mode = "XYZ"
    bpy.context.view_layer.update()
except Exception as x:
    log("pose check skipped", x)
arm.data.pose_position = "REST"

# ------------------------------------------------------------------ export (the class recipe: cm_art/export.py)
for o in scene.objects:
    o.select_set(o is arm or o.name in meshes)
bpy.context.view_layer.objects.active = arm
path = os.path.join(OUT, "Farmer.fbx")
r = bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"}, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", global_scale=1.0, axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, add_leaf_bones=False, primary_bone_axis="Y",
                             secondary_bone_axis="X", use_armature_deform_only=False, armature_nodetype="NULL",
                             use_mesh_modifiers=False, mesh_smooth_type="FACE", colors_type="SRGB",
                             bake_anim=False, use_custom_props=False, path_mode="STRIP")
REP["export"] = dict(path=path, result=list(r), bytes=os.path.getsize(path) if os.path.exists(path) else 0)
log("export", REP["export"])
REP["log"] = LOG
json.dump(REP, open(os.path.join(OUT, "fix_farmer_report.json"), "w"), indent=1)
log("done")
