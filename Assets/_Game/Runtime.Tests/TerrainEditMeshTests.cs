using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CriticalMass.Runtime.View;
using CriticalMass.Runtime.View.B13;
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
    /// b14 terrain look (owner 2026-11-17: a dig showed the sky, a placed block was a flat brick, stacking looked wrong):
    /// a dug cell draws pit walls and a floor and carries no dressing; a deeper dig shows taller walls; a placed cell has
    /// sides and a bottom; a filled pit is a flush block; short wall blocks sit exactly on their cell, top at the ghost
    /// height, and a row of them is flush (no inner faces, no gap); a chunk rebuild allocates nothing and stays fast.
    /// Every test prints one line per measured case (what, value, limit).
    /// </summary>
    public sealed class TerrainEditMeshTests
    {
        const int W = 12, H = 12;
        const double G = 0.5;   // ground height of the synthetic maps
        const float Eps = 1e-4f;

        static int I(int cx, int cz) => cz * W + cx;
        static byte[] All(byte t, int n = W * H) => Enumerable.Repeat(t, n).ToArray();
        static double[] Heights(int n = W * H) => Enumerable.Repeat(G, n).ToArray();

        static void Print(string what, double value, string limit) =>
            Debug.Log($"[TerrainEdits] {what}: {value:0.####} (limit {limit})");

        static bool HasVert(TerrainEditMesh m, double simX, double y, double simZ) =>
            m.Verts.Any(v => Mathf.Abs(v.x - (float)simX) < Eps && Mathf.Abs(v.y - (float)y) < Eps && Mathf.Abs(v.z + (float)simZ) < Eps);

        [Test]
        public void ADugCellDrawsPitWallsAndAFloor()
        {
            // b15: these pin the b14 square pits (TerrainEditMesh.OrganicPits off); the natural pits are IslandGeometryTests'.
            bool was = TerrainEditMesh.OrganicPits;
            TerrainEditMesh.OrganicPits = false;
            try { ADugCellDrawsPitWallsAndAFloorSquare(); }
            finally { TerrainEditMesh.OrganicPits = was; }
        }

        void ADugCellDrawsPitWallsAndAFloorSquare()
        {
            byte[] pristine = All(Cell.Floor), cells = All(Cell.Floor);
            var touched = new bool[W * H];
            cells[I(5, 5)] = Cell.Void;
            touched[I(5, 5)] = true;
            Assert.AreEqual(TerrainEdits.Kind.Pit, TerrainEdits.Classify(Cell.Floor, Cell.Void, true), "a dug floor cell is a pit");
            Assert.AreEqual(TerrainEdits.Kind.None, TerrainEdits.Classify(Cell.Void, Cell.Void, false), "the island's own void stays undrawn");
            var m = new TerrainEditMesh();
            m.Build(W, H, cells, Heights(), pristine, touched, 0, 0, W, H);
            Print("pit cells", m.PitCells, "== 1");
            Print("pit rim walls", m.PitWallFaces, "== 4 (every side faces unbroken ground)");
            Print("pit floor faces", m.PitFloorFaces, "== 1");
            Print("pit floor height below ground", G - m.MinFloorY, "== " + TerrainEditMesh.PitDepth1);
            Print("spoil heaps at the rim", m.SpoilLumps, "1..4 (about two rims in three get a heap, by cell hash)");
            float maxY = m.Verts.Max(v => v.y), minY = m.Verts.Min(v => v.y);
            Print("highest vertex above ground (spoil lumps)", maxY - G, "<= 0.14");
            Print("lowest vertex below ground", G - minY, "<= " + TerrainEditMesh.PitDepth1);
            Assert.AreEqual(1, m.PitCells);
            Assert.AreEqual(4, m.PitWallFaces, "one rim wall per side");
            Assert.AreEqual(1, m.PitFloorFaces, "a floor closes the pit: never a hole to the sky");
            Assert.AreEqual(G - TerrainEditMesh.PitDepth1, m.MinFloorY, Eps, "floor at the dug depth");
            Assert.That(m.SpoilLumps, Is.InRange(1, 4), "spoil heaps on the untouched ground at the rim");
            Assert.LessOrEqual(maxY - G, 0.14f + Eps);
            Assert.GreaterOrEqual(minY, (float)(G - TerrainEditMesh.PitDepth1) - Eps);
            foreach (var (x, z) in new[] { (5, 5), (6, 5), (6, 6), (5, 6) })
                Assert.IsTrue(HasVert(m, x, G, z), $"the rim wall's top edge meets the ground at corner ({x}, {z})");
            // The floor is inset by the taper on every walled side, and every wall face leans into the pit (normal toward the
            // cell centre, slightly up), so nothing faces away from the player looking in.
            Assert.IsTrue(HasVert(m, 5 + TerrainEditMesh.Taper, G - TerrainEditMesh.PitDepth1, 5 + TerrainEditMesh.Taper), "tapered floor corner");
            var centre = SimSpace.ToWorld(5.5, G, 5.5);
            for (int q = 0; q < m.Verts.Count; q += 4)
            {
                Vector3 n = m.Normals[q];
                if (n.y > 0.5f || m.Weights[q].x > 0f) continue;   // floors, lumps
                Vector3 mid = (m.Verts[q] + m.Verts[q + 1] + m.Verts[q + 2] + m.Verts[q + 3]) * 0.25f;
                Vector3 toCentre = centre - mid;
                toCentre.y = 0;
                Assert.Greater(Vector3.Dot(n, toCentre), 0f, "pit wall face " + q / 4 + " faces into the pit");
            }
        }

        [Test]
        public void ADeeperDigShowsTallerWalls()
        {
            // b15: these pin the b14 square pits (TerrainEditMesh.OrganicPits off); the natural pits are IslandGeometryTests'.
            bool was = TerrainEditMesh.OrganicPits;
            TerrainEditMesh.OrganicPits = false;
            try { ADeeperDigShowsTallerWallsSquare(); }
            finally { TerrainEditMesh.OrganicPits = was; }
        }

        void ADeeperDigShowsTallerWallsSquare()
        {
            byte[] pristine = All(Cell.Floor), cells = All(Cell.Floor);
            var touched = new bool[W * H];
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                cells[I(5 + dx, 5 + dz)] = Cell.Void;
                touched[I(5 + dx, 5 + dz)] = true;
            }
            var m = new TerrainEditMesh();
            m.Build(W, H, cells, Heights(), pristine, touched, 0, 0, W, H);
            Print("3x3 dig: pit cells", m.PitCells, "== 9");
            Print("3x3 dig: centre floor below ground", G - m.MinFloorY, "== " + (TerrainEditMesh.PitDepth1 + TerrainEditMesh.PitDepthStep));
            Print("3x3 dig: rim floor below ground", G - m.MaxFloorY, "== " + TerrainEditMesh.PitDepth1);
            Print("3x3 dig: rim walls", m.PitWallFaces, "== 12");
            Print("3x3 dig: step walls down to the centre", m.StepWallFaces, "== 4");
            Assert.AreEqual(9, m.PitCells);
            Assert.AreEqual(G - TerrainEditMesh.PitDepth1 - TerrainEditMesh.PitDepthStep, m.MinFloorY, Eps, "the middle is deeper");
            Assert.AreEqual(G - TerrainEditMesh.PitDepth1, m.MaxFloorY, Eps);
            Assert.AreEqual(12, m.PitWallFaces, "rim walls only toward unbroken ground");
            Assert.AreEqual(4, m.StepWallFaces, "a rock step from each shallower neighbour down to the middle");
            var big = BigDig();
            var mb = new TerrainEditMesh();
            mb.Build(21, 21, big.cells, Heights(21 * 21), big.pristine, big.touched, 0, 0, 21, 21);
            Print("13x13 dig: deepest floor below ground", G - mb.MinFloorY, "== " + TerrainEditMesh.PitDepthMax);
            Assert.AreEqual(G - TerrainEditMesh.PitDepthMax, mb.MinFloorY, Eps, "a wide dig bottoms out at PitDepthMax");
        }

        static (byte[] cells, byte[] pristine, bool[] touched) BigDig()
        {
            int n = 21 * 21;
            byte[] p = All(Cell.Floor, n), c = All(Cell.Floor, n);
            var t = new bool[n];
            for (int z = 4; z <= 16; z++)
            for (int x = 4; x <= 16; x++) { c[z * 21 + x] = Cell.Void; t[z * 21 + x] = true; }
            return (c, p, t);
        }

        [Test]
        public void ADugCellCarriesNoDressingAndMakesNoNewIslandEdge()
        {
            WorldKitCatalog cat = WorldKitCatalog.Load();
            if (cat == null || cat.Names.Length == 0) Assert.Ignore("world kit not imported");
            MapDef map = SkybreakBasinMap.BuildSkybreakBasin(GameConfig.CreateDefault());
            byte[] pristine = (byte[])map.Cells.Clone();
            BasesFile bases = BasesFile.Load();
            WorldPlan before = WorldDresser.Plan(map, bases, cat.Centroid, cat.Body);
            // A dressed interior floor cell (a small piece's pivot on it) and an island-edge floor cell (void to its north).
            int inner = -1, edge = -1;
            foreach (WorldPlacement p in before.Placements)
            {
                if (TerrainEdits.KeptOnEdited(p.Cat) || p.Cat == "ground") continue;
                int cx = (int)Math.Floor(p.SimX), cz = (int)Math.Floor(p.SimZ);
                if (cx < 2 || cz < 2 || cx >= map.Width - 2 || cz >= map.Height - 2) continue;
                bool ring = true;
                for (int dz = -1; dz <= 1 && ring; dz++)
                for (int dx = -1; dx <= 1 && ring; dx++) ring = map.Cells[(cz + dz) * map.Width + cx + dx] == Cell.Floor;
                if (ring) { inner = cz * map.Width + cx; break; }
            }
            for (int cz = 2; cz < map.Height - 2 && edge < 0; cz++)
            for (int cx = 24; cx < map.Width - 24 && edge < 0; cx++)
            {
                int i = cz * map.Width + cx;
                if (map.Cells[i] == Cell.Floor && map.Cells[i - map.Width] == Cell.Void && map.Cells[i + map.Width] == Cell.Floor &&
                    Math.Abs(cx - map.Hill.X) > 14) edge = i;
            }
            Assert.GreaterOrEqual(inner, 0, "a dressed interior floor cell");
            Assert.GreaterOrEqual(edge, 0, "an island edge floor cell");
            var touched = new bool[map.Cells.Length];
            foreach (int i in new[] { inner, edge }) { map.Cells[i] = Cell.Void; touched[i] = true; }
            WorldPlan dug = WorldDresser.Plan(map.WithCells(TerrainEdits.PlanningCells(map.Cells, pristine, touched)), bases, cat.Centroid, cat.Body);
            int dropped = TerrainEdits.FilterPlan(dug, touched);
            Print("dressing pieces dropped from the two dug cells", dropped, ">= 1");
            foreach (int i in new[] { inner, edge })
            {
                int cx = i % map.Width, cz = i / map.Width;
                var on = dug.Placements.Where(p => !TerrainEdits.KeptOnEdited(p.Cat) && (int)Math.Floor(p.SimX) == cx && (int)Math.Floor(p.SimZ) == cz)
                    .Select(p => p.Piece).ToList();
                Print($"dressing left on dug cell ({cx}, {cz})", on.Count, "== 0");
                CollectionAssert.IsEmpty(on, $"dressing still drawn over dug cell ({cx}, {cz})");
                Assert.IsFalse(dug.Dressed[i], "the dug cell is the terrain-edit mesh's, not the dressed world's");
                string a = EdgesNear(before, cx, cz), b = EdgesNear(dug, cx, cz);
                Print($"edge/cliff pieces near ({cx}, {cz}) changed", a == b ? 0 : 1, "== 0 (no new rim or cliff into the sky)");
                Assert.AreEqual(a, b, $"the island edge moved around dug cell ({cx}, {cz})");
            }
            Assert.GreaterOrEqual(dropped, 1);
        }

        static string EdgesNear(WorldPlan plan, int cx, int cz) => string.Join(";", plan.Placements
            .Where(p => (p.Cat == "edge" || p.Cat == "cliff") && Math.Abs(p.SimX - (cx + 0.5)) < 2.5 && Math.Abs(p.SimZ - (cz + 0.5)) < 2.5)
            .Select(p => $"{p.Piece}@{p.SimX:0.00},{p.SimZ:0.00}").OrderBy(x => x));

        [Test]
        public void APlacedCellIsABlockWithSidesAndABottom()
        {
            byte[] pristine = All(Cell.Floor), cells;
            for (int z = 0; z < H; z++) for (int x = 6; x < W; x++) pristine[I(x, z)] = Cell.Void;   // island edge at x = 6
            cells = (byte[])pristine.Clone();
            cells[I(6, 5)] = Cell.Floor;
            var touched = new bool[W * H];
            touched[I(6, 5)] = true;
            Assert.AreEqual(TerrainEdits.Kind.Placed, TerrainEdits.Classify(Cell.Void, Cell.Floor, true));
            var m = new TerrainEditMesh();
            m.Build(W, H, cells, Heights(), pristine, touched, 0, 0, W, H);
            Print("placed: block cells", m.BlockCells, "== 1");
            Print("placed: side faces", m.BlockSideFaces, "== 3 (east, north, south face the void; west is the island)");
            Print("placed: bottom faces", m.BlockBottomFaces, "== 1");
            Assert.AreEqual(1, m.BlockCells);
            Assert.AreEqual(3, m.BlockSideFaces);
            Assert.AreEqual(1, m.BlockBottomFaces);
            AssertInsideCell(m, 6, 5, G - TerrainEditMesh.PlacedDepth - TerrainEditMesh.UnderDepth, G, "placed block (body and its tapered rock keel)");
            Assert.AreEqual((float)G, m.Verts.Max(v => v.y), Eps, "top exactly at the cell's ground height (where the sim stands actors)");

            // Two placed side by side: no face between them, a bevel seam on both.
            cells[I(6, 6)] = Cell.Floor;
            touched[I(6, 6)] = true;
            m.Build(W, H, cells, Heights(), pristine, touched, 0, 0, W, H);
            Print("two placed: side faces", m.BlockSideFaces, "== 4");
            Assert.AreEqual(4, m.BlockSideFaces, "the face between two placed blocks is left out");
            Assert.IsTrue(HasVert(m, 6, G - TerrainEditMesh.EarthBevel, 6) && HasVert(m, 7, G - TerrainEditMesh.EarthBevel, 6), "both bevels meet at the shared edge: a seam, no gap");
        }

        [Test]
        public void AFilledPitIsAFlushBlock()
        {
            byte[] pristine = All(Cell.Floor), cells = All(Cell.Floor);
            var touched = new bool[W * H];
            touched[I(5, 5)] = true;   // dug, then filled back
            Assert.AreEqual(TerrainEdits.Kind.Filled, TerrainEdits.Classify(Cell.Floor, Cell.Floor, true));
            var m = new TerrainEditMesh();
            m.Build(W, H, cells, Heights(), pristine, touched, 0, 0, W, H);
            Print("filled: block cells / sides / bottoms", m.BlockCells * 100 + m.BlockSideFaces * 10 + m.BlockBottomFaces, "== 100");
            Assert.AreEqual(1, m.BlockCells);
            Assert.AreEqual(0, m.BlockSideFaces, "surrounded by ground: no side shows");
            Assert.AreEqual(0, m.BlockBottomFaces);
            Assert.AreEqual((float)G, m.Verts.Max(v => v.y), Eps, "flush with the ground");
            AssertInsideCell(m, 5, 5, G - TerrainEditMesh.EarthBevel, G, "filled block");
        }

        [Test]
        public void ShortWallBlocksSitOnTheirCellAndARowIsFlush()
        {
            Assert.AreEqual(WallView.ShortTop, TerrainEditMesh.ShortTop, "block top == the ghost's short height == WallView's cap");
            byte[] cells = All(Cell.Floor);
            cells[I(3, 4)] = cells[I(4, 4)] = cells[I(5, 4)] = Cell.LowWall;
            var m = new TerrainEditMesh();
            for (int x = 3; x <= 5; x++)
            {
                m.Build(W, H, cells, Heights(), null, null, x, 4, x + 1, 5);
                AssertInsideCell(m, x, 4, G - TerrainEditMesh.ShortSink, G + TerrainEditMesh.ShortTop, "short wall block " + x);
                Assert.AreEqual((float)(G + TerrainEditMesh.ShortTop), m.Verts.Max(v => v.y), Eps, "top on the grid height");
                Assert.AreEqual((float)(G - TerrainEditMesh.ShortSink), m.Verts.Min(v => v.y), Eps, "footing just under the ground");
            }
            m.Build(W, H, cells, Heights(), null, null, 0, 0, W, H);
            Print("row of 3 short blocks: cells", m.ShortWallCells, "== 3");
            Print("row of 3 short blocks: side faces", m.ShortWallSideFaces, "== 8 (12 less the 4 faces between blocks)");
            Assert.AreEqual(3, m.ShortWallCells);
            Assert.AreEqual(8, m.ShortWallSideFaces, "faces between two blocks never show and are left out (no z-fighting)");
            float top = (float)(G + TerrainEditMesh.ShortTop - TerrainEditMesh.StoneBevel);
            foreach (int x in new[] { 4, 5 })
                Assert.IsTrue(HasVert(m, x, top, 4) && HasVert(m, x, top, 5), $"bevels of both blocks meet at x = {x}: a seam, no gap, no overhang");
            float minX = m.Verts.Min(v => v.x), maxX = m.Verts.Max(v => v.x);
            Print("row extent in x", maxX - minX, "== 3");
            Assert.AreEqual(3f, minX, Eps);
            Assert.AreEqual(6f, maxX, Eps);
        }

        static void AssertInsideCell(TerrainEditMesh m, int cx, int cz, double yMin, double yMax, string what)
        {
            Assert.Greater(m.Verts.Count, 0, what + " drew nothing");
            foreach (Vector3 v in m.Verts)
            {
                Assert.That(v.x >= cx - Eps && v.x <= cx + 1 + Eps && -v.z >= cz - Eps && -v.z <= cz + 1 + Eps, $"{what}: vertex {v} outside cell ({cx}, {cz})");
                Assert.That(v.y >= yMin - Eps && v.y <= yMax + Eps, $"{what}: vertex {v} outside heights {yMin}..{yMax}");
            }
        }

        [Test]
        public void AChunkRebuildAllocatesNothingAndStaysFast()
        {
            const int N = 48;
            byte[] pristine = All(Cell.Floor, N * N), cells = All(Cell.Floor, N * N);
            var touched = new bool[N * N];
            for (int z = 0; z < N; z++) for (int x = 36; x < N; x++) pristine[z * N + x] = cells[z * N + x] = Cell.Void;
            int pits = 0, placed = 0, walls = 0;
            for (int z = 16; z < 32; z++)
            for (int x = 16; x < 32; x++)
            {
                int i = z * N + x;
                if ((x + 2 * z) % 5 == 0 || (x >= 20 && x <= 23 && z >= 20 && z <= 23)) { cells[i] = Cell.Void; touched[i] = true; pits++; }
                else if ((x * 7 + z) % 11 == 0) { cells[i] = Cell.LowWall; walls++; }
            }
            for (int z = 16; z < 32; z += 2) { cells[z * N + 36] = Cell.Floor; touched[z * N + 36] = true; placed++; }
            double[] h = Heights(N * N);
            var m = new TerrainEditMesh();
            var mesh = new Mesh();
            for (int k = 0; k < 3; k++) { m.Build(N, N, cells, h, pristine, touched, 16, 16, 32, 32); m.Upload(mesh); m.Build(N, N, cells, h, pristine, touched, 32, 16, 48, 32); }
            const int Runs = 50;
            var sw = Stopwatch.StartNew();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < Runs; k++) m.Build(N, N, cells, h, pristine, touched, 16, 16, 32, 32);
            long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
            double ms = sw.Elapsed.TotalMilliseconds / Runs;
            long beforeUp = GC.GetAllocatedBytesForCurrentThread();
            m.Upload(mesh);
            long allocUp = GC.GetAllocatedBytesForCurrentThread() - beforeUp;
            int worstTris = m.Tris.Count / 3;

            // The usual case: one chunk holding a 3 x 3 dig, one fill and a row of three BLOCK pieces.
            byte[] c2 = All(Cell.Floor, N * N);
            var t2 = new bool[N * N];
            for (int z = 20; z <= 22; z++) for (int x = 20; x <= 22; x++) { c2[z * N + x] = Cell.Void; t2[z * N + x] = true; }
            c2[21 * N + 21] = Cell.Floor;
            c2[25 * N + 20] = c2[25 * N + 21] = c2[25 * N + 22] = Cell.LowWall;
            byte[] p2 = All(Cell.Floor, N * N);
            for (int k = 0; k < 3; k++) m.Build(N, N, c2, h, p2, t2, 16, 16, 32, 32);
            sw.Restart();
            long before2 = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < Runs; k++) m.Build(N, N, c2, h, p2, t2, 16, 16, 32, 32);
            long alloc2 = GC.GetAllocatedBytesForCurrentThread() - before2;
            double ms2 = sw.Elapsed.TotalMilliseconds / Runs;

            Print($"worst chunk ({pits} pits of 256 cells, {walls} short walls): managed bytes over {Runs} rebuilds", alloc, "== 0");
            Print("worst chunk rebuild mean ms (CI editor, managed build only)", ms, "< 5.0 (15% of a 33.3 ms frame)");
            Print("usual chunk (3x3 dig, a fill, 3 blocks): managed bytes over " + Runs + " rebuilds", alloc2, "== 0");
            Print("usual chunk rebuild mean ms", ms2, "< 1.0");
            Print("mesh upload managed bytes (printed, not asserted)", allocUp, "info");
            Print("worst chunk triangles", worstTris, "info (draw calls added: one per chunk with edits)");
            Print("usual chunk triangles", m.Tris.Count / 3, "info");
            Print($"placed cells in the neighbour chunk", placed, "info");
            UnityEngine.Object.DestroyImmediate(mesh);
            Assert.AreEqual(0, alloc, "terrain edit rebuild allocates (worst chunk)");
            Assert.AreEqual(0, alloc2, "terrain edit rebuild allocates (usual chunk)");
            Assert.Less(ms, 5.0, "worst-case terrain edit rebuild cost");
            Assert.Less(ms2, 1.0, "usual terrain edit rebuild cost");
        }
    }
}
