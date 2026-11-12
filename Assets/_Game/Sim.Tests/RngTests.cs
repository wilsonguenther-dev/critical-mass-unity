using System.Collections.Generic;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of src/sim/rng.test.ts (mulberry32).</summary>
    [TestFixture]
    public sealed class RngTests
    {
        [Test]
        public void SameSeedGivesTheSame1000Values()
        {
            Rng a = Rng.CreateRng(1337);
            Rng b = Rng.CreateRng(1337);
            for (int i = 0; i < 1000; i++) Assert.AreEqual(Rng.NextFloat(b), Rng.NextFloat(a));
            Assert.AreEqual(b.State, a.State);
        }

        [Test]
        public void DifferentSeedsGiveDifferentSequences()
        {
            Rng a = Rng.CreateRng(1);
            Rng b = Rng.CreateRng(2);
            int same = 0;
            for (int i = 0; i < 1000; i++) if (Rng.NextFloat(a) == Rng.NextFloat(b)) same++;
            Assert.Less(same, 5);
        }

        [Test]
        public void NextFloatIn01NextIntInMinMaxPickReturnsAnElement()
        {
            Rng r = Rng.CreateRng(42);
            IReadOnlyList<string> arr = new[] { "a", "b", "c" };
            for (int i = 0; i < 2000; i++)
            {
                double f = Rng.NextFloat(r);
                Assert.GreaterOrEqual(f, 0.0);
                Assert.Less(f, 1.0);
                int n = Rng.NextInt(r, -3, 4);
                Assert.GreaterOrEqual(n, -3);
                Assert.Less(n, 4);
                CollectionAssert.Contains(arr, Rng.Pick(r, arr));
            }
        }

        [Test]
        public void StateIsAPlainSerialisableNumber()
        {
            Rng r = Rng.CreateRng(7);
            Rng.NextFloat(r);
            Rng copy = new Rng { State = r.State };
            Assert.AreEqual(Rng.NextFloat(r), Rng.NextFloat(copy));
        }
    }
}
