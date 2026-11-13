using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;
using UnityEngine;

namespace CriticalMass.Runtime.View.Dressing
{
    /// <summary>Who a placed piece belongs to. Hill (art refresh 2): the hill site, tinted at run time by the team that
    /// holds the hill in the Sim (MatchState.HillState), neutral when nobody does.</summary>
    public enum WorldTeam : byte { None, Azure, Crimson, Neutral, Hill }

    /// <summary>One kit piece in the dressed world: the FBX name, its Unity world matrix and what it is.</summary>
    public struct WorldPlacement
    {
        public string Piece;
        public Matrix4x4 Matrix;
        public string Cat;
        public WorldTeam Team;
        /// <summary>Pivot in sim coordinates (x east, z south) and height.</summary>
        public double SimX, SimZ, Y;
        /// <summary>True for pieces the kit lets stand over a walkable cell (the walkable surface itself: ground, bridge decks).</summary>
        public bool Surface;
    }

    /// <summary>A unit edge of the island outline (rim) or of a step down inside an island (ledge), sim coordinates.</summary>
    public sealed class WorldEdge
    {
        public double Px, Pz, Qx, Qz, Nx, Nz, H, Low;
        public bool Diag, Bridge, Fill;
        public int Cx, Cz;
    }

    /// <summary>A bases-kit breakable wall piece and the Sim breakable cells it draws.</summary>
    public sealed class WorldBreakable
    {
        /// <summary>"cm_hill_breakable_" or "cm_shrine_breakable_"; the state suffix is intact, cracked or destroyed.</summary>
        public string Family;
        public Matrix4x4 Matrix;
        public WorldTeam Team;
        public List<int> Cells = new List<int>();
        /// <summary>Mesh centroid in sim coordinates (the cells are matched to it).</summary>
        public double CentreX, CentreZ;
    }

    /// <summary>The whole dressed world for one map: every placement plus the data the ground field is baked from.</summary>
    public sealed class WorldPlan
    {
        public int Width, Height;
        public readonly List<WorldPlacement> Placements = new List<WorldPlacement>();
        public readonly List<WorldEdge> Rim = new List<WorldEdge>();
        public readonly List<WorldEdge> Ledge = new List<WorldEdge>();
        public readonly List<double[]> PathSegs = new List<double[]>();
        public readonly List<double[]> PathDiscs = new List<double[]>();
        /// <summary>Biomass / Corruption sites: x, z, radius (the dirt mask and the site crystals).</summary>
        public readonly List<double[]> Sites = new List<double[]>();
        public readonly List<WorldBreakable> Breakables = new List<WorldBreakable>();
        /// <summary>Bases-kit pieces left out because they would stand on walkable cells ("site:piece at x,z").</summary>
        public readonly List<string> DroppedBases = new List<string>();
        /// <summary>Adjustments the dresser made to the bases layouts, for the report.</summary>
        public readonly List<string> Notes = new List<string>();
        /// <summary>Cells the dressed world draws (the grey-box view skips them while their type is unchanged).</summary>
        public bool[] Dressed;
        /// <summary>Cell types when the plan was made.</summary>
        public byte[] CellsAtPlan;
        public int FillCount, LedgeFillCount, WaterfallCount, SpanCount;

        /// <summary>Order-sensitive FNV-1a 64 of every placement (piece, team, matrix to 1e-4): the determinism fingerprint.</summary>
        public string Hash()
        {
            ulong h = 14695981039346656037UL;
            var sb = new StringBuilder(96);
            foreach (WorldPlacement p in Placements)
            {
                sb.Clear();
                sb.Append(p.Piece).Append('|').Append((int)p.Team);
                for (int i = 0; i < 12; i++) sb.Append('|').Append(Math.Round(p.Matrix[i], 4).ToString("0.####", CultureInfo.InvariantCulture));
                foreach (char c in sb.ToString())
                {
                    h ^= c;
                    h *= 1099511628211UL;
                }
            }
            return h.ToString("x16");
        }

        /// <summary>The committed plan summary (tools/art/world-tsv): fingerprint and counts.</summary>
        public string Summary()
        {
            var sb = new StringBuilder();
            sb.Append("# World dresser plan for Skybreak Basin (Editor/Dressing/WorldBake.cs; regenerate with -executeMethod CriticalMass.Editor.Dressing.WorldBake.Bake)\n");
            sb.Append("hash\t").Append(Hash()).Append('\n');
            sb.Append("placements\t").Append(Placements.Count).Append('\n');
            sb.Append("rim_edges\t").Append(Rim.Count).Append('\n');
            sb.Append("ledge_edges\t").Append(Ledge.Count).Append('\n');
            sb.Append("fills\t").Append(FillCount).Append('\n');
            sb.Append("ledge_fills\t").Append(LedgeFillCount).Append('\n');
            sb.Append("waterfalls\t").Append(WaterfallCount).Append('\n');
            sb.Append("spans\t").Append(SpanCount).Append('\n');
            sb.Append("breakables\t").Append(Breakables.Count).Append('\n');
            sb.Append("dressed_cells\t").Append(CountTrue(Dressed)).Append('\n');
            foreach (var kv in Counts()) sb.Append("piece\t").Append(kv.Key).Append('\t').Append(kv.Value).Append('\n');
            return sb.ToString();
        }

        static int CountTrue(bool[] a) { int n = 0; foreach (bool b in a) if (b) n++; return n; }

        /// <summary>Placement count per piece, sorted by piece name.</summary>
        public SortedDictionary<string, int> Counts()
        {
            var d = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (WorldPlacement p in Placements) d[p.Piece] = d.TryGetValue(p.Piece, out int n) ? n + 1 : 1;
            return d;
        }
    }

    [Serializable] public sealed class BasesItem { public string piece; public float[] pos; public float yaw; }
    [Serializable] public sealed class BasesSite { public string name; public float[] anchorSim; public float[] anchorWorld; public BasesItem[] items; }
    [Serializable] public sealed class BasesFile
    {
        public BasesSite[] sites;
        public const string ResourcePath = "World/bases_placements";
        public static BasesFile Load()
        {
            var t = Resources.Load<TextAsset>(ResourcePath);
            return t == null ? null : JsonUtility.FromJson<BasesFile>(t.text);
        }
        public BasesSite Site(string name) => sites == null ? null : Array.Find(sites, s => s.name == name);
    }

    /// <summary>
    /// The deterministic world dresser (2026-11-12): a C# port of the environment kit v3 placement rules
    /// (env_art/layout2.py place_map, README "How the Unity side places v2 pieces") run on the Sim's own MapDef, plus the
    /// bases kit layouts (bases_placements.json: both team districts, the hill and the shrine). Pure function of the map
    /// and the layouts: same input, same placements in the same order (WorldPlan.Hash). The map-specific art constants
    /// (road hubs, waterfall anchors, keep-outs, team sides) are the kit's, copied below.
    /// </summary>
    public static class WorldDresser
    {
        const double SQ2 = 1.4142135623730951;
        const double STEP = 0.75;
        const double PATH_W = 1.9;
        const double DECK_HALF = 1.42;
        const double TILE = 2.04;
        const double MirrorX = 112;

