"""L2: write the solved rig (bone heads), rest meshes and weights into each HY character, export the swap FBX the same way
the build does, render before/after sheets, and round-trip every export.

blender -b --factory-startup --python-exit-code 1 -P apply_export.py -- <plan.json>
"""
import json
import math
import os
import sys
import time
import traceback

import bpy
import numpy as np
from mathutils import Matrix, Vector

T0 = time.time()
PLAN = json.load(open(sys.argv[sys.argv.index("--") + 1]))
H = os.path.expanduser("~")
REPO = H + "/critical-mass-unity-art-hy"
ART = REPO + "/Assets/_Game/Art"
LDSRC = H + "/critical-mass-unity-art-lookdev-a/Assets/_Game/Art/Characters/_source~"
KBLEND = H + "/critical-mass-unity-art-kaiju/Assets/_Game/Art/Kaiju/_source~/Kaiju.blend"
sys.dont_write_bytecode = True
sys.path.insert(0, LDSRC)
sys.path.insert(0, ART + "/Characters/_source~/characters_hy")
from cm_art import export as cm_export  # noqa: E402
import hy_common as hc  # noqa: E402

REN = PLAN["render_dir"]
os.makedirs(REN, exist_ok=True)
OUTREP = {}
NAVY = (0x18 / 255, 0x23 / 255, 0x3B / 255)
CAM_PITCH, CAM_FOV, CAM_DIST, CAM_YAW = 55.0, 35.0, 23.4, 45.0
KAIJU_FBX = dict(object_types={"ARMATURE", "MESH"}, global_scale=1.0, apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=False,
    use_mesh_modifiers=False, mesh_smooth_type="FACE", use_tspace=False, colors_type="SRGB", prioritize_active_color=True,
    add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X", use_armature_deform_only=False, bake_anim=True,
    bake_anim_use_all_bones=True, bake_anim_use_nla_strips=True, bake_anim_use_all_actions=False,
    bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=1.0, path_mode="STRIP",
    embed_textures=False)


def log(*a):
    print("L2", round(time.time() - T0, 1), *a, flush=True)


def world_verts(ob):
    co = np.zeros(len(ob.data.vertices) * 3)
    ob.data.vertices.foreach_get("co", co)
    M = np.array(ob.matrix_world)
    return co.reshape(-1, 3) @ M[:3, :3].T + M[:3, 3]


def set_verts(ob, V):
    ob.data.vertices.foreach_set("co", np.ascontiguousarray(V, np.float32).ravel())
    ob.data.update()


def rest_pose(arms, scene):
    for arm in arms:
        if arm.animation_data:
            arm.animation_data.action = None
        for pb in arm.pose.bones:
            pb.matrix_basis.identity()
    scene.frame_set(0)
    bpy.context.view_layer.update()


def set_action(arms, act, frame, scene):
    for arm in arms:
        for pb in arm.pose.bones:
            pb.matrix_basis.identity()
        arm.animation_data_create()
        arm.animation_data.action = act
        try:
            if arm.animation_data.action_slot is None and len(act.slots):
                arm.animation_data.action_slot = act.slots[0]
        except Exception:
            pass
    scene.frame_set(int(act.frame_range[0]) + int(frame))
    bpy.context.view_layer.update()


def fit_heads(arm, heads_w, names):
    AMi = arm.matrix_world.inverted()
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    n = 0
    for e in arm.data.edit_bones:
        if e.name not in names:
            continue
        h = AMi @ Vector([float(x) for x in heads_w[names.index(e.name)]])
        if (h - e.head).length > 1e-5:
            d = e.tail - e.head
            roll = e.roll
            e.use_connect = False
            e.head = h
            e.tail = h + d
            e.roll = roll
            n += 1
    bpy.ops.object.mode_set(mode="OBJECT")
    return n


def bake_world(ob):
    M = ob.matrix_world.copy()
    for m in list(ob.modifiers):
        ob.modifiers.remove(m)
    ob.parent = None
    ob.data.transform(M)
    ob.matrix_world = Matrix.Identity(4)


def write_groups(ob, W, bones):
    ob.vertex_groups.clear()
    for bi in np.nonzero(W.sum(0) > 0)[0]:
        g = ob.vertex_groups.new(name=bones[bi])
        nz = np.nonzero(W[:, bi] > 0)[0]
        for i in nz:
            g.add([int(i)], float(W[i, bi]), "REPLACE")


