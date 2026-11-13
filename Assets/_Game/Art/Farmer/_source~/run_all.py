"""ONE Blender process for the whole build-verify-render chain (machine rule 2026-11-12: at most one Blender launch per
4 minutes; never quick probe runs). Runs each step script in this process with its own argv and environment.

  ~/bin/cm-heavy blender perl -e 'alarm 2400; exec @ARGV' /Applications/Blender.app/Contents/MacOS/Blender -b \
      --factory-startup --python-exit-code 1 -P run_all.py -- plan.json

plan.json: [{"script": "build_farmer.py", "argv": []},
            {"script": "render_farmer.py", "argv": ["bible", "<dir>", "720"], "env": {"CM_POSES": "<json>"}}, ...]
Measurements and side-by-sides are done afterwards in plain Python (compare_farmer.py, fit_report.py), outside Blender.
"""
import json
import os
import runpy
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
plan = json.load(open(sys.argv[sys.argv.index("--") + 1]))
codes = []
for step in plan:
    script = os.path.join(HERE, step["script"])
    old_env = {k: os.environ.get(k) for k in step.get("env", {})}
    os.environ.update(step.get("env", {}))
    sys.argv = [script, "--"] + [str(a) for a in step.get("argv", [])]
    t0 = time.time()
    code = 0
    try:
        runpy.run_path(script, run_name="__main__")
    except SystemExit as e:
        code = e.code if isinstance(e.code, int) else (0 if e.code is None else 1)
    for k, v in old_env.items():
        if v is None:
            os.environ.pop(k, None)
        else:
            os.environ[k] = v
    codes.append(code)
    print(f"RUN_ALL step {step['script']} {step.get('argv', [])} exit {code} {time.time() - t0:.1f}s", flush=True)
print("RUN_ALL done", codes, flush=True)
sys.exit(1 if any(codes) else 0)
