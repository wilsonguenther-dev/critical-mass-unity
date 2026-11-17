// CM b13 item K (five Kaiju types). S0 stub: identity until item K fills it.
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>CM b13 item K: the Kaiju type state on the World (World.KaijuKind). S0 stub: identity until item K fills it.</summary>
    public sealed class KaijuTypeState
    {
        /// <summary>Called once in the World constructor after the actor arrays exist.</summary>
        public void Init(World w) { }

        /// <summary>Called by the world reset path (World.ResetWorld).</summary>
        public void Reset(World w) { }
    }

    /// <summary>CM b13 item K: the Kaiju type from the biomass mix, its inherent trait and fed power. S0 stub: identity until item K fills it.</summary>
    public static class KaijuTypes
    {
        /// <summary>The rule is on (item K: DepthRules.On(w, w.Config.Depth.KaijuTypes.On)).</summary>
        public static bool On(World w) => false;
        /// <summary>This match's Kaiju type (Elder = today's Kaiju).</summary>
        public static KaijuType Type(World w) => KaijuType.Elder;
        /// <summary>The fed power level, 0..3.</summary>
        public static int PowerLevel(World w) => 0;
        /// <summary>Called on the hatch tick right before the Hatch event.</summary>
        public static void OnHatch(World w) { }
        /// <summary>True when viewer viewerId cannot see (or target) targetId through the SHROUD.</summary>
        public static bool BlindBlocks(World w, int viewerId, int targetId) => false;
        /// <summary>The SHROUD cloud, when one is up.</summary>
        public static bool CloudActive(World w, out double x, out double z, out double r)
        {
            x = 0; z = 0; r = 0;
            return false;
        }
        /// <summary>Viewer viewerId's sight radius given its normal one.</summary>
        public static double SightRadiusFor(World w, int viewerId, double normal) => normal;
    }
}
