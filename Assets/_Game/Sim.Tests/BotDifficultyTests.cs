using System;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// DG-21 bot difficulty (U-058): Easy, Normal, Hard through WorldOptions.BotDifficulty (the human's opponents) and
    /// WorldOptions.AllyBotDifficulty (the human's team). Normal is the browser bots bit for bit: the golden hashes below were
    /// taken with MatchHash on feat/dg-16-rules 798fa11, before any DG-21 code existed. Time is sim ticks only.
    /// </summary>
    [TestFixture]
    public sealed class BotDifficultyTests
    {
        private static void Normal(WorldOptions o)
        {
            o.BotDifficulty = BotDifficulty.Normal;
            o.AllyBotDifficulty = BotDifficulty.Normal;
        }

        private static WorldOptions Opts(BotDifficulty enemy, BotDifficulty ally) => new WorldOptions
        {
            Seed = 5,
            TeamSize = 5,
            HumanTeam = TeamId.Azure,
            HumanClass = ClassId.Warrior,
            Config = GameConfig.CreateBrowser5v5(),
            BotDifficulty = enemy,
            AllyBotDifficulty = ally,
        };

        [Test]
        public void DG21NormalIsTodaysBotsBitForBitOnTheGoldenSeeds()
        {
            Assert.AreEqual(0x5C912AEED16BC302UL, MatchHash.Run(1, 16, 3600, ClassId.Warrior, Normal), "16v16 seed 1, 2:00");
            Assert.AreEqual(0x7A3D359D8D0DB066UL, MatchHash.Run(7, 16, 3600, ClassId.Miner, Normal), "16v16 seed 7, 2:00");
            Assert.AreEqual(0x0C1ACCC06A2C7E58UL, MatchHash.Run(1337, 16, 3600, ClassId.Farmer, Normal), "16v16 seed 1337, 2:00");
            Assert.AreEqual(0x2980068FA0457CC2UL, MatchHash.Run(42, 5, 14500, ClassId.Warrior, Normal), "5v5 seed 42, whole match");
            // A WorldOptions that never names a difficulty is Normal.
            Assert.AreEqual(0x5C912AEED16BC302UL, MatchHash.Run(1, 16, 3600, ClassId.Warrior));
            Assert.AreEqual(BotDifficulty.Normal, new WorldOptions().BotDifficulty);
        }

        [Test]
        public void DG21EasyAndHardAreDeterministicAndChangeTheMatch()
        {
            Action<WorldOptions> hardVsEasy = o => { o.BotDifficulty = BotDifficulty.Hard; o.AllyBotDifficulty = BotDifficulty.Easy; };
            ulong a = MatchHash.Run(1, 16, 1800, ClassId.Warrior, hardVsEasy);
            Assert.AreEqual(a, MatchHash.Run(1, 16, 1800, ClassId.Warrior, hardVsEasy), "same seed, same difficulty: same hash");
            Assert.AreNotEqual(a, MatchHash.Run(1, 16, 1800, ClassId.Warrior, Normal), "a difficulty changes the match");
            Action<WorldOptions> easy = o => { o.BotDifficulty = BotDifficulty.Easy; o.AllyBotDifficulty = BotDifficulty.Easy; };
            Assert.AreNotEqual(MatchHash.Run(1, 16, 1800, ClassId.Warrior, easy), a);
        }

        [Test]
        public void DG21TheOptionReachesTheWorldAndSurvivesAReset()
        {
            WorldOptions o = Opts(BotDifficulty.Hard, BotDifficulty.Easy);
            World w = World.CreateWorld(o);
            Assert.AreEqual(BotDifficulty.Hard, w.Opts.BotDifficulty);
            Assert.AreEqual(BotDifficulty.Easy, w.Opts.AllyBotDifficulty);
            World.ResetWorld(w, Opts(BotDifficulty.Easy, BotDifficulty.Normal));
            Assert.AreEqual(BotDifficulty.Easy, w.Opts.BotDifficulty);
            Assert.AreEqual(BotDifficulty.Normal, w.Opts.AllyBotDifficulty);
        }

        [Test]
        public void DG21TheProfileFollowsTheTeamAndScalesTheReactionDelay()
        {
            World w = World.CreateWorld(Opts(BotDifficulty.Hard, BotDifficulty.Easy));
            int azure = 1, crimson = -1;
            for (int j = 0; j < w.ActorCount; j++) if (w.Actors[j].Team == TeamId.Crimson) { crimson = j; break; }
            Assert.AreEqual(TeamId.Azure, w.Actors[azure].Team);
            Assert.AreEqual(BotDifficulty.Easy, BotSkill.Of(w, 0), "actor 0 on autopilot thinks like its team");
            Assert.AreEqual(BotDifficulty.Easy, BotSkill.Of(w, azure));
            Assert.AreEqual(BotDifficulty.Hard, BotSkill.Of(w, crimson));
            int normal = BotBrain.BotThinkPeriod(w.Config);
            Assert.AreEqual(8, normal);
            Assert.AreEqual(12, BotSkill.ThinkPeriod(w, azure, normal), "Easy reacts 1.5x slower");
            Assert.AreEqual(4, BotSkill.ThinkPeriod(w, crimson, normal), "Hard reacts twice as fast");
            World n = World.CreateWorld(Opts(BotDifficulty.Normal, BotDifficulty.Normal));
            Assert.IsNull(BotSkill.Profile(n, 1));
            Assert.AreEqual(normal, BotSkill.ThinkPeriod(n, 1, normal));
        }

        [Test]
        public void DG21RollsAreStatelessAimErrorStaysInItsDiscAndOnlyEasyHesitates()
        {
            World w = World.CreateWorld(Opts(BotDifficulty.Hard, BotDifficulty.Easy));
            BotSkillConfig easy = w.Config.Bots.Easy;
            uint rngBefore = w.Rng.State;
            int held = 0;
            for (int t = 0; t < 3000; t++)
            {
                double r = BotSkill.Roll(w, 3, BotSkill.SaltSwing, t);
                Assert.That(r, Is.GreaterThanOrEqualTo(0).And.LessThan(1));
                Assert.AreEqual(r, BotSkill.Roll(w, 3, BotSkill.SaltSwing, t));
                double x = 10, z = 10;
                w.Tick = t;
                BotSkill.Jitter(w, 1, easy, ref x, ref z);
                double d = Math.Sqrt((x - 10) * (x - 10) + (z - 10) * (z - 10));
                Assert.LessOrEqual(d, easy.AimErrorUnits + 1e-5);
                if (BotSkill.HoldsSwing(w, 1)) held++;
                int crimson = w.ActorCount - 1;
                Assert.IsFalse(BotSkill.HoldsSwing(w, crimson), "Hard never hesitates");
            }
            double share = held / 3000.0;
            Assert.That(share, Is.InRange(easy.SwingHesitation - 0.1, easy.SwingHesitation + 0.1), "hesitation share");
            Assert.AreEqual(rngBefore, w.Rng.State, "rolls never draw from a shared RNG");
        }

        [Test]
        public void DG21HardFocusesACarrierOverANearerEmptyHandedEnemy()
        {
            World w = World.CreateWorld(Opts(BotDifficulty.Hard, BotDifficulty.Normal));
            BotSkillConfig hard = w.Config.Bots.Hard;
            int a = 1, b = 2;
            w.Actors[b].Carry = CarryKind.Biomass;
            w.Actors[b].CarryAmount = 1;
            Assert.Less(BotSkill.FocusScore(w, hard, b, 9), BotSkill.FocusScore(w, hard, a, 4), "carrier at 3 beats empty-handed at 2");
            w.Actors[b].CarryAmount = 0;
            w.Actors[a].Hp = w.Actors[a].MaxHp * 0.2;
            Assert.Less(BotSkill.FocusScore(w, hard, a, 4), BotSkill.FocusScore(w, hard, b, 4), "a hurt enemy is preferred at equal range");
        }

        // ------------------------------------------------------------------ outcome bands (Slow)

        /// <summary>One whole 5v5 match from CreateBrowser5v5 with actor 0 on autopilot; returns the winner.</summary>
        private static MatchResult Play(int seed, BotDifficulty azure, BotDifficulty crimson, out double hillA, out double hillC)
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5();
            World w = World.CreateWorld(new WorldOptions
            {
                Seed = seed, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg,
                AllyBotDifficulty = azure, BotDifficulty = crimson,
            });
            InputFrame scratch = SimInput.EmptyInput();
            int max = (int)Math.Ceiling(cfg.MatchSeconds * cfg.SimHz) + (int)cfg.SimHz;
            for (int t = 0; t < max && w.Match.Phase != MatchPhase.Ended; t++) MatchTick.Advance(w, null, scratch);
            hillA = w.Match.HillSeconds.Azure;
            hillC = w.Match.HillSeconds.Crimson;
            return w.Match.Winner;
        }

        /// <summary>Hard bots beat Easy bots in a clear majority of seeded matches, on either side of the map (so side bias cancels).</summary>
        [Test, Category("Slow")]
        public void DG21HardBeatsEasyInAClearMajorityOnBothSides()
        {
            const int seeds = 20;
            int hardAzure = 0, hardCrimson = 0;
            for (int s = 1; s <= seeds; s++)
            {
                if (Play(s, BotDifficulty.Hard, BotDifficulty.Easy, out _, out _) == MatchResult.Azure) hardAzure++;
                if (Play(s, BotDifficulty.Easy, BotDifficulty.Hard, out _, out _) == MatchResult.Crimson) hardCrimson++;
            }
            Console.WriteLine("{\"item\":\"DG-21\",\"hardWinsAsAzure\":" + hardAzure + ",\"hardWinsAsCrimson\":" + hardCrimson + ",\"of\":" + seeds + "}");
            Assert.GreaterOrEqual(hardAzure, HardWinsFloor, "Hard (Azure) vs Easy (Crimson)");
            Assert.GreaterOrEqual(hardCrimson, HardWinsFloor, "Easy (Azure) vs Hard (Crimson)");
        }

        /// <summary>Clear majority: at least 14 of 20 (70%) per side.</summary>
        private const int HardWinsFloor = 14;

        /// <summary>The menu's meaning: the human proxy's team (Normal allies) wins more against Easy opponents than against Hard ones.</summary>
        [Test, Category("Slow")]
        public void DG21TheHumanProxyWinsMoreOftenAgainstEasyThanNormalThanHard()
        {
            const int seeds = 20;
            int vsEasy = 0, vsNormal = 0, vsHard = 0;
            for (int s = 1; s <= seeds; s++)
            {
                if (Play(s, BotDifficulty.Normal, BotDifficulty.Easy, out _, out _) == MatchResult.Azure) vsEasy++;
                if (Play(s, BotDifficulty.Normal, BotDifficulty.Normal, out _, out _) == MatchResult.Azure) vsNormal++;
                if (Play(s, BotDifficulty.Normal, BotDifficulty.Hard, out _, out _) == MatchResult.Azure) vsHard++;
            }
            Console.WriteLine("{\"item\":\"DG-21\",\"proxyWinsVsEasy\":" + vsEasy + ",\"vsNormal\":" + vsNormal + ",\"vsHard\":" + vsHard + ",\"of\":" + seeds + "}");
            Assert.Greater(vsEasy, vsNormal, "Easy opponents are easier than Normal");
            Assert.Greater(vsNormal, vsHard, "Hard opponents are harder than Normal");
        }
    }
}
