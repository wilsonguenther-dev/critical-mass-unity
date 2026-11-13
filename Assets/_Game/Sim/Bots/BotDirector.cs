using System;
using System.Runtime.CompilerServices;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Bots
{
    /// <summary>Team director (CM-09, tuned by CM-19): role quotas, Kaiju gambit, short-handed plan, cocky leader, corruption flag. Port of src/bots/director.ts (09e0904).</summary>
    public static class BotDirector
    {
        public const double RoleLockSeconds = 6, GambitDeficitSeconds = 10, GambitExtra = 2, AdvantageExtra = 2, ShortHandedDuty = 1, CockyLeadSeconds = 30, InactiveSeconds = 15;

        /// <summary>Director memory per world (allocated once per world; reset in place when the world restarts).</summary>
        private sealed class DirectorMem
        {
            public int Epoch;
            public double LastTime;
            /// <summary>Per team index (0 azure, 1 crimson): 1 while the team plays 'cocky' (far ahead on the hill).</summary>
            public byte[] Cocky;
            /// <summary>Contribution tracker for actors not driven by the bots: last seen hillSeconds / kills, last contribution time.</summary>
            public double[] LastHill;
            public int[] LastKills;
            public double[] ContribTime;
        }

        private static readonly ConditionalWeakTable<World, DirectorMem> Mems = new ConditionalWeakTable<World, DirectorMem>();

        private static DirectorMem MemFor(World world)
        {
            int cap = World.ActorCapacity(world.Config);
            DirectorMem mem;
            if (!Mems.TryGetValue(world, out mem))
            {
                mem = new DirectorMem
                {
                    Epoch = -1,
                    LastTime = 0,
                    Cocky = new byte[2],
                    LastHill = new double[cap],
                    LastKills = new int[cap],
                    ContribTime = new double[cap],
                };
                Mems.Add(world, mem);
            }
            double now = world.Match.Time;
            if (mem.Epoch != world.Epoch || now < mem.LastTime)
            {
                mem.Epoch = world.Epoch;
                mem.Cocky[0] = 0;
                mem.Cocky[1] = 0;
                for (int id = 0; id < cap; id++)
                {
                    Actor a = id < world.ActorCount ? world.Actors[id] : null;
                    mem.LastHill[id] = a != null ? a.HillSeconds : 0;
                    mem.LastKills[id] = a != null ? a.Kills : 0;
                    // A fresh match gives everyone the full window to walk out and start playing.
                    mem.ContribTime[id] = now;
                }
            }
            mem.LastTime = now;
            return mem;
        }

        /// <summary>True while `team` is far enough ahead on the hill to play cocky (fsm.ts chases off the hill).</summary>
        public static bool IsCocky(World w, TeamId team)
        {
            DirectorMem mem;
            return Mems.TryGetValue(w, out mem) && mem.Epoch == w.Epoch && mem.Cocky[team == TeamId.Azure ? 0 : 1] == 1;
        }

        /// <summary>Update the contribution tracker. Bots always count as contributing (they are the team's plan).</summary>
        private static void TrackContribution(World world, BotsState st, DirectorMem mem, double now)
        {
            for (int id = 0; id < world.ActorCount; id++)
            {
                Actor a = world.Actors[id];
                bool hillGrew = a.HillSeconds > mem.LastHill[id];
                bool killed = a.Kills > mem.LastKills[id];
                mem.LastHill[id] = a.HillSeconds;
                mem.LastKills[id] = a.Kills;
                if (BotsState.IsBot(st, world, id) || hillGrew || killed || a.CarryAmount > 0 || a.GatherProgress > 0) mem.ContribTime[id] = now;
            }
        }

        /// <summary>Players on `team` who are playing: every bot, and every non-bot actor that contributed within InactiveSeconds.</summary>
        private static int ActiveCount(World world, BotsState st, DirectorMem mem, TeamId team, double now)
        {
            int c = 0;
            for (int id = 0; id < world.ActorCount; id++)
            {
                if (world.Actors[id].Team != team) continue;
                if (BotsState.IsBot(st, world, id) || now - mem.ContribTime[id] < InactiveSeconds) c++;
            }
            return c;
        }

        private static int[] Ids = new int[0];
        private static double[] Score = new double[0];
        private static byte[] Desired = new byte[0];
        private static byte[] Chosen = new byte[0];

        /// <summary>Grow the module scratch to ActorCapacity (the browser allocates it once at ACTOR_CAPACITY).</summary>
        private static void EnsureScratch(GameConfig c)
        {
            int cap = World.ActorCapacity(c);
            if (Ids.Length >= cap) return;
            Ids = new int[cap];
            Score = new double[cap];
            Desired = new byte[cap];
            Chosen = new byte[cap];
        }

        private static double NearestBiomassDist(World world, BotsState st, int id)
        {
            BotFields f = st.Fields;
            double x = world.X[id];
            double z = world.Z[id];
            double best = double.PositiveInfinity;
            for (int i = 0; i < world.Nodes.Length; i++)
            {
                if (world.Nodes[i].Kind != NodeKind.Biomass) continue;
                int fi = f.FieldOfNode[i];
                if (fi < BotFields.FieldNode0) continue;
                double d = BotFields.FieldDistAt(f, fi, x, z);
                if (d < best) best = d;
            }
            return best == double.PositiveInfinity ? 1e6 : best;
        }

        /// <summary>Pick the `quota` lowest-score entries of ids[0..n) (ties: lower id) and mark them in `chosen`.</summary>
        private static void PickLowest(int n, int quota)
        {
            for (int k = 0; k < n; k++) Chosen[k] = 0;
            for (int q = 0; q < quota; q++)
            {
                int best = -1;
                for (int k = 0; k < n; k++)
                {
                    if (Chosen[k] != 0) continue;
                    if (best < 0 || Score[k] < Score[best] || (Score[k] == Score[best] && Ids[k] < Ids[best])) best = k;
                }
                if (best < 0) return;
                Chosen[best] = 1;
            }
        }

        /// <summary>
        /// CM-64: the team's dead bots that still hold Kaiju duty (a dead bot is outside the living roster, so AssignTeam never
        /// rewrites its role; it respawns as the incumbent). Before the hatch their seats are reserved: the Kaiju quota minus
        /// this many go to living bots, so a feeder killed at the shrine never pulls a hill bot off the hill. Allocation-free.
        /// </summary>
        public static int DeadKaijuDuty(World world, BotsState st, TeamId team)
        {
            int dead = 0;
            for (int id = 0; id < world.ActorCount; id++)
            {
                Actor a = world.Actors[id];
                if (a.Team != team || a.Alive || st.Role[id] != BotsState.RoleKaiju || !BotsState.IsBot(st, world, id)) continue;
                dead++;
            }
            return dead;
        }

        private static void AssignTeam(World world, BotsState st, TeamId team, double now, bool shortHanded, bool ahead)
        {
            GameConfig cfg = world.Config;
            int n = world.ActorCount;
            int count = 0;
            for (int id = 0; id < n; id++)
            {
                Actor a = world.Actors[id];
                if (a.Team != team || !a.Alive || !BotsState.IsBot(st, world, id)) continue;
                Ids[count++] = id;
            }
            if (count == 0) return;
            MatchState m = world.Match;
            Kaiju k = world.Kaiju;
            bool hatched = m.Hatched || k.State != KaijuState.Egg;
            for (int c = 0; c < count; c++) Desired[c] = (byte)BotsState.RoleHill;

            if (!hatched)
            {
                int hillQuota = (int)Math.Ceiling(count * cfg.Bots.HillMinFraction);
                int kaijuQuota = Math.Max(1, (int)JsMath.Round(count * cfg.Bots.KaijuGroupFraction));
                TeamId otherT = team == TeamId.Azure ? TeamId.Crimson : TeamId.Azure;
                if (shortHanded) kaijuQuota = (int)ShortHandedDuty;
                else
                {
                    if (ahead) kaijuQuota += (int)AdvantageExtra;
                    if (m.HillSeconds[otherT] - m.HillSeconds[team] >= GambitDeficitSeconds) kaijuQuota += (int)GambitExtra;
                }
                if (kaijuQuota > count - hillQuota) kaijuQuota = Math.Max(0, count - hillQuota);
                // CM-64: a Kaiju-duty bot that died keeps its duty while it respawns.
                kaijuQuota = Math.Max(0, kaijuQuota - DeadKaijuDuty(world, st, team));
                for (int c = 0; c < count; c++)
                {
                    int id = Ids[c];
                    Actor a = world.Actors[id];
                    int classRank = a.Cls == ClassId.Farmer ? 0 : 1;
                    bool incumbent = st.Role[id] == BotsState.RoleKaiju || a.CarryAmount > 0;
                    Score[c] = classRank * 1e7 + (incumbent ? 0 : 1 + NearestBiomassDist(world, st, id));
                }
                PickLowest(count, kaijuQuota);
                for (int c = 0; c < count; c++) if (Chosen[c] != 0) Desired[c] = (byte)BotsState.RoleKaiju;
            }
            else
            {
                bool hostile = k.State == KaijuState.Berserk || (k.State != KaijuState.Neutral && k.State != KaijuState.Egg && k.State != SimEnumMap.ToKaijuState(team));
                int tgt = k.TargetId;
                bool huntingUs = tgt >= 0 && tgt < world.ActorCount && world.Actors[tgt].Team == team;
                if (hostile && k.KoIn <= 0 && (k.State != KaijuState.Berserk || huntingUs))
                {
                    for (int c = 0; c < count; c++) Desired[c] = (byte)BotsState.RoleFightKaiju;
                }
            }

            // Apply with the role lock.
            for (int c = 0; c < count; c++)
            {
                int id = Ids[c];
                if (st.Role[id] == Desired[c]) continue;
                if (now - st.RoleSince[id] < RoleLockSeconds) continue;
                st.Role[id] = Desired[c];
                st.RoleSince[id] = now;
            }

            // Corruption flag: at most one kaiju-role bot.
            TeamId other = team == TeamId.Azure ? TeamId.Crimson : TeamId.Azure;
            bool behind = !hatched && !m.BerserkLocked && m.Allegiance[other] - m.Allegiance[team] >= cfg.Bots.CorruptWhenBehindBy;
            int keep = -1;
            if (behind)
            {
                for (int c = 0; c < count; c++)
                {
                    int id = Ids[c];
                    if (st.Corrupt[id] != 0 && st.Role[id] == BotsState.RoleKaiju && world.Actors[id].Carry != CarryKind.Biomass && Roles.MayCorrupt(world, world.Actors[id].Cls)) keep = id;
                }
                if (keep < 0)
                {
                    // Prefer a kaiju bot that is not the team's best biomass carrier (the last-picked, i.e. highest score).
                    double bestScore = double.NegativeInfinity;
                    for (int c = 0; c < count; c++)
                    {
                        int id = Ids[c];
                        if (st.Role[id] != BotsState.RoleKaiju || world.Actors[id].Carry == CarryKind.Biomass) continue;
                        if (!Roles.MayCorrupt(world, world.Actors[id].Cls)) continue; // U-063
                        double s = st.Role[id] == BotsState.RoleKaiju ? Score[c] : double.NegativeInfinity;
                        if (keep < 0 || s > bestScore)
                        {
                            keep = id;
                            bestScore = s;
                        }
                    }
                }
            }
            // U-063 class roles: the egg crew is feeders, who never touch corruption. A team that is behind and has no raider
            // on egg duty sends one: the living raider bot of the highest class index (Demolitions, then Miner, then Warrior;
            // lowest id on a tie) that carries no biomass takes egg duty for the corruption run.
            if (behind && keep < 0 && Roles.On(world))
            {
                int pick = -1;
                for (int c = 0; c < count; c++)
                {
                    int id = Ids[c];
                    Actor ra = world.Actors[id];
                    if (ra.Carry == CarryKind.Biomass || !Roles.MayCorrupt(world, ra.Cls)) continue;
                    // U-072: a raider (CorruptionRunner) first, then the highest class index, lowest id on a tie.
                    if (pick < 0) { pick = id; continue; }
                    bool rr = Roles.IsRunner(world, ra.Cls), pr = Roles.IsRunner(world, world.Actors[pick].Cls);
                    if ((rr && !pr) || (rr == pr && (int)ra.Cls > (int)world.Actors[pick].Cls)) pick = id;
                }
                if (pick >= 0)
                {
                    keep = pick;
                    if (st.Role[pick] != BotsState.RoleKaiju)
                    {
                        st.Role[pick] = (byte)BotsState.RoleKaiju;
                        st.RoleSince[pick] = now;
                    }
                }
            }
            bool spoil = behind && shortHanded;
            for (int id = 0; id < n; id++)
            {
                Actor a = world.Actors[id];
                if (a.Team != team) continue;
                st.Corrupt[id] = (byte)(id == keep || (spoil && st.Role[id] == BotsState.RoleKaiju && a.Carry != CarryKind.Biomass && Roles.MayCorrupt(world, a.Cls)) ? 1 : 0);
            }
        }

        /// <summary>Run the director when its period has elapsed (match time).</summary>
        public static void DirectorTick(World w, BotsState st)
        {
            double now = w.Match.Time;
            if (now + 1e-9 < st.NextDirectorTime) return;
            st.NextDirectorTime = now + w.Config.Bots.DirectorPeriodSeconds;
            EnsureScratch(w.Config);
            DirectorMem mem = MemFor(w);
            TrackContribution(w, st, mem, now);
            int activeA = ActiveCount(w, st, mem, TeamId.Azure, now);
            int activeC = ActiveCount(w, st, mem, TeamId.Crimson, now);
            for (int t = 0; t < TeamIds.All.Count; t++)
            {
                TeamId team = TeamIds.All[t];
                int mine = team == TeamId.Azure ? activeA : activeC;
                int theirs = team == TeamId.Azure ? activeC : activeA;
                AssignTeam(w, st, team, now, mine < theirs, mine > theirs);
                BotDepth.AssignSupply(w, st, team); // U-064
            }
            byte[] f = mem.Cocky;
            MatchState m = w.Match;
            double lead = m.HillSeconds.Azure - m.HillSeconds.Crimson;
            f[0] = (byte)(m.Hatched && lead >= CockyLeadSeconds ? 1 : 0);
            f[1] = (byte)(m.Hatched && -lead >= CockyLeadSeconds ? 1 : 0);
        }
    }
}
