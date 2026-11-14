using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Menus;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// hud-containment-1 (owner, 2026-11-14: "text is all over, display boxes are breaking, text is not properly
    /// contained"). Layout-rect tests, no graphics device: the one text rule (TextFit) keeps every registered label inside
    /// its box with its longest words padded 30% for future languages; the left stack, the bottom lane and the prompt pill
    /// keep apart and inside the safe area at every supported screen shape (the piece table is checked by
    /// OwnerWalkTests.NoHudPiecesCollideAtAnyScreenShapeWithEveryOptionalPieceShown, which now walks 9 shapes); a settings
    /// tab taller than its panel scrolls inside it.
    /// </summary>
    public sealed class HudContainmentTests
    {
        /// <summary>16:9, 16:10, 21:9, 4:3, 1280x720 and the smallest window (640x360).</summary>
        static readonly Vector2[] Shapes =
        {
            new Vector2(1920, 1080), new Vector2(1920, 1200), new Vector2(2560, 1080), new Vector2(1024, 768), new Vector2(1280, 720), new Vector2(640, 360),
        };

        /// <summary>A registered label: where it lives, its font and size range, its box and its longest words.</summary>
        struct Spec
        {
            public string Where;
            public Typeface Face;
            public int Max, Min, Lines;
            public float W, H;
            public string Longest;
            public Spec(string where, Typeface face, int max, int min, float w, float h, string longest, int lines = 0)
            { Where = where; Face = face; Max = max; Min = min; W = w; H = h; Longest = longest; Lines = lines; }
        }

        static IEnumerable<Spec> Registered()
        {
            Typeface ui = CMTokens.Ui, disp = CMTokens.Display, mono = CMTokens.Mono;
            Rect plate = HudZones.AutomationPlate;
            // match HUD: left stack
            yield return new Spec("kit title", disp, UiScale.TypeSmall, 13, HudZones.KitW - 36f, 28f, "TEAM BUILD BANK");
            yield return new Spec("kit title (class)", disp, UiScale.TypeSmall, 13, HudZones.KitW - 36f, 28f, "DEMOLITIONS");
            yield return new Spec("kit skill name", ui, UiScale.TypeSmall, 12, 154f, 26f, "HEALING WINGS");
            yield return new Spec("kit skill note", ui, UiScale.TypeMin, 11, 154f, 20f, "GO TO THE EGG");
            yield return new Spec("kit key chip", ui, 14, 9, 68f, 30f, "RIGHT CLICK", 2);
            yield return new Spec("kit bank line", ui, UiScale.TypeMin, 11, HudZones.KitW - 36f, 28f, "TEAM WOOD 199  LADDER 1, BRIDGE 2");
            yield return new Spec("ping line", ui, UiScale.TypeSmall, 12, 488f, HudZones.PingLineH, "Demolitions: Too many pings: wait 6 s");
            // automated-run plate
            yield return new Spec("automated-run plate", ui, UiScale.TypeSmall, 13, plate.width - 24f, plate.height - 12f,
                "AUTOMATED TEST RUN: THE COMPUTER IS PLAYING THIS WINDOW. YOUR KEYS DO NOTHING HERE. IT CLOSES BY ITSELF.");
            // bottom lane
            yield return new Spec("announcer subtitle (widest box)", ui, UiScale.TypeButton, UiScale.TypeMin, HudZones.BottomBandW - 32f, HudZones.SubtitleH,
                "Banners up. The hill is open. Azure takes the lead, and Crimson has the Kaiju on their side.");
            yield return new Spec("sound caption", ui, 20, 14, HudZones.CaptionW - 24f, HudZones.CaptionH, "Supply drop landed on the far bridge >");
            yield return new Spec("interact prompt words", ui, 20, 12, 370f, 50f, "HOLD RIGHT CLICK  REPAIR THE FALLEN BRIDGE", 2);
            yield return new Spec("prompt pill main line (widest pill)", disp, UiScale.TypeBody, 14, 720f - 28f, CriticalMass.Runtime.View.Affordance.TerrainAffordanceView.LineH, "HOLD RIGHT CLICK  BUILD BRIDGE (12)");
            yield return new Spec("prompt pill detail", ui, UiScale.TypeMin, 12, 720f - 28f, CriticalMass.Runtime.View.Affordance.TerrainAffordanceView.DetailH, "bank empty  ·  too far 2.0 s");
            yield return new Spec("stock word", ui, 13, 9, 40f, 38f, "WATER");
            yield return new Spec("power-up counter", ui, 13, 9, 250f, 26f, "Counter: a Warrior's shield stops it");
            // menus (settings rows, tabs, class cards)
            // settings rows (hud-containment-2): the label column and the value column, the longest words of every tab
            float lw = PauseMenu.ColLabelW, vw = PauseMenu.ColValueW, lh = PauseMenu.LineH;
            foreach (string s in new[] { "SAVE POWER IN BACKGROUND", "MUTE WHEN IN BACKGROUND", "PLAY INTRO AT LAUNCH", "COLOUR-BLIND TEAMS",
                         "LOOK SENSITIVITY", "FIELD OF VIEW", "CAMERA FOLLOW", "GORE", "CAMERA" })
                yield return new Spec("settings row label", disp, UiScale.TypeButton, UiScale.TypeMin, lw, lh, s);
            foreach (string s in new[] { "MANUAL, MOUSE OR STICK", "THIRD PERSON, AUTO FOLLOW", "AUTO, BEHIND YOU", "MANUAL, INVERT Y", "BLUE / AMBER",
                         "FIRST PERSON", "THIRD PERSON", "120 DEGREES", "3.00x" })
                yield return new Spec("settings row value", ui, UiScale.TypeButton, UiScale.TypeMin, vw, lh, s);
            foreach (CriticalMass.Runtime.View.Effects.GoreLevel g in (CriticalMass.Runtime.View.Effects.GoreLevel[])System.Enum.GetValues(typeof(CriticalMass.Runtime.View.Effects.GoreLevel)))
                yield return new Spec("settings row value (gore)", ui, UiScale.TypeButton, UiScale.TypeMin, vw, lh, CriticalMass.Runtime.View.Effects.GoreGate.Label(g));

            // hud-containment-2: the remaining label groups, every string written out or read from its table
            // kill feed: a name each side of the attack icon (132 and 136 px)
            foreach (string s in KillWords.ClassUpper)
                yield return new Spec("kill feed name", ui, UiScale.TypeBody, ContainedText.FloorFor(UiScale.TypeBody), 132f, KillFeed.LineH, s);
            yield return new Spec("kill feed name", ui, UiScale.TypeBody, ContainedText.FloorFor(UiScale.TypeBody), 132f, KillFeed.LineH, KillWords.Kaiju);
            // achievement toast: the title and the rule line, every achievement
            foreach (AchievementDef a in Achievements.All)
            {
                yield return new Spec("achievement title", disp, UiScale.TypeButton, ContainedText.FloorFor(UiScale.TypeButton), 480f, 36f, a.Title.ToUpperInvariant());
                yield return new Spec("achievement rule", ui, UiScale.TypeSmall, ContainedText.FloorFor(UiScale.TypeSmall), 480f, 24f, a.Description);
            }
            // loading screen: the step line for every step
            foreach (LoadStep st in (LoadStep[])System.Enum.GetValues(typeof(LoadStep)))
                yield return new Spec("loading step line", ui, UiScale.TypeBody, ContainedText.FloorFor(UiScale.TypeBody), 900f, 34f, LoadingScreen.StepLine(st, 59.9f));
            // camera notices (the toast lane, one line)
            foreach (string s in new[] { "VIEW: THIRD PERSON", "VIEW: FIRST PERSON", "VIEW: TOP-DOWN", "AUTO-DIRECTOR OFF", "AUTO-DIRECTOR ON", "GROUND VIEW", "HIGH VIEW" })
                yield return new Spec("camera notice", ui, 26, 16, HudZones.ColumnW - 24f, 40f, s, 1);
            // the HUD slot names under the three buttons at the shared floor size (one line, the widest box a slot gets)
            foreach (string s in new[] { CriticalMass.Sim.Core.LightWeapons.BuilderName, CriticalMass.Sim.Core.LightWeapons.HealerName, CriticalMass.Sim.Core.LightWeapons.DemolitionsName, GameHud.MinerAbility, "CORRUPTION", "BIOMASS" })
                yield return new Spec("slot name", ui, HudLayout.AbilityNameMax, HudLayout.AbilityNameFloor, HudLayout.AbilityPitch - HudLayout.AbilityNameGap, GameHud.AbilityNameH, s, 1);
            // the prompt pill: two actions share one line in the reserved lane (as wide as the bottom band)
            yield return new Spec("prompt pill, two actions", disp, UiScale.TypeBody, 14, HudZones.BottomBandW - 28f, CriticalMass.Runtime.View.Affordance.TerrainAffordanceView.LineH,
                "HOLD RIGHT CLICK  BUILD BRIDGE (12)" + CriticalMass.Runtime.View.Affordance.TerrainAffordanceView.ActionJoin + "SPACE  BUILD LADDER (12)", 1);
            yield return new Spec("settings tab (8 tabs)", disp, 22, 15, 1060f / 8f - 14f, 60f, "CONTROLS");
            yield return new Spec("class card name", disp, 36, 20, 300f, 48f, "DEMOLITIONS EXPERT");
        }

        static Text Make(GameObject root, Spec s)
        {
            Text t = HudText.Make(root.transform, s.Where, s.Face, s.Max, Color.white, TextAnchor.MiddleLeft, 2f);
            MenuUi.Place(t.rectTransform, 0f, 0f, s.W, s.H);
            Assert.IsNotNull(t.font, s.Where + ": the font loads");
            return t;
        }

        [Test]
        public void TheOneTextRuleKeepsEveryRegisteredLabelInsideItsBox()
        {
            var root = new GameObject("Containment Probe", typeof(RectTransform));
            bool stress = TextFit.Stress;
            var bad = new List<string>();
            int checkedCount = 0;
            try
            {
                foreach (bool padded in new[] { false, true })
                {
                    TextFit.Stress = padded;   // the -hudStress capture: every text 30% longer
                    foreach (Spec s in Registered())
                    {
                        Text t = Make(root, s);
                        t.text = s.Longest;
                        ContainedText c = ContainedText.Attach(t, s.Max, s.Min, s.Lines);
                        c.ApplyNow();
                        checkedCount++;
                        string what = s.Where + (padded ? " (+30%)" : "") + " '" + t.text + "' @" + t.fontSize;
                        if (!TextFit.Contained(t)) bad.Add(what + ": draws outside its box");
                        if (t.fontSize < s.Min) bad.Add(what + ": under its floor " + s.Min);
                        if (!padded && t.text != s.Longest && t.text.IndexOf(TextFit.Ellipsis) < 0 && !t.text.EndsWith("..."))
                            bad.Add(what + ": changed without an ellipsis");
                        Object.DestroyImmediate(t.gameObject);
                    }
                }
            }
            finally
            {
                TextFit.Stress = stress;
                Object.DestroyImmediate(root);
            }
            Debug.Log("hud-containment-1: labels checked " + checkedCount);
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void TheRuleWrapsThenShrinksThenEndsWithAnEllipsisAtAWordEnd()
        {
            var root = new GameObject("Containment Probe", typeof(RectTransform));
            try
            {
                Text t = HudText.Make(root.transform, "Probe", CMTokens.Ui, 20, Color.white, TextAnchor.MiddleLeft, 2f);
                MenuUi.Place(t.rectTransform, 0f, 0f, 200f, 50f);
                // fix/hud-containment-3: the rule measures without a graphics device (the runner gave no wrapped height)
                Assert.Greater(TextFit.WidthOf(t, "SHORT", 20), 20f, "the rule measures words on the test runner");
                Assert.Greater(TextFit.HeightOf(t, "BUILD A ROPE BRIDGE OVER THE GAP", 20, 200f, true), TextFit.LineHeightOf(t, 20) * 1.5f, "a long line wraps to more than one line at 200 px");
                // fits: untouched
                Assert.AreEqual("SHORT", TextFit.Apply(t, "SHORT", 20, 12, 2));
                Assert.AreEqual(20, t.fontSize);
                // two lines allowed: wraps before it shrinks to the floor
                string two = TextFit.Apply(t, "BUILD A ROPE BRIDGE OVER THE GAP", 20, 12, 2);
                Assert.AreEqual("BUILD A ROPE BRIDGE OVER THE GAP", two, "wrapped whole");
                Assert.IsTrue(TextFit.Contained(t));
                // far too long: ends with an ellipsis after a whole word, inside the box
                string cut = TextFit.Apply(t, "THE EGG IS EXPOSED. EVERY DELIVERY COUNTS DOUBLE. THE EGG IS EXPOSED. EVERY DELIVERY COUNTS DOUBLE.", 20, 12, 2);
                Assert.IsTrue(cut.EndsWith(TextFit.Ellipsis.ToString()) || cut.EndsWith("..."), cut);
                Assert.IsTrue(TextFit.Contained(t), cut);
                Assert.AreEqual(12, t.fontSize, "at the floor before the cut");
                string body = cut.TrimEnd('.', TextFit.Ellipsis);
                Assert.IsTrue("THE EGG IS EXPOSED. EVERY DELIVERY COUNTS DOUBLE.".StartsWith(body.Substring(0, Mathf.Min(body.Length, 10))), cut);
                // the same words again: cached, nothing re-measured, same result
                Assert.AreEqual(cut, TextFit.Apply(t, "THE EGG IS EXPOSED. EVERY DELIVERY COUNTS DOUBLE. THE EGG IS EXPOSED. EVERY DELIVERY COUNTS DOUBLE.", 20, 12, 2));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void TheLeftStackAndTheBottomLaneHaveASlotEach()
        {
            // left stack, bottom up: player card, kit, coin chip, ping feed (they shared one spot: "Vale: Help!" over the kit rows)
            Assert.LessOrEqual(HudZones.Kit.yMax + HudZones.ColumnGap, HudZones.PlayerCard.y + 0.01f, "kit above the player card");
            Assert.LessOrEqual(HudZones.CoinChip.yMax + HudZones.ColumnGap, HudZones.Kit.y + 0.01f, "coin chip above the kit");
            Assert.LessOrEqual(HudZones.PingFeed.yMax + HudZones.ColumnGap, HudZones.CoinChip.y + 0.01f, "ping feed above the coin chip");
            // the kit is sized to its rows: a TEAM BUILD BANK kit is a title and a bank line, no empty panel
            Assert.AreEqual(HudZones.KitMaxH, CriticalMass.Runtime.Hud.Mechanics.MechanicsHud.KitHeight(2, true), 0.01f);
            Assert.Less(CriticalMass.Runtime.Hud.Mechanics.MechanicsHud.KitHeight(0, true), 90f);
            // bottom lane, bottom up: subtitles, two captions, the prompt; all clear of the player card and the minimap
            Assert.LessOrEqual(HudZones.Caption(0).yMax, HudZones.Subtitles.y + 0.01f);
            Assert.LessOrEqual(HudZones.Caption(1).yMax, HudZones.Caption(0).y + 0.01f);
            Assert.LessOrEqual(HudZones.Prompt.yMax, HudZones.Caption(HudZones.CaptionLines - 1).y + 0.01f, "prompt above the captions");
            foreach (Rect r in new[] { HudZones.Subtitles, HudZones.Caption(0), HudZones.Caption(1), HudZones.Prompt })
            {
                Assert.GreaterOrEqual(r.x, HudZones.PlayerCard.xMax + HudZones.ColumnGap - 0.01f, r + " clears the player card");
                Assert.LessOrEqual(r.xMax, HudZones.Minimap.x - HudZones.ColumnGap + 0.01f, r + " clears the minimap");
            }
            Assert.LessOrEqual(HudZones.AutomationPlate.xMax, HudLayout.RefRect(HudCluster.Match).x, "plate left of the top band");
        }

        [Test]
        public void ThePromptPillNeverLeavesTheScreen()
        {
            foreach (Vector2 sh in Shapes)
            {
                float k = HudLayout.Scale(sh.x, sh.y);
                var canvas = new Vector2(sh.x / k, sh.y / k);
                float w = Mathf.Min(CriticalMass.Runtime.View.Affordance.TerrainAffordanceView.MaxPillW, canvas.x - 2f * HudLayout.Margin);
                var size = new Vector2(w, 2f + CriticalMass.Runtime.View.Affordance.TerrainAffordanceView.LineH + CriticalMass.Runtime.View.Affordance.TerrainAffordanceView.DetailH);
                foreach (Vector2 want in new[] { new Vector2(0f, 0f), new Vector2(canvas.x, canvas.y), new Vector2(canvas.x * 0.5f, canvas.y * 0.5f), new Vector2(-50f, canvas.y + 50f) })
                {
                    Vector2 p = CriticalMass.Runtime.View.Affordance.TerrainAffordanceView.ClampPill(want, size, canvas);
                    Assert.GreaterOrEqual(p.x - size.x * 0.5f, HudLayout.Margin - 0.01f, sh + " left edge");
                    Assert.LessOrEqual(p.x + size.x * 0.5f, canvas.x - HudLayout.Margin + 0.01f, sh + " right edge");
                    Assert.GreaterOrEqual(p.y, HudLayout.Margin - 0.01f, sh + " bottom edge");
                    Assert.LessOrEqual(p.y + size.y, canvas.y - HudLayout.Margin + 0.01f, sh + " top edge");
                }
            }
        }

        [Test]
        public void ASettingsTabTallerThanItsPanelScrollsInsideIt()
        {
            var root = new GameObject("Settings Probe", typeof(RectTransform));
            try
            {
                var space = (RectTransform)root.transform;
                space.sizeDelta = new Vector2(HudLayout.RefW, HudLayout.RefH);
                RectTransform tab = MenuUi.Rt(space, "Game Tab");
                MenuUi.Stretch(tab);
                // the GAME tab plus the rows other features add (gore, camera): 14 rows of 60 px at a 68 px pitch
                for (int i = 0; i < 14; i++)
                    MenuUi.Shape(tab, "Row " + i, ShapeKind.RoundRect, PauseMenu.ContentX, PauseMenu.ContentY + i * 68f, 900f, 60f, Color.gray);
                Assert.Greater(ContainedScroll.ContentBottom(tab), PauseMenu.TabViewBottom, "taller than the panel");
                RectTransform vp = ContainedScroll.Contain(tab, PauseMenu.TabViewX, PauseMenu.TabViewTop, PauseMenu.TabViewW, PauseMenu.TabViewBottom);
                Assert.IsNotNull(vp, "it scrolls");
                Assert.IsNotNull(vp.GetComponent<RectMask2D>(), "clipped to the panel");
                ScrollRect sr = vp.GetComponent<ScrollRect>();
                Assert.IsTrue(sr.vertical && !sr.horizontal);
                Assert.AreEqual(14, sr.content.childCount, "every row moved into the scrolling content");
                Assert.LessOrEqual(PauseMenu.TabViewBottom, PauseMenu.PanelY + PauseMenu.PanelH, "the view ends inside the panel");
                // a tab that fits is left as built
                RectTransform small = MenuUi.Rt(space, "Small Tab");
                MenuUi.Stretch(small);
                MenuUi.Shape(small, "Row", ShapeKind.RoundRect, PauseMenu.ContentX, PauseMenu.ContentY, 900f, 60f, Color.gray);
                Assert.IsNull(ContainedScroll.Contain(small, PauseMenu.TabViewX, PauseMenu.TabViewTop, PauseMenu.TabViewW, PauseMenu.TabViewBottom));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void TheStressCaptureFlagIsReadAndPadsByThirtyPercent()
        {
            Assert.IsTrue(HudStressFlag.In(new[] { "game", "-shots", "-hudStress" }));
            Assert.IsFalse(HudStressFlag.In(new[] { "game", "-shots" }));
            Assert.AreEqual(13, TextFit.Padded("0123456789").Length);
        }
    }
}
