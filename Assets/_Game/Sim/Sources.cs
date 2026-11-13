using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>U-064: a wood grove or a water spring on the map (not a ResourceNode: the browser's bot fields never see it).</summary>
    public sealed class SourceNode
    {
        public int Id;
        /// <summary>BiomassType.Wood or BiomassType.Water.</summary>
        public BiomassType Kind;
        public double X;
        public double Z;
        public int Charges;
        public int MaxCharges;
        /// <summary>Sim ticks until the next charge grows back (0 while full).</summary>
        public int RegrowIn;
        /// <summary>Bot route to this source, built on first use and when walls changed (Sources.EnsureField).</summary>
        public FlowField Field;
        public int FieldWallVersion;
    }

    /// <summary>
    /// U-064 biomass sources (DG-42; Travis 2026-11-06: "Biomass sources / Meat / Wood / Water / Corruption"). Our rule; the
    /// browser has plant, meat and corruption only.
    /// <para>ON THE MAP. Wood groves and water springs (SourcesConfig anchors, placed on open floor, mirrored, DepthState.Sources).
    /// Any class holds interact beside one to gather (class GatherSeconds x the class's scale for that type: Builders and
    /// Miners chop fastest, Healers and Farmers draw water fastest). A hit resets the progress. Charges grow back.</para>
    /// <para>CARRY. Wood and water ride in the class cargo (CarryKind.Wood / Water, one kind at a time, class CarryCapacity)
    /// and are lost on a knock-out like any cargo.</para>
    /// <para>SPEND, two ways. (1) FEED: a feeder hands them to the egg: wood WoodAllegiancePerUnit, water
    /// WaterAllegiancePerUnit and each water unit COOLS the egg by WaterCoolPerUnit instability (never below 0, never after
    /// the berserk lock). (2) BANK: a press of interact inside the own base zone (BankRange of the pad) puts the load in the
    /// team STOCK (wood, water, food = plant and meat). Wood pays Builder pieces first (U-060), wood and water pay class
    /// upgrades (U-065) and bridge repairs (U-068); a feeder with empty hands takes food back out to carry it to the egg.</para>
    /// <para>Bots: before the hatch floor(team x SupplyFraction) hill bots of the supply classes (Builder, Miner,
    /// Demolitions) run AiState.Supply: nearest source with charges, fill up, bank it.</para>
    /// Deterministic (actor-id order, no RNG), allocation-free after the first field builds.
    /// </summary>
    public static class Sources
    {
        /// <summary>Team stock slots (DepthState.Stock[team * StockKinds + slot]).</summary>
        public const int StockWood = 0, StockWater = 1, StockFood = 2, StockKinds = 3;

        public static bool On(World w) => DepthRules.On(w, w.Config.Depth.Sources.Enabled);

        public static int Stock(World w, TeamId team, int slot) => w.Depth.Stock[(int)team * StockKinds + slot];

        /// <summary>Take up to amount from one stock slot; returns what was taken.</summary>
        public static int SpendUpTo(World w, TeamId team, int slot, double amount)
        {
            int i = (int)team * StockKinds + slot;
            int want = amount > 0 ? (int)Math.Floor(amount) : 0;
            int take = w.Depth.Stock[i] < want ? w.Depth.Stock[i] : want;
            w.Depth.Stock[i] -= take;
            w.Depth.StockSpent += take;
            return take;
        }

        /// <summary>Pay wood and water from the team stock, all or nothing.</summary>
        public static bool TrySpend(World w, TeamId team, int wood, int water)
        {
            if (Stock(w, team, StockWood) < wood || Stock(w, team, StockWater) < water) return false;
            SpendUpTo(w, team, StockWood, wood);
            SpendUpTo(w, team, StockWater, water);
            return true;
        }

        /// <summary>The cargo kind a source type rides as.</summary>
        public static CarryKind CarryOf(BiomassType kind) => kind == BiomassType.Water ? CarryKind.Water : CarryKind.Wood;

        /// <summary>Is (x, z) inside team's base zone (BankRange of its pad centre)?</summary>
        public static bool InBase(World w, TeamId team, double x, double z)
        {
            PadDef pad = w.Map.Pads[(int)team];
            double r = w.Config.Depth.Sources.BankRange;
            double dx = x - pad.X;
            double dz = z - pad.Z;
            return dx * dx + dz * dz <= r * r;
        }

        // ---------------------------------------------------------------- placement

        private static bool OpenFloor(MapDef map, int cx, int cz)
        {
            if (cx < 1 || cz < 1 || cx >= map.Width - 1 || cz >= map.Height - 1) return false;
            if (map.Cells[cz * map.Width + cx] != Cell.Floor) return false;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if (map.Cells[(cz + dz) * map.Width + cx + dx] != Cell.Floor) return false;
            return true;
        }

        private static bool FarFrom(double x, double z, double px, double pz, double r)
        {
            double dx = x - px;
            double dz = z - pz;
            return dx * dx + dz * dz >= r * r;
        }

        private static bool SpotOk(GameConfig cfg, MapDef map, List<SourceNode> placed, double x, double z)
        {
            SourcesConfig S = cfg.Depth.Sources;
            if (!FarFrom(x, z, map.Shrine.X, map.Shrine.Z, S.ShrineKeepOut)) return false;
            if (!FarFrom(x, z, map.Hill.X, map.Hill.Z, map.Hill.R + S.HillKeepOut)) return false;
            for (int t = 0; t < map.Pads.Length; t++) if (!FarFrom(x, z, map.Pads[t].X, map.Pads[t].Z, map.Pads[t].R + S.PadKeepOut)) return false;
            foreach (PointXZ p in map.BiomassNodes) if (!FarFrom(x, z, p.X, p.Z, S.NodeKeepOut)) return false;
            foreach (PointXZ p in map.CorruptionNodes) if (!FarFrom(x, z, p.X, p.Z, S.NodeKeepOut)) return false;
            for (int i = 0; i < placed.Count; i++) if (!FarFrom(x, z, placed[i].X, placed[i].Z, S.NodeKeepOut)) return false;
            return true;
        }

        /// <summary>
        /// The open-floor cell nearest the anchor (rings 0..PlaceSearchRings, nearest first, ties by row then column) whose
        /// mirror cell is open floor too and that keeps the keep-outs; false when there is none.
        /// </summary>
        private static bool FindCell(GameConfig cfg, MapDef map, List<SourceNode> placed, double ax, double az, out int outCx, out int outCz)
        {
            int acx = JsMath.FloorToInt(ax);
            int acz = JsMath.FloorToInt(az);
            int rings = (int)cfg.Depth.Sources.PlaceSearchRings;
            outCx = -1;
            outCz = -1;
            for (int r = 0; r <= rings; r++)
            {
                double best = double.PositiveInfinity;
                for (int cz = acz - r; cz <= acz + r; cz++)
                {
                    for (int cx = acx - r; cx <= acx + r; cx++)
                    {
                        if (Math.Abs(cx - acx) != r && Math.Abs(cz - acz) != r) continue;
                        if (!OpenFloor(map, cx, cz) || !OpenFloor(map, map.Width - 1 - cx, cz)) continue;
                        double x = cx + 0.5;
                        double z = cz + 0.5;
                        if (!SpotOk(cfg, map, placed, x, z) || !SpotOk(cfg, map, placed, map.Width - x, z)) continue;
                        double d2 = (x - ax) * (x - ax) + (z - az) * (z - az);
                        if (d2 >= best) continue;
                        best = d2;
                        outCx = cx;
                        outCz = cz;
                    }
                }
                if (outCx >= 0) return true;
            }
            return false;
        }

        private static void AddAnchors(GameConfig cfg, MapDef map, List<SourceNode> list, double[] anchors, BiomassType kind, int charges)
        {
            for (int k = 0; k + 1 < anchors.Length; k += 2)
            {
                if (!FindCell(cfg, map, list, anchors[k], anchors[k + 1], out int cx, out int cz)) continue;
                double x = cx + 0.5;
                double z = cz + 0.5;
                list.Add(new SourceNode { Id = list.Count, Kind = kind, X = x, Z = z, MaxCharges = charges, Charges = charges, FieldWallVersion = -1 });
                double mx = map.Width - x;
                if (Math.Abs(mx - x) < 1.5) continue; // an anchor on the centre line is one source, not a pair
                list.Add(new SourceNode { Id = list.Count, Kind = kind, X = mx, Z = z, MaxCharges = charges, Charges = charges, FieldWallVersion = -1 });
            }
        }

        /// <summary>Place every source for this map (World.CreateWorld, once): wood first, then water; each left anchor with its mirror.</summary>
        public static SourceNode[] Place(GameConfig cfg, MapDef map)
        {
            SourcesConfig S = cfg.Depth.Sources;
            var list = new List<SourceNode>();
            AddAnchors(cfg, map, list, S.WoodAnchors, BiomassType.Wood, (int)S.WoodCharges);
            AddAnchors(cfg, map, list, S.WaterAnchors, BiomassType.Water, (int)S.WaterCharges);
            return list.ToArray();
        }

        // ---------------------------------------------------------------- gather, regrow, bank

        /// <summary>Gather time multiplier of a class for a source type.</summary>
        public static double GatherScale(GameConfig cfg, ClassId cls, BiomassType kind)
        {
            double[] m = kind == BiomassType.Water ? cfg.Depth.Sources.WaterGatherScale : cfg.Depth.Sources.WoodGatherScale;
            int k = (int)cls;
            return k >= 0 && k < m.Length ? m[k] : 1;
        }

        /// <summary>The source actor id can gather from right now, or -1 (in reach, charges left, cargo of that kind or none, room). Prefers the one it is working.</summary>
        public static int SourceInReach(World w, int id)
        {
            Actor a = w.Actors[id];
            if (a.CarryAmount >= w.Config.Class(a.Cls).CarryCapacity) return -1;
            double reach = w.Config.Resources.NodeInteractRange + Collide.ActorRadius(w, id);
            double r2 = reach * reach;
            SourceNode[] list = w.Depth.Sources;
            int best = -1;
            double bestD2 = double.PositiveInfinity;
            for (int i = 0; i < list.Length; i++)
            {
                SourceNode s = list[i];
                if (s.Charges <= 0) continue;
                if (a.Carry != CarryKind.None && a.CarryAmount > 0 && a.Carry != CarryOf(s.Kind)) continue;
                double dx = s.X - w.X[id];
                double dz = s.Z - w.Z[id];
                double d2 = dx * dx + dz * dz;
                if (d2 > r2) continue;
                if (i == w.Depth.SourceOf[id]) return i;
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>One step of sources: regrow, gathering, then the bank (SimStep, after ResourcesSystem and the hand-offs).</summary>
        public static void SourcesSystem(World w, double dt)
        {
            if (!On(w)) return;
            DepthState d = w.Depth;
            SourcesConfig S = w.Config.Depth.Sources;
            SourceNode[] list = d.Sources;
            for (int i = 0; i < list.Length; i++)
            {
                SourceNode s = list[i];
                if (s.Charges >= s.MaxCharges)
                {
                    s.RegrowIn = 0;
                    continue;
                }
                if (s.RegrowIn <= 0) s.RegrowIn = Durations.T30(w.Config, s.Kind == BiomassType.Water ? S.WaterRegrowTicks : S.WoodRegrowTicks);
                if (--s.RegrowIn <= 0) s.Charges++;
            }

            int n = w.ActorCount;
            for (int id = 0; id < n; id++)
            {
                Actor a = w.Actors[id];
                bool interact = w.Inputs[id].Interact && a.Alive && a.StandUp <= 0;
                bool hit = a.Hp < d.PrevHp[id];
                d.PrevHp[id] = a.Hp;
                bool edge = interact && d.PrevInteract[id] == 0;
                if (!interact)
                {
                    d.SourceProgress[id] = 0;
                    d.SourceOf[id] = -1;
                    continue;
                }
                if (edge && InBase(w, a.Team, w.X[id], w.Z[id]) && Bank(w, id)) continue;
                // Gathering: not while working a plant / corruption node or winding up a delivery.
                int si = a.GatherNodeId >= 0 || a.DeliverTicks != 0 ? -1 : SourceInReach(w, id);
                if (si < 0)
                {
                    d.SourceProgress[id] = 0;
                    d.SourceOf[id] = -1;
                    continue;
                }
                if (si != d.SourceOf[id] || hit)
                {
                    d.SourceProgress[id] = 0;
                    d.SourceOf[id] = si;
                }
                d.SourceProgress[id] += dt;
                SourceNode s = list[si];
                double need = w.Config.Class(a.Cls).GatherSeconds * GatherScale(w.Config, a.Cls, s.Kind);
                if (d.SourceProgress[id] >= need - Combat.TimerEps)
                {
                    s.Charges -= 1;
                    a.Carry = CarryOf(s.Kind);
                    a.CarryAmount += 1;
                    d.SourceProgress[id] = 0;
                    d.Gathered[s.Kind == BiomassType.Water ? 1 : 0]++;
                    World.PushEvent(w, SimEventType.SourceGather, id, si, s.X, s.Z, a.CarryAmount, s.Kind, DamageCause.None);
                }
            }
        }

        /// <summary>
        /// One bank press of actor id inside its base zone. A load that is not corruption goes into the stock (BankDeposit:
        /// A = actor, B = team index, (X, Z) = the pad, Value = units, Bio = the type; meat is its own event). A feeder with
        /// empty hands takes food out, up to its capacity (BankWithdraw, same fields, Bio = Plant). True when anything moved.
        /// </summary>
        public static bool Bank(World w, int id)
        {
            Actor a = w.Actors[id];
            DepthState d = w.Depth;
            int t = (int)a.Team;
            PadDef pad = w.Map.Pads[t];
            bool moved = false;
            if (a.CarryAmount > 0 && a.Carry != CarryKind.None && a.Carry != CarryKind.Corruption)
            {
                int slot = a.Carry == CarryKind.Wood ? StockWood : a.Carry == CarryKind.Water ? StockWater : StockFood;
                BiomassType bio = a.Carry == CarryKind.Wood ? BiomassType.Wood : a.Carry == CarryKind.Water ? BiomassType.Water : BiomassType.Plant;
                d.Stock[t * StockKinds + slot] += a.CarryAmount;
                d.Banked += a.CarryAmount;
                World.PushEvent(w, SimEventType.BankDeposit, id, t, pad.X, pad.Z, a.CarryAmount, bio, DamageCause.None);
                a.Carry = CarryKind.None;
                a.CarryAmount = 0;
                a.GatherProgress = 0;
                moved = true;
            }
            if (a.CarryMeat > 0)
            {
                d.Stock[t * StockKinds + StockFood] += a.CarryMeat;
                d.Banked += a.CarryMeat;
                World.PushEvent(w, SimEventType.BankDeposit, id, t, pad.X, pad.Z, a.CarryMeat, BiomassType.Meat, DamageCause.None);
                a.CarryMeat = 0;
                moved = true;
            }
            if (moved) return true;
            bool egg = w.Kaiju.State == KaijuState.Egg && !w.Match.Hatched;
            int food = d.Stock[t * StockKinds + StockFood];
            if (!egg || food <= 0 || a.CarryAmount > 0 || !Roles.On(w) || !Roles.MayFeed(w, a.Cls)) return false;
            int cap = (int)w.Config.Class(a.Cls).CarryCapacity;
            int take = food < cap ? food : cap;
            d.Stock[t * StockKinds + StockFood] -= take;
            d.Withdrawn += take;
            a.Carry = CarryKind.Biomass;
            a.CarryAmount = take;
            World.PushEvent(w, SimEventType.BankWithdraw, id, t, pad.X, pad.Z, take, BiomassType.Plant, DamageCause.None);
            return true;
        }

        // ---------------------------------------------------------------- bot routes

        private static readonly PointXZ[] Goal = new PointXZ[1];

        /// <summary>
        /// Make source i's route field usable: built on first use; rebuilt when walls changed, at most one rebuild per tick
        /// over all sources (a stale field is still followed meanwhile).
        /// </summary>
        public static FlowField EnsureField(World w, int i)
        {
            DepthState d = w.Depth;
            SourceNode s = d.Sources[i];
            bool fresh = s.Field == null;
            if (!fresh && s.FieldWallVersion == w.WallVersion) return s.Field;
            if (!fresh && d.FieldBuildTick == w.Tick) return s.Field;
            Goal[0] = new PointXZ(s.X, s.Z);
            s.Field = FlowField.ComputeFlowField(w.Map, Goal, s.Field, w.WallVersion);
            s.FieldWallVersion = w.WallVersion;
            if (!fresh) d.FieldBuildTick = w.Tick;
            return s.Field;
        }

        /// <summary>Walking distance from (x, z) to source i (infinity when unreachable or off the map).</summary>
        public static double FieldDist(World w, int i, double x, double z)
        {
            FlowField f = EnsureField(w, i);
            int cx = JsMath.FloorToInt(x);
            int cz = JsMath.FloorToInt(z);
            if (cx < 0 || cz < 0 || cx >= w.Map.Width || cz >= w.Map.Height) return double.PositiveInfinity;
            return f.Dist[cz * w.Map.Width + cx];
        }

        /// <summary>The source with charges a bot should walk to: nearest by walking distance, of a kind its cargo allows; -1 = none.</summary>
        public static int BestSource(World w, int id, bool waterOnly = false)
        {
            Actor a = w.Actors[id];
            SourceNode[] list = w.Depth.Sources;
            int best = -1;
            double bestD = double.PositiveInfinity;
            for (int i = 0; i < list.Length; i++)
            {
                SourceNode s = list[i];
                if (s.Charges <= 0) continue;
                if (a.CarryAmount > 0 && a.Carry != CarryOf(s.Kind)) continue;
                if (waterOnly && s.Kind != BiomassType.Water) continue;
                double dist = FieldDist(w, i, w.X[id], w.Z[id]);
                if (dist < bestD)
                {
                    bestD = dist;
                    best = i;
                }
            }
            return best;
        }
    }

    /// <summary>U-064 on-screen names, in ONE place for the owner to rename. Data only.</summary>
    public static class SourceNames
    {
        public const string Wood = "WOOD", Water = "WATER", Food = "FOOD", Stock = "TEAM STOCK", Grove = "GROVE", Spring = "SPRING";
        public const string Bank = "BANK IT", Withdraw = "TAKE FOOD";
    }
}
