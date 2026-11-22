using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Online;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>
    /// b16 finish: what the batch 16 Editor review could not see, staged with no window and no sound (batch mode with a
    /// graphics device, never -nographics; the camera renders into a 1920x1080 texture written as PNG). Explicit and
    /// RequiresGpu: never part of a CI run; run by name on a Mac:
    ///   Unity -batchmode -projectPath . -runTests -testPlatform PlayMode -testFilter B16FinishCapturePlayTests
    /// with CM_CAPTURE_DIR set to the evidence folder. Every match here is local: nothing is sent anywhere (the online
    /// room is an in-memory LoopbackNetwork; no relay, no direct listener).
    /// </summary>
    [Explicit("captures for the b16 finish; run by name")]
    [Category("RequiresGpu")]
    public sealed class B16FinishCapturePlayTests
    {
        static string Dir => Environment.GetEnvironmentVariable("CM_CAPTURE_DIR") is string d && d.Length > 0
            ? d : Path.Combine(Application.temporaryCachePath, "b16-finish-captures");

        SanityWorld _w;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode _editorInput;
        bool _restoreInput;
#endif
        readonly List<InputDevice> _devices = new List<InputDevice>();

        static void Mute()
        {
            AudioListener.volume = 0f;
            AudioListener.pause = true;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Mute();
            Directory.CreateDirectory(Dir);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try { OnlineMatch.Leave(); } catch (Exception e) { Debug.Log("[b16cap] leave: " + e.Message); }
            foreach (InputDevice d in _devices) if (d != null && d.added) InputSystem.RemoveDevice(d);
            _devices.Clear();
            ChatSettings.TypedText = false;   // ships off; a test turned it on only for the Esc order
#if UNITY_EDITOR
            if (_restoreInput) { InputSystem.settings.editorInputBehaviorInPlayMode = _editorInput; _restoreInput = false; }
#endif
            SanityWorld.Reset();
            yield return null;
        }

        static void Log(string s) => Debug.Log("[b16cap] " + s);

        /// <summary>Renders Camera.main (every overlay canvas moved onto it for the shot) into 1920x1080 and writes NAME.png.</summary>
        static IEnumerator Capture(string name)
        {
            Mute();
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            Assert.IsNotNull(cam, "no main camera to capture " + name);
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            var moved = new List<Canvas>();
            foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay || !c.enabled) continue;
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = Mathf.Max(0.5f, cam.nearClipPlane * 1.5f) + 0.01f;
                moved.Add(c);
            }
            RenderTexture prev = cam.targetTexture;
            cam.targetTexture = rt;
            for (int i = 0; i < 4; i++) yield return null;   // the HUD scale follows the 1920x1080 target
            Canvas.ForceUpdateCanvases();
            try { cam.Render(); } catch (Exception e) { Log("Render: " + e.Message + " (using the frame's own render)"); }
            RenderTexture old = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            cam.targetTexture = prev;
            foreach (Canvas c in moved) if (c != null) c.renderMode = RenderMode.ScreenSpaceOverlay;
            string path = Path.Combine(Dir, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(rt);
            Log("captured " + path);
        }

        static void Put(World w, int i, double x, double z)
        {
            w.X[i] = x;
            w.Z[i] = z;
        }

        static int FindActor(World w, TeamId team, Func<ClassId, bool> cls, ICollection<int> skip)
        {
            for (int i = 1; i < w.ActorCount; i++)
                if (w.Actors[i].Alive && w.Actors[i].Team == team && cls(w.Actors[i].Cls) && !skip.Contains(i)) return i;
            return -1;
        }

        static int CountFeints(World w, int from, int actor, int value)
        {
            int n = 0;
            for (int e = Math.Max(from, w.EventHead - World.EventCapacity + 1); e < w.EventHead; e++)
            {
                SimEvent ev = w.Events[e % World.EventCapacity];
                if (ev != null && ev.Type == SimEventType.Feint && ev.A == actor && (value < 0 || ev.Value == value)) n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ (ii) hill count, Settings, Codex, key caps

        [UnityTest]
        public IEnumerator HillCountSettingsCodexAndKeyCaps()
        {
            _w = new SanityWorld();
            yield return _w.Load(ClassId.Warrior);
            World w = _w.W;
            double hx = w.Map.Hill.X, hz = w.Map.Hill.Z;
            // 3 Azure Warriors + 1 Azure other on the hill: 4 bodies, 7 points; 2 Crimson Warriors: 4 points
            var used = new List<int> { 0 };
            Put(w, 0, hx - 20, hz - 20);
            double[,] spots = { { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 }, { 2, 1 }, { -2, -1 }, { 1, 2 }, { -1, -2 }, { 2, -2 }, { -2, 2 }, { 0, 0 } };
            int k = 0;
            for (int n = 0; n < 3; n++) { int a = FindActor(w, TeamId.Azure, c => c == ClassId.Warrior, used); if (a < 0) break; used.Add(a); Put(w, a, hx + spots[k, 0], hz + spots[k, 1]); k++; }
            { int a = FindActor(w, TeamId.Azure, c => c != ClassId.Warrior, used); if (a >= 0) { used.Add(a); Put(w, a, hx + spots[k, 0], hz + spots[k, 1]); k++; } }
            for (int n = 0; n < 2; n++) { int a = FindActor(w, TeamId.Crimson, c => c == ClassId.Warrior, used); if (a < 0) break; used.Add(a); Put(w, a, hx + spots[k, 0], hz + spots[k, 1]); k++; }
            // everyone else off the hill
            for (int i = 1; i < w.ActorCount; i++)
                if (!used.Contains(i)) { double dx = w.X[i] - hx, dz = w.Z[i] - hz; if (dx * dx + dz * dz < 100) Put(w, i, hx + 14 + (i % 4), hz - 14 - (i % 5)); }
            _w.Tick(1);
            Log("hill staged: weighted " + w.Mech.HillWeighted[0] + ":" + w.Mech.HillWeighted[1] + " counted " + w.Mech.HillCounted[0] + ":" + w.Mech.HillCounted[1]
                + " bodies " + w.Match.HillCount.Azure + ":" + w.Match.HillCount.Crimson);
            yield return Capture("p1-hill-staged");
            // capped: 11 Azure bodies on the hill
            var all = new List<int> { 0 };
            for (int n = 0; n < 11; n++) { int a = FindActor(w, TeamId.Azure, c => true, all); if (a < 0) break; all.Add(a); Put(w, a, hx + spots[n, 0] * 1.2, hz + spots[n, 1] * 1.2); }
            for (int i = 1; i < w.ActorCount; i++)
                if (w.Actors[i].Team == TeamId.Crimson) { double dx = w.X[i] - hx, dz = w.Z[i] - hz; if (dx * dx + dz * dz < 100) Put(w, i, hx + 14 + (i % 4), hz - 14 - (i % 5)); }
            _w.Tick(1);
            Log("hill capped: weighted " + w.Mech.HillWeighted[0] + " counted " + w.Mech.HillCounted[0] + " cap " + MechRules.HillCap(w));
            yield return Capture("p1-hill-capped");

            PauseMenu pm = _w.Driver.GetComponent<PauseMenu>() ?? Object.FindAnyObjectByType<PauseMenu>();
            Assert.IsNotNull(pm, "no PauseMenu in the match");
            pm.Pause();
            yield return null;
            pm.ShowSettings(PauseMenu.Tab.Game);
            yield return null;
            Assert.AreEqual(PauseMenu.Page.Settings, pm.CurrentPage, "Settings did not open");
            Log("settings open: page " + pm.CurrentPage + " anyOpen " + PauseMenu.AnyOpen);
            yield return Capture("p2-settings-game");
            pm.ShowSettings(PauseMenu.Tab.Controls);
            yield return null;
            yield return Capture("p2-settings-controls");
            int codex = pm.ExtraPageIndex(CriticalMass.Runtime.Codex.CodexPage.Label);
            Assert.GreaterOrEqual(codex, 0, "the pause menu has no CODEX page");
            pm.ShowExtra(codex);
            yield return null;
            Assert.AreEqual(PauseMenu.Page.Extra, pm.CurrentPage, "the Codex did not open");
            yield return Capture("p2-codex");
            pm.Resume();
            yield return null;
        }

        // ------------------------------------------------------------------ (i) cancel: feint in the wind-up, cancel after a hit

        [UnityTest]
        public IEnumerator CancelFeintAndHitCancel()
        {
            _w = new SanityWorld();
            yield return _w.Load(ClassId.Warrior);
            World w = _w.W;
            HumanInputState inp = _w.HumanInput;
            Assert.IsTrue(w.Config.Depth.CombatFeel.CancelEnabled, "the cancel rule is on in Full");
            Assert.IsTrue(inp.CancelButton, "the CANCEL BUTTON setting is on");
            int foe = FindActor(w, TeamId.Crimson, c => c == ClassId.Farmer, new List<int>());
            Assert.GreaterOrEqual(foe, 0);
            SanityWorld.FromScreenIntent(1, 0, out double dx, out double dz);
            Put(w, foe, w.X[0] + 1.7 * dx, w.Z[0] + 1.7 * dz);
            inp.SetAim(w.X[0] + 4 * dx, w.Z[0] + 4 * dz);
            _w.Tick(1);

            // feint during the wind-up
            int head = w.EventHead;
            inp.ButtonDown(HumanInputState.HumanButton.Attack);
            _w.Tick(1);
            inp.ButtonUp(HumanInputState.HumanButton.Attack);
            _w.Tick(2);
            Assert.IsTrue(w.Actors[0].SwingPending, "the press started a wind-up");
            yield return Capture("p2-cancel-1-windup");
            double hpBefore = w.Actors[foe].Hp;
            double cdBefore = w.Actors[0].AttackCd;
            inp.ButtonDown(HumanInputState.HumanButton.Cancel);
            _w.Tick(1);
            inp.ButtonUp(HumanInputState.HumanButton.Cancel);
            Assert.IsFalse(w.Actors[0].SwingPending, "the cancel dropped the wind-up the same tick");
            Assert.AreEqual(1, CountFeints(w, head, 0, 1), "one Feint event (Value 1) for the view's flick");
            Log("feint: cd " + cdBefore.ToString("0.###") + " -> " + w.Actors[0].AttackCd.ToString("0.###"));
            yield return Capture("p2-cancel-2-feint-flick");
            _w.Tick(30);
            Assert.AreEqual(hpBefore, w.Actors[foe].Hp, "a feinted swing never lands");

            // cancel after a landed hit
            for (int t = 0; t < 120 && w.Actors[0].AttackCd > 0; t++) _w.Tick(1);
            Put(w, foe, w.X[0] + 1.6 * dx, w.Z[0] + 1.6 * dz);
            inp.SetAim(w.X[0] + 4 * dx, w.Z[0] + 4 * dz);
            double hp0 = w.Actors[foe].Hp;
            inp.ButtonDown(HumanInputState.HumanButton.Attack);
            _w.Tick(1);
            inp.ButtonUp(HumanInputState.HumanButton.Attack);
            int landedAt = -1;
            for (int t = 0; t < 60; t++)
            {
                // the Farmer's bot brain walks off between ticks: hold it in front of the swing until it lands
                if (w.Actors[foe].Alive) Put(w, foe, w.X[0] + 1.6 * dx, w.Z[0] + 1.6 * dz);
                _w.Tick(1);
                if (w.Actors[foe].Hp < hp0 || !w.Actors[foe].Alive) { landedAt = t; break; }
            }
            Assert.GreaterOrEqual(landedAt, 0, "the second swing did not land on the Farmer");
            yield return Capture("p2-cancel-3-hit-landed");
            double cdHit = w.Actors[0].AttackCd;
            int head2 = w.EventHead;
            inp.ButtonDown(HumanInputState.HumanButton.Cancel);
            _w.Tick(1);
            inp.ButtonUp(HumanInputState.HumanButton.Cancel);
            double cdAfter = w.Actors[0].AttackCd;
            int hitCancels = CountFeints(w, head2, 0, -1);
            Log("hit-cancel: cd " + cdHit.ToString("0.###") + " -> " + cdAfter.ToString("0.###") + ", feint-type events " + hitCancels);
            Assert.Less(cdAfter, cdHit - 1.5 / w.Config.SimHz, "the cancel after a landed hit cut the recovery");
            yield return Capture("p2-cancel-4-after-hit-cancel");
        }

        // ------------------------------------------------------------------ (iii) chat: pause panel, Esc order, quick-chat wheel

        static void Key(Keyboard kb, params Key[] keys) => InputSystem.QueueStateEvent(kb, new KeyboardState(keys));

        [UnityTest]
        public IEnumerator OnlineChatPausePanelEscOrderAndWheel()
        {
            Assert.IsFalse(ChatSettings.TypedText, "typed chat ships off");

            var net = new CriticalMass.Net.LoopbackNetwork(7, 0, 0, 0);
            OnlineMatch.HostRoom(net.CreatePeer(), null, "Host");
            Assert.IsTrue(OnlineMatch.InRoom, "the loopback room did not open");
            string why = null;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 10f)
            {
                why = OnlineMatch.StartAsHost();
                if (why == null) break;
                yield return null;
            }
            Assert.IsNull(why, "a one-person loopback room could not start: " + why);
            for (int f = 0; f < 600 && UnityEngine.Object.FindAnyObjectByType<CriticalMass.Runtime.Game.SimDriverBehaviour>() == null; f++) yield return null;
            for (int f = 0; f < 120; f++) yield return null;
            Mute();
            Assert.IsTrue(OnlineMatch.InMatch, "no online session");
            var pad = InputSystem.AddDevice<Gamepad>("B16CapPad");
            _devices.Add(pad);
            yield return Capture("p2-chat-0-match");

            // quick-chat wheel: hold D-pad right
            InputSystem.QueueStateEvent(pad, new GamepadState());
            for (int f = 0; f < 3; f++) yield return null;   // the pad becomes the active device first
            float h0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - h0 < 2f && !OnlineChatHud.WheelOpen)
            {
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.DpadRight));
                yield return null;
            }
            Log("wheel hold took " + (Time.realtimeSinceStartup - h0).ToString("0.00") + " s (rule " + OnlineChatHud.HoldSeconds + " s)");
            bool wheel = OnlineChatHud.WheelOpen;
            Log("wheel open after hold: " + wheel);
            if (wheel)
            {
                InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = new Vector2(1f, 0f) }.WithButton(GamepadButton.DpadRight));
                for (int f = 0; f < 6; f++) yield return null;
                yield return Capture("p2-chat-1-wheel");
            }
            InputSystem.QueueStateEvent(pad, new GamepadState());
            for (int f = 0; f < 6; f++) yield return null;
            Log("wheel open after release: " + OnlineChatHud.WheelOpen);
            Assert.IsTrue(wheel, "the quick-chat wheel did not open on a D-pad right hold");
            Assert.IsFalse(OnlineChatHud.WheelOpen, "the wheel closes on release");
            yield return Capture("p2-chat-2-after-callout");

            // Esc order: typing first, then the pause menu (the keyboard joins only now, so the wheel above ran on the pad)
