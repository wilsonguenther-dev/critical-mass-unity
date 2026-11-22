using System;
using System.Collections.Generic;
using System.Linq;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;
using T = CriticalMass.Sim.Tests.PlayerSeat.PlayerSeatTargets;

namespace CriticalMass.Sim.Tests.PlayerSeat
{
    /// <summary>
    /// Player-seat gate, tier S, defence and movement verbs (lane DM, 2026-11-22; owner: "slide, glide, dodge, the timed dodge,
    /// the hits, the counters, the blocks, the shield: how does the player use it, and is it actually effective"; "climbing,
    /// going down stairs"). The numbers behind each bar: data/dm-defence/VERBS.txt. Category PlayerSeat (fast suite).
    /// </summary>
    [TestFixture, Category("PlayerSeat")]
    public sealed class PlayerSeatDefenceTests
    {
        static readonly ClassId[] All = T.Classes;
        public enum Dir { None, Away, Side, Through }

        sealed class Stat
        {
            public int Dodged, DodgedLanded, Reported, Undodged, UndodgedLanded, Reps, Wins;
            public double HpLeft;
            public double Avoided => Dodged > 0 ? 1.0 - (double)DodgedLanded / Dodged : double.NaN;
            public override string ToString() => "dodged " + Dodged + ", avoided " + (Dodged - DodgedLanded) + " (" + PxLog.F(Avoided) + "), DODGED reported " + Reported
                + "; undodged in reach " + Undodged + ", landed " + UndodgedLanded + (Reps > 0 ? "; won " + Wins + "/" + Reps + ", health left " + PxLog.F(HpLeft / Reps) : "");
        }

        /// <summary>
        /// Actor 0 (me) against the enemy seat (them): a Normal bot, or a lag-6 human seat with full swing tracking. Each time
        /// them starts a melee wind-up with me within its reach + 0.5, me presses Dodge (dir) or Ability (shield) react ticks
        /// later when it can. fight: me also fights as the lag-6 human model (a duel to the death); else me stands and both
        /// are kept alive. Counts per attacker swing that reached contact.
        /// </summary>
        static Stat Run(ClassId me, ClassId them, Dir dir, bool fight, bool humanAttacker, int reps, bool shield = false)
        {
            var st = new Stat();
            for (int k = 0; k < reps; k++)
            {
                Seat s = PlayerSeatRig.Duel(me, them, humanAttacker ? TargetMode.HumanVsHuman : TargetMode.RealBot, PlayerSeatRig.AngleOf(k, reps), k, 6);
                if (humanAttacker) { s.Human2.LagTicks = 6; s.Human2.HoldFireTicks = k % 4; }
                s.Human.Fight = fight;
                s.NoTnt = true;
                if (!fight) { s.KeepMeAbove = 0.3; s.KeepThemAbove = 0.3; }
                World w = s.W;
                int ia = s.Me, ib = s.Them, head = 0;
                var starts = new List<int>();
                var startDist = new Dictionary<int, double>();
                var answered = new HashSet<int>();
                s.BeforeTick = seat =>
                {
                    var all = seat.Spy.All;
                    for (; head < all.Count; head++)
                    {
                        SeatEvent e = all[head];
                        if (e.Type != SimEventType.AttackStart || e.A != ib || e.B != Combat.KindAttack) continue;
                        starts.Add(e.Tick - 1);
                        double dx = w.X[ib] - w.X[ia], dz = w.Z[ib] - w.Z[ia];
                        startDist[e.Tick - 1] = Math.Sqrt(dx * dx + dz * dz);
                    }
                    if (!fight) { seat.Human.MoveOverrideX = 0; seat.Human.MoveOverrideZ = 0; }
                    if ((dir == Dir.None && !shield) || starts.Count == 0) return;
                    int p = starts[starts.Count - 1];
                    if (w.Tick - p != T.DefenceReactTicks || !w.Actors[ia].Alive) return;
                    double rx = w.X[ib] - w.X[ia], rz = w.Z[ib] - w.Z[ia], rd = Math.Sqrt(rx * rx + rz * rz);
                    if (rd > w.Config.Class(them).AttackRange + w.Config.Class(me).Radius + 0.5 || rd < 1e-6) return;
                    if (shield)
                    {
                        if (w.Actors[ia].AbilityCd > 0) return;
                        seat.Human.PressAbility = true;
                        answered.Add(p);
                        return;
                    }
                    if (!Dodge.CanStart(w, ia)) return;
                    double ux = rx / rd, uz = rz / rd, sg = (p & 1) == 0 ? 1 : -1;
                    seat.Human.PressDodge = true;
                    seat.Human.MoveOverrideX = dir == Dir.Away ? -ux : dir == Dir.Through ? ux : -uz * sg;
                    seat.Human.MoveOverrideZ = dir == Dir.Away ? -uz : dir == Dir.Through ? uz : ux * sg;
                    answered.Add(p);
                };
                DuelResult r = PlayerSeatRig.Play(s, 30);
                if (fight)
                {
                    st.Reps++;
                    if (r.Won) st.Wins++;
                    st.HpLeft += w.Actors[ia].Alive ? w.Actors[ia].Hp / w.Actors[ia].MaxHp : 0;
                }
                var contacts = s.Spy.Of(SimEventType.AttackContact, e => e.A == ib);
                var hitsOnMe = s.Spy.Of(SimEventType.Hit, e => e.A == ia && e.B == ib && e.Cause == DamageCause.Melee);
                var dodgedEv = s.Spy.Of(SimEventType.HitBlocked, e => e.A == ia && e.B == ib && (int)e.Value == PlayerReasons.BlockedDodge);
                var shieldEv = s.Spy.Of(SimEventType.HitBlocked, e => e.A == ia && e.B == ib && (int)e.Value == PlayerReasons.BlockedShield);
                var dodgeTicks = new HashSet<int>(s.Spy.Of(SimEventType.Dodge, e => e.A == ia).Select(e => e.Tick - 1));
                if (!shield) answered.RemoveWhere(p => !dodgeTicks.Contains(p + T.DefenceReactTicks));
                int wu = w.Dur.AttackWindup[(int)them];
                double theirReach = w.Config.Class(them).AttackRange + w.Config.Class(me).Radius;
                foreach (int p in starts)
                {
                    SeatEvent c = contacts.FirstOrDefault(e => e.Tick - 1 >= p + wu - 1 && e.Tick - 1 <= p + wu + 1);
                    if (c == null) continue;
                    bool landed = hitsOnMe.Any(e => e.Tick == c.Tick);
                    bool reduced = shieldEv.Any(e => e.Tick == c.Tick);
                    if (answered.Contains(p))
                    {
                        st.Dodged++;
                        if (shield ? landed && !reduced : landed) st.DodgedLanded++;
                        if (shield ? reduced : dodgedEv.Any(e => e.Tick == c.Tick)) st.Reported++;
                    }
                    else if (startDist[p] <= theirReach)
                    {
                        st.Undodged++;
                        if (landed) st.UndodgedLanded++;
                    }
                }
            }
            return st;
        }

