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
        private static FlowField Field;
        private static readonly int[] OneCell = new int[1];
        private static readonly byte[] OneType = new byte[1];
        private static byte[] OldTypes = new byte[16];

        private static FlowField ScratchField(MapDef map)
        {
            int n = map.Width * map.Height;
            if (Field == null || Field.Dist.Length != n) Field = new FlowField { Dist = new double[n], Version = 0 };
            return Field;
        }

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

        private static bool PadReached(MapDef map, FlowField f, TeamId team)
        {
            PadDef p = map.Pads[(int)team];
            int cx = JsMath.FloorToInt(p.X);
            int cz = JsMath.FloorToInt(p.Z);
            if (cx < 0 || cz < 0 || cx >= map.Width || cz >= map.Height) return false;
            return double.IsFinite(f.Dist[cz * map.Width + cx]);
        }

        /// <summary>True when both team pads reach the hill centre on the CURRENT grid (the soak samples this).</summary>
        public static bool PadsReachHill(MapDef map)
        {
            HillGoal[0].X = map.Hill.X;
            HillGoal[0].Z = map.Hill.Z;
            FlowField f = FlowField.ComputeFlowField(map, HillGoal, ScratchField(map));
            return PadReached(map, f, TeamId.Azure) && PadReached(map, f, TeamId.Crimson);
        }

        /// <summary>
        /// Route guard (CM-30, shared with CM-31 topples): with cells[idx[k]] = next[k] for k &lt; n, both pads still reach
        /// the hill, and no pad that reaches the shrine ring now loses it. The tentative raw write is restored before returning.
        /// </summary>
        public static bool CellsKeepRoutes(World w, int[] idx, byte[] next, int n)
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
                ok = PadsReachHill(map);
                if (ok)
                {
                    FlowField f = FlowField.ComputeFlowField(map, ShrineRing(w), ScratchField(map));
                    bool aAfter = PadReached(map, f, TeamId.Azure);
                    bool cAfter = PadReached(map, f, TeamId.Crimson);
                    if (!aAfter || !cAfter)
                    {
                        for (int k = 0; k < n; k++) cells[idx[k]] = OldTypes[k];
                        FlowField g = FlowField.ComputeFlowField(map, ShrineRing(w), ScratchField(map));
                        // Refuse only a NEW disconnection: a pad already cut off from the shrine is not this change's doing.
                        if ((!aAfter && PadReached(map, g, TeamId.Azure)) || (!cAfter && PadReached(map, g, TeamId.Crimson))) ok = false;
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

        /// <summary>0 when cell (cx, cz) may be dug right now (ignoring carry and rate cap), else a Refused* code.</summary>
        public static int DigRefusal(World w, int cx, int cz)
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
            return DigKeepsRoutes(w, i) ? 0 : RefusedRoute;
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
            if (!a.Alive || a.Cls != ClassId.Miner || t.Carried[id] >= CarryMax(w.Config)) return Refuse(w, id, -1, ax, az, RefusedTarget);
            if (!TargetCell(w, id, aimX, aimZ, out int cx, out int cz)) return Refuse(w, id, -1, ax, az, RefusedOutside);
            int i = cz * w.Map.Width + cx;
            if (WaitTicks(w, a.Team) > 0) return Refuse(w, id, i, cx + 0.5, cz + 0.5, RefusedRate);
            int why = DigRefusal(w, cx, cz);
            if (why != 0) return Refuse(w, id, i, cx + 0.5, cz + 0.5, why);
            Commit(w, id, cx, cz, Cell.Void);
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
            if (!a.Alive || a.Cls != ClassId.Miner || t.Carried[id] == 0) return Refuse(w, id, -1, ax, az, RefusedTarget);
            if (!TargetCell(w, id, aimX, aimZ, out int cx, out int cz)) return Refuse(w, id, -1, ax, az, RefusedOutside);
            int i = cz * w.Map.Width + cx;
            if (WaitTicks(w, a.Team) > 0) return Refuse(w, id, i, cx + 0.5, cz + 0.5, RefusedRate);
            int why = PlaceRefusal(w, cx, cz);
            if (why != 0) return Refuse(w, id, i, cx + 0.5, cz + 0.5, why);
            Commit(w, id, cx, cz, Cell.Floor);
            t.Carried[id]--;
            if (t.Carried[id] == 0) t.Mode[id] = (byte)TerraformMode.Dig; // DG-09: an empty carry switches back to Dig
            World.PushEvent(w, SimEventType.Terraform, id, i, cx + 0.5, cz + 0.5, Placed);
            return Placed;
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
                if (a.Cls != ClassId.Miner) continue;
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
