using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Net.Replay
{
    /// <summary>b13-replay: how the replay's world is rebuilt.</summary>
    public enum ReplayKind : byte { Offline = 0, Online = 1 }

    /// <summary>b13-replay: the config factory the match started from (the game always uses Default; tests use Browser5v5).</summary>
    public enum ConfigBasis : byte { Default = 0, Browser5v5 = 1 }

    /// <summary>
    /// b13-replay: the starting conditions of a recorded match, enough to rebuild the identical world: the config recipe
    /// (factory, optional rules profile, the mode's match rules) and every WorldOptions field the world is created from.
    /// The recorded ConfigHash and the tick-0 fingerprint prove the rebuild at playback.
    /// </summary>
    public sealed class ReplayStart
    {
        public ReplayKind Kind;
        public ConfigBasis Basis;
        /// <summary>-1: the factory's own rules; otherwise a RulesProfile applied with GameConfig.ApplyRules.</summary>
        public int RulesProfileId = -1;
        /// <summary>The mode's match rules (PlayModes.MatchRules): 0 = the config's own value.</summary>
        public double MatchSeconds, HatchSeconds, PassiveBotSeconds;
        public int Seed;
        public int TeamSize;
        public TeamId HumanTeam;
        public ClassId HumanClass;
        public BotDifficulty BotDifficulty, AllyBotDifficulty;
        public double Pace;
        public int[] ExtraHumans = new int[0];
        public ClassId[] ExtraHumanClasses = new ClassId[0];
        /// <summary>The actor the recording machine's player played (the camera and HUD follow it in playback).</summary>
        public int LocalActor;

        /// <summary>A fresh config exactly as the recorded match built it (same order as MatchRequest.ToWorldOptions).</summary>
        public GameConfig BuildConfig()
        {
            GameConfig cfg = Basis == ConfigBasis.Browser5v5 ? GameConfig.CreateBrowser5v5() : GameConfig.CreateDefault();
            if (RulesProfileId >= 0) cfg = cfg.ApplyRules((CriticalMass.Sim.Config.RulesProfile)RulesProfileId);
            if (MatchSeconds > 0) cfg.MatchSeconds = MatchSeconds;
            if (HatchSeconds > 0) cfg.HatchSeconds = Math.Min(HatchSeconds, cfg.MatchSeconds);
            if (PassiveBotSeconds > 0 && cfg.Bots != null) cfg.Bots.PassiveSeconds = PassiveBotSeconds;
            return cfg;
        }

        public WorldOptions ToWorldOptions(GameConfig cfg) => new WorldOptions
        {
            Seed = Seed,
            TeamSize = TeamSize,
            HumanTeam = HumanTeam,
            HumanClass = HumanClass,
            Config = cfg,
            BotDifficulty = BotDifficulty,
            AllyBotDifficulty = AllyBotDifficulty,
            Pace = Pace,
            ExtraHumans = ExtraHumans != null && ExtraHumans.Length > 0 ? (int[])ExtraHumans.Clone() : null,
            ExtraHumanClasses = ExtraHumanClasses != null && ExtraHumanClasses.Length > 0 ? (ClassId[])ExtraHumanClasses.Clone() : null,
        };

        /// <summary>The starting conditions of the world options a match was created from (the Config itself is not copied).</summary>
        public static ReplayStart From(WorldOptions o, ReplayKind kind, ConfigBasis basis, int rulesProfile,
            double matchSeconds, double hatchSeconds, double passiveBotSeconds, int localActor) => new ReplayStart
        {
            Kind = kind,
            Basis = basis,
            RulesProfileId = rulesProfile,
            MatchSeconds = matchSeconds,
            HatchSeconds = hatchSeconds,
            PassiveBotSeconds = passiveBotSeconds,
            Seed = o.Seed,
            TeamSize = o.TeamSize,
            HumanTeam = o.HumanTeam,
            HumanClass = o.HumanClass,
            BotDifficulty = o.BotDifficulty,
            AllyBotDifficulty = o.AllyBotDifficulty,
            Pace = o.Pace,
            ExtraHumans = o.ExtraHumans != null ? (int[])o.ExtraHumans.Clone() : new int[0],
            ExtraHumanClasses = o.ExtraHumanClasses != null ? (ClassId[])o.ExtraHumanClasses.Clone() : new ClassId[0],
            LocalActor = localActor,
        };
    }

    /// <summary>b13-replay: one fighter as the list screen and the follow camera show it.</summary>
    public struct ReplayActor
    {
        public TeamId Team;
        public ClassId Class;
        public bool Human;
        /// <summary>A human's name ("" for bots).</summary>
        public string Name;
    }

    /// <summary>
    /// b13-replay: the small header the list screen reads without the body: format and build stamp, ConfigHash, date,
    /// length, the result, map and mode, the roster, and the starting conditions.
    /// </summary>
    public sealed class ReplayHeader
    {
        public int FormatVersion = ReplayFile.FormatVersion;
        public string BuildVersion = "";
        public string BuildCommit = "";
        public int SimSchema = CriticalMass.Sim.SimVersion.Schema;
        /// <summary>ConfigHash.Of(world.Config) of the recorded world (compared, like for like, with the rebuilt world's).</summary>
        public ulong ConfigHash;
        /// <summary>Recording finished at this UTC time (DateTime.Ticks, invariant).</summary>
        public long RecordedUtcTicks;
        /// <summary>Recorded ticks (one input record per tick).</summary>
        public int Ticks;
        public double SimHz = 30;
        /// <summary>Match clock at the end (sim seconds).</summary>
        public double MatchTime;
        public MatchResult Winner;
        public int KillsAzure, KillsCrimson;
        public double HillAzure, HillCrimson;
        /// <summary>false: the match was left before the final whistle.</summary>
        public bool Finished;
        public string MapName = "";
        public int Mode;
        public string ModeLabel = "";
        /// <summary>Ticks between fingerprints (StateHash.Of), the online room's own HashEvery.</summary>
        public int HashEvery = 30;
        /// <summary>StateHash.Of(world) after the last recorded tick.</summary>
        public ulong FinalHash;
        public ReplayStart Start = new ReplayStart();
        public ReplayActor[] Roster = new ReplayActor[0];

        /// <summary>The recorded length in sim seconds (ticks / SimHz).</summary>
        public double SimSeconds => SimHz > 0 ? Ticks / SimHz : 0;
        public DateTime RecordedUtc => new DateTime(RecordedUtcTicks < 0 ? 0 : RecordedUtcTicks, DateTimeKind.Utc);
    }

    /// <summary>b13-replay: a whole replay in memory: header, periodic fingerprints and the encoded input stream.</summary>
    public sealed class ReplayData
    {
        public ReplayHeader Header = new ReplayHeader();
        public int FingerprintCount;
        public int[] FingerprintTicks = new int[0];
        public ulong[] Fingerprints = new ulong[0];
        /// <summary>The ReplayInputStream bytes (InputLength of them are valid).</summary>
        public byte[] Inputs = new byte[0];
        public int InputLength;
    }

    /// <summary>Why a replay file could not be read or played.</summary>
    public enum ReplayReadStatus { Ok, NotAReplay, NewerFormat, Damaged }

    /// <summary>
    /// b13-replay: the file. Little-endian throughout, culture-free (no text formatting of numbers anywhere):
    /// <code>
    /// "CMRP" | u16 format | u32 headerLen | header | u64 FNV-1a(header)
    ///        | u8 bodyFlags (1 = deflate) | u32 storedLen | u32 rawLen | stored body | u64 FNV-1a(stored body)
    /// body (raw) = u32 fingerprintCount | (i32 tick, u64 hash) * count | u32 inputLen | input stream
    /// </code>
    /// Reading never throws: a truncated, damaged or foreign file returns a status and a plain reason.
    /// </summary>
    public static class ReplayFile
    {
        public const int FormatVersion = 1;
        public static readonly byte[] Magic = { (byte)'C', (byte)'M', (byte)'R', (byte)'P' };
        /// <summary>Guards against a hostile length field (a 10-minute 16v16 is far below this).</summary>
        public const int MaxSection = 64 * 1024 * 1024;
        const int MaxRoster = 64, MaxExtra = 64;

        // ------------------------------------------------------------------ write

        /// <summary>The whole file. compress: deflate the body (falls back to stored if the platform's deflate fails).</summary>
        public static byte[] Write(ReplayData d, bool compress = true)
        {
            var h = new ReplayBytes(1024);
            WriteHeader(ref h, d.Header);
            var body = new ReplayBytes(d.InputLength + 16 + d.FingerprintCount * 12);
            body.U32((uint)d.FingerprintCount);
            for (int i = 0; i < d.FingerprintCount; i++)
            {
                body.I32(d.FingerprintTicks[i]);
                body.U64(d.Fingerprints[i]);
            }
            body.U32((uint)d.InputLength);
            body.Raw(d.Inputs, 0, d.InputLength);

            byte flags = 0;
            byte[] stored = body.ToArray();
            if (compress)
            {
                byte[] z = TryDeflate(body.Buffer, body.Length);
                if (z != null && z.Length < body.Length) { stored = z; flags = 1; }
            }

            var f = new ReplayBytes(16 + h.Length + stored.Length + 32);
            f.Raw(Magic, 0, 4);
            f.U16(FormatVersion);
            f.U32((uint)h.Length);
            f.Raw(h.Buffer, 0, h.Length);
            f.U64(Fnv(h.Buffer, 0, h.Length));
            f.U8(flags);
            f.U32((uint)stored.Length);
            f.U32((uint)body.Length);
            f.Raw(stored, 0, stored.Length);
            f.U64(Fnv(stored, 0, stored.Length));
            return f.ToArray();
        }

        static void WriteHeader(ref ReplayBytes w, ReplayHeader h)
        {
            w.Str(h.BuildVersion);
            w.Str(h.BuildCommit);
            w.I32(h.SimSchema);
            w.U64(h.ConfigHash);
            w.I64(h.RecordedUtcTicks);
            w.I32(h.Ticks);
            w.F64(h.SimHz);
            w.F64(h.MatchTime);
            w.U8((int)h.Winner);
            w.I32(h.KillsAzure);
            w.I32(h.KillsCrimson);
            w.F64(h.HillAzure);
            w.F64(h.HillCrimson);
            w.U8(h.Finished ? 1 : 0);
            w.Str(h.MapName);
            w.I32(h.Mode);
            w.Str(h.ModeLabel);
            w.I32(h.HashEvery);
            w.U64(h.FinalHash);
            ReplayStart s = h.Start ?? new ReplayStart();
            w.U8((int)s.Kind);
            w.U8((int)s.Basis);
            w.I32(s.RulesProfileId);
            w.F64(s.MatchSeconds);
            w.F64(s.HatchSeconds);
            w.F64(s.PassiveBotSeconds);
            w.I32(s.Seed);
            w.I32(s.TeamSize);
            w.U8((int)s.HumanTeam);
            w.U8((int)s.HumanClass);
            w.U8((int)s.BotDifficulty);
            w.U8((int)s.AllyBotDifficulty);
            w.F64(s.Pace);
            int ne = s.ExtraHumans != null ? Math.Min(s.ExtraHumans.Length, MaxExtra) : 0;
            w.U8(ne);
            for (int i = 0; i < ne; i++)
            {
                w.U8(s.ExtraHumans[i]);
                w.U8(s.ExtraHumanClasses != null && i < s.ExtraHumanClasses.Length ? (int)s.ExtraHumanClasses[i] : 0);
            }
            w.U8(s.LocalActor);
            int nr = h.Roster != null ? Math.Min(h.Roster.Length, MaxRoster) : 0;
            w.U8(nr);
            for (int i = 0; i < nr; i++)
            {
                w.U8((int)h.Roster[i].Team);
                w.U8((int)h.Roster[i].Class);
                w.U8(h.Roster[i].Human ? 1 : 0);
                w.Str(h.Roster[i].Name);
            }
        }

        // ------------------------------------------------------------------ read

        /// <summary>Header only (the list screen): reads the first section of the file bytes.</summary>
        public static ReplayReadStatus TryReadHeader(byte[] file, int length, out ReplayHeader header, out string why)
        {
            header = null;
            try
            {
                var r = new ReplayReader(file, 0, Math.Min(length, file != null ? file.Length : 0));
                ReplayReadStatus st = ReadPreamble(ref r, out int headerStart, out int headerLen, out why);
                if (st != ReplayReadStatus.Ok) return st;
                var hr = new ReplayReader(file, headerStart, headerLen);
                header = ReadHeader(ref hr);
                if (hr.Bad || header == null) { header = null; why = "The replay's header is damaged."; return ReplayReadStatus.Damaged; }
                return ReplayReadStatus.Ok;
            }
            catch (Exception e)
            {
                header = null;
                why = "The replay could not be read (" + e.GetType().Name + ").";
                return ReplayReadStatus.Damaged;
            }
        }

        /// <summary>The whole replay; never throws.</summary>
        public static ReplayReadStatus TryRead(byte[] file, out ReplayData data, out string why)
        {
            data = null;
            try
            {
                int len = file != null ? file.Length : 0;
                var r = new ReplayReader(file, 0, len);
                ReplayReadStatus st = ReadPreamble(ref r, out int headerStart, out int headerLen, out why);
                if (st != ReplayReadStatus.Ok) return st;
                var hr = new ReplayReader(file, headerStart, headerLen);
                ReplayHeader header = ReadHeader(ref hr);
                if (hr.Bad || header == null) { why = "The replay's header is damaged."; return ReplayReadStatus.Damaged; }
                int flags = r.U8();
                uint storedLen = r.U32(), rawLen = r.U32();
                if (r.Bad || flags > 1 || storedLen > MaxSection || rawLen > MaxSection || storedLen > (uint)(len - r.Pos - 8))
                {
                    why = "The replay file is cut short.";
                    return ReplayReadStatus.Damaged;
                }
                int storedAt = r.Pos;
                r.Skip((int)storedLen);
                ulong sum = r.U64();
                if (r.Bad || sum != Fnv(file, storedAt, (int)storedLen)) { why = "The replay file is damaged."; return ReplayReadStatus.Damaged; }
                byte[] raw;
                int rawAt;
                if ((flags & 1) != 0)
                {
                    raw = Inflate(file, storedAt, (int)storedLen, (int)rawLen);
                    rawAt = 0;
                    if (raw == null) { why = "The replay file is damaged."; return ReplayReadStatus.Damaged; }
                }
                else
                {
                    raw = file;
                    rawAt = storedAt;
                    if (rawLen != storedLen) { why = "The replay file is damaged."; return ReplayReadStatus.Damaged; }
                }
                var b = new ReplayReader(raw, rawAt, (int)rawLen);
                uint fc = b.U32();
                if (b.Bad || fc > rawLen / 12) { why = "The replay file is damaged."; return ReplayReadStatus.Damaged; }
                var d = new ReplayData { Header = header, FingerprintCount = (int)fc, FingerprintTicks = new int[fc], Fingerprints = new ulong[fc] };
                for (int i = 0; i < fc; i++)
                {
                    d.FingerprintTicks[i] = b.I32();
                    d.Fingerprints[i] = b.U64();
                }
                uint il = b.U32();
                if (b.Bad || il != (uint)(b.End - b.Pos)) { why = "The replay file is damaged."; return ReplayReadStatus.Damaged; }
                d.Inputs = new byte[il];
                Buffer.BlockCopy(raw, b.Pos, d.Inputs, 0, (int)il);
                d.InputLength = (int)il;
                data = d;
                why = null;
                return ReplayReadStatus.Ok;
            }
            catch (Exception e)
            {
                data = null;
                why = "The replay could not be read (" + e.GetType().Name + ").";
                return ReplayReadStatus.Damaged;
            }
        }

        static ReplayReadStatus ReadPreamble(ref ReplayReader r, out int headerStart, out int headerLen, out string why)
        {
            headerStart = headerLen = 0;
            for (int i = 0; i < 4; i++)
                if (r.U8() != Magic[i] || r.Bad) { why = "This file is not a replay."; return ReplayReadStatus.NotAReplay; }
            int ver = r.U16();
            if (r.Bad) { why = "The replay file is cut short."; return ReplayReadStatus.Damaged; }
            if (ver > FormatVersion || ver < 1) { why = "This replay was saved by a newer version of the game."; return ReplayReadStatus.NewerFormat; }
            uint hl = r.U32();
            if (r.Bad || hl > MaxSection || hl > (uint)(r.End - r.Pos - 8)) { why = "The replay file is cut short."; return ReplayReadStatus.Damaged; }
            headerStart = r.Pos;
            headerLen = (int)hl;
            r.Skip(headerLen);
            ulong sum = r.U64();
            if (r.Bad || sum != Fnv(r.Data, headerStart, headerLen)) { why = "The replay's header is damaged."; return ReplayReadStatus.Damaged; }
            why = null;
            return ReplayReadStatus.Ok;
        }

        static ReplayHeader ReadHeader(ref ReplayReader r)
        {
            var h = new ReplayHeader
            {
                BuildVersion = r.Str(),
                BuildCommit = r.Str(),
                SimSchema = r.I32(),
                ConfigHash = r.U64(),
                RecordedUtcTicks = r.I64(),
                Ticks = r.I32(),
                SimHz = r.F64(),
                MatchTime = r.F64(),
                Winner = (MatchResult)r.U8(),
                KillsAzure = r.I32(),
                KillsCrimson = r.I32(),
                HillAzure = r.F64(),
                HillCrimson = r.F64(),
                Finished = r.U8() != 0,
                MapName = r.Str(),
                Mode = r.I32(),
                ModeLabel = r.Str(),
                HashEvery = r.I32(),
                FinalHash = r.U64(),
            };
            var s = new ReplayStart
            {
                Kind = (ReplayKind)r.U8(),
                Basis = (ConfigBasis)r.U8(),
                RulesProfileId = r.I32(),
                MatchSeconds = r.F64(),
                HatchSeconds = r.F64(),
                PassiveBotSeconds = r.F64(),
                Seed = r.I32(),
                TeamSize = r.I32(),
                HumanTeam = (TeamId)r.U8(),
                HumanClass = (ClassId)r.U8(),
                BotDifficulty = (BotDifficulty)r.U8(),
                AllyBotDifficulty = (BotDifficulty)r.U8(),
                Pace = r.F64(),
            };
            int ne = r.U8();
            if (r.Bad || ne > MaxExtra) return null;
            s.ExtraHumans = new int[ne];
            s.ExtraHumanClasses = new ClassId[ne];
            for (int i = 0; i < ne; i++)
            {
                s.ExtraHumans[i] = r.U8();
                s.ExtraHumanClasses[i] = (ClassId)r.U8();
            }
            s.LocalActor = r.U8();
            int nr = r.U8();
            if (r.Bad || nr > MaxRoster) return null;
            h.Roster = new ReplayActor[nr];
            for (int i = 0; i < nr; i++)
            {
                h.Roster[i].Team = (TeamId)r.U8();
                h.Roster[i].Class = (ClassId)r.U8();
                h.Roster[i].Human = r.U8() != 0;
                h.Roster[i].Name = r.Str();
            }
            h.Start = s;
            h.FormatVersion = FormatVersion;
            // Sanity: values a real recording always has (a damaged header that passed the checksum is still refused).
            if (r.Bad || h.Ticks < 0 || !(h.SimHz > 0 && h.SimHz <= 1000) || h.HashEvery < 1 || s.TeamSize < 1 || s.TeamSize > 32 ||
                (int)s.Kind > 1 || (int)s.Basis > 1 || !double.IsFinite(s.Pace) || !double.IsFinite(h.MatchTime)) return null;
            return h;
        }

        // ------------------------------------------------------------------ helpers

        public static ulong Fnv(byte[] b, int off, int len)
        {
            ulong h = 14695981039346656037UL;
            unchecked
            {
                for (int i = 0; i < len; i++)
                {
                    h ^= b[off + i];
                    h *= 1099511628211UL;
                }
            }
            return h;
        }

        static byte[] TryDeflate(byte[] src, int len)
        {
            try
            {
                using (var ms = new MemoryStream(len / 2 + 64))
                {
                    using (var z = new DeflateStream(ms, CompressionLevel.Optimal, true)) z.Write(src, 0, len);
                    return ms.ToArray();
                }
            }
            catch (Exception)
            {
                return null;   // a platform without a working deflate: the body is stored as is (flags 0)
            }
        }

        static byte[] Inflate(byte[] src, int off, int len, int rawLen)
        {
            try
            {
                var outBuf = new byte[rawLen];
                using (var ms = new MemoryStream(src, off, len, false))
                using (var z = new DeflateStream(ms, CompressionMode.Decompress))
                {
                    int got = 0;
                    while (got < rawLen)
                    {
                        int n = z.Read(outBuf, got, rawLen - got);
                        if (n <= 0) break;
                        got += n;
                    }
                    return got == rawLen ? outBuf : null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static string Utf8(byte[] b, int off, int len) => Encoding.UTF8.GetString(b, off, len);
    }

    /// <summary>b13-replay: a growable little-endian byte buffer (grows by doubling; preallocate to keep a hot path allocation-free).</summary>
    public struct ReplayBytes
    {
        public byte[] Buffer;
        public int Length;

        public ReplayBytes(int capacity)
        {
            Buffer = new byte[Math.Max(16, capacity)];
            Length = 0;
        }

        public void Clear() => Length = 0;

        public void Ensure(int more)
        {
            if (Length + more <= Buffer.Length) return;
            int cap = Buffer.Length * 2;
            while (cap < Length + more) cap *= 2;
            Array.Resize(ref Buffer, cap);
        }

        public void U8(int v) { Ensure(1); Buffer[Length++] = (byte)v; }
        public void U16(int v) { Ensure(2); Buffer[Length++] = (byte)v; Buffer[Length++] = (byte)(v >> 8); }
        public void U32(uint v) { Ensure(4); for (int i = 0; i < 4; i++) Buffer[Length++] = (byte)(v >> (8 * i)); }
        public void I32(int v) => U32(unchecked((uint)v));
        public void U64(ulong v) { Ensure(8); for (int i = 0; i < 8; i++) Buffer[Length++] = (byte)(v >> (8 * i)); }
        public void I64(long v) => U64(unchecked((ulong)v));
        public void F64(double v) => U64(unchecked((ulong)BitConverter.DoubleToInt64Bits(v)));

        /// <summary>LEB128 unsigned varint.</summary>
        public void Var(ulong v)
        {
            Ensure(10);
            while (v >= 0x80)
            {
                Buffer[Length++] = (byte)(v | 0x80);
                v >>= 7;
            }
            Buffer[Length++] = (byte)v;
        }

        public void Raw(byte[] src, int off, int len)
        {
            if (len <= 0) return;
            Ensure(len);
            System.Buffer.BlockCopy(src, off, Buffer, Length, len);
            Length += len;
        }

        /// <summary>UTF-8, u8 length, at most 64 bytes (cut on a character boundary). Header only: allocates.</summary>
        public void Str(string s)
        {
            byte[] b = Encoding.UTF8.GetBytes(s ?? "");
            int n = Math.Min(b.Length, 64);
            while (n > 0 && n < b.Length && (b[n] & 0xC0) == 0x80) n--;   // never split a character
            U8(n);
            Raw(b, 0, n);
        }

        public byte[] ToArray()
        {
            var a = new byte[Length];
            System.Buffer.BlockCopy(Buffer, 0, a, 0, Length);
            return a;
        }
    }

    /// <summary>b13-replay: a bounds-checked reader: past the end it returns zeros and sets Bad (never throws).</summary>
    public struct ReplayReader
    {
        public readonly byte[] Data;
        public int Pos;
        public readonly int End;
        public bool Bad;

        public ReplayReader(byte[] data, int off, int len)
        {
            Data = data ?? new byte[0];
            Pos = Math.Max(0, off);
            End = Math.Min(Data.Length, Pos + Math.Max(0, len));
            Bad = false;
        }

        bool Has(int n)
        {
            if (!Bad && Pos + n <= End) return true;
            Bad = true;
            return false;
        }

        public void Skip(int n) { if (n < 0 || !Has(n)) { Bad = true; return; } Pos += n; }
        public int U8() => Has(1) ? Data[Pos++] : 0;
        public int U16() { if (!Has(2)) return 0; int v = Data[Pos] | (Data[Pos + 1] << 8); Pos += 2; return v; }
        public uint U32() { if (!Has(4)) return 0; uint v = 0; for (int i = 0; i < 4; i++) v |= (uint)Data[Pos + i] << (8 * i); Pos += 4; return v; }
        public int I32() => unchecked((int)U32());
        public ulong U64() { if (!Has(8)) return 0; ulong v = 0; for (int i = 0; i < 8; i++) v |= (ulong)Data[Pos + i] << (8 * i); Pos += 8; return v; }
        public long I64() => unchecked((long)U64());
        public double F64() => BitConverter.Int64BitsToDouble(I64());

        public ulong Var()
        {
            ulong v = 0;
            for (int shift = 0; shift < 64; shift += 7)
            {
                if (!Has(1)) return 0;
                byte b = Data[Pos++];
                v |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return v;
            }
            Bad = true;
            return 0;
        }

        public string Str()
        {
            int n = U8();
            if (!Has(n)) return "";
            string s = ReplayFile.Utf8(Data, Pos, n);
            Pos += n;
            return s;
        }
    }
}
