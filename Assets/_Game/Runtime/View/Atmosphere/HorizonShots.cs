using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using CriticalMass.Runtime.Game;
using UnityEngine;

namespace CriticalMass.Runtime.View.Atmosphere
{
    /// <summary>
    /// World horizon 1: eye-level capture stills, inside the BUILT player (judged on the second Mac, never in an Editor on
    /// the owner's Mac). Off unless the player runs with -horizonShots (or CM_HORIZON_SHOTS=1). Works without the follow
    /// camera: it drives Camera.main itself as a FIXED capture camera at head height (1.7 u over the ground, FOV 60), with
    /// the Cinemachine brain switched off while it shoots. 4 s into the match it freezes the sim (time scale 0, view only),
    /// then for Day, Dusk and Night writes into the data folder:
    ///   hz-hill-north/east/south/west-TIME.png   from the hill, looking toward the shrine, Crimson, south and Azure;
    ///   hz-azure-out-TIME.png, hz-crimson-out-TIME.png   from behind each base, looking out past it;
    ///   hz-edge-north-TIME.png, hz-edge-west-TIME.png   standing at an island edge, looking out and down over the rim;
    ///   hz-hill-west-TIME-nohorizon.png   the same hill frame with the new horizon hidden (before / after);
    ///   hz-topdown-TIME.png and hz-topdown-TIME-nohorizon.png   the gameplay camera, to prove the top-down look holds;
    /// and horizon-shots.txt with the costs per tier. Then it gives the camera back and resumes. Visual only.
    /// </summary>
    [DefaultExecutionOrder(10001)]
    public sealed class HorizonShots : MonoBehaviour
    {
        public const string Flag = "-horizonShots";
        static bool _started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (_started || Application.isEditor) return;
            bool want = false;
            try
            {
                foreach (string a in Environment.GetCommandLineArgs()) want |= string.Equals(a, Flag, StringComparison.OrdinalIgnoreCase);
                want |= Environment.GetEnvironmentVariable("CM_HORIZON_SHOTS") == "1";
            }
            catch (Exception) { want = false; }
            if (!want) return;
            _started = true;
            var go = new GameObject("Horizon Shots");
            DontDestroyOnLoad(go);
            go.AddComponent<HorizonShots>();
            Debug.Log("[CM.HorizonShots] armed");
        }

        /// <summary>Name, sim point (x east, z south), yaw (0 north, 90 east), degrees looking down.</summary>
        static readonly (string name, float x, float z, float yaw, float down)[] Frames =
        {
            ("hill-north", 56f, 36f, 0f, 3f), ("hill-east", 56f, 36f, 90f, 3f), ("hill-south", 56f, 36f, 180f, 3f), ("hill-west", 56f, 36f, 270f, 3f),
            ("azure-out", 2.5f, 36f, 270f, 2f), ("crimson-out", 109.5f, 36f, 90f, 2f),
            ("edge-north", 50f, 0.8f, 0f, 14f), ("edge-west", 0.8f, 20f, 270f, 14f),
        };

        static readonly TimeOfDay[] Times = { TimeOfDay.Day, TimeOfDay.Dusk, TimeOfDay.Night };
        const float Eye = 1.7f, Fov = 60f;

        readonly StringBuilder _log = new StringBuilder();
        int _shots;
        bool _hold;
        Vector3 _pos;
        Quaternion _rot;

