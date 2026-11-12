using CriticalMass.Sim.Core;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>The soak runner is deterministic (U-034: new, no vitest twin; default EditMode gate, not Slow).</summary>
    [TestFixture]
    public sealed class OutcomeRunnerTests
    {
        private static void Same(double a, double b, string field)
        {
            Assert.IsTrue(a == b, field + ": " + a.ToString("R") + " != " + b.ToString("R"));
        }

        private static void SamePerTeam(PerTeam a, PerTeam b, string field)
        {
            if (a == null || b == null)
            {
                Assert.IsTrue(a == null && b == null, field + ": one run null, the other not");
                return;
            }
            Same(a.Azure, b.Azure, field + ".Azure");
            Same(a.Crimson, b.Crimson, field + ".Crimson");
        }

        [Test]
        public void SameSeedTwiceGivesTheSameOutcomeInEveryField()
        {
            MatchOutcome x = OutcomeRunner.RunOutcomeMatch(1, 5, HumanMode.Bot);
            MatchOutcome y = OutcomeRunner.RunOutcomeMatch(1, 5, HumanMode.Bot);
            Assert.AreEqual(x.Seed, y.Seed, "Seed");
            Assert.AreEqual(x.TeamSize, y.TeamSize, "TeamSize");
            Assert.AreEqual(x.DriveHuman, y.DriveHuman, "DriveHuman");
            Assert.AreEqual(x.Human, y.Human, "Human");
            Assert.AreEqual(x.Winner, y.Winner, "Winner");
            SamePerTeam(x.HillSeconds, y.HillSeconds, "HillSeconds");
            Same(x.OwnedShare, y.OwnedShare, "OwnedShare");
            Same(x.ContestedShare, y.ContestedShare, "ContestedShare");
            Assert.AreEqual(x.OwnerChanges, y.OwnerChanges, "OwnerChanges");
            Assert.AreEqual(x.Hatch.HasValue, y.Hatch.HasValue, "Hatch null alike");
            if (x.Hatch.HasValue) Assert.AreEqual(x.Hatch.Value, y.Hatch.Value, "Hatch");
            Same(x.KaijuDuty, y.KaijuDuty, "KaijuDuty");
            SamePerTeam(x.Allegiance, y.Allegiance, "Allegiance");
            SamePerTeam(x.DeliveredBiomass, y.DeliveredBiomass, "DeliveredBiomass");
            SamePerTeam(x.DeliveredCorruption, y.DeliveredCorruption, "DeliveredCorruption");
            SamePerTeam(x.Kills, y.Kills, "Kills");
            SamePerTeam(x.HillAtHatch, y.HillAtHatch, "HillAtHatch");
            Assert.AreEqual(x.KaijuKOs, y.KaijuKOs, "KaijuKOs");
        }
    }
}
