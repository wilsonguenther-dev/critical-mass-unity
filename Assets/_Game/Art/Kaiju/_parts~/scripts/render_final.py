# Verification renders FROM THE EXPORTED out/Kaiju_Adult.fbx (re-imported with its clips):
#  clips: every clip x 5 poses x 5 types (LOD0), joint-gap numbers per pose; LOD sheet; old vs new; size vs a player.
# blender -b --factory-startup --python render_final.py -- <part: clips|sheet>
import bpy, os, sys, json, math
from mathutils import Vector, Matrix, kdtree
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import krender as R
PART = sys.argv[sys.argv.index("--") + 1]
ROOT = os.path.expanduser("~/cm-art-trial/parts-kaiju"); T = ROOT + "/renders/final"; os.makedirs(T, exist_ok=True)
TYPES = ["Elder", "Grove", "Brute", "Timber", "Mire"]
KW = dict(use_custom_normals=True, ignore_leaf_bones=False, automatic_bone_orientation=False, primary_bone_axis="Y", secondary_bone_axis="X")
def nm(t, lod): return "Kaiju" + ("" if t == "Elder" else "_" + t) + "_" + lod
bpy.ops.wm.read_factory_settings(use_empty=True)
sc, cam = R.scene_setup()
def imp(p, anim=True):
    before = set(bpy.data.objects); bpy.ops.import_scene.fbx(filepath=p, use_anim=anim, **KW)
    new = [o for o in bpy.data.objects if o not in before]
    return [o for o in new if o.type == 'ARMATURE'][0], {o.name: o for o in new if o.type == 'MESH'}
rig, M = imp(ROOT + "/out/Kaiju_Adult.fbx")
ACTS = {a.name.split("|")[-1]: a for a in bpy.data.actions if a.name.startswith(rig.name)}
for o in M.values(): R.dress(o, 0.03); o.hide_render = True
R.set_lift(*R.PARTS_LIFT)   # round 2: previews carry the in-game band lift (parts model: half the b14 lift)
def pose(act=None, frame=1):
    ad = rig.animation_data or rig.animation_data_create()
    if act is None:
        ad.action = None
        for pb in rig.pose.bones: pb.matrix_basis = Matrix.Identity(4)
    else:
        ad.action = ACTS[act]
        if ACTS[act].slots: ad.action_slot = ACTS[act].slots[0]
    sc.frame_set(frame); bpy.context.view_layer.update()
def only(objs):
    for o in sc.objects:
        if o.type == 'MESH' and o.name != "Ground": o.hide_render = o not in objs
def frames(a):
    f0, f1 = int(ACTS[a].frame_range[0]), int(ACTS[a].frame_range[1])
    return [round(f0 + (f1 - f0) * k / 4) for k in range(5)]

def posed(o):
    dg = bpy.context.evaluated_depsgraph_get(); ev = o.evaluated_get(dg); m = ev.to_mesh()
    P = [o.matrix_world @ v.co for v in m.vertices]; ev.to_mesh_clear(); return P
def groups_of(o):
    g = [v.groups[0].group if v.groups else -1 for v in o.data.vertices]
    names = {x.index: x.name for x in o.vertex_groups}; out = {}
    for i, gi in enumerate(g): out.setdefault(names.get(gi), []).append(i)
    return out
PAIRS = [(b.parent.name, b.name) for b in rig.data.bones if b.parent]

def gaps(o, G):
    P = posed(o); res = {}
    for a, b in PAIRS:
        if a not in G or b not in G: continue
        kd = kdtree.KDTree(len(G[b]))
        for k, i in enumerate(G[b]): kd.insert(P[i], k)
        kd.balance()
        res[a + ">" + b] = min(kd.find(P[i])[2] for i in G[a][::2])
    return res

if PART == "clips":
    rep = {}
    for t in TYPES:
        o = M[nm(t, "LOD0")]; only([o]); R.swap_colours(o, R.GLOW[t]); R.look(sc, "day")
        G = groups_of(o); pose(None); rest = gaps(o, G); worst = {}
        for a in sorted(ACTS):
            for k, f in enumerate(frames(a)):
                pose(a, f); g = gaps(o, G)
                for pr, d in g.items():
                    dd = d - rest[pr]
                    if dd > worst.get(pr, (0,))[0]: worst[pr] = (round(dd, 3), a, f)
                R.cam_ortho(cam, Vector((0, 0.2, 2.4)), 8.4, 18, -35, 220); R.render(f"{T}/clip_{t}_{a}_{k}.png")
        rep[t] = {"rest_gap": {k: round(v, 3) for k, v in rest.items()},
                  "worst_gap_increase": dict(sorted(worst.items(), key=lambda kv: -kv[1][0])[:8])}
        print("GAPS", t, json.dumps(rep[t]["worst_gap_increase"]))
    json.dump(rep, open(T + "/gaps.json", "w"), indent=1)

if PART in ("sheet", "tail"):
    pose("Idle", 1)
