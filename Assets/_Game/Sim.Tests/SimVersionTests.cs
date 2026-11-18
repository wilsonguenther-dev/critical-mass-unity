using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    public class SimVersionTests
    {
        /// <summary>b15 terrain height bumped the schema 1 -> 2 (MapDef.Lift is sim state; older replays refuse to play).</summary>
        [Test]
        public void SchemaIsTwo()
        {
            System.Console.WriteLine("{\"item\":\"b15 sim schema\",\"schema\":" + SimVersion.Schema + ",\"expected\":2}");
            Assert.AreEqual(2, SimVersion.Schema);
        }
    }
}
