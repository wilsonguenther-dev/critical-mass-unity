using System;
using System.Reflection;
using CriticalMass.Net;
using CriticalMass.Net.Replay;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests.PlayerSeat
{
    /// <summary>
    /// Class moves (2026-11-22), lane 1: the contract every other lane builds on (cm-incidents/class-moves-audit-2026-11-22/
    /// LANE1-CONTRACT.md). The switch-off proof here is the one every lane runs before pushing: with DepthConfig.Moves present
    /// and every switch false, the Full goldens are the batch candidate's bit for bit. Category PlayerSeat (fast suite).
    /// </summary>
    [TestFixture, Category("PlayerSeat")]
    public sealed class MovesContractTests
    {
        // The Full goldens of integration/player-experience-cand1 (SimPhysicsSoakTests.Golden row Full).
        const ulong Full16v16Seed1 = 0x2873C138B069629AUL, Full5v5Seed42 = 0x98AFF3A3316ED55FUL;

        static FieldInfo[] Switches() =>
            Array.FindAll(typeof(MovesConfig).GetFields(BindingFlags.Instance | BindingFlags.Public), f => f.FieldType == typeof(bool));

        [Test]
        public void EverySwitchIsOffByDefaultAndThePlayBuildTurnsEveryOneOn()
        {
            var m = new MovesConfig();
            FieldInfo[] sw = Switches();
            Console.WriteLine($"[mv] class-moves switches: {sw.Length} (limit: 14, every one false by default)");
            Assert.AreEqual(14, sw.Length, "T1-1..T1-5 (six switches, T1-4 has two) and T2-1..T2-8 (eight)");
            foreach (FieldInfo f in sw) Assert.IsFalse((bool)f.GetValue(m), f.Name + " must be off by default");
            Assert.IsFalse(m.AnyOn);
            Assert.AreEqual(0, m.Mask());
            Assert.AreSame(m, MovesConfig.PlayBuildAllOn(m));
            foreach (FieldInfo f in sw) Assert.IsTrue((bool)f.GetValue(m), f.Name + " must be on in the play build");
            Assert.IsTrue(m.AnyOn);
            Assert.AreEqual((1 << 14) - 1, m.Mask(), "every switch has its own mask bit");
            var back = new MovesConfig().SetMask(m.Mask());
            foreach (FieldInfo f in sw) Assert.IsTrue((bool)f.GetValue(back), f.Name + " survives the mask");
            MovesConfig.AllOff(m);
            Assert.IsFalse(m.AnyOn);
            for (int bit = 0; bit < 14; bit++)
            {
                var one = new MovesConfig().SetMask(1 << bit);
                Assert.AreEqual(1 << bit, one.Mask(), "bit " + bit);
                Assert.IsTrue(one.AnyOn, "bit " + bit + " counts in AnyOn");
            }
            Assert.IsNotNull(GameConfig.CreateDefault().Depth.Moves, "DepthConfig.Moves is always present");
            Assert.AreNotSame(GameConfig.CreateDefault().Depth.Moves, GameConfig.CreateDefault().Clone().Depth.Moves);
        }

        [Test]
        public void TheWorldHasClassMovesStateOnlyWithTheFullRulesAndASwitchOn()
        {
            World off = World.CreateWorld(new WorldOptions { Seed = 42, TeamSize = 5, Config = GameConfig.CreateDefault() });
            Assert.IsNull(off.Moves, "every switch off: nothing allocated");
            GameConfig on = GameConfig.CreateDefault();
            on.Depth.Moves.Guard = true;
            World w = World.CreateWorld(new WorldOptions { Seed = 42, TeamSize = 5, Config = on });
            Assert.IsNotNull(w.Moves, "a switch on with the Full rules: allocated");
            Assert.IsTrue(MovesHooks.On(w, MovesHooks.Cfg(w).Guard));
            Assert.IsFalse(MovesHooks.On(w, MovesHooks.Cfg(w).Heavy));
            foreach (RulesProfile p in new[] { RulesProfile.Classic, RulesProfile.PhysicsOnly })
            {
                GameConfig c = GameConfig.CreateDefault().ApplyRules(p);
                MovesConfig.PlayBuildAllOn(c.Depth.Moves);
                Assert.IsNull(World.CreateWorld(new WorldOptions { Seed = 42, TeamSize = 5, Config = c }).Moves, p + ": the moves are Full rules only");
            }
        }

        /// <summary>THE switch-off proof (SPEC.md section 4): every lane runs it before pushing.</summary>
        [Test]
        public void WithEverySwitchOffTheFullGoldensAreTheBatchCandidates()
        {
            Action<GameConfig> full = c =>
            {
                c.ApplyRules(RulesProfile.Full);
                MovesConfig.AllOff(c.Depth.Moves);
            };
            ulong b = MatchHash.Run(42, 5, 14500, ClassId.Warrior, null, full);
            ulong a = MatchHash.Run(1, 16, 3600, ClassId.Warrior, null, full);
            Console.WriteLine($"[mv] switches off, Full: 16v16 seed 1 0x{a:X16} (pin 0x{Full16v16Seed1:X16}), 5v5 seed 42 0x{b:X16} (pin 0x{Full5v5Seed42:X16})");
            Assert.AreEqual(Full5v5Seed42, b, "Full 5v5 seed 42 with every class-moves switch off");
            Assert.AreEqual(Full16v16Seed1, a, "Full 16v16 seed 1 with every class-moves switch off");
        }

        /// <summary>The FullMoves golden (SPEC.md rule 0.2): Full rules plus every switch on. Pinned by the combine once, after the
        /// 72-seed balance run (integration/class-moves-cand1): a moved pin means class-moves behaviour changed; re-pin only with
        /// the reason in the commit.</summary>
        public const ulong FullMoves16v16Seed1 = 0x7E2E255FC9CAACA4, FullMoves5v5Seed42 = 0xCDECE0EF9E4D1902;   // round 3 2026-11-23 (fix/mv-round3-defence): every LUNGE re-decided at the swing + rooted unless the stick points at the target, person swing snap, bot step-out cap (Easy, Normal first 60 s); was 0x995EA6937985F040/0x490669BBFA2488E7

        [Test]
        public void TheFullMovesGoldenIsReported()
        {
            Action<GameConfig> moves = c =>
            {
                c.ApplyRules(RulesProfile.Full);
                MovesConfig.PlayBuildAllOn(c.Depth.Moves);
            };
            ulong a = MatchHash.Run(1, 16, 3600, ClassId.Warrior, null, moves);
            ulong b = MatchHash.Run(42, 5, 14500, ClassId.Warrior, null, moves);
            Console.WriteLine("{\"item\":\"rules-combined golden\",\"profile\":\"FullMoves\",\"16v16seed1\":\"0x" + a.ToString("X16") +
                              "\",\"5v5seed42\":\"0x" + b.ToString("X16") + "\",\"pinned\":true}");
            Assert.AreEqual(a, MatchHash.Run(1, 16, 3600, ClassId.Warrior, null, moves), "FullMoves: same seed, same hash");
            Assert.AreEqual(FullMoves16v16Seed1, a, "FullMoves 16v16 seed 1 (every class-moves switch on)");
            Assert.AreEqual(FullMoves5v5Seed42, b, "FullMoves 5v5 seed 42 (every class-moves switch on)");
        }

        [Test]
        public void TheConfigHashSeesTheMovesBlockOnlyWhileASwitchIsOn()
        {
            ulong plain = ConfigHash.Of(GameConfig.CreateDefault());
            GameConfig knob = GameConfig.CreateDefault();
            knob.Depth.Moves.TravelSpeedMul = 1.75;   // integrator: 1.5 is the default since decision 5
            Assert.AreEqual(plain, ConfigHash.Of(knob), "every switch off: the block is not mixed (old replays and peers still match)");
            GameConfig on = GameConfig.CreateDefault();
            on.Depth.Moves.Travel = true;
            ulong h1 = ConfigHash.Of(on);
            Assert.AreNotEqual(plain, h1, "a switch on is seen");
            on.Depth.Moves.TravelSpeedMul = 1.75;
            Assert.AreNotEqual(h1, ConfigHash.Of(on), "with a switch on, every knob is seen");
        }

        [Test]
        public void GuardAndJumpHeldSurviveTheWireAndTheReplayStream()
        {
            var buf = new byte[InputCodec.Size];
            foreach (bool g in new[] { false, true })
            foreach (bool j in new[] { false, true })
            foreach (bool c in new[] { false, true })
            foreach (int pk in new[] { 0, 1, 4 })
            {
                InputFrame f = SimInput.EmptyInput();
                f.Guard = g; f.JumpHeld = j; f.Cancel = c; f.PingKind = pk; f.PingX = 3.5; f.PingZ = -2.25; f.Jump = j;
                InputCodec.Encode(f, buf, 0);
                InputFrame d = SimInput.EmptyInput();
                InputCodec.Decode(buf, 0, d);
                string tag = $"guard {g} jumpHeld {j} cancel {c} ping {pk}";
                Assert.AreEqual(g, d.Guard, tag);
                Assert.AreEqual(j, d.JumpHeld, tag);
                Assert.AreEqual(c, d.Cancel, tag);
                Assert.AreEqual(pk, d.PingKind, tag);

                var w = new ReplayBytes(64);
                var lane = new ReplayInputStream.Lane();
                int changes = ReplayInputStream.Changes(lane.Last, f);
                if (g || j || pk != 0) Assert.AreNotEqual(0, changes & ReplayInputStream.CPicks, tag + ": the picks group carries the change");
                ReplayInputStream.PutFrame(ref w, lane, f, changes);
                var r = new ReplayReader(w.Buffer, 0, w.Length);
                var back = new ReplayInputStream.Lane();
                ReplayInputStream.GetFrame(ref r, back);
                Assert.AreEqual(g, back.Last.Guard, tag + " (replay)");
                Assert.AreEqual(j, back.Last.JumpHeld, tag + " (replay)");
                Assert.AreEqual(pk, back.Last.PingKind, tag + " (replay)");
            }
            InputFrame neutral = SimInput.EmptyInput();
            InputCodec.EncodeNeutral(buf, 0);
            InputCodec.Decode(buf, 0, neutral);
            Assert.IsFalse(neutral.Guard || neutral.JumpHeld, "the neutral frame holds nothing");
        }

        [Test]
        public void TheReplayHeaderCarriesTheSwitchesAndPlaybackRebuildsThem()
        {
            Assert.AreEqual(2, ReplayFile.FormatVersion, "format 2: Guard, JumpHeld and the moves mask");
            GameConfig cfg = GameConfig.CreateDefault();
            MovesConfig.PlayBuildAllOn(cfg.Depth.Moves);
            var opts = new WorldOptions { Seed = 7, TeamSize = 5, Config = cfg };
            ReplayStart s = ReplayStart.From(opts, ReplayKind.Offline, ConfigBasis.Default, -1, 0, 0, 0, 0);
            Assert.AreEqual(cfg.Depth.Moves.Mask(), s.MovesMask);
            var d = new ReplayData();
            d.Header.Start = s;
            d.Header.Start.TeamSize = 5;
            byte[] file = ReplayFile.Write(d, false);
            Assert.AreEqual(ReplayReadStatus.Ok, ReplayFile.TryReadHeader(file, file.Length, out ReplayHeader h, out string why), why);
            Assert.AreEqual(s.MovesMask, h.Start.MovesMask);
            GameConfig rebuilt = h.Start.BuildConfig();
            Assert.AreEqual(ConfigHash.Of(cfg), ConfigHash.Of(rebuilt), "playback rebuilds the recorded switches");
            Assert.AreEqual(ConfigHash.Of(GameConfig.CreateDefault()), ConfigHash.Of(new ReplayStart { TeamSize = 5 }.BuildConfig()),
                "a recording with every switch off (or a format 1 file) rebuilds the plain config");
        }

        [Test]
        public void TheIntentsFollowTheButtonsAndBotsNeverInheritAHumanGuard()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            cfg.Depth.Moves.Guard = true;
            World w = World.CreateWorld(new WorldOptions { Seed = 42, TeamSize = 5, Config = cfg });
            MovesState s = w.Moves;
            InputFrame scratch = SimInput.EmptyInput();
            InputFrame me = SimInput.EmptyInput();
            // let actor 0 live through spawn first
            for (int t = 0; t < 5; t++) MatchTick.Advance(w, me, scratch);
            Assert.IsTrue(w.Actors[0].Alive, "the human seat is alive");
            me.Guard = true;
            me.Attack = true;
            MatchTick.Advance(w, me, scratch);
            Assert.AreEqual(1, s.GuardPressed[0], "the press tick is an edge");
            Assert.AreEqual(1, s.GuardHeldTicks[0]);
            Assert.AreEqual(w.Tick, s.LastGuardPressTick[0]);
            Assert.AreEqual(1, s.AttackHeldTicks[0]);
            MatchTick.Advance(w, me, scratch);
            Assert.AreEqual(0, s.GuardPressed[0], "a held guard is not a new press");
            Assert.AreEqual(2, s.GuardHeldTicks[0]);
            Assert.AreEqual(2, s.AttackHeldTicks[0]);
            me.Guard = false;
            me.Attack = false;
            MatchTick.Advance(w, me, scratch);
            Assert.AreEqual(0, s.GuardHeldTicks[0]);
            Assert.AreEqual(1, s.AttackReleased[0], "the release tick");
            Assert.AreEqual(2, s.AttackReleasedAfter[0], "held two ticks before the release");
            me.Guard = true;
            me.JumpHeld = true;
            MatchTick.Advance(w, me, scratch);
            Assert.AreEqual(1, s.GuardPressed[0], "released then pressed again: a new edge");
            Assert.AreEqual(1, s.JumpHeldTicks[0]);
            for (int id = 1; id < w.ActorCount; id++)
                Assert.IsFalse(w.Inputs[id].Guard || w.Inputs[id].JumpHeld, "bot " + id + " never inherits the human's guard or held jump");
        }
    }
}
