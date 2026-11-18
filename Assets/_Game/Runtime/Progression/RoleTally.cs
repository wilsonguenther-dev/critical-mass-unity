using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.Progression
{
    /// <summary>
    /// b15 profile: every actor's role numbers (RoleStats) for one match, read from the sim's event ring once per tick.
    /// Read only: nothing here writes the world, so the sim and its checksums are untouched. Arrays are sized at Begin,
    /// so Consume allocates nothing.
    /// <para>Sources (SimEventType, A / B as the enum documents them): Death (A victim, B killer) for knock-outs, counted at
    /// most KnockOutsPerVictim times per victim (anti-farm); DeliverBiomass / DeliverCorruption (A, Value units);
    /// HealLanded (A healed, B Healer; self-heals do not count); BankDeposit with Bio Ore (A, Value units); PiecePlaced
    /// (A owner, B slot) and WallBuilt (A, B first cell, -1 = refused) and BridgeRepaired (A builder) for builds;
    /// PiecePickedUp (A carrier, B slot) by a teammate of the slot's owner for builds used; Explosion (A owner, Value
    /// fighters hurt); Terraform (A, Value Dug) and Topple (A attacker, Value &gt;= 0) for demolition; Purify (A, Value);
    /// Dodge (A). Hill seconds and deaths come from the actors at the end.</para>
    /// <para>The match-summary item (feat/b15-match-summary) can read Current for its role awards, or replace these
    /// numbers through MetaHooks.RoleStatsFor.</para>
    /// </summary>
    public sealed class RoleTally
    {
        /// <summary>Knock-outs on the same victim that count toward mastery (farming one idle player stops here).</summary>
        public const int KnockOutsPerVictim = 3;
        /// <summary>Built piece slots tracked for owner look-up (BuiltPieces pool sizes are far below this).</summary>
        public const int PieceSlots = 256;

        public int ActorCount { get; private set; }
        public int EventsMissed { get; private set; }

        RoleStats[] _stats = Array.Empty<RoleStats>();
        int[] _koOn = Array.Empty<int>();   // killer * n + victim
        int[] _koRaw = Array.Empty<int>();
        readonly int[] _pieceOwner = new int[PieceSlots];
        TeamId[] _team = Array.Empty<TeamId>();
        int _head;
        int _epoch;
        World _world;

        public World World => _world;
        /// <summary>The world's Epoch at Begin (a world reset in place starts a new match with the same object).</summary>
        public int Epoch => _epoch;

        /// <summary>Starts the record for w (reading what the ring still holds of it).</summary>
        public void Begin(World w)
        {
            _world = w;
            _epoch = w != null ? w.Epoch : 0;
            int n = w != null ? w.ActorCount : 0;
            ActorCount = n;
            _stats = new RoleStats[n];
            _koOn = new int[n * n];
            _koRaw = new int[n];
            _team = new TeamId[n];
            for (int i = 0; i < n; i++) _team[i] = w.Actors[i].Team;
            for (int i = 0; i < PieceSlots; i++) _pieceOwner[i] = -1;
            _head = w != null ? Math.Max(0, w.EventHead - World.EventCapacity) : 0;   // the ring still holds this match's first events
            EventsMissed = 0;
        }

        bool Valid(int id) => id >= 0 && id < ActorCount;
        static int Units(double v) => double.IsFinite(v) && v > 0 ? (int)Math.Round(v) : 0;

        /// <summary>Reads every event pushed since the last call (allocation-free).</summary>
        public void Consume(World w)
        {
            if (w == null || !ReferenceEquals(w, _world)) return;
            int head = w.EventHead;
            if (head < _head) _head = 0;
            if (head - _head > World.EventCapacity)
            {
                EventsMissed += head - _head - World.EventCapacity;
                _head = head - World.EventCapacity;
            }
            for (; _head < head; _head++) Feed(w.Events[_head % World.EventCapacity]);
        }

        /// <summary>One event (public for tests: a scripted stream needs no world).</summary>
        public void Feed(SimEvent e)
        {
            if (e == null) return;
            switch (e.Type)
            {
                case SimEventType.Death:
                    if (Valid(e.A) && Valid(e.B) && e.A != e.B && _team[e.A] != _team[e.B])
                    {
                        _koRaw[e.B]++;
                        int k = e.B * ActorCount + e.A;
                        if (_koOn[k] < KnockOutsPerVictim)
                        {
                            _koOn[k]++;
                            _stats[e.B].KnockOuts++;
                        }
                    }
                    if (Valid(e.A)) _stats[e.A].Deaths++;
                    break;
                case SimEventType.DeliverBiomass:
                case SimEventType.DeliverCorruption:
                    if (Valid(e.A)) _stats[e.A].Delivered += Units(e.Value);
                    break;
                case SimEventType.HealLanded:
                    if (Valid(e.A) && Valid(e.B) && e.A != e.B && _team[e.A] == _team[e.B]) _stats[e.B].Healing += Units(e.Value);
                    break;
                case SimEventType.BankDeposit:
                    if (Valid(e.A) && e.Bio == BiomassType.Ore) _stats[e.A].OreBanked += Units(e.Value);
                    break;
                case SimEventType.PiecePlaced:
                    if (Valid(e.A))
                    {
                        _stats[e.A].PiecesBuilt++;
                        if (e.B >= 0 && e.B < PieceSlots) _pieceOwner[e.B] = e.A;
                    }
                    break;
                case SimEventType.WallBuilt:
                    if (Valid(e.A) && e.B >= 0) _stats[e.A].PiecesBuilt++;
                    break;
                case SimEventType.BridgeRepaired:
                    if (Valid(e.A) && e.Value > 0) _stats[e.A].PiecesBuilt++;
                    break;
                case SimEventType.PiecePickedUp:
                    if (Valid(e.A) && e.B >= 0 && e.B < PieceSlots)
                    {
                        int owner = _pieceOwner[e.B];
                        if (Valid(owner) && owner != e.A && _team[owner] == _team[e.A]) _stats[owner].PiecesUsed++;
                    }
                    break;
                case SimEventType.Explosion:
                    if (Valid(e.A)) _stats[e.A].ExplosiveHits += Units(e.Value);
                    break;
                case SimEventType.Terraform:
                    if (Valid(e.A) && (int)Math.Round(e.Value) == Terraform.Dug) _stats[e.A].Demolished++;
                    break;
                case SimEventType.Topple:
                    if (Valid(e.A) && e.Value >= 0) _stats[e.A].Demolished++;
                    break;
                case SimEventType.Purify:
                    if (Valid(e.A)) _stats[e.A].Purified += Units(e.Value);
                    break;
                case SimEventType.Dodge:
                    if (Valid(e.A)) _stats[e.A].Dodges++;
                    break;
            }
        }

        /// <summary>Knock-outs before the per-victim cap (the scoreboard's number).</summary>
        public int RawKnockOuts(int id) => Valid(id) ? _koRaw[id] : 0;

        /// <summary>The role numbers of one actor, with hill seconds from the world when it is the tally's own.</summary>
        public RoleStats For(int id)
        {
            if (!Valid(id)) return default;
            RoleStats s = _stats[id];
            if (_world != null && id < _world.ActorCount)
            {
                double hill = _world.Actors[id].HillSeconds;
                s.HillSeconds = double.IsFinite(hill) && hill > 0 ? (int)Math.Floor(hill) : 0;
                s.Deaths = Math.Max(s.Deaths, _world.Actors[id].Deaths);
            }
            return s;
        }

        /// <summary>Tests: set an actor's team without a world (Begin(null) then SetTeams).</summary>
        public void SetTeamsForTests(TeamId[] teams)
        {
            int n = teams.Length;
            ActorCount = n;
            _stats = new RoleStats[n];
            _koOn = new int[n * n];
            _koRaw = new int[n];
            _team = (TeamId[])teams.Clone();
            for (int i = 0; i < PieceSlots; i++) _pieceOwner[i] = -1;
        }
    }

    /// <summary>
    /// Keeps RoleTally.Current for the driver on this GameObject: begins at each new world and reads the events after every
    /// tick. View side only.
    /// </summary>
    [CriticalMass.Runtime.Flow.GameOverlay(Order = 53, Scope = CriticalMass.Runtime.Flow.OverlayScope.MatchOnly)]
    public sealed class RoleTallyRecorder : MonoBehaviour
    {
        public static RoleTally Current { get; private set; }

        SimDriverBehaviour _driver;
        Action<World> _onTick;
        readonly RoleTally _tally = new RoleTally();

        void Awake()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            _onTick = OnTick;
        }

        void OnEnable()
        {
            if (_driver != null) _driver.Ticked += _onTick;
        }

        void OnDisable()
        {
            if (_driver != null) _driver.Ticked -= _onTick;
        }

        void OnTick(World w)
        {
            if (!ReferenceEquals(_tally.World, w) || _tally.Epoch != w.Epoch) _tally.Begin(w);
            _tally.Consume(w);
            Current = _tally;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Current = null;
    }
}
