# Phase 0 porting conventions (read before any F-item)

Drafted 2026-11-11; reviewed the same day against origin/main 09e0904 and the installed packages, architect rulings
applied (`_OPEN.md`). Every F-item spec says "Conventions apply"; this file is that text. An
item spec overrides this file where the two differ. Anything here that was a free choice is listed in `_OPEN.md`.

## 1. Source of truth

- Browser repo: `/Users/wilsonguenther/critical-mass`, commit `09e0904` (2026-11-09, "cm batch 2").
  Read a file ONLY with `git -C /Users/wilsonguenther/critical-mass show 09e0904:<path>` (pinned sha, not a moving
  branch). Never `cat`, `ls` or `grep` the `~/critical-mass` working tree: it is checked out at an older commit
  (9a29d71, 149 commits behind, with local edits on 2026-11-12) and its `src/` differs from the oracle in game.config.ts,
  types.ts, map.ts and world.ts (panel revision 2026-11-12, P-05). Read only: never check out, pull, edit, commit there.
- Never read another item's lane under `~/critical-mass-unity-lanes/`. Items of the same batch are built at the same
  time from origin/main; their code is not in your lane and may be half-written in theirs. The contract between items
  is the upstream item file: for every `waits_on` id that has not landed, read `loop/items/<ID>.md` (its Create, API and
  signature tables) and section 11 below, and call exactly those signatures, verbatim. Never stub, redeclare or guess an
  upstream member. An upstream type missing from your lane is expected and is NOT an open question; a signature the
  upstream spec does not give IS one (P-06).
- Port rule for rule, line for line. Private helpers and private constants of a TS file become `private static`
  members of the C# class that ports the file, same name in PascalCase. Doc comments are carried as one-line `///`
  summaries; the long reasoning comments are not copied (they stay in the browser repo).

## 2. Folders, assemblies, namespaces

| Browser file | C# folder | Namespace |
| --- | --- | --- |
| `src/config/game.config.ts`, `palette.ts` (PALETTE only) | `Assets/_Game/Sim/Config/` | `CriticalMass.Sim.Config` |
| `src/sim/types.ts`, `rng.ts`, `world.ts`, `step.ts`, `input.ts`, `hill.ts`, `match.ts`, `debug.ts` | `Assets/_Game/Sim/Core/` | `CriticalMass.Sim.Core` |
| `src/sim/movement.ts`, `collide.ts`, `combat.ts`, `damage.ts`, `abilities.ts`, `barricades.ts`, `resources.ts`, `kaiju.ts`, `respawn.ts` | `Assets/_Game/Sim/` (root: MAX-ITEMS names `Assets/_Game/Sim/Collide.cs` and `SimResources.cs`, U-031) | `CriticalMass.Sim.Core` |
| `src/sim/grid.ts`, `flowfield.ts`, `spatialHash.ts`, the `MapDef` type of `map.ts` | `Assets/_Game/Sim/Grid/` | `CriticalMass.Sim.Grid` |
| `src/sim/map.ts` (Skybreak Basin layout) | `Assets/_Game/Sim/Maps/` | `CriticalMass.Sim.Maps` |
| `src/bots/*.ts` (non-test) | `Assets/_Game/Sim/Bots/` | `CriticalMass.Sim.Bots` |
| every ported vitest file | `Assets/_Game/Sim.Tests/` | `CriticalMass.Sim.Tests` |
| soak harness (F-09) | `Assets/_Game/Sim.Tests/Harness/` | `CriticalMass.Sim.Tests.Harness` |
| Sim-to-Unity axis conversion (`SimSpace`, F-08a) | `Assets/_Game/Runtime/SimSpace.cs` | `CriticalMass.Runtime` |
| Runtime view and input | `Assets/_Game/Runtime/<Area>/` | `CriticalMass.Runtime.<Area>` |
| Runtime EditMode tests | `Assets/_Game/Runtime.Tests/` | `CriticalMass.Runtime.Tests` |

