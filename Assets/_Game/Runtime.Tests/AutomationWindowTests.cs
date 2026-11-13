using CriticalMass.Runtime.Game;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// Automated runs hide their own window (owner report 2026-11-13: a demo-smoke window, then a fullscreen one, on his
    /// screen). The decision is pure flag parsing: any automation flag hides, -showWindow is the only way to watch.
    /// </summary>
    public sealed class AutomationWindowTests
    {
        [Test]
        public void EveryAutomationFlagHidesTheWindow()
        {
            foreach (string flag in AutomationWindow.AutomationFlags)
                Assert.IsTrue(AutomationWindow.ShouldHide(new[] { "/x/Critical Mass", flag }), flag + " must hide the window");
        }

        [Test]
        public void TheDemoSmokeAndSoakCommandLinesHide()
        {
            // tools/demo-smoke.sh and tools/player-soak.sh (the soak asks for borderless fullscreen: still hidden).
            Assert.IsTrue(AutomationWindow.ShouldHide(new[] { "Critical Mass", "-autoplay", "-quitAfterMatch", "-autoplayMatches", "3",
                "-quality", "High", "-dataDir", "/tmp/d", "-screen-fullscreen", "0", "-screen-width", "1920", "-screen-height", "1080" }));
            Assert.IsTrue(AutomationWindow.ShouldHide(new[] { "Critical Mass", "-AUTOPLAY", "-screen-fullscreen", "1", "-window-mode", "borderless" }));
            Assert.IsTrue(AutomationWindow.ShouldHide(new[] { "Critical Mass", "  -perfLog ", "/tmp/p.json" }), "case and spaces are ignored");
        }

        [Test]
        public void ShowWindowIsTheExplicitOptIn()
        {
            Assert.IsFalse(AutomationWindow.ShouldHide(new[] { "Critical Mass", "-autoplay", "-showWindow" }));
            Assert.IsFalse(AutomationWindow.ShouldHide(new[] { "Critical Mass", "-showwindow", "-autoplay", "-quitAfterMatch" }));
        }

        [Test]
        public void APersonsGameIsNeverHidden()
        {
            Assert.IsFalse(AutomationWindow.ShouldHide(null));
            Assert.IsFalse(AutomationWindow.ShouldHide(new string[0]));
            Assert.IsFalse(AutomationWindow.ShouldHide(new[] { "/Applications/Critical Mass.app/Contents/MacOS/Critical Mass" }));
            Assert.IsFalse(AutomationWindow.ShouldHide(new[] { "Critical Mass", "-quality", "Low", "-safe", "-screen-fullscreen", "1", "-novsync" }));
            Assert.IsFalse(AutomationWindow.ShouldHide(new[] { "Critical Mass", "-autoplayish", "autoplay", null }), "only whole flags count");
        }

        [Test]
        public void TheEditorNeverHides()
        {
            Assert.IsFalse(AutomationWindow.Active, "the hiding is for player builds only (editor, batch mode and tests are untouched)");
        }
    }
}
