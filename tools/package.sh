#!/usr/bin/env bash
# DG-57: package a release for testers. Zips the .app only (ditto -c -k --keepParent; Windows: the player files only),
# leaving out every *DoNotShip* / *ButDontShip* folder, adds FIRST-RUN.txt (with the streamer note and where the game
# keeps its files) and writes a SHA-256 checksum. Records the Gatekeeper assessment of a quarantined copy.
#   tools/package.sh [--mac PATH.app] [--windows DIR] [--out DIR]
# Defaults: --mac Builds/current/CriticalMass.app, out Builds/packages/<stamp>. Exit 1 when a do-not-ship folder slipped in
# or the installed size is over 500 MB.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MAC="$ROOT/Builds/current/CriticalMass.app"; WIN=""; OUT="$ROOT/Builds/packages/$(date -u +%Y%m%dT%H%M%SZ)"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --mac) MAC="${2:?}"; shift 2 ;; --windows) WIN="${2:?}"; shift 2 ;; --out) OUT="${2:?}"; shift 2 ;;
    *) echo "usage: package.sh [--mac PATH.app] [--windows DIR] [--out DIR]" >&2; exit 64 ;;
  esac
done
mkdir -p "$OUT"
cat > "$OUT/FIRST-RUN.txt" <<'TXT'
Critical Mass: first run

Mac: the game is not signed by Apple yet. The first time, double-click it, close the warning, then open System Settings,
Privacy and Security, scroll down and click "Open Anyway". Move Critical Mass into Applications first so it stays stable.
Windows: unzip the whole folder, run CriticalMass.exe. If Windows shows "Windows protected your PC", click "More info",
then "Run anyway".

Streaming: capture the game with "Game capture" or "Window capture", not "Display capture" (on laptops with two graphics
chips display capture can show black). Clicking away to chat pauses the match; the picture and sound keep going.

Where the game keeps its files (settings, logs, problem reports):
  Mac: ~/Library/Application Support/com.drivia.criticalmass/ (logs in its logs/ folder);
       settings in ~/Library/Preferences/com.drivia.criticalmass.plist; Unity log in ~/Library/Logs/Drivia/Critical Mass/
  Windows: %USERPROFILE%\AppData\LocalLow\Drivia\Critical Mass\ (Player.log is there too)
Something wrong? Settings, Game, "Save a problem report" puts a zip on your Desktop. Send us that zip.
TXT
fail=0
pack() {  # $1 source dir parent, $2 item, $3 zip
  local stage; stage="$(mktemp -d)"
  rsync -a --exclude '*DoNotShip*' --exclude '*ButDontShip*' "$1/$2" "$stage/"
  cp "$OUT/FIRST-RUN.txt" "$stage/"
  [[ "$3" == *windows* ]] && cp "$ROOT/tools/windows-run.ps1" "$stage/$2/"
  (cd "$stage" && ditto -c -k --keepParent "$2" "$3.tmp.zip" && zip -q "$3.tmp.zip" FIRST-RUN.txt && mv "$3.tmp.zip" "$3")
  local mb; mb="$(du -sm "$stage/$2" | awk '{print $1}')"
  rm -rf "$stage"
  (cd "$(dirname "$3")" && shasum -a 256 "$(basename "$3")" > "$(basename "$3").sha256")
  local bad; bad="$(unzip -l "$3" | grep -ciE 'DoNotShip|ButDontShip' || true)"
  echo "package: $3 installed ${mb} MB, do-not-ship entries ${bad}"
  [[ "$bad" == 0 ]] || fail=1
  (( mb <= 500 )) || { echo "package: over the 500 MB installed-size budget" >&2; fail=1; }
}
if [[ -d "$MAC" ]]; then
  pack "$(dirname "$MAC")" "$(basename "$MAC")" "$OUT/CriticalMass-mac.zip"
  echo "codesign: $(codesign --verify --strict "$MAC" 2>&1 && echo ok)" | tee "$OUT/gatekeeper.txt"
  q="$(mktemp -d)"; ditto "$MAC" "$q/CriticalMass.app"; xattr -w com.apple.quarantine "0081;$(printf %x "$(date +%s)");package.sh;" "$q/CriticalMass.app" || true
  echo "spctl (quarantined copy): $(spctl -a -vv "$q/CriticalMass.app" 2>&1 | tr '\n' ' ')" | tee -a "$OUT/gatekeeper.txt"
  rm -rf "$q"
fi
if [[ -n "$WIN" && -d "$WIN" ]]; then
  pack "$(dirname "$WIN")" "$(basename "$WIN")" "$OUT/CriticalMass-windows.zip"
fi
echo "$OUT"
exit $fail
