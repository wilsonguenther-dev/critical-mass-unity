using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.Game
{
    /// <summary>One local achievement: stable id (the save key), title and the one-line rule shown to the player.</summary>
    public readonly struct AchievementDef
    {
        public readonly string Id;
        public readonly string Title;
        public readonly string Description;

        public AchievementDef(string id, string title, string description)
        {
            Id = id;
            Title = title;
            Description = description;
        }
    }

    /// <summary>Where unlocks are kept between sessions.</summary>
    public interface IAchievementStore
    {
        bool Has(string id);
        void Set(string id);
        /// <summary>Writes pending changes to disk (called at match end, never per frame).</summary>
        void Flush();
    }

    /// <summary>PlayerPrefs store: key "cm.ach." + id = 1.</summary>
    public sealed class PlayerPrefsAchievementStore : IAchievementStore
    {
        public const string Prefix = "cm.ach.";
        public bool Has(string id) => CmPrefs.GetInt(Prefix + id, 0) == 1;
        public void Set(string id) => CmPrefs.SetInt(Prefix + id, 1);
        public void Flush() => CmPrefs.Save();
    }

    /// <summary>
    /// b15 profile v2: the achievements live in the versioned profile (PlayerProfile.Achievements), not in PlayerPrefs.
    /// Set stamps the unlock time from the system clock; Flush saves the profile (match end only).
    /// </summary>
    public sealed class ProfileAchievementStore : IAchievementStore
    {
        public bool Has(string id) => CriticalMass.Runtime.Progression.Profile.Current.HasAchievement(id);

        public void Set(string id)
        {
            CriticalMass.Runtime.Progression.AchievementRecord r = CriticalMass.Runtime.Progression.Profile.Current.Achievement(id);
            if (r.Unlocked) return;
            r.Unlocked = true;
            r.UnlockedUtc = CriticalMass.Runtime.Progression.MatchHistory.Stamp(DateTime.UtcNow);
        }

        public void Flush() => CriticalMass.Runtime.Progression.Profile.Commit();
    }

    /// <summary>In-memory store for tests, captures and autopilot runs (nothing reaches the player's save).</summary>
    public sealed class MemoryAchievementStore : IAchievementStore
    {
        readonly HashSet<string> _set = new HashSet<string>();
        public bool Has(string id) => _set.Contains(id);
        public void Set(string id) => _set.Add(id);
        public void Flush() { }
    }

    /// <summary>
    /// DG-38: ten local achievements, judged from the CombatTally's event stream for the local actor only. Pure C#, so
    /// every trigger has a unit test on a scripted stream. Unlocks are lifetime (kept in the store); UnlockedThisMatch
    /// lists what this match earned, in order, for the results screen. Allocation-free while a match runs.
    /// </summary>
    public sealed class Achievements
    {
        public const int FirstFeeding = 0, HillHolder = 1, Kingmaker = 2, OpeningBlow = 3, CleanSweep = 4,
            OnARoll = 5, GiantSlayer = 6, FullHarvest = 7, Payback = 8, BannerRaised = 9;

        public const double HillHolderSeconds = 60;
        public const int CleanSweepKnockouts = 3;
        public const int OnARollStreak = 5;
        public const int FullHarvestUnits = 10;

        /// <summary>b15 profile: the number each achievement counts toward (1 = done or not), for honest progress bars.</summary>
        public static readonly int[] Target = { 1, (int)HillHolderSeconds, 1, 1, CleanSweepKnockouts, OnARollStreak, 1, FullHarvestUnits, 1, 1 };

        /// <summary>True when the target is counted within one match (the bar shows the best single match).</summary>
        public static bool InOneMatch(int index) => index == HillHolder || index == CleanSweep || index == OnARoll || index == FullHarvest;

        /// <summary>
        /// b15 profile: can this achievement be earned in a game with this config? Biomass ones need a class that may feed
        /// the egg; the rest need what every match has (the hill, knock-outs, the Kaiju, a result). The profile hides the
        /// ones that cannot be earned instead of showing a bar that never moves.
        /// </summary>
        public static bool Earnable(int index, GameConfig cfg)
        {
            if (index < 0 || index >= All.Length) return false;
            if (index == FirstFeeding || index == FullHarvest || index == Kingmaker)
            {
                bool[] may = cfg?.Depth?.Roles?.MayFeed;
                if (may == null) return true;
                foreach (bool b in may) if (b) return true;
                return false;
            }
            return true;
        }

        /// <summary>Every achievement in display order (index = the constants above). Built-in words; no words-pack file yet.</summary>
        public static readonly AchievementDef[] All =
        {
            new AchievementDef("first-feeding", "First Feeding", "Deliver biomass to the egg."),
            new AchievementDef("hill-holder", "Hill Holder", "Stand on the hill for 60 s in one match."),
            new AchievementDef("kingmaker", "Kingmaker", "Your team's biomass decided the hatch, and some of it was yours."),
            new AchievementDef("opening-blow", "Opening Blow", "Score the first knock-out of the match."),
            new AchievementDef("clean-sweep", "Clean Sweep", "Knock out three enemies with one blow."),
            new AchievementDef("on-a-roll", "On a Roll", "Five knock-outs without being knocked out."),
            new AchievementDef("giant-slayer", "Giant Slayer", "Land the hit that knocks out the Kaiju."),
            new AchievementDef("full-harvest", "Full Harvest", "Deliver 10 biomass in one match."),
            new AchievementDef("payback", "Payback", "Knock out whoever last knocked you out."),
            new AchievementDef("banner-raised", "Banner Raised", "Win a match."),
        };

        /// <summary>The live instance (AchievementToast creates it); the results screen reads it.</summary>
        public static Achievements Current { get; set; }

        /// <summary>Raised once per lifetime unlock with the achievement's index.</summary>
        public event Action<int> Unlocked;

        public int LocalActor = LocalSeat.Actor;
        /// <summary>The local player's team (the host sets it every frame from the world).</summary>
        public TeamId LocalTeam = TeamId.Azure;

        readonly IAchievementStore _store;
        readonly bool[] _unlocked = new bool[All.Length];
        readonly bool[] _earnedThisMatch = new bool[All.Length];
        readonly List<int> _thisMatch = new List<int>(All.Length);
        int _biomassThisMatch;
        int _nemesis = KillSource.Unknown;
        int _streak;
        bool _anyKnockout;
        int _hillThisMatch, _bestStreak, _bestBlow;

        /// <summary>b15 profile: this match's progress toward an achievement's Target (capped at it).</summary>
        public int MatchProgress(int index)
        {
            if (index < 0 || index >= All.Length) return 0;
            int v;
            switch (index)
            {
                case HillHolder: v = _hillThisMatch; break;
                case CleanSweep: v = _bestBlow; break;
                case OnARoll: v = _bestStreak; break;
                case FullHarvest: v = _biomassThisMatch; break;
                case FirstFeeding: v = _biomassThisMatch > 0 ? 1 : 0; break;
                default: v = _earnedThisMatch[index] ? 1 : 0; break;
            }
            if (_earnedThisMatch[index]) v = Math.Max(v, Target[index]);
            return Math.Min(v, Target[index]);
        }

        public Achievements(IAchievementStore store)
        {
            _store = store ?? new MemoryAchievementStore();
            for (int i = 0; i < All.Length; i++) _unlocked[i] = _store.Has(All[i].Id);
        }

        /// <summary>Earned in this match, in the order they were earned (includes ones already unlocked before).</summary>
        public IReadOnlyList<int> UnlockedThisMatch => _thisMatch;
        public bool IsUnlocked(int index) => index >= 0 && index < All.Length && _unlocked[index];
        public bool EarnedThisMatch(int index) => index >= 0 && index < All.Length && _earnedThisMatch[index];

        public int UnlockedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _unlocked.Length; i++) if (_unlocked[i]) n++;
                return n;
            }
        }

        /// <summary>A new match: per-match progress clears; lifetime unlocks stay.</summary>
        public void ResetMatch()
        {
            for (int i = 0; i < _earnedThisMatch.Length; i++) _earnedThisMatch[i] = false;
            _thisMatch.Clear();
            _biomassThisMatch = 0;
            _nemesis = KillSource.Unknown;
            _streak = 0;
            _anyKnockout = false;
            _hillThisMatch = _bestStreak = _bestBlow = 0;
        }

        /// <summary>Per-frame state the event stream does not carry: the local actor's hill seconds (Actor.HillSeconds).</summary>
        public void Sample(double localHillSeconds)
        {
            if (double.IsFinite(localHillSeconds) && localHillSeconds > _hillThisMatch) _hillThisMatch = (int)Math.Min(localHillSeconds, int.MaxValue);
            if (localHillSeconds >= HillHolderSeconds) Earn(HillHolder);
        }

        /// <summary>One event from CombatTally.Emitted (or a test's scripted stream).</summary>
        public void Feed(CombatEvent e)
        {
            switch (e.Kind)
            {
                case CombatEventKind.Knockout:
                    OnKnockout(e);
                    break;
                case CombatEventKind.DeliverBiomass:
                    if (e.Actor != LocalActor || e.Amount <= 0) break;
                    _biomassThisMatch += e.Amount;
                    Earn(FirstFeeding);
                    if (_biomassThisMatch >= FullHarvestUnits) Earn(FullHarvest);
                    break;
                case CombatEventKind.Hatch:
                {
                    int mine = LocalTeam == TeamId.Azure ? 1 : 2;   // Kaiju.Hatch.HatchCode: 1 Azure, 2 Crimson
                    if (e.Amount == mine && _biomassThisMatch > 0) Earn(Kingmaker);
                    break;
                }
                case CombatEventKind.KaijuKnockout:
                    if (e.Other == LocalActor) Earn(GiantSlayer);
                    break;
                case CombatEventKind.MatchEnd:
                {
                    var r = (MatchResult)e.Amount;
                    if ((r == MatchResult.Azure && LocalTeam == TeamId.Azure) || (r == MatchResult.Crimson && LocalTeam == TeamId.Crimson))
                        Earn(BannerRaised);
                    _store.Flush();
                    break;
                }
            }
        }

        void OnKnockout(CombatEvent e)
        {
            bool first = !_anyKnockout;
            _anyKnockout = true;
            if (e.Actor == LocalActor)
            {
                _streak = 0;
                _nemesis = e.Other;
                return;
            }
            if (e.Other != LocalActor) return;
            if (first) Earn(OpeningBlow);
            if (e.Blow > _bestBlow) _bestBlow = e.Blow;
            if (e.Blow >= CleanSweepKnockouts) Earn(CleanSweep);
            _streak++;
            if (_streak > _bestStreak) _bestStreak = _streak;
            if (_streak >= OnARollStreak) Earn(OnARoll);
            if (_nemesis >= 0 && e.Actor == _nemesis)
            {
                Earn(Payback);
                _nemesis = KillSource.Unknown;
            }
        }

        void Earn(int index)
        {
            if (_earnedThisMatch[index]) return;
            _earnedThisMatch[index] = true;
            _thisMatch.Add(index);
            if (_unlocked[index]) return;
            _unlocked[index] = true;
            _store.Set(All[index].Id);
            Unlocked?.Invoke(index);
        }
    }
}
