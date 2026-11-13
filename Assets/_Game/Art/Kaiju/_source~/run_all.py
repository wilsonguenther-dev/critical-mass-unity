"""ONE Blender process for the whole Kaiju chain (machine rule 2026-11-12: one Blender launch per 5 minutes, never
probe runs). Same idea as Characters/_source~/run_all.py, plus an "open" step for the scripts that expect Kaiju.blend.

  ~/bin/cm-heavy blender perl -e 'alarm 1700; exec @ARGV' /Applications/Blender.app/Contents/MacOS/Blender -b \
      --factory-startup --python-exit-code 1 -P run_all.py -- plan.json

plan.json: [{"script": "build_kaiju.py"}, {"script": "check_kaiju.py", "env": {...}},
            {"open": "Kaiju.blend", "script": "render_kaiju.py", "argv": ["<dir>", "<Warrior.fbx>", "job", ...]}]
A failing step is recorded and the later steps still run (so one launch always yields renders AND the check log).
Measuring and composing happen afterwards in plain Python (fit_kaiju.py), outside Blender.
"""
import json
import os
import runpy
import sys
import time
import traceback

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
plan = json.load(open(sys.argv[sys.argv.index("--") + 1]))
codes = []
for step in plan:
    script = os.path.join(HERE, step["script"])
    old_env = {k: os.environ.get(k) for k in step.get("env", {})}
    os.environ.update(step.get("env", {}))
    t0 = time.time()
    code = 0
    try:
        if step.get("open"):
            bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE, step["open"]))
        sys.argv = [script, "--"] + [str(a) for a in step.get("argv", [])]
        runpy.run_path(script, run_name="__main__")
    except SystemExit as e:
        code = e.code if isinstance(e.code, int) else (0 if e.code is None else 1)
    except Exception:
        traceback.print_exc()
        code = 1
    for k, v in old_env.items():
        if v is None:
            os.environ.pop(k, None)
        else:
            os.environ[k] = v
    codes.append(code)
    print("RUN_ALL step %s %s exit %s %.1fs" % (step["script"], step.get("argv", [])[2:], code, time.time() - t0), flush=True)
print("RUN_ALL done", codes, flush=True)
sys.exit(1 if any(codes) else 0)
