using System;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Core;

namespace CriticalMass.Net
{
    /// <summary>
    /// The fingerprint two machines compare to prove they hold the same world. b15-rejoin: it covers ALL sim state, not a
    /// hand-picked list: every instance field reachable from the World and from the world's bot state (BotBrain), nested
    /// objects, arrays and structs included, except the few derived caches and constants listed (with the reason) in
    /// StateHashCoverageTests.Excluded. The per-type walk is GENERATED (StateHash.Generated.cs) by
    /// StateHashCoverageTests from the Sim's types, and that test fails the moment a new sim field is neither hashed nor
    /// excluded with a reason, so a new field cannot be forgotten. A second test mutates every reachable field of a live
    /// world one at a time and proves the fingerprint changes each time.
    /// <para>Mixing is word-wise FNV (h = (h ^ v) * prime, odd prime): every step is a bijection, so changing any one value
    /// always changes the result. Doubles by their raw bits. Read-only, no allocation, run once every HashEvery ticks.</para>
    /// </summary>
    public static partial class StateHash
    {
        private const ulong Offset = 14695981039346656037UL, Prime = 1099511628211UL;

        public static ulong Of(World w)
        {
            ulong h = Offset;
            H_World(ref h, w);
            if (w != null && BotBrain.TryGetBotsState(w, out BotsState st)) { M(ref h, 1); H_BotsState(ref h, st); }
            else M(ref h, 0);
            return h;
        }

        private static void M(ref ulong h, long v) { unchecked { h = (h ^ (ulong)v) * Prime; } }
        private static void D(ref ulong h, double d) => M(ref h, BitConverter.DoubleToInt64Bits(d));

        private static void S(ref ulong h, string s)
        {
            if (s == null) { M(ref h, -1); return; }
            M(ref h, s.Length);
            for (int i = 0; i < s.Length; i++) M(ref h, s[i]);
        }

        private static void A(ref ulong h, double[] a) { if (a == null) { M(ref h, -1); return; } M(ref h, a.Length); for (int i = 0; i < a.Length; i++) M(ref h, BitConverter.DoubleToInt64Bits(a[i])); }
        private static void A(ref ulong h, float[] a) { if (a == null) { M(ref h, -1); return; } M(ref h, a.Length); for (int i = 0; i < a.Length; i++) M(ref h, BitConverter.DoubleToInt64Bits(a[i])); }
        private static void A(ref ulong h, int[] a) { if (a == null) { M(ref h, -1); return; } M(ref h, a.Length); for (int i = 0; i < a.Length; i++) M(ref h, a[i]); }
        private static void A(ref ulong h, uint[] a) { if (a == null) { M(ref h, -1); return; } M(ref h, a.Length); for (int i = 0; i < a.Length; i++) M(ref h, a[i]); }
        private static void A(ref ulong h, long[] a) { if (a == null) { M(ref h, -1); return; } M(ref h, a.Length); for (int i = 0; i < a.Length; i++) M(ref h, a[i]); }
        private static void A(ref ulong h, ulong[] a) { if (a == null) { M(ref h, -1); return; } M(ref h, a.Length); for (int i = 0; i < a.Length; i++) M(ref h, unchecked((long)a[i])); }
        private static void A(ref ulong h, short[] a) { if (a == null) { M(ref h, -1); return; } M(ref h, a.Length); for (int i = 0; i < a.Length; i++) M(ref h, a[i]); }
        private static void A(ref ulong h, ushort[] a) { if (a == null) { M(ref h, -1); return; } M(ref h, a.Length); for (int i = 0; i < a.Length; i++) M(ref h, a[i]); }
        private static void A(ref ulong h, byte[] a) { if (a == null) { M(ref h, -1); return; } M(ref h, a.Length); for (int i = 0; i < a.Length; i++) M(ref h, a[i]); }
        private static void A(ref ulong h, sbyte[] a) { if (a == null) { M(ref h, -1); return; } M(ref h, a.Length); for (int i = 0; i < a.Length; i++) M(ref h, a[i]); }
        private static void A(ref ulong h, bool[] a) { if (a == null) { M(ref h, -1); return; } M(ref h, a.Length); for (int i = 0; i < a.Length; i++) M(ref h, a[i] ? 1 : 2); }
        private static void A(ref ulong h, char[] a) { if (a == null) { M(ref h, -1); return; } M(ref h, a.Length); for (int i = 0; i < a.Length; i++) M(ref h, a[i]); }
    }
}
