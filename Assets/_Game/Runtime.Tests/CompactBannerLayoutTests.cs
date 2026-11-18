using CriticalMass.Runtime.View.Moments;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b14 polish, ability / hero banner: by default every moment is the compact side toast (<= 9 % of the screen, <= 1.6 s,
    /// queued), at 1280x720, 1920x1080 and 2560x1080 it stays on screen, clear of the player at the screen centre, and it
    /// docks to the other side when the hill centre would sit under it. Each case prints one line.
    /// </summary>
    public sealed class CompactBannerLayoutTests
    {
        static readonly Vector2[] Screens = { new Vector2(1280, 720), new Vector2(1920, 1080), new Vector2(2560, 1080) };

        [SetUp]
        public void SetUp() => BigMomentsSettings.ModeOverride = null;

        [TearDown]
        public void TearDown() => BigMomentsSettings.ModeOverride = null;

        [Test]
        public void ToastIsSmallOnScreenAndClearOfThePlayer()
        {
            foreach (Vector2 s in Screens)
            {
                Rect r = BigMoments.StingerScreenRect(s.x, s.y, false);
                float share = r.width * r.height / (s.x * s.y);
                Vector2 player = new Vector2(s.x * 0.5f, s.y * 0.58f);
                bool clear = !BigMoments.StingerCovers(r, player, BigMoments.StingerClearPx);
                Debug.Log("compact banner " + s.x + "x" + s.y + ": rect " + r + ", area share " + share.ToString("0.000") +
                          " (limit <= 0.090), on screen " + (r.xMin >= 0 && r.xMax <= s.x && r.yMin >= 0 && r.yMax <= s.y) + ", clear of the player " + clear);
                Assert.That(share, Is.LessThanOrEqualTo(0.09f));
                Assert.That(r.xMin, Is.GreaterThanOrEqualTo(0f));
                Assert.That(r.xMax, Is.LessThanOrEqualTo(s.x + 0.5f));
                Assert.That(r.yMax, Is.LessThanOrEqualTo(s.y * 0.5f), "the toast stays in the upper half");
                Assert.IsTrue(clear, "toast over the player at " + s);
                Rect l = BigMoments.StingerScreenRect(s.x, s.y, true);
                Assert.IsFalse(BigMoments.StingerCovers(l, player, BigMoments.StingerClearPx), "left toast over the player at " + s);
            }
        }

        [Test]
        public void ToastDocksLeftWhenTheHillCentreIsUnderIt()
        {
            foreach (Vector2 s in Screens)
            {
                Rect r = BigMoments.StingerScreenRect(s.x, s.y, false);
                Vector2 nan = new Vector2(float.NaN, float.NaN);
                Vector2 hillUnder = r.center;
                bool left = BigMoments.StingerDockLeft(s.x, s.y, nan, hillUnder);
                bool stay = BigMoments.StingerDockLeft(s.x, s.y, nan, new Vector2(s.x * 0.5f, s.y * 0.5f));
                Debug.Log("compact banner " + s.x + "x" + s.y + ": hill under the right toast -> dock left " + left + "; hill at centre -> dock left " + stay + " (limit: true, false)");
                Assert.IsTrue(left);
                Assert.IsFalse(stay);
                Assert.IsFalse(BigMoments.StingerCovers(BigMoments.StingerScreenRect(s.x, s.y, true), hillUnder, BigMoments.StingerClearPx));
            }
        }

        [Test]
        public void DefaultModePlaysEveryMomentAsAQueuedToastOfAtMostOnePointSixSeconds()
        {
            Assert.AreEqual(BigMomentsMode.Reduced, (BigMomentsMode)BigMomentsSettings.DefaultMode);
            var q = new MomentQueue();
            MomentId[] ids = { MomentId.BatteringRam, MomentId.Overcharged, MomentId.Wildfire };
            double now = 100;
            int queued = 0;
            foreach (MomentId id in ids)
            {
                var req = new MomentRequest { Id = id, Team = TeamId.Crimson, Cls = ClassId.Warrior, PlayerOwned = true };
                OfferResult res = q.Offer(req, now, (BigMomentsMode)BigMomentsSettings.DefaultMode, false);
                Debug.Log("compact banner offer " + id + ": " + res + " (limit QueuedAsStinger)");
                Assert.AreEqual(OfferResult.QueuedAsStinger, res, id.ToString());
                queued++;
            }
            Debug.Log("compact banner: stinger seconds " + MomentQueue.StingerSeconds + " (limit <= 1.6), queued " + queued);
            Assert.That(MomentQueue.StingerSeconds, Is.LessThanOrEqualTo(1.6f));
            Assert.That(BigMoments.StingerTitleMin, Is.LessThanOrEqualTo(32));
        }
    }
}
