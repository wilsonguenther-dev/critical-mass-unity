using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using CameraView = CriticalMass.Runtime.Camera.CameraView;
using Object = UnityEngine.Object;

namespace CriticalMass.Runtime.PlayTests.PlayerSeat
{
    /// <summary>
    /// Player-seat gate, tier F: the Input System's own test fixture (Input System 1.20 manual, "Set up test fixtures": a blank,
    /// default-initialised Input System per test, restored afterwards; derive and override Setup / TearDown, calling base).
    /// REAL devices (Keyboard, Mouse, Gamepad added inside the fixture) drive the shipped HumanInputSource of the Boot scene;
    /// the sim clock is stopped and the test steps it a tick at a time (SanityWorld). Every test runs in the camera view it is
    /// given (TopDown = a fresh install, ThirdPerson = the owner's prefs); the camera, clean-HUD and reduce-motion prefs the
    /// fixture touches are saved first and restored in TearDown, nothing else is written.
    /// </summary>
    public abstract class PlayerSeatFixture : InputTestFixture
    {
        private protected SanityWorld S;
        protected Keyboard Kb;
        protected Mouse Ms;
        protected Gamepad Pad;
        protected World W => S.W;
        protected int Me => 0;
        protected int Enemy = -1;
        protected int LedgerCursor;
        protected readonly List<string> Notes = new List<string>();

        static readonly string[] SavedKeys = { GameSettings.GroundCameraKey, GameSettings.CameraViewKey, SettingsExtras.CleanHudKey, GameSettings.ReduceMotionKey };
        readonly Dictionary<string, int?> _saved = new Dictionary<string, int?>();

        // The project-wide actions asset (InputSystem.actions) as the FIRST test found it. InputTestFixture's Reset
        // (InputTestStateManager.Reset -> InputSystem.TestHook_DisableActions) sets s_Manager.actions = null on the very
        // InputManager object it pushed as "saved", so Restore() brings that manager back with actions still null and
        // every later play test that boots the game throws at HumanInputSource.OnEnable. Saved once, put back after
        // every test (passed, failed or errored) and once more at fixture end.
        static InputActionAsset s_FoundActions;
        static bool s_FoundEnabled;

        static void CaptureActions()
        {
            InputActionAsset a = InputSystem.actions;
            if (a == null || s_FoundActions != null) return;
            s_FoundActions = a;
            s_FoundEnabled = a.enabled;
        }

        /// <summary>Puts the asset found back on the live InputManager (the public setter refuses in play mode, so the
        /// manager's own setter is used, which re-applies it), enabled as it was.</summary>
        internal static void RestoreActions()
        {
            if (s_FoundActions == null) return;
            try
            {
                if (!ReferenceEquals(InputSystem.actions, s_FoundActions))
                {
                    object mgr = typeof(InputSystem).GetField("s_Manager", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
                    PropertyInfo prop = mgr?.GetType().GetProperty("actions", BindingFlags.Public | BindingFlags.Instance);
                    if (prop == null) Debug.LogError("[px-f] cannot restore InputSystem.actions: InputManager.actions not found");
                    else prop.SetValue(mgr, s_FoundActions);
                }
                if (s_FoundEnabled && !s_FoundActions.enabled) s_FoundActions.Enable();
                if (!ReferenceEquals(InputSystem.actions, s_FoundActions)) Debug.LogError("[px-f] InputSystem.actions still not restored after the test");
            }
            catch (Exception ex) { Debug.LogError("[px-f] restoring InputSystem.actions failed: " + ex); }
        }

        [OneTimeTearDown]
        public void RestoreActionsAtFixtureEnd() => RestoreActions();

        // fix round 7: the Editor is not batch mode, so FogConfig.Pin is null there and the player's fog setting (on) applies:
        // an enemy the rig puts 0.8 reach in front of me stays fogged for ~0.25 s (10 Hz map + 0.3 s fade, FogConfig), and
        // FogOfWar.Hides drops its model, bar, aim candidate and hittable cue (Editor 79/42 vs GitHub 107/13, where batch
        // mode pins the fog off). The seat tests pin it off in both places; the fog has its own tests.
        bool? _fogWas;
        int _targetFpsWas, _vsyncWas;
        /// <summary>Fix round 7: every rendered frame of a seat test is a real 60 fps frame (16.7 ms of wall and game time), so
        /// the view's unscaled-time cues (commit rise, kick, fades) advance per frame as they do for a player at 60 fps; the
        /// Editor otherwise renders test frames ~1-5 ms apart and the GitHub runner at whatever pace it has.</summary>
        public const int SeatFps = 60;

        /// <summary>Fix round 7: pace rendered frames at SeatFps (on) or give the pace back (off). Only the measured windows
        /// are paced (a whole tier at 60 fps would add minutes on the runner).</summary>
        protected void Paced(bool on)
        {
            if (on) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = SeatFps; }
            else { Application.targetFrameRate = _targetFpsWas; QualitySettings.vSyncCount = _vsyncWas; }
        }

