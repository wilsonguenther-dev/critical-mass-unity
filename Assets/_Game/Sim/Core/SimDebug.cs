using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>Debug-overlay hooks into the sim (CM-10, browser src/sim/debug.ts, 09e0904); reuses the real delivery math.</summary>
    public static class SimDebug
    {
        private static bool ValidUnits(double n)
        {
            return n > 0 && double.IsFinite(n);
        }

        /// <summary>Feed the egg as if team delivered biomass then corruption (actor id -1); null team = instability only, no delivery event. True when anything was applied.</summary>
        public static bool DebugAddFeed(World w, TeamId? team, double biomassUnits, double corruptionUnits)
        {
            MatchState m = w.Match;
            if (m.Hatched || w.Kaiju.State != KaijuState.Egg) return false;
            bool applied = false;
            if (team != null)
            {
                if (ValidUnits(biomassUnits))
                {
                    SimResources.ApplyDelivery(w, team.Value, CarryKind.Biomass, biomassUnits, -1);
                    applied = true;
                }
                if (ValidUnits(corruptionUnits))
                {
                    SimResources.ApplyDelivery(w, team.Value, CarryKind.Corruption, corruptionUnits, -1);
                    applied = true;
                }
                return applied;
            }
            if (!ValidUnits(corruptionUnits)) return false;
            KaijuFeedConfig feed = w.Config.KaijuFeed;
            m.Instability += corruptionUnits * feed.CorruptionInstabilityPerUnit;
            // CM-28: the same lock path as a real delivery, so the one BerserkLocked event fires here too.
            if (m.Instability >= SimResources.BerserkLine(w)) SimResources.LockBerserk(w); // DG-41
            return true;
        }
    }
}
