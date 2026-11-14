using System;
using System.Threading;
using CriticalMass.Net.Relay;
using UnityEngine;

namespace CriticalMass.Runtime.Online
{
    /// <summary>
    /// online-rooms-2: the built game as a hidden test peer. With -onlineHost or -onlineJoin CODE, and either -batchmode or
    /// -onlineHeadless, the player hosts or joins a room on the relay, plays the match with the autoplay pilot, writes a
    /// fingerprint per 30 ticks to -onlineHashLog, prints "ROOM CODE ..." and "ONLINE SUMMARY ..." to the player log, and
    /// quits with HeadlessPeer's exit code (0 ok, 2 desync, 3 room closed or host left, 4 timeout, 5 could not create or
    /// join). The same HeadlessPeer code as the console peer (tools/online/HeadlessPeer), so a mismatch between a game log
    /// and a console log is a runtime difference (Mono player against .NET 8), the exact thing this exists to catch.
    /// The peer runs on a worker thread (it touches only the Sim and the socket); this object only waits and quits.
    /// Without those flags nothing happens. Flags: see tools/online/README.md.
    /// </summary>
    public sealed class OnlineHeadless : MonoBehaviour
    {
        static volatile int _exit = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
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
            HeadlessPeer peer = HeadlessPeer.FromArgs(args, OnlineConfig.RelayUrl);
            peer.Build = OnlineConfig.Build;
            peer.Log = s => Debug.Log("[online] " + s);
            if (string.IsNullOrEmpty(peer.RelayUrl))
            {
                Debug.LogError("[online] ONLINE ERROR no relay address (pass -onlineRelay URL or set CM_RELAY_URL)");
                Application.Quit(5);
                return;
            }
            var go = new GameObject("Online Headless Peer");
            DontDestroyOnLoad(go);
            go.AddComponent<OnlineHeadless>();
            var t = new Thread(() =>
            {
                int code;
                try { code = peer.Run(); }
                catch (Exception e) { Debug.LogError("[online] ONLINE ERROR " + e.GetType().Name + ": " + e.Message); code = 5; }
                _exit = code;
            }) { IsBackground = true, Name = "online-headless-peer" };
            t.Start();
        }

        void Update()
        {
            int code = _exit;
            if (code >= 0) Application.Quit(code);
        }
    }
}
