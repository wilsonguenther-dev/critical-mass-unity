using System;
using System.Diagnostics;
using CriticalMass.Net;
using CriticalMass.Net.Replay;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests.Net
{
    /// <summary>
    /// b13-replay: record a headless seeded match through MatchTick.Tap, play it back from the recorded start conditions,
    /// and prove it is the same match (every fingerprint and the final checksum); the file round trip, size, version
    /// refusal, damaged files, recording on/off golden parity, and an allocation-free per-tick recording path. Every
    /// test prints one plain line per measured case: what, the value, the limit.
    /// </summary>
    public sealed class ReplayTests
    {
        [TearDown]
        public void ClearTap() => MatchTick.Tap = null;

        // ---------------------------------------------------------------- helpers

        static ReplayStart Start(int seed, int teamSize, int rules = (int)RulesProfile.Full, int[] extra = null) => new ReplayStart
        {
            Kind = extra != null ? ReplayKind.Online : ReplayKind.Offline,
            Basis = ConfigBasis.Default,
            RulesProfileId = rules,
            Seed = seed,
            TeamSize = teamSize,
            HumanTeam = TeamId.Azure,
            HumanClass = ClassId.Warrior,
            Pace = 1,
            ExtraHumans = extra ?? new int[0],
            ExtraHumanClasses = extra != null ? new ClassId[extra.Length] : new ClassId[0],
        };

        static ReplayHeader Header(ReplayStart s) => new ReplayHeader
        {
            BuildVersion = "1.0.test",
            BuildCommit = "abc1234",
            MapName = "SKYBREAK BASIN",
            ModeLabel = "CUSTOM",
            Start = s,
        };

        /// <summary>A keyboard-and-mouse style human: 8-way move, a continuous aim point near the fighter, presses.</summary>
        static void Scripted(World w, int actor, int t, InputFrame f, bool quantise)
        {
            int phase = (t / 45 + actor * 3) % 9;
            double d = 0.70710678118654757;
            double[] mx = { 0, 1, d, 0, -d, -1, -d, 0, d };
            double[] mz = { 0, 0, d, 1, d, 0, -d, -1, -d };
            f.MoveX = mx[phase];
            f.MoveZ = mz[phase];
            f.AimX = w.X[actor] + 4.0 * Math.Cos(t * 0.031 + actor);
            f.AimZ = w.Z[actor] + 4.0 * Math.Sin(t * 0.027 + actor);
            f.Attack = (t + actor) % 17 < 4;
            f.Ability = (t + actor * 7) % 211 == 0;
            f.Interact = (t % 300) < 2;
            f.Terraform = false;
            f.Jump = (t + actor) % 97 == 0;
            f.TerraformSwitch = false;
            f.ClassPick = -1;
            f.StationBuy = 0;
            f.PingKind = (t + actor * 11) % 400 == 0 ? 1 : 0;
            f.PingX = f.PingKind != 0 ? w.X[actor] + 6 : 0;
            f.PingZ = f.PingKind != 0 ? w.Z[actor] : 0;
            if (!quantise) return;
            var buf = new byte[InputCodec.Size];   // test only: an online frame is the decoded wire frame
            InputCodec.Encode(f, buf, 0);
            InputCodec.Decode(buf, 0, f);
        }

        /// <summary>Records `ticks` ticks of a single-player match (actor 0 scripted, autopilot between 900 and 1200).</summary>
        static ReplayData RecordOffline(int seed, int teamSize, int ticks, out ulong liveFinal, int rules = (int)RulesProfile.Full)
        {
            ReplayStart s = Start(seed, teamSize, rules);
            World w = World.CreateWorld(s.ToWorldOptions(s.BuildConfig()));
            var rec = new ReplayRecorder();
            Assert.IsTrue(rec.Begin(w, Header(s)), rec.Refused);
            MatchTick.Tap = rec;
            InputFrame human = SimInput.EmptyInput(), scratch = SimInput.EmptyInput();
            for (int t = 0; t < ticks && w.Match.Phase != MatchPhase.Ended; t++)
            {
                bool autopilot = t >= 900 && t < 1200;
                Scripted(w, 0, t, human, false);
                MatchTick.Advance(w, autopilot ? null : human, scratch);
            }
            MatchTick.Tap = null;
            liveFinal = StateHash.Of(w);
            return rec.Finish(w.Match.Phase == MatchPhase.Ended, 638000000000000000L);
        }

        /// <summary>Records an online-style room: actor 0 plus extra humans with wire-quantised frames; one leaves at handTick.</summary>
        static ReplayData RecordOnline(int seed, int teamSize, int[] extra, int ticks, int handTick, out ulong liveFinal, out int[] liveFp)
        {
            ReplayStart s = Start(seed, teamSize, (int)RulesProfile.Full, extra);
            World w = World.CreateWorld(s.ToWorldOptions(s.BuildConfig()));
            var rec = new ReplayRecorder();
            Assert.IsTrue(rec.Begin(w, Header(s)), rec.Refused);
            MatchTick.Tap = rec;
            var frames = new InputFrame[w.ActorCount];
            var own = new InputFrame[w.ActorCount];
            for (int i = 0; i < own.Length; i++) own[i] = SimInput.EmptyInput();
            InputFrame scratch = SimInput.EmptyInput();
            bool left = false;
            for (int t = 0; t < ticks && w.Match.Phase != MatchPhase.Ended; t++)
            {
                for (int i = 0; i < frames.Length; i++) frames[i] = null;
                Scripted(w, 0, t, own[0], true);
                frames[0] = own[0];
                for (int k = 0; k < extra.Length; k++)
                {
                    int a = extra[k];
                    if (k == 0 && left) continue;
                    Scripted(w, a, t, own[a], true);
                    frames[a] = own[a];
                }
                if (t == handTick && extra.Length > 0)
                {
                    MatchTick.HandToBot(w, extra[0]);
                    left = true;
                    frames[extra[0]] = null;
                }
                MatchTick.AdvanceHumans(w, frames, scratch);
            }
            MatchTick.Tap = null;
            liveFinal = StateHash.Of(w);
            ReplayData d = rec.Finish(false, 638000000000000000L);
            liveFp = d.FingerprintTicks;
            return d;
        }

        // ---------------------------------------------------------------- record -> playback

        [Test]
        public void PlaybackOfAHeadlessSeededMatchMatchesEveryFingerprintAndTheFinalChecksum()
        {
            ReplayData d = RecordOffline(1, 16, 3600, out ulong live);
            var p = new ReplayPlayer(d);
            p.RunToEnd();
            Console.WriteLine($"[replay] offline 16v16 seed 1 2:00: ticks {p.TicksPlayed}/{d.Header.Ticks}, fingerprints checked {p.FingerprintsChecked}/{d.FingerprintCount}, " +
                              $"final live 0x{live:X16} replay 0x{StateHash.Of(p.World):X16}, desync tick {p.DesyncTick} (limit: none)");
            Assert.IsTrue(p.ConfigMatches, "rebuilt config hash equals the recorded one");
            Assert.IsFalse(p.Desynced, "desynced at tick " + p.DesyncTick);
            Assert.AreEqual(d.Header.Ticks, p.TicksPlayed);
            Assert.AreEqual(d.FingerprintCount, p.FingerprintsChecked, "every fingerprint was checked");
            Assert.IsTrue(p.FinalMatches);
            Assert.AreEqual(live, StateHash.Of(p.World), "final checksum");
            Assert.AreEqual(d.Header.FinalHash, live);
        }

        [Test]
        public void PlaybackOfAnOnlineStyleRoomWithAHandOverMatchesEveryFingerprint()
        {
            int[] extra = { 1, 2, 5, 16, 17 };
            ReplayData d = RecordOnline(7, 16, extra, 2400, 1000, out ulong live, out _);
            var p = new ReplayPlayer(d);
            p.RunToEnd();
            Console.WriteLine($"[replay] online-style 6 humans 16v16 seed 7 1:20, hand-over at tick 1000: ticks {p.TicksPlayed}, fingerprints {p.FingerprintsChecked}/{d.FingerprintCount}, " +
                              $"final live 0x{live:X16} replay 0x{StateHash.Of(p.World):X16}, actor 1 human after: {p.World.Actors[1].IsHuman} (limit: identical)");
            Assert.IsFalse(p.Desynced, "desynced at tick " + p.DesyncTick);
            Assert.AreEqual(d.FingerprintCount, p.FingerprintsChecked);
            Assert.AreEqual(live, StateHash.Of(p.World));
            Assert.IsFalse(p.World.Actors[1].IsHuman, "the hand-over replayed");
        }

        // ---------------------------------------------------------------- file

        [Test]
        public void FileRoundTripGivesIdenticalInputsAndHeader()
        {
            ReplayData d = RecordOffline(3, 5, 1800, out _);
            d.Header.Roster[0].Name = "WILSON ÉTÉ";
            foreach (bool compress in new[] { true, false })
            {
                byte[] file = ReplayFile.Write(d, compress);
                Assert.AreEqual(ReplayReadStatus.Ok, ReplayFile.TryRead(file, out ReplayData r, out string why), why);
                Assert.AreEqual(ReplayReadStatus.Ok, ReplayFile.TryReadHeader(file, file.Length, out ReplayHeader h, out why), why);
                Console.WriteLine($"[replay] round trip compress={compress}: file {file.Length} B, inputs {d.InputLength} B -> {r.InputLength} B, " +
                                  $"fingerprints {d.FingerprintCount} -> {r.FingerprintCount} (limit: identical)");
                Assert.AreEqual(d.InputLength, r.InputLength);
                for (int i = 0; i < d.InputLength; i++) Assert.AreEqual(d.Inputs[i], r.Inputs[i], "input byte " + i);
                CollectionAssert.AreEqual(d.FingerprintTicks, r.FingerprintTicks);
                CollectionAssert.AreEqual(d.Fingerprints, r.Fingerprints);
                foreach (ReplayHeader x in new[] { r.Header, h })
                {
                    Assert.AreEqual(d.Header.Ticks, x.Ticks);
                    Assert.AreEqual(d.Header.ConfigHash, x.ConfigHash);
                    Assert.AreEqual(d.Header.FinalHash, x.FinalHash);
                    Assert.AreEqual(d.Header.RecordedUtcTicks, x.RecordedUtcTicks);
                    Assert.AreEqual(d.Header.Start.Seed, x.Start.Seed);
                    Assert.AreEqual(d.Header.Start.RulesProfileId, x.Start.RulesProfileId);
                    Assert.AreEqual(d.Header.Roster.Length, x.Roster.Length);
                    Assert.AreEqual("WILSON ÉTÉ", x.Roster[0].Name);
                    Assert.AreEqual("1.0.test", x.BuildVersion);
                }
                var p = new ReplayPlayer(r);
                p.RunToEnd();
                Assert.IsTrue(p.FinalMatches && !p.Desynced, "the read file plays the same match");
            }
        }

        [Test]
        public void SizeOfA16v16MatchIsWellUnderOneMegabyteForTenMinutes()
        {
            // Measured over 2:00 (3600 ticks at 30 Hz) and scaled to 10:00 (x5): single player (1 human, 31 bots, raw
            // doubles) and the worst online case this test builds (16 humans with wire frames, everyone moving all match).
            ReplayData solo = RecordOffline(1, 16, 3600, out _);
            int[] extra = new int[15];
            for (int i = 0; i < 15; i++) extra[i] = i + 1;
            ReplayData room = RecordOnline(1, 16, extra, 3600, -1, out _, out _);
            const int Limit = 1024 * 1024;
            foreach ((string name, ReplayData d) in new[] { ("1 human + 31 bots", solo), ("16 humans + 16 bots", room) })
            {
                int file = ReplayFile.Write(d, true).Length, stored = ReplayFile.Write(d, false).Length;
                Console.WriteLine($"[replay] size 16v16 {name}: 2:00 measured inputs {d.InputLength} B, file {file} B (uncompressed {stored} B); " +
                                  $"10:00 estimate file {file * 5L} B (limit {Limit} B)");
                Assert.Less(file * 5L, Limit, name);
            }
        }

        // ---------------------------------------------------------------- refusal and damage

        [Test]
        public void AReplayFromAnotherVersionIsRefusedWithAPlainMessage()
        {
            ReplayData d = RecordOffline(5, 5, 300, out _);
            ReplayCompatibility same = ReplayCompat.Check(d.Header, "1.0.test", "abc1234");
            ReplayCompatibility otherBuild = ReplayCompat.Check(d.Header, "1.0.999", "abc1234");
            ReplayCompatibility otherCommit = ReplayCompat.Check(d.Header, "1.0.test", "def5678");
            d.Header.FormatVersion = ReplayFile.FormatVersion + 1;
            ReplayCompatibility newer = ReplayCompat.Check(d.Header, "1.0.test", "abc1234");
            d.Header.FormatVersion = ReplayFile.FormatVersion;
            string msg = ReplayCompat.Message(otherBuild, d.Header);
            Console.WriteLine($"[replay] version check: same {same}, other build {otherBuild}, other commit {otherCommit}, newer format {newer}; message \"{msg}\" (limit: Ok, OtherVersion, OtherVersion, NewerFormat)");
            Assert.AreEqual(ReplayCompatibility.Ok, same);
            Assert.AreEqual(ReplayCompatibility.OtherVersion, otherBuild);
            Assert.AreEqual(ReplayCompatibility.OtherVersion, otherCommit);
            Assert.AreEqual(ReplayCompatibility.NewerFormat, newer);
            StringAssert.Contains(ReplayCompat.OtherVersionText, msg);

            // A file whose rules differ (ConfigHash) is refused by the player itself: no step runs, no exception.
            d.Header.ConfigHash ^= 0x5A5AUL;
            var p = new ReplayPlayer(d);
            bool stepped = p.Step();
            Console.WriteLine($"[replay] other rules: config matches {p.ConfigMatches}, stepped {stepped}, desynced {p.Desynced} (limit: False, False, True)");
            Assert.IsFalse(p.ConfigMatches);
            Assert.IsFalse(stepped);
            Assert.IsTrue(p.Desynced);
        }

        [Test]
        public void ATruncatedOrDamagedFileIsRefusedWithoutAnException()
        {
            ReplayData d = RecordOffline(9, 5, 600, out _);
            byte[] file = ReplayFile.Write(d, true);
            int truncOk = 0, flipOk = 0, flips = 0;
            for (int len = 0; len < file.Length; len += Math.Max(1, file.Length / 300))
            {
                var cut = new byte[len];
                Array.Copy(file, cut, len);
                ReplayReadStatus st = ReplayFile.TryRead(cut, out _, out _);
                ReplayFile.TryReadHeader(cut, cut.Length, out _, out _);
                if (st == ReplayReadStatus.Ok) truncOk++;
            }
            var rng = new Random(1234);
            for (int i = 0; i < 400; i++)
            {
                var bad = (byte[])file.Clone();
                bad[rng.Next(bad.Length)] ^= (byte)(1 << rng.Next(8));
                flips++;
                if (ReplayFile.TryRead(bad, out ReplayData r, out _) == ReplayReadStatus.Ok)
                {
                    flipOk++;
                    new ReplayPlayer(r).RunToEnd();   // must not throw either
                }
            }
            Assert.AreEqual(ReplayReadStatus.NotAReplay, ReplayFile.TryRead(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, out _, out _));
            Assert.AreEqual(ReplayReadStatus.NotAReplay, ReplayFile.TryRead(null, out _, out _));
            // A damaged input stream handed straight to the player (no file checksum in the way): it stops, never throws.
            var mangled = new ReplayData { Header = d.Header, FingerprintCount = d.FingerprintCount, FingerprintTicks = d.FingerprintTicks,
                Fingerprints = d.Fingerprints, Inputs = (byte[])d.Inputs.Clone(), InputLength = d.InputLength / 2 };
            for (int i = 0; i < mangled.InputLength; i += 7) mangled.Inputs[i] = 0xFF;
            var p = new ReplayPlayer(mangled);
            Assert.DoesNotThrow(() => p.RunToEnd());
            Console.WriteLine($"[replay] damage: truncations read Ok {truncOk} (limit 0), bit flips read Ok {flipOk}/{flips} (limit 0), " +
                              $"mangled stream stopped at tick {p.TicksPlayed}/{d.Header.Ticks}, broken {p.StreamBroken}, desynced {p.Desynced} (limit: stops)");
            Assert.AreEqual(0, truncOk);
            Assert.AreEqual(0, flipOk);
            Assert.IsTrue(p.StreamBroken || p.Desynced || p.TicksPlayed < d.Header.Ticks);
        }

        // ---------------------------------------------------------------- the sim is untouched

        /// <summary>A tap that records whichever world it first sees at tick 0 (MatchHash.Run builds its own world).</summary>
        sealed class FirstWorldTap : IMatchInputTap
        {
            public readonly ReplayRecorder Rec = new ReplayRecorder();
            void Bind(World w) { if (Rec.World == null && !Rec.Done && w.Tick == 0) Rec.Begin(w, new ReplayHeader()); }
            public void OnAdvance(World w, InputFrame human) { Bind(w); Rec.OnAdvance(w, human); }
            public void OnAdvanceHumans(World w, InputFrame[] frames) { Bind(w); Rec.OnAdvanceHumans(w, frames); }
            public void OnAdvanceNoHuman(World w) { Bind(w); Rec.OnAdvanceNoHuman(w); }
            public void OnHandToBot(World w, int id) { Bind(w); Rec.OnHandToBot(w, id); }
        }

        [Test]
        public void RecordingOnOrOffGivesTheSameGoldenHashes()
        {
            foreach (RulesProfile prof in new[] { RulesProfile.Full, RulesProfile.PhysicsOnly, RulesProfile.Classic })
            {
                Action<GameConfig> t = c => c.ApplyRules(prof);
                MatchTick.Tap = null;
                ulong off = MatchHash.Run(1, 16, 3600, ClassId.Warrior, null, t);
                var tap = new FirstWorldTap();
                MatchTick.Tap = tap;
                ulong on = MatchHash.Run(1, 16, 3600, ClassId.Warrior, null, t);
                MatchTick.Tap = null;
                Console.WriteLine($"[replay] golden {prof} 16v16 seed 1 2:00: recording off 0x{off:X16}, on 0x{on:X16}, ticks recorded {tap.Rec.Ticks} (limit: identical)");
                Assert.AreEqual(off, on, prof + ": recording changed the match");
                Assert.Greater(tap.Rec.Ticks, 0, "the tap really recorded");
            }
        }

        // ---------------------------------------------------------------- cost

        [Test]
        public void PerTickRecordingAllocatesNothingAndCostsMicroseconds()
        {
            ReplayStart s = Start(11, 16);
            World w = World.CreateWorld(s.ToWorldOptions(s.BuildConfig()));
            var rec = new ReplayRecorder();
            Assert.IsTrue(rec.Begin(w, Header(s)));
            InputFrame human = SimInput.EmptyInput(), scratch = SimInput.EmptyInput();
            var frames = new InputFrame[w.ActorCount];
            const int Ticks = 1200;
            long allocated = 0, ticksElapsed = 0, fpTicks = 0;
            for (int t = 0; t < Ticks; t++)
            {
                Scripted(w, 0, t, human, false);
                frames[0] = human;
                bool fp = w.Tick % rec.Header.HashEvery == 0;
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                long c0 = Stopwatch.GetTimestamp();
                if ((t & 1) == 0) rec.OnAdvance(w, human);
                else rec.OnAdvanceHumans(w, frames);
                long c1 = Stopwatch.GetTimestamp();
                long a1 = GC.GetAllocatedBytesForCurrentThread();
                if (t >= 10) allocated += a1 - a0;   // the first calls may JIT
                if (!fp) ticksElapsed += c1 - c0; else fpTicks++;
                MatchTick.Advance(w, human, scratch);   // the sim itself is outside the measured window
            }
            double usPerTick = ticksElapsed * 1e6 / Stopwatch.Frequency / Math.Max(1, Ticks - fpTicks);
            Console.WriteLine($"[replay] per-tick recording: allocated {allocated} B over {Ticks - 10} ticks (limit 0 B); mean {usPerTick:0.00} us per tick without a fingerprint " +
                              $"(limit 50 us); {fpTicks} fingerprint ticks; buffer {rec.InputBytes} B");
            Assert.AreEqual(0, allocated, "the per-tick recording path allocated");
            Assert.Less(usPerTick, 50.0);
        }
    }
}
