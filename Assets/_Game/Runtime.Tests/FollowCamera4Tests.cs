using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.View;
using CriticalMass.Runtime.View.Effects;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// fix/follow-camera-4 (2026-11-14): smoke columns and fireballs as whole groups in the low views (screen cap at any
    /// distance, see-through over the fight, shorter and flatter smoke), and allies hidden at the first-person eyes.
    /// </summary>
    public sealed class FollowCamera4Tests
    {
        GameObject _camGo;

        UnityEngine.Camera LowCamera()
        {
            _camGo = new GameObject("Follow4 Test Camera");
            var cam = _camGo.AddComponent<UnityEngine.Camera>();
            cam.fieldOfView = 65f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 600f;
            cam.aspect = 16f / 9f;
            _camGo.transform.SetPositionAndRotation(new Vector3(0f, 3f, 0f), Quaternion.Euler(8f, 0f, 0f));   // looks along +z
            return cam;
        }

        [TearDown]
        public void TearDown()
        {
            ViewCull.SetFollowLine(Vector3.zero, false);
            ViewCull.SetView(null, false);
            if (_camGo != null) Object.DestroyImmediate(_camGo);
        }

        [Test]
        public void ASmokeColumnInFrontOfTheFightIsCappedAtAnyDistanceAndSeeThrough()
        {
            ViewCull.SetView(LowCamera(), true);
            ViewCull.SetFollowLine(new Vector3(0f, 1.6f, 10f), true);
            var c = new Vector3(0f, 3f, 7f);
            Assert.IsTrue(ViewCull.FxGroup(c, 6f, FxLook.Volume, out float k, out float a));
            Assert.LessOrEqual(6f * k, ViewCull.VolumeScreenTan * 7f + 1e-3f, "capped on screen though 7 u from the lens");
            Assert.AreEqual(ViewCull.VolumeOverFight, a, 1e-6f, "over the player: see-through");
        }

        [Test]
        public void SmokeBehindTheFightOrOffToTheSideOnlyGetsTheLowViewAlpha()
        {
            ViewCull.SetView(LowCamera(), true);
            ViewCull.SetFollowLine(new Vector3(0f, 1.6f, 10f), true);
            Assert.IsTrue(ViewCull.FxGroup(new Vector3(0f, 3f, 30f), 3f, FxLook.Volume, out _, out float behind));
            Assert.AreEqual(ViewCull.VolumeAlpha, behind, 1e-6f, "behind the player");
            Assert.IsTrue(ViewCull.FxGroup(new Vector3(7f, 2.5f, 10f), 1f, FxLook.Volume, out _, out float side));
            Assert.AreEqual(ViewCull.VolumeAlpha, side, 1e-6f, "well to the side");
        }

        [Test]
        public void AFlashIsCappedAndSeeThroughOnlyOverTheFight()
        {
            ViewCull.SetView(LowCamera(), true);
            ViewCull.SetFollowLine(new Vector3(0f, 1.6f, 10f), true);
            Assert.IsTrue(ViewCull.FxGroup(new Vector3(0f, 2.5f, 5f), 3f, FxLook.Flash, out float k, out float a));
            Assert.LessOrEqual(3f * k, ViewCull.FlashScreenTan * 5.03f + 1e-3f);
            Assert.AreEqual(ViewCull.VolumeOverFight, a, 1e-6f);
            Assert.IsTrue(ViewCull.FxGroup(new Vector3(7f, 2.5f, 10f), 0.5f, FxLook.Flash, out _, out float side));
            Assert.AreEqual(1f, side, 1e-6f, "a flash away from the fight keeps full colour");
            Assert.IsFalse(ViewCull.FxGroup(new Vector3(0f, 3f, 1.2f), 0.6f, FxLook.Flash, out _, out _), "at the lens: not drawn");
        }

        [Test]
        public void FirstPersonUsesTheMiddleOfTheViewAsTheFight()
        {
            ViewCull.SetView(LowCamera(), true);
            Assert.IsTrue(ViewCull.FxGroup(new Vector3(0f, 2.3f, 5f), 2f, FxLook.Volume, out _, out float a));
            Assert.AreEqual(ViewCull.VolumeOverFight, a, 1e-6f);
        }

        [Test]
        public void TopDownIsUntouchedAndAGroupAtTheLensIsSkipped()
        {
            Assert.IsTrue(ViewCull.FxGroup(new Vector3(0f, 3f, 0.5f), 9f, FxLook.Volume, out float k, out float a));
            Assert.AreEqual(1f, k);
            Assert.AreEqual(1f, a);
            ViewCull.SetView(LowCamera(), true);
            var air = new FxMeshBuilder("Follow4 Air", 64, 192) { ViewCulled = true, NearFade = true };
            var plain = new FxMeshBuilder("Follow4 Plain", 64, 192);
            try
            {
                Assert.IsFalse(air.BeginGroup(new Vector3(0f, 3f, 1.2f), 0.6f, FxLook.Flash, new Vector3(0f, 0f, 1.2f)));
                Assert.IsTrue(air.BeginGroup(new Vector3(0f, 2f, 12f), 2f, FxLook.Volume, new Vector3(0f, 0f, 12f)));
                air.Quad(new Vector3(0f, 2f, 12f), Vector3.right * 0.5f, Vector3.up * 0.5f, Color.white, Color.white);
                air.EndGroup();
                Assert.AreEqual(4, air.VertCount, "a drawn group builds its pieces");
                Assert.IsTrue(plain.BeginGroup(new Vector3(0f, 3f, 1.2f), 0.6f, FxLook.Flash, Vector3.zero), "unculled builders never skip");
                plain.EndGroup();
            }
            finally
            {
                Object.DestroyImmediate(air.Mesh);
                Object.DestroyImmediate(plain.Mesh);
            }
        }

        [Test]
        public void LowViewSmokeIsShorterAndFlatterAndAlliesClearTheEyes()
        {
            Assert.Less(FxScene.LowSmokeLife, 1.0);
            Assert.Less(FxScene.LowSmokeRise, 1f);
            Assert.GreaterOrEqual(ActorViews.FirstAllyMetres, 2f, "about 2 u around the first-person eyes");
            Assert.Greater(ActorViews.FirstAllyMetres, ActorViews.NearLensMetres);
        }
    }
}