def skin_to(ob, arm):
    ob.parent = arm
    ob.matrix_parent_inverse = arm.matrix_world.inverted()
    ob.modifiers.new("Armature", "ARMATURE").object = arm


def posed(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = ev.to_mesh()
    co = np.zeros(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    ev.to_mesh_clear()
    M = np.array(ob.matrix_world)
    return co.reshape(-1, 3) @ M[:3, :3].T + M[:3, 3]


# --------------------------------------------------------------------------------------------- render helpers
class Stage:
    def __init__(self, scene, ground_size=12):
        self.scene = scene
        scene.render.engine = "BLENDER_WORKBENCH"
        sh = scene.display.shading
        sh.light = "STUDIO"
        sh.color_type = "VERTEX"
        sh.show_object_outline = True
        sh.object_outline_color = NAVY
        sh.show_cavity = False
        sh.show_shadows = False
        scene.render.film_transparent = False
        if scene.world is None:
            scene.world = bpy.data.worlds.new("W")
        scene.world.color = (0.33, 0.33, 0.33)
        scene.view_settings.view_transform = "Standard"
        scene.render.image_settings.file_format = "PNG"
        g = ground_size
        gm = bpy.data.meshes.new("Ground")
        gm.from_pydata([(-g, -g, 0), (g, -g, 0), (g, g, 0), (-g, g, 0)], [], [(0, 1, 2, 3)])
        ga = gm.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
        for dd in ga.data:
            dd.color_srgb = (0.47, 0.49, 0.45, 0.0)
        self.ground = bpy.data.objects.new("Ground", gm)
        scene.collection.objects.link(self.ground)
        self.cam_d = bpy.data.cameras.new("Cam")
        self.cam = bpy.data.objects.new("Cam", self.cam_d)
        scene.collection.objects.link(self.cam)
        scene.camera = self.cam

    def show(self, keep):
        keep = set(keep)
        for o in self.scene.objects:
            if o.type == "MESH" and o is not self.ground:
                o.hide_render = o not in keep

    def look(self, target, d):
        self.cam.location = target + d
        self.cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()

    def close(self, obs, path, centre, ortho, az=35, el=12, W=360, Hh=420):
        a, e = math.radians(az), math.radians(el)
        d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
        self.cam_d.type = "ORTHO"
        self.cam_d.sensor_fit = "VERTICAL"
        self.cam_d.ortho_scale = ortho
        self.cam_d.clip_start, self.cam_d.clip_end = 0.1, 300
        self.look(Vector(centre), d * 40)
        sc = self.scene
        sc.render.resolution_x, sc.render.resolution_y = W, Hh
        sc.render.use_border = False
        self.show(obs)
        self.ground.hide_render = True
        sc.render.filepath = path
        bpy.ops.render.render(write_still=True)

    def game(self, obs, path, crop=230, target=(0, 0, 1.0), measure=None):
        from bpy_extras.object_utils import world_to_camera_view
        sc = self.scene
        self.cam_d.type = "PERSP"
        self.cam_d.sensor_fit = "VERTICAL"
        self.cam_d.angle_y = math.radians(CAM_FOV)
        self.cam_d.clip_start, self.cam_d.clip_end = 1.0, 200.0
        a, e = math.radians(CAM_YAW), math.radians(CAM_PITCH)
        d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
        self.look(Vector(target), d * CAM_DIST)
        sc.render.resolution_x, sc.render.resolution_y = 1920, 1080
        bpy.context.view_layer.update()
        P = np.concatenate([posed(o) for o in (measure or obs)])
        P = P[:: max(1, len(P) // 3000)]
        uv = np.array([world_to_camera_view(sc, self.cam, Vector(p))[:2] for p in P])
        cx, cy = (uv.max(0) + uv.min(0)) / 2
        px = float(np.ptp(uv[:, 1]) * 1080)
        sc.render.use_border = True
        sc.render.use_crop_to_border = True
        hw, hh = crop / 2 / 1920, crop / 2 / 1080
        sc.render.border_min_x, sc.render.border_max_x = cx - hw, cx + hw
        sc.render.border_min_y, sc.render.border_max_y = cy - hh, cy + hh
        self.show(obs)
        self.ground.hide_render = False
        sc.render.filepath = path
        bpy.ops.render.render(write_still=True)
        sc.render.use_border = False
        return round(px, 1)


def render_sets(stage, cfg, acts, arms, sets, tag):
    """sets: {'before': {'close': [obs], 'game': [obs]}, 'after': {...}}; cfg['renders'] from the offline metric."""
    rp = cfg["renders"]
    out = os.path.join(REN, tag)
    os.makedirs(out, exist_ok=True)
    centre, ortho = rp["centre"], rp["ortho"]
    for clip, fr in rp["worst"].items():
        for which in ("before", "after"):
            f = fr[which]
            set_action(arms, acts[clip], f, stage.scene)
            stage.close(sets[which]["close"], os.path.join(out, f"worst_{which}_{clip}.png"), centre, ortho)
    if rp.get("game_all"):
        for clip, fr in rp["worst"].items():
            for which in ("before", "after"):
                set_action(arms, acts[clip], fr[which], stage.scene)
                stage.game(sets[which]["game"], os.path.join(out, f"gameworst_{which}_{clip}.png"), crop=rp.get("crop", 230),
                           target=tuple(rp.get("game_target", (0, 0, 1.0))))
    for clip, frames in rp["strips"]:
        for which in ("before", "after"):
            for k, f in enumerate(frames):
                set_action(arms, acts[clip], f, stage.scene)
                stage.close(sets[which]["close"], os.path.join(out, f"strip_{which}_{clip}_{k}.png"), centre, ortho, W=300, Hh=360)
                stage.game(sets[which]["game"], os.path.join(out, f"stripgame_{which}_{clip}_{k}.png"), crop=rp.get("crop", 230),
                           target=tuple(rp.get("game_target", (0, 0, 1.0))))
    rest_pose(arms, stage.scene)


# --------------------------------------------------------------------------------------------- fighters
def fighter(cls, cfg):
    bpy.ops.wm.open_mainfile(filepath=os.path.join(LDSRC, "warrior.blend"))
    scene = bpy.context.scene
    arm = bpy.data.objects["Rig"]
    for o in list(bpy.data.objects):
        if o.type == "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
    rest_pose([arm], scene)
    bones = [b.name for b in arm.data.bones]
    fix = np.load(cfg["fix"])
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=cfg["fbx"], use_anim=False, use_custom_normals=False, colors_type="SRGB",
                             ignore_leaf_bones=False, automatic_bone_orientation=False, primary_bone_axis="Y", secondary_bone_axis="X")
    new = [o for o in bpy.data.objects if o not in before]
    farm = next(o for o in new if o.type == "ARMATURE")
    meshes = {o.name.split(".")[0]: o for o in new if o.type == "MESH"}
    FW = farm.matrix_world
    old_heads = np.array([np.array(FW @ farm.data.bones[n].head_local) for n in bones])
    old_arm = arm.copy()
    old_arm.data = arm.data.copy()
    old_arm.name = "RigOld"
    scene.collection.objects.link(old_arm)
    rest_pose([arm, old_arm], scene)
    fit_heads(old_arm, old_heads, bones)
    moved = fit_heads(arm, fix["heads_w"], bones)
    OLD, NEW = {}, {}
    for tag in ("Hero", "LOD0", "LOD1", "Shadow"):
        ob = meshes[f"{cls}_{tag}"]
        bake_world(ob)
        if ob.data.color_attributes:
            ob.data.color_attributes[0].name = "Col"
            ob.data.color_attributes.active_color_name = "Col"
        o2 = ob.copy()
        o2.data = ob.data.copy()
        scene.collection.objects.link(o2)
        o2.name = o2.data.name = f"OLD_{cls}_{tag}"
        skin_to(o2, old_arm)
        OLD[tag] = o2
        if tag == "Shadow":
            bpy.data.objects.remove(ob, do_unlink=True)
            continue
        R, W = fix[f"R_{tag}"], fix[f"W_{tag}"]
        assert len(R) == len(ob.data.vertices), (tag, len(R), len(ob.data.vertices))
        set_verts(ob, R)
        write_groups(ob, W, bones)
        ob.name = ob.data.name = f"{cls}_{tag}"
        NEW[tag] = ob
    bpy.data.objects.remove(farm, do_unlink=True)
    sh, sinfo = hc.hull_proxy(fix["R_LOD0"], fix["W_LOD0"], bones, f"{cls}_Shadow", scene, budget=780)
    hc.write_vcol(sh, np.full((len(sh.data.polygons), 3), 0.2), lambda c: 0.0)
    NEW["Shadow"] = sh
    mat = bpy.data.materials.get("CM_Toon") or bpy.data.materials.new("CM_Toon")
    exp = []
    for tag in ("Hero", "LOD0", "LOD1", "Shadow"):
        o = NEW[tag]
        o.data.materials.clear()
        o.data.materials.append(mat)
        for p in o.data.polygons:
            p.material_index = 0
        hc.constant_uv(o)
        if o.data.color_attributes:
            o.data.color_attributes.active_color_name = "Col"
        skin_to(o, arm)
        exp.append(o)
    path = cfg["export"]
    os.makedirs(os.path.dirname(path), exist_ok=True)
    cm_export.export_fbx(path, arm, exp)
    rep = dict(fbx=path, bytes=os.path.getsize(path), heads_moved=moved, shadow=sinfo,
               tris={t: sum(len(p.vertices) - 2 for p in NEW[t].data.polygons) for t in NEW})
    log(cls, "exported", json.dumps(rep))
    # renders
    stage = Stage(scene)
    acts = {c: bpy.data.actions[c] for c in bpy.data.actions.keys()}
    arms = [arm, old_arm]
    sets = {"before": {"close": [OLD["Hero"]], "game": [OLD["LOD0"]]}, "after": {"close": [NEW["Hero"]], "game": [NEW["LOD0"]]}}
    render_sets(stage, cfg, acts, arms, sets, cls)
    set_action(arms, acts["Idle"], 0, scene)
    px = {}
    for which in ("before", "after"):
        px[which] = stage.game(sets[which]["game"], os.path.join(REN, cls, f"lineup_{which}.png"), crop=240)
        stage.close(sets[which]["close"], os.path.join(REN, cls, f"idle_{which}.png"), (0, 0, 1.0), 2.4, W=420, Hh=480)
    if cfg.get("fallback"):
        before2 = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=cfg["fallback"], use_anim=False, use_custom_normals=False, colors_type="SRGB",
                                 ignore_leaf_bones=False, automatic_bone_orientation=False, primary_bone_axis="Y", secondary_bone_axis="X")
        new2 = [o for o in bpy.data.objects if o not in before2]
        farm2 = next(o for o in new2 if o.type == "ARMATURE")
        fb_arm = arm.copy()
        fb_arm.data = arm.data.copy()
        scene.collection.objects.link(fb_arm)
        rest_pose([fb_arm], scene)
        FW2 = farm2.matrix_world
        fit_heads(fb_arm, np.array([np.array(FW2 @ farm2.data.bones[n].head_local) for n in bones]), bones)
        fbm = [o for o in new2 if o.type == "MESH" and (o.name.endswith("_LOD0") or o.name.split(".")[0].endswith("_LOD0"))]
        if not fbm:
            fbm = [o for o in new2 if o.type == "MESH"][:1]
        for o in fbm:
            bake_world(o)
            skin_to(o, fb_arm)
        for o in new2:
            if o.type == "ARMATURE":
                bpy.data.objects.remove(o, do_unlink=True)
        set_action([fb_arm, arm, old_arm], acts["Idle"], 0, scene)
        px["fallback"] = stage.game(fbm, os.path.join(REN, cls, "lineup_fallback.png"), crop=240)
    rep["game_px"] = px
    # mean colour of the match mesh (area weighted, sRGB) for the readability check
    me = NEW["LOD0"].data
    a = me.color_attributes["Col"]
    c = np.zeros(len(a.data) * 4, np.float32)
    a.data.foreach_get("color_srgb", c)
    c = c.reshape(-1, 4)
    ar = np.array([p.area for p in me.polygons])
    fc = np.array([c[p.loop_start, :3] for p in me.polygons])
    rep["lod0_mean_srgb"] = [round(float(x), 3) for x in (fc * ar[:, None]).sum(0) / ar.sum()]
    rep["lod0_luma"] = round(float(((fc @ [0.2126, 0.7152, 0.0722]) * ar).sum() / ar.sum()), 3)
    OUTREP[cls] = rep


# --------------------------------------------------------------------------------------------- Kaiju
def kaiju(cfg):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.fps = 30
    fix = np.load(cfg["fix"])
    global RECT
    RECT, rinfo = decal_rect()
    log("decal rect", rinfo)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=cfg["fbx"], use_anim=False, use_custom_normals=False, colors_type="SRGB",
                             ignore_leaf_bones=False, automatic_bone_orientation=False, primary_bone_axis="Y", secondary_bone_axis="X")
    new = [o for o in bpy.data.objects if o not in before]
    farm = next(o for o in new if o.type == "ARMATURE")
    meshes = {o.name.split(".")[0]: o for o in new if o.type == "MESH"}
    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)
    with bpy.data.libraries.load(KBLEND, link=False) as (_src, _dst):
        _dst.objects = ["Adult_Rig"]
    arm = _dst.objects[0]
    scene.collection.objects.link(arm)
    strips = [s for t in arm.animation_data.nla_tracks for s in t.strips]
    ACTS = []
    for s in strips:
        a = s.action
        ACTS.append((s.name.split("|")[-1], a))
    for t in list(arm.animation_data.nla_tracks):
        arm.animation_data.nla_tracks.remove(t)
    arm.animation_data.action = None
    for clip, a in ACTS:
        a.name = "KaijuRig|" + clip
        a.use_fake_user = True
    arm.name = "KaijuRig"
    bones = [b.name for b in arm.data.bones]
    FW = farm.matrix_world
    old_heads = np.array([np.array(FW @ farm.data.bones[n].head_local) for n in bones])
    old_arm = arm.copy()
    old_arm.data = arm.data.copy()
    old_arm.name = "KaijuRigOld"
    old_arm.animation_data_clear()
    scene.collection.objects.link(old_arm)
    rest_pose([arm, old_arm], scene)
    fit_heads(old_arm, old_heads, bones)
    moved = fit_heads(arm, fix["heads_w"], bones)
    OLD, NEW = {}, {}
    for tag in ("Hero", "LOD0", "LOD1", "Shadow"):
        ob = meshes[f"Kaiju_{tag}"]
        bake_world(ob)
        if ob.data.color_attributes:
            ob.data.color_attributes[0].name = "Col"
            ob.data.color_attributes.active_color_name = "Col"
        o2 = ob.copy()
        o2.data = ob.data.copy()
        scene.collection.objects.link(o2)
        o2.name = o2.data.name = f"OLD_Kaiju_{tag}"
        skin_to(o2, old_arm)
        OLD[tag] = o2
        if tag == "Shadow":
            bpy.data.objects.remove(ob, do_unlink=True)
            continue
        R, W = fix[f"R_{tag}"], fix[f"W_{tag}"]
        assert len(R) == len(ob.data.vertices)
        set_verts(ob, R)
        write_groups(ob, W, bones)
        ob.name = ob.data.name = f"HYK_{tag}"
        NEW[tag] = ob
    farm_name = farm.name
    bpy.data.objects.remove(farm, do_unlink=True)
    sh, sinfo = hc.hull_proxy(fix["R_LOD0"], fix["W_LOD0"], bones, "HYK_Shadow", scene, budget=1450)
    hc.write_vcol(sh, np.full((len(sh.data.polygons), 3), 0.2), lambda c: 0.0)
    NEW["Shadow"] = sh
    mat = bpy.data.materials.new("CM_Toon")
    face = {}
    for tag in ("Hero", "LOD0", "LOD1", "Shadow"):
        o = NEW[tag]
        o.data.materials.clear()
        o.data.materials.append(mat)
        hc.constant_uv(o)
        if tag != "Shadow" and f"FUV_{tag}" in fix.files:
            face[tag] = face_uv(o, fix[f"FUV_{tag}"], RECT)
        o.data.color_attributes.active_color_name = "Col"
        skin_to(o, arm)
    for o in list(OLD.values()):
        pass
    exp = []
    for tag in ("Hero", "LOD0", "LOD1", "Shadow"):
        o = NEW[tag]
        o.name = o.data.name = f"Kaiju_{tag}"
        exp.append(o)
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = None
    for tr in list(ad.nla_tracks):
        ad.nla_tracks.remove(tr)
    for clip, a in ACTS:
        tr = ad.nla_tracks.new()
        tr.name = clip
        st = tr.strips.new(clip, int(a.frame_range[0]), a)
        st.name = clip
        try:
            if st.action_slot is None and len(a.slots):
                st.action_slot = a.slots[0]
        except Exception:
            pass
    for o in bpy.context.view_layer.objects:
        o.select_set(o == arm or o in exp)
    bpy.context.view_layer.objects.active = arm
    path = cfg["export"]
    os.makedirs(os.path.dirname(path), exist_ok=True)
    r = bpy.ops.export_scene.fbx(filepath=path, use_selection=True, **KAIJU_FBX)
    assert "FINISHED" in r
    for tr in list(ad.nla_tracks):
        ad.nla_tracks.remove(tr)
    rep = dict(fbx=path, bytes=os.path.getsize(path), heads_moved=moved, shadow=sinfo, face_uv=face, decal=rinfo,
               tris={t: sum(len(p.vertices) - 2 for p in NEW[t].data.polygons) for t in NEW})
    log("Kaiju exported", json.dumps(rep))
    for o in OLD.values():
        o.data.color_attributes.active_color_name = "Col"
    stage = Stage(scene, 24)
    acts = {c: a for c, a in ACTS}
    arms = [arm, old_arm]
    sets = {"before": {"close": [OLD["Hero"]], "game": [OLD["LOD0"]]}, "after": {"close": [NEW["Hero"]], "game": [NEW["LOD0"]]}}
    render_sets(stage, cfg, acts, arms, sets, "Kaiju")
    set_action(arms, acts["Idle"], 0, scene)
    px = {}
    for which in ("before", "after"):
        px[which] = stage.game(sets[which]["game"], os.path.join(REN, "Kaiju", f"lineup_{which}.png"), crop=480)
    # face decal preview: neutral + angry atlas on the Hero (texture shading through UV0)
    if face:
        for nm in ("Kaiju_Face", "Kaiju_Face_Angry", "Kaiju_Face_Berserk", "Kaiju_Face_Blink"):
            img = bpy.data.images.load(ART + f"/Kaiju/{nm}.png", check_existing=True)
            m2 = bpy.data.materials.new("prev_" + nm)
            m2.use_nodes = True
            nt = m2.node_tree
            bsdf = nt.nodes["Principled BSDF"]
            tn = nt.nodes.new("ShaderNodeTexImage")
            tn.image = img
            vc = nt.nodes.new("ShaderNodeVertexColor")
            vc.layer_name = "Col"
            mix = nt.nodes.new("ShaderNodeMix")
            mix.data_type = "RGBA"
            em = nt.nodes.new("ShaderNodeEmission")
            out = next(x for x in nt.nodes if x.type == "OUTPUT_MATERIAL")
            nt.links.new(tn.outputs["Alpha"], mix.inputs["Factor"])
            nt.links.new(vc.outputs["Color"], mix.inputs[6])
            nt.links.new(tn.outputs["Color"], mix.inputs[7])
            nt.links.new(mix.outputs[2], em.inputs["Color"])
            nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
            NEW["Hero"].data.materials.clear()
            NEW["Hero"].data.materials.append(m2)
            for clip in ("Idle", "Roar"):
                set_action(arms, acts[clip], cfg["renders"].get("roar_frame", 20) if clip == "Roar" else 0, scene)
                stage.scene.render.engine = "CYCLES"
                stage.scene.cycles.samples = 4
                stage.scene.cycles.use_denoising = False
                stage.scene.cycles.device = "CPU"
                hb = arm.pose.bones[cfg.get("head_bone", "Head")]
                hp = arm.matrix_world @ hb.head
                stage.close([NEW["Hero"]], os.path.join(REN, "Kaiju", f"face_{nm}_{clip}.png"), tuple(hp), 3.0, az=0, el=8, W=420, Hh=420)
                stage.scene.render.engine = "BLENDER_WORKBENCH"
        NEW["Hero"].data.materials.clear()
        NEW["Hero"].data.materials.append(mat)
    rep["game_px"] = px
    OUTREP["Kaiju"] = rep


