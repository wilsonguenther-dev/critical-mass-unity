using System;
using System.Collections.Generic;
using System.Globalization;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b15 second pass, the diagnosis (explicit, run by name): WHY do 16v16 bot matches blow out? Per 24-seed arm it prints the
    /// first hill owner's win rate, who owns the clock (winner / loser / contested / neutral, per half), bodies on the hill,
    /// death -> back on the hill (dead + walk), how attackers arrive (alone or outnumbered), the Kaiju's side against the result,
    /// the hut and allegiance at the hatch, and the bots' hill share by leading / trailing. Plus the seed-1 shiver soak per arm.
    /// </summary>
    public sealed class B15DiagnoseTests
    {
        static string F(double v, string f = "0.00") => v.ToString(f, CultureInfo.InvariantCulture);

        static double Median(List<double> v)
        {
            if (v.Count == 0) return double.NaN;
            var s = new List<double>(v);
            s.Sort();
            return s.Count % 2 == 1 ? s[s.Count / 2] : 0.5 * (s[s.Count / 2 - 1] + s[s.Count / 2]);
        }

        static double Mean(List<double> v)
        {
            double s = 0;
            foreach (double x in v) s += x;
            return v.Count > 0 ? s / v.Count : double.NaN;
        }

        public sealed class Diag
        {
            public string Name;
            public int Matches, FirstOwnerWon, FirstOwnerN, KaijuSideWon, KaijuSideN, HalfLeaderWon, HalfLeaderN, HalfLeaderKaiju, HutAheadWon, HutAheadN;
            public int AllegAheadWon, AllegAheadN, HalfLeaderGotKaiju, Berserk;
            public double WinOwn1, LoseOwn1, Cont1, Neut1, WinOwn2, LoseOwn2, Cont2, Neut2, Secs1, Secs2;
            public double OwnerBodies, ChallBodies, OwnedTicks, NearOwner, NearChall;
            public readonly List<double> DeadTime = new List<double>(), WalkTime = new List<double>(), WalkTrail = new List<double>(), WalkLead = new List<double>();
            public int Arrivals, ArrivalsOutnumbered2, ArrivalsAlone, ArrivalsKilled5;
            public readonly List<double> ArrivalGap = new List<double>();
            public double TrailHillRole, TrailBots, LeadHillRole, LeadBots, KaijuTrailFed, KaijuLeadFed;
            public readonly List<double> WinnerShare = new List<double>(), LoserShare = new List<double>(), Flips = new List<double>(), Length = new List<double>();
            public readonly double[] LeadAt = new double[9];
            public readonly int[] LeadAtN = new int[9];
        }

        public static void Play(Diag d, int seed, Action<GameConfig> tweak, int teamSize = 16)
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full);
            tweak?.Invoke(cfg);
            World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = teamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            InputFrame scratch = SimInput.EmptyInput();
            int hz = (int)Math.Round(cfg.SimHz), n = w.ActorCount;
            int max = (int)Math.Ceiling(cfg.MatchSeconds * cfg.SimHz) + hz;
            double r = cfg.Hill.Radius, r2 = r * r, near = (r + 6) * (r + 6), dt = SimStep.Dt(cfg);
            int[] deadAt = new int[n], backAt = new int[n], arriveAt = new int[n];
            bool[] inZone = new bool[n], seeking = new bool[n];
            for (int i = 0; i < n; i++) { deadAt[i] = -1; backAt[i] = -1; arriveAt[i] = -1; }
            int cursor = w.EventHead;
            int firstOwner = -1, halfLeader = 0, kaijuSide = -1, hutDiffHalf = 0, allegDiff = 0;
            int lastArrivalTick0 = -1, lastArrivalTick1 = -1, flips = 0, lastSign = 0;
            double halfT = cfg.MatchSeconds * 0.5;
            bool halfTaken = false, hatchTaken = false;
            BotsState st = null;
            int t = 0;
            // owner-state seconds per half from the winner's point of view, filled at the end
            var ownLog = new List<sbyte>(max);
            for (; t < max && w.Match.Phase != MatchPhase.Ended; t++)
            {
                MatchTick.Advance(w, null, scratch);
                if (st == null) st = BotBrain.GetBotsState(w);
                MatchState m = w.Match;
                for (; cursor < w.EventHead; cursor++)
                {
                    SimEvent e = w.Events[cursor % World.EventCapacity];
                    if (e.Type == SimEventType.Death && e.A >= 0 && e.A < n)
                    {
                        deadAt[e.A] = w.Tick;
                        seeking[e.A] = false;
                        if (arriveAt[e.A] >= 0 && w.Tick - arriveAt[e.A] <= 5 * hz) d.ArrivalsKilled5++;
                        arriveAt[e.A] = -1;
                    }
                    else if (e.Type == SimEventType.Respawn && e.A >= 0 && e.A < n && deadAt[e.A] >= 0)
                    {
                        d.DeadTime.Add((w.Tick - deadAt[e.A]) / cfg.SimHz);
                        backAt[e.A] = w.Tick;
                        seeking[e.A] = true;
                    }
                }
                HillState hs = m.HillState;
                sbyte code = (sbyte)(hs == HillState.Azure ? 1 : hs == HillState.Crimson ? 2 : hs == HillState.Contested ? 3 : 0);
                ownLog.Add(code);
                if (firstOwner < 0 && (code == 1 || code == 2)) firstOwner = code - 1;
                int ownerTeam = code == 1 ? 0 : code == 2 ? 1 : -1;
                if (ownerTeam >= 0)
                {
                    d.OwnedTicks++;
                    d.OwnerBodies += ownerTeam == 0 ? m.HillCount.Azure : m.HillCount.Crimson;
                    d.ChallBodies += ownerTeam == 0 ? m.HillCount.Crimson : m.HillCount.Azure;
                    int na = 0, nc = 0;
                    for (int i = 0; i < n; i++)
                    {
                        if (!w.Actors[i].Alive) continue;
                        double ddx = w.X[i] - w.Map.Hill.X, ddz = w.Z[i] - w.Map.Hill.Z;
                        if (ddx * ddx + ddz * ddz < near) { if (w.Actors[i].Team == TeamId.Azure) na++; else nc++; }
                    }
                    d.NearOwner += ownerTeam == 0 ? na : nc;
                    d.NearChall += ownerTeam == 0 ? nc : na;
                }
                // arrivals into the hill zone
                for (int i = 0; i < n; i++)
                {
                    Actor a = w.Actors[i];
                    bool z = false;
                    if (a.Alive)
                    {
                        double ddx = w.X[i] - w.Map.Hill.X, ddz = w.Z[i] - w.Map.Hill.Z;
                        z = ddx * ddx + ddz * ddz < r2;
                    }
                    if (z && !inZone[i])
                    {
                        int team = (int)a.Team;
                        if (seeking[i] && backAt[i] >= 0)
                        {
                            double walk = (w.Tick - backAt[i]) / cfg.SimHz;
                            if (walk < 60)
                            {
                                d.WalkTime.Add(walk);
                                double lead = team == 0 ? m.HillSeconds.Azure - m.HillSeconds.Crimson : m.HillSeconds.Crimson - m.HillSeconds.Azure;
                                (lead < 0 ? d.WalkTrail : d.WalkLead).Add(walk);
                            }
                            seeking[i] = false;
                        }
                        if (ownerTeam >= 0 && ownerTeam != team)
                        {
                            d.Arrivals++;
                            arriveAt[i] = w.Tick;
                            double mine = team == 0 ? m.HillCount.Azure : m.HillCount.Crimson;
                            double theirs = team == 0 ? m.HillCount.Crimson : m.HillCount.Azure;
                            if (mine <= 1) d.ArrivalsAlone++;
                            if (theirs - mine >= 2) d.ArrivalsOutnumbered2++;
                            int last = team == 0 ? lastArrivalTick0 : lastArrivalTick1;
                            if (last >= 0) d.ArrivalGap.Add((w.Tick - last) / cfg.SimHz);
                            if (team == 0) lastArrivalTick0 = w.Tick; else lastArrivalTick1 = w.Tick;
                        }
                    }
                    inZone[i] = z;
                }
                if (w.Tick % hz == 0)
                {
                    double lead = m.HillSeconds.Azure - m.HillSeconds.Crimson;
                    int sg = lead > 0.5 ? 1 : lead < -0.5 ? -1 : 0;
                    if (sg != 0 && lastSign != 0 && sg != lastSign) flips++;
                    if (sg != 0) lastSign = sg;
                    int sec = (int)Math.Round(m.Time);
                    if (sec % 60 == 0 && sec / 60 <= 8) { d.LeadAt[sec / 60] += Math.Abs(lead); d.LeadAtN[sec / 60]++; }
                    // bots on hill duty, by leading / trailing team
                    for (int team = 0; team < 2 && st != null; team++)
                    {
                        double tl = team == 0 ? lead : -lead;
                        int bots = 0, hill = 0, fed = 0;
                        for (int i = 0; i < n; i++)
                        {
                            if ((int)w.Actors[i].Team != team || !w.Actors[i].Alive || !BotsState.IsBot(st, w, i)) continue;
                            bots++;
                            if (st.Role[i] == BotsState.RoleHill) hill++;
                            if (st.Role[i] == BotsState.RoleKaiju) fed++;
                        }
                        if (tl < -0.5) { d.TrailBots += bots; d.TrailHillRole += hill; if (!m.Hatched) d.KaijuTrailFed += fed; }
                        else if (tl > 0.5) { d.LeadBots += bots; d.LeadHillRole += hill; if (!m.Hatched) d.KaijuLeadFed += fed; }
                    }
                    if (!halfTaken && m.Time >= halfT - 1e-6)
                    {
                        halfTaken = true;
                        halfLeader = sg;
                        hutDiffHalf = w.Hut.Level[0] - w.Hut.Level[1];
                    }
                    if (!hatchTaken && m.Hatched && w.Kaiju.State != KaijuState.Egg)
                    {
                        hatchTaken = true;
                        kaijuSide = w.Kaiju.State == KaijuState.Azure ? 0 : w.Kaiju.State == KaijuState.Crimson ? 1 : w.Kaiju.State == KaijuState.Berserk ? 3 : 2;
                        allegDiff = (int)Math.Round(m.Allegiance.Azure - m.Allegiance.Crimson);
                    }
                }
            }
            d.Matches++;
            MatchResult win = w.Match.Winner;
            int winT = win == MatchResult.Azure ? 0 : win == MatchResult.Crimson ? 1 : -1;
            d.Length.Add(w.Match.Time);
            d.Flips.Add(flips);
            if (winT >= 0)
            {
                double ha = w.Match.HillSeconds.Azure, hc = w.Match.HillSeconds.Crimson, len = Math.Max(1, w.Match.Time);
                d.WinnerShare.Add((winT == 0 ? ha : hc) / len);
                d.LoserShare.Add((winT == 0 ? hc : ha) / len);
                if (firstOwner >= 0) { d.FirstOwnerN++; if (firstOwner == winT) d.FirstOwnerWon++; }
                if (kaijuSide == 0 || kaijuSide == 1) { d.KaijuSideN++; if (kaijuSide == winT) d.KaijuSideWon++; }
                if (kaijuSide == 3) d.Berserk++;
                if (halfLeader != 0)
                {
                    int hl = halfLeader > 0 ? 0 : 1;
                    d.HalfLeaderN++;
                    if (hl == winT) d.HalfLeaderWon++;
                    if (kaijuSide == hl) d.HalfLeaderGotKaiju++;
                }
                if (hutDiffHalf != 0) { d.HutAheadN++; if ((hutDiffHalf > 0 ? 0 : 1) == winT) d.HutAheadWon++; }
                if (allegDiff != 0) { d.AllegAheadN++; if ((allegDiff > 0 ? 0 : 1) == winT) d.AllegAheadWon++; }
                int half = (int)Math.Round(halfT * cfg.SimHz);
                for (int k = 0; k < ownLog.Count; k++)
                {
                    sbyte c = ownLog[k];
                    bool first = k < half;
                    double s = dt;
                    if (c == 0) { if (first) d.Neut1 += s; else d.Neut2 += s; }
                    else if (c == 3) { if (first) d.Cont1 += s; else d.Cont2 += s; }
                    else if (c - 1 == winT) { if (first) d.WinOwn1 += s; else d.WinOwn2 += s; }
                    else { if (first) d.LoseOwn1 += s; else d.LoseOwn2 += s; }
                    if (first) d.Secs1 += s; else d.Secs2 += s;
                }
            }
            Console.WriteLine("[b15 diag] " + d.Name + " seed " + seed + ": winner " + win + ", hill " + F(w.Match.HillSeconds.Azure, "0") + "/" + F(w.Match.HillSeconds.Crimson, "0")
                + ", first owner " + firstOwner + ", leader at half " + halfLeader + ", Kaiju side " + kaijuSide + " (allegiance A-C " + allegDiff + "), hut A-C at half " + hutDiffHalf + ", lead changes " + flips);
        }

        public static void Print(Diag d)
        {
            double s1 = Math.Max(1e-9, d.Secs1), s2 = Math.Max(1e-9, d.Secs2), ot = Math.Max(1, d.OwnedTicks);
            Console.WriteLine("[b15 diag] " + d.Name + " SUMMARY over " + d.Matches + " matches:");
            Console.WriteLine("[b15 diag]   winner's hill share of the clock median " + F(Median(d.WinnerShare)) + " (loser " + F(Median(d.LoserShare)) + "); match length median " + F(Median(d.Length), "0") + " s; lead changes median " + F(Median(d.Flips), "0.0"));
            Console.WriteLine("[b15 diag]   first half: winner owns " + F(d.WinOwn1 / s1) + ", loser owns " + F(d.LoseOwn1 / s1) + ", contested " + F(d.Cont1 / s1) + ", neutral " + F(d.Neut1 / s1)
                + "; second half: winner " + F(d.WinOwn2 / s2) + ", loser " + F(d.LoseOwn2 / s2) + ", contested " + F(d.Cont2 / s2) + ", neutral " + F(d.Neut2 / s2));
            Console.WriteLine("[b15 diag]   first hill owner won " + d.FirstOwnerWon + " of " + d.FirstOwnerN + "; leader at half won " + d.HalfLeaderWon + " of " + d.HalfLeaderN + " and got the Kaiju " + d.HalfLeaderGotKaiju
                + "; the Kaiju's side won " + d.KaijuSideWon + " of " + d.KaijuSideN + " (berserk " + d.Berserk + "); more allegiance at the hatch won " + d.AllegAheadWon + " of " + d.AllegAheadN
                + "; higher hut at half won " + d.HutAheadWon + " of " + d.HutAheadN);
            Console.WriteLine("[b15 diag]   while owned: bodies on the hill owner " + F(d.OwnerBodies / ot) + " vs challenger " + F(d.ChallBodies / ot) + "; within radius+6: " + F(d.NearOwner / ot) + " vs " + F(d.NearChall / ot));
            Console.WriteLine("[b15 diag]   knock-out to back: dead median " + F(Median(d.DeadTime)) + " s, walk to the hill median " + F(Median(d.WalkTime)) + " s (mean " + F(Mean(d.WalkTime)) + "; trailing team " + F(Median(d.WalkTrail)) + ", leading " + F(Median(d.WalkLead)) + ")");
            Console.WriteLine("[b15 diag]   attackers arriving on an owned hill: " + d.Arrivals + "; alone (<= 1 of theirs on it) " + F((double)d.ArrivalsAlone / Math.Max(1, d.Arrivals)) + ", outnumbered by 2+ " + F((double)d.ArrivalsOutnumbered2 / Math.Max(1, d.Arrivals))
                + ", knocked out within 5 s " + F((double)d.ArrivalsKilled5 / Math.Max(1, d.Arrivals)) + "; gap between a team's arrivals median " + F(Median(d.ArrivalGap)) + " s");
            Console.WriteLine("[b15 diag]   bots on hill duty: trailing team " + F(d.TrailHillRole / Math.Max(1, d.TrailBots)) + ", leading " + F(d.LeadHillRole / Math.Max(1, d.LeadBots))
                + "; feeding before the hatch: trailing " + F(d.KaijuTrailFed / Math.Max(1, d.TrailBots)) + ", leading " + F(d.KaijuLeadFed / Math.Max(1, d.LeadBots)));
            string la = "";
            for (int k = 0; k <= 8; k++) la += (k > 0 ? " " : "") + k + ":" + F(d.LeadAtN[k] > 0 ? d.LeadAt[k] / d.LeadAtN[k] : 0, "0");
            Console.WriteLine("[b15 diag]   mean |lead| by minute: " + la);
        }

        static string Top(Dictionary<string, int> d)
        {
            var l = new List<KeyValuePair<string, int>>(d);
            l.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : string.CompareOrdinal(a.Key, b.Key));
            string s = "";
            for (int i = 0; i < Math.Min(6, l.Count); i++) s += (i > 0 ? ", " : "") + l[i].Key + " x" + l[i].Value;
            return s;
        }

        static int CountWalls(World w)
        {
            int n = 0;
            foreach (byte c in w.Map.Cells) if (c == Cell.LowWall || c == Cell.Barricade) n++;
            return n;
        }

        public static Diag Run(string name, Action<GameConfig> tweak, int[] seeds = null)
        {
            var d = new Diag { Name = name };
            foreach (int s in seeds ?? WinShare.Seeds16) Play(d, s, tweak);
            Print(d);
            return d;
        }

        [Test, Explicit, Timeout(3000000)]
        public void B15DiagWhyMatchesBlowOut()
        {
            Run("both off", c => { c.Depth.Comeback.Enabled = false; c.Depth.HutChoices.Enabled = false; });
            Run("hut choices only", c => { c.Depth.Comeback.Enabled = false; });
        }

        [Test, Explicit, Timeout(3000000)]
        public void B15DiagShiverSeedOneHatchSixty()
        {
            foreach (int arm in new[] { 0, 1, 2, 3 })
            {
                GameConfig cfg = GameConfig.CreateDefault();
                cfg.Physics.KaijuNavGuard = true;
                cfg.Physics.BotWideLine = true;
                cfg.Physics.BotCalmSteer = true;
                cfg.Physics.KaijuBridgeSqueeze = true;
                cfg.HatchSeconds = 60;
                cfg.Depth.Comeback.Enabled = (arm & 1) != 0;
                cfg.Depth.HutChoices.Enabled = (arm & 2) != 0;
                World w = World.CreateWorld(new WorldOptions { Seed = 1, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
                var watch = new ShiverWatch();
                watch.MaxFlags = 5000;
                InputFrame scratch = SimInput.EmptyInput();
                var byMinute = new int[9];
                var byClass = new int[7];
                int seen = 0, total = 0;
                for (int t = 0; t < 14400 && w.Match.Phase != MatchPhase.Ended; t++)
                {
                    MatchTick.Advance(w, null, scratch);
                    watch.Observe(w);
                    byMinute[Math.Min(8, (int)(w.Match.Time / 60))] += watch.Count - total;
                    total = watch.Count;
                    for (; seen < watch.Flags.Count; seen++)
                    {
                        ShiverFlag f = watch.Flags[seen];
                        byClass[f.Actor == ShiverWatch.KaijuId || f.Actor < 0 || f.Actor >= w.ActorCount ? 6 : (int)w.Actors[f.Actor].Cls]++;
                    }
                }
                string bm = "", bc = "";
                for (int k = 0; k < 9; k++) bm += (k > 0 ? " " : "") + byMinute[k];
                for (int k = 0; k < 7; k++) bc += (k > 0 ? " " : "") + (k < 6 ? ((ClassId)k).ToString() : "Kaiju") + " " + byClass[k];
                Console.WriteLine("[b15 diag shiver] seed 1 hatch 60 guard on, comeback " + ((arm & 1) != 0) + ", hut choices " + ((arm & 2) != 0) + ": flags " + watch.Count
                    + " (dither " + watch.CountByKind[0] + ", reversals " + watch.CountByKind[1] + ", pressed " + watch.CountByKind[2] + "); by minute " + bm + "; by class (first 256 flags) " + bc
                    + "; winner " + w.Match.Winner + " hill " + F(w.Match.HillSeconds.Azure, "0") + "/" + F(w.Match.HillSeconds.Crimson, "0") + ", Kaiju " + w.Kaiju.State);
                var cells = new Dictionary<string, int>();
                var states = new Dictionary<string, int>();
                var actors = new Dictionary<string, int>();
                foreach (ShiverFlag f in watch.Flags)
                {
                    string c = ((int)(f.X / 3) * 3) + ":" + ((int)(f.Z / 3) * 3);
                    cells[c] = (cells.TryGetValue(c, out int v) ? v : 0) + 1;
                    string sk = f.State + "/" + f.Kind;
                    states[sk] = (states.TryGetValue(sk, out int v2) ? v2 : 0) + 1;
                    string ak = f.Actor + " " + f.Cls + " " + f.Team;
                    actors[ak] = (actors.TryGetValue(ak, out int v3) ? v3 : 0) + 1;
                }
                Console.WriteLine("[b15 diag shiver]   top cells (3 u) " + Top(cells) + "; top state/kind " + Top(states) + "; top actors " + Top(actors)
                    + "; walls up at the end " + CountWalls(w) + ", hut picks A/C mask " + w.HutVote.Mask[0] + "/" + w.HutVote.Mask[1]);
            }
        }
    }
}
