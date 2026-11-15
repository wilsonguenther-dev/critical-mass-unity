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
        public const int Protocol = 3;   // 3 (online-rooms-9): bundles carry the host's tick and every slot's margin   // 2: the start message carries the config and tick 0 fingerprints
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
    }

    /// <summary>Room message numbers (lockstep uses 1 and 2).</summary>
    internal static class RoomMsg
    {
        public const int Hello = 20, State = 21, Pick = 22, Ready = 23, Reject = 24, Start = 25, Ping = 26, Pong = 27;
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

        /// <summary>Posts the in-match leave notice on this machine when a human becomes a bot.</summary>
        public static void Hook(LockstepSession s, ChatChannel chat)
        {
            if (s == null || chat == null) return;
            s.HumanToBot += (slot, actor, tick) =>
            {
                string name = slot >= 0 && slot < s.Setup.Humans.Length ? s.Setup.Humans[slot].Name : null;
                chat.Notice(LeftMatch(string.IsNullOrEmpty(name) ? "A player" : name), s.NowMs);
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
                TeamOf = p => (int)(Find(p)?.Team ?? TeamId.Azure),
                PeersInRoom = ConfirmedPeers,
            };
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
            if (ev.Data[0] == ChatChannel.Msg) { if (m.Confirmed) Chat.Handle(ref ev, now); return; }
            var r = new WireReader(ev.Data, 1, ev.Length);
            switch (ev.Data[0])
            {
                case RoomMsg.Hello:
                {
                    int proto = r.U8();
                    string build = r.Str(RoomMsg.BuildMax);
                    string name = r.Str(RoomMsg.NameMax);
                    if (r.Bad) return;
                    if (proto != NetVersion.Protocol || build != _build)
                    {
                        Reject(ev.From, "Your game is a different version from the host's (" + build + " vs " + _build + "). Both of you need the same update.");
                        Members.Remove(m);
                        Changed("");
                        return;
                    }
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
            Session = new LockstepSession(_t, setup, GameConfig.CreateDefault(), options) { Other = Chat.Handle };
            RoomNotices.Hook(Session, Chat);
            setup.ConfigHash = Session.MyConfigHash;
            setup.WorldHash0 = Session.MyWorldHash0;
            var w = new WireWriter(new byte[Peers.MaxPacket]);
            w.U8(RoomMsg.Start);
            setup.Write(ref w);
            if (w.Overflow) { Session = null; return null; }
            _t.Send(Peers.Broadcast, w.Buf, w.Pos);
            (_t as ILockableTransport)?.Lock();
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

        public RoomClient(ITransport transport, GameConfig cfg, string name, string build)
        {
            _t = transport;
            _cfg = cfg;
            _name = RoomMsg.CleanName(name);
            _build = build ?? "";
            Chat = new ChatChannel(transport);
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
                w.U8(RoomMsg.Hello);
                w.U8(NetVersion.Protocol);
                w.Str(_build, RoomMsg.BuildMax);
                w.Str(_name, RoomMsg.NameMax);
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
            if (ev.Data[0] == ChatChannel.Msg) { Chat.Handle(ref ev, now); return; }
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
                        if (_ready) SetReady(true);
                    }
                    Version++;
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
                case RoomMsg.Start:
                {
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
