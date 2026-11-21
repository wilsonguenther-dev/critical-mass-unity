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
            int wa = 0, wc = 0;   // b16-hook: weighted bodies (a Warrior counts WarriorHoldWeight; 1 each with the rule off)
            for (int i = 0; i < n; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive) continue;
                double dx = w.X[i] - hill.X;
                double dz = w.Z[i] - hill.Z;
                if (dx * dx + dz * dz < r2)
                {
                    if (a.Team == TeamId.Azure) { azure++; wa += MechRules.HillWeight(w, i); }
                    else { crimson++; wc += MechRules.HillWeight(w, i); }
                }
            }
            m.HillCount.Azure = azure;
            m.HillCount.Crimson = crimson;
            // b16 the hill count: Warrior weight first, then at most HillCap per team counts (raw bodies stay in HillCount).
            // With both rules off (Classic, PhysicsOnly) wa = azure and wc = crimson, so ownership is exactly as before.
            azure = MechRules.Counted(w, 0, wa);   // b16-hook
            crimson = MechRules.Counted(w, 1, wc);   // b16-hook
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

            next = Comeback.Majority(w, next, azure, crimson);   // b15-hook: MAJORITY (the same state with the lever off)
            double held = dt * Comeback.HoldRate(w, next) * Comeback.KaijuHillRate(w, next);   // b15-hook: HOLD FATIGUE, KAIJU HOLDS NOTHING (dt with both off)
            // b15 rule A RALLY: the trailing owner's extra credit, read before this tick's seconds are added (0 with the rule off)
            double rally = Comeback.RallyCredit(w, next, dt);   // b15-hook
            if (next == HillState.Azure) m.HillSeconds.Azure += held;
            else if (next == HillState.Crimson) m.HillSeconds.Crimson += held;
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
