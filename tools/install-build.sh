#!/usr/bin/env bash
# Install a built macOS player as the owner's current build (ci/offload-2, 2026-11-13): what integrators did by hand.
#   tools/install-build.sh PATH/TO/App.app [--dest DIR] [--expect-sha SHA]
# DEST defaults to CI_INSTALL_DIR from tools/ci/offload.conf (~/critical-mass-unity/Builds). Steps:
#   1. prepare the app (exec bits, quarantine, ad-hoc signature if invalid, stamp) with tools/ci/offload.sh prepare-mac-app;
#   2. if the game (CI_MAC_PROCESS) is running from DEST, do NOT touch the running copy: install as <App>.app.new and say so;
#   3. otherwise rotate DEST/<App>.app to <App>.app.prev (the old .prev is deleted), copy the new one in (ditto keeps
#      modes, xattrs and signatures), and verify the installed copy's signature and stamp.
# Prints one JSON line. Exit 0 installed, 2 installed as .new (game running), 1 failed, 64 usage.
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$HERE/ci/offload.conf"
SRC=''; DEST="$CI_INSTALL_DIR"; EXPECT=''
while [[ $# -gt 0 ]]; do
  case "$1" in
    --dest) DEST="${2:?}"; shift 2 ;;
    --expect-sha) EXPECT="${2:?}"; shift 2 ;;
    -h|--help) sed -n '2,10p' "$0"; exit 0 ;;
    -*) echo "install-build: unknown option $1" >&2; exit 64 ;;
    *) SRC="${1%/}"; shift ;;
  esac
done
[[ -d "$SRC/Contents/MacOS" ]] || { echo "usage: tools/install-build.sh APP.app [--dest DIR] [--expect-sha SHA]" >&2; exit 64; }
SRC="$(cd "$SRC" && pwd)"; mkdir -p "$DEST"; DEST="$(cd "$DEST" && pwd)"
NAME="$(basename "$SRC")"
out() { printf '{"tool":"install-build","ts":"%s","src":"%s","dest":"%s","status":"%s"%s}\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$SRC" "$1" "$2" "${3:-}"; exit "$4"; }
prep="$(bash "$HERE/ci/offload.sh" prepare-mac-app "$SRC" ${EXPECT:+--expect-sha "$EXPECT"})" \
  || out "$DEST/$NAME" fail ",\"reason\":\"source app not runnable\",\"prepare\":$prep" 1
# Running from DEST? (match the executable path, not just the name: other checkouts may run their own copy)
running=0
pgrep -fl "$CI_MAC_PROCESS" 2>/dev/null | grep -F "$DEST/" >/dev/null && running=1
if [[ $running -eq 1 ]]; then
  rm -rf "$DEST/$NAME.new"; ditto "$SRC" "$DEST/$NAME.new" || out "$DEST/$NAME.new" fail ',"reason":"copy failed"' 1
  out "$DEST/$NAME.new" installed-as-new ",\"reason\":\"$CI_MAC_PROCESS is running from $DEST; quit it, then in $DEST: rm -rf $NAME.prev && mv $NAME $NAME.prev && mv $NAME.new $NAME\"" 2
fi
if [[ -d "$DEST/$NAME" ]]; then rm -rf "$DEST/$NAME.prev"; mv "$DEST/$NAME" "$DEST/$NAME.prev"; fi
ditto "$SRC" "$DEST/$NAME" || { [[ -d "$DEST/$NAME.prev" ]] && mv "$DEST/$NAME.prev" "$DEST/$NAME"; out "$DEST/$NAME" fail ',"reason":"copy failed; previous build restored"' 1; }
check="$(bash "$HERE/ci/offload.sh" prepare-mac-app "$DEST/$NAME" ${EXPECT:+--expect-sha "$EXPECT"})" \
  || out "$DEST/$NAME" fail ",\"reason\":\"installed copy failed verification\",\"prepare\":$check" 1
out "$DEST/$NAME" installed ",\"previous\":\"$DEST/$NAME.prev\",\"prepare\":$check" 0
