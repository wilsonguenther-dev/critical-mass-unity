// BOOTSTRAP: replaced by the output of StateHashCoverageTests (Temp/ci/StateHash.Generated.cs).
namespace CriticalMass.Net
{
    public static partial class StateHash
    {
        private static void H_World(ref ulong h, global::CriticalMass.Sim.Core.World o) { M(ref h, o.Tick); }
        private static void H_BotsState(ref ulong h, global::CriticalMass.Sim.Bots.BotsState o) { M(ref h, o.Epoch); }
    }
}