        /// <summary>Fix round 7 (the shard-3 hang: GitHub runs 45081444538, 45081447225, 45103924574 all stopped right after
        /// D2(Healer), in D3, the first test that reached HitAndWatch's end-of-frame sample): WaitForEndOfFrame never resumes
        /// in a -batchmode -nographics player (nothing is rendered, so there is no end of frame). With no graphics device this
        /// waits one frame instead (the views' LateUpdate of the frame has run by then); with graphics, the real end of frame.</summary>
        /// <summary>fix/px-view-2: moves the virtual mouse like a hand does, the position WITH its delta. Since AimDeviceArbiter
        /// (82f00b6b) only a real move (> 2 px delta) or a button press hands the aim to the cursor; a bare position event
        /// left the aim where it was (F08: HittableTarget -1 with the enemy under the cursor).</summary>
        protected void MoveMouse(Vector2 to)
        {
            Vector2 from = Ms.position.ReadValue();
            Set(Ms.delta, to - from);
            Set(Ms.position, to);
        }

        protected static object EndOfFrame() =>
            SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null ? null : new WaitForEndOfFrame();

        /// <summary>Fix round 7: tick until `until` is true, at most maxTicks, else fail with what was waited for (no rig
        /// wait without a hard limit).</summary>
        protected IEnumerator TickUntil(Func<bool> until, int maxTicks, string what, Action beforeTick = null)
        {
            for (int t = 0; t < maxTicks; t++)
            {
                if (until()) yield break;
                yield return TickFrame(beforeTick);
            }
            if (!until()) Assert.Fail("[px-f] rig wait timed out after " + maxTicks + " ticks: " + what);
        }

        public override void Setup()
        {
            CaptureActions();
            _fogWas = CriticalMass.Runtime.View.Fog.FogConfig.CaptureOverride;
            CriticalMass.Runtime.View.Fog.FogConfig.CaptureOverride = false;
            _targetFpsWas = Application.targetFrameRate;
            _vsyncWas = QualitySettings.vSyncCount;
            try { base.Setup(); }
            catch { RestoreActions(); throw; }
            foreach (string k in SavedKeys) _saved[k] = CmPrefs.HasKey(k) ? CmPrefs.GetInt(k) : (int?)null;
            InputSettings s = InputSystem.settings;
            s.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            s.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Kb = InputSystem.AddDevice<Keyboard>();
            Ms = InputSystem.AddDevice<Mouse>();
            Pad = InputSystem.AddDevice<Gamepad>();
            Notes.Clear();
        }

        public override void TearDown()
        {
            foreach (var kv in _saved)
            {
                if (kv.Value.HasValue) CmPrefs.SetInt(kv.Key, kv.Value.Value);
                else CmPrefs.DeleteKey(kv.Key);
            }
            CmPrefs.Save();
            CriticalMass.Runtime.View.Fog.FogConfig.CaptureOverride = _fogWas;
            Paced(false);
            try
            {
                SanityWorld.Reset();
                base.TearDown();
            }
            finally { RestoreActions(); }
        }

        /// <summary>Loads Boot with actor 0 as cls in the given view, the real input reader ON, the sim clock stopped.</summary>
        protected IEnumerator Load(ClassId cls, CameraView view, bool reduceMotion = false)
        {
            GameSettings.CameraView = view;
            SettingsExtras.CleanHud = false;
            GameSettings.ReduceMotion = reduceMotion;
            S = new SanityWorld();
            yield return S.Load(cls, manualInput: false, settleSeconds: 1);
            PlayerFeedback.Clear();
            Ms.MakeCurrent();
            for (int f = 0; f < 10; f++) yield return null;
            Assert.AreEqual(view, GameSettings.CameraView, "the fixture set the view");
            LedgerCursor = PlayerFeedback.Head;
        }

