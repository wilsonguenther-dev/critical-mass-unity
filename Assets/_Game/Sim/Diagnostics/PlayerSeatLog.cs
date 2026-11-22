// Player-seat telemetry (px lane T, 2026-11-22): a read-only recorder of what ONE seat pressed and what each press resolved to.
// Docs and schema: docs/testing/PLAYER-SEAT-TELEMETRY.md. Summary: tools/px-telemetry/summarize.py.
//
// Contract: this class is a pure observer. It reads the World after a tick (w.Inputs[seat], the event ring, positions) and
// never writes the World, the frames or any static the rules read. It is NOT part of the hashed sim state; a match with or
// without it gives the same StateHash at every tick (Sim.Tests/Diagnostics/PlayerSeatLogTests). It allocates while on (it
// builds JSON text); when nobody constructs it, it costs nothing. No Unity types, no clock, no file access: the caller
// gives it a sink (the headless probe writes a file, the Unity logger writes persistentDataPath) and, in Unity, the wall
// clock of each tick and the PlayerFeedback ledger entries (AddCue).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Diagnostics
{
    /// <summary>Where finished JSON lines go (one complete record per call, no trailing newline).</summary>
    public interface IPlayerSeatLogSink
    {
        void WriteLine(string jsonLine);
    }

    /// <summary>A sink that keeps the lines in memory (tests, small probes).</summary>
    public sealed class ListLogSink : IPlayerSeatLogSink
    {
        public readonly List<string> Lines = new List<string>();
        public void WriteLine(string jsonLine) => Lines.Add(jsonLine);
    }

    /// <summary>
    /// One record per input edge of a chosen seat (press and release of Attack, Ability, DigPlace (F), Interact, Dodge, Cancel,
    /// Jump), one per auto-repeated swing while Attack stays held, one per hit taken, one per 0.5 s of held movement, plus a
    /// header and a summary. Each press record says what it resolved to (swing started / buffered / refused with a
    /// PlayerReasons code / ability / dodge / dig / place / mine / build / mend / pulse / tnt / feint / jump / gather ... /
    /// nothing), the swing's outcome and miss reason, the world change, and every feedback cue attached by the caller.
    /// Schema: SchemaVersion (bump on any field rename or meaning change; adding a field does not bump).
    /// </summary>
    public sealed class PlayerSeatLog
    {
        public const int SchemaVersion = 1;

        /// <summary>Ticks a press waits for its answer before it is recorded as "nothing" (the sim buffer is 0.35 s = 11 ticks at 30 Hz).</summary>
        public int ResolveWindowTicks = 14;
        /// <summary>Ticks a record stays open after its last sim answer so view cues (AddCue) can still attach (0.5 s at 30 Hz).</summary>
        public int CueWindowTicks = 15;
        /// <summary>Ticks of held movement per movement sample (0.5 s at 30 Hz).</summary>
        public int MoveSampleTicks = 15;

        public readonly int Seat;
        public int Seed;
        public string Mode = "";
        public string Source = "headless";
        public string Label = "";

        /// <summary>Counters (tests and the summary record).</summary>
        public int Presses, Releases, Repeats, HitsTaken, MoveSamples, RecordsWritten, AutopilotTicks, OrphanCues, Nothing, Refused;

        private readonly IPlayerSeatLogSink _sink;
        private readonly List<Rec> _open = new List<Rec>();
        private readonly List<Ev> _tickEvents = new List<Ev>();
        private readonly int[] _pressTick = new int[Buttons];
        private int[] _lastDodge = new int[0];
        private int _cursor = int.MinValue, _prevMask, _lastTick;
        private double _clockMs = double.NaN;
        private int _clockFrame = -1;
        private bool _begun;
        private int _lastKills;
        private Move _move;
        private InputFrame _frame;
        private bool _wasAlive = true;

        private const int Buttons = 8;
        private const int ButtonJump = PlayerReasons.ButtonJump;

        public PlayerSeatLog(int seat, IPlayerSeatLogSink sink)
        {
            Seat = seat;
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        // ------------------------------------------------------------------------------------------------- public feed

        /// <summary>Unity only: the wall clock (ms) and frame of the tick about to be observed (stamped on presses for cue delays).</summary>
        public void Clock(double realMs, int frame)
        {
            _clockMs = realMs;
            _clockFrame = frame;
        }

        /// <summary>Starts the log on w (writes the header). Called by Observe on first use if the caller did not.</summary>
        public void Begin(World w)
        {
            if (_begun) return;
            _begun = true;
            _cursor = w.EventHead;
            _lastTick = w.Tick;
            _lastDodge = new int[w.ActorCount];
            for (int i = 0; i < _lastDodge.Length; i++) _lastDodge[i] = int.MinValue / 2;
            Actor a = w.Actors[Seat];
            _lastKills = a.Kills;
            var j = new Json();
            j.Str("kind", "header").Int("schema", SchemaVersion).Str("source", Source).Str("label", Label).Int("seed", Seed)
                .Str("rules", w.Config.Rules.ToString()).Str("mode", Mode).Int("seat", Seat).Str("class", a.Cls.ToString())
                .Num("sim_hz", w.Config.SimHz).Int("tick", w.Tick).Int("actors", w.ActorCount)
                .Int("resolve_window_ticks", ResolveWindowTicks).Int("cue_window_ticks", CueWindowTicks).Int("move_sample_ticks", MoveSampleTicks);
            Emit(j);
        }

        /// <summary>
        /// Call once after every sim tick (after MatchTick.Advance / AdvanceHumans) with the frame the seat was GIVEN for that
        /// tick (read-only on both). Pass it: the step consumes the one-tick buttons Dodge, Cancel and Jump by clearing them in
        /// w.Inputs, so with seatFrame null (w.Inputs[seat] is read instead) those three presses are invisible.
        /// </summary>
        public void Observe(World w, InputFrame seatFrame = null)
        {
            if (!_begun) Begin(w);
            int tick = w.Tick - 1;   // the tick that consumed this input
            _lastTick = tick;
            ReadEvents(w);
            Actor me = w.Actors[Seat];
            bool auto = MechRules.Autopiloted(w, Seat);
            InputFrame f = seatFrame ?? w.Inputs[Seat];
            _frame = f;
            int mask = auto || f == null ? 0 : Mask(f);
            if (auto) AutopilotTicks++;
            int rise = mask & ~_prevMask, fall = _prevMask & ~mask;
            _prevMask = mask;
            for (int b = 1; b < Buttons; b++)
                if ((fall & (1 << b)) != 0) WriteRelease(w, b, tick);
            for (int b = 1; b < Buttons; b++)
                if ((rise & (1 << b)) != 0) Open(w, "press", b, tick);
            for (int k = 0; k < _tickEvents.Count; k++) Answer(w, _tickEvents[k], tick, (mask & (1 << PlayerReasons.ButtonAttack)) != 0);
            for (int k = 0; k < _tickEvents.Count; k++) Effects(w, _tickEvents[k], tick);
            Contacts(w, tick);
            JumpCheck(w, tick);
            if (!me.Alive)
                for (int k = 0; k < _open.Count; k++)
                    if (_open[k].Resolved == null && _open[k].Kind != "hit_taken") Resolve(_open[k], tick, "died", 0, null);   // killed before the answer came
            Timeouts(tick);
            Sample(w, f, auto, tick);
            Close(w, tick, false);
            _wasAlive = me.Alive;
        }

        /// <summary>
        /// Unity only: one PlayerFeedback ledger entry. Incoming channels (own hurt, damage direction, low health) attach to the
        /// newest open hit-taken record, every other channel to the newest open press record. simTick &lt;= 0 = unknown.
        /// </summary>
        public void AddCue(string channel, int actor, double value, int reason, int simTick, double realMs, int frame)
        {
            bool incoming = channel == "OwnHurtVignette" || channel == "DamageDirection" || channel == "OwnHurtSound" || channel == "LowHealthCue";
            Rec r = null;
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                Rec o = _open[i];
                if (incoming != (o.Kind == "hit_taken")) continue;
                if (simTick > 0 && simTick < o.Tick) continue;
                r = o;
                break;
            }
            if (r == null) { OrphanCues++; return; }
            if (r.Cues == null) r.Cues = new List<Cue>();
            r.Cues.Add(new Cue { Channel = channel, Actor = actor, Value = value, Reason = reason, Tick = simTick, Ms = realMs, Frame = frame });
        }

        /// <summary>Ends the log: every open record is written (a press still waiting is "nothing"), then the summary record.</summary>
        public void Finish(World w)
        {
            if (!_begun) Begin(w);
            if (_move != null) EmitMove(w);
            Timeouts(int.MaxValue / 2);
            Close(w, int.MaxValue / 2, true);
            Actor a = w.Actors[Seat];
            var j = new Json();
            j.Str("kind", "summary").Int("schema", SchemaVersion).Int("tick", w.Tick).Num("t", w.Tick / w.Config.SimHz).Str("class", a.Cls.ToString())
                .Int("presses", Presses).Int("releases", Releases).Int("repeats", Repeats).Int("hits_taken", HitsTaken)
                .Int("move_samples", MoveSamples).Int("refused", Refused).Int("nothing", Nothing).Int("autopilot_ticks", AutopilotTicks)
                .Int("orphan_cues", OrphanCues).Int("kills", a.Kills - _lastKills).Bool("alive", a.Alive);
            Emit(j);
        }

        // ------------------------------------------------------------------------------------------------- records

        private sealed class Cue
        {
            public string Channel;
            public int Actor, Reason, Tick, Frame;
            public double Value, Ms;
        }

        private sealed class Rec
        {
            public string Kind;
            public int Button, Tick, CloseAt = int.MaxValue, ResolveTick = -1, Frame = -1;
            public double Ms = double.NaN;
            public string Resolved, ReasonName;
            public int Reason;
            public ClassId Cls;
            public double X, Z, Facing, AimX, AimZ, MoveX, MoveZ, Hp;
            public int StunAt, AirAt, StandUpAt;
            public bool SwingingAt;
            public int Tgt = -1;
            public ClassId TgtCls;
            public double TgtDist, TgtReach, TgtOffAim, TgtHalfArc;
            // swing (melee AttackStart B = -1)
            public bool Swing;
            public int SwingStart = -1, Windup, ContactTick = -1, TargetsHit, Tier, Crit, Kills, Blocked, InterruptBy = -99, Structures;
            public double Range, ArcDeg, ContactFacing = double.NaN, Damage, StructDamage;
            public string Outcome, MissReason;
            public double OutBy = double.NaN, ArcBy = double.NaN, MissDist = double.NaN;
            public int MissTarget = -1;
            // world
            public int Dug, Placed, Ore, Walls, Pieces, Mines, Explosions;
            public double Heal;
            public List<string> Events;
            public List<Cue> Cues;
            // hit taken
            public int From = -1;
            public ClassId FromCls;
            public bool FromActor, Killed;
            public double Amount, DirDeg = double.NaN, RelDeg = double.NaN, Knock;
            public string Cause;
        }

        private struct Ev
        {
            public SimEventType Type;
            public int A, B, Tier, Windup;
            public double X, Z, Value, Radius, Impulse, DirX, DirZ;
            public DamageCause Cause;
        }

        private sealed class Move
        {
            public int Start, N, Reversals, IntentReversals, AirN, SwingN, StunN, BridgeN, LadderN, MinLevel, MaxLevel, LevelStart;
            public double X0, Z0, IX, IZ, LastX, LastZ, PStepX, PStepZ, PIX, PIZ;
        }

        // ------------------------------------------------------------------------------------------------- reading

        private void ReadEvents(World w)
        {
            _tickEvents.Clear();
            int from = _cursor;
            if (w.EventHead - from > World.EventCapacity) from = w.EventHead - World.EventCapacity;   // lapped: newest 512 only
            for (int i = from; i < w.EventHead; i++)
            {
                SimEvent e = w.Events[i % World.EventCapacity];
                _tickEvents.Add(new Ev
                {
                    Type = e.Type, A = e.A, B = e.B, Tier = e.Tier, Windup = e.Windup, X = e.X, Z = e.Z, Value = e.Value,
                    Radius = e.Radius, Impulse = e.Impulse, DirX = e.DirX, DirZ = e.DirZ, Cause = e.Cause,
                });
                if (e.Type == SimEventType.Dodge && e.A >= 0 && e.A < _lastDodge.Length) _lastDodge[e.A] = w.Tick - 1;
            }
            _cursor = w.EventHead;
        }

        private static int Mask(InputFrame f)
        {
            int m = 0;
            if (f.Attack) m |= 1 << PlayerReasons.ButtonAttack;
            if (f.Ability) m |= 1 << PlayerReasons.ButtonAbility;
            if (f.Terraform) m |= 1 << PlayerReasons.ButtonDigPlace;
            if (f.Interact) m |= 1 << PlayerReasons.ButtonInteract;
            if (f.Dodge) m |= 1 << PlayerReasons.ButtonDodge;
            if (f.Cancel) m |= 1 << PlayerReasons.ButtonCancel;
            if (f.Jump) m |= 1 << ButtonJump;
            return m;
        }

        private Rec Open(World w, string kind, int button, int tick)
        {
            Actor a = w.Actors[Seat];
            InputFrame f = _frame ?? w.Inputs[Seat];
            var r = new Rec
            {
                Kind = kind, Button = button, Tick = tick, Cls = a.Cls, X = w.X[Seat], Z = w.Z[Seat], Facing = a.Facing,
                AimX = f != null ? f.AimX : double.NaN, AimZ = f != null ? f.AimZ : double.NaN, MoveX = f != null ? f.MoveX : 0, MoveZ = f != null ? f.MoveZ : 0,
                Hp = a.Hp, Ms = _clockMs, Frame = _clockFrame,
                StunAt = a.HitStunTicks, AirAt = a.AirLen, StandUpAt = a.StandUp > 0 ? 1 : 0, SwingingAt = a.SwingPending,
            };
            if (kind == "press") { Presses++; if (button > 0 && button < Buttons) _pressTick[button] = tick; }
            else if (kind == "repeat") Repeats++;
            if (!a.Alive && !_wasAlive && kind != "hit_taken") Resolve(r, tick, "dead", 0, "dead");   // pressed while already dead
            Target(w, r);
            _open.Add(r);
            return r;
        }

        /// <summary>The nearest living enemy at the press: distance, reach (own range + its body), aim offset.</summary>
        private void Target(World w, Rec r)
        {
            int best = Nearest(w, out double bd);
            if (best < 0) return;
            ClassConfig cc = w.Config.Class(r.Cls);
            double rad = w.Config.Class(w.Actors[best].Cls).Radius;
            r.Tgt = best;
            r.TgtCls = w.Actors[best].Cls;
            r.TgtDist = bd;
            r.TgtReach = cc.AttackRange + rad;
            double ad = double.IsNaN(r.AimX) ? r.Facing : JsMath.Atan2(r.AimZ - r.Z, r.AimX - r.X);
            r.TgtOffAim = AngleDeg(JsMath.Atan2(w.Z[best] - r.Z, w.X[best] - r.X) - ad);
            r.TgtHalfArc = cc.AttackArcDeg / 2 + (bd > 1e-6 ? JsMath.Asin(Math.Min(1, rad / bd)) * 180 / Math.PI : 90);
        }

        private int Nearest(World w, out double dist)
        {
            int best = -1;
            dist = double.MaxValue;
            TeamId team = w.Actors[Seat].Team;
            for (int j = 0; j < w.ActorCount; j++)
            {
                Actor t = w.Actors[j];
                if (j == Seat || !t.Alive || t.Team == team) continue;
                double dx = w.X[j] - w.X[Seat], dz = w.Z[j] - w.Z[Seat], d = Math.Sqrt(dx * dx + dz * dz);
                if (d < dist) { dist = d; best = j; }
            }
            return best;
        }

        /// <summary>The oldest open record of button still waiting for its answer (null = none).</summary>
        private Rec Waiting(int button)
        {
            for (int i = 0; i < _open.Count; i++)
                if (_open[i].Button == button && _open[i].Resolved == null && _open[i].Kind != "hit_taken") return _open[i];
            return null;
        }

        /// <summary>The newest open record of button (answered or not).</summary>
        private Rec Latest(int button)
        {
            for (int i = _open.Count - 1; i >= 0; i--)
                if (_open[i].Button == button && _open[i].Kind != "hit_taken") return _open[i];
            return null;
        }

        private Rec NewestPress()
        {
            for (int i = _open.Count - 1; i >= 0; i--)
                if (_open[i].Kind != "hit_taken") return _open[i];
            return null;
        }

        private Rec PendingSwing()
        {
            for (int i = _open.Count - 1; i >= 0; i--)
                if (_open[i].Swing && _open[i].Outcome == null) return _open[i];
            return null;
        }

        private void Resolve(Rec r, int tick, string what, int reason, string reasonName)
        {
            if (r == null || r.Resolved != null) return;
            r.Resolved = what;
            r.Reason = reason;
            r.ReasonName = reasonName;
            r.ResolveTick = tick;
            r.CloseAt = tick + CueWindowTicks;
            if (what == "refused") Refused++;
            else if (what == "nothing") Nothing++;
        }

        private static void Note(Rec r, Ev e)
        {
            if (r == null) return;
            if (r.Events == null) r.Events = new List<string>();
            if (r.Events.Count < 24) r.Events.Add(e.Type + ":" + e.B.ToString(CultureInfo.InvariantCulture) + ":" + Json.F(e.Value));
        }

        private static string Code(string prefix, double v) => prefix + ":" + ((int)v).ToString(CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------------------------------------- answers

        /// <summary>Pass 1 of a tick: the events that ANSWER a press of the seat (A = seat).</summary>
        private void Answer(World w, Ev e, int tick, bool attackHeld)
        {
            if (e.A != Seat) return;
            Rec r;
            switch (e.Type)
            {
                case SimEventType.AttackStart:
                    if (e.B == Combat.KindAttack)
                    {
                        r = Waiting(PlayerReasons.ButtonAttack);
                        if (r == null) r = attackHeld || PendingSwing() == null ? Open(w, "repeat", PlayerReasons.ButtonAttack, tick) : null;
                        if (r == null) return;
                        Resolve(r, tick, r.Kind == "repeat" || tick == r.Tick ? "swing_started" : "buffered", 0, null);
                        ClassConfig cc = w.Config.Class(w.Actors[Seat].Cls);
                        r.Swing = true;
                        r.SwingStart = tick;
                        r.Windup = e.Windup;
                        r.Range = e.Radius > 0 ? e.Radius : cc.AttackRange;
                        r.ArcDeg = e.Impulse > 0 ? e.Impulse : cc.AttackArcDeg;
                        r.CloseAt = int.MaxValue;
                        if (e.Windup <= 0) r.ContactTick = -2;   // the hit is this tick (AttackContact follows in the same tick)
                    }
                    else
                    {
                        r = Waiting(PlayerReasons.ButtonAbility) ?? Waiting(PlayerReasons.ButtonDigPlace);
                        Resolve(r, tick, "ability", e.B, AbilityKind(e.B));
                        Note(r ?? Latest(PlayerReasons.ButtonAbility), e);
                    }
                    return;
                case SimEventType.HealCast:
                    r = e.B == Healer.KindMend ? Waiting(PlayerReasons.ButtonAttack) : Waiting(PlayerReasons.ButtonAbility);
                    if (r == null && e.B == Healer.KindMend && attackHeld) r = Open(w, "repeat", PlayerReasons.ButtonAttack, tick);
                    Resolve(r, tick, e.B == Healer.KindMend ? "mend" : "pulse", 0, null);
                    if (r != null) r.Heal += e.Value;
                    Note(r, e);
                    return;
                case SimEventType.TntThrown:
                    r = Waiting(PlayerReasons.ButtonAttack) ?? Waiting(PlayerReasons.ButtonAbility) ?? Waiting(PlayerReasons.ButtonDigPlace);
                    if (r == null)
                    {
                        Rec same = ResolvedLatest("tnt");   // the throw's Ability event (TntThrownCode) already answered the press this tick
                        if (same != null && same.ResolveTick == tick) r = same;
                    }
                    if (r == null && attackHeld) r = Open(w, "repeat", PlayerReasons.ButtonAttack, tick);
                    Resolve(r, tick, "tnt", 0, null);
                    if (r != null) r.CloseAt = Math.Max(r.CloseAt, tick + 3 * CueWindowTicks);   // fuse: keep it open for the blast
                    Note(r, e);
                    return;
                case SimEventType.Ability:
                    {
                        ClassId cls = w.Actors[Seat].Cls;
                        bool tnt = cls == ClassId.Demolitions && (int)e.Value == Demolitions.TntThrownCode;
                        r = tnt ? Waiting(PlayerReasons.ButtonAttack) ?? Waiting(PlayerReasons.ButtonDigPlace) ?? Waiting(PlayerReasons.ButtonAbility) : Waiting(PlayerReasons.ButtonAbility) ?? Waiting(PlayerReasons.ButtonDigPlace);
                        if (r == null && tnt) r = ResolvedLatest("tnt");
                        if (r != null && r.Resolved == null)
                        {
                            if (e.Value < 0) Resolve(r, tick, "refused", (int)e.Value, AbilityCode(cls, (int)e.Value));
                            else Resolve(r, tick, tnt ? "tnt" : "ability", (int)e.Value, AbilityCode(cls, (int)e.Value));
                        }
                    }
                    Note(r ?? NewestPress(), e);
                    return;
                case SimEventType.MinePlaced:
                    r = Waiting(PlayerReasons.ButtonAbility);
                    Resolve(r, tick, "mine_placed", 0, null);
                    if (r != null) r.Mines++;
                    Note(r, e);
                    return;
                case SimEventType.BarricadePlaced:
                case SimEventType.PiecePlaced:
                    r = Waiting(PlayerReasons.ButtonAbility) ?? Waiting(PlayerReasons.ButtonDigPlace) ?? Waiting(PlayerReasons.ButtonInteract);
                    Resolve(r, tick, e.Type == SimEventType.PiecePlaced ? "piece_placed" : "barricade", 0, null);
                    r = r ?? NewestPress();
                    if (r != null) r.Pieces++;
                    Note(r, e);
                    return;
                case SimEventType.Dodge:
                    r = Waiting(PlayerReasons.ButtonDodge);
                    Resolve(r, tick, "dodge", 0, null);
                    Note(r, e);
                    return;
                case SimEventType.Feint:
                    r = Waiting(PlayerReasons.ButtonCancel);
                    Resolve(r, tick, e.Value >= 2 ? "hit_cancel" : "feint", 0, null);
                    if (e.Value < 2)
                    {
                        Rec s = PendingSwing();
                        if (s != null) { s.Outcome = "cancelled"; s.CloseAt = tick + CueWindowTicks; }
                    }
                    Note(r, e);
                    return;
                case SimEventType.AttackContact:
                    {
                        Rec s = PendingSwing();
                        if (s == null) return;
                        s.ContactTick = tick;
                        s.TargetsHit = (int)e.Value;
                        s.ContactFacing = JsMath.Atan2(e.DirZ, e.DirX);
                        s.CloseAt = int.MaxValue;
                    }
                    return;
                case SimEventType.AttackCancel:
                    {
                        Rec s = PendingSwing();
                        if (s != null) { s.Outcome = "interrupted"; s.InterruptBy = e.B; s.CloseAt = tick + CueWindowTicks; }
                    }
                    return;
                case SimEventType.ActionRefused:
                    r = Waiting(e.B) ?? Latest(e.B);
                    if (r != null && r.Resolved == null) Resolve(r, tick, "refused", (int)e.Value, RefusedName((int)e.Value));
                    else Note(r, e);
                    return;
                case SimEventType.JumpRefused:
                    r = Waiting(ButtonJump);
                    Resolve(r, tick, "refused", -(int)e.Value, e.Value >= 2 ? "jump:past_coyote" : "jump:no_landing");
                    return;
                case SimEventType.Terraform:
                    r = Waiting(PlayerReasons.ButtonDigPlace);
                    if (r != null)
                    {
                        if (e.Value < 0) Resolve(r, tick, "refused", (int)e.Value, TerraformCode((int)e.Value));
                        else Resolve(r, tick, (int)e.Value == Terraform.Placed ? "place" : "dig", 0, null);
                    }
                    return;   // the cell count is pass 2
                case SimEventType.OreMined:
                    r = Waiting(PlayerReasons.ButtonDigPlace) ?? Waiting(PlayerReasons.ButtonInteract);
                    if (r != null)
                    {
                        if (e.Value < 0) Resolve(r, tick, "refused", (int)e.Value, OreCode((int)e.Value));
                        else Resolve(r, tick, "mine", 0, null);
                    }
                    return;
                case SimEventType.WallBuilt:
                    r = Waiting(PlayerReasons.ButtonDigPlace) ?? Waiting(PlayerReasons.ButtonAbility);
                    if (r != null)
                    {
                        if (e.Value < 0 || e.B < 0) Resolve(r, tick, "refused", (int)e.Value, WallCode((int)e.Value));
                        else Resolve(r, tick, "build", 0, null);
                    }
                    return;
                case SimEventType.WorldAction:
                    {
                        int kind = e.B;
                        bool f = kind == PlayerReasons.WorldDig || kind == PlayerReasons.WorldPlace || kind == PlayerReasons.WorldBuild || kind == PlayerReasons.WorldMineOre;
                        r = f ? Waiting(PlayerReasons.ButtonDigPlace) ?? Waiting(PlayerReasons.ButtonInteract) : Waiting(PlayerReasons.ButtonInteract) ?? Waiting(PlayerReasons.ButtonDigPlace);
                        Resolve(r, tick, WorldName(kind), 0, null);
                        Note(r ?? NewestPress(), e);
                    }
                    return;
                case SimEventType.Gather:
                case SimEventType.SourceGather:
                case SimEventType.DeliverBiomass:
                case SimEventType.DeliverCorruption:
                case SimEventType.BankDeposit:
                case SimEventType.BankWithdraw:
                case SimEventType.Handoff:
                case SimEventType.PiecePickedUp:
                case SimEventType.Cleanse:
                case SimEventType.Upgrade:
                case SimEventType.BridgeRepaired:
                case SimEventType.Purify:
                case SimEventType.DeliverRefused:
                    {
                        bool refused = e.Type == SimEventType.DeliverRefused || (e.Type == SimEventType.Upgrade && e.Value < 0) || (e.Type == SimEventType.BridgeRepaired && e.Value < 0) || (e.Type == SimEventType.Purify && e.Value < 0);
                        r = e.Type == SimEventType.Purify ? Waiting(PlayerReasons.ButtonAbility) ?? Waiting(PlayerReasons.ButtonInteract) : Waiting(PlayerReasons.ButtonInteract);
                        if (r != null)
                        {
                            if (refused) Resolve(r, tick, "refused", (int)e.Value, Code(e.Type.ToString(), e.Value));
                            else Resolve(r, tick, e.Type.ToString(), 0, null);
                        }
                        Note(r ?? Latest(PlayerReasons.ButtonInteract), e);
                    }
                    return;
            }
        }

        /// <summary>Pass 2 of a tick: damage, blocks, crits, kills, heals and world changes, attached to the record that caused them.</summary>
        private void Effects(World w, Ev e, int tick)
        {
            switch (e.Type)
            {
                case SimEventType.Hit:
                case SimEventType.Death:
                    if (e.A == Seat && e.B != Seat)
                    {
                        if (e.Type == SimEventType.Death)
                        {
                            Rec h = LatestHitTaken();
                            if (h != null && h.Tick == tick) h.Killed = true;
                            return;
                        }
                        HitTaken(w, e, tick);
                        return;
                    }
                    if (e.B == Seat && e.A != Seat)
                    {
                        Rec s = SwingAt(tick) ?? ResolvedLatest("tnt") ?? NewestPress();
                        if (s == null) return;
                        if (e.Type == SimEventType.Death) s.Kills++;
                        else
                        {
                            s.Damage += e.Value;
                            if (e.Tier > s.Tier) s.Tier = e.Tier;
                        }
                    }
                    return;
                case SimEventType.HitBlocked:
                    if (e.B != Seat) return;
                    {
                        Rec s = SwingAt(tick) ?? NewestPress();
                        if (s != null && (s.Blocked == 0 || (int)e.Value < PlayerReasons.BlockedShield)) s.Blocked = (int)e.Value;
                    }
                    return;
                case SimEventType.CriticalHit:
                    if (e.B != Seat) return;
                    {
                        Rec s = SwingAt(tick);
                        if (s != null) s.Crit = (int)e.Value;
                    }
                    return;
                case SimEventType.StructureHit:
                    if (e.A != Seat) return;
                    {
                        Rec s = SwingAt(tick) ?? ResolvedLatest("tnt") ?? NewestPress();
                        if (s != null) { s.Structures++; s.StructDamage += e.Value; Note(s, e); }
                    }
                    return;
                case SimEventType.Terraform:
                    if (e.A != Seat || e.Value < 0) return;
                    {
                        Rec s = Latest(PlayerReasons.ButtonDigPlace);
                        if (s == null || s.CloseAt < tick) s = ResolvedLatest("tnt") ?? NewestPress();
                        if (s == null) return;
                        if ((int)e.Value == Terraform.Placed) s.Placed++;
                        else s.Dug++;
                    }
                    return;
                case SimEventType.OreMined:
                    if (e.A != Seat || e.Value < 0) return;
                    {
                        Rec s = Latest(PlayerReasons.ButtonDigPlace) ?? Latest(PlayerReasons.ButtonInteract);
                        if (s != null) s.Ore++;
                    }
                    return;
                case SimEventType.WallBuilt:
                    if (e.A != Seat || e.B < 0) return;
                    {
                        Rec s = Latest(PlayerReasons.ButtonDigPlace) ?? Latest(PlayerReasons.ButtonAbility);
                        if (s != null) s.Walls += Math.Max(1, (int)e.Value);
                    }
                    return;
                case SimEventType.HealLanded:
                    if (e.B != Seat) return;
                    {
                        Rec s = ResolvedLatest("mend") ?? ResolvedLatest("pulse");
                        if (s != null && s.Kind != "hit_taken" && s.Heal <= 0) s.Heal += e.Value;
                    }
                    return;
                case SimEventType.Explosion:
                    if (e.A != Seat) return;
                    {
                        Rec s = ResolvedLatest("tnt") ?? Latest(PlayerReasons.ButtonAbility);
                        if (s != null) { s.Explosions++; Note(s, e); }
                    }
                    return;
                case SimEventType.Knock:
                    if (e.A != Seat) return;
                    {
                        Rec h = LatestHitTaken();
                        if (h != null && h.Tick == tick) h.Knock = Math.Max(h.Knock, e.Impulse);
                    }
                    return;
            }
        }

        private Rec SwingAt(int tick)
        {
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                Rec r = _open[i];
                if (r.Swing && (r.ContactTick == tick || (r.ContactTick == -2 && r.SwingStart == tick))) return r;
            }
            return null;
        }

        private Rec ResolvedLatest(string what)
        {
            for (int i = _open.Count - 1; i >= 0; i--)
                if (_open[i].Resolved == what) return _open[i];
            return null;
        }

        private Rec LatestHitTaken()
        {
            for (int i = _open.Count - 1; i >= 0; i--)
                if (_open[i].Kind == "hit_taken") return _open[i];
            return null;
        }

        private void HitTaken(World w, Ev e, int tick)
        {
            Rec r = Open(w, "hit_taken", 0, tick);
            HitsTaken++;
            r.From = e.B;
            r.Amount = e.Value;
            r.Tier = e.Tier;
            r.Cause = e.Cause.ToString();
            r.Resolved = "hit_taken";
            r.ResolveTick = tick;
            r.CloseAt = tick + CueWindowTicks;
            if (e.B >= 0 && e.B < w.ActorCount)
            {
                r.FromActor = true;
                r.FromCls = w.Actors[e.B].Cls;
                double dx = w.X[e.B] - w.X[Seat], dz = w.Z[e.B] - w.Z[Seat];
                if (dx * dx + dz * dz > 1e-12)
                {
                    double ang = JsMath.Atan2(dz, dx);
                    r.DirDeg = Norm360(ang * 180 / Math.PI);
                    r.RelDeg = AngleDeg(ang - w.Actors[Seat].Facing);
                }
            }
        }

        /// <summary>After both passes: a swing whose contact came this tick gets its outcome (hit / blocked / structure / miss + why).</summary>
        private void Contacts(World w, int tick)
        {
            for (int i = 0; i < _open.Count; i++)
            {
                Rec s = _open[i];
                if (!s.Swing || s.Outcome != null) continue;
                bool now = s.ContactTick == tick || (s.ContactTick == -2 && s.SwingStart == tick);
                if (!now)
                {
                    if (s.ContactTick == -1 && tick > s.SwingStart + s.Windup + 2) { s.Outcome = "lost"; s.CloseAt = tick + CueWindowTicks; }
                    continue;
                }
                s.CloseAt = tick + CueWindowTicks;
                if (s.TargetsHit > 0 || s.Damage > 0) { s.Outcome = "hit"; continue; }
                if (s.Blocked > 0 && s.Blocked != PlayerReasons.BlockedShield && s.Blocked != PlayerReasons.BlockedGlancing) { s.Outcome = "blocked"; continue; }
                if (s.Structures > 0) { s.Outcome = "structure"; continue; }
                s.Outcome = "miss";
                int t = Nearest(w, out double d);
                if (t < 0) { s.MissReason = "no_target"; continue; }
                double rad = w.Config.Class(w.Actors[t].Cls).Radius;
                double reach = s.Range + rad;
                double facing = double.IsNaN(s.ContactFacing) ? w.Actors[Seat].SwingFacing : s.ContactFacing;
                double off = Math.Abs(AngleDeg(JsMath.Atan2(w.Z[t] - w.Z[Seat], w.X[t] - w.X[Seat]) - facing));
                double half = s.ArcDeg / 2 + (d > 1e-6 ? JsMath.Asin(Math.Min(1, rad / d)) * 180 / Math.PI : 90);
                s.MissTarget = t;
                s.MissDist = d;
                s.OutBy = d - reach;
                s.ArcBy = off - half;
                bool outR = s.OutBy > 0, outA = s.ArcBy > 0;
                bool dodged = t < _lastDodge.Length && _lastDodge[t] >= s.SwingStart - 1 && _lastDodge[t] <= tick;
                if (dodged && (outR || outA)) s.MissReason = "target_dodged";
                else if (outR && outA) s.MissReason = "range_and_arc";
                else if (outR) s.MissReason = "out_of_range";
                else if (outA) s.MissReason = "outside_arc";
                else s.MissReason = "other";
            }
        }

        private void JumpCheck(World w, int tick)
        {
            Rec r = Waiting(ButtonJump);
            if (r != null && w.Actors[Seat].AirLen > 0) Resolve(r, tick, "jump", 0, null);
        }

        private void Timeouts(int tick)
        {
            for (int i = 0; i < _open.Count; i++)
            {
                Rec r = _open[i];
                if (r.Resolved == null && tick - r.Tick >= ResolveWindowTicks) Resolve(r, Math.Min(tick, r.Tick + ResolveWindowTicks), "nothing", 0, null);
                if (r.Swing && r.Outcome == null && tick >= int.MaxValue / 4) r.Outcome = "pending_at_end";
            }
        }

        private void Close(World w, int tick, bool all)
        {
            for (int i = 0; i < _open.Count; i++)
            {
                Rec r = _open[i];
                bool swingOpen = r.Swing && r.Outcome == null;
                if (!all && (r.Resolved == null || swingOpen || tick < r.CloseAt)) continue;
                WriteRec(w, r);
                _open.RemoveAt(i);
                i--;
            }
        }

        // ------------------------------------------------------------------------------------------------- movement

        private void Sample(World w, InputFrame f, bool auto, int tick)
        {
            Actor a = w.Actors[Seat];
            double mx = f != null ? f.MoveX : 0, mz = f != null ? f.MoveZ : 0;
            bool held = !auto && a.Alive && mx * mx + mz * mz > 0.01;
            if (!held)
            {
                if (_move != null && _move.N >= MoveSampleTicks / 3) EmitMove(w);
                _move = null;
                return;
            }
            double x = w.X[Seat], z = w.Z[Seat];
            int lvl = TerrainHeight.ActorLevel(w, Seat);
            if (_move == null)
            {
                // the sample starts from the position before this tick's step
                double px = w.PrevX[Seat], pz = w.PrevZ[Seat];
                _move = new Move { Start = tick, X0 = px, Z0 = pz, LastX = px, LastZ = pz, MinLevel = lvl, MaxLevel = lvl, LevelStart = lvl };
            }
            Move m = _move;
            m.N++;
            double il = Math.Sqrt(mx * mx + mz * mz);
            double ix = mx / il, iz = mz / il;
            m.IX += ix;
            m.IZ += iz;
            if (m.N > 1 && ix * m.PIX + iz * m.PIZ < -0.5) m.IntentReversals++;
            m.PIX = ix;
            m.PIZ = iz;
            double sx = x - m.LastX, sz = z - m.LastZ;
            double sl = Math.Sqrt(sx * sx + sz * sz), pl = Math.Sqrt(m.PStepX * m.PStepX + m.PStepZ * m.PStepZ);
            if (sl > 1e-4 && pl > 1e-4 && sx * m.PStepX + sz * m.PStepZ < -0.5 * sl * pl) m.Reversals++;
            if (sl > 1e-4) { m.PStepX = sx; m.PStepZ = sz; }
            m.LastX = x;
            m.LastZ = z;
            if (a.AirLen > 0) m.AirN++;
            if (a.SwingPending || (a.SwingTick >= 0 && w.Tick - 1 <= a.SwingTick + a.SwingWindup + a.SwingRecovery && w.Tick - 1 >= a.SwingTick)) m.SwingN++;
            if (a.HitStunTicks > 0) m.StunN++;
            if (MapGrid.CellAt(w.Map, x, z) == Cell.Bridge)
            {
                int ci = JsMath.FloorToInt(z) * w.Map.Width + JsMath.FloorToInt(x);
                if (TerrainHeight.LadderGround(w, ci, out byte _)) m.LadderN++;
                else m.BridgeN++;
            }
            if (lvl < m.MinLevel) m.MinLevel = lvl;
            if (lvl > m.MaxLevel) m.MaxLevel = lvl;
            if (m.N >= MoveSampleTicks)
            {
                EmitMove(w);
                _move = null;
            }
        }

        private void EmitMove(World w)
        {
            Move m = _move;
            if (m == null || m.N == 0) return;
            Actor a = w.Actors[Seat];
            double secs = m.N / w.Config.SimHz;
            double dx = m.LastX - m.X0, dz = m.LastZ - m.Z0, dl = Math.Sqrt(dx * dx + dz * dz);
            double il = Math.Sqrt(m.IX * m.IX + m.IZ * m.IZ);
            double walk = w.Config.Class(a.Cls).MoveSpeed;
            double share = walk > 0 ? dl / (walk * secs) : 0;
            double along = il > 1e-9 ? (dx * m.IX + dz * m.IZ) / il : 0;
            double err = il > 1e-9 && dl > 1e-6 ? Math.Abs(AngleDeg(JsMath.Atan2(dz, dx) - JsMath.Atan2(m.IZ, m.IX))) : double.NaN;
            int end = TerrainHeight.ActorLevel(w, Seat);
            string surface;
            if (m.LadderN > 0) surface = "ladder";
            else if (m.BridgeN > 0) surface = "bridge";
            else if (m.AirN * 2 > m.N) surface = "air";
            else if (m.MaxLevel - m.MinLevel >= 2) surface = end > m.LevelStart ? "climb" : "drop";
            else if (m.MaxLevel != m.MinLevel) surface = end > m.LevelStart ? "stair_up" : end < m.LevelStart ? "stair_down" : "stair";
            else surface = "flat";
            var j = new Json();
            j.Str("kind", "move").Int("tick", m.Start).Num("t", m.Start / w.Config.SimHz).Int("ticks", m.N).Str("class", a.Cls.ToString())
                .Pair("pos", m.X0, m.Z0).Pair("intent", il > 1e-9 ? m.IX / il : 0, il > 1e-9 ? m.IZ / il : 0)
                .Pair("disp", dx, dz).Num("dist", dl).Num("along_intent", along).Num("angle_error_deg", err).Num("speed_share", share)
                .Str("surface", surface).Int("level_from", m.LevelStart).Int("level_to", end)
                .Bool("stuck", share < 0.2).Int("reversals", m.Reversals).Int("intent_reversals", m.IntentReversals)
                .Int("air_ticks", m.AirN).Int("swing_ticks", m.SwingN).Int("stun_ticks", m.StunN);
            Emit(j);
            MoveSamples++;
        }

        // ------------------------------------------------------------------------------------------------- writing

        private void WriteRelease(World w, int b, int tick)
        {
            Releases++;
            var j = new Json();
            j.Str("kind", "release").Int("tick", tick).Num("t", tick / w.Config.SimHz).Str("class", w.Actors[Seat].Cls.ToString())
                .Str("button", ButtonName(b)).Int("held_ticks", tick - _pressTick[b]);
            Emit(j);
        }

        private void WriteRec(World w, Rec r)
        {
            double hz = w.Config.SimHz;
            var j = new Json();
            j.Str("kind", r.Kind).Int("tick", r.Tick).Num("t", r.Tick / hz).Str("class", r.Cls.ToString());
            if (r.Kind == "hit_taken")
            {
                j.Int("from", r.From).Str("from_class", r.FromActor ? r.FromCls.ToString() : r.From == -2 ? "Kaiju" : "none").Num("damage", r.Amount)
                    .Int("tier", r.Tier).Str("cause", r.Cause).Num("from_dir_deg", r.DirDeg).Num("from_rel_deg", r.RelDeg)
                    .Str("from_side", double.IsNaN(r.RelDeg) ? "unknown" : Math.Abs(r.RelDeg) <= 60 ? "front" : Math.Abs(r.RelDeg) >= 120 ? "back" : "side")
                    .Num("knock", r.Knock).Bool("killed", r.Killed).Pair("pos", r.X, r.Z).Num("hp_after", r.Hp);
            }
            else
            {
                j.Str("button", ButtonName(r.Button)).Pair("pos", r.X, r.Z).Num("facing_deg", Norm360(r.Facing * 180 / Math.PI)).Pair("aim", r.AimX, r.AimZ)
                    .Pair("move", r.MoveX, r.MoveZ).Num("hp", r.Hp)
                    .Raw("state", new Json().Int("stun_ticks", r.StunAt).Bool("in_air", r.AirAt > 0).Bool("knocked_down", r.StandUpAt > 0).Bool("winding_up", r.SwingingAt).Body())
                    .Str("resolved", r.Resolved ?? "nothing").Int("resolve_ticks", r.ResolveTick >= 0 ? r.ResolveTick - r.Tick : -1);
                if (r.Resolved == "refused") j.Int("reason", r.Reason).Str("reason_name", r.ReasonName ?? "unknown");
                else if (r.ReasonName != null) j.Str("detail", r.ReasonName);
                if (r.Tgt >= 0)
                    j.Raw("target", new Json().Int("id", r.Tgt).Str("class", r.TgtCls.ToString()).Num("dist", r.TgtDist).Num("reach", r.TgtReach)
                        .Bool("in_reach", r.TgtDist <= r.TgtReach).Num("off_aim_deg", r.TgtOffAim).Num("half_arc_deg", r.TgtHalfArc)
                        .Bool("in_arc", Math.Abs(r.TgtOffAim) <= r.TgtHalfArc).Body());
                else j.Raw("target", "null");
                if (r.Swing)
                {
                    var s = new Json();
                    s.Int("start_tick", r.SwingStart).Int("windup", r.Windup).Int("contact_tick", r.ContactTick == -2 ? r.SwingStart : r.ContactTick)
                        .Num("range", r.Range).Num("arc_deg", r.ArcDeg).Str("outcome", r.Outcome ?? "pending").Int("targets_hit", r.TargetsHit)
                        .Num("damage", r.Damage).Int("tier", r.Tier).Str("critical", CritName(r.Crit)).Int("kills", r.Kills);
                    if (r.Outcome == "miss")
                        s.Str("miss_reason", r.MissReason ?? "other").Int("miss_target", r.MissTarget).Num("miss_dist", r.MissDist)
                            .Num("out_of_range_by", r.OutBy).Num("outside_arc_by_deg", r.ArcBy);
                    if (r.Blocked > 0) s.Int("blocked", r.Blocked).Str("blocked_name", BlockedName(r.Blocked));
                    if (r.Outcome == "interrupted") s.Int("interrupted_by", r.InterruptBy);
                    if (r.Structures > 0) s.Int("structures", r.Structures).Num("structure_damage", r.StructDamage);
                    j.Raw("swing", s.Body());
                }
                else if (r.Damage > 0 || r.Kills > 0)
                    j.Num("damage", r.Damage).Int("kills", r.Kills);
                if (r.Dug + r.Placed + r.Ore + r.Walls + r.Pieces + r.Mines + r.Explosions + r.Structures > 0 || r.Heal > 0 || (r.StructDamage > 0 && !r.Swing))
                {
                    j.Raw("world", new Json().Int("cells_dug", r.Dug).Int("cells_placed", r.Placed).Int("ore", r.Ore).Int("walls", r.Walls)
                        .Int("pieces", r.Pieces).Int("mines", r.Mines).Int("explosions", r.Explosions).Num("structure_damage", r.StructDamage).Num("heal", r.Heal).Body());
                }
                if (r.Events != null && r.Events.Count > 0)
                {
                    var sb = new StringBuilder("[");
                    for (int i = 0; i < r.Events.Count; i++) { if (i > 0) sb.Append(','); Json.Quote(sb, r.Events[i]); }
                    j.Raw("events", sb.Append(']').ToString());
                }
            }
            // cues: PlayerFeedback ledger entries (Unity only; [] headless)
            var c = new StringBuilder("[");
            if (r.Cues != null)
            {
                for (int i = 0; i < r.Cues.Count; i++)
                {
                    Cue q = r.Cues[i];
                    if (i > 0) c.Append(',');
                    var cj = new Json();
                    cj.Str("ch", q.Channel).Int("actor", q.Actor).Num("value", q.Value);
                    if (q.Reason != 0) cj.Int("reason", q.Reason);
                    cj.Int("delay_ticks", q.Tick > 0 ? q.Tick - r.Tick : -1).Num("delay_ms", q.Ms - r.Ms).Int("delay_frames", q.Frame >= 0 && r.Frame >= 0 ? q.Frame - r.Frame : -1);
                    if (r.Swing && r.ContactTick >= 0 && q.Tick > 0) cj.Int("after_contact_ticks", q.Tick - r.ContactTick);
                    c.Append(cj.Body());
                }
            }
            j.Raw("cues", c.Append(']').ToString()).Int("cue_count", r.Cues != null ? r.Cues.Count : 0).Str("cue_source", Source == "unity" ? "ledger" : "none");
            Emit(j);
        }

        private void Emit(Json j)
        {
            RecordsWritten++;
            _sink.WriteLine(j.Body());
        }

        // ------------------------------------------------------------------------------------------------- names

        public static string ButtonName(int b)
        {
            switch (b)
            {
                case PlayerReasons.ButtonAttack: return "attack";
                case PlayerReasons.ButtonAbility: return "ability";
                case PlayerReasons.ButtonDigPlace: return "dig_place";
                case PlayerReasons.ButtonInteract: return "interact";
                case PlayerReasons.ButtonDodge: return "dodge";
                case PlayerReasons.ButtonCancel: return "cancel";
                case PlayerReasons.ButtonJump: return "jump";
                default: return "b" + b.ToString(CultureInfo.InvariantCulture);
            }
        }

        public static string RefusedName(int v)
        {
            switch (v)
            {
                case PlayerReasons.RefusedCooldown: return "cooldown";
                case PlayerReasons.RefusedNoTarget: return "no_target";
                case PlayerReasons.RefusedNotYourClass: return "not_your_class";
                case PlayerReasons.RefusedNoResource: return "no_resource";
                case PlayerReasons.RefusedBlockedPlace: return "blocked_place";
                case PlayerReasons.RefusedStaggered: return "staggered";
                case PlayerReasons.RefusedCarryFull: return "carry_full";
                case PlayerReasons.RefusedOutOfRange: return "out_of_range";
                case PlayerReasons.RefusedNothingToCancel: return "nothing_to_cancel";
                case PlayerReasons.RefusedTeamLimit: return "team_limit";
                case PlayerReasons.RefusedCarrying: return "carrying";
                case PlayerReasons.RefusedInAir: return "in_air";
                case PlayerReasons.RefusedRuleOff: return "rule_off";
                default: return "code_" + v.ToString(CultureInfo.InvariantCulture);
            }
        }

        public static string BlockedName(int v)
        {
            switch (v)
            {
                case PlayerReasons.BlockedSpawnProtect: return "spawn_protect";
                case PlayerReasons.BlockedDodge: return "dodge";
                case PlayerReasons.BlockedWard: return "ward";
                case PlayerReasons.BlockedWall: return "wall";
                case PlayerReasons.BlockedPassive: return "passive";
                case PlayerReasons.BlockedImmune: return "immune";
                case PlayerReasons.BlockedShield: return "shield";
                case PlayerReasons.BlockedGlancing: return "glancing";
                default: return "code_" + v.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Ability event refusal / success codes per class (Abilities, BuiltPieces, Barricades, Demolitions, Healer).</summary>
        public static string AbilityCode(ClassId cls, int v)
        {
            if (v > 0)
            {
                if (cls == ClassId.Demolitions) return v == Demolitions.MinePlacedCode ? "mine_placed" : v == Demolitions.TntThrownCode ? "tnt_thrown" : "code_" + I(v);
                if (cls == ClassId.Builder) return v == BuiltPieces.BuiltLadderCode ? "ladder" : v == BuiltPieces.BuiltBridgeCode ? "bridge" : "code_" + I(v);
                return "cast";
            }
            switch (cls)
            {
                case ClassId.Builder:
                    switch (v) { case -1: return "piece:no_target"; case -2: return "piece:length"; case -3: return "piece:occupied"; case -4: return "piece:route"; case -5: return "piece:rate"; case -6: return "piece:limit"; case -7: return "piece:biomass"; case -8: return "piece:class"; }
                    break;
                case ClassId.Demolitions:
                    switch (v) { case -1: return "mine:occupied"; case -4: return "mine:range"; case -5: return "tnt:cooldown"; case -6: return "tnt:class"; case -7: return "tnt:pool"; }
                    break;
                case ClassId.Miner:
                    switch (v) { case -1: return "barricade:occupied"; case -2: return "barricade:hill"; case -3: return "barricade:route"; case -4: return "barricade:range"; }
                    break;
                case ClassId.Healer:
                    switch (v) { case -1: return "purify:range"; case -2: return "purify:closed"; }
                    break;
            }
            return "ability:" + I(v);
        }

        public static string TerraformCode(int v)
        {
            switch (v)
            {
                case -1: return "terraform:outside";
                case -2: return "terraform:no_target";
                case -3: return "terraform:occupied";
                case -4: return "terraform:route";
                case -5: return "terraform:rate";
                case -11: return "terraform:too_high";
                case -12: return "terraform:too_deep";
                case -13: return "terraform:level_only";
                case -14: return "terraform:someone_there";
                default: return "terraform:" + I(v);
            }
        }

        public static string OreCode(int v)
        {
            switch (v) { case -1: return "ore:no_vein"; case -2: return "ore:full"; case -3: return "ore:other_cargo"; case -4: return "ore:hut_max"; case -5: return "ore:busy"; default: return "ore:" + I(v); }
        }

        public static string WallCode(int v)
        {
            switch (v)
            {
                case -1: return "wall:outside"; case -2: return "wall:ground"; case -3: return "wall:keep_out"; case -4: return "wall:occupied"; case -5: return "wall:wood";
                case -6: return "wall:lock"; case -7: return "wall:route"; case -8: return "wall:rate"; case -9: return "wall:full"; case -10: return "wall:down";
                default: return "wall:" + I(v);
            }
        }

        private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

        private static string CritName(int v) => v == 1 ? "counter" : v == 2 ? "punish" : v == 3 ? "back_hit" : "none";

        private static string AbilityKind(int b)
        {
            switch (b)
            {
                case Combat.KindShield: return "shield";
                case Combat.KindRoots: return "roots";
                case Combat.KindBarricade: return "barricade";
                case Combat.KindMine: return "mine";
                case Combat.KindBuild: return "build";
                case Combat.KindPulse: return "pulse";
                case Combat.KindTnt: return "tnt";
                default: return "kind_" + b.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static string WorldName(int b)
        {
            switch (b)
            {
                case PlayerReasons.WorldDig: return "dig";
                case PlayerReasons.WorldPlace: return "place";
                case PlayerReasons.WorldBuild: return "build";
                case PlayerReasons.WorldMineOre: return "mine";
                case PlayerReasons.WorldBank: return "bank";
                case PlayerReasons.WorldFeed: return "feed";
                case PlayerReasons.WorldCorrupt: return "corrupt";
                case PlayerReasons.WorldPurify: return "purify";
                case PlayerReasons.WorldRepair: return "repair";
                case PlayerReasons.WorldGather: return "gather";
                default: return "world_" + b.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Signed angle in degrees, (-180, 180].</summary>
        private static double AngleDeg(double rad)
        {
            double d = rad * 180 / Math.PI;
            d %= 360;
            if (d > 180) d -= 360;
            if (d <= -180) d += 360;
            return d;
        }

        private static double Norm360(double deg)
        {
            deg %= 360;
            return deg < 0 ? deg + 360 : deg;
        }

        // ------------------------------------------------------------------------------------------------- json

        /// <summary>A minimal JSON object builder (invariant culture, NaN/Infinity written as null).</summary>
        private sealed class Json
        {
            private readonly StringBuilder _b = new StringBuilder(256);
            private bool _any;

            private Json Key(string k)
            {
                _b.Append(_any ? ',' : '{');
                _any = true;
                Quote(_b, k);
                _b.Append(':');
                return this;
            }

            public Json Str(string k, string v)
            {
                Key(k);
                if (v == null) _b.Append("null"); else Quote(_b, v);
                return this;
            }

            public Json Int(string k, int v)
            {
                Key(k)._b.Append(v.ToString(CultureInfo.InvariantCulture));
                return this;
            }

            public Json Num(string k, double v)
            {
                Key(k)._b.Append(F(v));
                return this;
            }

            public Json Bool(string k, bool v)
            {
                Key(k)._b.Append(v ? "true" : "false");
                return this;
            }

            public Json Pair(string k, double a, double b)
            {
                Key(k)._b.Append('[').Append(F(a)).Append(',').Append(F(b)).Append(']');
                return this;
            }

            public Json Raw(string k, string json)
            {
                Key(k)._b.Append(json);
                return this;
            }

            public string Body() => _any ? _b.ToString() + "}" : "{}";

            public static string F(double v) => double.IsNaN(v) || double.IsInfinity(v) ? "null" : v.ToString("0.####", CultureInfo.InvariantCulture);

            public static void Quote(StringBuilder b, string s)
            {
                b.Append('"');
                for (int i = 0; i < s.Length; i++)
                {
                    char ch = s[i];
                    if (ch == '"' || ch == '\\') b.Append('\\').Append(ch);
                    else if (ch < ' ') b.Append(' ');
                    else b.Append(ch);
                }
                b.Append('"');
            }
        }
    }
}
