using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.View.Affordance;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// feat/terrain-affordances: the highlighted set is the Sim's valid set (per class and action, on the real map: cliff
    /// edges, bridges, the enemy base, full and empty carry and bank, already-dug cells), the strong target is what the
    /// action then does, the prompt follows the binding and the device, the damage stages and the progress values are the
    /// Sim's, and the settings switch each element.
    /// </summary>
    [TestFixture]
    public sealed class TerrainAffordanceTests
    {
        const int Me = 0;

        static World Field(ClassId cls)
        {
            GameConfig cfg = GameConfig.CreateDefault();
            // b13 r2: with the hut rule the Builder owns dig / place (Terraform.OwnerClass); the Miner's dig and barricade affordance
            // is exercised where he still owns them (no hut rule, as in Classic / PhysicsOnly).
            if (cls == ClassId.Miner) cfg.Depth.Hut.On = false;
            World w = World.CreateWorld(new WorldOptions { Seed = 7, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = cls, Config = cfg });
            for (int j = 1; j < w.ActorCount; j++)
            {
                w.Actors[j].Alive = false;
                w.Actors[j].RespawnIn = 1e9;
                w.MeatDropped[j] = 1;
                if (w.Depth != null) w.Depth.WasAlive[j] = 0;
            }
            Put(w, Me, w.X[Me], w.Z[Me], w.Actors[Me].Facing);
            w.Tick = 100;
            return w;
        }

        static void Put(World w, int j, double x, double z, double facing)
        {
            Actor a = w.Actors[j];
            a.Alive = true;
            if (!(a.Hp > 0)) a.Hp = a.MaxHp;
            a.SpawnProtect = 0;
            a.StandUp = 0;
            a.Facing = facing;
            w.X[j] = w.PrevX[j] = x;
            w.Z[j] = w.PrevZ[j] = z;
            w.Vx[j] = 0;
            w.Vz[j] = 0;
        }

        /// <summary>Stands actor 0 on the first floor cell and facing (from its spawn outward) where ok(scan) holds.</summary>
        static bool FindSpot(World w, AffordanceScan scan, Func<AffordanceScan, bool> ok, int maxRing = 40)
        {
            MapDef m = w.Map;
            int sx = JsMath.FloorToInt(w.X[Me]), sz = JsMath.FloorToInt(w.Z[Me]);
            int budget = scan.RouteBudget;
            scan.RouteBudget = 0;
            try
            {
                for (int ring = 0; ring <= maxRing; ring++)
                for (int cz = sz - ring; cz <= sz + ring; cz++)
                for (int cx = sx - ring; cx <= sx + ring; cx++)
                {
                    if (Math.Abs(cx - sx) != ring && Math.Abs(cz - sz) != ring) continue;
                    if (!MapGrid.InBounds(m, cx, cz) || m.Cells[cz * m.Width + cx] != Cell.Floor) continue;
                    for (int f = 0; f < 4; f++)
                    {
                        Put(w, Me, cx + 0.5, cz + 0.5, f * Math.PI / 2);
                        scan.Scan(w, Me, false, double.NaN, double.NaN, false);
                        if (ok(scan)) return true;
                    }
                }
                return false;
            }
            finally
            {
                scan.RouteBudget = budget;
            }
        }

        static void Warm(World w, AffordanceScan scan, bool all = false)
        {
            for (int k = 0; k < 400; k++) scan.Scan(w, Me, false, double.NaN, double.NaN, all);
        }

        static HashSet<int> Cells(AffordanceScan s, World w, AffAction act)
        {
            var set = new HashSet<int>();
            for (int k = 0; k < s.MarkerCount; k++) if (s.Markers[k].Action == act) set.Add(s.Markers[k].Cz * w.Map.Width + s.Markers[k].Cx);
            return set;
        }

        static bool InDisk(World w, int cx, int cz, double r)
        {
            double dx = cx + 0.5 - w.X[Me], dz = cz + 0.5 - w.Z[Me];
            return dx * dx + dz * dz <= r * r;
        }

        [TearDown]
        public void Clean()
        {
            WorldFeedbackSettings.ResetAll();
            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------------ Miner

        [Test]
        public void DigVerdictIsTheSimsDigRefusalOnEveryCell()
        {
            World w = Field(ClassId.Miner);
            var scan = new AffordanceScan();
            MapDef m = w.Map;
            // Already-dug cells and the enemy base are on the map too: dig two holes first.
            Assert.IsTrue(FindSpot(w, scan, s => s.Primary.Valid), "a diggable cell exists");
            Assert.AreEqual(Terraform.Dug, Terraform.Dig(w, Me, double.NaN, double.NaN));
            w.Tick += 1000;
            int refusals = 0, checkedCells = 0;
            for (int cz = 0; cz < m.Height; cz += 2)
            for (int cx = 0; cx < m.Width; cx += 2)
            {
                int sim = Terraform.DigRefusal(w, cx, cz);
                Assert.AreEqual(sim, scan.DigVerdict(w, cx, cz), "cell (" + cx + ", " + cz + ")");
                if (sim != 0) refusals++;
                checkedCells++;
            }
            Assert.Greater(refusals, 0, "some cells are refused (walls, pads, bridges, voids)");
            Assert.Less(refusals, checkedCells, "some cells are diggable");
        }

        [Test]
        public void MinerDigMarkersAreExactlyTheDiggableCellsNearby()
        {
            World w = Field(ClassId.Miner);
            var scan = new AffordanceScan();
            Assert.IsTrue(FindSpot(w, scan, s => s.Primary.Valid), "a diggable spot");
            Warm(w, scan);
            HashSet<int> marked = Cells(scan, w, AffAction.Dig);
            MapDef m = w.Map;
            int valid = 0;
            for (int cz = 0; cz < m.Height; cz++)
            for (int cx = 0; cx < m.Width; cx++)
            {
                if (!InDisk(w, cx, cz, AffordanceScan.Radius)) continue;
                byte t = m.Cells[cz * m.Width + cx];
                if (t == Cell.BreakableIntact || t == Cell.BreakableCracked) continue;
                bool ok = Terraform.DigRefusal(w, cx, cz) == 0;
                int i = cz * m.Width + cx;
                if (ok) valid++;
                if (!ok) Assert.IsFalse(marked.Contains(i), "an undiggable cell is marked: (" + cx + ", " + cz + ")");
                else if (!scan.Capped || Terraform.CircleHitsCell(w.X[Me], w.Z[Me], w.Config.Abilities.MinerTerraform.Reach, cx, cz))
                    Assert.IsTrue(marked.Contains(i), "a diggable cell is not marked: (" + cx + ", " + cz + ")");
            }
            Assert.Greater(valid, 0);
            for (int k = 0; k < scan.MarkerCount; k++)
            {
                AffMarker mk = scan.Markers[k];
                if (mk.Action == AffAction.Dig)
                    Assert.AreEqual(Terraform.CircleHitsCell(w.X[Me], w.Z[Me], w.Config.Abilities.MinerTerraform.Reach, mk.Cx, mk.Cz), mk.InReach, "in reach is the Sim's reach");
            }
        }

        [Test]
        public void MinerTargetIsWhatDigAndPlaceDo()
        {
            World w = Field(ClassId.Miner);
            var scan = new AffordanceScan();
            Assert.IsTrue(FindSpot(w, scan, s => s.Primary.Valid), "a diggable spot");
            scan.Scan(w, Me, false, double.NaN, double.NaN, false);
            AffTarget p = scan.Primary;
            Assert.AreEqual(AffAction.Dig, p.Action);
            Assert.IsTrue(Terraform.TargetCell(w, Me, double.NaN, double.NaN, out int tx, out int tz));
            Assert.AreEqual(tx, p.Cx);
            Assert.AreEqual(tz, p.Cz);
            Assert.AreEqual(Terraform.Dug, Terraform.Dig(w, Me, double.NaN, double.NaN));
            Assert.AreEqual(Cell.Void, w.Map.Cells[tz * w.Map.Width + tx], "the outlined cell is the one dug");

            // Right after the dig the team cooldown refuses the next one: the target says so ("wait").
            scan.Scan(w, Me, false, double.NaN, double.NaN, false);
            if (Terraform.WaitTicks(w, w.Actors[Me].Team) > 0)
            {
                Assert.IsFalse(scan.Primary.Valid);
                Assert.AreEqual(AffWords.Wait, scan.Primary.Why);
            }
            w.Tick += 10000;

            // Place mode: the ghost sits on the cell Place fills, valid exactly when Place succeeds (the hole just dug).
            w.Terraform.Mode[Me] = (byte)TerraformMode.Place;
            scan.Scan(w, Me, false, double.NaN, double.NaN, false);
            p = scan.Primary;
            Assert.AreEqual(AffAction.PlaceBlock, p.Action);
            Assert.AreEqual(1, p.Ghost);
            Assert.AreEqual(1, p.Count, "one block carried");
            bool valid = p.Valid;
            int cell = p.Cz * w.Map.Width + p.Cx;
            Assert.AreEqual(1, p.GhostCount);
            Assert.AreEqual(cell, p.GhostCells[0]);
            int code = Terraform.Place(w, Me, double.NaN, double.NaN);
            Assert.AreEqual(valid, code == Terraform.Placed, "ghost valid == Place succeeded (" + code + ", why " + p.Why + ")");
            if (valid) Assert.AreEqual(Cell.Floor, w.Map.Cells[cell]);
        }

        [Test]
        public void MinerRefusalsNameTheReason()
        {
            World w = Field(ClassId.Miner);
            var scan = new AffordanceScan();
            Assert.IsTrue(FindSpot(w, scan, s => s.Primary.Valid), "a diggable spot");
            // Full carry: hands full, and the scan shows no dig markers.
            w.Terraform.Carried[Me] = (byte)Terraform.CarryMax(w.Config);
            scan.Scan(w, Me, false, double.NaN, double.NaN, false);
            Assert.IsFalse(scan.Primary.Valid);
            Assert.AreEqual(AffWords.HandsFull, scan.Primary.Why);
            Assert.AreEqual(0, Cells(scan, w, AffAction.Dig).Count);
            // Empty carry in place mode: no blocks.
            w.Terraform.Carried[Me] = 0;
            w.Terraform.Mode[Me] = (byte)TerraformMode.Place;
            scan.Scan(w, Me, false, double.NaN, double.NaN, false);
            Assert.IsFalse(scan.Primary.Valid);
            Assert.AreEqual(AffWords.NoBlocks, scan.Primary.Why);
            Assert.AreEqual(0, Cells(scan, w, AffAction.PlaceBlock).Count);
            // Getting up: the Sim ignores the key while the miner stands up.
            w.Terraform.Mode[Me] = (byte)TerraformMode.Dig;
            w.Actors[Me].StandUp = 0.5;
            scan.Scan(w, Me, false, double.NaN, double.NaN, false);
            Assert.AreEqual(AffWords.GettingUp, scan.Primary.Why);
        }

        [Test]
        public void BreakableWallsAreMarkedForTheMiner()
        {
            World w = Field(ClassId.Miner);
            MapDef m = w.Map;
            int wall = -1;
            for (int i = 0; i < m.Cells.Length && wall < 0; i++) if (m.Cells[i] == Cell.BreakableIntact) wall = i;
            if (wall < 0) Assert.Ignore("this map has no breakable walls");
            int wx = wall % m.Width, wz = wall / m.Width;
            // Stand on the nearest floor cell beside it.
            bool stood = false;
            for (int d = 1; d <= 3 && !stood; d++)
            for (int k = 0; k < 4 && !stood; k++)
            {
                int cx = wx + (k == 0 ? d : k == 1 ? -d : 0), cz = wz + (k == 2 ? d : k == 3 ? -d : 0);
                if (!MapGrid.InBounds(m, cx, cz) || m.Cells[cz * m.Width + cx] != Cell.Floor) continue;
                Put(w, Me, cx + 0.5, cz + 0.5, 0);
                stood = true;
            }
            Assert.IsTrue(stood);
            var scan = new AffordanceScan();
            scan.Scan(w, Me, false, double.NaN, double.NaN, false);
            Assert.IsTrue(Cells(scan, w, AffAction.BreakWall).Contains(wall), "the breakable wall next to him is marked");
            foreach (int i in Cells(scan, w, AffAction.BreakWall))
                Assert.IsTrue(m.Cells[i] == Cell.BreakableIntact || m.Cells[i] == Cell.BreakableCracked);
        }

        [Test]
        public void BarricadeTargetIsWhatTheBarricadeDoes()
        {
            World w = Field(ClassId.Miner);
            var scan = new AffordanceScan();
            Assert.IsTrue(FindSpot(w, scan, s => s.Secondary.Has && s.Secondary.Valid && s.Secondary.Action == AffAction.Barricade), "a spot that takes a barricade");
            AffTarget b = scan.Secondary;
            int cx = b.Cx, cz = b.Cz;
            Actor a = w.Actors[Me];
            double r = w.Config.Abilities.MinerBarricade.PlaceRange;
            double ax = w.X[Me] + Math.Cos(a.Facing) * r, az = w.Z[Me] + Math.Sin(a.Facing) * r;
            Assert.IsTrue(Barricades.TryPlaceBarricade(w, Me, ax, az), "the barricade the ghost showed is placed");
            bool found = false;
            foreach (Barricade x in w.Barricades) if (x.Active && x.Cx == cx && x.Cz == cz) found = true;
            Assert.IsTrue(found, "on the ghost's cell (" + cx + ", " + cz + ")");
        }

        // ------------------------------------------------------------------ Builder

        [Test]
        public void BuilderBuildTargetIsWhatBuildDoes()
        {
            World w = Field(ClassId.Builder);
            var scan = new AffordanceScan();
            Assert.IsTrue(FindSpot(w, scan, s => s.Primary.Action == AffAction.Build && s.Primary.Valid, 60), "a cliff or gap to build across");
            AffTarget p = scan.Primary;
            Assert.AreEqual(3, p.Ghost);
            Assert.Greater(p.GhostCount, 0, "the ghost shows the piece's cells");
            // The build markers include the piece in front of him.
            Assert.Greater(Cells(scan, w, AffAction.Build).Count, 0);
            Actor a = w.Actors[Me];
            int code = BuiltPieces.Build(w, Me, w.X[Me] + Math.Cos(a.Facing) * 2, w.Z[Me] + Math.Sin(a.Facing) * 2);
            Assert.IsTrue(code == BuiltPieces.BuiltLadderCode || code == BuiltPieces.BuiltBridgeCode, "Build succeeded (" + code + ")");

            // Bank empty: the same spot refuses, and says why.
            World w2 = Field(ClassId.Builder);
            var scan2 = new AffordanceScan();
            Assert.IsTrue(FindSpot(w2, scan2, s => s.Primary.Action == AffAction.Build && s.Primary.Valid, 60));
            w2.Builder.Spent[(int)w2.Actors[Me].Team] += 1e6;
            scan2.Scan(w2, Me, false, double.NaN, double.NaN, false);
            Assert.IsFalse(scan2.Primary.Valid, "no wood, no build");
            Assert.AreEqual(AffWords.BankEmpty, scan2.Primary.Why);
            Assert.AreNotEqual(BuiltPieces.BuiltLadderCode, BuiltPieces.Build(w2, Me, w2.X[Me] + Math.Cos(w2.Actors[Me].Facing) * 2, w2.Z[Me] + Math.Sin(w2.Actors[Me].Facing) * 2));
        }

        // ------------------------------------------------------------------ Farmer

        [Test]
        public void FarmerMarkersAreTheNodesTheSimLetsHimGather()
        {
            World w = Field(ClassId.Farmer);
            var scan = new AffordanceScan();
            Assert.IsTrue(FindSpot(w, scan, s => { for (int k = 0; k < s.MarkerCount; k++) if (s.Markers[k].Action == AffAction.Gather) return true; return false; }, 60), "a node nearby");
            Actor a = w.Actors[Me];
            var marked = new HashSet<long>();
            for (int k = 0; k < scan.MarkerCount; k++)
                if (scan.Markers[k].Action == AffAction.Gather) marked.Add(((long)Math.Round(scan.Markers[k].X * 8) << 20) ^ (long)Math.Round(scan.Markers[k].Z * 8));
            int expected = 0;
            foreach (ResourceNode n in w.Nodes)
            {
                double dx = n.X - w.X[Me], dz = n.Z - w.Z[Me];
                double rr = AffordanceScan.Radius + w.Config.Resources.NodeInteractRange + Collide.ActorRadius(w, Me);
                if (dx * dx + dz * dz > rr * rr) continue;
                bool takes = SimResources.NodeTakes(w, a, n) && a.CarryAmount < w.Config.Class(a.Cls).CarryCapacity;
                long key = ((long)Math.Round(n.X * 8) << 20) ^ (long)Math.Round(n.Z * 8);
                if (takes) { expected++; if (!scan.Capped) Assert.IsTrue(marked.Contains(key), "a node he can gather is not marked"); }
                else Assert.IsFalse(marked.Contains(key), "a node he cannot gather is marked");
            }
            Assert.Greater(expected, 0);
            // Full hands: nothing to gather is marked.
            a.CarryAmount = (int)w.Config.Class(a.Cls).CarryCapacity;
            a.Carry = CarryKind.Biomass;
            scan.Scan(w, Me, false, double.NaN, double.NaN, false);
            Assert.AreEqual(0, CountOf(scan, AffAction.Gather), "full hands: no gather markers");
        }

        static int CountOf(AffordanceScan s, AffAction act)
        {
            int n = 0;
            for (int k = 0; k < s.MarkerCount; k++) if (s.Markers[k].Action == act) n++;
            return n;
        }

        // ------------------------------------------------------------------ Demolitions

        [Test]
        public void DemolitionsMarksWhatABlastBreaksAndShowsTheBlast()
        {
            World w = Field(ClassId.Demolitions);
            var scan = new AffordanceScan();
            scan.Scan(w, Me, false, double.NaN, double.NaN, true);
            Assert.AreEqual(AffAction.Tnt, scan.Primary.Action);
            Assert.AreEqual(5, scan.Primary.Ghost);
            Assert.AreEqual(w.Config.Abilities.DemolitionsTnt.BlastRadius, scan.Primary.GhostRadius, 1e-9);
            for (int k = 0; k < scan.MarkerCount; k++)
            {
                AffMarker mk = scan.Markers[k];
                if (mk.Action != AffAction.Tnt || mk.Cx < 0) continue;
                byte t = w.Map.Cells[mk.Cz * w.Map.Width + mk.Cx];
                bool barricade = false;
                foreach (Barricade b in w.Barricades) if (b.Active && b.Cx == mk.Cx && b.Cz == mk.Cz && b.Team != w.Actors[Me].Team) barricade = true;
                Assert.IsTrue(t == Cell.BreakableIntact || t == Cell.BreakableCracked || barricade, "TNT marks only breakables");
            }
            Assert.AreEqual(AffAction.Mine, scan.Secondary.Action);
            Assert.AreEqual(4, scan.Secondary.Ghost);
        }

        [Test]
        public void NonTerrainClassesGetNothing()
        {
            World w = Field(ClassId.Warrior);
            var scan = new AffordanceScan();
            Assert.IsFalse(scan.Scan(w, Me, false, double.NaN, double.NaN, false));
            Assert.AreEqual(0, scan.MarkerCount);
            Assert.IsFalse(scan.Primary.Has);
            Assert.IsFalse(AffordanceScan.IsTerrainClass(ClassId.Warrior));
            Assert.IsTrue(AffordanceScan.IsTerrainClass(ClassId.Farmer));
        }

        // ------------------------------------------------------------------ prompt

        [Test]
        public void PromptNamesTheKeyTheActionAndTheReason()
        {
            var t = new AffTarget();
            t.Clear();
            t.Has = true;
            t.Button = "DigPlace";
            t.Verb = "PLACE BLOCK";
            t.Count = 3;
            t.Valid = true;
            string kb = TerrainAffordanceView.Line(t, ControlDevice.KeyboardMouse);
            string pad = TerrainAffordanceView.Line(t, ControlDevice.Gamepad);
            StringAssert.Contains("PLACE BLOCK (3)", kb);
            StringAssert.StartsWith(TerrainAffordanceView.Glyph("DigPlace", ControlDevice.KeyboardMouse), kb, "the key comes first");
            Assert.AreNotEqual(kb, pad, "the prompt follows the device");
            t.Valid = false;
            t.Why = AffWords.TooFar;
            StringAssert.Contains("too far", TerrainAffordanceView.Line(t, ControlDevice.KeyboardMouse));
            t.Hold = true;
            StringAssert.StartsWith("HOLD ", TerrainAffordanceView.Line(t, ControlDevice.Gamepad));
            int key = t.TextKey();
            t.Count = 2;
            Assert.AreNotEqual(key, t.TextKey(), "a count change rebuilds the text");
        }

        [Test]
        public void ShowTerrainHasADefaultKeyAndPadButton()
        {
            Assert.Contains("ShowTerrain", InputGlyphs.PlayerActions);
            Assert.IsTrue(ControlSchemes.MayShareKey("OpenStation", "ShowTerrain"), "R3 is shared with the station key by context");
        }

        // ------------------------------------------------------------------ settings

        [Test]
        public void WorldFeedbackSwitchesDefaultOnAndTurnOff()
        {
            WorldFeedbackSettings.ResetAll();
            Assert.IsTrue(WorldFeedbackSettings.Prompts && WorldFeedbackSettings.Outline && WorldFeedbackSettings.Markers
                          && WorldFeedbackSettings.Progress && WorldFeedbackSettings.Shake && WorldFeedbackSettings.Particles);
            Assert.AreEqual(WorldFeedbackSettings.HintsFirstMatches, WorldFeedbackSettings.Hints);
            WorldFeedbackSettings.Prompts = false;
            WorldFeedbackSettings.Outline = false;
            WorldFeedbackSettings.Markers = false;
            WorldFeedbackSettings.Progress = false;
            WorldFeedbackSettings.Shake = false;
            WorldFeedbackSettings.Particles = false;
            Assert.IsFalse(WorldFeedbackSettings.Prompts || WorldFeedbackSettings.Outline || WorldFeedbackSettings.Markers
                           || WorldFeedbackSettings.Progress || WorldFeedbackSettings.Shake || WorldFeedbackSettings.Particles);
            WorldFeedbackSettings.ResetAll();
            Assert.IsTrue(WorldFeedbackSettings.Markers);
        }

        [Test]
        public void HintsShowForTheFirstTwoMatchesPerClass()
        {
            WorldFeedbackSettings.ResetAll();
            Assert.IsTrue(WorldFeedbackSettings.HintDue(1));
            WorldFeedbackSettings.NoteHinted(1);
            Assert.IsTrue(WorldFeedbackSettings.HintDue(1));
            WorldFeedbackSettings.NoteHinted(1);
            Assert.IsFalse(WorldFeedbackSettings.HintDue(1), "two matches hinted");
            Assert.IsTrue(WorldFeedbackSettings.HintDue(2), "per class");
            WorldFeedbackSettings.Hints = WorldFeedbackSettings.HintsAlways;
            Assert.IsTrue(WorldFeedbackSettings.HintDue(1));
            WorldFeedbackSettings.Hints = WorldFeedbackSettings.HintsOff;
            Assert.IsFalse(WorldFeedbackSettings.HintDue(2));
        }

        // ------------------------------------------------------------------ damage stages, reactions, progress

        [Test]
        public void StagesFollowTheHealthThresholds()
        {
            Assert.AreEqual(0, WorldReactions.StageOf(1.0));
            Assert.AreEqual(1, WorldReactions.StageOf(0.9));
            Assert.AreEqual(1, WorldReactions.StageOf(0.67));
            Assert.AreEqual(2, WorldReactions.StageOf(2.0 / 3.0));
            Assert.AreEqual(2, WorldReactions.StageOf(0.34));
            Assert.AreEqual(3, WorldReactions.StageOf(1.0 / 3.0));
            Assert.AreEqual(3, WorldReactions.StageOf(0.01));
        }

        [Test]
        public void BarricadeHitsReactAndAdvanceStagesThenBreak()
        {
            World w = Field(ClassId.Miner);
            var scan = new AffordanceScan();
            Assert.IsTrue(FindSpot(w, scan, s => s.Secondary.Has && s.Secondary.Valid && s.Secondary.Action == AffAction.Barricade));
            Actor a = w.Actors[Me];
            double r = w.Config.Abilities.MinerBarricade.PlaceRange;
            var rx = new WorldReactions();
            rx.Read(w, 0);
            Assert.IsTrue(Barricades.TryPlaceBarricade(w, Me, w.X[Me] + Math.Cos(a.Facing) * r, w.Z[Me] + Math.Sin(a.Facing) * r));
            int slot = -1;
            for (int i = 0; i < w.Barricades.Length; i++) if (w.Barricades[i].Active) slot = i;
            rx.Read(w, 0.5);
            Assert.AreEqual(1, rx.Builds, "placing raises a build pulse");
            Assert.AreEqual(0, WorldReactions.StageOf(WorldReactions.BarricadeLeft01(w, slot)));
            double max = w.Config.Abilities.MinerBarricade.MaxHp;
            int[] expect = { 1, 2, 3 };
            double[] left = { 0.8, 0.5, 0.2 };
            for (int k = 0; k < left.Length; k++)
            {
                w.Barricades[slot].Hp = max * left[k];
                rx.Read(w, k + 1);
                Assert.AreEqual(k + 1, rx.Hits, "each health drop raises one hit reaction");
                Assert.AreEqual(expect[k], WorldReactions.StageOf(WorldReactions.BarricadeLeft01(w, slot)));
            }
            w.Barricades[slot].Hp = 0;
            Barricades.BarricadesSystem(w, 1.0 / w.Config.SimHz);
            rx.Read(w, 5);
            Assert.AreEqual(1, rx.Breaks, "the break raises a break burst");
            Assert.AreEqual(-1, WorldReactions.BarricadeLeft01(w, slot));
        }

        [Test]
        public void WallHitStagesAndProgressAreTheSims()
        {
            World w = Field(ClassId.Miner);
            MapDef m = w.Map;
            int wall = -1;
            for (int i = 0; i < m.Cells.Length && wall < 0; i++) if (m.Cells[i] == Cell.BreakableIntact) wall = i;
            if (wall < 0) Assert.Ignore("this map has no breakable walls");
            double hps = w.Config.Abilities.MinerWallBreak.HitsPerState;
            Assert.AreEqual(1.0, WorldReactions.WallLeft01(w, wall), 1e-9);
            Assert.AreEqual((int)Math.Round(2 * hps), WorldReactions.WallHitsLeft(w, wall));
            var rx = new WorldReactions();
            rx.Read(w, 9);
            // One hit, as Abilities.MinerWallBreak writes it: the state drops to cracked, hp to HitsPerState.
            m.WallHp[wall] = (byte)hps;
            m.Cells[wall] = Cell.BreakableCracked;
            World.PushEvent(w, SimEventType.WallBreak, Me, wall, wall % m.Width + 0.5, wall / m.Width + 0.5, Cell.BreakableCracked);
            rx.Read(w, 10);
            Assert.AreEqual(1, rx.Hits);
            Assert.AreEqual(wall, rx.LocalWallCell);
            Assert.AreEqual(WorldReactions.StageOf(0.5), WorldReactions.StageOf(WorldReactions.WallLeft01(w, wall)));
            var focus = new ProgressFocus();
            focus.Update(w, Me, rx, 10.5);
            Assert.IsTrue(focus.Has);
            Assert.AreEqual(ReactKind.Wall, focus.Kind);
            Assert.AreEqual(WorldReactions.WallLeft01(w, wall), focus.Value01, 1e-9, "the bar is the Sim's wall health");
            Assert.AreEqual(Math.Max(0, 0.5 - 1.0 / (2 * hps)), focus.Next01, 1e-9, "the ghost segment is one more hit");
            Assert.AreEqual(WorldReactions.WallHitsLeft(w, wall), focus.HitsLeft);
            focus.Update(w, Me, rx, 10 + ProgressFocus.FocusSeconds + 1);
            Assert.IsFalse(focus.Has, "the bar lets go a few seconds after the last hit");
        }

        [Test]
        public void PillarHitsShowSwingsLeft()
        {
            World w = Field(ClassId.Warrior);
            if (w.Map.Decorations == null || w.Map.Decorations.Length == 0 || w.Topple == null) Assert.Ignore("no pillars");
            int k = 0;
            DecorationDef d = w.Map.Decorations[k];
            var rx = new WorldReactions();
            rx.Read(w, 0.5);
            w.Topple.Hp[k] = (byte)Math.Max(1, w.Topple.Hp[k] - 1);
            World.PushEvent(w, SimEventType.ToppleHit, Me, k, d.Cx + 0.5, d.Cz + 0.5, w.Topple.Hp[k]);
            rx.Read(w, 1);
            Assert.AreEqual(1, rx.Hits);
            var focus = new ProgressFocus();
            focus.Update(w, Me, rx, 1.2);
            Assert.IsTrue(focus.Has);
            Assert.AreEqual(ReactKind.Pillar, focus.Kind);
            Assert.AreEqual(w.Topple.Hp[k], focus.HitsLeft);
            Assert.AreEqual(w.Topple.Hp[k] / (double)w.Config.Abilities.Topple.HitsToTopple, focus.Value01, 1e-9);
        }

        [Test]
        public void GatherProgressIsTheSimsTimer()
        {
            World w = Field(ClassId.Farmer);
            if (w.Nodes == null || w.Nodes.Length == 0) Assert.Ignore("no nodes");
            Actor a = w.Actors[Me];
            double need = w.Config.Class(a.Cls).GatherSeconds;
            a.GatherNodeId = 0;
            a.GatherProgress = need * 0.25;
            var focus = new ProgressFocus();
            focus.Update(w, Me, new WorldReactions(), 0);
            Assert.IsTrue(focus.Has && focus.Filling);
            Assert.AreEqual(0.25, focus.Value01, 1e-9);
            Assert.AreEqual(need * 0.75, focus.SecondsLeft, 1e-9);
        }

        [Test]
        public void PiecesBuildAndBreakSequencesRaiseTheirBursts()
        {
            World w = Field(ClassId.Builder);
            var rx = new WorldReactions();
            rx.Read(w, 0);
            World.PushEvent(w, SimEventType.PiecePlaced, Me, 0, w.X[Me], w.Z[Me], 0, null, DamageCause.None, 2);
            World.PushEvent(w, SimEventType.Terraform, Me, 5, w.X[Me], w.Z[Me], Terraform.Placed);
            World.PushEvent(w, SimEventType.PieceDestroyed, Me, 0, w.X[Me], w.Z[Me], 0, null, DamageCause.None, 2);
            World.PushEvent(w, SimEventType.Terraform, Me, 5, w.X[Me], w.Z[Me], Terraform.Dug);
            rx.Read(w, 1);
            Assert.AreEqual(2, rx.Builds);
            Assert.AreEqual(2, rx.Breaks);
            int live = 0;
            foreach (ReactPulse p in rx.Pulses) if (1 - p.T0 >= 0 && 1 - p.T0 < WorldReactions.Life(p.Phase)) live++;
            Assert.AreEqual(4, live);
            int alive = 0;
            foreach (ReactPulse p in rx.Pulses) if (5 - p.T0 >= 0 && 5 - p.T0 < WorldReactions.Life(p.Phase)) alive++;
            Assert.AreEqual(0, alive, "every burst completes");
        }

        // ------------------------------------------------------------------ b12-jk signs (Jeisil's k)

        static void SignLog(string s) => Debug.Log("[CM.Signs] " + s);

        static string Fd(double v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The marker scan and the sign pass together, run until the dig route proofs are all cached.</summary>
        static void SignWarm(World w, AffordanceScan scan, SignScan signs)
        {
            for (int k = 0; k < 600; k++)
            {
                bool terrain = AffordanceScan.IsTerrainClass(w.Actors[Me].Cls);
                if (terrain) scan.Scan(w, Me, false, double.NaN, double.NaN, false);
                signs.Scan(w, Me, terrain ? scan : null);
            }
        }

        /// <summary>Paints every breakable wall within r of actor 0 back to floor (a clean scene for the sign counts).</summary>
        static void ClearBreakables(World w, double r)
        {
            MapDef m = w.Map;
            for (int cz = 0; cz < m.Height; cz++)
            for (int cx = 0; cx < m.Width; cx++)
            {
                int i = cz * m.Width + cx;
                byte t = m.Cells[i];
                if (t != Cell.BreakableIntact && t != Cell.BreakableCracked) continue;
                double dx = cx + 0.5 - w.X[Me], dz = cz + 0.5 - w.Z[Me];
                if (dx * dx + dz * dz <= r * r) m.Cells[i] = Cell.Floor;
            }
        }

        /// <summary>Floor cells around actor 0 between rMin and rMax u, nearest first (ring order), none twice.</summary>
        static List<int> FloorNear(World w, double rMin, double rMax, int want)
        {
            MapDef m = w.Map;
            var list = new List<int>();
            int sx = JsMath.FloorToInt(w.X[Me]), sz = JsMath.FloorToInt(w.Z[Me]);
            for (int ring = 1; ring <= (int)Math.Ceiling(rMax) + 1 && list.Count < want; ring++)
            for (int cz = sz - ring; cz <= sz + ring && list.Count < want; cz++)
            for (int cx = sx - ring; cx <= sx + ring && list.Count < want; cx++)
            {
                if (Math.Abs(cx - sx) != ring && Math.Abs(cz - sz) != ring) continue;
                if (!MapGrid.InBounds(m, cx, cz) || m.Cells[cz * m.Width + cx] != Cell.Floor) continue;
                double dx = cx + 0.5 - w.X[Me], dz = cz + 0.5 - w.Z[Me], d = Math.Sqrt(dx * dx + dz * dz);
                if (d < rMin || d > rMax) continue;
                list.Add(cz * m.Width + cx);
            }
            return list;
        }

        [Test]
        public void MinerGetsPickSignsOnlyOnDiggableCells()
        {
            World w = Field(ClassId.Miner);
            var scan = new AffordanceScan();
            var signs = new SignScan();
            Assert.IsTrue(FindSpot(w, scan, s => s.Primary.Valid), "a diggable spot");
            SignWarm(w, scan, signs);
            MapDef m = w.Map;
            int barricadeCell = scan.Secondary.Has && scan.Secondary.Valid ? scan.Secondary.Cz * m.Width + scan.Secondary.Cx : -1;
            int picks = signs.Count[(int)SignKind.Pick];
            SignLog("miner: " + picks + " pick signs of " + signs.Candidates[(int)SignKind.Pick] + " candidates (limit 1.." + GrenadeTuning.SignsPerKind
                    + "), hammer " + signs.Count[(int)SignKind.Hammer] + " (limit 0)");
            Assert.Greater(picks, 0, "the Miner sees pick signs");
            Assert.LessOrEqual(picks, GrenadeTuning.SignsPerKind);
            Assert.AreEqual(0, signs.Count[(int)SignKind.Hammer], "no hammer signs for the Miner (own-class build signs only)");
            for (int i = 0; i < picks; i++)
            {
                AffSign s = signs.Get(SignKind.Pick, i);
                int cx = s.Cell % m.Width, cz = s.Cell / m.Width;
                int dig = Terraform.DigRefusal(w, cx, cz);
                int bar = s.Cell == barricadeCell ? Barricades.BarricadeRefusal(w, Me, s.X, s.Z) : -99;
                SignLog("  pick at (" + cx + ", " + cz + ") " + Fd(s.Dist) + " u: dig refusal " + dig + ", barricade refusal " + (bar == -99 ? "-" : bar.ToString()) + " (limit one of them 0)");
                Assert.IsTrue(dig == 0 || bar == 0, "a pick sign marks a cell the Sim lets him dig or barricade");
            }
        }

        [Test]
        public void BuilderGetsHammerSignsOnlyOnBuildableSpots()
        {
            World w = Field(ClassId.Builder);
            var scan = new AffordanceScan();
            var signs = new SignScan();
            Assert.IsTrue(FindSpot(w, scan, s => s.Primary.Has && s.Primary.Action == AffAction.Build && s.Primary.Valid), "a spot with a valid build");
            signs.Scan(w, Me, scan);
            var buildable = new HashSet<int>();
            int dirs = 0;
            for (int k = 0; k < 4; k++)
            {
                int dx = k == 0 ? 1 : k == 1 ? -1 : 0, dz = k == 2 ? 1 : k == 3 ? -1 : 0;
                if (BuiltPieces.BuildRefusal(w, Me, dx, dz) != 0) continue;
                dirs++;
                for (int q = 0; q < BuiltPieces.LastPlanCount; q++) buildable.Add(BuiltPieces.LastPlanCell(q));
            }
            int hammers = signs.Count[(int)SignKind.Hammer];
            SignLog("builder: " + hammers + " hammer signs for " + dirs + " buildable directions (limit 1.." + dirs + "), pick " + signs.Count[(int)SignKind.Pick] + " (limit 0)");
            Assert.Greater(hammers, 0, "the Builder sees a hammer sign");
            Assert.LessOrEqual(hammers, dirs, "one hammer per buildable ladder or bridge");
            Assert.AreEqual(0, signs.Count[(int)SignKind.Pick], "no pick signs for the Builder");
            for (int i = 0; i < hammers; i++)
            {
                AffSign s = signs.Get(SignKind.Hammer, i);
                SignLog("  hammer at cell " + s.Cell + " " + Fd(s.Dist) + " u: in a plan the Sim accepts -> " + buildable.Contains(s.Cell) + " (limit true)");
                Assert.IsTrue(buildable.Contains(s.Cell), "a hammer sign sits on a ladder or bridge the Sim would build");
            }
        }

        [Test]
        public void EveryClassGetsCrackSignsOnWhatItCanTakeDown()
        {
            foreach (ClassId cls in ClassIds.All)
            {
                World w = Field(cls);
                MapDef m = w.Map;
                ClearBreakables(w, 16);
                List<int> cells = FloorNear(w, 2.5, 7, 3);
                Assert.AreEqual(3, cells.Count, "three floor cells near " + cls);
                Assert.GreaterOrEqual(w.Barricades.Length, 2);
                TeamId mine = w.Actors[Me].Team, theirs = mine == TeamId.Azure ? TeamId.Crimson : TeamId.Azure;
                Barricade enemy = w.Barricades[0], own = w.Barricades[1];
                enemy.Active = true; enemy.Team = theirs; enemy.Cx = cells[0] % m.Width; enemy.Cz = cells[0] / m.Width; enemy.Hp = 50;
                own.Active = true; own.Team = mine; own.Cx = cells[1] % m.Width; own.Cz = cells[1] / m.Width; own.Hp = 50;
                m.Cells[cells[0]] = Cell.Barricade;
                m.Cells[cells[1]] = Cell.Barricade;
                m.Cells[cells[2]] = Cell.BreakableIntact;
                var scan = new AffordanceScan();
                var signs = new SignScan();
                SignWarm(w, scan, signs);
                bool atEnemy = false, atOwn = false, atWall = false;
                for (int i = 0; i < signs.Count[(int)SignKind.Crack]; i++)
                {
                    int c = signs.Get(SignKind.Crack, i).Cell;
                    atEnemy |= c == cells[0];
                    atOwn |= c == cells[1];
                    atWall |= c == cells[2];
                }
                bool wallRule = SignScan.TakesDownWall(cls);
                SignLog(cls + ": crack on enemy barricade " + atEnemy + " (limit true), on own barricade " + atOwn + " (limit false), on breakable wall "
                        + atWall + " (limit " + wallRule + "); crack signs " + signs.Count[(int)SignKind.Crack]);
                Assert.IsTrue(atEnemy, cls + " can chip an enemy barricade: crack sign");
                Assert.IsFalse(atOwn, cls + " never gets a take-down sign on its own team's barricade");
                Assert.AreEqual(wallRule, atWall, cls + ": crack on a breakable wall exactly when the class can break one");
                if (cls != ClassId.Miner) Assert.AreEqual(0, signs.Count[(int)SignKind.Pick], cls + ": pick signs are the Miner's only");
                if (cls != ClassId.Builder) Assert.AreEqual(0, signs.Count[(int)SignKind.Hammer], cls + ": hammer signs are the Builder's only");
            }
        }

        [Test]
        public void SignsKeepTheNearestEightAndNothingPast14Units()
        {
            World w = Field(ClassId.Demolitions);
            MapDef m = w.Map;
            ClearBreakables(w, 21);
            List<int> cells = FloorNear(w, 1.5, 19, 400);
            var painted = new List<int>();
            for (int k = 0; k < cells.Count; k += 3) { m.Cells[cells[k]] = Cell.BreakableIntact; painted.Add(cells[k]); }
            var signs = new SignScan();
            signs.Scan(w, Me, null);
            int n = signs.Count[(int)SignKind.Crack];
            double maxKept = 0;
            var kept = new HashSet<int>();
            for (int i = 0; i < n; i++) { AffSign s = signs.Get(SignKind.Crack, i); kept.Add(s.Cell); maxKept = Math.Max(maxKept, s.Dist); }
            int within = 0, beyond = 0;
            double minLeftOut = double.MaxValue;
            foreach (int c in painted)
            {
                double dx = c % m.Width + 0.5 - w.X[Me], dz = c / m.Width + 0.5 - w.Z[Me], d = Math.Sqrt(dx * dx + dz * dz);
                if (d <= GrenadeTuning.SignRadius) within++; else beyond++;
                if (d > GrenadeTuning.SignRadius) Assert.IsFalse(kept.Contains(c), "no sign past " + GrenadeTuning.SignRadius + " u");
                else if (!kept.Contains(c)) minLeftOut = Math.Min(minLeftOut, d);
            }
            SignLog("crowd: " + painted.Count + " breakable walls (" + within + " within 14 u, " + beyond + " beyond), signs kept " + n + " (limit "
                    + GrenadeTuning.SignsPerKind + "), farthest kept " + Fd(maxKept) + " u (limit <= nearest left out " + (minLeftOut == double.MaxValue ? "-" : Fd(minLeftOut)) + " u and <= 14)");
            Assert.Greater(within, GrenadeTuning.SignsPerKind, "the scene has more walls in range than the cap");
            Assert.Greater(beyond, 0, "the scene has walls past the sign radius");
            Assert.AreEqual(GrenadeTuning.SignsPerKind, n, "only the nearest " + GrenadeTuning.SignsPerKind + " show");
            Assert.LessOrEqual(maxKept, GrenadeTuning.SignRadius);
            Assert.LessOrEqual(maxKept, minLeftOut + 1e-9, "the kept signs are the nearest ones");
            for (int i = 1; i < n; i++) Assert.LessOrEqual(signs.Get(SignKind.Crack, i - 1).Dist, signs.Get(SignKind.Crack, i).Dist, "nearest first");
            float f0 = SignScan.Fade(GrenadeTuning.SignFadeStart), f1 = SignScan.Fade(GrenadeTuning.SignRadius - 0.5), f2 = SignScan.Fade(GrenadeTuning.SignRadius);
            SignLog("fade: at " + Fd(GrenadeTuning.SignFadeStart) + " u " + Fd(f0) + " (limit 1), at " + Fd(GrenadeTuning.SignRadius - 0.5) + " u " + Fd(f1)
                    + " (limit 0..1), at 14 u " + Fd(f2) + " (limit 0)");
            Assert.AreEqual(1f, f0, 1e-6f);
            Assert.That(f1, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.AreEqual(0f, f2, 1e-6f, "gone at 14 u");
        }

        [Test]
        public void SignsSwitchDefaultsOnAndTurnsOff()
        {
            WorldFeedbackSettings.ResetAll();
            bool def = WorldFeedbackSettings.Signs;
            WorldFeedbackSettings.Signs = false;
            bool off = WorldFeedbackSettings.Signs;
            SignLog("signs switch: default " + def + " (limit true), after turning off " + off + " (limit false), in reset keys "
                    + Array.IndexOf(WorldFeedbackSettings.AllKeys, WorldFeedbackSettings.SignsKey) + " (limit >= 0)");
            Assert.IsTrue(def);
            Assert.IsFalse(off);
            Assert.Contains(WorldFeedbackSettings.SignsKey, WorldFeedbackSettings.AllKeys, "RESET THESE OPTIONS clears it");
        }

        [Test]
        public void AtlasBakesAllCells()
        {
            Texture2D t = TerrainAffordanceView.BakeAtlas();
            try
            {
                Assert.AreEqual(256, t.width);
                Assert.AreEqual(192, t.height);
                Color32[] px = t.GetPixels32();
                for (int cell = 0; cell < 12; cell++)
                {
                    int ox = (cell % 4) * 64, oy = (cell / 4) * 64, cover = 0;
                    for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) if (px[(oy + y) * 256 + ox + x].a > 32) cover++;
                    Assert.Greater(cover, 40, "atlas cell " + cell + " draws something");
                    Assert.Less(cover, 64 * 64, "atlas cell " + cell + " is not a solid block");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(t);
            }
        }
    }
}
