using System;
using System.Reflection;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.Onboarding
{
    /// <summary>
    /// The match a guided trial (or the real match after it) is played in. These are match settings, nothing more: team
    /// size and pace are the ones GameConfig already offers (TeamSizeOptions { 5, 16 }, Debug.ClockSpeedOptions, the
    /// browser 5v5 at 1x preset GameConfig.CreateBrowser5v5), applied through WorldOptions and the driver's TimeScale.
    /// </summary>
    public sealed class TrialMatchRequest
    {
        public int Seed;
        public int TeamSize;
        /// <summary>Sim seconds per wall second (SimDriverBehaviour.TimeScale).</summary>
        public double TimeScale;
        public TeamId HumanTeam;
        public ClassId HumanClass;
        /// <summary>Sim seconds at the start in which bots do not attack (0 = normal bots).</summary>
        public double PassiveBotSeconds;

        /// <summary>The guided match: 5v5 (the smallest team size the config offers) at 1x, passive bots for 30 s.</summary>
        public static TrialMatchRequest Trial(GameConfig cfg, TeamId team, ClassId cls)
        {
            GameConfig preset = GameConfig.CreateBrowser5v5();
            int size = preset.TeamSize;
            if (cfg.TeamSizeOptions != null && cfg.TeamSizeOptions.Length > 0)
            {
                size = cfg.TeamSizeOptions[0];
                for (int i = 1; i < cfg.TeamSizeOptions.Length; i++) size = Math.Min(size, cfg.TeamSizeOptions[i]);
            }
            return new TrialMatchRequest
            {
                Seed = cfg.Seed,
                TeamSize = size,
                TimeScale = preset.DefaultSpeed,
                HumanTeam = team,
                HumanClass = cls,
                PassiveBotSeconds = 30,
            };
        }

        /// <summary>A normal match: the config's own team size and pace (16v16 at 2x, U-004), a new seed.</summary>
        public static TrialMatchRequest Normal(GameConfig cfg, TeamId team, ClassId cls, int seed) => new TrialMatchRequest
        {
            Seed = seed,
            TeamSize = cfg.TeamSize,
            TimeScale = cfg.DefaultSpeed,
            HumanTeam = team,
            HumanClass = cls,
            PassiveBotSeconds = 0,
        };
    }

    /// <summary>
    /// Starts a match from a TrialMatchRequest. The game-flow item (DG-01, branch feat/dg-01-game-flow: menus, match setup)
    /// was not on origin when this was written, so this carries a fallback and a hook: DG-01 sets Starter to its own
    /// match-setup call and the guided match goes through it; until then the fallback restarts the running world in place
    /// with the sim's own World.ResetWorld (the restart the ported tests use; arrays are not reallocated, Epoch bumps, the
    /// camera snaps, bots reset) and sets the driver's TimeScale. No sim file changes.
    /// </summary>
    public static class TrialMatch
    {
        /// <summary>DG-01's match setup. Return true when it started the match; false falls back to the in-place restart.</summary>
        public static Func<SimDriverBehaviour, TrialMatchRequest, bool> Starter;

        /// <summary>The last log line about passive bots (tests read it).</summary>
        public static string PassiveBotsNote { get; private set; } = string.Empty;

        public static void Start(SimDriverBehaviour driver, TrialMatchRequest req, Action<string> log = null)
        {
            if (driver == null || req == null || driver.World == null) return;
            if (log == null) log = s => (Trial.InfoLog ?? UnityEngine.Debug.Log)(s);
            ApplyPassiveBots(driver.Config, req.PassiveBotSeconds, log);
            if (Starter != null && Starter(driver, req)) return;
            Restart(driver.World, req);
            driver.TimeScale = req.TimeScale;
            global::CriticalMass.Runtime.Flow.MatchSpeed.DisplayPace = req.TimeScale;   // timers in real seconds at this pace
            HumanInputSource input = driver.GetComponent<HumanInputSource>();
            // Clicks and keys pressed on the offer panel must not reach the first tick of the new match.
            if (input != null && !input.State.Paused) input.State.Reset();
        }

        /// <summary>The in-place restart (also what tests use on a headless world).</summary>
        public static void Restart(World w, TrialMatchRequest req)
        {
            World.ResetWorld(w, new WorldOptions
            {
                Seed = req.Seed,
                TeamSize = req.TeamSize,
                HumanTeam = req.HumanTeam,
                HumanClass = req.HumanClass,
                Config = w.Config,
            });
        }

        /// <summary>
        /// Passive bots are a match setting the sim does not offer on main: BotsConfig has no passivity field and the bots
        /// read nothing else the view could set. When a rules-lane item adds BotsConfig.PassiveSeconds (double, sim
        /// seconds from the match start), it is set here before the restart; otherwise one log line says the trial runs
        /// with normal bots.
        /// </summary>
        public static void ApplyPassiveBots(GameConfig cfg, double seconds, Action<string> log)
        {
            FieldInfo f = TrialFeatures.PassiveBotsSetting();
            if (f != null)
            {
                f.SetValue(cfg.Bots, seconds);
                PassiveBotsNote = "[Trial] passive bots for the first " + seconds + " s (BotsConfig." + TrialFeatures.PassiveBotsField + ")";
                log(PassiveBotsNote);
                return;
            }
            if (seconds <= 0)
            {
                PassiveBotsNote = string.Empty;
                return;
            }
            PassiveBotsNote = "[Trial] passive bots for the first " + seconds + " s are off: the sim exposes no bot-passivity setting " +
                "(no BotsConfig." + TrialFeatures.PassiveBotsField + "); the trial runs with normal bots until a rules-lane item adds it";
            log(PassiveBotsNote);
        }
    }
}
