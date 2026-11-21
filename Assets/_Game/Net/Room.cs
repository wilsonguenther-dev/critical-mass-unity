using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Net
{
    /// <summary>Versions both games must share to play together (a different Sim gives different worlds).</summary>
    public static class NetVersion
    {
        /// <summary>Bumps whenever any packet layout or the lockstep rules change.</summary>
        public const int Protocol = 4;   // 4 (b15-rejoin): HELLO carries the sim schema, the rules digest and a rejoin secret; REJOIN / REJOINED   // 3 (online-rooms-9): bundles carry the host's tick and every slot's margin   // 2: the start message carries the config and tick 0 fingerprints
    }

    /// <summary>A transport that can close its room to newcomers (the relay does; the loopback does not need to).</summary>
    public interface ILockableTransport
    {
        void Lock();
    }

    public enum RoomPhase { Connecting, Lobby, InMatch, Closed }

    public sealed class RoomMember
    {
        public int Peer;
        public string Name = "";
        public TeamId Team = TeamId.Azure;
        public ClassId Class = ClassId.Warrior;
        public bool Ready;
        public int PingMs;
        /// <summary>Host side: the member's HELLO arrived and its version matches.</summary>
        public bool Confirmed;
        /// <summary>b15-rejoin, host side: the secret the member sent in its HELLO (only the host ever sees it); a dropped
        /// player who presents it again gets their seat back.</summary>
        public ulong Secret;
        /// <summary>b15 profile: the member's cosmetic look for LookClass (clamped on arrival; never part of the match setup).</summary>
        public CosmeticLook Look = CosmeticLook.Default;
        public ClassId LookClass;
        /// <summary>The look for the class the member has picked now (Default until a look for that class arrives).</summary>
        public CosmeticLook LookNow => LookClass == Class ? Look : CosmeticLook.Default;
    }

    /// <summary>Room message numbers (lockstep uses 1 and 2).</summary>
    internal static class RoomMsg
    {
        public const int Hello = 20, State = 21, Pick = 22, Ready = 23, Reject = 24, Start = 25, Ping = 26, Pong = 27;
        /// <summary>b15-rejoin: a dropped player asks for its seat back (proto, build, schema, digest, secret); the host
        /// answers Rejoined (the match setup with the new peer in the seat) or Reject.</summary>
        public const int Rejoin = 28, Rejoined = 29;
        /// <summary>b15 profile, cosmetic only: client to host [class, look]; host to all [n, (peer, look) x n]. (Integrator b15:
        /// 40/41, because b15-rejoin took 28/29 and 30 to 39 are chat; TrafficCounts counts 40 to 49 as room.)</summary>
        public const int Look = 40, Looks = 41;
        public const int NameMax = 16, BuildMax = 40, TextMax = 120;

        /// <summary>Display names follow the chat rules (letters, digits, space, '-' and '_'; filtered; at most 16).</summary>
        public static string CleanName(string s) => ChatFilter.CleanName(s);
    }

    /// <summary>
    /// The host's side of a room: the authoritative member list (name, team, class, ready, ping), version checks, and the
    /// START that turns the room into a lockstep match. Everything a client sends is a request the host validates.
    /// </summary>
    /// <summary>
    /// online-rooms-9: the room's join and leave notices, the same words on every machine. In the lobby the host posts
    /// them as it sees peers come and go and each joiner posts them from the roster; in a match every machine posts
    /// "NAME left: a bot took over." when the lockstep hands that human's fighter to a bot (the same tick everywhere).
    /// </summary>
    public static class RoomNotices
    {
        public static string Joined(string name) => name + " joined.";
        public static string LeftRoom(string name) => name + " left the room.";
        public static string LeftMatch(string name) => name + " left: a bot took over.";
        /// <summary>b15-rejoin: posted on every machine at the tick a rejoined player takes the fighter back.</summary>
        public static string BackInMatch(string name) => name + " is back.";

        /// <summary>Posts the in-match leave notice on this machine when a human becomes a bot.</summary>
        public static void Hook(LockstepSession s, ChatChannel chat)
        {
            if (s == null || chat == null) return;
            s.HumanToBot += (slot, actor, tick) =>
            {
                string name = slot >= 0 && slot < s.Setup.Humans.Length ? s.Setup.Humans[slot].Name : null;
                chat.Notice(LeftMatch(string.IsNullOrEmpty(name) ? "A player" : name), s.NowMs);
            };
            s.BotToHuman += (slot, actor, tick) =>
            {
                string name = slot >= 0 && slot < s.Setup.Humans.Length ? s.Setup.Humans[slot].Name : null;
                chat.Notice(BackInMatch(string.IsNullOrEmpty(name) ? "A player" : name), s.NowMs);
            };
        }

        /// <summary>A joiner's roster update: notices for who came and went since the last one (none when !announce).
        /// known: peer to name, as of the previous roster (kept by the caller).</summary>
        public static void Roster(Dictionary<int, string> known, List<RoomMember> now, ChatChannel chat, long at, int self, bool announce)
        {
            var seen = new HashSet<int>();
            foreach (RoomMember m in now)
            {
                seen.Add(m.Peer);
                bool isNew = !known.ContainsKey(m.Peer);
                known[m.Peer] = m.Name;
                if (isNew && announce && m.Peer != self && chat != null) chat.Notice(Joined(m.Name), at);
            }
            var left = new List<int>();
            foreach (KeyValuePair<int, string> kv in known) if (!seen.Contains(kv.Key)) left.Add(kv.Key);
            foreach (int p in left)
            {
                string name = known[p];
                known.Remove(p);
                if (announce && p != self && chat != null) chat.Notice(LeftRoom(name), at);
            }
        }
    }

    public sealed class RoomHost
    {
        public readonly List<RoomMember> Members = new List<RoomMember>();
        public int TeamSize = 16;
        /// <summary>The match pace (0 = the game's default speed), the same for everyone in the room.</summary>
        public double Pace;
        public RoomPhase Phase { get; private set; } = RoomPhase.Lobby;
        /// <summary>The last thing that happened, as one plain sentence for the room screen.</summary>
        public string Notice { get; private set; } = "";
        /// <summary>Bumps on every change of the member list (the screen redraws when it changes).</summary>
        public int Version { get; private set; }
        public LockstepSession Session { get; private set; }
        /// <summary>Room and match chat (the host routes and checks every line).</summary>
        public readonly ChatChannel Chat;
        /// <summary>
        /// b15-rejoin: a player who drops may come back to the same match (default on). The relay only lets newcomers into an
        /// unlocked room, so with this on the host does not lock the room at the start; anyone else who knocks during the
        /// match is refused here ("The match already started.") and nothing they send ever reaches the lockstep.
        /// </summary>
        public bool AllowRejoin = true;
        /// <summary>b15-rejoin: the last rejoin request's outcome, one plain sentence (empty: none yet).</summary>
        public string RejoinNotice { get; private set; } = "";
        /// <summary>b15 profile: the host's own look for a class (null = Default). Cosmetic only.</summary>
        public Func<ClassId, CosmeticLook> LookFor;

        private readonly ITransport _t;
        private readonly GameConfig _cfg;
        private readonly string _build;
        private readonly byte[] _pkt = new byte[Peers.MaxPacket];
        private long _lastState = -100000, _lastPing = -100000;
        private bool _dirty = true;

        public RoomHost(ITransport transport, GameConfig cfg, string hostName, string build)
        {
            _t = transport;
            _cfg = cfg;
            _build = build ?? "";
            TeamSize = cfg.TeamSize;
            Members.Add(new RoomMember { Peer = Peers.Host, Name = RoomMsg.CleanName(hostName), Ready = true, Confirmed = true });
            Chat = new ChatChannel(transport)
            {
                NameOf = p => Find(p)?.Name ?? "PLAYER",
                // b11-b: an unknown peer has no team (-1 matches nobody); it used to count as Azure.
                TeamOf = p => { RoomMember m = Find(p); return m != null ? (int)m.Team : -1; },
                Knows = p => { RoomMember m = Find(p); return m != null && m.Confirmed; },
                PeersInRoom = ConfirmedPeers,
                // b17-chat: an All line is one broadcast while nobody in the room is half-joined (else one per member).
                CanBroadcast = AllConfirmed,
            };
            Chat.Voice = new VoiceChannel(transport)
            {
                TeamOf = p => { RoomMember m = Find(p); return m != null ? (int)m.Team : -1; },
                Knows = p => { RoomMember m = Find(p); return m != null && m.Confirmed; },
                InLobby = () => Phase == RoomPhase.Lobby && Session == null,
                Hears = Chat.Hears,
                CanBroadcast = AllConfirmed,
                PeersInRoom = ConfirmedPeers,
            };
        }

        private bool AllConfirmed()
        {
            foreach (RoomMember m in Members) if (!m.Confirmed) return false;
            return true;
        }

        private IEnumerable<int> ConfirmedPeers()
        {
            foreach (RoomMember m in Members) if (m.Confirmed) yield return m.Peer;
        }

        public RoomMember Find(int peer)
        {
            foreach (RoomMember m in Members) if (m.Peer == peer) return m;
            return null;
        }

        /// <summary>b15 profile: the look a member shows for the class it has picked (Default for unknown peers).</summary>
        public CosmeticLook LookOf(int peer)
        {
            RoomMember m = Find(peer);
            return m != null ? m.LookNow : CosmeticLook.Default;
        }

        public void SetLocalPick(TeamId team, ClassId cls)
        {
            RoomMember me = Members[0];
            if (me.Team != team && CountOn(team) >= TeamSize) return;
            me.Team = team;
            me.Class = cls;
            Changed("");
        }

        public int CountOn(TeamId team)
        {
            int n = 0;
            foreach (RoomMember m in Members) if (m.Confirmed && m.Team == team) n++;
            return n;
        }

        /// <summary>True when the host may press Start; otherwise why not, in plain words.</summary>
        public bool CanStart(out string why)
        {
            foreach (RoomMember m in Members)
            {
                if (!m.Confirmed) { why = "Someone is still joining."; return false; }
                if (!m.Ready) { why = m.Name + " is not ready yet."; return false; }
            }
            if (CountOn(TeamId.Azure) > TeamSize || CountOn(TeamId.Crimson) > TeamSize) { why = "A team has too many players."; return false; }
            why = "";
            return Phase == RoomPhase.Lobby;
        }

        public void Update(long now)
        {
            while (Phase == RoomPhase.Lobby && _t.Poll(out TransportEvent ev)) Handle(ref ev, now);
            if (Phase == RoomPhase.InMatch) { Session.Update(now); return; }
            if (Phase != RoomPhase.Lobby) return;
            if (now - _lastPing >= 1000)
            {
                _lastPing = now;
                var w = new WireWriter(_pkt);
                w.U8(RoomMsg.Ping);
                w.I32(unchecked((int)now));
                _t.Send(Peers.Broadcast, _pkt, w.Pos);
            }
            if (_dirty || now - _lastState >= 1000) SendState(now);
        }

        private void Handle(ref TransportEvent ev, long now)
        {
            switch (ev.Kind)
            {
                case TransportEventKind.PeerJoined:
                    if (Find(ev.From) == null && Members.Count < MatchSetup.MaxHumans)
                    {
                        // On the host's team while it has room: friends together against the bots is the default.
                        TeamId t = CountOn(Members[0].Team) < TeamSize ? Members[0].Team : World.OtherTeam(Members[0].Team);
                        Members.Add(new RoomMember { Peer = ev.From, Name = "Joining...", Team = t });
                        Changed("");
                    }
                    return;
                case TransportEventKind.Closed:
                    Phase = RoomPhase.Closed;
                    Changed(string.IsNullOrEmpty(ev.Reason) ? "The connection to the room was lost." : ev.Reason);
                    return;
                case TransportEventKind.PeerLeft:
                    RoomMember gone = Find(ev.From);
                    if (gone != null)
                    {
                        Members.Remove(gone);
                        Changed(gone.Confirmed ? gone.Name + " left the room." : "");
                        if (gone.Confirmed) Chat.Notice(RoomNotices.LeftRoom(gone.Name), now);
                    }
                    return;
                case TransportEventKind.Data:
                    break;
                default:
                    return;
            }
            RoomMember m = Find(ev.From);
            if (m == null || ev.Data == null || ev.Length < 1) return;
            if (ChatWire.Owns(ev.Data[0])) { if (m.Confirmed) Chat.Handle(ref ev, now); return; }   // b17-chat: the whole chat block (30 to 39)
            var r = new WireReader(ev.Data, 1, ev.Length);
            switch (ev.Data[0])
            {
                case RoomMsg.Hello:
                {
                    int proto = r.U8();
                    string build = r.Str(RoomMsg.BuildMax);
                    string name = r.Str(RoomMsg.NameMax);
                    if (r.Bad) return;
                    // b15-rejoin: the protocol first (an older game's HELLO is shorter), then the rules version.
                    string why = proto != NetVersion.Protocol || build != _build ? VersionText(build, _build) : null;
                    int schema = 0;
                    ulong digest = 0, secret = 0;
                    if (why == null)
                    {
                        schema = r.I32();
                        digest = r.U64();
                        secret = r.U64();
                        why = r.Bad ? VersionText(build, _build) : RulesText(schema, digest);
                    }
                    if (why != null)
                    {
                        Reject(ev.From, why);
                        Members.Remove(m);
                        Changed("");
                        return;
                    }
                    m.Secret = secret;
                    m.Name = RoomMsg.CleanName(name);
                    m.Confirmed = true;
                    Changed(m.Name + " joined.");
                    Chat.Notice(RoomNotices.Joined(m.Name), now);
                    return;
                }
                case RoomMsg.Pick:
                {
                    int team = r.U8(), cls = r.U8();
                    if (r.Bad || !m.Confirmed || team > 1 || cls >= ClassIds.All.Count) return;
                    var want = (TeamId)team;
                    if (want != m.Team && CountOn(want) >= TeamSize) return;   // that team is full: the pick is refused
                    m.Team = want;
                    m.Class = (ClassId)cls;
                    Changed("");
                    return;
                }
                case RoomMsg.Ready:
                {
                    int ready = r.U8();
                    if (r.Bad || !m.Confirmed) return;
                    m.Ready = ready != 0;
                    Changed("");
                    return;
                }
                case RoomMsg.Look:
                {
                    // b15 profile: cosmetic only. Clamped on arrival (ids out of range or above the claimed mastery become 0).
                    int cls = r.U8();
                    CosmeticLook look = CosmeticLook.Read(ref r);
                    if (r.Bad || !m.Confirmed || cls >= ClassIds.All.Count) return;
                    if (m.LookClass == (ClassId)cls && m.Look.Equals(look)) return;
                    m.Look = look;
                    m.LookClass = (ClassId)cls;
                    Changed("");
                    return;
                }
                case RoomMsg.Pong:
                {
                    int sent = r.I32();
                    if (r.Bad) return;
                    long rtt = unchecked((int)now - sent);   // both sides of the subtraction wrap the same way
                    if (rtt >= 0 && rtt < 10000) { m.PingMs = m.PingMs <= 0 ? (int)rtt : (3 * m.PingMs + (int)rtt) / 4; }   // online-rooms-9: smoothed, so one hitch (260 ms) does not set the whole match's delay
                    return;
                }
            }
        }

        /// <summary>b15-rejoin: the plain sentence for a different build or protocol.</summary>
        public static string VersionText(string theirs, string ours) =>
            "Your game is a different version from the host's (" + theirs + " vs " + ours + "). Both of you need the same update.";

        /// <summary>b15-rejoin: null when a joiner's sim schema and rules digest are this game's, else the plain sentence.</summary>
        public static string RulesText(int schema, ulong digest) =>
            schema == CriticalMass.Sim.SimVersion.Schema && digest == CriticalMass.Sim.SimVersion.RulesDigest ? null
                : "Your game plays by different match rules from the host's (rules version " + schema + " vs " + CriticalMass.Sim.SimVersion.Schema +
                  "). Both of you need the same update.";

        /// <summary>
        /// b15-rejoin: room messages during the match. Chat as before; REJOIN from a dropped player who presents the secret
        /// of its seat (same build, protocol, schema and rules digest) gets the seat back: the lockstep hands it the match
        /// from tick 0 (LockstepSession.AcceptRejoin) and the host answers REJOINED with the setup, its new peer in the seat.
        /// Anyone else is refused in plain words.
        /// </summary>
        private void InMatch(ref TransportEvent ev, long now)
        {
            if (ev.Data == null || ev.Length < 1) return;
            int type = ev.Data[0];
            if (ChatWire.Owns(type)) { Chat.Handle(ref ev, now); return; }
            if (type == RoomMsg.Hello) { Reject(ev.From, "The match already started."); return; }
            if (type != RoomMsg.Rejoin || Session == null) return;
            var r = new WireReader(ev.Data, 1, ev.Length);
            int proto = r.U8();
            string build = r.Str(RoomMsg.BuildMax);
            string why = r.Bad || proto != NetVersion.Protocol || build != _build ? VersionText(build, _build) : null;
            ulong secret = 0;
            if (why == null)
            {
                int schema = r.I32();
                ulong digest = r.U64();
                secret = r.U64();
                why = r.Bad ? VersionText(build, _build) : RulesText(schema, digest);
            }
            int slot = -1;
            if (why == null && secret != 0)
                for (int i = 1; i < Members.Count && i < Session.Setup.Humans.Length; i++) if (Members[i].Secret == secret) slot = i;
            if (why == null && slot < 0) why = "The match already started.";
            if (why == null) why = Session.AcceptRejoin(slot, ev.From, now);
            if (why != null) { RejoinNotice = why; Reject(ev.From, why); return; }
            Members[slot].Peer = ev.From;
            var w = new WireWriter(new byte[Peers.MaxPacket]);
            w.U8(RoomMsg.Rejoined);
            Session.Setup.Write(ref w);
            if (w.Overflow) return;
            _t.Send(ev.From, w.Buf, w.Pos);
            RejoinNotice = Members[slot].Name + " is rejoining.";
            Changed(RejoinNotice);
        }

        private void Reject(int peer, string text)
        {
            var w = new WireWriter(_pkt);
            w.U8(RoomMsg.Reject);
            w.Str(text, RoomMsg.TextMax);
            _t.Send(peer, _pkt, w.Pos);
        }

        private void Changed(string notice)
        {
            if (!string.IsNullOrEmpty(notice)) Notice = notice;
            _dirty = true;
            Version++;
        }

        private void SendState(long now)
        {
            _dirty = false;
            _lastState = now;
            var w = new WireWriter(_pkt);
            w.U8(RoomMsg.State);
            w.U8(TeamSize);
            int n = 0;
            foreach (RoomMember m in Members) if (m.Confirmed) n++;
            w.U8(n);
            foreach (RoomMember m in Members)
            {
                if (!m.Confirmed) continue;
                w.U8(m.Peer);
                w.Str(m.Name, RoomMsg.NameMax);
                w.U8((int)m.Team);
                w.U8((int)m.Class);
                w.U8(m.Ready ? 1 : 0);
                w.I16(Math.Min(m.PingMs, 9999));
            }
            if (!w.Overflow) _t.Send(Peers.Broadcast, _pkt, w.Pos);
            SendLooks(n);
        }

        /// <summary>b15 profile: every confirmed member's look for its picked class, after each State. Cosmetic only.</summary>
        private void SendLooks(int n)
        {
            RoomMember me = Members[0];
            if (LookFor != null)
            {
                me.Look = CosmeticLook.Clamp(LookFor(me.Class));
                me.LookClass = me.Class;
            }
            var w = new WireWriter(_pkt);
            w.U8(RoomMsg.Looks);
            w.U8(n);
            foreach (RoomMember m in Members)
            {
                if (!m.Confirmed) continue;
                w.U8(m.Peer);
                m.LookNow.Write(ref w);
            }
            if (!w.Overflow) _t.Send(Peers.Broadcast, _pkt, w.Pos);
        }

        /// <summary>
        /// Starts the match: picks the seed, sets the starting input delay from the worst measured ping, seats every human,
        /// sends START (resent by the session until each client answers) and locks the room. Null if it cannot start yet.
        /// </summary>
        /// <summary>
        /// online-rooms-9: the room's starting input delay in ticks: half the worst smoothed round trip plus 60 ms, at
        /// least 100 ms and at most 250 ms. It is the host's own felt delay for the whole match (the host's input never
        /// travels; the delay is the joiners' buffer against jitter on the bundles) and every joiner's starting lead, which
        /// then adapts both ways (LeadPolicy). Before: a whole round trip plus 60 ms up to 400 ms, set by one 260 ms ping.
        /// </summary>
        public static int StartDelay(int worstPingMs, double tickMs) =>
            Math.Max((int)Math.Ceiling(100 / tickMs - 1e-6), Math.Min((int)Math.Ceiling(250 / tickMs - 1e-6), (int)Math.Ceiling((Math.Max(0, worstPingMs) / 2.0 + 60) / tickMs - 1e-6)));

        public LockstepSession Start(long now, LockstepOptions options = null, int seed = 0)
        {
            if (!CanStart(out _)) return null;
            int n = Members.Count;
            var teams = new TeamId[n];
            for (int i = 0; i < n; i++) teams[i] = Members[i].Team;
            var actors = new int[n];
            if (!MatchSetup.AssignActors(TeamSize, Members[0].Team, teams, actors)) return null;
            int worst = 0;
            foreach (RoomMember m in Members) worst = Math.Max(worst, m.PingMs);
            double tickMs = 1000.0 / (_cfg.SimHz * LockstepSession.WallPace(new MatchSetup { Pace = Pace }, _cfg));
            var setup = new MatchSetup
            {
                Seed = seed != 0 ? seed : unchecked((int)(now * 2654435761L) ^ 0x5bd1e995),
                TeamSize = TeamSize,
                HostTeam = Members[0].Team,
                Pace = Pace,
                InputDelay = StartDelay(worst, tickMs),
                HashEvery = 30,
                Humans = new HumanSlot[n],
            };
            for (int i = 0; i < n; i++)
                setup.Humans[i] = new HumanSlot { Peer = Members[i].Peer, Actor = actors[i], Class = Members[i].Class, Name = Members[i].Name };
            // online-rooms-3: the match is built from the setup and a fresh default config only (never a config object an
            // earlier match or the offline game touched). The host builds first, then sends what it built from: the config
            // fingerprint and its tick 0 world fingerprint ride in the start message, and a client that differs refuses.
            Session = new LockstepSession(_t, setup, GameConfig.CreateDefault(), options) { Other = InMatch };
            RoomNotices.Hook(Session, Chat);
            setup.ConfigHash = Session.MyConfigHash;
            setup.WorldHash0 = Session.MyWorldHash0;
            var w = new WireWriter(new byte[Peers.MaxPacket]);
            w.U8(RoomMsg.Start);
            setup.Write(ref w);
            if (w.Overflow) { Session = null; return null; }
            _t.Send(Peers.Broadcast, w.Buf, w.Pos);
            if (!AllowRejoin) (_t as ILockableTransport)?.Lock();
            Session.StartPacket = w.Buf;
            Session.StartPacketLength = w.Pos;
            Phase = RoomPhase.InMatch;
            return Session;
        }
    }

    /// <summary>A joiner's side of a room: says hello, mirrors the host's member list, sends picks, and enters the match on START.</summary>
    public sealed class RoomClient
    {
        private readonly Dictionary<int, string> _announced = new Dictionary<int, string>();   // online-rooms-9: roster notices

        public RoomPhase Phase { get; private set; } = RoomPhase.Connecting;
        public readonly List<RoomMember> Members = new List<RoomMember>();
        public int TeamSize { get; private set; } = 16;
        /// <summary>Why the room ended for us, as one plain sentence (empty while all is well).</summary>
        public string Error { get; private set; } = "";
        public int Version { get; private set; }
        public LockstepSession Session { get; private set; }
        public int LocalPeer => _t.LocalPeer;
        /// <summary>Raised once when the match begins (the screen switches to the game).</summary>
        public Action<LockstepSession> Started;
        public LockstepOptions Options;
        /// <summary>Room and match chat (lines arrive from the host, already checked).</summary>
        public readonly ChatChannel Chat;
        /// <summary>b15 profile: this player's own look for a class (null = Default). Cosmetic only.</summary>
        public Func<ClassId, CosmeticLook> LookFor;
        /// <summary>b15 profile: each member's look for its picked class, as the host last sent them (already clamped).</summary>
        public readonly Dictionary<int, CosmeticLook> Looks = new Dictionary<int, CosmeticLook>();

        /// <summary>b15-rejoin: this player's seat secret (random per room join; sent only to the host). Keep it to rejoin.</summary>
        public ulong Secret { get; }
        /// <summary>b15-rejoin: true when this client asks for a dropped seat back instead of joining the lobby.</summary>
        public bool Rejoining { get; }

        private readonly ITransport _t;
        private readonly GameConfig _cfg;
        private readonly string _name, _build;
        private readonly byte[] _pkt = new byte[256];
        private bool _helloSent;
        private bool _ready;
        private TeamId _team;
        private ClassId _cls;
        private bool _pickSet;
        private long _lastHello = -100000;

        public RoomClient(ITransport transport, GameConfig cfg, string name, string build) : this(transport, cfg, name, build, 0) { }

        /// <summary>b15-rejoin: rejoinSecret != 0 asks the host for the seat that secret belongs to (the match in progress).</summary>
        public RoomClient(ITransport transport, GameConfig cfg, string name, string build, ulong rejoinSecret)
        {
            _t = transport;
            _cfg = cfg;
            _name = RoomMsg.CleanName(name);
            _build = build ?? "";
            Chat = new ChatChannel(transport);
            // b17-chat: lobby push-to-talk (off until this player turns it on); teams from the host's roster.
            Chat.Voice = new VoiceChannel(transport)
            {
                TeamOf = p => { foreach (RoomMember m in Members) if (m.Peer == p) return (int)m.Team; return -1; },
                InLobby = () => Phase == RoomPhase.Lobby && Session == null,
                Hears = Chat.Hears,
            };
            Rejoining = rejoinSecret != 0;
            Secret = rejoinSecret != 0 ? rejoinSecret : NewSecret();
        }

        private static ulong NewSecret()
        {
            var b = new byte[8];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(b);
            ulong v = System.BitConverter.ToUInt64(b, 0);
            return v != 0 ? v : 1;
        }

        public RoomMember Me
        {
            get
            {
                foreach (RoomMember m in Members) if (m.Peer == _t.LocalPeer) return m;
                return null;
            }
        }

        public void SetPick(TeamId team, ClassId cls)
        {
            _team = team;
            _cls = cls;
            _pickSet = true;
            var w = new WireWriter(_pkt);
            w.U8(RoomMsg.Pick);
            w.U8((int)team);
            w.U8((int)cls);
            _t.Send(Peers.Host, _pkt, w.Pos);
            SendLook(cls);
        }

        /// <summary>b15 profile: the look a member shows (Default for an unknown peer).</summary>
        public CosmeticLook LookOf(int peer) => Looks.TryGetValue(peer, out CosmeticLook l) ? l : CosmeticLook.Default;

        /// <summary>b15 profile: sends this player's look for cls to the host (cosmetic only; the host clamps it again).</summary>
        public void SendLook(ClassId cls)
        {
            if (LookFor == null) return;
            CosmeticLook look = CosmeticLook.Clamp(LookFor(cls));
            var w = new WireWriter(_pkt);
            w.U8(RoomMsg.Look);
            w.U8((int)cls);
            look.Write(ref w);
            _t.Send(Peers.Host, _pkt, w.Pos);
        }

        public void SetReady(bool ready)
        {
            _ready = ready;
            var w = new WireWriter(_pkt);
            w.U8(RoomMsg.Ready);
            w.U8(ready ? 1 : 0);
            _t.Send(Peers.Host, _pkt, w.Pos);
        }

        public void Update(long now)
        {
            if (Phase == RoomPhase.InMatch) { Session.Update(now); return; }
            if (Phase == RoomPhase.Closed) return;
            while (_t.Poll(out TransportEvent ev))
            {
                if (Phase == RoomPhase.InMatch) { Session.Handle(ref ev, now); continue; }   // the rest of this batch belongs to the match
                Handle(ref ev, now);
                if (Phase == RoomPhase.Closed) return;
            }
            if (Phase == RoomPhase.InMatch) { Session.Update(now); return; }
            if (_t.LocalPeer >= 0 && (!_helloSent || (Phase == RoomPhase.Connecting && now - _lastHello > 1000)))
            {
                _helloSent = true;
                _lastHello = now;
                var w = new WireWriter(_pkt);
                if (Rejoining)
                {
                    w.U8(RoomMsg.Rejoin);
                    w.U8(NetVersion.Protocol);
                    w.Str(_build, RoomMsg.BuildMax);
                }
                else
                {
                    w.U8(RoomMsg.Hello);
                    w.U8(NetVersion.Protocol);
                    w.Str(_build, RoomMsg.BuildMax);
                    w.Str(_name, RoomMsg.NameMax);
                }
                w.I32(CriticalMass.Sim.SimVersion.Schema);
                w.U64(CriticalMass.Sim.SimVersion.RulesDigest);
                w.U64(Secret);
                _t.Send(Peers.Host, _pkt, w.Pos);
            }
        }

        private void Fail(string why)
        {
            if (Phase == RoomPhase.Closed) return;
            Error = why;
            Phase = RoomPhase.Closed;
            Version++;
        }

        private void Handle(ref TransportEvent ev, long now)
        {
            switch (ev.Kind)
            {
                case TransportEventKind.Closed: Fail(string.IsNullOrEmpty(ev.Reason) ? "The connection to the room was lost." : ev.Reason); return;
                case TransportEventKind.PeerLeft: if (ev.From == Peers.Host) Fail("The host closed the room."); return;
                case TransportEventKind.Data: break;
                default: return;
            }
            if (ev.From != Peers.Host || ev.Data == null || ev.Length < 1) return;
            if (ChatWire.Owns(ev.Data[0])) { Chat.Handle(ref ev, now); return; }
            var r = new WireReader(ev.Data, 1, ev.Length);
            switch (ev.Data[0])
            {
                case RoomMsg.State:
                {
                    int teamSize = r.U8();
                    int n = r.U8();
                    if (r.Bad || n > MatchSetup.MaxHumans) return;
                    var list = new List<RoomMember>(n);
                    for (int i = 0; i < n; i++)
                    {
                        var m = new RoomMember { Peer = r.U8(), Name = r.Str(RoomMsg.NameMax) };
                        int team = r.U8(), cls = r.U8();
                        m.Ready = r.U8() != 0;
                        m.PingMs = r.I16();
                        if (r.Bad || team > 1 || cls >= ClassIds.All.Count) return;
                        m.Team = (TeamId)team;
                        m.Class = (ClassId)cls;
                        m.Confirmed = true;
                        list.Add(m);
                    }
                    // online-rooms-9: joiners see joins and leaves too (only the host used to); the first roster is not news.
                    RoomNotices.Roster(_announced, list, Chat, now, _t.LocalPeer, Phase == RoomPhase.Lobby);
                    Members.Clear();
                    Members.AddRange(list);
                    TeamSize = teamSize;
                    if (Phase == RoomPhase.Connecting && Me != null)
                    {
                        Phase = RoomPhase.Lobby;
                        if (_pickSet) SetPick(_team, _cls);
                        else SendLook(Me.Class);
                        if (_ready) SetReady(true);
                    }
                    Version++;
                    return;
                }
                case RoomMsg.Looks:
                {
                    int n = r.U8();
                    if (r.Bad || n > MatchSetup.MaxHumans) return;
                    var got = new Dictionary<int, CosmeticLook>(n);
                    for (int i = 0; i < n; i++)
                    {
                        int peer = r.U8();
                        CosmeticLook look = CosmeticLook.Read(ref r);
                        if (r.Bad) return;
                        got[peer] = look;
                    }
                    Looks.Clear();
                    foreach (KeyValuePair<int, CosmeticLook> kv in got) Looks[kv.Key] = kv.Value;
                    return;
                }
                case RoomMsg.Ping:
                {
                    int t = r.I32();
                    if (r.Bad) return;
                    var w = new WireWriter(_pkt);
                    w.U8(RoomMsg.Pong);
                    w.I32(t);
                    _t.Send(Peers.Host, _pkt, w.Pos);
                    return;
                }
                case RoomMsg.Reject:
                {
                    string text = r.Str(RoomMsg.TextMax);
                    Fail(r.Bad ? "The host refused the connection." : text);
                    return;
                }
                case RoomMsg.Rejoined:
                {
                    if (!Rejoining || Session != null) return;
                    MatchSetup setup = MatchSetup.Read(ref r, _cfg);
                    if (setup == null) { Fail("The host sent a match this game cannot play."); return; }
                    if (setup.SlotOfPeer(_t.LocalPeer) <= 0) { Fail("The host did not give your seat back."); return; }
                    Session = LockstepSession.Rejoining(_t, setup, GameConfig.CreateDefault(), Options);
                    Session.Other = Chat.Handle;
                    RoomNotices.Hook(Session, Chat);
                    Phase = RoomPhase.InMatch;
                    Version++;
                    Started?.Invoke(Session);
                    return;
                }
                case RoomMsg.Start:
                {
                    if (Rejoining) return;   // b15-rejoin: the START a lobby joiner would get; a rejoiner waits for REJOINED
                    MatchSetup setup = MatchSetup.Read(ref r, _cfg);
                    if (setup == null) { Fail("The host sent a match this game cannot play."); return; }
                    if (setup.SlotOfPeer(_t.LocalPeer) < 0) { Fail("The match started without you."); return; }
                    Session = new LockstepSession(_t, setup, GameConfig.CreateDefault(), Options) { Other = Chat.Handle };
                    RoomNotices.Hook(Session, Chat);
                    Phase = RoomPhase.InMatch;
                    Version++;
                    Started?.Invoke(Session);
                    return;
                }
            }
        }
    }
}
