using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Bots
{
    /// <summary>
    /// b15 bot brain (BotBrainConfig in DepthConfig.cs): a light TEAM brain that only hands out jobs; each class still decides
    /// HOW to do its job (BotFsm, BotSteer, BotMine, BotBuild). Deterministic (no RNG, ids break every tie) and fair: it reads
    /// only the team's own bots, the bodies on the hill disc and the Kaiju, all of which a player sees on screen.
    /// <para>Measured problem it answers (24-seed diagnosis, 2026-11-19): at the hatch every enemy bot left the hill to fight
    /// the Kaiju (BotDirector gave the whole team RoleFightKaiju), so the Kaiju's team held the hill 74-89% of the second
    /// half; attackers trickled back one by one (86% arrived outnumbered by 2+, 47% down within 5 s).</para>
    /// <para>Terrain verbs (d): bots already jump LowWalls (BotReflex.JumpOverLowWall), walk built ladders and bridges
    /// (BotFields.Passable takes Cell.Bridge) and the brain sends kiters through BotSteer.DodgeKaiju (step out of the claw cone
    /// and jump the slam ring) at every skill level. DIG / STACK / PIT use TerrainHeight (PR #211), which is not on this base:
    /// see <see cref="TerrainVerbsHook"/>.</para>
    /// </summary>
    public static class TeamBrain
    {
        public const byte JobNone = 0, JobHill = 1, JobGuard = 2, JobKite = 3, JobFeed = 4, JobMine = 5, JobBuild = 6, JobCalled = 7;
        public const byte RallyNone = 0, RallyStage = 1, RallyGo = 2;

        public static bool On(GameConfig c) =>
            c.Depth.BotBrain != null && c.Depth.BotBrain.Enabled && !c.Depth.Classic && !c.Physics.Classic;

        public static bool On(World w) => On(w.Config);

        private static int TeamIndex(TeamId t) => t == TeamId.Azure ? 0 : 1;

        /// <summary>Living bodies of `team` on the hill disc this tick (TeamBrain.Tick counts them).</summary>
        public static int HillBodies(BotsState st, TeamId team) => st.HillBodies[TeamIndex(team)];

        /// <summary>How many more bodies the other team has on the hill disc than `team` (negative: `team` leads).</summary>
        public static int HillDeficit(BotsState st, TeamId team) => st.HillBodies[1 - TeamIndex(team)] - st.HillBodies[TeamIndex(team)];

        private static int KiteRank(ClassId c)
        {
            switch (c)
            {
                case ClassId.Farmer: return 0;      // fastest (4.2) with the longest reach
                case ClassId.Miner: return 1;
                case ClassId.Demolitions: return 2;
                case ClassId.Warrior: return 3;     // slow: better on the hill
                default: return -1;                 // Healer and Builder never kite
            }
        }

        /// <summary>
        /// (a) BotDirector hook, a hostile Kaiju: the kite squad (fast fighters, incumbents first, then the closest to the
        /// Kaiju; lowest id on a tie) gets RoleFightKaiju in desired[]; every other bot keeps RoleHill (never all of them).
        /// </summary>
        internal static void PickKiteSquad(World w, BotsState st, int[] ids, int count, byte[] desired)
        {
            BotBrainConfig b = w.Config.Depth.BotBrain;
            int quota = count <= 10 ? b.KiteSquad : b.KiteSquadLarge;
            if (quota > count - 1) quota = Math.Max(0, count - 1);
            Kaiju k = w.Kaiju;
            for (int c = 0; c < count; c++) desired[c] = (byte)BotsState.RoleHill;
            for (int q = 0; q < quota; q++)
            {
                int best = -1;
                double bestScore = double.PositiveInfinity;
                for (int c = 0; c < count; c++)
                {
                    if (desired[c] == BotsState.RoleFightKaiju) continue;
                    int id = ids[c];
                    Actor a = w.Actors[id];
                    int rank = KiteRank(a.Cls);
                    if (rank < 0 || a.CarryAmount > 0) continue;
                    bool incumbent = st.Role[id] == BotsState.RoleFightKaiju;
                    if (!incumbent && (a.MaxHp <= 0 || a.Hp < a.MaxHp * b.KiteMinHpShare)) continue;
                    double dx = w.X[id] - k.X, dz = w.Z[id] - k.Z;
                    double score = (incumbent ? 0 : 1e6) + rank * 1e4 + Math.Sqrt(dx * dx + dz * dz);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = c;
                    }
                }
                if (best < 0) break;
                desired[best] = (byte)BotsState.RoleFightKaiju;
            }
        }

        /// <summary>(a) After the director's roles, hill squad and supply: write each living bot's job.</summary>
        internal static void AssignJobs(World w, BotsState st, TeamId team)
        {
            BotBrainConfig b = w.Config.Depth.BotBrain;
            int guards = 0;
            bool lost = HillDeficit(st, team) >= b.CallToHillDeficit;
            for (int id = 0; id < w.ActorCount; id++)
            {
                Actor a = w.Actors[id];
                if (a.Team != team || !a.Alive || !BotsState.IsBot(st, w, id)) continue;
                byte job;
                if (st.Role[id] == BotsState.RoleFightKaiju) job = JobKite;
                else if (st.Role[id] == BotsState.RoleKaiju) job = JobFeed;
                else if (st.HillSquad[id] != 0 && guards < b.GuardCount)
                {
                    job = JobGuard;
                    guards++;
                }
                else if (a.Cls == ClassId.Miner || a.Cls == ClassId.Builder)
                    job = lost && a.CarryAmount == 0 && st.Supply[id] == 0 ? JobCalled : a.Cls == ClassId.Miner ? JobMine : JobBuild;
                else job = JobHill;
                st.Job[id] = job;
            }
        }

        /// <summary>
        /// Once per sim step, before the thinks: the hill bodies, and (b) the group-up: a bot that respawned while the enemy
        /// holds the hill by RallyOutnumberedBy or more stages at StageRadius; the group goes in together.
        /// </summary>
        public static void Tick(World w, BotsState st)
        {
            if (!On(w)) return;
            BotBrainConfig b = w.Config.Depth.BotBrain;
            HillDef hill = w.Map.Hill;
            int n = w.ActorCount;
            st.HillBodies[0] = 0;
            st.HillBodies[1] = 0;
            for (int id = 0; id < n; id++)
            {
                Actor a = w.Actors[id];
                if (!a.Alive) continue;
                double dx = w.X[id] - hill.X, dz = w.Z[id] - hill.Z;
                if (dx * dx + dz * dz <= hill.R * hill.R) st.HillBodies[TeamIndex(a.Team)]++;
            }
            double now = w.Match.Time;
            double stage2 = (b.StageRadius + 1.5) * (b.StageRadius + 1.5);
            double near2 = (b.StageRadius + 4) * (b.StageRadius + 4);
            double in2 = (hill.R + 1) * (hill.R + 1);
            for (int t = 0; t < 2; t++)
            {
                TeamId team = t == 0 ? TeamId.Azure : TeamId.Crimson;
                int staged = 0, teamSize = 0;
                double oldest = double.PositiveInfinity;
                bool outnumbered = st.HillBodies[1 - t] - st.HillBodies[t] >= b.RallyOutnumberedBy;
                for (int id = 0; id < n; id++)
                {
                    Actor a = w.Actors[id];
                    if (a.Team != team) continue;
                    teamSize++;
                    if (!a.Alive || !BotsState.IsBot(st, w, id))
                    {
                        st.Rally[id] = RallyNone;
                        continue;
                    }
                    double dx = w.X[id] - hill.X, dz = w.Z[id] - hill.Z, d2 = dx * dx + dz * dz;
                    if (st.SeenDeaths[id] != a.Deaths)
                    {
                        st.SeenDeaths[id] = a.Deaths;
                        byte job = st.Job[id];
                        if (outnumbered && (job == JobHill || job == JobGuard || job == JobCalled) && d2 > near2)
                        {
                            st.Rally[id] = RallyStage;
                            st.RallySince[id] = now;
                            st.Rallies++;
                        }
                    }
                    if (st.Rally[id] == RallyGo && d2 <= in2) st.Rally[id] = RallyNone;
                    if (st.Rally[id] != RallyStage) continue;
                    if (d2 <= stage2) staged++;
                    if (st.RallySince[id] < oldest) oldest = st.RallySince[id];
                }
                if (oldest == double.PositiveInfinity) continue;
                int need = Math.Max(b.GroupMin, (int)Math.Ceiling(b.GroupShare * teamSize - 1e-9));
                bool go = !outnumbered || staged + st.HillBodies[t] >= need || now - oldest >= b.RallySeconds;
                bool released = false;
                for (int id = 0; id < n; id++)
                {
                    if (w.Actors[id].Team != team || st.Rally[id] != RallyStage) continue;
                    double dx = w.X[id] - hill.X, dz = w.Z[id] - hill.Z, d2 = dx * dx + dz * dz;
                    // The group goes in together; a straggler still walking in keeps coming, a long-stuck one gives up waiting.
                    if ((go && d2 <= near2) || !outnumbered || now - st.RallySince[id] >= b.RallySeconds + 4)
                    {
                        st.Rally[id] = RallyGo;
                        released = true;
                    }
                }
                if (released) st.Releases++;
            }
        }

        /// <summary>(b) Steering: true when bot id should wait at the staging ring now (it has reached StageRadius).</summary>
        internal static bool HoldsAtStage(World w, BotsState st, int id)
        {
            if (st.Rally[id] != RallyStage) return false;
            HillDef hill = w.Map.Hill;
            double r = w.Config.Depth.BotBrain.StageRadius;
            double dx = w.X[id] - hill.X, dz = w.Z[id] - hill.Z;
            return dx * dx + dz * dz <= r * r;
        }

        /// <summary>
        /// (c) Target score for bot id against visible enemy j at squared distance d2 (lower is better): a Healer first, then
        /// the lowest-health enemy within attack reach, then the nearest; the current target keeps TargetStickScore.
        /// </summary>
        internal static double TargetScore(World w, BotsState st, int id, int j, double d2, double reach)
        {
            BotBrainConfig b = w.Config.Depth.BotBrain;
            Actor t = w.Actors[j];
            double s;
            if (b.TargetHealersFirst && t.Cls == ClassId.Healer) s = Math.Sqrt(d2) * 0.01;
            else if (d2 <= reach * reach) s = 10 + (t.MaxHp > 0 ? t.Hp / t.MaxHp : 1) * 5;
            else s = 20 + Math.Sqrt(d2);
            if (j == st.Target[id]) s -= b.TargetStickScore;
            return s;
        }

        /// <summary>
        /// (a) Kite steering point: the side of the Kaiju away from the hill, at 0.75 of the kiter's reach, so its chase walks
        /// it off the disc (it keeps chasing a fighter that hit it while that fighter is within KaijuHillLeash of the hill).
        /// </summary>
        internal static void KitePoint(World w, int id, double reach, out double px, out double pz)
        {
            Kaiju k = w.Kaiju;
            HillDef hill = w.Map.Hill;
            double ox = k.X - hill.X, oz = k.Z - hill.Z;
            double l = Math.Sqrt(ox * ox + oz * oz);
            if (l < 1)
            {
                ox = w.X[id] - hill.X;
                oz = w.Z[id] - hill.Z;
                l = Math.Sqrt(ox * ox + oz * oz);
            }
            if (l < 1e-6)
            {
                ox = 1;
                oz = 0;
                l = 1;
            }
            px = k.X + ox / l * reach * 0.75;
            pz = k.Z + oz / l * reach * 0.75;
        }

        /// <summary>
        /// (d) HOOK for TerrainHeight (PR #211: stacked blocks, walkable pits, height ladders). Not on this base, so nothing
        /// can be called. When #211 lands: behind its TerrainHeight.Enabled check, a Builder bot whose hill route is blocked
        /// stacks a step, and a Miner on a lost hill (JobCalled) digs a pit in the enemy's approach. Until then a no-op.
        /// </summary>
        internal static void TerrainVerbsHook(World w, BotsState st, int id, InputFrame output)
        {
        }
    }
}
