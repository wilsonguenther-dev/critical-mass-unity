using System;
using System.Reflection;
using CriticalMass.Net;
using CriticalMass.Sim.Config;
using NUnit.Framework;


namespace CriticalMass.Sim.Tests.Net
{
    /// <summary>
    /// b15 profile: the cosmetic look players show each other. It travels in its own lobby messages (Look, Looks), never in
    /// the match setup, the inputs, the config hash or the state hash; every look from a peer is clamped on arrival (ids out
    /// of range or above the claimed mastery become 0, mastery is held to 1..20), and short or garbage packets are ignored.
    /// </summary>
    public sealed class CosmeticLookTests
    {
        static void P(string s) => Console.WriteLine("[b15-look] " + s);

        /// <summary>RoomMsg.Look (internal to the Net assembly): the first byte of a look packet.</summary>
        const byte LookMsg = 28;

        [Test]
        public void ClampClearsIdsOutOfRangeOrAboveTheClaimedMastery()
        {
            (CosmeticLook given, CosmeticLook want)[] cases =
            {
                (new CosmeticLook(0, 0, 0, 0, 1), new CosmeticLook(0, 0, 0, 0, 1)),
                (new CosmeticLook(200, 200, 200, 200, 255), new CosmeticLook(0, 0, 0, 0, CosmeticLook.MaxMastery)),
                (new CosmeticLook(7, 4, 5, 5, 20), new CosmeticLook(7, 4, 5, 5, 20)),
                (new CosmeticLook(7, 4, 5, 5, 3), new CosmeticLook(0, 0, 0, 0, 3)),   // above the mastery: cleared, never swapped
                (new CosmeticLook(1, 1, 1, 1, 0), new CosmeticLook(1, 0, 0, 0, 1)),   // mastery 0 is held to 1
                (new CosmeticLook(3, 1, 1, 1, 5), new CosmeticLook(3, 1, 1, 1, 5)),
                (new CosmeticLook(-5, -1, 999, 1, 2), new CosmeticLook(0, 0, 0, 1, 2)),
            };
            foreach ((CosmeticLook given, CosmeticLook want) in cases)
            {
                CosmeticLook got = CosmeticLook.Clamp(given);
                P($"clamp ({given}) -> ({got})");
                Assert.AreEqual(want, got, given.ToString());
                Assert.AreEqual(got, CosmeticLook.Clamp(got), "clamping twice changes nothing");
            }
            Assert.AreEqual(CosmeticLook.TitleLevels.Length, 8);
            Assert.AreEqual(CosmeticLook.MaxMastery, CosmeticLook.TitleLevels[CosmeticLook.TitleLevels.Length - 1]);
            Assert.AreEqual(CosmeticLook.MaxMastery, CosmeticLook.FrameLevels[CosmeticLook.FrameLevels.Length - 1]);
        }

        [Test]
        public void AShortOrGarbagePacketReadsAsTheDefaultLook()
        {
            var r = new WireReader(new byte[] { 3, 2 }, 0, 2);
            CosmeticLook l = CosmeticLook.Read(ref r);
            P($"short read: bad {r.Bad}, look ({l})");
            Assert.IsTrue(r.Bad);
            Assert.AreEqual(CosmeticLook.Default, l);
            var rnd = new System.Random(5);
            var buf = new byte[CosmeticLook.WireBytes];
            for (int i = 0; i < 500; i++)
            {
                rnd.NextBytes(buf);
                var g = new WireReader(buf, 0, buf.Length);
                CosmeticLook c = CosmeticLook.Read(ref g);
                Assert.IsFalse(g.Bad);
                Assert.AreEqual(c, CosmeticLook.Clamp(c), "every read look is already clamped");
                Assert.That(c.Mastery, Is.InRange(1, CosmeticLook.MaxMastery));
            }
        }

