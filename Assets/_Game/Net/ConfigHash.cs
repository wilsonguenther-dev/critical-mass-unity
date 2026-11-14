using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CriticalMass.Sim.Config;

namespace CriticalMass.Net
{
    /// <summary>
    /// online-rooms-3: one 64-bit fingerprint of everything in a GameConfig the Sim can read (every public and private
    /// instance field, nested objects and arrays included, in name order so Mono and .NET agree), skipping the view-only
    /// Render block. Two machines that will play one match must get the same value; the match start message carries the
    /// host's, and a client that differs refuses the match with a plain reason instead of drifting apart at tick 30.
    /// FNV-1a over raw bits (doubles by their bit pattern, strings by UTF-16 units). Reflection, so only called at match
    /// start, never per tick.
    /// </summary>
    public static class ConfigHash
    {
        private const ulong Offset = 14695981039346656037UL, Prime = 1099511628211UL;

        public static ulong Of(GameConfig cfg)
        {
            ulong h = Offset;
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            Mix(ref h, cfg, 0, seen);
            return h;
        }

        private static void Byte(ref ulong h, byte b) { h ^= b; h *= Prime; }

        private static void U64(ref ulong h, ulong v)
        {
            for (int i = 0; i < 8; i++) { Byte(ref h, (byte)v); v >>= 8; }
        }

        private static void Str(ref ulong h, string s)
        {
            if (s == null) { U64(ref h, 0xFFFFFFFFUL); return; }
            U64(ref h, (ulong)s.Length);
            foreach (char c in s) { Byte(ref h, (byte)c); Byte(ref h, (byte)(c >> 8)); }
        }

        private static void Mix(ref ulong h, object o, int depth, HashSet<object> seen)
        {
            if (o == null) { Byte(ref h, 0); return; }
            Type t = o.GetType();
            switch (o)
            {
                case double d: U64(ref h, (ulong)BitConverter.DoubleToInt64Bits(d)); return;
                case float f: U64(ref h, (ulong)BitConverter.DoubleToInt64Bits(f)); return;
                case bool b: Byte(ref h, b ? (byte)1 : (byte)2); return;
                case string s: Str(ref h, s); return;
                case char c: U64(ref h, c); return;
            }
            if (t.IsEnum) { U64(ref h, Convert.ToUInt64(Convert.ToInt64(o))); return; }
            if (t.IsPrimitive) { U64(ref h, unchecked((ulong)Convert.ToInt64(o))); return; }
            if (depth > 12) return;
            if (!t.IsValueType && !seen.Add(o)) { Byte(ref h, 7); return; }
            if (o is IDictionary) return;   // none in GameConfig today; an unordered map cannot be hashed in a stable order
            if (o is IEnumerable list)
            {
                ulong n = 0;
                foreach (object e in list) { Mix(ref h, e, depth + 1, seen); n++; }
                U64(ref h, n);
                return;
            }
            FieldInfo[] fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Array.Sort(fields, (a, b) => string.CompareOrdinal(a.Name, b.Name));
            foreach (FieldInfo f in fields)
            {
                if (f.FieldType == typeof(RenderConfig)) continue;   // the camera angle and other view settings differ per machine on purpose
                if (f.FieldType.IsPointer || typeof(Delegate).IsAssignableFrom(f.FieldType)) continue;
                Str(ref h, f.Name);
                Mix(ref h, f.GetValue(o), depth + 1, seen);
            }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }
    }
}
