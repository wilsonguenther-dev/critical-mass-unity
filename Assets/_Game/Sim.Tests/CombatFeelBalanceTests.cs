using System;
using System.Collections.Generic;
using System.Diagnostics;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b15 combat feel, the 24-seed 16v16 balance (WinShare.Seeds16, whole bot matches, Full rules) with the rule on and the
    /// same-seed control (CombatFeel.Enabled = false, everything else equal). Prints both arms: team win share, kills per
    /// minute, median first knock-out, hill lead changes, damage share of the top 10 fighters, per-class damage and kill share,
    /// Healer healing, stuck episodes per match (the audit's measure: pressing to move and under 0.4 u in 3 s) with the hot
    /// cells, the longest stagger run of any actor (no stun-lock) and the mean sim cost per tick.
    /// Limits (on): win share within 0.15 of control (WinShare.Judge); top-10 damage share at most 0.45; no class above 0.40 of
    /// all damage; median stuck episodes per match under 10; longest stagger run at most 0.5 s; Healer healing above control.
    /// </summary>
    [TestFixture]
    [Category("Slow")]
    public sealed class CombatFeelBalanceTests
    {
        private sealed class Arm
        {
            public readonly WinShare.Tally Tally = new WinShare.Tally();
            public readonly List<double> FirstKo = new List<double>();
            public readonly List<int> Stuck = new List<int>();
            public readonly List<double> Top10 = new List<double>();
            public readonly double[] ClassDamage = new double[6];
            public readonly int[] ClassKills = new int[6];
            public readonly Dictionary<int, int> StuckCells = new Dictionary<int, int>();
            public double Kills, Minutes, LeadChanges, Healing, Damage, Us;
            public int Ticks, MaxLock;
        }

        private static void Play(int seed, bool feel, Arm arm)
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full);
            cfg.Depth.CombatFeel.Enabled = feel;
            World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            InputFrame scratch = SimInput.EmptyInput();
            int n = w.ActorCount;
            var dealt = new double[n];
            var ax = new double[n];
            var az = new double[n];
            var at = new int[n];
            var lock_ = new int[n];
            for (int i = 0; i < n; i++) at[i] = -1;
            int head = w.EventHead, stuck = 0, lastLeader = 0, window = (int)Math.Round(3 * cfg.SimHz);
            double firstKo = double.NaN, kills = 0;
            var sw = new Stopwatch();
            while (w.Match.Phase != MatchPhase.Ended && w.Tick < 15000)
            {
                sw.Start();
                MatchTick.Advance(w, null, scratch);
                sw.Stop();
                arm.Ticks++;
                for (; head < w.EventHead; head++)
                {
                    SimEvent e = w.Events[head % World.EventCapacity];
                    if (e.Type == SimEventType.Hit && e.B >= 0 && e.B < n)
                    {
                        dealt[e.B] += e.Value;
                        arm.ClassDamage[(int)w.Actors[e.B].Cls] += e.Value;
                        arm.Damage += e.Value;
                    }
                    else if (e.Type == SimEventType.Death)
                    {
                        kills++;
                        if (double.IsNaN(firstKo)) firstKo = w.Match.Time;
                        if (e.B >= 0 && e.B < n) arm.ClassKills[(int)w.Actors[e.B].Cls]++;
                    }
                }
                for (int i = 0; i < n; i++)
                {
                    Actor a = w.Actors[i];
                    lock_[i] = a.Alive && a.HitStunTicks > 0 ? lock_[i] + 1 : 0;
                    if (lock_[i] > arm.MaxLock) arm.MaxLock = lock_[i];
                    InputFrame f = w.Inputs[i];
                    bool pressing = a.Alive && a.StandUp <= 0 && f.MoveX * f.MoveX + f.MoveZ * f.MoveZ > 0.01;
                    double dx = w.X[i] - ax[i], dz = w.Z[i] - az[i];
                    if (!pressing || at[i] < 0 || dx * dx + dz * dz >= 0.16)
                    {
                        ax[i] = w.X[i];
                        az[i] = w.Z[i];
                        at[i] = pressing ? w.Tick : -1;
                    }
                    else if (w.Tick - at[i] >= window)
                    {
                        stuck++;
                        int cell = (int)Math.Floor(w.X[i]) * 1000 + (int)Math.Floor(w.Z[i]);
                        arm.StuckCells.TryGetValue(cell, out int c);
                        arm.StuckCells[cell] = c + 1;
                        at[i] = w.Tick;
                    }
                }
                double azH = w.Match.HillSeconds.Azure, crH = w.Match.HillSeconds.Crimson;
                int leader = azH > crH ? 1 : crH > azH ? -1 : 0;
                if (leader != 0 && lastLeader != 0 && leader != lastLeader) arm.LeadChanges++;
                if (leader != 0) lastLeader = leader;
            }
            arm.Us += sw.Elapsed.TotalMilliseconds * 1000;
            arm.Tally.Add(w.Match.Winner);
            arm.Kills += kills;
            arm.Minutes += w.Match.Time / 60;
            arm.FirstKo.Add(double.IsNaN(firstKo) ? w.Match.Time : firstKo);
            arm.Stuck.Add(stuck);
            arm.Healing += w.Healer.Stats.HealingDone;
            double total = 0;
            var sorted = (double[])dealt.Clone();
            Array.Sort(sorted);
            Array.Reverse(sorted);
            double top = 0;
            for (int i = 0; i < sorted.Length; i++)
            {
                total += sorted[i];
                if (i < 10) top += sorted[i];
            }
            arm.Top10.Add(total > 0 ? top / total : 0);
            Console.WriteLine("{\"item\":\"b15 balance match\",\"arm\":\"" + (feel ? "on" : "control") + "\",\"seed\":" + seed + ",\"winner\":\"" + w.Match.Winner + "\",\"kills\":" + kills +
                              ",\"firstKo\":" + (double.IsNaN(firstKo) ? -1 : firstKo).ToString("0.0") + ",\"stuck\":" + stuck + ",\"top10\":" + (total > 0 ? top / total : 0).ToString("0.000") +
                              ",\"healing\":" + w.Healer.Stats.HealingDone.ToString("0") + (feel ? ",\"staggers\":" + w.Depth.Feel.Staggers + ",\"immune\":" + w.Depth.Feel.StaggerImmune +
                              ",\"whiffs\":" + w.Depth.Feel.Whiffs + ",\"swings\":" + w.Depth.Feel.Swings + ",\"tntSelf\":" + w.Depth.Feel.TntSelfHits + ",\"ladderFalls\":" + w.Depth.Feel.LadderFalls : "") + "}");
        }

        private static double Median(List<double> v)
        {
            var s = new List<double>(v);
            s.Sort();
            return s.Count == 0 ? 0 : s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
        }

        private static double MedianI(List<int> v)
        {
            var d = new List<double>();
            foreach (int x in v) d.Add(x);
            return Median(d);
        }

        private static readonly string[] Names = { "Warrior", "Farmer", "Miner", "Demolitions", "Builder", "Healer" };

        private static void Print(string name, Arm a)
        {
            var cls = new System.Text.StringBuilder();
            for (int c = 0; c < 6; c++)
            {
                double kAll = 0;
                for (int k = 0; k < 6; k++) kAll += a.ClassKills[k];
                cls.Append(",\"" + Names[c] + "\":{\"dmgShare\":" + (a.Damage > 0 ? a.ClassDamage[c] / a.Damage : 0).ToString("0.000") +
                           ",\"killShare\":" + (kAll > 0 ? a.ClassKills[c] / kAll : 0).ToString("0.000") + "}");
            }
            var hot = new List<KeyValuePair<int, int>>(a.StuckCells);
            hot.Sort((x, y) => y.Value != x.Value ? y.Value.CompareTo(x.Value) : x.Key.CompareTo(y.Key));
            var hs = new System.Text.StringBuilder();
            for (int k = 0; k < Math.Min(4, hot.Count); k++) hs.Append((k > 0 ? " " : "") + (hot[k].Key / 1000) + "," + (hot[k].Key % 1000) + "x" + hot[k].Value);
            double top10 = 0;
            foreach (double t in a.Top10) top10 += t;
            top10 /= Math.Max(1, a.Top10.Count);
            Console.WriteLine("{\"item\":\"b15 balance\",\"arm\":\"" + name + "\",\"matches\":" + a.Tally.Matches + ",\"wins\":\"" + a.Tally + "\",\"killsPerMin\":" + (a.Kills / Math.Max(1e-9, a.Minutes)).ToString("0.00") +
                              ",\"medianFirstKo\":" + Median(a.FirstKo).ToString("0.0") + ",\"leadChangesPerMatch\":" + (a.LeadChanges / Math.Max(1, a.Tally.Matches)).ToString("0.00") +
                              ",\"top10DamageShare\":" + top10.ToString("0.000") + ",\"medianStuck\":" + MedianI(a.Stuck) + ",\"meanStuck\":" + (Sum(a.Stuck) / (double)Math.Max(1, a.Stuck.Count)).ToString("0.0") +
                              ",\"stuckHot\":\"" + hs + "\",\"healingPerMatch\":" + (a.Healing / Math.Max(1, a.Tally.Matches)).ToString("0") + ",\"maxStaggerRunS\":" + (a.MaxLock / 30.0).ToString("0.00") +
                              ",\"usPerTick\":" + (a.Us / Math.Max(1, a.Ticks)).ToString("0.0") + cls + "}");
        }

        private static int Sum(List<int> v)
        {
            int s = 0;
            foreach (int x in v) s += x;
            return s;
        }

        [Test]
        public void TwentyFourSeedSixteenVersusSixteenBalanceOnAndControl()
        {
            var on = new Arm();
            var off = new Arm();
            foreach (int seed in WinShare.Seeds16)
            {
                Play(seed, false, off);
                Play(seed, true, on);
            }
            Print("control", off);
            Print("on", on);
            double top10 = 0;
            foreach (double t in on.Top10) top10 += t;
            top10 /= Math.Max(1, on.Top10.Count);
            var bad = new List<string>();
            if (!WinShare.Judge("b15 combat feel", "16v16 x24", on.Tally, off.Tally, true)) bad.Add("win share");
            if (top10 > 0.45) bad.Add("top-10 damage share " + top10.ToString("0.000") + " > 0.45");
            for (int c = 0; c < 6; c++)
                if (on.Damage > 0 && on.ClassDamage[c] / on.Damage > 0.40) bad.Add(Names[c] + " deals " + (on.ClassDamage[c] / on.Damage).ToString("0.000") + " of all damage (> 0.40)");
            if (MedianI(on.Stuck) >= 10) bad.Add("median stuck episodes " + MedianI(on.Stuck) + " >= 10");
            if (on.MaxLock / 30.0 > 0.5) bad.Add("longest stagger run " + (on.MaxLock / 30.0).ToString("0.00") + " s > 0.5 s");
            if (!(on.Healing > off.Healing)) bad.Add("Healer healing not above control");
            if (!(on.Kills > 0)) bad.Add("no kills");
            Console.WriteLine("{\"item\":\"b15 balance verdict\",\"fails\":" + bad.Count + ",\"detail\":\"" + string.Join("; ", bad) + "\"}");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }
    }
}
