#!/usr/bin/env bash
# DG-55: a real soak of the SHIPPED player (the editor "soak" of NUnit Slow tests is now the sim-long gate).
#   tools/player-soak.sh [--app PATH.app] [--out DIR] [--matches N] [--min-seconds S] [--safe] [--summarise-only DIR]
# Plays N (default 20) matches back to back through "Play again" in ONE process (-autoplayMatches; no relaunch) at the
# game's own 1x-equivalent pace (-autoplaySpeed 1), borderless fullscreen-window, own data folder, the owner's saved
# settings exported and restored. Runs inside ~/bin/cm-heavy and refuses while ~/cm-incidents/STOP-DISPATCH exists
# (the machine guard is red). Every 30 s: resident memory, Player.log size, open files (lsof) and threads (ps -M);
# at each match end the player's perf-mN.json carries frame p99 and worst, graphics memory and Mesh, Material,
# Texture, GameObject and AudioSource counts. Writes soak.json; exit 0 only when every soak budget in
# tools/budgets.json holds: >= 20 matches and >= 7200 s, RSS slope < 3 MB per match from match 3, object counts at
# match 3 equal to match 2, max frame < 250 ms, threads and open files within 5%, Player.log < 5 MB, 0 exceptions.
# --safe adds -safe (the plan's extra safe-mode pass).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$ROOT/Builds/current/CriticalMass.app"; OUT=""; MATCHES=20; MIN_S=7200; SAFE=(); SUMMARISE=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --app) APP="${2:?}"; shift 2 ;;
    --out) OUT="${2:?}"; shift 2 ;;
    --matches) MATCHES="${2:?}"; shift 2 ;;
    --min-seconds) MIN_S="${2:?}"; shift 2 ;;
    --safe) SAFE=(-safe); shift ;;
    --summarise-only) SUMMARISE="${2:?}"; shift 2 ;;
    *) echo "usage: player-soak.sh [--app PATH.app] [--out DIR] [--matches N] [--min-seconds S] [--safe] [--summarise-only DIR]" >&2; exit 64 ;;
  esac
done

summarise() {
python3 - "$1" "$2" "$MATCHES" "$MIN_S" "$ROOT/tools/budgets.json" <<'PY'
import glob, json, os, re, sys
out, code, want, min_s, bfile = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), float(sys.argv[4]), sys.argv[5]
B = json.load(open(bfile))['soak']
rows = []
p = os.path.join(out, 'soak-samples.tsv')
for i, l in enumerate(open(p) if os.path.exists(p) else []):
    f = l.rstrip('\n').split('\t')
    if i == 0 or len(f) < 6:
        continue
    try:
        rows.append({'s': float(f[0]), 'rss_mb': int(f[1]) / 1024, 'log_kb': int(f[2]), 'files': int(f[3]), 'threads': int(f[4]), 'ends': int(f[5])})
    except ValueError:
        pass
perfs = []
for k in range(1, want + 1):
    q = os.path.join(out, 'perf.json' if k == 1 else f'perf-m{k}.json')
    try:
        perfs.append(json.load(open(q)))
    except (OSError, ValueError):
        perfs.append(None)
done = [x for x in perfs if x]
txt = open(os.path.join(out, 'Player.log'), errors='replace').read() if os.path.exists(os.path.join(out, 'Player.log')) else ''
exc = len(re.findall(r'^\s*(?:[\w.]+\.)?\w*Exception\b', txt, re.M))
secs = rows[-1]['s'] if rows else 0
rss_end = {}
for r in rows:
    if r['ends'] and r['ends'] not in rss_end:
        rss_end[r['ends']] = r['rss_mb']
slope = None
pts = sorted((k, v) for k, v in rss_end.items() if k >= 3)
if len(pts) >= 2:
    n = len(pts); mx = sum(k for k, _ in pts) / n; my = sum(v for _, v in pts) / n
    den = sum((k - mx) ** 2 for k, _ in pts)
    slope = sum((k - mx) * (v - my) for k, v in pts) / den if den else None
