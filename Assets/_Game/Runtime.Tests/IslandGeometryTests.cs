using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CriticalMass.Runtime.View;
using CriticalMass.Runtime.View.Dressing;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;
using NUnit.Framework;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b15 island geometry (WORLD-AUDIT #4/#5/#9/#12/#20/#22/#23/#28; W01 W02 W03 W04 W07 W10 W11 W12): the continuous island
    /// skirt, natural pits, the restored bases pieces, the base density, grounded props and the placeholder replacements.
    /// View only: every rule here reads the Sim's MapDef and never changes it. Every test prints its measurements
    /// ("[IslandGeometry] what: value (limit ...)") so a CI log is the evidence.
    /// </summary>
    public sealed class IslandGeometryTests
    {
        static MapDef Map() => SkybreakBasinMap.BuildSkybreakBasin(GameConfig.CreateDefault());
        static WorldKitCatalog Cat => WorldKitCatalog.Load();

        static void Print(string what, double value, string limit) => Debug.Log($"[IslandGeometry] {what}: {value:0.####} (limit {limit})");

        static WorldKitCatalog NeedKit()
        {
            WorldKitCatalog cat = WorldKitCatalog.Load();
            if (cat == null || cat.Names.Length == 0) Assert.Ignore("world kit not imported");
            return cat;
        }

        static WorldPlan PlanOf(bool clutter) => WorldDresser.Plan(Map(), BasesFile.Load(), Cat.Centroid, Cat.Body, clutter);

        static long TrisOf(WorldKitCatalog cat, string piece, Dictionary<string, long> cache)
        {
            if (cache.TryGetValue(piece, out long t)) return t;
            GameObject model = cat.Find(piece);
            bool layout = piece.StartsWith("cm_layout_", StringComparison.Ordinal);
            t = model == null ? 0 : model.GetComponentsInChildren<MeshFilter>(true).Where(mf => mf.sharedMesh != null && (!layout || mf.name.Contains("cm_district_")))
                .Sum(mf => (long)mf.sharedMesh.triangles.Length / 3);
            return cache[piece] = t;
        }

        /// <summary>Lowest and highest y of a kit model in its own frame.</summary>
        static (float lo, float hi) YRange(WorldKitCatalog cat, string piece, Dictionary<string, (float, float)> cache)
        {
            if (cache.TryGetValue(piece, out var r)) return r;
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            GameObject model = cat.Find(piece);
            if (model != null)
                foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    Matrix4x4 m = model.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    Bounds b = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                        float y = m.MultiplyPoint3x4(c).y;
                        lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
                    }
                }
            if (float.IsInfinity(lo)) lo = hi = 0f;
            return cache[piece] = (lo, hi);
        }

        // ------------------------------------------------------------------------------------------------ 1 cliffs

        [Test]
        public void TheIslandSkirtIsContinuousClosedAndUnderTheOverhangRule()
        {
            WorldKitCatalog cat = NeedKit();
            WorldPlan plan = PlanOf(false);
            var skirt = new IslandSkirt();
            var sw = Stopwatch.StartNew();
            skirt.Build(plan);
            double ms = sw.Elapsed.TotalMilliseconds;
            var tc = new Dictionary<string, long>();
            long oldTris = 0;
            int oldPieces = 0;
            foreach (WorldPlacement p in plan.Placements)
                if (WorldView.SkirtDraws(p.Piece)) { oldTris += TrisOf(cat, p.Piece, tc); oldPieces++; }
            Print("rim edges", plan.Rim.Count, "info");
            Print("outline loops / closed / capped", skirt.Loops * 10000 + skirt.ClosedLoops * 100 + skirt.CappedLoops, "loops*10000+closed*100+capped; closed == loops");
            Print("outline samples", skirt.Samples, "info");
            Print("sawtooth teeth", skirt.Teeth, "info");
            Print("skirt triangles (whole map)", skirt.Tris, "< kit edge triangles");
            Print("cap triangles", skirt.CapTris, "info");
            Print("kit cliff + lip pieces no longer drawn", oldPieces, "info");
            Print("kit cliff + lip triangles no longer drawn", oldTris, "info");
            Print("skirt chunks (renderers)", skirt.Parts.Count, "info");
            Print("largest lip / rock overhang beyond the ground edge (u)", skirt.MaxOverhang, "<= 0.15");
            Print("skirt build ms (CI editor)", ms, "info (built once per world build, rebuilt only when the rim moves)");
            Assert.Greater(plan.Rim.Count, 100, "the plan has a rim");
            Assert.AreEqual(skirt.Loops, skirt.ClosedLoops, "every island outline closes on itself (no gap in the skirt)");
            Assert.GreaterOrEqual(skirt.CappedLoops, 1, "the islands' undersides are closed");
            Assert.LessOrEqual(skirt.MaxOverhang, 0.15 + 1e-6, "the lip overhangs the walkable edge by at most 0.15 u");
            Assert.Less(skirt.Tris, oldTris, "the skirt is cheaper than the kit edge pieces it replaces");
            // No gap: every rim edge's midpoint is a skirt vertex at the edge's ground height (the lip starts exactly at the
            // ground's edge, everywhere).
            var verts = new HashSet<(int, int, int)>();
            foreach (IslandSkirt.Part part in skirt.Parts.Values)
                foreach (Vector3 v in part.V) verts.Add((Mathf.RoundToInt(v.x * 100), Mathf.RoundToInt(v.y * 100), Mathf.RoundToInt(-v.z * 100)));
            var missing = new List<string>();
            foreach (WorldEdge e in plan.Rim)
            {
                double mx = (e.Px + e.Qx) * 0.5, mz = (e.Pz + e.Qz) * 0.5;
                if (!verts.Contains(((int)Math.Round(mx * 100), (int)Math.Round(e.H * 100), (int)Math.Round(mz * 100)))) missing.Add($"{mx:0.0},{mz:0.0}");
            }
            Print("rim edges without a skirt vertex at their midpoint", missing.Count, "== 0");
            CollectionAssert.IsEmpty(missing, "skirt gaps at rim edges: " + string.Join(" ", missing.Take(20)));
            // Every triangle's normal is finite and the streams are aligned.
            foreach (IslandSkirt.Part part in skirt.Parts.Values)
            {
                Assert.AreEqual(part.V.Count, part.N.Count);
                Assert.AreEqual(part.V.Count, part.C.Count);
                Assert.AreEqual(part.V.Count, part.Outline.Count);
                Assert.AreEqual(part.V.Count, part.W.Count);
                Assert.AreEqual(0, part.T.Count % 3);
            }
            // Deterministic.
            var again = new IslandSkirt();
            again.Build(plan);
            Assert.AreEqual(skirt.Tris, again.Tris, "same plan, same skirt");
        }

        // ------------------------------------------------------------------------------------------------ 2 pits

        const int W = 12, Hh = 12;
        const double G = 0.5;
        const float Eps = 1e-4f;
        static int I(int cx, int cz) => cz * W + cx;
        static byte[] All(byte t, int n = W * Hh) => Enumerable.Repeat(t, n).ToArray();
        static double[] Heights(int n = W * Hh) => Enumerable.Repeat(G, n).ToArray();

        static bool HasVert(TerrainEditMesh m, double simX, double y, double simZ) =>
            m.Verts.Any(v => Mathf.Abs(v.x - (float)simX) < Eps && Mathf.Abs(v.y - (float)y) < Eps && Mathf.Abs(v.z + (float)simZ) < Eps);

        [Test]
        public void ANaturalPitHasARoundedRimSlopedWallsAndAClosedFloor()
        {
            Assert.IsTrue(TerrainEditMesh.OrganicPits, "natural pits are the default");
            byte[] pristine = All(Cell.Floor), cells = All(Cell.Floor);
            var touched = new bool[W * Hh];
            cells[I(5, 5)] = Cell.Void;
            touched[I(5, 5)] = true;
            var m = new TerrainEditMesh();
            m.Build(W, Hh, cells, Heights(), pristine, touched, 0, 0, W, Hh);
            int tris = m.Tris.Count / 3;
            Print("lone dug cell: triangles", tris, "<= " + TerrainEditMesh.MaxTrisPerPitCell);
            Print("lone dug cell: rim sides / floors / stones / spoil heaps", m.PitWallFaces * 1000 + m.PitFloorFaces * 100 + m.Pebbles * 10 + m.SpoilLumps, "4 / 1 / 2 / 0..1");
            Assert.AreEqual(1, m.PitCells);
            Assert.AreEqual(4, m.PitWallFaces, "a rim on every side toward unbroken ground");
            Assert.AreEqual(1, m.PitFloorFaces, "a floor closes the pit");
            Assert.LessOrEqual(tris, TerrainEditMesh.MaxTrisPerPitCell, "triangle cap per dug cell");
            Assert.LessOrEqual(m.SpoilLumps, 1, "at most one spoil heap per cell");
            Assert.AreEqual(G - TerrainEditMesh.PitDepth1, m.MinFloorY, Eps, "floor at the dug depth");
            float maxY = m.Verts.Max(v => v.y), minY = m.Verts.Min(v => v.y);
            Assert.LessOrEqual(maxY - G, 0.14f + Eps, "spoil heaps stay low");
            Assert.GreaterOrEqual(minY, (float)(G - TerrainEditMesh.PitDepth1) - Eps, "nothing under the floor");
            foreach (var (x, z) in new[] { (5, 5), (6, 5), (6, 6), (5, 6) })
                Assert.IsTrue(HasVert(m, x, G, z), $"the grass lip meets the ground at the cell corner ({x}, {z})");
            // Below the ground everything stays inside the dug cell, and a lone cell's corners are cut round: no wall vertex
            // within 0.9 x CornerCut of a cell corner.
            float nearest = float.MaxValue;
            foreach (Vector3 v in m.Verts)
            {
                if (v.y > G - 0.02f) continue;   // the lip, and spoil heaps (their foot is 0.015 u under the neighbour's ground)
                float sx = v.x, sz = -v.z;
                Assert.That(sx >= 5 - Eps && sx <= 6 + Eps && sz >= 5 - Eps && sz <= 6 + Eps, $"pit vertex {v} outside the dug cell");
                foreach (var (x, z) in new[] { (5f, 5f), (6f, 5f), (6f, 6f), (5f, 6f) })
                    nearest = Mathf.Min(nearest, new Vector2(sx - x, sz - z).magnitude);
            }
            Print("closest below-ground vertex to a cell corner (u)", nearest, ">= " + 0.9f * TerrainEditMesh.CornerCut);
            Assert.GreaterOrEqual(nearest, 0.9f * TerrainEditMesh.CornerCut, "the pit's corners are rounded, not square");
            // Walls lean into the pit (per triangle: the floor fan is triangles, the rest quads).
            var centre = SimSpace.ToWorld(5.5, G, 5.5);
            int walls = 0;
            for (int t = 0; t + 2 < m.Tris.Count; t += 3)
            {
                int i0 = m.Tris[t], i1 = m.Tris[t + 1], i2 = m.Tris[t + 2];
                Vector3 n = m.Normals[i0];
                if (m.Weights[i0].x > 0f || n.y <= 0.05f || n.y >= 0.99f) continue;
                Vector3 a = m.Verts[i0], b = m.Verts[i1], c = m.Verts[i2];
                if (Mathf.Max(a.y, Mathf.Max(b.y, c.y)) - Mathf.Min(a.y, Mathf.Min(b.y, c.y)) < 1e-3f) continue;   // flat strips
                Vector3 to = centre - (a + b + c) / 3f;
                to.y = 0;
                if (to.sqrMagnitude < 1e-6f) continue;
                walls++;
                Assert.Greater(Vector3.Dot(new Vector3(n.x, 0, n.z), to), 0f, "pit wall triangle " + t / 3 + " faces into the pit");
            }
            walls /= 2;   // quads
            Print("lone dug cell: sloped wall faces", walls, ">= 48");
            Assert.GreaterOrEqual(walls, 48, "four sides of three segments of four bands");
        }

        [Test]
        public void ADugAreaIsOnePitWithNoInnerWallsAndADeeperMiddle()
        {
            byte[] pristine = All(Cell.Floor), cells = All(Cell.Floor);
            var touched = new bool[W * Hh];
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++) { cells[I(5 + dx, 5 + dz)] = Cell.Void; touched[I(5 + dx, 5 + dz)] = true; }
            var m = new TerrainEditMesh();
            m.Build(W, Hh, cells, Heights(), pristine, touched, 0, 0, W, Hh);
            int tris = m.Tris.Count / 3;
            Print("3x3 dig: triangles", tris, "<= 9 x " + TerrainEditMesh.MaxTrisPerPitCell);
            Print("3x3 dig: rim sides", m.PitWallFaces, "== 12");
            Print("3x3 dig: step walls", m.StepWallFaces, "== 0 (the floor slopes)");
            Assert.AreEqual(9, m.PitCells);
            Assert.AreEqual(12, m.PitWallFaces, "rims only toward unbroken ground: no wall between two dug cells");
            Assert.AreEqual(0, m.StepWallFaces, "the floor deepens smoothly: no step walls");
            Assert.LessOrEqual(tris, 9 * TerrainEditMesh.MaxTrisPerPitCell);
            // The middle of the floor is the lowest point and lower than the rim cells' floor.
            float centreY = float.MaxValue, rimY = float.MaxValue;
            foreach (Vector3 v in m.Verts)
            {
                float sx = v.x, sz = -v.z;
                if (Mathf.Abs(sx - 5.5f) < 0.05f && Mathf.Abs(sz - 5.5f) < 0.05f) centreY = Mathf.Min(centreY, v.y);
                if (Mathf.Abs(sx - 4.5f) < 0.05f && Mathf.Abs(sz - 4.5f) < 0.05f) rimY = Mathf.Min(rimY, v.y);
            }
            Print("3x3 dig: floor depth at the middle", G - centreY, "> rim cell's");
            Print("3x3 dig: floor depth at a corner cell", G - rimY, "info");
            Assert.Less(centreY, rimY - 0.02f, "the dig deepens toward its middle");
            // Seamless: each dug cell built on its own; every below-ground point a cell draws on the edge it shares with another
            // dug cell is drawn by that cell too (the same corner and floor heights from both sides), so the floor has no cracks.
            var perCell = new Dictionary<(int, int), HashSet<(int, int, int)>>();
            for (int cz = 4; cz <= 6; cz++)
            for (int cx = 4; cx <= 6; cx++)
            {
                m.Build(W, Hh, cells, Heights(), pristine, touched, cx, cz, cx + 1, cz + 1);
                var set = new HashSet<(int, int, int)>();
                foreach (Vector3 v in m.Verts) set.Add((Mathf.RoundToInt(v.x * 1000), Mathf.RoundToInt(v.y * 1000), Mathf.RoundToInt(-v.z * 1000)));
                perCell[(cx, cz)] = set;
            }
            int shared = 0, lonely = 0;
            foreach (var kv in perCell)
            {
                var (cx, cz) = kv.Key;
                foreach (var (dx, dz) in new[] { (1, 0), (0, 1) })
                {
                    if (!perCell.TryGetValue((cx + dx, cz + dz), out var other)) continue;
                    int edge = (dx == 1 ? cx + 1 : cz + 1) * 1000;
                    foreach (var k in kv.Value)
                    {
                        if (k.Item2 > (G - 0.2) * 1000 || (dx == 1 ? k.Item1 : k.Item3) != edge) continue;
                        if (other.Contains(k)) shared++; else lonely++;
                    }
                }
            }
            Print("3x3 dig: floor points on inner cell edges shared by both cells", shared, "> 0");
            Print("3x3 dig: floor points on inner cell edges drawn by one cell only", lonely, "== 0");
            Assert.Greater(shared, 0);
            Assert.AreEqual(0, lonely, "floor cracks between dug cells");
        }

        [Test]
        public void PitAndChunkRebuildCostBeforeAndAfter()
        {
            const int N = 48;
            byte[] pristine = All(Cell.Floor, N * N), cells = All(Cell.Floor, N * N);
            var touched = new bool[N * N];
            for (int z = 0; z < N; z++) for (int x = 36; x < N; x++) pristine[z * N + x] = cells[z * N + x] = Cell.Void;
            int pits = 0;
            for (int z = 16; z < 32; z++)
            for (int x = 16; x < 32; x++)
            {
                int i = z * N + x;
                if ((x + 2 * z) % 5 == 0 || (x >= 20 && x <= 23 && z >= 20 && z <= 23)) { cells[i] = Cell.Void; touched[i] = true; pits++; }
                else if ((x * 7 + z) % 11 == 0) cells[i] = Cell.LowWall;
            }
            // The all-dug chunk: every cell of a 16 x 16 chunk dug (the cap's worst case per chunk).
            byte[] full = All(Cell.Floor, N * N);
            var tFull = new bool[N * N];
            for (int z = 16; z < 32; z++) for (int x = 16; x < 32; x++) { full[z * N + x] = Cell.Void; tFull[z * N + x] = true; }
            double[] h = Heights(N * N);
            bool was = TerrainEditMesh.OrganicPits;
            try
            {
                foreach (bool organic in new[] { false, true })
                {
                    TerrainEditMesh.OrganicPits = organic;
                    string tag = organic ? "after (natural pits)" : "before (square pits)";
                    var m = new TerrainEditMesh();
                    for (int k = 0; k < 3; k++) m.Build(N, N, cells, h, pristine, touched, 16, 16, 32, 32);
                    const int Runs = 40;
                    var sw = Stopwatch.StartNew();
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int k = 0; k < Runs; k++) m.Build(N, N, cells, h, pristine, touched, 16, 16, 32, 32);
                    long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
                    double ms = sw.Elapsed.TotalMilliseconds / Runs;
                    int tris = m.Tris.Count / 3;
                    for (int k = 0; k < 3; k++) m.Build(N, N, full, h, pristine, tFull, 16, 16, 32, 32);
                    sw.Restart();
                    for (int k = 0; k < Runs; k++) m.Build(N, N, full, h, pristine, tFull, 16, 16, 32, 32);
                    double msFull = sw.Elapsed.TotalMilliseconds / Runs;
                    int trisFull = m.Tris.Count / 3;
                    Print($"{tag}: worst test chunk ({pits} pits) rebuild ms", ms, "< 5.0");
                    Print($"{tag}: worst test chunk triangles", tris, "info");
                    Print($"{tag}: worst test chunk managed bytes over {Runs} rebuilds", alloc, "== 0");
                    Print($"{tag}: all-dug chunk (256 pits) rebuild ms", msFull, "< 5.0");
                    Print($"{tag}: all-dug chunk triangles", trisFull, "<= 256 x " + TerrainEditMesh.MaxTrisPerPitCell);
                    Assert.AreEqual(0, alloc, tag + ": rebuild allocates");
                    Assert.Less(ms, 5.0, tag + ": worst-case rebuild cost");
                    Assert.Less(msFull, 5.0, tag + ": all-dug chunk rebuild cost");
                    Assert.LessOrEqual(trisFull, 256 * TerrainEditMesh.MaxTrisPerPitCell, tag + ": triangle cap");
                }
            }
            finally { TerrainEditMesh.OrganicPits = was; }
        }

        [Test]
        public void ShortWallStonesAreIrregularButKeepTheirGridTop()
        {
            byte[] cells = All(Cell.Floor);
            cells[I(3, 4)] = cells[I(4, 4)] = cells[I(5, 4)] = Cell.LowWall;
            var m = new TerrainEditMesh();
            var tops = new List<float>();
            for (int x = 3; x <= 5; x++)
            {
                m.Build(W, Hh, cells, Heights(), null, null, x, 4, x + 1, 5);
                Assert.AreEqual((float)(G + TerrainEditMesh.ShortTop), m.Verts.Max(v => v.y), Eps, "the stone's highest corner is on the grid height");
                float lo = m.Verts.Where(v => v.y > G + TerrainEditMesh.ShortTop - TerrainEditMesh.StoneBevel + 0.001f).Min(v => v.y);
                tops.Add(lo);
            }
            float spread = tops.Max() - tops.Min();
            Print("three stones: spread of their lowest top corner (u)", spread, "> 0 (irregular)");
            Assert.Greater(tops.Select(t => Mathf.RoundToInt(t * 1000)).Distinct().Count(), 1, "the stones of a run are not identical boxes");
        }

        // ------------------------------------------------------------------------------------------------ 3 missing pieces

        [Test]
        public void ThePlanCarriesEveryAuthoredBasesPiece()
        {
            NeedKit();
            BasesFile bases = BasesFile.Load();
            WorldPlan plan = PlanOf(false);
            Debug.Log("[IslandGeometry] bases notes: " + string.Join("; ", plan.Notes));
            Debug.Log("[IslandGeometry] bases pieces left out: " + plan.DroppedBases.Count + ": " + string.Join("; ", plan.DroppedBases));
            var authored = new Dictionary<string, int>();
            foreach (string site in WorldDresser.BaseSites)   // the sites the world places (the file carries other layouts too)
            {
                BasesSite s = bases.Site(site);
                if (s == null || s.items == null) continue;
                bool district = s.name.StartsWith("district_", StringComparison.Ordinal);
                bool[] kept = district ? WorldDresser.DistrictKept(s.items) : null;
                for (int i = 0; i < s.items.Length; i++)
                {
                    BasesItem it = s.items[i];
                    if (it.piece == null || it.piece.Contains("_breakable_")) continue;
                    if (district && !kept[i]) continue;
                    authored[it.piece] = authored.TryGetValue(it.piece, out int n) ? n + 1 : 1;
                }
            }
            var placed = plan.Placements.Where(p => p.Cat == "base").GroupBy(p => p.Piece).ToDictionary(g => g.Key, g => g.Count());
            var short1 = authored.Where(kv => !placed.TryGetValue(kv.Key, out int n) || n < kv.Value)
                .Select(kv => $"{kv.Key} {(placed.TryGetValue(kv.Key, out int n) ? n : 0)}/{kv.Value}").ToArray();
            Print("authored bases piece ids", authored.Count, "info");
            Print("authored bases items (after the district thinning)", authored.Values.Sum(), "info");
            Print("bases pieces left out", plan.DroppedBases.Count, "== 0");
            Print("piece ids placed fewer times than authored", short1.Length, "== 0");
            CollectionAssert.IsEmpty(plan.DroppedBases, "bases pieces left out by the walkable rule");
            CollectionAssert.IsEmpty(short1, "authored piece ids missing from the plan: " + string.Join(", ", short1));
        }

        // ------------------------------------------------------------------------------------------------ 4 base density

        static int Kind(string piece)
        {
            string l = piece.ToLowerInvariant();
            if (l.Contains("lantern") || l.Contains("brazier") || l.Contains("torch") || l.Contains("campfire") || l.Contains("lamp") || l.Contains("light")) return 2;
            if (l.Contains("tree") || l.Contains("boulder_l") || l.Contains("pine") || l.Contains("oak")) return 1;
            return 0;
        }

        static readonly HashSet<string> WindowSkip = new HashSet<string> { "edge", "cliff", "sky", "ground", "bridge", "ground_decal", "water" };

        static (int small, int lights) Window(WorldPlan plan, WorldKitCatalog cat, double cx, double cz, Dictionary<string, (float, float)> yr)
        {
            int small = 0, lights = 0;
            foreach (WorldPlacement p in plan.Placements)
            {
                if (Math.Abs(p.SimX - cx) > 13 || Math.Abs(p.SimZ - cz) > 9.5 || p.Surface || WindowSkip.Contains(p.Cat ?? "")) continue;
                if (p.Piece.StartsWith("cm_layout_", StringComparison.Ordinal)) continue;
                int k = Kind(p.Piece);
                var (lo, hi) = YRange(cat, p.Piece, yr);
                float h = (hi - lo) * p.Matrix.lossyScale.y;
                if (k == 0 && h < 1.5f) small++;
                else if (k == 2) lights++;
            }
            return (small, lights);
        }

        [Test]
        public void BaseWindowsMeetTheBibleDensity()
        {
            WorldKitCatalog cat = NeedKit();
            var yr = new Dictionary<string, (float, float)>();
            var windows = new[] { (13.0, 36.0), (99.0, 36.0), (8.0, 36.0), (104.0, 36.0) };
            bool was = WorldDresser.ThinBases;
            try
            {
                WorldDresser.ThinBases = false;
                WorldPlan before = PlanOf(true);
                WorldDresser.ThinBases = true;
                WorldPlan after = PlanOf(true);
                foreach (var (x, z) in windows)
                {
                    var b = Window(before, cat, x, z, yr);
                    var a = Window(after, cat, x, z, yr);
                    Print($"base window at {x:0},{z:0} (26 x 19): small props before -> after", b.small * 1000 + a.small, "before*1000+after; after 45..60");
                    Print($"base window at {x:0},{z:0} (26 x 19): lights before -> after", b.lights * 1000 + a.lights, "before*1000+after; after 10..14");
                    Assert.That(a.small, Is.InRange(45, 60), $"small props in the base window at {x},{z}");
                    Assert.That(a.lights, Is.InRange(10, 14), $"lights in the base window at {x},{z}");
                }
                Print("clutter pieces before -> after", before.ClutterCount * 10000 + after.ClutterCount, "before*10000+after");
                Print("base-plan placements before -> after", (before.Placements.Count - before.ClutterCount) * 10000 + (after.Placements.Count - after.ClutterCount), "before*10000+after");
                Print("route signposts / light hooks (dead space)", after.RouteSigns * 100 + after.RouteLights.Count, "signs*100+hooks");
                foreach (double[] l in after.RouteLights)
                    Debug.Log($"[IslandGeometry] route light hook: sim {l[0]:0.00},{l[1]:0.00} ground {l[2]:0.00} facing {l[3]:0.00},{l[4]:0.00}");
            }
            finally { WorldDresser.ThinBases = was; }
        }

        [Test]
        public void NoTallPropStandsOnAWalkableCellWithClutterOn()
        {
            WorldKitCatalog cat = NeedKit();
            MapDef map = Map();
            var yr = new Dictionary<string, (float, float)>();
            WorldPlan plan = WorldDresser.Plan(map, BasesFile.Load(), Cat.Centroid, Cat.Body, true);
            var bad = new List<string>();
            int n = 0;
            foreach (WorldPlacement p in plan.Placements)
            {
                if (p.Surface || p.Cat == "sky" || p.Cat == "base" || p.Cat == "decor") continue;
                var (lo, hi) = YRange(cat, p.Piece, yr);
                if (hi * p.Matrix.lossyScale.y <= 0.3f) continue;
                n++;
                int cx = (int)Math.Floor(p.SimX), cz = (int)Math.Floor(p.SimZ);
                if (MapGrid.IsWalkable(map, cx, cz)) bad.Add($"{p.Piece} ({p.Cat}) at sim ({p.SimX:0.00}, {p.SimZ:0.00})");
            }
            int lanterns = plan.Placements.Count(p => p.Piece == "env_lantern_hang");
            Print("tall props checked (clutter on)", n, "info");
            Print("tall props on walkable cells (clutter on)", bad.Count, "== 0");
            Print("hanging deck lanterns", lanterns, "== 0");
            foreach (string b in bad.Take(40)) Debug.Log("[IslandGeometry] tall on walkable: " + b);
            Assert.AreEqual(0, lanterns, "no lantern hangs from nothing over a bridge deck");
            CollectionAssert.IsEmpty(bad, "tall props on walkable cells:\n" + string.Join("\n", bad.Take(20)));
        }

        // ------------------------------------------------------------------------------------------------ 5 placeholders

        /// <summary>The ground a prop at sim (x, z) stands on: its cell's height if walkable or water; on the void (a prop on
        /// the rim lip), the highest walkable neighbour; NaN where the support is a wall top (wall cells) or a bridge.</summary>
        static double GroundAt(MapDef map, double x, double z)
        {
            int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
            if (cx < 0 || cz < 0 || cx >= map.Width || cz >= map.Height) return double.NaN;
            byte t = map.Cells[cz * map.Width + cx];
            if (t == Cell.Bridge) return double.NaN;
            if (MapGrid.IsWalkableType(t) || t == Cell.Water) return map.Height01[cz * map.Width + cx];
            if (t != Cell.Void) return double.NaN;   // walls, breakables, low walls: the prop stands on the wall's top
            double best = double.NaN;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = cx + dx, nz = cz + dz;
                if (nx < 0 || nz < 0 || nx >= map.Width || nz >= map.Height) continue;
                byte nt = map.Cells[nz * map.Width + nx];
                if (nt == Cell.Bridge) return double.NaN;   // a bridge mouth: the deck is the support
                if (!MapGrid.IsWalkableType(nt)) continue;
                double hh = map.Height01[nz * map.Width + nx];
                if (double.IsNaN(best) || hh > best) best = hh;
            }
            return best;
        }

        /// <summary>Authored floats (hovering on purpose): the hill marker and the egg.</summary>
        static bool AuthoredFloat(string piece) => piece.Contains("marker") || piece.Contains("egg");

        /// <summary>True when another placement holds this one up: its pivot within 0.75 u, its foot lower and its top at
        /// this foot (a crate on a crate; the hill's horns on the hill's own assembly, which shares one pivot).</summary>
        static bool RestsOnAnother(WorldPlan plan, WorldKitCatalog cat, Dictionary<string, (float, float)> yr, WorldPlacement p, double foot)
        {
            foreach (WorldPlacement q in plan.Placements)
            {
                if (ReferenceEquals(q.Piece, p.Piece) && q.SimX == p.SimX && q.SimZ == p.SimZ) continue;
                if (q.Surface || q.Cat == "sky") continue;
                double dx = q.SimX - p.SimX, dz = q.SimZ - p.SimZ;
                if (dx * dx + dz * dz > 0.5625) continue;
                var (lo, hi) = YRange(cat, q.Piece, yr);
                float sc = q.Matrix.lossyScale.y;
                double qFoot = q.Y + lo * sc, qTop = q.Y + hi * sc;
                if (qFoot < foot - 0.05 && qTop >= foot - 0.10) return true;
            }
            return false;
        }

        static readonly HashSet<string> GroundSkip = new HashSet<string> { "sky", "decor", "bridge", "edge", "cliff", "water", "waterfall", "ground" };

        [Test]
        public void EveryPropStandsOnTheGroundAndDecalsSitJustOverIt()
        {
            WorldKitCatalog cat = NeedKit();
            MapDef map = Map();
            var yr = new Dictionary<string, (float, float)>();
            WorldPlan plan = WorldDresser.Plan(map, BasesFile.Load(), Cat.Centroid, Cat.Body, true);
            const double Tol = 0.10;
            var floats = new List<string>();
            var flat = new List<string>();
            int props = 0, decals = 0;
            foreach (WorldPlacement p in plan.Placements)
            {
                if (p.Surface || GroundSkip.Contains(p.Cat ?? "") || p.Piece.StartsWith("cm_layout_", StringComparison.Ordinal)) continue;
                if (p.Piece.StartsWith("env_cliff_", StringComparison.Ordinal) || p.Piece.StartsWith("env_lip_", StringComparison.Ordinal) || p.Piece.StartsWith("env_waterfall", StringComparison.Ordinal)) continue;
                if (p.Piece.StartsWith("env_sign_board_", StringComparison.Ordinal)) continue;   // boards hang on their post
                double g = GroundAt(map, p.SimX, p.SimZ);
                if (double.IsNaN(g)) continue;
                var (lo, _) = YRange(cat, p.Piece, yr);
                double foot = p.Y + lo * p.Matrix.lossyScale.y;
                if (p.Cat == "ground_decal")
                {
                    decals++;
                    double lift = p.Y - g;
                    if (lift < 0.008 || lift > 0.05) flat.Add($"{p.Piece} at {p.SimX:0.0},{p.SimZ:0.0}: {lift:0.000} u over the ground");
                    continue;
                }
                props++;
                if (foot - g > Tol && !AuthoredFloat(p.Piece) && !RestsOnAnother(plan, cat, yr, p, foot)) floats.Add($"{p.Piece} ({p.Cat}) at {p.SimX:0.0},{p.SimZ:0.0}: foot {foot - g:0.00} u over the ground");
            }
            Print("props checked against the ground", props, "info");
            Print($"props whose foot is more than {Tol} u over the ground", floats.Count, "== 0 (hill marker and egg are authored floats)");
            Print("ground decals checked", decals, "info");
            Print("ground decals not 0.008..0.05 u over the ground (z-fight)", flat.Count, "== 0");
            foreach (string f in floats.Take(40)) Debug.Log("[IslandGeometry] float: " + f);
            foreach (string f in flat.Take(20)) Debug.Log("[IslandGeometry] flat decal: " + f);
            CollectionAssert.IsEmpty(floats, "floating props:\n" + string.Join("\n", floats.Take(20)));
            CollectionAssert.IsEmpty(flat, "coplanar decals:\n" + string.Join("\n", flat.Take(20)));
        }

        [Test]
        public void SkyRocksAreFewerAndVaried()
        {
            WorldPlan plan = PlanOf(false);
            WorldPlacement[] rocks = plan.Placements.Where(p => p.Cat == "sky" && (p.Piece.StartsWith("env_sky_rock_", StringComparison.Ordinal) || p.Piece.StartsWith("env_float_chunk_", StringComparison.Ordinal))).ToArray();
            int pieces = rocks.Select(p => p.Piece).Distinct().Count();
            int yaws = rocks.Select(p => Mathf.RoundToInt(p.Matrix.rotation.eulerAngles.y / 10f)).Distinct().Count();
            int scales = rocks.Select(p => Mathf.RoundToInt(p.Matrix.lossyScale.x * 10f)).Distinct().Count();
            Print("sky rocks", rocks.Length, "== " + WorldDresser.SkyRocks + " (was 70)");
            Print("sky rock silhouettes", pieces, ">= 4 (was 3)");
            Print("sky rock yaw buckets (10 degrees)", yaws, ">= 20 (was 1)");
            Print("sky rock scale buckets (0.1)", scales, ">= 5 (was 1)");
            Assert.AreEqual(WorldDresser.SkyRocks, rocks.Length);
            Assert.GreaterOrEqual(pieces, 4);
            Assert.GreaterOrEqual(yaws, 20);
            Assert.GreaterOrEqual(scales, 5);
        }

        [Test]
        public void TheHillPlateIsRoundAndTheFallArrowsAreFlatMeshes()
        {
            Mesh disc = HillDisc.Mesh;
            float rMin = float.MaxValue, rMax = 0f;
            foreach (Vector3 v in disc.vertices)
            {
                if (v.y > -0.49f) continue;   // the bottom rim
                float r = new Vector2(v.x, v.z).magnitude;
                rMin = Mathf.Min(rMin, r); rMax = Mathf.Max(rMax, r);
            }
            Print("hill plate rim radius min / max (unit plate)", rMin * 1000 + rMax, "min*1000+max; 0.5 x (1 - 0.035) .. 0.5");
            Print("hill plate triangles", disc.triangles.Length / 3, "info");
            Assert.GreaterOrEqual(rMin, 0.5f * (1f - HillDisc.EdgeWobble) - 1e-4f, "round: every rim point near radius 0.5");
            Assert.LessOrEqual(rMax, 0.5f + 1e-4f, "never wider than the zone");
            Assert.AreEqual(1f, disc.bounds.size.y, 1e-4f, "unit height (scaled to the plate thickness)");
            Mesh arrow = DecorationView.ArrowMesh;
            Print("fall arrow triangles", arrow.triangles.Length / 3, "info");
            Print("fall arrow height (u)", arrow.bounds.max.y, "<= 0.06");
            Assert.LessOrEqual(arrow.bounds.max.y, 0.06f + 1e-4f, "flat on the ground");
            Assert.GreaterOrEqual(arrow.bounds.min.y, -1e-4f);
            Assert.AreEqual(2.75f, arrow.bounds.max.z, 1e-3f, "head tip 2.75 u from the pillar");
        }

        // ------------------------------------------------------------------------------------------------ budget

        [Test]
        public void WorldTrianglesBeforeAndAfter()
        {
            WorldKitCatalog cat = NeedKit();
            var tc = new Dictionary<string, long>();
            bool was = WorldDresser.ThinBases;
            try
            {
                foreach (bool after in new[] { false, true })
                {
                    WorldDresser.ThinBases = after;
                    WorldPlan plan = PlanOf(true);
                    long all = 0, edges = 0, decor = 0;
                    foreach (WorldPlacement p in plan.Placements)
                    {
                        if (p.Piece == "env_ground_cell") continue;
                        long t = TrisOf(cat, p.Piece, tc);
                        all += t;
                        if (WorldView.SkirtDraws(p.Piece)) edges += t;
                        if (p.Cat == "decor") decor += t;
                    }
                    long drawn = all;
                    if (after)
                    {
                        var skirt = new IslandSkirt();
                        skirt.Build(plan);
                        drawn = all - edges - decor + skirt.Tris;
                        Print("after: kit pieces (clutter on, whole map, no ground)", all, "info");
                        Print("after: less kit edges and the pillar column (now drawn by the skirt / DecorationView)", edges + decor, "info");
                        Print("after: plus the island skirt", skirt.Tris, "info");
                    }
                    Print((after ? "after" : "before (b14 density, kit edges)") + ": whole-map dressed triangles (clutter on, no ground)", drawn, "info; the after must be lower");
                }
            }
            finally { WorldDresser.ThinBases = was; }
        }
    }
}
