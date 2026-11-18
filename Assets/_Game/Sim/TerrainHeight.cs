// b15 terrain height (owner ruling "do it"): blocks STACK and characters walk DOWN INTO a dug pit and back out.
// Design: docs/b15-terrain-height/DESIGN.md. Every number: Sim/Config/TerrainHeightConfig.cs. Integer levels only, no RNG,
// allocation-free after World creation. Full rules only: the layer (MapDef.Lift) exists only when the rule is on, and every
// entry point returns the old behaviour when it is null, so Classic and PhysicsOnly stay bit-identical.
using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>b15: per-actor height state (on DepthState.Height): level, slow after a level change, the climb counter.</summary>
    public sealed class TerrainHeightState
    {
        /// <summary>"No level yet" (dead, or the first tick alive): the next level is taken without a slow.</summary>
        public const sbyte NoLevel = sbyte.MinValue;

        /// <summary>Per actor: the level at the end of the last movement step (TerrainHeight.LevelAt).</summary>
        public sbyte[] Level;
        /// <summary>Per actor: ticks of slow left and its kind (1 = a 1-level step, 2 = a drop of 2+, 3 = a climb of 2).</summary>
        public byte[] SlowTicks;
        public byte[] SlowKind;
        /// <summary>Per actor: consecutive ticks it walked into a climbable ledge; ClimbHit = this tick's move met one.</summary>
        public byte[] Climb;
        public byte[] ClimbHit;

        // ---- stats (tests print them)
        public int Stacks, Lowered, Deepened, Steps, Drops, Climbs, Smashed, TntLowered, RouteRefusals, PitLadders, LaddersLost;

        // ---- route proof memo (the ghost asks every frame): (cell * 8 + next + 4) at World.GridVersion
        internal readonly long[] MemoKey = new long[8];
        internal readonly int[] MemoVersion = new int[8];
        internal readonly byte[] MemoOk = new byte[8];
        internal int MemoNext;

        public static TerrainHeightState Create(int cap)
        {
            var s = new TerrainHeightState
            {
                Level = new sbyte[cap],
                SlowTicks = new byte[cap],
                SlowKind = new byte[cap],
                Climb = new byte[cap],
                ClimbHit = new byte[cap],
            };
            s.Reset();
            return s;
        }

        /// <summary>Back to the start of a match, in place.</summary>
        public void Reset()
        {
            for (int i = 0; i < Level.Length; i++) Level[i] = NoLevel;
            Array.Clear(SlowTicks, 0, SlowTicks.Length);
            Array.Clear(SlowKind, 0, SlowKind.Length);
            Array.Clear(Climb, 0, Climb.Length);
            Array.Clear(ClimbHit, 0, ClimbHit.Length);
            Stacks = Lowered = Deepened = Steps = Drops = Climbs = Smashed = TntLowered = RouteRefusals = PitLadders = LaddersLost = 0;
            for (int k = 0; k < MemoVersion.Length; k++) { MemoVersion[k] = int.MinValue; MemoKey[k] = -1; MemoOk[k] = 0; }
            MemoNext = 0;
        }
    }

    /// <summary>
    /// b15 terrain height: the rules. A cell's LIFT (MapDef.Lift, Floor and BreakableDestroyed cells only) is -MaxDepth..MaxStack
    /// levels on top of the island's elevation. An actor's LEVEL is the highest lift among the walkable cells its square touches.
    /// Grounded walking: a cell WalkUp+1 or more levels above blocks; any drop is allowed. Airborne: JumpUp. CLIMB: walking into a
    /// ledge exactly JumpUp above for ClimbTicks climbs it. Bridge cells (ladders, decks) are ramps (never blocked by height).
    /// </summary>
    public static class TerrainHeight
    {
        /// <summary>Refusal codes (shared by Terraform and BuilderWalls: neither uses these values).</summary>
        public const int RefusedTooHigh = -11, RefusedTooDeep = -12, RefusedLevelOnly = -13, RefusedSomeone = -14;

        private const double RangeEps = 1e-6;   // Collide.RangeEps: the same cell range as the box collider

        /// <summary>The rule is on for this config (Full rules and its own switch).</summary>
        public static bool Enabled(GameConfig c) => DepthRules.On(c, c.Depth.TerrainHeight.Enabled);

        /// <summary>The rule is on in this world (the layer exists).</summary>
        public static bool On(World w) => w != null && w.Map != null && w.Map.Lift != null;

        /// <summary>Cell types that carry a lift.</summary>
        public static bool IsLiftType(byte t) => t == Cell.Floor || t == Cell.BreakableDestroyed;

        /// <summary>Lift of cell (cx, cz); 0 off the map or without the layer.</summary>
        public static int LiftAt(MapDef map, int cx, int cz)
        {
            sbyte[] l = map.Lift;
            if (l == null || cx < 0 || cz < 0 || cx >= map.Width || cz >= map.Height) return 0;
            return l[cz * map.Width + cx];
        }

        /// <summary>True when cell i is raised (a stacked block) and the layer exists.</summary>
        public static bool Raised(MapDef map, int i) => map.Lift != null && map.Lift[i] > 0 && IsLiftType(map.Cells[i]);

        /// <summary>
        /// The level of a body of half side r centred at (x, z): the highest lift among the walkable cells its square touches
        /// (same cell range as Collide's box rule); 0 without the layer or when it touches no walkable cell.
        /// </summary>
        public static int LevelAt(MapDef map, double x, double z, double r)
        {
            sbyte[] l = map.Lift;
            if (l == null) return 0;
            int x0 = JsMath.FloorToInt(x - r - RangeEps), x1 = JsMath.FloorToInt(x + r + RangeEps);
            int z0 = JsMath.FloorToInt(z - r - RangeEps), z1 = JsMath.FloorToInt(z + r + RangeEps);
            int w = map.Width, h = map.Height;
            int best = int.MinValue;
            for (int cz = z0; cz <= z1; cz++)
            {
                if (cz < 0 || cz >= h) continue;
                for (int cx = x0; cx <= x1; cx++)
                {
                    if (cx < 0 || cx >= w) continue;
                    int i = cz * w + cx;
                    if (!MapGrid.IsWalkableType(map.Cells[i])) continue;
                    if (l[i] > best) best = l[i];
                }
            }
            return best == int.MinValue ? 0 : best;
        }

        // -----------------------------------------------------------------------------------------------------------
        // Paths (FlowField, BotFields): symmetric edges

        /// <summary>
        /// Extra cost of the orthogonal edge between walkable cells c and n: 0 level, PathCostStep1 for 1 level, PathCostStep2 for
        /// 2 (a climb), -1 = closed (3+), unless the higher cell is a Bridge (a ladder or deck is a ramp). Costs: MapDef.LiftCost1 / 2
        /// (TerrainHeightConfig.PathCostStep1 / 2, copied when the layer is made). Call only with the layer.
        /// </summary>
        public static double EdgeExtra(MapDef map, int c, int n)
        {
            sbyte[] l = map.Lift;
            int d = l[n] - l[c];
            if (d == 0) return 0;
            int hi = d > 0 ? n : c;
            if (d < 0) d = -d;
            if (map.Cells[hi] == Cell.Bridge) return map.LiftCostLadder;   // a height ladder (or a deck on a raised cell): a ramp
            if (d == 1) return map.LiftCost1;
            if (d == 2) return map.LiftCost2;
            return -1;
        }

        /// <summary>A diagonal step from c to n (orthogonal cells a, b) is open only when the four cells are level.</summary>
        public static bool DiagonalLevel(MapDef map, int c, int n, int a, int b)
        {
            sbyte[] l = map.Lift;
            int v = l[c];
            return l[n] == v && l[a] == v && l[b] == v;
        }

        // -----------------------------------------------------------------------------------------------------------
        // Collide (Collide.SweepActor sets the mover per sweep and per sub-step; Collide.CellBlocks asks BlocksMover)

        internal static int MoverLevel, MoverUp, MoverClimbUp;
        internal static bool MoverAir;
        /// <summary>The last sweep met a ledge the mover could climb (MoveActor reads and clears it).</summary>
        internal static bool ClimbSeen;

        /// <summary>One sweep of actor id begins: how many levels it may rise (airborne: JumpUp; a ripe climb: JumpUp).</summary>
        internal static void SweepBegin(World w, int id, bool air)
        {
            TerrainHeightConfig c = w.Config.Depth.TerrainHeight;
            MoverAir = air;
            MoverClimbUp = c.JumpUp;
            int up = c.WalkUp;
            if (air) up = c.JumpUp;
            else
            {
                TerrainHeightState s = w.Depth != null ? w.Depth.Height : null;
                if (s != null && id >= 0 && id < s.Climb.Length && s.Climb[id] >= c.ClimbTicks) up = c.JumpUp;
            }
            MoverUp = up;
        }

        /// <summary>One sub-step begins at (x, z): the mover's level for this sub-step.</summary>
        internal static void SweepStep(MapDef map, double x, double z, double r) => MoverLevel = LevelAt(map, x, z, r);

        /// <summary>Walkable cell i blocks the current mover by height (more than MoverUp above its level; never a Bridge).</summary>
        internal static bool BlocksMover(MapDef map, int i)
        {
            if (map.Cells[i] == Cell.Bridge) return false;
            int d = map.Lift[i] - MoverLevel;
            if (d <= MoverUp) return false;
            if (!MoverAir && MoverUp < MoverClimbUp && d <= MoverClimbUp) ClimbSeen = true;
            return true;
        }

        /// <summary>MoveActor: actor id's move this tick met a climbable ledge.</summary>
        internal static void NoteClimb(World w, int id)
        {
            TerrainHeightState s = w.Depth != null ? w.Depth.Height : null;
            if (s != null && id >= 0 && id < s.ClimbHit.Length) s.ClimbHit[id] = 1;
        }

        // -----------------------------------------------------------------------------------------------------------
        // Movement

        /// <summary>Move speed scale of actor id: the slow after a level change, else exactly 1 (always 1 without the layer).</summary>
        public static double SpeedScale(World w, int id)
        {
            if (w.Map.Lift == null) return 1;
            TerrainHeightState s = w.Depth.Height;
            if (s == null || s.SlowTicks[id] == 0) return 1;
            TerrainHeightConfig c = w.Config.Depth.TerrainHeight;
            switch (s.SlowKind[id])
            {
                case 1: return c.StepSlowScale;
                case 2: return c.DropSlowScale;
                case 3: return c.ClimbSlowScale;
                default: return 1;
            }
        }

        /// <summary>
        /// End of Movement.MovementSystem (after separation): each living actor's new level; a grounded level change starts its
        /// slow (1 level: step, 2+ down: drop, 2+ up: climb); the climb counter grows while it walks into a climbable ledge.
        /// </summary>
        public static void MovementEnd(World w)
        {
            MapDef map = w.Map;
            if (map.Lift == null) return;
            TerrainHeightState s = w.Depth.Height;
            TerrainHeightConfig c = w.Config.Depth.TerrainHeight;
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive)
                {
                    s.Level[i] = TerrainHeightState.NoLevel;
                    s.SlowTicks[i] = 0;
                    s.Climb[i] = 0;
                    s.ClimbHit[i] = 0;
                    continue;
                }
                int lvl = LevelAt(map, w.X[i], w.Z[i], Collide.ActorRadius(w, i));
                int old = s.Level[i];
                bool grounded = a.AirLen == 0;
                if (old != TerrainHeightState.NoLevel && lvl != old && grounded)
                {
                    int d = lvl - old;
                    int ticks;
                    if (d >= 2) { s.SlowKind[i] = 3; ticks = c.ClimbSlowTicks; s.Climbs++; }
                    else if (d <= -2) { s.SlowKind[i] = 2; ticks = c.DropSlowTicks; s.Drops++; }
                    else { s.SlowKind[i] = 1; ticks = c.StepSlowTicks; s.Steps++; }
                    int t = Durations.T30(w.Config, ticks);
                    s.SlowTicks[i] = (byte)(t < 0 ? 0 : t > 255 ? 255 : t);
                    s.Climb[i] = 0;
                }
                else if (s.SlowTicks[i] > 0)
                {
                    s.SlowTicks[i]--;
                }
                s.Level[i] = (sbyte)lvl;
                bool pushing = grounded && a.Moving && s.ClimbHit[i] == 1;
                s.Climb[i] = pushing ? (byte)Math.Min(255, s.Climb[i] + 1) : (byte)0;
                s.ClimbHit[i] = 0;
            }
        }

        /// <summary>Actor id's level now (the last movement step's; 0 without the layer or when it has none).</summary>
        public static int ActorLevel(World w, int id)
        {
            if (w.Map.Lift == null || w.Depth == null || w.Depth.Height == null) return 0;
            sbyte v = w.Depth.Height.Level[id];
            return v == TerrainHeightState.NoLevel ? 0 : v;
        }

        // -----------------------------------------------------------------------------------------------------------
        // Height ladders (a Ladder piece laid on a lift cell: the cell is Bridge, its lift kept, the piece remembers the ground)

        /// <summary>
        /// Cell i is a Bridge of an active, laid Ladder piece whose ground before it was a lift cell (Floor / BreakableDestroyed):
        /// a height ladder. prev = that ground type. Scans the fixed piece pool (call only for Bridge cells with the layer on).
        /// </summary>
        public static bool LadderGround(World w, int i, out byte prev)
        {
            prev = 0;
            if (w.Map.Lift == null || w.Map.Cells[i] != Cell.Bridge || w.Builder == null || w.Builder.Pieces == null) return false;
            BuiltPiece[] ps = w.Builder.Pieces;
            for (int k = 0; k < ps.Length; k++)
            {
                BuiltPiece p = ps[k];
                if (p == null || !p.Active || p.Carried || p.Kind != BuiltPieces.Ladder || p.Cells == null) continue;
                for (int q = 0; q < p.Count; q++)
                {
                    if (p.Cells[q] != i) continue;
                    if (!IsLiftType(p.Prev[q])) return false;
                    prev = p.Prev[q];
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The ladder on cell i goes: its ground comes back (MapGrid.SetCell, so every version bumps); BuiltPieces sees a cell
        /// that is no longer Bridge and ends the piece (PieceDestroyed). The lift is untouched (the caller lowers it).
        /// </summary>
        public static void DropLadder(World w, int i)
        {
            if (!LadderGround(w, i, out byte prev)) return;
            MapGrid.SetCell(w, i % w.Map.Width, i / w.Map.Width, prev);
            w.Depth.Height.LaddersLost++;
        }

        // -----------------------------------------------------------------------------------------------------------
        // Edits

        /// <summary>Is (cx, cz) inside either team's station / hut zone (Stations.Range around the pad)?</summary>
        public static bool InStationZone(World w, int cx, int cz)
        {
            double r = w.Config.Depth.Stations.Range;
            double x = cx + 0.5, z = cz + 0.5;
            PadDef[] pads = w.Map.Pads;
            for (int k = 0; k < pads.Length; k++)
            {
                double dx = x - pads[k].X, dz = z - pads[k].Z;
                if (dx * dx + dz * dz <= r * r) return true;
            }
            return false;
        }

        /// <summary>
        /// 0 when one more level may go on cell (cx, cz) now (wood, lock, rate cap and route guard aside), else a code: outside /
        /// no ground (BuilderWalls codes), RefusedTooHigh at MaxStack, the BuilderWalls keep-outs, RefusedSomeone when an actor
        /// or the hatched Kaiju is within the build clearance, the station / hut zone.
        /// </summary>
        public static int StackRefusal(World w, int cx, int cz)
        {
            MapDef map = w.Map;
            if (!MapGrid.InBounds(map, cx, cz)) return BuilderWalls.RefusedOutside;
            int i = cz * map.Width + cx;
            if (!IsLiftType(map.Cells[i]) || map.Lift == null) return BuilderWalls.RefusedGround;
            if (map.Lift[i] >= w.Config.Depth.TerrainHeight.MaxStack) return RefusedTooHigh;
            int why = BuilderWalls.CellRefusal(w, cx, cz);
            if (why == BuilderWalls.RefusedOccupied) return RefusedSomeone;
            if (why != 0) return why;
            if (InStationZone(w, cx, cz)) return BuilderWalls.RefusedKeepOut;
            return 0;
        }

        /// <summary>
        /// Terraform.DigRefusalBeforeRoute for a lift cell (Terraform codes): a raised cell may always lose its top level (a body
        /// on it drops one level); ground and pits deepen down to MaxDepth (RefusedTooDeep) under the old dig keep-outs, and
        /// never under someone (RefusedSomeone).
        /// </summary>
        public static int DigRefusalBeforeRoute(World w, int cx, int cz)
        {
            MapDef map = w.Map;
            int i = cz * map.Width + cx;
            int l = map.Lift[i];
            if (l > 0 || map.Cells[i] == Cell.Bridge) return 0;   // a top block (bodies on it drop one level), or a height ladder
            if (map.Cells[i] != Cell.Floor) return Terraform.RefusedTarget;
            if (l <= -w.Config.Depth.TerrainHeight.MaxDepth) return RefusedTooDeep;
            int why = Terraform.VoidRefusalBeforeRoute(w, cx, cz);
            if (why == Terraform.RefusedOccupied && Terraform.CellOccupied(w, cx, cz)) return RefusedSomeone;
            return why;
        }

        /// <summary>Would setting cell i's lift to next need the route proof? Only a new 3-level edge (or worse) can cut a route.</summary>
        public static bool NeedsRouteProof(MapDef map, int i, int next)
        {
            sbyte[] l = map.Lift;
            int w = map.Width, h = map.Height;
            int cx = i % w, cz = i / w;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = cx + dx, nz = cz + dz;
                    if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                    int n = nz * w + nx;
                    byte t = map.Cells[n];
                    if (!MapGrid.IsWalkableType(t)) continue;
                    int d = l[n] - next;
                    if (d < 0) d = -d;
                    if (d >= 3) return true;
                }
            }
            return false;
        }

        /// <summary>The route guard for cell i at lift next (memoised by cell, next and World.GridVersion).</summary>
        public static bool KeepsRoutes(World w, int i, int next)
        {
            MapDef map = w.Map;
            if (!NeedsRouteProof(map, i, next)) return true;
            TerrainHeightState s = w.Depth.Height;
            long key = (long)i * 8 + (next + 4);
            for (int k = 0; k < s.MemoKey.Length; k++)
                if (s.MemoKey[k] == key && s.MemoVersion[k] == w.GridVersion) return s.MemoOk[k] == 1;
            bool ok = Terraform.LiftKeepsRoutes(w, i, (sbyte)next);
            int slot = s.MemoNext;
            s.MemoNext = (slot + 1) % s.MemoKey.Length;
            s.MemoKey[slot] = key;
            s.MemoVersion[slot] = w.GridVersion;
            s.MemoOk[slot] = ok ? (byte)1 : (byte)0;
            if (!ok) s.RouteRefusals++;
            return ok;
        }

        /// <summary>Write cell i's lift: every field and memo keyed on the grid rebuilds (GridVersion, WallVersion, WallChangeTick).</summary>
        public static void SetLift(World w, int i, int v)
        {
            w.Map.Lift[i] = (sbyte)v;
            w.GridVersion++;
            w.WallVersion++;
            w.WallChangeTick = w.Tick;
        }

        /// <summary>A cell that became Void (a TNT crater) starts again from level 0 (no version bump: SetCell already did).</summary>
        public static void ZeroLift(MapDef map, int i)
        {
            if (map.Lift != null) map.Lift[i] = 0;
        }

        private static void TeamCooldown(World w, int id)
        {
            w.Terraform.TeamReadyTick[(int)w.Actors[id].Team] = w.Tick + Durations.T30(w.Config, w.Config.Abilities.MinerTerraform.TeamCooldownTicks);
        }

        /// <summary>Actor id puts one more level on cell (cx, cz) (checks done by the caller); the team terrain cooldown starts.</summary>
        public static void CommitStack(World w, int id, int cx, int cz)
        {
            int i = cz * w.Map.Width + cx;
            SetLift(w, i, w.Map.Lift[i] + 1);
            TeamCooldown(w, id);
            w.Depth.Height.Stacks++;
        }

        /// <summary>Actor id takes one level off cell (cx, cz) (checks done by the caller); the team terrain cooldown starts.</summary>
        public static void CommitDig(World w, int id, int cx, int cz)
        {
            int i = cz * w.Map.Width + cx;
            int l = w.Map.Lift[i];
            if (w.Map.Cells[i] == Cell.Bridge)
            {
                // a height ladder: it comes down with the top block it leans on (a ladder on level or pit ground just comes down)
                DropLadder(w, i);
                if (l > 0) { w.Depth.Height.Lowered++; SetLift(w, i, l - 1); }
                TeamCooldown(w, id);
                return;
            }
            if (l > 0) w.Depth.Height.Lowered++;
            else w.Depth.Height.Deepened++;
            SetLift(w, i, l - 1);
            TeamCooldown(w, id);
        }

        /// <summary>
        /// TNT (Demolitions.BlowBomb's crater loop, inside the blast's carve radius): a raised cell loses TntLevels levels (not
        /// below 0; bodies on it drop) unless the route guard refuses. Returns false for any other cell (the crater rule applies).
        /// </summary>
        public static bool TntCell(World w, int owner, int cx, int cz)
        {
            MapDef map = w.Map;
            int i = cz * map.Width + cx;
            bool ladder = map.Lift != null && map.Lift[i] > 0 && map.Cells[i] == Cell.Bridge && LadderGround(w, i, out _);
            if (!ladder && !Raised(map, i)) return false;
            int next = map.Lift[i] - w.Config.Depth.TerrainHeight.TntLevels;
            if (next < 0) next = 0;
            if (ladder) DropLadder(w, i);   // the blast takes the ladder with the blocks it leans on
            if (!KeepsRoutes(w, i, next))
            {
                World.PushEvent(w, SimEventType.Terraform, owner, i, cx + 0.5, cz + 0.5, Terraform.RefusedRoute);
                return true;
            }
            SetLift(w, i, next);
            w.Depth.Height.TntLowered++;
            World.PushEvent(w, SimEventType.Terraform, owner, i, cx + 0.5, cz + 0.5, Terraform.Dug);
            return true;
        }

        /// <summary>
        /// Per tick (SimStep, after the Kaiju): the hatched Kaiju ignores height, and every cell at KaijuSmashFrom or more that its
        /// body (Kaiju.WallR + KaijuSmashMargin) overlaps is smashed down to SmashTo at once (no event: the grid version shows it).
        /// </summary>
        public static void HeightSystem(World w)
        {
            MapDef map = w.Map;
            if (map.Lift == null) return;
            Kaiju k = w.Kaiju;
            if (k == null || k.State == KaijuState.Egg) return;
            TerrainHeightConfig c = w.Config.Depth.TerrainHeight;
            double r = Kaiju.WallR(w) + c.KaijuSmashMargin;
            int x0 = Math.Max(0, JsMath.FloorToInt(k.X - r)), x1 = Math.Min(map.Width - 1, JsMath.FloorToInt(k.X + r));
            int z0 = Math.Max(0, JsMath.FloorToInt(k.Z - r)), z1 = Math.Min(map.Height - 1, JsMath.FloorToInt(k.Z + r));
            for (int cz = z0; cz <= z1; cz++)
            {
                for (int cx = x0; cx <= x1; cx++)
                {
                    int i = cz * map.Width + cx;
                    if (map.Lift[i] < c.KaijuSmashFrom) continue;
                    bool ladder = map.Cells[i] == Cell.Bridge && LadderGround(w, i, out _);
                    if (!ladder && !IsLiftType(map.Cells[i])) continue;
                    if (!Terraform.CircleHitsCell(k.X, k.Z, r, cx, cz)) continue;
                    if (ladder) DropLadder(w, i);   // the block it leans on goes, so the ladder goes
                    SetLift(w, i, c.SmashTo);
                    w.Depth.Height.Smashed++;
                }
            }
        }

        /// <summary>
        /// Builder height ladder: cell `to` (the cell faced) is a lift cell at least JumpUp levels above the Builder's cell `from`:
        /// a pit wall (the ledge above a pit), the side of a stack or tower, or any other face of 2+ levels. The ladder is laid ON
        /// that cell (it becomes a Bridge, a ramp, keeping its lift), so climbing it puts you on the ledge or the stack top.
        /// </summary>
        public static bool LedgeAbove(MapDef map, int from, int to, GameConfig cfg)
        {
            sbyte[] l = map.Lift;
            if (l == null || !IsLiftType(map.Cells[to]) || !MapGrid.IsWalkableType(map.Cells[from])) return false;
            return l[to] - l[from] >= cfg.Depth.TerrainHeight.JumpUp;
        }

        private static readonly TerrainHeightConfig Defaults = new TerrainHeightConfig();
        private static string _high, _deep;
        private static int _highN = -1, _deepN = -1;
        private static readonly string[] Tags = { null, null, "x2", "x3", "x4", "x5", "x6", "x7", "x8", "x9" };

        /// <summary>The prompt words of the b15 refusals (null for any other code). Cached: no allocation per frame.</summary>
        public static string Word(int why, GameConfig cfg)
        {
            TerrainHeightConfig c = cfg != null ? cfg.Depth.TerrainHeight : Defaults;
            switch (why)
            {
                case RefusedTooHigh:
                    if (_highN != c.MaxStack) { _highN = c.MaxStack; _high = "TOO HIGH - " + c.MaxStack + " MAX"; }
                    return _high;
                case RefusedTooDeep:
                    if (_deepN != c.MaxDepth) { _deepN = c.MaxDepth; _deep = "TOO DEEP - " + c.MaxDepth + " MAX"; }
                    return _deep;
                case RefusedLevelOnly: return "LEVEL GROUND ONLY";
                case RefusedSomeone: return "SOMEONE IS STANDING THERE";
                default: return null;
            }
        }

        /// <summary>The ghost tag for a stack that would reach `level` ("x2", "x3"; null below 2).</summary>
        public static string StackTag(int level) => level >= 2 && level < Tags.Length ? Tags[level] : null;
    }
}
