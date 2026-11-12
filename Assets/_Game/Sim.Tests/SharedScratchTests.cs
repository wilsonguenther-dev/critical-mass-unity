using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// Batch-1 review finding R20261112T1705-1-5 (U-047). FlowField (Heap, HeapPos, HeapKey, Closed, HeapSize) and World
    /// (RosterCounts, RosterFrac, RosterScratch) keep process-wide static scratch, as the browser keeps module scope.
    /// These tests prove the scratch carries nothing from one call to the next under sequential interleaving, the only
    /// interleaving the one-thread rule (conventions 6.10, U-047) allows. New, no vitest twin.
    /// </summary>
    [TestFixture]
    public sealed class SharedScratchTests
    {
        private static PointXZ[] CellGoal(double x, double z)
        {
            return new[] { new PointXZ(Math.Floor(x) + 0.5, Math.Floor(z) + 0.5) };
        }

        private static void AssertBitsEqual(double[] expected, double[] actual, string what)
        {
            Assert.AreEqual(expected.Length, actual.Length, what + ": length");
            for (int i = 0; i < expected.Length; i++)
            {
                long e = BitConverter.DoubleToInt64Bits(expected[i]);
                long a = BitConverter.DoubleToInt64Bits(actual[i]);
                if (e != a) Assert.Fail(what + ": cell " + i + " alone " + expected[i].ToString("R") + ", alternate " + actual[i].ToString("R"));
            }
        }

        [Test]
        public void TwoFlowFieldsComputedAlternatelyEqualEachComputedAlone()
        {
            MapDef map = SkybreakBasinMap.BuildSkybreakBasin(GameConfig.CreateBrowser5v5());
            PointXZ[] hill = CellGoal(map.Hill.X, map.Hill.Z);
            PadDef azure = map.Pads[(int)TeamId.Azure];
            PointXZ[] pad = CellGoal(azure.X, azure.Z);

            // Alone: each goal set in its own fresh call.
            double[] aloneH = (double[])FlowField.ComputeFlowField(map, hill).Dist.Clone();
            double[] aloneP = (double[])FlowField.ComputeFlowField(map, pad).Dist.Clone();

            // The two fields differ, and each goal cell is reached (the comparison is not vacuous).
            int hillCell = MapGrid.CellIndex(map, (int)Math.Floor(hill[0].X), (int)Math.Floor(hill[0].Z));
            int padCell = MapGrid.CellIndex(map, (int)Math.Floor(pad[0].X), (int)Math.Floor(pad[0].Z));
            Assert.AreEqual(0.0, aloneH[hillCell], "hill goal cell distance");
            Assert.AreEqual(0.0, aloneP[padCell], "Azure pad goal cell distance");
            Assert.Greater(aloneH[padCell], 0.0, "hill field at the pad cell");
            Assert.IsFalse(double.IsInfinity(aloneH[padCell]), "the pad is reachable from the hill");

            // Alternate: H, P, H, P, a new output each call.
            FlowField h1 = FlowField.ComputeFlowField(map, hill);
            FlowField p1 = FlowField.ComputeFlowField(map, pad);
            FlowField h2 = FlowField.ComputeFlowField(map, hill);
            FlowField p2 = FlowField.ComputeFlowField(map, pad);
            Assert.AreNotSame(h1.Dist, h2.Dist);
            Assert.AreNotSame(p1.Dist, p2.Dist);

            AssertBitsEqual(aloneH, h1.Dist, "alternate call 1 (hill)");
            AssertBitsEqual(aloneP, p1.Dist, "alternate call 2 (Azure pad)");
            AssertBitsEqual(aloneH, h2.Dist, "alternate call 3 (hill)");
            AssertBitsEqual(aloneP, p2.Dist, "alternate call 4 (Azure pad)");
        }

        private static WorldOptions Opts(int seed, int teamSize)
        {
            return new WorldOptions
            {
                Seed = seed,
                TeamSize = teamSize,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Miner,
                Config = GameConfig.CreateBrowser5v5(),
            };
        }

        /// <summary>X, Z, Facing, Cls, Team of every actor, then Rng.State, as bit patterns.</summary>
        private static long[] Snapshot(World w)
        {
            var s = new List<long> { w.ActorCount };
            for (int i = 0; i < w.ActorCount; i++)
            {
                s.Add(BitConverter.DoubleToInt64Bits(w.X[i]));
                s.Add(BitConverter.DoubleToInt64Bits(w.Z[i]));
                s.Add(BitConverter.DoubleToInt64Bits(w.Actors[i].Facing));
                s.Add((long)w.Actors[i].Cls);
                s.Add((long)w.Actors[i].Team);
            }
            s.Add(w.Rng.State);
            return s.ToArray();
        }

        [Test]
        public void TwoWorldsCreatedAndResetAlternatelyEqualEachAlone()
        {
            WorldOptions optsA = Opts(1, 5);
            WorldOptions optsB = Opts(2, 16);

            // Alone: each world created and reset twice on its own.
            World aloneA = World.CreateWorld(optsA);
            World.ResetWorld(aloneA, optsA);
            World.ResetWorld(aloneA, optsA);
            long[] snapA = Snapshot(aloneA);

            World aloneB = World.CreateWorld(optsB);
            World.ResetWorld(aloneB, optsB);
            World.ResetWorld(aloneB, optsB);
            long[] snapB = Snapshot(aloneB);

            // The two worlds differ (10 actors against 32), so the comparison is not vacuous.
            Assert.AreEqual(10, aloneA.ActorCount);
            Assert.AreEqual(32, aloneB.ActorCount);

            // Alternate: create A, create B, reset A, reset B, reset A, reset B.
            World a = World.CreateWorld(optsA);
            World b = World.CreateWorld(optsB);
            World.ResetWorld(a, optsA);
            World.ResetWorld(b, optsB);
            World.ResetWorld(a, optsA);
            World.ResetWorld(b, optsB);

            CollectionAssert.AreEqual(snapA, Snapshot(a), "world A (Seed 1, 5v5) after alternating with B");
            CollectionAssert.AreEqual(snapB, Snapshot(b), "world B (Seed 2, 16v16) after alternating with A");
        }
    }
}
