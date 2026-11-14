using CriticalMass.Runtime.Game;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>feat/flow-capture-1: the -flowShots flag hides the run like every capture flag; -soundVolume parses and
    /// keeps the sound on; the audio cue log names its cues.</summary>
    public sealed class FlowShotsTests
    {
        [Test]
        public void FlowShotsIsAnAutomationFlagAndFoundCaseInsensitively()
        {
            CollectionAssert.Contains(AutomationWindow.AutomationFlags, FlowShots.Flag);
            Assert.IsTrue(AutoplayArgs.HasAutomationFlag(new[] { "Game", "-flowShots" }, FlowShots.Flag));
            Assert.IsFalse(AutoplayArgs.HasAutomationFlag(new[] { "Game" }, FlowShots.Flag));
            Assert.IsTrue(AutomationWindow.ShouldHide(new[] { "Game", "-flowShots" }), "a flow capture run stays hidden");
            Assert.AreEqual(FlowShots.Flag, FlowShots.Flag.ToLowerInvariant(), "flags are matched lower case");
        }

        [Test]
        public void SoundVolumeParsesClampsAndIsAbsentByDefault()
        {
            Assert.AreEqual(-1f, AutomationWindow.SoundVolume(new[] { "Game", "-flowShots" }), "absent: the run stays muted");
            Assert.AreEqual(0.6f, AutomationWindow.SoundVolume(new[] { "Game", "-soundVolume", "0.6" }), 1e-6f);
            Assert.AreEqual(1f, AutomationWindow.SoundVolume(new[] { "-SOUNDVOLUME", "7" }), 1e-6f, "clamped to 1");
            Assert.AreEqual(0f, AutomationWindow.SoundVolume(new[] { "-soundvolume", "-2" }), 1e-6f, "clamped to 0");
            Assert.AreEqual(-1f, AutomationWindow.SoundVolume(new[] { "-soundvolume", "loud" }), "no number: absent");
            Assert.AreEqual(-1f, AutomationWindow.SoundVolume(new[] { "-soundvolume" }), "no value: absent");
            Assert.IsFalse(AutomationWindow.ShouldHide(new[] { "Game", "-soundVolume", "0.5" }), "on its own it is not an automation run");
        }

        [Test]
        public void TheCueLogSaysWhichKindOfSoundStarted()
        {
            Assert.AreEqual("music", AudioCueLog.Kind("CM Audio Music", "title-theme"));
            Assert.AreEqual("music", AudioCueLog.Kind("Sting", "victory"));
            Assert.AreEqual("voice", AudioCueLog.Kind("Announcer", "hill-captured"));
            Assert.AreEqual("ui", AudioCueLog.Kind("UI Sound", "click"));
            Assert.AreEqual("sound", AudioCueLog.Kind("CM Sound Effects", "hit-heavy"));
            Assert.AreEqual("sound", AudioCueLog.Kind(null, null));
        }
    }
}
