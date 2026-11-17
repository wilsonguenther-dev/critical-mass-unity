using System;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Net.Replay
{
    /// <summary>What the running game makes of a replay before playing it (b13-replay).</summary>
    public enum ReplayCompatibility { Ok, OtherVersion, NewerFormat, Damaged }

    /// <summary>
    /// b13-replay: the "is this replay from this game?" rules, pure. A replay plays only on the build that recorded it: same
    /// build version and commit (both "dev"/"" in the editor), same sim schema; the rebuilt world's ConfigHash and its tick-0
    /// fingerprint are then checked by ReplayPlayer itself (like for like), so a file that slipped through still never
    /// desyncs silently.
    /// </summary>
    public static class ReplayCompat
    {
        public const string OtherVersionText = "This replay is from a different game version";

        public static ReplayCompatibility Check(ReplayHeader h, string runningVersion, string runningCommit)
        {
            if (h == null) return ReplayCompatibility.Damaged;
            if (h.FormatVersion > ReplayFile.FormatVersion) return ReplayCompatibility.NewerFormat;
            if (h.SimSchema != CriticalMass.Sim.SimVersion.Schema) return ReplayCompatibility.OtherVersion;
            if (!string.Equals(h.BuildVersion ?? "", runningVersion ?? "", StringComparison.Ordinal)) return ReplayCompatibility.OtherVersion;
            if (!string.Equals(h.BuildCommit ?? "", runningCommit ?? "", StringComparison.Ordinal)) return ReplayCompatibility.OtherVersion;
            return ReplayCompatibility.Ok;
        }

        /// <summary>The plain sentence the replay screen shows for a status.</summary>
        public static string Message(ReplayCompatibility c, ReplayHeader h)
        {
            switch (c)
            {
                case ReplayCompatibility.Ok: return "";
                case ReplayCompatibility.NewerFormat: return "This replay was saved by a newer version of the game.";
                case ReplayCompatibility.OtherVersion:
                    string v = h != null && !string.IsNullOrEmpty(h.BuildVersion) ? " (v" + h.BuildVersion + ")" : "";
                    return OtherVersionText + v + ". It can only be watched in the version that recorded it.";
                default: return "This replay file is damaged and cannot be played.";
            }
        }
    }

    /// <summary>
    /// b13-replay: plays a replay: builds the world from the recorded starting conditions, then each Step decodes one tick
    /// record and runs MatchTick.AdvanceHumans with exactly the recorded frames (MatchTick.Advance(w, f) is documented
    /// bit-for-bit AdvanceHumans(w, [f]); AdvanceNoHuman ticks replay as AdvanceNoHuman; hand-overs to bots replay at the
    /// same tick). Before each tick whose number is a recorded fingerprint tick it compares StateHash.Of(world): the first
    /// difference stops playback (Desynced) instead of showing a different match. No allocation per Step.
    /// </summary>
    public sealed class ReplayPlayer
    {
        public ReplayData Data { get; }
        public ReplayHeader Header => Data.Header;
        public World World { get; }
        /// <summary>Ticks played so far (0..Header.Ticks).</summary>
        public int TicksPlayed { get; private set; }
        public int TotalTicks => Header.Ticks;
        public bool Finished => TicksPlayed >= TotalTicks || Desynced || _r.Bad;
        public bool Desynced { get; private set; }
        /// <summary>The world tick of the first mismatch (or of the end-of-stream check), -1 when none.</summary>
        public int DesyncTick { get; private set; } = -1;
        public int FingerprintsChecked { get; private set; }
        /// <summary>ConfigHash of the rebuilt world equals the recorded one (checked at construction).</summary>
        public bool ConfigMatches { get; }
        /// <summary>Set once the stream is exhausted: StateHash.Of(world) equals the recorded final hash.</summary>
        public bool FinalMatches { get; private set; }
        /// <summary>The stream itself is malformed (truncated inside a record).</summary>
        public bool StreamBroken => _r.Bad;

        ReplayReader _r;
        readonly ReplayInputStream.Lane[] _lanes;
        readonly InputFrame[] _frames;
        readonly InputFrame _scratch = SimInput.EmptyInput();
        ulong _mask;
        bool _noHuman;
        int _repeat;
        int _nextFp;
        bool _endChecked;

        public ReplayPlayer(ReplayData data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            ReplayStart s = data.Header.Start ?? new ReplayStart();
            GameConfig cfg = s.BuildConfig();
            World = World.CreateWorld(s.ToWorldOptions(cfg));
            ConfigMatches = data.Header.ConfigHash == 0 || ConfigHash.Of(World.Config) == data.Header.ConfigHash;
            _r = new ReplayReader(data.Inputs, 0, data.InputLength);
            int n = Math.Min(World.ActorCount, ReplayInputStream.MaxActors);
            _lanes = new ReplayInputStream.Lane[n];
            for (int i = 0; i < n; i++) _lanes[i] = new ReplayInputStream.Lane();
            _frames = new InputFrame[World.ActorCount];
            if (!ConfigMatches) { Desynced = true; DesyncTick = 0; }
        }

        /// <summary>Plays one tick. False (nothing done) when finished or out of step.</summary>
        public bool Step()
        {
            if (Finished)
            {
                CheckEnd();
                return false;
            }
            if (!Verify()) return false;
            if (_repeat > 0) _repeat--;
            else if (!ReadRecord()) return false;
            try
            {
                if (_noHuman) MatchTick.AdvanceNoHuman(World, _scratch);
                else MatchTick.AdvanceHumans(World, _frames, _scratch);
            }
            catch (Exception)
            {
                // Only a damaged stream can get here (a file's checksum stops damage before playback): stop, never crash.
                _r.Bad = true;
                Desynced = true;
                DesyncTick = World.Tick;
                return false;
            }
            TicksPlayed++;
            if (TicksPlayed >= TotalTicks) CheckEnd();
            return true;
        }

        /// <summary>Steps until n ticks ran or the replay ended; returns the ticks run.</summary>
        public int StepMany(int n)
        {
            int k = 0;
            while (k < n && Step()) k++;
            return k;
        }

        /// <summary>Plays the whole replay (tests, headless checks).</summary>
        public void RunToEnd()
        {
            while (Step()) { }
            CheckEnd();
        }

        bool Verify()
        {
            int t = World.Tick;
            while (_nextFp < Data.FingerprintCount && Data.FingerprintTicks[_nextFp] < t) _nextFp++;
            if (_nextFp < Data.FingerprintCount && Data.FingerprintTicks[_nextFp] == t)
            {
                ulong h = StateHash.Of(World);
                FingerprintsChecked++;
                if (h != Data.Fingerprints[_nextFp])
                {
                    Desynced = true;
                    DesyncTick = t;
                    return false;
                }
                _nextFp++;
            }
            return true;
        }

        bool ReadRecord()
        {
            int op = _r.U8();
            if (_r.Bad) return false;
            if ((op & ReplayInputStream.OpRepeat) != 0)
            {
                ulong n = _r.Var();
                if (_r.Bad || n == 0 || n > int.MaxValue) { _r.Bad = true; return false; }
                _repeat = (int)n - 1;   // this tick is the first of the n repeats
                return true;
            }
            _noHuman = (op & ReplayInputStream.OpNoHuman) != 0;
            if ((op & ReplayInputStream.OpMask) != 0) _mask = _r.Var();
            if ((op & ReplayInputStream.OpHandToBot) != 0)
            {
                int c = _r.U8();
                for (int i = 0; i < c; i++)
                {
                    int id = _r.U8();
                    MatchTick.HandToBot(World, id);
                }
            }
            for (int id = 0; id < _frames.Length; id++) _frames[id] = null;
            for (int id = 0; id < _lanes.Length && _mask >> id != 0; id++)
            {
                if ((_mask & (1UL << id)) == 0) continue;
                ReplayInputStream.GetFrame(ref _r, _lanes[id]);
                _frames[id] = _lanes[id].Last;
            }
            if (_r.Bad) return false;
            return true;
        }

        void CheckEnd()
        {
            if (_endChecked || Desynced || TicksPlayed < TotalTicks) return;
            _endChecked = true;
            FinalMatches = StateHash.Of(World) == Header.FinalHash;
            if (!FinalMatches)
            {
                Desynced = true;
                DesyncTick = World.Tick;
            }
        }
    }
}
