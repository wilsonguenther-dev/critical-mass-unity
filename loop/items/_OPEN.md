# Phase 0 open choices: RULED

Drafted 2026-11-11 by the spec author; ruled by the architect the same day and applied to the item specs by the
review. "Chosen" is what the specs use; "Ruling" is final. Rows not named in the ruling list stand as chosen.

| Id | Question | Chosen (used in the specs) | Ruling | Items |
| --- | --- | --- | --- | --- |
| O-01 | Float32 storage | `double` everywhere; every `Math.fround(v)` -> `v` | **Stands.** | all Sim items, F-09 |
| O-02 | `balance-table.test.ts` (11 runtime cases: prints tables, BALANCE.md verbatim, no TODO) | not ported | **Stands.** | F-01 |
| O-03 | Two tests added to F-01 that have no vitest twin | `DefaultIs16v16At2xPace`, `EveryTunableMatchesTheBrowserValue` | **Stands.** | F-01 |
| O-04 | Config access | instance `world.Config`; `GameConfig.CreateDefault()` (16v16, 2x) and `CreateBrowser5v5()` presets | **Stands.** | all |
| O-05 | Which preset ported tests use | `CreateBrowser5v5()` | **Stands.** | all tests |
| O-06 | `InputFrame.Jump`, `InputFrame.Terraform` in base | kept as data, unread | **Stands.** | F-02a, F-05a, F-06e, F-07 |
| O-07 | CM-54 switch cooldown (on main) | cut, marked `ITEM CM-84`; its 7 respawn tests move to CM-84 | **Stands.** | F-05d |
| O-08 | CM-85 biomass-instability terms in 2 base tests | ported without the term, assertion marked `ITEM CM-85` | **Stands.** | F-05g |
| O-09 | Bot progress detector (tagged CM-30, reads no terraform state) | base | **Stands.** | F-06d |
| O-10 | Namespaces | `CriticalMass.Sim.<Area>` (Config, Core, Grid, Maps, Bots) per the task | **Stands.** | all |
| O-11 | Cut marker text | `// ITEM CM-xx: added by its owner (<what>)` per the task | **Stands.** | all Sim items |
| O-12 | Camera extras (combat framing, hatch focus, wheel zoom) | not ported in Phase 0 | **Stands.** | F-08b |
| O-13 | Axis mapping and camera angle | Unity `(simX, height, -simZ)`, camera Euler (38, 315, 0) | **Stands, extended:** the conversion lives only in `CriticalMass.Runtime.SimSpace` (`ToWorld` / `ToSim`, plus `YawDeg` and `CameraRotation`), owned by F-08a, pinned by `SimSpaceTests` (azure pad left, screen-up = sim (-1,-1)/sqrt2, exact round trip). No other file negates z; `HumanInputSource` moves to F-08a to use it. | F-07, F-08a, F-08b |
| O-14 | Pointer position for aim | a `Point` PassThrough action in Controls.inputactions | **Approved:** F-07 carries `architect_approved: U-006` (Point is a device read, not a game binding). | F-07 |
| O-15 | Gamepad aim | none: facing follows movement (browser "no aim" rule) | **Stands for Phase 0:** no gamepad aim; facing follows movement. | F-07 |
| O-16 | How Runtime finds the actions | (overruled; see ruling) | **Overruled:** no hand-written fileID or GUID. F-07 extends `CriticalMass.Editor.ProjectSetup.Apply` to load `Assets/_Game/Settings/Controls.inputactions` and set `InputSystem.actions = asset` (public static setter, Input System 1.20.1 `InputSystem.cs` line 3042; the editor hook stores it in EditorBuildSettings `com.unity.input.settings.actions`); Runtime reads `InputSystem.actions`. The integrator runs Apply before the compile gate (U-029). F-07 owns `Assets/_Game/Editor/**` in batch 3. | F-07, F-08a |
| O-17 | Arena scene | no new scene: `GameBootstrap` builds the hierarchy from code when Boot.unity loads | **Stands, recorded as U-027:** the hierarchy is built in code when Boot loads; builders never author scene, prefab, asset, material or `.meta` files (conventions section 9 rewritten; every folder `.meta` entry removed from files_allowed; the placeholder material is created by ProjectSetup.Apply). Controls.inputactions is plain JSON, shape in F-07. | F-08a, F-08c |
| O-18 | Grid look | chunk 16 x 16; box heights Wall/BreakableIntact +1.5, Cracked +0.75, Barricade +1.0, floor slabs 0.5 thick; palette mapping in F-08b | **Stands.** | F-08b |
| O-19 | Test assembly locations | `Assets/_Game/Runtime.Tests` (EditMode), `Assets/_Game/Runtime.PlayTests` (PlayMode), `tools/unity-playtest.sh` | **Stands.** `tools/unity-playtest.sh` is F-08c's; the soak lane needs no new script (O-20). | F-07, F-08b, F-08c |
| O-20 | Slow soak in the default gate | excluded from the default gate; own soak lane | **Stands, recorded as U-028:** `Slow` is excluded by `tools/unity-test.sh` by default (already in the harness) and run by `tools/unity-test.sh --category Slow` after each landing; F-09 edits no `tools/` file. | F-09 |
| O-21 | Coupled batches (2, 3, 4) vs the U-011 drop rule | the coupled items compile as a unit; on a red the integrator's fix seat repairs the red item and the batch lands together | **Stands, recorded as U-026:** coupled batches compile as a unit; on red one fix seat repairs the red item, at most 2 rounds, then halt. | `_ORDER.md` |
| O-22 | Batch 2 has 10 items (above "about 6"; U-015 = 5 builders) | two waves of 5 builders, one compile after both | **Stands (U-026):** two waves of 5 builders, one compile after both (waves listed in `_ORDER.md`). | F-05* |
| O-23 | CM-58 provisional numbers (U-007) | left to CM-58, which adds them marked provisional | **Stands; overrides U-007's wording:** the CM-58 provisional numbers enter GameConfig with CM-58, not F-01. | F-01 |
| O-24 | Splits by partial class | `Kaiju` split at kaiju.ts line 262 (F-05h / F-05j); `MapGrid.SetCell` in F-02b | **Stands.** Split corrected: F-05h owns kaiju.ts lines 1-262 including `selectTarget`, the clearance helpers, `hatch` and every module constant (as `internal`); F-05j owns lines 263-543 (table in F-05h). | F-02b, F-03, F-05h, F-05j |
| O-25 | `vi.spyOn(computeFlowField)` | `FlowField.ComputeCalls` static counter | **Stands.** | F-04, F-05e |
| O-26 | humanInput test mapping | the 2 Space tests drive `Ability` (browser Space was the ability) plus a Jump latch assertion; arrow-key and backtick halves cut; preventDefault and T cases not ported | **Stands.** | F-07 |
| O-27 | `flowStep` name in BotFields | `FlowStepAt` (struct keeps `FlowStep`) | **Stands.** | F-06b |
| O-28 | Reading `heading-before.json` in Sim.Tests | 10-line hand parser for its fixed shape | **Stands.** | F-06a |
| O-29 | Runtime asmdef references | F-07 adds InputSystem, Cinemachine and URP references at once | **Stands.** F-07 is also the only Phase 0 writer of `Assets/_Game/Editor/**` (O-16 ruling). | F-07 |
| O-30 | Kaiju and node placeholder colours | Egg Cream, Neutral Stone, team colours, Berserk Corruption; nodes Biomass/Corruption | **Stands.** | F-08a |
| O-31 | `grid.ts` and `map.test.ts` split across two items | `setCell` in F-02b (it needs `World`); map route cases in F-04 (they need `FlowField`) | **Stands.** | F-02b, F-03, F-04 |