#if UNITY_EDITOR
            // a batch-mode Editor has no focused Game view: let the virtual keyboard reach the game (restored in TearDown)
            _editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _restoreInput = true;
#endif
            var kb = InputSystem.AddDevice<Keyboard>("B16CapKeyboard");
            _devices.Add(kb);
            ChatSettings.TypedText = true;
            for (int f = 0; f < 3; f++) yield return null;
            Key(kb, UnityEngine.InputSystem.Key.Enter);
            yield return null;
            Key(kb);
            for (int f = 0; f < 3; f++) yield return null;
            bool typing = OnlineChatHud.Typing;
            Log("typing after Enter: " + typing);
            if (typing) yield return Capture("p2-chat-3-typing");
            Key(kb, UnityEngine.InputSystem.Key.Escape);
            yield return null;
            Key(kb);
            for (int f = 0; f < 3; f++) yield return null;
            bool typingAfterEsc = OnlineChatHud.Typing, pauseAfterFirstEsc = PauseMenu.AnyOpen;
            Log("after Esc 1: typing " + typingAfterEsc + " pause " + pauseAfterFirstEsc);
            Key(kb, UnityEngine.InputSystem.Key.Escape);
            yield return null;
            Key(kb);
            for (int f = 0; f < 6; f++) yield return null;
            bool pauseAfterSecondEsc = PauseMenu.AnyOpen;
            Log("after Esc 2: pause " + pauseAfterSecondEsc);
            if (!pauseAfterSecondEsc)
            {
                PauseMenu pm = Object.FindAnyObjectByType<PauseMenu>();
                if (pm != null) pm.Pause();
                for (int f = 0; f < 3; f++) yield return null;
                Log("pause opened by call (Esc did not open it)");
            }
            yield return Capture("p2-chat-4-pause-panel");
            OnlineChatHud.ToggleSettings();
            for (int f = 0; f < 3; f++) yield return null;
            bool panel = OnlineChatHud.PanelOpen;
            if (panel) yield return Capture("p2-chat-5-chat-settings-on-pause");
            Key(kb, UnityEngine.InputSystem.Key.Escape);
            yield return null;
            Key(kb);
            for (int f = 0; f < 3; f++) yield return null;
            Log("chat settings panel " + panel + " -> after Esc " + OnlineChatHud.PanelOpen + ", pause still " + PauseMenu.AnyOpen);
            Assert.IsTrue(panel, "the chat settings panel did not open over the pause screen");
            // b16 finish: in a hidden batch-mode Editor the virtual keyboard never reached the game in 3 tries (with and
            // without AllDeviceInputAlwaysGoesToGameView): the Esc order is then the owner's play test, not a failure here.
            if (!typing) Assert.Inconclusive("the virtual keyboard did not reach the game (no focused Game view); Esc order not staged");
            Assert.IsFalse(typingAfterEsc, "the first Esc did not cancel typing");
            Assert.IsFalse(pauseAfterFirstEsc, "the first Esc reached the pause menu while typing");
            Assert.IsTrue(panel, "the chat settings panel did not open over the pause screen");
            Assert.IsFalse(OnlineChatHud.PanelOpen, "Esc did not close the chat settings panel first");
            Assert.IsTrue(PauseMenu.AnyOpen, "Esc on the chat settings panel also closed the pause menu");
        }
    }
}
