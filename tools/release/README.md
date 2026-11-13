# tools/release: a signed demo download for testers

`package-demo.sh` turns an existing player build (the .app this Mac built, plus the Windows player folder from CI)
into two downloads a tester double-clicks, and can publish them as a GitHub pre-release. It never opens Unity, never
launches the game, and never touches the source .app: it works on copies under `--scratch` (default
`~/cm-release-scratch`; delete it afterwards).

```bash
# Windows player from CI (same commit as the Mac build):
gh run download <run-id> -R wilsonguenther-dev/critical-mass-unity -n player-StandaloneWindows64 -D /tmp/w
mkdir /tmp/w/x && tar -xzf /tmp/w/player-StandaloneWindows64.tgz -C /tmp/w/x

tools/release/package-demo.sh --app Builds/macOS/CriticalMass.app --version 1.0.365 --commit a81efe4 \
  --windows-dir /tmp/w/x --notary-profile yap-notary
# write ~/cm-release-scratch/release-notes.md (plain words), then re-run with --publish wilsonguenther-dev/critical-mass-unity
```

## What it does, and why

| Step | Command | Why |
| --- | --- | --- |
| Stamp check | reads `version= commit= dirty= development=` from `Data/resources.assets` | refuses to ship a build that is not the one named |
| Sign nested code first | `codesign --force --timestamp --options runtime` on each Mach-O in Frameworks and PlugIns | Apple's "Creating distribution-signed code for macOS" (developer.apple.com/forums/thread/128166): sign inside-out, never rely on `--deep`; Unity's page warns `--deep` applies one set of options to everything |
| Sign the app | same, plus `--entitlements tools/release/CriticalMass.entitlements` | hardened runtime is required for notarisation |
| Verify | `codesign --verify --deep --strict`, `spctl -a -vv` | before notarisation spctl says "Unnotarized Developer ID"; that is expected |
| DMG | `hdiutil create -format UDZO -volname "Critical Mass Demo"` with an `/Applications` symlink, then sign the DMG | drag-to-install; a signed DMG carries the notarisation ticket |
| Notarise | `xcrun notarytool submit --wait` with a keychain profile or an API key file, then `stapler staple` | only when the credential is accepted (`notarytool history` is tried first); on Invalid it prints `notarytool log` |
| Windows | copies the player without `*DoNotShip*`, requires `CriticalMass.exe`, `*_Data`, `UnityPlayer.dll`, adds `READ-ME.txt`, `zip -9 -X` | the do-not-ship folder holds debug symbols |
| Publish | `gh release create demo-v<version> --prerelease --target <full sha>` | a pre-release on a public repo is downloadable by anyone with the link |

## Entitlements (CriticalMass.entitlements)

`com.apple.security.cs.allow-jit`, `com.apple.security.cs.allow-unsigned-executable-memory`,
`com.apple.security.cs.disable-library-validation`. The Mono scripting backend generates code at run time (JIT and
writable-executable pages), and the player loads its own dylibs and the Burst bundle. Unity's manual page "Code sign
your application" (docs.unity.com, Unity 6.3, platform-specific/apple-mac/macos-delivery/macos-building-notarization/
macoscodesigning) lists `disable-library-validation` plus `disable-executable-page-protection` as its minimum;
`allow-unsigned-executable-memory` is the narrower Apple entitlement for the same need (Apple: Hardened Runtime,
developer.apple.com/documentation/security/hardened-runtime). If a signed player ever fails at Mono start-up, add
`disable-executable-page-protection` as Unity suggests and re-sign. Proof that a signed build still plays: copy it to
the second Mac and run `tools/remote-playtest.sh --mode smoke` (0 exceptions expected).

## Notarisation credentials

Credentials are only ever passed by reference: `--notary-profile NAME` (a profile stored with
`xcrun notarytool store-credentials`) or `--notary-key PATH.p8 --notary-key-id ID [--notary-issuer UUID]`. Nothing
secret is printed or copied. If Apple refuses the credential (HTTP 401), the script still produces a
Developer-ID-signed DMG; testers then open it once via right-click Open, or System Settings, Privacy and Security,
Open Anyway. To fix it once, the owner runs, in a normal Terminal (the agent sandbox cannot write the keychain):

```bash
xcrun notarytool store-credentials yap-notary --apple-id <apple id> --team-id VHYV8C2JNU
```

and enters a fresh app-specific password from appleid.apple.com when asked.
