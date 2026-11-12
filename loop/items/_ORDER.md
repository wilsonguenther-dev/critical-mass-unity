# Phase 0 order

Drafted 2026-11-11; reviewed and corrected 2026-11-11 against origin/main 09e0904 and the installed packages (architect
rulings applied, see `_OPEN.md`). Item specs: `loop/items/F-*.md`; conventions: `_CONVENTIONS.md`;
open choices: `_OPEN.md`. Nothing here is launched.

## 1. Items

| Item | MAX-ITEMS id | Ports | Est. C# lines | Est. test lines | Ported cases |
| --- | --- | --- | --- | --- | --- |
| F-01 | F-01 | game.config.ts, palette.ts (PALETTE) | 420 | 170 | 9 (+2 added) |
| F-02a | F-02 | types.ts, rng.ts, boundary | 330 | 140 | 7 |
| F-03 | F-03 | map.ts, grid.ts (no setCell) | 430 | 190 | 6 |
| F-04 | F-04 | flowfield.ts, spatialHash.ts | 300 | 210 | 8 |
| F-02b | F-02 | world.ts, setCell | 560 | 180 | 7 |
| F-05a | F-05 | input.ts, movement.ts | 170 | 260 | 11 |
| F-05b | F-05 | collide.ts | 190 | 220 | 5 |
| F-05c | F-05 | damage.ts, combat.ts | 230 | 420 | 14 |
| F-05d | F-05 | respawn.ts (no CM-54) | 140 | 330 | 12 |
| F-05e | F-05 | barricades.ts | 320 | 380 | 15 |
| F-05f | F-05 | abilities.ts | 130 | 300 | 8 |
| F-05g | F-05 (MAX lists debug under F-02) | resources.ts, debug.ts | 230 | 420 | 16 |
| F-05h | F-05 | kaiju.ts 1-262 | 270 | 420 | 12 |
| F-05j | F-05 | kaiju.ts 263-543 | 290 | 330 | 7 |
| F-05i | F-05 (MAX lists step/hill/match under F-02) | step.ts, hill.ts, match.ts | 140 | 330 | 17 |
| F-06b | F-06 | bots/fields.ts | 380 | 130 | 3 |
| F-06a | F-06 | bots/state.ts, headingMetric.ts | 210 | 140 | 3 |
| F-06c | F-06 | bots/steer.ts | 480 | 30 | 0 (+1 added) |
| F-06d | F-06 | bots/fsm.ts (no CM-38) | 420 | 20 | 0 (+1 added) |
| F-06e | F-06 | bots/director.ts, bots/index.ts, MatchTick | 360 | 120 | 3 |
| F-06f | F-06 | bots.test.ts match bands | 0 | 330 | 8 (+1 Explicit perf) |
| F-07 | F-07 | Controls asset, humanInput.ts state, ProjectSetup.Apply (actions, placeholder material) | 300 | 260 | 12 |
| F-08a | F-08 | SimDriver.tsx, bootstrap, placeholder views, SimSpace, HumanInputSource | 400 | 60 | 0 (+3 added) |
| F-08b | F-08 | rig.ts, CameraRig.tsx follow, grid meshes | 400 | 80 | 4 |
| F-08c | F-08 | debug HUD, PlayMode smoke | 170 | 60 | 1 (new) |
| F-09 | F-09 (MAX lists outcomes.slow under F-06) | outcomes.ts, thresholds | 260 | 230 | 14 |
| **Total** | | | **~7,550** | **~5,760** | **202 ported runtime cases (198 methods) + 7 added EditMode + 1 PlayMode smoke** |

## 2. Dependency graph

```
F-01 -> F-02a -> F-03 -> F-04 -> F-02b
F-02b -> {F-05a, F-05b, F-05c, F-05d, F-05e, F-05f, F-05g, F-05h, F-05i, F-05j}   (coupled: one compile unit)
all F-05* -> F-06b -> F-06a -> F-06c -> F-06d -> F-06e -> F-06f                      (coupled: one compile unit)
F-05a -> F-07
F-06e, F-07 -> {F-08a, F-08b, F-08c}                                                 (coupled: one compile unit)
F-06e -> F-09
```

