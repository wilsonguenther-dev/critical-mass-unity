using System;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Config;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CriticalMass.Runtime.Onboarding
{
    /// <summary>
    /// "Trial of the Basin" (DG-12), the guided first match: the public API the menus call. The game-flow item (DG-01) was
    /// not on origin when this was written, so the trial installs itself (TrialInstaller) and offers itself once on first
    /// launch in player builds, after the title screen. Menus call Start() for "How to play" (main menu and pause menu).
    /// </summary>
    public static class Trial
    {
        /// <summary>The step list the next Start uses (data: replace or edit rows to change the trial).</summary>
        public static TrialStep[] Steps = TrialSteps.Default;

        /// <summary>A guided match is running (its guide exists and has steps left).</summary>
        public static bool IsRunning => Guide != null && Guide.Runner != null && !Guide.Runner.Finished;

        /// <summary>The first-launch offer was answered or a trial was finished (saved between launches).</summary>
        public static bool IsDone => TrialProgress.Done;

        /// <summary>The guide of the current or last trial (null when none).</summary>
        public static TrialGuide Guide { get; private set; }

        /// <summary>A trial started.</summary>
        public static event Action Started;
        /// <summary>A step ended (Done or Skipped).</summary>
        public static event Action<TrialStep, TrialStepState> StepEnded;
        /// <summary>The trial ended: true = every runnable step completed, false = something was skipped.</summary>
        public static event Action<bool> Finished;

        /// <summary>
        /// DG-01 (results, menu): called once when the trial ends instead of the built-in "Enter: real match" hint. Leave
        /// null to keep the built-in ending.
        /// </summary>
        public static Action AfterTrial;

        /// <summary>
        /// Where the trial's information lines go ("[Trial] dig is off: ...", the passive-bots note): Debug.Log by default.
        /// Tests collect them instead, so LogAssert.NoUnexpectedReceived still catches everything else.
        /// </summary>
        public static Action<string> InfoLog = DefaultInfoLog;

        static void DefaultInfoLog(string line) => UnityEngine.Debug.Log(line);

        /// <summary>
        /// Starts the guided match now: 5v5 at 1x on the running game (TrialMatch), then the steps. Replayable at any time
        /// ("How to play"). team/cls: null = Azure Warrior (or the class the current world's human plays).
        /// </summary>
        public static bool Start(TeamId? team = null, ClassId? cls = null) => Start(team, cls, Steps, null);

        /// <summary>
        /// Creator acceptance: starts a guided match with its own step list (the CREATOR'S TOUR) and an optional hook that
        /// edits the new match's request and config before the restart (passive bots, the tour's egg rules).
        /// </summary>
        public static bool Start(TeamId? team, ClassId? cls, TrialStep[] steps, Action<GameConfig, TrialMatchRequest> tune)
        {
            SimDriverBehaviour driver = UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();
            if (driver == null || driver.World == null) return false;
            GameObject host = HostOf(driver);
            if (TrialOffer.Current != null) UnityEngine.Object.Destroy(TrialOffer.Current);
            if (Guide != null) UnityEngine.Object.Destroy(Guide);

            HumanInputSource input = host.GetComponent<HumanInputSource>();
            if (input != null && input.State.Paused) input.State.ButtonDown(HumanInputState.HumanButton.Pause);   // resume

            TeamId t = team ?? driver.World.Opts.HumanTeam;
            ClassId c = cls ?? driver.World.Opts.HumanClass;
            TrialMatchRequest req = TrialMatchRequest.Trial(driver.Config, t, c);
            tune?.Invoke(driver.Config, req);
            TrialMatch.Start(driver, req);

            TrialGuide g = host.AddComponent<TrialGuide>();
            g.Driver = driver;
            Guide = g;
            g.Begin(steps ?? Steps);
            Started?.Invoke();
            return true;
        }

        /// <summary>Ends the guided match (no done flag change) and takes the mascot away.</summary>
        public static void Stop()
        {
            if (Guide != null) UnityEngine.Object.Destroy(Guide);
            Guide = null;
            MascotGuide m = MascotGuide.Instance;
            if (m != null) m.Hide();
        }

        /// <summary>Shows the first-launch question now (the menu can call it; the installer calls it once).</summary>
        /// <summary>A guided match asked for from the menus; the next Boot build's match starts it (TrialBoot).</summary>
        internal static bool PendingStart;
        /// <summary>Creator acceptance: the pending start is the CREATOR'S TOUR (CreatorsTour.Begin), not the first match.</summary>
        internal static bool PendingTour;

        /// <summary>
        /// "How to play" in the main menu and the pause menu (DG-01's [TutorialStart]). In a match it starts in place; from the
        /// title, menus or results (a backdrop world) it asks the flow for a new 5 v 5 match at 1x and starts there, so the
        /// trial never runs on the backdrop behind the menus. The player's last setup is kept for Play again.
        /// </summary>
        [TutorialStart]
        public static void StartFromMenu()
        {
            FlowState st = GameFlow.State;
            if (GameFlow.Enabled && st != FlowState.Match && st != FlowState.Off)
            {
                MatchSetupChoice keep = GameFlow.LastSetup;
                PendingStart = true;
                GameFlow.BeginMatch(new MatchSetupChoice { Team = keep.Team, Class = keep.Class, TeamSize = 5, Pace = 1 });
                GameFlow.LastSetup = keep;
                return;
            }
            Start();
        }

        public static TrialOffer ShowOffer()
        {
            SimDriverBehaviour driver = UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();
            return driver == null ? null : TrialOffer.Show(HostOf(driver));
        }

        /// <summary>After the trial: a normal match (the config's size and pace, a new seed), mascot away.</summary>
        public static void StartRealMatch()
        {
            SimDriverBehaviour driver = UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();
            Stop();
            if (driver == null || driver.World == null) return;
            int seed = unchecked(driver.World.Opts.Seed + 1);
            TrialMatch.Start(driver, TrialMatchRequest.Normal(driver.Config, driver.World.Opts.HumanTeam, driver.World.Opts.HumanClass, seed));
        }

        internal static void RaiseStepEnded(TrialStep s, TrialStepState how) => StepEnded?.Invoke(s, how);
        internal static void RaiseFinished(bool all) => Finished?.Invoke(all);

        /// <summary>Components go on the HumanInputSource object (as the pause menu's), else on the driver's.</summary>
        static GameObject HostOf(SimDriverBehaviour driver)
        {
            HumanInputSource input = UnityEngine.Object.FindAnyObjectByType<HumanInputSource>();
            return input != null ? input.gameObject : driver.gameObject;
        }
    }

    /// <summary>
    /// Waits on the game object for the title screen to go, then shows the first-launch offer once (player builds only,
    /// as the title screen: never in the editor, batch mode or tests, so no gate or test sees it uninvited).
    /// </summary>
    [Flow.GameOverlay(Order = 60)]   // installed by GameBootstrap through the DG-01 overlay registry
    public sealed class TrialBoot : MonoBehaviour
    {
        /// <summary>The offer is shown on boot (player builds, done flag not set).</summary>
        public static bool OfferOnBoot => !Application.isEditor && !Application.isBatchMode && !GameFlow.Autoplay && !TrialProgress.Done;

        float _readyAt = -1f;

        void Update()
        {
            // A guided match asked for from the menus starts once the new match's world exists.
            if (Trial.PendingStart && GameFlow.State == FlowState.Match)
            {
                SimDriverBehaviour d = GetComponent<SimDriverBehaviour>();
                if (d == null || d.World == null) return;
                Trial.PendingStart = false;
                if (Trial.PendingTour)
                {
                    Trial.PendingTour = false;
                    CreatorsTour.Begin();
                }
                else Trial.Start();
                enabled = false;
                return;
            }
            // Sequencing (DG-01): story intro, then the title, then this card, which comes up in place of the main menu the
            // first time the title is dismissed. Never over a match, setup or results.
            if (GameFlow.Enabled && GameFlow.State != FlowState.Title && GameFlow.State != FlowState.Menu)
            {
                enabled = false;
                return;
            }
            if (!OfferOnBoot || Trial.IsRunning || TrialOffer.Current != null)
            {
                enabled = false;
                return;
            }
            if (GetComponent<TitleScreen>() != null)
            {
                _readyAt = -1f;
                return;
            }
            // A short beat after the title goes, so the dismissing key does not land on the offer.
            if (_readyAt < 0f) _readyAt = Time.unscaledTime + 0.25f;
            if (Time.unscaledTime < _readyAt) return;
            enabled = false;
            // The card stands in for the main menu while it is up (its Skip brings the menu back), so the two never show at once.
            if (Trial.ShowOffer() != null && GameFlow.State == FlowState.Menu && FlowDirector.Current != null)
                FlowDirector.Current.MainMenu.Hide();
        }
    }

    /// <summary>
    /// Attaches TrialBoot to the game GameBootstrap builds, on boot and after every scene load, without editing the
    /// bootstrap (the pause menu's MenuInstaller pattern; GameBootstrap.Build is idempotent).
    /// </summary>
    public static class TrialInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            // Installed by the DG-01 overlay registry ([GameOverlay] on TrialBoot); PendingStart never survives a play session.
            Trial.PendingStart = false;
            Trial.PendingTour = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach(scene);

        /// <summary>Adds TrialBoot to the game's HumanInputSource object; returns it (null: no game).</summary>
        public static TrialBoot Attach(Scene scene)
        {
            GameBootstrap.Build(scene);
            HumanInputSource input = UnityEngine.Object.FindAnyObjectByType<HumanInputSource>();
            if (input == null) return null;
            GameObject go = input.gameObject;
            TrialBoot boot = go.GetComponent<TrialBoot>();
            if (boot == null) boot = go.AddComponent<TrialBoot>();
            return boot;
        }
    }
}
