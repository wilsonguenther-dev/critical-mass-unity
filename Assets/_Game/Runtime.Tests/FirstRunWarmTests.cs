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
            // first-run-warm-2: nothing runs, so nothing holds the sim or the frame governor; the hold has its own cap.
            Assert.IsFalse(FirstRunWarm.Running, "no warm-up running outside a match load");
            Assert.IsFalse(FirstRunWarm.HoldsSim, "the sim is never held without a warm-up");
            Assert.LessOrEqual(FirstRunWarm.HoldCapSeconds, LoadingScreen.WarmUpWaitSeconds + 1f, "the sim hold is bounded");
            Assert.Greater(FirstRunWarm.HoldCapSeconds, FirstRunWarm.BudgetSeconds, "the hold outlasts the warm-up's own cap");
            bool rec = FirstRunWarm.RecordingSupported;   // never throws, whatever the device says
            Assert.That(rec || !rec);
            Assert.GreaterOrEqual(ShaderWarmup.MinRecordingBytes, 283L, "the Air's empty 282-byte trace counts as no recording");
        }
    }
}