Coupling (O-21): batch-2 tests call `SimStep.Step`, which calls every batch-2 system, and combat/kaiju/barricades/
abilities call each other; the bot files call each other (fsm <-> director); `GameBootstrap` creates the F-08b and F-08c
components. A coupled set lands together; a red in it is repaired by the fix seat, then the set re-compiles.

## 3. Batches (no two items in a batch share a `files_allowed` path: checked by script)

| Batch | Items | coupled | Waves (builders) | Compiles | Gate after the batch |
| --- | --- | --- | --- | --- | --- |
| 1 | F-01, F-02a, F-03, F-04, F-02b | false (a chain: each item waits on the earlier ones) | 1 wave of 5 | 1 | compile, EditMode 41 (37 ported + 3 added + SimVersionTests 1; panel 2026-11-12 added the JsMath case) |
| 2 | F-05a, F-05b, F-05c, F-05d, F-05e, F-05f, F-05g, F-05h, F-05i, F-05j | true (U-026) | wave 1: F-05a, F-05b, F-05c, F-05d, F-05e; wave 2: F-05f, F-05g, F-05h, F-05i, F-05j | 1 after both waves | compile, EditMode +117 runtime cases (113 methods) |
| 3 | F-06b, F-06a, F-06c, F-06d, F-06e, F-06f, F-07 | true for the six F-06 items (U-026); F-07 is not coupled to them and drops alone | wave 1: F-06b, F-06a, F-06c, F-06d, F-06e; wave 2: F-06f, F-07 | 1 after both waves | compile, EditMode +31 (29 ported, 2 added; the perf case is Explicit; corrected by the panel 2026-11-12) |
| 4 | F-08a, F-08b, F-08c, F-09 | true for F-08a, F-08b, F-08c (bootstrap creates the other two; SimSpace is F-08a's); F-09 is not coupled and drops alone | 1 wave of 4 | 1 | compile, EditMode +7 (4 ported, 3 added), PlayMode smoke 1, Mac build; soak lane `tools/unity-test.sh --category Slow` 14 (U-028) |

Acyclic: every `waits_on` edge points to an earlier batch, or inside batch 1 to an earlier item of the chain
(F-01 -> F-02a -> F-03 -> F-04 -> F-02b). The only cycles are the code-level ones inside the coupled sets (combat <->
kaiju <-> barricades through `SimStep`, fsm <-> director, bootstrap <-> camera/HUD), which compile as one unit: on a red
one fix seat repairs the red item, at most 2 rounds, then halt (U-026). Batch membership equals `waits_on`: each F-05
item waits on F-02b only; F-06b waits on all ten F-05 items; F-06a, F-06c, F-06d, F-06e, F-06f each wait on the
previous one; F-07 waits on F-05a; F-08a, F-08b, F-08c wait on F-06e and F-07; F-09 waits on F-06e.

Shared-file owners (one writer per batch, script-checked): `Assets/_Game/Runtime/CriticalMass.Runtime.asmdef`,
`Assets/_Game/Runtime.Tests/CriticalMass.Runtime.Tests.asmdef` and `Assets/_Game/Editor/**` (ProjectSetup.cs and the
Editor asmdef): F-07 only, in Phase 0. No item touches `ProjectSettings/`, `Packages/` or `tools/unity-test.sh`; F-08c creates
`tools/unity-playtest.sh` and F-09 creates `tools/unity-soak.sh` (the two paths the harness gates run, U-028). No item writes a
`.meta`, scene, prefab, material or asset file (U-027): Unity generates the metas on the integrator's import and
ProjectSetup.Apply creates the one runtime asset (the placeholder material). ProjectSetup changes made in batch 3 must
be applied (U-029) before batch 4's gates, which need the project-wide actions and the material.

Phase 0 exit (U-012): after batch 4, EditMode default 195 cases green (187 ported + 8 added; plus SimVersionTests), soak 14 green, PlayMode
smoke green, Mac build green.

## 4. Ported test count

202 vitest runtime cases ported in 198 NUnit methods (the clarity refusal loop is 1 method with 5 `[TestCase]` rows):
187 in the default EditMode gate, 14 `[Category("Slow")]` (F-09), 1 `[Explicit]` perf (F-06f). Counted with
`grep -cE '^\s*(it|test)(\.skipIf\([^)]*\))?\('` per file on 09e0904 (no `.each` in any ported file; the one
`it` inside a `for` loop, clarity.test.ts line 223, runs 5 times) and matched name by name: every ported vitest title
maps to exactly one `tests_required` entry. Added (no vitest twin): 2 config (F-01), 1 JsMath (F-02a, panel 2026-11-12), 1 steer (F-06c), 1 fsm (F-06d),
3 SimSpace (F-08a), 1 PlayMode smoke (F-08c). Not ported: balance-table (3 sites, 11 runtime, O-02), humanInput (2,
O-26), bots CM21 capture (1), palette.test (13, browser CSS), buildTag.test (3, browser build tag). Moved to mechanics
items: respawn CM-54 (7, CM-84), bots human-jump (1, CM-32). The mechanics test files on main (jump, terraform, topple,
demolitions, heal, biomass, corruption) belong to their CM items.

## 5. Coverage: recon section 5 files

| Browser file | Item |
| --- | --- |
| config/game.config.ts, game.config.test.ts | F-01 (roster case: F-02b) |
| config/palette.ts | F-01 (PALETTE; STATE_CUES and CSS are browser-only) |
| config/balance-table.test.ts | not ported (O-02) |
| sim/rng.ts, types.ts, boundary.test.ts | F-02a |
| sim/index.ts | not ported (barrel; namespaces replace it) |
| sim/world.ts | F-02b |
| sim/map.ts | F-03 (route tests F-04) |
| sim/grid.ts | F-03 (setCell F-02b, O-31) |
| sim/flowfield.ts, spatialHash.ts | F-04 |
| sim/input.ts, movement.ts | F-05a |
| sim/collide.ts | F-05b |
| sim/damage.ts, combat.ts | F-05c |
| sim/respawn.ts | F-05d (CM-54 part: CM-84) |
| sim/barricades.ts | F-05e |
| sim/abilities.ts | F-05f |
| sim/resources.ts, debug.ts | F-05g (meat: CM-35; biomass instability: CM-85) |
| sim/kaiju.ts | F-05h + F-05j (one file, split by line) |
| sim/step.ts, hill.ts, match.ts | F-05i (heal call: CM-37) |
| sim/clarity.test.ts | F-05d, F-05e, F-05g (by subject) |
| sim/jump.ts | belongs to mechanics item CM-32 |
| sim/terraform.ts | belongs to mechanics item CM-30 |
| sim/topple.ts | belongs to mechanics item CM-31 (map decoration data in F-03) |
| sim/demolitions.ts | belongs to mechanics item CM-38 |
| sim/heal.ts | belongs to mechanics item CM-37 |
| sim/biomass and corruption tests | belong to mechanics items CM-35 and CM-34 |
| bots/fields.ts | F-06b |
| bots/state.ts, headingMetric.ts | F-06a |
| bots/steer.ts | F-06c |
| bots/fsm.ts | F-06d (Demolitions: CM-38) |
| bots/director.ts, index.ts | F-06e (meat pickup CM-35, TNT CM-38) |
| bots/bots.test.ts | F-06a, F-06b, F-06e, F-06f (by describe; human-jump case CM-32) |
| bots/outcomes.ts, outcomes.thresholds.ts, outcomes.slow.test.ts | F-09 |
| game/humanInput.ts (+test) | F-07 (state, tests) + F-08a (`HumanInputSource`) |
| game/rig.ts (+test), CameraRig.tsx | F-08b |
| game/SimDriver.tsx | F-06e (tick order) + F-08a (accumulator) |

Totals: 50 recon section-5 files; 41 assigned to F-items, 7 to mechanics items (5 sources, 2 tests), 2 not ported
(balance-table.test, the sim barrel index.ts). Three source files are split
across two sub-items (grid.ts, kaiju.ts, SimDriver.tsx), and four test files are split by subject (map, clarity, bots,
combat).

## 6. MAX-ITEMS items: the F-items each waits on

MAX-ITEMS.md is updated to these final ids (2026-11-11). Split ids: F-02 = F-02a, F-02b; F-05 = F-05a..F-05j;
F-06 = F-06a..F-06f; F-08 = F-08a, F-08b, F-08c.

| Max item | waits on (final) | Reopens (F-item files) |
| --- | --- | --- |
| CM-32 jump | F-01 .. F-08c | F-02a (Actor), F-05a (Movement.cs), F-05b (Collide.cs), F-07 + F-08a (Jump binding read) |
| CM-33 bridge spans | F-01 .. F-08c | F-03, F-04, F-05b, F-08b |
| CM-30 dig/place | F-01 .. F-09 | F-02a, F-02b (World, SetCell), F-05f (Abilities.cs), F-06b, F-06e, F-07, F-08b |
| CM-31 topple | CM-30 | F-02a, F-02b, F-03 (Decorations), F-05c (Combat.cs) |
| CM-37 heal | F-01 .. F-09 | F-05i (SimStep.cs), F-06d, F-02a (SimEventType) |
| CM-35 biomass types | F-01 .. F-09 | every file with an `ITEM CM-35` marker: Core/SimEvent.cs, Core/Actor.cs, Core/ResourceNode.cs, Core/MatchState.cs, Core/World.cs (F-02a, F-02b), Combat.cs (F-05c), SimResources.cs (F-05g), Bots/BotBrain.cs (F-06e); the browser files with CM-35 code are exactly types.ts, world.ts, combat.ts, resources.ts, bots/index.ts |
| CM-34 | CM-35 | F-05g |
| CM-85 | CM-34 | SimResources.cs, DebugTests.cs, KaijuResourceTests.cs (F-05g); no kaiju.ts code (verified: CM-85 commit 3807f98 touches game.config.ts, resources.ts, debug.test.ts, kaiju.test.ts, corruption tests) |
| CM-38 Demolitions | CM-30 | F-05c, F-05f, F-06d, F-06e |
| CM-64 | CM-85 (kept) | branch `origin/cm/CM-64` read: built on CM-35 (e0ab1c7 is an ancestor), not on CM-85 (3807f98 is not); it edits resources.ts, damage.ts, types.ts, world.ts, fsm.ts, director.ts, debug.test.ts, kaiju.test.ts, so it shares SimResources.cs and the two test files with CM-85: the CM-85 wait serialises those edits |
| CM-36 | CM-85 (kept) | branch `origin/cm/CM-36` read: built on CM-35, not on CM-85; edits kaiju.ts (+4), fsm.ts, steer.ts, types.ts, world.ts; its berserk-share band is measured on instability, which CM-85 changes, so the wait stands |
| CM-39 stations/upgrades | CM-35 | F-05d (Respawn.cs), F-08c |
| CM-84 class switch | CM-39 | F-05d (CM-54 cut + 7 tests), F-02a (SimEventType, Actor) |
| CM-58 Builder | CM-39, CM-30 | F-01 (adds its provisional numbers, O-23 overrides U-007's wording), F-05f |
| CM-59 | CM-58 | - |
| CM-66 CLI | F-08a, F-08b, F-08c, F-09 | F-08a (driver reads `-teamSize`, `-speed`); the 16v16 / 2x defaults are F-01's |
| CM-65 | F-08a, F-08b, F-08c | F-08b |
| CM-68 maps | F-03, F-04, F-06b | F-03, F-04, F-06b |
| CM-67 heavy/light | team pick | F-05c |
