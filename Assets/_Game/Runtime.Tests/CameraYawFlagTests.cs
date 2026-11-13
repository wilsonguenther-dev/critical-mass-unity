using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>-cameraYaw launch flag (camera-orientation grabs, 2026-11-13): honoured only with an automation flag.</summary>
    public sealed class CameraYawFlagTests
    {
        [Test]
        public void CameraYawWithAnAutomationFlagSetsTheYaw()
        {
            Assert.IsTrue(AutoplayArgs.TryCameraYaw(new[] { "Critical Mass", "-autoplay", "-cameraYaw", "0" }, out double y));
            Assert.AreEqual(0d, y);
            Assert.IsTrue(AutoplayArgs.TryCameraYaw(new[] { "-cameraYaw", "45", "-shots" }, out y));
            Assert.AreEqual(45d, y);
        }

        [Test]
        public void CameraYawWithoutAnAutomationFlagIsIgnored()
        {
            Assert.IsFalse(AutoplayArgs.TryCameraYaw(new[] { "Critical Mass", "-cameraYaw", "0" }, out _));
            Assert.IsFalse(AutoplayArgs.TryCameraYaw(new[] { "-showWindow", "-cameraYaw", "10" }, out _));
            Assert.IsFalse(AutoplayArgs.TryCameraYaw(null, out _));
        }

        [Test]
        public void CameraYawIsNormalisedAndBadValuesAreIgnored()
        {
            Assert.IsTrue(AutoplayArgs.TryCameraYaw(new[] { "-autoplay", "-cameraYaw", "-90" }, out double y));
            Assert.AreEqual(270d, y, 1e-9);
            Assert.IsTrue(AutoplayArgs.TryCameraYaw(new[] { "-autoplay", "-cameraYaw", "405" }, out y));
            Assert.AreEqual(45d, y, 1e-9);
            Assert.IsFalse(AutoplayArgs.TryCameraYaw(new[] { "-autoplay", "-cameraYaw", "north" }, out _));
            Assert.IsFalse(AutoplayArgs.TryCameraYaw(new[] { "-autoplay", "-cameraYaw" }, out _));
        }

        [Test]
        public void TheDefaultYawStays45()
        {
            Assert.AreEqual(45d, new RenderConfig().CameraYawDeg);
        }
    }
}
