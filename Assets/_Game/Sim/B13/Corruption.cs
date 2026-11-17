// CM b13 item C (corruption crystal and the wild Kaiju). S0 stub: identity until item C fills it.
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>CM b13 item C: the corruption pool and rolls state on the World (World.Corrupt). S0 stub: identity until item C fills it.</summary>
    public sealed class CorruptionState
    {
        /// <summary>Called once in the World constructor after the actor arrays exist.</summary>
        public void Init(World w) { }

        /// <summary>Called by the world reset path (World.ResetWorld).</summary>
        public void Reset(World w) { }
    }

    /// <summary>CM b13 item C: crystals, the pool, the sqrt chance, the roll and the wild Kaiju. S0 stub: identity until item C fills it.</summary>
    public static class Corruption
    {
        /// <summary>The rule is on (item C: DepthRules.On(w, w.Config.Depth.Corruption.On)).</summary>
        public static bool On(World w) => false;
        /// <summary>The Kaiju state at the hatch given the resolved one (the hatch roll).</summary>
        public static KaijuState AtHatch(World w, KaijuState resolved) => resolved;
        /// <summary>Called on the tick a knocked-out Kaiju gets up (the get-up roll).</summary>
        public static void AtGetUp(World w) { }
        /// <summary>The team index a wild Kaiju is biased toward, given the bigger feeder's.</summary>
        public static int WildTargetTeam(World w, int bigFeeder) => bigFeeder;
        /// <summary>Crystals in the pool since the last roll.</summary>
        public static double Pool(World w) => 0;
        /// <summary>The chance the next roll succeeds, 0..0.70.</summary>
        public static double Chance(World w) => 0;
        /// <summary>The tick of the next roll (-1 none).</summary>
        public static int NextRollTick(World w) => -1;
        /// <summary>The carrier's speed multiplier for actor id (1 = none).</summary>
        public static double SpeedScale(World w, int id) => 1.0;
        /// <summary>Per tick, after SimResources.ResourcesSystem.</summary>
        public static void CorruptionSystem(World w, double dt) { }
    }
}
