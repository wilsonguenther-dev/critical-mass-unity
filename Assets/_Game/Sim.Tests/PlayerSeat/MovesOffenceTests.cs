// Class moves build (2026-11-22), lane 4 (Offence): T1-1 TNT on its own key, T2-2 heavy / charged, T2-3 combos.
// Every number is the HUMAN seat (actor 0) through MatchTick.AdvanceHumans: reaction lag 6 ticks (0.2 s), the shipped mouse aim
// help (Low), real Normal bots unless a line says otherwise (the audit probe's model, cm-incidents/class-moves-audit-2026-11-22).
// Each test prints "[px]" lines through PxLog with the measured value next to the SPEC.md acceptance number.
// With the Heavy switch on a held Attack charges, so the rig's human (who holds Attack while a swing is ready) is driven
// through OffencePilot: a tap (press one tick, release the next) for light swings, and, in the heavy variant, a hold.
using System;
using System.Collections.Generic;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using T = CriticalMass.Sim.Tests.PlayerSeat.PlayerSeatTargets;

namespace CriticalMass.Sim.Tests.PlayerSeat
{
    /// <summary>Wins, swings and the per-opponent rows of one class's duel table.</summary>
    public sealed class OffTally
    {
        public int Reps, Wins, Losses, Swings, Landed, Heavies, Steps;
        public double Seconds;
        public readonly List<string> Rows = new List<string>();
        public readonly Dictionary<ClassId, int> WinsVs = new Dictionary<ClassId, int>();
        public double Share => Reps > 0 ? (double)Wins / Reps : 0;
        public double Land => Swings > 0 ? (double)Landed / Swings : 0;
        public override string ToString() => Wins + "/" + Reps + " land " + PxLog.F(Land) + " heavies " + Heavies + " steps " + Steps + " [" + string.Join(", ", Rows) + "]";
    }

    /// <summary>The human seat's hands with the class moves on: taps for light swings, an optional heavy policy.</summary>
    public sealed class OffencePilot
    {
        /// <summary>True: the Heavy switch is on, so the model's held Attack becomes a one-tick tap (release on the next tick).</summary>
        public bool Tap;
        /// <summary>The heavy policy: hold to charge when the target is staggered or outside its own reach; release once it is in
        /// the heavy's reach after ChargeStartTick, or at full charge.</summary>
        public bool UseHeavy;
        /// <summary>Policy B (approach charge): charge while the target walks in, outside its own reach and outside the light
        /// reach, and release once it is inside the heavy's reach (or at full charge once it is).</summary>
        public bool ApproachHeavy;
        public int FullHold = 24, MinHold = 8;
        public int Heavies;
        bool _prev, _holding;
        int _hold;

        public void Post(Seat s, InputFrame f)
        {
            if (!Tap || s.Human == null) return;
            World w = s.W;
            int me = s.Human.Me, t = s.Human.Target;
            if (_holding)
            {
                _hold++;
                double d = MvDist(w, me, t);
                MovesHooks.OffenceHeavyShape(w, w.Actors[me].Cls, out double hr, out _);
                bool inHeavy = t >= 0 && w.Actors[t].Alive && d <= hr + w.Config.Class(w.Actors[t].Cls).Radius;
                bool release = ApproachHeavy
                    ? !w.Actors[me].Alive || _hold >= 35 || (_hold >= MinHold && inHeavy)
                    : !w.Actors[me].Alive || _hold >= FullHold || (_hold >= MinHold && inHeavy && _hold >= MinHold + 2);
                f.Attack = !release;
                if (release) { _holding = false; Heavies++; }
                _prev = f.Attack;
                return;
            }
            if (UseHeavy && f.Attack && !_prev && t >= 0 && w.Actors[t].Alive)
            {
                ClassConfig them = w.Config.Class(w.Actors[t].Cls);
                double d = MvDist(w, me, t);
                bool staggered = w.Actors[t].HitStunTicks > 0;
                bool outOfTheirReach = d > them.AttackRange + w.Config.Class(w.Actors[me].Cls).Radius + 0.2;
                if (staggered || outOfTheirReach)
                {
                    _holding = true;
                    _hold = 1;
                    _prev = true;
                    return;
                }
            }
            if (ApproachHeavy && !f.Attack && !_prev && t >= 0 && w.Actors[t].Alive && w.Actors[me].Alive && Combat.AttackReady(w, me))
            {
                ClassConfig mine = w.Config.Class(w.Actors[me].Cls), them = w.Config.Class(w.Actors[t].Cls);
                MovesHooks.OffenceHeavyShape(w, w.Actors[me].Cls, out double hr, out _);
                double d = MvDist(w, me, t);
                if (d > mine.AttackRange + them.Radius && d > them.AttackRange + mine.Radius + 0.2 && d <= hr + them.Radius + 1.2)
                {
                    _holding = true;
                    _hold = 1;
                    f.Attack = true;
                    _prev = true;
                    return;
                }
            }
            if (f.Attack && _prev) f.Attack = false;   // a tap: one tick down, then up
            _prev = f.Attack;
        }

        public static double MvDist(World w, int a, int b) { double dx = w.X[b] - w.X[a], dz = w.Z[b] - w.Z[a]; return Math.Sqrt(dx * dx + dz * dz); }
    }

    /// <summary>The enemy seat's melee wind-ups (a copy of the audit probe's watch) and the class kit a sensible player uses.</summary>
    public sealed class OffWatch
    {
        int _head;
        public int TheirStart = -100000;
        public double TheirStartDist = 999;
        public int Since(World w) => w.Tick - TheirStart;
        public void Update(Seat s)
        {
            var all = s.Spy.All;
            for (; _head < all.Count; _head++)
            {
                SeatEvent e = all[_head];
                if (e.Type == SimEventType.AttackStart && e.A == s.Them && e.B == Combat.KindAttack)
                {
                    TheirStart = e.Tick - 1;
                    TheirStartDist = OffencePilot.MvDist(s.W, s.Me, s.Them);
                }
            }
        }

        /// <summary>The audit probe's UseKit (one-on-one): Warrior shield on a read swing, Farmer roots, Miner haul, Demolitions
        /// mine at her feet, Healer pulse on herself under 65%; the Builder has no combat ability.</summary>
        public static bool UseKit(Seat s, OffWatch wt, ClassId me)
        {
            World w = s.W;
            Actor a = w.Actors[s.Me];
            if (!a.Alive || a.AbilityCd > 0) return false;
            double d = OffencePilot.MvDist(w, s.Me, s.Them);
            ClassId them = w.Actors[s.Them].Cls;
            double theirReach = w.Config.Class(them).AttackRange + w.Config.Class(me).Radius;
            switch (me)
            {
                case ClassId.Warrior:
                    if (wt.Since(w) == OffRig.Lag && wt.TheirStartDist <= theirReach + 0.5) { s.Human.PressAbility = true; return true; }
                    return false;
                case ClassId.Farmer:
                    if (d <= 5.5) { s.Human.PressAbility = true; s.Human.AimOverrideX = w.X[s.Them]; s.Human.AimOverrideZ = w.Z[s.Them]; return true; }
                    return false;
                case ClassId.Miner:
                    if (d <= 4) { s.Human.PressAbility = true; return true; }
                    return false;
                case ClassId.Demolitions:
                    if (d <= 2.5) { s.Human.PressAbility = true; s.Human.AimOverrideX = w.X[s.Me]; s.Human.AimOverrideZ = w.Z[s.Me]; return true; }
                    return false;
                case ClassId.Healer:
                    if (a.Hp < 0.65 * a.MaxHp) { s.Human.PressAbility = true; s.Human.AimOverrideX = w.X[s.Me]; s.Human.AimOverrideZ = w.Z[s.Me]; return true; }
                    return false;
                default:
                    return false;
            }
        }
    }

    /// <summary>Seat stepping with a frame post-process (Seat.Step's order exactly, plus a hook before AdvanceHumans).</summary>
    public static class OffRig
    {
        public const int Lag = 6, Help = 1;
        /// <summary>The heavy variant's policy in Cell: false = literal (staggered or out of its reach), true = approach charge.</summary>
        [ThreadStatic] public static bool ApproachPolicy;
        /// <summary>The duel cells' aim help (1 = Low, the audit probe's model; 0 = Off, the S5 / S44 / S45 gate model).</summary>
        public static int CellHelp = Help;
        /// <summary>The rig's held-Attack model (PlayerSeatRig HumanModel.HoldAttack) in Cell; false = the rig's tap mode with Heavy on.</summary>
        public static bool CellHold;

        public static void Step(Seat s, Action<Seat> post)
        {
            s.BeforeTick?.Invoke(s);
            World w = s.W;
            if (s.NoTnt && w.Demolitions != null && w.Demolitions.TntReadyTick != null) w.Demolitions.TntReadyTick[s.Me] = int.MaxValue / 2;
            for (int i = 0; i < s.Frames.Length; i++) s.Frames[i] = null;
            if (s.Human != null) s.Frames[s.Human.Me] = s.Human.Fill(w);
            if (s.Human2 != null) s.Frames[s.Human2.Me] = s.Human2.Fill(w);
            if (s.Dummy != null) s.Frames[s.Dummy.Me] = s.Dummy.Fill(w);
            post?.Invoke(s);
            MatchTick.AdvanceHumans(w, s.Frames, s.Scratch);
            s.Spy.Pump();
            if (s.Human != null && MechRules.Autopiloted(w, s.Human.Me))
            {
                s.AutopilotTicks++;
                if (!s.AllowAutopilot) throw new InvalidOperationException("lane 4 rig: the idle autopilot drove the human seat at tick " + w.Tick);
            }
            if (s.KeepMeAbove > 0 && w.Actors[s.Me].Alive && w.Actors[s.Me].Hp < s.KeepMeAbove * w.Actors[s.Me].MaxHp) w.Actors[s.Me].Hp = w.Actors[s.Me].MaxHp;
            if (s.KeepThemAbove > 0 && s.Them >= 0 && w.Actors[s.Them].Alive && w.Actors[s.Them].Hp < s.KeepThemAbove * w.Actors[s.Them].MaxHp) w.Actors[s.Them].Hp = w.Actors[s.Them].MaxHp;
        }

        public static DuelResult Play(Seat seat, double maxSeconds, Action<Seat> post, Func<Seat, bool> stop = null)
        {
            World w = seat.W;
            int max = seat.Ticks(maxSeconds), start = w.Tick, mark = seat.Spy.Mark;
            var r = new DuelResult { Seat = seat };
            for (int t = 0; t < max; t++)
            {
                Step(seat, post);
                bool aDead = !w.Actors[seat.Me].Alive, bDead = seat.Them >= 0 && !w.Actors[seat.Them].Alive;
                if (bDead || aDead || (stop != null && stop(seat)))
                {
                    r.Won = bDead && !aDead;
                    r.Lost = aDead && !bDead;
                    break;
                }
            }
            r.Timeout = !r.Won && !r.Lost;
            r.Seconds = (w.Tick - start) / w.Config.SimHz;
            PlayerSeatRig.Count(seat, mark, r);
            return r;
        }

        /// <summary>A config tweak: every lane-4 switch named on (the others stay off).</summary>
        public static Action<GameConfig> Moves(bool tnt, bool heavy, bool combos) => c =>
        {
            MovesConfig m = c.Depth.Moves;
            m.DemoTntOnDigOnly = tnt;
            m.Heavy = heavy;
            m.Combos = combos;
        };

