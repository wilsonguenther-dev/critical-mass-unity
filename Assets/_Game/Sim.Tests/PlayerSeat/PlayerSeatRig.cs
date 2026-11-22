using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Tests.Harness;

namespace CriticalMass.Sim.Tests.PlayerSeat
{
    /// <summary>Who stands across from the human seat in a player-seat duel.</summary>
    public enum TargetMode
    {
        /// <summary>A scripted dummy that stands still (its frame is empty; the rig keeps the idle rule off it).</summary>
        Passive,
        /// <summary>A scripted dummy circling the human at StraferSpeedShare x its speed, StraferRadiusShare x the human's reach.</summary>
        Strafer,
        /// <summary>A real bot (BotBrain) at the duel's difficulty: it fights back, dodges and retreats.</summary>
        RealBot,
        /// <summary>A passive dummy target plus one enemy Healer bot (BotBrain) standing 2.5 u behind it: isolates the healing.</summary>
        Healed,
        /// <summary>Both seats are human models (an online room: the enemy seat is an ExtraHumans actor).</summary>
        HumanVsHuman,
    }

    /// <summary>A copy of one sim event, stamped with the tick it was read after.</summary>
    public sealed class SeatEvent
    {
        public SimEventType Type;
        public int A, B, Tier, Tick;
        public double X, Z, Value, Impulse, Radius, DirX, DirZ;
        public DamageCause Cause;
        public override string ToString() => Type + "(A " + A + ", B " + B + ", V " + Value.ToString("0.##", CultureInfo.InvariantCulture) + ", t " + Tick + ")";
    }

    /// <summary>
    /// Reads w.Events from a cursor after every tick (as GameHud does) and keeps a copy of every event, so a test can ask
    /// what the sim said about a press, a hit or a kill. Throws if the 512-slot ring lapped the cursor (never silently loses one).
    /// </summary>
    public sealed class EventSpy
    {
        private readonly World _w;
        private int _cursor;
        public readonly List<SeatEvent> All = new List<SeatEvent>();

        public EventSpy(World w)
        {
            _w = w;
            _cursor = w.EventHead;
        }

        /// <summary>Copies every event pushed since the last pump.</summary>
        public void Pump()
        {
            if (_w.EventHead - _cursor > World.EventCapacity) throw new InvalidOperationException("player-seat EventSpy: the event ring lapped the cursor (more than 512 events in one tick)");
            for (; _cursor < _w.EventHead; _cursor++)
            {
                SimEvent e = _w.Events[_cursor % World.EventCapacity];
                All.Add(new SeatEvent { Type = e.Type, A = e.A, B = e.B, X = e.X, Z = e.Z, Value = e.Value, Impulse = e.Impulse, Radius = e.Radius, DirX = e.DirX, DirZ = e.DirZ, Tier = e.Tier, Cause = e.Cause, Tick = _w.Tick });
            }
        }

        /// <summary>A mark: the number of events read so far (pass to Since).</summary>
        public int Mark => All.Count;

        public List<SeatEvent> Since(int mark, SimEventType type, Func<SeatEvent, bool> where = null)
        {
            var r = new List<SeatEvent>();
            for (int i = Math.Max(0, mark); i < All.Count; i++)
                if (All[i].Type == type && (where == null || where(All[i]))) r.Add(All[i]);
            return r;
        }

        public List<SeatEvent> Of(SimEventType type, Func<SeatEvent, bool> where = null) => Since(0, type, where);

        /// <summary>Hits landed BY attacker (Hit: A = victim, B = credit), optionally on one victim.</summary>
        public List<SeatEvent> Hits(int by, int on = -1, int mark = 0) => Since(mark, SimEventType.Hit, e => e.B == by && (on < 0 || e.A == on));
        public List<SeatEvent> Deaths(int by, int mark = 0) => Since(mark, SimEventType.Death, e => e.B == by);
        public List<SeatEvent> Contacts(int by, int mark = 0) => Since(mark, SimEventType.AttackContact, e => e.A == by);
        public List<SeatEvent> Knocks(int on, int mark = 0) => Since(mark, SimEventType.Knock, e => e.A == on);

