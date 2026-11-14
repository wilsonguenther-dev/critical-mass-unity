using CriticalMass.Runtime.View.Effects;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>fix/gore-proof-1: the -goreShots red measure and the scene clear a world reset needs.</summary>
    public sealed class GoreShotsTests
    {
        [Test]
        public void EveryBloodColourCountsAsBloodRedAndTheTeamCrimsonDoesNot()
        {
            Assert.IsTrue(GoreShots.BloodRed(FxScene.BloodLight));
            Assert.IsTrue(GoreShots.BloodRed(FxScene.BloodMid));
            Assert.IsTrue(GoreShots.BloodRed(FxScene.BloodDark));
            var crimson = new Color32(0xFF, 0x5A, 0x5F, 0xFF);   // Palette.Crimson
            foreach (float shade in new[] { 1f, 0.8f, 0.6f, 0.45f })
            {
                var c = new Color32((byte)(crimson.r * shade), (byte)(crimson.g * shade), (byte)(crimson.b * shade), 255);
                Assert.IsFalse(GoreShots.BloodRed(c), "team Crimson shaded x" + shade + " is not blood");
                Assert.IsTrue(GoreShots.CrimsonLike(c), "it is counted as team-crimson-like instead");
            }
            Assert.IsFalse(GoreShots.BloodRed(new Color32(24, 35, 59, 255)), "navy");
            Assert.IsTrue(GoreShots.NavyEdge(new Color32(24, 35, 59, 255)));
            Assert.IsFalse(GoreShots.BloodRed(new Color32(255, 255, 255, 255)), "the white flash");
            Assert.IsTrue(GoreShots.NearWhite(new Color32(250, 248, 246, 255)));
        }

        [Test]
        public void ShareCountsTheEdgeBandOnly()
        {
            const int w = 100, h = 50;
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(24, 35, 59, 255);
            px[25 * w + 50] = FxScene.BloodMid;                     // centre: outside a 10% band
            Assert.AreEqual(0.0, GoreShots.Share(px, w, h, GoreShots.BloodRed, 0.1f));
            Assert.AreEqual(1.0 / (w * h), GoreShots.Share(px, w, h, GoreShots.BloodRed), 1e-12);
            Assert.AreEqual(1.0, GoreShots.Share(px, w, h, GoreShots.NavyEdge, 0.1f));
        }
    }
}
