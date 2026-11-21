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
    public static partial class TeamBrain
    {
        public const byte JobNone = 0, JobHill = 1, JobGuard = 2, JobKite = 3, JobFeed = 4, JobMine = 5, JobBuild = 6, JobCalled = 7, JobEscort = 8;
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
                EscortJobs(w, st, team, t);
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
                    st.SeenDeaths[id] = a.Deaths;
                    // v2: every hill-job bot walking in from outside the staging ring stages while the hill is lost (respawned
                    // ones and field walkers alike: v1 staged only respawns, 0.58 of arrivals were still outnumbered by 2+).
                    byte job = st.Job[id];
                    if (outnumbered && st.Rally[id] == RallyNone && (job == JobHill || job == JobGuard || job == JobCalled) && d2 > near2 &&
                        (a.AiState == AiState.ToHill || a.AiState == AiState.None))
                    {
                        st.Rally[id] = RallyStage;
                        st.RallySince[id] = now;
                        st.Rallies++;
                    }
                    if (st.Rally[id] == RallyGo && d2 <= in2) st.Rally[id] = RallyNone;
                    if (st.Rally[id] != RallyStage) continue;
                    if (d2 <= stage2) staged++;
                    if (st.RallySince[id] < oldest) oldest = st.RallySince[id];
                }
                if (oldest == double.PositiveInfinity) continue;
                // v2: the group goes in once it can match the holders (their disc bodies minus one), at least GroupMin and at
                // most GroupShare of the team (v1 sent groups of 4 at a hill held by 12).
                int need = Math.Max(b.GroupMin, Math.Min(st.HillBodies[1 - t] - 1, (int)Math.Ceiling(b.GroupShare * teamSize - 1e-9)));
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

        /// <summary>
        /// HOOK for PR #219's KAIJU HOLDS NOTHING (ComebackConfig.KaijuHillPerMille, default 0 there; not on this base). A
        /// partial method: with no body it and its call compile away, so the rule reads as off. After the combine add
        /// Sim/Bots/TeamBrain.Comeback.cs with:
        /// <code>
        /// public static partial class TeamBrain
        /// {
        ///     static partial void KaijuHoldsNothingRule(World w, ref bool cuts)
        ///     {
        ///         cuts = Comeback.On(w) &amp;&amp; w.Config.Depth.Comeback.KaijuHillPerMille &lt; 1000;
        ///     }
        /// }
        /// </code>
        /// </summary>
        static partial void KaijuHoldsNothingRule(World w, ref bool cuts);

        /// <summary>True when `team`'s own sided Kaiju is up and inside the hill ring (hill radius + Kaiju radius) and the
        /// rule makes that cut `team`'s hill score (PR #219, or OwnKaijuRuleAssumed in a test).</summary>
        public static bool OwnKaijuBlocksScore(World w, TeamId team)
        {
            BotBrainConfig b = w.Config.Depth.BotBrain;
            if (!b.OwnKaijuEscort) return false;
            bool cuts = b.OwnKaijuRuleAssumed;
            if (!cuts) KaijuHoldsNothingRule(w, ref cuts);
            if (!cuts) return false;
            Kaiju k = w.Kaiju;
            if (k.KoIn > 0) return false;
            if (!((team == TeamId.Azure && k.State == KaijuState.Azure) || (team == TeamId.Crimson && k.State == KaijuState.Crimson))) return false;
            double r = w.Config.Hill.Radius + w.Config.Kaiju.Radius;
            double dx = k.X - w.Map.Hill.X, dz = k.Z - w.Map.Hill.Z;
            return dx * dx + dz * dz < r * r;
        }

        /// <summary>
        /// KAIJU HOLDS NOTHING job, every step after the hill count: while our own Kaiju on the hill cuts our score and no
        /// enemy stands on the disc, every hill-job bot beyond GuardCount guards (JobGuard first, then the lowest ids stay)
        /// becomes JobEscort: it walks off the disc toward the enemy pad and fights what it meets (the Kaiju follows fights).
        /// The step the Kaiju leaves the ring or an enemy contests, every escort is a hill bot again.
        /// </summary>
        private static void EscortJobs(World w, BotsState st, TeamId team, int t)
        {
            BotBrainConfig b = w.Config.Depth.BotBrain;
            bool escort = st.HillBodies[1 - t] == 0 && OwnKaijuBlocksScore(w, team);
            int keep = 0;
            if (escort)
                for (int id = 0; id < w.ActorCount; id++)
                    if (w.Actors[id].Team == team && w.Actors[id].Alive && st.Job[id] == JobGuard) keep++;
            for (int id = 0; id < w.ActorCount; id++)
            {
                Actor a = w.Actors[id];
                if (a.Team != team || !a.Alive || !BotsState.IsBot(st, w, id)) continue;
                byte job = st.Job[id];
                if (!escort)
                {
                    if (job == JobEscort) st.Job[id] = JobHill;
                    continue;
                }
                if (job != JobHill && job != JobEscort) continue;
                if (keep < b.GuardCount)
                {
                    keep++;
                    st.Job[id] = JobHill;
                    continue;
                }
                if (job != JobEscort) st.Escorts++;
                st.Job[id] = JobEscort;
                st.Rally[id] = RallyNone;
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
        /// (d) HOOK for TerrainHeight (PR #211: stacked blocks, walkable pits, height ladders). Wired at the wave-two combine,
        /// movement verbs only: a 1-level step needs nothing (WalkUp), and a bot walking at a ledge exactly a CLIMB high
        /// (more than WalkUp, at most JumpUp levels above its own level, not a ladder or deck) JUMPS it instead of pushing
        /// ClimbTicks into the wall (an airborne mover passes JumpUp levels, TerrainHeight.SweepBegin). The route field already
        /// sends bots up such edges (EdgeExtra PathCostStep2). Same probe and jump cadence as BotReflex.JumpOverLowWall; no RNG,
        /// no allocation, nothing without the layer or the dodge/jump rule. Not built here (follow-up): a Builder bot that
        /// STACKS a step on a blocked route, a Miner that DIGS a pit in the enemy's approach.
        /// </summary>
        internal static void TerrainVerbsHook(World w, BotsState st, int id, InputFrame output)
        {
            MapDef map = w.Map;
            if (map.Lift == null || !Dodge.On(w) || output.Jump) return;
            Actor a = w.Actors[id];
            if (!a.Alive || a.AirLen > 0 || a.StandUp > 0) return;
            double mx = output.MoveX, mz = output.MoveZ;
            double ml = Math.Sqrt(mx * mx + mz * mz);
            if (!(ml > 0.3)) return;
            DodgeState s = w.Dodges;
            s.Ensure(w);
            DodgeConfig dc = w.Config.Depth.Dodge;
            if (w.Tick - s.LastJump[id] < dc.JumpEveryTicks) return;
            TerrainHeightConfig hc = w.Config.Depth.TerrainHeight;
            int own = TerrainHeight.ActorLevel(w, id);
            mx /= ml;
            mz /= ml;
            double x = w.X[id], z = w.Z[id];
            int here = JsMath.FloorToInt(z) * map.Width + JsMath.FloorToInt(x);
            for (int q = 1; q <= 2; q++)
            {
                double d = dc.JumpProbe * q * 0.5;
                int cx = JsMath.FloorToInt(x + mx * d), cz = JsMath.FloorToInt(z + mz * d);
                if (!MapGrid.InBounds(map, cx, cz)) return;
                int i = cz * map.Width + cx;
                if (i == here) continue;
                if (!TerrainHeight.IsLiftType(map.Cells[i])) return;   // a wall, void, a ladder or deck (a ramp): not a jump
                int rise = map.Lift[i] - own;
                if (rise <= hc.WalkUp) continue;   // level or a step: walk on
                if (rise > hc.JumpUp) return;      // too high to jump: the route goes round
                output.Jump = true;
                s.LastJump[id] = w.Tick;
                st.TerrainJumps++;
                return;
            }
        }
    }
}
