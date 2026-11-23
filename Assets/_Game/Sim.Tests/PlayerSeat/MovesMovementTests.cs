using System;
using System.Collections.Generic;
using System.Linq;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;
using T = CriticalMass.Sim.Tests.PlayerSeat.PlayerSeatTargets;

namespace CriticalMass.Sim.Tests.PlayerSeat
{
    /// <summary>
    /// Class moves lane 3 (movement): T2-4 TRAVEL, T2-5 SLIDE, T2-6 GLIDE (Healer), T2-7 the evade landing lock. Every number is
    /// the human seat (actor 0) through MatchTick.AdvanceHumans (PlayerSeatRig), lag 6 unless a line says otherwise. Each test
    /// prints its "[px] MV3 ..." line with the measured value, before and after where the switch changes a number.
    /// </summary>
    [TestFixture, Category("PlayerSeat")]
    public sealed class MovesMovementTests
    {
        const int Lag = 6;

        static Action<GameConfig> Sw(bool travel, bool slide = false, bool glide = false, bool evade = false) => c =>
        {
            MovesConfig m = c.Depth.Moves;
            m.Travel = travel;
            m.Slide = slide;
            m.Glide = glide;
            m.JumpEvade = evade;
        };

        static MovementMovesState M(World w) => w.Moves.Movement;

        /// <summary>The seat's map repainted: void everywhere, floor (x0..x1, z0..z1) and any extra rectangles; the hill, pads,
        /// shrine and nodes moved off it (as PlayerSeatDefenceTests.Strip does).</summary>
        static void Paint(World w, params int[][] floors)
        {
            MapDef map = w.Map;
            Array.Fill(map.Cells, Cell.Void);
            Array.Clear(map.WallHp, 0, map.WallHp.Length);
            foreach (int[] r in floors)
                for (int z = r[1]; z <= r[3]; z++)
                    for (int x = r[0]; x <= r[2]; x++) map.Cells[z * map.Width + x] = Cell.Floor;
            for (int t = 0; t < 2; t++) { map.Pads[t].X = 3.5; map.Pads[t].Z = 13.5; }
            map.Hill.X = 100.5; map.Hill.Z = 100.5; map.Hill.R = 3;
            map.Shrine.X = 104.5; map.Shrine.Z = 100.5;
            foreach (ResourceNode nd in w.Nodes) { nd.X = -100; nd.Z = -100; }
            w.GridVersion++;
        }

        static readonly int[] Flat = { 2, 4, 90, 26 };

        static double Dist(World w, int a, int b) { double dx = w.X[b] - w.X[a], dz = w.Z[b] - w.Z[a]; return Math.Sqrt(dx * dx + dz * dz); }

        // ================================================================== T2-4 travel

        [Test]
        public void Travel_LightsAfterOneSecondRampsTo130AndKeepsTheWarriorSlowest()
        {
            var rows = new List<string>();
            var lit = new Dictionary<ClassId, double>();
            bool ok = true;
            foreach (ClassId c in T.Classes)
            {
                Seat s = PlayerSeatRig.Field(c, x: 5.5, z: 15.5, tweak: Sw(true));
                World w = s.W;
                Paint(w, Flat);
                int litAt = -1, t0 = w.Tick;
                double x30 = 0;
                for (int t = 1; t <= 90; t++)
                {
                    s.Human.MoveOverrideX = 1; s.Human.MoveOverrideZ = 0;
                    s.Step();
                    if (litAt < 0 && MovesHooks.MovementTravelling(w, 0)) litAt = t;
                    if (t == 60) x30 = w.X[0];
                }
                double speed = (w.X[0] - x30) / 30 * w.Config.SimHz, walk = w.Config.Class(c).MoveSpeed, ratio = speed / walk;
                lit[c] = speed;
                bool row = litAt == T30(w, w.Config.Depth.Moves.TravelAfterMoveTicks) && Math.Abs(ratio - w.Config.Depth.Moves.TravelSpeedMul) <= 0.02   /* integrator: decision 5 reads the knobs (x1.5 after 15) */ && s.Spy.Of(SimEventType.TravelChanged, e => e.A == 0 && e.Value == 1).Count == 1;
                ok &= row;
                rows.Add(c + " lit at tick " + litAt + ", " + PxLog.F(speed) + " u/s = x" + PxLog.F(ratio, "0.000"));
            }
            bool slowest = lit.Where(p => p.Key != ClassId.Warrior).All(p => p.Value > lit[ClassId.Warrior]);
            ok &= slowest;
            Assert.IsTrue(PxLog.Line("MV3 T2-4 Travel lights and ramps", "hold a move 3 s in the open, no enemy", string.Join("; ", rows) + "; Warrior slowest " + slowest,
                "lit at TravelAfterMoveTicks, x TravelSpeedMul +-0.02, the Warrior the slowest", ok));
        }

        [Test]
        public void Travel_EndsAtOnceOnAPressAnEnemyOrAHitAndAHitBlocksItForThreeSeconds()
        {
            ClassId c = ClassId.Miner;
            Seat s = PlayerSeatRig.Field(c, x: 5.5, z: 15.5, tweak: Sw(true));
            World w = s.W;
            Paint(w, Flat);
            int ib = PlayerSeatRig.EnemySlot(w);
            void Walk(int n) { for (int t = 0; t < n; t++) { s.Human.MoveOverrideX = 1; s.Human.MoveOverrideZ = 0; s.Step(); } }
            Walk(40);
            bool litBefore = MovesHooks.MovementTravelling(w, 0);
            // (1) an attack press ends it the same tick
            s.Human.MoveOverrideX = 1; s.Human.PressAttack = true; s.Step();
            bool endsOnPress = !MovesHooks.MovementTravelling(w, 0);
            Walk(40);
            bool relitAfterPress = MovesHooks.MovementTravelling(w, 0);
            // (2) an enemy body within 8 u ends it
            PlayerSeatRig.Place(w, ib, ClassId.Farmer, w.X[0] + 7.8, w.Z[0] + 1.5);
            s.Dummy = new DummyModel { Me = ib, Around = 0 };
            s.Human.MoveOverrideX = 0; s.Human.MoveOverrideZ = 1; s.Step();
            bool endsOnEnemy = !MovesHooks.MovementTravelling(w, 0);
            PlayerSeatRig.Remove(w, ib);
            s.Dummy = null;
            // (3) a hit ends it and blocks it for 3 s
            Walk(40);
            bool relitAfterEnemy = MovesHooks.MovementTravelling(w, 0);
            PlayerSeatRig.Place(w, ib, ClassId.Farmer, w.X[0] + 30, w.Z[0]);
            s.Dummy = new DummyModel { Me = ib, Around = 0 };
            Damage.ApplyDamage(w, 0, 5, ib, w.X[ib], w.Z[ib]);
            bool endsOnHit = !MovesHooks.MovementTravelling(w, 0);
            int hitTick = w.Tick, relit = -1;
            for (int t = 0; t < 150 && relit < 0; t++)
            {
                s.Human.MoveOverrideX = 1; s.Step();
                if (MovesHooks.MovementTravelling(w, 0)) relit = w.Tick - hitTick;
            }
            bool ok = litBefore && endsOnPress && relitAfterPress && endsOnEnemy && relitAfterEnemy && endsOnHit && relit == 90;
            Assert.IsTrue(PxLog.Line("MV3 T2-4 Travel ends at once", "Miner travelling: attack press, an enemy at 7.9 u, a hit",
                "lit " + litBefore + ", press ends " + endsOnPress + ", enemy ends " + endsOnEnemy + ", hit ends " + endsOnHit + ", relit " + relit + " ticks after the hit",
                "every one ends it the same tick; relit 90 ticks (3 s) after the hit", ok));
        }

