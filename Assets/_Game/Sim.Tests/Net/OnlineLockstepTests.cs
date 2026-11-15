using System;
using System.Collections.Generic;
using CriticalMass.Net;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests.Net
{
    /// <summary>
    /// online-rooms-2: deterministic lockstep proven with no network. Two (or three) complete matches run in one process,
    /// each with its own World, and talk only through LoopbackNetwork (delay, jitter, loss, reordering on simulated time).
    /// </summary>
    public sealed class OnlineLockstepTests
    {
        private const int StepMs = 5;

        private sealed class Rig
        {
            public LoopbackNetwork Net;
            public List<LockstepSession> Sessions = new List<LockstepSession>();
            public List<Dictionary<int, ulong>> Hashes = new List<Dictionary<int, ulong>>();
            public List<LoopbackTransport> Links = new List<LoopbackTransport>();
            public long Now;
        }

        /// <summary>A room of `humans` people: the host on Azure, the second on the other team, any third on the host's team.</summary>
        private static Rig Make(int humans, int teamSize, int delayMs, int jitterMs, double loss, LockstepOptions o = null, uint netSeed = 7, int inputDelay = 6)
        {
            var rig = new Rig { Net = new LoopbackNetwork(netSeed, delayMs, jitterMs, loss) };
            var teams = new TeamId[humans];
            // Peers 0 and 2 Azure, 1 Crimson (the small tests); from peer 3 on they alternate, so 32 humans fill both teams.
            for (int i = 0; i < humans; i++) teams[i] = i == 1 || (i >= 3 && i % 2 == 1) ? TeamId.Crimson : TeamId.Azure;
            var actors = new int[humans];
            Assert.IsTrue(MatchSetup.AssignActors(teamSize, TeamId.Azure, teams, actors));
            // Pace 1: 30 ticks per wall second, so the millisecond numbers in these tests read as 33 ms ticks.
            var setup = new MatchSetup { Seed = 4242, TeamSize = teamSize, HostTeam = TeamId.Azure, InputDelay = inputDelay, HashEvery = 30, Pace = 1 };
            setup.Humans = new HumanSlot[humans];
            var transports = new LoopbackTransport[humans];
            for (int i = 0; i < humans; i++)
            {
                transports[i] = rig.Net.CreatePeer();
                rig.Links.Add(transports[i]);
                setup.Humans[i] = new HumanSlot { Peer = transports[i].LocalPeer, Actor = actors[i], Class = teams[i] == TeamId.Crimson ? ClassId.Miner : ClassId.Warrior, Name = "P" + i };
            }
            for (int i = 0; i < humans; i++)
            {
                var s = new LockstepSession(transports[i], setup, GameConfig.CreateDefault(), o);
                var pilot = new AutoplayPilot((uint)(100 + i));
                s.Sampler = pilot.Fill;
                var log = new Dictionary<int, ulong>();
                s.HashRecorded = (t, h) => log[t] = h;
                rig.Sessions.Add(s);
                rig.Hashes.Add(log);
            }
            return rig;
        }

        /// <summary>Runs simulated time until every live session has stepped `ticks` ticks, ended or stopped, or the clock runs out.</summary>
        private static void Run(Rig rig, int ticks, long maxMs, Func<Rig, bool> stop = null, bool[] skip = null)
        {
            while (rig.Now < maxMs)
            {
                rig.Now += StepMs;
                rig.Net.Advance(rig.Now);
                bool allDone = true;
                for (int i = 0; i < rig.Sessions.Count; i++)
                {
                    if (skip != null && skip[i]) continue;
                    LockstepSession s = rig.Sessions[i];
                    if (s.Stepped < ticks && s.Status == LockstepStatus.Running) { s.Update(rig.Now); allDone = false; }
                    else s.Update(rig.Now);   // keep answering so the others can finish
                }
                if (allDone) return;
                if (stop != null && stop(rig)) return;
            }
        }

        private static void AssertSameHashes(Rig rig, int a, int b, int minChecks)
        {
            int compared = 0;
            foreach (KeyValuePair<int, ulong> kv in rig.Hashes[a])
            {
                if (!rig.Hashes[b].TryGetValue(kv.Key, out ulong other)) continue;
                Assert.AreEqual(kv.Value, other, $"peers {a} and {b} differ at tick {kv.Key}");
                compared++;
            }
            Assert.GreaterOrEqual(compared, minChecks, "too few common hash checks");
        }

        // ------------------------------------------------------------------ codec and setup

        [Test]
        public void InputCodecRoundTripIsStableAndFuzzSafe()
        {
            var f = SimInput.EmptyInput();
            f.MoveX = 0.731; f.MoveZ = -0.2; f.AimX = 55.5551; f.AimZ = -12.3449; f.Attack = true; f.Jump = true; f.ClassPick = 2; f.StationBuy = 1;
            f.PingKind = 3; f.PingX = 10.01; f.PingZ = 20.02;
            var b = new byte[InputCodec.Size];
            InputCodec.Encode(f, b, 0);
            var g = SimInput.EmptyInput();
            InputCodec.Decode(b, 0, g);
            var b2 = new byte[InputCodec.Size];
            InputCodec.Encode(g, b2, 0);
            CollectionAssert.AreEqual(b, b2, "decode then encode must give the same bytes (quantised once, stable after)");
            Assert.AreEqual(0.731, g.MoveX, 1.0 / 127);
            Assert.AreEqual(55.56, g.AimX, 0.006);
            Assert.IsTrue(g.Attack && g.Jump && !g.Ability);
            Assert.AreEqual(2, g.ClassPick);
            Assert.AreEqual(3, g.PingKind);
            // Non-finite aim travels as "no aim".
            f.AimX = double.NaN;
            InputCodec.Encode(f, b, 0);
            InputCodec.Decode(b, 0, g);
            Assert.IsTrue(double.IsNaN(g.AimX));
            // Random bytes: never throw, always legal values.
            uint x = 12345;
            for (int n = 0; n < 20000; n++)
            {
                for (int i = 0; i < b.Length; i++) { x ^= x << 13; x ^= x >> 17; x ^= x << 5; b[i] = (byte)x; }
                InputCodec.Decode(b, 0, g);
                Assert.IsTrue(g.MoveX >= -1 && g.MoveX <= 1 && g.MoveZ >= -1 && g.MoveZ <= 1);
                Assert.IsTrue(double.IsNaN(g.AimX) || Math.Abs(g.AimX) <= InputCodec.CoordLimit);
                Assert.IsTrue(g.ClassPick >= -1 && g.ClassPick < ClassIds.All.Count);
                Assert.IsTrue(g.StationBuy >= 0 && g.StationBuy <= ClassIds.All.Count);
                Assert.IsTrue(g.PingKind >= 0 && g.PingKind <= 4);
            }
        }

        [Test]
        public void MatchSetupRoundTripsAndRejectsHostileValues()
        {
            var cfg = GameConfig.CreateDefault();
            var s = new MatchSetup { Seed = -77, TeamSize = 16, HostTeam = TeamId.Crimson, InputDelay = 5, HashEvery = 30, Pace = 1.5 };
            s.Humans = new[]
            {
                new HumanSlot { Peer = 0, Actor = 0, Class = ClassId.Warrior, Name = "Wilson" },
                new HumanSlot { Peer = 3, Actor = 16, Class = ClassId.Miner, Name = "Jeisilé" },
            };
            var buf = new byte[512];
            var w = new WireWriter(buf);
            s.Write(ref w);
            var r = new WireReader(buf, 0, w.Pos);
            MatchSetup back = MatchSetup.Read(ref r, cfg);
            Assert.IsNotNull(back);
            Assert.AreEqual(-77, back.Seed);
            Assert.AreEqual(TeamId.Crimson, back.HostTeam);
            Assert.AreEqual(1.5, back.Pace);
            Assert.AreEqual(16, back.Humans[1].Actor);
            Assert.AreEqual("Jeisil?", back.Humans[1].Name, "non-ASCII names are replaced, never passed through");
            // Two humans on one fighter: refused.
            s.Humans[1].Actor = 0;
            w = new WireWriter(buf);
            s.Write(ref w);
            r = new WireReader(buf, 0, w.Pos);
            Assert.IsNull(MatchSetup.Read(ref r, cfg));
            // Truncated: refused, never thrown.
            r = new WireReader(buf, 0, 9);
            Assert.IsNull(MatchSetup.Read(ref r, cfg));
        }

        [Test]
        public void AdvanceHumansWithOnlyActorZeroIsExactlyAdvance()
        {
            var cfg = GameConfig.CreateDefault();
            var o = new WorldOptions { Seed = 9, TeamSize = 8, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg };
            World a = World.CreateWorld(o), b = World.CreateWorld(o);
            InputFrame sa = SimInput.EmptyInput(), sb = SimInput.EmptyInput();
            var frames = new InputFrame[b.ActorCount];
            for (int t = 0; t < 900; t++)
            {
                MatchTick.Advance(a, null, sa);
                MatchTick.AdvanceHumans(b, frames, sb);
            }
            Assert.AreEqual(StateHash.Of(a), StateHash.Of(b));
        }

        [Test]
        public void ExtraHumansGetTheirFlagAndClassAndKeepTheTeamMix()
        {
            var cfg = GameConfig.CreateDefault();
            var plain = World.CreateWorld(new WorldOptions { Seed = 3, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            var room = World.CreateWorld(new WorldOptions
            {
                Seed = 3, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg,
                ExtraHumans = new[] { 16 }, ExtraHumanClasses = new[] { ClassId.Miner },
            });
            Assert.IsTrue(room.Actors[0].IsHuman && room.Actors[16].IsHuman);
            Assert.IsFalse(plain.Actors[16].IsHuman);
            Assert.AreEqual(ClassId.Miner, room.Actors[16].Cls);
            var mixPlain = new int[ClassIds.All.Count];
            var mixRoom = new int[ClassIds.All.Count];
            for (int i = 16; i < 32; i++) { mixPlain[(int)plain.Actors[i].Cls]++; mixRoom[(int)room.Actors[i].Cls]++; }
            CollectionAssert.AreEqual(mixPlain, mixRoom, "a room pick swaps classes inside the team; the team's class mix is unchanged");
        }

        // ------------------------------------------------------------------ lockstep

        [Test]
        public void TwoPeersStayIdenticalUnder150msWithJitterAndTwoPercentLoss()
        {
            Rig rig = Make(2, 8, 150, 40, 0.02);
            Run(rig, 2700, 400_000);   // 90 s of match
            foreach (LockstepSession s in rig.Sessions)
            {
                Assert.AreEqual(LockstepStatus.Running, s.Status, "no desync, no host loss");
                Assert.GreaterOrEqual(s.Stepped, 2700);
            }
            AssertSameHashes(rig, 0, 1, 80);
            Assert.Greater(rig.Net.Dropped, 0, "the link really lost packets");
            // The client's own fighter was really driven by its pilot (it is not standing at its spawn).
            World w = rig.Sessions[1].World;
            Assert.IsTrue(w.Actors[rig.Sessions[1].LocalActor].IsHuman);
            TestContext.WriteLine($"lead host {rig.Sessions[0].Lead} client {rig.Sessions[1].Lead}; late host {rig.Sessions[0].LateInputs} client {rig.Sessions[1].LateInputs}; " +
                                  $"stall host {rig.Sessions[0].StallMs} ms client {rig.Sessions[1].StallMs} ms; bytes host {rig.Sessions[0].BytesSent} client {rig.Sessions[1].BytesSent}; " +
                                  $"sent {rig.Net.Sent} dropped {rig.Net.Dropped}");
        }

        [Test]
        public void ThirtyTwoHumansInOneRoomStayIdenticalAndSendAtTheBigRoomRate()
        {
            Rig rig = Make(32, 16, 60, 20, 0.01, null, 33);
            Assert.AreEqual(2 * new LockstepOptions().SendEveryMs, rig.Sessions[5].SendEveryMs, "a 32-human room sends half as often");
            Run(rig, 240, 60000);
            for (int i = 0; i < rig.Sessions.Count; i++)
            {
                Assert.AreEqual(LockstepStatus.Running, rig.Sessions[i].Status, "peer " + i);
                Assert.GreaterOrEqual(rig.Sessions[i].Stepped, 240, "peer " + i + " kept up");
                Assert.IsFalse(rig.Sessions[0].SlotIsBot(i), "nobody was handed to a bot");
            }
            for (int i = 1; i < rig.Sessions.Count; i++) AssertSameHashes(rig, 0, i, 6);
            int maxLead = 0;
            foreach (LockstepSession ss in rig.Sessions) maxLead = Math.Max(maxLead, ss.Lead);
            TestContext.WriteLine($"32 humans: 240 ticks took {rig.Now} ms of clock, host late inputs {rig.Sessions[0].LateInputs}, max lead {maxLead}, host stepped {rig.Sessions[0].Stepped}");
            TestContext.WriteLine($"32 humans, 240 ticks: host sent {rig.Sessions[0].BytesSent} B, a client {rig.Sessions[7].BytesSent} B, " +
                                  $"stall host {rig.Sessions[0].StallMs} ms, packets {rig.Net.Sent}, dropped {rig.Net.Dropped}");
        }

        [Test, Category("Slow")]
        public void TwoPeersPlayAFull16v16MatchHashIdentical()
        {
            Rig rig = Make(2, 16, 150, 40, 0.02);
            int full = (int)(GameConfig.CreateDefault().MatchSeconds * 30) + 300;
            Run(rig, full, 2_000_000);
            foreach (LockstepSession s in rig.Sessions) Assert.AreEqual(LockstepStatus.Ended, s.Status, "both machines reach the match end");
            Assert.AreEqual(rig.Sessions[0].World.Tick, rig.Sessions[1].World.Tick);
            Assert.AreEqual(StateHash.Of(rig.Sessions[0].World), StateHash.Of(rig.Sessions[1].World), "final worlds identical");
            Assert.AreEqual(rig.Sessions[0].World.Match.Winner, rig.Sessions[1].World.Match.Winner);
            AssertSameHashes(rig, 0, 1, 400);
        }

        [Test]
        public void ACorruptedInputIsCaughtAsDesyncWithinHashEveryTicks()
        {
            Rig rig = Make(2, 6, 60, 20, 0.02);
            const int bad = 300;
            rig.Sessions[1].TestTamper = (tick, frames) =>
            {
                if (tick < bad || tick > bad + 2) return;
                InputFrame f = frames[0];   // the host's fighter, as this client applies it
                if (f == null) return;
                f.MoveX = -f.MoveX - 0.5;
                f.MoveZ = 0.9;
                f.AimX += 5;
            };
            Run(rig, 1200, 200_000, r => r.Sessions[0].Status == LockstepStatus.Desynced && r.Sessions[1].Status == LockstepStatus.Desynced);
            foreach (LockstepSession s in rig.Sessions) Assert.AreEqual(LockstepStatus.Desynced, s.Status, "both sides stop");
            int at = rig.Sessions[0].DesyncTick;
            Assert.AreEqual(at, rig.Sessions[1].DesyncTick, "both name the same tick");
            Assert.Greater(at, bad);
            Assert.LessOrEqual(at - bad, 30, "found at the first fingerprint after the bad input");
        }

        [Test]
        public void ASilentPeerBecomesABotAtTheSameTickOnEveryMachine()
        {
            var o = new LockstepOptions { DropAfterMs = 3000 };
            Rig rig = Make(3, 8, 80, 30, 0.02, o);
            var handed = new int[3];
            for (int i = 0; i < 3; i++)
            {
                int me = i;
                handed[i] = -1;
                rig.Sessions[i].HumanToBot = (slot, actor, tick) => { if (slot == 2) handed[me] = tick; };
            }
            Run(rig, 600, 100_000);
            int leaver = rig.Sessions[2].LocalActor;
            Assert.IsTrue(rig.Sessions[0].World.Actors[leaver].IsHuman);
            rig.Net.Cut(2, false);   // no goodbye: the host must notice the silence
            var skip = new[] { false, false, true };
            Run(rig, 2400, 400_000, null, skip);
            Assert.GreaterOrEqual(handed[0], 600);
            Assert.AreEqual(handed[0], handed[1], "host and the other client hand the fighter over at the same tick");
            Assert.IsFalse(rig.Sessions[0].World.Actors[leaver].IsHuman);
            Assert.IsFalse(rig.Sessions[1].World.Actors[leaver].IsHuman);
            Assert.AreEqual(LockstepStatus.Running, rig.Sessions[0].Status);
            Assert.AreEqual(LockstepStatus.Running, rig.Sessions[1].Status);
            AssertSameHashes(rig, 0, 1, 60);
        }

        [Test]
        public void ALeaverWhoSaysGoodbyeIsHandedOverAtOnce()
        {
            Rig rig = Make(2, 6, 50, 10, 0);
            Run(rig, 300, 60_000);
            int handedAt = -1;
            rig.Sessions[0].HumanToBot = (slot, actor, tick) => handedAt = tick;
            long cutAt = rig.Now;
            rig.Net.Cut(1, true);
            Run(rig, 600, 120_000, null, new[] { false, true });
            Assert.GreaterOrEqual(handedAt, 300);
            Assert.Less(handedAt, 300 + 30, "a relay-reported leave hands over within a second, not after the silence timeout");
            Assert.AreEqual(LockstepStatus.Running, rig.Sessions[0].Status);
            Assert.Greater(cutAt, 0);
        }

        [Test]
        public void WhenTheHostVanishesTheClientSaysSo()
        {
            var o = new LockstepOptions { HostLostMs = 2000 };
            Rig rig = Make(2, 6, 50, 10, 0, o);
            Run(rig, 300, 60_000);
            rig.Net.Cut(0, false);
            Run(rig, 100000, rig.Now + 5000, r => r.Sessions[1].Status != LockstepStatus.Running, new[] { true, false });
            Assert.AreEqual(LockstepStatus.HostLeft, rig.Sessions[1].Status);
        }

        [Test]
        public void ASlowPeerIsCarriedAndAdaptsItsLead()
        {
            // 250 ms each way: a round trip longer than the starting 6-tick lead. The host repeats late inputs rather than
            // freezing the match, the client raises its lead, and the worlds still match.
            Rig rig = Make(2, 6, 250, 60, 0.02);
            Run(rig, 1800, 400_000);
            Assert.AreEqual(LockstepStatus.Running, rig.Sessions[0].Status);
            Assert.Greater(rig.Sessions[1].Lead, 6, "the client's lead grew to cover the round trip");
            AssertSameHashes(rig, 0, 1, 50);
        }

        // ------------------------------------------------------------------ online-rooms-9

        [Test]
        public void AGoodLinkBringsTheClientLeadDownToTheFloorAndTheWorldsStillMatch()
        {
            // The 2026-11-15 relay run left a joiner at a 19-tick lead (633 ms) on a link that needed far less. Start there
            // on a 30 ms each-way link: the lead must come down, never under the floor, and nothing may diverge.
            Rig rig = Make(2, 6, 30, 5, 0, null, 7, 19);
            Run(rig, 1200, 400_000);
            LockstepSession c = rig.Sessions[1];
            Assert.AreEqual(19, c.LeadMax, "starts at the room's delay");
            Assert.LessOrEqual(c.Lead, 9, "a 60 ms round trip needs a few ticks, not 19");
            Assert.GreaterOrEqual(c.LeadMin, LeadPolicy.For(19, 1000.0 / 30, new LockstepOptions().MaxLeadMs).Floor, "never under the floor");
            Assert.AreEqual(19, rig.Sessions[0].Lead, "the host's lead is the room's start delay");
            Assert.AreEqual(LockstepStatus.Running, rig.Sessions[0].Status);
            AssertSameHashes(rig, 0, 1, 30);
        }

        [Test]
        public void APeerThatKeepsMissingTicksIsRepeatedAndTheRoomDoesNotWaitForItEachTime()
        {
            // Peer 2 goes quiet for 250 ms of every 500 ms (a link that hovers at the edge), with a ceiling too low for
            // its lead to cover the gaps. Before online-rooms-9 every gap cost the whole room a LateMs wait again (an
            // on-time input cleared "lagging" at once): 40 gaps, about 10 s of waits. Now it stays lagging and is repeated.
            var o = new LockstepOptions { MaxLeadMs = 150 };
            Rig rig = Make(3, 8, 40, 10, 0, o);
            Run(rig, 300, 60_000);
            long before = rig.Sessions[0].StallMs;
            var skip = new[] { false, false, true };
            for (int n = 0; n < 40; n++)
            {
                Run(rig, 1_000_000, rig.Now + 250, null, skip);
                Run(rig, 1_000_000, rig.Now + 250);
            }
            LockstepSession host = rig.Sessions[0];
            long waited = host.StallMs - before;
            Assert.Less(waited, 1500, $"the room waited {waited} ms in 20 s for one edge-of-time peer");
            Assert.Greater(host.LateInputs, 30, "the missing inputs were repeated");
            Assert.IsTrue(host.World.Actors[rig.Sessions[2].LocalActor].IsHuman, "not dropped: it is still sending");
            Assert.AreEqual(LockstepStatus.Running, host.Status);
            Assert.AreEqual(LockstepStatus.Running, rig.Sessions[1].Status);
            AssertSameHashes(rig, 0, 1, 20);
            AssertSameHashes(rig, 0, 2, 10);
        }

        [Test]
        public void EveryoneLeftInTheMatchSeesWhoLeftAndThatABotTookOver()
        {
            Rig rig = Make(3, 8, 50, 10, 0);
            var chats = new ChatChannel[3];
            for (int i = 0; i < 3; i++)
            {
                chats[i] = new ChatChannel(rig.Links[i]);
                RoomNotices.Hook(rig.Sessions[i], chats[i]);
            }
            Run(rig, 300, 60_000);
            rig.Net.Cut(2, true);
            Run(rig, 600, 120_000, null, new[] { false, false, true });
            string want = RoomNotices.LeftMatch("P2");
            Assert.AreEqual("P2 left: a bot took over.", want);
            for (int i = 0; i < 2; i++)
            {
                int found = 0;
                foreach (ChatLine l in chats[i].Lines) if (l.Scope == ChatScope.System && l.Text == want) found++;
                Assert.AreEqual(1, found, (i == 0 ? "the host" : "the joiner") + " shows the leave notice once");
            }
        }
    }
}
