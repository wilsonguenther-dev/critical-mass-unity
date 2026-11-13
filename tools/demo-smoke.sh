#!/usr/bin/env bash
# DG-05 demo smoke: run a BUILT Mac player with -autoplay -quitAfterMatch through one full 16v16 match and check
# Player.log. Pass = the player exits 0, logs "[CM.Autoplay] MATCH END" and Player.log holds zero exception lines.
#   tools/demo-smoke.sh [--app PATH.app] [--out DIR] [--speed X] [--novsync] [--width W --height H] [--timeout S]
# Defaults: the app this checkout's build-mac makes, out Logs/demo-smoke/<utc>, the game's own speed (a 480 s match
# at 2x = about 4 minutes), 1920x1080 windowed, timeout 900 s (the player quits itself; this script's hard limit is
# timeout + 120 s). Writes Player.log, perf.json (frame times, markers) and summary.json into DIR; prints the summary.
# Windows (one line, PowerShell, in the folder holding CriticalMass.exe):
#   .\CriticalMass.exe -batchmode -nographics -autoplay -quitAfterMatch -autoplaySpeed 8 -logFile Player.log -perfLog perf.json; "exit $LASTEXITCODE"; Select-String -Path Player.log -Pattern 'Exception','MATCH END'
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$ROOT/Builds/macOS/CriticalMass.app"
OUT=""; SPEED=""; NOVSYNC=(); W=1920; H=1080; TIMEOUT=900
while [[ $# -gt 0 ]]; do
  case "$1" in
    --app) APP="${2:?}"; shift 2 ;;
    --out) OUT="${2:?}"; shift 2 ;;
    --speed) SPEED="${2:?}"; shift 2 ;;
    --novsync) NOVSYNC=(-novsync); shift ;;
    --width) W="${2:?}"; shift 2 ;;
    --height) H="${2:?}"; shift 2 ;;
    --timeout) TIMEOUT="${2:?}"; shift 2 ;;
    *) echo "usage: demo-smoke.sh [--app PATH.app] [--out DIR] [--speed X] [--novsync] [--width W --height H] [--timeout S]" >&2; exit 64 ;;
  esac
done
[[ -d "$APP" ]] || { echo "demo-smoke: FAILED, no app at $APP (run tools/unity-build-mac.sh first)" >&2; exit 66; }
EXE_NAME="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$APP/Contents/Info.plist")"
EXE="$APP/Contents/MacOS/$EXE_NAME"
[[ -x "$EXE" ]] || { echo "demo-smoke: FAILED, no executable at $EXE" >&2; exit 66; }
OUT="${OUT:-$ROOT/Logs/demo-smoke/$(date -u +%Y%m%dT%H%M%SZ)}"
mkdir -p "$OUT"
LOG="$OUT/Player.log"; PERF="$OUT/perf.json"; SUM="$OUT/summary.json"
rm -f "$LOG" "$PERF" "$SUM"

others="$(pgrep -f 'Unity.app/Contents/MacOS/Unity' | tr '\n' ' ' || true)"
launch_ms="$(python3 -c 'import time; print(int(time.time()*1000))')"
set +e
perl -e 'alarm shift; exec @ARGV' "$((TIMEOUT + 120))" \
  "$EXE" -autoplay -quitAfterMatch -autoplayTimeout "$TIMEOUT" ${SPEED:+-autoplaySpeed "$SPEED"} ${NOVSYNC[@]+"${NOVSYNC[@]}"} \
  -screen-fullscreen 0 -screen-width "$W" -screen-height "$H" -logFile "$LOG" -perfLog "$PERF"
code=$?
set -e

python3 - "$LOG" "$PERF" "$SUM" "$code" "$launch_ms" "$others" <<'PY'
import json, re, sys
log, perf, out, code, launch_ms, others = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4]), int(sys.argv[5]), sys.argv[6]
txt = open(log, errors='replace').read() if __import__('os').path.exists(log) else ''
exc = sorted(set(m.group(0).strip()[:200] for m in re.finditer(r'^\s*(?:[\w.]+\.)?\w*Exception\b[^\n]*', txt, re.M)))
ended = '[CM.Autoplay] MATCH END' in txt
m = re.search(r'\[CM\.Boot\] title on screen ([\d.]+) s after engine start, (\S+) s after process start, utc_ms=(\d+)', txt)
p = json.load(open(perf)) if __import__('os').path.exists(perf) else {}
s = {'pass': code == 0 and ended and not exc, 'exit': code, 'match_end_logged': ended, 'exception_lines': len(exc),
     'exceptions': exc[:20], 'errors_logged': p.get('errors'), 'boot_to_title_from_launch_s':
     round((int(m.group(3)) - launch_ms) / 1000, 2) if m else None,
     'boot_to_title_engine_s': float(m.group(1)) if m else None, 'frame_ms': p.get('frame_ms'),
     'frames_over_ms': p.get('frames_over_ms'), 'frames': p.get('frames'), 'screen': p.get('screen'),
     'winner': p.get('winner'), 'sim_seconds': p.get('sim_seconds'), 'wall_seconds': p.get('wall_seconds'),
     'version': p.get('version'), 'commit': p.get('commit'), 'other_unity_pids': others.split(),
     'player_log': log, 'perf_json': perf}
open(out, 'w').write(json.dumps(s, indent=1) + '\n')
print(json.dumps({k: s[k] for k in ('pass', 'exit', 'match_end_logged', 'exception_lines', 'boot_to_title_from_launch_s',
                                     'frame_ms', 'frames_over_ms', 'screen', 'winner', 'other_unity_pids')}))
sys.exit(0 if s['pass'] else 1)
PY
