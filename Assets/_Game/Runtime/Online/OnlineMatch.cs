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
        public const string DefaultRelayUrl = "https://cm-rooms.driviaclip.workers.dev";   // integration batch 10: the owner deployed it

        public static string RelayUrl
        {
            get
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-onlineRelay") return args[i + 1];
                string env = Environment.GetEnvironmentVariable("CM_RELAY_URL");
                if (!string.IsNullOrEmpty(env)) return env;
                string saved = CmPrefs.GetString("cm.online.relay", "");
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
        public static ITransport Transport { get; private set; }
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

        // ---------------------------------------------------------------- b15-zero-cost: direct and local-network play
        /// <summary>The host's direct listener (local network and typed address), null when it could not open a port.</summary>
        public static DirectHost Direct { get; private set; }
        static LanBeacon _beacon;
        static int _beaconVersion = -1;
        static LanListener _lan;
        /// <summary>The relay's count of today's requests (-1 unknown), fetched once per hosted room.</summary>
        public static double RelayUsedToday { get; private set; } = -1;
        static System.Threading.Tasks.Task<double> _usage;

        /// <summary>Joiner side: rooms heard on this computer's local network (empty when none, or the port is busy).</summary>
        public static System.Collections.Generic.List<LanGame> LanGames()
        {
            if (_lan == null) _lan = LanListener.TryStart();
            return _lan != null ? _lan.Games() : new System.Collections.Generic.List<LanGame>();
        }

        public static void StopLanListening() { _lan?.Dispose(); _lan = null; }

        /// <summary>
        /// Opens the host's direct listener next to the relay room (relay may be null: a room for local-network and
        /// address joiners only, for when the relay is unreachable or full for the day). Returns the host's transport.
        /// </summary>
        public static ITransport HostTransport(ITransport relay)
        {
            DirectHost d = null;
            try { d = DirectHost.Start(DirectHost.DefaultPort, false); }
            catch (RoomException) { if (relay == null) throw; }
            Direct = d;
            return d != null ? new HostMux(relay, d) : relay;
        }

        /// <summary>The host's plain line about free ways in: the local address, and the day's relay allowance.</summary>
        public static string HostWaysLine()
        {
            var sb = new System.Text.StringBuilder();
            if (Direct != null)
            {
                System.Collections.Generic.List<string> addrs = DirectAddress.LocalAddresses();
                if (addrs.Count > 0) sb.Append("On the same network they can also type " + addrs[0] + (Direct.Port != DirectHost.DefaultPort ? ":" + Direct.Port : "") + ". ");
            }
            if (Transport is HostMux mux && mux.Relay != null && !mux.RelayUp) sb.Append("The online room service closed; network friends can still join. ");
            else if (Code != null && RelayUsedToday >= 0) sb.Append(RelayBudget.HostLine(Host != null ? Math.Max(2, Host.Members.Count) : 2, RelayUsedToday));
            return sb.ToString().Trim();
        }

        public static void HostRoom(ITransport t, string code, string name)
        {
            DirectHost keep = Direct;
            Direct = null;
            Leave();
            Direct = keep;
            Transport = t;
            Code = code;
            Host = new RoomHost(t, GameConfig.CreateDefault(), name, OnlineConfig.Build) { LookFor = CriticalMass.Runtime.Progression.LookRegistry.Local };   // b15 profile: cosmetic look, outside the sim
#if CM_MOVES_ON
            Host.MovesMask = MovesConfig.PlayBuildAllOn(new MovesConfig()).Mask();   // class moves (integrator): the play build hosts with every switch on; START carries it
#endif
            if (Direct != null)
            {
                try { _beacon = new LanBeacon(Direct.Port, code, name, 1, OnlineConfig.Build); } catch (Exception) { _beacon = null; }
            }
            RelayUsedToday = -1;
            _usage = code != null ? RelayTransport.FetchRequestsTodayAsync(OnlineConfig.RelayUrl, System.Threading.CancellationToken.None) : null;
            OnlineRunner.Ensure();
        }

        public static void JoinRoom(ITransport t, string code, string name)
        {
            Leave();
            Transport = t;
            Code = code;
            Client = new RoomClient(t, GameConfig.CreateDefault(), name, OnlineConfig.Build) { LookFor = CriticalMass.Runtime.Progression.LookRegistry.Local };   // b15 profile
            Client.Started = Begin;
            _joinName = name;
            _joinTries = 0;
            _retryAt = -1;
            OnlineRunner.Ensure();
        }

        // online-rooms-3: a joiner who is quicker than the host's own connection hears "no room" / "still opening": ask
        // again a few times before showing the error (the host's game may connect a second later).
        const int JoinTries = 6, JoinRetryMs = 1500;
        static string _joinName;
        static int _joinTries;
        static long _retryAt = -1;

        /// <summary>true inside an online match (the pause menu keeps the match running and offers no restart).</summary>
        public static bool InMatch => Session != null;

        /// <summary>true while a join is being asked again (the room screen shows "Connecting" instead of the error).</summary>
        public static bool Retrying => _retryAt >= 0;
        /// <summary>true when the closed join is still going to be asked again on the next update.</summary>
        public static bool WillRetry => Client != null && Client.Session == null && Client.Phase == RoomPhase.Closed
                                        && RelayTransport.IsRetryable(Client.Error) && _joinTries < JoinTries;

        static void RetryJoin(long now)
        {
            bool relay = Transport is RelayTransport || (Transport is FallbackTransport f && f.Current is RelayTransport);
            if (Client == null || Client.Session != null || Client.Phase != RoomPhase.Closed || !relay || Code == null) return;
            if (_retryAt < 0)
            {
                if (!RelayTransport.IsRetryable(Client.Error) || _joinTries >= JoinTries) return;
                _joinTries++;
                _retryAt = now + JoinRetryMs;
                return;
            }
            if (now < _retryAt) return;
            _retryAt = -1;
            try
            {
                Transport?.Close();
                RelayTransport t = RelayTransport.Connect(OnlineConfig.RelayUrl, Code, null);
                Transport = t;
                Client = new RoomClient(t, GameConfig.CreateDefault(), _joinName, OnlineConfig.Build) { Started = Begin, LookFor = CriticalMass.Runtime.Progression.LookRegistry.Local };
            }
            catch (RoomException) { /* keeps the closed client and its error */ }
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
            if (Host != null)
            {
                Host.Update(now);
                if (_beacon != null && Host.Version != _beaconVersion && Direct != null)
                {
                    _beaconVersion = Host.Version;
                    _beacon.Update(Direct.Port, Code, Host.Members.Count > 0 ? Host.Members[0].Name : "", Host.Members.Count, OnlineConfig.Build);
                }
                if (_usage != null && _usage.IsCompleted) { RelayUsedToday = _usage.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? _usage.Result : -1; _usage = null; }
            }
            else if (Client != null) { Client.Update(now); RetryJoin(now); }
        }

        /// <summary>Leave the room (idempotent): the socket closes, the seat goes back to actor 0.</summary>
        public static void Leave()
        {
            Transport?.Close();
            Transport = null;
            _beacon?.Dispose();
            _beacon = null;
            _beaconVersion = -1;
            Direct = null;   // closed with the transport (HostMux closes its listener)
            _usage = null;
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
            OnlineChatHud.Ensure();
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
