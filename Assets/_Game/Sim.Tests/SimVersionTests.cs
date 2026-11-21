using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    public class SimVersionTests
    {
        /// <summary>b15-rejoin took the schema to 2 and b15 terrain height to 3 (MapDef.Lift is sim state; older replays refuse to play); batch 15 wave two to 4.</summary>
        [Test]
        public void SchemaIsAtLeastFour()
        {
            // b15-rejoin: the schema now follows the rules (Net/SimVersionTests pins RulesDigest and says when to bump);
            // b15 terrain height took it to 3, batch 15 wave two to 4.
            System.Console.WriteLine("{\"item\":\"b15 sim schema\",\"schema\":" + SimVersion.Schema + ",\"expected\":4}");
            Assert.GreaterOrEqual(SimVersion.Schema, 4);
        }
    }
}
