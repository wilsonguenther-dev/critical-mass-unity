using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// Miner terrain dig / place (CM-30, port of src/sim/terraform.ts on 09e0904). Travis (2026-11-12, "What Demo needs"):
    /// "Ground removable and placeable by miners". Every rule lives here; Topple (CM-31) and later terrain tools (DG-22 TNT)
    /// reuse the route guard. Holes and blocks are discrete cell states (bible p14: no procedural destruction).
    /// <para>One press of InputFrame.Terraform (F) by a living, standing miner acts in its dig / place mode (DG-09, U-053):</para>
    /// <para>DIG: the cell under the aim point (clamped to MinerTerraform.Reach) must be walkable ground (Floor or Bridge),
    /// outside every keep-out (pads, hill, shrine, resource nodes), with no living actor or hatched Kaiju overlapping it, and
    /// the ROUTE GUARD must pass. It becomes Void and the miner carries one more block (at most MinerTerraform.CarryMax).</para>
    /// <para>PLACE: the cell must be Void or Water with at least one orthogonal walkable neighbour; it becomes Floor and the
    /// carry drops by one. A place only adds walkable ground, so it can never cut a route, but it is rate-capped like a dig.</para>
    /// <para>Mode (DG-09, U-053, not in the browser): a dig that fills the carry switches the miner to Place, a place that
    /// empties it switches back to Dig, so with CarryMax 1 the browser's one-key rule (dig when empty-handed, place while
    /// carrying) is replayed exactly; InputFrame.TerraformSwitch (wheel, LB / RB) flips the mode by hand.</para>
    /// <para>Route guard: with the cells changed, both team pads must still reach the hill centre, and a pad that reached the
    /// shrine ring before must still reach it (the barricade rule, CM-05). Proven on a static scratch field over a tentative
    /// raw write to map.Cells that is always restored, so the proof never moves GridVersion / WallVersion.</para>
    /// <para>Rate cap: a team changes terrain once per MinerTerraform.TeamCooldownTicks, and no change lands within
    /// MinGapTicks of the last wall change of any kind (World.WallChangeTick), so every bot field rebuilds between two changes.</para>
    /// <para>Every outcome pushes one Terraform event: A = miner, B = cell index (-1 = no cell), (X, Z) = the cell centre
    /// (the aim point when there is no cell), Value = Dug / Placed or a Refused* code.</para>
    /// <para>DG-09 fall rule (not in the browser, U-053): see <see cref="FallIfUnsupported"/>.</para>
    /// Timing is in sim ticks (World.Tick). Allocation-free after the first call (static scratch).
    /// </summary>
    public static class Terraform
    {
        /// <summary>Terraform event values: successes are positive, refusals negative.</summary>
        public const int Dug = 1;
        public const int Placed = 2;
        /// <summary>The clamped aim point is outside the map.</summary>
        public const int RefusedOutside = -1;
        /// <summary>Wrong cell type, not a miner, a full carry (dig) or an empty one (place).</summary>
        public const int RefusedTarget = -2;
        /// <summary>A keep-out (pad, hill, shrine, node) or an actor / the hatched Kaiju overlaps the cell.</summary>
        public const int RefusedOccupied = -3;
        /// <summary>The dig would disconnect a team pad from the hill (or newly from the shrine).</summary>
        public const int RefusedRoute = -4;
        /// <summary>The team or world rate cap has not run out.</summary>
        public const int RefusedRate = -5;

        // -----------------------------------------------------------------------------------------------------------
        // Scratch

        private static readonly PointXZ[] HillGoal = { new PointXZ(0, 0) };
        private static PointXZ[] ShrineGoals = new PointXZ[0];
        private static double ShrineKeyX = double.NaN;
        private static double ShrineKeyZ = double.NaN;
        private static double ShrineKeyR = double.NaN;
        private static readonly int[] OneCell = new int[1];
        private static readonly byte[] OneType = new byte[1];
        private static byte[] OldTypes = new byte[16];

        /// <summary>Goal points for "reaches the shrine": every cell centre within ShrineInteractRange of the shrine centre.</summary>
        private static PointXZ[] ShrineRing(World w)
        {
            ShrineDef s = w.Map.Shrine;
            double r = w.Config.KaijuFeed.ShrineInteractRange;
            if (s.X == ShrineKeyX && s.Z == ShrineKeyZ && r == ShrineKeyR) return ShrineGoals;
            int count = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1) ShrineGoals = new PointXZ[count];
                int k = 0;
                for (int cz = JsMath.FloorToInt(s.Z - r); cz <= JsMath.FloorToInt(s.Z + r); cz++)
                {
                    for (int cx = JsMath.FloorToInt(s.X - r); cx <= JsMath.FloorToInt(s.X + r); cx++)
                    {
                        double dx = cx + 0.5 - s.X;
                        double dz = cz + 0.5 - s.Z;
                        if (dx * dx + dz * dz > r * r) continue;
                        if (pass == 1) ShrineGoals[k] = new PointXZ(cx + 0.5, cz + 0.5);
                        k++;
                    }
                }
                count = k;
            }
            ShrineKeyX = s.X;
            ShrineKeyZ = s.Z;
            ShrineKeyR = r;
            return ShrineGoals;
        }

        /// <summary>True when both team pads reach the hill centre on the CURRENT grid (the soak samples this).</summary>
        public static bool PadsReachHill(MapDef map)
        {
            HillGoal[0].X = map.Hill.X;
            HillGoal[0].Z = map.Hill.Z;
            return PadsReached(map, HillGoal) == 3;
        }

        /// <summary>
        /// b14 sim cost: bit 1 = the Azure pad reaches one of the goals, bit 2 = the Crimson pad (GridReach: the same yes/no
        /// as a finite flow-field distance at the pad's cell, PadReached, without building the field).
        /// </summary>
        private static int PadsReached(MapDef map, PointXZ[] goals)
        {
            PadDef a = map.Pads[(int)TeamId.Azure], c = map.Pads[(int)TeamId.Crimson];
            return GridReach.Reached(map, goals, a.X, a.Z, c.X, c.Z);
        }

        /// <summary>
        /// Route guard (CM-30, shared with CM-31 topples): with cells[idx[k]] = next[k] for k &lt; n, both pads still reach
        /// the hill, and no pad that reaches the shrine ring now loses it. The tentative raw write is restored before returning.
        /// </summary>
        public static bool CellsKeepRoutes(World w, int[] idx, byte[] next, int n)
        {
            // b14 sim cost: the same request on the same grid (compared cell for cell; and, when the Kaiju route guard looked at the body, the same
            // Kaiju start cell on the same clearance build) has the same verdict: answer it from the memo (an expired piece
            // whose lift is refused retries every tick; each retry paid two flow fields and the Kaiju guard's searches).
            int hit = RouteMemoFind(w, idx, next, n);
            if (hit >= 0)
            {
                SimProfile.RouteMemoHits++;
                return Memo[hit].Ok;
            }
            SimProfile.RouteProofs++;
            Kaiju.LastGuard.Used = false;
            bool verdict = CellsKeepRoutesUncached(w, idx, next, n);
            RouteMemoStore(w, idx, next, n, verdict);
            return verdict;
        }

        // ------------------------------------------------------------------------------------------ route verdict memo
        /// <summary>
        /// b14 sim cost: one remembered route verdict. A pure function of (the grid, compared cell for cell; the request; and,
        /// only when the Kaiju route guard reached the body test, the guard's start cell on the Kaiju clearance grid, whose
        /// build is identified by Kaiju.ClearEpoch). Never Sim state: a hit and a miss give the same answer, so no checksum moves.
        /// </summary>
        private sealed class RouteMemoEntry
        {
            public World W;
            /// <summary>The whole grid the verdict was worked out on (exact compare: a raw cell write without a GridVersion bump, as
            /// the tests paint, still misses).</summary>
            public byte[] Grid = new byte[CriticalMass.Sim.Maps.SkybreakBasinMap.MapWidth * CriticalMass.Sim.Maps.SkybreakBasinMap.MapHeight];   // the shipped map: no allocation in play
            /// <summary>integrator b15: the terrain-height layer the verdict was worked out on (the proof respects height).</summary>
            public sbyte[] Lift = new sbyte[CriticalMass.Sim.Maps.SkybreakBasinMap.MapWidth * CriticalMass.Sim.Maps.SkybreakBasinMap.MapHeight];
            public bool HasLift;
            public long Epoch;
            /// <summary>What the Kaiju route guard read (Used false: the verdict did not depend on the Kaiju: pads cut, nothing narrowed, guard off).</summary>
            public Kaiju.GuardKey Guard;
            public int N;
            public int[] Idx = new int[RouteMemoMaxCells];
            public byte[] Next = new byte[RouteMemoMaxCells];
            public bool Ok;
        }

        /// <summary>Requests with more cells than this are never memoised (a whole wall line is checked once anyway).</summary>
        private const int RouteMemoMaxCells = 64;
        private static readonly RouteMemoEntry[] Memo = NewMemo(8);
        private static int MemoCursor;

        private static RouteMemoEntry[] NewMemo(int k)
        {
            var m = new RouteMemoEntry[k];
            for (int i = 0; i < k; i++) m[i] = new RouteMemoEntry();
            return m;
        }

        private static int RouteMemoFind(World w, int[] idx, byte[] next, int n)
        {
            if (n > RouteMemoMaxCells || SimProfile.NoRouteMemo) return -1;
            for (int e = 0; e < Memo.Length; e++)
            {
                RouteMemoEntry m = Memo[e];
                if (!ReferenceEquals(m.W, w) || m.Epoch != Kaiju.ClearEpoch || m.N != n) continue;
                bool same = true;
                for (int k = 0; k < n && same; k++) same = m.Idx[k] == idx[k] && m.Next[k] == next[k];
                if (!same) continue;
                byte[] cells = w.Map.Cells;
                if (m.Grid.Length != cells.Length || !new ReadOnlySpan<byte>(m.Grid).SequenceEqual(cells)) continue;
                sbyte[] lift = w.Map.Lift;
                if (m.HasLift != (lift != null)) continue;
                if (lift != null && (m.Lift.Length != lift.Length || !new ReadOnlySpan<sbyte>(m.Lift).SequenceEqual(lift))) continue;   // integrator b15
                if (!m.Guard.Used || Kaiju.RouteMemoSameKaiju(w, in m.Guard)) return e;
            }
            return -1;
        }

        private static void RouteMemoStore(World w, int[] idx, byte[] next, int n, bool ok)
        {
            if (n > RouteMemoMaxCells) return;
            RouteMemoEntry m = Memo[MemoCursor];
            MemoCursor = (MemoCursor + 1) % Memo.Length;
            m.W = w;
            byte[] cells = w.Map.Cells;
            if (m.Grid.Length != cells.Length) m.Grid = new byte[cells.Length];   // once per map size
            Array.Copy(cells, m.Grid, cells.Length);
            sbyte[] lift = w.Map.Lift;   // integrator b15
            m.HasLift = lift != null;
            if (lift != null)
            {
                if (m.Lift.Length != lift.Length) m.Lift = new sbyte[lift.Length];   // once per map size
                Array.Copy(lift, m.Lift, lift.Length);
            }
            m.Epoch = Kaiju.ClearEpoch;
            m.Guard = Kaiju.LastGuard;
            m.N = n;
            Array.Copy(idx, m.Idx, n);
            Array.Copy(next, m.Next, n);
            m.Ok = ok;
        }

        /// <summary>The route guard proper (CellsKeepRoutes without the memo).</summary>
        private static bool CellsKeepRoutesUncached(World w, int[] idx, byte[] next, int n)
        {
            MapDef map = w.Map;
            byte[] cells = map.Cells;
            if (OldTypes.Length < n) OldTypes = new byte[n];
            for (int k = 0; k < n; k++)
            {
                OldTypes[k] = cells[idx[k]];
                cells[idx[k]] = next[k];
            }
            bool ok = false;
            try
            {
                // b14-c item 2: and the Kaiju's body still reaches the hill (Kaiju.RouteGuard.cs; Full only; tentative cells written)
                ok = PadsReachHill(map) && Kaiju.TentativeKeepsKaijuRoute(w, idx, next, OldTypes, n);
                if (ok)
                {
                    int after = PadsReached(map, ShrineRing(w));
                    bool aAfter = (after & 1) != 0;
                    bool cAfter = (after & 2) != 0;
                    if (!aAfter || !cAfter)
                    {
                        for (int k = 0; k < n; k++) cells[idx[k]] = OldTypes[k];
                        int g = PadsReached(map, ShrineRing(w));
                        // Refuse only a NEW disconnection: a pad already cut off from the shrine is not this change's doing.
                        if ((!aAfter && (g & 1) != 0) || (!cAfter && (g & 2) != 0)) ok = false;
                    }
                }
            }
            finally
            {
                for (int k = 0; k < n; k++) cells[idx[k]] = OldTypes[k];
            }
            return ok;
        }

        /// <summary>Would removing cell index i (made Void) keep the routes? (browser digKeepsRoutes)</summary>
        public static bool DigKeepsRoutes(World w, int i)
        {
            OneCell[0] = i;
            OneType[0] = Cell.Void;
            return CellsKeepRoutes(w, OneCell, OneType, 1);
        }

        // -----------------------------------------------------------------------------------------------------------
        // Targeting and rules

        /// <summary>
        /// The cell miner id would dig / place on for the aim point: the aim pulled back to Reach from the miner, floored.
        /// Non-finite aim gives the point Reach ahead along facing. False when the point is outside the map.
        /// </summary>
        public static bool TargetCell(World w, int id, double aimX, double aimZ, out int cx, out int cz)
        {
            Abilities.ClampToRange(w.X[id], w.Z[id], aimX, aimZ, w.Actors[id].Facing, w.Config.Abilities.MinerTerraform.Reach, out double tx, out double tz);
            cx = JsMath.FloorToInt(tx);
            cz = JsMath.FloorToInt(tz);
            MapDef map = w.Map;
            return cx >= 0 && cz >= 0 && cx < map.Width && cz < map.Height;
        }

        /// <summary>Ticks until team may change terrain again (0 = now): the team cooldown and the world-wide gap, whichever is later.</summary>
        public static int WaitTicks(World w, TeamId team)
        {
            int ready = Math.Max(w.Terraform.TeamReadyTick[(int)team], w.WallChangeTick + Durations.T30(w.Config, w.Config.Abilities.MinerTerraform.MinGapTicks));
            return ready > w.Tick ? ready - w.Tick : 0;
        }

        /// <summary>Does a circle of radius r at (x, z) overlap cell (cx, cz)'s unit square (strictly)?</summary>
        public static bool CircleHitsCell(double x, double z, double r, int cx, int cz)
        {
            double nx = x < cx ? cx : x > cx + 1 ? cx + 1 : x;
            double nz = z < cz ? cz : z > cz + 1 ? cz + 1 : z;
            double dx = x - nx;
            double dz = z - nz;
            return dx * dx + dz * dz < r * r;
        }

        /// <summary>True when a living actor or the hatched Kaiju (radius + ActorMargin) overlaps cell (cx, cz).</summary>
        public static bool CellOccupied(World w, int cx, int cz)
        {
            GameConfig cfg = w.Config;
            double margin = cfg.Abilities.MinerTerraform.ActorMargin;
            for (int j = 0; j < w.ActorCount; j++)
            {
                Actor a = w.Actors[j];
                if (!a.Alive) continue;
                if (CircleHitsCell(w.X[j], w.Z[j], cfg.Class(a.Cls).Radius + margin, cx, cz)) return true;
            }
            Kaiju kj = w.Kaiju;
            return kj.State != KaijuState.Egg && CircleHitsCell(kj.X, kj.Z, cfg.Kaiju.Radius + margin, cx, cz);
        }

        /// <summary>
        /// 0 when cell (cx, cz) may be dug right now (ignoring carry and rate cap), else a Refused* code. b15 terrain height:
        /// a Floor / BreakableDestroyed cell under the height rule loses one level instead (TerrainHeight.DigRefusalBeforeRoute,
        /// then the lift route guard); every other cell, and every cell without the rule, keeps the Void dig (VoidDigRefusal).
        /// </summary>
        public static int DigRefusal(World w, int cx, int cz)
        {
            if (LiftDig(w, cx, cz))
            {
                int hw = TerrainHeight.DigRefusalBeforeRoute(w, cx, cz);
                if (hw != 0) return hw;
                int i = cz * w.Map.Width + cx;
                if (w.Map.Cells[i] == Cell.Bridge)
                {
                    // a height ladder: the proof runs with its ground back and one level off a raised cell
                    TerrainHeight.LadderGround(w, i, out byte prev);
                    int l = w.Map.Lift[i];
                    return LiftKeepsRoutes(w, i, (sbyte)(l > 0 ? l - 1 : l), prev) ? 0 : RefusedRoute;
                }
                return TerrainHeight.KeepsRoutes(w, i, w.Map.Lift[i] - 1) ? 0 : RefusedRoute;
            }
            return VoidDigRefusal(w, cx, cz);
        }

        /// <summary>The pre-b15 dig rule: cell (cx, cz) may become Void now (TNT craters use it with the height rule on too).</summary>
        public static int VoidDigRefusal(World w, int cx, int cz)
        {
            int why = VoidRefusalBeforeRoute(w, cx, cz);
            if (why != 0) return why;
            return DigKeepsRoutes(w, cz * w.Map.Width + cx) ? 0 : RefusedRoute;
        }

        /// <summary>b15: a dig on (cx, cz) changes its level (the height rule is on and the cell carries a lift).</summary>
        public static bool LiftDig(World w, int cx, int cz)
        {
            MapDef map = w.Map;
            if (map.Lift == null || cx < 0 || cz < 0 || cx >= map.Width || cz >= map.Height) return false;
            int i = cz * map.Width + cx;
            if (map.Cells[i] == Cell.Bridge) return TerrainHeight.LadderGround(w, i, out _);   // a height ladder: it comes down
            return TerrainHeight.IsLiftType(map.Cells[i]);
        }

        /// <summary>
        /// Every DigRefusal rule except the route guard, in the same order (terrain-affordances view: the route proof depends
        /// on the grid alone, so the view caches it per World.GridVersion; DigRefusal = this, then DigKeepsRoutes).
        /// </summary>
        public static int DigRefusalBeforeRoute(World w, int cx, int cz)
        {
            if (LiftDig(w, cx, cz)) return TerrainHeight.DigRefusalBeforeRoute(w, cx, cz);   // b15
            return VoidRefusalBeforeRoute(w, cx, cz);
        }

        /// <summary>The pre-b15 DigRefusalBeforeRoute (the Void dig): every rule but the route guard.</summary>
        public static int VoidRefusalBeforeRoute(World w, int cx, int cz)
        {
            MapDef map = w.Map;
            if (cx < 0 || cz < 0 || cx >= map.Width || cz >= map.Height) return RefusedOutside;
            int i = cz * map.Width + cx;
            byte t = map.Cells[i];
            if (t != Cell.Floor && t != Cell.Bridge) return RefusedTarget;
            double x = cx + 0.5;
            double z = cz + 0.5;
            const double pk2 = Barricades.PadKeepout * Barricades.PadKeepout;
            for (int team = 0; team < 2; team++)
            {
                PadDef p = map.Pads[team];
                double pdx = x - p.X;
                double pdz = z - p.Z;
                if (pdx * pdx + pdz * pdz <= pk2) return RefusedOccupied;
            }
            double dx = x - map.Hill.X;
            double dz = z - map.Hill.Z;
            if (dx * dx + dz * dz < map.Hill.R * map.Hill.R) return RefusedOccupied;
            dx = x - map.Shrine.X;
            dz = z - map.Shrine.Z;
            if (dx * dx + dz * dz <= Barricades.ShrineKeepout * Barricades.ShrineKeepout) return RefusedOccupied;
            for (int k = 0; k < w.Nodes.Length; k++)
            {
                ResourceNode nd = w.Nodes[k];
                dx = x - nd.X;
                dz = z - nd.Z;
                if (dx * dx + dz * dz < Barricades.NodeKeepout * Barricades.NodeKeepout) return RefusedOccupied;
            }
            // Nobody may end up standing in a hole: any living actor (or the hatched Kaiju) touching the square blocks the dig.
            if (CellOccupied(w, cx, cz)) return RefusedOccupied;
            return 0;
        }

        /// <summary>0 when a block may be placed on cell (cx, cz) right now (ignoring carry and rate cap), else a Refused* code.</summary>
        public static int PlaceRefusal(World w, int cx, int cz)
        {
            MapDef map = w.Map;
            if (cx < 0 || cz < 0 || cx >= map.Width || cz >= map.Height) return RefusedOutside;
            byte t = map.Cells[cz * map.Width + cx];
            if (t != Cell.Void && t != Cell.Water) return RefusedTarget;
            if (!MapGrid.IsWalkable(map, cx + 1, cz) && !MapGrid.IsWalkable(map, cx - 1, cz)
                && !MapGrid.IsWalkable(map, cx, cz + 1) && !MapGrid.IsWalkable(map, cx, cz - 1))
            {
                return RefusedTarget;
            }
            return 0;
        }

        /// <summary>
        /// b15: 0 when a PLACE on (cx, cz) may happen now (carry and rate cap aside): a hole (PlaceRefusal) or, under the height
        /// rule, one more level on a lift cell (TerrainHeight.StackRefusal, then the lift route guard).
        /// </summary>
        public static int PlaceVerdict(World w, int cx, int cz)
        {
            if (!LiftDig(w, cx, cz)) return PlaceRefusal(w, cx, cz);
            int why = TerrainHeight.StackRefusal(w, cx, cz);
            if (why == BuilderWalls.RefusedOutside) return RefusedOutside;
            if (why == BuilderWalls.RefusedGround) return RefusedTarget;
            if (why == BuilderWalls.RefusedKeepOut) return RefusedOccupied;
            if (why != 0) return why;
            int i = cz * w.Map.Width + cx;
            return TerrainHeight.KeepsRoutes(w, i, w.Map.Lift[i] + 1) ? 0 : RefusedRoute;
        }

        /// <summary>
        /// b15 route guard for a lift change: with cell i at lift next, both pads still reach the hill and no pad that reaches
        /// the shrine ring now loses it (the fields respect height). The tentative write is restored before returning. The
        /// Kaiju ignores height, so its route is not part of this proof.
        /// </summary>
        public static bool LiftKeepsRoutes(World w, int i, sbyte next, byte cellType = 255)
        {
            MapDef map = w.Map;
            sbyte[] lift = map.Lift;
            sbyte old = lift[i];
            byte oldCell = map.Cells[i];
            lift[i] = next;
            if (cellType != 255) map.Cells[i] = cellType;   // b15 height ladder: the proof with its ground back
            bool ok = false;
            try
            {
                ok = PadsReachHill(map);
                if (ok)
                {
                    // integrator b15: #213 replaced the scratch flow fields with GridReach (now height-aware, the same yes/no)
                    int after = PadsReached(map, ShrineRing(w));
                    bool aAfter = (after & 1) != 0;
                    bool cAfter = (after & 2) != 0;
                    if (!aAfter || !cAfter)
                    {
                        lift[i] = old;
                        map.Cells[i] = oldCell;
                        int g = PadsReached(map, ShrineRing(w));
                        if ((!aAfter && (g & 1) != 0) || (!cAfter && (g & 2) != 0)) ok = false;
                    }
                }
            }
            finally
            {
                lift[i] = old;
                map.Cells[i] = oldCell;
            }
            return ok;
        }

        private static int Refuse(World w, int id, int i, double x, double z, int code)
        {
            World.PushEvent(w, SimEventType.Terraform, id, i, x, z, code);
            return code;
        }

        private static void Commit(World w, int id, int cx, int cz, byte type)
        {
            TeamId team = w.Actors[id].Team;
            MapGrid.SetCell(w, cx, cz, type);
            w.Terraform.TeamReadyTick[(int)team] = w.Tick + Durations.T30(w.Config, w.Config.Abilities.MinerTerraform.TeamCooldownTicks);
        }

        /// <summary>
        /// CM b13 B: a Builder's BLOCK into a hole fills it to Floor (the old Miner place: permanent, PlaceRefusal and WaitTicks
        /// already checked by BuilderWalls). Same commit as a place (team cooldown, wall change tick) and one Terraform Placed event.
        /// </summary>
        public static void CommitFill(World w, int id, int cx, int cz)
        {
            Commit(w, id, cx, cz, Cell.Floor);
            World.PushEvent(w, SimEventType.Terraform, id, cz * w.Map.Width + cx, cx + 0.5, cz + 0.5, Placed);
        }

        /// <summary>DG-09: blocks a miner may carry (MinerTerraform.CarryMax, at least 1, at most 255).</summary>
        public static int CarryMax(GameConfig c)
        {
            double m = c.Abilities.MinerTerraform.CarryMax;
            return m < 1 ? 1 : m > 255 ? 255 : (int)m;
        }

        /// <summary>
        /// Miner id digs the cell for the aim point. Returns Dug or the refusal code (cell unchanged, Terraform event pushed
        /// either way). Checks alive / miner / carry not full / rate cap / DigRefusal in that order.
        /// </summary>
        public static int Dig(World w, int id, double aimX, double aimZ)
        {
            Actor a = w.Actors[id];
            double ax = double.IsFinite(aimX) ? aimX : w.X[id];
            double az = double.IsFinite(aimZ) ? aimZ : w.Z[id];
            TerraformState t = w.Terraform;
            if (!a.Alive || a.Cls != OwnerClass(w) || t.Carried[id] >= CarryMax(w.Config)) return Refuse(w, id, -1, ax, az, RefusedTarget);
            if (!TargetCell(w, id, aimX, aimZ, out int cx, out int cz)) return Refuse(w, id, -1, ax, az, RefusedOutside);
            int i = cz * w.Map.Width + cx;
            if (WaitTicks(w, a.Team) > 0) return Refuse(w, id, i, cx + 0.5, cz + 0.5, RefusedRate);
            int why = DigRefusal(w, cx, cz);
            if (why != 0) return Refuse(w, id, i, cx + 0.5, cz + 0.5, why);
            if (LiftDig(w, cx, cz)) TerrainHeight.CommitDig(w, id, cx, cz);   // b15: one level off (a top block, or a pit one deeper)
            else Commit(w, id, cx, cz, Cell.Void);
            t.Carried[id]++;
            if (t.Carried[id] >= CarryMax(w.Config)) t.Mode[id] = (byte)TerraformMode.Place; // DG-09: a full carry switches to Place
            World.PushEvent(w, SimEventType.Terraform, id, i, cx + 0.5, cz + 0.5, Dug);
            return Dug;
        }

        /// <summary>
        /// Miner id places one carried block on the cell for the aim point. Returns Placed or the refusal code (cell and carry
        /// unchanged, Terraform event pushed either way).
        /// </summary>
        public static int Place(World w, int id, double aimX, double aimZ)
        {
            Actor a = w.Actors[id];
            double ax = double.IsFinite(aimX) ? aimX : w.X[id];
            double az = double.IsFinite(aimZ) ? aimZ : w.Z[id];
            TerraformState t = w.Terraform;
            if (!a.Alive || a.Cls != OwnerClass(w) || t.Carried[id] == 0) return Refuse(w, id, -1, ax, az, RefusedTarget);
            if (!TargetCell(w, id, aimX, aimZ, out int cx, out int cz)) return Refuse(w, id, -1, ax, az, RefusedOutside);
            int i = cz * w.Map.Width + cx;
            if (WaitTicks(w, a.Team) > 0) return Refuse(w, id, i, cx + 0.5, cz + 0.5, RefusedRate);
            int why = PlaceVerdict(w, cx, cz);   // b15: a hole as before, or one more level on a lift cell
            if (why != 0) return Refuse(w, id, i, cx + 0.5, cz + 0.5, why);
            if (LiftDig(w, cx, cz)) TerrainHeight.CommitStack(w, id, cx, cz);
            else Commit(w, id, cx, cz, Cell.Floor);
            t.Carried[id]--;
            if (t.Carried[id] == 0) t.Mode[id] = (byte)TerraformMode.Dig; // DG-09: an empty carry switches back to Dig
            World.PushEvent(w, SimEventType.Terraform, id, i, cx + 0.5, cz + 0.5, Placed);
            return Placed;
        }

        /// <summary>
        /// b13 r2 (owner decision: terrain play belongs to the Builder and the Demolitionist, never the Miner): the class whose F
        /// digs and places. The BUILDER while the hut rule is on (Full profile, HutConfig.BuilderTerraform), else the Miner
        /// (Classic and PhysicsOnly unchanged). Same costs, reach, carry and team rate cap for whoever owns it.
        /// </summary>
        public static ClassId OwnerClass(World w)
        {
            return Blacksmith.On(w) && w.Config.Depth.Hut.BuilderTerraform ? ClassId.Builder : ClassId.Miner;
        }

        /// <summary>DG-09: actor id's current dig / place mode.</summary>
        public static TerraformMode ModeOf(World w, int id)
        {
            return w.Terraform.Mode[id] == (byte)TerraformMode.Place ? TerraformMode.Place : TerraformMode.Dig;
        }

        /// <summary>
        /// Per step (the end of Abilities.AbilitiesSystem, so it uses the positions the previous step ended with): a dead
        /// actor loses its blocks (mode back to Dig); a living miner flips its mode on InputFrame.TerraformSwitch (read and
        /// cleared here); a living, standing miner acts on the RISING edge of InputFrame.Terraform (one action per press),
        /// digging in Dig mode and placing in Place mode. The aim follows the HasAim rule: no aim = straight ahead.
        /// </summary>
        public static void TerraformSystem(World w)
        {
            TerraformState t = w.Terraform;
            int n = w.ActorCount;
            for (int i = 0; i < n; i++)
            {
                Actor a = w.Actors[i];
                InputFrame inp = w.Inputs[i];
                bool flip = inp.TerraformSwitch;
                inp.TerraformSwitch = false;
                byte pressed = inp.Terraform ? (byte)1 : (byte)0;
                bool rising = pressed == 1 && t.PrevPressed[i] == 0;
                t.PrevPressed[i] = pressed;
                if (!a.Alive)
                {
                    t.Carried[i] = 0;
                    t.Mode[i] = (byte)TerraformMode.Dig;
                    continue;
                }
                // CM b13: a switch press, then a rising F press, are offered to the items first (bookkeeping above still ran).
                bool b13Switch = flip && B13Hooks.TakeTerraformSwitch(w, i);   // b13-hook
                bool b13Press = rising && B13Hooks.TakeTerraformPress(w, i);   // b13-hook
                if (b13Switch || b13Press) continue;   // b13-hook
                if (a.Cls != OwnerClass(w)) continue;   // b13 r2: the Builder while the hut rule is on, else the Miner
                if (flip) t.Mode[i] = t.Mode[i] == (byte)TerraformMode.Place ? (byte)TerraformMode.Dig : (byte)TerraformMode.Place;
                if (!rising || a.StandUp > 0) continue;
                bool hasAim = w.HasAim[i] == 1;
                double ax = hasAim ? inp.AimX : double.NaN;
                double az = hasAim ? inp.AimZ : double.NaN;
                if (t.Mode[i] == (byte)TerraformMode.Place) Place(w, i, ax, az);
                else Dig(w, i, ax, az);
            }
        }

        /// <summary>Is the cell under (x, z) walkable ground?</summary>
        public static bool WalkableAt(MapDef map, double x, double z)
        {
            int cx = JsMath.FloorToInt(x);
            int cz = JsMath.FloorToInt(z);
            if (cx < 0 || cz < 0 || cx >= map.Width || cz >= map.Height) return false;
            return MapGrid.IsWalkableType(map.Cells[cz * map.Width + cx]);
        }

        /// <summary>
        /// The nearest cell (centre within Reach of miner id, so the aim clamp keeps it) where a block may be placed right
        /// now, as its centre. Deterministic scan (nearest first, ties by row then column). False when there is none.
        /// </summary>
        public static bool FindPlaceCell(World w, int id, out double x, out double z)
        {
            double ox = w.X[id];
            double oz = w.Z[id];
            double reach = w.Config.Abilities.MinerTerraform.Reach;
            double r2 = reach * reach;
            double best = double.PositiveInfinity;
            x = 0;
            z = 0;
            int x0 = JsMath.FloorToInt(ox - reach);
            int x1 = JsMath.FloorToInt(ox + reach);
            int z0 = JsMath.FloorToInt(oz - reach);
            int z1 = JsMath.FloorToInt(oz + reach);
            for (int cz = z0; cz <= z1; cz++)
            {
                for (int cx = x0; cx <= x1; cx++)
                {
                    double dx = cx + 0.5 - ox;
                    double dz = cz + 0.5 - oz;
                    double d2 = dx * dx + dz * dz;
                    if (d2 > r2 || d2 >= best) continue;
                    if (PlaceRefusal(w, cx, cz) != 0) continue;
                    best = d2;
                    x = cx + 0.5;
                    z = cz + 0.5;
                }
            }
            return best < double.PositiveInfinity;
        }

        // -----------------------------------------------------------------------------------------------------------
        // DG-09 fall rule (not in the browser, U-053)

        /// <summary>
        /// DG-09 fall rule, called by Movement for a grounded, living, standing actor after it counts UnsupportedTicks. When
        /// the actor has stood unsupported for more than Jump.CoyoteTicks ticks AND the cell under its centre is no longer
        /// walkable (its ground was dug or removed under it; a box corner over an edge or a diagonal deck is not a fall), it
        /// falls in and climbs out: it moves to the nearest cell centre within MinerTerraform.FallSearchRings rings where its
        /// footprint fits (nearest first, ties by row then column), or to its pad spawn point when there is none, with zero
        /// velocity and the knock-down get-up (Respawn.StandUpSeconds). No damage: bible p13, "no accidental fall deaths".
        /// Pushes one Fall event. Returns true when the actor fell.
        /// </summary>
        public static bool FallIfUnsupported(World w, int id)
        {
            Actor a = w.Actors[id];
            GameConfig cfg = w.Config;
            if (a.UnsupportedTicks <= Jump.CoyoteTicks(cfg)) return false;
            MapDef map = w.Map;
            double x = w.X[id];
            double z = w.Z[id];
            int ccx = JsMath.FloorToInt(x);
            int ccz = JsMath.FloorToInt(z);
            if (MapGrid.IsWalkable(map, ccx, ccz)) return false;
            int rings = (int)cfg.Abilities.MinerTerraform.FallSearchRings;
            double best = double.PositiveInfinity;
            double bx = 0;
            double bz = 0;
            for (int r = 1; r <= rings && !(best < double.PositiveInfinity); r++)
            {
                for (int cz = ccz - r; cz <= ccz + r; cz++)
                {
                    for (int cx = ccx - r; cx <= ccx + r; cx++)
                    {
                        if (Math.Abs(cx - ccx) != r && Math.Abs(cz - ccz) != r) continue; // ring r only
                        if (!MapGrid.IsWalkable(map, cx, cz)) continue;
                        double px = cx + 0.5;
                        double pz = cz + 0.5;
                        double dx = px - x;
                        double dz = pz - z;
                        double d2 = dx * dx + dz * dz;
                        if (d2 >= best) continue;
                        if (!Collide.FootprintClear(w, id, px, pz)) continue;
                        best = d2;
                        bx = px;
                        bz = pz;
                    }
                }
            }
            if (!(best < double.PositiveInfinity)) World.SpawnPoint(map, a.Team, World.TeamSlot(w, id), out bx, out bz);
            int cell = MapGrid.InBounds(map, ccx, ccz) ? ccz * map.Width + ccx : -1;
            w.X[id] = JsMath.F32(bx);
            w.Z[id] = JsMath.F32(bz);
            w.PrevX[id] = w.X[id];
            w.PrevZ[id] = w.Z[id];
            w.Vx[id] = JsMath.F32(0);
            w.Vz[id] = JsMath.F32(0);
            a.Moving = false;
            a.UnsupportedTicks = 0;
            a.JumpBuffer = 0;
            Jump.ClearJump(a);
            a.StandUp = cfg.Respawn.StandUpSeconds;
            World.PushEvent(w, SimEventType.Fall, id, cell, w.X[id], w.Z[id], a.StandUp);
            return true;
        }
    }
}
