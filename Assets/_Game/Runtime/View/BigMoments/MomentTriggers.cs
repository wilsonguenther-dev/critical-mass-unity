using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.View.Moments
{
    /// <summary>
    /// Reads the sim's per-tick event ring and its state, and raises big-moment requests (pure, view only; it never writes
    /// the World). Call Observe once per sim tick (SimDriverBehaviour.Ticked); a new match (Epoch) or a reset ring starts
    /// it over. No allocation after the first Observe of a match.
    /// <para>Events used on main: Hit, Death (A victim, B credit: actor id, -2 Kaiju melee, -1 slam or none), Ability (Value 1 =
    /// cast), KaijuSlam (B hits, X/Z centre, Value radius; its deaths are pushed just before it with credit -1), Hatch
    /// (Value = hatch code), BerserkLocked, KaijuKO (B source), MatchEnd. State: hill seconds, allegiance, Kaiju state, clock.
    /// </para>
    /// <para>Events that light up when they exist (looked up by name, so this compiles on main): DG-16's AttackStart (the
    /// player's own swing counts as fighting) and KaijuDamaged (the same), and the reserved names ShieldCharge (A = caster,
    /// Value = enemies hit) and AbilityCharged (A = caster, a full-power cast). An Ability event with Value 2 or more is
    /// also read as a full-power cast (no item writes that yet; refusals are negative).</para>
    /// </summary>
    public sealed class MomentTriggers
    {
        public const int LocalActor = 0;
        /// <summary>Kills within this many sim seconds chain into a multi-kill (the announcer's window too).</summary>
        public const double MultiKillWindow = 4.0;
        public const int WildfireStreak = 5;
        public const int BatteringRamHits = 3;
        public const int OverchargedStreak = 3;
        public const double MatchPointRemaining = 30, AtTheWireSeconds = 10, TurnaroundDeficit = 30, TurnaroundAfter = 60;
        public const double TightFightSeconds = 2.5;
        public const double AllegianceWindow = 30, AllegianceMargin = 2, HillLeadEpsilon = 0.5;
        public const int MaxOut = 16, MaxSlamDeaths = 32;

        public static readonly int AttackStartCode = Code("AttackStart");
        public static readonly int KaijuDamagedCode = Code("KaijuDamaged");
        public static readonly int ShieldChargeCode = Code("ShieldCharge");
        public static readonly int AbilityChargedCode = Code("AbilityCharged");

        /// <summary>The SimEventType member called name as an int, or -1 when this build of the sim has no such event.</summary>
        public static int Code(string name)
        {
            try
            {
                return Enum.IsDefined(typeof(SimEventType), name) ? (int)(SimEventType)Enum.Parse(typeof(SimEventType), name) : -1;
            }
            catch (ArgumentException)
            {
                return -1;
            }
        }

        private int _epoch = int.MinValue;
        private int _cursor = -1;
        private int[] _lifeStreak = new int[0], _multi = new int[0], _hitTick = new int[0], _hitCount = new int[0];
        private double[] _lastKill = new double[0];
        private bool _firstBlood, _matchPoint, _ended, _hatchSeen;
        private double _lastPlayerFight = double.NegativeInfinity;
        private int _leader;
        private double _leadChangedAt = double.NegativeInfinity;
        private double _deficitAzure, _deficitCrimson;
        private int _allegianceLeader;
        private KaijuState _kaijuState = KaijuState.Egg;
        private readonly double[] _slamX = new double[MaxSlamDeaths], _slamZ = new double[MaxSlamDeaths];
        private int _slamCount;
        private readonly MomentRequest[] _out = new MomentRequest[MaxOut];

        /// <summary>Requests raised by the last Observe (oldest first).</summary>
        public int Count { get; private set; }
        public MomentRequest Get(int i) => _out[i];

        /// <summary>Sim time of the local player's last hit taken or dealt (or own swing once DG-16 lands).</summary>
        public double LastPlayerFight => _lastPlayerFight;

        /// <summary>The local player is alive and was in a fight within TightFightSeconds of sim time.</summary>
        public bool TightFight(World w)
        {
            if (w == null || w.ActorCount <= LocalActor || !w.Actors[LocalActor].Alive) return false;
            return w.Match.Time - _lastPlayerFight < TightFightSeconds;
        }

        public void Reset(World w)
        {
            int n = w != null ? Math.Max(w.ActorCount, w.Actors != null ? w.Actors.Length : 0) : 0;
            if (_lifeStreak.Length < n)
            {
                _lifeStreak = new int[n];
                _multi = new int[n];
                _hitTick = new int[n];
                _hitCount = new int[n];
                _lastKill = new double[n];
            }
            for (int i = 0; i < _lifeStreak.Length; i++)
            {
                _lifeStreak[i] = 0;
                _multi[i] = 0;
                _hitTick[i] = int.MinValue;
                _hitCount[i] = 0;
                _lastKill[i] = double.NegativeInfinity;
            }
            _cursor = w != null ? 0 : -1;
            _firstBlood = _matchPoint = _ended = _hatchSeen = false;
            _lastPlayerFight = double.NegativeInfinity;
            _leader = 0;
            _leadChangedAt = double.NegativeInfinity;
            _deficitAzure = _deficitCrimson = 0;
            _allegianceLeader = 0;
            _kaijuState = KaijuState.Egg;
            _slamCount = 0;
            Count = 0;
        }

        public void Observe(World w)
        {
            Count = 0;
            if (w == null) return;
            if (w.Epoch != _epoch || _cursor < 0 || _cursor > w.EventHead)
            {
                bool fresh = w.Epoch != _epoch;
                _epoch = w.Epoch;
                Reset(w);
                // a driver that attaches mid-match only reads what is still in the ring
                _cursor = fresh && w.EventHead > World.EventCapacity ? w.EventHead - World.EventCapacity : 0;
            }
            if (w.ActorCount > _lifeStreak.Length) GrowTo(w.ActorCount);
            if (w.EventHead - _cursor > World.EventCapacity) _cursor = w.EventHead - World.EventCapacity;

            _slamCount = 0;
            double now = w.Match.Time;
            for (; _cursor < w.EventHead; _cursor++)
            {
                SimEvent e = w.Events[_cursor % World.EventCapacity];
                if (e != null) Read(w, e, now);
            }
            ReadState(w, now);
        }

        private void GrowTo(int n)
        {
            Array.Resize(ref _lifeStreak, n);
            Array.Resize(ref _multi, n);
            int old = _hitTick.Length;
            Array.Resize(ref _hitTick, n);
            Array.Resize(ref _hitCount, n);
            Array.Resize(ref _lastKill, n);
            for (int i = old; i < n; i++)
            {
                _hitTick[i] = int.MinValue;
                _lastKill[i] = double.NegativeInfinity;
            }
        }

        private bool Valid(World w, int id) => id >= 0 && id < w.ActorCount && id < _lifeStreak.Length;

        private void Read(World w, SimEvent e, double now)
        {
            int code = (int)e.Type;
            switch (e.Type)
            {
                case SimEventType.Hit:
                    if (e.A == LocalActor || e.B == LocalActor) _lastPlayerFight = now;
                    if (Valid(w, e.B))
                    {
                        if (_hitTick[e.B] != w.Tick)
                        {
                            _hitTick[e.B] = w.Tick;
                            _hitCount[e.B] = 0;
                        }
                        _hitCount[e.B]++;
                        if (_hitCount[e.B] == BatteringRamHits) Raise(w, MomentId.BatteringRam, e.B, e.A, BatteringRamHits, e.X, e.Z);
                    }
                    return;
                case SimEventType.Death:
                    ReadDeath(w, e, now);
                    return;
                case SimEventType.Ability:
                    if (!Valid(w, e.A)) return;
                    if (e.Value >= 2) Raise(w, MomentId.Overcharged, e.A, -1, 1, e.X, e.Z);
                    else if (e.Value == 1 && e.A == LocalActor && _lifeStreak[e.A] >= OverchargedStreak)
                        Raise(w, MomentId.Overcharged, e.A, -1, _lifeStreak[e.A], e.X, e.Z);
                    return;
                case SimEventType.KaijuSlam:
                {
                    double r = e.Value + 0.75, r2 = r * r;
                    int n = 0;
                    for (int i = 0; i < _slamCount; i++)
                    {
                        double dx = _slamX[i] - e.X, dz = _slamZ[i] - e.Z;
                        if (dx * dx + dz * dz <= r2) n++;
                    }
                    _slamCount = 0;
                    if (n > 0) RaiseKaiju(w, MomentId.Quake, w.Kaiju.State, n, e.X, e.Z);
                    return;
                }
                case SimEventType.Hatch:
                {
                    KaijuState s = KaijuMomentTracker.StateOfCode((int)Math.Round(e.Value));
                    _hatchSeen = true;
                    _kaijuState = s;
                    RaiseKaiju(w, MomentId.Hatch, s, 1, e.X, e.Z);
                    return;
                }
                case SimEventType.BerserkLocked:
                    RaiseKaiju(w, MomentId.Berserk, KaijuState.Berserk, 1, e.X, e.Z);
                    return;
                case SimEventType.KaijuKO:
                    if (Valid(w, e.B)) Raise(w, MomentId.KaijuDown, e.B, -1, 1, e.X, e.Z);
                    else RaiseKaiju(w, MomentId.KaijuDown, w.Kaiju.State, 1, e.X, e.Z);
                    return;
                case SimEventType.MatchEnd:
                    ReadEnd(w, now);
                    return;
            }

            if (code < 0) return;
            if (code == AttackStartCode || code == KaijuDamagedCode)
            {
                if (e.A == LocalActor) _lastPlayerFight = now;
            }
            else if (code == ShieldChargeCode)
            {
                if (Valid(w, e.A) && e.Value >= BatteringRamHits) Raise(w, MomentId.BatteringRam, e.A, -1, (int)e.Value, e.X, e.Z);
            }
            else if (code == AbilityChargedCode)
            {
                if (Valid(w, e.A)) Raise(w, MomentId.Overcharged, e.A, -1, 1, e.X, e.Z);
            }
        }

        private void ReadDeath(World w, SimEvent e, double now)
        {
            int victim = e.A, killer = e.B;
            if (victim == LocalActor || killer == LocalActor) _lastPlayerFight = now;
            if (Valid(w, victim))
            {
                _lifeStreak[victim] = 0;
                _multi[victim] = 0;
            }
            if (killer == -1)
            {
                if (_slamCount < MaxSlamDeaths)
                {
                    _slamX[_slamCount] = e.X;
                    _slamZ[_slamCount] = e.Z;
                    _slamCount++;
                }
                return;
            }
            if (!Valid(w, killer)) return;

            if (!_firstBlood)
            {
                _firstBlood = true;
                Raise(w, MomentId.OpeningStrike, killer, victim, 1, e.X, e.Z);
            }
            _multi[killer] = now - _lastKill[killer] <= MultiKillWindow ? _multi[killer] + 1 : 1;
            _lastKill[killer] = now;
            _lifeStreak[killer]++;
            if (killer == LocalActor)
            {
                int m = _multi[killer];
                if (m == 2) Raise(w, MomentId.TwoDown, killer, victim, 2, e.X, e.Z);
                else if (m == 3) Raise(w, MomentId.ThreeDown, killer, victim, 3, e.X, e.Z);
                else if (m >= 4) Raise(w, MomentId.Wildfire, killer, victim, m, e.X, e.Z);
            }
            if (_lifeStreak[killer] == WildfireStreak) Raise(w, MomentId.Wildfire, killer, victim, WildfireStreak, e.X, e.Z);

            if (Valid(w, victim) && LastDefenderFell(w, victim, killer, e.X, e.Z))
                Raise(w, MomentId.HillCleared, killer, victim, 1, e.X, e.Z);
        }

        /// <summary>The victim died on the hill and no living team-mate is left on it, while the killer's team stands on it.</summary>
        public static bool LastDefenderFell(World w, int victim, int killer, double x, double z)
        {
            double hx = w.Map.Hill.X, hz = w.Map.Hill.Z, r = w.Config.Hill.Radius, r2 = r * r;
            double dx = x - hx, dz = z - hz;
            if (dx * dx + dz * dz >= r2) return false;
            TeamId vt = w.Actors[victim].Team, kt = w.Actors[killer].Team;
            if (vt == kt) return false;
            int defenders = 0, attackers = 0;
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive || i == victim) continue;
                double ax = w.X[i] - hx, az = w.Z[i] - hz;
                if (ax * ax + az * az >= r2) continue;
                if (a.Team == vt) defenders++;
                else attackers++;
            }
            return defenders == 0 && attackers > 0;
        }

        private void ReadEnd(World w, double now)
        {
            if (_ended) return;
            _ended = true;
            MatchResult res = w.Match.Winner;
            if (res != MatchResult.Azure && res != MatchResult.Crimson) return;
            TeamId win = res == MatchResult.Azure ? TeamId.Azure : TeamId.Crimson;
            MomentId id = EndMoment(win, now, _leadChangedAt, win == TeamId.Azure ? _deficitAzure : _deficitCrimson);
            MomentRequest r = MomentRequest.Of(id, win);
            r.PlayerOwned = w.ActorCount > LocalActor && w.Actors[LocalActor].Team == win;
            r.Tick = w.Tick;
            r.X = w.Map.Hill.X;
            r.Z = w.Map.Hill.Z;
            Push(r);
        }

        /// <summary>The end frame: AT THE WIRE when the lead changed hands in the last 10 s, TURNAROUND after a 30 s deficit, else HILL TAKEN.</summary>
        public static MomentId EndMoment(TeamId winner, double endTime, double leadChangedAt, double winnerWorstDeficit)
        {
            if (endTime - leadChangedAt <= AtTheWireSeconds) return MomentId.AtTheWire;
            if (winnerWorstDeficit >= TurnaroundDeficit) return MomentId.Turnaround;
            return MomentId.Finisher;
        }

        private void ReadState(World w, double now)
        {
            MatchState m = w.Match;
            GameConfig c = w.Config;
            double diff = m.HillSeconds.Azure - m.HillSeconds.Crimson;
            int leader = diff > HillLeadEpsilon ? 1 : diff < -HillLeadEpsilon ? -1 : 0;
            if (leader != 0 && leader != _leader)
            {
                if (_leader != 0) _leadChangedAt = now;
                _leader = leader;
            }
            if (now >= TurnaroundAfter)
            {
                if (-diff > _deficitAzure) _deficitAzure = -diff;
                if (diff > _deficitCrimson) _deficitCrimson = diff;
            }

            if (!_matchPoint && m.Phase == MatchPhase.Playing && c.MatchSeconds - now <= MatchPointRemaining && _leader != 0)
            {
                _matchPoint = true;
                TeamId t = _leader > 0 ? TeamId.Azure : TeamId.Crimson;
                MomentRequest r = MomentRequest.Of(MomentId.MatchPoint, t);
                r.PlayerOwned = w.ActorCount > LocalActor && w.Actors[LocalActor].Team == t;
                r.Tick = w.Tick;
                Push(r);
            }

            if (!m.Hatched)
            {
                double lead = m.Allegiance.Azure - m.Allegiance.Crimson;
                int al = lead > AllegianceMargin ? 1 : lead < -AllegianceMargin ? -1 : 0;
                if (al != 0)
                {
                    bool inWindow = c.HatchSeconds - now <= AllegianceWindow && c.HatchSeconds - now > 0;
                    if (inWindow && _allegianceLeader != 0 && al != _allegianceLeader)
                        RaiseKaiju(w, MomentId.AllegianceTurns, al > 0 ? KaijuState.Azure : KaijuState.Crimson, 1, w.Kaiju.X, w.Kaiju.Z);
                    _allegianceLeader = al;
                }
            }
            else if (_hatchSeen && w.Kaiju.State != _kaijuState)
            {
                // the sim freezes allegiance at the hatch today; a later rule that turns the Kaiju lights this up
                KaijuState s = w.Kaiju.State;
                if (s == KaijuState.Azure || s == KaijuState.Crimson || s == KaijuState.Berserk)
                    RaiseKaiju(w, s == KaijuState.Berserk ? MomentId.Berserk : MomentId.AllegianceTurns, s, 1, w.Kaiju.X, w.Kaiju.Z);
                _kaijuState = s;
            }
        }

        private void Raise(World w, MomentId id, int actor, int victim, int count, double x, double z)
        {
            var r = new MomentRequest { Id = id, Actor = actor, Victim = victim, Count = count, X = x, Z = z, Tick = w.Tick };
            if (Valid(w, actor))
            {
                r.Team = w.Actors[actor].Team;
                r.Cls = w.Actors[actor].Cls;
                r.PlayerOwned = actor == LocalActor;
            }
            r.Kaiju = w.Kaiju.State;
            Push(r);
        }

        private void RaiseKaiju(World w, MomentId id, KaijuState s, int count, double x, double z)
        {
            var r = new MomentRequest
            {
                Id = id, Actor = -1, Victim = -1, Count = count, X = x, Z = z, Tick = w.Tick, Kaiju = s,
                Team = s == KaijuState.Crimson ? TeamId.Crimson : TeamId.Azure,
            };
            r.PlayerOwned = w.ActorCount > LocalActor && (s == KaijuState.Azure || s == KaijuState.Crimson) &&
                            w.Actors[LocalActor].Team == r.Team;
            Push(r);
        }

        private void Push(in MomentRequest r)
        {
            if (Count < MaxOut) _out[Count++] = r;
        }
    }
}
