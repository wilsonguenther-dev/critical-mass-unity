using System;
using System.Collections.Generic;

namespace CriticalMass.Net
{
    /// <summary>
    /// b17-chat voice: IMA ADPCM, 4 bits a sample (no library, no licence, a few dozen lines). The relay bills messages,
    /// not bytes, so a plain codec at 64 kbit/s costs the same as Opus at 12: what matters is frames a second. Each frame
    /// carries its own starting state, so a frame decodes on its own (a lost frame on a datagram link costs only itself).
    /// </summary>
    public static class ImaAdpcm
    {
        private static readonly int[] IndexTable = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };
        private static readonly int[] StepTable =
        {
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118,
            130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963, 1060,
            1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484,
            7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767,
        };

        public struct State { public int Predictor; public int Index; }

        /// <summary>Bytes for n samples.</summary>
        public static int Bytes(int n) => (n + 1) / 2;

        /// <summary>Encodes n samples of pcm from off into dst at dstOff (two samples a byte, low nibble first); updates s.</summary>
        public static void Encode(short[] pcm, int off, int n, ref State s, byte[] dst, int dstOff)
        {
            int pred = s.Predictor, index = s.Index;
            for (int i = 0; i < n; i++)
            {
                int step = StepTable[index];
                int diff = pcm[off + i] - pred;
                int code = 0;
                if (diff < 0) { code = 8; diff = -diff; }
                int delta = step >> 3;
                if (diff >= step) { code |= 4; diff -= step; delta += step; }
                step >>= 1;
                if (diff >= step) { code |= 2; diff -= step; delta += step; }
                step >>= 1;
                if (diff >= step) { code |= 1; delta += step; }
                pred += (code & 8) != 0 ? -delta : delta;
                pred = pred > 32767 ? 32767 : pred < -32768 ? -32768 : pred;
                index += IndexTable[code];
                index = index < 0 ? 0 : index > 88 ? 88 : index;
                int b = dstOff + (i >> 1);
                if ((i & 1) == 0) dst[b] = (byte)code; else dst[b] |= (byte)(code << 4);
            }
            s.Predictor = pred;
            s.Index = index;
        }