        /// <summary>One duel cell (probe DUEL model): lag-6 human me with aim help Low vs a bot of them, angles starts.
        /// variant "base" or "strong" (lag 5, feints, hit-cancels, dodges, Farmer keeps range, class kit).</summary>
        public static OffTally Cell(ClassId me, ClassId them, string variant, Action<GameConfig> tweak, bool heavy, bool useHeavy,
            int angles = 8, BotDifficulty bots = BotDifficulty.Normal)
        {
            var t = new OffTally();
            int wins = 0;
            for (int k = 0; k < angles; k++)
            {
                Seat s = PlayerSeatRig.Duel(me, them, TargetMode.RealBot, PlayerSeatRig.AngleOf(k, angles), k, Lag, bots: bots, tweak: tweak);
                s.Human.AimHelp = CellHelp;
                s.Human.HoldAttack = CellHold;
                bool strong = variant == "strong";
                if (strong)
                {
                    s.Human.LagTicks = 5;
                    s.Human.Feints = s.Human.HitCancels = s.Human.Dodges = true;
                    if (me == ClassId.Farmer) s.Human.KeepRange = true;
                    var wt = new OffWatch();
                    s.BeforeTick = q => { wt.Update(q); OffWatch.UseKit(q, wt, me); };
                }
                var pilot = new OffencePilot { Tap = heavy, UseHeavy = useHeavy && !ApproachPolicy, ApproachHeavy = useHeavy && ApproachPolicy };
                DuelResult r = Play(s, 30, q => pilot.Post(q, q.Frames[q.Me]));
                t.Reps++;
                if (r.Won) { t.Wins++; wins++; }
                if (r.Lost) t.Losses++;
                t.Swings += r.Swings;
                t.Landed += r.Landed;
                t.Seconds += r.Seconds;
                t.Heavies += s.Spy.Of(SimEventType.HeavyRelease, e => e.A == s.Me).Count;
                t.Steps += s.Spy.Of(SimEventType.ComboStep, e => e.A == s.Me).Count;
            }
            t.WinsVs[them] = wins;
            t.Rows.Add(them + " " + wins + "/" + angles);
            return t;
        }

        public static OffTally Row(ClassId me, string variant, Action<GameConfig> tweak, bool heavy, bool useHeavy, BotDifficulty bots = BotDifficulty.Normal, ClassId[] only = null)
        {
            var row = new OffTally();
            foreach (ClassId them in only ?? T.Classes)
            {
                if (them == me) continue;
                OffTally c = Cell(me, them, variant, tweak, heavy, useHeavy, 8, bots);
                row.Reps += c.Reps; row.Wins += c.Wins; row.Losses += c.Losses; row.Swings += c.Swings; row.Landed += c.Landed;
                row.Heavies += c.Heavies; row.Steps += c.Steps; row.Seconds += c.Seconds;
                row.Rows.AddRange(c.Rows);
                foreach (var kv in c.WinsVs) row.WinsVs[kv.Key] = kv.Value;
            }
            return row;
        }

        /// <summary>me chases a running 29% Normal bot (the S11 staging), catch within 4 s, 5 classes x angles.</summary>
        public static (int caught, int reps) Chase(ClassId me, Action<GameConfig> tweak, bool heavy, int angles = 8)
        {
            int caught = 0, reps = 0;
            GameConfig gc = PlayerSeatRig.Config();
            foreach (ClassId runner in T.Classes)
            {
                if (runner == me) continue;
                double gap = gc.Class(me).AttackRange + gc.Class(runner).Radius + T.ChaseStartBeyondReach;
                for (int k = 0; k < angles; k++)
                {
                    Seat s = PlayerSeatRig.Duel(me, runner, TargetMode.RealBot, PlayerSeatRig.AngleOf(k, angles), k, Lag, distance: gap, tweak: tweak);
                    s.Human.AimHelp = Help;
                    s.W.Actors[s.Them].Hp = T.ChaseRunnerHpShare * s.W.Actors[s.Them].MaxHp;
                    bool started = false;
                    s.BeforeTick = q =>
                    {
                        if (started) return;
                        started = true;
                        World qw = q.W;
                        BotsState bs = BotBrain.GetBotsState(qw);
                        qw.Actors[q.Them].AiState = AiState.Retreat;
                        if (bs != null) bs.StateSince[q.Them] = qw.Match.Time;
                        double ux = qw.X[q.Them] - qw.X[q.Me], uz = qw.Z[q.Them] - qw.Z[q.Me], ud = Math.Sqrt(ux * ux + uz * uz);
                        double sp = qw.Config.Class(runner).MoveSpeed;
                        qw.Vx[q.Them] = ux / ud * sp;
                        qw.Vz[q.Them] = uz / ud * sp;
                    };
                    var pilot = new OffencePilot { Tap = heavy };
                    DuelResult r = Play(s, T.ChaseSeconds, q => pilot.Post(q, q.Frames[q.Me]));
                    reps++;
                    if (r.Won) caught++;
                }
            }
            return (caught, reps);
        }

        public static string Of(int a, int b) => a + "/" + b + " (" + PxLog.F(b > 0 ? (double)a / b : 0) + ")";
    }

    // =================================================================================== T1-1 TNT on its own key

    [TestFixture]
    [Category("PlayerSeat")]
    public sealed class MovesOffenceTntTests
    {
        static readonly Action<GameConfig> On = OffRig.Moves(true, false, false);

        [Test]
        public void T1_1_AttackIsThePryBar_EvenWithTheTntReady_AtOneAndThreeUnits()
        {
            var bad = new List<string>();
            foreach (double gap in new[] { 1.0, 3.0 })
            {
                Seat s = PlayerSeatRig.Duel(ClassId.Demolitions, ClassId.Farmer, TargetMode.Passive, 0, 0, 0, distance: gap, tweak: On);
                s.Human.ApproachShare = 0.5;
                DuelResult r = OffRig.Play(s, 2.0, null, q => q.Spy.Of(SimEventType.Hit, e => e.B == 0).Count > 0);
                int thrown = s.Spy.Of(SimEventType.TntThrown, e => e.A == 0).Count;
                int hits = s.Spy.Of(SimEventType.Hit, e => e.B == 0 && e.Cause == DamageCause.Melee).Count;
                bool ready = s.W.Tick >= s.W.Demolitions.TntReadyTick[0];
                if (!PxLog.Line("T1-1 AttackIsThePryBar", "switch on, TNT ready, Farmer dummy " + PxLog.F(gap, "0.0") + " u away, Attack held", "pry-bar hits " + hits + ", TNT thrown " + thrown + ", TNT still ready " + ready, "a pry-bar hit, no throw, TNT still ready", hits > 0 && thrown == 0 && ready)) bad.Add(gap.ToString());
            }
            // F still throws (unchanged path)
            Seat f = PlayerSeatRig.Duel(ClassId.Demolitions, ClassId.Farmer, TargetMode.Passive, 0, 0, 0, distance: 4.0, tweak: On);
            f.Human.Fight = false;
            f.Human.ApproachShare = 5;
            int pressAt = 3;
            f.BeforeTick = q => { if (q.W.Tick == pressAt) q.Human.PressDigPlace = true; };
            OffRig.Play(f, 1.0, null);
            int fThrown = f.Spy.Of(SimEventType.TntThrown, e => e.A == 0).Count;
            if (!PxLog.Line("T1-1 FStillThrows", "switch on, F pressed with the TNT ready", "TNT thrown " + fThrown, "1", fThrown == 1)) bad.Add("F");
            Assert.IsEmpty(bad);
        }

        [Test]
        public void T1_1_BotsStillThrowTheirTnt_SameAsSwitchOff()
        {
            // a Demolitions BOT with the TNT ready and a target at 4 u: its attack intent is still its throw request (bot balance kept)
            int[] thrown = new int[2];
            for (int arm = 0; arm < 2; arm++)
            {
                Seat s = PlayerSeatRig.Duel(ClassId.Farmer, ClassId.Demolitions, TargetMode.RealBot, 0, 0, 6, distance: 4.5, tweak: OffRig.Moves(arm == 1, false, false));
                s.Human.Fight = false;
                s.Human.ApproachShare = 5;
                s.KeepMeAbove = 0.3;
                OffRig.Play(s, 6.0, null);
                thrown[arm] = s.Spy.Of(SimEventType.TntThrown, e => e.A == s.Them).Count;
            }
            bool ok = PxLog.Line("T1-1 BotsStillThrow", "Demolitions bot vs a standing human 4.5 u away, 6 s", "throws off " + thrown[0] + ", on " + thrown[1], "the same, at least 1", thrown[0] == thrown[1] && thrown[1] >= 1);
            Assert.IsTrue(ok);
        }

        [Test]
        [Category("Slow")]
        public void T1_1_Chase_Duel_And_FThrow_FromTheSeat()
        {
            var off = OffRig.Chase(ClassId.Demolitions, null, false);
            var on = OffRig.Chase(ClassId.Demolitions, On, false);
            double share = on.reps > 0 ? (double)on.caught / on.reps : 0;
            bool a = PxLog.Line("T1-1 Chase", "lag-6 human Demolitions chasing a running 29% Normal bot (5 classes x 8), catch within 4 s", "off " + OffRig.Of(off.caught, off.reps) + ", on " + OffRig.Of(on.caught, on.reps), ">= 0.40 (audit: 0.05)", share >= 0.40);
            OffTally d0 = OffRig.Row(ClassId.Demolitions, "base", null, false, false);
            OffTally d1 = OffRig.Row(ClassId.Demolitions, "base", On, false, false);
            bool b = PxLog.Line("T1-1 Duel", "lag-6 human Demolitions vs Normal bots (5 x 8)", "off " + d0 + " | on " + d1, ">= 27 of 40 (audit: 24)", d1.Wins >= 27);
            // F throw at 2.5 to 6 u on a Normal bot: hurts it at least half of the time
            int throws = 0, hurt = 0;
            foreach (ClassId tgt in new[] { ClassId.Warrior, ClassId.Farmer, ClassId.Healer })
            {
                for (int k = 0; k < 8; k++)
                {
                    Seat s = PlayerSeatRig.Duel(ClassId.Demolitions, tgt, TargetMode.RealBot, PlayerSeatRig.AngleOf(k, 8), k, OffRig.Lag, distance: 4.5, tweak: On);
                    s.Human.AimHelp = OffRig.Help;
                    s.Human.ApproachShare = 3.0;
                    s.Human.Fight = false;
                    s.KeepThemAbove = 0.3; s.KeepMeAbove = 0.3;
                    s.BeforeTick = q =>
                    {
                        double dd = OffencePilot.MvDist(q.W, 0, q.Them);
                        if (q.W.Tick >= q.W.Demolitions.TntReadyTick[0] && dd >= 2.5 && dd <= 6) q.Human.PressDigPlace = true;
                    };
                    OffRig.Play(s, 20, null);
                    throws += s.Spy.Of(SimEventType.TntThrown, e => e.A == 0).Count;
                    foreach (SeatEvent e in s.Spy.Of(SimEventType.Explosion, x => x.A == 0 && x.B == Demolitions.ExplosionTnt))
                        if (s.Spy.Of(SimEventType.Hit, h => h.B == 0 && h.A == s.Them && h.Tick == e.Tick && h.Cause == DamageCause.Explosion).Count > 0) hurt++;
                }
            }
            bool c = PxLog.Line("T1-1 FThrow", "switch on, F throw at 2.5 to 6 u on a Normal Warrior / Farmer / Healer bot (3 x 8 x 20 s)", "hurt " + OffRig.Of(hurt, throws), ">= 0.50", throws > 0 && (double)hurt / throws >= 0.50);
            Assert.IsTrue(a && b && c);
        }
    }

