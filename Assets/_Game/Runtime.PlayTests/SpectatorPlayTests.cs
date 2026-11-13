using System;
using System.Collections;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Spectator;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>
    /// 2026-11-12 (spectator and readable prompts) in the real Boot scene with virtual devices (as ControlsPlayTests):
    /// the autopilot banner and the first input taking control; idle autopilot; no on-screen text (HUD, pause menu,
    /// settings) contains LMB, RMB or MMB and the HUD's key plates draw glyphs; spectator follow, cycle, objective and
    /// rejoin through the camera rig's hooks; match setup's SPECTATE; a live class change applied at the next respawn;
    /// and the sim's state hash is unaffected by everything the spectator camera does.
    /// </summary>
    public sealed class SpectatorPlayTests
    {
        const string BootScene = "Assets/_Game/Scenes/Boot.unity";

        SimDriverBehaviour _driver;
        HumanInputSource _input;
        PauseMenu _menu;
        Keyboard _kb;
        Gamepad _pad;
        bool _settingsSaved;
        InputSettings.BackgroundBehavior _savedBackground;
        InputSettings.EditorInputBehaviorInPlayMode _savedEditorBehaviour;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // Capture mode: actor 0 on autopilot and the wall clock stopped, so the world is a pure function of the ticks
            // stepped; reset at once so only this driver starts that way (every overlay then sees a normal match).
            SpectatorMode.SpectateNextMatch = false;
            SimDriverBehaviour.CaptureMode = true;
            SceneManager.LoadScene(BootScene);
            yield return null;
            SimDriverBehaviour.CaptureMode = false;
            yield return null;
            yield return Bind();

            InputSettings s = InputSystem.settings;
            _savedBackground = s.backgroundBehavior;
            _savedEditorBehaviour = s.editorInputBehaviorInPlayMode;
            _settingsSaved = true;
            s.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            s.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _kb = InputSystem.AddDevice<Keyboard>();
            _pad = InputSystem.AddDevice<Gamepad>();
            ControlBindings.SetActiveDevice(ControlDevice.KeyboardMouse);
            yield return null;
        }

        IEnumerator Bind()
        {
            _driver = UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();
            Assert.IsNotNull(_driver, "Boot did not build a SimDriverBehaviour");
            _input = _driver.GetComponent<HumanInputSource>();
            _menu = _driver.GetComponent<PauseMenu>();
            Assert.IsNotNull(_driver.GetComponent<SpectatorMode>(), "the overlay registry did not install SpectatorMode");
            Assert.IsNotNull(_driver.GetComponent<PlayerPresence>(), "the overlay registry did not install PlayerPresence");
            yield return null;
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
            SimDriverBehaviour.CaptureMode = false;
            SpectatorMode.SpectateNextMatch = false;
            PlayerPresence.IdleAutopilotSeconds = PlayerPresence.DefaultIdleAutopilotSeconds;
            PlayerPresence.IdleAutopilotEnabled = true;
            ClassSwitch.Clear();
            _driver = null;
        }

        IEnumerator Send<TState>(InputDevice d, TState state) where TState : struct, IInputStateTypeInfo
        {
            InputSystem.QueueStateEvent(d, state);
            yield return null;
        }

        IEnumerator Tap(Key k)
        {
            yield return Send(_kb, new KeyboardState(k));
            yield return Send(_kb, new KeyboardState());
        }

        IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        // ---------------------------------------------------------------- who is playing

        [UnityTest]
        public IEnumerator AutopilotShowsTheBannerAndTheFirstKeyTakesControl()
        {
            PlayerPresence p = _driver.GetComponent<PlayerPresence>();
            PlayerPresence.IdleAutopilotEnabled = false;
            Assert.IsTrue(_driver.HumanAutopilot, "capture start: actor 0 on autopilot");
            yield return Frames(2);
            Assert.IsTrue(p.BannerVisible, "autopilot in a real match shows the banner");
            yield return Send(_kb, new KeyboardState(Key.W));
            Assert.IsFalse(_driver.HumanAutopilot, "the first key takes control at once");
            yield return Send(_kb, new KeyboardState());
            yield return null;
            Assert.IsFalse(p.BannerVisible);
            Assert.IsTrue(p.IntroVisible, "taking control shows who you are");
            StringAssert.StartsWith("YOU ARE THE AZURE", p.IntroText);
            StringAssert.Contains("MOVE WITH", p.IntroPrompt);
            StringAssert.Contains("W A S D", p.IntroPrompt, "the move keys are drawn from the bindings");
            Assert.IsTrue(p.MarkerVisible, "the YOU tag is over actor 0");

            // A controller button takes control too.
            _driver.HumanAutopilot = true;
            yield return null;
            yield return Send(_pad, new GamepadState().WithButton(GamepadButton.South));
            Assert.IsFalse(_driver.HumanAutopilot, "a controller button takes control");
            yield return Send(_pad, new GamepadState());
        }

        [UnityTest]
        public IEnumerator IdleHandsTheFighterToAutopilotAndAnyInputTakesItBack()
        {
            PlayerPresence p = _driver.GetComponent<PlayerPresence>();
            yield return Tap(Key.D);
            Assert.IsFalse(_driver.HumanAutopilot);
            PlayerPresence.IdleAutopilotSeconds = 0.3f;
            yield return new WaitForSecondsRealtime(0.5f);
            yield return null;
            Assert.IsTrue(_driver.HumanAutopilot, "no input for the idle time: autopilot");
            Assert.IsTrue(p.IdleAutopilot);
            Assert.IsTrue(p.BannerVisible);
            PlayerPresence.IdleAutopilotSeconds = PlayerPresence.DefaultIdleAutopilotSeconds;
            yield return Send(_pad, new GamepadState { leftStick = new Vector2(0f, 1f) });
            Assert.IsFalse(_driver.HumanAutopilot, "a stick push takes control");
            yield return Send(_pad, new GamepadState());
        }

        // ---------------------------------------------------------------- readable prompts

        [UnityTest]
        public IEnumerator NoTextOnScreenShowsLmbRmbOrMmbAndTheHudDrawsGlyphs()
        {
            yield return Frames(3);
            HudPrompts prompts = _driver.GetComponent<HudPrompts>();
            Assert.IsNotNull(prompts);
            Assert.IsNotNull(prompts.Glyph(0), "the HUD's attack plate has a glyph");
            Assert.AreEqual(GlyphKind.Mouse, prompts.Glyph(0).Spec.Kind);
            Assert.AreEqual(GlyphMark.Left, prompts.Glyph(0).Spec.Mark);
            Assert.AreEqual(GlyphMark.Right, prompts.Glyph(1).Spec.Mark);
            Assert.AreEqual("E", prompts.Glyph(2).Spec.Cap);

            _menu.Pause();
            yield return null;
            AssertNoAbbreviations("pause root");
            _menu.ShowSettings(PauseMenu.Tab.Controls);
            yield return null;
            AssertNoAbbreviations("settings, controls tab");
            _menu.ShowClassPage();
            yield return null;
            AssertNoAbbreviations("class page");
            _menu.ShowSwitchTeam();
            yield return null;
            AssertNoAbbreviations("switch team");
            _menu.Resume();
            yield return null;

            ControlBindings.SetActiveDevice(ControlDevice.Gamepad);
            yield return Frames(2);
            Assert.AreEqual(GlyphKind.Trigger, prompts.Glyph(0).Spec.Kind, "controller: the attack plate draws the trigger");
            Assert.AreEqual("RT", prompts.Glyph(0).Spec.Cap);
            AssertNoAbbreviations("controller HUD");
            ControlBindings.SetActiveDevice(ControlDevice.KeyboardMouse);
        }

        static void AssertNoAbbreviations(string where)
        {
            Text[] all = UnityEngine.Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.Greater(all.Length, 0);
            foreach (Text t in all)
                Assert.IsFalse(InputGlyphs.ContainsBannedAbbreviation(t.text), $"{where}: '{t.text}' on {t.name} under {Path(t.transform)}");
        }

        static string Path(Transform t)
        {
            string s = t.name;
            for (Transform p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
            return s;
        }

        // ---------------------------------------------------------------- spectator mode

        [UnityTest]
        public IEnumerator SpectatorFollowsCyclesJumpsToObjectivesAndRejoins()
        {
            SpectatorMode m = _driver.GetComponent<SpectatorMode>();
            CameraFollowTarget follow = UnityEngine.Object.FindAnyObjectByType<CameraFollowTarget>();
            Assert.IsNotNull(follow);
            Vector3 baseOffset = follow.BaseOffset;
            yield return Tap(Key.W);   // take control from the capture autopilot first
            Assert.IsFalse(_driver.HumanAutopilot);

            _menu.Pause();
            yield return null;
            _menu.ToggleSpectate();   // the pause menu's SPECTATE
            yield return null;
            Assert.IsTrue(SpectatorMode.IsSpectating);
            Assert.IsFalse(_input.State.Paused, "spectate resumes the match");
            Assert.IsTrue(_driver.HumanAutopilot, "actor 0 plays itself while spectating");
            Assert.AreEqual(0, m.Rig.Followed, "starts on your own fighter");
            SpectatorOverlay overlay = _driver.GetComponent<SpectatorOverlay>();
            yield return null;
            Assert.IsTrue(overlay.WatchVisible);

            yield return Tap(Key.E);
            Assert.AreEqual(1, m.Rig.Followed, "E: next player");
            yield return Tap(Key.Q);
            Assert.AreEqual(0, m.Rig.Followed, "Q: previous player");
            yield return Send(_pad, new GamepadState().WithButton(GamepadButton.RightShoulder));
            yield return Send(_pad, new GamepadState());
            Assert.AreEqual(1, m.Rig.Followed, "right bumper: next player");
            yield return null;
            StringAssert.Contains(_driver.World.Actors[1].Team == TeamId.Azure ? "AZURE" : "CRIMSON", overlay.FollowName);

            yield return Tap(Key.Digit1);
            Assert.AreEqual(SpectatorFocus.Objective, m.Rig.Focus);
            Assert.AreEqual(SpectatorObjective.Hill, m.Rig.Objective);
            float until = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < until &&
                   Math.Abs(m.Rig.X - _driver.World.Map.Hill.X) + Math.Abs(m.Rig.Z - _driver.World.Map.Hill.Z) > 0.2)
                yield return null;
            yield return null;
            Assert.AreEqual(_driver.World.Map.Hill.X, m.Rig.X, 0.5, "the look point reaches the hill");
            Assert.AreEqual(m.Rig.X, follow.Follow.SX.X, 0.05, "the rig frames the spectator's look point (SetOverride)");
            Assert.AreEqual(m.Rig.Z, follow.Follow.SZ.X, 0.05);

            yield return Tap(Key.T);
            Assert.IsTrue(m.Rig.Tactical);
            yield return null;
            Assert.Greater(follow.BaseOffset.magnitude, baseOffset.magnitude * 1.2f, "the tactical view sits higher");

            yield return Tap(Key.R);
            Assert.IsFalse(SpectatorMode.IsSpectating, "R rejoins at once while standing");
            Assert.IsFalse(_driver.HumanAutopilot, "control is back");
            Assert.AreEqual(baseOffset, follow.BaseOffset, "the rig's offset is restored");
        }

        [UnityTest]
        public IEnumerator RejoinWhileDownWaitsForTheRespawn()
        {
            SpectatorMode m = _driver.GetComponent<SpectatorMode>();
            Assert.IsTrue(SpectatorMode.Enter());
            yield return null;
            World w = _driver.World;
            w.Actors[0].SpawnProtect = 0;   // the test's own knock-out
            int killer = -1;
            for (int i = 1; i < w.ActorCount && killer < 0; i++)
                if (w.Actors[i].Team != w.Actors[0].Team) killer = i;
            Damage.ApplyDamage(w, 0, 1e6, killer, w.X[killer], w.Z[killer]);
            Assert.IsFalse(w.Actors[0].Alive);
            Assert.IsTrue(SpectatorMode.Rejoin());
            Assert.IsTrue(SpectatorMode.IsSpectating, "still watching while down");
            Assert.IsTrue(m.RejoinPending);
            Assert.IsFalse(_driver.HumanAutopilot, "actor 0 is back on the human input at once");
            int guard = (int)Math.Ceiling(60 * w.Config.SimHz);
            while (!w.Actors[0].Alive && guard-- > 0) _driver.StepTicks(1);
            Assert.IsTrue(w.Actors[0].Alive, "respawned");
            yield return null;
            Assert.IsFalse(SpectatorMode.IsSpectating, "control returns at the respawn");
        }

        [UnityTest]
        public IEnumerator MatchSetupSpectateStartsTheMatchWatching()
        {
            SpectatorMode.SpectateNextMatch = true;
            SceneManager.LoadScene(BootScene);
            yield return null;
            yield return null;
            yield return Bind();
            yield return null;
            Assert.IsTrue(SpectatorMode.IsSpectating, "SPECTATE in setup starts the match in spectator mode");
            Assert.IsTrue(_driver.HumanAutopilot);
            Assert.IsFalse(_driver.GetComponent<PlayerPresence>().BannerVisible, "no autopilot banner while spectating");
            SpectatorMode.Rejoin();
            Assert.IsFalse(SpectatorMode.SpectateNextMatch, "rejoining clears the setup choice");
        }

        [UnityTest]
        public IEnumerator ChangeClassAppliesAtTheNextRespawn()
        {
            yield return Tap(Key.W);
            Assert.IsFalse(_driver.HumanAutopilot);
            World w = _driver.World;
            Assert.AreEqual(ClassId.Warrior, w.Actors[0].Cls);
            _menu.Pause();
            yield return null;
            _menu.ShowClassPage();
            _menu.PickClass(ClassId.Miner);
            Assert.IsTrue(ClassSwitch.HasPending);
            Assert.AreEqual(PauseMenu.Page.Root, _menu.CurrentPage, "back to the root with a line saying so");
            _menu.Resume();
            yield return null;
            Assert.AreEqual(ClassId.Warrior, w.Actors[0].Cls, "nothing changes while standing");

            w.Actors[0].SpawnProtect = 0;
            int killer = -1;
            for (int i = 1; i < w.ActorCount && killer < 0; i++)
                if (w.Actors[i].Team != w.Actors[0].Team) killer = i;
            Damage.ApplyDamage(w, 0, 1e6, killer, w.X[killer], w.Z[killer]);
            int guard = (int)Math.Ceiling(60 * w.Config.SimHz);
            while (!w.Actors[0].Alive && guard-- > 0)
            {
                yield return null;   // ClassSwitch queues the pick while actor 0 is down
                _driver.StepTicks(1);
            }
            Assert.IsTrue(w.Actors[0].Alive);
            Assert.AreEqual(ClassId.Miner, w.Actors[0].Cls, "respawned as the picked class");
            yield return null;
            Assert.IsFalse(ClassSwitch.HasPending, "applied and cleared");
        }

        // ---------------------------------------------------------------- determinism

        [UnityTest]
        public IEnumerator TheSimStateHashIsUnaffectedBySpectatorCamera()
        {
            Assert.AreEqual(0, _driver.World.Tick, "capture: no ticks before the test");
            Assert.AreEqual(0.0, _driver.TimeScale);
            WorldOptions o = _driver.World.Opts;
            World reference = World.CreateWorld(new WorldOptions
            {
                Seed = o.Seed,
                TeamSize = o.TeamSize,
                HumanTeam = o.HumanTeam,
                HumanClass = o.HumanClass,
                Config = o.Config,
            });
            InputFrame scratch = SimInput.EmptyInput();
            Assert.IsTrue(SpectatorMode.Enter());
            SpectatorMode m = _driver.GetComponent<SpectatorMode>();
            const int ticks = 240;
            for (int t = 0; t < ticks; t++)
            {
                if (t % 20 == 0) yield return Tap(t % 40 == 0 ? Key.E : Key.Q);
                if (t % 60 == 30) yield return Tap(Key.Digit1 + (t / 60 % 5));
                if (t % 90 == 45) yield return Tap(Key.T);
                if (t % 50 == 25) m.PickUnderPointer(_driver.World, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
                yield return null;
                _driver.StepTicks(1);
                MatchTick.Advance(reference, null, scratch);
            }
            Assert.AreEqual(ticks, _driver.World.Tick);
            Assert.AreEqual(StateHash(reference), StateHash(_driver.World),
                "spectating (cuts, objectives, tactical view, picks) never changes the sim");
            SpectatorMode.Rejoin();
        }

        /// <summary>The state hash of SpectatorRigTests (EditMode), repeated: the PlayMode assembly cannot see that one.</summary>
        static ulong StateHash(World w)
        {
            ulong h = 1469598103934665603UL;
            void Mix(double v)
            {
                long bits = BitConverter.DoubleToInt64Bits(v);
                for (int i = 0; i < 8; i++)
                {
                    h ^= (ulong)((bits >> (i * 8)) & 0xFF);
                    h *= 1099511628211UL;
                }
            }
            Mix(w.Tick);
            Mix(w.EventHead);
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                Mix(w.X[i]);
                Mix(w.Z[i]);
                Mix(w.Vx[i]);
                Mix(w.Vz[i]);
                Mix(a.Hp);
                Mix(a.Alive ? 1 : 0);
                Mix((int)a.Cls);
                Mix(a.Kills);
                Mix(a.Deaths);
                Mix(a.Facing);
            }
            if (w.Kaiju != null)
            {
                Mix(w.Kaiju.X);
                Mix(w.Kaiju.Z);
                Mix(w.Kaiju.Hp);
                Mix((int)w.Kaiju.State);
            }
            Mix(w.Match.HillSeconds.Azure);
            Mix(w.Match.HillSeconds.Crimson);
            Mix(w.Match.Time);
            return h;
        }
    }
}
