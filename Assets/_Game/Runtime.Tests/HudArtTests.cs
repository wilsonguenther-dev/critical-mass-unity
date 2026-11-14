using System.Collections.Generic;
using System.IO;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.View.Moments;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

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

        // ---------------------------------------------------------------- hud-overlaps-1 (2026-11-13)
        // Two overlaps in the installed build: the map-event pill's line printed over its clock, and the ability names
        // over the bottom-left buttons printed over each other when one was wide. These tests measure real labels in
        // the HUD's own fonts (no graphics device needed) and check the layout in reference px at every screen shape.

        /// <summary>16:9, 16:10, 21:9, 4:3 and the small 1280x720.</summary>
        static readonly Vector2[] Shapes = { new Vector2(1920, 1080), new Vector2(1920, 1200), new Vector2(2560, 1080), new Vector2(1024, 768), new Vector2(1280, 720) };

        static Text Probe(GameObject root, CriticalMass.Runtime.Hud.Style.Typeface face, int size, TextAnchor anchor, float x, float w, float h)
        {
            Text t = HudText.Make(root.transform, "Probe", face, size, Color.white, anchor, 2f);
            RectTransform rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x + w * 0.5f, -h * 0.5f);
            Assert.IsNotNull(t.font, "the HUD font loads");
            return t;
        }

        /// <summary>Left and right edge of the text actually drawn in a label (by its alignment), label-parent px.</summary>
        static Vector2 Ink(Text t)
        {
            Rect r = t.rectTransform.rect;
            float x0 = t.rectTransform.anchoredPosition.x - r.width * 0.5f, w = t.preferredWidth;
            switch (t.alignment)
            {
                case TextAnchor.MiddleLeft: case TextAnchor.UpperLeft: case TextAnchor.LowerLeft: return new Vector2(x0, x0 + w);
                case TextAnchor.MiddleRight: case TextAnchor.UpperRight: case TextAnchor.LowerRight: return new Vector2(x0 + r.width - w, x0 + r.width);
                default: return new Vector2(x0 + (r.width - w) * 0.5f, x0 + (r.width + w) * 0.5f);
            }
        }

        static void ClearAtEveryShape(float gapRef, string what)
        {
            foreach (Vector2 sh in Shapes)
            {
                float k = CriticalMass.Runtime.Hud.HudLayout.Scale(sh.x, sh.y);
                // two 2 px text outlines face each other across the gap; at least 1 screen px must stay clear
                Assert.GreaterOrEqual((gapRef - 4f) * k, 1f, what + " at " + sh.x + "x" + sh.y + " (gap " + gapRef.ToString("0.0") + " ref px)");
            }
        }

        [Test]
        public void TheAbilityNamesNeverOverlapForAnyClassOrCarry()
        {
            var root = new GameObject("Hud Overlap Probe", typeof(RectTransform));
            try
            {
                var labels = new Text[3];
                for (int i = 0; i < 3; i++)
                    labels[i] = Probe(root, CriticalMass.Runtime.Hud.Style.CMTokens.Ui, 16, TextAnchor.MiddleCenter, 0f, 76f, 22f);
                labels[0].text = "WALL BREAK";
                Assert.Greater(labels[0].preferredWidth, 40f, "the font measures text in this run");
                string[][] slots = CriticalMass.Runtime.Hud.GameHud.AbilitySlotNames();
                float[] widths = new float[3], lefts = new float[3], boxes = new float[3];
                var sets = new List<string[]>();
                // every class's real row with every carry word
                for (int cls = 0; cls < slots[1].Length; cls++)
                    foreach (string carry in slots[2]) sets.Add(new[] { slots[0][cls], slots[1][cls], carry });
                // and the stress row: the longest name of each slot side by side, and the longest name of all in every slot
                string longest = "";
                foreach (string[] slot in slots) foreach (string n in slot) if (n.Length > longest.Length) longest = n;
                sets.Add(new[] { Longest(slots[0]), Longest(slots[1]), Longest(slots[2]) });
                sets.Add(new[] { longest, longest, longest });
                int cut = 0;
                foreach (string[] names in sets)
                {
                    int size = CriticalMass.Runtime.Hud.GameHud.LayoutAbilityNameTexts(labels, names, widths, lefts, boxes);
                    string row = string.Join(" | ", names) + " @" + size;
                    Assert.That(size, Is.InRange(CriticalMass.Runtime.Hud.HudLayout.AbilityNameFloor, CriticalMass.Runtime.Hud.HudLayout.AbilityNameMax), row);
                    var ink = new Vector2[3];
                    for (int i = 0; i < 3; i++)
                    {
                        ink[i] = Ink(labels[i]);
                        Rect box = labels[i].rectTransform.rect;
                        Assert.LessOrEqual(labels[i].preferredWidth, box.width + 0.5f, row + ": name " + i + " wider than its box");
                        float centre = (ink[i].x + ink[i].y) * 0.5f, b0 = CriticalMass.Runtime.Hud.HudLayout.AbilityX0 + i * CriticalMass.Runtime.Hud.HudLayout.AbilityPitch;
                        Assert.That(centre, Is.InRange(b0 - 0.5f, b0 + CriticalMass.Runtime.Hud.HudLayout.AbilityButton + 0.5f), row + ": name " + i + " left its button");
                        if (labels[i].text != names[i]) cut++;
                    }
                    for (int i = 0; i + 1 < 3; i++)
                    {
                        float boxGap = lefts[i + 1] - (lefts[i] + boxes[i]);
                        Assert.GreaterOrEqual(boxGap, CriticalMass.Runtime.Hud.HudLayout.AbilityNameGap - 0.01f, row + ": boxes " + i + "/" + (i + 1));
                        ClearAtEveryShape(ink[i + 1].x - ink[i].y, row + ": names " + i + "/" + (i + 1));
                    }
                    if (names[0] != longest || names[1] != longest)   // real rows: the whole name always shows
                        for (int i = 0; i < 3; i++) Assert.AreEqual(names[i], labels[i].text, row + ": a real name was cut");
                }
                Debug.Log("hud-overlaps-1: ability name rows checked " + sets.Count + ", cut with an ellipsis " + cut + " (stress rows only)");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static string Longest(string[] a)
        {
            string l = "";
            foreach (string s in a) if (s.Length > l.Length) l = s;
            return l;
        }

        [Test]
        public void ThePillLineAndTheKaijuWordNeverReachTheirNeighbours()
        {
            var root = new GameObject("Hud Overlap Probe", typeof(RectTransform));
            try
            {
                // map-event pill: every line the pill shows, plus the full banner sentence that once ran under the clock
                Text word = Probe(root, CriticalMass.Runtime.Hud.Style.CMTokens.Display, 17, TextAnchor.MiddleLeft,
                    CriticalMass.Runtime.Hud.Mechanics.DepthHud.WordX, CriticalMass.Runtime.Hud.Mechanics.DepthHud.WordW, 38f);
                Text clock = Probe(root, CriticalMass.Runtime.Hud.Style.CMTokens.Mono, 20, TextAnchor.MiddleRight,
                    CriticalMass.Runtime.Hud.Mechanics.DepthHud.ClockX, CriticalMass.Runtime.Hud.Mechanics.DepthHud.ClockW, 38f);
                clock.text = "10:00";   // the widest clock the pill shows
                Vector2 clockInk = Ink(clock);
                Assert.Greater(clockInk.y - clockInk.x, 20f, "the font measures text in this run");
                Assert.GreaterOrEqual(clockInk.x, CriticalMass.Runtime.Hud.Mechanics.DepthHud.ClockX - 0.5f, "the clock stays in its box");
                string[] lines =
                {
                    CriticalMass.Runtime.View.Mechanics.DepthWords.EggExposedPill, CriticalMass.Runtime.View.Mechanics.DepthWords.DropIncoming,
                    CriticalMass.Runtime.View.Mechanics.DepthWords.DropLanded, CriticalMass.Runtime.View.Mechanics.DepthWords.DropContested,
                    CriticalMass.Runtime.View.Mechanics.DepthWords.BridgeWarning, CriticalMass.Runtime.View.Mechanics.DepthWords.BridgeCollapsed,
                    CriticalMass.Runtime.View.Mechanics.DepthWords.EggExposed,
                };
                foreach (string l in lines)
                {
                    HudText.SetOneLine(word, l, CriticalMass.Runtime.Hud.Mechanics.DepthHud.WordMax, CriticalMass.Runtime.Hud.Mechanics.DepthHud.WordFloor);
                    Vector2 ink = Ink(word);
                    Assert.LessOrEqual(ink.y, CriticalMass.Runtime.Hud.Mechanics.DepthHud.WordX + CriticalMass.Runtime.Hud.Mechanics.DepthHud.WordW + 0.5f, "'" + word.text + "' runs past its box");
                    ClearAtEveryShape(clockInk.x - ink.y, "pill '" + word.text + "' and its clock");
                    if (l.Length <= CriticalMass.Runtime.View.Mechanics.DepthWords.PillMaxChars) Assert.AreEqual(l, word.text, "a pill line was cut");
                }

                // Kaiju panel: the hatch timer or allegiance word keeps clear of the last rage pip
                float pipEnd = CriticalMass.Runtime.Hud.GameHud.RagePipX0 + (CriticalMass.Runtime.Hud.GameHud.RagePips - 1) * CriticalMass.Runtime.Hud.GameHud.RagePipPitch + 20f;
                Text kaiju = Probe(root, CriticalMass.Runtime.Hud.Style.CMTokens.Ui, 18, TextAnchor.MiddleRight,
                    CriticalMass.Runtime.Hud.GameHud.KaijuWordX, CriticalMass.Runtime.Hud.GameHud.KaijuWordRight - CriticalMass.Runtime.Hud.GameHud.KaijuWordX, 28f);
                foreach (string k in new[] { "HATCH 10:00", "HATCH 9:59", "HATCH 3:00", "CRIMSON", "BERSERK", "NEUTRAL", "AZURE" })
                {
                    HudText.SetOneLine(kaiju, k, CriticalMass.Runtime.Hud.GameHud.KaijuWordMax, CriticalMass.Runtime.Hud.GameHud.KaijuWordFloor);
                    Vector2 ink = Ink(kaiju);
                    Assert.AreEqual(k, kaiju.text, "the Kaiju word was cut");
                    Assert.LessOrEqual(ink.y, CriticalMass.Runtime.Hud.GameHud.KaijuWordRight + 0.5f, k);
                    ClearAtEveryShape(ink.x - pipEnd + 2f, "Kaiju word '" + k + "' and the rage pips");
                }
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
