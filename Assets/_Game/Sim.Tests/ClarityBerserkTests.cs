using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of src/sim/clarity.test.ts describe "CM-28 berserkLocked event" (lines 141-196, 09e0904).</summary>
    [TestFixture]
    public sealed class ClarityBerserkTests
    {
        private static WorldOptions Opts(ClassId cls, int seed)
        {
            return new WorldOptions
            {
                Seed = seed,
                TeamSize = 5,
                HumanTeam = TeamId.Azure,
                HumanClass = cls,
                Config = GameConfig.CreateBrowser5v5(),
            };
        }

        private static World MakeWorld(ClassId cls = ClassId.Warrior, int seed = 11)
        {
            return World.CreateWorld(Opts(cls, seed));
        }

        private static List<SimEvent> Events(World w, SimEventType type)
        {
            var output = new List<SimEvent>();
            for (int h = System.Math.Max(0, w.EventHead - World.EventCapacity); h < w.EventHead; h++)
            {
                SimEvent e = w.Events[h % World.EventCapacity];
                if (e.Type == type)
                    output.Add(new SimEvent { Type = e.Type, A = e.A, B = e.B, X = e.X, Z = e.Z, Value = e.Value });
            }
            return output;
        }

        [Test]
        public void BerserkLockedOneEventTheFirstTimeTheLockIsSetByARealDeliveryNoneAfter()
        {
            World w = MakeWorld();
            KaijuFeedConfig feed = w.Config.KaijuFeed;
            double dt = 1 / w.Config.SimHz;
            double units = System.Math.Ceiling(feed.BerserkThreshold / feed.CorruptionInstabilityPerUnit);
            double half = System.Math.Floor(units / 2);
            SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Corruption, half, 5);
            Assert.AreEqual(false, w.Match.BerserkLocked);
            Assert.AreEqual(0, Events(w, SimEventType.BerserkLocked).Count);
            SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Corruption, units - half, 5);
            Assert.AreEqual(true, w.Match.BerserkLocked);
            List<SimEvent> ev = Events(w, SimEventType.BerserkLocked);
            Assert.AreEqual(1, ev.Count);
            Assert.AreEqual(w.Map.Shrine.X, ev[0].X);
            Assert.AreEqual(w.Map.Shrine.Z, ev[0].Z);
            Assert.GreaterOrEqual(ev[0].Value, feed.BerserkThreshold);
            // More corruption, the kaiju system's own check and the hatch never raise a second one.
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Corruption, 10, 1);
            for (int t = 0; t < 30; t++) SimStep.Step(w, dt);
            w.ForceHatch = true;
            SimStep.Step(w, dt);
            Assert.AreEqual(KaijuState.Berserk, w.Kaiju.State);
            Assert.AreEqual(1, Events(w, SimEventType.BerserkLocked).Count);
        }

        [Test]
        public void BerserkLockedTheDebugPlusInstabilityPathAndLockBerserkRaiseItOnceResetWorldReArmsIt()
        {
            World w = MakeWorld();
            KaijuFeedConfig feed = w.Config.KaijuFeed;
            double units = System.Math.Ceiling(feed.BerserkThreshold / feed.CorruptionInstabilityPerUnit);
            SimDebug.DebugAddFeed(w, null, 0, units);
            Assert.AreEqual(true, w.Match.BerserkLocked);
            Assert.AreEqual(1, Events(w, SimEventType.BerserkLocked).Count);
            Assert.AreEqual(false, SimResources.LockBerserk(w));
            SimDebug.DebugAddFeed(w, null, 0, units);
            Assert.AreEqual(1, Events(w, SimEventType.BerserkLocked).Count);
            World.ResetWorld(w, Opts(ClassId.Warrior, 11));
            Assert.AreEqual(false, w.Match.BerserkLocked);
            Assert.AreEqual(true, SimResources.LockBerserk(w));
            Assert.AreEqual(1, Events(w, SimEventType.BerserkLocked).Count);
        }

        [Test]
        public void BerserkLockedExactlyOnePerMatchInAFullDeterministicCorruptionFedRun()
        {
            World w = MakeWorld(ClassId.Farmer, 1);
            double dt = 1 / w.Config.SimHz;
            // Crimson force-feeds corruption every 2 s of sim time until the hatch.
            double total = JsMath.Round(w.Config.HatchSeconds * w.Config.SimHz) + 30;
            int raised = 0;
            int head = w.EventHead;
            for (int t = 0; t < total; t++)
            {
                if (t % 60 == 0) SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Corruption, 2, 5);
                SimStep.Step(w, dt);
                for (int h = head; h < w.EventHead; h++)
                    if (w.Events[h % World.EventCapacity].Type == SimEventType.BerserkLocked) raised++;
                head = w.EventHead;
            }
            Assert.AreEqual(true, w.Match.BerserkLocked);
            Assert.AreEqual(1, raised);
        }
    }
}
