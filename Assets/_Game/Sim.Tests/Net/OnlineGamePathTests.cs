using System;
using System.Collections.Generic;
using System.Threading;
using CriticalMass.Net;
using CriticalMass.Net.Relay;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests.Net
{
    /// <summary>
    /// online-rooms-3: the game's own online path (HeadlessPeer, the code the hidden test mode and the console peer run),
    /// host and joiners in one process through LocalRelay (server/relay's rules, no sockets, no relay requests), with an
    /// offline match ticking beside them the way the scene's match did in the first real test.
    /// </summary>
    public sealed class OnlineGamePathTests
    {
        private sealed class Peer
        {
            public HeadlessPeer P;
            public readonly List<string> Log = new List<string>();
            public int? Exit;
        }

        private static Peer Make(LocalRelay relay, bool host, string name, Action<HeadlessPeer> tweak = null)
        {
            var peer = new Peer();
            peer.P = new HeadlessPeer { Local = relay, Host = host, Name = name, TeamSize = 8, Pace = 1, Seed = 777, JoinRetryMs = 300, LobbySeconds = 60, Ticks = 600 };
            peer.P.Log = s => peer.Log.Add(s);
            tweak?.Invoke(peer.P);
            return peer;
        }

        /// <summary>Steps everyone (and an offline world) on one clock until every peer has exited.</summary>
        private static void RunAll(LocalRelay relay, List<Peer> peers, World offline, long maxMs = 120000)
        {
            var scratch = new InputFrame();
            for (long now = 0; now < maxMs; now += 5)
            {
                relay.Advance(now);
                bool all = true;
                foreach (Peer p in peers)
                {
                    if (p.Exit.HasValue) continue;
                    p.Exit = p.P.Step(now);
                    if (!p.Exit.HasValue) all = false;
                }
                // The scene's offline match, ticking on the same thread between the online steps (30 a second).
                if (offline != null && now % 33 == 0 && offline.Match.Phase != MatchPhase.Ended) MatchTick.Advance(offline, null, scratch);
                if (all) return;
            }
            Assert.Fail("the peers did not finish: " + string.Join(" | ", peers.ConvertAll(p => p.Log.Count > 0 ? p.Log[p.Log.Count - 1] : "(nothing)")));
        }

        private static void Begin(Peer p, long now = 0) { if (!p.P.Begin(now)) p.Exit = p.P.ExitCode; }

        private static void AssertSame(Peer a, Peer b, int minChecks)
        {
            var theirs = new Dictionary<int, ulong>();
            foreach (KeyValuePair<int, ulong> kv in b.P.Hashes) theirs[kv.Key] = kv.Value;
            int n = 0;
            bool sawZero = false, sawOne = false;
            foreach (KeyValuePair<int, ulong> kv in a.P.Hashes)
            {
                if (!theirs.TryGetValue(kv.Key, out ulong h)) continue;
                Assert.AreEqual(kv.Value.ToString("X16"), h.ToString("X16"), "fingerprints differ at tick " + kv.Key);
                if (kv.Key == 0) sawZero = true;
                if (kv.Key == 1) sawOne = true;
                n++;
            }
            Assert.IsTrue(sawZero && sawOne, "tick 0 and tick 1 are both fingerprinted");
            Assert.GreaterOrEqual(n, minChecks, "enough common fingerprints");
        }

        private static bool Logged(Peer p, string part) => p.Log.Exists(l => l.Contains(part));

        [Test]
        public void HostAndJoinerAgreeFor600TicksThroughTheLocalRelayWhileAnOfflineMatchTicks()
        {
            var relay = new LocalRelay(40);
            Peer host = Make(relay, true, "Wilson", p => { p.ChatAt = 90; p.ChatText = "good luck"; });
            Begin(host);
            string code = host.P.Code;
            Assert.IsNotNull(code);
            // The joiner asks before the host's connection is through: it must hear "not ready" and ask again, not fail.
            Peer join = Make(relay, false, "Jeisil", p => { p.Code = code; p.Team = TeamId.Crimson; p.Class = ClassId.Miner; });
            Begin(join);
            World offline = World.CreateWorld(new WorldOptions { Seed = 5, TeamSize = 8, Config = GameConfig.CreateDefault() });
            RunAll(relay, new List<Peer> { host, join }, offline);
            Assert.AreEqual(0, host.Exit, string.Join("\n", host.Log));
            Assert.AreEqual(0, join.Exit, string.Join("\n", join.Log));
            Assert.IsTrue(Logged(join, "ONLINE RETRY"), "the early joiner retried");
            Assert.IsTrue(Logged(host, "ROOM READY"));
            Assert.IsTrue(Logged(join, "good luck"), "the joiner received the host's chat line");
            Assert.IsTrue(Logged(host, "pings "), "the summary carries round trips");
            AssertSame(host, join, 20);
        }

        [Test]
        public void ADroppedPeerBecomesABotAtTheSameTickOnEveryOtherMachine()
        {
            var relay = new LocalRelay(30);
            Peer host = Make(relay, true, "Wilson", p => p.Humans = 3);
            Begin(host);
            relay.Advance(100);
            host.P.Step(100);
            relay.Advance(200);
            host.P.Step(200);
            Peer a = Make(relay, false, "Jeisil", p => { p.Code = host.P.Code; p.Team = TeamId.Crimson; });
            Peer b = Make(relay, false, "Travis", p => { p.Code = host.P.Code; p.DropAt = 200; });
            Begin(a);
            Begin(b);
            RunAll(relay, new List<Peer> { host, a, b }, null);
            Assert.AreEqual(0, host.Exit, string.Join("\n", host.Log));
            Assert.AreEqual(0, a.Exit, string.Join("\n", a.Log));
            Assert.IsTrue(Logged(b, "ONLINE DROPPED"));
            string hostBot = host.Log.Find(l => l.Contains("ONLINE TOBOT"));
            string aBot = a.Log.Find(l => l.Contains("ONLINE TOBOT"));
            Assert.IsNotNull(hostBot, string.Join("\n", host.Log));
            Assert.AreEqual(hostBot, aBot, "the same seat at the same tick on both machines");
            AssertSame(host, a, 20);
        }

        [Test]
        public void ADifferentSetupIsReportedAtTickZeroNotAsADriftLater()
        {
            GameConfig a = GameConfig.CreateDefault(), b = GameConfig.CreateDefault();
            Assert.AreEqual(ConfigHash.Of(a), ConfigHash.Of(b), "two default configs fingerprint the same");
            b.Render.CameraYawDeg = 135;
            Assert.AreEqual(ConfigHash.Of(a), ConfigHash.Of(b), "the camera angle is view-only and ignored");
            b.DefaultSpeed = 1.5;
            Assert.AreNotEqual(ConfigHash.Of(a), ConfigHash.Of(b), "a rule change is seen");

            var net = new LoopbackNetwork(3, 20, 0, 0);
            LoopbackTransport th = net.CreatePeer(), tc = net.CreatePeer();
            var setup = new MatchSetup { Seed = 9, TeamSize = 4, HostTeam = TeamId.Azure, InputDelay = 4, HashEvery = 30, Pace = 1 };
            setup.Humans = new[] { new HumanSlot { Peer = th.LocalPeer, Actor = 0, Name = "H" }, new HumanSlot { Peer = tc.LocalPeer, Actor = 4, Name = "C" } };
            var host = new LockstepSession(th, setup, GameConfig.CreateDefault());
            setup.ConfigHash = host.MyConfigHash;
            setup.WorldHash0 = host.MyWorldHash0;
            var client = new LockstepSession(tc, setup, b);
            Assert.AreEqual(LockstepStatus.Desynced, client.Status);
            Assert.AreEqual(0, client.DesyncTick);
            StringAssert.Contains("rules", client.SetupMismatch);
            for (long now = 0; now < 3000; now += 5) { net.Advance(now); host.Update(now); client.Update(now); }
            Assert.AreEqual(LockstepStatus.Desynced, host.Status, "the host hears it too");
            Assert.AreEqual(0, host.DesyncTick);
        }

        [Test]
        public void TwoThreadsSteppingTheSimAtOnceAreStoppedLoudly()
        {
            using (var inside = new ManualResetEventSlim(false))
            using (var release = new ManualResetEventSlim(false))
            {
                var t = new Thread(() => { using (SimThreadGuard.Enter()) { inside.Set(); release.Wait(5000); } });
                t.Start();
                Assert.IsTrue(inside.Wait(5000));
                Assert.Throws<InvalidOperationException>(() => { using (SimThreadGuard.Enter()) { } });
                release.Set();
                t.Join(5000);
            }
            using (SimThreadGuard.Enter()) using (SimThreadGuard.Enter()) { }   // the same thread may nest
        }
    }
}
