using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// Readable input prompts (2026-11-12): every Player action maps to a drawn glyph and plain words on keyboard-mouse and
    /// on a controller; mouse buttons are a mouse with the button lit and "Left click" / "Right click" / "Middle click" /
    /// "Scroll"; no prompt text, settings cell text or words-pack line contains LMB, RMB or MMB; menu hints become glyphs.
    /// </summary>
    public sealed class InputGlyphsTests
    {
        static readonly ControlDevice[] Devices = { ControlDevice.KeyboardMouse, ControlDevice.Gamepad };

        [Test]
        public void EveryActionMapsToAGlyphAndWordsOnBothDevices()
        {
            foreach (ControlDevice d in Devices)
                foreach (string action in InputGlyphs.PlayerActions)
                {
                    GlyphSpec g = InputGlyphs.ForAction(action, d);
                    Assert.IsFalse(g.IsEmpty, $"{action} on {d} has no glyph");
                    Assert.IsFalse(string.IsNullOrEmpty(g.Words), $"{action} on {d} has no words");
                    Assert.AreNotEqual("-", g.Words, $"{action} on {d}");
                    Assert.IsFalse(InputGlyphs.ContainsBannedAbbreviation(g.Words), $"{action} on {d}: '{g.Words}'");
                    Assert.IsFalse(InputGlyphs.ContainsBannedAbbreviation(g.Cap), $"{action} on {d}: cap '{g.Cap}'");
                }
        }

        [Test]
        public void KeyboardDefaultsAreAMouseWithTheRightButtonLitAndKeycaps()
        {
            GlyphSpec attack = InputGlyphs.ForAction("Attack", ControlDevice.KeyboardMouse);
            Assert.AreEqual(GlyphKind.Mouse, attack.Kind);
            Assert.AreEqual(GlyphMark.Left, attack.Mark);
            Assert.AreEqual("Left click", attack.Words);
            Assert.IsNull(attack.Cap, "a mouse glyph prints no letters");
            GlyphSpec ability = InputGlyphs.ForAction("Ability", ControlDevice.KeyboardMouse);
            Assert.AreEqual(GlyphMark.Right, ability.Mark);
            Assert.AreEqual("Right click", ability.Words);
            GlyphSpec jump = InputGlyphs.ForAction("Jump", ControlDevice.KeyboardMouse);
            Assert.AreEqual(GlyphKind.Key, jump.Kind);
            Assert.AreEqual("Space", jump.Cap);
            Assert.AreEqual("E", InputGlyphs.ForAction("Interact", ControlDevice.KeyboardMouse).Cap);
            GlyphSpec move = InputGlyphs.ForAction("Move", ControlDevice.KeyboardMouse);
            Assert.AreEqual(GlyphKind.Key, move.Kind);
            Assert.AreEqual("WASD", move.Cap);
        }

        [Test]
        public void ControllerDefaultsAreTriggersFaceButtonsAndSticks()
        {
            GlyphSpec attack = InputGlyphs.ForAction("Attack", ControlDevice.Gamepad);
            Assert.AreEqual(GlyphKind.Trigger, attack.Kind);
            Assert.AreEqual("RT", attack.Cap);
            Assert.AreEqual("Right trigger", attack.Words);
            GlyphSpec jump = InputGlyphs.ForAction("Jump", ControlDevice.Gamepad);
            Assert.AreEqual(GlyphKind.FaceButton, jump.Kind);
            Assert.AreEqual("A", jump.Cap);
            Assert.AreEqual(InputGlyphs.FaceA, jump.Face);
            Assert.AreEqual(GlyphKind.Stick, InputGlyphs.ForAction("Move", ControlDevice.Gamepad).Kind);
            Assert.AreEqual(GlyphKind.Bumper, InputGlyphs.ForAction("SwitchDigPlace", ControlDevice.Gamepad).Kind, "composite part (Shoulders)");
        }

        [Test]
        public void PathsMapToTheirShapes()
        {
            Assert.AreEqual(GlyphMark.Middle, InputGlyphs.ForPath("<Mouse>/middleButton").Mark);
            Assert.AreEqual("Middle click", InputGlyphs.ForPath("<Mouse>/middleButton").Words);
            Assert.AreEqual(GlyphMark.Wheel, InputGlyphs.ForPath("<Mouse>/scroll/y").Mark);
            Assert.AreEqual("Scroll", InputGlyphs.ForPath("<Mouse>/scroll/y").Words);
            GlyphSpec up = InputGlyphs.ForPath("<Keyboard>/upArrow");
            Assert.AreEqual(GlyphKind.Key, up.Kind);
            Assert.AreEqual(GlyphMark.Up, up.Mark);
            Assert.IsNull(up.Cap, "arrow keys draw an arrow");
            Assert.AreEqual("Esc", InputGlyphs.ForPath("<Keyboard>/escape").Cap);
            Assert.AreEqual("1", InputGlyphs.ForPath("<Keyboard>/1").Cap);
            Assert.AreEqual(GlyphKind.Bumper, InputGlyphs.ForPath("<Gamepad>/leftShoulder").Kind);
            Assert.AreEqual(GlyphMark.Press, InputGlyphs.ForPath("<Gamepad>/rightStickPress").Mark);
            Assert.AreEqual(GlyphMark.Up, InputGlyphs.ForPath("<Gamepad>/dpad/up").Mark);
            Assert.AreEqual(GlyphKind.FaceButton, InputGlyphs.ForPath("<XInputController>/buttonEast").Kind);
            Assert.AreEqual("Menu", InputGlyphs.ForPath("<Gamepad>/start").Cap);
        }

        [Test]
        public void TheBannedAbbreviationCheckFindsWholeWordsOnly()
        {
            Assert.IsTrue(InputGlyphs.ContainsBannedAbbreviation("HOLD MMB TO LOOK"));
            Assert.IsTrue(InputGlyphs.ContainsBannedAbbreviation("lmb"));
            Assert.IsTrue(InputGlyphs.ContainsBannedAbbreviation("[RMB]"));
            Assert.IsFalse(InputGlyphs.ContainsBannedAbbreviation("CLIMB THE HILL"));
            Assert.IsFalse(InputGlyphs.ContainsBannedAbbreviation("Left click"));
            Assert.IsFalse(InputGlyphs.ContainsBannedAbbreviation(null));
        }

        [Test]
        public void SettingsAndPromptTextsNeverUseTheAbbreviations()
        {
            foreach (string p in new[] { "<Mouse>/leftButton", "<Mouse>/rightButton", "<Mouse>/middleButton", "<Mouse>/scroll/y" })
                Assert.IsFalse(InputGlyphs.ContainsBannedAbbreviation(ControlBindings.DisplayPath(p)), p);
            InputActionMap map = ControlBindings.Map(InputSystem.actions);
            if (map == null) Assert.Ignore("no project-wide actions in this editor");
            foreach (ControlRow row in ControlBindings.Rows)
            {
                foreach (RebindSlot[] set in new[] { row.Keyboard, row.Gamepad })
                    foreach (RebindSlot slot in set)
                    {
                        InputAction a = map.FindAction(slot.Action, false);
                        string text = ControlBindings.Display(a, ControlBindings.FindBindingIndex(a, slot));
                        Assert.IsFalse(InputGlyphs.ContainsBannedAbbreviation(text), $"{row.Label}: '{text}'");
                    }
            }
            foreach (string action in InputGlyphs.PlayerActions)
                foreach (ControlDevice d in Devices)
                {
                    string label = ControlBindings.PromptLabel(map.FindAction(action, false), d);
                    Assert.IsFalse(InputGlyphs.ContainsBannedAbbreviation(label), $"{action} {d}: '{label}'");
                }
        }

        [Test]
        public void TheWordsPackHasNoAbbreviations()
        {
            TextAsset[] all = Resources.LoadAll<TextAsset>("Lore");
            foreach (TextAsset t in all)
                Assert.IsFalse(InputGlyphs.ContainsBannedAbbreviation(t.text), $"Resources/Lore/{t.name} contains LMB, RMB or MMB");
            foreach (TextAsset t in all) Resources.UnloadAsset(t);
        }

        [Test]
        public void MenuHintsBecomeGlyphsAndWords()
        {
            var host = new GameObject("Hint Host", typeof(RectTransform));
            try
            {
                PromptStrip s = PromptStrip.Make(host.transform, "Strip", 30f, 20, Color.white);
                s.SetHint("ENTER SELECT   ·   ESC BACK", ControlDevice.KeyboardMouse);
                Assert.AreEqual("Enter SELECT Escape BACK", s.PlainText);
                s.SetHint("A SELECT   ·   LB / RB TABS", ControlDevice.Gamepad);
                Assert.AreEqual("A button SELECT Left bumper Right bumper TABS", s.PlainText);
                s.SetHint("PRESS A KEY OR MOUSE BUTTON", ControlDevice.KeyboardMouse);
                Assert.AreEqual("PRESS A KEY OR MOUSE BUTTON", s.PlainText, "a part that does not start with a key stays words");
                Assert.IsFalse(InputGlyphs.ContainsBannedAbbreviation(s.PlainText));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void AGlyphViewSizesToItsKeyAndShowsWordsWhenAsked()
        {
            var host = new GameObject("Glyph Host", typeof(RectTransform));
            try
            {
                InputGlyphView v = InputGlyphView.Make(host.transform, "Glyph", 30f, true);
                float wE = v.Set(InputGlyphs.ForPath("<Keyboard>/e"));
                Assert.AreEqual(GlyphKind.Key, v.Graphic.Kind);
                Assert.AreEqual("E", v.Cap.text);
                Assert.Greater(wE, v.GlyphWidth, "the words sit beside the glyph");
                v.ShowWords = false;
                float space = v.Set(InputGlyphs.ForPath("<Keyboard>/space"));
                Assert.Greater(space, 30f, "a long key name widens the keycap");
                v.Set(InputGlyphs.ForPath("<Mouse>/rightButton"));
                Assert.AreEqual(GlyphKind.Mouse, v.Graphic.Kind);
                Assert.AreEqual(GlyphMark.Right, v.Graphic.Mark);
                Assert.IsFalse(v.Cap.gameObject.activeSelf, "a mouse glyph has no letters");
                Assert.AreEqual("Right click", v.WordsText.text);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
