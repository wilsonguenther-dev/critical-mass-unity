using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Menus;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>first-run-warm-1: the first-run warm-up stays inside the boot budget and never holds the loading screen.</summary>
    public sealed class FirstRunWarmTests
    {
        [Test]
        public void TheWarmUpFitsTheBootBudgetAndTheScreenNeverWaitsLonger()
        {
            Assert.LessOrEqual(FirstRunWarm.BudgetSeconds, 1.5f, "boot time growth under 1.5 s");
            Assert.LessOrEqual(FirstRunWarm.BudgetSeconds, LoadingScreen.WarmUpWaitSeconds, "the screen's own bound covers it");
            Assert.GreaterOrEqual(FirstRunWarm.WideFrames, 1);
            Assert.GreaterOrEqual(FirstRunWarm.FxFrames, 1);
            Assert.IsTrue(FirstRunWarm.Done, "no warm-up in progress outside a match load (EditMode: none installed)");
            Assert.AreEqual(1f, FirstRunWarm.Progress, 1e-6f);
            Assert.AreEqual(-1, FirstRunWarm.StatesAfterStart, "counter off");
        }
    }
}
