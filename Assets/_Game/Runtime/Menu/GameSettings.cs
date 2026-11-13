using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace CriticalMass.Runtime.Menu
{
    /// <summary>
    /// The player's settings, stored in PlayerPrefs (kept between launches), read by the settings page and by any system
    /// that honours them. View side only: nothing here reaches the sim.
    /// <para>Keys other code may read directly: cm.blood (int, 1 = blood on, default 1), cm.shake (camera shake on/off, mirrors cm.access.shake, default off),
    /// cm.damagenumbers (default 1). Volumes are linear 0..1; the AudioMixer (Resources/Audio/CMMixer) gets them as dB on
    /// the exposed parameters MasterVolume, MusicVolume and EffectsVolume. Without the mixer, master volume falls back to
    /// AudioListener.volume.</para>
    /// </summary>
    public static class GameSettings
    {
        public const string MasterKey = "cm.audio.master";
        public const string MusicKey = "cm.audio.music";
        public const string EffectsKey = "cm.audio.effects";
        public const string WidthKey = "cm.video.width";
        public const string HeightKey = "cm.video.height";
        public const string FullScreenKey = "cm.video.fullscreen";
        public const string VSyncKey = "cm.video.vsync";
        public const string QualityKey = "cm.video.quality";
        public const string ShakeKey = "cm.shake";
        // Physics audit M11 / DG-94 / DG-106. The keys are PR #28's (SettingsExtras, DG-30) so both branches read and write
        // the same stored values: shake is an int amount 0..100, default 0 (off, owner decision OD-6), reduce flashing an int.
        /// <summary>Camera shake amount 0..100 (int, default 0 = off); same key as PR #28's SettingsExtras.ShakeAmountKey.</summary>
        public const string ShakeScaleKey = "cm.access.shake";
        /// <summary>Reduce flashing (1 on, default 0): hit flash as a light tint, a steady damage edge, pulses at 2 Hz or less.</summary>
        public const string ReduceFlashingKey = "cm.access.reduceFlashing";
        /// <summary>Reduce motion (1 on, default 0): no camera shake, no combat zoom, no Kaiju spotlight.</summary>
        public const string ReduceMotionKey = "cm.reducedMotion";   // one key: StoryIntro, AttractMode, big moments and world reactions read it too
        public const string DamageNumbersKey = "cm.damagenumbers";
        public const string BloodKey = "cm.blood";
        /// <summary>Frame-rate limit: 30, 60 or 120 frames per second, or 0 = the display's refresh rate (default 60).</summary>
        public const string FpsCapKey = "cm.fpsCap";
        /// <summary>"Save power in background": 15 fps while the game is not the front window: 1 on, 0 off (default, DG-51).</summary>
        public const string BgThrottleKey = "cm.bgThrottle";
        /// <summary>Easy movement: lane assist and corner sliding, 1 on (default), 0 off.</summary>
        public const string MoveAssistKey = "cm.moveAssist";
        /// <summary>Easy movement: direction keys follow the map's grid instead of the screen, 1 on, 0 off (default).</summary>
        public const string KeysFollowMapKey = "cm.keysFollowMap";
        /// <summary>DG-53: the settings format version, so a later build can migrate or reset stored keys safely.</summary>
        public const string PrefsVersionKey = "cm.prefsVersion";
        public const int PrefsVersion = 1;
        /// <summary>World reactions' name for the one reduce-motion key (same key as ReduceMotionKey).</summary>
        public const string ReducedMotionKey = ReduceMotionKey;

        /// <summary>Every key this class writes (tests back these up and restore them).</summary>
        public static readonly string[] AllKeys =
        {
            MasterKey, MusicKey, EffectsKey, WidthKey, HeightKey, FullScreenKey, VSyncKey, QualityKey, ShakeKey,
            DamageNumbersKey, BloodKey, FpsCapKey, BgThrottleKey, MoveAssistKey, KeysFollowMapKey, PrefsVersionKey,
            ShakeScaleKey, ReduceFlashingKey, ReduceMotionKey,
        };

        public const string MixerResource = "Audio/CMMixer";
        public const string MasterParam = "MasterVolume";
        public const string MusicParam = "MusicVolume";
        public const string EffectsParam = "EffectsVolume";

        /// <summary>Raised after any setting changes (the HUD or effects may re-read their flags).</summary>
        public static event Action Changed;

        // ---- audio ----
        public static float MasterVolume { get => GetF(MasterKey, 1f); set { SetF(MasterKey, Mathf.Clamp01(value)); ApplyAudio(); } }
        public static float MusicVolume { get => GetF(MusicKey, 0.8f); set { SetF(MusicKey, Mathf.Clamp01(value)); ApplyAudio(); } }
        public static float EffectsVolume { get => GetF(EffectsKey, 1f); set { SetF(EffectsKey, Mathf.Clamp01(value)); ApplyAudio(); } }

        // ---- game ----
        /// <summary>Camera shake allowed: ShakeScale above 0 (cm.shake is kept in step for older readers).</summary>
        public static bool CameraShake { get => ShakeScale > 0f; set => ShakeScale = value ? 1f : 0f; }

        /// <summary>The shake steps the settings page cycles through, in screen order (off is the default).</summary>
        public static readonly float[] ShakeSteps = { 0f, 0.5f, 1f };

        /// <summary>
        /// Camera shake strength 0..1, stored as the int amount 0..100 (default 0: off). CameraShake reads it; ReduceMotion
        /// overrides it to 0 in FeelGate. Writing it keeps the older on/off flag cm.shake in step.
        /// </summary>
        public static float ShakeScale
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(ShakeScaleKey, 0), 0, 100) / 100f;
            set
            {
                int v = Mathf.Clamp(Mathf.RoundToInt(value * 100f), 0, 100);
                PlayerPrefs.SetInt(ShakeKey, v > 0 ? 1 : 0);
                PlayerPrefs.SetInt(ShakeScaleKey, v);
                Save();
            }
        }

        public static bool ReduceFlashing { get => GetB(ReduceFlashingKey, false); set => SetB(ReduceFlashingKey, value); }
        public static bool ReduceMotion { get => GetB(ReduceMotionKey, false); set => SetB(ReduceMotionKey, value); }
        /// <summary>Same setting as ReduceMotion (world reactions' name): sway, springs, launches and the edge flash at 25%.</summary>
        public static bool ReducedMotion { get => ReduceMotion; set => ReduceMotion = value; }
        /// <summary>Floating damage numbers shown.</summary>
        public static bool DamageNumbers { get => GetB(DamageNumbersKey, true); set => SetB(DamageNumbersKey, value); }
        /// <summary>Blood effects on (PlayerPrefs cm.blood, default on).</summary>
        public static bool Blood { get => GetB(BloodKey, true); set => SetB(BloodKey, value); }
        /// <summary>
        /// "Movement assist" (cm.moveAssist, default on): one direction key follows a bridge it points along (within 50
        /// degrees) and a blocked move slides along corners and rails (Input.MoveAssist).
        /// </summary>
        public static bool MovementAssist { get => GetB(MoveAssistKey, true); set => SetB(MoveAssistKey, value); }
        /// <summary>
        /// "Keys follow the map" (cm.keysFollowMap, default off): W, A, S and D move along the map's grid axes instead
        /// of screen up, left, down and right. The controller stick stays screen-relative.
        /// </summary>
        public static bool KeysFollowMap { get => GetB(KeysFollowMapKey, false); set => SetB(KeysFollowMapKey, value); }

        // ---- video ----
        /// <summary>The frame-rate limit choices in screen order; 0 = the display's refresh rate.</summary>
        public static readonly int[] FpsCapOptions = { 30, 60, 120, 0 };
        public const int DefaultFpsCap = 60;

        /// <summary>
        /// Frame-rate limit (cm.fpsCap): 30, 60, 120, or 0 for the display's refresh rate; default 60. Stored here; FrameGovernor
        /// applies it (Reapply on save).
        /// </summary>
        public static int FpsCap
        {
            get
            {
                int v = PlayerPrefs.GetInt(FpsCapKey, DefaultFpsCap);
                return Array.IndexOf(FpsCapOptions, v) >= 0 ? v : DefaultFpsCap;
            }
            set
            {
                PlayerPrefs.SetInt(FpsCapKey, Array.IndexOf(FpsCapOptions, value) >= 0 ? value : DefaultFpsCap);
                Save();
                CriticalMass.Runtime.Game.FrameGovernor.Reapply();   // FrameGovernor applies it on the next frame
            }
        }

        /// <summary>Save power in background: 15 fps when the game is not the front window (cm.bgThrottle, default off, DG-51).</summary>
        public static bool BackgroundThrottle { get => GetB(BgThrottleKey, false); set { SetB(BgThrottleKey, value); CriticalMass.Runtime.Game.FrameGovernor.Reapply(); } }

        public static bool FullScreen { get => GetB(FullScreenKey, true); set { SetB(FullScreenKey, value); ApplyVideo(); } }
        public static bool VSync { get => GetB(VSyncKey, true); set { SetB(VSyncKey, value); ApplyVideo(); } }

        /// <summary>Quality level index (QualitySettings.names); -1 = the project's default level.</summary>
        public static int Quality
        {
            get => PlayerPrefs.GetInt(QualityKey, -1);
            set
            {
                PlayerPrefs.SetInt(QualityKey, value);
                Save();
                ApplyVideo();
            }
        }

        /// <summary>The stored resolution, or (0, 0) for "the current one".</summary>
        public static Vector2Int Resolution
        {
            get => new Vector2Int(PlayerPrefs.GetInt(WidthKey, 0), PlayerPrefs.GetInt(HeightKey, 0));
            set
            {
                PlayerPrefs.SetInt(WidthKey, value.x);
                PlayerPrefs.SetInt(HeightKey, value.y);
                Save();
                ApplyVideo();
            }
        }

        /// <summary>Distinct screen sizes the display offers, smallest first, always including the current size.</summary>
        public static List<Vector2Int> ResolutionOptions()
        {
            var list = new List<Vector2Int>();
            foreach (UnityEngine.Resolution r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (!list.Contains(v)) list.Add(v);
            }
            var cur = new Vector2Int(Screen.width, Screen.height);
            if (!list.Contains(cur)) list.Add(cur);
            list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            return list;
        }

        /// <summary>Linear 0..1 to mixer decibels (0 = -80 dB, 1 = 0 dB).</summary>
        public static float ToDecibels(float linear) => linear <= 0.0001f ? -80f : Mathf.Max(-80f, 20f * Mathf.Log10(linear));

        static AudioMixer _mixer;
        static bool _mixerLooked;

        /// <summary>The game's mixer (Resources/Audio/CMMixer), or null when it is missing.</summary>
        public static AudioMixer Mixer
        {
            get
            {
                if (!_mixerLooked)
                {
                    _mixerLooked = true;
                    _mixer = Resources.Load<AudioMixer>(MixerResource);
                }
                return _mixer;
            }
        }

        /// <summary>The mixer group new music sources should use (null without the mixer).</summary>
        public static AudioMixerGroup MusicGroup => Group("Music");
        /// <summary>The mixer group new sound effect sources should use (null without the mixer).</summary>
        public static AudioMixerGroup EffectsGroup => Group("Effects");

        static AudioMixerGroup Group(string name)
        {
            AudioMixer m = Mixer;
            if (m == null) return null;
            AudioMixerGroup[] g = m.FindMatchingGroups(name);
            return g != null && g.Length > 0 ? g[0] : null;
        }

        /// <summary>Pushes the volumes to the mixer (or the listener when there is no mixer).</summary>
        public static void ApplyAudio()
        {
            if (!Application.isPlaying) return;   // edit mode (tests): never touch the mixer asset or the editor's listener
            AudioMixer m = Mixer;
            if (m != null)
            {
                m.SetFloat(MasterParam, ToDecibels(MasterVolume));
                m.SetFloat(MusicParam, ToDecibels(MusicVolume));
                m.SetFloat(EffectsParam, ToDecibels(EffectsVolume));
                AudioListener.volume = 1f;
            }
            else AudioListener.volume = MasterVolume;
        }

        /// <summary>True where the window may be changed (player builds; never the editor, batch mode or tests).</summary>
        public static bool CanChangeWindow => !Application.isEditor && !Application.isBatchMode;

        /// <summary>Applies quality, vertical sync, resolution and window mode (player builds only).</summary>
        public static void ApplyVideo()
        {
            // Never in the editor: QualitySettings changes there edit ProjectSettings/QualitySettings.asset.
            if (!CanChangeWindow) return;
            int q = Quality;
            if (q >= 0 && q < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != q) QualitySettings.SetQualityLevel(q, true);
            // Vertical sync and the frame limit are FrameGovernor's alone (it re-applies them after a level change).
            CriticalMass.Runtime.Game.FrameGovernor.Reapply();
            Vector2Int r = ClampedResolution(Resolution, Display.main.systemWidth, Display.main.systemHeight);
            int w = r.x > 0 ? r.x : Screen.width, h = r.y > 0 ? r.y : Screen.height;
            // DG-52: borderless fullscreen-window or a window, never exclusive fullscreen or a display-mode change; the
            // governor spaces changes at least 1 s apart.
            FullScreenMode mode = FullScreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (w != Screen.width || h != Screen.height || Screen.fullScreenMode != mode) CriticalMass.Runtime.Game.FrameGovernor.RequestWindow(w, h, mode);
        }

        /// <summary>DG-52: a saved size larger than the display shrinks to fit it (aspect kept); (0, 0) stays "current".</summary>
        public static Vector2Int ClampedResolution(Vector2Int saved, int displayW, int displayH)
        {
            if (saved.x <= 0 || saved.y <= 0) return saved;
            (int w, int h) = CriticalMass.Runtime.Game.FramePolicy.ClampToNative(saved.x, saved.y, displayW, displayH);
            return new Vector2Int(w, h);
        }

        /// <summary>Applies every stored setting (boot).</summary>
        public static void ApplyAll()
        {
            ApplyAudio();
            ApplyVideo();
        }

        /// <summary>Restores every default (deletes the stored keys) and applies them.</summary>
        public static void ResetAll()
        {
            for (int i = 0; i < AllKeys.Length; i++) PlayerPrefs.DeleteKey(AllKeys[i]);
            Save();
            Flush();
            ApplyAll();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void ApplyOnBoot()
        {
            _mixerLooked = false;
            _mixer = null;
            Application.quitting -= OnQuit;
            Application.quitting += OnQuit;
            CheckPrefsVersion();
            if (Application.isBatchMode) return;   // gates and tests keep the project's settings
            if (CriticalMass.Runtime.Game.GamePaths.IsolatedRun) return;   // a -dataDir measurement run keeps its own window and quality
            ApplyAll();
        }

        static void OnQuit() => Flush();

        /// <summary>DG-53: stamps cm.prefsVersion; a newer format (a later build ran) is kept and logged, never misread.</summary>
        static void CheckPrefsVersion()
        {
            int v = PlayerPrefs.GetInt(PrefsVersionKey, 0);
            if (v == PrefsVersion) return;
            if (v > PrefsVersion)
            {
                Debug.LogWarning($"[CM.Settings] settings were saved by a newer build (format {v}); reading what this build knows");
                return;
            }
            PlayerPrefs.SetInt(PrefsVersionKey, PrefsVersion);
            _dirty = true;
        }

        static float GetF(string k, float d) => PlayerPrefs.GetFloat(k, d);
        static bool GetB(string k, bool d) => PlayerPrefs.GetInt(k, d ? 1 : 0) != 0;

        static void SetF(string k, float v)
        {
            PlayerPrefs.SetFloat(k, v);
            Save();
        }

        static void SetB(string k, bool v)
        {
            PlayerPrefs.SetInt(k, v ? 1 : 0);
            Save();
        }

        static bool _dirty;

        /// <summary>Disk writes of the settings since boot (DG-53 test: 100 volume changes, 0 saves until the page closes).</summary>
        public static int SaveCount { get; private set; }

        /// <summary>true while a change waits for Flush.</summary>
        public static bool Dirty => _dirty;

        /// <summary>The last save failure, or null.</summary>
        public static string LastSaveError { get; private set; }

        /// <summary>DG-53: a change is kept in memory and marked; it reaches the disk when the page closes (Flush), not per tick.</summary>
        static void Save()
        {
            _dirty = true;
            Changed?.Invoke();
        }

        /// <summary>
        /// Writes pending changes to disk: the settings page and pause menu call it when they close, and the game at quit.
        /// Returns false (and shows "Settings could not be saved") when the write failed.
        /// </summary>
        public static bool Flush()
        {
            if (!_dirty) return true;
            if (CriticalMass.Runtime.Game.GamePaths.IsolatedRun)
            {
                _dirty = false;   // a -dataDir measurement run never writes the player's saved settings
                return true;
            }
            try
            {
                PlayerPrefs.SetInt(PrefsVersionKey, PrefsVersion);
                PlayerPrefs.Save();
                _dirty = false;
                SaveCount++;
                LastSaveError = null;
                return true;
            }
            catch (Exception e)
            {
                LastSaveError = e.Message;
                Debug.LogError($"[CM.Settings] settings could not be saved: {e.Message}");
                CriticalMass.Runtime.Game.FrameGovernor.ShowNotice("Settings could not be saved.");
                return false;
            }
        }
    }
}