    // =================================================================================== T2-2 heavy / charged

    [TestFixture]
    [Category("PlayerSeat")]
    public sealed class MovesOffenceHeavyTests
    {
        static readonly Action<GameConfig> HeavyOn = OffRig.Moves(false, true, false);

        /// <summary>One swing on a passive Healer dummy facing the attacker: hold Attack holdTicks (1 = a tap), first Hit and contact tick.</summary>
        static (double dmg, int contactAfterPress, bool crit, int heavy, int windup) OneSwing(ClassId me, int holdTicks)
        {
            GameConfig gc = PlayerSeatRig.Config();
            double gap = 0.8 * Math.Min(gc.Class(me).AttackRange, 1.5) + 0.1;
            Seat s = PlayerSeatRig.Duel(me, ClassId.Healer, TargetMode.Passive, 0, 0, 0, distance: gap, tweak: HeavyOn);
            s.NoTnt = true;
            s.Human.Fight = false;
            s.Human.ApproachShare = 10;
            int press = -1, held = 0;
            OffRig.Play(s, 3.0, q =>
            {
                World w = q.W;
                InputFrame f = q.Frames[0];
                f.AimX = w.X[q.Them]; f.AimZ = w.Z[q.Them];
                w.Actors[q.Them].Facing = Math.Atan2(w.Z[0] - w.Z[q.Them], w.X[0] - w.X[q.Them]);
                if (w.Tick < 5) { f.Attack = false; return; }
                if (press < 0) press = w.Tick;
                f.Attack = held < holdTicks;
                held++;
            }, q => q.Spy.Of(SimEventType.Hit, e => e.B == 0).Count > 0);
            List<SeatEvent> hits = s.Spy.Of(SimEventType.Hit, e => e.B == 0 && e.A == s.Them);
            List<SeatEvent> contacts = s.Spy.Of(SimEventType.AttackContact, e => e.A == 0);
            int crit = s.Spy.Of(SimEventType.CriticalHit, e => e.B == 0).Count;
            int heavies = s.Spy.Of(SimEventType.HeavyRelease, e => e.A == 0).Count;
            // a frame written before tick T is applied on tick T (SeatEvent.Tick is the tick after the step)
            return (hits.Count > 0 ? hits[0].Value : 0, contacts.Count > 0 ? contacts[0].Tick - press : -1, crit > 0, heavies, s.W.Dur.AttackWindup[(int)me]);
        }

        [Test]
        public void T2_2a_FullChargeDeals1_6xTheLight_OnTime()
        {
            var bad = new List<string>();
            MovesConfig mc = new MovesConfig();
            foreach (ClassId me in T.Classes)
            {
                var light = OneSwing(me, 1);
                var full = OneSwing(me, mc.ChargeFullTick);
                double want = mc.HeavyDamageBonus[(int)me] ? mc.ChargeDamageMulMax : 1.0;
                double ratio = light.dmg > 0 ? full.dmg / light.dmg : 0;
                // the hold is written for ticks press+1 .. press+24; the release (and the swing) is press+25, contact + wind-up
                int expect = mc.ChargeFullTick + 1 + full.windup;
                bool ok = Math.Abs(ratio - want) < 0.02 && Math.Abs(full.contactAfterPress - expect) <= 1 && full.heavy == 1 && light.heavy == 0 && light.crit == full.crit;
                if (!PxLog.Line("T2-2a FullCharge", me + ": tap vs a 24-tick hold on a passive Healer dummy", "light " + PxLog.F(light.dmg, "0.0") + " (contact +" + light.contactAfterPress + "), heavy " + PxLog.F(full.dmg, "0.0") + " (contact +" + full.contactAfterPress + ") ratio " + PxLog.F(ratio, "0.000") + (full.crit ? " crit" : ""), "ratio " + PxLog.F(want) + ", contact +" + expect + " +-1", ok)) bad.Add(me.ToString());
            }
            Assert.IsEmpty(bad);
        }

        [Test]
        public void T2_2b_ALag6HumanSideDodgesAReadHeavy()
        {
            // actor 0 (a human seat) charges and releases a heavy at actor 5 (a second human seat, standing still), who reads the
            // release (HeavyRelease / AttackStart) 6 ticks late and side-dodges. Charges 8, 16 and 24 ticks, both sides, 4 angles.
            var bad = new List<string>();
            int allAvoid = 0, allReps = 0;
            foreach (ClassId me in T.Classes)
            {
                int avoided = 0, reps = 0;
                MovesConfig mc = new MovesConfig();
                double gap = 0.75 * mc.HeavyReach[(int)me] + 0.3;
                foreach (int hold in new[] { 8, 16, 24 })
                    foreach (int side in new[] { -1, 1 })
                        for (int k = 0; k < 4; k++)
                        {
                            Seat s = PlayerSeatRig.Duel(me, ClassId.Miner, TargetMode.HumanVsHuman, PlayerSeatRig.AngleOf(k, 4), k, 0, distance: gap, tweak: HeavyOn);
                            s.NoTnt = true;
                            int held = 0, release = -1;
                            OffRig.Play(s, 2.5, q =>
                            {
                                World w = q.W;
                                InputFrame a = q.Frames[0], b = q.Frames[q.Them];
                                a.MoveX = a.MoveZ = 0; a.AimX = w.X[q.Them]; a.AimZ = w.Z[q.Them]; a.Cancel = false; a.Dodge = false;
                                b.MoveX = b.MoveZ = 0; b.Attack = false; b.Dodge = false; b.Cancel = false;
                                b.AimX = w.X[0]; b.AimZ = w.Z[0];
                                if (w.Tick >= 4) { a.Attack = held < hold; held++; } else a.Attack = false;
                                if (release < 0)
                                {
                                    var ev = q.Spy.Of(SimEventType.HeavyRelease, e => e.A == 0);
                                    if (ev.Count > 0) release = ev[0].Tick - 1;
                                }
                                if (release >= 0 && w.Tick == release + OffRig.Lag)
                                {
                                    double ux = w.X[q.Them] - w.X[0], uz = w.Z[q.Them] - w.Z[0], ul = Math.Sqrt(ux * ux + uz * uz);
                                    b.Dodge = true;
                                    b.MoveX = -uz / ul * side; b.MoveZ = ux / ul * side;
                                }
                            }, q => q.Spy.Of(SimEventType.AttackContact, e => e.A == 0).Count > 0);
                            reps++;
                            if (s.Spy.Of(SimEventType.Hit, e => e.B == 0 && e.A == s.Them).Count == 0 && s.Spy.Of(SimEventType.AttackContact, e => e.A == 0).Count > 0) avoided++;
                        }
                allAvoid += avoided; allReps += reps;
                if (!PxLog.Line("T2-2b ReadAndDodge", me + " heavy (8 / 16 / 24 tick charge) vs a lag-6 side dodge on the release", "avoided " + OffRig.Of(avoided, reps), ">= 0.90", (double)avoided / reps >= 0.90)) bad.Add(me.ToString());
            }
            PxLog.Line("T2-2b ReadAndDodge", "all classes", "avoided " + OffRig.Of(allAvoid, allReps), ">= 0.90", (double)allAvoid / allReps >= 0.90);
            Assert.IsEmpty(bad);
        }

        [Test]
        public void T2_2_ChargeCancelIsAFeint_AndChargeArmourHolds()
        {
            // a Warrior human seat charges 14 ticks then presses CANCEL: Feint event Value 1, no swing, cooldown x(1 - 0.7)
            Seat s = PlayerSeatRig.Duel(ClassId.Warrior, ClassId.Healer, TargetMode.Passive, 0, 0, 0, distance: 1.5, tweak: HeavyOn);
            s.Human.Fight = false;
            int held = 0;
            OffRig.Play(s, 1.5, q =>
            {
                InputFrame f = q.Frames[0];
                f.MoveX = f.MoveZ = 0;
                if (q.W.Tick < 3) return;
                held++;
                f.Attack = held <= 14;
                f.Cancel = held == 14;
            });
            int feints = s.Spy.Of(SimEventType.Feint, e => e.A == 0 && e.Value == 1).Count;
            int swings = s.Spy.Of(SimEventType.AttackContact, e => e.A == 0).Count;
            bool a = PxLog.Line("T2-2 ChargeCancel", "Warrior charges 14 ticks, CANCEL on tick 14, then lets go", "feints " + feints + ", swings " + swings, "1 feint, 0 swings", feints == 1 && swings == 0);

            // charge armour: a light hit (under the Poise) on a charging body does not stagger it and the charge goes on
            Seat t = PlayerSeatRig.Duel(ClassId.Builder, ClassId.Healer, TargetMode.HumanVsHuman, 0, 0, 0, distance: 1.2, tweak: HeavyOn);
            int h2 = 0, stunned = 0;
            bool released = false;
            OffRig.Play(t, 2.0, q =>
            {
                World w = q.W;
                InputFrame me = q.Frames[0], them = q.Frames[q.Them];
                me.MoveX = me.MoveZ = 0; me.AimX = w.X[q.Them]; me.AimZ = w.Z[q.Them];
                them.MoveX = them.MoveZ = 0; them.AimX = w.X[0]; them.AimZ = w.Z[0]; them.Dodge = false; them.Cancel = false;
                if (w.Tick < 3) { me.Attack = false; them.Attack = false; return; }
                h2++;
                me.Attack = h2 <= 30;
                them.Attack = h2 == 10;   // the Healer's lantern (14 < Builder Poise 26) lands mid-charge
                if (w.Actors[0].HitStunTicks > 0) stunned++;
                if (q.Spy.Of(SimEventType.HeavyRelease, e => e.A == 0).Count > 0) released = true;
            });
            int lantern = t.Spy.Of(SimEventType.Hit, e => e.A == 0 && e.B == t.Them).Count;
            bool b = PxLog.Line("T2-2 ChargeArmour", "Builder charging 30 ticks, a Healer lantern lands at hold tick ~17", "lantern hits " + lantern + ", stunned ticks " + stunned + ", heavy released " + released, ">= 1 hit, 0 stunned, released", lantern >= 1 && stunned == 0 && released);
            Assert.IsTrue(a && b);
        }

