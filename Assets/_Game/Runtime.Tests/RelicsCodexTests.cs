using System.Collections.Generic;
using System.IO;
using CriticalMass.Runtime.Codex;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// DG-25 acceptance: the placement script against the sim map (every relic on a walkable floor cell, reachable on
    /// foot, at least 3 cells from a main route, clear of walls and edges), its determinism and mirroring, the secret
    /// ledge, Codex progress and the lore text by id with fallbacks.
    /// </summary>
    public sealed class RelicsCodexTests
    {
        static MapDef Map() => SkybreakBasinMap.BuildSkybreakBasin(GameConfig.CreateDefault());

        int _savedMask;
        bool _hadMask;

        [SetUp]
        public void SetUp()
        {
            _hadMask = PlayerPrefs.HasKey(CodexProgress.Key);
            _savedMask = PlayerPrefs.GetInt(CodexProgress.Key, 0);
            CodexProgress.ResetAll();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hadMask) PlayerPrefs.SetInt(CodexProgress.Key, _savedMask);
            else PlayerPrefs.DeleteKey(CodexProgress.Key);
            PlayerPrefs.Save();
        }

        [Test]
        public void EveryRelicSitsOnAWalkableCellAtLeastThreeCellsFromAMainRoute()
        {
            MapDef map = Map();
            RelicPlacement.Spot[] spots = RelicPlacement.Place(map);
            Assert.AreEqual(RelicPlacement.Count, spots.Length);
            List<string> errors = RelicPlacement.Check(map, spots);
            Assert.IsEmpty(errors, string.Join("\n", errors));
            int[] dist = RelicPlacement.RouteDistance(map, RelicPlacement.RouteCells(map));
            foreach (RelicPlacement.Spot s in spots)
            {
                Assert.IsTrue(MapGrid.IsWalkable(map, s.Cx, s.Cz), s + " walkable");
                Assert.GreaterOrEqual(dist[s.Cz * map.Width + s.Cx], 3, s + " route distance");
                TestContext.WriteLine($"relic at {s} route distance {dist[s.Cz * map.Width + s.Cx]}");
            }
        }

        [Test]
        public void TheCheckCatchesARelicOnARoute()
        {
            MapDef map = Map();
            RelicPlacement.Spot[] spots = RelicPlacement.Place(map);
            int hill = MapGrid.CellIndex(map, (int)map.Hill.X, (int)map.Hill.Z);
            spots[0] = new RelicPlacement.Spot { Cx = hill % map.Width, Cz = hill / map.Width };
            Assert.IsNotEmpty(RelicPlacement.Check(map, spots));
        }

        [Test]
        public void PlacementIsDeterministicMirroredAndSpreadOut()
        {
            RelicPlacement.Spot[] a = RelicPlacement.Place(Map()), b = RelicPlacement.Place(Map());
            CollectionAssert.AreEqual(a, b);
            for (int i = 0; i < a.Length; i += 2)
            {
                Assert.AreEqual(SkybreakBasinMap.MapWidth - 1 - a[i].Cx, a[i + 1].Cx, "pair " + i + " mirrored in x");
                Assert.AreEqual(a[i].Cz, a[i + 1].Cz, "pair " + i + " same row");
            }
            for (int i = 0; i < a.Length; i++)
                for (int j = i + 1; j < a.Length; j++)
                {
                    int dx = a[i].Cx - a[j].Cx, dz = a[i].Cz - a[j].Cz;
                    Assert.Greater(dx * dx + dz * dz, 36, $"relics {a[i]} and {a[j]} at least 6 cells apart");
                }
        }

        [Test]
        public void TheCrownLedgeIsFloorThatNoWalkReaches()
        {
            MapDef map = Map();
            Assert.IsTrue(RelicPlacement.TryCrownSpot(map, out RelicPlacement.Spot s));
            Assert.AreEqual(Cell.Floor, map.Cells[s.Cz * map.Width + s.Cx]);
            int[] reach = RelicPlacement.Reach(map, RelicPlacement.StartCell(map, 0));
            Assert.Less(reach[s.Cz * map.Width + s.Cx], 0, "the overlook ledge needs a jump");
        }

        [Test]
        public void CodexCountsEightAndKeepsTheCrownApart()
        {
            Assert.AreEqual(0, CodexProgress.FoundCount);
            Assert.IsTrue(CodexProgress.Mark(2));
            Assert.IsFalse(CodexProgress.Mark(2), "only the first find counts");
            Assert.IsTrue(CodexProgress.Mark(CodexProgress.CrownIndex));
            Assert.AreEqual(1, CodexProgress.FoundCount, "the crown is not one of the 8");
            Assert.AreEqual("1 OF 8", CodexProgress.CountLabel(CodexProgress.FoundCount));
            Assert.AreEqual((1 << 2) | (1 << 8), PlayerPrefs.GetInt(CodexProgress.Key, 0), "persisted");
            Assert.AreEqual("relic-12", CodexProgress.IdOf(CodexProgress.CrownIndex));
        }

        [Test]
        public void EveryRelicHasANameAndTwoLines()
        {
            for (int i = 0; i <= CodexProgress.CrownIndex; i++)
            {
                LoreText.Relic r = LoreText.GetRelic(CodexProgress.IdOf(i));
                Assert.IsFalse(string.IsNullOrEmpty(r.Name), r.Id);
                Assert.IsFalse(string.IsNullOrEmpty(r.Line1), r.Id);
                Assert.IsFalse(string.IsNullOrEmpty(r.Line2), r.Id);
            }
            Assert.AreEqual("Crown of the Hill", LoreText.GetRelic("relic-12").Name);
            Assert.IsNotEmpty(LoreText.GetRelic("relic-99").Name, "an unknown id still reads");
        }

        [Test]
        public void FallbacksMatchTheWordsPackWhenItIsPresent()
        {
            string json = LoreText.ReadPackFile("relics.json");
            if (json == null) Assert.Ignore("words pack (DG-04) not in this tree; fallbacks are used");
            foreach (string[] f in LoreText.FallbackRelics)
            {
                StringAssert.Contains("\"" + f[0] + "\"", json, f[0] + " in the pack");
                StringAssert.Contains(f[1], json, f[0] + " name matches the pack");
            }
        }
    }
}
