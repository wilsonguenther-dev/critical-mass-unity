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

        /// <summary>Each triangle on its own three vertices with its face normal: what a one-colour-per-triangle mesh imports as.</summary>
        static void Split(System.Collections.Generic.List<Vector3> v, System.Collections.Generic.List<Vector3> n,
            System.Collections.Generic.List<int> t, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 f = Vector3.Cross(b - a, c - a).normalized;
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            n.Add(f); n.Add(f); n.Add(f);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        [Test]
        public void ASplitCubeKeepsEveryHardEdge()
        {
            var v = new System.Collections.Generic.List<Vector3>();
            var n = new System.Collections.Generic.List<Vector3>();
            var t = new System.Collections.Generic.List<int>();
            Vector3[] c = new Vector3[8];
            for (int i = 0; i < 8; i++) c[i] = new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1) - Vector3.one * 0.5f;
            int[][] quads = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
            foreach (int[] q in quads)
            {
                Split(v, n, t, c[q[0]], c[q[1]], c[q[2]]);
                Split(v, n, t, c[q[0]], c[q[2]], c[q[3]]);
            }
            Vector3[] s = CriticalMass.Editor.Art.SharpNormalsPostprocessor.SmoothByPosition(v.ToArray(), n.ToArray(), t.ToArray(),
                SharpSwitches.SmoothNormalsAngleDeg, out int positions);
            float moved = 0f, minCorner = 180f;
            for (int i = 0; i < s.Length; i++)
            {
                moved = Mathf.Max(moved, Vector3.Angle(s[i], n[i]));
                for (int j = 0; j < s.Length; j++)
                    if (j != i && (v[j] - v[i]).sqrMagnitude < 1e-10f && Vector3.Angle(n[j], n[i]) > 1f)
                        minCorner = Mathf.Min(minCorner, Vector3.Angle(s[j], s[i]));
            }
            Line("split cube: positions", positions, "8");
            Line("split cube: largest turn of a smoothed normal from its face (deg)", moved.ToString("0.000"), "< 0.01, every edge is 90 > 60");
            Line("split cube: smallest angle between two faces' normals at one corner (deg)", minCorner.ToString("0.0"), "90 (hard edges kept)");
            Assert.AreEqual(8, positions);
            Assert.Less(moved, 0.01f);
            Assert.AreEqual(90f, minCorner, 0.01f);
        }

        [Test]
        public void AFourteenColourFlatStripSharesNormalsUnderSixtyAndKeepsTheFold()
        {
            // 7 quads = 14 triangles, each triangle its own colour (so its own vertices), bent 10 degrees per quad, with a
            // 90 degree fold between quad 3 and quad 4.
            float[] deg = { 0f, 10f, 20f, 30f, 120f, 130f, 140f };
            var p = new Vector3[deg.Length + 1];
            for (int k = 0; k < deg.Length; k++)
                p[k + 1] = p[k] + new Vector3(Mathf.Cos(deg[k] * Mathf.Deg2Rad), 0f, Mathf.Sin(deg[k] * Mathf.Deg2Rad));
            var v = new System.Collections.Generic.List<Vector3>();
            var n = new System.Collections.Generic.List<Vector3>();
            var t = new System.Collections.Generic.List<int>();
            var quadOf = new System.Collections.Generic.List<int>();
            for (int k = 0; k < deg.Length; k++)
            {
                Vector3 a = p[k], b = p[k + 1], c = p[k + 1] + Vector3.up, d = p[k] + Vector3.up;
                Split(v, n, t, a, b, c);
                Split(v, n, t, a, c, d);
                for (int r = 0; r < 6; r++) quadOf.Add(k);
            }
            Assert.AreEqual(14, t.Count / 3, "fourteen triangles");
            Vector3[] s = CriticalMass.Editor.Art.SharpNormalsPostprocessor.SmoothByPosition(v.ToArray(), n.ToArray(), t.ToArray(),
                SharpSwitches.SmoothNormalsAngleDeg, out int positions);
            float sharedSpread = 0f, foldMin = 180f, turn = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                turn = Mathf.Max(turn, Vector3.Angle(s[i], n[i]));
                for (int j = 0; j < s.Length; j++)
                {
                    if (j == i || (v[j] - v[i]).sqrMagnitude > 1e-10f) continue;
                    bool acrossFold = (quadOf[i] <= 3) != (quadOf[j] <= 3);
                    float a = Vector3.Angle(s[i], s[j]);
                    if (acrossFold) foldMin = Mathf.Min(foldMin, a);
                    else sharedSpread = Mathf.Max(sharedSpread, a);
                }
            }
            Line("strip: welded positions", positions, "16");
            Line("strip: largest angle between split vertices at one position, same side of the fold (deg)", sharedSpread.ToString("0.000"), "< 0.01 (one shared normal)");
            Line("strip: smallest angle across the 90 degree fold (deg)", foldMin.ToString("0.0"), "> 60 (hard edge kept)");
            Line("strip: largest turn of a smoothed normal from its own face (deg)", turn.ToString("0.0"), "<= 10 (half the 10 degree bends both sides)");
            Assert.AreEqual(16, positions);
            Assert.Less(sharedSpread, 0.01f);
            Assert.Greater(foldMin, 60f);
            Assert.LessOrEqual(turn, 10.01f);
        }

        [Test]
        public void TheSmoothedNormalTurnsWithTheSkinnedFrame()
        {
            // The importer stores s in the frame of the bind-pose N and T; skinning turns N and T; the shader decodes.
            var rot = Quaternion.Euler(37f, -71f, 12f);
            float worst = 0f;
            int degenerate = 0;
            for (int k = 0; k < 24; k++)
            {
                Vector3 nn = Quaternion.Euler(k * 15f, k * 7f, 0f) * Vector3.up;
                Vector3 tt = Quaternion.AngleAxis(25f + k, Vector3.Cross(nn, Vector3.right).normalized + Vector3.forward * 0.01f) * nn;
                Vector3 ss = Quaternion.AngleAxis(8f + k, Vector3.Cross(nn, tt).normalized) * nn;
                Vector3[] e = CriticalMass.Editor.Art.SharpNormalsPostprocessor.EncodeInFrame(new[] { nn }, new[] { new Vector4(tt.x, tt.y, tt.z, 1f) },
                    new[] { ss }, out int dg);
                degenerate += dg;
                Vector3 tr = rot * tt;
                Vector3 back = CriticalMass.Editor.Art.SharpNormalsPostprocessor.DecodeInFrame(rot * nn, new Vector4(tr.x, tr.y, tr.z, 1f), e[0]);
                worst = Mathf.Max(worst, Vector3.Angle(back, rot * ss));
            }
            Vector3[] flat = CriticalMass.Editor.Art.SharpNormalsPostprocessor.EncodeInFrame(new[] { Vector3.up }, new[] { new Vector4(0f, 1f, 0f, 1f) },
                new[] { Vector3.right }, out int parallel);
            Line("frame: largest error of the decoded normal after a bone turn (deg)", worst.ToString("0.0000"), "< 0.01");
            Line("frame: T parallel to N stores", flat[0] + " degenerate=" + parallel, "(0, 0, 1) degenerate=1 (keeps the imported normal)");
            Assert.AreEqual(0, degenerate);
            Assert.Less(worst, 0.01f);
            Assert.AreEqual(1, parallel);
            Assert.AreEqual(new Vector3(0f, 0f, 1f), flat[0]);
        }

        // ------------------------------------------------------------------------------- end
    }
}
