using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Bots
{
    /// <summary>Bot-side helpers of the mechanics-depth lane (U-064 onward). Deterministic, allocation-free.</summary>
    public static class BotDepth
    {
        private const byte Picked = 2;

        private static int SupplyRank(World w, ClassId cls)
        {
            double[] r = w.Config.Depth.Sources.SupplyClassRank;
            int k = (int)cls;
            return k >= 0 && k < r.Length ? (int)r[k] : 9;
        }

        /// <summary>
        /// U-064 (director, every period): before the hatch, floor(living bots x SupplyFraction) hill-duty bots of the team run
        /// supply: lowest SupplyClassRank first (9 = never), a bot already on supply before a new one, lowest id on a tie.
        /// Everyone else, and everyone after the hatch, is off it.
        /// </summary>
        public static void AssignSupply(World w, BotsState st, TeamId team)
        {
            int n = w.ActorCount;
            bool on = Sources.On(w) && w.Kaiju.State == KaijuState.Egg && !w.Match.Hatched && w.Depth.Sources.Length > 0;
            int count = 0;
            for (int id = 0; id < n; id++)
            {
                Actor a = w.Actors[id];
                if (a.Team == team && a.Alive && BotsState.IsBot(st, w, id)) count++;
            }
            int quota = on ? (int)Math.Floor(count * w.Config.Depth.Sources.SupplyFraction + 1e-9) : 0;
            for (int q = 0; q < quota; q++)
            {
                int best = -1;
                int bestScore = int.MaxValue;
                for (int id = 0; id < n; id++)
                {
                    Actor a = w.Actors[id];
                    if (a.Team != team || !a.Alive || !BotsState.IsBot(st, w, id) || st.Supply[id] == Picked || st.Role[id] != BotsState.RoleHill) continue;
                    int rank = SupplyRank(w, a.Cls);
                    if (rank >= 9) continue;
                    int score = rank * 2 + (st.Supply[id] == 1 ? 0 : 1);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = id;
                    }
                }
                if (best < 0) break;
                st.Supply[best] = Picked;
            }
            for (int id = 0; id < n; id++)
            {
                if (w.Actors[id].Team != team) continue;
                st.Supply[id] = (byte)(st.Supply[id] == Picked ? 1 : 0);
            }
        }

        /// <summary>
        /// U-076 (director, every period, after AssignSupply): ceil(hill-duty bots not on supply x HillSquadFraction) of them
        /// form the team's hill squad: lowest HillSquadStyleRank first, a bot already in the squad before a new one, then the
        /// one nearest the hill, lowest id on a tie. Off (nobody in the squad) with the rules off or the fraction at 0.
        /// </summary>
        public static void AssignHillSquad(World w, BotsState st, TeamId team)
        {
            int n = w.ActorCount;
            RolesConfig R = w.Config.Depth.Roles;
            double frac = R.HillSquadFraction;
            int count = 0;
            for (int id = 0; id < n; id++)
            {
                Actor a = w.Actors[id];
                if (a.Team != team || !a.Alive || !BotsState.IsBot(st, w, id)) continue;
                // U-078: a Hard team keeps every hill bot in the squad (BotSkillConfig.HillSquadFraction).
                BotSkillConfig sk = BotSkill.Profile(w, id);
                if (sk != null && sk.HillSquadFraction >= 0) frac = sk.HillSquadFraction;
                if (st.Role[id] == BotsState.RoleHill && st.Supply[id] == 0) count++;
            }
            bool on = DepthRules.On(w, frac > 0);
            int quota = on && count > 0 ? Math.Min(count, (int)Math.Ceiling(count * frac - 1e-9)) : 0;
            HillDef h = w.Map.Hill;
            for (int q = 0; q < quota; q++)
            {
                int best = -1;
                double bestScore = double.PositiveInfinity;
                for (int id = 0; id < n; id++)
                {
                    Actor a = w.Actors[id];
                    if (a.Team != team || !a.Alive || !BotsState.IsBot(st, w, id) || st.HillSquad[id] == Picked || st.Role[id] != BotsState.RoleHill || st.Supply[id] != 0) continue;
                    int s = BotNames.StyleOf(w, id);
                    double rank = s >= 0 && s < R.HillSquadStyleRank.Length ? R.HillSquadStyleRank[s] : 9;
                    double dx = w.X[id] - h.X, dz = w.Z[id] - h.Z;
                    double score = rank * 1e6 + (st.HillSquad[id] == 1 ? 0 : 1e5) + Math.Sqrt(dx * dx + dz * dz);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = id;
                    }
                }
                if (best < 0) break;
                st.HillSquad[best] = Picked;
            }
            for (int id = 0; id < n; id++)
            {
                if (w.Actors[id].Team != team) continue;
                st.HillSquad[id] = (byte)(st.HillSquad[id] == Picked ? 1 : 0);
            }
        }
    }
}
