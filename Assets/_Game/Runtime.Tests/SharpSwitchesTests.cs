using CriticalMass.Runtime.Game;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// Batch 12 item AB: the five sharpness switches (Runtime/Game/SharpSwitches.cs) and the by-position normal smoothing
    /// of Editor/Art/SharpNormalsPostprocessor. Every case prints one line: what was measured, the value, the limit.
    /// </summary>
    [TestFixture]
    public sealed class SharpSwitchesTests
    {
        [TearDown]
        public void ReadTheCommandLineAgain() => SharpSwitches.ResetForTests();

        static void Line(string what, object value, string limit) =>
            Debug.Log("[SharpSwitchesTests] " + what + ": " + value + " (expected " + limit + ")");

        [Test]
        public void EachSwitchParsesAloneAndInAll()
        {
            var cases = new (string list, SharpSwitches.Switch want)[]
            {
                ("normals", SharpSwitches.Switch.Normals), ("rim", SharpSwitches.Switch.Rim),
                ("upscale", SharpSwitches.Switch.Upscale), ("msaa", SharpSwitches.Switch.Msaa),
                ("lod0", SharpSwitches.Switch.Lod0), ("all", SharpSwitches.Switch.All),
                ("Rim, MSAA", SharpSwitches.Switch.Rim | SharpSwitches.Switch.Msaa),
                ("normals,rim,upscale,msaa,lod0", SharpSwitches.Switch.All),
                ("nonsense", SharpSwitches.Switch.None),
            };
            foreach (var (list, want) in cases)
            {
                SharpSwitches.Switch got = SharpSwitches.Parse(new[] { "game", "-autoplay", SharpSwitches.Flag, list, "-seed", "7" });
                Line("-sharpOff " + list, got + " -> " + SharpSwitches.Describe(got), want.ToString());
                Assert.AreEqual(want, got, list);
            }
            foreach (SharpSwitches.Switch s in new[] { SharpSwitches.Switch.Normals, SharpSwitches.Switch.Rim,
                         SharpSwitches.Switch.Upscale, SharpSwitches.Switch.Msaa, SharpSwitches.Switch.Lod0 })
                Assert.AreEqual(s, SharpSwitches.Parse(new[] { SharpSwitches.Flag, s.ToString().ToLowerInvariant() }) & SharpSwitches.Switch.All,
                    s + " alone turns only itself off");
        }

        [Test]
        public void NoFlagLeavesEverythingOnAndABareFlagTurnsEverythingOff()
        {
            SharpSwitches.Switch none = SharpSwitches.Parse(new[] { "game", "-autoplay" });
            SharpSwitches.Switch bare = SharpSwitches.Parse(new[] { "game", SharpSwitches.Flag, "-autoplay" });
            SharpSwitches.Switch last = SharpSwitches.Parse(new[] { "game", SharpSwitches.Flag });
            Line("no flag", SharpSwitches.Describe(none), "all on");
            Line("bare -sharpOff before another flag", SharpSwitches.Describe(bare), "all off");
            Line("bare -sharpOff last", SharpSwitches.Describe(last), "all off");
            Assert.AreEqual(SharpSwitches.Switch.None, none);
            Assert.AreEqual(SharpSwitches.Switch.All, bare);
            Assert.AreEqual(SharpSwitches.Switch.All, last);
            Assert.AreEqual(SharpSwitches.Switch.None, SharpSwitches.Parse(null));
        }

        [Test]
        public void WithASwitchOffTheOldValueIsUsed()
        {
            float rim = SharpSwitches.RimFor(0.6f, false);
            float scale = SharpSwitches.MidRenderScaleFor(false);
            int msaaHigh = SharpSwitches.MsaaFor(FramePolicy.LevelHigh, false);
            int lod0 = SharpSwitches.MediumLod0BudgetFor(false);
            float smooth = SharpSwitches.SmoothNormalsFor(false);
            Line("rim off: material rim for a 0.6 model", rim, "0.6");
            Line("upscale off: Medium render scale (bilinear upscale, as before)", scale, "0.75");
            Line("msaa off: High MSAA samples", msaaHigh, "1");
            Line("lod0 off: Medium LOD0 budget", lod0, "10");
            Line("normals off: _SmoothNormals", smooth, "0");
            Assert.AreEqual(0.6f, rim, 1e-6f);
            Assert.AreEqual(0.75f, scale, 1e-6f);
            Assert.AreEqual(1, msaaHigh);
            Assert.AreEqual(10, lod0);
            Assert.AreEqual(0f, smooth);
        }

        [Test]
        public void WithEverySwitchOnTheNewValuesAreUsed()
        {
            float rim = SharpSwitches.RimFor(0.6f, true);
            float scale = SharpSwitches.MidRenderScaleFor(true);
            int msaaHigh = SharpSwitches.MsaaFor(FramePolicy.LevelHigh, true), msaaMed = SharpSwitches.MsaaFor(FramePolicy.LevelMedium, true),
                msaaLow = SharpSwitches.MsaaFor(FramePolicy.LevelLow, true);
            int lod0 = SharpSwitches.MediumLod0BudgetFor(true);
            Line("rim on: material rim for a 0.6 model", rim, "0.3");
            Line("upscale on: Medium render scale", scale, "0.85");
            Line("msaa on: MSAA samples High / Medium / Low", msaaHigh + " / " + msaaMed + " / " + msaaLow, "2 / 1 / 1");
            Line("lod0 on: Medium LOD0 budget", lod0, "16");
            Line("normals on: _SmoothNormals", SharpSwitches.SmoothNormalsFor(true), "1");
            Assert.AreEqual(0.3f, rim, 1e-6f);
            Assert.AreEqual(0.85f, scale, 1e-6f);
            Assert.AreEqual(2, msaaHigh);
            Assert.AreEqual(1, msaaMed, "Medium unchanged");
            Assert.AreEqual(1, msaaLow, "Low unchanged");
            Assert.AreEqual(16, lod0);
            Assert.AreEqual(1f, SharpSwitches.SmoothNormalsFor(true));
            Assert.AreEqual(0f, SharpSwitches.RimFor(0f, true), "props and structures keep no rim");
        }

        [Test]
        public void SetForTestsDrivesTheProperties()
        {
            SharpSwitches.SetForTests(SharpSwitches.Switch.Rim | SharpSwitches.Switch.Lod0);
            Line("forced off rim,lod0", SharpSwitches.Describe(SharpSwitches.Off), "normals on, rim off, upscale on, msaa on, lod0 off");
            Assert.IsTrue(SharpSwitches.Normals);
            Assert.IsFalse(SharpSwitches.Rim);
            Assert.IsTrue(SharpSwitches.Upscale);
            Assert.IsTrue(SharpSwitches.Msaa);
            Assert.IsFalse(SharpSwitches.Lod0);
        }

        // ------------------------------------------------------------------------------- normals (SharpNormalsPostprocessor)
        // (added with the normals switch)

        // ------------------------------------------------------------------------------- end
    }
}
