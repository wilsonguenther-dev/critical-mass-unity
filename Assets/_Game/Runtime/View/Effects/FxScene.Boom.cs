using System;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>
    /// Explosions (v2), one layered toon recipe with clear timing (wall seconds at the default speed):
    /// <list type="bullet">
    /// <item>0.000-0.034 white core flash (two frames; at most one flash per 0.34 s, WCAG 2.3.1: never three a second)</item>
    /// <item>0.000-0.300 view-only light pulse: an additive warm disc on the ground and a glow billboard (no real light)</item>
    /// <item>0.000-0.600 the fireball (polish-3): one soft core that scales up fast with three merged lobes, a shared ramp
    /// from a yellow heart through orange to a dark-red edge and soft halo, fading out by 0.6</item>
    /// <item>0.150-2.800 a smoke column of six stepped-grey puffs (dust tones for walls and the slam)</item>
    /// <item>shock ring, debris chunks that bounce once, embers, ground dust, a scorch decal that lingers 14 s</item>
    /// <item>screen trauma by distance from the local player, applied only when cm.shake is on (CombatFx)</item>
    /// </list>
    /// Long-lived ground marks (slam cracks, scorch, corruption puddles) share the Marks pool here.
    /// </summary>
    public sealed partial class FxScene
    {
        private struct Explosion { public double Born; public Vector3 P; public float R; public ExplosionKind Kind; public uint Seed; public bool Flash; }
        private struct Mark { public double Born, Life, Grow; public Vector3 P; public float Size, Rot; public uint Seed; public byte Kind; }

        private readonly Explosion[] _booms = new Explosion[MaxExplosions];
        private readonly Mark[] _marks = new Mark[MaxMarks];
        private int _nx, _nm;
        private double _lastFlash = double.NegativeInfinity;

        private double BoomLife => S(2.8);
        private double TraumaLife => S(0.55);

        private void InitBoom()
        {
            for (int i = 0; i < MaxExplosions; i++) _booms[i].Born = double.NegativeInfinity;
            for (int i = 0; i < MaxMarks; i++) { _marks[i].Born = double.NegativeInfinity; _marks[i].Life = 0; }
        }

        private int ActiveBoom(double now)
        {
            int n = 0;
            for (int i = 0; i < MaxExplosions; i++) if (now - _booms[i].Born < BoomLife) n++;
            for (int i = 0; i < MaxMarks; i++) if (now - _marks[i].Born < _marks[i].Life) n++;
            return n;
        }

        public int ExplosionCount(double now)
        {
            int n = 0;
            for (int i = 0; i < MaxExplosions; i++) if (now - _booms[i].Born < BoomLife) n++;
            return n;
        }

        private static bool HasFire(ExplosionKind k) => k == ExplosionKind.Tnt || k == ExplosionKind.Mine || k == ExplosionKind.Barrel;

        /// <summary>Peak screen trauma per kind (0..1) before the distance falloff.</summary>
        public static float TraumaPeak(ExplosionKind k)
        {
            switch (k)
            {
                case ExplosionKind.Tnt: return 0.65f;
                case ExplosionKind.Mine: return 0.5f;
                case ExplosionKind.Barrel: return 0.55f;
                case ExplosionKind.WallBreak: return 0.25f;
                default: return 0.8f;
            }
        }

        private void AddMark(double t, Vector3 p, float size, byte kind, uint seed, double grow, double life)
        {
            _markStamp[_nm % MaxMarks] = -1;
            ref Mark m = ref _marks[_nm++ % MaxMarks];
            m.Born = t; m.P = p; m.Size = size; m.Kind = kind; m.Seed = seed; m.Grow = grow; m.Life = life;
            m.Rot = R01(seed, 11) * Mathf.PI * 2f;
        }

        /// <summary>
        /// Screen trauma 0..1 at `now` for a listener at sim (lx, lz): the strongest live explosion, falling off from full
        /// within its radius + 3 u to nothing at 30 u, decaying quadratically over 0.55 s. CombatFx squares it into a shake
        /// only when the player has cm.shake on.
        /// </summary>
        public float Trauma(double now, double lx, double lz)
        {
            float best = 0f;
            double life = TraumaLife;
            for (int i = 0; i < MaxExplosions; i++)
            {
                ref Explosion ex = ref _booms[i];
                double age = now - ex.Born;
                if (age < 0 || age >= life) continue;
                double dx = SimX(ex.P) - lx, dz = SimZ(ex.P) - lz;
                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                float near = ex.R + 3f, far = 30f;
                float fall = d <= near ? 1f : d >= far ? 0f : 1f - (d - near) / (far - near);
                float k = 1f - (float)(age / life);
                float v = TraumaPeak(ex.Kind) * Mathf.Clamp(ex.R / 2f, 0.6f, 1.4f) * fall * fall * k * k;
                if (v > best) best = v;
            }
            return Mathf.Clamp01(best);
        }

        private bool RecentExplosionNear(double t, double x, double z)
        {
            double win = 2.0 / _hz + 1e-9;
            for (int i = 0; i < MaxExplosions; i++)
            {
                ref Explosion ex = ref _booms[i];
                if (Math.Abs(ex.Born - t) > win || ex.Kind == ExplosionKind.WallBreak) continue;
                double dx = SimX(ex.P) - x, dz = SimZ(ex.P) - z, r = ex.R * 1.3;
                if (dx * dx + dz * dz <= r * r) return true;
            }
            return false;
        }

        /// <summary>smoke-look-1 capture (-smokeShots): clears every effect, then starts one explosion that is already
        /// ageSeconds old at sim time now (view only; the sim never hears of it).</summary>
        public void PreviewExplosion(double now, double ageSeconds, double x, double z, float R, ExplosionKind kind, World w)
        {
            Clear();
            _lastFlash = double.NegativeInfinity;
            SpawnExplosion(now - S(ageSeconds), x, z, R, kind, 7331u, w);
        }

        private void SpawnExplosion(double t, double x, double z, float R, ExplosionKind kind, uint seed, World w)
        {
            double h = Ground(x, z);
            Vector3 c = W(x, h, z);
            bool fire = HasFire(kind);
            ref Explosion ex = ref _booms[_nx++ % MaxExplosions];
            ex.Born = t; ex.P = c; ex.R = R; ex.Kind = kind; ex.Seed = seed;
            ex.Flash = fire && t - _lastFlash >= S(0.34);
            if (ex.Flash) _lastFlash = t;
            Vector3 up = Vector3.up;

            if (kind == ExplosionKind.KaijuSlam)
            {
                // The v1 slam (shockwave ring, crack, dust ring, rock chunks) plus the v2 dust column, pulse and trauma.
                AddRing(t, c + up * 0.05f, 0.4f, R * 1.15f, 0.55f, S(0.32), DustLight, 0);
                AddRing(t + S(0.06), c + up * 0.05f, 0.2f, R * 0.8f, 0.3f, S(0.3), Warn, 0);
                AddMark(t, c, R * 0.75f, 0, seed, S(0.08), S(7.0));
                for (int k = 0; k < 14; k++)
                {
                    float a = (k + R01(seed, (uint)k) * 0.6f) / 14f * Mathf.PI * 2f;
                    Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * R * (0.75f + 0.3f * R01(seed, (uint)k + 90));
                    p.y = c.y + 0.25f;
                    AddPuff(t + S(0.03 * (k % 3)), p, seed, (uint)k, 0.7f, k % 2 == 0 ? DustLight : DustMid, 1);
                }
                for (int k = 0; k < 12; k++) AddChunk(t, c + up * 0.2f, R * 0.55f, seed, (uint)k, 0, 1.0f, 1f);
            }
            else
            {
                AddRing(t, c + up * 0.05f, 0.3f * R, 1.7f * R, 0.5f, S(0.3), fire ? DustLight : DustMid, 0);
                if (fire) AddRing(t + S(0.04), c + up * 0.06f, 0.2f * R, 1.2f * R, 0.24f, S(0.26), WarnHot, 0);
                int n = kind == ExplosionKind.WallBreak ? 12 : kind == ExplosionKind.Mine ? 10 : kind == ExplosionKind.Barrel ? 9 : 8;
                n = Math.Min(14, n + (int)(R * 0.8f));
                float speed = Mathf.Clamp(0.8f + 0.25f * R, 0.8f, 1.8f);
                float scale = Mathf.Clamp(0.7f + 0.15f * R, 0.7f, 1.4f);
                for (int k = 0; k < n; k++)
                {
                    byte mat = kind == ExplosionKind.WallBreak ? (byte)0 : kind == ExplosionKind.Mine ? (k % 3 == 0 ? (byte)0 : (byte)2) :
                        kind == ExplosionKind.Barrel ? (byte)1 : (k % 2 == 0 ? (byte)1 : (byte)3);
                    AddChunk(t, c + up * (kind == ExplosionKind.WallBreak ? 0.9f : 0.3f), R * 0.35f, seed, (uint)k, mat, scale, speed);
                }
                int embers = kind == ExplosionKind.Mine ? 5 : fire ? 8 : 0;
                for (int k = 0; k < embers; k++) AddSpark(t + S(0.04), c + up * (0.6f * R), 0, 0, seed, (uint)(k + 30), 5);
                if (kind == ExplosionKind.Mine) for (int k = 0; k < 6; k++) AddSpark(t, c + up * 0.2f, 0, 0, seed, (uint)(k + 50), 7);
                for (int k = 0; k < 8; k++)
                {
                    float a = (k + R01(seed, (uint)k + 70)) / 8f * Mathf.PI * 2f;
                    Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (0.6f * R) + up * 0.2f;
                    AddPuff(t + S(0.02 * (k % 3)), p, seed, (uint)(k + 70), Mathf.Min(0.5f + 0.1f * R, BlastPuffMax), kind == ExplosionKind.Mine ? DirtLight : DustMid, 1);
                }
                if (fire && IsGround(x, z)) AddMark(t, c, 0.95f * R, 1, seed, S(0.06), S(14.0));
            }
            GibRecentDeaths(t, x, z, R * 1.3);
            RustleArea(x, z, 1.6 * R, t, seed);
        }

        // ---- drawing ----

        private void BuildMarks(double now, FxMeshBuilder ground)
        {
            const float lift = 0.028f;
            for (int i = 0; i < MaxMarks; i++)
            {
                ref Mark m = ref _marks[i];
                double age = now - m.Born;
                if (age < 0 || age >= m.Life) continue;
                float grow = m.Grow > 0 ? Mathf.Clamp01((float)(age / m.Grow)) : 1f;
                grow = 1f - (1f - grow) * (1f - grow);
                float fade = (float)Math.Min(1.0, (m.Life - age) / (m.Life * 0.3));
                Vector3 c = m.P + Vector3.up * lift;
                float s = m.Size * grow;
                // Cracks and scorch go to the ground paint once still; corruption puddles animate, so they stay here.
                bool still = m.Kind <= 1 && age >= m.Grow && fade >= 1f;
                int before = _markStamp[i];
                if (PaintedAway(ref _markStamp[i], m.P, still)) continue;
                if (before == -1 && _markStamp[i] >= 0)
                {
                    if (m.Kind == 0) DrawCrack(Paint.Builder, m.P, m.Size, m.Rot, m.Seed, 1f);
                    else DrawScorch(Paint.Builder, m.P, m.Size, m.Rot, m.Seed, 1f);
                }
                LastGroundMarks++;
                if (m.Kind == 0) DrawCrack(ground, c, s, m.Rot, m.Seed, fade);
                else if (m.Kind == 1) DrawScorch(ground, c, s, m.Rot, m.Seed, fade);
                else DrawPuddle(ground, c, s, m.Rot, m.Seed, fade, now);
            }
        }

        /// <summary>Slam crack (v1): a scorched dust disc, nine jagged navy cracks with a pale lip. 166 verts.</summary>
        private void DrawCrack(FxMeshBuilder g, Vector3 c, float size, float rot, uint seed, float fade)
        {
            Vector3 gr = Vector3.right, gu = Vector3.forward;
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

        /// <summary>Scorch: a ragged soot blot with ten burn rays and a darker heart, two flat greys. 77 verts.</summary>
        private void DrawScorch(FxMeshBuilder g, Vector3 c, float s, float rot, uint seed, float fade)
        {
            Vector3 gr = Vector3.right, gu = Vector3.forward;
            Color32 outer = A(ScorchOuter, fade * 0.62f), inner = A(ScorchInner, fade * 0.78f);
            for (int k = 0; k < 10; k++)
            {
                float a = rot + k * Mathf.PI * 0.2f + (R01(seed, (uint)k + 400) - 0.5f) * 0.3f;
                float len = s * (1.05f + 0.5f * R01(seed, (uint)k + 410));
                Vector3 dir = gr * Mathf.Cos(a) + gu * Mathf.Sin(a);
                Vector3 side = Vector3.Cross(Vector3.up, dir) * (s * 0.07f);
                g.Quad(c + dir * (len * 0.55f), dir * (len * 0.45f), side, outer, outer);
            }
            Lobes(seed, 420, 14, 0.75f, 1.15f);
            g.Blob(c, gr, gu, s, _radii, 14, rot, outer);
            Lobes(seed, 440, 12, 0.8f, 1.1f);
            g.Blob(c, gr, gu, s * 0.55f, _radii, 12, rot + 0.6f, inner);
            g.Blob(c, gr, gu, s * 0.24f, null, 8, 0, A(ScorchInner, fade * 0.9f));
        }

        /// <summary>
        /// Corruption puddle: concentric perfect rings (never blood's lobes, never biomass's stars), an oily teal sheen and
        /// three bubbles that swell and pop on a loop. 237 verts.
        /// </summary>
        private void DrawPuddle(FxMeshBuilder g, Vector3 c, float s, float rot, uint seed, float fade, double now)
        {
            Vector3 gr = Vector3.right, gu = Vector3.forward;
            const float tau = Mathf.PI * 2f;
            g.Blob(c, gr, gu, s, null, 20, 0, A(CorruptDark, fade * 0.85f));
            g.Ring(c, gr, gu, s, s + 0.05f, 0, tau, 20, A(Navy, fade * 0.9f));
            g.Ring(c, gr, gu, s * 0.62f, s * 0.8f, 0, tau, 20, A(CorruptionC, fade));
            g.Ring(c, gr, gu, s * 0.3f, s * 0.4f, 0, tau, 20, A(CorruptLight, fade * 0.9f));
            float wob = 0.25f * Mathf.Sin((float)now * 1.7f + seed * 0.001f);
            g.Ring(c, gr, gu, s * 0.45f, s * 0.52f, rot + wob, rot + wob + 0.9f, 8, A(OilSheen, fade * 0.8f));
            g.Ring(c, gr, gu, s * 0.84f, s * 0.9f, rot + Mathf.PI - wob, rot + Mathf.PI - wob + 0.6f, 8, A(OilSheen, fade * 0.6f));
            for (int k = 0; k < 3; k++)
            {
                float ph = (float)((now * 0.7 + k * 0.33 + R01(seed, (uint)k + 500)) % 1.0);
                if (ph > 0.85f) continue; // popped
                float a = rot + k * 2.1f;
                Vector3 p = c + (gr * Mathf.Cos(a) + gu * Mathf.Sin(a)) * s * (0.25f + 0.4f * R01(seed, (uint)k + 510)) + Vector3.up * 0.004f;
                float r = s * (0.05f + 0.13f * ph);
                g.Ring(p, gr, gu, r * 0.6f, r, 0, tau, 8, A(CorruptLight, fade));
            }
        }

        /// <summary>The light pulse on the ground: a warm additive disc (21 verts per explosion).</summary>
        private void BuildBoomGround(double now, FxMeshBuilder ground)
        {
            Vector3 gr = Vector3.right, gu = Vector3.forward;
            double dur = S(0.3);
            for (int i = 0; i < MaxExplosions; i++)
            {
                ref Explosion ex = ref _booms[i];
                double age = now - ex.Born;
                if (age < 0 || age >= dur || ex.Kind == ExplosionKind.WallBreak) continue;
                float u = (float)(age / dur);
                float k = (1f - u) * (1f - u);
                bool fire = HasFire(ex.Kind);
                ground.Add = 255;
                ground.Blob(ex.P + Vector3.up * 0.04f, gr, gu, ex.R * 2.1f * (0.8f + 0.2f * u), null, 20, 0, A(fire ? Glow : DustLight, (fire ? 0.85f : 0.35f) * k));
                ground.Add = 0;
            }
        }

        /// <summary>polish-3: the fireball is gone by this age (s at normal speed); the core reaches full size by
        /// FireballGrow and fades out from FireballFadeFrom.</summary>
        public const double FireballEnd = 0.6, FireballGrow = 0.12, FireballFadeFrom = 0.36;
        /// <summary>polish-3: merged lobes around the core (3: each overlaps its neighbour by more than half its radius).</summary>
        public const int FireballLobes = 3;
        /// <summary>polish-3: lobe radius / core radius, lobe centre offset / lobe radius, the soft halo and the orange band
        /// as shares of a shape's radius.</summary>
        public const float FireballLobeK = 0.66f, FireballLobeOffset = 0.8f, FireballHaloK = 1.15f, FireballMidK = 0.78f;
        /// <summary>polish-3: the widest the fireball ever draws from its centre to its halo edge (u). Fighters stand about
        /// 2.1 u; the whole blast is about 2.2 u across, so it never swallows more than about one and a half fighters.</summary>
        public const float FireballHalfMax = 1.1f;
        /// <summary>polish-3: the dark-red edge of the fireball ramp.</summary>
        public static readonly Color32 FireDeep = new Color32(168, 38, 30, 255);

        /// <summary>Half-width of the whole drawn fireball as a share of the core radius (pure; tests).</summary>
        public static float FireballHalfK => Mathf.Max(FireballHaloK, FireballLobeK * (FireballLobeOffset + FireballHaloK));
        /// <summary>Overlap of two neighbouring lobes in lobe radii (pure; tests): 2 minus their centre distance.</summary>
        public static float FireballLobeOverlap => 2f - 2f * FireballLobeOffset * Mathf.Sin(Mathf.PI / FireballLobes);

        /// <summary>The fireball core radius at age t (s at normal speed) for a blast of radius R (pure; tests): 0 outside
        /// its life; an ease-out from 35% to full by FireballGrow, then shrinking by a quarter while it fades.</summary>
        public static float FireballCoreRadius(float R, float t)
        {
            if (t < 0f || t >= (float)FireballEnd) return 0f;
            float max = Mathf.Min(0.7f * Mathf.Min(Mathf.Max(R, 0f), BoomVisualMax), FireballHalfMax / FireballHalfK);
            float g = Mathf.Clamp01(t / (float)FireballGrow);
            float e = 1f - (1f - g) * (1f - g) * (1f - g);
            float f = Mathf.Clamp01((t - (float)FireballFadeFrom) / (float)(FireballEnd - FireballFadeFrom));
            return max * (0.35f + 0.65f * e) * (1f - 0.25f * f);
        }

        /// <summary>The fireball alpha at age t (pure; tests): full, then fading to nothing by FireballEnd.</summary>
        public static float FireballAlpha(float t)
        {
            if (t < 0f || t >= (float)FireballEnd) return 0f;
            return 1f - Mathf.Clamp01((t - (float)FireballFadeFrom) / (float)(FireballEnd - FireballFadeFrom));
        }

        /// <summary>The yellow heart as a share of the core radius at age t (pure; tests): big at the flash, shrinking first.</summary>
        public static float FireballYellowK(float t) => Mathf.Max(0f, 0.6f - 1.1f * Mathf.Max(0f, t - 0.08f));

        /// <summary>follow-camera-4: the smoke column in the low views lives this share of its time and rises this share
        /// of its height (a flatter, shorter column that clears the fight sooner).</summary>
        public const double LowSmokeLife = 0.6;

        /// <summary>smoke-look-1: the drawn fireball, lobes and smoke size with the blast radius only up to this many u
        /// (fighters stand about 2.1 u tall); the ground ring and glow still show the full radius.</summary>
        public const float BoomVisualMax = 1.6f;
        /// <summary>smoke-look-1: the ring of dust puffs at a blast never grows past this size, whatever the radius.</summary>
        public const float BlastPuffMax = 0.8f;
        /// <summary>smoke-look-1: smoke puffs per explosion, the largest birth radius, how much a puff grows, its peak
        /// alpha, its life and the stagger between births (seconds at normal speed).</summary>
        public const int SmokePuffs = 8;
        public const float SmokeBirthMax = 0.55f, SmokeGrow = 1.5f, SmokeAlpha = 0.45f;
        public const double SmokeStart = 0.04, SmokeStagger = 0.065, SmokeLife = 1.0;
        /// <summary>The time the last puff is gone (s): 1.495.</summary>
        public const double SmokeEnd = SmokeStart + SmokeStagger * (SmokePuffs - 1) + SmokeLife;
        static readonly Vector3 SmokeWind = new Vector3(0.6f, 0f, 0.25f);

        /// <summary>A puff's radius at birth for a blast of radius R (pure; tests).</summary>
        public static float SmokeBirthRadius(float R) => Mathf.Min(0.32f + 0.06f * Mathf.Max(0f, R), SmokeBirthMax);
        /// <summary>A puff's radius at life fraction u (pure; tests): from birth to SmokeGrow times birth.</summary>
        public static float SmokeRadius(float R, float u) => SmokeBirthRadius(R) * (1f + (SmokeGrow - 1f) * Mathf.Sqrt(Mathf.Clamp01(u)));
        /// <summary>A puff's alpha at life fraction u (pure; tests): a quick fade-in, then thinning to nothing.</summary>
        public static float SmokeAlphaAt(float u)
        {
            u = Mathf.Clamp01(u);
            return SmokeAlpha * Mathf.Min(1f, u / 0.1f) * (1f - u);
        }
        /// <summary>Bounding radius of the whole smoke for the low-view group test (pure; tests).</summary>
        public static float SmokeBound(float R) => 0.6f * Mathf.Min(R, BoomVisualMax) + 2.2f;
        public const float LowSmokeRise = 0.6f;

        private void BuildBoomAir(double now, FxMeshBuilder air, Vector3 camRight, Vector3 camUp)
        {
            Vector3 toCam = -Vector3.Cross(camRight, camUp);
            double life = BoomLife;
            for (int i = 0; i < MaxExplosions; i++)
            {
                ref Explosion ex = ref _booms[i];
                double age = now - ex.Born;
                if (age < 0 || age >= life) continue;
                float R = Mathf.Min(ex.R, BoomVisualMax);   // smoke-look-1: the air parts stop growing past BoomVisualMax
                bool fire = HasFire(ex.Kind);
                Vector3 c = ex.P + toCam * 0.4f;

                // follow-camera-4: the glow, fireball and lobes are one flash group (low views cap its size on screen).
                if (air.BeginGroup(c + Vector3.up * (0.95f * R), 2.0f * R, CriticalMass.Runtime.Camera.FxLook.Flash, ex.P))
                {
                // Glow billboard (additive) and the two-frame white core.
                if (fire && age < S(0.14))
                {
                    float k = 1f - (float)(age / S(0.14));
                    air.Add = 255;
                    air.Blob(c + Vector3.up * (0.8f * R), camRight, camUp, R * 1.3f, null, 16, 0, A(Glow, 0.7f * k * k));
                    air.Add = 0;
                }
                if (ex.Flash && age < S(0.034))
                    air.Blob(c + Vector3.up * (0.6f * R), camRight, camUp, R * 0.62f, null, 14, 0, SparkWhite);

                // polish-3 (owner: the TNT fireball read as "a bunch of orange balloons", about eight hard-edged spheres two
                // fighters wide at 0.4 s): ONE soft toon blast. A single core that scales up fast and fades, FireballLobes
                // lobes merged into it (each overlaps its neighbour by more than half its radius), drawn layer by layer
                // across all shapes so they share one silhouette and one colour ramp: a soft dark-red halo, the dark-red
                // edge, orange, and a bright yellow heart that shrinks first. No navy rim per ball, no lobes flying apart.
                // Gone by FireballEnd; the small grey smoke puffs below carry on. Same builder, same draw call, fewer blobs.
                if (fire)
                {
                    float t = (float)(age / S(1.0));
                    float rc = FireballCoreRadius(R, t);
                    if (rc > 0f)
                    {
                        float a = FireballAlpha(t);
                        float rise = Mathf.Clamp01(t / (float)FireballEnd);
                        Vector3 p = c + Vector3.up * (0.55f * R + 0.35f * R * rise);
                        float rl = rc * FireballLobeK, d = rl * FireballLobeOffset;
                        float a0 = R01(ex.Seed, 600) * Mathf.PI * 2f;
                        for (int layer = 0; layer < 3; layer++)
                        {
                            float k = layer == 0 ? FireballHaloK : layer == 1 ? 1f : FireballMidK;
                            Color32 col = layer == 0 ? A(FireDeep, a * 0.45f) : layer == 1 ? A(FireDeep, a) : A(FireOrange, a);
                            air.Blob(p, camRight, camUp, rc * k, null, 12, 0, col);
                            for (int j = 0; j < FireballLobes; j++)
                            {
                                float ang = a0 + j * Mathf.PI * 2f / FireballLobes;
                                Vector3 q = p + (camRight * Mathf.Cos(ang) + camUp * Mathf.Sin(ang)) * d;
                                air.Blob(q, camRight, camUp, rl * k, null, 10, 0, col);
                            }
                        }
                        float yr = rc * FireballYellowK(t);
                        if (yr > 0.04f) air.Blob(p + camUp * (rc * 0.08f), camRight, camUp, yr, null, 12, 0, A(FireYellow, a));
                    }
                }

                air.EndGroup();
                }

                // smoke-look-1: the smoke is SmokePuffs small soft puffs, born staggered around the blast, each well under a
                // fighter's height across (SmokeBirthRadius), growing a little, rising, drifting with a light wind and
                // thinning out (SmokeAlphaAt, never above SmokeAlpha) within SmokeLife; all gone by SmokeEnd. Flat stepped
                // tones: a faint wider halo (the soft edge), the body, and a lighter cap. No navy rim, no single big ball.
                // follow-camera-4 low views: one volume group (screen cap, see-through over the fight), shorter and lower.
                bool low = CriticalMass.Runtime.Camera.ViewCull.On;
                float riseK = low ? LowSmokeRise : 1f;
                if (age >= S(SmokeEnd)) continue;
                if (!air.BeginGroup(ex.P + Vector3.up * (1.2f * riseK + 0.3f), SmokeBound(R), CriticalMass.Runtime.Camera.FxLook.Volume, ex.P)) continue;
                Color32 sm = fire ? SmokeMid : DustMid, sl = fire ? SmokeLight : DustLight;
                double puffLife = S(SmokeLife) * (low ? LowSmokeLife : 1.0);
                for (int j = 0; j < SmokePuffs; j++)
                {
                    double pa = age - S(SmokeStart + SmokeStagger * j);
                    if (pa < 0 || pa >= puffLife) continue;
                    float u = (float)(pa / puffLife);
                    float rise = (1f - (1f - u) * (1f - u)) * riseK;
                    float ang = (j + R01(ex.Seed, (uint)j + 640)) / SmokePuffs * Mathf.PI * 2f;
                    float spread = R * (0.25f + 0.3f * R01(ex.Seed, (uint)j + 650));
                    Vector3 start = ex.P + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * spread;
                    Vector3 p = start + Vector3.up * (0.35f + (1.3f + 0.6f * R01(ex.Seed, (uint)j + 660)) * rise)
                        + SmokeWind * (0.6f * u) + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (0.3f * rise) + toCam * 0.15f;
                    float sz = SmokeRadius(R, u) * (0.85f + 0.3f * R01(ex.Seed, (uint)j + 670));
                    float al = SmokeAlphaAt(u);
                    air.Blob(p, camRight, camUp, sz * 1.22f, null, 8, j, A(sm, al * 0.4f));
                    air.Blob(p, camRight, camUp, sz, null, 8, j + 0.4f, A(sm, al));
                    air.Blob(p + (camUp - camRight) * (sz * 0.25f), camRight, camUp, sz * 0.5f, null, 8, 0, A(sl, al * 0.6f));
                }
                air.EndGroup();
            }
        }
    }
}
