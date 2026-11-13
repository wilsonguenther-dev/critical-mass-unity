#!/usr/bin/env bash
# DG-05 demo smoke + budgets: run a BUILT Mac player with -autoplay -quitAfterMatch through back-to-back 16v16 matches,
# sample its memory and CPU from outside, and fail when a budget is exceeded.
#   tools/demo-smoke.sh [--app PATH.app] [--out DIR] [--matches N] [--speed X] [--novsync] [--width W --height H]
#                       [--title-seconds S] [--timeout S]
# It runs itself inside the machine-wide limiter (~/bin/cm-heavy unity) when that exists.
# Defaults: this checkout's build-mac app, out Logs/demo-smoke/<utc>, 3 matches (480 s each at the game's 2x speed, about
# 4 minutes each), 1920x1080 windowed, 12 s idle on the title first, timeout 900 s per match (the player quits itself;
# this script's hard limit is matches x (timeout + 60) + 120 s).
# PASS needs all of: player exit 0, every match logs "[CM.Autoplay] MATCH END", zero exception lines in Player.log, and
# every budget below (override with env vars). A budget whose counter the run did not produce FAILS the run (fail closed,
# DG-54): it is listed under not_measured.
#   CM_BUDGET_PEAK_RSS_MB=1536   peak resident memory of the player over the run
#   CM_BUDGET_GROWTH_MB=50       resident memory at the end of the last match minus the end of match 1 (3+ matches)
#   CM_BUDGET_P95_MS=17.5        match 1 p95 frame time with the 60 fps cap (a vsync miss is 33 ms); 16.7 with --novsync
#   CM_BUDGET_IDLE_CPU_PCT=35    CPU on the title screen, percent of one core, from 2 s after the title to 1 s before it goes
#   CM_BUDGET_BATCHES=1500       worst Batches Count in match 1 (the gameplay camera is the wide view; no zoom exists)
#   CM_BUDGET_TRIANGLES=1000000  worst Triangles Count in match 1
# Reported only: worst hitch, boot to title, SetPass calls, Unity and graphics memory, the largest textures and meshes,
# textures flagged (over 2048 px or uncompressed). Writes Player.log, perf.json (+ perf-m2.json ...), samples.tsv and
# summary.json into DIR; prints one summary line.
# Windows (one line, PowerShell, in the folder holding CriticalMass.exe):
#   .\CriticalMass.exe -batchmode -nographics -autoplay -quitAfterMatch -autoplaySpeed 8 -logFile Player.log -perfLog perf.json; "exit $LASTEXITCODE"; Select-String -Path Player.log -Pattern 'Exception','MATCH END'
set -euo pipefail
# One graphics-heavy job machine-wide (2026-11-12 watchdog restart): re-run this script inside ~/bin/cm-heavy's Unity slot.
if [[ -z "${CM_HEAVY_HELD:-}" && -x "$HOME/bin/cm-heavy" ]]; then
  CM_HEAVY_HELD=1 exec "$HOME/bin/cm-heavy" unity env CM_HEAVY_HELD=1 bash "${BASH_SOURCE[0]}" "$@"
fi
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$ROOT/Builds/macOS/CriticalMass.app"
OUT=""; SPEED=""; NOVSYNC=(); W=1920; H=1080; TIMEOUT=900; MATCHES=3; TITLE_S=12
while [[ $# -gt 0 ]]; do
  case "$1" in
    --app) APP="${2:?}"; shift 2 ;;
    --out) OUT="${2:?}"; shift 2 ;;
    --matches) MATCHES="${2:?}"; shift 2 ;;
    --speed) SPEED="${2:?}"; shift 2 ;;
    --novsync) NOVSYNC=(-novsync); shift ;;
    --width) W="${2:?}"; shift 2 ;;
    --height) H="${2:?}"; shift 2 ;;
    --title-seconds) TITLE_S="${2:?}"; shift 2 ;;
    --timeout) TIMEOUT="${2:?}"; shift 2 ;;
    *) echo "usage: demo-smoke.sh [--app PATH.app] [--out DIR] [--matches N] [--speed X] [--novsync] [--width W --height H] [--title-seconds S] [--timeout S]" >&2; exit 64 ;;
  esac
