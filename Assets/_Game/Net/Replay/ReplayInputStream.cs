using System;
using CriticalMass.Sim.Core;

namespace CriticalMass.Net.Replay
{
    /// <summary>
    /// b13-replay: the per-tick input stream, lossless and delta encoded. One record per sim tick, in tick order:
    /// <code>
    /// op (u8): bit0 NoHuman tick (AdvanceNoHuman) | bit1 presence mask changed (varint mask follows)
    ///          | bit2 hand-to-bot ids follow (u8 count, u8 id each) | bit3 REPEAT (varint n: the previous tick's inputs n more times)
    /// then, for every actor whose bit is set in the presence mask (id order): u8 change bits, then each changed field.
    /// </code>
    /// Change bits: 0 MoveX, 1 MoveZ, 2 AimX, 3 AimZ, 4 buttons (u8: Attack, Ability, Interact, Terraform, Jump,
    /// TerraformSwitch, Dodge = bit 64 since CM b13, Cancel = bit 128 since b16), 5 picks (i8 ClassPick, u8 StationBuy, u8 PingKind), 6 PingX, 7 PingZ.
    /// A double is written as a varint: even = zig-zag delta of its quantum index (move 1/127, points 1/100, which is
    /// exactly what online frames hold and what keyboards, sticks at rest and ping points mostly hold), odd (1) = the raw
    /// 8 bytes follow. Either way the decoded double has the recorded bits exactly, so playback consumes the same frames.
    /// Presence: bit id set = actor id received a frame this tick; clear = a bot drove it. 64 actors at most.
    /// Writing never allocates once the buffer has room (the recorder preallocates it).
    /// </summary>
    public static class ReplayInputStream
    {
        public const int MaxActors = 64;
        public const int OpNoHuman = 1, OpMask = 2, OpHandToBot = 4, OpRepeat = 8;
        /// <summary>b15-rejoin: bot-to-human ids follow (u8 count, u8 id each), after any hand-to-bot ids: a rejoined player took the fighter back.</summary>
        public const int OpBotToHuman = 16;
        public const int CMoveX = 1, CMoveZ = 2, CAimX = 4, CAimZ = 8, CButtons = 16, CPicks = 32, CPingX = 64, CPingZ = 128;
        public const double MoveQuantum = 127.0, PointQuantum = 100.0;
        /// <summary>Beyond this magnitude a double is always stored raw (no quantum index overflow, no NaN casts).</summary>
        const double QuantLimit = 1e7;

        public static int Buttons(InputFrame f) =>
            (f.Attack ? 1 : 0) | (f.Ability ? 2 : 0) | (f.Interact ? 4 : 0) | (f.Terraform ? 8 : 0) | (f.Jump ? 16 : 0) | (f.TerraformSwitch ? 32 : 0)
            | (f.Dodge ? 64 : 0)   // b13-hook
            | (f.Cancel ? 128 : 0);   // b16 cancel: bit 128 was never written before, so older replays read Cancel false

        public static void SetButtons(InputFrame f, int b)
        {
            f.Attack = (b & 1) != 0;
            f.Ability = (b & 2) != 0;
            f.Interact = (b & 4) != 0;
            f.Terraform = (b & 8) != 0;
            f.Jump = (b & 16) != 0;
            f.TerraformSwitch = (b & 32) != 0;
            f.Dodge = (b & 64) != 0;   // b13-hook
            f.Cancel = (b & 128) != 0;   // b16 cancel
        }

        static bool Same(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);

        /// <summary>The fields of f that differ from prev (bit for bit).</summary>
        public static int Changes(InputFrame prev, InputFrame f)
        {
            int c = 0;
            if (!Same(prev.MoveX, f.MoveX)) c |= CMoveX;
            if (!Same(prev.MoveZ, f.MoveZ)) c |= CMoveZ;
            if (!Same(prev.AimX, f.AimX)) c |= CAimX;
            if (!Same(prev.AimZ, f.AimZ)) c |= CAimZ;
            if (Buttons(prev) != Buttons(f)) c |= CButtons;
            if (prev.ClassPick != f.ClassPick || prev.StationBuy != f.StationBuy || prev.PingKind != f.PingKind) c |= CPicks;
            if (!Same(prev.PingX, f.PingX)) c |= CPingX;
            if (!Same(prev.PingZ, f.PingZ)) c |= CPingZ;
            return c;
        }

        static bool Quantisable(double v) => double.IsFinite(v) && v < QuantLimit && v > -QuantLimit;

