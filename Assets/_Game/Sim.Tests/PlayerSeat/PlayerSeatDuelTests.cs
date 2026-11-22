using System;
using System.Collections.Generic;
using System.Linq;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using T = CriticalMass.Sim.Tests.PlayerSeat.PlayerSeatTargets;

namespace CriticalMass.Sim.Tests.PlayerSeat
{
    /// <summary>
    /// Player-seat gate, tier S, duels (player-agency audit 6.4: S3, S5, S6, S11, and the lead's S35, S41-S45). A human model
    /// in actor 0's seat against a dummy, a real bot or a second human, on the empty Full-rules map. Category PlayerSeat
    /// (fast suite). Every case prints one "[px]" line with the measured number and the target.
    /// </summary>
    [TestFixture, Category("PlayerSeat")]
    public sealed class PlayerSeatDuelTests
    {
        static readonly ClassId[] All = T.Classes;

        sealed class Tally
        {
            public int Reps, Wins, Losses, Draws, Swings, Landed, TheirSwings, TheirLanded;
            public readonly List<double> Ttk = new List<double>();
            public double Share => Reps > 0 ? (double)Wins / Reps : 0;
            public double Land => Swings > 0 ? (double)Landed / Swings : 0;
            public double TheirLand => TheirSwings > 0 ? (double)TheirLanded / TheirSwings : 0;
            public void Add(DuelResult r)
            {
                Reps++;
                if (r.Won) { Wins++; Ttk.Add(r.Seconds); }
                else if (r.Lost) Losses++;
                else Draws++;
                Swings += r.Swings; Landed += r.Landed; TheirSwings += r.TheirSwings; TheirLanded += r.TheirLanded;
            }
            public override string ToString() => Wins + "/" + Reps + " won (" + Losses + " lost, " + Draws + " drawn), land " + PxLog.F(Land) + " of " + Swings + ", median ttk " + (Ttk.Count > 0 ? PxLog.F(PlayerSeatRig.Median(Ttk), "0.0") + " s" : "none");
        }

        static Tally Cell(ClassId me, ClassId them, TargetMode mode, int angles, int lag, bool half = false, bool strafe = false, BotDifficulty bots = BotDifficulty.Normal, Action<Seat, int> setup = null)
        {
            var t = new Tally();
            for (int k = 0; k < angles; k++)
            {
                Seat s = PlayerSeatRig.Duel(me, them, mode, PlayerSeatRig.AngleOf(k, angles), k, lag, half, strafe, bots);
                if (mode == TargetMode.HumanVsHuman)
                {
                    // two people are never the same: seat B's reaction is 5, 6 or 7 ticks and its first swing waits 0-3 ticks
                    // (two identical models trade blow for blow on the same tick and both fall: a 16-of-16 double knock-out)
                    s.Human2.LagTicks = lag + (k % 3) - 1;
                    s.Human2.HoldFireTicks = k % 4;
                }
                setup?.Invoke(s, k);
                t.Add(PlayerSeatRig.Play(s));
            }
            return t;
        }

        // ------------------------------------------------------------------ rig control

        [TestCase(ClassId.Warrior)]
        [TestCase(ClassId.Farmer)]
        [TestCase(ClassId.Miner)]
        [TestCase(ClassId.Demolitions)]
        [TestCase(ClassId.Builder)]
        [TestCase(ClassId.Healer)]
        public void Control_PassiveTarget_Lands(ClassId x)
        {
            // the rig's calibration: a lag-0 human against a passive dummy of every class lands (probe control: 97-100%)
            var t = new Tally();
            foreach (ClassId them in All)
                for (int k = 0; k < T.Angles; k++)
                {
                    Seat s = PlayerSeatRig.Duel(x, them, TargetMode.Passive, PlayerSeatRig.AngleOf(k, T.Angles), k);
                    s.NoTnt = true;
                    t.Add(PlayerSeatRig.Play(s));
                }
            bool ok = PxLog.Line("CTRL Control_PassiveTarget_Lands", x.ToString(), t.ToString(), "land >= " + T.PassiveLandMin + ", all killed", t.Land >= T.PassiveLandMin && t.Wins == t.Reps);
            Assert.IsTrue(ok, x + " vs passive: " + t);
        }

