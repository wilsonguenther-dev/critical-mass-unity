using System.Collections.Generic;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Menus;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Tests.PlayerExperience
{
    /// <summary>
    /// px lane F (2026-11-22): DUEL, PRACTICE RANGE and the first PLAY NOW class pick: how each maps onto the existing match
    /// options (team size, classes, bot difficulty, the world's own config), that each screen builds with one focus and live
    /// navigation, and that the pick shows once. The owner's bar: a practice range and a 1v1 within 2 presses.
    /// </summary>
    public sealed class PxModesTests
    {
        readonly Dictionary<string, (bool has, int v)> _prefs = new Dictionary<string, (bool, int)>();
        MatchSetupChoice _last;
        bool _idle;

        static IEnumerable<string> Keys()
        {
            foreach (string k in VersusPrefs.AllKeys) yield return k;
            foreach (string k in SetupPrefs.AllKeys) yield return k;
            yield return StarterPick.DoneKey;
        }

        [SetUp]
        public void SaveState()
        {
            _prefs.Clear();
            foreach (string k in Keys()) _prefs[k] = (PlayerPrefs.HasKey(k), PlayerPrefs.GetInt(k, 0));
            _last = GameFlow.LastSetup;
            _idle = HumanControlGuard.IdleAutopilotEnabled;
        }

        [TearDown]
        public void RestoreState()
        {
            foreach (KeyValuePair<string, (bool has, int v)> kv in _prefs)
                if (kv.Value.has) PlayerPrefs.SetInt(kv.Key, kv.Value.v); else PlayerPrefs.DeleteKey(kv.Key);
            PlayerPrefs.Save();
            GameFlow.LastSetup = _last;
            PlayModes.ResetForTests();
            HumanControlGuard.IdleAutopilotEnabled = _idle;
            StarterPick.ForceForTests = false;
        }

        static World Build(MatchSetupChoice c, out GameConfig cfg)
        {
            cfg = GameConfig.CreateDefault();
            WorldOptions o = MatchRequest.From(c, 4242).ToWorldOptions(cfg);
            return World.CreateWorld(o);
        }

        [Test]
        public void ADuelIsTeamSizeOneWithBothChosenClassesAndTheChosenBots()
        {
            foreach (ClassId mine in ClassIds.All)
                foreach (ClassId enemy in ClassIds.All)
                {
                    MatchSetupChoice c = PlayModes.DuelChoice(MatchSetupChoice.Default, mine, enemy, BotDifficulty.Hard);
                    MatchRequest r = MatchRequest.From(c, 7);
                    Assert.AreEqual(MatchMode.Duel, r.Mode);
                    Assert.AreEqual(1, r.TeamSize, "1 v 1");
                    Assert.AreEqual(BotDifficulty.Hard, r.BotDifficulty);
                    Assert.IsFalse(r.Backdrop);
                    World w = Build(c, out GameConfig cfg);
                    Assert.AreEqual(2, w.ActorCount, "two fighters on the real map");
                    Assert.AreEqual(mine, w.Actors[LocalSeat.Actor].Cls, "my class");
                    Assert.AreEqual(enemy, w.Actors[1].Cls, mine + " v " + enemy + ": the enemy's class");
                    Assert.AreNotEqual(w.Actors[0].Team, w.Actors[1].Team);
                    Assert.AreEqual(PlayModes.DuelMatchSeconds, cfg.MatchSeconds, "a 3:00 clock (no knock-out limit in the sim)");
                    Assert.GreaterOrEqual(cfg.HatchSeconds, cfg.MatchSeconds, "no Kaiju in a duel");
                    Assert.AreEqual(0, cfg.Bots.PassiveSeconds, "the duel bot fights");
                }
            MatchSetupChoice d = PlayModes.DuelChoice(MatchSetupChoice.Default, ClassId.Miner, ClassId.Healer, BotDifficulty.Easy);
            MatchSetupChoice s = PlayModes.Swapped(d);
            Assert.AreEqual(ClassId.Healer, s.Class, "SWAP CLASSES: I take theirs");
            Assert.AreEqual(ClassId.Miner, s.EnemyClass, "and they take mine");
            Assert.AreEqual(MatchMode.Duel, s.Mode);
            Assert.AreEqual(PlayModes.MatchSeconds(MatchMode.Duel), PlayModes.DuelMatchSeconds);
        }

        [Test]
        public void TheRangeDummyNeverAttacksUnlessItFightsBackAndTheClockNeverPresses()
        {
            foreach (RangeDummy k in new[] { RangeDummy.Standing, RangeDummy.Moving, RangeDummy.Fighting })
            {
                MatchSetupChoice c = PlayModes.RangeChoice(MatchSetupChoice.Default, ClassId.Farmer, ClassId.Miner, k, BotDifficulty.Normal);
                World w = Build(c, out GameConfig cfg);
                Assert.AreEqual(2, w.ActorCount, k + ": me and one dummy");
                Assert.AreEqual(ClassId.Farmer, w.Actors[0].Cls);
                Assert.AreEqual(ClassId.Miner, w.Actors[1].Cls, k + ": the dummy's class");
                Assert.AreEqual(PlayModes.RangeMatchSeconds, cfg.MatchSeconds, "an hour: no clock to beat");
                Assert.GreaterOrEqual(cfg.HatchSeconds, cfg.MatchSeconds, "the egg never hatches on the range");
                if (k == RangeDummy.Fighting) Assert.AreEqual(0, cfg.Bots.PassiveSeconds, "FIGHTS BACK is a real bot");
                else Assert.GreaterOrEqual(cfg.Bots.PassiveSeconds, cfg.MatchSeconds, k + ": never targets or damages me");
            }
            Assert.AreEqual(BotDifficulty.Easy, PlayModes.RangeChoice(MatchSetupChoice.Default, ClassId.Warrior, ClassId.Warrior, RangeDummy.Standing, BotDifficulty.Hard).BotDifficulty);
            Assert.AreEqual(BotDifficulty.Hard, PlayModes.RangeChoice(MatchSetupChoice.Default, ClassId.Warrior, ClassId.Warrior, RangeDummy.Moving, BotDifficulty.Easy).BotDifficulty);
            Assert.AreEqual(BotDifficulty.Easy, PlayModes.RangeChoice(MatchSetupChoice.Default, ClassId.Warrior, ClassId.Warrior, RangeDummy.Fighting, BotDifficulty.Easy).BotDifficulty);
            Assert.AreEqual("NO CLOCK TO BEAT", PlayModes.LengthLabel(MatchMode.Range, 1));

            // the idle autopilot never takes me on the range; the next mode gets it back
            HumanControlGuard.IdleAutopilotEnabled = true;
            PlayModes.OnMatchPrepared(PlayModes.RangeChoice(MatchSetupChoice.Default, ClassId.Warrior, ClassId.Warrior, RangeDummy.Standing, BotDifficulty.Normal));
            Assert.IsFalse(HumanControlGuard.IdleAutopilotEnabled, "no AFK autopilot on the range");
            PlayModes.OnMatchPrepared(PlayModes.ToChoice(MatchMode.Quick, MatchSetupChoice.Default, 5));
            Assert.IsTrue(HumanControlGuard.IdleAutopilotEnabled, "back on for a quick match");
        }

        [Test]
        public void OtherModesKeepTheirRulesBitForBit()
        {
            foreach (MatchMode m in new[] { MatchMode.Custom, MatchMode.Quick, MatchMode.FullBattle, MatchMode.Training, MatchMode.Practice, MatchMode.Spectate, MatchMode.Demo })
            {
                MatchSetupChoice c = PlayModes.ToChoice(m, MatchSetupChoice.Default, 5);
                c.EnemyClass = ClassId.Healer;   // ignored outside a duel or the range
                MatchRules a = PlayModes.Rules(m), b = PlayModes.Rules(c);
                Assert.AreEqual(a.MatchSeconds, b.MatchSeconds, m.ToString());
                Assert.AreEqual(a.HatchSeconds, b.HatchSeconds, m.ToString());
                Assert.AreEqual(a.PassiveBotSeconds, b.PassiveBotSeconds, m.ToString());
                Assert.AreEqual(0, b.OnlyClassPlusOne, m + ": the roster mix is untouched");
            }
        }

        [Test]
        public void DuelAndRangeAreTwoPressesFromThePlayMenuAndTheirScreenHasOneFocus()
        {
            CollectionAssert.Contains(LobbyNav.Forward[LobbyScreen.ModeSelect], LobbyScreen.Versus);
            CollectionAssert.Contains(LobbyNav.Forward[LobbyScreen.Versus], LobbyScreen.Ready);
            Assert.AreEqual(LobbyScreen.ModeSelect, LobbyNav.Back[LobbyScreen.Versus]);
            var go = new GameObject("Px Modes Test");
            try
            {
                ModeSelect s = go.AddComponent<ModeSelect>();
                s.EnsureBuilt();
                Assert.AreEqual(6, s.Cards.Count, "the six mode cards are unchanged");
                foreach (MenuItem it in new[] { s.DuelItem, s.RangeItem })
                {
                    Assert.IsNotNull(it);
                    Assert.IsTrue(it.interactable);
                    Assert.IsNotNull(it.Submitted, it.name + " does something");
                    Assert.AreEqual(Navigation.Mode.Explicit, it.navigation.mode);
                    Assert.IsNotNull(it.navigation.selectOnUp);
                }
                Assert.AreSame(s.DuelItem, s.Card(MatchMode.Custom).navigation.selectOnDown, "down from the last card reaches DUEL");

                foreach (MatchMode m in new[] { MatchMode.Duel, MatchMode.Range })
                {
                    VersusSetup v = go.AddComponent<VersusSetup>();
                    v.Bind(m);
                    Assert.AreSame(v.StartItem, v.DefaultItem, m + ": START is focused (one press to play)");
                    foreach (MenuItem it in new[] { v.MineItem, v.EnemyItem, v.KindItem, v.StartItem, v.BackItem })
                        Assert.IsTrue(it.navigation.selectOnUp != null || it.navigation.selectOnDown != null, it.name + " is no dead end");
                    MatchSetupChoice c = v.Choice();
                    Assert.AreEqual(m, c.Mode);
                    Assert.AreEqual(1, c.TeamSize);
                    Object.DestroyImmediate(v);
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
            Assert.AreEqual(ClassId.Farmer, VersusPrefs.Step(ClassId.Warrior, 1));
            Assert.AreEqual(ClassId.Healer, VersusPrefs.Step(ClassId.Warrior, -1));
            Assert.AreEqual(BotDifficulty.Normal, VersusPrefs.StepBots(BotDifficulty.Easy, 1));
            Assert.AreEqual(BotDifficulty.Easy, VersusPrefs.StepBots(BotDifficulty.Hard, 1));
        }

        [Test]
        public void TheFirstPlayNowPicksAClassOnceWithThreeStartersFirst()
        {
            PlayerPrefs.DeleteKey(StarterPick.DoneKey);
            Assert.IsFalse(StarterPick.Done);
            Assert.IsFalse(StarterPick.ShouldShow, "never in the Editor unless a test forces it (play tests press PLAY NOW)");
            CollectionAssert.AreEqual(new[] { ClassId.Farmer, ClassId.Warrior, ClassId.Miner }, new[] { StarterPick.Order[0], StarterPick.Order[1], StarterPick.Order[2] },
                "the three recommended starters first");
            CollectionAssert.AreEquivalent(ClassIds.All, StarterPick.Order, "all six shown");
            foreach (ClassId c in StarterPick.Order) Assert.IsFalse(string.IsNullOrEmpty(StarterPick.Does(c)), c + " says what you do");

            var go = new GameObject("Px Starter Test");
            try
            {
                StarterPick p = go.AddComponent<StarterPick>();
                p.EnsureBuilt();
                Assert.AreEqual(6, p.Cards.Count);
                Assert.AreSame(p.Cards[ClassId.Farmer], p.DefaultItem, "focus starts on a recommended starter");
                Assert.IsNotNull(p.PracticeItem, "PRACTICE FIRST is offered");
                foreach (MenuItem it in p.Cards.Values)
                {
                    Assert.IsNotNull(it.Submitted, "one press continues");
                    Assert.IsNotNull(it.navigation.selectOnUp);
                    Assert.IsNotNull(it.navigation.selectOnDown);
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }

            StarterPick.Remember(ClassId.Miner);
            Assert.IsTrue(StarterPick.Done, "shown once per profile");
            Assert.AreEqual((int)ClassId.Miner, PlayerPrefs.GetInt(SetupPrefs.ClassKey));
            Assert.AreEqual(ClassId.Miner, PlayNow.Choice().Class, "later PLAY NOW keeps the picked class");
            StarterPick.ForceForTests = true;
            Assert.IsFalse(StarterPick.ShouldShow, "never again after the pick");

            MatchSetupChoice range = StarterPick.PracticeChoice(ClassId.Farmer);
            Assert.AreEqual(MatchMode.Range, range.Mode, "PRACTICE FIRST is the range");
            Assert.AreEqual(ClassId.Farmer, range.Class);
            Assert.AreEqual(RangeDummy.Standing, range.Dummy);
        }
    }
}