        [Test]
        public void Travel_JumpsTakeOffAtWalkSpeedSoNoNewGapsOpen()
        {
            // a x1.30 takeoff would carry 3.2 u instead of 2.5 u and open 2-cell void gaps to every class (the glide's job)
            double[] flight = new double[2];
            for (int on = 0; on < 2; on++)
            {
                Seat s = PlayerSeatRig.Field(ClassId.Farmer, x: 5.5, z: 15.5, tweak: Sw(on == 1));
                World w = s.W;
                Paint(w, Flat);
                for (int t = 0; t < 60; t++) { s.Human.MoveOverrideX = 1; s.Step(); }
                bool lit = MovesHooks.MovementTravelling(w, 0);
                s.Human.MoveOverrideX = 1; s.Human.PressJump = true; s.Step();
                Actor a = w.Actors[0];
                flight[on] = a.AirLen > 0 ? a.AirToX - a.AirFromX : -1;
                if (on == 1) Assert.IsTrue(lit, "travel lit before the jump");
            }
            Assert.IsTrue(PxLog.Line("MV3 T2-4 Travel jump length", "Farmer jumps after 2 s of walking, travel off vs lit",
                "flight " + PxLog.F(flight[0]) + " u vs " + PxLog.F(flight[1]) + " u", "the same (within 0.05 u)", flight[0] > 0 && Math.Abs(flight[0] - flight[1]) <= 0.05));
        }

        // ---- escape and chase bands with the movement switches on (the ruling: a chance of escape, not a certainty)

        /// <summary>The probe's ESCAPE staging (MovesAuditProbe.Escape): me at 30% runs down the open east-west lane from a
        /// lag-6 human chaser that starts just outside its reach; variant run or dodge; seconds long; startGap null = the probe's.</summary>
        static int Escape(ClassId me, ClassId chaser, string variant, Action<GameConfig> tweak, double seconds = 6, double startGap = double.NaN)
        {
            GameConfig gc = PlayerSeatRig.Config();
            double gap = double.IsNaN(startGap) ? gc.Class(chaser).AttackRange + gc.Class(me).Radius + 0.5 : startGap;
            int escaped = 0;
            for (int k = 0; k < 16; k++)
            {
                Seat s = PlayerSeatRig.Duel(me, chaser, TargetMode.HumanVsHuman, k % 2 == 0 ? 0 : Math.PI, k, Lag, distance: gap, tweak: tweak);
                s.Human2.LagTicks = 5 + k % 3;
                s.Human2.HoldFireTicks = k % 4;
                s.Human.Fight = false;
                s.Human2.AimHelp = 1;
                s.Human2.ApproachShare = 0.5;
                World w = s.W;
                w.Actors[s.Me].Hp = 0.3 * w.Actors[s.Me].MaxHp;
                int theirStart = -100000;
                int head = 0;
                s.BeforeTick = q =>
                {
                    var all = q.Spy.All;
                    for (; head < all.Count; head++)
                        if (all[head].Type == SimEventType.AttackStart && all[head].A == q.Them && all[head].B == Combat.KindAttack) theirStart = all[head].Tick - 1;
                    double lx = k % 2 == 0 ? -1 : 1, lz = Math.Max(-0.6, Math.Min(0.6, (w.Map.Hill.Z - w.Z[q.Me]) * 0.8));
                    double ll = Math.Sqrt(lx * lx + lz * lz);
                    q.Human.MoveOverrideX = lx / ll; q.Human.MoveOverrideZ = lz / ll;
                    if (variant == "dodge" && w.Tick - theirStart == Lag && Dodge.CanStart(w, q.Me)) q.Human.PressDodge = true;
                };
                DuelResult r = PlayerSeatRig.Play(s, seconds);
                if (!r.Lost) escaped++;
            }
            return escaped;
        }

        [Test, Category("Slow")]
        public void Travel_EscapeBandsPerClassBeforeAndAfter()
        {
            // the probe's staging (just outside reach, 6 s) and a far start (9 u, past the 8 u travel line, 12 s), each against
            // the faster human chasers; travel + slide on vs every switch off. Reported, and asserted only where the switch
            // could move it: with travel on no class becomes uncatchable where it was catchable at the near start.
            var rows = new List<string>();
            bool ok = true;
            foreach (ClassId me in T.Classes)
                foreach (ClassId chaser in new[] { ClassId.Warrior, ClassId.Farmer, ClassId.Builder })
                {
                    if (chaser == me) continue;
                    int nearOff = Escape(me, chaser, "run", null), nearOn = Escape(me, chaser, "run", Sw(true, true));
                    int dodgeOff = Escape(me, chaser, "dodge", null), dodgeOn = Escape(me, chaser, "dodge", Sw(true, true));
                    int farOff = Escape(me, chaser, "run", null, 12, 9), farOn = Escape(me, chaser, "run", Sw(true, true), 12, 9);
                    ok &= nearOn == nearOff && dodgeOn == dodgeOff;
                    rows.Add(me + " from " + chaser + ": run " + nearOff + "->" + nearOn + ", dodge " + dodgeOff + "->" + dodgeOn + ", far run " + farOff + "->" + farOn);
                    Console.WriteLine("[mv3] ESCAPE | " + rows[rows.Count - 1] + " (of 16, off->on)");
                }
            Assert.IsTrue(PxLog.Line("MV3 T2-4 Escape bands", "per class, 16 starts each, off->on (travel + slide)", string.Join("; ", rows),
                "the near-start bands do not move (travel never lights inside 8 u)", ok));
        }

