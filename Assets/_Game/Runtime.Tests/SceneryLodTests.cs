using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Dressing;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// Scenery LODs (2026-11-12 triangle budget): level selection by quality and distance, the naming rule, chunk keys and
    /// bounds, the clutter density table, shadow casting by size, and that the lighter kit files match their sources.
    /// </summary>
    public sealed class SceneryLodTests
    {
        const int VeryLow = 0, Low = FramePolicy.LevelLow, Medium = FramePolicy.LevelMedium, High = FramePolicy.LevelHigh;

        // ------------------------------------------------------------------------------------------- level selection
        [Test]
        public void PickFollowsTheQualityBands()
        {
            Assert.AreEqual(0, SceneryLod.Pick(High, 0f));
            Assert.AreEqual(0, SceneryLod.Pick(High, 15.9f));
            Assert.AreEqual(1, SceneryLod.Pick(High, 16f));
            Assert.AreEqual(1, SceneryLod.Pick(High, 500f), "High never draws level 2");
            Assert.AreEqual(0, SceneryLod.Pick(Medium, 5.9f));
            Assert.AreEqual(1, SceneryLod.Pick(Medium, 6f));
            Assert.AreEqual(1, SceneryLod.Pick(Medium, 13.9f));
            Assert.AreEqual(2, SceneryLod.Pick(Medium, 14f));
            Assert.AreEqual(1, SceneryLod.Pick(Low, 0f), "Low never draws level 0");
            Assert.AreEqual(2, SceneryLod.Pick(Low, 6f));
            Assert.AreEqual(1, SceneryLod.Pick(VeryLow, 0f));
            Assert.AreEqual(SceneryLod.Pick(High, 20f), SceneryLod.Pick(5, 20f), "Ultra behaves like High");
        }

        [Test]
        public void PickNeverGetsFinerWithDistance()
        {
            for (int q = 0; q <= 5; q++)
            {
                int last = 0;
                for (float d = 0f; d < 60f; d += 0.25f)
                {
                    int l = SceneryLod.Pick(q, d);
                    Assert.GreaterOrEqual(l, last, $"quality {q} at {d} m");
                    Assert.IsTrue(SceneryLod.Builds(q, l), $"quality {q} picks level {l}, which it builds");
                    last = l;
                }
            }
        }

        [Test]
        public void HysteresisHoldsTheCoarserLevelAtTheEdge()
        {
            Assert.AreEqual(2, SceneryLod.Pick(Medium, 13.5f, 2), "inside the band by less than the hysteresis: stays coarse");
            Assert.AreEqual(1, SceneryLod.Pick(Medium, 12.9f, 2), "a full metre inside: goes finer");
            Assert.AreEqual(2, SceneryLod.Pick(Medium, 14f, 1), "coarser at the edge at once");
            Assert.AreEqual(1, SceneryLod.Pick(Medium, 5.5f, 1));
            Assert.AreEqual(0, SceneryLod.Pick(Medium, 4.9f, 1));
            Assert.AreEqual(0, SceneryLod.Pick(Medium, 3f, -1), "no level shown yet: plain pick");
        }

        [Test]
        public void BuiltLevelsPerQuality()
        {
            Assert.IsTrue(SceneryLod.Builds(High, 0) && SceneryLod.Builds(High, 1) && !SceneryLod.Builds(High, 2));
            Assert.IsTrue(SceneryLod.Builds(Medium, 0) && SceneryLod.Builds(Medium, 1) && SceneryLod.Builds(Medium, 2));
            Assert.IsTrue(!SceneryLod.Builds(Low, 0) && SceneryLod.Builds(Low, 1) && SceneryLod.Builds(Low, 2));
            Assert.AreEqual(1, WorldView.BuiltLevel(Low, 0), "Low falls back from 0 to 1");
            Assert.AreEqual(1, WorldView.BuiltLevel(High, 2), "High falls back from 2 to 1");
            Assert.AreEqual(2, WorldView.BuiltLevel(Medium, 2));
            Assert.IsFalse(WorldView.NeedsRebuild(High, 4), "High and Very High build the same groups");
            Assert.IsTrue(WorldView.NeedsRebuild(High, Medium));
            Assert.IsTrue(WorldView.NeedsRebuild(Low, VeryLow), "clutter density differs");
            Assert.IsTrue(WorldView.NeedsRebuild(-1, High), "nothing built yet");
        }

        [Test]
        public void ResolveFallsBackAndCullsTinyPiecesFar()
        {
            var files = new HashSet<string> { "env_rocks", "env_rocks_lod1", "env_rocks_lod2", "env_grass_tuft", "env_bush", "env_bush_lod1" };
            Func<string, bool> has = files.Contains;
            Assert.AreEqual(2, SceneryLod.Resolve("env_rocks", 2, has));
            Assert.AreEqual(1, SceneryLod.Resolve("env_rocks", 1, has));
            Assert.AreEqual(0, SceneryLod.Resolve("env_rocks", 0, has));
            Assert.IsTrue(SceneryLodTable.Rows["env_grass_tuft"].FarCull, "grass is tiny on screen");
            Assert.AreEqual(-1, SceneryLod.Resolve("env_grass_tuft", 2, has), "tiny: not drawn at the far level");
            Assert.AreEqual(0, SceneryLod.Resolve("env_grass_tuft", 1, has), "no lighter file: level 0");
            Assert.IsFalse(SceneryLodTable.Rows["cm_tower"].FarCull, "big pieces are never culled");
            Assert.AreEqual(0, SceneryLod.Resolve("cm_tower", 2, n => n == "cm_tower"), "no lighter file and not tiny: level 0 everywhere");
            Assert.AreEqual(1, SceneryLod.Resolve("env_bush", 2, has), "no far file and not culled: level 1");
        }

        // ------------------------------------------------------------------------------------------------ naming
        [Test]
        public void NamingRuleRoundTrips()
        {
            Assert.AreEqual("env_rocks_lod1", SceneryLod.LevelPiece("env_rocks", 1));
            Assert.AreEqual("env_rocks_lod2", SceneryLod.LevelPiece("env_rocks", 2));
            Assert.AreEqual("env_rocks", SceneryLod.LevelPiece("env_rocks", 0));
            for (int l = 0; l < SceneryLod.Levels; l++)
            {
                Assert.AreEqual("cm_layout_district_azure", SceneryLod.BasePiece(SceneryLod.LevelPiece("cm_layout_district_azure", l)));
                Assert.AreEqual(l, SceneryLod.LevelOf(SceneryLod.LevelPiece("env_tree_round_l", l)));
            }
            Assert.AreEqual("cm_crystal_lamp_low", SceneryLod.BasePiece("cm_crystal_lamp_low"), "'_low' is not a level suffix");
            Assert.AreEqual(WorldLook.Prop, WorldKit.EnvToonLook("env_bush_lod1"), "a lighter file keeps its piece's look");
            Assert.AreEqual(WorldLook.Sky, WorldKit.EnvToonLook("env_sky_rock_l_lod2"));
            Assert.AreEqual(3, WorldKit.SwayKind("env_tree_round_l_lod2"), "and its sway kind");
            Assert.AreEqual(5, WorldKit.SwayKind("env_bridge_span_lod1"));
        }

        // ------------------------------------------------------------------------------------------- chunk bounds
        [Test]
        public void ChunkKeysTileTheMapIn16CellSquares()
        {
            Assert.AreEqual(16, WorldView.GroupSize);
            var keys = new HashSet<int>();
            for (int z = 0; z < 72; z++)
            for (int x = 0; x < 112; x++)
            {
                int k = WorldView.GroupOf(x + 0.5, z + 0.5);
                keys.Add(k);
                var (x0, z0, x1, z1) = WorldView.GroupRect(k);
                Assert.IsTrue(x >= x0 && x < x1 && z >= z0 && z < z1, $"cell {x},{z} inside the rectangle of its key {k}");
                Assert.AreEqual(16, x1 - x0);
                Assert.AreEqual(16, z1 - z0);
            }
            Assert.AreEqual(7 * 5, keys.Count, "a 112 x 72 map is 7 x 5 chunks");
            Assert.Less(WorldView.GroupOf(111.5, 71.5), WorldView.SkyKeyBase, "ground keys stay below the sky keys");
            var (sx0, sz0, _, _) = WorldView.GroupRect(WorldView.SkyKeyBase + WorldView.GroupOf(40.5, 20.5));
            Assert.AreEqual((32, 16), (sx0, sz0), "a sky key maps to its ground rectangle");
        }

        [Test]
        public void GroundDistanceToChunkBounds()
        {
            var b = new Bounds(new Vector3(8f, 3f, -8f), new Vector3(16f, 6f, 16f));
            Assert.AreEqual(0f, SceneryLod.GroundDistance(new Vector3(4f, 40f, -4f), b), 1e-5f, "inside on the ground: 0, height ignored");
            Assert.AreEqual(4f, SceneryLod.GroundDistance(new Vector3(20f, 0f, -8f), b), 1e-5f);
            Assert.AreEqual(5f, SceneryLod.GroundDistance(new Vector3(19f, 0f, 4f), b), 1e-5f, "corner: 3-4-5");
        }

        [Test]
        public void LookPointOfTheGameplayCameraIsItsTarget()
        {
            // Pitch 55, yaw 45, 23.4 u from the target (GameplayCamera).
            Quaternion rot = Quaternion.Euler(55f, 45f, 0f);
            Vector3 target = new Vector3(30f, 0f, -20f);
            Vector3 cam = target - rot * Vector3.forward * 23.4f;
            Vector3 look = SceneryLod.LookPoint(cam, rot * Vector3.forward);
            Assert.Less(Vector3.Distance(look, target), 1e-3f);
            Assert.AreEqual(cam, SceneryLod.LookPoint(cam, Vector3.forward), "looking level: the camera position");
        }

        // ------------------------------------------------------------------------------------------- density table
        [Test]
        public void ClutterDensityTable()
        {
            Assert.AreEqual(0.25f, SceneryLod.ClutterDensity(VeryLow));
            Assert.AreEqual(0.4f, SceneryLod.ClutterDensity(Low));
            Assert.AreEqual(0.7f, SceneryLod.ClutterDensity(Medium));
            Assert.AreEqual(1f, SceneryLod.ClutterDensity(High));
            Assert.AreEqual(1f, SceneryLod.ClutterDensity(5));
            for (int q = 1; q <= 5; q++) Assert.GreaterOrEqual(SceneryLod.ClutterDensity(q), SceneryLod.ClutterDensity(q - 1));
        }

        [Test]
        public void ClutterThinningIsStableAndNested()
        {
            int[] kept = new int[4];
            const int n = 4000;
            for (int i = 0; i < n; i++)
            {
                double x = (i * 7.31) % 112.0, z = (i * 3.77) % 72.0;
                bool prev = false;
                for (int q = 0; q <= High; q++)
                {
                    bool k = SceneryLod.KeepClutter("env_grass_tuft", x, z, q);
                    Assert.AreEqual(k, SceneryLod.KeepClutter("env_grass_tuft", x, z, q), "deterministic");
                    if (prev) Assert.IsTrue(k, "kept at a lower density means kept at every higher one");
                    prev = k;
                    if (k) kept[q]++;
                }
                Assert.IsTrue(SceneryLod.KeepClutter("cm_tower", x, z, VeryLow), "only clutter is thinned");
            }
            for (int q = 0; q <= High; q++)
                Assert.AreEqual(SceneryLod.ClutterDensity(q), kept[q] / (float)n, 0.05f, $"quality {q} keeps about its density");
        }

        [Test]
        public void OnlyLargePiecesCastShadows()
        {
            Assert.IsTrue(SceneryLod.LargeEnoughToCast("cm_tower"));
            Assert.IsTrue(SceneryLod.LargeEnoughToCast("env_tree_round_l_lod1"), "lighter files use their piece's size");
            Assert.IsFalse(SceneryLod.LargeEnoughToCast("env_pebbles"));
            Assert.IsFalse(SceneryLod.LargeEnoughToCast("env_grass_tuft"));
            Assert.IsTrue(SceneryLod.LargeEnoughToCast("env_not_in_the_table"), "new art casts until it is measured");
        }

        // ------------------------------------------------------------------------------------------- kit files
        static string Root => Path.GetDirectoryName(Application.dataPath);

        [Test]
        public void EveryLighterLevelIsInTheCatalogAndLighter()
        {
            WorldKitCatalog cat = WorldKitCatalog.Load();
            Assert.IsNotNull(cat, "WorldKitCatalog");
            int checkedRows = 0;
            foreach (KeyValuePair<string, SceneryLodRow> kv in SceneryLodTable.Rows)
            {
                SceneryLodRow r = kv.Value;
                if (r.Tris1 > 0)
                {
                    Assert.IsNotNull(cat.Find(kv.Key + "_lod1"), kv.Key + "_lod1 in the catalog");
                    Assert.LessOrEqual(r.Tris1, r.Tris0 * 0.8f, kv.Key + " level 1 is lighter");
                    checkedRows++;
                }
                else Assert.IsNull(cat.Find(kv.Key + "_lod1"), kv.Key + "_lod1 has no table row");
                if (r.Tris2 > 0)
                {
                    Assert.Greater(r.Tris1, 0, kv.Key + ": a far level needs a level 1");
                    Assert.IsNotNull(cat.Find(kv.Key + "_lod2"), kv.Key + "_lod2 in the catalog");
                    Assert.Less(r.Tris2, r.Tris1, kv.Key + " level 2 is lighter than level 1");
                }
                Assert.IsFalse(r.FarCull && r.Tris2 > 0, kv.Key + ": culled far and a far file");
            }
            Assert.GreaterOrEqual(checkedRows, 60, "lighter levels for the heavy pieces");
        }

        [Test]
        public void LodSourcesMatchTheKit()
        {
            string json = File.ReadAllText(Path.Combine(Root, "tools/art/scenery-lods.json"));
            int n = 0;
            foreach (Match m in Regex.Matches(json, "\"(?<name>(?:env|cm)_[a-z0-9_]+)\": \\{[^{}]*?\"sha256\": \"(?<sha>[0-9a-f]{64})\"[^{}]*?\\}"))
            {
                string name = m.Groups["name"].Value;
                string dir = name.StartsWith("cm_", StringComparison.Ordinal) ? WorldKit.BasesDir : WorldKit.EnvDir;
                string path = Path.Combine(Root, dir + name + ".fbx");
                if (!File.Exists(path)) continue;
                if (!File.Exists(Path.Combine(Root, dir + name + "_lod1.fbx"))) continue;
                using (SHA256 sha = SHA256.Create())
                {
                    string got = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
                    Assert.AreEqual(m.Groups["sha"].Value, got,
                        name + ".fbx changed since its lighter levels were built: re-run Art/Environment/_source~/build_scenery_lods.py");
                }
                n++;
            }
            Assert.GreaterOrEqual(n, 60, "manifest rows checked");
        }
    }
}
