"""Re-import ../Demolitions.fbx and ../DemolitionsProps.fbx in a fresh headless Blender and check them (same checks as
verify_miner.py, plus the vertex-colour contract compared against the Miner's FBX). Exit code 1 on any failed check.

  cm-heavy blender ... Blender -b --factory-startup --python-exit-code 1 -P verify_demolitions.py
Writes verify_report.json next to this script.
"""
import json
import os
import sys

sys.dont_write_bytecode = True
import bmesh  # noqa: E402
import bpy  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ART = os.path.dirname(os.path.dirname(HERE))
FBX = os.path.join(os.path.dirname(HERE), "Demolitions.fbx")
PROPS_FBX = os.path.join(os.path.dirname(HERE), "DemolitionsProps.fbx")
MINER_FBX = os.path.join(ART, "Miner", "Miner.fbx")
BLEND = os.path.join(HERE, "demolitions.blend")
RIG_JSON = os.path.join(ART, "Characters", "_source~", "rig_kaykit41.json")
TRI_BUDGET = 12000
results, failed = [], []


def check(name, ok, detail):
    results.append({"check": name, "ok": bool(ok), "detail": detail})
    print(("PASS " if ok else "FAIL ") + name + " :: " + str(detail)[:200])
    if not ok:
        failed.append(name)


