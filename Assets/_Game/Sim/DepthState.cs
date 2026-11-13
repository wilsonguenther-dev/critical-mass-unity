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

        public static DepthState Create(GameConfig cfg, Grid.MapDef map)
        {
            int cap = World.ActorCapacity(cfg);
            var s = new DepthState
            {
                RefuseReadyTick = new int[cap],
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
        }
    }
}
