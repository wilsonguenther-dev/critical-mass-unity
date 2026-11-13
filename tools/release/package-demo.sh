#!/usr/bin/env bash
# Package a signed demo of an existing player build for testers: a Developer-ID-signed (and, when a notary
# credential works, notarised and stapled) Mac DMG, plus a Windows zip with a READ-ME, optionally published as a
# GitHub pre-release. Never touches the source .app: everything happens on copies under --scratch.
#
#   tools/release/package-demo.sh --app PATH.app --version 1.0.365 --commit a81efe4 [--windows-dir DIR]
#       [--scratch DIR] [--identity "Developer ID Application: NAME (TEAM)"] [--notary-profile NAME]
#       [--notary-key PATH.p8 --notary-key-id ID [--notary-issuer UUID]] [--publish OWNER/REPO]
#
# Steps (see tools/release/README.md for the why of each):
#   1. ditto the .app into scratch as "Critical Mass.app", clear xattrs, make it world-readable.
#   2. Verify the build stamp inside Data/resources.assets matches --version and --commit (dirty=0, development=0).
#   3. Sign inside-out: every Mach-O under Contents/Frameworks and Contents/PlugIns first (hardened runtime +
#      timestamp, no entitlements), then the app with tools/release/CriticalMass.entitlements. No --deep.
#   4. codesign --verify --deep --strict; spctl -a -vv (expected "Unnotarized Developer ID" until stapled).
#   5. DMG: hdiutil UDZO, volume "Critical Mass Demo", the app plus an /Applications symlink; sign the DMG.
#   6. Notarise (only when a credential is given AND works): notarytool submit --wait; on Invalid, print
#      notarytool log; staple; spctl -a -t open --context context:primary-signature -vv.
#   7. Windows: copy DIR minus *DoNotShip* folders, require CriticalMass.exe, *_Data and UnityPlayer.dll, add
#      READ-ME.txt, zip -9 -X as CriticalMass-Windows-<version>.zip.
#   8. SHA256SUMS.txt. With --publish: gh release create demo-v<version> --prerelease --target <full sha>.
# Credentials are passed by reference only (keychain profile name or key file path); nothing secret is printed.
# Exit: 0 ok, 1 a check failed, 2 notarisation was requested and failed, 64 usage.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; ROOT="$(cd "$HERE/../.." && pwd)"
APP=""; VERSION=""; COMMIT=""; WINDIR=""; SCRATCH="$HOME/cm-release-scratch"
IDENTITY="Developer ID Application: Wilson Guenther (VHYV8C2JNU)"
NPROFILE=""; NKEY=""; NKEYID=""; NISSUER=""; PUBLISH=""
usage() { sed -n '6,8p' "${BASH_SOURCE[0]}" | sed 's/^# *//' >&2; exit 64; }
while [[ $# -gt 0 ]]; do
  case "$1" in
    --app) APP="${2:?}"; shift 2 ;; --version) VERSION="${2:?}"; shift 2 ;; --commit) COMMIT="${2:?}"; shift 2 ;;
    --windows-dir) WINDIR="${2:?}"; shift 2 ;; --scratch) SCRATCH="${2:?}"; shift 2 ;;
    --identity) IDENTITY="${2:?}"; shift 2 ;; --notary-profile) NPROFILE="${2:?}"; shift 2 ;;
    --notary-key) NKEY="${2:?}"; shift 2 ;; --notary-key-id) NKEYID="${2:?}"; shift 2 ;;
    --notary-issuer) NISSUER="${2:?}"; shift 2 ;; --publish) PUBLISH="${2:?}"; shift 2 ;;
    *) usage ;;
  esac
done
[[ -d "$APP" && -n "$VERSION" && -n "$COMMIT" ]] || usage
ENT="$HERE/CriticalMass.entitlements"; OUT="$SCRATCH/out"; STAGE="$SCRATCH/stage"
DMG="$OUT/CriticalMass-Mac-$VERSION.dmg"; ZIP="$OUT/CriticalMass-Windows-$VERSION.zip"
mkdir -p "$OUT" "$STAGE"
say() { printf '== %s\n' "$*"; }