        // ------------------------------------------------------------------ S3

        [TestCase(ClassId.Warrior)]
        [TestCase(ClassId.Farmer)]
        [TestCase(ClassId.Miner)]
        [TestCase(ClassId.Demolitions)]
        [TestCase(ClassId.Builder)]
        [TestCase(ClassId.Healer)]
        public void S03_Primary_StrafingTarget_LandsWithHumanLag(ClassId x)
        {
            var t = new Tally();
            for (int k = 0; t.Swings < T.StrafeSwings && k < 16; k++)
            {
                Seat s = PlayerSeatRig.Duel(x, ClassId.Farmer, TargetMode.Strafer, PlayerSeatRig.AngleOf(k, 8), k, T.HumanLagTicks);
                s.KeepThemAbove = 0.3;
                s.Dummy.Dir = k % 2 == 0 ? 1 : -1;
                int need = T.StrafeSwings - t.Swings;
                t.Add(PlayerSeatRig.Play(s, 20, st => PlayerSeatRig.CountSwings(st) >= need));
            }
            bool ok = PxLog.Line("S03 Primary_StrafingTarget_LandsWithHumanLag", x.ToString(), "land " + PxLog.F(t.Land) + " (" + t.Landed + " of " + t.Swings + ")", ">= " + T.LandRateMin, t.Land >= T.LandRateMin && t.Swings >= T.StrafeSwings / 2);
            Assert.IsTrue(ok, x + " lag-6 on a strafer lands " + PxLog.F(t.Land) + " of " + t.Swings + " swings (target " + T.LandRateMin + ")");
        }

        // ------------------------------------------------------------------ S5

        [TestCase(ClassId.Warrior)]
        [TestCase(ClassId.Farmer)]
        [TestCase(ClassId.Miner)]
        [TestCase(ClassId.Demolitions)]
        [TestCase(ClassId.Builder)]
        public void S05_Duel_VsNormalBot_HumanLag(ClassId x)
        {
            var all = new Tally();
            var parts = new List<string>();
            bool ok = true;
            foreach (ClassId them in All)
            {
                bool mirror = x == ClassId.Warrior && them == ClassId.Warrior;
                Tally c = Cell(x, them, TargetMode.RealBot, mirror ? T.MirrorAngles : T.Angles, T.HumanLagTicks);
                if (!mirror) All6(all, c);
                parts.Add(them + " " + c.Wins + "/" + c.Reps);
                if (x == ClassId.Warrior)
                {
                    bool cellOk = mirror ? c.Share >= T.WarriorMirrorMin && c.Share <= T.WarriorMirrorMax : c.Wins >= T.WarriorWinsOf8Min;
                    ok &= PxLog.Line("S05 Duel_VsNormalBot_HumanLag", "Warrior vs " + them, c.ToString(), mirror ? "share in [" + T.WarriorMirrorMin + ", " + T.WarriorMirrorMax + "] of 16" : ">= " + T.WarriorWinsOf8Min + " of 8", cellOk);
                }
            }
            if (x != ClassId.Warrior) ok &= PxLog.Line("S05 Duel_VsNormalBot_HumanLag", x + " vs all six", all.Wins + "/" + all.Reps + " = " + PxLog.F(all.Share) + " (" + string.Join(", ", parts) + ")", ">= " + T.DuelWinShareMin, all.Share >= T.DuelWinShareMin);
            Assert.IsTrue(ok, x + " lag-6 human vs Normal bots: " + string.Join(", ", parts));
        }

        // ------------------------------------------------------------------ PLAN P3: the overall share

