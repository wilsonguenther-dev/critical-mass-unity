using System;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Core;

namespace CriticalMass.Net.Replay
{
    /// <summary>
    /// b13-replay: records ONE world's inputs exactly as the sim consumes them. Fed through MatchTick.Tap (the single point
    /// where every input enters the sim: the single-player driver's Advance, the online lockstep's AdvanceHumans and
    /// HandToBot, the hidden online peer's too), or called directly by tests. Every call for another world is ignored.
    /// <para>Per tick it compares each present actor's frame with the last one, writes only what changed (or bumps a
    /// repeat counter when nothing did) into a preallocated buffer, and every HashEvery ticks stores StateHash.Of(world)
    /// (the online fingerprint) before the tick's inputs apply. No allocation per tick: the buffer, the fingerprint arrays
    /// and the lanes exist from Begin (they grow by doubling only past their preallocated size).</para>
    /// Read-only on the world and on the frames.
    /// </summary>
    public sealed class ReplayRecorder : IMatchInputTap
    {
        /// <summary>Preallocated input bytes: a long single-player match fits many times over; a 32-human room still rarely grows.</summary>
        public const int DefaultCapacity = 512 * 1024;

        public World World { get; private set; }
        public ReplayHeader Header { get; private set; }
        public bool Recording => World != null && !Done;
        public bool Done { get; private set; }
        /// <summary>Tick records written (ticks recorded).</summary>
        public int Ticks { get; private set; }
        public int InputBytes => _buf.Length;
        /// <summary>Set when the world can not be recorded (more than 64 actors); the recorder then ignores it.</summary>
        public string Refused { get; private set; }

        ReplayBytes _buf;
        int[] _fpTick = new int[0];
        ulong[] _fp = new ulong[0];
        int _fpCount;
        readonly ReplayInputStream.Lane[] _lanes = new ReplayInputStream.Lane[ReplayInputStream.MaxActors];
        readonly int[] _changes = new int[ReplayInputStream.MaxActors];
        readonly InputFrame[] _one = new InputFrame[1];
        readonly int[] _handToBot = new int[ReplayInputStream.MaxActors];
        int _handCount;
        readonly int[] _botToHuman = new int[ReplayInputStream.MaxActors];   // b15-rejoin
        int _backCount;
        ulong _lastMask;
        bool _lastNoHuman;
        int _repeat;
        bool _any;

        public ReplayRecorder(int capacity = DefaultCapacity)
        {
            _buf = new ReplayBytes(capacity);
            for (int i = 0; i < _lanes.Length; i++) _lanes[i] = new ReplayInputStream.Lane();
        }

        /// <summary>
        /// Starts recording w (at its current tick, normally 0) with the given header (start conditions, roster, build stamp
        /// are the caller's). False when the world has more actors than the format holds.
        /// </summary>
        public bool Begin(World w, ReplayHeader header)
        {
            World = null;
            Done = false;
            Refused = null;
            if (w == null) { Refused = "no world"; return false; }
            if (w.ActorCount > ReplayInputStream.MaxActors) { Refused = "more than 64 fighters"; return false; }
            Header = header ?? new ReplayHeader();
            if (Header.HashEvery < 1) Header.HashEvery = 30;
            Header.SimHz = w.Config != null && w.Config.SimHz > 0 ? w.Config.SimHz : 30;
            _buf.Clear();
            for (int i = 0; i < _lanes.Length; i++) _lanes[i].Reset();
            int expect = (int)Math.Min(1 << 16, (w.Config != null ? w.Config.MatchSeconds * Header.SimHz : 18000) / Header.HashEvery + 64);
            if (_fp.Length < expect) { _fp = new ulong[expect]; _fpTick = new int[expect]; }
            _fpCount = 0;
            _handCount = 0;
            _backCount = 0;
            _lastMask = 0;
            _lastNoHuman = false;
            _repeat = 0;
            _any = false;
            Ticks = 0;
            World = w;
            return true;
        }

        // ---------------------------------------------------------------- the tap (hot path: no allocation)

        public void OnAdvance(World w, InputFrame human)
        {
            if (!ReferenceEquals(w, World) || Done) return;
            _one[0] = human;
            Record(w, _one, false);
            _one[0] = null;
        }

        public void OnAdvanceHumans(World w, InputFrame[] frames)
        {
            if (!ReferenceEquals(w, World) || Done) return;
            Record(w, frames, false);
        }

        public void OnAdvanceNoHuman(World w)
        {
            if (!ReferenceEquals(w, World) || Done) return;
            Record(w, null, true);
        }

        public void OnHandToBot(World w, int id)
        {
            if (!ReferenceEquals(w, World) || Done) return;
            if (_handCount < _handToBot.Length) _handToBot[_handCount++] = id;
        }

        /// <summary>b15-rejoin: a rejoined player takes the fighter back before this tick's inputs.</summary>
        public void OnBotToHuman(World w, int id)
        {
            if (!ReferenceEquals(w, World) || Done) return;
            if (_backCount < _botToHuman.Length) _botToHuman[_backCount++] = id;
        }