        /// <summary>Decodes n samples from src at srcOff into pcm at off, starting from s (updated).</summary>
        public static void Decode(byte[] src, int srcOff, int n, ref State s, short[] pcm, int off)
        {
            int pred = s.Predictor, index = Math.Max(0, Math.Min(88, s.Index));
            for (int i = 0; i < n; i++)
            {
                int code = (i & 1) == 0 ? src[srcOff + (i >> 1)] & 15 : src[srcOff + (i >> 1)] >> 4;
                int step = StepTable[index];
                int delta = step >> 3;
                if ((code & 4) != 0) delta += step;
                if ((code & 2) != 0) delta += step >> 1;
                if ((code & 1) != 0) delta += step >> 2;
                pred += (code & 8) != 0 ? -delta : delta;
                pred = pred > 32767 ? 32767 : pred < -32768 ? -32768 : pred;
                index += IndexTable[code];
                index = index < 0 ? 0 : index > 88 ? 88 : index;
                pcm[off + i] = (short)pred;
            }
            s.Predictor = pred;
            s.Index = index;
        }
    }

    /// <summary>
    /// b17-chat: push-to-talk voice in the ROOM (lobby) only, to your team only, OFF by default (each player's switch).
    /// Frames of FrameMs at 16 kHz in IMA ADPCM ride the room connection as chat-block type 34 (ChatWire.Voice): never the
    /// lockstep, never the match. A talker's frame goes to the host, which checks the talker (a confirmed member, the room
    /// still in the lobby, its rate, at most MaxTalkersPerTeam talking on its team at once) and forwards it in ONE
    /// broadcast stamped with the talker's peer; each game plays it only when the talker is on its team, is not muted and
    /// its own voice switch is on. Cost (VoiceCostTests, docs/online/CHAT-AND-VOICE.md): two relay messages a frame,
    /// 1,000 a talker-minute, 50 billed requests; the host's floor caps a room at four talkers (two a team).
    /// <para>Layout: [34][ver u8 = 1][from u8][seq u16][samples u16][predictor i16][index u8][ADPCM bytes].</para>
    /// <para>Privacy note: the broadcast reaches the other team's games too (they drop it unheard); a modified game could
    /// listen in. The relay's team multicast (server/relay, not deployed) removes that and stays one message.</para>
    /// </summary>
    public sealed class VoiceChannel
    {
        public const int SampleRate = 16000, FrameMs = 120, FrameSamples = SampleRate * FrameMs / 1000;
        public const int HeaderBytes = 10;
        public const int MaxTalkersPerTeam = 2;
        /// <summary>A talker counts as talking this long after its last frame.</summary>
        public const long TalkHoldMs = 400;
        /// <summary>One press talks at most this long (the key must be let go and pressed again).</summary>
        public const long MaxPressMs = 15000;
        public const double FramesPerSecond = 1000.0 / FrameMs;
        /// <summary>The host's per-talker bucket (a little over the frame rate).</summary>
        public const double HostRate = 10, HostBurst = 20;

        private readonly ITransport _t;
        private readonly byte[] _pkt = new byte[Peers.MaxPacket];
        private readonly short[] _pcm = new short[FrameSamples];
        private readonly Dictionary<int, long> _heardAt = new Dictionary<int, long>();
        private readonly Dictionary<int, double> _tok = new Dictionary<int, double>();
        private readonly Dictionary<int, long> _tokAt = new Dictionary<int, long>();
        private readonly List<int> _talking = new List<int>(8);
        private ImaAdpcm.State _enc;
        private int _seq;

        /// <summary>Team of a peer (host: its member list; client: the roster), -1 unknown.</summary>
        public Func<int, int> TeamOf = p => -1;
        /// <summary>Host: a confirmed member.</summary>
        public Func<int, bool> Knows = p => false;
        /// <summary>The room is in its lobby (voice is off in the match on every machine).</summary>
        public Func<bool> InLobby = () => false;
        /// <summary>Not muted (ChatChannel.Hears).</summary>
        public Func<int, bool> Hears = p => true;
        /// <summary>Host: one broadcast reaches exactly the members (ChatChannel.CanBroadcast).</summary>
        public Func<bool> CanBroadcast = () => false;
        /// <summary>Host: every confirmed member (unicast fallback).</summary>
        public Func<IEnumerable<int>> PeersInRoom = () => Array.Empty<int>();
        /// <summary>This player's voice switch (default OFF): off = nothing sent, nothing played.</summary>
        public bool Enabled;
        /// <summary>A decoded frame to play: (talker peer, samples, count). The Runtime's voice player fills it.</summary>
        public Action<int, short[], int> Play;
        public long FramesSent { get; private set; }
        public long FramesPlayed { get; private set; }
        public long FramesDropped { get; private set; }

        public VoiceChannel(ITransport t) { _t = t; }

        private bool Host => _t.LocalPeer == Peers.Host;
        private int MyTeam => TeamOf(_t.LocalPeer);

        /// <summary>Peers heard talking in the last TalkHoldMs (any team this machine hears), for the indicator.</summary>
        public List<int> Talking(long now)
        {
            _talking.Clear();
            int mine = MyTeam;
            foreach (KeyValuePair<int, long> kv in _heardAt)
                if (now - kv.Value <= TalkHoldMs && mine >= 0 && TeamOf(kv.Key) == mine && Hears(kv.Key)) _talking.Add(kv.Key);
            _talking.Sort();
            return _talking;
        }

        /// <summary>True when a teammate slot is free for self to talk (fewer than MaxTalkersPerTeam others talking).</summary>
        public bool FloorFree(int self, long now)
        {
            int team = TeamOf(self), n = 0;
            if (team < 0) return false;
            foreach (KeyValuePair<int, long> kv in _heardAt)
                if (kv.Key != self && now - kv.Value <= TalkHoldMs && TeamOf(kv.Key) == team) n++;
            return n < MaxTalkersPerTeam;
        }

        /// <summary>
        /// Sends one frame of exactly FrameSamples samples. Returns null when sent, else why not (plain words, for the
        /// indicator). The caller (push-to-talk) calls it once per FrameMs while the key is held.
        /// </summary>
        public string SendFrame(short[] pcm, long now)
        {
            if (!Enabled) return "Voice is off (chat settings).";
            if (!InLobby()) return "Voice works in the room, before the match.";
            if (_t.LocalPeer < 0 || MyTeam < 0) return "Not in a room yet.";
            if (pcm == null || pcm.Length < FrameSamples) return "No microphone sound.";
            if (!FloorFree(_t.LocalPeer, now)) return "Two teammates are talking; wait a moment.";
            int n = Build(Host ? Peers.Host : 0, pcm, 0);
            if (Host)
            {
                _heardAt[Peers.Host] = now;
                Forward(n);
            }
            else _t.Send(Peers.Host, _pkt, n);
            FramesSent++;
            return null;
        }

        /// <summary>Test seam: one frame of pcm encoded into dst (as SendFrame would send it); returns its length.</summary>
        public int WriteFrame(short[] pcm, byte[] dst)
        {
            int n = Build(0, pcm, 0);
            Buffer.BlockCopy(_pkt, 0, dst, 0, n);
            return n;
        }

        private int Build(int from, short[] pcm, int off)
        {
            var w = new WireWriter(_pkt);
            w.U8(ChatWire.Voice);
            w.U8(1);
            w.U8(from);
            w.I16(unchecked((short)(_seq++ & 0xFFFF)));
            w.I16(FrameSamples);
            w.I16(_enc.Predictor);
            w.U8(_enc.Index);
            int at = w.Pos;
            ImaAdpcm.Encode(pcm, off, FrameSamples, ref _enc, _pkt, at);
            return at + ImaAdpcm.Bytes(FrameSamples);
        }

        private void Forward(int n)
        {
            if (CanBroadcast()) { _t.Send(Peers.Broadcast, _pkt, n); return; }
            int team = TeamOf(_pkt[2]);
            foreach (int p in PeersInRoom())
                if (p != Peers.Host && p != _pkt[2] && TeamOf(p) == team) _t.Send(p, _pkt, n);
        }

        /// <summary>Every type-34 packet (ChatChannel.Handle passes them on).</summary>
        public void Handle(ref TransportEvent ev, long now)
        {
            if (ev.Kind != TransportEventKind.Data || ev.Data == null || ev.Length < HeaderBytes || ev.Data[0] != ChatWire.Voice) return;
            var r = new WireReader(ev.Data, 1, ev.Length);
            int ver = r.U8();
            r.U8();   // from (a client's own claim is never trusted)
            r.I16();  // seq
            int samples = r.I16();
            var st = new ImaAdpcm.State { Predictor = r.I16(), Index = r.U8() };
            if (r.Bad || ver < 1 || samples <= 0 || samples > FrameSamples || r.Left < ImaAdpcm.Bytes(samples)) { FramesDropped++; return; }
            int talker;
            if (Host)
            {
                talker = ev.From;
                if (!Knows(talker) || !InLobby() || !Take(talker, now)) { FramesDropped++; return; }
                if (!FloorFreeOrTalking(talker, now)) { FramesDropped++; return; }
                _heardAt[talker] = now;
                // Re-stamp the sender and forward the same bytes (one broadcast).
                Buffer.BlockCopy(ev.Data, 0, _pkt, 0, ev.Length);
                _pkt[2] = (byte)talker;
                Forward(ev.Length);
            }
            else
            {
                if (ev.From != Peers.Host) return;
                talker = ev.Data[2];
                if (!InLobby()) { FramesDropped++; return; }
                if (MyTeam >= 0 && TeamOf(talker) == MyTeam) _heardAt[talker] = now;   // teammates only (the floor and the indicator)
            }
            if (!Enabled || talker == _t.LocalPeer || TeamOf(talker) != MyTeam || MyTeam < 0 || !Hears(talker)) return;
            ImaAdpcm.Decode(ev.Data, r.Pos, samples, ref st, _pcm, 0);
            FramesPlayed++;
            Play?.Invoke(talker, _pcm, samples);
        }

        /// <summary>Host: a talker already talking keeps its slot; a new one needs a free slot on its team.</summary>
        private bool FloorFreeOrTalking(int talker, long now)
        {
            if (_heardAt.TryGetValue(talker, out long at) && now - at <= TalkHoldMs) return true;
            return FloorFree(talker, now);
        }

        private bool Take(int peer, long now)
        {
            if (!_tok.TryGetValue(peer, out double t)) { t = HostBurst; _tokAt[peer] = now; }
            t = Math.Min(HostBurst, t + Math.Max(0, now - _tokAt[peer]) * HostRate / 1000.0);
            _tokAt[peer] = now;
            if (t < 1) { _tok[peer] = t; return false; }
            _tok[peer] = t - 1;
            return true;
        }
    }
}
