using System;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>
    /// Blood and gore (v2), every piece behind FxScene.Blood (cm.blood): seven procedural splat shapes (six for flying
    /// drops, one knock-out pool that spreads over a second), splats on wall top faces and bridge planks, drips behind badly
    /// hurt actors, cartoon gibs (chunky meat cubes and bones in the game's palette, a few, bouncing once and fading) on a
    /// Kaiju stomp or an explosion knock-out. Debris chunks live here too (they share the solid layer and the landing code:
    /// bounce once on ground, sink with a splash in water, drop into the void past the rim with a spray).
    /// Stylised, never anatomical: rounded lobes in three flat reds with a Void Navy rim, cubes and cartoon bones.
    /// <para>GORE setting (2026-11-14): every spawn here asks Allow(piece) and every draw Shows(piece) (GoreGate): gibs,
    /// drips, wall-top splats, the pool and stamped ground stains are Full only; splats are smaller and short-lived at
    /// Reduced; nothing here exists at Off.</para>
    /// </summary>
    public sealed partial class FxScene
    {
        /// <summary>The water surface height (GridChunkMeshView.WaterTop, private there).</summary>
        public const float WaterTop = -0.2f;

        private struct Chunk
        {
            public double Born, Life;
            public Vector3 P0, V, Axis;
            public float Size, Spin, TLand, GroundY;
            public byte Mat, Land, Type;
            public bool Done;
        }

        private struct Drip { public double Born; public Vector3 P; public float Size; public uint Seed; }
        private struct DeathRec { public double T; public double X, Z, H, Dx, Dz; public uint Seed; public bool Gibbed; }

        private readonly Chunk[] _chunks = new Chunk[MaxChunks];
        private readonly Chunk[] _gibs = new Chunk[MaxGibs];
        private readonly Drip[] _drips = new Drip[MaxDrips];
        private const int DeathMemory = 16;
        private readonly DeathRec[] _deaths = new DeathRec[DeathMemory];
        private int _nc, _ng, _ndr, _nde;

        private double DripLife => S(5.0);

        private void InitGore()
        {
            for (int i = 0; i < MaxChunks; i++) { _chunks[i].Born = double.NegativeInfinity; _chunks[i].Done = true; }
            for (int i = 0; i < MaxGibs; i++) { _gibs[i].Born = double.NegativeInfinity; _gibs[i].Done = true; }
            for (int i = 0; i < MaxDrips; i++) _drips[i].Born = double.NegativeInfinity;
            for (int i = 0; i < DeathMemory; i++) { _deaths[i].T = double.NegativeInfinity; _deaths[i].Gibbed = true; }
        }

        private int ActiveGore(double now)
        {
            int n = 0;
            for (int i = 0; i < MaxChunks; i++) if (now - _chunks[i].Born < _chunks[i].Life) n++;
            for (int i = 0; i < MaxGibs; i++) if (now - _gibs[i].Born < _gibs[i].Life) n++;
            return n + DripCount(now);
        }

        public int DripCount(double now)
        {
            int n = 0;
            for (int i = 0; i < MaxDrips; i++) if (now - _drips[i].Born < DripLife) n++;
            return n;
        }

        public int GibCount(double now)
        {
            int n = 0;
            for (int i = 0; i < MaxGibs; i++) if (now - _gibs[i].Born < _gibs[i].Life) n++;
            return n;
        }

        // ---- spawning ----

        /// <summary>A debris chunk: mat 0 rock, 1 wood, 2 dirt, 3 charred wood. speedMul scales the throw (explosions).</summary>
        private void AddChunk(double t, Vector3 p, float spread, uint seed, uint k, byte mat, float scale, float speedMul)
        {
            ref Chunk c = ref _chunks[_nc++ % MaxChunks];
            Throw(ref c, t, p, spread, seed, k, scale, speedMul, Vector3.zero);
            c.Mat = mat;
            c.Type = 0;
            c.Life = c.Land == 2 ? c.TLand : S(1.6);
        }

        private void Throw(ref Chunk c, double t, Vector3 p, float spread, uint seed, uint k, float scale, float speedMul, Vector3 bias)
        {
            float a = R01(seed, k * 5 + 1) * Mathf.PI * 2f;
            float sp = (1.8f + R01(seed, k * 5 + 2) * 3.2f) * speedMul;
            c.Born = t;
            c.P0 = p + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * spread * R01(seed, k * 5 + 3);
            c.V = new Vector3(Mathf.Cos(a) * sp, (4.5f + R01(seed, k * 5 + 4) * 4.5f) * Mathf.Sqrt(speedMul), Mathf.Sin(a) * sp) + bias;
            c.Axis = new Vector3(R01(seed, k + 60) - 0.5f, R01(seed, k + 61) - 0.5f, R01(seed, k + 62) - 0.5f);
            c.Axis = c.Axis.sqrMagnitude < 0.01f ? Vector3.right : c.Axis.normalized;
            c.Spin = 8f + R01(seed, k + 63) * 10f;
            c.Size = (0.09f + R01(seed, k + 64) * 0.12f) * scale;
            c.Done = false;
            // Where it comes down: the ground at the spawn, then the real surface under the landing point (a wall top, the
            // water, or the void past the island rim).
            c.GroundY = (float)Ground(SimX(c.P0), SimZ(c.P0));
            c.Land = 0;
            c.TLand = LandTime(c.V.y, c.P0.y - c.GroundY - c.Size);
            Vector3 lp = c.P0 + c.V * c.TLand;
            double lx = SimX(lp), lz = SimZ(lp);
            byte cell = CellAt(lx, lz);
            if (cell == Cell.Water) { c.GroundY = WaterTop - c.Size; c.Land = 1; }
            else if (cell == Cell.Void) { c.GroundY = c.GroundY - 1.4f; c.Land = 2; }
            else if (SurfaceTop(lx, lz, out double top)) c.GroundY = (float)top;
            c.TLand = LandTime(c.V.y, c.P0.y - c.GroundY - c.Size);
        }

        private static float LandTime(float vy, float dy) => (vy + Mathf.Sqrt(vy * vy + 2f * G * Mathf.Max(0f, dy))) / G;

        /// <summary>A knock-out the effects should gib: a Kaiju slam (credit -1 while the Kaiju is out, inside the slam circle)
        /// or a blast that went off within two ticks and reaches the body.</summary>
        private bool GibWorthy(ref FxEvent e, World w)
        {
            // DG-16: the sim names the cause. A slam or an explosion knock-out is gibbed; a melee or a fall never is.
            if (e.Cause == DamageCause.KaijuSlam || e.Cause == DamageCause.Explosion) return true;
            if (e.Cause == DamageCause.None && e.B == -1 && w.Kaiju.State != KaijuState.Egg)
            {
                double dx = e.X - w.Kaiju.TelegraphX, dz = e.Z - w.Kaiju.TelegraphZ, r = _cfg.Kaiju.SlamRadius + 1.0;
                if (dx * dx + dz * dz <= r * r) return true;
            }
            return RecentExplosionNear(T(e.Tick), e.X, e.Z);
        }

        private void RememberDeath(double t, double x, double z, double h, uint seed, double dx, double dz)
        {
            ref DeathRec d = ref _deaths[_nde++ % DeathMemory];
            d.T = t; d.X = x; d.Z = z; d.H = h; d.Seed = seed; d.Dx = dx; d.Dz = dz;
            d.Gibbed = false;
        }

        /// <summary>Gib the knock-outs of the last two ticks inside radius r of a blast (at most three bodies per blast).</summary>
        private void GibRecentDeaths(double t, double x, double z, double r)
        {
            if (!Blood) return;
            int n = 0;
            double win = 2.0 / _hz + 1e-9;
            for (int i = 0; i < DeathMemory && n < 3; i++)
            {
                ref DeathRec d = ref _deaths[i];
                if (d.Gibbed || Math.Abs(d.T - t) > win) continue;
                double dx = d.X - x, dz = d.Z - z;
                if (dx * dx + dz * dz > r * r) continue;
                d.Gibbed = true;
                AddGibs(t, d.X, d.Z, d.H, d.Seed, d.Dx, d.Dz);
                n++;
            }
        }

        /// <summary>Three meat cubes and two cartoon bones, thrown along the hit, bouncing once, fading; each leaves a splat.</summary>
        private void AddGibs(double t, double x, double z, double h, uint seed, double dirX, double dirZ)
        {
            for (int i = 0; i < DeathMemory; i++)
                if (_deaths[i].T == t && _deaths[i].X == x && _deaths[i].Z == z) _deaths[i].Gibbed = true;
            // Body parts: GORE Full only (three meat cubes and two bones per body).
            if (!Shows(GorePiece.GibMeat) || !Shows(GorePiece.GibBone)) return;
            _goreSpawns[(int)GorePiece.GibMeat] += 3;
            _goreSpawns[(int)GorePiece.GibBone] += 2;
            Vector3 bias = W(dirX, 0, dirZ) * 1.6f;
            Vector3 p = W(x, h + 0.8, z);
            for (int k = 0; k < 5; k++)
            {
                ref Chunk c = ref _gibs[_ng++ % MaxGibs];
                Throw(ref c, t, p, 0.25f, seed + 911u, (uint)k, k < 3 ? 1.5f : 1.0f, 0.9f, bias);
                c.Type = k < 3 ? (byte)0 : (byte)1;
                c.Mat = 0;
                c.Life = c.Land == 2 ? c.TLand : S(2.2);
            }
        }

        /// <summary>A drip of blood on the surface under a badly hurt actor (the trail behind them).</summary>
        private void AddDrip(double t, Vector3 p, uint seed)
        {
            if (!Allow(GorePiece.BloodDrip)) return;
            ref Drip d = ref _drips[_ndr++ % MaxDrips];
            d.Born = t; d.P = p; d.Seed = seed;
            d.Size = 0.06f + R01(seed, 3) * 0.05f;
        }

        // ---- landings ----

        private void AdvanceSolids(double now)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                Chunk[] arr = pass == 0 ? _chunks : _gibs;
                for (int i = 0; i < arr.Length; i++)
                {
                    ref Chunk c = ref arr[i];
                    if (c.Done || now < c.Born + c.TLand) continue;
                    c.Done = true;
                    double tl = c.Born + c.TLand;
                    Vector3 lp = c.P0 + c.V * c.TLand + Vector3.down * (0.5f * G * c.TLand * c.TLand);
                    double lx = SimX(lp), lz = SimZ(lp);
                    uint seed = Hash((uint)i, (uint)(c.Born * 1000), (uint)(pass + 21));
                    if (c.Land == 1) SpawnSplash(tl, lx, lz, 0.35f + c.Size * 2f, seed);
                    else if (c.Land == 2)
                    {
                        // Over the rim into the void: a spray off the falling water sheet, and it is gone.
                        Vector3 sp = lp + Vector3.up * 0.2f;
                        AddPuff(tl, sp, seed, 1, 0.3f, WaterLight, 4);
                        AddSpark(tl, sp, 0, 0, seed, 2, 6);
                        AddSpark(tl, sp, 0, 0, seed, 3, 6);
                    }
                    else if (pass == 1 && Allow(GorePiece.WallTopSplat) && SurfaceTop(lx, lz, out double top))
                    {
                        lp.y = (float)top;
                        AddDecal(tl, lp, 0.26f + c.Size, 0, (byte)(seed % 6u), seed, c.V.x, -c.V.z, S(0.05));
                    }
                }
            }
        }

        private static Vector3 SolidPos(in Chunk c, float age, out float spinAge)
        {
            if (age <= c.TLand)
            {
                spinAge = age;
                return c.P0 + c.V * age + Vector3.down * (0.5f * G * age * age);
            }
            Vector3 land = c.P0 + c.V * c.TLand + Vector3.down * (0.5f * G * c.TLand * c.TLand);
            float s = age - c.TLand;
            if (c.Land == 1)
            {
                spinAge = c.TLand + s * 0.3f;
                return land + Vector3.down * (0.8f * s);
            }
            // One bounce: up at a third of the landing speed, half the horizontal speed, then rest with a short slide.
            float vLand = c.V.y - G * c.TLand;
            float vb = -vLand * 0.32f;
            float tb = 2f * vb / G;
            var hv = new Vector3(c.V.x, 0, c.V.z);
            Vector3 p;
            if (s < tb)
            {
                spinAge = c.TLand + s;
                p = land + hv * (0.45f * s);
                p.y = c.GroundY + c.Size + vb * s - 0.5f * G * s * s;
                return p;
            }
            float r = s - tb;
            spinAge = c.TLand + tb;
            p = land + hv * (0.45f * tb) + hv * (0.12f * (1f - Mathf.Exp(-6f * r)));
            p.y = c.GroundY + c.Size;
            return p;
        }

        // ---- drawing ----

        private void BuildSolids(double now, FxMeshBuilder solid)
        {
            // Debris: toon cubes with an inverted navy hull; one bounce, then shrink out. 48 verts each.
            for (int i = 0; i < MaxChunks; i++)
            {
                ref Chunk c = ref _chunks[i];
                double age = now - c.Born;
                if (age < 0 || age >= c.Life) continue;
                Vector3 p = SolidPos(c, (float)age, out float spinAge);
                float tail = (float)((c.Life - age) / (c.Life * 0.25));
                float sz = c.Size * Mathf.Clamp01(tail);
                if (c.Land == 1 && age > c.TLand) sz *= Mathf.Clamp01(1f - (float)(age - c.TLand) * 3f);
                Quaternion rot = Quaternion.AngleAxis(Mathf.Rad2Deg * c.Spin * spinAge, c.Axis);
                Color32 l, m, dk;
                switch (c.Mat)
                {
                    case 1: l = WoodLight; m = WoodMid; dk = WoodDark; break;
                    case 2: l = DirtLight; m = DirtMid; dk = DirtDark; break;
                    case 3: l = WoodDark; m = ScorchOuter; dk = ScorchInner; break;
                    default: l = RockLight; m = RockMid; dk = RockDark; break;
                }
                solid.Chunk(p, rot, new Vector3(sz, sz * 0.8f, sz * 1.1f), 0.035f, l, m, dk, Navy);
            }
            // Gibs: chunky meat cubes (48 verts) and cartoon bones (144 verts). GORE Full only (hidden at once on a change).
            for (int i = 0; i < MaxGibs && Shows(GorePiece.GibMeat); i++)
            {
                ref Chunk c = ref _gibs[i];
                double age = now - c.Born;
                if (age < 0 || age >= c.Life) continue;
                Vector3 p = SolidPos(c, (float)age, out float spinAge);
                float tail = (float)((c.Life - age) / (c.Life * 0.25));
                float sz = c.Size * Mathf.Clamp01(tail);
                if (c.Land == 1 && age > c.TLand) sz *= Mathf.Clamp01(1f - (float)(age - c.TLand) * 3f);
                Quaternion rot = Quaternion.AngleAxis(Mathf.Rad2Deg * c.Spin * spinAge, c.Axis);
                if (c.Type == 1) solid.Bone(p, rot, sz * 1.6f, sz * 0.32f, 0.03f, BoneLight, BoneMid, BoneDark, Navy);
                else solid.Chunk(p, rot, new Vector3(sz, sz * 0.75f, sz * 0.9f), 0.035f, MeatLight, MeatMid, MeatDark, Navy);
            }
        }

        private void BuildDrips(double now, FxMeshBuilder ground)
        {
            Vector3 gr = Vector3.right, gu = Vector3.forward;
            double life = DripLife;
            if (!Shows(GorePiece.BloodDrip)) return;
            for (int i = 0; i < MaxDrips; i++)
            {
                ref Drip d = ref _drips[i];
                double age = now - d.Born;
                if (age < 0 || age >= life) continue;
                float fade = (float)Math.Min(1.0, (life - age) / (life * 0.3));
                Vector3 c = d.P + Vector3.up * 0.032f;
                float grow = Mathf.Clamp01((float)(age / S(0.05)));
                ground.Blob(c, gr, gu, d.Size * grow + 0.022f, null, 6, d.Seed * 0.01f, A(Navy, fade * 0.55f));
                ground.Blob(c, gr, gu, d.Size * grow, null, 6, d.Seed * 0.01f, A(GroundInkMid, fade * GroundInkAlpha));
            }
        }

        /// <summary>Fill _radii[0..n) with lobes between lo and hi (deterministic per seed and salt).</summary>
        private void Lobes(uint seed, uint salt, int n, float lo, float hi)
        {
            for (int k = 0; k < n; k++) _radii[k] = lo + (hi - lo) * R01(seed, salt + (uint)k);
        }

        private void BuildDecals(double now, FxMeshBuilder ground)
        {
            const float lift = 0.03f;
            for (int i = 0; i < MaxDecals; i++)
            {
                ref Decal d = ref _decals[i];
                double age = now - d.Born;
                if (age < 0 || age >= d.Life) continue;
                if (!Shows(d.Shape == 6 ? GorePiece.KnockOutPool : GorePiece.BloodSplat)) continue;
                float grow = d.Grow > 0 ? Mathf.Clamp01((float)(age / d.Grow)) : 1f;
                grow = 1f - (1f - grow) * (1f - grow);
                float fade = (float)Math.Min(1.0, (d.Life - age) / (d.Life * 0.3));
                bool still = age >= d.Grow && fade >= 1f;
                int before = _decalStamp[i];
                // Stamping into the ground paint is what makes stains build up where fights happen: GORE Full only.
                bool stamp = Shows(GorePiece.GroundStain);
                if (stamp && PaintedAway(ref _decalStamp[i], d.P, still)) continue;
                if (stamp && before == -1 && _decalStamp[i] >= 0)
                {
                    _goreSpawns[(int)GorePiece.GroundStain]++;
                    DrawSplat(Paint.Builder, d.P, d.Size, d, 1f); // stamped once
                }
                DrawSplat(ground, d.P + Vector3.up * lift, d.Size * grow, d, fade);
                LastGroundMarks++;
            }
        }

        private void Dot(FxMeshBuilder g, Vector3 p, Vector3 gr, Vector3 gu, float r, float fade)
        {
            g.Blob(p, gr, gu, r + 0.025f, null, 7, 0, A(Navy, fade * 0.55f));
            g.Blob(p, gr, gu, r, null, 7, 0, A(GroundInkMid, fade * GroundInkAlpha));
        }

        /// <summary>
        /// The seven splat shapes, every one outlined in navy with mid, dark and light ground ink (at most 130 vertices):
        /// 0 round splat with satellites, 1 teardrop streak along the flight, 2 crown (spiky), 3 cluster of three lobes,
        /// 4 smear (a dragged stroke with round ends), 5 spatter fan, 6 the knock-out pool (big lobed puddle).
        /// </summary>
        private void DrawSplat(FxMeshBuilder g, Vector3 c, float s, in Decal d, float fade)
        {
            Vector3 gr = Vector3.right, gu = Vector3.forward;
            Vector3 dv = gr * Mathf.Cos(d.DirA) + gu * Mathf.Sin(d.DirA);
            Vector3 pv = gr * -Mathf.Sin(d.DirA) + gu * Mathf.Cos(d.DirA);
            uint sd = d.Seed;
            // Neutral ground ink (world around arena 1), never a team hue.
            Color32 navy = A(Navy, fade * 0.55f), mid = A(GroundInkMid, fade * GroundInkAlpha), dark = A(GroundInkDark, fade * GroundInkAlpha), light = A(GroundInkLight, fade * GroundInkAlpha);
            switch (d.Shape)
            {
                case 1:
                {
                    // Teardrop streak: stretched along the flight, two trailing dots ahead. 72 verts.
                    Lobes(sd, 0, 10, 0.8f, 1.1f);
                    g.Blob(c, dv * 1.7f, pv * 0.85f, s * 1.12f + 0.03f, _radii, 10, d.Rot, navy);
                    g.Blob(c, dv * 1.7f, pv * 0.85f, s, _radii, 10, d.Rot, mid);
                    g.Blob(c - dv * s * 0.25f, dv * 1.4f, pv * 0.7f, s * 0.55f, _radii, 10, d.Rot + 0.7f, dark);
                    g.Blob(c - dv * s * 0.5f + pv * s * 0.2f, gr, gu, s * 0.18f, null, 6, 0, light);
                    Dot(g, c + dv * s * 1.95f, gr, gu, s * 0.26f, fade);
                    Dot(g, c + dv * s * 2.6f, gr, gu, s * 0.15f, fade);
                    break;
                }
                case 2:
                {
                    // Crown: a spiky impact star with four flung dots. 120 verts.
                    g.Star(c, gr, gu, s * 1.3f + 0.04f, 0.55f, 9, d.Rot, navy);
                    g.Star(c, gr, gu, s * 1.3f, 0.55f, 9, d.Rot, mid);
                    Lobes(sd, 10, 10, 0.8f, 1.1f);
                    g.Blob(c, gr, gu, s * 0.6f, _radii, 10, d.Rot + 0.4f, dark);
                    g.Blob(c + (pv - dv) * s * 0.18f, gr, gu, s * 0.17f, null, 6, 0, light);
                    for (int k = 0; k < 4; k++)
                    {
                        float a = d.DirA + (R01(sd, (uint)k + 30) - 0.5f) * 2.4f;
                        Dot(g, c + (gr * Mathf.Cos(a) + gu * Mathf.Sin(a)) * s * (1.6f + 0.6f * R01(sd, (uint)k + 31)), gr, gu, s * (0.12f + 0.1f * R01(sd, (uint)k + 32)), fade);
                    }
                    break;
                }
                case 3:
                {
                    // Cluster: three overlapping lobes of falling size. 70 verts.
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 o = dv * (s * (k * 0.55f - 0.3f)) + pv * (s * (R01(sd, (uint)k + 40) - 0.5f) * 0.7f);
                        float r = s * (0.66f - 0.14f * k);
                        Lobes(sd, (uint)(50 + k * 8), 8, 0.8f, 1.15f);
                        g.Blob(c + o, gr, gu, r + 0.03f, _radii, 8, d.Rot + k, navy);
                        g.Blob(c + o, gr, gu, r, _radii, 8, d.Rot + k, mid);
                    }
                    g.Blob(c - dv * s * 0.25f, gr, gu, s * 0.32f, _radii, 8, d.Rot, dark);
                    g.Blob(c + pv * s * 0.2f, gr, gu, s * 0.14f, null, 6, 0, light);
                    break;
                }
                case 4:
                {
                    // Smear: a dragged stroke along the flight with round ends. 65 verts.
                    Vector3 mc = c + dv * s * 0.75f;
                    g.Quad(mc, dv * s * 0.75f, pv * (s * 0.42f + 0.03f), navy, navy);
                    g.Blob(c, gr, gu, s * 0.42f + 0.03f, null, 9, 0, navy);
                    g.Blob(c + dv * s * 1.5f, gr, gu, s * 0.5f + 0.03f, null, 9, 0, navy);
                    g.Quad(mc, dv * s * 0.75f, pv * (s * 0.42f), mid, mid);
                    g.Blob(c, gr, gu, s * 0.42f, null, 9, 0, mid);
                    g.Blob(c + dv * s * 1.5f, gr, gu, s * 0.5f, null, 9, 0, mid);
                    g.Blob(c + dv * s * 1.45f, gr, gu, s * 0.26f, null, 9, 0, dark);
                    g.Blob(c + dv * s * 0.6f + pv * s * 0.15f, gr, gu, s * 0.12f, null, 6, 0, light);
                    break;
                }
                case 5:
                {
                    // Spatter fan: a small body and six dots flung in a fan along the flight. 121 verts.
                    g.Blob(c, gr, gu, s * 0.55f + 0.03f, null, 8, 0, navy);
                    g.Blob(c, gr, gu, s * 0.55f, null, 8, 0, mid);
                    for (int k = 0; k < 6; k++)
                    {
                        float a = d.DirA + (k / 5f - 0.5f) * 1.4f + (R01(sd, (uint)k + 60) - 0.5f) * 0.2f;
                        float dist = s * (0.9f + 1.3f * R01(sd, (uint)k + 61));
                        Dot(g, c + (gr * Mathf.Cos(a) + gu * Mathf.Sin(a)) * dist, gr, gu, s * (0.12f + 0.13f * R01(sd, (uint)k + 62)), fade);
                    }
                    g.Blob(c + pv * s * 0.12f, gr, gu, s * 0.16f, null, 6, 0, light);
                    break;
                }
                case 6:
                {
                    // Knock-out pool: a big lobed puddle (it spreads over a second via Grow) with four side lobes. 124 verts.
                    Lobes(sd, 70, 14, 0.85f, 1.15f);
                    g.Blob(c, gr, gu, s * 1.08f + 0.04f, _radii, 14, d.Rot, navy);
                    g.Blob(c, gr, gu, s, _radii, 14, d.Rot, mid);
                    g.Blob(c + dv * s * 0.1f, gr, gu, s * 0.62f, _radii, 14, d.Rot + 0.5f, dark);
                    g.Blob(c - dv * s * 0.35f + pv * s * 0.3f, gr, gu, s * 0.16f, null, 6, 0, light);
                    for (int k = 0; k < 4; k++)
                    {
                        float a = d.Rot + k * Mathf.PI * 0.5f + R01(sd, (uint)k + 80) * 0.8f;
                        Vector3 o = (gr * Mathf.Cos(a) + gu * Mathf.Sin(a)) * s * 0.95f;
                        g.Blob(c + o, gr, gu, s * 0.32f + 0.035f, null, 8, 0, navy);
                        g.Blob(c + o, gr, gu, s * 0.32f, null, 8, 0, mid);
                    }
                    break;
                }
                default:
                {
                    // Round splat with three satellites. 94 verts.
                    Lobes(sd, 0, 12, 0.75f, 1.2f);
                    g.Blob(c, gr, gu, s * 1.14f + 0.03f, _radii, 12, d.Rot, navy);
                    g.Blob(c, gr, gu, s, _radii, 12, d.Rot, mid);
                    Vector3 off = -dv * s * 0.18f;
                    g.Blob(c + off, gr, gu, s * 0.56f, _radii, 12, d.Rot + 0.9f, dark);
                    g.Blob(c - off * 1.6f + (gr * 0.3f + gu * 0.25f) * s * 0.4f, gr, gu, s * 0.2f, null, 6, 0, light);
                    for (int k = 0; k < 3; k++)
                    {
                        float a = d.DirA + (R01(sd, (uint)k + 30) - 0.5f) * 1.1f;
                        float dist = s * (1.3f + 0.7f * R01(sd, (uint)k + 31));
                        Dot(g, c + (gr * Mathf.Cos(a) + gu * Mathf.Sin(a)) * dist, gr, gu, s * (0.16f + 0.14f * R01(sd, (uint)k + 32)), fade);
                    }
                    break;
                }
            }
        }
    }
}
