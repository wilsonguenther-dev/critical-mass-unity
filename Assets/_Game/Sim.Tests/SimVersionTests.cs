using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    public class SimVersionTests
    {
        /// <summary>b15-rejoin took the schema to 2 and b15 terrain height to 3 (MapDef.Lift is sim state; older replays refuse to play).</summary>
        [Test]
        public void SchemaIsAtLeastThree()
        {
            // b15-rejoin: the schema now follows the rules (Net/SimVersionTests pins RulesDigest and says when to bump);
            // b15 terrain height took it to 3.
            System.Console.WriteLine("{\"item\":\"b15 sim schema\",\"schema\":" + SimVersion.Schema + ",\"expected\":3}");
            Assert.GreaterOrEqual(SimVersion.Schema, 3);
        }
    }
}
