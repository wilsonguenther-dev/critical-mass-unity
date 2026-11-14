using System;

namespace CriticalMass.Net
{
    /// <summary>
    /// Little-endian writer over a caller-owned buffer (no allocation). Every packet on the wire is built with it, so the
    /// byte layout is the same on every CPU (BitConverter's order is the machine's; this never is).
    /// </summary>
    public struct WireWriter
    {
        public byte[] Buf;
        public int Pos;
        public bool Overflow;

        public WireWriter(byte[] buf, int pos = 0) { Buf = buf; Pos = pos; Overflow = false; }

        private bool Room(int n)
        {
            if (Pos + n <= Buf.Length) return true;
            Overflow = true;
            return false;
        }

        public void U8(int v) { if (Room(1)) Buf[Pos++] = (byte)v; }
        public void I16(int v) { if (!Room(2)) return; Buf[Pos++] = (byte)v; Buf[Pos++] = (byte)(v >> 8); }
        public void I32(int v)
        {
            if (!Room(4)) return;
            Buf[Pos++] = (byte)v; Buf[Pos++] = (byte)(v >> 8); Buf[Pos++] = (byte)(v >> 16); Buf[Pos++] = (byte)(v >> 24);
        }
        public void U64(ulong v) { I32(unchecked((int)v)); I32(unchecked((int)(v >> 32))); }
        public void Bytes(byte[] src, int off, int n) { if (!Room(n)) return; Buffer.BlockCopy(src, off, Buf, Pos, n); Pos += n; }

        /// <summary>A short string: one length byte, then printable ASCII only (anything else becomes '?'), at most max chars.</summary>
        public void Str(string s, int max)
        {
            s = s ?? "";
            int n = Math.Min(Math.Min(s.Length, max), 255);
            U8(n);
            for (int i = 0; i < n; i++)
            {
                char c = s[i];
                U8(c >= 32 && c < 127 ? c : '?');
            }
        }
    }

    /// <summary>
    /// Bounds-checked little-endian reader. A read past the end never throws: it returns 0 and sets Bad, and every handler
    /// drops a packet whose reader went Bad. Malformed or hostile bytes therefore never crash the game.
    /// </summary>
    public struct WireReader
    {
        public byte[] Buf;
        public int Pos;
        public int End;
        public bool Bad;

        public WireReader(byte[] buf, int pos, int end) { Buf = buf; Pos = pos; End = Math.Min(end, buf != null ? buf.Length : 0); Bad = buf == null; }

        public int Left => Bad ? 0 : End - Pos;

        private bool Has(int n)
        {
            if (!Bad && Pos + n <= End) return true;
            Bad = true;
            return false;
        }

        public int U8() => Has(1) ? Buf[Pos++] : 0;
        public int I16()
        {
            if (!Has(2)) return 0;
            int v = (short)(Buf[Pos] | (Buf[Pos + 1] << 8));
            Pos += 2;
            return v;
        }
        public int I32()
        {
            if (!Has(4)) return 0;
            int v = Buf[Pos] | (Buf[Pos + 1] << 8) | (Buf[Pos + 2] << 16) | (Buf[Pos + 3] << 24);
            Pos += 4;
            return v;
        }
        public ulong U64()
        {
            uint lo = unchecked((uint)I32());
            uint hi = unchecked((uint)I32());
            return ((ulong)hi << 32) | lo;
        }
        public bool Skip(int n) { if (!Has(n)) return false; Pos += n; return true; }

        public string Str(int max)
        {
            int n = U8();
            if (n > max || !Has(n)) { Bad = true; return ""; }
            var cs = new char[n];
            for (int i = 0; i < n; i++)
            {
                int c = Buf[Pos + i];
                cs[i] = c >= 32 && c < 127 ? (char)c : '?';
            }
            Pos += n;
            return new string(cs);
        }
    }
}
