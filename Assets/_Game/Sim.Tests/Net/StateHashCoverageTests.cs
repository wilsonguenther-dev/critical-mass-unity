using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using CriticalMass.Net;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests.Net
{
    /// <summary>
    /// b15-rejoin, desync coverage. The state fingerprint (Net/StateHash) must cover ALL sim state, so a divergence in any
    /// system (hut, ore, dodge, Kaiju type, walls, bots, anything a later item adds) is caught at the next check instead of
    /// only once it has spread into HP or positions. Three proofs:
    /// (1) GENERATOR: the per-type walk in Net/StateHash.Generated.cs is generated here from the Sim's types (every instance
    ///     field reachable from World and from the world's BotsState, nested objects, arrays and structs); the test fails
    ///     when the checked-in file is not exactly what the types give today, i.e. when someone added a sim field and did not
    ///     regenerate. To regenerate: run this test, copy Temp/ci/StateHash.Generated.cs over Assets/_Game/Net/.
    /// (2) MUTATION: in a live 16v16 world, every reachable leaf (first and last element of every array) is changed one at a
    ///     time and the fingerprint must change each time, then come back exactly when the value is restored.
    /// (3) COST: the fingerprint's time and allocation per call, printed with its limit.
    /// The only state left out is in Excluded, each with its reason: derived caches rebuilt from hashed state, the view's
    /// event ring, and the match's fixed inputs (checked once at the start instead).
    /// </summary>
    public sealed class StateHashCoverageTests
    {
        /// <summary>Declaring type name + "." + member: why the fingerprint leaves it out.</summary>
        public static readonly Dictionary<string, string> Excluded = new Dictionary<string, string>
        {
            ["World.Config"] = "the rules: fixed for the match, checked once at the start by ConfigHash (online START, replay header)",
            ["World.Opts"] = "the match options: fixed for the match, checked once at the start through MatchSetup and the tick 0 fingerprint",
            ["World.Hash"] = "spatial index: rebuilt from the hashed positions every step",
            ["World.Events"] = "the view's event ring: written by rules, read by no rule (EventHead is hashed)",
            ["KaijuNav.Clear"] = "the Kaiju's shadow grid: rebuilt from the hashed cells whenever ClearGridVersion (hashed) moves",
            ["KaijuNav.Region"] = "clearance regions: rebuilt with the shadow grid",
            ["KaijuNav.RegionQueue"] = "flood-fill scratch for Region",
            ["KaijuNav.Squeeze"] = "squeeze cells: rebuilt with the shadow grid",
            ["FlowField.Dist"] = "a distance field: rebuilt from the hashed grid; its Version is hashed",
            ["BotFields.Dist"] = "the bots' flow fields: rebuilt from the hashed grid; Version per field is hashed",
            ["BotsState.LastTick"] = "bookkeeping of the last GetBotsState call (the view's ShiverWatch calls it between ticks too)",
        };

        // ------------------------------------------------------------------ the generator

        /// <summary>The member name code would use: the field, or the public property behind an auto-property's backing field.</summary>
        internal static string MemberName(FieldInfo f, out bool accessible)
        {
            string n = f.Name;
            if (n.StartsWith("<", StringComparison.Ordinal) && n.Contains(">k__BackingField"))
            {
                string p = n.Substring(1, n.IndexOf('>') - 1);
                PropertyInfo pi = f.DeclaringType.GetProperty(p, BindingFlags.Instance | BindingFlags.Public);
                accessible = pi != null && pi.GetGetMethod() != null;
                return p;
            }
            accessible = f.IsPublic;
            return n;
        }

        internal static List<FieldInfo> Fields(Type t)
        {
            var list = new List<FieldInfo>();
            for (Type x = t; x != null && x != typeof(object) && x != typeof(ValueType); x = x.BaseType)
                list.AddRange(x.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
            list.Sort((a, b) => string.CompareOrdinal(MemberName(a, out _), MemberName(b, out _)));
            return list;
        }

        internal static string Key(FieldInfo f) => f.DeclaringType.Name + "." + MemberName(f, out _);

        static bool Leaf(Type t) => t.IsPrimitive || t.IsEnum || t == typeof(string) || Nullable.GetUnderlyingType(t) != null;

        static bool HasArrayHelper(Type e) =>
            e == typeof(double) || e == typeof(float) || e == typeof(int) || e == typeof(uint) || e == typeof(long) || e == typeof(ulong) ||
            e == typeof(short) || e == typeof(ushort) || e == typeof(byte) || e == typeof(sbyte) || e == typeof(bool) || e == typeof(char);

        internal static bool IsList(Type t) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>);

        internal sealed class Gen
        {
            readonly StringBuilder _fns = new StringBuilder();
            readonly Queue<Type> _todo = new Queue<Type>();
            readonly HashSet<Type> _seen = new HashSet<Type>();
            readonly Dictionary<string, Type> _names = new Dictionary<string, Type>();
            public readonly List<string> Problems = new List<string>();
            public readonly HashSet<string> UsedExclusions = new HashSet<string>();
            public int Members;
            int _var;

            static string Fn(Type t) => "H_" + t.Name;
            static string TypeName(Type t) => "global::" + t.FullName.Replace('+', '.');

            void Want(Type t) { if (_seen.Add(t)) _todo.Enqueue(t); }

            public string Run(params Type[] roots)
            {
                foreach (Type r in roots) Want(r);
                while (_todo.Count > 0)
                {
                    Type t = _todo.Dequeue();
                    if (_names.TryGetValue(t.Name, out Type other) && other != t) Problems.Add($"two state types share the name {t.Name}: {other.FullName} and {t.FullName}");
                    _names[t.Name] = t;
                    Class(t);
                }
                var sb = new StringBuilder();
                sb.Append("// <auto-generated>\n");
                sb.Append("// GENERATED by CriticalMass.Sim.Tests.Net.StateHashCoverageTests from the Sim's types. Do not edit by hand:\n");
                sb.Append("// run the sim tests and copy Temp/ci/StateHash.Generated.cs over this file. Every instance field reachable from\n");
                sb.Append("// World and BotsState is mixed, in name order, except (each with its reason):\n");
                var keys = new List<string>(Excluded.Keys);
                keys.Sort(string.CompareOrdinal);
                foreach (string k in keys) sb.Append("//   ").Append(k).Append(": ").Append(Excluded[k]).Append('\n');
                sb.Append("// </auto-generated>\n");
                sb.Append("namespace CriticalMass.Net\n{\n    public static partial class StateHash\n    {\n");
                sb.Append(_fns);
                sb.Append("    }\n}\n");
                return sb.ToString();
            }

            void Line(int ind, string s) { _fns.Append(' ', 4 * ind).Append(s).Append('\n'); }

            void Class(Type t)
            {
                Line(2, $"private static void {Fn(t)}(ref ulong h, {TypeName(t)} o)");
                Line(2, "{");
                Line(3, "if (o == null) { M(ref h, 0x4E554C4CL); return; }");
                foreach (FieldInfo f in Fields(t)) Member(f, "o", 3);
                Line(2, "}");
                _fns.Append('\n');
            }

            void Member(FieldInfo f, string obj, int ind)
            {
                string m = MemberName(f, out bool ok);
                string key = Key(f);
                if (Excluded.ContainsKey(key)) { UsedExclusions.Add(key); Line(ind, $"// {m}: not hashed (see the header)"); return; }
                if (f.FieldType == typeof(World)) { Line(ind, $"// {m}: a reference back to the world"); return; }
                if (!ok) { Problems.Add($"{key}: not public (make it public, or exclude it with a reason)"); return; }
                Members++;
                Value(f.FieldType, obj + "." + m, ind, key);
            }

            void Value(Type ft, string e, int ind, string key)
            {
                Type u = Nullable.GetUnderlyingType(ft);
                if (ft == typeof(bool)) Line(ind, $"M(ref h, {e} ? 1 : 2);");
                else if (ft.IsEnum) Line(ind, $"M(ref h, (long){e});");
                else if (ft == typeof(double) || ft == typeof(float)) Line(ind, $"D(ref h, {e});");
                else if (ft == typeof(ulong)) Line(ind, $"M(ref h, unchecked((long){e}));");
                else if (ft.IsPrimitive) Line(ind, $"M(ref h, {e});");
                else if (ft == typeof(string)) Line(ind, $"S(ref h, {e});");
                else if (u != null)
                {
                    Line(ind, $"if ({e}.HasValue)");
                    Line(ind, "{");
                    Line(ind + 1, "M(ref h, 1);");
                    Value(u, e + ".Value", ind + 1, key);
                    Line(ind, "}");
                    Line(ind, "else M(ref h, 0);");
                }
                else if (ft.IsArray || IsList(ft))
                {
                    if (ft.IsArray && ft.GetArrayRank() != 1) { Problems.Add($"{key}: a multi-dimensional array"); return; }
                    Type el = ft.IsArray ? ft.GetElementType() : ft.GetGenericArguments()[0];
                    if (ft.IsArray && HasArrayHelper(el)) { Line(ind, $"A(ref h, {e});"); return; }
                    string a = "a" + _var, i = "i" + _var;
                    _var++;
                    string len = ft.IsArray ? "Length" : "Count";
                    Line(ind, "{");
                    Line(ind + 1, $"var {a} = {e};");
                    Line(ind + 1, $"if ({a} == null) M(ref h, -1);");
                    Line(ind + 1, "else");
                    Line(ind + 1, "{");
                    Line(ind + 2, $"M(ref h, {a}.{len});");
                    Line(ind + 2, $"for (int {i} = 0; {i} < {a}.{len}; {i}++)");
                    Line(ind + 2, "{");
                    Value(el, $"{a}[{i}]", ind + 3, key + "[]");
                    Line(ind + 2, "}");
                    Line(ind + 1, "}");
                    Line(ind, "}");
                }
                else if (ft.IsValueType)
                {
                    foreach (FieldInfo sf in Fields(ft))
                    {
                        string m = MemberName(sf, out bool ok);
                        string k = Key(sf);
                        if (Excluded.ContainsKey(k)) { UsedExclusions.Add(k); continue; }
                        if (!ok) { Problems.Add($"{k} (in {key}): not public"); continue; }
                        Value(sf.FieldType, e + "." + m, ind, k);
                    }
                }
                else if (ft.IsClass && !ft.IsAbstract && !ft.IsGenericType && ft != typeof(object) && !typeof(Delegate).IsAssignableFrom(ft) && !typeof(IDictionary).IsAssignableFrom(ft))
                {
                    if (!ft.IsVisible) { Problems.Add($"{key}: type {ft.FullName} is not public"); return; }
                    if (!ft.IsSealed) Problems.Add($"{key}: type {ft.FullName} is not sealed (a subclass's fields would be missed)");
                    Want(ft);
                    Line(ind, $"{Fn(ft)}(ref h, {e});");
                }
                else Problems.Add($"{key}: cannot fingerprint a {ft.FullName}");
            }
        }

        static string RepoRoot()
        {
            string d = Directory.GetCurrentDirectory();
            for (int i = 0; i < 8 && d != null; i++)
            {
                if (Directory.Exists(Path.Combine(d, "Assets", "_Game", "Net"))) return d;
                d = Path.GetDirectoryName(d);
            }
            return Directory.GetCurrentDirectory();
        }

        [Test]
        public void TheFingerprintIsGeneratedFromEverySimFieldAndTheCheckedInCopyIsCurrent()
        {
            var g = new Gen();
            string text = g.Run(typeof(World), typeof(BotsState));
            string root = RepoRoot();
            string outDir = Path.Combine(root, "Temp", "ci");
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "StateHash.Generated.cs"), text);
            var unused = new List<string>();
            foreach (string k in Excluded.Keys) if (!g.UsedExclusions.Contains(k)) unused.Add(k);
            string path = Path.Combine(root, "Assets", "_Game", "Net", "StateHash.Generated.cs");
            string have = File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n") : "";
            bool current = have == text;
            Console.WriteLine($"[state hash] generated walk: {g.Members} members hashed, {g.UsedExclusions.Count} excluded with a reason, " +
                              $"{g.Problems.Count} problems, {unused.Count} unused exclusions; checked-in copy {(current ? "CURRENT" : "STALE")} " +
                              $"(written to Temp/ci/StateHash.Generated.cs; limit: 0 problems, 0 unused, current)");
            foreach (string p in g.Problems) Console.WriteLine("[state hash] problem: " + p);
            Assert.IsEmpty(g.Problems, "sim state the fingerprint cannot cover:\n" + string.Join("\n", g.Problems));
            Assert.IsEmpty(unused, "exclusions that match no sim field (renamed? remove them):\n" + string.Join("\n", unused));
            Assert.IsTrue(current, "Assets/_Game/Net/StateHash.Generated.cs is not what the sim's types give today (a sim field was added, " +
                                   "renamed or removed). Copy Temp/ci/StateHash.Generated.cs over it.");
        }

        // ------------------------------------------------------------------ the mutation proof

        sealed class Loc
        {
            public string Path;
            public Type Type;
            public Func<object> Get;
            public Action<object> Set;
        }

        sealed class RefEq : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object o) => RuntimeHelpers.GetHashCode(o);
        }

        sealed class Walker
        {
            public readonly List<Loc> Leaves = new List<Loc>();
            public readonly List<string> Nulls = new List<string>();
            public readonly List<string> Empty = new List<string>();
            readonly HashSet<object> _seen = new HashSet<object>(new RefEq());

            public void Object(object o, string path)
            {
                if (o == null || !_seen.Add(o)) return;
                foreach (FieldInfo f in Fields(o.GetType()))
                {
                    if (Excluded.ContainsKey(Key(f)) || f.FieldType == typeof(World)) continue;
                    FieldInfo fi = f;
                    object holder = o;
                    Visit(new Loc { Path = path + "." + MemberName(f, out _), Type = f.FieldType, Get = () => fi.GetValue(holder), Set = v => fi.SetValue(holder, v) });
                }
            }

            void Visit(Loc loc)
            {
                Type t = loc.Type;
                if (Leaf(t)) { Leaves.Add(loc); return; }
                object v = loc.Get();
                if (v == null) { Nulls.Add(loc.Path); return; }
                if (t.IsArray || IsList(t))
                {
                    var list = (IList)v;
                    if (list.Count == 0) { Empty.Add(loc.Path); return; }
                    Type el = t.IsArray ? t.GetElementType() : t.GetGenericArguments()[0];
                    int last = list.Count - 1;
                    foreach (int ix in last == 0 ? new[] { 0 } : new[] { 0, last })
                    {
                        int at = ix;
                        Visit(new Loc { Path = loc.Path + "[" + at + "]", Type = el, Get = () => list[at], Set = x => list[at] = x });
                    }
                    return;
                }
                if (t.IsValueType)
                {
                    foreach (FieldInfo sf in Fields(t))
                    {
                        if (Excluded.ContainsKey(Key(sf))) continue;
                        FieldInfo s = sf;
                        Loc outer = loc;
                        Visit(new Loc
                        {
                            Path = loc.Path + "." + MemberName(sf, out _), Type = sf.FieldType,
                            Get = () => s.GetValue(outer.Get()),
                            Set = x => { object box = outer.Get(); s.SetValue(box, x); outer.Set(box); },
                        });
                    }
                    return;
                }
                Object(v, loc.Path);
            }
        }

        static object Mutate(object v, Type t)
        {
            Type u = Nullable.GetUnderlyingType(t);
            if (u != null) return v == null ? Activator.CreateInstance(u) : null;
            if (t == typeof(string)) return (string)v + "x";
            if (t == typeof(bool)) return !(bool)v;
            if (t == typeof(double)) return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits((double)v) ^ 1L);
            if (t == typeof(float)) return BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits((float)v) ^ 1);
            if (t.IsEnum) return Enum.ToObject(t, Convert.ToInt64(v) ^ 1L);
            if (t == typeof(int)) return (int)v ^ 1;
            if (t == typeof(uint)) return (uint)v ^ 1u;
            if (t == typeof(long)) return (long)v ^ 1L;
            if (t == typeof(ulong)) return (ulong)v ^ 1UL;
            if (t == typeof(short)) return (short)((short)v ^ 1);
            if (t == typeof(ushort)) return (ushort)((ushort)v ^ 1);
            if (t == typeof(byte)) return (byte)((byte)v ^ 1);
            if (t == typeof(sbyte)) return (sbyte)((sbyte)v ^ 1);
            if (t == typeof(char)) return (char)((char)v ^ 1);
            throw new ArgumentException("no mutation for " + t);
        }

        /// <summary>A default 16v16 world after `ticks` of bot play (so the bot state exists and every system has run).</summary>
        internal static World Played(int ticks, int seed = 4242, int team = 16)
        {
            World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = team, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateDefault() });
            InputFrame scratch = SimInput.EmptyInput();
            for (int t = 0; t < ticks && w.Match.Phase == MatchPhase.Playing; t++) MatchTick.Advance(w, null, scratch);
            return w;
        }

        [Test]
        public void ChangingAnySingleSimFieldChangesTheFingerprint()
        {
            World w = Played(2400);
            Assert.IsTrue(BotBrain.TryGetBotsState(w, out BotsState bots), "bots have thought");
            var walk = new Walker();
            walk.Object(w, "World");
            walk.Object(bots, "BotsState");
            ulong h0 = StateHash.Of(w);
            var missed = new List<string>();
            var notRestored = new List<string>();
            foreach (Loc l in walk.Leaves)
            {
                object old = l.Get();
                l.Set(Mutate(old, l.Type));
                ulong h1 = StateHash.Of(w);
                l.Set(old);
                ulong h2 = StateHash.Of(w);
                if (h1 == h0) missed.Add(l.Path);
                if (h2 != h0) notRestored.Add(l.Path);
            }
            Console.WriteLine($"[state hash] mutation: {walk.Leaves.Count} leaves changed one at a time, {missed.Count} left the fingerprint unchanged, " +
                              $"{notRestored.Count} not restored; {walk.Nulls.Count} null objects and {walk.Empty.Count} empty arrays had nothing to change " +
                              $"(limit: 0 unchanged, 0 not restored)");
            if (walk.Empty.Count > 0) Console.WriteLine("[state hash] empty arrays (nothing to mutate): " + string.Join(", ", walk.Empty));
            if (walk.Nulls.Count > 0) Console.WriteLine("[state hash] null objects (nothing to mutate): " + string.Join(", ", walk.Nulls));
            Assert.IsEmpty(missed, "fields the fingerprint does not see:\n" + string.Join("\n", missed.GetRange(0, Math.Min(80, missed.Count))));
            Assert.IsEmpty(notRestored, "the fingerprint did not come back:\n" + string.Join("\n", notRestored));
            Assert.Greater(walk.Leaves.Count, 500, "the walk reached the sim state");
        }

        [Test]
        public void TheB13StateTheOldFingerprintMissedIsNowCovered()
        {
            // The audit's list (2026-11-17, section 5): hut level, ore, stock, dodge, Kaiju type, power-ups, coins.
            World w = Played(2400);
            ulong h0 = StateHash.Of(w);
            void Expect(string what, Action change, Action undo)
            {
                change();
                ulong h = StateHash.Of(w);
                undo();
                Console.WriteLine($"[state hash] {what}: fingerprint {(h != h0 ? "CHANGES" : "UNCHANGED")} (limit: changes)");
                Assert.AreNotEqual(h0, h, what);
                Assert.AreEqual(h0, StateHash.Of(w), what + " restored");
            }
            Expect("hut level", () => w.Hut.Level[0]++, () => w.Hut.Level[0]--);
            Expect("ore carried (hut haul)", () => w.Hut.HaulLeft[0]++, () => w.Hut.HaulLeft[0]--);
            Expect("team stock", () => w.Depth.Stock[0]++, () => w.Depth.Stock[0]--);
            Expect("dodge ready tick", () => w.Dodges.Ready[0]++, () => w.Dodges.Ready[0]--);
            Expect("Kaiju type", () => w.KaijuKind.Type = (KaijuType)((int)w.KaijuKind.Type ^ 1), () => w.KaijuKind.Type = (KaijuType)((int)w.KaijuKind.Type ^ 1));
            Expect("power-up kind", () => w.Depth.PowerKind[0] ^= 1, () => w.Depth.PowerKind[0] ^= 1);
            Expect("coins picked", () => w.Actors[3].CoinsPicked++, () => w.Actors[3].CoinsPicked--);
            Expect("bot role", () => BotBrain.GetBotsState(w).Role[5] ^= 1, () => BotBrain.GetBotsState(w).Role[5] ^= 1);
        }

        [Test]
        public void TheFingerprintCostsLittleAndAllocatesNothing()
        {
            World w = Played(1800);
            for (int i = 0; i < 20; i++) StateHash.Of(w);
            const int N = 200;
            long before = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            ulong x = 0;
            for (int i = 0; i < N; i++) x ^= StateHash.Of(w);
            sw.Stop();
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            double us = sw.Elapsed.TotalMilliseconds * 1000.0 / N;
            Console.WriteLine($"[state hash] cost: {us:F1} us per fingerprint (16v16, once every 30 ticks = {us / 30:F2} us per tick), {bytes} B allocated over {N} calls (limit: 0 B, under 2000 us) {x:X4}");
            Assert.AreEqual(0, bytes, "the fingerprint must not allocate");
            Assert.Less(us, 2000.0, "the fingerprint is run every second on every machine");
        }
    }
}