def decal_rect():
    """UV rectangle of the expression atlas region the shipped Kaiju adult maps (which half), tightened to the decal's
    opaque area in that half."""
    before = set(bpy.data.objects)
    acts0 = set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=ART + "/Kaiju/_fallback~/Kaiju_Adult.fbx", use_anim=False)
    new = [o for o in bpy.data.objects if o not in before]
    hero = next(o for o in new if o.type == "MESH" and o.name.startswith("Kaiju_Hero"))
    me = hero.data
    uv = np.zeros(len(me.loops) * 2)
    me.uv_layers[0].data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    img = bpy.data.images.load(ART + "/Kaiju/Kaiju_Face.png", check_existing=True)
    w, h = img.size
    px = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(px)
    px = px.reshape(h, w, 4)
    a = px[np.clip((uv[:, 1] * h).astype(int), 0, h - 1), np.clip((uv[:, 0] * w).astype(int), 0, w - 1), 3]
    fu = uv[a > 0.5]
    top = float(np.mean(fu[:, 1])) > 0.5 if len(fu) else True
    rows = np.arange(h // 2, h) if top else np.arange(0, h // 2)
    m = px[rows, :, 3] > 0.1
    rr = rows[np.nonzero(m.any(1))[0]]
    cc = np.nonzero(m.any(0))[0]
    rect = (float(cc.min()) / w, float(rr.min()) / h, float(cc.max() + 1) / w, float(rr.max() + 1) / h)
    info = dict(old_face_corners=int(len(fu)), old_face_uv_bbox=[round(float(x), 3) for x in (list(fu.min(0)) + list(fu.max(0)))] if len(fu) else None,
                half="top" if top else "bottom", rect=[round(x, 3) for x in rect])
    for o in new:
        bpy.data.objects.remove(o, do_unlink=True)
    for ac in list(bpy.data.actions):
        if ac not in acts0:
            bpy.data.actions.remove(ac)
    return rect, info


def face_uv(ob, fuv, rect):
    """fuv: per-vertex (u, v, inside) from the offline fit. A triangle whose three corners are inside gets the decal UVs,
    every other corner stays on the transparent texel, so no triangle smears the decal."""
    me = ob.data
    lay = me.uv_layers[0]
    uv = np.full((len(me.loops), 2), 0.01, np.float32)
    n = 0
    for p in me.polygons:
        vs = list(p.vertices)
        if all(fuv[v, 2] > 0.5 for v in vs):
            for li, v in zip(p.loop_indices, vs):
                uv[li] = (rect[0] + fuv[v, 0] * (rect[2] - rect[0]), rect[1] + fuv[v, 1] * (rect[3] - rect[1]))
            n += 1
    lay.data.foreach_set("uv", uv.ravel())
    return n


def round_trip(path, kaiju_mode):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path, use_anim=kaiju_mode)
    rt = {"objects": {}, "bones": None, "actions": sorted(a.name for a in bpy.data.actions)}
    for o in bpy.data.objects:
        if o.type == "MESH":
            me = o.data
            uvs = None
            if me.uv_layers:
                u = np.zeros(len(me.loops) * 2)
                me.uv_layers[0].data.foreach_get("uv", u)
                u = u.reshape(-1, 2)
                uvs = int((np.abs(u - 0.01).max(1) > 1e-3).sum())
            W = np.zeros(len(me.vertices))
            mx = 0
            for v in me.vertices:
                k = sum(1 for g in v.groups if g.weight > 0)
                mx = max(mx, k)
            rt["objects"][o.name] = {"tris": sum(len(p.vertices) - 2 for p in me.polygons), "groups": len(o.vertex_groups),
                                     "colors": [a.name for a in me.color_attributes], "max_influences": mx,
                                     "uv_off_texel_corners": uvs, "dims": [round(x, 3) for x in o.dimensions]}
        if o.type == "ARMATURE":
            rt["bones"] = len(o.data.bones)
    return rt


for cls, cfg in PLAN["chars"].items():
    try:
        if cls == "Kaiju":
            kaiju(cfg)
        else:
            fighter(cls, cfg)
    except Exception:
        traceback.print_exc()
        OUTREP[cls] = {"error": traceback.format_exc()[-2000:]}
    json.dump(OUTREP, open(os.path.join(REN, "l2_report.json"), "w"), indent=1, default=str)
for cls, cfg in PLAN["chars"].items():
    try:
        if "error" not in OUTREP.get(cls, {"error": 1}):
            OUTREP[cls]["round_trip"] = round_trip(cfg["export"], cls == "Kaiju")
    except Exception:
        OUTREP[cls]["round_trip_error"] = traceback.format_exc()[-1500:]
json.dump(OUTREP, open(os.path.join(REN, "l2_report.json"), "w"), indent=1, default=str)
log("ALL_DONE")
