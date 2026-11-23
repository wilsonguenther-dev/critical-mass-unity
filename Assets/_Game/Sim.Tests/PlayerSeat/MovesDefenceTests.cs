// Class moves build (2026-11-22), LANE 2 (Defence) tests: T1-2 lunge, T1-3 dodge refund, T1-4a feint bait (sim side), T2-1 guard
// and parry, T2-7 jump evade (the hit test), T2-8 second wind. Every number is the HUMAN seat (actor 0) through
// MatchTick.AdvanceHumans (PlayerSeatRig), lag 6 ticks (0.2 s), the shipped aim help Low, against Normal bots unless a line says
// otherwise. Assertions are the acceptance numbers of cm-incidents/class-moves-audit-2026-11-22/SPEC.md; every measured value is
// printed as a "[px] mv2-..." line, with the switch off beside it where the spec compares.
using System;
using System.Collections.Generic;
using System.Linq;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using T = CriticalMass.Sim.Tests.PlayerSeat.PlayerSeatTargets;

namespace CriticalMass.Sim.Tests.PlayerSeat
{
    /// <summary>Lane 2's duel / chase / reaction tally.</summary>
    public sealed class MvDefenceTally
    {
        public int Reps, Wins, Losses, Escaped, Answered, AnsweredLanded, Unanswered, UnansweredLanded, Uses;
        public double Taken, Saved;
        public double Share => Reps > 0 ? (double)Wins / Reps : 0;
        public void Add(MvDefenceTally o)
        {
            Reps += o.Reps; Wins += o.Wins; Losses += o.Losses; Escaped += o.Escaped; Answered += o.Answered; AnsweredLanded += o.AnsweredLanded;
            Unanswered += o.Unanswered; UnansweredLanded += o.UnansweredLanded; Uses += o.Uses; Taken += o.Taken; Saved += o.Saved;
        }
    }

    /// <summary>Watches one seat's melee wind-ups (press tick, distance at the press), as the audit probe's MvWatch.</summary>
    public sealed class MvDefenceWatch
    {
        int _head;
        public int Who;
        public int Start = -100000;
        public double StartDist = 999;
        public MvDefenceWatch(int who) { Who = who; }
        public int Since(World w) => w.Tick - Start;
        public void Update(Seat s)
        {
            var all = s.Spy.All;
            for (; _head < all.Count; _head++)
            {
                SeatEvent e = all[_head];
                if (e.Type == SimEventType.AttackStart && e.A == Who && e.B == Combat.KindAttack)
                {
                    Start = e.Tick - 1;
                    StartDist = Dist(s.W, s.Me, s.Them);
                }
            }
        }
        public static double Dist(World w, int a, int b) { double dx = w.X[b] - w.X[a], dz = w.Z[b] - w.Z[a]; return Math.Sqrt(dx * dx + dz * dz); }
    }

    public static class MvDefenceRig
    {
        public static readonly ClassId[] All = T.Classes;
        public const int Lag = 6;
        public const int Help = 1;   // the shipped mouse aim help (Low)

        /// <summary>Every lane 2 switch on (lunge, refund, feint bait, guard, jump evade, second wind); the rest stay off.</summary>
        public static void Lane2(GameConfig c)
        {
            MovesConfig m = c.Depth.Moves;
            m.DodgeLunge = m.DodgeAvoidRefund = m.FeintBait = m.Guard = m.JumpEvade = m.SecondWind = true;
        }

        public static Action<GameConfig> With(Action<MovesConfig> set) => c => set(c.Depth.Moves);

        /// <summary>
        /// One duel cell: human me vs a Normal bot them, angles starts, 30 s. variant: base (walk in and swing), dodge (plus a side
        /// dodge on each read wind-up in reach), guard (dodge, and a held guard on each read wind-up while the dodge cools down),
        /// strong (the S48 strong model: feints, hit-cancels, dodges). startHp: the human's starting share of health.
        /// </summary>
        public static MvDefenceTally DuelCell(ClassId me, ClassId them, string variant, Action<GameConfig> tweak, int angles = 8, double startHp = 1, BotDifficulty bots = BotDifficulty.Normal, Action<Seat> onTick = null, Action<Seat, DuelResult> onEnd = null)
        {
            var t = new MvDefenceTally();
            for (int k = 0; k < angles; k++)
            {
                Seat s = PlayerSeatRig.Duel(me, them, TargetMode.RealBot, PlayerSeatRig.AngleOf(k, angles), k, Lag, bots: bots, tweak: tweak);
                s.Human.AimHelp = Help;
                World w = s.W;
                if (startHp < 1) w.Actors[s.Me].Hp = startHp * w.Actors[s.Me].MaxHp;
                bool strong = variant == "strong";
                if (strong) { s.Human.LagTicks = 5; s.Human.Feints = s.Human.HitCancels = true; if (me == ClassId.Farmer) s.Human.KeepRange = true; }
                if (variant == "dodge" || variant == "guard" || strong) s.Human.Dodges = true;
                var wt = new MvDefenceWatch(s.Them);
                int guardUntil = -1, uses = 0;
                s.BeforeTick = q =>
                {
                    wt.Update(q);
                    onTick?.Invoke(q);
                    if (variant != "guard") return;
                    int wu = w.Dur.AttackWindup[(int)them];
                    double reach = w.Config.Class(them).AttackRange + w.Config.Class(me).Radius;
                    if (wt.Since(w) == Lag && wt.StartDist <= reach + 0.5 && !Dodge.CanStart(w, q.Me)) { guardUntil = wt.Start + wu + 2; uses++; }
                    q.Human.Frame.Guard = w.Tick < guardUntil;
                };
                DuelResult r = PlayerSeatRig.Play(s, 30);
                onEnd?.Invoke(s, r);
                t.Reps++;
                if (r.Won) t.Wins++;
                if (r.Lost) t.Losses++;
                t.Taken += r.Taken;
                t.Uses += uses;
                t.Saved += s.Spy.Of(SimEventType.HitBlocked, e => e.A == s.Me && (int)e.Value == PlayerReasons.BlockedGuard).Sum(e => e.Radius);
            }
            return t;
        }

        /// <summary>40 duels for me: the five other classes x 8 starts.</summary>
        public static MvDefenceTally DuelRow(ClassId me, string variant, Action<GameConfig> tweak, double startHp = 1, Dictionary<ClassId, int> perCell = null, int angles = 8)
        {
            var row = new MvDefenceTally();
            foreach (ClassId them in All)
            {
                if (them == me) continue;
                MvDefenceTally c = DuelCell(me, them, variant, tweak, angles, startHp);
                if (perCell != null) perCell[them] = c.Wins;
                row.Add(c);
            }
            return row;
        }

        /// <summary>me chases a 29% Normal bot that is already running (S11 staging), catch within 4 s. variant plain / dodgein.</summary>
        public static MvDefenceTally Chase(ClassId me, ClassId runner, string variant, Action<GameConfig> tweak, int angles = 8)
        {
            var t = new MvDefenceTally();
            GameConfig gc = PlayerSeatRig.Config();
            double gap = gc.Class(me).AttackRange + gc.Class(runner).Radius + T.ChaseStartBeyondReach;
            for (int k = 0; k < angles; k++)
            {
                Seat s = PlayerSeatRig.Duel(me, runner, TargetMode.RealBot, PlayerSeatRig.AngleOf(k, angles), k, Lag, distance: gap, tweak: tweak);
                s.Human.AimHelp = Help;
                s.W.Actors[s.Them].Hp = T.ChaseRunnerHpShare * s.W.Actors[s.Them].MaxHp;
                bool started = false;
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
                    double rd = MvDefenceWatch.Dist(qw, q.Me, q.Them);
                    double reach = qw.Config.Class(me).AttackRange + qw.Config.Class(runner).Radius;
                    if (variant == "dodgein" && rd > reach && rd < reach + 3.2 && Dodge.CanStart(qw, q.Me))
                    {
                        q.Human.PressDodge = true;
                        q.Human.MoveOverrideX = (qw.X[q.Them] - qw.X[q.Me]) / rd;
                        q.Human.MoveOverrideZ = (qw.Z[q.Them] - qw.Z[q.Me]) / rd;
                    }
                };
                DuelResult r = PlayerSeatRig.Play(s, T.ChaseSeconds);
                t.Reps++;
                if (r.Won) t.Wins++;
            }
            return t;
        }

        public static MvDefenceTally ChaseRow(ClassId me, string variant, Action<GameConfig> tweak)
        {
            var all = new MvDefenceTally();
            foreach (ClassId runner in All) if (runner != me) all.Add(Chase(me, runner, variant, tweak));
            return all;
        }

