using CriticalMass.Net;
using CriticalMass.Sim.Config;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests.Net
{
    /// <summary>online-rooms-2: chat and names for an all-ages game, carried beside (never inside) the lockstep inputs.</summary>
    public sealed class OnlineChatTests
    {
        [Test]
        public void TheFilterMasksBlockedWordsKeepsInnocentOnesAndDropsLinks()
        {
            Assert.AreEqual("you are a *****", ChatFilter.CleanLine("you are a b1tch"));
            Assert.AreEqual("****** off", ChatFilter.CleanLine("f.u.c.k off"));
            Assert.AreEqual("grape juice and a peacock, spicy", ChatFilter.CleanLine("grape juice and a peacock, spicy"));
            Assert.AreEqual("this hit was great", ChatFilter.CleanLine("this hit was great"));
            Assert.AreEqual("join [link removed] now", ChatFilter.CleanLine("join www.example.com now"));
            Assert.AreEqual("[link removed]", ChatFilter.CleanLine("https://x.y/z"));
            Assert.AreEqual("see [link removed]", ChatFilter.CleanLine("see free-stuff.gg"));
            Assert.AreEqual("ok", ChatFilter.CleanLine("  ok\u0007  "));
            Assert.AreEqual(ChatFilter.MaxLength, ChatFilter.CleanLine(new string('a', 500)).Length);
            Assert.AreEqual("JEISIL", ChatFilter.CleanName("Jeisil"));
            Assert.AreEqual("PLAYER", ChatFilter.CleanName("sh1t"));
            Assert.AreEqual("PLAYER", ChatFilter.CleanName("   "));
            Assert.AreEqual("TRAVIS-2", ChatFilter.CleanName("travis-2<script>"));
        }

        [Test]
        public void ChatReachesTheRightPeopleIsRateLimitedAndNeverTouchesTheMatch()
        {
            var net = new LoopbackNetwork(21, 30, 10, 0);
            var cfg = GameConfig.CreateDefault();
            var host = new RoomHost(net.CreatePeer(), cfg, "Wilson", "b") { TeamSize = 4, Pace = 1 };
            var mate = new RoomClient(net.CreatePeer(), cfg, "Travis", "b");
            var foe = new RoomClient(net.CreatePeer(), cfg, "Jeisil", "b");
            long now = 0;
            void Run(long ms)
            {
                for (long end = now + ms; now < end; now += 5) { net.Advance(now); host.Update(now); mate.Update(now); foe.Update(now); }
            }
            Run(2000);
            mate.SetPick(TeamId.Azure, ClassId.Builder);
            foe.SetPick(TeamId.Crimson, ClassId.Miner);
            mate.SetReady(true);
            foe.SetReady(true);
            Run(1500);
            Assert.IsNull(host.Chat.Say("team push the hill", ChatScope.Team, now));
            Assert.IsNull(foe.Chat.Say("good luck all", ChatScope.All, now));
            Run(500);
            Assert.IsTrue(Has(mate.Chat, "team push the hill"), "a teammate sees team chat");
            Assert.IsFalse(Has(foe.Chat, "team push the hill"), "the other team never does");
            Assert.IsTrue(Has(host.Chat, "good luck all") && Has(mate.Chat, "good luck all"));
            // Flood: ten lines at once, the host passes the burst only.
            int before = Count(mate.Chat, "spam");
            for (int i = 0; i < 10; i++) foe.Chat.Say("spam " + i, ChatScope.All, now);
            Run(500);
            Assert.LessOrEqual(Count(mate.Chat, "spam") - before, ChatChannel.Burst);
            // Mute.
            mate.Chat.SetMuted(2, true);
            Run(ChatChannel.RateMs * 3);
            foe.Chat.Say("muted line", ChatScope.All, now);
            Run(500);
            Assert.IsFalse(Has(mate.Chat, "muted line"));
            Assert.IsTrue(Has(host.Chat, "muted line"));

            // The match: chat keeps flowing, and every machine's world stays identical.
            LockstepSession hs = host.Start(now);
            Assert.IsNotNull(hs);
            for (long end = now + 60000; now < end && (mate.Session == null || foe.Session == null || hs.Stepped < 900 || mate.Session.Stepped < 900 || foe.Session.Stepped < 900); now += 5)
            {
                net.Advance(now);
                host.Update(now);
                mate.Update(now);
                foe.Update(now);
                if (now % 700 == 0) { mate.Chat.Say("in match " + now, ChatScope.All, now); foe.Chat.Say("crimson only " + now, ChatScope.Team, now); }
            }
            Assert.IsTrue(Has(host.Chat, "in match"), "chat flows during the match");
            Assert.IsFalse(Has(mate.Chat, "crimson only"));
            Assert.IsTrue(hs.TryGetHash(900, out ulong a));
            Assert.IsTrue(mate.Session.TryGetHash(900, out ulong b));
            Assert.IsTrue(foe.Session.TryGetHash(900, out ulong c));
            Assert.AreEqual(a, b);
            Assert.AreEqual(a, c);
        }

        static bool Has(ChatChannel ch, string text)
        {
            foreach (ChatLine l in ch.Lines) if (l.Text.Contains(text)) return true;
            return false;
        }

        static int Count(ChatChannel ch, string text)
        {
            int n = 0;
            foreach (ChatLine l in ch.Lines) if (l.Text.Contains(text)) n++;
            return n;
        }
    }
}
