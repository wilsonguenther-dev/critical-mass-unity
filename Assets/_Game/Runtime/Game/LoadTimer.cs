using System.Diagnostics;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// perf/start-hitch-1: names the parts of the scene-load frame (the 400 to 450 ms frame at 0.25 s of a match).
    /// The load call stamps the time, GameBootstrap stamps the scene's arrival, each big Start() adds its own part, and
    /// the first Update after the activation frame (every Start() of that frame has run by then) writes one line:
    /// "[CM.Load] scene start ...". The first ticks after a load get their own line. Fixed arrays, no per-frame work
    /// beyond one bool check; the two strings are built once per load.
    /// </summary>
    public static class LoadTimer
    {
        public const int MaxParts = 32;
        public const int TicksWatched = 3;

        static readonly string[] Names = new string[MaxParts];
        static readonly double[] Ms = new double[MaxParts];
        static readonly double[] TickMs = new double[TicksWatched];
        static readonly double ToMs = 1000.0 / Stopwatch.Frequency;
        static int _count;
        static long _loadIssued;   // 0: no load call stamped (the first scene of the process)
        static long _sceneLoaded;
        static bool _armed;
        static bool _arrived;   // the new scene is in: the old scene's last Update never reports
        static int _ticks = TicksWatched;
        static int _loads;

        /// <summary>The last scene-start line (also kept in the editor, where nothing is logged).</summary>
        public static string LastReport { get; private set; }
        public static string LastTicks { get; private set; }

        public static long Now() => Stopwatch.GetTimestamp();

        /// <summary>Right before SceneManager.LoadScene: the old scene's teardown and the load count from here.</summary>
        public static void LoadIssued()
        {
            // Armed from the load call, so sceneLoaded handlers that run before GameBootstrap's (music, announcer) count.
            _loadIssued = Now();
            _count = 0;
            _armed = true;
            _arrived = false;
        }

        /// <summary>The new scene has arrived (sceneLoaded, before its Start() calls).</summary>
        public static void SceneLoaded(long t)
        {
            _sceneLoaded = t;
            if (_loadIssued == 0 || _arrived) _count = 0;   // the boot scene, or a second arrival without a load call
            _armed = true;
            _arrived = true;
            _ticks = 0;
        }

        /// <summary>Adds the time since t0 to the named part (same name: summed). Only between a load and its report.</summary>
        public static void Part(string name, long t0)
        {
            if (!_armed) return;
            double ms = (Now() - t0) * ToMs;
            for (int i = 0; i < _count; i++)
                if (ReferenceEquals(Names[i], name) || Names[i] == name) { Ms[i] += ms; return; }
            if (_count >= MaxParts) return;
            Names[_count] = name;
            Ms[_count] = ms;
            _count++;
        }

        public static bool Armed => _armed;

        /// <summary>From SimDriverBehaviour.Update: the activation frame's Start() calls are done; report once.</summary>
        public static void FirstUpdate()
        {
            if (!_armed || !_arrived) return;
            _armed = false;
            long now = Now();
            double toUpdate = (now - _sceneLoaded) * ToMs;
            double outIn = _loadIssued != 0 && _loadIssued <= _sceneLoaded ? (_sceneLoaded - _loadIssued) * ToMs : -1;
            LastReport = Format(_loads++, outIn, toUpdate, Names, Ms, _count);
            _loadIssued = 0;
            if (!Application.isEditor) Debug.Log(LastReport);
        }

        /// <summary>From SimDriverBehaviour.Tick: the first ticks after a load, each timed.</summary>
        public static bool WatchingTicks => _ticks < TicksWatched;

        public static void TickDone(long t0)
        {
            if (_ticks >= TicksWatched) return;
            TickMs[_ticks++] = (Now() - t0) * ToMs;
            if (_ticks < TicksWatched) return;
            var sb = new StringBuilder("[CM.Load] first ticks after the load:");
            for (int i = 0; i < TicksWatched; i++) sb.Append(' ').Append(TickMs[i].ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(" ms");
            LastTicks = sb.ToString();
            if (!Application.isEditor) Debug.Log(LastTicks);
        }

        /// <summary>The report line: the parts largest first, then what no part explains.</summary>
        public static string Format(int load, double outInMs, double toUpdateMs, string[] names, double[] ms, int count)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder(256);
            sb.Append("[CM.Load] scene start ").Append(load == 0 ? "(boot)" : "(load " + load + ")");
            if (outInMs >= 0) sb.Append(": load call to scene arrival ").Append(outInMs.ToString("0", ci)).Append(" ms (old scene out, new scene in)");
            sb.Append("; scene arrival to first update ").Append(toUpdateMs.ToString("0", ci)).Append(" ms:");
            var used = new bool[count];
            double named = 0;
            for (int n = 0; n < count; n++)
            {
                int best = -1;
                for (int i = 0; i < count; i++) if (!used[i] && (best < 0 || ms[i] > ms[best])) best = i;
                used[best] = true;
                named += ms[best];
                sb.Append(n == 0 ? " " : ", ").Append(names[best]).Append(' ').Append(ms[best].ToString("0.0", ci));
            }
            // Parts can nest (hud stages inside the boot build), so "unnamed" is a floor, never negative.
            sb.Append("; unnamed ").Append(System.Math.Max(0, toUpdateMs - named).ToString("0", ci));
            return sb.ToString();
        }
    }
}
