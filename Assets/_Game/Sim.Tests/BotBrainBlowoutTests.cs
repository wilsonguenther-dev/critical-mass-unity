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
    /// b15 bot brain: how much of the blowout problem smarter bots fix WITHOUT any comeback rule (none is on this base).
    /// 16v16 Full bot matches, the same seeds with BotBrain.Enabled off and on. Per arm: blowout rate (the winner held the hill
    /// 2x the loser's seconds, the rules lane's definition), hill lead changes per match (sign of the hill-seconds lead, sampled
    /// every second), leader-at-half win rate, the Kaiju side's share of hill seconds by half, the Kaiju side's win rate,
    /// arrivals on the hill (entering hill radius + 2 alive after the hatch) outnumbered by 2+ and down within 5 s, the hill
    /// owner's bodies against the challengers', stuck episodes (pressing, under 0.4 u in 3 s), shiver flags and the tick cost.
    /// </summary>
    [TestFixture]
    public sealed class BotBrainBlowoutTests
    {
        private sealed class Arm
        {
            public string Name;
            public int Matches, Decided, Blowouts, LeaderAtHalf, LeaderAtHalfWon, Sided, SideWon, Arrivals, Outnumbered, DownFast, Shiver, Rallies, Releases;
            public double LeadChanges, SideShare1, SideShare2, SideHalves, OwnerBodies, ChallengerBodies, BodySamples, Us;
            public long Ticks;
            public readonly List<int> Stuck = new List<int>();
        }

        private static void Play(Arm arm, int seed, bool brain)
        {
            GameConfig cfg = GameConfig.CreateDefault();
            cfg.Depth.BotBrain.Enabled = brain;
            World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            InputFrame scratch = SimInput.EmptyInput();
            HillDef hill = w.Map.Hill;
            int n = w.ActorCount, hz = (int)Math.Round(cfg.SimHz), head = w.EventHead;
            double zone2 = (hill.R + 2) * (hill.R + 2);
            var inZone = new bool[n];
            var arrivedAt = new int[n];
            var sx = new double[n];
            var sz = new double[n];
            var sat = new int[n];
            for (int i = 0; i < n; i++)
            {
                arrivedAt[i] = -1;
                sat[i] = -1;
            }
            var watch = new ShiverWatch();
            var sw = new Stopwatch();
            int stuck = 0, lastSign = 0, changes = 0, halfSign = 0, side = 0, halfTick = (int)Math.Round(cfg.MatchSeconds * 0.5 * hz);
            double halfA = 0, halfC = 0;
            while (w.Match.Phase != MatchPhase.Ended && w.Tick < (int)(cfg.MatchSeconds * hz) + 600)
            {
                sw.Start();
                MatchTick.Advance(w, null, scratch);
                sw.Stop();
                arm.Ticks++;
                watch.Observe(w);
                Kaiju k = w.Kaiju;
                if (side == 0 && w.Match.Hatched) side = k.State == KaijuState.Azure ? 1 : k.State == KaijuState.Crimson ? -1 : 0;
                for (; head < w.EventHead; head++)
                {
                    SimEvent e = w.Events[head % World.EventCapacity];
                    if (e.Type != SimEventType.Death || e.A < 0 || e.A >= n) continue;
                    if (arrivedAt[e.A] >= 0 && w.Tick - arrivedAt[e.A] <= 5 * hz) arm.DownFast++;
                    arrivedAt[e.A] = -1;
                }
                for (int i = 0; i < n; i++)
                {
                    Actor a = w.Actors[i];
                    double dx = w.X[i] - hill.X, dz = w.Z[i] - hill.Z;
                    bool z = a.Alive && dx * dx + dz * dz <= zone2;
                    if (z && !inZone[i] && w.Match.Hatched)
                    {
                        int mine = 0, theirs = 0;
                        for (int j = 0; j < n; j++)
                        {
                            if (!w.Actors[j].Alive) continue;
                            double jx = w.X[j] - hill.X, jz = w.Z[j] - hill.Z;
                            if (jx * jx + jz * jz > zone2) continue;
                            if (w.Actors[j].Team == a.Team) mine++;
                            else theirs++;
                        }
                        arm.Arrivals++;
                        if (theirs - mine >= 2) arm.Outnumbered++;
                        arrivedAt[i] = w.Tick;
                    }
                    inZone[i] = z;
                    InputFrame f = w.Inputs[i];
                    bool pressing = a.Alive && a.StandUp <= 0 && f.MoveX * f.MoveX + f.MoveZ * f.MoveZ > 0.01;
                    double mx = w.X[i] - sx[i], mz = w.Z[i] - sz[i];
                    if (!pressing || sat[i] < 0 || mx * mx + mz * mz >= 0.16)
                    {
                        sx[i] = w.X[i];
                        sz[i] = w.Z[i];
                        sat[i] = pressing ? w.Tick : -1;
                    }
                    else if (w.Tick - sat[i] >= 3 * hz)
                    {
                        stuck++;
                        sat[i] = w.Tick;
                    }
                }
                if (w.Tick % hz != 0) continue;
                double lead = w.Match.HillSeconds.Azure - w.Match.HillSeconds.Crimson;
                int sg = lead > 0.5 ? 1 : lead < -0.5 ? -1 : 0;
                if (sg != 0 && lastSign != 0 && sg != lastSign) changes++;
                if (sg != 0) lastSign = sg;
                if (w.Tick == halfTick)
                {
                    halfSign = sg;
                    halfA = w.Match.HillSeconds.Azure;
                    halfC = w.Match.HillSeconds.Crimson;
                }
                if (w.Match.Hatched && w.Tick > halfTick)
                {
                    int ca = 0, cc = 0;
                    for (int j = 0; j < n; j++)
                    {
                        if (!w.Actors[j].Alive) continue;
                        double jx = w.X[j] - hill.X, jz = w.Z[j] - hill.Z;
                        if (jx * jx + jz * jz > zone2) continue;
                        if (w.Actors[j].Team == TeamId.Azure) ca++;
                        else cc++;
                    }
                    if (ca + cc > 0)
                    {
                        arm.OwnerBodies += Math.Max(ca, cc);
                        arm.ChallengerBodies += Math.Min(ca, cc);
                        arm.BodySamples++;
                    }
                }
            }
            BotsState st = BotBrain.GetBotsState(w);
            arm.Rallies += st.Rallies;
            arm.Releases += st.Releases;
            arm.Us += sw.Elapsed.TotalMilliseconds * 1000;
            arm.Matches++;
            arm.LeadChanges += changes;
            arm.Stuck.Add(stuck);
            arm.Shiver += watch.Count;
            double ha = w.Match.HillSeconds.Azure, hc = w.Match.HillSeconds.Crimson;
            MatchResult win = w.Match.Winner;
            int ws = win == MatchResult.Azure ? 1 : win == MatchResult.Crimson ? -1 : 0;
            if (ws != 0)
            {
                arm.Decided++;
                if (Math.Max(ha, hc) >= 2 * Math.Min(ha, hc)) arm.Blowouts++;
                if (halfSign != 0)
                {
                    arm.LeaderAtHalf++;
                    if (halfSign == ws) arm.LeaderAtHalfWon++;
                }
            }
            if (side != 0)
            {
                arm.Sided++;
                if (side == ws) arm.SideWon++;
                double s1 = side > 0 ? halfA : halfC, o1 = side > 0 ? halfC : halfA;
                double s2 = (side > 0 ? ha : hc) - s1, o2 = (side > 0 ? hc : ha) - o1;
                if (s1 + o1 > 0 && s2 + o2 > 0)
                {
                    arm.SideShare1 += s1 / (s1 + o1);
                    arm.SideShare2 += s2 / (s2 + o2);
                    arm.SideHalves++;
                }
            }
            Console.WriteLine("{\"item\":\"b15 brain match\",\"arm\":\"" + arm.Name + "\",\"seed\":" + seed + ",\"winner\":\"" + win + "\",\"hill\":\"" + ha.ToString("0") + "/" + hc.ToString("0") +
                              "\",\"leadChanges\":" + changes + ",\"kaijuSide\":" + side + ",\"stuck\":" + stuck + ",\"shiver\":" + watch.Count + ",\"rallies\":" + st.Rallies + "}");
        }

        private static string F(double v) => v.ToString("0.000");

        private static string Summary(Arm a)
        {
            var s = new List<int>(a.Stuck);
            s.Sort();
            double med = s.Count == 0 ? 0 : s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2.0;
            return "{\"item\":\"b15 brain arm\",\"arm\":\"" + a.Name + "\",\"matches\":" + a.Matches + ",\"blowoutRate\":" + F(a.Blowouts / (double)Math.Max(1, a.Decided)) +
                   ",\"leadChangesPerMatch\":" + (a.LeadChanges / Math.Max(1, a.Matches)).ToString("0.00") + ",\"leaderAtHalfWins\":" + F(a.LeaderAtHalfWon / (double)Math.Max(1, a.LeaderAtHalf)) +
                   ",\"kaijuSideHillShareH1\":" + F(a.SideShare1 / Math.Max(1, a.SideHalves)) + ",\"kaijuSideHillShareH2\":" + F(a.SideShare2 / Math.Max(1, a.SideHalves)) +
                   ",\"kaijuSideWins\":" + F(a.SideWon / (double)Math.Max(1, a.Sided)) + ",\"arrivals\":" + a.Arrivals + ",\"arrivalsOutnumbered2\":" + F(a.Outnumbered / (double)Math.Max(1, a.Arrivals)) +
                   ",\"downWithin5s\":" + F(a.DownFast / (double)Math.Max(1, a.Arrivals)) + ",\"ownerBodiesH2\":" + (a.OwnerBodies / Math.Max(1, a.BodySamples)).ToString("0.0") +
                   ",\"challengerBodiesH2\":" + (a.ChallengerBodies / Math.Max(1, a.BodySamples)).ToString("0.0") + ",\"stuckMedian\":" + med.ToString("0.0") + ",\"shiverFlags\":" + a.Shiver +
                   ",\"rallies\":" + a.Rallies + ",\"releases\":" + a.Releases + ",\"usPerTick\":" + (a.Us / Math.Max(1, a.Ticks)).ToString("0.0") + "}";
        }

        private static void Compare(int[] seeds, string label)
        {
            var off = new Arm { Name = "brain off" };
            var on = new Arm { Name = "brain on" };
            foreach (int seed in seeds)
            {
                Play(off, seed, false);
                Play(on, seed, true);
            }
            Console.WriteLine(Summary(off));
            Console.WriteLine(Summary(on));
            Console.WriteLine("{\"item\":\"b15 brain compare\",\"seeds\":\"" + label + "\",\"tickCostRatio\":" + ((on.Us / Math.Max(1, on.Ticks)) / Math.Max(1e-9, off.Us / Math.Max(1, off.Ticks))).ToString("0.000") +
                              ",\"blowoutOff\":" + F(off.Blowouts / (double)Math.Max(1, off.Decided)) + ",\"blowoutOn\":" + F(on.Blowouts / (double)Math.Max(1, on.Decided)) + "}");
            Assert.AreEqual(seeds.Length, on.Matches);
        }

        [Test]
        public void TwentyFourSeedsBrainOffAgainstOn() => Compare(WinShare.Seeds16, "24 (WinShare.Seeds16)");

        [Test, Category("Slow")]
        public void SeventyTwoSeedsBrainOffAgainstOn()
        {
            var seeds = new List<int>(WinShare.Seeds16);
            for (int s = 101; seeds.Count < 72; s++) seeds.Add(s);
            Compare(seeds.ToArray(), "72 (Seeds16 + 101..148)");
        }
    }
}
