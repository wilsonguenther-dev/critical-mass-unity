using System.Collections.Generic;
using CriticalMass.Net;
using CriticalMass.Net.Relay;
using CriticalMass.Sim.Config;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests.Net
{
    /// <summary>
    /// b11-b: team chat arrival on the host, through LocalRelay (server/relay's rules, no sockets, no relay requests).
    /// A host and two joiners, one joiner on the host's team and one on the other: a TEAM line from the teammate lands in
    /// the host's ChatChannel.Lines with Scope Team, the enemy never gets it, an ALL line lands on all three, a joiner who
    /// changes team in the lobby is routed by the new team, and a connection the room refused is routed nowhere (it used
    /// to count as Azure). The host sits on Crimson in most rigs, so a lookup that fell back to the old Azure default
    /// would show up as a missing line. Chat never enters the lockstep inputs: no checksum row can move.
    /// </summary>
    public sealed class TeamChatRelayTests
    {
        /// <summary>One room: a host and two joiners (plus an optional stranger) on one LocalRelay, one clock.</summary>
        private sealed class Rig
        {
            public readonly LocalRelay Relay = new LocalRelay(20);
            public readonly RoomHost Host;
            public readonly RoomClient Mate, Foe;
            public RoomClient Stranger;
            public long Now;

            /// <summary>The host takes hostTeam; Mate (TRAVIS) joins the host's team, Foe (JEISIL) the other one.</summary>
            public Rig(TeamId hostTeam, bool withStranger = false)
            {
                GameConfig cfg = GameConfig.CreateDefault();
                (string code, string key) = Relay.CreateRoom();
                Host = new RoomHost(Relay.Connect(code, key), cfg, "Wilson", "b") { TeamSize = 4, Pace = 1 };
                Run(200);   // the host's connection is through before anyone joins (a joiner first hears "not ready")
                Host.SetLocalPick(hostTeam, ClassId.Warrior);
                Mate = new RoomClient(Relay.Connect(code, null), cfg, "Travis", "b");
                Foe = new RoomClient(Relay.Connect(code, null), cfg, "Jeisil", "b");
                // A joiner on another build: the host refuses it and drops it from the room, but its connection stays open.
                if (withStranger) Stranger = new RoomClient(Relay.Connect(code, null), cfg, "Stranger", "other-build");
                Run(1500);
                Mate.SetPick(hostTeam, ClassId.Builder);
                Foe.SetPick(Other(hostTeam), ClassId.Miner);
                Mate.SetReady(true);
                Foe.SetReady(true);
                Run(1500);
                Assert.AreEqual(hostTeam, Host.Find(Mate.LocalPeer).Team, "the mate sits on the host's team");
                Assert.AreEqual(Other(hostTeam), Host.Find(Foe.LocalPeer).Team, "the foe sits on the other team");
            }

            public void Run(long ms)
            {
                for (long end = Now + ms; Now < end; Now += 5)
                {
                    Relay.Advance(Now);
                    Host.Update(Now);
                    Mate?.Update(Now);
                    Foe?.Update(Now);
                    Stranger?.Update(Now);
                }
            }
        }

        private static TeamId Other(TeamId t) => t == TeamId.Azure ? TeamId.Crimson : TeamId.Azure;

        private static bool Find(ChatChannel ch, string text, out ChatLine line)
        {
            foreach (ChatLine l in ch.Lines)
                if (l.Scope != ChatScope.System && l.Text == text) { line = l; return true; }
            line = default;
            return false;
        }

        private static bool Has(ChatChannel ch, string text) => Find(ch, text, out _);

        private static string Dump(ChatChannel ch)
        {
            var parts = new List<string>();
            foreach (ChatLine l in ch.Lines) parts.Add(l.Scope + " " + l.From + ": " + l.Text);
            return string.Join(" | ", parts);
        }

        [Test]
        public void ASameTeamJoinersTeamLineLandsInTheHostsLinesWithScopeTeam()
        {
            var rig = new Rig(TeamId.Crimson);
            Assert.IsNull(rig.Mate.Chat.Say("crimson push the hill", ChatScope.Team, rig.Now));
            rig.Run(500);
            Assert.IsTrue(Find(rig.Host.Chat, "crimson push the hill", out ChatLine got), "the host has the line: " + Dump(rig.Host.Chat));
            Assert.AreEqual(ChatScope.Team, got.Scope);
            Assert.AreEqual("TRAVIS", got.From, "named by the host's member list, never by the sender");
            Assert.AreEqual(rig.Mate.LocalPeer, got.FromPeer);
            Assert.IsTrue(Find(rig.Mate.Chat, "crimson push the hill", out ChatLine echo) && echo.Scope == ChatScope.Team, "the sender sees its own line come back");
        }

        [Test]
        public void TheEnemyJoinerNeverGetsAnotherTeamsLine()
        {
            var rig = new Rig(TeamId.Crimson);
            rig.Mate.Chat.Say("flank left", ChatScope.Team, rig.Now);
            rig.Host.Chat.Say("hold the gate", ChatScope.Team, rig.Now);
            rig.Foe.Chat.Say("azure only", ChatScope.Team, rig.Now);
            rig.Run(500);
            Assert.IsTrue(Has(rig.Host.Chat, "flank left") && Has(rig.Mate.Chat, "hold the gate"), "teammates hear each other");
            Assert.IsFalse(Has(rig.Foe.Chat, "flank left"), "the enemy never gets the joiner's team line: " + Dump(rig.Foe.Chat));
            Assert.IsFalse(Has(rig.Foe.Chat, "hold the gate"), "nor the host's");
            Assert.IsTrue(Has(rig.Foe.Chat, "azure only"), "the enemy's own team line comes back to it");
            Assert.IsFalse(Has(rig.Host.Chat, "azure only") || Has(rig.Mate.Chat, "azure only"), "and goes no further");
        }

        [Test]
        public void AnAllLineLandsOnAllThree()
        {
            var rig = new Rig(TeamId.Crimson);
            rig.Foe.Chat.Say("good luck all", ChatScope.All, rig.Now);
            rig.Mate.Chat.Say("have fun", ChatScope.All, rig.Now);
            rig.Host.Chat.Say("glhf", ChatScope.All, rig.Now);
            rig.Run(500);
            foreach (string text in new[] { "good luck all", "have fun", "glhf" })
                foreach (ChatChannel ch in new[] { rig.Host.Chat, rig.Mate.Chat, rig.Foe.Chat })
                {
                    Assert.IsTrue(Find(ch, text, out ChatLine l), "'" + text + "' everywhere: " + Dump(ch));
                    Assert.AreEqual(ChatScope.All, l.Scope);
                }
        }

        [Test]
        public void AJoinerWhoseTeamChangedInTheLobbyIsRoutedByItsNewTeam()
        {
            var rig = new Rig(TeamId.Azure);
            // Foe started on Crimson: its team line does not reach the Azure host.
            rig.Foe.Chat.Say("first on crimson", ChatScope.Team, rig.Now);
            rig.Run(ChatChannel.RateMs);
            Assert.IsFalse(Has(rig.Host.Chat, "first on crimson"));
            // It moves to the host's team in the lobby: now the host and the mate get its team lines.
            rig.Foe.SetPick(TeamId.Azure, ClassId.Miner);
            rig.Run(500);
            Assert.AreEqual(TeamId.Azure, rig.Host.Find(rig.Foe.LocalPeer).Team);
            rig.Foe.Chat.Say("now on azure", ChatScope.Team, rig.Now);
            rig.Run(ChatChannel.RateMs);
            Assert.IsTrue(Find(rig.Host.Chat, "now on azure", out ChatLine l) && l.Scope == ChatScope.Team, "routed by the new team: " + Dump(rig.Host.Chat));
            Assert.IsTrue(Has(rig.Mate.Chat, "now on azure"));
            // The mate moves away to Crimson: the foe's next team line no longer reaches it, the host still gets it.
            rig.Mate.SetPick(TeamId.Crimson, ClassId.Builder);
            rig.Run(500);
            Assert.AreEqual(TeamId.Crimson, rig.Host.Find(rig.Mate.LocalPeer).Team);
            rig.Foe.Chat.Say("mate left us", ChatScope.Team, rig.Now);
            rig.Mate.Chat.Say("mate on crimson", ChatScope.Team, rig.Now);
            rig.Run(ChatChannel.RateMs);
            Assert.IsTrue(Has(rig.Host.Chat, "mate left us") && Has(rig.Foe.Chat, "mate left us"));
            Assert.IsFalse(Has(rig.Mate.Chat, "mate left us"), "the moved mate no longer hears its old team");
            Assert.IsFalse(Has(rig.Host.Chat, "mate on crimson") || Has(rig.Foe.Chat, "mate on crimson"), "nor do its old teammates hear it");
            Assert.IsTrue(Has(rig.Mate.Chat, "mate on crimson"));
        }

        [Test]
        public void AConnectionTheRoomRefusedIsRoutedNowhereInTheLobbyOrTheMatch()
        {
            var rig = new Rig(TeamId.Crimson, withStranger: true);
            Assert.AreEqual(RoomPhase.Closed, rig.Stranger.Phase, "the other build was refused");
            Assert.IsNull(rig.Host.Find(rig.Stranger.LocalPeer), "and is not a member");
            rig.Stranger.Chat.Say("stranger lobby all", ChatScope.All, rig.Now);
            rig.Run(500);
            LockstepSession hs = rig.Host.Start(rig.Now);
            Assert.IsNotNull(hs, "the match starts without the refused peer");
            rig.Run(1500);
            // In the match every chat packet goes through the session to the room's channel: the stranger is still
            // unknown there (it used to be routed as an Azure player named PLAYER).
            rig.Stranger.Chat.Say("stranger match team", ChatScope.Team, rig.Now);
            rig.Stranger.Chat.Say("stranger match all", ChatScope.All, rig.Now);
            rig.Mate.Chat.Say("mate in match", ChatScope.Team, rig.Now);
            rig.Run(1000);
            foreach (ChatChannel ch in new[] { rig.Host.Chat, rig.Mate.Chat, rig.Foe.Chat })
            {
                Assert.IsFalse(Has(ch, "stranger lobby all"), Dump(ch));
                Assert.IsFalse(Has(ch, "stranger match team"), Dump(ch));
                Assert.IsFalse(Has(ch, "stranger match all"), Dump(ch));
            }
            Assert.IsTrue(Find(rig.Host.Chat, "mate in match", out ChatLine l) && l.Scope == ChatScope.Team, "a member's team line still lands in the match: " + Dump(rig.Host.Chat));
            Assert.IsFalse(Has(rig.Foe.Chat, "mate in match"));
        }

        // ---- the game's own online path: whole HeadlessPeers (the hidden test mode's code) through the same relay ----

        private sealed class Peer
        {
            public HeadlessPeer P;
            public readonly List<string> Log = new List<string>();
            public int? Exit;
        }

        private static Peer Make(LocalRelay relay, bool host, string name, System.Action<HeadlessPeer> tweak)
        {
            var peer = new Peer();
            peer.P = new HeadlessPeer { Local = relay, Host = host, Name = name, TeamSize = 8, Pace = 1, Seed = 777, JoinRetryMs = 300, LobbySeconds = 60, Ticks = 300 };
            peer.P.Log = s => peer.Log.Add(s);
            tweak?.Invoke(peer.P);
            return peer;
        }

        [Test]
        public void HeadlessPeersCarryAJoinersTeamLineToTheHostAndAnAllLineToEveryone()
        {
            var relay = new LocalRelay(30);
            Peer host = Make(relay, true, "Wilson", p => { p.Humans = 3; p.Team = TeamId.Crimson; });
            Assert.IsTrue(host.P.Begin(0), string.Join("\n", host.Log));
            for (long t = 0; t <= 300; t += 5) { relay.Advance(t); host.P.Step(t); }
            Assert.AreEqual(Peers.Host, host.P.LocalPeer, "the host is through before the joiners ask");
            Peer mate = Make(relay, false, "Travis", p => { p.Code = host.P.Code; p.Team = TeamId.Crimson; p.ChatAt = 60; p.ChatTeam = true; p.ChatText = "crimson with the host"; });
            Peer foe = Make(relay, false, "Jeisil", p => { p.Code = host.P.Code; p.Team = TeamId.Azure; p.ChatAt = 90; p.ChatText = "hello everyone"; });
            Assert.IsTrue(mate.P.Begin(300) && foe.P.Begin(300));
            var peers = new[] { host, mate, foe };
            bool all = false;
            for (long now = 305; now < 120000 && !all; now += 5)
            {
                relay.Advance(now);
                all = true;
                foreach (Peer p in peers)
                {
                    if (p.Exit.HasValue) continue;
                    p.Exit = p.P.Step(now);
                    if (!p.Exit.HasValue) all = false;
                }
            }
            Assert.IsTrue(all, "the peers finished");
            foreach (Peer p in peers) Assert.AreEqual(0, p.Exit, string.Join("\n", p.Log));
            Assert.IsTrue(mate.Log.Exists(l => l.Contains("ONLINE CHAT SENT TEAM")), string.Join("\n", mate.Log));

            Assert.IsTrue(Find(host.P.Chat, "crimson with the host", out ChatLine got), "the host got the teammate's line: " + Dump(host.P.Chat));
            Assert.AreEqual(ChatScope.Team, got.Scope);
            Assert.AreEqual("TRAVIS", got.From);
            Assert.AreEqual(mate.P.LocalPeer, got.FromPeer);
            Assert.IsTrue(Has(mate.P.Chat, "crimson with the host"));
            Assert.IsFalse(Has(foe.P.Chat, "crimson with the host"), "the enemy never gets it: " + Dump(foe.P.Chat));
            Assert.IsFalse(foe.Log.Exists(l => l.Contains("crimson with the host")), "not in the enemy's log either");

            foreach (Peer p in peers)
            {
                Assert.IsTrue(Find(p.P.Chat, "hello everyone", out ChatLine a), p.P.Name + " got the ALL line: " + Dump(p.P.Chat));
                Assert.AreEqual(ChatScope.All, a.Scope);
            }
        }
    }
}