done
[[ -d "$APP" ]] || { echo "demo-smoke: FAILED, no app at $APP (run tools/unity-build-mac.sh first)" >&2; exit 66; }
EXE_NAME="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$APP/Contents/Info.plist")"
EXE="$APP/Contents/MacOS/$EXE_NAME"
[[ -x "$EXE" ]] || { echo "demo-smoke: FAILED, no executable at $EXE" >&2; exit 66; }
OUT="${OUT:-$ROOT/Logs/demo-smoke/$(date -u +%Y%m%dT%H%M%SZ)}"
mkdir -p "$OUT"
LOG="$OUT/Player.log"; PERF="$OUT/perf.json"; SUM="$OUT/summary.json"; SAMPLES="$OUT/samples.tsv"
rm -f "$LOG" "$OUT"/perf*.json "$SUM" "$SAMPLES"

others="$(pgrep -f 'Unity.app/Contents/MacOS/Unity' | tr '\n' ' ' || true)"
launch_ms="$(python3 -c 'import time; print(int(time.time()*1000))')"
LIMIT=$((MATCHES * (TIMEOUT + 60) + 120))
set +e
perl -e 'alarm shift; exec @ARGV' "$LIMIT" \
  "$EXE" -autoplay -quitAfterMatch -autoplayMatches "$MATCHES" -autoplayTitleSeconds "$TITLE_S" \
  -autoplayTimeout "$TIMEOUT" ${SPEED:+-autoplaySpeed "$SPEED"} ${NOVSYNC[@]+"${NOVSYNC[@]}"} \
  -screen-fullscreen 0 -screen-width "$W" -screen-height "$H" -logFile "$LOG" -perfLog "$PERF" &
pid=$!
# Outside view, once a second: wall ms, resident KB, cumulative CPU time (ps cputime), and the log markers seen so far.
printf 'ms\trss_kb\tcputime\ttitle\tmatch_ends\n' > "$SAMPLES"
while kill -0 "$pid" 2>/dev/null; do
  line="$(ps -o rss=,cputime= -p "$pid" 2>/dev/null || true)"
  if [[ -n "$line" ]]; then
    t=0; e=0
    if [[ -f "$LOG" ]]; then
      grep -q '\[CM.Boot\] title on screen' "$LOG" && t=1
      e="$(grep -c '\[CM.Autoplay\] MATCH END' "$LOG" || true)"
    fi
    printf '%s\t%s\t%s\t%s\t%s\n' "$(python3 -c 'import time; print(int(time.time()*1000))')" \
      "$(awk '{print $1}' <<<"$line")" "$(awk '{print $2}' <<<"$line")" "$t" "$e" >> "$SAMPLES"
  fi
  sleep 1
done
wait "$pid"; code=$?
set -e

python3 - "$OUT" "$code" "$launch_ms" "$others" "$MATCHES" "$TITLE_S" "${NOVSYNC[*]:-}" <<'PY'
import json, os, re, sys
out, code, launch_ms, others, matches, title_s, novsync = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), sys.argv[4], int(sys.argv[5]), float(sys.argv[6]), sys.argv[7]
env = lambda k, d: float(os.environ.get(k, d))
B = {'peak_rss_mb': env('CM_BUDGET_PEAK_RSS_MB', 1536), 'growth_mb': env('CM_BUDGET_GROWTH_MB', 50),
     'p95_ms': env('CM_BUDGET_P95_MS', 16.7 if novsync else 17.5), 'idle_cpu_pct': env('CM_BUDGET_IDLE_CPU_PCT', 35),
     'batches': env('CM_BUDGET_BATCHES', 1500), 'triangles': env('CM_BUDGET_TRIANGLES', 1000000)}
log = os.path.join(out, 'Player.log')
txt = open(log, errors='replace').read() if os.path.exists(log) else ''
exc = sorted(set(m.group(0).strip()[:200] for m in re.finditer(r'^\s*(?:[\w.]+\.)?\w*Exception\b[^\n]*', txt, re.M)))
ends = txt.count('[CM.Autoplay] MATCH END')
m = re.search(r'\[CM\.Boot\] title on screen ([\d.]+) s after engine start, (\S+) s after process start, utc_ms=(\d+)', txt)

def cpu_s(s):  # ps cputime: [[dd-]hh:]mm:ss[.cc]
    d = 0
    if '-' in s:
        d, s = s.split('-', 1)
    parts = [float(x) for x in s.split(':')]
    while len(parts) < 3:
        parts.insert(0, 0.0)
    return int(d) * 86400 + parts[0] * 3600 + parts[1] * 60 + parts[2]

