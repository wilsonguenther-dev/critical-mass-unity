using CriticalMass.Net;
using CriticalMass.Sim.Config;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests.Net
{
    /// <summary>online-rooms-2: the room flow (join, pick, ready, start) over the loopback network, then the match itself.</summary>
    public sealed class OnlineRoomTests
    {
        [Test]
        public void JoinPickReadyStartThenBothPlayTheSameMatch()
        {
            var net = new LoopbackNetwork(11, 40, 15, 0);
            var cfg = GameConfig.CreateDefault();
            var host = new RoomHost(net.CreatePeer(), cfg, "Wilson", "v1.0.600");
            host.TeamSize = 6;
            var client = new RoomClient(net.CreatePeer(), cfg, "Jeisil", "v1.0.600");
            long now = 0;
            for (; now < 5000; now += 10) { net.Advance(now); host.Update(now); client.Update(now); }
            Assert.AreEqual(RoomPhase.Lobby, client.Phase);
            Assert.AreEqual(2, client.Members.Count);
            Assert.IsFalse(host.CanStart(out string why));
            StringAssert.Contains("Jeisil", why);
            client.SetPick(TeamId.Crimson, ClassId.Miner);
            client.SetReady(true);
            for (long end = now + 3000; now < end; now += 10) { net.Advance(now); host.Update(now); client.Update(now); }
            Assert.IsTrue(host.CanStart(out why), why);
            Assert.Greater(host.Find(1).PingMs, 60, "the lobby measured the round trip");
            LockstepSession hs = host.Start(now);
            Assert.IsNotNull(hs);
            for (long end = now + 60000; now < end && (hs.Stepped < 600 || client.Session == null || client.Session.Stepped < 600); now += 5)
            {
                net.Advance(now);
                host.Update(now);
                client.Update(now);
            }
            LockstepSession cs = client.Session;
            Assert.IsNotNull(cs, "the client entered the match");
            Assert.AreEqual(6, cs.LocalActor, "a Crimson joiner plays the first Crimson seat");
            Assert.AreEqual(ClassId.Miner, cs.World.Actors[6].Cls);
            Assert.IsTrue(hs.World.Actors[6].IsHuman);
            Assert.AreEqual(hs.Setup.Seed, cs.Setup.Seed);
            Assert.IsTrue(hs.TryGetHash(600, out ulong a), "host fingerprint at tick 600");
            Assert.IsTrue(cs.TryGetHash(600, out ulong b), "client fingerprint at tick 600");
            Assert.AreEqual(a, b);
        }

        [Test]
        public void ADifferentGameVersionIsRefusedWithAPlainReason()
        {
            var net = new LoopbackNetwork(3, 20, 0, 0);
            var cfg = GameConfig.CreateDefault();
            var host = new RoomHost(net.CreatePeer(), cfg, "Wilson", "v1.0.600");
            var client = new RoomClient(net.CreatePeer(), cfg, "Jeisil", "v1.0.599");
            for (long now = 0; now < 3000; now += 10) { net.Advance(now); host.Update(now); client.Update(now); }
            Assert.AreEqual(RoomPhase.Closed, client.Phase);
            StringAssert.Contains("different version", client.Error);
            Assert.AreEqual(1, host.Members.Count, "the refused player is not listed");
        }

        [Test]
        public void AFullTeamRefusesAPickAndHostilePacketsAreIgnored()
        {
            var net = new LoopbackNetwork(5, 10, 0, 0);
            var cfg = GameConfig.CreateDefault();
            var host = new RoomHost(net.CreatePeer(), cfg, "Wilson", "b");
            host.TeamSize = 1;   // one seat per team: the host's team is full already
            LoopbackTransport raw = net.CreatePeer();
            var client = new RoomClient(raw, cfg, "Jeisil", "b");
            long now = 0;
            for (; now < 2000; now += 10) { net.Advance(now); host.Update(now); client.Update(now); }
            Assert.AreEqual(TeamId.Crimson, host.Find(1).Team, "the joiner was seated on the team with room");
            client.SetPick(TeamId.Azure, ClassId.Warrior);
            // Garbage, truncated and oversized-count packets: dropped, nothing thrown.
            raw.Send(0, new byte[] { 22 }, 1);
            raw.Send(0, new byte[] { 22, 9, 200 }, 3);
            raw.Send(0, new byte[] { 1, 255, 255, 255, 255, 0, 0, 0, 0, 7 }, 10);
            raw.Send(0, new byte[] { 25, 1, 2 }, 3);
            for (long end = now + 2000; now < end; now += 10) { net.Advance(now); host.Update(now); client.Update(now); }
            Assert.AreEqual(TeamId.Crimson, host.Find(1).Team, "a pick for a full team is refused");
            Assert.AreEqual(RoomPhase.Lobby, host.Phase);
        }
    }
}
