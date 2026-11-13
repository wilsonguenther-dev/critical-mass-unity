using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using NUnit.Framework;
using UnityEngine.Experimental.Rendering;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>DG-05 crash safety: frame cap, safe start, atomic files, the error-loop guard and the asset flags (pure parts).</summary>
    public sealed class FrameGovernorTests
    {
        [Test]
        public void CapIsAlwaysVsyncLockedAtTheNearestRateAtOrAboveTheCap()
        {
            Assert.AreEqual((1, -1), FrameGovernor.FrameSettings(60, 60));
            Assert.AreEqual((2, -1), FrameGovernor.FrameSettings(60, 120));
            Assert.AreEqual((1, -1), FrameGovernor.FrameSettings(120, 120));
            Assert.AreEqual((2, -1), FrameGovernor.FrameSettings(30, 60));
            Assert.AreEqual((2, -1), FrameGovernor.FrameSettings(60, 144), "perf-smooth-1: 72 Hz on a 144 Hz panel, locked, no tearing");
            Assert.AreEqual((2, -1), FrameGovernor.FrameSettings(60, 165), "82.5 Hz on 165");
            Assert.AreEqual((1, -1), FrameGovernor.FrameSettings(60, 75), "75 on 75");
            Assert.AreEqual((1, -1), FrameGovernor.FrameSettings(144, 144));
            Assert.AreEqual((1, -1), FrameGovernor.FrameSettings(144, 120), "a cap above the display runs at the display rate");
            Assert.AreEqual((1, -1), FrameGovernor.FrameSettings(120, 60), "a cap above the display runs at the display rate");
            Assert.AreEqual((1, -1), FrameGovernor.FrameSettings(120, 144));
            Assert.AreEqual((4, -1), FrameGovernor.FrameSettings(30, 240), "never more than 4");
            Assert.AreEqual((0, -1), FrameGovernor.FrameSettings(FramePolicy.Unlimited, 144), "UNLIMITED: vsync off, no target");
            Assert.AreEqual((1, -1), FrameGovernor.FrameSettings(0, 144), "0 = display rate");
            Assert.AreEqual((1, -1), FrameGovernor.FrameSettings(60, double.NaN), "unknown refresh counts as 60 Hz");
            Assert.AreEqual((1, -1), FrameGovernor.FrameSettings(60, 59.94));
        }

        [Test]
        public void PrefsAreSanitisedAndMenusNeverExceedSixty()
        {
            Assert.AreEqual(60, FrameGovernor.SanitiseCap(999));
            Assert.AreEqual(60, FrameGovernor.SanitiseCap(-2));
            Assert.AreEqual(FramePolicy.Unlimited, FrameGovernor.SanitiseCap(-1), "perf-smooth-1: UNLIMITED is a choice");
            Assert.AreEqual(144, FrameGovernor.SanitiseCap(144));
            Assert.AreEqual(60, FrameGovernor.EffectiveCap(FramePolicy.Unlimited, true), "menus never unlimited");
            Assert.AreEqual(60, FramePolicy.BackgroundCap(FramePolicy.Unlimited, false), "unfocused never unbounded");
            Assert.AreEqual(30, FramePolicy.TitleCapFor(FramePolicy.Unlimited));
            Assert.AreEqual(60, FramePolicy.EffectiveCap(FramePolicy.Unlimited, false, true), "battery caps unlimited at 60");
            Assert.AreEqual(30, FrameGovernor.SanitiseCap(30));
            Assert.AreEqual(0, FrameGovernor.SanitiseCap(0));
            Assert.AreEqual(60, FrameGovernor.EffectiveCap(120, true));
            Assert.AreEqual(60, FrameGovernor.EffectiveCap(0, true));
            Assert.AreEqual(30, FrameGovernor.EffectiveCap(30, true));
            Assert.AreEqual(120, FrameGovernor.EffectiveCap(120, false));
            Assert.AreEqual(0, FrameGovernor.EffectiveCap(0, false));
        }

        [Test]
        public void FirstLaunchPicksLowForIntegratedOrSmallMachines()
        {
            Assert.AreEqual((1, FrameGovernor.LowRenderScale, "integrated graphics"),
                FrameGovernor.PickQuality("Intel(R) UHD Graphics 620", 1536, 16384, 6, 3));
            Assert.AreEqual((1, FrameGovernor.LowRenderScale, "integrated graphics"),
                FrameGovernor.PickQuality("AMD Radeon(TM) Graphics", 512, 16384, 6, 3));
            Assert.AreEqual((2, FramePolicy.MidRenderScale, "8 GB memory"),
                FrameGovernor.PickQuality("NVIDIA GeForce GTX 1650", 4096, 6144, 6, 3), "DG-50: 8 GB or less is mid tier");
            Assert.AreEqual((1, FrameGovernor.LowRenderScale, "under 6 GB memory"),
                FrameGovernor.PickQuality("NVIDIA GeForce GTX 1650", 4096, 4096, 6, 3));
            Assert.AreEqual((3, 1f, "capable machine"), FrameGovernor.PickQuality("Apple M4 Pro", 18432, 24576, 6, 3));
            Assert.AreEqual((3, 1f, "capable machine"), FrameGovernor.PickQuality("NVIDIA GeForce RTX 3060", 12288, 16384, 6, 3));
            Assert.IsFalse(FrameGovernor.IsIntegrated("AMD Radeon RX Vega 64", 8192));
            Assert.IsTrue(FrameGovernor.IsIntegrated("Microsoft Basic Render Driver", 0));
        }

        [Test]
        public void WindowNeverExceedsTheNativeDisplay()
        {
            Assert.AreEqual((1920, 1080), FrameGovernor.ClampToNative(1920, 1080, 3024, 1964));
            Assert.AreEqual((1440, 810), FrameGovernor.ClampToNative(1920, 1080, 1440, 900));
            Assert.AreEqual((1280, 720), FrameGovernor.ClampToNative(1920, 1080, 1280, 720));
            Assert.AreEqual((1920, 1080), FrameGovernor.ClampToNative(1920, 1080, 0, 0), "unknown display: unchanged");
        }

        [Test]
        public void SavesAreAtomicVersionedAndFallBackToTheBackup()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cm-dg05-safefile-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string p = Path.Combine(dir, "machine-profile.json");
                Assert.IsNull(SafeFile.ReadVersioned(p, 1, out string why));
                Assert.AreEqual("missing", why);

                SafeFile.WriteAtomic(p, SafeFile.Versioned(1, "level=3\n"), true);
                SafeFile.WriteAtomic(p, SafeFile.Versioned(1, "level=1\n"), true);
                Assert.IsFalse(File.Exists(p + ".tmp"));
                Assert.AreEqual("level=1\n", SafeFile.ReadVersioned(p, 1, out why));
                Assert.IsNull(why);
                Assert.AreEqual("level=3\n", SafeFile.ReadVersioned(p + ".bak", 1, out _));

                File.WriteAllText(p, "\u0000garbage");   // corrupt: the backup is read instead
                Assert.AreEqual("level=3\n", SafeFile.ReadVersioned(p, 1, out why));
                Assert.IsNull(why);

                File.Delete(p + ".bak");
                Assert.IsNull(SafeFile.ReadVersioned(p, 1, out why), "corrupt and no backup: defaults");
                StringAssert.StartsWith("corrupt", why);

                File.WriteAllText(p, SafeFile.Versioned(2, "level=1\n"));
                Assert.IsNull(SafeFile.ReadVersioned(p, 1, out why), "another format version is not read as this one");
                StringAssert.Contains("format version 2", why);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void GuardFindsTheTypesInBothUnityStackFormats()
        {
            CollectionAssert.AreEqual(new[] { "CriticalMass.Runtime.View.ActorViews", "UnityEngine.Debug" },
                AutoplayArgs.StackTypeNames("CriticalMass.Runtime.View.ActorViews.LateUpdate () (at Assets/_Game/Runtime/View/ActorViews.cs:40)\n" +
                                            "UnityEngine.Debug:LogException(Exception)\n"));
            CollectionAssert.AreEqual(new[] { "CriticalMass.Runtime.Hud.GameHud" },
                AutoplayArgs.StackTypeNames("CriticalMass.Runtime.Hud.GameHud:Update()\n\n"));
            Assert.AreEqual(0, AutoplayArgs.StackTypeNames(null).Count);
        }

        [Test]
        public void MultiMatchFlagsAndPerMatchReportPaths()
        {
            AutoplayArgs.Options o = AutoplayArgs.Parse(new[] { "-autoplay", "-autoplayMatches", "3", "-autoplayTitleSeconds", "12" });
            Assert.AreEqual(3, o.Matches);
            Assert.AreEqual(12.0, o.TitleSeconds);
            AutoplayArgs.Options d = AutoplayArgs.Parse(new string[0]);
            Assert.AreEqual(1, d.Matches);
            Assert.AreEqual(1.0, d.TitleSeconds);
            Assert.AreEqual("/a/perf.json", AutoplayArgs.PerfPathFor("/a/perf.json", 1));
            Assert.AreEqual("/a/perf-m3.json", AutoplayArgs.PerfPathFor("/a/perf.json", 3));
        }

        [Test]
        public void TexturesOver2048OrUncompressedAreFlaggedRenderTargetsAreNot()
        {
            Assert.IsTrue(PerfOverlay.FlagTexture(4096, 1024, true, false));
            Assert.IsTrue(PerfOverlay.FlagTexture(512, 512, false, false));
            Assert.IsFalse(PerfOverlay.FlagTexture(2048, 2048, true, false));
            Assert.IsFalse(PerfOverlay.FlagTexture(3840, 2160, false, true));
            Assert.AreEqual(1024L * 1024 * 4, PerfOverlay.TextureBytes(1024, 1024, GraphicsFormat.R8G8B8A8_SRGB, 1, 1));
            Assert.AreEqual(1024L * 1024 * 4 * 4 / 3, PerfOverlay.TextureBytes(1024, 1024, GraphicsFormat.R8G8B8A8_SRGB, 11, 1));
        }
    }
}
