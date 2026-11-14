# rig-demolitions-1 (2026-11-14): rigb_run.py (clean, rig, bake) and then check_sheet.py in one GitHub Blender launch.
#   loop/gates/remote blender ... --script <this> --args '{out} {repo} {inputs} <Cls> [steps] [fingers]'
import sys, os, runpy, traceback

argv = sys.argv[sys.argv.index('--') + 1:]
OUT, REPO, INP, CLS = argv[:4]
HERE = os.path.join(REPO, 'Assets/_Game/Art/Characters/_source~/characters_hy/rig')
sys.argv = ['blender', '--'] + argv
runpy.run_path(os.path.join(HERE, 'rigb_run.py'), run_name='__main__')
sys.argv = ['blender', '--', OUT, CLS]
try:
    runpy.run_path(os.path.join(HERE, 'check_sheet.py'), run_name='__main__')
except SystemExit:
    pass
except Exception:
    print(traceback.format_exc()[-2000:], flush=True)
