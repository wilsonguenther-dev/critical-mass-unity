// Class moves build (2026-11-22), lane 6, T1-7: the class-moves audit probe (cm-incidents/class-moves-audit-2026-11-22/probe/
// MovesAuditProbe.cs) made permanent. The probe's measurements are kept as helpers (PsmRig, PsmProbe*), every seat built with
// PsmRig.Tweak (null = every switch off, AllOn = the play build). Tests S62 to S70 print the [mv] lines for both and assert
// the SPEC.md acceptance numbers with the switches ON (S70 asserts that switches OFF change nothing). Category PlayerSeat;
// the long ones are Slow.
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
    public sealed class PsmTally
    {
        public int Reps, Wins, Losses, Swings, Landed, Crits, CritCounter, CritPunish, CritBack, MyHits, Escaped, Engaged, Uses;
        public double HpLeft, Seconds, Saved, Taken;
        public double Share => Reps > 0 ? (double)Wins / Reps : 0;
        public double Land => Swings > 0 ? (double)Landed / Swings : 0;
        public void Add(PsmTally o)
        {
            Reps += o.Reps; Wins += o.Wins; Losses += o.Losses; Swings += o.Swings; Landed += o.Landed; Crits += o.Crits;
            CritCounter += o.CritCounter; CritPunish += o.CritPunish; CritBack += o.CritBack; MyHits += o.MyHits; Escaped += o.Escaped;
            Engaged += o.Engaged; Uses += o.Uses; HpLeft += o.HpLeft; Seconds += o.Seconds; Saved += o.Saved; Taken += o.Taken;
        }
        public string Duel() => "won " + Wins + "/" + Reps + " (" + PxLog.F(Share) + ") land " + PxLog.F(Land) + " hpLeft " + PxLog.F(Reps > 0 ? HpLeft / Reps : 0)
            + " crit " + Crits + "/" + MyHits + " (C" + CritCounter + " P" + CritPunish + " B" + CritBack + ") uses " + Uses;
    }

    /// <summary>Watches the enemy seat's melee wind-ups (press tick, distance at the press).</summary>
    public sealed class PsmWatch
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
                    TheirStartDist = Dist(s.W, s.Me, s.Them);
                }
            }
        }
        public static double Dist(World w, int a, int b) { double dx = w.X[b] - w.X[a], dz = w.Z[b] - w.Z[a]; return Math.Sqrt(dx * dx + dz * dz); }
    }

    public static class PsmRig
    {
        public static readonly ClassId[] All = T.Classes;
        public const int Lag = 6;
        /// <summary>The world tweak every probe seat is built with: null = the shipped rules (every class-moves switch off),
        /// AllOn = the play build (MovesConfig.PlayBuildAllOn).</summary>
        public static Action<GameConfig> Tweak;
        public static readonly Action<GameConfig> AllOn = c => MovesConfig.PlayBuildAllOn(c.Depth.Moves);
        public static bool On => Tweak != null;
        /// <summary>The checks of one test (Unity's NUnit has no Assert.Multiple: the [mv] lines are printed first, so every
        /// number is in the log even when the first check stops the test).</summary>
        public static void Checks(Action checks) => checks();

        /// <summary>
        /// One duel through the human seam. Switches off: PlayerSeatRig.Play exactly as the probe ran. Switches on: the seat
        /// models' held Attack (PlayerSeatRig's "want" holds it while a swing is ready) becomes a one-tick tap for both human
        /// seats, through lane 4's OffRig step: with Heavy on a held Attack CHARGES (tap = light), so the probe's held button
        /// would measure charged heavies instead of the light swings a person taps. Same hands as lane 4's OffencePilot.Tap.
        /// </summary>
        public static DuelResult Play(Seat s, double maxSeconds, Func<Seat, bool> stop = null)
        {
            if (!On) return PlayerSeatRig.Play(s, maxSeconds, stop);
            bool p1 = false, p2 = false;
            return OffRig.Play(s, maxSeconds, q =>
            {
                Tap(q.Human != null ? q.Frames[q.Human.Me] : null, ref p1);
                Tap(q.Human2 != null ? q.Frames[q.Human2.Me] : null, ref p2);
            }, stop);
        }

        static void Tap(InputFrame f, ref bool prev)
        {
            if (f == null) return;
            if (f.Attack && prev) f.Attack = false;   // one tick down, then up
            prev = f.Attack;
        }
        public const int Help = 1;   // shipped mouse aim help (Low)

        public static void CountCrits(Seat s, PsmTally t)
        {
            t.MyHits += s.Spy.Of(SimEventType.Hit, e => e.B == s.Me && e.A == s.Them).Count;
            foreach (SeatEvent e in s.Spy.Of(SimEventType.CriticalHit, e => e.B == s.Me))
            {
                t.Crits++;
                int k = (int)e.Value;
                if (k == 1) t.CritCounter++; else if (k == 2) t.CritPunish++; else if (k == 3) t.CritBack++;
            }
        }

        /// <summary>The class's own ability used the way a sensible player would in a one-on-one (returns true when pressed).</summary>
        public static bool UseKit(Seat s, PsmWatch wt, ClassId me, bool fleeing)
        {
            World w = s.W;
            Actor a = w.Actors[s.Me];
            if (!a.Alive || a.AbilityCd > 0) return false;
            double d = PsmWatch.Dist(w, s.Me, s.Them);
            ClassId them = w.Actors[s.Them].Cls;
            double theirReach = w.Config.Class(them).AttackRange + w.Config.Class(me).Radius;
            switch (me)
            {
                case ClassId.Warrior:
                    if (wt.Since(w) == Lag && wt.TheirStartDist <= theirReach + 0.5) { s.Human.PressAbility = true; return true; }
                    return false;
                case ClassId.Farmer:
                    if (d <= 5.5) { s.Human.PressAbility = true; s.Human.AimOverrideX = w.X[s.Them]; s.Human.AimOverrideZ = w.Z[s.Them]; return true; }
                    return false;
                case ClassId.Miner:
                    if (fleeing || d <= 4) { s.Human.PressAbility = true; return true; }
                    return false;
                case ClassId.Demolitions:
                    if (d <= (fleeing ? 4.0 : 2.5)) { s.Human.PressAbility = true; s.Human.AimOverrideX = w.X[s.Me]; s.Human.AimOverrideZ = w.Z[s.Me]; return true; }
                    return false;
                case ClassId.Healer:
                    if (a.Hp < 0.65 * a.MaxHp) { s.Human.PressAbility = true; s.Human.AimOverrideX = w.X[s.Me]; s.Human.AimOverrideZ = w.Z[s.Me]; return true; }
                    return false;
                default:
                    return false;   // Builder: his ability is the ladder / bridge tool, no combat use
            }
        }

        /// <summary>One duel cell: human me vs Normal bot them, angles starts. variant: base, feint, hitcancel, dodge, kit, strong, notnt.</summary>
        public static PsmTally DuelCell(ClassId me, ClassId them, string variant, int angles = 8, BotDifficulty bots = BotDifficulty.Normal)
        {
            var t = new PsmTally();
            for (int k = 0; k < angles; k++)
            {
                Seat s = PlayerSeatRig.Duel(me, them, TargetMode.RealBot, PlayerSeatRig.AngleOf(k, angles), k, Lag, bots: bots, tweak: PsmRig.Tweak);
                s.Human.AimHelp = Help;
                bool strong = variant == "strong";
                if (strong) s.Human.LagTicks = 5;
                if (variant == "feint" || strong) s.Human.Feints = true;
                if (variant == "hitcancel" || strong) s.Human.HitCancels = true;
                if (variant == "dodge" || strong) s.Human.Dodges = true;
                if (strong && me == ClassId.Farmer) s.Human.KeepRange = true;
                if (variant == "notnt") s.NoTnt = true;
                var wt = new PsmWatch();
                int uses = 0;
                if (variant == "kit" || strong) s.BeforeTick = q => { wt.Update(q); if (UseKit(q, wt, me, false)) uses++; };
                DuelResult r = PsmRig.Play(s, 30);
                t.Reps++;
                if (r.Won) t.Wins++;
                if (r.Lost) t.Losses++;
                t.Swings += r.Swings;
                t.Landed += r.Landed;
                t.HpLeft += s.W.Actors[s.Me].Alive ? s.W.Actors[s.Me].Hp / s.W.Actors[s.Me].MaxHp : 0;
                t.Uses += uses;
                t.Taken += r.Taken;
                CountCrits(s, t);
            }
            return t;
        }

        public static PsmTally DuelRow(ClassId me, string variant, BotDifficulty bots = BotDifficulty.Normal)
        {
            var row = new PsmTally();
            foreach (ClassId them in All) if (them != me) row.Add(DuelCell(me, them, variant, 8, bots));
            return row;
        }
    }

    public static class PsmProbeDuels
    {
        public static void Duels_ByVariant()
        {
            string[] variants = { "base", "feint", "hitcancel", "dodge", "kit", "strong" };
            foreach (ClassId me in PsmRig.All)
            {
                foreach (string v in variants)
                {
                    PsmTally r = PsmRig.DuelRow(me, v);
                    Console.WriteLine("[mv] DUEL | " + me + " | " + v + " | " + r.Duel());
                }
                if (me == ClassId.Demolitions) Console.WriteLine("[mv] DUEL | " + me + " | notnt | " + PsmRig.DuelRow(me, "notnt").Duel());
            }
            Console.WriteLine("[mv] DUEL | strong vs Hard per class follows");
            foreach (ClassId me in PsmRig.All) Console.WriteLine("[mv] DUEL | " + me + " | strong-hard | " + PsmRig.DuelRow(me, "strong", BotDifficulty.Hard).Duel());
        }
    }

    public static class PsmProbeOffence
    {
        public static void Strafer_LandRate_And_PassiveTtk()
        {
            foreach (ClassId me in PsmRig.All)
            {
                // strafing dummy of the same class, 40 swings, lag 6, aim help Low
                Seat s = PlayerSeatRig.Duel(me, me, TargetMode.Strafer, 0, 0, PsmRig.Lag, tweak: PsmRig.Tweak);
                s.Human.AimHelp = PsmRig.Help;
                s.NoTnt = true;
                s.KeepThemAbove = 0.3;
                DuelResult r = PsmRig.Play(s, 60, q => PlayerSeatRig.CountSwings(q) >= 40);
                Console.WriteLine("[mv] STRAFER | " + me + " | land " + r.Landed + "/" + r.Swings + " (" + PxLog.F(r.LandRate) + ")");
                foreach (ClassId tgt in new[] { ClassId.Warrior, ClassId.Farmer, ClassId.Builder })
                {
                    foreach (bool hc in new[] { false, true })
                    {
                        Seat p = PlayerSeatRig.Duel(me, tgt, TargetMode.Passive, 0, 0, 0, tweak: PsmRig.Tweak);
                        p.NoTnt = true;
                        p.Human.HitCancels = hc;
                        DuelResult pr = PsmRig.Play(p, 30);
                        Console.WriteLine("[mv] TTK | " + me + " -> " + tgt + " | hitcancel " + hc + " | " + (pr.Won ? PxLog.F(pr.Seconds) + " s" : "no kill") + " swings " + pr.Swings);
                    }
                }
            }
            // Demolitions TNT on a passive and on a Normal bot: hits per throw
            foreach (ClassId tgt in new[] { ClassId.Warrior, ClassId.Farmer, ClassId.Healer })
            {
                int throws = 0, hurt = 0;
                for (int k = 0; k < 8; k++)
                {
                    Seat s = PlayerSeatRig.Duel(ClassId.Demolitions, tgt, TargetMode.RealBot, PlayerSeatRig.AngleOf(k, 8), k, PsmRig.Lag, distance: 4.5, tweak: PsmRig.Tweak);
                    s.Human.AimHelp = PsmRig.Help;
                    s.Human.ApproachShare = 3.0;   // stand off at about 4 u and throw at where she sees him (lag 6)
                    s.Human.Fight = false;
                    s.KeepThemAbove = 0.3; s.KeepMeAbove = 0.3;
                    s.BeforeTick = q =>
                    {
                        double d = PsmWatch.Dist(q.W, 0, q.Them);
                        if (q.W.Tick >= q.W.Demolitions.TntReadyTick[0] && d >= 2.5 && d <= 6) { if (PsmRig.On) q.Human.PressDigPlace = true; else q.Human.PressAttack = true; }   // T1-1: F throws
                    };
                    PsmRig.Play(s, 20);
                    throws += s.Spy.Of(SimEventType.TntThrown, e => e.A == 0).Count;
                    foreach (SeatEvent e in s.Spy.Of(SimEventType.Explosion, x => x.A == 0 && x.B == Demolitions.ExplosionTnt))
                        if (s.Spy.Of(SimEventType.Hit, h => h.B == 0 && h.A == s.Them && h.Tick == e.Tick && h.Cause == DamageCause.Explosion).Count > 0) hurt++;
                }
                Console.WriteLine("[mv] TNT | vs Normal " + tgt + " bot | throws " + throws + " hurt " + hurt + " (" + PxLog.F(throws > 0 ? (double)hurt / throws : 0) + ")");
            }
        }
    }

    public static class PsmProbeDefence
    {
        /// <summary>me stands in reach (kept alive), them = Normal bot attacks. action: none, side, away, jump, shield. React 6 ticks after its press.</summary>
        internal static (int answered, int landedAnswered, int ctrl, int ctrlLanded, double saved) React(ClassId me, ClassId them, string action, int angles = 8)
        {
            int ans = 0, la = 0, ctrl = 0, cl = 0;
            double saved = 0;
            for (int k = 0; k < angles; k++)
            {
                Seat s = PlayerSeatRig.Duel(me, them, TargetMode.RealBot, PlayerSeatRig.AngleOf(k, angles), k, PsmRig.Lag, tweak: PsmRig.Tweak);
                s.Human.Fight = false;
                s.NoTnt = true;
                s.KeepMeAbove = 0.3; s.KeepThemAbove = 0.3;
                World w = s.W;
                var wt = new PsmWatch();
                var answeredStarts = new HashSet<int>();
                var starts = new List<(int p, double d)>();
                int last = -1;
                s.BeforeTick = q =>
                {
                    wt.Update(q);
                    if (wt.TheirStart != last) { last = wt.TheirStart; starts.Add((wt.TheirStart, wt.TheirStartDist)); }
                    q.Human.MoveOverrideX = 0; q.Human.MoveOverrideZ = 0;
                    if (action == "none" || wt.Since(w) != PsmRig.Lag) return;
                    double reach = w.Config.Class(them).AttackRange + w.Config.Class(me).Radius;
                    if (wt.TheirStartDist > reach + 0.5) return;
                    double rx = w.X[q.Them] - w.X[q.Me], rz = w.Z[q.Them] - w.Z[q.Me], rd = Math.Sqrt(rx * rx + rz * rz);
                    if (rd < 1e-6) return;
                    double ux = rx / rd, uz = rz / rd, sg = (wt.TheirStart & 1) == 0 ? 1 : -1;
                    if (action == "shield") { if (w.Actors[q.Me].AbilityCd > 0) return; q.Human.PressAbility = true; answeredStarts.Add(wt.TheirStart); return; }
                    if (action == "jump")
                    {
                        // integrator: like the shield on cooldown, a jump that cannot start (still airborne, or inside the 1 s
                        // relock after an evading jump) is no answer: counting those presses measured the relock, not the evade
                        // (0.56 vs the Warrior, whose next swing usually falls inside the relock)
                        Actor me0 = w.Actors[q.Me];
                        if (me0.AirLen > 0 || (w.Moves != null && (w.Tick < w.Moves.Movement.JumpRelockUntil[q.Me] || w.Tick < w.Moves.Defence.JumpRelockUntil[q.Me]))) return;
                        q.Human.PressJump = true; answeredStarts.Add(wt.TheirStart); return;
                    }
                    if (!Dodge.CanStart(w, q.Me)) return;
                    q.Human.PressDodge = true;
                    q.Human.MoveOverrideX = action == "away" ? -ux : -uz * sg;
                    q.Human.MoveOverrideZ = action == "away" ? -uz : ux * sg;
                    answeredStarts.Add(wt.TheirStart);
                };
                PsmRig.Play(s, 30);
                int wu = w.Dur.AttackWindup[(int)them];
                double theirReach = w.Config.Class(them).AttackRange + w.Config.Class(me).Radius;
                var contacts = s.Spy.Of(SimEventType.AttackContact, e => e.A == s.Them);
                var hits = s.Spy.Of(SimEventType.Hit, e => e.A == s.Me && e.B == s.Them && e.Cause == DamageCause.Melee);
                var shieldEv = s.Spy.Of(SimEventType.HitBlocked, e => e.A == s.Me && (int)e.Value == PlayerReasons.BlockedShield);
                saved += shieldEv.Sum(e => e.Radius);
                foreach (var (p, d) in starts)
                {
                    SeatEvent c = contacts.FirstOrDefault(e => e.Tick - 1 >= p + wu - 1 && e.Tick - 1 <= p + wu + 1);
                    if (c == null) continue;
                    bool landed = hits.Any(e => e.Tick == c.Tick);
                    bool cut = shieldEv.Any(e => e.Tick == c.Tick);
                    if (answeredStarts.Contains(p)) { ans++; if (action == "shield" ? landed && !cut : landed) la++; }
                    else if (d <= theirReach) { ctrl++; if (landed) cl++; }
                }
            }
            return (ans, la, ctrl, cl, saved);
        }

        public static void Reactive_Defence_Per_Class()
        {
            foreach (ClassId me in PsmRig.All)
            {
                foreach (string act in me == ClassId.Warrior ? new[] { "none", "side", "away", "jump", "shield" } : new[] { "none", "side", "away", "jump" })
                {
                    int a = 0, la = 0, c = 0, cl = 0; double sv = 0;
                    var per = new List<string>();
                    foreach (ClassId them in PsmRig.All)
                    {
                        var r = React(me, them, act);
                        a += r.answered; la += r.landedAnswered; c += r.ctrl; cl += r.ctrlLanded; sv += r.saved;
                        per.Add(them + " " + (r.answered > 0 ? PxLog.F(1 - (double)r.landedAnswered / r.answered) : "-"));
                    }
                    Console.WriteLine("[mv] REACT | " + me + " | " + act + " | answered " + a + " avoided " + (a - la) + " (" + PxLog.F(a > 0 ? 1 - (double)la / a : double.NaN) + ") | unanswered in reach " + c + " landed " + cl + " (" + PxLog.F(c > 0 ? (double)cl / c : double.NaN) + ") | saved " + PxLog.F(sv, "0") + " | by attacker: " + string.Join(", ", per));
                }
            }
        }
    }

    public static class PsmProbeMovement
    {
        /// <summary>me at 30% runs straight away from a full-health Normal bot chaser that starts just outside its reach. 6 s.</summary>
        internal static PsmTally Escape(ClassId me, ClassId chaser, string variant, int angles = 16)
        {
            var t = new PsmTally();
            GameConfig gc = PlayerSeatRig.Config();
            double gap = gc.Class(chaser).AttackRange + gc.Class(me).Radius + 0.5;
            for (int k = 0; k < angles; k++)
            {
                // the chaser is a second HUMAN model (lag 6, aim help Low, fights): Normal bots do not pursue a runner off the
                // hill (first run: the bot came within reach in 0-1 of 8 starts), so a bot chaser measures nothing
                // along the open east-west lane through the hill (the 16 radial starts ran into the hill's rock ring in 12 of 16)
                Seat s = PlayerSeatRig.Duel(me, chaser, TargetMode.HumanVsHuman, k % 2 == 0 ? 0 : Math.PI, k, PsmRig.Lag, distance: gap, tweak: PsmRig.Tweak);
                s.Human2.LagTicks = 5 + k % 3;
                s.Human2.HoldFireTicks = k % 4;
                s.Human.Fight = false;
                s.Human2.AimHelp = PsmRig.Help;
                s.Human2.ApproachShare = 0.5;
                World w = s.W;
                w.Actors[s.Me].Hp = 0.3 * w.Actors[s.Me].MaxHp;
                var wt = new PsmWatch();
                bool engaged = false;
                int uses = 0;
                s.BeforeTick = q =>
                {
                    wt.Update(q);
                    double rx = w.X[q.Them] - w.X[q.Me], rz = w.Z[q.Them] - w.Z[q.Me], rd = Math.Sqrt(rx * rx + rz * rz);
                    if (rd < w.Config.Class(chaser).AttackRange + w.Config.Class(me).Radius) engaged = true;
                    // run DOWN THE LANE (a person steers; a straight "away" line drifted into the rock ring after a knock)
                    double lx = k % 2 == 0 ? -1 : 1, lz = Math.Max(-0.6, Math.Min(0.6, (w.Map.Hill.Z - w.Z[q.Me]) * 0.8));
                    double ll = Math.Sqrt(lx * lx + lz * lz);
                    q.Human.MoveOverrideX = lx / ll; q.Human.MoveOverrideZ = lz / ll;
                    // integrator (decision 7): the runner's eye varies 5 to 8 ticks per start, as the chaser's does (fixed 6 put every
                    // start on the same tick and gave 16 of 16 or 0 of 16), and with the switches on the chaser LUNGES (dodge-in) from
                    // just outside its reach, as lane 2's escape rig does (without it this measured no lunge at all: 16 of 16 off and on)
                    if (variant == "dodge" && wt.Since(w) == 5 + k % 4 && Dodge.CanStart(w, q.Me)) { q.Human.PressDodge = true; }
                    double creach = w.Config.Class(chaser).AttackRange + w.Config.Class(me).Radius;
                    if (w.Moves != null && rd > creach && rd < creach + 3.2 && rd > 1e-6 && Dodge.CanStart(w, q.Them))
                    {
                        q.Human2.PressDodge = true;
                        q.Human2.MoveOverrideX = (w.X[q.Me] - w.X[q.Them]) / rd;
                        q.Human2.MoveOverrideZ = (w.Z[q.Me] - w.Z[q.Them]) / rd;
                    }
                    if (variant == "dodgegap" && rd < w.Config.Class(chaser).AttackRange + w.Config.Class(me).Radius + 0.3 && Dodge.CanStart(w, q.Me)) q.Human.PressDodge = true;
                    if (variant == "kit" && PsmRig.UseKit(q, wt, me, true)) uses++;
                };
                DuelResult r = PsmRig.Play(s, 6);
                t.Reps++;
                if (!r.Lost) t.Escaped++;
                if (engaged || r.TheirSwings > 0) t.Engaged++;
                t.Uses += uses;
                t.Taken += r.Taken;
            }
            return t;
        }

        /// <summary>me chases a 29% Normal bot that is already running (S11 staging), catch within 4 s.</summary>
        internal static PsmTally Chase(ClassId me, ClassId runner, string variant, int angles = 8)
        {
            var t = new PsmTally();
            GameConfig gc = PlayerSeatRig.Config();
            double gap = gc.Class(me).AttackRange + gc.Class(runner).Radius + T.ChaseStartBeyondReach;
            for (int k = 0; k < angles; k++)
            {
                Seat s = PlayerSeatRig.Duel(me, runner, TargetMode.RealBot, PlayerSeatRig.AngleOf(k, angles), k, PsmRig.Lag, distance: gap, tweak: PsmRig.Tweak);
                s.Human.AimHelp = PsmRig.Help;
                s.W.Actors[s.Them].Hp = T.ChaseRunnerHpShare * s.W.Actors[s.Them].MaxHp;
                bool started = false;
                var wt = new PsmWatch();
                int uses = 0;
                s.BeforeTick = q =>
                {
                    World qw = q.W;
                    wt.Update(q);
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
                    double rd = PsmWatch.Dist(qw, q.Me, q.Them);
                    double reach = qw.Config.Class(me).AttackRange + qw.Config.Class(runner).Radius;
                    if (variant == "dodgein" && rd > reach && rd < reach + 3.2 && Dodge.CanStart(qw, q.Me))
                    {
                        q.Human.PressDodge = true;
                        q.Human.MoveOverrideX = (qw.X[q.Them] - qw.X[q.Me]) / rd; q.Human.MoveOverrideZ = (qw.Z[q.Them] - qw.Z[q.Me]) / rd;
                    }
                    if (variant == "kit" && PsmRig.UseKit(q, wt, me, false)) uses++;
                };
                DuelResult r = PsmRig.Play(s, T.ChaseSeconds);
                t.Reps++;
                if (r.Won) t.Wins++;
                t.Uses += uses;
            }
            return t;
        }

        public static void Escape_And_Chase_Per_Class()
        {
            foreach (ClassId me in PsmRig.All)
            {
                foreach (ClassId chaser in new[] { ClassId.Warrior, ClassId.Farmer, ClassId.Builder })
                {
                    if (chaser == me) continue;
                    var parts = new List<string>();
                    foreach (string v in new[] { "run", "dodge", "dodgegap", "kit" })
                    {
                        PsmTally e = Escape(me, chaser, v);
                        parts.Add(v + " " + e.Escaped + "/" + e.Reps + " taken " + PxLog.F(e.Taken / e.Reps, "0") + (v == "kit" ? " (uses " + e.Uses + ")" : ""));
                        if (v == "run") parts[parts.Count - 1] += " engaged " + e.Engaged;
                    }
                    Console.WriteLine("[mv] ESCAPE | " + me + " at 30% from a lag-6 human " + chaser + ", 6 s, open lane, 16 starts | " + string.Join(" | ", parts));
                }
                var cp = new List<string>();
                foreach (string v in new[] { "plain", "dodgein", "kit" })
                {
                    var all = new PsmTally();
                    foreach (ClassId runner in PsmRig.All) if (runner != me) all.Add(Chase(me, runner, v));
                    cp.Add(v + " " + all.Wins + "/" + all.Reps + " (" + PxLog.F(all.Share) + ")" + (v == "kit" ? " uses " + all.Uses : ""));
                }
                Console.WriteLine("[mv] CHASE | " + me + " chasing a running 29% bot (5 classes x 8), catch within 4 s | " + string.Join(" | ", cp));
            }
        }
    }
}

