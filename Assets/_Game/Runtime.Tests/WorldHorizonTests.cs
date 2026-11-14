using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Atmosphere;
using CriticalMass.Runtime.View.Dressing;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// World horizon 1 (2026-11-14): the eye-level sky and far silhouettes stay one mesh, one draw, one material, no
    /// texture, under a triangle ceiling at every tier, with land on the horizon in every direction and each direction's
    /// accent; the bands paint back to front; nothing new casts or receives shadows, has a collider or runs per frame;
    /// the eye-level landmarks and the rim fringe stay inside their own budget and off the top-down frame.
    /// </summary>
    public sealed class WorldHorizonTests
    {
        const int MapW = 112, MapH = 72;
        static readonly int[] Levels = { FramePolicy.LevelVeryLow, FramePolicy.LevelLow, FramePolicy.LevelMedium, FramePolicy.LevelHigh };

        static string Src(string rel) => File.ReadAllText(Path.Combine(Application.dataPath, rel));

        static HorizonMesh Horizon(int level, out List<HorizonPiece> pieces)
        {
            var m = new HorizonMesh();
            pieces = WorldHorizonLayout.Build(m, WorldHorizonBudget.For(level));
            return m;
        }

        static List<WorldEdge> Rim()
        {
            var o = new List<WorldEdge>();
            int per = 570 / 4;
            for (int k = 0; k < per; k++)
            {
                double t0 = k / (double)per, t1 = (k + 1) / (double)per;
                o.Add(new WorldEdge { Px = MapW * t0, Pz = 0, Qx = MapW * t1, Qz = 0, Nx = 0, Nz = -1, H = 0 });
                o.Add(new WorldEdge { Px = MapW * t0, Pz = MapH, Qx = MapW * t1, Qz = MapH, Nx = 0, Nz = 1, H = 0 });
                o.Add(new WorldEdge { Px = 0, Pz = MapH * t0, Qx = 0, Qz = MapH * t1, Nx = -1, Nz = 0, H = 0 });
                o.Add(new WorldEdge { Px = MapW, Pz = MapH * t0, Qx = MapW, Qz = MapH * t1, Nx = 1, Nz = 0, H = 0 });
            }
            return o;
        }

        [Test]
        public void HorizonIsOneDrawUnderItsTriangleCeilingAtEveryTier()
        {
            int low = 0, med = 0, high = 0;
            foreach (int q in Levels)
            {
                HorizonMesh m = Horizon(q, out List<HorizonPiece> pieces);
                WorldHorizonBudget b = WorldHorizonBudget.For(q);
                Assert.LessOrEqual(m.Tris, WorldHorizonBudget.MaxTriangles, $"horizon triangles on level {q}");
                foreach (HorizonPiece p in pieces)
                    Assert.Less(p.Triangles, WorldHorizonBudget.MaxPieceTriangles, $"{p.Kind} at {p.AzimuthDeg:0} is a sub-100-triangle card");
                if (q <= FramePolicy.LevelLow) { low = m.Tris; Assert.AreEqual(1, b.Bands, "Low keeps one band"); }
                else if (q == FramePolicy.LevelMedium) med = m.Tris;
                else high = m.Tris;
                TestContext.WriteLine($"horizon level {q}: {m.Tris} triangles, {m.V.Count} vertices, {pieces.Count} pieces, {b.Bands} bands, {WorldHorizonBudget.Draws} draw, {WorldHorizonBudget.TextureBytes} texture bytes");
            }
            Assert.AreEqual(1, WorldHorizonBudget.Draws);
            Assert.AreEqual(0, WorldHorizonBudget.TextureBytes, "no new texture");
            Assert.Greater(high, med);
            Assert.Greater(med, low);
        }

        [Test]
        public void HorizonMeshIsWellFormedAndPaintsBackToFront()
        {
            foreach (int q in Levels)
            {
                HorizonMesh m = Horizon(q, out _);
                Assert.AreEqual(m.V.Count, m.C.Count);
                Assert.AreEqual(m.V.Count, m.U.Count);
                Assert.AreEqual(0, m.T.Count % 3);
                foreach (int i in m.T) Assert.That(i, Is.InRange(0, m.V.Count - 1));
                foreach (Vector3 v in m.V) Assert.IsFalse(float.IsNaN(v.x + v.y + v.z) || float.IsInfinity(v.x + v.y + v.z));
                // No depth write: the index order is the paint order. Dome (follows 1) first, then far, mid, near.
                float prev = 1f;
                for (int t = 0; t < m.T.Count; t += 3)
                {
                    float follow = m.U[m.T[t]].x;
                    Assert.LessOrEqual(follow, prev + 1e-5f, $"triangle {t / 3} paints after a nearer band");
                    prev = follow;
                }
                foreach (Color c in m.C) Assert.That(c.a, Is.InRange(0f, 1f), "the air amount is 0..1");
            }
        }

        [Test]
        public void LandOnTheHorizonInEveryDirectionAndEachDirectionHasItsAccent()
        {
            foreach (int q in Levels)
            {
                Horizon(q, out List<HorizonPiece> pieces);
                var az = new List<float>();
                foreach (HorizonPiece p in pieces) if (p.Kind != "cloud") az.Add(p.AzimuthDeg);
                az.Sort();
                float gap = 360f - (az[az.Count - 1] - az[0]);
                for (int k = 1; k < az.Count; k++) gap = Mathf.Max(gap, az[k] - az[k - 1]);
                Assert.Less(gap, 60f, $"widest empty arc of far land on level {q}");
            }
            Horizon(FramePolicy.LevelHigh, out List<HorizonPiece> hi);
            var seen = new HashSet<string>();
            foreach (HorizonPiece p in hi)
            {
                if (p.Kind == "tower" || p.Kind == "columns" || p.Kind == "falls" || p.Kind == "shard")
                {
                    Assert.AreEqual(WorldHorizonLayout.Accent(p.AzimuthDeg), p.Kind, "an accent only in its own direction");
                    seen.Add(p.Kind);
                }
            }
            foreach (string k in new[] { "tower", "columns", "falls", "shard" }) Assert.IsTrue(seen.Contains(k), $"{k} on the High horizon");
        }

        [Test]
        public void HorizonShaderIsFrugalAndHidesForTheTopDownCamera()
        {
            string s = Src("_Game/Runtime/View/Atmosphere/Resources/CMWorldHorizon.shader");
            StringAssert.Contains("ZWrite Off", s);
            StringAssert.DoesNotContain("ShadowCaster", s, "casts no shadow");
            StringAssert.DoesNotContain("TEXTURE2D", s, "no texture");
            StringAssert.DoesNotContain("sampler2D", s, "no texture");
            StringAssert.Contains("_HideBelowDeg", s, "collapses when the view looks down (top-down camera)");
            StringAssert.Contains("unity_OrthoParams", s, "collapses on orthographic cameras");
            StringAssert.Contains("_ProjectionParams.z < 150.0", s, "collapses on short-range cameras");
            StringAssert.Contains("CMTodGrade(", s, "takes the time-of-day grade");
            StringAssert.Contains("_CMAmbientTime", s, "clouds move in the shader, not in a script");
            StringAssert.Contains("_CMTodZenith", Src("_Game/Runtime/View/Atmosphere/TimeOfDay.cs"), "the director feeds the zenith");
        }

        [Test]
        public void NothingNewCastsShadowsHasCollidersOrRunsPerFrame()
        {
            string beyond = Src("_Game/Runtime/View/Atmosphere/WorldBeyond.cs");
            string horizon = Src("_Game/Runtime/View/Atmosphere/WorldHorizon.cs");
            Assert.AreEqual(2, Regex.Matches(beyond, @"AddComponent<MeshRenderer>\(\)").Count, "two renderers: the world around the arena and the horizon");
            Assert.AreEqual(2, Regex.Matches(beyond, @"new Material\(").Count, "two materials");
            Assert.AreEqual(2, Regex.Matches(beyond, @"shadowCastingMode = ShadowCastingMode\.Off").Count, "neither casts a shadow");
            Assert.AreEqual(2, Regex.Matches(beyond, @"receiveShadows = false").Count, "neither receives one");
            foreach (string src in new[] { beyond, horizon }) StringAssert.DoesNotContain("Collider", src, "no colliders on scenery");
            StringAssert.DoesNotContain("Update(", horizon, "no per-frame script on the horizon");
            StringAssert.DoesNotContain("MonoBehaviour", horizon);
        }

        [Test]
        public void LandmarksAndRimFringeStayInTheirBudgetAndOffTheTopDownFrame()
        {
            string[] names = { "landmark shield spire", "landmark cinder crown", "landmark falls spire", "landmark shard spire" };
            foreach (int q in Levels)
            {
                WorldBeyondBudget b = WorldBeyondBudget.For(q);
                var with = new BeyondMesh();
                List<BeyondFeature> f = WorldBeyondLayout.Build(with, b, MapW, MapH, Rim());
                var without = new BeyondMesh();
                WorldBeyondLayout.Build(without, new WorldBeyondBudget(b.Segments, b.NearIslets, b.FarIslets, b.RootsPerIslet, b.RimRootPercent,
                    b.Massifs, b.Trees, b.UnderCrystals, b.Ink, b.Horizon, 0, 0), MapW, MapH, Rim());
                int added = with.Tris - without.Tris;
                Assert.LessOrEqual(added, q <= FramePolicy.LevelLow ? 1200 : 3000, $"landmarks + rim fringe triangles on level {q} (x2 with ink)");
                TestContext.WriteLine($"level {q}: landmarks + fringe add {added} triangles ({added * (b.Ink ? 2 : 1)} drawn, 0 draws)");
                var have = new HashSet<string>();
                foreach (BeyondFeature x in f)
                {
                    if (!x.Name.StartsWith("landmark")) continue;
                    have.Add(x.Name);
                    float dx = Mathf.Max(0f, Mathf.Max(-x.Centre.x, x.Centre.x - MapW));
                    float dz = Mathf.Max(0f, Mathf.Max(x.Centre.z, -MapH - x.Centre.z));
                    Assert.Greater(Mathf.Sqrt(dx * dx + dz * dz) - x.Radius, 40f, $"{x.Name} stands well past the top-down camera's view (about 16 u past an edge)");
                    // It rises above a standing player's eye (island tops at 0, eye about 1.7): something to look at.
                    float top = float.MinValue;
                    for (int i = 0; i < with.V.Count; i++)
                    {
                        Vector3 v = with.V[i];
                        if (new Vector2(v.x - x.Centre.x, v.z - x.Centre.z).magnitude < x.Radius + 1f) top = Mathf.Max(top, v.y);
                    }
                    Assert.Greater(top, 12f, $"{x.Name} rises well above eye level");
                }
                foreach (string n in names) Assert.IsTrue(have.Contains(n), $"{n} on level {q}");
                bool fringe = false;
                foreach (BeyondFeature x in f) fringe |= x.Name.StartsWith("rim fringe");
                Assert.AreEqual(q > FramePolicy.LevelLow, fringe, $"rim fringe on Medium and High only (level {q})");
            }
        }
    }
}