        // Islands (sim map data, SkybreakBasinMap.Islands): cx, cz, rx, rz, h, mirrored.
        static readonly double[][] Islands =
        {
            new double[] { 10, 36, 9, 11, 0, 1 }, new double[] { 28, 18, 8, 6, 0, 1 }, new double[] { 28, 56, 8, 6, 0, 1 },
            new double[] { 56, 40, 17, 14, 0.75, 0 }, new double[] { 56, 10, 12, 7, 1.5, 0 }, new double[] { 56, 65, 9, 5, 0, 0 },
        };
        static readonly double[][] WaterfallAnchors =
        {
            new double[] { 41.5, 47.0 }, new double[] { 47.0, 52.5 }, new double[] { 25.0, 23.5 }, new double[] { 30.0, 61.5 },
            new double[] { 14.0, 46.5 }, new double[] { 64.0, 15.0 }, new double[] { 98.0, 46.5 }, new double[] { 86.0, 23.5 },
            new double[] { 60.0, 69.5 }, new double[] { 69.0, 50.0 },
        };
        static readonly string[] Small = { "env_pebbles", "env_grass_tuft", "env_flowers", "env_stump", "env_bush" };
        static readonly string[] Trees = { "env_tree_round_l", "env_tree_round_s", "env_tree_tall_l", "env_tree_tall_s" };

        /// <summary>Deterministic integer hash (layout.py h32; unchecked uint arithmetic).</summary>
        public static uint H32(long cx, long cz, long salt = 0)
        {
            unchecked
            {
                uint x = (uint)((cx * 73856093L) ^ (cz * 19349663L) ^ (salt * 83492791L));
                x ^= x >> 13;
                x *= 0x5BD1E995u;
                x ^= x >> 15;
                return x;
            }
        }

        sealed class G
        {
            public MapDef M;
            public int W, H;
            public byte T(int x, int z) => x >= 0 && z >= 0 && x < W && z < H ? M.Cells[z * W + x] : Cell.Void;
            public bool Land(int x, int z) => Solid(T(x, z));
            public double Hh(int x, int z) => M.Height01[z * W + x];
        }

        /// <summary>Cells that draw island ground (layout.py solid_ground).</summary>
        public static bool Solid(byte t) =>
            t == Cell.Floor || t == Cell.Wall || t == Cell.BreakableIntact || t == Cell.BreakableCracked || t == Cell.BreakableDestroyed || t == Cell.Barricade;

        static double YawOf(double dx, double dz) => Math.Atan2(dz, dx) * 180.0 / Math.PI;

        static int PyRound(double v) => (int)Math.Round(v, MidpointRounding.ToEven);

        static WorldPlacement At(string piece, double x, double y, double z, double yaw, string cat, WorldTeam team = WorldTeam.None, bool surface = false) =>
            new WorldPlacement
            {
                Piece = piece, Cat = cat, Team = team, SimX = x, SimZ = z, Y = y, Surface = surface,
                Matrix = Matrix4x4.TRS(new Vector3((float)x, (float)y, (float)-z), Quaternion.Euler(0f, (float)yaw, 0f), Vector3.one),
            };

        /// <param name="bodyOf">A bases piece's vertices 0.3 to 2 u above its base, own frame (WorldKitCatalog.Body): a piece
        /// with one of them more than 0.4 u inside walkable space is left out (WorldPlan.DroppedBases).</param>
        /// <param name="centroidOf">Vertex centroid of a bases piece in its own frame (WorldKitCatalog.Centroid): the hill and shrine
        /// breakables pivot on the site centre, so their cells are found from where the mesh is. Null: the pivot.</param>
        public static WorldPlan Plan(MapDef map, BasesFile bases, Func<string, Vector3> centroidOf = null, Func<string, Vector3[]> bodyOf = null)
        {
            var g = new G { M = map, W = map.Width, H = map.Height };
            var plan = new WorldPlan { Width = g.W, Height = g.H, Dressed = new bool[g.W * g.H], CellsAtPlan = (byte[])map.Cells.Clone() };
            List<int[]> fl = Fills(g);
            List<int[]> lfl = LedgeFills(g);
            var flH = new Dictionary<long, double>();
            foreach (int[] f in fl) flH[Key(f[0], f[1])] = g.Hh(f[0] + f[2], f[1]);
            var lflH = new Dictionary<long, double>();
            foreach (int[] f in lfl) lflH[Key(f[0], f[1])] = g.Hh(f[0] + f[2], f[1]);
            Boundary(g, fl, lfl, plan.Rim, plan.Ledge);
            plan.FillCount = fl.Count;
            plan.LedgeFillCount = lfl.Count;
            MapPaths(map, plan);
            List<double[]> spans = Spans(g);
            plan.SpanCount = spans.Count;

            // 1. Ground cells, bridge cells not under a deck.
            for (int cz = 0; cz < g.H; cz++)
            for (int cx = 0; cx < g.W; cx++)
            {
                byte t = g.T(cx, cz);
                if (g.Land(cx, cz))
                {
                    plan.Placements.Add(At("env_ground_cell", cx + 0.5, g.Hh(cx, cz), cz + 0.5, 0, "ground", surface: true));
                    if (t == Cell.Floor || t == Cell.BreakableDestroyed) plan.Dressed[cz * g.W + cx] = true;
                }
                else if (t == Cell.Bridge)
                {
                    plan.Dressed[cz * g.W + cx] = true;
                    if (!CellUnderSpan(cx, cz, spans))
                        plan.Placements.Add(At("env_bridge_cell", cx + 0.5, g.Hh(cx, cz) - 0.06, cz + 0.5, 0, "bridge", surface: true));
                }
            }
            // 2. Rim and ledge fill triangles.
            foreach (int[] f in fl)
                plan.Placements.Add(At("env_ground_fill_tri", f[0] + 0.5, flH[Key(f[0], f[1])], f[1] + 0.5, YawOf(f[2], f[3]) - 45.0, "ground", surface: true));
            foreach (int[] f in lfl)
                plan.Placements.Add(At("env_ground_fill_tri", f[0] + 0.5, lflH[Key(f[0], f[1])], f[1] + 0.5, YawOf(f[2], f[3]) - 45.0, "ground", surface: true));
            // 3-4. Lips, ledge bands and shadows, cliff skirts, corners.
            EdgePieces(plan.Rim, plan.Ledge, plan.Placements);
            // 5. Waterfalls.
            var wfCells = new HashSet<long>();
            Waterfalls(g, plan.Rim, plan.Placements, wfCells);
            plan.WaterfallCount = wfCells.Count;
            // 6. Bridges.
            BridgeTiles(g, spans, plan.Placements);
            // 7. Decor.
            var skip = new HashSet<long>();
            foreach (int[] f in lfl) skip.Add(Key(f[0], f[1]));
            Decor(g, map, plan, fl, flH, wfCells, skip);
            // 8. Knock-down decorations (CM-31): the environment kit's ruin column for a pillar.
            Decorations(g, map, plan.Placements);
            // Sky rocks, cloud puffs and the cloud floor under the arena (layout.py place, v1 kept).
            Sky(plan.Placements);
            // Bases kit: districts, hill, shrine; their breakables; the Sim wall cells they cover.
            if (bases != null) Bases(g, map, bases, plan, centroidOf, bodyOf);
            return plan;
        }

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        // ------------------------------------------------------------------------------------------- outline
        static List<int[]> Fills(G g)
        {
            var o = new List<int[]>();
            for (int cz = 0; cz < g.H; cz++)
            for (int cx = 0; cx < g.W; cx++)
            {
                if (g.T(cx, cz) != Cell.Void) continue;
                int n = 0, bdx = 0, bdz = 0;
                for (int dx = -1; dx <= 1; dx += 2)
                for (int dz = -1; dz <= 1; dz += 2)
                {
                    if (!(g.Land(cx + dx, cz) && g.Land(cx, cz + dz) && g.Land(cx + dx, cz + dz))) continue;
                    double h = g.Hh(cx + dx, cz);
                    if (Math.Abs(g.Hh(cx, cz + dz) - h) < 1e-6 && Math.Abs(g.Hh(cx + dx, cz + dz) - h) < 1e-6) { n++; bdx = dx; bdz = dz; }
                }
                if (n == 1) o.Add(new[] { cx, cz, bdx, bdz });
            }
            return o;
        }