namespace CriticalMass.Sim.Tests.PlayerSeat
{
    public static class PsmProbeMap
    {
        public static void Map_Around_Hill()
        {
            Seat s = PlayerSeatRig.Duel(ClassId.Farmer, ClassId.Warrior, TargetMode.Passive, 0, 0, tweak: PsmRig.Tweak);
            World w = s.W;
            var m = w.Map;
            int hx = (int)w.Map.Hill.X, hz = (int)w.Map.Hill.Z;
            Console.WriteLine("[mv] MAP " + m.Width + "x" + m.Height + " hill " + w.Map.Hill.X + "," + w.Map.Hill.Z);
            for (int z = Math.Max(0, hz - 26); z < Math.Min(m.Height, hz + 27); z++)
            {
                var sb = new System.Text.StringBuilder();
                for (int x = Math.Max(0, hx - 34); x < Math.Min(m.Width, hx + 35); x++)
                {
                    byte c = m.Cells[z * m.Width + x];
                    bool walk = CriticalMass.Sim.Grid.MapGrid.IsWalkable(m, x, z);
                    sb.Append(x == hx && z == hz ? 'H' : walk ? '.' : (char)('A' + (c % 26)));
                }
                Console.WriteLine("[mv] MAP " + sb);
            }
        }
    }
}

