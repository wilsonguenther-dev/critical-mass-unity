using System;
using System.Collections.Generic;

namespace CriticalMass.Net.Relay
{
    /// <summary>
    /// online-rooms-3: an in-process stand-in for server/relay with the same rules (rooms exist from creation, the host
    /// is peer 0 by key, a joiner before the host hears "not ready yet", peer numbers from 1, stamping the sender,
    /// broadcast to everyone but the sender, lock on start, the host leaving closes the room for everyone), delivering
    /// in order after DelayMs on a clock the caller advances. Single-threaded, no sockets: tests run whole HeadlessPeers
    /// (the code the game's hidden test mode runs) through it, so a game-path desync is caught in CI and costs no relay
    /// requests.
    /// </summary>
    public sealed class LocalRelay
    {
        public int DelayMs;
        /// <summary>Extra time a host's connection takes to arrive (a slow host network: the joiner can get there first).</summary>
        public int HostExtraMs;
        private long _now;
        private uint _rng = 0x9E3779B9u;
        private readonly Dictionary<string, Room> _rooms = new Dictionary<string, Room>();
        private readonly List<Pending> _queue = new List<Pending>();

        internal sealed class Room
        {
            public string Key;
            public bool Locked, Closed;
            public int NextPeer = 1;
            public readonly List<LocalRelayTransport> Peers = new List<LocalRelayTransport>();
        }

        private struct Pending
        {
            public long At;
            public LocalRelayTransport To;
            public TransportEvent Ev;
        }

        public LocalRelay(int delayMs = 20) { DelayMs = delayMs; }

        public (string code, string key) CreateRoom()
        {
            string code;
            do
            {
                var c = new char[RoomCodes.Length];
                for (int i = 0; i < c.Length; i++) c[i] = RoomCodes.Alphabet[(int)(Next() % (uint)RoomCodes.Alphabet.Length)];
                code = new string(c);
            } while (_rooms.ContainsKey(code));
            string key = Next().ToString("X8") + Next().ToString("X8");
            _rooms[code] = new Room { Key = key };
            return (code, key);
        }

        private uint Next() { _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5; return _rng; }

        /// <summary>Opens a connection; like a real socket it reaches the room DelayMs later (so a joiner can beat the host).</summary>
        public ITransport Connect(string code, string key)
        {
            var t = new LocalRelayTransport(this);
            _joins.Add(new Join { At = _now + DelayMs + (key != null ? HostExtraMs : 0), T = t, Code = code, Key = key });
            return t;
        }

        private struct Join
        {
            public long At;
            public LocalRelayTransport T;
            public string Code, Key;
        }

        private readonly List<Join> _joins = new List<Join>();

        private void Arrive(LocalRelayTransport t, string code, string key)
        {
            if (t.Dead) return;
            if (code == null || !_rooms.TryGetValue(code, out Room room) || room.Closed) { Refuse(t, RelayTransport.NotFoundText); return; }
            bool isHost = key != null && key == room.Key && room.Peers.TrueForAll(p => p.Peer != Peers.Host);
            bool hostIn = room.Peers.Exists(p => p.Peer == Peers.Host);
            if (!isHost && !hostIn) { Refuse(t, RelayTransport.HostNotReadyText); return; }
            if (!isHost && room.Locked) { Refuse(t, RelayTransport.ReasonText(3)); return; }
            if (room.Peers.Count >= 32) { Refuse(t, RelayTransport.ReasonText(2)); return; }
            int peer = isHost ? Peers.Host : room.NextPeer++;
            t.Room = room;
            t.Peer = peer;
            Post(t, new TransportEvent { Kind = TransportEventKind.Connected, From = peer });
            foreach (LocalRelayTransport o in room.Peers)
            {
                Post(t, new TransportEvent { Kind = TransportEventKind.PeerJoined, From = o.Peer });
                Post(o, new TransportEvent { Kind = TransportEventKind.PeerJoined, From = peer });
            }
            room.Peers.Add(t);
        }

        private void Refuse(LocalRelayTransport t, string reason)
        {
            t.Dead = true;
            Post(t, new TransportEvent { Kind = TransportEventKind.Closed, From = -1, Reason = reason });
        }

        private void Post(LocalRelayTransport to, TransportEvent ev) => _queue.Add(new Pending { At = _now + DelayMs, To = to, Ev = ev });

        /// <summary>Delivers everything due by now (in the order it was sent).</summary>
        public void Advance(long now)
        {
            _now = now;
            for (int i = 0; i < _joins.Count; i++)
                if (_joins[i].At <= now) { Join j = _joins[i]; _joins.RemoveAt(i--); Arrive(j.T, j.Code, j.Key); }
            int kept = 0;
            for (int i = 0; i < _queue.Count; i++)
            {
                Pending p = _queue[i];
                if (p.At <= now) p.To.Inbox.Enqueue(p.Ev);
                else _queue[kept++] = p;
            }
            _queue.RemoveRange(kept, _queue.Count - kept);
        }

        internal void Send(LocalRelayTransport from, int to, byte[] data, int length)
        {
            Room room = from.Room;
            if (room == null || from.Dead) return;
            var copy = new byte[length];
            Buffer.BlockCopy(data, 0, copy, 0, length);
            foreach (LocalRelayTransport p in room.Peers)
            {
                if (p == from || (to != Peers.Broadcast && p.Peer != to)) continue;
                Post(p, new TransportEvent { Kind = TransportEventKind.Data, From = from.Peer, Data = copy, Length = length });
            }
        }

        internal void Lock(LocalRelayTransport from) { if (from.Room != null && from.Peer == Peers.Host) from.Room.Locked = true; }

        internal void Leave(LocalRelayTransport t)
        {
            Room room = t.Room;
            if (room == null || !room.Peers.Remove(t)) return;
            t.Dead = true;
            if (t.Peer == Peers.Host)
            {
                room.Closed = true;
                foreach (LocalRelayTransport p in room.Peers)
                {
                    p.Dead = true;
                    Post(p, new TransportEvent { Kind = TransportEventKind.Closed, From = -1, Reason = RelayTransport.ReasonText(4) });
                }
                room.Peers.Clear();
                return;
            }
            foreach (LocalRelayTransport p in room.Peers) Post(p, new TransportEvent { Kind = TransportEventKind.PeerLeft, From = t.Peer });
        }
    }

    /// <summary>One connection into a LocalRelay room (reliable and ordered, like the WebSocket relay).</summary>
    public sealed class LocalRelayTransport : ITransport, ILockableTransport
    {
        private readonly LocalRelay _relay;
        internal readonly Queue<TransportEvent> Inbox = new Queue<TransportEvent>();
        internal LocalRelay.Room Room;
        internal int Peer = -1;
        internal bool Dead;
        private int _seenPeer = -1;

        internal LocalRelayTransport(LocalRelay relay) { _relay = relay; }

        public int LocalPeer => _seenPeer;
        public bool Reliable => true;

        public void Send(int to, byte[] data, int length)
        {
            if (Dead || _seenPeer < 0 || length <= 0 || length > Peers.MaxPacket) return;
            _relay.Send(this, to, data, length);
        }

        public bool Poll(out TransportEvent ev)
        {
            if (Inbox.Count == 0) { ev = default; return false; }
            ev = Inbox.Dequeue();
            if (ev.Kind == TransportEventKind.Connected) _seenPeer = ev.From;
            return true;
        }

        public void Lock() => _relay.Lock(this);

        public void Close() { if (Room == null) Dead = true; else _relay.Leave(this); }
    }
}
