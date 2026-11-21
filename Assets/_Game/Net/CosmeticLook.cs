using System;

namespace CriticalMass.Net
{
    /// <summary>
    /// b15 profile: what other players see of one player's class loadout (title, portrait frame, colour trim, emblem) and
    /// that class's mastery level. Cosmetic only: it rides in its own lobby messages (RoomMsg.Look / RoomMsg.Looks), never
    /// in MatchSetup, the config hash, the input stream or the state hash, so it cannot change the sim or the desync check.
    /// Every id is a small index into the reward table (Runtime/Progression/MasteryRewards); 0 = the class default (no
    /// title, plain frame, stock colours, no emblem). The unlock schedule lives here, next to the wire contract, so both
    /// ends clamp a received look the same way: an id out of range, or one that needs a higher mastery level than the
    /// sender claims, becomes 0.
    /// </summary>
    public struct CosmeticLook : IEquatable<CosmeticLook>
    {
        /// <summary>Bytes on the wire: title, frame, trim, emblem, mastery.</summary>
        public const int WireBytes = 5;
        public const int MaxMastery = 20;

        /// <summary>Mastery level at which title k (1-based) unlocks; index 0 is unused (0 = no title).</summary>
        public static readonly int[] TitleLevels = { 0, 1, 3, 7, 11, 14, 18, 20 };
        /// <summary>Frames: 1 Bronze, 2 Silver, 3 Gold, 4 Basin Crown.</summary>
        public static readonly int[] FrameLevels = { 0, 5, 10, 15, 20 };
        /// <summary>Colour trims (palette variants of the fixed, non-team colours).</summary>
        public static readonly int[] TrimLevels = { 0, 4, 8, 12, 16, 19 };
        /// <summary>Emblems drawn from simple shapes.</summary>
        public static readonly int[] EmblemLevels = { 0, 2, 6, 9, 13, 17 };

        public static int TitleCount => TitleLevels.Length - 1;
        public static int FrameCount => FrameLevels.Length - 1;
        public static int TrimCount => TrimLevels.Length - 1;
        public static int EmblemCount => EmblemLevels.Length - 1;

        public byte Title, Frame, Trim, Emblem, Mastery;

        public CosmeticLook(int title, int frame, int trim, int emblem, int mastery)
        {
            Title = ToByte(title);
            Frame = ToByte(frame);
            Trim = ToByte(trim);
            Emblem = ToByte(emblem);
            Mastery = ToByte(mastery);
        }

        static byte ToByte(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);

        /// <summary>No look at all (bots, unknown peers): every slot default, mastery 1.</summary>
        public static CosmeticLook Default => new CosmeticLook(0, 0, 0, 0, 1);

        public bool IsDefault => Title == 0 && Frame == 0 && Trim == 0 && Emblem == 0;

        /// <summary>Is id (1-based) of a slot unlocked at mastery level? 0 is always allowed.</summary>
        public static bool Allowed(int[] levels, int id, int mastery) =>
            id == 0 || (levels != null && id > 0 && id < levels.Length && levels[id] <= mastery);

        /// <summary>
        /// The look made safe: mastery clamped to 1..MaxMastery, then each id that is out of range or above that mastery
        /// becomes 0. Pure; used on every look a peer sends and on the local one before it is sent.
        /// </summary>
        public static CosmeticLook Clamp(CosmeticLook l)
        {
            int m = l.Mastery < 1 ? 1 : l.Mastery > MaxMastery ? MaxMastery : l.Mastery;
            return new CosmeticLook(
                Allowed(TitleLevels, l.Title, m) ? l.Title : 0,
                Allowed(FrameLevels, l.Frame, m) ? l.Frame : 0,
                Allowed(TrimLevels, l.Trim, m) ? l.Trim : 0,
                Allowed(EmblemLevels, l.Emblem, m) ? l.Emblem : 0,
                m);
        }

        public void Write(ref WireWriter w)
        {
            w.U8(Title);
            w.U8(Frame);
            w.U8(Trim);
            w.U8(Emblem);
            w.U8(Mastery);
        }

        /// <summary>Reads five bytes and clamps them (a short packet leaves r.Bad set and returns Default).</summary>
        public static CosmeticLook Read(ref WireReader r)
        {
            var l = new CosmeticLook(r.U8(), r.U8(), r.U8(), r.U8(), r.U8());
            return r.Bad ? Default : Clamp(l);
        }

        public bool Equals(CosmeticLook o) => Title == o.Title && Frame == o.Frame && Trim == o.Trim && Emblem == o.Emblem && Mastery == o.Mastery;
        public override bool Equals(object obj) => obj is CosmeticLook o && Equals(o);
        public override int GetHashCode() => Title | (Frame << 8) | (Trim << 16) | (Emblem << 24) ^ (Mastery * 397);
        public override string ToString() => $"title {Title} frame {Frame} trim {Trim} emblem {Emblem} mastery {Mastery}";
    }
}
