"""Farmer/Miner: the shared KayKit clips animate the hips bone's translation in absolute values (KayKit hip height),
so a class whose hips joint sits lower than KayKit's is lifted by the difference: feet float and the planted foot
slides. Fix: the hips bone rest head goes to the KayKit hips head; every other joint, the meshes and the weights stay.
Prints the evidence (hips/foot heights, lowest foot over Idle/Running_A, old vs new) and exports to <out>/<cls>.fbx."""
import sys, os, json
sys.argv = [sys.argv[0], "--", "/Users/wilsonguenther/cm-rig2/plan.json"] + sys.argv[sys.argv.index("--") + 1:]
RIG = "/Users/wilsonguenther/critical-mass-unity-art-hy/Assets/_Game/Art/Characters/_source~/characters_hy/rig"
sys.path.insert(0, RIG)
import apply_export as ax
import bpy, numpy as np
from mathutils import Vector
ART = ax.ART
SRC = {"Farmer": ART + "/Farmer/Farmer.fbx", "Miner": ART + "/Miner/_hy~/Miner.fbx", "Warrior": ART + "/Characters/Warrior.fbx"}
OUT = "/Users/wilsonguenther/cm-rig2/export"
rep = {}
def foot_low(arm, scene, act):
    lo = 9.0
    a = arm.animation_data or arm.animation_data_create()
    a.action = act
    f0, f1 = [int(x) for x in act.frame_range]
    for f in range(f0, f1 + 1):
        scene.frame_set(f)
        for b in ("foot.l", "foot.r", "toes.l", "toes.r"):
            lo = min(lo, (arm.matrix_world @ arm.pose.bones[b].head).z)
    return round(lo, 4)
for cls in sys.argv[3:]:
    bpy.ops.wm.open_mainfile(filepath=os.path.join(ax.LDSRC, "warrior.blend"))
    scene = bpy.context.scene
    arm = bpy.data.objects["Rig"]
    for o in list(bpy.data.objects):
        if o.type == "MESH": bpy.data.objects.remove(o, do_unlink=True)
    ax.rest_pose([arm], scene)
    bones = [b.name for b in arm.data.bones]
    kk = {n: np.array(arm.matrix_world @ arm.data.bones[n].head_local) for n in ("hips", "upperleg.l", "foot.l", "spine")}
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=SRC[cls], use_anim=False, use_custom_normals=False, colors_type="SRGB",
                             ignore_leaf_bones=False, automatic_bone_orientation=False, primary_bone_axis="Y", secondary_bone_axis="X")
    new = [o for o in bpy.data.objects if o not in before]
    farm = next(o for o in new if o.type == "ARMATURE")
    meshes = {o.name.split(".")[0]: o for o in new if o.type == "MESH"}
    FW = farm.matrix_world
    heads = np.array([np.array(FW @ farm.data.bones[n].head_local) for n in bones])
    H = {n: heads[bones.index(n)] for n in ("hips", "upperleg.l", "foot.l", "spine")}
    old = arm.copy(); old.data = arm.data.copy(); old.name = "RigOld"; scene.collection.objects.link(old)
    ax.rest_pose([arm, old], scene)
    ax.fit_heads(old, heads, bones)
    h2 = heads.copy(); h2[bones.index("hips")] = kk["hips"]
    ax.fit_heads(arm, h2, bones)
    acts = {c: bpy.data.actions[c] for c in ("Idle", "Running_A", "Walking_A") if c in bpy.data.actions}
    r = dict(kaykit={k: v.round(4).tolist() for k, v in kk.items()}, fbx={k: v.round(4).tolist() for k, v in H.items()},
             hips_dz=round(float(kk["hips"][2] - H["hips"][2]), 4))
    for c, act in acts.items():
        r[f"foot_low_{c}"] = {"old": foot_low(old, scene, act), "new": foot_low(arm, scene, act)}
    ax.rest_pose([arm, old], scene)
    exp = []
    mat = bpy.data.materials.get("CM_Toon") or bpy.data.materials.new("CM_Toon")
    for tag in ("Hero", "LOD0", "LOD1", "Shadow"):
        ob = meshes[f"{cls}_{tag}"]
        ax.bake_world(ob)
        if ob.data.color_attributes:
            ob.data.color_attributes[0].name = "Col"; ob.data.color_attributes.active_color_name = "Col"
        ob.name = ob.data.name = f"{cls}_{tag}"
        ob.data.materials.clear(); ob.data.materials.append(mat)
        for p in ob.data.polygons: p.material_index = 0
        ax.skin_to(ob, arm)
        exp.append(ob)
    bpy.data.objects.remove(farm, do_unlink=True)
    # in-place check of the mesh: lowest Hero vertex over Idle, old rig vs new
    hero = meshes[f"{cls}_Hero"]
    o2 = hero.copy(); o2.data = hero.data.copy(); scene.collection.objects.link(o2)
    o2.modifiers["Armature"].object = old
    lows = {}
    for nm, ob_, rg in (("new", hero, arm), ("old", o2, old)):
        a = rg.animation_data or rg.animation_data_create(); a.action = acts["Idle"]; scene.frame_set(1)
        dg = bpy.context.evaluated_depsgraph_get(); me = ob_.evaluated_get(dg).to_mesh()
        lows[nm] = round(min((ob_.matrix_world @ v.co).z for v in me.vertices), 4); ob_.evaluated_get(dg).to_mesh_clear()
    r["hero_lowest_vertex_idle"] = lows
    bpy.data.objects.remove(o2, do_unlink=True)
    ax.rest_pose([arm], scene)
    os.makedirs(OUT, exist_ok=True)
    ax.cm_export.export_fbx(os.path.join(OUT, f"{cls}.fbx"), arm, exp)
    r["round_trip"] = ax.round_trip(os.path.join(OUT, f"{cls}.fbx"), False)
    rep[cls] = r
    print("HIPSFIX", cls, json.dumps({k: v for k, v in r.items() if k != "round_trip"}), flush=True)
json.dump(rep, open("/Users/wilsonguenther/cm-rig2/hips_report.json", "w"), indent=1, default=str)
print("HIPSFIX_DONE")
