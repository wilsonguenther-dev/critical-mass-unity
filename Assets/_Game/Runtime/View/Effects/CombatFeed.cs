using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>What a view effect reacts to (decoded from the sim's event ring and its state, never written back).</summary>
    public enum FxKind : byte
    {
        Hit, Block, Death, Respawn, Swing, Ability, KaijuHit, KaijuSlam, KaijuRoar, KaijuKO, BerserkLock, Hatch,
        Gather, DeliverBiomass, DeliverCorruption, BarricadePlaced, BarricadeRefused, BarricadeBroken, WallCrack, WallBreak,
        HillChange,
        /// <summary>v2: an explosion (Effects.Explode): Value = radius, B = (int)ExplosionKind.</summary>
        Explosion,
        /// <summary>v2: something fell into water (Effects.Splash): Value = size.</summary>
        Splash,
        /// <summary>v2: a landing from a jump (Effects.Land or the CM_HAS_JUMP decode): Value = 1 heavy actor.</summary>
        Land,
        /// <summary>px lane C: a swing reached its contact tick (AttackContact): A = attacker, Value = bodies hit plus 1 when a
        /// structure was hit the same tick (0 = a whiff), (X, Z) = the reach point, Dir = the swing facing.</summary>
        Contact,
        /// <summary>px lane C: a feint (b16 CANCEL Value 1) or a hit-cancel (2): A = attacker, Value = the sim's code.</summary>
        Feint,
        /// <summary>px lane C: SimEventType.HitBlocked: A = target, B = attacker, Value = PlayerReasons.Blocked* code.</summary>
        Blocked,
        /// <summary>px lane C: SimEventType.StructureHit: A = attacker, B = PlayerReasons.Structure* kind, (X, Z) = the cell
        /// centre, Value = damage (0 with StructureSolid: cannot break), Extra = share of health left (0 = it broke).</summary>
        StructureHit,
        /// <summary>px lane S3: SimEventType.CriticalHit (lead's call 12): A = victim, B = attacker, Value = kind (1 counter,
        /// 2 punish, 3 back hit), Extra = the bonus damage. Raised right after the critical Hit of the same tick.</summary>
        Critical,
    }

    /// <summary>One decoded effect event. Tick is the sim tick the sim raised it in (exact: decoded once per tick).</summary>
    public struct FxEvent
    {
        public FxKind Kind;
        public int Tick;
        /// <summary>The actor it happened to (victim, caster, gatherer), or -1.</summary>
        public int A;
        /// <summary>The other party (attacker id, -2 the Kaiju melee, -1 none) or a slot / node id.</summary>
        public int B;
        public double X, Z;
        /// <summary>Damage, radius, units: the sim event's Value.</summary>
        public double Value;
        /// <summary>Unit direction in sim space from the source to the target (hits), or the facing (swings).</summary>
        public double DirX, DirZ;
        public TeamId Team;
        public ClassId Cls;
        public bool Heavy;
        /// <summary>v2 hit tier: 0 light, 1 heavy, 2 critical or finishing (a death is always 2).</summary>
        public byte Tier;
        /// <summary>v2: the attacker's class and facing when B is an actor (slash arcs follow the real swing).</summary>
        public bool HasAttacker;
        public ClassId AttCls;
        public double AttFacing;
        /// <summary>The local player (actor 0) is A or B.</summary>
        public bool Local;
        /// <summary>DG-16: what did the damage, from the sim event (None on events that carry no cause).</summary>
        public DamageCause Cause;
        /// <summary>px lane C: StructureHit's health share left (the sim event's Radius); 0 elsewhere.</summary>
        public double Extra;
    }

    /// <summary>Receives decoded effect events (FxScene, CombatAudio, the vignette).</summary>
    public interface IFxSink
    {
        void OnFx(ref FxEvent e, World w);
    }

    /// <summary>
    /// Decodes, once per sim tick, the events the sim already exposes (World.Events ring: Hit, Death, Respawn, Ability,
    /// Kaiju*, Gather, Deliver*, Barricade*, WallBreak, HillOwnerChange, BerserkLocked) plus three state edges the sim has
    /// no event for (attack start: AnimAction reset with a full AttackCd; Kaiju damage: Hp drop; shield block: a hit on a
    /// shielded Warrior from inside Damage.IsFrontal's cone). Reads the world only. Keeps a small per-actor record (last
    /// hit tick and direction, heavy-hold tick, death tick) for the pose effects. No allocation after construction.
    /// </summary>
    public sealed class CombatFeed
    {
        /// <summary>Damage at or above this is a heavy hit (Warrior cleave 22, Kaiju melee 30, slam 45): it holds poses.</summary>
        public const double HeavyDamage = 20;
        /// <summary>v2: damage at or above this (the slam's 45) is a critical hit; a death is always critical (finishing).</summary>
        public const double CriticalDamage = 40;

        /// <summary>v2 hit tier from damage: 0 light (under 20), 1 heavy, 2 critical (40 and up) or finishing (a death).</summary>
        public static byte TierOf(double damage, bool death) => death || damage >= CriticalDamage ? (byte)2 : damage >= HeavyDamage ? (byte)1 : (byte)0;

        private readonly int _capacity;
        private int _head;
        private double _kaijuHp = double.NaN;
        private FxEvent _e;

        public readonly int[] LastHitTick, HoldTick, DeathTick;
        public readonly double[] LastHitDmg, HitDirX, HitDirZ, DeathX, DeathZ;
        /// <summary>v2: tier of the hold that started at HoldTick (0 light, 1 heavy, 2 critical): CombatFx maps it to a length.</summary>
        public readonly byte[] HoldTier;
        /// <summary>v2: last tick actor i landed a hit (weapon blood tint hook); KaijuDealtTick for the Kaiju's fists.</summary>
        public readonly int[] LastDealtTick;
        public int KaijuDealtTick = int.MinValue / 2;
        /// <summary>px lane C: who dealt actor i's last hit (-1 none, -2 the Kaiju).</summary>
        public readonly int[] LastHitBy;
        /// <summary>px lane C: actor i's last sim Knock: tick, distance (u), unit direction (sim) and where it stood (sim).</summary>
        public readonly int[] KnockTick;
        public readonly double[] KnockU, KnockDirX, KnockDirZ, KnockX, KnockZ;
        /// <summary>px lane C: the last feint (b16 CANCEL Value 1) tick of attacker i.</summary>
        public readonly int[] FeintTick;
        /// <summary>px lane C: attacker i's last AttackContact tick and what it hit (bodies + 1 for a structure; 0 = whiff).</summary>
        public readonly int[] ContactTick, ContactHits;
        /// <summary>Critical hits (lead's call 12; set by CombatFx.OnCritical): the tick actor i TOOK a critical hit
        /// (CritTick, its kind CritKind: 0 top-tier look, 1 counter, 2 punish, 3 back hit) and the tick it held for one,
        /// as victim or attacker (CritHoldTick: the longer hit-stop).</summary>
        public readonly int[] CritTick, CritHoldTick;
        public readonly byte[] CritKind;
        /// <summary>px lane C: attacker i's last StructureHit tick (a wall swing is not a whiff).</summary>
        public readonly int[] StructureTick;
        /// <summary>px lane C: true once any StructureHit event was decoded this world (polling then stands down).</summary>
        public bool StructureEventsSeen;

        /// <summary>Events decoded since construction (tests).</summary>
        public long Decoded { get; private set; }
        /// <summary>Ring entries lost because more than EventCapacity arrived in one tick (should stay 0).</summary>
        public long Lapped { get; private set; }

        public CombatFeed(int actorCapacity)
        {
            _capacity = actorCapacity;
            LastHitTick = Filled(actorCapacity, int.MinValue / 2);
            HoldTick = Filled(actorCapacity, int.MinValue / 2);
            DeathTick = Filled(actorCapacity, int.MinValue / 2);
            LastHitDmg = new double[actorCapacity];
            HitDirX = new double[actorCapacity];
            HitDirZ = new double[actorCapacity];
            DeathX = new double[actorCapacity];
            DeathZ = new double[actorCapacity];
            HoldTier = new byte[actorCapacity];
            LastDealtTick = Filled(actorCapacity, int.MinValue / 2);
            LastHitBy = Filled(actorCapacity, -1);
            KnockTick = Filled(actorCapacity, int.MinValue / 2);
            KnockU = new double[actorCapacity];
            KnockDirX = new double[actorCapacity];
            KnockDirZ = new double[actorCapacity];
            KnockX = new double[actorCapacity];
            KnockZ = new double[actorCapacity];
            FeintTick = Filled(actorCapacity, int.MinValue / 2);
            ContactTick = Filled(actorCapacity, int.MinValue / 2);
            CritTick = Filled(actorCapacity, int.MinValue / 2);
            CritHoldTick = Filled(actorCapacity, int.MinValue / 2);
            CritKind = new byte[actorCapacity];
            ContactHits = new int[actorCapacity];
            StructureTick = Filled(actorCapacity, int.MinValue / 2);
            _airLen = new int[actorCapacity];
        }
        // Jump landings (branch feat/dg-02-jump-bridges adds Actor.AirLen): the tick AirLen goes from > 0 to 0 is a landing.
        // Integration batch 2: DG-02 is in, so the landing decode is always compiled.
        private readonly int[] _airLen;

        // px lane C: AttackContact is sent after the whole tick is decoded, so a StructureHit raised later in the same tick
        // still turns the swing into a contact (not a whiff). Fixed capacity, no allocation.
        private const int MaxPendingContacts = 64;
        private readonly int[] _pcA = new int[MaxPendingContacts], _pcHits = new int[MaxPendingContacts];
        private readonly double[] _pcX = new double[MaxPendingContacts], _pcZ = new double[MaxPendingContacts],
            _pcDx = new double[MaxPendingContacts], _pcDz = new double[MaxPendingContacts];
        private int _pcCount;

        /// <summary>World reset (CombatFx.ResetForNewWorld): forget the lane C per-actor records.</summary>
        public void ResetPx()
        {
            for (int i = 0; i < _capacity; i++)
            {
                LastHitBy[i] = -1;
                KnockTick[i] = FeintTick[i] = ContactTick[i] = StructureTick[i] = CritTick[i] = CritHoldTick[i] = int.MinValue / 2;
                CritKind[i] = 0;
                KnockU[i] = 0;
                ContactHits[i] = 0;
            }
            StructureEventsSeen = false;
            _pcCount = 0;
        }

        private static int[] Filled(int n, int v)
        {
            var a = new int[n];
            for (int i = 0; i < n; i++) a[i] = v;
            return a;
        }

        /// <summary>Decode everything the last tick produced and hand it to the sinks (call after every MatchTick.Advance).</summary>
        public void OnTick(World w, IFxSink a, IFxSink b = null, IFxSink c = null)
        {
            if (w.EventHead < _head) _head = 0; // world reset
            int from = _head;
            if (w.EventHead - from > World.EventCapacity)
            {
                Lapped += w.EventHead - from - World.EventCapacity;
                from = w.EventHead - World.EventCapacity;
            }
            _pcCount = 0;
            for (int n = from; n < w.EventHead; n++)
            {
                SimEvent ev = w.Events[n % World.EventCapacity];
                if (Decode(ev, w)) Send(w, a, b, c);
            }
            _head = w.EventHead;
            // px lane C: the swings that reached contact this tick, now that every StructureHit of the tick is known.
            for (int k = 0; k < _pcCount; k++)
            {
                int s = _pcA[k];
                int hits = _pcHits[k] + (s < _capacity && StructureTick[s] == w.Tick ? 1 : 0);
                if (s < _capacity) { ContactTick[s] = w.Tick; ContactHits[s] = hits; }
                Begin(FxKind.Contact, w, s, -1, _pcX[k], _pcZ[k], hits);
                _e.DirX = _pcDx[k];
                _e.DirZ = _pcDz[k];
                if (s < w.ActorCount) { _e.HasAttacker = true; _e.AttCls = w.Actors[s].Cls; _e.AttFacing = w.Actors[s].SwingFacing; }
                Send(w, a, b, c);
            }
            _pcCount = 0;

            // Integration batch 2 (DG-16): swings come from the sim's AttackStart event and Kaiju hits from KaijuDamaged
            // (decoded below with the contact position); nothing is inferred from cooldowns or the Kaiju's health any more.

            for (int i = 0; i < w.ActorCount && i < _capacity; i++)
            {
                Actor ac = w.Actors[i];
                int air = ac.AirLen;
                if (_airLen[i] > 0 && air == 0 && ac.Alive)
                {
                    Begin(FxKind.Land, w, i, -1, w.X[i], w.Z[i], ac.Cls == ClassId.Warrior ? 1 : 0);
                    Send(w, a, b, c);
                }
                _airLen[i] = air;
            }
            _kaijuHp = w.Kaiju.Hp;
        }

        private void Send(World w, IFxSink a, IFxSink b, IFxSink c)
        {
            if (a != null) a.OnFx(ref _e, w);
            if (b != null) b.OnFx(ref _e, w);
            if (c != null) c.OnFx(ref _e, w);
        }

        private void Begin(FxKind kind, World w, int a, int b, double x, double z, double value)
        {
            Decoded++;
            _e.Kind = kind;
            _e.Tick = w.Tick;
            _e.A = a;
            _e.B = b;
            _e.X = x;
            _e.Z = z;
            _e.Value = value;
            _e.DirX = 0;
            _e.DirZ = 0;
            _e.Heavy = false;
            _e.Tier = 0;
            _e.HasAttacker = false;
            _e.AttCls = ClassId.Warrior;
            _e.AttFacing = 0;
            bool valid = a >= 0 && a < w.ActorCount;
            _e.Team = valid ? w.Actors[a].Team : TeamId.Azure;
            _e.Cls = valid ? w.Actors[a].Cls : ClassId.Warrior;
            _e.Local = a == LocalSeat.Actor || b == LocalSeat.Actor;
            _e.Cause = DamageCause.None;
            _e.Extra = 0;
        }

        private bool Decode(SimEvent ev, World w)
        {
            switch (ev.Type)
            {
                case SimEventType.Hit:
                case SimEventType.Death:
                {
                    int t = ev.A;
                    if (t < 0 || t >= w.ActorCount || t >= _capacity) return false;
                    bool death = ev.Type == SimEventType.Death;
                    double sx, sz;
                    Source(w, ev.B, ev.X, ev.Z, out sx, out sz);
                    double dx = ev.X - sx, dz = ev.Z - sz, d = Math.Sqrt(dx * dx + dz * dz);
                    if (d > 1e-6) { dx /= d; dz /= d; }
                    else { dx = -JsMath.Cos(w.Actors[t].Facing); dz = -JsMath.Sin(w.Actors[t].Facing); }
                    bool blocked = !death && w.Actors[t].Cls == ClassId.Warrior && w.Actors[t].AbilityActive > 0 &&
                                   Damage.IsFrontal(w, t, sx, sz);
                    Begin(death ? FxKind.Death : blocked ? FxKind.Block : FxKind.Hit, w, t, ev.B, ev.X, ev.Z, ev.Value);
                    _e.Cause = ev.Cause;
                    _e.DirX = dx;
                    _e.DirZ = dz;
                    _e.Heavy = death || ev.Value >= HeavyDamage;
                    _e.Tier = TierOf(ev.Value, death);
                    if (ev.B >= 0 && ev.B < w.ActorCount)
                    {
                        _e.HasAttacker = true;
                        _e.AttCls = w.Actors[ev.B].Cls;
                        _e.AttFacing = w.Actors[ev.B].Facing;
                        if (ev.B < _capacity) LastDealtTick[ev.B] = w.Tick;
                    }
                    else if (ev.B == Damage.KaijuSource) KaijuDealtTick = w.Tick;
                    if (death)
                    {
                        DeathTick[t] = w.Tick;
                        DeathX[t] = ev.X;
                        DeathZ[t] = ev.Z;
                    }
                    else
                    {
                        LastHitTick[t] = w.Tick;
                        LastHitBy[t] = ev.B;
                        LastHitDmg[t] = ev.Value;
                        HitDirX[t] = dx;
                        HitDirZ[t] = dz;
                    }
                    if (!blocked)
                    {
                        // v2 tiers: every hit holds the victim's pose; heavy and critical hold the attacker too.
                        HoldTick[t] = w.Tick;
                        HoldTier[t] = _e.Tier;
                        // feat/hit-feel-1: the attacker holds on every hit too (hit-stop on both fighters, HitFeel.HoldWallSeconds).
                        // px lane C: every actor-dealt swing hit (melee or a light weapon, cause Melee or None) holds the
                        // attacker's pose too (the audit: "the attacker never pauses"); blasts and falls do not.
                        bool swingHit = ev.Cause == DamageCause.Melee || ev.Cause == DamageCause.None;
                        if ((_e.Tier > 0 || swingHit) && ev.B >= 0 && ev.B < _capacity && ev.B != t) { HoldTick[ev.B] = w.Tick; HoldTier[ev.B] = _e.Tier; }
                    }
                    return true;
                }
                case SimEventType.AttackStart:
                {
                    // A = attacker, Value = facing (radians). The swing effect starts at the attacker, along the real swing.
                    int s = ev.A;
                    if (s < 0 || s >= w.ActorCount) return false;
                    Begin(FxKind.Swing, w, s, -1, w.X[s], w.Z[s], CriticalMass.Runtime.View.Anim.LightWeaponView.ArcDeg(w.Config, w.Actors[s].Cls)); // feat/light-weapons-2
                    _e.DirX = JsMath.Cos(ev.Value);
                    _e.DirZ = JsMath.Sin(ev.Value);
                    return true;
                }
                case SimEventType.Knock:
                {
                    // px lane C: the sim knock (Impulse = travelled distance after mass and shield), for the drawn lead.
                    int t = ev.A;
                    if (t < 0 || t >= _capacity || t >= w.ActorCount || !(ev.Impulse > 0)) return false;
                    KnockTick[t] = w.Tick;
                    KnockU[t] = ev.Impulse;
                    double l = Math.Sqrt(ev.DirX * ev.DirX + ev.DirZ * ev.DirZ);
                    KnockDirX[t] = l > 1e-6 ? ev.DirX / l : 0;
                    KnockDirZ[t] = l > 1e-6 ? ev.DirZ / l : 0;
                    KnockX[t] = ev.X;
                    KnockZ[t] = ev.Z;
                    return false;
                }
                case SimEventType.AttackContact:
                {
                    int s = ev.A;
                    if (s < 0 || s >= w.ActorCount || _pcCount >= MaxPendingContacts) return false;
                    _pcA[_pcCount] = s;
                    _pcHits[_pcCount] = (int)Math.Round(ev.Value);
                    _pcX[_pcCount] = ev.X;
                    _pcZ[_pcCount] = ev.Z;
                    _pcDx[_pcCount] = ev.DirX;
                    _pcDz[_pcCount] = ev.DirZ;
                    _pcCount++;
                    return false;
                }
                case SimEventType.Feint:
                {
                    int s = ev.A;
                    if (s < 0 || s >= w.ActorCount) return false;
                    if (ev.Value == 1 && s < _capacity) FeintTick[s] = w.Tick;
                    Begin(FxKind.Feint, w, s, -1, w.X[s], w.Z[s], ev.Value);
                    return true;
                }
                case SimEventType.HitBlocked:
                {
                    int t = ev.A;
                    if (t < 0 || t >= w.ActorCount) return false;
                    double sx, sz;
                    Source(w, ev.B, w.X[t], w.Z[t], out sx, out sz);
                    double dx = w.X[t] - sx, dz = w.Z[t] - sz, d = Math.Sqrt(dx * dx + dz * dz);
                    if (d > 1e-6) { dx /= d; dz /= d; } else { dx = 0; dz = 0; }
                    Begin(FxKind.Blocked, w, t, ev.B, w.X[t], w.Z[t], ev.Value);
                    _e.DirX = dx;
                    _e.DirZ = dz;
                    if (ev.B >= 0 && ev.B < w.ActorCount) { _e.HasAttacker = true; _e.AttCls = w.Actors[ev.B].Cls; _e.AttFacing = w.Actors[ev.B].SwingFacing; }
                    return true;
                }
                case SimEventType.StructureHit:
                {
                    int s = ev.A;
                    StructureEventsSeen = true;
                    if (s >= 0 && s < _capacity) StructureTick[s] = w.Tick;
                    Begin(FxKind.StructureHit, w, s, ev.B, ev.X, ev.Z, ev.Value);
                    _e.Extra = ev.Radius;
                    if (s >= 0 && s < w.ActorCount)
                    {
                        double dx = ev.X - w.X[s], dz = ev.Z - w.Z[s], d = Math.Sqrt(dx * dx + dz * dz);
                        if (d > 1e-6) { _e.DirX = dx / d; _e.DirZ = dz / d; }
                        _e.HasAttacker = true;
                        _e.AttCls = w.Actors[s].Cls;
                        _e.AttFacing = w.Actors[s].SwingFacing;
                    }
                    return true;
                }
                case SimEventType.CriticalHit:
                    if (ev.A < 0 || ev.A >= w.ActorCount) return false;
                    Begin(FxKind.Critical, w, ev.A, ev.B, w.X[ev.A], w.Z[ev.A], ev.Value);
                    _e.Extra = ev.Radius;
                    return true;
                case SimEventType.KaijuDamaged:
                    // (X, Z) = the contact point on the Kaiju toward the attacker (A), Value = damage dealt.
                    Begin(FxKind.KaijuHit, w, -1, -1, ev.X, ev.Z, ev.Value);
                    _e.Local = ev.A == LocalSeat.Actor;
                    return true;
                case SimEventType.Respawn:
                    if (ev.A >= 0 && ev.A < _capacity) DeathTick[ev.A] = int.MinValue / 2;
                    Begin(FxKind.Respawn, w, ev.A, -1, ev.X, ev.Z, ev.Value);
                    return true;
                case SimEventType.Ability:
                    Begin(ev.Value == 1 ? FxKind.Ability : FxKind.BarricadeRefused, w, ev.A, -1, ev.X, ev.Z, ev.Value);
                    return true;
                case SimEventType.KaijuSlam:
                    Begin(FxKind.KaijuSlam, w, -1, ev.B, ev.X, ev.Z, ev.Value);
                    _e.Heavy = true;
                    return true;
                case SimEventType.KaijuMeleeWindup:
                    Begin(FxKind.KaijuRoar, w, -1, ev.A, w.Kaiju.X, w.Kaiju.Z, ev.Value);
                    _e.Local = ev.A == LocalSeat.Actor;
                    return true;
                case SimEventType.KaijuKO:
                    Begin(FxKind.KaijuKO, w, -1, ev.B, ev.X, ev.Z, 0);
                    return true;
                case SimEventType.BerserkLocked:
                    Begin(FxKind.BerserkLock, w, -1, -1, ev.X, ev.Z, ev.Value);
                    return true;
                case SimEventType.Hatch:
                    Begin(FxKind.Hatch, w, -1, -1, ev.X, ev.Z, ev.Value);
                    return true;
                case SimEventType.Gather:
                    Begin(FxKind.Gather, w, ev.A, ev.B, ev.X, ev.Z, ev.Value);
                    return true;
                case SimEventType.DeliverBiomass:
                case SimEventType.DeliverCorruption:
                    Begin(ev.Type == SimEventType.DeliverBiomass ? FxKind.DeliverBiomass : FxKind.DeliverCorruption, w, ev.A, -1, ev.X, ev.Z, ev.Value);
                    return true;
                case SimEventType.BarricadePlaced:
                    Begin(FxKind.BarricadePlaced, w, ev.A, ev.B, ev.X, ev.Z, ev.Value);
                    return true;
                case SimEventType.BarricadeBroken:
                    Begin(FxKind.BarricadeBroken, w, ev.A, ev.B, ev.X, ev.Z, ev.Value);
                    return true;
                case SimEventType.WallBreak:
                    Begin(ev.Value == Cell.BreakableCracked ? FxKind.WallCrack : FxKind.WallBreak, w, ev.A, ev.B, ev.X, ev.Z, ev.Value);
                    return true;
                case SimEventType.HillOwnerChange:
                    Begin(FxKind.HillChange, w, -1, -1, ev.X, ev.Z, ev.A);
                    return true;
                // ---- batch 3: Demolitions, Builder and Healer events reach the same effects, force bus and audio paths
                case SimEventType.Explosion:
                    // DG-22: the same FxKind.Explosion Effects.Explode injects (fireball, scorch, crater dust, force ring).
                    Begin(FxKind.Explosion, w, ev.A, (int)(ev.B == Demolitions.ExplosionMine ? ExplosionKind.Mine : ExplosionKind.Tnt),
                        ev.X, ev.Z, Math.Max(0.5, ev.Radius));
                    return true;
                case SimEventType.MinePlaced:
                case SimEventType.TntLanded:
                    Begin(FxKind.BarricadePlaced, w, ev.A, -1, ev.X, ev.Z, ev.Radius);   // a dust puff where it lands
                    return true;
                case SimEventType.PiecePlaced:
                    Begin(FxKind.BarricadePlaced, w, ev.A, -1, ev.X, ev.Z, ev.Radius);   // DG-24: dust where the ladder or bridge goes up
                    return true;
                case SimEventType.PieceExpired:
                case SimEventType.PieceDestroyed:
                    Begin(FxKind.BarricadeBroken, w, ev.A, -1, ev.X, ev.Z, ev.Value);    // wood debris when a piece goes
                    return true;
                case SimEventType.HealCast:
                    // DG-40: Healing Wings (B = 2) rings out like a respawn beam on the Healer's team colour.
                    if (ev.B != 2) return false;
                    Begin(FxKind.Respawn, w, ev.A, -1, ev.X, ev.Z, ev.Radius);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Where a hit came from: the attacker, the Kaiju (melee -2), else the slam centre.</summary>
        private static void Source(World w, int b, double tx, double tz, out double sx, out double sz)
        {
            if (b >= 0 && b < w.ActorCount) { sx = w.X[b]; sz = w.Z[b]; return; }
            if (b == Damage.KaijuSource) { sx = w.Kaiju.X; sz = w.Kaiju.Z; return; }
            if (w.Kaiju.State != KaijuState.Egg) { sx = w.Kaiju.TelegraphX; sz = w.Kaiju.TelegraphZ; return; }
            sx = tx;
            sz = tz;
        }
    }
}
