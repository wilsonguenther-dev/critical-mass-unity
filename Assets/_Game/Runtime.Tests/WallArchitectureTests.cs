using System.Collections.Generic;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Dressing;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// World horizon 2 (2026-11-14): the wall architecture overlay. Budget per quality level (one draw, no texture,
    /// triangles per wall cell), view only (the map is not touched, breakables are never dressed), top-down safety (from
    /// the gameplay camera nothing rises, gate arches fold away and nothing stands over a walkable cell), mesh sanity,
    /// and the shader's promises (no shadow caster, no texture, the low-view weight, orthographic off, the see-through line).
    /// </summary>
    public sealed class WallArchitectureTests
    {
        static readonly int[] Levels = { FramePolicy.LevelVeryLow, FramePolicy.LevelLow, FramePolicy.LevelMedium, FramePolicy.LevelHigh };

        static MapDef Map() => SkybreakBasinMap.BuildSkybreakBasin(GameConfig.CreateBrowser5v5());

        static WallArchReport Build(MapDef map, int level, out ArchMesh m)
        {
            m = new ArchMesh();
            return WallArchitectureLayout.Build(m, map, WallArchBudget.For(level));
        }

        [Test]
        public void BudgetPerLevelOneDrawNoTextureAndBoundedTriangles()
        {
            MapDef map = Map();
            int prev = -1;
            foreach (int q in Levels)
            {
                WallArchReport r = Build(map, q, out ArchMesh m);
                int ceiling = r.WallCells * WallArchBudget.MaxPerCell(q) + WallArchBudget.MaxFixed;
                Assert.LessOrEqual(m.Tris, ceiling, $"wall architecture triangles on level {q}");
                Assert.GreaterOrEqual(m.Tris, prev, "a higher level never has less");
                prev = m.Tris;
                Assert.AreEqual(2, r.Keeps, "a keep for each base on every level");
                Assert.Greater(r.Pillars, 0, "the hill's beacon pillars on every level");
                TestContext.WriteLine($"wall architecture level {q}: {m.Tris} triangles, {m.V.Count} vertices, {WallArchBudget.Draws} draw, " +
                    $"{WallArchBudget.TextureBytes} texture bytes; {r.WallCells} wall cells, {r.Coping} coping, {r.Merlons} merlons, {r.Courses} courses, " +
                    $"{r.Buttresses} buttresses, {r.Towers} towers, {r.Gates} gates, {r.Banners} banners, {r.Torches} torches, {r.Keeps} keeps, " +
                    $"{r.Pillars} pillars, {r.RopeCells} rope cells, {r.Views.Count} shot views");
            }
            Assert.AreEqual(1, WallArchBudget.Draws);
            Assert.AreEqual(0, WallArchBudget.TextureBytes);
            WallArchReport hi = Build(map, FramePolicy.LevelHigh, out _);
            Assert.Greater(hi.Merlons, 0); Assert.Greater(hi.Towers, 0); Assert.Greater(hi.Banners, 0); Assert.Greater(hi.Torches, 0);
            Assert.LessOrEqual(hi.Towers, WallArchBudget.For(FramePolicy.LevelHigh).Towers);
            Assert.LessOrEqual(hi.Banners, WallArchBudget.For(FramePolicy.LevelHigh).Banners);
            Assert.LessOrEqual(hi.Torches, WallArchBudget.For(FramePolicy.LevelHigh).Torches);
        }

        [Test]
        public void ViewOnlyTheMapIsUntouched()
        {
            MapDef map = Map();
            byte[] cells = (byte[])map.Cells.Clone();
            double[] h = (double[])map.Height01.Clone();
            byte[] hp = map.WallHp != null ? (byte[])map.WallHp.Clone() : null;
            Build(map, FramePolicy.LevelHigh, out _);
            CollectionAssert.AreEqual(cells, map.Cells, "cells unchanged");
            CollectionAssert.AreEqual(h, map.Height01, "heights unchanged");
            if (hp != null) CollectionAssert.AreEqual(hp, map.WallHp, "wall hit points unchanged");
        }

        [Test]
        public void MeshIsSaneAndEveryChannelMatches()
        {
            MapDef map = Map();
            foreach (int q in Levels)
            {
                Build(map, q, out ArchMesh m);
                int n = m.V.Count;
                Assert.AreEqual(n, m.N.Count); Assert.AreEqual(n, m.C.Count); Assert.AreEqual(n, m.U0.Count);
                Assert.AreEqual(n, m.U1.Count); Assert.AreEqual(n, m.U2.Count);
                Assert.AreEqual(0, m.T.Count % 3);
                foreach (int i in m.T) Assert.That(i, Is.InRange(0, n - 1));
                foreach (Vector3 v in m.V) Assert.IsFalse(float.IsNaN(v.x + v.y + v.z) || float.IsInfinity(v.x + v.y + v.z));
                foreach (Vector2 u in m.U0) Assert.That(u.x, Is.InRange(0f, WallArchBudget.MaxRise), "rise is capped");
            }
        }

        /// <summary>
        /// What the top-down camera sees (rise 0, low-only pieces folded): no dressing vertex stands over the inside of a
        /// walkable cell by more than MaxIntrusion from a wall face, except the bridge rope rails on a bridge's void edge,
        /// and nothing (keeps aside, which stand off the island) stands higher than 0.5 u over the wall top.
        /// </summary>
        [Test]
        public void TopDownViewAddsNothingOverTheFightersAndBreakablesAreNotDressed()
        {
            MapDef map = Map();
            Build(map, FramePolicy.LevelHigh, out ArchMesh m);
            int W = map.Width, H = map.Height;
            double maxH = 0;
            for (int i = 0; i < map.Height01.Length; i++) maxH = System.Math.Max(maxH, map.Height01[i]);
            int over = 0, high = 0;
            for (int i = 0; i < m.V.Count; i++)
            {
                Vector3 p = m.V[i];
                if (m.U0[i].y > 2.5f) p += (Vector3)m.U1[i];   // folded (top-down)
                float sx = p.x, sz = -p.z;
                int cx = Mathf.FloorToInt(sx), cz = Mathf.FloorToInt(sz);
                if (cx < 0 || cz < 0 || cx >= W || cz >= H) continue;   // off the island: keeps, chains
                byte t = map.Cells[cz * W + cx];
                if (t == Cell.BreakableIntact || t == Cell.BreakableCracked)
                    Assert.Fail($"dressing over a breakable cell ({cx}, {cz}): the #98 damage stages must read as before");
                if (p.y > map.Height01[cz * W + cx] + WallArchitectureLayout.WallHeight + 0.5f) high++;
                bool walk = t == Cell.Floor || t == Cell.BreakableDestroyed;
                if (!walk) continue;
                float fx = sx - cx, fz = sz - cz;
                float edge = Mathf.Min(Mathf.Min(fx, 1f - fx), Mathf.Min(fz, 1f - fz));
                if (edge > WallArchBudget.MaxIntrusion + 1e-3f && p.y > map.Height01[cz * W + cx] + 0.3f) over++;
            }
            Assert.AreEqual(0, over, "vertices standing inside a walkable cell, top-down");
            Assert.AreEqual(0, high, "vertices more than 0.5 u over the wall top, top-down");
        }

        [Test]
        public void ShotViewsCoverWallsGateKeepsAndHill()
        {
            WallArchReport r = Build(Map(), FramePolicy.LevelHigh, out _);
            var names = new HashSet<string>();
            foreach (var v in r.Views) names.Add(v.name);
            foreach (string k in new[] { "wall-run", "keep-azure", "keep-crimson", "hill-beacon" }) Assert.IsTrue(names.Contains(k), k);
            if (r.Gates > 0) Assert.IsTrue(names.Contains("gate"));
        }

        [Test]
        public void ShaderKeepsItsPromises()
        {
            string s = File.ReadAllText(Path.Combine(Application.dataPath, "_Game/Runtime/View/Atmosphere/Resources/CMWallDress.shader"));
            StringAssert.Contains("Shader \"CM/WallDress\"", s);
            StringAssert.DoesNotContain("ShadowCaster", s, "casts no shadow");
            StringAssert.DoesNotContain("TEXTURE2D", s, "no texture");
            StringAssert.DoesNotContain("sampler2D", s, "no texture");
            StringAssert.Contains("unity_OrthoParams.w > 0.5", s, "no rise on orthographic cameras");
            StringAssert.Contains("(fwd.y + 0.7) / 0.3", s, "the low-view weight (0 for the top-down camera)");
            StringAssert.Contains("smoothstep(3.0, 8.0", s, "nothing grows right in front of the follow camera");
            StringAssert.Contains("_CMFadeCam", s, "keeps the follow camera's see-through line");
            StringAssert.Contains("CMTodGrade(", s, "takes the time-of-day grade");
            // The top-down gameplay camera looks about 55 degrees down: forward.y = -0.82, weight 0.
            Assert.AreEqual(0f, Mathf.Clamp01((-Mathf.Sin(55f * Mathf.Deg2Rad) + 0.7f) / 0.3f));
        }
    }
}
