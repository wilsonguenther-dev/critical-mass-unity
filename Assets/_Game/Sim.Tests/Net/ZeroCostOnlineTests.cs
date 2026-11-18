using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using CriticalMass.Net;
using CriticalMass.Net.Relay;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests.Net
{
    /// <summary>
    /// b15-zero-cost: what online play costs the free relay, measured on the game's own online path (HeadlessPeer through
    /// LocalRelay, which follows server/relay's rules), before and after b15's sending changes, plus the no-relay paths
    /// (direct TCP, local-network discovery, fallback to the relay, the relay dying under a room). Every case prints one
    /// line per number. The cost guard (CostPerPlayerMinuteStaysAtTheBaseline) fails if messages or bytes per player-minute
    /// creep more than 10% over the pinned baseline, so the traffic cannot quietly come back.
    /// </summary>
    public sealed class ZeroCostOnlineTests
    {
        // ------------------------------------------------------------------------------------------------ the rig

        private sealed class Peer
        {
            public HeadlessPeer P;
            public readonly List<string> Log = new List<string>();
            public int? Exit;
            public double LeadSum;
            public long LeadN;
        }

        private static Peer Make(LocalRelay relay, bool host, string name, Action<HeadlessPeer> tweak = null)
        {
            var peer = new Peer();
            peer.P = new HeadlessPeer { Local = relay, Host = host, Name = name, TeamSize = 16, Pace = 1, Seed = 4242, JoinRetryMs = 300, LobbySeconds = 60, JoinTries = 20 };
            peer.P.Log = s => { lock (peer.Log) peer.Log.Add(s); };
            tweak?.Invoke(peer.P);
            return peer;
        }

        private static void Begin(Peer p, long now = 0) { if (!p.P.Begin(now)) p.Exit = p.P.ExitCode; }

        private static void StepOnce(Peer p, long now)
        {
            if (p.Exit.HasValue) return;
            p.Exit = p.P.Step(now);
            LockstepSession s = p.P.Session;
            if (!p.P.Host && s != null && s.Status == LockstepStatus.Running && s.Stepped > 60) { p.LeadSum += s.Lead; p.LeadN++; }
        }

        /// <summary>Everyone on one simulated clock (5 ms steps): deterministic, so the counts are the same on every run.</summary>
        private static void RunSim(LocalRelay relay, List<Peer> peers, long maxMs)
        {
            for (long now = 0; now < maxMs; now += 5)
            {
                relay?.Advance(now);
                bool all = true;
                foreach (Peer p in peers) { StepOnce(p, now); if (!p.Exit.HasValue) all = false; }
                if (all) return;
            }
            Assert.Fail("the peers did not finish: " + string.Join(" | ", peers.ConvertAll(Last)));
        }

        /// <summary>Everyone on the wall clock (real sockets need real time).</summary>
        private static void RunReal(LocalRelay relay, List<Peer> peers, long maxMs, Action<long> each = null)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < maxMs)
            {
                long now = clock.ElapsedMilliseconds;
                relay?.Advance(now);
                bool all = true;
                foreach (Peer p in peers) { StepOnce(p, now); if (!p.Exit.HasValue) all = false; }
                each?.Invoke(now);
                if (all) return;
                Thread.Sleep(1);
            }
            Assert.Fail("the peers did not finish: " + string.Join(" | ", peers.ConvertAll(Last)));
        }

        private static string Last(Peer p) { lock (p.Log) return p.P.Name + ": " + (p.Log.Count > 0 ? p.Log[p.Log.Count - 1] : "(nothing)"); }
        private static string All(Peer p) { lock (p.Log) return string.Join("\n", p.Log); }

        /// <summary>Fingerprints the two machines share must be equal; returns how many were compared.</summary>
        private static int AssertSame(Peer a, Peer b, int minChecks, int fromTick = 0)
        {
            var theirs = new Dictionary<int, ulong>();
            foreach (KeyValuePair<int, ulong> kv in b.P.Hashes) theirs[kv.Key] = kv.Value;
            int n = 0;
            foreach (KeyValuePair<int, ulong> kv in a.P.Hashes)
            {
                if (kv.Key < fromTick || !theirs.TryGetValue(kv.Key, out ulong h)) continue;
                Assert.AreEqual(kv.Value.ToString("X16"), h.ToString("X16"), $"{a.P.Name} and {b.P.Name}: fingerprints differ at tick {kv.Key}");
                n++;
            }
            Assert.GreaterOrEqual(n, minChecks, $"{a.P.Name} and {b.P.Name}: enough common fingerprints");
            return n;
        }

        private static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        // ------------------------------------------------------------------------------------------------ measuring

        private struct Arm
        {
            public double RoomMsgsPerSec, MsgsPerPlayerMin, BytesPerPlayerMin, LobbyMsgs, AvgLead, Seconds;
            public int LeadMax, Late, Checks;
            public long StallMs;
            public TrafficCounts Room;
        }

        /// <summary>One room of `humans` through LocalRelay (25 ms each way), every human an autopilot; the relay's incoming
        /// messages are what every peer sent (each Send is one relay message).</summary>
        private static Arm Measure(int humans, int ticks, LockstepOptions options)
        {
            var relay = new LocalRelay(25);
            var peers = new List<Peer>();
            Peer host = Make(relay, true, "Host", p => { p.Humans = humans; p.Ticks = ticks; p.Options = options; });
            peers.Add(host);
            Begin(host);
            for (int i = 1; i < humans; i++)
            {
                int k = i;
                Peer j = Make(relay, false, "P" + k, p =>
                {
                    p.Code = host.P.Code;
                    p.Ticks = ticks;
                    p.Options = options;
                    p.Team = k % 2 == 1 ? TeamId.Crimson : TeamId.Azure;   // at most 16 a side
                });
                peers.Add(j);
                Begin(j);
            }
            RunSim(relay, peers, 600000);
            var arm = new Arm { Room = new TrafficCounts() };
            foreach (Peer p in peers)
            {
                Assert.AreEqual(0, p.Exit, All(p));
                TrafficCounts m = p.P.Meter.Sent.Since(p.P.SentAtStart);
                for (int i = 0; i < TrafficCounts.Kinds; i++) { arm.Room.Messages[i] += m.Messages[i]; arm.Room.Bytes[i] += m.Bytes[i]; }
                arm.LobbyMsgs += p.P.SentAtStart != null ? p.P.SentAtStart.TotalMessages : 0;
                if (p != host)
                {
                    arm.Checks = Math.Max(arm.Checks, AssertSame(host, p, ticks / 30 - 2));
                    arm.LeadMax = Math.Max(arm.LeadMax, p.P.Session.LeadMax);
                    arm.Late += p.P.Session.LateInputs;
                    arm.StallMs += p.P.Session.StallMs;
                    arm.AvgLead += p.LeadN > 0 ? p.LeadSum / p.LeadN : 0;
                }
            }
            arm.AvgLead /= Math.Max(1, humans - 1);
            arm.Seconds = host.P.Session.Stepped / 30.0;
            arm.RoomMsgsPerSec = arm.Room.TotalMessages / arm.Seconds;
            arm.MsgsPerPlayerMin = arm.RoomMsgsPerSec * 60 / humans;
            arm.BytesPerPlayerMin = arm.Room.TotalBytes / arm.Seconds * 60 / humans;
            return arm;
        }

        private static string Line(string arm, int humans, Arm a) =>
            $"[zero-cost] {humans} humans {arm}: {a.Seconds:F1} s of match, relay receives {a.RoomMsgsPerSec:F1} msg/s ({a.Room.Describe()}); " +
            $"per player-minute {a.MsgsPerPlayerMin:F0} msg, {a.BytesPerPlayerMin:F0} B; lobby {a.LobbyMsgs:F0} msg; joiners' lead avg {a.AvgLead:F2} max {a.LeadMax} ticks, " +
            $"late inputs {a.Late}, stalls {a.StallMs} ms; {a.Checks} fingerprints MATCH";

        /// <summary>Messages and bytes per player-minute after b15, pinned from the sim gate's print (the cost guard's line).
        /// 0 bytes = not pinned (printed, not asserted).</summary>
        private static readonly Dictionary<int, (double msgs, double bytes)> Baseline = new Dictionary<int, (double, double)>
        {
            // humans: (messages, bytes) per player-minute in a match, pinned from sim-tests run 44245987359 (b15-zero-cost,
            // deterministic: LocalRelay on a simulated clock). Before b15 the same rooms measured 1,799 to 1,800 messages
            // (999 at 32 humans) and 119,930 to 125,363 bytes (92,940 at 32). Raise a number only with the reason in the PR.
            { 2, (902, 59340) },
            { 4, (902, 64299) },
            { 8, (902, 66779) },
            { 32, (907, 68889) },
        };

        private static void CompareArms(int humans, int ticks)
        {
            Arm before = Measure(humans, ticks, LockstepOptions.PreB15());
            Arm after = Measure(humans, ticks, null);
            Console.WriteLine(Line("before b15", humans, before));
            Console.WriteLine(Line("after b15 ", humans, after));
            double cut = 1 - after.RoomMsgsPerSec / before.RoomMsgsPerSec;
            double est = RelayBudget.MatchMessagesPerSecond(humans), estBefore = RelayBudget.MatchMessagesPerSecond(humans, true);
            double reqBefore = RelayBudget.RequestsPerMatch(humans, before.RoomMsgsPerSec), reqAfter = RelayBudget.RequestsPerMatch(humans, after.RoomMsgsPerSec);
            Console.WriteLine($"[zero-cost] {humans} humans: relay messages cut {cut:P0} (limit: at least {(humans > 16 ? 8 : 40)}%); estimate {est:F1} msg/s after, {estBefore:F1} before " +
                $"(limit: measured within 15% of it); an 8-minute match with a 2-minute lobby = {reqBefore:F0} requests before, {reqAfter:F0} after = " +
                $"{RelayBudget.MatchesPerDay(reqBefore)} -> {RelayBudget.MatchesPerDay(reqAfter)} matches a day on the free tier");
            Console.WriteLine($"[zero-cost] {humans} humans: joiners' felt delay avg {before.AvgLead:F2} -> {after.AvgLead:F2} ticks, max {before.LeadMax} -> {after.LeadMax} (limit: at most +1 tick)");
            Assert.GreaterOrEqual(cut, humans > 16 ? 0.08 : 0.40, "the relay receives clearly fewer messages");
            Assert.AreEqual(est, after.RoomMsgsPerSec, est * 0.15, "the in-game estimate (RelayBudget) matches the measured room");
            Assert.LessOrEqual(after.AvgLead, before.AvgLead + 1.0, "average felt input delay grows by at most one tick");
            Assert.LessOrEqual(after.LeadMax, before.LeadMax + 1, "the largest felt input delay grows by at most one tick");
            double stallBefore = before.StallMs / (double)(humans - 1), stallAfter = after.StallMs / (double)(humans - 1);
            Console.WriteLine($"[zero-cost] {humans} humans: waiting for the network per joiner {stallBefore:F0} -> {stallAfter:F0} ms in {after.Seconds:F0} s (limit: at most +300 ms)");
            Assert.LessOrEqual(stallAfter, stallBefore + 300, "joiners do not wait for the network noticeably more");
            // The cost guard: traffic per player-minute never creeps back past the pinned baseline (+10%).
            (double msgs, double bytes) b = Baseline[humans];
            Console.WriteLine($"[zero-cost] cost guard {humans} humans: {after.MsgsPerPlayerMin:F0} msg per player-minute (baseline {b.msgs:F0}, limit {b.msgs * 1.1:F0}); " +
                $"{after.BytesPerPlayerMin:F0} B per player-minute (baseline {(b.bytes > 0 ? b.bytes.ToString("F0") : "not pinned")}, limit {(b.bytes > 0 ? (b.bytes * 1.1).ToString("F0") : "-")})");
            Assert.LessOrEqual(after.MsgsPerPlayerMin, b.msgs * 1.10, "messages per player-minute crept over the baseline");
            if (b.bytes > 0) Assert.LessOrEqual(after.BytesPerPlayerMin, b.bytes * 1.10, "bytes per player-minute crept over the baseline");
        }

        [Test]
        public void CostPerPlayerMinuteStaysAtTheBaseline_2Humans() => CompareArms(2, 900);

        [Test]
        public void CostPerPlayerMinuteStaysAtTheBaseline_4Humans() => CompareArms(4, 900);

        [Test]
        public void CostPerPlayerMinuteStaysAtTheBaseline_8Humans() => CompareArms(8, 900);

        [Test]
        public void CostPerPlayerMinuteStaysAtTheBaseline_32Humans() => CompareArms(32, 450);

        [Test]
        public void TheFreeTierInMatchesADayByRoomSize()
        {
            foreach (int h in new[] { 2, 4, 8, 16, 32 })
            {
                int before = RelayBudget.MatchesPerDay(h, true), after = RelayBudget.MatchesPerDay(h);
                Console.WriteLine($"[zero-cost] estimate, {h} humans, 8-minute match + 2-minute lobby: {RelayBudget.RequestsPerMatch(h, RelayBudget.MatchMessagesPerSecond(h, true)):F0} -> " +
                    $"{RelayBudget.RequestsPerMatch(h, RelayBudget.MatchMessagesPerSecond(h)):F0} requests, {before} -> {after} matches a day (100,000 requests, 20 messages each)");
                Assert.Greater(after, before);
            }
            Console.WriteLine("[zero-cost] host line, 8 humans, 30,000 used: " + RelayBudget.HostLine(8, 30000));
            Console.WriteLine("[zero-cost] host line, 8 humans, 99,000 used: " + RelayBudget.HostLine(8, 99000));
            StringAssert.Contains("can still join", RelayBudget.HostLine(8, 100000));
        }

        // ------------------------------------------------------------------------------------------------ no relay at all

        [Test]
        public void DirectPlayOverTcpMatchesAndUsesNoRelay()
        {
            Peer host = Make(null, true, "Host", p => { p.NoRelay = true; p.DirectPort = 0; p.Ticks = 450; p.Pace = 3; });
            Begin(host);
            Assert.IsNull(host.Exit, All(host));
            Assert.Greater(host.P.DirectPortBound, 0);
            Peer join = Make(null, false, "Friend", p => { p.DirectAddr = "127.0.0.1:" + host.P.DirectPortBound; p.Ticks = 450; p.Pace = 3; p.Team = TeamId.Crimson; });
            Begin(join);
            RunReal(null, new List<Peer> { host, join }, 90000);
            Assert.AreEqual(0, host.Exit, All(host));
            Assert.AreEqual(0, join.Exit, All(join));
            int n = AssertSame(host, join, 450 / 30 - 2);
            Assert.AreEqual("direct", host.P.PathUsed);
            Assert.AreEqual("direct", join.P.PathUsed);
            Console.WriteLine($"[zero-cost] direct TCP 127.0.0.1:{host.P.DirectPortBound}: {join.P.Session.Stepped} ticks, {n} fingerprints MATCH, joiner sent " +
                $"{join.P.Meter.Sent.TotalMessages} msg {join.P.Meter.Sent.TotalBytes} B, relay messages 0 (no relay in this room)");
        }

        [Test]
        public void ARoomTakesRelayAndDirectPlayersTogetherAndTheRelayOnlySeesItsOwn()
        {
            var relay = new LocalRelay(25);
            Peer host = Make(relay, true, "Host", p => { p.DirectPort = 0; p.Humans = 3; p.Ticks = 450; p.Pace = 3; });
            Begin(host);
            Peer viaRelay = Make(relay, false, "Remote", p => { p.Code = host.P.Code; p.Ticks = 450; p.Pace = 3; p.Team = TeamId.Crimson; });
            Peer direct = Make(relay, false, "Couch", p => { p.DirectAddr = "127.0.0.1:" + host.P.DirectPortBound; p.Ticks = 450; p.Pace = 3; });
            Begin(viaRelay);
            Begin(direct);
            RunReal(relay, new List<Peer> { host, viaRelay, direct }, 120000);
            foreach (Peer p in new[] { host, viaRelay, direct }) Assert.AreEqual(0, p.Exit, All(p));
            AssertSame(host, viaRelay, 450 / 30 - 2);
            AssertSame(host, direct, 450 / 30 - 2);
            AssertSame(viaRelay, direct, 450 / 30 - 2);
            Assert.AreEqual("relay+direct", host.P.PathUsed);
            Assert.AreEqual("relay", viaRelay.P.PathUsed);
            Assert.AreEqual("direct", direct.P.PathUsed);
            Console.WriteLine($"[zero-cost] mixed room: host relay leg sent {host.P.Meter.Sent.TotalMessages} msg, remote joiner {viaRelay.P.Meter.Sent.TotalMessages} msg (the relay's whole bill), " +
                $"couch joiner {direct.P.Meter.Sent.TotalMessages} msg direct (free); all three MATCH");
        }

        [Test]
        public void AJoinerWhoCannotReachTheHostDirectlyFallsBackToTheRelayRoom()
        {
            var relay = new LocalRelay(25);
            Peer host = Make(relay, true, "Host", p => { p.Ticks = 300; p.Pace = 3; });
            Begin(host);
            int closed = FreePort();
            Peer join = Make(relay, false, "Friend", p => { p.Code = host.P.Code; p.DirectAddr = "127.0.0.1:" + closed; p.Ticks = 300; p.Pace = 3; p.Team = TeamId.Crimson; });
            Begin(join);
            RunReal(relay, new List<Peer> { host, join }, 90000);
            Assert.AreEqual(0, host.Exit, All(host));
            Assert.AreEqual(0, join.Exit, All(join));
            AssertSame(host, join, 300 / 30 - 2);
            Assert.AreEqual("relay", join.P.PathUsed, "nothing listens on the direct port, so the relay room carried the match");
            Console.WriteLine($"[zero-cost] fallback: direct 127.0.0.1:{closed} refused, joined room {host.P.Code} by the relay, MATCH");
        }

        /// <summary>A relay leg that can be cut (the relay over its daily limit, or gone): Closed once, then silence.</summary>
        private sealed class Cuttable : ITransport, ILockableTransport
        {
            public ITransport Inner;
            public bool Cut;
            private bool _told;
            public int LocalPeer => Inner.LocalPeer;
            public bool Reliable => true;
            public void Send(int to, byte[] data, int length) { if (!Cut) Inner.Send(to, data, length); }
            public bool Poll(out TransportEvent ev)
            {
                if (Cut)
                {
                    ev = new TransportEvent { Kind = TransportEventKind.Closed, From = -1, Reason = "The connection to the room was lost." };
                    if (_told) return false;
                    _told = true;
                    return true;
                }
                return Inner.Poll(out ev);
            }
            public void Lock() { if (!Cut) (Inner as ILockableTransport)?.Lock(); }
            public void Close() => Inner.Close();
        }

        [Test]
        public void WhenTheRelayDiesMidMatchDirectPlayersPlayOnAndRelayPlayersBecomeBots()
        {
            // The host's own room parts, assembled by hand so the relay leg can be cut at tick 240.
            var relay = new LocalRelay(25);
            (string code, string key) = relay.CreateRoom();
            var leg = new Cuttable { Inner = relay.Connect(code, key) };
            DirectHost dh = DirectHost.Start(0, false);
            var mux = new HostMux(leg, dh);
            var room = new RoomHost(mux, GameConfig.CreateDefault(), "Host", "headless") { TeamSize = 16, Pace = 3 };
            Peer remote = Make(relay, false, "Remote", p => { p.Code = code; p.Pace = 3; p.Team = TeamId.Crimson; p.TimeoutSeconds = 60; });
            Peer couch = Make(null, false, "Couch", p => { p.DirectAddr = "127.0.0.1:" + dh.Port; p.Pace = 3; p.Ticks = 600; });
            Begin(remote);
            Begin(couch);
            LockstepSession s = null;
            var hostHashes = new Dictionary<int, ulong>();
            int cutAt = -1, botTick = -1;
            var pilot = new AutoplayPilot(101);
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 120000 && !(couch.Exit.HasValue && (s == null || s.Stepped >= 640)))
            {
                long now = clock.ElapsedMilliseconds;
                relay.Advance(now);
                room.Update(now);
                if (s == null && room.Members.Count >= 3 && room.CanStart(out _))
                {
                    s = room.Start(now, null, 4242);
                    s.Sampler = pilot.Fill;
                    s.HashRecorded = (t, h) => hostHashes[t] = h;
                    s.HumanToBot += (slot, actor, t) => { if (s.Setup.Humans[slot].Peer < DirectHost.FirstPeer) botTick = t; };
                }
                if (s != null && cutAt < 0 && s.Stepped >= 240) { leg.Cut = true; cutAt = s.Stepped; }
                StepOnce(remote, now);
                StepOnce(couch, now);
                Thread.Sleep(1);
            }
            Assert.IsNotNull(s, "the match started");
            Assert.AreEqual(LockstepStatus.Running, s.Status, "the host's match goes on");
            Assert.AreEqual(0, couch.Exit, All(couch));
            Assert.Greater(botTick, cutAt - 1, "the relay player's fighter went to a bot after the cut");
            int n = 0;
            foreach (KeyValuePair<int, ulong> kv in couch.P.Hashes)
                if (hostHashes.TryGetValue(kv.Key, out ulong h)) { Assert.AreEqual(h.ToString("X16"), kv.Value.ToString("X16"), "host and couch differ at tick " + kv.Key); n++; }
            Assert.GreaterOrEqual(n, 15);
            Console.WriteLine($"[zero-cost] relay cut at tick {cutAt}: the relay player became a bot at tick {botTick} on the host; the couch player (direct) played on to tick " +
                $"{couch.P.Session.Stepped}, {n} fingerprints MATCH the host; relay lost: \"{mux.RelayLostReason}\"; remote player: {Last(remote)}");
            mux.Close();
        }

        [Test]
        public void ALocalNetworkRoomIsHeardAndFoundByItsCode()
        {
            LanListener listener = null;
            int port = 0;
            for (int p = 47861; p < 47871 && listener == null; p++) { listener = LanListener.TryStart(p); port = p; }
            Assert.IsNotNull(listener, "a free UDP port for the test");
            using (listener)
            using (new LanBeacon(47800, "K7Q2PF", "Wilson", 3, "headless", port, new[] { new IPEndPoint(IPAddress.Loopback, port) }, false))
            {
                var clock = Stopwatch.StartNew();
                LanGame g = default;
                while (clock.ElapsedMilliseconds < 5000 && !listener.TryFind("K7Q2PF", out g)) Thread.Sleep(20);
                Assert.AreEqual("K7Q2PF", g.Code, "the beacon was heard");
                Assert.AreEqual(47800, g.Port);
                Assert.AreEqual("WILSON", g.Name, "names travel cleaned, as in the room");
                Assert.AreEqual(3, g.Humans);
                Console.WriteLine($"[zero-cost] local network: heard room {g.Code} from {g.Name} at {g.Address}:{g.Port} after {clock.ElapsedMilliseconds} ms");
            }
        }

        [Test]
        public void TypedAddressesAreReadPlainly()
        {
            Assert.IsTrue(DirectAddress.TryParse("192.168.1.5", out string h, out int p));
            Assert.AreEqual("192.168.1.5", h);
            Assert.AreEqual(DirectHost.DefaultPort, p);
            Assert.IsTrue(DirectAddress.TryParse(" 10.0.0.7:5000 ", out h, out p));
            Assert.AreEqual("10.0.0.7", h);
            Assert.AreEqual(5000, p);
            Assert.IsTrue(DirectAddress.TryParse("wilsons-mac.local:47800", out h, out p));
            Assert.IsFalse(DirectAddress.TryParse("K7Q2PF:99999", out _, out _));
            Assert.IsFalse(DirectAddress.TryParse("1.2.3", out _, out _));
            Assert.IsFalse(DirectAddress.TryParse("300.1.1.1", out _, out _));
            Assert.IsFalse(DirectAddress.TryParse("a b", out _, out _));
            Console.WriteLine("[zero-cost] addresses: 192.168.1.5, 10.0.0.7:5000 and a .local name read; 1.2.3, 300.1.1.1, a bad port and spaces refused; this computer: " +
                string.Join(", ", DirectAddress.LocalAddresses()));
        }
    }
}
