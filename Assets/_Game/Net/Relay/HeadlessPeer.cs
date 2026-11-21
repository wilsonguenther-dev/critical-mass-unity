using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Net.Relay
{
    /// <summary>
    /// A whole online peer with no screen: joins or hosts a room on the relay, readies up, plays the match with the
    /// autoplay pilot as its human, and writes the fingerprint of tick 0, tick 1 and every 30th tick to a log ("tick hash"
    /// per line) so a harness can compare machines line by line. Used by the console peer (tools/online/HeadlessPeer, no
    /// Unity) and by the game's -onlineHost / -onlineJoin flags (Runtime/Online/OnlineHeadless.cs): the same code on both.
    /// <para>online-rooms-3: driven by Begin() then Step(now) from ONE thread, the thread that owns the Sim (the game's main
    /// thread; the Sim keeps static scratch buffers and must never be stepped from two threads at once, which is what made
    /// the first game-to-game test drift at tick 30). Run() is the console loop around the same two calls.</para>
    /// Exit codes: 0 the match ran to the end (or to Ticks, or this peer dropped itself on purpose), 2 desync or a setup
    /// mismatch, 3 the room closed or the host left, 4 timed out (also: nobody joined the host in LobbySeconds), 5 could
    /// not create or join.
    /// </summary>
    public sealed class HeadlessPeer
    {
        public string RelayUrl;
        public bool Host;
        public string Code;
        public string Key;
        public string Name = "Headless";
        public TeamId Team = TeamId.Azure;
        public ClassId Class = ClassId.Warrior;
        /// <summary>Host: start once this many humans (the host included) are in and ready.</summary>
        public int Humans = 2;
        /// <summary>Stop after this many ticks (0 = the whole match).</summary>
        public int Ticks;
        public int TeamSize = 16;
        public double Pace = 1;
        public int Seed;
        public string HashLogPath;
        /// <summary>Host: the room code is written here once the host is connected ("room ready"), for a harness.</summary>
        public string CodeOutPath;
        public int TimeoutSeconds = 900;
        /// <summary>Host: give up (exit 4) if the room is not full and ready this long after it opened.</summary>
        public int LobbySeconds = 180;
        /// <summary>Joiner: how many times to ask again when the room is not there yet (the host may still be connecting).</summary>
        public int JoinTries = 8;
        public int JoinRetryMs = 1500;
        /// <summary>Send ChatText to everyone when the match reaches this tick (-1 = never).</summary>
        public int ChatAt = -1;
        public string ChatText = "hello from the headless peer";
        /// <summary>online-rooms-9 (-onlineChatTeam): the scripted chat line goes to the peer's team only.</summary>
        public bool ChatTeam;
        /// <summary>Drop off the network with no goodbye at this tick (-1 = never), to test the bot handover.</summary>
        public int DropAt = -1;
        /// <summary>b15-rejoin (-rejoin-after MS): after DropAt, wait this long, connect again with the same seat secret, replay
        /// the match from the host's log and take the fighter back (-1 = stay dropped). Its fingerprints after the rejoin
        /// (tick 0 to the end, the replayed ticks included) go to HashLogPath + ".rejoin".</summary>
        public int RejoinAfterMs = -1;
        public string Build = "headless";
        public Action<string> Log = Console.WriteLine;
        /// <summary>Tests: use this in-process relay instead of the network (same rules as server/relay).</summary>
        public LocalRelay Local;
        /// <summary>b15-zero-cost (-direct-port P), host: also take direct joiners (local network, typed address) on TCP port P
        /// (0 = any free port; -1 = off). The room is the same room: relay and direct players play one match.</summary>
        public int DirectPort = -1;
        /// <summary>b15-zero-cost (-no-relay), host: direct joiners only; no relay room is made (costs the relay nothing).</summary>
        public bool NoRelay;
        /// <summary>b15-zero-cost (-direct HOST:PORT), joiner: connect straight to the host; if it cannot be reached and a
        /// room code is given too, join the same room through the relay instead.</summary>
        public string DirectAddr;
        /// <summary>b15-zero-cost (-lan), host: announce the room on the local network (LanBeacon).</summary>
        public bool Lan;
        /// <summary>b15-zero-cost: counts what this peer sends and receives (the relay leg only, on a host that has one).</summary>
        public MeteredTransport Meter => _meter;
        /// <summary>b15-zero-cost: Meter's counts when the match started (lobby = before, match = after).</summary>
        public TrafficCounts SentAtStart { get; private set; }
        public TrafficCounts ReceivedAtStart { get; private set; }
        /// <summary>b15-zero-cost: "relay", "direct" or "relay+direct" (a host taking both).</summary>
        public string PathUsed { get; private set; } = "relay";
        /// <summary>b15-zero-cost: the TCP port the direct listener got (0: none).</summary>
        public int DirectPortBound => _direct != null ? _direct.Port : 0;
        public DirectHost DirectListener => _direct;
        /// <summary>b15-zero-cost, tests: the lockstep tuning for this peer's session (null = the shipped defaults;
        /// LockstepOptions.PreB15() = the sending before b15, for before/after measurements).</summary>
        public LockstepOptions Options;

        public int ExitCode { get; private set; } = -1;
        /// <summary>Every fingerprint so far (tick 0 first), for tests.</summary>
        public IReadOnlyList<KeyValuePair<int, ulong>> Hashes => _hashes;

        private GameConfig _cfg;
        private ITransport _t;
        private MeteredTransport _meter;
        private ITransport _relayRaw;
        private DirectHost _direct;
        private LanBeacon _beacon;
        private RoomHost _room;
        private RoomClient _client;
        private LockstepSession _session;
        private readonly List<KeyValuePair<int, ulong>> _hashes = new List<KeyValuePair<int, ulong>>();
        private AutoplayPilot _pilot;
        private bool _picked, _readyLogged, _chatSent, _dropped;
        private bool _dropDone, _liveLogged;
        private long _rejoinAt = -1, _droppedAtMs = -1;
        private ulong _secret;
        private readonly List<KeyValuePair<int, ulong>> _rejoinHashes = new List<KeyValuePair<int, ulong>>();
        /// <summary>b15-rejoin: the fingerprints of the rejoined session, for tests.</summary>
        public IReadOnlyList<KeyValuePair<int, ulong>> RejoinHashes => _rejoinHashes;
        /// <summary>b15-rejoin: the session (the rejoined one after a rejoin), for tests.</summary>
        public LockstepSession Session => _session;
        // b12 (X2 follow-up): one "ONLINE LEAD" line per second of match (30 ticks at the shipped 30 Hz) so a log shows how the
        // adaptive lead moved, not only the SUMMARY's range. LeadPolicy/LockstepSession already expose these; no wire change.
        private const int LeadLineEveryTicks = 30;
        private int _nextLeadLine;
        private int _tries, _chatSeen;
        private long _start, _deadline, _retryAt = -1, _lingerUntil = -1;

        /// <summary>The console loop: Begin, then Step until done (one thread throughout).</summary>
        public int Run()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            if (!Begin(0)) return ExitCode;
            while (true)
            {
                int? done = Step(clock.ElapsedMilliseconds);
                if (done.HasValue) return done.Value;
                Thread.Sleep(2);
            }
        }

        /// <summary>Creates or joins the room. False (with ExitCode set) when that already failed.</summary>
        public bool Begin(long now)
        {
            _cfg = GameConfig.CreateDefault();
            _start = now;
            _deadline = now + TimeoutSeconds * 1000L;
            _pilot = new AutoplayPilot((uint)(Host ? 101 : 202) ^ (uint)(Name ?? "").Length);
            try
            {
                if (Host && !NoRelay && string.IsNullOrEmpty(Code))
                {
                    (string newCode, string hostKey) = Local != null ? Local.CreateRoom()
                        : RelayTransport.CreateRoomAsync(RelayUrl, CancellationToken.None).GetAwaiter().GetResult();
                    Code = newCode;
                    Key = hostKey;
                }
                if (Host && NoRelay) Code = null;
                else
                {
                    Code = RelayTransport.CleanCode(Code);
                    if (Code == null && (Host || string.IsNullOrEmpty(DirectAddr))) return Fail(5, "that is not a room code");
                }
                if (Host) Log("ROOM CODE " + (Code ?? "none (direct only)"));
                Connect();
                return true;
            }
            catch (RoomException e) { return Fail(5, e.Message); }
            catch (Exception e) { return Fail(5, e.ToString()); }
        }

        private ITransport RelayLeg(bool host)
        {
            _relayRaw = Local != null ? Local.Connect(Code, host ? Key : null) : RelayTransport.Connect(RelayUrl, Code, host ? Key : null);
            return _relayRaw;
        }

        private void Connect()
        {
            if (Host)
            {
                // b15-zero-cost: the relay leg (metered: what the relay bills), plus the direct listener when asked for.
                ITransport relay = NoRelay ? null : (_meter = new MeteredTransport(RelayLeg(true)));
                if (DirectPort >= 0 || NoRelay)
                {
                    if (_direct == null)
                    {
                        _direct = DirectHost.Start(DirectPort >= 0 ? DirectPort : DirectHost.DefaultPort, relay == null);
                        Log("ROOM DIRECT port " + _direct.Port + " addresses " + string.Join(",", DirectAddress.LocalAddresses()));
                    }
                    if (relay == null) _meter = new MeteredTransport(_direct);
                    _t = relay != null ? new HostMux(relay, _direct) : (ITransport)_meter;
                    PathUsed = relay != null ? "relay+direct" : "direct";
                    if (Lan && _beacon == null) _beacon = new LanBeacon(_direct.Port, Code, Name, Humans, Build);
                }
                else { _t = relay; PathUsed = "relay"; }
            }
            else if (!string.IsNullOrEmpty(DirectAddr) && DirectAddress.TryParse(DirectAddr, out string dh, out int dp))
            {
                Func<ITransport> viaRelay = Code == null ? (Func<ITransport>)null : () => RelayLeg(false);
                _t = _meter = new MeteredTransport(new FallbackTransport(DirectClient.Connect(dh, dp), viaRelay));
                PathUsed = "direct";
            }
            else { _t = _meter = new MeteredTransport(RelayLeg(false)); PathUsed = "relay"; }
            if (Host)
            {
                _room = new RoomHost(_t, _cfg, Name, Build) { TeamSize = TeamSize, Pace = Pace };
                return;
            }
            if (_secret != 0) { _client = new RoomClient(_t, _cfg, Name, Build, _secret) { Options = Options }; return; }   // b15-rejoin: ask for the seat back
            _client = new RoomClient(_t, _cfg, Name, Build) { Options = Options };
            _client.SetPick(Team, Class);
            _client.SetReady(true);
        }

        /// <summary>One step: null while running, else the exit code (the transport is closed and the logs written).</summary>
        public int? Step(long now)
        {
            if (ExitCode >= 0) return ExitCode;
            try { return StepInner(now); }
            catch (Exception e) { Fail(5, e.ToString()); return Finish(); }
        }

        private int? StepInner(long now)
        {
            if (_lingerUntil >= 0)
            {
                if (_room != null) _room.Update(now); else _client?.Update(now);
                if (now < _lingerUntil) return null;
                ExitCode = _session != null && _session.Status == LockstepStatus.Desynced ? 2 : 0;
                return Finish();
            }
            if (now > _deadline) { Fail(4, "timed out after " + TimeoutSeconds + " s"); return Finish(); }
            if (_retryAt >= 0)
            {
                if (now < _retryAt) return null;
                _retryAt = -1;
                Connect();
            }
            if (_rejoinAt >= 0)
            {
                // b15-rejoin: back after the drop: a new connection, the same seat secret.
                if (now < _rejoinAt) return null;
                _rejoinAt = -1;
                _tries = 0;
                Connect();
                _dropped = false;
                Log($"ONLINE REJOINING after {now - _droppedAtMs} ms");
            }
            if (Host)
            {
                if (!_picked && _t.LocalPeer == Peers.Host)
                {
                    _room.SetLocalPick(Team, Class);
                    _picked = true;
                    Log("ROOM READY " + Code);
                    if (!string.IsNullOrEmpty(CodeOutPath)) File.WriteAllText(CodeOutPath, (Code ?? "direct") + "\n");
                }
                _room.Update(now);
                if (_session == null && _room.Members.Count >= Humans && _room.CanStart(out _))
                {
                    _session = _room.Start(now, Options, Seed);
                    if (_session != null) Hook(_session);
                }
                if (_session == null && _room.Phase == RoomPhase.Closed) { Fail(3, _room.Notice); return Finish(); }
                if (_session == null && now - _start > LobbySeconds * 1000L)
                {
                    Fail(4, $"nobody joined: {_room.Members.Count} of {Humans} in the room after {LobbySeconds} s");
                    return Finish();
                }
            }
            else
            {
                _client.Update(now);
                if (_client.Phase == RoomPhase.Closed)
                {
                    if (_client.Session == null && RelayTransport.IsRetryable(_client.Error) && ++_tries < JoinTries)
                    {
                        Log($"ONLINE RETRY {_tries} of {JoinTries - 1}: {_client.Error}");
                        _t.Close();
                        _retryAt = now + JoinRetryMs;
                        return null;
                    }
                    Fail(_client.Session == null ? 5 : 3, _client.Error);
                    return Finish();
                }
                if (_session == null && _client.Session != null) { _session = _client.Session; Hook(_session); }
            }
            ChatLog();
            if (_session == null) return null;
            if (_session.Status == LockstepStatus.Desynced)
            {
                Log(_session.SetupMismatch != null ? "ONLINE SETUP MISMATCH " + _session.SetupMismatch + " (" + _session.SetupMismatchDetail + ")" : "ONLINE DESYNC at tick " + _session.DesyncTick);
                ExitCode = 2;
                return Finish();
            }
            if (_session.Status == LockstepStatus.HostLeft) { Fail(3, _session.ClosedReason ?? "the host left"); return Finish(); }
            if (ChatAt >= 0 && !_chatSent && _session.Stepped >= ChatAt)
            {
                _chatSent = true;
                string why = Chat?.Say(ChatText, ChatTeam ? ChatScope.Team : ChatScope.All, now);
                Log("ONLINE CHAT SENT" + (ChatTeam ? " TEAM" : "") + " at tick " + _session.Stepped + (why != null ? " refused: " + why : ""));
            }
            if (_session.Stepped >= _nextLeadLine)
            {
                _nextLeadLine = _session.Stepped + LeadLineEveryTicks;
                Log($"ONLINE LEAD tick {_session.Stepped} lead {_session.Lead} leadmin {_session.LeadMin} leadmax {_session.LeadMax} late {_session.LateInputs}");
            }
            if (_session.IsRejoin && !_liveLogged && _session.RejoinLiveTick >= 0)
            {
                _liveLogged = true;
                Log($"ONLINE REJOINED live at tick {_session.RejoinLiveTick} after {_session.RejoinLiveMs - _session.RejoinStartMs} ms in the match " +
                    $"({now - _droppedAtMs} ms after the drop): log {_session.LogBytes} B on the wire for {_session.LogRawBytes} B of bundles, " +
                    $"{_session.RejoinVerified} log fingerprints checked, catch-up stepping {_session.CatchUpStepMs:F0} ms");
            }
            if (DropAt >= 0 && !_dropDone && _session.Stepped >= DropAt)
            {
                _dropped = true;
                _dropDone = true;
                Log("ONLINE DROPPED on purpose at tick " + _session.Stepped);
                AbortLink();   // the socket dies, as a crashed game's does
                if (RejoinAfterMs >= 0 && _client != null)
                {
                    _secret = _client.Secret;
                    _session = null;
                    _client = null;
                    _droppedAtMs = now;
                    _rejoinAt = now + RejoinAfterMs;
                    return null;
                }
                ExitCode = 0;
                return Finish();
            }
            if (_session.Status == LockstepStatus.Ended || (Ticks > 0 && _session.Stepped >= Ticks))
            {
                // Keep answering for two seconds so the others get the last bundles and fingerprints.
                _lingerUntil = now + 2000;
            }
            return null;
        }

        /// <summary>This peer's chat (the host's routing channel, or a joiner's), null before Begin. b11-b: public for the relay tests.</summary>
        public ChatChannel Chat => _room != null ? _room.Chat : _client?.Chat;
        /// <summary>b11-b: the peer number the relay gave this connection (-1 before it connects).</summary>
        public int LocalPeer => _t != null ? _t.LocalPeer : -1;

        private void ChatLog()
        {
            ChatChannel ch = Chat;
            if (ch == null) return;
            IReadOnlyList<ChatLine> lines = ch.Lines;
            // Lines are kept in a ring of ChatChannel.Keep; log everything new since the last look.
            int total = ch.Version;
            int fresh = Math.Min(total - _chatSeen, lines.Count);
            for (int i = lines.Count - fresh; i < lines.Count; i++)
                if (i >= 0) Log($"ONLINE CHAT {(lines[i].Scope == ChatScope.System ? "notice" : lines[i].From)}: {lines[i].Text}");
            _chatSeen = total;
        }

        /// <summary>Drops this peer's own connection with no goodbye (relay or direct), for the drop and rejoin tests.</summary>
        private void AbortLink()
        {
            ITransport u = _t is MeteredTransport m ? m.Inner : _t;
            if (u is FallbackTransport f) u = f.Current;
            (u as RelayTransport)?.Abort();
            (u as DirectClient)?.Abort();
            // b15-zero-cost: the in-process relay closes the socket as the real one does when a game's connection dies (its
            // TCP reset reaches Cloudflare): the seat's socket leaves the room. Before, it stayed, so a full 32-human room
            // refused the rejoin ("That room is full."), which the real relay does not.
            (u as LocalRelayTransport)?.Close();
        }

        /// <summary>b15-zero-cost: "ONLINE TRAFFIC ..." lines: what this peer sent and received in the lobby and in the match,
        /// and per minute of match (each sent message is one message the relay bills, on the relay path).</summary>
        private void TrafficLog()
        {
            if (_meter == null) return;
            if (_t is MeteredTransport mt && mt.Inner is FallbackTransport ft) PathUsed = ft.Path;
            TrafficCounts lobbyS = SentAtStart ?? _meter.Sent, lobbyR = ReceivedAtStart ?? _meter.Received;
            TrafficCounts matchS = _meter.Sent.Since(SentAtStart ?? _meter.Sent), matchR = _meter.Received.Since(ReceivedAtStart ?? _meter.Received);
            double sec = _session != null ? _session.Stepped / Math.Max(1e-9, _cfg.SimHz * LockstepSession.WallPace(_session.Setup, _cfg)) : 0;
            double perMin = sec > 0 ? 60.0 / sec : 0;
            Log($"ONLINE TRAFFIC peer {LocalPeer} path {PathUsed} lobby sent {lobbyS.TotalMessages} msg {lobbyS.TotalBytes} B recv {lobbyR.TotalMessages} msg {lobbyR.TotalBytes} B");
            Log($"ONLINE TRAFFIC peer {LocalPeer} path {PathUsed} match {sec:F1} s sent {matchS.TotalMessages} msg {matchS.TotalBytes} B ({matchS.Describe()}) recv {matchR.TotalMessages} msg {matchR.TotalBytes} B");
            Log($"ONLINE TRAFFIC peer {LocalPeer} path {PathUsed} per-minute sent {matchS.TotalMessages * perMin:F0} msg {matchS.TotalBytes * perMin:F0} B recv {matchR.TotalMessages * perMin:F0} msg {matchR.TotalBytes * perMin:F0} B" +
                (_session != null ? $" packet-ticks {_session.TicksPerPacket}" : ""));
            if (_t is HostMux hm && hm.RelayLostReason != null) Log("ONLINE RELAY LOST, the room went on direct: " + hm.RelayLostReason);
        }

        private void Hook(LockstepSession s)
        {
            if (_meter != null) { SentAtStart = _meter.Sent.Copy(); ReceivedAtStart = _meter.Received.Copy(); }
            s.Sampler = _pilot.Fill;
            _hashes.Add(new KeyValuePair<int, ulong>(0, s.MyWorldHash0));
            s.HashRecorded = (tick, h) => _hashes.Add(new KeyValuePair<int, ulong>(tick, h));
            s.HumanToBot += (slot, actor, tick) => Log($"ONLINE TOBOT slot {slot} actor {actor} at tick {tick}");   // += : the room posts its notice too
            s.BotToHuman += (slot, actor, tick) => Log($"ONLINE TOHUMAN slot {slot} actor {actor} at tick {tick}");   // b15-rejoin
            if (s.IsRejoin)
            {
                // b15-rejoin: the rejoined session's own log (tick 0 to the end); the first session's stays in _hashes.
                _rejoinHashes.Add(new KeyValuePair<int, ulong>(0, s.MyWorldHash0));
                s.HashRecorded = (tick, h) => { _hashes.Add(new KeyValuePair<int, ulong>(tick, h)); _rejoinHashes.Add(new KeyValuePair<int, ulong>(tick, h)); };
            }
            Log($"ONLINE SETUP peer {_t.LocalPeer} actor {s.LocalActor} humans {s.Setup.Humans.Length} seed {s.Setup.Seed} " +
                $"config 0x{s.MyConfigHash:X16} world0 0x{s.MyWorldHash0:X16} host config 0x{s.Setup.ConfigHash:X16} host world0 0x{s.Setup.WorldHash0:X16}");
        }

        /// <summary>online-rooms-9: stall milliseconds by the slot waited for ("1=840ms,2=12ms"; a client waits on slot 0, the host).</summary>
        private string Waits()
        {
            long[] w = _session?.StallBySlotMs;
            if (w == null) return "";
            var sb = new StringBuilder();
            for (int k = 0; k < w.Length; k++) { if (sb.Length > 0) sb.Append(','); sb.Append(k).Append('=').Append(w[k]).Append("ms"); }
            return sb.ToString();
        }

        private bool Fail(int code, string why)
        {
            Log("ONLINE ERROR " + why);
            ExitCode = code;
            return false;
        }

        private int Finish()
        {
            if (_session != null)
                Log($"ONLINE SUMMARY peer {(_t != null ? _t.LocalPeer : -1)} actor {_session.LocalActor} ticks {_session.Stepped} final 0x{StateHash.Of(_session.World):X16} " +
                    $"lead {_session.Lead} late {_session.LateInputs} stall {_session.StallMs} ms sent {_session.BytesSent} B status {_session.Status} pings {Pings()} " +
                    $"leadmin {_session.LeadMin} leadmax {_session.LeadMax} waits {Waits()}");   // online-rooms-9: lead range and stall by slot
            if (!string.IsNullOrEmpty(HashLogPath))
            {
                var sb = new StringBuilder();
                foreach (KeyValuePair<int, ulong> kv in _hashes) sb.Append(kv.Key).Append(' ').Append(kv.Value.ToString("X16")).Append('\n');
                File.WriteAllText(HashLogPath, sb.ToString());
                if (_rejoinHashes.Count > 0)
                {
                    sb.Clear();
                    foreach (KeyValuePair<int, ulong> kv in _rejoinHashes) sb.Append(kv.Key).Append(' ').Append(kv.Value.ToString("X16")).Append('\n');
                    File.WriteAllText(HashLogPath + ".rejoin", sb.ToString());
                }
            }
            TrafficLog();
            _beacon?.Dispose();
            if (_t != null && !_dropped) { _t.Close(); (_relayRaw as RelayTransport)?.WaitClosed(1500); }
            Log("ONLINE EXIT " + ExitCode);
            return ExitCode;
        }

        /// <summary>"peer=ms" for everyone in the room, as the host measured it (round trip through the relay).</summary>
        private string Pings()
        {
            IReadOnlyList<RoomMember> members = _room != null ? (IReadOnlyList<RoomMember>)_room.Members : _client?.Members;
            if (members == null || members.Count == 0) return "none";
            var sb = new StringBuilder();
            foreach (RoomMember m in members) sb.Append(sb.Length > 0 ? "," : "").Append(m.Peer).Append('=').Append(m.PingMs).Append("ms");
            return sb.ToString();
        }

        /// <summary>Reads the shared flags (the same names for the console peer and the game player).</summary>
        public static HeadlessPeer FromArgs(string[] args, string defaultRelay)
        {
            var p = new HeadlessPeer { RelayUrl = Environment.GetEnvironmentVariable("CM_RELAY_URL") ?? defaultRelay };
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (a.TrimStart('-').ToLowerInvariant())
                {
                    case "onlinehost": case "host": p.Host = true; break;
                    case "onlinejoin": case "join": p.Code = next; i++; break;
                    case "onlinecode": case "code": p.Code = next; i++; break;
                    case "onlinekey": case "key": p.Key = next; i++; break;
                    case "onlinerelay": case "relay": p.RelayUrl = next; i++; break;
                    case "onlinename": case "name": p.Name = next; i++; break;
                    case "onlineteam": case "team": p.Team = (next ?? "").ToLowerInvariant().StartsWith("c") ? TeamId.Crimson : TeamId.Azure; i++; break;
                    case "onlineclass": case "class":
                        foreach (string cn in Enum.GetNames(typeof(ClassId)))
                            if (string.Equals(cn, next, StringComparison.OrdinalIgnoreCase)) { p.Class = (ClassId)Enum.Parse(typeof(ClassId), cn); break; }
                        i++;
                        break;
                    case "onlinehumans": case "humans": int.TryParse(next, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out p.Humans); i++; break;
                    case "onlineticks": case "ticks": int.TryParse(next, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out p.Ticks); i++; break;
                    case "onlineteamsize": case "teamsize": int.TryParse(next, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out p.TeamSize); i++; break;
                    case "onlineseed": case "seed": int.TryParse(next, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out p.Seed); i++; break;
                    case "onlinepace": case "pace": double.TryParse(next, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out p.Pace); i++; break;
                    case "onlinehashlog": case "hash-log": p.HashLogPath = next; i++; break;
                    case "onlinecodeout": case "code-out": p.CodeOutPath = next; i++; break;
                    case "onlinetimeout": case "timeout": int.TryParse(next, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out p.TimeoutSeconds); i++; break;
                    case "onlinelobby": case "lobby": int.TryParse(next, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out p.LobbySeconds); i++; break;
                    case "onlinejointries": case "join-tries": int.TryParse(next, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out p.JoinTries); i++; break;
                    case "onlinechatat": case "chat-at": int.TryParse(next, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out p.ChatAt); i++; break;
                    case "onlinechattext": case "chat-text": p.ChatText = next; i++; break;
                    case "onlinechatteam": case "chat-team": p.ChatTeam = true; break;
                    case "onlinedropat": case "drop-at": int.TryParse(next, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out p.DropAt); i++; break;
                    case "onlinerejoinafter": case "rejoin-after": int.TryParse(next, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out p.RejoinAfterMs); i++; break;
                    case "onlinebuild": case "build": p.Build = next; i++; break;
                    case "onlinedirectport": case "direct-port": int.TryParse(next, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out p.DirectPort); i++; break;
                    case "onlinenorelay": case "no-relay": p.NoRelay = true; break;
                    case "onlinedirect": case "direct": p.DirectAddr = next; i++; break;
                    case "onlinelan": case "lan": p.Lan = true; break;
                }
            }
            return p;
        }
    }
}
