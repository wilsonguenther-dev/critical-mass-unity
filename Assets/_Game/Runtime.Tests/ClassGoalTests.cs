using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Onboarding;
using CriticalMass.Runtime.Progression;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b15 item 3 (systems audit W2): the first-match class goal. Every class has one line in the words table whose key
    /// slot resolves to a real key on both control schemes; each goal is done by exactly its own class's job (read from
    /// sim events); the done bit lives in the profile, survives a save, and an older profile reads as "no goal done".
    /// </summary>
    [TestFixture]
    public sealed class ClassGoalTests
    {
        static World SmallWorld()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            return World.CreateWorld(new WorldOptions { Seed = 7, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
        }

        [Test]
        public void EveryClassHasAGoalWithARealKeyOnBothControlSchemes()
        {
            Assert.AreEqual(ClassIds.All.Count, MatchWords.ClassGoals.Length, "one goal per class");
            foreach (ClassId c in ClassIds.All)
            foreach (ControlDevice d in new[] { ControlDevice.KeyboardMouse, ControlDevice.Gamepad })
            {
                string line = ClassGoals.Line(c, d);
                Debug.Log($"[b15 goal] {c} {d}: {line}");
                Assert.IsFalse(string.IsNullOrEmpty(line), c.ToString());
                Assert.AreEqual(-1, line.IndexOf('{'), $"{c} {d}: every key slot is filled: {line}");
                Assert.AreNotEqual("-", line.Substring(line.Length - 1), "a bound key, never '-'");
            }
        }

        static SimEvent Ev(SimEventType t, int a, int b, double x, double z, double v, BiomassType? bio = null) =>
            new SimEvent { Type = t, A = a, B = b, X = x, Z = z, Value = v, Bio = bio };

        [Test]
        public void EachGoalIsDoneOnlyByItsOwnJob()
        {
            World w = SmallWorld();
            int me = 0, ally = 1;
            double hx = w.Map.Hill.X, hz = w.Map.Hill.Z;
            Assert.IsTrue(ClassGoals.Done(ClassId.Warrior, Ev(SimEventType.Death, 6, me, hx, hz, 30), w, me), "knock-out on the hill");
            Assert.IsFalse(ClassGoals.Done(ClassId.Warrior, Ev(SimEventType.Death, 6, me, hx + 40, hz, 30), w, me), "a knock-out off the hill is not the goal");
            Assert.IsFalse(ClassGoals.Done(ClassId.Warrior, Ev(SimEventType.Death, 6, ally, hx, hz, 30), w, me), "someone else's knock-out");
            Assert.IsTrue(ClassGoals.Done(ClassId.Farmer, Ev(SimEventType.DeliverBiomass, me, 0, 0, 0, 2), w, me));
            Assert.IsTrue(ClassGoals.Done(ClassId.Miner, Ev(SimEventType.BankDeposit, me, 0, 0, 0, 3, BiomassType.Ore), w, me));
            Assert.IsFalse(ClassGoals.Done(ClassId.Miner, Ev(SimEventType.BankDeposit, me, 0, 0, 0, 3, BiomassType.Wood), w, me), "wood is not ore");
            Assert.IsTrue(ClassGoals.Done(ClassId.Demolitions, Ev(SimEventType.Explosion, me, 2, 0, 0, 1), w, me));
            Assert.IsFalse(ClassGoals.Done(ClassId.Demolitions, Ev(SimEventType.Explosion, me, 2, 0, 0, 0), w, me), "a blast that hurt nobody");
            Assert.IsTrue(ClassGoals.Done(ClassId.Builder, Ev(SimEventType.PiecePlaced, me, 0, 0, 0, 900), w, me), "a ladder or bridge");
            Assert.IsFalse(ClassGoals.Done(ClassId.Builder, Ev(SimEventType.WallBuilt, me, -1, 0, 0, -2), w, me), "a refused build");
            Assert.IsTrue(ClassGoals.Done(ClassId.Healer, Ev(SimEventType.HealLanded, ally, me, 0, 0, 12), w, me));
            Assert.IsFalse(ClassGoals.Done(ClassId.Healer, Ev(SimEventType.HealLanded, me, me, 0, 0, 12), w, me), "healing herself is not the goal");
            Assert.IsFalse(ClassGoals.Done(ClassId.Farmer, Ev(SimEventType.PiecePlaced, me, 0, 0, 0, 900), w, me), "another class's job");
        }

        [Test]
        public void TheDoneBitLivesInTheProfileAndAnOldProfileReadsAsNoneDone()
        {
            var p = new PlayerProfile();
            Assert.IsFalse(ClassGoals.IsDone(p, ClassId.Builder));
            Assert.IsTrue(ClassGoals.MarkDone(p, ClassId.Builder));
            Assert.IsFalse(ClassGoals.MarkDone(p, ClassId.Builder), "once only");
            Assert.IsTrue(ClassGoals.IsDone(p, ClassId.Builder));
            Assert.IsFalse(ClassGoals.IsDone(p, ClassId.Healer), "one bit per class");
            Assert.IsTrue(ProfileStore.TryParse(p.ToJson(), out PlayerProfile back));
            Assert.IsTrue(ClassGoals.IsDone(back, ClassId.Builder), "the bit survives a save");
            Assert.IsTrue(ProfileStore.TryParse("{\"Version\": 1, \"Coins\": 5}", out PlayerProfile old));
            Assert.AreEqual(0, old.ClassGoalsDone, "a profile written before b15 has no goal done");
            back.ClassGoalsDone = -1;
            back.Sanitize();
            Assert.AreEqual(63, back.ClassGoalsDone, "only the six class bits are kept");
        }

        [Test]
        public void TheWordsTableHasNoEmptyEntryAndEveryGoalNamesItself()
        {
            // the view shows only lines from the words table (ClassGoals.Line and MatchWords.GoalDone)
            foreach (string s in MatchWords.All()) Assert.IsFalse(string.IsNullOrEmpty(s), "no empty entry in the words table");
            foreach (string g in MatchWords.ClassGoals) StringAssert.Contains("goal:", g);
        }
    }
}