        [Test]
        public void AHostileLookOverTheRoomIsClampedAndGarbageIsIgnored()
        {
            var net = new LoopbackNetwork(33, 30, 10, 0);
            var cfg = GameConfig.CreateDefault();
            var host = new RoomHost(net.CreatePeer(), cfg, "Wilson", "b") { TeamSize = 4, Pace = 1 };
            host.LookFor = c => new CosmeticLook(5, 3, 4, 4, 16);
            LoopbackTransport foeLink = net.CreatePeer();
            var mate = new RoomClient(net.CreatePeer(), cfg, "Travis", "b") { LookFor = c => new CosmeticLook(2, 1, 0, 1, 5) };
            var foe = new RoomClient(foeLink, cfg, "Jeisil", "b");
            long now = 0;
            void Run(long ms)
            {
                for (long end = now + ms; now < end; now += 5) { net.Advance(now); host.Update(now); mate.Update(now); foe.Update(now); }
            }
            Run(2000);
            mate.SetPick(TeamId.Azure, ClassId.Healer);
            foe.SetPick(TeamId.Crimson, ClassId.Miner);
            Run(1000);
            int foePeer = 1;   // the foe's transport was created first after the host
            P($"peers: mate {mate.Me?.Peer}, foe {foe.Me?.Peer}");
            if (foe.Me != null) foePeer = foe.Me.Peer;

            // a hand-made packet: every byte at its worst
            byte[] hostile = { LookMsg, (byte)ClassId.Miner, 250, 250, 250, 250, 250 };
            foeLink.Send(Peers.Host, hostile, hostile.Length);
            Run(1500);
            CosmeticLook atHost = host.LookOf(foePeer);
            CosmeticLook atMate = mate.LookOf(foePeer);
            P($"hostile ({string.Join(",", hostile)}) -> host sees ({atHost}), mate sees ({atMate})");
            Assert.AreEqual(new CosmeticLook(0, 0, 0, 0, CosmeticLook.MaxMastery), atHost);
            Assert.AreEqual(atHost, atMate, "the host passes on only the clamped look");

            // a frame the claimed mastery has not reached
            byte[] early = { LookMsg, (byte)ClassId.Miner, 1, 4, 1, 1, 4 };
            foeLink.Send(Peers.Host, early, early.Length);
            Run(1500);
            P($"early ({string.Join(",", early)}) -> host sees ({host.LookOf(foePeer)})");
            Assert.AreEqual(new CosmeticLook(1, 0, 1, 1, 4), host.LookOf(foePeer));

            // short, unknown class, and garbage: ignored, the last good look stays
            CosmeticLook before = host.LookOf(foePeer);
            foeLink.Send(Peers.Host, new byte[] { LookMsg, (byte)ClassId.Miner, 1 }, 3);
            foeLink.Send(Peers.Host, new byte[] { LookMsg, 77, 1, 1, 1, 1, 5 }, 7);
            foeLink.Send(Peers.Host, new byte[] { LookMsg }, 1);
            Run(1500);
            P($"after short/unknown/garbage: host sees ({host.LookOf(foePeer)})");
            Assert.AreEqual(before, host.LookOf(foePeer));

            // a look for a class the player has not picked shows nothing
            byte[] other = { LookMsg, (byte)ClassId.Warrior, 3, 1, 1, 1, 8 };
            foeLink.Send(Peers.Host, other, other.Length);
            Run(1500);
            P($"look for an unpicked class -> host sees ({host.LookOf(foePeer)})");
            Assert.AreEqual(CosmeticLook.Default, host.LookOf(foePeer));

            // honest clients and the host itself come through as sent
            int matePeer = mate.Me != null ? mate.Me.Peer : 2;
            P($"mate at host ({host.LookOf(matePeer)}), host at mate ({mate.LookOf(Peers.Host)})");
            Assert.AreEqual(new CosmeticLook(2, 1, 0, 1, 5), host.LookOf(matePeer));
            Assert.AreEqual(new CosmeticLook(5, 3, 4, 4, 16), mate.LookOf(Peers.Host));
        }

        [Test]
        public void LooksNeverChangeTheMatch()
        {
            ulong withLooks = RunMatch(true, out int setupA), without = RunMatch(false, out int setupB);
            P($"state hash at tick 600: with looks {withLooks:X16}, without {without:X16}; setup bytes {setupA} vs {setupB}");
            Assert.AreEqual(without, withLooks, "the same seed and inputs give the same world with or without looks");

            foreach (FieldInfo f in typeof(MatchSetup).GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Assert.AreNotEqual(typeof(CosmeticLook), f.FieldType, "MatchSetup." + f.Name);
                Assert.AreNotEqual(typeof(CosmeticLook[]), f.FieldType, "MatchSetup." + f.Name);
            }
            foreach (FieldInfo f in typeof(HumanSlot).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                Assert.AreNotEqual(typeof(CosmeticLook), f.FieldType, "HumanSlot." + f.Name);
            P("MatchSetup and HumanSlot carry no look field");
        }

        static ulong RunMatch(bool looks, out int setupHumans)
        {
            var net = new LoopbackNetwork(21, 30, 10, 0);
            var cfg = GameConfig.CreateDefault();
            var host = new RoomHost(net.CreatePeer(), cfg, "Wilson", "b") { TeamSize = 4, Pace = 1 };
            var mate = new RoomClient(net.CreatePeer(), cfg, "Travis", "b");
            var foe = new RoomClient(net.CreatePeer(), cfg, "Jeisil", "b");
            if (looks)
            {
                host.LookFor = c => new CosmeticLook(7, 4, 5, 5, 20);
                mate.LookFor = c => new CosmeticLook(3, 1, 2, 2, 9);
                foe.LookFor = c => new CosmeticLook(200, 200, 200, 200, 200);
            }
            long now = 0;
            void Step() { net.Advance(now); host.Update(now); mate.Update(now); foe.Update(now); }
            for (long end = now + 2000; now < end; now += 5) Step();
            mate.SetPick(TeamId.Azure, ClassId.Builder);
            foe.SetPick(TeamId.Crimson, ClassId.Miner);
            mate.SetReady(true);
            foe.SetReady(true);
            for (long end = now + 1500; now < end; now += 5) Step();
            LockstepSession hs = host.Start(now, null, 777);
            Assert.IsNotNull(hs);
            setupHumans = hs.Setup?.Humans?.Length ?? 0;
            for (long end = now + 60000; now < end && (mate.Session == null || foe.Session == null || hs.Stepped < 600 || mate.Session.Stepped < 600 || foe.Session.Stepped < 600); now += 5) Step();
            Assert.IsTrue(hs.TryGetHash(600, out ulong a));
            Assert.IsTrue(mate.Session.TryGetHash(600, out ulong b));
            Assert.IsTrue(foe.Session.TryGetHash(600, out ulong c));
            Assert.AreEqual(a, b);
            Assert.AreEqual(a, c);
            return a;
        }
    }
}
