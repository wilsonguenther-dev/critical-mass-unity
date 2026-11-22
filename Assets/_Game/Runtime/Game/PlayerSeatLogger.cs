// Player-seat telemetry, Unity side (px lane T, 2026-11-22). Docs: docs/testing/PLAYER-SEAT-TELEMETRY.md.
// The shared recorder is CriticalMass.Sim.Diagnostics.PlayerSeatLog (plain C#); this file adds the wall clock, the
// PlayerFeedback ledger (every cue the local player was really shown) and a file under persistentDataPath.
using System;
using System.IO;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Diagnostics;
using UnityEngine;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// The hook point (always compiled, so release builds keep the one hook line in SimDriverBehaviour.Tick): raised after
    /// every single-player tick with the world and the frame actor LocalSeat.Actor was given (null while on autopilot).
    /// Null unless the development logger below is on: the cost in a release build is one static load per tick.
    /// Subscribers read only; they never write the world or the frame.
    /// </summary>
    public static class PlayerSeatLogHook
    {
        public static Action<World, InputFrame> AfterTick;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>
    /// Records the local player's every input edge, what it resolved to in the sim, and every feedback cue it raised (the
    /// PlayerFeedback ledger) to Application.persistentDataPath/PlayerSeatLogs/&lt;utc stamp&gt;-&lt;class&gt;.jsonl, one file
    /// per match. Editor and development builds only (compiled out of release builds), off by default. On with the
    /// command-line flag -playerSeatLog, the PlayerPrefs key cm.dev.playerSeatLog = 1 (Editor menu Critical Mass / Dev /
    /// Player-seat log), or PlayerSeatLogger.Enable() from a test or a debug console. Off: no object exists and nothing runs.
    /// </summary>
    public sealed class PlayerSeatLogger : MonoBehaviour
    {
        public const string PrefKey = "cm.dev.playerSeatLog";
        public const string Flag = "-playerSeatLog";

        private static PlayerSeatLogger _instance;

        /// <summary>The logger is running.</summary>
        public static bool Active => _instance != null;

        /// <summary>The file of the match being recorded (null when none).</summary>
        public static string CurrentPath { get; private set; }

        private World _world;
        private PlayerSeatLog _log;
        private Sink _sink;
        private int _cueCursor;
        private bool _ended;

        private sealed class Sink : IPlayerSeatLogSink
        {
            private readonly StreamWriter _w;
            private int _n;
            public Sink(string path) { _w = new StreamWriter(path, false) { NewLine = "\n" }; }
            public void WriteLine(string json)
            {
                _w.WriteLine(json);
                if (++_n % 64 == 0) _w.Flush();
            }
            public void Close() => _w.Dispose();
        }

        /// <summary>Pure: should the logger run for these command-line args and this PlayerPrefs value (Editor / dev builds only).</summary>
        public static bool Wanted(string[] args, int prefValue)
        {
            if (!(Application.isEditor || Debug.isDebugBuild)) return false;
            if (args != null) foreach (string a in args) if (a == Flag) return true;
            return prefValue == 1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Wanted(Environment.GetCommandLineArgs(), PlayerPrefs.GetInt(PrefKey, 0))) Enable();
        }

        public static void Enable()
        {
            if (_instance != null) return;
            var go = new GameObject("PlayerSeatLogger") { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<PlayerSeatLogger>();
        }

        public static void Disable()
        {
            if (_instance == null) return;
            PlayerSeatLogger keep = _instance;
            keep.Finish();
            _instance = null;
            if (Application.isPlaying) Destroy(keep.gameObject);
            else DestroyImmediate(keep.gameObject);
        }

        /// <summary>The per-frame entry; with the logger off it returns at once and allocates nothing (PlayerSeatLoggerTests).</summary>
        public static void Pump()
        {
            if (_instance == null) return;
            _instance.PumpCues();
        }

        private void OnEnable() => PlayerSeatLogHook.AfterTick += OnTick;

        private void OnDisable()
        {
            PlayerSeatLogHook.AfterTick -= OnTick;
            Finish();
        }

        private void OnApplicationQuit() => Finish();

        private void LateUpdate() => PumpCues();

        private void OnTick(World w, InputFrame seatFrame)
        {
            if (w == null) return;
            if (!ReferenceEquals(w, _world)) Begin(w);
            PumpCues();
            _log.Clock(Time.realtimeSinceStartupAsDouble * 1000.0, Time.frameCount);
            _log.Observe(w, seatFrame);
            if (w.Match.Phase == MatchPhase.Ended && !_ended)
            {
                _ended = true;
                Finish();
            }
        }

        private void Begin(World w)
        {
            Finish();
            _world = w;
            _ended = false;
            int seat = LocalSeat.Actor;
            string dir = Path.Combine(Application.persistentDataPath, "PlayerSeatLogs");
            Directory.CreateDirectory(dir);
            CurrentPath = Path.Combine(dir, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + w.Actors[seat].Cls + ".jsonl");
            _sink = new Sink(CurrentPath);
            _log = new PlayerSeatLog(seat, _sink) { Source = "unity", Mode = "live", Label = Application.version + " " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name };
            _log.Begin(w);
            _cueCursor = PlayerFeedback.Head;
            Debug.Log("[CM.PlayerSeatLog] recording the local player to " + CurrentPath);
        }

        private void PumpCues()
        {
            if (_log == null) return;
            int head = PlayerFeedback.Head;
            if (head < _cueCursor) _cueCursor = 0;   // the ledger was cleared (match load)
            int from = Math.Max(_cueCursor, head - PlayerFeedback.Capacity);
            for (int i = from; i < head; i++)
            {
                FeedbackEntry e = PlayerFeedback.At(i);
                _log.AddCue(e.Channel.ToString(), e.Actor, e.Value, e.Reason, e.SimTick, e.RealTime * 1000.0, e.Frame);
            }
            _cueCursor = head;
        }

        private void Finish()
        {
            if (_log == null) return;
            PumpCues();
            _log.Finish(_world);
            _sink.Close();
            Debug.Log("[CM.PlayerSeatLog] wrote " + _log.RecordsWritten + " records to " + CurrentPath);
            _log = null;
            _sink = null;
            _world = null;
        }
    }
#endif
}
