using System;
using System.Collections.Generic;
using System.Linq;
using CriticalMass.Sim.Config;
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

        static Tally Cell(ClassId me, ClassId them, TargetMode mode, int angles, int lag, bool half = false, bool strafe = false, BotDifficulty bots = BotDifficulty.Normal)
        {
            var t = new Tally();
            for (int k = 0; k < angles; k++)
            {
                Seat s = PlayerSeatRig.Duel(me, them, mode, PlayerSeatRig.AngleOf(k, angles), k, lag, half, strafe, bots);
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

        static void All6(Tally a, Tally b)
        {
            a.Reps += b.Reps; a.Wins += b.Wins; a.Losses += b.Losses; a.Draws += b.Draws; a.Swings += b.Swings; a.Landed += b.Landed;
        }
    }
}
