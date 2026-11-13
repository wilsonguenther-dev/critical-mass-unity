#!/usr/bin/env bash
# DG-05: refresh the owner's playable Mac copy without touching a running one. Builds this checkout's HEAD into
# Builds/macOS/<short-commit>/CriticalMass.app (suffix -dirty when tracked files have local changes), keeps the newest
# two versioned folders (never deleting one whose app is running), and prints the app path as the last line.
#   tools/play-latest.sh [--force] [--keep N] [--prune-only]
#   --force       rebuild even when this commit's folder already exists (refused while that copy is running)
#   --keep N      versioned folders to keep, default 2
#   --prune-only  no build; only prune and print the newest app
# The legacy Builds/macOS/CriticalMass.app (the build-mac gate's output) is never touched.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FORCE=0; KEEP=2; PRUNE_ONLY=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --force) FORCE=1; shift ;;
    --keep) KEEP="${2:?}"; shift 2 ;;
    --prune-only) PRUNE_ONLY=1; shift ;;
    *) echo "usage: play-latest.sh [--force] [--keep N] [--prune-only]" >&2; exit 64 ;;
  esac
done
BASE="$ROOT/Builds/macOS"
mkdir -p "$BASE"

running() {  # $1 = a versioned folder; true when a process runs the app inside it (read only, never kills)
  pgrep -f "$1/CriticalMass.app/Contents/MacOS/" >/dev/null 2>&1
}

prune() {
  local n=0 d
  # newest first by folder modification time; only folders named like a short commit
  while IFS= read -r d; do
    [[ -d "$d/CriticalMass.app" ]] || continue
    n=$((n + 1))
    if (( n > KEEP )); then
      if running "$d"; then echo "play-latest: keeping $d (running)" >&2; else rm -rf "$d"; echo "play-latest: removed $d" >&2; fi
    fi
  done < <(ls -1dt "$BASE"/*/ 2>/dev/null | sed 's:/$::' | grep -E '/[0-9a-f]{7,12}(-dirty)?$' || true)
}

if (( PRUNE_ONLY )); then
  prune
  newest="$(ls -1dt "$BASE"/*/ 2>/dev/null | sed 's:/$::' | grep -E '/[0-9a-f]{7,12}(-dirty)?$' | head -1 || true)"
  [[ -n "$newest" ]] || { echo "play-latest: no versioned build yet" >&2; exit 1; }
  echo "$newest/CriticalMass.app"
  exit 0
fi

short="$(git -C "$ROOT" rev-parse --short=7 HEAD)"
[[ -z "$(git -C "$ROOT" status --porcelain --untracked-files=no)" ]] || short="$short-dirty"
DEST="$BASE/$short"
if [[ -d "$DEST/CriticalMass.app" && $FORCE -eq 0 ]]; then
  echo "play-latest: $short already built" >&2
  touch "$DEST"; prune
  echo "$DEST/CriticalMass.app"; exit 0
fi
if [[ -d "$DEST" ]] && running "$DEST"; then
  echo "play-latest: refused, $DEST/CriticalMass.app is running; quit it or build another commit" >&2; exit 75
fi
if pgrep -f "Unity.app/Contents/MacOS/Unity.*-projectPath $ROOT( |$)" >/dev/null 2>&1; then
  echo "play-latest: refused, a Unity editor already has $ROOT open" >&2; exit 3
fi

STAGE="$BASE/.staging-$short-$$"
trap 'rm -rf "$STAGE"' EXIT
# One graphics-heavy job machine-wide (2026-11-12 watchdog restart): the build waits for ~/bin/cm-heavy's Unity slot.
HEAVY=()
[[ -x "$HOME/bin/cm-heavy" ]] && HEAVY=("$HOME/bin/cm-heavy" unity)
CM_BUILD_LOG="$ROOT/Logs/play-latest-build.log" ${HEAVY[@]+"${HEAVY[@]}"} "$ROOT/tools/unity-build-mac.sh" --output "$STAGE/CriticalMass.app" >&2
rm -rf "$DEST"
mv "$STAGE" "$DEST"
trap - EXIT
prune
echo "$DEST/CriticalMass.app"