        /// <summary>Every event since mark whose A is actor (any type), for "did the sim say anything about this press".</summary>
        public List<SeatEvent> AboutActor(int actor, int mark)
        {
            var r = new List<SeatEvent>();
            for (int i = Math.Max(0, mark); i < All.Count; i++) if (All[i].A == actor) r.Add(All[i]);
            return r;
        }
    }

    /// <summary>
    /// A human player's hands, as a model: writes the InputFrame exactly the way HumanInputState.Fill does (aim point, move,
    /// Attack held while the player wants to swing, the other buttons as one-tick edges) for actor Me, against Target.
    /// Lag: the aim and the approach use where the target was LagTicks ago (a human's tracking delay). HalfMiss: every other
    /// swing is aimed 90 degrees off (a 50% player). Strafe: circle the target while fighting (as a player does to dodge).
    /// The probe's human (player-agency audit, probe/HumanSeatProbe.cs) made permanent.
    /// </summary>
    public sealed class HumanModel
    {
        public int Me;
        public int Target;
        public int LagTicks;
        public bool HalfMiss;
        public bool Strafe;
        /// <summary>Walks in until the target is within this share of the attack range (the probe: 0.8).</summary>
        public double ApproachShare = 0.8;
        /// <summary>False = the model never presses attack (it only moves / aims).</summary>
        public bool Fight = true;
        /// <summary>One-tick buttons for the next frame (the test sets them; the model clears them after one tick).</summary>
        public bool PressAttack, PressAbility, PressDigPlace, PressDodge, PressCancel, PressJump, PressSwitch, HoldInteract;
        /// <summary>Optional move override for the next frame (NaN = the model's own move).</summary>
        public double MoveOverrideX = double.NaN, MoveOverrideZ = double.NaN;
        public int StationBuy;
        /// <summary>Optional override of the aim point for the next frame (NaN = the model's own aim).</summary>
        public double AimOverrideX = double.NaN, AimOverrideZ = double.NaN;
        public readonly InputFrame Frame = SimInput.EmptyInput();
        public int Presses;
        private readonly double[] _hx = new double[64], _hz = new double[64];
        private int _seen;
        private bool _prevWant, _landNext = true;
        private int _strafeDir = 1;

        public HumanModel(int me, int target)
        {
            Me = me;
            Target = target;
        }

