using System;
using System.Diagnostics;
using CriticalMass.Net;
using CriticalMass.Net.Relay;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.Online
{
    /// <summary>
    /// online-rooms-2: where the room service lives. Not a secret (anyone may create a room), but it names the owner's
    /// account subdomain, so it is set by flag, environment or the one constant below after the owner deploys the relay.
    /// Never shown on screen.
    /// </summary>
    public static class OnlineConfig
    {
        /// <summary>The deployed relay, for example "https://cm-rooms.NAME.workers.dev" (empty until the owner deploys it).</summary>
        public const string DefaultRelayUrl = "";

        public static string RelayUrl
        {
            get
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-onlineRelay") return args[i + 1];
                string env = Environment.GetEnvironmentVariable("CM_RELAY_URL");
                if (!string.IsNullOrEmpty(env)) return env;
                string saved = PlayerPrefs.GetString("cm.online.relay", "");
                return !string.IsNullOrEmpty(saved) ? saved : DefaultRelayUrl;
            }
        }

        public static bool Configured => !string.IsNullOrEmpty(RelayUrl);

        /// <summary>What two games must share to play together: the build's commit (Mac and Windows builds of one commit match).</summary>
        public static string Build
        {
            get
            {
                string c = BuildInfo.Commit ?? "";
                int plus = c.IndexOf('+');
                if (plus >= 0) c = c.Substring(0, plus);
                return c.Length > 0 ? c : BuildInfo.Version;
            }
        }
    }

    /// <summary>
    /// online-rooms-2: the one online room this game is in (or none). The room screen fills it; OnlineRunner keeps it
    /// ticking every frame, through scene reloads, from the first lobby message to the match's last tick; the match driver
    /// (SimDriverBehaviour) shows Session.World instead of making its own.
    /// </summary>
    public static class OnlineMatch
    {
        public static RelayTransport Transport { get; private set; }
        public static RoomHost Host { get; private set; }
        public static RoomClient Client { get; private set; }
        public static LockstepSession Session { get; private set; }
        public static string Code { get; private set; }
        public static bool InRoom => Host != null || Client != null;
        /// <summary>The match driver's input hook: fills this machine's player's frame (null = neutral frames).</summary>
        public static Action<InputFrame> LocalInput;
        /// <summary>Raised after every lockstep tick (the driver forwards it as its own Ticked).</summary>
        public static event Action<World> Ticked;

        static readonly Stopwatch Clock = Stopwatch.StartNew();
        public static long NowMs => Clock.ElapsedMilliseconds;
        static readonly InputFrame Neutral = SimInput.EmptyInput();

        public static void HostRoom(RelayTransport t, string code, string name)
        {
            Leave();
            Transport = t;
            Code = code;
            Host = new RoomHost(t, GameConfig.CreateDefault(), name, OnlineConfig.Build);
            OnlineRunner.Ensure();
        }

        public static void JoinRoom(RelayTransport t, string code, string name)
        {
            Leave();
            Transport = t;
            Code = code;
            Client = new RoomClient(t, GameConfig.CreateDefault(), name, OnlineConfig.Build);
            Client.Started = Begin;
            OnlineRunner.Ensure();
        }

        /// <summary>Host: starts the match for everyone (null and a reason when it cannot yet).</summary>
        public static string StartAsHost()
        {
            if (Host == null) return "You are not hosting a room.";
            if (!Host.CanStart(out string why)) return why;
            LockstepSession s = Host.Start(NowMs);
            if (s == null) return "The match could not start. Check the teams.";
            Begin(s);
            return null;
        }

        static void Begin(LockstepSession s)
        {
            Session = s;
            s.Sampler = Sample;
            s.Ticked = w => Ticked?.Invoke(w);
            LocalSeat.Set(s.LocalActor);
            HumanSlot me = s.Setup.Humans[s.LocalSlot];
            MatchSetupChoice c = MatchSetupChoice.Default;
            c.TeamSize = s.Setup.TeamSize;
            c.Team = s.World.Actors[s.LocalActor].Team;
            c.Class = me.Class;
            c.Pace = LockstepSession.WallPace(s.Setup, s.World.Config);
            // The match scene loads around the session's world; the session keeps ticking through the reload.
            GameFlow.BeginMatchWithSeed(c, s.Setup.Seed != 0 ? Math.Abs(s.Setup.Seed) : 1, false);
        }

        static void Sample(World w, int actor, int tick, InputFrame into)
        {
            if (LocalInput != null) LocalInput(into);
            else
            {
                into.CopyFrom(Neutral);
                into.AimX = double.NaN;
                into.AimZ = double.NaN;
            }
        }

        /// <summary>One frame of network work (OnlineRunner calls it).</summary>
        public static void Update()
        {
            long now = NowMs;
            if (Host != null) Host.Update(now);
            else if (Client != null) Client.Update(now);
        }

        /// <summary>Leave the room (idempotent): the socket closes, the seat goes back to actor 0.</summary>
        public static void Leave()
        {
            Transport?.Close();
            Transport = null;
            Host = null;
            Client = null;
            Session = null;
            Code = null;
            LocalInput = null;
            LocalSeat.Reset();
        }
    }

    /// <summary>Keeps OnlineMatch ticking every frame across scene loads (one hidden object, only while in a room).</summary>
    public sealed class OnlineRunner : MonoBehaviour
    {
        static OnlineRunner _one;

        public static void Ensure()
        {
            if (_one != null) return;
            var go = new GameObject("Online Runner");
            DontDestroyOnLoad(go);
            _one = go.AddComponent<OnlineRunner>();
        }

        void Update()
        {
            if (!OnlineMatch.InRoom) return;
            OnlineMatch.Update();
        }

        void OnApplicationQuit() => OnlineMatch.Leave();
    }
}