        static List<int[]> LedgeFills(G g)
        {
            var o = new List<int[]>();
            for (int cz = 0; cz < g.H; cz++)
            for (int cx = 0; cx < g.W; cx++)
            {
                if (!MapGrid.IsWalkableType(g.T(cx, cz)) || !g.Land(cx, cz)) continue;
                double hl = g.Hh(cx, cz);
                int n = 0, bdx = 0, bdz = 0;
                for (int dx = -1; dx <= 1; dx += 2)
                for (int dz = -1; dz <= 1; dz += 2)
                {
                    if (!(g.Land(cx + dx, cz) && g.Land(cx, cz + dz) && g.Land(cx + dx, cz + dz))) continue;
                    double hh = g.Hh(cx + dx, cz);
                    if (hh - hl > 0.3 && Math.Abs(g.Hh(cx, cz + dz) - hh) < 1e-6 && Math.Abs(g.Hh(cx + dx, cz + dz) - hh) < 1e-6) { n++; bdx = dx; bdz = dz; }
                }
                if (n == 1) o.Add(new[] { cx, cz, bdx, bdz });
            }
            return o;
        }

        /// <summary>The fill triangle's corner C and the two other points P, Q (layout2.fill_tri).</summary>
        static void FillTri(int[] f, out double[] c, out double[] p, out double[] q)
        {
            int Cx = f[0] + (f[2] > 0 ? 1 : 0), Cz = f[1] + (f[3] > 0 ? 1 : 0);
            c = new double[] { Cx, Cz };
            p = new double[] { Cx, Cz - f[3] };
            q = new double[] { Cx - f[2], Cz };
        }

        static WorldEdge Oriented(double px, double pz, double qx, double qz, double nx, double nz)
        {
            double dx = nz, dz = -nx;
            if ((qx - px) * dx + (qz - pz) * dz < 0) { (px, qx) = (qx, px); (pz, qz) = (qz, pz); }
            return new WorldEdge { Px = px, Pz = pz, Qx = qx, Qz = qz, Nx = nx, Nz = nz };
        }

        static void Boundary(G g, List<int[]> fl, List<int[]> lfl, List<WorldEdge> rim, List<WorldEdge> ledge)
        {
            var fillAt = new Dictionary<long, int[]>();
            foreach (int[] f in fl) fillAt[Key(f[0], f[1])] = f;
            var lfillAt = new Dictionary<long, int[]>();
            foreach (int[] f in lfl) lfillAt[Key(f[0], f[1])] = f;
            int[][] sides = { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } };
            for (int cz = 0; cz < g.H; cz++)
            for (int cx = 0; cx < g.W; cx++)
            {
                if (!g.Land(cx, cz)) continue;
                double h = g.Hh(cx, cz);
                foreach (int[] s in sides)
                {
                    int sx = s[0], sz = s[1], nx = cx + sx, nz = cz + sz;
                    double px, pz, qx, qz;
                    if (sx == 1) { px = cx + 1; pz = cz; qx = cx + 1; qz = cz + 1; }
                    else if (sx == -1) { px = cx; pz = cz; qx = cx; qz = cz + 1; }
                    else if (sz == 1) { px = cx; pz = cz + 1; qx = cx + 1; qz = cz + 1; }
                    else { px = cx; pz = cz; qx = cx + 1; qz = cz; }
                    if (g.Land(nx, nz))
                    {
                        double dh = h - g.Hh(nx, nz);
                        if (dh > 0.3 && lfillAt.TryGetValue(Key(nx, nz), out int[] lf) && ((nx + lf[2] == cx && nz == cz) || (nx == cx && nz + lf[3] == cz))) continue;
                        if (dh > 0.3)
                        {
                            WorldEdge e = Oriented(px, pz, qx, qz, sx, sz);
                            e.H = h; e.Low = g.Hh(nx, nz); e.Cx = cx; e.Cz = cz;
                            ledge.Add(e);
                        }
                        continue;
                    }
                    if (fillAt.TryGetValue(Key(nx, nz), out int[] f) && ((nx + f[2] == cx && nz == cz) || (nx == cx && nz + f[3] == cz))) continue;
                    WorldEdge r = Oriented(px, pz, qx, qz, sx, sz);
                    r.H = h; r.Cx = cx; r.Cz = cz; r.Bridge = g.T(nx, nz) == Cell.Bridge;
                    rim.Add(r);
                }
            }
            foreach (int[] f in fl)
            {
                FillTri(f, out _, out double[] P, out double[] Q);
                WorldEdge e = Oriented(P[0], P[1], Q[0], Q[1], -f[2] / SQ2, -f[3] / SQ2);
                e.H = g.Hh(f[0] + f[2], f[1]); e.Diag = true; e.Cx = f[0]; e.Cz = f[1]; e.Fill = true;
                rim.Add(e);
            }
            foreach (int[] f in lfl)
            {
                FillTri(f, out _, out double[] P, out double[] Q);
                WorldEdge e = Oriented(P[0], P[1], Q[0], Q[1], -f[2] / SQ2, -f[3] / SQ2);
                e.H = g.Hh(f[0] + f[2], f[1]); e.Low = g.Hh(f[0], f[1]); e.Diag = true; e.Cx = f[0]; e.Cz = f[1]; e.Fill = true;
                ledge.Add(e);
            }
        }

        static string PKey(double x, double z, double h) =>
            Math.Round(x, 4).ToString(CultureInfo.InvariantCulture) + "," + Math.Round(z, 4).ToString(CultureInfo.InvariantCulture) + "," +
            Math.Round(h, 4).ToString(CultureInfo.InvariantCulture);

        static List<List<WorldEdge>> Loops(List<WorldEdge> edges)
        {
            var byStart = new Dictionary<string, List<int>>();
            for (int i = 0; i < edges.Count; i++)
            {
                string k = PKey(edges[i].Px, edges[i].Pz, edges[i].H);
                if (!byStart.TryGetValue(k, out List<int> l)) byStart[k] = l = new List<int>();
                l.Add(i);
            }
            var used = new bool[edges.Count];
            var o = new List<List<WorldEdge>>();
            for (int i = 0; i < edges.Count; i++)
            {
                if (used[i]) continue;
                var lp = new List<WorldEdge>();
                int j = i;
                while (j >= 0 && !used[j])
                {
                    used[j] = true;
                    lp.Add(edges[j]);
                    int nxt = -1;
                    if (byStart.TryGetValue(PKey(edges[j].Qx, edges[j].Qz, edges[j].H), out List<int> c))
                        foreach (int k in c) if (!used[k]) { nxt = k; break; }
                    j = nxt;
                }
                o.Add(lp);
            }
            return o;
        }

