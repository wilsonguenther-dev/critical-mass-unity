using System;
using CriticalMass.Runtime.Camera;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>Port of src/game/rig.test.ts (09e0904): the CM-27 (1) critically damped camera spring.</summary>
    [TestFixture]
    public sealed class CameraSpringTests
    {
        private const double Omega = 9;

        [Test]
        public void SpringFromRestTowardAStepTargetItNeverOvershootsAtDt16001And045()
        {
            foreach (double dt in new[] { 1.0 / 60, 0.1, 0.45 })
            {
                Spring1 s = CameraSpring.CreateSpring(0);
                double prev = 0;
                for (int i = 0; i < 400; i++)
                {
                    CameraSpring.SpringStep(s, 10, Omega, dt);
                    Assert.LessOrEqual(s.X, 10, $"dt {dt} step {i}");
                    Assert.GreaterOrEqual(s.X, prev, $"dt {dt} step {i} monotonic");
                    prev = s.X;
                }
                Assert.AreEqual(10, s.X, 0.5 * Math.Pow(10, -6));
            }
        }

        [Test]
        public void SpringTheSameTimeSpanLandsOnTheSameStateWhateverTheFrameSplitExactSolutionFrameRateIndependent()
        {
            Spring1 a = CameraSpring.CreateSpring(0);
            Spring1 b = CameraSpring.CreateSpring(0);
            for (int i = 0; i < 60; i++) CameraSpring.SpringStep(a, 5, Omega, 1.0 / 60);
            for (int i = 0; i < 6; i++) CameraSpring.SpringStep(b, 5, Omega, 1.0 / 6);
            Assert.AreEqual(b.X, a.X, 0.5 * Math.Pow(10, -9));
            Assert.AreEqual(b.V, a.V, 0.5 * Math.Pow(10, -9));
        }

        [Test]
        public void SpringATargetMovingAtConstantSpeedVIsTrackedWithASteadyLagOf2vOmega()
        {
            Spring1 s = CameraSpring.CreateSpring(0);
            const double v = 3.8;
            const double dt = 1.0 / 60;
            double t = 0;
            for (int i = 0; i < 240; i++)
            {
                t += dt;
                CameraSpring.SpringStep(s, v * t, Omega, dt);
            }
            // Driven by samples of the target at the END of each step, the discrete lag sits within v * dt of 2v / omega.
            Assert.Less(Math.Abs(v * t - s.X - (2 * v) / Omega), v * dt);
            Assert.Less(Math.Abs(s.V - v), 0.02 * v);
        }

        [Test]
        public void SpringStableForAHugeFrameDt5SLandsOnTheTargetNoBlowUp()
        {
            Spring1 s = CameraSpring.CreateSpring(-3);
            CameraSpring.SpringStep(s, 4, Omega, 5);
            Assert.Less(Math.Abs(s.X - 4), 1e-12);
            Assert.Less(Math.Abs(s.V), 1e-12);
        }
    }
}
