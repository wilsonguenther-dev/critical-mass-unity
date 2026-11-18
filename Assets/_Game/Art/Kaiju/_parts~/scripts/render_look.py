# Quick look renders of the built (rest-pose) meshes: per type turntable (4 ortho views), gameplay camera night/day,
# black silhouette at gameplay size. blender -b --factory-startup --python render_look.py -- <LOD> <outdir> [types]
import bpy, sys, os, math
from mathutils import Vector, Matrix
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import krender as R
argv = sys.argv[sys.argv.index("--") + 1:]
LOD, OUT = argv[0], argv[1]; TYPES = argv[2:] or ["Elder", "Grove", "Brute", "Timber", "Mire"]
ROOT = os.path.expanduser("~/cm-art-trial/parts-kaiju"); os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
sc, cam = R.scene_setup()
obs = {}
for t in TYPES:
    with bpy.data.libraries.load(f"{ROOT}/work/{t}/{LOD}/Kaiju.blend") as (src, dst):
        dst.meshes = [m for m in src.meshes]
    me = [m for m in dst.meshes if m and len(m.polygons)][0]
    ob = bpy.data.objects.new(t, me); sc.collection.objects.link(ob); R.dress(ob); obs[t] = ob
def only(o):
    for x in obs.values(): x.hide_render = x is not o
C = Vector((0, 0.3, 2.4))
for t, ob in obs.items():
    only(ob)
    R.swap_colours(ob, R.GLOW[t]); gcol = R.look(sc, "day"); g = R.ground(sc, gcol); g.hide_render = True
    for nm, pitch, yaw in (("front", 8, 0), ("q34", 22, -38), ("side", 6, -90), ("back", 25, 160), ("top", 89, 0)):
        R.cam_ortho(cam, C, 7.6, pitch, yaw, 420); R.render(f"{OUT}/{t}_{nm}.png")
    g.hide_render = False
    for mode in ("night", "day"):
        gcol = R.look(sc, mode); R.ground(sc, gcol)
        for yaw in (45, 225):
            R.cam_game(cam, Vector((0, 0.4, 1.6)), 420, yaw=yaw); R.render(f"{OUT}/{t}_game_{mode}_{yaw}.png")
    for team in ("azure", "crimson"):
        R.swap_colours(ob, R.GLOW[team]); R.look(sc, "night"); R.ground(sc, (0.11, 0.14, 0.19))
        R.cam_game(cam, Vector((0, 0.4, 1.6)), 420, yaw=225); R.render(f"{OUT}/{t}_game_{team}.png")
    R.swap_colours(ob, R.GLOW[t])
    g.hide_render = True
    R.silhouette_mode(True)
    for yaw in (45, 135, 225, 315):
        R.cam_game(cam, Vector((0, 0.4, 1.6)), 420, yaw=yaw); R.render(f"{OUT}/{t}_sil_{yaw}.png")
    R.silhouette_mode(False)
print("LOOK_DONE", OUT)