`Assets/_Game/Sim/Rules/` (namespace `CriticalMass.Sim.Rules`) is reserved for Max's mechanics items; no F-item writes
there. `Assets/_Game/Editor/` (`CriticalMass.Editor`): only F-07 edits it in Phase 0 (`ProjectSetup.cs`, its asmdef).

## 3. Names

- A TS file's free functions become `public static` methods of one C# class, PascalCase of the TS name
  (`createWorld` -> `CreateWorld`, `queryCircle` -> `QueryCircle`). The class is named as follows:
  - the file defines a type of the same role: the type and its functions share one class (`World`, `FlowField`,
    `SpatialHash`, `Rng`, `BotsState`, `BotFields`); data and functions are split across files with `partial` where an
    item spec says so (`Kaiju`, `MapGrid`);
  - names fixed by MAX-ITEMS: `step.ts` -> `SimStep`, `grid.ts` -> `MapGrid`, `bots/fsm.ts` -> `BotFsm`,
    `game.config.ts` -> `GameConfig`;
  - names that would hide a UnityEngine type in Runtime code: `input.ts` -> `SimInput`, `debug.ts` -> `SimDebug`,
    `resources.ts` -> `SimResources` (U-031: `Resources` is ambiguous with `UnityEngine.Resources` in any Runtime file
    that also imports the Sim namespace, CS0104);
  - `src/bots/*`: `Bot` prefix (`BotSteer`, `BotDirector`, `BotFields`), `index.ts` -> `BotBrain` (U-031: a class `Bots` inside
    namespace `CriticalMass.Sim.Bots` is shadowed by the namespace from other `CriticalMass.Sim.*` code, CS0234);
  - every other file: PascalCase of the file name (`Movement`, `Collide`, `Combat`, `Damage`, `Abilities`,
    `Barricades`, `Respawn`, `Hill`, `Match`, `HeadingMetric`).
- `SCREAMING_CASE` constants -> PascalCase `public const`; `public static readonly` when not a compile-time
  constant: `ACTOR_CAPACITY` -> `ActorCapacity`, `KAIJU_SOURCE` -> `KaijuSource`.
- Object fields -> PascalCase public fields (`world.prevX` -> `world.PrevX`, `actor.spawnProtect` -> `SpawnProtect`).
- NUnit test method names: the vitest `it(...)` title with `%` -> "percent", `>=` -> "ge", `<=` -> "le", `=>` -> "gives",
  `->` -> "to", `>` -> "gt", `<` -> "lt", `+/-` -> "plus minus", `+` -> "plus", `*` -> "x" (applied in that order),
  then every character other than letters and digits becomes a space (so `spawn-protected` -> `Spawn Protected`,
  `actor's` -> `Actor S`, `0.08..0.20` -> `0 08 0 20`), each word gets its first letter upper-cased (the rest unchanged)
  and the words are joined; a leading digit is prefixed with `Case`. A template-literal title takes the value of a
  module constant (`${PASSIVE_SWEEP_TEAM_WINS_MIN}` -> `11`) and the field name of a loop variable (`${c.name}` -> `Name`,
  the loop becoming `[TestCase]` rows). Every item spec lists the resulting names (checked by script against every
  vitest title on 09e0904); use them exactly.

## 4. Types

