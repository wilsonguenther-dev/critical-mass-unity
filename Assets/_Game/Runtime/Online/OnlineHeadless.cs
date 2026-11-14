using System;
using CriticalMass.Net.Relay;
using UnityEngine;

namespace CriticalMass.Runtime.Online
{
    /// <summary>
    /// online-rooms-2/3: the built game as a hidden test peer. With -onlineHost or -onlineJoin CODE, and either -batchmode
    /// or -onlineHeadless, the player hosts or joins a room on the relay, plays the match with the autoplay pilot, writes
    /// the fingerprints (tick 0, tick 1, every 30th) to -onlineHashLog, prints ROOM CODE / ROOM READY / ONLINE SETUP /
    /// ONLINE SUMMARY lines to the player log, and quits with HeadlessPeer's exit code (0 ok, 2 desync or setup mismatch,
    /// 3 room closed or host left, 4 timeout or nobody joined, 5 could not create or join). Same HeadlessPeer code as the
    /// console peer (tools/online/HeadlessPeer).
    /// <para>online-rooms-3: the peer is stepped HERE, on the main thread, once per frame (it was a worker thread, which
    /// stepped the online world at the same time as the scene's offline match stepped its own: the Sim's static scratch
    /// buffers made the two corrupt each other, the tick 30 desync and the index error of the first real test). While
    /// Active, the scene's SimDriver does not tick at all, so the online world is the only world that moves.</para>
    /// </summary>
    public sealed class OnlineHeadless : MonoBehaviour
    {
        /// <summary>true when this process is a hidden online test peer (set before the first scene loads).</summary>
        public static bool Active { get; private set; }

        static HeadlessPeer _peer;
        static readonly System.Diagnostics.Stopwatch Clock = new System.Diagnostics.Stopwatch();
        bool _quitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            bool wanted = false, headless = Application.isBatchMode;
            foreach (string a in args)
            {
                string f = a.TrimStart('-').ToLowerInvariant();
                if (f == "onlinehost" || f == "onlinejoin") wanted = true;
                if (f == "onlineheadless") headless = true;
            }
            if (!wanted || !headless) return;
            Active = true;
            LocalSeat.OnlineLive = true;
            Application.runInBackground = true;
            CriticalMass.Runtime.Game.FrameGovernor.SetHeadlessPeerFrameRate(200);   // the peer steps once a frame: keep frames short
            HeadlessPeer peer = HeadlessPeer.FromArgs(args, OnlineConfig.RelayUrl);
            peer.Build = OnlineConfig.Build;
            peer.Log = s => Debug.Log("[online] " + s);
            if (string.IsNullOrEmpty(peer.RelayUrl))
            {
                Debug.LogError("[online] ONLINE ERROR no relay address (pass -onlineRelay URL or set CM_RELAY_URL)");
                Application.Quit(5);
                return;
            }
            _peer = peer;
            var go = new GameObject("Online Headless Peer");
            DontDestroyOnLoad(go);
            go.AddComponent<OnlineHeadless>();
        }

        void Start()
        {
            Clock.Restart();
            if (!_peer.Begin(0)) Quit(_peer.ExitCode);
        }

        void Update()
        {
            using var __ab = CriticalMass.Runtime.Game.AllocBy.Upd(this);
            if (_quitting || _peer == null) return;
            int? done;
            try { done = _peer.Step(Clock.ElapsedMilliseconds); }
            catch (Exception e) { Debug.LogError("[online] ONLINE ERROR " + e); done = 5; }
            if (done.HasValue) Quit(done.Value);
        }

        void Quit(int code)
        {
            _quitting = true;
            Debug.Log("[online] quitting with " + code);
            Application.Quit(code);
        }
    }
}
