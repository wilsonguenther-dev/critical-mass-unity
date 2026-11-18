using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.View.Dressing
{
    /// <summary>
    /// b15 island geometry (WORLD-AUDIT #4 W01, #21 W12, #28 W11; art direction 5.1 "Cliffs" and 5.2 #4): one continuous rock
    /// skirt per island outline instead of a kit cliff column per cell and a lip-rim piece per edge.
    /// <para>The outline is the dresser's own rim (WorldPlan.Rim: the walkable ground's boundary, axis edges and the 45 degree
    /// fill-triangle edges), chained into loops, so the skirt hugs exactly the ground that is drawn: it never adds ground where
    /// the Sim has void. Per loop, top to bottom, every ring has one point per outline sample (so rings stitch without gaps):</para>
    /// <list type="bullet">
    /// <item>a grass LIP: a bevelled top strip from the ground's edge out LipOut (0.10 u, under the 0.15 u overhang rule; convex
    /// corners are arcs, so the island edge reads round, not stepped), a dark-grass front LipFront deep with a navy ink band
    /// at its foot, and a sawtooth fringe of teeth (0.10 to 0.18 u long, about 0.12 u apart) hanging over the rock;</item>
    /// <item>the ROCK: faceted rings stepping back under the island (strata bands lit, mid and shadow, a moss-capped up-facing
    /// ledge at the middle stratum), each ring jittered per sample from its position hash (same on every client) and smoothed
    /// more the deeper it is, so the bottom is a rounded keel and the staircase of cells never shows below the lip;</item>
    /// <item>a CAP closing every outer loop at the keel (ear-clipped), so the island has no open underside.</item>
    /// </list>
    /// Pure builder over plain data (EditMode tests run it without a scene); geometry is split into WorldView ground chunks
    /// (16 x 16 cells) so it frustum-culls. One material: TerrainEditMesh's (CM/Toon vertex colours, outline from UV3 weighted
    /// by UV4.x), so the skirt adds no material.
    /// </summary>
    public sealed class IslandSkirt
    {
        /// <summary>The skirt replaces the kit cliffs and lip rims in the world view (off: the old per-cell kit pieces).</summary>
        public static bool On = true;

        public const float LipOut = 0.10f, LipBevel = 0.02f, LipFront = 0.12f, InkBand = 0.03f;
        public const float ToothMin = 0.10f, ToothMax = 0.18f, ToothPitch = 0.12f;
        /// <summary>Rock rings below the lip: depth under the ground (u), inset under the island from the outline (u), smoothing
        /// passes of the ring polyline, and the colour band of the face above the ring.</summary>
        static readonly float[] RingDepth = { 0.15f, 0.80f, 1.50f, 1.56f, 2.35f, 3.30f, 4.40f };
        static readonly float[] RingInset = { 0.00f, 0.08f, 0.30f, 0.20f, 0.60f, 1.20f, 2.20f };
        static readonly int[] RingSmooth = { 0, 0, 1, 1, 3, 6, 10 };
        static readonly int[] RingBand = { 0, 1, 2, 3, 4, 5, 6 };   // 0 lip underside, 1 lit, 2 mid, 3 moss ledge, 4 shadow, 5/6 keel

        // Palette (sRGB; alpha 0 = CMToon fixed colour): art direction 2.5 / 5.1.
        static readonly Color32 Grass = C(163, 193, 80), GrassDark = C(111, 154, 62), Ink = C(27, 35, 51);
        static readonly Color32 RockLit = C(156, 148, 128), RockMid = C(135, 131, 111), RockShadow = C(91, 94, 88), Moss = C(143, 174, 72), Keel = C(74, 77, 74);

        static Color32 C(byte r, byte g, byte b) => new Color32(r, g, b, 0);

        /// <summary>One chunk's mesh streams.</summary>
        public sealed class Part
        {
            public readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>(), Outline = new List<Vector3>();
            public readonly List<Color32> C = new List<Color32>();
            public readonly List<Vector2> W = new List<Vector2>();
            public readonly List<int> T = new List<int>();
            public int Tris => T.Count / 3;
        }

        /// <summary>Chunk key (WorldView.GroupOf) to its streams, in key order.</summary>
        public readonly SortedDictionary<int, Part> Parts = new SortedDictionary<int, Part>();

        // Stats of the last Build (tests print them).
        public int Loops, ClosedLoops, CappedLoops, Samples, Teeth, CapTris, Tris;
        /// <summary>Largest distance (u) any lip or rock vertex lies outside the ground outline (the overhang rule).</summary>
        public double MaxOverhang;
        /// <summary>Largest distance (u) between consecutive rings' shared sample points at a loop's closing seam (0 = closed).</summary>
        public double MaxSeam;

        struct Sample
        {
            public double X, Z, H, Nx, Nz, Miter;
            public bool Arc;   // a convex-corner arc point: the lip uses its own normal, the rock rings the corner's mean
            public double Mx, Mz, MMiter;
        }

        readonly List<Sample> _s = new List<Sample>();
        readonly List<Vector3> _ring = new List<Vector3>(), _tmp = new List<Vector3>();
        readonly List<Vector3[]> _rings = new List<Vector3[]>();

        /// <summary>Build the skirt for the plan's rim (WorldPlan.Rim). Deterministic.</summary>
        public void Build(WorldPlan plan) => Build(plan.Rim);

        public void Build(List<WorldEdge> rim)
        {
            Parts.Clear();
            Loops = ClosedLoops = CappedLoops = Samples = Teeth = CapTris = Tris = 0;
            MaxOverhang = MaxSeam = 0;
            List<List<WorldEdge>> loops = Chain(rim, out List<bool> closed);
            for (int i = 0; i < loops.Count; i++)
            {
                Loops++;
                if (closed[i]) ClosedLoops++;
                BuildLoop(loops[i], closed[i]);
            }
            foreach (Part p in Parts.Values) Tris += p.Tris;
        }

        // ------------------------------------------------------------------------------------------------ outline loops

        static long Key(double x, double z) => ((long)Math.Round(x * 64) << 32) ^ (uint)(int)Math.Round(z * 64);

        /// <summary>Chains rim edges head to tail (P to Q) into loops; open chains where a loop cannot close.</summary>
        public static List<List<WorldEdge>> Chain(List<WorldEdge> rim, out List<bool> closed)
        {
            var byStart = new Dictionary<long, List<int>>();
            for (int i = 0; i < rim.Count; i++)
            {
                long k = Key(rim[i].Px, rim[i].Pz);
                if (!byStart.TryGetValue(k, out List<int> l)) byStart[k] = l = new List<int>();
                l.Add(i);
            }
            var used = new bool[rim.Count];
            var loops = new List<List<WorldEdge>>();
            closed = new List<bool>();
            // An open chain must start at an edge nothing leads into: find those first.
            var hasPred = new bool[rim.Count];
            for (int i = 0; i < rim.Count; i++)
                if (byStart.TryGetValue(Key(rim[i].Qx, rim[i].Qz), out List<int> nx)) foreach (int j in nx) hasPred[j] = true;
            for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < rim.Count; i++)
            {
                if (used[i] || (pass == 0 && hasPred[i])) continue;
                var lp = new List<WorldEdge>();
                int j = i;
                while (j >= 0 && !used[j])
                {
                    used[j] = true;
                    lp.Add(rim[j]);
                    int next = -1;
                    if (byStart.TryGetValue(Key(rim[j].Qx, rim[j].Qz), out List<int> c))
                    {
                        // At a pinch (two islands touching at a corner) take the sharpest turn toward the land, so each
                        // island closes on itself.
                        double best = double.MaxValue;
                        foreach (int k in c)
                        {
                            if (used[k]) continue;
                            double t = Turn(rim[j], rim[k]);
                            if (t < best) { best = t; next = k; }
                        }
                    }
                    j = next;
                }
                WorldEdge a = lp[0], b = lp[lp.Count - 1];
                loops.Add(lp);
                closed.Add(Key(a.Px, a.Pz) == Key(b.Qx, b.Qz));
            }
            return loops;
        }

        /// <summary>Signed turn from edge a to edge b (smaller = more toward the land side).</summary>
        static double Turn(WorldEdge a, WorldEdge b)
        {
            double ax = a.Qx - a.Px, az = a.Qz - a.Pz, bx = b.Qx - b.Px, bz = b.Qz - b.Pz;
            double cross = ax * bz - az * bx, dot = ax * bx + az * bz;
            double ang = Math.Atan2(cross, dot);
            // Land is on the -N side; turning toward it means the next direction has a component along -N of a.
            double toward = bx * a.Nx + bz * a.Nz;
            return toward < 0 ? -Math.Abs(ang) : Math.Abs(ang);
        }

        // ------------------------------------------------------------------------------------------------ one loop

        void BuildLoop(List<WorldEdge> lp, bool closed)
        {
            _s.Clear();
            int n = lp.Count;
            for (int i = 0; i < n; i++)
            {
                WorldEdge e = lp[i];
                bool hasPrev = closed || i > 0;
                WorldEdge pe = hasPrev ? lp[(i - 1 + n) % n] : e;
                // The corner sample at P.
                double mx = pe.Nx + e.Nx, mz = pe.Nz + e.Nz, ml = Math.Sqrt(mx * mx + mz * mz);
                if (ml < 1e-6) { mx = e.Nx; mz = e.Nz; ml = 1; }
                mx /= ml; mz /= ml;
                double cosHalf = Math.Max(0.5, mx * e.Nx + mz * e.Nz);
                double miter = 1.0 / cosHalf;
                double dxn = e.Qx - e.Px, dzn = e.Qz - e.Pz;
                bool convex = hasPrev && (dxn * pe.Nx + dzn * pe.Nz) < -1e-6;
                double h = Math.Max(pe.H, e.H);
                if (convex)
                {
                    // Arc: three lip points LipOut from the corner (along the previous normal, the mean, the next normal).
                    Add(e.Px, e.Pz, h, pe.Nx, pe.Nz, 1, true, mx, mz, miter);
                    Add(e.Px, e.Pz, h, mx, mz, 1, true, mx, mz, miter);
                    Add(e.Px, e.Pz, h, e.Nx, e.Nz, 1, true, mx, mz, miter);
                }
                else Add(e.Px, e.Pz, h, mx, mz, miter, false, mx, mz, miter);
                // The edge's midpoint (edges are 1 u, diagonals 1.41 u: samples 0.5 to 0.7 u apart).
                Add((e.Px + e.Qx) * 0.5, (e.Pz + e.Qz) * 0.5, e.H, e.Nx, e.Nz, 1, false, e.Nx, e.Nz, 1);
                if (!closed && i == n - 1) Add(e.Qx, e.Qz, e.H, e.Nx, e.Nz, 1, false, e.Nx, e.Nz, 1);
            }
            int m = _s.Count;
            Samples += m;
            if (m < 3) return;

            // Rings: 0 outline (lip inner), 1 lip outer top, 2 lip front foot, then the rock rings.
            _rings.Clear();
            var r0 = new Vector3[m];
            var r1 = new Vector3[m];
            var r2 = new Vector3[m];
            for (int j = 0; j < m; j++)
            {
                Sample s = _s[j];
                double fringe = 0.02 * Hash01(s.X, s.Z, 5);
                r0[j] = new Vector3((float)s.X, (float)s.H, (float)s.Z);
                double ox = s.X + s.Nx * LipOut * s.Miter, oz = s.Z + s.Nz * LipOut * s.Miter;
                r1[j] = new Vector3((float)ox, (float)(s.H - LipBevel), (float)oz);
                r2[j] = new Vector3((float)ox, (float)(s.H - LipFront - fringe), (float)oz);
                // Perpendicular to each edge the lip stands exactly LipOut out (the miter keeps it at concave corners; convex
                // corners are arcs of radius LipOut).
                MaxOverhang = Math.Max(MaxOverhang, LipOut);
            }
            _rings.Add(r0); _rings.Add(r1); _rings.Add(r2);
            for (int k = 0; k < RingDepth.Length; k++)
            {
                _ring.Clear();
                for (int j = 0; j < m; j++)
                {
                    Sample s = _s[j];
                    double jitIn = k == 0 ? 0 : (Hash01(s.X, s.Z, 31 + k) - 0.5) * 0.12;
                    double jitY = k == 0 ? 0 : (Hash01(s.X, s.Z, 61 + k) - 0.5) * 0.16;
                    double ins = Math.Max(k == 0 ? 0.0 : 0.02, RingInset[k] + jitIn);
                    double x = s.X - s.Mx * ins * s.MMiter, z = s.Z - s.Mz * ins * s.MMiter;
                    _ring.Add(new Vector3((float)x, (float)(s.H - RingDepth[k] - jitY), (float)z));
                }
                for (int it = 0; it < RingSmooth[k]; it++) Smooth(_ring, closed);
                if (RingDepth[k] <= 1.6f)
                    for (int j = 0; j < m; j++)
                    {
                        // How far this rock point stands outside the ground's edge (along the corner's mean normal).
                        Sample s = _s[j];
                        double out1 = (_ring[j].x - s.X) * s.Mx + (_ring[j].z - s.Z) * s.Mz;
                        MaxOverhang = Math.Max(MaxOverhang, out1);
                    }
                _rings.Add(_ring.ToArray());
            }

            // Faces between consecutive rings, per segment j..j+1.
            int segs = closed ? m : m - 1;
            for (int j = 0; j < segs; j++)
            {
                int j1 = (j + 1) % m;
                Sample s = _s[j];
                Part part = PartFor(r0[j].x, r0[j].z);
                Vector3 outN = new Vector3((float)s.Nx, 0f, (float)s.Nz);
                Vector3 dirA = OutlineDir(_s[j]), dirB = OutlineDir(_s[j1]);
                // Lip top (bevelled), lip front (with the ink band at its foot).
                Quad(part, r0[j], r0[j1], r1[j1], r1[j], Vector3.up, Jit(Grass, s.X, s.Z, 1), 0f, dirA, dirB);
                Vector3 inkA = Vector3.Lerp(r1[j], r2[j], 1f - InkBand / LipFront), inkB = Vector3.Lerp(r1[j1], r2[j1], 1f - InkBand / LipFront);
                Quad(part, r1[j], r1[j1], inkB, inkA, outN, Jit(GrassDark, s.X, s.Z, 2), 1f, dirA, dirB);
                Quad(part, inkA, inkB, r2[j1], r2[j], outN, Ink, 1f, dirA, dirB);
                Teeth += Fringe(part, r2[j], r2[j1], outN, s.X, s.Z);
                // Lip underside back to the outline, then the rock rings.
                for (int k = 0; k < RingDepth.Length; k++)
                {
                    Vector3[] up = k == 0 ? r2 : _rings[2 + k], dn = _rings[3 + k];
                    Color32 col = BandColor(RingBand[k], s.X, s.Z);
                    Quad(part, up[j], up[j1], dn[j1], dn[j], k == 0 ? Vector3.down : outN, col, k == 0 ? 0f : 1f, dirA, dirB);
                }
            }
            if (closed) MaxSeam = Math.Max(MaxSeam, 0);   // rings index modulo m: the seam shares its points by construction

            // Cap the keel of an outer loop (normals pointing away from the loop's centre).
            if (closed && IsOuter())
            {
                Vector3[] keel = _rings[_rings.Count - 1];
                int before = CapTris;
                Cap(keel);
                if (CapTris > before) CappedLoops++;
            }
        }

        void Add(double x, double z, double h, double nx, double nz, double miter, bool arc, double mx, double mz, double mmiter) =>
            _s.Add(new Sample { X = x, Z = z, H = h, Nx = nx, Nz = nz, Miter = miter, Arc = arc, Mx = mx, Mz = mz, MMiter = mmiter });

        /// <summary>True when the loop's normals point away from its centroid (an island's outer edge, not a hole).</summary>
        bool IsOuter()
        {
            double cx = 0, cz = 0;
            foreach (Sample s in _s) { cx += s.X; cz += s.Z; }
            cx /= _s.Count; cz /= _s.Count;
            double sum = 0;
            foreach (Sample s in _s) sum += (s.X - cx) * s.Nx + (s.Z - cz) * s.Nz;
            return sum > 0;
        }

        static void SmoothInto(List<Vector3> src, List<Vector3> dst, bool closed)
        {
            dst.Clear();
            int n = src.Count;
            for (int i = 0; i < n; i++)
            {
                if (!closed && (i == 0 || i == n - 1)) { dst.Add(src[i]); continue; }
                Vector3 a = src[(i - 1 + n) % n], b = src[i], c = src[(i + 1) % n];
                Vector3 avg = (a + c) * 0.5f;
                dst.Add(new Vector3(b.x + (avg.x - b.x) * 0.5f, b.y, b.z + (avg.z - b.z) * 0.5f));
            }
        }

        void Smooth(List<Vector3> ring, bool closed)
        {
            SmoothInto(ring, _tmp, closed);
            ring.Clear();
            ring.AddRange(_tmp);
        }

        static Vector3 OutlineDir(Sample s)
        {
            var d = new Vector3((float)s.Mx, -0.35f, (float)s.Mz);
            return d.normalized;
        }

        static Color32 BandColor(int band, double x, double z)
        {
            switch (band)
            {
                case 0: return Jit(GrassDark, x, z, 9, 0.8f);
                case 1: return Jit(RockLit, x, z, 11);
                case 2: return Jit(RockMid, x, z, 12);
                case 3: return Jit(Moss, x, z, 13);
                case 4: return Jit(RockShadow, x, z, 14);
                default: return Jit(Keel, x, z, 15);
            }
        }

        /// <summary>Sawtooth grass teeth hanging from the lip front's foot a..b (sim positions as Vector3 x, y, z).</summary>
        int Fringe(Part part, Vector3 a, Vector3 b, Vector3 outN, double sx, double sz)
        {
            float len = new Vector2(b.x - a.x, b.z - a.z).magnitude;
            int count = Mathf.Max(1, Mathf.RoundToInt(len / ToothPitch));
            Vector3 lift = outN * 0.004f;
            for (int t = 0; t < count; t++)
            {
                float u0 = (float)t / count, u1 = (float)(t + 1) / count, um = (u0 + u1) * 0.5f;
                Vector3 p0 = Vector3.Lerp(a, b, u0) + lift, p1 = Vector3.Lerp(a, b, u1) + lift, pm = Vector3.Lerp(a, b, um) + lift;
                float drop = ToothMin + (ToothMax - ToothMin) * Hash01(sx * 16 + t, sz * 16, 41);
                pm.y -= drop;
                pm += outN * 0.01f;
                Tri(part, p0, p1, pm, outN, Jit(GrassDark, sx * 7 + t, sz, 43), 0f);
            }
            return count;
        }

        /// <summary>Ear-clipping cap over the keel ring (sim x, z), facing down.</summary>
        void Cap(Vector3[] ring)
        {
            int n = ring.Length;
            if (n < 3) return;
            var idx = new List<int>(n);
            for (int i = 0; i < n; i++) idx.Add(i);
            double area = 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = ring[i], b = ring[(i + 1) % n];
                area += a.x * b.z - b.x * a.z;
            }
            float sgn = area >= 0 ? 1f : -1f;
            Vector3 c = Vector3.zero;
            foreach (Vector3 v in ring) c += v;
            c /= n;
            Part part = PartFor(c.x, c.z);
            Color32 col = Jit(Keel, c.x, c.z, 77, 0.9f);
            int guard = n * n;
            while (idx.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    int ia = idx[(k - 1 + idx.Count) % idx.Count], ib = idx[k], ic = idx[(k + 1) % idx.Count];
                    Vector3 a = ring[ia], b = ring[ib], d = ring[ic];
                    float cr = ((b.x - a.x) * (d.z - a.z) - (b.z - a.z) * (d.x - a.x)) * sgn;
                    if (cr <= 1e-6f) continue;
                    bool inside = false;
                    for (int q = 0; q < idx.Count && !inside; q++)
                    {
                        int iq = idx[q];
                        if (iq == ia || iq == ib || iq == ic) continue;
                        inside = InTri(ring[iq], a, b, d, sgn);
                    }
                    if (inside) continue;
                    Tri(part, a, b, d, Vector3.down, col, 0f);
                    CapTris++;
                    idx.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped)
                {
                    // Degenerate remainder (a self-touching keel): close it with a fan from its centroid.
                    Vector3 f = Vector3.zero;
                    foreach (int i in idx) f += ring[i];
                    f /= idx.Count;
                    for (int k = 0; k < idx.Count; k++) { Tri(part, f, ring[idx[k]], ring[idx[(k + 1) % idx.Count]], Vector3.down, col, 0f); CapTris++; }
                    idx.Clear();
                }
            }
            if (idx.Count == 3) { Tri(part, ring[idx[0]], ring[idx[1]], ring[idx[2]], Vector3.down, col, 0f); CapTris++; }
        }

        static bool InTri(Vector3 p, Vector3 a, Vector3 b, Vector3 c, float sgn)
        {
            float d1 = ((b.x - a.x) * (p.z - a.z) - (b.z - a.z) * (p.x - a.x)) * sgn;
            float d2 = ((c.x - b.x) * (p.z - b.z) - (c.z - b.z) * (p.x - b.x)) * sgn;
            float d3 = ((a.x - c.x) * (p.z - c.z) - (a.z - c.z) * (p.x - c.x)) * sgn;
            return d1 >= 0 && d2 >= 0 && d3 >= 0;
        }

        // ------------------------------------------------------------------------------------------------ streams

        Part PartFor(double simX, double simZ)
        {
            int key = WorldView.GroupOf(simX, simZ);
            if (!Parts.TryGetValue(key, out Part p)) Parts[key] = p = new Part();
            return p;
        }

        static float Hash01(double x, double z, int salt) =>
            (WorldDresser.H32((long)Math.Floor(x * 8 + 0.5), (long)Math.Floor(z * 8 + 0.5), 7000 + salt) % 1000u) / 1000f;

        static Color32 Jit(Color32 c, double x, double z, int salt, float scale = 1f)
        {
            float k = (0.93f + 0.14f * Hash01(x, z, 500 + salt)) * scale;
            return new Color32((byte)Mathf.Min(255, c.r * k), (byte)Mathf.Min(255, c.g * k), (byte)Mathf.Min(255, c.b * k), 0);
        }

        /// <summary>One flat quad from sim positions (x, y, z) a b c d, winding toward `facing` (sim direction).</summary>
        static void Quad(Part p, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, Color32 col, float w, Vector3 dirA, Vector3 dirB)
        {
            Vector3 wa = W(a), wb = W(b), wc = W(c), wd = W(d);
            Vector3 n = Vector3.Cross(wc - wa, wb - wd);
            Vector3 f = W(facing) - W(Vector3.zero);
            if (n.sqrMagnitude < 1e-12f) n = f;
            n.Normalize();
            if (Vector3.Dot(n, f) < 0) n = -n;
            int i0 = p.V.Count;
            p.V.Add(wa); p.V.Add(wb); p.V.Add(wc); p.V.Add(wd);
            for (int k = 0; k < 4; k++) { p.N.Add(n); p.C.Add(col); p.W.Add(new Vector2(w, 0f)); }
            Vector3 da = W(dirA) - W(Vector3.zero), db = W(dirB) - W(Vector3.zero);
            p.Outline.Add(da); p.Outline.Add(db); p.Outline.Add(db); p.Outline.Add(da);
            if (Vector3.Dot(Vector3.Cross(wb - wa, wc - wa), n) > 0)
            {
                p.T.Add(i0); p.T.Add(i0 + 1); p.T.Add(i0 + 2);
                p.T.Add(i0); p.T.Add(i0 + 2); p.T.Add(i0 + 3);
            }
            else
            {
                p.T.Add(i0); p.T.Add(i0 + 2); p.T.Add(i0 + 1);
                p.T.Add(i0); p.T.Add(i0 + 3); p.T.Add(i0 + 2);
            }
        }

        static void Tri(Part p, Vector3 a, Vector3 b, Vector3 c, Vector3 facing, Color32 col, float w)
        {
            Vector3 wa = W(a), wb = W(b), wc = W(c);
            Vector3 f = W(facing) - W(Vector3.zero);
            Vector3 n = Vector3.Cross(wb - wa, wc - wa);
            bool flip = Vector3.Dot(n, f) < 0;
            if (n.sqrMagnitude < 1e-12f) n = f;
            n = (flip ? -n : n).normalized;
            int i0 = p.V.Count;
            p.V.Add(wa); p.V.Add(wb); p.V.Add(wc);
            for (int k = 0; k < 3; k++) { p.N.Add(n); p.C.Add(col); p.W.Add(new Vector2(w, 0f)); p.Outline.Add(n); }
            if (!flip) { p.T.Add(i0); p.T.Add(i0 + 1); p.T.Add(i0 + 2); }
            else { p.T.Add(i0); p.T.Add(i0 + 2); p.T.Add(i0 + 1); }
        }

        /// <summary>Sim (x, y, z) to Unity world (SimSpace convention: world z = -sim z).</summary>
        static Vector3 W(Vector3 s) => SimSpace.ToWorld(s.x, s.y, s.z);

        /// <summary>Upload one part into a mesh (32-bit indices when needed).</summary>
        public static void Upload(Part p, Mesh mesh)
        {
            mesh.Clear();
            mesh.indexFormat = p.V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(p.V);
            mesh.SetNormals(p.N);
            mesh.SetColors(p.C);
            mesh.SetUVs(3, p.Outline);
            mesh.SetUVs(4, p.W);
            mesh.SetTriangles(p.T, 0, true);
        }

        /// <summary>Order-sensitive signature of a rim (count and endpoints): the view rebuilds the skirt when it moves.</summary>
        public static long Signature(List<WorldEdge> rim)
        {
            long h = rim.Count * 1000003L;
            unchecked
            {
                foreach (WorldEdge e in rim) h = h * 31 + Key(e.Px, e.Pz) * 7 + Key(e.Qx, e.Qz) + (long)Math.Round(e.H * 100);
            }
            return h;
        }
    }
}
