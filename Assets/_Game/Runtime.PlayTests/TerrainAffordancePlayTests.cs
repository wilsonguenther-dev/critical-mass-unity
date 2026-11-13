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
        bool FindSpot(Func<AffordanceScan, bool> ok, int maxRing = 60)
        {
            var scan = new AffordanceScan { RouteBudget = 0 };
            MapDef m = W.Map;
            int sx = JsMath.FloorToInt(W.X[0]), sz = JsMath.FloorToInt(W.Z[0]);
            for (int ring = 0; ring <= maxRing; ring++)
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
            Assert.Greater(v.MarkersDrawn, 0, "markers on usable terrain");
            Assert.IsTrue(v.OutlineDrawn, "the strong outline on the target");
            StringAssert.Contains(verb, v.PromptText, "the prompt names the action");
            Assert.LessOrEqual(v.QuadsDrawn, TerrainAffordanceView.MaxQuads);
        }

        [UnityTest]
        public IEnumerator Miner_MarkersShow_AndFDigsTheOutlinedCell()
        {
            Restart(ClassId.Miner);
            double start = W.Match.Time;
            TerrainAffordanceView v = View();
            Assert.IsTrue(FindSpot(s => s.Primary.Valid && s.MarkerCount >= 3), "a diggable patch");
            yield return Shows(v, "DIG");
            StringAssert.Contains(TerrainAffordanceView.Glyph("DigPlace", ControlBindings.ActiveDevice), v.PromptText, "the prompt names the bound key");
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

        [UnityTest]
        public IEnumerator Builder_GhostShows_AndAbilityBuildsThePiece()
        {
            Restart(ClassId.Builder);
            double start = W.Match.Time;
            TerrainAffordanceView v = View();
            Assert.IsTrue(FindSpot(s => s.Primary.Action == AffAction.Build && s.Primary.Valid), "a cliff or gap to build across");
            yield return Shows(v, "BUILD");
            Assert.Greater(v.GhostCellsDrawn, 0, "the placement ghost");
            Assert.IsTrue(v.GhostValid, "green when valid");
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
            Restart(ClassId.Miner);
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
            Assert.Greater(v.MarkersDrawn, 0);
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
