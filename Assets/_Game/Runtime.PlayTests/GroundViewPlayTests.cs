using System.Collections;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Spectator;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>
    /// feat/ground-camera in the real Boot scene (capture mode: actor 0 on autopilot, the wall clock stopped, the world a
    /// pure function of the ticks stepped). Spectating in ground view for 20 sim seconds the camera never enters
    /// geometry (a sphere check every frame against the grid's walls and terraces and the dressing's trees and towers,
    /// GroundCollision.Inside; the world has no physics colliders) and never loses its target; the high view is
    /// untouched by default and comes back exactly when ground view ends.
    /// </summary>
    public sealed class GroundViewPlayTests
    {
        const string BootScene = "Assets/_Game/Scenes/Boot.unity";
        SimDriverBehaviour _driver;
        bool _hadSetting;
        int _oldSetting;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _hadSetting = PlayerPrefs.HasKey(GameSettings.GroundCameraKey);
            _oldSetting = PlayerPrefs.GetInt(GameSettings.GroundCameraKey, 0);
            PlayerPrefs.DeleteKey(GameSettings.GroundCameraKey);
            SpectatorMode.SpectateNextMatch = false;
            SimDriverBehaviour.CaptureMode = true;
            SceneManager.LoadScene(BootScene);
            yield return null;
            SimDriverBehaviour.CaptureMode = false;
            yield return null;
            _driver = Object.FindAnyObjectByType<SimDriverBehaviour>();
            Assert.IsNotNull(_driver, "Boot did not build a SimDriverBehaviour");
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            if (SpectatorMode.IsSpectating) SpectatorMode.Rejoin();
            if (GroundView.Current != null) GroundView.Current.WatchGround = false;
            SimDriverBehaviour.CaptureMode = false;
            SpectatorMode.SpectateNextMatch = false;
            if (_hadSetting) PlayerPrefs.SetInt(GameSettings.GroundCameraKey, _oldSetting);
            else PlayerPrefs.DeleteKey(GameSettings.GroundCameraKey);
            _driver = null;
        }

        [UnityTest]
        public IEnumerator TheHighViewIsTheDefaultAndGroundViewIsOff()
        {
            yield return null;
            GroundView g = GroundView.Current;
            Assert.IsNotNull(g, "CameraRig installs GroundView");
            Assert.AreEqual(GroundViewMode.Off, g.Mode);
            Assert.IsFalse(GameSettings.GroundCamera);
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            Assert.IsTrue(cam.GetComponent<CinemachineBrain>().enabled, "the brain drives the high camera");
            Assert.AreEqual(GameplayCamera.FovDeg, cam.fieldOfView, 0.01);
        }

        [UnityTest]
        public IEnumerator SpectatingInGroundViewFor20SecondsNeverEntersGeometryNorLosesTheTarget()
        {
            Assert.IsTrue(SpectatorMode.Enter(), "spectator mode starts");
            GroundView g = GroundView.Current;
            g.WatchGround = true;
            g.SetDirector(true);
            yield return null;
            yield return null;
            Assert.AreEqual(GroundViewMode.Watch, g.Mode, "spectating with ground view asked: watching from the ground");
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            World w = _driver.World;
            int hz = (int)System.Math.Round(_driver.Config.SimHz);
            int ticks = 20 * hz;
            int frames = 0, overHead = 0;
            int ticksPerFrame = Mathf.Max(1, hz / 30);
            for (int t = 0; t < ticks; t += ticksPerFrame)
            {
                _driver.StepTicks(ticksPerFrame);
                yield return null;
                frames++;
                Assert.AreEqual(GroundViewMode.Watch, g.Mode, $"frame {frames}: still in ground view");
                Assert.IsTrue(GroundDirector.Valid(w, g.Target), $"frame {frames}: the target {g.Target} is gone");
                Vector3 p = cam.transform.position;
                SimSpace.ToSim(p, out double sx, out double sz);
                int n = g.Cylinders(out SeeThroughCylinder[] cyl);
                Assert.IsFalse(GroundCollision.Inside(w.Map, sx, p.y, sz, GroundCollision.Radius * 0.9, cyl, n),
                    $"frame {frames}: camera at ({sx:F2}, {p.y:F2}, {sz:F2}) is inside geometry (target {g.Target})");
                Assert.IsFalse(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z));
                // Low and close: within a few metres of its target, horizon on screen (pitch well under the high view's 55).
                Assert.Less(cam.transform.eulerAngles.x < 180 ? cam.transform.eulerAngles.x : 0f, 40f);
                if (g.Follow.OverHead) overHead++;
            }
            Assert.Greater(frames, 100);
            Assert.Less(overHead, frames / 2, "the over-the-head fallback is the exception, not the shot");
            Assert.IsFalse(cam.GetComponent<CinemachineBrain>().enabled, "ground view owns the camera");
            Assert.AreEqual(GroundCamera.FovDeg, cam.fieldOfView, 0.01);

            // Off again: the brain and the high lens come back.
            g.WatchGround = false;
            yield return null;
            yield return null;
            Assert.AreEqual(GroundViewMode.Off, g.Mode);
            Assert.IsTrue(cam.GetComponent<CinemachineBrain>().enabled);
            Assert.AreEqual(GameplayCamera.FovDeg, cam.fieldOfView, 0.01);
        }

        [UnityTest]
        public IEnumerator TheCameraSettingPlaysFromTheGroundWithCameraRelativeInput()
        {
            yield return null;
            _driver.HumanAutopilot = false;
            GameSettings.GroundCamera = true;
            yield return null;
            yield return null;
            GroundView g = GroundView.Current;
            if (!_driver.World.Actors[0].Alive) Assert.Ignore("actor 0 is down at the start of this seed");
            Assert.AreEqual(GroundViewMode.Play, g.Mode, "CAMERA: GROUND plays from the ground");
            Assert.IsTrue(GroundView.PlayActive);
            CriticalMass.Runtime.Input.HumanInputSource src = _driver.GetComponent<CriticalMass.Runtime.Input.HumanInputSource>();
            Assert.IsTrue(src.State.CameraBasisActive, "the move goes through the camera's basis");
            Assert.IsTrue(GroundView.TryPlayAim(_driver.World, out double ax, out double az));
            GroundCamera.Basis(GroundView.PlayYaw, out double ux, out double uz, out _, out _);
            double dx = ax - _driver.World.X[0], dz = az - _driver.World.Z[0];
            Assert.Greater(dx * ux + dz * uz, 0, "the aim is ahead along the camera");
            GameSettings.GroundCamera = false;
            yield return null;
            yield return null;
            Assert.AreEqual(GroundViewMode.Off, g.Mode);
            yield return null;
            Assert.IsFalse(src.State.CameraBasisActive, "back on the high camera, the fixed basis");
        }
    }
}