namespace CriticalMass.Sim.Tests.PlayerSeat
{
    public static class PsmProbeMineTrace
    {
        public static void Mine_While_Fleeing_Trace()
        {
            GameConfig gc = PlayerSeatRig.Config();
            double gap = gc.Class(ClassId.Farmer).AttackRange + gc.Class(ClassId.Demolitions).Radius + 0.5;
            foreach (bool kit in new[] { false, true })
            {
                Seat s = PlayerSeatRig.Duel(ClassId.Demolitions, ClassId.Farmer, TargetMode.HumanVsHuman, 0, 0, PsmRig.Lag, distance: gap, tweak: PsmRig.Tweak);
                s.Human.Fight = false; s.Human2.AimHelp = 1; s.Human2.ApproachShare = 0.5;
                World w = s.W;
                w.Actors[0].Hp = 0.3 * w.Actors[0].MaxHp;
                int pressed = -1;
                s.BeforeTick = q =>
                {
                    double rx = w.X[q.Them] - w.X[0], rz = w.Z[q.Them] - w.Z[0], rd = Math.Sqrt(rx * rx + rz * rz);
                    if (rd > 1e-6) { q.Human.MoveOverrideX = -rx / rd; q.Human.MoveOverrideZ = -rz / rd; }
                    if (kit && w.Actors[0].AbilityCd <= 0 && rd <= 4) { q.Human.PressAbility = true; q.Human.AimOverrideX = w.X[0]; q.Human.AimOverrideZ = w.Z[0]; if (pressed < 0) pressed = w.Tick; }
                    if (w.Tick % 15 == 0) Console.WriteLine("[mv] TRACE kit " + kit + " t " + w.Tick + " me " + PxLog.F(w.X[0]) + "," + PxLog.F(w.Z[0]) + " v " + PxLog.F(w.Vx[0]) + " them " + PxLog.F(w.X[q.Them]) + " gap " + PxLog.F(rd) + " hp " + PxLog.F(w.Actors[0].Hp, "0"));
                };
                PsmRig.Play(s, 6);
                foreach (SeatEvent e in s.Spy.All.Where(e => (e.A == 0 || e.B == 0) && e.Type != SimEventType.AttackStart && e.Type != SimEventType.Knock).Take(40))
                    Console.WriteLine("[mv] TRACE kit " + kit + " ev " + e + " cause " + e.Cause);
            }
        }
    }
}