        /// <summary>Writes this tick's frame (call once per tick, before MatchTick.AdvanceHumans).</summary>
        public InputFrame Fill(World w)
        {
            InputFrame f = Frame;
            double mx = 0, mz = 0, ax = w.X[Me] + JsMath.Cos(w.Actors[Me].Facing), az = w.Z[Me] + JsMath.Sin(w.Actors[Me].Facing);
            bool want = false;
            int t = Target;
            if (t >= 0 && t < w.ActorCount && w.Actors[t].Alive && w.Actors[Me].Alive)
            {
                int h = w.Tick & 63;
                _hx[h] = w.X[t];
                _hz[h] = w.Z[t];
                _seen++;
                double tx = w.X[t], tz = w.Z[t];
                if (LagTicks > 0 && _seen > LagTicks)
                {
                    int hl = (w.Tick - LagTicks) & 63;
                    tx = _hx[hl];
                    tz = _hz[hl];
                }
                double dx = tx - w.X[Me], dz = tz - w.Z[Me];
                double d = Math.Sqrt(dx * dx + dz * dz);
                ClassConfig cc = w.Config.Class(w.Actors[Me].Cls);
                double reach = cc.AttackRange + w.Config.Class(w.Actors[t].Cls).Radius;
                if (d > ApproachShare * cc.AttackRange && d > 1e-6)
                {
                    mx = dx / d;
                    mz = dz / d;
                }
                if (Strafe && d > 1e-6 && d <= reach + 0.5)
                {
                    // circle the target (tangent), holding the distance near ApproachShare x range; flip every 2 s
                    if (w.Tick % 60 == 0) _strafeDir = -_strafeDir;
                    double ux = dx / d, uz = dz / d;
                    double k = Math.Max(-1, Math.Min(1, (d - ApproachShare * cc.AttackRange) * 2));
                    mx = -uz * _strafeDir + ux * k;
                    mz = ux * _strafeDir + uz * k;
                }
                want = Fight && d <= reach && Combat.AttackReady(w, Me);
                ax = tx;
                az = tz;
                if (HalfMiss && want && !_prevWant) _landNext = !_landNext;
                if (HalfMiss && !_landNext)
                {
                    // every other press aimed 90 degrees off the target: a whiff
                    ax = w.X[Me] - dz;
                    az = w.Z[Me] + dx;
                }
                if (want && !_prevWant) Presses++;
            }
            _prevWant = want;
            f.MoveX = double.IsNaN(MoveOverrideX) ? mx : MoveOverrideX;
            f.MoveZ = double.IsNaN(MoveOverrideZ) ? mz : MoveOverrideZ;
            f.AimX = double.IsNaN(AimOverrideX) ? ax : AimOverrideX;
            f.AimZ = double.IsNaN(AimOverrideZ) ? az : AimOverrideZ;
            f.Attack = want || PressAttack;
            f.Ability = PressAbility;
            f.Terraform = PressDigPlace;
            f.Dodge = PressDodge;
            f.Cancel = PressCancel;
            f.Jump = PressJump;
            f.TerraformSwitch = PressSwitch;
            f.Interact = HoldInteract;
            f.StationBuy = StationBuy;
            f.ClassPick = -1;
            f.PingKind = 0;
            PressAttack = PressAbility = PressDigPlace = PressDodge = PressCancel = PressJump = PressSwitch = false;
            StationBuy = 0;
            AimOverrideX = AimOverrideZ = double.NaN;
            MoveOverrideX = MoveOverrideZ = double.NaN;
            return f;
        }
    }

    /// <summary>A scripted dummy for actor Me: stands still (Passive) or circles Around (Strafer). Never presses a button.</summary>
    public sealed class DummyModel
    {
        public int Me, Around;
        public bool Circle;
        public double Radius, SpeedShare;
        public int Dir = 1;
        /// <summary>One-tick presses and a move for the next frame (test staging: a dummy that swings or dodges on cue).</summary>
        public bool PressAttack, PressDodge, PressAbility;
        public double MoveX, MoveZ;
        public double AimX = double.NaN, AimZ = double.NaN;
        public readonly InputFrame Frame = SimInput.EmptyInput();

        public InputFrame Fill(World w)
        {
            InputFrame f = Frame;
            f.MoveX = 0;
            f.MoveZ = 0;
            f.AimX = w.X[Around];
            f.AimZ = w.Z[Around];
            f.Attack = f.Ability = f.Interact = f.Terraform = f.Jump = f.TerraformSwitch = f.Dodge = f.Cancel = false;
            f.ClassPick = -1;
            f.StationBuy = 0;
            f.PingKind = 0;
            f.MoveX = MoveX;
            f.MoveZ = MoveZ;
            if (!double.IsNaN(AimX)) { f.AimX = AimX; f.AimZ = AimZ; }
            f.Attack = PressAttack;
            f.Dodge = PressDodge;
            f.Ability = PressAbility;
            PressAttack = PressDodge = PressAbility = false;
            if (Circle && w.Actors[Me].Alive)
            {
                double dx = w.X[Me] - w.X[Around], dz = w.Z[Me] - w.Z[Around];
                double d = Math.Sqrt(dx * dx + dz * dz);
                if (d > 1e-6)
                {
                    double ux = dx / d, uz = dz / d;
                    double k = Math.Max(-1, Math.Min(1, (Radius - d) * 1.5));
                    double mx = -uz * Dir + ux * k, mz = ux * Dir + uz * k;
                    double l = Math.Sqrt(mx * mx + mz * mz);
                    if (l > 1e-9)
                    {
                        f.MoveX = mx / l * SpeedShare;
                        f.MoveZ = mz / l * SpeedShare;
                    }
                }
            }
            // the idle rule (MechRules) would hand a still dummy to the autopilot after AfkSeconds: a dummy is not a person
            if (w.Mech != null && w.Mech.IdleTicks != null && Me < w.Mech.IdleTicks.Length) w.Mech.IdleTicks[Me] = 0;
            return f;
        }
    }

