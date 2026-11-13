using System.Collections.Generic;
using System.IO;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.View.Moments;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// hud-art-1 (2026-11-13): the owner saw the Kaiju cut-in as "a whole game card" pasted over the game. Every cut-in is now a
    /// true alpha cut-out, every HUD icon ships at all three tiers with clean alpha, and the takeovers stay on screen long
    /// enough to read. These tests read the committed PNG files themselves (not the imported textures), so they hold for the
    /// art as shipped.
    /// </summary>
    public class HudArtTests
    {
        const string Moments = "Assets/_Game/Resources/UI/BigMoments/";
        const string Icons = "Assets/_Game/Resources/UI/Icons/";

        static Texture2D Png(string path)
        {
            Assert.IsTrue(File.Exists(path), path + " is missing");
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.IsTrue(t.LoadImage(File.ReadAllBytes(path)), path + " does not decode");
            return t;
        }

        /// <summary>Alpha facts of a cut-out: max alpha in each corner patch, share of pixels over half alpha, any partial alpha.</summary>
        static void Alpha(Texture2D t, out float cornerMax, out float coverage, out bool soft)
        {
            Color32[] px = t.GetPixels32();
            int w = t.width, h = t.height, k = Mathf.Max(2, w / 64);
            cornerMax = 0f;
            int over = 0, partial = 0;
            for (int i = 0; i < px.Length; i++)
            {
                byte a = px[i].a;
                if (a > 127) over++;
                if (a > 8 && a < 247) partial++;
            }
            for (int y = 0; y < k; y++)
                for (int x = 0; x < k; x++)
                {
                    cornerMax = Mathf.Max(cornerMax, px[y * w + x].a, px[y * w + (w - 1 - x)].a, px[(h - 1 - y) * w + x].a,
                        px[(h - 1 - y) * w + (w - 1 - x)].a);
                }
            cornerMax /= 255f;
            coverage = (float)over / px.Length;
            soft = partial > 0;
        }

        static IEnumerable<string> CutIns()
        {
            foreach (ClassId c in ClassIds.All)
                foreach (TeamId t in new[] { TeamId.Azure, TeamId.Crimson })
                    yield return MomentArt.PortraitName(c, t);
            foreach (KaijuState s in new[] { KaijuState.Neutral, KaijuState.Azure, KaijuState.Crimson, KaijuState.Berserk })
                yield return MomentArt.KaijuName(s);
        }

        [Test]
        public void EveryCutInIsATrueCutOutNotARectangle()
        {
            int n = 0;
            foreach (string name in CutIns())
            {
                Texture2D t = Png(Moments + name + ".png");
                try
                {
                    Assert.GreaterOrEqual(t.width, 1024, name + " width");
                    Assert.AreEqual(t.width, t.height, name + " is square (the portrait slot)");
                    Alpha(t, out float corner, out float cover, out bool soft);
                    Assert.AreEqual(0f, corner, 1e-6f, name + ": a corner is not transparent (a pasted card)");
                    Assert.That(cover, Is.InRange(0.15f, 0.70f), name + ": silhouette coverage");
                    Assert.IsTrue(soft, name + ": no anti-aliased edge");
                    n++;
                }
                finally { Object.DestroyImmediate(t); }
            }
            Assert.AreEqual(16, n, "6 classes x 2 teams + 4 Kaiju states");
        }

        [Test]
        public void EveryCutInResolvesToItsOwnImageAheadOfThePainting()
        {
            MomentArt.ResetLoader();
            try
            {
                foreach (ClassId c in ClassIds.All)
                    foreach (TeamId t in new[] { TeamId.Azure, TeamId.Crimson })
                        Assert.AreEqual(PortraitSource.TeamImage, MomentArt.ResolvePortrait(c, t, true, true, out _, out _), c + " " + t);
                foreach (KaijuState s in new[] { KaijuState.Neutral, KaijuState.Azure, KaijuState.Crimson, KaijuState.Berserk })
                    Assert.AreEqual(PortraitSource.TeamImage, MomentArt.ResolveKaiju(s, true, true, out _), s.ToString());
            }
            finally { MomentArt.ResetLoader(); }
        }

        [Test]
        public void ThePanelAndPlatesShipWithAlpha()
        {
            var names = new List<string> { MomentArt.Band, MomentArt.PlateAccent };
            names.AddRange(new[] { "bm-title-plate", "bm-title-plate-impact", "bm-title-plate-ribbon" });
            foreach (string name in names)
            {
                Texture2D t = Png(Moments + name + ".png");
                try
                {
                    Alpha(t, out _, out float cover, out bool soft);
                    Assert.IsTrue(soft, name + " has an anti-aliased edge");
                    Assert.That(cover, Is.InRange(0.02f, 0.95f), name + " is a shape, not a full rectangle");
                }
                finally { Object.DestroyImmediate(t); }
            }
        }

        [Test]
        public void EveryHudIconShipsAtThreeTiersWithCleanAlpha()
        {
            var cutOuts = new HashSet<string> { UiIcons.Egg, UiIcons.EggCracking, UiIcons.Hatchling, UiIcons.Coin };
            foreach (string s in UiIcons.Adult) cutOuts.Add(s);
            foreach (string s in UiIcons.Stock) cutOuts.Add(s);
            foreach (string s in UiIcons.Items) cutOuts.Add(s);
            var fresh = new List<string>(cutOuts);
            foreach (string[,] g in new[] { UiIcons.Portrait, UiIcons.Attack, UiIcons.Ability, UiIcons.ClassBadge })
                foreach (string s in g) fresh.Add(s);
            foreach (string name in fresh)
            {
                int w1 = -1;
                foreach (int tier in UiIcons.Tiers)
                {
                    Texture2D t = Png(Icons + name + "@" + tier + "x.png");
                    try
                    {
                        if (tier == 1) w1 = t.width;
                        Assert.AreEqual(w1 * tier, t.width, name + "@" + tier + "x width is " + tier + "x the 1x");
                        Alpha(t, out float corner, out float cover, out bool soft);
                        Assert.Less(corner, 0.05f, name + "@" + tier + "x: opaque corner");
                        Assert.That(cover, Is.InRange(0.10f, 0.99f), name + "@" + tier + "x coverage");
                        if (tier > 1) Assert.IsTrue(soft, name + "@" + tier + "x: no anti-aliased edge");
                    }
                    finally { Object.DestroyImmediate(t); }
                }
            }
        }

        [Test]
        public void TheHealerHasItsOwnIconsAndTheEggCracksBeforeTheHatch()
        {
            Assert.AreEqual(6, UiIcons.Classes.Length, "six classes");
            Assert.AreEqual("portrait-healer-azure", UiIcons.Portrait[(int)ClassId.Healer, 0]);
            Assert.AreEqual("ability-healer-crimson", UiIcons.Ability[(int)ClassId.Healer, 1]);
            Assert.That(CriticalMass.Runtime.Hud.GameHud.CrackSeconds, Is.InRange(10.0, 60.0));
        }

        [Test]
        public void CutInsStayLongEnoughToReadAndEaseInAndOut()
        {
            Assert.AreEqual(2.2f, MomentQueue.MaxSeconds, 1e-6f);
            Assert.GreaterOrEqual(MomentQueue.StingerSeconds, 1.6f);
            foreach (MomentId id in new[] { MomentId.Hatch, MomentId.Berserk, MomentId.AllegianceTurns, MomentId.Finisher })
                Assert.That(MomentQueue.TakeoverSeconds(MomentRecipes.Get(id), false), Is.InRange(2.0f, 2.2f), id.ToString());
            // the entry overshoots a little and settles; the exit starts slow
            Assert.Greater(BigMoments.BackOut(0.7f), 1f);
            Assert.AreEqual(1f, BigMoments.BackOut(1f), 1e-5f);
            Assert.Less(BigMoments.EaseIn(0.3f), 0.3f);
            Assert.Greater(BigMoments.PortraitIn + BigMoments.PortraitOut, 0.4f);
        }

        [Test]
        public void TheMapEventPillNeverRunsUnderItsClock()
        {
            // Art lead: "EVERY DELIVERY COUNTS DOUB0:10". The pill shows a short line, ends 10 px before the clock
            // (x 470 of 560) and shrinks the line to fit instead of overflowing.
            Assert.LessOrEqual(14f + CriticalMass.Runtime.Hud.Mechanics.DepthHud.WordW, 560f - 90f - 10f + 0.01f);
            string[] lines =
            {
                CriticalMass.Runtime.View.Mechanics.DepthWords.EggExposedPill, CriticalMass.Runtime.View.Mechanics.DepthWords.DropIncoming,
                CriticalMass.Runtime.View.Mechanics.DepthWords.DropLanded, CriticalMass.Runtime.View.Mechanics.DepthWords.DropContested,
                CriticalMass.Runtime.View.Mechanics.DepthWords.BridgeWarning, CriticalMass.Runtime.View.Mechanics.DepthWords.BridgeCollapsed,
            };
            foreach (string l in lines)
                Assert.LessOrEqual(l.Length, CriticalMass.Runtime.View.Mechanics.DepthWords.PillMaxChars, l);
        }
    }
}
