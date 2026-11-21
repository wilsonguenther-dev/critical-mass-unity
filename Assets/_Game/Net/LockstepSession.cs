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
        /// <summary>b15-rejoin, host: packets per second of match log fed to rejoining players (all of them together). The relay
        /// allows 90 messages a second per sender; the bundles take 33, so the feed stays well under the limit.</summary>
        public int RejoinFeedPerSec = 40;
        /// <summary>b15-rejoin, host on a lossy link: resend the log from the rejoiner's acknowledgement after this long without progress.</summary>
        public int RejoinFeedRetryMs = 400;
        /// <summary>b15-rejoin, rejoiner: at most this many log ticks stepped per Update while catching up (one frame's budget).</summary>
        public int CatchUpTicksPerUpdate = 1500;
        /// <summary>
        /// b15-zero-cost: a joiner sends its inputs in packets of this many ticks, sent when the packet's last tick is a
        /// multiple of it (so every joiner's packets cover the same ticks and the host's bundles go out in the same groups),
        /// or after (this - 1) ticks of waiting at most. 0 = by room size (<see cref="LockstepSession.TicksPerPacketFor"/>:
        /// 2); 1 = one packet per tick on SendEveryMs, as before b15. The relay bills every message
        /// a game sends it, so this is the room's cost; it adds at most one tick to when an input reaches the host.
        /// </summary>
        public int ClientTicksPerPacket = 0;
        /// <summary>b15-zero-cost: on a reliable link each state fingerprint travels once (false: the newest two in every
        /// packet, as before; a datagram link always repeats them, since a packet may be lost).</summary>
        public bool HashesOnce = true;
        /// <summary>b15-zero-cost: the keep-alive interval once this machine's match has ended (nothing is waited for then).</summary>
        public int EndedKeepAliveMs = 1000;
        /// <summary>
        /// b15-zero-cost: a rejoiner going live waits until the host's clock reaches the tick it starts at. The replay ends at
        /// the newest bundle it holds, and the host's feed runs to the newest tick the host has SEQUENCED, which is the
        /// host's input delay (3 ticks or more) ahead of the host's own stepping. Without the wait the rejoiner then played
        /// at the very edge of what was sequenced, so every bundle that was not sent the instant it was sequenced made it
        /// wait (with the host's bundles in pairs: every second tick; online-smoke 44247175418 measured 11.7 s of waits in
        /// 40 s). A normal joiner sits at the host's stepping, the input delay behind that edge. False = the b15-rejoin rule.
        /// </summary>
        public bool RejoinWaitsForTheHost = true;

        /// <summary>b15-zero-cost: the sending as it was before b15 (one packet per tick, fingerprints repeated), for
        /// before/after measurements and the cost guard's control arm.</summary>
        public static LockstepOptions PreB15() => new LockstepOptions { ClientTicksPerPacket = 1, HashesOnce = false, EndedKeepAliveMs = 250 };
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
        private const int MsgInputs = 1, MsgBundles = 2, MsgLog = 3;
        private const int HashKeep = 64;
        private const byte FlagInput = 1, FlagToBot = 2, FlagToHuman = 4;
        /// <summary>b15-rejoin: a rejoiner keeps at most this many ticks beyond what it holds (a hostile host cannot make it allocate more).</summary>
        private const int FlatAhead = 1 << 15;

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
        /// <summary>b15-zero-cost: packets this session handed to the transport (each one is one message the relay bills).</summary>
        public long PacketsSent { get; private set; }
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
        /// <summary>b15-rejoin: raised when a rejoined human takes their fighter back from the bot: (slot, actor, world tick).</summary>
        public Action<int, int, int> BotToHuman;

        // ---------------------------------------------------------------- b15-rejoin: state a screen or a log reads
        /// <summary>This session joined a match already running and is replaying the host's log (not playing yet).</summary>
        public bool IsRejoin => _flat;
        /// <summary>Rejoiner: still replaying the log; the fighter is the bot's until the hand-back.</summary>
        public bool CatchingUp => _catchingUp;
        /// <summary>Rejoiner: the newest host tick heard of (the catch-up target), -1 before any.</summary>
        public int CatchUpTarget => _hostSteppedSeen;
        /// <summary>Rejoiner: wall time the session started (ms) and the time it went live (ms, -1 while catching up).</summary>
        public long RejoinStartMs { get; private set; } = -1;
        public long RejoinLiveMs { get; private set; } = -1;
        /// <summary>b15-zero-cost: ticks the rejoiner held its clock at going live, to start at the host's stepping.</summary>
        public int RejoinHeldTicks { get; private set; }
        /// <summary>Rejoiner: the tick it went live at, and the wall milliseconds spent stepping the log.</summary>
        public int RejoinLiveTick { get; private set; } = -1;
        public double CatchUpStepMs { get; private set; }
        /// <summary>Rejoiner: log fingerprints from the host checked during the catch-up (each one a whole second of match).</summary>
        public int RejoinVerified { get; private set; }
        /// <summary>Rejoiner: received log bytes (compressed, on the wire) and the raw bundle bytes they held.</summary>
        public long LogBytes { get; private set; }
        public long LogRawBytes { get; private set; }
        /// <summary>Host: rejoins accepted, log packets and bytes fed.</summary>
        public int RejoinsAccepted { get; private set; }
        public int FeedPackets { get; private set; }
        public long FeedBytes { get; private set; }
        /// <summary>Test seam (host): may edit a log packet's raw bundles before they are encoded (proves a bad log is caught).</summary>
        public Action<int, byte[], int, int> TestFeedTamper;
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
        private byte[] _bundles;
        private int[] _bundleTick;
        /// <summary>b15-rejoin: a rejoiner keeps its bundles by tick for the whole match (it replays from tick 0), not in the ring.</summary>
        private readonly bool _flat;
        private bool _catchingUp;
        private int _hostSteppedSeen = -1;
        /// <summary>Rejoiner: the host's fingerprints for the log, by tick / HashEvery (1 = held in _logHashHave).</summary>
        private ulong[] _logHash = new ulong[0];
        private byte[] _logHashHave = new byte[0];
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
        // b15-rejoin, host: every produced bundle by tick (the log a rejoiner replays), every fingerprint by tick / HashEvery,
        // and the rejoin feed per slot.
        private byte[] _hist = new byte[0];
        private ulong[] _hashHist = new ulong[0];
        private int _hashHistThrough = -1;
        private bool[] _rejoining, _rejoinLive, _feeding;
        private int[] _feedSent, _feedAckSeen;
        private long[] _feedAckAt;
        private double _feedTokens;
        private long _feedLast = -1;
        private int _feedNext;
        private byte[] _feedRaw = new byte[0];
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
        /// <summary>b15-zero-cost: ticks of input in one joiner packet (1 = a packet per tick, the pre-b15 way).</summary>
        public int TicksPerPacket => _clientTicks;
        private readonly int _clientTicks;
        /// <summary>b15-zero-cost, host: per slot, how many ticks before the host needed it the first new input of that slot's
        /// last packet arrived (int.MinValue: none yet). With packed inputs this is the margin the bundles report.</summary>
        private int[] _arrSlack;
        private int _hashesSent;
        private long _unsentSince = -1;
        /// <summary>b15-zero-cost, host: when the oldest produced-but-unbroadcast tick was first seen (-1: none).</summary>
        private long _hostUnsentSince = -1;

        /// <summary>b15-zero-cost: the default inputs-per-packet for a room of this many humans: 2 (above 16 humans the pre-b15
        /// joiners already sent about every 2 ticks; 3 there measured +2 ticks of felt delay, over the one-tick limit).</summary>
        public static int TicksPerPacketFor(int humans) => 2;

        /// <summary>
        /// b15-rejoin: a session for a seat whose player dropped and came back (RoomClient after REJOINED). It builds the
        /// world from the setup like everyone did at the start, then replays the host's log of every input bundle from tick 0
        /// as fast as it arrives, checking the host's fingerprint every HashEvery ticks of it, with no input of its own; once
        /// it holds everything the host has stepped it starts sending inputs, and the host hands the fighter back from the bot
        /// at the next tick it sequences (the same tick on every machine, carried in the bundle).
        /// </summary>
        public static LockstepSession Rejoining(ITransport transport, MatchSetup setup, GameConfig cfg, LockstepOptions options = null) =>
            new LockstepSession(transport, setup, cfg, options, true);

        /// <summary>Ticks of log a session keeps room for at the start: the match plus a quarter (more only for a long overtime).</summary>
        private static int LogTicks(GameConfig cfg) => Math.Max(Window, (int)(cfg.MatchSeconds * cfg.SimHz * 1.25) + Window);

        public LockstepSession(ITransport transport, MatchSetup setup, GameConfig cfg, LockstepOptions options = null) : this(transport, setup, cfg, options, false) { }

        private LockstepSession(ITransport transport, MatchSetup setup, GameConfig cfg, LockstepOptions options, bool rejoin)
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
            // A big room's host sends half as often (two ticks per packet): the relay's room object sees one message per human
            // per send, and 32 humans x 33 a second would sit at its 1,000 a second soft limit.
            _sendEveryMs = setup.Humans.Length > BigRoomHumans ? Math.Max(_o.SendEveryMs, 2 * _o.SendEveryMs) : _o.SendEveryMs;
            // b15-zero-cost: joiners pack 2 ticks a packet: the relay bills each message it receives, and a joiner's inputs
            // are nearly all of them. The host's bundles keep their interval (and go out in the same pairs).
            _clientTicks = _o.ClientTicksPerPacket > 0 ? _o.ClientTicksPerPacket : TicksPerPacketFor(setup.Humans.Length);
            _h = setup.Humans.Length;
            _bundleSize = _h * (1 + InputCodec.Size);
            _flat = rejoin && !IsHost;
            _catchingUp = _flat;
            int ring = _flat ? LogTicks(cfg) : Window;
            _bundles = new byte[ring * _bundleSize];
            _bundleTick = new int[ring];
            for (int i = 0; i < ring; i++) _bundleTick[i] = -1;
            for (int i = 0; i < Window; i++) _mineTick[i] = -1;
            if (_flat) { _logHash = new ulong[ring / Math.Max(1, setup.HashEvery) + 1]; _logHashHave = new byte[_logHash.Length]; }
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
                _hist = new byte[LogTicks(cfg) * _bundleSize];
                _hashHist = new ulong[LogTicks(cfg) / Math.Max(1, setup.HashEvery) + 1];
                _rejoining = new bool[_h];
                _rejoinLive = new bool[_h];
                _arrSlack = new int[_h];
                for (int k = 0; k < _h; k++) _arrSlack[k] = int.MinValue;
                _feeding = new bool[_h];
                _feedSent = new int[_h];
                _feedAckSeen = new int[_h];
                _feedAckAt = new long[_h];
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
            if (_lastNow < 0) { _lastNow = nowMs; _startMs = nowMs; _lastFromHost = nowMs; if (IsHost) for (int k = 0; k < _h; k++) _lastHeard[k] = nowMs; if (_flat) RejoinStartMs = nowMs; }
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
                    if (Stepped == 0 && _mineThrough < 0 && !_catchingUp) GenerateLocal(Lead - 1, true);
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
                    if (IsHost)
                    {
                        int k = Setup.SlotOfPeer(ev.From);
                        if (k > 0 && !_gone[k]) _dropPending[k] = true;
                        if (k > 0 && _rejoining[k]) { _rejoining[k] = false; _rejoinLive[k] = false; _feeding[k] = false; }   // b15-rejoin: gave up
                    }
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
            else if (!IsHost && type == MsgLog && ev.From == Peers.Host && _flat) ReadLog(ev.Data, ev.Length, nowMs);
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
                    if (_gone[k] && _rejoining[k] && _rejoinLive[k])
                    {
                        // b15-rejoin: the rejoined player is live (its inputs arrive): it takes the fighter back at this tick, on
                        // every machine. Its input for the tick if it is here, else its newest (one-shots cleared), as for a
                        // lagging human; the lead then adapts as usual.
                        _gone[k] = false;
                        _rejoining[k] = false;
                        _rejoinLive[k] = false;
                        _bundles[o] = FlagToHuman | FlagInput;
                        if (_inTick[k][idx] == s) Buffer.BlockCopy(_in[k], idx * InputCodec.Size, _lastInput[k], 0, InputCodec.Size);
                        else { ClearOneShots(_lastInput[k], 0); _lagging[k] = true; _lagUntil[k] = now + _o.LagHoldMs; LateInputs++; }
                        Buffer.BlockCopy(_lastInput[k], 0, _bundles, o + 1, InputCodec.Size);
                        _heardInput[k] = true;
                        _lastHeard[k] = now;
                        continue;
                    }
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
                // b15-rejoin: the log a rejoiner replays (allocated for the match up front; grows only in a long overtime).
                if ((s + 1) * _bundleSize > _hist.Length) Array.Resize(ref _hist, Math.Max((s + 1) * _bundleSize, _hist.Length * 2));
                Buffer.BlockCopy(_bundles, off, _hist, s * _bundleSize, _bundleSize);
            }
        }

        private void ReadInputs(int from, byte[] d, int len, long now)
        {
            int k = Setup.SlotOfPeer(from);
            if (k <= 0 || (_gone[k] && !_rejoining[k])) return;
            bool rejoining = _gone[k] && _rejoining[k];
            var r = new WireReader(d, 1, len);
            int theirDesync = r.I32();
            int ack = r.I32();
            if (!ReadHashes(ref r, k)) return;
            // b15-rejoin: a rejoiner's fingerprints are of ticks long gone here and its desync report is its own matter (it
            // stops itself); neither may stop the match for everyone before it has its fighter back.
            if (rejoining) { _theirTick[k * 2] = -1; _theirTick[k * 2 + 1] = -1; theirDesync = -1; }
            int first = r.I32();
            int count = r.U8();
            if (r.Bad || count > 64 || r.Left < count * InputCodec.Size) return;
            _lastHeard[k] = now;
            if (ack > _ackedBundle[k] && ack <= _producedThrough) _ackedBundle[k] = ack;
            if (rejoining && count > 0)
            {
                // Live: it holds everything we stepped and plays now. Its newest input is what the hand-back starts from.
                _rejoinLive[k] = true;
                Buffer.BlockCopy(d, r.Pos + (count - 1) * InputCodec.Size, _lastInput[k], 0, InputCodec.Size);
            }
            int lastNew = -1;
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
                lastNew = t;
            }
            // b15-zero-cost: with packed inputs the margin a bundle reports is measured when the inputs ARRIVE: how many ticks
            // before the host needs the first tick of the newest group (newest - (ticks a packet - 1)), not when the bundle
            // is sent: bundles now go out right after a group completes, when every slot's last group looks "just in time",
            // so a send-time margin would grow every lead. (The newest tick, not the first new one: the opening packet
            // carries ticks 0 to lead - 1 and its tick 0 is always needed at once.)
            if (lastNew >= 0) _arrSlack[k] = lastNew - (_clientTicks - 1) - Stepped;
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
            for (int i = 0; i < na; i++) { int md = (sbyte)r.U8(); if (i == LocalSlot && md != -128) myMargin = md; }   // -128: none of ours in yet
            if (!ReadHashes(ref r, 0)) return;
            int first = r.I32();
            int count = r.U8();
            int ended = r.U8();
            if (r.Bad || r.Left < count * _bundleSize) return;
            _lastFromHost = now;
            if (ack > _hostAckedInput && ack <= _mineThrough) _hostAckedInput = ack;
            if (hostStepped > _hostSteppedSeen) _hostSteppedSeen = hostStepped;
            for (int j = 0; j < count; j++) PutBundle(first + j, d, r.Pos + j * _bundleSize);
            AdvanceHeld();
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

        // ---------------------------------------------------------------- b15-rejoin: the bundle store

        /// <summary>Where bundle t lives: by tick for a rejoiner (the whole match), in the ring otherwise.</summary>
        private int BIdx(int t) => _flat ? t : t & (Window - 1);

        private bool HaveBundle(int t) { int i = BIdx(t); return t >= 0 && i < _bundleTick.Length && _bundleTick[i] == t; }

        private void AdvanceHeld() { while (HaveBundle(_bundlesThrough + 1)) _bundlesThrough++; }

        /// <summary>Stores bundle t (copied from src at off) if it is new and fits the window (a rejoiner's: anything ahead).</summary>
        private void PutBundle(int t, byte[] src, int off)
        {
            if (t < Stepped || t <= _bundlesThrough) return;
            if (_flat)
            {
                if (t > _bundlesThrough + FlatAhead) return;
                if (t >= _bundleTick.Length)
                {
                    int n = Math.Max(t + 1, _bundleTick.Length * 2), was = _bundleTick.Length;
                    Array.Resize(ref _bundleTick, n);
                    Array.Resize(ref _bundles, n * _bundleSize);
                    for (int i = was; i < n; i++) _bundleTick[i] = -1;
                }
            }
            else if (t >= Stepped + Window) return;
            int idx = BIdx(t);
            if (_bundleTick[idx] == t) return;
            Buffer.BlockCopy(src, off, _bundles, idx * _bundleSize, _bundleSize);
            _bundleTick[idx] = t;
        }

        /// <summary>
        /// Rejoiner: a packet of the host's log (MsgLog): the host's fingerprints for those ticks, then `count` bundles from
        /// `first`, XOR-delta and zero-run encoded (LogCodec). Stored like bundles; the fingerprints are checked as it steps.
        /// </summary>
        private void ReadLog(byte[] d, int len, long now)
        {
            var r = new WireReader(d, 1, len);
            int hostStepped = r.I32();
            int nh = r.U8();
            if (r.Bad || nh > 8) return;
            for (int j = 0; j < nh; j++)
            {
                int tick = r.I32();
                ulong h = r.U64();
                int he = Math.Max(1, Setup.HashEvery);
                if (r.Bad || tick <= 0 || tick % he != 0) continue;
                int i = tick / he;
                if (i >= _logHash.Length) { Array.Resize(ref _logHash, i * 2); Array.Resize(ref _logHashHave, i * 2); }
                if (i < _logHash.Length && Stepped < tick) { _logHash[i] = h; _logHashHave[i] = 1; }
            }
            int first = r.I32();
            int count = r.I16();
            int enc = r.I16();
            if (r.Bad || first < 0 || count <= 0 || count > 4096 || enc < 0 || r.Left < enc) return;
            _lastFromHost = now;
            if (hostStepped > _hostSteppedSeen) _hostSteppedSeen = hostStepped;
            int raw = count * _bundleSize;
            if (_feedRaw.Length < raw) _feedRaw = new byte[raw];
            if (!LogCodec.Decode(d, r.Pos, enc, _feedRaw, raw, _bundleSize)) return;
            LogBytes += len;
            LogRawBytes += raw;
            for (int j = 0; j < count; j++) PutBundle(first + j, _feedRaw, j * _bundleSize);
            AdvanceHeld();
        }

        /// <summary>
        /// Host: takes `slot` back for a player who dropped and reconnected as `peer`. The seat goes to the bot now if it has
        /// not yet; from here the host feeds the new peer the log of every bundle from tick 0 (MsgLog, at most
        /// RejoinFeedPerSec packets a second for all rejoiners together, so the relay's limit and the match are never at
        /// risk), and the first input it then sends hands the fighter back at the next tick sequenced. Nobody waits for it.
        /// Null on success, else why not (plain words).
        /// </summary>
        public string AcceptRejoin(int slot, int peer, long now)
        {
            if (!IsHost) return "Only the host can take a player back.";
            if (Status != LockstepStatus.Running) return "The match is over.";
            if (slot <= 0 || slot >= _h) return "That seat is not in this match.";
            if (peer == Peers.Host || peer == Peers.Broadcast) return "That seat is not in this match.";
            for (int k = 0; k < _h; k++) if (k != slot && Setup.Humans[k].Peer == peer) return "That player is already in the match.";
            if (!_gone[slot]) _dropPending[slot] = true;   // still seated (its old link has not timed out): to the bot first
            Setup.Humans[slot].Peer = peer;
            _rejoining[slot] = true;
            _rejoinLive[slot] = false;
            _feeding[slot] = true;
            _ackedBundle[slot] = -1;
            _feedSent[slot] = -1;
            _feedAckSeen[slot] = -1;
            _feedAckAt[slot] = now;
            _inThrough[slot] = _producedThrough;
            _heardInput[slot] = false;
            _lagging[slot] = false;
            _lastHeard[slot] = now;
            _lastStartSend[slot] = long.MaxValue / 2;   // it has its setup (REJOINED); never resend START to it
            _theirTick[slot * 2] = -1;
            _theirTick[slot * 2 + 1] = -1;
            _arrSlack[slot] = int.MinValue;
            RejoinsAccepted++;
            return null;
        }

        /// <summary>Host: feeds the log to rejoining players (round robin), token-bucketed at RejoinFeedPerSec.</summary>
        private void FeedRejoiners(long now)
        {
            if (_feedLast < 0) _feedLast = now;
            _feedTokens = Math.Min(4, _feedTokens + (now - _feedLast) * _o.RejoinFeedPerSec / 1000.0);
            _feedLast = now;
            for (int n = 0; n < _h && _feedTokens >= 1; n++)
            {
                int k = (_feedNext + n) % _h;
                if (k == 0 || !_feeding[k]) continue;
                if (!_gone[k] && !_rejoining[k]) { _feeding[k] = false; continue; }   // handed back: the normal stream has it
                if (_ackedBundle[k] > _feedAckSeen[k]) { _feedAckSeen[k] = _ackedBundle[k]; _feedAckAt[k] = now; }
                if (!_t.Reliable && now - _feedAckAt[k] > _o.RejoinFeedRetryMs) { _feedSent[k] = _ackedBundle[k]; _feedAckAt[k] = now; }
                int from = Math.Max(_feedSent[k], _ackedBundle[k]) + 1;
                if (_t.Reliable && from > _sentBundle[0])
                {
                    // Everything broadcast so far is fed; every later bundle reaches it by the broadcast.
                    _feeding[k] = false;
                    continue;
                }
                if (from > _producedThrough) continue;
                int len = BuildLog(from, _producedThrough, out int count);
                if (len <= 0) continue;
                _t.Send(Setup.Humans[k].Peer, _pkt, len);
                BytesSent += len;
                FeedBytes += len;
                FeedPackets++;
                PacketsSent++;
                _feedSent[k] = from + count - 1;
                _feedTokens -= 1;
                _feedNext = k + 1;
            }
        }

        /// <summary>One MsgLog packet of bundles from..at most upTo from the history, as many as fit; count = how many.</summary>
        private int BuildLog(int from, int upTo, out int count)
        {
            var w = new WireWriter(_pkt);
            w.U8(MsgLog);
            w.I32(Stepped);
            int he = Math.Max(1, Setup.HashEvery);
            // Up to 8 of the host's fingerprints at ticks this packet will reach (every HashEvery-th); the first tick after
            // bundle t is t + 1.
            int hashes = 0;
            int at = w.Pos;
            w.U8(0);
            int maxTicks = Math.Min(upTo - from + 1, 600);   // a tick encodes to 2 bytes at the very least, so 600 never all fit
            for (int t = ((from + 1 + he - 1) / he) * he; t <= from + maxTicks && t <= _hashHistThrough && hashes < 8; t += he)
            {
                w.I32(t);
                w.U64(_hashHist[t / he]);
                hashes++;
            }
            _pkt[at] = (byte)hashes;
            w.I32(from);
            int countAt = w.Pos;
            w.I16(0);
            w.I16(0);
            if (w.Overflow) { count = 0; return -1; }
            int room = Peers.MaxPacket - w.Pos;
            if (_feedRaw.Length < maxTicks * _bundleSize) _feedRaw = new byte[maxTicks * _bundleSize];
            Buffer.BlockCopy(_hist, from * _bundleSize, _feedRaw, 0, maxTicks * _bundleSize);
            TestFeedTamper?.Invoke(from, _feedRaw, maxTicks, _bundleSize);
            int enc = LogCodec.Encode(_feedRaw, maxTicks, _bundleSize, _pkt, w.Pos, room, out count);
            if (count <= 0) return -1;
            // The hashes written may reach past the ticks that fit; the rejoiner only checks ticks it steps to, so that is fine.
            _pkt[countAt] = (byte)count; _pkt[countAt + 1] = (byte)(count >> 8);
            _pkt[countAt + 2] = (byte)enc; _pkt[countAt + 3] = (byte)(enc >> 8);
            return w.Pos + enc;
        }

        /// <summary>Rejoiner: everything the host has stepped is held and stepped: start playing (inputs from here on).</summary>
        private void GoLive(long now)
        {
            _catchingUp = false;
            _acc = 0;
            // b15-zero-cost: start where a normal joiner is (at the host's stepping, as last reported), not at the edge of
            // what the host has sequenced: hold the clock for the difference (at most a second).
            int ahead = Stepped - _hostSteppedSeen;
            RejoinHeldTicks = _o.RejoinWaitsForTheHost && ahead > 0 ? Math.Min(ahead, 30) : 0;
            _acc = -RejoinHeldTicks * _tickMs;
            _mineThrough = Stepped - 1;
            _sentInput = Stepped - 1;
            _hostAckedInput = Stepped - 1;
            RejoinLiveMs = now;
            RejoinLiveTick = Stepped;
            GenerateLocal(Stepped + Lead - 1, true);
        }

        // ---------------------------------------------------------------- stepping

        private void StepDue(long now)
        {
            if (_catchingUp)
            {
                // b15-rejoin: replay the log as fast as it is held (a frame's budget per Update), with no input of our own;
                // live once every bundle the host has stepped is here and stepped.
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                int n = 0;
                while (Status == LockstepStatus.Running && Stepped <= _bundlesThrough && n < _o.CatchUpTicksPerUpdate) { StepOne(); n++; }
                if (n > 0) CatchUpStepMs += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (Status == LockstepStatus.Running && _hostSteppedSeen > 0 && Stepped > _bundlesThrough && _bundlesThrough >= _hostSteppedSeen - 1) GoLive(now);
                return;
            }
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
            int off = BIdx(s) * _bundleSize;
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
                if ((flags & FlagToHuman) != 0 && _slotIsBot[k])
                {
                    // b15-rejoin: the player who dropped is back: the fighter is theirs again from this tick, on every machine.
                    _slotIsBot[k] = false;
                    MatchTick.BotToHuman(World, actor);
                    BotToHuman?.Invoke(k, actor, World.Tick);
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
            // This machine's next input goes Lead ticks ahead of the tick being stepped (a rejoiner replaying the log has none).
            if (!_catchingUp) GenerateLocal(s + Lead, false);
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
            int he = Math.Max(1, Setup.HashEvery);
            if (IsHost && tick % he == 0)
            {
                // b15-rejoin: every fingerprint of the match, for the rejoin log.
                int hi = tick / he;
                if (hi >= _hashHist.Length) Array.Resize(ref _hashHist, Math.Max(hi + 1, _hashHist.Length * 2));
                _hashHist[hi] = h;
                if (tick > _hashHistThrough) _hashHistThrough = tick;
            }
            if (_flat && tick % he == 0 && tick / he < _logHashHave.Length && _logHashHave[tick / he] != 0)
            {
                // b15-rejoin: the host's fingerprint for this second of the log: a wrong or garbage log stops here.
                _logHashHave[tick / he] = 0;
                if (_logHash[tick / he] != h)
                {
                    SetupMismatch = "Could not rejoin: the match the host sent did not check out. Try again.";
                    SetupMismatchDetail = $"rejoin log fingerprint at tick {tick}: mine 0x{h:X16}, host 0x{_logHash[tick / he]:X16}";
                    Desync(tick);
                }
                else RejoinVerified++;
            }
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
                if (r.Bad || tick <= 0) continue;
                // b15-zero-cost: a fingerprint may now travel once, so it takes the slot's entry for the same tick, else a
                // free one, else the older one (a pending check is never pushed out by a newer packet's position).
                int a = slot * 2, b = a + 1, at;
                if (_theirTick[a] == tick) at = a;
                else if (_theirTick[b] == tick) at = b;
                else if (_theirTick[a] <= 0) at = a;
                else if (_theirTick[b] <= 0) at = b;
                else at = _theirTick[a] <= _theirTick[b] ? a : b;
                _theirTick[at] = tick;
                _theirHash[at] = h;
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
            // b15-zero-cost: on a reliable link only the fingerprints not sent yet (at most two); on a datagram link the
            // newest two in every packet, as before (any packet may be lost).
            bool once = _o.HashesOnce && _t.Reliable;
            int n = once ? Math.Max(0, Math.Min(_hashCount - _hashesSent, 2)) : Math.Min(_hashCount, 2);
            if (once) _hashesSent = _hashCount;
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

        private int KeepAliveNow => Status == LockstepStatus.Ended ? Math.Max(_o.KeepAliveMs, _o.EndedKeepAliveMs) : _o.KeepAliveMs;

        private void SendClient(long now)
        {
            int from = _t.Reliable ? _sentInput + 1 : _hostAckedInput + 1;
            bool news = _mineThrough >= from || Status == LockstepStatus.Desynced || _catchingUp;   // a rejoiner acks the log at the send rate
            int fresh = _mineThrough - _sentInput;   // ticks generated since the last packet
            if (_clientTicks > 1 && fresh > 0 && !_catchingUp && Status != LockstepStatus.Desynced)
            {
                // b15-zero-cost: one packet per _clientTicks ticks, when its last tick is a multiple of _clientTicks (every
                // joiner the same, so the host completes and sends its bundles in the same groups), or when the oldest unsent
                // input has waited (_clientTicks - 1) ticks (a stall: never hold an input longer than that).
                if (_unsentSince < 0) _unsentSince = now;
                bool aligned = (_mineThrough + 1) % _clientTicks == 0;
                bool full = fresh >= _clientTicks;
                bool waited = now - _unsentSince >= (_clientTicks - 1) * _tickMs - 0.5;
                if (!aligned && !full && !waited) return;
            }
            else if (now - _lastSendHost < (news ? _sendEveryMs : KeepAliveNow)) return;
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
            PacketsSent++;
            _lastSendHost = now;
            _unsentSince = -1;
            if (count > 0) _sentInput = from + count - 1;
        }

        private void SendHost(long now)
        {
            FeedRejoiners(now);
            if (StartPacket != null)
                for (int k = 1; k < _h; k++)
                    if (!_heardInput[k] && !_gone[k] && now - _lastStartSend[k] >= 250)
                    {
                        _t.Send(Setup.Humans[k].Peer, StartPacket, StartPacketLength);
                        PacketsSent++;
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
                    if (pkt == 0)
                    {
                        // b15-zero-cost: on a real clock the host produces one tick at a time (its own input arrives each
                        // tick, the joiners' are already there), so a 30 ms gate broadcast every tick: the online-smoke run
                        // of 2026-11-18 measured 29 bundles a second against 15 inputs a second from each joiner. Broadcast
                        // the same pairs the joiners send (when the pair's last tick is produced, or after one tick of
                        // waiting at most), so the relay receives half the host's messages too.
                        if (news && _clientTicks > 1 && Status != LockstepStatus.Desynced)
                        {
                            if (_hostUnsentSince < 0) _hostUnsentSince = now;
                            bool aligned = (_producedThrough + 1) % _clientTicks == 0;
                            bool full = _producedThrough - from + 1 >= _clientTicks;
                            bool waited = now - _hostUnsentSince >= (_clientTicks - 1) * _tickMs - 0.5;
                            if (!aligned && !full && !waited) return;
                        }
                        else if (now - _lastSendTo[0] < (news ? _sendEveryMs : KeepAliveNow)) return;
                    }
                    if (pkt > 0 && _producedThrough < from) return;
                    int count = Math.Max(0, Math.Min(_producedThrough - from + 1, maxPer));
                    int len = BuildBundles(from, count, -1);
                    if (len <= 0) return;
                    _t.Send(Peers.Broadcast, _pkt, len);
                    BytesSent += len;
                    PacketsSent++;
                    _lastSendTo[0] = now;
                    if (count > 0) { _sentBundle[0] = from + count - 1; _hostUnsentSince = -1; }
                }
                return;
            }
            for (int k = 1; k < _h; k++)
            {
                if (_gone[k]) continue;
                int first = _ackedBundle[k] + 1;
                bool news = _producedThrough >= first || Status == LockstepStatus.Desynced;
                if (now - _lastSendTo[k] < (news ? _sendEveryMs : KeepAliveNow)) continue;
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
                    PacketsSent++;
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
                int d = (_gone[k] && !_rejoining[k]) || _inThrough[k] < 0 ? -128
                    : _clientTicks > 1 && _arrSlack[k] != int.MinValue ? _arrSlack[k] : _inThrough[k] - Stepped;
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
