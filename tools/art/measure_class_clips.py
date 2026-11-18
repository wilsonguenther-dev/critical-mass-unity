"""Measures each class's OWN baked locomotion clips (b15 world look, motion item 4a).

clip_facts.json was measured on the KayKit Knight; every class plays its own clips baked with foot IK (<Class>_Clips.fbx),
whose stride and foot lift differ from the Knight's times the hip ratio. This script imports one clips FBX in headless
Blender 5.x, evaluates each locomotion action by forward kinematics (the pose bones of the evaluated armature) and prints
one JSON line per action:
  clip, frames, fps, length (s), groundSpeed (planted-toe speed in armature space, u/s), stride (u per cycle),
  liftL/liftR (toe height range, u), stanceShare (frames with a toe within 0.03 u of its lowest), leftPlant (phase).
Sampled at 120 Hz (sub-frames). Usage (stdin from /dev/null, hard timeout):
  blender -b --factory-startup -P tools/art/measure_class_clips.py -- <Class>_Clips.fbx <out.json>
"""
import json
import math
import sys

import bpy

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
src, out = argv[0], argv[1]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src, automatic_bone_orientation=False)
arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
scene = bpy.context.scene
fps = scene.render.fps / scene.render.fps_base

WANT = ('run', 'walk', 'strafe', 'back')


def bone(names):
    for n in names:
        if n in arm.pose.bones:
            return arm.pose.bones[n]
    return None


toeL = bone(['toes.l', 'toe.l', 'foot.l'])
toeR = bone(['toes.r', 'toe.r', 'foot.r'])
hips = bone(['hips'])
rows = []
if arm.animation_data is None:
    arm.animation_data_create()
for act in bpy.data.actions:
    name = act.name.split('|')[-1]
    if not any(w in name.lower() for w in WANT):
        continue
    arm.animation_data.action = act
    try:
        if hasattr(arm.animation_data, 'action_slot') and len(act.slots) > 0:
            arm.animation_data.action_slot = act.slots[0]
    except Exception:
        pass
    f0, f1 = int(math.floor(act.frame_range[0])), int(math.ceil(act.frame_range[1]))
    P = []
    SUB = 5   # 5 sub-frames per 24 fps frame = 120 Hz, the rate measure_clips.py uses on the Knight
    for k in range((f1 - f0) * SUB + 1):
        scene.frame_set(f0 + k // SUB, subframe=(k % SUB) / SUB)
        dg = bpy.context.evaluated_depsgraph_get()
        ea = arm.evaluated_get(dg)
        mw = ea.matrix_world
        pl = mw @ ea.pose.bones[toeL.name].head
        pr = mw @ ea.pose.bones[toeR.name].head
        ph = mw @ ea.pose.bones[hips.name].head
        P.append((tuple(pl), tuple(pr), tuple(ph)))
    n = len(P) - 1
    if n < 4:
        continue
    length = n / (fps * SUB)
    res = {'clip': name, 'samples': n, 'hz': fps * SUB, 'length': round(length, 4)}
    spd, share = [], []
    for k, side in ((0, 'L'), (1, 'R')):
        zs = [p[k][2] for p in P]   # Blender Z up
        lo = min(zs)
        res['lift' + side] = round(max(zs) - lo, 4)
        st = [i for i in range(n) if zs[i] - lo < 0.03]
        share.append(len(st) / n)
        if st:
            vs = [math.hypot(P[i + 1][k][0] - P[i][k][0], P[i + 1][k][1] - P[i][k][1]) * fps * SUB for i in st]
            spd.append(sum(vs) / len(vs))
        if k == 0 and st:
            # mid-stance phase (circular mean of stance frames)
            a = [2 * math.pi * i / n for i in st]
            m = math.atan2(sum(math.sin(x) for x in a), sum(math.cos(x) for x in a))
            res['leftPlant'] = round((m / (2 * math.pi)) % 1.0, 4)
    hz = [p[2][2] for p in P]
    res['hipBob'] = round(max(hz) - min(hz), 4)
    res['hipZ'] = round(sum(hz) / len(hz), 4)
    res['groundSpeed'] = round(sum(spd) / len(spd), 4) if spd else 0.0
    res['stride'] = round(res['groundSpeed'] * length, 4)
    res['stanceShare'] = round(sum(share) / len(share), 3)
    rows.append(res)
    print('CLIP ' + json.dumps(res))
json.dump({'source': src, 'rows': rows}, open(out, 'w'), indent=1)
print('WROTE', out, len(rows))
