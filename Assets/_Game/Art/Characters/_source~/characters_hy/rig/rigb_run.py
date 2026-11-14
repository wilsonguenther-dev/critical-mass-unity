# rig-batch-b (2026-11-14): one GitHub Blender launch per character: clean (only when no holding model exists), rig,
# bake the clip pack, measure and render.
#   loop/gates/remote blender --ref <branch> --script <this> --args '{out} {repo} {inputs} <Cls> [steps]' --inputs <raw.glb>
#   steps: comma list of clean,rig,bake (default all). Holding model order: {out}/holding (this run's clean), the repo's
#   Assets/_Game/Art/_gen2~/holding/<Cls>/<Cls>.fbx (gen-models-2), else clean from {inputs} with rigb_manifest.json.
import sys, os, json, runpy, traceback

argv = sys.argv[sys.argv.index('--') + 1:]
OUT, REPO, INP, CLS = argv[:4]
STEPS = (argv[4] if len(argv) > 4 else 'clean,rig,bake').split(',')
FING = argv[5] if len(argv) > 5 else '1'
HERE = os.path.join(REPO, 'Assets/_Game/Art/Characters/_source~/characters_hy/rig')
os.makedirs(OUT, exist_ok=True)
RES = {}


def run(script, args):
    sys.argv = ['blender', '--'] + args
    try:
        runpy.run_path(script, run_name='__main__')
        RES[os.path.basename(script)] = 'ok'
    except SystemExit as e:
        RES[os.path.basename(script)] = f'exit {e.code}'
    except Exception:
        RES[os.path.basename(script)] = traceback.format_exc()[-1500:]
        print(RES[os.path.basename(script)], flush=True)
    json.dump(RES, open(os.path.join(OUT, 'rigb_run.json'), 'w'), indent=1)


hold = os.path.join(OUT, 'holding', CLS, CLS + '.fbx')
repo_hold = os.path.join(REPO, 'Assets/_Game/Art/_gen2~/holding', CLS, CLS + '.fbx')
if not os.path.exists(hold) and os.path.exists(repo_hold) and os.path.getsize(repo_hold) > 1000:
    hold = repo_hold
    RES['holding'] = 'repo gen-models-2'
elif 'clean' in STEPS:
    man = json.load(open(os.path.join(HERE, 'rigb_manifest.json')))
    man['clean'] = [e for e in man['clean'] if e['name'] == CLS]
    mp = os.path.join(OUT, 'rigb_manifest_' + CLS + '.json')
    json.dump(man, open(mp, 'w'), indent=1)
    run(os.path.join(REPO, 'Assets/_Game/Art/_gen2~/tools/clean_export.py'), [OUT, REPO, INP, mp])
    RES['holding'] = 'cleaned in this run from ' + INP
if 'rig' in STEPS:
    run(os.path.join(HERE, 'rig_body.py'), [OUT, REPO, CLS, hold, FING])
if 'bake' in STEPS:
    fbx = os.path.join(OUT, CLS + '.fbx')
    run(os.path.join(HERE, 'bake_clips.py'), [OUT, REPO, CLS, fbx if os.path.exists(fbx) else ''])
print('RIGB', json.dumps(RES)[:2000], flush=True)
