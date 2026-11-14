#!/usr/bin/env bash
# DG-05 demo smoke, made a real perf gate by DG-54 (2026-11-12 panel): run a BUILT Mac player with -autoplay
# -quitAfterMatch through back-to-back 16v16 matches, sample memory and CPU from outside, and FAIL when a budget is
# exceeded OR NOT MEASURED (fail closed), when the run started in safe mode, or when the quality is not the one asked for.
#   tools/demo-smoke.sh [--app PATH.app] [--out DIR] [--matches N] [--speed X] [--novsync] [--width W --height H]
#                       [--fullscreen] [--quality NAME] [--title-seconds S] [--timeout S] [--summarise-only DIR]
#                       [--qualities High,Medium,Low] [--trace-states] [--pacing rule,display,timed60]
# perf-smooth-1: --qualities runs match n at the n-th level (one hidden run measures three levels with the same match
# settings; perf-mN.json names each level); --trace-states records the shader variants and graphics states the run met
# (OUT/data/traces/<api>.graphicsstate, the source of Assets/StreamingAssets/ShaderWarmup). Each match also writes
# frames.csv / frames-mN.csv (the frame-cost recorder, FrameRecorder) and a "frame_recorder" summary in its perf json.
# Profiles: CM_PROFILE=default (High, 1920x1080 window) or CM_PROFILE=floor (Low, 1920x1080, 30 fps cap, the floor
# budgets). --fullscreen runs at the panel's native size in borderless fullscreen (proves the pixel cap).
# Budgets: tools/budgets.json (profile section); env CM_BUDGET_<KEY> overrides one (for example CM_BUDGET_P95_MS=20).
# Isolation: the player's files go to OUT/data (-dataDir), and the player's saved settings (macOS defaults domain
# com.drivia.criticalmass) are exported before the run and restored after it, so a smoke never changes the owner's
# settings or window size. The run deletes no session marker of the owner's.
# It runs itself inside the machine-wide limiter (~/bin/cm-heavy unity) when that exists.
# Writes Player.log, perf.json (+ perf-m2.json ...), samples.tsv and summary.json into OUT; prints one summary line.
# Exit: 0 pass, 1 fail (budget over or not measured), 66 no app, 64 usage.
# Windows: see tools/windows-run.ps1 (one command for the owner) and .github/workflows/windows-player.yml.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$ROOT/Builds/current/CriticalMass.app"
[[ -d "$APP" ]] || APP="$ROOT/Builds/macOS/CriticalMass.app"
ORIG_ARGS=("$@")   # kept for the cm-heavy re-exec below (the option loop shifts "$@" empty: --out and --quality were lost)
QUALITIES=(); TRACE=(); OUT=""; SPEED=""; NOVSYNC=(); W=1920; H=1080; TIMEOUT=900; MATCHES=3; TITLE_S=12; FULL=0; QUALITY=""; SUMMARISE=""
PROFILE="${CM_PROFILE:-default}"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --app) APP="${2:?}"; shift 2 ;;
    --out) OUT="${2:?}"; shift 2 ;;
    --matches) MATCHES="${2:?}"; shift 2 ;;
    --speed) SPEED="${2:?}"; shift 2 ;;
    --novsync) NOVSYNC=(-novsync); shift ;;
    --width) W="${2:?}"; shift 2 ;;
    --height) H="${2:?}"; shift 2 ;;
    --fullscreen) FULL=1; shift ;;
    --quality) QUALITY="${2:?}"; shift 2 ;;
    --title-seconds) TITLE_S="${2:?}"; shift 2 ;;
    --timeout) TIMEOUT="${2:?}"; shift 2 ;;
    --summarise-only) SUMMARISE="${2:?}"; shift 2 ;;
    --qualities) QUALITIES=(-autoplayQualities "${2:?}"); shift 2 ;;
    --trace-states) TRACE=(-traceStates); shift ;;
    --pacing) TRACE+=(-autoplayPacing "${2:?}"); shift 2 ;;
    *) echo "usage: demo-smoke.sh [--app PATH.app] [--out DIR] [--matches N] [--speed X] [--novsync] [--width W --height H] [--fullscreen] [--quality NAME] [--title-seconds S] [--timeout S] [--summarise-only DIR]" >&2; exit 64 ;;
  esac
done
case "$PROFILE" in
  floor) QUALITY="${QUALITY:-Low}"; W=1920; H=1080; FULL=0; CAP_ARGS=(-fpsCap 30) ;;
  default) QUALITY="${QUALITY:-High}"; CAP_ARGS=() ;;
  *) echo "demo-smoke: unknown CM_PROFILE=$PROFILE (default or floor)" >&2; exit 64 ;;