    /// <summary>
    /// One player-seat world: actor 0 is the human seat (IsHuman), driven by Human through MatchTick.AdvanceHumans, the one
    /// input seam (Sim/Core/SimInput.cs); everyone else is a bot unless the seat gives it a frame (a dummy or a second human).
    /// </summary>
    public sealed class Seat
    {
        public World W;
        public int Me, Them = -1, Medic = -1;
        public HumanModel Human;
        public HumanModel Human2;
        public DummyModel Dummy;
        public EventSpy Spy;
        public InputFrame[] Frames;
        public InputFrame Scratch = SimInput.EmptyInput();
        /// <summary>Keeps the target's health above this share each tick (land-rate runs that must not end in a kill); 0 = off.</summary>
        public double KeepThemAbove;
        /// <summary>True: the human seat's Demolitions TNT is never ready (so the primary is the pry bar swing).</summary>
        public bool NoTnt;
        /// <summary>
        /// False (default): the rig throws if the idle rule's autopilot (MechRules.AutopilotFrame, AfkSeconds) drove the human
        /// seat on any tick: a bot swinging "for" the player would fake every number (the hit-feel audit's first runs). Only
        /// the idle-rule tests set it.
        /// </summary>
        public bool AllowAutopilot;
        /// <summary>Ticks the autopilot drove the human seat.</summary>
        public int AutopilotTicks;
        /// <summary>Called every tick before the frames are written (a test's staging hook).</summary>
        public Action<Seat> BeforeTick;

        public double Hz => W.Config.SimHz;
        public int Ticks(double seconds) => (int)Math.Ceiling(seconds * W.Config.SimHz);

        /// <summary>One sim tick through the human seam, then the spy reads the events.</summary>
        public void Step()
        {
            BeforeTick?.Invoke(this);
            if (NoTnt && W.Demolitions != null && W.Demolitions.TntReadyTick != null) W.Demolitions.TntReadyTick[Me] = int.MaxValue / 2;
            for (int i = 0; i < Frames.Length; i++) Frames[i] = null;
            if (Human != null) Frames[Human.Me] = Human.Fill(W);
            if (Human2 != null) Frames[Human2.Me] = Human2.Fill(W);
            if (Dummy != null) Frames[Dummy.Me] = Dummy.Fill(W);
            MatchTick.AdvanceHumans(W, Frames, Scratch);
            Spy.Pump();
            if (Human != null && MechRules.Autopiloted(W, Human.Me))
            {
                AutopilotTicks++;
                if (!AllowAutopilot) throw new InvalidOperationException("player-seat rig: the idle autopilot drove the human seat at tick " + W.Tick + " (the model stopped giving input); the measurement would be a bot's, not the player's");
            }
            if (KeepThemAbove > 0 && Them >= 0 && W.Actors[Them].Alive && W.Actors[Them].Hp < KeepThemAbove * W.Actors[Them].MaxHp) W.Actors[Them].Hp = W.Actors[Them].MaxHp;
        }

        public void Steps(int n)
        {
            for (int i = 0; i < n; i++) Step();
        }
    }

