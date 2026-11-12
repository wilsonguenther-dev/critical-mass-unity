using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CriticalMass.Runtime.View.Dressing;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// World art (2026-11-12): the environment kit v3 and bases kit v1 import contract, the world dresser's determinism and
    /// its walkable-cell rule against the Sim map, and the dressed world's triangle count.
    /// </summary>
    public sealed class WorldArtTests
    {
        static IEnumerable<string> KitPaths() =>
            AssetDatabase.FindAssets("t:Model", new[] { "Assets/_Game/Art/Environment", "Assets/_Game/Art/Bases" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => WorldKit.IsEnvironment(p) || WorldKit.IsBases(p))
                .OrderBy(p => p, StringComparer.Ordinal);

        static WorldKitCatalog Cat => WorldKitCatalog.Load();

        static MapDef Map() => SkybreakBasinMap.BuildSkybreakBasin(GameConfig.CreateDefault());

        [Test]
        public void BothKitsAreImported()
        {
            string[] paths = KitPaths().ToArray();
            Assert.GreaterOrEqual(paths.Count(WorldKit.IsEnvironment), 75, "environment kit v3 FBX (tools/art-sync.sh)");
            Assert.GreaterOrEqual(paths.Count(WorldKit.IsBases), 42, "bases kit v1 FBX (tools/art-sync.sh)");
            WorldKitCatalog cat = WorldKitCatalog.Load();
            Assert.IsNotNull(cat, WorldKitCatalog.AssetPath);
            Assert.AreEqual(paths.Length, cat.Names.Length, "catalog lists every kit model");
            CollectionAssert.IsOrdered(cat.Names, StringComparer.Ordinal, "catalog sorted (binary search)");
        }

        [TestCaseSource(nameof(KitPaths))]
        public void KitFbxImportsToContract(string path)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(path);
            Assert.IsNotNull(mi, path);
            Assert.AreEqual(1f, mi.globalScale, "scale factor");
            Assert.IsTrue(mi.useFileUnits, "convert units");
            Assert.IsTrue(mi.bakeAxisConversion, "bake axis conversion");
            Assert.IsFalse(mi.importCameras, "cameras");
            Assert.IsFalse(mi.importLights, "lights");
            Assert.IsFalse(mi.addCollider, "colliders");
            Assert.IsTrue(mi.isReadable, "read/write (the world view combines the meshes)");
            Assert.AreEqual(ModelImporterNormals.Import, mi.importNormals, "normals");
            Assert.AreEqual(ModelImporterAnimationType.None, mi.animationType, "static: no rig");
            Assert.AreEqual(WorldKit.IsEnvironment(path) ? ModelImporterMaterialImportMode.ImportViaMaterialDescription : ModelImporterMaterialImportMode.None,
                mi.materialImportMode, "material slots (environment keeps slot names)");

            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(go, path + " has no model prefab");
            MeshFilter[] mfs = go.GetComponentsInChildren<MeshFilter>(true);
            Assert.IsNotEmpty(mfs, "meshes");
            var bounds = new Bounds();
            bool any = false;
            foreach (MeshFilter mf in mfs)
            {
                Mesh mesh = mf.sharedMesh;
                Assert.IsNotNull(mesh, mf.name + " mesh");
                Assert.IsTrue(mesh.HasVertexAttribute(VertexAttribute.Color), mf.name + " vertex colour (Col)");
                Assert.IsTrue(mesh.HasVertexAttribute(VertexAttribute.Normal), mf.name + " normals");
                Assert.IsTrue(mesh.HasVertexAttribute(VertexAttribute.TexCoord3), mf.name + " smoothed outline direction (UV3)");
                var r = mf.GetComponent<MeshRenderer>();
                Assert.IsNotNull(r, mf.name + " renderer");
                Assert.AreEqual(mesh.subMeshCount, r.sharedMaterials.Length, mf.name + " one material per slot");
                foreach (Material m in r.sharedMaterials)
                {
                    Assert.IsNotNull(m, mf.name + " material");
                    Assert.IsTrue(WorldKit.AllLooks.Any(l => AssetDatabase.GetAssetPath(m) == WorldKit.AssetPath(l)),
                        mf.name + " material " + AssetDatabase.GetAssetPath(m) + " is not a world look material");
                }
                Bounds b = mesh.bounds;
                if (!any) { bounds = b; any = true; } else bounds.Encapsulate(b);
            }
            // Sanity of scale and axes: no kit piece is over 70 u or under 1 mm, and nothing is lying on its back.
            bool big = path.Contains("cm_layout_") || path.EndsWith("env_cloud_floor.fbx", StringComparison.Ordinal);
            Assert.Less(bounds.size.magnitude, big ? 200f : 70f, "size (100x scale?)");
            Assert.Greater(bounds.size.magnitude, 0.001f, "size");
            string piece = Path.GetFileNameWithoutExtension(path);
            if (piece == "env_tree_tall_l") Assert.AreEqual(4.13f, bounds.max.y, 0.05f, "tall tree top (manifest 4.13 u, Y up)");
            if (piece == "env_waterfall")
            {
                var uv2 = new List<Vector3>();
                mfs[0].sharedMesh.GetUVs(2, uv2);
                Assert.Greater(uv2.Count(v => v.sqrMagnitude > 0.25f), 10, "foam band extrusion directions (UV2)");
            }
        }

        [Test]
        public void DresserIsDeterministic()
        {
            BasesFile bases = BasesFile.Load();
            Assert.IsNotNull(bases, "Resources/" + BasesFile.ResourcePath);
            WorldPlan a = WorldDresser.Plan(Map(), bases, Cat.Centroid, Cat.Body), b = WorldDresser.Plan(Map(), BasesFile.Load(), Cat.Centroid, Cat.Body);
            Assert.AreEqual(a.Placements.Count, b.Placements.Count, "placement count");
            Assert.AreEqual(a.Hash(), b.Hash(), "plan fingerprint");
            CollectionAssert.AreEqual(a.Dressed, b.Dressed, "covered cells");
            Color[] fa = WorldField.Compute(a, out int c1, out int r1), fb = WorldField.Compute(b, out _, out _);
            Assert.AreEqual(112 * 4, c1, "field 4 texels per unit");
            Assert.AreEqual(72 * 4, r1);
            CollectionAssert.AreEqual(fa, fb, "field texture");
            Debug.Log("[WorldArtTests] plan " + a.Hash() + ": " + a.Placements.Count + " placements, " + a.Rim.Count + " rim edges, " +
                      a.Ledge.Count + " ledge edges, " + a.WaterfallCount + " waterfalls, " + a.SpanCount + " spans, " + a.Breakables.Count + " breakables");
        }

        [Test]
        public void CommittedPlanSummaryIsCurrent()
        {
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "tools/art/world-plan.tsv");
            Assert.IsTrue(File.Exists(path), path + " (run -executeMethod CriticalMass.Editor.Dressing.WorldBake.Bake)");
            Assert.AreEqual(File.ReadAllText(path), WorldDresser.Plan(Map(), BasesFile.Load(), Cat.Centroid, Cat.Body).Summary(),
                "tools/art/world-plan.tsv is stale: the map, a kit or the rules changed; re-bake and review the diff");
        }

        [Test]
        public void EveryPlacedPieceIsInTheCatalog()
        {
            WorldKitCatalog cat = WorldKitCatalog.Load();
            var missing = WorldDresser.Plan(Map(), BasesFile.Load(), Cat.Centroid, Cat.Body).Counts().Keys.Where(p => cat.Find(p) == null).ToArray();
            CollectionAssert.IsEmpty(missing, "pieces the dresser places but the kit does not have");
        }

        /// <summary>Height of a kit model above its pivot (max y of its meshes in model space), u.</summary>
        static float TopOf(GameObject model)
        {
            float top = float.NegativeInfinity;
            foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                Matrix4x4 m = model.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                Bounds b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    top = Mathf.Max(top, m.MultiplyPoint3x4(c).y);
                }
            }
            return top;
        }

        /// <summary>
        /// The kit rule (README v3 "Decor"): nothing taller than 0.3 u stands with its centre on a walkable Sim cell. Checked
        /// for every dresser placement (except the walkable surfaces themselves: ground tiles, fills and bridge decks) and
        /// for every piece in the bases layouts, against the Sim's own MapDef.
        /// </summary>
        [Test]
        public void NothingTallStandsOnAWalkableCell()
        {
            MapDef map = Map();
            WorldKitCatalog cat = WorldKitCatalog.Load();
            var tops = new Dictionary<string, float>();
            float Top(string piece)
            {
                if (tops.TryGetValue(piece, out float t)) return t;
                GameObject model = cat.Find(piece);
                return tops[piece] = model == null ? 0f : TopOf(model);
            }
            var bad = new List<string>();
            int checkedPieces = 0;
            WorldPlan plan = WorldDresser.Plan(map, BasesFile.Load(), Cat.Centroid, Cat.Body);
            foreach (WorldPlacement p in plan.Placements)
            {
                if (p.Surface || p.Cat == "sky" || p.Cat == "base") continue; // bases: footprint rule below
                float h = Top(p.Piece) * p.Matrix.lossyScale.y;
                if (h <= 0.3f) continue;
                checkedPieces++;
                int cx = (int)Math.Floor(p.SimX), cz = (int)Math.Floor(p.SimZ);
                if (MapGrid.IsWalkable(map, cx, cz)) bad.Add($"{p.Piece} ({h:0.00} u) at sim ({p.SimX:0.00}, {p.SimZ:0.00}) cell {cx},{cz}");
            }
            // Bases pieces pivot on their site (the hill arcs on the ring centre), so their rule is checked on the mesh: no
            // vertex 0.3 to 2 u above the base more than 0.4 u inside walkable space (WorldDresser.Intrudes, the bases kit's
            // own footprint measure in bases-v1 checks.json). Pieces that break it are left out by the dresser and listed.
            foreach (WorldPlacement p in plan.Placements)
            {
                if (p.Cat != "base" || p.Piece.StartsWith("cm_layout_", StringComparison.Ordinal) || Top(p.Piece) <= 0.3f) continue;
                checkedPieces++;
                if (WorldDresser.Intrudes(map, p.Matrix, cat.Body(p.Piece))) bad.Add($"{p.Piece} at sim ({p.SimX:0.00}, {p.SimZ:0.00})");
            }
            Debug.Log($"[WorldArtTests] bases pieces left out by the walkable rule: {plan.DroppedBases.Count}: " + string.Join("; ", plan.DroppedBases));
            Debug.Log($"[WorldArtTests] walkable-cell rule: {checkedPieces} pieces over 0.3 u checked, {bad.Count} on walkable cells");
            CollectionAssert.IsEmpty(bad, "tall pieces on walkable cells:\n" + string.Join("\n", bad.Take(20)));
        }

        [Test]
        public void BasesKitCoversEverySimWallCell()
        {
            MapDef map = Map();
            WorldPlan plan = WorldDresser.Plan(map, BasesFile.Load(), Cat.Centroid, Cat.Body);
            int walls = 0, covered = 0, breakable = 0, breakCovered = 0;
            for (int i = 0; i < map.Cells.Length; i++)
            {
                if (map.Cells[i] == Cell.Wall) { walls++; if (plan.Dressed[i]) covered++; }
                if (map.Cells[i] == Cell.BreakableIntact) { breakable++; if (plan.Dressed[i]) breakCovered++; }
            }
            Debug.Log("[WorldArtTests] bases notes: " + string.Join("; ", plan.Notes));
            Debug.Log($"[WorldArtTests] wall cells {covered}/{walls} covered by the bases kit, breakable cells {breakCovered}/{breakable}");
            Assert.AreEqual(walls, covered, "every grey cube wall cell is drawn by a bases piece");
            var loose = Enumerable.Range(0, map.Cells.Length).Where(i => map.Cells[i] == Cell.BreakableIntact && !plan.Dressed[i])
                .Select(i => (i % map.Width) + "," + (i / map.Width)).ToArray();
            var centres = plan.Breakables.Select(b => b.Family + " centroid sim " + b.CentreX.ToString("0.0") + "," + b.CentreZ.ToString("0.0") + " cells " + b.Cells.Count).ToArray();
            Assert.AreEqual(breakable, breakCovered, "every breakable cell belongs to a bases breakable piece; loose " +
                string.Join(" ", loose) + "; pieces " + string.Join("; ", centres));
            for (int i = 0; i < map.Cells.Length; i++)
                if (MapGrid.IsWalkableType(map.Cells[i]) && map.Cells[i] != Cell.BreakableDestroyed)
                    Assert.IsTrue(plan.Dressed[i], "walkable cell " + i + " is not dressed");
        }

        [Test]
        public void DressedWorldTriangleCount()
        {
            WorldKitCatalog cat = WorldKitCatalog.Load();
            WorldPlan plan = WorldDresser.Plan(Map(), BasesFile.Load(), Cat.Centroid, Cat.Body);
            long tris = 0;
            var per = new Dictionary<string, long>();
            foreach (WorldPlacement p in plan.Placements)
            {
                GameObject model = cat.Find(p.Piece);
                if (model == null) continue;
                bool layout = p.Piece.StartsWith("cm_layout_", StringComparison.Ordinal);
                long t = model.GetComponentsInChildren<MeshFilter>(true).Where(mf => !layout || mf.name.Contains("cm_district_"))
                    .Sum(mf => (long)mf.sharedMesh.triangles.Length / 3);
                tris += t;
                string k = p.Cat ?? "?";
                per[k] = per.TryGetValue(k, out long n) ? n + t : t;
            }
            Debug.Log($"[WorldArtTests] dressed world: {tris} triangles (" + string.Join(", ", per.OrderBy(kv => kv.Key).Select(kv => kv.Key + " " + kv.Value)) + ")");
            Assert.Less(tris, 1_500_000, "whole-map triangle budget");
            foreach (string site in new[] { "district_azure", "district_crimson" })
                Assert.IsTrue(cat.Find("cm_layout_" + site).GetComponentsInChildren<MeshFilter>(true).Any(mf => mf.name.Contains("cm_district_")),
                    "cm_layout_" + site + " has its wall run (cm_district_*)");
        }
    }
}
