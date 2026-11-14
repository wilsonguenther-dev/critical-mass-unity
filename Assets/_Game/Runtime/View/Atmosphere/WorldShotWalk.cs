using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Game;
using UnityEngine;

namespace CriticalMass.Runtime.View.Atmosphere
{
    /// <summary>
    /// World around arena 1: a capture walk that runs inside the BUILT player (so the art is judged on the second Mac with
    /// tools/remote-playtest.sh --mode captures, never in an Editor on the owner's Mac). Off unless the player runs with
    /// -shots AND the saved setting cm.dev.worldShots is 1 (set it on the capture box with
    /// `defaults write com.drivia.criticalmass cm.dev.worldShots -int 1`; the remote runner exports and restores the
    /// settings around a run). 75 s into the autoplay match it freezes the match (sim time scale 0, view only), then for
    /// gameplay-camera frames at the Azure edge, the north-west corner, the north edge, the Crimson half and the busy hill,
    /// and for Day, Dusk and Night, writes shot-w-FRAME-TIME-before.png (the world around the arena hidden) and -after.png
    /// into the data folder; then a wide beauty shot per time of day, a Night frame on Low, and world-walk.json with the
    /// budgets. Then the match resumes. Visual only: the Sim is paused, never written.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class WorldShotWalk : MonoBehaviour
    {
        public const string PrefKey = "cm.dev.worldShots";
        static bool _started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (_started || Application.isEditor) return;
            bool want;
            try { want = AutoplayArgs.Current.Shots && (PlayerPrefs.GetInt(PrefKey, 0) == 1 || Environment.GetEnvironmentVariable("CM_WORLD_SHOTS") == "1"); }
            catch (Exception) { want = false; }
            if (!want) return;
            _started = true;
            var go = new GameObject("World Shot Walk");
            DontDestroyOnLoad(go);
            go.AddComponent<WorldShotWalk>();
            Debug.Log("[CM.WorldWalk] armed");
        }

        /// <summary>Gameplay frames: name and sim point (x east, z south).</summary>
        static readonly (string name, double x, double z)[] Frames =
        {
            ("azure", 7.0, 30.0), ("corner", 7.0, 7.0), ("north", 50.0, 7.0), ("crimson", 98.0, 10.0), ("hill", 56.0, 40.0),
        };

        static readonly TimeOfDay[] Times = { TimeOfDay.Day, TimeOfDay.Dusk, TimeOfDay.Night };

        readonly StringBuilder _log = new StringBuilder();
        int _shots;
        bool _wide;
        Vector3 _widePos;
        Quaternion _wideRot;
        float _wideFov;

