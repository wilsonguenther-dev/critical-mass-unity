using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace CriticalMass.Runtime.Menu
{
    /// <summary>
    /// DG-30 streamer and accessibility options: hide names, clean HUD (H), subtitles, UI scale 80 to 130 %, reduce
    /// flashing (at most 3 flashes per second) and camera shake 0 to 100 (default 0). Stored in PlayerPrefs like
    /// GameSettings, shown as the ACCESS tab of the settings page (PauseMenu.AddTab, registered before the menu builds).
    /// GORE intensity lives on the Game tab and is repeated here (GameSettings.Gore, the same key). View side only: nothing here reaches the sim.
    /// <para>Readers: the HUD scale and the clean HUD are applied here (AccessibilityApplier); names (scoreboard, kill
    /// feed, bot names), subtitles (announcer), flashes and shake are read by the systems that draw them, through the
    /// properties below and FlashLimiter.</para>
    /// </summary>
    public static class SettingsExtras
    {
        public const string HideNamesKey = "cm.access.hideNames";
        public const string CleanHudKey = "cm.access.cleanHud";
        public const string SubtitlesKey = "cm.access.subtitles";
        public const string UiScaleKey = "cm.access.uiScale";
        public const string ReduceFlashingKey = "cm.access.reduceFlashing";
        public const string ShakeAmountKey = GameSettings.ShakeScaleKey;
        /// <summary>Reduce motion: one key for every reader (StoryIntro, AttractMode, FeelGate, big moments, world reactions).</summary>
        public const string ReduceMotionKey = GameSettings.ReduceMotionKey;

        public static readonly string[] AllKeys = { HideNamesKey, CleanHudKey, SubtitlesKey, UiScaleKey, ReduceFlashingKey, ShakeAmountKey, ReduceMotionKey, View.Moments.BigMomentsSettings.ModeKey };

        /// <summary>UI scale choices in percent, screen order.</summary>
        public static readonly int[] UiScaleOptions = { 80, 90, 100, 110, 120, 130 };
        public const int DefaultUiScale = 100;
        /// <summary>The flash ceiling while Reduce flashing is on.</summary>
        public const int ReducedFlashesPerSecond = 3;
        public const string TabName = "ACCESS";

        /// <summary>Raised after any of these options changes.</summary>
        public static event Action Changed;

        /// <summary>Hide player and bot names wherever the game shows them (default off).</summary>
        public static bool HideNames { get => GetB(HideNamesKey, false); set => SetB(HideNamesKey, value); }
        /// <summary>Hide the match HUD for clean footage (default off; H toggles it in a match).</summary>
        public static bool CleanHud { get => GetB(CleanHudKey, false); set => SetB(CleanHudKey, value); }
        /// <summary>Show subtitles for spoken lines (default on).</summary>
        public static bool Subtitles { get => GetB(SubtitlesKey, true); set => SetB(SubtitlesKey, value); }
        /// <summary>Limit full-screen and HUD flashes to ReducedFlashesPerSecond (default off).</summary>
        public static bool ReduceFlashing { get => GameSettings.ReduceFlashing; set { GameSettings.ReduceFlashing = value; Changed?.Invoke(); } }
        /// <summary>Reduce motion (default off): fades instead of slides, a quarter of the world sway, no shake.</summary>
        public static bool ReduceMotion { get => GameSettings.ReduceMotion; set { GameSettings.ReduceMotion = value; Changed?.Invoke(); } }

        /// <summary>Flashes allowed per second: 3 with Reduce flashing on, otherwise no limit.</summary>
        public static int MaxFlashesPerSecond => ReduceFlashing ? ReducedFlashesPerSecond : int.MaxValue;

        /// <summary>HUD scale in percent, one of UiScaleOptions (default 100).</summary>
        public static int UiScalePercent
        {
            get
            {
                int v = PlayerPrefs.GetInt(UiScaleKey, DefaultUiScale);
                return Array.IndexOf(UiScaleOptions, v) >= 0 ? v : DefaultUiScale;
            }
            set
            {
                PlayerPrefs.SetInt(UiScaleKey, Array.IndexOf(UiScaleOptions, value) >= 0 ? value : DefaultUiScale);
                Save();
            }
        }

        /// <summary>HUD scale as a multiplier (0.8 .. 1.3).</summary>
        public static float UiScale => UiScalePercent / 100f;

        /// <summary>
        /// Camera shake amount 0..100 (default 0, no shake). Writing it also keeps the older on/off flag
        /// (GameSettings.CameraShake, cm.shake) in step: on when the amount is above 0.
        /// </summary>
        public static int ShakeAmount
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(ShakeAmountKey, 0), 0, 100);
            set { GameSettings.ShakeScale = Mathf.Clamp(value, 0, 100) / 100f; Changed?.Invoke(); }   // one writer: GameSettings keeps cm.shake in step
        }

        /// <summary>Camera shake as a 0..1 multiplier for the camera effects.</summary>
        public static float Shake01 => ShakeAmount / 100f;

        /// <summary>Restores every default of this tab (the Game tab's reset covers GameSettings).</summary>
        public static void ResetAll()
        {
            for (int i = 0; i < AllKeys.Length; i++) PlayerPrefs.DeleteKey(AllKeys[i]);
            GameSettings.CameraShake = false;   // also raises GameSettings.Changed, so FeelGate reloads the cleared keys
            Save();
        }

        public static void StepUiScale(int d)
        {
            int i = Array.IndexOf(UiScaleOptions, UiScalePercent);
            if (i < 0) i = Array.IndexOf(UiScaleOptions, DefaultUiScale);
            UiScalePercent = UiScaleOptions[Mathf.Clamp(i + d, 0, UiScaleOptions.Length - 1)];
        }

        static readonly string[] PercentText = { "80%", "90%", "100%", "110%", "120%", "130%" };

        public static string UiScaleText()
        {
            int i = Array.IndexOf(UiScaleOptions, UiScalePercent);
            return i >= 0 ? PercentText[i] : "100%";
        }

        static string OnOff(bool b) => b ? "ON" : "OFF";

        /// <summary>The clean-HUD key's name through InputGlyphs (the H key is read directly by AccessibilityApplier).</summary>
        public static string CleanHudKeyWords => InputGlyphs.ForPath("<Keyboard>/h").Words.ToUpperInvariant();

        /// <summary>The ACCESS tab (PauseMenu.ExtraTab.Build): six lines, a reset button and a note.</summary>
        public static List<MenuItem> BuildTab(PauseMenu menu, RectTransform tab)
        {
            var items = new List<MenuItem>
            {
                menu.Stepper(tab, 0, "HIDE NAMES", () => OnOff(HideNames), d => HideNames = !HideNames),
                menu.Stepper(tab, 1, "CLEAN HUD   (" + CleanHudKeyWords + ")", () => OnOff(CleanHud), d => CleanHud = !CleanHud),
                menu.Stepper(tab, 2, "SUBTITLES", () => OnOff(Subtitles), d => Subtitles = !Subtitles),
                menu.Stepper(tab, 3, "UI SCALE", UiScaleText, StepUiScale),
                menu.Stepper(tab, 4, "REDUCE FLASHING", () => OnOff(ReduceFlashing), d => ReduceFlashing = !ReduceFlashing),
                menu.Stepper(tab, 5, "REDUCE MOTION", () => OnOff(ReduceMotion), d => ReduceMotion = !ReduceMotion),
                menu.Slider(tab, 6, "CAMERA SHAKE", () => Shake01, v => ShakeAmount = Mathf.RoundToInt(v * 100f)),
                // Big moments (screen takeovers): full, reduced (corner callouts only), off.
                menu.Stepper(tab, 7, "BIG MOMENTS", () => View.Moments.BigMomentsSettings.Label(View.Moments.BigMomentsSettings.Mode),
                    d => View.Moments.BigMomentsSettings.Mode = View.Moments.BigMomentsSettings.Step(View.Moments.BigMomentsSettings.Mode, d)),
                // Owner, 2026-11-14: GORE intensity (the Game tab's row, same setting): FULL default, REDUCED, OFF.
                menu.Stepper(tab, 8, "GORE", () => global::CriticalMass.Runtime.View.Effects.GoreGate.Label(GameSettings.Gore), d => GameSettings.Gore = global::CriticalMass.Runtime.View.Effects.GoreGate.Step(GameSettings.Gore, d)),
            };
            float y = PauseMenu.ContentY + items.Count * (PauseMenu.LineH + PauseMenu.LineGap) + 10f;
            MenuItem reset = MenuUi.Item(tab, "Reset Access", PauseMenu.ContentX, y, 460f, 56f, "RESET THESE OPTIONS",
                MenuItem.Kind.Button, 24, true, CMTokens.Display);
            reset.Submitted = () =>
            {
                ResetAll();
                menu.RefreshAll();
                menu.SetStatus("Streamer and accessibility options are back to their defaults.", false);
            };
            items.Add(reset);
            menu.Note(tab, items.Count, "Press " + CleanHudKeyWords + " in a match to hide or show the HUD. GORE is also on the Game tab.");
            return items;
        }

        // Registered before any scene loads, so the pause menu (built on its Start) already has the tab.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            PauseMenu.AddTab(new PauseMenu.ExtraTab { Name = TabName, Build = BuildTab });
        }

        // Integration batch 2: installed by GameBootstrap through the DG-01 overlay registry ([GameOverlay]); Attach stays for tests.
        /// <summary>Adds the applier to the game object (the one the pause menu lives on); null when there is no game.</summary>
        public static AccessibilityApplier Attach(Scene scene)
        {
            PauseMenu menu = MenuInstaller.Attach(scene);
            if (menu == null) return null;
            AccessibilityApplier a = menu.GetComponent<AccessibilityApplier>();
            return a != null ? a : menu.gameObject.AddComponent<AccessibilityApplier>();
        }

        static bool GetB(string k, bool d) => PlayerPrefs.GetInt(k, d ? 1 : 0) != 0;

        static void SetB(string k, bool v)
        {
            PlayerPrefs.SetInt(k, v ? 1 : 0);
            Save();
        }

        static void Save()
        {
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Holds flashes to SettingsExtras.MaxFlashesPerSecond: call TryFlash(now) before starting a flash and skip it on
    /// false. One instance per flashing element; no allocation after construction.
    /// </summary>
    public sealed class FlashLimiter
    {
        readonly float[] _times = new float[SettingsExtras.ReducedFlashesPerSecond];
        int _next;
        int _count;

        /// <summary>True when a flash may start at <paramref name="now"/> (seconds, any monotonic clock); records it.</summary>
        public bool TryFlash(float now) => TryFlash(now, SettingsExtras.ReduceFlashing);

        public bool TryFlash(float now, bool limit)
        {
            if (limit && _count >= _times.Length)
            {
                // The oldest of the last three flashes must be at least one second old.
                float oldest = _times[_next];
                if (now - oldest < 1f) return false;
            }
            _times[_next] = now;
            _next = (_next + 1) % _times.Length;
            if (_count < _times.Length) _count++;
            return true;
        }
    }

    /// <summary>
    /// Applies the options that need no other system's help: the clean HUD (H, or the settings row) hides the match HUD
    /// canvas, and the UI scale scales the four HUD clusters about their screen-corner anchors. Runs after the HUD's own
    /// LateUpdate. It only acts on changes it made, so another screen that hides the HUD (title, menus) is not fought.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [CriticalMass.Runtime.Flow.GameOverlay(Order = 22, Scope = CriticalMass.Runtime.Flow.OverlayScope.Always)]   // installed once by GameBootstrap through the overlay registry
    public sealed class AccessibilityApplier : MonoBehaviour
    {
        GameHud _hud;
        HumanInputSource _input;
        bool _hiddenByUs;
        float _appliedScale = 1f;

        void LateUpdate()
        {
            if (_hud == null) _hud = GetComponent<GameHud>();
            if (_input == null) _input = GetComponent<HumanInputSource>();
            bool title = GetComponent<TitleScreen>() != null;
            bool paused = _input != null && _input.State.Paused;

            Keyboard kb = Keyboard.current;
            if (!title && !paused && kb != null && kb.hKey.wasPressedThisFrame) SettingsExtras.CleanHud = !SettingsExtras.CleanHud;

            if (_hud == null || _hud.Canvas == null) return;
            if (!title)
            {
                bool clean = SettingsExtras.CleanHud;
                if (clean && _hud.Canvas.enabled)
                {
                    _hud.Canvas.enabled = false;
                    _hiddenByUs = true;
                }
                else if (!clean && _hiddenByUs)
                {
                    _hud.Canvas.enabled = true;
                    _hiddenByUs = false;
                }
            }

            float s = SettingsExtras.UiScale;
            if (Mathf.Abs(s - _appliedScale) > 1e-4f)
            {
                _appliedScale = s;
                RectTransform[] clusters = _hud.Clusters;
                var v = new Vector3(s, s, 1f);
                for (int i = 0; i < clusters.Length; i++) if (clusters[i] != null) clusters[i].localScale = v;
            }
        }

        /// <summary>The HUD scale last applied (tests).</summary>
        public float AppliedScale => _appliedScale;
    }
}
