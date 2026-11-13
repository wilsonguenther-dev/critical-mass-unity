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
    /// <item>0.012-0.320 the fireball: outlined, three stepped bands (yellow, orange, red), growing and rising</item>
    /// <item>0.180-0.750 it breaks into seven lobes that fly out and step from fire to smoke grey</item>
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
                    AddPuff(t + S(0.02 * (k % 3)), p, seed, (uint)(k + 70), 0.5f + 0.1f * R, kind == ExplosionKind.Mine ? DirtLight : DustMid, 1);
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

        private void BuildBoomAir(double now, FxMeshBuilder air, Vector3 camRight, Vector3 camUp)
        {
            Vector3 toCam = -Vector3.Cross(camRight, camUp);
            double life = BoomLife;
            for (int i = 0; i < MaxExplosions; i++)
            {
                ref Explosion ex = ref _booms[i];
                double age = now - ex.Born;
                if (age < 0 || age >= life) continue;
                float R = ex.R;
                bool fire = HasFire(ex.Kind);
                Vector3 c = ex.P + toCam * 0.4f;

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

                // Fireball: three stepped bands under a navy rim, growing and rising, the yellow heart shrinking first.
                double f0 = S(0.012), f1 = S(0.32);
                if (fire && age >= f0 && age < f1)
                {
                    float u = (float)((age - f0) / (f1 - f0));
                    float rf = R * (0.3f + 0.62f * (1f - (1f - u) * (1f - u) * (1f - u)));
                    Vector3 p = c + Vector3.up * (0.55f * R + 0.4f * R * u);
                    Lobes(ex.Seed, 600, 12, 0.86f, 1.12f);
                    float a = 1f - Mathf.Clamp01((u - 0.75f) / 0.25f);
                    air.Blob(p, camRight, camUp, rf * 1.12f + 0.05f, _radii, 12, ex.Seed * 0.001f, A(Navy, a));
                    air.Blob(p, camRight, camUp, rf, _radii, 12, ex.Seed * 0.001f, A(FireRed, a));
                    air.Blob(p, camRight, camUp, rf * (0.8f - 0.25f * u), _radii, 12, ex.Seed * 0.001f + 0.4f, A(FireOrange, a));
                    float yr = rf * (0.56f - 0.5f * u);
                    if (yr > 0.04f) air.Blob(p + camUp * (rf * 0.08f), camRight, camUp, yr, _radii, 12, ex.Seed * 0.001f + 0.8f, A(FireYellow, a));
                }

                // Lobes: the fireball breaks into seven puffs that fly out and cool from orange to red to smoke.
                double l0 = S(0.18), l1 = S(0.75);
                if (fire && age >= l0 && age < l1)
                {
                    float v = (float)((age - l0) / (l1 - l0));
                    float e = 1f - (1f - v) * (1f - v);
                    float rEnd = R * 0.92f;
                    Vector3 centre = c + Vector3.up * (0.95f * R + 0.6f * R * v);
                    Color32 fill = v < 0.35f ? FireOrange : v < 0.65f ? FireRed : SmokeDark;
                    Color32 hi = v < 0.35f ? FireYellow : v < 0.65f ? FireOrange : SmokeMid;
                    float al = 1f - v * v;
                    for (int k = 0; k < 7; k++)
                    {
                        float a = k * Mathf.PI * 2f / 7f + (R01(ex.Seed, (uint)k + 620) - 0.5f) * 0.5f;
                        Vector3 dir = camRight * Mathf.Cos(a) + camUp * Mathf.Sin(a);
                        Vector3 p = centre + dir * (rEnd * (0.45f + 0.75f * e));
                        float sz = R * 0.42f * (1f - 0.55f * v) + 0.05f;
                        air.Blob(p, camRight, camUp, sz + 0.05f, null, 9, 0, A(Navy, al));
                        air.Blob(p, camRight, camUp, sz, null, 9, 0, A(fill, al));
                        air.Blob(p + (camUp - camRight) * (sz * 0.25f), camRight, camUp, sz * 0.5f, null, 7, 0, A(hi, al));
                    }
                }

                // Smoke column: six stacked puffs rising and drifting, stepped greys (dust tones without fire).
                Color32 sm = fire ? SmokeMid : DustMid, sl = fire ? SmokeLight : DustLight, sd = fire ? SmokeDark : DustDark;
                double colLife = S(fire ? 2.2 : 1.6);
                for (int j = 0; j < 6; j++)
                {
                    double born = S(0.15 + 0.12 * j);
                    double pa = age - born;
                    if (pa < 0 || pa >= colLife) continue;
                    float u = (float)(pa / colLife);
                    float rise = 1f - (1f - u) * (1f - u);
                    float drift = (R01(ex.Seed, (uint)j + 640) - 0.5f) * R * 0.8f;
                    Vector3 p = ex.P + toCam * 0.2f + Vector3.up * (0.5f * R + (1.2f + 0.35f * j) * R * rise) + camRight * (drift * rise);
                    float sz = R * (0.38f + 0.08f * j) * (0.6f + 0.6f * Mathf.Sqrt(u)) * (fire ? 1f : 0.85f);
                    float fade = u < 0.6f ? 1f : 1f - (u - 0.6f) / 0.4f;
                    air.Blob(p, camRight, camUp, sz + 0.05f, null, 10, j, A(Navy, fade * 0.7f));
                    air.Blob(p, camRight, camUp, sz, null, 10, j, A(sm, fade));
                    air.Blob(p + (camUp - camRight) * (sz * 0.28f), camRight, camUp, sz * 0.55f, null, 8, 0, A(sl, fade));
                    air.Blob(p - (camUp - camRight * 0.4f) * (sz * 0.3f), camRight, camUp, sz * 0.45f, null, 8, 0, A(sd, fade * 0.85f));
                }
            }
        }
    }
}