        void LateUpdate()
        {
            if (!_hold) return;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) return;
            cam.transform.SetPositionAndRotation(_pos, _rot);
            cam.fieldOfView = Fov;
        }

        IEnumerator Start()
        {
            SimDriverBehaviour drv = null;
            while (true)
            {
                if (drv == null) drv = FindAnyObjectByType<SimDriverBehaviour>();
                if (drv != null && drv.World != null && drv.World.Match.Time > 4) break;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            yield return new WaitForSecondsRealtime(2.0f);   // the world around the arena builds after the plan
            Note($"start: match time {drv.World.Match.Time:0.0}, quality {RenderTier.Level}, screen {Screen.width}x{Screen.height}");
            double ts = drv.TimeScale;
            drv.TimeScale = 0;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) { Note("no main camera"); Finish(drv, ts); yield break; }

            // Top-down first, with the gameplay camera as it is: with and without the horizon (they should match).
            foreach (TimeOfDay t in Times)
            {
                SetTime(t);
                yield return new WaitForSecondsRealtime(0.5f);
                Shot($"hz-topdown-{Name(t)}.png");
                yield return null;
                SetHorizon(false);
                yield return null;
                Shot($"hz-topdown-{Name(t)}-nohorizon.png");
                yield return null;
                SetHorizon(true);
            }

            Behaviour brain = null;
            foreach (Behaviour b in cam.GetComponents<Behaviour>()) if (b != null && b.GetType().Name == "CinemachineBrain") brain = b;
            if (brain != null) brain.enabled = false;
            float far0 = cam.farClipPlane, fov0 = cam.fieldOfView;
            cam.farClipPlane = Mathf.Max(far0, 600f);
            _hold = true;
            foreach ((string name, float x, float z, float yaw, float down) f in Frames)
            {
                _pos = new Vector3(f.x, GroundY(f.x, -f.z) + Eye, -f.z);
                _rot = Quaternion.Euler(f.down, f.yaw, 0f);
                yield return new WaitForSecondsRealtime(0.4f);
                foreach (TimeOfDay t in Times)
                {
                    SetTime(t);
                    yield return new WaitForSecondsRealtime(0.4f);
                    Shot($"hz-{f.name}-{Name(t)}.png");
                    yield return null;
                    if (f.name == "hill-west")
                    {
                        SetHorizon(false);
                        yield return null;
                        Shot($"hz-{f.name}-{Name(t)}-nohorizon.png");
                        yield return null;
                        SetHorizon(true);
                    }
                    yield return null;
                }
            }
            _hold = false;
            cam.farClipPlane = far0;
            cam.fieldOfView = fov0;
            if (brain != null) brain.enabled = true;

            WorldBeyond wb = WorldBeyond.Current;
            if (wb != null) Note($"quality {RenderTier.Level}: world beyond {wb.Triangles} triangles {wb.Draws} draws; horizon {wb.HorizonTriangles} triangles {wb.HorizonDraws} draw, 0 texture bytes");
            foreach (int q in new[] { FramePolicy.LevelLow, FramePolicy.LevelMedium, FramePolicy.LevelHigh })
            {
                var hm = new HorizonMesh();
                WorldHorizonLayout.Build(hm, WorldHorizonBudget.For(q));
                Note($"budget q{q}: horizon {hm.Tris} triangles, {hm.V.Count} vertices, {WorldHorizonBudget.Draws} draw");
            }
            Finish(drv, ts);
        }

        void Finish(SimDriverBehaviour drv, double ts)
        {
            TimeOfDayDirector.Override = null;
            if (drv != null) drv.TimeScale = ts > 0 ? ts : 1;
            Note($"done: {_shots} shots");
            try { File.WriteAllText(Path.Combine(GamePaths.DataDir, "horizon-shots.txt"), _log.ToString()); }
            catch (Exception e) { Debug.LogWarning("[CM.HorizonShots] could not write the log: " + e.Message); }
        }

        /// <summary>Ground under a point: a ray down onto whatever the arena has; 2.8 u (over the hill) when nothing answers.</summary>
        static float GroundY(float x, float z)
        {
            RaycastHit[] hits = Physics.RaycastAll(new Vector3(x, 40f, z), Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NaN;
            foreach (RaycastHit h in hits)
                if (h.point.y > -3f && h.point.y < 12f && (float.IsNaN(best) || h.point.y > best)) best = h.point.y;
            return float.IsNaN(best) ? 2.8f : best;
        }

        static void SetHorizon(bool on)
        {
            WorldBeyond wb = WorldBeyond.Current;
            if (wb == null) return;
            Transform t = FindDeep(wb.transform, "World Horizon");
            if (t != null) t.gameObject.SetActive(on);
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        static string Name(TimeOfDay t) => t.ToString().ToLowerInvariant();

        static void SetTime(TimeOfDay t)
        {
            TimeOfDayDirector.Override = t;
            if (TimeOfDayDirector.Current != null) TimeOfDayDirector.Current.SnapToTarget();
        }

        void Shot(string file)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(GamePaths.DataDir, file));
            _shots++;
        }

        void Note(string s)
        {
            string line = DateTime.UtcNow.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + s;
            _log.AppendLine(line);
            Debug.Log("[CM.HorizonShots] " + s);
        }
    }
}