| TypeScript | C# |
| --- | --- |
| `number` (game quantities: positions, seconds, hp, rates, allegiance) | `double` |
| `number` used only as an id, index, count, tick, version, epoch | `int` (each item spec names these fields) |
| `Float32Array`, `Float64Array` | `double[]` (U-002: numbers are `double`) |
| `Int32Array` | `int[]` |
| `Uint8Array` | `byte[]` |
| `boolean` | `bool` |
| string unions (`TeamId`, `ClassId`, `KaijuState`, `HillState`, `AiState`, `CarryKind`, `SimEventType`, ...) | `enum`, members in the TS declaration order, PascalCase |
| `Record<TeamId, number>` | `PerTeam` (F-02a): `double Azure`, `double Crimson`, indexer `this[TeamId]` |
| `T \| null` | `T?` for enums and structs, `null` for classes |
| out-objects `{ x, z }` / `{ cx, cz }` passed to be filled | `out double x, out double z` / `out int cx, out int cz` |
| `ReadonlyArray<{ x: number; z: number }>` | `IReadOnlyList<PointXZ>` (F-03 `PointXZ` struct) |
| `WeakMap<World, T>` | `System.Runtime.CompilerServices.ConditionalWeakTable<World, T>` |

## 5. Config

- No static config. Every Sim function reads `world.Config` (a `GameConfig` instance set by `World.CreateWorld` from
  `WorldOptions.Config`). Functions without a world take `GameConfig config` as their first parameter.
- A TS module constant computed from `GAME_CONFIG` at load (`ACTOR_CAPACITY`, `MAX_RADIUS`, `DT`, `SHIELD_COS`,
  `BOT_THINK_PERIOD`, ...) is computed from `world.Config` at the use site, with the same expression.
- Ported tests build worlds with `GameConfig.CreateBrowser5v5()` (the browser's main defaults, teamSize 5,
  defaultSpeed 1), passing the same teamSize, seed, human team and class as the vitest. The game itself uses
  `GameConfig.CreateDefault()` (16v16, 2x, U-004).

## 6. Behaviour rules that never translate mechanically

1. **Float32 storage (panel revision 2026-11-12, P-03; replaces "every fround becomes v").** The browser stores
   positions, velocities, flow distances and several bot fields in `Float32Array` and calls `Math.fround` before
   collision tests (D-012). The C# Sim keeps `double[]` storage but rounds exactly where the browser rounds: every
   store into a field the browser holds in a `Float32Array`, and every `Math.fround(v)`, is written `JsMath.F32(v)`
   (`(double)(float)v`, F-02a). Reads and arithmetic stay `double`, as in V8. `Float64Array` stays plain `double`.
   Why: with plain double storage, ties that collapse in f32 survive as one-ulp differences and change discrete
   choices; a port of computeFlowField + sampleFlowDir on 20 random 112x72 grids picked a different direction in
   539 of 144,221 cells (0.374%). Oracle tolerance (U-002): ticks, counts, booleans, event types and outcomes exact;
   positions, velocities and distances within 1e-4 only for the last-bit transcendental case of rule 15 (after F-02c the
   four transcendentals are bit-exact, so the tolerance is a safety margin, not an expected difference).
