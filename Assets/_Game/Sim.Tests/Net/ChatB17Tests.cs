using System;
using System.Collections.Generic;
using CriticalMass.Net;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests.Net
{
    /// <summary>
    /// b17-chat: the chat wire (type 31), its versions, the filter switch, rate limits (sender, host room budget), tag
    /// escaping, mute all, the chat switch, callouts, bots never chatting, voice frames, and the proof that none of it
    /// reaches the match: the same match with and without heavy chat and voice traffic has the same fingerprints and the
    /// same lockstep traffic, per rules profile, and the offline goldens are the base branch's pins.
    /// </summary>
    public sealed class ChatB17Tests
    {
        // ---- framing and versions -----------------------------------------------------------------------------------

        [Test]
        public void TheV1LineRoundTripsAndTheB11LineIsStillRead()
        {
            var buf = new byte[Peers.MaxPacket];
            int n = ChatWire.Write(buf, ChatScope.Team, 7, 1, -1, "TRAVIS", "push the hill");
            Assert.Greater(n, 0);
            Assert.AreEqual(ChatWire.Msg, buf[0]);
            Assert.IsTrue(ChatWire.Read(buf, n, out ChatWire.Packet p));
            Assert.AreEqual(1, p.Ver);
            Assert.AreEqual(ChatScope.Team, p.Scope);
            Assert.AreEqual(7, p.From);
            Assert.AreEqual(1, p.Team);
            Assert.AreEqual(-1, p.Callout);
            Assert.AreEqual("TRAVIS", p.Name);
            Assert.AreEqual("push the hill", p.Text);

            n = ChatWire.Write(buf, ChatScope.Team, 3, 0, 2, "JEISIL", "");
            Assert.IsTrue(ChatWire.Read(buf, n, out p));
            Assert.AreEqual(2, p.Callout, "a callout travels as its id");

            // The b11 layout (type 30): [30][scope][from][name][text].
            var w = new WireWriter(buf);
            w.U8(30); w.U8(0); w.U8(4); w.Str("WILSON", 16); w.Str("old line", 120);
            Assert.IsTrue(ChatWire.Read(buf, w.Pos, out p));
            Assert.AreEqual(0, p.Ver);
            Assert.AreEqual("old line", p.Text);
            Assert.AreEqual(-1, p.Team);
        }

        [Test]
        public void ALaterVersionMayOnlyAppendAndBadPacketsAreDropped()
        {
            var buf = new byte[Peers.MaxPacket];
            int n = ChatWire.Write(buf, ChatScope.All, 2, 0, -1, "A", "hi");
            buf[1] = 2;                       // a future version ...
            buf[n] = 0xAB; buf[n + 1] = 0xCD; // ... with two more bytes after ver 1's fields
            Assert.IsTrue(ChatWire.Read(buf, n + 2, out ChatWire.Packet p), "ver 2 is read as ver 1 plus ignored bytes");
            Assert.AreEqual("hi", p.Text);
            buf[1] = 0;
            Assert.IsFalse(ChatWire.Read(buf, n, out _), "ver 0 is invalid");
            buf[1] = 1;
            Assert.IsFalse(ChatWire.Read(buf, n - 1, out _), "a cut packet is dropped");
            n = ChatWire.Write(buf, ChatScope.All, 2, 0, QuickChat.Count + 3, "A", "");
            Assert.IsFalse(ChatWire.Read(buf, n, out _), "an unknown callout id is dropped");
            buf[0] = 33;
            Assert.IsFalse(ChatWire.Read(buf, n, out _), "reserved chat types are not lines");
            for (int t = 30; t < 40; t++) Assert.IsTrue(ChatWire.Owns(t));
            Assert.IsFalse(ChatWire.Owns(29) || ChatWire.Owns(40) || ChatWire.Owns(1) || ChatWire.Owns(2));
        }

        /// <summary>
        /// Old-client compatibility, in both directions: a game that does not know a chat-block type ignores it (no line, no
        /// crash, the room goes on: the rule a pre-b17 game applies to type 31), and this game still shows a b11 host's
        /// type-30 line.
        /// </summary>
        [Test]
        public void UnknownChatTypesAreIgnoredAndAnOldHostsLineStillShows()
        {
            var net = new LoopbackNetwork(5, 20, 0, 0);
            LoopbackTransport oldHost = net.CreatePeer();
            var client = new RoomClient(net.CreatePeer(), GameConfig.CreateDefault(), "Travis", "b");
            long now = 0;
            for (; now < 300; now += 5) { net.Advance(now); client.Update(now); while (oldHost.Poll(out _)) { } }
            var buf = new byte[64];
            for (int t = 32; t < 40; t++)
            {
                if (t == ChatWire.Voice) continue;
                buf[0] = (byte)t; buf[1] = 1; buf[2] = 0; buf[3] = 0;
                oldHost.Send(client.LocalPeer, buf, 8);
            }
            var w = new WireWriter(buf);
            w.U8(30); w.U8(0); w.U8(0); w.Str("WILSON", 16); w.Str("hello from b11", 120);
            oldHost.Send(client.LocalPeer, buf, w.Pos);
            for (long end = now + 300; now < end; now += 5) { net.Advance(now); client.Update(now); while (oldHost.Poll(out _)) { } }
            Assert.AreEqual(1, client.Chat.Lines.Count, Dump(client.Chat));
            Assert.AreEqual("hello from b11", client.Chat.Lines[0].Text);
            Assert.AreNotEqual(RoomPhase.Closed, client.Phase, "unknown types never close the room");
        }

        // ---- filter, escaping, switches -----------------------------------------------------------------------------

        [Test]
        public void TheWordFilterIsEachPlayersSwitchAndOnByDefault()
        {
            Assert.AreEqual("you are a b1tch", ChatFilter.Transport("you are a b1tch"), "the host forwards words as typed");
            Assert.AreEqual("join [link removed] now", ChatFilter.Transport("join www.example.com now"), "links never travel");
            Assert.AreEqual("you are a *****", ChatFilter.MaskWords("you are a b1tch"));
            var ch = new ChatChannel(new NullTransport(0));
            Assert.IsTrue(ch.FilterWords, "on by default");
            var line = new ChatLine { FromPeer = 1, From = "X", Scope = ChatScope.All, Text = "you are a b1tch", Team = 0, Callout = -1 };
            Assert.AreEqual("you are a *****", ch.Shown(line));
            ch.FilterWords = false;
            Assert.AreEqual("you are a b1tch", ch.Shown(line));
            Assert.AreEqual(ChatFilter.MaxLength, ChatFilter.Transport(new string('a', 400)).Length, "max length");
        }

        [Test]
        public void TypedTagsNeverBecomeRichTextAndNamesAreColouredOnlyByTheGame()
        {
            string evil = "<color=red>x</color><size=300>BIG</size><b><noparse></noparse><link=\"http://x\">y</link><sprite=1>";
            string esc = ChatText.EscapeRich(evil);
            Assert.AreEqual(-1, esc.IndexOf('<'));
            Assert.AreEqual(-1, esc.IndexOf('>'));
            var l = new ChatLine { From = ChatFilter.CleanName("<b>EVIL</b>"), Scope = ChatScope.Team, Text = evil, Team = 1, Callout = -1 };
            string rich = ChatText.Rich(l, "FF7276");
            Assert.AreEqual(2, Count(rich, '<'), "only the game's own colour tag pair: " + rich);
            StringAssert.StartsWith("[TEAM] <color=#FF7276>", rich);
            Assert.AreEqual(0, Count(ChatText.Rich(l, "zz<b>"), '<'), "a bad colour adds no tag");
            Assert.AreEqual("EVIL", ChatFilter.CleanName("<<EVIL>>"), "names keep letters, digits, space, - and _ only");
        }

        [Test]
        public void MuteAllAndTheChatSwitchHideLinesButNotTheRoomsNotices()
        {
            var r = Rig3();
            r.Mate.Chat.SetMuteAll(true);
            Assert.IsNull(r.Host.Chat.Say("can you hear me", ChatScope.All, r.Now));
            r.Run(400);
            Assert.IsFalse(Has(r.Mate.Chat, "can you hear me"));
            Assert.IsNull(r.Mate.Chat.Say("I still talk", ChatScope.All, r.Now));
            r.Run(400);
            Assert.IsTrue(Has(r.Mate.Chat, "I still talk"), "your own lines still show with mute all");
            Assert.IsTrue(Has(r.Foe.Chat, "I still talk"));
            r.Mate.Chat.SetMuteAll(false);
            r.Foe.Chat.Enabled = false;
            Assert.IsNotNull(r.Foe.Chat.Say("chat is off", ChatScope.All, r.Now), "chat off: nothing is sent");
            r.Foe.Chat.Notice("Jeisil joined.", r.Now);
            Assert.IsTrue(Has(r.Foe.Chat, "Jeisil joined."), "the room's notices still show");
            Assert.IsNull(r.Host.Chat.Say("anyone", ChatScope.All, r.Now + 2000));
            r.Run(2400);
            Assert.IsFalse(Has(r.Foe.Chat, "anyone"), "chat off: nobody's lines show");
            Assert.IsTrue(Has(r.Mate.Chat, "anyone"));
        }

        [Test]
        public void TheGameShipsTypedChatOffAndCalloutsOnAndAReportMutesAndKeepsTheEvidence()
        {
            Assert.IsFalse(ChatDefaults.TypedText, "typed free text ships off (players may be minors)");
            Assert.IsTrue(ChatDefaults.Callouts && ChatDefaults.Chat && ChatDefaults.WordFilter);
            Assert.IsFalse(ChatDefaults.Voice);
            var r = Rig3();
            r.Foe.Chat.TypedText = false;
            Assert.IsNotNull(r.Foe.Chat.Say("typed", ChatScope.All, r.Now), "typed chat off: nothing typed is sent");
            Assert.IsNull(r.Mate.Chat.Say("hello all", ChatScope.All, r.Now));
            Assert.IsNull(r.Mate.Chat.Callout(7, r.Now));
            r.Run(400);
            Assert.IsFalse(Has(r.Foe.Chat, "hello all"), "nor shown");
            Assert.IsTrue(Has(r.Foe.Chat, "Good game"), "quick chat still works");
            Assert.IsTrue(Has(r.Host.Chat, "hello all"));

            string rep = r.Host.Chat.Report(r.Mate.LocalPeer, r.Now, "2026-11-21T20:00:00Z");
            Assert.IsNotNull(rep);
            StringAssert.Contains("TRAVIS", rep);
            StringAssert.Contains("hello all", rep, "the report keeps their lines as evidence");
            Assert.IsTrue(r.Host.Chat.IsMuted(r.Mate.LocalPeer), "a report mutes at once");
            Assert.IsNull(r.Host.Chat.Report(r.Host.Chat.Voice == null ? -1 : 0, r.Now, ""), "nobody reports themselves");
            Assert.IsNull(r.Mate.Chat.Say("after the report", ChatScope.All, r.Now + 2000));
            r.Run(2400);
            Assert.IsFalse(Has(r.Host.Chat, "after the report"));
        }

        // ---- rate limits ----------------------------------------------------------------------------------------------

        [Test]
        public void TheSenderChecksItsOwnRateSoAStockGameNeverSpendsTheRelayOnADroppedLine()
        {
            var r = Rig3();
            long before = r.Foe.Chat.Sent;
            int refused = 0;
            for (int i = 0; i < 10; i++) if (r.Foe.Chat.Say("spam " + i, ChatScope.All, r.Now) != null) refused++;
            Assert.AreEqual(10 - ChatChannel.Burst, refused);
            Assert.AreEqual(ChatChannel.Burst, r.Foe.Chat.Sent - before, "only the burst left this computer");
            Assert.IsNull(r.Foe.Chat.Say("after a wait", ChatScope.All, r.Now + ChatChannel.RateMs), "one more after RateMs");
        }

        [Test]
        public void TheHostsRoomBudgetCapsChatSendsForTheWholeRoom()
        {
            // A host alone with 31 teammates, every one of them flooding team lines: the host spends at most its room budget.
            var t = new NullTransport(0);
            var peers = new List<int>();
            for (int p = 0; p < 32; p++) peers.Add(p);
            var ch = new ChatChannel(t) { NameOf = p => "P" + p, TeamOf = p => 0, Knows = p => true, PeersInRoom = () => peers };
            var buf = new byte[Peers.MaxPacket];
            long now = 0;
            for (; now <= 60000; now += 50)
                for (int p = 1; p < 32; p++)
                {
                    int n = ChatWire.Write(buf, ChatScope.Team, 0, 0, -1, "", "flood " + now);
                    var ev = new TransportEvent { Kind = TransportEventKind.Data, From = p, Data = buf, Length = n };
                    ch.Handle(ref ev, now);
                }
            double cap = ChatChannel.RoomBurst + ChatChannel.RoomSendsPerSecond * 60.0 + 31;   // one line may straddle the edge
            Console.WriteLine("{\"item\":\"b17-chat room budget\",\"host_sends_per_minute\":" + t.Sends + ",\"cap\":" + cap + "}");
            Assert.LessOrEqual(t.Sends, cap);
            Assert.Greater(t.Sends, ChatChannel.RoomSendsPerSecond * 60 * 0.8, "the budget is used, not starved");
        }

        // ---- callouts, bots ------------------------------------------------------------------------------------------

        [Test]
        public void CalloutsReachTheirScopeAsTheSameWordsEverywhere()
        {
            var r = Rig3();
            Assert.IsNull(r.Mate.Chat.Callout(2, r.Now));   // "Need a Healer", team
            r.Run(400);
            Assert.IsTrue(Find(r.Host.Chat, QuickChat.Text[2], out ChatLine got));
            Assert.AreEqual(2, got.Callout);
            Assert.AreEqual(ChatScope.Team, got.Scope);
            Assert.AreEqual((int)TeamId.Azure, got.Team);
            Assert.IsTrue(Has(r.Mate.Chat, QuickChat.Text[2]));
            Assert.IsFalse(Has(r.Foe.Chat, QuickChat.Text[2]), "a team callout stays on the team");
            Assert.IsNull(r.Foe.Chat.Callout(7, r.Now));     // "Good game", everyone
            r.Run(400);
            Assert.IsTrue(Has(r.Host.Chat, "Good game") && Has(r.Mate.Chat, "Good game") && Has(r.Foe.Chat, "Good game"));
            Assert.IsNotNull(r.Foe.Chat.Callout(99, r.Now));
            Assert.AreEqual(QuickChat.Count, QuickChat.Text.Length);
            Assert.AreEqual(QuickChat.Count, QuickChat.Scope.Length);
            Assert.AreEqual(QuickChat.Count, QuickChat.Marker.Length);
            Assert.AreEqual(QuickChat.Count, QuickChat.MarkerKind.Length);
        }

        [Test]
        public void BotsNeverChatAndOnlyRoomMembersAreRouted()
        {
            // Bots are sim actors, not peers: the only way into chat is a packet from a confirmed member of the room.
            var r = Rig3();
            var ev = new TransportEvent { Kind = TransportEventKind.Data, From = 9, Data = new byte[Peers.MaxPacket] };
            ev.Length = ChatWire.Write(ev.Data, ChatScope.All, 0, 0, -1, "", "i am a bot seat");
            int before = r.Host.Chat.Version;
            r.Host.Chat.Handle(ref ev, r.Now);
            Assert.AreEqual(before, r.Host.Chat.Version, "a peer that is not a member is never routed");
            r.Run(1000);
            foreach (ChatLine l in r.Host.Chat.Lines)
                if (l.Scope != ChatScope.System) Assert.IsNotNull(r.Host.Find(l.FromPeer), "every line comes from a member");
        }

        // ---- voice ---------------------------------------------------------------------------------------------------

        [Test]
        public void TheVoiceCodecRoundTripsSpeechLikeSound()
        {
            var pcm = new short[VoiceChannel.FrameSamples];
            for (int i = 0; i < pcm.Length; i++)
                pcm[i] = (short)(8000 * Math.Sin(i * 2 * Math.PI * 220 / 16000.0) + 3000 * Math.Sin(i * 2 * Math.PI * 1250 / 16000.0));
            var bytes = new byte[ImaAdpcm.Bytes(pcm.Length)];
            var s = new ImaAdpcm.State();
            ImaAdpcm.Encode(pcm, 0, pcm.Length, ref s, bytes, 0);
            var back = new short[pcm.Length];
            var d = new ImaAdpcm.State();
            ImaAdpcm.Decode(bytes, 0, pcm.Length, ref d, back, 0);
            double sig = 0, err = 0;
            for (int i = 200; i < pcm.Length; i++) { sig += (double)pcm[i] * pcm[i]; double e = pcm[i] - back[i]; err += e * e; }
            double snr = 10 * Math.Log10(sig / Math.Max(1, err));
            Console.WriteLine("{\"item\":\"b17-voice codec\",\"snr_db\":" + snr.ToString("0.0") + ",\"bytes_per_frame\":" + (bytes.Length + VoiceChannel.HeaderBytes) + "}");
            Assert.Greater(snr, 18, "IMA ADPCM keeps speech intelligible");
            Assert.LessOrEqual(bytes.Length + VoiceChannel.HeaderBytes, Peers.MaxPacket, "a frame fits one packet");
        }

        [Test]
        public void VoiceIsLobbyOnlyTeamOnlyOffByDefaultAndTheFloorHoldsTwoATeam()
        {
            var r = Rig3();
            var frame = new short[VoiceChannel.FrameSamples];
            Assert.IsFalse(r.Host.Chat.Voice.Enabled, "voice is off by default");
            Assert.IsNotNull(r.Mate.Chat.Voice.SendFrame(frame, r.Now), "off: nothing is sent");
            int heardByHost = 0, heardByFoe = 0;
            r.Host.Chat.Voice.Play = (p, s, n) => heardByHost++;
            r.Foe.Chat.Voice.Play = (p, s, n) => heardByFoe++;
            r.Host.Chat.Voice.Enabled = r.Mate.Chat.Voice.Enabled = r.Foe.Chat.Voice.Enabled = true;
            for (int i = 0; i < 10; i++) { Assert.IsNull(r.Mate.Chat.Voice.SendFrame(frame, r.Now)); r.Run(VoiceChannel.FrameMs); }
            r.Run(300);
            Assert.AreEqual(10, heardByHost, "the teammate (host) hears every frame");
            Assert.AreEqual(0, heardByFoe, "the other team never plays it");
            r.Host.Chat.SetMuted(r.Mate.LocalPeer, true);
            r.Mate.Chat.Voice.SendFrame(frame, r.Now);
            r.Run(300);
            Assert.AreEqual(10, heardByHost, "a muted talker is not played");

            // The floor: two talkers on a team at once; a third waits.
            var t = new NullTransport(0);
            var v = new VoiceChannel(t) { TeamOf = p => 0, Knows = p => true, InLobby = () => true, Enabled = true, CanBroadcast = () => true };
            var buf = new byte[Peers.MaxPacket];
            var one = new VoiceChannel(new NullTransport(5)) { TeamOf = p => 0, InLobby = () => true, Enabled = true };
            int len = one.WriteFrame(frame, buf);
            for (long now = 0; now < 2000; now += VoiceChannel.FrameMs)
                for (int p = 1; p <= 3; p++)
                {
                    var ev = new TransportEvent { Kind = TransportEventKind.Data, From = p, Data = buf, Length = len };
                    v.Handle(ref ev, now);
                }
            Assert.AreEqual(0, CountFrom(t, 3), "the third talker on a team is not forwarded");
            Assert.Greater(t.Sends, 0);

            // Lobby only: once the match starts, nothing is sent or forwarded.
            var m = new VoiceChannel(new NullTransport(0)) { TeamOf = p => 0, Knows = p => true, InLobby = () => false, Enabled = true };
            Assert.IsNotNull(m.SendFrame(frame, 0));
        }

        // ---- relay cost: a full room spamming at the limit ------------------------------------------------------------

        private static (long msgs, long bytes, long chatMsgs) Lobby32(bool chat, long ms, Action<RoomHost, List<RoomClient>, long> each = null)
        {
            var net = new LoopbackNetwork(31, 30, 0, 0);
            var cfg = GameConfig.CreateDefault();
            var links = new List<MeteredTransport>();
            var hl = new MeteredTransport(net.CreatePeer());
            links.Add(hl);
            var host = new RoomHost(hl, cfg, "Host", "b") { TeamSize = 16, Pace = 1 };
            var clients = new List<RoomClient>();
            for (int i = 1; i < 32; i++)
            {
                var l = new MeteredTransport(net.CreatePeer());
                links.Add(l);
                clients.Add(new RoomClient(l, cfg, "P" + i, "b"));
            }
            long now = 0;
            void Step()
            {
                net.Advance(now);
                host.Update(now);
                foreach (RoomClient c in clients) c.Update(now);
                now += 5;
            }
            while (now < 3000) Step();
            for (int i = 0; i < clients.Count; i++) clients[i].SetPick(i % 2 == 0 ? TeamId.Crimson : TeamId.Azure, ClassId.Warrior);
            while (now < 6000) Step();
            Assert.AreEqual(32, host.Members.Count);
            var before = new List<TrafficCounts>();
            foreach (MeteredTransport l in links) before.Add(l.Sent.Copy());
            long end = now + ms;
            while (now < end)
            {
                if (chat && now % 50 == 0)
                {
                    // Everyone tries a line every 50 ms (half team, half all): the sender's own limit lets the burst, then one per RateMs, out.
                    host.Chat.Say("host spam " + now, ChatScope.Team, now);
                    for (int i = 0; i < clients.Count; i++) clients[i].Chat.Say("spam " + now, (i + now / 50) % 2 == 0 ? ChatScope.Team : ChatScope.All, now);
                }
                each?.Invoke(host, clients, now);
                Step();
            }
            long msgs = 0, bytes = 0, chatMsgs = 0;
            for (int i = 0; i < links.Count; i++)
            {
                TrafficCounts d = links[i].Sent.Since(before[i]);
                msgs += d.TotalMessages;
                bytes += d.TotalBytes;
                chatMsgs += d.Messages[(int)TrafficKind.Chat];
            }
            return (msgs, bytes, chatMsgs);
        }

        [Test]
        public void AFullRoomSpammingAtTheLimitStaysWithinTheStatedShareOfTheFreeRelay()
        {
            const long Ms = 60000;
            var quiet = Lobby32(false, Ms);
            var loud = Lobby32(true, Ms);
            double minutes = Ms / 60000.0;
            double chatPerMin = loud.chatMsgs / minutes;
            double worst = ChatBudget.WorstMessagesPerMinute(32);
            double matchPerMin = RelayBudget.MatchMessagesPerSecond(32) * 60.0;
            Console.WriteLine("{\"item\":\"b17-chat relay cost\",\"humans\":32,\"lobby_msgs_per_player_min\":\"" + (quiet.msgs / minutes / 32).ToString("0.0") + " -> " + (loud.msgs / minutes / 32).ToString("0.0") +
                              "\",\"lobby_bytes_per_player_min\":\"" + (quiet.bytes / minutes / 32).ToString("0") + " -> " + (loud.bytes / minutes / 32).ToString("0") +
                              "\",\"chat_msgs_per_min\":" + chatPerMin.ToString("0") + ",\"chat_requests_per_min\":" + (chatPerMin / RelayBudget.MessagesPerRequest).ToString("0.0") +
                              ",\"worst_case_rule\":" + worst.ToString("0") + ",\"match_msgs_per_min\":" + matchPerMin.ToString("0") +
                              ",\"share_of_match\":" + (chatPerMin / matchPerMin).ToString("0.000") +
                              ",\"share_of_free_day_per_10_min_room\":" + (10 * chatPerMin / RelayBudget.MessagesPerRequest / RelayBudget.FreeRequestsPerDay).ToString("0.0000") + "}");
            Assert.AreEqual(0, quiet.chatMsgs);
            Assert.LessOrEqual(chatPerMin, worst * 1.02, "the measured room stays inside the rule's worst case");
            Assert.LessOrEqual(worst / matchPerMin, 0.06, "worst-case chat is at most 6% of what the match itself costs");
            Assert.LessOrEqual(10 * worst / RelayBudget.MessagesPerRequest, 0.01 * RelayBudget.FreeRequestsPerDay, "a 10-minute room spamming nonstop is at most 1% of the free day");
        }

        [Test]
        public void LobbyVoiceCostsTwoMessagesAFrameAndTheFloorCapsTheRoom()
        {
            // Six people hold push-to-talk for a whole minute (three a team); the host's floor lets two a team through.
            var frame = new short[VoiceChannel.FrameSamples];
            long last = -1000;
            bool on = false;
            var loud = Lobby32(false, 60000, (host, clients, now) =>
            {
                if (!on) { on = true; host.Chat.Voice.Enabled = true; foreach (RoomClient c in clients) c.Chat.Voice.Enabled = true; }
                if (now - last < VoiceChannel.FrameMs) return;
                last = now;
                host.Chat.Voice.SendFrame(frame, now);
                for (int i = 0; i < 5; i++) clients[i].Chat.Voice.SendFrame(frame, now);
            });
            double perMin = loud.chatMsgs;
            double talkers = 2 * VoiceChannel.MaxTalkersPerTeam;
            double rule = talkers * 2 * VoiceChannel.FramesPerSecond * 60;   // uplink + one broadcast per frame
            Console.WriteLine("{\"item\":\"b17-voice relay cost\",\"voice_msgs_per_min\":" + perMin + ",\"rule\":" + rule.ToString("0") +
                              ",\"requests_per_talker_min\":" + (2 * VoiceChannel.FramesPerSecond * 60 / RelayBudget.MessagesPerRequest).ToString("0.0") + "}");
            Assert.LessOrEqual(perMin, rule * 1.05, "at most four talkers, two messages a frame");
            Assert.Greater(perMin, rule * 0.5, "the granted talkers were heard");
        }

        // ---- the match never sees chat -------------------------------------------------------------------------------

        private struct MatchRun
        {
            public Dictionary<int, ulong> Hashes;
            public long LockstepMessages, LockstepBytes, ChatMessages;
            public bool AllMatched;
        }

        /// <summary>Three humans (two Azure, one Crimson) on a steady link; withChat floods text, callouts and voice frames.</summary>
        private static MatchRun Play(RulesProfile profile, bool withChat, int ticks)
        {
            var net = new LoopbackNetwork(11, 30, 0, 0);
            var teams = new[] { TeamId.Azure, TeamId.Crimson, TeamId.Azure };
            var actors = new int[3];
            Assert.IsTrue(MatchSetup.AssignActors(4, TeamId.Azure, teams, actors));
            var setup = new MatchSetup { Seed = 1717, TeamSize = 4, HostTeam = TeamId.Azure, InputDelay = 4, HashEvery = 30, Pace = 1 };
            setup.Humans = new HumanSlot[3];
            var links = new MeteredTransport[3];
            for (int i = 0; i < 3; i++)
            {
                links[i] = new MeteredTransport(net.CreatePeer());
                setup.Humans[i] = new HumanSlot { Peer = links[i].LocalPeer, Actor = actors[i], Class = ClassId.Warrior, Name = "P" + i };
            }
            var sessions = new LockstepSession[3];
            var chats = new ChatChannel[3];
            var hashes = new Dictionary<int, ulong>[3];
            var peers = new List<int> { 0, 1, 2 };
            for (int i = 0; i < 3; i++)
            {
                GameConfig cfg = GameConfig.CreateDefault();
                cfg.ApplyRules(profile);
                sessions[i] = new LockstepSession(links[i], setup, cfg);
                var pilot = new AutoplayPilot((uint)(300 + i));
                sessions[i].Sampler = pilot.Fill;
                var log = new Dictionary<int, ulong>();
                hashes[i] = log;
                sessions[i].HashRecorded = (t, h) => log[t] = h;
                int me = i;
                chats[i] = new ChatChannel(links[i]) { NameOf = p => "P" + p, TeamOf = p => (int)teams[p], Knows = p => p >= 0 && p < 3, PeersInRoom = () => peers, CanBroadcast = () => true };
                chats[i].Voice = new VoiceChannel(links[i]) { TeamOf = p => p >= 0 && p < 3 ? (int)teams[p] : -1, Knows = p => true, InLobby = () => true, Enabled = true, CanBroadcast = () => true };
                sessions[i].Other = chats[i].Handle;
            }
            var frame = new short[VoiceChannel.FrameSamples];
            for (int i = 0; i < frame.Length; i++) frame[i] = (short)(i * 37 % 4000 - 2000);
            long now = 0;
            while (now < 120000)
            {
                now += 5;
                net.Advance(now);
                bool done = true;
                for (int i = 0; i < 3; i++)
                {
                    sessions[i].Update(now);
                    if (sessions[i].Stepped < ticks && sessions[i].Status == LockstepStatus.Running) done = false;
                }
                if (withChat && now % 100 == 0)
                    for (int i = 0; i < 3; i++)
                    {
                        chats[i].Say("<b>flood</b> " + now + " sh1t www.x.com", (now / 100) % 2 == 0 ? ChatScope.All : ChatScope.Team, now);
                        chats[i].Callout((int)(now / 100 % QuickChat.Count), now);
                        chats[i].Voice.SendFrame(frame, now);
                    }
                if (done) break;
            }
            var run = new MatchRun { Hashes = hashes[0], AllMatched = true };
            foreach (KeyValuePair<int, ulong> kv in hashes[0])
                for (int i = 1; i < 3; i++)
                    if (hashes[i].TryGetValue(kv.Key, out ulong h) && h != kv.Value) run.AllMatched = false;
            for (int i = 0; i < 3; i++)
            {
                run.LockstepMessages += links[i].Sent.Messages[(int)TrafficKind.Inputs] + links[i].Sent.Messages[(int)TrafficKind.Bundles];
                run.LockstepBytes += links[i].Sent.Bytes[(int)TrafficKind.Inputs] + links[i].Sent.Bytes[(int)TrafficKind.Bundles];
                run.ChatMessages += links[i].Sent.Messages[(int)TrafficKind.Chat];
            }
            Assert.GreaterOrEqual(sessions[0].Stepped, ticks, profile + (withChat ? " with chat" : "") + ": the match ran");
            return run;
        }

        [TestCase(RulesProfile.Classic)]
        [TestCase(RulesProfile.PhysicsOnly)]
        [TestCase(RulesProfile.Full)]
        public void TheSameMatchWithAndWithoutChatHasTheSameFingerprintsAndLockstepTraffic(RulesProfile profile)
        {
            const int Ticks = 900;
            MatchRun quiet = Play(profile, false, Ticks);
            MatchRun loud = Play(profile, true, Ticks);
            Console.WriteLine("{\"item\":\"b17-chat no-sim-impact\",\"profile\":\"" + profile + "\",\"checks\":" + quiet.Hashes.Count +
                              ",\"lockstep_msgs\":" + quiet.LockstepMessages + "/" + loud.LockstepMessages +
                              ",\"lockstep_bytes\":" + quiet.LockstepBytes + "/" + loud.LockstepBytes + ",\"chat_msgs\":" + quiet.ChatMessages + "/" + loud.ChatMessages + "}");
            Assert.Greater(loud.ChatMessages, 300, "the loud match really carried chat and voice");
            Assert.AreEqual(0, quiet.ChatMessages);
            Assert.IsTrue(quiet.AllMatched && loud.AllMatched, "every machine agrees in both matches");
            Assert.GreaterOrEqual(quiet.Hashes.Count, Ticks / 30 - 1);
            foreach (KeyValuePair<int, ulong> kv in quiet.Hashes)
            {
                Assert.IsTrue(loud.Hashes.TryGetValue(kv.Key, out ulong h), "tick " + kv.Key + " checked in both");
                Assert.AreEqual(kv.Value, h, profile + ": tick " + kv.Key + " differs with chat");
            }
            Assert.AreEqual(quiet.LockstepMessages, loud.LockstepMessages, "the lockstep input stream is untouched (messages)");
            Assert.AreEqual(quiet.LockstepBytes, loud.LockstepBytes, "the lockstep input stream is untouched (bytes)");
        }

        /// <summary>The offline goldens (SimPhysicsSoakTests) are the base branch's pins: this branch changes no rule.</summary>
        [Test]
        public void TheGoldensAreTheBaseBranchsPins()
        {
            var pins = new Dictionary<RulesProfile, ulong>
            {
                { RulesProfile.Classic, 0x5C912AEED16BC302UL },
                { RulesProfile.PhysicsOnly, 0x0836DA5FD21C4854UL },
                { RulesProfile.Full, 0x0B1E594CAC2A34E0UL },   // batch 16 combined (sim run 44953083481); chat alone matched the base 0x48A817A4B2B3F068
            };
            foreach (KeyValuePair<RulesProfile, ulong> kv in pins)
            {
                RulesProfile p = kv.Key;
                ulong a = MatchHash.Run(1, 16, 3600, ClassId.Warrior, null, c => c.ApplyRules(p));
                Console.WriteLine("{\"item\":\"b17-chat golden\",\"profile\":\"" + p + "\",\"16v16seed1\":\"0x" + a.ToString("X16") + "\"}");
                Assert.AreEqual(kv.Value, a, p + " 16v16 seed 1 at 2:00");
            }
        }

        [Test, Category("Slow")]
        public void TheWholeMatchGoldensAreTheBaseBranchsPins()
        {
            var pins = new Dictionary<RulesProfile, ulong>
            {
                { RulesProfile.Classic, 0x2980068FA0457CC2UL },
                { RulesProfile.PhysicsOnly, 0x780B92B03E6FFEAEUL },
                { RulesProfile.Full, 0xC01D547484A56516UL },   // batch 16 combined (sim run 44955919924; the merge alone 0xF41666EAD7BC9994); chat alone matched the base 0x1978E881F4005F28
            };
            foreach (KeyValuePair<RulesProfile, ulong> kv in pins)
            {
                RulesProfile p = kv.Key;
                Assert.AreEqual(kv.Value, MatchHash.Run(42, 5, 14500, ClassId.Warrior, null, c => c.ApplyRules(p)), p + " 5v5 seed 42 whole match");
            }
        }

        // ---- helpers -------------------------------------------------------------------------------------------------

        private sealed class Room3
        {
            public LoopbackNetwork Net;
            public RoomHost Host;
            public RoomClient Mate, Foe;
            public long Now;
            public void Run(long ms)
            {
                for (long end = Now + ms; Now < end; Now += 5) { Net.Advance(Now); Host.Update(Now); Mate.Update(Now); Foe.Update(Now); }
            }
        }

        /// <summary>A lobby: the host and the mate on Azure, the foe on Crimson, everyone confirmed.</summary>
        private static Room3 Rig3()
        {
            var net = new LoopbackNetwork(21, 30, 0, 0);
            var cfg = GameConfig.CreateDefault();
            var r = new Room3
            {
                Net = net,
                Host = new RoomHost(net.CreatePeer(), cfg, "Wilson", "b") { TeamSize = 4, Pace = 1 },
            };
            r.Mate = new RoomClient(net.CreatePeer(), cfg, "Travis", "b");
            r.Foe = new RoomClient(net.CreatePeer(), cfg, "Jeisil", "b");
            r.Run(2000);
            r.Mate.SetPick(TeamId.Azure, ClassId.Builder);
            r.Foe.SetPick(TeamId.Crimson, ClassId.Miner);
            r.Run(1500);
            Assert.AreEqual(TeamId.Azure, r.Host.Find(r.Mate.LocalPeer).Team);
            Assert.AreEqual(TeamId.Crimson, r.Host.Find(r.Foe.LocalPeer).Team);
            return r;
        }

        /// <summary>A transport that counts what is sent and never delivers (unit tests of one channel).</summary>
        private sealed class NullTransport : ITransport
        {
            public NullTransport(int self) { LocalPeer = self; }
            public int LocalPeer { get; }
            public bool Reliable => true;
            public long Sends;
            public readonly List<int> SentFrom = new List<int>();
            public void Send(int to, byte[] data, int length) { Sends++; if (length > 2) SentFrom.Add(data[2]); }
            public bool Poll(out TransportEvent ev) { ev = default; return false; }
            public void Close() { }
        }

        private static int CountFrom(NullTransport t, int peer) { int n = 0; foreach (int p in t.SentFrom) if (p == peer) n++; return n; }

        private static int Count(string s, char c) { int n = 0; foreach (char x in s) if (x == c) n++; return n; }

        private static bool Has(ChatChannel ch, string text) => Find(ch, text, out _);

        private static bool Find(ChatChannel ch, string text, out ChatLine line)
        {
            foreach (ChatLine l in ch.Lines) if (l.Text.Contains(text)) { line = l; return true; }
            line = default;
            return false;
        }

        private static string Dump(ChatChannel ch)
        {
            var parts = new List<string>();
            foreach (ChatLine l in ch.Lines) parts.Add(l.Scope + " " + l.From + ": " + l.Text);
            return string.Join(" | ", parts);
        }
    }
}
