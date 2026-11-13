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
            };
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