        static (double, double) Dir(WorldEdge e)
        {
            double dx = e.Qx - e.Px, dz = e.Qz - e.Pz, ln = Math.Sqrt(dx * dx + dz * dz);
            return (Math.Round(dx / ln, 6), Math.Round(dz / ln, 6));
        }

        static List<List<WorldEdge>> Runs(List<WorldEdge> lp, out bool closed)
        {
            int n = lp.Count, start = 0;
            for (int k = 0; k < n; k++)
                if (Dir(lp[k]) != Dir(lp[(k - 1 + n) % n])) { start = k; break; }
            var seq = new List<WorldEdge>(n);
            for (int k = 0; k < n; k++) seq.Add(lp[(start + k) % n]);
            var o = new List<List<WorldEdge>>();
            var cur = new List<WorldEdge> { seq[0] };
            for (int k = 1; k < n; k++)
            {
                WorldEdge e = seq[k], last = cur[cur.Count - 1];
                if (Dir(e) == Dir(last) && PKey(e.Px, e.Pz, e.H) == PKey(last.Qx, last.Qz, last.H)) cur.Add(e);
                else { o.Add(cur); cur = new List<WorldEdge> { e }; }
            }
            o.Add(cur);
            closed = PKey(seq[n - 1].Qx, seq[n - 1].Qz, seq[n - 1].H) == PKey(seq[0].Px, seq[0].Pz, seq[0].H);
            return o;
        }

        static List<int> PartitionUnits(int n, int[] choices, uint salt)
        {
            var o = new List<int>();
            int r = n, i = 0, mx = choices[choices.Length - 1], mn = choices[0];
            var opts = new List<int>();
            while (r > mx)
            {
                opts.Clear();
                foreach (int c in choices) if (r - c == 0 || r - c >= mn) opts.Add(c);
                int pick = opts[(int)(H32(salt & 0xFFFF, i, 41) % (uint)opts.Count)];
                o.Add(pick);
                r -= pick;
                i++;
            }
            if (r > 0) o.Add(r);
            return o;
        }