# 1-2. copy and check the stamp
SAPP="$STAGE/Critical Mass.app"; rm -rf "$SAPP"; ditto "$APP" "$SAPP"; xattr -cr "$SAPP"; chmod -R a+rX "$SAPP"
stamp() { python3 -I -c 'import re,sys
d=open(sys.argv[1],"rb").read(); m=re.search(rb"version=([^\n]*)\ncommit=([^\n]*)\ndirty=([^\n]*)\n(?:[^\n]*\n)*?development=([^\n]*)",d)
print(" ".join(x.decode() for x in m.groups()) if m else "none")' "$1"; }
S="$(stamp "$SAPP/Contents/Resources/Data/resources.assets")"; say "mac stamp: $S"
[[ "$S" == "$VERSION $COMMIT 0 0" ]] || { echo "stamp mismatch (want $VERSION $COMMIT 0 0)" >&2; exit 1; }

# 3-4. sign inside-out
NESTED=(); while IFS= read -r -d '' f; do NESTED+=("$f"); done < <(python3 -I -c 'import os,sys
M={b"\xcf\xfa\xed\xfe",b"\xca\xfe\xba\xbe",b"\xce\xfa\xed\xfe",b"\xbe\xba\xfe\xca"}
root=sys.argv[1]
for sub in ("Contents/Frameworks","Contents/PlugIns","Contents/Resources"):
    for r,_,fs in os.walk(os.path.join(root,sub)):
        for n in fs:
            p=os.path.join(r,n)
            if not os.path.islink(p) and open(p,"rb").read(4) in M: sys.stdout.write(p+"\0")' "$SAPP")
say "signing ${#NESTED[@]} nested Mach-O files, then the app"
codesign --force --timestamp --options runtime --sign "$IDENTITY" "${NESTED[@]}"
codesign --force --timestamp --options runtime --entitlements "$ENT" --sign "$IDENTITY" "$SAPP"
codesign --verify --deep --strict --verbose=2 "$SAPP"
codesign -dvv "$SAPP" 2>&1 | grep -E '^(Authority|TeamIdentifier|Timestamp)|flags=' || true
spctl -a -vv "$SAPP" 2>&1 || true   # rejected "Unnotarized Developer ID" is expected before stapling

# 5. DMG
ROOTD="$SCRATCH/dmgroot"; rm -rf "$ROOTD"; mkdir -p "$ROOTD"; ditto "$SAPP" "$ROOTD/Critical Mass.app"
ln -s /Applications "$ROOTD/Applications"
hdiutil create -volname "Critical Mass Demo" -srcfolder "$ROOTD" -ov -format UDZO -imagekey zlib-level=9 -fs HFS+ "$DMG"
rm -rf "$ROOTD"; codesign --force --timestamp --sign "$IDENTITY" "$DMG"

# 6. notarise when a credential is given and accepted
NOTARISED=0; NARGS=()
if [[ -n "$NPROFILE" ]]; then NARGS=(--keychain-profile "$NPROFILE")
elif [[ -n "$NKEY" ]]; then NARGS=(--key "$NKEY" --key-id "$NKEYID"); [[ -n "$NISSUER" ]] && NARGS+=(--issuer "$NISSUER"); fi
if [[ ${#NARGS[@]} -gt 0 ]]; then
  if xcrun notarytool history "${NARGS[@]}" >/dev/null 2>&1; then
    R="$(xcrun notarytool submit "$DMG" "${NARGS[@]}" --wait --output-format json)"; echo "$R"
    ID="$(python3 -I -c 'import json,sys; print(json.loads(sys.argv[1]).get("id",""))' "$R")"
    if [[ "$R" == *'"Accepted"'* ]]; then xcrun stapler staple "$DMG"; xcrun stapler validate "$DMG"; NOTARISED=1
    else xcrun notarytool log "$ID" "${NARGS[@]}" || true; echo "notarisation failed: read the log above" >&2; exit 2; fi
  else
    echo "notary credential refused by Apple (401?): DMG stays Developer-ID signed, not notarised" >&2
    echo "fix once, outside any sandbox: xcrun notarytool store-credentials <profile> --apple-id <id> --team-id <team>" >&2
  fi
fi
spctl -a -t open --context context:primary-signature -vv "$DMG" 2>&1 || true

# 7. Windows zip
if [[ -n "$WINDIR" ]]; then
  WS="$SCRATCH/win/CriticalMass-Windows-$VERSION"; rm -rf "$WS"; mkdir -p "$WS"
  rsync -a --exclude '*DoNotShip*' --exclude '*ButDontShip*' "$WINDIR"/ "$WS"/
  [[ -f "$WS/CriticalMass.exe" && -f "$WS/UnityPlayer.dll" ]] && compgen -G "$WS/*_Data" >/dev/null || { echo "windows build incomplete" >&2; exit 1; }
  WSTAMP="$(stamp "$WS/CriticalMass_Data/resources.assets")"; say "windows stamp: $WSTAMP"
  [[ "$WSTAMP" == "$VERSION $COMMIT 0 0" ]] || { echo "windows stamp mismatch" >&2; exit 1; }
  sed "s/@VERSION@/$VERSION/" "$HERE/READ-ME-windows.txt" > "$WS/READ-ME.txt"
  rm -f "$ZIP"; (cd "$SCRATCH/win" && zip -r -q -9 -X "$ZIP" "CriticalMass-Windows-$VERSION")
fi

# 8. checksums and optional publish
(cd "$OUT" && shasum -a 256 ./*.dmg ./*.zip 2>/dev/null | sed 's| \./| |' > SHA256SUMS.txt; cat SHA256SUMS.txt)
if [[ -n "$PUBLISH" ]]; then
  FULL="$(git -C "$ROOT" rev-parse "$COMMIT^{commit}")"
  NOTES="$SCRATCH/release-notes.md"; [[ -f "$NOTES" ]] || { echo "write $NOTES first (plain words, no tool names)" >&2; exit 1; }
  ASSETS=("$DMG"); [[ -f "$ZIP" ]] && ASSETS+=("$ZIP"); ASSETS+=("$OUT/SHA256SUMS.txt")
  gh release create "demo-v$VERSION" -R "$PUBLISH" --prerelease --target "$FULL" \
    --title "Critical Mass demo build $VERSION (work in progress)" --notes-file "$NOTES" "${ASSETS[@]}"
fi
say "done: notarised=$NOTARISED dmg=$DMG"
