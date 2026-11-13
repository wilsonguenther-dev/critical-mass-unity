#!/usr/bin/env bash
# DG-66: the same sim result on Apple silicon, on Intel (Rosetta) and on Windows. Runs the built Mac player's sim-only
# hash mode (no graphics, no game view: -batchmode -nographics -seed N -simHashOut FILE) natively and under
# `arch -x86_64`, and compares with a Windows hash file when given. No graphics: never takes the graphics slot.
#   tools/sim-hash.sh [--app PATH.app] [--seed N] [--out DIR] [--windows FILE]
# Exit 0 when every hash file is identical (diff), 1 otherwise, 66 without the app.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$ROOT/Builds/current/CriticalMass.app"; SEED=1337; OUT="$ROOT/Logs/sim-hash/$(date -u +%Y%m%dT%H%M%SZ)"; WIN=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --app) APP="${2:?}"; shift 2 ;; --seed) SEED="${2:?}"; shift 2 ;; --out) OUT="${2:?}"; shift 2 ;;
    --windows) WIN="${2:?}"; shift 2 ;;
    *) echo "usage: sim-hash.sh [--app PATH.app] [--seed N] [--out DIR] [--windows FILE]" >&2; exit 64 ;;
  esac
done
[[ -d "$APP" ]] || { echo "sim-hash: no app at $APP" >&2; exit 66; }
EXE="$APP/Contents/MacOS/$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$APP/Contents/Info.plist")"
mkdir -p "$OUT"
run() {  # $1 label, rest: prefix command
  local label="$1"; shift
  "$@" "$EXE" -batchmode -nographics -seed "$SEED" -simHashOut "$OUT/hash-$label.txt" -dataDir "$OUT/data-$label" \
    -logFile "$OUT/Player-$label.log" || { echo "sim-hash: $label run failed (exit $?)" >&2; return 1; }
}
run arm64 env
if /usr/bin/arch -x86_64 /usr/bin/true 2>/dev/null; then run x86_64 /usr/bin/arch -x86_64; else echo "sim-hash: Rosetta not installed; Intel run skipped" >&2; fi
[[ -n "$WIN" ]] && cp "$WIN" "$OUT/hash-windows.txt"
ref="$OUT/hash-arm64.txt"; ok=0
for f in "$OUT"/hash-*.txt; do
  if ! diff -q "$ref" "$f" >/dev/null; then echo "sim-hash: MISMATCH $f"; diff "$ref" "$f" || true; ok=1; fi
done
cat "$OUT"/hash-*.txt
exit $ok
