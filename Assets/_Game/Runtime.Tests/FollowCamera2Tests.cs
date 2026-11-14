using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.View;
using CriticalMass.Runtime.View.Effects;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// feat/follow-camera-2 (2026-11-14): culling of other systems' cosmetic effects in the low views, the toon shader's
    /// see-through dither maths, name tag and health sizing by distance, the strafe hold, and the old ground sky stepping
    /// aside for the world horizon.
    /// </summary>
    public sealed class FollowCamera2Tests
    {
        GameObject _camGo;

        UnityEngine.Camera LowCamera()
        {
            _camGo = new GameObject("Follow2 Test Camera");
            var cam = _camGo.AddComponent<UnityEngine.Camera>();
            cam.fieldOfView = 65f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 600f;
            cam.aspect = 16f / 9f;
            _camGo.transform.SetPositionAndRotation(new Vector3(0f, 3f, 0f), Quaternion.Euler(14f, 0f, 0f));   // looks along +z
            return cam;
        }

        [TearDown]
        public void TearDown()
        {
            ViewCull.SetView(null, false);
            if (_camGo != null) Object.DestroyImmediate(_camGo);
        }

        [Test]
        public void EffectsOutOfViewOrPastTheDrawDistanceAreCulledAndCounted()
        {
            ViewCull.SetView(LowCamera(), true);
            int culled0 = ViewCull.FxCulled, drawn0 = ViewCull.FxDrawn;
            Assert.IsTrue(ViewCull.FxVisible(new Vector3(0f, 1f, 10f), 0.5f), "ahead and near: drawn");
            Assert.IsFalse(ViewCull.FxVisible(new Vector3(0f, 1f, -12f), 0.5f), "behind the camera: culled");
            Assert.IsFalse(ViewCull.FxVisible(new Vector3(0f, 1f, ViewCull.FxDrawDistance + 10f), 0.5f), "past the draw distance: culled");
            Assert.IsTrue(ViewCull.FxVisible(new Vector3(0f, 1f, -0.5f), 1f), "just behind the lens but within the margin: kept (no popping)");
            Assert.AreEqual(2, ViewCull.FxCulled - culled0);
            Assert.AreEqual(2, ViewCull.FxDrawn - drawn0);
            ViewCull.SetView(null, false);
            Assert.IsTrue(ViewCull.FxVisible(new Vector3(0f, 1f, -500f), 0.5f), "top-down (culling off): everything draws");
        }

        [Test]
        public void ACulledBuilderSkipsThePrimitiveAndAnUnculledBuilderNeverDoes()
        {
            ViewCull.SetView(LowCamera(), true);
            var fx = new FxMeshBuilder("Follow2 Fx", 64, 192) { ViewCulled = true };
            var paint = new FxMeshBuilder("Follow2 Paint", 64, 192);
            try
            {
                fx.Quad(new Vector3(0f, 1f, -20f), Vector3.right * 0.2f, Vector3.up * 0.2f, Color.white, Color.white);
                Assert.AreEqual(0, fx.VertCount, "gore drop behind the camera: not built");
                fx.Quad(new Vector3(0f, 1f, 8f), Vector3.right * 0.2f, Vector3.up * 0.2f, Color.white, Color.white);
                Assert.AreEqual(4, fx.VertCount, "in view: built");
                paint.Quad(new Vector3(0f, 1f, -20f), Vector3.right * 0.2f, Vector3.up * 0.2f, Color.white, Color.white);
                Assert.AreEqual(4, paint.VertCount, "ground paint stamps and markers are never view-culled");
            }
            finally
            {
                Object.DestroyImmediate(fx.Mesh);
                Object.DestroyImmediate(paint.Mesh);
            }
        }

        [Test]
        public void TheDitherFadesBodiesOnTheSightLineOnlyAndNeverThePlayer()
        {
            var cam = new Vector3(0f, 3.3f, 0f);
            var chest = new Vector3(0f, 1.2f, 4.4f);
            Vector3 mid = Vector3.Lerp(cam, chest, 0.5f);
            Assert.AreEqual(ViewCull.MaxFade, ViewCull.FadeAmount(cam, chest, mid, ViewCull.FadeRadius), 1e-5f, "on the line: most pixels gone");
            float edge = ViewCull.FadeAmount(cam, chest, mid + Vector3.right * 0.75f, ViewCull.FadeRadius);
            Assert.Greater(edge, 0f);
            Assert.Less(edge, ViewCull.MaxFade, "soft towards the tube's edge");
            Assert.AreEqual(0f, ViewCull.FadeAmount(cam, chest, mid + Vector3.right * 1.2f, ViewCull.FadeRadius), "beside the line: solid");
            Assert.AreEqual(0f, ViewCull.FadeAmount(cam, chest, chest, ViewCull.FadeRadius), "the player himself: solid");
            Assert.AreEqual(0f, ViewCull.FadeAmount(cam, chest, Vector3.Lerp(cam, chest, 0.9f), ViewCull.FadeRadius), "his front edge: solid");
            Assert.AreEqual(0f, ViewCull.FadeAmount(cam, chest, chest + (chest - cam) * 0.5f, ViewCull.FadeRadius), "beyond him: solid");
            Assert.Less(ViewCull.MaxFade, 1f, "never fully invisible");
        }

        [Test]
        public void TagsGrowUpCloseShrinkFarAndHideAtTheLensOrPastTheirRange()
        {
            Assert.AreEqual(1f, TagLens.Scale(TagLens.UnitDistance), 1e-6f);
            Assert.AreEqual(TagLens.MaxScale, TagLens.Scale(2f), 1e-6f, "up close: capped, never screen-filling");
            Assert.AreEqual(TagLens.MinScale, TagLens.Scale(40f), 1e-6f, "far: never below readable");
            Assert.Greater(TagLens.Scale(6f), TagLens.Scale(14f), "nearer is larger");
            Assert.IsFalse(TagLens.Show(0.5f, TagLens.TagMaxDistance), "at the lens: hidden");
            Assert.IsTrue(TagLens.Show(12f, TagLens.TagMaxDistance));
            Assert.IsFalse(TagLens.Show(TagLens.TagMaxDistance + 1f, TagLens.TagMaxDistance), "past the range: hidden");
            Assert.IsTrue(TagLens.Show(TagLens.TagMaxDistance + 1f, TagLens.BarMaxDistance), "health reads a little further");
            Assert.IsTrue(LowViewTags.IsMark("Name Tag 3", "Name Tag "));
            Assert.IsTrue(LowViewTags.IsMark("Hearts 0", "Hearts "));
            Assert.IsFalse(LowViewTags.IsMark("Heart 2", "Hearts "), "the player's own heart row is not an overhead mark");
            Assert.IsFalse(LowViewTags.IsMark("Name Tag", "Name Tag "));
            Assert.IsFalse(LowViewTags.IsMark("Hearts 1 Fill", "Hearts "));
        }

        [Test]
        public void ASidewaysRunHoldsTheYawAndForwardInputReCentres()
        {
            var g = new StrafeGate();
            var f = new YawFollow();
            f.Reset(0);
            const double dt = 1 / 60.0;
            // Two seconds of a pure sideways run: the travel says 90 degrees, the held yaw stays put.
            for (int i = 0; i < 120; i++)
            {
                g.Step(1, 0, out bool hold, out bool re);
                Assert.IsTrue(hold);
                Assert.IsFalse(re);
                f.Step(dt, hold ? f.Target : 90, false);
            }
            Assert.AreEqual(0, GroundCamera.DeltaAngle(f.Yaw, 0), 0.01, "the view did not turn during the strafe");
            // Forward comes back (W with D): the frame it returns is a re-centre, and only that frame.
            g.Step(1, 1, out bool h2, out bool r2);
            Assert.IsFalse(h2);
            Assert.IsTrue(r2);
            g.Step(1, 1, out _, out bool r3);
            Assert.IsFalse(r3);
            // Back-only input is not a strafe (the turn-around swing still applies), and a diagonal is not either.
            g.Step(0, -1, out bool h4, out _);
            Assert.IsFalse(h4);
            g.Step(0.7, 0.7, out bool h5, out _);
            Assert.IsFalse(h5);
            g.Step(0.1, 0, out bool h6, out _);
            Assert.IsFalse(h6, "a stick resting near the centre is not a strafe");
        }

        [Test]
        public void TheOldGroundSkyStandsAsideOnlyWhileTheWorldHorizonIsPresent()
        {
            var root = new GameObject("World Beyond Test");
            var bd = new GameObject("Backdrop Test").AddComponent<GroundBackdrop>();
            try
            {
                Assert.IsFalse(GroundBackdrop.HorizonPresent(null));
                Assert.IsFalse(GroundBackdrop.HorizonPresent(root.transform), "no horizon: keep the backdrop's own sky");
                var band = new GameObject("Band");
                band.transform.SetParent(root.transform, false);
                var h = new GameObject(GroundBackdrop.HorizonName);
                h.transform.SetParent(band.transform, false);
                Assert.IsTrue(GroundBackdrop.HorizonPresent(root.transform), "found under any depth");
                h.SetActive(false);
                Assert.IsFalse(GroundBackdrop.HorizonPresent(root.transform), "a hidden horizon does not count");
                Assert.IsTrue(bd.OwnSky);
                bd.SetOwnSky(false);
                Assert.IsFalse(bd.OwnSky);
                bd.SetOwnSky(true);
                Assert.IsTrue(bd.OwnSky);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(bd.gameObject);
            }
        }

        [Test]
        public void TheHeadingDefaultIsTheCreatorsAngleAsTheCommentSays()
        {
            Assert.IsTrue(CameraHeading.CreatorAngleIsDefault, "CameraHeading's summary says the creator's angle is the default");
        }
    }
}
