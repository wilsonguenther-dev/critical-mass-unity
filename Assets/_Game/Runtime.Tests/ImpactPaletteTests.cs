using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Config;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>b15 safe defaults, item 2: hit and gore colours never read as a team colour (one line per colour).</summary>
    public sealed class ImpactPaletteTests
    {
        static string Hex(Color32 c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        [Test]
        public void TheTeamHuesHereMatchTheTokens()
        {
            bool cb = CMTokens.ColourBlindTeams;
            try
            {
                CMTokens.ColourBlindTeams = false;
                Assert.AreEqual(Hex(ImpactPalette.TeamHues[0]), Hex(CMTokens.Team(TeamId.Azure)));
                Assert.AreEqual(Hex(ImpactPalette.TeamHues[1]), Hex(CMTokens.Team(TeamId.Crimson)));
                CMTokens.ColourBlindTeams = true;
                Assert.AreEqual(Hex(ImpactPalette.TeamHues[2]), Hex(CMTokens.Team(TeamId.Azure)));
                Assert.AreEqual(Hex(ImpactPalette.TeamHues[3]), Hex(CMTokens.Team(TeamId.Crimson)));
            }
            finally { CMTokens.ColourBlindTeams = cb; }
        }

        [Test]
        public void NeutralImpactColoursCarryNoTeamHue()
        {
            foreach (Color32 c in ImpactPalette.Neutral)
            {
                GoreShots.Hsv(c, out double h, out double s, out double v);
                bool ok = s < ImpactPalette.NeutralMaxSaturation && !GoreShots.BloodRed(c) && !GoreShots.CrimsonLike(c);
                TestContext.WriteLine("neutral " + Hex(c) + ": hue=" + h.ToString("0") + " sat=" + s.ToString("0.00") + " (limit < " +
                                      ImpactPalette.NeutralMaxSaturation + ") -> " + (ok ? "PASS" : "FAIL"));
                Assert.IsTrue(ok, Hex(c) + " is not neutral");
            }
        }

        [Test]
        public void FullGoreColoursStayClearOfEveryTeamHue()
        {
            foreach (Color32 c in ImpactPalette.FullGore)
            {
                double gap = ImpactPalette.NearestTeamHueGap(c);
                bool ok = gap >= ImpactPalette.MinTeamHueGapDeg && !GoreShots.BloodRed(c) && !GoreShots.CrimsonLike(c);
                TestContext.WriteLine("full gore " + Hex(c) + ": nearest team hue gap=" + gap.ToString("0.0") + " deg (limit >= " +
                                      ImpactPalette.MinTeamHueGapDeg + "), bloodRed=" + GoreShots.BloodRed(c) + " crimsonLike=" +
                                      GoreShots.CrimsonLike(c) + " -> " + (ok ? "PASS" : "FAIL"));
                Assert.IsTrue(ok, Hex(c) + " reads as a team colour");
            }
            // The old FxScene blood red (150,18,34) fails the same rule: the measure really catches a crimson-like red. Since the
            // wave-two combine FxScene's blood IS ImpactPalette's gore (feat/b15-combat-feel took the routed patch).
            var oldRed = new Color32(150, 18, 34, 255);
            double old = ImpactPalette.NearestTeamHueGap(oldRed);
            TestContext.WriteLine("old FxScene.BloodMid " + Hex(oldRed) + ": nearest team hue gap=" + old.ToString("0.0") +
                                  " deg; FxScene.BloodMid now " + Hex(FxScene.BloodMid) + " (ImpactPalette.GoreMid " + Hex(ImpactPalette.GoreMid) + ")");
            Assert.Less(old, ImpactPalette.MinTeamHueGapDeg, "the measure really catches a crimson-like red");
            Assert.AreEqual(Hex(ImpactPalette.GoreMid), Hex(FxScene.BloodMid), "FxScene blood is ImpactPalette's gore");
            Assert.AreEqual(Hex(ImpactPalette.SparkMid), Hex(FxScene.SparkMid), "FxScene hit sparks are ImpactPalette's neutral");
        }
    }
}
