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
        /// <summary>The local player (actor 0) is A or B.</summary>
        public bool Local;
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

        private readonly int _capacity;
        private int _head;
        private double _kaijuHp = double.NaN;
        private FxEvent _e;

        public readonly int[] LastHitTick, HoldTick, DeathTick;
        public readonly double[] LastHitDmg, HitDirX, HitDirZ, DeathX, DeathZ;

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
            for (int n = from; n < w.EventHead; n++)
            {
                SimEvent ev = w.Events[n % World.EventCapacity];
                if (Decode(ev, w)) Send(w, a, b, c);
            }
            _head = w.EventHead;

            GameConfig cfg = w.Config;
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor ac = w.Actors[i];
                if (!ac.Alive || ac.AnimAction != 0 || !(ac.AttackCd > 0)) continue;
                if (Math.Abs(ac.AttackCd - cfg.Class(ac.Cls).AttackCooldown) > 1e-9) continue;
                Begin(FxKind.Swing, w, i, -1, w.X[i], w.Z[i], cfg.Class(ac.Cls).AttackArcDeg);
                _e.DirX = JsMath.Cos(ac.Facing);
                _e.DirZ = JsMath.Sin(ac.Facing);
                Send(w, a, b, c);
            }

            Kaiju k = w.Kaiju;
            if (k.State != KaijuState.Egg && !double.IsNaN(_kaijuHp) && k.Hp < _kaijuHp - 1e-9)
            {
                Begin(FxKind.KaijuHit, w, -1, -1, k.X, k.Z, _kaijuHp - k.Hp);
                Send(w, a, b, c);
            }
            _kaijuHp = k.Hp;
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
            bool valid = a >= 0 && a < w.ActorCount;
            _e.Team = valid ? w.Actors[a].Team : TeamId.Azure;
            _e.Cls = valid ? w.Actors[a].Cls : ClassId.Warrior;
            _e.Local = a == 0 || b == 0;
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
                    _e.DirX = dx;
                    _e.DirZ = dz;
                    _e.Heavy = death || ev.Value >= HeavyDamage;
                    if (death)
                    {
                        DeathTick[t] = w.Tick;
                        DeathX[t] = ev.X;
                        DeathZ[t] = ev.Z;
                    }
                    else
                    {
                        LastHitTick[t] = w.Tick;
                        LastHitDmg[t] = ev.Value;
                        HitDirX[t] = dx;
                        HitDirZ[t] = dz;
                    }
                    if (_e.Heavy && !blocked)
                    {
                        HoldTick[t] = w.Tick;
                        if (ev.B >= 0 && ev.B < _capacity) HoldTick[ev.B] = w.Tick;
                    }
                    return true;
                }
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
                    _e.Local = ev.A == 0;
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
