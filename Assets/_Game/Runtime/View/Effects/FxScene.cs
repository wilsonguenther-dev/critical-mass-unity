using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>
    /// Every pooled combat effect (bursts, blood droplets and decals, sparks, rings, swing trails, debris chunks, puffs)
    /// plus the state-driven ones (Kaiju slam telegraph, berserk aura, Warrior shield arcs). Spawned from FxEvents with
    /// the event's tick as birth time, evaluated analytically from SIM time (tick + alpha) / SimHz, so a replay or a
    /// capture after the same ticks draws the same frame. Each pool is a fixed ring: a spawn into a full pool reuses the
    /// oldest slot, so a pool can never exceed its cap. Plain C# (EditMode-testable); CombatFx owns the meshes.
    /// Times below are authored in wall seconds at the default match speed and converted with Config.DefaultSpeed.
    /// </summary>
    public sealed class FxScene : IFxSink
    {
        // ---- hard budgets (32 fighters) ----
        public const int MaxBursts = 48, MaxDroplets = 160, MaxDecals = 192, MaxRings = 32, MaxTrails = 40, MaxChunks = 120, MaxPuffs = 96, MaxSparks = 96;

        // ---- palette (sRGB; three flat tone steps per material, Void Navy outline) ----
        public static readonly Color32 Navy = new Color32(24, 35, 59, 255);
        public static readonly Color32 BloodLight = new Color32(214, 52, 60, 255), BloodMid = new Color32(150, 18, 34, 255), BloodDark = new Color32(88, 8, 20, 255);
        public static readonly Color32 SparkWhite = new Color32(255, 251, 236, 255), SparkYellow = new Color32(255, 214, 72, 255), SparkOrange = new Color32(255, 150, 40, 255);
        public static readonly Color32 Steel = new Color32(196, 210, 226, 255);
        public static readonly Color32 DustLight = new Color32(247, 240, 229, 255), DustMid = new Color32(214, 202, 184, 255), DustDark = new Color32(158, 146, 132, 255);
        public static readonly Color32 RockLight = new Color32(156, 160, 176, 255), RockMid = new Color32(111, 114, 130, 255), RockDark = new Color32(68, 71, 88, 255);
        public static readonly Color32 WoodLight = new Color32(214, 160, 96, 255), WoodMid = new Color32(166, 110, 60, 255), WoodDark = new Color32(106, 66, 38, 255);
        public static readonly Color32 Warn = new Color32(255, 196, 58, 255), WarnHot = new Color32(255, 112, 36, 255);
        public static readonly Color32 BiomassC = new Color32(183, 242, 56, 255), CorruptionC = new Color32(142, 63, 209, 255);
        public static readonly Color32 Berserk = new Color32(238, 44, 192, 255);
        public static readonly Color32 AzureC = new Color32(22, 199, 217, 255), CrimsonC = new Color32(255, 90, 95, 255);

        private struct Burst { public double Born; public Vector3 P; public float Size; public byte Kind; public float Rot; }
        private struct Droplet { public double Born; public Vector3 P0, V; public float Size, TLand, GroundY; public bool Landed, Pool; public double DirX, DirZ; }
        private struct Decal { public double Born, Life; public Vector3 P; public float Size, Rot, Grow; public uint Seed; public byte Kind; public float DirA; }
        private struct Ring { public double Born, Life; public Vector3 P; public float R0, R1, Width; public Color32 Col; public byte Kind; }
        private struct Trail { public double Born; public Vector3 P; public float Facing, Arc, Radius; public Color32 Team; public byte Kind; }
        private struct Chunk { public double Born, Life; public Vector3 P0, V, Axis; public float Size, Spin, TLand, GroundY; public byte Mat; }
        private struct Puff { public double Born, Life; public Vector3 P0, V; public float Size; public Color32 Col; public byte Kind; }

        private readonly Burst[] _bursts = new Burst[MaxBursts];
        private readonly Droplet[] _drops = new Droplet[MaxDroplets];
        private readonly Decal[] _decals = new Decal[MaxDecals];
        private readonly Ring[] _rings = new Ring[MaxRings];
        private readonly Trail[] _trails = new Trail[MaxTrails];
        private readonly Chunk[] _chunks = new Chunk[MaxChunks];
        private readonly Puff[] _puffs = new Puff[MaxPuffs];
        private readonly Puff[] _sparks = new Puff[MaxSparks];
        private int _nb, _nd, _ndc, _nr, _nt, _nc, _np, _ns;

        private readonly float[] _radii = new float[12];
        private readonly GameConfig _cfg;
        private readonly double _speed;
        private readonly double _hz;
        private readonly MapDef _map;

        /// <summary>Blood on (the cm.blood setting): droplets and decals; off: neutral white-yellow sparks only.</summary>
        public bool Blood = true;

        public FxScene(GameConfig cfg, MapDef map)
        {
            _cfg = cfg;
            _map = map;
            _speed = cfg.DefaultSpeed > 0 ? cfg.DefaultSpeed : 1;
            _hz = cfg.SimHz;
            for (int i = 0; i < MaxBursts; i++) _bursts[i].Born = double.NegativeInfinity;
            for (int i = 0; i < MaxDroplets; i++) { _drops[i].Born = double.NegativeInfinity; _drops[i].Landed = true; }
            for (int i = 0; i < MaxDecals; i++) { _decals[i].Born = double.NegativeInfinity; _decals[i].Life = 0; }
            for (int i = 0; i < MaxRings; i++) _rings[i].Born = double.NegativeInfinity;
            for (int i = 0; i < MaxTrails; i++) _trails[i].Born = double.NegativeInfinity;
            for (int i = 0; i < MaxChunks; i++) _chunks[i].Born = double.NegativeInfinity;
            for (int i = 0; i < MaxPuffs; i++) _puffs[i].Born = double.NegativeInfinity;
            for (int i = 0; i < MaxSparks; i++) _sparks[i].Born = double.NegativeInfinity;
        }

        /// <summary>Sim seconds for a wall-clock duration at the default match speed.</summary>
        public double S(double wallSeconds) => wallSeconds * _speed;

        // Lifetimes (sim seconds).
        private double BurstLife => S(0.16);
        private double DecalLife => S(20.0);
        private double TrailLife => S(0.2);
        private double PuffLife => S(0.42);
        private double SparkLife => S(0.24);
        private const float G = 22f; // gravity, units per sim second squared

        // ---- deterministic noise (no System.Random: the same tick and actor always give the same shape) ----
        public static uint Hash(uint a, uint b, uint c)
        {
            uint h = 2166136261u;
            h = (h ^ a) * 16777619u; h ^= h >> 13;
            h = (h ^ b) * 16777619u; h ^= h >> 15;
            h = (h ^ c) * 16777619u; h ^= h >> 16;
            h *= 0x85EBCA6Bu; h ^= h >> 13;
            return h;
        }

        private static float R01(uint h, uint k) => (Hash(h, k, 0x9E37u) & 0xFFFFFF) / 16777216f;

        private double T(int tick) => tick / _hz;

        private static Vector3 W(double x, double y, double z) => SimSpace.ToWorld(x, y, z);

        private double Ground(double x, double z) => CriticalMass.Runtime.GroundHeight.At(_map, x, z);

        /// <summary>True when (x, z) is walkable ground (floor, bridge, rubble, barricade footing): decals land only there.</summary>
        public bool IsGround(double x, double z)
        {
            int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
            if (_map.Cells == null || cx < 0 || cz < 0 || cx >= _map.Width || cz >= _map.Height) return false;
            byte c = _map.Cells[cz * _map.Width + cx];
            return c == Cell.Floor || c == Cell.Bridge || c == Cell.BreakableDestroyed || c == Cell.Barricade;
        }

        // ---- counts (tests, budgets) ----
        public int ActiveCount(double now)
        {
            int n = 0;
            for (int i = 0; i < MaxBursts; i++) if (now - _bursts[i].Born < BurstLife) n++;
            for (int i = 0; i < MaxDroplets; i++) if (!_drops[i].Landed) n++;
            for (int i = 0; i < MaxDecals; i++) if (now - _decals[i].Born < _decals[i].Life) n++;
            for (int i = 0; i < MaxRings; i++) if (now - _rings[i].Born < _rings[i].Life) n++;
            for (int i = 0; i < MaxTrails; i++) if (now - _trails[i].Born < TrailLife) n++;
            for (int i = 0; i < MaxChunks; i++) if (now - _chunks[i].Born < _chunks[i].Life) n++;
            for (int i = 0; i < MaxPuffs; i++) if (now - _puffs[i].Born < _puffs[i].Life) n++;
            for (int i = 0; i < MaxSparks; i++) if (now - _sparks[i].Born < _sparks[i].Life) n++;
            return n;
        }

        public int DecalCount(double now)
        {
            int n = 0;
            for (int i = 0; i < MaxDecals; i++) if (now - _decals[i].Born < _decals[i].Life && _decals[i].Kind <= 1) n++;
            return n;
        }

        public int SparkCount(double now)
        {
            int n = 0;
            for (int i = 0; i < MaxSparks; i++) if (now - _sparks[i].Born < _sparks[i].Life) n++;
            return n;
        }

        public static int Capacity => MaxBursts + MaxDroplets + MaxDecals + MaxRings + MaxTrails + MaxChunks + MaxPuffs + MaxSparks;

        // ---- spawning from events ----

        public void OnFx(ref FxEvent e, World w)
        {
            double t = T(e.Tick);
            uint seed = Hash((uint)e.Tick, (uint)(e.A + 7), (uint)e.Kind);
            switch (e.Kind)
            {
                case FxKind.Hit:
                case FxKind.Death:
                {
                    double h = Ground(e.X, e.Z);
                    double r = _cfg.Class(e.Cls).Radius;
                    // Contact point: the victim's surface facing the source, at chest height.
                    Vector3 contact = W(e.X - e.DirX * r * 0.8, h + 0.95, e.Z - e.DirZ * r * 0.8);
                    float dmg01 = Mathf.Clamp01((float)(e.Value / 45.0));
                    AddBurst(t, contact, 0.34f + 0.62f * dmg01 + (e.Kind == FxKind.Death ? 0.25f : 0f), 0, seed);
                    int n = e.Kind == FxKind.Death ? 12 : 2 + (int)Math.Min(6, e.Value / 6);
                    if (Blood)
                    {
                        for (int k = 0; k < n; k++) AddDroplet(t, e, h, seed, (uint)k, e.Kind == FxKind.Death);
                        if (e.Kind == FxKind.Death && IsGround(e.X, e.Z))
                            AddDecal(t, W(e.X + e.DirX * 0.25, h, e.Z + e.DirZ * 0.25), 1.05f, 1, seed, e.DirX, e.DirZ, S(0.7));
                    }
                    else
                    {
                        for (int k = 0; k < n + 2; k++) AddSpark(t, contact, e.DirX, e.DirZ, seed, (uint)k, 1);
                    }
                    if (e.Kind == FxKind.Death)
                    {
                        // The corpse pops out with a puff when CombatFx stops showing it (CorpseSeconds after the death).
                        double pop = t + CorpseSeconds;
                        Vector3 p = W(e.X, h + 0.5, e.Z);
                        for (int k = 0; k < 7; k++) AddPuff(pop, p, seed, (uint)(k + 40), 0.42f, DustLight, 0);
                    }
                    break;
                }
                case FxKind.Block:
                {
                    double h = Ground(e.X, e.Z);
                    Actor a = w.Actors[e.A];
                    double fx = JsMath.Cos(a.Facing), fz = JsMath.Sin(a.Facing);
                    Vector3 p = W(e.X + fx * 0.7, h + 0.95, e.Z + fz * 0.7);
                    AddBurst(t, p, 0.5f, 2, seed);
                    for (int k = 0; k < 6; k++) AddSpark(t, p, -e.DirX, -e.DirZ, seed, (uint)k, 2);
                    break;
                }
                case FxKind.Swing:
                    AddTrail(t, e, w);
                    break;
                case FxKind.KaijuHit:
                {
                    double h = Ground(e.X, e.Z);
                    double kr = _cfg.Kaiju.Radius;
                    float a = R01(seed, 1) * Mathf.PI * 2f;
                    Vector3 p = W(e.X + Math.Cos(a) * kr * 0.8, h + 1.4 + R01(seed, 2) * 0.8, e.Z + Math.Sin(a) * kr * 0.8);
                    AddBurst(t, p, 0.45f + 0.3f * Mathf.Clamp01((float)(e.Value / 30.0)), 0, seed);
                    for (int k = 0; k < 3; k++) AddSpark(t, p, Math.Cos(a), Math.Sin(a), seed, (uint)k, 1);
                    break;
                }
                case FxKind.KaijuSlam:
                {
                    double h = Ground(e.X, e.Z);
                    float rad = (float)e.Value;
                    Vector3 c = W(e.X, h, e.Z);
                    AddRing(t, c + Vector3.up * 0.05f, 0.4f, rad * 1.15f, 0.55f, S(0.32), DustLight, 0);
                    AddRing(t + S(0.06), c + Vector3.up * 0.05f, 0.2f, rad * 0.8f, 0.3f, S(0.3), Warn, 0);
                    AddDecal(t, c, rad * 0.75f, 2, seed, 1, 0, S(0.08), S(7.0));
                    for (int k = 0; k < 14; k++)
                    {
                        float a = (k + R01(seed, (uint)k) * 0.6f) / 14f * Mathf.PI * 2f;
                        Vector3 p = c + new Vector3(Mathf.Cos(a), 0.2f, Mathf.Sin(a)) * rad * (0.75f + 0.3f * R01(seed, (uint)k + 90));
                        p.y = c.y + 0.25f;
                        AddPuff(t + S(0.03 * (k % 3)), p, seed, (uint)k, 0.7f, k % 2 == 0 ? DustLight : DustMid, 1);
                    }
                    for (int k = 0; k < 12; k++) AddChunk(t, c + Vector3.up * 0.2f, rad * 0.55f, seed, (uint)k, 0, 1.0f);
                    break;
                }
                case FxKind.KaijuRoar:
                case FxKind.Hatch:
                case FxKind.BerserkLock:
                {
                    double h = Ground(e.X, e.Z);
                    Vector3 c = W(e.X, h + 0.08, e.Z);
                    Color32 col = e.Kind == FxKind.BerserkLock ? Berserk : e.Kind == FxKind.Hatch ? DustLight : Warn;
                    float big = e.Kind == FxKind.KaijuRoar ? 1f : 1.6f;
                    AddRing(t, c, 1.2f, 4.5f * big, 0.22f, S(0.45), col, 1);
                    AddRing(t + S(0.12), c, 1.2f, 3.4f * big, 0.16f, S(0.4), col, 1);
                    if (e.Kind != FxKind.KaijuRoar) AddRing(t + S(0.24), c, 1.0f, 6.5f, 0.3f, S(0.6), col, 1);
                    break;
                }
                case FxKind.Respawn:
                {
                    double h = Ground(e.X, e.Z);
                    Vector3 c = W(e.X, h + 0.06, e.Z);
                    Color32 col = e.Team == TeamId.Azure ? AzureC : CrimsonC;
                    AddRing(t, c, 0.2f, 1.6f, 0.2f, S(0.5), col, 2);   // the beam (kind 2) and the ground ring
                    AddRing(t, c, 0.3f, 1.4f, 0.16f, S(0.5), col, 0);
                    for (int k = 0; k < 5; k++) AddPuff(t + S(0.05), c + Vector3.up * 0.3f, seed, (uint)k, 0.3f, DustLight, 0);
                    break;
                }
                case FxKind.Gather:
                case FxKind.DeliverBiomass:
                case FxKind.DeliverCorruption:
                {
                    double h = Ground(e.X, e.Z);
                    Color32 col = e.Kind == FxKind.DeliverCorruption ? CorruptionC : BiomassC;
                    bool deliver = e.Kind != FxKind.Gather;
                    double px = e.X, pz = e.Z;
                    if (deliver && e.A >= 0 && e.A < w.ActorCount) { px = w.X[e.A]; pz = w.Z[e.A]; }
                    Vector3 c = W(px, h + 0.9, pz);
                    AddRing(t, W(px, h + 0.06, pz), 0.2f, deliver ? 1.5f : 0.9f, 0.14f, S(0.35), col, 0);
                    AddBurst(t, c, deliver ? 0.55f : 0.35f, 3, seed);
                    for (int k = 0; k < (deliver ? 7 : 4); k++) AddSpark(t, c, 0, 0, seed, (uint)k, (byte)(deliver ? (e.Kind == FxKind.DeliverCorruption ? 4 : 3) : 3));
                    break;
                }
                case FxKind.BarricadePlaced:
                {
                    double h = Ground(e.X, e.Z);
                    Vector3 c = W(e.X, h + 0.15, e.Z);
                    for (int k = 0; k < 6; k++) AddPuff(t, c, seed, (uint)k, 0.35f, DustMid, 1);
                    break;
                }
                case FxKind.BarricadeBroken:
                case FxKind.WallCrack:
                case FxKind.WallBreak:
                {
                    double h = Ground(e.X, e.Z);
                    bool wood = e.Kind == FxKind.BarricadeBroken;
                    int n = e.Kind == FxKind.WallCrack ? 5 : 12;
                    Vector3 c = W(e.X, h + (wood ? 0.6 : 0.9), e.Z);
                    for (int k = 0; k < n; k++) AddChunk(t, c, 0.4f, seed, (uint)k, wood ? (byte)1 : (byte)0, e.Kind == FxKind.WallCrack ? 0.6f : 0.85f);
                    for (int k = 0; k < (n + 1) / 2; k++) AddPuff(t, c, seed, (uint)k + 20, 0.45f, DustMid, 1);
                    break;
                }
            }
        }

        /// <summary>How long a dead actor's body is shown (Death clip, a beat on the floor, then the pop), sim seconds.</summary>
        public double CorpseSeconds => S(1.35);
        public double PopSeconds => S(0.14);

        private void AddBurst(double t, Vector3 p, float size, byte kind, uint seed)
        {
            ref Burst b = ref _bursts[_nb++ % MaxBursts];
            b.Born = t; b.P = p; b.Size = size; b.Kind = kind; b.Rot = R01(seed, 77) * Mathf.PI;
        }

        private void AddDroplet(double t, FxEvent e, double h, uint seed, uint k, bool big)
        {
            ref Droplet d = ref _drops[_nd++ % MaxDroplets];
            float spread = (R01(seed, k * 3 + 1) - 0.5f) * 1.4f;
            double dx = e.DirX * Math.Cos(spread) - e.DirZ * Math.Sin(spread);
            double dz = e.DirX * Math.Sin(spread) + e.DirZ * Math.Cos(spread);
            float sp = (big ? 2.4f : 1.6f) + R01(seed, k * 3 + 2) * (big ? 3.2f : 2.6f);
            float vy = 3.0f + R01(seed, k * 3 + 3) * 3.4f;
            d.Born = t;
            d.P0 = W(e.X + e.DirX * 0.15, h + 0.95, e.Z + e.DirZ * 0.15);
            Vector3 dirW = W(dx, 0, dz);
            d.V = dirW * sp + Vector3.up * vy;
            d.Size = (big ? 0.12f : 0.08f) + R01(seed, k * 7 + 5) * 0.07f;
            d.GroundY = (float)h;
            float dy = d.P0.y - d.GroundY;
            d.TLand = (vy + Mathf.Sqrt(vy * vy + 2f * G * dy)) / G;
            d.Landed = false;
            d.Pool = big;
            d.DirX = dx;
            d.DirZ = dz;
        }

        private void AddDecal(double t, Vector3 p, float size, byte kind, uint seed, double dirX, double dirZ, double grow, double life = -1)
        {
            ref Decal d = ref _decals[_ndc++ % MaxDecals];
            d.Born = t; d.P = p; d.Size = size; d.Kind = kind; d.Seed = seed; d.Grow = (float)grow;
            d.Life = life > 0 ? life : DecalLife;
            d.Rot = R01(seed, 11) * Mathf.PI * 2f;
            d.DirA = Mathf.Atan2((float)-dirZ, (float)dirX); // basis angle in the ground plane (right = +X, up = +Z world)
        }

        private void AddRing(double t, Vector3 p, float r0, float r1, float width, double life, Color32 col, byte kind)
        {
            ref Ring r = ref _rings[_nr++ % MaxRings];
            r.Born = t; r.P = p; r.R0 = r0; r.R1 = r1; r.Width = width; r.Life = life; r.Col = col; r.Kind = kind;
        }

        private void AddTrail(double t, FxEvent e, World w)
        {
            ref Trail tr = ref _trails[_nt++ % MaxTrails];
            ClassConfig cc = _cfg.Class(e.Cls);
            double h = Ground(e.X, e.Z);
            tr.Born = t;
            tr.P = W(e.X, h, e.Z);
            tr.Facing = (float)Math.Atan2(e.DirZ, e.DirX);
            tr.Arc = (float)(cc.AttackArcDeg * Math.PI / 180);
            tr.Radius = (float)cc.AttackRange * 0.82f;
            tr.Team = e.Team == TeamId.Azure ? AzureC : CrimsonC;
            tr.Kind = e.Cls == ClassId.Miner ? (byte)1 : (byte)0;
        }

        private void AddChunk(double t, Vector3 p, float spread, uint seed, uint k, byte mat, float scale)
        {
            ref Chunk c = ref _chunks[_nc++ % MaxChunks];
            float a = R01(seed, k * 5 + 1) * Mathf.PI * 2f;
            float sp = 1.8f + R01(seed, k * 5 + 2) * 3.2f;
            c.Born = t;
            c.P0 = p + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * spread * R01(seed, k * 5 + 3);
            c.V = new Vector3(Mathf.Cos(a) * sp, 4.5f + R01(seed, k * 5 + 4) * 4.5f, Mathf.Sin(a) * sp);
            c.Axis = new Vector3(R01(seed, k + 60) - 0.5f, R01(seed, k + 61) - 0.5f, R01(seed, k + 62) - 0.5f).normalized;
            if (c.Axis.sqrMagnitude < 0.01f) c.Axis = Vector3.right;
            c.Spin = 8f + R01(seed, k + 63) * 10f;
            c.Size = (0.09f + R01(seed, k + 64) * 0.12f) * scale;
            c.GroundY = p.y - 0.85f;
            double gy = Ground(SimX(c.P0), SimZ(c.P0));
            c.GroundY = (float)gy;
            float dy = c.P0.y - c.GroundY - c.Size;
            c.TLand = (c.V.y + Mathf.Sqrt(c.V.y * c.V.y + 2f * G * Mathf.Max(0, dy))) / G;
            c.Life = S(1.6);
            c.Mat = mat;
        }

        private void AddPuff(double t, Vector3 p, uint seed, uint k, float size, Color32 col, byte kind)
        {
            ref Puff f = ref _puffs[_np++ % MaxPuffs];
            float a = R01(seed, k * 3 + 100) * Mathf.PI * 2f;
            float sp = (kind == 1 ? 1.4f : 0.8f) + R01(seed, k * 3 + 101) * 1.2f;
            f.Born = t;
            f.P0 = p;
            f.V = new Vector3(Mathf.Cos(a) * sp, 0.6f + R01(seed, k * 3 + 102) * 0.9f, Mathf.Sin(a) * sp);
            f.Size = size * (0.7f + 0.6f * R01(seed, k * 3 + 103));
            f.Col = col;
            f.Kind = kind;
            f.Life = PuffLife * (0.8 + 0.4 * R01(seed, k + 104));
        }

        /// <summary>kind 1 white-yellow hit spark, 2 steel block spark, 3 biomass mote, 4 corruption mote.</summary>
        private void AddSpark(double t, Vector3 p, double dirX, double dirZ, uint seed, uint k, byte kind)
        {
            ref Puff s = ref _sparks[_ns++ % MaxSparks];
            float a = R01(seed, k * 3 + 200) * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            if (dirX != 0 || dirZ != 0) dir = (W(dirX, 0, dirZ) * 1.3f + dir * 0.8f).normalized;
            float sp = 3f + R01(seed, k * 3 + 201) * 4f;
            if (kind >= 3) sp *= 0.4f;
            s.Born = t;
            s.P0 = p;
            s.V = dir * sp + Vector3.up * (kind >= 3 ? 3.2f : 1.5f + R01(seed, k + 202) * 2.5f);
            s.Size = (kind >= 3 ? 0.12f : 0.09f) + R01(seed, k + 203) * 0.06f;
            s.Col = kind == 2 ? Steel : kind == 3 ? BiomassC : kind == 4 ? CorruptionC : SparkYellow;
            s.Kind = kind;
            s.Life = SparkLife * (kind >= 3 ? 2.2 : 1.0);
        }

        private static double SimX(Vector3 p) => p.x;
        private static double SimZ(Vector3 p) => -p.z;

        // ---- per-frame update and mesh build ----

        /// <summary>Land droplets whose flight ended by `now` (decal birth = the exact landing time).</summary>
        public void Advance(double now)
        {
            for (int i = 0; i < MaxDroplets; i++)
            {
                ref Droplet d = ref _drops[i];
                if (d.Landed || now < d.Born + d.TLand) continue;
                d.Landed = true;
                Vector3 p = d.P0 + d.V * d.TLand;
                double sx = p.x, sz = -p.z;
                if (!IsGround(sx, sz)) continue;
                p.y = (float)Ground(sx, sz);
                uint seed = Hash((uint)i, (uint)(d.Born * 1000), 3u);
                AddDecal(d.Born + d.TLand, p, d.Size * (d.Pool ? 3.6f : 3.0f), 0, seed, d.DirX, d.DirZ, S(0.05));
            }
        }

        private static Color32 A(Color32 c, float a)
        {
            c.a = (byte)Mathf.Clamp(a * 255f, 0, 255);
            return c;
        }

        /// <summary>Rebuild the three layer meshes: ground (decals, rings, telegraph), air (billboards, trails), solid (chunks).</summary>
        public void Build(double now, World w, Vector3 camRight, Vector3 camUp, FxMeshBuilder ground, FxMeshBuilder air, FxMeshBuilder solid)
        {
            ground.Clear();
            air.Clear();
            solid.Clear();
            Vector3 gr = Vector3.right, gu = Vector3.forward; // ground plane basis (world)
            const float lift = 0.03f;

            // Decals: outlined splats (kind 0), pools (1), slam cracks (2).
            for (int i = 0; i < MaxDecals; i++)
            {
                ref Decal d = ref _decals[i];
                double age = now - d.Born;
                if (age < 0 || age >= d.Life) continue;
                float grow = d.Grow > 0 ? Mathf.Clamp01((float)(age / d.Grow)) : 1f;
                grow = 1f - (1f - grow) * (1f - grow);
                float fade = (float)Math.Min(1.0, (d.Life - age) / (d.Life * 0.3));
                Vector3 c = d.P + Vector3.up * lift;
                if (d.Kind == 2) { DrawCrack(ground, c, d.Size * grow, d.Rot, d.Seed, fade); continue; }
                float s = d.Size * grow * (d.Kind == 1 ? 1f : 1f);
                int n = d.Kind == 1 ? 12 : 9;
                for (int k = 0; k < n; k++) _radii[k] = 0.72f + 0.5f * R01(d.Seed, (uint)k);
                // Stretch along the fly direction: offset the blob centre forward a little and add satellites.
                ground.Blob(c, gr, gu, s * 1.16f + 0.03f, _radii, n, d.Rot, A(Navy, fade * 0.9f));
                ground.Blob(c, gr, gu, s, _radii, n, d.Rot, A(BloodMid, fade));
                Vector3 off = (gr * Mathf.Cos(d.DirA + Mathf.PI) + gu * Mathf.Sin(d.DirA + Mathf.PI)) * s * 0.18f;
                ground.Blob(c + off, gr, gu, s * 0.58f, _radii, n, d.Rot + 0.9f, A(BloodDark, fade));
                ground.Blob(c - off * 1.6f + (gr * 0.3f + gu * 0.25f) * s * 0.4f, gr, gu, s * 0.2f, null, 6, 0, A(BloodLight, fade));
                int sats = d.Kind == 1 ? 4 : 2;
                for (int k = 0; k < sats; k++)
                {
                    float a = d.DirA + (R01(d.Seed, (uint)k + 30) - 0.5f) * 1.1f;
                    float dist = s * (1.3f + 0.7f * R01(d.Seed, (uint)k + 31));
                    float sr = s * (0.16f + 0.14f * R01(d.Seed, (uint)k + 32));
                    Vector3 p = c + (gr * Mathf.Cos(a) + gu * Mathf.Sin(a)) * dist;
                    ground.Blob(p, gr, gu, sr + 0.025f, null, 7, 0, A(Navy, fade * 0.9f));
                    ground.Blob(p, gr, gu, sr, null, 7, 0, A(BloodMid, fade));
                }
            }

            // Expanding rings (0 ground ring, 1 roar double ring, 2 respawn beam).
            for (int i = 0; i < MaxRings; i++)
            {
                ref Ring r = ref _rings[i];
                double age = now - r.Born;
                if (age < 0 || age >= r.Life) continue;
                float t = (float)(age / r.Life);
                float e = 1f - (1f - t) * (1f - t) * (1f - t);
                if (r.Kind == 2)
                {
                    // Respawn beam: a tall camera-facing column in team colour with a white core, shrinking to a line.
                    Vector3 side = Vector3.ProjectOnPlane(camRight, Vector3.up).normalized;
                    float wdt = r.R1 * 0.32f * (1f - t);
                    float hgt = 4.2f;
                    Vector3 mid = r.P + Vector3.up * (hgt * 0.5f);
                    air.Quad(mid, side * (wdt + 0.06f), Vector3.up * (hgt * 0.5f), A(Navy, (1 - t) * 0.8f), A(Navy, 0));
                    air.Quad(mid, side * wdt, Vector3.up * (hgt * 0.5f), A(r.Col, 1 - t), A(r.Col, 0));
                    air.Quad(mid, side * wdt * 0.4f, Vector3.up * (hgt * 0.5f), A(SparkWhite, 1 - t), A(SparkWhite, 0));
                    continue;
                }
                float rad = Mathf.Lerp(r.R0, r.R1, e);
                float wd = r.Width * (1f - 0.6f * t);
                float fade = 1f - t * t;
                Vector3 c = r.P + Vector3.up * lift;
                int seg = Mathf.Clamp((int)(rad * 10f), 20, 64);
                ground.Ring(c, gr, gu, rad - wd * 0.5f - 0.05f, rad + wd * 0.5f + 0.05f, 0, Mathf.PI * 2f, seg, A(Navy, fade * 0.85f));
                ground.Ring(c, gr, gu, rad - wd * 0.5f, rad + wd * 0.5f, 0, Mathf.PI * 2f, seg, A(r.Col, fade));
                if (r.Kind == 0) ground.Ring(c, gr, gu, rad - wd * 0.5f, rad - wd * 0.1f, 0, Mathf.PI * 2f, seg, A(SparkWhite, fade * 0.8f));
            }

            DrawKaijuState(w, ground, air, now);
            DrawShields(w, air, now);

            // Swing trails: Warrior cleave = horizontal crescent across the attack arc; Miner pick = vertical chop arc.
            for (int i = 0; i < MaxTrails; i++)
            {
                ref Trail tr = ref _trails[i];
                double age = now - tr.Born;
                if (age < 0 || age >= TrailLife) continue;
                float t = (float)(age / TrailLife);
                float sweep = Mathf.Clamp01(t / 0.55f);
                sweep = 1f - (1f - sweep) * (1f - sweep);
                float fade = t < 0.55f ? 1f : 1f - (t - 0.55f) / 0.45f;
                if (tr.Kind == 0)
                {
                    float centre = -tr.Facing; // sim angle -> ground basis angle
                    float a0 = centre + tr.Arc * 0.5f, a1 = centre - tr.Arc * 0.5f;
                    float head = Mathf.Lerp(a0, a1, sweep);
                    float tail = Mathf.Lerp(a0, head, Mathf.Clamp01(t * 1.4f));
                    Vector3 c = tr.P + Vector3.up * 0.85f;
                    air.Crescent(c, gr, gu, tr.Radius, 0.26f, tail, head, 14, A(Navy, 0), A(Navy, fade * 0.95f));
                    air.Crescent(c, gr, gu, tr.Radius, 0.2f, tail, head, 14, A(tr.Team, 0), A(tr.Team, fade * 0.85f));
                    air.Crescent(c, gr, gu, tr.Radius + 0.02f, 0.1f, tail, head, 14, A(SparkWhite, 0), A(SparkWhite, fade));
                }
                else
                {
                    Vector3 fwd = W(Math.Cos(tr.Facing), 0, Math.Sin(tr.Facing)).normalized;
                    Vector3 c = tr.P + Vector3.up * 0.75f;
                    float a0 = 1.9f, a1 = -0.35f; // overhead behind -> low in front
                    float head = Mathf.Lerp(a0, a1, sweep);
                    float tail = Mathf.Lerp(a0, head, Mathf.Clamp01(t * 1.4f));
                    air.Crescent(c, fwd, Vector3.up, 0.95f, 0.2f, tail, head, 12, A(Navy, 0), A(Navy, fade * 0.95f));
                    air.Crescent(c, fwd, Vector3.up, 0.95f, 0.15f, tail, head, 12, A(SparkYellow, 0), A(SparkYellow, fade * 0.9f));
                    air.Crescent(c, fwd, Vector3.up, 0.97f, 0.07f, tail, head, 12, A(SparkWhite, 0), A(SparkWhite, fade));
                }
            }

            // Impact bursts: navy star, colour star, white core; pop then shrink.
            for (int i = 0; i < MaxBursts; i++)
            {
                ref Burst b = ref _bursts[i];
                double age = now - b.Born;
                if (age < 0 || age >= BurstLife) continue;
                float t = (float)(age / BurstLife);
                float s = b.Size * (t < 0.25f ? Mathf.Lerp(0.55f, 1.15f, t / 0.25f) : Mathf.Lerp(1.15f, 0.35f, (t - 0.25f) / 0.75f));
                float fade = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
                Color32 mid = b.Kind == 2 ? Steel : b.Kind == 3 ? BiomassC : SparkYellow;
                Color32 core = SparkWhite;
                int pts = b.Kind == 2 ? 6 : 8;
                Vector3 p = b.P - (camRight * 0 + Vector3.Cross(camRight, camUp)) * 0.3f; // nudge toward the camera
                air.Star(p, camRight, camUp, s * 1.22f + 0.04f, 0.42f, pts, b.Rot, A(Navy, fade));
                air.Star(p, camRight, camUp, s, 0.42f, pts, b.Rot, A(mid, fade));
                air.Star(p, camRight, camUp, s * 0.62f, 0.5f, pts, b.Rot + 0.2f, A(core, fade));
                if (b.Kind == 0) air.Blob(p, camRight, camUp, s * 0.24f, null, 8, 0, A(SparkOrange, fade));
            }

            // Blood droplets in flight (billboard blobs with navy rims).
            for (int i = 0; i < MaxDroplets; i++)
            {
                ref Droplet d = ref _drops[i];
                if (d.Landed) continue;
                float age = (float)(now - d.Born);
                if (age < 0) continue;
                Vector3 p = d.P0 + d.V * age + Vector3.down * (0.5f * G * age * age);
                air.Blob(p, camRight, camUp, d.Size + 0.03f, null, 7, 0, Navy);
                air.Blob(p, camRight, camUp, d.Size, null, 7, 0, BloodMid);
                air.Blob(p + (camUp - camRight) * d.Size * 0.3f, camRight, camUp, d.Size * 0.38f, null, 5, 0, BloodLight);
            }

            // Sparks (diamonds) and motes.
            for (int i = 0; i < MaxSparks; i++)
            {
                ref Puff s = ref _sparks[i];
                double age = now - s.Born;
                if (age < 0 || age >= s.Life) continue;
                float t = (float)(age / s.Life);
                float a = (float)age;
                Vector3 p = s.P0 + s.V * a + Vector3.down * (0.5f * (s.Kind >= 3 ? -2f : G * 0.6f) * a * a);
                float sz = s.Size * (1f - t * 0.7f);
                float fade = 1f - t * t;
                air.Star(p, camRight, camUp, sz + 0.035f, 0.45f, 4, 0.785f, A(Navy, fade));
                air.Star(p, camRight, camUp, sz, 0.45f, 4, 0.785f, A(s.Col, fade));
                if (s.Kind <= 2) air.Star(p, camRight, camUp, sz * 0.5f, 0.5f, 4, 0.785f, A(SparkWhite, fade));
            }

            // Dust puffs: chunky outlined cloud blobs, rise and fade.
            for (int i = 0; i < MaxPuffs; i++)
            {
                ref Puff f = ref _puffs[i];
                double age = now - f.Born;
                if (age < 0 || age >= f.Life) continue;
                float t = (float)(age / f.Life);
                float drag = 1f - Mathf.Exp(-4f * (float)age);
                Vector3 p = f.P0 + f.V * (drag / 4f) * 1.6f;
                float sz = f.Size * (0.5f + 0.8f * Mathf.Sqrt(t)) * (1f - t * 0.35f);
                float fade = 1f - t * t;
                air.Blob(p, camRight, camUp, sz + 0.05f, null, 9, 0, A(Navy, fade * 0.85f));
                air.Blob(p, camRight, camUp, sz, null, 9, 0, A(f.Col, fade));
                air.Blob(p + (camUp - camRight) * sz * 0.25f, camRight, camUp, sz * 0.55f, null, 8, 0, A(DustLight, fade));
                air.Blob(p - (camUp - camRight * 0.4f) * sz * 0.3f, camRight, camUp, sz * 0.45f, null, 8, 0, A(DustDark, fade * 0.8f));
            }

            // Debris chunks: toon cubes with an inverted navy hull; bounce once onto the ground, then shrink out.
            for (int i = 0; i < MaxChunks; i++)
            {
                ref Chunk c = ref _chunks[i];
                double age = now - c.Born;
                if (age < 0 || age >= c.Life) continue;
                float a = (float)Math.Min(age, c.TLand);
                Vector3 p = c.P0 + c.V * a + Vector3.down * (0.5f * G * a * a);
                if (age > c.TLand)
                {
                    float sl = (float)(age - c.TLand);
                    p += new Vector3(c.V.x, 0, c.V.z) * (0.18f * (1f - Mathf.Exp(-6f * sl)));
                    p.y = c.GroundY + c.Size;
                }
                float tail = (float)((c.Life - age) / (c.Life * 0.25));
                float sz = c.Size * Mathf.Clamp01(tail);
                Quaternion rot = Quaternion.AngleAxis(Mathf.Rad2Deg * c.Spin * a, c.Axis);
                Color32 l = c.Mat == 1 ? WoodLight : RockLight, m = c.Mat == 1 ? WoodMid : RockMid, dk = c.Mat == 1 ? WoodDark : RockDark;
                solid.Chunk(p, rot, new Vector3(sz, sz * 0.8f, sz * 1.1f), 0.035f, l, m, dk, Navy);
            }
        }

        private void DrawCrack(FxMeshBuilder g, Vector3 c, float size, float rot, uint seed, float fade)
        {
            Vector3 gr = Vector3.right, gu = Vector3.forward;
            // Scorched dust disc, then 9 jagged navy cracks with a pale lip.
            for (int k = 0; k < 10; k++) _radii[k] = 0.85f + 0.3f * R01(seed, (uint)k + 300);
            g.Blob(c, gr, gu, size * 0.9f, _radii, 10, rot, A(DustDark, fade * 0.55f));
            g.Blob(c, gr, gu, size * 0.38f, _radii, 10, rot + 1, A(RockDark, fade * 0.8f));
            for (int k = 0; k < 9; k++)
            {
                float a = rot + k * Mathf.PI * 2f / 9f + (R01(seed, (uint)k + 310) - 0.5f) * 0.4f;
                float len = size * (0.65f + 0.5f * R01(seed, (uint)k + 320));
                Vector3 dir = gr * Mathf.Cos(a) + gu * Mathf.Sin(a);
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                Vector3 mid = c + dir * len * 0.5f + side * (R01(seed, (uint)k + 330) - 0.5f) * size * 0.2f;
                CrackSeg(g, c + dir * size * 0.2f, mid, 0.12f, fade);
                CrackSeg(g, mid, c + dir * len, 0.07f, fade);
            }
        }

        private void CrackSeg(FxMeshBuilder g, Vector3 a, Vector3 b, float w, float fade)
        {
            Vector3 d = b - a;
            Vector3 side = Vector3.Cross(Vector3.up, d.normalized) * w;
            Vector3 c = (a + b) * 0.5f;
            g.Quad(c, d * 0.5f, side * 1.6f, A(DustLight, fade * 0.7f), A(DustLight, fade * 0.7f));
            g.Quad(c + Vector3.up * 0.002f, d * 0.5f, side, A(Navy, fade), A(Navy, fade));
        }

        private void DrawKaijuState(World w, FxMeshBuilder ground, FxMeshBuilder air, double now)
        {
            Kaiju k = w.Kaiju;
            if (k.State == KaijuState.Egg) return;
            Vector3 gr = Vector3.right, gu = Vector3.forward;
            // Slam telegraph: the full ring appears at once at the real slam radius (the shape is the hitbox), the inner
            // disc fills as the wind-up runs out.
            if (k.TelegraphIn > 0)
            {
                float R = (float)_cfg.Kaiju.SlamRadius;
                float p = 1f - (float)(k.TelegraphIn / _cfg.Kaiju.SlamTelegraphSeconds);
                double h = Ground(k.TelegraphX, k.TelegraphZ);
                Vector3 c = W(k.TelegraphX, h + 0.04, k.TelegraphZ);
                float pulse = 0.75f + 0.25f * Mathf.Sin((float)now * 18f);
                ground.Blob(c, gr, gu, R, null, 48, 0, A(WarnHot, 0.14f));
                ground.Blob(c, gr, gu, Mathf.Max(0.05f, R * p), null, 48, 0, A(Warn, 0.32f + 0.2f * p));
                ground.Ring(c, gr, gu, R - 0.2f, R + 0.08f, 0, Mathf.PI * 2f, 64, A(Navy, 0.9f));
                ground.Ring(c, gr, gu, R - 0.14f, R + 0.02f, 0, Mathf.PI * 2f, 64, A(WarnHot, pulse));
                for (int s = 0; s < 8; s++)
                {
                    float a = s * Mathf.PI / 4f + (float)now * 0.6f;
                    ground.Ring(c, gr, gu, R * p - 0.12f, R * p + 0.02f, a, a + 0.42f, 6, A(Navy, 0.6f));
                }
            }
            if (k.State == KaijuState.Berserk)
            {
                double h = Ground(k.X, k.Z);
                Vector3 c = W(k.X, h + 0.05, k.Z);
                float R = (float)_cfg.Kaiju.Radius * 1.7f;
                float pulse = 0.5f + 0.5f * Mathf.Sin((float)now * 6f);
                ground.Star(c, gr, gu, R * (1.05f + 0.1f * pulse) + 0.1f, 0.62f, 10, (float)now * 0.8f, A(Navy, 0.75f));
                ground.Star(c, gr, gu, R * (1.05f + 0.1f * pulse), 0.62f, 10, (float)now * 0.8f, A(Berserk, 0.55f + 0.3f * pulse));
                ground.Ring(c, gr, gu, R * 0.55f, R * 0.62f, 0, Mathf.PI * 2f, 40, A(SparkWhite, 0.5f * pulse));
                // Rising embers around the body.
                for (int s = 0; s < 6; s++)
                {
                    float ph = (float)((now * 0.9 + s * 0.17) % 1.0);
                    float a = s * 1.047f + (float)now * 0.5f;
                    Vector3 p = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * R * 0.75f + Vector3.up * (0.4f + ph * 3.2f);
                    air.Star(p, Vector3.right, Vector3.up, 0.16f * (1 - ph) + 0.04f, 0.45f, 4, 0.785f, A(Navy, 1 - ph));
                    air.Star(p, Vector3.right, Vector3.up, 0.12f * (1 - ph), 0.45f, 4, 0.785f, A(Berserk, 1 - ph));
                }
            }
        }

        private void DrawShields(World w, FxMeshBuilder air, double now)
        {
            double half = Damage.ShieldHalfArcDeg * Math.PI / 180;
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive || a.Cls != ClassId.Warrior || !(a.AbilityActive > 0)) continue;
                double h = Ground(w.X[i], w.Z[i]);
                Vector3 c = W(w.X[i], h + 0.9, w.Z[i]);
                float centre = (float)-a.Facing;
                Color32 team = a.Team == TeamId.Azure ? AzureC : CrimsonC;
                float pulse = 0.75f + 0.25f * Mathf.Sin((float)now * 9f + i);
                air.Ring(c, Vector3.right, Vector3.forward, 0.66f, 0.86f, centre - (float)half, centre + (float)half, 10, A(Navy, 0.85f));
                air.Ring(c, Vector3.right, Vector3.forward, 0.69f, 0.83f, centre - (float)half + 0.03f, centre + (float)half - 0.03f, 10, A(team, 0.75f * pulse));
                air.Ring(c + Vector3.up * 0.01f, Vector3.right, Vector3.forward, 0.79f, 0.83f, centre - (float)half + 0.05f, centre + (float)half - 0.05f, 10, A(SparkWhite, 0.8f * pulse));
            }
        }
    }
}
