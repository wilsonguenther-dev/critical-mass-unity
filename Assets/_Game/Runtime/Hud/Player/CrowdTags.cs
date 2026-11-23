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
            int focus = WorldWords.Focus, aim = CriticalMass.Runtime.Input.AimAssist.LockTarget;
            bool ally = w.Actors[id].Team == w.Actors[me].Team;
            Ranks(w, me, focus, aim, out int n0, out int n1, out int crowd);
            int rank = id == n0 ? 0 : id == n1 ? 1 : 99;
            return Pick(id, me, focus, aim, ally, Dist2(w, me, id), rank, crowd >= CrowdEnemies);
        }

        /// <summary>fix/px-rough-spots item 9: my target, the enemy under my aim or one of the two nearest enemies (their names
        /// win a clash with any other tag).</summary>
        public static bool Priority(World w, int me, int id)
        {
            if (me < 0 || me >= w.ActorCount || id < 0 || id >= w.ActorCount || w.Actors[id].Team == w.Actors[me].Team) return false;
            int focus = WorldWords.Focus, aim = CriticalMass.Runtime.Input.AimAssist.LockTarget;
            if (id == focus || id == aim) return true;
            Ranks(w, me, focus, aim, out int n0, out int n1, out _);
            return id == n0 || id == n1;
        }

        /// <summary>fix/px-rough-spots item 9: besides my target, the two nearest standing enemies within NearRange keep their
        /// names (a crowd's closest threats stay readable); in a crowd (CrowdEnemies or more enemies within NearRange) ally
        /// names shrink to AllyCrowdRange so they do not pile onto the enemy names.</summary>
        public const double NearRange = 8.0, AllyCrowdRange = 2.5;
        public const int CrowdEnemies = 3;
        static int _rankFrame = -1, _r0 = -1, _r1 = -1, _crowd;

        static void Ranks(World w, int me, int focus, int aim, out int n0, out int n1, out int crowd)
        {
            int frame = UnityEngine.Time.frameCount;
            if (frame != _rankFrame)
            {
                _rankFrame = frame; _r0 = _r1 = -1; _crowd = 0;
                double d0 = double.MaxValue, d1 = double.MaxValue;
                CriticalMass.Sim.Config.TeamId team = w.Actors[me].Team;
                for (int i = 0; i < w.ActorCount; i++)
                {
                    if (i == me || !w.Actors[i].Alive || w.Actors[i].Team == team) continue;
                    double d2 = Dist2(w, me, i);
                    if (d2 > NearRange * NearRange) continue;
                    _crowd++;
                    if (i == focus || i == aim) continue;
                    if (d2 < d0) { d1 = d0; _r1 = _r0; d0 = d2; _r0 = i; }
                    else if (d2 < d1) { d1 = d2; _r1 = i; }
                }
            }
            n0 = _r0; n1 = _r1; crowd = _crowd;
        }

        /// <summary>Pure (tests): the rule with the near-enemy rank (0, 1 = the two nearest after my target; else 99) and the
        /// crowd flag.</summary>
        public static bool Pick(int id, int me, int focus, int aim, bool ally, double dist2, int nearRank, bool crowd)
        {
            if (id == me) return true;
            if (ally) return dist2 <= (crowd ? AllyCrowdRange * AllyCrowdRange : AllyNameRange * AllyNameRange);
            return id == focus || id == aim || nearRank < 2;
        }

        /// <summary>Pure (tests): the rule itself.</summary>
        public static bool Pick(int id, int me, int focus, int aim, bool ally, double dist2) => Pick(id, me, focus, aim, ally, dist2, 99, false);

        static double Dist2(World w, int a, int b)
        {
            double dx = w.X[a] - w.X[b], dz = w.Z[a] - w.Z[b];
            return dx * dx + dz * dz;
        }
    }
}
