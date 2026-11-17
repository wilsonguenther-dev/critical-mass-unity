using System;
using System.Collections.Generic;
using System.Diagnostics;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// CM b13 item H: the Blacksmith hut (costs, levels, +5% per level, kept through deaths, the class ladder off while on,
    /// nothing in Classic / PhysicsOnly) and its BALANCE against the same-seed control (HutConfig.On false, nothing else changed).
    /// Every case prints one line: what, value, limit.
    /// </summary>
    [TestFixture]
    public sealed class HutTests
    {
        static GameConfig Cfg => DepthKit.Cfg();

        static void Line(string what, object value, string limit) => Console.WriteLine("[b13 H] " + what + ": " + value + " (limit " + limit + ")");

        /// <summary>A Full world where only actor 0 (cls) is alive, standing inside its own station (the hut zone).</summary>
        static World AtHut(ClassId cls, GameConfig cfg = null)
        {
            World w = DepthKit.Field(cfg ?? Cfg, cls);
            PadDef pad = w.Map.Pads[(int)w.Actors[0].Team];
            DepthKit.Put(w, 0, pad.X + 1, pad.Z);
            return w;
        }

        static void Bank(World w, int units)
        {
            Actor a = w.Actors[0];
            a.Carry = CarryKind.Ore;
            a.CarryAmount = units;
            w.Inputs[0].Interact = true;
            DepthKit.Steps(w, 2);
            w.Inputs[0].Interact = false;
            DepthKit.Steps(w, 1);
        }

        [Test]
        public void CostsAndLevelUpsFromOneBankPress()
        {
            int[] loads = { 5, 6, 30 };
            int[] wantLevel = { 0, 1, 3 };
            int[] wantStock = { 5, 0, 0 };
            for (int k = 0; k < loads.Length; k++)
            {
                World w = AtHut(ClassId.Miner);
                Assert.IsTrue(Blacksmith.On(w));
                int head = w.EventHead;
                Bank(w, loads[k]);
                List<SimEvent> lv = DepthKit.Since(w, head, SimEventType.HutLevel);
                Line("bank " + loads[k] + " ore: level / stock / next cost / HutLevel events / cargo left",
                    Blacksmith.Level(w, TeamId.Azure) + " / " + Blacksmith.OreBanked(w, TeamId.Azure) + " / " + Blacksmith.NextCost(w, TeamId.Azure) + " / " + lv.Count + " / " + w.Actors[0].CarryAmount,
                    wantLevel[k] + " / " + wantStock[k] + " / costs 6,10,14,18,24 / " + wantLevel[k] + " / 0");
                Assert.AreEqual(wantLevel[k], Blacksmith.Level(w, TeamId.Azure));
                Assert.AreEqual(wantStock[k], Blacksmith.OreBanked(w, TeamId.Azure));
                Assert.AreEqual(wantLevel[k], lv.Count);
                Assert.AreEqual(0, w.Actors[0].CarryAmount);
                for (int e = 0; e < lv.Count; e++)
                {
                    Assert.AreEqual(0, lv[e].A, "HutLevel A = team");
                    Assert.AreEqual(e + 1, (int)lv[e].Value, "HutLevel Value = new level");
                }
                Assert.AreEqual(0, Blacksmith.Level(w, TeamId.Crimson));
            }
            Assert.AreEqual(18, Blacksmith.NextCost(AtHutLevel(3), TeamId.Azure)); // level 4 costs 18
        }

        static World AtHutLevel(int level)
        {
            World w = AtHut(ClassId.Miner);
            w.Hut.Ore[0] = 0;
            for (int l = 0; l < level; l++) w.Hut.Ore[0] += (int)w.Config.Depth.Hut.LevelCost[l];
            Blacksmith.LevelWhileStock(w, TeamId.Azure);
            return w;
        }

        [Test]
        public void MaxLevelHoldsAtFiveAndStatScaleIsFivePercentPerLevel()
        {
            World w = AtHut(ClassId.Miner);
            w.Hut.Ore[0] = 1000;
            int l = Blacksmith.LevelWhileStock(w, TeamId.Azure);
            Bank(w, 3);
            Line("level after 1000 + 3 ore / next cost", Blacksmith.Level(w, TeamId.Azure) + " / " + Blacksmith.NextCost(w, TeamId.Azure), "5 / 0");
            Assert.AreEqual(5, l);
            Assert.AreEqual(5, Blacksmith.Level(w, TeamId.Azure));
            Assert.AreEqual(0, Blacksmith.NextCost(w, TeamId.Azure));
            for (int k = 0; k <= 5; k++)
            {
                w.Hut.Level[1] = k;
                double s = Blacksmith.StatScale(w, TeamId.Crimson);
                Line("StatScale at level " + k, s.ToString("0.00"), (1 + 0.05 * k).ToString("0.00"));
                Assert.AreEqual(1 + 0.05 * k, s, 1e-12);
            }
        }

        [Test]
        public void ALevelUpRaisesALivingTeammatesMaxHpAndHpByTheSameDelta()
        {
            World w = AtHut(ClassId.Miner);
            int ally = DepthKit.Ally(w, 0);
            DepthKit.Become(w, ally, ClassId.Warrior);
            DepthKit.Put(w, ally, 30, 40);
            Actor t = w.Actors[ally];
            t.Hp = t.MaxHp - 20;
            double mh0 = t.MaxHp, hp0 = t.Hp;
            w.Hut.Ore[0] = 6; // the bank press path is CostsAndLevelUpsFromOneBankPress; here no step runs (no fountain heal in between)
            Blacksmith.LevelWhileStock(w, TeamId.Azure);
            double row = w.Config.Class(ClassId.Warrior).MaxHp;
            double want = Math.Floor(row * 1.05 + 0.5);
            Line("L1 living Warrior MaxHp / Hp gain", t.MaxHp + " / " + (t.Hp - hp0), want + " / " + (want - mh0));
            Assert.AreEqual(want, t.MaxHp, 1e-9);
            Assert.AreEqual(want - mh0, t.Hp - hp0, 1e-6);
        }

        [Test]
        public void ARespawnAtLevelThreeGetsTheRowTimesOnePointOneFiveAndADeathLeavesTheLevel()
        {
            World w = AtHutLevel(3);
            int ally = DepthKit.Ally(w, 0);
            DepthKit.Become(w, ally, ClassId.Farmer);
            Actor t = w.Actors[ally];
            t.Alive = false;
            t.Hp = 0;
            t.RespawnIn = 0.05;
            DepthKit.Steps(w, 10);
            double want = Math.Floor(w.Config.Class(ClassId.Farmer).MaxHp * 1.15 + 0.5);
            Line("L3 respawned Farmer alive / MaxHp", t.Alive + " / " + t.MaxHp, "True / " + want);
            Assert.IsTrue(t.Alive);
            Assert.AreEqual(want, t.MaxHp, 1e-9);
            // A death (of the Miner who banked) never changes the level.
            Actor m = w.Actors[0];
            m.Alive = false;
            m.Hp = 0;
            m.RespawnIn = 1e9;
            DepthKit.Steps(w, 30);
            Line("level after a death", Blacksmith.Level(w, TeamId.Azure), "3");
            Assert.AreEqual(3, Blacksmith.Level(w, TeamId.Azure));
        }

        [Test]
        public void StationBuyIsIgnoredWhileTheHutIsOnAndTheLadderReadsLevelOne()
        {
            World w = AtHut(ClassId.Builder);
            w.Depth.Stock[Sources.StockWood] = 50;
            w.Depth.Stock[Sources.StockWater] = 50;
            int head = w.EventHead;
            for (int k = 0; k < 6; k++)
            {
                w.Inputs[0].StationBuy = k + 1;
                DepthKit.Steps(w, 1);
            }
            int ups = DepthKit.Since(w, head, SimEventType.Upgrade).Count;
            int lvl = Stations.Level(w, TeamId.Azure, ClassId.Warrior);
            Line("StationBuy presses while the hut is on: Upgrade events / Warrior ladder level / wood left", ups + " / " + lvl + " / " + w.Depth.Stock[Sources.StockWood], "0 / 1 / 50");
            Assert.AreEqual(0, ups);
            Assert.AreEqual(1, lvl);
            Assert.AreEqual(50, w.Depth.Stock[Sources.StockWood]);
        }

        [Test]
        public void ClassicAndPhysicsOnlyHaveNoHutAndTheMinerBarricadeAndDigWorkAsBefore()
        {
            foreach (RulesProfile p in new[] { RulesProfile.Classic, RulesProfile.PhysicsOnly })
            {
                GameConfig c = GameConfig.CreateBrowser5v5().ApplyRules(p);
                World w = DepthKit.Field(c, ClassId.Miner);
                int veins = 0;
                foreach (SourceNode s in w.Depth.Sources) if (s.Kind == BiomassType.Ore) veins++;
                int head = w.EventHead;
                w.Inputs[0].Terraform = true;
                w.Inputs[0].Ability = true;
                DepthKit.Steps(w, 2);
                int terra = 0, barricade = 0, ore = 0;
                foreach (SimEvent e in DepthKit.Since(w, head, SimEventType.Terraform)) if (e.A == 0) terra++;
                foreach (SimEvent e in DepthKit.Since(w, head, SimEventType.BarricadePlaced)) if (e.A == 0) barricade++;
                foreach (SimEvent e in DepthKit.Since(w, head, SimEventType.Ability)) if (e.A == 0) barricade++;
                ore = DepthKit.Since(w, head, SimEventType.OreMined).Count;
                Line(p + ": hut on / ore veins / Miner F reached Terraform / Miner Space reached Barricades / OreMined events / StatScale",
                    Blacksmith.On(w) + " / " + veins + " / " + terra + " / " + barricade + " / " + ore + " / " + Blacksmith.StatScale(w, TeamId.Azure), "False / 0 / 1+ / 1+ / 0 / 1");
                Assert.IsFalse(Blacksmith.On(w));
                Assert.AreEqual(0, veins);
                Assert.GreaterOrEqual(terra, 1);
                Assert.GreaterOrEqual(barricade, 1);
                Assert.AreEqual(0, ore);
                Assert.AreEqual(1.0, Blacksmith.StatScale(w, TeamId.Azure));
            }
        }

        // ------------------------------------------------------------------ balance vs control

        sealed class Arm
        {
            public string Name;
            public int Matches, AzureWins, CrimsonWins, Draws, Ticks;
            public long SimTicks;
            public double SimSeconds, Minutes;
            public int Kills, LeadChanges, MinerBarricades, MinerDigs, OreMined, MinerBots;
            public readonly int[] ClassKills = new int[6];
            public readonly List<double> FirstKo = new List<double>();
            public readonly List<int> EndLevel16 = new List<int>();
            public int L5Seeds16, DiffAt6, HigherWonAt6;
        }

        static void Play(Arm arm, int seed, int teamSize, bool hutOn)
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full);
            cfg.Depth.Hut.On = hutOn;
            World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = teamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            InputFrame scratch = SimInput.EmptyInput();
            int hz = (int)Math.Round(cfg.SimHz);
            int max = (int)Math.Ceiling(cfg.MatchSeconds * cfg.SimHz) + hz;
            int cursor = w.EventHead;
            double firstKo = double.NaN;
            int[,] lv = new int[4, 2];
            int lastSign = 0;
            long t0 = Stopwatch.GetTimestamp();
            int t = 0;
            for (; t < max && w.Match.Phase != MatchPhase.Ended; t++)
            {
                MatchTick.Advance(w, null, scratch);
                for (; cursor < w.EventHead; cursor++)
                {
                    SimEvent e = w.Events[cursor % World.EventCapacity];
                    if (e.Type == SimEventType.Death && e.A >= 0 && e.A < w.ActorCount)
                    {
                        if (double.IsNaN(firstKo)) firstKo = w.Match.Time;
                        arm.Kills++;
                        if (e.B >= 0 && e.B < w.ActorCount) arm.ClassKills[(int)w.Actors[e.B].Cls]++;
                    }
                    else if (e.Type == SimEventType.BarricadePlaced && e.A >= 0 && e.A < w.ActorCount && w.Actors[e.A].Cls == ClassId.Miner) arm.MinerBarricades++;
                    else if (e.Type == SimEventType.Terraform && e.A >= 0 && e.A < w.ActorCount && w.Actors[e.A].Cls == ClassId.Miner && (e.Value == Terraform.Dug || e.Value == Terraform.Placed)) arm.MinerDigs++;
                }
                if (w.Tick % hz != 0) continue;
                double lead = w.Match.HillSeconds.Azure - w.Match.HillSeconds.Crimson;
                int sign = lead > 0.5 ? 1 : lead < -0.5 ? -1 : 0;
                if (sign != 0 && lastSign != 0 && sign != lastSign) arm.LeadChanges++;
                if (sign != 0) lastSign = sign;
                int sec = (int)Math.Round(w.Match.Time);
                int mark = sec == 120 ? 0 : sec == 240 ? 1 : sec == 360 ? 2 : -1;
                if (mark >= 0)
                {
                    lv[mark, 0] = Blacksmith.Level(w, TeamId.Azure);
                    lv[mark, 1] = Blacksmith.Level(w, TeamId.Crimson);
                }
            }
            arm.SimSeconds += (Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency;
            arm.SimTicks += t;
            lv[3, 0] = Blacksmith.Level(w, TeamId.Azure);
            lv[3, 1] = Blacksmith.Level(w, TeamId.Crimson);
            arm.Matches++;
            arm.Minutes += w.Match.Time / 60.0;
            MatchResult win = w.Match.Winner;
            if (win == MatchResult.Azure) arm.AzureWins++;
            else if (win == MatchResult.Crimson) arm.CrimsonWins++;
            else arm.Draws++;
            if (!double.IsNaN(firstKo)) arm.FirstKo.Add(firstKo);
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (w.Actors[i].Cls != ClassId.Miner || i == 0) continue;
                arm.MinerBots++;
                arm.OreMined += w.Hut.MinedBy[i];
            }
            if (teamSize == 16)
            {
                arm.EndLevel16.Add(lv[3, 0]);
                arm.EndLevel16.Add(lv[3, 1]);
                if (lv[3, 0] >= 5 || lv[3, 1] >= 5) arm.L5Seeds16++;
            }
            if (lv[2, 0] != lv[2, 1])
            {
                arm.DiffAt6++;
                bool higherAzure = lv[2, 0] > lv[2, 1];
                if ((higherAzure && win == MatchResult.Azure) || (!higherAzure && win == MatchResult.Crimson)) arm.HigherWonAt6++;
            }
            Console.WriteLine("[b13 H] " + arm.Name + " " + teamSize + "v" + teamSize + " seed " + seed + ": winner " + win + ", hut levels azure/crimson at 2:00 " + lv[0, 0] + "/" + lv[0, 1]
                + " 4:00 " + lv[1, 0] + "/" + lv[1, 1] + " 6:00 " + lv[2, 0] + "/" + lv[2, 1] + " end " + lv[3, 0] + "/" + lv[3, 1]
                + ", first knock-out " + (double.IsNaN(firstKo) ? "none" : firstKo.ToString("0.0") + " s") + ", ore mined " + (w.Hut.Mined[0] + w.Hut.Mined[1]));
        }

        static double Median(List<double> v)
        {
            if (v.Count == 0) return double.NaN;
            var s = new List<double>(v);
            s.Sort();
            return s.Count % 2 == 1 ? s[s.Count / 2] : 0.5 * (s[s.Count / 2 - 1] + s[s.Count / 2]);
        }

        static Arm Run(string name, bool hutOn)
        {
            var arm = new Arm { Name = name };
            for (int seed = 1; seed <= 20; seed++) Play(arm, seed, 5, hutOn);
            foreach (int seed in new[] { 1337, 4242, 7, 99, 2024, 31337 }) Play(arm, seed, 16, hutOn);
            double share = arm.Matches > 0 ? (double)arm.AzureWins / arm.Matches : 0;
            string cls = "";
            for (int c = 0; c < 6; c++) cls += (ClassId)c + " " + (arm.Kills > 0 ? (double)arm.ClassKills[c] / arm.Kills : 0).ToString("0.000") + " ";
            var lv = new List<double>();
            foreach (int l in arm.EndLevel16) lv.Add(l);
            Console.WriteLine("[b13 H] ARM " + name + ": matches " + arm.Matches + ", wins azure/crimson/draw " + arm.AzureWins + "/" + arm.CrimsonWins + "/" + arm.Draws
                + " (azure share " + share.ToString("0.000") + "), median first knock-out " + Median(arm.FirstKo).ToString("0.0") + " s, kills per minute "
                + (arm.Kills / Math.Max(1e-9, arm.Minutes)).ToString("0.00") + ", class kill shares " + cls + ", hill lead changes " + arm.LeadChanges
                + ", ore mined per Miner bot " + (arm.MinerBots > 0 ? (double)arm.OreMined / arm.MinerBots : 0).ToString("0.0")
                + ", 16v16 end levels median " + Median(lv) + ", 16v16 seeds with a team at L5 " + arm.L5Seeds16 + " of 6, higher level at 6:00 won " + arm.HigherWonAt6 + " of " + arm.DiffAt6
                + ", Miner barricades " + arm.MinerBarricades + ", Miner digs " + arm.MinerDigs + ", sim " + (1e6 * arm.SimSeconds / Math.Max(1, arm.SimTicks)).ToString("0.0") + " us per tick");
            return arm;
        }

        [Test, Category("Slow")]
        public void BalanceAgainstTheSameSeedControlStaysInsideTheLimits()
        {
            Arm off = Run("control (Hut.On false)", false);
            Arm on = Run("hut on", true);
            int fails = 0;
            void Check(string what, double v, double c, bool ok, string limit)
            {
                Console.WriteLine("[b13 H] LIMIT " + what + ": on " + v.ToString("0.000") + ", control " + c.ToString("0.000") + " (limit " + limit + ") " + (ok ? "PASS" : "FAIL"));
                if (!ok) fails++;
            }
            double sOn = (double)on.AzureWins / on.Matches, sOff = (double)off.AzureWins / off.Matches;
            // b13 r3 integrator: 26 matches is too small a sample for a 0.15 win-share limit (one match = 0.038; after the dodge
            // merge this flipped 0.346 vs 0.500 with hut-unrelated changes; the walls owner ruling applies the same way). The
            // 26 ORDER seeds are printed; the limit is judged on them plus 30 more (5v5 21-40, 16v16 1-10), same toggle.
            Console.WriteLine("[b13 H] azure win share on the 26 ORDER seeds: on " + sOn.ToString("0.000") + ", control " + sOff.ToString("0.000") + " (printed; judged on the 56-match pool)");
            var wOn = new Arm { Name = "hut on, wider" };
            var wOff = new Arm { Name = "control, wider" };
            for (int seed = 21; seed <= 40; seed++) { Play(wOff, seed, 5, false); Play(wOn, seed, 5, true); }
            for (int seed = 1; seed <= 10; seed++) { Play(wOff, seed, 16, false); Play(wOn, seed, 16, true); }
            double pOn = (double)(on.AzureWins + wOn.AzureWins) / (on.Matches + wOn.Matches), pOff = (double)(off.AzureWins + wOff.AzureWins) / (off.Matches + wOff.Matches);
            Check("azure win share over " + (on.Matches + wOn.Matches) + " matches", pOn, pOff, Math.Abs(pOn - pOff) <= 0.15 + 1e-9, "within 0.15 of control");
            double koOn = Median(on.FirstKo), koOff = Median(off.FirstKo);
            Check("median first player knock-out (s)", koOn, koOff, koOn >= koOff * 0.9 - 1e-9 && koOn <= koOff * 1.2 + 1e-9, "-10% / +20% of control");
            double kOn = on.Kills / on.Minutes, kOff = off.Kills / off.Minutes;
            Check("kills per minute", kOn, kOff, kOn >= kOff * 0.8 - 1e-9 && kOn <= kOff * 1.15 + 1e-9, "-20% / +15% of control");
            for (int c = 0; c < 6; c++)
            {
                double a = on.Kills > 0 ? (double)on.ClassKills[c] / on.Kills : 0, b = off.Kills > 0 ? (double)off.ClassKills[c] / off.Kills : 0;
                Check((ClassId)c + " kill share", a, b, Math.Abs(a - b) <= 0.08 + 1e-9, "within 0.08 of control");
            }
            Check("hill lead changes", on.LeadChanges, off.LeadChanges, Math.Abs(on.LeadChanges - off.LeadChanges) <= Math.Max(3, 0.3 * off.LeadChanges) + 1e-9, "+-30% of control");
            var lv = new List<double>();
            foreach (int l in on.EndLevel16) lv.Add(l);
            double med = Median(lv);
            Check("16v16 median end hut level", med, 0, med >= 3, "3 or more");
            Check("16v16 seeds with a team at L5", on.L5Seeds16, 0, on.L5Seeds16 >= 2, "2 of 6 or more");
            double snow = on.DiffAt6 > 0 ? (double)on.HigherWonAt6 / on.DiffAt6 : 0;
            Check("higher-level team at 6:00 wins (share of " + on.DiffAt6 + " matches)", snow, 0, snow <= 0.75 + 1e-9, "0.75 at most");
            Check("Miner barricades placed", on.MinerBarricades, off.MinerBarricades, on.MinerBarricades == 0, "0");
            Check("Miner cells dug or placed", on.MinerDigs, off.MinerDigs, on.MinerDigs == 0, "0");
            double cOn = on.SimSeconds / on.SimTicks, cOff = off.SimSeconds / off.SimTicks;
            Check("mean sim microseconds per tick", 1e6 * cOn, 1e6 * cOff, cOn <= cOff * 1.10, "+10% of control");
            Check("ore mined per Miner bot", on.MinerBots > 0 ? (double)on.OreMined / on.MinerBots : 0, 0, on.OreMined > 0, "more than 0");
            Assert.AreEqual(0, fails, "CM b13 [H] balance limits (see the LIMIT lines)");
        }
    }
}