        void Record(World w, InputFrame[] frames, bool noHuman)
        {
            if (w.Tick % Header.HashEvery == 0) AddFingerprint(w.Tick, StateHash.Of(w));
            int n = w.ActorCount;
            ulong mask = 0;
            bool changed = false;
            if (frames != null)
            {
                int m = Math.Min(n, frames.Length);
                for (int id = 0; id < m; id++)
                {
                    InputFrame f = frames[id];
                    if (f == null) continue;
                    mask |= 1UL << id;
                    int c = ReplayInputStream.Changes(_lanes[id].Last, f);
                    _changes[id] = c;
                    if (c != 0) changed = true;
                }
            }
            bool same = _any && !changed && mask == _lastMask && noHuman == _lastNoHuman && _handCount == 0 && _backCount == 0;
            Ticks++;
            if (same)
            {
                _repeat++;
                return;
            }
            FlushRepeat();
            int op = (noHuman ? ReplayInputStream.OpNoHuman : 0) | (mask != _lastMask ? ReplayInputStream.OpMask : 0) |
                     (_handCount > 0 ? ReplayInputStream.OpHandToBot : 0) | (_backCount > 0 ? ReplayInputStream.OpBotToHuman : 0);
            _buf.U8(op);
            if ((op & ReplayInputStream.OpMask) != 0) _buf.Var(mask);
            if (_handCount > 0)
            {
                _buf.U8(_handCount);
                for (int i = 0; i < _handCount; i++) _buf.U8(_handToBot[i]);
                _handCount = 0;
            }
            if (_backCount > 0)
            {
                _buf.U8(_backCount);
                for (int i = 0; i < _backCount; i++) _buf.U8(_botToHuman[i]);
                _backCount = 0;
            }
            for (int id = 0; id < n && mask >> id != 0; id++)
                if ((mask & (1UL << id)) != 0) ReplayInputStream.PutFrame(ref _buf, _lanes[id], frames[id], _changes[id]);
            _lastMask = mask;
            _lastNoHuman = noHuman;
            _any = true;
        }

        void FlushRepeat()
        {
            if (_repeat == 0) return;
            _buf.U8(ReplayInputStream.OpRepeat);
            _buf.Var((ulong)_repeat);
            _repeat = 0;
        }

        void AddFingerprint(int tick, ulong h)
        {
            if (_fpCount == _fp.Length)
            {
                Array.Resize(ref _fp, Math.Max(16, _fp.Length * 2));
                Array.Resize(ref _fpTick, _fp.Length);
            }
            _fpTick[_fpCount] = tick;
            _fp[_fpCount] = h;
            _fpCount++;
        }

        // ---------------------------------------------------------------- end

        /// <summary>
        /// Stops recording and returns the replay (allocates: once per match). finished: the match reached its final whistle.
        /// The header's result, length, hashes and date are filled from the world; nowUtcTicks is the caller's clock.
        /// </summary>
        public ReplayData Finish(bool finished, long nowUtcTicks)
        {
            if (World == null) return null;
            FlushRepeat();
            Done = true;
            World w = World;
            ReplayHeader h = Header;
            h.Ticks = Ticks;
            h.FinalHash = StateHash.Of(w);
            h.ConfigHash = ConfigHash.Of(w.Config);
            h.RecordedUtcTicks = nowUtcTicks;
            h.MatchTime = w.Match.Time;
            h.Winner = w.Match.Winner;
            h.Finished = finished;
            h.HillAzure = w.Match.HillSeconds.Azure;
            h.HillCrimson = w.Match.HillSeconds.Crimson;
            int ka = 0, kc = 0;
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (w.Actors[i].Team == CriticalMass.Sim.Config.TeamId.Azure) ka += w.Actors[i].Kills;
                else kc += w.Actors[i].Kills;
            }
            h.KillsAzure = ka;
            h.KillsCrimson = kc;
            if (h.Roster == null || h.Roster.Length != w.ActorCount)
            {
                var roster = new ReplayActor[w.ActorCount];
                for (int i = 0; i < roster.Length; i++)
                {
                    roster[i].Team = w.Actors[i].Team;
                    roster[i].Class = w.Actors[i].Cls;
                    roster[i].Human = w.Actors[i].IsHuman;
                    roster[i].Name = h.Roster != null && i < h.Roster.Length ? h.Roster[i].Name ?? "" : "";
                }
                h.Roster = roster;
            }
            var d = new ReplayData
            {
                Header = h,
                FingerprintCount = _fpCount,
                FingerprintTicks = new int[_fpCount],
                Fingerprints = new ulong[_fpCount],
                Inputs = _buf.ToArray(),
                InputLength = _buf.Length,
            };
            Array.Copy(_fpTick, d.FingerprintTicks, _fpCount);
            Array.Copy(_fp, d.Fingerprints, _fpCount);
            World = null;
            return d;
        }

        /// <summary>Drops the recording without a replay.</summary>
        public void Cancel()
        {
            World = null;
            Done = true;
        }
    }
}
