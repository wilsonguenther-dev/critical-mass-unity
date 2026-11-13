using System;
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>Mechanics-depth switches: a rule is on when DepthConfig.Classic is false and its own Enabled is true.</summary>
    public static class DepthRules
    {
        public static bool On(GameConfig c, bool enabled) => enabled && !c.Depth.Classic;
        public static bool On(World w, bool enabled) => enabled && !w.Config.Depth.Classic;

        /// <summary>
        /// A uniform value in [0, 1) from the match seed and two salts: a stateless integer hash, never the shared RNG, so a
        /// depth rule's roll never moves another system's draws (same idea as BotSkill.Roll and SimResources.SeedUniform).
        /// </summary>
        public static double Hash01(int seed, int a, int b)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u;
                h ^= (uint)a * 0x85EBCA6Bu + 0x165667B1u;
                h = (h << 13) | (h >> 19);
                h ^= (uint)b * 0xC2B2AE35u + 0x27D4EB2Fu;
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return (h >> 8) / 16777216.0;
            }
        }

        /// <summary>An integer in [0, n) from Hash01 (0 when n is not positive).</summary>
        public static int HashInt(int seed, int a, int b, int n)
        {
            if (n <= 0) return 0;
            int v = (int)(Hash01(seed, a, b) * n);
            return v >= n ? n - 1 : v;
        }
    }

    /// <summary>Mechanics-depth match state (U-063 onward), allocated once per World at capacity and reset in place.</summary>
    public sealed class DepthState
    {
        /// <summary>U-063: per actor, the first World.Tick its next DeliverRefused event may be pushed.</summary>
        public int[] RefuseReadyTick;
        /// <summary>U-063 counters (soak, tests): deliveries refused, hand-offs made, units handed off.</summary>
        public int Refusals;
        public int Handoffs;
        public int HandoffUnits;
        /// <summary>Per actor: was interact held (alive, standing) at the end of last step? Depth rules act on the rising edge.</summary>
        public byte[] PrevInteract;

        /// <summary>U-064: the wood groves and water springs of this map (Sources.Place), fixed for the world.</summary>
        public SourceNode[] Sources;
        /// <summary>U-064: per actor, seconds gathered at its current source and that source (-1 none).</summary>
        public double[] SourceProgress;
        public int[] SourceOf;
        /// <summary>U-064: per actor, HP at the end of its last sources pass (a drop = it was hit: gathering restarts).</summary>
        public double[] PrevHp;
        /// <summary>U-064: the team stocks, [team * Sources.StockKinds + slot] (wood, water, food).</summary>
        public int[] Stock;
        /// <summary>U-064 counters: units gathered (wood, water), banked, taken back out, spent from the stock.</summary>
        public int[] Gathered;
        public int Banked;
        public int Withdrawn;
        public int StockSpent;
        /// <summary>U-064: the World.Tick of the last source route rebuild (one per tick).</summary>
        public int FieldBuildTick;

        /// <summary>U-065: each team's level of each class, [team * 6 + class] (0 or 1 = the spawn level).</summary>
        public byte[] ClassLevel;
        public int UpgradesBought;

        /// <summary>U-066: the pickup pool (fixed capacity; inactive slots have Active = false).</summary>
        public Pickup[] Pickups;
        /// <summary>U-066: coin spots and centre-line power-up spots of this map, and the tick each coin spot refills (int.MaxValue while a pile waits).</summary>
        public Grid.PointXZ[] CoinSpots;
        public Grid.PointXZ[] PowerSpots;
        public int[] CoinSpotNextTick;
        /// <summary>U-066: power-up spawns so far, and the tick of the next one.</summary>
        public int PowerIndex;
        public int NextPowerTick;
        /// <summary>U-066: per actor, the power-up it holds ((byte)PickupKind, 0 = none), the tick it ends and the ward HP left.</summary>
        public byte[] PowerKind;
        public int[] PowerEndTick;
        public double[] WardLeft;
        /// <summary>U-066: per actor, was it alive at the end of the last pickups pass (a knock-out is the 1 to 0 edge).</summary>
        public byte[] WasAlive;
        /// <summary>U-066 counters: coins picked up, power-ups taken, power-ups dropped by the knocked out, pool recycles.</summary>
        public int CoinsTaken;
        public int PowerUpsTaken;
        public int PowerUpsDropped;
        public int PickupOverflows;

        /// <summary>U-067: per actor, the ticks of its last PingRing accepted pings (a ring) and the ring's next slot.</summary>
        public int PingRing;
        public int[] PingTicks;
        public byte[] PingNext;
        /// <summary>U-067: per bot, the order it follows ((int)PingCall + 1, 0 = none), its spot, who called, when it ends, and its route slot and serial.</summary>
        public byte[] OrderKind;
        public double[] OrderX;
        public double[] OrderZ;
        public int[] OrderBy;
        public int[] OrderEnd;
        public int[] OrderField;
        public int[] OrderSerial;
        /// <summary>U-067: the order routes ([team * slots + k], built on use), their serials and each team's next slot.</summary>
        public Grid.FlowField[] PingFields;
        public int[] PingFieldSerial;
        public int[] PingFieldNext;
        /// <summary>U-067: per bot and per team, the first tick it may call again.</summary>
        public int[] BotPingReady;
        public int[] TeamBotPingReady;
        /// <summary>U-067 counters: pings accepted, refused, answers given, calls made by bots.</summary>
        public int PingsAccepted;
        public int PingsRefused;
        public int PingAnswers;
        public int BotPings;

        /// <summary>U-068: events started so far and the tick of the next.</summary>
        public int EventIndex;
        public int NextEventTick;
        /// <summary>U-068 supply drop: spots, state (MapEvents.Drop*), where, the tick it lands (incoming) or is gone (landed), each team's claim ticks, who stands on it.</summary>
        public Grid.PointXZ[] DropSpots;
        public int DropState;
        public double DropX;
        public double DropZ;
        public int DropTick;
        public int[] DropProgress;
        public int DropContest;
        /// <summary>U-068 egg exposed: the tick the window closes (0 = closed).</summary>
        public int EggExposedUntil;
        /// <summary>U-068 bridge: state (MapEvents.Bridge*), the span, the tick it falls (warned) or rebuilds itself (broken), its cells, the two broken ends, repair ticks.</summary>
        public int BridgeState;
        public int BrokenSpan;
        public int BridgeTick;
        public int[] BrokenIdx;
        public int BrokenN;
        public double BridgeAx;
        public double BridgeAz;
        public double BridgeBx;
        public double BridgeBz;
        public int RepairProgress;
        /// <summary>U-068 counters.</summary>
        public int DropsClaimed;
        public int EggWindows;
        public int BridgesCollapsed;
        public int BridgesRepaired;

        public static DepthState Create(GameConfig cfg, Grid.MapDef map)
        {
            int cap = World.ActorCapacity(cfg);
            var s = new DepthState
            {
                RefuseReadyTick = new int[cap],
                PrevInteract = new byte[cap],
                Sources = Core.Sources.Place(cfg, map),
                SourceProgress = new double[cap],
                SourceOf = new int[cap],
                PrevHp = new double[cap],
                Stock = new int[2 * Core.Sources.StockKinds],
                Gathered = new int[2],
                ClassLevel = new byte[2 * ClassIds.All.Count],
                Pickups = new Pickup[(int)cfg.Depth.Pickups.Capacity],
                CoinSpots = Core.Pickups.PlaceCoinSpots(cfg, map),
                PowerSpots = Core.Pickups.PlaceCentreSpots(map, cfg.Depth.Pickups.PowerSpotsZ),
                PowerKind = new byte[cap],
                PowerEndTick = new int[cap],
                WardLeft = new double[cap],
                WasAlive = new byte[cap],
                PingRing = Math.Max(1, (int)cfg.Depth.Pings.RateCount),
                PingNext = new byte[cap],
                OrderKind = new byte[cap],
                OrderX = new double[cap],
                OrderZ = new double[cap],
                OrderBy = new int[cap],
                OrderEnd = new int[cap],
                OrderField = new int[cap],
                OrderSerial = new int[cap],
                PingFields = new Grid.FlowField[2 * Math.Max(1, (int)cfg.Depth.Pings.RouteSlotsPerTeam)],
                PingFieldNext = new int[2],
                BotPingReady = new int[cap],
                TeamBotPingReady = new int[2],
                DropSpots = Core.Pickups.PlaceCentreSpots(map, cfg.Depth.Events.DropSpotsZ),
                DropProgress = new int[2],
                BrokenIdx = new int[MapEvents.MaxBrokenCells],
            };
            for (int i = 0; i < s.Pickups.Length; i++) s.Pickups[i] = new Pickup { Source = -1, Spot = -1 };
            s.CoinSpotNextTick = new int[s.CoinSpots.Length];
            s.PingTicks = new int[cap * s.PingRing];
            s.PingFieldSerial = new int[s.PingFields.Length];
            return s;
        }

        /// <summary>Back to the start of a match, in place (World.InitWorld, after the map repaint and the actor spawn).</summary>
        public void Reset(World w)
        {
            Array.Clear(RefuseReadyTick, 0, RefuseReadyTick.Length);
            Refusals = 0;
            Handoffs = 0;
            HandoffUnits = 0;
            Array.Clear(PrevInteract, 0, PrevInteract.Length);
            foreach (SourceNode s in Sources)
            {
                s.Charges = s.MaxCharges;
                s.RegrowIn = 0;
                s.FieldWallVersion = -1;
            }
            Array.Clear(SourceProgress, 0, SourceProgress.Length);
            Array.Fill(SourceOf, -1);
            Array.Clear(PrevHp, 0, PrevHp.Length);
            Array.Clear(Stock, 0, Stock.Length);
            Array.Clear(Gathered, 0, Gathered.Length);
            Banked = 0;
            Withdrawn = 0;
            StockSpent = 0;
            FieldBuildTick = -1;
            Array.Clear(ClassLevel, 0, ClassLevel.Length);
            UpgradesBought = 0;
            foreach (Pickup p in Pickups)
            {
                p.Active = false;
                p.Source = -1;
                p.Spot = -1;
            }
            Array.Fill(CoinSpotNextTick, (int)w.Config.Depth.Pickups.CoinFirstTicks);
            PowerIndex = 0;
            NextPowerTick = (int)w.Config.Depth.Pickups.FirstPowerUpTicks;
            Array.Clear(PowerKind, 0, PowerKind.Length);
            Array.Clear(PowerEndTick, 0, PowerEndTick.Length);
            Array.Clear(WardLeft, 0, WardLeft.Length);
            for (int id = 0; id < WasAlive.Length; id++) WasAlive[id] = (byte)(id < w.ActorCount && w.Actors[id].Alive ? 1 : 0);
            CoinsTaken = 0;
            PowerUpsTaken = 0;
            PowerUpsDropped = 0;
            PickupOverflows = 0;
            Array.Fill(PingTicks, -1000000);
            Array.Clear(PingNext, 0, PingNext.Length);
            Array.Clear(OrderKind, 0, OrderKind.Length);
            Array.Fill(OrderBy, -1);
            Array.Clear(OrderEnd, 0, OrderEnd.Length);
            Array.Fill(OrderField, -1);
            Array.Clear(PingFieldNext, 0, 2);
            Array.Clear(BotPingReady, 0, BotPingReady.Length);
            Array.Clear(TeamBotPingReady, 0, 2);
            PingsAccepted = 0;
            PingsRefused = 0;
            PingAnswers = 0;
            BotPings = 0;
            EventIndex = 0;
            NextEventTick = (int)w.Config.Depth.Events.FirstEventTicks;
            DropState = MapEvents.DropNone;
            DropProgress[0] = 0;
            DropProgress[1] = 0;
            DropContest = 0;
            EggExposedUntil = 0;
            BridgeState = MapEvents.BridgeNone;
            BrokenSpan = -1;
            BrokenN = 0;
            RepairProgress = 0;
            DropsClaimed = 0;
            EggWindows = 0;
            BridgesCollapsed = 0;
            BridgesRepaired = 0;
            for (int t = 0; t < 2; t++)
            {
                w.Match.Delivered[t].Wood = 0;
                w.Match.Delivered[t].Water = 0;
            }
        }

        /// <summary>The last thing a sim step does when the lane is on: remember each actor's interact for next step's rising edge.</summary>
        public static void EndStep(World w)
        {
            if (w.Config.Depth.Classic) return;
            byte[] prev = w.Depth.PrevInteract;
            for (int id = 0; id < w.ActorCount; id++)
            {
                Actor a = w.Actors[id];
                prev[id] = (byte)(w.Inputs[id].Interact && a.Alive && a.StandUp <= 0 ? 1 : 0);
            }
        }
    }
}
