using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Player;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests.PlayerExperience
{
    /// <summary>
    /// Review fix round (defects 4, 5, 10): the stack over a unit has one layout owner (UnitStack) and nothing in it
    /// overlaps at either camera; the fight rule, the low-health treatment and the no-Kaiju rule.
    /// </summary>
    [TestFixture]
    public sealed class PxHudLayoutTests
    {
        // Name-tag point (lift 2.65) and chest point (lift 1.2) on screen, px at 1080p, y up: third person at the fight
        // distance (about 8 u) the chest is ~210 px under the tag; top-down (~35 u) ~40 px; a far third-person unit ~60 px.
        static readonly (string cam, float chestBelow, float tagScale, float risePx)[] Cameras =
        {
            ("third person 8 u", 210f, 1f, 87f), ("third person far", 60f, 0.8f, 25f), ("top-down", 40f, 1f, 17f), ("top-down close", 28f, 1f, 12f),
        };

        [Test]
        public void NumbersNeverCoverTheNameTheBarOrTheWordRow()
        {
            Vector2 tag = new Vector2(960f, 640f);
            foreach (var c in Cameras)
            {
                Vector2 chest = tag - new Vector2(0f, c.chestBelow);
                Rect name = UnitStack.NameRect(tag, c.tagScale);
                Rect bar = UnitStack.BarRect(tag, c.tagScale, HealthBars.MaxW * HealthBars.FocusScale, 10f);
                Rect words = UnitStack.TagRect(tag, c.tagScale, 0, 520f);   // the widest pill
                Assert.IsFalse(bar.Overlaps(name), c.cam + ": bar under the name");
                Assert.IsFalse(words.Overlaps(name) || words.Overlaps(bar), c.cam + ": word row above the name");
                foreach (DamageNumbers.Kind k in System.Enum.GetValues(typeof(DamageNumbers.Kind)))
                {
                    int size = DamageNumbers.SizeFor(k, c.cam.StartsWith("third"));
                    for (int stack = 0; stack <= 2; stack++)
                        foreach (float rise in new[] { 0f, c.risePx })
                        {
                            Rect n = UnitStack.NumberRect(UnitStack.NumberLeftMid(chest, tag, c.tagScale, size, rise, stack), size, 4);
                            string at = c.cam + " " + k + " stack " + stack + " rise " + rise;
                            Assert.IsFalse(n.Overlaps(name), at + ": number over the name / class");
                            Assert.IsFalse(n.Overlaps(bar), at + ": number over the bar");
                            Assert.IsFalse(n.Overlaps(words), at + ": number over the word row");
                        }
                }
            }
        }

        [Test]
        public void NumbersNeverSitOnMyOwnFighter()
        {
            // review fix round 2 (pass-2 defect 4): close third person put the number 80 px right of the enemy, where I
            // stand. My fighter (body + name + bar) is a screen rect; the number goes on the enemy's far side from it.
            Vector2 tag = new Vector2(960f, 640f);
            foreach (var c in Cameras)
            {
                bool third = c.cam.StartsWith("third");
                Vector2 chest = tag - new Vector2(0f, c.chestBelow);
                Rect name = UnitStack.NameRect(tag, c.tagScale);
                Rect bar = UnitStack.BarRect(tag, c.tagScale, HealthBars.MaxW * HealthBars.FocusScale, 10f);
                Rect words = UnitStack.TagRect(tag, c.tagScale, 0, 520f);
                // me: in third person near the camera (big, lower on screen); top-down about the enemy's size
                float myH = third ? 520f : c.chestBelow * 2.2f;
                foreach (float dx in new[] { -220f, -120f, -60f, 0f, 60f, 80f, 120f, 220f })
                {
                    Vector2 myFeet = new Vector2(960f + dx, (third ? 90f : chest.y - c.chestBelow * 0.9f));
                    Vector2 myTag = myFeet + new Vector2(0f, myH);
                    Rect mine = UnitStack.MineRect(myFeet, myTag, 1f, myH * 0.3f);
                    foreach (DamageNumbers.Kind k in System.Enum.GetValues(typeof(DamageNumbers.Kind)))
                    {
                        int size = DamageNumbers.SizeFor(k, third);
                        for (int stack = 0; stack <= 2; stack++)
                        {
                            Rect n = UnitStack.NumberRect(UnitStack.NumberLeftMid(chest, tag, c.tagScale, size, c.risePx, stack, mine, 4), size, 4);
                            string at = c.cam + " me at dx " + dx + " " + k + " stack " + stack;
                            Assert.IsFalse(n.Overlaps(mine), at + ": number over my fighter / my name / my bar");
                            Assert.IsFalse(n.Overlaps(name) || n.Overlaps(bar) || n.Overlaps(words), at + ": number over his name, bar or words");
                        }
                    }
                }
            }
        }

        [Test]
        public void WordRowsStackUpwardWithoutOverlap()
        {
            Vector2 tag = new Vector2(400f, 300f);
            Rect r0 = UnitStack.TagRect(tag, 1f, 0, 300f), r1 = UnitStack.TagRect(tag, 1f, 1, 300f), r2 = UnitStack.TagRect(tag, 1f, 2, 300f);
            Assert.IsFalse(r0.Overlaps(r1) || r1.Overlaps(r2));
            Assert.Greater(r1.yMin, r0.yMax);
            Assert.Greater(r0.yMin, UnitStack.NameRect(tag, 1f).yMax, "the first row clears the name");
        }

        [Test]
        public void FightRuleLowHealthAndNoKaiju()
        {
            Assert.IsTrue(PxFight.ActiveAt(10f, 12.9f));
            Assert.IsFalse(PxFight.ActiveAt(10f, 13.1f), "three seconds after the last hit the fight is over");
            Vector4 fromRight = HurtEdge.SidesFor(90f);
            Assert.AreEqual(1f, fromRight.y, 1e-3f, "a hit from the right lights the right edge");
            Assert.AreEqual(HurtEdge.SideFloor, fromRight.w, 1e-3f, "and barely the left");
            Assert.Greater(HurtEdge.Heartbeat(0.08f), 0.9f);
            Assert.Less(HurtEdge.Heartbeat(0.6f), 0.05f, "a resting gap between beats");
            Assert.GreaterOrEqual(HurtEdge.WordPx, 28f, "LOW HEALTH at a readable size");
            Assert.AreNotEqual(PxPalette.Hurt, PxPalette.LowVignette, "low health is not the hurt colour");
            Assert.Less(HurtEdge.HurtSeconds, 0.3f, "the hurt flash is short");
            var c = CriticalMass.Sim.Config.GameConfig.CreateDefault();
            Assert.IsFalse(HudState.NoKaijuIn(c), "the default match has a Kaiju");
            c.HatchSeconds = c.MatchSeconds + 1;
            Assert.IsTrue(HudState.NoKaijuIn(c), "a duel / practice range (hatch past the match) shows no Kaiju panel");
        }
    }
}