namespace CriticalMass.Sim.Tests.PlayerSeat
{
    /// <summary>Player vs player: the verbs bots never exercise (feint baits a human's dodge; hit-cancel; dodge vs a human).</summary>
    public static class PsmProbePvp
    {
        internal static PsmTally Cell(ClassId me, ClassId them, string variant)
        {
            var t = new PsmTally();
            for (int k = 0; k < 16; k++)
            {
                Seat s = PlayerSeatRig.Duel(me, them, TargetMode.HumanVsHuman, PlayerSeatRig.AngleOf(k, 16), k, PsmRig.Lag, tweak: PsmRig.Tweak);
                s.Human.AimHelp = PsmRig.Help; s.Human2.AimHelp = PsmRig.Help;
                s.Human2.LagTicks = 6; s.Human2.HoldFireTicks = k % 4;
                s.Human2.Dodges = true;            // the opponent dodges every swing it reads (6 ticks late)
                s.NoTnt = true;
                if (variant == "feint" || variant == "all") s.Human.Feints = true;
                if (variant == "hitcancel" || variant == "all") s.Human.HitCancels = true;
                if (variant == "dodge" || variant == "all") s.Human.Dodges = true;
                DuelResult r = PsmRig.Play(s, 30);
                t.Reps++; if (r.Won) t.Wins++; if (r.Lost) t.Losses++;
                t.Swings += r.Swings; t.Landed += r.Landed;
                t.HpLeft += s.W.Actors[0].Alive ? s.W.Actors[0].Hp / s.W.Actors[0].MaxHp : 0;
                t.Uses += s.Human.FeintCount;
                PsmRig.CountCrits(s, t);
            }
            return t;
        }

