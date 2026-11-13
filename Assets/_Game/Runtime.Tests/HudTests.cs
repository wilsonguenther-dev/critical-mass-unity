using System;
using System.IO;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>Bible p05 tokens, the p15 layout at 720p / 1080p / 4K, and the HUD's read of the Sim (hud-and-logo brief).</summary>
    [TestFixture]
    public sealed class HudTests
    {
        // ---- tokens: the bible's own hexes, typed out here independently of Palette.cs ----
        [TestCase("Void Navy", "#18233B")]
        [TestCase("Azure Team", "#16C7D9")]
        [TestCase("Crimson Team", "#FF5A5F")]
        [TestCase("Warm Cream", "#F7F0E5")]
        [TestCase("Biomass", "#B7F238")]
        [TestCase("Corruption", "#8E3FD1")]
        [TestCase("Stone", "#6F7282")]
        [TestCase("Tool Brass", "#C79245")]
        public void BibleSwatchMatchesP05(string name, string hex)
        {
            Swatch s = Array.Find(CMTokens.Bible, x => x.Name == name);
            Assert.AreEqual(name, s.Name, "missing bible swatch " + name);
            Assert.AreEqual(hex, s.Hex);
            Color32 c = s.Color;
            Assert.AreEqual(Convert.ToInt32(hex.Substring(1, 2), 16), c.r);
            Assert.AreEqual(Convert.ToInt32(hex.Substring(3, 2), 16), c.g);
            Assert.AreEqual(Convert.ToInt32(hex.Substring(5, 2), 16), c.b);
        }

        [Test]
        public void TokenNamesAreUniqueAndBibleComesFirst()
        {
            Assert.AreEqual(8, CMTokens.Bible.Length);
            for (int i = 0; i < CMTokens.Bible.Length; i++) Assert.AreEqual(CMTokens.Bible[i].Name, CMTokens.All[i].Name);
            for (int i = 0; i < CMTokens.All.Length; i++)
                for (int j = i + 1; j < CMTokens.All.Length; j++)
                    Assert.AreNotEqual(CMTokens.All[i].Name, CMTokens.All[j].Name);
        }

        [Test]
        public void ToonShaderOutlineDefaultIsVoidNavy()
        {
            Shader sh = Shader.Find("CM/Toon");
            if (sh == null) Assert.Ignore("CM/Toon not found");
            var m = new Material(sh);
            try
            {
                Color32 c = m.GetColor("_OutlineColor");
                Color32 navy = CMTokens.VoidNavy.Color;
                Assert.LessOrEqual(Math.Abs(c.r - navy.r), 1);
                Assert.LessOrEqual(Math.Abs(c.g - navy.g), 1);
                Assert.LessOrEqual(Math.Abs(c.b - navy.b), 1);
            }
            finally { UnityEngine.Object.DestroyImmediate(m); }
        }

        [Test]
        public void FontsShipWithAnOpenLicence()
        {
            foreach (Typeface t in CMTokens.Typefaces)
            {
                Assert.IsNotNull(Resources.Load<Font>(t.Resource), t.Family + " TTF missing at Resources/" + t.Resource);
                Assert.AreEqual("SIL Open Font License 1.1", t.Licence);
            }
            string dir = Path.Combine(Application.dataPath, "_Game/Resources/UI/Fonts");
            foreach (string f in new[] { "Bungee-OFL.txt", "NunitoSans-OFL.txt", "JetBrainsMono-OFL.txt" })
            {
                string p = Path.Combine(dir, f);
                Assert.IsTrue(File.Exists(p), p);
                StringAssert.Contains("SIL OPEN FONT LICENSE Version 1.1", File.ReadAllText(p));
            }
        }

        // ---- layout ----
        [TestCase(1280, 720)]
        [TestCase(1920, 1080)]
        [TestCase(3840, 2160)]
        public void LayoutHoldsAtResolution(int w, int h)
        {
            float s = HudLayout.Scale(w, h);
            var rects = new Rect[4];
            float area = 0;
            for (int i = 0; i < 4; i++)
            {
                rects[i] = HudLayout.PixelRect((HudCluster)i, w, h);
                Rect r = rects[i];
                Assert.GreaterOrEqual(r.xMin, -0.01f, (HudCluster)i + " off the left");
                Assert.GreaterOrEqual(r.yMin, -0.01f, (HudCluster)i + " off the top");
                Assert.LessOrEqual(r.xMax, w + 0.01f, (HudCluster)i + " off the right");
                Assert.LessOrEqual(r.yMax, h + 0.01f, (HudCluster)i + " off the bottom");
                area += r.width * r.height;
            }
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++)
                    Assert.IsFalse(rects[i].Overlaps(rects[j]), (HudCluster)i + " overlaps " + (HudCluster)j);
            // bounding rects are an upper bound of the drawn area; the 10% check is by pixel mask in the capture REPORT.md
            Assert.LessOrEqual(area / (w * h), 0.15f, "cluster bounding area");
            Assert.GreaterOrEqual(HudLayout.AbilityButton * s, HudLayout.AbilityButtonPx * s - 0.01f);
            // the 1080p layout is the same picture at every 16:9 size: positions scale exactly
            Rect r1080 = HudLayout.PixelRect(HudCluster.Minimap, 1920, 1080);
            Assert.AreEqual(r1080.x * s, rects[(int)HudCluster.Minimap].x, 0.01f);
            Assert.AreEqual(r1080.width * s, rects[(int)HudCluster.Minimap].width, 0.01f);
        }

        [Test]
        public void MatchClusterIsTopCentreAndPlayerBottomLeft()
        {
            Rect m = HudLayout.PixelRect(HudCluster.Match, 1920, 1080);
            Assert.AreEqual(960f, m.center.x, 0.5f);
            Assert.AreEqual(0f, m.yMin, 0.5f);
            Rect p = HudLayout.PixelRect(HudCluster.Player, 1920, 1080);
            Assert.Less(p.xMin, 100f);
            Assert.Greater(p.yMax, 1000f);
            Rect k = HudLayout.PixelRect(HudCluster.Kaiju, 1920, 1080);
            Assert.Greater(k.xMax, 1850f);
            Assert.Less(k.yMin, 20f);
        }

        [Test]
        public void AnchoredPositionsAgreeWithPixelRects()
        {
            for (int i = 0; i < 4; i++)
            {
                var c = (HudCluster)i;
                Vector2 a = HudLayout.Anchor(c), pos = HudLayout.AnchoredPosition(c);
                Rect r = HudLayout.RefRect(c);
                // pivot = anchor: the pivot's screen point (y up) = anchor point + anchoredPosition
                float pivotX = a.x * HudLayout.RefW + pos.x;
                float pivotYUp = a.y * HudLayout.RefH + pos.y;
                Assert.AreEqual(r.x + a.x * r.width, pivotX, 0.01f, c + " x");
                Assert.AreEqual(HudLayout.RefH - (r.y + (1 - a.y) * r.height), pivotYUp, 0.01f, c + " y");
            }
        }

        // ---- binding ----
        static World NewWorld()
        {
            return World.CreateWorld(new WorldOptions
            {
                Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateDefault(),
            });
        }

        static void Run(World w, int ticks)
        {
            var scratch = new InputFrame();
            for (int i = 0; i < ticks; i++) MatchTick.AdvanceNoHuman(w, scratch);
        }

        [Test]
        public void BinderReadsTheSim()
        {
            World w = NewWorld();
            Run(w, 900);
            var s = new HudState();
            CriticalMass.Runtime.Flow.MatchSpeed.DisplayPace = 1;   // sim seconds = wall seconds (Play Now / speed choice)
            HudBinder.Read(w, HudBinder.LocalActor, s);
            Assert.AreEqual(Math.Max(0, w.Config.MatchSeconds - w.Match.Time), s.ClockRemaining, 1e-9);
            Assert.AreEqual((int)Math.Floor(w.Match.HillSeconds.Azure), s.ScoreAzure);
            Assert.AreEqual((int)Math.Floor(w.Match.HillSeconds.Crimson), s.ScoreCrimson);
            Assert.AreEqual(w.Match.HillState, s.Hill);
            Assert.AreEqual(w.Kaiju.State, s.Kaiju);
            Assert.IsTrue(s.HasPlayer);
            Assert.AreEqual(w.Actors[0].Team, s.Team);
            Assert.AreEqual(w.Actors[0].Cls, s.Cls);
            Assert.AreEqual(w.Actors[0].CarryAmount, s.CarryAmount);
            Assert.That(s.Hp01, Is.InRange(0f, 1f));
            Assert.That(s.KaijuMeter01, Is.InRange(0f, 1f));
            Assert.That(s.HillShareAzure, Is.InRange(0f, 1f));
        }

        [Test]
        public void BinderNeverWritesTheSim()
        {
            World w = NewWorld();
            Run(w, 300);
            int tick = w.Tick;
            double t = w.Match.Time, hp = w.Actors[0].Hp, x = w.X[0];
            var s = new HudState();
            for (int i = 0; i < 10; i++) HudBinder.Read(w, HudBinder.LocalActor, s);
            Assert.AreEqual(tick, w.Tick);
            Assert.AreEqual(t, w.Match.Time);
            Assert.AreEqual(hp, w.Actors[0].Hp);
            Assert.AreEqual(x, w.X[0]);
        }

        [Test]
        public void BinderAllocatesNothingPerFrame()
        {
            World w = NewWorld();
            Run(w, 300);
            var s = new HudState();
            HudBinder.Read(w, HudBinder.LocalActor, s);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) HudBinder.Read(w, HudBinder.LocalActor, s);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, bytes, "HudBinder.Read allocated " + bytes + " bytes over 1000 frames");
        }

        [Test]
        public void HudRefreshAllocatesNothingAfterWarmUp()
        {
            World w = NewWorld();
            Run(w, 600);
            var go = new GameObject("HUD test");
            try
            {
                GameHud hud = go.AddComponent<GameHud>();
                hud.Build();
                hud.Refresh(w, 0.016f);
                hud.Refresh(w, 0.016f);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 120; i++)
                {
                    w.Actors[0].Hp = w.Actors[0].MaxHp * (1.0 - (i % 40) / 50.0);   // the test's own copy: health changes
                    hud.Refresh(w, 0.016f);
                }
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                TestContext.WriteLine("GameHud.Refresh allocated " + bytes + " bytes over 120 frames");
                Assert.AreEqual(0, bytes, "GameHud.Refresh allocated " + bytes + " bytes over 120 frames");
                Assert.AreEqual(4, hud.Clusters.Length);
                foreach (RectTransform c in hud.Clusters) Assert.IsNotNull(c);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void NumberCacheFormatsLikeTheDebugHud()
        {
            Assert.AreEqual(DebugHud.Mmss(487.2), NumberCache.Mmss(487.2));
            Assert.AreEqual(DebugHud.Mmss(0), NumberCache.Mmss(0));
            Assert.AreEqual("412", NumberCache.Int(412));
        }

        [Test]
        public void VectorOutlinesAreCounterClockwiseAndOffsetOutward()
        {
            var pts = new System.Collections.Generic.List<Vector2>();
            var off = new System.Collections.Generic.List<Vector2>();
            foreach (ShapeKind k in Enum.GetValues(typeof(ShapeKind)))
            {
                if (k == ShapeKind.Pie) continue;
                VectorShape.BuildOutline(k, new Rect(0, 0, 100, 80), 10f, pts);
                Assert.GreaterOrEqual(pts.Count, 3, k.ToString());
                Assert.Greater(Area(pts), 0f, k + " is not counter-clockwise");
                VectorShape.Offset(pts, 3f, off);
                Assert.Greater(Area(off), Area(pts), k + " outward offset shrank");
            }
        }

        // ---- v2: illustrated icons, class portraits, death grade ----

        [Test]
        public void EveryIconShipsAtThreeTiersAtDoubleAndQuadrupleSize()
        {
            int n = 0;
            foreach (string name in UiIcons.All())
            {
                Texture2D t1 = UiIcons.Get(name, 1), t2 = UiIcons.Get(name, 2), t4 = UiIcons.Get(name, 4);
                Assert.IsNotNull(t1, name + "@1x missing");
                Assert.IsNotNull(t2, name + "@2x missing");
                Assert.IsNotNull(t4, name + "@4x missing");
                Assert.AreEqual(t1.width * 2, t2.width, name + " @2x is not twice @1x");
                Assert.AreEqual(t1.width * 4, t4.width, name + " @4x is not four times @1x");
                Assert.Greater(t4.mipmapCount, 1, name + "@4x has no mipmaps (it is drawn smaller than its size)");
                n++;
            }
            Assert.GreaterOrEqual(n, 60, "icon catalogue shrank");
            string svg = Path.Combine(Application.dataPath, "_Game/Art/UI/Icons/_source~/svg");
            // hud-art-1: the painted icons have no vector source; each is listed in the raster manifest with its rebuild script
            string rasterList = Path.Combine(Application.dataPath, "_Game/Art/UI/Icons/_source~/hud-art-1-raster.txt");
            var raster = new System.Collections.Generic.HashSet<string>(File.Exists(rasterList) ? File.ReadAllLines(rasterList) : new string[0]);
            foreach (string name in UiIcons.All())
                Assert.IsTrue(File.Exists(Path.Combine(svg, name + ".svg")) || raster.Contains(name), name + ": no vector source and not in the raster manifest");
        }

        [Test]
        public void EveryClassHasAPortraitBadgeAttackAndAbilityPerTeam()
        {
            foreach (ClassId c in Enum.GetValues(typeof(ClassId)))
            foreach (TeamId t in Enum.GetValues(typeof(TeamId)))
            {
                Assert.IsNotNull(UiIcons.Get(UiIcons.Portrait[(int)c, (int)t], 2), c + " " + t + " portrait");
                Assert.IsNotNull(UiIcons.Get(UiIcons.ClassBadge[(int)c, (int)t], 2), c + " " + t + " badge");
                Assert.IsNotNull(UiIcons.Get(UiIcons.Attack[(int)c, (int)t], 2), c + " " + t + " attack");
                Assert.IsNotNull(UiIcons.Get(UiIcons.Ability[(int)c, (int)t], 2), c + " " + t + " ability");
            }
        }

        [TestCase(1280, 720, 1)]
        [TestCase(1920, 1080, 2)]
        [TestCase(3840, 2160, 4)]
        public void IconTierNeverUpscales(int w, int h, int tier)
        {
            float s = HudLayout.Scale(w, h);
            Assert.AreEqual(tier, UiIcons.TierFor(s));
            Assert.GreaterOrEqual(UiIcons.TierFor(s), s, "an icon texture would be stretched above its size");
        }

        [Test]
        public void HudShowsTheLocalClassPortraitAndCrests()
        {
            World w = NewWorld();
            Run(w, 60);
            var go = new GameObject("HUD test");
            try
            {
                GameHud hud = go.AddComponent<GameHud>();
                hud.Build();
                hud.Refresh(w, 0.016f);
                int cls = (int)w.Actors[0].Cls, team = (int)w.Actors[0].Team;
                bool portrait = false, crestA = false, crestC = false;
                foreach (HudIcon i in hud.Icons)
                {
                    if (i.Name == UiIcons.Portrait[cls, team]) portrait = i.Image.texture != null;
                    if (i.Name == UiIcons.Crest[0]) crestA = i.Image.texture != null;
                    if (i.Name == UiIcons.Crest[1]) crestC = i.Image.texture != null;
                }
                Assert.IsTrue(portrait, "player card does not show " + UiIcons.Portrait[cls, team]);
                Assert.IsTrue(crestA && crestC, "team crests missing");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void DeathGradeShaderShipsAndTargetsTheBrief()
        {
            Shader sh = Resources.Load<Shader>(DeathGrade.ShaderResource);
            Assert.IsNotNull(sh, "death grade shader missing from Resources");
            Assert.IsTrue(sh.isSupported, "death grade shader does not compile (a shader with errors draws nothing)");
            Material m = DeathGrade.GradeMaterial;
            Assert.IsNotNull(m);
            Assert.AreEqual(0.7f, m.GetFloat("_Brightness"), 1e-4f);
            Assert.AreEqual(0.6f, m.GetFloat("_Saturation"), 1e-4f);
        }

        static float Area(System.Collections.Generic.List<Vector2> p)
        {
            float a = 0;
            for (int i = 0; i < p.Count; i++)
            {
                Vector2 u = p[i], v = p[(i + 1) % p.Count];
                a += u.x * v.y - v.x * u.y;
            }
            return a * 0.5f;
        }
    }
}
