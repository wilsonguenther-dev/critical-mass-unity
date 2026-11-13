using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>DG-11: the sim's death event names the killer, the tally reads it, and the recap, feed and scoreboard show it.</summary>
    [TestFixture]
    public sealed class CombatFeedTests
    {
        static World NewWorld(int teamSize = 16)
        {
            return World.CreateWorld(new WorldOptions
            {
                Seed = 1337, TeamSize = teamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateDefault(),
            });
        }

        static int FirstOf(World w, TeamId team)
        {
            for (int i = 0; i < w.ActorCount; i++) if (w.Actors[i].Team == team && i != HudBinder.LocalActor) return i;
            return -1;
        }

        /// <summary>The rules-lane question: Damage.ApplyDamage writes the killer's id into the Death event's B.</summary>
        [Test]
        public void SimDeathEventCarriesTheKillerId()
        {
            World w = NewWorld();
            int killer = FirstOf(w, TeamId.Crimson);
            Assert.GreaterOrEqual(killer, 0);
            w.Actors[0].SpawnProtect = 0;   // the test's own world: no spawn protection on the victim
            int head = w.EventHead;
            Damage.ApplyDamage(w, 0, 1e6, killer, w.X[killer], w.Z[killer]);
            bool found = false;
            for (int i = head; i < w.EventHead; i++)
            {
                SimEvent e = w.Events[i % World.EventCapacity];
                if (e.Type != SimEventType.Death) continue;
                Assert.AreEqual(0, e.A, "A is the victim");
                Assert.AreEqual(killer, e.B, "B is the killer");
                found = true;
            }
            Assert.IsTrue(found, "no Death event");
        }

        [Test]
        public void TallyTranslatesADeathIntoAKnockoutWithTeamsAndClasses()
        {
            World w = NewWorld();
            var tally = new CombatTally();
            tally.Bind(w);
            int killer = FirstOf(w, TeamId.Crimson);
            w.Actors[killer].Cls = ClassId.Warrior;
            w.Actors[0].SpawnProtect = 0;
            CombatEvent got = default;
            int n = 0;
            tally.Emitted += e => { if (e.Kind == CombatEventKind.Knockout) { got = e; n++; } };
            Damage.ApplyDamage(w, 0, 1e6, killer, w.X[killer], w.Z[killer]);
            tally.Pump(w);
            Assert.AreEqual(1, n);
            Assert.AreEqual(0, got.Actor);
            Assert.AreEqual(killer, got.Other);
            Assert.AreEqual(TeamId.Crimson, got.OtherTeam);
            Assert.AreEqual(ClassId.Warrior, got.OtherClass);
            Assert.AreEqual(TeamId.Azure, got.ActorTeam);
            Assert.AreEqual(1, got.Blow);
            Assert.AreEqual(killer, tally.LastKiller(0));
            Assert.AreEqual(1, tally.Tally(killer).Streak);
        }

        [Test]
        public void ScriptedDeathShowsRecapTextPortraitAndFeedLine()
        {
            World w = NewWorld();
            int killer = FirstOf(w, TeamId.Crimson);
            w.Actors[killer].Cls = ClassId.Warrior;
            var tally = new CombatTally();
            tally.Bind(w);
            var go = new GameObject("Feed test");
            try
            {
                KillFeed feed = go.AddComponent<KillFeed>();
                DeathRecap recap = go.AddComponent<DeathRecap>();
                feed.Build();
                recap.Build();
                tally.Emitted += e =>
                {
                    if (e.Kind != CombatEventKind.Knockout) return;
                    feed.Push(e, 0, w.Kaiju.State);
                    if (e.Actor == 0) recap.Show(e, w.Kaiju.State, false, 1);
                };
                World.PushEvent(w, SimEventType.Hit, 0, killer, 0, 0, 30);
                World.PushEvent(w, SimEventType.Death, 0, killer, 0, 0, 30);
                tally.Pump(w);

                Assert.IsTrue(recap.IsOpen);
                Assert.AreEqual("Knocked out by Crimson Warrior: Cleave", recap.Line);
                Assert.AreEqual("portrait-warrior-crimson", recap.PortraitName);
                Assert.IsFalse(string.IsNullOrEmpty(recap.TipText), "one tip");
                StringAssert.DoesNotContain("{", recap.TipText);

                KillFeed.Entry row = feed.Row(0);
                Assert.IsTrue(row.Active);
                Assert.AreEqual("WARRIOR", row.Killer);
                Assert.AreEqual("YOU", row.Victim);
                Assert.IsTrue(row.Own, "the local player's line is on brass");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void SlamDeathIsCreditedToTheKaijuAndUnknownShowsKnockedOut()
        {
            World w = NewWorld();
            var tally = new CombatTally();
            tally.Bind(w);
            int last = int.MinValue;
            tally.Emitted += e => { if (e.Kind == CombatEventKind.Knockout) last = e.Other; };

            World.PushEvent(w, SimEventType.Hit, 0, -1, 0, 0, 40);
            World.PushEvent(w, SimEventType.Death, 0, -1, 0, 0, 40);
            World.PushEvent(w, SimEventType.KaijuSlam, -1, 1, 0, 0, 5);
            tally.Pump(w);
            Assert.AreEqual(KillSource.KaijuSlam, last);
            Assert.AreEqual("Knocked out by the Kaiju: Slam", KillWords.Recap(last, TeamId.Azure, ClassId.Warrior));

            World.PushEvent(w, SimEventType.Death, 0, Damage.KaijuSource, 0, 0, 40);
            tally.Pump(w);
            Assert.AreEqual(KillSource.KaijuMelee, last);
            Assert.AreEqual("Knocked out by the Kaiju", KillWords.Recap(last, TeamId.Azure, ClassId.Warrior));

            World.PushEvent(w, SimEventType.Death, 0, -1, 0, 0, 40);
            World.PushEvent(w, SimEventType.Respawn, 3, -1, 0, 0, 0);
            tally.Pump(w);
            Assert.AreEqual(KillSource.Unknown, last);
            Assert.AreEqual("Knocked out", KillWords.Recap(last, TeamId.Azure, ClassId.Warrior));
            Assert.IsNull(DeathRecap.PortraitFor(last, TeamId.Azure, ClassId.Warrior, KaijuState.Azure));
        }

        [Test]
        public void ThreeKnockoutsInOneSwingCountAsOneBlow()
        {
            World w = NewWorld();
            var tally = new CombatTally();
            tally.Bind(w);
            int maxBlow = 0;
            tally.Emitted += e => { if (e.Kind == CombatEventKind.Knockout && e.Other == 0) maxBlow = Math.Max(maxBlow, e.Blow); };
            for (int v = 0; v < 3; v++)
            {
                int victim = 17 + v;
                World.PushEvent(w, SimEventType.Hit, victim, 0, 0, 0, 30);
                World.PushEvent(w, SimEventType.Death, victim, 0, 0, 0, 30);
            }
            tally.Pump(w);
            Assert.AreEqual(3, maxBlow);

            // a different event between two kills ends the blow
            maxBlow = 0;
            World.PushEvent(w, SimEventType.Death, 20, 0, 0, 0, 30);
            World.PushEvent(w, SimEventType.Gather, 5, 0, 0, 0, 1);
            World.PushEvent(w, SimEventType.Death, 21, 0, 0, 0, 30);
            tally.Pump(w);
            Assert.AreEqual(1, maxBlow);
        }

        [Test]
        public void DeliveriesAreTalliedPerActorAndANewEpochResets()
        {
            World w = NewWorld();
            var tally = new CombatTally();
            tally.Bind(w);
            World.PushEvent(w, SimEventType.DeliverBiomass, 3, 0, 0, 0, 4);
            World.PushEvent(w, SimEventType.DeliverBiomass, 3, 0, 0, 0, 2);
            World.PushEvent(w, SimEventType.DeliverCorruption, 20, 1, 0, 0, 1);
            tally.Pump(w);
            Assert.AreEqual(6, tally.BiomassDelivered(3));
            Assert.AreEqual(1, tally.CorruptionDelivered(20));
            Assert.AreEqual(6, tally.Tally(3).BiomassDelivered);

            bool reset = false;
            tally.MatchReset += () => reset = true;
            World.ResetWorld(w, w.Opts);
            tally.Pump(w);
            Assert.IsTrue(reset);
            Assert.AreEqual(0, tally.BiomassDelivered(3));
        }

        [Test]
        public void FeedKeepsFiveLinesForFourSeconds()
        {
            var go = new GameObject("Feed test");
            try
            {
                KillFeed feed = go.AddComponent<KillFeed>();
                feed.Build();
                for (int i = 0; i < 7; i++)
                {
                    var e = new CombatEvent { Kind = CombatEventKind.Knockout, Actor = 17 + i, Other = 1, ActorTeam = TeamId.Crimson, OtherTeam = TeamId.Azure, OtherClass = ClassId.Miner, ActorClass = ClassId.Farmer };
                    feed.Push(e, 0, KaijuState.Egg);
                }
                Assert.AreEqual(KillFeed.Lines, feed.ActiveCount);
                Assert.AreEqual("MINER", feed.Row(0).Killer);
                Assert.AreEqual("FARMER", feed.Row(0).Victim);
                Assert.IsFalse(feed.Row(0).Own);
                feed.Tick(KillFeed.LineSeconds - 0.1f);
                Assert.AreEqual(KillFeed.Lines, feed.ActiveCount);
                feed.Tick(0.2f);
                Assert.AreEqual(0, feed.ActiveCount);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void ScoreboardShows32RowsThatMatchTheTally()
        {
            World w = NewWorld(16);
            var tally = new CombatTally();
            tally.Bind(w);
            var scratch = new InputFrame();
            for (int i = 0; i < 1800; i++)
            {
                MatchTick.AdvanceNoHuman(w, scratch);
                if (i % 2 == 0) tally.Pump(w);   // two ticks per frame, as at 2x
            }
            tally.Pump(w);
            var go = new GameObject("Scoreboard test");
            try
            {
                Scoreboard sb = go.AddComponent<Scoreboard>();
                sb.Build();
                sb.Refresh(w, tally, 0);
                Assert.AreEqual(16, sb.RowCount(TeamId.Azure));
                Assert.AreEqual(16, sb.RowCount(TeamId.Crimson));
                var seen = new bool[w.ActorCount];
                foreach (TeamId team in TeamIds.All)
                {
                    int prevKo = int.MaxValue;
                    for (int r = 0; r < 16; r++)
                    {
                        int id = sb.RowActor(team, r);
                        Assert.GreaterOrEqual(id, 0);
                        Assert.IsFalse(seen[id], "actor listed twice");
                        seen[id] = true;
                        ActorTally t = tally.Tally(id);
                        Assert.AreEqual(team, t.Team);
                        Assert.AreEqual(id == 0 ? "YOU" : KillWords.ClassUpper[(int)t.Cls], sb.Cell(team, r, 0));
                        Assert.AreEqual(t.Knockouts.ToString(), sb.Cell(team, r, 1));
                        Assert.AreEqual(t.Deaths.ToString(), sb.Cell(team, r, 2));
                        Assert.AreEqual(((int)Math.Floor(t.HillSeconds)).ToString(), sb.Cell(team, r, 3));
                        Assert.AreEqual(t.BiomassDelivered.ToString(), sb.Cell(team, r, 4));
                        Assert.AreEqual(t.CorruptionDelivered.ToString(), sb.Cell(team, r, 5));
                        Assert.AreEqual(w.Actors[id].Kills, t.Knockouts, "knock-outs are the sim's own counter");
                        Assert.LessOrEqual(t.Knockouts, prevKo, "sorted by knock-outs");
                        prevKo = t.Knockouts;
                    }
                }
                // delivered totals agree with the sim's per-team tally
                foreach (TeamId team in TeamIds.All)
                {
                    int sum = 0;
                    for (int id = 0; id < w.ActorCount; id++) if (w.Actors[id].Team == team) sum += tally.BiomassDelivered(id);
                    Assert.AreEqual(Math.Round(w.Match.Delivered[(int)team].Biomass), sum, 1e-6, team + " biomass");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void PumpAndScoreboardAllocateNothingAfterWarmUp()
        {
            World w = NewWorld(16);
            var tally = new CombatTally();
            tally.Bind(w);
            int seen = 0;
            tally.Emitted += e => seen++;
            var scratch = new InputFrame();
            var go = new GameObject("Scoreboard test");
            try
            {
                Scoreboard sb = go.AddComponent<Scoreboard>();
                sb.Build();
                for (int i = 0; i < 600; i++) { MatchTick.AdvanceNoHuman(w, scratch); tally.Pump(w); }
                sb.Refresh(w, tally, 0);
                sb.Refresh(w, tally, 0);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 120; i++)
                {
                    World.PushEvent(w, SimEventType.DeliverBiomass, 3, 0, 0, 0, 1);
                    World.PushEvent(w, SimEventType.Death, 18, 3, 0, 0, 1);
                    tally.Pump(w);
                    sb.Refresh(w, tally, 0);
                }
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                TestContext.WriteLine("Pump + Scoreboard.Refresh allocated " + bytes + " bytes over 120 frames");
                Assert.AreEqual(0, bytes);
                Assert.Greater(seen, 0);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void TipsResolveEveryTokenAndPickByCause()
        {
            KillWords.Init(GameConfig.CreateDefault());
            string slam = DeathRecap.TipFor(KillSource.KaijuSlam, ClassId.Miner, false, KaijuState.Azure, 3);
            StringAssert.Contains("slam", slam);
            StringAssert.DoesNotContain("{", slam);
            string first = DeathRecap.TipFor(4, ClassId.Miner, false, KaijuState.Egg, 1);
            StringAssert.Contains("back in 5 s", first);
            string cargo = DeathRecap.TipFor(4, ClassId.Farmer, true, KaijuState.Egg, 4);
            StringAssert.Contains("Carriers", cargo);
            Assert.IsNull(KillWords.Resolve("A {cfg:noSuchToken} tip", GameConfig.CreateDefault()));
        }
    }
}