    /// <summary>The outcome of one duel.</summary>
    public sealed class DuelResult
    {
        public bool Won, Lost, Timeout;
        public double Seconds;
        /// <summary>The human seat's (actor 0's) swings and the ones that landed (AttackContact with Value &gt; 0, plus TNT throws that hurt the target).</summary>
        public int Swings, Landed;
        /// <summary>The enemy seat's swings and landed swings.</summary>
        public int TheirSwings, TheirLanded;
        public double Dealt, Taken;
        public Seat Seat;
        public double LandRate => Swings > 0 ? (double)Landed / Swings : 0;
        public double TheirLandRate => TheirSwings > 0 ? (double)TheirLanded / TheirSwings : 0;
    }

    /// <summary>
    /// The player-seat rig (player-agency audit 2026-11-22, section 6.3): Full rules, the browser 5v5 config
    /// (BalanceRunner.Config(RulesProfile.Full, null)), the empty map the balance tools use (every body but the seats removed,
    /// as DuelRunner.One does), the human seat at actor 0.
    /// </summary>
    public static class PlayerSeatRig
    {
        public static GameConfig Config() => BalanceRunner.Config(RulesProfile.Full, null);

        /// <summary>A Full-rules world with only actor 0 (cls) alive at (x, z) (NaN = 1.5 u west of the hill centre); no Seat target.</summary>
        public static Seat Field(ClassId me, int seed = 4242, double x = double.NaN, double z = double.NaN, int teamSize = 5, BotDifficulty bots = BotDifficulty.Normal, Action<GameConfig> tweak = null)
        {
            GameConfig cfg = Config();
            tweak?.Invoke(cfg);
            var opts = new WorldOptions { Seed = seed, TeamSize = teamSize, HumanTeam = TeamId.Azure, HumanClass = me, Config = cfg, BotDifficulty = bots };
            World w = World.CreateWorld(opts);
            for (int j = 0; j < w.ActorCount; j++)
            {
                if (j == 0) continue;
                Remove(w, j);
            }
            double px = double.IsNaN(x) ? w.Map.Hill.X - 1.5 : x, pz = double.IsNaN(z) ? w.Map.Hill.Z : z;
            Place(w, 0, me, px, pz);
            var s = new Seat { W = w, Me = 0, Spy = new EventSpy(w), Frames = new InputFrame[w.ActorCount] };
            s.Human = new HumanModel(0, -1) { Fight = false };
            return s;
        }

        /// <summary>Takes actor j out of the match for good (as DuelRunner.One does: no respawn, no meat, no coins).</summary>
        public static void Remove(World w, int j)
        {
            w.Actors[j].Alive = false;
            w.Actors[j].RespawnIn = 1e9;
            w.MeatDropped[j] = 1;
            w.Depth.WasAlive[j] = 0;
        }

        /// <summary>Puts actor j in play as cls at full health at (x, z), standing, unprotected (DuelRunner.Place).</summary>
        public static void Place(World w, int j, ClassId cls, double x, double z)
        {
            Actor a = w.Actors[j];
            a.Cls = cls;
            a.MaxHp = w.Config.Class(cls).MaxHp;
            a.Hp = a.MaxHp;
            a.Alive = true;
            a.SpawnProtect = 0;
            a.StandUp = 0;
            a.AirLen = 0;
            a.RespawnIn = 0;
            w.X[j] = x;
            w.Z[j] = z;
            w.PrevX[j] = x;
            w.PrevZ[j] = z;
            w.Vx[j] = 0;
            w.Vz[j] = 0;
            w.Depth.WasAlive[j] = 1;
            w.MeatDropped[j] = 0;
        }

        /// <summary>The first enemy slot (the other team's first actor).</summary>
        public static int EnemySlot(World w) => w.ActorCount / 2;