        [Test]
        public void Travel_S11ChaseBandHolds()
        {
            // S11 staging: a lag-6 human chases a running 29% Normal bot from just past reach, catch within 4 s; switches on
            int[] caught = new int[2];
            for (int on = 0; on < 2; on++)
                foreach (ClassId me in new[] { ClassId.Warrior, ClassId.Miner, ClassId.Builder })
                    foreach (ClassId runner in T.Classes)
                    {
                        if (runner == me) continue;
                        GameConfig gc = PlayerSeatRig.Config();
                        double gap = gc.Class(me).AttackRange + gc.Class(runner).Radius + T.ChaseStartBeyondReach;
                        for (int k = 0; k < 4; k++)
                        {
                            Seat s = PlayerSeatRig.Duel(me, runner, TargetMode.RealBot, PlayerSeatRig.AngleOf(k, 4), k, Lag, distance: gap, tweak: on == 1 ? Sw(true, true) : null);
                            s.Human.AimHelp = 1;
                            s.W.Actors[s.Them].Hp = T.ChaseRunnerHpShare * s.W.Actors[s.Them].MaxHp;
                            bool started = false;
                            s.BeforeTick = q =>
                            {
                                if (started) return;
                                started = true;
                                q.W.Actors[q.Them].AiState = AiState.Retreat;
                                var bs = BotBrain.GetBotsState(q.W);
                                if (bs != null) bs.StateSince[q.Them] = q.W.Match.Time;
                            };
                            if (PlayerSeatRig.Play(s, T.ChaseSeconds).Won) caught[on]++;
                        }
                    }
            int n = 3 * 5 * 4;
            double off = caught[0] / (double)n, onShare = caught[1] / (double)n;
            Assert.IsTrue(PxLog.Line("MV3 T2-4 S11 chase holds", "Warrior / Miner / Builder chase a running 29% bot, 60 chases, off -> on",
                PxLog.F(off) + " -> " + PxLog.F(onShare), "unchanged (the runner and the chaser are inside 8 u: nobody travels)", caught[0] == caught[1]));
        }

        // ================================================================== T2-5 slide

        [Test]
        public void Slide_TripsAnAimedPassiveDummyEveryTime()
        {
            int trips = 0, tries = 0, slides = 0, dodges = 0;
            var rows = new List<string>();
            foreach (ClassId c in T.Classes)
            {
                int ct = 0;
                for (int k = 0; k < 8; k++)
                {
                    Seat s = PlayerSeatRig.Field(c, x: 5.5, z: 15.5, tweak: Sw(true, true));
                    World w = s.W;
                    Paint(w, Flat);
                    int ib = PlayerSeatRig.EnemySlot(w);
                    PlayerSeatRig.Place(w, ib, ClassId.Warrior + (k % 6), 5.5 + 15 + k * 0.4, 15.5 + (k % 3 - 1) * 0.3);
                    s.Dummy = new DummyModel { Me = ib, Around = 0 };
                    tries++;
                    double reach = w.Config.Depth.Moves.SlideDistance + Collide.ActorRadius(w, 0) + Collide.ActorRadius(w, ib) - 0.2;
                    bool pressed = false;
                    // the press decision reads the distance Lag ticks old (a human's reaction)
                    var hist = new double[64];
                    for (int t = 0; t < 150; t++)
                    {
                        double d = Dist(w, 0, ib);
                        hist[w.Tick & 63] = d;
                        double seen = t >= Lag ? hist[(w.Tick - Lag) & 63] : 99;
                        s.Human.MoveOverrideX = (w.X[ib] - w.X[0]) / d; s.Human.MoveOverrideZ = (w.Z[ib] - w.Z[0]) / d;
                        if (!pressed && seen <= reach + 0.6 && MovesHooks.MovementSlideArmed(w, 0)) { s.Human.PressDodge = true; pressed = true; }
                        s.Step();
                        if (s.Spy.Of(SimEventType.Slide, e => e.A == 0 && e.B == ib).Count > 0) break;
                    }
                    if (s.Spy.Of(SimEventType.Slide, e => e.A == 0 && e.B == ib).Count > 0) { trips++; ct++; }
                    slides += s.Spy.Of(SimEventType.Slide, e => e.A == 0 && e.B < 0).Count;
                    dodges += s.Spy.Of(SimEventType.Dodge, e => e.A == 0).Count;
                }
                rows.Add(c + " " + ct + "/8");
            }
            Assert.IsTrue(PxLog.Line("MV3 T2-5 Slide trip on a passive dummy", "travel in from 15 u, Dodge pressed (lag 6) once the slide reaches the body",
                trips + "/" + tries + " (" + string.Join(", ", rows) + "); slides " + slides + ", dodges " + dodges, "1.0 of aimed slides trip; the Dodge press is never a dodge here", trips == tries && dodges == 0));
        }

        /// <summary>Round 4 (pass 4: TRIPPED never appeared in the engine): a person's slide into a REAL Normal bot in a duel, with
        /// every class move on, emits the trip (Slide, B = the victim) the view draws as TRIPPED: travel in from 15 u, Dodge pressed
        /// (lag 6) once the slide reaches the body. Target: at least 0.6 of the slides trip.</summary>
        [Test]
        public void Slide_TripsARealBotInADuel()
        {
            int trips = 0, slides = 0, tries = 0;
            var rows = new List<string>();
            foreach (ClassId c in T.Classes)
            {
                int ct = 0, cs = 0;
                for (int k = 0; k < 4; k++)
                {
                    Seat s = PlayerSeatRig.Duel(c, ClassId.Farmer, TargetMode.RealBot, 0, k, distance: 15.0, tweak: MvCombine.AllOn);
                    World w = s.W;
                    int ib = s.Dummy != null ? s.Dummy.Me : PlayerSeatRig.EnemySlot(w);
                    tries++;
                    double reach = w.Config.Depth.Moves.SlideDistance + Collide.ActorRadius(w, 0) + Collide.ActorRadius(w, ib) - 0.2;
                    bool pressed = false, tripped = false, slid = false;
                    var hist = new double[64];
                    for (int t = 0; t < 240 && !tripped; t++)
                    {
                        double d = Dist(w, 0, ib);
                        hist[w.Tick & 63] = d;
                        double seen = t >= Lag ? hist[(w.Tick - Lag) & 63] : 99;
                        s.Human.MoveOverrideX = (w.X[ib] - w.X[0]) / d; s.Human.MoveOverrideZ = (w.Z[ib] - w.Z[0]) / d;
                        if (!pressed && seen <= reach + 0.6 && MovesHooks.MovementSlideArmed(w, 0)) { s.Human.PressDodge = true; pressed = true; }
                        s.Step();
                        if (s.Spy.Of(SimEventType.Slide, e => e.A == 0 && e.B < 0).Count > 0) slid = true;
                        if (s.Spy.Of(SimEventType.Slide, e => e.A == 0 && e.B == ib).Count > 0) tripped = true;
                    }
                    if (slid) { slides++; cs++; }
                    if (tripped) { trips++; ct++; }
                }
                rows.Add(c + " " + ct + "/" + cs);
            }
            double rate = slides > 0 ? (double)trips / slides : 0;
            Assert.IsTrue(PxLog.Line("MV3 T2-5 Slide trip on a real bot", "duel vs a Normal Farmer bot, every switch on, travel in from 15 u, Dodge (lag 6) at slide reach",
                trips + " trips of " + slides + " slides, " + tries + " tries (" + string.Join(", ", rows) + ")", ">= 0.6 of slides trip (the Slide event with the victim)",
                slides > 0 && rate >= 0.6));
        }

