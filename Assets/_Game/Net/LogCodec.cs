using System;

namespace CriticalMass.Net
{
    /// <summary>
    /// b15-rejoin: the compression of the match log a rejoining player replays (LockstepSession, MsgLog). Each bundle is
    /// XORed with the bundle before it in the same packet (the first with zeros), so a seat whose input did not change, a
    /// bot seat and a quiet tick become zeros; then zeros are run-length coded (0x00, run 1..255) and every other byte is
    /// itself. Runs stop at each tick, so the encoder can stop at any tick that would not fit. Self-contained per packet
    /// (a lost or resent packet needs no other), deterministic, no allocation, and Decode refuses anything malformed.
    /// </summary>
    public static class LogCodec
    {
        /// <summary>
        /// Encodes up to `ticks` bundles of `bsz` bytes from src[0..] into dst[off..off+room), as many whole ticks as fit.
        /// Returns the bytes written; `count` = ticks encoded.
        /// </summary>
        public static int Encode(byte[] src, int ticks, int bsz, byte[] dst, int off, int room, out int count)
        {
            int pos = off, end = off + room;
            count = 0;
            for (int t = 0; t < ticks; t++)
            {
                int start = pos;
                int b = t * bsz;
                bool fits = true;
                for (int i = 0; i < bsz && fits;)
                {
                    int x = src[b + i] ^ (t > 0 ? src[b + i - bsz] : 0);
                    if (x != 0)
                    {
                        if (pos >= end) { fits = false; break; }
                        dst[pos++] = (byte)x;
                        i++;
                        continue;
                    }
                    int run = 0;
                    while (i < bsz && run < 255 && (src[b + i] ^ (t > 0 ? src[b + i - bsz] : 0)) == 0) { run++; i++; }
                    if (pos + 2 > end) { fits = false; break; }
                    dst[pos++] = 0;
                    dst[pos++] = (byte)run;
                }
                if (!fits) { pos = start; break; }
                count++;
            }
            return pos - off;
        }

        /// <summary>Decodes len bytes at src[off..] into exactly raw bytes of dst (whole bundles of bsz). False if malformed.</summary>
        public static bool Decode(byte[] src, int off, int len, byte[] dst, int raw, int bsz)
        {
            if (bsz <= 0 || raw % bsz != 0 || raw > dst.Length || off + len > src.Length) return false;
            int p = off, end = off + len, o = 0;
            while (p < end && o < raw)
            {
                byte b = src[p++];
                if (b != 0) { dst[o++] = b; continue; }
                if (p >= end) return false;
                int run = src[p++];
                if (run == 0 || o + run > raw) return false;
                for (int k = 0; k < run; k++) dst[o++] = 0;
            }
            if (p != end || o != raw) return false;
            for (int i = bsz; i < raw; i++) dst[i] ^= dst[i - bsz];
            return true;
        }
    }
}
