#!/usr/bin/env bash
# DG-57: build the owner's playable Mac copy without ever touching the one he is playing.
#   tools/play-latest.sh [--keep N] [--prune-only] [--no-build]
# Builds HEAD into Builds/staging/<stamp>/CriticalMass.app (stamp = UTC time + short commit, -dirty when tracked files
# changed), verifies it (executable present, codesign --verify --strict, Gatekeeper assessment recorded), then moves the
# whole folder with ONE atomic rename to Builds/releases/<stamp>/ and repoints the Builds/current link (atomic replace).
# Keeps the newest N releases (default 5) and never deletes or rewrites a release whose app is running. Prints the app
# path as the last line. Builds/macOS/CriticalMass.app (the build-mac gate's output) is never touched.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KEEP=5; PRUNE_ONLY=0; NO_BUILD=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --keep) KEEP="${2:?}"; shift 2 ;;
    --prune-only) PRUNE_ONLY=1; shift ;;
    --no-build) NO_BUILD=1; shift ;;
    --force) shift ;;   # kept for old callers: every build is a new release folder now
    *) echo "usage: play-latest.sh [--keep N] [--prune-only] [--no-build]" >&2; exit 64 ;;
  esac
done
B="$ROOT/Builds"; REL="$B/releases"; STG="$B/staging"
mkdir -p "$REL" "$STG"

running() { pgrep -f "$1/CriticalMass.app/Contents/MacOS/" >/dev/null 2>&1; }   # read only, never kills

prune() {
  local n=0 d
  while IFS= read -r d; do
    [[ -d "$d" ]] || continue
    n=$((n + 1))
    if (( n > KEEP )); then
      if running "$d"; then echo "play-latest: keeping $d (running)" >&2
      elif [[ "$(readlink "$B/current" 2>/dev/null)" == "$d" ]]; then echo "play-latest: keeping $d (current)" >&2
      else rm -rf "$d"; echo "play-latest: removed $d" >&2; fi
    fi
  done < <(ls -1d "$REL"/*/ 2>/dev/null | sed 's:/$::' | sort -r)
  # staging leftovers of interrupted builds (never a running one)
  for d in "$STG"/*/; do [[ -d "$d" ]] && ! running "${d%/}" && rm -rf "${d%/}"; done 2>/dev/null || true
}

point_current() {  # atomic: a new link renamed over the old one
  python3 -c 'import os,sys; t=sys.argv[2]+".new"; (os.path.lexists(t) and os.remove(t)); os.symlink(sys.argv[1], t); os.replace(t, sys.argv[2])' "$1" "$B/current"
}

if (( PRUNE_ONLY )); then
  prune
  [[ -e "$B/current/CriticalMass.app" ]] || { echo "play-latest: no release yet" >&2; exit 1; }
  echo "$B/current/CriticalMass.app"; exit 0
fi
(( NO_BUILD )) && { echo "$B/current/CriticalMass.app"; exit 0; }
if pgrep -f "Unity.app/Contents/MacOS/Unity.*-projectPath $ROOT( |$)" >/dev/null 2>&1; then
  echo "play-latest: refused, a Unity editor already has $ROOT open" >&2; exit 3
fi
short="$(git -C "$ROOT" rev-parse --short=7 HEAD)"
[[ -z "$(git -C "$ROOT" status --porcelain --untracked-files=no)" ]] || short="$short-dirty"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)-$short"
STAGE="$STG/$STAMP"
mkdir -p "$STAGE"
trap 'rm -rf "$STAGE"' EXIT
HEAVY=(); [[ -x "$HOME/bin/cm-heavy" ]] && HEAVY=("$HOME/bin/cm-heavy" unity)
CM_BUILD_LOG="$ROOT/Logs/play-latest-build.log" ${HEAVY[@]+"${HEAVY[@]}"} "$ROOT/tools/unity-build-mac.sh" --output "$STAGE/CriticalMass.app" >&2
APP="$STAGE/CriticalMass.app"
EXE="$APP/Contents/MacOS/$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$APP/Contents/Info.plist")"
[[ -x "$EXE" ]] || { echo "play-latest: FAILED, no executable in the staged app" >&2; exit 1; }
{
  echo "codesign: $(codesign --verify --strict "$APP" 2>&1 && echo ok)"
  echo "spctl: $(spctl -a -vv "$APP" 2>&1 | tr '\n' ' ')"
  echo "size_mb: $(du -sm "$APP" | awk '{print $1}')"
} > "$STAGE/verify.txt" 2>&1 || true
[[ -e "$REL/$STAMP" ]] && { echo "play-latest: refused, $REL/$STAMP exists" >&2; exit 75; }
mv "$STAGE" "$REL/$STAMP"     # one rename; a running release is never rewritten (every build is a new folder)
trap - EXIT
point_current "$REL/$STAMP"
prune
echo "$B/current/CriticalMass.app"
