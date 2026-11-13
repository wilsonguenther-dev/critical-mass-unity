using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CriticalMass.Runtime.Flow
{
    /// <summary>Where the player is in the game flow (DG-01).</summary>
    public enum FlowState
    {
        /// <summary>The flow is not running (editor, tests, capture tools): Boot goes straight into a match.</summary>
        Off,
        Title,
        Menu,
        Setup,
        Match,
        Results,
    }

    /// <summary>What the player picked in match setup.</summary>
    [Serializable]
    public struct MatchSetupChoice
    {
        public TeamId Team;
        public ClassId Class;
        /// <summary>Players per team: 16 or 5.</summary>
        public int TeamSize;
        /// <summary>Sim seconds per wall second: 2 or 1.</summary>
        public double Pace;

        public static MatchSetupChoice Default => new MatchSetupChoice
        {
            Team = TeamId.Azure,
            Class = ClassId.Warrior,
            TeamSize = 16,
            Pace = 2,
        };
    }

    /// <summary>
    /// The match the next SimDriverBehaviour creates (GameBootstrap hands it to the driver before Awake). The world is
    /// created from it through the sim's own WorldOptions path (World.CreateWorld); nothing under Sim changes.
    /// </summary>
    public sealed class MatchRequest
    {
        public int Seed;
        public TeamId Team;
        public ClassId Class;
        public int TeamSize;
        public double Pace;
        /// <summary>true: the bot match behind the title and the menus (actor 0 on autopilot, never recorded).</summary>
        public bool Backdrop;

        public WorldOptions ToWorldOptions(GameConfig cfg) => new WorldOptions
        {
            Seed = Seed,
            TeamSize = World.ClampTeamSize(cfg, TeamSize),
            HumanTeam = Team,
            HumanClass = Class,
            Config = cfg,
        };

        public static MatchRequest From(MatchSetupChoice s, int seed) => new MatchRequest
        {
            Seed = seed,
            Team = s.Team,
            Class = s.Class,
            TeamSize = s.TeamSize,
            Pace = s.Pace > 0 ? s.Pace : 2,
            Backdrop = false,
        };
    }

    /// <summary>
    /// DG-01 game flow state, kept in statics across Boot reloads: title, main menu, match setup, match, results, play
    /// again. Every match (and every backdrop) gets a fresh seed; the world is created when Play is pressed, by reloading
    /// Boot with a MatchRequest (the same reload the pause menu's Restart and Quit to title already use), so every view,
    /// effect and HUD part is rebuilt for the new world with no rewiring.
    /// <para>Runs in player builds (TitleScreen.ShowOnBoot) or when ForceEnabled is set (tests, the flow capture tool).
    /// Off, Boot behaves exactly as before: one match from GameConfig defaults, seed 1337, no title.</para>
    /// </summary>
    public static class GameFlow
    {
        /// <summary>Tests and the capture tool run the flow in the editor.</summary>
        public static bool ForceEnabled;

        /// <summary>
        /// Autoplay (DG-05's -autoplay, or set by its component): the title's dismissal starts a match with the last setup
        /// straight away, and the results screen presses Play again by itself after AutoplayResultsSeconds.
        /// </summary>
        public static bool Autoplay;

        public const double AutoplayResultsSeconds = 5;
        /// <summary>Backdrop matches play at 1x behind the menus.</summary>
        public const double BackdropPace = 1;

        public static bool Enabled => ForceEnabled || TitleScreen.ShowOnBoot;

        public static FlowState State { get; private set; } = FlowState.Off;
        public static MatchSetupChoice LastSetup = MatchSetupChoice.Default;
        /// <summary>Seed of the last world created (match or backdrop).</summary>
        public static int LastSeed { get; private set; }
        /// <summary>Matches started this session (Play, Play again, Restart).</summary>
        public static int MatchesStarted { get; private set; }

        static MatchRequest _pending;
        static uint _seedCounter;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => ResetForTests();

        /// <summary>Back to first-boot state (domain reload is off: statics survive between play sessions).</summary>
        public static void ResetForTests()
        {
            State = FlowState.Off;
            LastSetup = MatchSetupChoice.Default;
            LastSeed = 0;
            MatchesStarted = 0;
            _pending = null;
            Autoplay = HasArg("-autoplay");
            SimDriverBehaviour.NextMatch = null;
        }

        static bool HasArg(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>A positive seed that differs from the previous one (time, a counter and a mix; never 0).</summary>
        public static int NewSeed()
        {
            int seed;
            do
            {
                unchecked
                {
                    _seedCounter++;
                    ulong x = (ulong)DateTime.UtcNow.Ticks ^ ((ulong)_seedCounter * 0x9E3779B97F4A7C15UL);
                    x ^= x >> 33;
                    x *= 0xFF51AFD7ED558CCDUL;
                    x ^= x >> 33;
                    seed = (int)(x & 0x7FFFFFFF);
                }
            } while (seed == 0 || seed == LastSeed);
            return seed;
        }

        /// <summary>
        /// Called by GameBootstrap.Build before it adds the driver: the request the new world is created from, or null
        /// (flow off: the config defaults). Takes over the pause menu's reload requests: Restart match starts the last
        /// setup again on a new seed, Quit to title goes to the title over a new backdrop.
        /// </summary>
        public static MatchRequest RequestForBuild()
        {
            if (!Enabled)
            {
                // Flow off (editor, tests): Boot builds the default match, unless the results screen of such a match
                // asked for Play again or the menu (then that request is honoured and the flow runs from there).
                if (_pending == null)
                {
                    State = FlowState.Off;
                    return null;
                }
            }
            else
            {
                PauseMenu.AfterReload after = PauseMenu.PendingAfterReload;
                if (after != PauseMenu.AfterReload.None)
                {
                    PauseMenu.PendingAfterReload = PauseMenu.AfterReload.None;
                    if (after == PauseMenu.AfterReload.Play) PrepareMatch(LastSetup);
                    else PrepareBackdrop(FlowState.Title);
                }
                if (_pending == null) PrepareBackdrop(FlowState.Title);   // first boot
            }
            MatchRequest r = _pending;
            _pending = null;
            LastSeed = r.Seed;
            return r;
        }

        /// <summary>Queues a match for the next Boot build without reloading (tests; BeginMatch = queue + reload).</summary>
        public static void QueueMatch(MatchSetupChoice s) => PrepareMatch(s);

        /// <summary>Queues a backdrop for screen (Title, Menu or Setup) without reloading (tests).</summary>
        public static void QueueBackdrop(FlowState screen) => PrepareBackdrop(screen);

        static void PrepareMatch(MatchSetupChoice s)
        {
            LastSetup = s;
            _pending = MatchRequest.From(s, NewSeed());
            MatchesStarted++;
            State = FlowState.Match;
        }

        static void PrepareBackdrop(FlowState screen)
        {
            _pending = new MatchRequest
            {
                Seed = NewSeed(),
                Team = TeamId.Azure,
                Class = ClassId.Warrior,
                TeamSize = 16,
                Pace = BackdropPace,
                Backdrop = true,
            };
            State = screen;
        }

        /// <summary>Play / Play again / Change class then Start: a new world with a new seed.</summary>
        public static void BeginMatch(MatchSetupChoice s)
        {
            PrepareMatch(s);
            Reload();
        }

        /// <summary>To the title or the main menu over a fresh backdrop match.</summary>
        public static void ReturnTo(FlowState screen)
        {
            PrepareBackdrop(screen == FlowState.Title ? FlowState.Title : FlowState.Menu);
            Reload();
        }

        /// <summary>The backdrop is about to end: a fresh one, staying on the same screen.</summary>
        public static void RenewBackdrop()
        {
            PrepareBackdrop(State == FlowState.Off ? FlowState.Title : State);
            Reload();
        }

        /// <summary>Screen changes inside one world (title, menu, setup, results); no reload.</summary>
        internal static void Enter(FlowState s) => State = s;

        static void Reload()
        {
            FlowDirector d = FlowDirector.Current;
            if (d != null) d.FadeThenLoad(GameBootstrap.BootScenePath);
            else SceneManager.LoadScene(GameBootstrap.BootScenePath);
        }
    }
}
