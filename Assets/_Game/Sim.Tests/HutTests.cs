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
        // b15: these cases pin the b13 flat hut (+5% a level); the b15 hut choices are B15RulesTests'
        static GameConfig Cfg => DepthKit.Cfg(d => d.HutChoices.Enabled = false);

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
            public int L5Seeds16, DiffAt6, HigherWonAt6, Matches16;
            public readonly WinShare.Tally T16 = new WinShare.Tally(), T5 = new WinShare.Tally();
            /// <summary>Hill lead changes of each match in play order (paired by index with the other arm's same seed).</summary>
            public readonly List<double> MatchLeadChanges = new List<double>();
        }

        static void Play(Arm arm, int seed, int teamSize, bool hutOn)
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full);
            cfg.Depth.Hut.On = hutOn;
            // b15 wave two: the b13 H limits judge the b13 hut against no hut. PR #219's hut CHOICES (on by default) only exist
            // with a hut, so with them on the "hut" arm also carried the choices: hill lead changes 448 vs 249 over 112 matches
            // (slow run 44857709950), a wave-two target the balance report measures (median 2 -> 4 at 72 seeds). Off in both arms.
            cfg.Depth.HutChoices.Enabled = false;
            World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = teamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            InputFrame scratch = SimInput.EmptyInput();
            int hz = (int)Math.Round(cfg.SimHz);
            int max = (int)Math.Ceiling(cfg.MatchSeconds * cfg.SimHz) + hz;
            int cursor = w.EventHead;
            double firstKo = double.NaN;
            int[,] lv = new int[4, 2];
            int lastSign = 0;
            int lc0 = arm.LeadChanges;
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
            arm.MatchLeadChanges.Add(arm.LeadChanges - lc0);
            MatchResult win = w.Match.Winner;
            if (win == MatchResult.Azure) arm.AzureWins++;
            else if (win == MatchResult.Crimson) arm.CrimsonWins++;
            else arm.Draws++;
            (teamSize == 16 ? arm.T16 : arm.T5).Add(win);
            if (!double.IsNaN(firstKo)) arm.FirstKo.Add(firstKo);
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (w.Actors[i].Cls != ClassId.Miner || i == 0) continue;
                arm.MinerBots++;
                arm.OreMined += w.Hut.MinedBy[i];
            }
            if (teamSize == 16)
            {
                arm.Matches16++;
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
            foreach (int seed in WinShare.OrderSeeds16) Play(arm, seed, 16, hutOn);
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
            // b14-c item 5 (approved): the win share is judged by the one helper (WinShare) on the 16v16 pool of 24 seeds (the
            // six ORDER seeds plus WinShare.ExtraSeeds16) and on the whole pool (plus 5v5 21-40); the 26 ORDER seeds print.
            Console.WriteLine("[b13 H] azure win share on the 26 ORDER seeds: on " + sOn.ToString("0.000") + ", control " + sOff.ToString("0.000") + " (printed; judged on the pools below)");
            var wOn = new Arm { Name = "hut on, wider" };
            var wOff = new Arm { Name = "control, wider" };
            for (int seed = 21; seed <= 40; seed++) { Play(wOff, seed, 5, false); Play(wOn, seed, 5, true); }
            foreach (int seed in WinShare.ExtraSeeds16) { Play(wOff, seed, 16, false); Play(wOn, seed, 16, true); }
            var t16On = new WinShare.Tally(); t16On.Add(on.T16); t16On.Add(wOn.T16);
            var t16Off = new WinShare.Tally(); t16Off.Add(off.T16); t16Off.Add(wOff.T16);
            var allOn = new WinShare.Tally(); allOn.Add(t16On); allOn.Add(on.T5); allOn.Add(wOn.T5);
            var allOff = new WinShare.Tally(); allOff.Add(t16Off); allOff.Add(off.T5); allOff.Add(wOff.T5);
            // b15 rules pass 3 (2026-11-20): the 16v16 win share, the lead changes and the snowball guard are judged on a pool
            // sized for their limits (limits unchanged). With the b15 comeback rule the matches are close and a close match flips
            // on any perturbation, so the same-seed arms decorrelate: the 24-seed 16v16 share's noise is up to 0.144 (it read
            // 0.292 at bf1e2c9c and 0.042 at 6ef45146 with no hut change between them). The sized pool adds the 48 16v16 seeds of
            // the 72-seed pool (PairedShift.Extra48); the 24-seed lines print.
            var xOn = new Arm { Name = "hut on, 72-seed pool" };
            var xOff = new Arm { Name = "control, 72-seed pool" };
            foreach (int seed in PairedShift.Extra48()) { Play(xOff, seed, 16, false); Play(xOn, seed, 16, true); }
            WinShare.Judge("b13 H hut (printed, 24 seeds)", "16v16 pool", t16On, t16Off, true);
            var x16On = new WinShare.Tally(); x16On.Add(t16On); x16On.Add(xOn.T16);
            var x16Off = new WinShare.Tally(); x16Off.Add(t16Off); x16Off.Add(xOff.T16);
            if (!WinShare.Judge("b13 H hut", "16v16 pool of " + x16On.Matches + " (72 seeds)", x16On, x16Off, true)) fails++;
            if (!WinShare.Judge("b13 H hut", "all " + allOn.Matches + " matches", allOn, allOff, false)) fails++;
            double koOn = Median(on.FirstKo), koOff = Median(off.FirstKo);
            Check("median first player knock-out (s)", koOn, koOff, koOn >= koOff * 0.9 - 1e-9 && koOn <= koOff * 1.2 + 1e-9, "-10% / +20% of control");
            double kOn = on.Kills / on.Minutes, kOff = off.Kills / off.Minutes;
            Check("kills per minute", kOn, kOff, kOn >= kOff * 0.8 - 1e-9 && kOn <= kOff * 1.15 + 1e-9, "-20% / +15% of control");
            for (int c = 0; c < 6; c++)
            {
                double a = on.Kills > 0 ? (double)on.ClassKills[c] / on.Kills : 0, b = off.Kills > 0 ? (double)off.ClassKills[c] / off.Kills : 0;
                Check((ClassId)c + " kill share", a, b, Math.Abs(a - b) <= 0.08 + 1e-9, "within 0.08 of control");
            }
            // b13 r3 integrator: lead changes are judged on the same 56-match pool as the win share (26 matches swung 49 vs 33
            // and 52 vs 30 once dodge, walls and corruption joined; the 26 ORDER seeds print)
            Console.WriteLine("[b13 H] hill lead changes on the 26 ORDER seeds: on " + on.LeadChanges + ", control " + off.LeadChanges + " (printed; judged on the whole pool)");
            int lcOn = on.LeadChanges + wOn.LeadChanges, lcOff = off.LeadChanges + wOff.LeadChanges;
            // b15 rules pass 3 (2026-11-20): the +-30% limit is unchanged, but it is judged on a pool sized for it. Once the b15
            // comeback rule made matches close, the same-seed totals of the 64-match pool swung past 30% between commits that did
            // not touch the hut (256 vs 192 at 6ef45146): a close match's lead changes flip on any perturbation. The judged pool
            // adds the 48 16v16 seeds of the 72-seed pool (PairedShift.Extra48): 112 matches, 72 of them 16v16; the old 64-match
            // totals print. The per-seed paired shift and its 95% interval print with it.
            Console.WriteLine("[b13 H] hill lead changes over the " + (on.Matches + wOn.Matches) + "-match pool: on " + lcOn + ", control " + lcOff + " (printed; judged on the sized pool below)");
            var pOn = new List<double>(on.MatchLeadChanges); pOn.AddRange(wOn.MatchLeadChanges); pOn.AddRange(xOn.MatchLeadChanges);
            var pOff = new List<double>(off.MatchLeadChanges); pOff.AddRange(wOff.MatchLeadChanges); pOff.AddRange(xOff.MatchLeadChanges);
            PairedShift.Result lcp = PairedShift.Of(pOn, pOff);
            double lcRel = lcp.MeanOff > 0 ? lcp.Mean / lcp.MeanOff : 0, lcRelHalf = lcp.MeanOff > 0 ? lcp.Half / lcp.MeanOff : 0;
            Console.WriteLine("[b13 H] hill lead changes per match, sized pool: " + PairedShift.Text(lcp) + " = " + (lcRel * 100).ToString("+0;-0") + "% of control (95% interval "
                + ((lcRel - lcRelHalf) * 100).ToString("+0;-0") + "% to " + ((lcRel + lcRelHalf) * 100).ToString("+0;-0") + "%)");
            double slOn = lcp.MeanOn * lcp.N, slOff = lcp.MeanOff * lcp.N;
            Check("hill lead changes over " + lcp.N + " matches (" + (on.Matches16 + wOn.Matches16 + xOn.Matches16) + " of them 16v16)", slOn, slOff, Math.Abs(slOn - slOff) <= Math.Max(3, 0.3 * slOff) + 1e-9, "+-30% of control");
            var lv = new List<double>();
            foreach (int l in on.EndLevel16) lv.Add(l);
            double med = Median(lv);
            Check("16v16 median end hut level", med, 0, med >= 3, "3 or more");
            // b13 r3 integrator: judged on the 16v16 pool (the 6 ORDER seeds print); b14-c: the pool is the helper's 24 seeds
            Console.WriteLine("[b13 H] 16v16 ORDER seeds with a team at L5: on " + on.L5Seeds16 + " of 6 (printed; judged on the 16v16 pool)");
            int l5 = on.L5Seeds16 + wOn.L5Seeds16, n16 = on.Matches16 + wOn.Matches16;
            Check("16v16 matches with a team at L5 (of " + n16 + ")", l5, off.L5Seeds16 + wOff.L5Seeds16, n16 > 0 && l5 * 3 >= n16, "one third or more");
            // b14-c: the snowball guard on the widened sample (the whole pool), not the 26 ORDER seeds alone
            // b15 pass 3: on the sized pool too, and judged from 12 matches with a level gap at 6:00 (under that one match moves
            // the share by more than 0.08; at bf1e2c9c it read 2 of 2 = 1.00 on the 64-match pool); fewer prints
            int diff6 = on.DiffAt6 + wOn.DiffAt6 + xOn.DiffAt6, won6 = on.HigherWonAt6 + wOn.HigherWonAt6 + xOn.HigherWonAt6;
            double snow = diff6 > 0 ? (double)won6 / diff6 : 0;
            if (diff6 >= 12) Check("higher-level team at 6:00 wins (share of " + diff6 + " matches of the sized pool)", snow, 0, snow <= 0.75 + 1e-9, "0.75 at most");
            else Console.WriteLine("[b13 H] higher-level team at 6:00 wins " + won6 + " of " + diff6 + " matches of the sized pool (printed: under 12 matches with a level gap, not judged)");
            Check("Miner barricades placed", on.MinerBarricades, off.MinerBarricades, on.MinerBarricades == 0, "0");
            Check("Miner cells dug or placed", on.MinerDigs, off.MinerDigs, on.MinerDigs == 0, "0");
            double cOn = on.SimSeconds / on.SimTicks, cOff = off.SimSeconds / off.SimTicks;
            Check("mean sim microseconds per tick", 1e6 * cOn, 1e6 * cOff, cOn <= cOff * 1.10, "+10% of control");
            Check("ore mined per Miner bot", on.MinerBots > 0 ? (double)on.OreMined / on.MinerBots : 0, 0, on.OreMined > 0, "more than 0");
            Assert.AreEqual(0, fails, "CM b13 [H] balance limits (see the LIMIT lines)");
        }
    }
}
