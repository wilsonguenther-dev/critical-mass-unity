using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    public class SimVersionTests
    {
        [Test]
        public void SchemaIsOne()
        {
            Assert.AreEqual(1, SimVersion.Schema);
        }
    }
}
