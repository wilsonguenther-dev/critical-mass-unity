using System.Text.RegularExpressions;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Moments;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>fix/model-scale: the one owner of model scale composes from the stored base, clamps, logs once.</summary>
    public sealed class ModelScaleTests
    {
        [SetUp]
        public void SetUp() => ModelScale.ResetForTests();

        [Test]
        public void ComposesFromTheBaseNeverFromTheCurrentScale()
        {
            Vector3 b = new Vector3(1f, 1f, 1f);
            Vector3 punch = new Vector3(1.2f, 1.2f, 1.2f);
            // The same factor applied twice (two overlapping effects in one frame, or a tween interrupted and restarted)
            // gives the same answer: nothing reads the scale the last frame wrote.
            Vector3 first = ModelScale.Compose(b, punch, "test");
            Vector3 second = ModelScale.Compose(b, punch, "test");
            Assert.AreEqual(first, second);
            Assert.AreEqual(1.2f, first.y, 1e-6f);
            Assert.AreEqual(b, ModelScale.Compose(b, Vector3.one, "test"), "factor 1 is exactly the base");
            Assert.AreEqual(new Vector3(2f, 2f, 2f), ModelScale.Compose(new Vector3(2f, 2f, 2f), Vector3.one, "test"), "a non-unit base is kept");
            Assert.AreEqual(0, ModelScale.ClampCount);
        }

        [Test]
        public void ClampsAWildFactorAndLogsOnce()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[ModelScale\].*clamped"));
            Vector3 s = ModelScale.Compose(Vector3.one, new Vector3(2.7f, 2.7f, 2.7f), "Actor 3 warrior");
            Assert.AreEqual(ModelScale.MaxFactor, s.y, 1e-6f);
            s = ModelScale.Compose(Vector3.one, new Vector3(0.01f, float.NaN, 3f), "Actor 3 warrior");   // second clamp: no second log
            Assert.AreEqual(ModelScale.MinFactor, s.x, 1e-6f);
            Assert.AreEqual(1f, s.y, 1e-6f);
            Assert.AreEqual(ModelScale.MaxFactor, s.z, 1e-6f);
            Assert.AreEqual(2, ModelScale.ClampCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void BoneScaleIsSetFromRest()
        {
            Vector3 rest = new Vector3(1f, 1f, 1f);
            Vector3 v = rest;
            for (int f = 0; f < 600; f++) v = ModelScale.FromRest(rest, 1.016f);   // a frozen breath, 10 s of frames
            Assert.AreEqual(1.016f, v.y, 1e-6f, "the breath compounded");
        }

        [Test]
        public void TakeoverPortraitBackdropIsOpaque()
        {
            Assert.AreEqual(1f, ModelPortrait.BackdropFor(CMTokens.Azure.Color).a, 1e-6f);
            Assert.AreEqual(1f, ModelPortrait.BackdropFor(new Color(1f, 0f, 0f, 0f)).a, 1e-6f);
        }
    }
}
