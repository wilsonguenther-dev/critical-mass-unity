using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>Hill control (browser hill.ts).</summary>
    public static class Hill
    {
        /// <summary>HillOwnerChange event code: 0 Neutral, 1 Azure, 2 Crimson, 3 Contested.</summary>
        private static int StateCode(HillState s)
        {
            return s == HillState.Neutral ? 0 : s == HillState.Azure ? 1 : s == HillState.Crimson ? 2 : 3;
        }

        /// <summary>Counts living actors inside the hill (strictly less than r), sets control, accrues team and personal hill time.</summary>
        public static void HillSystem(World w, double dt)
        {
            MatchState m = w.Match;
            HillDef hill = w.Map.Hill;
            double r = w.Config.Hill.Radius;
            double r2 = r * r;
            int n = w.ActorCount;
            int azure = 0;
            int crimson = 0;
            for (int i = 0; i < n; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive) continue;
                double dx = w.X[i] - hill.X;
                double dz = w.Z[i] - hill.Z;
                if (dx * dx + dz * dz < r2)
                {
                    if (a.Team == TeamId.Azure) azure++;
                    else crimson++;
                }
            }
            m.HillCount.Azure = azure;
            m.HillCount.Crimson = crimson;
            HillState next;
            if (azure == 0 && crimson == 0) next = HillState.Neutral;
            else if (azure > crimson) next = HillState.Azure;
            else if (crimson > azure) next = HillState.Crimson;
            else
            {
                // b15 rule B HILL ANCHOR: with equal bodies on the hill, the one team holding it owns the hill (0 = contested)
                int anchor = HutChoices.AnchorTie(w);   // b15-hook
                next = anchor == 1 ? HillState.Azure : anchor == 2 ? HillState.Crimson : HillState.Contested;
            }

            // b15 rule A RALLY: the trailing owner's extra credit, read before this tick's seconds are added (0 with the rule off)
            double rally = Comeback.RallyCredit(w, next, dt);   // b15-hook
            if (next == HillState.Azure) m.HillSeconds.Azure += dt;
            else if (next == HillState.Crimson) m.HillSeconds.Crimson += dt;
            if (rally > 0)
            {
                if (next == HillState.Azure) m.HillSeconds.Azure += rally;
                else m.HillSeconds.Crimson += rally;
            }

            TeamId? owner = SimEnumMap.TeamOf(next);
            if (owner.HasValue)
            {
                for (int i = 0; i < n; i++)
                {
                    Actor a = w.Actors[i];
                    if (!a.Alive || a.Team != owner.Value) continue;
                    double dx = w.X[i] - hill.X;
                    double dz = w.Z[i] - hill.Z;
                    if (dx * dx + dz * dz < r2) a.HillSeconds += dt;
                }
            }

            if (next != m.HillState)
            {
                m.HillState = next;
                World.PushEvent(w, SimEventType.HillOwnerChange, StateCode(next), 0, hill.X, hill.Z, 0);
            }
        }
    }
}
