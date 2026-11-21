using System.IO;
using CriticalMass.Runtime.View.Dressing;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Config;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b15 view polish r2, Editor review round 2, item 1: the ground wedge failed (on the hill it drew under the raised
    /// paving, in contact the two bodies hid it). The warning now sits ON THE ATTACKER: a glint and streak on the weapon arm,
    /// a thin bright arc at chest height along the swing path (air layer, depth-tested), and the wedge only as a secondary
    /// cue lifted onto the walking surface. Every case PRINTS what, value, limit.
    /// </summary>
    public sealed class SwingCueTests
    {
        [Test]
        public void AShortWindUpIsAtFullStrengthFromItsFirstFrame()
        {
            float miner = SwingTelegraph.Strength(0f, 0.20f);
            Debug.Log($"[b15 swing cue] 0.20 s wind-up (Miner) first frame strength {miner:0.00} (limit 1); 0.5 s wind-up first frame {SwingTelegraph.Strength(0f, 0.5f):0.00}, end {SwingTelegraph.Strength(1f, 0.5f):0.00}");
            Assert.AreEqual(1f, miner, "no ramp on a 0.20 s wind-up");
            Assert.AreEqual(1f, SwingTelegraph.Strength(0f, SwingTelegraph.ShortWindupSeconds));
            Assert.GreaterOrEqual(SwingTelegraph.Strength(0f, 0.5f), 0.5f, "a long wind-up still starts clearly visible");
            Assert.AreEqual(1f, SwingTelegraph.Strength(1f, 0.5f));
        }

        [Test]
        public void ReducedFlashingIsASteadyCappedGlow()
        {
            for (float s = 0f; s <= 1f; s += 0.25f)
            {
                Assert.LessOrEqual(SwingTelegraph.CueAlpha(s, true), SwingTelegraph.ReducedEdgeCap + 1e-5f);
                Assert.AreEqual(1f, SwingTelegraph.GlintScale(s * 0.2f, true), "no start flash");
            }
            Assert.Greater(SwingTelegraph.CueAlpha(1f, true), 0.5f, "still visible");
            Assert.Greater(SwingTelegraph.GlintScale(0f, false), 1.3f, "a start flash in full flashing");
            Assert.AreEqual(1f, SwingTelegraph.GlintScale(SwingTelegraph.FlashSeconds, false));
            Color.RGBToHSV(ImpactPalette.SparkHot, out _, out float sat, out _);
            Assert.LessOrEqual(sat, (float)ImpactPalette.NeutralMaxSaturation, "the cue is neutral, never a team hue");
        }

        [Test]
        public void TheArcSitsOutsideEveryClassBodyWithinItsReach()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            foreach (ClassId id in ClassIds.All)
            {
                ClassConfig cc = cfg.Class(id);
                float r = SwingTelegraph.ArcRadius((float)cc.AttackRange, (float)cc.Radius);
                Debug.Log($"[b15 swing cue] {id}: arc radius {r:0.00} (limit >= body {cc.Radius:0.00} + {SwingTelegraph.ArcMinClear}), reach {cc.AttackRange:0.00}");
                Assert.GreaterOrEqual(r, (float)cc.Radius + SwingTelegraph.ArcMinClear - 1e-4f, id + ": the arc clears the attacker's own body");
            }
            Assert.That(SwingTelegraph.ArcHeight, Is.InRange(0.8f, 1.4f), "chest height");
        }

        [Test]
        public void TheWedgeIsLiftedOverTheHillPaving()
        {
            // b15 Editor review r3: the placed plaza is sunk; its highest tile is 0.145 over the ground (raycast on the
            // WorldBase Hill group) and it ends at ~5.8 u, so the lift clears 0.145 without floating far over it, and the
            // ground between the paving and the hill ring (6..12 u) gets the plain ground lift.
            const float measuredPavingTop = 0.145f;
            float plaza = SceneryLodTable.Rows["env_hill_plaza"].Height;
            float onHill = SwingTelegraph.SurfaceLift(56, 40, 56, 40, true), off = SwingTelegraph.SurfaceLift(80, 40, 56, 40, true);
            float ringGround = SwingTelegraph.SurfaceLift(56 + 8, 40, 56, 40, true);
            Debug.Log($"[b15 swing cue] wedge lift on the paving {onHill:0.000} (paving top {measuredPavingTop:0.000}), at 8 u {ringGround:0.000}, off the hill {off:0.000} (was 0.035 everywhere)");
            Assert.Greater(onHill, measuredPavingTop, "over the paving");
            Assert.Less(onHill, measuredPavingTop + 0.1f, "not floating over it");
            Assert.Less(onHill, plaza + 0.04f);
            Assert.AreEqual(off, ringGround, 1e-6f, "the ground inside the hill ring past the paving is ground");
            Assert.Greater(off, 0.035f);
            Assert.Less(off, plaza);
        }

        [Test]
        public void TheCuesAreDrawnOnTheAttackerInTheAirLayer()
        {
            string rt = Path.Combine(Application.dataPath, "_Game", "Runtime", "View");
            string fx = File.ReadAllText(Path.Combine(rt, "Effects", "FxScene.cs"));
            int a = fx.IndexOf("private void DrawSwingTelegraphs(", System.StringComparison.Ordinal);
            int b = fx.IndexOf("private double PaceNow", a, System.StringComparison.Ordinal);
            string body = fx.Substring(a, b - a);
            StringAssert.Contains("air.Ring(chest", body, "the arc at chest height in the air layer (depth-tested, over ground and paving)");
            StringAssert.Contains("SwingCue.TryArm(i", body, "the weapon-arm cue reads the posed hand");
            StringAssert.Contains("air.SoftMote(hand", body);
            StringAssert.Contains("SwingTelegraph.SurfaceLift(", body);
            StringAssert.DoesNotContain("Ground(x, z) + 0.035", body, "no wedge under the paving");
            StringAssert.Contains("SwingCue.Record(actor, mv)", File.ReadAllText(Path.Combine(rt, "ActorViews.cs")), "every posed fighter records its weapon arm");
            string combat = File.ReadAllText(Path.Combine(rt, "Effects", "CombatFx.cs"));
            StringAssert.Contains("_airMat = Mat(sh, \"Fx Air\", 0, 0, 0, 3100);", combat, "air layer: after the opaque characters, depth-tested, no depth write");
        }
    }
}