        // ------------------------------------------------------------------ S51 / S52: the reactive dodge works and says so

        [TestCase(ClassId.Warrior)]
        [TestCase(ClassId.Farmer)]
        [TestCase(ClassId.Miner)]
        [TestCase(ClassId.Demolitions)]
        [TestCase(ClassId.Builder)]
        [TestCase(ClassId.Healer)]
        public void S51_ReactiveDodge_AvoidsTheSwing(ClassId x)
        {
            // a lag-6 human attacker of class x with full swing tracking swings at a standing x; the defender side-dodges 0.2 s
            // after the wind-up starts. The dodge is in time when the press comes before the contact tick (the i-frames start on
            // the tick after the press): margin = wind-up - 1 - 6 ticks. A margin of 0 is a finding: no slack for a human.
            Stat st = Run(x, x, Dir.Side, false, true, T.Angles);
            int wu = PlayerSeatRig.Duel(x, x, TargetMode.Passive, 0, 0).W.Dur.AttackWindup[(int)x];
            int margin = wu - 1 - T.DefenceReactTicks;
            bool reactable = margin >= 0;
            bool ok = PxLog.Line("S51 ReactiveDodge_AvoidsTheSwing", "lag-6 human " + x + " swings, a standing " + x + " side-dodges at +" + T.DefenceReactTicks + " ticks (wind-up " + wu + " ticks, margin " + margin + ")", st.ToString(),
                reactable ? "avoided >= " + T.ReactiveDodgeAvoidMin + ", undodged swings land" : "wind-up shorter than a 0.2 s reaction (reported, not asserted)",
                !reactable || (st.Dodged >= 8 && st.Avoided >= T.ReactiveDodgeAvoidMin && st.UndodgedLanded >= 0.9 * st.Undodged));
            Assert.IsTrue(ok, x + ": " + st);
        }

        [TestCase(Dir.Away)]
        [TestCase(Dir.Side)]
        [TestCase(Dir.Through)]
        public void S52_SuccessfulDodge_RaisesDodgedEvent(Dir dir)
        {
            // every class against a Normal bot of its class: each avoided swing raises one HitBlocked(BlockedDodge) on its contact
            // tick, whether the dash left the reach (away), the arc (side) or neither (through, inside the i-frames).
            int avoided = 0, reported = 0;
            var rows = new List<string>();
            foreach (ClassId x in All)
            {
                Stat st = Run(x, x, dir, false, false, T.Angles);
                avoided += st.Dodged - st.DodgedLanded;
                reported += st.Reported;
                rows.Add(x + " " + st.Reported + "/" + (st.Dodged - st.DodgedLanded));
            }
            double share = avoided > 0 ? (double)reported / avoided : 0;
            bool ok = PxLog.Line("S52 SuccessfulDodge_RaisesDodgedEvent", "dodge " + dir + " at +6 vs a Normal bot of the same class, every class", "DODGED " + reported + " of " + avoided + " avoided swings (" + PxLog.F(share) + ") [" + string.Join("; ", rows) + "]", ">= " + T.DodgeReportedMin, avoided >= 20 && share >= T.DodgeReportedMin);
            Assert.IsTrue(ok);
        }

        // ------------------------------------------------------------------ S53 / S54: the dodge changes the fight

