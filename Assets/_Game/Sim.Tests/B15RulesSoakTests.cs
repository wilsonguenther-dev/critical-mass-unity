using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b15 balance proof: the 24-seed 16v16 helper (WinShare.Seeds16) with each switch on against both off, and both on. Per
    /// arm it prints, one plain line each: blowout rate (winner held 2x the loser), hill lead changes (median), when the result
    /// was decided as a share of the match (the last second the final winner was not ahead), share of fighter time dead and
    /// dead-or-walking-back, the win share (WinShare.Judge), the leader at 4:00's win rate, kills per minute and class kill
    /// shares, the knock-out wait, RALLY use, the hut picks and the win rate of each option, and the votes (opened, closed,
    /// empty, longest). Owner targets (both on): blowouts 15% or less, lead changes 5 or more, dead/walking 28% or less, win
    /// share shift 0.15 or less, leader-at-half win rate 0.60 to 0.80, no option over 0.65 win rate, no vote over its window.
    /// </summary>
    public sealed class B15RulesSoakTests
    {
        public sealed class Arm
        {
            public string Name;
            public bool Comeback, Choices;
            public Action<GameConfig> Tweak;
            public readonly WinShare.Tally T16 = new WinShare.Tally();
            public readonly List<double> LeadChanges = new List<double>(), Decided = new List<double>(), Length = new List<double>();
            public int Turns;
            public int Matches, Blowouts, LeaderAtHalf, LeaderAtHalfWon, Kills, RespawnN, RallyMatches, RallyWon;
            public double Minutes, DeadTicks, WalkTicks, ActorTicks, RespawnSum, RallySeconds, SimSeconds, RallyMax, RespawnMin = double.MaxValue, RespawnMax;
            public long Ticks;
            public readonly int[] ClassKills = new int[6];
            public readonly int[] Picks = new int[11], PickWins = new int[11];
            public int Opened, Closed, Empty, Longest;
        }

        static string F(double v, string f = "0.000") => v.ToString(f, CultureInfo.InvariantCulture);

        public static double Median(List<double> v)
        {
            if (v.Count == 0) return double.NaN;
            var s = new List<double>(v);
            s.Sort();
            return s.Count % 2 == 1 ? s[s.Count / 2] : 0.5 * (s[s.Count / 2 - 1] + s[s.Count / 2]);
        }

        /// <summary>Walking back ends at the hill (radius + this), at a hit dealt or taken, at a job (gathering or mining), or after WalkCap s.</summary>
        public const double WalkHillMargin = 6, WalkCap = 20;

        public static void Play(Arm arm, int seed, int teamSize = 16)
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full);
            cfg.Depth.Comeback.Enabled = arm.Comeback;
            cfg.Depth.HutChoices.Enabled = arm.Choices;
            arm.Tweak?.Invoke(cfg);
            World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = teamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            InputFrame scratch = SimInput.EmptyInput();
            int hz = (int)Math.Round(cfg.SimHz);
            int max = (int)Math.Ceiling(cfg.MatchSeconds * cfg.SimHz) + hz;
            int n = w.ActorCount;
            bool[] walking = new bool[n];
            int[] walkFrom = new int[n];
            int seconds = (int)Math.Ceiling(cfg.MatchSeconds) + 2;
            int[] sign = new int[seconds];
            int lastSign = 0, changes = 0, halfSign = 0, kills = 0, lastSec = 0;
            double hr = cfg.Hill.Radius + WalkHillMargin, hr2 = hr * hr, dt = SimStep.Dt(cfg);
            int cursor = w.EventHead;
            long t0 = Stopwatch.GetTimestamp();
            int t = 0;
            for (; t < max && w.Match.Phase != MatchPhase.Ended; t++)
            {
                MatchTick.Advance(w, null, scratch);
                for (; cursor < w.EventHead; cursor++)
                {
                    SimEvent e = w.Events[cursor % World.EventCapacity];
                    if (e.Type == SimEventType.Death && e.A >= 0 && e.A < n)
                    {
                        kills++;
                        if (e.B >= 0 && e.B < n) arm.ClassKills[(int)w.Actors[e.B].Cls]++;
                        walking[e.A] = false;
                        arm.RespawnSum += w.Actors[e.A].RespawnIn + dt;   // the respawn system already took this tick's dt
                        arm.RespawnN++;
                        double wait = w.Actors[e.A].RespawnIn + dt;
                        if (wait < 1e8) { arm.RespawnMin = Math.Min(arm.RespawnMin, wait); arm.RespawnMax = Math.Max(arm.RespawnMax, wait); }
                    }
                    else if (e.Type == SimEventType.Respawn && e.A >= 0 && e.A < n)
                    {
                        walking[e.A] = true;
                        walkFrom[e.A] = w.Tick;
                    }
                    else if (e.Type == SimEventType.Hit)
                    {
                        if (e.A >= 0 && e.A < n) walking[e.A] = false;
                        if (e.B >= 0 && e.B < n) walking[e.B] = false;
                    }
                }
                for (int i = 0; i < n; i++)
                {
                    Actor a = w.Actors[i];
                    if (!a.Alive) { arm.DeadTicks++; continue; }
                    if (!walking[i]) continue;
                    double dx = w.X[i] - w.Map.Hill.X, dz = w.Z[i] - w.Map.Hill.Z;
                    if (dx * dx + dz * dz < hr2 || a.GatherProgress > 0 || Blacksmith.ChannelVein(w, i) >= 0 || w.Tick - walkFrom[i] > WalkCap * hz)
                    {
                        walking[i] = false;
                        continue;
                    }
                    arm.WalkTicks++;
                }
                arm.ActorTicks += n;
                if (w.Tick % hz != 0) continue;
                double lead = w.Match.HillSeconds.Azure - w.Match.HillSeconds.Crimson;
                int sg = lead > 0.5 ? 1 : lead < -0.5 ? -1 : 0;
                if (sg != 0 && lastSign != 0 && sg != lastSign) changes++;
                if (sg != 0) lastSign = sg;
                int sec = (int)Math.Round(w.Match.Time);
                if (sec >= 0 && sec < seconds) { sign[sec] = sg; lastSec = Math.Max(lastSec, sec); }
                if (sec == (int)Math.Round(cfg.MatchSeconds * 0.5)) halfSign = sg;
            }
            arm.SimSeconds += (Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency;
            arm.Ticks += t;
            arm.Matches++;
            arm.Kills += kills;
            arm.Minutes += w.Match.Time / 60.0;
            arm.LeadChanges.Add(changes);
            arm.Length.Add(w.Match.Time);
            arm.Turns += w.Rally.Turns;
            MatchResult win = w.Match.Winner;
            arm.T16.Add(win);
            double ha = w.Match.HillSeconds.Azure, hc = w.Match.HillSeconds.Crimson;
            int winSign = win == MatchResult.Azure ? 1 : win == MatchResult.Crimson ? -1 : 0;
            double decided = double.NaN;
            if (winSign != 0)
            {
                int last = 0;
                for (int s = 1; s <= lastSec; s++) if (sign[s] != winSign) last = s;
                decided = Math.Min(1.0, (last + 1) / cfg.MatchSeconds);
                arm.Decided.Add(decided);
                if (Math.Max(ha, hc) >= 2 * Math.Min(ha, hc)) arm.Blowouts++;
                if (halfSign != 0)
                {
                    arm.LeaderAtHalf++;
                    if (halfSign == winSign) arm.LeaderAtHalfWon++;
                }
            }
            string picks = "";
            if (HutChoices.On(w))
            {
                for (int team = 0; team < 2; team++)
                {
                    bool won = (team == 0 && win == MatchResult.Azure) || (team == 1 && win == MatchResult.Crimson);
                    picks += (team == 0 ? " picks azure" : " crimson");
                    for (int l = 1; l <= HutChoiceState.Levels; l++)
                    {
                        HutOption o = HutChoices.Chosen(w, (TeamId)team, l);
                        if (o == HutOption.None) continue;
                        arm.Picks[(int)o]++;
                        if (won) arm.PickWins[(int)o]++;
                        picks += " " + (int)o;
                    }
                }
                arm.Opened += w.HutVote.Opened;
                arm.Closed += w.HutVote.Closed;
                arm.Empty += w.HutVote.Empty;
                arm.Longest = Math.Max(arm.Longest, w.HutVote.LongestTicks);
            }
            double ra = Comeback.BonusSeconds(w, TeamId.Azure), rc = Comeback.BonusSeconds(w, TeamId.Crimson);
            arm.RallyMax = Math.Max(arm.RallyMax, Math.Max(ra, rc));
            if (ra + rc > 0)
            {
                arm.RallyMatches++;
                arm.RallySeconds += ra + rc;
                if ((ra >= rc && win == MatchResult.Azure) || (rc > ra && win == MatchResult.Crimson)) arm.RallyWon++;
            }
            Console.WriteLine("[b15 soak] " + arm.Name + " 16v16 seed " + seed + ": winner " + win + ", hill " + F(ha, "0.0") + " / " + F(hc, "0.0") + ", lead changes " + changes
                + ", decided at " + (double.IsNaN(decided) ? "draw" : F(decided, "0.00")) + ", leader at 4:00 " + halfSign + ", rally " + F(ra, "0.0") + " / " + F(rc, "0.0")
                + ", knock-outs " + kills + ", hut " + Blacksmith.Level(w, TeamId.Azure) + "/" + Blacksmith.Level(w, TeamId.Crimson) + picks);
        }

        public static Arm Run(string name, bool comeback, bool choices, Action<GameConfig> tweak = null, int[] seeds = null)
        {
            var arm = new Arm { Name = name, Comeback = comeback, Choices = choices, Tweak = tweak };
            foreach (int seed in seeds ?? WinShare.Seeds16) Play(arm, seed);
            return arm;
        }

        /// <summary>Bots vote a coin flip each (BotContrarianShare 0.5), so a team's pick does not follow its state: the option win
        /// rates of this arm measure the option, not the bots' choice (with need-voting the leading team picks HILL ANCHOR, which
        /// then "wins" 90% of the time because the team was already winning).</summary>
        public static readonly Action<GameConfig> CoinPicks = c => c.Depth.HutChoices.BotContrarianShare = 0.5;

        public static double BlowoutRate(Arm a) => a.Decided.Count > 0 ? (double)a.Blowouts / a.Decided.Count : 0;
        public static double DeadShare(Arm a) => a.ActorTicks > 0 ? a.DeadTicks / a.ActorTicks : 0;
        public static double DownShare(Arm a) => a.ActorTicks > 0 ? (a.DeadTicks + a.WalkTicks) / a.ActorTicks : 0;
        public static double LeaderWins(Arm a) => a.LeaderAtHalf > 0 ? (double)a.LeaderAtHalfWon / a.LeaderAtHalf : 0;

        public static void Print(Arm a)
        {
            string cls = "";
            int ck = 0;
            for (int c = 0; c < 6; c++) ck += a.ClassKills[c];
            for (int c = 0; c < 6; c++) cls += (c > 0 ? " " : "") + ((ClassId)c) + " " + F(ck > 0 ? (double)a.ClassKills[c] / ck : 0);
            Console.WriteLine("[b15 soak] " + a.Name + ": " + a.Matches + " matches; blowouts (2:1) " + a.Blowouts + " of " + a.Decided.Count + " = " + F(BlowoutRate(a))
                + "; lead changes median " + F(Median(a.LeadChanges), "0.0") + " (mean " + F(Sum(a.LeadChanges) / Math.Max(1, a.LeadChanges.Count), "0.00") + ")"
                + "; decided at median " + F(Median(a.Decided), "0.00") + " of the match; dead " + F(DeadShare(a)) + ", dead or walking back " + F(DownShare(a))
                + "; leader at 4:00 won " + a.LeaderAtHalfWon + " of " + a.LeaderAtHalf + " = " + F(LeaderWins(a))
                + "; kills per minute " + F(a.Kills / Math.Max(1e-9, a.Minutes), "0.00") + "; class kill shares " + cls
                + "; knock-out wait mean " + F(a.RespawnSum / Math.Max(1, a.RespawnN), "0.00") + " s"
                + "; rally in " + a.RallyMatches + " matches (" + F(a.RallySeconds / Math.Max(1, a.RallyMatches), "0.0") + " s each), the rallying team won " + a.RallyWon
                + "; sim " + F(a.SimSeconds * 1e6 / Math.Max(1, a.Ticks), "0.0") + " us per tick");
            if (!a.Choices) return;
            string picks = "";
            for (int o = 1; o <= 10; o++)
                picks += (o > 1 ? "; " : "") + HutChoiceNames.Name[o] + " picked " + a.Picks[o] + " won " + a.PickWins[o] + " (" + (a.Picks[o] > 0 ? F((double)a.PickWins[o] / a.Picks[o], "0.00") : "-") + ")";
            Console.WriteLine("[b15 soak] " + a.Name + " hut options: " + picks);
            Console.WriteLine("[b15 soak] " + a.Name + " votes: opened " + a.Opened + ", closed " + a.Closed + ", closed with no vote " + a.Empty + ", longest " + a.Longest + " ticks");
        }

        static double Sum(List<double> v)
        {
            double s = 0;
            foreach (double x in v) s += x;
            return s;
        }

        static bool Check(string what, string value, bool ok, string limit)
        {
            Console.WriteLine("[b15 soak] " + what + ": " + value + " (limit " + limit + ") " + (ok ? "PASS" : "FAIL"));
            return ok;
        }

        /// <summary>Options with at least this many picks are judged on their win rate.</summary>
        public const int MinPicksJudged = 6;

        static bool Target(string what, string value, bool ok, string limit) => Check("target " + what, value, ok, limit);

        /// <summary>
        /// The owner's balance targets are printed as "target" lines (PASS / FAIL) and reported; they are not all reachable by
        /// these two rules on today's bots (both-off already misses most of them), so the test FAILS only on what the rules own:
        /// every vote closes inside its window, RALLY never exceeds its cap, the knock-out wait stays inside 3..8 s, and the
        /// option win rates of the coin-flip arm (the honest measure of an option) stay at or under 0.65.
        /// </summary>
        [Test, Category("Slow"), Timeout(3000000)]
        public void ComebackAndHutChoicesOnTheTwentyFourSeedSixteenAside()
        {
            Arm off = Run("both off", false, false);
            Arm cb = Run("comeback on", true, false);
            Arm hut = Run("hut choices on", false, true);
            Arm both = Run("both on", true, true);
            Arm coin = Run("both on, coin-flip picks", true, true, CoinPicks);
            Arm cp2 = Run("both on, pass-1 comeback (RALLY x1.25 at 15 s, 3 -> 8 s)", true, true, c =>
            {
                B15SweepTests.Bare(c);
                ComebackConfig k = c.Depth.Comeback;
                k.RallyOn = true; k.RallyPerMille = 250; k.BonusCapSeconds = 45; k.TrailThresholdSeconds = 15;
                k.RespawnCurveOn = true; k.RespawnStartSeconds = 3; k.RespawnEndSeconds = 8; k.CurvePower = 1;
            });
            Arm[] arms = { off, cb, hut, both, coin, cp2 };
            foreach (Arm a in arms) Print(a);
            foreach (Arm a in new[] { cb, hut, both, coin, cp2 }) WinShare.Judge("b15 " + a.Name, "16v16 pool", a.T16, off.T16, true);
            foreach (Arm a in arms)
            {
                string n = a.Name + ": ";
                Target(n + "blowout rate", F(BlowoutRate(a)), BlowoutRate(a) <= 0.15 + 1e-9, "0.15 or less");
                Target(n + "lead changes median", F(Median(a.LeadChanges), "0.0"), Median(a.LeadChanges) >= 5 - 1e-9, "5 or more");
                Target(n + "dead or walking back", F(DownShare(a)), DownShare(a) <= 0.28 + 1e-9, "0.28 or less");
                Target(n + "leader at 4:00 wins", F(LeaderWins(a)), LeaderWins(a) >= 0.60 - 1e-9 && LeaderWins(a) <= 0.80 + 1e-9, "0.60 to 0.80");
                Target(n + "decided at (median share of the match)", F(Median(a.Decided), "0.00"), Median(a.Decided) >= Median(off.Decided) - 1e-9, "not earlier than off " + F(Median(off.Decided), "0.00"));
            }
            int fails = 0;
            int window = (int)Math.Round(new HutChoicesConfig().VoteSeconds * 30);
            foreach (Arm a in new[] { hut, both, coin, cp2 })
            {
                if (!Check(a.Name + ": votes never stall", "longest " + a.Longest + " ticks, opened " + a.Opened + " closed " + a.Closed + " (a vote open at the final whistle is the only gap)",
                        a.Longest <= window && a.Opened - a.Closed <= a.Matches && a.Closed > 0, "<= " + window + " ticks, every vote closes")) fails++;
                for (int o = 1; o <= 10; o++)
                {
                    if (a.Picks[o] < MinPicksJudged) continue;
                    double wr = (double)a.PickWins[o] / a.Picks[o];
                    bool judged = ReferenceEquals(a, coin);
                    bool ok = Check(a.Name + ": " + HutChoiceNames.Name[o] + " win rate" + (judged ? "" : " (bots chose by need: printed)"), F(wr, "0.00") + " over " + a.Picks[o] + " picks", wr <= 0.65 + 1e-9, "0.65 or less");
                    if (judged && !ok) fails++;
                }
            }
            foreach (Arm a in new[] { cb, both, coin, cp2 })
            {
                double cap = Math.Max(45, new ComebackConfig().BonusCapSeconds);
                if (!Check(a.Name + ": RALLY within its cap", F(a.RallyMax, "0.00") + " s most in one team's match", a.RallyMax <= cap + 1e-6, "<= " + F(cap, "0") + " s")) fails++;
                var kc = new ComebackConfig();
                double lo = Math.Min(new HutChoicesConfig().RespawnFloorSeconds, kc.RespawnStartSeconds), hi = Math.Max(8, kc.RespawnEndSeconds) + kc.WaveSeconds;
                if (!Check(a.Name + ": knock-out wait inside the curve", F(a.RespawnMin, "0.00") + " .. " + F(a.RespawnMax, "0.00") + " s", a.RespawnMin >= lo - 1e-6 && a.RespawnMax <= hi + 1.0 / 30 + 1e-6, F(lo, "0.0") + " .. " + F(hi, "0.0") + " s")) fails++;
            }
            double usOff = off.SimSeconds * 1e6 / Math.Max(1, off.Ticks), usBoth = both.SimSeconds * 1e6 / Math.Max(1, both.Ticks);
            if (!Check("both on: sim cost per tick against both off (same job, 24 whole matches each)", F(usOff, "0.0") + " / " + F(usBoth, "0.0") + " us = " + F((usBoth / usOff - 1) * 100, "+0.0;-0.0") + " %", usBoth <= usOff * 1.03, "+3 % or less")) fails++;
            Assert.AreEqual(0, fails, "b15 rule invariants (see the [b15 soak] lines)");
        }

        /// <summary>Diagnostic (run by name): 72 seeds of both off against both on, to see how much of the win-share shift is the
        /// 24-seed pool's noise.</summary>
        [Test, Explicit, Timeout(3000000)]
        public void B15RulesSeventyTwoSeedSideSplit()
        {
            var seeds = new int[72];
            for (int i = 0; i < seeds.Length; i++) seeds[i] = i < 24 ? WinShare.Seeds16[i] : 50000 + i * 7919;
            Arm off = Run("72 seeds both off", false, false, null, seeds);
            Arm both = Run("72 seeds both on", true, true, null, seeds);
            Arm coin = Run("72 seeds both on, coin-flip picks", true, true, CoinPicks, seeds);
            foreach (Arm a in new[] { off, both, coin }) Print(a);
            WinShare.Judge("b15 72 seeds both on", "16v16 pool", both.T16, off.T16, true);
            WinShare.Judge("b15 72 seeds coin-flip picks", "16v16 pool", coin.T16, off.T16, true);
        }
    }
}
