using System;
using System.Collections.Generic;
using System.Globalization;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b15 pass 2: the automated lever sweep on the 24-seed 16v16 helper (explicit, run by name). Each arm runs with the hut
    /// choices on and the comeback rule's levers set by the arm; one line per arm with every target metric, the PASS count, and
    /// a score (targets met first, then distance to the unmet ones) so the best set reads off the top.
    /// </summary>
    public sealed class B15SweepTests
    {
        static string F(double v, string f = "0.00") => v.ToString(f, CultureInfo.InvariantCulture);

        /// <summary>Every lever off (the hut choices stay on): the base each sweep arm adds levers to.</summary>
        public static void Bare(GameConfig c)
        {
            ComebackConfig k = c.Depth.Comeback;
            k.RallyOn = false;
            k.RespawnCurveOn = false;
            k.WaveSeconds = 0;
            k.KaijuTurncoat = false;
            k.HoldFatigueOn = false;
            k.MajorityMargin = 1;
            k.ForwardReturnOn = false;
        }

        public static Action<GameConfig> Arm(params Action<ComebackConfig>[] levers) => c =>
        {
            Bare(c);
            foreach (var l in levers) l(c.Depth.Comeback);
        };

        public static readonly Action<ComebackConfig> Turncoat = k => k.KaijuTurncoat = true;
        public static Action<ComebackConfig> Waves(double s) => k => k.WaveSeconds = s;
        public static Action<ComebackConfig> Fatigue(double grace, double span, int min) => k => { k.HoldFatigueOn = true; k.HoldGraceSeconds = grace; k.HoldSpanSeconds = span; k.HoldMinPerMille = min; };
        public static Action<ComebackConfig> Majority(int m) => k => k.MajorityMargin = m;
        public static Action<ComebackConfig> Forward(double share) => k => { k.ForwardReturnOn = true; k.ForwardShare = share; };
        public static Action<ComebackConfig> Curve(double a, double b, int p = 1) => k => { k.RespawnCurveOn = true; k.RespawnStartSeconds = a; k.RespawnEndSeconds = b; k.CurvePower = p; };
        public static Action<ComebackConfig> Rally(int perMille, double cap, double threshold) => k => { k.RallyOn = true; k.RallyPerMille = perMille; k.BonusCapSeconds = cap; k.TrailThresholdSeconds = threshold; };

        public static int Passes(B15RulesSoakTests.Arm a, B15RulesSoakTests.Arm off, out double score, out string line)
        {
            double blow = B15RulesSoakTests.BlowoutRate(a), lc = B15RulesSoakTests.Median(a.LeadChanges), dec = B15RulesSoakTests.Median(a.Decided);
            double down = B15RulesSoakTests.DownShare(a), lead = B15RulesSoakTests.LeaderWins(a);
            double shift = Math.Abs(a.T16.AzureShare - off.T16.AzureShare);
            int p = 0;
            score = 0;
            if (blow <= 0.15 + 1e-9) p++; else score += (blow - 0.15) * 4;
            if (lc >= 5 - 1e-9) p++; else score += (5 - lc) * 0.25;
            if (down <= 0.28 + 1e-9) p++; else score += (down - 0.28) * 10;
            if (lead >= 0.6 - 1e-9 && lead <= 0.8 + 1e-9) p++; else score += Math.Min(Math.Abs(lead - 0.6), Math.Abs(lead - 0.8)) * 4;
            if (shift <= 0.15 + 1e-9) p++; else score += (shift - 0.15) * 2;
            if (dec >= B15RulesSoakTests.Median(off.Decided) - 1e-9) p++; else score += 0.5;
            score = (6 - p) * 10 + score;
            line = "blowout " + F(blow) + ", lead changes " + F(lc, "0.0") + ", decided at " + F(dec) + ", dead/walk " + F(B15RulesSoakTests.DeadShare(a), "0.000") + "/" + F(down, "0.000")
                + ", leader at half " + F(lead) + ", win shift " + F(shift) + ", length " + F(B15RulesSoakTests.Median(a.Length), "0") + " s, kills/min " + F(a.Kills / Math.Max(1e-9, a.Minutes), "0.0")
                + ", KO wait " + F(a.RespawnSum / Math.Max(1, a.RespawnN)) + " s, turns " + a.Turns + " -> " + p + "/6 targets";
            return p;
        }

        public static void Sweep(string title, List<KeyValuePair<string, Action<GameConfig>>> arms, int[] seeds = null)
        {
            B15RulesSoakTests.Arm off = B15RulesSoakTests.Run("both off", false, false, null, seeds);
            var rows = new List<KeyValuePair<double, string>>();
            Passes(off, off, out double s0, out string l0);
            Console.WriteLine("[b15 sweep] " + title + " | both off: " + l0);
            foreach (var kv in arms)
            {
                B15RulesSoakTests.Arm a = B15RulesSoakTests.Run(kv.Key, true, true, kv.Value, seeds);
                Passes(a, off, out double score, out string line);
                Console.WriteLine("[b15 sweep] " + title + " | " + kv.Key + ": " + line);
                rows.Add(new KeyValuePair<double, string>(score, kv.Key));
            }
            rows.Sort((x, y) => x.Key.CompareTo(y.Key));
            for (int i = 0; i < Math.Min(6, rows.Count); i++) Console.WriteLine("[b15 sweep] " + title + " RANK " + (i + 1) + ": " + rows[i].Value + " (score " + F(rows[i].Key) + ")");
        }

        static KeyValuePair<string, Action<GameConfig>> A(string n, params Action<ComebackConfig>[] l) => new KeyValuePair<string, Action<GameConfig>>(n, Arm(l));

        [Test, Explicit, Timeout(3500000)]
        public void B15SweepRoundOne()
        {
            Sweep("r1", new List<KeyValuePair<string, Action<GameConfig>>>
            {
                A("hut only"),
                A("pass-1 rally+curve", Rally(250, 45, 15), Curve(3, 8)),
                A("turncoat", Turncoat),
                A("waves 6", Waves(6)),
                A("waves 10", Waves(10)),
                A("fatigue 30/60/0.5", Fatigue(30, 60, 500)),
                A("fatigue 20/40/0.25", Fatigue(20, 40, 250)),
                A("majority 2", Majority(2)),
                A("majority 3", Majority(3)),
                A("forward 0.4", Forward(0.4)),
                A("forward 0.6", Forward(0.6)),
                A("curve 2-6", Curve(2, 6)),
                A("turncoat+fatigue 30/60/0.5", Turncoat, Fatigue(30, 60, 500)),
                A("turncoat+waves 8", Turncoat, Waves(8)),
                A("turncoat+fatigue+waves 8", Turncoat, Fatigue(30, 60, 500), Waves(8)),
                A("turncoat+fatigue+waves 8+forward 0.4", Turncoat, Fatigue(30, 60, 500), Waves(8), Forward(0.4)),
                A("turncoat+fatigue+majority 2", Turncoat, Fatigue(30, 60, 500), Majority(2)),
                A("fatigue+waves 8+forward 0.4", Fatigue(30, 60, 500), Waves(8), Forward(0.4)),
                A("all + rally", Turncoat, Fatigue(30, 60, 500), Waves(8), Forward(0.4), Majority(2), Rally(250, 45, 15)),
                A("turncoat+fatigue 20/40/0.25+waves 8+forward 0.5", Turncoat, Fatigue(20, 40, 250), Waves(8), Forward(0.5)),
            });
        }
    
        [Test, Explicit, Timeout(3500000)]
        public void B15SweepRoundTwo()
        {
            var F2 = Fatigue(20, 40, 250);
            Sweep("r2", new List<KeyValuePair<string, Action<GameConfig>>>
            {
                A("T+F20/40/.25", Turncoat, F2),
                A("T+F15/30/.25", Turncoat, Fatigue(15, 30, 250)),
                A("T+F20/40/.25+C2-6", Turncoat, F2, Curve(2, 6)),
                A("T+F20/40/.25+C2-6+Fw.4", Turncoat, F2, Curve(2, 6), Forward(0.4)),
                A("T+F20/40/.25+C2-6+Fw.6", Turncoat, F2, Curve(2, 6), Forward(0.6)),
                A("T+F20/40/.25+C2-6+Fw.4+R500/60/5", Turncoat, F2, Curve(2, 6), Forward(0.4), Rally(500, 60, 5)),
                A("T+F30/60/.5+C2-6+Fw.4", Turncoat, Fatigue(30, 60, 500), Curve(2, 6), Forward(0.4)),
                A("T+F20/40/.25+C2-5", Turncoat, F2, Curve(2, 5)),
                A("T+F20/40/.1+C2-6+Fw.4", Turncoat, Fatigue(20, 40, 100), Curve(2, 6), Forward(0.4)),
                A("T+F10/30/.25+C2-6+Fw.4", Turncoat, Fatigue(10, 30, 250), Curve(2, 6), Forward(0.4)),
                A("F20/40/.25+C2-6+Fw.4", F2, Curve(2, 6), Forward(0.4)),
                A("T+F20/40/.25+C2-6+Fw.4+M2", Turncoat, F2, Curve(2, 6), Forward(0.4), Majority(2)),
                A("T+F20/40/.25+C2-6+Fw.4+R250/45/15", Turncoat, F2, Curve(2, 6), Forward(0.4), Rally(250, 45, 15)),
                A("T+F15/45/0+C2-6+Fw.4", Turncoat, Fatigue(15, 45, 0), Curve(2, 6), Forward(0.4)),
                A("T+F10/20/0+C2-6+Fw.4", Turncoat, Fatigue(10, 20, 0), Curve(2, 6), Forward(0.4)),
                A("T+F20/40/.25+C2-6+Fw.4+R1000/90/5", Turncoat, F2, Curve(2, 6), Forward(0.4), Rally(1000, 90, 5)),
            });
        }
    }
}
