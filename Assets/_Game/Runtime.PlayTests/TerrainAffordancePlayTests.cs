using System;
using System.Collections;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.View.Affordance;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>
    /// feat/terrain-affordances: each terrain class plays 20 s of match time with scripted keyboard and pad input; the
    /// markers, the strong target and the prompt appear, the action succeeds on the marked target, the settings switch each
    /// element off, and nothing is drawn while a bot drives actor 0.
    /// </summary>
    public sealed class TerrainAffordancePlayTests
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
            WorldFeedbackSettings.ResetAll();
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
            WorldFeedbackSettings.ResetAll();
            TerrainAffordanceView.ShowAllForced = false;
            _driver = null;
        }

        void Restart(ClassId cls)
        {
            _driver.Config.Bots.PassiveSeconds = 100000;
            _driver.Config.MatchSeconds = 100000;
            _driver.Config.HatchSeconds = 100000;
            World.ResetWorld(W, new WorldOptions { Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = cls, Config = _driver.Config });
            _driver.StepTicks((int)Math.Ceiling(3 * C.SimHz));
            Assert.IsTrue(W.Actors[0].Alive);
            Assert.AreEqual(cls, W.Actors[0].Cls);
        }

        void Put(double x, double z, double facing)
        {
            W.X[0] = W.PrevX[0] = x;
            W.Z[0] = W.PrevZ[0] = z;
            W.Vx[0] = 0;
            W.Vz[0] = 0;
            W.Actors[0].Facing = facing;
        }

        /// <summary>A floor cell and facing near the spawn where ok(scan) holds; actor 0 is left standing there.</summary>
        bool FindSpot(Func<AffordanceScan, bool> ok, int maxRing = 60, int minRing = 0)
        {
            var scan = new AffordanceScan { RouteBudget = 0 };
            MapDef m = W.Map;
            int sx = JsMath.FloorToInt(W.X[0]), sz = JsMath.FloorToInt(W.Z[0]);
            for (int ring = minRing; ring <= maxRing; ring++)
            for (int cz = sz - ring; cz <= sz + ring; cz++)
            for (int cx = sx - ring; cx <= sx + ring; cx++)
            {
                if (Math.Abs(cx - sx) != ring && Math.Abs(cz - sz) != ring) continue;
                if (!MapGrid.InBounds(m, cx, cz) || m.Cells[cz * m.Width + cx] != Cell.Floor) continue;
                for (int f = 0; f < 4; f++)
                {
                    Put(cx + 0.5, cz + 0.5, f * Math.PI / 2);
                    scan.Scan(W, 0, false, double.NaN, double.NaN, false);
                    if (ok(scan)) return true;
                }
            }
            return false;
        }

        IEnumerator Keys(params Key[] keys)
        {
            InputSystem.QueueStateEvent(_kb, new KeyboardState(keys));
            InputSystem.Update();
            yield return null;
        }

        IEnumerator Pad(GamepadButton? button)
        {
            var st = new GamepadState();
            if (button.HasValue) st = st.WithButton(button.Value);
            InputSystem.QueueStateEvent(_pad, st);
            InputSystem.Update();
            yield return null;
        }

        TerrainAffordanceView View()
        {
            TerrainAffordanceView v = TerrainAffordanceView.Instance;
            Assert.IsNotNull(v, "the terrain affordance overlay is installed in a match");
            return v;
        }

        int Count(int head, SimEventType type)
        {
            int n = 0;
            for (int c = Math.Max(head, W.EventHead - World.EventCapacity); c < W.EventHead; c++) if (W.Events[c % World.EventCapacity].Type == type) n++;
            return n;
        }

        /// <summary>The rest of the 20 s: the view keeps showing for him while the match runs.</summary>
        IEnumerator PlayOut(TerrainAffordanceView v, double start)
        {
            int seen = 0, frames = 0;
            while (W.Match.Time < start + 20)
            {
                _driver.StepTicks((int)C.SimHz / 2);
                yield return null;
                frames++;
                if (v.Showing) seen++;
            }
            Assert.Greater(seen, frames / 2, "the affordances stay up for the local player through the 20 s");
        }

        IEnumerator Shows(TerrainAffordanceView v, string verb)
        {
            _driver.StepTicks(1);
            yield return null;
            yield return null;
            Assert.IsTrue(v.Showing, "showing for the local terrain class");
            // b14 (owner: hint tiles everywhere): cell markers only in the short reach window after a tool / class pick, or
            // while the show key is held; the target is outlined only when there is something real to point at.
            Debug.Log($"[Affordance] markers {v.MarkersDrawn}, reach window {v.ReachWindow01:0.00}, show-all {v.ShowAllHeld}, outline {v.OutlineDrawn}, mark {v.Scan.Primary.Mark}");
            Assert.IsTrue(v.MarkersDrawn == 0 || v.ReachWindowActive || v.ShowAllHeld, "markers only in the reach window or while the show key is held");
            Assert.IsTrue(v.OutlineDrawn || !v.Scan.Primary.Mark, "the strong outline on a real target");
            StringAssert.Contains(verb, v.PromptText, "the prompt names the action");
            Assert.LessOrEqual(v.QuadsDrawn, TerrainAffordanceView.MaxQuads);
        }

        [UnityTest]
        public IEnumerator Builder_DigMarkersShow_AndFDigsTheOutlinedCell()
        {
            // b13 r2 (owner decision): dig / place is the Builder's while the hut rule is on; it is his second prompt line
            // (the first stays Space STAIRS / BRIDGE) unless E has a piece to carry.
            Restart(ClassId.Builder);
            double start = W.Match.Time;
            TerrainAffordanceView v = View();
            // b14: the Builder's one tool wheel: with DIG selected, dig is his primary (F).
            Assert.IsTrue(FindSpot(s => s.Secondary.Has && s.Secondary.Action == AffAction.Dig && s.Secondary.Valid && s.MarkerCount >= 3), "a diggable patch");
            Assert.IsTrue(BuilderToolbelt.Local != null && BuilderToolbelt.Local.Pick(W, 0, BuilderTool.Dig), "DIG on the wheel");
            yield return Shows(v, "DIG");
            StringAssert.Contains(TerrainAffordanceView.Glyph("DigPlace", ControlBindings.ActiveDevice), v.PromptText, "the prompt names the bound key");
            Assert.AreEqual(AffAction.Dig, v.Scan.Primary.Action, "the view's own scan offers DIG");
            int cx = v.Scan.Primary.Cx, cz = v.Scan.Primary.Cz;
            int head = W.EventHead;
            yield return Keys(Key.F);
            _driver.StepTicks(1);
            yield return Keys();
            Assert.AreEqual(Cell.Void, W.Map.Cells[cz * W.Map.Width + cx], "F dug the outlined cell");
            Assert.GreaterOrEqual(Count(head, SimEventType.Terraform), 1);
            yield return null;
            yield return PlayOut(v, start);
        }

        /// <summary>b14 ladder fix: the view (and the belt's press) refuse a ladder that is no real shortcut over the rock
        /// ("no shortcut here"); the batch 13 scan FindSpot uses does not, so a ladder spot must pass the same check along the
        /// facing axis.</summary>
        bool UsefulLadderAhead()
        {
            double f = W.Actors[0].Facing;
            int dx = (int)Math.Round(Math.Cos(f)), dz = (int)Math.Round(Math.Sin(f));
            if (BuiltPieces.Plan(W, 0, dx, dz, BuiltPieces.Ladder) != 0) return false;
            // integrator b15: a height ladder (pit wall / stack side) is always a climb, as AffordanceCore treats it
            if (TerrainHeight.On(W) && BuiltPieces.LastPlanCount == 1 && TerrainHeight.IsLiftType(W.Map.Cells[BuiltPieces.LastPlanCell(0)])) return true;
            return CriticalMass.Runtime.View.Mechanics.LadderClimb.LastPlanIsShortcut(W, 0, dx, dz);
        }

        [UnityTest]
        public IEnumerator Builder_GhostShows_AndAbilityBuildsThePiece()
        {
            Restart(ClassId.Builder);
            double start = W.Match.Time;
            TerrainAffordanceView v = View();
            // b13 r2: the spot is found with a facing-only scan, but the view scans a tick later with its own aim and a crowd
            // that moved (bots now walk to the ore veins and the hut), so one spot can turn red in between. Each try prints the
            // view's verdict; the ghost colour must always equal it, and some try must be green.
            // b14: the ladder / bridge is a tool on the wheel; F (or the ability button, B on a pad) uses it.
            // b14 ladder fix: a ladder that is no shortcut over the rock (a step round the wall's end) is refused with its reason.
            // Checked with the belt scan along the facing axis (the live view picks its axis from the mouse aim, so it may
            // refuse the same spot for another reason such as "face a rock wall").
            if (FindSpot(s => s.Primary.Action == AffAction.Build && s.Primary.Valid && s.Primary.Verb.Contains("LADDER") && !UsefulLadderAhead()))
            {
                double f = W.Actors[0].Facing;
                var belt = new AffordanceScan { RouteBudget = 0, Belt = true, Tool = BuilderTool.Ladder, ToolDx = (int)Math.Round(Math.Cos(f)), ToolDz = (int)Math.Round(Math.Sin(f)) };
                belt.Scan(W, 0, false, double.NaN, double.NaN, false);
                Debug.Log($"[Affordance] Builder useless ladder (belt scan, axis {belt.ToolDx},{belt.ToolDz}): verdict {belt.Primary.Valid} ({belt.Primary.Why}), verb {belt.Primary.Verb}");
                Assert.IsFalse(belt.Primary.Valid, "a ladder with no shortcut is refused");
                Assert.AreEqual(AffWords.NoShortcut, belt.Primary.Why, "the refused ladder says why");
            }
            else Debug.Log("[Affordance] Builder useless ladder: no such spot on this map (limit: none)");
            bool green = false;
            for (int k = 0; k < 6 && !green; k++)
            {
                bool ladder = false;
                Assert.IsTrue(FindSpot(s => { bool ok = s.Primary.Action == AffAction.Build && s.Primary.Valid; if (ok) ladder = s.Primary.Verb.Contains("LADDER"); if (ok && ladder) ok = UsefulLadderAhead(); return ok; }, 60, k * 6), "a cliff or gap to build across");
                Assert.IsTrue(BuilderToolbelt.Local != null && BuilderToolbelt.Local.Pick(W, 0, ladder ? BuilderTool.Ladder : BuilderTool.Bridge), "the matching tool on the wheel");
                // b14 ladder fix: a ladder's prompt says what it does ("LADDER TO THE HILL" / "LADDER OVER THE WALL"), a bridge's stays "BUILD BRIDGE".
                yield return Shows(v, ladder ? "LADDER" : "BUILD");
                Debug.Log($"[Affordance] Builder try {k}: view verdict {v.Scan.Primary.Valid} ({v.Scan.Primary.Why}), ghost cells {v.GhostCellsDrawn}, ghost green {v.GhostValid}");
                Assert.AreEqual(v.Scan.Primary.Valid, v.GhostValid, "the ghost colour is the view's own verdict");
                green = v.GhostValid;
            }
            Assert.Greater(v.GhostCellsDrawn, 0, "the placement ghost");
            Assert.IsTrue(green, "green when valid (tries above)");
            Debug.Log($"[Affordance] Builder green spot: tool {BuilderToolbelt.Local.Selected}, verb {v.Scan.Primary.Verb}, ghost cells {v.GhostCellsDrawn}");
            int head = W.EventHead;
            yield return Pad(GamepadButton.East);
            _driver.StepTicks(1);
            yield return Pad(null);
            _driver.StepTicks(2);
            Assert.AreEqual(1, Count(head, SimEventType.PiecePlaced), "the pad's ability button built the ghosted piece");
            yield return null;
            yield return PlayOut(v, start);
        }

        [UnityTest]
        public IEnumerator Farmer_NodeMarked_AndHoldingInteractGathers()
        {
            Restart(ClassId.Farmer);
            double start = W.Match.Time;
            TerrainAffordanceView v = View();
            Assert.IsTrue(FindSpot(s => s.Primary.Valid && s.Primary.Action == AffAction.Gather), "a node in reach");
            yield return Shows(v, "GATHER");
            int before = W.Actors[0].CarryAmount;
            yield return Keys(Key.E);
            bool bar = false;
            for (int t = 0; t < 6 * C.SimHz && W.Actors[0].CarryAmount == before; t += 3)
            {
                _driver.StepTicks(3);
                yield return null;
                if (v.BarValue >= 0) bar = true;
            }
            yield return Keys();
            Assert.Greater(W.Actors[0].CarryAmount, before, "holding E at the marked node gathered");
            Assert.IsTrue(bar, "the progress bar showed the gather timer");
            yield return PlayOut(v, start);
        }

        [UnityTest]
        public IEnumerator Demolitions_BlastShows_AndFThrowsTnt()
        {
            Restart(ClassId.Demolitions);
            double start = W.Match.Time;
            TerrainAffordanceView v = View();
            Assert.IsTrue(FindSpot(s => s.Primary.Valid && s.MarkerCount >= 1), "a spot with something to blow up");
            yield return Shows(v, "TNT");
            Assert.IsTrue(v.GhostCellsDrawn >= 0);
            int head = W.EventHead;
            yield return Keys(Key.F);
            _driver.StepTicks(1);
            yield return Keys();
            Assert.AreEqual(1, Count(head, SimEventType.TntThrown), "F threw the TNT at the shown circle");
            yield return PlayOut(v, start);
        }

        [UnityTest]
        public IEnumerator Settings_SwitchEachElementOff_AndBotsGetNothing()
        {
            Restart(ClassId.Builder);   // b13 r2: the Miner has no terrain affordance with the hut rule (he mines)
            TerrainAffordanceView v = View();
            Assert.IsTrue(FindSpot(s => s.Primary.Valid && s.MarkerCount >= 3));
            yield return Shows(v, "DIG");
            WorldFeedbackSettings.Markers = false;
            yield return null;
            Assert.AreEqual(0, v.MarkersDrawn, "markers off");
            WorldFeedbackSettings.Outline = false;
            yield return null;
            Assert.IsFalse(v.OutlineDrawn, "outline off");
            WorldFeedbackSettings.Prompts = false;
            yield return null;
            Assert.IsEmpty(v.PromptText, "prompt off");
            TerrainAffordanceView.ShowAllForced = true;
            yield return null;
            Assert.Greater(v.MarkersDrawn, 0, "holding the show-all key shows them even with markers off");
            TerrainAffordanceView.ShowAllForced = false;
            WorldFeedbackSettings.ResetAll();
            yield return null;
            Assert.IsTrue(v.MarkersDrawn == 0 || v.ReachWindowActive, "b14: only the aimed cell outside the reach window");
            _driver.HumanAutopilot = true;
            yield return null;
            Assert.IsFalse(v.Showing, "never for a bot-driven actor 0");
            Assert.AreEqual(0, v.MarkersDrawn);
            _driver.HumanAutopilot = false;
            // One draw call: one renderer, one material.
            Assert.IsNotNull(v.Material, "the CM/Affordance shader is in Resources");
            Assert.AreEqual(TerrainAffordanceView.MaxQuads * 4, v.Mesh.vertexCount);
        }
    }
}
