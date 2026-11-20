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

        /// <summary>The arms, in report order (the both-off control is run separately).</summary>
        public static List<KeyValuePair<string, Action<ComebackConfig>[]>> Sets() => new List<KeyValuePair<string, Action<ComebackConfig>[]>>
        {
            new KeyValuePair<string, Action<ComebackConfig>[]>("A all levers off", new Action<ComebackConfig>[0]),
            new KeyValuePair<string, Action<ComebackConfig>[]>("B fatigue+forward+rally+2-5 s", new[] { SetB }),
            new KeyValuePair<string, Action<ComebackConfig>[]>("C B+trailing feed x1.5", new[] { SetB, TrailingFeed(500) }),
            new KeyValuePair<string, Action<ComebackConfig>[]>("D B+kaiju hill x0.5", new[] { SetB, KaijuHill(500) }),
            new KeyValuePair<string, Action<ComebackConfig>[]>("D' B+kaiju hill x0.25", new[] { SetB, KaijuHill(250) }),
            new KeyValuePair<string, Action<ComebackConfig>[]>("C+D B+feed x1.5+kaiju hill x0.5", new[] { SetB, TrailingFeed(500), KaijuHill(500) }),
            new KeyValuePair<string, Action<ComebackConfig>[]>("E B+turncoat (shipped 6ef45146)", new[] { SetB, B15SweepTests.Turncoat }),
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
                + " || kills/min " + F(a.Kills / Math.Max(1e-9, a.Minutes), "0.0") + ", KO wait " + F(a.RespawnSum / Math.Max(1, a.RespawnN)) + " s";
        }

        /// <summary>Runs every set at 24 seeds, prints, then continues the same arms to 72 seeds and prints again.</summary>
        public static void Measure(string brain)
        {
            Action<GameConfig> withBrain = c => { if (!string.IsNullOrEmpty(brain)) SetSwitch(c, brain); };
            string tag = "[b15 sets" + (string.IsNullOrEmpty(brain) ? "" : " brain " + brain) + "] ";
            int[] seeds = B15SweepTests.Seeds72();
            var off = new B15RulesSoakTests.Arm { Name = "control both off", Comeback = false, Choices = false, Tweak = withBrain };
            var arms = new List<B15RulesSoakTests.Arm>();
            foreach (var kv in Sets())
            {
                Action<ComebackConfig>[] levers = kv.Value;
                arms.Add(new B15RulesSoakTests.Arm
                {
                    Name = kv.Key, Comeback = true, Choices = true,
                    Tweak = c => { withBrain(c); B15SweepTests.Arm(levers)(c); },
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
    }
}
