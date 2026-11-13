using System;
using System.Globalization;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// Rules-combined (U-069..U-073): the band checks of the earlier suites, run once more with EVERYTHING on (sim physics plus
    /// mechanics depth, the game's default rules) at pace 1x, so all three rule profiles carry their own bands: Classic (the
    /// DG-21 goldens and the classic arms), PhysicsOnly (OutcomesSoakTests, RulesDg16bSoakTests, BotsMatchTests, BotDifficultyTests
    /// on CreateBrowser5v5) and Full (this fixture and DepthSoakTests). Measure() is also the PR's before / after table.
    /// </summary>
    [TestFixture, Category("Slow")]
    public sealed class RulesCombinedSoakTests
    {
        public sealed class Bands
        {
            public int Passive5, Passive16, Passive16Of, Intercepts, Matches, Berserk;
            public double HillA, HillC, MatchSeconds, CorruptionPerMatch, FarmerFeedShare;
            public int VsEasy, VsNormal, VsHard;
            public readonly double[] KoPer = new double[6];
            public readonly double[] DeathsPer = new double[6];

            public string Json(string arm)
            {
                var inv = CultureInfo.InvariantCulture;
                string per = "";
                for (int c = 0; c < 6; c++) per += ",\"" + (ClassId)c + "\":\"" + KoPer[c].ToString("F2", inv) + "/" + DeathsPer[c].ToString("F2", inv) + "\"";
                return "{\"item\":\"rules-combined bands\",\"arm\":\"" + arm + "\",\"passive5v5\":" + Passive5 + ",\"passive16v16\":" + Passive16 + ",\"passive16of\":" + Passive16Of
                    + ",\"interceptMatches\":" + Intercepts + ",\"berserkShare\":" + ((double)Berserk / Matches).ToString("F2", inv) + ",\"corruptionPerMatch\":" + CorruptionPerMatch.ToString("F1", inv)
                    + ",\"farmerFeedShare\":" + FarmerFeedShare.ToString("F2", inv) + ",\"hillA\":" + HillA.ToString("F3", inv) + ",\"hillC\":" + HillC.ToString("F3", inv)
                    + ",\"proxyE\":" + VsEasy + ",\"proxyN\":" + VsNormal + ",\"proxyH\":" + VsHard + ",\"matchSeconds\":" + MatchSeconds.ToString("F0", inv) + per + "}";
            }
        }

        public static void Full(GameConfig c) => c.ApplyRules(RulesProfile.Full);

        private static World Make(int seed, int teamSize, Action<GameConfig> tweak, BotDifficulty ally = BotDifficulty.Normal, BotDifficulty enemy = BotDifficulty.Normal)
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5();
            tweak?.Invoke(cfg);
            return World.CreateWorld(new WorldOptions
            {
                Seed = seed, TeamSize = teamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg,
                AllyBotDifficulty = ally, BotDifficulty = enemy,
            });
        }

        private static void Play(World w, Action<World> each = null)
        {
            InputFrame scratch = SimInput.EmptyInput();
            int max = (int)Math.Ceiling(w.Config.MatchSeconds * w.Config.SimHz) + (int)w.Config.SimHz;
            for (int t = 0; t < max && w.Match.Phase != MatchPhase.Ended; t++)
            {
                MatchTick.Advance(w, null, scratch);
                each?.Invoke(w);
            }
        }

        private static bool OnHill(World w, TeamId team)
        {
            HillDef h = w.Map.Hill;
            double r2 = w.Config.Hill.Radius * w.Config.Hill.Radius;
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive || a.Team != team) continue;
                double dx = w.X[i] - h.X, dz = w.Z[i] - h.Z;
                if (dx * dx + dz * dz < r2) return true;
            }
            return false;
        }

        /// <summary>Every band number for one rules setting (tweak edits CreateBrowser5v5, pace 1). passive16 = 16v16 idle seeds.</summary>
        public static Bands Measure(Action<GameConfig> tweak, int passive16 = 200, int seeds = 100)
        {
            var b = new Bands { Matches = seeds, Passive16Of = passive16 };
            // Idle-player sweeps (D-030 seeds 201..400): the idle human's team winning.
            for (int s = 201; s < 401; s++)
                if (OutcomeRunner.RunOutcomeMatch(s, 5, HumanMode.Passive, tweak).Winner == MatchResult.Azure) b.Passive5++;
            for (int s = 201; s < 201 + passive16; s++)
                if (OutcomeRunner.RunOutcomeMatch(s, 16, HumanMode.Passive, tweak).Winner == MatchResult.Azure) b.Passive16++;
            // CM-64 seeds 1..100, 5v5 all bots: interceptions, berserk share, match length, corruption delivered, who feeds.
            double ticks = 0, corr = 0, fedFarmer = 0, fedAll = 0;
            for (int s = 1; s <= seeds; s++)
            {
                World w = Make(s, 5, tweak);
                int head = 0;
                Play(w, x =>
                {
                    for (; head < x.EventHead; head++)
                    {
                        SimEvent e = x.Events[head % x.Events.Length];
                        if (e.Type != SimEventType.DeliverBiomass || e.A < 0 || e.A >= x.ActorCount) continue;
                        fedAll += e.Value;
                        if (x.Actors[e.A].Cls == ClassId.Farmer) fedFarmer += e.Value;
                    }
                });
                MatchState m = w.Match;
                if (m.Intercepted.Azure + m.Intercepted.Crimson > 0) b.Intercepts++;
                if (m.BerserkLocked) b.Berserk++;
                ticks += w.Tick;
                corr += m.Delivered[0].Corruption + m.Delivered[1].Corruption;
            }
            b.MatchSeconds = ticks / seeds / GameConfig.CreateBrowser5v5().SimHz;
            b.CorruptionPerMatch = corr / seeds;
            b.FarmerFeedShare = fedAll > 0 ? fedFarmer / fedAll : 0;
            // BotsMatch hill presence: 5v5 seeds 1..8, 120 s, every 15 ticks after 20 s.
            int total = 0, ha = 0, hc = 0;
            for (int s = 1; s <= 8; s++)
            {
                World w = Make(s, 5, tweak);
                InputFrame scratch = SimInput.EmptyInput();
                for (int k = 1; k <= 120 * (int)w.Config.SimHz; k++)
                {
                    MatchTick.Advance(w, null, scratch);
                    if (k % 15 != 0 || w.Match.Time <= 20) continue;
                    total++;
                    if (OnHill(w, TeamId.Azure)) ha++;
                    if (OnHill(w, TeamId.Crimson)) hc++;
                }
            }
            b.HillA = (double)ha / total;
            b.HillC = (double)hc / total;
            // DG-21 human proxy (Normal allies) against Easy / Normal / Hard, 20 seeds each.
            for (int s = 1; s <= 20; s++)
            {
                World e = Make(s, 5, tweak, BotDifficulty.Normal, BotDifficulty.Easy); Play(e); if (e.Match.Winner == MatchResult.Azure) b.VsEasy++;
                World n = Make(s, 5, tweak, BotDifficulty.Normal, BotDifficulty.Normal); Play(n); if (n.Match.Winner == MatchResult.Azure) b.VsNormal++;
                World h = Make(s, 5, tweak, BotDifficulty.Normal, BotDifficulty.Hard); Play(h); if (h.Match.Winner == MatchResult.Azure) b.VsHard++;
            }
            // Knock-outs and deaths per fighter by class: 16v16 seeds 101..104, whole matches.
            var count = new double[6];
            for (int s = 101; s <= 104; s++)
            {
                World w = Make(s, 16, tweak);
                Play(w);
                for (int i = 0; i < w.ActorCount; i++)
                {
                    Actor a = w.Actors[i];
                    int c = (int)a.Cls;
                    count[c]++;
                    b.KoPer[c] += a.Kills;
                    b.DeathsPer[c] += a.Deaths;
                }
            }
            for (int c = 0; c < 6; c++) if (count[c] > 0) { b.KoPer[c] /= count[c]; b.DeathsPer[c] /= count[c]; }
            return b;
        }

        private static Bands _full;
        private static Bands FullBands => _full ??= Measure(Full);

        // Integration batch 4: the shared FullBands measure (100 seeds + 200 passive matches, physics and mechanics on)
        // runs inside whichever of these two tests is first and took just over the default 180 s; 10 minutes of headroom.
        [Test, Category("Slow"), Timeout(600000)]
        public void RulesCombinedEverythingOnTheIdlePlayerFloorsHoldAt5v5And16v16()
        {
            Bands b = FullBands;
            Console.WriteLine(b.Json("everything on (shipped)"));
            Assert.GreaterOrEqual(b.Passive5 / 200.0, 0.15, "5v5 idle-player floor (D-030)");
            Assert.GreaterOrEqual(b.Passive16, 34, "16v16 idle-player sweep (owner floor 34 of 200)");
            // U-077: an idle human is a handicap: its team wins under half (an all-bot Azure wins about 59 of 100 at 16v16, 50 at 5v5).
            Assert.Less(b.Passive16, b.Passive16Of / 2, "16v16: the idle player's team wins under half");
            Assert.Less(b.Passive5, 100, "5v5: the idle player's team wins under half");
        }

        // Integration batch 4: the shared FullBands measure (100 seeds + 200 passive matches, physics and mechanics on)
        // runs inside whichever of these two tests is first and took just over the default 180 s; 10 minutes of headroom.
        [Test, Category("Slow"), Timeout(600000)]
        public void RulesCombinedEverythingOnInterceptsBerserkHillAndProxyBandsHold()
        {
            Bands b = FullBands;
            Assert.GreaterOrEqual(b.Intercepts, 80, "CM-64: interceptions in at least 80 of 100 seeds");
            Assert.GreaterOrEqual((double)b.Berserk / b.Matches, 0.10, "CM-34 berserk band");
            Assert.LessOrEqual((double)b.Berserk / b.Matches, 0.50, "CM-34 berserk band");
            Assert.GreaterOrEqual(Math.Min(b.HillA, b.HillC), 0.70, "CM-09 hill presence");
            Assert.Greater(b.VsEasy, b.VsNormal, "Easy opponents are easier than Normal");
            Assert.Greater(b.VsNormal, b.VsHard, "Hard opponents are harder than Normal");
            Assert.Greater(b.FarmerFeedShare, 0.3, "the Farmer is still the team's main feeder");
            Assert.Less(b.FarmerFeedShare, 0.95, "every class feeds (U-070)");
        }
    }
}
