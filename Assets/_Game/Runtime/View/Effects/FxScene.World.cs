using System;
using CriticalMass.Runtime.View.Dressing;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>
    /// World reactions (v2), all decoded per sim tick from positions the sim already has (OnTickState), so a replay draws
    /// the same frame: footstep puffs on speed bursts and landings, plank dust under heavy actors (Warrior, Kaiju) on
    /// bridges, Kaiju stomps, grass and bush rustle near impacts and passing actors (leaf flecks + the _CMFxImpulses
    /// shader hook for real sway), water splashes when debris or gibs fall into water or over the rim, biomass sparkles
    /// that home into the carrier, and corruption's own oily look (hollow bubbles, concentric puddles, teal sheen) that
    /// never uses blood's lobes or biomass's stars.
    /// </summary>
    public sealed partial class FxScene
    {
        /// <summary>A footstep puff needs this many times the class move speed (dashes, knock-back, any future sprint).</summary>
        public const double SprintFactor = 1.15;
        /// <summary>Blood drips under actors below this share of max health, one every DripTicks while they move.</summary>
        public const double HurtShare = 0.25;
        public const int DripTicks = 10, BubbleTicks = 12;

        private struct Mote { public double Born, Life; public Vector3 P0, Ctrl; public float Size; public int Target; }
        private struct Bubble { public double Born, Life; public Vector3 P0; public float Size, Rise; public uint Seed; }
        private struct Leaf { public double Born, Life; public Vector3 P0, V; public float Size, Spin; public Color32 Col; }

        private readonly Mote[] _motes = new Mote[MaxMotes];
        private readonly Bubble[] _bubbles = new Bubble[MaxBubbles];
        private readonly Leaf[] _leaves = new Leaf[MaxLeaves];
        private int _nmo, _nbu, _nle;

        private double[] _px, _pz, _stride;
        private bool[] _hasPrev;
        private double _kStride;
        private bool _kHasPrev;
        private double _kpx, _kpz;
        private int _kFoot;

        private byte[] _foliage;
        private int[] _foliageTick;

        public static readonly Color32 GrassLight = new Color32(150, 206, 84, 255), GrassMid = new Color32(96, 160, 56, 255), BushMid = new Color32(70, 138, 62, 255);
        public static readonly Color32 Petal = new Color32(255, 170, 196, 255);

        private void InitWorld()
        {
            for (int i = 0; i < MaxMotes; i++) _motes[i].Born = double.NegativeInfinity;
            for (int i = 0; i < MaxBubbles; i++) _bubbles[i].Born = double.NegativeInfinity;
            for (int i = 0; i < MaxLeaves; i++) _leaves[i].Born = double.NegativeInfinity;
            _px = new double[_cap];
            _pz = new double[_cap];
            _stride = new double[_cap];
            _hasPrev = new bool[_cap];
        }

        private int ActiveWorld(double now)
        {
            int n = 0;
            for (int i = 0; i < MaxMotes; i++) if (now - _motes[i].Born < _motes[i].Life) n++;
            for (int i = 0; i < MaxBubbles; i++) if (now - _bubbles[i].Born < _bubbles[i].Life) n++;
            for (int i = 0; i < MaxLeaves; i++) if (now - _leaves[i].Born < _leaves[i].Life) n++;
            return n;
        }

        public int BubbleCount(double now)
        {
            int n = 0;
            for (int i = 0; i < MaxBubbles; i++) if (now - _bubbles[i].Born < _bubbles[i].Life) n++;
            return n;
        }

        public int MoteCount(double now)
        {
            int n = 0;
            for (int i = 0; i < MaxMotes; i++) if (now - _motes[i].Born < _motes[i].Life) n++;
            return n;
        }

        public int LeafCount(double now)
        {
            int n = 0;
            for (int i = 0; i < MaxLeaves; i++) if (now - _leaves[i].Born < _leaves[i].Life) n++;
            return n;
        }

        // ---- foliage (the dressed world merges grass and bushes into static chunk meshes, so they are found by cell) ----

        /// <summary>Per map cell: 0 none, 1 grass or flowers, 2 bush or tree, 3 flowers. Built once from the dresser's plan.</summary>
        public static byte[] FoliageMask(WorldPlan plan, MapDef map)
        {
            if (plan == null || map == null) return null;
            var mask = new byte[map.Width * map.Height];
            foreach (WorldPlacement p in plan.Placements)
            {
                string n = p.Piece;
                if (n == null) continue;
                byte kind = n.Contains("flower") ? (byte)3 : n.Contains("grass") || n.Contains("fern") || n.Contains("reed") ? (byte)1 :
                    n.Contains("bush") || n.Contains("tree") || n.Contains("shrub") ? (byte)2 : (byte)0;
                if (kind == 0) continue;
                int cx = (int)Math.Floor(p.SimX), cz = (int)Math.Floor(p.SimZ);
                if (cx < 0 || cz < 0 || cx >= map.Width || cz >= map.Height) continue;
                int i = cz * map.Width + cx;
                if (mask[i] == 0 || kind == 2) mask[i] = kind;
            }
            return mask;
        }

        /// <summary>Hand the scene the foliage mask (CombatFx does it once the world art is built; null clears it).</summary>
        public void SetFoliage(byte[] mask)
        {
            _foliage = mask != null && _map.Cells != null && mask.Length == _map.Cells.Length ? mask : null;
            if (_foliage != null && (_foliageTick == null || _foliageTick.Length != _foliage.Length))
            {
                _foliageTick = new int[_foliage.Length];
                for (int i = 0; i < _foliageTick.Length; i++) _foliageTick[i] = int.MinValue / 2;
            }
        }

        public bool HasFoliage => _foliage != null;

        /// <summary>Leaves pop off the tuft or bush in cell i (throttled per cell to one rustle per 0.5 s wall).</summary>
        private void Rustle(int i, double t, uint seed)
        {
            if (_foliage == null || i < 0 || i >= _foliage.Length) return;
            byte kind = _foliage[i];
            if (kind == 0) return;
            int tick = (int)Math.Round(t * _hz);
            if (tick - _foliageTick[i] < (int)Math.Round(S(0.5) * _hz)) return;
            _foliageTick[i] = tick;
            double x = i % _map.Width + 0.5, z = i / _map.Width + 0.5;
            double h = Ground(x, z);
            Vector3 p = W(x, h + (kind == 2 ? 0.6 : 0.25), z);
            int n = kind == 2 ? 3 : 2;
            for (int k = 0; k < n; k++)
            {
                ref Leaf l = ref _leaves[_nle++ % MaxLeaves];
                uint s = Hash(seed, (uint)i, (uint)k);
                float a = R01(s, 1) * Mathf.PI * 2f;
                l.Born = t;
                l.P0 = p + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.15f;
                l.V = new Vector3(Mathf.Cos(a) * (0.8f + R01(s, 2)), 1.6f + R01(s, 3) * 1.4f, Mathf.Sin(a) * (0.8f + R01(s, 2)));
                l.Size = 0.07f + R01(s, 4) * 0.05f;
                l.Spin = (R01(s, 5) - 0.5f) * 14f;
                l.Life = S(0.7 + 0.3 * R01(s, 6));
                l.Col = kind == 3 && k == 0 ? Petal : kind == 2 ? BushMid : (k & 1) == 0 ? GrassLight : GrassMid;
            }
        }

        /// <summary>Rustle the foliage within r of sim (x, z) (at most ten cells per call).</summary>
        private void RustleArea(double x, double z, double r, double t, uint seed)
        {
            if (_foliage == null) return;
            r = Math.Min(r, 4.0);
            int x0 = Math.Max(0, (int)Math.Floor(x - r)), x1 = Math.Min(_map.Width - 1, (int)Math.Floor(x + r));
            int z0 = Math.Max(0, (int)Math.Floor(z - r)), z1 = Math.Min(_map.Height - 1, (int)Math.Floor(z + r));
            int n = 0;
            for (int cz = z0; cz <= z1 && n < 10; cz++)
            for (int cx = x0; cx <= x1 && n < 10; cx++)
            {
                int i = cz * _map.Width + cx;
                if (_foliage[i] == 0) continue;
                double dx = cx + 0.5 - x, dz = cz + 0.5 - z;
                if (dx * dx + dz * dz > r * r) continue;
                Rustle(i, t, seed);
                n++;
            }
        }

        private int CellIndex(double x, double z)
        {
            int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
            if (cx < 0 || cz < 0 || cx >= _map.Width || cz >= _map.Height) return -1;
            return cz * _map.Width + cx;
        }

        // ---- per tick ----

        /// <summary>
        /// Once per sim tick after the event decode (CombatFx.OnTicked): strides, footsteps, plank dust, foliage brushed by
        /// actors, blood drips behind badly hurt actors, bubbles off corruption carriers, Kaiju stomps. Reads the world only.
        /// </summary>
        public void OnTickState(World w)
        {
            double t = T(w.Tick);
            int count = Math.Min(w.ActorCount, _cap);
            for (int i = 0; i < count; i++)
            {
                Actor a = w.Actors[i];
                double x = w.X[i], z = w.Z[i];
                if (!a.Alive) { _hasPrev[i] = false; _stride[i] = 0; continue; }
                if (!_hasPrev[i]) { _px[i] = x; _pz[i] = z; _hasPrev[i] = true; continue; }
                double dx = x - _px[i], dz = z - _pz[i];
                double d = Math.Sqrt(dx * dx + dz * dz);
                _px[i] = x;
                _pz[i] = z;
                if (d > 3.0 || d < 1e-6) continue; // a teleport (respawn) or standing still
                double speed = d * _hz;
                ClassConfig cc = _cfg.Class(a.Cls);
                bool heavy = a.Cls == ClassId.Warrior;
                double strideLen = heavy ? 1.1 : 0.9;
                _stride[i] += d;
                uint seed = Hash((uint)w.Tick, (uint)i, 0x51u);
                if (_stride[i] >= strideLen)
                {
                    _stride[i] = _stride[i] >= 2 * strideLen ? 0 : _stride[i] - strideLen;
                    byte cell = CellAt(x, z);
                    double h = Ground(x, z);
                    if (cell == Cell.Bridge && heavy) PlankDust(t, x, z, h, seed, 1f);
                    else if (speed > cc.MoveSpeed * SprintFactor) FootPuff(t, x, z, h, seed, dx / d, dz / d);
                    if (_foliage != null) Rustle(CellIndex(x, z), t, seed);
                }
                if (Blood && a.MaxHp > 0 && a.Hp < a.MaxHp * HurtShare && (w.Tick + i * 5) % DripTicks == 0)
                {
                    double bx = x - dx / d * 0.25, bz = z - dz / d * 0.25;
                    if (SurfaceTop(bx, bz, out double top)) AddDrip(t, W(bx, top, bz), seed);
                }
                if (a.Carry == CarryKind.Corruption && (w.Tick + i * 3) % BubbleTicks == 0)
                {
                    double h = Ground(x, z);
                    AddBubble(t, W(x, h + 1.25, z), seed, 0.1f + 0.06f * R01(seed, 9), 1.3f, S(0.75));
                }
            }

            Kaiju k = w.Kaiju;
            if (k.State == KaijuState.Egg) { _kHasPrev = false; return; }
            if (!_kHasPrev) { _kpx = k.X; _kpz = k.Z; _kHasPrev = true; return; }
            double kdx = k.X - _kpx, kdz = k.Z - _kpz, kd = Math.Sqrt(kdx * kdx + kdz * kdz);
            _kpx = k.X;
            _kpz = k.Z;
            if (kd > 4.0 || kd < 1e-6) return;
            _kStride += kd;
            if (_kStride < 2.6) return;
            _kStride = 0;
            // A Kaiju stomp: alternate feet either side of the path, a dust ring, puffs, rustle, plank dust on bridges.
            _kFoot ^= 1;
            double side = (_kFoot == 0 ? 1 : -1) * _cfg.Kaiju.Radius * 0.45;
            double fx = k.X + (-kdz / kd) * side, fz = k.Z + (kdx / kd) * side;
            double fh = Ground(fx, fz);
            uint ks = Hash((uint)w.Tick, 0xBEEFu, 0x52u);
            Vector3 c = W(fx, fh + 0.05, fz);
            AddRing(t, c, 0.3f, 2.0f, 0.16f, S(0.26), DustMid, 1);
            for (int n = 0; n < 4; n++) AddPuff(t, c + Vector3.up * 0.15f, ks, (uint)n, 0.45f, DustMid, 1);
            if (CellAt(fx, fz) == Cell.Bridge) PlankDust(t, fx, fz, fh, ks, 1.8f);
            RustleArea(fx, fz, 2.5, t, ks);
            AddImpulse(t, fx, fh, fz, 0.5f);
        }

        private void FootPuff(double t, double x, double z, double h, uint seed, double dirX, double dirZ)
        {
            Vector3 p = W(x - dirX * 0.2, h + 0.08, z - dirZ * 0.2);
            AddPuff(t, p, seed, 1, 0.22f, DustLight, 2, -dirX, -dirZ);
            AddPuff(t, p, seed, 2, 0.18f, DustMid, 0);
        }

        private void PlankDust(double t, double x, double z, double h, uint seed, float scale)
        {
            Vector3 p = W(x, h + 0.06, z);
            for (int k = 0; k < 3; k++) AddPuff(t, p, seed, (uint)(k + 7), 0.2f * scale, WoodMid, 3);
            AddSpark(t, p, 0, 0, seed, 11, 7);
        }

        // ---- resources ----

        private void SpawnResource(ref FxEvent e, World w, double t, uint seed)
        {
            double h = Ground(e.X, e.Z);
            bool valid = e.A >= 0 && e.A < w.ActorCount;
            if (e.Kind == FxKind.DeliverCorruption)
            {
                double px = valid ? w.X[e.A] : e.X, pz = valid ? w.Z[e.A] : e.Z;
                double ph = Ground(px, pz);
                // Corruption's own look: an oily puddle of concentric rings, a gush of hollow bubbles. No stars, no lobes.
                if (IsGround(px, pz)) AddMark(t, W(px, ph, pz), 1.0f, 2, seed, S(0.25), S(9.0));
                for (int k = 0; k < 8; k++)
                {
                    uint s = Hash(seed, (uint)k, 0x77u);
                    float a = R01(s, 1) * Mathf.PI * 2f;
                    Vector3 p = W(px, ph + 0.3 + 0.5 * R01(s, 2), pz) + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (0.5f * R01(s, 3));
                    AddBubble(t + S(0.03 * k), p, s, 0.12f + 0.1f * R01(s, 4), 1.6f + R01(s, 5), S(0.6 + 0.3 * R01(s, 6)));
                }
                return;
            }
            if (e.Kind == FxKind.DeliverBiomass)
            {
                double px = valid ? w.X[e.A] : e.X, pz = valid ? w.Z[e.A] : e.Z;
                Vector3 c = W(px, h + 0.9, pz);
                AddRing(t, W(px, h + 0.06, pz), 0.2f, 1.5f, 0.14f, S(0.35), BiomassC, 0);
                AddBurst(t, c, 0.55f, 3, seed, Vector3.zero, S(0.16));
                for (int k = 0; k < 7; k++) AddSpark(t, c, 0, 0, seed, (uint)k, 3);
                return;
            }
            // Gather: a corruption pickup bubbles; a biomass pickup sends sparkles along a curve into the carrier.
            if (valid && w.Actors[e.A].Carry == CarryKind.Corruption)
            {
                for (int k = 0; k < 4; k++)
                {
                    uint s = Hash(seed, (uint)k, 0x78u);
                    AddBubble(t + S(0.04 * k), W(e.X, h + 0.4, e.Z), s, 0.1f + 0.06f * R01(s, 4), 1.4f, S(0.6));
                }
                return;
            }
            AddRing(t, W(e.X, h + 0.06, e.Z), 0.2f, 0.9f, 0.14f, S(0.35), BiomassC, 0);
            AddBurst(t, W(e.X, h + 0.7, e.Z), 0.35f, 3, seed, Vector3.zero, S(0.16));
            for (int k = 0; k < 5; k++)
            {
                ref Mote m = ref _motes[_nmo++ % MaxMotes];
                uint s = Hash(seed, (uint)k, 0x79u);
                float a = R01(s, 1) * Mathf.PI * 2f;
                m.Born = t + S(0.045 * k);
                m.Life = S(0.5);
                m.P0 = W(e.X, h + 0.5, e.Z) + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.3f;
                m.Ctrl = new Vector3(Mathf.Cos(a) * 1.1f, 1.4f + R01(s, 2), Mathf.Sin(a) * 1.1f);
                m.Size = 0.1f + 0.05f * R01(s, 3);
                m.Target = valid ? e.A : -1;
            }
        }

        private void AddBubble(double t, Vector3 p, uint seed, float size, float rise, double life)
        {
            ref Bubble b = ref _bubbles[_nbu++ % MaxBubbles];
            b.Born = t; b.P0 = p; b.Seed = seed; b.Size = size; b.Rise = rise; b.Life = life;
        }

        // ---- splash and landing ----

        private void SpawnSplash(double t, double x, double z, float size, uint seed)
        {
            float y = CellAt(x, z) == Cell.Water ? WaterTop : (float)Ground(x, z);
            Vector3 c = W(x, y + 0.02, z);
            AddRing(t, c, 0.15f * size, 1.2f * size, 0.14f, S(0.4), WaterMid, 4);
            AddRing(t + S(0.08), c, 0.1f, 0.8f * size, 0.1f, S(0.35), WaterLight, 4);
            for (int k = 0; k < 6; k++) AddSpark(t, c + Vector3.up * 0.05f, 0, 0, seed, (uint)(k + 80), 6);
            for (int k = 0; k < 3; k++) AddPuff(t, c + Vector3.up * 0.1f, seed, (uint)(k + 90), 0.3f * size, WaterLight, 4);
        }

        private void SpawnLanding(double t, double x, double z, bool heavy, uint seed)
        {
            double h = Ground(x, z);
            Vector3 c = W(x, h + 0.08, z);
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI / 3f;
                AddPuff(t, c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.3f, seed, (uint)(k + 110), heavy ? 0.3f : 0.24f, DustLight, 0);
            }
            if (heavy) AddRing(t, c, 0.2f, 1.2f, 0.12f, S(0.22), DustMid, 1);
            if (CellAt(x, z) == Cell.Bridge) PlankDust(t, x, z, h, seed, heavy ? 1.4f : 1f);
            if (_foliage != null) Rustle(CellIndex(x, z), t, seed);
        }

        // ---- drawing ----

        private void BuildWorldAir(double now, World w, FxMeshBuilder air, Vector3 camRight, Vector3 camUp)
        {
            // Biomass motes: four-point sparkles along a curve into the carrier, two ghost sparkles behind. 36 verts.
            for (int i = 0; i < MaxMotes; i++)
            {
                ref Mote m = ref _motes[i];
                double age = now - m.Born;
                if (age < 0 || age >= m.Life) continue;
                float u = (float)(age / m.Life);
                Vector3 end = m.P0 + Vector3.up * 0.8f;
                if (m.Target >= 0 && m.Target < w.ActorCount && w.Actors[m.Target].Alive)
                    end = W(w.X[m.Target], Ground(w.X[m.Target], w.Z[m.Target]) + 1.1, w.Z[m.Target]);
                float fade = u < 0.85f ? 1f : 1f - (u - 0.85f) / 0.15f;
                for (int g = 2; g >= 0; g--)
                {
                    float uu = Mathf.Clamp01(u - g * 0.09f);
                    float e = uu * uu * (3f - 2f * uu);
                    Vector3 a = Vector3.Lerp(m.P0, m.P0 + m.Ctrl, e), b = Vector3.Lerp(m.P0 + m.Ctrl, end, e);
                    Vector3 p = Vector3.Lerp(a, b, e);
                    float sz = m.Size * (1f - 0.3f * g) * (1f - 0.4f * u);
                    if (g == 0) air.Star(p, camRight, camUp, sz + 0.04f, 0.4f, 4, 0f, A(Navy, fade));
                    air.Star(p, camRight, camUp, sz, 0.4f, 4, 0f, A(g == 0 ? BiomassC : SparkWhite, fade * (1f - 0.3f * g)));
                }
            }

            // Corruption bubbles: hollow purple rings with a navy rim and a teal sheen, wobbling up, popping into four ticks.
            const float tau = Mathf.PI * 2f;
            for (int i = 0; i < MaxBubbles; i++)
            {
                ref Bubble b = ref _bubbles[i];
                double age = now - b.Born;
                if (age < 0 || age >= b.Life) continue;
                float u = (float)(age / b.Life);
                float a = (float)age;
                Vector3 p = b.P0 + Vector3.up * (b.Rise * a) + camRight * (0.07f * Mathf.Sin(a * 9f + (b.Seed & 255)));
                float r = b.Size * (0.7f + 0.5f * u);
                if (u < 0.85f)
                {
                    air.Ring(p, camRight, camUp, r, r + 0.035f, 0, tau, 10, Navy);
                    air.Ring(p, camRight, camUp, r * 0.55f, r, 0, tau, 10, u < 0.4f ? CorruptionC : CorruptDark);
                    air.Ring(p, camRight, camUp, r * 0.6f, r * 0.85f, 0.7f, 1.7f, 4, OilSheen);
                }
                else
                {
                    float pu = (u - 0.85f) / 0.15f;
                    for (int k = 0; k < 4; k++)
                    {
                        float ang = k * Mathf.PI * 0.5f + 0.785f;
                        Vector3 dir = camRight * Mathf.Cos(ang) + camUp * Mathf.Sin(ang);
                        Vector3 perp = camRight * -Mathf.Sin(ang) + camUp * Mathf.Cos(ang);
                        air.Quad(p + dir * (r * (1f + pu)), dir * (r * 0.3f), perp * 0.025f, A(CorruptLight, 1f - pu), A(CorruptLight, 1f - pu));
                    }
                }
            }

            // Leaves and petals: little outlined slivers that flip and flutter down. 14 verts.
            for (int i = 0; i < MaxLeaves; i++)
            {
                ref Leaf l = ref _leaves[i];
                double age = now - l.Born;
                if (age < 0 || age >= l.Life) continue;
                float u = (float)(age / l.Life);
                float a = (float)age;
                float drag = 1f - Mathf.Exp(-3f * a);
                Vector3 p = l.P0 + new Vector3(l.V.x, 0, l.V.z) * (drag / 3f) + Vector3.up * (l.V.y * (drag / 3f) - 0.6f * a * a) +
                            camRight * (0.08f * Mathf.Sin(a * 8f + i));
                float th = l.Spin * a;
                Vector3 rr = camRight * Mathf.Cos(th) + camUp * Mathf.Sin(th);
                Vector3 ru = camRight * -Mathf.Sin(th) + camUp * Mathf.Cos(th);
                float fade = u < 0.7f ? 1f : 1f - (u - 0.7f) / 0.3f;
                air.Blob(p, rr * 1.7f, ru, l.Size + 0.025f, null, 6, 0, A(Navy, fade * 0.9f));
                air.Blob(p, rr * 1.7f, ru, l.Size, null, 6, 0, A(l.Col, fade));
            }
        }
    }
}