        [Test]
        public void S53_DodgingWell_WinsMoreDuels()
        {
            Stat never = new Stat(), well = new Stat();
            foreach (ClassId me in All)
                foreach (ClassId them in All)
                {
                    Stat a = Run(me, them, Dir.None, true, false, T.Angles), b = Run(me, them, Dir.Side, true, false, T.Angles);
                    never.Reps += a.Reps; never.Wins += a.Wins; never.HpLeft += a.HpLeft;
                    well.Reps += b.Reps; well.Wins += b.Wins; well.HpLeft += b.HpLeft; well.Dodged += b.Dodged; well.DodgedLanded += b.DodgedLanded;
                }
            double w0 = (double)never.Wins / never.Reps, w1 = (double)well.Wins / well.Reps, h0 = never.HpLeft / never.Reps, h1 = well.HpLeft / well.Reps;
            bool ok = PxLog.Line("S53 DodgingWell_WinsMoreDuels", "lag-6 human vs Normal bots, 36 pairs x 8: never dodges vs side-dodges each telegraphed swing at +6", "win " + PxLog.F(w0) + " -> " + PxLog.F(w1) + ", health left " + PxLog.F(h0) + " -> " + PxLog.F(h1) + " (" + well.Dodged + " dodges, avoided " + PxLog.F(well.Avoided) + ")", "win +" + T.DodgeWinGainMin + ", health +" + T.DodgeHpGainMin, w1 - w0 >= T.DodgeWinGainMin && h1 - h0 >= T.DodgeHpGainMin);
            Assert.IsTrue(ok);
        }

        /// <summary>One trial per dodge: me stands, them swings; me side-dodges at +6, walks back in (live position), aims where
        /// them was 6 ticks ago and swings as soon as in reach; success = my first landed hit within 40 ticks of their contact
        /// is a CriticalHit.</summary>
        static void Chain(ClassId me, ClassId them, bool humanAttacker, int reps, ref int trials, ref int crits, ref int punish)
        {
            for (int k = 0; k < reps; k++)
            {
                Seat s = PlayerSeatRig.Duel(me, them, humanAttacker ? TargetMode.HumanVsHuman : TargetMode.RealBot, PlayerSeatRig.AngleOf(k, reps), k, 6);
                if (humanAttacker) { s.Human2.LagTicks = 6; s.Human2.HoldFireTicks = k % 4; }
                s.Human.Fight = false;
                s.NoTnt = true;
                s.KeepMeAbove = 0.3; s.KeepThemAbove = 0.3;
                World w = s.W; int ia = s.Me, ib = s.Them, head = 0, lastStart = -1000, chaseUntil = -1;
                double[] hx = new double[16], hz = new double[16];
                var trialContact = new List<int>();
                s.BeforeTick = seat =>
                {
                    var all = seat.Spy.All;
                    for (; head < all.Count; head++)
                    {
                        SeatEvent e = all[head];
                        if (e.Type == SimEventType.AttackStart && e.A == ib && e.B == Combat.KindAttack) lastStart = e.Tick - 1;
                        if ((e.Type == SimEventType.AttackContact || e.Type == SimEventType.TntThrown) && e.A == ia) chaseUntil = -1;
                    }
                    hx[w.Tick & 15] = w.X[ib]; hz[w.Tick & 15] = w.Z[ib];
                    if (chaseUntil >= 0 && w.Tick > chaseUntil) chaseUntil = -1;
                    seat.Human.MoveOverrideX = 0; seat.Human.MoveOverrideZ = 0;
                    if (chaseUntil >= 0 && !Dodge.Active(w, ia))
                    {
                        double cx = w.X[ib] - w.X[ia], cz = w.Z[ib] - w.Z[ia], cd = Math.Sqrt(cx * cx + cz * cz);
                        if (cd > 0.8 * w.Config.Class(me).AttackRange && cd > 1e-6) { seat.Human.MoveOverrideX = cx / cd; seat.Human.MoveOverrideZ = cz / cd; }
                        seat.Human.AimOverrideX = hx[(w.Tick - 6) & 15]; seat.Human.AimOverrideZ = hz[(w.Tick - 6) & 15];
                        if (cd <= w.Config.Class(me).AttackRange + w.Config.Class(them).Radius && Combat.AttackReady(w, ia)) seat.Human.PressAttack = true;
                    }
                    if (w.Tick - lastStart != T.DefenceReactTicks || chaseUntil >= 0) return;
                    double rx = w.X[ib] - w.X[ia], rz = w.Z[ib] - w.Z[ia], rd = Math.Sqrt(rx * rx + rz * rz);
                    if (rd > w.Config.Class(them).AttackRange + w.Config.Class(me).Radius || rd < 1e-6) return;
                    if (!Dodge.CanStart(w, ia) || w.Actors[ia].AttackCd > 0) return;
                    double sg = (lastStart & 1) == 0 ? 1 : -1;
                    seat.Human.PressDodge = true;
                    seat.Human.MoveOverrideX = -rz / rd * sg;
                    seat.Human.MoveOverrideZ = rx / rd * sg;
                    chaseUntil = w.Tick + 45;
                    trialContact.Add(lastStart + w.Dur.AttackWindup[(int)them]);
                };
                PlayerSeatRig.Play(s, 30);
                var myHits = s.Spy.Of(SimEventType.Hit, e => e.A == ib && e.B == ia);
                var cr = s.Spy.Of(SimEventType.CriticalHit, e => e.A == ib && e.B == ia);
                foreach (int c in trialContact)
                {
                    trials++;
                    SeatEvent h = myHits.FirstOrDefault(e => e.Tick - 1 > c && e.Tick - 1 <= c + 40);
                    SeatEvent ce = h == null ? null : cr.FirstOrDefault(e => e.Tick == h.Tick);
                    if (ce == null) continue;
                    crits++;
                    if ((int)ce.Value == 2) punish++;
                }
            }
        }

