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
            foreach (string k in Keys()) _prefs[k] = (CmPrefs.HasKey(k), CmPrefs.GetInt(k, 0));
            _last = GameFlow.LastSetup;
            _idle = HumanControlGuard.IdleAutopilotEnabled;
        }

        [TearDown]
        public void RestoreState()
        {
            GameFlow.ResetForTests();
            foreach (KeyValuePair<string, (bool has, int v)> kv in _prefs)
                if (kv.Value.has) CmPrefs.SetInt(kv.Key, kv.Value.v); else CmPrefs.DeleteKey(kv.Key);
            CmPrefs.Save();
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
                    Assert.Greater(cfg.HatchSeconds, cfg.MatchSeconds, "no Kaiju in a duel");
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
        public void ADuelOrRangeQueuedWithOnlyItsModeIsOneVersusOneWithNoKaijuAndTheRangeHasNoAutopilot()
        {
            // the Editor review queued Mode = Duel / Range by hand (team size left at 16) and got a 16 v 16 world: the mode
            // now normalises its own choice on GameFlow's path (PrepareMatch -> PlayModes.Normalize -> MatchRequest.From)
            foreach (MatchMode m in new[] { MatchMode.Duel, MatchMode.Range })
            {
                MatchSetupChoice raw = MatchSetupChoice.Default;
                raw.Mode = m;
                raw.TeamSize = 16;
                GameFlow.ResetForTests();
                HumanControlGuard.IdleAutopilotEnabled = true;
                GameFlow.QueueMatch(raw, 99);
                MatchRequest r = GameFlow.RequestForBuild();
                Assert.IsNotNull(r, m + ": queued");
                Assert.AreEqual(m, r.Mode);
                Assert.AreEqual(1, r.TeamSize, m + ": team size 1 whoever built the choice");
                Assert.IsFalse(r.Backdrop, m + ": never a backdrop (no actor-0 autopilot)");
                var cfg = GameConfig.CreateDefault();
                World w = World.CreateWorld(r.ToWorldOptions(cfg));
                Assert.AreEqual(2, w.ActorCount, m + ": me and one opponent");
                Assert.Greater(w.Config.HatchSeconds, w.Config.MatchSeconds, m + ": the egg never hatches inside the match");
                if (m == MatchMode.Range) Assert.IsFalse(HumanControlGuard.IdleAutopilotEnabled, "no AFK autopilot on the range");
                else Assert.IsTrue(HumanControlGuard.IdleAutopilotEnabled, "a duel keeps the idle autopilot setting");
            }
            GameFlow.ResetForTests();
        }

        [Test]
        public void AWholeDuelRunsToTheFinalWhistleWithoutAKaiju()
        {
            MatchSetupChoice c = PlayModes.DuelChoice(MatchSetupChoice.Default, ClassId.Warrior, ClassId.Farmer, BotDifficulty.Normal);
            World w = Build(c, out GameConfig cfg);
            int ticks = (int)System.Math.Ceiling((cfg.MatchSeconds + 2) * cfg.SimHz);
            for (int i = 0; i < ticks && w.Match.Phase != MatchPhase.Ended; i++) SimStep.Step(w);
            Assert.AreEqual(MatchPhase.Ended, w.Match.Phase, "a duel ends on its 3:00 clock");
            Assert.IsFalse(w.Match.Hatched, "and the Kaiju never hatched");
            Assert.AreEqual(2, w.ActorCount);
        }

        [Test]
        public void NothingScoresOnTheRangeAndTheDuelSaysHowItIsWon()
        {
            MatchSetupChoice c = PlayModes.RangeChoice(MatchSetupChoice.Default, ClassId.Warrior, ClassId.Farmer, RangeDummy.Standing, BotDifficulty.Normal);
            World w = Build(c, out GameConfig cfg);
            Assert.AreEqual(0, w.Config.Hill.Radius, "the range has no hill to hold");
            int ticks = (int)(60 * cfg.SimHz);   // a minute: the review saw 10 hill points 20 s in
            for (int i = 0; i < ticks; i++) SimStep.Step(w);
            Assert.AreEqual(0, w.Match.HillSeconds.Azure, 1e-9, "no hill time for me");
            Assert.AreEqual(0, w.Match.HillSeconds.Crimson, 1e-9, "none for the dummy");
            Assert.AreEqual(HillState.Neutral, w.Match.HillState);
            Assert.AreEqual(MatchPhase.Playing, w.Match.Phase, "and nothing ends it");

            MatchSetupChoice d = PlayModes.DuelChoice(MatchSetupChoice.Default, ClassId.Warrior, ClassId.Farmer, BotDifficulty.Normal);
            World dw = Build(d, out GameConfig dcfg);
            Assert.Greater(dw.Config.Hill.Radius, 0, "a duel keeps the hill: it is how a duel is won");
            StringAssert.Contains("3:00", VersusSetup.HowToWin(MatchMode.Duel));
            StringAssert.Contains("hill", VersusSetup.HowToWin(MatchMode.Duel));
            StringAssert.Contains("No knock-out limit", VersusSetup.HowToWin(MatchMode.Duel));
            StringAssert.Contains("NOTHING SCORES", VersusSetup.HowToWin(MatchMode.Range));
            var go = new GameObject("Px How Test");
            try
            {
                VersusSetup v = go.AddComponent<VersusSetup>();
                v.Bind(MatchMode.Duel);
                Assert.AreEqual(VersusSetup.HowToWin(MatchMode.Duel), v.HowLine, "the duel screen says it");
                v.Bind(MatchMode.Range);
                Assert.AreEqual(VersusSetup.HowToWin(MatchMode.Range), v.HowLine);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void EveryStarterCardShowsItsNameItsLineAndThreeCarryTheRecommendedMark()
        {
            var go = new GameObject("Px Starter Cards Test");
            try
            {
                StarterPick p = go.AddComponent<StarterPick>();
                p.EnsureBuilt();
                foreach (ClassId c in StarterPick.Order)
                {
                    Assert.IsFalse(string.IsNullOrEmpty(p.CardTitle(c)), c + ": a title");
                    Assert.IsFalse(string.IsNullOrEmpty(p.CardLine(c)), c + ": one line on what you do");
                    Assert.AreEqual(StarterPick.Does(c), p.CardLine(c));
                }
                Assert.AreEqual(3, p.RecommendedMarks, "three RECOMMENDED marks");
                // pass 3 review: the labels existed but drew nothing. Every label must have its words, a font, a colour
                // that differs from what it sits on, and a preferred width above zero.
                foreach (ClassId c in StarterPick.Order)
                {
                    Color card = p.CardItem(c).Body.FillColor;
                    foreach (Text t in p.CardLabels(c)) AssertDraws(t, card, c + " " + t.name);
                    Text bw = p.BadgeWord(c);
                    if (!p.IsRecommended(c)) { Assert.IsNull(bw, c + " has no badge"); continue; }
                    var pill = bw.transform.parent.GetComponent<CriticalMass.Runtime.Hud.Ui.VectorShape>();
                    Assert.IsNotNull(pill);
                    AssertDraws(bw, pill.FillColor, c + " badge");
                    Assert.AreEqual("RECOMMENDED", bw.text);
                    // the tab sits above the card, never over the name
                    RectTransform tab = (RectTransform)pill.transform, cardRt = (RectTransform)p.CardItem(c).transform;
                    Vector3[] a = new Vector3[4], b = new Vector3[4];
                    tab.GetWorldCorners(a);
                    cardRt.GetWorldCorners(b);
                    Assert.GreaterOrEqual(a[0].y, b[1].y - 1f, c + ": the tab's bottom edge is at or above the card's top edge");
                }
                foreach (ClassId c in new[] { ClassId.Farmer, ClassId.Warrior, ClassId.Miner }) Assert.IsTrue(p.IsRecommended(c), c + " is marked");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        static void AssertDraws(Text t, Color behind, string what)
        {
            Assert.IsNotNull(t, what);
            Assert.IsFalse(string.IsNullOrEmpty(t.text), what + ": has words");
            Assert.IsNotNull(t.font, what + ": has a font");
            Assert.Greater(t.color.a, 0.2f, what + ": is not transparent");
            float d = Mathf.Abs(t.color.r - behind.r) + Mathf.Abs(t.color.g - behind.g) + Mathf.Abs(t.color.b - behind.b);
            Assert.Greater(d, 0.3f, what + ": its colour differs from what it sits on");
            Assert.Greater(t.preferredWidth, 0f, what + ": draws something");
            Assert.IsTrue(t.gameObject.activeInHierarchy || !t.transform.root.gameObject.activeSelf, what + ": active");
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
                Assert.IsFalse(b.NoKaiju, m + ": the Kaiju still hatches");
                Assert.IsFalse(b.NoHill, m + ": the hill still scores");
                Assert.AreEqual(c.TeamSize, PlayModes.Normalize(c).TeamSize, m + ": Normalize leaves it alone");
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
            CmPrefs.DeleteKey(StarterPick.DoneKey);
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
            Assert.AreEqual((int)ClassId.Miner, CmPrefs.GetInt(SetupPrefs.ClassKey));
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
