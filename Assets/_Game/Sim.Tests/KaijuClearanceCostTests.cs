using System;
using System.Collections.Generic;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b11-dg (frame cost): Kaiju.RefreshClearance patches only the cells near a grid change instead of rebuilding the whole
    /// shadow grid. These prove (1) the Sim result is identical to the old full rebuild (SimProfile.ForceFullClearance) and
    /// (2) the full rebuilds drop to the first one, with at most one refresh of any kind per grid change. Fixed seeds.
    /// </summary>
    [TestFixture]
    public sealed class KaijuClearanceCostTests
    {
        private const int Seed = 1337;
        private const int Ticks = 30 * 75; // 75 s at 30 Hz

        [TearDown]
        public void Restore()
        {
            SimProfile.ForceFullClearance = false;
        }

        /// <summary>A seeded 16v16 match on the game's rules with the Kaiju hatched on tick 1 and owned by Azure (it walks to the hill).</summary>
        private static World NewMatch(bool crush = true)
        {
            GameConfig cfg = GameConfig.CreateDefault();
            if (!crush) cfg.Physics.KaijuHillSiege = false;
            World w = World.CreateWorld(new WorldOptions { Seed = Seed, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 1);
            w.ForceHatch = true;
            return w;
        }

        private static int FirstMiner(World w, TeamId team)
        {
            for (int i = 0; i < w.ActorCount; i++)
                if (w.Actors[i].Cls == ClassId.Miner && w.Actors[i].Team == team) return i;
            return -1;
        }

        /// <summary>
        /// Every 20 ticks: a Crimson barricade on the cell just ahead of the Kaiju's body (within its crush reach of WallR + 0.25),
        /// through the same slot and SetCell path a miner uses. Returns the slot, or -1 when nothing was placed.
        /// </summary>
        private static int PlaceAheadOfKaiju(World w, int owner)
        {
            if (owner < 0 || !w.Match.Hatched || w.Tick % 20 != 0) return -1;
            Kaiju k = w.Kaiju;
            if (k.State == KaijuState.Egg) return -1;
            double d = Kaiju.WallR(w) + 0.2;
            int cx = JsMath.FloorToInt(k.X + Math.Cos(k.Facing) * d);
            int cz = JsMath.FloorToInt(k.Z + Math.Sin(k.Facing) * d);
            MapDef map = w.Map;
            if (!MapGrid.InBounds(map, cx, cz)) return -1;
            byte t = map.Cells[cz * map.Width + cx];
            if (t != Cell.Floor && t != Cell.Bridge) return -1;
            Barricade[] list = w.Barricades;
            for (int s = 0; s < list.Length; s++)
            {
                Barricade b = list[s];
                if (b.Active) continue;
                MinerBarricadeConfig mb = w.Config.Abilities.MinerBarricade;
                b.Active = true;
                b.Owner = owner;
                b.Team = w.Actors[owner].Team;
                b.Cx = cx;
                b.Cz = cz;
                b.Hp = mb.MaxHp;
                b.Ttl = mb.LifetimeSeconds;
                b.PrevCell = t;
                MapGrid.SetCell(w, cx, cz, Cell.Barricade);
                return s;
            }
            return -1;
        }

        private static ulong Hash(World w)
        {
            ulong h = MatchHash.Of(w);
            h ^= (ulong)BitConverter.DoubleToInt64Bits(w.Kaiju.X) * 1099511628211UL;
            h ^= (ulong)BitConverter.DoubleToInt64Bits(w.Kaiju.Z) * 0x9E3779B97F4A7C15UL;
            h ^= (ulong)BitConverter.DoubleToInt64Bits(w.Kaiju.Hp);
            return h;
        }

        private static string FirstDiff(World a, World b)
        {
            KaijuNav na = a.KaijuNav, nb = b.KaijuNav;
            for (int i = 0; i < na.Clear.Cells.Length; i++)
            {
                if (na.Clear.Cells[i] != nb.Clear.Cells[i]) return "shadow cell " + i;
                if (na.Squeeze[i] != nb.Squeeze[i]) return "squeeze cell " + i;
                if (na.Region[i] != nb.Region[i]) return "region of cell " + i;
            }
            if (na.ClearGridVersion != nb.ClearGridVersion) return "ClearGridVersion";
            if (na.FieldWallVersion != nb.FieldWallVersion) return "FieldWallVersion";
            if (na.RepathIn != nb.RepathIn) return "RepathIn";
            if (Hash(a) != Hash(b)) return "world hash";
            return null;
        }

        /// <summary>
        /// (1) Two copies of the same seeded match in lock step, one on the old path (a full rebuild on every grid change), one on
        /// the patch, with the Kaiju crushing barricades placed on its path: after every tick the shadow cells, squeeze flags,
        /// region labels, the flow-field invalidation and the world hash are identical, and so is the hash at the end.
        /// </summary>
        [Test]
        public void TheWorldHashIsIdenticalWithThePatchAndWithAFullRebuildEveryCallWhileTheKaijuCrushesBarricades()
        {
            World full = NewMatch();
            World patch = NewMatch();
            int ownerFull = FirstMiner(full, TeamId.Crimson);
            int ownerPatch = FirstMiner(patch, TeamId.Crimson);
            Assert.GreaterOrEqual(ownerFull, 0, "a Crimson miner owns the placed barricades");
            InputFrame sf = SimInput.EmptyInput();
            InputFrame sp = SimInput.EmptyInput();
            var placed = new List<int>();
            int crushed = 0, gridChanges = 0;
            for (int t = 0; t < Ticks && full.Match.Phase != MatchPhase.Ended; t++)
            {
                SimProfile.ForceFullClearance = true;
                int sFull = PlaceAheadOfKaiju(full, ownerFull);
                MatchTick.Advance(full, null, sf);
                SimProfile.ForceFullClearance = false;
                int gv = patch.GridVersion;
                int sPatch = PlaceAheadOfKaiju(patch, ownerPatch);
                MatchTick.Advance(patch, null, sp);
                gridChanges += patch.GridVersion - gv;
                Assert.AreEqual(sFull, sPatch, "same placement at tick " + t);
                if (sPatch >= 0) placed.Add(sPatch);
                for (int j = placed.Count - 1; j >= 0; j--)
                {
                    Barricade b = patch.Barricades[placed[j]];
                    if (b.Active) continue;
                    if (b.Hp <= 0) crushed++;
                    placed.RemoveAt(j);
                }
                string diff = FirstDiff(full, patch);
                Assert.IsNull(diff, "tick " + t + ": the patched world differs from the full-rebuild world (" + diff + ")");
            }
            Console.WriteLine("{\"item\":\"b11-dg clearance hash\",\"seed\":" + Seed + ",\"ticks\":" + full.Tick + ",\"gridChanges\":" +
                              gridChanges + ",\"barricadesBroken\":" + crushed + ",\"hash\":\"" + Hash(patch).ToString("x16") + "\"}");
            Assert.AreEqual(Hash(full), Hash(patch), "final world hash");
            Assert.GreaterOrEqual(crushed, 3, "the Kaiju broke the barricades placed on its path (the scenario is exercised)");
            Assert.GreaterOrEqual(gridChanges, 6, "the grid changed during the run");
        }

        /// <summary>
        /// (1b) Purity of the patch near the bridges (squeeze cells and the deck rule) and on the no-crush rule (barricades block
        /// the clearance): random barricade and wall flips of cells within 3 of a bridge cell, every tick, in lock step against
        /// the full rebuild. Every tick the shadow grid, squeeze flags and regions match.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void RandomCellFlipsNearTheBridgesGiveTheSameShadowGridAsAFullRebuild(bool crush)
        {
            World full = NewMatch(crush);
            World patch = NewMatch(crush);
            MapDef map = patch.Map;
            int W = map.Width, H = map.Height;
            var cand = new List<int>();
            for (int cz = 0; cz < H; cz++)
                for (int cx = 0; cx < W; cx++)
                {
                    byte t = map.Cells[cz * W + cx];
                    if (t != Cell.Floor && t != Cell.Bridge) continue;
                    bool near = false;
                    for (int dz = -3; dz <= 3 && !near; dz++)
                        for (int dx = -3; dx <= 3; dx++)
                            if (MapGrid.InBounds(map, cx + dx, cz + dz) && map.Cells[(cz + dz) * W + cx + dx] == Cell.Bridge) { near = true; break; }
                    if (near) cand.Add(cz * W + cx);
                }
            Assert.Greater(cand.Count, 20, "the map has bridge cells");
            byte[] original = (byte[])map.Cells.Clone();
            var rng = new Random(Seed);
            InputFrame sf = SimInput.EmptyInput();
            InputFrame sp = SimInput.EmptyInput();
            int flips = 0;
            for (int t = 0; t < 30 * 30; t++)
            {
                if (t > 2)
                {
                    int k = 1 + rng.Next(3);
                    for (int j = 0; j < k; j++)
                    {
                        int i = cand[rng.Next(cand.Count)];
                        byte to = patch.Map.Cells[i] != original[i] ? original[i] : (rng.Next(4) == 0 ? Cell.Wall : Cell.Barricade);
                        MapGrid.SetCell(full, i % W, i / W, to);
                        MapGrid.SetCell(patch, i % W, i / W, to);
                        flips++;
                    }
                }
                SimProfile.ForceFullClearance = true;
                MatchTick.Advance(full, null, sf);
                SimProfile.ForceFullClearance = false;
                MatchTick.Advance(patch, null, sp);
                string diff = FirstDiff(full, patch);
                Assert.IsNull(diff, "crush=" + crush + " tick " + t + ": the patched shadow grid differs from the full rebuild (" + diff + ")");
            }
            Assert.Greater(flips, 100);
        }

        /// <summary>
        /// (2) The same crush scenario on the patch alone, then on the old path alone: with the patch the whole-grid rebuilds
        /// drop to the first one (at most 2), and refreshes of any kind are at most one per grid change (+ the first build);
        /// the old path rebuilds the whole grid on every grid change.
        /// </summary>
        [Test]
        public void FullClearanceRebuildsDropToAtMostOnePerGridChange()
        {
            int Run(bool force, out int patches, out int regions, out int gridChanges)
            {
                SimProfile.ForceFullClearance = force;
                World w = NewMatch();
                int owner = FirstMiner(w, TeamId.Crimson);
                InputFrame s = SimInput.EmptyInput();
                SimProfile.ResetCounters();
                gridChanges = 0;
                for (int t = 0; t < Ticks && w.Match.Phase != MatchPhase.Ended; t++)
                {
                    int gv = w.GridVersion;
                    PlaceAheadOfKaiju(w, owner);
                    MatchTick.Advance(w, null, s);
                    gridChanges += w.GridVersion - gv;
                }
                patches = SimProfile.ClearancePatches;
                regions = SimProfile.ClearanceRegionBuilds;
                int fulls = SimProfile.ClearanceFullRebuilds;
                SimProfile.ForceFullClearance = false;
                return fulls;
            }

            int fixedFull = Run(false, out int fixedPatches, out int fixedRegions, out int changes);
            int oldFull = Run(true, out int oldPatches, out int oldRegions, out int oldChanges);
            Console.WriteLine("{\"item\":\"b11-dg clearance rebuilds\",\"seed\":" + Seed + ",\"gridChanges\":" + changes +
                              ",\"fixed\":{\"full\":" + fixedFull + ",\"patches\":" + fixedPatches + ",\"regions\":" + fixedRegions +
                              "},\"old\":{\"full\":" + oldFull + ",\"patches\":" + oldPatches + ",\"regions\":" + oldRegions + "}}");
            Assert.AreEqual(changes, oldChanges, "both runs see the same grid changes (same Sim)");
            Assert.GreaterOrEqual(changes, 6, "the grid changed during the run");
            Assert.LessOrEqual(fixedFull, 2, "with the patch only the first build (and at most one spare) rebuilds the whole grid");
            Assert.GreaterOrEqual(fixedPatches, 3, "the patch path ran");
            Assert.LessOrEqual(fixedFull + fixedPatches, changes + 1, "at most one refresh per grid change (+ the first build)");
            Assert.LessOrEqual(fixedRegions, fixedFull + fixedPatches, "regions rebuilt at most once per refresh");
            Assert.Greater(oldFull, fixedFull, "the old path rebuilt the whole grid on every grid change");
            Assert.AreEqual(0, oldPatches, "the old path never patches");
        }
    }
}
