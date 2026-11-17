// b13 r2 P2 (owner decision: terrain play belongs to the Builder and the Demolitionist, never the Miner). The dig / place
// terraform moves from the Miner to the BUILDER while the hut rule is on (HutConfig.BuilderTerraform); Classic and PhysicsOnly
// keep it on the Miner. Every case prints one line (what, value, limit).
using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    public sealed class BuilderTerraformTests
    {
        static void Line(string what, object value, string limit) => Console.WriteLine("[b13 r2 P2] " + what + ": " + value + " (limit " + limit + ")");

        /// <summary>
        /// Terraform events of actor 0 (class cls) for one F press on a test field that got past the class check: Dug, or a
        /// ground refusal (hill keep-out, route guard, rate cap). A press the class does not own makes no event at all.
        /// </summary>
        static int DigsFor(RulesProfile p, ClassId cls, bool builderTerraform, out ClassId owner, out string values)
        {
            GameConfig c = GameConfig.CreateBrowser5v5().ApplyRules(p);
            c.Depth.Hut.BuilderTerraform = builderTerraform;
            World w = DepthKit.Field(c, cls);
            owner = Terraform.OwnerClass(w);
            int head = w.EventHead;
            w.Inputs[0].Terraform = true;
            DepthKit.Steps(w, 2);
            int n = 0;
            values = "";
            foreach (SimEvent e in DepthKit.Since(w, head, SimEventType.Terraform))
            {
                if (e.A != 0) continue;
                values += (values.Length > 0 ? "," : "") + e.Value;
                if (e.Value != Terraform.RefusedTarget) n++;
            }
            return n;
        }

        [Test]
        public void TheBuilderDigsWithFWhileTheHutRuleIsOnAndTheMinerNeverDoes()
        {
            int bad = 0;
            void Case(RulesProfile p, ClassId cls, bool flag, int want)
            {
                int got = DigsFor(p, cls, flag, out ClassId owner, out string values);
                Line(p + " " + cls + " F press (BuilderTerraform " + flag + ", owner " + owner + "): presses that reached dig (event values " + (values.Length > 0 ? values : "none") + ")", got, want.ToString());
                if (got != want) bad++;
            }
            Case(RulesProfile.Full, ClassId.Builder, true, 1);
            Case(RulesProfile.Full, ClassId.Miner, true, 0);
            Case(RulesProfile.Full, ClassId.Warrior, true, 0);
            Case(RulesProfile.Full, ClassId.Builder, false, 0);
            Case(RulesProfile.Full, ClassId.Miner, false, 0);
            Case(RulesProfile.Classic, ClassId.Miner, true, 1);
            Case(RulesProfile.Classic, ClassId.Builder, true, 0);
            Case(RulesProfile.PhysicsOnly, ClassId.Miner, true, 1);
            Case(RulesProfile.PhysicsOnly, ClassId.Builder, true, 0);
            Assert.AreEqual(0, bad, "dig / place owner per profile (lines above)");
        }

        sealed class Arm
        {
            public string Name;
            public int Matches, AzureWins, Kills, LeadChanges, RouteFailures, RouteChecks, Digs;
            public readonly WinShare.Tally T16 = new WinShare.Tally(), T5 = new WinShare.Tally();
            public double Minutes;
            public readonly int[] ClassKills = new int[6];
            public readonly List<double> FirstKo = new List<double>();
            public readonly List<string> End = new List<string>();
        }

        static void Play(Arm arm, int seed, int teamSize, bool on)
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full);
            cfg.Depth.Hut.BuilderTerraform = on;
            World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = teamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            InputFrame scratch = SimInput.EmptyInput();
            int hz = (int)Math.Round(cfg.SimHz);
            int max = (int)Math.Ceiling(cfg.MatchSeconds * cfg.SimHz) + hz;
            int cursor = w.EventHead, lastSign = 0;
            double firstKo = double.NaN;
            for (int t = 0; t < max && w.Match.Phase != MatchPhase.Ended; t++)
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
                    else if (e.Type == SimEventType.Terraform && (e.Value == Terraform.Dug || e.Value == Terraform.Placed)) arm.Digs++;
                }
                if (w.Tick % 30 == 0)
                {
                    arm.RouteChecks++;
                    if (!Terraform.PadsReachHill(w.Map)) arm.RouteFailures++;
                }
                if (w.Tick % hz != 0) continue;
                double lead = w.Match.HillSeconds.Azure - w.Match.HillSeconds.Crimson;
                int sign = lead > 0.5 ? 1 : lead < -0.5 ? -1 : 0;
                if (sign != 0 && lastSign != 0 && sign != lastSign) arm.LeadChanges++;
                if (sign != 0) lastSign = sign;
            }
            arm.Matches++;
            arm.Minutes += w.Match.Time / 60.0;
            if (w.Match.Winner == MatchResult.Azure) arm.AzureWins++;
            (teamSize == 16 ? arm.T16 : arm.T5).Add(w.Match.Winner);
            if (!double.IsNaN(firstKo)) arm.FirstKo.Add(firstKo);
            double sx = 0;
            for (int i = 0; i < w.ActorCount; i++) sx += w.X[i] * (i + 1) + w.Z[i] * (i + 7);
            arm.End.Add(w.Tick + "/" + w.Match.Winner + "/" + sx.ToString("R"));
        }

        static double Median(List<double> v)
        {
            if (v.Count == 0) return double.NaN;
            var s = new List<double>(v);
            s.Sort();
            return s.Count % 2 == 1 ? s[s.Count / 2] : 0.5 * (s[s.Count / 2 - 1] + s[s.Count / 2]);
        }

        static Arm Run(string name, bool on)
        {
            var arm = new Arm { Name = name };
            for (int seed = 1; seed <= 20; seed++) Play(arm, seed, 5, on);
            foreach (int seed in WinShare.Seeds16) Play(arm, seed, 16, on);   // b14-c item 5: 24 16v16 seeds (ORDER six first)
            string cls = "";
            for (int c = 0; c < 6; c++) cls += (ClassId)c + " " + (arm.ClassKills[c] / Math.Max(1.0, arm.Kills)).ToString("0.000") + " ";
            Console.WriteLine("[b13 r2 P2] ARM " + name + ": matches " + arm.Matches + ", azure win share " + (arm.AzureWins / (double)arm.Matches).ToString("0.000")
                + ", median first knock-out " + Median(arm.FirstKo).ToString("0.0") + " s, kills per minute " + (arm.Kills / Math.Max(1e-9, arm.Minutes)).ToString("0.00")
                + ", class kill shares " + cls + ", hill lead changes " + arm.LeadChanges + ", cells dug or placed " + arm.Digs
                + ", route failures " + arm.RouteFailures + " of " + arm.RouteChecks + " checks");
            return arm;
        }

        [Test, Category("Slow")]
        public void BalanceAgainstTheSameSeedControlStaysInsideTheLimits()
        {
            Arm on = Run("builder terrain on", true), off = Run("control (BuilderTerraform false)", false);
            int fails = 0;
            void Lim(string what, double a, double b, bool ok, string limit)
            {
                Console.WriteLine("[b13 r2 P2] LIMIT " + what + ": on " + a.ToString("0.000") + ", control " + b.ToString("0.000") + " (limit " + limit + ") " + (ok ? "PASS" : "FAIL"));
                if (!ok) fails++;
            }
            // b14-c item 5: judged by the one helper (WinShare) on the 24 16v16 seeds and on all matches
            var allOn = new WinShare.Tally(); allOn.Add(on.T16); allOn.Add(on.T5);
            var allOff = new WinShare.Tally(); allOff.Add(off.T16); allOff.Add(off.T5);
            if (!WinShare.Judge("b13 r2 P2 builder terrain", "16v16 (" + on.T16.Matches + " seeds)", on.T16, off.T16, true)) fails++;
            if (!WinShare.Judge("b13 r2 P2 builder terrain", "all " + allOn.Matches + " matches", allOn, allOff, false)) fails++;
            double ka = Median(on.FirstKo), kb = Median(off.FirstKo);
            Lim("median first player knock-out (s)", ka, kb, ka >= kb * 0.9 - 1e-9 && ka <= kb * 1.2 + 1e-9, "-10% / +20% of control");
            double ma = on.Kills / on.Minutes, mb = off.Kills / off.Minutes;
            Lim("kills per minute", ma, mb, ma >= mb * 0.8 - 1e-9 && ma <= mb * 1.15 + 1e-9, "-20% / +15% of control");
            for (int c = 0; c < 6; c++)
            {
                double sa = on.ClassKills[c] / Math.Max(1.0, on.Kills), sb = off.ClassKills[c] / Math.Max(1.0, off.Kills);
                Lim((ClassId)c + " kill share", sa, sb, Math.Abs(sa - sb) <= 0.08, "within 0.08 of control");
            }
            Lim("route failures (both pads reach the hill, every 30th tick)", on.RouteFailures, off.RouteFailures, on.RouteFailures == 0, "0");
            int same = 0;
            for (int i = 0; i < on.End.Count; i++) if (on.End[i] == off.End[i]) same++;
            Line("matches whose end state equals the control (bots never dig: BotBrain clears F)", same + " of " + on.End.Count, "information");
            Assert.AreEqual(0, fails, "CM b13 r2 P2 balance limits (see the LIMIT lines)");
        }
    }
}
