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
            foreach (ClassId me in T.Classes)
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
                if (!PxLog.Line("T2-3c PassiveTtk" + (noChain ? " (no chain: ComboSteps 1)" : ""), me + " on passive Warrior / Farmer / Builder dummies, combos off -> on", "fall " + PxLog.F(fall, "0.000") + " (" + string.Join(", ", parts) + ")", noChain ? "0 (no chain)" : "0.05 to 0.15", noChain ? fall == 0 : fall >= 0.05 && fall <= 0.15)) bad.Add(me + " " + PxLog.F(fall));
            }
            Assert.IsEmpty(bad);
        }

        [Test]
        [Category("Slow")]
        public void T2_3b_StrongWithCombos_BeatsStrongWithout()
        {
            var bad = new List<string>();
            foreach (ClassId me in T.Classes)
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
