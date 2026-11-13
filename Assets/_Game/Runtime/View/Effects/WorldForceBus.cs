using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>
    /// The one world-force bus (contract C1, DG-77). CombatFeed stays its only decoder of World.Events (decoded once per
    /// sim tick); CombatFx hands every decoded FxEvent here (OnFx), Effects.Explode / Splash / Land arrive through the same
    /// CombatFx.Inject path, and the animation lane's footfalls (DG-93) call Push directly. Each force gets the tick's next
    /// Seq and a Seed, goes to every listener in subscription order, and competes for one of 16 shader slots.
    /// <para>Slot priority = Strength x remaining life; when all 16 are live the weakest is evicted, never the oldest, so a
    /// Kaiju slam survives a 16-Warrior brawl. A per-tick cap drops only forces weaker than StrongFloor once the tick has
    /// delivered PerTickCap of them, so a flood can never starve a slam. No allocation after construction. View only.</para>
    /// </summary>
    public sealed class WorldForceBus : IFxSink
    {
        public const int SlotCount = 16, MaxListeners = 16, MaxPending = 32;
        /// <summary>Forces at or above this strength are never dropped by the per-tick cap.</summary>
        public const float StrongFloor = 0.6f;

        /// <summary>The running bus (CombatFx creates it in Start, clears it in OnDestroy).</summary>
        public static WorldForceBus Active { get; internal set; }

        private struct Slot { public WorldForce F; public double Born, Life; public bool Used; }

        private readonly Slot[] _slots = new Slot[SlotCount];
        private readonly IWorldForceListener[] _listeners = new IWorldForceListener[MaxListeners];
        private readonly WorldForce[] _pending = new WorldForce[MaxPending];
        private readonly int[] _pendingTick = new int[MaxPending];
        private int _listenerCount, _pendingCount;
        private readonly double _hz;
        private int _tick = int.MinValue, _seq, _perTick;
        private WorldForce _f;
        private MapDef _surfaceMap;
        private byte[] _surface;

        /// <summary>Sim seconds per wall second (the match pace); slot lives are authored in wall seconds.</summary>
        public double SimPerWall = 1.0;
        /// <summary>Forces delivered per tick before weak ones are dropped (ReactionBudget sets it per quality tier).</summary>
        public int PerTickCap = 96;

        /// <summary>Forces delivered and forces dropped by the cap since construction (tests, perf overlay).</summary>
        public long Pushed { get; private set; }
        public long Dropped { get; private set; }

        public WorldForceBus(double simHz)
        {
            _hz = simHz > 0 ? simHz : 30.0;
        }

        // ------------------------------------------------------------------ listeners

        public bool Subscribe(IWorldForceListener l)
        {
            if (l == null) return false;
            for (int i = 0; i < _listenerCount; i++) if (ReferenceEquals(_listeners[i], l)) return true;
            if (_listenerCount >= MaxListeners) return false;
            _listeners[_listenerCount++] = l;
            return true;
        }

        public void Unsubscribe(IWorldForceListener l)
        {
            for (int i = 0; i < _listenerCount; i++)
            {
                if (!ReferenceEquals(_listeners[i], l)) continue;
                for (int k = i + 1; k < _listenerCount; k++) _listeners[k - 1] = _listeners[k];
                _listeners[--_listenerCount] = null;
                return;
            }
        }

        public int ListenerCount => _listenerCount;

        /// <summary>Push into the running bus; false when none runs (menus, tests without CombatFx).</summary>
        public static bool PushActive(in WorldForce f) => Active != null && Active.Push(f);

        // ------------------------------------------------------------------ emit

        /// <summary>
        /// Deliver one force: Seq (order inside its tick) and Seed are assigned here; Tick must be the sim tick it belongs
        /// to. Returns false when the per-tick cap dropped it (weak forces only).
        /// </summary>
        public bool Push(in WorldForce f)
        {
            _f = f;
            if (_f.Tick != _tick)
            {
                _tick = _f.Tick;
                _seq = 0;
                _perTick = 0;
            }
            if (_perTick >= PerTickCap && _f.Strength < StrongFloor)
            {
                Dropped++;
                return false;
            }
            _perTick++;
            _f.Seq = _seq++;
            _f.Seed = Hash((uint)_f.Tick, (uint)_f.Seq, (uint)_f.Source + 0x9E37u);
            Pushed++;
            Insert(in _f);
            for (int i = 0; i < _listenerCount; i++)
            {
                WorldForce copy = _f; // a listener can never change what the next one sees
                _listeners[i].OnForce(ref copy);
            }
            return true;
        }

        /// <summary>Queue a force for a later tick (death landings); delivered by OnTick when the tick arrives.</summary>
        public bool Schedule(in WorldForce f, int tick)
        {
            if (_pendingCount >= MaxPending)
            {
                Dropped++;
                return false;
            }
            _pending[_pendingCount] = f;
            _pendingTick[_pendingCount] = tick;
            _pendingCount++;
            return true;
        }

        /// <summary>Once per sim tick after the decode: deliver the scheduled forces that are due (in schedule order).</summary>
        public void OnTick(World w)
        {
            int k = 0, n = _pendingCount;
            for (int i = 0; i < n; i++)
            {
                if (_pendingTick[i] <= w.Tick)
                {
                    WorldForce f = _pending[i];
                    f.Tick = w.Tick;
                    Push(f);
                }
                else
                {
                    _pending[k] = _pending[i];
                    _pendingTick[k] = _pendingTick[i];
                    k++;
                }
            }
            // Anything a listener scheduled while these were delivered was appended after n: keep it.
            for (int i = n; i < _pendingCount; i++)
            {
                _pending[k] = _pending[i];
                _pendingTick[k] = _pendingTick[i];
                k++;
            }
            _pendingCount = k;
        }

        /// <summary>A Kaiju footfall (until DG-93 pushes them from the clip's contact table).</summary>
        public void PushKaijuStep(World w, double x, double z)
        {
            var f = Make(w, ForceSource.KaijuStep, ForceShape.Footfall, x, z, 0, 0, 0f, ExplosionKind.KaijuSlam);
            f.Actor = -1;
            f.Other = Damage.KaijuSource;
            Push(f);
        }

        /// <summary>A force at sim (x, z) with the table's strength, radius and shock, the surface and height filled in.</summary>
        public WorldForce Make(World w, ForceSource s, ForceShape shape, double x, double z, double dirX, double dirZ, float size, ExplosionKind kind)
        {
            ForceTable.Of(s, size, kind, out float strength, out float radius, out float shock);
            var f = new WorldForce
            {
                Tick = w != null ? w.Tick : 0,
                X = (float)x,
                Z = (float)z,
                Y = w != null && w.Map != null ? (float)GroundHeight.At(w.Map, x, z) : 0f,
                DirX = (float)dirX,
                DirZ = (float)dirZ,
                Radius = radius,
                Strength = strength,
                Shock = shock,
                Shape = shape,
                Source = s,
                Surface = SurfaceAt(w, x, z),
                Actor = -1,
                Other = -1,
            };
            return f;
        }

        /// <summary>Decoded sim events and injected effects become forces (CombatFx forwards every FxEvent).</summary>
        public void OnFx(ref FxEvent e, World w)
        {
            WorldForce f;
            switch (e.Kind)
            {
                case FxKind.Hit:
                case FxKind.Block:
                case FxKind.Death:
                {
                    ForceSource s = e.Kind == FxKind.Block ? ForceSource.Block
                                  : e.B == Damage.KaijuSource ? ForceSource.KaijuMelee
                                  : e.Kind == FxKind.Death ? ForceSource.Death
                                  : e.Tier == 0 ? ForceSource.HitLight : e.Tier == 1 ? ForceSource.HitHeavy : ForceSource.HitCritical;
                    f = Make(w, s, ForceShape.Cone, e.X, e.Z, e.DirX, e.DirZ, 0f, ExplosionKind.Tnt);
                    f.Tier = e.Kind == FxKind.Death ? CombatFeed.TierOf(e.Value, false) : e.Tier;
                    break;
                }
                case FxKind.KaijuHit:
                    f = Make(w, ForceSource.HitLight, ForceShape.Point, e.X, e.Z, 0, 0, 0f, ExplosionKind.Tnt);
                    f.Tier = CombatFeed.TierOf(e.Value, false);
                    break;
                case FxKind.KaijuSlam:
                    f = Make(w, ForceSource.KaijuSlam, ForceShape.Radial, e.X, e.Z, 0, 0, (float)e.Value, ExplosionKind.KaijuSlam);
                    f.Tier = 2;
                    break;
                case FxKind.Explosion:
                {
                    int k = e.B;
                    if (k < 0 || k > (int)ExplosionKind.KaijuSlam) k = 0;
                    f = Make(w, ForceSource.Explosion, ForceShape.Radial, e.X, e.Z, 0, 0, (float)e.Value, (ExplosionKind)k);
                    f.Tier = 2;
                    break;
                }
                case FxKind.Splash:
                    f = Make(w, ForceSource.Splash, ForceShape.Radial, e.X, e.Z, 0, 0, (float)e.Value, ExplosionKind.Tnt);
                    break;
                case FxKind.Land:
                    f = Make(w, ForceSource.Land, ForceShape.Landing, e.X, e.Z, 0, 0, 0f, ExplosionKind.Tnt);
                    if (e.Value >= 1) f.Strength = Mathf.Min(1f, f.Strength * 1.5f); // heavy actor
                    break;
                case FxKind.WallCrack:
                    f = Make(w, ForceSource.WallCrack, ForceShape.Break, e.X, e.Z, 0, 0, 0f, ExplosionKind.WallBreak);
                    break;
                case FxKind.WallBreak:
                    f = Make(w, ForceSource.WallBreak, ForceShape.Break, e.X, e.Z, 0, 0, 0f, ExplosionKind.WallBreak);
                    break;
                case FxKind.BarricadeBroken:
                    f = Make(w, ForceSource.BarricadeBreak, ForceShape.Break, e.X, e.Z, 0, 0, 0f, ExplosionKind.WallBreak);
                    break;
                case FxKind.Hatch:
                    f = Make(w, ForceSource.Hatch, ForceShape.Radial, e.X, e.Z, 0, 0, 0f, ExplosionKind.KaijuSlam);
                    break;
                default:
                    return; // swings, pickups, deliveries, respawns and HUD-only events push nothing
            }
            f.Tick = e.Tick;
            f.Actor = e.A;
            f.Other = e.B;
            f.Team = e.Team;
            Push(f);
        }

        // ------------------------------------------------------------------ slots

        private double LifeOf(float strength) => (0.35 + 0.65 * Mathf.Clamp01(strength)) * SimPerWall;

        private static float Priority(in Slot s, double now)
        {
            if (!s.Used) return -1f;
            double age = now - s.Born;
            if (age < 0) age = 0;
            if (age >= s.Life) return -1f;
            return s.F.Strength * (float)(1.0 - age / s.Life);
        }

        private void Insert(in WorldForce f)
        {
            double born = f.Tick / _hz;
            int weakest = -1;
            float weakestP = float.MaxValue;
            for (int i = 0; i < SlotCount; i++)
            {
                float p = Priority(in _slots[i], born);
                if (p < weakestP)
                {
                    weakestP = p;
                    weakest = i;
                }
            }
            if (weakest < 0) return;
            if (weakestP >= 0f && weakestP >= f.Strength) return; // every live slot outranks it: listeners still saw it
            _slots[weakest].F = f;
            _slots[weakest].Born = born;
            _slots[weakest].Life = LifeOf(f.Strength);
            _slots[weakest].Used = true;
        }

        /// <summary>Slot i at sim time now: false when empty or expired; age01 0..1 of its life, ageWall in wall seconds.</summary>
        public bool TryGetSlot(int i, double now, out WorldForce f, out float age01, out float ageWall)
        {
            f = default;
            age01 = 1f;
            ageWall = 0f;
            if (i < 0 || i >= SlotCount || !_slots[i].Used) return false;
            double age = now - _slots[i].Born;
            if (age < 0) age = 0;
            if (age >= _slots[i].Life) return false;
            f = _slots[i].F;
            age01 = (float)(age / _slots[i].Life);
            ageWall = (float)(age / Math.Max(1e-6, SimPerWall));
            return true;
        }

        public int LiveCount(double now)
        {
            int n = 0;
            for (int i = 0; i < SlotCount; i++) if (Priority(in _slots[i], now) >= 0f) n++;
            return n;
        }

        /// <summary>True when a live slot holds a force from source s (tests).</summary>
        public bool SlotHolds(ForceSource s, double now)
        {
            for (int i = 0; i < SlotCount; i++)
                if (_slots[i].F.Source == s && Priority(in _slots[i], now) >= 0f) return true;
            return false;
        }

        /// <summary>
        /// The shader slots (contract C1): two float4 per live slot, (world x, world z, radius, strength x remaining life)
        /// and (world dir x, world dir z, age in wall s, shape), packed from index 0; the rest zeroed. Returns the count.
        /// </summary>
        public int CopySlots(double now, Vector4[] dst)
        {
            int n = 0;
            for (int i = 0; i < SlotCount && (n + 1) * 2 <= dst.Length; i++)
            {
                if (!TryGetSlot(i, now, out WorldForce f, out float a01, out float aw)) continue;
                dst[n * 2] = new Vector4(f.X, -f.Z, f.Radius, f.Strength * (1f - a01));
                dst[n * 2 + 1] = new Vector4(f.DirX, -f.DirZ, aw, (float)f.Shape);
                n++;
            }
            for (int i = n * 2; i < dst.Length; i++) dst[i] = Vector4.zero;
            return n;
        }

        /// <summary>Forget every slot and pending force (world reset).</summary>
        public void Clear()
        {
            for (int i = 0; i < SlotCount; i++) _slots[i].Used = false;
            _pendingCount = 0;
            _tick = int.MinValue;
        }

        // ------------------------------------------------------------------ surface

        /// <summary>The surface byte of sim (x, z): bridges wood, walls and breakables stone, water, void, else grass.</summary>
        public Surface SurfaceAt(World w, double x, double z)
        {
            MapDef map = w != null ? w.Map : null;
            if (map == null || map.Cells == null) return Surface.Grass;
            if (_surfaceMap != map || _surface == null || _surface.Length != map.Cells.Length)
            {
                _surfaceMap = map;
                if (_surface == null || _surface.Length != map.Cells.Length) _surface = new byte[map.Cells.Length];
                for (int i = 0; i < map.Cells.Length; i++) _surface[i] = (byte)SurfaceOf(map.Cells[i]);
            }
            int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
            if (cx < 0 || cz < 0 || cx >= map.Width || cz >= map.Height) return Surface.Void;
            int c = cz * map.Width + cx;
            byte cell = map.Cells[c];
            // Cells change with digs and breaks: re-derive this one (cheap) rather than trusting the cache.
            if ((Surface)_surface[c] != SurfaceOf(cell)) _surface[c] = (byte)SurfaceOf(cell);
            return (Surface)_surface[c];
        }

        public static Surface SurfaceOf(byte cell)
        {
            switch (cell)
            {
                case Cell.Bridge: return Surface.Wood;
                case Cell.Wall:
                case Cell.BreakableIntact:
                case Cell.BreakableCracked:
                case Cell.Barricade: return Surface.Stone;
                case Cell.BreakableDestroyed: return Surface.Dirt;
                case Cell.Water: return Surface.Water;
                case Cell.Void: return Surface.Void;
                default: return Surface.Grass;
            }
        }

        public static uint Hash(uint a, uint b, uint c)
        {
            uint h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u) * 0x85EBCA77u ^ (c + 0x165667B1u) * 0xC2B2AE3Du;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
            return h;
        }
    }
}
