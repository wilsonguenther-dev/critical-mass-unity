using System;
using System.Collections.Generic;
using System.Reflection;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Menus;
using CriticalMass.Sim.Config;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// Owner walk 1 (2026-11-13): "the HUD things are phasing into each other" and "the warrior is still not clickable".
    /// The HUD layout table at the five screen shapes the owner and the reviewers use (with the camera notch on the
    /// MacBook shapes), the anchor frames, the clickable fighter and class plate on Home, the Barracks class chips and back,
    /// pointer feedback on every control, and a back key on every flow screen.
    /// </summary>
    [TestFixture]
    public sealed class OwnerWalkTests
    {
        static readonly Vector2Int[] Shapes =
        {
            new Vector2Int(3024, 1964), new Vector2Int(1512, 982), new Vector2Int(1920, 1080), new Vector2Int(2560, 1080), new Vector2Int(1280, 800),
        };

        static Vector4 Notch(Vector2Int s) => HudSafeArea.NotchFor(s.x, s.y);

        // ---------------------------------------------------------------- HUD layout

        [Test]
        public void TheNotchIsOnlyOnTheMacBookShapes()
        {
            Assert.AreEqual(74f, Notch(new Vector2Int(3024, 1964)).y, 1f, "3024x1964 notch");
            Assert.AreEqual(37f, Notch(new Vector2Int(1512, 982)).y, 1f, "1512x982 notch");
            Assert.AreEqual(Vector4.zero, Notch(new Vector2Int(1920, 1080)));
            Assert.AreEqual(Vector4.zero, Notch(new Vector2Int(2560, 1080)));
            Assert.AreEqual(Vector4.zero, Notch(new Vector2Int(1280, 800)));
        }

        [Test]
        public void AnchorFramesLeaveSixteenByNineUntouched()
        {
            foreach (HudZones.Piece p in HudZones.Pieces())
            {
                Rect px = HudFrames.PixelRect(p.Rect, 1920f, 1080f, Vector4.zero);
                Assert.AreEqual(p.Rect.x, px.x, 0.01f, p.Name);
                Assert.AreEqual(p.Rect.y, px.y, 0.01f, p.Name);
                Assert.AreEqual(p.Rect.width, px.width, 0.01f, p.Name);
            }
            Assert.AreEqual(HudAnchor.TopRight, HudFrames.Classify(HudZones.Coins), "the coin counter rides the right column");
            Assert.AreEqual(HudAnchor.Top, HudFrames.Classify(HudZones.Job), "the job line stays under the clock");
            Assert.AreEqual(HudAnchor.BottomRight, HudFrames.Classify(HudZones.Minimap));
        }

        [Test]
        public void NoHudPiecesCollideAtAnyScreenShapeWithEveryOptionalPieceShown()
        {
            HudZones.Piece[] pieces = HudZones.Pieces();
            Assert.GreaterOrEqual(pieces.Length, 15, "the table lists every piece");
            var bad = new List<string>();
            foreach (Vector2Int s in Shapes)
            {
                Vector4 ins = Notch(s);
                var safe = Rect.MinMaxRect(ins.x, ins.y, s.x - ins.z, s.y - ins.w);
                var px = new Rect[pieces.Length];
                for (int i = 0; i < pieces.Length; i++)
                {
                    px[i] = HudFrames.PixelRect(pieces[i].Rect, s.x, s.y, ins);
                    if (px[i].xMin < safe.xMin - 0.5f || px[i].yMin < safe.yMin - 0.5f || px[i].xMax > safe.xMax + 0.5f || px[i].yMax > safe.yMax + 0.5f)
                        bad.Add(s.x + "x" + s.y + ": " + pieces[i].Name + " leaves the safe area " + px[i]);
                }
                for (int i = 0; i < pieces.Length; i++)
                for (int j = i + 1; j < pieces.Length; j++)
                {
                    HudZones.Piece a = pieces[i], b = pieces[j];
                    if (a.Shared && b.Shared && a.Lane == b.Lane) continue;    // one lane, one resident at a time (the arbiter)
                    if (HudZones.Arbitrated(a.Lane, b.Lane)) continue;
                    if (px[i].Overlaps(px[j])) bad.Add(s.x + "x" + s.y + ": " + a.Name + " x " + b.Name);
                }
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void TheClockClearsTheNotchOnTheMacBook()
        {
            Rect match = HudLayout.RefRect(HudCluster.Match);
            foreach (Vector2Int s in new[] { Shapes[0], Shapes[1] })
            {
                Rect px = HudFrames.PixelRect(match, s.x, s.y, Notch(s));
                Assert.GreaterOrEqual(px.yMin, Notch(s).y - 0.5f, s + ": the clock sits under the notch, not behind it");
            }
        }

        [Test]
        public void TheDeathRecapTakesTheCentreLaneAndTheJobLineStepsBack()
        {
            HudZones.Clear();
            Assert.IsFalse(HudZones.Blocked(HudZone.Callout, HudPriority.Tip), "nothing holds the lane");
            HudZones.Hold(HudZone.Callout, HudPriority.Recap);
            Assert.IsTrue(HudZones.Blocked(HudZone.Callout, HudPriority.Tip), "the job line, stock and event bar step back");
            Assert.IsTrue(HudZones.Blocked(HudZone.Callout, HudPriority.Banner), "a banner waits for the recap");
            HudZones.Clear();
            Assert.That(DeathRecap.TopY, Is.GreaterThanOrEqualTo(HudZones.CalloutTop));
            Assert.That(DeathRecap.TopY + DeathRecap.CardH, Is.LessThanOrEqualTo(HudZones.CalloutTop + HudZones.CalloutH));
        }

        // ---------------------------------------------------------------- Home: the fighter and the class plate

        static MainMenu Home(GameObject go)
        {
            MainMenu m = go.AddComponent<MainMenu>();
            m.Show();
            MenuItem.SubmitBlockedUntil = float.MinValue;
            return m;
        }

        [Test]
        public void TheFighterAndTheClassPlateAreControlsReachedByKeysAndPad()
        {
            var go = new GameObject("OW1 Home Nav");
            try
            {
                MainMenu m = Home(go);
                Assert.IsNotNull(m.FighterItem, "the fighter is a control");
                Assert.IsNotNull(m.ClassPlateItem, "the class plate is a control");
                Assert.AreSame(m.FighterItem, m.PlayItem.navigation.selectOnRight, "PLAY right goes to the fighter");
                Assert.AreSame(m.ClassPlateItem, m.FighterItem.navigation.selectOnDown, "fighter down goes to the class plate");
                Assert.AreSame(m.FighterItem, m.ClassPlateItem.navigation.selectOnUp, "plate up goes back to the fighter");
                Assert.IsNotNull(m.FighterItem.navigation.selectOnLeft, "fighter left goes back to the column");
                Assert.IsTrue(m.FighterItem.IsInteractable());
            }
            finally { Object.DestroyImmediate(go); MenuItem.SubmitBlockedUntil = 0f; }
        }

        [Test]
        public void DraggingTheFighterTurnsItAndDoesNotCountAsAClick()
        {
            var go = new GameObject("OW1 Home Drag");
            try
            {
                MainMenu m = Home(go);
                int opened = 0;
                Action was = m.FighterItem.Submitted;
                m.FighterItem.Submitted = () => opened++;
                float before = m.FighterYaw;
                var e = new PointerEventData(null) { button = PointerEventData.InputButton.Left, delta = new Vector2(-120f, 0f) };
                m.FighterItem.OnBeginDrag(e);
                m.FighterItem.OnDrag(e);
                Assert.IsTrue(m.FighterItem.Dragging);
                Assert.That(Mathf.Abs(m.FighterYaw - before), Is.GreaterThan(0.01f), "a drag turns the fighter");
                m.FighterItem.OnPointerClick(e);
                Assert.AreEqual(0, opened, "the click that ends a drag does not open the Barracks");
                m.FighterItem.OnEndDrag(e);
                m.FighterItem.OnPointerClick(e);
                Assert.AreEqual(1, opened, "a plain click opens it");
                m.FighterItem.Submitted = was;
            }
            finally { Object.DestroyImmediate(go); MenuItem.SubmitBlockedUntil = 0f; }
        }

        [Test]
        public void ClickingTheFighterOpensTheBarracksForThatClassWithSixClassChipsAndBackCloses()
        {
            var go = new GameObject("OW1 Home Barracks");
            try
            {
                MainMenu m = Home(go);
                m.FighterItem.OnPointerClick(new PointerEventData(null) { button = PointerEventData.InputButton.Left });
                Assert.IsTrue(Barracks.IsOpen, "the fighter opens the Barracks");
                CharacterPanel p = CharacterPanel.Instance;
                Assert.IsNotNull(p);
                var chips = new List<MenuItem>();
                foreach (MenuItem it in p.GetComponentsInChildren<MenuItem>(true))
                    if (it.name.StartsWith("Class ", StringComparison.Ordinal)) chips.Add(it);
                Assert.AreEqual(6, chips.Count, "one class tab per class");
                MenuItem miner = chips.Find(c => c.name == "Class " + ClassId.Miner);
                Assert.IsNotNull(miner);
                MenuItem.SubmitBlockedUntil = float.MinValue;
                miner.Submitted();
                Assert.AreEqual(ClassId.Miner, p.ShownClass, "a class chip switches the locker to that class");
                CharacterPanel.Hide();
                Assert.IsFalse(CharacterPanel.IsOpen, "back closes the Barracks");
            }
            finally
            {
                if (CharacterPanel.Instance != null) Object.DestroyImmediate(CharacterPanel.Instance.gameObject);
                Object.DestroyImmediate(go);
                MenuItem.SubmitBlockedUntil = 0f;
            }
        }

        // ---------------------------------------------------------------- pointer feedback

        static void AssertPointerReady(Component root, List<string> bad)
        {
            foreach (Selectable s in root.GetComponentsInChildren<Selectable>(true))
            {
                if (!(s is IPointerClickHandler)) bad.Add(s.name + " has no click handler");
                MethodInfo dst = s.GetType().GetMethod("DoStateTransition", BindingFlags.Instance | BindingFlags.NonPublic);
                bool hover = s.transition != Selectable.Transition.None || (dst != null && dst.DeclaringType != typeof(Selectable));
                if (!hover) bad.Add(s.name + " has no hover look");
            }
        }

        [Test]
        public void EverySelectableOnHomeAndInTheBarracksHasAClickHandlerAndAHoverLook()
        {
            var go = new GameObject("OW1 Pointer");
            try
            {
                MainMenu m = Home(go);
                Barracks.Show(ClassId.Warrior);
                var bad = new List<string>();
                AssertPointerReady(m.Canvas, bad);
                AssertPointerReady(CharacterPanel.Instance, bad);
                Assert.GreaterOrEqual(m.Canvas.GetComponentsInChildren<Selectable>(true).Length, 8, "Home has its controls");
                Assert.IsEmpty(bad, string.Join("\n", bad));
            }
            finally
            {
                if (CharacterPanel.Instance != null) Object.DestroyImmediate(CharacterPanel.Instance.gameObject);
                Object.DestroyImmediate(go);
                MenuItem.SubmitBlockedUntil = 0f;
            }
        }

        [Test]
        public void HoveringAControlTurnsThePointerAndLeavingRestoresIt()
        {
            UiCursor.Reset();
            var go = new GameObject("OW1 Cursor");
            try
            {
                MenuItem it = MenuUi.Item(go.transform, "Item", 0f, 0f, 200f, 60f, "X", MenuItem.Kind.Button);
                var e = new PointerEventData(null);
                it.OnPointerEnter(e);
                Assert.IsTrue(UiCursor.Hovering);
                it.OnPointerExit(e);
                Assert.IsFalse(UiCursor.Hovering);
                Texture2D t = UiCursor.Make();
                Assert.AreEqual(UiCursor.Size, t.width);
                Object.DestroyImmediate(t);
            }
            finally { Object.DestroyImmediate(go); UiCursor.Reset(); }
        }

        // ---------------------------------------------------------------- navigation: no hidden keys, no dead ends

        [Test]
        public void EveryFlowScreenShowsItsBackKeyForKeyboardAndPad()
        {
            var bad = new List<string>();
            int seen = 0;
            foreach (Type t in typeof(FlowScreen).Assembly.GetTypes())
            {
                if (t.IsAbstract || !typeof(FlowScreen).IsAssignableFrom(t) || t.Name.Contains("Title") || t.Name.Contains("Loading")) continue;
                object o = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(t);
                string keys = (string)t.GetProperty("HintKeys", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o);
                string pad = (string)t.GetProperty("HintPad", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o);
                seen++;
                if (keys == null || !keys.Contains("ESC")) bad.Add(t.Name + " keys hint has no ESC: " + keys);
                if (pad == null || !(pad.Contains(" B ") || pad.StartsWith("B ", StringComparison.Ordinal))) bad.Add(t.Name + " pad hint has no B: " + pad);
            }
            Assert.GreaterOrEqual(seen, 4, "found the flow screens");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void HomeNamesTheCustomiseKey()
        {
            var go = new GameObject("OW1 Hints");
            try
            {
                MainMenu m = go.AddComponent<MainMenu>();
                string keys = (string)typeof(MainMenu).GetProperty("HintKeys", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(m);
                string pad = (string)typeof(MainMenu).GetProperty("HintPad", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(m);
                StringAssert.Contains("C CUSTOMISE", keys);
                StringAssert.Contains("X CUSTOMISE", pad);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