rows = []
for i, l in enumerate(open(os.path.join(out, 'samples.tsv'))):
    if i == 0:
        continue
    f = l.rstrip('\n').split('\t')
    try:
        rows.append({'ms': int(f[0]), 'rss_mb': int(f[1]) / 1024, 'cpu': cpu_s(f[2]), 'title': f[3] == '1', 'ends': int(f[4] or 0)})
    except (ValueError, IndexError):
        pass
peak = max((r['rss_mb'] for r in rows), default=None)
rss_at_end = {}
for r in rows:  # first sample after each MATCH END appeared
    if r['ends'] and r['ends'] not in rss_at_end:
        rss_at_end[r['ends']] = r['rss_mb']
growth = (rss_at_end[max(rss_at_end)] - rss_at_end[1]) if len(rss_at_end) >= 3 and 1 in rss_at_end else None
idle = None
t0 = next((r['ms'] for r in rows if r['title']), None)
if t0 is not None:
    win = [r for r in rows if t0 + 2000 <= r['ms'] <= t0 + (title_s - 1) * 1000]
    if len(win) >= 3 and win[-1]['ms'] > win[0]['ms']:
        idle = 100.0 * (win[-1]['cpu'] - win[0]['cpu']) / ((win[-1]['ms'] - win[0]['ms']) / 1000)
perfs = []
for k in range(1, matches + 1):
    p = os.path.join(out, 'perf.json' if k == 1 else f'perf-m{k}.json')
    perfs.append(json.load(open(p)) if os.path.exists(p) else {})
p1 = perfs[0] if perfs else {}
mk = {x['name']: x for x in p1.get('markers', [])}
batches = mk.get('Batches Count', {}).get('max')
tris = mk.get('Triangles Count', {}).get('max')
p95 = (p1.get('frame_ms') or {}).get('p95')
meas = {'peak_rss_mb': peak, 'growth_mb': growth, 'p95_ms': p95, 'idle_cpu_pct': idle, 'batches': batches, 'triangles': tris}
over = [k for k, v in meas.items() if v is not None and v > B[k]]
not_measured = [k for k, v in meas.items() if v is None]
boot_s = round((int(m.group(3)) - launch_ms) / 1000, 2) if m else None
if boot_s is None:
    not_measured.append('boot_to_title_s')
# Fail closed (DG-54): a number the run did not produce is a failure, never a silent pass.
s = {'pass': code == 0 and ends == matches and not exc and not over and not not_measured, 'exit': code, 'matches': matches,
     'match_end_logged': ends, 'exception_lines': len(exc), 'exceptions': exc[:20],
     'errors_logged': p1.get('errors'), 'over_budget': over, 'not_measured': not_measured, 'measured': meas, 'budgets': B,
     'boot_to_title_from_launch_s': boot_s,
     'boot_to_title_engine_s': float(m.group(1)) if m else None,
     'frame_ms': p1.get('frame_ms'), 'frames_over_ms': p1.get('frames_over_ms'),
     'setpass_max': mk.get('SetPass Calls Count', {}).get('max'), 'draw_calls_max': mk.get('Draw Calls Count', {}).get('max'),
     'top_time_markers': [(x['name'], round(x['mean'], 3)) for x in p1.get('markers', []) if x.get('unit') == 'ms'][:8],
     'rss_mb_at_match_end': rss_at_end, 'unity_mb_per_match': [(p.get('memory_mb') or {}).get('unity_allocated') for p in perfs],
     'assets': p1.get('assets'), 'screen': p1.get('screen'), 'frame_cap': p1.get('frame_cap'), 'vsync': p1.get('vsync'),
     'quality': p1.get('quality'), 'winner': [p.get('winner') for p in perfs], 'version': p1.get('version'),
     'commit': p1.get('commit'), 'other_unity_pids': others.split(), 'out': out}
open(os.path.join(out, 'summary.json'), 'w').write(json.dumps(s, indent=1) + '\n')
print(json.dumps({k: s[k] for k in ('pass', 'exit', 'match_end_logged', 'exception_lines', 'over_budget', 'not_measured', 'measured',
                                     'boot_to_title_from_launch_s', 'frame_ms', 'other_unity_pids')}))
sys.exit(0 if s['pass'] else 1)
PY
