using System.IO;
using CriticalMass.Runtime.Audio.Sound;
using CriticalMass.Runtime.Menus;
using CriticalMass.Runtime.View.Affordance;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b15 view polish, Editor review item 4: the "F DIG" prompt and the sound captions drew over class select and the
    /// profile (screen-space overlay canvases draw over camera-space menus whatever the sorting order). One shared gate,
    /// MenuGate.AnyOpen, hides every in-world prompt and caption while a menu or overlay is open.
    /// </summary>
    public sealed class MenuGateTests
    {
        [TearDown]
        public void Reset() => MenuGate.OverrideForTests = null;

        [Test]
        public void CaptionsAndPromptsFollowTheOneGate()
        {
            MenuGate.OverrideForTests = true;
            Assert.IsTrue(SoundCaptions.HiddenNow(false, false), "captions hide under a menu");
            Assert.IsTrue(TerrainAffordanceView.MenuCovers, "the prompt view reads the same gate");
            MenuGate.OverrideForTests = false;
            Assert.IsFalse(SoundCaptions.HiddenNow(false, false), "captions show with no menu open");
            Assert.IsTrue(SoundCaptions.HiddenNow(true, false), "and still hide while paused");
            Assert.IsFalse(TerrainAffordanceView.MenuCovers);
        }

        [Test]
        public void TheNewMenuFlagsAreClosedWithNoMenuInTheScene()
        {
            Assert.IsFalse(ClassSelect.SetupOpen);
            Assert.IsFalse(ProfileMenuScreen.AnyOpen);
            Assert.IsFalse(CriticalMass.Runtime.Menu.PauseMenu.AnyOpen);
        }

        [Test]
        public void EveryInWorldOverlayReadsTheGate()
        {
            string rt = Path.Combine(Application.dataPath, "_Game", "Runtime");
            string[] files =
            {
                Path.Combine(rt, "Audio", "Sound", "SoundDirector.cs"), Path.Combine(rt, "Audio", "Announcer.cs"),
                Path.Combine(rt, "Hud", "TerrainPrompt.cs"), Path.Combine(rt, "View", "Affordance", "TerrainAffordanceView.cs"),
                Path.Combine(rt, "Hud", "Mechanics", "B15RulesHud.cs"),
            };
            foreach (string f in files)
            {
                string src = File.ReadAllText(f);
                Debug.Log($"[b15 menu gate] {Path.GetFileName(f)} reads MenuGate.AnyOpen: {src.Contains("MenuGate.AnyOpen")}");
                StringAssert.Contains("MenuGate.AnyOpen", src, Path.GetFileName(f));
            }
            string gate = File.ReadAllText(Path.Combine(rt, "Menus", "MenuGate.cs"));
            foreach (string must in new[] { "ClassSelect.SetupOpen", "ProfileMenuScreen.AnyOpen", "PauseMenu.AnyOpen", "FeedbackCard.IsShowing", "OverlayGuard.Active" })
                StringAssert.Contains(must, gate, "the gate covers " + must);
        }
    }
}