        /// <summary>
        /// Takes every body but actor 0 out of the match and puts enemy slot k of class cls dist u from actor 0 in direction
        /// (dx, dz) (unit, sim space). The enemy stays a bot unless the test pins it (Pin) each tick.
        /// </summary>
        protected int Isolate(ClassId cls, double dist, double dx, double dz)
        {
            World w = W;
            int e = w.ActorCount / 2;
            for (int j = 1; j < w.ActorCount; j++)
            {
                if (j == e) continue;
                w.Actors[j].Alive = false;
                w.Actors[j].RespawnIn = 1e9;
                w.MeatDropped[j] = 1;
                w.Depth.WasAlive[j] = 0;
            }
            Put(e, cls, w.X[0] + dx * dist, w.Z[0] + dz * dist);
            w.Actors[0].SpawnProtect = 0;
            Enemy = e;
            return e;
        }

        protected void Put(int j, ClassId cls, double x, double z)
        {
            World w = W;
            Actor a = w.Actors[j];
            a.Cls = cls;
            a.MaxHp = w.Config.Class(cls).MaxHp;
            a.Hp = a.MaxHp;
            a.Alive = true;
            a.SpawnProtect = 0;
            a.StandUp = 0;
            a.AirLen = 0;
            w.X[j] = x; w.Z[j] = z; w.PrevX[j] = x; w.PrevZ[j] = z; w.Vx[j] = 0; w.Vz[j] = 0;
            w.Depth.WasAlive[j] = 1;
        }

        /// <summary>Holds a bot where the test wants it and never lets it swing (a passive dummy in the live match).</summary>
        protected void Pin(int j, double x, double z)
        {
            World w = W;
            w.X[j] = x; w.Z[j] = z; w.PrevX[j] = x; w.PrevZ[j] = z; w.Vx[j] = 0; w.Vz[j] = 0;
            w.Actors[j].AttackCd = Math.Max(w.Actors[j].AttackCd, 1);
            w.Actors[j].AbilityCd = Math.Max(w.Actors[j].AbilityCd, 1);
            if (w.Demolitions != null && w.Demolitions.TntReadyTick != null) w.Demolitions.TntReadyTick[j] = int.MaxValue / 2;
        }

        /// <summary>The camera's flat forward as a sim-space unit (third person: where the swing goes with no mouse).</summary>
        protected void CameraForward(out double dx, out double dz)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            Vector3 f = cam != null ? cam.transform.forward : Vector3.forward;
            f.y = 0;
            if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
            f.Normalize();
            SimSpace.ToSim(f, out dx, out dz);
            SimSpace.ToSim(Vector3.zero, out double ox, out double oz);
            dx -= ox; dz -= oz;
            double l = Math.Sqrt(dx * dx + dz * dz);
            if (l < 1e-9) { dx = 1; dz = 0; return; }
            dx /= l; dz /= l;
        }

        /// <summary>The direction a test places the enemy in: the camera forward in third person, screen-right in top-down.</summary>
        protected void FrontDir(CameraView view, out double dx, out double dz)
        {
            if (view == CameraView.TopDown) SanityWorld.FromScreenIntent(1, 0, out dx, out dz);
            else CameraForward(out dx, out dz);
        }

