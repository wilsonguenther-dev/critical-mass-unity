using CriticalMass.Runtime.Menus;
using CriticalMass.Runtime.Onboarding;
using CriticalMass.Runtime.Spectator;
using CriticalMass.Sim.Config;
using UnityEngine;

namespace CriticalMass.Runtime.Flow
{
    /// <summary>
    /// The last match setup, kept between launches in PlayerPrefs (team, class, size, enemy bots, speed). Shared by match
    /// setup (reads it on first show, writes it on START MATCH) and Play Now (reads it without opening setup). Speed is
    /// stored as a MatchSpeedChoice under its own key; the old 1-or-2 pace key is no longer read, so everyone who never
    /// chose a speed lands on the new default (Standard).
    /// </summary>
    public static class SetupPrefs
    {
        public const string TeamKey = "cm.flow.setup.team", ClassKey = "cm.flow.setup.class", SizeKey = "cm.flow.setup.size",
            BotsKey = "cm.flow.setup.bots", SpeedKey = "cm.flow.setup.speed";

        /// <summary>Every key this class writes (tests back these up and restore them).</summary>
        public static readonly string[] AllKeys = { TeamKey, ClassKey, SizeKey, BotsKey, SpeedKey };

        static bool _loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => _loaded = false;

        /// <summary>Tests: forget that the prefs were read this session.</summary>
        public static void ResetForTests() => _loaded = false;

        /// <summary>The saved setup (defaults where nothing is saved: Azure Warrior, 16 v 16, Normal bots, default speed).</summary>
        public static MatchSetupChoice Load()
        {
            MatchSetupChoice d = MatchSetupChoice.Default;
            int speed = PlayerPrefs.GetInt(SpeedKey, (int)MatchSpeed.DefaultChoice);
            if (speed < 0 || speed > 2) speed = (int)MatchSpeed.DefaultChoice;
            return new MatchSetupChoice
            {
                Team = PlayerPrefs.GetInt(TeamKey, (int)d.Team) == 1 ? TeamId.Crimson : TeamId.Azure,
                Class = (ClassId)Mathf.Clamp(PlayerPrefs.GetInt(ClassKey, (int)d.Class), 0, ClassIds.All.Count - 1),
                BotDifficulty = (CriticalMass.Sim.Core.BotDifficulty)Mathf.Clamp(PlayerPrefs.GetInt(BotsKey, (int)d.BotDifficulty), 0, 2),
                TeamSize = PlayerPrefs.GetInt(SizeKey, d.TeamSize) <= 5 ? 5 : 16,
                Pace = MatchSpeed.PaceOf((MatchSpeedChoice)speed),
            };
        }

        public static void Save(MatchSetupChoice c)
        {
            PlayerPrefs.SetInt(TeamKey, (int)c.Team);
            PlayerPrefs.SetInt(ClassKey, (int)c.Class);
            PlayerPrefs.SetInt(SizeKey, c.TeamSize <= 5 ? 5 : 16);
            PlayerPrefs.SetInt(BotsKey, (int)c.BotDifficulty);
            PlayerPrefs.SetInt(SpeedKey, (int)MatchSpeed.FromPace(c.Pace));
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Before the first match of a session GameFlow.LastSetup is loaded from the prefs once; after that LastSetup (what
        /// was last played) is the truth. Returns GameFlow.LastSetup.
        /// </summary>
        public static MatchSetupChoice EnsureLoaded()
        {
            if (!_loaded && GameFlow.MatchesStarted == 0)
            {
                _loaded = true;
                GameFlow.LastSetup = Load();
            }
            return GameFlow.LastSetup;
        }
    }

    /// <summary>
    /// PLAY NOW (owner, 2026-11-12: "I want to be able to drop into a game"): the first, focused button on the title and
    /// the main menu. One press starts a match with the last class (Warrior the first time) and the last settings, no setup
    /// screens, the short loading screen, and the human in control from the first frame: never spectating
    /// (SpectatorMode.SpectateNextMatch cleared), never the guided match (Trial.PendingStart cleared), never a backdrop
    /// request (so SimDriverBehaviour does not put actor 0 on autopilot), and PlayerPresence's idle autopilot clock does not
    /// run behind the loading screen. On the very first launch the title's Play Now shows the guided-match offer first;
    /// its Skip (or Escape / B) then drops straight into the match.
    /// </summary>
    public static class PlayNow
    {
        public const string Label = "PLAY NOW";

        /// <summary>The first-launch offer was raised by Play Now: its Skip starts the Play Now match.</summary>
        public static bool OfferThenPlay { get; private set; }

        /// <summary>Raised when a Play Now match is queued (the choice it uses).</summary>
        public static event System.Action<MatchSetupChoice> Started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            OfferThenPlay = false;
            Started = null;
        }

        /// <summary>The setup Play Now uses: the last one played or saved, with a playable class and an offered speed.</summary>
        public static MatchSetupChoice Choice()
        {
            MatchSetupChoice c = SetupPrefs.EnsureLoaded();
            if (!MatchSetup.Playable.Contains(c.Class)) c.Class = ClassId.Warrior;
            if (c.TeamSize != 5) c.TeamSize = 16;
            c.Pace = c.Pace > 0 ? MatchSpeed.Snap(c.Pace) : MatchSpeed.DefaultPace;
            return c;
        }

        /// <summary>The request Play Now would build for a seed (tests; the same path GameFlow.BeginMatch takes).</summary>
        public static MatchRequest Request(int seed) => MatchRequest.From(Choice(), seed);

        /// <summary>The guided-match offer should come first (the very first launch in a player build).</summary>
        public static bool OfferFirst => TrialBoot.OfferOnBoot && !Trial.IsRunning;

        /// <summary>
        /// The title's Play Now. First launch: to the main menu state, where TrialBoot puts the offer up in its place, and the
        /// offer's Skip comes back here. Otherwise straight into the match.
        /// </summary>
        public static void FromTitle()
        {
            if (OfferFirst && FlowDirector.Current != null && GameFlow.State == FlowState.Title)
            {
                OfferThenPlay = true;
                FlowDirector.Current.ShowMenu(OverlayRegistry.PlayNow);
                return;
            }
            Go();
        }

        /// <summary>The offer was answered: true when its Skip should start the Play Now match (clears the flag).</summary>
        public static bool ConsumeOfferThenPlay()
        {
            bool b = OfferThenPlay;
            OfferThenPlay = false;
            return b;
        }

        /// <summary>Queues the Play Now match and reloads into it (with the short loading screen). false: the flow is off.</summary>
        public static bool Go()
        {
            OfferThenPlay = false;
            if (!GameFlow.Enabled) return false;
            MatchSetupChoice c = Prepare();
            GameFlow.BeginMatch(c, true);
            return true;
        }

        /// <summary>Everything Go does before the reload (tests call this and then GameFlow.RequestForBuild).</summary>
        public static MatchSetupChoice Prepare()
        {
            SpectatorMode.SpectateNextMatch = false;
            Trial.PendingStart = false;
            MatchSetupChoice c = Choice();
            ClassSelect.SelectedClass = c.Class;   // DG-10 reads it for the respawn strip and the pause menu
            Started?.Invoke(c);
            return c;
        }
    }
}
