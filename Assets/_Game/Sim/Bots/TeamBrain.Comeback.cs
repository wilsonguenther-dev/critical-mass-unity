using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Bots
{
    /// <summary>
    /// b15 wave-two combine: the bot brain's KAIJU HOLDS NOTHING hook (PR #227) reads PR #219's rule. While the comeback rule
    /// is on and ComebackConfig.KaijuHillPerMille is under 1000 (the default 0: a team scores nothing on the hill while its own
    /// sided Kaiju stands in the ring), TeamBrain.OwnKaijuBlocksScore can be true, so the guard + escort job switches on.
    /// </summary>
    public static partial class TeamBrain
    {
        static partial void KaijuHoldsNothingRule(World w, ref bool cuts)
        {
            cuts = Comeback.On(w) && w.Config.Depth.Comeback.KaijuHillPerMille < 1000;
        }
    }
}
