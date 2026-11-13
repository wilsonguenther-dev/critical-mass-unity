"""Run several Blender scripts in ONE Blender process (CI offload kit, ci/offload-2, 2026-11-13). Project-neutral.

  blender -b --factory-startup --python-exit-code 1 --python tools/ci/blender_plan.py -- [plan.json | '<json>']
  (no argument: the plan is read from the environment variable CI_PLAN)

plan: [{"script": "path/a.py", "argv": ["x", "y"], "open": "path/file.blend", "env": {"K": "v"}, "cwd": "dir"}, ...]
Paths are relative to the current directory (the repo root on CI). Every step runs even when an earlier one fails, so a
single launch yields both the build log and the renders; the exit code is the first non-zero step code. A summary is
printed as one line starting with CI_PLAN_RESULT and, when $CI_OUT is set, written to $CI_OUT/plan-result.json.
"""
import json
import os
import runpy
import sys
import time
import traceback

import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
src = argv[0] if argv else os.environ.get("CI_PLAN", "")
if not src:
    print("blender_plan: no plan (argument or CI_PLAN)", file=sys.stderr)
    sys.exit(64)
plan = json.loads(src) if src.lstrip().startswith("[") else json.load(open(src))
base = os.getcwd()
results = []
for i, step in enumerate(plan):
    script = os.path.join(base, step["script"])
    saved_env = {k: os.environ.get(k) for k in step.get("env", {})}
    os.environ.update({k: str(v) for k, v in step.get("env", {}).items()})
    t0 = time.time()
    code = 0
    try:
        os.chdir(os.path.join(base, step.get("cwd", ".")))
        if step.get("open"):
            bpy.ops.wm.open_mainfile(filepath=os.path.join(base, step["open"]))
        sys.argv = [script, "--"] + [str(a) for a in step.get("argv", [])]
        runpy.run_path(script, run_name="__main__")
    except SystemExit as e:
        code = e.code if isinstance(e.code, int) else (0 if e.code is None else 1)
    except Exception:
        traceback.print_exc()
        code = 1
    finally:
        os.chdir(base)
    for k, v in saved_env.items():
        if v is None:
            os.environ.pop(k, None)
        else:
            os.environ[k] = v
    results.append({"step": i, "script": step["script"], "exit": code, "seconds": round(time.time() - t0, 1)})
    print(f"blender_plan: step {i} {step['script']} exit {code} in {results[-1]['seconds']} s", flush=True)
first_bad = next((r["exit"] for r in results if r["exit"]), 0)
print("CI_PLAN_RESULT " + json.dumps(results), flush=True)
if os.environ.get("CI_OUT"):
    json.dump(results, open(os.path.join(os.environ["CI_OUT"], "plan-result.json"), "w"), indent=1)
sys.exit(first_bad)