2. **Rounding.** `Math.round(v)` -> `JsMath.Round(v)` (floor, then +1 when the remainder is at least 0.5, F-02a; not
   `Math.Floor(v + 0.5)`, which is wrong at 0.49999999999999994; C# `Math.Round` rounds half to even).
3. **Floor to a cell.** `Math.floor(v)` used as an integer (cell coordinate, index, count) -> `JsMath.FloorToInt(v)`
   (F-02a): `int.MinValue` for NaN, infinities and values outside int range, else `(int)Math.Floor(v)`. A raw
   `(int)Math.Floor(NaN)` is platform-dependent (0 on Apple Silicon), which would let a NaN aim read cell (0, 0).
4. **Integer division.** TS has none. Never divide an `int` by an `int`; cast the numerator to `double` first
   (`(double)simHz / thinkHz` = 7.5, then `JsMath.Round` -> 8).
5. **Unsigned ops.** `x >>> 0`, `Math.imul`, `|`, `^`, `>>>` in `rng.ts` -> `uint` arithmetic in `unchecked`.
6. **Modulo.** C# `%` has the same truncated sign rule as JS `%` for `int` and `double`; every modulo in the ported files
   has a non-negative left side. Port `%` as `%`.
7. **NaN and finiteness.** `Number.isFinite(v)` -> `double.IsFinite(v)`; `!(v === v)` -> `double.IsNaN(v)`;
   `!(a > 0)` stays `!(a > 0)` (NaN-safe). `Infinity` -> `double.PositiveInfinity`.
8. **Iteration order.** Arrays iterate by index as in TS. Sim and bot files never iterate a `Map`, a `Set`, nor object keys,
   and never call `Array.prototype.sort`, so no ordering rule is needed beyond index order.
9. **Randomness.** Only the seeded `Rng` (F-02a). No `System.Random`, `UnityEngine.Random`, `DateTime`, `Stopwatch` in
   `Assets/_Game/Sim` (`BoundaryTests` enforces it). `Math.random` appears in no browser sim file and no bot file.
10. **Module-scope scratch.** TS module-level scratch arrays and objects become `private static` fields, grown the
    same way. Tests run single-threaded: never add `[Parallelizable]`. The Sim is stepped from exactly one thread
    (U-047): the Runtime driver (F-08a) steps it on the Unity main thread only and the soak harness (F-09) runs matches
    one after another, never in parallel; F-01b proves sequential interleaving of two worlds is safe.
11. **Logging.** `console.error` / `console.log` in sim code is dropped (the Sim has no logger).
12. **Ceil and round of products.** `Math.ceil(a * b)` -> `Math.Ceiling(a * b)` with both operands `double`, and the
    same for `Math.round` -> `JsMath.Round`: the IEEE product is the same in both runtimes (10 * 0.3 = 3.0000000000000004
    ceils to 4 in both). Never rewrite such an expression in `int` or `decimal`.
13. **Tick counts from config.** Every config number is `double` (F-01). A TS field that counts whole ticks
    (`Kaiju.meleeWindupIn`, `Barricade` slots, ...) is `int` in C# and is set with an explicit `(int)` cast of the config
    value; the cast is exact because those values are integers.
14a. **Team enums (U-047).** `TeamId`, `MatchResult`, `HillState` and `KaijuState` have different integer values for
    Azure and Crimson. A browser assignment of a team string (`winner = team`, `kaiju.state = team`, `hillState = team`)
    goes through `SimEnumMap` (F-02c); a raw cast between them fails `BoundaryTests`.
14. **Empty input is not "no aim".** `SimInput.EmptyInput()` has aim (0, 0), which is finite. Code that means "no aim"
    passes `double.NaN`; code that means "no input" does not call `ApplyInput` at all.
15. **Math library (U-032, replaces the earlier "System.Math equivalents, 1e-4 covers it").** `Math.sin`, `Math.cos`,
    `Math.atan2`, `Math.exp` -> `JsMath.Sin`, `JsMath.Cos`, `JsMath.Atan2`, `JsMath.Exp` (F-02c: ports of V8's fdlibm,
    bit-exact with node on the golden file `tools/golden/jsmath-fdlibm.json`). `BoundaryTests` fails any
    `Math.Sin/Cos/Atan2/Exp`, `MathF.*` of those four, and `using static System.Math` under `Assets/_Game/Sim/`.
    `Math.sqrt/abs/min/max/floor/ceil` -> `System.Math` (exact in IEEE-754 on every platform). Sim.Tests code that
    feeds the Sim (F-09's wandering actor) uses `JsMath` too.

## 7. Mechanics item code left out of shared files (U-023)

Where a browser file mixes base rules with a mechanics item's code, the port leaves the item code out and writes, at
the exact place it would go, one line: `// ITEM CM-xx: added by its owner (<what is missing>)`. Each item spec lists
every cut. A test whose expected value includes a cut item's effect is ported with that term left out and the same
marker on the assertion line.

## 8. Tests

- One `public sealed class <Name>Tests` with `[TestFixture]` per spec'd fixture, one `[Test]` per vitest `it(...)`.
  An `it(...)` generated inside a `for` loop becomes one method with one `[TestCase(...)]` row per loop element.
- `expect(a).toBe(b)`: `Assert.AreEqual(b, a)` exact, except for positions, velocities and distances, which use
  `Assert.AreEqual(b, a, 1e-4)`. `toBeCloseTo(v, d)` -> `Assert.AreEqual(v, a, 0.5 * Math.Pow(10, -d))`.
  `toEqual` on an object -> one assertion per field. `toBeGreaterThan` etc. -> `Assert.Greater` etc.
- Vitest helpers (`world()`, `place()`, `clearOthers()`, ...) become `private static` methods of the fixture that uses
  them; fixtures do not share helper files (F-09's `Harness/` is the only shared test code in Phase 0).
- `*.slow.test.ts` fixtures carry `[Category("Slow")]`. `it.skipIf(!process.env.PERF)` becomes
  `[Test, Explicit("perf"), Category("Perf")]`.

## 9. Meta files, scenes and assets (U-027)

Builders never author a `.meta`, `.unity`, `.prefab`, `.asset` or `.mat` file by hand, and never pick a GUID. Unity
creates the `.meta` of every new file and folder when the integrator imports the project (ProjectSetup.Apply and the
compile gate, U-029); a `files_allowed` glob ending in `*` covers that generated `.meta`. The game hierarchy is built in
code when Boot loads (F-08a `GameBootstrap`); assets Runtime needs at run time are created by
`CriticalMass.Editor.ProjectSetup.Apply` (F-07). The one hand-written Unity data file in Phase 0 is
`Assets/_Game/Settings/Controls.inputactions`, which is plain JSON (shape in F-07).

## 10. Pass checks

Builders do not run Unity (loop/README.md). The integrator runs, per batch: `tools/unity-compile.sh`, then
`tools/unity-test.sh` (default filter `!Slow`, U-028; the soak lane is `tools/unity-test.sh --category Slow`), then per
item the fixture counts:

```bash
fc() { xmllint --xpath "count(//test-case[starts-with(@fullname,'$1.')][@result='Passed'])" Logs/editmode-results.xml; }
fc CriticalMass.Sim.Tests.RngTests   # prints the number of passed cases in that fixture
```

## 11. Canonical shared types (one listing; items refer here)

Checked field by field against `src/sim/types.ts`, `world.ts`, `map.ts`, `flowfield.ts`, `spatialHash.ts`,
`src/bots/state.ts`, `fields.ts` on 09e0904. Order = browser declaration order. `// ITEM CM-xx` lines sit exactly where
the browser field is, as a comment line inside the class. Every class is `public sealed` (Kaiju: `public sealed partial`),
every member a public field unless written otherwise. Owner item in brackets.

```csharp
// CriticalMass.Sim.Config [F-01]
enum ClassId { Warrior, Farmer, Miner, Demolitions, Builder }      enum TeamId { Azure, Crimson }
class GameConfig { int Seed; double SimHz; int TeamSize; int[] TeamSizeOptions; int TeamSizeMax; double DefaultSpeed; ... }
//   (full field table in F-01; CreateDefault(), CreateBrowser5v5(), Clone(), Class(ClassId), ClassMix(ClassId))

// CriticalMass.Sim.Core [F-02a]
static class Cell { const byte Void=0, Floor=1, Wall=2, BreakableIntact=3, BreakableCracked=4, BreakableDestroyed=5, Water=6, Bridge=7, Barricade=8; }
enum CarryKind { None, Biomass, Corruption }   enum KaijuState { Egg, Neutral, Azure, Crimson, Berserk }
enum HillState { Neutral, Azure, Crimson, Contested }   enum MatchPhase { Playing, Ended }   enum MatchResult { None, Azure, Crimson, Draw }
enum AiState { None, ToHill, HoldHill, Fight, Gather, Deliver, Corrupt, Retreat, Unstick }   enum NodeKind { Biomass, Corruption }
enum SimEventType { Hit, Death, Respawn, Gather, DeliverBiomass, DeliverCorruption, Hatch, KaijuTelegraph, KaijuMeleeWindup,
  KaijuSlam, KaijuKO, KaijuRecover, WallBreak, BarricadePlaced, BarricadeBroken, HillOwnerChange, BerserkLocked, Ability,
  /* ITEM CM-30 Terraform, CM-31 Topple, CM-84 ClassSwitch, CM-37 Heal: one marker line each, here */ MatchEnd }
//   (the browser order puts terraform, topple, classSwitch, heal between ability and matchEnd; items append them there,
//    so no Phase 0 code may store or compare SimEventType by its integer value)
class PerTeam { double Azure; double Crimson; double this[TeamId t] { get; set; } void Set(double azure, double crimson); }
class InputFrame { double MoveX, MoveZ, AimX, AimZ; bool Attack, Ability, Interact; int ClassPick; bool Terraform; bool Jump; void CopyFrom(InputFrame o); }
class SimEvent { SimEventType Type; int A; int B; double X; double Z; double Value; /* ITEM CM-35 (Bio) */ }
class LostCargo { CarryKind Kind; int Amount; }
class Actor { int Id; TeamId Team; ClassId Cls; bool IsHuman; bool Alive; double Hp; double MaxHp; double Facing; double AttackCd;
  double AbilityCd; double AbilityActive; double SlowTimer; double SpawnProtect; double RespawnIn; CarryKind Carry; int CarryAmount;
  /* ITEM CM-35 (CarryMeat, MeatPickup) */ double GatherProgress; int GatherNodeId; int LastAttacker; LostCargo LostCargo;
  int Kills; int Deaths; double HillSeconds; AiState AiState; double AnimAction; bool Moving; ClassId? PendingClass;
  /* ITEM CM-84 (ClassSwitchCd, ClassSwitches) */ double StandUp; /* ITEM CM-32 (AirLen, AirTick, AirFromX, AirFromZ, AirToX, AirToZ) */ }
class ResourceNode { int Id; NodeKind Kind; double X; double Z; int Charges; int MaxCharges; double RegrowIn; }   /* ITEM CM-35 (LooseOrb) */
class Barricade { bool Active; int Owner; TeamId Team; int Cx; int Cz; double Hp; double Ttl; byte PrevCell; }
partial class Kaiju { KaijuState State; double X, Z, PrevX, PrevZ, Facing, Hp, MaxHp, KoIn, MeleeCd, SlamCd, TelegraphIn;
  int MeleeWindupIn; int MeleeTargetId; double TelegraphX, TelegraphZ; int TargetId; double[] Threat; }
class DeliveredTally { double Biomass; double Corruption; /* ITEM CM-35 (Plant, Meat) */ }
class MatchState { double Time; MatchPhase Phase; PerTeam HillSeconds; HillState HillState; PerTeam HillCount; PerTeam Allegiance;
  DeliveredTally[] Delivered /* [(int)TeamId] */; double Instability; bool BerserkLocked; bool Hatched; MatchResult Winner; }
class Rng { uint State; }   // statics: CreateRng(uint), SeedRng(Rng, uint), NextFloat(Rng), NextInt(Rng, int, int), Pick<T>(Rng, IReadOnlyList<T>)
static class JsMath { double Round(double); int FloorToInt(double); }

// CriticalMass.Sim.Grid [F-03 MapDef, MapGrid; F-04 FlowField, SpatialHash; F-02b MapGrid.SetCell]
struct PointXZ { double X; double Z; }   struct CellDir { int Dx; int Dz; }   enum DecorationKind { Pillar }
class DecorationDef { DecorationKind Kind; int Cx; int Cz; CellDir Ramp; CellDir Block; }
class PadDef { double X, Z, R; }  class HillDef { double X, Z, R; }  class ShrineDef { double X, Z, SolidR; }  class BoundsDef { double MinX, MinZ, MaxX, MaxZ; }
class MapDef { int Width; int Height; byte[] Cells; double[] Height01; byte[] WallHp; PadDef[] Pads /* [(int)TeamId] */; HillDef Hill;
  ShrineDef Shrine; PointXZ[] BiomassNodes; PointXZ[] CorruptionNodes; BoundsDef Bounds; DecorationDef[] Decorations; MapDef WithCells(byte[] cells); }
class FlowField { double[] Dist; int Version; }
class SpatialHash { double CellSize; int Cols; int Rows; int Capacity; int[] CellStart; int[] CellFill; int[] Items; int[] ItemCell; double[] Xs; double[] Zs; int Count; }
static partial class MapGrid { InBounds, IsWalkableType, IsWalkable, CellAt, WorldToCell, CellIndex, ClearanceOk [F-03]; void SetCell(World, int, int, byte) [F-02b] }

// CriticalMass.Sim.Core [F-02b]
class WorldOptions { int Seed; int TeamSize; TeamId HumanTeam; ClassId HumanClass; GameConfig Config; }
class KaijuNav { MapDef Clear; int ClearGridVersion; FlowField Field; int FieldWallVersion; int GoalCx; int GoalCz; double RepathIn;
  double RetargetIn; double HomeX; double HomeZ; }
class World { WorldOptions Opts; GameConfig Config; Rng Rng; MapDef Map; int GridVersion; int WallVersion;
  /* ITEM CM-30 (WallChangeTick, Terraform) */ /* ITEM CM-31 (Topple) */ int Epoch; int Tick; int ActorCount;
  double[] X, Z, PrevX, PrevZ, Vx, Vz; Actor[] Actors; InputFrame[] Inputs; byte[] HasAim; ResourceNode[] Nodes;
  /* ITEM CM-35 (Orbs, MeatDropped, OrbOverflows) */ Barricade[] Barricades; Kaiju Kaiju; bool ForceHatch; KaijuNav KaijuNav;
  MatchState Match; SimEvent[] Events; int EventHead; SpatialHash Hash; }

// CriticalMass.Sim.Bots [F-06a BotsState; F-06b BotFields]
class BotsState { int Epoch; int ActorCount; int LastTick; Rng Rng; BotFields Fields; int HumanDrivenTick; double NextDirectorTime;
  int ThinkPeriod; byte[] Role; double[] RoleSince; byte[] Corrupt; double[] StateSince; double[] Aggression; double[] Greed;
  double[] AnchorX, AnchorZ, AnchorTime; double[] ProgressX, ProgressZ, ProgressTime; byte[] WantMove;
  double[] UnstickX, UnstickZ, UnstickUntil; int[] Target; int[] TargetNode; double[] PostX, PostZ, PostUntil;
  byte[] AbilityReq; double[] AbilityX, AbilityZ; int StateChanges; }
class BotFields { int Count; int Width; int Height; double[][] Dist; int[] Version; int[][] Goals; int[] GoalCount; byte[] Miner;
  int[] FieldOfNode; int Cursor; int Builds; int LastRebuildTick; }   struct FlowStep { double X; double Z; int Cell; }
static class MatchTick { void Advance(World w, InputFrame humanFrame, InputFrame scratch); void AdvanceNoHuman(World w, InputFrame scratch); } [F-06e]

// CriticalMass.Runtime [F-08a]
static class SimSpace { Vector3 ToWorld(double simX, double height, double simZ); void ToSim(Vector3 world, out double simX, out double simZ);
  float YawDeg(double facing); Quaternion CameraRotation(GameConfig c); }   // the only z negation in the project (O-13)
```

`int` fields: ids, indices, counts, ticks, versions, epochs (`HillCount` stays `PerTeam` double as `Record<TeamId, number>`).