        [Test]
        public void Slide_TripNumbersAndTheSharedCooldown()
        {
            Seat s = PlayerSeatRig.Field(ClassId.Warrior, x: 5.5, z: 15.5, tweak: Sw(true, true));
            World w = s.W;
            Paint(w, Flat);
            int ib = PlayerSeatRig.EnemySlot(w);
            PlayerSeatRig.Place(w, ib, ClassId.Farmer, 30, 15.5);
            s.Dummy = new DummyModel { Me = ib, Around = 0 };
            int pressTick = -1, tripTick = -1;
            for (int t = 0; t < 200 && tripTick < 0; t++)
            {
                double d = Dist(w, 0, ib);
                s.Human.MoveOverrideX = 1; s.Human.MoveOverrideZ = 0;
                if (pressTick < 0 && d < 5.2 && MovesHooks.MovementSlideArmed(w, 0)) { s.Human.PressDodge = true; pressTick = w.Tick + 1; }
                s.Step();
                if (s.Spy.Of(SimEventType.Slide, e => e.B == ib).Count > 0) tripTick = w.Tick;
            }
            SeatEvent trip = s.Spy.Of(SimEventType.Slide, e => e.B == ib).FirstOrDefault();
            SeatEvent hit = s.Spy.Of(SimEventType.Hit, e => e.A == ib && e.B == 0).FirstOrDefault();
            int stun = w.Actors[ib].HitStunTicks;
            int cd = Dodge.CooldownTicks(w, 0);
            bool locked = MovesHooks.AttackLocked(w, 0);
            // the slider's 6-tick recovery: no move, no swing
            double x0 = w.X[0];
            s.Human.MoveOverrideX = 1; s.Step();
            bool still = Math.Abs(w.X[0] - x0) < 1e-6;
            bool ok = trip != null && hit != null && Math.Abs(hit.Value - 8) < 1e-9 && stun >= 8 && cd > 60 && locked && still;
            Assert.IsTrue(PxLog.Line("MV3 T2-5 Slide trip numbers", "Warrior slides into a standing Farmer",
                "trip " + (trip != null) + " " + (tripTick - pressTick) + " ticks after the press, damage " + (hit != null ? PxLog.F(hit.Value) : "none") + ", victim stagger left " + stun
                + ", dodge cooldown left " + cd + ", slider locked " + locked + ", slider still " + still,
                "8 damage, ~10-tick stagger, the 90-tick dodge cooldown spent, 6-tick slider recovery", ok));
        }

        [Test]
        public void Slide_OnceAnEnemyNearWindsUpDodgeIsTheDodgeAgain()
        {
            Seat s = PlayerSeatRig.Field(ClassId.Miner, x: 5.5, z: 15.5, tweak: Sw(true, true));
            World w = s.W;
            Paint(w, Flat);
            int ib = PlayerSeatRig.EnemySlot(w);
            PlayerSeatRig.Place(w, ib, ClassId.Farmer, 24, 15.5);
            s.Dummy = new DummyModel { Me = ib, Around = 0 };
            bool armedNear = false, swung = false;
            for (int t = 0; t < 150; t++)
            {
                double d = Dist(w, 0, ib);
                s.Human.MoveOverrideX = 1;
                if (d < 7.5 && !swung) { armedNear = MovesHooks.MovementSlideArmed(w, 0); s.Dummy.PressAttack = true; swung = true; }
                else if (swung && w.Actors[ib].SwingPending) { s.Human.PressDodge = true; s.Step(); break; }
                s.Step();
            }
            int dodges = s.Spy.Of(SimEventType.Dodge, e => e.A == 0).Count, slides = s.Spy.Of(SimEventType.Slide, e => e.A == 0).Count;
            Assert.IsTrue(PxLog.Line("MV3 T2-5 Slide carry ends on a wind-up", "Miner travels in; at 7.5 u the Farmer starts a swing; Dodge pressed during the wind-up",
                "slide armed before the swing " + armedNear + "; then dodges " + dodges + ", slides " + slides, "armed, then the press is a dodge", armedNear && dodges == 1 && slides == 0));
        }

        [Test]
        public void Slide_EngageFromTravelBeatsWalkingIn()
        {
            // "the way into a fight from travel": a lag-6 human Warrior walks in from 20 u at a Normal bot of each class (travel lights
            // on the way in); first landed hit (trip or swing) within 5 s, slide entry vs walking in. (SPEC T2-5 asks for the chase with slide
            // entry at plain + 0.10; the S11 chase starts inside 8 u, where nobody travels, so it cannot move there: see report.)
            int[] first = new int[2];
            double[] when = new double[2];
            int n = 0;
            for (int v = 0; v < 2; v++)
                foreach (ClassId them in T.Classes)
                {
                    if (them == ClassId.Warrior) continue;
                    for (int k = 0; k < 4; k++)
                    {
                        Seat s = PlayerSeatRig.Duel(ClassId.Warrior, them, TargetMode.RealBot, PlayerSeatRig.AngleOf(k, 4), k, Lag, distance: 20, tweak: Sw(true, v == 1));
                        s.Human.AimHelp = 1;
                        World w = s.W;
                        var hist = new double[64];
                        bool pressed = false;
                        s.BeforeTick = q =>
                        {
                            double d = Dist(w, q.Me, q.Them);
                            hist[w.Tick & 63] = d;
                            double seen = hist[(w.Tick - Lag) & 63];
                            if (v == 1 && !pressed && seen > 0 && seen < 5.6 && MovesHooks.MovementSlideArmed(w, q.Me))
                            {
                                q.Human.PressDodge = true;
                                q.Human.MoveOverrideX = (w.X[q.Them] - w.X[q.Me]) / d; q.Human.MoveOverrideZ = (w.Z[q.Them] - w.Z[q.Me]) / d;
                                pressed = true;
                            }
                        };
                        if (v == 0) n++;
                        int start = w.Tick;
                        PlayerSeatRig.Play(s, 5, q => q.Spy.Hits(q.Me, q.Them).Count > 0);
                        var h = s.Spy.Hits(s.Me, s.Them);
                        if (h.Count > 0) { first[v]++; when[v] += (h[0].Tick - start) / w.Config.SimHz; }
                    }
                }
            double walkShare = first[0] / (double)n, slideShare = first[1] / (double)n;
            Assert.IsTrue(PxLog.Line("MV3 T2-5 Slide entry from travel", "lag-6 human Warrior walks in from 20 u on a Normal bot (5 classes x 4), first hit within 5 s",
                "walk in " + first[0] + "/" + n + " (" + PxLog.F(walkShare) + ", mean " + PxLog.F(first[0] > 0 ? when[0] / first[0] : 0) + " s) vs slide entry " + first[1] + "/" + n + " (" + PxLog.F(slideShare) + ", mean " + PxLog.F(first[1] > 0 ? when[1] / first[1] : 0) + " s)",
                "slide entry lands as often and its first hit at least 0.5 s sooner", slideShare >= walkShare - 1e-9 && first[1] > 0 && first[0] > 0 && when[0] / first[0] - when[1] / first[1] >= 0.5));
        }

