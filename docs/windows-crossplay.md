# Windows, and Windows with Mac: what is proven, how, and what is not (b12-w, 2026-11-16)

Owner priority: the game must be playable on Windows, and Windows players must be able to play with Mac players.
This page says what the automated checks prove, how to run them, the determinism audit, and the honest limits.

## The checks

| Check | Where | What it proves |
|---|---|---|
| Windows player build | `unity-ci.yml` jobs=build-windows / builds / all (ubuntu, StandaloneWindows64, Mono) | The Windows player builds from the same commit as the Mac one |
| Windows smoke | `unity-ci.yml` job `smoke-windows` (windows-latest, `tools/windows-run.ps1`) | Pass A (required): the player starts, loads, plays one autoplay match and quits on Windows, no exceptions. Pass B (best effort): a Direct3D 11 window on WARP, screenshots `player/test-*/data-b/shot-*.png` uploaded in artifact `windows-smoke` |
| Cross-play, players | `crossplay.yml` mode `players` | The BUILT Windows player (windows-latest, x64, Mono) hosts and the BUILT macOS player (macos-latest, Apple Silicon) joins one room on the real relay, both `-batchmode -nographics` with autopilot humans; every fingerprint row (tick 0, tick 1, every 30th) must MATCH, and both sides must show the same ConfigHash, tick-0 world and build |
| Cross-play, console | `crossplay.yml` mode `console` (and `online-smoke.yml` cross_os) | The .NET 8 console peers (the game's own Sim and Net code) agree Windows-to-macOS through the real relay |
| Cross-play, mixed | `crossplay.yml` mode `mixed` | Windows player against a Linux .NET 8 console peer. Diagnostic only: Mono against .NET 8 is not the shipping pair |
| External host | `crossplay.yml` mode `mac_host_external` | A Windows-runner player joins a room the owner's Mac (or the Air) hosts; compare its uploaded `hashes.txt` with the host's `-onlineHashLog` |
| Windows zip | `crossplay.yml` job `package-windows` (macos-latest, `tools/package.sh --windows --version`) | `CriticalMass-<version>-windows.zip` + `.sha256` as a workflow artifact (never a release) |
| Culture and text | `Sim.Tests/Net/CrossPlatformDeterminismTests.cs` (no-Unity sim gate and Unity EditMode) | The same 600-tick match gives the same fingerprints every 30 ticks and the same ConfigHash under the invariant, tr-TR, de-DE and ar-SA cultures; one changed config field moves the hash; room codes and hash logs read the same with LF and CRLF; the Sim reads no text, clock or environment; C# sources stay LF on a Windows checkout |

How to run (until `crossplay.yml` is on main, GitHub can only dispatch it through `online-smoke.yml`, which is):

```
gh workflow run unity-ci.yml --ref BRANCH -f jobs=builds                      # both players, then note the run id
gh workflow run online-smoke.yml --ref BRANCH -f crossplay_mode=players -f unity_ci_run_id=RUN_ID
gh workflow run online-smoke.yml --ref BRANCH -f crossplay_mode=console
gh workflow run online-smoke.yml --ref BRANCH -f crossplay_mode=mac_host_external -f unity_ci_run_id=RUN_ID -f room_code=CODE
# once on main:  gh workflow run crossplay.yml --ref BRANCH -f mode=players -f unity_ci_run_id=RUN_ID
```

The verdict is in the `compare` job: `CROSSPLAY CHECK OK ...` (tools/online/crossplay-check.py) and
`MATCH n fingerprints agree` (tools/online/compare-hashes.py, at least ticks/30 - 2 rows).

How the two runners meet: the host job makes the room itself (POST /rooms) just before its game starts (the relay
gives a host 2 minutes to connect), uploads the code as artifact `room-<mode>`, and the joiner waits for that artifact
(`tools/online/wait-room.sh`, one check every 10 s) and then joins with retries (`-onlineJoinTries 80`).

## Why the cross-OS run of 2026-11-14 failed (run 43627866241)

A separate job made the room, and both runners started at once. The macOS joiner built faster and connected at
18:40:56, 9 s before the Windows host (18:41:05); the relay answered "There is no room with that code" (a room whose
host has not connected yet), that build of the console peer gave up on the first refusal (exit 5), and the Windows
host then waited 15 minutes for nobody (exit 4, timed out). The joiner retry (`-join-tries`, "host not ready") was
added afterwards but never run; this branch also removes the race itself (the joiner waits for the host's room).

## Version and config handshake (what happens today when different copies meet)

1. Relay door: the WebSocket URL carries `v=1` (RelayTransport.ProtocolVersion); a different relay protocol is refused.
2. Lobby door (Net/Room.cs, Hello): the joiner sends `NetVersion.Protocol` (3) and its build string; the host refuses
   any difference with "Your game is a different version from the host's (X vs Y). Both of you need the same update."
   The player's build string is `OnlineConfig.Build`: the 7-character commit it was built from (without "+dirty"), or
   the version when there is no commit. So a Windows and a Mac copy of the SAME commit (the same unity-ci run, or the
   same release) meet; copies of different commits are refused at the lobby even when the rules are identical.
3. Match start (Net/LockstepSession.cs): the start message carries the host's ConfigHash and tick-0 world fingerprint;
   a client whose rules differ refuses with "Your game's match rules differ from the host's ..." (the hidden peer exits 2).
4. `-onlineBuild`: read by HeadlessPeer.FromArgs, but the game's hidden peer (Runtime/Online/OnlineHeadless.cs)
   overwrites it with `OnlineConfig.Build`, so in the player it does nothing. In the console peer it sets the build
   string it sends (default "headless"), so a console peer meeting a player must pass the player's commit
   (`tools/online/player-stamp.py PLAYER --field netbuild`; crossplay mode `mixed` does).

New in b12-w (no wire change): both peers print one line before joining and one at match start:
`[CM.Online] build=<version> config=<ConfigHash, 16 hex> protocol=<n> platform=<os>/<arch> runtime=mono|dotnet8 netbuild=<commit> phase=join|start`.

Release rule this implies: the Mac and Windows downloads must come from ONE commit (tools/release/package-demo.sh
already refuses a Windows folder whose stamp differs from the Mac app's).

## Determinism audit

| Risk | Where | Verdict | Test or proof |
|---|---|---|---|
| Transcendental functions | Sim: sin, cos, atan2, exp, asin go through JsMath/Fdlibm (Sim/Core/Fdlibm.cs), pure C# | Safe | crossplay players MATCH (x64 Windows vs arm64 Mac) |
| Raw Math.Sqrt (about 60 calls in Sim) | Bots, Collide, Combat, Kaiju, Maps | Safe: IEEE 754 requires a correctly rounded square root on every CPU and runtime | same |
| Raw Math.Pow | Sim/Knock.cs:29 `DecayPerTick` | Dormant: only when SimHz is not 30; at the shipped 30 Hz the exponent is exactly 1 and pow is never called (the code says so) | Patch below if another SimHz ever ships |
| Math.Round / Math.Floor | Net/InputCodec.cs, Sim/Maps/SkybreakBasinMap.cs | Safe: exact operations | n/a |
| float and double mixing | Sim is double throughout; JsMath.F32 rounds to float on purpose; no float fields | Safe | ConfigHash hashes floats by their double bits |
| Fused multiply-add on arm64 vs x64 | Mono JIT (player) and RyuJIT (.NET 8) do not contract `a*b+c` into one FMA unless asked (Math.FusedMultiplyAdd: none in Sim/Net); x64 uses SSE2, not x87 | Safe, proven only by the run | crossplay players MATCH; the job prints the macOS player's slices (`lipo -archs`) and `platform=` so a Rosetta (x64) run is visible |
| Unstable sorts | Sim: none (the two "OrderBy" hits are an int[] field in DepthState/PingRules). Net: ConfigHash sorts fields by unique name, ordinal; LoopbackNetwork (tests) has a sequence tie-break | Safe | grep; ConfigHash test |
| Culture in hashed data | Sim formats text only in ShiverWatch diagnostics, with InvariantCulture; hashes print as hex | Safe | CrossPlatformDeterminismTests: 600 ticks under invariant, tr-TR, de-DE, ar-SA |
| Culture in command-line numbers | Net/Relay/HeadlessPeer.cs FromArgs: `int.TryParse` with the current culture (pace uses invariant) | Safe for determinism: ASCII digits read the same; only `-onlineSeed` could be negative and the host's seed travels in the match start. Hygiene patch below | review |
| Culture in `-onlineClass` | HeadlessPeer.FromArgs `Enum.TryParse(ignoreCase)` | Not a desync (the pick goes to the host); on Mono under tr-TR a lowercase "miner" may not match "Miner" (Turkish dotted i). Unverified on Mono | patch below (use the exact names or ToUpperInvariant first) |
| Culture in config | GameConfig is built in code (GameConfig.CreateDefault), nothing parsed | Safe | config test under four cultures |
| string.GetHashCode, Dictionary/HashSet order | Sim: none. Net: int-keyed dictionaries for chat and roster notices (not hashed), HashSet<string> lookups only, ConfigHash's reference set for cycles only (never iterated) | Safe | grep |
| Path separators | Sim does no file or path work; ConfigHash hashes no paths | Safe | the Sim text guard test |
| Line endings | The Sim reads no text; `.gitattributes` pins `*.cs text eol=lf`, so a Windows checkout with autocrlf compiles the same sources; no multi-line verbatim strings in Sim | Safe | the Sim text guard test |
| Line endings in a room code | Net/Relay/RelayTransport.cs CleanCode refuses a code with a trailing line end (LF and CRLF alike) | Not a desync; the harness strips it. Patch below so a pasted code with a newline works | room code test prints it |
| Line endings in test goldens | tools/golden under `* text=auto` become CRLF on a Windows checkout | Tests only (they run on Linux and macOS); not the Sim | n/a |
| Time, threads | Sim: no clock (SimProfile.cs counters are profiling only); stepped on one thread (online-rooms-3) | Safe | Sim text guard test |
| System.Random, Environment | Sim: none (Sim/Core/Rng.cs is the game's own seeded generator) | Safe | Sim text guard test |
| Runtime pair | Players are Mono on both platforms (the shipping pair); console peers are .NET 8 | The players mode is the real proof; console and mixed are extra | crossplay players / console / mixed |

## What this does not prove

- A GitHub Windows runner has no GPU: Direct3D 11 runs on WARP, a software renderer. Real-GPU shaders, drivers,
  frame rate, window and full-screen switching, alt-tab and DPI scaling (125 %, 150 %) on a home PC are unproven.
- SmartScreen ("Windows protected your PC") and antivirus behaviour on an unsigned exe from a zip.
- Keyboard layouts (AZERTY, QWERTZ), IME, and controllers (XInput, DualShock/DualSense) on Windows.
- Windows on ARM (the player is x64 only; on ARM it would run under emulation, untested).
- Home and corporate networks: the relay is an outbound WebSocket over HTTPS (port 443), which passes most home
  routers, but proxies, captive portals and strict firewalls are untested. Both runners sit in GitHub's data centres.
- Hidden-peer autopilot humans are not people with a mouse: no menus, no UI, no pausing, no chat typing.
- One match is one seed and one map, 1800 ticks (one minute); late-match states (Kaiju, long fights) are reached only
  in longer runs (`ticks` input, 0 = whole match).
- A Windows-to-Mac match between DIFFERENT builds is refused by design; this proves only same-build play.
- The macOS runner may run an Intel-only player under Rosetta: the job prints the player's slices; if it says only
  x86_64, the arm64 claim is not proven by that run.

## The first real Windows PC test (owner's PC script: tools/windows-run.ps1)

1. Unzip `CriticalMass-<version>-windows.zip` (Extract All, not run from inside the zip). Note what SmartScreen says
   and whether antivirus quarantines anything.
2. In PowerShell in that folder: `powershell -ExecutionPolicy Bypass -File .\windows-run.ps1` (Pass A logic, Pass B a
   real window with screenshots). Send back the zip it leaves on the Desktop (system.txt has the GPU and driver).
3. Then play by hand for ten minutes: frame rate (F3 overlay), full screen and windowed, alt-tab, a 125 % or 150 %
   display scale, the keyboard layout in use, a controller if there is one.
4. Online: the owner's Mac hosts, the Windows PC joins by code from its home network, one full match; then the
   Windows PC hosts and the Mac joins. The "MATCH STOPPED" panel must never appear. Both must be the same build
   (title screen label).

## Patches outside b12-w's files (for the integrator to route; none moves a checksum)

RelayTransport.cs (forbidden here), accept a pasted code with a trailing line end or tab:

```diff
-                if (c == ' ' || c == '-') continue;
+                if (c == ' ' || c == '-' || c == '\r' || c == '\n' || c == '\t') continue;
```

Net/Relay/HeadlessPeer.cs FromArgs, culture hygiene (integers and the class name read the same in every locale):

```diff
-                    case "onlinehumans": case "humans": int.TryParse(next, out p.Humans); i++; break;
+                    case "onlinehumans": case "humans": int.TryParse(next, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out p.Humans); i++; break;
```
(the same for ticks, teamsize, seed, timeout, lobby, join-tries, chat-at, drop-at), and for `-onlineClass` match the
names with `string.Equals(name, next, StringComparison.OrdinalIgnoreCase)` over `Enum.GetNames(typeof(ClassId))`
instead of `Enum.TryParse(next, true, ...)`.

Sim/Knock.cs, only if a SimHz other than 30 is ever shipped (a rule-change merge; it moves checksums at that SimHz only):
replace `Math.Pow(c.Physics.KnockDecayPer30Hz, e)` with a pure C# power (for example `Fdlibm.Exp(e * Fdlibm.Log(x))`
if Fdlibm gains a log), so Windows and macOS C libraries cannot differ in the last bit.