        protected Vector2 ScreenOf(double x, double z)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            Vector3 p = cam.WorldToScreenPoint(SimSpace.ToWorld(x, S.Ground(x, z), z));
            return new Vector2(p.x, p.y);
        }

        /// <summary>One frame (the input reader samples the devices) then one sim tick, views synced.</summary>
        protected IEnumerator TickFrame(Action beforeTick = null)
        {
            yield return null;
            beforeTick?.Invoke();
            S.Tick(1);
        }

        /// <summary>The attack press as a player makes it in this view: the mouse on the enemy (top-down) or the button with the camera on it (third person).</summary>
        protected void PressAttack(CameraView view, bool gamepad = false)
        {
            if (gamepad) { Press(Pad.rightTrigger); return; }
            // fix/px-base-seat-reds: a mouse user MOVES the mouse onto the enemy before he clicks (position with its delta); a
            // bare position event plus a click aimed only through the old click-takes-the-aim rule, which no longer holds for a
            // mouse never moved this match
            if (view == CameraView.TopDown && Enemy >= 0) MoveMouse(ScreenOf(W.X[Enemy], W.Z[Enemy]));
            Press(Ms.leftButton);
        }

        protected void ReleaseAttack(bool gamepad = false)
        {
            if (gamepad) Release(Pad.rightTrigger);
            else Release(Ms.leftButton);
        }

        /// <summary>Press, one frame + tick, release: a tap of the attack button.</summary>
        protected IEnumerator TapAttack(CameraView view, bool gamepad = false, Action beforeTick = null)
        {
            PressAttack(view, gamepad);
            yield return TickFrame(beforeTick);
            ReleaseAttack(gamepad);
        }

        /// <summary>Sim events since head (copied).</summary>
        protected List<SimEvent> Events(int head, SimEventType type)
        {
            var r = new List<SimEvent>();
            for (int i = Math.Max(head, W.EventHead - World.EventCapacity); i < W.EventHead; i++)
            {
                SimEvent e = W.Events[i % World.EventCapacity];
                if (e.Type == type) r.Add(new SimEvent { Type = e.Type, A = e.A, B = e.B, X = e.X, Z = e.Z, Value = e.Value, Impulse = e.Impulse, Cause = e.Cause });
            }
            return r;
        }

        /// <summary>The idle autopilot must never be the one swinging in these tests (the hit-feel audit's contaminated first runs).</summary>
        protected void AssertAtControls(string when)
        {
            Assert.IsFalse(MechRules.Autopiloted(W, 0), "the idle autopilot was driving the player at " + when + ": the measurement would be a bot's");
            Assert.IsFalse(S.Driver.HumanAutopilot, "the driver's human autopilot is on at " + when);
        }

        // ------------------------------------------------------------------ the ledger

        /// <summary>A ledger entry of channel (for actor when actor >= 0) since the cursor whose Source, if any, is active and enabled.</summary>
        protected bool Ledger(FeedbackChannel channel, int actor, out FeedbackEntry entry, int cursor = -1)
        {
            int from = cursor >= 0 ? cursor : LedgerCursor;
            for (int i = Math.Max(from, PlayerFeedback.Head - PlayerFeedback.Capacity); i < PlayerFeedback.Head; i++)
            {
                FeedbackEntry e = PlayerFeedback.At(i);
                if (e.Channel != channel || (actor >= 0 && e.Actor != actor)) continue;
                if (!SourceShown(e.Source)) continue;
                entry = e;
                return true;
            }
            entry = default;
            return false;
        }

        /// <summary>The latest shown entry of channel (actor or any) since cursor.</summary>
        protected bool LedgerLast(FeedbackChannel channel, int actor, int cursor, out FeedbackEntry entry)
        {
            entry = default;
            bool any = false;
            for (int i = Math.Max(cursor, PlayerFeedback.Head - PlayerFeedback.Capacity); i < PlayerFeedback.Head; i++)
            {
                FeedbackEntry e = PlayerFeedback.At(i);
                if (e.Channel != channel || (actor >= 0 && e.Actor != actor) || !SourceShown(e.Source)) continue;
                entry = e;
                any = true;
            }
            return any;
        }

        /// <summary>The largest value of a shown channel entry since cursor (0 when none).</summary>
        protected static double LedgerMax(FeedbackChannel channel, int cursor)
        {
            double m = 0;
            for (int i = Math.Max(cursor, PlayerFeedback.Head - PlayerFeedback.Capacity); i < PlayerFeedback.Head; i++)
            {
                FeedbackEntry e = PlayerFeedback.At(i);
                if (e.Channel == channel && SourceShown(e.Source)) m = Math.Max(m, e.Value);
            }
            return m;
        }

        protected static bool LedgerReason(FeedbackChannel channel, int reason, int cursor)
        {
            for (int i = Math.Max(cursor, PlayerFeedback.Head - PlayerFeedback.Capacity); i < PlayerFeedback.Head; i++)
            {
                FeedbackEntry e = PlayerFeedback.At(i);
                if (e.Channel == channel && e.Reason == reason && SourceShown(e.Source)) return true;
            }
            return false;
        }

        /// <summary>Every ledger entry since cursor as channel:actor:value(:reason)(:hidden), for the failure line (at most 30).</summary>
        protected static string LedgerDump(int cursor)
        {
            var parts = new List<string>();
            for (int i = Math.Max(cursor, PlayerFeedback.Head - PlayerFeedback.Capacity); i < PlayerFeedback.Head && parts.Count < 30; i++)
            {
                FeedbackEntry e = PlayerFeedback.At(i);
                parts.Add(e.Channel + ":" + e.Actor + ":" + e.Value.ToString("0.##") + (e.Reason != 0 ? ":r" + e.Reason : "") + (SourceShown(e.Source) ? "" : ":hidden"));
            }
            return parts.Count == 0 ? "ledger empty since the hit" : string.Join(" ", parts);
        }

        protected int LedgerCount(FeedbackChannel channel, int actor, int cursor)
        {
            int n = 0;
            for (int i = Math.Max(cursor, PlayerFeedback.Head - PlayerFeedback.Capacity); i < PlayerFeedback.Head; i++)
            {
                FeedbackEntry e = PlayerFeedback.At(i);
                if (e.Channel == channel && (actor < 0 || e.Actor == actor) && SourceShown(e.Source)) n++;
            }
            return n;
        }

        /// <summary>A reported cue's source must be really showing: an active GameObject and an enabled Behaviour / Renderer (null = a sound with no object).</summary>
        protected static bool SourceShown(Object src)
        {
            if (src == null) return true;
            GameObject go = src is GameObject g ? g : src is Component c ? c.gameObject : null;
            if (go == null || !go.activeInHierarchy) return false;
            if (src is Behaviour b && !b.isActiveAndEnabled) return false;
            if (src is Renderer r && !r.enabled) return false;
            return true;
        }

        /// <summary>Steps frames (one sim tick per frame for the first ticks frames, then frames without ticks) until done() or the limit.</summary>
        protected IEnumerator Within(int ticks, int frames, Func<bool> done, Action<bool> result, Action beforeTick = null)
        {
            for (int t = 0; t < ticks; t++)
            {
                if (done()) { result(true); yield break; }
                yield return TickFrame(beforeTick);
            }
            for (int f = 0; f < frames; f++)
            {
                if (done()) { result(true); yield break; }
                yield return null;
            }
            result(done());
        }

        // ------------------------------------------------------------------ cues that exist today (read without the ledger)

        /// <summary>GameHud's damage-number slots (private Text[] _dmg): an active one whose text holds the rounded value.</summary>
        protected bool DamageNumberShown(double value)
        {
            GameHud hud = S.Hud;
            if (hud == null) return false;
            FieldInfo f = typeof(GameHud).GetField("_dmg", BindingFlags.NonPublic | BindingFlags.Instance);
            if (!(f?.GetValue(hud) is Text[] slots)) return false;
            string want = Math.Round(value).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return slots.Any(t => t != null && t.gameObject.activeInHierarchy && t.enabled && t.text != null && t.text.Contains(want));
        }

        protected static int PlayingSources() => Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Count(a => a.isPlaying);

        protected static int PlayingParticles() => Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Count(p => p.isPlaying);

        /// <summary>Every active, enabled uGUI Text on screen, upper case.</summary>
        protected static List<string> ScreenTexts() =>
            Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Where(t => t.isActiveAndEnabled && !string.IsNullOrEmpty(t.text)).Select(t => t.text.ToUpperInvariant()).ToList();

        protected static bool HudCanvasShown() =>
            Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Any(c => c.isActiveAndEnabled && c.GetComponentInParent<GameHud>() != null)
            || Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Any(c => c.isActiveAndEnabled && c.gameObject.name.IndexOf("Hud", StringComparison.OrdinalIgnoreCase) >= 0);

        protected static double BearingDeg(double fromX, double fromZ, double toX, double toZ) => Math.Atan2(toZ - fromZ, toX - fromX) * 180 / Math.PI;

        protected static double AngleGap(double a, double b)
        {
            double d = (a - b) % 360;
            if (d > 180) d -= 360;
            if (d < -180) d += 360;
            return Math.Abs(d);
        }

        /// <summary>The line, then the assertion carrying it: a red test says what was measured against what.</summary>
        protected static void Check(string id, string what, string measured, string target, bool ok)
        {
            Line(id, what, measured, target, ok);
            Assert.IsTrue(ok, id + " | " + what + " | measured: " + measured + " | target: " + target);
        }

        /// <summary>One "[px-f]" line per measured case in the player log (the gate parses it), and true when ok.</summary>
        protected static bool Line(string id, string what, string measured, string target, bool ok)
        {
            Debug.Log("[px-f] " + id + " | " + what + " | " + measured + " | " + target + " | " + (ok ? "PASS" : "FAIL"));
            return ok;
        }
    }
}