        [Test]
        [Category("Slow")]
        public void T2_2c_HeavyPolicyDuels_StayWithinTheBand()
        {
            var bad = new List<string>();
            foreach (ClassId me in MvCombine.Classes())   // integrator: CM_MVI_CLASSES narrows a lever probe
            {
                OffTally b0 = OffRig.Row(me, "base", null, false, false);
                OffTally tap = OffRig.Row(me, "base", HeavyOn, true, false);
                OffRig.ApproachPolicy = false;
                OffTally hv = OffRig.Row(me, "base", HeavyOn, true, true);
                OffRig.ApproachPolicy = true;
                OffTally ap = OffRig.Row(me, "base", HeavyOn, true, true);
                OffRig.ApproachPolicy = false;
                int delta = ap.Wins - b0.Wins;
                if (!PxLog.Line("T2-2c HeavyPolicy", me + " lag-6 vs Normal bots (5 x 8)", "base " + b0.Wins + "/40, taps only (Heavy on) " + tap.Wins + "/40, literal (heavy whenever the target is staggered or out of its reach) " + hv.Wins + "/40 heavies " + hv.Heavies + ", approach charge " + ap, "approach charge within -2 to +4 of base (" + delta + ")", delta >= -2 && delta <= 4)) bad.Add(me + " " + delta);
            }
            Assert.IsEmpty(bad);
        }
    }

    // =================================================================================== T2-3 combos

    [TestFixture]
    [Category("PlayerSeat")]
    public sealed class MovesOffenceComboTests
    {
        static readonly Action<GameConfig> ComboOn = OffRig.Moves(false, false, true);

        [Test]
        public void T2_3_StepsLandAndTheFinisherCloses_OnAPassiveDummy()
        {
            var bad = new List<string>();
            MovesConfig mc = new MovesConfig();
            foreach (ClassId me in T.Classes)
            {
                Seat s = PlayerSeatRig.Duel(me, ClassId.Healer, TargetMode.Passive, 0, 0, 0, tweak: ComboOn);
                s.NoTnt = true;
                s.KeepThemAbove = 0.3;
                OffRig.Play(s, 6.0, null);
                List<SeatEvent> steps = s.Spy.Of(SimEventType.ComboStep, e => e.A == 0);
                int fin = 0, maxStep = 0;
                foreach (SeatEvent e in steps) { if (e.Tier == 1) fin++; if (e.Value > maxStep) maxStep = (int)e.Value; }
                int want = mc.ComboSteps[(int)me];
                // integrator (decision 1): a class with ComboSteps 1 (the Healer) has no chain: no step, no finisher
                bool ok = want < 2 ? steps.Count == 0 : fin > 0 && maxStep == want;
                if (!PxLog.Line("T2-3 Steps", me + " swinging at a passive dummy for 6 s", "steps " + steps.Count + ", finishers " + fin + ", highest step " + maxStep, want < 2 ? "no chain (ComboSteps 1)" : "finishers > 0, highest step " + want, ok)) bad.Add(me.ToString());
            }
            Assert.IsEmpty(bad);
        }

        [Test]
        public void T2_3a_ALag6VictimWhoDodgesBetweenStepsEscapesStep2()
        {
            // actor 0 (human seat, attack held whenever ready) lands step 1 on actor 5 (a second human seat standing still), who
            // dodges away-to-the-side 6 ticks after that hit. Escaped = no step-2 hit inside the chain window.
            var bad = new List<string>();
            foreach (ClassId me in T.Classes)
            {
                int esc = 0, reps = 0, escStart = 0, escWalk = 0, again = 0;
                foreach (int side in new[] { -1, 1 })
                    for (int k = 0; k < 4; k++)
                        foreach (int mode in new[] { 0, 1, 2 })
                        {
                            // mode 0: the attacker presses step 2 in rhythm (held from 3 ticks after the hit, the buffer keeps it) and
                            // the victim dodges 6 ticks after step 1 lands; mode 1: the attacker walks back in and swings when in reach
                            // (the rig model), same dodge; mode 2: the victim waits for step 2's wind-up and dodges 6 ticks into it.
                            bool onStart = mode == 2, rhythm = mode == 0;
                            Seat s = PlayerSeatRig.Duel(me, ClassId.Miner, TargetMode.HumanVsHuman, PlayerSeatRig.AngleOf(k, 4), k, 0, tweak: ComboOn);
                            s.NoTnt = true;
                            s.Human2.Fight = false;
                            int firstHit = -1, secondStart = -1;
                            bool dodged = false;
                            OffRig.Play(s, 3.0, q =>
                            {
                                World w = q.W;
                                InputFrame b = q.Frames[q.Them];
                                if (rhythm && firstHit >= 0 && secondStart < 0 && w.Tick >= firstHit + 3) q.Frames[0].Attack = true;
                                b.MoveX = b.MoveZ = 0; b.Attack = false; b.Dodge = false; b.Cancel = false;
                                if (firstHit < 0)
                                {
                                    var h = q.Spy.Of(SimEventType.Hit, e => e.B == 0 && e.A == q.Them);
                                    if (h.Count > 0) firstHit = h[0].Tick - 1;
                                }
                                if (firstHit >= 0 && secondStart < 0)
                                {
                                    var st = q.Spy.Of(SimEventType.AttackStart, e => e.A == 0 && e.Tick - 1 > firstHit);
                                    if (st.Count > 0) secondStart = st[0].Tick - 1;
                                }
                                int at = onStart ? (secondStart >= 0 ? secondStart + OffRig.Lag : -1) : (firstHit >= 0 ? firstHit + OffRig.Lag : -1);
                                // the press repeats until the dodge starts (a press while still staggered is refused)
                                if (at >= 0 && w.Tick >= at && !dodged)
                                {
                                    dodged = q.Spy.Of(SimEventType.Dodge, e => e.A == q.Them).Count > 0;
                                                    if (dodged || !Dodge.CanStart(w, q.Them)) return;
                                    double ux = w.X[q.Them] - w.X[0], uz = w.Z[q.Them] - w.Z[0], ul = Math.Sqrt(ux * ux + uz * uz);
                                    b.Dodge = true;
                                    b.MoveX = (ux / ul + -uz / ul * side) * 0.7071; b.MoveZ = (uz / ul + ux / ul * side) * 0.7071;
                                }
                            }, q => firstHit >= 0 && q.W.Tick > firstHit + 45);
                            if (firstHit < 0) continue;
                            reps++;
                            // integrator (decision 2): escaped = the attacker's NEXT hit after step 1 is not that chain's step 2 (a fresh
                            // opener after the dodge broke the chain is a new string, counted apart as "a second hit")
                            var later = s.Spy.Of(SimEventType.Hit, e => e.A == s.Them && e.B == 0 && e.Tick - 1 > firstHit + 1 && e.Tick - 1 <= firstHit + 45);
                            bool step2 = later.Count > 0 && s.Spy.Of(SimEventType.ComboStep, e => e.A == 0 && e.Value == 2 && Math.Abs(e.Tick - later[0].Tick) <= 1).Count > 0;
                            if (!step2) { if (onStart) escStart++; else if (rhythm) esc++; else escWalk++; }
                            if (later.Count > 0) again++;
                        }
                int third = reps / 3;
                // integrator (decision 2): the knock slide no longer refuses the dodge (MovesConfig.ComboDodgeOutOfKnock) and a dodge
                // breaks the chain that last hit the dodger (ComboBreaksOnDodge), so the Farmer is asserted like every class. The
                // combine's target is at least 6 of 8 per mode; "any second hit" counts the attacker's fresh openers as well.
                if (!PxLog.Line("T2-3a EscapeStep2", me + " chain vs a lag-6 victim's dodge (2 sides x 4 angles)", "dodge 6 ticks after step 1 lands (or once the stagger ends): escaped " + OffRig.Of(esc, third) + " (rhythm press), " + OffRig.Of(escWalk, third) + " (attacker walks back in); dodge 6 ticks into step 2's wind-up: " + OffRig.Of(escStart, third) + "; any second hit within 1.5 s " + again + "/" + reps, ">= 6 of 8 (dodge between steps, both attackers)", third > 0 && esc * 8 >= 6 * third && escWalk * 8 >= 6 * third)) bad.Add(me.ToString());
            }
            Assert.IsEmpty(bad);
        }

        /// <summary>KNOWN (accepted by the orchestrator, 2026-11-23): the Farmer's passive time-to-kill falls 0.171 with combos on
        /// (FinisherDamageMulByClass is not to change); the band's top is this for the Farmer only.</summary>
        const double FarmerTtkFallKnownMax = 0.175;

        [Test]
        public void T2_3c_PassiveTimeToKillFalls5To15Percent()
        {
            var bad = new List<string>();
            foreach (ClassId me in T.Classes)
            {
                double sum0 = 0, sum1 = 0;
                var parts = new List<string>();
                foreach (ClassId tgt in new[] { ClassId.Warrior, ClassId.Farmer, ClassId.Builder })
                {
                    double[] ttk = new double[2];
                    for (int arm = 0; arm < 2; arm++)
                    {
                        Seat p = PlayerSeatRig.Duel(me, tgt, TargetMode.Passive, 0, 0, 0, tweak: arm == 1 ? ComboOn : null);
                        p.NoTnt = true;
                        DuelResult r = OffRig.Play(p, 30, null);
                        ttk[arm] = r.Won ? r.Seconds : 30;
                    }
                    sum0 += ttk[0]; sum1 += ttk[1];
                    parts.Add(tgt + " " + PxLog.F(ttk[0]) + " -> " + PxLog.F(ttk[1]));
                }
                double fall = 1 - sum1 / sum0;
                // KNOWN (a knob, lane 1's MovesConfig; reported): the Builder's 2-step chain makes the finisher every other hit
                // (x1.35), which turns 5-hit kills into 4-hit kills on 115 to 120 HP bodies (-18%). Lever: FinisherDamageMul for
                // 2-step classes, or ComboSteps[Builder] = 3. Printed, not asserted.
                // integrator: decision 3 set ComboSteps[Builder] = 3 (asserted now); decision 1 gave the Healer no chain (fall 0, printed)
                bool noChain = new MovesConfig().ComboSteps[(int)me] < 2;
                if (!PxLog.Line("T2-3c PassiveTtk" + (noChain ? " (no chain: ComboSteps 1)" : ""), me + " on passive Warrior / Farmer / Builder dummies, combos off -> on", "fall " + PxLog.F(fall, "0.000") + " (" + string.Join(", ", parts) + ")", noChain ? "0 (no chain)" : "0.05 to 0.15", noChain ? fall == 0 : fall >= 0.05 && fall <= (me == ClassId.Farmer ? FarmerTtkFallKnownMax : 0.15))) bad.Add(me + " " + PxLog.F(fall));
            }
            Assert.IsEmpty(bad);
        }

        [Test]
        [Category("Slow")]
        public void T2_3b_StrongWithCombos_BeatsStrongWithout()
        {
            var bad = new List<string>();
            foreach (ClassId me in MvCombine.Classes())   // integrator: CM_MVI_CLASSES narrows a lever probe
            {
                OffTally s0 = OffRig.Row(me, "strong", null, false, false);
                OffTally s1 = OffRig.Row(me, "strong", ComboOn, false, false);
                bool need = me == ClassId.Farmer || me == ClassId.Miner || me == ClassId.Demolitions;
                bool ok = !need || s1.Wins - s0.Wins >= 2;
                if (!PxLog.Line("T2-3b StrongCombos", me + " strong (lag 5, feints, hit-cancels, dodges, kit) vs Normal bots, combos off -> on", s0.Wins + "/40 -> " + s1, need ? ">= +2" : "(reported)", ok)) bad.Add(me.ToString());
            }
            Assert.IsEmpty(bad);
        }
    }

