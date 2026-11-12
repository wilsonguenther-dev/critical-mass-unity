# Max's work list: game mechanics, Unity rebuild

Written 2026-11-12T02:16:06Z (Wednesday 2026-11-11, 8:16 pm Central). Replaces the browser-era list at
`loop/MAX-ITEMS.md` in the old repo [wilsonguenther-dev/critical-mass](https://github.com/wilsonguenther-dev/critical-mass)
(snapshot of 2026-11-09). Old-repo paths below are on its `main` at commit `09e0904` unless a branch is named.

Critical Mass is now a Unity game. You own the game-mechanics items below. The build AI ("the loop") builds the
foundation first (section 3) and the look (models, shader, art). The architect's answers (U-004 to U-007 and U-023, see
[DECISIONS.md](DECISIONS.md)) are written into the items below as **Decided (U-###)**. Anything still marked **OPEN**
waits on the architect: build the parts of that item that do not depend on the open answer, and do not pick the answer
yourself.

---

## 1. Start here

**Get the project.**

```bash
git lfs install                      # once per machine
git clone https://github.com/wilsonguenther-dev/critical-mass-unity.git
cd critical-mass-unity && git lfs pull
```

Open it with Unity **6000.3.28f1** (Unity 6.3) from Unity Hub. Other versions will rewrite project files; do not upgrade.

**One-time merge setup (per clone).** Scenes, prefabs and assets are Unity YAML and merge through UnityYAMLMerge:

```bash
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver '"/Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/Helpers/UnityYAMLMerge" merge -p %O %B %A %A'
git config merge.unityyamlmerge.recursive binary
```

If your editor is installed somewhere else, point the driver path (and `UNITY_PATH` for the scripts) at your install.

**The three commands.** Each runs Unity headless and exits with Unity's real exit code.

```bash
tools/unity-compile.sh    # compile every assembly; fails on any compiler error
tools/unity-test.sh       # EditMode tests; prints "unity-test: result=... total=... passed=... failed=..."
tools/unity-build-mac.sh  # macOS player build -> Builds/macOS/CriticalMass.app
```

**How to work.**

- Branch `max/<item-id>-short-name` off `main` (for example `max/cm-32-jumping`). One item per pull request into `main`.
  Never push to `main` directly.
- Before opening the PR run `tools/unity-compile.sh` and `tools/unity-test.sh` and paste both result lines into the PR.
- Binary assets go through Git LFS (already set up in `.gitattributes` for .fbx, .blend, .png, .wav and the rest).
- Scenes and prefabs conflict easily. An item touches only the scenes and prefabs it names. Most items below name none.
- An item is not done without its tests (see rule 2 below).

**The architecture (decided; items are written against it).**

1. The game rules live in `Assets/_Game/Sim` (assembly `CriticalMass.Sim`). Plain C#, no UnityEngine (the assembly
   enforces this). Fixed tick at **30 Hz**, the browser's `simHz`. Numbers are `double`. State is plain classes and
   structs. Randomness only from the Sim's seeded RNG (the browser's mulberry32, ported). No MonoBehaviour, no Unity
   physics, no `UnityEngine.Random` in rules. One `GameConfig` class holds every tunable, ported 1:1 from the old
   `src/config/game.config.ts`.
2. Every rule is a port of the matching old file in `src/sim` or `src/bots`, and its vitest file is ported to NUnit in
   `Assets/_Game/Sim.Tests` as the oracle: same cases, same expected numbers. One `[Test]` per vitest `it(...)`, named
   after it in PascalCase. Slow 100-seed soak files become their own fixture tagged `[Category("Slow")]`.
3. `Assets/_Game/Runtime` (`CriticalMass.Runtime`) is the view. MonoBehaviours read Sim state every frame and
   interpolate between ticks; player input becomes Sim commands. Runtime never changes game state itself.
4. The ground is a grid of cells with heights, owned by the Sim. That is what makes digging, placing ground and
   knock-down structures real. Runtime rebuilds chunk meshes from the grid. Bots path on the ported flow field. No NavMesh.
5. Controls are one Input System asset, `Assets/_Game/Settings/Controls.inputactions` (table below). No item adds or
   moves a binding.
6. Camera: Cinemachine 3, fixed yaw (never rotates), follows your hero; angle and size come from the old config.
7. Look: models from the asset bible under one shared toon shader with outlines. Mechanics items use plain
   placeholder capsules and cubes coloured by team and never wait on art.

**Controls.**

| Action | Keyboard and mouse | Gamepad |
| --- | --- | --- |
| Move | W A S D | Left stick |
| Jump | Space | South button |
| Primary attack | Left mouse | West button |
| Class ability | Right mouse | Right trigger |
| Interact, carry, drop | E | North button |
| Dig or place (Miner) | F | Left trigger |
| Switch dig / place | Mouse wheel | Bumpers |
| Scoreboard | Tab | (none) |
| Pause | Esc | Start |

Browser keys that changed: jump was Q (D-062), the class ability was Space. The rules behind them did not change.

**Shared files.** **Decided (U-023).** Several old files mix base rules with item code (for example `resources.ts`
carries CM-35 meat and CM-85 biomass instability, `bots/fsm.ts` carries CM-38 and CM-37 bot rules). The loop ports
those files in the foundation with the item code left out and marked with a `// ITEM CM-xx: added by its owner (<what>)` comment line (one grep, `ITEM CM-`, finds every hole); the item owner
(you) adds that part back in the item, with its tests.

---

## 2. Your items at a glance (build order)