        /// <summary>
        /// me at 30% runs down the open lane from a lag-6 HUMAN chaser (aim help Low) for 6 s, 16 starts, and dodges once on each
        /// read wind-up (the audit's "dodge" escape). lunging: the chaser dodges in when the gap is between its reach and reach + 3.2.
        /// </summary>
        public static MvDefenceTally Escape(ClassId me, ClassId chaser, bool lunging, Action<GameConfig> tweak, int angles = 16, bool spreadLag = false)
        {
            var t = new MvDefenceTally();
            GameConfig gc = PlayerSeatRig.Config();
            double gap = gc.Class(chaser).AttackRange + gc.Class(me).Radius + 0.5;
            for (int k = 0; k < angles; k++)
            {
                Seat s = PlayerSeatRig.Duel(me, chaser, TargetMode.HumanVsHuman, k % 2 == 0 ? 0 : Math.PI, k, Lag, distance: gap, tweak: tweak);
                s.Human2.LagTicks = 5 + k % 3;
                s.Human2.HoldFireTicks = k % 4;
                s.Human.Fight = false;
                s.Human2.AimHelp = Help;
                s.Human2.ApproachShare = 0.5;
                World w = s.W;
                w.Actors[s.Me].Hp = 0.3 * w.Actors[s.Me].MaxHp;
                var wt = new MvDefenceWatch(s.Them);
                s.BeforeTick = q =>
                {
                    wt.Update(q);
                    double rd = MvDefenceWatch.Dist(w, q.Me, q.Them);
                    double lx = k % 2 == 0 ? -1 : 1, lz = Math.Max(-0.6, Math.Min(0.6, (w.Map.Hill.Z - w.Z[q.Me]) * 0.8));
                    double ll = Math.Sqrt(lx * lx + lz * lz);
                    q.Human.MoveOverrideX = lx / ll; q.Human.MoveOverrideZ = lz / ll;
                    int lag = spreadLag ? 5 + k % 4 : Lag;   // spreadLag: the runner's eye varies 5 to 8 ticks per start, as the chaser's does
                    if (wt.Since(w) == lag && Dodge.CanStart(w, q.Me)) q.Human.PressDodge = true;
                    double creach = w.Config.Class(chaser).AttackRange + w.Config.Class(me).Radius;
                    if (lunging && rd > creach && rd < creach + 3.2 && Dodge.CanStart(w, q.Them) && rd > 1e-6)
                    {
                        q.Human2.PressDodge = true;
                        q.Human2.MoveOverrideX = (w.X[q.Me] - w.X[q.Them]) / rd;
                        q.Human2.MoveOverrideZ = (w.Z[q.Me] - w.Z[q.Them]) / rd;
                    }
                };
                DuelResult r = PlayerSeatRig.Play(s, 6);
                t.Reps++;
                if (!r.Lost) t.Escaped++;
                t.Taken += r.Taken;
                t.Uses += s.Spy.Of(SimEventType.Lunge, e => e.A == s.Them).Count;
                t.Answered += r.TheirSwings;
                t.AnsweredLanded += r.TheirLanded;
            }
            return t;
        }

        /// <summary>
        /// The audit's reactive defence (REACT): the human stands (no attack), a Normal bot of class them fights it for 30 s (both
        /// kept above 30% health). action: side (a side dodge on each read wind-up in reach, lag 6), guard (hold the guard from the
        /// read to just after contact), sideguard (side dodge when it is ready, else the guard). Answered = a read wind-up in reach
        /// that the action answered; unanswered = a wind-up in reach the action could not answer (the dodge cooling down).
        /// </summary>
        public static MvDefenceTally React(ClassId me, ClassId them, string action, Action<GameConfig> tweak, int angles = 8)
        {
            var t = new MvDefenceTally();
            for (int k = 0; k < angles; k++)
            {
                Seat s = PlayerSeatRig.Duel(me, them, TargetMode.RealBot, PlayerSeatRig.AngleOf(k, angles), k, Lag, tweak: tweak);
                s.Human.Fight = false;
                s.NoTnt = true;
                s.KeepMeAbove = 0.3; s.KeepThemAbove = 0.3;
                World w = s.W;
                var wt = new MvDefenceWatch(s.Them);
                var answered = new HashSet<int>();
                var starts = new List<(int p, double d)>();
                int last = -1, guardUntil = -1;
                int wu = w.Dur.AttackWindup[(int)them];
                s.BeforeTick = q =>
                {
                    wt.Update(q);
                    if (wt.Start != last) { last = wt.Start; starts.Add((wt.Start, wt.StartDist)); }
                    q.Human.MoveOverrideX = 0; q.Human.MoveOverrideZ = 0;
                    q.Human.Frame.Guard = w.Tick < guardUntil;
                    if (wt.Since(w) != Lag) return;
                    double reach = w.Config.Class(them).AttackRange + w.Config.Class(me).Radius;
                    if (wt.StartDist > reach + 0.5) return;
                    double rx = w.X[q.Them] - w.X[q.Me], rz = w.Z[q.Them] - w.Z[q.Me], rd = Math.Sqrt(rx * rx + rz * rz);
                    if (rd < 1e-6) return;
                    double ux = rx / rd, uz = rz / rd, sg = (wt.Start & 1) == 0 ? 1 : -1;
                    bool canDodge = Dodge.CanStart(w, q.Me);
                    if ((action == "side" || action == "sideguard") && canDodge)
                    {
                        q.Human.PressDodge = true;
                        q.Human.MoveOverrideX = -uz * sg;
                        q.Human.MoveOverrideZ = ux * sg;
                        answered.Add(wt.Start);
                        return;
                    }
                    if (action == "guard" || action == "sideguard")
                    {
                        guardUntil = wt.Start + wu + 2;
                        q.Human.Frame.Guard = true;
                        answered.Add(wt.Start);
                    }
                };
                PlayerSeatRig.Play(s, 30);
                double theirReach = w.Config.Class(them).AttackRange + w.Config.Class(me).Radius;
                var contacts = s.Spy.Of(SimEventType.AttackContact, e => e.A == s.Them);
                var hits = s.Spy.Of(SimEventType.Hit, e => e.A == s.Me && e.B == s.Them && e.Cause == DamageCause.Melee);
                t.Saved += s.Spy.Of(SimEventType.HitBlocked, e => e.A == s.Me && (int)e.Value == PlayerReasons.BlockedGuard).Sum(e => e.Radius);
                t.Taken += hits.Sum(e => e.Value);
                foreach (var (p, d) in starts)
                {
                    SeatEvent c = contacts.FirstOrDefault(e => e.Tick - 1 >= p + wu - 1 && e.Tick - 1 <= p + wu + 1);
                    if (c == null) continue;
                    bool landed = hits.Any(e => e.Tick == c.Tick);
                    if (answered.Contains(p)) { t.Answered++; if (landed) t.AnsweredLanded++; }
                    else if (d <= theirReach) { t.Unanswered++; if (landed) t.UnansweredLanded++; }
                }
            }
            return t;
        }