        [Test]
        public void S05b_Duel_AllMatchups_OverallShare()
        {
            var all = new Tally();
            var rows = new List<string>();
            foreach (ClassId x in All)
            {
                var row = new Tally();
                foreach (ClassId them in All) if (them != x) All6(row, Cell(x, them, TargetMode.RealBot, T.Angles, T.HumanLagTicks));
                All6(all, row);
                rows.Add(x + " " + row.Wins + "/" + row.Reps);
            }
            bool ok = PxLog.Line("S05b Duel_AllMatchups_OverallShare", "30 non-mirror pairs x 8 angles", all.Wins + "/" + all.Reps + " = " + PxLog.F(all.Share) + " (" + string.Join(", ", rows) + ")", ">= " + T.OverallDuelShareMin, all.Share >= T.OverallDuelShareMin);
            Assert.IsTrue(ok);
        }

        // ------------------------------------------------------------------ S6

        [TestCase(ClassId.Warrior)]
        [TestCase(ClassId.Farmer)]
        [TestCase(ClassId.Miner)]
        [TestCase(ClassId.Demolitions)]
        [TestCase(ClassId.Builder)]
        public void S06_Kill_ThroughOneHealer(ClassId x)
        {
            var failed = new List<string>();
            foreach (ClassId them in All)
            {
                int kills = 0, reps = 2;
                var secs = new List<double>();
                for (int k = 0; k < reps; k++)
                {
                    Seat s = PlayerSeatRig.Duel(x, them, TargetMode.Healed, PlayerSeatRig.AngleOf(k, reps), k, 0, halfMiss: true);
                    DuelResult r = PlayerSeatRig.Play(s, T.KillThroughHealerMaxSeconds);
                    if (r.Won) { kills++; secs.Add(r.Seconds); }
                }
                bool ok = PxLog.Line("S06 Kill_ThroughOneHealer", x + " (50%) on " + them + " + Healer bot", kills + "/" + reps + " killed" + (secs.Count > 0 ? ", median " + PxLog.F(PlayerSeatRig.Median(secs), "0.0") + " s" : ""), "all killed within " + T.KillThroughHealerMaxSeconds + " s", kills == reps);
                if (!ok) failed.Add(them.ToString());
            }
            Assert.IsEmpty(failed, x + " cannot kill through one Healer: " + string.Join(", ", failed));
        }

        // ------------------------------------------------------------------ S11

        [TestCase(ClassId.Farmer)]
        [TestCase(ClassId.Miner)]
        [TestCase(ClassId.Demolitions)]
        [TestCase(ClassId.Builder)]
        [TestCase(ClassId.Healer)]
        public void S11_Warrior_Chase_CatchesRunnerAt30Pct(ClassId runner)
        {
            // px S4 rig: the runner used to start 1.5 u away, inside the Warrior's reach, and standing in HoldHill (hold 0.7 +
            // inertia 0.25 beats the 0.9 retreat score, so it never fled: "retreated in 0"), so it was always caught. The test
            // is a FLEEING target (PLAN P3: "a chance of escape"): it now starts ChaseStartBeyondReach outside the Warrior's
            // reach, already running: in Retreat, moving away from the Warrior at its full speed. Band unchanged.
            int caught = 0, retreated = 0;
            GameConfig gc = PlayerSeatRig.Config();
            double gap = gc.Class(ClassId.Warrior).AttackRange + gc.Class(runner).Radius + T.ChaseStartBeyondReach;
            for (int k = 0; k < T.Angles; k++)
            {
                Seat s = PlayerSeatRig.Duel(ClassId.Warrior, runner, TargetMode.RealBot, PlayerSeatRig.AngleOf(k, T.Angles), k, T.HumanLagTicks, distance: gap);
                s.W.Actors[s.Them].Hp = T.ChaseRunnerHpShare * s.W.Actors[s.Them].MaxHp;
                bool ran = false, started = false;
                s.BeforeTick = q =>
                {
                    World qw = q.W;
                    if (!started)
                    {
                        started = true;
                        BotsState bs = BotBrain.GetBotsState(qw);
                        qw.Actors[q.Them].AiState = AiState.Retreat;
                        if (bs != null) bs.StateSince[q.Them] = qw.Match.Time;
                        double ux = qw.X[q.Them] - qw.X[q.Me], uz = qw.Z[q.Them] - qw.Z[q.Me], ud = Math.Sqrt(ux * ux + uz * uz);
                        double sp = qw.Config.Class(runner).MoveSpeed;
                        qw.Vx[q.Them] = ux / ud * sp;
                        qw.Vz[q.Them] = uz / ud * sp;
                    }
                    ran |= qw.Actors[q.Them].AiState == AiState.Retreat;
                };
                DuelResult r = PlayerSeatRig.Play(s, T.ChaseSeconds);
                if (r.Won) caught++;
                if (ran) retreated++;
            }
            double share = (double)caught / T.Angles;
            bool ok = PxLog.Line("S11 Warrior_Chase_CatchesRunnerAt30Pct", "lag-6 Warrior vs a running " + runner + " bot at 29%, " + PxLog.F(T.ChaseStartBeyondReach) + " u outside reach", caught + "/" + T.Angles + " caught within " + T.ChaseSeconds + " s (bot retreated in " + retreated + ")", "share in [" + T.ChaseCatchShareMin + ", " + T.ChaseCatchShareMax + "]", share >= T.ChaseCatchShareMin && share <= T.ChaseCatchShareMax);
            Assert.IsTrue(ok);
        }

