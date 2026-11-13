using System;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using UnityEngine;

namespace CriticalMass.Runtime.Onboarding
{
    /// <summary>
    /// CREATOR'S TOUR (creator acceptance, 2026-11-13): the creator's four demo needs and his two pitch points in about two
    /// minutes. The player is a Miner on the Azure side of a 5 v 5 at 1x whose bots never target him for the whole tour.
    /// The guided-match machinery (Trial, TrialGuide, TrialChecks) runs four short prompts with a world arrow each:
    /// <list type="number">
    /// <item>dig a block and build new ground with it at the edge (an arrow on the nearest edge);</item>
    /// <item>knock the stone pillar by the base down: from the bridge side it falls into a ramp, from the side into a wall;</item>
    /// <item>cross the bridge holding two keys (diagonal), and jump a one-cell gap;</item>
    /// <item>bring corruption to the egg: under the tour's egg rule one load locks berserk and the egg hatches at 2:30.</item>
    /// </list>
    /// Entry: Home, "CREATOR'S TOUR" (a main-menu line, MenuEntry "tour"), or CreatorsTour.StartFromMenu from anywhere. The
    /// tour only changes the new match's own config before its restart (passive bots, berserk line, hatch time); nothing
    /// under Sim changes and no sim state is written.
    /// </summary>
    public static class CreatorsTour
    {
        public const string MenuId = "tour", MenuLabel = "CREATOR'S TOUR";
        /// <summary>Bots never target the touring player for this long (sim seconds).</summary>
        public const double PassiveSeconds = 900;
        /// <summary>The tour's egg: one Miner load of corruption (9 instability) locks berserk.</summary>
        public const double TourBerserkThreshold = 9;
        /// <summary>The tour's egg hatches at 2:30 of match time, so the berserk outcome lands inside the tour.</summary>
        public const double TourHatchSeconds = 150;
        public const double TourMatchSeconds = 360;

        public static readonly TrialStep[] Steps =
        {
            new TrialStep
            {
                Id = "tour-dig", Prompt = "1/4  {DigPlace} digs a block. At the edge, {DigPlace} builds new ground.",
                Goal = TrialGoal.Place, Target = TrialTarget.Gap, Param = 3.5, OnlyClass = ClassId.Miner, Needs = TrialFeature.Terraform,
            },
            new TrialStep
            {
                Id = "tour-topple", Prompt = "2/4  {Attack} the pillar. Bridge side: a ramp. Other side: a wall.",
                Goal = TrialGoal.Topple, Target = TrialTarget.Pillar,
            },
            new TrialStep
            {
                Id = "tour-bridge", Prompt = "3/4  Cross the bridge holding two keys. {Jump} clears a one-cell gap.",
                Goal = TrialGoal.CrossBridge, Target = TrialTarget.Bridge, Param = 6,
            },
            new TrialStep
            {
                Id = "tour-egg", Prompt = "4/4  Corruption to the egg: one load here and it hatches berserk.",
                Goal = TrialGoal.ReachTarget, Target = TrialTarget.Shrine, Param = 4.5,
            },
        };

        /// <summary>True while the running guided match is the tour.</summary>
        public static bool IsRunning { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            IsRunning = false;
            Trial.Finished -= OnFinished;
        }

        /// <summary>The tour's own match rules, written into the new match's config and request before its restart.</summary>
        public static void Tune(GameConfig cfg, TrialMatchRequest req)
        {
            if (req != null)
            {
                req.PassiveBotSeconds = PassiveSeconds;
                req.HumanClass = ClassId.Miner;
            }
            if (cfg == null) return;
            cfg.MatchSeconds = Math.Max(cfg.MatchSeconds, TourMatchSeconds);
            cfg.HatchSeconds = Math.Min(TourHatchSeconds, cfg.MatchSeconds);
            if (cfg.KaijuFeed != null)
            {
                cfg.KaijuFeed.RandomBerserk = false;
                cfg.KaijuFeed.BerserkThreshold = TourBerserkThreshold;
            }
        }

        /// <summary>Starts the tour on the running game (in a match). False without a game.</summary>
        public static bool Begin()
        {
            bool ok = Trial.Start(null, ClassId.Miner, Steps, Tune);
            IsRunning = ok;
            Trial.Finished -= OnFinished;
            if (ok) Trial.Finished += OnFinished;
            return ok;
        }

        static void OnFinished(bool all)
        {
            IsRunning = false;
            Trial.Finished -= OnFinished;
        }

        /// <summary>
        /// Home's "CREATOR'S TOUR": from the menus a new 5 v 5 Miner match at 1x is started and the tour begins once its
        /// world exists (TrialBoot); inside a match it starts in place. The player's last setup is kept for Play again.
        /// </summary>
        public static void StartFromMenu()
        {
            FlowState st = GameFlow.State;
            if (GameFlow.Enabled && st != FlowState.Match && st != FlowState.Off)
            {
                MatchSetupChoice keep = GameFlow.LastSetup;
                Trial.PendingStart = true;
                Trial.PendingTour = true;
                GameFlow.BeginMatch(new MatchSetupChoice { Team = keep.Team, Class = ClassId.Miner, TeamSize = 5, Pace = 1, Mode = MatchMode.Training });
                GameFlow.LastSetup = keep;
                return;
            }
            Begin();
        }
    }

    /// <summary>The main-menu line "CREATOR'S TOUR" (DG-01 registry): choosing it starts the tour.</summary>
    [MenuEntry(CreatorsTour.MenuId, CreatorsTour.MenuLabel, 22)]
    public sealed class CreatorsTourMenuScreen : MonoBehaviour, IMenuScreen
    {
        public void Open(Action onClosed)
        {
            CreatorsTour.StartFromMenu();
            // The match reload replaces the menus; if no match could start, give the menu back.
            if (!Trial.PendingStart && !Trial.IsRunning) onClosed?.Invoke();
        }
    }
}
