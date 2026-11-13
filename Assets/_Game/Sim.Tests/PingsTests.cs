using System;
using System.Collections.Generic;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>U-067 pings the bots answer, the bots' own calls, bot names and play styles.</summary>
    [TestFixture]
    public sealed class PingsTests
    {
        private static GameConfig Quiet => DepthKit.Cfg(d => d.Pings.BotCalls = false);

        /// <summary>A 16v16 match with a standing human in seat 0, run for warm ticks.</summary>
        private static World Match(GameConfig cfg, int warm, InputFrame f, int seed = 7)
        {
            World w = DepthKit.Make(cfg, ClassId.Warrior, seed, 16);
            Run(w, f, warm);
            return w;
        }

        private static void Run(World w, InputFrame f, int ticks)
        {
            InputFrame scratch = SimInput.EmptyInput();
            for (int t = 0; t < ticks; t++)
            {
                MatchTick.Advance(w, f, scratch);
                f.PingKind = 0;
            }
        }

        private static void Ping(World w, InputFrame f, PingCall call, double x, double z)
        {
            f.PingKind = (int)call + 1;
            f.PingX = x;
            f.PingZ = z;
            Run(w, f, 1);
        }

        [Test]
        public void U067BotsInRangeAnswerAnAttackPingOnThatTickAndFarOnesDoNot()
        {
            InputFrame f = SimInput.EmptyInput();
            World w = Match(Quiet, 240, f);
            PingsConfig P = w.Config.Depth.Pings;
            double px = w.X[0] + 6, pz = w.Z[0];
            int head = w.EventHead;
            Ping(w, f, PingCall.Attack, px, pz);
            SimEvent ping = DepthKit.Since(w, head, SimEventType.Ping)[0];
            Assert.AreEqual(0, ping.A);
            Assert.AreEqual((int)PingCall.Attack, ping.B);
            Assert.AreEqual(px, ping.X);
            Assert.AreEqual(P.PingLifeTicks, ping.Value);
            List<SimEvent> ans = DepthKit.Since(w, head, SimEventType.PingAnswered);
            Assert.GreaterOrEqual(ans.Count, 1);
            Assert.LessOrEqual(ans.Count, 3);
            foreach (SimEvent e in ans)
            {
                Actor b = w.Actors[e.A];
                Assert.AreEqual(w.Actors[0].Team, b.Team);
                Assert.IsFalse(b.IsHuman);
                Assert.AreEqual(0, e.B);
                Assert.AreEqual((int)PingCall.Attack, e.Value);
                Assert.IsTrue(b.AiState == AiState.Order || b.AiState == AiState.Fight, "state changed on the ping's tick: " + b.AiState);
                Assert.Greater(w.Config.Class(b.Cls).AttackDamage, 0, "no weapon, no Attack answer");
                double dx = e.X - px, dz = e.Z - pz;
                Assert.LessOrEqual(Math.Sqrt(dx * dx + dz * dz), P.AnswerRange);
                Assert.IsTrue(PingRules.HasOrder(w, e.A));
            }
            // They walk to it.
            int first = ans[0].A;
            double d0 = Math.Abs(w.X[first] - px) + Math.Abs(w.Z[first] - pz);
            Run(w, f, 90);
            double d1 = Math.Abs(w.X[first] - px) + Math.Abs(w.Z[first] - pz);
            Console.WriteLine("{\"item\":\"U-067\",\"answers\":" + ans.Count + ",\"firstFrom\":" + d0.ToString("F1") + ",\"after3s\":" + d1.ToString("F1") + "}");
            Assert.IsTrue(d1 < d0 || d1 < 10 || !w.Actors[first].Alive);

            // A ping with nobody within range recruits nobody (a tiny answer range).
            World v = Match(DepthKit.Cfg(d => { d.Pings.BotCalls = false; d.Pings.AnswerRange = 0.01; }), 240, f);
            head = v.EventHead;
            Ping(v, f, PingCall.Attack, v.X[0] + 6, v.Z[0]);
            Assert.AreEqual(1, DepthKit.Since(v, head, SimEventType.Ping).Count);
            Assert.AreEqual(0, DepthKit.Since(v, head, SimEventType.PingAnswered).Count);
        }

        [Test]
        public void U067TheFourthPingInsideFiveSecondsIsRefusedAndOnMyWayRecruitsNobody()
        {
            InputFrame f = SimInput.EmptyInput();
            World w = Match(Quiet, 120, f);
            int head = w.EventHead;
            for (int k = 0; k < 4; k++)
            {
                Ping(w, f, PingCall.OnMyWay, 30 + k, 36);
                Run(w, f, 9);
            }
            Assert.AreEqual(3, DepthKit.Since(w, head, SimEventType.Ping).Count);
            List<SimEvent> no = DepthKit.Since(w, head, SimEventType.PingRefused);
            Assert.AreEqual(1, no.Count);
            Assert.AreEqual(150 - 30, no[0].Value, "ticks until the oldest ping is 5 s old");
            Assert.AreEqual(0, DepthKit.Since(w, head, SimEventType.PingAnswered).Count, "On my way is an announcement");
            Run(w, f, 120);
            Ping(w, f, PingCall.OnMyWay, 30, 36);
            Assert.AreEqual(4, DepthKit.Since(w, head, SimEventType.Ping).Count);
            Assert.AreEqual(4, w.Depth.PingsAccepted);
            Assert.AreEqual(1, w.Depth.PingsRefused);
        }

        [Test]
        public void U067DefendersHoldTheSpotAndHelpersComeToTheCaller()
        {
            InputFrame f = SimInput.EmptyInput();
            World w = Match(Quiet, 240, f);
            PingsConfig P = w.Config.Depth.Pings;
            double px = w.X[0] + 4, pz = w.Z[0] + 3;
            int head = w.EventHead;
            Ping(w, f, PingCall.Defend, px, pz);
            List<SimEvent> ans = DepthKit.Since(w, head, SimEventType.PingAnswered);
            Assert.GreaterOrEqual(ans.Count, 1);
            Run(w, f, 240);
            int held = 0;
            foreach (SimEvent e in ans)
            {
                if (!w.Actors[e.A].Alive || !PingRules.HasOrder(w, e.A)) continue;
                double dx = w.X[e.A] - px, dz = w.Z[e.A] - pz;
                if (Math.Sqrt(dx * dx + dz * dz) <= P.DefendHoldRange + P.DefendReach + 8) held++;
            }
            Console.WriteLine("{\"item\":\"U-067\",\"defendAnswers\":" + ans.Count + ",\"holdingAt8s\":" + held + "}");
            Assert.GreaterOrEqual(held, 1);
            Run(w, f, (int)P.DefendTicks);
            foreach (SimEvent e in ans) Assert.IsFalse(PingRules.HasOrder(w, e.A), "12 s and the order is over");

            head = w.EventHead;
            Ping(w, f, PingCall.Help, w.X[0], w.Z[0]);
            List<SimEvent> help = DepthKit.Since(w, head, SimEventType.PingAnswered);
            Assert.LessOrEqual(help.Count, 2);
            Assert.GreaterOrEqual(help.Count, 1);
            int h = help[0].A;
            double before = Math.Abs(w.X[h] - w.X[0]) + Math.Abs(w.Z[h] - w.Z[0]);
            Run(w, f, 150);
            double after = Math.Abs(w.X[h] - w.X[0]) + Math.Abs(w.Z[h] - w.Z[0]);
            Console.WriteLine("{\"item\":\"U-067\",\"helpFrom\":" + before.ToString("F1") + ",\"after5s\":" + after.ToString("F1") + "}");
            Assert.IsTrue(after < before || after < 8 || !w.Actors[h].Alive);
        }

        [Test]
        public void U067ACarrierNeverAnswersAndClassicIgnoresPings()
        {
            InputFrame f = SimInput.EmptyInput();
            World w = Match(Quiet, 240, f);
            for (int j = 1; j < w.ActorCount / 2; j++)
            {
                w.Actors[j].Carry = CarryKind.Biomass;
                w.Actors[j].CarryAmount = 1;
            }
            int head = w.EventHead;
            Ping(w, f, PingCall.Attack, w.X[0] + 5, w.Z[0]);
            Assert.AreEqual(0, DepthKit.Since(w, head, SimEventType.PingAnswered).Count);

            World c = DepthKit.Make(GameConfig.CreateBrowser5v5(), ClassId.Warrior, 7, 5);
            Run(c, f, 60);
            head = c.EventHead;
            Ping(c, f, PingCall.Attack, 56, 40);
            Assert.AreEqual(0, DepthKit.Since(c, head, SimEventType.Ping).Count);
            Assert.AreEqual(0, c.Inputs[0].PingKind, "consumed all the same");
        }

        [Test]
        public void U067SameSeedAndSamePingTicksGiveTheSameWorld()
        {
            ulong Go(bool ping)
            {
                InputFrame f = SimInput.EmptyInput();
                World w = Match(DepthKit.Cfg(), 300, f, 11);
                if (ping) Ping(w, f, PingCall.Attack, 56, 40);
                Run(w, f, 200);
                if (ping) Ping(w, f, PingCall.Defend, 40, 36);
                Run(w, f, 400);
                return MatchHash.Of(w);
            }
            ulong a = Go(true), b = Go(true), c = Go(false);
            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, c, "the pings moved the match");
        }

        [Test]
        public void U067EveryBotHasANameUniqueInTheMatchAndAStyleFromItsClass()
        {
            World w = DepthKit.Make(DepthKit.Cfg(), ClassId.Warrior, 7, 16);
            var seen = new HashSet<string>();
            int[] styles = new int[4];
            for (int i = 0; i < w.ActorCount; i++)
            {
                Assert.IsTrue(seen.Add(BotNames.NameOf(w, i)), "duplicate name " + BotNames.NameOf(w, i));
                int s = BotNames.StyleOf(w, i);
                styles[s]++;
                if (w.Actors[i].Cls == ClassId.Farmer) Assert.AreEqual(BotNames.Harvester, s);
                if (w.Actors[i].Cls == ClassId.Miner) Assert.AreEqual(BotNames.Sapper, s);
            }
            World v = DepthKit.Make(DepthKit.Cfg(), ClassId.Warrior, 7, 16);
            World u = DepthKit.Make(DepthKit.Cfg(), ClassId.Warrior, 8, 16);
            Assert.AreEqual(BotNames.NameOf(w, 5), BotNames.NameOf(v, 5));
            int same = 0;
            for (int i = 0; i < w.ActorCount; i++) if (BotNames.NameOf(w, i) == BotNames.NameOf(u, i)) same++;
            Assert.Less(same, w.ActorCount, "another seed, another roster");
            Console.WriteLine("{\"item\":\"U-067\",\"names\":\"" + BotNames.NameOf(w, 1) + "," + BotNames.NameOf(w, 2) + "," + BotNames.NameOf(w, 3) + "\",\"styles\":[" + string.Join(",", styles) + "]}");
            Assert.Greater(styles[BotNames.Brawler], 0);
            Assert.Greater(styles[BotNames.Guard], 0);
            Assert.AreEqual(1, BotNames.FightScale(DepthKit.Make(GameConfig.CreateBrowser5v5(), ClassId.Warrior), 1));
        }

        [Test]
        public void U067BotsCallForHelpAndCallOutCarriersAndMatesAnswer()
        {
            World w = DepthKit.Make(DepthKit.Cfg(), ClassId.Warrior, 7, 16);
            int help = 0, attack = 0, answered = 0;
            DepthKit.RunBots(w, 5400, e =>
            {
                if (e.Type == SimEventType.Ping && e.B == (int)PingCall.Help) help++;
                if (e.Type == SimEventType.Ping && e.B == (int)PingCall.Attack) attack++;
                if (e.Type == SimEventType.PingAnswered) answered++;
            });
            Console.WriteLine("{\"item\":\"U-067\",\"botHelpCalls\":" + help + ",\"botAttackCalls\":" + attack + ",\"answers\":" + answered + ",\"refused\":" + w.Depth.PingsRefused + "}");
            Assert.Greater(help + attack, 3);
            Assert.Greater(answered, 0);
            Assert.LessOrEqual(help + attack, 5400 / 240 * 2 + 2, "one call per team per 8 s at most");
        }
    }
}
