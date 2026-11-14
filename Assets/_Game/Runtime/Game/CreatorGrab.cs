using System;
using System.Collections.Generic;
using System.IO;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.View.Moments;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// -creatorGrab (automation only, with -shots -shotTick T): the creator's camera-orientation grabs. From tick T the
    /// clock is held and the sim steps one tick at a time until actor 0 stands on the hill (a pure function of the world,
    /// so the same seed lands on the same tick whatever the camera yaw), then four stills are written:
    /// shot-grab-M-normal-hud / -normal-clean (the gameplay camera on the player) and shot-grab-M-wide-hud / -wide-clean (M = 1, 2: the second moment the next on-hill tick 600 ticks later) (the
    /// same rotation pulled back until both bases and the hill fit). "hud" keeps the match HUD and kill feed but hides
    /// banners, cut-ins, toasts, captions, prompts and the automation plate; "clean" hides every canvas. Changes when
    /// ticks run, never what they compute; the camera and canvases are restored afterwards.
    /// </summary>
    public sealed class CreatorGrab
    {
        public const string Flag = "-creatorgrab";   // lower case: HasAutomationFlag compares lowered args
        const int SeekLimit = 3600, PhaseFrames = 12, ShotAt = 8;
        static readonly string[] Names = { "normal-hud", "normal-clean", "wide-hud", "wide-clean" };

        readonly SimDriverBehaviour _driver;
        readonly List<GameObject> _hidden = new List<GameObject>();
        const int Moments = 2, MomentGap = 600;
        int _moment, _momentStart = 1;
        Behaviour _brain;
        float _fov, _far;
        bool _sought;
        GameHud _hud;

        public CreatorGrab(SimDriverBehaviour driver) { _driver = driver; }

        public static bool Wanted() => AutoplayArgs.HasAutomationFlag(Flag);

        /// <summary>Frame n of the hold (1-based). True when the four stills are written and everything is restored.</summary>
        public bool Step(int n)
        {
            World w = _driver.World;
            n -= _momentStart - 1;
            if (!_sought)
            {
                _sought = true;
                int k = 0;
                if (_moment > 0) { _driver.StepTicks(MomentGap); }
                while (k < SeekLimit && !OnHill(w)) { _driver.StepTicks(1); k++; }
                Debug.Log($"[CM.Grab] tick {w.Tick} (stepped {k}{(k >= SeekLimit ? ", limit: player not on the hill" : "")}) yaw {_driver.Config.Render.CameraYawDeg}");
                UnityEngine.Camera c0 = UnityEngine.Camera.main;
                _brain = c0 != null ? (Behaviour)c0.GetComponent("CinemachineBrain") : null;
                if (c0 != null) { _fov = c0.fieldOfView; _far = c0.farClipPlane; }
                return false;
            }
            int phase = (n - 2) / PhaseFrames, f = (n - 2) % PhaseFrames;
            if (phase >= Names.Length)
            {
                Restore();
                if (++_moment >= Moments) return true;
                _sought = false;
                _momentStart += n;
                return false;
            }
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) return false;
            BigMoments.StopAll();
            Banner.Hide();
            if (f == 0) { foreach (GameObject g in _hidden) if (g != null) g.SetActive(true); _hidden.Clear(); }
            Canvases(phase % 2 == 1);
            if (_brain != null) _brain.enabled = false;
            Place(cam, w, phase >= 2);
            if (f == ShotAt)
            {
                string path = Path.Combine(GamePaths.DataDir, "shot-grab-" + (_moment + 1) + "-" + Names[phase] + ".png");
                ScreenCapture.CaptureScreenshot(path);
                Debug.Log($"[CM.Grab] {path} cam {cam.transform.position} fov {cam.fieldOfView:F1}");
            }
            return false;
        }

        static bool OnHill(World w)
        {
            if (w.ActorCount == 0 || !w.Actors[0].Alive) return false;
            HillDef h = w.Map.Hill;
            double dx = w.X[0] - h.X, dz = w.Z[0] - h.Z;
            return dx * dx + dz * dz <= (h.R + 1) * (h.R + 1);
        }

        /// <summary>Hide transient canvases (hud) or every canvas (clean). The match HUD and kill feed stay in hud.</summary>
        void Canvases(bool clean)
        {
            if (_hud == null) _hud = UnityEngine.Object.FindAnyObjectByType<GameHud>();
            Canvas hud = _hud != null ? _hud.Canvas : null;
            foreach (Canvas c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas) continue;
                // Deactivate the canvas object (its owner sits on the parent and keeps running): feeds and captions
                // set canvas.enabled themselves every frame, so enabled = false does not stick.
                bool keep = !clean && (c == hud || c.gameObject.name == "Kill Feed Canvas");
                if (keep) continue;
                _hidden.Add(c.gameObject);
                c.gameObject.SetActive(false);
            }
        }

        void Place(UnityEngine.Camera cam, World w, bool wide)
        {
            GameConfig cfg = _driver.Config;
            MapDef map = w.Map;
            Quaternion rot = GameplayCameraRotation(cfg);
            Vector3 fwd = rot * Vector3.forward;
            Vector3 groundUp = new Vector3(fwd.x, 0f, fwd.z).normalized;
            cam.fieldOfView = 35f;
            cam.transform.rotation = rot;
            if (!wide)
            {
                double px = w.PrevX[0] + (w.X[0] - w.PrevX[0]) * _driver.Alpha, pz = w.PrevZ[0] + (w.Z[0] - w.PrevZ[0]) * _driver.Alpha;
                Vector3 target = SimSpace.ToWorld(px, GroundHeight.At(map, px, pz), pz) + groundUp * 2f;
                cam.farClipPlane = Mathf.Max(_far, 200f);
                cam.transform.position = target - fwd * 23.4f;
                return;
            }
            var pts = new List<Vector3>();
            void Box(double x, double z, double r)
            {
                double y = GroundHeight.At(map, x, z);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    pts.Add(SimSpace.ToWorld(x + r * Math.Cos(a), y, z + r * Math.Sin(a)));
                }
            }
            Box(map.Hill.X, map.Hill.Z, map.Hill.R + 3);
            foreach (PadDef p in map.Pads) if (p != null) Box(p.X, p.Z, 9);
            Vector3 centre = Vector3.zero;
            foreach (Vector3 p in pts) centre += p;
            centre /= pts.Count;
            float lo = 10f, hi = 600f;
            for (int it = 0; it < 30; it++)
            {
                float d = (lo + hi) * 0.5f;
                cam.transform.position = centre - fwd * d;
                cam.farClipPlane = d + 300f;
                bool fits = true;
                foreach (Vector3 p in pts)
                {
                    Vector3 v = cam.WorldToViewportPoint(p);
                    if (v.z <= 0 || v.x < 0.04f || v.x > 0.96f || v.y < 0.10f || v.y > 0.84f) { fits = false; break; }
                }
                if (fits) hi = d; else lo = d;
            }
            cam.transform.position = centre - fwd * hi;
            cam.farClipPlane = hi + 300f;
        }

        static Quaternion GameplayCameraRotation(GameConfig c) =>
            Quaternion.Euler(55f, (float)(360 - c.Render.CameraYawDeg), 0f);

        void Restore()
        {
            foreach (GameObject g in _hidden) if (g != null) g.SetActive(true);
            _hidden.Clear();
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null) { cam.fieldOfView = _fov; cam.farClipPlane = _far; }
            if (_brain != null) _brain.enabled = true;
        }
    }
}