def fresh():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def geo(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    loose_v = sum(1 for v in bm.verts if not v.link_edges)
    loose_e = sum(1 for e in bm.edges if not e.link_faces)
    nm = sum(1 for e in bm.edges if not e.is_manifold)
    bm.free()
    return loose_v, loose_e, nm


def tris(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def colour_contract(ob):
    ca = ob.data.color_attributes
    out = [(c.name, c.domain, c.data_type) for c in ca]
    codes = set()
    if len(ca):
        c = ca[0]
        codes = {round(d.color[3], 2) for d in c.data}
    return out, sorted(codes)


# ---------------------------------------------------------------- the Miner's contract, for comparison
fresh()
bpy.ops.import_scene.fbx(filepath=MINER_FBX)
miner_contract, miner_codes = colour_contract(bpy.data.objects["Miner_Body"])

# ---------------------------------------------------------------- character FBX
fresh()
data = open(FBX, "rb").read()
check("fbx has no baked animation (clips stay in the KayKit source, as the Miner)",
      data.count(b"AnimationCurveNode") == 0, data.count(b"AnimationCurveNode"))
bpy.ops.import_scene.fbx(filepath=FBX)
arms = [o for o in bpy.data.objects if o.type == "ARMATURE"]
meshes = {o.name: o for o in bpy.data.objects if o.type == "MESH"}
check("objects: Rig + Demolitions_Body + Demolitions_TNTHand", len(arms) == 1 and
      set(meshes) == {"Demolitions_Body", "Demolitions_TNTHand"}, sorted(meshes))
arm = arms[0]
rig = json.load(open(RIG_JSON))
ref = {j["name"]: j for j in rig["joints"]}
names = [b.name for b in arm.data.bones]
check("bone count 41", len(names) == 41, len(names))
check("bone names = KayKit rig", sorted(names) == sorted(ref), "match" if sorted(names) == sorted(ref) else
      sorted(set(names) ^ set(ref)))
check("bone hierarchy = KayKit rig", all((b.parent.name if b.parent else None) == ref[b.name]["parent"]
                                          for b in arm.data.bones if b.name in ref), "parents")
maxd = max(((arm.matrix_world @ b.head_local) - __import__("mathutils").Vector(ref[b.name]["head"])).length
           for b in arm.data.bones if b.name in ref)
check("joint rest positions = KayKit (metres, after the round trip)", maxd < 1e-3, f"max {maxd:.6f} m")
check("armature root transform clean", all(abs(v) < 1e-4 for v in arm.rotation_euler) and
      all(abs(v - 1) < 1e-4 for v in arm.scale), [tuple(arm.rotation_euler), tuple(arm.scale)])
total = 0
for name, ob in sorted(meshes.items()):
    mats = [m.name.split(".")[0] for m in ob.data.materials]
    want = ["CM_Toon", "CM_FaceDecal"] if name == "Demolitions_Body" else ["CM_Toon"]
    check(f"{name} material slots", mats == want, mats)
    lv, le, nm = geo(ob)
    check(f"{name} loose / non-manifold", lv == 0 and le == 0 and nm == 0, {"loose_v": lv, "loose_e": le, "non_manifold": nm})
    mod = [m for m in ob.modifiers if m.type == "ARMATURE"]
    check(f"{name} skinned to the rig", mod and mod[0].object == arm, [m.name for m in ob.modifiers])
    bone_g = {g.index for g in ob.vertex_groups if g.name in ref}
    unw = over4 = 0
    for v in ob.data.vertices:
        gs = [g for g in v.groups if g.group in bone_g and g.weight > 1e-5]
        unw += not gs
        over4 += len(gs) > 4
    check(f"{name} weights (every vertex weighted, at most 4 influences)", unw == 0 and over4 == 0,
          {"unweighted": unw, "over4": over4})
    contract, codes = colour_contract(ob)
    check(f"{name} vertex-colour contract identical to the Miner's", contract == miner_contract and
          set(codes) <= {0.0, 0.75, 1.0}, {"ours": contract, "codes": codes, "miner": miner_contract,
                                            "miner_codes": miner_codes})
    if name == "Demolitions_Body":
        check("team mask codes on the body: cloth 1.0 and glow 0.75 (goggle lenses)", {0.75, 1.0} <= set(codes), codes)
    total += tris(ob)
check(f"triangle budget <= {TRI_BUDGET} (body + TNT in hand)", total <= TRI_BUDGET, total)
zs = [(meshes["Demolitions_Body"].matrix_world @ v.co).z for v in meshes["Demolitions_Body"].data.vertices]
check("height 2.0 u after the round trip (boots to the fuse spark), feet at 0", 1.95 < max(zs) - min(zs) < 2.05 and
      abs(min(zs)) < 0.02, [round(min(zs), 3), round(max(zs), 3)])
fbx_frames = {b.name: b.matrix_local.copy() for b in arm.data.bones}

# ---------------------------------------------------------------- clip binding probe: KayKit clips by bone name
bpy.ops.wm.open_mainfile(filepath=BLEND)
src = bpy.data.objects["Rig"]
bpy.ops.import_scene.fbx(filepath=FBX)
fbx_arm = [o for o in bpy.data.objects if o.type == "ARMATURE" and o is not src][0]
for clip, frame in (("Idle", 1), ("Throw", 16), ("PickUp", 13), ("Hit_A", 5), ("Death_A", 20), ("Interact", 10)):
    act = bpy.data.actions[clip]
    for a in (src, fbx_arm):
        a.animation_data_create()
        a.animation_data.action = act
        if len(act.slots):
            a.animation_data.action_slot = act.slots[0]
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    offs = {bn: round(((fbx_arm.matrix_world @ fbx_arm.pose.bones[bn].head) -
                       (src.matrix_world @ src.pose.bones[bn].head)).length, 4)
            for bn in ("hand.l", "hand.r", "head", "foot.l", "foot.r", "handslot.r")}
    moved = (src.pose.bones["hand.r"].head - src.data.bones["hand.r"].head_local).length
    check(f"KayKit '{clip}' binds to the re-imported FBX rig by bone name (frame {frame})",
          max(offs.values()) < 0.01 and moved > 0.05, {"max_offset_m": max(offs.values()), "hand.r moved": round(moved, 3)})

# ---------------------------------------------------------------- required features (brief DG-07) in the .blend
groups = {o.name: {g.name for g in o.vertex_groups} for o in bpy.data.objects if o.type == "MESH" and o.parent is src}
need = {"round powder keg with staves and hoops": {"feat_keg", "feat_keg_detail"},
        "curled fuse ending in a spark": {"feat_fuse", "feat_spark"},
        "short leather cap": {"feat_cap", "feat_cap_detail"}, "big round goggles pushed up": {"feat_goggles"},
        "painted face decal (eyes, singed brows, gap-tooth grin, lips)": {"feat_face_decal"},
        "soot as vertex-colour value steps": {"feat_soot"},
        "bandolier of 3-stick TNT bundles": {"feat_bandolier", "feat_tnt"},
        "fingerless gloves": {"feat_gloves", "feat_fingers"}, "padded Stone vest with brass buckles":
        {"feat_vest", "feat_vest_detail", "feat_buckles"}, "navy trousers, heavy boots": {"feat_trousers", "feat_boots"},
        "team cloth (scarf, armband, keg band, TNT bands, hair ties)": {"feat_team_cloth"},
        "female: puffed pigtails": {"feat_hair"}}
for label, g in need.items():
    check("feature: " + label, g <= groups.get("Demolitions_Body", set()), sorted(g - groups.get("Demolitions_Body", set())) or "present")
check("feature: oversized TNT bundle in the throwing hand", "feat_tnt_hand" in groups.get("Demolitions_TNTHand", set()),
      sorted(groups.get("Demolitions_TNTHand", set())))
check("no melee weapon object", set(groups) == {"Demolitions_Body", "Demolitions_TNTHand"}, sorted(groups))
mat = bpy.data.materials["CM_Toon"]
glossy = [n.type for n in mat.node_tree.nodes if n.type in ("BSDF_GLOSSY", "BSDF_PRINCIPLED", "BSDF_ANISOTROPIC")]
check("flat toon shading (no glossy nodes in CM_Toon)", not glossy, glossy or "ramped emission only")

# ---------------------------------------------------------------- props FBX
fresh()
bpy.ops.import_scene.fbx(filepath=PROPS_FBX)
pm = {o.name: o for o in bpy.data.objects if o.type == "MESH"}
want = {"Demolitions_Mine_Buried", "Demolitions_Mine_Armed", "Demolitions_Mine_Armed_OwnerRing",
        "Demolitions_Mine_Triggered", "Demolitions_TNT_Flight", "Demolitions_CraterRim", "Demolitions_Explosion"}
check("props FBX: authored static meshes, no armature", set(pm) == want and
      not any(o.type == "ARMATURE" for o in bpy.data.objects), sorted(pm))
caps = {"Demolitions_Mine_Buried": 1200, "Demolitions_Mine_Triggered": 1200, "Demolitions_TNT_Flight": 800,
        "Demolitions_CraterRim": 400, "Demolitions_Explosion": 2500}
for n, cap in caps.items():
    t_ = tris(pm[n])
    lv, le, nm = geo(pm[n])
    check(f"{n}: tris <= {cap}, closed shells", t_ <= cap and lv == 0 and le == 0 and nm == 0,
          {"tris": t_, "non_manifold": nm, "loose": lv + le})
t_ = tris(pm["Demolitions_Mine_Armed"]) + tris(pm["Demolitions_Mine_Armed_OwnerRing"])
check("Demolitions_Mine_Armed + owner ring: tris <= 1200", t_ <= 1200, t_)
for n, o in pm.items():
    zs = [(o.matrix_world @ v.co).z for v in o.data.vertices]
    if n != "Demolitions_TNT_Flight":
        check(f"{n}: sits on the ground (min z about 0)", abs(min(zs)) < 0.05, round(min(zs), 3))
    check(f"{n}: vertex colour 'Col' present", len(o.data.color_attributes) >= 1,
          [c.name for c in o.data.color_attributes])

with open(os.path.join(HERE, "verify_report.json"), "w") as f:
    json.dump({"passed": len(results) - len(failed), "total": len(results), "failed": failed, "results": results}, f,
              indent=1)
print(f"VERIFY {len(results) - len(failed)}/{len(results)}")
if failed:
    sys.exit(1)
