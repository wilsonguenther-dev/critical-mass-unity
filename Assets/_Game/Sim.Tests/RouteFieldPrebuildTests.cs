using System;
using CriticalMass.Net;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b15 item 5 (systems audit W6 / defect 16): the base sim allocated one 64,568 B FlowField (double[112x72] + object) the
    /// first time each bot route was used (Sources.EnsureField, Blacksmith.PadField, PingRules route slots), which is the
    /// 129,136 / 193,704 / 258,272 B windows the cost tests printed. RouteFields.Preallocate gives every such route its
    /// buffer at CreateWorld. These tests prove (1) every buffer exists at creation, (2) pathing allocates no field after
    /// creation over a whole 16v16 match, (3) the whole-tick allocation over the audit's 300-tick window before and after,
    /// and (4) the lazy and preallocated paths give bit-identical matches on every rules profile.
    /// </summary>
    [TestFixture]
    public sealed class RouteFieldPrebuildTests
    {
        const int FieldBytes = 64568;

        static World Make(int seed, int team, GameConfig cfg = null) =>
            World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = team, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg ?? GameConfig.CreateDefault() });

        static T WithLazy<T>(bool lazy, Func<T> f)
        {
            bool prev = RouteFields.Lazy;
            RouteFields.Lazy = lazy;
            try { return f(); }
            finally { RouteFields.Lazy = prev; }
        }

        [Test]
        public void EveryRouteBufferExistsWhenTheWorldIsCreated()
        {
            World w = Make(4242, 16);
            Assert.Greater(RouteFields.LastPreallocated, 0, "CreateWorld preallocated the route buffers");
            int n = w.Map.Width * w.Map.Height;
            foreach (SourceNode s in w.Depth.Sources)
            {
                Assert.IsNotNull(s.Field, "source " + s.Id + " (" + s.Kind + ") has its route buffer");
                Assert.AreEqual(n, s.Field.Dist.Length);
                Assert.IsFalse(s.FieldBuilt, "the buffer exists but is not computed yet (first use still builds it, as before)");
            }
            for (int t = 0; t < 2; t++)
            {
                Assert.IsNotNull(w.Hut.PadField[t], "hut pad route " + t);
                Assert.IsFalse(w.Hut.PadFieldBuilt[t]);
            }
            for (int i = 0; i < w.Depth.PingFields.Length; i++) Assert.IsNotNull(w.Depth.PingFields[i], "ping route slot " + i);
            Console.WriteLine($"[b15 route prebuild] 16v16 seed 4242 Full: {RouteFields.LastPreallocated} route buffers at CreateWorld ({RouteFields.LastPreallocated * FieldBytes} B, once): {w.Depth.Sources.Length} sources, 2 hut pads, {w.Depth.PingFields.Length} ping slots");
        }

        /// <summary>One whole match: hashes every 600 ticks and the FlowField buffers allocated after CreateWorld.</summary>
        static ulong[] PlayWhole(int seed, int team, bool lazy, out int fieldsAfterCreate, out int ticks)
        {
            int made = 0, played = 0;
            ulong[] h = WithLazy(lazy, () =>
            {
                World w = Make(seed, team);
                int a0 = FlowField.Allocations;
                var scratch = SimInput.EmptyInput();
                var hs = new ulong[25];
                int k = 0, t = 0;
                for (; t < 14400 && w.Match.Phase == MatchPhase.Playing; t++)
                {
                    MatchTick.Advance(w, null, scratch);
                    if (t % 600 == 599 && k < hs.Length) hs[k++] = StateHash.Of(w) ^ MatchHash.Of(w);
                }
                if (k < hs.Length) hs[k] = StateHash.Of(w) ^ MatchHash.Of(w);
                made = FlowField.Allocations - a0;
                played = t;
                return hs;
            });
            fieldsAfterCreate = made;
            ticks = played;
            return h;
        }

        [TestCase(4242, 16)]
        [TestCase(1337, 16)]
        [TestCase(42, 5)]
        public void PathingAllocatesNoFieldAfterCreationAndTheMatchIsIdentical(int seed, int team)
        {
            ulong[] lazy = PlayWhole(seed, team, true, out int lazyFields, out int lazyTicks);
            ulong[] pre = PlayWhole(seed, team, false, out int preFields, out int preTicks);
            int diff = -1;
            for (int i = 0; i < pre.Length && diff < 0; i++) if (pre[i] != lazy[i]) diff = i;
            Console.WriteLine($"[b15 route prebuild] seed {seed} {team}v{team} whole match ({preTicks} ticks): FlowField buffers allocated after CreateWorld: lazy (before) {lazyFields} = {lazyFields * FieldBytes} B, preallocated (after) {preFields} = {preFields * FieldBytes} B; hashes every 600 ticks {(diff < 0 ? "IDENTICAL" : "DIFFER at sample " + diff)}; end hash {pre[pre.Length - 1]:X16} (limit: identical, after = 0)");
            Assert.AreEqual(lazyTicks, preTicks, "same match length");
            Assert.AreEqual(-1, diff, "preallocating the route buffers must not change the match");
            Assert.AreEqual(0, preFields, "after warm-up (CreateWorld) pathing allocates no FlowField for the whole match");
        }

        /// <summary>The audit's window: 16v16 seed 4242 Full, 600 warm-up ticks, then 300 ticks, whole-tick bytes before and after.</summary>
        [Test]
        public void WholeTickAllocationOverThe300TickWindowBeforeAndAfter()
        {
            long Window(bool lazy, out int fields)
            {
                int made = 0;
                long bytes = WithLazy(lazy, () =>
                {
                    World w = Make(4242, 16);
                    var scratch = SimInput.EmptyInput();
                    for (int t = 0; t < 600; t++) MatchTick.Advance(w, null, scratch);
                    int a0 = FlowField.Allocations;
                    long b = 0;
                    for (int t = 0; t < 300; t++)
                    {
                        long b0 = GC.GetAllocatedBytesForCurrentThread();
                        MatchTick.Advance(w, null, scratch);
                        b += GC.GetAllocatedBytesForCurrentThread() - b0;
                    }
                    made = FlowField.Allocations - a0;
                    return b;
                });
                fields = made;
                return bytes;
            }

            long before = Window(true, out int beforeFields);
            long after = Window(false, out int afterFields);
            // and over the first 900 ticks from creation (where most first uses land), per tick, from pathing only
            int Early(bool lazy) => WithLazy(lazy, () =>
            {
                World w = Make(4242, 16);
                var scratch = SimInput.EmptyInput();
                int a0 = FlowField.Allocations;
                for (int t = 0; t < 900; t++) MatchTick.Advance(w, null, scratch);
                return FlowField.Allocations - a0;
            });
            int earlyBefore = Early(true), earlyAfter = Early(false);
            Console.WriteLine($"[b15 route prebuild] 300 whole ticks 16v16 seed 4242 Full after 600 warm-up: before (lazy) {before} B ({beforeFields} fields), after (preallocated) {after} B ({afterFields} fields); first 900 ticks from creation, pathing fields: before {earlyBefore} ({earlyBefore * FieldBytes} B), after {earlyAfter} (limit: after fields 0, after B <= before B)");
            Assert.AreEqual(0, afterFields, "no route field allocated in the window");
            Assert.AreEqual(0, earlyAfter, "no route field allocated in the first 900 ticks");
            Assert.LessOrEqual(after, before, "the preallocated path never allocates more per tick");
        }

        [Test]
        public void LazyAndPreallocatedGiveTheSameGoldenRowsOnEveryProfile()
        {
            foreach (RulesProfile p in new[] { RulesProfile.Classic, RulesProfile.PhysicsOnly, RulesProfile.Full })
            {
                Action<GameConfig> t = c => c.ApplyRules(p);
                ulong a0 = WithLazy(true, () => MatchHash.Run(1, 16, 3600, ClassId.Warrior, null, t));
                ulong a1 = WithLazy(false, () => MatchHash.Run(1, 16, 3600, ClassId.Warrior, null, t));
                ulong b0 = WithLazy(true, () => MatchHash.Run(42, 5, 14500, ClassId.Warrior, null, t));
                ulong b1 = WithLazy(false, () => MatchHash.Run(42, 5, 14500, ClassId.Warrior, null, t));
                Console.WriteLine($"[b15 route prebuild] {p}: 16v16 seed 1 2:00 lazy 0x{a0:X16} pre 0x{a1:X16}; 5v5 seed 42 whole lazy 0x{b0:X16} pre 0x{b1:X16}");
                Assert.AreEqual(a0, a1, p + " 16v16");
                Assert.AreEqual(b0, b1, p + " 5v5");
            }
        }
    }
}
