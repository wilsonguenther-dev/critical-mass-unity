using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CriticalMass.Net.Relay
{
    // b15-zero-cost: online play with no relay at all. The host's game IS the relay for direct joiners: it listens on a TCP
    // port and speaks the relay's own frames (server/relay/src/protocol.js) over a 2-byte length prefix, so RoomHost,
    // RoomClient and LockstepSession run unchanged (the same lockstep bytes, version checks, chat and rejoin) whether a
    // player came by the relay, the local network or a typed address. Plain .NET sockets: no service, no account.

    /// <summary>A typed or discovered "host[:port]" (an IPv4 address or a name; the port defaults to DirectHost.DefaultPort).</summary>
    public static class DirectAddress
    {
        /// <summary>Parses "192.168.1.5", "192.168.1.5:47800" or "my-pc.local:47800". False when it cannot be an address.</summary>
        public static bool TryParse(string typed, out string host, out int port)
        {
            host = null;
            port = DirectHost.DefaultPort;
            if (string.IsNullOrWhiteSpace(typed)) return false;
            string s = typed.Trim();
            int colon = s.LastIndexOf(':');
            if (colon >= 0)
            {
                if (!int.TryParse(s.Substring(colon + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535) return false;
                s = s.Substring(0, colon);
            }
            if (s.Length == 0 || s.Length > 253) return false;
            bool digitsAndDots = true;
            foreach (char c in s)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '-';
                if (!ok) return false;
                if (!((c >= '0' && c <= '9') || c == '.')) digitsAndDots = false;
            }
            if (digitsAndDots && !IPAddress.TryParse(s, out _)) return false;   // "1.2.3" or "300.1.1.1"
            if (digitsAndDots && s.Split('.').Length != 4) return false;
            host = s;
            return true;
        }

        /// <summary>This machine's local-network IPv4 addresses (what a friend on the same Wi-Fi types), private ones first.</summary>
        public static List<string> LocalAddresses()
        {
            var lan = new List<string>();
            var other = new List<string>();
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (UnicastIPAddressInformation u in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (u.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(u.Address)) continue;
                        byte[] b = u.Address.GetAddressBytes();
                        if (b[0] == 169 && b[1] == 254) continue;   // link-local: no router handed out an address
                        bool priv = b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
                        string a = u.Address.ToString();
                        if (!lan.Contains(a) && !other.Contains(a)) (priv ? lan : other).Add(a);
                    }
                }
            }
            catch (Exception) { /* no interface list on this platform: the screen simply shows no address */ }
            lan.AddRange(other);
            return lan;
        }
    }

    /// <summary>One TCP connection carrying length-prefixed frames; a background task writes, the owner reads.</summary>
    internal sealed class FramedLink
    {
        public const int MaxFrame = 1400;
        private readonly TcpClient _c;
        private readonly NetworkStream _s;
        private readonly ConcurrentQueue<byte[]> _out = new ConcurrentQueue<byte[]>();
        private readonly SemaphoreSlim _sig = new SemaphoreSlim(0);
        private volatile bool _closing, _dead;

        public FramedLink(TcpClient c)
        {
            _c = c;
            _c.NoDelay = true;   // no Nagle: a 40-byte input packet goes now, not after 40 ms
            _s = c.GetStream();
            Task.Run(WriteLoop);
        }

        public bool Dead => _dead;

        public void Send(byte[] frame, int off, int len)
        {
            if (_dead || _closing || len <= 0 || len > MaxFrame) return;
            var b = new byte[len + 2];
            b[0] = (byte)len;
            b[1] = (byte)(len >> 8);
            Buffer.BlockCopy(frame, off, b, 2, len);
            _out.Enqueue(b);
            _sig.Release();
        }

        /// <summary>Sends what is queued, then closes (a goodbye frame queued first is delivered).</summary>
        public void CloseAfterFlush()
        {
            _closing = true;
            _sig.Release();
        }

        /// <summary>Drops the connection at once, nothing flushed (a pulled cable, for tests and for misbehaving peers).</summary>
        public void Abort()
        {
            _dead = true;
            try { _c.Client.LingerState = new LingerOption(true, 0); } catch (Exception) { }
            try { _c.Close(); } catch (Exception) { }
            _sig.Release();
        }

        private async Task WriteLoop()
        {
            try
            {
                while (!_dead)
                {
                    await _sig.WaitAsync().ConfigureAwait(false);
                    while (!_dead && _out.TryDequeue(out byte[] b)) await _s.WriteAsync(b, 0, b.Length).ConfigureAwait(false);
                    if (_closing) break;
                }
            }
            catch (Exception) { /* the reader reports the closed connection */ }
            finally
            {
                _dead = true;
                try { _c.Close(); } catch (Exception) { }
            }
        }

        /// <summary>The next frame into buf: its length, or -1 when the connection ended or sent something malformed.</summary>
        public async Task<int> ReadFrame(byte[] buf)
        {
            try
            {
                if (!await ReadExact(buf, 2).ConfigureAwait(false)) return -1;
                int n = buf[0] | (buf[1] << 8);
                if (n <= 0 || n > MaxFrame || n > buf.Length) return -1;
                if (!await ReadExact(buf, n).ConfigureAwait(false)) return -1;
                return n;
            }
            catch (Exception) { return -1; }
        }

        private async Task<bool> ReadExact(byte[] b, int n)
        {
            int got = 0;
            while (got < n)
            {
                int r = await _s.ReadAsync(b, got, n - got).ConfigureAwait(false);
                if (r <= 0) return false;
                got += r;
            }
            return true;
        }
    }

    /// <summary>The relay's per-sender limit (90 a second, burst 180), so a direct host is as hard to flood as the relay.</summary>
    internal sealed class SendBucket
    {
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private double _tokens = 180;
        private long _at;

        public bool Take()
        {
            long now = _clock.ElapsedMilliseconds;
            _tokens = Math.Min(180, _tokens + (now - _at) * 90 / 1000.0);
            _at = now;
            if (_tokens < 1) return false;
            _tokens -= 1;
            return true;
        }
    }

    internal static class DirectFrames
    {
        public const byte OpData = 0, OpLock = 1, OpWelcome = 1, OpJoined = 2, OpLeft = 3, OpClosing = 4;
        /// <summary>The first frame a direct joiner sends: "CMD" and the direct protocol version.</summary>
        public static readonly byte[] Hello = { (byte)'C', (byte)'M', (byte)'D', DirectHost.Version };
    }

    /// <summary>
    /// b15-zero-cost: the host's side of direct play: a TCP listener that gives each joiner a peer number (200 to 254, apart
    /// from the relay's 1 and up, so one room can hold both) and routes their frames exactly as server/relay does: stamps
    /// the sender, broadcast to everyone but the sender, size and rate limits, "the match started" after Lock, and every
    /// joiner told "the host closed the room" when the host leaves. As an ITransport it is the host's own end (peer 0).
    /// </summary>
    public sealed class DirectHost : ITransport, ILockableTransport
    {
        public const int DefaultPort = 47800;
        public const int FirstPeer = 200, LastPeer = 254;
        public const byte Version = 1;

        private readonly TcpListener _l;
        private readonly ConcurrentQueue<TransportEvent> _in = new ConcurrentQueue<TransportEvent>();
        private readonly object _gate = new object();
        private readonly Dictionary<int, FramedLink> _links = new Dictionary<int, FramedLink>();
        private int _next = FirstPeer;
        private volatile bool _locked, _closed;

        /// <summary>The TCP port it listens on (a friend outside the network needs this port forwarded to this computer).</summary>
        public int Port { get; }
        public int Count { get { lock (_gate) return _links.Count; } }
        public int LocalPeer => Peers.Host;
        public bool Reliable => true;
        public long Accepted, Refused;

        /// <summary>
        /// Listens on port (0 = any free port; otherwise the next of `tries` ports that is free). announce: post Connected
        /// at once (alone; a HostMux announces when its relay leg is in). Throws RoomException with a plain sentence.
        /// </summary>
        public static DirectHost Start(int port = DefaultPort, bool announce = true, int tries = 10)
        {
            string last = "";
            for (int i = 0; i < Math.Max(1, tries); i++)
            {
                int p = port == 0 ? 0 : port + i;
                TcpListener l = null;
                try
                {
                    l = new TcpListener(IPAddress.Any, p);
                    l.Start();
                    return new DirectHost(l, announce);
                }
                catch (SocketException e)
                {
                    last = e.Message;
                    try { l?.Stop(); } catch (Exception) { }
                    if (port == 0) break;
                }
            }
            throw new RoomException("Could not open a port for direct play (" + last + ").");
        }

        private DirectHost(TcpListener l, bool announce)
        {
            _l = l;
            Port = ((IPEndPoint)l.LocalEndpoint).Port;
            if (announce) _in.Enqueue(new TransportEvent { Kind = TransportEventKind.Connected, From = Peers.Host });
            Task.Run(AcceptLoop);
        }

        public bool Poll(out TransportEvent ev) => _in.TryDequeue(out ev);

        public void Send(int to, byte[] data, int length)
        {
            if (_closed || length < 0 || length > Peers.MaxPacket) return;
            var f = new byte[length + 2];
            f[0] = DirectFrames.OpData;
            f[1] = Peers.Host;
            Buffer.BlockCopy(data, 0, f, 2, length);
            foreach (FramedLink link in Targets(to, -1)) link.Send(f, 0, f.Length);
        }

        public void Lock() => _locked = true;

        public void Close()
        {
            if (_closed) return;
            _closed = true;
            try { _l.Stop(); } catch (Exception) { }
            List<FramedLink> all;
            lock (_gate) { all = new List<FramedLink>(_links.Values); _links.Clear(); }
            var bye = new[] { DirectFrames.OpClosing, (byte)4 };   // REASON.HOST_LEFT
            foreach (FramedLink link in all) { link.Send(bye, 0, 2); link.CloseAfterFlush(); }
        }

        private List<FramedLink> Targets(int to, int except)
        {
            var list = new List<FramedLink>();
            lock (_gate)
            {
                if (to == Peers.Broadcast) { foreach (KeyValuePair<int, FramedLink> kv in _links) if (kv.Key != except) list.Add(kv.Value); }
                else if (to != except && _links.TryGetValue(to, out FramedLink one)) list.Add(one);
            }
            return list;
        }

        private async Task AcceptLoop()
        {
            while (!_closed)
            {
                TcpClient c;
                try { c = await _l.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch (Exception)
                {
                    if (_closed) return;
                    await Task.Delay(50).ConfigureAwait(false);
                    continue;
                }
                _ = Task.Run(() => Serve(c));
            }
        }

        private static void Kick(FramedLink link, int reason)
        {
            link.Send(new[] { DirectFrames.OpClosing, (byte)reason }, 0, 2);
            link.CloseAfterFlush();
        }

        private async Task Serve(TcpClient c)
        {
            FramedLink link;
            try { link = new FramedLink(c); }
            catch (Exception) { try { c.Close(); } catch (Exception) { } return; }
            var buf = new byte[FramedLink.MaxFrame];
            Task<int> hello = link.ReadFrame(buf);
            if (await Task.WhenAny(hello, Task.Delay(5000)).ConfigureAwait(false) != hello) { link.Abort(); Interlocked.Increment(ref Refused); return; }
            int n = hello.Result;
            if (n < 4 || buf[0] != 'C' || buf[1] != 'M' || buf[2] != 'D') { Kick(link, 9); Interlocked.Increment(ref Refused); return; }   // BAD_REQUEST
            if (buf[3] != Version) { Kick(link, 8); Interlocked.Increment(ref Refused); return; }   // BAD_VERSION
            int peer = -1, reason = 0;
            var others = new List<int>();
            var told = new List<FramedLink>();
            lock (_gate)
            {
                if (_closed) reason = 4;
                else if (_locked) reason = 3;
                else if (_links.Count >= MatchSetup.MaxHumans - 1 || _next > LastPeer) reason = 2;
                else
                {
                    peer = _next++;
                    others.Add(Peers.Host);
                    foreach (KeyValuePair<int, FramedLink> kv in _links) { others.Add(kv.Key); told.Add(kv.Value); }
                    _links[peer] = link;
                }
            }
            if (peer < 0) { Kick(link, reason); Interlocked.Increment(ref Refused); return; }
            Interlocked.Increment(ref Accepted);
            var welcome = new byte[3 + others.Count];
            welcome[0] = DirectFrames.OpWelcome;
            welcome[1] = (byte)peer;
            welcome[2] = (byte)others.Count;
            for (int i = 0; i < others.Count; i++) welcome[3 + i] = (byte)others[i];
            link.Send(welcome, 0, welcome.Length);
            var joined = new[] { DirectFrames.OpJoined, (byte)peer };
            foreach (FramedLink o in told) o.Send(joined, 0, 2);
            _in.Enqueue(new TransportEvent { Kind = TransportEventKind.PeerJoined, From = peer });
            var bucket = new SendBucket();
            while (!_closed)
            {
                int m = await link.ReadFrame(buf).ConfigureAwait(false);
                if (m < 0) break;
                if (!bucket.Take()) { Kick(link, 5); break; }   // TOO_FAST
                if (buf[0] == DirectFrames.OpData && m >= 2)
                {
                    int to = buf[1];
                    if (to == Peers.Host || to == Peers.Broadcast)
                    {
                        var payload = new byte[m - 2];
                        Buffer.BlockCopy(buf, 2, payload, 0, m - 2);
                        _in.Enqueue(new TransportEvent { Kind = TransportEventKind.Data, From = peer, Data = payload, Length = payload.Length });
                    }
                    if (to != Peers.Host)
                    {
                        buf[1] = (byte)peer;   // the host stamps the sender; nobody can speak for someone else
                        foreach (FramedLink o in Targets(to, peer)) o.Send(buf, 0, m);
                    }
                    continue;
                }
                if (buf[0] == DirectFrames.OpLock && m == 1) continue;   // only the host locks (it is in this process)
                Kick(link, 9);
                break;
            }
            bool removed;
            List<FramedLink> rest;
            lock (_gate)
            {
                removed = _links.TryGetValue(peer, out FramedLink l) && l == link && _links.Remove(peer);
                rest = new List<FramedLink>(_links.Values);
            }
            link.CloseAfterFlush();
            if (!removed) return;
            _in.Enqueue(new TransportEvent { Kind = TransportEventKind.PeerLeft, From = peer });
            var left = new[] { DirectFrames.OpLeft, (byte)peer };
            foreach (FramedLink o in rest) o.Send(left, 0, 2);
        }
    }

    /// <summary>
    /// b15-zero-cost: a joiner's direct connection to a host's DirectHost (a local-network game or a typed address). The
    /// same events as RelayTransport; ConnectFailed says the host could not be reached at all (then a FallbackTransport
    /// tries the relay).
    /// </summary>
    public sealed class DirectClient : ITransport, ILockableTransport
    {
        private readonly ConcurrentQueue<TransportEvent> _in = new ConcurrentQueue<TransportEvent>();
        private readonly ConcurrentQueue<byte[]> _early = new ConcurrentQueue<byte[]>();
        private volatile int _localPeer = -1;
        private volatile bool _closed;
        private volatile FramedLink _link;
        private string _closingReason;
        private int _finished;

        public string Address { get; }
        public bool ConnectFailed { get; private set; }
        public int LocalPeer => _localPeer;
        public bool Reliable => true;

        public const string UnreachableText = "Could not reach that computer. Check the address, and that the host's game is in a room.";

        private DirectClient(string address) { Address = address; }

        public static DirectClient Connect(string host, int port, int timeoutMs = 4000)
        {
            var t = new DirectClient(host + ":" + port);
            Task.Run(() => t.Run(host, port, timeoutMs));
            return t;
        }

        public void Send(int to, byte[] data, int length)
        {
            if (_closed || length < 0 || length > Peers.MaxPacket) return;
            var f = new byte[length + 2];
            f[0] = DirectFrames.OpData;
            f[1] = (byte)to;
            Buffer.BlockCopy(data, 0, f, 2, length);
            FramedLink link = _link;
            if (link != null && _localPeer >= 0) link.Send(f, 0, f.Length);
            else _early.Enqueue(f);   // before the welcome: sent once the host has taken us in
        }

        public void Lock() { }

        public bool Poll(out TransportEvent ev) => _in.TryDequeue(out ev);

        public void Close()
        {
            if (_closed) return;
            _closed = true;
            _link?.CloseAfterFlush();
        }

        /// <summary>Tests: drop with no goodbye, like a pulled cable.</summary>
        public void Abort()
        {
            _closed = true;
            _link?.Abort();
        }

        private async Task Run(string host, int port, int timeoutMs)
        {
            var c = new TcpClient();
            try
            {
                Task t = c.ConnectAsync(host, port);
                if (await Task.WhenAny(t, Task.Delay(timeoutMs)).ConfigureAwait(false) != t)
                {
                    _ = t.ContinueWith(x => { _ = x.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    throw new TimeoutException();
                }
                await t.ConfigureAwait(false);
            }
            catch (Exception)
            {
                ConnectFailed = true;
                try { c.Close(); } catch (Exception) { }
                Finish(UnreachableText);
                return;
            }
            if (_closed) { try { c.Close(); } catch (Exception) { } Finish("You left the room."); return; }
            var link = new FramedLink(c);
            link.Send(DirectFrames.Hello, 0, DirectFrames.Hello.Length);
            _link = link;
            var buf = new byte[FramedLink.MaxFrame];
            while (true)
            {
                int n = await link.ReadFrame(buf).ConfigureAwait(false);
                if (n < 0) break;
                OnFrame(buf, n);
            }
            Finish(_closingReason ?? (_closed ? "You left the room." : "The connection to the host was lost."));
        }

        private void OnFrame(byte[] b, int n)
        {
            if (n < 1) return;
            switch (b[0])
            {
                case DirectFrames.OpData when n >= 2 && _localPeer >= 0:
                {
                    var payload = new byte[n - 2];
                    Buffer.BlockCopy(b, 2, payload, 0, n - 2);
                    _in.Enqueue(new TransportEvent { Kind = TransportEventKind.Data, From = b[1], Data = payload, Length = payload.Length });
                    return;
                }
                case DirectFrames.OpWelcome when n >= 3:
                {
                    _localPeer = b[1];
                    _in.Enqueue(new TransportEvent { Kind = TransportEventKind.Connected, From = b[1] });
                    int count = b[2];
                    for (int i = 0; i < count && 3 + i < n; i++) _in.Enqueue(new TransportEvent { Kind = TransportEventKind.PeerJoined, From = b[3 + i] });
                    while (_early.TryDequeue(out byte[] f)) _link?.Send(f, 0, f.Length);
                    return;
                }
                case DirectFrames.OpJoined when n >= 2:
                    _in.Enqueue(new TransportEvent { Kind = TransportEventKind.PeerJoined, From = b[1] });
                    return;
                case DirectFrames.OpLeft when n >= 2:
                    _in.Enqueue(new TransportEvent { Kind = TransportEventKind.PeerLeft, From = b[1] });
                    return;
                case DirectFrames.OpClosing when n >= 2:
                    _closingReason = RelayTransport.ReasonText(b[1]);
                    return;
            }
        }

        private void Finish(string reason)
        {
            if (Interlocked.Exchange(ref _finished, 1) != 0) return;
            _closed = true;
            _in.Enqueue(new TransportEvent { Kind = TransportEventKind.Closed, From = -1, Reason = reason });
        }
    }

    /// <summary>
    /// b15-zero-cost: the host's transport when it takes players both ways at once: the relay room (code) and direct
    /// (local network and typed address). Peer numbers never collide (relay 1 and up, direct 200 and up). A broadcast goes
    /// to the relay only while a relay player is in, so a room of local-network friends costs the relay nothing. If the
    /// relay leg fails (over its daily limit, offline, account trouble), the room carries on direct: the relay's players
    /// leave (each a PeerLeft, so the match hands them to bots) and nobody else notices.
    /// </summary>
    public sealed class HostMux : ITransport, ILockableTransport
    {
        public readonly ITransport Relay;
        public readonly DirectHost Direct;
        private readonly Queue<TransportEvent> _q = new Queue<TransportEvent>();
        private readonly HashSet<int> _relayPeers = new HashSet<int>();
        private bool _connected, _relayGone;

        /// <summary>Why the relay leg ended while the room went on (plain words), else null.</summary>
        public string RelayLostReason { get; private set; }
        public bool RelayUp => Relay != null && !_relayGone;
        public int RelayPeers => _relayPeers.Count;
        public int LocalPeer => _connected ? Peers.Host : -1;
        public bool Reliable => true;

        /// <summary>relay may be null (direct only); direct must not be (use the relay transport alone otherwise).</summary>
        public HostMux(ITransport relay, DirectHost direct)
        {
            Direct = direct ?? throw new ArgumentNullException(nameof(direct));
            Relay = relay;
            if (relay == null) { _connected = true; _q.Enqueue(new TransportEvent { Kind = TransportEventKind.Connected, From = Peers.Host }); }
        }

        public bool Poll(out TransportEvent ev)
        {
            while (true)
            {
                if (_q.Count > 0) { ev = _q.Dequeue(); return true; }
                if (RelayUp && Relay.Poll(out ev))
                {
                    switch (ev.Kind)
                    {
                        case TransportEventKind.Connected:
                            if (_connected) continue;
                            _connected = true;
                            return true;
                        case TransportEventKind.PeerJoined:
                            if (ev.From >= DirectHost.FirstPeer || ev.From == Peers.Host) continue;
                            _relayPeers.Add(ev.From);
                            return true;
                        case TransportEventKind.PeerLeft:
                            if (!_relayPeers.Remove(ev.From)) continue;
                            return true;
                        case TransportEventKind.Data:
                            if (ev.From >= DirectHost.FirstPeer) continue;
                            return true;
                        case TransportEventKind.Closed:
                            // The relay is gone; the room is not: its relay players leave, direct ones stay.
                            _relayGone = true;
                            RelayLostReason = string.IsNullOrEmpty(ev.Reason) ? "The online room service closed." : ev.Reason;
                            foreach (int p in _relayPeers) _q.Enqueue(new TransportEvent { Kind = TransportEventKind.PeerLeft, From = p });
                            _relayPeers.Clear();
                            if (!_connected) { _connected = true; _q.Enqueue(new TransportEvent { Kind = TransportEventKind.Connected, From = Peers.Host }); }
                            continue;
                    }
                    return true;
                }
                if (Direct.Poll(out ev))
                {
                    if (ev.Kind == TransportEventKind.Connected) continue;   // the mux announces once
                    if (ev.Kind == TransportEventKind.Closed) continue;
                    return true;
                }
                ev = default;
                return false;
            }
        }

        public void Send(int to, byte[] data, int length)
        {
            if (to == Peers.Broadcast)
            {
                if (RelayUp && _relayPeers.Count > 0) Relay.Send(to, data, length);
                Direct.Send(to, data, length);
                return;
            }
            if (to >= DirectHost.FirstPeer) Direct.Send(to, data, length);
            else if (RelayUp) Relay.Send(to, data, length);
        }

        public void Lock()
        {
            if (RelayUp && Relay is ILockableTransport l) l.Lock();
            Direct.Lock();
        }

        public void Close()
        {
            Relay?.Close();
            Direct.Close();
        }
    }

    /// <summary>
    /// b15-zero-cost: a joiner's transport that tries a direct connection first and, if the host cannot be reached at all,
    /// joins the same room through the relay (when a code and relay are known). The room code never changes; the path is
    /// picked for the player. Anything after the connection is made is passed through unchanged.
    /// </summary>
    public sealed class FallbackTransport : ITransport, ILockableTransport
    {
        private readonly Func<ITransport> _fallback;
        private ITransport _cur;
        private bool _connected, _fellBack;

        public ITransport Current => _cur;
        /// <summary>"direct" or "relay": the path in use.</summary>
        public string Path => _fellBack || !(_cur is DirectClient) ? "relay" : "direct";
        /// <summary>Why the direct attempt failed (plain words), when it did.</summary>
        public string DirectFailure { get; private set; }

        public FallbackTransport(ITransport first, Func<ITransport> fallback)
        {
            _cur = first ?? throw new ArgumentNullException(nameof(first));
            _fallback = fallback;
        }

        public int LocalPeer => _cur.LocalPeer;
        public bool Reliable => _cur.Reliable;

        public bool Poll(out TransportEvent ev)
        {
            while (_cur.Poll(out ev))
            {
                if (ev.Kind == TransportEventKind.Connected) _connected = true;
                if (ev.Kind == TransportEventKind.Closed && !_connected && !_fellBack && _fallback != null)
                {
                    DirectFailure = ev.Reason;
                    _fellBack = true;
                    try { _cur.Close(); } catch (Exception) { }
                    _cur = _fallback();
                    continue;
                }
                return true;
            }
            return false;
        }

        public void Send(int to, byte[] data, int length) => _cur.Send(to, data, length);
        public void Lock() { if (_cur is ILockableTransport l) l.Lock(); }
        public void Close() => _cur.Close();
    }

    /// <summary>A room another game on this local network announced.</summary>
    public struct LanGame
    {
        public string Address;
        public int Port;
        public string Code;
        public string Name;
        public int Humans;
        public string Build;
        public int Protocol;
        public long SeenMs;
    }

    /// <summary>
    /// b15-zero-cost: local-network discovery. A hosting game sends one small UDP datagram a second to the local broadcast
    /// address (port 47801): its TCP port, room code, host name, player count and build. Joiners listen and list what they
    /// heard in the last few seconds; one whose typed code matches a local game joins it directly (free) instead of through
    /// the relay. Some networks (guest Wi-Fi, "client isolation") block this: then the code still works through the relay.
    /// </summary>
    public sealed class LanBeacon : IDisposable
    {
        public const int Port = 47801;
        private static readonly byte[] Magic = { (byte)'C', (byte)'M', (byte)'L', (byte)'A', (byte)'N', 1 };
        private readonly UdpClient _udp;
        private readonly List<IPEndPoint> _to = new List<IPEndPoint>();
        private volatile byte[] _payload;
        private volatile bool _stop;
        public long Sent;

        /// <summary>Starts announcing (extraTargets: tests send to 127.0.0.1 instead of the broadcast address).</summary>
        public LanBeacon(int tcpPort, string code, string name, int humans, string build, int listenPort = Port, IEnumerable<IPEndPoint> extraTargets = null, bool broadcast = true)
        {
            _udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            if (broadcast) _to.Add(new IPEndPoint(IPAddress.Broadcast, listenPort));
            if (extraTargets != null) _to.AddRange(extraTargets);
            Update(tcpPort, code, name, humans, build);
            Task.Run(Loop);
        }

        public void Update(int tcpPort, string code, string name, int humans, string build) => _payload = Build(tcpPort, code, name, humans, build);

        public static byte[] Build(int tcpPort, string code, string name, int humans, string build)
        {
            var w = new WireWriter(new byte[256]);
            w.Bytes(Magic, 0, Magic.Length);
            w.U8(NetVersion.Protocol);
            w.I16(tcpPort);
            w.Str(code ?? "", RoomCodes.Length);
            w.Str(name ?? "", RoomMsg.NameMax);
            w.U8(Math.Max(0, Math.Min(255, humans)));
            w.Str(build ?? "", RoomMsg.BuildMax);
            var b = new byte[w.Pos];
            Buffer.BlockCopy(w.Buf, 0, b, 0, w.Pos);
            return b;
        }

        public static bool TryRead(byte[] d, int len, out LanGame g)
        {
            g = default;
            if (d == null || len < Magic.Length + 3) return false;
            for (int i = 0; i < Magic.Length; i++) if (d[i] != Magic[i]) return false;
            var r = new WireReader(d, Magic.Length, len);
            g.Protocol = r.U8();
            g.Port = r.I16() & 0xFFFF;
            g.Code = r.Str(RoomCodes.Length);
            g.Name = ChatFilter.CleanName(r.Str(RoomMsg.NameMax));
            g.Humans = r.U8();
            g.Build = r.Str(RoomMsg.BuildMax);
            return !r.Bad && g.Port > 0;
        }

        private async Task Loop()
        {
            while (!_stop)
            {
                byte[] p = _payload;
                foreach (IPEndPoint ep in _to)
                {
                    try { await _udp.SendAsync(p, p.Length, ep).ConfigureAwait(false); Interlocked.Increment(ref Sent); }
                    catch (Exception) { /* no network right now: try again next second */ }
                }
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            _stop = true;
            try { _udp.Close(); } catch (Exception) { }
        }
    }

    /// <summary>b15-zero-cost: hears LanBeacons (see there). Thread-safe reads; Games() lists the ones heard recently.</summary>
    public sealed class LanListener : IDisposable
    {
        private readonly UdpClient _udp;
        private readonly ConcurrentDictionary<string, LanGame> _seen = new ConcurrentDictionary<string, LanGame>();
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private volatile bool _stop;
        public const int ForgetMs = 4000;

        /// <summary>Null when the port cannot be opened (another game on this computer is listening): no list, no error.</summary>
        public static LanListener TryStart(int port = LanBeacon.Port)
        {
            try { return new LanListener(port); }
            catch (Exception) { return null; }
        }

        private LanListener(int port)
        {
            _udp = new UdpClient(AddressFamily.InterNetwork);
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            Task.Run(Loop);
        }

        private async Task Loop()
        {
            while (!_stop)
            {
                UdpReceiveResult r;
                try { r = await _udp.ReceiveAsync().ConfigureAwait(false); }
                catch (Exception) { if (_stop) return; await Task.Delay(200).ConfigureAwait(false); continue; }
                if (!LanBeacon.TryRead(r.Buffer, r.Buffer.Length, out LanGame g)) continue;
                g.Address = r.RemoteEndPoint.Address.ToString();
                g.SeenMs = _clock.ElapsedMilliseconds;
                _seen[g.Address + ":" + g.Port] = g;
            }
        }

        /// <summary>Rooms heard in the last ForgetMs, the same network protocol as this game only.</summary>
        public List<LanGame> Games()
        {
            long now = _clock.ElapsedMilliseconds;
            var list = new List<LanGame>();
            foreach (KeyValuePair<string, LanGame> kv in _seen)
                if (now - kv.Value.SeenMs <= ForgetMs && kv.Value.Protocol == NetVersion.Protocol) list.Add(kv.Value);
            list.Sort((a, b) => string.CompareOrdinal(a.Address + a.Port, b.Address + b.Port));
            return list;
        }

        /// <summary>The local game with this room code, if one was heard recently.</summary>
        public bool TryFind(string code, out LanGame game)
        {
            foreach (LanGame g in Games()) if (!string.IsNullOrEmpty(code) && g.Code == code) { game = g; return true; }
            game = default;
            return false;
        }

        public void Dispose()
        {
            _stop = true;
            try { _udp.Close(); } catch (Exception) { }
        }
    }
}
