# rig-hmw (2026-11-14): one GitHub Blender launch per class for the Healer, Miner and Warrior fixes.
#   loop/gates/remote blender --ref <branch> --script <this> --args '{out} {repo} {inputs} <Cls> <mode>'
#   mode bake: bake_clips.py on the repo's class FBX, then check_sheet.py (clips played the way Unity plays them).
#   mode rig:  rigb_run.py rig,bake (whole-hand weights) on the repo holding model, then check_sheet.py.
import sys, os, json, runpy, traceback

argv = sys.argv[sys.argv.index('--') + 1:]
OUT, REPO, INP, CLS, MODE = argv[:5]
HERE = os.path.join(REPO, 'Assets/_Game/Art/Characters/_source~/characters_hy/rig')
os.makedirs(OUT, exist_ok=True)
RES = {'mode': MODE}
FBX = {'Miner': 'Assets/_Game/Art/Miner/Miner.fbx', 'Warrior': 'Assets/_Game/Art/Characters/Warrior.fbx',
       'Healer': 'Assets/_Game/Art/Healer/Healer.fbx'}[CLS]


def run(script, args):
    sys.argv = ['blender', '--'] + args
    try:
        runpy.run_path(os.path.join(HERE, script), run_name='__main__')
        RES[script] = 'ok'
    except SystemExit as e:
        RES[script] = f'exit {e.code}'
    except Exception:
        RES[script] = traceback.format_exc()[-1500:]
        print(RES[script], flush=True)
    json.dump(RES, open(os.path.join(OUT, 'hmw_run.json'), 'w'), indent=1)


def reset():
    import bpy
    bpy.ops.wm.read_factory_settings(use_empty=True)


if MODE == 'rig':
    run('rigb_run.py', [OUT, REPO, INP, CLS, 'rig,bake', '0'])
    fbx = os.path.join(OUT, CLS + '.fbx')
else:
    fbx = os.path.join(REPO, FBX)
    run('bake_clips.py', [OUT, REPO, CLS, fbx])
reset()
run('check_sheet.py', [OUT, CLS, fbx, os.path.join(OUT, CLS + '_Clips.fbx')])
print('HMW', json.dumps(RES)[:2000], flush=True)
