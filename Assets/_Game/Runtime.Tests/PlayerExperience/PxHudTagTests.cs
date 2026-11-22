using System.Collections.Generic;
using System.Reflection;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Player;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests.PlayerExperience
{
    /// <summary>
    /// Player-experience lane D: every PlayerReasons code has words (by reflection, so a code another lane adds fails here
    /// until it is worded), the damage-direction bearing in both cameras, my kill-feed line is pinned, and the personal
    /// tally shows the sim's and the combat log's numbers.
    /// </summary>
    [TestFixture]
    public sealed class PxHudTagTests
    {
        static List<KeyValuePair<string, int>> Codes(string prefix)
        {
            var list = new List<KeyValuePair<string, int>>();
            foreach (FieldInfo f in typeof(PlayerReasons).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (f.IsLiteral && f.FieldType == typeof(int) && f.Name.StartsWith(prefix))
                    list.Add(new KeyValuePair<string, int>(f.Name, (int)f.GetRawConstantValue()));
            Assert.IsNotEmpty(list, "PlayerReasons has " + prefix + "* codes");
            return list;
        }

        [Test]
        public void EveryBlockedCodeHasItsOwnWord()
        {
            foreach (var c in Codes("Blocked"))
            {
                string w = ActionTags.BlockedWords(c.Value, 12);
                Assert.IsFalse(string.IsNullOrEmpty(w), c.Key);
                if (c.Value != PlayerReasons.BlockedWall) Assert.AreNotEqual("BLOCKED", w, c.Key + " falls through to the generic word");
                Assert.LessOrEqual(w.Length, 16, c.Key + " fits one pill");
            }
            Assert.AreEqual("SHIELD -12", ActionTags.BlockedWords(PlayerReasons.BlockedShield, 12));
            Assert.AreEqual("TOO CLOSE -5", ActionTags.BlockedWords(ActionTags.BlockedGlancing, 5));
        }

        [Test]
        public void EveryButtonRefusalStructureAndWorldCodeHasWords()
        {
            foreach (var b in Codes("Button")) Assert.AreNotEqual("PRESS", ActionTags.ButtonWords(b.Value), b.Key);
            foreach (var s in Codes("Structure")) Assert.AreNotEqual("STRUCTURE", WorldPops.StructureWordsFor(s.Value), s.Key);
            foreach (var k in Codes("World")) Assert.AreNotEqual("DONE", WorldPops.WorldWordsFor(k.Value, 1), k.Key);
            foreach (var r in Codes("Refused"))
                foreach (var b in Codes("Button"))
                    for (int cls = 0; cls < 6; cls++)
                    {
                        string w = ActionTags.RefusalWords(b.Value, r.Value, (ClassId)cls, 3);
                        Assert.AreNotEqual("CAN'T DO THAT NOW", w, r.Key + " / " + b.Key + " / " + (ClassId)cls);
                        Assert.LessOrEqual(w.Length, 32, r.Key + " fits one pill");
                    }
            Assert.AreEqual("ON COOLDOWN 3 s", ActionTags.RefusalWords(PlayerReasons.ButtonAbility, PlayerReasons.RefusedCooldown, ClassId.Warrior, 3));
            Assert.AreEqual("NO ORE VEIN HERE", ActionTags.RefusalWords(PlayerReasons.ButtonDigPlace, PlayerReasons.RefusedNoTarget, ClassId.Miner, 0));
        }

        [Test]
        public void TheOlderRefusalEventsAreWordedAndCarryALedgerReason()
        {
            var refused = new HashSet<int>();
            foreach (var r in Codes("Refused")) refused.Add(r.Value);
            for (int code = -5; code <= -1; code++)
            {
                Assert.IsNotEmpty(ActionTags.OreRefusal(code, out int reason));
                Assert.IsTrue(refused.Contains(reason), "ore " + code);
            }
            for (int code = -10; code <= -1; code++)
            {
                Assert.IsNotEmpty(ActionTags.WallRefusal(code, out int reason));
                Assert.IsTrue(refused.Contains(reason), "wall " + code);
            }
            Assert.AreNotEqual(ActionTags.DeliverRefusal(1), ActionTags.DeliverRefusal(2));
            Assert.AreNotEqual(ActionTags.JumpRefusal(1), ActionTags.JumpRefusal(2));
        }

        [Test]
        public void DamageDirectionBearingInBothCameras()
        {
            Assert.AreEqual(0f, PxBearing.FromScreenDelta(0f, 1f), 1e-3f, "above the player: up");
            Assert.AreEqual(90f, PxBearing.FromScreenDelta(1f, 0f), 1e-3f, "right");
            Assert.AreEqual(180f, PxBearing.FromScreenDelta(0f, -1f), 1e-3f, "below / behind");
            Assert.AreEqual(270f, PxBearing.FromScreenDelta(-1f, 0f), 1e-3f, "left");
            // top-down: screen-up is world +Z, screen-right world +X
            Assert.AreEqual(90f, PxBearing.FromBasis(1f, 0f, 0f, 1f, 1f, 0f), 1e-3f);
            Assert.AreEqual(180f, PxBearing.FromBasis(0f, -1f, 0f, 1f, 1f, 0f), 1e-3f);
            // third person looking down +X: screen-up is +X, screen-right is -Z; a hit from -X is behind me
            Assert.AreEqual(0f, PxBearing.FromBasis(1f, 0f, 1f, 0f, 0f, -1f), 1e-3f, "in front");
            Assert.AreEqual(180f, PxBearing.FromBasis(-1f, 0f, 1f, 0f, 0f, -1f), 1e-3f, "from behind");
            Assert.AreEqual(270f, PxBearing.FromBasis(0f, 1f, 1f, 0f, 0f, -1f), 1e-3f, "+Z is on my left");
            Assert.AreEqual(20f, PxBearing.Delta(350f, 10f), 1e-3f);
            Assert.AreEqual(1f, DamageDirection.StrengthFor(1f), 1e-4f);
            Assert.AreEqual(0.45f, DamageDirection.StrengthFor(0f), 1e-4f, "a chip still shows");
        }

        static CombatEvent Ko(int victim, int killer) => new CombatEvent
        {
            Kind = CombatEventKind.Knockout, Actor = victim, Other = killer, ActorTeam = TeamId.Crimson, OtherTeam = TeamId.Azure,
            ActorClass = ClassId.Farmer, OtherClass = ClassId.Miner,
        };

        [Test]
        public void MyKillFeedLineIsPinnedThroughABusyFight()
        {
            var go = new GameObject("Px feed test");
            try
            {
                KillFeed feed = go.AddComponent<KillFeed>();
                feed.Build();
                for (int i = 0; i < 8; i++) feed.Push(Ko(17 + i, 1), 0, KaijuState.Egg);   // eight other kills fill the feed and queue
                feed.Push(Ko(25, 0), 0, KaijuState.Egg);                                    // then mine
                Assert.IsTrue(feed.Row(0).Own, "my line shows at once, ahead of the queue");
                Assert.AreEqual(KillWords.You, feed.Row(0).Killer);
                for (int i = 0; i < 8; i++) { feed.Push(Ko(17 + i, 2), 0, KaijuState.Egg); feed.Tick(0.1f); }
                int own = 0;
                for (int r = 0; r < KillFeed.Lines; r++) if (feed.Row(r).Active && feed.Row(r).Own) own++;
                Assert.AreEqual(1, own, "other kills never push my line out during its four seconds");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ThePersonalTallyEqualsTheCombatLogTotals()
        {
            World w = World.CreateWorld(new WorldOptions
            {
                Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateDefault(),
            });
            int me = 0;
            var tally = new CombatTally();
            tally.Bind(w);
            var assists = new AssistTracker();
            int ko = 0;
            for (int i = 0; i < w.ActorCount && ko < 3; i++)
            {
                if (w.Actors[i].Team != TeamId.Crimson) continue;
                w.Actors[i].SpawnProtect = 0;
                Damage.ApplyDamage(w, i, 1e6, me, w.X[me], w.Z[me]);
                ko++;
            }
            w.Actors[me].HillSeconds = 34.6;
            tally.Pump(w);
            var go = new GameObject("Px tally test", typeof(RectTransform));
            try
            {
                var pt = new PlayerTally((RectTransform)go.transform);
                pt.Tick(w, me, assists);
                Assert.AreEqual(3, tally.Tally(me).Knockouts, "the combat log saw three knock-outs");
                Assert.AreEqual(tally.Tally(me).Knockouts, pt.Kills, "KO matches the combat log");
                Assert.AreEqual((int)System.Math.Floor(tally.Tally(me).HillSeconds), pt.HillSeconds, "hill seconds match the scoreboard");
                Assert.AreEqual(0, pt.Assists);
                StringAssert.StartsWith("KO 3  AST 0", pt.Line.text);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