esac

summarise() {  # $1 out dir, $2 exit code, $3 launch ms, $4 other unity pids
python3 - "$1" "$2" "$3" "$4" "$MATCHES" "$TITLE_S" "${NOVSYNC[*]:-}" "$PROFILE" "$QUALITY" "$ROOT/tools/budgets.json" <<'PY'
import json, os, re, sys
out, code, launch_ms, others, matches, title_s, novsync, profile, quality, bfile = sys.argv[1:11]
code, launch_ms, matches, title_s = int(code), int(launch_ms), int(matches), float(title_s)
cfg = json.load(open(bfile))[profile]
def budget(k):
    v = os.environ.get('CM_BUDGET_' + k.upper())
    if v is not None:
        return float(v)
    b = cfg.get(k)
    if isinstance(b, dict):  # per quality level: the level the player really ran (perf.json), else the one asked for
        return b.get(q_real or quality)
    return b
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
sp = os.path.join(out, 'samples.tsv')
for i, l in enumerate(open(sp) if os.path.exists(sp) else []):
    if i == 0:
        continue
    f = l.rstrip('\n').split('\t')
    try:
        rows.append({'ms': int(f[0]), 'rss_mb': int(f[1]) / 1024, 'cpu': cpu_s(f[2]), 'title': f[3] == '1', 'ends': int(f[4] or 0)})
    except (ValueError, IndexError):
        pass
peak = max((r['rss_mb'] for r in rows), default=None)
rss_at_end = {}
for r in rows:
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
    try:
        perfs.append(json.load(open(p)))
    except (OSError, ValueError):
        perfs.append({})
p1 = perfs[0] if perfs else {}
mk = {x['name']: x for x in p1.get('markers', [])}
fm = p1.get('frame_ms') or {}
gpu = (p1.get('gpu_ms') or {}).get('p95')
cpu = (p1.get('cpu_main_ms') or {}).get('p95')
q_real, q_req = p1.get('quality'), p1.get('quality_requested')
pix = p1.get('render_pixels')
pix_cap = budget('render_pixels_max_high') if q_real in ('High', 'Very High', 'Ultra') and budget('render_pixels_max_high') else budget('render_pixels_max')
boot_s = round((int(m.group(3)) - launch_ms) / 1000, 2) if m else None
gfx = (p1.get('memory_mb') or {}).get('graphics_driver')
# Physics audit m1 / DG-111: triangles, batches and shadow casters are gated on the MATCH phase's p95 (perf.json
# "phases"), not on the run maximum that included title and loading frames. A player without phases falls back to the max.
phases = p1.get('phases') or {}
pm = phases.get('match') or {}
def p95(d, k):
    v = d.get(k)
    return v.get('p95') if isinstance(v, dict) else None
tri = p95(pm, 'triangles') if pm else mk.get('Triangles Count', {}).get('max')
bat = p95(pm, 'batches') if pm else mk.get('Batches Count', {}).get('max')
meas = {'peak_rss_mb': peak, 'growth_mb': growth if matches >= 3 else 0.0, 'p95_ms': fm.get('p95'), 'worst_hitch_ms': fm.get('worst'),
        'idle_cpu_pct': idle, 'batches': bat, 'triangles': tri,
        'render_pixels_max': pix, 'boot_to_title_s': boot_s}
# B2: GPU frame time must be measured on every profile (frame timing stats on); a null or 0 fails as not measured.
meas['gpu_p95_ms'] = gpu if gpu else None
if pm:
    meas['shadow_casters'] = p95(pm, 'shadow_casters')
if profile == 'floor':
    meas['cpu_main_p95_ms'] = cpu
if gfx:  # 0 in release players (Unity reports graphics-driver memory in development builds only): reported, not gated
    meas['gfx_mb'] = gfx
limits = {k: (pix_cap if k == 'render_pixels_max' else budget(k)) for k in meas}
not_measured = sorted(k for k, v in meas.items() if v is None)
no_budget = sorted(k for k, v in limits.items() if v is None)
over = sorted(k for k, v in meas.items() if v is not None and limits[k] is not None and v > limits[k])
problems = []
if not p1:
    problems.append('perf.json missing')
if p1.get('safe_mode') is True:
    problems.append('started in safe mode')
if q_req and q_real != q_req:
    problems.append(f'quality {q_real} is not the requested {q_req}')
if quality and q_real and q_real.replace(' ', '').lower() != quality.replace(' ', '').lower():
    problems.append(f'quality {q_real} is not --quality {quality}')
if p1 and p1.get('data_isolated') is not True:
    problems.append('the run did not use its own data folder (-dataDir)')
s = {'pass': code == 0 and ends == matches and not exc and not over and not not_measured and not no_budget and not problems,
     'profile': profile, 'exit': code, 'matches': matches, 'match_end_logged': ends, 'exception_lines': len(exc), 'exceptions': exc[:20],
     'errors_logged': p1.get('errors'), 'over_budget': over, 'not_measured': not_measured, 'no_budget': no_budget, 'problems': problems,
     'measured': meas, 'budgets': limits, 'quality': q_real, 'quality_requested': q_req, 'safe_mode': p1.get('safe_mode'),
     'render_scale': p1.get('render_scale'), 'world_triangles': p1.get('world_triangles'), 'render_tier': p1.get('render_tier'),
     'boot_to_title_engine_s': float(m.group(1)) if m else None,
     'frame_ms': fm, 'gpu_ms': p1.get('gpu_ms'), 'cpu_main_ms': p1.get('cpu_main_ms'), 'frames_over_ms': p1.get('frames_over_ms'),
     'setpass_max': mk.get('SetPass Calls Count', {}).get('max'), 'draw_calls_max': mk.get('Draw Calls Count', {}).get('max'),
     'top_time_markers': [(x['name'], round(x['mean'], 3)) for x in p1.get('markers', []) if x.get('unit') == 'ms'][:8],
     'rss_mb_at_match_end': rss_at_end, 'objects_per_match': [p.get('objects') for p in perfs],
     'sim_dropped_seconds': [p.get('sim_dropped_seconds') for p in perfs], 'hud_events_dropped': [p.get('hud_events_dropped') for p in perfs],
     'phases': phases, 'triangles_run_max': mk.get('Triangles Count', {}).get('max'),
     'assets': p1.get('assets'), 'screen': p1.get('screen'), 'frame_cap': p1.get('frame_cap'), 'vsync': p1.get('vsync'),
     'winner': [p.get('winner') for p in perfs], 'version': p1.get('version'), 'commit': p1.get('commit'),
     'other_unity_pids': others.split(), 'out': out}
open(os.path.join(out, 'summary.json'), 'w').write(json.dumps(s, indent=1) + '\n')
print(json.dumps({k: s[k] for k in ('pass', 'profile', 'exit', 'match_end_logged', 'exception_lines', 'over_budget', 'not_measured', 'problems', 'measured')}))
sys.exit(0 if s['pass'] else 1)
PY
}