        static ulong Zig(long v) => (ulong)((v << 1) ^ (v >> 63));
        static long Unzig(ulong u) => (long)(u >> 1) ^ -(long)(u & 1);

        /// <summary>Writes v against the field's last quantum index q (updated).</summary>
        public static void PutDouble(ref ReplayBytes w, double v, ref long q, double quantum)
        {
            if (Quantisable(v))
            {
                long k = (long)Math.Round(v * quantum, MidpointRounding.AwayFromZero);
                if (Same(k / quantum, v))
                {
                    w.Var(Zig(k - q) << 1);
                    q = k;
                    return;
                }
                w.Var(1);
                w.F64(v);
                q = k;
                return;
            }
            w.Var(1);
            w.F64(v);
        }

        public static double GetDouble(ref ReplayReader r, ref long q, double quantum)
        {
            ulong u = r.Var();
            if ((u & 1) == 0)
            {
                long k = q + Unzig(u >> 1);
                q = k;
                return k / quantum;
            }
            double v = r.F64();
            if (Quantisable(v)) q = (long)Math.Round(v * quantum, MidpointRounding.AwayFromZero);
            return v;
        }

        /// <summary>The per-actor state both sides keep: the last frame and the quantum index of each double field.</summary>
        public sealed class Lane
        {
            public readonly InputFrame Last = SimInput.EmptyInput();
            public long QMoveX, QMoveZ, QAimX, QAimZ, QPingX, QPingZ;

            public void Reset()
            {
                Last.CopyFrom(SimInput.EmptyInput());
                QMoveX = QMoveZ = QAimX = QAimZ = QPingX = QPingZ = 0;
            }
        }

        /// <summary>Writes f's changed fields (c from Changes) and makes f the lane's last frame.</summary>
        public static void PutFrame(ref ReplayBytes w, Lane lane, InputFrame f, int c)
        {
            w.U8(c);
            if ((c & CMoveX) != 0) PutDouble(ref w, f.MoveX, ref lane.QMoveX, MoveQuantum);
            if ((c & CMoveZ) != 0) PutDouble(ref w, f.MoveZ, ref lane.QMoveZ, MoveQuantum);
            if ((c & CAimX) != 0) PutDouble(ref w, f.AimX, ref lane.QAimX, PointQuantum);
            if ((c & CAimZ) != 0) PutDouble(ref w, f.AimZ, ref lane.QAimZ, PointQuantum);
            if ((c & CButtons) != 0) w.U8(Buttons(f));
            if ((c & CPicks) != 0)
            {
                w.U8((byte)(sbyte)Math.Max(-128, Math.Min(127, f.ClassPick)));
                w.U8(Math.Max(0, Math.Min(255, f.StationBuy)));
                w.U8(Math.Max(0, Math.Min(255, f.PingKind)));
            }
            if ((c & CPingX) != 0) PutDouble(ref w, f.PingX, ref lane.QPingX, PointQuantum);
            if ((c & CPingZ) != 0) PutDouble(ref w, f.PingZ, ref lane.QPingZ, PointQuantum);
            lane.Last.CopyFrom(f);
        }

        /// <summary>Reads one actor's frame record into the lane's last frame.</summary>
        public static void GetFrame(ref ReplayReader r, Lane lane)
        {
            InputFrame f = lane.Last;
            int c = r.U8();
            if ((c & CMoveX) != 0) f.MoveX = GetDouble(ref r, ref lane.QMoveX, MoveQuantum);
            if ((c & CMoveZ) != 0) f.MoveZ = GetDouble(ref r, ref lane.QMoveZ, MoveQuantum);
            if ((c & CAimX) != 0) f.AimX = GetDouble(ref r, ref lane.QAimX, PointQuantum);
            if ((c & CAimZ) != 0) f.AimZ = GetDouble(ref r, ref lane.QAimZ, PointQuantum);
            if ((c & CButtons) != 0) SetButtons(f, r.U8());
            if ((c & CPicks) != 0)
            {
                f.ClassPick = (sbyte)(byte)r.U8();
                f.StationBuy = r.U8();
                f.PingKind = r.U8();
            }
            if ((c & CPingX) != 0) f.PingX = GetDouble(ref r, ref lane.QPingX, PointQuantum);
            if ((c & CPingZ) != 0) f.PingZ = GetDouble(ref r, ref lane.QPingZ, PointQuantum);
        }
    }
}
