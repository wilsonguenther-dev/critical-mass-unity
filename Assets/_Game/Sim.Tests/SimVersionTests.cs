using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    public class SimVersionTests
    {
        /// <summary>b15 comeback + hut choices bumped the schema 1 -> 2 (RALLY credit and the hut vote are hashed sim state).</summary>
        [Test]
        public void SchemaIsTwo()
        {
            System.Console.WriteLine("{\"item\":\"b15 sim schema\",\"schema\":" + SimVersion.Schema + ",\"expected\":2}");
            Assert.AreEqual(2, SimVersion.Schema);
        }
    }
}
