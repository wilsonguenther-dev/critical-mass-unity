using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menus.Characters;
using CriticalMass.Sim.Config;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>b13-v: the CHARACTERS screen's data, navigation, clip fallback, controls table and layout. Every case prints.</summary>
    public class CharactersScreenTests
    {
        static void P(string s) => Debug.Log("[CharactersScreen] " + s);

        [Test]
        public void SixTilesThreeTabsInClassOrder()
        {
            Assert.AreEqual(6, CharacterShowcaseData.All.Length);
            for (int i = 0; i < CharacterShowcaseData.All.Length; i++)
            {
                ShowcaseClass k = CharacterShowcaseData.All[i];
                P("tile " + i + " " + k.Name + " (" + k.FileId + ") role \"" + k.Role + "\"");
                Assert.AreEqual(ClassIds.All[i], k.Id, "tiles follow ClassId order");
                Assert.IsNotEmpty(k.Role);
                Assert.IsNotEmpty(k.Summary);
            }
            string[] tabs = Enum.GetNames(typeof(ShowcaseTab));
            P("tabs " + string.Join(", ", tabs) + " (limit 3)");
            Assert.AreEqual(3, tabs.Length);
        }

        [Test]
        public void EveryClassHasFourSlidesAndAClipPath()
        {
            foreach (ShowcaseClass k in CharacterShowcaseData.All)
            {
                string clip = CharacterShowcaseData.ClipPath(k.Id);
                P(k.Name + " slides=" + k.Slides.Length + " (limit 4) clip=" + clip + " present=" + CharacterShowcaseData.FileThere(clip));
                Assert.AreEqual(CharacterShowcaseData.SlideCount, k.Slides.Length, k.Name);
                StringAssert.EndsWith("Characters/" + k.FileId + ".webm", clip.Replace('\\', '/'));
                for (int n = 0; n < k.Slides.Length; n++)
                {
                    Assert.IsNotEmpty(k.Slides[n].Title, k.Name + " slide " + n);
                    Assert.IsNotEmpty(k.Slides[n].Line, k.Name + " slide " + n);
                    StringAssert.EndsWith("still-" + k.FileId + "-" + (n + 1) + ".png", CharacterShowcaseData.StillPath(k.Id, n + 1).Replace('\\', '/'));
                }
            }
        }

        static ShowcaseNav Run(ShowcaseNav s, params ShowcaseInput[] inputs)
        {
            foreach (ShowcaseInput i in inputs) s = ShowcaseNav.Step(s, i, 6);
            return s;
        }

        [Test]
        public void NavigationOrderByKeyboardAndController()
        {
            // Both devices map to the same presses: W / up / d-pad up = Up, Q / E = LB / RB, Enter = A, Escape = B.
            var s = new ShowcaseNav { Class = 0, Tab = ShowcaseTab.Watch };
            var order = new List<int>();
            for (int i = 0; i < 7; i++) { order.Add(s.Class); s = ShowcaseNav.Step(s, ShowcaseInput.Down, 6); }
            P("down x7 visits classes " + string.Join(" ", order) + " then " + s.Class);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5, 0 }, order);
            s = Run(new ShowcaseNav(), ShowcaseInput.Up);
            P("up from Warrior wraps to " + s.Class + " (limit 5)");
            Assert.AreEqual(5, s.Class);
            s = Run(new ShowcaseNav(), ShowcaseInput.NextTab, ShowcaseInput.NextTab, ShowcaseInput.NextTab);
            P("next tab x3 ends on " + s.Tab);
            Assert.AreEqual(ShowcaseTab.Watch, s.Tab);
            s = Run(new ShowcaseNav(), ShowcaseInput.PrevTab);
            P("prev tab from WATCH: " + s.Tab);
            Assert.AreEqual(ShowcaseTab.Controls, s.Tab);
            s = Run(new ShowcaseNav { Tab = ShowcaseTab.HowItWorks }, ShowcaseInput.Right, ShowcaseInput.Right, ShowcaseInput.Left);
            P("HOW IT WORKS right, right, left: slide " + s.Slide);
            Assert.AreEqual(1, s.Slide);
            s = Run(new ShowcaseNav { Tab = ShowcaseTab.Controls }, ShowcaseInput.Right);
            P("CONTROLS right: slide " + s.Slide + " (no slides there)");
            Assert.AreEqual(0, s.Slide);
            s = Run(new ShowcaseNav(), ShowcaseInput.Select);
            P("select: in pane " + s.InPane);
            Assert.IsTrue(s.InPane);
            s = Run(s, ShowcaseInput.Back);
            P("back from the pane: in pane " + s.InPane + " closed " + s.Closed);
            Assert.IsFalse(s.InPane);
            Assert.IsFalse(s.Closed);
            s = Run(s, ShowcaseInput.Back);
            P("back from the tiles: closed " + s.Closed);
            Assert.IsTrue(s.Closed);
            s = Run(new ShowcaseNav { Tab = ShowcaseTab.Controls, InPane = true }, ShowcaseInput.Down);
            P("down inside the pane keeps the tab " + s.Tab + " and picks class " + s.Class);
            Assert.AreEqual(ShowcaseTab.Controls, s.Tab);
            Assert.AreEqual(1, s.Class);
        }

        [Test]
        public void AMissingClipFallsBackToTheFlipbook()
        {
            string missing = CharacterShowcaseData.ClipPath(ClassId.Warrior) + ".missing";
            bool there = CharacterShowcaseData.FileThere(missing);
            P("missing file present=" + there + " useClip=" + CharacterShowcaseData.UseClip(there, false, 0f, false));
            Assert.IsFalse(CharacterShowcaseData.UseClip(there, false, 0f, false));
            P("file there, waiting 1.0 s, not ready: useClip=" + CharacterShowcaseData.UseClip(true, false, 1f, false));
            Assert.IsTrue(CharacterShowcaseData.UseClip(true, false, 1f, false));
            P("file there, not ready after 2.5 s (limit 2): useClip=" + CharacterShowcaseData.UseClip(true, false, 2.5f, false));
            Assert.IsFalse(CharacterShowcaseData.UseClip(true, false, 2.5f, false));
            P("file there, decode error: useClip=" + CharacterShowcaseData.UseClip(true, true, 0.1f, false));
            Assert.IsFalse(CharacterShowcaseData.UseClip(true, true, 0.1f, false));
            float[] ts = { 0f, 1.7f, 1.9f, 2.0f, 7.9f, 8.0f };
            foreach (float t in ts)
            {
                CharacterShowcaseData.Flipbook(t, out int a, out int b, out float blend);
                P("flipbook t=" + t.ToString("0.00") + "s slide=" + a + " next=" + b + " blend=" + blend.ToString("0.00"));
            }
            CharacterShowcaseData.Flipbook(1.0f, out int s0, out _, out float b0);
            CharacterShowcaseData.Flipbook(1.8f, out int s1, out int n1, out float b1);
            CharacterShowcaseData.Flipbook(8.0f, out int s2, out _, out _);
            Assert.AreEqual(0, s0); Assert.AreEqual(0f, b0);
            Assert.AreEqual(0, s1); Assert.AreEqual(1, n1); Assert.That(b1, Is.InRange(0.4f, 0.6f));
            Assert.AreEqual(0, s2, "loops after the fourth slide");
        }

        [Test]
        public void ControlsRowsMatchTheBindingsForEachClass()
        {
            var bound = new HashSet<string>();
            foreach (ControlRow r in ControlBindings.Rows)
            {
                foreach (RebindSlot k in r.Keyboard) bound.Add(k.Action);
                foreach (RebindSlot k in r.Gamepad) bound.Add(k.Action);
            }
            foreach (ShowcaseClass k in CharacterShowcaseData.All)
            {
                ShowcaseControl[] rows = CharacterShowcaseData.ControlsFor(k.Id);
                Assert.AreEqual(7, rows.Length, k.Name);   // b13 r3 integrator: Dodge row back; walls (Switch piece) still cut
                foreach (ShowcaseControl c in rows)
                {
                    string kb = CharacterShowcaseData.KeyWords(c.Action, ControlDevice.KeyboardMouse);
                    string pad = CharacterShowcaseData.KeyWords(c.Action, ControlDevice.Gamepad);
                    P(k.Name.PadRight(14) + c.Label.PadRight(14) + c.Action.PadRight(16) + "keys=" + kb.PadRight(22) + "pad=" + pad.PadRight(14) + c.What);
                    if (c.Action == "Dodge" && !bound.Contains("Dodge"))
                    {
                        Assert.AreEqual(CharacterShowcaseData.DodgeKeyFallback, kb, "dodge default until the binding exists");
                        continue;
                    }
                    Assert.IsTrue(bound.Contains(c.Action), c.Action + " is a ControlBindings action");
                    string expectKb = c.Action == "Move" ? InputGlyphs.MoveWords(ControlDevice.KeyboardMouse) : InputGlyphs.Words(c.Action, ControlDevice.KeyboardMouse);
                    if (expectKb != "-" && expectKb.Length > 0) Assert.AreEqual(expectKb, kb, k.Name + " " + c.Label);
                }
            }
        }

        static bool Overlap(Rect a, Rect b) => a.xMin < b.xMax - 0.5f && b.xMin < a.xMax - 0.5f && a.yMin < b.yMax - 0.5f && b.yMin < a.yMax - 0.5f;

        [Test]
        public void TheLayoutFitsFrom720pTo4K()
        {
            var shapes = new[] { new Vector2(1280, 720), new Vector2(1920, 1080), new Vector2(2560, 1440), new Vector2(3840, 2160) };
            (string name, Rect rect)[] all = CharacterShowcaseData.LayoutRects();
            foreach (Vector2 sz in shapes)
                foreach ((string name, Rect rect) in all)
                {
                    Rect px = CharacterShowcaseData.ToPixels(rect, sz.x, sz.y);
                    P(sz.x + "x" + sz.y + " " + name.PadRight(16) + " x=" + px.x.ToString("0") + " y=" + px.y.ToString("0") + " w=" + px.width.ToString("0") + " h=" + px.height.ToString("0"));
                    Assert.GreaterOrEqual(px.xMin, -0.5f, name);
                    Assert.GreaterOrEqual(px.yMin, -0.5f, name);
                    Assert.LessOrEqual(px.xMax, sz.x + 0.5f, name);
                    Assert.LessOrEqual(px.yMax, sz.y + 0.5f, name);
                    Assert.GreaterOrEqual(px.height, 17f, name + ": text rows stay readable");
                }
            // No overlap inside each group shown together (the tab contents share the pane, one at a time).
            var frame = new List<(string, Rect)> { ("title", CharacterShowcaseData.Title), ("pane", CharacterShowcaseData.Pane), ("back", CharacterShowcaseData.Back), ("hint", CharacterShowcaseData.Hint) };
            for (int i = 0; i < 6; i++) frame.Add(("tile " + i, CharacterShowcaseData.Tile(i)));
            for (int i = 0; i < 3; i++) frame.Add(("tab " + i, CharacterShowcaseData.Tab(i)));
            var watch = new List<(string, Rect)> { ("summary", CharacterShowcaseData.Summary), ("video", CharacterShowcaseData.Video), ("caption", CharacterShowcaseData.VideoCaption) };
            var how = new List<(string, Rect)> { ("summary", CharacterShowcaseData.Summary), ("still", CharacterShowcaseData.Still), ("slide title", CharacterShowcaseData.SlideTitle),
                ("slide line", CharacterShowcaseData.SlideLine), ("prev", CharacterShowcaseData.PrevArrow), ("next", CharacterShowcaseData.NextArrow) };
            var ctrl = new List<(string, Rect)> { ("summary", CharacterShowcaseData.Summary) };
            for (int i = 0; i < 8; i++) ctrl.Add(("controls row " + i, CharacterShowcaseData.CtrlRow(i)));
            foreach (var group in new[] { frame, watch, how, ctrl })
                for (int i = 0; i < group.Count; i++)
                    for (int j = i + 1; j < group.Count; j++)
                    {
                        bool o = Overlap(group[i].Item2, group[j].Item2);
                        if (o) P("OVERLAP " + group[i].Item1 + " / " + group[j].Item1);
                        Assert.IsFalse(o, group[i].Item1 + " overlaps " + group[j].Item1);
                    }
            foreach (var group in new[] { watch, how, ctrl })
                foreach ((string n, Rect r) in group)
                {
                    bool inside = CharacterShowcaseData.Pane.Contains(r.min) && CharacterShowcaseData.Pane.Contains(r.max);
                    P(n + " inside the pane: " + inside);
                    Assert.IsTrue(inside, n + " stays inside the pane");
                }
            P("groups checked: frame " + frame.Count + ", watch " + watch.Count + ", how " + how.Count + ", controls " + ctrl.Count);
        }
    }
}