        public static void Pvp_VerbValue_VsADodgingHuman()
        {
            foreach (ClassId me in PsmRig.All)
                foreach (string v in new[] { "base", "feint", "hitcancel", "dodge", "all" })
                {
                    var row = new PsmTally();
                    foreach (ClassId them in PsmRig.All) row.Add(Cell(me, them, v));
                    Console.WriteLine("[mv] PVP | " + me + " vs a dodging lag-6 human of each class (6 x 16) | " + v + " | won " + row.Wins + " lost " + row.Losses + " of " + row.Reps + " land " + PxLog.F(row.Land) + " crit " + row.Crits + "/" + row.MyHits + " feints " + row.Uses);
                }
        }
    }
}

namespace CriticalMass.Sim.Tests.PlayerSeat
{
    [TestFixture, Category("PlayerSeat")]
    public sealed class PlayerSeatMovesTests
    {
        [TearDown]
        public void SwitchesBackOff() => PsmRig.Tweak = null;

        static string Tag => PsmRig.On ? "on" : "off";

        /// <summary>SPEC section 3: the one-on-one table with every switch on (base model vs Normal bots, 40 duels each).</summary>
        [Test, Category("Slow")]
        public void S62_DuelTableWithEverySwitchOnMeetsSection3()
        {
            var on = new Dictionary<ClassId, int>();
            foreach (bool sw in new[] { false, true })
            {
                PsmRig.Tweak = sw ? PsmRig.AllOn : null;
                foreach (ClassId me in PsmRig.All)
                {
                    PsmTally r = PsmRig.DuelRow(me, "base");
                    Console.WriteLine("[mv] S62 DUEL | " + Tag + " | " + me + " | base | " + r.Duel());
                    if (sw) on[me] = r.Wins;
                }
            }
            PsmRig.Tweak = null;
            PsmRig.Checks(() =>
            {
                Assert.That(on[ClassId.Warrior], Is.GreaterThanOrEqualTo(36), "Warrior at least 36 of 40");
                Assert.That(on[ClassId.Farmer], Is.InRange(22, 30), "Farmer 22 to 30");
                Assert.That(on[ClassId.Miner], Is.InRange(20, 30), "Miner 20 to 30");
                Assert.That(on[ClassId.Demolitions], Is.InRange(24, 30), "Demolitions 24 to 30");
                Assert.That(on[ClassId.Builder], Is.InRange(20, 28), "Builder 20 to 28");
                Assert.That(on[ClassId.Healer], Is.LessThanOrEqualTo(8), "Healer at most 8");
            });
        }

