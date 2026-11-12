using System;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of src/sim/spatialHash.test.ts.</summary>
    [TestFixture]
    public sealed class SpatialHashTests
    {
        /// <summary>The browser builds these arrays as Float32Array: every stored value is f32-rounded.</summary>
        private static double[] F32Array(params double[] values)
        {
            double[] a = new double[values.Length];
            for (int i = 0; i < values.Length; i++) a[i] = JsMath.F32(values[i]);
            return a;
        }

        [Test]
        public void ReturnsExactlyTheLivingPointsInsideTheCircle()
        {
            SpatialHash h = SpatialHash.CreateSpatialHash(112, 72, 8);
            double[] xs = F32Array(10, 11, 30, 10.5, 0, 111.9, 0, 0);
            double[] zs = F32Array(10, 10, 30, 10.2, 0, 71.9, 0, 0);
            bool[] flags = { true, true, true, false, true, true, true, true };
            Actor[] alive = new Actor[flags.Length];
            for (int i = 0; i < flags.Length; i++) alive[i] = new Actor { Alive = flags[i] };
            SpatialHash.Rebuild(h, xs, zs, alive, 6);
            int[] output = new int[8];
            int n = SpatialHash.QueryCircle(h, 10, 10, 1.5, output);
            int[] hits = new int[n];
            Array.Copy(output, hits, n);
            Array.Sort(hits);
            CollectionAssert.AreEqual(new[] { 0, 1 }, hits);
            Assert.AreEqual(1, SpatialHash.QueryCircle(h, 111, 71, 2, output));
            Assert.AreEqual(5, output[0]);
        }
    }
}