"Old state" is where the item stood in the browser build. **Landed** = the rule is on the old `main`; you port it.
**Built, not in main** = code exists on an old branch but was held or pulled out; you port it from that branch.
**New** = no code exists; you build it from the rule text here.

| # | Id | Item | Old state | Waits on |
| --- | --- | --- | --- | --- |
| 1 | CM-32 | Jumping (Travis must-have) | Landed | F-01 to F-08c |
| 2 | CM-33 | Cross bridges at any angle (Travis must-have) | Built, not in main (pulled out twice) | F-01 to F-08c |
| 3 | CM-30 | Miners dig and place ground (Travis must-have) | Landed | F-01 to F-09 |
| 4 | CM-31 | Knock-down structures (Travis must-have) | Landed | CM-30 |
| 5 | CM-37 | Base healing fountain | Landed | F-01 to F-09 |
| 6 | CM-35 | Two kinds of biomass | Landed | F-01 to F-09 |
| 7 | CM-34 | Slower corruption | Landed | CM-35 |
| 8 | CM-85 | Corruption also builds from biomass | Landed | CM-34 |
| 9 | CM-38 | Demolitions Expert | Landed | CM-30 |
| 10 | CM-64 | Contestable deliveries | Built, not in main (reverted) | CM-85 |
| 11 | CM-36 | Cleanse corruption at the shrine | Built, not in main (held) | CM-85 |
| 12 | CM-39 | Builder part 1: biomass upgrade ladder | Built, not in main (held) | CM-35 |
| 13 | CM-84 | Base stations: swap class and upgrade | New | CM-39 |
| 14 | CM-58 | Builder part 2: ladders and bridges | New | CM-39, CM-30 |
| 15 | CM-59 | Any class carries built pieces | New | CM-58 |
| 16 | CM-66 | Command-line size and pace (the 16v16 / 2x defaults are F-01's) | Built, not in main (held) | F-08a, F-08b, F-08c, F-09 |
| 17 | CM-65 | Free camera | Landed (view only) | F-08a, F-08b, F-08c |
| 18 | CM-68 | Maps B (Ember Orchard) and C (Tidal Crown) | Built, not in main (held) | F-03, F-04, F-06b |
| 19 | CM-67 | The combat choice | Held (team has not picked) | the team's pick |
| - | CM-55 | Engine port spike | Held | closed: this repo is the port |

Travis's must-haves: miners digging and placing ground is **CM-30**, knock-down structures is **CM-31**, jumping is
**CM-32**, diagonal bridge crossing is **CM-33**. Those four come first.

Old ids that are not on your list: **CM-54** (class switch on the death screen) is superseded by CM-84, which ports its
cooldown. **CM-56** (five-class roster) is part of F-01 and F-02b. **CM-61** (the look of dug holes and placed blocks) is
covered by the chunk meshes in F-08b and later by the loop's art.

---

## 3. Foundation: built by the loop first, not yours

| Id | What the loop builds | Old source (lines) |
| --- | --- | --- |
Foundation ids are split into sub-items; the item specs are `loop/items/F-*.md` and their order is
`loop/items/_ORDER.md`. F-02 = F-02a, F-02b; F-05 = F-05a..F-05j; F-06 = F-06a..F-06f; F-08 = F-08a, F-08b, F-08c.
"F-01 to F-08c" means every foundation item up to and including F-08c (F-09 excluded).

| Id | What the loop builds | Old source (lines) |
| --- | --- | --- |
| F-01 | `GameConfig` with every tunable 1:1, five class rows, palette, plus its tests; `CreateDefault()` is 16v16 at 2x (U-004, `TeamSize` 16, `DefaultSpeed` 2), `CreateBrowser5v5()` the old main defaults for ported tests | `src/config/game.config.ts` (426), `game.config.test.ts` (91), `palette.ts` (121); `balance-table.test.ts` (120) is not ported |
| F-02a, F-02b | Core types, seeded RNG, `JsMath`, the Sim boundary guard; `World` (create, reset, roster, events) and `MapGrid.SetCell` | `types.ts` (309), `rng.ts` (38), `boundary.test.ts` (70), `world.ts` (667), `setCell` of `grid.ts`, and their tests |
| F-03 | Ground grid with Sim-owned heights and the Skybreak Basin map (with the CM-31 decoration data) | `map.ts` (364), `grid.ts` (85), `map.test.ts` (194), `grid.test.ts` (47) |
| F-04 | Flow field and spatial hash, plus the map route tests | `flowfield.ts` (191), `spatialHash.ts` (125) and tests |
| F-05a .. F-05j | Base rules: input, movement, collision, combat, damage, abilities, barricades, plant biomass and corruption runs, the debug feed, Kaiju, respawn, and the 30 Hz `SimStep`, hill scoring, match clock and winner | `input.ts` (41), `movement.ts` (114), `collide.ts` (190), `combat.ts` (143), `damage.ts` (100), `abilities.ts` (161), `barricades.ts` (328), `resources.ts` (245), `debug.ts` (44), `kaiju.ts` (543), `respawn.ts` (194), `step.ts` (46), `hill.ts` (54), `match.ts` (31) and tests |
| F-06a .. F-06f | Bots: state, fields, steering, state machine, director, `MatchTick`, the bot match tests | `src/bots/*.ts` except outcomes (2,100 non-test), `bots.test.ts` (548) |
| F-07 | `Controls.inputactions` with the table above, the human input state, and `ProjectSetup.Apply` assigning the project-wide actions | `src/game/humanInput.ts` (292) for behaviour |
| F-08a, F-08b, F-08c | Placeholder view built in code when Boot loads (no new scene, U-027): the Sim driver (30 Hz accumulator, interpolation), `SimSpace` axis conversion, team-coloured capsules, grid chunk meshes, the Cinemachine follow camera, a plain debug HUD (clock, hill seconds, Kaiju instability, team biomass), the PlayMode smoke test | `src/game/SimDriver.tsx`, `CameraRig.tsx`, `rig.ts` for behaviour |
| F-09 | Headless match runner for soak tests (N seeds, team size, bot / passive / wandering actor 0) inside `Sim.Tests`, and the 14 outcome bands (`[Category("Slow")]`, U-028) | `src/bots/outcomes.ts` (171), `outcomes.thresholds.ts` (48), `outcomes.slow.test.ts` (215) |

**Foundation exit (U-012).** Phase 0 is done when a full 16v16 bot match plays in grey-box and every ported test
passes. Phase 1 (your items, Travis's four must-haves first) and Phase 2 (the look) then run side by side. Phase 1 ends
when Travis's four must-haves pass a play test; Phase 2 when every bible row passes; Phase 3 when Travis has a working
build for his platform (U-024).

Camera values for F-08b from the old config: yaw 45°, pitch 38°, orthographic, 20 world units tall
(`viewHeightUnits`), distance 60, follow sharpness 8, target clamped to the map bounds inset by 6.

Your items call the foundation by its class names (`GameConfig`, `World`, `SimStep`, `MapGrid`, `FlowField`,
`BotFsm`). If a foundation PR lands with different names, follow the code.

---

## 4. The items

Every item lists: what the player sees, the old files to port (with line counts), the C# files to create, the
tunables with their values, how it is checked, and what is out of scope. New Sim files go in `Assets/_Game/Sim/Rules/`,
tests in `Assets/_Game/Sim.Tests/`, view code in `Assets/_Game/Runtime/View/`, namespaces `CriticalMass.Sim.<Area>` (the
foundation uses `Config`, `Core`, `Grid`, `Maps`, `Bots`; new files in `Sim/Rules/` use `CriticalMass.Sim.Rules`), tests
`CriticalMass.Sim.Tests`, view `CriticalMass.Runtime.<Area>`.

### CM-32 Jumping (Travis must-have)

Old state: landed (D-062). Waits on F-01 to F-08c.

**What the player sees.** Press Space (south button) and your capsule hops in a short arc, keeping the speed and
direction it had at takeoff. You can hop a one-cell gap. A jump that would land you in a wall or a hole is shortened, or
refused if even the shortest jump would land badly, so you never fall to your death by accident.

**Port.** `src/sim/jump.ts` (113 lines), `src/sim/jump.test.ts` (181 lines, 6 cases). Uses `collide.ts` and `world.ts`
from the foundation.

**Create.** `Assets/_Game/Sim/Rules/JumpSystem.cs` (static class `JumpSystem`), `Assets/_Game/Sim.Tests/JumpTests.cs`
(class `JumpTests`). Runtime: `Assets/_Game/Runtime/View/ActorJumpLift.cs` (`ActorJumpLift`, lifts the capsule by the
Sim's jump height each frame).

**Tunables.** `jump.airTicks` 18, `jump.apexUnits` 0.8, `jump.minAirTicks` 6; a shortened jump of k ticks lifts
`apexUnits × (k / airTicks)²`.

**Check.** `JumpTests` has the 6 ported cases with the browser's numbers and passes. Play check in the Boot scene (the arena is built in code, U-027): run at a
one-cell void gap and jump, you land on the far side; jump at a wall, the jump is shortened or refused and you never end
up in the void; hold Space, you jump once.

**Out of scope.** Air control, double jump, jumping up onto higher cells (cell height is not a rule yet), any bot jumping
(bots do not jump, as in the browser).

### CM-33 Cross bridges at any angle (Travis must-have)

Old state: built on branch `cm/CM-33` (commit `003c970`), landed in main twice on 2026-11-09 and pulled out both times
during batch integration over a render check, not over the rule. D-060 was dropped from DECISIONS.md with it; read the
branch's own DECISIONS.md entry and commit messages. Waits on F-01 to F-08c.

**What the player sees.** Walk onto any bridge, including the diagonal ones, from any angle up to 60° off its axis, and
you slide along it to the far side without lining up first, without sticking on the rail and without dropping into the void.

**Port** (from `cm/CM-33`). `src/sim/collide.ts` (391 lines on the branch; +212 over main), the bridge-span part of
`src/sim/map.ts` (+77; `BRIDGE_SPANS` and the span axis), `src/sim/bridges.test.ts` (260 lines, 6 cases).

**Create.** `Assets/_Game/Sim/Rules/BridgeSpans.cs` (`BridgeSpans`), the bridge rail inside the foundation's
`Assets/_Game/Sim/Collide.cs`, `Assets/_Game/Sim.Tests/BridgeTravelTests.cs` (`BridgeTravelTests`).

**Tunables.** None new. Uses class `radius` (Warrior 0.45, Farmer 0.4, Miner 0.45) and `moveSpeed` (3.8, 4.2, 4.0),
`movement.accelSeconds` 0.12, `movement.brakeSeconds` 0.08, and the map's bridge half width 1.5.

**Check.** `BridgeTravelTests` passes all 6: 16 spans with a chasm part; free lateral band at least 2.1 for radius 0.45
over the middle 70 % of every span; the nearest 8-way key from 5 lateral offsets arrives with no stall tick and no void;
constant input from −60° to +60° in 15° steps arrives; the same crossing twice lands on the same position and tick (see
the open note on float precision); the deck height lies between the span's end heights. Play check: cross every
diagonal bridge on Skybreak with keyboard and with the left stick at an angle.

**Out of scope.** Bridge art, bot steering changes (bots already use the flow field), new bridges.

**Standing note (Jeisil's fix-first 01).** While doing CM-32 and CM-33, play the movement and write down each movement
bug with steps in your PR. Fix the ones inside these two items; list the rest for the architect.

### CM-30 Miners dig and place ground (Travis must-have)

Old state: landed (D-059). Waits on F-01 to F-09.

**What the player sees.** As a Miner, press F (left trigger) at the ground in front of you: the cell drops away into a
hole and your capsule carries a small block. Press again over a hole or water next to solid ground and the block fills
it. The ground mesh changes at once and bots re-route around holes. You cannot dig the last route from a base to the hill.

**Port.** `src/sim/terraform.ts` (361), `src/sim/terraform.test.ts` (350, 11 cases), `src/sim/terraform.slow.test.ts`
(117, 1 case: 16v16, seed 101, 14,400 ticks). Rules: old DECISIONS.md D-059.

**Create.** `Assets/_Game/Sim/Rules/TerraformSystem.cs` (`TerraformSystem`, with the `TerraformRefused*` codes),
`Assets/_Game/Sim.Tests/TerraformTests.cs` (`TerraformTests`), `Assets/_Game/Sim.Tests/TerraformSoakTests.cs`
(`TerraformSoakTests`, `[Category("Slow")]`). Runtime: `Assets/_Game/Runtime/View/CarriedBlockView.cs`
(`CarriedBlockView`, a small cube on the carrier). Chunk meshes already rebuild from the grid (F-08b).

**Tunables.** `abilities.minerTerraform`: `reach` 1.6, `teamCooldownTicks` 30, `minGapTicks` 15, `actorMargin` 0.05.
Keep-outs as on main: pads 4, hill radius 6, shrine 3, nodes 1.

**Rules to keep.** Dig turns walkable floor or bridge into void and you carry one block; place turns void or water with a
walkable side neighbour into floor (never bridge); refused under any living actor or the hatched Kaiju, inside keep-outs,
and when it would cut a pad from the hill (or newly from the shrine); one change per team per 30 ticks and none within 15
ticks of any wall change; a dead Miner loses the block. Every outcome is a `terraform` event.

**Decided (U-007).** Dig/place keeps the browser's one-key carry rule: F (left trigger) digs with empty hands and places
while carrying. There is no dig/place mode; the "Switch dig / place" action in the controls table is not read by this
item. **Decided (U-023):** that row stays bound but unread.

**Check.** `TerraformTests` (11 cases) and `TerraformSoakTests` (1 case) pass with the browser's numbers. Play check: as
a Miner dig three cells in a row, watch the mesh drop and a bot path around; refill one; try to dig the last cell of the
only route and see it refused.

**Out of scope.** Bot digging beyond what the ported files already do, hole and block art (the loop's), changing the rate cap. If dig does not feel right in
play (reach, rate), write it in the PR for the architect, do not change the numbers in this item.

### CM-31 Knock-down structures (Travis must-have)

Old state: landed (D-065, CM-31 entry). Waits on CM-30 (it reuses the dig route guard).

**What the player sees.** Tall pillars stand at fixed spots on Skybreak. Hit one three times with any class and it falls
away from you: one way it becomes a ramp onto an overlook ledge, the other way it walls off a path. Bots re-route.

**Port.** `src/sim/topple.ts` (292), `src/sim/topple.test.ts` (225, 10 cases). The decoration positions come with the
map in F-03.

**Create.** `Assets/_Game/Sim/Rules/ToppleSystem.cs` (`ToppleSystem`), `Assets/_Game/Sim.Tests/ToppleTests.cs`
(`ToppleTests`). Runtime: `Assets/_Game/Runtime/View/DecorationView.cs` (`DecorationView`, a tall cube that tips
over across `fallTicks`).

**Tunables.** `abilities.topple`: `hitsToTopple` 3, `lengthCells` 4, `widthCells` 3, `fallTicks` 18, `reach` 0.5.

**Check.** `ToppleTests` (10 cases) pass. Play check: knock one pillar toward the ledge and walk up the ramp; knock its
mirror the other way and see the path blocked and a bot go around.

**Out of scope.** Pillar art, new decoration spots, rebuilding a fallen pillar.

### CM-37 Base healing fountain

Old state: landed (batch 2, old PR #18; D-061, D-068). Waits on F-01 to F-09.

**What the player sees.** Stand hurt on your own base pad and your health climbs back to full; an enemy standing there
does not heal. Bots below 30 % health with an enemy near run home and heal.

**Port.** `src/sim/heal.ts` (41), `src/sim/heal.test.ts` (202, 9 cases), `src/sim/heal.slow.test.ts` (134, 1 case:
5v5, seeds 1 to 100). The bot side reads `heal.seekHpShare` through the foundation's `BotFsm` (old `LOW_HP`).

**Create.** `Assets/_Game/Sim/Rules/HealSystem.cs` (`HealSystem`, runs after respawn in the step order),
`Assets/_Game/Sim.Tests/HealTests.cs`, `Assets/_Game/Sim.Tests/HealSoakTests.cs` (`[Category("Slow")]`).

**Tunables.** `heal.hpPerTick` 1, `heal.zoneRadius` 3.75, `heal.seekHpShare` 0.3.

**Check.** `HealTests` (9) and `HealSoakTests` (1) pass; the soak's recorded line in old BALANCE.md "Healing fountain"
holds within the test's own tolerance. Play check: take damage, walk home, watch the HUD health rise 30 per second.

**Out of scope.** Fountain art, a healer class, flasks.

### CM-35 Two kinds of biomass

Old state: landed (D-063). Waits on F-01 to F-09.

**What the player sees.** Plants around the map still give biomass. A killed actor (while the Kaiju is an egg) drops a
meat orb you grab by walking over it; it rots after 20 seconds. Meat rides separately from your cargo and pays at the
shrine.

**Port.** The meat-orb parts of `src/sim/resources.ts` (245) and `spawnLooseOrb` in `world.ts`,
`src/sim/biomass.test.ts` (436, 17 cases), `src/sim/biomass.slow.test.ts` (129, 2 cases: 5v5, seeds 1 to 100).

**Reopens** (each at its `// ITEM CM-35` marker; the browser files with CM-35 code are types.ts, world.ts, combat.ts,
resources.ts and bots/index.ts): `Assets/_Game/Sim/Core/SimEvent.cs` (`Bio`), `Core/Actor.cs` (`CarryMeat`,
`MeatPickup`), `Core/ResourceNode.cs` (`LooseOrb`), `Core/MatchState.cs` (`DeliveredTally.Plant`, `Meat`),
`Core/SimEnums.cs` (`BiomassType`), `Core/World.cs` (`Orbs`, `MeatDropped`, `OrbOverflows`, `SpawnLooseOrb`,
`OrbCapacity`), `Assets/_Game/Sim/Combat.cs` (corpse meat drop), `Assets/_Game/Sim/Resources.cs` (`bio`, orb ageing,
pickup, meat delivery), and `Assets/_Game/Sim/Bots/Bots.cs` (Kaiju-role meat pickup).

**Create.** `Assets/_Game/Sim/Rules/MeatOrbs.cs` (`MeatOrbs`), `Assets/_Game/Sim.Tests/BiomassTests.cs`,
`Assets/_Game/Sim.Tests/BiomassSoakTests.cs` (`[Category("Slow")]`). Runtime: `Assets/_Game/Runtime/View/OrbView.cs`.

**Tunables.** `kaijuFeed.biomassAllegiancePerUnit` 10, `kaijuFeed.meatAllegiancePerUnit` 6,
`resources.meatUnitsPerKill` 2, `meatOrbLifetimeTicks` 600, `meatPickupRange` 1.2, `meatCarryCapacity` 2.

**Check.** `BiomassTests` (17) and `BiomassSoakTests` (2) pass. Play check: kill an enemy, walk over the orb, deliver it.

**Out of scope.** Meat and plant art (the loop's).

### CM-34 Slower corruption

Old state: landed (D-056). Waits on CM-35 (its soak plays full matches with both biomass kinds).

**What the player sees.** The Kaiju turns berserk later: it now takes 12 corruption units instead of 10.

**Port.** The rate is one config value. `src/sim/corruption.slow.test.ts` (314, 11 cases: 5v5, seeds 1 to 100, actor 0
on a bot).

**Create.** `Assets/_Game/Sim.Tests/CorruptionSoakTests.cs` (`[Category("Slow")]`).

**Tunables.** `kaijuFeed.corruptionInstabilityPerUnit` 9, `kaijuFeed.berserkThreshold` 100,
`kaijuFeed.corruptionAllegiancePerUnit` 4.

**Check.** `CorruptionSoakTests` (11) pass, including the berserk share inside 10 to 50 %. Play check: the debug HUD
instability rises 9 per corruption unit delivered.

**Out of scope.** Corruption spread art on the map (the loop's).

### CM-85 Corruption also builds from biomass (Travis must-have 2)

Old state: landed (batch 2, old PR #18; D-068 CM-85 entry). Waits on CM-34.

**What the player sees.** Every biomass delivery, plant or meat, also nudges Kaiju instability up a little.

**Port.** The `biomassInstabilityPerUnit` line of `applyDelivery` in `src/sim/resources.ts` (CM-85 commit `3807f98`
touches game.config.ts, resources.ts, debug.test.ts, kaiju.test.ts and the corruption tests; no kaiju.ts code),
`src/sim/corruption.test.ts` (149, 10 cases). The tunable is already in `GameConfig` (F-01).

**Reopens** (each at its `// ITEM CM-85` marker): `Assets/_Game/Sim/Resources.cs` (`ApplyDelivery`),
`Assets/_Game/Sim.Tests/DebugTests.cs` (`+ 7 * BiomassInstabilityPerUnit`), `Assets/_Game/Sim.Tests/KaijuResourceTests.cs`
(`+ 2 * BiomassInstabilityPerUnit`).

**Create.** `Assets/_Game/Sim.Tests/CorruptionRateTests.cs` (`CorruptionRateTests`); the rule goes into the
foundation's `Resources.cs` delivery path.

**Tunables.** `kaijuFeed.biomassInstabilityPerUnit` 0.125 (exactly 1/8, so it adds without rounding in a double too).

**Check.** `CorruptionRateTests` (10) pass and `CorruptionSoakTests` still pass. Play check: deliver 8 plant units,
instability rises by exactly 1.

**Out of scope.** Tuning the rate (record any change for the architect).

### CM-38 Demolitions Expert

Old state: landed (D-068 CM-38 entry). Waits on CM-30 (craters use the dig rules).

**What the player sees.** The Demolitions Expert has no normal attack. The class ability drops a land mine at the aimed
cell that blows up on the first enemy to step on it. The dig/place action throws TNT that explodes after 1.5 seconds,
hurting enemies, breaking walls and cratering the ground (never the last route to the hill). Bots play the class in
soak tests.

**Port.** `src/sim/demolitions.ts` (525), `src/sim/demolitions.test.ts` (362, 7 cases), `src/sim/demolitions.slow.test.ts`
(145, 1 case: 16v16, seeds 1 to 100), and `decideDemolitions` in `src/bots/fsm.ts` plus the TNT press in
`src/bots/index.ts`.

**Create.** `Assets/_Game/Sim/Rules/DemolitionsSystem.cs` (`DemolitionsSystem`), `Assets/_Game/Sim.Tests/DemolitionsTests.cs`,
`Assets/_Game/Sim.Tests/DemolitionsSoakTests.cs` (`[Category("Slow")]`). Runtime: `MineView.cs`, `TntView.cs` in
`Runtime/View/` (cubes).

**Tunables.** `abilities.demolitionsMine`: `placeRange` 1.6, `maxActivePerOwner` 2, `armTicks` 15, `damage` 70,
`blastRadius` 1.2, `lifetimeTicks` 1800. `abilities.demolitionsTnt`: `throwRange` 5, `fuseTicks` 45, `blastRadius` 2,
`damage` 55, `carveRadius` 1, `cooldownTicks` 150. `classes.demolitions.abilityCooldown` 12 (the row is otherwise the
Miner's copy). `bots.classMix.demolitions` stays 0.

**Decided (U-007).** TNT is thrown on F / left trigger (the "Dig or place (Miner)" action), as the browser did with F.
Build the rule with a `ThrowTnt` command and bind it to that action for the Demolitions Expert.

**Check.** `DemolitionsTests` (7) and `DemolitionsSoakTests` (1) pass. Play check: lay a mine, lure a bot over it; throw
TNT at a bridge and see one column carved while the last route survives.

**Out of scope.** Class model and icons (the loop's). Making the class "feel distinct" from the Miner (Wilson's
complaint) is a design follow-up: write your ideas in the PR, the rules here stay the browser's.

### CM-64 Contestable deliveries

Old state: built on branch `cm/CM-64` (`e056d8e`), landed in batch 1 and reverted, then held (D-067 CM-64 entry is on
the branch). Waits on CM-85.

**What the player sees.** Delivering at the shrine now takes a one-second wind-up while you hold E. An enemy hit during
the wind-up stops it and it starts again; you keep your cargo unless you die. Defender bots near the shrine go after
carriers.

**Port** (from `cm/CM-64`). `src/sim/delivery.ts` (69), `src/sim/delivery.test.ts` (328, 11 cases),
`src/sim/delivery.slow.test.ts` (188, 6 cases), the hit hook in `damage.ts` (+4), the delivery path in `resources.ts`
(+12), `interceptTarget` in `src/bots/fsm.ts` (+61) and `src/bots/director.ts` (+22). The branch is 44 commits behind
main: port the rule, do not merge the branch.

**Create.** `Assets/_Game/Sim/Rules/DeliverySystem.cs` (`DeliverySystem`), `Assets/_Game/Sim.Tests/DeliveryTests.cs`,
`Assets/_Game/Sim.Tests/DeliverySoakTests.cs` (`[Category("Slow")]`). Adds `Intercepted` per team to match state.

**Tunables** (from the branch's config). `kaijuFeed.deliverWindupTicks` 30, `bots.interceptShrineRadius` 11.

**Check.** `DeliveryTests` (11) and `DeliverySoakTests` (6) pass: an unhit carrier delivers after exactly 30 ticks; both
teams have intercepted deliveries over seeds 1 to 100; hatch and berserk shares stay inside the old CM-19 thresholds
(`src/bots/outcomes.thresholds.ts`). Play check: hit a carrying bot at the shrine and watch the delivery fail.

**Out of scope.** A wind-up meter in the HUD.

### CM-36 Cleanse corruption at the shrine (Travis must-have 2)

Old state: built on branch `cm/CM-36` (`1c6ce00`), held. Waits on CM-85.

**What the player sees.** At the shrine you can cleanse: Kaiju instability drops by a set amount over three seconds,
then the cleanse is on a long cooldown. The debug HUD instability number visibly falls. Bots use it.

**Port** (from `cm/CM-36`). `src/sim/cleanse.ts` (140), `src/sim/cleanse.test.ts` (337, 13 cases),
`src/sim/cleanse.slow.test.ts` (119, 3 cases), the hooks in `kaiju.ts` (+4), `src/bots/fsm.ts` (+38),
`src/bots/steer.ts` (+19). The branch is 44 commits behind main: port the rule only.

**Create.** `Assets/_Game/Sim/Rules/CleanseSystem.cs` (`CleanseSystem`), `Assets/_Game/Sim.Tests/CleanseTests.cs`,
`Assets/_Game/Sim.Tests/CleanseSoakTests.cs` (`[Category("Slow")]`).

**Tunables** (the branch's `kaijuFeed.cleanse` block). `units` 2, `ticks` 90, `cooldownTicks` 2700, `holdTicks` 15,
`botNetUnits` 10. These are on the branch only, not on old main; port them as they are.

**Check.** `CleanseTests` (13) and `CleanseSoakTests` (3) pass; 5v5 over seeds 1 to 100 keeps the berserk share between
10 and 50 %. Play check: cleanse at the shrine and watch instability fall.

**Out of scope.** The cleanse effect art (keep any placeholder to a plain flash).

### CM-39 Builder part 1: biomass upgrade ladder

Old state: built on branch `cm/CM-39` (`b37bb1c`, up to date with old main), held. Waits on CM-35.

**What the player sees.** The team banks delivered biomass. Spending it raises an ally's class level by one (two
levels above the start), which raises their max health and heals the gain.

**Port** (from `cm/CM-39`). `src/sim/upgrade.ts` (148), `src/sim/upgrade.test.ts` (268, 11 cases), the bank credit
in `resources.ts` (+6), the level hook in `respawn.ts` (+6), `types.ts` (+17), `world.ts` (+5).

**Create.** `Assets/_Game/Sim/Rules/UpgradeSystem.cs` (`UpgradeSystem`, `MaxClassLevel`), `Assets/_Game/Sim.Tests/UpgradeTests.cs`.
Adds `BiomassBank` per team to match state.

**Tunables** (from the branch). `abilities.builderUpgrade`: `range` 3, `costBiomass` [3, 5], `maxHpBonus` [0.15, 0.3],
`refuseCooldownTicks` 15.

**Decided (U-007).** Upgrades happen at base stations (CM-84), not through the Builder's class ability, which builds
ladders and bridges (CM-58). The branch's "hold the ability near an ally" spending is not ported. Build the ladder, the
bank and the cost and health math behind an `UpgradeSystem.TryUpgrade(world, buyer, target)` call; CM-84's station
panel calls it. **Decided (U-023):** every class buys its own upgrades at the station, so `buyer` and `target` are the
same actor.

**Check.** `UpgradeTests` (11) pass (target choice and refusal cases follow the answer above; keep every case the
answer does not touch).

**Out of scope.** Badge art, an upgrade tree (D-067: the ladder is linear).

### CM-84 Base stations: swap class and upgrade

Old state: new (D-067 item 5, supersedes D-053). Waits on CM-39.

**What the player sees.** Inside your own base station you can switch class and spend team biomass to go up one level.
Outside it, or at the enemy's station, you cannot. Switching keeps your biomass, score and kills; it has a 5 second
cooldown. The death screen no longer offers a class switch.

**Port.** The class-switch cooldown from old main (CM-54): `src/sim/respawn.ts` (194) class-pick block and its cases in
`src/sim/respawn.test.ts` (379), rule detail in the D-053 addendum. New files are yours; there is no old code for stations.

**Create.** `Assets/_Game/Sim/Rules/StationSystem.cs` (`StationSystem`), `Assets/_Game/Sim.Tests/StationTests.cs`
(`StationTests`). Runtime: `Assets/_Game/Runtime/View/StationPanel.cs` (plain uGUI or UI Toolkit placeholder).

**Tunables.** `respawn.classSwitchCooldownTicks` 150 (switch at tick T refused through T+149, accepted at T+150).
Upgrade costs from CM-39.

**Decided (U-007).** The station is your own base pad, radius 3.75; pressing E inside it opens the station panel. Build
the rules against a `StationZone` read from the map (centre = own base pad, radius 3.75).

**Check.** `StationTests`: switch accepted inside own station, refused outside and at the enemy station, refused within
150 ticks, biomass, score and kills unchanged; upgrade refused outside, without biomass and at the top level; the ported
CM-54 cooldown cases pass. Play check: walk into your station, switch to Farmer, try again at once and see it refused.

**Out of scope.** Station art, bots switching class (they never did).

### CM-58 Builder part 2: ladders and bridges

Old state: new, no code. Waits on CM-39 (team bank) and CM-30 (route guard). The loop's Builder look item waits on this,
so do it early.

**What the player sees.** A Builder spends team biomass to turn a gap or wall cell into a walkable piece; players and bots
re-route over it; the piece disappears after a time or when broken; a piece can never seal the only route to anything.

**Port.** No old code. Reuse the dig route guard from CM-30 and the barricade lifetime and health pattern from
`src/sim/barricades.ts` (328).

**Create.** `Assets/_Game/Sim/Rules/BuildSystem.cs` (`BuildSystem`), `Assets/_Game/Sim.Tests/BuildTests.cs`,
`Assets/_Game/Sim.Tests/BuildSoakTests.cs` (`[Category("Slow")]`).

**Decided (U-007).** The Builder's class ability (right mouse / right trigger) places a piece. Cost 2 biomass, lifetime
600 ticks, 120 HP, at most 2 per Builder. These numbers are **provisional** and are marked provisional in `GameConfig`.
**Decided (U-023):** a ladder is a one-step piece joining two cell heights.

**Check.** `BuildTests`: a piece makes a cell walkable, the flow field routes over it, it expires and breaks on schedule,
and a piece that would seal the only route is refused. `BuildSoakTests`: 16v16 over seeds 1 to 100 reports pieces built
per match and every spawn pad keeps a route. Play check: bridge a gap as a Builder and watch a bot use it.

**Out of scope.** Ladder and bridge meshes (the loop's).

### CM-59 Any class carries built pieces

Old state: new. Waits on CM-58.

**What the player sees.** Any class presses E on a built piece to pick it up (the ground goes back to what it was),
carries one at a time, and presses E at a valid gap or wall edge to put it down. The never-seal-the-only-route rule holds
on pickup and on drop.

**Port.** No old code.

**Create.** `Assets/_Game/Sim/Rules/CarrySystem.cs` (`CarrySystem`), `Assets/_Game/Sim.Tests/CarryTests.cs`.

**Decided (U-007).** Picking up a piece needs empty hands (no biomass, meat or block). A carried piece drops when you
die if the cell is valid; otherwise it is destroyed (U-023).

**Check.** `CarryTests`: pickup restores the cell, one piece at a time, drop only on a valid edge, route rule on both
pickup and drop. Play check: move a bridge from one gap to another.

**Out of scope.** Carry animation.

### CM-66 Default 16v16 and 2x pace

Old state: built on branch `cm/CM-66` (`8083906`), held. Waits on F-08a, F-08b, F-08c and F-09.

**What the player sees.** A match with no options is 16 against 16 and runs at double speed: the 8:00 match clock (sim
time) plays in 4:00 of real time. The Sim still ticks at 30 Hz; the driver runs two ticks per frame slot.

**Already in the foundation (F-01).** `GameConfig.CreateDefault()` is 16v16 at 2x (`TeamSize` 16, `DefaultSpeed` 2,
U-004) and `GameConfigTests.DefaultIs16v16At2xPace` pins it; the F-08a driver plays that default; ported tests use
`GameConfig.CreateBrowser5v5()`. What is left for this item: the command-line arguments and the pause-menu size and
speed options.

**Port** (from `cm/CM-66`). Behaviour only: the `?size=` / `?speed=` parsing in `src/game/params.ts` and
`params.test.ts` becomes command-line parsing.

**Create.** `Assets/_Game/Runtime/Game/CommandLine.cs` (reads `-teamSize` and `-speed` into the driver's `GameConfig`
before `World.CreateWorld`), `Assets/_Game/Runtime.Tests/CommandLineTests.cs`.

**Decided (U-004).** 16v16 (1 human + 31 bots) at 2x is the default. 5v5 and 1x remain as options in the pause menu and
as command-line arguments. Match length 8 minutes. Frame budget: 60 fps at 16v16 in the Mac build on this Mac.
**Decided (U-023):** the command-line arguments are `-teamSize` and `-speed` (and `-map`, CM-68). **OPEN:** whether a
size change from the pause menu restarts the match.
Land this item in its own PR: it touches many tests.

**Check.** `CommandLineTests` pass and every test still passes. Play check: a build started with no arguments has 32
capsules and the clock runs
at twice real time.

**Out of scope.** Performance work beyond reporting the frame time you measure.

### CM-65 Free camera

Old state: landed in the browser as a view-only change (D-067 CM-65 entry): mouse at a screen edge pans, middle drag
grabs the ground, C or a respawn snaps back. Wilson could not find it. Waits on F-08a, F-08b, F-08c.

**Decided (U-005).** Free camera is out of the first playable. Do not build it. The camera is orthographic, yaw 45°,
pitch 38°, 20 world units tall, never rotates, damped-spring follow with 0.25 s look-ahead capped at 2 units.

**Port if it is brought back later.** Behaviour from old `src/game/CameraRig.tsx` (612) and `src/game/rig.ts` (160). Create
`Assets/_Game/Runtime/View/CameraFreePan.cs`. Check: Wilson finds and uses it without being told.

### CM-68 Maps B (Ember Orchard) and C (Tidal Crown)

Old state: built on branch `cm/CM-68` (`572927e`), held. Waits on F-03, F-04 and F-06b.

**What the player sees.** Two more maps from the bible's page 12. Skybreak stays the default.

**Port** (from `cm/CM-68`). The two layouts in `src/sim/map.ts` (560 lines on the branch, +233), `src/sim/map.test.ts`
(362, 20 cases), `src/bots/maps.slow.test.ts` (111, 1 case).

**Create.** `Assets/_Game/Sim/Maps/EmberOrchardMap.cs`, `Assets/_Game/Sim/Maps/TidalCrownMap.cs`, `MapId` in the Sim,
`Assets/_Game/Sim.Tests/MapLayoutTests.cs`, `Assets/_Game/Sim.Tests/MapSoakTests.cs` (`[Category("Slow")]`).

**Decided (U-023).** A map is picked with the `-map` command-line argument and in the pause menu, the same pattern as
team size and pace (the browser used `?map=ember|tidal`). Skybreak stays the default.

**Check.** `MapLayoutTests` (20) and `MapSoakTests` (1) pass: both bases, the hill and the shrine reachable on the flow
field; base-to-hill path lengths within 5 % for the two teams; a 10-seed headless match on each map plays to the clock
with no stuck bot.

**Out of scope.** Landmarks and materials (the loop's), new scenes.

### CM-67 The combat choice

**Decided (U-007): this stays a team pick.** Old state: held. The team has not picked parry, heavy versus light attacks, or both (D-067 item 6). Max and Jeisil pick,
Wilson's pick goes into the decisions log, then Max builds it. Nothing to build until then. Whatever is picked must fit
the controls table without a new binding.

### CM-55 Engine port spike

Old state: held until after the demo. Closed: moving to this Unity repo is the port.

---

## 5. Art

No art item is assigned to you here. You have been modelling a hero character in Blender: please push the `.blend` to
`Assets/_Game/Art/Characters/_source~/` (the `~` keeps Unity from importing it) and its FBX export to
`Assets/_Game/Art/Characters/` on a branch (for example `max/art-hero-source`) and open a PR,
so it can be checked against the asset bible's Warrior page (page 6). Both file types already go through Git LFS.