        void LateUpdate()
        {
            if (!_wide) return;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) return;
            cam.transform.SetPositionAndRotation(_widePos, _wideRot);
            cam.fieldOfView = _wideFov;
        }

        IEnumerator Start()
        {
            SimDriverBehaviour drv = null;
            while (true)
            {
                if (drv == null) drv = FindAnyObjectByType<SimDriverBehaviour>();
                if (drv != null && drv.World != null && drv.World.Match.Time > 75) break;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Note($"start: match time {drv.World.Match.Time:0.0}, quality {RenderTier.Level}, screen {Screen.width}x{Screen.height}");
            double ts = drv.TimeScale;
            drv.TimeScale = 0;
            CriticalMass.Runtime.View.Moments.BigMomentsSettings.ModeOverride = CriticalMass.Runtime.View.Moments.BigMomentsMode.Off;
            CriticalMass.Runtime.View.Moments.BigMoments.StopAll();
            CameraFollowTarget follow = FindAnyObjectByType<CameraFollowTarget>();
            yield return new WaitForSecondsRealtime(1.0f);

            // Gameplay camera, before (hidden) then after (shown), each in Day, Dusk and Night.
            foreach (bool shown in new[] { false, true })
            {
                WorldBeyond.SetShown(shown);
                yield return new WaitForSecondsRealtime(0.6f);
                foreach ((string name, double x, double z) f in Frames)
                {
                    if (follow != null) follow.SetOverride(f.x, f.z);
                    yield return new WaitForSecondsRealtime(0.7f);
                    foreach (TimeOfDay t in Times)
                    {
                        SetTime(t);
                        yield return new WaitForSecondsRealtime(0.35f);
                        Shot($"shot-w-{f.name}-{Name(t)}-{(shown ? "after" : "before")}.png");
                        yield return null;
                        yield return null;
                    }
                }
            }
            if (WorldBeyond.Current != null) Note($"world beyond on quality {RenderTier.Level}: {WorldBeyond.Current.Triangles} triangles, {WorldBeyond.Current.Draws} draws, {WorldBeyond.Current.Features} features");
            if (AmbientLife.Current != null) Note($"ambient on quality {RenderTier.Level}: {AmbientLife.Current.Creatures} creatures, {AmbientLife.Current.Sprites} sprites, {AmbientLife.Current.Triangles} triangles, {AmbientLife.Current.Draws} draws");

            // Wide beauty shots: the brain off, a lower camera from the south-east looking over the arena to the north-west.
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            Behaviour brain = null;
            float far0 = 0f, fov0 = 0f;
            if (cam != null)
            {
                foreach (Behaviour b in cam.GetComponents<Behaviour>()) if (b != null && b.GetType().Name == "CinemachineBrain") brain = b;
                if (brain != null) brain.enabled = false;
                far0 = cam.farClipPlane;
                fov0 = cam.fieldOfView;
                cam.farClipPlane = Mathf.Max(far0, 700f);
                var centre = new Vector3(56f, 0f, -36f);
                var up = new Vector3(-0.7071f, 0f, 0.7071f);
                _widePos = centre - up * 62f + Vector3.up * 34f;
                Vector3 look = centre + up * 26f + Vector3.down * 7f;
                _wideRot = Quaternion.LookRotation(look - _widePos, Vector3.up);
                _wideFov = 46f;
                _wide = true;
                foreach (TimeOfDay t in Times)
                {
                    SetTime(t);
                    yield return new WaitForSecondsRealtime(0.5f);
                    Shot($"shot-w-wide-{Name(t)}-after.png");
                    yield return null;
                    yield return null;
                }
                WorldBeyond.SetShown(false);
                yield return new WaitForSecondsRealtime(0.6f);
                SetTime(TimeOfDay.Dusk);
                yield return new WaitForSecondsRealtime(0.4f);
                Shot("shot-w-wide-dusk-before.png");
                yield return null;
                yield return null;
                WorldBeyond.SetShown(true);
                _wide = false;
                cam.farClipPlane = far0;
                cam.fieldOfView = fov0;
                if (brain != null) brain.enabled = true;
            }

            // The cheap version: Night on Low at the north edge.
            int lvl = RenderTier.Level;
            RenderTier.Set(FramePolicy.LevelLow);
            if (follow != null) follow.SetOverride(50.0, 7.0);
            yield return new WaitForSecondsRealtime(2.0f);
            SetTime(TimeOfDay.Night);
            yield return new WaitForSecondsRealtime(0.5f);
            Shot("shot-w-north-night-low.png");
            yield return null;
            if (WorldBeyond.Current != null) Note($"world beyond on Low: {WorldBeyond.Current.Triangles} triangles, {WorldBeyond.Current.Draws} draws, {WorldBeyond.Current.Features} features");
            if (AmbientLife.Current != null) Note($"ambient on Low: {AmbientLife.Current.Creatures} creatures, {AmbientLife.Current.Sprites} sprites, {AmbientLife.Current.Triangles} triangles, {AmbientLife.Current.Draws} draws");
            RenderTier.Set(lvl);
            foreach (int q in new[] { FramePolicy.LevelLow, FramePolicy.LevelMedium, FramePolicy.LevelHigh })
            {
                var m = new BeyondMesh();
                WorldBeyondBudget b = WorldBeyondBudget.For(q);
                var plan = FindAnyObjectByType<CriticalMass.Runtime.View.Dressing.WorldView>();
                WorldBeyondLayout.Build(m, b, drv.World.Map.Width, drv.World.Map.Height, plan != null && plan.Plan != null ? plan.Plan.Rim : null);
                AmbientBudget a = AmbientBudget.For(q);
                Note($"budget q{q}: world beyond {m.Tris * (b.Ink ? 2 : 1)} triangles {b.Draws} draws; ambient {a.Triangles(AmbientLife.Current != null ? AmbientLife.Current.LanternsFound : 0)} triangles {a.Draws(AmbientLife.Current != null ? AmbientLife.Current.LanternsFound : 0)} draws");
            }
            yield return new WaitForSecondsRealtime(1.0f);
            if (follow != null) follow.ClearOverride();
            TimeOfDayDirector.Override = null;
            CriticalMass.Runtime.View.Moments.BigMomentsSettings.ModeOverride = null;
            drv.TimeScale = ts > 0 ? ts : 1;
            Note($"done: {_shots} shots");
            try { File.WriteAllText(Path.Combine(GamePaths.DataDir, "world-walk.txt"), _log.ToString()); }
            catch (Exception e) { Debug.LogWarning("[CM.WorldWalk] could not write the log: " + e.Message); }
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
            Debug.Log("[CM.WorldWalk] " + s);
        }
    }
}
