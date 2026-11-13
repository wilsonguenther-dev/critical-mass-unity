using System.IO;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// The minimap is the real map: baked from the sim's MapDef, never drifting from it, camera-up like the gameplay view,
    /// and every marker mapped through the same projection (within 1 px at 1x).
    /// </summary>
    public class MinimapTests
    {
        const string Png1x = "Assets/_Game/Resources/UI/Minimap/minimap@1x.png";
        static MinimapBake _bake;
        static MinimapBake Bake => _bake ?? (_bake = new MinimapBake(MinimapGeometry.BakedMap, GameConfig.CreateDefault().Render.CameraYawDeg));

        static World NewWorld() => World.CreateWorld(new WorldOptions
        {
            Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateDefault(),
        });

        static Color32[] LoadTopDown(string path, out int w, out int h)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.IsTrue(t.LoadImage(File.ReadAllBytes(path)), path);
            w = t.width; h = t.height;
            Color32[] bottomUp = t.GetPixels32();
            Object.DestroyImmediate(t);
            var top = new Color32[bottomUp.Length];
            for (int j = 0; j < h; j++) System.Array.Copy(bottomUp, (h - 1 - j) * w, top, j * w, w);
            return top;
        }

        [Test]
        public void TheGamePlaysOnTheBakedMap()
        {
            World w = NewWorld();
            MapDef baked = MinimapGeometry.BakedMap;
            Assert.AreEqual(baked.Width, w.Map.Width);
            Assert.AreEqual(baked.Height, w.Map.Height);
            CollectionAssert.AreEqual(baked.Cells, w.Map.Cells, "the match map is not the map the minimap was baked from");
            Assert.AreEqual(baked.Hill.X, w.Map.Hill.X);
            Assert.AreEqual(baked.Shrine.Z, w.Map.Shrine.Z);
        }

        [Test]
        public void BakedImageMatchesTheSimMap()
        {
            Color32[] png = LoadTopDown(Png1x, out int w, out int h);
            Color32[] now = Bake.Render(1, out int rw, out int rh);
            Assert.AreEqual(rw, w, "width: re-run Critical Mass > Bake Minimap");
            Assert.AreEqual(rh, h, "height: re-run Critical Mass > Bake Minimap");
            int same = 0, worst = 0;
            for (int i = 0; i < png.Length; i++)
            {
                int d = Mathf.Max(Mathf.Abs(png[i].r - now[i].r), Mathf.Max(Mathf.Abs(png[i].g - now[i].g), Mathf.Abs(png[i].b - now[i].b)));
                if (d == 0) same++;
                if (d > worst) worst = d;
            }
            TestContext.WriteLine($"minimap@1x {w}x{h}: {same}/{png.Length} pixels identical, worst channel diff {worst}");
            Assert.GreaterOrEqual(same, (int)(png.Length * 0.999), "the committed minimap drifted from the map: re-run Critical Mass > Bake Minimap");
            Assert.LessOrEqual(worst, 2);
            foreach (int tier in new[] { 2, 4 })
            {
                LoadTopDown($"Assets/_Game/Resources/UI/Minimap/minimap@{tier}x.png", out int tw, out int th);
                Assert.AreEqual(w * tier, tw);
                Assert.AreEqual(h * tier, th);
            }
        }

        [Test]
        public void EveryCellIsDrawnAsWhatTheSimSaysItIs()
        {
            MapDef m = MinimapGeometry.BakedMap;
            int checkedCells = 0, wrong = 0;
            for (int cz = 1; cz < m.Height - 1; cz++)
                for (int cx = 1; cx < m.Width - 1; cx++)
                {
                    bool land = m.Cells[cz * m.Width + cx] != Cell.Void, uniform = true;
                    for (int dz = -1; dz <= 1 && uniform; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                            if ((m.Cells[(cz + dz) * m.Width + cx + dx] != Cell.Void) != land) { uniform = false; break; }
                    if (!uniform) continue;
                    Bake.Shade(cx + 0.5, cz + 0.5, 1, out MinimapClass k);
                    bool drawnLand = k != MinimapClass.Void && k != MinimapClass.Outline && k != MinimapClass.Cliff && k != MinimapClass.Underside && k != MinimapClass.Coast;
                    checkedCells++;
                    if (drawnLand != land) wrong++;
                }
            TestContext.WriteLine($"{checkedCells} cells with a uniform neighbourhood checked, {wrong} drawn wrong");
            Assert.Greater(checkedCells, 5000);
            Assert.AreEqual(0, wrong);
        }

        [Test]
        public void ProjectionIsTheGameplayCameraViewCameraUp()
        {
            GameConfig c = GameConfig.CreateDefault();
            MinimapProjection p = MinimapGeometry.Projection;
            Quaternion rot = Camera.GameplayCamera.Rotation(c);
            Vector3 right = rot * Vector3.right; right.y = 0; right.Normalize();
            Vector3 up = rot * Vector3.forward; up.y = 0; up.Normalize();   // screen up = camera forward on the ground
            double[,] pts = { { 8, 36 }, { 104, 36 }, { 56, 40 }, { 56, 8 }, { 18, 37 }, { 94, 37 }, { 56, 65 } };
            Vector2 o = p.ToPixel(0, 0);
            for (int i = 0; i < pts.GetLength(0); i++)
            {
                Vector3 wp = SimSpace.ToWorld(pts[i, 0], 0, pts[i, 1]);
                Vector2 expect = o + new Vector2(Vector3.Dot(wp, right), -Vector3.Dot(wp, up)) * (float)MinimapProjection.PxPerUnit;
                Vector2 got = p.ToPixel(pts[i, 0], pts[i, 1]);
                Assert.Less(Vector2.Distance(expect, got), 0.05f, $"point {i}: camera says {expect}, minimap says {got}");
                p.ToSim(got.x, got.y, 1, out double bx, out double bz);
                Assert.AreEqual(pts[i, 0], bx, 1e-6);
                Assert.AreEqual(pts[i, 1], bz, 1e-6);
            }
            // camera-up at yaw 45: Azure base upper left of Crimson, shrine above the corruption island
            Vector2 az = p.ToPixel(8, 36), cr = p.ToPixel(104, 36), sh = p.ToPixel(56, 8), so = p.ToPixel(56, 65);
            Assert.Less(az.x, cr.x);
            Assert.Less(az.y, cr.y);
            Assert.Less(sh.y, so.y);
        }

        static bool ClassNear(MinimapProjection p, double x, double z, MinimapClass want)
        {
            Vector2 px = p.ToPixel(x, z);
            int i0 = Mathf.FloorToInt(px.x), j0 = Mathf.FloorToInt(px.y);
            for (int dj = -1; dj <= 1; dj++)
                for (int di = -1; di <= 1; di++)
                {
                    p.ToSim(i0 + di + 0.5, j0 + dj + 0.5, 1, out double sx, out double sz);
                    Bake.Shade(sx, sz, 1, out MinimapClass k);
                    if (k == want) return true;
                }
            return false;
        }

        [Test]
        public void KnownWorldPointsLandOnTheirMinimapPixels()
        {
            MapDef m = MinimapGeometry.BakedMap;
            MinimapProjection p = MinimapGeometry.Projection;
            for (int t = 0; t < 2; t++)
            {
                Vector2 px = p.ToPixel(m.Pads[t].X, m.Pads[t].Z);
                Assert.IsTrue(px.x >= 0 && px.y >= 0 && px.x < p.Width && px.y < p.Height);
                Assert.IsTrue(ClassNear(p, m.Pads[t].X, m.Pads[t].Z, MinimapClass.Pad), "pad " + t);
            }
            Assert.IsTrue(ClassNear(p, m.Hill.X, m.Hill.Z, MinimapClass.Hill), "hill centre");
            Assert.IsTrue(ClassNear(p, m.Shrine.X, m.Shrine.Z, MinimapClass.Wall), "shrine");
            int ends = 0;
            foreach (SkybreakBasinMap.Bridge b in SkybreakBasinMap.Bridges)
                for (int mirror = 0; mirror < (b.Mirror ? 2 : 1); mirror++)
                {
                    double x0 = mirror == 0 ? b.X0 : 112 - b.X0, x1 = mirror == 0 ? b.X1 : 112 - b.X1;
                    for (int end = 0; end < 2; end++)
                    {
                        double ax = end == 0 ? x0 : x1, az = end == 0 ? b.Z0 : b.Z1, bx = end == 0 ? x1 : x0, bz = end == 0 ? b.Z1 : b.Z0;
                        double len = System.Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
                        // walk in from the end to the first Bridge cell, then one cell further: the bridge's end on the map
                        double s = 0;
                        while (s < len && m.Cells[(int)(az + (bz - az) * s / len) * m.Width + (int)(ax + (bx - ax) * s / len)] != Cell.Bridge) s += 0.25;
                        Assert.Less(s, len, "bridge without Bridge cells");
                        s += 1.0;
                        double ex = ax + (bx - ax) * s / len, ez = az + (bz - az) * s / len;
                        Assert.IsTrue(ClassNear(p, ex, ez, MinimapClass.Bridge), $"bridge end ({ex:0.0}, {ez:0.0}) is not drawn as planks within 1 px");
                        ends++;
                    }
                }
            Assert.AreEqual(32, ends);
        }

        [Test]
        public void HudMarkersUseTheBakedProjection()
        {
            World w = NewWorld();
            var go = new GameObject("HUD minimap test");
            try
            {
                GameHud hud = go.AddComponent<GameHud>();
                hud.Build();
                hud.Refresh(w, 0.016f);
                MinimapProjection p = hud.MapProjection;
                Assert.AreSame(MinimapGeometry.Projection, p);
                Assert.AreEqual(p.Width, hud.MapArea.rect.width, 0.01f);
                Assert.AreEqual(p.Height, hud.MapArea.rect.height, 0.01f);
                Assert.IsNotNull(hud.MapArea.GetComponent<RawImage>().texture, "baked minimap texture missing");
                for (int t = 0; t < 2; t++)
                {
                    var banner = (RectTransform)hud.MapArea.Find((TeamId)t + " Base");
                    Vector2 px = p.ToPixel(w.Map.Pads[t].X, w.Map.Pads[t].Z);
                    Assert.Less(Vector2.Distance(banner.anchoredPosition, new Vector2(px.x, -px.y)), 1f, "banner " + t);
                }
                var hill = (RectTransform)hud.MapArea.Find("Map Hill");
                Vector2 hp = p.ToPixel(w.Map.Hill.X, w.Map.Hill.Z);
                Assert.Less(Vector2.Distance(hill.anchoredPosition, new Vector2(hp.x, -hp.y)), 1f, "hill");
                Rect frame = HudLayout.RefRect(HudCluster.Minimap);
                Assert.AreEqual(p.Width + 2 * HudLayout.MinimapFrame, frame.width, 0.01f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void FacingTurnsTheYouArrowLikeTheWorld()
        {
            MinimapProjection p = MinimapGeometry.Projection;
            for (int k = 0; k < 8; k++)
            {
                double f = k * System.Math.PI / 4;
                Vector2 a = p.ToPixel(50, 40), b = p.ToPixel(50 + System.Math.Cos(f), 40 + System.Math.Sin(f));
                Vector2 dirUi = new Vector2(b.x - a.x, -(b.y - a.y)).normalized;   // y up
                Vector2 arrow = Quaternion.Euler(0, 0, p.FacingToUiDeg(f)) * Vector3.right;
                Assert.Less(Vector2.Distance(dirUi, arrow), 1e-3f, "facing " + k);
            }
        }

        [Test]
        public void TitleUsesTheBibleLogoCutOutAtThreeTiers()
        {
            const string dir = "Assets/_Game/Resources/UI/Logo/";
            Color32[] px = LoadTopDown(dir + "critical-mass-bible-logo@1x.png", out int w, out int h);
            Assert.AreEqual((int)TitleScreen.LogoSize.x, w);
            Assert.AreEqual((int)TitleScreen.LogoSize.y, h);
            LoadTopDown(dir + "critical-mass-bible-logo@2x.png", out int w2, out _);
            LoadTopDown(dir + "critical-mass-bible-logo@4x.png", out int w4, out _);
            Assert.AreEqual(w * 2, w2);
            Assert.AreEqual(w * 4, w4);
            Assert.AreEqual(0, px[0].a, "corner must be transparent");
            Assert.AreEqual(0, px[px.Length - 1].a, "corner must be transparent");
            // no cream halo: soft-edge pixels must not be page cream (#FEFAEF)
            int edge = 0, creamy = 0;
            foreach (Color32 c in px)
            {
                if (c.a < 25 || c.a > 230) continue;
                edge++;
                if (c.r > 235 && c.g > 230 && c.b > 215) creamy++;
            }
            TestContext.WriteLine($"logo {w}x{h}: {edge} soft-edge pixels, {creamy} cream-coloured");
            Assert.Greater(edge, 500);
            Assert.Less(creamy, edge * 0.05f, "cream halo on the cut-out edge");
            foreach (string f in new[] { "critical-mass-logo@1x.png", "critical-mass-bible-logo-glow@1x.png", "critical-mass-bible-badge@1x.png" })
                Assert.IsTrue(File.Exists(dir + f), f);
            Assert.IsTrue(File.Exists("Assets/_Game/Art/UI/Logo/_source~/critical-mass-logo.svg"), "vector fallback must stay");
        }
    }
}