        // ---- S58 with the movement switches on: stairs (height bands), map bridges, built bridge and ladder

        static bool Walk(Seat s, double x0, double z0, double x1, double z1, bool slide, out string what)
        {
            World w = s.W; int me = s.Me;
            double dx = x1 - x0, dz = z1 - z0, len = Math.Sqrt(dx * dx + dz * dz), ux = dx / len, uz = dz / len, sx = x0, sz = z0;
            for (int k = 0; k < 20 && !Collide.FootprintClear(w, me, sx, sz); k++) { sx += ux * 0.25; sz += uz * 0.25; }
            PlayerSeatRig.Place(w, me, w.Actors[me].Cls, sx, sz);
            double walk = w.Config.Class(w.Actors[me].Cls).MoveSpeed;
            int max = (int)Math.Ceiling((len / walk * 2.5 + 2) * w.Config.SimHz), slow = 0, worst = 0, rev = 0, falls0 = s.Spy.Of(SimEventType.Fall).Count;
            double px = w.X[me], pz = w.Z[me], pdx = 0, pdz = 0;
            bool reached = false;
            for (int t = 0; t < max && w.Actors[me].Alive; t++)
            {
                s.Human.MoveOverrideX = ux; s.Human.MoveOverrideZ = uz;
                s.Human.AimOverrideX = w.X[me] + ux * 3; s.Human.AimOverrideZ = w.Z[me] + uz * 3;
                if (slide && MovesHooks.MovementSlideArmed(w, me) && Dodge.CanStart(w, me)) s.Human.PressDodge = true;
                s.Step();
                double mx = w.X[me] - px, mz = w.Z[me] - pz, sp = Math.Sqrt(mx * mx + mz * mz) * w.Config.SimHz;
                if ((x1 - w.X[me]) * ux + (z1 - w.Z[me]) * uz < 0.8) { reached = true; break; }
                slow = sp < 0.2 * walk ? slow + 1 : 0;
                if (slow > worst) worst = slow;
                if (pdx * mx + pdz * mz < -1e-6 && pdx * pdx + pdz * pdz > 1e-8 && mx * mx + mz * mz > 1e-8) rev++;
                pdx = mx; pdz = mz; px = w.X[me]; pz = w.Z[me];
            }
            int falls = s.Spy.Of(SimEventType.Fall).Count - falls0;
            what = (reached ? "reached" : "NOT reached at " + PxLog.F(w.X[me], "0.0") + "," + PxLog.F(w.Z[me], "0.0")) + ", slow run " + worst + ", reversals " + rev + ", falls " + falls;
            return reached && worst <= T.StuckTicksMax && rev == 0 && falls == 0 && w.Actors[me].Alive;
        }

        static Seat Strip(ClassId c, Action<GameConfig> tweak)
        {
            Seat s = PlayerSeatRig.Field(c, x: 7.5, z: 13.5, tweak: tweak);
            World w = s.W;
            MapDef map = w.Map;
            Array.Fill(map.Cells, Cell.Void);
            Array.Clear(map.WallHp, 0, map.WallHp.Length);
            void P(int x0, int z0, int x1, int z1, byte t) { for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) map.Cells[z * map.Width + x] = t; }
            P(2, 10, 8, 16, Cell.Floor);
            P(13, 8, 20, 18, Cell.Floor);
            P(21, 8, 21, 18, Cell.Wall);
            P(22, 8, 40, 18, Cell.Floor);
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
        public void S58_WithTravelAndSlideOn_MovementNeverSticks(ClassId c)
        {
            // S58's walks (PlayerSeatDefenceTests) with travel + slide on, each walked plain and again pressing Dodge whenever the
            // slide is armed (every 3 s): map bridges x 3 lines x 2 ways, the built bridge and ladder, height bands +-1 / +-2.
            var bad = new List<string>();
            int n = 0, slides = 0;
            Action<GameConfig> on = Sw(true, true, true);
            var bridges = CriticalMass.Sim.Maps.SkybreakBasinMap.Bridges;
            for (int sl = 0; sl < 2; sl++)
            {
                bool slide = sl == 1;
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
                                Seat s = PlayerSeatRig.Field(c, tweak: on);
                                // approach from 4 u back along the line, so travel is lit (or nearly) on the bridge
                                if (!Walk(s, ax - lx / ll * 4, az - lz / ll * 4, bx, bz, slide, out string what))
                                    bad.Add((slide ? "slide " : "") + "bridge B" + (b + 1) + (m == 1 ? "m" : "") + (back == 1 ? " back" : "") + " off " + off + ": " + what);
                                slides += s.Spy.Of(SimEventType.Slide, e => e.A == 0 && e.B < 0).Count;
                            }
                foreach (string piece in new[] { "bridge", "ladder" })
                    for (int back = 0; back < 2; back++)
                    {
                        Seat s = Strip(ClassId.Builder, on);
                        World w = s.W;
                        PlayerSeatRig.Place(w, 0, ClassId.Builder, piece == "bridge" ? 7.5 : 19.5, 13.5);
                        s.Human.AimOverrideX = piece == "bridge" ? 20 : 30; s.Human.AimOverrideZ = 13.5;
                        s.Human.PressAbility = true;
                        s.Step();
                        bool placed = s.Spy.Of(SimEventType.PiecePlaced, e => e.A == 0).Count > 0;
                        s.Steps(40);
                        PlayerSeatRig.Place(w, 0, c, 7.5, 13.5);
                        double ax = piece == "bridge" ? 3.5 : 14.5, bx = piece == "bridge" ? 18.5 : 27.5;
                        if (back == 1) { ax = piece == "bridge" ? 18.5 : 32.5; bx = piece == "bridge" ? 3.5 : 15.5; }
                        n++;
                        string what = "not placed";
                        if (!placed || !Walk(s, ax, 13.5, bx, 13.5, slide, out what)) bad.Add((slide ? "slide " : "") + "built " + piece + (back == 1 ? " back" : "") + ": " + what);
                        slides += s.Spy.Of(SimEventType.Slide, e => e.A == 0 && e.B < 0).Count;
                    }
                foreach (int lift in new[] { 1, 2, -1, -2 })
                    for (int back = 0; back < 2; back++)
                        for (int diag = 0; diag < 2; diag++)
                        {
                            Seat s = Strip(c, on);
                            MapDef map = s.W.Map;
                            for (int zz = 8; zz <= 18; zz++) for (int xx = 30; xx <= 32; xx++) map.Lift[zz * map.Width + xx] = (sbyte)lift;
                            s.W.GridVersion++;
                            double ax = 22.5, bx = 39.5, az = diag == 1 ? 10.5 : 13.5, bz = diag == 1 ? 16.5 : 13.5;
                            if (back == 1) { double t = ax; ax = bx; bx = t; t = az; az = bz; bz = t; }
                            n++;
                            if (!Walk(s, ax, az, bx, bz, slide, out string what)) bad.Add((slide ? "slide " : "") + "height " + lift + (back == 1 ? " back" : "") + (diag == 1 ? " diagonal" : "") + ": " + what);
                            slides += s.Spy.Of(SimEventType.Slide, e => e.A == 0 && e.B < 0).Count;
                        }
            }
            bool ok = PxLog.Line("MV3 S58 with travel + slide", c + ": " + n + " walks (half pressing Dodge whenever the slide is armed; " + slides + " slides)",
                bad.Count == 0 ? "all clean" : string.Join("; ", bad), "0 stuck (under 20% speed > 0.5 s), 0 reversals, 0 falls", bad.Count == 0);
            Assert.IsTrue(ok, string.Join("\n", bad));
        }

