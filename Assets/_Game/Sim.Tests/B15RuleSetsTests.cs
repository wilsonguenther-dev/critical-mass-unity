using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b15 rules pass 3 (2026-11-20): the candidate DEFAULT SETS of the comeback rule, measured against the owner's targets at 24
    /// seeds (WinShare.Seeds16) and then 72 (B15SweepTests.Seeds72, whose first 24 are the same seeds, so the 24 line is the
    /// first third of the same matches). Hut choices on in every arm (they stay). Per arm one line per sample size: the targets
    /// (blowout, lead changes, leader at half, dead/walking in BOTH definitions, win-share shift against the both-off control),
    /// the Kaiju snowball check (who got the Kaiju at the hatch and whether that side won), and the dead/walking breakdown.
    /// <para>(A) every lever off; (B) HOLD FATIGUE + FORWARD RETURN + RALLY + respawn 2 -> 5 s, no TURNCOAT; (C) B + TRAILING FEED
    /// x1.5; (D) B + KAIJU HOLDS NOTHING (its team's hill seconds x0.5 / x0.25 while it stands on the hill); (C+D); (E) B +
    /// TURNCOAT, the set shipped at 6ef45146 (reference).</para>
    /// <para>THE BOT BRAIN: feat/b15-bot-brain (smarter bots) changes every number here. Re-measure once it lands with ONE
    /// command: tools/ci/b15-rulesets-probe.sh &lt;GameConfig field path of the brain switch, e.g. Bots.TeamBrain&gt; - it pushes a
    /// probe branch that runs only this test with Brain set to that path (set to true by reflection on every arm, the control
    /// included). An empty Brain = today's bots.</para>
    /// </summary>
    public sealed class B15RuleSetsTests
    {
        /// <summary>GameConfig field path of the bot-brain switch, set to true on every arm ("" = today's bots). The probe script
        /// rewrites this line; a path that does not exist on the branch fails the test loudly (never a silent today's-bots run).</summary>
        public const string Brain = "";

        static string F(double v, string f = "0.00") => v.ToString(f, CultureInfo.InvariantCulture);

        /// <summary>Sets the bool at a dotted GameConfig field path (e.g. "Bots.TeamBrain") to true.</summary>
        public static void SetSwitch(GameConfig c, string path)
        {
            object o = c;
            string[] parts = path.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                FieldInfo f = o.GetType().GetField(parts[i], BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNotNull(f, "bot-brain switch '" + path + "': no field '" + parts[i] + "' on " + o.GetType().Name);
                if (i == parts.Length - 1)
                {
                    Assert.AreEqual(typeof(bool), f.FieldType, "bot-brain switch '" + path + "' is not a bool");
                    f.SetValue(o, true);
                }
                else o = f.GetValue(o);
            }
        }

        public static readonly Action<ComebackConfig> SetB = k =>
        {
            B15SweepTests.Fatigue(20, 40, 250)(k);
            B15SweepTests.Forward(0.4)(k);
            B15SweepTests.FwLost(k);
            B15SweepTests.Rally(500, 60, 5)(k);
            B15SweepTests.Curve(2, 5)(k);
        };

        public static Action<ComebackConfig> TrailingFeed(int perMille) => k => k.FeedTrailingPerMille = perMille;
        public static Action<ComebackConfig> KaijuHill(int perMille) => k => k.KaijuHillPerMille = perMille;

        public static readonly Action<ComebackConfig> Anywhere = k => k.KaijuHillAnywhere = true;
        /// <summary>A GameConfig-level tweak (not a comeback lever): the Kaiju's HP per team member (400 today).</summary>
        public static Action<GameConfig> KaijuHp(double perMember) => c => c.Kaiju.HpPerTeamMember = perMember;

        static KeyValuePair<string, Action<GameConfig>> S(string name, params Action<ComebackConfig>[] levers) => S2(name, null, levers);
        static KeyValuePair<string, Action<GameConfig>> S2(string name, Action<GameConfig> extra, params Action<ComebackConfig>[] levers) =>
            new KeyValuePair<string, Action<GameConfig>>(name, c => { B15SweepTests.Arm(levers)(c); extra?.Invoke(c); });

        /// <summary>The arms, in report order (the both-off control is run separately).</summary>
        public static List<KeyValuePair<string, Action<GameConfig>>> Sets() => new List<KeyValuePair<string, Action<GameConfig>>>
        {
            S("A all levers off"),
            S("B fatigue+forward+rally+2-5 s", SetB),
            S("D0 DEFAULT (B + kaiju hill x0)", SetB, KaijuHill(0)),
            S("C B+trailing feed x1.5", SetB, TrailingFeed(500)),
            S("D B+kaiju hill x0.5", SetB, KaijuHill(500)),
            S("D' B+kaiju hill x0.25", SetB, KaijuHill(250)),
            S("C+D B+feed x1.5+kaiju hill x0.5", SetB, TrailingFeed(500), KaijuHill(500)),
            S("E B+turncoat (shipped 6ef45146)", SetB, B15SweepTests.Turncoat),
        };

        /// <summary>Round 2 (run 44834953494 showed the Kaiju side winning 62-67 of 67 sided matches without TURNCOAT, so the
        /// lead at the half does not decide; these weaken the Kaiju's hold on the result harder, and two push dead/walking).</summary>
        public static List<KeyValuePair<string, Action<GameConfig>>> SetsRound2() => new List<KeyValuePair<string, Action<GameConfig>>>
        {
            S("B fatigue+forward+rally+2-5 s", SetB),
            S("D0 B+kaiju hill x0", SetB, KaijuHill(0)),
            S("DA B+kaiju side x0.5 anywhere", SetB, KaijuHill(500), Anywhere),
            S("DA' B+kaiju side x0.75 anywhere", SetB, KaijuHill(750), Anywhere),
            S2("DH B+kaiju HP 250", KaijuHp(250), SetB),
            S2("DH' B+kaiju HP 250+kaiju hill x0.25", KaijuHp(250), SetB, KaijuHill(250)),
            S("D'4 B(2-4 s)+kaiju hill x0.25", SetB, KaijuHill(250), B15SweepTests.Curve(2, 4)),
            S("D'f B(forward 0.6)+kaiju hill x0.25", SetB, KaijuHill(250), B15SweepTests.Forward(0.6)),
            S2("DX B(2-4 s, forward 0.6)+kaiju HP 250+kaiju hill x0.25", KaijuHp(250), SetB, KaijuHill(250), B15SweepTests.Curve(2, 4), B15SweepTests.Forward(0.6)),
        };

        static string Line(B15RulesSoakTests.Arm a, B15RulesSoakTests.Arm off)
        {
            double blow = B15RulesSoakTests.BlowoutRate(a), lc = B15RulesSoakTests.Median(a.LeadChanges), lead = B15RulesSoakTests.LeaderWins(a);
            double down = B15RulesSoakTests.DownShare(a), probe = B15RulesSoakTests.ProbeDownShare(a);
            double shift = Math.Max(Math.Abs(a.T16.AzureShare - off.T16.AzureShare), Math.Abs(a.T16.CrimsonShare - off.T16.CrimsonShare));
            int p = 0;
            if (blow <= 0.15 + 1e-9) p++;
            if (lc >= 5 - 1e-9) p++;
            if (lead >= 0.6 - 1e-9 && lead <= 0.8 + 1e-9) p++;
            if (down <= 0.28 + 1e-9) p++;
            if (shift <= 0.15 + 1e-9) p++;
            double t = Math.Max(1, a.ActorTicks);
            return a.Matches + " seeds: blowout " + F(blow) + (blow <= 0.15 + 1e-9 ? " ok" : " MISS")
                + " | lead changes " + F(lc, "0.0") + (lc >= 5 - 1e-9 ? " ok" : " MISS")
                + " | leader at half " + F(lead) + (lead >= 0.6 - 1e-9 && lead <= 0.8 + 1e-9 ? " ok" : " MISS")
                + " | dead/walk (b15) " + F(down, "0.000") + (down <= 0.28 + 1e-9 ? " ok" : " MISS")
                + " | dead/walk (BalanceProbe) " + F(probe, "0.000")
                + " | win shift " + F(shift) + (shift <= 0.15 + 1e-9 ? " ok" : " MISS")
                + " | decided at " + F(B15RulesSoakTests.Median(a.Decided)) + " | " + p + "/5 targets"
                + " || breakdown: dead " + F(a.DeadTicks / t, "0.000") + ", walk-back " + F(a.WalkTicks / t, "0.000")
                + "; probe dead+getting-up " + F(a.ProbeDeadTicks / t, "0.000") + " (getting up " + F(a.StandUpTicks / t, "0.000") + "), probe walk " + F(a.ProbeWalkTicks / t, "0.000")
                + " (inside the walk-back window " + F(a.ProbeWalkInWindow / t, "0.000") + ", outside it " + F((a.ProbeWalkTicks - a.ProbeWalkInWindow) / t, "0.000") + ")"
                + " || kaiju: sided " + a.Sided + ", side won " + a.SideWon + "; at the hatch the hill trailer got it " + a.TrailerGotIt + " (won " + a.TrailerGotItWon
                + "), the leader got it " + a.LeaderGotIt + " (won " + a.LeaderGotItWon + ") of " + a.HatchWithLeader + "; turns " + a.Turns
                + " || kaiju-hill block: " + F(a.BlockedTicks / Math.Max(1, a.Ticks), "0.000") + " of match ticks (" + F(a.BlockedTicks / Math.Max(1, a.SidedUpTicks), "0.00") + " of sided-Kaiju-up ticks), owner bodies on the hill "
                + F(a.BlockedOwnBodies / Math.Max(1, a.BlockedTicks), "0.0") + " vs other team " + F(a.BlockedEnemyBodies / Math.Max(1, a.BlockedTicks), "0.0") + " while blocked"
                + " || kills/min " + F(a.Kills / Math.Max(1e-9, a.Minutes), "0.0") + ", KO wait " + F(a.RespawnSum / Math.Max(1, a.RespawnN)) + " s";
        }

        /// <summary>Runs every set at 24 seeds, prints, then continues the same arms to 72 seeds and prints again.</summary>
        public static void Measure(string brain) => Measure(brain, Sets(), "");

        public static void Measure(string brain, List<KeyValuePair<string, Action<GameConfig>>> sets, string round)
        {
            Action<GameConfig> withBrain = c => { if (!string.IsNullOrEmpty(brain)) SetSwitch(c, brain); };
            string tag = "[b15 sets" + round + (string.IsNullOrEmpty(brain) ? "" : " brain " + brain) + "] ";
            int[] seeds = B15SweepTests.Seeds72();
            var off = new B15RulesSoakTests.Arm { Name = "control both off", Comeback = false, Choices = false, Tweak = withBrain };
            var arms = new List<B15RulesSoakTests.Arm>();
            foreach (var kv in sets)
            {
                Action<GameConfig> set = kv.Value;
                arms.Add(new B15RulesSoakTests.Arm
                {
                    Name = kv.Key, Comeback = true, Choices = true,
                    Tweak = c => { withBrain(c); set(c); },
                });
            }
            for (int stage = 0; stage < 2; stage++)
            {
                int from = stage == 0 ? 0 : 24, to = stage == 0 ? 24 : seeds.Length;
                for (int i = from; i < to; i++) B15RulesSoakTests.Play(off, seeds[i]);
                foreach (var a in arms) for (int i = from; i < to; i++) B15RulesSoakTests.Play(a, seeds[i]);
                Console.WriteLine(tag + "control both off | " + Line(off, off));
                foreach (var a in arms) Console.WriteLine(tag + a.Name + " | " + Line(a, off));
            }
        }

        [Test, Explicit, Timeout(3500000)]
        public void B15RuleSetsAgainstTheTargetsAt24Then72Seeds() => Measure(Brain);

        [Test, Explicit, Timeout(3500000)]
        public void B15RuleSetsRoundTwo() => Measure(Brain, SetsRound2(), " r2");

        /// <summary>Round 3 (run 44836594576: D0 met 4 of 5 at 24 and 72 seeds, dead/walking 0.285 the one miss; a 2-4 s respawn
        /// alone reached 0.279): D0 with shorter respawn curves.</summary>
        public static List<KeyValuePair<string, Action<GameConfig>>> SetsRound3() => new List<KeyValuePair<string, Action<GameConfig>>>
        {
            S("D0 B+kaiju hill x0", SetB, KaijuHill(0)),
            S("D0 2-4 s", SetB, KaijuHill(0), B15SweepTests.Curve(2, 4)),
            S("D0 2-4.5 s", SetB, KaijuHill(0), B15SweepTests.Curve(2, 4.5)),
            S("D0 1.5-5 s", SetB, KaijuHill(0), B15SweepTests.Curve(1.5, 5)),
            S("D0 1.5-4.5 s", SetB, KaijuHill(0), B15SweepTests.Curve(1.5, 4.5)),
        };

        [Test, Explicit, Timeout(3500000)]
        public void B15RuleSetsRoundThree() => Measure(Brain, SetsRound3(), " r3");
    }
}
