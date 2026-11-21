using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.Progression
{
    /// <summary>What one finished match did to the local player's meta (the results strip reads it).</summary>
    public sealed class MetaResult
    {
        public ClassId Class;
        public MasteryAward Award;
        public RoleStats Stats;
        public MatchRecord Record;
        public readonly List<MasteryReward> Unlocked = new List<MasteryReward>();
        public MasteryReward? Next;
    }

    /// <summary>
    /// b15 profile: once per finished match, after MatchRewardTracker has paid coins and skill points, adds the match to
    /// the profile's meta: the class's mastery XP (MasteryRules), its career totals and a history record (MatchHistory),
    /// and the achievements' best progress. Then it links the replay file once ReplayCapture has written it. View side
    /// only: it reads the finished world and never writes it. Nothing runs in capture mode, replays or batch tools that
    /// use the in-memory profile (Profile.DiskEnabled false still updates memory, which the tests read).
    /// </summary>
    [CriticalMass.Runtime.Flow.GameOverlay(Order = 54, Scope = CriticalMass.Runtime.Flow.OverlayScope.MatchOnly)]
    public sealed class MetaRecorder : MonoBehaviour
    {
        /// <summary>The last finished match's meta (null before the first).</summary>
        public static MetaResult Last { get; private set; }
        public static event Action<MetaResult> Ready;

        /// <summary>Seconds after the final whistle to wait for the replay file name.</summary>
        public const float ReplayLinkSeconds = 20f;

        SimDriverBehaviour _driver;
        int _epoch = int.MinValue;
        string _savedAtStart;
        MatchRecord _waiting;
        int _waitingSeed;
        float _waitUntil;

        void Awake() => _driver = GetComponent<SimDriverBehaviour>();

        void OnEnable() => MatchRewardTracker.RewardsReady += OnRewards;
        void OnDisable() => MatchRewardTracker.RewardsReady -= OnRewards;

        void LateUpdate()
        {
            World w = _driver != null ? _driver.World : null;
            if (w != null && w.Epoch != _epoch)
            {
                _epoch = w.Epoch;
                _savedAtStart = CriticalMass.Runtime.Replay.ReplayCapture.LastSaved;
            }
            if (_waiting == null) return;
            string saved = CriticalMass.Runtime.Replay.ReplayCapture.LastSaved;
            if (!string.IsNullOrEmpty(saved) && saved != _savedAtStart && ReplayMatches(saved, _waitingSeed))
            {
                _waiting.ReplayFile = PlayerProfile.SafeFileName(saved);
                _waiting = null;
                Profile.Commit();
                return;
            }
            if (Time.unscaledTime > _waitUntil) _waiting = null;
        }

        /// <summary>Is a replay file the one of a match on this seed (ReplayStore.FileName ends "-&lt;|seed|&gt;.ext")?</summary>
        public static bool ReplayMatches(string path, int seed)
        {
            string n = PlayerProfile.SafeFileName(path);
            if (n.Length == 0) return false;
            int dot = n.LastIndexOf('.');
            string stem = dot > 0 ? n.Substring(0, dot) : n;
            return stem.EndsWith("-" + Math.Abs((long)seed).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        void OnRewards(MatchRewards r)
        {
            World w = _driver != null ? _driver.World : null;
            if (w == null || r == null || !r.Stats.Completed || _driver.IsReplay || SimDriverBehaviour.CaptureMode) return;
            int local = MatchRewardTracker.LocalActor;
            if (local < 0 || local >= w.ActorCount) return;
            RoleStats stats = StatsFor(w, local, r.Stats);
            bool botOnly = true;
            for (int i = 0; i < w.ActorCount; i++) if (i != local && w.Actors[i].IsHuman) { botOnly = false; break; }
            MatchMode mode = PlayModes.SessionMode;
            int score = ScoreOf(w, local);
            MetaResult m = Record(Profile.Current, w.Actors[local].Cls, w.Actors[local].Team, stats, new MasteryContext
            {
                Completed = true,
                Outcome = r.Stats.Outcome,
                BotOnly = botOnly,
                PracticeMode = IsPractice(mode),
                Day = MasteryRules.DayOf(DateTime.Now),
            }, score, DateTime.UtcNow);
            MatchRecord rec = m.Record;
            rec.Map = PlayModes.MapName ?? string.Empty;
            rec.Mode = (int)mode;
            ModeInfo info = PlayModes.Info(mode);
            rec.ModeLabel = info != null && info.Title != null ? info.Title : mode.ToString().ToUpperInvariant();
            rec.TeamSize = w.Opts.TeamSize;
            rec.Seed = w.Opts.Seed;
            rec.AzureHill = Whole(w.Match.HillSeconds.Azure);
            rec.CrimsonHill = Whole(w.Match.HillSeconds.Crimson);
            try { rec.TurningPoint = MetaHooks.TurningPoint?.Invoke(w) ?? string.Empty; }
            catch (Exception e) { Debug.LogWarning("[Profile] turning point: " + e.Message); rec.TurningPoint = string.Empty; }
            RecordAchievements(Profile.Current, Achievements.Current);
            Profile.Commit();
            _waiting = rec;
            _waitingSeed = rec.Seed;
            _waitUntil = Time.unscaledTime + ReplayLinkSeconds;
            Last = m;
            Ready?.Invoke(m);
        }

        static int Whole(double s) => double.IsFinite(s) && s > 0 ? (int)Math.Round(s) : 0;

        /// <summary>Practice modes give no mastery (Training, Practice, Spectate, the demo).</summary>
        public static bool IsPractice(MatchMode m) => m == MatchMode.Training || m == MatchMode.Practice || m == MatchMode.Spectate || m == MatchMode.Demo;

        static RoleStats StatsFor(World w, int local, in MatchStatsInput fallback)
        {
            RoleStats? hooked = null;
            try { hooked = MetaHooks.RoleStatsFor?.Invoke(w, local); }
            catch (Exception e) { Debug.LogWarning("[Profile] role stats hook: " + e.Message); }
            if (hooked.HasValue) return hooked.Value;
            RoleTally t = RoleTallyRecorder.Current;
            if (t != null && ReferenceEquals(t.World, w)) return t.For(local);
            // no tally (a test world): what the reward tracker saw
            return new RoleStats
            {
                KnockOuts = Math.Max(0, fallback.KnockOuts),
                Deaths = Math.Max(0, fallback.Deaths),
                HillSeconds = Whole(fallback.HillSeconds),
                Delivered = Math.Max(0, fallback.BiomassDelivered) + Math.Max(0, fallback.CorruptionDelivered),
            };
        }

        static int ScoreOf(World w, int local)
        {
            MatchStats s = MatchStatsRecorder.Current;
            if (s == null || local >= s.ActorCount) return 0;
            s.Finish(w);
            return s.Actors[local] != null ? Math.Max(0, s.Actors[local].Points) : 0;
        }

        /// <summary>
        /// The pure core (tests): mastery, career and a new history record for one finished match of cls on team. The
        /// caller fills the record's map, mode, seed and hill split, then saves.
        /// </summary>
        public static MetaResult Record(PlayerProfile p, ClassId cls, TeamId team, in RoleStats stats, in MasteryContext ctx, int score, DateTime utc)
        {
            var m = new MetaResult { Class = cls, Stats = stats };
            ClassMastery track = p.MasteryOf(cls);
            MasteryAward a = MasteryRules.Compute(cls, stats, ctx);
            MasteryRules.Apply(a, track, ctx);
            m.Award = a;
            if (ctx.Completed && !ctx.PracticeMode) MatchHistory.AddCareer(track, stats, ctx.Outcome, score);
            m.Unlocked.AddRange(MasteryRewards.Between(cls, a.LevelBefore, a.LevelAfter));
            m.Next = MasteryRewards.NextAfter(cls, a.LevelAfter);
            var rec = new MatchRecord
            {
                DateUtc = MatchHistory.Stamp(utc),
                Class = cls.ToString(),
                Result = ctx.Outcome == MatchOutcome.Win ? 2 : ctx.Outcome == MatchOutcome.Draw ? 1 : 0,
                Score = Math.Max(0, score),
                Azure = team == TeamId.Azure,
                BotOnly = ctx.BotOnly,
                MasteryXp = a.Xp,
                Stats = stats,
            };
            MatchHistory.Add(p, rec);
            m.Record = rec;
            return m;
        }

        /// <summary>Moves this match's progress into the profile's achievement records (best values only ever rise).</summary>
        public static void RecordAchievements(PlayerProfile p, Achievements a)
        {
            if (p == null || a == null) return;
            for (int i = 0; i < Achievements.All.Length; i++)
            {
                AchievementRecord r = p.Achievement(Achievements.All[i].Id);
                r.Best = Math.Max(r.Best, a.MatchProgress(i));
                if (a.IsUnlocked(i) && !r.Unlocked)
                {
                    r.Unlocked = true;
                    if (string.IsNullOrEmpty(r.UnlockedUtc)) r.UnlockedUtc = MatchHistory.Stamp(DateTime.UtcNow);
                }
                if (r.Unlocked) r.Best = Math.Max(r.Best, Achievements.Target[i]);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Last = null;
            Ready = null;
        }
    }
}