        // ------------------------------------------------------------------ S35

        [TestCase(ClassId.Warrior)]
        [TestCase(ClassId.Farmer)]
        [TestCase(ClassId.Miner)]
        [TestCase(ClassId.Demolitions)]
        [TestCase(ClassId.Builder)]
        [TestCase(ClassId.Healer)]
        public void S35_HumanVsHuman_Symmetric(ClassId x)
        {
            Tally t = Cell(x, x, TargetMode.HumanVsHuman, T.MirrorAngles, T.HumanLagTicks);
            double a = (double)t.Wins / t.Reps, b = (double)t.Losses / t.Reps;
            bool ok = PxLog.Line("S35 HumanVsHuman_Symmetric", x + " vs " + x + " (both lag 6)", "seat A " + t.Wins + ", seat B " + t.Losses + ", draws " + t.Draws + " of " + t.Reps + "; land A " + PxLog.F(t.Land) + " B " + PxLog.F(t.TheirLand),
                "each seat <= " + T.HumanMirrorMaxShare + ", land each >= " + T.LandRateMin, a <= T.HumanMirrorMaxShare && b <= T.HumanMirrorMaxShare && t.Land >= T.LandRateMin && t.TheirLand >= T.LandRateMin);
            Assert.IsTrue(ok);
        }

        // ------------------------------------------------------------------ S41

        [Test]
        public void S41_Fountain_DoesNotOutHealPursuit()
        {
            var bad = new List<string>();
            foreach (ClassId them in All)
            {
                Seat s = PlayerSeatRig.Duel(ClassId.Warrior, them, TargetMode.Passive, 0, 0, T.HumanLagTicks);
                World w = s.W;
                PadDef pad = w.Map.Pads[(int)TeamId.Crimson];
                PlayerSeatRig.Place(w, s.Them, them, pad.X, pad.Z);
                PlayerSeatRig.Place(w, 0, ClassId.Warrior, pad.X - 3, pad.Z);
                int firstHit = -1;
                double hp0 = 0, hp1 = double.NaN;
                int window = s.Ticks(T.FountainWindowSeconds), limit = s.Ticks(15);
                for (int t = 0; t < limit; t++)
                {
                    int m = s.Spy.Mark;
                    s.Step();
                    if (firstHit < 0 && s.Spy.Hits(0, s.Them, m).Count > 0) { firstHit = w.Tick; hp0 = w.Actors[s.Them].Hp; }
                    if (!w.Actors[s.Them].Alive) { hp1 = 0; break; }
                    if (firstHit >= 0 && w.Tick - firstHit >= window) { hp1 = w.Actors[s.Them].Hp; break; }
                }
                double healed = s.Spy.Of(SimEventType.Heal, e => e.A == s.Them).Sum(e => e.Value);
                bool ok = PxLog.Line("S41 Fountain_DoesNotOutHealPursuit", "lag-6 Warrior on a " + them + " standing in its fountain", firstHit < 0 ? "never hit" : "net HP over " + T.FountainWindowSeconds + " s after the first hit: " + PxLog.F(hp1 - hp0, "0.0") + " (fountain healed " + PxLog.F(healed, "0") + ")", "< 0", firstHit >= 0 && hp1 - hp0 < 0);
                if (!ok) bad.Add(them.ToString());
            }
            Assert.IsEmpty(bad, "fountain out-heals a Warrior on: " + string.Join(", ", bad));
        }

