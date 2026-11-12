# Critical Mass (Unity): architecture

Written 2026-11-12T03:08:23Z (date -u). Every rule here comes from the architect's decisions (`U-###`, see
[DECISIONS.md](DECISIONS.md)) or from the source recon of the browser demo. Nothing here is a new decision. Where a
document needs an answer nobody has given yet, it says **OPEN:** and the question waits for the architect.

The browser demo ([wilsonguenther-dev/critical-mass](https://github.com/wilsonguenther-dev/critical-mass)) is frozen.
It is the spec and the test oracle for this repo (U-001). A **test oracle** is a trusted source of correct answers: the
browser's tests say what the right number is, and the Unity code must produce the same number.

---

## 1. The three parts: Sim, Runtime, Editor

The game is split into three C# assemblies. An **assembly definition** (`.asmdef` file) tells Unity to compile a folder
into its own DLL with an explicit list of what it may reference; Unity refuses to compile a reference that is not on
the list, so the split is enforced by the compiler, not by good manners.

| Assembly | Folder | References | Job |
| --- | --- | --- | --- |
| `CriticalMass.Sim` | `Assets/_Game/Sim` | nothing (`noEngineReferences: true`) | The game rules. Pure C#, no UnityEngine. |
| `CriticalMass.Sim.Tests` | `Assets/_Game/Sim.Tests` | Sim, NUnit, Unity Test Runner (Editor only) | The ported browser tests: the oracle. |
| `CriticalMass.Runtime` | `Assets/_Game/Runtime` | Sim | View and input only. Never changes game state. |
| `CriticalMass.Editor` | `Assets/_Game/Editor` | Sim, Runtime, URP (Editor only) | Build scripts, project setup, the parity capture script. Never ships. |

**Why this split (U-002).** The rules must give the same answer every run so the browser tests can be ported as an
oracle, so bots can play thousands of headless soak matches, and so a replay or a bug report reproduces. Keeping
UnityEngine out of the rules makes that a compiler guarantee: nothing in the Sim can read the frame time, Unity
physics or `UnityEngine.Random`.

**The Sim (`CriticalMass.Sim`).**
- **Deterministic**: the same starting state and the same inputs always give exactly the same result, tick for tick.
- **Fixed tick**: the Sim advances in equal steps of 1/30 s (30 Hz, the browser's `simHz`), no matter how fast the
  screen draws. One step is one **tick**.
- **Seeded RNG**: all randomness comes from one random number generator started from a known number (the seed, default
  1337), ported from the browser's `sim/rng.ts`. Same seed, same match.
- Numbers are `double`. State is plain classes and structs.
- Ported file by file from the browser's `src/sim` and `src/bots`. Each vitest file is ported to **NUnit** (the C# test
  framework Unity's Test Framework runs) in `Sim.Tests`, one `[Test]` per vitest `it(...)`.
- Oracle tolerance: ticks, counts and outcomes must match the browser tests exactly; positions within 1e-4, because
  the browser stored collision positions as 32-bit floats (D-012) and the Sim uses `double`.
- Bots are part of the Sim: they think inside the tick and write their own input frames (the browser's
  `bots/index.ts fillBotInput`), so a bot-only match is fully deterministic and runs headless.
- One `GameConfig` class holds every tunable, ported 1:1 from `src/config/game.config.ts` (F-01). Provisional numbers
  are marked provisional in `GameConfig` (U-007: the CM-58 Builder numbers).

**The Runtime (`CriticalMass.Runtime`).** MonoBehaviours that read Sim state and draw it, and that turn player input
into Sim commands. It never writes game state. A **MonoBehaviour** is a Unity script attached to a scene object that
Unity calls every frame.

**The Editor (`CriticalMass.Editor`).** Editor-only scripts: `Build.MacOS` / `Build.Windows` (command-line player
builds), `ProjectSetup.Apply` (re-applies project settings and URP assets), and later the parity capture script run by
`tools/unity-capture.sh` (see [PARITY.md](PARITY.md)).

**OPEN:** where the PlayMode smoke test required by U-011 lives. It needs its own test assembly, because
`CriticalMass.Sim.Tests` is EditMode-only and cannot reference the Runtime. (**EditMode** tests run in the editor
without entering play; **PlayMode** tests boot scenes and run frames.)

## 2. Data flow: input to command to state to view

```
 keyboard / mouse / gamepad
        |
        v
 Controls.inputactions  (Unity Input System asset, one fixed binding table, U-006)
        |
        v
 Runtime input layer (F-07): reads actions, writes one InputFrame for the human actor
        |
        v
 Sim driver (F-08, in Runtime): 30 Hz accumulator; each due tick calls Sim Step once
        |                       (at 2x pace it runs two ticks per frame slot, CM-66)
        v
 Sim Step (CriticalMass.Sim): bots fill their own InputFrames, then every rule system runs in fixed order
        |
        v
 Sim state (World, MatchState) + SimEvents for that tick
        |
        v
 Runtime views: read the previous and current tick, interpolate, draw; play effects and sounds from SimEvents
```

- An **InputFrame** is the per-actor, per-tick list of what that actor wants to do (move direction, jump, attack,
  ability, interact, dig/place). The browser type is `InputFrame` in `sim/types.ts`; `sim/input.ts` applies it.
- The **accumulator** collects real elapsed time and spends it in whole 1/30 s ticks, so the Sim runs at 30 Hz whatever
  the frame rate.
- **Interpolation**: the view draws each object part way between where it was on the last tick and where it is on the
  current tick, so motion looks smooth at 60 fps or more while the rules run at 30 Hz.
- Events flow one way. The view may read anything from the Sim; it may only write an InputFrame (and match options
  such as team size and pace before a match starts).

## 3. The ground grid (U-003)

- The ground is a **grid of cells**, each with a type and a height, owned by the Sim. One world unit is one cell (the
  browser's unit). Cell types come from the browser's `sim/types.ts` (`CELL`, `CellType`): walkable floor, bridge,
  void, water, wall and the rest of that list.
- Digging, placing ground (CM-30), knock-down structures (CM-31), Builder pieces (CM-58) and TNT craters (CM-38) are
  all edits to this grid. That is why the grid lives in the Sim: those are rules, not decoration.
- The Runtime rebuilds **chunk meshes** from the grid: the map is cut into square blocks of cells, each block is one
  mesh, and only the blocks whose cells changed are rebuilt.
- Bots path on the ported **flow field** (`sim/flowfield.ts`, `bots/fields.ts`): for a target such as the hill, every
  cell stores the direction to step toward it, computed once for the whole grid, so 31 bots can follow it without each
  running its own path search. A grid edit marks the affected fields stale and they rebuild (the browser rebuilds them
  staggered across ticks).
- No NavMesh. No Unity physics in rules. Collision is the ported circle-against-grid code (`sim/collide.ts`) plus the
  spatial hash broadphase (`sim/spatialHash.ts`).
- The map is Skybreak Basin, ported from `sim/map.ts` (F-03). Maps B and C are CM-68.

**OPEN:** the chunk size in cells, and the height representation (unit of one height step, range). Neither is in the
decisions or the browser config, which has no cell heights.

## 4. Camera (U-005)

- **Orthographic**: no perspective; things far away are drawn the same size as things near, which keeps every
  character readable at the same scale.
- Fixed yaw 45°, pitch 38°, and the screen always shows **20 world units** vertically. The camera never rotates.
- Browser values that carry over (spec recon section 4, D-045 still applies): distance 60; target clamped to the map
  bounds inset by 6.
- **Damped-spring follow**: the camera chases its target like a weight on a spring with friction tuned so it arrives
  without overshooting. The browser's spring (D-045) is critically damped, ω = 9 rad/s, solved exactly per step.
- **Look-ahead**: the follow target is the hero's position plus velocity × 0.25 s, capped at 2 units, so the camera
  leads the direction you run. (The browser eased the look-ahead in over 0.1 s and ignored jump velocity above 20 u/s.)
- Built with **Cinemachine 3** (Unity's camera package).
- Free camera (CM-65) is out of the first playable.

**OPEN:** whether the browser's combat framing (zoom out 12 % over 0.4 s when 3 or more enemies are within 8 units) and
hatch focus (ease in 12 ticks, hold 80, ease out at least 45) carry into Unity, and whether mouse-wheel zoom (browser
range 0.8 to 1.25) exists at all, since the wheel is bound to "Switch dig / place" in the controls table.

**OPEN:** whether the spring is Cinemachine's built-in position damping or a custom Cinemachine extension that solves the
D-045 spring exactly. Only the second reproduces the browser numbers.

## 5. Look: the `CM/Toon` shader contract (U-008)

Every 3D model in the game is drawn by **one shared shader, `CM/Toon`**. A shader is the small GPU program that decides
the colour of every pixel of a model.

**Inputs (per material or per asset).**
- **Flat bible-palette colour**: each surface region is one flat colour taken from Jeisil's asset bible (core palette
  on page 5: Void Navy #18233B, Azure Team #16C7D9, Crimson Team #FF5A5F, Warm Cream #F7F0E5, Biomass #B7F238,
  Corruption #8E3FD1, Stone #6F7282, Tool Brass #C79245; character sheets pp 06-08 add their own swatches, see
  [PARITY.md](PARITY.md)).
- **Outline width**, set per asset.
- **Team colour** (Azure or Crimson), applied through the team mask.

**Channels.**
- **Two-band light ramp**: light is not a smooth gradient; each pixel is either "lit" or "shade", two flat bands, which
  gives the cel-painted look.
- **Inverted-hull outline**: a second copy of the mesh is pushed outward along its normals, its front faces are culled
  so only the back shows, and it is drawn in the outline colour. The result is a clean outline whose width you control
  per asset.
- **Team-colour mask channel**: a mask marks which parts of a model take the team colour (on the Warrior: scarf,
  tabard, shield core, blade edge, per bible p06). The shader paints those parts Azure or Crimson; everything else
  keeps its bible colour.
- **Rim light for team read**: a thin band of light on the model's silhouette edge, tinted toward the team colour, so
  a player can tell the team from the outline of a figure in a crowd (the browser's D-047 idea of a rim mixed toward
  the team colour).

**Forbidden.**
- No glossy or metallic response anywhere: no specular highlights from the shader, no PBR metal, no reflection probes
  on characters, Kaiju or props.
- No second shader for characters, Kaiju or props. Everything goes through `CM/Toon`.
- Mechanics work never waits on art: it uses team-coloured capsules and cubes.

**OPEN (the architect specifies it in the shader item spec):** the exact contract details: shader property names; how the team mask is stored (a vertex-colour channel, a
mask texture, or a material slot); outline colour (the bible's line art is "dark-brown or navy" per the recon, not
black); the ramp threshold; the rim width and strength; and whether `CM/Toon` is hand-written HLSL or Shader Graph.

## 6. Art pipeline (U-009)

### Characters (Warrior, Farmer, Miner)

| Step | Rule |
| --- | --- |
| Source folder | `Assets/_Game/Art/Characters/_source/`: the `.blend` plus its FBX export, with a build note. In Git LFS. |
| Rig | All three classes share the **KayKit 41-joint rig**. A **rig** is the skeleton of joints a mesh is bound to so it can be animated. |
| Clips | All **76** CC0 KayKit clips work on every class (the browser kept 32; Unity keeps all 76). A **clip** is one recorded animation, such as `Running_A`. Source: the KayKit Adventurers 1.0 GLBs at `/Users/wilsonguenther/critical-mass-assets/work/kaykit-adventurers/KayKit-Character-Pack-Adventures-1.0-main/addons/kaykit_character_pack_adventures/Characters/gltf/` (`Knight.glb`, `Rogue.glb`, `Barbarian.glb`; 76 clips on a 41-joint skin each; licence CC0). |
| Mesh | Sculpted by a person in Blender to the bible page. Never procedural stand-ins. |
| Warrior | Max's Blender model is the starting mesh **if** his file passes audit (rig-compatible, clean topology). It is then corrected to bible p06: round shield (the class identifier, never removed), stone cleaver with glowing edge, dark-brown topknot with brass ring, grey stone slab armour with cyan cracks, flat toon shading. |
| Farmer, Miner | Same route: sculpt in Blender to bible p07 and p08. |
| Look | `CM/Toon` only (section 5). |
| Check | The parity gate ([PARITY.md](PARITY.md)). |

**FBX** is the 3D interchange format Unity imports models and animations from.

### Kaiju

Same route as the characters: sculpted in Blender to bible pp 10 and 11 (a heavy, gorilla-weight quadruped), never a
procedural stand-in. The source goes beside the characters' in the Art tree.

**OPEN (the architect specifies it in the art item spec):** the Kaiju's rig and clip list (the KayKit rig is a humanoid; p10 and p11 need egg, hatchling, adult idle, walk,
roar, slam wind-up, slam, berserk and KO), and its source folder (`Assets/_Game/Art/Kaiju/_source/` would mirror the
characters).

### Props and environment kit pieces

The headless-Blender script route (the browser loop's `scripts/sculpt/kaiju_adult.py`) is kept only for props and
environment kit pieces. Bible p14: "Author each damage state as a discrete 2D sprite or cutout layer. Avoid procedural
destruction", so walls, barricades and similar props get one authored model per state (intact, cracked, destroyed;
folded, deployed, damaged).

**OPEN (the architect specifies it in the art item spec):** where the prop scripts and their sources live in this repo (`tools/sculpt/` and
`Assets/_Game/Art/Props/_source/` would mirror the character layout), and whether the browser's script budgets
(12,000 triangles, a builder's guess) carry over.

### Export and import settings

**OPEN (the architect specifies it in the art item spec):** FBX export settings (scale, forward and up axes, apply transforms, clip naming, embedded textures or not) and
Unity import settings (Generic or Humanoid animation type, avatar per class or shared, compression). None are decided.

**Decided (U-022).** glTFast (`com.unity.cloud.gltfast`) is build-time and import only: it brings the CC0 KayKit GLB
sources and their clips into the editor. Nothing loads glTF at runtime.

**Decided (U-022).** All art source is authored in **Blender 5.2.3**.

## 7. Folder map

| Path | Holds | State |
| --- | --- | --- |
| `Assets/_Game/Sim/` | `CriticalMass.Sim`: rules | exists (`SimVersion.cs` only) |
| `Assets/_Game/Sim/Rules/` | per-item rule systems (`JumpSystem.cs`, `TerraformSystem.cs`, ...) | named in MAX-ITEMS |
| `Assets/_Game/Sim/Maps/` | extra maps (`EmberOrchardMap.cs`, `TidalCrownMap.cs`) | named in MAX-ITEMS |
| `Assets/_Game/Sim.Tests/` | `CriticalMass.Sim.Tests`: ported NUnit oracle, `[Category("Slow")]` soak fixtures | exists |
| `Assets/_Game/Runtime/` | `CriticalMass.Runtime`: Sim driver, input layer, views | exists (`AssemblyInfo.cs` only) |
| `Assets/_Game/Runtime/View/` | per-item views (`ActorJumpLift.cs`, `CarriedBlockView.cs`, ...) | named in MAX-ITEMS |
| `Assets/_Game/Editor/` | `CriticalMass.Editor`: `Build.cs`, `ProjectSetup.cs`, parity capture | exists |
| `Assets/_Game/Art/Characters/_source/` | character `.blend` + FBX + build note | decided (U-009) |
| `Assets/_Game/Art/{Characters,Kaiju,Props,Environment,Materials,Shaders,UI,VFX}/` | imported art; `CM/Toon` in `Shaders/` | folders exist |
| `Assets/_Game/Audio/` | the 42 CC0 sounds (Phase 3) | folder exists |
| `Assets/_Game/Scenes/` | `Boot.unity` (exists), `Arena.unity` (F-08) | |
| `Assets/_Game/Settings/` | URP assets, `Controls.inputactions` (F-07) | URP assets exist |
| `Packages/` | `manifest.json`, `packages-lock.json` | exists |
| `ProjectSettings/` | Unity project settings (Force Text) | exists |
| `tools/` | `unity-compile.sh`, `unity-test.sh`, `unity-build-mac.sh`, `_unity-env.sh`; `unity-capture.sh` (U-010) | capture script not yet written |
| `loop/` | build-loop rules and harness scripts, committed (U-011) | [loop/README.md](loop/README.md) only |
| `Builds/`, `Library/`, `Logs/`, `Temp/`, `UserSettings/` | generated; git-ignored | |

## 8. Runtime Dependencies

Everything the shipped game or the build needs. Nothing may be "found on the machine" without being listed here.
Package versions are copied from `Packages/manifest.json` (direct) and `Packages/packages-lock.json` (pulled in by a
direct package).

**Where it lives:** *shipped in build* = inside the player app a player runs; *build-time only* = needed to compile,
test or build, never in the player; *developer machine* = must be installed on the machine that edits or builds.

### Engine and tools

| Dependency | Version | Where it lives | Notes |
| --- | --- | --- | --- |
| Unity Editor | 6000.3.28f1 (revision 8f082d2fe476) | developer machine | `ProjectSettings/ProjectVersion.txt`; installed via Unity Hub at `/Applications/Unity/Hub/Editor/6000.3.28f1/`; `UNITY_PATH` overrides |
| Unity player runtime (Mono scripting backend, .NET Standard API level, URP Forward+, linear colour) | as the editor | shipped in build | set by `ProjectSetup.Apply` |
| Unity Hub | not pinned | developer machine | installs the editor and its build modules |
| Mac build support (StandaloneOSX) | as the editor | developer machine | `tools/unity-build-mac.sh` works today |
| Windows build support (StandaloneWindows64) | as the editor | developer machine | not installed; `Build.Windows` exits 3 until it is; install before the first handoff (U-013) |
| UnityYAMLMerge | ships with the editor | developer machine | merge driver for `.unity`, `.prefab`, `.asset`, `.meta`; one-time `git config` per clone (README) |
| Git | not pinned | developer machine | |
| Git LFS | 3.7.1 observed on this Mac; not pinned | developer machine | `.gitattributes` routes .fbx, .blend, .glb, .png, .jpg, .psd, .tga, .exr, .hdr, .wav, .ogg, .mp3 through LFS |
| Blender | 5.2.3 (pinned, U-022; 5.2.3 LTS installed at `/Applications/Blender.app` on this Mac) | developer machine | art source for characters, Kaiju, props (U-009); headless prop scripts |
| bash, `xmllint` | macOS system | developer machine | `tools/*.sh`; `unity-test.sh` reads results with `xmllint` |

### Unity packages (direct, `Packages/manifest.json`)

| Package | Version | Where it lives | Job |
| --- | --- | --- | --- |
| com.unity.render-pipelines.universal | 17.3.0 | shipped in build | URP, Forward+ renderer; hosts `CM/Toon` |
| com.unity.inputsystem | 1.20.1 | shipped in build | `Controls.inputactions` (U-006) |
| com.unity.cinemachine | 3.1.7 | shipped in build | follow camera (U-005) |
| com.unity.cloud.gltfast | 6.14.1 | build-time only | imports the KayKit GLB sources and clips in the editor; nothing loads glTF at runtime (U-022) |
| com.unity.test-framework | 1.6.0 | build-time only | EditMode and PlayMode tests (U-011 gates) |
| com.unity.ugui | 2.0.0 | shipped in build | uGUI for HUD and panels (CM-84 `StationPanel` placeholder) |
| com.unity.timeline | 1.8.13 | shipped in build | in the manifest; no decided job yet |
| com.unity.modules.accessibility | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.adaptiveperformance | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.ai | 1.0.0 | shipped in build | built-in engine module (NavMesh is not used, U-003) |
| com.unity.modules.androidjni | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.animation | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.assetbundle | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.audio | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.cloth | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.director | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.imageconversion | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.imgui | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.jsonserialize | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.particlesystem | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.physics | 1.0.0 | shipped in build | built-in engine module (not used by rules, U-003) |
| com.unity.modules.physics2d | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.screencapture | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.terrain | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.terrainphysics | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.tilemap | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.ui | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.uielements | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.umbra | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.unityanalytics | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.unitywebrequest | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.unitywebrequestassetbundle | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.unitywebrequestaudio | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.unitywebrequesttexture | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.unitywebrequestwww | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.vectorgraphics | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.vehicles | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.video | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.vr | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.wind | 1.0.0 | shipped in build | built-in engine module |
| com.unity.modules.xr | 1.0.0 | shipped in build | built-in engine module |

"Shipped in build" for a built-in module means it is available to the player; Unity's engine code stripping may drop
modules nothing references. Whether to remove unused modules from the manifest is not decided.

### Unity packages (pulled in by the direct ones, `Packages/packages-lock.json`)

| Package | Version | Pulled in by (lock depth) |
| --- | --- | --- |
| com.unity.render-pipelines.core | 17.3.0 | URP (1) |
| com.unity.render-pipelines.universal-config | 17.0.3 | URP (1) |
| com.unity.shadergraph | 17.3.0 | URP (1) |
| com.unity.burst | 1.8.31 | depth 1 |
| com.unity.collections | 2.7.1 | depth 1 |
| com.unity.mathematics | 1.3.3 | depth 1 |
| com.unity.splines | 2.9.1 | depth 1 |
| com.unity.ext.nunit | 2.0.5 | test framework (1) |
| com.unity.nuget.mono-cecil | 1.11.6 | depth 2 |
| com.unity.searcher | 4.9.5 | depth 2 |
| com.unity.settings-manager | 2.1.1 | depth 2 |
| com.unity.test-framework.performance | 3.5.0 | depth 2 |
| com.unity.modules.hierarchycore | 1.0.0 | built-in engine module, depth 1 |
| com.unity.modules.subsystems | 1.0.0 | built-in engine module, depth 1 |

### Content the shipped game will carry

| Content | Source | Licence | Where it lives |
| --- | --- | --- | --- |
| Character rig and 76 clips | KayKit Adventurers 1.0 (path in section 6) | CC0 | shipped in build |
| 42 sounds (Phase 3) | the browser's Kenney SFX bank (D-044) | CC0 | shipped in build |
| Fonts Bungee, Nunito Sans, JetBrains Mono (bible p05, D-064) | **OPEN**: not yet in the repo; source and licence file to be added with the HUD work | not recorded | shipped in build |
