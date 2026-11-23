using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.Hud.Player
{
    /// <summary>
    /// mv-6 readability-4 item 2 (pass 4: ORRIN / QUILL / UMBER / SABLE / RUNE / NETTLE in one clump): in third person under
    /// the class moves a fighter's NAME shows only for my current opponent (WorldWords.Focus), the enemy under my aim
    /// (AimAssist.LockTarget) and allies within AllyNameRange; everyone else keeps the bar only, and the bars de-overlap
    /// (HealthBars). Off (switches off or the top-down view): every name shows as before.
    /// </summary>
    public static class CrowdTags
    {
        public const double AllyNameRange = 6.0;

        public static bool On => WorldWords.MovesCap && PxProject.ThirdPerson;

        /// <summary>Does fighter id show his name for me?</summary>
        public static bool NameShown(World w, int me, int id)
        {
            if (!On) return true;
            if (me < 0 || me >= w.ActorCount || id < 0 || id >= w.ActorCount) return true;
            return Pick(id, me, WorldWords.Focus, CriticalMass.Runtime.Input.AimAssist.LockTarget,
                w.Actors[id].Team == w.Actors[me].Team, Dist2(w, me, id));
        }

        /// <summary>Pure (tests): the rule itself.</summary>
        public static bool Pick(int id, int me, int focus, int aim, bool ally, double dist2)
        {
            if (id == me) return true;
            if (ally) return dist2 <= AllyNameRange * AllyNameRange;
            return id == focus || id == aim;
        }

        static double Dist2(World w, int a, int b)
        {
            double dx = w.X[a] - w.X[b], dz = w.Z[a] - w.Z[b];
            return dx * dx + dz * dz;
        }
    }
}
