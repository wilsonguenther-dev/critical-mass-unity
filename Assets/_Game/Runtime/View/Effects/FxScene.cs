using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>
    /// Every pooled combat effect plus the state-driven ones (Kaiju slam telegraph, berserk aura, Warrior shield arcs).
    /// Spawned from FxEvents with the event's tick as birth time, evaluated analytically from SIM time (tick + alpha) / SimHz,
    /// so a replay or a capture after the same ticks draws the same frame. Each pool is a fixed ring: a spawn into a full
    /// pool reuses the oldest slot, so a pool can never exceed its cap. Plain C# (EditMode-testable); CombatFx owns the meshes.
    /// Times below are authored in wall seconds at the default match speed and converted with Config.DefaultSpeed.
    /// <para>v2 (this file: pools, palette, dispatch, hit tiers, trails, droplets, sparks, puffs, rings, shields, Kaiju
    /// state). FxScene.Gore.cs: splat shapes, KO pools, drips, gibs, wall-top splats. FxScene.Boom.cs: explosions, light
    /// pulse, smoke column, scorch, screen trauma. FxScene.World.cs: footsteps, plank dust, foliage rustle, splashes,
    /// biomass motes, corruption bubbles and puddles.</para>
    /// <para>Every element's worst-case vertex cost is a constant next to the code that draws it (Cost*), and
    /// WorstCaseVerts sums pool caps times costs per layer; a test holds each layer's worst case under its fixed buffer.</para>
    /// </summary>
    public sealed partial class FxScene : IFxSink
    {
        // ---- hard budgets (32 fighters); v1 values in brackets, reasons in the PR budget table ----
        public const int MaxBursts = 64;    // [48] three hit tiers + Kaiju claws share the pool
        public const int MaxDroplets = 192; // [160] directional spray adds 2-3 mist drops on heavy hits
        public const int MaxDecals = 224;   // [192] blood only now (scorch, cracks, corruption moved to Marks); wall tops too
        public const int MaxRings = 48;     // [32] shock rings, block ripples, splash rings, Kaiju stomps
        public const int MaxTrails = 48;    // [40] five classes have a trail now (v1: two shapes)
        public const int MaxChunks = 144;   // [120] explosions throw 6 to 14 chunks each
        public const int MaxPuffs = 128;    // [96] footstep puffs, plank dust, knock-back dust, splash foam
        public const int MaxSparks = 128;   // [96] embers, water drops, grit
        public const int MaxMarks = 24;     // new: slam cracks, scorch decals, corruption puddles (long-lived, few)
        public const int MaxDrips = 96;     // new: blood trail drips behind badly hurt actors
        public const int MaxGibs = 24;      // new: cartoon meat chunks and bones (a few per stomp or blast knock-out)
        public const int MaxExplosions = 8; // new: fireball, lobes, smoke column, light pulse (analytic per explosion)
        public const int MaxMotes = 48;     // new: biomass sparkles homing into the carrier
        public const int MaxBubbles = 48;   // new: corruption bubbles
        public const int MaxLeaves = 64;    // new: grass and bush rustle flecks

        // ---- palette (sRGB; three flat tone steps per material, Void Navy outline) ----
        public static readonly Color32 Navy = new Color32(24, 35, 59, 255);
        public static readonly Color32 BloodLight = new Color32(214, 52, 60, 255), BloodMid = new Color32(150, 18, 34, 255), BloodDark = new Color32(88, 8, 20, 255);
        // World around arena 1 (2026-11-13): marks left ON THE GROUND by hits and knock-outs (splats, drips, the stamped
        // paint) are a neutral dark ink, not red. Red ground paint read as "Crimson team" under every fight, which breaks
        // the bible's reserved-hue rule (art direction 2026-11-12: ground decals for hits and knock-outs are neutral marks
        // in Void Navy, never red, never Azure or Crimson). Low chroma (under 0.03 OKLab), hue of Void Navy, drawn at
        // GroundInkAlpha so the ground shows through and the marks stay quieter than the fighters.
        public static readonly Color32 GroundInkLight = new Color32(104, 104, 118, 255), GroundInkMid = new Color32(64, 66, 82, 255), GroundInkDark = new Color32(38, 42, 58, 255);
        public const float GroundInkAlpha = 0.72f;
        public static readonly Color32 SparkWhite = new Color32(255, 251, 236, 255), SparkYellow = new Color32(255, 214, 72, 255), SparkOrange = new Color32(255, 150, 40, 255);
        public static readonly Color32 Steel = new Color32(196, 210, 226, 255);
        public static readonly Color32 DustLight = new Color32(247, 240, 229, 255), DustMid = new Color32(214, 202, 184, 255), DustDark = new Color32(158, 146, 132, 255);
        public static readonly Color32 RockLight = new Color32(156, 160, 176, 255), RockMid = new Color32(111, 114, 130, 255), RockDark = new Color32(68, 71, 88, 255);
        public static readonly Color32 WoodLight = new Color32(214, 160, 96, 255), WoodMid = new Color32(166, 110, 60, 255), WoodDark = new Color32(106, 66, 38, 255);
        public static readonly Color32 Warn = new Color32(255, 196, 58, 255), WarnHot = new Color32(255, 112, 36, 255);
        public static readonly Color32 BiomassC = new Color32(183, 242, 56, 255), CorruptionC = new Color32(142, 63, 209, 255);
        public static readonly Color32 Berserk = new Color32(238, 44, 192, 255);
        public static readonly Color32 AzureC = new Color32(22, 199, 217, 255), CrimsonC = new Color32(255, 90, 95, 255);
        // v2
        public static readonly Color32 FireYellow = new Color32(255, 232, 112, 255), FireOrange = new Color32(255, 146, 38, 255), FireRed = new Color32(232, 64, 36, 255);
        public static readonly Color32 Glow = new Color32(255, 176, 92, 255);
        public static readonly Color32 SmokeLight = new Color32(196, 192, 200, 255), SmokeMid = new Color32(146, 142, 152, 255), SmokeDark = new Color32(100, 96, 108, 255);
        public static readonly Color32 ScorchOuter = new Color32(74, 64, 62, 255), ScorchInner = new Color32(38, 32, 34, 255);
        public static readonly Color32 DirtLight = new Color32(176, 136, 96, 255), DirtMid = new Color32(132, 96, 64, 255), DirtDark = new Color32(86, 60, 40, 255);
        public static readonly Color32 MeatLight = new Color32(240, 120, 128, 255), MeatMid = new Color32(196, 60, 76, 255), MeatDark = new Color32(124, 28, 44, 255);
        public static readonly Color32 BoneLight = new Color32(252, 244, 220, 255), BoneMid = new Color32(226, 210, 176, 255), BoneDark = new Color32(172, 152, 120, 255);
        public static readonly Color32 CorruptDark = new Color32(70, 22, 110, 255), CorruptLight = new Color32(196, 128, 246, 255), OilSheen = new Color32(92, 232, 212, 255);
        public static readonly Color32 WaterLight = new Color32(226, 246, 255, 255), WaterMid = new Color32(120, 200, 238, 255);
        public static readonly Color32 HarvestGold = new Color32(255, 214, 110, 255);

        private struct Burst { public double Born, Life; public Vector3 P, Tan; public float Size; public byte Kind; public float Rot; }
        private struct Droplet { public double Born; public Vector3 P0, V; public float Size, TLand, GroundY; public bool Landed, Pool; public double DirX, DirZ; }
        private struct Decal { public double Born, Life; public Vector3 P; public float Size, Rot, Grow; public uint Seed; public byte Kind, Shape; public float DirA; }
        private struct Ring { public double Born, Life; public Vector3 P; public float R0, R1, Width, Facing; public Color32 Col; public byte Kind; }
        private struct Trail { public double Born; public Vector3 P; public float Facing, Arc, Radius; public Color32 Team; public byte Kind; }
        private struct Puff { public double Born, Life; public Vector3 P0, V; public float Size; public Color32 Col; public byte Kind; }

        private readonly Burst[] _bursts = new Burst[MaxBursts];
        private readonly Droplet[] _drops = new Droplet[MaxDroplets];
        private readonly Decal[] _decals = new Decal[MaxDecals];
        private readonly Ring[] _rings = new Ring[MaxRings];
        private readonly Trail[] _trails = new Trail[MaxTrails];
        private readonly Puff[] _puffs = new Puff[MaxPuffs];
        private readonly Puff[] _sparks = new Puff[MaxSparks];
        private int _nb, _nd, _ndc, _nr, _nt, _np, _ns;

        private readonly float[] _radii = new float[16];
        private readonly GameConfig _cfg;
        private readonly double _speed;
        private readonly double _hz;
        private readonly MapDef _map;
        private readonly int _cap;

        /// <summary>Blood on (the cm.blood setting): droplets, splats, pools, drips, gibs; off: neutral white-yellow sparks only.</summary>
        public bool Blood = true;

        /// <summary>The driver's interpolation alpha for this build (CombatFx sets it): blob shadows follow the drawn bodies.</summary>
        public double Alpha;
        /// <summary>The match pace in force (sim seconds per wall second; CombatFx sets it from the driver): pulses are in wall Hz.</summary>
        public double Pace;
        /// <summary>Physics audit B1/M8: flat ground blobs under fighters and the Kaiju where the quality level has no shadow map.</summary>
        public bool BlobShadows;

        /// <summary>Blob shadow radius as a multiple of the body radius, and its opacity.</summary>
        public const float BlobScale = 1.35f, BlobAlpha = 0.34f;
        /// <summary>Slam telegraph rim pulse in wall Hz (was 18 rad per sim second: 5.7 Hz on screen at pace 2).</summary>
        public const float SlamPulseHz = 2f;
        public const float BerserkPulseHz = 1.5f, ShieldPulseHz = 2f, MeleePulseHz = 2f;
        /// <summary>
        /// World reactions: the ground paint texture (Reactions.GroundPaint). When set and accepting, a still decal or mark
        /// (blood once spread, scorch, slam crack) is drawn ONCE into Paint.Builder and leaves the per-frame ground mesh one
        /// paint flush later; animated parts (spreading pools, corruption puddles, drips) stay in the mesh. Null: as before.
        /// </summary>
        public IGroundPaintTarget Paint;

        /// <summary>A Kaiju footfall at sim (x, z) (CombatFx forwards it to the WorldForceBus until DG-93 pushes contacts).</summary>
        public System.Action<double, double> KaijuFootfall;

        private readonly int[] _decalStamp = new int[MaxDecals];
        private readonly int[] _markStamp = new int[MaxMarks];

        /// <summary>True when the decal at p lies on the island ground (not a wall top): only those go to the paint.</summary>
        private bool OnGround(Vector3 p)
        {
            double x = SimX(p), z = SimZ(p);
            return IsGround(x, z) && Mathf.Abs(p.y - (float)Ground(x, z)) < 0.2f;
        }

        /// <summary>
        /// Paint hand-off for one still mark: false = draw it in the mesh this frame. Stamps it on the first call after it
        /// is still, keeps drawing it until the paint has flushed once since (no blank frame), then skips it.
        /// </summary>
        private bool PaintedAway(ref int stamp, Vector3 p, bool still)
        {
            if (Paint == null || !Paint.Accepts || !still) return false;
            if (stamp == -2) return false;                    // not on paintable ground: mesh only
            if (stamp == -1)
            {
                if (!OnGround(p)) { stamp = -2; return false; }
                stamp = Paint.Frame;
                return false;                                 // caller stamps now and still draws it this frame
            }
            return Paint.Frame > stamp;
        }

        /// <summary>Decals and marks drawn in the ground mesh last build (budget tests: under 6k verts with paint on).</summary>
        public int LastGroundMarks { get; private set; }

        public FxScene(GameConfig cfg, MapDef map)
        {
            _cfg = cfg;
            _map = map;
            _speed = cfg.DefaultSpeed > 0 ? cfg.DefaultSpeed : 1;
            _hz = cfg.SimHz;
            _cap = World.ActorCapacity(cfg);
            for (int i = 0; i < MaxBursts; i++) _bursts[i].Born = double.NegativeInfinity;
            for (int i = 0; i < MaxDroplets; i++) { _drops[i].Born = double.NegativeInfinity; _drops[i].Landed = true; }
            for (int i = 0; i < MaxDecals; i++) { _decals[i].Born = double.NegativeInfinity; _decals[i].Life = 0; _decalStamp[i] = -1; }
            for (int i = 0; i < MaxMarks; i++) _markStamp[i] = -1;
            for (int i = 0; i < MaxRings; i++) _rings[i].Born = double.NegativeInfinity;
            for (int i = 0; i < MaxTrails; i++) _trails[i].Born = double.NegativeInfinity;
            for (int i = 0; i < MaxPuffs; i++) _puffs[i].Born = double.NegativeInfinity;
            for (int i = 0; i < MaxSparks; i++) _sparks[i].Born = double.NegativeInfinity;
            InitGore();
            InitBoom();
            InitWorld();
        }

        /// <summary>Sim seconds for a wall-clock duration at the default match speed.</summary>
        public double S(double wallSeconds) => wallSeconds * _speed;

        // Lifetimes (sim seconds).
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

        /// <summary>The cell type under sim (x, z), Void off the map.</summary>
        public byte CellAt(double x, double z)
        {
            int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
            if (_map.Cells == null || cx < 0 || cz < 0 || cx >= _map.Width || cz >= _map.Height) return Cell.Void;
            return _map.Cells[cz * _map.Width + cx];
        }

        /// <summary>True when (x, z) is walkable ground (floor, bridge, rubble, barricade footing): decals land only there.</summary>
        public bool IsGround(double x, double z)
        {
            byte c = CellAt(x, z);
            return c == Cell.Floor || c == Cell.Bridge || c == Cell.BreakableDestroyed || c == Cell.Barricade;
        }

        /// <summary>
        /// The top face a splat can rest on at sim (x, z): ground cells at their height, walls at their top face (the grey
        /// box heights of GridChunkMeshView.CellBox: wall and intact breakable +1.5, cracked +0.75). False for void and water.
        /// </summary>
        public bool SurfaceTop(double x, double z, out double y)
        {
            byte c = CellAt(x, z);
            double h = Ground(x, z);
            switch (c)
            {
                case Cell.Floor:
                case Cell.Bridge:
                case Cell.BreakableDestroyed:
                case Cell.Barricade: y = h; return true;
                case Cell.Wall:
                case Cell.BreakableIntact: y = h + 1.5; return true;
                case Cell.BreakableCracked: y = h + 0.75; return true;
                default: y = h; return false;
            }
        }

        // ---- counts (tests, budgets) ----
        public int ActiveCount(double now)
        {
            int n = 0;
            for (int i = 0; i < MaxBursts; i++) if (now - _bursts[i].Born < _bursts[i].Life) n++;
            for (int i = 0; i < MaxDroplets; i++) if (!_drops[i].Landed) n++;
            for (int i = 0; i < MaxDecals; i++) if (now - _decals[i].Born < _decals[i].Life) n++;
            for (int i = 0; i < MaxRings; i++) if (now - _rings[i].Born < _rings[i].Life) n++;
            for (int i = 0; i < MaxTrails; i++) if (now - _trails[i].Born < TrailLife) n++;
            for (int i = 0; i < MaxPuffs; i++) if (now - _puffs[i].Born < _puffs[i].Life) n++;
            for (int i = 0; i < MaxSparks; i++) if (now - _sparks[i].Born < _sparks[i].Life) n++;
            return n + ActiveGore(now) + ActiveBoom(now) + ActiveWorld(now);
        }

        /// <summary>Live blood splats and pools (wall tops included).</summary>
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

        /// <summary>Every live blood piece: droplets in flight, splats and pools, drips and gibs (0 whenever Blood is off).</summary>
        public int BloodCount(double now)
        {
            int n = DecalCount(now) + DripCount(now) + GibCount(now);
            for (int i = 0; i < MaxDroplets; i++) if (!_drops[i].Landed) n++;
            return n;
        }

        public static int Capacity => MaxBursts + MaxDroplets + MaxDecals + MaxRings + MaxTrails + MaxChunks + MaxPuffs + MaxSparks +
                                      MaxMarks + MaxDrips + MaxGibs + MaxExplosions + MaxMotes + MaxBubbles + MaxLeaves;

        // ---- worst-case vertex cost per element and layer (the draw code below stays inside these) ----
        public const int CostBurst = 270, CostDroplet = 30, CostDecal = 130, CostRingGround = 390, CostRingAir = 52, CostTrail = 100;
        public const int CostChunk = 48, CostPuff = 40, CostSpark = 27, CostMark = 240, CostDrip = 14, CostGib = 144;
        public const int CostExplosionAir = 520, CostExplosionGround = 21, CostMote = 36, CostBubble = 74, CostLeaf = 14;
        public const int CostShield = 66, CostKaijuGround = 600, CostKaijuAir = 108;

        /// <summary>Upper bound of vertices per layer (0 ground, 1 air, 2 solid) with every pool full at its worst shape.</summary>
        public int WorstCaseVerts(int layer)
        {
            switch (layer)
            {
                case 0: return MaxDecals * CostDecal + MaxMarks * CostMark + MaxDrips * CostDrip + MaxRings * CostRingGround +
                               MaxExplosions * CostExplosionGround + CostKaijuGround;
                case 1: return MaxBursts * CostBurst + MaxDroplets * CostDroplet + MaxSparks * CostSpark + MaxPuffs * CostPuff +
                               MaxTrails * CostTrail + _cap * CostShield + CostKaijuAir + MaxExplosions * CostExplosionAir +
                               MaxMotes * CostMote + MaxBubbles * CostBubble + MaxLeaves * CostLeaf + MaxRings * CostRingAir;
                default: return MaxChunks * CostChunk + MaxGibs * CostGib;
            }
        }

        // ---- spawning from events ----

        public void OnFx(ref FxEvent e, World w)
        {
            double t = T(e.Tick);
            uint seed = Hash((uint)e.Tick, (uint)(e.A + 7), (uint)e.Kind);
            switch (e.Kind)
            {
                case FxKind.Hit:
                case FxKind.Death:
                    SpawnHit(ref e, w, t, seed);
                    break;
                case FxKind.Block:
                {
                    double h = Ground(e.X, e.Z);
                    double facing = e.A >= 0 && e.A < w.ActorCount ? w.Actors[e.A].Facing : Math.Atan2(-e.DirZ, -e.DirX);
                    double fx = JsMath.Cos(facing), fz = JsMath.Sin(facing);
                    Vector3 p = W(e.X + fx * 0.7, h + 0.95, e.Z + fz * 0.7);
                    AddBurst(t, p, 0.5f, 2, seed, Vector3.zero, S(0.16));
                    for (int k = 0; k < 7; k++) AddSpark(t, p, -e.DirX, -e.DirZ, seed, (uint)k, 2);
                    // Ring ripple across the shield face: an expanding arc on the real +-60 degree cone.
                    AddRing(t, W(e.X, h + 0.9, e.Z), 0.84f, 1.45f, 0.16f, S(0.24), Steel, 3, (float)-facing);
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
                    byte tier = FxTier(e.Value, false);
                    AddBurst(t, p, 0.45f + 0.3f * Mathf.Clamp01((float)(e.Value / 30.0)), tier == 0 ? (byte)0 : (byte)1, seed, Vector3.down, S(tier == 0 ? 0.16 : 0.2));
                    for (int k = 0; k < 3 + tier * 2; k++) AddSpark(t, p, Math.Cos(a), Math.Sin(a), seed, (uint)k, 1);
                    break;
                }
                case FxKind.KaijuSlam:
                    SpawnExplosion(t, e.X, e.Z, Mathf.Clamp((float)e.Value, 0.5f, 8f), ExplosionKind.KaijuSlam, seed, w);
                    break;
                case FxKind.Explosion:
                {
                    int k = e.B;
                    if (k < 0 || k > (int)ExplosionKind.KaijuSlam) k = 0;
                    SpawnExplosion(t, e.X, e.Z, Mathf.Clamp((float)e.Value, 0.5f, 6f), (ExplosionKind)k, seed, w);
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
                    if (e.Kind == FxKind.Hatch) RustleArea(e.X, e.Z, 4.0, t, seed);
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
                    SpawnResource(ref e, w, t, seed);
                    break;
                case FxKind.BarricadePlaced:
                {
                    double h = Ground(e.X, e.Z);
                    Vector3 c = W(e.X, h + 0.15, e.Z);
                    for (int k = 0; k < 6; k++) AddPuff(t, c, seed, (uint)k, 0.35f, DustMid, 1);
                    break;
                }
                case FxKind.BarricadeBroken:
                case FxKind.WallCrack:
                {
                    double h = Ground(e.X, e.Z);
                    bool wood = e.Kind == FxKind.BarricadeBroken;
                    int n = e.Kind == FxKind.WallCrack ? 5 : 12;
                    Vector3 c = W(e.X, h + (wood ? 0.6 : 0.9), e.Z);
                    for (int k = 0; k < n; k++) AddChunk(t, c, 0.4f, seed, (uint)k, wood ? (byte)1 : (byte)0, e.Kind == FxKind.WallCrack ? 0.6f : 0.85f, 1f);
                    for (int k = 0; k < (n + 1) / 2; k++) AddPuff(t, c, seed, (uint)k + 20, 0.45f, DustMid, 1);
                    break;
                }
                case FxKind.WallBreak:
                    SpawnExplosion(t, e.X, e.Z, 1.1f, ExplosionKind.WallBreak, seed, w);
                    break;
                case FxKind.Splash:
                    SpawnSplash(t, e.X, e.Z, Mathf.Clamp((float)e.Value, 0.3f, 3f), seed);
                    break;
                case FxKind.Land:
                    SpawnLanding(t, e.X, e.Z, e.Value >= 1, seed);
                    break;
            }
        }

        /// <summary>How long a dead actor's body is shown (Death clip, a beat on the floor, then the pop), sim seconds.</summary>
        public double CorpseSeconds => S(1.35);
        public double PopSeconds => S(0.14);

        /// <summary>Same tiers as CombatFeed.TierOf (hits decoded elsewhere carry their tier in FxEvent.Tier).</summary>
        private static byte FxTier(double dmg, bool death) => CombatFeed.TierOf(dmg, death);

        /// <summary>
        /// The world direction the weapon travels through the victim (slash arcs follow it). Horizontal cleaves sweep across
        /// the attack arc the way AddTrail draws them; chops (Miner, Builder) come down; Demolitions diagonal; Kaiju claws down
        /// and across.
        /// </summary>
        private static Vector3 SwingTangent(ref FxEvent e)
        {
            Vector3 d = W(e.DirX, 0, e.DirZ);
            if (d.sqrMagnitude < 1e-6f) d = Vector3.right;
            d.Normalize();
            var across = new Vector3(d.z, 0, -d.x); // the cleave's sweep direction at the victim (basis angle decreasing)
            if (e.B == Damage.KaijuSource) return (across + Vector3.down * 1.3f).normalized;
            if (!e.HasAttacker) return across;
            switch (e.AttCls)
            {
                case ClassId.Miner:
                case ClassId.Builder: return (Vector3.down * 1.6f + d * 0.4f).normalized;
                case ClassId.Demolitions: return (across + Vector3.down).normalized;
                case ClassId.Farmer: return -across;
                default: return across;
            }
        }

        private void SpawnHit(ref FxEvent e, World w, double t, uint seed)
        {
            bool death = e.Kind == FxKind.Death;
            double h = Ground(e.X, e.Z);
            double r = _cfg.Class(e.Cls).Radius;
            // Contact point: the victim's surface facing the source, at chest height.
            Vector3 contact = W(e.X - e.DirX * r * 0.8, h + 0.95, e.Z - e.DirZ * r * 0.8);
            float dmg01 = Mathf.Clamp01((float)(e.Value / 45.0));
            byte tier = death ? (byte)2 : e.Tier;
            Vector3 tan = SwingTangent(ref e);
            // Shape language per tier: light = star burst; heavy = star + slash arc along the swing; critical or finishing =
            // the big cracked impact frame. Kaiju melee adds three claw rakes.
            if (tier == 0) AddBurst(t, contact, 0.34f + 0.4f * dmg01, 0, seed, tan, S(0.16));
            else if (tier == 1) AddBurst(t, contact, 0.52f + 0.4f * dmg01, 1, seed, tan, S(0.2));
            else AddBurst(t, contact, 0.95f + (death ? 0.15f : 0f), 4, seed, tan, S(0.3));
            if (e.B == Damage.KaijuSource) AddBurst(t, contact + Vector3.up * 0.2f, 1.05f, 5, seed + 1, tan, S(0.24));

            // Knock-back dust at the feet, blown along the hit.
            // feat/light-weapons-2: a light-weapon hit shoves, so it gets the same dust (two puffs) to read as a push-back.
            if (tier >= 1 || CriticalMass.Runtime.View.Anim.LightWeaponView.Shoves(_cfg, e.HasAttacker, e.AttCls))
            {
                Vector3 feet = W(e.X, h + 0.12, e.Z);
                for (int k = 0; k < 2 + tier; k++) AddPuff(t, feet, seed, (uint)(k + 60), 0.3f + 0.06f * tier, DustMid, 2, e.DirX, e.DirZ);
                RustleArea(e.X, e.Z, 0.9 + 0.5 * tier, t, seed);
            }

            if (death) RememberDeath(t, e.X, e.Z, h, seed, e.DirX, e.DirZ);
            if (Blood)
            {
                int n = death ? 14 : 2 + (int)Math.Min(6, e.Value / 6) + tier * 2;
                float spread = tier == 0 ? 0.95f : tier == 1 ? 0.6f : 0.75f;
                for (int k = 0; k < n; k++) AddDroplet(t, e, h, seed, (uint)k, death, spread, 1f);
                // Mist: small fast drops straight down the hit line (the "spray").
                for (int k = 0; k < tier; k++) AddDroplet(t, e, h, seed, (uint)(k + 40), false, 0.2f, 1.7f);
                if (death)
                {
                    if (SurfaceTop(e.X, e.Z, out double py))
                        AddDecal(t, W(e.X + e.DirX * 0.25, py, e.Z + e.DirZ * 0.25), 1.25f, 1, 6, seed, e.DirX, e.DirZ, S(1.0));
                    if (GibWorthy(ref e, w)) AddGibs(t, e.X, e.Z, h, seed, e.DirX, e.DirZ);
                }
            }
            else
            {
                int n = death ? 12 : 2 + (int)Math.Min(6, e.Value / 6);
                for (int k = 0; k < n + 2 + tier * 2; k++) AddSpark(t, contact, e.DirX, e.DirZ, seed, (uint)k, 1);
            }
            if (death)
            {
                // The corpse pops out with a puff when CombatFx stops showing it (CorpseSeconds after the death).
                double pop = t + CorpseSeconds;
                Vector3 p = W(e.X, h + 0.5, e.Z);
                for (int k = 0; k < 7; k++) AddPuff(pop, p, seed, (uint)(k + 40), 0.42f, DustLight, 0);
            }
        }

        private void AddBurst(double t, Vector3 p, float size, byte kind, uint seed, Vector3 tan, double life)
        {
            ref Burst b = ref _bursts[_nb++ % MaxBursts];
            b.Born = t; b.P = p; b.Size = size; b.Kind = kind; b.Rot = R01(seed, 77) * Mathf.PI; b.Tan = tan; b.Life = life;
        }

        private void AddDroplet(double t, FxEvent e, double h, uint seed, uint k, bool big, float spreadRad, float speedMul)
        {
            ref Droplet d = ref _drops[_nd++ % MaxDroplets];
            float spread = (R01(seed, k * 3 + 1) - 0.5f) * 2f * spreadRad;
            double dx = e.DirX * Math.Cos(spread) - e.DirZ * Math.Sin(spread);
            double dz = e.DirX * Math.Sin(spread) + e.DirZ * Math.Cos(spread);
            float sp = ((big ? 2.4f : 1.6f) + R01(seed, k * 3 + 2) * (big ? 3.2f : 2.6f)) * speedMul;
            float vy = (3.0f + R01(seed, k * 3 + 3) * 3.4f) / Mathf.Sqrt(speedMul);
            d.Born = t;
            d.P0 = W(e.X + e.DirX * 0.15, h + 0.95, e.Z + e.DirZ * 0.15);
            Vector3 dirW = W(dx, 0, dz);
            d.V = dirW * sp + Vector3.up * vy;
            d.Size = ((big ? 0.12f : 0.08f) + R01(seed, k * 7 + 5) * 0.07f) * (speedMul > 1f ? 0.6f : 1f);
            d.GroundY = (float)h;
            float dy = d.P0.y - d.GroundY;
            d.TLand = (vy + Mathf.Sqrt(vy * vy + 2f * G * dy)) / G;
            d.Landed = false;
            d.Pool = big;
            d.DirX = dx;
            d.DirZ = dz;
        }

        private void AddDecal(double t, Vector3 p, float size, byte kind, byte shape, uint seed, double dirX, double dirZ, double grow, double life = -1)
        {
            _decalStamp[_ndc % MaxDecals] = -1;
            ref Decal d = ref _decals[_ndc++ % MaxDecals];
            d.Born = t; d.P = p; d.Size = size; d.Kind = kind; d.Shape = shape; d.Seed = seed; d.Grow = (float)grow;
            d.Life = life > 0 ? life : DecalLife;
            d.Rot = R01(seed, 11) * Mathf.PI * 2f;
            d.DirA = Mathf.Atan2((float)-dirZ, (float)dirX); // basis angle in the ground plane (right = +X, up = +Z world)
        }

        private void AddRing(double t, Vector3 p, float r0, float r1, float width, double life, Color32 col, byte kind, float facing = 0f)
        {
            ref Ring r = ref _rings[_nr++ % MaxRings];
            r.Born = t; r.P = p; r.R0 = r0; r.R1 = r1; r.Width = width; r.Life = life; r.Col = col; r.Kind = kind; r.Facing = facing;
        }

        private void AddTrail(double t, FxEvent e, World w)
        {
            ref Trail tr = ref _trails[_nt++ % MaxTrails];
            double h = Ground(e.X, e.Z);
            tr.Born = t;
            tr.P = W(e.X, h, e.Z);
            tr.Facing = (float)Math.Atan2(e.DirZ, e.DirX);
            // feat/light-weapons-2: a light weapon draws its own short arc (its reach and width, not the class row's).
            tr.Arc = (float)(CriticalMass.Runtime.View.Anim.LightWeaponView.ArcDeg(_cfg, e.Cls) * Math.PI / 180);
            tr.Radius = (float)CriticalMass.Runtime.View.Anim.LightWeaponView.Range(_cfg, e.Cls) * 0.82f;
            tr.Team = e.Team == TeamId.Azure ? AzureC : CrimsonC;
            tr.Kind = (byte)e.Cls;
        }

        private void AddPuff(double t, Vector3 p, uint seed, uint k, float size, Color32 col, byte kind, double dirX = 0, double dirZ = 0)
        {
            ref Puff f = ref _puffs[_np++ % MaxPuffs];
            float a = R01(seed, k * 3 + 100) * Mathf.PI * 2f;
            float sp = (kind == 1 ? 1.4f : 0.8f) + R01(seed, k * 3 + 101) * 1.2f;
            f.Born = t;
            f.P0 = p;
            var v = new Vector3(Mathf.Cos(a) * sp, 0.6f + R01(seed, k * 3 + 102) * 0.9f, Mathf.Sin(a) * sp);
            if (kind == 2 && (dirX != 0 || dirZ != 0)) v = W(dirX, 0, dirZ) * (1.6f + R01(seed, k + 105) * 1.4f) + v * 0.45f; // knock-back: blown along the hit
            if (kind == 3) v *= 0.5f; // plank dust: short and low
            f.V = v;
            f.Size = size * (0.7f + 0.6f * R01(seed, k * 3 + 103));
            f.Col = col;
            f.Kind = kind;
            f.Life = PuffLife * (0.8 + 0.4 * R01(seed, k + 104)) * (kind == 5 ? 2.2 : 1.0);
        }

        /// <summary>kind 1 white-yellow hit spark, 2 steel block spark, 3 biomass mote, 5 ember, 6 water drop, 7 grit.</summary>
        private void AddSpark(double t, Vector3 p, double dirX, double dirZ, uint seed, uint k, byte kind)
        {
            ref Puff s = ref _sparks[_ns++ % MaxSparks];
            float a = R01(seed, k * 3 + 200) * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            if (dirX != 0 || dirZ != 0) dir = (W(dirX, 0, dirZ) * 1.3f + dir * 0.8f).normalized;
            float sp = 3f + R01(seed, k * 3 + 201) * 4f;
            if (kind == 3) sp *= 0.4f;
            if (kind == 5) sp *= 0.35f;
            s.Born = t;
            s.P0 = p;
            float up = kind == 3 ? 3.2f : kind == 5 ? 2.4f + R01(seed, k + 204) * 2f : kind == 6 ? 4f + R01(seed, k + 204) * 2.5f : 1.5f + R01(seed, k + 202) * 2.5f;
            s.V = dir * sp + Vector3.up * up;
            s.Size = (kind == 3 ? 0.12f : kind == 6 ? 0.1f : 0.09f) + R01(seed, k + 203) * 0.06f;
            s.Col = kind == 2 ? Steel : kind == 3 ? BiomassC : kind == 5 ? FireOrange : kind == 6 ? WaterMid : kind == 7 ? DirtMid : SparkYellow;
            s.Kind = kind;
            s.Life = SparkLife * (kind == 3 ? 2.2 : kind == 5 ? 3.6 : kind == 6 ? 1.8 : 1.0);
        }

        private static double SimX(Vector3 p) => p.x;
        private static double SimZ(Vector3 p) => -p.z;

        // ---- per-frame update and mesh build ----

        /// <summary>Land droplets, chunks and gibs whose flight ended by `now` (each landing's birth = its exact landing time).</summary>
        public void Advance(double now)
        {
            for (int i = 0; i < MaxDroplets; i++)
            {
                ref Droplet d = ref _drops[i];
                if (d.Landed || now < d.Born + d.TLand) continue;
                d.Landed = true;
                Vector3 p = d.P0 + d.V * d.TLand;
                double sx = p.x, sz = -p.z;
                if (!SurfaceTop(sx, sz, out double top)) continue;
                p.y = (float)top;
                uint seed = Hash((uint)i, (uint)(d.Born * 1000), 3u);
                // Six splat shapes for flying drops; the pool shape (6) is the knock-out's own.
                byte shape = (byte)(Hash(seed, 5u, 9u) % 6u);
                AddDecal(d.Born + d.TLand, p, d.Size * (d.Pool ? 3.6f : 3.0f), 0, shape, seed, d.DirX, d.DirZ, S(0.05));
            }
            AdvanceSolids(now);
        }

        private static Color32 A(Color32 c, float a)
        {
            c.a = (byte)Mathf.Clamp(a * 255f, 0, 255);
            return c;
        }

        /// <summary>Rebuild the three layer meshes: ground (decals, marks, rings, telegraph, light pulses), air (billboards,
        /// trails, fireballs, smoke), solid (chunks, gibs).</summary>
        public void Build(double now, World w, Vector3 camRight, Vector3 camUp, FxMeshBuilder ground, FxMeshBuilder air, FxMeshBuilder solid)
        {
            ground.Clear();
            air.Clear();
            solid.Clear();
            Vector3 gr = Vector3.right, gu = Vector3.forward; // ground plane basis (world)
            const float lift = 0.03f;
            if (BlobShadows) DrawBlobShadows(w, ground);

            LastGroundMarks = 0;
            BuildMarks(now, ground);
            BuildDecals(now, ground);
            BuildDrips(now, ground);

            // Expanding rings (0 ground ring, 1 roar double ring, 2 respawn beam, 3 shield ripple, 4 water splash ring).
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
                if (r.Kind == 3)
                {
                    // Shield ripple: the struck arc pushed outward, navy then steel-white.
                    float half = (float)(Damage.ShieldHalfArcDeg * Math.PI / 180) * (1f - 0.3f * t);
                    air.Ring(r.P, gr, gu, rad - wd * 0.5f - 0.04f, rad + wd * 0.5f + 0.04f, r.Facing - half, r.Facing + half, 12, A(Navy, fade * 0.8f));
                    air.Ring(r.P, gr, gu, rad - wd * 0.5f, rad + wd * 0.5f, r.Facing - half, r.Facing + half, 12, A(t < 0.3f ? SparkWhite : r.Col, fade));
                    continue;
                }
                Vector3 c = r.P + Vector3.up * lift;
                int seg = Mathf.Clamp((int)(rad * 10f), 20, 64);
                ground.Ring(c, gr, gu, rad - wd * 0.5f - 0.05f, rad + wd * 0.5f + 0.05f, 0, Mathf.PI * 2f, seg, A(Navy, fade * 0.85f));
                ground.Ring(c, gr, gu, rad - wd * 0.5f, rad + wd * 0.5f, 0, Mathf.PI * 2f, seg, A(r.Col, fade));
                if (r.Kind == 0) ground.Ring(c, gr, gu, rad - wd * 0.5f, rad - wd * 0.1f, 0, Mathf.PI * 2f, seg, A(SparkWhite, fade * 0.8f));
            }

            DrawKaijuState(w, ground, air, now);
            DrawShields(w, air, now);
            BuildBoomGround(now, ground);

            BuildTrails(now, air, gr, gu);
            BuildBursts(now, air, camRight, camUp);

            // Blood droplets in flight: teardrops stretched along their own velocity, navy rims.
            for (int i = 0; i < MaxDroplets; i++)
            {
                ref Droplet d = ref _drops[i];
                if (d.Landed) continue;
                float age = (float)(now - d.Born);
                if (age < 0) continue;
                Vector3 p = d.P0 + d.V * age + Vector3.down * (0.5f * G * age * age);
                Vector3 v = d.V + Vector3.down * (G * age);
                float vx = Vector3.Dot(v, camRight), vy = Vector3.Dot(v, camUp);
                float vl = Mathf.Sqrt(vx * vx + vy * vy);
                Vector3 back = vl > 1e-3f ? -(camRight * (vx / vl) + camUp * (vy / vl)) : -camUp;
                Vector3 side = Vector3.Cross(back, Vector3.Cross(camRight, camUp)).normalized;
                float tail = d.Size * Mathf.Min(2.6f, 0.6f + vl * 0.12f);
                air.Blob(p, camRight, camUp, d.Size + 0.03f, null, 7, 0, Navy);
                air.Quad(p + back * (tail * 0.5f), back * (tail * 0.5f), side * (d.Size * 0.62f + 0.03f), Navy, Navy);
                air.Blob(p, camRight, camUp, d.Size, null, 7, 0, BloodMid);
                air.Quad(p + back * (tail * 0.5f), back * (tail * 0.5f), side * (d.Size * 0.62f), BloodMid, BloodMid);
                air.Blob(p + (camUp - camRight) * d.Size * 0.3f, camRight, camUp, d.Size * 0.38f, null, 5, 0, BloodLight);
            }

            // Sparks (diamonds), embers, water drops, grit.
            for (int i = 0; i < MaxSparks; i++)
            {
                ref Puff s = ref _sparks[i];
                double age = now - s.Born;
                if (age < 0 || age >= s.Life) continue;
                float t = (float)(age / s.Life);
                float a = (float)age;
                float grav = s.Kind == 3 ? -2f : s.Kind == 5 ? 1.2f : s.Kind == 6 ? G : G * 0.6f;
                Vector3 p = s.P0 + s.V * a + Vector3.down * (0.5f * grav * a * a);
                if (s.Kind == 5) p += camRight * (0.12f * Mathf.Sin(a * 7f + i)); // embers flicker sideways as they float
                float sz = s.Size * (1f - t * 0.7f);
                float fade = 1f - t * t;
                if (s.Kind == 6)
                {
                    air.Blob(p, camRight, camUp, sz + 0.03f, null, 6, 0, A(Navy, fade * 0.8f));
                    air.Blob(p, camRight, camUp, sz, null, 6, 0, A(t < 0.4f ? WaterLight : s.Col, fade));
                    continue;
                }
                air.Star(p, camRight, camUp, sz + 0.035f, 0.45f, 4, 0.785f, A(Navy, fade));
                Color32 col = s.Col;
                if (s.Kind == 5) col = t < 0.3f ? FireYellow : t < 0.65f ? FireOrange : FireRed; // stepped cooling
                air.Star(p, camRight, camUp, sz, 0.45f, 4, 0.785f, A(col, fade));
                if (s.Kind <= 2 || s.Kind == 5) air.Star(p, camRight, camUp, sz * 0.5f, 0.5f, 4, 0.785f, A(SparkWhite, fade));
            }

            // Dust puffs: chunky outlined cloud blobs, rise and fade (kind 3 plank dust in wood tones, 4 foam, 5 smoke).
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
                Color32 hi = f.Kind == 3 ? WoodLight : f.Kind == 4 ? WaterLight : f.Kind == 5 ? SmokeLight : DustLight;
                Color32 lo = f.Kind == 3 ? WoodDark : f.Kind == 4 ? WaterMid : f.Kind == 5 ? SmokeDark : DustDark;
                air.Blob(p, camRight, camUp, sz + 0.05f, null, 9, 0, A(Navy, fade * 0.85f));
                air.Blob(p, camRight, camUp, sz, null, 9, 0, A(f.Col, fade));
                air.Blob(p + (camUp - camRight) * sz * 0.25f, camRight, camUp, sz * 0.55f, null, 8, 0, A(hi, fade));
                air.Blob(p - (camUp - camRight * 0.4f) * sz * 0.3f, camRight, camUp, sz * 0.45f, null, 8, 0, A(lo, fade * 0.8f));
            }

            BuildBoomAir(now, air, camRight, camUp);
            BuildWorldAir(now, w, air, camRight, camUp);
            BuildSolids(now, solid);
        }

        // ---- hit bursts: light star, heavy star + slash, block, gather, critical impact frame, Kaiju claws ----

        private void BuildBursts(double now, FxMeshBuilder air, Vector3 camRight, Vector3 camUp)
        {
            Vector3 toCam = -Vector3.Cross(camRight, camUp);
            for (int i = 0; i < MaxBursts; i++)
            {
                ref Burst b = ref _bursts[i];
                double age = now - b.Born;
                if (age < 0 || age >= b.Life) continue;
                float t = (float)(age / b.Life);
                float s = b.Size * (t < 0.25f ? Mathf.Lerp(0.55f, 1.15f, t / 0.25f) : Mathf.Lerp(1.15f, 0.35f, (t - 0.25f) / 0.75f));
                float fade = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
                Vector3 p = b.P + toCam * 0.3f; // nudge toward the camera
                // The slash plane: the swing tangent projected into the camera plane.
                float tx = Vector3.Dot(b.Tan, camRight), ty = Vector3.Dot(b.Tan, camUp);
                float tl = Mathf.Sqrt(tx * tx + ty * ty);
                if (tl < 1e-3f) { tx = 1; ty = 0; tl = 1; }
                Vector3 sR = camRight * (tx / tl) + camUp * (ty / tl);
                Vector3 sU = Vector3.Cross(toCam, sR).normalized;
                switch (b.Kind)
                {
                    case 4:
                        DrawImpactFrame(air, p, sR, sU, camRight, camUp, b, (float)age, t, fade);
                        break;
                    case 5:
                    {
                        // Kaiju claws: three parallel rakes along the swing, hot yellow on navy.
                        for (int k = -1; k <= 1; k++)
                        {
                            Vector3 o = sU * (k * 0.22f * b.Size);
                            Slash(air, p + o, sR, sU, b.Size * 0.9f, 0.13f, t, fade, WarnHot, Warn, 1.15f);
                        }
                        air.Star(p, camRight, camUp, s * 0.5f + 0.04f, 0.42f, 8, b.Rot, A(Navy, fade));
                        air.Star(p, camRight, camUp, s * 0.5f, 0.42f, 8, b.Rot, A(Warn, fade));
                        break;
                    }
                    default:
                    {
                        Color32 mid = b.Kind == 2 ? Steel : b.Kind == 3 ? BiomassC : SparkYellow;
                        int pts = b.Kind == 2 ? 6 : 8;
                        if (b.Kind == 1) Slash(air, p, sR, sU, b.Size * 1.1f, 0.15f, t, fade, SparkYellow, SparkWhite, 1.0f);
                        air.Star(p, camRight, camUp, s * 1.22f + 0.04f, 0.42f, pts, b.Rot, A(Navy, fade));
                        air.Star(p, camRight, camUp, s, 0.42f, pts, b.Rot, A(mid, fade));
                        air.Star(p, camRight, camUp, s * 0.62f, 0.5f, pts, b.Rot + 0.2f, A(SparkWhite, fade));
                        if (b.Kind == 0 || b.Kind == 1) air.Blob(p, camRight, camUp, s * 0.24f, null, 8, 0, A(SparkOrange, fade));
                        break;
                    }
                }
            }
        }

        /// <summary>A slash arc through p along sR (the head sweeps in, the tail follows): navy, colour, white core. 78 verts.</summary>
        private static void Slash(FxMeshBuilder air, Vector3 p, Vector3 sR, Vector3 sU, float rad, float w, float t, float fade, Color32 col, Color32 core, float span)
        {
            Vector3 c = p - sU * rad;
            float a0 = Mathf.PI * 0.5f + span * 0.5f, a1 = Mathf.PI * 0.5f - span * 0.5f;
            float sweep = Mathf.Clamp01(t / 0.45f);
            sweep = 1f - (1f - sweep) * (1f - sweep);
            float head = Mathf.Lerp(a0, a1, sweep);
            float tail = Mathf.Lerp(a0, head, Mathf.Clamp01(t * 1.6f));
            float f = t < 0.5f ? 1f : fade;
            air.Crescent(c, sR, sU, rad, w * 1.4f, tail, head, 12, A(Navy, 0), A(Navy, f));
            air.Crescent(c, sR, sU, rad, w, tail, head, 12, A(col, 0), A(col, f));
            air.Crescent(c, sR, sU, rad + w * 0.15f, w * 0.45f, tail, head, 12, A(core, 0), A(core, f));
        }

        /// <summary>
        /// The finisher's cracked impact frame: for its first two frames an inverted navy star with a white rim (a manga-style
        /// impact frame, local to the hit, never full screen); then a 12-point white and yellow star, a slash across it and
        /// six crack lines that shoot outward. At most 201 verts.
        /// </summary>
        private void DrawImpactFrame(FxMeshBuilder air, Vector3 p, Vector3 sR, Vector3 sU, Vector3 camRight, Vector3 camUp, in Burst b, float age, float t, float fade)
        {
            float s = b.Size * (t < 0.18f ? Mathf.Lerp(0.6f, 1.25f, t / 0.18f) : Mathf.Lerp(1.25f, 0.45f, (t - 0.18f) / 0.82f));
            bool inverted = age < S(0.034);
            air.Star(p, camRight, camUp, s * 1.18f + 0.06f, 0.36f, 12, b.Rot, A(inverted ? SparkWhite : Navy, fade));
            air.Star(p, camRight, camUp, s, 0.36f, 12, b.Rot, A(inverted ? Navy : SparkWhite, fade));
            if (!inverted) air.Star(p, camRight, camUp, s * 0.6f, 0.44f, 12, b.Rot + 0.13f, A(SparkYellow, fade));
            Slash(air, p, sR, sU, b.Size * 1.25f, 0.2f, t, fade, SparkWhite, SparkYellow, 1.2f);
            // Crack lines: jagged spokes that run out from the rim.
            float run = Mathf.Clamp01(t / 0.35f);
            for (int k = 0; k < 6; k++)
            {
                float a = b.Rot + k * Mathf.PI / 3f + 0.3f;
                Vector3 dir = camRight * Mathf.Cos(a) + camUp * Mathf.Sin(a);
                Vector3 perp = camRight * -Mathf.Sin(a) + camUp * Mathf.Cos(a);
                float r0 = s * 0.9f, r1 = s * (1.0f + 0.9f * run);
                Vector3 mid = p + dir * ((r0 + r1) * 0.5f);
                air.Quad(mid, dir * ((r1 - r0) * 0.5f), perp * 0.07f, A(Navy, fade), A(Navy, fade));
                air.Quad(mid, dir * ((r1 - r0) * 0.5f), perp * 0.03f, A(SparkWhite, fade), A(SparkWhite, fade));
            }
        }

        // ---- per-class weapon trails ----

        private void BuildTrails(double now, FxMeshBuilder air, Vector3 gr, Vector3 gu)
        {
            for (int i = 0; i < MaxTrails; i++)
            {
                ref Trail tr = ref _trails[i];
                double age = now - tr.Born;
                if (age < 0 || age >= TrailLife) continue;
                float t = (float)(age / TrailLife);
                float sweep = Mathf.Clamp01(t / 0.55f);
                sweep = 1f - (1f - sweep) * (1f - sweep);
                float fade = t < 0.55f ? 1f : 1f - (t - 0.55f) / 0.45f;
                Vector3 fwd = W(Math.Cos(tr.Facing), 0, Math.Sin(tr.Facing)).normalized;
                switch ((ClassId)tr.Kind)
                {
                    case ClassId.Warrior:
                    {
                        // Cleave: a wide horizontal crescent across the real attack arc, team colour with a white edge.
                        float centre = -tr.Facing; // sim angle -> ground basis angle
                        float a0 = centre + tr.Arc * 0.5f, a1 = centre - tr.Arc * 0.5f;
                        float head = Mathf.Lerp(a0, a1, sweep);
                        float tail = Mathf.Lerp(a0, head, Mathf.Clamp01(t * 1.4f));
                        Vector3 c = tr.P + Vector3.up * 0.85f;
                        air.Crescent(c, gr, gu, tr.Radius, 0.3f, tail, head, 14, A(Navy, 0), A(Navy, fade * 0.95f));
                        air.Crescent(c, gr, gu, tr.Radius, 0.23f, tail, head, 14, A(tr.Team, 0), A(tr.Team, fade * 0.9f));
                        air.Crescent(c, gr, gu, tr.Radius + 0.03f, 0.1f, tail, head, 14, A(SparkWhite, 0), A(SparkWhite, fade));
                        break;
                    }
                    case ClassId.Farmer:
                    {
                        // Harvest sweep: a thin, low, fast crescent the other way round, gold with a biomass-green edge.
                        float centre = -tr.Facing;
                        float a0 = centre - tr.Arc * 0.5f, a1 = centre + tr.Arc * 0.5f;
                        float head = Mathf.Lerp(a0, a1, Mathf.Clamp01(sweep * 1.2f));
                        float tail = Mathf.Lerp(a0, head, Mathf.Clamp01(t * 1.6f));
                        Vector3 c = tr.P + Vector3.up * 0.6f;
                        air.Crescent(c, gr, gu, tr.Radius * 1.05f, 0.18f, tail, head, 12, A(Navy, 0), A(Navy, fade * 0.95f));
                        air.Crescent(c, gr, gu, tr.Radius * 1.05f, 0.13f, tail, head, 12, A(HarvestGold, 0), A(HarvestGold, fade * 0.9f));
                        air.Crescent(c, gr, gu, tr.Radius * 1.05f + 0.04f, 0.05f, tail, head, 12, A(BiomassC, 0), A(BiomassC, fade));
                        break;
                    }
                    case ClassId.Demolitions:
                    {
                        // Heavy diagonal haymaker in hot orange, with a puff of smoke where it ends.
                        Vector3 up = (Vector3.up + Vector3.Cross(Vector3.up, fwd)).normalized;
                        Vector3 c = tr.P + Vector3.up * 0.8f;
                        float a0 = 2.1f, a1 = -0.5f;
                        float head = Mathf.Lerp(a0, a1, sweep);
                        float tail = Mathf.Lerp(a0, head, Mathf.Clamp01(t * 1.4f));
                        air.Crescent(c, fwd, up, 0.9f, 0.26f, tail, head, 12, A(Navy, 0), A(Navy, fade * 0.95f));
                        air.Crescent(c, fwd, up, 0.9f, 0.2f, tail, head, 12, A(WarnHot, 0), A(WarnHot, fade * 0.9f));
                        air.Crescent(c, fwd, up, 0.92f, 0.08f, tail, head, 12, A(SparkYellow, 0), A(SparkYellow, fade));
                        if (t > 0.45f)
                        {
                            Vector3 end = c + (fwd * Mathf.Cos(a1) + up * Mathf.Sin(a1)) * 0.9f;
                            float pf = 1f - (t - 0.45f) / 0.55f;
                            air.Blob(end, gr, Vector3.up, 0.22f + 0.04f, null, 9, 0, A(Navy, pf * 0.8f));
                            air.Blob(end, gr, Vector3.up, 0.22f, null, 9, 0, A(SmokeMid, pf));
                        }
                        break;
                    }
                    case ClassId.Builder:
                    {
                        // Hammer: a short, tight overhead arc in wood tones with a white edge.
                        Vector3 c = tr.P + Vector3.up * 0.7f;
                        float a0 = 1.7f, a1 = -0.2f;
                        float head = Mathf.Lerp(a0, a1, sweep);
                        float tail = Mathf.Lerp(a0, head, Mathf.Clamp01(t * 1.5f));
                        air.Crescent(c, fwd, Vector3.up, 0.72f, 0.22f, tail, head, 10, A(Navy, 0), A(Navy, fade * 0.95f));
                        air.Crescent(c, fwd, Vector3.up, 0.72f, 0.16f, tail, head, 10, A(WoodLight, 0), A(WoodLight, fade * 0.9f));
                        air.Crescent(c, fwd, Vector3.up, 0.74f, 0.06f, tail, head, 10, A(SparkWhite, 0), A(SparkWhite, fade));
                        break;
                    }
                    default:
                    {
                        // Miner pick: a vertical chop arc, overhead behind to low in front, yellow and white.
                        Vector3 c = tr.P + Vector3.up * 0.75f;
                        float a0 = 1.9f, a1 = -0.35f;
                        float head = Mathf.Lerp(a0, a1, sweep);
                        float tail = Mathf.Lerp(a0, head, Mathf.Clamp01(t * 1.4f));
                        air.Crescent(c, fwd, Vector3.up, 0.95f, 0.2f, tail, head, 12, A(Navy, 0), A(Navy, fade * 0.95f));
                        air.Crescent(c, fwd, Vector3.up, 0.95f, 0.15f, tail, head, 12, A(SparkYellow, 0), A(SparkYellow, fade * 0.9f));
                        air.Crescent(c, fwd, Vector3.up, 0.97f, 0.07f, tail, head, 12, A(SparkWhite, 0), A(SparkWhite, fade));
                        break;
                    }
                }
            }
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
                // fix/kaiju-alive-1: the fill runs over the sim's real telegraph (floored to KaijuSlamMinRealSeconds at the
                // pace), not the config's 1.2 s: at pace 2 the disc stayed empty for the first quarter of the warning.
                float p = Mathf.Clamp01(1f - (float)(k.TelegraphIn / Telegraphs.SlamTotalSim(w)));
                double h = Ground(k.TelegraphX, k.TelegraphZ);
                Vector3 c = W(k.TelegraphX, h + 0.04, k.TelegraphZ);
                float pulse = 0.75f + 0.25f * (FeelGate.Pulse(now, PaceNow, SlamPulseHz) * 2f - 1f) * (FeelGate.ReduceFlashing ? 0.4f : 1f);
                ground.Blob(c, gr, gu, R, null, 48, 0, A(WarnHot, 0.14f));
                ground.Blob(c, gr, gu, Mathf.Max(0.05f, R * p), null, 48, 0, A(Warn, 0.32f + 0.2f * p));
                ground.Ring(c, gr, gu, R - 0.2f, R + 0.08f, 0, Mathf.PI * 2f, 64, A(Navy, 0.9f));
                ground.Ring(c, gr, gu, R - 0.14f, R + 0.02f, 0, Mathf.PI * 2f, 64, A(WarnHot, pulse));
                for (int s = 0; s < 8; s++)
                {
                    float a = s * Mathf.PI / 4f + (float)now * 0.6f;
                    ground.Ring(c, gr, gu, R * p - 0.12f, R * p + 0.02f, a, a + 0.42f, 6, A(Navy, 0.6f));
                }
                // feat/ground-camera: from a low camera a ground ring is a thin line; a fading wall on the hitbox edge keeps
                // the slam readable at a grazing angle (the shape stays the hitbox: radius R exactly).
                if (CriticalMass.Runtime.Camera.GroundView.IsActive) Fence(air, c, R, 0f, Mathf.PI * 2f, 40, 1.6f, A(WarnHot, 0.55f * pulse));
            }
            if (k.MeleeWindupIn > 0 && CriticalMass.Runtime.Camera.GroundView.IsActive)
            {
                double kx = k.PrevX + (k.X - k.PrevX) * Alpha, kz = k.PrevZ + (k.Z - k.PrevZ) * Alpha;
                Fence(air, W(kx, Ground(kx, kz) + 0.05, kz), (float)(_cfg.Kaiju.MeleeRange + Kaiju.MeleeLeash), 0f, Mathf.PI * 2f, 36, 1.0f, A(WarnHot, 0.4f));
            }
            if (k.MeleeWindupIn > 0) DrawMeleeTelegraph(w, ground, now);
            else DrawPreWarnings(w, ground);
            if (k.State == KaijuState.Berserk)
            {
                double h = Ground(k.X, k.Z);
                Vector3 c = W(k.X, h + 0.05, k.Z);
                float R = (float)_cfg.Kaiju.Radius * 1.7f;
                float pulse = FeelGate.Pulse(now, PaceNow, BerserkPulseHz);
                if (FeelGate.ReduceFlashing) pulse = 0.5f + 0.2f * (pulse - 0.5f);
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

        /// <summary>The pace pulses use: the driver's pace when CombatFx set it, else the default match speed.</summary>
        private double PaceNow => Pace > 0 ? Pace : _speed;

        /// <summary>
        /// Physics audit M6 (view half): the Kaiju melee wind-up gets a ground telegraph for its whole length. The swing
        /// hits its locked target if that target is still within MeleeRange + MeleeLeash of the Kaiju when the wind-up runs
        /// out (Kaiju.ResolveMelee), so the shape drawn is that reach circle, a wedge toward the target and a disc under the
        /// target that fills as the wind-up runs out. The wind-up length itself is a sim number (GameConfig
        /// Kaiju.MeleeWindupTicks); lengthening it is the sim lane's change.
        /// </summary>
        private void DrawMeleeTelegraph(World w, FxMeshBuilder ground, double now)
        {
            Kaiju k = w.Kaiju;
            Vector3 gr = Vector3.right, gu = Vector3.forward;
            float reach = (float)(_cfg.Kaiju.MeleeRange + Kaiju.MeleeLeash);
            // fix/kaiju-alive-1: over the wind-up's real length (MeleeWindupLen, floored to 0.45 real s), not the config's 11 ticks.
            float p = Telegraphs.MeleeProgress(k.MeleeWindupIn, k.MeleeWindupLen > 0 ? k.MeleeWindupLen : _cfg.Kaiju.MeleeWindupTicks, Alpha);
            float pulse = 0.7f + 0.3f * FeelGate.Pulse(now, PaceNow, MeleePulseHz) * (FeelGate.ReduceFlashing ? 0.4f : 1f);
            double kx = k.PrevX + (k.X - k.PrevX) * Alpha, kz = k.PrevZ + (k.Z - k.PrevZ) * Alpha;
            Vector3 c = W(kx, Ground(kx, kz) + 0.05, kz);
            ground.Ring(c, gr, gu, reach - 0.12f, reach + 0.06f, 0, Mathf.PI * 2f, 48, A(Navy, 0.75f));
            ground.Ring(c, gr, gu, reach - 0.08f, reach + 0.02f, 0, Mathf.PI * 2f, 48, A(WarnHot, 0.55f * pulse));
            int t = k.MeleeTargetId;
            if (t < 0 || t >= w.ActorCount || !w.Actors[t].Alive) return;
            double tx = w.PrevX[t] + (w.X[t] - w.PrevX[t]) * Alpha, tz = w.PrevZ[t] + (w.Z[t] - w.PrevZ[t]) * Alpha;
            Vector3 tc = W(tx, Ground(tx, tz) + 0.06, tz);
            Vector3 d = tc - c;
            float ang = Mathf.Atan2(d.z, d.x);
            const float half = 0.42f;   // about 24 degrees each side: reads as "this one is coming at you"
            ground.Ring(c, gr, gu, 0.4f, reach, ang - half, ang + half, 10, A(WarnHot, 0.12f + 0.2f * p), A(Warn, 0.3f + 0.3f * p));
            ground.Ring(c, gr, gu, reach * p - 0.1f, reach * p + 0.04f, ang - half, ang + half, 8, A(Navy, 0.8f));
            float tr = (float)_cfg.Class(w.Actors[t].Cls).Radius * 1.6f + 0.2f;
            ground.Blob(tc, gr, gu, tr + 0.08f, null, 24, 0, A(Navy, 0.6f));
            ground.Blob(tc, gr, gu, tr, null, 24, 0, A(WarnHot, 0.18f));
            ground.Blob(tc, gr, gu, Mathf.Max(0.04f, tr * p), null, 24, 0, A(Warn, 0.35f + 0.35f * p * pulse));
        }

        /// <summary>
        /// Telegraphs.cs pre-warning stage: a faint reach ring and a thin wedge toward the target before the claw wind-up,
        /// a faint dashed slam ring before the slam telegraph, so the total warning meets the owner's minimums on screen.
        /// </summary>
        private void DrawPreWarnings(World w, FxMeshBuilder ground)
        {
            Kaiju k = w.Kaiju;
            Vector3 gr = Vector3.right, gu = Vector3.forward;
            double kx = k.PrevX + (k.X - k.PrevX) * Alpha, kz = k.PrevZ + (k.Z - k.PrevZ) * Alpha;
            Vector3 c = W(kx, Ground(kx, kz) + 0.045, kz);
            int t = Telegraphs.ClawPreWarning(w, PaceNow);
            if (t >= 0)
            {
                float reach = (float)(_cfg.Kaiju.MeleeRange + Kaiju.MeleeLeash);
                ground.Ring(c, gr, gu, reach - 0.06f, reach + 0.02f, 0, Mathf.PI * 2f, 48, A(WarnHot, 0.28f));
                double tx = w.PrevX[t] + (w.X[t] - w.PrevX[t]) * Alpha, tz = w.PrevZ[t] + (w.Z[t] - w.PrevZ[t]) * Alpha;
                Vector3 d = W(tx, 0, tz) - W(kx, 0, kz);
                float ang = Mathf.Atan2(d.z, d.x);
                ground.Ring(c, gr, gu, 0.4f, reach, ang - 0.42f, ang + 0.42f, 10, A(Warn, 0.06f), A(Warn, 0.14f));
            }
            if (Telegraphs.SlamPreWarning(w, PaceNow))
            {
                float R = (float)_cfg.Kaiju.SlamRadius;
                for (int s = 0; s < 16; s++)
                {
                    float a = s * Mathf.PI / 8f;
                    ground.Ring(c, gr, gu, R - 0.1f, R + 0.02f, a, a + 0.26f, 4, A(WarnHot, 0.32f));
                }
            }
        }

        /// <summary>
        /// feat/ground-camera: a vertical band standing on a ground circle (centre c, radius r, angles a0..a1), colour at the
        /// foot fading to clear at height h: the telegraph's vertical element for the low ground camera.
        /// </summary>
        private static void Fence(FxMeshBuilder b, Vector3 c, float r, float a0, float a1, int seg, float h, Color32 col)
        {
            Color32 top = col;
            top.a = 0;
            float step = (a1 - a0) / seg;
            for (int i = 0; i < seg; i++)
            {
                float a = a0 + (i + 0.5f) * step;
                Vector3 radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                Vector3 mid = c + radial * r + Vector3.up * (h * 0.5f);
                b.Quad(mid, tangent * (r * step * 0.5f * 1.02f), Vector3.up * (h * 0.5f), col, top);
            }
        }

        /// <summary>Flat dark blobs under every living fighter and the hatched Kaiju (Low: no shadow map), interpolated.</summary>
        private void DrawBlobShadows(World w, FxMeshBuilder ground)
        {
            Vector3 gr = Vector3.right, gu = Vector3.forward;
            Color32 col = A(Navy, BlobAlpha);
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (!w.Actors[i].Alive) continue;
                double x = w.PrevX[i] + (w.X[i] - w.PrevX[i]) * Alpha, z = w.PrevZ[i] + (w.Z[i] - w.PrevZ[i]) * Alpha;
                float r = (float)_cfg.Class(w.Actors[i].Cls).Radius * BlobScale;
                ground.Blob(W(x, Ground(x, z) + 0.02, z), gr, gu, r, null, 12, 0, col);
            }
            Kaiju k = w.Kaiju;
            if (k.State != KaijuState.Egg)
            {
                double x = k.PrevX + (k.X - k.PrevX) * Alpha, z = k.PrevZ + (k.Z - k.PrevZ) * Alpha;
                ground.Blob(W(x, Ground(x, z) + 0.02, z), gr, gu, (float)_cfg.Kaiju.Radius * 1.1f, null, 20, 0, col);
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
                float pulse = 0.75f + 0.25f * (FeelGate.Pulse(now + i * 0.11, PaceNow, ShieldPulseHz) * 2f - 1f) * (FeelGate.ReduceFlashing ? 0.4f : 1f);
                air.Ring(c, Vector3.right, Vector3.forward, 0.66f, 0.86f, centre - (float)half, centre + (float)half, 10, A(Navy, 0.85f));
                air.Ring(c, Vector3.right, Vector3.forward, 0.69f, 0.83f, centre - (float)half + 0.03f, centre + (float)half - 0.03f, 10, A(team, 0.75f * pulse));
                air.Ring(c + Vector3.up * 0.01f, Vector3.right, Vector3.forward, 0.79f, 0.83f, centre - (float)half + 0.05f, centre + (float)half - 0.05f, 10, A(SparkWhite, 0.8f * pulse));
            }
        }
    }
}
