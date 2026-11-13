using System.Collections.Generic;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Menus;
using CriticalMass.Runtime.Onboarding;
using CriticalMass.Runtime.Spectator;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// Play Now (one press into a match: last class, last settings, the human in control) and the Standard / Fast / Relaxed
    /// speed choice (mapping, default from the sim config, persistence, timers in real seconds). No Play Mode.
    /// </summary>
    [TestFixture]
    public sealed class PlayNowSpeedTests
    {
        readonly Dictionary<string, int?> _saved = new Dictionary<string, int?>();
        ClassId _savedClass;
        bool _savedSpectate;

        [SetUp]
        public void SetUp()
        {
            _saved.Clear();
            foreach (string k in SetupPrefs.AllKeys)
            {
                _saved[k] = PlayerPrefs.HasKey(k) ? PlayerPrefs.GetInt(k) : (int?)null;
                PlayerPrefs.DeleteKey(k);
            }
            _savedClass = ClassSelect.SelectedClass;
            _savedSpectate = SpectatorMode.SpectateNextMatch;
            GameFlow.ResetForTests();
            GameFlow.ForceEnabled = true;
            PauseMenu.PendingAfterReload = PauseMenu.AfterReload.None;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (KeyValuePair<string, int?> kv in _saved)
            {
                if (kv.Value.HasValue) PlayerPrefs.SetInt(kv.Key, kv.Value.Value);
                else PlayerPrefs.DeleteKey(kv.Key);
            }
            PlayerPrefs.Save();
            ClassSelect.SelectedClass = _savedClass;
            SpectatorMode.SpectateNextMatch = _savedSpectate;
            GameFlow.ResetForTests();
            GameFlow.ForceEnabled = false;
            PauseMenu.PendingAfterReload = PauseMenu.AfterReload.None;
            MatchSpeed.DisplayPace = 1;
        }

        static readonly MatchSetupChoice CrimsonMinerFast = new MatchSetupChoice
        {
            Team = TeamId.Crimson, Class = ClassId.Miner, TeamSize = 5, Pace = MatchSpeed.FastPace, BotDifficulty = BotDifficulty.Hard,
        };

        // ---- Play Now request ----

        [Test]
        public void PlayNowTheFirstTimeIsAnAzureWarriorAtTheDefaultSpeed()
        {
            MatchSetupChoice c = PlayNow.Choice();
            Assert.AreEqual(ClassId.Warrior, c.Class, "Warrior the first time");
            Assert.AreEqual(TeamId.Azure, c.Team);
            Assert.AreEqual(16, c.TeamSize);
            Assert.AreEqual(BotDifficulty.Normal, c.BotDifficulty);
            Assert.AreEqual(MatchSpeed.DefaultPace, c.Pace, 1e-9);
            if (MatchSpeed.ConfigDefaultSpeed >= 2) Assert.AreEqual(MatchSpeed.StandardPace, c.Pace, 1e-9, "sim still says 2x: the view defaults to Standard");
        }

        [Test]
        public void PlayNowUsesTheLastSavedClassAndSettings()
        {
            SetupPrefs.Save(CrimsonMinerFast);
            GameFlow.ResetForTests();   // a new launch: the prefs are read again

            MatchRequest r = PlayNow.Request(4242);
            Assert.IsFalse(r.Backdrop, "a real match, never the menus' backdrop");
            Assert.AreEqual(4242, r.Seed);
            Assert.AreEqual(TeamId.Crimson, r.Team);
            Assert.AreEqual(ClassId.Miner, r.Class);
            Assert.AreEqual(5, r.TeamSize);
            Assert.AreEqual(MatchSpeed.FastPace, r.Pace, 1e-9);
            Assert.AreEqual(BotDifficulty.Hard, r.BotDifficulty);

            WorldOptions o = r.ToWorldOptions(GameConfig.CreateDefault());
            Assert.AreEqual(ClassId.Miner, o.HumanClass);
            Assert.AreEqual(TeamId.Crimson, o.HumanTeam);
            Assert.AreEqual(MatchSpeed.FastPace, o.Pace, 1e-9, "the speed choice reaches the sim pace too (#61 WorldOptions.Pace)");
        }

        [Test]
        public void EverySpeedChoiceSetsBothTheTimeScaleAndTheSimPace()
        {
            foreach (MatchSpeedChoice c in MatchSpeed.Order)
            {
                var setup = MatchSetupChoice.Default;
                setup.Pace = MatchSpeed.PaceOf(c);
                MatchRequest r = MatchRequest.From(setup, 7);
                WorldOptions o = r.ToWorldOptions(GameConfig.CreateDefault());
                Assert.AreEqual(MatchSpeed.PaceOf(c), r.Pace, 1e-9, c + ": driver time scale (MatchRequest.Pace)");
                Assert.AreEqual(MatchSpeed.PaceOf(c), o.Pace, 1e-9, c + ": WorldOptions.Pace");
                World w = World.CreateWorld(o);
                Assert.AreEqual(MatchSpeed.PaceOf(c), w.Dur.Pace, 1e-9, c + ": the world's durations use the same pace");
            }
        }

        [Test]
        public void PlayNowUsesTheSetupPlayedLastThisSession()
        {
            MatchSetupChoice played = new MatchSetupChoice
            {
                Team = TeamId.Azure, Class = ClassId.Farmer, TeamSize = 16, Pace = MatchSpeed.RelaxedPace, BotDifficulty = BotDifficulty.Easy,
            };
            GameFlow.QueueMatch(played);
            GameFlow.RequestForBuild();
            MatchSetupChoice c = PlayNow.Choice();
            Assert.AreEqual(ClassId.Farmer, c.Class);
            Assert.AreEqual(MatchSpeed.RelaxedPace, c.Pace, 1e-9);
            Assert.AreEqual(BotDifficulty.Easy, c.BotDifficulty);
        }

        [Test]
        public void PlayNowHandsControlToTheHuman()
        {
            SpectatorMode.SpectateNextMatch = true;   // left over from a Spectate pick
            MatchSetupChoice c = PlayNow.Prepare();
            Assert.IsFalse(SpectatorMode.SpectateNextMatch, "Play Now never starts in spectator mode");
            Assert.IsFalse(PlayNow.OfferThenPlay);
            Assert.IsFalse(TrialBoot.OfferOnBoot, "the guided-match offer is player builds only, first launch only");

            GameFlow.QueueMatch(c);
            MatchRequest r = GameFlow.RequestForBuild();
            Assert.AreEqual(FlowState.Match, GameFlow.State);
            Assert.IsFalse(r.Backdrop, "only a backdrop request puts actor 0 on autopilot at match start");
            World w = World.CreateWorld(r.ToWorldOptions(GameConfig.CreateDefault()));
            Assert.IsTrue(w.Actors[0].IsHuman);
            Assert.AreEqual(ClassId.Warrior, w.Actors[0].Cls);
        }

        [Test]
        public void PlayNowFallsBackToWarriorWhenTheSavedClassIsNotPlayable()
        {
            PlayerPrefs.SetInt(SetupPrefs.ClassKey, 99);   // clamped to the last class, which must be playable or Warrior
            GameFlow.ResetForTests();
            MatchSetupChoice c = PlayNow.Choice();
            Assert.IsTrue(MatchSetup.Playable.Contains(c.Class));
        }

        [Test]
        public void PlayNowIsTheFirstMenuLine()
        {
            List<MenuEntry> e = OverlayRegistry.MenuEntries(System.Array.Empty<System.Type>());
            Assert.AreEqual(OverlayRegistry.PlayNow, e[0].Id);
            Assert.AreEqual(PlayNow.Label, e[0].Label);
            Assert.IsTrue(e[0].Available);
        }

        // ---- speed mapping ----

        [Test]
        public void SpeedsMapToPaces()
        {
            Assert.AreEqual(0.75, MatchSpeed.PaceOf(MatchSpeedChoice.Relaxed), 1e-9);
            Assert.AreEqual(1.0, MatchSpeed.PaceOf(MatchSpeedChoice.Standard), 1e-9);
            Assert.AreEqual(2.0, MatchSpeed.PaceOf(MatchSpeedChoice.Fast), 1e-9);
            Assert.AreEqual(MatchSpeedChoice.Relaxed, MatchSpeed.FromPace(0.75));
            Assert.AreEqual(MatchSpeedChoice.Relaxed, MatchSpeed.FromPace(0.5));
            Assert.AreEqual(MatchSpeedChoice.Standard, MatchSpeed.FromPace(1));
            Assert.AreEqual(MatchSpeedChoice.Standard, MatchSpeed.FromPace(1.4));
            Assert.AreEqual(MatchSpeedChoice.Fast, MatchSpeed.FromPace(2));
            Assert.AreEqual(MatchSpeedChoice.Fast, MatchSpeed.FromPace(3));
            Assert.AreEqual(MatchSpeed.DefaultChoice, MatchSpeed.FromPace(0));
            Assert.AreEqual("STANDARD", MatchSpeed.Label(MatchSpeedChoice.Standard));
            Assert.AreEqual("FAST", MatchSpeed.Label(MatchSpeedChoice.Fast));
            Assert.AreEqual("RELAXED", MatchSpeed.Label(MatchSpeedChoice.Relaxed));
            Assert.AreEqual("STANDARD SPEED   ·   1X", MatchSpeed.LoadingLine(1));
            Assert.AreEqual("RELAXED SPEED   ·   0.75X", MatchSpeed.LoadingLine(0.75));
        }

        [Test]
        public void TheDefaultComesFromTheSimConfigAndIsStandardWhileItIsStill2x()
        {
            Assert.AreEqual(MatchSpeedChoice.Standard, MatchSpeed.DefaultFor(2), "old 2x constant: the view says Standard");
            Assert.AreEqual(MatchSpeedChoice.Standard, MatchSpeed.DefaultFor(1), "the rules change to 1x");
            Assert.AreEqual(MatchSpeedChoice.Relaxed, MatchSpeed.DefaultFor(0.75));
            Assert.AreEqual(GameConfig.CreateDefault().DefaultSpeed, MatchSpeed.ConfigDefaultSpeed, 1e-9);
            Assert.AreEqual(MatchSpeed.DefaultPace, MatchSetupChoice.Default.Pace, 1e-9);
            MatchSetupChoice noPace = MatchSetupChoice.Default;
            noPace.Pace = 0;
            Assert.AreEqual(MatchSpeed.DefaultPace, MatchRequest.From(noPace, 7).Pace, 1e-9);
        }

        [Test]
        public void TheSpeedStepperWrapsThroughAllThree()
        {
            Assert.AreEqual(MatchSpeedChoice.Fast, MatchSpeed.Step(MatchSpeedChoice.Standard, 1));
            Assert.AreEqual(MatchSpeedChoice.Relaxed, MatchSpeed.Step(MatchSpeedChoice.Fast, 1));
            Assert.AreEqual(MatchSpeedChoice.Standard, MatchSpeed.Step(MatchSpeedChoice.Relaxed, 1));
            Assert.AreEqual(MatchSpeedChoice.Fast, MatchSpeed.Step(MatchSpeedChoice.Relaxed, -1));
            Assert.AreEqual(MatchSpeedChoice.Relaxed, MatchSpeed.Step(MatchSpeedChoice.Standard, -1));
        }

        [Test]
        public void RelaxedReachesTheDriverUnchanged()
        {
            MatchSetupChoice c = MatchSetupChoice.Default;
            c.Pace = MatchSpeed.RelaxedPace;
            Assert.AreEqual(0.75, MatchRequest.From(c, 3).Pace, 1e-9, "the request's pace is the driver's TimeScale");
        }

        // ---- persistence ----

        [Test]
        public void TheChosenSpeedIsRemembered()
        {
            foreach (MatchSpeedChoice s in MatchSpeed.Order)
            {
                MatchSetupChoice c = MatchSetupChoice.Default;
                c.Pace = MatchSpeed.PaceOf(s);
                SetupPrefs.Save(c);
                Assert.AreEqual((int)s, PlayerPrefs.GetInt(SetupPrefs.SpeedKey, -1));
                Assert.AreEqual(MatchSpeed.PaceOf(s), SetupPrefs.Load().Pace, 1e-9, s.ToString());
                GameFlow.ResetForTests();
                Assert.AreEqual(MatchSpeed.PaceOf(s), PlayNow.Choice().Pace, 1e-9, "Play Now uses the remembered speed: " + s);
            }
        }

        [Test]
        public void NoSavedSpeedMeansTheDefault()
        {
            Assert.AreEqual(MatchSpeed.DefaultPace, SetupPrefs.Load().Pace, 1e-9);
            PlayerPrefs.SetInt(SetupPrefs.SpeedKey, 9);
            Assert.AreEqual(MatchSpeed.DefaultPace, SetupPrefs.Load().Pace, 1e-9, "a bad value falls back to the default");
        }

        // ---- real seconds ----

        [Test]
        public void TimersReadInRealSecondsAtAnySpeed()
        {
            MatchSpeed.DisplayPace = 2;
            Assert.AreEqual(5.0, MatchSpeed.ToReal(10), 1e-9);
            MatchSpeed.DisplayPace = 0.75;
            Assert.AreEqual(10.0 / 0.75, MatchSpeed.ToReal(10), 1e-9);
            MatchSpeed.DisplayPace = 0;
            Assert.AreEqual(10.0, MatchSpeed.ToReal(10), 1e-9, "an unknown pace shows sim seconds");

            GameConfig cfg = GameConfig.CreateDefault();
            World w = World.CreateWorld(new WorldOptions { Seed = 5, TeamSize = cfg.TeamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            var s = new HudState();
            MatchSpeed.DisplayPace = 2;
            HudBinder.Read(w, HudBinder.LocalActor, s);
            Assert.AreEqual(cfg.MatchSeconds / 2, s.ClockRemaining, 1e-9, "the match clock at 2x shows wall time");
            Assert.AreEqual(cfg.HatchSeconds / 2, s.UntilHatch, 1e-9);
            MatchSpeed.DisplayPace = 0.75;
            HudBinder.Read(w, HudBinder.LocalActor, s);
            Assert.AreEqual(cfg.MatchSeconds / 0.75, s.ClockRemaining, 1e-9);
        }

        [Test]
        public void EachBuiltWorldSetsTheDisplayPace()
        {
            MatchSetupChoice c = CrimsonMinerFast;
            GameFlow.QueueMatch(c);
            GameFlow.RequestForBuild();
            Assert.AreEqual(MatchSpeed.FastPace, MatchSpeed.DisplayPace, 1e-9, "a match shows its own pace");

            GameFlow.QueueBackdrop(FlowState.Menu);
            GameFlow.RequestForBuild();
            Assert.AreEqual(MatchSpeed.FastPace, MatchSpeed.DisplayPace, 1e-9, "behind the menus: the pace the next match will use");
        }
    }
}