        /// <summary>
        /// A staged single swing: a scripted dummy of class attacker presses Attack once at the human seat (class defender) standing
        /// still 0.75 x the attacker's reach away, facing it. plan(seat, swingStartTick) writes the human's frame each tick (guard,
        /// jump). Returns (damage the human took from the swing, the HitBlocked reasons it raised).
        /// </summary>
        public static (double taken, List<int> blocked, Seat seat) StagedSwing(ClassId defender, ClassId attacker, double angle, int rep, Action<Seat, int> plan, Action<GameConfig> tweak, int fireIn = 4)
        {
            GameConfig gc = PlayerSeatRig.Config();
            double gap = 0.75 * gc.Class(attacker).AttackRange + gc.Class(defender).Radius;
            Seat s = PlayerSeatRig.Duel(defender, attacker, TargetMode.Passive, angle, rep, 0, distance: gap, tweak: tweak);
            s.Human.Fight = false;
            s.NoTnt = true;
            World w = s.W;
            if (attacker == ClassId.Demolitions && w.Demolitions != null && w.Demolitions.TntReadyTick != null) w.Demolitions.TntReadyTick[s.Them] = int.MaxValue / 2;
            int fireAt = w.Tick + fireIn;
            int start = fireAt + 1;   // the frame written before step fireAt + 1 is applied on tick fireAt + 1
            s.BeforeTick = q =>
            {
                q.Human.MoveOverrideX = 0; q.Human.MoveOverrideZ = 0;
                if (attacker == ClassId.Demolitions && w.Demolitions != null && w.Demolitions.TntReadyTick != null) w.Demolitions.TntReadyTick[q.Them] = int.MaxValue / 2;
                if (w.Tick == fireAt) q.Dummy.PressAttack = true;
                plan?.Invoke(q, start);
            };
            int mark = s.Spy.Mark;
            int wu = w.Dur.AttackWindup[(int)attacker];
            s.Steps(start + wu + 6 - w.Tick);
            double taken = s.Spy.Since(mark, SimEventType.Hit, e => e.A == s.Me && e.B == s.Them && e.Cause == DamageCause.Melee).Sum(e => e.Value);
            var blocked = s.Spy.Since(mark, SimEventType.HitBlocked, e => e.A == s.Me).Select(e => (int)e.Value).ToList();
            return (taken, blocked, s);
        }
    }

    [TestFixture, Category("PlayerSeat")]
    public sealed class MovesDefenceTests
    {
        static readonly ClassId[] GuardClasses = { ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder, ClassId.Healer };

        // ============================================================== switch off: nothing of lane 2 runs

        [Test]
        public void WithTheSwitchesOffNoDefenceEventFires()
        {
            var (taken, blocked, seat) = MvDefenceRig.StagedSwing(ClassId.Farmer, ClassId.Warrior, 0, 0, (q, start) => q.Human.Frame.Guard = true, null);
            Assert.IsNull(seat.W.Moves, "every switch off: World.Moves is not allocated");
            Assert.Greater(taken, 0, "the swing lands");
            Assert.IsFalse(blocked.Contains(PlayerReasons.BlockedGuard) || blocked.Contains(PlayerReasons.BlockedParried) || blocked.Contains(PlayerReasons.BlockedAir), "no lane 2 reason with the switches off");
        }

        // ============================================================== T2-1 guard (a)

        [Test]
        public void T2_1a_AHeldFrontalGuardCutsEveryAttackersSwing()
        {
            var fails = new List<string>();
            Action<GameConfig> on = MvDefenceRig.With(m => m.Guard = true);
            foreach (ClassId def in GuardClasses)
            {
                double cut = PlayerSeatRig.Config().Depth.Moves.GuardCut[(int)def];
                foreach (ClassId atk in MvDefenceRig.All)
                {
                    double plain = 0, guarded = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        double ang = PlayerSeatRig.AngleOf(k, 4) + 0.3;
                        plain += MvDefenceRig.StagedSwing(def, atk, ang, k, null, on).taken;
                        guarded += MvDefenceRig.StagedSwing(def, atk, ang, k, (q, start) => q.Human.Frame.Guard = true, on).taken;
                    }
                    double share = plain > 0 ? guarded / plain : double.NaN;
                    bool pass = plain > 0 && share <= 1 - cut + 0.05 + 1e-9;
                    PxLog.Line("mv2-T2-1a", def + " guards " + atk + " (4 starts, held from 3+ ticks before contact)", "takes " + PxLog.F(share) + " of " + PxLog.F(plain / 4, "0.0"), "<= " + PxLog.F(1 - cut + 0.05), pass);
                    if (!pass) fails.Add(def + "<-" + atk + " " + PxLog.F(share));
                }
            }
            Assert.IsEmpty(fails, "guard cut: " + string.Join(", ", fails));
        }

        [Test]
        public void T2_1_TheGuardBreaksBackHitsIgnoreItAndTheWarriorHasNone()
        {
            Action<GameConfig> on = MvDefenceRig.With(m => m.Guard = true);
            // a Builder's 60 meter against repeated Warrior cleaves (x1.5 drain): the guard breaks into the 15-tick stagger
            GameConfig gc = PlayerSeatRig.Config();
            Seat s = PlayerSeatRig.Duel(ClassId.Farmer, ClassId.Warrior, TargetMode.Passive, 0, 0, 0, distance: 0.75 * gc.Class(ClassId.Warrior).AttackRange + gc.Class(ClassId.Farmer).Radius, tweak: on);
            s.Human.Fight = false;
            s.KeepMeAbove = 0.3;
            int n = 0;
            double x0 = s.W.X[s.Me], z0 = s.W.Z[s.Me];
            s.BeforeTick = q =>
            {
                q.Human.MoveOverrideX = 0; q.Human.MoveOverrideZ = 0;
                q.W.X[q.Me] = x0; q.W.Z[q.Me] = z0;   // staging: the knock never carries her out of his reach
                q.W.Actors[q.Me].KVx = 0; q.W.Actors[q.Me].KVz = 0;
                q.Human.Frame.Guard = true;
                if (q.W.Tick % 20 == 5) { q.Dummy.PressAttack = true; n++; }
            };
            s.Steps(200);
            var breaks = s.Spy.Of(SimEventType.GuardBreak, e => e.A == s.Me);
            var blocks = s.Spy.Of(SimEventType.HitBlocked, e => e.A == s.Me && (int)e.Value == PlayerReasons.BlockedGuard);
            PxLog.Line("mv2-T2-1", "Farmer guard (meter 40) under Warrior cleaves x1.5 drain", "blocks " + blocks.Count + " breaks " + breaks.Count + " of " + n + " swings", "at least one break", breaks.Count >= 1);
            Assert.GreaterOrEqual(breaks.Count, 1, "the meter breaks under repeated cleaves");
            Assert.AreEqual((double)gc.Depth.Moves.GuardBreakStaggerTicks, breaks[0].Value, "GuardBreak Value = stagger ticks");
            // from behind: no cut
            var back = MvDefenceRig.StagedSwing(ClassId.Builder, ClassId.Warrior, 0, 0, (q, start) => { q.Human.Frame.Guard = true; q.Human.AimOverrideX = q.W.X[q.Me] - (q.W.X[q.Them] - q.W.X[q.Me]); q.Human.AimOverrideZ = q.W.Z[q.Me] - (q.W.Z[q.Them] - q.W.Z[q.Me]); }, on);
            var front = MvDefenceRig.StagedSwing(ClassId.Builder, ClassId.Warrior, 0, 0, null, on);
            PxLog.Line("mv2-T2-1", "Builder guarding with his back to a Warrior cleave", "takes " + PxLog.F(back.taken, "0.0") + " vs unguarded " + PxLog.F(front.taken, "0.0"), "back hits ignore the guard (equal, no BlockedGuard)", !back.blocked.Contains(PlayerReasons.BlockedGuard));
            Assert.IsFalse(back.blocked.Contains(PlayerReasons.BlockedGuard), "a back hit ignores the guard");
            // the Warrior: no guard cut (his shield is his block)
            var wg = MvDefenceRig.StagedSwing(ClassId.Warrior, ClassId.Miner, 0, 0, (q, start) => q.Human.Frame.Guard = true, on);
            Assert.IsFalse(wg.blocked.Contains(PlayerReasons.BlockedGuard), "the Warrior has no guard");
        }

        // ============================================================== T2-1 parry (b)

        [Test]
        public void T2_1b_AParryAtTheWindowsMiddleTickNegatesTheSwing()
        {
            Action<GameConfig> on = MvDefenceRig.With(m => m.Guard = true);
            GameConfig gc = PlayerSeatRig.Config();
            var fails = new List<string>();
            foreach (ClassId atk in MvDefenceRig.All)
            {
                int tries = 0, negated = 0, staggered = 0, jitterTries = 0, jitterNeg = 0;
                foreach (ClassId def in MvDefenceRig.All)
                {
                    // integrator (decision 1, the Healer ruling): a class with no parry window (the Healer, 0) cannot parry; her
                    // guard only cuts damage (MovesDefenceTests T2_1 guard rows and the combine duel table measure her)
                    if (def != ClassId.Warrior && gc.Depth.Moves.ParryWindowTicks[(int)def] <= 0) continue;
                    for (int k = 0; k < 4; k++)
                    {
                        for (int jitter = -1; jitter <= 1; jitter++)
                        {
                            int window = def == ClassId.Warrior ? gc.Depth.Moves.WarriorParryWindowShieldDown : gc.Depth.Moves.ParryWindowTicks[(int)def];
                            int before = (window + 1) / 2 + jitter;   // ticks before contact of the press edge
                            var r = MvDefenceRig.StagedSwing(def, atk, PlayerSeatRig.AngleOf(k, 4) + 0.3, k, (q, start) =>
                            {
                                int contact = start + q.W.Dur.AttackWindup[(int)atk];
                                q.Human.Frame.Guard = q.W.Tick + 1 == contact - before;   // a tap: the edge lands on tick contact - before
                            }, on);
                            bool neg = r.blocked.Contains(PlayerReasons.BlockedParried) && r.taken == 0;
                            if (jitter == 0)
                            {
                                tries++;
                                if (neg) negated++;
                                if (neg && r.seat.Spy.Of(SimEventType.HitStun, e => e.A == r.seat.Them).Count > 0) staggered++;
                            }
                            jitterTries++;
                            if (neg) jitterNeg++;
                        }
                    }
                }
                double share = (double)negated / tries;
                double need = atk == ClassId.Demolitions || atk == ClassId.Healer ? 0.70 : 0.95;
                bool pass = share >= need;
                PxLog.Line("mv2-T2-1b", "parry at the window's middle tick vs " + atk + " (6 defenders x 4 starts)", "negated " + negated + "/" + tries + " (" + PxLog.F(share) + "), attacker staggered " + staggered + "; with +-1 tick jitter " + PxLog.F((double)jitterNeg / jitterTries), ">= " + PxLog.F(need), pass);
                if (!pass) fails.Add(atk + " " + PxLog.F(share));
                if (atk == ClassId.Warrior) Assert.AreEqual(0, staggered, "a parried Warrior cleave is negated but never staggers him");
                else Assert.AreEqual(negated, staggered, "a parried swing staggers its attacker");
            }
            Assert.IsEmpty(fails, "parry: " + string.Join(", ", fails));
        }

        [Test]
        public void T2_1_ParryMashingIsLockedOutAndAParryOpensThePunishWindow()
        {
            Action<GameConfig> on = MvDefenceRig.With(m => m.Guard = true);
            // press 20 ticks early (a whiff, starts the lockout), then again at the right tick: the second press is locked out
            var r = MvDefenceRig.StagedSwing(ClassId.Miner, ClassId.Warrior, 0, 0, (q, start) =>
            {
                int contact = start + q.W.Dur.AttackWindup[(int)ClassId.Warrior];
                q.Human.Frame.Guard = q.W.Tick + 1 == contact - 12 || q.W.Tick + 1 == contact - 2;
            }, on);
            Assert.IsFalse(r.blocked.Contains(PlayerReasons.BlockedParried), "a second press inside the lockout does not parry");
            Assert.Greater(r.seat.W.Moves.Defence.ParryLocked, 0, "the lockout counted");
            // a landed parry: the attacker's WhiffRecoverUntil (the existing PUNISH window) covers ParryPunishSeconds
            var p = MvDefenceRig.StagedSwing(ClassId.Miner, ClassId.Farmer, 0, 0, (q, start) =>
            {
                int contact = start + q.W.Dur.AttackWindup[(int)ClassId.Farmer];
                q.Human.Frame.Guard = q.W.Tick + 1 == contact - 2;
            }, on);
            Assert.IsTrue(p.blocked.Contains(PlayerReasons.BlockedParried));
            SeatEvent ev = p.seat.Spy.Of(SimEventType.HitBlocked, e => (int)e.Value == PlayerReasons.BlockedParried).First();
            int until = p.seat.W.Depth.Feel.WhiffRecoverUntil[p.seat.Them];
            Assert.GreaterOrEqual(until, ev.Tick + CombatFeel.Ticks(p.seat.W, 0.8) - 1, "the parried attacker is open to PUNISH for 0.8 s");
            PxLog.Line("mv2-T2-1", "parry lockout and PUNISH window", "locked " + r.seat.W.Moves.Defence.ParryLocked + ", punish until +" + (until - ev.Tick) + " ticks", "lockout holds; punish >= 0.8 s", true);
        }

        // ============================================================== T2-7 jump evade (the hit test)

        [Test]
        public void T2_7_AJumpSixTicksIntoTheWindupEvadesTheLongSwingsOnly()
        {
            Action<GameConfig> on = MvDefenceRig.With(m => m.JumpEvade = true);
            var fails = new List<string>();
            foreach (ClassId atk in MvDefenceRig.All)
            {
                int tries = 0, evaded = 0, offEvaded = 0;
                foreach (ClassId def in MvDefenceRig.All)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        Action<Seat, int> plan = (q, start) => { if (q.W.Tick + 1 == start + 6) q.Human.PressJump = true; };
                        var r = MvDefenceRig.StagedSwing(def, atk, PlayerSeatRig.AngleOf(k, 4) + 0.3, k, plan, on);
                        var off = MvDefenceRig.StagedSwing(def, atk, PlayerSeatRig.AngleOf(k, 4) + 0.3, k, plan, null);
                        tries++;
                        if (r.taken == 0) evaded++;
                        if (off.taken == 0) offEvaded++;
                    }
                }
                int wu = PlayerSeatRig.Field(ClassId.Warrior).W.Dur.AttackWindup[(int)atk];
                double share = (double)evaded / tries;
                bool longSwing = atk == ClassId.Warrior || atk == ClassId.Builder || atk == ClassId.Miner;
                bool pass = longSwing ? share >= 0.80 : wu > 7 || share <= 0.40;
                PxLog.Line("mv2-T2-7", "jump pressed 6 ticks into a " + atk + " wind-up (" + wu + " ticks), 6 defenders x 4", "avoided " + PxLog.F(share) + " (switch off " + PxLog.F((double)offEvaded / tries) + ")", longSwing ? ">= 0.80" : (wu <= 7 ? "<= 0.40 (a read, not an answer)" : "report"), pass);
                if (!pass) fails.Add(atk + " " + PxLog.F(share));
            }
            Assert.IsEmpty(fails, "jump evade: " + string.Join(", ", fails));
        }

        [Test]
        public void T2_7_AnEvadingJumpLocksTheLandingAndTheNextJump()
        {
            Action<GameConfig> on = MvDefenceRig.With(m => m.JumpEvade = true);
            var r = MvDefenceRig.StagedSwing(ClassId.Miner, ClassId.Warrior, 0.3, 0, (q, start) => { if (q.W.Tick + 1 == start + 6) q.Human.PressJump = true; }, on);
            Assert.IsTrue(r.blocked.Contains(PlayerReasons.BlockedAir), "AIRBORNE");
            r.seat.Steps(24);   // the flight (18 ticks) lands
            DefenceMovesState d = r.seat.W.Moves.Defence;
            Assert.Greater(d.LandRecoverUntil[0], int.MinValue / 8, "the landing recovery was set");
            Assert.Greater(d.JumpRelockUntil[0], d.LandRecoverUntil[0], "the jump relock outlasts the landing recovery");
            PxLog.Line("mv2-T2-7", "evading jump landing", "recover " + (d.JumpRelockUntil[0] - d.LandRecoverUntil[0]) + " ticks between landing lock end and jump relock end", "landing 4 ticks, relock 1.0 s", true);
        }

        // ============================================================== T1-2 lunge

        [Test, Category("Slow")]
        public void T1_2_DodgeInLungesCatchMoreAndAChanceOfEscapeRemains()
        {
            Action<GameConfig> on = MvDefenceRig.With(m => m.DodgeLunge = true);
            var fails = new List<string>();
            foreach (ClassId me in MvDefenceRig.All)
            {
                MvDefenceTally plain = MvDefenceRig.ChaseRow(me, "plain", on);
                MvDefenceTally din = MvDefenceRig.ChaseRow(me, "dodgein", on);
                MvDefenceTally dinOff = MvDefenceRig.ChaseRow(me, "dodgein", null);
                bool pass = din.Share >= Math.Min(1.0, plain.Share + 0.10) - 1e-9;   // the Farmer's plain 0.93 has 0.07 of room
                if (me == ClassId.Warrior) pass &= plain.Share >= T.ChaseCatchShareMin - 1e-9 && plain.Share <= T.ChaseCatchShareMax + 1e-9;
                PxLog.Line("mv2-T1-2", me + " chase a running 29% bot (5 x 8), 4 s", "plain " + PxLog.F(plain.Share) + " dodge-in " + PxLog.F(din.Share) + " (switch off " + PxLog.F(dinOff.Share) + ")", ">= plain + 0.10" + (me == ClassId.Warrior ? ", plain in S11 band" : ""), pass);
                if (!pass) fails.Add(me + " plain " + PxLog.F(plain.Share) + " dodgein " + PxLog.F(din.Share));
            }
            foreach (ClassId me in new[] { ClassId.Warrior, ClassId.Miner, ClassId.Demolitions, ClassId.Builder })
            {
                // asserted: the runner's eye varies 5 to 8 ticks per start (a person, as the chaser's 5 to 7 does); a fixed 6-tick
                // eye is printed beside it: against the 6-tick Farmer lunge it is binary (the dodge always beats it by one tick)
                MvDefenceTally e = MvDefenceRig.Escape(me, ClassId.Farmer, true, on, 16, true);
                MvDefenceTally fixedEye = MvDefenceRig.Escape(me, ClassId.Farmer, true, on);
                MvDefenceTally eRefund = MvDefenceRig.Escape(me, ClassId.Farmer, true, MvDefenceRig.With(m => { m.DodgeLunge = true; m.DodgeAvoidRefund = true; }), 16, true);
                MvDefenceTally eOff = MvDefenceRig.Escape(me, ClassId.Farmer, false, null, 16, true);
                bool pass = e.Escaped >= 6 && e.Escaped <= 13;
                PxLog.Line("mv2-T1-2", me + " at 30% escapes a lunging lag-6 Farmer with one dodge per read, eye 5-8 ticks (16)", e.Escaped + "/16 (chaser lunges " + e.Uses + ", swings " + e.Answered + " landed " + e.AnsweredLanded + "; with refund on " + eRefund.Escaped + "/16; switches off, no lunge " + eOff.Escaped + "/16; fixed 6-tick eye " + fixedEye.Escaped + "/16)", "6 to 13 of 16", pass);
                if (!pass) fails.Add(me + " escape " + e.Escaped);
                if (fixedEye.Escaped > 13)
                {
                    MvDefenceTally l7 = MvDefenceRig.Escape(me, ClassId.Farmer, true, MvDefenceRig.With(m => { m.DodgeLunge = true; m.LungeStopShare = 0.70; }));
                    MvDefenceTally lw = MvDefenceRig.Escape(me, ClassId.Farmer, true, MvDefenceRig.With(m => { m.DodgeLunge = true; m.LungeWindupMul = 0.6; }));
                    PxLog.Line("mv2-lever", me + " escape, fixed 6-tick eye, LungeStopShare 0.70 (spec 0.85) / LungeWindupMul 0.6 (spec 0.75)", l7.Escaped + "/16 / " + lw.Escaped + "/16", "report", true);
                }
            }
            Assert.IsEmpty(fails, "lunge: " + string.Join(", ", fails));
        }

        [Test]
        public void T1_2_ADodgeInStopsShortAndTheBufferedSwingIsALunge()
        {
            Action<GameConfig> on = MvDefenceRig.With(m => m.DodgeLunge = true);
            GameConfig gc = PlayerSeatRig.Config();
            double reach = gc.Class(ClassId.Warrior).AttackRange;
            double gap = reach + gc.Class(ClassId.Miner).Radius + 2.0;
            Seat s = PlayerSeatRig.Duel(ClassId.Warrior, ClassId.Miner, TargetMode.Passive, 0, 0, 0, distance: gap, tweak: on);
            s.Human.Fight = false;
            World w = s.W;
            int t0 = w.Tick;
            s.BeforeTick = q =>
            {
                double rd = MvDefenceWatch.Dist(w, q.Me, q.Them);
                q.Human.MoveOverrideX = (w.X[q.Them] - w.X[q.Me]) / rd; q.Human.MoveOverrideZ = (w.Z[q.Them] - w.Z[q.Me]) / rd;
                if (w.Tick == t0 + 2) q.Human.PressDodge = true;
                if (w.Tick >= t0 + 4 && w.Tick <= t0 + 6) q.Human.PressAttack = true;
            };
            s.Steps(10);
            var lunge = s.Spy.Of(SimEventType.Lunge, e => e.A == s.Me);
            double end = MvDefenceWatch.Dist(w, s.Me, s.Them);
            Assert.AreEqual(1, lunge.Count, "one LUNGE");
            Assert.AreEqual((double)(int)Math.Floor(w.Dur.AttackWindup[(int)ClassId.Warrior] * 0.75 + 1e-9), lunge[0].Value, "the lunge wind-up is x0.75");
            Assert.LessOrEqual(end, reach + gc.Class(ClassId.Miner).Radius, "the dash ended inside reach");
            PxLog.Line("mv2-T1-2", "Warrior dodge-in at a Miner " + PxLog.F(gap) + " u away", "lunge wind-up " + lunge[0].Value + ", gap after dash " + PxLog.F(end), "9 ticks, inside reach", true);
        }

        // ============================================================== T1-3 refund and T2-1 (e)

        [Test, Category("Slow")]
        public void T1_3_AndT2_1e_TheAnsweredShareOfSwingsInReach()
        {
            Action<GameConfig> refund = MvDefenceRig.With(m => m.DodgeAvoidRefund = true);
            Action<GameConfig> both = MvDefenceRig.With(m => { m.DodgeAvoidRefund = true; m.Guard = true; });
            var fails = new List<string>();
            foreach (ClassId me in MvDefenceRig.All)
            {
                var off = new MvDefenceTally(); var on = new MvDefenceTally(); var sg = new MvDefenceTally();
                foreach (ClassId them in MvDefenceRig.All)
                {
                    off.Add(MvDefenceRig.React(me, them, "side", null));
                    on.Add(MvDefenceRig.React(me, them, "side", refund));
                    sg.Add(MvDefenceRig.React(me, them, "sideguard", both));
                }
                double S(MvDefenceTally x) => x.Answered + x.Unanswered > 0 ? (double)x.Answered / (x.Answered + x.Unanswered) : 0;
                bool pass = S(on) >= 0.30 - 1e-9 && S(sg) >= 0.60 - 1e-9;
                PxLog.Line("mv2-T1-3", me + " REACT side, answered share of swings in reach", "off " + PxLog.F(S(off)) + " refund on " + PxLog.F(S(on)) + " (avoided " + PxLog.F(on.Answered > 0 ? 1 - (double)on.AnsweredLanded / on.Answered : 0) + ")", ">= 0.30", S(on) >= 0.30 - 1e-9);
                PxLog.Line("mv2-T2-1e", me + " REACT dodge + guard, answered share", PxLog.F(S(sg)) + " (dodge-only off " + PxLog.F(S(off)) + "), guard saved " + PxLog.F(sg.Saved, "0") + " of taken " + PxLog.F(sg.Taken, "0"), ">= 0.60", S(sg) >= 0.60 - 1e-9);
                if (S(on) < 0.30 - 1e-9)
                {
                    var l6 = new MvDefenceTally(); var l8 = new MvDefenceTally();
                    foreach (ClassId them in MvDefenceRig.All)
                    {
                        l6.Add(MvDefenceRig.React(me, them, "side", MvDefenceRig.With(m => { m.DodgeAvoidRefund = true; m.AvoidRefundShare = 0.6; })));
                        l8.Add(MvDefenceRig.React(me, them, "side", MvDefenceRig.With(m => { m.DodgeAvoidRefund = true; m.AvoidRefundShare = 0.8; })));
                    }
                    PxLog.Line("mv2-lever", me + " REACT side with AvoidRefundShare 0.6 / 0.8 (spec 0.4)", PxLog.F(S(l6)) + " / " + PxLog.F(S(l8)), "report", true);
                }
                if (!pass) fails.Add(me + " refund " + PxLog.F(S(on)) + " guard " + PxLog.F(S(sg)));
            }
            Assert.IsEmpty(fails, "answered share: " + string.Join(", ", fails));
        }

        // ============================================================== T2-1 (c), T2-8, and the 40-duel table

        [Test, Category("Slow")]
        public void T2_1c_T2_8_TheDuelTableWithLaneTwoOn()
        {
            Action<GameConfig> on = MvDefenceRig.Lane2;
            var fails = new List<string>();
            var gains = new List<double>();
            foreach (ClassId me in MvDefenceRig.All)
            {
                var cellsOff = new Dictionary<ClassId, int>();
                var cellsOn = new Dictionary<ClassId, int>();
                MvDefenceTally baseOff = MvDefenceRig.DuelRow(me, "base", null, 1, cellsOff);
                MvDefenceTally baseOn = MvDefenceRig.DuelRow(me, "base", on, 1, cellsOn);
                MvDefenceTally dodgeOn = MvDefenceRig.DuelRow(me, "dodge", on);
                MvDefenceTally guardOn = MvDefenceRig.DuelRow(me, "guard", on);
                string cells = string.Join(" ", cellsOn.Select(kv => kv.Key.ToString().Substring(0, 2) + kv.Value));
                ClassId[] mid = { ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder };
                if (mid.Contains(me))
                {
                    int mo = mid.Where(c => c != me).Sum(c => cellsOff[c]), mn = mid.Where(c => c != me).Sum(c => cellsOn[c]);
                    PxLog.Line("mv2-S45", me + " vs the other middle three (24), base model", "off " + PxLog.F(mo / 24.0) + " on " + PxLog.F(mn / 24.0), "in [0.30, 0.70] (S45; reported, a known red at base)", mn / 24.0 >= 0.30 && mn / 24.0 <= 0.70);
                }
                PxLog.Line("mv2-duel", me + " vs Normal bots (40)", "base off " + baseOff.Wins + " on " + baseOn.Wins + " [" + cells + "] dodge " + dodgeOn.Wins + " guard " + guardOn.Wins + " (uses " + guardOn.Uses + ", saved " + PxLog.F(guardOn.Saved, "0") + ")", "ruling", true);
                if (me == ClassId.Warrior)
                {
                    bool ok = baseOn.Wins >= 36 && cellsOn.Values.All(v => v >= 6);
                    PxLog.Line("mv2-duel", "Warrior wins every one-on-one with lane 2 on", baseOn.Wins + "/40, min cell " + cellsOn.Values.Min(), ">= 36 and >= 6 of 8 each", ok);
                    if (!ok) fails.Add("Warrior " + baseOn.Wins);
                }
                else if (me == ClassId.Healer)
                {
                    bool ok = baseOn.Wins <= 8 && cellsOn.Values.All(v => v <= 3);
                    PxLog.Line("mv2-duel", "Healer loses nearly all with lane 2 on (the S44 model)", "base " + baseOn.Wins + "/40, max cell " + cellsOn.Values.Max(), "<= 8 and <= 3 of 8 each", ok);
                    // FINDING for the integrator (not asserted: a reactive lag-6 parry model against bots that do not guard yet)
                    PxLog.Line("mv2-finding", "Healer, dodge + guard model (every read wind-up answered; the press is also a parry)", guardOn.Wins + "/40", "<= 8 for the ruling", guardOn.Wins <= 8);
                    if (!ok) fails.Add("Healer " + baseOn.Wins);
                }
                else
                {
                    bool need = me != ClassId.Warrior && me != ClassId.Healer;
                    bool ok = !need || guardOn.Wins >= baseOn.Wins + 3;
                    PxLog.Line("mv2-T2-1c", me + " guard variant beats base", "guard " + guardOn.Wins + " base " + baseOn.Wins + " (dodge " + dodgeOn.Wins + ")", ">= base + 3", ok);
                    if (!ok) fails.Add(me + " guard " + guardOn.Wins + " base " + baseOn.Wins);
                }
                // T2-8: start at 25% with the dodge + guard model, second wind off vs on (the other lane 2 switches on in both);
                // 80 duels per class (16 starts), the band is asserted on the mean over the six classes (one class's 80 duels
                // still carry +-0.05 of noise), each class printed
                Action<GameConfig> noSw = c => { MvDefenceRig.Lane2(c); c.Depth.Moves.SecondWind = false; };
                MvDefenceTally lowOff = MvDefenceRig.DuelRow(me, "guard", noSw, 0.25, null, 16);
                MvDefenceTally lowOn = MvDefenceRig.DuelRow(me, "guard", on, 0.25, null, 16);
                double gain = lowOn.Share - lowOff.Share;
                gains.Add(gain);
                PxLog.Line("mv2-T2-8", me + " starts at 25%, dodge + guard model (80)", "second wind off " + lowOff.Wins + " on " + lowOn.Wins + " (gain " + PxLog.F(gain) + ")", "+0.05 to +0.15 (mean over classes)", gain <= 0.25);
                if (me == ClassId.Healer)
                {
                    // levers for the Healer ruling (measured, not shipped): her parry window narrowed, and no parry at all
                    MvDefenceTally w3 = MvDefenceRig.DuelRow(me, "guard", c => { MvDefenceRig.Lane2(c); c.Depth.Moves.ParryWindowTicks[(int)ClassId.Healer] = 3; });
                    MvDefenceTally w0 = MvDefenceRig.DuelRow(me, "guard", c => { MvDefenceRig.Lane2(c); c.Depth.Moves.ParryWindowTicks[(int)ClassId.Healer] = 0; });
                    MvDefenceTally c0 = MvDefenceRig.DuelRow(me, "guard", c => { MvDefenceRig.Lane2(c); c.Depth.Moves.GuardCut[(int)ClassId.Healer] = 0; });
                    MvDefenceTally c0sw = MvDefenceRig.DuelRow(me, "guard", c => { MvDefenceRig.Lane2(c); c.Depth.Moves.SecondWind = false; });
                    PxLog.Line("mv2-lever", "Healer guard variant with her parry window 3 / 0 (spec 5); with no guard cut (parry only, as the Warrior); with second wind off", w3.Wins + "/40 / " + w0.Wins + "/40; " + c0.Wins + "/40; " + c0sw.Wins + "/40", "report", true);
                }
            }
            double mean = gains.Average();
            bool swOk = mean >= 0.05 - 1e-9 && mean <= 0.15 + 1e-9;
            PxLog.Line("mv2-T2-8", "second wind gain, mean of six classes", PxLog.F(mean), "+0.05 to +0.15", swOk);
            if (!swOk) fails.Add("second wind mean gain " + PxLog.F(mean));
            Assert.IsEmpty(fails, "duels: " + string.Join(", ", fails));
        }

        // ============================================================== T1-4a feint bait (sim side)

        [Test]
        public void T1_4a_AFeintThatBaitedADodgeSaysSo()
        {
            Action<GameConfig> on = MvDefenceRig.With(m => m.FeintBait = true);
            GameConfig gc = PlayerSeatRig.Config();
            double gap = 0.8 * gc.Class(ClassId.Miner).AttackRange + gc.Class(ClassId.Farmer).Radius;
            int Run(int dodgeAt, int cancelAt, Action<GameConfig> tweak, out int value)
            {
                Seat s = PlayerSeatRig.Duel(ClassId.Miner, ClassId.Farmer, TargetMode.Passive, 0, 0, 0, distance: gap, tweak: tweak);
                s.Human.Fight = false;
                World w = s.W;
                int t0 = w.Tick + 2;
                s.BeforeTick = q =>
                {
                    q.Human.MoveOverrideX = 0; q.Human.MoveOverrideZ = 0;
                    if (w.Tick == t0) q.Human.PressAttack = true;
                    if (w.Tick == t0 + cancelAt) q.Human.PressCancel = true;
                    if (w.Tick == t0 + dodgeAt) { q.Dummy.PressDodge = true; q.Dummy.MoveX = 0; q.Dummy.MoveZ = 1; }
                };
                s.Steps(30);
                var ev = s.Spy.Of(SimEventType.FeintBaited, e => e.A == s.Me && e.B == s.Them);
                Assert.AreEqual(1, s.Spy.Of(SimEventType.Feint, e => e.A == s.Me && (int)e.Value == 1).Count, "the feint happened");
                value = ev.Count > 0 ? (int)ev[0].Value : 0;
                return ev.Count;
            }
            int before = Run(2, 4, on, out int v1);
            int after = Run(5, 4, on, out int v2);
            int early = Run(-12, 4, on, out _);
            int off = Run(2, 4, null, out _);
            PxLog.Line("mv2-T1-4a", "FeintBaited on a dodge 2 before / 1 after / 16 before the feint, switch off", before + " (V" + v1 + ") / " + after + " (V" + v2 + ") / " + early + " / off " + off, "1 / 1 / 0 / 0", before == 1 && after == 1 && early == 0 && off == 0);
            Assert.AreEqual(1, before);
            Assert.AreEqual(1, v1, "Value 1 = dodge");
            Assert.AreEqual(1, after);
            Assert.AreEqual(0, early);
            Assert.AreEqual(0, off);
        }

        // ============================================================== T2-8 unit

        [Test]
        public void T2_8_ADodgeBelowAQuarterGivesTheSecondWindOncePerLife()
        {
            Action<GameConfig> on = MvDefenceRig.With(m => { m.SecondWind = true; m.Guard = true; });
            var r = MvDefenceRig.StagedSwing(ClassId.Miner, ClassId.Farmer, 0.3, 0, (q, start) =>
            {
                if (q.W.Tick + 1 == start) q.W.Actors[q.Me].Hp = 0.2 * q.W.Actors[q.Me].MaxHp;
                q.Human.Frame.Guard = true;
            }, on);
            var sw = r.seat.Spy.Of(SimEventType.SecondWind, e => e.A == r.seat.Me);
            Assert.AreEqual(1, sw.Count, "a guard block below 25% fires SECOND WIND");
            Assert.AreEqual(0.15 * r.seat.W.Actors[0].MaxHp, sw[0].Value, 1e-9, "heals 15% of max HP");
            PxLog.Line("mv2-T2-8", "guard block at 20% HP", "SECOND WIND +" + PxLog.F(sw[0].Value, "0.0"), "once, 15% max HP", true);
        }
    }

    /// <summary>
    /// Healer-ruling diagnosis (fix/mv-healer-ruling): with every switch on (MvCombine.AllOn, CM_MVI_KNOBS honoured), per variant and
    /// opponent, what the Healer's wins are made of: her dealt and taken damage, heals landed on her, second winds, her criticals by
    /// kind, her dodges and dodged swings, hit-cancels, and how long the fight ran. Runs only with CM_MVH_DIAG=1.
    /// </summary>
    [TestFixture, Category("PlayerSeat")]
    public class MvHealerDiag
    {
        /// <summary>The combine's bot check under CM_MVI_KNOBS (CM_MVH_BOTS=1): which knob moves the own-Kaiju blocking.</summary>
        [Test, Category("Slow"), Timeout(3000000)]
        public void BotsLever()
        {
            if (Environment.GetEnvironmentVariable("CM_MVH_BOTS") != "1") return;   // CM_MVH_BOTS=1 only
            try { new MovesCombineTests().Combine_Bots_OwnKaijuBlockAndShiver_EverySwitchOn(); }
            catch (AssertionException) { }
        }

        /// <summary>The Warrior escape lever (CM_MVH_ESC=1): Combine_Escape's Warrior row with CM_MVI_KNOBS.</summary>
        [Test, Category("Slow"), Timeout(3000000)]
        public void WarriorEscapeLever()
        {
            if (Environment.GetEnvironmentVariable("CM_MVH_ESC") != "1") return;   // CM_MVH_ESC=1 only
            MvDefenceTally varied = MvDefenceRig.Escape(ClassId.Warrior, ClassId.Farmer, true, MvCombine.AllOn, spreadLag: true);
            MvDefenceTally fixed6 = MvDefenceRig.Escape(ClassId.Warrior, ClassId.Farmer, true, MvCombine.AllOn);
            PxLog.Line("mvh-escape", "Warrior at 30% from a lunging lag-6 human Farmer [" + MvCombine.Knobs() + "]", varied.Escaped + "/16 (fixed 6-tick eye " + fixed6.Escaped + "/16)", "6 to 13", varied.Escaped >= 6 && varied.Escaped <= 13);
        }

        [Test, Category("Slow"), Timeout(6000000)]
        public void HealerWinsAreMadeOf()
        {
            if (Environment.GetEnvironmentVariable("CM_MVH_DIAG") != "1") return;   // CM_MVH_DIAG=1 only
            ClassId me = ClassId.Healer;
            foreach (string v in MvCombine.Variants())
            {
                int totalWins = 0;
                foreach (ClassId them in MvDefenceRig.All)
                {
                    if (them == me) continue;
                    int wins = 0, losses = 0, n = 0, crits = 0, critC = 0, critP = 0, critB = 0, sw = 0, dodges = 0, dodged = 0, cancels = 0, guards = 0;
                    double secW = 0, secL = 0, dealtW = 0, takenW = 0, healW = 0, swW = 0, dealtL = 0, healL = 0, critDmg = 0;
                    int lastCrit = -1, myCrits = 0, myCritC = 0, myCritP = 0, myCritB = 0;
                    double heal = 0, swHeal = 0;
                    var lines = new List<string>();
                    MvDefenceRig.DuelCell(me, them, v, Environment.GetEnvironmentVariable("CM_MVH_OFF") == "1" ? (Action<GameConfig>)MvCombine.AllOff : MvCombine.AllOn, 8, 1, BotDifficulty.Normal,
                        q =>
                        {
                            CombatFeelState st = q.W.Depth.Feel;
                            if (st != null && st.CritFrom[q.Them] == q.Me && st.CritTick[q.Them] != lastCrit && st.CritTick[q.Them] >= 0)
                            {
                                lastCrit = st.CritTick[q.Them];
                                myCrits++;
                                if (st.CritKind[q.Them] == 1) myCritC++; else if (st.CritKind[q.Them] == 2) myCritP++; else myCritB++;
                            }
                        },
                        (q, r) =>
                        {
                            n++;
                            heal = q.Spy.Of(SimEventType.HealLanded, e => e.A == q.Me).Sum(e => e.Value);
                            swHeal = q.Spy.Of(SimEventType.SecondWind, e => e.A == q.Me).Sum(e => e.Value);
                            int swc = q.Spy.Of(SimEventType.SecondWind, e => e.A == q.Me).Count();
                            int stag = q.Spy.Of(SimEventType.AttackCancel, e => e.A == q.Them && e.B == q.Me).Count();
                            int miss = q.Spy.Of(SimEventType.AttackContact, e => e.A == q.Them && e.Value == 0).Count();
                            int stun = q.Spy.Of(SimEventType.HitStun, e => e.A == q.Them && e.B == q.Me).Count();
                            dodges += q.Spy.Of(SimEventType.Dodge, e => e.A == q.Me).Count();
                            dodged += q.Spy.Of(SimEventType.HitBlocked, e => e.A == q.Me && (int)e.Value == PlayerReasons.BlockedDodge).Count();
                            guards += q.Spy.Of(SimEventType.HitBlocked, e => e.A == q.Me && (int)e.Value == PlayerReasons.BlockedGuard).Count();
                            cancels += q.Spy.Of(SimEventType.AttackCancel, e => e.A == q.Me).Count();
                            sw += swc;
                            crits += myCrits; critC += myCritC; critP += myCritP; critB += myCritB;
                            if (r.Won) { wins++; secW += r.Seconds; dealtW += r.Dealt; takenW += r.Taken; healW += heal; swW += swHeal; }
                            if (r.Lost) { losses++; secL += r.Seconds; dealtL += r.Dealt; healL += heal; }
                            lines.Add((r.Won ? "W" : r.Lost ? "L" : "T") + PxLog.F(r.Seconds, "0.0") + "s d" + PxLog.F(r.Dealt, "0") + " t" + PxLog.F(r.Taken, "0")
                                + " h" + PxLog.F(heal, "0") + " sw" + PxLog.F(swHeal, "0") + " c" + myCritC + "/" + myCritP + "/" + myCritB + " sw" + r.Swings + "/" + r.Landed + " th" + r.TheirSwings + "/" + r.TheirLanded + " miss" + miss + " cut" + stag + " stun" + stun);
                            myCrits = myCritC = myCritP = myCritB = 0; lastCrit = -1;
                        });
                    totalWins += wins;
                    PxLog.Line("mvh-diag", "Healer " + v + " vs " + them + " (8)", wins + "W " + losses + "L; wins: mean " + (wins > 0 ? PxLog.F(secW / wins, "0.0") : "-") + " s, dealt "
                        + (wins > 0 ? PxLog.F(dealtW / wins, "0") : "-") + " taken " + (wins > 0 ? PxLog.F(takenW / wins, "0") : "-") + " healed " + (wins > 0 ? PxLog.F(healW / wins, "0") : "-")
                        + " secondwind " + (wins > 0 ? PxLog.F(swW / wins, "0") : "-") + "; crits counter/punish/back " + critC + "/" + critP + "/" + critB + ", dodges " + dodges + " dodged " + dodged
                        + " guarded " + guards + " cancels " + cancels + " secondwinds " + sw + " | " + string.Join(", ", lines), "report", true);
                }
                PxLog.Line("mvh-diag", "Healer " + v + " total", totalWins + "/40", "<= 8", totalWins <= 8);
            }
        }
    }

    /// <summary>
    /// Guard fix (2026-11-23, the Editor reviewer's live-game findings with every switch on): a parried swing breaks the attacker's
    /// combo chain and a fully blocked one does not advance it; a held guard never arms the parry lockout (hold, release, tap
    /// parries); the guard cuts damage from the press tick + 2; a lunge whose aim lags (the third-person camera point) still lands.
    /// </summary>
    [TestFixture, Category("PlayerSeat")]
    public sealed class MovesGuardFixTests
    {
        /// <summary>Human attacker me (lag 0, fights) vs a passive dummy defender that parries ("tap": a 2-tick press ending the tick
        /// before contact) or holds its guard ("hold") on every swing. Returns ComboStep events of me whose previous contact was
        /// parried or blocked, the step events in all, and the parries / blocks seen.</summary>
        static (int bad, int steps, int parried, int blocked, int contacts) ChainRun(ClassId me, ClassId them, string mode, int angle)
        {
            Seat s = PlayerSeatRig.Duel(me, them, TargetMode.Passive, PlayerSeatRig.AngleOf(angle, 4), angle, 0, tweak: MvCombine.AllOn);
            World w = s.W;
            s.BeforeTick = q =>
            {
                Actor a = w.Actors[q.Me];
                int contact = a.SwingPending ? a.SwingTick + a.SwingWindup : int.MinValue;
                int applied = w.Tick + 1;
                if (mode == "hold") q.Dummy.Frame.Guard = true;
                else if (mode == "tap") q.Dummy.Frame.Guard = applied == contact - 2 || applied == contact - 1;
                else q.Dummy.Frame.Guard = false;
                q.Dummy.AimX = w.X[q.Me]; q.Dummy.AimZ = w.Z[q.Me];
                w.Actors[q.Them].Hp = w.Actors[q.Them].MaxHp;   // the defender never dies: the chain is what is measured
            };
            s.Steps(30 * 12);
            var contacts = s.Spy.Of(SimEventType.AttackContact, e => e.A == s.Me).Select(e => e.Tick).ToList();
            var bl = s.Spy.Of(SimEventType.HitBlocked, e => e.B == s.Me && e.A == s.Them);
            var parriedAt = new HashSet<int>(bl.Where(e => (int)e.Value == PlayerReasons.BlockedParried).Select(e => e.Tick));
            var blockedAt = new HashSet<int>(bl.Where(e => (int)e.Value == PlayerReasons.BlockedGuard).Select(e => e.Tick));
            int bad = 0;
            var steps = s.Spy.Of(SimEventType.ComboStep, e => e.A == s.Me);
            var stepAt = new Dictionary<int, int>();
            foreach (SeatEvent st in steps) stepAt[st.Tick] = (int)st.Value;
            foreach (SeatEvent st in steps)
            {
                int prev = contacts.Where(t => t < st.Tick).DefaultIfEmpty(int.MinValue).Max();
                if (prev == int.MinValue) continue;
                int prevStep = stepAt.TryGetValue(prev, out int ps) ? ps : 1;
                // a parried swing breaks the chain (no step follows it); a blocked one does not advance it (at most its own step again)
                if (parriedAt.Contains(prev) || (blockedAt.Contains(prev) && (int)st.Value > prevStep)) bad++;
            }
            return (bad, steps.Count, bl.Count(e => (int)e.Value == PlayerReasons.BlockedParried), bl.Count(e => (int)e.Value == PlayerReasons.BlockedGuard), contacts.Count);
        }

        [Test]
        public void AParriedSwingBreaksTheChainAndABlockedOneDoesNotAdvanceIt()
        {
            var bad = new List<string>();
            foreach (ClassId me in new[] { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Builder })
            {
                int free = 0, tapBad = 0, tapSteps = 0, tapParried = 0, holdBad = 0, holdSteps = 0, holdBlocked = 0;
                for (int k = 0; k < 4; k++)
                {
                    free += ChainRun(me, ClassId.Farmer, "none", k).steps;
                    var t = ChainRun(me, ClassId.Farmer, "tap", k);
                    tapBad += t.bad; tapSteps += t.steps; tapParried += t.parried;
                    var h = ChainRun(me, ClassId.Farmer, "hold", k);
                    holdBad += h.bad; holdSteps += h.steps; holdBlocked += h.blocked;
                }
                bool ok = tapBad == 0 && holdBad == 0 && free > 0 && tapParried > 0;
                PxLog.Line("mvg-chain", me + " chains on a passive Farmer, 4 starts x 12 s", "no defence " + free + " steps; parried " + tapParried + ": steps " + tapSteps + " (after a parried swing " + tapBad
                    + "); held guard, blocked " + holdBlocked + ": steps " + holdSteps + " (after a blocked swing " + holdBad + ")", "0 steps after a parried swing; after a blocked one never a higher step", ok);
                if (!ok) bad.Add(me.ToString());
            }
            Assert.IsEmpty(bad, "a parried or blocked swing still opened the chain: " + string.Join(", ", bad));
        }

        [Test]
        public void HoldReleaseTapParriesAndMashingIsStillLockedOut()
        {
            var fails = new List<string>();
            foreach (ClassId def in new[] { ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder })
            {
                // hold the guard 20 ticks, let go, tap 2 ticks before contact (inside 1 s of the first press)
                var r = MvDefenceRig.StagedSwing(def, ClassId.Warrior, 0, 0, (q, start) =>
                {
                    int contact = start + q.W.Dur.AttackWindup[(int)ClassId.Warrior];
                    int t = q.W.Tick + 1;
                    q.Human.Frame.Guard = (t >= contact - 34 && t < contact - 14) || t == contact - 2;
                }, MvCombine.AllOn, fireIn: 24);
                bool parried = r.blocked.Contains(PlayerReasons.BlockedParried);
                // mash: two taps 6 ticks apart (the first caught nothing), then the real one: locked out
                var m = MvDefenceRig.StagedSwing(def, ClassId.Warrior, 0, 0, (q, start) =>
                {
                    int contact = start + q.W.Dur.AttackWindup[(int)ClassId.Warrior];
                    int t = q.W.Tick + 1;
                    q.Human.Frame.Guard = t == contact - 14 || t == contact - 8 || t == contact - 2;
                }, MvCombine.AllOn, fireIn: 24);
                bool mashParried = m.blocked.Contains(PlayerReasons.BlockedParried);
                bool ok = parried && !mashParried && m.seat.W.Moves.Defence.ParryLocked > 0;
                PxLog.Line("mvg-parry", def + " vs a Warrior swing", "hold 20, release, tap: " + (parried ? "PARRIED" : "not parried") + "; mash: " + (mashParried ? "PARRIED" : "locked out (" + m.seat.W.Moves.Defence.ParryLocked + ")"),
                    "hold-release-tap parries; mashing locked out", ok);
                if (!ok) fails.Add(def.ToString());
            }
            Assert.IsEmpty(fails, string.Join(", ", fails));
        }

        /// <summary>The first contact tick (relative to the press's applied tick) a held guard cuts: press applied at contact - k.</summary>
        [Test]
        public void TheGuardCutsFromThePressTickPlusTwo()
        {
            var fails = new List<string>();
            foreach (ClassId def in new[] { ClassId.Healer, ClassId.Farmer, ClassId.Builder })
            {
                var parts = new List<string>();
                int first = -1;
                for (int k = 0; k <= 6; k++)
                {
                    int kk = k;
                    var r = MvDefenceRig.StagedSwing(def, ClassId.Warrior, 0, 0, (q, start) =>
                    {
                        int contact = start + q.W.Dur.AttackWindup[(int)ClassId.Warrior];
                        q.Human.Frame.Guard = q.W.Tick + 1 >= contact - kk;
                    }, MvCombine.AllOn, fireIn: 24);
                    bool cut = r.blocked.Contains(PlayerReasons.BlockedGuard) || r.blocked.Contains(PlayerReasons.BlockedParried);
                    parts.Add("+" + k + (r.blocked.Contains(PlayerReasons.BlockedParried) ? " parry" : r.blocked.Contains(PlayerReasons.BlockedGuard) ? " guard" : " -"));
                    if (cut && first < 0) first = k;
                }
                bool ok = first >= 0 && first <= 2;
                PxLog.Line("mvg-raise", def + " press applied k ticks before a Warrior's contact", string.Join(", ", parts) + "; first cut at press + " + first, "<= press + 2", ok);
                if (!ok) fails.Add(def + " " + first);
            }
            Assert.IsEmpty(fails, string.Join(", ", fails));
        }

        /// <summary>A dodge-in at a standing target 3.4 u away, the attack buffered in the dash. Top-down (off 0): stick and aim on
        /// the target. Third person (off +-30): the stick and the aim both point along the lagging camera, 30 degrees off the target
        /// (inside the 35-degree lunge cone): before the fix that dash ended 1.7 u BESIDE the target, facing past it, and whiffed.</summary>
        [Test]
        public void ALungeThatReachesItsStopPointLandsWhateverTheAim()
        {
            var fails = new List<string>();
            foreach (ClassId me in new[] { ClassId.Miner, ClassId.Warrior, ClassId.Farmer, ClassId.Builder })
                foreach (double off in new[] { 0.0, 30.0, -30.0 })
                {
                    Seat s = PlayerSeatRig.Duel(me, ClassId.Farmer, TargetMode.Passive, 0, 0, 0, distance: 3.4, tweak: MvCombine.AllOn);
                    s.Human.Fight = false;
                    World w = s.W;
                    int t0 = w.Tick;
                    double end = -1;
                    s.BeforeTick = q =>
                    {
                        double rd = MvDefenceWatch.Dist(w, q.Me, q.Them);
                        double ux = (w.X[q.Them] - w.X[q.Me]) / rd, uz = (w.Z[q.Them] - w.Z[q.Me]) / rd;
                        double a = off * Math.PI / 180;
                        q.Human.AimOverrideX = w.X[q.Me] + 4 * (ux * Math.Cos(a) - uz * Math.Sin(a));
                        q.Human.AimOverrideZ = w.Z[q.Me] + 4 * (ux * Math.Sin(a) + uz * Math.Cos(a));
                        q.Human.MoveOverrideX = ux * Math.Cos(a) - uz * Math.Sin(a); q.Human.MoveOverrideZ = ux * Math.Sin(a) + uz * Math.Cos(a);
                        if (w.Tick == t0 + 2) q.Human.PressDodge = true;
                        if (w.Tick >= t0 + 4 && w.Tick <= t0 + 6) q.Human.PressAttack = true;
                        if (w.Moves != null && w.Moves.Defence.LungeSwingTick[q.Me] == w.Tick && end < 0) end = rd;
                    };
                    s.Steps(30);
                    int lunges = s.Spy.Of(SimEventType.Lunge, e => e.A == s.Me).Count;
                    int hits = s.Spy.Of(SimEventType.Hit, e => e.A == s.Them && e.B == s.Me && e.Cause == DamageCause.Melee).Count;
                    bool ok = lunges == 1 && hits >= 1;
                    PxLog.Line("mvg-lunge", me + " dodge-in at 3.4 u, stick and aim " + off + " deg off the target", "lunges " + lunges + ", gap at the swing " + PxLog.F(end) + ", hits " + hits, "1 lunge, it lands", ok);
                    if (!ok) fails.Add(me + " " + off);
                }
            Assert.IsEmpty(fails, "lunge whiffed: " + string.Join(", ", fails));
        }
    }
}
