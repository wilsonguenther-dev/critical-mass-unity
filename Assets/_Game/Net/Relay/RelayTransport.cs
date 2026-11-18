using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace CriticalMass.Net.Relay
{
    /// <summary>
    /// ITransport over the room relay (server/relay: one small WebSocket service per room code). Plain .NET, no engine:
    /// the game, the headless test peer and the tests all use this one class. Two background tasks (receive, send) talk to
    /// the socket; the game thread only touches two lock-free queues through Send and Poll, so a slow network never blocks
    /// a frame. Reliable and ordered (TCP), so the lockstep layer sends each item once.
    /// Frames are defined in server/relay/src/protocol.js; keep both in step.
    /// </summary>
    public sealed class RelayTransport : ITransport, ILockableTransport, IDisposable
    {
        public const int ProtocolVersion = 1;
        private const byte OpData = 0, OpLock = 1, OpWelcome = 1, OpJoined = 2, OpLeft = 3, OpClosing = 4;

        private readonly ConcurrentQueue<TransportEvent> _in = new ConcurrentQueue<TransportEvent>();
        private readonly ConcurrentQueue<byte[]> _out = new ConcurrentQueue<byte[]>();
        private readonly SemaphoreSlim _outSignal = new SemaphoreSlim(0);
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly ClientWebSocket _ws = new ClientWebSocket();
        private volatile int _localPeer = -1;
        private volatile bool _closed;
        private string _closingReason;
        private Task _runTask;

        public int LocalPeer => _localPeer;
        public bool Reliable => true;
        /// <summary>The room code this transport joined (upper case).</summary>
        public string Code { get; }

        private RelayTransport(string code) { Code = code; }

        /// <summary>Normalises what a person typed: upper case, spaces and dashes removed; null if it cannot be a code.</summary>
        public static string CleanCode(string typed)
        {
            if (typed == null) return null;
            var sb = new System.Text.StringBuilder(6);
            foreach (char ch in typed)
            {
                char c = char.ToUpperInvariant(ch);
                if (c == ' ' || c == '-' || c == '\r' || c == '\n' || c == '\t') continue;
                if (RoomCodes.Alphabet.IndexOf(c) < 0) return null;
                sb.Append(c);
            }
            return sb.Length == RoomCodes.Length ? sb.ToString() : null;
        }

        /// <summary>Asks the relay for a new room. Returns (code, host key) or throws with a plain-words message.</summary>
        public static async Task<(string code, string key)> CreateRoomAsync(string baseUrl, CancellationToken ct)
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
            {
                HttpResponseMessage r;
                try { r = await http.PostAsync(baseUrl.TrimEnd('/') + "/rooms", new StringContent(""), ct).ConfigureAwait(false); }
                catch (Exception) { throw new RoomException("Could not reach the room service. Check your internet connection."); }
                if (!r.IsSuccessStatusCode) throw new RoomException("The room service is busy. Try again in a minute.");
                string body = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                string code = JsonField(body, "code"), key = JsonField(body, "key");
                if (CleanCode(code) == null || string.IsNullOrEmpty(key)) throw new RoomException("The room service sent an answer this game cannot read.");
                return (code, key);
            }
        }

        /// <summary>
        /// b15-zero-cost: the relay's own count of today's billed requests (GET /usage; server/relay counts what its rooms
        /// cost), or -1 when the relay cannot say (an older relay without /usage, offline). Never throws.
        /// </summary>
        public static async Task<double> FetchRequestsTodayAsync(string baseUrl, CancellationToken ct)
        {
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) })
                {
                    HttpResponseMessage r = await http.GetAsync(baseUrl.TrimEnd('/') + "/usage", ct).ConfigureAwait(false);
                    if (!r.IsSuccessStatusCode) return -1;
                    return JsonNumber(await r.Content.ReadAsStringAsync().ConfigureAwait(false), "requests");
                }
            }
            catch (Exception) { return -1; }
        }

        /// <summary>The number value of "name":123 in a small flat JSON object, or -1.</summary>
        internal static double JsonNumber(string json, string name)
        {
            if (json == null) return -1;
            string k = "\"" + name + "\"";
            int i = json.IndexOf(k, StringComparison.Ordinal);
            if (i < 0) return -1;
            i = json.IndexOf(':', i + k.Length);
            if (i < 0) return -1;
            int a = i + 1;
            while (a < json.Length && json[a] == ' ') a++;
            int b = a;
            while (b < json.Length && (char.IsDigit(json[b]) || json[b] == '.')) b++;
            return b > a && double.TryParse(json.Substring(a, b - a), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : -1;
        }

        /// <summary>Opens a socket to room code (as host when hostKey is set). Returns at once; Poll reports Connected or Closed.</summary>
        public static RelayTransport Connect(string baseUrl, string code, string hostKey)
        {
            var t = new RelayTransport(code);
            string ws = baseUrl.TrimEnd('/');
            if (ws.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) ws = "wss://" + ws.Substring(8);
            else if (ws.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) ws = "ws://" + ws.Substring(7);
            string url = ws + "/rooms/" + code + "/ws?v=" + ProtocolVersion + (string.IsNullOrEmpty(hostKey) ? "" : "&key=" + Uri.EscapeDataString(hostKey));
            t._runTask = Task.Run(() => t.Run(new Uri(url)));
            return t;
        }

        public void Send(int to, byte[] data, int length)
        {
            if (_closed || length < 0 || length > Peers.MaxPacket) return;
            var f = new byte[length + 2];
            f[0] = OpData;
            f[1] = (byte)to;
            Buffer.BlockCopy(data, 0, f, 2, length);
            _out.Enqueue(f);
            _outSignal.Release();
        }

        public void Lock()
        {
            if (_closed) return;
            _out.Enqueue(new[] { OpLock });
            _outSignal.Release();
        }

        public bool Poll(out TransportEvent ev) => _in.TryDequeue(out ev);

        public void Close()
        {
            if (_closed) return;
            _closed = true;
            try { _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch (Exception) { }
            _cts.CancelAfter(2000);
        }

        /// <summary>online-rooms-3: after Close, wait up to ms for the socket's background work to finish (so a quitting
        /// player never kills it mid-call). True when it finished.</summary>
        public bool WaitClosed(int ms)
        {
            try { return _runTask == null || _runTask.Wait(ms); } catch (Exception) { return true; }
        }

        /// <summary>online-rooms-3, tests only: drop the connection with no goodbye, like a pulled cable or a crashed game.</summary>
        public void Abort()
        {
            _closed = true;
            try { _ws.Abort(); } catch (Exception) { }
            try { _cts.Cancel(); } catch (Exception) { }
        }

        public void Dispose()
        {
            Close();
            try { _ws.Dispose(); } catch (Exception) { }
        }

        // ------------------------------------------------------------------------------------------------ background

        private async Task Run(Uri uri)
        {
            try
            {
                using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
                {
                    connectTimeout.CancelAfter(10000);
                    await _ws.ConnectAsync(uri, connectTimeout.Token).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                Finish("Could not reach the room service. Check your internet connection.");
                return;
            }
            Task sender = Task.Run(SendLoop);
            var buf = new byte[4096];
            try
            {
                while (_ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                {
                    int n = 0;
                    WebSocketReceiveResult r;
                    do
                    {
                        r = await _ws.ReceiveAsync(new ArraySegment<byte>(buf, n, buf.Length - n), _cts.Token).ConfigureAwait(false);
                        if (r.MessageType == WebSocketMessageType.Close) { Finish(_closingReason ?? (_closed ? "You left the room." : "The connection to the room was lost.")); return; }
                        n += r.Count;
                        if (n >= buf.Length) { Finish("The room service sent a message that was too large."); return; }
                    } while (!r.EndOfMessage);
                    if (r.MessageType == WebSocketMessageType.Binary) OnFrame(buf, n);
                }
            }
            catch (Exception e)
            {
                // Socket errors, cancellation: reported once below, naming the exception so a lost room says why.
                if (!_closed && _closingReason == null) _closingReason = "The connection to the room was lost (" + e.GetType().Name + ": " + e.Message + ").";
            }
            Finish(_closingReason ?? (_closed ? "You left the room." : "The connection to the room was lost."));
            await sender.ConfigureAwait(false);
        }

        private async Task SendLoop()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    await _outSignal.WaitAsync(_cts.Token).ConfigureAwait(false);
                    while (_out.TryDequeue(out byte[] f))
                    {
                        if (_ws.State != WebSocketState.Open) return;
                        await _ws.SendAsync(new ArraySegment<byte>(f), WebSocketMessageType.Binary, true, _cts.Token).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception)
            {
                // The receive loop reports the closed socket.
            }
        }

        private void OnFrame(byte[] b, int n)
        {
            if (n < 1) return;
            switch (b[0])
            {
                case OpData when n >= 2 && _localPeer >= 0:
                {
                    var payload = new byte[n - 2];
                    Buffer.BlockCopy(b, 2, payload, 0, n - 2);
                    _in.Enqueue(new TransportEvent { Kind = TransportEventKind.Data, From = b[1], Data = payload, Length = payload.Length });
                    return;
                }
                case OpWelcome when n >= 3:
                {
                    _localPeer = b[1];
                    _in.Enqueue(new TransportEvent { Kind = TransportEventKind.Connected, From = b[1] });
                    int count = b[2];
                    for (int i = 0; i < count && 3 + i < n; i++)
                        _in.Enqueue(new TransportEvent { Kind = TransportEventKind.PeerJoined, From = b[3 + i] });
                    return;
                }
                case OpJoined when n >= 2:
                    _in.Enqueue(new TransportEvent { Kind = TransportEventKind.PeerJoined, From = b[1] });
                    return;
                case OpLeft when n >= 2:
                    _in.Enqueue(new TransportEvent { Kind = TransportEventKind.PeerLeft, From = b[1] });
                    return;
                case OpClosing when n >= 2:
                    _closingReason = ReasonText(b[1]);
                    return;
            }
        }

        private int _finished;

        private void Finish(string reason)
        {
            if (Interlocked.Exchange(ref _finished, 1) != 0) return;
            _closed = true;
            _in.Enqueue(new TransportEvent { Kind = TransportEventKind.Closed, From = -1, Reason = reason });
            try { _cts.Cancel(); } catch (Exception) { }
            _outSignal.Release();
        }

        /// <summary>One plain sentence for each reason the relay gives (server/relay/src/protocol.js REASON).</summary>
        public const string NotFoundText = "There is no room with that code. Check the code and try again.";
        public const string HostNotReadyText = "The host's room is still opening.";

        /// <summary>online-rooms-3: worth asking again in a moment (the host's game may not have connected yet).</summary>
        public static bool IsRetryable(string reason) => reason == NotFoundText || reason == HostNotReadyText;

        public static string ReasonText(int code)
        {
            switch (code)
            {
                case 1: return NotFoundText;
                case 2: return "That room is full.";
                case 3: return "That room's match has already started.";
                case 4: return "The host closed the room.";
                case 5: case 6: return "The connection sent too much too fast and was closed.";
                case 7: return "The room closed after ten minutes with nothing happening.";
                case 8: return "This game is a different version from the room service. Update the game.";
                case 9: return "The room service did not understand this game.";
                case 10: return "The room reached its four-hour limit and closed.";
                case 11: return HostNotReadyText;
                default: return "The room closed.";
            }
        }

        /// <summary>The string value of "name":"..." in a small flat JSON object (the relay's only JSON answer).</summary>
        internal static string JsonField(string json, string name)
        {
            if (json == null) return null;
            string k = "\"" + name + "\"";
            int i = json.IndexOf(k, StringComparison.Ordinal);
            if (i < 0) return null;
            i = json.IndexOf(':', i + k.Length);
            if (i < 0) return null;
            int a = json.IndexOf('"', i + 1);
            if (a < 0) return null;
            int b = json.IndexOf('"', a + 1);
            return b > a ? json.Substring(a + 1, b - a - 1) : null;
        }
    }

    /// <summary>Room code shape, the same as the relay's (no look-alike characters: no 0/O, no 1/I/L).</summary>
    public static class RoomCodes
    {
        public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        public const int Length = 6;
    }

    /// <summary>A room step failed; Message is one plain sentence for the screen.</summary>
    public sealed class RoomException : Exception
    {
        public RoomException(string message) : base(message) { }
    }
}
