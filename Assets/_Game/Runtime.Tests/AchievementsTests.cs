using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>DG-38: one test per achievement trigger on a scripted event stream, plus persistence, the toast and the list.</summary>
    [TestFixture]
    public sealed class AchievementsTests
    {
        const int Me = 0;

        static Achievements New(IAchievementStore store = null) =>
            new Achievements(store ?? new MemoryAchievementStore()) { LocalActor = Me, LocalTeam = TeamId.Azure };

        static CombatEvent Ko(int victim, int killer, int blow = 1) =>
            new CombatEvent { Kind = CombatEventKind.Knockout, Actor = victim, Other = killer, Blow = blow };

        static CombatEvent Deliver(int actor, int units) =>
            new CombatEvent { Kind = CombatEventKind.DeliverBiomass, Actor = actor, Other = -1, Amount = units };

        [TearDown]
        public void ClearCurrent() => Achievements.Current = null;

        [Test]
        public void TenAchievementsWithUniqueIds()
        {
            Assert.AreEqual(10, Achievements.All.Length);
            for (int i = 0; i < Achievements.All.Length; i++)
            for (int j = i + 1; j < Achievements.All.Length; j++)
                Assert.AreNotEqual(Achievements.All[i].Id, Achievements.All[j].Id);
        }

        [Test]
        public void FirstFeeding()
        {
            Achievements a = New();
            a.Feed(Deliver(5, 3));
            Assert.IsFalse(a.IsUnlocked(Achievements.FirstFeeding), "someone else's delivery");
            a.Feed(Deliver(Me, 1));
            Assert.IsTrue(a.IsUnlocked(Achievements.FirstFeeding));
        }

        [Test]
        public void HillHolder()
        {
            Achievements a = New();
            a.Sample(59.9);
            Assert.IsFalse(a.IsUnlocked(Achievements.HillHolder));
            a.Sample(60.0);
            Assert.IsTrue(a.IsUnlocked(Achievements.HillHolder));
        }

        [Test]
        public void Kingmaker()
        {
            Achievements a = New();
            a.Feed(new CombatEvent { Kind = CombatEventKind.Hatch, Amount = 1 });
            Assert.IsFalse(a.IsUnlocked(Achievements.Kingmaker), "none of the biomass was mine");
            a.ResetMatch();
            a.Feed(Deliver(Me, 2));
            a.Feed(new CombatEvent { Kind = CombatEventKind.Hatch, Amount = 2 });
            Assert.IsFalse(a.IsUnlocked(Achievements.Kingmaker), "the Kaiju sided with Crimson");
            a.ResetMatch();
            a.Feed(Deliver(Me, 2));
            a.Feed(new CombatEvent { Kind = CombatEventKind.Hatch, Amount = 1 });
            Assert.IsTrue(a.IsUnlocked(Achievements.Kingmaker));
        }

        [Test]
        public void OpeningBlow()
        {
            Achievements a = New();
            a.Feed(Ko(20, 7));
            a.Feed(Ko(21, Me));
            Assert.IsFalse(a.IsUnlocked(Achievements.OpeningBlow), "not the first knock-out");
            a.ResetMatch();
            a.Feed(Ko(21, Me));
            Assert.IsTrue(a.IsUnlocked(Achievements.OpeningBlow));
        }

        [Test]
        public void CleanSweep()
        {
            Achievements a = New();
            a.Feed(Ko(20, Me, 1));
            a.Feed(Ko(21, Me, 2));
            Assert.IsFalse(a.IsUnlocked(Achievements.CleanSweep));
            a.Feed(Ko(22, Me, 3));
            Assert.IsTrue(a.IsUnlocked(Achievements.CleanSweep));
        }

        [Test]
        public void OnARoll()
        {
            Achievements a = New();
            for (int i = 0; i < 4; i++) a.Feed(Ko(20 + i, Me));
            a.Feed(Ko(Me, 25));
            a.Feed(Ko(24, Me));
            Assert.IsFalse(a.IsUnlocked(Achievements.OnARoll), "the streak broke at four");
            for (int i = 0; i < 4; i++) a.Feed(Ko(26 + i % 3, Me));
            Assert.IsTrue(a.IsUnlocked(Achievements.OnARoll));
        }

        [Test]
        public void GiantSlayer()
        {
            Achievements a = New();
            a.Feed(new CombatEvent { Kind = CombatEventKind.KaijuKnockout, Other = 4 });
            Assert.IsFalse(a.IsUnlocked(Achievements.GiantSlayer));
            a.Feed(new CombatEvent { Kind = CombatEventKind.KaijuKnockout, Other = Me });
            Assert.IsTrue(a.IsUnlocked(Achievements.GiantSlayer));
        }

        [Test]
        public void FullHarvest()
        {
            Achievements a = New();
            a.Feed(Deliver(Me, 4));
            a.Feed(Deliver(Me, 3));
            Assert.IsFalse(a.IsUnlocked(Achievements.FullHarvest));
            a.Feed(Deliver(Me, 3));
            Assert.IsTrue(a.IsUnlocked(Achievements.FullHarvest));
        }

        [Test]
        public void Payback()
        {
            Achievements a = New();
            a.Feed(Ko(Me, 23));
            a.Feed(Ko(22, Me));
            Assert.IsFalse(a.IsUnlocked(Achievements.Payback), "wrong target");
            a.Feed(Ko(23, Me));
            Assert.IsTrue(a.IsUnlocked(Achievements.Payback));
        }

        [Test]
        public void BannerRaised()
        {
            Achievements a = New();
            a.Feed(new CombatEvent { Kind = CombatEventKind.MatchEnd, Amount = (int)MatchResult.Draw });
            a.Feed(new CombatEvent { Kind = CombatEventKind.MatchEnd, Amount = (int)MatchResult.Crimson });
            Assert.IsFalse(a.IsUnlocked(Achievements.BannerRaised));
            a.Feed(new CombatEvent { Kind = CombatEventKind.MatchEnd, Amount = (int)MatchResult.Azure });
            Assert.IsTrue(a.IsUnlocked(Achievements.BannerRaised));
        }

        [Test]
        public void UnlocksPersistAndAnnounceOnce()
        {
            var store = new MemoryAchievementStore();
            Achievements a = New(store);
            int announced = 0;
            a.Unlocked += i => announced++;
            a.Feed(Deliver(Me, 1));
            a.Feed(Deliver(Me, 1));
            Assert.AreEqual(1, announced);
            Assert.AreEqual(1, a.UnlockedThisMatch.Count);

            Achievements b = New(store);
            int again = 0;
            b.Unlocked += i => again++;
            Assert.IsTrue(b.IsUnlocked(Achievements.FirstFeeding), "kept between sessions");
            b.Feed(Deliver(Me, 1));
            Assert.AreEqual(0, again, "no second toast for an old unlock");
            Assert.IsTrue(b.EarnedThisMatch(Achievements.FirstFeeding), "still listed for this match");
            Assert.AreEqual(1, b.UnlockedCount);
        }

        [Test]
        public void TallyStreamFromTheWorldDrivesAchievements()
        {
            World w = World.CreateWorld(new WorldOptions
            {
                Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateDefault(),
            });
            var tally = new CombatTally();
            tally.Bind(w);
            Achievements a = New();
            tally.Emitted += a.Feed;
            World.PushEvent(w, SimEventType.DeliverBiomass, Me, 0, 0, 0, 10);
            for (int v = 0; v < 3; v++)
            {
                World.PushEvent(w, SimEventType.Hit, 17 + v, Me, 0, 0, 30);
                World.PushEvent(w, SimEventType.Death, 17 + v, Me, 0, 0, 30);
            }
            tally.Pump(w);
            Assert.IsTrue(a.IsUnlocked(Achievements.FirstFeeding));
            Assert.IsTrue(a.IsUnlocked(Achievements.FullHarvest));
            Assert.IsTrue(a.IsUnlocked(Achievements.OpeningBlow));
            Assert.IsTrue(a.IsUnlocked(Achievements.CleanSweep));
        }

        [Test]
        public void ToastQueuesUnlocksOneAtATimeAndListShowsState()
        {
            var go = new GameObject("Toast test");
            try
            {
                AchievementToast toast = go.AddComponent<AchievementToast>();
                toast.Build();
                Achievements a = New();
                toast.Use(a);
                Assert.AreSame(a, Achievements.Current);
                a.Feed(Deliver(Me, 10));   // First Feeding, then Full Harvest
                Assert.AreEqual(2, toast.Queued);
                toast.Tick(0.05f);
                Assert.AreEqual(Achievements.FirstFeeding, toast.ToastIndex);
                Assert.AreEqual("FIRST FEEDING", toast.ToastTitle);
                toast.Tick(AchievementToast.InSeconds + AchievementToast.HoldSeconds + AchievementToast.OutSeconds);
                Assert.IsFalse(toast.ToastShowing);
                toast.Tick(0.05f);
                Assert.AreEqual(Achievements.FullHarvest, toast.ToastIndex);

                toast.List.Refresh(a);
                Assert.AreEqual("EARNED", toast.List.State(Achievements.FirstFeeding));
                Assert.AreEqual("LOCKED", toast.List.State(Achievements.GiantSlayer));
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
