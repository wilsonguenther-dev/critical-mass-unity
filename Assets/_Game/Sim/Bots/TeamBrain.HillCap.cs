using System;
using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Bots
{
    /// <summary>
    /// b16 THE HILL CAP, bot side (design advisor 2026-11-21: "capping the number of units that contribute to the hill at 10 ...
    /// see if that will create more off the hill fighting"). At most MechRules.HillCap weighted bodies of a team count on the
    /// hill (a Warrior fills WarriorHoldWeight of them), so bodies past it add nothing. Every step, after the escort jobs: the
    /// team's bodies on the disc that are not hill-job bots are counted first (people, guards, feeders crossing), then the
    /// hill-job bots on the disc in id order keep their place while it still fits, then the hill-job bots walking in keep
    /// coming while the count plus HillCapWalkInSlack is under the cap. Every other hill-job bot takes JobIntercept: it stands
    /// on the enemy's approach (InterceptAlong u of path from the hill toward the enemy pad) and fights whoever comes, and
    /// it goes back the moment the team has room again. A switch holds at least HillCapLockSeconds. Deterministic (ids break
    /// every tie), allocation-free; nothing runs without the cap (Classic, PhysicsOnly, HillContributorCap 0).
    /// </summary>
    public static partial class TeamBrain
    {
        public const byte JobIntercept = 9;

        /// <summary>A job that walks a hill bot off the disc on purpose (KAIJU HOLDS NOTHING escort, b16 intercept).</summary>
        public static bool OffHillJob(byte job) => job == JobEscort || job == JobIntercept;

        static bool CapJob(byte job) => job == JobHill || job == JobIntercept;

        private static void HillCapJobs(World w, BotsState st, TeamId team, int t)
        {
            MechState ms = w.Mech;
            if (ms == null || ms.Surplus == null) return;
            int n = w.ActorCount;
            int cap = MechRules.HillCap(w);
            bool on = cap != int.MaxValue && w.Config.Depth.Mech.HillCapBotsOffHill;
            double hx = w.Map.Hill.X, hz = w.Map.Hill.Z, r = w.Config.Hill.Radius, r2 = r * r;
            int left = cap;
            if (on)
                for (int id = 0; id < n; id++)
                {
                    Actor a = w.Actors[id];
                    if (a.Team != team || !a.Alive) continue;
                    if (BotsState.IsBot(st, w, id) && CapJob(st.Job[id])) continue;
                    double dx = w.X[id] - hx, dz = w.Z[id] - hz;
                    if (dx * dx + dz * dz < r2) left -= MechRules.HillWeight(w, id);
                }
            int lockTicks = Math.Max(0, (int)Math.Floor(w.Config.Depth.Mech.HillCapLockSeconds * w.Config.SimHz + 0.5));
            // pass 0: the hill-job bots on the disc; pass 1: the ones walking in
            for (int pass = 0; pass < 2; pass++)
                for (int id = 0; id < n; id++)
                {
                    Actor a = w.Actors[id];
                    if (a.Team != team) continue;
                    bool bot = BotsState.IsBot(st, w, id);
                    if (!a.Alive || !bot || !CapJob(st.Job[id]) || !on)
                    {
                        if (pass == 1 && ms.Surplus[id] != 0)
                        {
                            ms.Surplus[id] = 0;
                            if (bot && st.Job[id] == JobIntercept) st.Job[id] = JobHill;
                        }
                        continue;
                    }
                    double dx = w.X[id] - hx, dz = w.Z[id] - hz;
                    bool onDisc = dx * dx + dz * dz < r2;
                    if (onDisc != (pass == 0)) continue;
                    int wgt = MechRules.HillWeight(w, id);
                    bool keep = onDisc ? left >= wgt : left + w.Config.Depth.Mech.HillCapWalkInSlack > 0;
                    if (keep) left -= wgt;
                    byte want = (byte)(keep ? 0 : 1);
                    if (want != ms.Surplus[id] && w.Tick >= ms.SurplusLock[id])
                    {
                        ms.Surplus[id] = want;
                        ms.SurplusLock[id] = w.Tick + lockTicks;
                        if (want != 0) ms.Intercepts++;
                    }
                    if (ms.Surplus[id] != 0)
                    {
                        st.Job[id] = JobIntercept;
                        st.Rally[id] = RallyNone;
                    }
                    else st.Job[id] = JobHill;
                }
        }
    }
}