        static double PassiveTtk(ClassId me, ClassId tgt)
        {
            Seat p = PlayerSeatRig.Duel(me, tgt, TargetMode.Passive, 0, 0, 0, tweak: PsmRig.Tweak);
            p.NoTnt = true;
            DuelResult pr = PsmRig.Play(p, 30);
            return pr.Won ? pr.Seconds : double.NaN;
        }

        /// <summary>T2-3 (c): passive time to kill falls 5 to 15% per class with the switches on (combos); T1-1: a TNT thrown
        /// from F at 2.5 to 6 u hurts a Normal bot at least 0.50 of the time.</summary>
        [Test, Category("Slow")]
        public void S63_PassiveTimeToKillAndTheTntKey()
        {
            var falls = new Dictionary<ClassId, double>();
            foreach (ClassId me in PsmRig.All)
            {
                double off = 0, on = 0;
                foreach (ClassId tgt in new[] { ClassId.Warrior, ClassId.Farmer, ClassId.Builder })
                {
                    PsmRig.Tweak = null; off += PassiveTtk(me, tgt);
                    PsmRig.Tweak = PsmRig.AllOn; on += PassiveTtk(me, tgt);
                }
                PsmRig.Tweak = null;
                falls[me] = off > 0 ? 1 - on / off : double.NaN;
                Console.WriteLine("[mv] S63 TTK | " + me + " | off " + PxLog.F(off / 3) + " s on " + PxLog.F(on / 3) + " s | fall " + PxLog.F(falls[me]));
            }
            int throws = 0, hurt = 0;
            PsmRig.Tweak = PsmRig.AllOn;
            foreach (ClassId tgt in new[] { ClassId.Warrior, ClassId.Farmer, ClassId.Healer })
                for (int k = 0; k < 8; k++)
                {
                    Seat s = PlayerSeatRig.Duel(ClassId.Demolitions, tgt, TargetMode.RealBot, PlayerSeatRig.AngleOf(k, 8), k, PsmRig.Lag, distance: 4.5, tweak: PsmRig.Tweak);
                    s.Human.AimHelp = PsmRig.Help;
                    s.Human.ApproachShare = 3.0;
                    s.Human.Fight = false;
                    s.KeepThemAbove = 0.3; s.KeepMeAbove = 0.3;
                    s.BeforeTick = q =>
                    {
                        double d = PsmWatch.Dist(q.W, 0, q.Them);
                        if (q.W.Tick >= q.W.Demolitions.TntReadyTick[0] && d >= 2.5 && d <= 6) q.Human.PressDigPlace = true;
                    };
                    PsmRig.Play(s, 20);
                    throws += s.Spy.Of(SimEventType.TntThrown, e => e.A == 0).Count;
                    foreach (SeatEvent e in s.Spy.Of(SimEventType.Explosion, x => x.A == 0 && x.B == Demolitions.ExplosionTnt))
                        if (s.Spy.Of(SimEventType.Hit, h => h.B == 0 && h.A == s.Them && h.Tick == e.Tick && h.Cause == DamageCause.Explosion).Count > 0) hurt++;
                }
            PsmRig.Tweak = null;
            double share = throws > 0 ? (double)hurt / throws : 0;
            Console.WriteLine("[mv] S63 TNT F key | on | throws " + throws + " hurt " + hurt + " (" + PxLog.F(share) + ")");
            PsmRig.Checks(() =>
            {
                foreach (var kv in falls) Assert.That(kv.Value, Is.InRange(0.05, 0.15), kv.Key + ": passive time to kill falls 5 to 15%");
                Assert.That(throws, Is.GreaterThan(0), "F throws TNT with DemoTntOnDigOnly on");
                Assert.That(share, Is.GreaterThanOrEqualTo(0.50), "a TNT from F hurts a Normal bot at least 0.50");
            });
        }