        static void EdgePieces(List<WorldEdge> rim, List<WorldEdge> ledge, List<WorldPlacement> o)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                bool isRim = pass == 0;
                List<WorldEdge> set = isRim ? rim : ledge;
                foreach (WorldEdge e in set)
                {
                    var d = Dir(e);
                    string pre = isRim ? "env_lip_rim_" : "env_lip_";
                    string nm = !e.Diag
                        ? pre + "straight_" + "abc"[(int)(H32((long)(e.Px * 2), (long)(e.Pz * 2), 3) % 3)]
                        : pre + "diag_" + "ab"[(int)(H32((long)(e.Px * 2), (long)(e.Pz * 2), 5) % 2)];
                    double yaw = YawOf(d.Item1, d.Item2);
                    o.Add(At(nm, e.Px, e.H, e.Pz, yaw, "edge"));
                    if (!isRim)
                    {
                        int steps = Math.Max(1, PyRound((e.H - e.Low) / STEP));
                        string sfx = e.Diag ? "_diag" : "";
                        for (int k = 1; k < steps; k++) o.Add(At("env_ledge_band" + sfx, e.Px, e.H - STEP * k, e.Pz, yaw, "edge"));
                        o.Add(At("env_ledge_shadow" + sfx, e.Px, e.H - STEP * (steps - 1), e.Pz, yaw, "edge"));
                    }
                }
                foreach (List<WorldEdge> lp in Loops(set))
                {
                    List<List<WorldEdge>> rs = Runs(lp, out bool closed);
                    for (int ri = 0; ri < rs.Count; ri++)
                    {
                        List<WorldEdge> run = rs[ri];
                        var d = Dir(run[0]);
                        double yaw = YawOf(d.Item1, d.Item2);
                        if (isRim)
                        {
                            bool ax = !run[0].Diag;
                            List<int> parts = PartitionUnits(run.Count, ax ? new[] { 3, 4, 5, 6 } : new[] { 2, 3, 4 },
                                H32((long)run[0].Px, (long)run[0].Pz, 43));
                            int s = 0;
                            foreach (int k in parts)
                            {
                                o.Add(At("env_cliff_" + (ax ? "s" : "d") + k, run[s].Px, run[0].H, run[s].Pz, yaw, "cliff"));
                                s += k;
                            }
                        }
                        if (ri + 1 < rs.Count || closed)
                        {
                            List<WorldEdge> nxt = rs[(ri + 1) % rs.Count];
                            var d2 = Dir(nxt[0]);
                            WorldEdge last = run[run.Count - 1];
                            bool conv = d2.Item1 * last.Nx + d2.Item2 * last.Nz < -1e-6;
                            if (!conv) continue;
                            double ang = Math.Acos(Math.Max(-1.0, Math.Min(1.0, d.Item1 * d2.Item1 + d.Item2 * d2.Item2))) * 180.0 / Math.PI;
                            if (isRim)
                            {
                                o.Add(At(ang > 60 ? "env_lip_rim_corner90" : "env_lip_rim_corner45", last.Qx, last.H, last.Qz, yaw, "edge"));
                                o.Add(At(ang > 60 ? "env_cliff_corner" : "env_cliff_bend", last.Qx, last.H, last.Qz, yaw, "cliff"));
                            }
                            else if (ang > 60) o.Add(At("env_lip_corner", last.Qx, last.H, last.Qz, yaw, "edge"));
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------------------------------------- paths
        static double[] NearestIsland(double x, double z, bool withMirror)
        {
            double best = double.PositiveInfinity;
            double[] c = null;
            foreach (double[] isl in Islands)
            {
                int copies = withMirror && isl[5] > 0.5 ? 2 : 1;
                for (int m = 0; m < copies; m++)
                {
                    double mx = m == 0 ? isl[0] : MirrorX - isl[0];
                    double u = (x - mx) / isl[2], v = (z - isl[1]) / isl[3], d = u * u + v * v;
                    if (d < best) { best = d; c = new[] { mx, isl[1], isl[4] }; }
                }
            }
            return c;
        }

        static void MapPaths(MapDef map, WorldPlan plan)
        {
            foreach (SkybreakBasinMap.Bridge b in SkybreakBasinMap.Bridges)
            {
                for (int m = 0; m < (b.Mirror ? 2 : 1); m++)
                {
                    double x0 = m == 0 ? b.X0 : MirrorX - b.X0, x1 = m == 0 ? b.X1 : MirrorX - b.X1;
                    foreach (double[] pt in new[] { new[] { x0, b.Z0 }, new[] { x1, b.Z1 } })
                    {
                        double[] isl = NearestIsland(pt[0], pt[1], true);
                        double hx = isl[0], hz = isl[1];
                        if (isl[0] == 10 && isl[1] == 36) { hx = 8; hz = 36; }
                        else if (isl[0] == 102 && isl[1] == 36) { hx = 104; hz = 36; }
                        else if (isl[0] == 56 && isl[1] == 10) { hx = 56; hz = 13; }
                        plan.PathSegs.Add(new[] { pt[0], pt[1], hx, hz });
                    }
                }
            }
            plan.PathDiscs.Add(new[] { 56.0, 40.0, 10.0 });
            plan.PathDiscs.Add(new[] { 56.0, 8.0, 4.0 });
            plan.PathDiscs.Add(new[] { 56.0, 11.5, 2.0 });
            if (map.Pads != null) foreach (PadDef p in map.Pads) plan.PathDiscs.Add(new[] { p.X, p.Z, p.R + 2.5 });
            foreach (double[] isl in Islands)
            {
                if (isl[4] != 0.0) continue;
                plan.PathDiscs.Add(new[] { isl[0], isl[1], 3.0 });
                if (isl[5] > 0.5) plan.PathDiscs.Add(new[] { MirrorX - isl[0], isl[1], 3.0 });
            }
            if (map.BiomassNodes != null) foreach (PointXZ p in map.BiomassNodes) plan.Sites.Add(new[] { p.X, p.Z, 3.2, 0 });
            if (map.CorruptionNodes != null) foreach (PointXZ p in map.CorruptionNodes) plan.Sites.Add(new[] { p.X, p.Z, 3.6, 1 });
        }

        public static double SegDist(double px, double pz, double ax, double az, double bx, double bz)
        {
            double ex = bx - ax, ez = bz - az, ll = ex * ex + ez * ez;
            double t = ll > 0 ? Math.Max(0.0, Math.Min(1.0, ((px - ax) * ex + (pz - az) * ez) / ll)) : 0.0;
            double dx = ax + ex * t - px, dz = az + ez * t - pz;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Path distance (layout2.path_dist): roads, and discs paved over their whole radius.</summary>
        public static double PathDist(WorldPlan plan, double x, double z)
        {
            double d = 1e9;
            foreach (double[] s in plan.PathSegs) d = Math.Min(d, SegDist(x, z, s[0], s[1], s[2], s[3]));
            foreach (double[] c in plan.PathDiscs)
            {
                double dx = x - c[0], dz = z - c[1];
                d = Math.Min(d, Math.Max(0.0, Math.Sqrt(dx * dx + dz * dz) - (c[2] - PATH_W)));
            }
            return d;
        }

        static bool Keep(MapDef map, double xc, double zc)
        {
            double hx = map.Hill != null ? map.Hill.X : 56, hz = map.Hill != null ? map.Hill.Z : 40;
            double dh = Math.Sqrt((xc - hx) * (xc - hx) + (zc - hz) * (zc - hz));
            if (dh < 11.2 && !(dh > 6.6 && dh < 9.8)) return true;
            double sx = map.Shrine != null ? map.Shrine.X : 56, sz = map.Shrine != null ? map.Shrine.Z : 8;
            if (Math.Sqrt((xc - sx) * (xc - sx) + (zc - sz) * (zc - sz)) < 5.4) return true;
            if (map.Pads != null)
                foreach (PadDef p in map.Pads)
                    if (Math.Sqrt((xc - p.X) * (xc - p.X) + (zc - p.Z) * (zc - p.Z)) < p.R + 1.5) return true;
            return false;
        }

        static WorldTeam TeamOf(double x) => x < 36 ? WorldTeam.Azure : x > 76 ? WorldTeam.Crimson : WorldTeam.None;

        // ------------------------------------------------------------------------------------------- waterfalls
        static void Waterfalls(G g, List<WorldEdge> rim, List<WorldPlacement> o, HashSet<long> wfCells)
        {
            var perCell = new Dictionary<long, int>();
            foreach (WorldEdge e in rim) perCell[Key(e.Cx, e.Cz)] = perCell.TryGetValue(Key(e.Cx, e.Cz), out int n) ? n + 1 : 1;
            var cand = new List<WorldEdge>();
            foreach (WorldEdge e in rim)
            {
                if (e.Diag || e.Bridge) continue;
                bool camSide = (e.Nx == 0.0 && e.Nz == 1.0) || (e.Nx == 1.0 && e.Nz == 0.0);
                if (!camSide) continue;
                double mx = (e.Px + e.Qx) / 2, mz = (e.Pz + e.Qz) / 2;
                if (Keep(g.M, mx, mz) || perCell[Key(e.Cx, e.Cz)] != 1) continue;
                cand.Add(e);
            }
            var pick = new HashSet<WorldEdge>();
            foreach (double[] a in WaterfallAnchors)
            {
                WorldEdge best = null;
                double bd = double.PositiveInfinity;
                foreach (WorldEdge e in cand)
                {
                    double mx = (e.Px + e.Qx) / 2 - a[0], mz = (e.Pz + e.Qz) / 2 - a[1], d = Math.Sqrt(mx * mx + mz * mz);
                    if (d < bd) { bd = d; best = e; }
                }
                if (best != null && bd < 5) pick.Add(best);
            }
            foreach (WorldEdge e in cand) if (H32(e.Cx, e.Cz, 13) % 100 < 6) pick.Add(e);
            foreach (WorldEdge e in cand)
            {
                if (!pick.Contains(e)) continue;
                var d = Dir(e);
                o.Add(At("env_waterfall", (e.Px + e.Qx) / 2, e.H, (e.Pz + e.Qz) / 2, YawOf(d.Item1, d.Item2), "water"));
                wfCells.Add(Key(e.Cx, e.Cz));
            }
        }

        // ------------------------------------------------------------------------------------------- decor
        static void Decor(G g, MapDef map, WorldPlan plan, List<int[]> fl, Dictionary<long, double> flH, HashSet<long> wfOnly, HashSet<long> skip)
        {
            List<WorldPlacement> o = plan.Placements;
            var rimCells = new Dictionary<long, List<WorldEdge>>();
            foreach (WorldEdge e in plan.Rim)
            {
                if (e.Diag) continue;
                if (!rimCells.TryGetValue(Key(e.Cx, e.Cz), out List<WorldEdge> l)) rimCells[Key(e.Cx, e.Cz)] = l = new List<WorldEdge>();
                l.Add(e);
            }
            for (int cz = 0; cz < g.H; cz++)
            for (int cx = 0; cx < g.W; cx++)
            {
                if (!g.Land(cx, cz) || g.T(cx, cz) != Cell.Floor) continue;
                double xc = cx + 0.5, zc = cz + 0.5;
                if (Keep(map, xc, zc) || wfOnly.Contains(Key(cx, cz)) || skip.Contains(Key(cx, cz))) continue;
                double h = g.Hh(cx, cz);
                double jx = (H32(cx, cz, 71) % 100) / 100.0 - 0.5, jz = (H32(cx, cz, 73) % 100) / 100.0 - 0.5;
                double yaw = H32(cx, cz, 75) % 360;
                double pd = PathDist(plan, xc, zc);
                if (rimCells.TryGetValue(Key(cx, cz), out List<WorldEdge> es) && !es.Exists(e => e.Bridge))
                {
                    uint r = H32(cx, cz, 111) % 1000;
                    double nx = es[0].Nx, nz = es[0].Nz;
                    if (r < 110 && pd > PATH_W + 0.6)
                    {
                        // Edge tree. The kit sets it 0.28 u toward the lip, which leaves its trunk on the walkable rim cell;
                        // here it moves 0.58 u (the big-prop rule), so its centre sits over the rim band, not on a walkable cell.
                        o.Add(At(Trees[H32(cx, cz, 112) % 4], xc + nx * 0.58, h, zc + nz * 0.58, yaw, "tree"));
                    }
                    else if (r < 310 && pd < PATH_W + 4.5 && !NearWaterfall(wfOnly, cx, cz))
                    {
                        uint k = H32(cx, cz, 122) % 100;
                        double x = xc + nx * 0.58 - nz * jx * 0.6, z = zc + nz * 0.58 + nx * jx * 0.6;
                        double[] site = SiteNear(plan, x, z, 2.0);
                        if (site != null && k < 70) o.Add(At(site[3] < 0.5 ? "env_crystal_biomass" : "env_crystal_corrupt", x, h - 0.03, z, yaw, "crystal"));
                        else if (TeamOf(x) != WorldTeam.None && k < 72)
                            o.Add(At(k % 2 == 0 ? "env_crystal_cluster" : "env_crystal_cluster_small", x, h - 0.03, z, yaw, "crystal", TeamOf(x)));
                        else
                            o.Add(At(new[] { "env_boulder_l", "env_boulder_m", "env_rocks", "env_boulder_m" }[k % 4], x, h - 0.10, z, yaw, "rock_big"));
                    }
                    else if (r >= 310 && r < 410 && pd > PATH_W - 0.2)
                        o.Add(At(Small[H32(cx, cz, 114) % 3], xc + jx * 0.5, h, zc + jz * 0.5, yaw, "small"));
                    continue;
                }
                if (pd > PATH_W + 0.1 && pd < PATH_W + 1.5)
                {
                    if (H32(cx, cz, 117) % 100 < 18) o.Add(At(Small[H32(cx, cz, 118) % 5], xc + jx * 0.6, h, zc + jz * 0.6, yaw, "small"));
                }
                else if (pd >= PATH_W + 1.5 && H32(cx, cz, 119) % 100 < 3)
                    o.Add(At(Small[H32(cx, cz, 120) % 5], xc + jx * 0.6, h, zc + jz * 0.6, yaw, "small"));
            }
            foreach (int[] f in fl)
            {
                int cx = f[0], cz = f[1];
                double h = flH[Key(cx, cz)];
                FillTri(f, out double[] C, out double[] P, out double[] Q);
                double x = (C[0] + P[0] + Q[0]) / 3, z = (C[1] + P[1] + Q[1]) / 3;
                if (Keep(map, x, z)) continue;
                uint r = H32(cx, cz, 113) % 100;
                double pd = PathDist(plan, x, z);
                double yaw = H32(cx, cz, 115) % 360;
                double[] site = SiteNear(plan, x, z, 0.0);
                bool near = pd < 6.5;
                WorldTeam team = TeamOf(x);
                string siteCrystal = site == null ? null : site[3] < 0.5 ? "env_crystal_biomass" : "env_crystal_corrupt";
                string teamCrystal = r % 2 == 0 ? "env_crystal_cluster" : "env_crystal_cluster_small";
                if (r < (near ? 16u : 32u)) o.Add(At(Trees[H32(cx, cz, 116) % 4], x, h, z, yaw, "tree"));
                else if (near && r >= 36 && r < 80 && (site != null || team != WorldTeam.None))
                    o.Add(site != null ? At(siteCrystal, x, h, z, yaw, "crystal") : At(teamCrystal, x, h, z, yaw, "crystal", team));
                else if (r < 60 || (near && r < 72))
                    o.Add(At(new[] { "env_boulder_l", "env_boulder_m", "env_rocks" }[H32(cx, cz, 121) % 3], x, h, z, yaw, "rock_big"));
                else if (r < 80 && (site != null || (pd < 6.5 && team != WorldTeam.None)))
                    o.Add(site != null ? At(siteCrystal, x, h, z, yaw, "crystal") : At(teamCrystal, x, h, z, yaw, "crystal", team));
                else if (r < 90) o.Add(At("env_grass_tuft", x, h, z, yaw, "small"));
            }
        }

        static bool NearWaterfall(HashSet<long> wf, int cx, int cz)
        {
            for (int a = -2; a <= 2; a++)
            for (int b = -2; b <= 2; b++)
                if (wf.Contains(Key(cx + a, cz + b))) return true;
            return false;
        }

        static double[] SiteNear(WorldPlan plan, double x, double z, double extra)
        {
            foreach (double[] s in plan.Sites)
                if (Math.Sqrt((x - s[0]) * (x - s[0]) + (z - s[1]) * (z - s[1])) < s[2] + extra) return s;
            return null;
        }

        // ------------------------------------------------------------------------------------------- bridges
        /// <summary>Every bridge segment with its deck t-range (layout.all_spans): x0, z0, x1, z1, ha, hb, L, ta, tb, hw.</summary>
        static List<double[]> Spans(G g)
        {
            var o = new List<double[]>();
            foreach (SkybreakBasinMap.Bridge b in SkybreakBasinMap.Bridges)
            {
                double ha = NearestIsland(b.X0, b.Z0, false)[2], hb = NearestIsland(b.X1, b.Z1, false)[2];
                for (int m = 0; m < (b.Mirror ? 2 : 1); m++)
                {
                    double ax = m == 0 ? b.X0 : MirrorX - b.X0, bx = m == 0 ? b.X1 : MirrorX - b.X1;
                    double L = Math.Sqrt((bx - ax) * (bx - ax) + (b.Z1 - b.Z0) * (b.Z1 - b.Z0));
                    int steps = (int)(L * 20);
                    double tmin = double.PositiveInfinity, tmax = double.NegativeInfinity;
                    for (int k = 0; k <= steps; k++)
                    {
                        double t = (double)k / steps, x = ax + (bx - ax) * t, z = b.Z0 + (b.Z1 - b.Z0) * t;
                        int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
                        if (g.T(cx, cz) == Cell.Bridge) { tmin = Math.Min(tmin, t); tmax = Math.Max(tmax, t); }
                    }
                    if (double.IsInfinity(tmin)) continue;
                    var sp = new[] { ax, b.Z0, bx, b.Z1, ha, hb, L, Math.Max(0.0, tmin - 0.6 / L), Math.Min(1.0, tmax + 0.6 / L), 1.5 };
                    sp[9] = SpanHalfWidth(sp, g);
                    o.Add(sp);
                }
            }
            return o;
        }

        static double SpanHalfWidth(double[] sp, G g)
        {
            double ex = sp[2] - sp[0], ez = sp[3] - sp[1];
            if (Math.Abs(ex) > 1e-6 && Math.Abs(ez) > 1e-6) return 1.5;
            double best = 1.0;
            for (int cz = 0; cz < g.H; cz++)
            for (int cx = 0; cx < g.W; cx++)
            {
                if (g.T(cx, cz) != Cell.Bridge) continue;
                double px = cx + 0.5, pz = cz + 0.5;
                double t = ((px - sp[0]) * ex + (pz - sp[1]) * ez) / (sp[6] * sp[6]);
                double perp = Math.Abs((px - sp[0]) * ez - (pz - sp[1]) * ex) / sp[6];
                if (sp[7] <= t && t <= sp[8] && perp <= 1.6) best = Math.Max(best, perp + 0.5);
            }
            return best;
        }

        static bool CellUnderSpan(int cx, int cz, List<double[]> spans)
        {
            foreach (double[] sp in spans)
            {
                double ex = sp[2] - sp[0], ez = sp[3] - sp[1];
                bool ok = true;
                for (int c = 0; c < 4 && ok; c++)
                {
                    double px = cx + (c & 1), pz = cz + (c >> 1);
                    double t = ((px - sp[0]) * ex + (pz - sp[1]) * ez) / (sp[6] * sp[6]);
                    double perp = Math.Abs((px - sp[0]) * ez - (pz - sp[1]) * ex) / sp[6];
                    if (!(sp[7] <= t && t <= sp[8] && perp <= sp[9] - (1.5 - DECK_HALF))) ok = false;
                }
                if (ok) return true;
            }
            return false;
        }

        static void BridgeTiles(G g, List<double[]> spans, List<WorldPlacement> o)
        {
            foreach (double[] sp in spans)
            {
                double w = Math.Round(sp[9] / 1.5, 4);
                double x0 = sp[0], z0 = sp[1], x1 = sp[2], z1 = sp[3], ha = sp[4], hb = sp[5], L = sp[6], ta = sp[7], tb = sp[8];
                int n = Math.Max(1, PyRound((tb - ta) * L / TILE));
                Func<double, double[]> pt = t => new[] { x0 + (x1 - x0) * t, ha + (hb - ha) * t, z0 + (z1 - z0) * t };
                for (int k = 0; k < n; k++)
                {
                    double[] a = pt(ta + (tb - ta) * k / n), b = pt(ta + (tb - ta) * (k + 1) / n);
                    o.Add(SpanTile(a, b, w));
                }
                double ang = Math.Atan2(z1 - z0, x1 - x0), ox = -Math.Sin(ang), oz = Math.Cos(ang);
                for (int side = -1; side <= 1; side += 2)
                {
                    double[] e = pt(tb);
                    double px = e[0] + ox * 1.58 * w * side, pz = e[2] + oz * 1.58 * w * side;
                    if (!MapGrid.IsWalkableType(g.T((int)Math.Floor(px), (int)Math.Floor(pz)))) o.Add(At("env_bridge_post", px, e[1], pz, 0, "bridge"));
                    foreach (var (t, sgn) in new[] { (ta, 1), (tb, -1) })
                    {
                        foreach (double dt in new[] { 0.6, 0.35, 0.85, 0.1 })
                        {
                            double[] p = pt(t + sgn * dt / L);
                            double x = p[0] + ox * (2.0 * w) * side, z = p[2] + oz * (2.0 * w) * side;
                            if (!MapGrid.IsWalkableType(g.T((int)Math.Floor(x), (int)Math.Floor(z))))
                            {
                                o.Add(At("env_bridge_pier", x, p[1], z, ang * 180.0 / Math.PI, "bridge"));
                                break;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>A deck tile stretched from a to b (sim points x, h, z), scaled by w across (render_v3.pmat).</summary>
        static WorldPlacement SpanTile(double[] a, double[] b, double w)
        {
            var A = new Vector3((float)a[0], (float)a[1], (float)-a[2]);
            var B = new Vector3((float)b[0], (float)b[1], (float)-b[2]);
            Vector3 f = B - A;
            float ln = f.magnitude;
            Vector3 fz = f / ln;
            Vector3 X = Vector3.Cross(Vector3.up, fz).normalized * (float)w;
            Vector3 Y = Vector3.Cross(fz, X);
            float s = ln / (float)TILE;
            var m = new Matrix4x4();
            m.SetColumn(0, new Vector4(X.x, X.y, X.z, 0));
            m.SetColumn(1, new Vector4(Y.x, Y.y, Y.z, 0));
            m.SetColumn(2, new Vector4(fz.x * s, fz.y * s, fz.z * s, 0));
            m.SetColumn(3, new Vector4(A.x, A.y, A.z, 1));
            return new WorldPlacement { Piece = "env_bridge_span", Matrix = m, Cat = "bridge", SimX = a[0], SimZ = a[2], Y = a[1], Surface = true };
        }

        // ------------------------------------------------------------------------------------------- sky
        /// <summary>The kit piece of a knock-down decoration (MapDef.Decorations): a pillar is env_ruin_column_intact standing,
        /// env_ruin_column_fallen_ramp fallen along its Ramp (bridging the void), env_ruin_column_fallen_block fallen along
        /// its Block. The Sim keeps no topple state yet (CM-31 is the owner's item), so the view draws the standing column.</summary>
        public static string DecorationPiece(DecorationKind kind, bool fallen, bool alongRamp)
        {
            switch (kind)
            {
                case DecorationKind.Pillar:
                    return !fallen ? "env_ruin_column_intact" : alongRamp ? "env_ruin_column_fallen_ramp" : "env_ruin_column_fallen_block";
                default:
                    return null;
            }
        }

        static void Decorations(G g, MapDef map, List<WorldPlacement> o)
        {
            if (map.Decorations == null) return;
            foreach (DecorationDef d in map.Decorations)
            {
                string piece = DecorationPiece(d.Kind, false, false);
                if (piece == null) continue;
                // Yaw faces the ramp direction, so the fallen pieces (authored falling along +Z) line up when they land.
                double yaw = Math.Atan2(d.Ramp.Dx, -d.Ramp.Dz) * 180.0 / Math.PI;
                o.Add(At(piece, d.Cx + 0.5, g.Hh(d.Cx, d.Cz), d.Cz + 0.5, yaw, "decor"));
            }
        }

        static void Sky(List<WorldPlacement> o)
        {
            for (int k = 0; k < 70; k++)
            {
                uint hx = H32(k, 1, 17);
                double a = (hx % 3600) / 3600.0 * 2 * Math.PI, rr = 1.0 + ((hx >> 12) % 100) / 100.0 * 0.35;
                double x = 56 + Math.Cos(a) * 62 * rr, z = 36 + Math.Sin(a) * 44 * rr;
                string size = "sml"[(int)((hx >> 20) % 3)].ToString();
                double y = -2.0 - ((hx >> 8) % 7);
                o.Add(At("env_sky_rock_" + size, x, y, z, 0, "sky"));
            }
            for (int k = 0; k < 130; k++)
            {
                uint hx = H32(k, 2, 19);
                double x = -20 + (hx % 1520) / 10.0, z = -20 + ((hx >> 11) % 1120) / 10.0;
                o.Add(At("env_cloud_puff", x, -9.0 - (hx >> 24) % 3, z, (hx >> 5) % 360, "sky"));
            }
            // Art refresh 2: the flat cloud floor reaches past the wide frame on every side (it ended inside it with 5 x 4
            // tiles); the tiles are 4-vertex quads, so 14 x 12 costs nothing.
            for (int gx = -6; gx < 8; gx++)
            for (int gz = -5; gz < 7; gz++)
                o.Add(At("env_cloud_floor", gx * 64 + 32 - 40, -11.0, gz * 64 + 32 - 28, 0, "sky"));
        }

        // ------------------------------------------------------------------------------------------- bases
        public static readonly string[] BaseSites = { "district_azure", "district_crimson", "hill", "shrine" };

        static WorldTeam SiteTeam(string site) =>
            site == "district_azure" ? WorldTeam.Azure : site == "district_crimson" ? WorldTeam.Crimson : site == "hill" ? WorldTeam.Hill : WorldTeam.Neutral;

        static void Bases(G g, MapDef map, BasesFile bases, WorldPlan plan, Func<string, Vector3> centroidOf, Func<string, Vector3[]> bodyOf)
        {
            var centres = new List<(WorldBreakable b, double x, double z)>();
            foreach (string name in BaseSites)
            {
                BasesSite s = bases.Site(name);
                if (s == null || s.anchorWorld == null || s.anchorWorld.Length < 3) continue;
                var anchor = new Vector3(s.anchorWorld[0], s.anchorWorld[1], s.anchorWorld[2]);
                WorldTeam team = SiteTeam(name);
                // Site turn: the bases kit v1 shrine is authored north-south mirrored against the Sim (its breakables sit at
                // sim z 4.7, the Sim's yard breakables and gap are on the south side, z 11), so a site whose breakables
                // match the Sim's breakable cells better when turned 180 degrees about its anchor is turned.
                // Bases v2 (ead6b1e, checked 2026-11-12 in art refresh 2): still mirrored at source (the bake notes "shrine:
                // turned 180"), so the turn stays; it switches itself off once the art matches the Sim.
                float siteYaw = BreakableFit(g, s, anchor, 180f, centroidOf) > BreakableFit(g, s, anchor, 0f, centroidOf) ? 180f : 0f;
                if (siteYaw != 0f) plan.Notes.Add($"{name}: turned {siteYaw:0} degrees about its anchor to match the Sim breakable cells");
                Quaternion siteRot = Quaternion.Euler(0f, siteYaw, 0f);
                // The district layouts carry the wall runs and footings that are not in the placement list; the view
                // draws only those children of cm_layout_* ("cm_district_*"); every listed item is placed on its own.
                if (name.StartsWith("district_", StringComparison.Ordinal))
                    plan.Placements.Add(new WorldPlacement
                    {
                        Piece = "cm_layout_" + name, Cat = "base", Team = team, Matrix = Matrix4x4.Translate(anchor),
                        SimX = anchor.x, SimZ = -anchor.z, Y = anchor.y,
                    });
                foreach (BasesItem it in s.items)
                {
                    if (it.piece == null || it.pos == null || it.pos.Length < 3) continue;
                    var p = anchor + siteRot * new Vector3(it.pos[0], it.pos[1], it.pos[2]);
                    Quaternion rot = Quaternion.Euler(0f, it.yaw + siteYaw, 0f);
                    Matrix4x4 m = Matrix4x4.TRS(p, rot, Vector3.one);
                    if (it.piece.Contains("_breakable_"))
                    {
                        var br = new WorldBreakable
                        {
                            Family = it.piece.Substring(0, it.piece.IndexOf("_breakable_", StringComparison.Ordinal) + "_breakable_".Length),
                            Team = team, Matrix = m,
                        };
                        Vector3 c = p + rot * (centroidOf != null ? centroidOf(it.piece) : Vector3.zero);
                        br.CentreX = c.x;
                        br.CentreZ = -c.z;
                        plan.Breakables.Add(br);
                        centres.Add((br, c.x, -c.z));
                        continue;
                    }
                    if (bodyOf != null && Intrudes(g, m, bodyOf(it.piece)))
                    {
                        plan.DroppedBases.Add($"{name}:{it.piece} at {p.x:0.0},{-p.z:0.0}");
                        continue;
                    }
                    plan.Placements.Add(new WorldPlacement
                    {
                        Piece = it.piece, Cat = "base", Team = team, Matrix = m, SimX = p.x, SimZ = -p.z, Y = p.y,
                    });
                }
                // The Sim's static wall cells under the hill ring and the shrine yard are drawn by these layouts.
                if (name == "hill" || name == "shrine")
                {
                    double ax = s.anchorSim[0], az = s.anchorSim[1], rad = name == "hill" ? 12.0 : 6.0;
                    for (int cz = 0; cz < g.H; cz++)
                    for (int cx = 0; cx < g.W; cx++)
                    {
                        if (g.T(cx, cz) != Cell.Wall) continue;
                        double dx = cx + 0.5 - ax, dz = cz + 0.5 - az;
                        if (dx * dx + dz * dz <= rad * rad) plan.Dressed[cz * g.W + cx] = true;
                    }
                }
            }
            // Each Sim breakable cell belongs to the nearest breakable piece (mesh centroid) within 4 u.
            for (int i = 0; i < g.M.Cells.Length; i++)
            {
                byte t = g.M.Cells[i];
                if (t != Cell.BreakableIntact && t != Cell.BreakableCracked && t != Cell.BreakableDestroyed) continue;
                double x = i % g.W + 0.5, z = i / g.W + 0.5, bd = 16.0;
                WorldBreakable best = null;
                foreach (var (b, bx, bz) in centres)
                {
                    double d = (bx - x) * (bx - x) + (bz - z) * (bz - z);
                    if (d < bd) { bd = d; best = b; }
                }
                if (best == null) continue;
                best.Cells.Add(i);
                plan.Dressed[i] = true;
            }
        }

        /// <summary>How many Sim breakable cells lie within 2.5 u of a site's breakable pieces when it is turned by yaw.</summary>
        static int BreakableFit(G g, BasesSite s, Vector3 anchor, float yaw, Func<string, Vector3> centroidOf)
        {
            int n = 0;
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            foreach (BasesItem it in s.items)
            {
                if (it.piece == null || !it.piece.Contains("_breakable_") || it.pos == null || it.pos.Length < 3) continue;
                Vector3 c = anchor + r * new Vector3(it.pos[0], it.pos[1], it.pos[2]) +
                            Quaternion.Euler(0f, it.yaw + yaw, 0f) * (centroidOf != null ? centroidOf(it.piece) : Vector3.zero);
                for (int i = 0; i < g.M.Cells.Length; i++)
                {
                    byte t = g.M.Cells[i];
                    if (t != Cell.BreakableIntact && t != Cell.BreakableCracked && t != Cell.BreakableDestroyed) continue;
                    double dx = i % g.W + 0.5 - c.x, dz = i / g.W + 0.5 + c.z;
                    if (dx * dx + dz * dz < 2.5 * 2.5) n++;
                }
            }
            return n;
        }

        /// <summary>True when a body vertex (0.3 to 2 u above the base) lies more than 0.4 u inside walkable space.</summary>
        public static bool Intrudes(MapDef map, Matrix4x4 m, Vector3[] body) => Intrudes(new G { M = map, W = map.Width, H = map.Height }, m, body);

        static bool Intrudes(G g, Matrix4x4 m, Vector3[] body)
        {
            if (body == null) return false;
            foreach (Vector3 v0 in body)
            {
                Vector3 v = m.MultiplyPoint3x4(v0);
                double x = v.x, z = -v.z;
                bool W(double dx, double dz) => MapGrid.IsWalkableType(g.T((int)Math.Floor(x + dx), (int)Math.Floor(z + dz)));
                if (W(0, 0) && W(0.4, 0) && W(-0.4, 0) && W(0, 0.4) && W(0, -0.4)) return true;
            }
            return false;
        }
    }
}
