using System.Collections.Generic;
using CriticalMass.Runtime.Codex;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menus;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b12-x1: the "Kaiju ... to th" strip behind the results' middle card. (1) The strip was the match's CaptionBar
    /// (order 28, under the results canvas at 30) still showing a late caption; the results close it when they show and while
    /// they are up. (2) Every active Text of the summary page, filled with the longest lines the game writes, is drawn inside
    /// its own card at 1280x720 and 1920x1080. Every case prints what it measured.
    /// </summary>
    public sealed class ResultsStrayTextTests
    {
        const string EggLine = "The Kaiju chose no one, and sat down on the hill to think about it.";

        static readonly (string name, string text)[] Longest =
        {
            ("Kaiju Caption", EggLine),
            ("Kaiju Reason", "Instability reached 100 of 100 before the hatch, and corruption fed the egg to the very end."),
            ("Kaiju KOs", "Knocked out 12 times: Azure 6, Crimson 6."),
            ("Kaiju Kills", "Players it knocked out: Azure 16, Crimson 16."),
            ("You Name", "DEMOLITIONS\nCRIMSON"),
            ("MVP Name 0", "DEMOLITIONS 16"),
            ("MVP Name 1", "DEMOLITIONS 16"),
            ("MVP Line 0", "24 KO  ·  999 s HILL  ·  9999 BIOMASS"),
            ("MVP Line 1", "24 KO  ·  999 s HILL  ·  9999 BIOMASS"),
            ("MVP Points 0", "99999 PTS"),
            ("MVP Points 1", "99999 PTS"),
        };

        /// <summary>A RectTransform's rect in 1920x1080 reference px (y down from the top-left) via its world corners.</summary>
        static Rect RefRect(RectTransform space, RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            Vector3 a = space.InverseTransformPoint(c[0]), b = space.InverseTransformPoint(c[2]);
            float x0 = Mathf.Min(a.x, b.x) + HudLayout.RefW * 0.5f, x1 = Mathf.Max(a.x, b.x) + HudLayout.RefW * 0.5f;
            float y0 = HudLayout.RefH * 0.5f - Mathf.Max(a.y, b.y), y1 = HudLayout.RefH * 0.5f - Mathf.Min(a.y, b.y);
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }

        /// <summary>The rect the text really draws in: its box, widened (by alignment) when it overflows on one line.</summary>
        static Rect Drawn(Rect box, Text t)
        {
            float pw = t.horizontalOverflow == HorizontalWrapMode.Overflow ? t.preferredWidth : box.width;
            float ph = t.verticalOverflow == VerticalWrapMode.Overflow ? t.preferredHeight : box.height;
            float w = Mathf.Max(box.width, pw), h = Mathf.Max(box.height, ph);
            int col = (int)t.alignment % 3, row = (int)t.alignment / 3;   // 0 left/upper, 1 centre/middle, 2 right/lower
            float x = col == 0 ? box.x : col == 1 ? box.center.x - w * 0.5f : box.xMax - w;
            float y = row == 0 ? box.y : row == 1 ? box.center.y - h * 0.5f : box.yMax - h;
            return new Rect(x, y, w, h);
        }

        [TestCase(1280, 720)]
        [TestCase(1920, 1080)]
        public void EverySummaryTextStaysInsideItsOwnCard(int w, int h)
        {
            var go = new GameObject("Results Under Test");
            try
            {
                ResultsScreen r = go.AddComponent<ResultsScreen>();
                r.EnsureBuilt();
                r.Canvas.scaleFactor = HudLayout.Scale(w, h);
                r.ShowTab(ResultsTab.Summary);
                Transform root = r.Canvas.transform;
                var space = (RectTransform)root.Find("Rise/Space");
                Assert.IsNotNull(space, "the results space");
                foreach (var (name, text) in Longest)
                {
                    Text t = FindText(root, name);
                    Assert.IsNotNull(t, "the summary has its label " + name);
                    HudText.Set(t, text);
                }
                Canvas.ForceUpdateCanvases();
                RectTransform page = r.Page(ResultsTab.Summary);
                var cards = new List<(string, Rect)>
                {
                    ("kaiju card", ResultsLayout.Card(0)), ("you card", ResultsLayout.Card(1)), ("mvp card", ResultsLayout.Card(2)),
                };
                int checkedTexts = 0, outside = 0;
                foreach (Text t in page.GetComponentsInChildren<Text>(false))
                {
                    if (!t.isActiveAndEnabled || string.IsNullOrEmpty(t.text)) continue;
                    Rect box = RefRect(space, t.rectTransform), drawn = Drawn(box, t);
                    string own = null;
                    Rect card = default;
                    foreach (var (cn, cr) in cards) if (cr.Contains(box.center)) { own = cn; card = cr; }
                    checkedTexts++;
                    const float slack = 2f;
                    bool inside = own != null && drawn.xMin >= card.xMin - slack && drawn.xMax <= card.xMax + slack &&
                                  drawn.yMin >= card.yMin - slack && drawn.yMax <= card.yMax + slack;
                    Rect scr = ResultsLayout.OnScreen(drawn, w, h);
                    Debug.Log($"[b12-x1] {w}x{h} '{t.name}' \"{Short(t.text)}\" size {t.fontSize} drawn {Fmt(drawn)} (screen {Fmt(scr)}) " +
                              $"card {own ?? "none"} {(own != null ? Fmt(card) : "")} -> {(inside ? "inside" : "OUTSIDE")}");
                    if (!inside) outside++;
                }
                Debug.Log($"[b12-x1] {w}x{h} summary texts checked {checkedTexts}, outside their card {outside} (limit 0)");
                Assert.Greater(checkedTexts, 10, "the check saw the summary's labels (not vacuous)");
                Assert.AreEqual(0, outside, $"{w}x{h}: every summary text inside its own card (see the [b12-x1] lines)");

                // The pages not on show draw nothing.
                for (int i = 0; i < ResultsLayout.TabCount; i++)
                {
                    if (i == (int)ResultsTab.Summary) continue;
                    int active = 0;
                    foreach (Text t in r.Page((ResultsTab)i).GetComponentsInChildren<Text>(true)) if (t.isActiveAndEnabled) active++;
                    Debug.Log($"[b12-x1] {w}x{h} hidden page {(ResultsTab)i}: active texts {active} (limit 0)");
                    Assert.AreEqual(0, active, "a page not on show draws no text");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ALateCaptionNeverStaysBehindTheResults()
        {
            var go = new GameObject("Results Under Test");
            try
            {
                CaptionBar.Show(EggLine, 6f);
                bool before = CaptionBar.IsShowing;
                string text = CaptionBar.Current;
                ResultsScreen r = go.AddComponent<ResultsScreen>();
                r.EnsureBuilt();
                Canvas bar = FindCaptionCanvas();
                int barOrder = bar != null ? bar.sortingOrder : -1;
                Debug.Log($"[b12-x1] caption before the results: showing={before} text=\"{text}\" order {barOrder}, results order {r.Canvas.sortingOrder}");
                Assert.IsTrue(before, "the caption is up (the check is not vacuous)");
                Assert.Less(barOrder, r.Canvas.sortingOrder, "the caption draws under the results: this is how it became a strip behind the cards");
                r.Show();
                bool after = CaptionBar.IsShowing;
                Debug.Log($"[b12-x1] caption after the results showed: showing={after} (limit false)");
                Assert.IsFalse(after, "the results close a caption left up from the match");

                CaptionBar.Show(EggLine, 6f);
                bool closed = ResultsScreen.HideStrayOverlays();
                Debug.Log($"[b12-x1] caption fired while the results are up: closed={closed}, showing={CaptionBar.IsShowing} (limit false)");
                Assert.IsTrue(closed);
                Assert.IsFalse(CaptionBar.IsShowing, "a caption fired after the results opened is closed on their next frame");
                Assert.IsFalse(ResultsScreen.HideStrayOverlays(), "nothing to close: no-op");
            }
            finally
            {
                Object.DestroyImmediate(go);
                GameObject cb = GameObject.Find("Caption Bar");
                if (cb != null) Object.DestroyImmediate(cb);
            }
        }

        static Canvas FindCaptionCanvas()
        {
            GameObject cb = GameObject.Find("Caption Bar");
            return cb != null ? cb.GetComponentInChildren<Canvas>(true) : null;
        }

        static Text FindText(Transform root, string name)
        {
            foreach (Text t in root.GetComponentsInChildren<Text>(true)) if (t.name == name) return t;
            return null;
        }

        static string Short(string s) => s.Length > 40 ? s.Substring(0, 40).Replace("\n", " / ") + "..." : s.Replace("\n", " / ");
        static string Fmt(Rect r) => $"[{r.xMin:0},{r.yMin:0} .. {r.xMax:0},{r.yMax:0}]";
    }
}