objs = [x.get('objects') for x in perfs[:3]] if len(perfs) >= 3 else []
obj_equal = len(objs) == 3 and objs[1] is not None and objs[1] == objs[2]
worst = max(((x.get('frame_ms') or {}).get('worst') or 0) for x in done) if done else None
def within(key):
    vals = [r[key] for r in rows if r[key] > 0]
    if len(vals) < 4:
        return None
    a, b = vals[len(vals) // 4], vals[-1]   # after warm-up vs the end
    return abs(b - a) <= max(2, a * B['threads_files_tolerance'])
log_mb = (rows[-1]['log_kb'] / 1024) if rows else None
checks = {
    'matches': len(done) >= max(want, B['min_matches']) if want >= B['min_matches'] else len(done) >= want,
    'seconds': secs >= min_s,
    'rss_slope': slope is not None and slope < B['rss_slope_mb_per_match'],
    'objects_m3_equal_m2': obj_equal,
    'max_frame': worst is not None and worst < B['max_frame_ms'],
    'threads': within('threads'),
    'open_files': within('files'),
    'player_log': log_mb is not None and log_mb < B['player_log_mb'],
    'exceptions': exc <= B['exceptions'],
    'exit': code == 0,
}
s = {'pass': all(v is True for v in checks.values()), 'checks': checks, 'matches': len(done), 'seconds': secs,
     'rss_slope_mb_per_match': slope, 'rss_mb_at_match_end': rss_end, 'objects_first_three': objs, 'max_frame_ms': worst,
     'player_log_mb': log_mb, 'exceptions': exc, 'exit': code, 'not_measured': sorted(k for k, v in checks.items() if v is None)}
open(os.path.join(out, 'soak.json'), 'w').write(json.dumps(s, indent=1) + '\n')
print(json.dumps({k: s[k] for k in ('pass', 'checks', 'matches', 'seconds', 'rss_slope_mb_per_match', 'max_frame_ms')}))
sys.exit(0 if s['pass'] else 1)
PY
}
if [[ -n "$SUMMARISE" ]]; then set +e; summarise "$SUMMARISE" "${CM_SOAK_EXIT:-0}"; exit $?; fi

[[ ! -e "$HOME/cm-incidents/STOP-DISPATCH" ]] || { echo "player-soak: refused, the machine guard is red (~/cm-incidents/STOP-DISPATCH)" >&2; exit 75; }
if [[ -z "${CM_HEAVY_HELD:-}" && -x "$HOME/bin/cm-heavy" ]]; then
  CM_HEAVY_HELD=1 exec "$HOME/bin/cm-heavy" unity env CM_HEAVY_HELD=1 bash "${BASH_SOURCE[0]}" "$@"
fi
[[ -d "$APP" ]] || { echo "player-soak: FAILED, no app at $APP" >&2; exit 66; }
EXE="$APP/Contents/MacOS/$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$APP/Contents/Info.plist")"
OUT="${OUT:-$ROOT/Logs/player-soak/$(date -u +%Y%m%dT%H%M%SZ)${SAFE:+-safe}}"
mkdir -p "$OUT/data"
DOMAIN="${CM_PREFS_DOMAIN:-com.drivia.criticalmass}"; HAD=0
defaults export "$DOMAIN" "$OUT/prefs-before.plist" 2>/dev/null && HAD=1
trap 'if (( HAD )); then defaults import "$DOMAIN" "$OUT/prefs-before.plist" 2>/dev/null || true; else defaults delete "$DOMAIN" 2>/dev/null || true; fi' EXIT
LIMIT=$((MIN_S * 2 + 1800))
start="$(date +%s)"
set +e
perl -e 'alarm shift; exec @ARGV' "$LIMIT" "$EXE" -autoplay -quitAfterMatch -autoplayMatches "$MATCHES" -autoplaySpeed 1 \
  -autoplayTimeout 1200 -autoplayTitleSeconds 5 -dataDir "$OUT/data" -screen-fullscreen 1 -window-mode borderless \
  ${SAFE[@]+"${SAFE[@]}"} -logFile "$OUT/Player.log" -perfLog "$OUT/perf.json" &
pid=$!
printf 's\trss_kb\tlog_kb\topen_files\tthreads\tmatch_ends\n' > "$OUT/soak-samples.tsv"
while kill -0 "$pid" 2>/dev/null; do
  rss="$(ps -o rss= -p "$pid" 2>/dev/null | tr -d ' ' || echo 0)"
  files="$(lsof -p "$pid" 2>/dev/null | wc -l | tr -d ' ' || echo 0)"
  threads="$(($(ps -M -p "$pid" 2>/dev/null | wc -l) - 1))"
  logkb=$(( $(stat -f %z "$OUT/Player.log" 2>/dev/null || echo 0) / 1024 ))
  ends="$(grep -c '\[CM.Autoplay\] MATCH END' "$OUT/Player.log" 2>/dev/null || true)"
  printf '%s\t%s\t%s\t%s\t%s\t%s\n' "$(( $(date +%s) - start ))" "${rss:-0}" "$logkb" "$files" "$threads" "${ends:-0}" >> "$OUT/soak-samples.tsv"
  for _ in $(seq 30); do kill -0 "$pid" 2>/dev/null || break; sleep 1; done
done
wait "$pid"; code=$?
set -e
set +e; summarise "$OUT" "$code"; rc=$?; set -e
exit "$rc"