        // ================================================================== T2-6 glide

        /// <summary>A strip with a void gap of gap cells across x = 20 .. 19 + gap (floor z 6..20 both sides).</summary>
        static Seat GapStrip(ClassId c, int gap, Action<GameConfig> tweak, double x, double z)
        {
            Seat s = PlayerSeatRig.Field(c, x: x, z: z, tweak: tweak);
            Paint(s.W, new[] { 2, 6, 19, 20 }, new[] { 20 + gap, 6, 50, 20 });
            return s;
        }

        [Test]
        public void Glide_EveryGlideLandsOnWalkableGround()
        {
            // 200 tries: 2- and 3-cell gaps, run-ups from 6 start offsets, 4 jump-press distances before the edge (ones the
            // plain jump shortens or refuses included), and 4 holds (hold through, let go at the apex, let go mid-glide, steer
            // sideways then back). A fall = a Fall event, a death, or ending on a cell that is not walkable.
            int tries = 0, falls = 0, crossed3 = 0, tries3 = 0, glides = 0, crossedOff3 = 0;
            var fails = new List<string>();
            foreach (int gap in new[] { 2, 3 })
                for (int hold = 0; hold < 4; hold++)
                    for (int press = 0; press < 5; press++)
                        for (int run = 0; run < 5; run++)
                        {
                            if (tries >= 200) break;
                            double sx = 12.0 - run * 0.7, sz = 9.5 + run * 2.1;
                            Seat s = GapStrip(ClassId.Healer, gap, Sw(false, false, true), sx, sz);
                            World w = s.W;
                            double edge = 20.0 - Collide.ActorRadius(w, 0) - 0.05 - press * 0.35;
                            bool jumped = false;
                            int air0 = -1;
                            for (int t = 0; t < 150; t++)
                            {
                                Actor a = w.Actors[0];
                                double mz = 0;
                                if (hold == 3 && a.AirLen > 0 && air0 >= 0) mz = w.Tick - air0 < 18 ? 0.8 : -0.8;
                                s.Human.MoveOverrideX = 1; s.Human.MoveOverrideZ = mz;
                                if (!jumped && w.X[0] >= edge) { s.Human.PressJump = true; jumped = true; air0 = w.Tick; }
                                bool held = jumped && (hold == 0 || hold == 3 || (hold == 1 && w.Tick - air0 < 9) || (hold == 2 && w.Tick - air0 < 18));
                                s.Human.Frame.JumpHeld = held;
                                s.Step();
                                s.Human.Frame.JumpHeld = false;
                                if (jumped && w.Actors[0].AirLen == 0 && w.Tick - air0 > 2 && w.X[0] > 22 + gap) break;
                            }
                            tries++;
                            bool fell = s.Spy.Of(SimEventType.Fall).Count > 0 || !w.Actors[0].Alive
                                || !MapGrid.IsWalkable(w.Map, JsMath.FloorToInt(w.X[0]), JsMath.FloorToInt(w.Z[0]));
                            if (fell) { falls++; if (fails.Count < 6) fails.Add("gap " + gap + " hold " + hold + " press " + press + " run " + run + " at " + PxLog.F(w.X[0], "0.0")); }
                            glides += s.Spy.Of(SimEventType.Glide, e => e.A == 0 && e.Value == 1).Count;
                            if (gap == 3) { tries3++; if (w.X[0] > 20 + gap) crossed3++; }
                        }
            // the control: the plain jump (glide off) never crosses the 3-cell gap
            for (int run = 0; run < 10; run++)
            {
                Seat s = GapStrip(ClassId.Healer, 3, null, 12.0 - run * 0.3, 9.5 + run);
                World w = s.W;
                bool jumped = false;
                for (int t = 0; t < 120; t++)
                {
                    s.Human.MoveOverrideX = 1;
                    if (!jumped && w.X[0] >= 19.4) { s.Human.PressJump = true; jumped = true; }
                    s.Human.Frame.JumpHeld = jumped;
                    s.Step();
                }
                if (w.X[0] > 23) crossedOff3++;
            }
            bool ok = falls == 0 && tries == 200 && crossed3 > 0 && crossedOff3 == 0;
            Assert.IsTrue(PxLog.Line("MV3 T2-6 Glide lands on walkable ground", "Healer, 2- and 3-cell void gaps, 200 tries (run-ups x press points x holds)",
                "falls " + falls + "/" + tries + (fails.Count > 0 ? " [" + string.Join("; ", fails) + "]" : "") + ", glides " + glides + ", crossed the 3-cell gap " + crossed3 + "/" + tries3 + "; glide off crossed it " + crossedOff3 + "/10",
                "0 falls in 200; the glide crosses 3 cells, the plain jump does not", ok));
        }

        [Test]
        public void Glide_HealerFleeingAWarriorAcrossAGapSurvives()
        {
            int[] lived = new int[2];
            for (int on = 0; on < 2; on++)
                for (int k = 0; k < 16; k++)
                {
                    Seat s = PlayerSeatRig.Duel(ClassId.Healer, ClassId.Warrior, TargetMode.HumanVsHuman, 0, k, Lag, distance: 3, tweak: on == 1 ? Sw(true, true, true) : null);
                    World w = s.W;
                    Paint(w, new[] { 2, 6, 19, 20 }, new[] { 23, 6, 50, 20 });
                    double hz = 8.5 + (k % 8) * 1.4;
                    PlayerSeatRig.Place(w, s.Me, ClassId.Healer, 15.0 - (k / 8) * 1.0, hz);
                    PlayerSeatRig.Place(w, s.Them, ClassId.Warrior, 15.0 - (k / 8) * 1.0 - 2.6, hz + (k % 3 - 1) * 0.4);
                    w.Actors[s.Me].Hp = 0.3 * w.Actors[s.Me].MaxHp;
                    s.Human.Fight = false;
                    s.Human2.LagTicks = 5 + k % 3;
                    s.Human2.HoldFireTicks = k % 4;
                    s.Human2.AimHelp = 1;
                    bool jumped = false;
                    s.BeforeTick = q =>
                    {
                        Actor a = w.Actors[q.Me];
                        q.Human.MoveOverrideX = 1; q.Human.MoveOverrideZ = 0;
                        // the runner jumps at the edge and holds Jump through the flight (glide off: the plain jump is refused or short)
                        if (a.AirLen == 0 && w.X[q.Me] >= 19.0 && w.X[q.Me] < 20) { q.Human.PressJump = true; jumped = true; }
                        q.Human.Frame.JumpHeld = jumped && (a.AirLen > 0 || w.X[q.Me] < 20);
                    };
                    DuelResult r = PlayerSeatRig.Play(s, 6);
                    if (!r.Lost) lived[on]++;
                }
            Assert.IsTrue(PxLog.Line("MV3 T2-6 Healer escapes across a gap", "Healer at 30% runs from a lag-6 human Warrior (2.6 u behind) to a 3-cell void gap, 16 starts, 6 s",
                "glide off " + lived[0] + "/16, glide on " + lived[1] + "/16", "on at least 12 of 16", lived[1] >= 12));
        }

