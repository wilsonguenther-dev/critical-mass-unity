using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    public class SimVersionTests
    {
        [Test]
        public void SchemaIsAtLeastTwo()
        {
            // b15-rejoin: the schema now follows the rules (Net/SimVersionTests pins RulesDigest and says when to bump).
            Assert.GreaterOrEqual(SimVersion.Schema, 2);
        }
    }
}