        [Test]
        public void S54_DodgeThenPunish_LandsACritical()
        {
            // the timed-dodge reward (lead's call 14): dodge through the swing -> DODGED -> the attacker stays open to a PUNISH
            // critical for DodgePunishSeconds -> my counter-hit is a CriticalHit. 36 pairs x 8 against Normal bots.
            int trials = 0, crits = 0, punish = 0;
            foreach (ClassId me in All)
                foreach (ClassId them in All)
                    Chain(me, them, false, T.Angles, ref trials, ref crits, ref punish);
            double share = trials > 0 ? (double)crits / trials : 0;
            bool ok = PxLog.Line("S54 DodgeThenPunish_LandsACritical", "side-dodge at +6 vs a Normal bot, walk back in, swing (36 pairs x 8)", "critical " + crits + " of " + trials + " dodges (" + PxLog.F(share) + "), of them PUNISH " + punish + " (the rest COUNTER: he had started his next swing)", ">= " + T.DodgeChainCritMin, trials >= 200 && share >= T.DodgeChainCritMin && punish > 0);
            Assert.IsTrue(ok);
        }

        // ------------------------------------------------------------------ S55 / S56: never silent, PERFECT

        [Test]
        public void S55_DodgeRefused_OnCooldownAndInTheAir_SaysWhy()
        {
            Seat s = PlayerSeatRig.Field(ClassId.Warrior);
            World w = s.W;
            int mark = s.Spy.Mark;
            s.Human.PressDodge = true;
            s.Human.MoveOverrideX = 1; s.Human.MoveOverrideZ = 0;
            s.Step();
            int dodges = s.Spy.Since(mark, SimEventType.Dodge, e => e.A == 0).Count;
            s.Steps(12);
            int m2 = s.Spy.Mark;
            s.Human.PressDodge = true;
            s.Step();
            var cool = s.Spy.Since(m2, SimEventType.ActionRefused, e => e.A == 0 && e.B == PlayerReasons.ButtonDodge);
            s.Steps(T.PressAnswerTicks + 90);
            int m3 = s.Spy.Mark;
            s.Human.PressJump = true;
            s.Human.MoveOverrideX = 1; s.Human.MoveOverrideZ = 0;
            s.Step();
            s.Steps(3);
            bool air = w.Actors[0].AirLen > 0;
            s.Human.PressDodge = true;
            s.Step();
            var inAir = s.Spy.Since(m3, SimEventType.ActionRefused, e => e.A == 0 && e.B == PlayerReasons.ButtonDodge);
            bool ok = PxLog.Line("S55 DodgeRefused_OnCooldownAndInTheAir_SaysWhy", "Warrior: dodge, then Dodge again 0.4 s later; then a Dodge press mid-jump", "first dodge " + dodges + "; on cooldown: " + (cool.Count > 0 ? "ActionRefused value " + cool[0].Value : "silent") + "; airborne " + air + ": " + (inAir.Count > 0 ? "ActionRefused value " + inAir[0].Value : "silent"),
                "ActionRefused RefusedCooldown (" + PlayerReasons.RefusedCooldown + ") and RefusedInAir (" + PlayerReasons.RefusedInAir + ")",
                dodges == 1 && cool.Count == 1 && (int)cool[0].Value == PlayerReasons.RefusedCooldown && air && inAir.Count == 1 && (int)inAir[0].Value == PlayerReasons.RefusedInAir);
            Assert.IsTrue(ok);
        }

        [TestCase(ClassId.Warrior)]
        [TestCase(ClassId.Miner)]
        [TestCase(ClassId.Healer)]
        public void S56_LateDodge_IsPerfect_EarlyDodgeIsNot(ClassId x)
        {
            // a passive human x is swung at by a human Warrior from 1.2 u; it side-dodges `late` ticks before the contact tick.
            var results = new List<string>();
            bool ok = true;
            foreach (int before in new[] { 1, 2, 6 })
            {
                Seat s = PlayerSeatRig.Duel(ClassId.Warrior, x, TargetMode.Passive, 0, 0, distance: 1.2);
                World w = s.W;
                int wu = w.Dur.AttackWindup[(int)ClassId.Warrior];
                int mark = s.Spy.Mark;
                s.Human.Fight = false;
                s.Human.PressAttack = true;
                s.Human.AimOverrideX = w.X[s.Them]; s.Human.AimOverrideZ = w.Z[s.Them];
                for (int t = 0; t < wu + 3; t++)
                {
                    s.Human.MoveOverrideX = s.Human.MoveOverrideZ = 0;
                    if (t == wu - before)
                    {
                        s.Dummy.PressDodge = true;
                        s.Dummy.MoveX = 0; s.Dummy.MoveZ = 1;
                    }
                    s.Step();
                    s.Dummy.MoveX = s.Dummy.MoveZ = 0;
                }
                var dodged = s.Spy.Since(mark, SimEventType.HitBlocked, e => e.A == s.Them && e.B == 0 && (int)e.Value == PlayerReasons.BlockedDodge);
                int hits = s.Spy.Hits(0, s.Them, mark).Count;
                bool want = before <= w.Config.Depth.Dodge.PerfectDodgeTicks;
                bool good = hits == 0 && dodged.Count == 1 && (dodged[0].Tier == 1) == want && dodged[0].Radius > 0;
                ok &= good;
                results.Add(before + " ticks before contact: hits " + hits + ", DODGED " + dodged.Count + (dodged.Count > 0 ? " tier " + dodged[0].Tier + " avoided " + PxLog.F(dodged[0].Radius, "0.0") : ""));
            }
            ok = PxLog.Line("S56 LateDodge_IsPerfect_EarlyDodgeIsNot", x + " side-dodges a Warrior swing", string.Join("; ", results), "DODGED once each, Tier 1 (PERFECT) only within PerfectDodgeTicks, Radius = damage avoided", ok);
            Assert.IsTrue(ok);
        }

