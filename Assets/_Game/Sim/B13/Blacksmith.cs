// CM b13 item H (Miner and the Blacksmith hut). S0 stub: identity until item H fills it.
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>CM b13 item H: the Blacksmith hut state on the World (World.Hut). S0 stub: identity until item H fills it.</summary>
    public sealed class HutState
    {
        /// <summary>Called once in the World constructor after the actor arrays exist.</summary>
        public void Init(World w) { }

        /// <summary>Called by the world reset path (World.ResetWorld).</summary>
        public void Reset(World w) { }
    }

    /// <summary>CM b13 item H: the Miner mines ore; the team's Blacksmith hut levels 0 to 5 (+5% per level). S0 stub: identity until item H fills it.</summary>
    public static class Blacksmith
    {
        /// <summary>The rule is on (item H: DepthRules.On(w, w.Config.Depth.Hut.On)).</summary>
        public static bool On(World w) => false;
        /// <summary>The team's hut level, 0..5.</summary>
        public static int Level(World w, TeamId team) => 0;
        /// <summary>Ore in the team's hut stock toward the next level.</summary>
        public static int OreBanked(World w, TeamId team) => 0;
        /// <summary>Ore the next level costs (0 at max).</summary>
        public static int NextCost(World w, TeamId team) => 0;
        /// <summary>1 + 0.05 x Level: max HP, damage dealt and healing of every class of the team.</summary>
        public static double StatScale(World w, TeamId team) => 1.0;
        /// <summary>The Miner's HAUL speed multiplier for actor id (1 = none).</summary>
        public static double SpeedScale(World w, int id) => 1.0;
        /// <summary>A Miner's F press: true = taken by the hut rule (mine), so Terraform skips it.</summary>
        public static bool TakeMinerPress(World w, int id) => false;
        /// <summary>A Miner's ability press: true = taken by the hut rule (HAUL), so Abilities skips it.</summary>
        public static bool TakeMinerAbility(World w, int id) => false;
        /// <summary>Per tick, after Sources.SourcesSystem.</summary>
        public static void HutSystem(World w) { }
    }
}
