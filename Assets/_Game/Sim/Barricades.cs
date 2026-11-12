using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// Miner barricades (CM-05, browser barricades.ts): a BARRICADE grid cell plus a slot in World.Barricades. Placement
    /// rules live in PlacementRefusal (CM-28: 0 or a reason code); the route proof is memoised per world by
    /// (cell index, GridVersion) and its tentative block is a raw cell write that is always restored.
    /// </summary>
    public static class Barricades
    {
        /// <summary>Placement keep-out radii from the CM-05 spec (BALANCE.md "Barricade keep-outs").</summary>
        public const int PadKeepout = 4, ShrineKeepout = 3, NodeKeepout = 1;

        /// <summary>BarricadeBroken event value: why the barricade went away.</summary>
        public const int BarricadeExpired = 0, BarricadeDestroyed = 1, BarricadeReplaced = 2;

        /// <summary>CM-28 refusal reasons, the value of the refused Ability event (always lt 0).</summary>
        public const int RefusedOccupied = -1, RefusedHill = -2, RefusedRoute = -3, RefusedRange = -4;

        /// <summary>Same snap rule as Combat.TickDown (kept local so Barricades and Combat do not depend on each other).</summary>
        private const double TimerSnap = 1e-6;

        // -----------------------------------------------------------------------------------------------------------
        // Scratch

        private static readonly PointXZ[] HillGoal = { new PointXZ(0, 0) };
        private static PointXZ[] ShrineGoals = new PointXZ[0];
        private static double ShrineKeyX = double.NaN;
        private static double ShrineKeyZ = double.NaN;
        private static double ShrineKeyR = double.NaN;
        private static FlowField Field;

        /// <summary>Per-world connectivity memo (browser ConnectivityMemo).</summary>
        private sealed class BarricadeMemo
        {
            /// <summary>GridVersion the proof for this cell index was made at; -1 = none.</summary>
            public int[] Version;
            public byte[] Ok;
        }

        private static readonly ConditionalWeakTable<World, BarricadeMemo> Memos = new ConditionalWeakTable<World, BarricadeMemo>();

        private static BarricadeMemo MemoFor(World world)
        {
            int n = world.Map.Width * world.Map.Height;
            if (!Memos.TryGetValue(world, out BarricadeMemo m) || m.Ok.Length != n)
            {
                if (m != null) Memos.Remove(world);
                m = new BarricadeMemo { Version = new int[n], Ok = new byte[n] };
                Array.Fill(m.Version, -1);
                Memos.Add(world, m);
            }
            return m;
        }

        /// <summary>Goal points for "reaches the shrine": every cell centre within ShrineInteractRange of the shrine centre.</summary>
        private static IReadOnlyList<PointXZ> ShrineRing(World world, MapDef map)
        {
            ShrineDef s = map.Shrine;
            double r = world.Config.KaijuFeed.ShrineInteractRange;
            if (s.X == ShrineKeyX && s.Z == ShrineKeyZ && r == ShrineKeyR) return ShrineGoals;
            var goals = new List<PointXZ>();
            for (int cz = JsMath.FloorToInt(s.Z - r); cz <= JsMath.FloorToInt(s.Z + r); cz++)
            {
                for (int cx = JsMath.FloorToInt(s.X - r); cx <= JsMath.FloorToInt(s.X + r); cx++)
                {
                    double dx = cx + 0.5 - s.X;
                    double dz = cz + 0.5 - s.Z;
                    if (dx * dx + dz * dz <= r * r) goals.Add(new PointXZ(cx + 0.5, cz + 0.5));
                }
            }
            ShrineGoals = goals.ToArray();
            ShrineKeyX = s.X;
            ShrineKeyZ = s.Z;
            ShrineKeyR = r;
            return ShrineGoals;
        }

        private static bool PadsReach(MapDef map, FlowField f)
        {
            int W = map.Width;
            PadDef a = map.Pads[(int)TeamId.Azure];
            PadDef c = map.Pads[(int)TeamId.Crimson];
            return double.IsFinite(f.Dist[JsMath.FloorToInt(a.Z) * W + JsMath.FloorToInt(a.X)])
                && double.IsFinite(f.Dist[JsMath.FloorToInt(c.Z) * W + JsMath.FloorToInt(c.X)]);
        }

        /// <summary>Both pads still reach the hill centre and the shrine ring with cell i blocked. Memoised by (i, GridVersion).</summary>
        private static bool ConnectivityOk(World world, int i)
        {
            BarricadeMemo memo = MemoFor(world);
            if (memo.Version[i] == world.GridVersion) return memo.Ok[i] == 1;
            MapDef map = world.Map;
            int n = map.Width * map.Height;
            if (Field == null || Field.Dist.Length != n) Field = new FlowField { Dist = new double[n], Version = 0 };
            byte[] cells = map.Cells;
            byte old = cells[i];
            cells[i] = Cell.Barricade;
            bool ok = false;
            try
            {
                HillGoal[0].X = map.Hill.X;
                HillGoal[0].Z = map.Hill.Z;
                ok = PadsReach(map, FlowField.ComputeFlowField(map, HillGoal, Field));
                if (ok) ok = PadsReach(map, FlowField.ComputeFlowField(map, ShrineRing(world, map), Field));
            }
            finally
            {
                cells[i] = old;
            }
            memo.Version[i] = world.GridVersion;
            memo.Ok[i] = ok ? (byte)1 : (byte)0;
            return ok;
        }

        // -----------------------------------------------------------------------------------------------------------
        // Placement

        /// <summary>The cell a miner would build on for the aim point (x, z): the aim clamped to PlaceRange from the miner, floored; false when off the map or not finite.</summary>
        public static bool BarricadeTargetCell(World w, int minerId, double x, double z, out int cx, out int cz)
        {
            cx = 0;
            cz = 0;
            if (!double.IsFinite(x) || !double.IsFinite(z)) return false;
            double mx = w.X[minerId];
            double mz = w.Z[minerId];
            double dx = x - mx;
            double dz = z - mz;
            double d2 = dx * dx + dz * dz;
            double range = w.Config.Abilities.MinerBarricade.PlaceRange;
            if (d2 > range * range)
            {
                double k = range / Math.Sqrt(d2);
                dx *= k;
                dz *= k;
            }
            cx = JsMath.FloorToInt(mx + dx);
            cz = JsMath.FloorToInt(mz + dz);
            MapDef map = w.Map;
            return cx >= 0 && cz >= 0 && cx < map.Width && cz < map.Height;
        }

        /// <summary>Every placement rule except cooldown and the per-miner cap: 0 = valid, else a reason.</summary>
        private static int PlacementRefusal(World world, int cx, int cz)
        {
            MapDef map = world.Map;
            int i = cz * map.Width + cx;
            byte t = map.Cells[i];
            if (t == Cell.Void || t == Cell.Water) return RefusedRange;
            if (t != Cell.Floor && t != Cell.Bridge) return RefusedOccupied;
            double x = cx + 0.5;
            double z = cz + 0.5;

            PadDef pa = map.Pads[(int)TeamId.Azure];
            double dx = x - pa.X;
            double dz = z - pa.Z;
            if (dx * dx + dz * dz <= PadKeepout * PadKeepout) return RefusedOccupied;
            PadDef pc = map.Pads[(int)TeamId.Crimson];
            dx = x - pc.X;
            dz = z - pc.Z;
            if (dx * dx + dz * dz <= PadKeepout * PadKeepout) return RefusedOccupied;
            dx = x - map.Hill.X;
            dz = z - map.Hill.Z;
            if (dx * dx + dz * dz < map.Hill.R * map.Hill.R) return RefusedHill;
            dx = x - map.Shrine.X;
            dz = z - map.Shrine.Z;
            if (dx * dx + dz * dz <= ShrineKeepout * ShrineKeepout) return RefusedOccupied;
            for (int k = 0; k < world.Nodes.Length; k++)
            {
                ResourceNode nd = world.Nodes[k];
                dx = x - nd.X;
                dz = z - nd.Z;
                if (dx * dx + dz * dz < NodeKeepout * NodeKeepout) return RefusedOccupied;
            }

            double minDist = world.Config.Abilities.MinerBarricade.MinDistanceFromActor;
            for (int j = 0; j < world.ActorCount; j++)
            {
                Actor a = world.Actors[j];
                if (!a.Alive) continue;
                double lim = minDist + world.Config.Class(a.Cls).Radius;
                dx = world.X[j] - x;
                dz = world.Z[j] - z;
                if (dx * dx + dz * dz < lim * lim) return RefusedOccupied;
            }

            return ConnectivityOk(world, i) ? 0 : RefusedRoute;
        }

        /// <summary>CM-28: 0 when miner minerId could place a barricade for (x, z) now (ignoring its cooldown), else the Refused* reason; a dead or non-miner actor reads RefusedOccupied.</summary>
        public static int BarricadeRefusal(World w, int minerId, double x, double z)
        {
            if (!(minerId >= 0 && minerId < w.ActorCount)) return RefusedOccupied;
            Actor a = w.Actors[minerId];
            if (!a.Alive || a.Cls != ClassId.Miner) return RefusedOccupied;
            if (!BarricadeTargetCell(w, minerId, x, z, out int cx, out int cz)) return RefusedRange;
            return PlacementRefusal(w, cx, cz);
        }

        /// <summary>Would miner minerId be allowed to place a barricade for (x, z) now (ignoring its cooldown)? Used by the aim preview and bots.</summary>
        public static bool CanPlaceBarricade(World w, int minerId, double x, double z)
        {
            return BarricadeRefusal(w, minerId, x, z) == 0;
        }

        /// <summary>Remove the barricade in slot: restore the stored cell type and push BarricadeBroken (value = reason).</summary>
        public static void RemoveBarricade(World w, int slot, int reason)
        {
            Barricade b = w.Barricades[slot];
            if (!b.Active) return;
            b.Active = false;
            MapDef map = w.Map;
            if (map.Cells[b.Cz * map.Width + b.Cx] == Cell.Barricade)
                MapGrid.SetCell(w, b.Cx, b.Cz, b.PrevCell == Cell.Bridge ? Cell.Bridge : Cell.Floor);
            World.PushEvent(w, SimEventType.BarricadeBroken, b.Owner, slot, b.Cx + 0.5, b.Cz + 0.5, reason);
        }

        /// <summary>Miner cast (cooldown already checked by AbilitiesSystem): place, replacing the oldest at the cap; on refusal push an Ability event with the reason and spend no cooldown. Returns whether it placed.</summary>
        public static bool TryPlaceBarricade(World w, int minerId, double aimX, double aimZ)
        {
            Actor a = w.Actors[minerId];
            int cx;
            int cz;
            int refusal = BarricadeTargetCell(w, minerId, aimX, aimZ, out cx, out cz)
                ? PlacementRefusal(w, cx, cz)
                : RefusedRange;
            if (refusal != 0)
            {
                World.PushEvent(w, SimEventType.Ability, minerId, -1, aimX, aimZ, refusal);
                return false;
            }

            MinerBarricadeConfig mb = w.Config.Abilities.MinerBarricade;
            Barricade[] list = w.Barricades;
            int owned = 0;
            int oldest = -1;
            int free = -1;
            for (int s = 0; s < list.Length; s++)
            {
                Barricade bs = list[s];
                if (!bs.Active)
                {
                    if (free < 0) free = s;
                    continue;
                }
                if (bs.Owner != minerId) continue;
                owned++;
                if (oldest < 0 || bs.Ttl < list[oldest].Ttl) oldest = s;
            }
            if (owned >= mb.MaxActivePerMiner && oldest >= 0)
            {
                RemoveBarricade(w, oldest, BarricadeReplaced);
                if (free < 0 || oldest < free) free = oldest;
            }
            if (free < 0)
            {
                World.PushEvent(w, SimEventType.Ability, minerId, -1, aimX, aimZ, RefusedOccupied);
                return false;
            }

            MapDef map = w.Map;
            Barricade b = list[free];
            b.Active = true;
            b.Owner = minerId;
            b.Team = a.Team;
            b.Cx = cx;
            b.Cz = cz;
            b.Hp = mb.MaxHp;
            b.Ttl = mb.LifetimeSeconds;
            b.PrevCell = map.Cells[cz * map.Width + cx];
            MapGrid.SetCell(w, cx, cz, Cell.Barricade);
            a.AbilityCd = w.Config.Class(ClassId.Miner).AbilityCooldown;
            a.AnimAction = 0;
            World.PushEvent(w, SimEventType.BarricadePlaced, minerId, free, cx + 0.5, cz + 0.5, 1);
            return true;
        }

        /// <summary>Cleave damage to ENEMY barricades (called by CombatSystem after a swing's actor hits): every active barricade of another team whose cell centre is within range and inside the arc loses damage hp.</summary>
        public static void DamageBarricadesInArc(World w, int attackerId, double ax, double az, double fx, double fz, double range, double cosHalf, double damage)
        {
            TeamId team = w.Actors[attackerId].Team;
            double r2 = range * range;
            Barricade[] list = w.Barricades;
            for (int s = 0; s < list.Length; s++)
            {
                Barricade b = list[s];
                if (!b.Active || b.Team == team || b.Hp <= 0) continue;
                double dx = b.Cx + 0.5 - ax;
                double dz = b.Cz + 0.5 - az;
                double d2 = dx * dx + dz * dz;
                if (d2 > r2) continue;
                if (d2 > 1e-12 && (dx * fx + dz * fz) / Math.Sqrt(d2) < cosHalf - 1e-9) continue;
                b.Hp -= damage;
            }
        }

        /// <summary>Ttl counts down; at ttl le 0 or hp le 0 the cell returns to its stored type and BarricadeBroken is pushed.</summary>
        public static void BarricadesSystem(World w, double dt)
        {
            Barricade[] list = w.Barricades;
            for (int s = 0; s < list.Length; s++)
            {
                Barricade b = list[s];
                if (!b.Active) continue;
                double r = b.Ttl - dt;
                b.Ttl = r > TimerSnap ? r : 0;
                if (b.Hp <= 0) RemoveBarricade(w, s, BarricadeDestroyed);
                else if (b.Ttl <= 0) RemoveBarricade(w, s, BarricadeExpired);
            }
        }
    }
}
