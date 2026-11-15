using System;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Net
{
    public enum LockstepStatus
    {
        Running,
        /// <summary>The match reached its end on this machine.</summary>
        Ended,
        /// <summary>Two machines' worlds differ (DesyncTick says where). The match stops for everyone.</summary>
        Desynced,
        /// <summary>The host is gone (client side only).</summary>
        HostLeft,
    }

    /// <summary>Tuning of one lockstep session (defaults are the shipped ones; tests shorten the timeouts).</summary>
    public sealed class LockstepOptions
    {
        /// <summary>The host waits this long for a late human input before it repeats that human's last input.</summary>
        public int LateMs = 100;
        /// <summary>
        /// online-rooms-9: a human the host had to repeat stays "lagging" this long: its inputs are used when they are in
        /// time and repeated when not, and nobody waits for it. Before, an on-time input cleared lagging at once, so a peer
        /// hovering at the edge cost everyone a LateMs wait again and again (77 s of waits in an 8 minute match).
        /// </summary>
        public int LagHoldMs = 2000;
        /// <summary>A human the host has not heard from for this long becomes a bot (at one announced tick).</summary>
        public int DropAfterMs = 6000;
        /// <summary>A client that has not heard from the host for this long ends with HostLeft.</summary>
        public int HostLostMs = 8000;
        /// <summary>At most one packet per peer this often while there is news, and a keep-alive this often without.</summary>
        public int SendEveryMs = 30;
        public int KeepAliveMs = 250;
        /// <summary>Largest per-client input lead the adaptive delay may reach, in milliseconds (converted to ticks at the match pace).</summary>
        public int MaxLeadMs = 700;   // online-rooms-9: the adaptive lead's ceiling (LeadPolicy: floor 100 ms)
        /// <summary>Ticks of wall-clock backlog the stepping clock keeps after a stall (catch-up burst cap).</summary>
        public int BacklogTicks = 6;
    }

    /// <summary>Receives a transport event the lockstep layer does not own (room and chat messages, type 20 and up).</summary>
    public delegate void TransportEventHandler(ref TransportEvent ev, long nowMs);

    /// <summary>Fills one human's input for a tick on this machine (keyboard and mouse, a gamepad, or the autoplay pilot).</summary>
    public delegate void LocalInputSampler(World w, int actor, int tick, InputFrame into);

    /// <summary>
    /// Deterministic lockstep with the host as sequencer. Every machine runs the whole match; only the humans' inputs travel.
    /// Each human sends its input for tick t (lead ticks ahead of its own sim) to the host. The host packs every human's
    /// input for tick t into one bundle (repeating a late human's last input after LateMs, and announcing "this human is a
    /// bot from tick t" when someone leaves) and sends the bundles to everyone. Nobody, the host included, steps tick t
    /// before it holds bundle t, so every machine applies identical inputs in identical order. Every HashEvery ticks each
    /// machine fingerprints its world; the fingerprints travel with the packets, and a mismatch stops the match for all
    /// with the tick it was found at. On a lossy link everything not yet acknowledged is resent in every packet; on a
    /// reliable one (WebSocket) each item is sent once and the host broadcasts.
    /// Single-threaded: call Update from one thread.
    /// </summary>
    public sealed class LockstepSession
    {
        public const int Window = 512;
        private const int MsgInputs = 1, MsgBundles = 2;
        private const int HashKeep = 64;
        private const byte FlagInput = 1, FlagToBot = 2;

        public readonly World World;
        public readonly MatchSetup Setup;
        public readonly bool IsHost;
        public readonly int LocalSlot;
        public readonly int LocalActor;
        public LockstepStatus Status { get; private set; }
        /// <summary>World tick at which a desync was found (-1: none).</summary>
        public int DesyncTick { get; private set; } = -1;
        /// <summary>Ticks this machine has stepped (equals World.Tick while the match runs).</summary>
        public int Stepped { get; private set; }
        /// <summary>This machine's current input lead in ticks (the felt input delay).</summary>
        public int Lead { get; private set; }
        /// <summary>Host: inputs it had to repeat because they came late. Client: own inputs the host replaced.</summary>
        public int LateInputs { get; private set; }
        /// <summary>Milliseconds this machine waited for the network (stalls).</summary>
        public long StallMs { get; private set; }
        /// <summary>online-rooms-9: stall time by the slot it waited for (the host: the human whose input was missing; a client: slot 0).</summary>
        public long[] StallBySlotMs { get; private set; }
        /// <summary>online-rooms-9: the smallest and largest lead this machine used (a client's adapts; the host's is fixed).</summary>
        public int LeadMin => _lead != null ? _lead.Min : Lead;
        public int LeadMax => _lead != null ? _lead.Max : Lead;
        /// <summary>The clock of the last Update (milliseconds).</summary>
        public long NowMs => _nowMs;
        private LeadPolicy _lead;
        private long[] _lagUntil;
        private int _blame = -1;
        /// <summary>online-rooms-3: ConfigHash.Of the config this machine built the world from.</summary>
        public ulong MyConfigHash { get; }
        /// <summary>online-rooms-3: StateHash.Of the world right after it was built (tick 0).</summary>
        public ulong MyWorldHash0 { get; }
        /// <summary>online-rooms-3: set when this machine's setup differs from the host's (then Status is Desynced at tick 0).</summary>
        public string SetupMismatch { get; private set; }
        /// <summary>online-rooms-5: the fingerprints behind SetupMismatch, for logs only (never on screen).</summary>
        public string SetupMismatchDetail { get; private set; }
        private long _stallSeen;
        public long BytesSent { get; private set; }
        /// <summary>The transport's reason when our own connection closed (plain words), else null.</summary>
        public string ClosedReason { get; private set; }
        /// <summary>Milliseconds the network has kept this machine waiting right now (0 when ticking): the "waiting for" cue.</summary>
        public long WaitingMs => _waitingSince >= 0 ? _nowMs - _waitingSince : 0;
        /// <summary>Interpolation fraction for the view, 0..1, between the last tick and the next.</summary>
        public double Alpha => _acc <= 0 ? 0 : (_acc >= _tickMs ? 1 : _acc / _tickMs);

        /// <summary>The local human's input source (null = neutral frames).</summary>
        public LocalInputSampler Sampler;
        /// <summary>Raised after every state fingerprint: (world tick, hash). Headless runs log it.</summary>
        public Action<int, ulong> HashRecorded;
        /// <summary>Raised when a human becomes a bot: (slot, actor, world tick of the hand-over).</summary>
        public Action<int, int, int> HumanToBot;
        /// <summary>Raised after every step with the world (the view reads it).</summary>
        public Action<World> Ticked;
        /// <summary>Test seam: may edit the decoded frames of a tick before it is applied (proves desync detection).</summary>
        public Action<int, InputFrame[]> TestTamper;
        /// <summary>Host: the START packet, resent to every human not heard from yet (it may have been lost).</summary>
        /// <summary>Gets every packet of type 20 and up (chat): kept apart from the inputs, so it can never change the match.</summary>
        public TransportEventHandler Other;
        public byte[] StartPacket;
        public int StartPacketLength;

        private readonly ITransport _t;
        private readonly LockstepOptions _o;
        private readonly double _tickMs;
        private readonly int _h;
        private readonly int _bundleSize;
        private readonly InputFrame _scratch = SimInput.EmptyInput();
        private readonly InputFrame[] _frames;
        private readonly InputFrame[] _slotFrames;
        private readonly InputFrame _sample = SimInput.EmptyInput();
        private readonly byte[] _pkt = new byte[Peers.MaxPacket];
        private readonly byte[] _enc = new byte[InputCodec.Size];

        // Bundles (host: produced; client: received), ring by tick.
        private readonly byte[] _bundles;
        private readonly int[] _bundleTick;
        /// <summary>Highest tick t such that every bundle 0..t is held (-1: none).</summary>
        private int _bundlesThrough = -1;
        /// <summary>Per slot: became a bot at the stepping side (applied).</summary>
        private readonly bool[] _slotIsBot;

        // Local inputs (client: own; host: slot 0 lives in _in[0]).
        private readonly byte[] _mine = new byte[Window * InputCodec.Size];
        private readonly int[] _mineTick = new int[Window];
        private int _mineThrough = -1;

        // Host side.
        private readonly byte[][] _in;
        private readonly int[][] _inTick;
        private readonly int[] _inThrough;
        private readonly long[] _lastHeard;
        private readonly bool[] _heardInput;
        private readonly bool[] _lagging;
        private readonly bool[] _dropPending;
        private readonly bool[] _gone;
        private readonly int[] _ackedBundle;
        private readonly int[] _sentBundle;
        private readonly long[] _lastSendTo;
        private readonly long[] _lastStartSend;
        private readonly byte[][] _lastInput;
        private int _producedThrough = -1;
        private long _waitSince = -1;

        // Client side.
        private int _hostAckedInput = -1;
        private int _sentInput = -1;
        private long _lastFromHost;
        private long _lastSendHost;

        // Hashes.
        private readonly int[] _hashTick = new int[HashKeep];
        private readonly ulong[] _hash = new ulong[HashKeep];
        private int _hashCount;
        private readonly int[] _theirTick;
        private readonly ulong[] _theirHash;

        private long _lastNow = -1;
        private long _nowMs;
        private long _waitingSince = -1;
        private double _acc;
        private long _startMs = -1;

        /// <summary>Above this many humans every machine sends half as often (see the constructor).</summary>
        public const int BigRoomHumans = 16;
        private readonly int _sendEveryMs;
        /// <summary>How often this machine sends new inputs or bundles, in ms.</summary>
        public int SendEveryMs => _sendEveryMs;

        public LockstepSession(ITransport transport, MatchSetup setup, GameConfig cfg, LockstepOptions options = null)
        {
            _t = transport ?? throw new ArgumentNullException(nameof(transport));
            Setup = setup ?? throw new ArgumentNullException(nameof(setup));
            _o = options ?? new LockstepOptions();
            IsHost = transport.LocalPeer == Peers.Host;
            LocalSlot = setup.SlotOfPeer(transport.LocalPeer);
            if (LocalSlot < 0) throw new ArgumentException("this peer has no seat in the match setup", nameof(setup));
            LocalActor = setup.Humans[LocalSlot].Actor;
            // online-rooms-3: what this machine builds the match from, fingerprinted before and right after the world exists,
            // so a different setup is reported as that (tick 0) instead of as a simulation drift at tick 30.
            MyConfigHash = ConfigHash.Of(cfg);
            World = World.CreateWorld(setup.ToWorldOptions(cfg));
            MyWorldHash0 = StateHash.Of(World);
            // online-rooms-5: plain words on screen; the fingerprints go to SetupMismatchDetail (logs only).
            if (setup.ConfigHash != 0 && setup.ConfigHash != MyConfigHash)
            {
                SetupMismatch = "Your game's match rules differ from the host's. Both players need the same update.";
                SetupMismatchDetail = $"rules 0x{MyConfigHash:X16}, host 0x{setup.ConfigHash:X16}";
            }
            else if (setup.WorldHash0 != 0 && setup.WorldHash0 != MyWorldHash0)
            {
                SetupMismatch = "Your game built a different arena from the host's. Both players need the same update.";
                SetupMismatchDetail = $"arena 0x{MyWorldHash0:X16}, host 0x{setup.WorldHash0:X16}";
            }
            if (SetupMismatch != null) { Status = LockstepStatus.Desynced; DesyncTick = 0; }
            // Wall milliseconds per tick: the match pace (sim ticks per wall second / SimHz) speeds the clock, never the rules.
            _tickMs = 1000.0 / (cfg.SimHz * WallPace(setup, cfg));
            // A big room sends half as often (two ticks per packet): the relay's room object sees one message per human per
            // send, and 32 humans x 33 a second would sit at its 1,000 a second soft limit. 17+ humans: 66 ms, 32 x 15 = 480/s.
            _sendEveryMs = setup.Humans.Length > BigRoomHumans ? Math.Max(_o.SendEveryMs, 2 * _o.SendEveryMs) : _o.SendEveryMs;
            _h = setup.Humans.Length;
            _bundleSize = _h * (1 + InputCodec.Size);
            _bundles = new byte[Window * _bundleSize];
            _bundleTick = new int[Window];
            for (int i = 0; i < Window; i++) { _bundleTick[i] = -1; _mineTick[i] = -1; }
            _frames = new InputFrame[World.ActorCount];
            _slotFrames = new InputFrame[_h];
            for (int i = 0; i < _h; i++) _slotFrames[i] = SimInput.EmptyInput();
            _slotIsBot = new bool[_h];
            _theirTick = new int[_h * 2];
            _theirHash = new ulong[_h * 2];
            for (int i = 0; i < _theirTick.Length; i++) _theirTick[i] = -1;
            Lead = Math.Max(1, setup.InputDelay);
            StallBySlotMs = new long[_h];
            // online-rooms-9: a client's lead adapts both ways (LeadPolicy); the host's own input never travels, so its lead
            // stays the room's start delay (it is also the joiners' buffer against jitter on the bundles).
            if (!IsHost) { _lead = LeadPolicy.For(Lead, _tickMs, _o.MaxLeadMs); Lead = _lead.Lead; }
            if (IsHost)
            {
                _in = new byte[_h][];
                _inTick = new int[_h][];
                _inThrough = new int[_h];
                _lastHeard = new long[_h];
                _heardInput = new bool[_h];
                _lagging = new bool[_h];
                _lagUntil = new long[_h];
                _dropPending = new bool[_h];
                _gone = new bool[_h];
                _ackedBundle = new int[_h];
                _sentBundle = new int[_h];
                _lastSendTo = new long[_h];
                _lastStartSend = new long[_h];
                _lastInput = new byte[_h][];
                for (int k = 0; k < _h; k++)
                {
                    _in[k] = new byte[Window * InputCodec.Size];
                    _inTick[k] = new int[Window];
                    for (int i = 0; i < Window; i++) _inTick[k][i] = -1;
                    _inThrough[k] = -1;
                    _ackedBundle[k] = -1;
                    _sentBundle[k] = -1;
                    _lastInput[k] = new byte[InputCodec.Size];
                    InputCodec.EncodeNeutral(_lastInput[k], 0);
                }
            }
        }

        /// <summary>The match pace a setup runs at (0 = the config's default speed), as SimDriverBehaviour reads it.</summary>
        public static double WallPace(MatchSetup s, GameConfig cfg) => s.Pace > 0 ? s.Pace : (cfg.DefaultSpeed > 0 ? cfg.DefaultSpeed : 1);

        /// <summary>Wall milliseconds per tick.</summary>
        public double TickMs => _tickMs;

        /// <summary>Hashes this machine has recorded, newest last: (world tick, hash).</summary>
        public int HashCount => Math.Min(_hashCount, HashKeep);

        /// <summary>The fingerprint recorded at world tick, if still kept.</summary>
        public bool TryGetHash(int tick, out ulong hash)
        {
            for (int i = 0; i < HashKeep; i++)
                if (_hashTick[i] == tick && i < _hashCount) { hash = _hash[i]; return true; }
            hash = 0;
            return false;
        }

        /// <summary>Peer p's seat has become a bot (it left or went silent).</summary>
        public bool SlotIsBot(int slot) => _slotIsBot[slot];

        /// <summary>One frame of work: read the network, sequence (host), step what is due, send what is new.</summary>
        public void Update(long nowMs)
        {
            if (_lastNow < 0) { _lastNow = nowMs; _startMs = nowMs; _lastFromHost = nowMs; if (IsHost) for (int k = 0; k < _h; k++) _lastHeard[k] = nowMs; }
            long dt = Math.Max(0, nowMs - _lastNow);
            _nowMs = nowMs;
            _lastNow = nowMs;
            while (_t.Poll(out TransportEvent ev)) Handle(ref ev, nowMs);
            if (Status == LockstepStatus.Running || Status == LockstepStatus.Ended)
            {
                if (Status == LockstepStatus.Running)
                {
                    _acc += dt;
                    double cap = _o.BacklogTicks * _tickMs;
                    if (_acc > cap) _acc = cap;
                    if (Stepped == 0 && _mineThrough < 0) GenerateLocal(Lead - 1, true);
                    if (IsHost) { DetectDrops(nowMs); Produce(nowMs); }
                    StepDue(nowMs);
                }
                if (!IsHost && Status == LockstepStatus.Running && nowMs - _lastFromHost > _o.HostLostMs) Status = LockstepStatus.HostLeft;
            }
            Send(nowMs);
        }

        /// <summary>Feeds one transport event (Update polls by itself; a room client forwards the rest of its batch here).</summary>
        public void Handle(ref TransportEvent ev, long nowMs)
        {
            switch (ev.Kind)
            {
                case TransportEventKind.PeerLeft:
                    if (IsHost) { int k = Setup.SlotOfPeer(ev.From); if (k > 0 && !_gone[k]) _dropPending[k] = true; }
                    else if (ev.From == Peers.Host && Status == LockstepStatus.Running) Status = LockstepStatus.HostLeft;
                    return;
                case TransportEventKind.Closed:
                    // Our own link is gone: for a client that is the host gone; for the host, everyone is gone.
                    if (Status == LockstepStatus.Running) { Status = LockstepStatus.HostLeft; ClosedReason = ev.Reason; }
                    return;
                case TransportEventKind.Data:
                    break;
                default:
                    return;
            }
            if (ev.Data == null || ev.Length < 1) return;
            int type = ev.Data[0];
            if (IsHost && type == MsgInputs) ReadInputs(ev.From, ev.Data, ev.Length, nowMs);
            else if (!IsHost && type == MsgBundles && ev.From == Peers.Host) ReadBundles(ev.Data, ev.Length, nowMs);
            else if (type >= 20) Other?.Invoke(ref ev, nowMs);
        }

        // ---------------------------------------------------------------- local input

        private void GenerateLocal(int upTo, bool neutral)
        {
            if (upTo <= _mineThrough) return;
            if (neutral || Sampler == null)
            {
                InputCodec.EncodeNeutral(_enc, 0);
            }
            else
            {
                _sample.CopyFrom(SimInput.EmptyInput());
                Sampler(World, LocalActor, Stepped, _sample);
                InputCodec.Encode(_sample, _enc, 0);
            }
            for (int t = _mineThrough + 1; t <= upTo; t++)
            {
                int i = t & (Window - 1);
                if (IsHost)
                {
                    Buffer.BlockCopy(_enc, 0, _in[0], i * InputCodec.Size, InputCodec.Size);
                    _inTick[0][i] = t;
                    _inThrough[0] = t;
                }
                Buffer.BlockCopy(_enc, 0, _mine, i * InputCodec.Size, InputCodec.Size);
                _mineTick[i] = t;
                // Several ticks from one sample (the lead just grew): one-shot presses only on the first of them.
                if (t == _mineThrough + 1) ClearOneShots(_enc, 0);
            }
            _mineThrough = upTo;
        }

        private static void ClearOneShots(byte[] b, int o)
        {
            b[o + 6] = (byte)(b[o + 6] & ~(16 | 32));   // Jump, TerraformSwitch
            b[o + 7] = unchecked((byte)(sbyte)-1);    // ClassPick
            b[o + 8] = 0;                              // StationBuy
            b[o + 9] = 0;                              // PingKind
        }

        // ---------------------------------------------------------------- host: sequencing

        private void DetectDrops(long now)
        {
            for (int k = 1; k < _h; k++)
                if (!_gone[k] && !_dropPending[k] && now - _lastHeard[k] > _o.DropAfterMs) _dropPending[k] = true;
        }

        private void Produce(long now)
        {
            while (true)
            {
                int s = _producedThrough + 1;
                if (s >= Stepped + Window / 2) return;
                int idx = s & (Window - 1);
                // A bundle goes out as soon as every human's input for its tick is in. A missing input is only replaced
                // once the host itself needs the tick (s == Stepped): a lagging human is repeated at once, anyone else
                // after LateMs of waiting (then marked lagging). Before its first input a human is waited for, up to the
                // silence timeout, so a slow START never costs anyone their first seconds.
                bool needNow = s <= Stepped;
                bool ready = true;
                for (int k = 0; k < _h && ready; k++)
                {
                    if (_gone[k] || _dropPending[k]) continue;
                    if (_inTick[k][idx] == s) continue;
                    // Host's own input is generated as it steps: never wait on a timer for it.
                    if (k == 0) return;
                    if (!needNow) return;
                    if (_lagging[k]) continue;
                    if (_waitSince < 0) _waitSince = now;
                    if (_heardInput[k] && now - _waitSince >= _o.LateMs) { _lagging[k] = true; _lagUntil[k] = now + _o.LagHoldMs; continue; }
                    _blame = k;
                    ready = false;
                }
                if (!ready) return;
                _waitSince = -1;
                _blame = -1;
                int off = idx * _bundleSize;
                for (int k = 0; k < _h; k++)
                {
                    int o = off + k * (1 + InputCodec.Size);
                    if (_gone[k]) { _bundles[o] = 0; continue; }
                    if (_dropPending[k])
                    {
                        _bundles[o] = FlagToBot;
                        _dropPending[k] = false;
                        _gone[k] = true;
                        continue;
                    }
                    _bundles[o] = FlagInput;
                    if (_inTick[k][idx] == s)
                    {
                        Buffer.BlockCopy(_in[k], idx * InputCodec.Size, _bundles, o + 1, InputCodec.Size);
                        Buffer.BlockCopy(_in[k], idx * InputCodec.Size, _lastInput[k], 0, InputCodec.Size);
                        if (now >= _lagUntil[k]) _lagging[k] = false;   // online-rooms-9: on time again for LagHoldMs first
                    }
                    else
                    {
                        if (_lagging[k]) _lagUntil[k] = now + _o.LagHoldMs;   // online-rooms-9: still late: stay lagging
                        // Late: keep this human moving as they last did, without repeating a one-shot press.
                        ClearOneShots(_lastInput[k], 0);
                        Buffer.BlockCopy(_lastInput[k], 0, _bundles, o + 1, InputCodec.Size);
                        LateInputs++;
                    }
                }
                _bundleTick[idx] = s;
                _producedThrough = s;
                _bundlesThrough = s;
            }
        }

        private void ReadInputs(int from, byte[] d, int len, long now)
        {
            int k = Setup.SlotOfPeer(from);
            if (k <= 0 || _gone[k]) return;
            var r = new WireReader(d, 1, len);
            int theirDesync = r.I32();
            int ack = r.I32();
            if (!ReadHashes(ref r, k)) return;
            int first = r.I32();
            int count = r.U8();
            if (r.Bad || count > 64 || r.Left < count * InputCodec.Size) return;
            _lastHeard[k] = now;
            if (ack > _ackedBundle[k] && ack <= _producedThrough) _ackedBundle[k] = ack;
            for (int j = 0; j < count; j++)
            {
                int t = first + j;
                int at = r.Pos + j * InputCodec.Size;
                // Only ticks the host has not sequenced yet and that fit the window; anything else is stale or hostile.
                if (t <= _producedThrough || t > _producedThrough + Window / 2 || t < 0) continue;
                int idx = t & (Window - 1);
                if (_inTick[k][idx] == t) continue;
                Buffer.BlockCopy(d, at, _in[k], idx * InputCodec.Size, InputCodec.Size);
                _inTick[k][idx] = t;
                _heardInput[k] = true;
            }
            // Everything up to the last sequenced tick is no longer needed (a late input there was replaced), so the
            // acknowledgement never sticks at a hole and the client never resends stale inputs forever.
            if (_inThrough[k] < _producedThrough) _inThrough[k] = _producedThrough;
            while (_inTick[k][(_inThrough[k] + 1) & (Window - 1)] == _inThrough[k] + 1) _inThrough[k]++;
            if (theirDesync >= 0) Desync(theirDesync);
            CheckHashes();
        }

        // ---------------------------------------------------------------- client: receiving

        private void ReadBundles(byte[] d, int len, long now)
        {
            var r = new WireReader(d, 1, len);
            int theirDesync = r.I32();
            int ack = r.I32();
            // online-rooms-9: the host's stepping tick and every slot's input horizon relative to it (the margin), in
            // broadcasts too: the relay path used to carry no acknowledgement at all, so a client's lead could only grow.
            int hostStepped = r.I32();
            int na = r.U8();
            int myMargin = int.MinValue;
            for (int i = 0; i < na; i++) { int d = (sbyte)r.U8(); if (i == LocalSlot && d != -128) myMargin = d; }   // -128: none of ours in yet
            if (!ReadHashes(ref r, 0)) return;
            int first = r.I32();
            int count = r.U8();
            int ended = r.U8();
            if (r.Bad || r.Left < count * _bundleSize) return;
            _lastFromHost = now;
            if (ack > _hostAckedInput && ack <= _mineThrough) _hostAckedInput = ack;
            for (int j = 0; j < count; j++)
            {
                int t = first + j;
                if (t < Stepped || t >= Stepped + Window || t <= _bundlesThrough) continue;
                int idx = t & (Window - 1);
                if (_bundleTick[idx] == t) continue;
                Buffer.BlockCopy(d, r.Pos + j * _bundleSize, _bundles, idx * _bundleSize, _bundleSize);
                _bundleTick[idx] = t;
            }
            while (_bundleTick[(_bundlesThrough + 1) & (Window - 1)] == _bundlesThrough + 1) _bundlesThrough++;
            // Adaptive lead (LeadPolicy): how many ticks ahead of the host's stepping our inputs were when it sent this.
            if (myMargin != int.MinValue && _lead != null && hostStepped >= 0)
            {
                _lead.OnMargin(myMargin, now);
                Lead = _lead.Lead;
            }
            if (theirDesync >= 0) Desync(theirDesync);
            CheckHashes();
            _ = ended;
        }

        // ---------------------------------------------------------------- stepping

        private void StepDue(long now)
        {
            int extra = 0;
            bool waited = false;
            while (Status == LockstepStatus.Running)
            {
                bool due = _acc >= _tickMs;
                // A client that fell behind the host (bundles queued up) catches up a little faster than real time.
                bool behind = !IsHost && _bundlesThrough - Stepped > Lead + 3 && extra < 2;
                if (!due && !behind) break;
                if (Stepped > _bundlesThrough) { waited = due; break; }
                if (!due) extra++;
                else _acc -= _tickMs;
                StepOne();
                if (IsHost) Produce(now);
            }
            // Wall time spent waiting for a bundle (counted per call, so it no longer depends on how often Update runs).
            if (waited)
            {
                if (_waitingSince >= 0)
                {
                    long add = now - _stallSeen;
                    StallMs += add;
                    int slot = IsHost ? _blame : 0;
                    if (slot >= 0 && slot < _h) StallBySlotMs[slot] += add;
                }
                else _waitingSince = now;
                _stallSeen = now;
            }
            else if (Stepped > 0) _waitingSince = -1;
        }

        private void StepOne()
        {
            int s = Stepped;
            int idx = s & (Window - 1);
            int off = idx * _bundleSize;
            for (int a = 0; a < _frames.Length; a++) _frames[a] = null;
            for (int k = 0; k < _h; k++)
            {
                int o = off + k * (1 + InputCodec.Size);
                byte flags = _bundles[o];
                int actor = Setup.Humans[k].Actor;
                if ((flags & FlagToBot) != 0 && !_slotIsBot[k])
                {
                    _slotIsBot[k] = true;
                    MatchTick.HandToBot(World, actor);
                    HumanToBot?.Invoke(k, actor, World.Tick);
                }
                if ((flags & FlagInput) != 0 && !_slotIsBot[k] && actor < _frames.Length)
                {
                    InputCodec.Decode(_bundles, o + 1, _slotFrames[k]);
                    _frames[actor] = _slotFrames[k];
                    if (k == LocalSlot && !IsHost && _mineTick[idx] == s && !SameBytes(_bundles, o + 1, _mine, idx * InputCodec.Size))
                    {
                        // The host had to replace our input (it came too late): lead one tick more.
                        LateInputs++;
                        // One step up per half second: a burst of late ticks is one late episode, not ten.
                        if (_lead != null) { _lead.OnLate(_nowMs); Lead = _lead.Lead; }
                    }
                }
            }
            TestTamper?.Invoke(s, _frames);
            // This machine's next input goes Lead ticks ahead of the tick being stepped.
            GenerateLocal(s + Lead, false);
            using (SimThreadGuard.Enter()) MatchTick.AdvanceHumans(World, _frames, _scratch);
            Stepped = s + 1;
            // Tick 1 too: a mismatch there means the first inputs or the first step differ, not a slow drift.
            if (World.Tick % Setup.HashEvery == 0 || World.Tick == 1) RecordHash(World.Tick, StateHash.Of(World));
            Ticked?.Invoke(World);
            if (World.Match.Phase == MatchPhase.Ended) Status = LockstepStatus.Ended;
        }

        private static bool SameBytes(byte[] a, int ao, byte[] b, int bo)
        {
            for (int i = 0; i < InputCodec.Size; i++) if (a[ao + i] != b[bo + i]) return false;
            return true;
        }

        // ---------------------------------------------------------------- hashes and desync

        /// <summary>The tick 0 fingerprint, for logs (it is checked through the start message, not exchanged).</summary>
        public ulong WorldHashAtStart => MyWorldHash0;

        private void RecordHash(int tick, ulong h)
        {
            int i = _hashCount % HashKeep;
            _hashTick[i] = tick;
            _hash[i] = h;
            _hashCount++;
            HashRecorded?.Invoke(tick, h);
            CheckHashes();
        }

        private bool ReadHashes(ref WireReader r, int slot)
        {
            int n = r.U8();
            if (n > 2) { r.Bad = true; return false; }
            for (int j = 0; j < n; j++)
            {
                int tick = r.I32();
                ulong h = r.U64();
                int at = slot * 2 + j;
                if (!r.Bad && tick > 0) { _theirTick[at] = tick; _theirHash[at] = h; }
            }
            return !r.Bad;
        }

        private void CheckHashes()
        {
            for (int i = 0; i < _theirTick.Length; i++)
            {
                int tick = _theirTick[i];
                if (tick <= 0) continue;
                if (!TryGetHash(tick, out ulong mine))
                {
                    if (tick < OldestKeptHashTick()) _theirTick[i] = -1;   // too old to check: drop it
                    continue;
                }
                _theirTick[i] = -1;
                if (mine != _theirHash[i]) Desync(tick);
            }
        }

        private int OldestKeptHashTick()
        {
            if (_hashCount == 0) return 0;
            int n = Math.Min(_hashCount, HashKeep);
            int oldest = int.MaxValue;
            for (int i = 0; i < n; i++) if (_hashTick[i] < oldest) oldest = _hashTick[i];
            return oldest;
        }

        private void Desync(int tick)
        {
            if (Status == LockstepStatus.Desynced) { if (tick < DesyncTick) DesyncTick = tick; return; }
            Status = LockstepStatus.Desynced;
            DesyncTick = tick;
        }

        private void WriteHashes(ref WireWriter w)
        {
            int n = Math.Min(_hashCount, 2);
            w.U8(n);
            for (int j = n; j >= 1; j--)
            {
                int i = (_hashCount - j) % HashKeep;
                w.I32(_hashTick[i]);
                w.U64(_hash[i]);
            }
        }

        // ---------------------------------------------------------------- sending

        private void Send(long now)
        {
            if (IsHost) SendHost(now);
            else SendClient(now);
        }

        private void SendClient(long now)
        {
            int from = _t.Reliable ? _sentInput + 1 : _hostAckedInput + 1;
            bool news = _mineThrough >= from || Status == LockstepStatus.Desynced;
            if (now - _lastSendHost < (news ? _sendEveryMs : _o.KeepAliveMs)) return;
            int count = Math.Max(0, Math.Min(_mineThrough - from + 1, 64));
            var w = new WireWriter(_pkt);
            w.U8(MsgInputs);
            w.I32(DesyncTick);
            w.I32(_bundlesThrough);
            WriteHashes(ref w);
            w.I32(from);
            w.U8(count);
            for (int j = 0; j < count; j++)
            {
                int t = from + j;
                w.Bytes(_mine, (t & (Window - 1)) * InputCodec.Size, InputCodec.Size);
            }
            if (w.Overflow) return;
            _t.Send(Peers.Host, _pkt, w.Pos);
            BytesSent += w.Pos;
            _lastSendHost = now;
            if (count > 0) _sentInput = from + count - 1;
        }

        private void SendHost(long now)
        {
            if (StartPacket != null)
                for (int k = 1; k < _h; k++)
                    if (!_heardInput[k] && !_gone[k] && now - _lastStartSend[k] >= 250)
                    {
                        _t.Send(Setup.Humans[k].Peer, StartPacket, StartPacketLength);
                        _lastStartSend[k] = now;
                    }
            int maxPer = Math.Max(1, (Peers.MaxPacket - 48 - _h) / _bundleSize);   // online-rooms-9: + the margins
            if (_t.Reliable)
            {
                // One broadcast carries every new bundle to everyone (relay messages cost; acks are not needed on TCP).
                // A 32-human bundle fits only two ticks in a packet, the same as a big room's send interval produces, so
                // a backlog (after a stall) goes out in up to three packets at once instead of never draining.
                for (int pkt = 0; pkt < 3; pkt++)
                {
                    int from = _sentBundle[0] + 1;
                    bool news = _producedThrough >= from || Status == LockstepStatus.Desynced;
                    if (pkt == 0 && now - _lastSendTo[0] < (news ? _sendEveryMs : _o.KeepAliveMs)) return;
                    if (pkt > 0 && _producedThrough < from) return;
                    int count = Math.Max(0, Math.Min(_producedThrough - from + 1, maxPer));
                    int len = BuildBundles(from, count, -1);
                    if (len <= 0) return;
                    _t.Send(Peers.Broadcast, _pkt, len);
                    BytesSent += len;
                    _lastSendTo[0] = now;
                    if (count > 0) _sentBundle[0] = from + count - 1;
                }
                return;
            }
            for (int k = 1; k < _h; k++)
            {
                if (_gone[k]) continue;
                int first = _ackedBundle[k] + 1;
                bool news = _producedThrough >= first || Status == LockstepStatus.Desynced;
                if (now - _lastSendTo[k] < (news ? _sendEveryMs : _o.KeepAliveMs)) continue;
                // Everything unacked, in up to three packets (a big room fits only two ticks in one).
                for (int pkt = 0; pkt < 3; pkt++)
                {
                    int from = first + pkt * maxPer;
                    if (pkt > 0 && _producedThrough < from) break;
                    int count = Math.Max(0, Math.Min(_producedThrough - from + 1, maxPer));
                    int len = BuildBundles(from, count, _inThrough[k]);
                    if (len <= 0) break;
                    _t.Send(Setup.Humans[k].Peer, _pkt, len);
                    BytesSent += len;
                }
                _lastSendTo[k] = now;
            }
        }

        private int BuildBundles(int from, int count, int ackInput)
        {
            var w = new WireWriter(_pkt);
            w.U8(MsgBundles);
            w.I32(DesyncTick);
            w.I32(ackInput);
            w.I32(Stepped);
            w.U8(_h);
            for (int k = 0; k < _h; k++)
            {
                int d = _gone[k] || _inThrough[k] < 0 ? -128 : _inThrough[k] - Stepped;
                w.U8(unchecked((byte)(sbyte)Math.Max(-128, Math.Min(127, d))));
            }
            WriteHashes(ref w);
            w.I32(from);
            w.U8(count);
            w.U8(Status == LockstepStatus.Ended ? 1 : 0);
            for (int j = 0; j < count; j++) w.Bytes(_bundles, ((from + j) & (Window - 1)) * _bundleSize, _bundleSize);
            return w.Overflow ? -1 : w.Pos;
        }
    }
}
