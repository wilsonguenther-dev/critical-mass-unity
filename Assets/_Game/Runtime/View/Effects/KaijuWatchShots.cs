using System;
using System.Collections;
using System.Globalization;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>
    /// fix/kaiju-hill-1 picture check (owner 2026-11-15: where is the Kaiju on the hill?). Player build, <c>-kaijuWatchShots [DIR]</c>
    /// (default DIR = GamePaths.DataDir/kaiju-watch). From the hatch, every 30 s of match time, it renders one still from a high
    /// camera framing the hill and the Kaiju into shot-kaiju-watch-NN.png (the main camera is re-posed for that one render and put
    /// back the same frame; UI canvases hidden for it) and logs one "[CM.KaijuWatch]" line. View only: it reads the World and never
    /// writes it, so no checksum can move. Added by CombatFx only when the flag is given; without it nothing exists or runs.
    /// </summary>
    public sealed class KaijuWatchShots : MonoBehaviour
    {
        public const string Flag = "-kaijuWatchShots";
        const double EverySeconds = 30;
        const int ShotW = 1280, ShotH = 720;

        SimDriverBehaviour _driver;
        string _dir;

        public static bool Requested =>
            Array.FindIndex(Environment.GetCommandLineArgs(), s => string.Equals(s, Flag, StringComparison.OrdinalIgnoreCase)) >= 0;

        static void Log(string s) => Debug.Log("[CM.KaijuWatch] " + s);
        static string F(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            if (_driver == null) _driver = FindAnyObjectByType<SimDriverBehaviour>();
            if (_driver == null) { Log("no sim driver; off"); enabled = false; return; }
            string[] a = Environment.GetCommandLineArgs();
            int i = Array.FindIndex(a, s => string.Equals(s, Flag, StringComparison.OrdinalIgnoreCase));
            _dir = i >= 0 && i + 1 < a.Length && !a[i + 1].StartsWith("-") ? a[i + 1] : Path.Combine(GamePaths.DataDir, "kaiju-watch");
            Directory.CreateDirectory(_dir);
            Log("on, writing to " + _dir);
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            while (true)
            {
                // A match with a hatched Kaiju (a new match starts the count again).
                World w = _driver.World;
                while (w == null || !w.Match.Hatched || w.Kaiju.State == KaijuState.Egg)
                {
                    yield return new WaitForSecondsRealtime(0.5f);
                    w = _driver.World;
                }
                double hatchAt = w.Match.Time;
                int n = 0;
                Log("hatch at t=" + F(hatchAt));
                while (ReferenceEquals(w, _driver.World) && w.Match.Phase == MatchPhase.Playing && w.Match.Hatched)
                {
                    double due = hatchAt + n * EverySeconds;
                    if (w.Match.Time + 1e-6 >= due)
                    {
                        yield return new WaitForEndOfFrame();
                        Shoot(w, n);
                        n++;
                    }
                    else yield return null;
                }
                Log("match over after " + n + " shots");
                while (ReferenceEquals(w, _driver.World)) yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        void Shoot(World w, int n)
        {
            Kaiju k = w.Kaiju;
            HillDef hill = w.Map.Hill;
            double dx = k.X - hill.X, dz = k.Z - hill.Z;
            double dist = Math.Sqrt(dx * dx + dz * dz);
            int barricades = 0, touching = 0;
            foreach (Barricade b in w.Barricades)
            {
                if (!b.Active) continue;
                double bx = b.Cx + 0.5 - hill.X, bz = b.Cz + 0.5 - hill.Z;
                if (bx * bx + bz * bz <= 13 * 13) barricades++;
            }
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (!w.Actors[i].Alive) continue;
                double ax = w.X[i] - k.X, az = w.Z[i] - k.Z;
                if (ax * ax + az * az <= 2.2 * 2.2) touching++;
            }
            string name = "shot-kaiju-watch-" + n.ToString("00", CultureInfo.InvariantCulture) + ".png";
            Log("t=" + F(w.Match.Time) + " kaiju " + F(k.X) + "," + F(k.Z) + " state " + k.State + " target " + k.TargetId +
                " distToHill " + F(dist) + " onHill " + (dist <= hill.R ? "yes" : "no") + " barricades13m " + barricades +
                " touching " + touching + " shot " + name);

            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) { Log("no main camera; no picture"); return; }
            Transform t = cam.transform;
            Vector3 p0 = t.position;
            Quaternion r0 = t.rotation;
            RenderTexture old = cam.targetTexture, prev = RenderTexture.active;
            RenderTexture rt = RenderTexture.GetTemporary(ShotW, ShotH, 24);
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            bool[] was = new bool[canvases.Length];
            Texture2D tex = null;
            try
            {
                for (int c = 0; c < canvases.Length; c++) { was[c] = canvases[c].enabled; canvases[c].enabled = false; }
                // Frame the hill and the Kaiju: look down at their midpoint from high enough to hold both plus the ring.
                Vector3 hillW = SimSpace.ToWorld(hill.X, 0, hill.Z);
                Vector3 kaijuW = SimSpace.ToWorld(k.X, 0, k.Z);
                Vector3 focus = (hillW + kaijuW) * 0.5f;
                float back = Mathf.Max(34f, (float)dist * 1.4f + 26f);
                Quaternion r = Quaternion.Euler(62f, 0f, 0f);
                t.rotation = r;
                t.position = focus - r * Vector3.forward * back;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(ShotW, ShotH, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, ShotW, ShotH), 0, 0);
                tex.Apply();
            }
            catch (Exception ex) { Log("shot failed: " + ex.Message); }
            finally
            {
                for (int c = 0; c < canvases.Length; c++) if (canvases[c] != null && was[c]) canvases[c].enabled = true;
                cam.targetTexture = old;
                RenderTexture.active = prev;
                t.position = p0;
                t.rotation = r0;
                RenderTexture.ReleaseTemporary(rt);
            }
            if (tex == null) return;
            try { File.WriteAllBytes(Path.Combine(_dir, name), tex.EncodeToPNG()); }
            catch (Exception ex) { Log("write failed: " + ex.Message); }
            Destroy(tex);
        }
    }
}
