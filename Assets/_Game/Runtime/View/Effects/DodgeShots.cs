using System;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>
    /// CM b13 item J capture (flag -dodgeShots, added by DodgeView): watches the running match and saves one picture of
    /// each moment the first time it happens: "dodge-streak" (a fighter mid-dash, streak drawn), "dodge-slam" (a dodge that
    /// starts inside a Kaiju slam telegraph circle) and "jump-lowwall" (an airborne fighter over a short wall cell). Prints
    /// "[CM.DodgeShots] &lt;name&gt; REACHED &lt;file&gt;" when saved and "[CM.DodgeShots] &lt;name&gt; NOT REACHED" for the rest when
    /// the match ends or the app quits. Pictures go to -shotsOut &lt;dir&gt; (else persistentDataPath/dodge-shots). View only.
    /// </summary>
    public sealed class DodgeShots : MonoBehaviour
    {
        static readonly string[] Names = { "dodge-streak", "dodge-slam", "jump-lowwall" };
        readonly bool[] _done = new bool[3];
        bool _reported;
        string _dir;
        SimDriverBehaviour _driver;

        void Start()
        {
            _driver = GetComponentInParent<SimDriverBehaviour>();
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-shotsOut");
            _dir = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(CriticalMass.Runtime.Game.GamePaths.PersistentRoot, "dodge-shots");
            Directory.CreateDirectory(_dir);
            Debug.Log("[CM.DodgeShots] watching; pictures to " + _dir);
        }

        void LateUpdate()
        {
            World w = _driver != null ? _driver.World : null;
            if (w == null || _reported) return;
            if (w.Match.Phase == MatchPhase.Ended) { Report(); return; }
            double alpha = _driver.Alpha;
            Kaiju k = w.Kaiju;
            for (int id = 0; id < w.ActorCount; id++)
            {
                Actor a = w.Actors[id];
                if (!a.Alive) continue;
                double p = Dodge.Phase(w, id, alpha);
                if (!_done[0] && p > 0.3 && p < 0.8) Shoot(0);
                if (!_done[1] && p >= 0 && p < 0.5 && k.State != KaijuState.Egg && k.TelegraphIn > 0)
                {
                    double dx = w.X[id] - k.TelegraphX, dz = w.Z[id] - k.TelegraphZ, r = w.Config.Kaiju.SlamRadius + 3.5;
                    if (dx * dx + dz * dz <= r * r) Shoot(1);
                }
                if (!_done[2] && a.AirLen > 0)
                {
                    int cx = (int)Math.Floor(w.X[id]), cz = (int)Math.Floor(w.Z[id]);
                    if (MapGrid.InBounds(w.Map, cx, cz) && w.Map.Cells[cz * w.Map.Width + cx] == Cell.LowWall) Shoot(2);
                }
            }
            if (_done[0] && _done[1] && _done[2]) Report();
        }

        void Shoot(int n)
        {
            _done[n] = true;
            string file = Path.Combine(_dir, Names[n] + ".png");
            ScreenCapture.CaptureScreenshot(file);
            Debug.Log("[CM.DodgeShots] " + Names[n] + " REACHED " + file);
        }

        void Report()
        {
            if (_reported) return;
            _reported = true;
            for (int n = 0; n < Names.Length; n++) if (!_done[n]) Debug.Log("[CM.DodgeShots] " + Names[n] + " NOT REACHED");
        }

        void OnApplicationQuit() => Report();
        void OnDestroy() => Report();
    }
}
