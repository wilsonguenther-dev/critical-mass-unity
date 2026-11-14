using System;
using System.Collections;
using System.Globalization;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>
    /// Capture only (integration batch 10, for fix/movement-shiver-1): `-kaijuBridgeShots [dir]` stages the owner's spot from
    /// MovementShiverTests in the live match: the Kaiju hatched for the other team at the last cell its body fits beside the
    /// north-east end of the hill's ring wall, the player pinned on the diagonal bridge 3.6 m away, everyone else benched.
    /// Eight frames with the nav guard on, then eight with it off (the old walk), each a "[CM.KaijuBridge]" log line with the
    /// Kaiju's position. Writes the sim, so never in a real match: only with the flag.
    /// </summary>
    public sealed class KaijuBridgeShots : MonoBehaviour
    {
        public const string Flag = "-kaijuBridgeShots";
        const double KaijuX = 68.5, KaijuZ = 30.5, BridgeX = 71.5, BridgeZ = 27.5;

        SimDriverBehaviour _driver;
        string _dir;
        bool _pin;
        int _target;

        public static bool Requested =>
            Array.FindIndex(Environment.GetCommandLineArgs(), s => string.Equals(s, Flag, StringComparison.OrdinalIgnoreCase)) >= 0;

        static void Log(string s) => Debug.Log("[CM.KaijuBridge] " + s);
        static string F(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            if (_driver == null) { enabled = false; return; }
            string[] a = Environment.GetCommandLineArgs();
            int i = Array.FindIndex(a, s => string.Equals(s, Flag, StringComparison.OrdinalIgnoreCase));
            _dir = i >= 0 && i + 1 < a.Length && !a[i + 1].StartsWith("-") ? a[i + 1] : Path.Combine(GamePaths.DataDir, "kaiju-bridge");
            Directory.CreateDirectory(_dir);
            StartCoroutine(Run());
        }

        void OnDestroy() { if (_driver != null) _driver.Ticked -= OnTick; }

        static bool TitleShowing()
        {
            var t = FindAnyObjectByType<CriticalMass.Runtime.Hud.TitleScreen>();
            return t != null && t.isActiveAndEnabled;
        }

        void OnTick(World w)
        {
            if (!_pin) return;
            w.X[_target] = BridgeX; w.Z[_target] = BridgeZ; w.PrevX[_target] = BridgeX; w.PrevZ[_target] = BridgeZ;
            w.Vx[_target] = 0; w.Vz[_target] = 0;
            Actor a = w.Actors[_target];
            a.SpawnProtect = 0;
            a.Hp = a.MaxHp;
        }

        IEnumerator WaitTicks(World w, int n)
        {
            int end = w.Tick + n;
            float t0 = Time.realtimeSinceStartup;
            while (w.Tick < end && Time.realtimeSinceStartup - t0 < 30f) yield return null;
        }

        IEnumerator Run()
        {
            float t0 = Time.realtimeSinceStartup;
            while (_driver.World == null || _driver.World.Match.Phase != MatchPhase.Playing || _driver.World.Match.Time < 3 || TitleShowing())
            {
                if (Time.realtimeSinceStartup - t0 > 120f) { Log("no match started; giving up"); Application.Quit(3); yield break; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            World w = _driver.World;
            int hz = (int)Math.Round(w.Config.SimHz);
            _target = 0;
            TeamId owner = w.Actors[0].Team == TeamId.Azure ? TeamId.Crimson : TeamId.Azure;
            SimResources.ApplyDelivery(w, owner, CarryKind.Biomass, 1);
            w.ForceHatch = true;
            float h0 = Time.realtimeSinceStartup;
            while (!w.Match.Hatched && Time.realtimeSinceStartup - h0 < 20f) yield return null;
            if (!w.Match.Hatched) { Log("the Kaiju did not hatch; giving up"); Application.Quit(4); yield break; }
            yield return WaitTicks(w, hz * 10);   // past the hatchling
            _driver.Ticked += OnTick;
            for (int pass = 0; pass < 2; pass++)
            {
                bool guard = pass == 0;
                w.Config.Physics.KaijuNavGuard = guard;
                for (int id = 0; id < w.ActorCount; id++)
                {
                    if (id == _target) continue;
                    Actor a = w.Actors[id];
                    a.Alive = false; a.Hp = 0; a.RespawnIn = 1e9; a.Moving = false;
                }
                Kaiju k = w.Kaiju;
                k.X = KaijuX; k.Z = KaijuZ; k.PrevX = KaijuX; k.PrevZ = KaijuZ;
                _pin = true;
                Log("pass guard=" + (guard ? "on" : "off") + " owner=" + owner + " state=" + k.State + " player team=" + w.Actors[0].Team);
                for (int s = 0; s < 8; s++)
                {
                    yield return WaitTicks(w, hz * 3 / 4);
                    yield return new WaitForEndOfFrame();
                    string name = "kb-guard-" + (guard ? "on" : "off") + "-" + s + ".png";
                    ScreenCapture.CaptureScreenshot(Path.Combine(_dir, name));
                    Log(name + " tick=" + w.Tick + " kaiju=(" + F(k.X) + ", " + F(k.Z) + ") target=" + k.TargetId +
                        " player=(" + F(w.X[_target]) + ", " + F(w.Z[_target]) + ")");
                }
            }
            w.Config.Physics.KaijuNavGuard = true;
            _pin = false;
            _driver.Ticked -= OnTick;
            yield return new WaitForSecondsRealtime(1f);
            Log("done");
            Application.Quit(0);
        }
    }
}