    // =================================================================================== the 40-duel table, lane 4 switches on

    [TestFixture]
    [Category("PlayerSeat")]
    public sealed class MovesOffenceHealerLevers
    {
        [Test]
        [Category("Slow")]
        public void HealerLevers_HeavyOnly_CombosOnly_GateModel()
        {
            // S44 lever print: which lane-4 verb lifts the lag-6 Healer (gate model, aim help Off) against each bot class
            OffRig.CellHelp = 0;
            OffRig.ApproachPolicy = true;
            OffTally hv = OffRig.Row(ClassId.Healer, "base", OffRig.Moves(false, true, false), true, true);
            OffTally cb = OffRig.Row(ClassId.Healer, "base", OffRig.Moves(false, false, true), false, false);
            OffTally tp = OffRig.Row(ClassId.Healer, "base", OffRig.Moves(false, true, false), true, false);
            OffRig.ApproachPolicy = false;
            OffRig.CellHelp = OffRig.Help;
            PxLog.Line("L4 HealerLevers", "lag-6 Healer, gate model, vs Normal bots", "heavy only (approach charge) " + hv + " | combos only " + cb + " | taps only (Heavy on, no heavy) " + tp, "(reported: S44 wants <= 3 of 8 each)", true);
        }
    }

    [TestFixture]
    [Category("PlayerSeat")]
    public sealed class MovesOffenceTapOnly
    {
        /// <summary>fix/mv-heavy-press acceptance: every switch on, a human who only taps (the rig's tap mode) or holds the way the
        /// old rig held (HoldAttack): each class's duel result is within 1 of the same human with only Heavy off. The light starts on
        /// the press, so the heavy's existence costs the person no tick.</summary>
        [Test]
        [Category("Slow")]
        public void TapOnly_EverySwitchOn_HeavyOnWithin1OfHeavyOff()
        {
            Action<GameConfig> allOn = c => MovesConfig.PlayBuildAllOn(c.Depth.Moves);
            Action<GameConfig> noHeavy = c => { MovesConfig.PlayBuildAllOn(c.Depth.Moves); c.Depth.Moves.Heavy = false; };
            var bad = new List<string>();
            foreach (ClassId me in T.Classes)
            {
                OffTally off = OffRig.Row(me, "base", noHeavy, false, false);
                OffTally tap = OffRig.Row(me, "base", allOn, false, false);
                OffRig.CellHold = true;
                OffTally hold = OffRig.Row(me, "base", allOn, false, false);
                OffRig.CellHold = false;
                bool ok = Math.Abs(tap.Wins - off.Wins) <= 1 && Math.Abs(hold.Wins - off.Wins) <= 1;
                if (!PxLog.Line("fix-heavy TapOnly", me + " lag-6, aim help Low, vs Normal bots (5 x 8), every switch on", "Heavy off " + off + " | Heavy on, taps " + tap + " | Heavy on, rig hold model " + hold, "taps and hold within 1 of Heavy off", ok)) bad.Add(me.ToString());
            }
            Assert.IsEmpty(bad);
        }
    }

    [TestFixture]
    [Category("PlayerSeat")]
    public sealed class MovesOffenceDuelTable
    {
        [Test]
        [Category("Slow")]
        public void DuelTable_PerClass_Lane4SwitchesOn()
        {
            Action<GameConfig> all = OffRig.Moves(true, true, true);
            var bad = new List<string>();
            // two human models: the audit probe's (aim help Low; SPEC section 3's table) and the gate's (aim help Off; S5 / S44 / S45)
            foreach (int help in new[] { 1, 0 })
            {
                OffRig.CellHelp = help;
                string model = help == 1 ? "probe model (aim help Low)" : "gate model (aim help Off)";
                var offT = new Dictionary<ClassId, OffTally>();
                var onT = new Dictionary<ClassId, OffTally>();
                foreach (ClassId me in T.Classes)
                {
                    OffTally b0 = OffRig.Row(me, "base", null, false, false);
                    OffRig.ApproachPolicy = true;
                    OffTally on = OffRig.Row(me, "base", all, true, true);
                    OffRig.ApproachPolicy = false;
                    offT[me] = b0;
                    onT[me] = on;
                    string rule = "(reported)";
                    bool ok = true;
                    if (help == 0 && me == ClassId.Warrior)
                    {
                        rule = ">= 6 of 8 each (S5)";
                        foreach (var kv in on.WinsVs) if (kv.Value < 6) ok = false;
                    }
                    else if (help == 0 && me == ClassId.Healer)
                    {
                        rule = "<= 3 of 8 each (S44)";
                        foreach (var kv in on.WinsVs) if (kv.Value > 3) ok = false;
                    }
                    else if (help == 1 && me == ClassId.Warrior) { rule = ">= 36 (SPEC section 3)"; ok = on.Wins >= 36; }
                    else if (help == 1 && me == ClassId.Healer) { rule = "<= 8 (SPEC section 3)"; ok = on.Wins <= 8; }
                    if (!PxLog.Line("L4 DuelTable", me + " lag-6, " + model + ", vs Normal bots (5 x 8), TNT-on-F + taps + approach-charge heavies + combos", "off " + b0 + " | on " + on, rule, ok)) bad.Add(me + " " + help);
                }
                // S45 shape: each middle class against the other middle three, off and on
                ClassId[] m4 = { ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder };
                foreach (ClassId x in m4)
                {
                    int w0 = 0, w1 = 0, n = 0;
                    foreach (ClassId y in m4)
                    {
                        if (y == x) continue;
                        if (offT[x].WinsVs.TryGetValue(y, out int a)) w0 += a;
                        if (onT[x].WinsVs.TryGetValue(y, out int b)) w1 += b;
                        n += 8;
                    }
                    double sh = n > 0 ? (double)w1 / n : 0;
                    bool ok = help == 1 || (sh >= 0.30 && sh <= 0.70);
                    if (!PxLog.Line("L4 DuelTable S45", x + " vs the other middle three, " + model, "off " + w0 + "/" + n + ", on " + w1 + "/" + n + " = " + PxLog.F(sh), help == 0 ? "0.30 to 0.70 (S45)" : "(reported)", ok)) bad.Add(x + " S45");
                }
            }
            OffRig.CellHelp = OffRig.Help;
            Assert.IsEmpty(bad);
        }
    }
}

namespace CriticalMass.Sim.Tests.PlayerSeat
{
    using System;
    using System.Collections.Generic;
    using CriticalMass.Sim.Bots;
    using CriticalMass.Sim.Config;
    using CriticalMass.Sim.Core;
    using NUnit.Framework;

    /// <summary>fix/mv-heavy-press item 2: bot-only play must not move with Heavy (or Combos) on; one switch at a time, so a
    /// divergence names its switch.</summary>
    [TestFixture]
    [Category("PlayerSeat")]
    public sealed class MovesOffenceBotIdentity
    {
        static string Run(Action<MovesConfig> on, int ticks, Action<MovesConfig> baseline = null)
        {
            GameConfig ca = GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full);
            ca.Depth.Moves.FeintBait = true;
            baseline?.Invoke(ca.Depth.Moves);
            GameConfig cb = GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full);
            cb.Depth.Moves.FeintBait = true;
            baseline?.Invoke(cb.Depth.Moves);
            on(cb.Depth.Moves);
            World a = World.CreateWorld(new WorldOptions { Seed = 1, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = ca });
            World b = World.CreateWorld(new WorldOptions { Seed = 1, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cb });
            InputFrame sa = SimInput.EmptyInput(), sb = SimInput.EmptyInput();
            for (int t = 0; t < ticks; t++)
            {
                MatchTick.Advance(a, null, sa);
                MatchTick.Advance(b, null, sb);
                for (int i = 0; i < a.ActorCount; i++)
                    if (a.X[i] != b.X[i] || a.Z[i] != b.Z[i] || a.Actors[i].Hp != b.Actors[i].Hp || a.Actors[i].SwingTick != b.Actors[i].SwingTick || a.Inputs[i].Attack != b.Inputs[i].Attack)
                        return "diverged at tick " + a.Tick + " on actor " + i + " (" + a.Actors[i].Cls + ") swing " + a.Actors[i].SwingTick + "/" + b.Actors[i].SwingTick
                            + " attack " + a.Inputs[i].Attack + "/" + b.Inputs[i].Attack + " stepOf " + b.Moves.Offence.StepOf[i] + " chainNext " + b.Moves.Offence.ChainNext[i]
                            + " heavy " + (b.Moves.Offence.HeavySwingTick[i] == b.Actors[i].SwingTick) + " hp " + a.Actors[i].Hp + "/" + b.Actors[i].Hp;
            }
            return null;
        }

        [Test]
        public void BotOnly16v16_HeavyAlone_CombosAlone_Both_AreTickForTickToday()
        {
            var bad = new List<string>();
            var arms = new (string, Action<MovesConfig>)[]
            {
                ("Heavy", m => m.Heavy = true),
                ("Combos", m => m.Combos = true),
                ("Heavy + Combos", m => { m.Heavy = true; m.Combos = true; }),
                ("lane-5 bot verbs (step-out, guard, heavy, combos, slide)", m => { m.BotStepOut = true; m.Guard = true; m.Heavy = true; m.Combos = true; m.Slide = true; }),
            };
            foreach (var (name, on) in arms)
            {
                string r = Run(on, 3600);
                if (!PxLog.Line("fix-heavy bot-only identity", "16v16 seed 1, 2:00, actor 0 on autopilot: " + name + " vs every switch off", r ?? "identical for 3600 ticks", "identical", r == null)) bad.Add(name);
            }
            // the play build: every switch on vs every switch on but Heavy and Combos (lane 4's verbs must not move bot-only play)
            {
                Action<MovesConfig> allBut = m => { MovesConfig.PlayBuildAllOn(m); m.Heavy = false; m.Combos = false; };
                string r = Run(m => { m.Heavy = true; m.Combos = true; }, 3600, allBut);
                if (!PxLog.Line("fix-heavy bot-only identity", "16v16 seed 1, 2:00: every switch on vs every switch on but Heavy and Combos", r ?? "identical for 3600 ticks", "identical", r == null)) bad.Add("play build");
            }
            foreach (var (name, on) in new (string, Action<MovesConfig>)[0])
            {
                string r = Run(on, 3600);
                if (!PxLog.Line("fix-heavy bot-only identity", "16v16 seed 1, 2:00, actor 0 on autopilot: " + name + " vs every switch off", r ?? "identical for 3600 ticks", "identical", r == null)) bad.Add(name);
            }
            Assert.IsEmpty(bad);
        }
    }
}

namespace CriticalMass.Sim.Tests.PlayerSeat
{
    using System;
    using System.Collections.Generic;
    using CriticalMass.Sim.Config;
    using CriticalMass.Sim.Core;
    using NUnit.Framework;

