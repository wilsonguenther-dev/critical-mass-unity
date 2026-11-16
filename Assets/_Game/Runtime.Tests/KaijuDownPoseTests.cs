using CriticalMass.Runtime.View.Anim;
using CriticalMass.Sim.Config;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b11-eh: the knocked-out Kaiju reads as down (on its belly, chin on the ground) for the whole knock-out and stands
    /// again after it recovers. The pose numbers live in KaijuDownPose; these tests hold the weight curve, the root pitch,
    /// the chin solve and the stand-again.
    /// </summary>
    [TestFixture]
    public sealed class KaijuDownPoseTests
    {
        const float H = 5f;   // the adult's height (ArtMapping Kaiju_Adult Height)

        static double Ko => GameConfig.CreateDefault().Kaiju.KoSeconds;

        [Test]
        public void DuringTheKnockOutTheRootIsPitchedIntoTheDownBand()
        {
            double ko = Ko;
            Assert.AreEqual(0f, KaijuDownPose.Weight(0, ko, double.PositiveInfinity, KaijuAnimator.GetUpSeconds), "standing before the KO");
            Assert.Less(KaijuDownPose.Weight(ko - 0.05, ko, double.PositiveInfinity, KaijuAnimator.GetUpSeconds), 0.05f, "it falls, it does not snap");
            double settled = KaijuDownPose.FallDelay + KaijuDownPose.FallSeconds;
            for (double since = settled; since < ko; since += 0.5)
            {
                float w = KaijuDownPose.Weight(ko - since, ko, double.PositiveInfinity, KaijuAnimator.GetUpSeconds);
                Assert.AreEqual(1f, w, 1e-5f, "fully down " + since + " s into the KO");
                float pitch = KaijuDownPose.RootPitchDeg * w;
                Assert.That(pitch, Is.InRange(KaijuDownPose.RootPitchMinDeg, KaijuDownPose.RootPitchMaxDeg), "root pitch in the down band");
            }
            Assert.That(KaijuDownPose.BackShare * H, Is.InRange(0.5f, 2.0f), "the lying body is pulled back over its sim position");
            Assert.That(KaijuDownPose.BellyRestShare * H, Is.InRange(0.2f, 1.0f), "the torso rests on the ground, not in it or above it");
        }

        [TestCase(1.2f, 1.0f)]   // head held up and forward of the neck
        [TestCase(-0.8f, 1.1f)]  // head dug in below the target
        [TestCase(0.6f, -1.0f)]  // a head pointing back along the body
        public void TheNeckSolvePutsTheChinOnTheGround(float headRise, float headAhead)
        {
            Vector3 fwd = Vector3.forward, side = Vector3.right;
            float ground = 2f;
            Vector3 neck = new Vector3(0f, ground + 0.9f, 1.5f);
            Vector3 head = neck + new Vector3(0f, headRise, headAhead);
            float target = ground + KaijuDownPose.HeadRestShare * H;
            float deg = KaijuDownPose.NeckCorrectionDeg(neck, head, fwd, target);
            Vector3 moved = neck + Quaternion.AngleAxis(deg, side) * (head - neck);
            Assert.IsTrue(KaijuDownPose.InBand(KaijuDownPose.RootPitchDeg, moved.y - ground, H),
                "chin height " + (moved.y - ground) + " within the down band of " + KaijuDownPose.HeadRestShare * H);
        }

        [Test]
        public void AfterRecoveredItIsBackToStand()
        {
            double ko = Ko, up = KaijuAnimator.GetUpSeconds;
            Assert.AreEqual(1f, KaijuDownPose.Weight(0, ko, 0.0, up), 1e-5f, "the get-up starts from down");
            float mid = KaijuDownPose.Weight(0, ko, up * 0.5, up);
            Assert.That(mid, Is.InRange(0.05f, 0.95f), "the get-up rises, it does not snap");
            Assert.AreEqual(0f, KaijuDownPose.Weight(0, ko, up, up), "stood up once the get-up is over");
            Assert.AreEqual(0f, KaijuDownPose.Weight(0, ko, up + 30.0, up), "and stays up");
            Assert.AreEqual(0f, KaijuDownPose.Weight(0, ko, double.PositiveInfinity, up), "never knocked out: standing");
            Assert.IsFalse(KaijuDownPose.InBand(0f, H * 0.6f, H), "a standing Kaiju is not in the down band");
        }

        [Test]
        public void SplayTurnsALimbOutwardAndLeavesNoneOnTheInside()
        {
            var root = new GameObject("kaiju-down-test");
            try
            {
                var thigh = new GameObject("Thigh_L").transform;
                thigh.SetParent(root.transform, false);
                thigh.position = new Vector3(-0.5f, 1f, 0f);
                var shin = new GameObject("Shin_L").transform;
                shin.SetParent(thigh, true);
                shin.position = new Vector3(-0.5f, 1f, -1.5f);   // the leg lies back along the ground
                KaijuDownPose.Splay(thigh, shin, Vector3.up, Vector3.right, KaijuDownPose.ThighSplayDeg);
                Assert.Less(shin.position.x, -0.5f - 0.3f, "the left leg swings out to the left");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