        /// <summary>
        /// A duel seat: actor 0 (me) and the enemy's first actor (them) distance apart across the hill centre along angle;
        /// mode picks who drives the enemy. lag / halfMiss / strafe shape the human model of actor 0 (and of the enemy seat in
        /// HumanVsHuman).
        /// </summary>
        public static Seat Duel(ClassId me, ClassId them, TargetMode mode, double angle, int rep, int lag = 0, bool halfMiss = false, bool strafe = false,
            BotDifficulty bots = BotDifficulty.Normal, double distance = PlayerSeatTargets.DuelStartGap, Action<GameConfig> tweak = null)
        {
            GameConfig cfg = Config();
            tweak?.Invoke(cfg);
            var opts = new WorldOptions { Seed = 4242 + rep, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = me, Config = cfg, BotDifficulty = bots };
            int ib0 = 5; // TeamSize 5: the enemy team holds ids 5..9
            if (mode == TargetMode.HumanVsHuman)
            {
                opts.ExtraHumans = new[] { ib0 };
                opts.ExtraHumanClasses = new[] { them };
            }
            World w = World.CreateWorld(opts);
            int ia = 0, ib = EnemySlot(w), ih = mode == TargetMode.Healed ? ib + 1 : -1;
            for (int j = 0; j < w.ActorCount; j++)
                if (j != ia && j != ib && j != ih) Remove(w, j);
            double cx = w.Map.Hill.X, cz = w.Map.Hill.Z;
            double c = Math.Cos(angle), s = Math.Sin(angle);
            Place(w, ia, me, cx - c * distance / 2, cz - s * distance / 2);
            Place(w, ib, them, cx + c * distance / 2, cz + s * distance / 2);
            if (ih >= 0) Place(w, ih, ClassId.Healer, cx + c * (distance / 2 + 2.5), cz + s * (distance / 2 + 2.5));
            var seat = new Seat { W = w, Me = ia, Them = ib, Medic = ih, Spy = new EventSpy(w), Frames = new InputFrame[w.ActorCount] };
            seat.Human = new HumanModel(ia, ib) { LagTicks = lag, HalfMiss = halfMiss, Strafe = strafe };
            if (mode == TargetMode.Passive || mode == TargetMode.Strafer || mode == TargetMode.Healed)
            {
                ClassConfig mc = cfg.Class(me);
                seat.Dummy = new DummyModel
                {
                    Me = ib, Around = ia, Circle = mode == TargetMode.Strafer,
                    Radius = PlayerSeatTargets.StraferRadiusShare * (mc.AttackRange + cfg.Class(them).Radius),
                    SpeedShare = PlayerSeatTargets.StraferSpeedShare,
                };
            }
            if (mode == TargetMode.HumanVsHuman) seat.Human2 = new HumanModel(ib, ia) { LagTicks = lag, HalfMiss = halfMiss, Strafe = strafe };
            return seat;
        }

        /// <summary>Plays a seat until one side dies or maxSeconds (or until stop says so) and counts swings and damage.</summary>
        public static DuelResult Play(Seat seat, double maxSeconds = PlayerSeatTargets.DuelMaxSeconds, Func<Seat, bool> stop = null)
        {
            World w = seat.W;
            int max = seat.Ticks(maxSeconds), start = w.Tick, mark = seat.Spy.Mark;
            var r = new DuelResult { Seat = seat };
            int ia = seat.Me, ib = seat.Them;
            for (int t = 0; t < max; t++)
            {
                seat.Step();
                bool aDead = !w.Actors[ia].Alive, bDead = ib >= 0 && !w.Actors[ib].Alive;
                if (bDead || aDead || (stop != null && stop(seat)))
                {
                    r.Won = bDead && !aDead;
                    r.Lost = aDead && !bDead;
                    break;
                }
            }
            r.Timeout = !r.Won && !r.Lost;
            r.Seconds = (w.Tick - start) / w.Config.SimHz;
            Count(seat, mark, r);
            return r;
        }

