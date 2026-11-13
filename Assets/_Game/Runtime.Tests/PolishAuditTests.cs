using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// polish-audit-1 (2026-11-13): the pure rules behind the first fixes of the flow, feel, smoothness and Windows audit.
    /// The splash check lives with the other player-settings checks (TechHardeningTests).
    /// </summary>
    public sealed class PolishAuditTests
    {
        // ── Pause while the loading screen covers a match ──

        [Test]
        public void TheFlowTakesThePausePressWhileTheLoadingScreenIsUp()
        {
            Assert.IsTrue(FlowDirector.FlowOwnsPause(FlowState.Match, false, true), "loading over a match: Esc is swallowed, no hidden pause menu");
            Assert.IsTrue(FlowDirector.FlowOwnsPause(FlowState.Off, false, true));
            Assert.IsFalse(FlowDirector.FlowOwnsPause(FlowState.Match, false, false), "a live match: the pause menu takes it");
            Assert.IsFalse(FlowDirector.FlowOwnsPause(FlowState.Off, false, false));
            foreach (FlowState s in new[] { FlowState.Title, FlowState.Menu, FlowState.Setup, FlowState.Results })
            {
                Assert.IsTrue(FlowDirector.FlowOwnsPause(s, false, false), s + ": the flow screen's Back");
                Assert.IsTrue(FlowDirector.FlowOwnsPause(s, false, true), s + " with loading up");
                Assert.IsFalse(FlowDirector.FlowOwnsPause(s, true, false), s + ": the settings bridge leaves it to the pause menu");
            }
            Assert.IsFalse(FlowDirector.FlowOwnsPause(FlowState.Match, true, true), "the settings bridge always wins");
        }

        // ── Home QUIT needs a second press ──

        [Test]
        public void HomeQuitOnlyQuitsOnASecondPressInsideTheWindow()
        {
            Assert.IsFalse(Menus.MainMenu.QuitArmed(float.NegativeInfinity, 10f), "never armed: the first press only arms");
            Assert.IsTrue(Menus.MainMenu.QuitArmed(10f, 10.5f));
            Assert.IsTrue(Menus.MainMenu.QuitArmed(10f, 10f + Menus.MainMenu.QuitConfirmSeconds));
            Assert.IsFalse(Menus.MainMenu.QuitArmed(10f, 10.01f + Menus.MainMenu.QuitConfirmSeconds), "too late: arms again");
            Assert.IsFalse(Menus.MainMenu.QuitArmed(10f, 9f), "a clock before the arm never counts");
            Assert.LessOrEqual(Menus.MainMenu.QuitConfirmLabel.Length, "HOW TO PLAY".Length + 1, "the armed label is no longer than the rail's longest line");
        }

        // ── Alt+Enter and the OS full-screen control ──

        [Test]
        public void AWindowMadeFromFullScreenGetsTwoThirdsOfTheDisplayAt16By9()
        {
            Assert.AreEqual((1280, 720), FramePolicy.WindowAfterExternalToggle(1920, 1080, 1920, 1080), "1080p: two thirds is exactly 1280x720");
            Assert.AreEqual((1707, 960), FramePolicy.WindowAfterExternalToggle(2560, 1440, 2560, 1440));
            Assert.AreEqual((2560, 1440), FramePolicy.WindowAfterExternalToggle(3840, 2160, 3840, 2160), "4K");
            Assert.AreEqual((1707, 960), FramePolicy.WindowAfterExternalToggle(3440, 1440, 3440, 1440), "ultrawide: height decides, 16:9 kept");
            Assert.AreEqual((1280, 720), FramePolicy.WindowAfterExternalToggle(1366, 768, 1366, 768), "small laptop: 1280x720 still fits");
            (int w, int h) = FramePolicy.WindowAfterExternalToggle(1024, 600, 1024, 600);
            Assert.LessOrEqual(w, 1024, "never wider than the display");
            Assert.LessOrEqual(h, 600, "never taller than the display");
        }

        [Test]
        public void AWindowAlreadySmallerThanTheDisplayIsLeftAlone()
        {
            Assert.AreEqual((0, 0), FramePolicy.WindowAfterExternalToggle(1280, 720, 1920, 1080));
            Assert.AreEqual((0, 0), FramePolicy.WindowAfterExternalToggle(1600, 900, 1920, 1080));
            Assert.AreEqual((0, 0), FramePolicy.WindowAfterExternalToggle(1920, 1080, 0, 0), "unknown display: keep it");
        }

        [Test]
        public void OnlyAModeChangeTheGameDidNotAskForIsStoredAsThePlayersChoice()
        {
            Assert.IsFalse(FramePolicy.IsExternalModeChange(false, false, 99), "no change");
            Assert.IsTrue(FramePolicy.IsExternalModeChange(true, false, 0.5), "a mode the game did not request: Alt+Enter");
            Assert.IsFalse(FramePolicy.IsExternalModeChange(true, true, 0.5), "the game's own request, just now");
            Assert.IsFalse(FramePolicy.IsExternalModeChange(true, true, 3.0));
            Assert.IsTrue(FramePolicy.IsExternalModeChange(true, true, 30), "the same mode again much later: the player toggled back");
            Assert.IsTrue(FramePolicy.IsExternalModeChange(true, true, double.NegativeInfinity), "the game never requested one");
        }
    }
}