        // ------------------------------------------------------------------ S42

        [TestCase(ClassId.Warrior)]
        [TestCase(ClassId.Farmer)]
        [TestCase(ClassId.Miner)]
        [TestCase(ClassId.Demolitions)]
        [TestCase(ClassId.Builder)]
        [TestCase(ClassId.Healer)]
        public void S42_BotFair_NormalBotLandRate_VsStrafingHuman(ClassId x)
        {
            var t = new Tally();
            int spaced = 0;
            for (int k = 0; t.TheirSwings < T.BotSwings && k < 16; k++)
            {
                Seat s = PlayerSeatRig.Duel(x, x, TargetMode.RealBot, PlayerSeatRig.AngleOf(k, 8), k, T.HumanLagTicks, strafe: true);
                // px S4: the strafer used to circle at 0.8 x its range, so deep inside the bot's reach that the hit arc covered
                // any aim error (bot land 0.80-0.95). A dodging player strafes at the edge of the bot's reach, steps out when he
                // sees the wind-up (6 ticks late) and comes back in to punish: HumanModel.DodgeSpacing. The band is unchanged.
                s.Human.DodgeSpacing = true;
                s.KeepThemAbove = 0.35;
                s.KeepMeAbove = 0.35;
                int before = t.TheirSwings;
                t.Add(PlayerSeatRig.Play(s, 20, st => st.Spy.Contacts(st.Them).Count + before >= T.BotSwings));
                spaced += s.Human.SpaceOutCount;
            }
            bool ok = PxLog.Line("S42 BotFair_NormalBotLandRate_VsStrafingHuman", "Normal " + x + " bot vs a lag-6 strafing " + x, "bot land " + PxLog.F(t.TheirLand) + " (" + t.TheirLanded + " of " + t.TheirSwings + "), human land " + PxLog.F(t.Land) + " (" + t.Landed + " of " + t.Swings + "), step-outs " + spaced, "in [" + T.BotLandMin + ", " + T.BotLandMax + "]", t.TheirSwings >= T.BotSwings / 2 && t.TheirLand >= T.BotLandMin && t.TheirLand <= T.BotLandMax);
            Assert.IsTrue(ok);
        }

        // ------------------------------------------------------------------ S43 / S44 / S45

        [Test]
        public void S43_Warrior_50PctPlayer_WinsEachMatchup()
        {
            var bad = new List<string>();
            foreach (ClassId them in All)
            {
                if (them == ClassId.Warrior) continue;
                Tally c = Cell(ClassId.Warrior, them, TargetMode.RealBot, T.Angles, T.HumanLagTicks, half: true);
                double med = c.Ttk.Count > 0 ? PlayerSeatRig.Median(c.Ttk) : double.PositiveInfinity;
                bool ok = PxLog.Line("S43 Warrior_50PctPlayer_WinsEachMatchup", "50% Warrior vs " + them, c.ToString(), ">= " + T.Warrior50PctWinsOf8Min + " of 8, median ttk <= " + T.WarriorTtkMaxSeconds + " s", c.Wins >= T.Warrior50PctWinsOf8Min && med <= T.WarriorTtkMaxSeconds);
                if (!ok) bad.Add(them.ToString());
            }
            Assert.IsEmpty(bad, "the 50% Warrior loses to: " + string.Join(", ", bad));
        }

