using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>SimEnumMap (U-047): team values map by name, never by integer. No vitest twin.</summary>
    [TestFixture]
    public sealed class SimEnumMapTests
    {
        [Test]
        public void AzureAndCrimsonMapToTheSameNamedMemberInEveryEnum()
        {
            foreach (TeamId t in new[] { TeamId.Azure, TeamId.Crimson })
            {
                Assert.AreEqual(t.ToString(), SimEnumMap.ToMatchResult(t).ToString());
                Assert.AreEqual(t.ToString(), SimEnumMap.ToHillState(t).ToString());
                Assert.AreEqual(t.ToString(), SimEnumMap.ToKaijuState(t).ToString());
                Assert.AreEqual((TeamId?)t, SimEnumMap.TeamOf(SimEnumMap.ToMatchResult(t)));
                Assert.AreEqual((TeamId?)t, SimEnumMap.TeamOf(SimEnumMap.ToHillState(t)));
                Assert.AreEqual((TeamId?)t, SimEnumMap.TeamOf(SimEnumMap.ToKaijuState(t)));
            }

            Assert.IsNull(SimEnumMap.TeamOf(MatchResult.None));
            Assert.IsNull(SimEnumMap.TeamOf(MatchResult.Draw));
            Assert.IsNull(SimEnumMap.TeamOf(HillState.Neutral));
            Assert.IsNull(SimEnumMap.TeamOf(HillState.Contested));
            Assert.IsNull(SimEnumMap.TeamOf(KaijuState.Egg));
            Assert.IsNull(SimEnumMap.TeamOf(KaijuState.Neutral));
            Assert.IsNull(SimEnumMap.TeamOf(KaijuState.Berserk));
        }
    }
}