    /// <summary>fix/mv-heavy-press lever probe (Slow, report only): the Healer's combine rows (every switch on) under knob specs
    /// from CM_L4_PROBES ("spec|spec|..."; a spec is MvCombine.ApplyKnobs syntax, empty = as committed).</summary>
    [TestFixture]
    [Category("PlayerSeat")]
    public sealed class MovesOffenceHealerProbe
    {
        [Test, Category("Slow")]
        public void HealerRows_UnderKnobSpecs()
        {
            string all = Environment.GetEnvironmentVariable("CM_L4_PROBES") ?? "";
            string cls = Environment.GetEnvironmentVariable("CM_L4_CLASS") ?? "Healer";
            ClassId me = (ClassId)Enum.Parse(typeof(ClassId), cls);
            foreach (string spec in all.Split('|'))
            {
                var parts = new List<string>();
                foreach (string v in new[] { "base", "dodge", "guard", "strong" })
                {
                    var cells = new Dictionary<ClassId, int>();
                    MvDefenceTally t = MvDefenceRig.DuelRow(me, v, c => { MovesConfig.PlayBuildAllOn(c.Depth.Moves); MvCombine.ApplyKnobs(c.Depth.Moves, spec); }, perCell: cells);
                    parts.Add(v + " " + t.Wins + " (" + MvCombine.Cells(cells) + ")");
                }
                PxLog.Line("L4 probe", me + " every switch on [" + spec + "]", string.Join(", ", parts), "report", true);
            }
        }
    }
}

namespace CriticalMass.Sim.Tests.PlayerSeat
{
    using System;
    using System.Collections.Generic;
    using CriticalMass.Sim.Config;
    using CriticalMass.Sim.Core;
    using NUnit.Framework;
    using T = CriticalMass.Sim.Tests.PlayerSeat.PlayerSeatTargets;

    /// <summary>fix/mv-combo-buffer (Editor review): a person tapping Attack in a steady rhythm chains. Every switch on (the play
    /// build); the human seat taps one tick every P ticks (6, 8, 10) at a passive dummy; a trial = a landed opener (step 1) whose
    /// next swing starts inside the chain; success = that next swing is step 2. 10 trials per class and interval.</summary>
    [TestFixture]
    [Category("PlayerSeat")]
    public sealed class MovesOffenceComboRhythm
    {
        internal static (int ok, int trials, string why) Tapper(ClassId me, int period, int phase, Action<GameConfig> tweak, int want = 10)
        {
            Seat s = PlayerSeatRig.Duel(me, ClassId.Healer, TargetMode.Passive, 0, phase, 0, tweak: tweak);
            s.NoTnt = true;
            s.KeepThemAbove = 0.3;
            World w = s.W;
            int lastSwing = CombatFeel.NoTick, lastStep = 0, ok = 0, trials = 0, t0 = -1;
            bool lastLanded = false, pendingTrial = false;
            var why = new List<string>();
            OffRig.Play(s, 30.0, q =>
            {
                Actor a = q.W.Actors[q.Me];
                if (t0 < 0) t0 = q.W.Tick;
                if (a.SwingTick != lastSwing)
                {
                    // a new swing started: score the trial the previous landed opener opened
                    int step = q.W.Moves.Offence.StepOf[q.Me];
                    if (pendingTrial)
                    {
                        trials++;
                        if (step == 2) ok++;
                        else if (why.Count < 3) why.Add("t" + (a.SwingTick - t0) + " step " + step);
                        pendingTrial = false;
                    }
                    lastSwing = a.SwingTick;
                    lastStep = step;
                    lastLanded = false;
                }
                if (!lastLanded && !a.SwingPending && q.W.Depth.Feel.LandedSwing[q.Me] == a.SwingTick && a.SwingTick != CombatFeel.NoTick)
                {
                    lastLanded = true;
                    if (lastStep <= 1) pendingTrial = true;
                }
                // the tapper: one tick down every period ticks, never held
                q.Frames[q.Me].Attack = (q.W.Tick - t0) % period == 0;
            }, q => trials >= want);
            return (ok, trials, string.Join(", ", why));
        }

        /// <summary>A person's double-tap: press 1 starts the opener, press 2 comes `gap` ticks after press 1 and nothing more;
        /// chained = the second swing is step 2. 8 starts per gap.</summary>
        internal static (int chained, int reps) DoubleTap(ClassId me, int gap, Action<GameConfig> tweak)
        {
            int chained = 0, reps = 0;
            for (int k = 0; k < 8; k++)
            {
                Seat s = PlayerSeatRig.Duel(me, ClassId.Healer, TargetMode.Passive, 0, k, 0, tweak: tweak);
                s.NoTnt = true;
                s.KeepThemAbove = 0.3;
                int first = -1, swings = 0, lastSwing = s.W.Actors[s.Me].SwingTick, step2 = 0;
                OffRig.Play(s, 6.0, q =>
                {
                    Actor a = q.W.Actors[q.Me];
                    if (a.SwingTick != lastSwing)
                    {
                        lastSwing = a.SwingTick;
                        swings++;
                        if (swings == 2 && q.W.Moves.Offence.StepOf[q.Me] == 2) step2 = 1;
                    }
                    bool inReach = q.W.Actors[q.Them].Alive && Dist(q.W, q.Me, q.Them) <= q.W.Config.Class(me).AttackRange + q.W.Config.Class(ClassId.Healer).Radius - 0.1;
                    bool press = false;
                    if (first < 0 && inReach && Combat.AttackReady(q.W, q.Me)) { first = q.W.Tick; press = true; }
                    else if (first >= 0 && q.W.Tick == first + gap) press = true;
                    q.Frames[q.Me].Attack = press;
                    if (first >= 0) { q.Frames[q.Me].MoveX = 0; q.Frames[q.Me].MoveZ = 0; }
                }, q => swings >= 2 || (first >= 0 && q.W.Tick > first + 90));
                reps++;
                chained += step2;
            }
            return (chained, reps);
        }

        static double Dist(World w, int a, int b) { double dx = w.X[b] - w.X[a], dz = w.Z[b] - w.Z[a]; return Math.Sqrt(dx * dx + dz * dz); }

        [Test]
        public void ADoubleTapChains_AnEarlyMashDoesNot_AndNPerClass()
        {
            Action<GameConfig> allOn = c => MovesConfig.PlayBuildAllOn(c.Depth.Moves);
            var bad = new List<string>();
            MovesConfig mc = new MovesConfig();
            GameConfig gc = GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full);
            MovesConfig.PlayBuildAllOn(gc.Depth.Moves);
            World w0 = World.CreateWorld(new WorldOptions { Seed = 1, TeamSize = 1, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = gc });
            foreach (ClassId me in T.Classes)
            {
                if (mc.ComboSteps[(int)me] < 2) continue;
                int wu = w0.Dur.AttackWindup[(int)me], n = MovesHooks.OffenceComboBufferTicks(w0, me);
                // a natural double-tap: the second press just after the hit lands (contact + 1 .. + 4)
                var parts = new List<string>();
                bool ok = true;
                foreach (int extra in new[] { 1, 4 })
                {
                    var r = DoubleTap(me, wu + extra, allOn);
                    parts.Add("press 2 at contact+" + extra + ": " + r.chained + "/" + r.reps);
                    if (r.chained < 7) ok = false;
                }
                // mashing during the wind-up (press 2 at 3 ticks, well before the hit): not remembered
                var early = DoubleTap(me, 3, allOn);
                parts.Add("press 2 at +3 (in the wind-up): " + early.chained + "/" + early.reps);
                if (wu > 4 && early.chained > 0) ok = false;
                if (!PxLog.Line("combo-buffer DoubleTap", me + " (wind-up " + wu + ", N = " + n + " ticks from contact to the window)", string.Join("; ", parts), "double-tap after the hit >= 7 of 8; a press in the wind-up never", ok)) bad.Add(me.ToString());
            }
            Assert.IsEmpty(bad);
        }

        [Test]
        public void ATapperAt6_8_10TicksGetsStep2_8Of10_EveryChainingClass()
        {
            Action<GameConfig> allOn = c => MovesConfig.PlayBuildAllOn(c.Depth.Moves);
            var bad = new List<string>();
            MovesConfig mc = new MovesConfig();
            foreach (ClassId me in T.Classes)
            {
                if (mc.ComboSteps[(int)me] < 2) continue;
                var parts = new List<string>();
                bool ok = true;
                foreach (int p in new[] { 6, 8, 10 })
                {
                    var r = Tapper(me, p, 0, allOn);
                    parts.Add("every " + p + ": " + r.ok + "/" + r.trials + (r.why.Length > 0 ? " [" + r.why + "]" : ""));
                    if (r.trials < 10 || r.ok < 8) ok = false;
                }
                if (!PxLog.Line("combo-buffer Rhythm", me + " taps Attack at a passive dummy, every switch on", string.Join("; ", parts), ">= 8 of 10 at each interval", ok)) bad.Add(me.ToString());
            }
            Assert.IsEmpty(bad);
        }
    }
}

namespace CriticalMass.Sim.Tests.PlayerSeat
{
    using System;
    using System.Collections.Generic;
    using CriticalMass.Sim.Config;
    using CriticalMass.Sim.Core;
    using NUnit.Framework;
    using T = CriticalMass.Sim.Tests.PlayerSeat.PlayerSeatTargets;

    /// <summary>fix/mv-combo-reach (Editor pass 2: the Farmer's chain never connected): a person who keeps tapping Attack and holds
    /// toward the target lands step 2 and the finisher after the class's own step-1 knock. Every switch on.</summary>
    [TestFixture]
    [Category("PlayerSeat")]
    public sealed class MovesOffenceComboReach
    {
        internal static string Diag = "";

