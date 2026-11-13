using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.Hud.Ui
{
    /// <summary>What the bake draws at a sim ground point (tests check features against the sim map with it).</summary>
    public enum MinimapClass : byte
    {
        Void, Outline, Cliff, Underside, Coast, Grass, Path, Plaza, Pad, Hill, Bridge, Wall, Breakable, Rubble, Barricade,
        Water, Biomass, Corruption, Pillar,
    }

    /// <summary>
    /// The minimap image, generated from the game's own map data (MapDef: cells, heights, pads, hill, shrine, biomass and
    /// corruption nodes, decorations) in the HUD's illustrated language: chunky land with a Void Navy outline, grass with a
    /// light sliver on the upper-left rim and a shade crescent on the lower right, paved lanes from every bridge end to its
    /// island's hub, team-tinted base plazas around each pad, the hill's paved ring, stone walls with a front face, cracked
    /// breakables, plank bridges with rails, cliff faces under every island (deeper for higher islands, moss drips at the
    /// lip), purple corruption, biomass orbs, over a navy sky with a lighter haze around the land.
    /// Pure and deterministic (no Unity state, no randomness: a hash of the position), so the editor bake
    /// (Editor/MinimapBaker) and the EditMode drift test produce identical pixels.
    /// </summary>
    public sealed class MinimapBake
    {
        public const int Supersample = 3;
        /// <summary>Fine field resolution, samples per sim unit.</summary>
        const int R = 10;
        /// <summary>Fine field padding around the map, sim units.</summary>
        const int Pad = 6;

        // outline widths in 1x pixels (they scale with the tier, so every tier is drawn natively at its own resolution)
        const double OutlinePx = 1.6, CoastPx = 0.9, WallLinePx = 0.7;
        const double WallHalf = 0.62, PathHalf = 0.72, PlazaExtra = 3.6, YardR = 5.6;

        static Color H(string hex) => CMTokens.ParseHex(hex);
        static readonly Color Navy = H("#18233B"), Sky = H("#1E2D4A"), Haze = H("#2E4A70");
        static readonly Color GrassMid = H("#77B247"), GrassHigh = H("#8BC453"), GrassLow = H("#649F3E"), GrassLight = H("#B4DD72"),
            GrassShade = H("#4E8838"), Tuft = H("#5C9A3A");
        static readonly Color CliffTop = H("#A39684"), CliffBot = H("#5E5866"), CliffCrack = H("#454052"), Moss = H("#6DA640");
        static readonly Color Pave = H("#E4D6B6"), PaveJoint = H("#B9A684"), PaveEdge = H("#9E8C6E");
        static readonly Color Plank = H("#C99258"), Plank2 = H("#B47E4A"), PlankGap = H("#6E4B2E"), Rail = H("#5A3B25"), Underside = H("#4A3122");
        static readonly Color WallTop = H("#C2BFC8"), WallFace = H("#6F7282"), BreakTop = H("#C9A57C"), BreakFace = H("#8A6A4C");
        static readonly Color Water = H("#3FA7D6"), WaterLight = H("#8FD6F2");
        static readonly Color Corrupt = H("#8E3FD1"), CorruptDark = H("#56208A"), CorruptLight = H("#C27FEB");
        static readonly Color Bio = H("#B7F238"), BioDark = H("#5E9A1E"), Brass = H("#C79245");

        readonly MapDef _map;
        public readonly MinimapProjection Proj;
        readonly int _fw, _fh;
        readonly float[] _sdAll, _sdIsland, _sdUnion, _cliffT, _wallD, _breakD, _crackD, _rubbleD, _barD, _pathD, _plankS, _height;
        readonly byte[] _under;
        readonly Color[] _team = new Color[2];

        public MinimapBake(MapDef map, double yawDeg)
        {
            _map = map;
            Proj = MinimapProjection.For(map, yawDeg);
            _team[0] = CMTokens.Azure.Color;
            _team[1] = CMTokens.Crimson.Color;
            _fw = (map.Width + 2 * Pad) * R;
            _fh = (map.Height + 2 * Pad) * R;
            int n = _fw * _fh;

            // 1) smooth land masks: bilinear over a binomial blur of the cell solidity, contour at 0.5
            float[] all = Blur(c => c != Cell.Void);
            float[] isl = Blur(c => c != Cell.Void && c != Cell.Bridge);
            var mAll = new bool[n];
            var mIsl = new bool[n];
            _height = new float[n];
            for (int j = 0; j < _fh; j++)
                for (int i = 0; i < _fw; i++)
                {
                    double x = FX(i), z = FZ(j);
                    int k = j * _fw + i;
                    mAll[k] = CellBilinear(all, x, z) >= 0.5f;
                    mIsl[k] = CellBilinear(isl, x, z) >= 0.5f;
                    int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
                    if (cx >= 0 && cz >= 0 && cx < map.Width && cz < map.Height && map.Height01 != null)
                        _height[k] = (float)map.Height01[cz * map.Width + cx];
                }
            _sdAll = Signed(mAll);
            _sdIsland = Signed(mIsl);

            // 2) cliff faces: a sample below an island on screen (along screen-down) within the island's cliff depth
            _cliffT = new float[n];
            _under = new byte[n];
            var mUnion = new bool[n];
            double ux = Proj.UpX, uz = Proj.UpZ;
            double maxDepth = MinimapProjection.CliffUnits + MinimapProjection.CliffPerHeight * 2.0;
            int steps = (int)Math.Ceiling(maxDepth * R);
            for (int j = 0; j < _fh; j++)
                for (int i = 0; i < _fw; i++)
                {
                    int k = j * _fw + i;
                    _cliffT[k] = -1f;
                    if (mAll[k]) { mUnion[k] = true; continue; }
                    double x = FX(i), z = FZ(j);
                    for (int s = 1; s <= steps; s++)
                    {
                        double t = s / (double)R;
                        int q = Index(x + ux * t, z + uz * t);
                        if (q < 0) break;
                        if (mIsl[q])
                        {
                            double depth = MinimapProjection.CliffUnits + MinimapProjection.CliffPerHeight * _height[q];
                            if (t <= depth) _cliffT[k] = (float)(t / depth);
                            break;
                        }
                        if (mAll[q])
                        {
                            if (t <= 0.45) _under[k] = 1;   // plank thickness under a bridge
                            break;
                        }
                    }
                    mUnion[k] = _cliffT[k] >= 0f || _under[k] != 0;
                }
            _sdUnion = Signed(mUnion);

            // 3) walls, breakables, barricades: capsule chains through the cell centres (smooth bands, not diamonds)
            _wallD = Skeleton(c => c == Cell.Wall);
            _breakD = Skeleton(c => c == Cell.BreakableIntact);
            _crackD = Skeleton(c => c == Cell.BreakableCracked);
            _rubbleD = Skeleton(c => c == Cell.BreakableDestroyed);
            _barD = Skeleton(c => c == Cell.Barricade);

            // 4) bridges (plank axis per connected bridge) and the paved lanes from each bridge end to its island's hub
            _pathD = Fill(float.MaxValue);
            _plankS = Fill(float.NaN);
            Lanes();
        }

        // ------------------------------------------------------------------ fields

        double FX(int i) => (i + 0.5) / R - Pad;
        double FZ(int j) => (j + 0.5) / R - Pad;

        int Index(double x, double z)
        {
            int i = (int)Math.Floor((x + Pad) * R), j = (int)Math.Floor((z + Pad) * R);
            return i < 0 || j < 0 || i >= _fw || j >= _fh ? -1 : j * _fw + i;
        }

        float[] Fill(float v)
        {
            var a = new float[_fw * _fh];
            for (int i = 0; i < a.Length; i++) a[i] = v;
            return a;
        }

        float Sample(float[] f, double x, double z)
        {
            double gx = (x + Pad) * R - 0.5, gz = (z + Pad) * R - 0.5;
            int i = (int)Math.Floor(gx), j = (int)Math.Floor(gz);
            double fx = gx - i, fz = gz - j;
            float a = At(f, i, j), b = At(f, i + 1, j), c = At(f, i, j + 1), d = At(f, i + 1, j + 1);
            return (float)((a * (1 - fx) + b * fx) * (1 - fz) + (c * (1 - fx) + d * fx) * fz);
        }

        float At(float[] f, int i, int j)
        {
            if (i < 0) i = 0; else if (i >= _fw) i = _fw - 1;
            if (j < 0) j = 0; else if (j >= _fh) j = _fh - 1;
            return f[j * _fw + i];
        }

        float Nearest(float[] f, double x, double z)
        {
            int k = Index(x, z);
            return k < 0 ? f[0] : f[k];
        }

        byte CellAt(int cx, int cz) => cx < 0 || cz < 0 || cx >= _map.Width || cz >= _map.Height ? Cell.Void : _map.Cells[cz * _map.Width + cx];

        float[] Blur(Func<byte, bool> solid)
        {
            int w = _map.Width + 2, h = _map.Height + 2;   // one void cell of border on each side
            var s = new float[w * h];
            for (int cz = -1; cz <= _map.Height; cz++)
                for (int cx = -1; cx <= _map.Width; cx++)
                {
                    float acc = 0;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (solid(CellAt(cx + dx, cz + dz))) acc += (dx == 0 ? 2 : 1) * (dz == 0 ? 2 : 1);
                    s[(cz + 1) * w + cx + 1] = acc / 16f;
                }
            return s;
        }

        float CellBilinear(float[] s, double x, double z)
        {
            int w = _map.Width + 2, h = _map.Height + 2;
            double gx = x - 0.5 + 1, gz = z - 0.5 + 1;
            int i = (int)Math.Floor(gx), j = (int)Math.Floor(gz);
            double fx = gx - i, fz = gz - j;
            float A(int a, int b) => a < 0 || b < 0 || a >= w || b >= h ? 0f : s[b * w + a];
            return (float)((A(i, j) * (1 - fx) + A(i + 1, j) * fx) * (1 - fz) + (A(i, j + 1) * (1 - fx) + A(i + 1, j + 1) * fx) * fz);
        }

        /// <summary>Signed distance in sim units (negative inside) by two exact Euclidean distance transforms.</summary>
        float[] Signed(bool[] mask)
        {
            float[] dIn = Edt(mask, true), dOut = Edt(mask, false);
            var sd = new float[mask.Length];
            for (int k = 0; k < sd.Length; k++)
                sd[k] = mask[k] ? -(Mathf.Sqrt(dOut[k]) - 0.5f) / R : (Mathf.Sqrt(dIn[k]) - 0.5f) / R;
            return sd;
        }

        /// <summary>Squared distance (samples) to the nearest sample whose mask equals target (Felzenszwalb and Huttenlocher).</summary>
        float[] Edt(bool[] mask, bool target)
        {
            const float Inf = 1e20f;
            int w = _fw, h = _fh, m = Math.Max(w, h);
            var g = new float[w * h];
            for (int k = 0; k < g.Length; k++) g[k] = mask[k] == target ? 0f : Inf;
            var f = new float[m];
            var d = new float[m];
            var v = new int[m];
            var zz = new float[m + 1];
            for (int i = 0; i < w; i++)
            {
                for (int j = 0; j < h; j++) f[j] = g[j * w + i];
                Edt1(f, h, d, v, zz);
                for (int j = 0; j < h; j++) g[j * w + i] = d[j];
            }
            for (int j = 0; j < h; j++)
            {
                for (int i = 0; i < w; i++) f[i] = g[j * w + i];
                Edt1(f, w, d, v, zz);
                for (int i = 0; i < w; i++) g[j * w + i] = d[i];
            }
            return g;
        }

        static void Edt1(float[] f, int n, float[] d, int[] v, float[] z)
        {
            int k = 0;
            v[0] = 0;
            z[0] = float.NegativeInfinity;
            z[1] = float.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                float s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2f * q - 2f * v[k]);
                while (s <= z[k])
                {
                    k--;
                    s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2f * q - 2f * v[k]);
                }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = float.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                d[q] = (q - v[k]) * (q - v[k]) + f[v[k]];
            }
        }

        /// <summary>Distance (sim units) to the chain of segments joining 8-adjacent matching cell centres (or the centre alone).</summary>
        float[] Skeleton(Func<byte, bool> match)
        {
            float[] d = Fill(float.MaxValue);
            for (int cz = 0; cz < _map.Height; cz++)
                for (int cx = 0; cx < _map.Width; cx++)
                {
                    if (!match(CellAt(cx, cz))) continue;
                    double x = cx + 0.5, z = cz + 0.5;
                    Segment(d, x, z, x, z, 1.2);
                    if (match(CellAt(cx + 1, cz))) Segment(d, x, z, x + 1, z, 1.2);
                    if (match(CellAt(cx, cz + 1))) Segment(d, x, z, x, z + 1, 1.2);
                    bool e = match(CellAt(cx + 1, cz)), s = match(CellAt(cx, cz + 1)), w = match(CellAt(cx - 1, cz));
                    if (match(CellAt(cx + 1, cz + 1)) && !e && !s) Segment(d, x, z, x + 1, z + 1, 1.2);
                    if (match(CellAt(cx - 1, cz + 1)) && !w && !s) Segment(d, x, z, x - 1, z + 1, 1.2);
                }
            return d;
        }

        /// <summary>min-writes the distance to segment AB into d, within reach of it.</summary>
        void Segment(float[] d, double ax, double az, double bx, double bz, double reach, float[] along = null, double s0 = 0, double ex = 0, double ez = 0)
        {
            int i0 = Math.Max(0, (int)Math.Floor((Math.Min(ax, bx) - reach + Pad) * R)), i1 = Math.Min(_fw - 1, (int)Math.Ceiling((Math.Max(ax, bx) + reach + Pad) * R));
            int j0 = Math.Max(0, (int)Math.Floor((Math.Min(az, bz) - reach + Pad) * R)), j1 = Math.Min(_fh - 1, (int)Math.Ceiling((Math.Max(az, bz) + reach + Pad) * R));
            double vx = bx - ax, vz = bz - az, len2 = vx * vx + vz * vz;
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    double x = FX(i), z = FZ(j);
                    double t = len2 > 0 ? ((x - ax) * vx + (z - az) * vz) / len2 : 0;
                    if (t < 0) t = 0; else if (t > 1) t = 1;
                    double px = ax + vx * t - x, pz = az + vz * t - z;
                    float dist = (float)Math.Sqrt(px * px + pz * pz);
                    int k = j * _fw + i;
                    if (dist < d[k]) d[k] = dist;
                    if (along != null && dist <= reach) along[k] = (float)((x - ax) * ex + (z - az) * ez + s0);
                }
        }

        void Lanes()
        {
            int w = _map.Width, h = _map.Height;
            // island components (4-connected non-void, non-bridge)
            var island = new int[w * h];
            for (int i = 0; i < island.Length; i++) island[i] = -1;
            var cent = new List<Vector2>();
            var stack = new Stack<int>();
            for (int s = 0; s < w * h; s++)
            {
                byte c = _map.Cells[s];
                if (c == Cell.Void || c == Cell.Bridge || island[s] >= 0) continue;
                int id = cent.Count;
                double sx = 0, sz = 0;
                int count = 0;
                island[s] = id;
                stack.Push(s);
                while (stack.Count > 0)
                {
                    int q = stack.Pop();
                    int qx = q % w, qz = q / w;
                    sx += qx + 0.5; sz += qz + 0.5; count++;
                    for (int dd = 0; dd < 4; dd++)
                    {
                        int nx = qx + (dd == 0 ? 1 : dd == 1 ? -1 : 0), nz = qz + (dd == 2 ? 1 : dd == 3 ? -1 : 0);
                        if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                        int ni = nz * w + nx;
                        byte nc = _map.Cells[ni];
                        if (nc == Cell.Void || nc == Cell.Bridge || island[ni] >= 0) continue;
                        island[ni] = id;
                        stack.Push(ni);
                    }
                }
                cent.Add(new Vector2((float)(sx / count), (float)(sz / count)));
            }
            // hubs: a pad, the hill or the shrine on the island, else its centroid
            var hub = new Vector2[cent.Count];
            for (int k = 0; k < hub.Length; k++) hub[k] = cent[k];
            void Claim(double x, double z)
            {
                int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
                if (cx < 0 || cz < 0 || cx >= w || cz >= h) return;
                int id = island[cz * w + cx];
                if (id >= 0) hub[id] = new Vector2((float)x, (float)z);
            }
            if (_map.Pads != null) foreach (PadDef p in _map.Pads) Claim(p.X, p.Z);
            if (_map.Hill != null) Claim(_map.Hill.X, _map.Hill.Z);
            if (_map.Shrine != null) Claim(_map.Shrine.X, _map.Shrine.Z + YardR - 0.6);   // the yard's gate, not inside the solid

            // bridge components (8-connected): principal axis, ends, plank coordinate
            var seen = new bool[w * h];
            var cells = new List<int>();
            for (int s = 0; s < w * h; s++)
            {
                if (_map.Cells[s] != Cell.Bridge || seen[s]) continue;
                cells.Clear();
                seen[s] = true;
                stack.Push(s);
                while (stack.Count > 0)
                {
                    int q = stack.Pop();
                    cells.Add(q);
                    int qx = q % w, qz = q / w;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = qx + dx, nz = qz + dz;
                            if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                            int ni = nz * w + nx;
                            if (_map.Cells[ni] != Cell.Bridge || seen[ni]) continue;
                            seen[ni] = true;
                            stack.Push(ni);
                        }
                }
                double mx = 0, mz = 0;
                foreach (int q in cells) { mx += q % w + 0.5; mz += q / w + 0.5; }
                mx /= cells.Count; mz /= cells.Count;
                double sxx = 0, sxz = 0, szz = 0;
                foreach (int q in cells)
                {
                    double dx = q % w + 0.5 - mx, dz = q / w + 0.5 - mz;
                    sxx += dx * dx; sxz += dx * dz; szz += dz * dz;
                }
                double ang = 0.5 * Math.Atan2(2 * sxz, sxx - szz);
                double ax = Math.Cos(ang), az = Math.Sin(ang);
                double sMin = double.MaxValue, sMax = double.MinValue;
                foreach (int q in cells)
                {
                    double sp = (q % w + 0.5 - mx) * ax + (q / w + 0.5 - mz) * az;
                    if (sp < sMin) sMin = sp;
                    if (sp > sMax) sMax = sp;
                }
                double e0x = mx + ax * (sMin - 0.5), e0z = mz + az * (sMin - 0.5), e1x = mx + ax * (sMax + 0.5), e1z = mz + az * (sMax + 0.5);
                Segment(_plankFieldDummy ?? (_plankFieldDummy = Fill(float.MaxValue)), e0x, e0z, e1x, e1z, 2.2, _plankS, 0, ax, az);
                for (int end = 0; end < 2; end++)
                {
                    double ex = end == 0 ? e0x : e1x, ez = end == 0 ? e0z : e1z;
                    double ox = end == 0 ? -ax : ax, oz = end == 0 ? -az : az;
                    int id = -1;
                    for (int step = 0; step <= 4 && id < 0; step++)
                    {
                        int cx = (int)Math.Floor(ex + ox * step * 0.5), cz = (int)Math.Floor(ez + oz * step * 0.5);
                        if (cx >= 0 && cz >= 0 && cx < w && cz < h) id = island[cz * w + cx];
                    }
                    if (id < 0) continue;
                    Segment(_pathD, ex, ez, hub[id].x, hub[id].y, PathHalf + 0.5);
                }
            }
        }

        float[] _plankFieldDummy;

        // ------------------------------------------------------------------ shading

        static double Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777216.0;
            }
        }

        static double Noise(double x, double z)
        {
            int i = (int)Math.Floor(x), j = (int)Math.Floor(z);
            double fx = x - i, fz = z - j;
            fx = fx * fx * (3 - 2 * fx);
            fz = fz * fz * (3 - 2 * fz);
            double a = Hash(i, j), b = Hash(i + 1, j), c = Hash(i, j + 1), d = Hash(i + 1, j + 1);
            return (a * (1 - fx) + b * fx) * (1 - fz) + (c * (1 - fx) + d * fx) * fz;
        }

        static double Frac(double v) => v - Math.Floor(v);

        /// <summary>The colour at a sim ground point for a tier (line widths are in 1x pixels), and its class.</summary>
        public Color Shade(double x, double z, int tier, out MinimapClass cls)
        {
            double px1 = 1.0 / (MinimapProjection.PxPerUnit * tier);   // sim units per output pixel
            double u = Proj.U(x, z);
            float dU = Sample(_sdUnion, x, z);
            if (dU > OutlinePx * px1)
            {
                cls = MinimapClass.Void;
                double haze = 1.0 - Smooth(0.0, 4.5, dU);
                return Color.Lerp(Sky, Haze, (float)(haze * 0.55));
            }
            if (dU > 0) { cls = MinimapClass.Outline; return Navy; }
            float dA = Sample(_sdAll, x, z);
            if (dA > 0)
            {
                if (dA < CoastPx * px1 * 0.5) { cls = MinimapClass.Coast; return Navy; }
                float t = Nearest(_cliffT, x, z);
                if (t < 0) { cls = MinimapClass.Underside; return Underside; }
                cls = MinimapClass.Cliff;
                return CliffColour(u, t);
            }
            if (dA > -CoastPx * px1 * 0.5) { cls = MinimapClass.Coast; return Navy; }

            int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
            byte cell = CellAt(cx, cz);
            float dI = Sample(_sdIsland, x, z);
            Color col;
            if (dI > 0 && cell != Cell.Floor) { cls = MinimapClass.Bridge; col = PlankColour(x, z, dA); }
            else if (cell == Cell.Water) { cls = MinimapClass.Water; col = Frac((u + Noise(x, z) * 0.6) * 1.3) < 0.12 ? WaterLight : Water; }
            else col = GroundColour(x, z, dA, out cls);

            // structures on the ground
            Color s;
            if (Structure(x, z, px1, out s, ref cls)) col = s;
            return col;
        }

        static double Smooth(double e0, double e1, double v)
        {
            double t = Math.Max(0, Math.Min(1, (v - e0) / (e1 - e0)));
            return t * t * (3 - 2 * t);
        }

        Color CliffColour(double u, float t)
        {
            double col = u * 0.85;
            int ci = (int)Math.Floor(col);
            double drip = 0.12 + 0.28 * Hash(ci, 7) * Hash(ci, 11);
            if (t < drip && Frac(col) > 0.15 && Frac(col) < 0.85) return Moss;
            Color c = Color.Lerp(CliffTop, CliffBot, Mathf.Pow(t, 0.8f));
            double crackAt = 0.25 + 0.5 * Hash(ci, 3);
            if (Math.Abs(Frac(col) - crackAt) < 0.07 && t > 0.22) return CliffCrack;
            if (Math.Abs(t - (0.55 + 0.1 * Hash(ci, 5))) < 0.035) return Color.Lerp(c, CliffCrack, 0.6f);
            return c;
        }

        Color PlankColour(double x, double z, float dA)
        {
            if (dA > -0.32) return Rail;
            float s = Sample(_plankS, x, z);
            if (float.IsNaN(s)) return Plank;
            double p = s / 0.62;
            if (Frac(p) < 0.16) return PlankGap;
            return Hash((int)Math.Floor(p), 1) > 0.5 ? Plank : Plank2;
        }

        Color GroundColour(double x, double z, float dA, out MinimapClass cls)
        {
            int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
            float hgt = cx >= 0 && cz >= 0 && cx < _map.Width && cz < _map.Height && _map.Height01 != null ? (float)_map.Height01[cz * _map.Width + cx] : 0f;
            // base plazas
            if (_map.Pads != null)
                for (int t = 0; t < _map.Pads.Length && t < 2; t++)
                {
                    PadDef p = _map.Pads[t];
                    double d = Math.Sqrt((x - p.X) * (x - p.X) + (z - p.Z) * (z - p.Z));
                    if (d <= p.R)
                    {
                        cls = MinimapClass.Pad;
                        if (d > p.R - 0.32) return Navy;
                        if (d < p.R * 0.42) return Color.Lerp(_team[t], Color.white, 0.35f);
                        return Color.Lerp(_team[t], Navy, 0.12f);
                    }
                    if (d <= p.R + PlazaExtra)
                    {
                        cls = MinimapClass.Plaza;
                        if (d > p.R + PlazaExtra - 0.42) return Color.Lerp(_team[t], Navy, 0.25f);
                        Color pave = Color.Lerp(PaveColour(x, z), _team[t], 0.32f);
                        return pave;
                    }
                }
            // hill
            if (_map.Hill != null)
            {
                double d = Math.Sqrt((x - _map.Hill.X) * (x - _map.Hill.X) + (z - _map.Hill.Z) * (z - _map.Hill.Z));
                if (d <= _map.Hill.R)
                {
                    cls = MinimapClass.Hill;
                    if (d > _map.Hill.R - 0.45) return PaveEdge;
                    if (Math.Abs(d - _map.Hill.R * 0.55) < 0.14) return PaveJoint;
                    if (d < _map.Hill.R * 0.25) return Color.Lerp(Pave, Color.white, 0.35f);
                    return PaveColour(x, z);
                }
            }
            // shrine yard
            if (_map.Shrine != null)
            {
                double d = Math.Sqrt((x - _map.Shrine.X) * (x - _map.Shrine.X) + (z - _map.Shrine.Z) * (z - _map.Shrine.Z));
                if (d <= YardR) { cls = MinimapClass.Plaza; return d > YardR - 0.35 ? PaveEdge : Color.Lerp(PaveColour(x, z), Corrupt, 0.08f); }
            }
            // lanes
            float pd = Sample(_pathD, x, z);
            if (pd <= PathHalf)
            {
                cls = MinimapClass.Path;
                return pd > PathHalf - 0.16 ? PaveEdge : PaveColour(x, z);
            }
            // grass
            cls = MinimapClass.Grass;
            double n = Noise(x * 0.32, z * 0.32) * 0.65 + Noise(x * 0.9 + 17, z * 0.9 + 5) * 0.35;
            Color g = n < 0.38 ? GrassLow : n > 0.62 ? GrassHigh : GrassMid;
            if (hgt > 0.01f) g = Color.Lerp(g, GrassLight, Mathf.Min(0.25f, hgt * 0.16f));
            // tufts: small darker chevrons on a jittered grid
            double tx = x * 0.7, tz = z * 0.7;
            int ti = (int)Math.Floor(tx), tj = (int)Math.Floor(tz);
            double ox = 0.25 + 0.5 * Hash(ti, tj), oz = 0.25 + 0.5 * Hash(tj, ti + 9);
            if (Hash(ti + 3, tj + 1) > 0.55 && Math.Abs(Frac(tx) - ox) < 0.09 && Math.Abs(Frac(tz) - oz) < 0.09) g = Tuft;
            // rim light and shade (light from the screen's upper left)
            if (dA > -0.95)
            {
                double gx = Sample(_sdAll, x + 0.15, z) - Sample(_sdAll, x - 0.15, z);
                double gz = Sample(_sdAll, x, z + 0.15) - Sample(_sdAll, x, z - 0.15);
                double nu = gx * Proj.Rx + gz * Proj.Rz, nv = gx * Proj.Dx + gz * Proj.Dz;
                double len = Math.Sqrt(nu * nu + nv * nv);
                if (len > 1e-6)
                {
                    double lit = -(nu + nv) / (len * 1.41421356);   // outward normal toward (-1, -1) on screen
                    if (lit > 0.3 && dA > -0.55) return GrassLight;
                    if (lit < -0.3) return GrassShade;
                }
            }
            return g;
        }

        static Color PaveColour(double x, double z)
        {
            double a = x * 1.35, b = z * 1.35;
            double row = Math.Floor(b);
            if (Frac(b) < 0.13 || Frac(a + (((int)row & 1) == 0 ? 0 : 0.5)) < 0.13) return PaveJoint;
            return Hash((int)Math.Floor(a + (((int)row & 1) == 0 ? 0 : 0.5)), (int)row) > 0.78 ? Color.Lerp(Pave, PaveJoint, 0.3f) : Pave;
        }

        bool Structure(double x, double z, double px1, out Color c, ref MinimapClass cls)
        {
            double line = WallLinePx * px1;
            double ux = Proj.UpX, uz = Proj.UpZ;
            // biomass orbs and corruption pools (flat, drawn under the walls)
            c = default;
            bool hit = false;
            if (_map.CorruptionNodes != null)
                foreach (PointXZ p in _map.CorruptionNodes)
                {
                    double dx = x - p.X, dz = z - p.Z, d = Math.Sqrt(dx * dx + dz * dz);
                    double r = 1.75 + 0.3 * Math.Sin(Math.Atan2(dz, dx) * 5 + p.X);
                    if (d > r) continue;
                    cls = MinimapClass.Corruption;
                    hit = true;
                    c = d > r - line * 1.2 ? Navy : d > r - 0.4 ? CorruptDark : Corrupt;
                    double bx = p.X - 0.5, bz = p.Z - 0.6;
                    if ((x - bx) * (x - bx) + (z - bz) * (z - bz) < 0.12) c = CorruptLight;
                }
            if (_map.BiomassNodes != null)
                foreach (PointXZ p in _map.BiomassNodes)
                {
                    double dx = x - p.X, dz = z - p.Z, d = Math.Sqrt(dx * dx + dz * dz);
                    const double r = 0.95;
                    if (d > r) continue;
                    cls = MinimapClass.Biomass;
                    hit = true;
                    double su = dx * Proj.Rx + dz * Proj.Rz, sv = dx * Proj.Dx + dz * Proj.Dz;
                    c = d > r - line * 1.3 ? Navy : (su + sv) > 0.35 ? BioDark : Bio;
                    if ((su + 0.3) * (su + 0.3) + (sv + 0.3) * (sv + 0.3) < 0.05) c = Color.white;
                }
            if (_map.Decorations != null)
                foreach (DecorationDef dd in _map.Decorations)
                {
                    double dx = x - (dd.Cx + 0.5), dz = z - (dd.Cz + 0.5), d = Math.Sqrt(dx * dx + dz * dz);
                    if (d > 0.55) continue;
                    cls = MinimapClass.Pillar;
                    hit = true;
                    c = d > 0.55 - line ? Navy : WallTop;
                }
            // raised bands: top with a navy line, then the front face below it on screen
            if (Band(_wallD, x, z, ux, uz, line, WallTop, WallFace, MinimapClass.Wall, ref c, ref cls)) return true;
            if (Band(_breakD, x, z, ux, uz, line, BreakTop, BreakFace, MinimapClass.Breakable, ref c, ref cls))
            {
                if (cls == MinimapClass.Breakable && c == BreakTop && Math.Abs(Frac((x + z) * 1.1) - 0.5) < 0.06) c = BreakFace;
                return true;
            }
            if (Band(_crackD, x, z, ux, uz, line, Color.Lerp(BreakTop, BreakFace, 0.4f), BreakFace, MinimapClass.Breakable, ref c, ref cls))
            {
                if (c != Navy && (Math.Abs(Frac((x + z) * 1.1) - 0.5) < 0.07 || Math.Abs(Frac((x - z) * 1.3) - 0.5) < 0.07)) c = Navy;
                return true;
            }
            if (Band(_barD, x, z, ux, uz, line, Brass, Color.Lerp(Brass, Navy, 0.45f), MinimapClass.Barricade, ref c, ref cls)) return true;
            float rd = Sample(_rubbleD, x, z);
            if (rd < 0.6)
            {
                int ri = (int)Math.Floor(x * 3), rj = (int)Math.Floor(z * 3);
                if (Hash(ri, rj) > 0.6) { cls = MinimapClass.Rubble; c = BreakFace; return true; }
            }
            return hit;
        }

        bool Band(float[] dist, double x, double z, double ux, double uz, double line, Color top, Color face, MinimapClass k, ref Color c, ref MinimapClass cls)
        {
            // top face, lifted up the screen by the wall height
            const double Lift = 0.45;
            float dTop = Sample(dist, x - ux * Lift, z - uz * Lift);
            if (dTop <= WallHalf)
            {
                cls = k;
                c = dTop > WallHalf - line ? Navy : dTop < WallHalf * 0.35 ? Color.Lerp(top, Color.white, 0.25f) : top;
                return true;
            }
            for (int s = 0; s <= 3; s++)
            {
                double t = Lift * s / 3.0;
                if (Sample(dist, x - ux * t, z - uz * t) <= WallHalf)
                {
                    cls = k;
                    float dBase = Sample(dist, x, z);
                    c = dBase > WallHalf - line ? Navy : face;
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ render

        /// <summary>Image for a tier (1, 2 or 4), rows top-down, sRGB colours averaged over Supersample^2 points per pixel.</summary>
        public Color32[] Render(int tier, out int width, out int height)
        {
            width = Proj.Width * tier;
            height = Proj.Height * tier;
            var px = new Color32[width * height];
            int ss = Supersample;
            for (int j = 0; j < height; j++)
                for (int i = 0; i < width; i++)
                {
                    float r = 0, g = 0, b = 0;
                    for (int sy = 0; sy < ss; sy++)
                        for (int sx = 0; sx < ss; sx++)
                        {
                            Proj.ToSim(i + (sx + 0.5) / ss, j + (sy + 0.5) / ss, tier, out double x, out double z);
                            Color c = Shade(x, z, tier, out _);
                            r += c.r; g += c.g; b += c.b;
                        }
                    float inv = 1f / (ss * ss);
                    px[j * width + i] = new Color32((byte)Mathf.RoundToInt(r * inv * 255f), (byte)Mathf.RoundToInt(g * inv * 255f), (byte)Mathf.RoundToInt(b * inv * 255f), 255);
                }
            return px;
        }

        /// <summary>Top-down rows to a Texture2D (bottom-up rows).</summary>
        public static Texture2D ToTexture(Color32[] topDown, int w, int h)
        {
            var flipped = new Color32[topDown.Length];
            for (int j = 0; j < h; j++) Array.Copy(topDown, j * w, flipped, (h - 1 - j) * w, w);
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.SetPixels32(flipped);
            t.Apply(false);
            return t;
        }
    }
}