        [Test]
        public void Glide_NoAttackOrCastAHitEndsItOverGroundOnlyAndItIsHealerOnly()
        {
            // (a) only the Healer glides; (b) no swing or cast while gliding; (c) a hit over the void ends the glide only where
            // the drop lands on walkable ground; (d) the 4 s cooldown
            Seat s = GapStrip(ClassId.Healer, 3, Sw(false, false, true), 16, 13.5);
            World w = s.W;
            int ib = PlayerSeatRig.EnemySlot(w);
            PlayerSeatRig.Place(w, ib, ClassId.Farmer, 40, 13.5);
            s.Dummy = new DummyModel { Me = ib, Around = 0 };
            bool jumped = false, lockedAtk = false, lockedCast = false, hitDone = false;
            int air0 = -1, glideEnds = 0, hitEnded = 0;
            bool endedOverVoid = false;
            for (int t = 0; t < 150; t++)
            {
                s.Human.MoveOverrideX = 1;
                if (!jumped && w.X[0] >= 19.3) { s.Human.PressJump = true; jumped = true; air0 = w.Tick; }
                s.Human.Frame.JumpHeld = jumped;
                if (MovesHooks.MovementGliding(w, 0))
                {
                    lockedAtk |= MovesHooks.AttackLocked(w, 0);
                    lockedCast |= MovesHooks.CastLocked(w, 0);
                    if (!hitDone && w.X[0] > 20.5 && w.X[0] < 21.5) { Damage.ApplyDamage(w, 0, 3, ib, w.X[ib], w.Z[ib]); hitDone = true; }
                }
                s.Step();
                foreach (SeatEvent e in s.Spy.Of(SimEventType.Glide, e => e.A == 0 && e.Value == 0 && e.Tick == w.Tick))
                {
                    glideEnds++;
                    if (e.Radius == 1) hitEnded++;
                    endedOverVoid |= !MapGrid.IsWalkable(w.Map, JsMath.FloorToInt(w.Actors[0].AirToX), JsMath.FloorToInt(w.Actors[0].AirToZ));
                }
                if (jumped && w.Actors[0].AirLen == 0 && w.Tick - air0 > 2) break;
            }
            bool landedOk = MapGrid.IsWalkable(w.Map, JsMath.FloorToInt(w.X[0]), JsMath.FloorToInt(w.Z[0])) && s.Spy.Of(SimEventType.Fall).Count == 0;
            // the cooldown: a second jump at once does not glide
            int ready = M(w).GlideReady[0] - w.Tick;
            // the Warrior never glides
            Seat wr = GapStrip(ClassId.Warrior, 1, Sw(false, false, true), 16, 13.5);
            bool wj = false;
            for (int t = 0; t < 60; t++) { wr.Human.MoveOverrideX = 1; if (!wj && wr.W.X[0] >= 19.3) { wr.Human.PressJump = true; wj = true; } wr.Human.Frame.JumpHeld = wj; wr.Step(); }
            bool warriorGlided = wr.Spy.Of(SimEventType.Glide).Count > 0;
            bool ok = lockedAtk && lockedCast && hitDone && hitEnded == 1 && !endedOverVoid && landedOk && ready > 90 && !warriorGlided;
            Assert.IsTrue(PxLog.Line("MV3 T2-6 Glide rules", "Healer glides a 3-cell gap, hit over the void at x 21",
                "attack locked " + lockedAtk + ", cast locked " + lockedCast + ", ended by the hit " + hitEnded + ", ended over the void " + endedOverVoid + ", landed on ground " + landedOk
                + ", cooldown left " + ready + " ticks, Warrior glided " + warriorGlided,
                "locked, the hit ends it only where the drop lands on ground, cooldown ~120 ticks, Healer only", ok));
        }

        // ================================================================== T2-7 the landing lock (lane 2 owns the hit test)

        [Test]
        public void JumpEvade_LandingRecoveryAndRelockAfterAnEvadingJumpOnly()
        {
            var rows = new List<string>();
            bool ok = true;
            for (int evade = 0; evade < 2; evade++)
            {
                Seat s = PlayerSeatRig.Field(ClassId.Miner, x: 10.5, z: 15.5, tweak: Sw(false, false, false, true));
                World w = s.W;
                Paint(w, Flat);
                s.Human.PressJump = true;
                s.Step();
                var window = new List<int>();
                int landTick = -1;
                for (int t = 0; t < 30 && landTick < 0; t++)
                {
                    if (MovesHooks.MovementInAirEvadeWindow(w, 0)) window.Add(w.Actors[0].AirTick);
                    if (evade == 1 && w.Actors[0].AirTick == 8)
                        World.PushEvent(w, SimEventType.HitBlocked, 0, -1, w.X[0], w.Z[0], PlayerReasons.BlockedAir);   // lane 2's hit test, simulated
                    s.Step();
                    if (w.Actors[0].AirLen == 0) landTick = w.Tick;
                }
                bool landing = MovesHooks.MovementLanding(w, 0), atkLocked = MovesHooks.AttackLocked(w, 0);
                int recover = 0;
                while (w.Tick + 1 < M(w).LandRecoverUntil[0] && recover < 20) { s.Human.PressDodge = true; s.Step(); recover++; }
                bool dodged = s.Spy.Of(SimEventType.Dodge, e => e.A == 0).Count > 0;
                // jump presses: one at +10 ticks after landing (locked after an evade), one at +31 (free)
                while (w.Tick < landTick + 10) s.Step();
                s.Human.PressJump = true; s.Step();
                bool jump10 = w.Actors[0].AirLen > 0;
                while (w.Actors[0].AirLen > 0) s.Step();
                while (w.Tick < landTick + 32) s.Step();
                s.Human.PressJump = true; s.Step();
                bool jump32 = w.Actors[0].AirLen > 0;
                string win = window.Count > 0 ? window.Min() + ".." + window.Max() : "none";
                if (evade == 1) ok &= landing && atkLocked && recover == 4 && !dodged && !jump10 && jump32 && win == "5..12";
                else ok &= !landing && !atkLocked && recover == 0 && jump10 && jump32 && win == "5..12";
                rows.Add((evade == 1 ? "evaded" : "plain") + ": window air ticks " + win + ", landing recovery " + recover + " ticks, attack locked " + atkLocked + ", dodge in it " + dodged + ", jump at +10 " + jump10 + ", at +32 " + jump32);
            }
            Assert.IsTrue(PxLog.Line("MV3 T2-7 Evade landing lock", "Miner jumps; a BlockedAir at air tick 8 (lane 2's hit test simulated) vs none", string.Join("; ", rows),
                "window 5..12; after an evade 4 ticks of no dodge / attack, the next jump locked 1 s; none after a plain jump", ok));
        }