        /// <summary>T2-7: a jump pressed 6 ticks into the wind-up avoids at least 0.80 of Warrior, Builder and Miner swings
        /// and at most 0.40 of the 7-tick swings (Demolitions, Healer). T1-3: the side dodge answers at least 0.30 of the
        /// swings in reach for every class.</summary>
        [Test, Category("Slow")]
        public void S64_ReactiveDefenceJumpEvadeAndDodgeCoverage()
        {
            PsmRig.Tweak = PsmRig.AllOn;
            var jumpAns = new Dictionary<ClassId, int>();
            var jumpLand = new Dictionary<ClassId, int>();
            foreach (ClassId them in PsmRig.All) { jumpAns[them] = 0; jumpLand[them] = 0; }
            var cover = new Dictionary<ClassId, double>();
            foreach (ClassId me in PsmRig.All)
            {
                int a = 0, c = 0;
                foreach (ClassId them in PsmRig.All)
                {
                    var j = PsmProbeDefence.React(me, them, "jump");
                    jumpAns[them] += j.answered; jumpLand[them] += j.landedAnswered;
                    var s = PsmProbeDefence.React(me, them, "side");
                    a += s.answered; c += s.ctrl;
                }
                cover[me] = a + c > 0 ? (double)a / (a + c) : 0;
                Console.WriteLine("[mv] S64 REACT | on | " + me + " | side answered " + a + " unanswered in reach " + c + " (" + PxLog.F(cover[me]) + ")");
            }
            PsmRig.Tweak = null;
            foreach (ClassId them in PsmRig.All)
                Console.WriteLine("[mv] S64 JUMP | on | vs " + them + " | answered " + jumpAns[them] + " avoided " + (jumpAns[them] - jumpLand[them]));
            PsmRig.Checks(() =>
            {
                foreach (ClassId t in new[] { ClassId.Warrior, ClassId.Builder, ClassId.Miner })
                    if (jumpAns[t] > 0) Assert.That(1 - (double)jumpLand[t] / jumpAns[t], Is.GreaterThanOrEqualTo(0.80), "jump avoids " + t);
                foreach (ClassId t in new[] { ClassId.Demolitions, ClassId.Healer })
                    if (jumpAns[t] > 0) Assert.That(1 - (double)jumpLand[t] / jumpAns[t], Is.LessThanOrEqualTo(0.40), "jump is a read vs " + t);
                foreach (var kv in cover) Assert.That(kv.Value, Is.GreaterThanOrEqualTo(0.30), kv.Key + ": the dodge answers at least 0.30");
            });
        }