if [[ -n "$SUMMARISE" ]]; then  # re-score an existing run folder (tests and re-analysis); no player starts
  set +e; summarise "$SUMMARISE" "${CM_SMOKE_EXIT:-0}" "${CM_SMOKE_LAUNCH_MS:-0}" ""; exit $?
fi

# One graphics-heavy job machine-wide: re-run this script inside ~/bin/cm-heavy's Unity slot.
if [[ -z "${CM_HEAVY_HELD:-}" && -x "$HOME/bin/cm-heavy" ]]; then
  CM_HEAVY_HELD=1 exec "$HOME/bin/cm-heavy" unity env CM_HEAVY_HELD=1 bash "${BASH_SOURCE[0]}" ${ORIG_ARGS[@]+"${ORIG_ARGS[@]}"}
fi
[[ -d "$APP" ]] || { echo "demo-smoke: FAILED, no app at $APP (run tools/unity-build-mac.sh first)" >&2; exit 66; }
EXE_NAME="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$APP/Contents/Info.plist")"
EXE="$APP/Contents/MacOS/$EXE_NAME"
[[ -x "$EXE" ]] || { echo "demo-smoke: FAILED, no executable at $EXE" >&2; exit 66; }
OUT="${OUT:-$ROOT/Logs/demo-smoke/$(date -u +%Y%m%dT%H%M%SZ)-$PROFILE}"
mkdir -p "$OUT/data"
LOG="$OUT/Player.log"; PERF="$OUT/perf.json"; SAMPLES="$OUT/samples.tsv"
rm -f "$LOG" "$OUT"/perf*.json "$OUT/summary.json" "$SAMPLES"

# Isolation of the owner's saved settings (PlayerPrefs live in this defaults domain on macOS).
DOMAIN="${CM_PREFS_DOMAIN:-com.drivia.criticalmass}"
HAD_PREFS=0
if defaults export "$DOMAIN" "$OUT/prefs-before.plist" 2>/dev/null; then HAD_PREFS=1; fi
restore_prefs() {
  if (( HAD_PREFS )); then defaults import "$DOMAIN" "$OUT/prefs-before.plist" 2>/dev/null || true
  else defaults delete "$DOMAIN" 2>/dev/null || true; fi
}
trap restore_prefs EXIT

