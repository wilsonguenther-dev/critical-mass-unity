using CriticalMass.Runtime.Camera;
using NUnit.Framework;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>
    /// feat/camera-creator-angle: the tests written before the creator's angle describe the classic 45-degree heading
    /// (screen up = sim (-1, -1)/sqrt2 and so on), so every test in this namespace runs with it pinned; the creator's
    /// angle has its own tests (CameraHeadingTests), which pin 135 and restore.
    /// </summary>
    [SetUpFixture]
    public sealed class ClassicHeadingPin
    {
        [OneTimeSetUp] public void Pin() => CameraHeading.Pin = CameraHeading.Classic;
        [OneTimeTearDown] public void Unpin() => CameraHeading.Pin = null;
    }
}
