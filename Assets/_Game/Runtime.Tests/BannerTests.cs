using CriticalMass.Runtime.Hud;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>The banner's timeline and queue (DG-17; DG-37 reuses it).</summary>
    [TestFixture]
    public sealed class BannerTests
    {
        static BannerSpec Spec(string h, int priority = 0, double hold = 2) =>
            new BannerSpec { Headline = h, Reason = "r", Priority = priority, HoldSeconds = hold, Style = BannerStyle.Neutral };

        [Test]
        public void TransitionsStayUnderAQuarterSecond()
        {
            Assert.Less(BannerQueue.InSeconds, 0.25);
            Assert.Less(BannerQueue.OutSeconds, 0.25);
        }

        [Test]
        public void ABannerFadesInHoldsAndLeaves()
        {
            var q = new BannerQueue();
            q.Show(Spec("A"), 10);
            Assert.IsTrue(q.HasCurrent);
            Assert.AreEqual(0f, q.Alpha(10), 1e-6);
            Assert.AreEqual(1f, q.Alpha(10 + BannerQueue.InSeconds), 1e-6);
            q.Motion(10, out float s0, out _);
            Assert.Greater(s0, 1f);
            q.Update(11);
            Assert.AreEqual(1f, q.Alpha(11), 1e-6);
            double end = 10 + BannerQueue.InSeconds + 2 + BannerQueue.OutSeconds;
            q.Update(end - 0.01);
            Assert.IsTrue(q.HasCurrent);
            q.Update(end + 0.01);
            Assert.IsFalse(q.HasCurrent);
        }

        [Test]
        public void EqualPriorityWaitsHigherReplaces()
        {
            var q = new BannerQueue();
            q.Show(Spec("A", 1), 0);
            q.Show(Spec("B", 1), 0.1);
            Assert.AreEqual("A", q.Current.Headline);
            Assert.AreEqual(1, q.WaitingCount);
            q.Show(Spec("C", 5), 0.2);
            Assert.AreEqual("C", q.Current.Headline);
            q.Update(0.2 + BannerQueue.InSeconds + 2 + BannerQueue.OutSeconds + 0.01);
            Assert.AreEqual("B", q.Current.Headline);
        }

        [Test]
        public void HideStartsTheExitAndTheQueueHasACap()
        {
            var q = new BannerQueue();
            q.Show(Spec("A"), 0);
            q.Hide(0.5);
            q.Update(0.5 + BannerQueue.OutSeconds + 0.01);
            Assert.IsFalse(q.HasCurrent);
            q.Show(Spec("X"), 1);
            for (int i = 0; i < BannerQueue.Capacity + 3; i++) q.Show(Spec("W" + i), 1);
            Assert.AreEqual(BannerQueue.Capacity, q.WaitingCount);
        }

        [Test]
        public void AClockThatRunsBackwardsClearsEverything()
        {
            var q = new BannerQueue();
            q.Show(Spec("A"), 100);
            q.Show(Spec("B"), 100);
            q.Update(0.5); // a new match restarted sim time
            Assert.IsFalse(q.HasCurrent);
            Assert.AreEqual(0, q.WaitingCount);
        }
    }
}