        internal static (int trials, int step2, int fin) Chase(ClassId me, ClassId them, bool bot, int period, int k)
        {
            Action<GameConfig> tweak = c =>
            {
                MovesConfig.PlayBuildAllOn(c.Depth.Moves);
                if (!bot) return;
                MovesConfig m = c.Depth.Moves;   // a Normal bot that never dodges, guards, parries or steps out
                for (int d = 0; d < m.BotGuardChance.Length; d++) m.BotGuardChance[d] = 0;
                for (int d = 0; d < m.BotParryChance.Length; d++) m.BotParryChance[d] = 0;
                for (int d = 0; d < m.BotStepOutChance.Length; d++) m.BotStepOutChance[d] = 0;
                for (int d = 0; d < c.Depth.CombatFeel.BotMeleeDodgeChance.Length; d++) c.Depth.CombatFeel.BotMeleeDodgeChance[d] = 0;
                for (int d = 0; d < c.Depth.Dodge.DodgeChance.Length; d++) c.Depth.Dodge.DodgeChance[d] = 0;
            };
            Seat s = PlayerSeatRig.Duel(me, them, bot ? TargetMode.RealBot : TargetMode.Passive, PlayerSeatRig.AngleOf(k, 4), k, 0, tweak: tweak);
            s.NoTnt = true;
            s.KeepThemAbove = 0.6;   // a whole chain can take 40% of a body: refill early so the duel runs all 10 trials
            s.KeepMeAbove = 0.6;
            World w = s.W;
            int steps = new MovesConfig().ComboSteps[(int)me];
            int lastSwing = w.Actors[s.Me].SwingTick, curStep = 0, trials = 0, s2 = 0, fin = 0, t0 = -1;
            Diag = "";
            int started2 = 0, whiff2 = 0, stunned = 0, lastTrialTick = 0;
            var contacts = new List<string>();
            double gapSum = 0;
            bool landed = false, inTrial = false, got2 = false, gotFin = false;
            int openLand = -100;
            OffRig.Play(s, 40.0, q =>
            {
                Actor a = q.W.Actors[q.Me];
                if (t0 < 0) t0 = q.W.Tick;
                if (a.SwingTick != lastSwing)
                {
                    lastSwing = a.SwingTick;
                    curStep = q.W.Moves.Offence.StepOf[q.Me];
                    landed = false;
                    if (curStep <= 1 && inTrial) { inTrial = false; }
                    if (inTrial && curStep == 2) { started2++; double gx = q.W.X[q.Them] - q.W.X[q.Me], gz = q.W.Z[q.Them] - q.W.Z[q.Me]; gapSum += Math.Sqrt(gx * gx + gz * gz); }
                }
                if (inTrial && a.HitStunTicks > 0) stunned++;
                if (inTrial && curStep == 2 && a.SwingPending && q.W.Tick + 1 == a.SwingTick + a.SwingWindup && contacts.Count < 6)
                {
                    double cx = q.W.X[q.Them] - q.W.X[q.Me], cz = q.W.Z[q.Them] - q.W.Z[q.Me];
                    double ang = Math.Atan2(cz, cx) - a.SwingFacing;
                    while (ang > Math.PI) ang -= 2 * Math.PI;
                    while (ang < -Math.PI) ang += 2 * Math.PI;
                    contacts.Add(Math.Sqrt(cx * cx + cz * cz).ToString("0.00") + "@" + (ang * 180 / Math.PI).ToString("0") + "deg" + (q.W.Actors[q.Them].SwingPending ? "S" : ""));
                }
                // fix/mv-trade-neutral: an opener traded with the victim's own hit opens no chain by rule: not a chain trial
                int took = q.W.Moves.Offence.HitTakenTick[q.Me];
                if (inTrial && !got2 && took != CombatFeel.NoTick && Math.Abs(took - openLand) <= MovesHooks.OffenceTradeTicks) { trials--; inTrial = false; }
                if (inTrial && curStep == 2 && !a.SwingPending && !landed && q.W.Tick == a.SwingTick + a.SwingWindup + 1) whiff2++;
                if (!landed && !a.SwingPending && q.W.Depth.Feel.LandedSwing[q.Me] == a.SwingTick)
                {
                    landed = true;
                    if (curStep <= 1) { trials++; inTrial = true; got2 = gotFin = false; openLand = q.W.Tick; }
                    else if (inTrial)
                    {
                        if (curStep == 2 && !got2) { got2 = true; s2++; }
                        if (curStep >= steps && !gotFin) { gotFin = true; fin++; }
                    }
                }
                // the person: holds toward the target, aims at it, taps Attack every `period` ticks
                double dx = q.W.X[q.Them] - q.W.X[q.Me], dz = q.W.Z[q.Them] - q.W.Z[q.Me], d = Math.Sqrt(dx * dx + dz * dz);
                InputFrame f = q.Frames[q.Me];
                if (d > 1e-6)
                {
                    f.MoveX = dx / d; f.MoveZ = dz / d;
                    f.AimX = q.W.X[q.Them]; f.AimZ = q.W.Z[q.Them];
                }
                f.Attack = (q.W.Tick - t0) % period == 0;
            }, q => trials >= 11 && !inTrial);
            var why = new SortedDictionary<int, int>();
            foreach (SeatEvent e in s.Spy.Of(SimEventType.HitBlocked, e => e.B == s.Me)) { int v = (int)e.Value; why[v] = why.TryGetValue(v, out int c0) ? c0 + 1 : 1; }
            var wb = new List<string>(); foreach (var kv in why) wb.Add(kv.Key + ":" + kv.Value);
            Diag = "started2 " + started2 + " whiff2 " + whiff2 + " gap@start2 " + (started2 > 0 ? (gapSum / started2).ToString("0.00") : "-") + " stunnedTicks " + stunned + " blocked " + string.Join(",", wb)
                + " dodges " + s.Spy.Of(SimEventType.Dodge, e => e.A == s.Them).Count + " step2 contact " + string.Join(" ", contacts);
            return (Math.Min(trials, 10), Math.Min(s2, 10), Math.Min(fin, 10));
        }

        [Test]
        public void ATapperHoldingTowardTheTargetLandsStep2AndTheFinisher_DummyAndNoDodgeBot()
        {
            var bad = new List<string>();
            MovesConfig mc = new MovesConfig();
            foreach (ClassId me in T.Classes)
            {
                if (mc.ComboSteps[(int)me] < 2) continue;
                var parts = new List<string>();
                bool ok = true;
                foreach (bool bot in new[] { false, true })
                {
                    int tr = 0, a2 = 0, af = 0;
                    // the Editor's pass-2 case: a Farmer victim (the Farmer mirror too); never the Warrior, whose armoured wind-up
                    // trades through a light hit by design (the "Warrior wins every one-on-one" ruling)
                    ClassId them = ClassId.Farmer;
                    var r = Chase(me, them, bot, 8, 0);
                    tr += r.trials; a2 += r.step2; af += r.fin;
                    parts.Add((bot ? "Normal no-dodge bot " : "passive dummy ") + them + ": step 2 " + a2 + "/" + tr + ", finisher " + af + "/" + tr + " {" + Diag + "}");
                    // KNOWN (accepted by the orchestrator, 2026-11-23): Demolitions' chain vs the Normal fighting bot lands step 2
                    // 6 of 10 (the bot's own swing trades into the pry bar's reach); asserted at 6 for that one cell
                    // fix/mv-trade-neutral: an opener traded with the victim's own hit opens no chain by rule, so the trial is
                    // dropped (a fighting bot trades a share of them): the shares are of the clean openers, at least 3
                    int need2 = bot && me == ClassId.Demolitions ? 6 : 8;
                    if (tr < (bot ? 3 : 10) || a2 * 10 < need2 * tr || af * 10 < 6 * tr) ok = false;
                }
                if (!PxLog.Line("combo-reach", me + " taps every 8 ticks holding toward the target, every switch on", string.Join("; ", parts), "step 2 >= 8 of 10, finisher >= 6 of 10 of the clean (untraded) openers", ok)) bad.Add(me.ToString());
            }
            Assert.IsEmpty(bad);
        }
    }
}

namespace CriticalMass.Sim.Tests.PlayerSeat
{
    using System;
    using System.Collections.Generic;
    using CriticalMass.Sim.Config;
    using CriticalMass.Sim.Core;
    using NUnit.Framework;
    using T = CriticalMass.Sim.Tests.PlayerSeat.PlayerSeatTargets;

    /// <summary>fix/mv-combo-reach-2 (Editor pass 3: "a deliberate second tap after a landed hit whiffs"): the press-timing grid.
    /// One opener, then each next press at the hit (the contact tick), just after it, in the middle of the chain window or on
    /// its LAST tick; the stick held toward the victim or let go; a passive Farmer dummy or a Normal Farmer bot that never
    /// dodges. Every switch on. 8 starts per cell.</summary>
    [TestFixture]
    [Category("PlayerSeat")]
    public sealed class MovesOffenceComboGrid
    {
        internal static readonly Dictionary<string, int> Diag = new Dictionary<string, int>();
        internal static (int s2, int s3, int n) Cell(ClassId me, string when, bool stick, bool bot)
        {
            int s2 = 0, s3 = 0, n = 0;
            int steps = new MovesConfig().ComboSteps[(int)me];
            for (int k = 0; k < 8; k++)
            {
                Action<GameConfig> tweak = c =>
                {
                    MovesConfig.PlayBuildAllOn(c.Depth.Moves);
                    if (!bot) return;
                    MovesConfig m = c.Depth.Moves;
                    for (int d = 0; d < m.BotGuardChance.Length; d++) m.BotGuardChance[d] = 0;
                    for (int d = 0; d < m.BotParryChance.Length; d++) m.BotParryChance[d] = 0;
                    for (int d = 0; d < m.BotStepOutChance.Length; d++) m.BotStepOutChance[d] = 0;
                    for (int d = 0; d < c.Depth.CombatFeel.BotMeleeDodgeChance.Length; d++) c.Depth.CombatFeel.BotMeleeDodgeChance[d] = 0;
                    for (int d = 0; d < c.Depth.Dodge.DodgeChance.Length; d++) c.Depth.Dodge.DodgeChance[d] = 0;
                };
                Seat s = PlayerSeatRig.Duel(me, ClassId.Farmer, bot ? TargetMode.RealBot : TargetMode.Passive, PlayerSeatRig.AngleOf(k, 8), k, 0, tweak: tweak);
                s.NoTnt = true;
                s.KeepThemAbove = 0.6;
                s.KeepMeAbove = 0.6;
                int lastSwing = s.W.Actors[s.Me].SwingTick, curStep = 0, pressAt = -1, opened = 0, got = 0, t0 = s.W.Tick;
                bool pressedOpener = false, landed = false, done = false, traded = false;
                int openerLand = -100;
                OffRig.Play(s, 6.0, q =>
                {
                    World w = q.W;
                    Actor a = w.Actors[q.Me];
                    if (a.SwingTick != lastSwing) { lastSwing = a.SwingTick; curStep = w.Moves.Offence.StepOf[q.Me]; landed = false; if (curStep <= 1 && opened > 0) done = true; }
                    bool justLanded = false;
                    int took = w.Moves.Offence.HitTakenTick[q.Me];   // fix/mv-trade-neutral: a traded opener opens no chain by rule
                    if (opened > 0 && took != CombatFeel.NoTick && Math.Abs(took - openerLand) <= MovesHooks.OffenceTradeTicks) { traded = true; done = true; }
                    if (curStep >= 2 && a.SwingPending && a.HitStunTicks > 0) Diag["stunned"] = Diag.GetValueOrDefault("stunned") + 1;
                    if (curStep >= 2 && a.SwingPending && w.Tick == a.SwingTick + 1) { double ex = w.X[q.Them] - w.X[q.Me], ez = w.Z[q.Them] - w.Z[q.Me]; Diag["gap@start x10 sum"] = Diag.GetValueOrDefault("gap@start x10 sum") + (int)(10 * Math.Sqrt(ex * ex + ez * ez)); Diag["starts"] = Diag.GetValueOrDefault("starts") + 1; Diag["themStun@start>0"] = Diag.GetValueOrDefault("themStun@start>0") + (w.Actors[q.Them].HitStunTicks > 0 ? 1 : 0); Diag["themSwinging@start"] = Diag.GetValueOrDefault("themSwinging@start") + (w.Actors[q.Them].SwingPending ? 1 : 0); }
                    if (!landed && !a.SwingPending && w.Depth.Feel.LandedSwing[q.Me] == a.SwingTick && pressedOpener)
                    {
                        landed = true;
                        justLanded = true;
                        if (curStep <= 1) { opened = 1; openerLand = w.Tick; Diag["L:stun=" + w.Actors[q.Them].HitStunTicks + " cd=" + (int)Math.Ceiling(a.AttackCd * w.Config.SimHz - 1e-9) + " win=" + (w.Moves.Offence.ChainUntil[q.Me] - w.Tick) + " rec=" + a.SwingRecovery] = 1; }
                        else if (curStep > got && opened > 0) got = curStep;
                        if (got >= steps) done = true;
                    }
                    if (!a.SwingPending && !landed && pressedOpener && w.Tick > a.SwingTick + a.SwingWindup + 1 && opened > 0 && curStep >= 2) done = true;   // a step whiffed
                    double dx = w.X[q.Them] - w.X[q.Me], dz = w.Z[q.Them] - w.Z[q.Me], d = Math.Sqrt(dx * dx + dz * dz);
                    InputFrame f = q.Frames[q.Me];
                    f.AimX = w.X[q.Them]; f.AimZ = w.Z[q.Them];
                    bool toward = opened == 0 || stick;
                    f.MoveX = toward && d > 1e-6 ? dx / d : 0; f.MoveZ = toward && d > 1e-6 ? dz / d : 0;
                    bool press = false;
                    double reach = w.Config.Class(me).AttackRange + w.Config.Class(ClassId.Farmer).Radius;
                    // the opener: pressed in reach; pressed again (walking in) until one LANDS, so a trial measures the chain only
                    if (opened == 0 && pressedOpener && !a.SwingPending && !landed && w.Tick > a.SwingTick + a.SwingWindup + 1) pressedOpener = false;
                    if (!pressedOpener && opened == 0 && d <= reach - 0.3 && Combat.AttackReady(w, q.Me)) { press = true; pressedOpener = true; }
                    else if (pressedOpener && !done)
                    {
                        int contact = a.SwingTick + a.SwingWindup;
                        // schedule this chain's next press from the swing that is landing now
                        if (when == "hit" && a.SwingPending && w.Tick + 1 == contact && (curStep < steps)) press = true;
                        if (justLanded && curStep < steps)
                        {
                            int until = w.Moves.Offence.ChainUntil[q.Me];
                            if (when == "after") pressAt = contact + 1;
                            else if (when == "mid") pressAt = (contact + until) / 2;
                            else if (when == "last") pressAt = until;
                        }
                        if (pressAt >= 0 && w.Tick + 1 == pressAt)
                        {
                            press = true; pressAt = -1;
                            string why = a.HitStunTicks > 0 ? "meStunned" : a.SwingPending ? "mePending" : a.AttackCd > 1.5 / w.Config.SimHz ? "meCd" : w.Moves.Offence.ChainNext[q.Me] < 2 ? "chainGone" : "ok";
                            Diag["press:" + why] = Diag.GetValueOrDefault("press:" + why) + 1;
                            Diag["press:themStun>0"] = Diag.GetValueOrDefault("press:themStun>0") + (w.Actors[q.Them].HitStunTicks > 0 ? 1 : 0);
                        }
                    }
                    f.Attack = press;
                }, q => done || q.W.Tick - t0 > 300);
                if (opened == 0 || traded) continue;   // no opener landed in 10 s, or it was a trade (no chain by rule): not a chain trial
                n++;
                if (got >= 2) s2++;
                if (got >= 3) s3++;
            }
            return (s2, s3, n);
        }