        /// <summary>Swing / land / damage counts since mark (melee AttackContact plus TNT throws; a throw lands if its blast hurt an enemy).</summary>
        public static void Count(Seat seat, int mark, DuelResult r)
        {
            int ia = seat.Me, ib = seat.Them;
            EventSpy spy = seat.Spy;
            r.Swings = r.Landed = r.TheirSwings = r.TheirLanded = 0;
            foreach (SeatEvent e in spy.Since(mark, SimEventType.AttackContact))
            {
                if (e.A == ia) { r.Swings++; if (e.Value > 0) r.Landed++; }
                else if (e.A == ib) { r.TheirSwings++; if (e.Value > 0) r.TheirLanded++; }
            }
            foreach (SeatEvent e in spy.Since(mark, SimEventType.TntThrown, x => x.A == ia)) r.Swings++;
            foreach (SeatEvent e in spy.Since(mark, SimEventType.Explosion, x => x.A == ia && x.B == Demolitions.ExplosionTnt))
            {
                // a TNT blast "landed" when it hurt someone other than the thrower at its tick
                bool hurt = spy.Since(mark, SimEventType.Hit, h => h.B == ia && h.A != ia && h.Tick == e.Tick && h.Cause == DamageCause.Explosion).Count > 0;
                if (hurt) r.Landed++;
            }
            r.Dealt = 0;
            r.Taken = 0;
            foreach (SeatEvent e in spy.Since(mark, SimEventType.Hit))
            {
                if (e.B == ia && e.A != ia) r.Dealt += e.Value;
                if (e.A == ia) r.Taken += e.Value;
            }
        }

        /// <summary>The human seat's swings so far (melee contacts plus TNT throws).</summary>
        public static int CountSwings(Seat seat) => seat.Spy.Of(SimEventType.AttackContact, e => e.A == seat.Me).Count + seat.Spy.Of(SimEventType.TntThrown, e => e.A == seat.Me).Count;

        public static double Median(List<double> v)
        {
            if (v.Count == 0) return double.NaN;
            var c = new List<double>(v);
            c.Sort();
            return c[c.Count / 2];
        }

        public static double AngleOf(int k, int n) => 2 * Math.PI * k / n;
    }

    /// <summary>
    /// The gate's measurement log: one parseable line per measured case on the console ("[px] id | case | measured | target |
    /// PASS/FAIL"), and, when CM_PX_DATA_DIR is set, the same row appended to $CM_PX_DATA_DIR/tier-s-measures.csv.
    /// </summary>
    public static class PxLog
    {
        public static bool Line(string id, string what, string measured, string target, bool pass)
        {
            Console.WriteLine("[px] " + id + " | " + what + " | " + measured + " | " + target + " | " + (pass ? "PASS" : "FAIL"));
            string dir = Environment.GetEnvironmentVariable("CM_PX_DATA_DIR");
            if (!string.IsNullOrEmpty(dir))
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, "tier-s-measures.csv");
                    if (!File.Exists(path)) File.AppendAllText(path, "id,case,measured,target,result\n");
                    File.AppendAllText(path, Csv(id) + "," + Csv(what) + "," + Csv(measured) + "," + Csv(target) + "," + (pass ? "PASS" : "FAIL") + "\n");
                }
                catch (IOException) { }
            }
            return pass;
        }

        public static void Csv(string file, string header, string row)
        {
            string dir = Environment.GetEnvironmentVariable("CM_PX_DATA_DIR");
            if (string.IsNullOrEmpty(dir)) return;
            try
            {
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, file);
                if (!File.Exists(path)) File.AppendAllText(path, header + "\n");
                File.AppendAllText(path, row + "\n");
            }
            catch (IOException) { }
        }

        static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "'") + "\"";

        public static string F(double v, string fmt = "0.00") => v.ToString(fmt, CultureInfo.InvariantCulture);
    }
}
