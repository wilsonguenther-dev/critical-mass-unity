using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Net
{
    /// <summary>
    /// One human's InputFrame as exactly Size bytes on the wire. Quantised: move in 1/127 steps, aim and ping points in
    /// 1/100 sim units. EVERY machine, the sender included, applies the decoded frame (never the raw local one), so a key
    /// press produces the same doubles everywhere. Decoding never fails and never produces a value ApplyInput would reject
    /// as unsafe: out-of-range fields are clamped or cleared, so a hostile peer can only send legal key presses.
    /// </summary>
    public static class InputCodec
    {
        public const int Size = 14;
        /// <summary>Aim and ping points are clamped to this many sim units from the origin (the arena is about 112 x 72).</summary>
        public const double CoordLimit = 320;

        private const int BAttack = 1, BAbility = 2, BInteract = 4, BTerraform = 8, BJump = 16, BSwitch = 32, BHasAim = 64,
            BDodge = 128;   // b13-hook: CM b13 dodge, same byte as BJump (Size stays 14)
        /// <summary>b16 CANCEL rides the top bit of the ping byte (PingKind is 0..4), so Size stays 14.</summary>
        private const int PCancel = 128;

        public static void Encode(InputFrame f, byte[] buf, int off)
        {
            buf[off + 0] = (byte)(sbyte)QMove(f.MoveX);
            buf[off + 1] = (byte)(sbyte)QMove(f.MoveZ);
            bool aim = double.IsFinite(f.AimX) && double.IsFinite(f.AimZ);
            Put16(buf, off + 2, aim ? QCoord(f.AimX) : 0);
            Put16(buf, off + 4, aim ? QCoord(f.AimZ) : 0);
            int b = 0;
            if (f.Attack) b |= BAttack;
            if (f.Ability) b |= BAbility;
            if (f.Interact) b |= BInteract;
            if (f.Terraform) b |= BTerraform;
            if (f.Jump) b |= BJump;
            if (f.TerraformSwitch) b |= BSwitch;
            if (aim) b |= BHasAim;
            if (f.Dodge) b |= BDodge;   // b13-hook
            buf[off + 6] = (byte)b;
            buf[off + 7] = (byte)(sbyte)(f.ClassPick >= 0 && f.ClassPick < ClassIds.All.Count ? f.ClassPick : -1);
            buf[off + 8] = (byte)(f.StationBuy >= 1 && f.StationBuy <= ClassIds.All.Count ? f.StationBuy : 0);
            bool ping = f.PingKind >= 1 && f.PingKind <= 4 && double.IsFinite(f.PingX) && double.IsFinite(f.PingZ);
            buf[off + 9] = (byte)((ping ? f.PingKind : 0) | (f.Cancel ? PCancel : 0));   // b16 cancel
            Put16(buf, off + 10, ping ? QCoord(f.PingX) : 0);
            Put16(buf, off + 12, ping ? QCoord(f.PingZ) : 0);
        }

        /// <summary>Decodes Size bytes at off into f (no allocation). Every field is range-checked.</summary>
        public static void Decode(byte[] buf, int off, InputFrame f)
        {
            int mx = (sbyte)buf[off + 0], mz = (sbyte)buf[off + 1];
            f.MoveX = (mx < -127 ? -127 : mx) / 127.0;
            f.MoveZ = (mz < -127 ? -127 : mz) / 127.0;
            int b = buf[off + 6];
            if ((b & BHasAim) != 0)
            {
                f.AimX = Coord(Get16(buf, off + 2));
                f.AimZ = Coord(Get16(buf, off + 4));
            }
            else
            {
                f.AimX = double.NaN;   // "no aim": ApplyInput aims at the actor itself (D-011)
                f.AimZ = double.NaN;
            }
            f.Attack = (b & BAttack) != 0;
            f.Ability = (b & BAbility) != 0;
            f.Interact = (b & BInteract) != 0;
            f.Terraform = (b & BTerraform) != 0;
            f.Jump = (b & BJump) != 0;
            f.TerraformSwitch = (b & BSwitch) != 0;
            f.Dodge = (b & BDodge) != 0;   // b13-hook
            int cp = (sbyte)buf[off + 7];
            f.ClassPick = cp >= 0 && cp < ClassIds.All.Count ? cp : -1;
            int sb = buf[off + 8];
            f.StationBuy = sb >= 1 && sb <= ClassIds.All.Count ? sb : 0;
            int pk = buf[off + 9] & 0x7F;
            f.Cancel = (buf[off + 9] & PCancel) != 0;   // b16 cancel
            f.PingKind = pk >= 1 && pk <= 4 ? pk : 0;
            f.PingX = f.PingKind != 0 ? Coord(Get16(buf, off + 10)) : 0;
            f.PingZ = f.PingKind != 0 ? Coord(Get16(buf, off + 12)) : 0;
        }

        /// <summary>The neutral frame's bytes (no move, no buttons, no aim, no pick).</summary>
        public static void EncodeNeutral(byte[] buf, int off)
        {
            for (int i = 0; i < Size; i++) buf[off + i] = 0;
            buf[off + 7] = unchecked((byte)(sbyte)-1);
        }

        private static int QMove(double v)
        {
            if (!double.IsFinite(v)) return 0;
            double q = v * 127.0;
            if (q > 127) q = 127;
            if (q < -127) q = -127;
            return (int)System.Math.Round(q, System.MidpointRounding.AwayFromZero);
        }

        private static int QCoord(double v)
        {
            if (v > CoordLimit) v = CoordLimit;
            if (v < -CoordLimit) v = -CoordLimit;
            return (int)System.Math.Round(v * 100.0, System.MidpointRounding.AwayFromZero);
        }

        private static double Coord(int q)
        {
            double v = q / 100.0;
            return v > CoordLimit ? CoordLimit : v < -CoordLimit ? -CoordLimit : v;
        }

        private static void Put16(byte[] b, int o, int v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
        private static int Get16(byte[] b, int o) => (short)(b[o] | (b[o + 1] << 8));
    }
}
