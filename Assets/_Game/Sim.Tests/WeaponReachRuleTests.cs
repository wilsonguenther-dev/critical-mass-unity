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
            int lastHead = 0;
            for (int t = 0; t < maxTicks && w.Match.Phase != MatchPhase.Ended; t++)
            {
                if (t >= minTicks && w.Match.Hatched) break;
                MatchTick.Advance(w, null, scratch);
                int from = Math.Max(w.EventHead >= lastHead ? lastHead : 0, w.EventHead - World.EventCapacity);
                for (int q = from; q < w.EventHead; q++)
                {
                    SimEvent ev = w.Events[q % World.EventCapacity];
                    if (ev.Type != SimEventType.AttackContact || ev.A < 0 || ev.A >= w.ActorCount) continue;
                    Actor a = w.Actors[ev.A];
                    int c = (int)a.Cls, team = a.Team == TeamId.Azure ? 0 : 1;
                    if (c < 0 || c >= r.Swings.Length) continue;
                    r.Swings[c]++;
                    r.TeamSwings[team]++;
                    if (ev.Value <= 0) { r.Whiffs[c]++; r.TeamWhiffs[team]++; }
                }
                lastHead = w.EventHead;
                if (w.Match.Hatched && r.HatchAt < 0)
                {
                    r.HatchAt = w.Match.Time;
                    r.Instability = w.Match.Instability;
                    r.Berserk = w.Match.BerserkLocked;
                }
            }
            r.Winner = w.Match.Winner;
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
    }
}
