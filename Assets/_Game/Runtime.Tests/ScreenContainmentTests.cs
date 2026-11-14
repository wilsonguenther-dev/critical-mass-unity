using System;
using System.Collections.Generic;
using System.Reflection;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Interact;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Menus;
using CriticalMass.Runtime.View.Affordance;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// hud-containment-2. Box tests inside the screens (title, Home, class and team select, loading, full map, scoreboard,
    /// pause and every settings tab, results) at the 9 screen shapes of OwnerWalkTests, with no graphics device: each canvas
    /// is laid out at the shape's canvas size (screen px / the uniform HUD scale), then every visible line's ink (its words
    /// at its fitted size, placed by its alignment, clipped by any scroll mask) must stay inside its own panel and the
    /// screen, out of the notch band, and off every other line. Also: the kill feed and toast lanes keep a cap and a
    /// queue, the prompt pill keeps to its reserved lane, the settings rows keep one set of columns, and the attack slot
    /// fits MALLET, LANTERN and PRY BAR at the HUD's shared size without a cut.
    /// </summary>
    public sealed class ScreenContainmentTests
    {
        static readonly Vector2[] Shapes =
        {
            new Vector2(3024, 1964), new Vector2(1512, 982), new Vector2(1920, 1080), new Vector2(2560, 1080), new Vector2(1280, 800),
            new Vector2(1920, 1200), new Vector2(1024, 768), new Vector2(1280, 720), new Vector2(640, 360),
        };

        const float Tol = 2f;
        const int MaxReport = 40;

        // ---------------------------------------------------------------- geometry helpers (root-local, y up)

        static Rect In(RectTransform root, RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            Vector3 a = root.InverseTransformPoint(c[0]), b = root.InverseTransformPoint(c[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        static bool Inside(Rect inner, Rect outer, float tol) =>
            inner.xMin >= outer.xMin - tol && inner.xMax <= outer.xMax + tol && inner.yMin >= outer.yMin - tol && inner.yMax <= outer.yMax + tol;

        static Rect Intersect(Rect a, Rect b)
        {
            float x0 = Mathf.Max(a.xMin, b.xMin), y0 = Mathf.Max(a.yMin, b.yMin), x1 = Mathf.Min(a.xMax, b.xMax), y1 = Mathf.Min(a.yMax, b.yMax);
            return x1 > x0 && y1 > y0 ? Rect.MinMaxRect(x0, y0, x1, y1) : new Rect(x0, y0, 0f, 0f);
        }

        /// <summary>Where a line's words actually draw: its fitted words' size inside its box, placed by its alignment.</summary>
        static Rect Ink(Text t, RectTransform root)
        {
            RectTransform rt = t.rectTransform;
            Rect box = In(root, rt);
            float lw = Mathf.Max(1e-3f, rt.rect.width), lh = Mathf.Max(1e-3f, rt.rect.height);
            float sx = box.width / lw, sy = box.height / lh;
            bool wrap = t.horizontalOverflow == HorizontalWrapMode.Wrap;
            float one = TextFit.WidthOf(t, t.text, t.fontSize);   // the rule's own measure: works on the headless runner
            float pw = wrap ? Mathf.Min(lw, one) : one;
            float tall = TextFit.HeightOf(t, t.text, t.fontSize, lw, wrap);
            float ph = t.verticalOverflow == VerticalWrapMode.Truncate ? Mathf.Min(lh, tall) : tall;
            pw *= sx;
            ph *= sy;
            int a = (int)t.alignment;
            float hx = (a % 3) * 0.5f;
            int row = a / 3;   // 0 upper, 1 middle, 2 lower
            float x = box.x + (box.width - pw) * hx;
            float y = row == 0 ? box.yMax - ph : row == 1 ? box.center.y - ph * 0.5f : box.y;
            return new Rect(x, y, pw, ph);
        }

        static bool Visible(Text t, Transform root)
        {
            if (!t.isActiveAndEnabled || string.IsNullOrEmpty(t.text) || t.color.a <= 0.01f) return false;
            for (Transform p = t.transform; p != null; p = p.parent)
            {
                CanvasGroup g = p.GetComponent<CanvasGroup>();
                if (g != null && g.alpha <= 0.01f) return false;
                Canvas c = p.GetComponent<Canvas>();   // == null, not "is": Unity's missing component is a fake null
                if (p != root && c != null && !c.enabled) return false;
                if (p == root) break;
            }
            return true;
        }

        /// <summary>The line's nearest ancestor panel (a visible shape or image), or null.</summary>
        static RectTransform BoxOf(Text t, RectTransform root)
        {
            for (Transform p = t.transform.parent; p != null && p != root; p = p.parent)
            {
                Graphic g = p.GetComponent<Graphic>();
                if (g != null && !(g is Text) && g.enabled && g.color.a > 0.01f) return (RectTransform)p;
            }
            return null;
        }

        static string Path(Transform t, Transform root)
        {
            string s = t.name;
            for (Transform p = t.parent; p != null && p != root; p = p.parent) s = p.name + "/" + s;
            return s;
        }

        // ---------------------------------------------------------------- one canvas at one shape

        static void LayOut(Canvas c, Vector2 shape)
        {
            float k = HudLayout.Scale(shape.x, shape.y);
            c.renderMode = RenderMode.WorldSpace;   // lets the test size the root; the layout below it is unchanged
            CanvasScaler cs = c.GetComponent<CanvasScaler>();
            if (cs != null) cs.enabled = false;
            var root = (RectTransform)c.transform;
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.localScale = Vector3.one;
            root.sizeDelta = new Vector2(shape.x / k, shape.y / k);
        }

        static void Check(string screen, Canvas c, Vector2 shape, List<string> bad)
        {
            var root = (RectTransform)c.transform;
            float k = HudLayout.Scale(shape.x, shape.y);
            Rect frame = root.rect;
            float notch = HudSafeArea.NotchFor(shape.x, shape.y).y / k;
            string at = screen + " @" + shape.x + "x" + shape.y + ": ";
            var lines = new List<KeyValuePair<Text, Rect>>();
            foreach (Text t in c.GetComponentsInChildren<Text>(false))
            {
                if (!Visible(t, root)) continue;
                Rect ink = Ink(t, root);
                for (Transform p = t.transform.parent; p != null && p != root; p = p.parent)
                    if (p.GetComponent<RectMask2D>() != null) ink = Intersect(ink, In(root, (RectTransform)p));
                if (ink.width < 0.5f || ink.height < 0.5f) continue;   // scrolled out of its view
                string who = Path(t.transform, root) + " '" + t.text + "'";
                if (!Inside(ink, frame, 1f)) bad.Add(at + who + " leaves the screen " + ink);
                else if (notch > 0f && ink.yMax > frame.yMax - notch + 1f) bad.Add(at + who + " sits in the notch band");
                RectTransform box = BoxOf(t, root);
                if (box != null && !Inside(ink, In(root, box), Tol)) bad.Add(at + who + " draws outside its panel " + Path(box, root));
                lines.Add(new KeyValuePair<Text, Rect>(t, ink));
            }
            for (int i = 0; i < lines.Count; i++)
                for (int j = i + 1; j < lines.Count; j++)
                {
                    Text a = lines[i].Key, b = lines[j].Key;
                    if (a.transform.IsChildOf(b.transform) || b.transform.IsChildOf(a.transform)) continue;
                    Rect x = Intersect(lines[i].Value, lines[j].Value);
                    if (x.width <= Tol || x.height <= Tol) continue;
                    // A drop shadow is the same words a pixel or two off: not a collision.
                    if (a.text == b.text && Vector2.Distance(lines[i].Value.center, lines[j].Value.center) < 4f) continue;
                    bad.Add(at + Path(a.transform, root) + " '" + a.text + "' runs into " + Path(b.transform, root) + " '" + b.text + "'");
                }
        }

        static void CheckAllShapes(string screen, GameObject host, List<string> bad)
        {
            var roots = new List<Canvas>();
            foreach (Canvas c in host.GetComponentsInChildren<Canvas>(true))
                if (c.GetComponentsInParent<Canvas>(true).Length == 1) roots.Add(c);
            Assert.IsNotEmpty(roots, screen + " built a canvas");
            foreach (Vector2 shape in Shapes)
                foreach (Canvas c in roots)
                {
                    LayOut(c, shape);
                    Check(screen, c, shape, bad);
                }
        }

        static void Build(string screen, Action<GameObject> make, List<string> bad, Action<GameObject, Action<string>> pages = null)
        {
            var go = new GameObject("Containment " + screen, typeof(RectTransform));
            try
            {
                make(go);
                if (pages == null) CheckAllShapes(screen, go, bad);
                else pages(go, page => CheckAllShapes(screen + " / " + page, go, bad));
            }
            finally { Object.DestroyImmediate(go); }
        }

        static void Report(List<string> bad, string what)
        {
            if (bad.Count == 0) return;
            int n = Mathf.Min(bad.Count, MaxReport);
            Assert.Fail(bad.Count + " " + what + " (first " + n + "):\n" + string.Join("\n", bad.GetRange(0, n)));
        }

        // ---------------------------------------------------------------- 1. the screens

        [Test]
        public void EveryScreenKeepsItsLinesInsideTheirPanelsAndOffEachOtherAtEveryShape()
        {
            var bad = new List<string>();
            try { Build("Title", go => TitleScreen.Show(go, null), bad); }
            finally
            {
                // TitleScreen.Show hands over its egg, and edit-mode DestroyImmediate skips the egg's OnDestroy: clear the
                // flag (as MotionComicTests does), or SecretsAndOptionsTests sees the title egg as active.
                typeof(CriticalMass.Runtime.Secrets.SecretsBoard).GetField("TitleEggAttached",
                    BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, false);
            }
            Build("Home", go => go.AddComponent<MainMenu>().EnsureBuilt(), bad);
            Build("Class and team select", go => go.AddComponent<ClassSelect>().Build(), bad);
            Build("Loading", go =>
            {
                var l = go.AddComponent<LoadingScreen>();
                MethodInfo build = typeof(LoadingScreen).GetMethod("Build", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                Assert.IsNotNull(build, "LoadingScreen.Build");
                build.Invoke(l, null);
            }, bad);
            Build("Full map", go =>
            {
                var c = go.AddComponent<Canvas>();
                c.renderMode = RenderMode.WorldSpace;
                new FullMapView().Build((RectTransform)go.transform, 2);
            }, bad);
            Build("Scoreboard", go => go.AddComponent<Scoreboard>().Build(), bad);
            Build("Results", go => go.AddComponent<ResultsScreen>().EnsureBuilt(), bad);
            Build("Pause", go => go.AddComponent<PauseMenu>().Build(), bad, (go, check) =>
            {
                PauseMenu m = go.GetComponent<PauseMenu>();
                m.ShowRoot();
                check("menu");
                foreach (PauseMenu.Tab tab in (PauseMenu.Tab[])Enum.GetValues(typeof(PauseMenu.Tab)))
                {
                    m.ShowSettings(tab);
                    check("settings " + tab);
                }
            });
            Report(bad, "lines out of their box or into another line");
        }

        // ---------------------------------------------------------------- 5. settings columns

        [Test]
        public void SettingsRowsShareOneSetOfColumnsInEveryTab()
        {
            Assert.LessOrEqual(PauseMenu.ColLabelX + PauseMenu.ColLabelW, PauseMenu.ColArrowLX, "label column ends before the < arrow");
            Assert.LessOrEqual(PauseMenu.ColArrowLX + PauseMenu.ColArrowW, PauseMenu.ColValueX + 0.01f, "< arrow before the value");
            Assert.LessOrEqual(PauseMenu.ColValueX + PauseMenu.ColValueW, PauseMenu.ColArrowRX + 0.01f, "value before the > arrow");
            Assert.LessOrEqual(PauseMenu.ColArrowRX + PauseMenu.ColArrowW, PauseMenu.LineW, "> arrow inside the row");
            Assert.LessOrEqual(PauseMenu.ColBarX + PauseMenu.ColBarW, PauseMenu.LineW - 20f - PauseMenu.ColNumberW, "slider bar ends before its number");

            var go = new GameObject("Settings Columns", typeof(RectTransform));
            try
            {
                PauseMenu m = go.AddComponent<PauseMenu>();
                m.Build();
                var bad = new List<string>();
                var seen = new HashSet<string>();
                foreach (PauseMenu.Tab tab in (PauseMenu.Tab[])Enum.GetValues(typeof(PauseMenu.Tab)))
                {
                    m.ShowSettings(tab);
                    foreach (MenuItem it in go.GetComponentsInChildren<MenuItem>(false))
                    {
                        if (it.ItemKind != MenuItem.Kind.Stepper && it.ItemKind != MenuItem.Kind.Slider) continue;
                        seen.Add(it.name);
                        RectTransform lr = it.Label.rectTransform;
                        if (Mathf.Abs(lr.anchoredPosition.x - lr.rect.width * 0.5f - PauseMenu.ColLabelX) > 0.5f)   // MenuUi.Place: centre pivot
                            bad.Add(tab + " " + it.name + ": label not in the label column");
                        if (lr.rect.width > PauseMenu.ColLabelW + 0.5f) bad.Add(tab + " " + it.name + ": label wider than its column");
                        if (it.ItemKind == MenuItem.Kind.Stepper && it.Value != null && Mathf.Abs(it.Value.rectTransform.rect.width - PauseMenu.ColValueW) > 0.5f)
                            bad.Add(tab + " " + it.name + ": value not in the value column");
                    }
                }
                Assert.IsEmpty(bad, string.Join("\n", bad));
                foreach (string row in new[] { "GORE", "CAMERA", "CAMERA FOLLOW", "LOOK SENSITIVITY", "FIELD OF VIEW" })
                    Assert.IsTrue(seen.Contains(row), row + " is a settings row in the columns");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheControlsTabIsReadableAndItsGridFitsThePanelWidth()
        {
            // fix/hud-containment-4: rows at the type scale, five binding columns inside the panel's content width
            CollectionAssert.Contains(UiScale.TypeSteps, PauseMenu.GridType);
            Assert.GreaterOrEqual(PauseMenu.GridType, UiScale.TypeSmall, "binding words at least the small type size");
            Assert.GreaterOrEqual(PauseMenu.GridRowH, PauseMenu.GridGlyphH + 12f, "a row holds its glyph with room");
            Assert.GreaterOrEqual(PauseMenu.GridRowH, 40f, "rows readable on the smallest window");
            Assert.LessOrEqual(PauseMenu.GridColX(4) + PauseMenu.GridCellW, PauseMenu.PanelX + PauseMenu.PanelW - 40f + 0.01f, "the last column inside the panel");
            var go = new GameObject("Controls Tab", typeof(RectTransform));
            try
            {
                PauseMenu m = go.AddComponent<PauseMenu>();
                m.Build();
                m.ShowSettings(PauseMenu.Tab.Controls);
                bool scrolls = false;
                foreach (ScrollRect sr in go.GetComponentsInChildren<ScrollRect>(false)) scrolls = true;
                float bottom = 0f;
                foreach (MenuItem it in go.GetComponentsInChildren<MenuItem>(true))
                    if (it.name.StartsWith("Cell ")) bottom = Mathf.Max(bottom, -it.GetComponent<RectTransform>().anchoredPosition.y + PauseMenu.GridRowH * 0.5f);
                if (bottom > PauseMenu.PanelY + PauseMenu.PanelH - 78f) Assert.IsTrue(scrolls, "a grid taller than the panel scrolls inside it");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void AWorldLabelOverARegisteredPanelIsCaught()
        {
            // fix/hud-containment-4: name tags and call bubbles test their screen rect against the HUD's panels
            var go = new GameObject("Panel Mask", typeof(RectTransform));
            try
            {
                Canvas c = go.AddComponent<Canvas>();
                c.renderMode = RenderMode.ScreenSpaceOverlay;
                var panel = MenuUi.Rt((RectTransform)go.transform, "Panel");
                panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.zero;
                panel.anchoredPosition = Vector2.zero;
                panel.sizeDelta = new Vector2(400f, 200f);
                HudZones.RegisterPanel(panel);
                var tag = MenuUi.Rt((RectTransform)go.transform, "Tag");
                tag.anchorMin = tag.anchorMax = tag.pivot = Vector2.zero;
                tag.sizeDelta = new Vector2(120f, 26f);
                tag.anchoredPosition = new Vector2(300f, 150f);
                Assert.IsTrue(HudZones.OverPanel(tag, c), "a tag on the panel is caught");
                tag.anchoredPosition = new Vector2(700f, 600f);
                Assert.IsFalse(HudZones.OverPanel(tag, c), "a tag in the open shows");
                tag.anchoredPosition = new Vector2(300f, 150f);
                panel.gameObject.SetActive(false);
                Assert.IsFalse(HudZones.OverPanel(tag, c), "a hidden panel masks nothing");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheStartBannerStaysOutOfStagedShotsAndCoversTheActionAtMostFiveSeconds()
        {
            Assert.IsTrue(FirstMinutes.StagesCloseUps(new[] { "game", "-groundShots" }));
            Assert.IsTrue(FirstMinutes.StagesCloseUps(new[] { "-followshots" }));
            Assert.IsTrue(FirstMinutes.StagesCloseUps(new[] { "-skillShots" }));
            Assert.IsFalse(FirstMinutes.StagesCloseUps(new[] { "-flowShots" }), "the flow walk shows the real HUD");
            Assert.IsFalse(FirstMinutes.StagesCloseUps(null));
            Assert.LessOrEqual(FirstMinutes.StartBannerOnScreenSeconds, 5.0 + 1e-9, "YOU ARE AZURE covers the action 5 s at most");
        }

        // ---------------------------------------------------------------- 3. kill feed and toast lanes

        [Test]
        public void TheKillFeedShowsFiveAndQueuesTheRestWithACap()
        {
            var go = new GameObject("Kill Feed Lane");
            try
            {
                KillFeed f = go.AddComponent<KillFeed>();
                var e = new CombatEvent { Kind = CombatEventKind.Knockout, Actor = 1, Other = 2 };
                for (int i = 0; i < KillFeed.Lines; i++) f.Push(e, -1, default);
                Assert.AreEqual(0, f.Queued, "five rows take five lines");
                for (int i = 0; i < 3; i++) f.Push(e, -1, default);
                Assert.AreEqual(3, f.Queued, "a burst waits while every row is fresh");
                for (int i = 0; i < 20; i++) f.Push(e, -1, default);
                Assert.AreEqual(KillFeed.QueueCap, f.Queued, "the queue keeps its cap (the oldest waiting line drops)");
                f.Tick(KillFeed.MinShowSeconds + 0.01f);
                f.Tick(0f);
                Assert.AreEqual(KillFeed.QueueCap - 1, f.Queued, "a row read long enough makes room for one waiting line");
                f.Clear();
                Assert.AreEqual(0, f.Queued);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheToastLaneShowsOneNoticeWaitsBehindAnAchievementAndKeepsItsCap()
        {
            ToastLane.Clear();
            HudZones.Clear();
            try
            {
                HudZones.FrameOverride = 100;
                HudZones.Hold(HudZone.Toast, HudPriority.Banner);   // an achievement toast is up
                ToastLane.Post("VIEW: THIRD PERSON", 0f);
                Assert.IsNull(ToastLane.Current(0f), "the notice waits behind the achievement toast");
                HudZones.FrameOverride = 105;
                Assert.AreEqual("VIEW: THIRD PERSON", ToastLane.Current(0f));
                ToastLane.Post("VIEW: THIRD PERSON", 0.5f);
                Assert.AreEqual(0, ToastLane.Queued, "the same words refresh the shown notice");
                foreach (string s in new[] { "A", "B", "C", "D" }) ToastLane.Post(s, 0.6f);
                Assert.AreEqual(ToastLane.Cap, ToastLane.Queued, "the queue keeps its cap");
                Assert.AreEqual("VIEW: THIRD PERSON", ToastLane.Current(1.9f), "one notice at a time");
                Assert.AreEqual("B", ToastLane.Current(2.6f), "the oldest waiting notice dropped when full");
                for (int i = 0; i < 9; i++)
                {
                    Vector2 s = Shapes[i];
                    Rect r = ToastLane.PixelRect(s.x, s.y, 40f);
                    Rect lane = HudZones.PixelRect(HudZone.Toast, s.x, s.y);
                    Assert.IsTrue(Inside(r, lane, 0.5f), s + ": the notice stays in the toast lane");
                }
            }
            finally
            {
                HudZones.FrameOverride = -1;
                HudZones.Clear();
                ToastLane.Clear();
            }
        }

        // ---------------------------------------------------------------- 4. the prompt pill's lane

        static Rect Flip(Rect r, float h) => new Rect(r.x, h - r.yMax, r.width, r.height);

        [Test]
        public void ThePromptPillKeepsToItsReservedLaneAndTheLaneTouchesNoPanel()
        {
            Assert.LessOrEqual(2f + TerrainAffordanceView.LineH + TerrainAffordanceView.DetailH, HudZones.PromptLaneH + 0.01f, "the tallest pill fits the lane");
            Assert.LessOrEqual(HudZones.PromptLane.yMax, HudZones.Caption(HudZones.CaptionLines - 1).y + 0.01f, "lane above the captions");
            Assert.GreaterOrEqual(HudZones.PromptLane.y, HudZones.Lower.yMax, "lane under the lower lane");
            var panels = new[] { HudZone.Lower, HudZone.PlayerCard, HudZone.Minimap, HudZone.Toast, HudZone.Feed, HudZone.Stinger, HudZone.TopBand, HudZone.Callout, HudZone.Guide };
            foreach (Vector2 s in Shapes)
            {
                float k = HudLayout.Scale(s.x, s.y);
                Rect lane = HudZones.PromptLanePixels(s.x, s.y);
                foreach (HudZone z in panels)
                {
                    Rect p = HudZones.PixelRect(z, s.x, s.y);
                    Rect x = Intersect(lane, p);
                    Assert.IsFalse(x.width > 0.5f && x.height > 0.5f, s + ": the pill lane runs into " + z + " " + p + " vs " + lane);
                }
                // The captions are bottom-centre anchored like the lane.
                Rect cap = HudZones.Caption(HudZones.CaptionLines - 1);
                Rect capPx = new Rect(0.5f * s.x + (cap.x - HudZones.RefW * 0.5f) * k, s.y + (cap.y - HudZones.RefH) * k, cap.width * k, cap.height * k);
                Assert.LessOrEqual(lane.yMax, capPx.y + 0.5f, s + ": the lane stays above the captions");

                foreach (float w in new[] { TerrainAffordanceView.MinPillW, 500f, 5000f })
                    foreach (float wantX in new[] { -1000f, s.x / k * 0.5f, 1e5f })
                    {
                        var size = new Vector2(Mathf.Min(w, TerrainAffordanceView.LaneWidth(s, k)), 2f + TerrainAffordanceView.LineH + TerrainAffordanceView.DetailH);
                        Vector2 p = TerrainAffordanceView.LanePill(wantX, size, s, k);   // canvas units, centre x, bottom y, y up
                        Rect pill = Flip(new Rect((p.x - size.x * 0.5f) * k, p.y * k, size.x * k, size.y * k), s.y);
                        Assert.IsTrue(Inside(pill, lane, 0.5f), s + " w" + w + " x" + wantX + ": the pill " + pill + " leaves its lane " + lane);
                    }
            }
        }

        // ---------------------------------------------------------------- fix/hud-containment-3: ping markers off the corners

        [Test]
        public void APingMarkerNeverSitsOnThePlayerCardTheKitOrTheMinimap()
        {
            foreach (Vector2 s in Shapes)
            {
                float k = HudLayout.Scale(s.x, s.y);
                var r = new Rect(-s.x / k * 0.5f, -s.y / k * 0.5f, s.x / k, s.y / k);   // a centred root in reference units
                Rect left = new Rect(r.xMin, r.yMin, Mathf.Max(HudZones.PlayerCard.xMax, HudZones.Kit.xMax), HudLayout.RefH - HudZones.Kit.y);
                Rect right = new Rect(r.xMax - (HudLayout.RefW - HudZones.Minimap.x), r.yMin, HudLayout.RefW - HudZones.Minimap.x, HudLayout.RefH - HudZones.Minimap.y);
                foreach (Vector2 p in new[] { new Vector2(left.x + 40f, left.y + 40f), new Vector2(left.xMax - 2f, left.yMax - 2f), new Vector2(right.x + 2f, right.y + 2f), new Vector2(right.xMax - 30f, right.center.y) })
                {
                    Vector2 q = CriticalMass.Runtime.Hud.Interact.PingMarkers.OffPanels(p, r);
                    Assert.IsFalse(left.Contains(q) || right.Contains(q), s + ": a marker at " + p + " stays on a corner panel at " + q);
                }
                Vector2 mid = new Vector2(0f, 0f);
                Assert.AreEqual(mid, CriticalMass.Runtime.Hud.Interact.PingMarkers.OffPanels(mid, r), "a marker in the open stays put");
            }
        }

        // ---------------------------------------------------------------- 6. the light weapon names in the attack slot

        [Test]
        public void TheAttackSlotFitsEveryLightWeaponNameWithoutACut()
        {
            string[] weapons = { CriticalMass.Sim.Core.LightWeapons.BuilderName, CriticalMass.Sim.Core.LightWeapons.HealerName, CriticalMass.Sim.Core.LightWeapons.DemolitionsName };
            string[][] slots = GameHud.AbilitySlotNames();
            var root = new GameObject("Slot Names", typeof(RectTransform));
            try
            {
                var labels = new Text[3];
                for (int i = 0; i < 3; i++)
                    labels[i] = HudText.Make(root.transform, "Name " + i, CMTokens.Ui, HudLayout.AbilityNameMax, Color.white, TextAnchor.MiddleCenter, 2f);
                var widths = new float[3];
                var lefts = new float[3];
                var boxes = new float[3];
                var bad = new List<string>();
                foreach (string weapon in weapons)
                    foreach (string ability in slots[1])
                        foreach (string carry in slots[2])
                        {
                            var names = new[] { weapon, ability, carry };
                            int size = GameHud.LayoutAbilityNameTexts(labels, names, widths, lefts, boxes);
                            string row = weapon + " | " + ability + " | " + carry + " @" + size;
                            if (size < HudLayout.AbilityNameFloor) bad.Add(row + ": under the floor");
                            if (labels[0].text != weapon) bad.Add(row + ": the weapon name is cut to '" + labels[0].text + "'");
                            for (int i = 1; i < 3; i++)
                                if (lefts[i] < lefts[i - 1] + boxes[i - 1] + HudLayout.AbilityNameGap - 0.5f) bad.Add(row + ": slot " + i + " runs into slot " + (i - 1));
                        }
                Assert.IsEmpty(bad, string.Join("\n", bad));
            }
            finally { Object.DestroyImmediate(root); }
        }

        // ---------------------------------------------------------------- fix/hud-containment-5 (owner screenshots)

        [Test]
        public void AWorldBarSlidesOffTheHudAndAFadedLineMasksNothing()
        {
            // (2) the Kaiju bar and the hill marker slide down off the top band and its stack; (1) a faded-out call line
            // does not push tags away
            var go = new GameObject("Slide Off", typeof(RectTransform));
            try
            {
                Canvas c = go.AddComponent<Canvas>();
                c.renderMode = RenderMode.ScreenSpaceOverlay;
                var panel = MenuUi.Rt((RectTransform)go.transform, "Band");
                panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.zero;
                panel.anchoredPosition = new Vector2(0f, 400f);
                panel.sizeDelta = new Vector2(600f, 128f);
                HudZones.RegisterPanel(panel);
                var bar = MenuUi.Rt((RectTransform)go.transform, "Kaiju Bar");
                bar.anchorMin = bar.anchorMax = bar.pivot = Vector2.zero;
                bar.sizeDelta = new Vector2(300f, 56f);
                bar.anchoredPosition = new Vector2(100f, 420f);
                Assert.IsTrue(HudZones.OverPanel(bar, c), "the bar starts on the band");
                Assert.IsTrue(HudZones.SlideOffPanels(bar, c), "it moves");
                Assert.IsFalse(HudZones.OverPanel(bar, c), "and ends clear of it");
                Assert.LessOrEqual(bar.anchoredPosition.y + 56f, 400.01f, "below the band, not on it");

                var line = MenuUi.Rt((RectTransform)go.transform, "Ping Line");
                line.anchorMin = line.anchorMax = line.pivot = Vector2.zero;
                line.anchoredPosition = new Vector2(0f, 0f);
                line.sizeDelta = new Vector2(520f, 30f);
                CanvasGroup g = line.gameObject.AddComponent<CanvasGroup>();
                HudZones.RegisterPanel(line);
                var tag = MenuUi.Rt((RectTransform)go.transform, "Tag");
                tag.anchorMin = tag.anchorMax = tag.pivot = Vector2.zero;
                tag.sizeDelta = new Vector2(80f, 22f);
                tag.anchoredPosition = new Vector2(100f, 4f);
                Assert.IsTrue(HudZones.OverPanel(tag, c), "a tag through a shown call line is caught (no \"FerGALElp!\")");
                g.alpha = 0f;
                Assert.IsFalse(HudZones.OverPanel(tag, c), "a faded-out line masks nothing");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheTopCentreStackHasOneOrderAndOneGap()
        {
            // (6) band, job line, stock chips, map-event bar, top down, StackGap apart, all above the guide column
            float bandBottom = HudLayout.MatchSize.y;
            Assert.AreEqual(HudZones.StackGap, HudZones.JobY - bandBottom, 0.01f, "job line under the band");
            Assert.AreEqual(HudZones.StackGap, HudZones.StockY - (HudZones.JobY + HudZones.JobH), 0.01f, "stock under the job line");
            Assert.AreEqual(HudZones.StackGap, HudZones.EventY - (HudZones.StockY + HudZones.StockH), 0.01f, "event bar under the stock");
            Assert.LessOrEqual(HudZones.EventY + HudZones.EventH, HudZones.Callout.yMax + 0.01f, "inside the centre lane");
            Assert.Less(HudZones.EventY + HudZones.EventH, HudZones.GuideUpperY, "above the guide column");
            Assert.Greater(HudZones.StackIn, 0f);
            Assert.Greater(HudZones.StackOut, 0f);
        }

        [Test]
        public void TeamCallsHaveOneLaneOfTwoThatFades()
        {
            // (3) a cap of two lines, a few seconds each, inside the reserved feed zone
            Assert.AreEqual(2, PingFeed.Lines);
            Assert.LessOrEqual(PingFeed.LineLife, 5f, "fades after a few seconds");
            Assert.Greater(PingFeed.FadeIn, 0f, "enters, not pops");
            Assert.LessOrEqual(PingFeed.Lines * HudZones.PingLineH, HudZones.PingFeed.height + 0.01f, "inside its zone");
            Assert.IsFalse(PingFeed.Showing(0), "a line that is not a call never hides a marker word");
        }

        [Test]
        public void KeyPlatesNeverFlickerBetweenSchemes()
        {
            // (5) a scheme change redraws once it has settled, and at most once per SchemeHoldSeconds
            Assert.IsFalse(HudPrompts.SchemeRedrawDue(10.1f, 10f, 0f), "not before it settles");
            Assert.IsTrue(HudPrompts.SchemeRedrawDue(10f + HudPrompts.SchemeSettleSeconds + 0.01f, 10f, 0f), "once it has settled");
            Assert.IsFalse(HudPrompts.SchemeRedrawDue(11f, 10f, 10f), "not twice inside the hold");
            Assert.IsTrue(HudPrompts.SchemeRedrawDue(12.5f, 10f, 10f), "after the hold");
        }

        [Test]
        public void LaneNoticesFadeInAndOut()
        {
            ToastLane.Clear();
            try
            {
                ToastLane.Post("VIEW: THIRD PERSON", 0f);
                Assert.IsNotNull(ToastLane.Current(0f));
                Assert.AreEqual(0f, ToastLane.Alpha(0f), 0.01f, "enters from clear");
                Assert.AreEqual(1f, ToastLane.Alpha(1f), 0.01f, "held");
                Assert.Less(ToastLane.Alpha(ToastLane.Seconds - 0.05f), 0.5f, "leaving");
                Assert.AreEqual(0f, ToastLane.Alpha(ToastLane.Seconds + 0.1f), 0.01f, "gone");
            }
            finally { ToastLane.Clear(); }
        }

        [Test]
        public void EveryPanelPillAndChipTakesTheFinishTokens()
        {
            // (7) one radius per kind, one stroke, one rim, one shadow on the backing plate only
            Assert.AreEqual(HudStyle.PanelRadius, HudStyle.RadiusFor(330f, 120f, 18f));
            Assert.AreEqual(HudStyle.ChipRadius, HudStyle.RadiusFor(150f, 40f, 12f));
            Assert.AreEqual(-1f, HudStyle.RadiusFor(760f, 40f, 14f), "a wide line is a pill");
            Assert.AreEqual(-1f, HudStyle.RadiusFor(60f, 30f, -1f), "a built pill stays a pill");
            var go = new GameObject("Finish", typeof(RectTransform));
            try
            {
                VectorShape plate = MenuUi.Panel(go.transform, "Panel", 0f, 0f, 330f, 120f);
                VectorShape row = MenuUi.Shape(go.transform, "Row", ShapeKind.RoundRect, 12f, 40f, 300f, 44f, Color.white, 12f);
                row.OutlinePx = 4f;
                row.RimPx = 0f;
                VectorShape meter = MenuUi.Shape(go.transform, "Meter", ShapeKind.RoundRect, 12f, 90f, 300f, 22f, Color.white, 6f);
                meter.EmptyColor = Color.black;
                HudStyle.Sweep(go.transform);
                Assert.AreEqual(HudStyle.PanelRadius, plate.Radius);
                Assert.AreEqual(HudStyle.OutlinePx, plate.OutlinePx);
                Assert.AreEqual(HudStyle.RimPx, plate.RimPx);
                Assert.AreEqual(HudStyle.ShadowY, plate.ShadowPx, "the plate carries the shadow");
                Assert.AreEqual(HudStyle.ChipRadius, row.Radius, 0.01f);
                Assert.AreEqual(HudStyle.OutlinePx, row.OutlinePx);
                Assert.AreEqual(0f, row.RimPx, "no rim stays no rim");
                Assert.AreEqual(0f, row.ShadowPx, "rows inside it carry none");
                Assert.AreEqual(6f, meter.Radius, "a meter keeps its own shape");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void KitRowsShowTheSkillIcon()
        {
            // (4) the Healer's two rows (HEALING WINGS, PURIFY) name a painted icon, never a blank square
            int healer = Array.IndexOf(UiIcons.Classes, "healer");
            Assert.IsNotNull(CriticalMass.Runtime.Hud.Mechanics.MechanicsHud.RowIcon("Ability", healer, 0));
            Assert.IsNotNull(CriticalMass.Runtime.Hud.Mechanics.MechanicsHud.RowIcon("DigPlace", healer, 1), "PURIFY");
        }
    }
}
