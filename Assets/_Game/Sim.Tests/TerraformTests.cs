using System;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// CM-30 miner dig / place: port of src/sim/terraform.test.ts (09e0904, 11 cases) on a hand-painted strip (pad island,
    /// 3-wide bridge, hill island, nothing else walkable) plus one short bot run on the real map, then the DG-09 additions
    /// (U-053): dig / place mode and its switch, CarryMax, the fall rule. The ported cases run with CarryMax 1, which
    /// replays the browser's one-block carry exactly. All time is sim ticks.
    /// </summary>
    [TestFixture]
    public sealed class TerraformTests
    {
        private const double PadX = 5.5, PadZ = 13.5, HillX = 36.5, HillZ = 13.5;
        private const int Miner = 0;

        private static GameConfig Cfg(int carryMax)
        {
            GameConfig c = GameConfig.CreateBrowser5v5();
            c.Abilities.MinerTerraform.CarryMax = carryMax;
            return c;
        }

        private static MinerTerraformConfig MT => GameConfig.CreateBrowser5v5().Abilities.MinerTerraform;

        private static World Make(int seed, ClassId cls, int carryMax = 1, int teamSize = 5)
        {
            return World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = teamSize, HumanTeam = TeamId.Azure, HumanClass = cls, Config = Cfg(carryMax) });
        }

        private static void Paint(World w, int x0, int z0, int x1, int z1, byte t)
        {
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) w.Map.Cells[z * w.Map.Width + x] = t;
        }

        /// <summary>
        /// Pad island x 2..8, bridge x 9..29 on rows 12..14 (3 wide), hill island x 30..42. Both pads on the pad island, the
        /// shrine in the void (only the hill clause binds), nodes parked off the map, every actor but the miner dead, the
        /// miner on the bridge at (19.3, 13.5), tick 100.
        /// </summary>
        internal static World Strip(int carryMax = 1)
        {
            World w = Make(30, ClassId.Miner, carryMax);
            MapDef map = w.Map;
            Array.Fill(map.Cells, Cell.Void);
            Paint(w, 2, 10, 8, 16, Cell.Floor);
            Paint(w, 9, 12, 29, 14, Cell.Bridge);
            Paint(w, 30, 8, 42, 18, Cell.Floor);
            for (int t = 0; t < 2; t++)
            {
                map.Pads[t].X = PadX;
                map.Pads[t].Z = PadZ;
            }
            map.Hill.X = HillX;
            map.Hill.Z = HillZ;
            map.Hill.R = 3;
            map.Shrine.X = 100.5;
            map.Shrine.Z = 60.5;
            foreach (ResourceNode nd in w.Nodes)
            {
                nd.X = -100;
                nd.Z = -100;
            }
            for (int j = 1; j < w.ActorCount; j++)
            {
                w.Actors[j].Alive = false;
                w.Actors[j].RespawnIn = 1e9;
            }
            w.X[Miner] = 19.3;
            w.Z[Miner] = 13.5;
            w.Tick = 100;
            Assert.AreEqual(ClassId.Miner, w.Actors[Miner].Cls);
            return w;
        }

        private static byte CellAt(World w, int cx, int cz) => w.Map.Cells[cz * w.Map.Width + cx];

        private static SimEvent LastEvent(World w) => w.Events[(w.EventHead - 1) % World.EventCapacity];

        private static double PadDist(World w)
        {
            FlowField f = FlowField.ComputeFlowField(w.Map, new[] { new PointXZ(HillX, HillZ) });
            return f.Dist[(int)Math.Floor(PadZ) * w.Map.Width + (int)Math.Floor(PadX)];
        }

        /// <summary>Advance past both rate caps (team cooldown and the world-wide gap).</summary>
        private static void Cool(World w)
        {
            w.Tick += (int)Math.Max(MT.TeamCooldownTicks, MT.MinGapTicks);
        }

        private static int FindActor(World w, Func<int, bool> pred)
        {
            for (int i = 1; i < w.ActorCount; i++) if (pred(i)) return i;
            return -1;
        }

        // ------------------------------------------------------------------------------------------- ported (09e0904)

        [Test]
        public void ADigTurnsTheCellVoidTheMinerCarriesOneBlockWallVersionBumpsTheFieldRoutesAround()
        {
            World w = Strip();
            double before = PadDist(w);
            Assert.IsTrue(double.IsFinite(before));
            int wv = w.WallVersion;
            Assert.AreEqual(Terraform.Dug, Terraform.Dig(w, Miner, 20.5, 13.5));
            Assert.AreEqual(Cell.Void, CellAt(w, 20, 13));
            Assert.AreEqual(1, w.Terraform.Carried[Miner]);
            Assert.AreEqual(wv + 1, w.WallVersion);
            SimEvent e = LastEvent(w);
            Assert.AreEqual(SimEventType.Terraform, e.Type);
            Assert.AreEqual(Miner, e.A);
            Assert.AreEqual(13 * w.Map.Width + 20, e.B);
            Assert.AreEqual(Terraform.Dug, e.Value);
            double around = PadDist(w);
            Assert.IsTrue(double.IsFinite(around));
            Assert.Greater(around, before + 0.5, "detour around the hole (no corner cutting)");
            // Carrying (CarryMax 1): a second dig is refused, nothing changes.
            Cool(w);
            Assert.AreEqual(Terraform.RefusedTarget, Terraform.Dig(w, Miner, 20.5, 12.5));
            Assert.AreEqual(Cell.Bridge, CellAt(w, 20, 12));
        }

        [Test]
        public void APlaceTurnsAVoidCellNextToWalkableGroundFloorAndTheFieldRoutesAcrossTheBlock()
        {
            World w = Strip();
            double before = PadDist(w);
            Assert.AreEqual(Terraform.Dug, Terraform.Dig(w, Miner, 20.5, 13.5));
            double holed = PadDist(w);
            Cool(w);
            int wv = w.WallVersion;
            Assert.AreEqual(Terraform.Placed, Terraform.Place(w, Miner, 20.5, 13.5));
            Assert.AreEqual(Cell.Floor, CellAt(w, 20, 13));
            Assert.AreEqual(0, w.Terraform.Carried[Miner]);
            Assert.AreEqual(wv + 1, w.WallVersion);
            Assert.AreEqual(Terraform.Placed, LastEvent(w).Value);
            double after = PadDist(w);
            Assert.Less(after, holed - 0.5);
            Assert.AreEqual(before, after, 1e-4);
            FlowField f = FlowField.ComputeFlowField(w.Map, new[] { new PointXZ(HillX, HillZ) });
            Assert.IsTrue(FlowField.SampleFlowDir(f, w.Map, 19.5, 13.5, out double dx, out double dz));
            Assert.AreEqual(1, dx);
            Assert.AreEqual(0, dz);
        }

        [Test]
        public void APlaceTurnsWaterNextToGroundFloorAndVoidWithNoWalkableNeighbourOrGroundIsRefused()
        {
            World w = Strip();
            Paint(w, 24, 11, 24, 11, Cell.Water);
            w.X[Miner] = 23.5;
            w.Z[Miner] = 12.5;
            w.Terraform.Carried[Miner] = 1;
            Assert.AreEqual(Terraform.RefusedTarget, Terraform.PlaceRefusal(w, 24, 30), "void, no walkable neighbour");
            Assert.AreEqual(Terraform.RefusedTarget, Terraform.PlaceRefusal(w, 23, 12), "already ground");
            int wv = w.WallVersion;
            Assert.AreEqual(Terraform.RefusedTarget, Terraform.Place(w, Miner, 23.5, 12.5));
            Assert.AreEqual(1, w.Terraform.Carried[Miner]);
            Assert.AreEqual(wv, w.WallVersion);
            Assert.IsTrue(Terraform.FindPlaceCell(w, Miner, out _, out _));
            Assert.AreEqual(Terraform.Placed, Terraform.Place(w, Miner, 24.5, 11.5));
            Assert.AreEqual(Cell.Floor, CellAt(w, 24, 11));
            Assert.AreEqual(wv + 1, w.WallVersion);
        }

        [Test]
        public void KeepOutsNoDigInsideThePadKeepOutUnderAnActorOrByANonMiner()
        {
            World w = Strip();
            Assert.AreEqual(Terraform.RefusedOccupied, Terraform.DigRefusal(w, 6, 13), "pad");
            Assert.AreEqual(Terraform.RefusedOccupied, Terraform.DigRefusal(w, 36, 13), "hill");
            Assert.AreEqual(Terraform.RefusedOccupied, Terraform.DigRefusal(w, 19, 13), "the miner itself stands there");
            int other = FindActor(w, i => w.Actors[i].Cls != ClassId.Miner);
            Assert.Greater(other, 0);
            w.Actors[other].Alive = true;
            w.X[other] = 21.5;
            w.Z[other] = 13.5;
            Assert.AreEqual(Terraform.RefusedOccupied, Terraform.DigRefusal(w, 21, 13));
            Assert.AreEqual(Terraform.RefusedTarget, Terraform.Dig(w, other, 22.5, 13.5));
            Assert.AreEqual(Cell.Bridge, CellAt(w, 22, 13));
        }

        [Test]
        public void RouteGuardTheDigThatWouldCutTheLastPadToHillRouteIsRefusedWithAnEvent()
        {
            World w = Strip();
            Assert.AreEqual(Terraform.Dug, Terraform.Dig(w, Miner, 20.5, 12.5));
            w.Terraform.Carried[Miner] = 0; // the block is set down elsewhere; only the route matters here
            Cool(w);
            Assert.AreEqual(Terraform.Dug, Terraform.Dig(w, Miner, 20.5, 14.5));
            w.Terraform.Carried[Miner] = 0;
            Cool(w);
            Assert.IsTrue(Terraform.PadsReachHill(w.Map));
            int wv = w.WallVersion;
            int gv = w.GridVersion;
            int head = w.EventHead;
            Assert.AreEqual(Terraform.RefusedRoute, Terraform.Dig(w, Miner, 20.5, 13.5));
            Assert.AreEqual(Cell.Bridge, CellAt(w, 20, 13));
            Assert.AreEqual(0, w.Terraform.Carried[Miner]);
            Assert.AreEqual(wv, w.WallVersion);
            Assert.AreEqual(gv, w.GridVersion);
            Assert.AreEqual(head + 1, w.EventHead);
            SimEvent e = LastEvent(w);
            Assert.AreEqual(SimEventType.Terraform, e.Type);
            Assert.AreEqual(13 * w.Map.Width + 20, e.B);
            Assert.AreEqual(Terraform.RefusedRoute, e.Value);
            Assert.IsTrue(Terraform.PadsReachHill(w.Map));
        }

        [Test]
        public void RateCapOneChangePerTeamPerTeamCooldownTicksAndMinGapTicksBetweenAnyTwoChanges()
        {
            World w = Strip();
            Assert.AreEqual(Terraform.Dug, Terraform.Dig(w, Miner, 20.5, 13.5));
            int t0 = w.Tick;
            Assert.AreEqual(t0, w.WallChangeTick);
            w.Terraform.Carried[Miner] = 0;
            w.Tick = t0 + 1;
            Assert.AreEqual(Terraform.RefusedRate, Terraform.Dig(w, Miner, 20.5, 12.5));
            Assert.AreEqual(Cell.Bridge, CellAt(w, 20, 12));
            w.Tick = t0 + (int)MT.TeamCooldownTicks - 1;
            Assert.AreEqual(Terraform.RefusedRate, Terraform.Dig(w, Miner, 20.5, 12.5));
            // The other team is held only by the world-wide gap.
            int foe = FindActor(w, i => w.Actors[i].Team != w.Actors[Miner].Team && w.Actors[i].Cls == ClassId.Miner);
            Assert.Greater(foe, 0);
            w.Actors[foe].Alive = true;
            w.X[foe] = 25.3;
            w.Z[foe] = 13.5;
            w.Tick = t0 + (int)MT.MinGapTicks - 1;
            Assert.AreEqual(Terraform.RefusedRate, Terraform.Dig(w, foe, 26.5, 13.5));
            w.Tick = t0 + (int)MT.MinGapTicks;
            Assert.AreEqual(Terraform.Dug, Terraform.Dig(w, foe, 26.5, 13.5));
            w.Tick = t0 + (int)MT.TeamCooldownTicks;
            Assert.AreEqual(Terraform.Dug, Terraform.Dig(w, Miner, 20.5, 12.5));
        }

        [Test]
        public void TheSystemActsOncePerPressRisingEdgeAndADeadMinerLosesItsBlock()
        {
            World w = Strip();
            InputFrame f = SimInput.EmptyInput();
            f.Terraform = true;
            f.AimX = 20.5;
            f.AimZ = 13.5;
            SimInput.ApplyInput(w, Miner, f);
            Terraform.TerraformSystem(w);
            Assert.AreEqual(Cell.Void, CellAt(w, 20, 13));
            Assert.AreEqual(1, w.Terraform.Carried[Miner]);
            Cool(w);
            Terraform.TerraformSystem(w); // still held: no place
            Assert.AreEqual(1, w.Terraform.Carried[Miner]);
            f.Terraform = false;
            SimInput.ApplyInput(w, Miner, f);
            Terraform.TerraformSystem(w);
            f.Terraform = true;
            SimInput.ApplyInput(w, Miner, f);
            Terraform.TerraformSystem(w); // new press while carrying: place
            Assert.AreEqual(Cell.Floor, CellAt(w, 20, 13));
            Assert.AreEqual(0, w.Terraform.Carried[Miner]);
            w.Terraform.Carried[Miner] = 1;
            w.Actors[Miner].Alive = false;
            Terraform.TerraformSystem(w);
            Assert.AreEqual(0, w.Terraform.Carried[Miner]);
        }

        [Test]
        public void AHumanFPressOrModeSwitchInTheSharedInputFrameNeverReachesABot()
        {
            World w = Make(30, ClassId.Miner);
            InputFrame frame = SimInput.EmptyInput();
            frame.Terraform = true; // what the human input layer leaves in the driver's scratch frame after a press
            frame.TerraformSwitch = true;
            int bot = FindActor(w, i => w.Actors[i].Cls == ClassId.Miner);
            Assert.Greater(bot, 0);
            BotBrain.BotsThink(w, SimStep.Dt(w.Config));
            BotBrain.FillBotInput(w, bot, frame);
            Assert.IsFalse(frame.Terraform);
            Assert.IsFalse(frame.TerraformSwitch);
        }

        [Test]
        public void BotFlowFieldsAreAllFreshAtLeastOncePer30TicksWhileMinersDigAndPlaceAsFastAsAllowed()
        {
            World w = Make(101, ClassId.Miner);
            BotsState st = BotBrain.GetBotsState(w);
            Assert.Less(st.Fields.Count, MT.MinGapTicks, "derived: one field rebuilds per step, so the world-wide gap must exceed the field count");
            InputFrame frame = SimInput.EmptyInput();
            int sinceFresh = 0, worst = 0, changes = 0, head = w.EventHead;
            double dt = SimStep.Dt(w.Config);
            for (int s = 0; s < 900; s++)
            {
                BotBrain.BotsThink(w, dt);
                if (BotFields.StaleFieldCount(st.Fields, w) == 0) sinceFresh = 0;
                else sinceFresh++;
                if (sinceFresh > worst) worst = sinceFresh;
                for (int id = 0; id < w.ActorCount; id++)
                {
                    BotBrain.FillBotInput(w, id, frame);
                    // No swings or abilities: wall breaks would invalidate fields outside this item's cap.
                    frame.Attack = false;
                    frame.Ability = false;
                    bool force = w.Actors[id].Cls == ClassId.Miner && (w.Tick + id) % 2 == 0;
                    frame.Terraform = force;
                    if (force) AimForced(w, id, frame);
                    SimInput.ApplyInput(w, id, frame);
                }
                SimStep.Step(w, dt);
                for (int i = Math.Max(head, w.EventHead - World.EventCapacity); i < w.EventHead; i++)
                {
                    SimEvent e = w.Events[i % World.EventCapacity];
                    if (e.Type == SimEventType.Terraform && e.Value > 0) changes++;
                }
                head = w.EventHead;
            }
            Assert.Greater(changes, 2);
            Assert.Less(worst, 30);
            Assert.IsTrue(Terraform.PadsReachHill(w.Map));
        }

        /// <summary>The oracle soak's forced aim: in Place mode the nearest placeable cell, else straight ahead (no aim).</summary>
        internal static void AimForced(World w, int id, InputFrame frame)
        {
            if (Terraform.ModeOf(w, id) == TerraformMode.Place && Terraform.FindPlaceCell(w, id, out double px, out double pz))
            {
                frame.AimX = px;
                frame.AimZ = pz;
            }
            else
            {
                frame.AimX = double.NaN;
                frame.AimZ = double.NaN;
            }
        }

        /// <summary>Bot 1 (azure) in ToHill, asking to move, in the state for 10 s; its 0.3 stuck anchor 0.4 away (so it resets).</summary>
        private static World Jammed(double progressAge, out int id)
        {
            World w = Make(30, ClassId.Miner);
            BotsState st = BotBrain.GetBotsState(w);
            id = 1;
            Actor a = w.Actors[id];
            Assert.IsTrue(a.Alive);
            double now = w.Match.Time;
            a.AiState = AiState.ToHill;
            st.StateSince[id] = now - 10;
            st.WantMove[id] = 1;
            st.AnchorX[id] = w.X[id] + 0.4;
            st.AnchorZ[id] = w.Z[id];
            st.AnchorTime[id] = now;
            st.ProgressX[id] = w.X[id] + 0.4;
            st.ProgressZ[id] = w.Z[id];
            st.ProgressTime[id] = now - progressAge;
            BotFsm.ThinkBot(w, st, id);
            return w;
        }

        [Test]
        public void ProgressDetectorATravellingBotThatJostlesButStaysInsideProgressRadiusForProgressSecondsUnsticks()
        {
            GameConfig c = GameConfig.CreateBrowser5v5();
            Assert.AreEqual(1, BotFsm.ProgressRadius);
            Assert.Less(BotFsm.ProgressSeconds * c.SimHz + 2 * JsMath.Round((double)c.SimHz / c.Bots.ThinkHz), 90,
                "derived budget: ProgressSeconds in ticks plus two think periods under the soak's 90-tick stillness limit");
            World w = Jammed(BotFsm.ProgressSeconds + 0.01, out int id);
            Assert.AreEqual(AiState.Unstick, w.Actors[id].AiState);
        }

        [Test]
        public void ProgressDetectorControlTheSameJostleWithAFreshProgressAnchorDoesNotUnstick()
        {
            World w = Jammed(0, out int id);
            Assert.AreNotEqual(AiState.Unstick, w.Actors[id].AiState);
        }

        // ------------------------------------------------------------------------------------------- DG-09 (U-053)

        private static void Press(World w, bool terraform, bool flip, double ax, double az)
        {
            InputFrame f = SimInput.EmptyInput();
            f.Terraform = terraform;
            f.TerraformSwitch = flip;
            f.AimX = ax;
            f.AimZ = az;
            SimInput.ApplyInput(w, Miner, f);
            Terraform.TerraformSystem(w);
        }

        [Test]
        public void DG09CarryMax3DigModeDigsThreeThenSwitchesToPlaceAndPlaceModeEmptiesTheCarryBackToDig()
        {
            World w = Strip(3);
            Assert.AreEqual(TerraformMode.Dig, Terraform.ModeOf(w, Miner));
            int[] cxs = { 20, 18, 16 };
            for (int k = 0; k < 3; k++)
            {
                w.X[Miner] = cxs[k] + 0.5;
                w.Z[Miner] = 13.3;
                Press(w, true, false, cxs[k] + 0.5, 14.5);
                Press(w, false, false, 0, 0);
                Assert.AreEqual(k + 1, w.Terraform.Carried[Miner], "dig " + k);
                Cool(w);
            }
            Assert.AreEqual(TerraformMode.Place, Terraform.ModeOf(w, Miner), "a full carry switches to Place");
            for (int k = 0; k < 3; k++)
            {
                w.X[Miner] = cxs[k] + 0.5;
                w.Z[Miner] = 13.3;
                Press(w, true, false, cxs[k] + 0.5, 14.5);
                Assert.AreEqual(Cell.Floor, CellAt(w, cxs[k], 14), "refilled " + k);
                Press(w, false, false, 0, 0);
                Assert.AreEqual(2 - k, w.Terraform.Carried[Miner], "place " + k);
                Cool(w);
            }
            Assert.AreEqual(TerraformMode.Dig, Terraform.ModeOf(w, Miner), "an empty carry switches back to Dig");
        }

        [Test]
        public void DG09TheSwitchFlipsTheModeOncePerPressAndFActsInTheChosenMode()
        {
            World w = Strip(3);
            Press(w, true, false, 20.5, 14.5); // dig one: carry 1 of 3, still Dig
            Press(w, false, false, 0, 0);
            Assert.AreEqual(1, w.Terraform.Carried[Miner]);
            Assert.AreEqual(TerraformMode.Dig, Terraform.ModeOf(w, Miner));
            Cool(w);
            Press(w, false, true, 0, 0); // wheel / LB / RB
            Assert.AreEqual(TerraformMode.Place, Terraform.ModeOf(w, Miner));
            Assert.IsFalse(w.Inputs[Miner].TerraformSwitch, "the sim clears the switch when it reads it");
            Terraform.TerraformSystem(w); // the same stored frame again: no second flip
            Assert.AreEqual(TerraformMode.Place, Terraform.ModeOf(w, Miner));
            Press(w, true, false, 20.5, 14.5); // F in Place mode refills the hole
            Assert.AreEqual(Cell.Floor, CellAt(w, 20, 14));
            Assert.AreEqual(0, w.Terraform.Carried[Miner]);
            Assert.AreEqual(TerraformMode.Dig, Terraform.ModeOf(w, Miner));
            Cool(w);
            Press(w, false, true, 0, 0);
            Press(w, true, false, 20.5, 12.5); // Place mode with nothing carried: refused, nothing changes
            Assert.AreEqual(Terraform.RefusedTarget, LastEvent(w).Value);
            Assert.AreEqual(Cell.Bridge, CellAt(w, 20, 12));
        }

        [Test]
        public void DG09WithCarryMax1TheModeAlwaysFollowsTheCarrySoFReplaysTheBrowserDigPlaceAlternation()
        {
            World w = Strip(1);
            for (int k = 0; k < 4; k++)
            {
                Press(w, true, false, 20.5, 14.5);
                Press(w, false, false, 0, 0);
                Assert.AreEqual(k % 2 == 0 ? 1 : 0, w.Terraform.Carried[Miner], "press " + k);
                Assert.AreEqual(k % 2 == 0 ? TerraformMode.Place : TerraformMode.Dig, Terraform.ModeOf(w, Miner));
                Cool(w);
            }
        }

        [Test]
        public void DG09ANonMinerNeverDigsOrSwitchesAndADeadMinerGoesBackToDig()
        {
            World w = Strip(3);
            int other = FindActor(w, i => w.Actors[i].Cls == ClassId.Warrior);
            w.Actors[other].Alive = true;
            w.X[other] = 25.5;
            w.Z[other] = 13.5;
            InputFrame f = SimInput.EmptyInput();
            f.Terraform = true;
            f.TerraformSwitch = true;
            f.AimX = 26.5;
            f.AimZ = 13.5;
            SimInput.ApplyInput(w, other, f);
            Terraform.TerraformSystem(w);
            Assert.AreEqual(Cell.Bridge, CellAt(w, 26, 13));
            Assert.AreEqual(TerraformMode.Dig, Terraform.ModeOf(w, other));
            w.Terraform.Mode[Miner] = (byte)TerraformMode.Place;
            w.Terraform.Carried[Miner] = 2;
            w.Actors[Miner].Alive = false;
            Terraform.TerraformSystem(w);
            Assert.AreEqual(0, w.Terraform.Carried[Miner]);
            Assert.AreEqual(TerraformMode.Dig, Terraform.ModeOf(w, Miner));
        }

        [Test]
        public void DG09ResetWorldClearsCarryModeCooldownsAndTheWallChangeTick()
        {
            World w = Make(30, ClassId.Miner, 3);
            w.Terraform.Carried[0] = 2;
            w.Terraform.Mode[0] = (byte)TerraformMode.Place;
            w.Terraform.TeamReadyTick[0] = 999;
            w.WallChangeTick = 5;
            World.ResetWorld(w, new WorldOptions { Seed = 30, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Miner });
            Assert.AreEqual(0, w.Terraform.Carried[0]);
            Assert.AreEqual(TerraformMode.Dig, Terraform.ModeOf(w, 0));
            Assert.AreEqual(0, w.Terraform.TeamReadyTick[0]);
            Assert.AreEqual(World.NeverTick, w.WallChangeTick);
        }

        [Test]
        public void DG09FallRuleGroundRemovedUnderTheCentrePastTheCoyoteWindowFallsInAndClimbsOutWithAGetUpAndNoDamage()
        {
            World w = Strip();
            Actor a = w.Actors[Miner];
            a.SpawnProtect = 0;
            double hp = a.Hp;
            int coyote = Jump.CoyoteTicks(w.Config);
            // The ground under the miner goes (as a TNT crater or a raw edit would do; a dig is refused under an actor).
            MapGrid.SetCell(w, 19, 13, Cell.Void);
            int head = w.EventHead;
            double dt = SimStep.Dt(w.Config);
            for (int k = 0; k < coyote; k++)
            {
                Movement.MovementSystem(w, dt);
                Assert.AreEqual(k + 1, a.UnsupportedTicks, "inside the coyote window: still standing, may still jump");
                Assert.AreEqual(19.3, w.X[Miner], 1e-6);
            }
            Movement.MovementSystem(w, dt); // the first tick past the window
            Assert.AreEqual(0, a.UnsupportedTicks);
            Assert.IsTrue(Collide.FootprintClear(w, Miner, w.X[Miner], w.Z[Miner]), "climbed out onto ground its body fits");
            Assert.IsTrue(Terraform.WalkableAt(w.Map, w.X[Miner], w.Z[Miner]));
            Assert.Less(Math.Abs(w.X[Miner] - 19.3) + Math.Abs(w.Z[Miner] - 13.5), 2.5, "the nearest ground, not the pad");
            Assert.AreEqual(w.Config.Respawn.StandUpSeconds, a.StandUp, "the knock-down get-up");
            Assert.AreEqual(hp, a.Hp, "no fall damage (bible p13)");
            Assert.IsTrue(a.Alive);
            Assert.AreEqual(head + 1, w.EventHead);
            SimEvent e = LastEvent(w);
            Assert.AreEqual(SimEventType.Fall, e.Type);
            Assert.AreEqual(Miner, e.A);
            Assert.AreEqual(13 * w.Map.Width + 19, e.B);
            Assert.AreEqual(w.X[Miner], e.X);
            Assert.AreEqual(w.Z[Miner], e.Z);
        }

        [Test]
        public void DG09FallRuleABoxCornerOverAFreshHoleIsNotAFallAndNoClimbOutGroundFallsBackToThePad()
        {
            World w = Strip();
            Actor a = w.Actors[Miner];
            double r = Collide.ActorRadius(w, Miner);
            // Centre on (19, 13), a corner over (20, 14) only: unsupported by the box rule, but standing on its centre.
            w.X[Miner] = 20 - r * 0.8;
            w.Z[Miner] = 14 - r * 0.8;
            MapGrid.SetCell(w, 20, 14, Cell.Void);
            Assert.IsFalse(Collide.FootprintClear(w, Miner, w.X[Miner], w.Z[Miner]));
            a.UnsupportedTicks = 10;
            Assert.IsFalse(Terraform.FallIfUnsupported(w, Miner), "centre cell still walkable: no fall");
            // Nothing within FallSearchRings: back to the pad spawn point.
            World w2 = Strip();
            Array.Fill(w2.Map.Cells, Cell.Void);
            Paint(w2, 2, 10, 8, 16, Cell.Floor);
            w2.Actors[Miner].UnsupportedTicks = Jump.CoyoteTicks(w2.Config) + 1;
            Assert.IsTrue(Terraform.FallIfUnsupported(w2, Miner));
            World.SpawnPoint(w2.Map, TeamId.Azure, 0, out double sx, out double sz);
            Assert.AreEqual(JsMath.F32(sx), w2.X[Miner]);
            Assert.AreEqual(JsMath.F32(sz), w2.Z[Miner]);
        }
    }
}
