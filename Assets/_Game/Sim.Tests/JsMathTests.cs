using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>JsMath against JS Math.round and Math.fround (values from node 22). No vitest twin.</summary>
    [TestFixture]
    public sealed class JsMathTests
    {
        [Test]
        public void RoundMatchesJsMathRoundAtTheEdgesAndF32MatchesMathFround()
        {
            Assert.AreEqual(0.0, JsMath.Round(0.49999999999999994));
            Assert.AreEqual(1.0, JsMath.Round(0.5));
            Assert.AreEqual(0.0, JsMath.Round(-0.5));
            Assert.AreEqual(3.0, JsMath.Round(2.5));
            Assert.AreEqual(-2.0, JsMath.Round(-2.5));
            Assert.AreEqual(4503599627370497.0, JsMath.Round(4503599627370497.0));

            Assert.AreEqual(0.10000000149011612, JsMath.F32(0.1));
            Assert.AreEqual(1.0000001192092896, JsMath.F32(1.0000001));
            Assert.AreEqual(16777216.0, JsMath.F32(16777217.0));
            Assert.IsTrue(double.IsPositiveInfinity(JsMath.F32(1e39)));
            Assert.IsTrue(double.IsNaN(JsMath.F32(double.NaN)));
        }
    }
}
