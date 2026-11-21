# Silhouettes + team-region masks at the TRUE gameplay camera (55 deg, FOV 35 at 1080p, 35 m), 4 yaws, from built blends
# (work/<Type>/<LOD>/Kaiju.blend) or, with --fbx, from out/Kaiju_Adult.fbx (Idle frame 1).
# blender -b --factory-startup --python measure.py -- <LOD> <outdir> [--fbx] [types]
import bpy, sys, os, math
from mathutils import Vector, Matrix
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import krender as R
argv = sys.argv[sys.argv.index("--") + 1:]
LOD, OUT = argv[0], argv[1]; FBX = "--fbx" in argv
TYPES = [a for a in argv[2:] if not a.startswith("--")] or ["Elder", "Grove", "Brute", "Timber", "Mire"]
ROOT = os.path.expanduser("~/cm-art-trial/parts-kaiju"); os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
sc, cam = R.scene_setup()
def nm(t): return "Kaiju" + ("" if t == "Elder" else "_" + t) + "_" + LOD
obs = {}
if FBX:
    bpy.ops.import_scene.fbx(filepath=ROOT + "/out/Kaiju_Adult.fbx", use_anim=True, automatic_bone_orientation=False, ignore_leaf_bones=False)
    rig = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    a = [x for x in bpy.data.actions if x.name.endswith("Idle")][0]; rig.animation_data.action = a
    if a.slots: rig.animation_data.action_slot = a.slots[0]
    sc.frame_set(1)
    for t in TYPES: obs[t] = bpy.data.objects[nm(t)]
else:
    for t in TYPES:
        with bpy.data.libraries.load(f"{ROOT}/work/{t}/{LOD}/Kaiju.blend") as (src, dst): dst.meshes = list(src.meshes)
        me = [m for m in dst.meshes if m and len(m.polygons)][0]
        ob = bpy.data.objects.new(t, me); sc.collection.objects.link(ob); obs[t] = ob
for o in bpy.data.objects:
    if o.type == 'MESH': o.hide_render = True
for t, ob in obs.items():
    me = ob.data; src = me.color_attributes["Col"]
    mk = me.color_attributes.new("Mask", 'BYTE_COLOR', 'CORNER')
    for a_, b_ in zip(src.data, mk.data):
        code = round(a_.color_srgb[3] * 8); v = 1.0 if code in (6, 8) else 0.0
        b_.color_srgb = (v, v * 0.0, 1 - v, 1)
    me.color_attributes.active_color = mk
sc.render.engine = 'BLENDER_WORKBENCH'; sh = sc.display.shading; sh.light = 'FLAT'; sh.show_object_outline = False
sh.show_shadows = False; sh.show_cavity = False; sc.render.film_transparent = True
sc.display.render_aa = 'OFF'
sc.view_settings.view_transform = 'Standard'
for t, ob in obs.items():
    for x in obs.values(): x.hide_render = x is not ob
    for yaw in (45, 135, 225, 300):
        R.cam_game(cam, Vector((0, 0.4, 1.8)), 460, yaw=yaw)
        sh.color_type = 'VERTEX'; R.render(f"{OUT}/mask_{t}_{yaw}.png")
        sh.color_type = 'SINGLE'; sh.single_color = (0, 0, 0); R.render(f"{OUT}/sil_{t}_{yaw}.png")
print("MEASURE_DONE")
