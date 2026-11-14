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
            float pw = t.horizontalOverflow == HorizontalWrapMode.Wrap ? Mathf.Min(lw, t.preferredWidth) : t.preferredWidth;
            float ph = t.verticalOverflow == VerticalWrapMode.Truncate ? Mathf.Min(lh, t.preferredHeight) : t.preferredHeight;
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
                if (p != root && p.GetComponent<Canvas>() is Canvas c && !c.enabled) return false;
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
            Build("Title", go => TitleScreen.Show(go, null), bad);
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
    }
}