        [Test]
        public void S44_Healer_LosesEachMatchup()
        {
            var bad = new List<string>();
            foreach (ClassId them in All)
            {
                if (them == ClassId.Healer) continue;
                Tally c = Cell(ClassId.Healer, them, TargetMode.RealBot, T.Angles, T.HumanLagTicks);
                if (!PxLog.Line("S44 Healer_LosesEachMatchup", "lag-6 Healer vs " + them, c.ToString(), "<= " + T.HealerWinsOf8Max + " of 8", c.Wins <= T.HealerWinsOf8Max)) bad.Add(them.ToString());
            }
            Assert.IsEmpty(bad, "the human Healer beats: " + string.Join(", ", bad));
        }

        [Test]
        public void S44_EveryClass_BeatsHealerBot()
        {
            var bad = new List<string>();
            foreach (ClassId x in All)
            {
                if (x == ClassId.Healer) continue;
                Tally c = Cell(x, ClassId.Healer, TargetMode.RealBot, T.Angles, T.HumanLagTicks);
                if (!PxLog.Line("S44 EveryClass_BeatsHealerBot", "lag-6 " + x + " vs Healer bot", c.ToString(), ">= " + T.BeatsHealerWinsOf8Min + " of 8", c.Wins >= T.BeatsHealerWinsOf8Min)) bad.Add(x.ToString());
            }
            Assert.IsEmpty(bad, "cannot beat the Healer bot: " + string.Join(", ", bad));
        }

        [Test]
        public void S45_MiddleClasses_Mixed()
        {
            ClassId[] mid = { ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder };
            var bad = new List<string>();
            foreach (ClassId x in mid)
            {
                var t = new Tally();
                var parts = new List<string>();
                foreach (ClassId them in mid)
                {
                    if (them == x) continue;
                    Tally c = Cell(x, them, TargetMode.RealBot, T.Angles, T.HumanLagTicks);
                    All6(t, c);
                    parts.Add(them + " " + c.Wins + "/" + c.Reps);
                }
                if (!PxLog.Line("S45 MiddleClasses_Mixed", "lag-6 " + x + " vs the other middle three", t.Wins + "/" + t.Reps + " = " + PxLog.F(t.Share) + " (" + string.Join(", ", parts) + ")", "in [" + T.MiddleShareMin + ", " + T.MiddleShareMax + "]", t.Share >= T.MiddleShareMin && t.Share <= T.MiddleShareMax)) bad.Add(x.ToString());
            }
            Assert.IsEmpty(bad, "middle classes out of band: " + string.Join(", ", bad));
        }

        static void All6(Tally a, Tally b)
        {
            a.Reps += b.Reps; a.Wins += b.Wins; a.Losses += b.Losses; a.Draws += b.Draws; a.Swings += b.Swings; a.Landed += b.Landed;
            a.TheirSwings += b.TheirSwings; a.TheirLanded += b.TheirLanded; a.Ttk.AddRange(b.Ttk);
        }

        // ------------------------------------------------------------------ S48: floor and ceiling (PLAN THE STANDARD rows 4, 5)

        static readonly Dictionary<string, (Tally all, List<string> rows)> Sweeps = new Dictionary<string, (Tally, List<string>)>();

        /// <summary>The 30 non-mirror match-ups x 8 angles for one human model against bots of one difficulty (cached per run).</summary>
        static (Tally all, List<string> rows) Sweep(bool strong, BotDifficulty bots)
        {
            string key = (strong ? "ceiling" : "floor") + "/" + bots;
            if (Sweeps.TryGetValue(key, out var hit)) return hit;
            var all = new Tally();
            var rows = new List<string>();
            var models = new List<HumanModel>();
            foreach (ClassId x in All)
            {
                var row = new Tally();
                foreach (ClassId them in All)
                {
                    if (them == x) continue;
                    Tally c = strong
                        ? Cell(x, them, TargetMode.RealBot, T.Angles, T.CeilingLagTicks, bots: bots, setup: (s, k) =>
                        {
                            s.Human.LagTicks = T.CeilingLagTicks + (k % 2);
                            s.Human.Feints = s.Human.HitCancels = s.Human.Dodges = true;
                            s.Human.KeepRange = x == ClassId.Farmer;
                            models.Add(s.Human);
                        })
                        : Cell(x, them, TargetMode.RealBot, T.Angles, T.FloorLagTicks, half: true, bots: bots, setup: (s, k) => s.Human.AimHelp = T.FloorAimHelp);
                    All6(row, c);
                }
                All6(all, row);
                rows.Add(x + " won " + row.Wins + "/" + row.Reps + " (" + PxLog.F(row.Share) + "), land " + PxLog.F(row.Land) + ", bot land " + PxLog.F(row.TheirLand));
                PxLog.Csv("s48-" + key.Replace('/', '-') + ".csv", "model,bots,class,wins,reps,share,land,bot_land", (strong ? "ceiling" : "floor") + "," + bots + "," + x + "," + row.Wins + "," + row.Reps + "," + PxLog.F(row.Share) + "," + PxLog.F(row.Land) + "," + PxLog.F(row.TheirLand));
            }
            if (strong) rows.Add("feints " + models.Sum(m => m.FeintCount) + ", dodges " + models.Sum(m => m.DodgeCount) + " over " + models.Count + " duels");
            var r = (all, rows);
            Sweeps[key] = r;
            return r;
        }

