using System.Collections.Generic;
using System.Linq;
using CriticalMass.Runtime.Audio.Sound;
using CriticalMass.Runtime.View.Affordance;
using CriticalMass.Runtime.View.WallDamage;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>wall-damage-1: damage stages, per-cell wall looks, rubble life, material sounds, and the Sim left untouched.</summary>
    public class WallDamageTests
    {
        static World NewWorld()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            return World.CreateWorld(new WorldOptions { Seed = cfg.Seed, TeamSize = cfg.TeamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Miner, Config = cfg });
        }

        // ------------------------------------------------------------------ stages

        [Test]
        public void StagesFollowHealthLeft()
        {
            Assert.AreEqual(0, DamageLook.Stage(1.0), "full = intact");
            Assert.AreEqual(1, DamageLook.Stage(0.8), "cracked");
            Assert.AreEqual(2, DamageLook.Stage(0.5), "heavily damaged");
            Assert.AreEqual(3, DamageLook.Stage(0.2), "nearly broken");
            Assert.AreEqual(3, DamageLook.Stage(0.01));
            Assert.AreEqual(0, DamageLook.Stage(-1), "inactive = no damage look");
            Assert.AreEqual(0f, DamageLook.LeanDeg(0));
            Assert.Greater(DamageLook.LeanDeg(3), DamageLook.LeanDeg(2), "only the last stage leans clearly");
            Assert.AreEqual(0, DamageLook.CrackLines(0));
            Assert.Less(DamageLook.CrackLines(1), DamageLook.CrackLines(2));
            Assert.Less(DamageLook.CrackLines(2), DamageLook.CrackLines(3));
        }

        [Test]
        public void PiecePosesAreSeededAndGrowWithTheStage()
        {
            for (int k = 0; k < 8; k++)
            {
                DamageLook.PiecePose(77, k, 0, out Vector3 t0, out Vector3 s0, out bool m0);
                Assert.AreEqual(Vector3.zero, t0, "stage 0 = the authored pose");
                Assert.AreEqual(Vector3.zero, s0);
                Assert.IsFalse(m0);
                DamageLook.PiecePose(77, k, 2, out Vector3 a, out Vector3 sa, out bool ma);
                DamageLook.PiecePose(77, k, 2, out Vector3 b, out Vector3 sb, out bool mb);
                Assert.AreEqual(a, b, "same seed, same pose");
                Assert.AreEqual(ma, mb);
                DamageLook.PiecePose(77, k, 3, out _, out _, out bool m3);
                if (ma) Assert.IsTrue(m3, "a piece knocked out at stage 2 stays out at stage 3");
            }
            DamageLook.PiecePose(77, 0, 3, out _, out _, out bool first);
            Assert.IsFalse(first, "the first piece always stands until the break");
        }

        [Test]
        public void ShakeLastsAboutAnEighthOfASecond()
        {
            Assert.AreEqual(0.12f, DamageLook.ShakeSeconds, 1e-6f);
            Assert.AreNotEqual(0f, DamageLook.Shake(0.02f, 1f));
            Assert.AreEqual(0f, DamageLook.Shake(0.12f, 1f));
            Assert.AreEqual(0f, DamageLook.Shake(0.5f, 1f));
            Assert.AreEqual(0f, DamageLook.Shake(-0.01f, 1f));
        }

        // ------------------------------------------------------------------ per-cell walls

        [Test]
        public void EachCellLooksOnlyAtItsOwnType()
        {
            Assert.AreEqual(CellLook.Intact, DamageLook.LookOf(Cell.BreakableIntact));
            Assert.AreEqual(CellLook.Cracked, DamageLook.LookOf(Cell.BreakableCracked));
            Assert.AreEqual(CellLook.Rubble, DamageLook.LookOf(Cell.BreakableDestroyed));
            Assert.AreEqual(CellLook.None, DamageLook.LookOf(Cell.Floor));
            // The grey-box view never draws a bare box or floor tile under a broken wall cell's art.
            Assert.IsTrue(DamageLook.DressedStillCovers(Cell.BreakableIntact, Cell.BreakableCracked));
            Assert.IsTrue(DamageLook.DressedStillCovers(Cell.BreakableIntact, Cell.BreakableDestroyed));
            Assert.IsTrue(DamageLook.DressedStillCovers(Cell.Wall, Cell.Wall));
            Assert.IsFalse(DamageLook.DressedStillCovers(Cell.Floor, Cell.Void), "a dug cell goes back to the grey-box view");
            Assert.IsFalse(DamageLook.DressedStillCovers(Cell.BreakableDestroyed, Cell.Void));
        }

        /// <summary>Three adjacent breakable cells of the real map, a synthetic readable section mesh over them.</summary>
        static bool ThreeInARow(MapDef m, out List<int> cells)
        {
            cells = new List<int>();
            for (int i = 1; i < m.Cells.Length - 1; i++)
            {
                int cx = i % m.Width;
                if (cx == 0 || cx == m.Width - 1) continue;
                if (m.Cells[i - 1] == Cell.BreakableIntact && m.Cells[i] == Cell.BreakableIntact && m.Cells[i + 1] == Cell.BreakableIntact)
                {
                    cells.Add(i - 1); cells.Add(i); cells.Add(i + 1);
                    return true;
                }
            }
            return false;
        }

        static Mesh Boxes(MapDef m, List<int> cells)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            foreach (int i in cells)
            {
                Vector3 c = SimSpace.ToWorld(i % m.Width + 0.5, 0.75, i / m.Width + 0.5);
                int b = v.Count;
                for (int k = 0; k < 8; k++) v.Add(c + new Vector3((k & 1) == 0 ? -0.45f : 0.45f, (k & 2) == 0 ? -0.7f : 0.7f, (k & 4) == 0 ? -0.45f : 0.45f));
                int[] f = { 0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4, 2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5 };
                foreach (int q in f) t.Add(b + q);
            }
            var mesh = new Mesh();
            mesh.SetVertices(v);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        [Test]
        public void OneBrokenCellDoesNotChangeItsNeighboursLook()
        {
            World w = NewWorld();
            MapDef m = w.Map;
            if (!ThreeInARow(m, out List<int> cells)) Assert.Ignore("no three breakable cells in a row on this map");
            var section = new GameObject("test section");
            try
            {
                Mesh mesh = Boxes(m, cells);
                BreakableCellSet.ClearAll();
                BreakableCellSet set = BreakableCellSet.TryBuild(cells, m.Width, mesh, mesh, mesh, section.transform, m.Cells.Length);
                Assert.IsNotNull(set, "a readable section is cut per cell");
                var mats = new Material[] { null };
                set.Sync(w, mats, true);
                foreach (int i in cells) Assert.AreEqual(CellLook.Intact, BreakableCellSet.Shown(i));
                // Break the middle cell only.
                m.Cells[cells[1]] = Cell.BreakableDestroyed;
                set.Sync(w, mats, false);
                Assert.AreEqual(CellLook.Rubble, BreakableCellSet.Shown(cells[1]), "the hit cell shows rubble");
                Assert.AreEqual(CellLook.Intact, BreakableCellSet.Shown(cells[0]), "left neighbour keeps its own look");
                Assert.AreEqual(CellLook.Intact, BreakableCellSet.Shown(cells[2]), "right neighbour keeps its own look");
                // Crack the left one: the right one still intact.
                m.Cells[cells[0]] = Cell.BreakableCracked;
                set.Sync(w, mats, false);
                Assert.AreEqual(CellLook.Cracked, BreakableCellSet.Shown(cells[0]));
                Assert.AreEqual(CellLook.Intact, BreakableCellSet.Shown(cells[2]));
                // Each cell got its own slice of the section (one box each).
                int drawn = section.GetComponentsInChildren<MeshFilter>(true).Count(f => f.name.StartsWith("Cell") && f.sharedMesh != null);
                Assert.AreEqual(3, drawn);
            }
            finally
            {
                BreakableCellSet.ClearAll();
                Object.DestroyImmediate(section);
                var pool = GameObject.Find("CM Rubble");
                if (pool != null) Object.DestroyImmediate(pool);
            }
        }

        [Test]
        public void ABrokenBlockFallsApartIntoSeveralChunksAndACrackedOneIsTinted()
        {
            World w = NewWorld();
            var cells = new List<int> { 0 };
            Mesh box = Boxes(w.Map, cells);
            MeshPiece[] chunks = MeshPieces.TryGrid(box, 2, 5);
            Assert.IsNotNull(chunks);
            Assert.Greater(chunks.Length, 1, "not one slab");
            Assert.LessOrEqual(chunks.Length, 8);
            Mesh tint = MeshPieces.TintShell(box, BreakableCellSet.CrackedTint, 0.012f);
            Assert.IsNotNull(tint);
            Assert.AreEqual(box.vertexCount, tint.vertexCount);
            Assert.Greater(box.bounds.size.x, 0f);
            Assert.Greater(tint.bounds.size.x, box.bounds.size.x, "the shell sits just outside the block");
            foreach (MeshPiece c in chunks) { Object.DestroyImmediate(c.Mesh); if (c.Cracks != null) foreach (Mesh m in c.Cracks) if (m != null) Object.DestroyImmediate(m); }
            Object.DestroyImmediate(tint);
            Object.DestroyImmediate(box);
        }

        // ------------------------------------------------------------------ rubble

        [Test]
        public void RubbleStaysTwentySecondsThenSinksOut()
        {
            Assert.AreEqual(20f, DamageLook.RubbleSeconds);
            Assert.AreEqual(60, DamageLook.MaxRubble, "art direction cap");
            Assert.IsTrue(DamageLook.RubbleAlive(0f));
            Assert.IsTrue(DamageLook.RubbleAlive(19.9f));
            Assert.IsFalse(DamageLook.RubbleAlive(20f));
            Assert.AreEqual(0f, DamageLook.RubbleSink(10f), "resting, not fading");
            Assert.AreEqual(0f, DamageLook.RubbleSink(20f - DamageLook.FadeSeconds));
            Assert.Greater(DamageLook.RubbleSink(19.5f), 0f);
            Assert.Less(DamageLook.RubbleSink(19.5f), 1f);
        }

        [Test]
        public void RubblePoolNeverPassesItsCap()
        {
            var start = new double[DamageLook.MaxRubble];
            for (int i = 0; i < start.Length; i++) start[i] = double.NegativeInfinity;
            for (int n = 0; n < DamageLook.MaxRubble; n++)
            {
                int s = PoolPick.Pick(start, 100 + n * 0.01, DamageLook.RubbleSeconds);
                Assert.IsTrue(double.IsNegativeInfinity(start[s]), "free slots first");
                start[s] = 100 + n * 0.01;
            }
            int reuse = PoolPick.Pick(start, 101, DamageLook.RubbleSeconds);
            Assert.AreEqual(100.0, start[reuse], 1e-9, "full: the oldest piece is reused");
            int expired = PoolPick.Pick(start, 130, DamageLook.RubbleSeconds);
            Assert.GreaterOrEqual(130 - start[expired], DamageLook.RubbleSeconds, "an expired slot is free again");
        }

        // ------------------------------------------------------------------ sound

        [Test]
        public void SoundsMatchTheMaterial()
        {
            Assert.AreEqual(DamageMaterial.Wood, DamageLook.MaterialOf(ReactKind.Barricade));
            Assert.AreEqual(DamageMaterial.Stone, DamageLook.MaterialOf(ReactKind.Wall));
            string[] ids = { DamageLook.CueFor(DamageMaterial.Wood, false), DamageLook.CueFor(DamageMaterial.Wood, true), DamageLook.CueFor(DamageMaterial.Stone, false), DamageLook.CueFor(DamageMaterial.Stone, true) };
            foreach (string id in ids) Assert.IsTrue(SoundTable.Cues.Any(c => c.Id == id), id + " is in the sound table");
            foreach (string id in new[] { ids[2], ids[3] })
            {
                CueDef c = SoundTable.Cues.First(x => x.Id == id);
                foreach (string f in c.Files ?? new string[0])
                    Assert.IsFalse(f.Contains("Wood") || f.Contains("Plank"), id + " (stone) must not play a wood sample: " + f);
            }
            foreach (string id in new[] { ids[0], ids[1] })
            {
                CueDef c = SoundTable.Cues.First(x => x.Id == id);
                Assert.IsTrue((c.Files ?? new string[0]).All(f => f.Contains("Wood") || f.Contains("Plank")), id + " (wood) plays wood samples");
            }
        }

        // ------------------------------------------------------------------ the Sim is untouched

        static ulong Hash(World w)
        {
            unchecked
            {
                ulong h = 1469598103934665603UL;
                void Mix(ulong x) { h ^= x; h *= 1099511628211UL; }
                for (int i = 0; i < w.ActorCount; i++) { Mix((ulong)System.BitConverter.DoubleToInt64Bits(w.X[i])); Mix((ulong)System.BitConverter.DoubleToInt64Bits(w.Z[i])); }
                foreach (byte c in w.Map.Cells) Mix(c);
                foreach (byte c in w.Map.WallHp) Mix(c);
                if (w.Barricades != null) foreach (Barricade b in w.Barricades) if (b != null) Mix((ulong)System.BitConverter.DoubleToInt64Bits(b.Hp));
                if (w.Kaiju != null) Mix((ulong)System.BitConverter.DoubleToInt64Bits(w.Kaiju.X));
                Mix((ulong)w.Tick);
                return h;
            }
        }

        [Test]
        public void TheDamageViewNeverChangesTheSimChecksum()
        {
            // No rule changed in wall-damage-1: the same seed gives the same state with or without the view reading it,
            // including a Kaiju slam next to the walls (impact only).
            World a = NewWorld(), b = NewWorld();
            var rx = new WorldReactions();
            var fa = new InputFrame();
            var fb = new InputFrame();
            for (int t = 0; t < 600; t++)
            {
                if (t == 300)
                {
                    World.PushEvent(a, SimEventType.KaijuSlam, -1, 0, a.Map.Width * 0.5, a.Map.Height * 0.5, 5.0);
                    World.PushEvent(b, SimEventType.KaijuSlam, -1, 0, b.Map.Width * 0.5, b.Map.Height * 0.5, 5.0);
                }
                MatchTick.AdvanceNoHuman(a, fa);
                MatchTick.AdvanceNoHuman(b, fb);
                rx.Read(b, t / 30.0);
            }
            Assert.AreEqual(Hash(a), Hash(b));
        }
    }
}