        /// <summary>T1-2: escape at 30% with one dodge from a lunging lag-6 human Farmer (the fastest chaser): 6 to 13 of 16.</summary>
        [Test, Category("Slow")]
        public void S65_EscapeWithOneDodgeFromALungingChaser()
        {
            var got = new Dictionary<ClassId, int>();
            foreach (ClassId me in new[] { ClassId.Warrior, ClassId.Miner, ClassId.Demolitions, ClassId.Builder })
            {
                PsmRig.Tweak = null;
                PsmTally off = PsmProbeMovement.Escape(me, ClassId.Farmer, "dodge");
                PsmRig.Tweak = PsmRig.AllOn;
                PsmTally on = PsmProbeMovement.Escape(me, ClassId.Farmer, "dodge");
                got[me] = on.Escaped;
                Console.WriteLine("[mv] S65 ESCAPE | " + me + " from a lag-6 human Farmer, one dodge | off " + off.Escaped + "/16 on " + on.Escaped + "/16");
            }
            PsmRig.Tweak = null;
            PsmRig.Checks(() => { foreach (var kv in got) Assert.That(kv.Value, Is.InRange(6, 13), kv.Key + ": a chance of escape, not a certainty"); });
        }

        /// <summary>T1-1: Demolitions catches a fleeing 29% bot at least 0.40; T1-2: dodge-in beats plain by 0.10 for every
        /// class; the Warrior's plain catch stays in S11's 0.40 to 0.80.</summary>
        [Test, Category("Slow")]
        public void S66_ChaseCatchRates()
        {
            PsmRig.Tweak = PsmRig.AllOn;
            var plain = new Dictionary<ClassId, double>();
            var dodgein = new Dictionary<ClassId, double>();
            foreach (ClassId me in PsmRig.All)
            {
                var p = new PsmTally(); var d = new PsmTally();
                foreach (ClassId runner in PsmRig.All)
                {
                    if (runner == me) continue;
                    p.Add(PsmProbeMovement.Chase(me, runner, "plain"));
                    d.Add(PsmProbeMovement.Chase(me, runner, "dodgein"));
                }
                plain[me] = p.Share; dodgein[me] = d.Share;
                Console.WriteLine("[mv] S66 CHASE | on | " + me + " | plain " + PxLog.F(p.Share) + " dodgein " + PxLog.F(d.Share));
            }
            PsmRig.Tweak = null;
            PsmRig.Checks(() =>
            {
                Assert.That(plain[ClassId.Demolitions], Is.GreaterThanOrEqualTo(0.40), "Demolitions chase");
                Assert.That(plain[ClassId.Warrior], Is.InRange(0.40, 0.80), "Warrior plain chase in the S11 band");
                foreach (ClassId me in PsmRig.All) Assert.That(dodgein[me], Is.GreaterThanOrEqualTo(plain[me] + 0.10 - 1e-9), me + ": dodge-in at least plain + 0.10");
            });
        }

        /// <summary>PvP verb value against a dodging lag-6 human (no spec number: printed for the combine), both settings.</summary>
        [Test, Category("Slow")]
        public void S67_PvpVerbValueIsReported()
        {
            int reps = 0;
            foreach (bool sw in new[] { false, true })
            {
                PsmRig.Tweak = sw ? PsmRig.AllOn : null;
                foreach (ClassId me in PsmRig.All)
                {
                    var row = new PsmTally();
                    foreach (ClassId them in PsmRig.All) row.Add(PsmProbePvp.Cell(me, them, "all"));
                    reps += row.Reps;
                    Console.WriteLine("[mv] S67 PVP | " + Tag + " | " + me + " | all | won " + row.Wins + " lost " + row.Losses + " of " + row.Reps + " land " + PxLog.F(row.Land));
                }
            }
            PsmRig.Tweak = null;
            Assert.That(reps, Is.EqualTo(2 * 6 * 6 * 16));
        }

        /// <summary>The probe's map print around the hill (the lanes' escape staging reads it); the hill cell is walkable.</summary>
        [Test]
        public void S68_MapAroundTheHill()
        {
            PsmProbeMap.Map_Around_Hill();
            Seat s = PlayerSeatRig.Duel(ClassId.Farmer, ClassId.Warrior, TargetMode.Passive, 0, 0);
            Assert.That(CriticalMass.Sim.Grid.MapGrid.IsWalkable(s.W.Map, (int)s.W.Map.Hill.X, (int)s.W.Map.Hill.Z), Is.True);
        }

        /// <summary>The probe's mine-while-fleeing trace, with the switches on (prints; it must run clean).</summary>
        [Test, Category("Slow")]
        public void S69_MineWhileFleeingTraceRuns()
        {
            PsmRig.Tweak = PsmRig.AllOn;
            Assert.DoesNotThrow(PsmProbeMineTrace.Mine_While_Fleeing_Trace);
            PsmRig.Tweak = null;
        }

        /// <summary>Switches present but every one OFF: a player-seat duel is identical to the shipped rules (no switch leaks).</summary>
        [Test]
        public void S70_EverySwitchOffChangesNothingAtTheSeat()
        {
            PsmRig.Tweak = null;
            PsmTally a = PsmRig.DuelCell(ClassId.Warrior, ClassId.Farmer, "strong", 2);
            PsmRig.Tweak = c => MovesConfig.AllOff(c.Depth.Moves);
            PsmTally b = PsmRig.DuelCell(ClassId.Warrior, ClassId.Farmer, "strong", 2);
            PsmRig.Tweak = null;
            Console.WriteLine("[mv] S70 | shipped " + a.Duel() + " | switches present, all off " + b.Duel());
            PsmRig.Checks(() =>
            {
                Assert.That(b.Wins, Is.EqualTo(a.Wins));
                Assert.That(b.Swings, Is.EqualTo(a.Swings));
                Assert.That(b.Landed, Is.EqualTo(a.Landed));
                Assert.That(b.Taken, Is.EqualTo(a.Taken));
                Assert.That(b.HpLeft, Is.EqualTo(a.HpLeft));
            });
        }
    }
}