        [Test]
        public void S48a_Floor_FirstMatchPlayer()
        {
            var n = Sweep(false, BotDifficulty.Normal);
            var e = Sweep(false, BotDifficulty.Easy);
            bool a = PxLog.Line("S48a Floor_FirstMatchPlayer", "0.3 s reactions, every other swing off, no dodge/feint, aim help " + (T.FloorAimHelp >= 2 ? "High" : T.FloorAimHelp == 1 ? "Low" : "Off") + " vs Normal (30 pairs x 8)", "land " + PxLog.F(n.all.Land) + ", won " + n.all.Wins + "/" + n.all.Reps + " = " + PxLog.F(n.all.Share) + " [" + string.Join("; ", n.rows) + "]", "land >= " + PxLog.F(T.FloorLandMin) + ", win >= " + PxLog.F(T.FloorWinVsNormalMin), n.all.Land >= T.FloorLandMin && n.all.Share >= T.FloorWinVsNormalMin);
            bool b = PxLog.Line("S48a Floor_FirstMatchPlayer", "the same model vs Easy bots", "won " + e.all.Wins + "/" + e.all.Reps + " = " + PxLog.F(e.all.Share) + " [" + string.Join("; ", e.rows) + "]", ">= " + PxLog.F(T.FloorWinVsEasyMin), e.all.Share >= T.FloorWinVsEasyMin);
            Assert.IsTrue(a && b);
        }

        [Test]
        public void S48b_Ceiling_StrongPlayer()
        {
            var n = Sweep(true, BotDifficulty.Normal);
            var h = Sweep(true, BotDifficulty.Hard);
            bool a = PxLog.Line("S48b Ceiling_StrongPlayer", "0.15 s reactions, feints, hit-cancels, dodges read 6 ticks late, Farmer keeps range vs Normal", "won " + n.all.Wins + "/" + n.all.Reps + " = " + PxLog.F(n.all.Share) + ", land " + PxLog.F(n.all.Land) + " [" + string.Join("; ", n.rows) + "]", ">= " + PxLog.F(T.CeilingWinVsNormalMin), n.all.Share >= T.CeilingWinVsNormalMin);
            bool b = PxLog.Line("S48b Ceiling_StrongPlayer", "the same model vs Hard bots", "won " + h.all.Wins + "/" + h.all.Reps + " = " + PxLog.F(h.all.Share) + " [" + string.Join("; ", h.rows) + "]", ">= " + PxLog.F(T.CeilingWinVsHardMin), h.all.Share >= T.CeilingWinVsHardMin);
            Assert.IsTrue(a && b);
        }

        [Test]
        public void S48c_SkillGap_Pays()
        {
            double gap = Sweep(true, BotDifficulty.Normal).all.Share - Sweep(false, BotDifficulty.Normal).all.Share;
            Assert.IsTrue(PxLog.Line("S48c SkillGap_Pays", "strong minus first-match win share vs Normal", PxLog.F(gap), ">= +" + PxLog.F(T.SkillGapMin), gap >= T.SkillGapMin));
        }
    }
}