        // ================================================================== S49 before / after (Slow)

        [Test, Category("Slow")]
        public void S49_WithTravelOn_DeadOrWalkingBackAndRespawnToFirstHit()
        {
            // S49's measure (PlayerSeatEngagementSoak), seat 0 on the bot brain, CM_PX_SOAK_SEEDS seeds (default 4), 5v5 and
            // 16v16, Warrior / Farmer; every switch off vs travel + slide on. Target: dead-or-walking <= 0.28, respawn to first hit <= 10 s.
            string env = Environment.GetEnvironmentVariable("CM_PX_SOAK_SEEDS");
            int seeds = int.TryParse(env, out int ns) && ns > 0 ? ns : 4;
            var rows = new List<string>();
            double share = 0, hit = 0;
            foreach (int size in new[] { 5, 16 })
                for (int on = 0; on < 2; on++)
                {
                    var dead = new List<double>();
                    var toHit = new List<double>();
                    foreach (ClassId cls in new[] { ClassId.Warrior, ClassId.Farmer })
                        for (int sd = 0; sd < seeds; sd++)
                        {
                            Soak(size, cls, sd, on == 1, out double d, out double h);
                            dead.Add(d);
                            if (!double.IsNaN(h)) toHit.Add(h);
                        }
                    toHit.Sort();
                    double ds = dead.Average(), mh = toHit.Count > 0 ? toHit[toHit.Count / 2] : double.NaN;
                    rows.Add(size + "v" + size + (on == 1 ? " on" : " off") + ": dead or walking back " + PxLog.F(ds) + ", respawn to first hit median " + PxLog.F(mh, "0.0") + " s");
                    if (on == 1 && size == 16) { share = ds; hit = mh; }
                }
            Assert.IsTrue(PxLog.Line("MV3 T2-4 S49 with travel", seeds + " seeds x Warrior/Farmer, seat 0 on the bot brain", string.Join("; ", rows),
                "16v16 on: dead or walking <= 0.28, respawn to hit median <= 10.5 s (integrator, decision 5)", share <= 0.28 && !(hit > 10.5)));
        }

        static void Soak(int teamSize, ClassId cls, int seed, bool on, out double deadShare, out double toHitMedian)
        {
            GameConfig cfg = teamSize >= 16 ? GameConfig.CreateDefault().ApplyRules(RulesProfile.Full) : GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full);
            cfg.TeamSize = teamSize;
            if (on)
            {
                // integrator: CM_MVI_ALLON=1 measures the play build (every switch, plus CM_MVI_KNOBS), else lane 3's travel + slide
                if (Environment.GetEnvironmentVariable("CM_MVI_ALLON") == "1") MvCombine.AllOn(cfg); else Sw(true, true)(cfg);
                // lever probes for the combine (unset = the spec's knobs): CM_MV3_TRAVEL_MUL, CM_MV3_TRAVEL_AFTER (ticks)
                if (double.TryParse(Environment.GetEnvironmentVariable("CM_MV3_TRAVEL_MUL"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double mul)) cfg.Depth.Moves.TravelSpeedMul = mul;
                if (int.TryParse(Environment.GetEnvironmentVariable("CM_MV3_TRAVEL_AFTER"), out int after)) cfg.Depth.Moves.TravelAfterMoveTicks = after;
            }
            World w = World.CreateWorld(new WorldOptions { Seed = 7000 + seed, TeamSize = teamSize, HumanTeam = TeamId.Azure, HumanClass = cls, Config = cfg, BotDifficulty = BotDifficulty.Normal, AllyBotDifficulty = BotDifficulty.Normal });
            var scratch = new InputFrame();
            double hz = w.Config.SimHz;
            int cap = (int)Math.Ceiling(w.Config.MatchSeconds * hz * 1.5);
            int head = w.EventHead, deadOrWalk = 0, ticks = 0, respawnAt = -1;
            bool wasAlive = w.Actors[0].Alive, walking = false;
            var toHit = new List<double>();
            for (int t = 0; t < cap && w.Match.Winner == MatchResult.None; t++)
            {
                MatchTick.Advance(w, null, scratch);
                ticks++;
                bool hit0 = false;
                for (int i = Math.Max(head, w.EventHead - World.EventCapacity); i < w.EventHead; i++)
                {
                    SimEvent e = w.Events[i % World.EventCapacity];
                    if (e.Type == SimEventType.Hit && (e.A == 0 || e.B == 0)) hit0 = true;
                }
                head = w.EventHead;
                Actor a = w.Actors[0];
                bool alive = a.Alive;
                if (alive && !wasAlive) { respawnAt = w.Tick; walking = true; }
                wasAlive = alive;
                if (respawnAt >= 0 && hit0 && alive) { toHit.Add((w.Tick - respawnAt) / hz); respawnAt = -1; }
                bool meaningful = false;
                if (alive)
                {
                    double x = w.X[0], z = w.Z[0], r2 = T.EngageEnemyRadius * T.EngageEnemyRadius;
                    for (int j = 0; j < w.ActorCount && !meaningful; j++)
                    {
                        Actor o = w.Actors[j];
                        if (!o.Alive || o.Team == a.Team) continue;
                        double dx = w.X[j] - x, dz = w.Z[j] - z;
                        meaningful = dx * dx + dz * dz <= r2;
                    }
                    double hx = x - w.Map.Hill.X, hzz = z - w.Map.Hill.Z;
                    meaningful |= hx * hx + hzz * hzz <= w.Map.Hill.R * w.Map.Hill.R;
                    meaningful |= a.Carry != CarryKind.None || a.DeliverTicks > 0;
                }
                if (meaningful) walking = false;
                if (!alive || walking) deadOrWalk++;
            }
            toHit.Sort();
            deadShare = ticks > 0 ? (double)deadOrWalk / ticks : 0;
            toHitMedian = toHit.Count > 0 ? toHit[toHit.Count / 2] : double.NaN;
        }

        static int T30(World w, int ticks) => Durations.T30(w.Config, ticks);
    }
}
