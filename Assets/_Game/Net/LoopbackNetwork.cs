using System;
using System.Collections.Generic;

namespace CriticalMass.Net
{
    /// <summary>
    /// A simulated network inside one process, for tests with no sockets: every packet is delayed by DelayMs plus a uniform
    /// 0..JitterMs (so packets overtake each other: reordering), and dropped with probability Loss. Time is the caller's
    /// (Advance), never the wall clock, and the randomness is a seeded xorshift, so a test run is the same every time.
    /// </summary>
    public sealed class LoopbackNetwork
    {
        public int DelayMs;
        public int JitterMs;
        public double Loss;
        public long NowMs { get; private set; }
        public int Sent { get; private set; }
        public int Dropped { get; private set; }
        public long BytesSent { get; private set; }

        private uint _rng;
        private long _seq;
        private readonly List<LoopbackTransport> _peers = new List<LoopbackTransport>();
        private readonly List<Packet> _inFlight = new List<Packet>();

        private struct Packet
        {
            public long At;
            public long Seq;
            public int From;
            public int To;
            public byte[] Data;
        }

        public LoopbackNetwork(uint seed, int delayMs, int jitterMs, double loss)
        {
            _rng = seed == 0 ? 0x9E3779B9u : seed;
            DelayMs = delayMs;
            JitterMs = jitterMs;
            Loss = loss;
        }

        /// <summary>A new peer; the first one created is peer 0, the host. Every existing peer sees PeerJoined.</summary>
        public LoopbackTransport CreatePeer()
        {
            var t = new LoopbackTransport(this, _peers.Count);
            foreach (LoopbackTransport p in _peers) if (p.Open) { p.Push(TransportEventKind.PeerJoined, t.LocalPeer, null); t.Push(TransportEventKind.PeerJoined, p.LocalPeer, null); }
            _peers.Add(t);
            t.Push(TransportEventKind.Connected, t.LocalPeer, null);
            return t;
        }

        /// <summary>
        /// Peer id goes silent: nothing it sends is delivered and nothing reaches it (a pulled cable, a crashed game). With
        /// announce, the others also get PeerLeft at once, as a relay would report a closed socket; without it they have to
        /// notice the silence themselves.
        /// </summary>
        public void Cut(int id, bool announce)
        {
            LoopbackTransport t = _peers[id];
            t.Open = false;
            if (!announce) return;
            foreach (LoopbackTransport p in _peers) if (p.Open) p.Push(TransportEventKind.PeerLeft, id, null);
        }

        internal void Send(int from, int to, byte[] data, int length)
        {
            if (!_peers[from].Open) return;
            for (int i = 0; i < _peers.Count; i++)
            {
                if (i == from || (to != Peers.Broadcast && to != i)) continue;
                Sent++;
                BytesSent += length;
                if (Loss > 0 && Next01() < Loss) { Dropped++; continue; }
                int jitter = JitterMs > 0 ? (int)(Next01() * (JitterMs + 1)) : 0;
                var copy = new byte[length];
                Buffer.BlockCopy(data, 0, copy, 0, length);
                _inFlight.Add(new Packet { At = NowMs + DelayMs + jitter, Seq = _seq++, From = from, To = i, Data = copy });
            }
        }

        /// <summary>Moves simulated time to nowMs and delivers every packet due by then, in arrival order.</summary>
        public void Advance(long nowMs)
        {
            if (nowMs > NowMs) NowMs = nowMs;
            if (_inFlight.Count == 0) return;
            _inFlight.Sort((a, b) => a.At != b.At ? a.At.CompareTo(b.At) : a.Seq.CompareTo(b.Seq));
            int n = 0;
            while (n < _inFlight.Count && _inFlight[n].At <= NowMs)
            {
                Packet p = _inFlight[n++];
                LoopbackTransport dst = _peers[p.To];
                if (dst.Open && _peers[p.From].Open) dst.Push(TransportEventKind.Data, p.From, p.Data);
            }
            _inFlight.RemoveRange(0, n);
        }

        private double Next01()
        {
            uint x = _rng;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _rng = x;
            return x / 4294967296.0;
        }
    }

    /// <summary>One peer's end of a LoopbackNetwork.</summary>
    public sealed class LoopbackTransport : ITransport
    {
        private readonly LoopbackNetwork _net;
        private readonly Queue<TransportEvent> _events = new Queue<TransportEvent>();
        internal bool Open = true;

        internal LoopbackTransport(LoopbackNetwork net, int id)
        {
            _net = net;
            LocalPeer = id;
        }

        public int LocalPeer { get; }
        public bool Reliable => false;

        public void Send(int to, byte[] data, int length) => _net.Send(LocalPeer, to, data, length);

        public bool Poll(out TransportEvent ev)
        {
            if (!Open || _events.Count == 0) { ev = default; return false; }
            ev = _events.Dequeue();
            return true;
        }

        public void Close() => _net.Cut(LocalPeer, true);

        internal void Push(TransportEventKind kind, int from, byte[] data)
        {
            _events.Enqueue(new TransportEvent { Kind = kind, From = from, Data = data, Length = data != null ? data.Length : 0 });
        }
    }
}
