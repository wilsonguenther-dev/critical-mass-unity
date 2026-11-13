using System.Collections.Generic;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Atmosphere;
using CriticalMass.Runtime.View.Dressing;
using CriticalMass.Runtime.View.Effects;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// World around arena 1 (2026-11-13): the islands, landmarks and roots around the play island stay inside their
    /// per-quality budget (Low is the cheap version: one draw), never stand over the play surface, are deterministic, and
    /// carry every landmark at every tier; the new ambient extras (night gliders, cloud banks, wind streaks, far
    /// lightning, drips) stay in the void and off Low; ground marks from hits are a neutral ink, never a team hue.
    /// </summary>
    public sealed class WorldBeyondTests
    {
        const int MapW = 112, MapH = 72;
        static readonly int[] Levels = { FramePolicy.LevelVeryLow, FramePolicy.LevelLow, FramePolicy.LevelMedium, FramePolicy.LevelHigh };

        /// <summary>A rim of 570 edges (today's plan) around the map rectangle, tops at y 0, normals outward.</summary>
        static List<WorldEdge> Rim()
        {
            var o = new List<WorldEdge>();
            int per = 570 / 4;
            for (int k = 0; k < per; k++)
            {
                double t0 = k / (double)per, t1 = (k + 1) / (double)per;
                o.Add(new WorldEdge { Px = MapW * t0, Pz = 0, Qx = MapW * t1, Qz = 0, Nx = 0, Nz = -1, H = 0 });          // north (sim z 0)
                o.Add(new WorldEdge { Px = MapW * t0, Pz = MapH, Qx = MapW * t1, Qz = MapH, Nx = 0, Nz = 1, H = 0 });    // south
                o.Add(new WorldEdge { Px = 0, Pz = MapH * t0, Qx = 0, Qz = MapH * t1, Nx = -1, Nz = 0, H = 0 });          // west
                o.Add(new WorldEdge { Px = MapW, Pz = MapH * t0, Qx = MapW, Qz = MapH * t1, Nx = 1, Nz = 0, H = 0 });    // east
            }
            return o;
        }

        static BeyondMesh Build(int level, out List<BeyondFeature> f, List<WorldEdge> rim = null, List<Vector3> tips = null, List<Vector3> feet = null)
        {
            var m = new BeyondMesh();
            f = WorldBeyondLayout.Build(m, WorldBeyondBudget.For(level), MapW, MapH, rim ?? Rim(), tips, feet);
            return m;
        }

        [Test]
        public void BudgetsPerTierAndLowIsTheCheapVersion()
        {
            // The world draws 1.36 M triangles on High (arena night life 1 report); budgets 1.4 / 2.2 / 3.0 M at Low / Medium
            // / High. The world around the arena takes at most about 0.35 / 0.9 / 1.3 % of them.
            int tLow = 0, tMed = 0, tHigh = 0;
            foreach (int q in Levels)
            {
                BeyondMesh m = Build(q, out _);
                WorldBeyondBudget b = WorldBeyondBudget.For(q);
                int drawn = m.Tris * (b.Ink ? 2 : 1);
                if (q <= FramePolicy.LevelLow) { tLow = drawn; Assert.AreEqual(1, b.Draws, "one draw on Low (no ink)"); Assert.LessOrEqual(drawn, 5000, "Low triangles"); }
                else if (q == FramePolicy.LevelMedium) { tMed = drawn; Assert.LessOrEqual(b.Draws, 2); Assert.LessOrEqual(drawn, 20000, "Medium triangles"); }
                else { tHigh = drawn; Assert.LessOrEqual(b.Draws, 2); Assert.LessOrEqual(drawn, 40000, "High triangles"); }
                TestContext.WriteLine($"level {q}: {m.Tris} triangles in the mesh, {drawn} drawn, {b.Draws} draws, {m.V.Count} vertices");
            }
            Assert.Greater(tHigh, tMed);
            Assert.Greater(tMed, tLow);
            WorldBeyondBudget low = WorldBeyondBudget.For(FramePolicy.LevelLow);
            Assert.AreEqual(0, low.RimRootPercent + low.RootsPerIslet + low.FarIslets + low.NearIslets, "no roots, no extra islets on Low");
            Assert.IsFalse(low.Massifs);
        }

        [Test]
        public void MeshIsWellFormed()
        {
            foreach (int q in Levels)
            {
                BeyondMesh m = Build(q, out _);
                Assert.AreEqual(m.V.Count, m.N.Count);
                Assert.AreEqual(m.V.Count, m.C.Count);
                Assert.AreEqual(m.V.Count, m.U0.Count);
                Assert.AreEqual(m.V.Count, m.U1.Count);
                Assert.AreEqual(m.V.Count, m.U2.Count);
                Assert.AreEqual(0, m.T.Count % 3);
                foreach (int i in m.T) Assert.That(i, Is.InRange(0, m.V.Count - 1));
                foreach (Vector3 v in m.V) Assert.IsFalse(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.y), "finite vertices");
            }
        }

        [Test]
        public void NothingStandsOverThePlaySurface()
        {
            foreach (int q in Levels)
            {
                Build(q, out List<BeyondFeature> f, new List<WorldEdge>());
                foreach (BeyondFeature x in f)
                {
                    if (x.Band == 3) continue;
                    float dx = Mathf.Max(0f, Mathf.Max(-x.Centre.x, x.Centre.x - MapW));
                    float dz = Mathf.Max(0f, Mathf.Max(x.Centre.z, -MapH - x.Centre.z));
                    Assert.Greater(Mathf.Sqrt(dx * dx + dz * dz), x.Radius, $"{x.Name} at {x.Centre} lies off the map rectangle");
                    Assert.LessOrEqual(x.Centre.y, -2f, $"{x.Name} sits below the island tops");
                }
                // Every vertex that is over the map rectangle at all (a hanging chain, a root) is well below the tops.
                BeyondMesh m = Build(q, out _, new List<WorldEdge>());
                foreach (Vector3 v in m.V)
                    if (v.x > 0f && v.x < MapW && v.z < 0f && v.z > -MapH)
                        Assert.Less(v.y, -1.5f, $"vertex {v} over the play surface is below the island tops");
            }
            // Roots under the rim hang below its lip.
            BeyondMesh r = Build(FramePolicy.LevelHigh, out List<BeyondFeature> fr, Rim());
            bool rootsFound = false;
            foreach (BeyondFeature x in fr) rootsFound |= x.Band == 3;
            Assert.IsTrue(rootsFound, "the play island gets hanging roots on High");
            for (int i = 0; i < r.V.Count; i++)
                if (r.U1[i].z > 1.5f && r.V[i].x > -0.5f && r.V[i].x < MapW + 0.5f && r.V[i].z < 0.5f && r.V[i].z > -MapH - 0.5f)
                    Assert.Less(r.V[i].y, -0.15f, "a rim root hangs below the lip");
        }

        [Test]
        public void EveryLandmarkAtEveryTierAndDeterministic()
        {
            string[] landmarks = { "watchtower", "waterfall", "crystal outcrop", "azure homeland outpost", "crimson homeland outpost", "old kaiju remains" };
            foreach (int q in Levels)
            {
                Build(q, out List<BeyondFeature> f);
                var names = new HashSet<string>();
                foreach (BeyondFeature x in f) names.Add(x.Name);
                foreach (string l in landmarks) Assert.IsTrue(names.Contains(l), $"{l} on level {q}");
            }
            BeyondMesh a = Build(FramePolicy.LevelHigh, out _), b = Build(FramePolicy.LevelHigh, out _);
            Assert.AreEqual(a.V.Count, b.V.Count);
            Assert.AreEqual(a.T.Count, b.T.Count);
            for (int i = 0; i < a.V.Count; i += 97) Assert.AreEqual(a.V[i], b.V[i]);
            // The homelands carry their team, nothing else does.
            int azure = 0, crimson = 0;
            foreach (Vector4 d in a.U1) { if (Mathf.Approximately(d.y, 1f)) azure++; else if (Mathf.Approximately(d.y, 2f)) crimson++; }
            Assert.Greater(azure, 0);
            Assert.Greater(crimson, 0);
            // Glows exist (crystals, homeland windows, embers) and the waterfall is flagged as water.
            bool glow = false, water = false;
            for (int i = 0; i < a.V.Count; i++) { glow |= a.C[i].a > 0.5f; water |= Mathf.Approximately(a.U1[i].z, 1f); }
            Assert.IsTrue(glow && water);
        }

        [Test]
        public void AmbientExtrasStayInTheVoidAndOffLow()
        {
            Assert.AreEqual(AmbientMeshes.GliderTris, AmbientMeshes.Glider().Tris);
            bool lamp = false;
            foreach (Color c in AmbientMeshes.Glider().C) lamp |= c.a > 0.5f;
            Assert.IsTrue(lamp, "the glider carries its lantern");
            AmbientBudget low = AmbientBudget.For(FramePolicy.LevelLow);
            Assert.AreEqual(0, low.Gliders + low.Streaks + low.Bolts + low.Drips, "no gliders, streaks, lightning or drips on Low");
            AmbientBudget hi = AmbientBudget.For(FramePolicy.LevelHigh);
            var tips = new List<Vector3>(); var feet = new List<Vector3>();
            Build(FramePolicy.LevelHigh, out _, Rim(), tips, feet);
            Assert.Greater(tips.Count, 0);
            Assert.AreEqual(1, feet.Count, "one waterfall foot");
            List<AmbientSprite> ex = AmbientLayout.WorldExtras(hi, MapW, MapH, tips, feet);
            int bolts = 0;
            foreach (AmbientSprite s in ex)
            {
                Assert.Less(s.Anchor.y, -0.1f, $"kind {s.Kind} below the island tops");
                if (s.Kind == 6) { bolts++; Assert.LessOrEqual(s.Anchor.y, -9f, "lightning lives in the cloud sea"); }
                if (s.Kind == 5) Assert.LessOrEqual(s.Anchor.y, -9f, "banks lie on the cloud sea");
                if (s.Kind == 7) Assert.LessOrEqual(s.Anchor.y, -1.5f, "wind streaks below the tops");
            }
            Assert.AreEqual(hi.Bolts, bolts);
            int gliders = 0;
            foreach (AmbientCreature c in AmbientLayout.Creatures(hi, MapW, MapH))
                if (c.Kind == 3) { gliders++; Assert.LessOrEqual(c.Height, -1f, "gliders fly below the island tops"); }
            Assert.AreEqual(hi.Gliders, gliders);
            Assert.AreEqual(0, AmbientLayout.WorldExtras(hi.WithoutWorldExtras(), MapW, MapH, tips, feet).Count, "before captures: no extras");
        }

        [Test]
        public void GroundMarksAreANeutralInkNotATeamHue()
        {
            Color crimson = (Color)new Color32(255, 90, 95, 255), azure = (Color)new Color32(22, 199, 217, 255);
            foreach (Color32 c32 in new[] { FxScene.GroundInkLight, FxScene.GroundInkMid, FxScene.GroundInkDark })
            {
                Color c = c32;
                Vector3 lab = TimeOfDayGrade.OkLab(c.linear);
                float chroma = new Vector2(lab.y, lab.z).magnitude;
                Assert.Less(chroma, 0.04f, $"ground ink {c32} is neutral (chroma {chroma:0.000})");
                Assert.Greater(TimeOfDayGrade.DeltaE(c.linear, crimson.linear), 0.2f, "far from Crimson");
                Assert.Greater(TimeOfDayGrade.DeltaE(c.linear, azure.linear), 0.2f, "far from Azure");
            }
            Assert.Less(FxScene.GroundInkAlpha, 1f, "the ground shows through the marks");
        }

        [Test]
        public void TheWorldShaderTakesTheGrade()
        {
            string path = Path.Combine(Application.dataPath, "_Game/Runtime/View/Atmosphere/Resources/CMWorldBeyond.shader");
            string s = File.ReadAllText(path);
            StringAssert.Contains("CMTodGrade(", s);
            StringAssert.Contains("CMTodFogColor(", s);
            StringAssert.Contains("CMTodRim(", s);
            StringAssert.DoesNotContain("ShadowCaster", s, "the void never shadows the play island");
        }
    }
}
