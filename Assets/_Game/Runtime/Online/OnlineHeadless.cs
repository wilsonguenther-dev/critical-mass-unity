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
            Application.runInBackground = true;
            CriticalMass.Runtime.Game.FrameGovernor.SetHeadlessPeerFrameRate(200);   // the peer steps once a frame: keep frames short
            HeadlessPeer peer = HeadlessPeer.FromArgs(args, OnlineConfig.RelayUrl);
            peer.Build = OnlineConfig.Build;   // b12-w: -onlineBuild is read by FromArgs but the player always sends its own commit
            peer.Log = s =>
            {
                Debug.Log("[online] " + s);
                if (s.StartsWith("ONLINE SETUP", StringComparison.Ordinal)) Debug.Log(VersionLine("start"));   // match start
            };
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

        /// <summary>
        /// b12-w: one line at room join and one at match start, the same format as the console peer
        /// (tools/online/HeadlessPeer), so a Windows and a Mac log can be compared by eye or by
        /// tools/online/crossplay-check.py: "[CM.Online] build=1.0.900 config=0123456789ABCDEF protocol=3
        /// platform=windows/x64 runtime=mono netbuild=a1b2c3d phase=join". build is the stamped version, netbuild what the
        /// host compares (the commit: a host refuses a joiner whose netbuild differs, Room.cs Hello), config the
        /// ConfigHash the match start compares (a client whose rules differ refuses with a plain reason).
        /// Reflection over the config: called twice per process, never per frame.
        /// </summary>
        public static string VersionLine(string phase)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string config = CriticalMass.Net.ConfigHash.Of(CriticalMass.Sim.Config.GameConfig.CreateDefault()).ToString("X16", inv);
            string os = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows) ? "windows"
                : System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX) ? "macos"
                : System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux) ? "linux" : "other";
            string arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
            string runtime = Type.GetType("Mono.Runtime") != null ? "mono" : "dotnet" + Environment.Version.Major.ToString(inv);
            return "[CM.Online] build=" + CriticalMass.Runtime.Game.BuildInfo.Version + " config=" + config +
                   " protocol=" + CriticalMass.Net.NetVersion.Protocol.ToString(inv) + " platform=" + os + "/" + arch +
                   " runtime=" + runtime + " netbuild=" + OnlineConfig.Build + " phase=" + phase;
        }

        void Start()
        {
            Clock.Restart();
            Debug.Log(VersionLine("join"));   // b12-w: before the peer creates or joins the room
            if (!_peer.Begin(0)) Quit(_peer.ExitCode);
        }

        void Update()
        {
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
