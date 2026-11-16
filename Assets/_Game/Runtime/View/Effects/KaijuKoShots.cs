using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>
    /// b12-t: the knocked-out Kaiju in a PLAYER build. Autoplay run with <c>-kaijuKoShots [DIR]</c> (default DIR =
    /// GamePaths.DataDir/kaiju-ko-shots); a staged run only, never part of a normal match (AutoplayArgs adds it to the
    /// match's driver only when the flag is on the command line). The world is reset to seed 1337, the egg is force-hatched
    /// (World.ForceHatch, the rules' own switch, as -skillShots does), the Kaiju grows up (ArtMapping.HatchlingSeconds), and
    /// it is knocked out through Kaiju.DamageKaiju with its whole HP from a fighter the rules let hit it: the same call a
    /// real blow takes (Combat, Demolitions), so KoIn, the KaijuKO event and the view's down pose are the real ones.
    /// Stills (Plan): 0.5 s after the knock-out (falling), 2 s after (lying on its belly), and after it recovers (the
    /// KaijuRecover event plus the get-up). Each still is written twice: the gameplay camera held on the Kaiju (full
    /// screen, HUD hidden) and a low side camera (a camera render, no UI), and logged as
    /// <c>[CM.KaijuKoShots] NN-name REACHED|NOT REACHED rootPitch=.. chinHeight=.. weight=.. expect=..</c>
    /// read from KaijuAnimator's DownRootPitchDeg / DownChinHeight / DownWeight and held to KaijuDownPose.InBand.
    /// The clock is held (AutoplayArgs.ClockHeld) and stepped tick by tick; the run quits when done.
    /// </summary>
    public sealed class KaijuKoShots : MonoBehaviour
    {
        public const string Flag = "-kaijuKoShots";
        public const int Seed = 1337, ShotW = 1600, ShotH = 900;
        /// <summary>Low side camera: height above the ground, distance from the Kaiju, look-at lift (u).</summary>
        public const float SideHeight = 1.4f, SideDistance = 11f, SideLookLift = 1.0f;
        /// <summary>Seconds after the hatch before the knock-out (past the hatchling model).</summary>
        public const double GrownSeconds = ArtMapping.HatchlingSeconds + 2.0;

        /// <summary>What a still must show: mid-fall, down in the band, or standing again.</summary>
        public enum Expect { Falling, Down, Up }

        /// <summary>One still: its name, when (seconds after the knock-out, or after the recovery), and what it must show.</summary>
        public struct Still
        {
            public string Name;
            public double Seconds;
            public bool AfterRecover;
            public Expect Expect;
        }

        /// <summary>The three stills, in order (one config place; tests).</summary>
        public static readonly Still[] Plan =
        {
            new Still { Name = "01-ko-0.5s", Seconds = 0.5, AfterRecover = false, Expect = Expect.Falling },
            new Still { Name = "02-ko-2s", Seconds = 2.0, AfterRecover = false, Expect = Expect.Down },
            // "on Recovered": taken once the get-up after the KaijuRecover event has finished, so it shows the Kaiju standing
            new Still { Name = "03-recovered", Seconds = KaijuAnimator.GetUpSeconds + 0.25, AfterRecover = true, Expect = Expect.Up },
        };

        static bool? _req;
        /// <summary>True when the command line has -kaijuKoShots.</summary>
        public static bool Requested
        {
            get
            {
                if (_req == null)
                {
                    _req = false;
                    foreach (string a in Environment.GetCommandLineArgs())
                        if (string.Equals(a, Flag, StringComparison.OrdinalIgnoreCase)) _req = true;
                }
                return _req.Value;
            }
        }

        /// <summary>Did a still reach what it expects (pure; tests)? h = the Kaiju's drawn height.</summary>
        public static bool Reached(Expect e, float weight, float rootPitchDeg, float chinHeight, float h)
        {
            switch (e)
            {
                case Expect.Falling: return weight > 0f && rootPitchDeg > 0f;
                case Expect.Down: return weight >= 0.99f && !float.IsNaN(chinHeight) && KaijuDownPose.InBand(rootPitchDeg, chinHeight, h);
                default: return weight <= 0.001f && Mathf.Abs(rootPitchDeg) < 1f;
            }
        }

        SimDriverBehaviour _driver;
        ActorViews _views;
        CombatFx _fx;
        string _dir;
        readonly List<Canvas> _hidden = new List<Canvas>();

        static void Log(string s) => Debug.Log("[CM.KaijuKoShots] " + s);
        static string F(double v, string f = "0.00") => double.IsNaN(v) ? "NaN" : v.ToString(f, CultureInfo.InvariantCulture);

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            _views = GetComponent<ActorViews>();
            _fx = GetComponent<CombatFx>();
            if (_driver == null || _views == null) { Log("no driver or views on this object; not run"); enabled = false; return; }
            string[] a = Environment.GetCommandLineArgs();
            int i = Array.FindIndex(a, s => string.Equals(s, Flag, StringComparison.OrdinalIgnoreCase));
            _dir = i >= 0 && i + 1 < a.Length && !a[i + 1].StartsWith("-") ? a[i + 1] : Path.Combine(GamePaths.DataDir, "kaiju-ko-shots");
            Directory.CreateDirectory(_dir);
            StartCoroutine(Run());
        }

        void OnDestroy()
        {
            AutoplayArgs.ClockHeld = false;
            ShowHud();
        }

        static bool TitleShowing()
        {
            var t = FindAnyObjectByType<CriticalMass.Runtime.Hud.TitleScreen>();
            return t != null && t.isActiveAndEnabled;
        }

        void HideHud()
        {
            CriticalMass.Runtime.View.Moments.BigMoments.StopAll();
            CriticalMass.Runtime.Hud.Banner.ClearAll();
            foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c != null && c.enabled && c.isRootCanvas && c.renderMode != RenderMode.WorldSpace) { c.enabled = false; _hidden.Add(c); }
        }

        void ShowHud()
        {
            foreach (Canvas c in _hidden) if (c != null) c.enabled = true;
            _hidden.Clear();
        }

        void Step(int n)
        {
            for (int t = 0; t < n; t++)
            {
                _driver.HumanAutopilot = true;
                _driver.StepTicks(1);
            }
        }

        /// <summary>A fighter the rules let hit the Kaiju now (Kaiju.DamageKaiju's own team checks), or -1.</summary>
        static int Hitter(World w)
        {
            KaijuState s = w.Kaiju.State;
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (!w.Actors[i].Alive) continue;
                TeamId t = w.Actors[i].Team;
                if (s == KaijuState.Azure && t != TeamId.Crimson) continue;
                if (s == KaijuState.Crimson && t != TeamId.Azure) continue;
                return i;
            }
            return -1;
        }

        IEnumerator Run()
        {
            float t0 = Time.realtimeSinceStartup;
            while (_driver.World == null || _driver.World.Match.Phase != MatchPhase.Playing || TitleShowing())
            {
                if (Time.realtimeSinceStartup - t0 > 120f) { Log("no match started; giving up"); Application.Quit(3); yield break; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            yield return new WaitForSecondsRealtime((float)CriticalMass.Runtime.Hud.FirstMinutes.BannerHoldSeconds + CriticalMass.Runtime.Hud.FirstMinutes.FadeSeconds);
            double scale = _driver.TimeScale;
            World w = _driver.World;
            World.ResetWorld(w, new WorldOptions { Seed = Seed, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = _driver.Config });
            CriticalMass.Runtime.Hud.FirstMinutes.StartBannerOff = true;
            CriticalMass.Runtime.Hud.Banner.ClearAll();
            AutoplayArgs.ClockHeld = true;
            _driver.TimeScale = 0;
            SimDriverBehaviour.ScriptedHumanFrame = null;
            double hz = w.Config.SimHz;
            var cam = FindAnyObjectByType<CriticalMass.Runtime.Camera.CameraFollowTarget>();

            // 1. hatch (the rules' own switch) and grow up
            w.ForceHatch = true;
            int stepped = 0, limit = (int)(240 * hz);
            while (!(w.Match.Hatched && w.Kaiju.State != KaijuState.Egg) && stepped < limit && w.Match.Phase == MatchPhase.Playing)
            {
                Step(1);
                if (++stepped % 300 == 0) yield return null;
            }
            w.ForceHatch = false;
            if (!w.Match.Hatched || w.Kaiju.State == KaijuState.Egg) { Log("NOT REACHED: the egg did not hatch after " + stepped + " ticks"); yield return Quit(scale, 3); yield break; }
            int hatchTick = w.Tick;
            Log("hatched at tick " + hatchTick + " (" + w.Kaiju.State + ")");
            for (int n = 0; n < (int)Math.Round(GrownSeconds * hz) && w.Match.Phase == MatchPhase.Playing; n++)
            {
                Step(1);
                if (n % 120 == 0) { if (cam != null) cam.SetOverride(w.Kaiju.X, w.Kaiju.Z); yield return null; }
            }

            // 2. the knock-out, through the real damage path
            int src = Hitter(w);
            double hp = w.Kaiju.Hp;
            double dealt = src >= 0 && hp > 0 ? CriticalMass.Sim.Core.Kaiju.DamageKaiju(w, src, hp) : 0;
            if (!(w.Kaiju.KoIn > 0))
            {
                Log("NOT REACHED: the knock-out was refused (state " + w.Kaiju.State + ", hp " + F(hp) + ", source " + src + ", dealt " + F(dealt) + ")");
                yield return Quit(scale, 3);
                yield break;
            }
            int koTick = w.Tick;
            Log("knocked out at tick " + koTick + " by actor " + src + " (" + w.Kaiju.State + ", hp " + F(hp) + ", KO " + F(w.Kaiju.KoIn) + " s)");

            int recoverTick = -1;
            int reached = 0;
            for (int p = 0; p < Plan.Length; p++)
            {
                Still st = Plan[p];
                if (st.AfterRecover && recoverTick < 0)
                {
                    int guard = 0, max = (int)Math.Ceiling((w.Config.Kaiju.KoSeconds + 5) * hz);
                    while (w.Kaiju.KoIn > 0 && guard < max && w.Match.Phase == MatchPhase.Playing)
                    {
                        Step(1);
                        if (++guard % 300 == 0) { if (cam != null) cam.SetOverride(w.Kaiju.X, w.Kaiju.Z); yield return null; }
                    }
                    if (w.Kaiju.KoIn > 0) { Log(st.Name + " NOT REACHED: still knocked out after " + guard + " ticks"); continue; }
                    recoverTick = w.Tick;
                    Log("recovered at tick " + recoverTick + " (" + F((recoverTick - koTick) / hz, "0.0") + " s after the knock-out)");
                }
                int target = (st.AfterRecover ? recoverTick : koTick) + (int)Math.Round(st.Seconds * hz);
                if (w.Tick < target) Step(target - w.Tick);
                // the views pose at the held clock; the camera settles on the Kaiju
                for (int f = 0; f < 20; f++)
                {
                    if (cam != null) cam.SetOverride(w.Kaiju.X, w.Kaiju.Z);
                    if (f == 14) HideHud();
                    yield return null;
                }
                yield return new WaitForEndOfFrame();
                KaijuAnimator ka = _views.KaijuAnimator;
                float weight = ka.DownWeight, pitch = ka.DownRootPitchDeg, chin = ka.DownChinHeight;
                ModelSpec spec = ArtMapping.Spec(ArtMapping.KaijuModel(w.Kaiju.State, (w.Tick - hatchTick) / hz));
                float h = (spec != null && spec.Height > 0 ? spec.Height : 5f) * ka.Scale;
                string game = Path.Combine(_dir, "kaiju-ko-" + st.Name + "-gameplay.png");
                Texture2D full = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(game, full.EncodeToPNG());
                Destroy(full);
                string side = Path.Combine(_dir, "kaiju-ko-" + st.Name + "-side.png");
                bool sideOk = SideShot(w, ka.DrawnYaw, side);
                ShowHud();
                bool ok = Reached(st.Expect, weight, pitch, chin, h);
                if (ok) reached++;
                Log(st.Name + (ok ? " REACHED" : " NOT REACHED") + " rootPitch=" + F(pitch, "0.0") + " chinHeight=" + F(chin, "0.000")
                    + " weight=" + F(weight, "0.000") + " expect=" + st.Expect.ToString().ToLowerInvariant()
                    + " band=" + F(KaijuDownPose.RootPitchMinDeg, "0") + ".." + F(KaijuDownPose.RootPitchMaxDeg, "0") + "deg chin "
                    + F(KaijuDownPose.HeadRestShare * h, "0.00") + "+-" + F(KaijuDownPose.HeadBandShare * h, "0.00")
                    + " tick=" + w.Tick + " koIn=" + F(w.Kaiju.KoIn, "0.0") + " files=" + Path.GetFileName(game) + (sideOk ? "," + Path.GetFileName(side) : ""));
            }
            if (cam != null) cam.ClearOverride();
            Log("done: " + reached + "/" + Plan.Length + " REACHED; stills in " + _dir);
            yield return Quit(scale, 0);
        }

        IEnumerator Quit(double scale, int code)
        {
            AutoplayArgs.ClockHeld = false;
            _driver.TimeScale = scale;
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(code);
        }

        /// <summary>A low camera beside the Kaiju (90 degrees off its drawn facing), looking at its body; UI culled.</summary>
        bool SideShot(World w, float yawDeg, string path)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) return false;
            Kaiju k = w.Kaiju;
            double gy = GroundHeight.At(w.Map, k.X, k.Z);
            Vector3 body = SimSpace.ToWorld(k.X, gy, k.Z) + Vector3.up * SideLookLift;
            Vector3 dir = Quaternion.Euler(0f, yawDeg + 90f, 0f) * Vector3.forward;
            Vector3 pos = SimSpace.ToWorld(k.X, gy, k.Z) + dir * SideDistance + Vector3.up * SideHeight;
            Transform t = cam.transform;
            Vector3 p0 = t.position;
            Quaternion r0 = t.rotation;
            int mask = cam.cullingMask;
            RenderTexture old = cam.targetTexture, prev = RenderTexture.active;
            RenderTexture rt = RenderTexture.GetTemporary(ShotW, ShotH, 24);
            bool ok = false;
            try
            {
                cam.cullingMask = mask & ~(1 << 5);   // the UI layer
                t.position = pos;
                t.rotation = Quaternion.LookRotation(body - pos, Vector3.up);
                if (_fx != null) _fx.Rebuild(t.right, t.up);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(ShotW, ShotH, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, ShotW, ShotH), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Destroy(tex);
                ok = true;
            }
            catch (Exception ex) { Log("side shot failed: " + ex.Message); }
            finally
            {
                cam.cullingMask = mask;
                cam.targetTexture = old;
                RenderTexture.active = prev;
                t.position = p0;
                t.rotation = r0;
                RenderTexture.ReleaseTemporary(rt);
            }
            return ok;
        }
    }
}