        // ------------------------------------------------------------------ S57: the Warrior's shield

        [Test]
        public void S57_Shield_CutsFrontalHits_SaysBlocked_AndPays()
        {
            Stat react = new Stat(), never = new Stat(), report = new Stat();
            foreach (ClassId them in All)
            {
                Stat a = Run(ClassId.Warrior, them, Dir.None, true, false, T.Angles), b = Run(ClassId.Warrior, them, Dir.None, true, false, T.Angles, shield: true);
                never.Reps += a.Reps; never.HpLeft += a.HpLeft; never.Wins += a.Wins;
                react.Reps += b.Reps; react.HpLeft += b.HpLeft; react.Wins += b.Wins;
                Stat c = Run(ClassId.Warrior, them, Dir.None, false, false, T.Angles, shield: true);
                report.Dodged += c.Dodged; report.DodgedLanded += c.DodgedLanded; report.Reported += c.Reported;
            }
            double h0 = never.HpLeft / never.Reps, h1 = react.HpLeft / react.Reps;
            bool ok = PxLog.Line("S57 Shield_CutsFrontalHits_SaysBlocked_AndPays", "Warrior raises the shield 0.2 s into a telegraphed swing (Ability), vs the six Normal bots x 8",
                "shielded swings " + report.Dodged + ", cut and reported BLOCKED " + report.Reported + "; duel health left " + PxLog.F(h0) + " -> " + PxLog.F(h1) + ", won " + never.Wins + " -> " + react.Wins + " of " + react.Reps,
                "every shielded frontal hit says BlockedShield; health left +" + T.ShieldHpGainMin, report.Dodged >= 20 && report.Reported >= 0.9 * report.Dodged && h1 - h0 >= T.ShieldHpGainMin);
            Assert.IsTrue(ok);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void S57b_ShieldCutHit_DoesNotStagger(bool rule)
        {
            // review defect 6: a frontal hit the raised shield cuts says BLOCKED and does not stagger the Warrior (switch off: it does)
            Seat s = PlayerSeatRig.Duel(ClassId.Miner, ClassId.Warrior, TargetMode.Passive, 0, 0, distance: 1.2);
            World w = s.W;
            w.Config.Depth.Seat.ShieldStopsStagger = rule;
            int me = s.Me, him = s.Them, wu = w.Dur.AttackWindup[(int)ClassId.Miner];
            w.Actors[him].Facing = Math.Atan2(w.Z[me] - w.Z[him], w.X[me] - w.X[him]);
            w.Actors[him].AbilityActive = 2.5;
            int mark = s.Spy.Mark, stun = 0;
            s.Human.Fight = false;
            s.Human.PressAttack = true;
            s.Human.AimOverrideX = w.X[him]; s.Human.AimOverrideZ = w.Z[him];
            for (int t = 0; t < wu + 4; t++)
            {
                s.Human.MoveOverrideX = s.Human.MoveOverrideZ = 0;
                s.Step();
                stun = Math.Max(stun, w.Actors[him].HitStunTicks);
            }
            int hits = s.Spy.Hits(me, him, mark).Count;
            int blocked = s.Spy.Since(mark, SimEventType.HitBlocked, e => e.A == him && e.B == me && (int)e.Value == PlayerReasons.BlockedShield).Count;
            bool ok = PxLog.Line("S57b ShieldCutHit_DoesNotStagger", "Miner hits a Warrior with his shield up, facing him; ShieldStopsStagger " + rule, "Hit " + hits + ", BLOCKED " + blocked + ", Warrior stagger ticks " + stun,
                rule ? "Hit 1, BLOCKED 1, no stagger" : "Hit 1, BLOCKED 1, staggered", hits == 1 && blocked == 1 && (rule ? stun == 0 : stun > 0));
            Assert.IsTrue(ok);
        }

        // ------------------------------------------------------------------ S61: the real game's match says IMMUNE (gate F13)

        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        public void S61_BootMatch_SpawnProtectedHit_SaysImmune(bool onItsPad, bool fightsBack)
        {
            // the world the game's Boot scene creates (SimDriverBehaviour.Awake: GameConfig.CreateDefault(), 16 a side, seed
            // 1337, Azure Warrior; a menu MatchRequest only changes match length, hatch, passive bots and the class mix), not
            // the gate rig's Full profile. Spawn protection is pad-bound (Combat: it ends beyond pad.R + 1 of the pad centre at
            // the start of every tick), so a protected enemy is only protected on its own pad: off it, the hit lands. And
            // swinging drops the swinger's own protection (Combat: "attacking drops protection"): a bot that fights back is
            // not protected when the hit lands. Gate F13's staging had both (target off its pad, a live bot), not a rules gap.
            GameConfig cfg = GameConfig.CreateDefault();
            if (!fightsBack) cfg.Bots.PassiveSeconds = 60;   // the menu's guided first match: bots leave the human alone
            World w = World.CreateWorld(new WorldOptions { Seed = cfg.Seed, TeamSize = cfg.TeamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            bool seatOn = CriticalMass.Sim.Core.PlayerSeat.On(w), feelOn = CombatFeel.On(w);
            int e = PlayerSeatRig.EnemySlot(w);
            for (int j = 1; j < w.ActorCount; j++) if (j != e) PlayerSeatRig.Remove(w, j);
            PadDef pad = w.Map.Pads[(int)TeamId.Crimson];
            double ex = onItsPad ? pad.X : w.Map.Hill.X, ez = onItsPad ? pad.Z : w.Map.Hill.Z;
            PlayerSeatRig.Place(w, e, ClassId.Farmer, ex, ez);
            PlayerSeatRig.Place(w, 0, ClassId.Warrior, ex - 1.2, ez);
            w.Actors[e].SpawnProtect = 3;
            var seat = new Seat { W = w, Me = 0, Spy = new EventSpy(w), Frames = new InputFrame[w.ActorCount] };
            seat.Human = new HumanModel(0, -1) { Fight = false };
            int mark = seat.Spy.Mark;
            seat.Human.PressAttack = true;
            for (int t = 0; t < w.Dur.AttackWindup[(int)ClassId.Warrior] + 4; t++)
            {
                seat.Human.AimOverrideX = w.X[e]; seat.Human.AimOverrideZ = w.Z[e];
                seat.Human.MoveOverrideX = seat.Human.MoveOverrideZ = 0;
                w.X[e] = ex; w.Z[e] = ez; w.Vx[e] = w.Vz[e] = 0;
                seat.Step();
            }
            int hits = seat.Spy.Hits(0, e, mark).Count;
            int immune = seat.Spy.Since(mark, SimEventType.HitBlocked, q => q.A == e && q.B == 0 && (int)q.Value == PlayerReasons.BlockedSpawnProtect).Count;
            bool ok = PxLog.Line("S61 BootMatch_SpawnProtectedHit_SaysImmune", "the Boot match's config (16 a side, defaults): Warrior swings at a spawn-protected Farmer " + (onItsPad ? "on its pad" : "off its pad (at the hill)") + (fightsBack ? ", the bot swings back" : ", passive bot"),
                "PlayerSeat.On " + seatOn + ", CombatFeel.On " + feelOn + "; Hit " + hits + ", HitBlocked IMMUNE " + immune,
                onItsPad && !fightsBack ? "the rules on, no hit, one IMMUNE" : "the rules on, protection gone: the hit lands, no IMMUNE",
                seatOn && feelOn && (onItsPad && !fightsBack ? hits == 0 && immune == 1 : hits == 1 && immune == 0));
            Assert.IsTrue(ok);
        }

        // ------------------------------------------------------------------ S58: movement never sticks or shivers

        /// <summary>Holds one move direction from (x0, z0) toward (x1, z1); false = not reached, stuck (under 20% of walk speed
        /// for more than StuckTicksMax with input held, end more than 0.8 u away) or a body direction reversal.</summary>
        static bool Walk(Seat s, double x0, double z0, double x1, double z1, out string what)
        {
            World w = s.W; int me = s.Me;
            double dx = x1 - x0, dz = z1 - z0, len = Math.Sqrt(dx * dx + dz * dz), ux = dx / len, uz = dz / len, sx = x0, sz = z0;
            for (int k = 0; k < 20 && !Collide.FootprintClear(w, me, sx, sz); k++) { sx += ux * 0.25; sz += uz * 0.25; }
            PlayerSeatRig.Place(w, me, w.Actors[me].Cls, sx, sz);
            double walk = w.Config.Class(w.Actors[me].Cls).MoveSpeed;
            int max = (int)Math.Ceiling((len / walk * 2.5 + 2) * w.Config.SimHz), slow = 0, worst = 0, rev = 0;
            double px = w.X[me], pz = w.Z[me], pdx = 0, pdz = 0;
            bool reached = false;
            for (int t = 0; t < max && w.Actors[me].Alive; t++)
            {
                s.Human.MoveOverrideX = ux; s.Human.MoveOverrideZ = uz;
                s.Human.AimOverrideX = w.X[me] + ux * 3; s.Human.AimOverrideZ = w.Z[me] + uz * 3;
                s.Step();
                double mx = w.X[me] - px, mz = w.Z[me] - pz, sp = Math.Sqrt(mx * mx + mz * mz) * w.Config.SimHz;
                if ((x1 - w.X[me]) * ux + (z1 - w.Z[me]) * uz < 0.8) { reached = true; break; }
                slow = sp < 0.2 * walk ? slow + 1 : 0;
                if (slow > worst) worst = slow;
                if (pdx * mx + pdz * mz < -1e-6 && pdx * pdx + pdz * pdz > 1e-8 && mx * mx + mz * mz > 1e-8) rev++;
                pdx = mx; pdz = mz; px = w.X[me]; pz = w.Z[me];
            }
            what = (reached ? "reached" : "NOT reached at " + PxLog.F(w.X[me], "0.0") + "," + PxLog.F(w.Z[me], "0.0")) + ", slow run " + worst + ", reversals " + rev;
            return reached && worst <= T.StuckTicksMax && rev == 0 && w.Actors[me].Alive;
        }

        static Seat Strip(ClassId c)
        {
            // BuilderTests.Strip's void gap (x 9..12) and one-cell wall (x 21), the floor east of it widened to x 40
            Seat s = PlayerSeatRig.Field(c, x: 7.5, z: 13.5);
            World w = s.W;
            MapDef map = w.Map;
            Array.Fill(map.Cells, Cell.Void);
            Array.Clear(map.WallHp, 0, map.WallHp.Length);
            void Paint(int x0, int z0, int x1, int z1, byte t) { for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) map.Cells[z * map.Width + x] = t; }
            Paint(2, 10, 8, 16, Cell.Floor);
            Paint(13, 8, 20, 18, Cell.Floor);
            Paint(21, 8, 21, 18, Cell.Wall);
            Paint(22, 8, 40, 18, Cell.Floor);
            for (int t = 0; t < 2; t++) { map.Pads[t].X = 5.5; map.Pads[t].Z = 13.5; }
            map.Hill.X = 60.5; map.Hill.Z = 60.5; map.Hill.R = 3;
            map.Shrine.X = 100.5; map.Shrine.Z = 60.5;
            foreach (ResourceNode nd in w.Nodes) { nd.X = -100; nd.Z = -100; }
            w.GridVersion++;
            return s;
        }

        [TestCase(ClassId.Warrior)]
        [TestCase(ClassId.Farmer)]
        [TestCase(ClassId.Miner)]
        [TestCase(ClassId.Demolitions)]
        [TestCase(ClassId.Builder)]
        [TestCase(ClassId.Healer)]
        public void S58_Movement_NeverSticksOrShivers(ClassId c)
        {
            // the real map has no stairs or slopes in the sim (its hills are view height only, MapDef.Height01): its walkable
            // height changes are the 9 bridges (16 with mirrors). The built ones: a Builder rope bridge and ladder, and terrain
            // height bands (stacked blocks +1 / +2, dug pits -1 / -2). Each walked both ways; bridges centred and 1 u off centre.
            var bad = new List<string>();
            int n = 0;
            var bridges = CriticalMass.Sim.Maps.SkybreakBasinMap.Bridges;
            for (int b = 0; b < bridges.Count; b++)
                for (int m = 0; m < (bridges[b].Mirror ? 2 : 1); m++)
                    for (int back = 0; back < 2; back++)
                        for (int off = -1; off <= 1; off++)
                        {
                            double ax = bridges[b].X0, az = bridges[b].Z0, bx = bridges[b].X1, bz = bridges[b].Z1;
                            if (m == 1) { ax = 112 - ax; bx = 112 - bx; }
                            if (back == 1) { double tx = ax, tz = az; ax = bx; az = bz; bx = tx; bz = tz; }
                            double lx = bx - ax, lz = bz - az, ll = Math.Sqrt(lx * lx + lz * lz);
                            ax += -lz / ll * off; az += lx / ll * off; bx += -lz / ll * off; bz += lx / ll * off;
                            n++;
                            if (!Walk(PlayerSeatRig.Field(c), ax, az, bx, bz, out string what)) bad.Add("bridge B" + (b + 1) + (m == 1 ? "m" : "") + (back == 1 ? " back" : "") + " off " + off + ": " + what);
                        }
            foreach (string piece in new[] { "bridge", "ladder" })
                for (int back = 0; back < 2; back++)
                {
                    Seat s = Strip(ClassId.Builder);
                    World w = s.W;
                    PlayerSeatRig.Place(w, 0, ClassId.Builder, piece == "bridge" ? 7.5 : 19.5, 13.5);
                    s.Human.AimOverrideX = piece == "bridge" ? 20 : 30; s.Human.AimOverrideZ = 13.5;
                    s.Human.PressAbility = true;
                    s.Step();
                    bool placed = s.Spy.Of(SimEventType.PiecePlaced, e => e.A == 0).Count > 0;
                    s.Steps(40);
                    PlayerSeatRig.Place(w, 0, c, 7.5, 13.5);
                    double ax = piece == "bridge" ? 5.5 : 17.5, bx = piece == "bridge" ? 16.5 : 25.5;
                    if (back == 1) { double t = ax; ax = bx; bx = t; }
                    n++;
                    string what = "not placed";
                    if (!placed || !Walk(s, ax, 13.5, bx, 13.5, out what)) bad.Add("built " + piece + (back == 1 ? " back" : "") + ": " + what);
                }
            foreach (int lift in new[] { 1, 2, -1, -2 })
                for (int back = 0; back < 2; back++)
                    for (int diag = 0; diag < 2; diag++)
                    {
                        Seat s = Strip(c);
                        MapDef map = s.W.Map;
                        for (int zz = 8; zz <= 18; zz++) for (int xx = 28; xx <= 30; xx++) map.Lift[zz * map.Width + xx] = (sbyte)lift;
                        s.W.GridVersion++;
                        double ax = 24.5, bx = 35.5, az = diag == 1 ? 10.5 : 13.5, bz = diag == 1 ? 16.5 : 13.5;
                        if (back == 1) { double t = ax; ax = bx; bx = t; t = az; az = bz; bz = t; }
                        n++;
                        if (!Walk(s, ax, az, bx, bz, out string what)) bad.Add("height " + lift + (back == 1 ? " back" : "") + (diag == 1 ? " diagonal" : "") + ": " + what);
                    }
            bool ok = PxLog.Line("S58 Movement_NeverSticksOrShivers", c + ": " + n + " walks (map bridges x 3 lines x 2 ways, built bridge and ladder, height bands +-1 / +-2)", bad.Count == 0 ? "all clean" : string.Join("; ", bad), "never under 20% speed > 0.5 s, no reversal", bad.Count == 0);
            Assert.IsTrue(ok, string.Join("\n", bad));
        }

        // ------------------------------------------------------------------ S59: Demolitions pry bar at a wall (review defect 9)

        [Test]
        public void S59_Demolitions_PointBlankWall_TakesThePryBar()
        {
            Seat s = PlayerSeatRig.Field(ClassId.Demolitions);
            World w = s.W;
            double x = w.X[0], z = w.Z[0];
            int cx = (int)Math.Floor(x + 1.3), cz = (int)Math.Floor(z);
            // a Crimson Miner's barricade, staged as PlayerSeatWorldTests.PlaceEnemyBarricade does
            MinerBarricadeConfig mb = w.Config.Abilities.MinerBarricade;
            int slot = -1;
            for (int sl = 0; sl < w.Barricades.Length && slot < 0; sl++)
            {
                Barricade b = w.Barricades[sl];
                if (b.Active) continue;
                b.Active = true; b.Owner = PlayerSeatRig.EnemySlot(w) + 3; b.Team = TeamId.Crimson; b.Cx = cx; b.Cz = cz;
                b.Hp = mb.MaxHp; b.Ttl = mb.LifetimeSeconds; b.PrevCell = w.Map.Cells[cz * w.Map.Width + cx];
                MapGrid.SetCell(w, cx, cz, Cell.Barricade);
                slot = sl;
            }
            Assert.IsTrue(slot >= 0, "staging: no barricade slot");
            PlayerSeatRig.Place(w, 0, ClassId.Demolitions, cx + 0.5 - 1.3, cz + 0.5);
            w.Demolitions.TntReadyTick[0] = 0;
            int mark = s.Spy.Mark;
            s.Human.AimOverrideX = cx + 0.5; s.Human.AimOverrideZ = cz + 0.5;
            s.Human.PressAttack = true;
            s.Steps(w.Dur.AttackWindup[(int)ClassId.Demolitions] + 3);
            var hit = s.Spy.Since(mark, SimEventType.StructureHit, e => e.A == 0);
            int thrown = s.Spy.Since(mark, SimEventType.TntThrown, e => e.A == 0).Count;
            bool ok = PxLog.Line("S59 Demolitions_PointBlankWall_TakesThePryBar", "TNT ready, attack at an enemy barricade 1.3 u ahead", "StructureHit " + hit.Count + (hit.Count > 0 ? " value " + PxLog.F(hit[0].Value, "0.0") : "") + ", TNT thrown " + thrown + ", TNT still ready " + (w.Tick >= w.Demolitions.TntReadyTick[0]), "pry bar: one StructureHit, no throw", hit.Count == 1 && thrown == 0);
            Assert.IsTrue(ok);
        }

        // ------------------------------------------------------------------ S60: the same-tick exchange (review defect 7)

        [TestCase(ClassId.Farmer)]
        [TestCase(ClassId.Miner)]
        [TestCase(ClassId.Demolitions)]
        [TestCase(ClassId.Builder)]
        [TestCase(ClassId.Healer)]
        public void S60_SameTickExchange_WarriorStillOutDamages(ClassId x)
        {
            // both press on the same tick 1.1 u apart: x lands first (shorter wind-up) and it is a COUNTER critical (he is in his
            // wind-up), but the Warrior's armoured wind-up holds and his hit lands for more (Travis: the Warrior wins every 1v1).
            Seat s = PlayerSeatRig.Duel(ClassId.Warrior, x, TargetMode.HumanVsHuman, 0, 0, 0, distance: 1.1);
            World w = s.W;
            s.Human.Fight = false; s.Human2.Fight = false; s.NoTnt = true;
            int mark = s.Spy.Mark;
            s.Human.PressAttack = true; s.Human2.PressAttack = true;
            for (int t = 0; t < 30; t++)
            {
                if (w.Demolitions != null && w.Demolitions.TntReadyTick != null) w.Demolitions.TntReadyTick[s.Them] = int.MaxValue / 2;
                s.Human.MoveOverrideX = s.Human.MoveOverrideZ = 0; s.Human2.MoveOverrideX = s.Human2.MoveOverrideZ = 0;
                s.Step();
            }
            var mine = s.Spy.Since(mark, SimEventType.Hit, e => e.B == s.Me && e.A == s.Them);
            var theirs = s.Spy.Since(mark, SimEventType.Hit, e => e.B == s.Them && e.A == s.Me);
            var crit = s.Spy.Since(mark, SimEventType.CriticalHit, e => e.B == s.Them && e.A == s.Me);
            double dm = mine.Count > 0 ? mine[0].Value : 0, dt = theirs.Count > 0 ? theirs[0].Value : 0;
            bool ok = PxLog.Line("S60 SameTickExchange_WarriorStillOutDamages", "Warrior and " + x + " press on the same tick, 1.1 u apart", "Warrior's hit " + PxLog.F(dm, "0.0") + (mine.Count > 0 ? " at t " + mine[0].Tick : "") + ", " + x + "'s hit " + PxLog.F(dt, "0.0") + (theirs.Count > 0 ? " at t " + theirs[0].Tick : "") + (crit.Count > 0 ? " (critical kind " + crit[0].Value + ")" : ""), "the Warrior's swing lands and deals more", mine.Count == 1 && dm > dt);
            Assert.IsTrue(ok);
        }
    }
}