if PART == "sheet":
    # LOD sheet at the gameplay camera (night), per type
    g = R.ground(sc, (0.11, 0.14, 0.19))
    for t in TYPES:
        for lod in ("Hero", "LOD0", "LOD1", "Shadow"):
            o = M[nm(t, lod)]; only([o]); R.swap_colours(o, R.GLOW[t]) if lod != "Shadow" else None
            gc = R.look(sc, "night"); R.ground(sc, gc)
            R.cam_game(cam, Vector((0, 0.4, 1.6)), 420, yaw=45); R.render(f"{T}/lod_{t}_{lod}.png")
    # five types at the gameplay camera, night and day, two yaws; owned azure / crimson; silhouettes
    for t in TYPES:
        o = M[nm(t, "LOD0")]; only([o])
        for mode in ("night", "day"):
            R.swap_colours(o, R.GLOW[t]); gc = R.look(sc, mode); R.ground(sc, gc)
            for yaw in (45, 300):
                R.cam_game(cam, Vector((0, 0.4, 1.6)), 420, yaw=yaw); R.render(f"{T}/game_{t}_{mode}_{yaw}.png")
        for team in ("azure", "crimson"):
            R.swap_colours(o, R.GLOW[team]); gc = R.look(sc, "night"); R.ground(sc, gc)
            R.cam_game(cam, Vector((0, 0.4, 1.6)), 420, yaw=300); R.render(f"{T}/game_{t}_{team}.png")
        R.swap_colours(o, R.GLOW[t])
        g.hide_render = True; R.silhouette_mode(True)
        for yaw in (45, 135, 225, 300):
            R.cam_game(cam, Vector((0, 0.4, 1.6)), 420, yaw=yaw); R.render(f"{T}/sil_{t}_{yaw}.png")
        R.silhouette_mode(False); g.hide_render = False
    # turntable (Elder hero, day, ortho) for every type
    for t in TYPES:
        o = M[nm(t, "Hero")]; only([o]); R.swap_colours(o, R.GLOW[t]); R.look(sc, "day"); g.hide_render = True
        for k, yaw in enumerate((0, 60, 120, 180, 240, 300)):
            R.cam_ortho(cam, Vector((0, 0.3, 2.4)), 7.8, 15, yaw, 300); R.render(f"{T}/turn_{t}_{k}.png")
        g.hide_render = False
if PART in ("sheet", "tail"):
    g = R.ground(sc, (0.11, 0.14, 0.19))
    # OLD model (the file being replaced) at the same camera, night
    orig, OM = imp(ROOT + "/work/Kaiju_Adult_ORIG.fbx", anim=False)
    old = [o for n, o in OM.items() if "LOD0" in n][0]
    for o in OM.values(): o.hide_render = True
    R.dress(old, 0.03); R.swap_colours(old, R.GLOW["neutral_elder"]); only([old]); gc = R.look(sc, "night"); R.ground(sc, gc)
    R.set_lift(*R.OLD_LIFT)      # the old model with ITS in-game lift (b14: 1.22 / 1.38 / 1.55), so the comparison is fair
    R.cam_game(cam, Vector((0, 0.4, 1.6)), 420, yaw=45); R.render(f"{T}/old_night_45.png")
    R.cam_game(cam, Vector((0, 0.4, 1.6)), 420, yaw=300); R.render(f"{T}/old_night_300.png")
    R.set_lift(*R.PARTS_LIFT)
    # size vs a player character (the rebuilt Warrior, team azure), side by side, gameplay camera, night
    wr, WM = imp(os.path.expanduser("~/cm-art-trial/parts-game-ready/out/Warrior/Warrior.fbx"), anim=False)
    war = [o for n, o in WM.items() if "LOD0" in n][0]
    for o in WM.values(): o.hide_render = True
    mw = war.matrix_world.copy()                      # bind-pose mesh: drop the skin (rig imports lying down), keep upright
    for md in list(war.modifiers):
        if md.type == 'ARMATURE': war.modifiers.remove(md)
    war.parent = None; war.matrix_world = Matrix.Translation((3.4, -1.6, 0)) @ Matrix.Rotation(math.radians(180), 4, 'Z') @ mw
    bpy.context.view_layer.update(); print("WARRIOR_DIMS", [round(x, 2) for x in war.dimensions], [round(v[2], 2) for v in war.bound_box][:2])
    R.dress(war, 0.012)
    # one clear panel per type: low 3/4 front ortho (heights read true), the Warrior standing beside the Kaiju, day
    dg = bpy.context.evaluated_depsgraph_get()
    def zrange(o):
        ev = o.evaluated_get(dg); m = ev.to_mesh(); zs = [(o.matrix_world @ v.co).z for v in m.vertices]; ev.to_mesh_clear(); return min(zs), max(zs)
    heights = {"Warrior": round(zrange(war)[1] - max(0.0, zrange(war)[0]), 2)}
    for t in ("Elder", "Brute"):
        k = M[nm(t, "LOD0")]; R.swap_colours(k, R.GLOW["azure"]); only([k, war]); gc = R.look(sc, "day"); R.ground(sc, gc)
        heights[t] = round(zrange(k)[1], 2)
        R.cam_ortho(cam, Vector((1.3, 0.0, 2.6)), 9.0, 8, -22, 520); R.render(f"{T}/size_{t}.png")
    json.dump(heights, open(f"{T}/heights.json", "w")); print("HEIGHTS", heights)
    print("SHEET_DONE")
