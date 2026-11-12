using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of src/sim/match.test.ts (09e0904): decideWinner, matchSystem, matchClock.</summary>
    [TestFixture]
    public sealed class MatchTests
    {
        private static WorldOptions Opts()
        {
            return new WorldOptions
            {
                Seed = 1,
                TeamSize = 5,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Miner,
                Config = GameConfig.CreateBrowser5v5(),
            };
        }

        private static void HoldHill(World w, int[] ids)
        {
            foreach (int id in ids)
            {
                w.X[id] = w.Map.Hill.X;
                w.Z[id] = w.Map.Hill.Z;
                w.PrevX[id] = w.X[id];
                w.PrevZ[id] = w.Z[id];
            }
        }

        /// <summary>Every field of MatchState as a flat value list (browser JSON.parse(JSON.stringify(w.match))).</summary>
        private static List<object> Snapshot(MatchState m)
        {
            var s = new List<object>
            {
                m.Time, m.Phase,
                m.HillSeconds.Azure, m.HillSeconds.Crimson,
                m.HillState,
                m.HillCount.Azure, m.HillCount.Crimson,
                m.Allegiance.Azure, m.Allegiance.Crimson,
                m.Delivered.Length,
            };
            foreach (DeliveredTally d in m.Delivered)
            {
                s.Add(d.Biomass);
                s.Add(d.Corruption);
            }
            s.Add(m.Instability);
            s.Add(m.BerserkLocked);
            s.Add(m.Hatched);
            s.Add(m.Winner);
            return s;
        }

        [Test]
        public void DrawWithinEpsilonOtherwiseTheLargerTotal()
        {
            Assert.AreEqual(MatchResult.Draw, Match.DecideWinner(100, 99.95, 0.1));
            Assert.AreEqual(MatchResult.Azure, Match.DecideWinner(100, 99.8, 0.1));
            Assert.AreEqual(MatchResult.Crimson, Match.DecideWinner(10, 20, 0.1));
        }

        [Test]
        public void DoesNothingBeforeTheFinalStep()
        {
            World w = World.CreateWorld(Opts());
            w.Match.Time = 100;
            Match.MatchSystem(w, SimStep.Dt(w.Config));
            Assert.AreEqual(MatchPhase.Playing, w.Match.Phase);
            Assert.AreEqual(MatchResult.None, w.Match.Winner);
        }

        [Test]
        public void EndsAsADrawWithEqualHillTimeAndPushesMatchEndWithValueDiff()
        {
            World w = World.CreateWorld(Opts());
            double dt = SimStep.Dt(w.Config);
            w.Match.Time = w.Config.MatchSeconds - dt;
            w.Match.HillSeconds.Azure = 50;
            w.Match.HillSeconds.Crimson = 50;
            int baseHead = w.EventHead;
            Match.MatchSystem(w, dt);
            Assert.AreEqual(MatchPhase.Ended, w.Match.Phase);
            Assert.AreEqual(MatchResult.Draw, w.Match.Winner);
            Assert.AreEqual(baseHead + 1, w.EventHead);
            SimEvent e = w.Events[baseHead % w.Events.Length];
            Assert.AreEqual(SimEventType.MatchEnd, e.Type);
            Assert.AreEqual(0.0, e.Value);
        }

        [Test]
        public void FullMatchAzureHoldsTheHillEndsAt480SWithWinnerAzureThenStepIsInert()
        {
            World w = World.CreateWorld(Opts());
            double dt = SimStep.Dt(w.Config);
            int total = (int)(w.Config.MatchSeconds * w.Config.SimHz) + 5;
            int[] holders = { 0, 1 };
            for (int i = 0; i < total; i++)
            {
                HoldHill(w, holders);
                SimStep.Step(w);
            }
            Assert.AreEqual(MatchPhase.Ended, w.Match.Phase);
            Assert.AreEqual(MatchResult.Azure, w.Match.Winner);
            Assert.LessOrEqual(Math.Abs(w.Match.Time - w.Config.MatchSeconds), dt + 1e-9);
            Assert.Greater(w.Match.HillSeconds.Azure, 400.0);
            List<object> snapshot = Snapshot(w.Match);
            int tick = w.Tick;
            for (int i = 0; i < 10; i++)
            {
                HoldHill(w, holders);
                SimStep.Step(w);
            }
            CollectionAssert.AreEqual(snapshot, Snapshot(w.Match));
            Assert.AreEqual(tick, w.Tick);
        }

        [Test]
        public void ReportsElapsedRemainingAndTimeUntilHatchClampedAt0()
        {
            World w = World.CreateWorld(Opts());
            w.Match.Time = 100;
            MatchClock c0 = Match.GetMatchClock(w);
            Assert.AreEqual(100.0, c0.Elapsed);
            Assert.AreEqual(380.0, c0.Remaining);
            Assert.AreEqual(140.0, c0.UntilHatch);
            w.Match.Time = 500;
            MatchClock c = Match.GetMatchClock(w);
            Assert.AreEqual(0.0, c.Remaining);
            Assert.AreEqual(0.0, c.UntilHatch);
            Assert.AreEqual(500.0, c.Elapsed);
        }
    }
}