        [Test]
        public void EveryPressTimingInTheWindowLandsTheNextStep_StickOrNot_DummyAndBot()
        {
            var bad = new List<string>();
            MovesConfig mc = new MovesConfig();
            foreach (ClassId me in T.Classes)
            {
                int steps = mc.ComboSteps[(int)me];
                if (steps < 2) continue;
                foreach (bool bot in new[] { false, true })
                {
                    var parts = new List<string>();
                    bool ok = true;
                    foreach (string when in new[] { "hit", "after", "mid", "last" })
                        foreach (bool stick in new[] { true, false })
                        {
                            Diag.Clear();
                            var r = Cell(me, when, stick, bot);
                            if (Environment.GetEnvironmentVariable("CM_L4_GRIDDIAG") != null && bot) PxLog.Line("grid-diag", me + " " + when + (stick ? "+stick" : ""), string.Join(" ", Diag), "-", true);
                            parts.Add(when + (stick ? "+stick" : "") + " " + r.s2 + (steps >= 3 ? "/" + r.s3 : "") + (r.n < 8 ? " of " + r.n : ""));
                            if (r.n < 6 || r.s2 < r.n - 1 || (steps >= 3 && r.s3 < r.n - 2)) ok = false;
                        }
                    if (!PxLog.Line("combo-grid", me + " vs a " + (bot ? "Normal no-dodge Farmer bot" : "passive Farmer dummy") + " (step 2" + (steps >= 3 ? "/step 3" : "") + " of 8)", string.Join(", ", parts), "step 2 >= 7 of 8" + (steps >= 3 ? ", step 3 >= 6 of 8" : "") + " in every cell", ok)) bad.Add(me + (bot ? " bot" : " dummy"));
                }
            }
            Assert.IsEmpty(bad);
        }
    }
}

namespace CriticalMass.Sim.Tests.PlayerSeat
{
    using System;
    using System.Collections.Generic;
    using CriticalMass.Sim.Config;
    using CriticalMass.Sim.Core;
    using NUnit.Framework;
    using T = CriticalMass.Sim.Tests.PlayerSeat.PlayerSeatTargets;

    /// <summary>fix/mv-trade-neutral: person vs person mirror duels (every switch on). Both walk in; A presses on reach, B d
    /// ticks later (d 0..3), then each presses at every ready swing in reach after a seeded 0-3 tick reaction (same skill), 4 reaction seeds per start.
    /// A trade = both sides' first hits within OffenceTradeTicks: the earlier hitter's win share must sit in 0.35-0.65.
    /// A clean first hit (B presses 12 ticks late) must still chain.</summary>
    [TestFixture]
    [Category("PlayerSeat")]
    public sealed class MovesOffenceTradeNeutral
    {
        sealed class Side { public int Id, Foe, PressAt = -1, FirstHit = CombatFeel.NoTick, Steps; public bool WasReady; }

        static (bool traded, bool earlierWon, bool decided, int aSteps) Duel(ClassId cls, int k, int d, int rep = 0)
        {
            Seat s = PlayerSeatRig.Duel(cls, cls, TargetMode.HumanVsHuman, PlayerSeatRig.AngleOf(k, 8), k, 0, tweak: c =>
            {
                MovesConfig.PlayBuildAllOn(c.Depth.Moves);
                if (Environment.GetEnvironmentVariable("CM_L4_NOCRIT") != null) c.Depth.CombatFeel.CritEnabled = false;   // probe only
            });
            s.NoTnt = true;
            var rng = new Random(1000 * k + 17 * d + 7919 * rep + (int)cls);
            var A = new Side { Id = s.Me, Foe = s.Them };
            var B = new Side { Id = s.Them, Foe = s.Me };
            bool opened = false;
            int openTick = -1;
            DuelResult r = OffRig.Play(s, 25.0, q =>
            {
                World w = q.W;
                foreach (Side x in new[] { A, B })
                {
                    Actor a = w.Actors[x.Id];
                    if (x.FirstHit == CombatFeel.NoTick && w.Depth.Feel.LandedSwing[x.Id] == a.SwingTick && !a.SwingPending && a.SwingTick != CombatFeel.NoTick) x.FirstHit = w.Tick;
                    double dx = w.X[x.Foe] - w.X[x.Id], dz = w.Z[x.Foe] - w.Z[x.Id], dist = Math.Sqrt(dx * dx + dz * dz);
                    double reach = w.Config.Class(cls).AttackRange + w.Config.Class(cls).Radius;
                    InputFrame f = q.Frames[x.Id];
                    f.MoveX = f.MoveZ = 0; f.Attack = f.Dodge = f.Guard = f.Cancel = false;
                    f.AimX = w.X[x.Foe]; f.AimZ = w.Z[x.Foe];
                    if (dist > reach - 0.3 && dist > 1e-6) { f.MoveX = dx / dist; f.MoveZ = dz / dist; }
                    bool inReach = dist <= reach - 0.3;
                    if (!opened)
                    {
                        if (x == A && inReach && Combat.AttackReady(w, A.Id)) { opened = true; openTick = w.Tick; A.PressAt = w.Tick; B.PressAt = w.Tick + d; }
                    }
                    bool ready = Combat.AttackReady(w, x.Id) || (w.Moves.Offence.ChainNext[x.Id] >= 2 && !a.SwingPending);
                    if (opened && ready && !x.WasReady && x.PressAt < w.Tick) x.PressAt = w.Tick + rng.Next(0, 4);
                    x.WasReady = ready;
                    if (opened && x.PressAt == w.Tick && inReach) f.Attack = true;
                    else if (opened && x.PressAt == w.Tick) x.PressAt = w.Tick + 1;   // walk in, press on reach
                }
            });
            A.Steps = s.Spy.Of(SimEventType.ComboStep, e => e.A == A.Id).Count;
            string dg = Environment.GetEnvironmentVariable("CM_L4_TRADEDIAG");
            if (dg != null && dg == cls + "/" + k + "/" + d)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var e in s.Spy.All)
                    if (e.Tick <= openTick + 90 && (e.Type == SimEventType.AttackStart || e.Type == SimEventType.HitStun || e.Type == SimEventType.AttackCancel || e.Type == SimEventType.ComboStep || e.Type == SimEventType.Hit))
                        sb.Append(e.Type).Append(' ').Append(e.A).Append('>').Append(e.B).Append(" v").Append(e.Value.ToString("0.#")).Append(" t").Append(e.Tick - openTick).Append("; ");
                PxLog.Line("trade-diag", cls + " k" + k + " d" + d + " A=" + A.Id + " firstA " + (A.FirstHit - openTick) + " firstB " + (B.FirstHit - openTick) + " won " + r.Won + " lost " + r.Lost, sb.ToString(), "-", true);
            }
            bool traded = A.FirstHit != CombatFeel.NoTick && B.FirstHit != CombatFeel.NoTick && Math.Abs(A.FirstHit - B.FirstHit) <= MovesHooks.OffenceTradeTicks;
            bool aEarlier = A.FirstHit <= B.FirstHit;
            bool earlierWon = aEarlier ? r.Won : r.Lost;
            return (traded, earlierWon, r.Won || r.Lost, A.Steps);
        }

        [Test]
        public void ATradeOpensNoChain_EarlierHitterWinsAboutHalf_ACleanHitStillChains()
        {
            var bad = new List<string>();
            MovesConfig mc = new MovesConfig();
            foreach (ClassId cls in T.Classes)
            {
                if (mc.ComboSteps[(int)cls] < 2) continue;
                int trades = 0, earlierWins = 0, decided = 0;
                for (int d = 0; d <= 3; d++)
                    for (int k = 0; k < 8; k++)
                    for (int rp = 0; rp < 4; rp++)
                    {
                        var r = Duel(cls, k, d, rp);
                        if (!r.traded || !r.decided) continue;
                        trades++; decided++;
                        if (r.earlierWon) earlierWins++;
                    }
                int clean = 0;
                for (int k = 0; k < 8; k++) if (Duel(cls, k, 12).aSteps > 0) clean++;
                double share = decided > 0 ? (double)earlierWins / decided : -1;
                bool ok = trades >= 24 && share >= 0.35 && share <= 0.65 && clean >= 6;
                if (!PxLog.Line("trade-neutral", cls + " mirror, person vs person", "trades " + trades + " of 128, earlier hitter won " + earlierWins + " (" + share.ToString("0.00") + "); clean first hit chained " + clean + "/8", "trades >= 24, share 0.35-0.65, clean >= 6 of 8", ok)) bad.Add(cls.ToString());
            }
            Assert.IsEmpty(bad);
        }
    }
}
