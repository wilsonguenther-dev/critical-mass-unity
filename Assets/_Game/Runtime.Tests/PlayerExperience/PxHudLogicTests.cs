using CriticalMass.Runtime.Hud.Player;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests.PlayerExperience
{
    /// <summary>Player-experience lane D: the pure rules of the player HUD (bars, low health, assists, flashes, hill, sizes).</summary>
    [TestFixture]
    public sealed class PxHudLogicTests
    {
        [Test]
        public void HealthBarsHoldTheirSizeFloorsAndReadAtTheFightDistance()
        {
            // third-person fight distance (about 8 u from the camera): at least 44 x 6 px at 1080p
            Assert.GreaterOrEqual(HealthBars.WidthFor(8f), 44f);
            Assert.GreaterOrEqual(HealthBars.HeightFor(HealthBars.WidthFor(8f)), 6f);
            // far away and in the top-down view: never under 30 x 5
            foreach (float d in new[] { 12f, 20f, 35f, 80f, 500f })
            {
                Assert.GreaterOrEqual(HealthBars.WidthFor(d), 30f, "width floor at " + d);
                Assert.GreaterOrEqual(HealthBars.HeightFor(HealthBars.WidthFor(d)), 5f, "height floor at " + d);
            }
            // up close it stops growing
            Assert.LessOrEqual(HealthBars.WidthFor(0.5f), HealthBars.MaxW);
            Assert.Greater(HealthBars.FocusScale, 1f, "the target's bar is larger");
        }

        [Test]
        public void AHitLeavesAWhiteChunkThatHoldsThenDrains()
        {
            float shown = 1f, ghost = 1f, hold = 0f;
            Assert.AreEqual(1, HealthBars.Step(ref shown, ref ghost, ref hold, 0.8f, 1f / 60f), "a drop is reported");
            Assert.AreEqual(0.8f, shown, 1e-4f, "the fill drops at once");
            Assert.AreEqual(1f, ghost, 1e-4f, "the chunk shows the lost 20%");
            for (int i = 0; i < 12; i++) HealthBars.Step(ref shown, ref ghost, ref hold, 0.8f, 1f / 60f);   // 0.2 s
            Assert.AreEqual(1f, ghost, 1e-4f, "the chunk holds for 0.25 s");
            for (int i = 0; i < 60; i++) HealthBars.Step(ref shown, ref ghost, ref hold, 0.8f, 1f / 60f);   // +1 s
            Assert.AreEqual(0.8f, ghost, 1e-3f, "then drains to the fill");
            Assert.AreEqual(-1, HealthBars.Step(ref shown, ref ghost, ref hold, 1f, 1f / 60f), "a heal starts a rise");
            Assert.Greater(shown, 0.8f);
            Assert.Less(shown, 1f, "healing climbs, it does not snap");
        }

        [Test]
        public void LowHealthHasHysteresis()
        {
            Assert.IsFalse(HurtEdge.LowState(false, 0.31f, true));
            Assert.IsTrue(HurtEdge.LowState(false, 0.29f, true), "on below 30%");
            Assert.IsTrue(HurtEdge.LowState(true, 0.33f, true), "stays on between 30 and 35%");
            Assert.IsFalse(HurtEdge.LowState(true, 0.35f, true), "off when healed to 35%");
            Assert.IsFalse(HurtEdge.LowState(true, 0.1f, false), "off when dead");
            Assert.Less(HurtEdge.LowHz, 2.0f, "the pulse stays under 2 Hz");
            Assert.Less(HurtEdge.HurtPeak(0.2f, true), HurtEdge.HurtPeak(0.2f, false), "reduce flashing is calmer");
            Assert.Greater(HurtEdge.HurtPeak(0.2f, true), 0.15f, "and never nothing");
        }

        [Test]
        public void AssistsCountInsideTheWindowOnly()
        {
            var t = new AssistTracker();
            t.OnHit(5, 0, 10.0);
            t.OnHit(5, 3, 11.0);
            Assert.IsTrue(t.OnDeath(5, 3, 17.9, 0, -1), "I hit him 7.9 s before someone else's knock-out: assist");
            Assert.AreEqual(1, t.Assists(0));
            Assert.AreEqual(0, t.Assists(3), "the killer gets no assist");
            Assert.IsFalse(t.DamagedRecently(5, 0, 18.0), "the victim's row clears on the knock-out");

            t.OnHit(6, 0, 10.0);
            Assert.IsFalse(t.OnDeath(6, 4, 18.1, 0, -1), "8.1 s is outside the 8 s window");
            Assert.AreEqual(1, t.Assists(0));

            t.OnHit(0, 7, 20.0);
            t.OnHit(0, 8, 21.0);
            t.OnDeath(0, 8, 22.0, 0, 0);
            Assert.AreEqual(1, t.RecapCount, "my death recap lists the other attacker");
            Assert.AreEqual(7, t.RecapAt(0));
        }

        [Test]
        public void TheFlashBudgetAllowsThreeASecond()
        {
            PxFlashBudget.Reset();
            Assert.IsTrue(PxFlashBudget.TryOnset(10.0f));
            Assert.IsTrue(PxFlashBudget.TryOnset(10.2f));
            Assert.IsTrue(PxFlashBudget.TryOnset(10.4f));
            Assert.IsFalse(PxFlashBudget.TryOnset(10.6f), "a fourth flash inside one second is refused");
            Assert.IsTrue(PxFlashBudget.TryOnset(11.05f), "a second later there is room again");
            PxFlashBudget.Reset();
        }

        [Test]
        public void MyBodyHoldsTheHillOnlyWhenRemovingItFlipsOwnership()
        {
            Assert.IsTrue(WorldPops.IHoldTheHill(3, 1, 2, 0, true), "3 v 2: without me 2 v 2, the hill is no longer ours");
            Assert.IsFalse(WorldPops.IHoldTheHill(4, 1, 2, 0, true), "4 v 2: we keep it without me");
            Assert.IsTrue(WorldPops.IHoldTheHill(4, 2, 2, 0, true), "a Warrior counts 2");
            Assert.IsFalse(WorldPops.IHoldTheHill(12, 1, 9, 10, true), "capped at 10 v 9: 11 without me is still 10");
            Assert.IsFalse(WorldPops.IHoldTheHill(3, 1, 2, 0, false), "not ours, nothing to hold");
        }

        [Test]
        public void DamageNumbersReadInBothCameras()
        {
            foreach (DamageNumbers.Kind k in System.Enum.GetValues(typeof(DamageNumbers.Kind)))
            {
                Assert.GreaterOrEqual(DamageNumbers.SizeFor(k, true), 28, k + " third person");
                Assert.GreaterOrEqual(DamageNumbers.SizeFor(k, false), 28, k + " top-down");
            }
            Assert.Greater(DamageNumbers.SizeFor(DamageNumbers.Kind.Heavy, true), DamageNumbers.SizeFor(DamageNumbers.Kind.Hit, true));
            Assert.Greater(DamageNumbers.SizeFor(DamageNumbers.Kind.Crit, true), DamageNumbers.SizeFor(DamageNumbers.Kind.Heavy, true));
        }

        [Test]
        public void HurtAndDamageColoursSitOutsideBothTeamHues()
        {
            float[] teams = { Hue("#16C7D9"), Hue("#FF5A5F"), Hue("#3D9BFF"), Hue("#FFB000") };
            foreach (Color c in new[] { PxPalette.Strike, PxPalette.Hurt, PxPalette.CritSkill })
            {
                Color.RGBToHSV(c, out float h, out _, out _);
                foreach (float t in teams) Assert.Greater(HueGap(h * 360f, t), 25f, "damage hue " + h * 360f + " vs team hue " + t);
            }
        }

        [Test]
        public void StreakWordsAndDebugArgument()
        {
            Assert.AreEqual(string.Empty, KillConfirm.StreakWord(1));
            Assert.AreEqual("DOUBLE", KillConfirm.StreakWord(2));
            Assert.AreEqual("TRIPLE", KillConfirm.StreakWord(3));
            Assert.AreEqual("RAMPAGE", KillConfirm.StreakWord(9));
            Assert.IsTrue(DebugGate.HasArg(new[] { "app", "-DEBUGHUD" }));
            Assert.IsFalse(DebugGate.HasArg(new[] { "app", "-batchmode" }));
            Assert.AreEqual("KO 3  AST 1  DMG 412  HEAL 0  HILL 34s", PlayerTally.Compose(3, 1, 412, 0, 34));
        }

        static float Hue(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color c);
            Color.RGBToHSV(c, out float h, out _, out _);
            return h * 360f;
        }

        static float HueGap(float a, float b)
        {
            float d = Mathf.Abs(a - b) % 360f;
            return d > 180f ? 360f - d : d;
        }
    }
}
