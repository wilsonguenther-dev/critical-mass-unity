"""ONE Blender launch: build, FBX export, verify and every render in a single process (machine rule 2026-11-12:
at most one Blender launch per 4 minutes, no short probe runs).

  ~/bin/cm-heavy blender perl -e 'alarm 2400; exec @ARGV' /Applications/Blender.app/Contents/MacOS/Blender -b \
      --factory-startup --python-exit-code 1 -P run_all.py -- <render_dir> [ortho_height_px]
Measurements (silhouette overlap, sheets) run afterwards in plain python3: compare_healer.py.
"""
import os
import runpy
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = args[0] if args else "/tmp/healer-renders"
OH = args[1] if len(args) > 1 else "600"
status = {}
for script, a in (("build_healer.py", []), ("verify_healer.py", []), ("render_healer.py", [OUT, OH])):
    sys.argv = [script] + (["--"] + a if a else [])
    try:
        runpy.run_path(os.path.join(HERE, script), run_name="__main__")
        status[script] = 0
    except SystemExit as e:
        status[script] = 0 if e.code in (None, 0) else e.code
    except Exception as e:      # keep going so one launch still yields renders to inspect
        import traceback
        traceback.print_exc()
        status[script] = repr(e)
    print("STEP", script, status[script], flush=True)
print("RUN_ALL", status)
sys.exit(0 if all(v == 0 for v in status.values()) else 1)