if (( FULL )); then WIN=(-screen-fullscreen 1 -window-mode borderless); else WIN=(-screen-fullscreen 0 -screen-width "$W" -screen-height "$H"); fi
others="$(pgrep -f 'Unity.app/Contents/MacOS/Unity' | tr '\n' ' ' || true)"
launch_ms="$(python3 -c 'import time; print(int(time.time()*1000))')"
LIMIT=$((MATCHES * (TIMEOUT + 60) + 120))
set +e
ARGS=(-autoplay -quitAfterMatch -autoplayMatches "$MATCHES" -autoplayTitleSeconds "$TITLE_S" \
  -autoplayTimeout "$TIMEOUT" ${SPEED:+-autoplaySpeed "$SPEED"} ${NOVSYNC[@]+"${NOVSYNC[@]}"} ${CAP_ARGS[@]+"${CAP_ARGS[@]}"} \
  ${QUALITIES[@]+"${QUALITIES[@]}"} ${TRACE[@]+"${TRACE[@]}"} -quality "$QUALITY" -dataDir "$OUT/data" "${WIN[@]}" -logFile "$LOG" -perfLog "$PERF")
# Integration batch 3 (owner report 2026-11-12): automated runs never take the owner's screen. By default the player is
# launched through LaunchServices hidden and in the background (open -g -j: no activation, no focus steal, no visible
# window); CM_SMOKE_VISIBLE=1 restores the old direct launch. The watchdog replaces perl's alarm for the hidden path.
# fix/fighters-always-visible (owner report 2026-11-13: the player activates itself and re-shows its window at boot and at
# every match load, so open -g -j alone did not keep it hidden): the GAME now hides itself whenever an automation flag is
# on its command line (Runtime/Game/AutomationWindow.cs). CM_SMOKE_VISIBLE=1 also passes -showWindow, the only opt-in.
[[ "${CM_SMOKE_VISIBLE:-0}" == 1 ]] && ARGS+=(-showWindow)
WATCHDOG=""
if [[ "${CM_SMOKE_VISIBLE:-0}" != 1 ]]; then
  BUNDLE="${EXE%/Contents/MacOS/*}"
  open -g -j -n "$BUNDLE" --args "${ARGS[@]}"
  pid=""
  for _ in $(seq 1 30); do pid="$(pgrep -n -f -- "$LOG" || true)"; [[ -n "$pid" ]] && break; sleep 1; done
  if [[ -z "$pid" ]]; then echo "demo-smoke: the hidden player did not start" >&2; pid=999999999; fi
  ( sleep "$LIMIT"; kill "$pid" 2>/dev/null ) & WATCHDOG=$!
else
  perl -e 'alarm shift; exec @ARGV' "$LIMIT" "$EXE" "${ARGS[@]}" &
  pid=$!
fi
# Outside view, once a second: wall ms, resident KB, CPU time, log markers. Low process churn (integration batch 3: the
# machine guard's STOP-DISPATCH fired at 800+ new processes a minute): one ps, one grep and one sleep a second, bash
# builtins for the rest (the wall clock is one date at the start plus $SECONDS, read).
printf 'ms\trss_kb\tcputime\ttitle\tmatch_ends\n' > "$SAMPLES"
t=0; e=0; epoch0=$(( $(date +%s) - SECONDS ))
while kill -0 "$pid" 2>/dev/null; do
  line="$(ps -o rss=,cputime= -p "$pid" 2>/dev/null || true)"
  if [[ -n "$line" ]]; then
    if [[ -f "$LOG" ]]; then   # one grep a second: the title flag until seen, then the match-end count (growth needs every end)
      if (( ! t )); then grep -q '\[CM.Boot\] title on screen' "$LOG" && t=1
      else e="$(grep -c '\[CM.Autoplay\] MATCH END' "$LOG" || true)"; fi
    fi
    read -r rss cput _ <<<"$line"
    printf '%s000\t%s\t%s\t%s\t%s\n' "$((epoch0 + SECONDS))" "$rss" "$cput" "$t" "$e" >> "$SAMPLES"
  fi
  sleep 1
done
if [[ -n "$WATCHDOG" ]]; then
  # Not our child: the exit code is read from the log (the player logs MATCH END and quits 0 after its matches).
  if kill -0 "$WATCHDOG" 2>/dev/null; then kill "$WATCHDOG" 2>/dev/null; code=0; else code=142; fi
else
  wait "$pid"; code=$?
fi
set -e
set +e; summarise "$OUT" "$code" "$launch_ms" "$others"; rc=$?; set -e
exit "$rc"
