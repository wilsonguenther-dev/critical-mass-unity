using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace CriticalMass.Runtime.Menu
{
    /// <summary>
    /// The player's settings, stored in PlayerPrefs (kept between launches), read by the settings page and by any system
    /// that honours them. View side only: nothing here reaches the sim.
    /// <para>Keys other code may read directly: cm.blood (int, 1 = blood on, default 1), cm.shake (camera shake, default 0 = OFF:
    /// the owner's accessibility decision OD-6; Effects.ShakeEnabled reads CameraShake, the one source of truth),
    /// cm.reducedMotion (default 0; world sway, springs and launches drop to 25%), cm.damagenumbers (default 1). Volumes are linear 0..1; the AudioMixer (Resources/Audio/CMMixer) gets them as dB on
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
        public const string DamageNumbersKey = "cm.damagenumbers";
        public const string BloodKey = "cm.blood";
        /// <summary>Frame-rate limit: 30, 60 or 120 frames per second, or 0 = the display's refresh rate (default 60).</summary>
        public const string FpsCapKey = "cm.fpsCap";
        /// <summary>Lower the frame rate while the game is not the front window: 1 on (default), 0 off.</summary>
        public const string BgThrottleKey = "cm.bgThrottle";
        /// <summary>Reduced motion (shared with the story intro and attract mode): 1 on, default 0.</summary>
        public const string ReducedMotionKey = "cm.reducedMotion";

        /// <summary>Every key this class writes (tests back these up and restore them).</summary>
        public static readonly string[] AllKeys =
        {
            MasterKey, MusicKey, EffectsKey, WidthKey, HeightKey, FullScreenKey, VSyncKey, QualityKey, ShakeKey,
            DamageNumbersKey, BloodKey, FpsCapKey, BgThrottleKey, ReducedMotionKey,
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
        /// <summary>Camera shake allowed. Default OFF (OD-6); with it off, impacts land through the shake replacement
        /// (grass shock ring, edge flash toward the source, controller rumble, bass hook: Reactions.CameraResponse).</summary>
        public static bool CameraShake { get => GetB(ShakeKey, false); set => SetB(ShakeKey, value); }
        /// <summary>Reduced motion: world sway, spring bones, death launches and the edge flash at 25% (cm.reducedMotion).</summary>
        public static bool ReducedMotion { get => GetB(ReducedMotionKey, false); set => SetB(ReducedMotionKey, value); }
        /// <summary>Floating damage numbers shown.</summary>
        public static bool DamageNumbers { get => GetB(DamageNumbersKey, true); set => SetB(DamageNumbersKey, value); }
        /// <summary>Blood effects on (PlayerPrefs cm.blood, default on).</summary>
        public static bool Blood { get => GetB(BloodKey, true); set => SetB(BloodKey, value); }

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
                CriticalMass.Runtime.Game.FrameGovernor.Reapply();
            }
        }

        /// <summary>Reduce the frame rate when the game is not the front window (cm.bgThrottle, default on). Stored only.</summary>
        public static bool BackgroundThrottle { get => GetB(BgThrottleKey, true); set { SetB(BgThrottleKey, value); CriticalMass.Runtime.Game.FrameGovernor.Reapply(); } }

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
            Vector2Int r = Resolution;
            int w = r.x > 0 ? r.x : Screen.width, h = r.y > 0 ? r.y : Screen.height;
            FullScreenMode mode = FullScreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (w != Screen.width || h != Screen.height || Screen.fullScreenMode != mode) Screen.SetResolution(w, h, mode);
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
            ApplyAll();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void ApplyOnBoot()
        {
            _mixerLooked = false;
            _mixer = null;
            if (Application.isBatchMode) return;   // gates and tests keep the project's settings
            ApplyAll();
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

        static void Save()
        {
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
