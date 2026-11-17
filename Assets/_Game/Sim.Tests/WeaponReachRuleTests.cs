using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b11-c (RULE CHANGE): every class's swing reach comes from ONE table, DepthConfig.WeaponReach (what its weapon really
    /// reaches at the drawn strike), read through Combat.ReachOf. With the rule on, a target whose body surface is 0.01 u
    /// past the table reach is missed and one 0.01 u inside it is hit, for every class; with the rule off (Classic) the
    /// class row's old AttackRange decides. On in the Full profile only; the bots chase at the same reach (BotReachOf).
    /// Instant hits (sim physics Classic) so one CombatSystem call resolves a press, as in LightWeaponsTests.
    /// </summary>
    [TestFixture]
    public sealed class WeaponReachRuleTests
    {
        private const int Me = 0;
        private const double X0 = 56, Z0 = 40, Edge = 0.01;
        private static readonly ClassId[] All = { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder, ClassId.Healer };
        /// <summary>Classes with a swing when the depth lane is off (Demolitions has none; the Healer only mends).</summary>
        private static readonly ClassId[] ClassicSwingers = { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Builder };

        private static GameConfig Cfg(bool depthOn)
        {
            GameConfig c = GameConfig.CreateBrowser5v5();
            c.Physics.Classic = true;    // instant hits: no wind-up
            c.Depth.Classic = !depthOn;  // the weapon-reach table is a mechanics-depth rule
            return c;
        }

        private static void Put(World w, int j, double x, double z)
        {
            Actor a = w.Actors[j];
            a.Alive = true;
            a.Hp = a.MaxHp;
            a.SpawnProtect = 0;
            a.StandUp = 0;
            a.AirLen = 0;
            a.AbilityActive = 0;
            a.AttackCd = 0;
            a.KVx = 0;
            a.KVz = 0;
            w.X[j] = x;
            w.Z[j] = z;
        }

        /// <summary>The human of class cls at (X0, Z0) facing +x, everyone else dead, one Miner enemy (radius 0.45).</summary>
        private static World Field(ClassId cls, bool depthOn, out int enemy)
        {
            World w = World.CreateWorld(new WorldOptions { Seed = 41, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = cls, Config = Cfg(depthOn) });
            for (int j = 1; j < w.ActorCount; j++)
            {
                w.Actors[j].Alive = false;
                w.Actors[j].RespawnIn = 1e9;
            }
            Put(w, Me, X0, Z0);
            w.Actors[Me].Facing = 0;
            enemy = w.ActorCount / 2;
            w.Actors[enemy].Cls = ClassId.Miner;
            w.Actors[enemy].MaxHp = w.Config.Class(ClassId.Miner).MaxHp;
            w.Tick = 100;
            Assert.AreEqual(cls, w.Actors[Me].Cls);
            Assert.AreEqual(TeamId.Crimson, w.Actors[enemy].Team);
            return w;
        }

        /// <summary>One press with the enemy's body surface at surface u ahead: true when it took damage.</summary>
        private static bool Lands(ClassId cls, bool depthOn, double surface)
        {
            World w = Field(cls, depthOn, out int e);
            double radius = w.Config.Class(ClassId.Miner).Radius;
            Put(w, e, X0 + surface + radius, Z0);
            Put(w, Me, X0, Z0);
            double hp0 = w.Actors[e].Hp;
            w.Inputs[Me].Attack = true;
            SpatialHash.Rebuild(w.Hash, w.X, w.Z, w.Actors, w.ActorCount);
            Combat.CombatSystem(w, SimStep.Dt(w.Config));
            w.Inputs[Me].Attack = false;
            Assert.Greater(w.Actors[Me].AttackCd, 0, cls + ": the press swung (cooldown spent)");
            return w.Actors[e].Hp < hp0 - 1e-9;
        }

        [Test]
        public void EveryClassHitsJustInsideTheTableReachAndMissesJustPastIt()
        {
            foreach (ClassId cls in All)
            {
                GameConfig c = Cfg(true);
                double table = c.Depth.WeaponReach.Reach[(int)cls];
                Assert.AreEqual(table, Combat.ReachOf(c, cls), 1e-12, cls + ": the swing reads the table");
                Assert.IsTrue(Lands(cls, true, table - Edge), cls + ": surface at table reach - 0.01 is hit");
                Assert.IsFalse(Lands(cls, true, table + Edge), cls + ": surface at table reach + 0.01 is missed");
            }
        }

        [Test]
        public void ALongerTableRowReachesFurther()
        {
            // the range IS the reach: lengthen one row and the same target is hit
            GameConfig c = Cfg(true);
            double farmer = c.Depth.WeaponReach.Reach[(int)ClassId.Farmer];
            Assert.IsFalse(Lands(ClassId.Farmer, true, farmer + 0.2));
            World w = Field(ClassId.Farmer, true, out int e);
            w.Config.Depth.WeaponReach.Reach[(int)ClassId.Farmer] = farmer + 0.3;
            Put(w, e, X0 + farmer + 0.2 + w.Config.Class(ClassId.Miner).Radius, Z0);
            double hp0 = w.Actors[e].Hp;
            w.Inputs[Me].Attack = true;
            SpatialHash.Rebuild(w.Hash, w.X, w.Z, w.Actors, w.ActorCount);
            Combat.CombatSystem(w, SimStep.Dt(w.Config));
            Assert.Less(w.Actors[e].Hp, hp0, "a 0.3 u longer weapon lands 0.2 u further out");
        }

        [Test]
        public void WithClassicTheOldAttackRangeDecides()
        {
            foreach (ClassId cls in All)
            {
                GameConfig c = Cfg(false);
                Assert.IsFalse(Combat.WeaponReachOn(c), "depth lane off: rule off");
                Assert.AreEqual(c.Class(cls).AttackRange, Combat.ReachOf(c, cls), 1e-12, cls + ": the class row");
            }
            foreach (ClassId cls in ClassicSwingers)
            {
                double range = Cfg(false).Class(cls).AttackRange;
                Assert.IsTrue(Lands(cls, false, range - Edge), cls + ": Classic, surface at AttackRange - 0.01 is hit");
                Assert.IsFalse(Lands(cls, false, range + Edge), cls + ": Classic, surface at AttackRange + 0.01 is missed");
            }
        }

        [Test]
        public void TheRuleRunsInTheFullProfileOnlyAndTheBotsUseTheSameReach()
        {
            Assert.IsTrue(Combat.WeaponReachOn(GameConfig.CreateDefault()), "Full (the game's default)");
            Assert.IsTrue(Combat.WeaponReachOn(GameConfig.CreateDefault().ApplyRules(RulesProfile.Full)));
            Assert.IsFalse(Combat.WeaponReachOn(GameConfig.CreateDefault().ApplyRules(RulesProfile.PhysicsOnly)));
            Assert.IsFalse(Combat.WeaponReachOn(GameConfig.CreateDefault().ApplyRules(RulesProfile.Classic)));
            Assert.IsFalse(Combat.WeaponReachOn(GameConfig.CreateBrowser5v5()), "the PhysicsOnly test config");
            GameConfig off = GameConfig.CreateDefault();
            off.Depth.WeaponReach.Enabled = false;
            Assert.IsFalse(Combat.WeaponReachOn(off), "its own switch");

            World full = World.CreateWorld(new WorldOptions { Seed = 41, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateDefault() });
            World classic = World.CreateWorld(new WorldOptions { Seed = 41, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateBrowser5v5() });
            foreach (ClassId cls in All)
            {
                double want = cls == ClassId.Healer ? full.Config.Class(cls).AttackRange : full.Config.Depth.WeaponReach.Reach[(int)cls];
                Assert.AreEqual(want, Combat.BotReachOf(full, cls), 1e-12, cls + ": bots chase at the swing's reach (Healer: her mend stand-off)");
                Assert.AreEqual(classic.Config.Class(cls).AttackRange, Combat.BotReachOf(classic, cls), 1e-12, cls + ": rule off, the bots as before");
            }
        }

        [Test]
        public void TheLightWeaponsReadTheSameTable()
        {
            GameConfig c = GameConfig.CreateDefault();
            Assert.AreEqual(c.Depth.WeaponReach.Reach[(int)ClassId.Builder], c.Depth.LightWeapons.Builder.Range, 1e-12);
            Assert.AreEqual(c.Depth.WeaponReach.Reach[(int)ClassId.Demolitions], c.Depth.LightWeapons.Demolitions.Range, 1e-12);
            Assert.AreEqual(c.Depth.WeaponReach.Reach[(int)ClassId.Healer], c.Depth.LightWeapons.Healer.Range, 1e-12);
            GameConfig copy = c.Clone();
            copy.Depth.WeaponReach.Reach[0] = 9;
            Assert.AreNotEqual(9, c.Depth.WeaponReach.Reach[0], "Clone copies the table");
            Assert.AreEqual(6, c.Depth.WeaponReach.Reach.Length, "one row per class");
        }

        // ---------------------------------------------------------------- b12-c: bot whiffs, rule on against the control

        private sealed class WhiffRun
        {
            public readonly int[] Swings = new int[6], Whiffs = new int[6], TeamSwings = new int[2], TeamWhiffs = new int[2];
            /// <summary>b13-reach: kills credited to each class (an enemy actor's Death event), all kills, lead changes on hill seconds.</summary>
            public readonly int[] KillsByClass = new int[6];
            public int Kills, LeadChanges;
            public double FirstKoAt = -1, EndTime;
            public double HatchAt = -1, Instability = double.NaN;
            public bool Berserk;
            public MatchResult Winner = MatchResult.Draw;
        }

        /// <summary>
        /// The match config: 16v16 = the game's default (Full, as MovementShiverTests); 5v5 = CreateBrowser5v5 with the depth
        /// lane on (as DepthSoakTests). Rule off = the CONTROL, the game before b11-c: WeaponReach off and the light weapons'
        /// pre-b11-c Range (1.5 / 1.3 / 1.2 u), so the bots' whiffs are compared like with like.
        /// </summary>
        private static GameConfig MatchConfig(int teamSize, bool ruleOn)
        {
            GameConfig c = teamSize >= 16 ? GameConfig.CreateDefault() : GameConfig.CreateBrowser5v5();
            c.Depth.Classic = false;
            if (!ruleOn)
            {
                c.Depth.WeaponReach.Enabled = false;
                c.Depth.LightWeapons.Builder.Range = 1.5;
                c.Depth.LightWeapons.Demolitions.Range = 1.3;
                c.Depth.LightWeapons.Healer.Range = 1.2;
            }
            return c;
        }

        /// <summary>One bot match (every slot bot-driven) for at least minTicks and until the hatch, at most maxTicks.</summary>
        private static WhiffRun Play(int seed, int teamSize, bool ruleOn, int minTicks, int maxTicks)
        {
            GameConfig cfg = MatchConfig(teamSize, ruleOn);
            World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = teamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            InputFrame scratch = SimInput.EmptyInput();
            var r = new WhiffRun();
            int lastHead = 0, lastLeader = 0;
            for (int t = 0; t < maxTicks && w.Match.Phase != MatchPhase.Ended; t++)
            {
                if (t >= minTicks && w.Match.Hatched) break;
                MatchTick.Advance(w, null, scratch);
                int from = Math.Max(w.EventHead >= lastHead ? lastHead : 0, w.EventHead - World.EventCapacity);
                for (int q = from; q < w.EventHead; q++)
                {
                    SimEvent ev = w.Events[q % World.EventCapacity];
                    if (ev.Type == SimEventType.Death && ev.A >= 0 && ev.A < w.ActorCount)
                    {
                        // b13-reach: a player knock-out (the victim is an actor); credited to the killer's class when an enemy actor
                        if (r.FirstKoAt < 0) r.FirstKoAt = w.Match.Time;
                        r.Kills++;
                        if (ev.B >= 0 && ev.B < w.ActorCount && w.Actors[ev.B].Team != w.Actors[ev.A].Team)
                        {
                            int kc = (int)w.Actors[ev.B].Cls;
                            if (kc >= 0 && kc < r.KillsByClass.Length) r.KillsByClass[kc]++;
                        }
                        continue;
                    }
                    if (ev.Type != SimEventType.AttackContact || ev.A < 0 || ev.A >= w.ActorCount) continue;
                    Actor a = w.Actors[ev.A];
                    int c = (int)a.Cls, team = a.Team == TeamId.Azure ? 0 : 1;
                    if (c < 0 || c >= r.Swings.Length) continue;
                    r.Swings[c]++;
                    r.TeamSwings[team]++;
                    if (ev.Value <= 0) { r.Whiffs[c]++; r.TeamWhiffs[team]++; }
                }
                lastHead = w.EventHead;
                double lead = w.Match.HillSeconds.Azure - w.Match.HillSeconds.Crimson;
                int leader = lead > 1e-9 ? 1 : lead < -1e-9 ? -1 : 0;
                if (leader != 0)
                {
                    if (lastLeader != 0 && leader != lastLeader) r.LeadChanges++;
                    lastLeader = leader;
                }
                if (w.Match.Hatched && r.HatchAt < 0)
                {
                    r.HatchAt = w.Match.Time;
                    r.Instability = w.Match.Instability;
                    r.Berserk = w.Match.BerserkLocked;
                }
            }
            r.Winner = w.Match.Winner;
            r.EndTime = w.Match.Time;
            return r;
        }

        private static string Shares(int[] whiffs, int[] swings)
        {
            var sb = new System.Text.StringBuilder();
            for (int c = 0; c < swings.Length; c++)
                sb.Append(c == 0 ? "" : ",").Append('"').Append(((ClassId)c).ToString()).Append("\":\"").Append(whiffs[c]).Append('/').Append(swings[c]).Append('"');
            return sb.ToString();
        }

        /// <summary>
        /// b12-c: bots close to inside the table reach before they swing (BotSteer.PressLands), so the shorter reach must not
        /// raise any class's whiff share (contacts with no actor hit / swings) more than WhiffCeilingOverControl over the
        /// control (the same seeds, rule off, pre-b11-c light-weapon ranges). Prints, per size and arm, the whiffs per swings
        /// by class and by team, and for 16v16 the hatch time, the instability at the hatch and whether Berserk locked, so the
        /// shorter reach's effect on the Berserk line is read like with like.
        /// </summary>
        [Test]
        public void BotsDoNotWhiffMoreWithTheReachRuleThanTheControlOnTheSameSeeds()
        {
            const double WhiffCeilingOverControl = 0.10;
            const int MinSwings = 40;
            var failures = new System.Collections.Generic.List<string>();
            foreach (int size in new[] { 16, 5 })
            {
                int[] seeds = size == 16 ? new[] { 1337, 4242 } : new[] { 1, 2, 3, 4 };
                var on = new WhiffRun();
                var off = new WhiffRun();
                foreach (int seed in seeds)
                    foreach (bool rule in new[] { true, false })
                    {
                        WhiffRun r = Play(seed, size, rule, 7200, 10800);
                        WhiffRun sum = rule ? on : off;
                        for (int c = 0; c < 6; c++) { sum.Swings[c] += r.Swings[c]; sum.Whiffs[c] += r.Whiffs[c]; }
                        for (int k = 0; k < 2; k++) { sum.TeamSwings[k] += r.TeamSwings[k]; sum.TeamWhiffs[k] += r.TeamWhiffs[k]; }
                        Console.WriteLine("{\"item\":\"b12-c whiffs\",\"size\":" + size + ",\"seed\":" + seed + ",\"rule\":\"" + (rule ? "on" : "off (control)") +
                                          "\",\"hatchAt\":" + r.HatchAt.ToString("F0") + ",\"instabilityAtHatch\":" + r.Instability.ToString("F1") +
                                          ",\"berserkLocked\":" + (r.Berserk ? "true" : "false") + ",\"winnerSoFar\":\"" + r.Winner + "\",\"byClass\":{" + Shares(r.Whiffs, r.Swings) +
                                          "},\"azure\":\"" + r.TeamWhiffs[0] + "/" + r.TeamSwings[0] + "\",\"crimson\":\"" + r.TeamWhiffs[1] + "/" + r.TeamSwings[1] + "\"}");
                    }
                for (int c = 0; c < 6; c++)
                {
                    if (on.Swings[c] < MinSwings || off.Swings[c] < MinSwings) continue;
                    double so = (double)on.Whiffs[c] / on.Swings[c], sc = (double)off.Whiffs[c] / off.Swings[c];
                    Console.WriteLine("{\"item\":\"b12-c whiff share\",\"size\":" + size + ",\"class\":\"" + (ClassId)c + "\",\"on\":" + so.ToString("F3") +
                                      ",\"control\":" + sc.ToString("F3") + ",\"ceiling\":" + (sc + WhiffCeilingOverControl).ToString("F3") + "}");
                    if (so > sc + WhiffCeilingOverControl)
                        failures.Add(size + "v" + size + " " + (ClassId)c + ": whiff share " + so.ToString("F3") + " with the rule against " + sc.ToString("F3") + " in the control");
                }
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        // ---------------------------------------------------------------- b12-c2: balance arm and the Healer on seed 101

        /// <summary>One whole bot match (until it ends, at most the match length plus a second), every slot bot-driven.</summary>
        private static WhiffRun PlayFull(int seed, int teamSize, bool ruleOn)
        {
            GameConfig cfg = MatchConfig(teamSize, ruleOn);
            int max = (int)Math.Ceiling(cfg.MatchSeconds * cfg.SimHz) + (int)cfg.SimHz;
            return Play(seed, teamSize, ruleOn, int.MaxValue, max);
        }

        /// <summary>
        /// b12-c2 / b13-reach (RULE CHANGE balance arm): whole bot matches, rule on against the same-seed CONTROL (rule off,
        /// pre-b11-c light-weapon ranges; only WeaponReach's own switch and the light weapons' own Range differ): 5v5 with the
        /// depth lane on (DepthSoakTests.DepthA5v5's config) on seeds 1 to 20, and 16v16 (the game's default) on seeds 1337,
        /// 4242, 7, 99, 2024 and 31337. Prints one line per match and arm (winner, hatch time, first knock-out, kills, lead
        /// changes, whiffs per team and per class) and one summary per size and arm. LIMITS (the batch 13 ORDER's common
        /// limits plus the whiff ceiling), judged per size on the pooled matches: whiff share at most control + 0.10 for a
        /// class with 40+ swings in both arms; team win share within 0.15 of the control (5v5 20 seeds, and all 26 matches
        /// together; 16v16 alone is printed: six matches step in sixths); median first player knock-out within -10% / +20%;
        /// kills per minute within -20% / +15%; each class's kill share within 0.08; hill lead changes within +-30%.
        /// </summary>
        [Test]
        public void TheReachRuleKeepsTheControlsBalanceOnTheSameSeeds()
        {
            const double WinShareLimit = 0.15, WhiffCeilingOverControl = 0.10, KoLow = -0.10, KoHigh = 0.20, KpmLow = -0.20, KpmHigh = 0.15,
                KillShareLimit = 0.08, LeadLimit = 0.30;
            const int MinSwings = 40;
            var failures = new System.Collections.Generic.List<string>();
            var allWins = new int[2, 3];
            int allN = 0;
            foreach (int size in new[] { 5, 16 })
            {
                int[] seeds = size == 16 ? new[] { 1337, 4242, 7, 99, 2024, 31337 } : new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 };
                var arms = new[] { new WhiffRun(), new WhiffRun() };   // 0 = rule on, 1 = control
                var wins = new int[2, 3];                               // azure, crimson, draw
                var hatchSum = new double[2];
                var hatchN = new int[2];
                var berserk = new int[2];
                var minutes = new double[2];
                var firstKo = new[] { new System.Collections.Generic.List<double>(), new System.Collections.Generic.List<double>() };
                foreach (int seed in seeds)
                    for (int arm = 0; arm < 2; arm++)
                    {
                        bool rule = arm == 0;
                        WhiffRun r = PlayFull(seed, size, rule);
                        WhiffRun sum = arms[arm];
                        for (int c = 0; c < 6; c++) { sum.Swings[c] += r.Swings[c]; sum.Whiffs[c] += r.Whiffs[c]; sum.KillsByClass[c] += r.KillsByClass[c]; }
                        for (int k = 0; k < 2; k++) { sum.TeamSwings[k] += r.TeamSwings[k]; sum.TeamWhiffs[k] += r.TeamWhiffs[k]; }
                        sum.Kills += r.Kills;
                        sum.LeadChanges += r.LeadChanges;
                        minutes[arm] += r.EndTime / 60.0;
                        if (r.FirstKoAt >= 0) firstKo[arm].Add(r.FirstKoAt);
                        int wi = r.Winner == MatchResult.Azure ? 0 : r.Winner == MatchResult.Crimson ? 1 : 2;
                        wins[arm, wi]++;
                        allWins[arm, wi]++;
                        if (r.HatchAt >= 0) { hatchSum[arm] += r.HatchAt; hatchN[arm]++; }
                        if (r.Berserk) berserk[arm]++;
                        Console.WriteLine("{\"item\":\"b13-reach balance match\",\"size\":" + size + ",\"seed\":" + seed + ",\"rule\":\"" + (rule ? "on" : "off (control)") +
                                          "\",\"winner\":\"" + r.Winner + "\",\"hatchAt\":" + r.HatchAt.ToString("F0") + ",\"firstKoAt\":" + r.FirstKoAt.ToString("F1") +
                                          ",\"kills\":" + r.Kills + ",\"seconds\":" + r.EndTime.ToString("F0") + ",\"leadChanges\":" + r.LeadChanges +
                                          ",\"berserkLocked\":" + (r.Berserk ? "true" : "false") + ",\"azureWhiffs\":\"" + r.TeamWhiffs[0] + "/" + r.TeamSwings[0] +
                                          "\",\"crimsonWhiffs\":\"" + r.TeamWhiffs[1] + "/" + r.TeamSwings[1] + "\",\"byClass\":{" + Shares(r.Whiffs, r.Swings) + "}}");
                    }
                int n = seeds.Length;
                allN += n;
                var kpm = new double[2];
                var koMedian = new double[2];
                for (int arm = 0; arm < 2; arm++)
                {
                    kpm[arm] = minutes[arm] > 0 ? arms[arm].Kills / minutes[arm] : 0;
                    koMedian[arm] = Median(firstKo[arm]);
                    Console.WriteLine("{\"item\":\"b13-reach balance\",\"size\":" + size + ",\"rule\":\"" + (arm == 0 ? "on" : "off (control)") + "\",\"matches\":" + n +
                                      ",\"azureWins\":" + wins[arm, 0] + ",\"crimsonWins\":" + wins[arm, 1] + ",\"draws\":" + wins[arm, 2] +
                                      ",\"azureShare\":" + ((double)wins[arm, 0] / n).ToString("F2") + ",\"crimsonShare\":" + ((double)wins[arm, 1] / n).ToString("F2") +
                                      ",\"hatchMean\":" + (hatchN[arm] > 0 ? hatchSum[arm] / hatchN[arm] : -1).ToString("F0") + ",\"hatched\":" + hatchN[arm] +
                                      ",\"berserkLocks\":" + berserk[arm] + ",\"medianFirstKo\":" + koMedian[arm].ToString("F1") + ",\"killsPerMinute\":" + kpm[arm].ToString("F2") +
                                      ",\"leadChanges\":" + arms[arm].LeadChanges + ",\"azureWhiffShare\":" + Share(arms[arm].TeamWhiffs[0], arms[arm].TeamSwings[0]).ToString("F3") +
                                      ",\"crimsonWhiffShare\":" + Share(arms[arm].TeamWhiffs[1], arms[arm].TeamSwings[1]).ToString("F3") + ",\"killShares\":{" + KillShares(arms[arm]) + "}}");
                }
                for (int c = 0; c < 6; c++)
                {
                    WhiffRun on = arms[0], off = arms[1];
                    double so = Share(on.Whiffs[c], on.Swings[c]), sc = Share(off.Whiffs[c], off.Swings[c]);
                    bool judged = on.Swings[c] >= MinSwings && off.Swings[c] >= MinSwings;
                    Console.WriteLine("{\"item\":\"b13-reach whiff share\",\"size\":" + size + ",\"class\":\"" + (ClassId)c + "\",\"on\":\"" + on.Whiffs[c] + "/" + on.Swings[c] +
                                      "\",\"onShare\":" + so.ToString("F3") + ",\"control\":\"" + off.Whiffs[c] + "/" + off.Swings[c] + "\",\"controlShare\":" + sc.ToString("F3") +
                                      ",\"ceiling\":" + (sc + WhiffCeilingOverControl).ToString("F3") + ",\"judged\":" + (judged ? "true" : "false (under " + MinSwings + " swings)") +
                                      ",\"verdict\":\"" + (!judged ? "not judged" : so > sc + WhiffCeilingOverControl ? "FAIL" : "PASS") + "\"}");
                    if (judged && so > sc + WhiffCeilingOverControl)
                        failures.Add(size + "v" + size + " " + (ClassId)c + ": whiff share " + so.ToString("F3") + " with the rule against " + sc.ToString("F3") + " in the control (ceiling +" + WhiffCeilingOverControl + ")");
                    double ks = Share(on.KillsByClass[c], Sum(on.KillsByClass)), kc = Share(off.KillsByClass[c], Sum(off.KillsByClass));
                    Limit(failures, size + "v" + size + " " + (ClassId)c + " kill share", ks, kc, Math.Abs(ks - kc) <= KillShareLimit + 1e-9, "within " + KillShareLimit.ToString("F2"));
                }
                Limit(failures, size + "v" + size + " median first knock-out (s)", koMedian[0], koMedian[1],
                      koMedian[1] > 0 && koMedian[0] >= koMedian[1] * (1 + KoLow) - 1e-9 && koMedian[0] <= koMedian[1] * (1 + KoHigh) + 1e-9, "-10% / +20%");
                Limit(failures, size + "v" + size + " kills per minute", kpm[0], kpm[1],
                      kpm[1] > 0 && kpm[0] >= kpm[1] * (1 + KpmLow) - 1e-9 && kpm[0] <= kpm[1] * (1 + KpmHigh) + 1e-9, "-20% / +15%");
                Limit(failures, size + "v" + size + " hill lead changes", arms[0].LeadChanges, arms[1].LeadChanges,
                      Math.Abs(arms[0].LeadChanges - arms[1].LeadChanges) <= LeadLimit * Math.Max(1, arms[1].LeadChanges) + 1e-9, "+-30%");
                for (int team = 0; team < 2; team++)
                {
                    double on = (double)wins[0, team] / n, off = (double)wins[1, team] / n;
                    string name = team == 0 ? "Azure" : "Crimson";
                    if (size == 5) Limit(failures, "5v5 " + name + " win share", on, off, Math.Abs(on - off) <= WinShareLimit + 1e-9, "within 0.15");
                    else Console.WriteLine("{\"item\":\"b13-reach limit\",\"what\":\"16v16 " + name + " win share (printed only, 6 matches)\",\"on\":" + on.ToString("F3") + ",\"control\":" + off.ToString("F3") + "}");
                }
            }
            for (int team = 0; team < 2; team++)
            {
                double on = (double)allWins[0, team] / allN, off = (double)allWins[1, team] / allN;
                Limit(failures, "all " + allN + " matches " + (team == 0 ? "Azure" : "Crimson") + " win share", on, off, Math.Abs(on - off) <= WinShareLimit + 1e-9, "within 0.15");
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>b13-reach: prints one limit line ("what, on, control, limit, PASS/FAIL") and records a failure.</summary>
        private static void Limit(System.Collections.Generic.List<string> failures, string what, double on, double control, bool pass, string limit)
        {
            string line = what + ": on " + on.ToString("F3") + ", control " + control.ToString("F3") + ", limit " + limit + " -> " + (pass ? "PASS" : "FAIL");
            Console.WriteLine("{\"item\":\"b13-reach limit\",\"line\":\"" + line + "\"}");
            if (!pass) failures.Add(line);
        }

        private static double Median(System.Collections.Generic.List<double> v)
        {
            if (v.Count == 0) return -1;
            v.Sort();
            return v.Count % 2 == 1 ? v[v.Count / 2] : 0.5 * (v[v.Count / 2 - 1] + v[v.Count / 2]);
        }

        private static int Sum(int[] v)
        {
            int s = 0;
            for (int i = 0; i < v.Length; i++) s += v[i];
            return s;
        }

        private static string KillShares(WhiffRun r)
        {
            int all = Sum(r.KillsByClass);
            var sb = new System.Text.StringBuilder();
            for (int c = 0; c < r.KillsByClass.Length; c++)
                sb.Append(c == 0 ? "" : ",").Append('"').Append(((ClassId)c).ToString()).Append("\":").Append(Share(r.KillsByClass[c], all).ToString("F3"));
            return sb.ToString();
        }

        private static double Share(int part, int whole) => whole > 0 ? (double)part / whole : 0;

        /// <summary>
        /// b12-c2 (PR #177 point 4): the Healer bot stalled in Retreat on seed 101 (SimPhysicsSoakTests' 16v16 six-class
        /// soak). Runs that soak at every difficulty and fails on any Healer stationary line; each failure line carries
        /// SimPhysicsSoakTests.StuckContext (her health, swing, cooldown, stagger, velocity, knock, walkable cells, every
        /// actor within 2.5 u). Prints per difficulty how many Healer lines and other failure lines there were.
        /// </summary>
        [Test]
        public void TheHealerBotIsNotStuckInRetreatOnSeed101()
        {
            var healer = new System.Collections.Generic.List<string>();
            foreach (BotDifficulty d in new[] { BotDifficulty.Easy, BotDifficulty.Normal, BotDifficulty.Hard })
            {
                var lines = new System.Collections.Generic.List<string>();
                SimPhysicsSoakTests.Soak(101, d, false, lines);
                int mine = 0;
                foreach (string l in lines)
                    if (l.Contains("(Healer, ") && l.Contains(" stationary "))
                    {
                        healer.Add(l);
                        mine++;
                        Console.WriteLine("{\"item\":\"b12-c2 healer seed 101 stuck\",\"line\":\"" + l.Replace("\"", "'") + "\"}");
                    }
                Console.WriteLine("{\"item\":\"b12-c2 healer seed 101\",\"difficulty\":\"" + d + "\",\"rule\":\"" + (Combat.WeaponReachOn(SimPhysicsSoakTests.SixClassMix(false)) ? "on" : "off") +
                                  "\",\"healerStationaryLines\":" + mine + ",\"otherFailureLines\":" + (lines.Count - mine) + ",\"limit\":0}");
            }
            Assert.IsEmpty(healer, "the Healer bot stalls (StuckContext on each line):\n" + string.Join("\n", healer));
        }

        // ---------------------------------------------------------------- b13-reach: mend exempt, rows re-pinned, cost

        /// <summary>
        /// b13-reach (C2 point a): the Healer's MEND is not a weapon swing. A bot press at a hurt ally inside
        /// HealerConfig.MendRange is kept and "lands" (BotSteer.PressLands) whatever the reach table says; with nobody to mend
        /// the same press is a LANTERN swing, kept only inside WeaponReach.HealerBotPressDistance, and that distance lands on
        /// every class's body with her measured 1.34 u row. Prints one line per case.
        /// </summary>
        [Test]
        public void TheHealersMendIsKeptAtMendRangeAndOnlyHerLanternReadsTheTable()
        {
            World w = Field(ClassId.Healer, true, out int enemy);
            GameConfig c = w.Config;
            double mend = c.Abilities.Healer.MendRange;
            const int Ally = 1;
            Assert.AreEqual(TeamId.Azure, w.Actors[Ally].Team);
            Put(w, Ally, X0 + mend - 0.2, Z0);
            w.Actors[Ally].Hp = w.Actors[Ally].MaxHp * 0.5;
            double allyR = c.Class(w.Actors[Ally].Cls).Radius;
            bool keepMend = LightWeapons.BotKeepsPress(w, Me, w.X[Ally], w.Z[Ally]);
            bool landsMend = BotSteer.PressLands(w, Me, w.X[Ally], w.Z[Ally], allyR);
            Console.WriteLine("{\"item\":\"b13-reach healer mend\",\"allyAt\":" + (mend - 0.2).ToString("F2") + ",\"mendRange\":" + mend.ToString("F2") +
                              ",\"tableRow\":" + Combat.ReachOf(c, ClassId.Healer).ToString("F2") + ",\"kept\":" + (keepMend ? "true" : "false") + ",\"lands\":" + (landsMend ? "true" : "false") + "}");
            Assert.IsTrue(keepMend, "a mend press at a hurt ally inside MendRange is kept (not the reach table)");
            Assert.IsTrue(landsMend, "a mend is never judged by the reach table");

            w.Actors[Ally].Hp = w.Actors[Ally].MaxHp;   // nobody to mend: the press is her lantern
            bool keepFar = LightWeapons.BotKeepsPress(w, Me, w.X[Ally], w.Z[Ally]);
            double press = c.Depth.WeaponReach.HealerBotPressDistance;
            Put(w, enemy, X0 + press - 0.05, Z0);
            bool keepIn = LightWeapons.BotKeepsPress(w, Me, w.X[enemy], w.Z[enemy]);
            Put(w, enemy, X0 + press + 0.05, Z0);
            bool keepOut = LightWeapons.BotKeepsPress(w, Me, w.X[enemy], w.Z[enemy]);
            Console.WriteLine("{\"item\":\"b13-reach healer lantern\",\"pressDistance\":" + press.ToString("F2") + ",\"keptAtFullAlly\":" + (keepFar ? "true" : "false") +
                              ",\"keptJustInside\":" + (keepIn ? "true" : "false") + ",\"keptJustOutside\":" + (keepOut ? "true" : "false") + "}");
            Assert.IsFalse(keepFar, "with no hurt ally the far press is a lantern swing at air: dropped");
            Assert.IsTrue(keepIn, "lantern press inside HealerBotPressDistance kept");
            Assert.IsFalse(keepOut, "lantern press outside HealerBotPressDistance dropped");

            Assert.AreEqual(1.34, Combat.ReachOf(c, ClassId.Healer), 1e-12, "her lantern row is the measured 1.34 u");
            foreach (ClassId cls in All)
            {
                double land = Combat.ReachOf(c, ClassId.Healer) + c.Class(cls).Radius - BotSteer.BotLandMargin;
                Console.WriteLine("{\"item\":\"b13-reach healer lantern lands\",\"target\":\"" + cls + "\",\"pressDistance\":" + press.ToString("F2") + ",\"landsWithin\":" + land.ToString("F2") +
                                  ",\"verdict\":\"" + (press <= land ? "PASS" : "FAIL") + "\"}");
                Assert.LessOrEqual(press, land, cls + ": a lantern press decided at the press distance lands on this body");
            }
        }

        /// <summary>
        /// b13-reach (C2 point b): every row is the measured value of the batch 12 C2 combined run (unity-ci 43968649842,
        /// EditMode WeaponReachTests "[CM.WeaponReach] &lt;class&gt;: measured reach X u, table Y u"), within 0.05 u. The EditMode
        /// test re-measures the drawn models; this sim-side copy keeps the table honest in the no-Unity sim gate.
        /// </summary>
        [Test]
        public void EveryReachRowIsTheMeasuredBatch12Value()
        {
            double[] measured = { 1.47, 0.96, 1.37, 1.19, 1.20, 1.34 };
            GameConfig c = GameConfig.CreateDefault();
            foreach (ClassId cls in All)
            {
                double row = c.Depth.WeaponReach.Reach[(int)cls];
                double m = measured[(int)cls];
                bool ok = Math.Abs(row - m) <= 0.05 + 1e-9;
                Console.WriteLine("{\"item\":\"b13-reach row\",\"class\":\"" + cls + "\",\"measured\":" + m.ToString("F2") + ",\"table\":" + row.ToString("F2") + ",\"limit\":0.05,\"verdict\":\"" + (ok ? "PASS" : "FAIL") + "\"}");
                Assert.IsTrue(ok, cls + ": table " + row + " vs measured " + m);
            }
        }

        /// <summary>
        /// b13-reach (ORDER performance limits): 16v16 seed 4242 Full, 600 warm-up ticks, then 300 ticks per arm in alternating
        /// 50-tick blocks. Prints the allocated bytes of the whole tick per arm, the bytes of the rule's own calls (ReachOf,
        /// BotReachOf, PressLands, BotKeepsPress for every living actor each tick: must be 0 B), and the mean sim microseconds
        /// per tick rule on against the control (limit +10%).
        /// </summary>
        [Test]
        public void TheReachRuleAddsNoAllocationAndAtMostTenPercentSimCost()
        {
            const int Warm = 600, Ticks = 300, Block = 50;
            var worlds = new World[2];
            var scratch = new InputFrame[2];
            for (int arm = 0; arm < 2; arm++)
            {
                worlds[arm] = World.CreateWorld(new WorldOptions { Seed = 4242, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = MatchConfig(16, arm == 0) });
                scratch[arm] = SimInput.EmptyInput();
                for (int t = 0; t < Warm; t++) MatchTick.Advance(worlds[arm], null, scratch[arm]);
            }
            var alloc = new long[2];
            var clock = new long[2];
            long own = 0;
            double sink = 0;
            for (int b = 0; b < Ticks / Block; b++)
                for (int arm = 0; arm < 2; arm++)
                {
                    World w = worlds[arm];
                    for (int t = 0; t < Block; t++)
                    {
                        long a0 = GC.GetAllocatedBytesForCurrentThread();
                        long c0 = System.Diagnostics.Stopwatch.GetTimestamp();
                        MatchTick.Advance(w, null, scratch[arm]);
                        long c1 = System.Diagnostics.Stopwatch.GetTimestamp();
                        long a1 = GC.GetAllocatedBytesForCurrentThread();
                        alloc[arm] += a1 - a0;
                        clock[arm] += c1 - c0;
                        if (arm != 0) continue;
                        long o0 = GC.GetAllocatedBytesForCurrentThread();
                        int n = w.ActorCount;
                        for (int id = 0; id < n; id++)
                        {
                            Actor a = w.Actors[id];
                            if (!a.Alive) continue;
                            int o = (id + n / 2) % n;
                            sink += Combat.ReachOf(w, a.Cls) + Combat.BotReachOf(w, a.Cls);
                            if (BotSteer.PressLands(w, id, w.X[o], w.Z[o], 0.45)) sink += 1;
                            if (LightWeapons.BotKeepsPress(w, id, w.X[o], w.Z[o])) sink += 1;
                        }
                        own += GC.GetAllocatedBytesForCurrentThread() - o0;
                    }
                }
            double usOn = clock[0] * 1e6 / System.Diagnostics.Stopwatch.Frequency / Ticks;
            double usOff = clock[1] * 1e6 / System.Diagnostics.Stopwatch.Frequency / Ticks;
            Console.WriteLine("{\"item\":\"b13-reach allocation\",\"ruleOwnCallsBytes\":" + own + ",\"wholeTickBytesOn\":" + alloc[0] + ",\"wholeTickBytesControl\":" + alloc[1] +
                              ",\"ticks\":" + Ticks + ",\"limit\":\"0 B for the rule's own calls; whole tick on at most the control's\",\"sink\":" + (sink > 0 ? 1 : 0) + "}");
            Console.WriteLine("{\"item\":\"b13-reach sim cost\",\"usPerTickOn\":" + usOn.ToString("F1") + ",\"usPerTickControl\":" + usOff.ToString("F1") +
                              ",\"ratio\":" + (usOff > 0 ? usOn / usOff : 0).ToString("F3") + ",\"limit\":1.10,\"verdict\":\"" + (usOn <= usOff * 1.10 ? "PASS" : "FAIL") + "\"}");
            Assert.AreEqual(0, own, "the reach rule's own calls allocate nothing");
            Assert.LessOrEqual(alloc[0], alloc[1], "the rule adds no allocation to the tick");
            Assert.LessOrEqual(usOn, usOff * 1.10, "sim cost rule on at most control + 10%");
        }
    }
}
