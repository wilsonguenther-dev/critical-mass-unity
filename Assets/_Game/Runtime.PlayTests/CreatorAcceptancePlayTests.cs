using System;
using System.Collections;
using System.Collections.Generic;
using CriticalMass.Runtime;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Onboarding;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>
    /// Creator acceptance (2026-11-13): the creator's four demo needs and corruption, proven the way a player does them:
    /// real Input System presses on a virtual keyboard and gamepad, through the project-wide actions and HumanInputSource,
    /// in the Boot scene's real game (views and HUD included). The sim runs at TimeScale 0 and is stepped tick by tick, so
    /// every result is the same on every machine. Test SETUP may place actor 0 next to the thing under test (a teleport,
    /// the capture tools' pattern) and sets bots passive so nothing interferes; every ACTION is a real press.
    /// Every test logs one "[Creator]" line with its numbers (the acceptance report quotes them).
    /// </summary>
    public sealed class CreatorAcceptancePlayTests
    {
        const string BootScene = "Assets/_Game/Scenes/Boot.unity";

        SimDriverBehaviour _driver;
        Keyboard _kb;
        Gamepad _pad;
        bool _settingsSaved;
        InputSettings.BackgroundBehavior _savedBackground;
        InputSettings.EditorInputBehaviorInPlayMode _savedEditorBehaviour;

        World W => _driver.World;
        GameConfig C => _driver.World.Config;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene(BootScene);
            yield return null;
            yield return null;
            _driver = UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();
            Assert.IsNotNull(_driver, "Boot did not build a SimDriverBehaviour");
            _driver.HumanAutopilot = false;
            _driver.TimeScale = 0;
            InputSettings s = InputSystem.settings;
            _savedBackground = s.backgroundBehavior;
            _savedEditorBehaviour = s.editorInputBehaviorInPlayMode;
            _settingsSaved = true;
            s.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            s.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _kb = InputSystem.AddDevice<Keyboard>();
            _pad = InputSystem.AddDevice<Gamepad>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_kb != null) InputSystem.RemoveDevice(_kb);
            if (_pad != null) InputSystem.RemoveDevice(_pad);
            _kb = null;
            _pad = null;
            if (_settingsSaved)
            {
                InputSettings s = InputSystem.settings;
                s.backgroundBehavior = _savedBackground;
                s.editorInputBehaviorInPlayMode = _savedEditorBehaviour;
                _settingsSaved = false;
            }
            _driver = null;
        }

        // ---------------------------------------------------------------- harness

        /// <summary>Restarts the running world in place (the guided match's restart) with this class and size, bots passive.</summary>
        void Restart(ClassId cls, int teamSize)
        {
            _driver.Config.Bots.PassiveSeconds = 100000;
            // Long acceptance runs (the bridge sweep is ~30 sim minutes) must not hit the final whistle or the hatch.
            _driver.Config.MatchSeconds = 100000;
            _driver.Config.HatchSeconds = 100000;
            World.ResetWorld(W, new WorldOptions
            {
                Seed = 1337, TeamSize = teamSize, HumanTeam = TeamId.Azure, HumanClass = cls, Config = _driver.Config,
            });
            _driver.StepTicks((int)Math.Ceiling(3 * C.SimHz));
            Assert.IsTrue(W.Actors[0].Alive, "actor 0 alive after the settle");
            Assert.AreEqual(cls, W.Actors[0].Cls);
        }

        void Teleport(double x, double z)
        {
            W.X[0] = W.PrevX[0] = x;
            W.Z[0] = W.PrevZ[0] = z;
            W.Vx[0] = 0;
            W.Vz[0] = 0;
        }

        /// <summary>Screen-space stick for a world direction (the fixed camera yaw, HumanInputState's basis).</summary>
        static Vector2 Stick(double dx, double dz)
        {
            double len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-9) return Vector2.zero;
            dx /= len;
            dz /= len;
            return new Vector2((float)(dx * HumanInputState.ScreenRightX + dz * HumanInputState.ScreenRightZ),
                (float)(dx * HumanInputState.ScreenUpX + dz * HumanInputState.ScreenUpZ));
        }

        IEnumerator Pad(Vector2 left, Vector2 right, float rightTrigger = 0f, GamepadButton? button = null)
        {
            var st = new GamepadState { leftStick = left, rightStick = right, rightTrigger = rightTrigger };
            if (button.HasValue) st = st.WithButton(button.Value);
            InputSystem.QueueStateEvent(_pad, st);
            InputSystem.Update();
            yield return null;
        }

        IEnumerator Keys(params Key[] keys)
        {
            InputSystem.QueueStateEvent(_kb, new KeyboardState(keys));
            InputSystem.Update();
            yield return null;
        }

        /// <summary>Points the gamepad aim (right stick) at world direction (dx, dz) and lets go (the aim keeps it).</summary>
        IEnumerator AimAt(double dx, double dz)
        {
            yield return Pad(Vector2.zero, Stick(dx, dz));
            yield return Pad(Vector2.zero, Vector2.zero);
        }

        int CountEvents(int fromHead, SimEventType t, out double lastValue, out int lastB)
        {
            lastValue = 0;
            lastB = -1;
            int n = 0;
            for (int i = fromHead; i < W.EventHead; i++)
            {
                SimEvent e = W.Events[i % W.Events.Length];
                if (e.Type != t) continue;
                n++;
                lastValue = e.Value;
                lastB = e.B;
            }
            return n;
        }

        static byte CellAt(MapDef m, int cx, int cz) => cx < 0 || cz < 0 || cx >= m.Width || cz >= m.Height ? Cell.Void : m.Cells[cz * m.Width + cx];

        /// <summary>A Floor cell whose 7 x 7 block is all Floor and diggable-clear, nearest (nx, nz).</summary>
        bool OpenSpot(double nx, double nz, out int sx, out int sz)
        {
            MapDef m = W.Map;
            sx = sz = -1;
            double best = double.MaxValue;
            for (int cz = 3; cz < m.Height - 3; cz++)
            {
                for (int cx = 3; cx < m.Width - 3; cx++)
                {
                    bool ok = true;
                    for (int dz = -3; dz <= 3 && ok; dz++)
                        for (int dx = -3; dx <= 3 && ok; dx++)
                            if (CellAt(m, cx + dx, cz + dz) != Cell.Floor) ok = false;
                    if (!ok || Terraform.DigRefusal(W, cx + 1, cz) != 0) continue;
                    double d = (cx + 0.5 - nx) * (cx + 0.5 - nx) + (cz + 0.5 - nz) * (cz + 0.5 - nz);
                    if (d < best)
                    {
                        best = d;
                        sx = cx;
                        sz = cz;
                    }
                }
            }
            return sx >= 0;
        }

        /// <summary>A Floor cell with a clear footprint whose +x neighbour is a placeable VOID edge, nearest (nx, nz).</summary>
        bool EdgeSpot(double nx, double nz, out int fx, out int fz, out int vx, out int vz)
        {
            MapDef m = W.Map;
            fx = fz = vx = vz = -1;
            double best = double.MaxValue;
            int[] ddx = { 1, -1, 0, 0 }, ddz = { 0, 0, 1, -1 };
            for (int cz = 2; cz < m.Height - 2; cz++)
            {
                for (int cx = 2; cx < m.Width - 2; cx++)
                {
                    if (CellAt(m, cx, cz) != Cell.Floor) continue;
                    if (!Collide.ActorFits(W, cx + 0.5, cz + 0.5, C.Class(ClassId.Miner).Radius)) continue;
                    for (int k = 0; k < 4; k++)
                    {
                        int ax = cx + ddx[k], az = cz + ddz[k];
                        if (CellAt(m, ax, az) != Cell.Void || Terraform.PlaceRefusal(W, ax, az) != 0) continue;
                        // a real edge: the cell beyond the void is void too (new ground, not a gap filler)
                        if (CellAt(m, ax + ddx[k], az + ddz[k]) != Cell.Void) continue;
                        double d = (cx + 0.5 - nx) * (cx + 0.5 - nx) + (cz + 0.5 - nz) * (cz + 0.5 - nz);
                        if (d >= best) continue;
                        best = d;
                        fx = cx;
                        fz = cz;
                        vx = ax;
                        vz = az;
                    }
                }
            }
            return fx >= 0;
        }

        double FlowDistance(double fromX, double fromZ, double toX, double toZ)
        {
            FlowField f = FlowField.ComputeFlowField(W.Map, new[] { new PointXZ(toX, toZ) });
            int cx = (int)Math.Floor(fromX), cz = (int)Math.Floor(fromZ);
            return f.Dist[cz * W.Map.Width + cx];
        }

        // ---------------------------------------------------------------- 1. ground removable and placeable by miners

        [UnityTest]
        public IEnumerator Need1_MinerDigsWithF_TheHoleIsReal_CarriesTheBlock_PlacesItAtTheEdge_WalksOnIt()
        {
            Restart(ClassId.Miner, 5);
            PadDef pad = W.Map.Pads[(int)TeamId.Azure];
            Assert.IsTrue(OpenSpot(pad.X + 7, pad.Z, out int sx, out int sz), "an open floor patch on the base island");
            Teleport(sx + 0.5, sz + 0.5);
            _driver.StepTicks(1);
            yield return AimAt(1, 0);
            yield return null;
            string before = TerrainPrompt.Line;

            // DIG with the key the HUD shows (F on the keyboard).
            int head = W.EventHead;
            yield return Keys(Key.F);
            _driver.StepTicks(1);
            yield return Keys();
            int dug = CountEvents(head, SimEventType.Terraform, out double value, out int cell);
            Assert.AreEqual(1, dug, "one Terraform event for one F press");
            Assert.AreEqual(Terraform.Dug, (int)value, "F dug (value " + value + ")");
            int hx = cell % W.Map.Width, hz = cell / W.Map.Width;
            Assert.AreEqual(Cell.Void, W.Map.Cells[cell], "the dug cell is a hole (VOID)");
            Assert.AreEqual(1, W.Terraform.Carried[0], "the miner carries the block");
            yield return null;
            yield return null;
            string carrying = TerrainPrompt.Line;
            StringAssert.Contains("BLOCKS 1/", carrying, "the HUD shows what the miner carries");
            StringAssert.Contains("PLACE", carrying, "the HUD shows how to place it");

            // The hole is real: walking into it is stopped at the edge, and bots' flow fields go around it.
            Assert.IsFalse(MapGrid.IsWalkable(W.Map, hx, hz));
            FlowField ff = FlowField.ComputeFlowField(W.Map, new[] { new PointXZ(W.Map.Hill.X, W.Map.Hill.Z) });
            Assert.IsTrue(double.IsPositiveInfinity(ff.Dist[cell]), "flow fields treat the hole as impassable");
            Assert.IsTrue(double.IsFinite(ff.Dist[cell - 1]) && double.IsFinite(ff.Dist[cell + W.Map.Width]), "and route around it");
            yield return Pad(Stick(1, 0), Vector2.zero);
            int onHole = 0, fell = 0;
            for (int t = 0; t < (int)C.SimHz; t++)
            {
                _driver.StepTicks(1);
                if (MapGrid.CellAt(W.Map, W.X[0], W.Z[0]) == Cell.Void && W.Actors[0].AirLen == 0) onHole++;
                if (W.Actors[0].StandUp > 0) fell++;
            }
            yield return Pad(Vector2.zero, Vector2.zero);
            double stopX = W.X[0];
            Assert.LessOrEqual(onHole, Jump.CoyoteTicks(C) + 1, "never left standing over the hole");

            // PLACE: to the edge of the island, switch mode with the bumper, aim at the void, press F.
            Assert.IsTrue(EdgeSpot(W.X[0], W.Z[0], out int fx, out int fz, out int vx, out int vz), "an island edge to build on");
            Teleport(fx + 0.5, fz + 0.5);
            _driver.StepTicks((int)Math.Ceiling(1.5 * C.SimHz));   // the team's terraform cooldown
            yield return Pad(Vector2.zero, Vector2.zero, 0f, GamepadButton.RightShoulder);
            _driver.StepTicks(1);
            yield return Pad(Vector2.zero, Vector2.zero);
            Assert.AreEqual(TerraformMode.Place, Terraform.ModeOf(W, 0), "the bumper switched the miner to Place");
            yield return AimAt(vx - fx, vz - fz);
            head = W.EventHead;
            yield return Keys(Key.F);
            _driver.StepTicks(1);
            yield return Keys();
            CountEvents(head, SimEventType.Terraform, out value, out cell);
            Assert.AreEqual(Terraform.Placed, (int)value, "F placed the block (value " + value + ")");
            // The aim point is pulled back to Reach (1.6 u) from the miner, so from a cell centre it lands on the void cell
            // the aim ray reaches, which may be one beyond the first edge cell: either is new ground at the edge.
            Assert.AreEqual(Cell.Floor, W.Map.Cells[cell], "new ground");
            vx = cell % W.Map.Width;
            vz = cell / W.Map.Width;
            Assert.LessOrEqual(Math.Abs(vx - fx) + Math.Abs(vz - fz), 2, "next to the miner");
            Assert.AreEqual(0, W.Terraform.Carried[0]);

            // WALK onto the new ground.
            yield return Pad(Stick(vx + 0.5 - W.X[0], vz + 0.5 - W.Z[0]), Vector2.zero);
            bool reached = false;
            for (int t = 0; t < 2 * (int)C.SimHz && !reached; t++)
            {
                _driver.StepTicks(1);
                int cx = (int)Math.Floor(W.X[0]), cz = (int)Math.Floor(W.Z[0]);
                reached = cx == vx && cz == vz;
            }
            yield return Pad(Vector2.zero, Vector2.zero);
            Assert.IsTrue(reached, "the miner stands on the placed block");
            Assert.AreEqual(0, W.Actors[0].StandUp, "without falling");

            // Bots cope: both pads still reach the hill and no bot stands in the hole for 20 s of normal play.
            Assert.IsTrue(Terraform.PadsReachHill(W.Map));
            int botInHole = 0;
            for (int t = 0; t < 20 * C.SimHz; t++)
            {
                _driver.StepTicks(1);
                for (int i = 1; i < W.ActorCount; i++)
                    if (W.Actors[i].Alive && W.Actors[i].AirLen == 0 && (int)Math.Floor(W.X[i]) == hx && (int)Math.Floor(W.Z[i]) == hz) botInHole++;
            }
            Assert.AreEqual(0, botInHole, "no bot stands in the hole");
            Debug.Log($"[Creator] need1 PASS: hole ({hx},{hz}), HUD before '{before}', carrying '{carrying}', walked into the hole: stopped at x {stopX:0.00} " +
                      $"(over-hole ticks {onHole}, fall ticks {fell}); placed ({vx},{vz}) from ({fx},{fz}); walked on it; bots in hole 0 over 20 s");
        }

        // ---------------------------------------------------------------- 2. knock-down structures: ramp one way, block the other

        /// <summary>A stand point near pillar k from which a swing knocks it over as mode (the sim's own ModeFor).</summary>
        bool StandFor(int k, int mode, out double x, out double z)
        {
            DecorationDef d = W.Map.Decorations[k];
            double bx = d.Cx + 0.5, bz = d.Cz + 0.5, r = C.Class(W.Actors[0].Cls).Radius;
            CellDir dir = Topple.Dir(d, mode);
            // Straight behind the fall direction first, then around it.
            for (double dist = 1.5; dist <= 2.0; dist += 0.25)
            {
                for (int a = 0; a <= 60; a += 15)
                {
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        double th = Math.Atan2(-dir.Dz, -dir.Dx) + sgn * a * Math.PI / 180;
                        x = bx + Math.Cos(th) * dist;
                        z = bz + Math.Sin(th) * dist;
                        if (Topple.ModeFor(d, x, z) == mode && Collide.ActorFits(W, x, z, r)) return true;
                    }
                }
            }
            x = z = 0;
            return false;
        }

        IEnumerator SwingUntilDown(int k, double bx, double bz, int maxSwings)
        {
            for (int s = 0; s < maxSwings && W.Topple.Fallen[k] == Topple.Standing; s++)
            {
                yield return AimAt(bx - W.X[0], bz - W.Z[0]);
                yield return Pad(Vector2.zero, Vector2.zero, 1f);
                _driver.StepTicks(1);
                yield return Pad(Vector2.zero, Vector2.zero, 0f);
                _driver.StepTicks((int)Math.Ceiling(1.0 * C.SimHz));
            }
        }

        [UnityTest]
        public IEnumerator Need2_PillarHitFromOneSideFallsIntoARampAFighterWalksUp_FromTheOtherSideIntoAWallThatBlocksThePath()
        {
            Restart(ClassId.Warrior, 5);
            Assert.IsNotNull(W.Map.Decorations);
            int count = W.Map.Decorations.Length;
            Assert.GreaterOrEqual(count, 1, "the map has knock-down pillars");
            PadDef pad = W.Map.Pads[(int)TeamId.Azure];
            int k = TrialChecks.PillarFor(W, TeamId.Azure);
            DecorationDef d = W.Map.Decorations[k];
            double bx = d.Cx + 0.5, bz = d.Cz + 0.5;
            double padDist = Math.Sqrt((bx - pad.X) * (bx - pad.X) + (bz - pad.Z) * (bz - pad.Z));
            double hillDist = Math.Sqrt((bx - W.Map.Hill.X) * (bx - W.Map.Hill.X) + (bz - W.Map.Hill.Z) * (bz - W.Map.Hill.Z));

            // RAMP
            Assert.IsTrue(StandFor(k, Topple.Ramp, out double rx, out double rz), "a stand point on the ramp side");
            Teleport(rx, rz);
            _driver.StepTicks(1);
            yield return null;
            string prompt = TerrainPrompt.Line;
            StringAssert.Contains("RAMP", prompt, "the HUD tells the player this side makes a ramp");
            yield return SwingUntilDown(k, bx, bz, 12);
            Assert.AreEqual(Topple.Ramp, (int)W.Topple.Fallen[k], "the pillar fell into a ramp");
            _driver.StepTicks((int)C.Abilities.Topple.FallTicks + 1);
            var foot = new int[Topple.FootprintMax(C)];
            int n = Topple.Footprint(C, W.Map, d, d.Ramp, foot);
            int bridges = 0;
            for (int i = 0; i < n; i++) if (W.Map.Cells[foot[i]] == Cell.Bridge) bridges++;
            Assert.Greater(bridges, 0, "the ramp laid walkable deck across the void");
            double z0 = W.Z[0], x0 = W.X[0];
            yield return Pad(Stick(d.Ramp.Dx, d.Ramp.Dz), Vector2.zero);
            double far = 0;
            bool overDeck = false;
            for (int t = 0; t < 5 * C.SimHz; t++)
            {
                _driver.StepTicks(1);
                double along = (W.X[0] - x0) * d.Ramp.Dx + (W.Z[0] - z0) * d.Ramp.Dz;
                if (along > far) far = along;
                if (MapGrid.CellAt(W.Map, W.X[0], W.Z[0]) == Cell.Bridge) overDeck = true;
            }
            yield return Pad(Vector2.zero, Vector2.zero);
            int landCell = MapGrid.CellAt(W.Map, W.X[0], W.Z[0]);
            Assert.IsTrue(overDeck, "the fighter walked over the ramp deck");
            Assert.Greater(far, C.Abilities.Topple.LengthCells + 1, "and up it to the far side");
            Assert.AreEqual(Cell.Floor, landCell, "onto the other area's ground");
            Assert.AreEqual(0, W.Actors[0].StandUp, "without falling");
            Debug.Log($"[Creator] need2 ramp PASS: pillar {k} of {count} at ({bx},{bz}), {padDist:0.0} u from the Azure pad, {hillDist:0.0} u from the hill; " +
                      $"HUD '{prompt}'; ramp deck cells {bridges}; fighter went {far:0.0} u along the ramp, ended on cell type {landCell}");

            // RESET, then WALL
            Restart(ClassId.Warrior, 5);
            Assert.AreEqual(Topple.Standing, (int)W.Topple.Fallen[k], "reset stands the pillar up");
            n = Topple.Footprint(C, W.Map, d, d.Block, foot);
            double probeX = bx + d.Block.Dx * (C.Abilities.Topple.LengthCells + 1.5), probeZ = bz + d.Block.Dz * (C.Abilities.Topple.LengthCells + 1.5);
            double routeBefore = FlowDistance(pad.X, pad.Z, probeX, probeZ);
            Assert.IsTrue(StandFor(k, Topple.Block, out double wx, out double wz), "a stand point on the wall side");
            Teleport(wx, wz);
            _driver.StepTicks(1);
            yield return null;
            string prompt2 = TerrainPrompt.Line;
            StringAssert.Contains("WALL", prompt2, "the HUD tells the player this side makes a wall");
            yield return SwingUntilDown(k, bx, bz, 15);
            Assert.AreEqual(Topple.Block, (int)W.Topple.Fallen[k], "the pillar fell into a wall");
            _driver.StepTicks((int)C.Abilities.Topple.FallTicks + 1);
            int walls = 0;
            for (int i = 0; i < n; i++) if (W.Map.Cells[foot[i]] == Cell.Wall) walls++;
            Assert.Greater(walls, 0, "the wall stands across the ground");
            double routeAfter = FlowDistance(pad.X, pad.Z, probeX, probeZ);
            // A fighter pushing straight into it is stopped.
            x0 = W.X[0];
            z0 = W.Z[0];
            yield return Pad(Stick(d.Block.Dx, d.Block.Dz), Vector2.zero);
            int inWall = 0;
            for (int t = 0; t < 2 * C.SimHz; t++)
            {
                _driver.StepTicks(1);
                int c = MapGrid.CellAt(W.Map, W.X[0], W.Z[0]);
                if (c == Cell.Wall) inWall++;
            }
            yield return Pad(Vector2.zero, Vector2.zero);
            double pushed = (W.X[0] - x0) * d.Block.Dx + (W.Z[0] - z0) * d.Block.Dz;
            double faceAlong = (W.X[0] - bx) * d.Block.Dx + (W.Z[0] - bz) * d.Block.Dz;
            Assert.AreEqual(0, inWall, "nobody walks through the wall");
            Assert.Less(faceAlong, 0.55, "the fighter pushing into the wall stops at its face (base cell edge at 0.5)");
            Assert.IsTrue(double.IsPositiveInfinity(routeAfter) || routeAfter > routeBefore + 0.5,
                $"the wall blocks the path: pad to beyond-the-wall was {routeBefore:0.0}, now {routeAfter:0.0}");
            Debug.Log($"[Creator] need2 wall PASS: HUD '{prompt2}'; wall cells {walls}; fighter pushed {pushed:0.00} u into it; " +
                      $"stopped {faceAlong:0.00} u past the pillar centre; pad route to ({probeX:0.0},{probeZ:0.0}) {routeBefore:0.0} -> {routeAfter:0.0}; pillars on the map {count}");
        }

        // ---------------------------------------------------------------- 3. jumping

        IEnumerator JumpOver(double fx, double fz, double gx, double gz, bool pad, List<string> notes, string tag)
        {
            double dx = gx - fx, dz = gz - fz, len = Math.Sqrt(dx * dx + dz * dz);
            dx /= len;
            dz /= len;
            Teleport(fx - dx * 1.2, fz - dz * 1.2);
            _driver.StepTicks(1);
            yield return Pad(Stick(dx, dz), Vector2.zero);
            double target = fx * dx + fz * dz - 0.05;
            for (int t = 0; t < C.SimHz && W.X[0] * dx + W.Z[0] * dz < target; t++) _driver.StepTicks(1);
            double sx = W.X[0], sz = W.Z[0];
            if (pad)
            {
                yield return Pad(Stick(dx, dz), Vector2.zero, 0f, GamepadButton.South);
                _driver.StepTicks(1);
                yield return Pad(Stick(dx, dz), Vector2.zero);
            }
            else
            {
                yield return Keys(Key.Space);
                _driver.StepTicks(1);
                yield return Keys();
            }
            int air = W.Actors[0].AirLen;
            bool overGap = false;
            double apex = 0;
            for (int t = 0; t < 2 * C.SimHz && W.Actors[0].AirLen > 0; t++)
            {
                if (MapGrid.CellAt(W.Map, W.X[0], W.Z[0]) == Cell.Void) overGap = true;
                double lift = Jump.JumpLift(C, W.Actors[0], Jump.JumpPhase(W.Actors[0], 1.0));
                if (lift > apex) apex = lift;
                _driver.StepTicks(1);
            }
            yield return Pad(Vector2.zero, Vector2.zero);
            double flown = (W.X[0] - sx) * dx + (W.Z[0] - sz) * dz;
            int land = MapGrid.CellAt(W.Map, W.X[0], W.Z[0]);
            notes.Add($"{tag}: air ticks {air} ({air / C.SimHz:0.00} s), apex {apex:0.00}, over the gap {overGap}, flew {flown:0.00} u, landed on {land}, standUp {W.Actors[0].StandUp}");
            Assert.Greater(air, 0, tag + ": the press made the fighter airborne");
            Assert.IsTrue(MapGrid.IsWalkableType(land), tag + ": landed on walkable ground");
            Assert.AreEqual(0, W.Actors[0].StandUp, tag + ": no fall");
            Assert.IsTrue(W.Actors[0].Alive, tag);
        }

        [UnityTest]
        public IEnumerator Need3_SpaceAndTheGamepadButtonJumpOverAOneCellGapAndOntoAOneStepLedge()
        {
            Restart(ClassId.Miner, 5);
            var notes = new List<string>();
            // A one-cell gap: dig one with the miner (the creator's own combo: dig a trench, jump it) in open ground.
            PadDef pad = W.Map.Pads[(int)TeamId.Azure];
            Assert.IsTrue(OpenSpot(pad.X + 7, pad.Z, out int sx, out int sz));
            Teleport(sx + 0.5, sz + 0.5);
            _driver.StepTicks(1);
            yield return AimAt(1, 0);
            int head = W.EventHead;
            yield return Keys(Key.F);
            _driver.StepTicks(1);
            yield return Keys();
            CountEvents(head, SimEventType.Terraform, out double dv, out int hole);
            Assert.AreEqual(Terraform.Dug, (int)dv, "F dug the gap");
            int hx = hole % W.Map.Width;
            Assert.AreEqual(sz, hole / W.Map.Width);
            Assert.AreEqual(Cell.Void, W.Map.Cells[hole], "a one-cell gap dug at (" + hx + "," + sz + ")");
            double gx = hx + 0.5, gz = sz + 0.5;
            yield return JumpOver(hx - 0.5, sz + 0.5, gx, gz, false, notes, "Space over the one-cell gap");
            Assert.Greater(W.X[0], gx + 0.5, "Space carried the fighter past the gap");
            yield return JumpOver(hx + 1.5, sz + 0.5, gx, gz, true, notes, "Gamepad A over the one-cell gap");
            Assert.Less(W.X[0], gx - 0.5, "A carried the fighter back over the gap");

            // A one-step ledge: the nearest orthogonal pair of walkable cells whose ground heights differ by a step.
            MapDef m = W.Map;
            double bestStep = 0;
            int lo = -1, hi = -1;
            double bd = double.MaxValue;
            for (int cz = 1; cz < m.Height - 1; cz++)
            {
                for (int cx = 1; cx < m.Width - 1; cx++)
                {
                    if (CellAt(m, cx, cz) != Cell.Floor) continue;
                    double h0 = GroundHeight.At(m, cx + 0.5, cz + 0.5);
                    int[] ox = { 1, 0 }, oz = { 0, 1 };
                    for (int q = 0; q < 2; q++)
                    {
                        int nx = cx + ox[q], nz = cz + oz[q];
                        if (CellAt(m, nx, nz) != Cell.Floor) continue;
                        double h1 = GroundHeight.At(m, nx + 0.5, nz + 0.5);
                        double step = Math.Abs(h1 - h0);
                        if (step < 0.3 || step > 1.5) continue;
                        double d = Math.Abs(cx - pad.X) + Math.Abs(cz - pad.Z);
                        if (d >= bd) continue;
                        bd = d;
                        bestStep = step;
                        lo = h0 < h1 ? cz * m.Width + cx : nz * m.Width + nx;
                        hi = h0 < h1 ? nz * m.Width + nx : cz * m.Width + cx;
                    }
                }
            }
            if (lo >= 0)
            {
                double lx = lo % m.Width + 0.5, lz = lo / m.Width + 0.5, ux = hi % m.Width + 0.5, uz = hi / m.Width + 0.5;
                yield return JumpOver(lx, lz, ux + (ux - lx), uz + (uz - lz), false, notes, $"Space onto a {bestStep:0.00} u ledge");
                double hNow = GroundHeight.At(m, W.X[0], W.Z[0]);
                notes.Add($"ledge: low ground {GroundHeight.At(m, lx, lz):0.00}, landed on ground at {hNow:0.00}");
                Assert.Greater(hNow, GroundHeight.At(m, lx, lz) + 0.25, "landed up on the ledge");
            }
            else notes.Add("ledge: no floor step between 0.3 and 1.5 u on this map; the sim has no height, so steps never block walking");
            Debug.Log("[Creator] need3 PASS: " + string.Join(" | ", notes));
        }

        // ---------------------------------------------------------------- 4. bridges at any angle

        sealed class Crossing
        {
            public bool Arrived;
            public int StallTick = -1, FallTick = -1, Ticks;
        }

        static bool Chasm(MapDef map, BridgeSpan sp, out double enter, out double exit)
        {
            enter = exit = -1;
            for (int k = 0; k * 0.05 <= sp.Len; k++)
            {
                double a = k * 0.05;
                int c = MapGrid.CellAt(map, sp.X0 + sp.Ux * a, sp.Z0 + sp.Uz * a);
                if (c != Cell.Bridge && c != Cell.Void) continue;
                if (enter < 0) enter = a;
                exit = a;
            }
            return enter >= 0;
        }

        IEnumerator Cross(double x, double z, double ux, double uz, double goalAlong, double ox, double oz, Func<IEnumerator> press,
            int budget, Crossing output)
        {
            Teleport(x, z);
            yield return press();
            var along = new List<double>();
            double a0 = (x - ox) * ux + (z - oz) * uz;
            along.Add(a0);
            for (int t = 1; t <= budget; t++)
            {
                _driver.StepTicks(1);
                double a = (W.X[0] - ox) * ux + (W.Z[0] - oz) * uz;
                along.Add(a);
                if (W.Actors[0].StandUp > 0 && output.FallTick < 0) output.FallTick = t;
                if (t >= 6 && a - along[t - 3] < 0.01 && output.StallTick < 0) output.StallTick = t;
                if (a >= goalAlong)
                {
                    output.Arrived = true;
                    output.Ticks = t;
                    break;
                }
                output.Ticks = t;
            }
            yield return Pad(Vector2.zero, Vector2.zero);
            yield return Keys();
        }

        static readonly (Key[] keys, double up, double right)[] KeyDirs =
        {
            (new[] { Key.W }, 1, 0), (new[] { Key.W, Key.D }, 1, 1), (new[] { Key.D }, 0, 1), (new[] { Key.S, Key.D }, -1, 1),
            (new[] { Key.S }, -1, 0), (new[] { Key.S, Key.A }, -1, -1), (new[] { Key.A }, 0, -1), (new[] { Key.W, Key.A }, 1, -1),
        };

        [UnityTest]
        public IEnumerator Need4_EveryBridgeBothWaysAt0_30_45_60DegreesWithTheStickAndWithTheNearestKeyPairsNoStallNoFall()
        {
            Restart(ClassId.Warrior, 1);
            var bad = new List<string>();
            int runs = 0, spans = 0;
            double worstKeyAngle = 0;
            int[] angles = { 0, 30, -30, 45, -45, 60, -60 };
            double speed = C.Class(ClassId.Warrior).MoveSpeed;
            double r = C.Class(ClassId.Warrior).Radius;
            for (int s = 0; s < BridgeSpans.All.Count; s++)
            {
                BridgeSpan sp = BridgeSpans.All[s];
                if (!Chasm(W.Map, sp, out double enter, out double exit)) continue;
                spans++;
                for (int dirSign = 1; dirSign >= -1; dirSign -= 2)
                {
                    // Origin and axis for this direction (reverse: start at the far end).
                    double ux = sp.Ux * dirSign, uz = sp.Uz * dirSign;
                    double ox = dirSign > 0 ? sp.X0 : sp.X1, oz = dirSign > 0 ? sp.Z0 : sp.Z1;
                    double en = dirSign > 0 ? enter : sp.Len - exit, ex = dirSign > 0 ? exit : sp.Len - enter;
                    double startA = -1;
                    for (double a = Math.Max(0, en - 1); a <= en + 1; a += 0.25)
                        if (Collide.ActorFits(W, ox + ux * a, oz + uz * a, r)) { startA = a; break; }
                    string label = "B" + (s + 1) + (dirSign > 0 ? " fwd" : " back");
                    if (startA < 0)
                    {
                        bad.Add(label + ": no fitting start");
                        continue;
                    }
                    double sx = ox + ux * startA, sz = oz + uz * startA;
                    double goal = Math.Min(sp.Len, ex + 1);
                    double travel = goal - startA;
                    double axis = Math.Atan2(uz, ux);
                    foreach (int deg in angles)
                    {
                        double th = axis + deg * Math.PI / 180;
                        Vector2 st = Stick(Math.Cos(th), Math.Sin(th));
                        int budget = (int)Math.Ceiling(1.25 * travel / (speed * Math.Cos(deg * Math.PI / 180)) * C.SimHz) + (int)C.SimHz;
                        var run = new Crossing();
                        yield return Cross(sx, sz, ux, uz, goal, ox, oz, () => Pad(st, Vector2.zero), budget, run);
                        runs++;
                        if (!run.Arrived || run.StallTick >= 0)
                        {
                            // Diagnosis: the same crossing with the input layer's movement assist off.
                            HumanInputSource src = _driver.GetComponent<HumanInputSource>();
                            if (src == null) src = UnityEngine.Object.FindAnyObjectByType<HumanInputSource>();
                            if (src != null)
                            {
                                src.State.MovementAssist = false;
                                var diag = new Crossing();
                                yield return Cross(sx, sz, ux, uz, goal, ox, oz, () => Pad(st, Vector2.zero), budget, diag);
                                src.State.MovementAssist = true;
                                bad.Add($"{label} stick {deg} deg: assist OFF -> arrived {diag.Arrived}, stall tick {diag.StallTick}");
                            }
                        }
                        if (!run.Arrived) bad.Add($"{label} stick {deg} deg: not across in {budget} ticks");
                        if (run.StallTick >= 0) bad.Add($"{label} stick {deg} deg: stalled 0.1 s at tick {run.StallTick}");
                        if (run.FallTick >= 0) bad.Add($"{label} stick {deg} deg: fell at tick {run.FallTick}");
                    }
                    // Keyboard: the two key combinations nearest the bridge's axis, held unchanged.
                    var order = new List<(int idx, double off)>();
                    for (int q = 0; q < KeyDirs.Length; q++)
                    {
                        double wx = KeyDirs[q].right * HumanInputState.ScreenRightX + KeyDirs[q].up * HumanInputState.ScreenUpX;
                        double wz = KeyDirs[q].right * HumanInputState.ScreenRightZ + KeyDirs[q].up * HumanInputState.ScreenUpZ;
                        double off = Math.Abs(Math.IEEERemainder(Math.Atan2(wz, wx) - axis, 2 * Math.PI)) * 180 / Math.PI;
                        order.Add((q, off));
                    }
                    order.Sort((p, q) => p.off.CompareTo(q.off));
                    for (int o = 0; o < 2; o++)
                    {
                        var kd = KeyDirs[order[o].idx];
                        double off = order[o].off;
                        if (off > 60.5) continue;
                        if (off > worstKeyAngle) worstKeyAngle = off;
                        int budget = (int)Math.Ceiling(1.25 * travel / (speed * Math.Cos(off * Math.PI / 180)) * C.SimHz) + (int)C.SimHz;
                        var run = new Crossing();
                        Key[] keys = kd.keys;
                        yield return Cross(sx, sz, ux, uz, goal, ox, oz, () => Keys(keys), budget, run);
                        runs++;
                        string kn = string.Join("+", keys);
                        if (!run.Arrived) bad.Add($"{label} keys {kn} ({off:0} deg off): not across in {budget} ticks");
                        if (run.StallTick >= 0) bad.Add($"{label} keys {kn} ({off:0} deg off): stalled 0.1 s at tick {run.StallTick}");
                        if (run.FallTick >= 0) bad.Add($"{label} keys {kn} ({off:0} deg off): fell at tick {run.FallTick}");
                    }
                }
            }
            Debug.Log($"[Creator] need4 {(bad.Count == 0 ? "PASS" : "FAIL")}: {spans} spans x 2 directions, {runs} crossings (stick 0/±30/±45/±60, " +
                      $"the two nearest key combinations, worst key angle {worstKeyAngle:0} deg); failures {bad.Count}" + (bad.Count > 0 ? ":\n" + string.Join("\n", bad) : ""));
            Assert.AreEqual(16, spans, "every bridge span and its mirror");
            Assert.IsTrue(bad.Count == 0, bad.Count + " failure(s):\n" + string.Join("\n", bad));
        }

        // ---------------------------------------------------------------- 5. corruption: carry, instability, berserk, announced

        [UnityTest]
        public IEnumerator Need5_CorruptionCarriedToTheEggRaisesInstabilityLocksBerserkAndTheHatchIsAnnouncedBerserk()
        {
            Restart(ClassId.Miner, 5);
            var banners = new List<string>();
            Action<BannerSpec> onBanner = b => banners.Add((b.Headline ?? "") + " / " + (b.Reason ?? ""));
            Banner.Shown += onBanner;
            try
            {
                // Tour rule for the test: one Miner load (9 instability) locks berserk.
                C.KaijuFeed.RandomBerserk = false;
                C.KaijuFeed.BerserkThreshold = 9;
                int ni = TrialChecks.NearestCorruptionNode(W, W.Map.Shrine.X, W.Map.Shrine.Z);
                Assert.GreaterOrEqual(ni, 0, "a corruption node");
                ResourceNode node = W.Nodes[ni];
                Assert.IsTrue(StandNear(node.X, node.Z, 0.9, 1.4, out double gx, out double gz), "a stand point at the node");
                Teleport(gx, gz);
                yield return Keys(Key.E);
                for (int t = 0; t < 4 * C.SimHz && W.Actors[0].Carry != CarryKind.Corruption; t++) _driver.StepTicks(1);
                yield return Keys();
                Assert.AreEqual(CarryKind.Corruption, W.Actors[0].Carry, "holding E at the node picked up corruption");
                yield return null;
                yield return null;
                var carryState = new HudState();
                HudBinder.Read(W, 0, carryState);
                GameObject lab = GameObject.Find("Ability Name 2");
                string carryLabel = lab != null && lab.GetComponent<UnityEngine.UI.Text>() != null ? lab.GetComponent<UnityEngine.UI.Text>().text : "(no label)";
                bool hudCarry = carryState.Carry == CarryKind.Corruption;
                Assert.IsTrue(hudCarry, "the HUD's state reads CORRUPTION carried (label '" + carryLabel + "')");

                double inst0 = W.Match.Instability;
                ShrineDef sh = W.Map.Shrine;
                Assert.IsTrue(StandNear(sh.X, sh.Z, sh.SolidR + 0.6, C.KaijuFeed.ShrineInteractRange - 0.3, out double ex, out double ez), "a stand point at the egg");
                Teleport(ex, ez);
                yield return Keys(Key.E);
                for (int t = 0; t < 5 * C.SimHz && W.Actors[0].Carry == CarryKind.Corruption; t++) _driver.StepTicks(1);
                yield return Keys();
                yield return null;
                double inst1 = W.Match.Instability;
                Assert.Greater(inst1, inst0, "the delivery raised the egg's instability");
                Assert.IsTrue(W.Match.BerserkLocked, "past the line, berserk locked");
                var hs = new HudState();
                HudBinder.Read(W, 0, hs);
                Assert.Greater(hs.Instability01, 0, "the HUD's instability reads it");
                Assert.IsTrue(hs.BerserkLocked, "the HUD knows it is locked");

                // Hatch now through the natural path: the Kaiju comes out berserk and the hatch is announced.
                C.HatchSeconds = W.Match.Time + 1;
                for (int t = 0; t < 6 * C.SimHz && W.Kaiju.State == KaijuState.Egg; t++)
                {
                    _driver.StepTicks(1);
                    if (t % 5 == 0) yield return null;
                }
                for (int f = 0; f < 10; f++) yield return null;
                Assert.AreEqual(KaijuState.Berserk, W.Kaiju.State, "the egg hatched berserk");
                string all = string.Join(" | ", banners);
                Assert.IsTrue(all.ToUpperInvariant().Contains("BERSERK"), "a banner announced BERSERK: " + all);
                Debug.Log($"[Creator] need5 PASS: instability {inst0:0.0} -> {inst1:0.0} (line {C.KaijuFeed.BerserkThreshold}), HUD carry {carryState.Carry} label '{carryLabel}', " +
                          $"HUD instability {hs.Instability01:0.00} locked {hs.BerserkLocked}; Kaiju {W.Kaiju.State}; banners: {all}");
            }
            finally
            {
                Banner.Shown -= onBanner;
            }
        }

        bool StandNear(double cx, double cz, double r0, double r1, out double x, out double z)
        {
            double r = C.Class(W.Actors[0].Cls).Radius;
            for (double d = r0; d <= r1 + 1e-9; d += 0.2)
            {
                for (int a = 0; a < 360; a += 15)
                {
                    x = cx + Math.Cos(a * Math.PI / 180) * d;
                    z = cz + Math.Sin(a * Math.PI / 180) * d;
                    if (Collide.ActorFits(W, x, z, r)) return true;
                }
            }
            x = z = 0;
            return false;
        }

        static bool AnyActiveText(string s)
        {
            foreach (UnityEngine.UI.Text t in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsSortMode.None))
                if (t.isActiveAndEnabled && t.text != null && t.text.Contains(s)) return true;
            return false;
        }

        // ---------------------------------------------------------------- teaching: guided match, quick demo, the tour

        [UnityTest]
        public IEnumerator Teach_TheTourStartsAsAMinerWithFourPromptsAndTheFirstMatchAndDemoNameTerrainAndCorruption()
        {
            yield return null;
            Assert.IsTrue(CreatorsTour.Begin(), "the tour starts on the running game");
            yield return null;
            try
            {
                Assert.AreEqual(ClassId.Miner, W.Actors[0].Cls, "the tour plays a Miner");
                Assert.AreEqual(4, CreatorsTour.Steps.Length, "four prompts");
                Assert.IsTrue(Trial.IsRunning, "the tour's guide runs");
                Assert.AreEqual("tour-dig", Trial.Guide.Runner.ActiveStep.Id, "the first prompt is dig and place");
                Assert.AreEqual(CreatorsTour.TourBerserkThreshold, C.KaijuFeed.BerserkThreshold);
                Assert.AreEqual(CreatorsTour.TourHatchSeconds, C.HatchSeconds);
                foreach (TrialStep s in CreatorsTour.Steps) Assert.LessOrEqual(s.Prompt.Length, 80, s.Id);
                bool pillarNear = false, bridgeNear = false;
                PadDef pad = W.Map.Pads[(int)TeamId.Azure];
                int k = TrialChecks.PillarFor(W, TeamId.Azure);
                pillarNear = k >= 0 && Math.Abs(W.Map.Decorations[k].Cx - pad.X) + Math.Abs(W.Map.Decorations[k].Cz - pad.Z) < 20;
                bridgeNear = TrialChecks.NearestCellOfType(W.Map, pad.X, pad.Z, Cell.Bridge, out double bx, out double bz) &&
                             Math.Abs(bx - pad.X) + Math.Abs(bz - pad.Z) < 20;
                Assert.IsTrue(pillarNear && bridgeNear, "a pillar and a bridge are within a short walk of the spawn");
                string[] ids = Array.ConvertAll(TrialSteps.Default, s => s.Id);
                CollectionAssert.Contains(ids, "topple", "the first match teaches the knock-down pillar");
                CollectionAssert.Contains(ids, "corruption", "the first match teaches corruption");
                CollectionAssert.Contains(ids, "dig", "the first match teaches digging (Miner)");
                bool demoTerrain = Array.Exists(QuickDemo.Beats, b => b.Id == "demo.terrain");
                bool demoCorrupt = Array.Exists(QuickDemo.Beats, b => b.Id == "demo.corrupt");
                Assert.IsTrue(demoTerrain && demoCorrupt, "the quick demo names terrain and corruption");
                Debug.Log($"[Creator] teach PASS: tour steps {string.Join(", ", Array.ConvertAll(CreatorsTour.Steps, s => s.Id))}; first match {string.Join(", ", ids)}; " +
                          $"demo beats {string.Join(", ", Array.ConvertAll(QuickDemo.Beats, b => b.Id))}");
            }
            finally
            {
                Trial.Stop();
            }
        }
    }
}
