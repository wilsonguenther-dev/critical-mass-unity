using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// U-068 map events (our rule; the browser has none). From FirstEventTicks, every PeriodTicks, ONE event starts; the
    /// three kinds rotate, starting from a kind fixed by the match seed, so every match sees each of them and two matches
    /// on one seed see the same ones at the same ticks. A kind that cannot run (the egg has hatched, a bridge is already
    /// down) is replaced by a supply drop. Every stage pushes an event with a position for the HUD, the announcer and the
    /// big-moment camera.
    /// <para>SUPPLY DROP. Announced DropWarnTicks ahead on a centre-line spot (equally far from both bases), then it lands. A
    /// team CLAIMS it by standing within DropClaimRange for DropClaimTicks while no enemy stands there too (a contested drop
    /// does not tick for anyone). The claimer's team gets DropWood and DropWater in its stock, the claimer DropCoins, and a
    /// power-up appears on the spot. Unclaimed after DropLifetimeTicks it is gone.</para>
    /// <para>EGG EXPOSED. For EggWindowTicks every delivery to the egg, food or corruption, counts EggFeedScale times toward
    /// allegiance. Ends early at the hatch.</para>
    /// <para>BRIDGE COLLAPSE. One bridge span is announced BridgeWarnTicks ahead, then its middle third falls (bridge cells
    /// become void; anyone on it falls and climbs out, no damage, the DG-09 fall rule). Never a span whose loss would cut a
    /// base off from the hill or the shrine (the CM-30 route guard); with no span that passes, a supply drop runs instead.
    /// A BUILDER holding interact within BridgeRepairRange of either broken end for BridgeRepairTicks rebuilds it for
    /// BridgeRepairWood from the team stock; otherwise it rebuilds itself after BridgeAutoRepairTicks.</para>
    /// Deterministic (stateless hash of the seed, actor-id order), allocation-free.
    /// </summary>
    public static class MapEvents
    {
        public const int KindSupplyDrop = 0, KindBridge = 1, KindEgg = 2;
        public const int DropNone = 0, DropIncoming = 1, DropLanded = 2;
        public const int BridgeNone = 0, BridgeWarned = 1, BridgeBroken = 2;
        /// <summary>SupplyDropContest values: who stands on the drop.</summary>
        public const int ContestNobody = 0, ContestAzure = 1, ContestCrimson = 2, ContestBoth = 3;
        public const int MaxBrokenCells = 96;

        private static readonly byte[] VoidCells = MakeVoid();

        private static byte[] MakeVoid()
        {
            var b = new byte[MaxBrokenCells];
            for (int i = 0; i < b.Length; i++) b[i] = Cell.Void;
            return b;
        }

        public static bool On(World w) => DepthRules.On(w, w.Config.Depth.Events.Enabled);

        /// <summary>SimResources.ApplyDelivery: 1, or EggFeedScale while the egg is exposed.</summary>
        public static double FeedScale(World w)
        {
            if (w.Config.Depth.Classic || w.Depth.EggExposedUntil <= w.Tick) return 1;
            return w.Config.Depth.Events.EggFeedScale;
        }

        // ---------------------------------------------------------------- supply drop

        private static void StartDrop(World w, int k)
        {
            DepthState d = w.Depth;
            MapEventsConfig E = w.Config.Depth.Events;
            if (d.DropState != DropNone || d.DropSpots.Length == 0) return;
            PointXZ s = d.DropSpots[DepthRules.HashInt(w.Opts.Seed, k, 31, d.DropSpots.Length)];
            d.DropState = DropIncoming;
            d.DropX = s.X;
            d.DropZ = s.Z;
            d.DropTick = w.Tick + (int)E.DropWarnTicks;
            d.DropProgress[0] = 0;
            d.DropProgress[1] = 0;
            d.DropContest = ContestNobody;
            World.PushEvent(w, SimEventType.SupplyDropIncoming, -1, k, s.X, s.Z, d.DropTick, null, DamageCause.None, E.DropClaimRange);
        }

        private static void DropStep(World w)
        {
            DepthState d = w.Depth;
            MapEventsConfig E = w.Config.Depth.Events;
            if (d.DropState == DropIncoming)
            {
                if (w.Tick < d.DropTick) return;
                d.DropState = DropLanded;
                d.DropTick = w.Tick + (int)E.DropLifetimeTicks;
                World.PushEvent(w, SimEventType.SupplyDropLanded, -1, 0, d.DropX, d.DropZ, d.DropTick, null, DamageCause.None, E.DropClaimRange);
                return;
            }
            if (d.DropState != DropLanded) return;
            if (w.Tick >= d.DropTick)
            {
                d.DropState = DropNone;
                World.PushEvent(w, SimEventType.SupplyDropExpired, -1, 0, d.DropX, d.DropZ, 0);
                return;
            }
            int first0 = -1;
            int first1 = -1;
            double r2 = E.DropClaimRange * E.DropClaimRange;
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive || a.StandUp > 0) continue;
                double dx = w.X[i] - d.DropX;
                double dz = w.Z[i] - d.DropZ;
                if (dx * dx + dz * dz > r2) continue;
                if (a.Team == TeamId.Azure)
                {
                    if (first0 < 0) first0 = i;
                }
                else if (first1 < 0) first1 = i;
            }
            int contest = (first0 >= 0 ? ContestAzure : 0) | (first1 >= 0 ? ContestCrimson : 0);
            if (contest != d.DropContest)
            {
                d.DropContest = contest;
                World.PushEvent(w, SimEventType.SupplyDropContest, contest == ContestCrimson ? first1 : first0, 0, d.DropX, d.DropZ, contest);
            }
            if (contest != ContestAzure && contest != ContestCrimson) return;
            int team = contest == ContestAzure ? 0 : 1;
            if (++d.DropProgress[team] < (int)E.DropClaimTicks) return;
            int who = team == 0 ? first0 : first1;
            d.DropState = DropNone;
            d.DropsClaimed++;
            d.Stock[team * Sources.StockKinds + Sources.StockWood] += (int)E.DropWood;
            d.Stock[team * Sources.StockKinds + Sources.StockWater] += (int)E.DropWater;
            w.Actors[who].CoinsPicked += (int)E.DropCoins;
            d.CoinsTaken += (int)E.DropCoins;
            World.PushEvent(w, SimEventType.SupplyDropClaimed, who, team, d.DropX, d.DropZ, E.DropWood);
            if (Pickups.On(w))
            {
                PickupKind kind = (PickupKind)(1 + DepthRules.HashInt(w.Opts.Seed, d.EventIndex, 32, 3));
                Pickups.Spawn(w, kind, d.DropX, d.DropZ, 0, w.Tick + (int)E.DropPowerUpDelayTicks, w.Tick + (int)E.DropPowerUpDelayTicks + (int)w.Config.Depth.Pickups.PowerUpLifetimeTicks, -1, -1);
            }
        }

        /// <summary>Bots: the supply drop bot id should walk to, if any: announced or landed, within BotDropRange, a clear straight line to it.</summary>
        public static bool BotDropSpot(World w, int id, out double gx, out double gz)
        {
            DepthState d = w.Depth;
            gx = d.DropX;
            gz = d.DropZ;
            if (d.DropState == DropNone) return false;
            double r = w.Config.Depth.Events.BotDropRange;
            double dx = gx - w.X[id];
            double dz = gz - w.Z[id];
            if (dx * dx + dz * dz > r * r) return false;
            return Bots.BotSteer.FirstBlockedCell(w.Map, w.X[id], w.Z[id], gx, gz) < 0;
        }

        // ---------------------------------------------------------------- egg exposed

        private static bool StartEgg(World w, int k)
        {
            DepthState d = w.Depth;
            if (w.Kaiju.State != KaijuState.Egg || w.Match.Hatched || d.EggExposedUntil > w.Tick) return false;
            d.EggExposedUntil = w.Tick + (int)w.Config.Depth.Events.EggWindowTicks;
            d.EggWindows++;
            ShrineDef sh = w.Map.Shrine;
            World.PushEvent(w, SimEventType.EggExposed, -1, k, sh.X, sh.Z, d.EggExposedUntil);
            return true;
        }

        private static void EggStep(World w)
        {
            DepthState d = w.Depth;
            if (d.EggExposedUntil == 0) return;
            bool hatched = w.Kaiju.State != KaijuState.Egg || w.Match.Hatched;
            if (w.Tick < d.EggExposedUntil && !hatched) return;
            d.EggExposedUntil = 0;
            ShrineDef sh = w.Map.Shrine;
            World.PushEvent(w, SimEventType.EggExposedEnded, -1, hatched ? 1 : 0, sh.X, sh.Z, 0);
        }

        // ---------------------------------------------------------------- bridge collapse

        /// <summary>Collect the bridge cells of the middle third of span s into d.BrokenIdx; the count.</summary>
        private static int MiddleCells(World w, BridgeSpan s)
        {
            DepthState d = w.Depth;
            MapDef map = w.Map;
            double half = w.Config.Depth.Events.BridgeHalfWidth;
            double t0 = s.Len / 3;
            double t1 = s.Len * 2 / 3;
            double ax = s.X0 + s.Ux * t0, az = s.Z0 + s.Uz * t0, bx = s.X0 + s.Ux * t1, bz = s.Z0 + s.Uz * t1;
            int minX = JsMath.FloorToInt(Math.Min(ax, bx) - half), maxX = JsMath.FloorToInt(Math.Max(ax, bx) + half);
            int minZ = JsMath.FloorToInt(Math.Min(az, bz) - half), maxZ = JsMath.FloorToInt(Math.Max(az, bz) + half);
            int n = 0;
            for (int cz = minZ; cz <= maxZ; cz++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    if (!MapGrid.InBounds(map, cx, cz) || map.Cells[cz * map.Width + cx] != Cell.Bridge) continue;
                    double px = cx + 0.5 - s.X0;
                    double pz = cz + 0.5 - s.Z0;
                    double along = px * s.Ux + pz * s.Uz;
                    double across = Math.Abs(px * -s.Uz + pz * s.Ux);
                    if (along < t0 || along > t1 || across > half) continue;
                    if (n >= MaxBrokenCells) return n;
                    d.BrokenIdx[n++] = cz * map.Width + cx;
                }
            }
            d.BridgeAx = ax;
            d.BridgeAz = az;
            d.BridgeBx = bx;
            d.BridgeBz = bz;
            return n;
        }

        private static bool StartBridge(World w, int k)
        {
            DepthState d = w.Depth;
            if (d.BridgeState != BridgeNone) return false;
            int count = BridgeSpans.All.Count;
            int start = DepthRules.HashInt(w.Opts.Seed, k, 33, count);
            for (int c = 0; c < count; c++)
            {
                int si = (start + c) % count;
                BridgeSpan s = BridgeSpans.All[si];
                int n = MiddleCells(w, s);
                if (n < (int)w.Config.Depth.Events.BridgeMinCells) continue;
                if (!Terraform.CellsKeepRoutes(w, d.BrokenIdx, VoidCells, n)) continue;
                d.BrokenN = n;
                d.BrokenSpan = si;
                d.BridgeState = BridgeWarned;
                d.BridgeTick = w.Tick + (int)w.Config.Depth.Events.BridgeWarnTicks;
                d.RepairProgress = 0;
                World.PushEvent(w, SimEventType.BridgeWarning, -1, si, (d.BridgeAx + d.BridgeBx) / 2, (d.BridgeAz + d.BridgeBz) / 2, d.BridgeTick, null, DamageCause.None, s.Len / 6);
                return true;
            }
            return false;
        }

        private static void Rebuild(World w, int by, int wood)
        {
            DepthState d = w.Depth;
            MapDef map = w.Map;
            for (int i = 0; i < d.BrokenN; i++)
            {
                int idx = d.BrokenIdx[i];
                if (map.Cells[idx] == Cell.Void) MapGrid.SetCell(w, idx % map.Width, idx / map.Width, Cell.Bridge);
            }
            d.BridgeState = BridgeNone;
            d.BridgesRepaired++;
            World.PushEvent(w, SimEventType.BridgeRepaired, by, d.BrokenSpan, (d.BridgeAx + d.BridgeBx) / 2, (d.BridgeAz + d.BridgeBz) / 2, wood);
        }

        private static void BridgeStep(World w)
        {
            DepthState d = w.Depth;
            MapEventsConfig E = w.Config.Depth.Events;
            MapDef map = w.Map;
            if (d.BridgeState == BridgeWarned)
            {
                if (w.Tick < d.BridgeTick) return;
                // The map may have changed during the warning: re-check the route guard on what is still bridge.
                int n = 0;
                for (int i = 0; i < d.BrokenN; i++) if (map.Cells[d.BrokenIdx[i]] == Cell.Bridge) d.BrokenIdx[n++] = d.BrokenIdx[i];
                d.BrokenN = n;
                if (n == 0 || !Terraform.CellsKeepRoutes(w, d.BrokenIdx, VoidCells, n))
                {
                    d.BridgeState = BridgeNone;
                    World.PushEvent(w, SimEventType.BridgeRepaired, -1, d.BrokenSpan, (d.BridgeAx + d.BridgeBx) / 2, (d.BridgeAz + d.BridgeBz) / 2, -1);
                    return;
                }
                for (int i = 0; i < n; i++) MapGrid.SetCell(w, d.BrokenIdx[i] % map.Width, d.BrokenIdx[i] / map.Width, Cell.Void);
                d.BridgeState = BridgeBroken;
                d.BridgeTick = w.Tick + (int)E.BridgeAutoRepairTicks;
                d.BridgesCollapsed++;
                World.PushEvent(w, SimEventType.BridgeCollapsed, -1, d.BrokenSpan, (d.BridgeAx + d.BridgeBx) / 2, (d.BridgeAz + d.BridgeBz) / 2, d.BridgeTick, null, DamageCause.None, n);
                return;
            }
            if (d.BridgeState != BridgeBroken) return;
            if (w.Tick >= d.BridgeTick)
            {
                Rebuild(w, -1, 0);
                return;
            }
            double r2 = E.BridgeRepairRange * E.BridgeRepairRange;
            int worker = -1;
            for (int i = 0; i < w.ActorCount && worker < 0; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive || a.StandUp > 0 || a.Cls != ClassId.Builder || !w.Inputs[i].Interact) continue;
                double ax = w.X[i] - d.BridgeAx, az = w.Z[i] - d.BridgeAz, bx = w.X[i] - d.BridgeBx, bz = w.Z[i] - d.BridgeBz;
                if (ax * ax + az * az > r2 && bx * bx + bz * bz > r2) continue;
                if (Sources.Stock(w, a.Team, Sources.StockWood) < (int)E.BridgeRepairWood)
                {
                    if (w.Depth.PrevInteract[i] == 0) World.PushEvent(w, SimEventType.BridgeRepaired, i, d.BrokenSpan, w.X[i], w.Z[i], -2);
                    continue;
                }
                worker = i;
            }
            if (worker < 0)
            {
                d.RepairProgress = 0;
                return;
            }
            if (++d.RepairProgress < (int)E.BridgeRepairTicks) return;
            Sources.TrySpend(w, w.Actors[worker].Team, (int)E.BridgeRepairWood, 0);
            Rebuild(w, worker, (int)E.BridgeRepairWood);
        }

        // ---------------------------------------------------------------- the step

        /// <summary>Start event k of the given kind now (the schedule calls this; tests may too). A kind that cannot run falls back to a supply drop.</summary>
        public static void Start(World w, int kind, int k)
        {
            if (kind == KindEgg && StartEgg(w, k)) return;
            if (kind == KindBridge && StartBridge(w, k)) return;
            StartDrop(w, k);
        }

        /// <summary>One step of map events (SimStep, after the pickups).</summary>
        public static void EventsSystem(World w)
        {
            if (!On(w)) return;
            DepthState d = w.Depth;
            MapEventsConfig E = w.Config.Depth.Events;
            if (w.Tick >= d.NextEventTick && w.Match.Phase != MatchPhase.Ended)
            {
                int k = d.EventIndex++;
                d.NextEventTick += (int)E.PeriodTicks;
                Start(w, (DepthRules.HashInt(w.Opts.Seed, 30, 0, 3) + k) % 3, k);
            }
            DropStep(w);
            EggStep(w);
            BridgeStep(w);
        }
    }

    /// <summary>U-068 announcer lines, in ONE place for the owner to reword. Data only.</summary>
    public static class MapEventNames
    {
        public const string DropIncoming = "SUPPLY DROP INCOMING", DropLanded = "SUPPLY DROP LANDED", DropClaimed = "SUPPLY DROP CLAIMED", DropContested = "CONTESTED";
        public const string EggExposed = "THE EGG IS EXPOSED. EVERY DELIVERY COUNTS DOUBLE.", EggSealed = "THE EGG HAS SEALED";
        public const string BridgeWarning = "A BRIDGE IS GIVING WAY", BridgeCollapsed = "BRIDGE DOWN", BridgeRepaired = "BRIDGE REBUILT";
        public const string RepairPrompt = "Builder: hold interact to rebuild", RepairNeedWood = "Not enough wood in the team stock.";
    }
}
