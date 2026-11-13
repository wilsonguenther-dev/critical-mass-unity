using System;
using System.Globalization;
using System.IO;
using System.Text;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Input;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// DG-05 "do not blow up the player's machine" (after the 2026-11-12 watchdog restart: the player had no frame limit).
    /// Installs itself (DontDestroyOnLoad) and only touches engine settings, never game or Sim state.
    ///
    /// Frame limit: PlayerPrefs cm.fpsCap (30, 60, 120, 0 = display rate; default 60) through vertical sync at every
    /// quality level: vSyncCount = refresh / cap when that divides the display rate, else vSync off and
    /// targetFrameRate = cap; measured every 2 s and switched to targetFrameRate if the display runs faster than the cap.
    /// Title, pause and menus (MenuOpen) cap at 60 whatever the setting. cm.bgThrottle (default 1): an unfocused or
    /// minimised window drops to 15 fps and pauses audio. Autoplay runs are exempt from the background throttle, and
    /// -novsync (measurement only) lifts every cap.
    ///
    /// Safe start: the first launch picks the quality from the machine (integrated graphics or under 8 GB memory: the low
    /// level at render scale 0.75) and stores it in machine-profile.json; the window never exceeds the display's native
    /// resolution; -safe forces the lowest settings; a session marker left by a run that did not exit cleanly starts the
    /// next run in safe settings and says so on the title screen.
    ///
    /// Files the game writes (all in Application.persistentDataPath unless noted): machine-profile.json (+ .bak, atomic,
    /// versioned), session.running (marker, deleted on a clean quit), crash-log.txt (+ crash-log.1.txt, capped),
    /// perf-last.json (autoplay only); Unity's Player.log (Application.consoleLogPath); PlayerPrefs cm.fpsCap and
    /// cm.bgThrottle (read here; written only by a settings menu).
    /// </summary>
    public sealed class FrameGovernor : MonoBehaviour
    {
        public const string PrefFpsCap = "cm.fpsCap";
        public const string PrefBgThrottle = "cm.bgThrottle";
        /// <summary>The settings page's keys (GameSettings, controls lane): a vsync choice and a quality choice win over the pick.</summary>
        public const string PrefVSync = "cm.video.vsync";
        public const string PrefQuality = "cm.video.quality";
        public const int DefaultCap = 60;
        public const int MenuCap = 60;
        public const int BackgroundFps = 15;
        public const int SafeCap = 30;
        public const float LowRenderScale = 0.75f;
        public const float SafeRenderScale = 0.7f;
        public const int ProfileVersion = 1;
        public const string ProfileFile = "machine-profile.json";
        public const string MarkerFile = "session.running";

        /// <summary>Set by any menu while it is open: the frame cap drops to 60 (MenuCap).</summary>
        public static bool MenuOpen;

        /// <summary>true when this run started in safe settings; SafeReason says why ("-safe", "last run did not exit cleanly").</summary>
        public static bool SafeMode { get; private set; }
        public static string SafeReason { get; private set; } = "";

        /// <summary>The frame settings now applied: vSyncCount, targetFrameRate and the cap they aim at (0 = uncapped).</summary>
        public static int AppliedVSync { get; private set; }
        public static int AppliedTarget { get; private set; }
        public static int AppliedCap { get; private set; }

        static GameObject _host;
        static bool _reapply;

        /// <summary>
        /// The settings page saved cm.fpsCap, cm.bgThrottle, cm.video.vsync or cm.video.quality: re-read them on the next
        /// frame. FrameGovernor is the only code that writes QualitySettings.vSyncCount and Application.targetFrameRate.
        /// </summary>
        public static void Reapply() => _reapply = true;
        string _dataDir;
        bool _focused = true, _pausedAudio, _forceTarget, _title, _inputPaused;
        float _checkAge = 10f, _measureAge, _noticeUntil;
        int _measureFrames, _prefCap = DefaultCap, _prefBg = 1, _prefVSync = 1;
        HumanInputSource _input;
        FrameGovernorGui _gui;

        /// <summary>The frame target under -novsync.</summary>
        public const int NoVsyncTarget = 240;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            // Keep running (at the background rate below) when the window loses focus: a streamer clicking away must not
            // freeze the game. ProjectSettings sets the same; this covers builds made before it.
            Application.runInBackground = true;
            if (_host != null) return;
            _host = new GameObject("CriticalMass FrameGovernor");
            DontDestroyOnLoad(_host);
            _host.AddComponent<FrameGovernor>();
        }

        // ── pure parts (tests) ──

        /// <summary>vSyncCount and targetFrameRate for a cap (0 = display rate) on a display of refreshHz.</summary>
        public static (int vSync, int target) FrameSettings(int cap, double refreshHz)
        {
            if (cap <= 0) return (1, -1);
            if (double.IsNaN(refreshHz) || refreshHz < 20) refreshHz = 60;
            int n = (int)Math.Round(refreshHz / cap);
            if (n >= 1 && n <= 4 && Math.Abs(refreshHz / n - cap) <= cap * 0.1) return (n, -1);
            return (0, cap);
        }

        /// <summary>A cap read from PlayerPrefs: one of 30, 60, 120, 0; anything else is 60.</summary>
        public static int SanitiseCap(int v) => v == 30 || v == 60 || v == 120 || v == 0 ? v : DefaultCap;

        /// <summary>The cap in force: title, pause and menus never above 60 (0 counts as above).</summary>
        public static int EffectiveCap(int prefCap, bool menuLike) =>
            menuLike && (prefCap == 0 || prefCap > MenuCap) ? MenuCap : prefCap;

        /// <summary>true for integrated GPUs (Intel, AMD APUs); Apple silicon counts as a capable GPU (U-004 targets it).</summary>
        public static bool IsIntegrated(string gpuName, int gpuMemoryMb)
        {
            string g = (gpuName ?? "").ToLowerInvariant();
            if (g.Contains("apple")) return false;
            if (g.Contains("intel")) return true;
            if (g.Contains("radeon(tm) graphics") || g.Contains("radeon graphics") || g.Contains("vega") && !g.Contains("rx vega")) return true;
            if (g.Contains("microsoft basic render") || g.Contains("llvmpipe") || g.Contains("swiftshader")) return true;
            return gpuMemoryMb > 0 && gpuMemoryMb < 1024;
        }

        /// <summary>First-launch pick: (quality level, render scale, reason).</summary>
        public static (int level, float scale, string reason) PickQuality(string gpuName, int gpuMemoryMb, int systemMemoryMb,
            int levelCount, int defaultLevel)
        {
            int low = Math.Min(1, Math.Max(0, levelCount - 1));
            if (IsIntegrated(gpuName, gpuMemoryMb)) return (low, LowRenderScale, "integrated graphics");
            if (systemMemoryMb > 0 && systemMemoryMb < 8 * 1024) return (low, LowRenderScale, "under 8 GB memory");
            return (Math.Max(0, Math.Min(defaultLevel, levelCount - 1)), 1f, "capable machine");
        }

        /// <summary>A window size never above the display's native size, aspect kept.</summary>
        public static (int w, int h) ClampToNative(int w, int h, int nativeW, int nativeH)
        {
            if (nativeW <= 0 || nativeH <= 0 || (w <= nativeW && h <= nativeH)) return (w, h);
            double s = Math.Min((double)nativeW / w, (double)nativeH / h);
            return (Math.Max(640, (int)(w * s + 1e-6)), Math.Max(360, (int)(h * s + 1e-6)));
        }

        // ── lifecycle ──

        void Awake()
        {
            _gui = gameObject.AddComponent<FrameGovernorGui>();
            _gui.enabled = false;
            if (Application.isEditor)
            {
                enabled = false;   // frame caps, settings and files belong to players; the editor and its tests keep their own
                return;
            }
            _dataDir = Application.persistentDataPath;
            string marker = Path.Combine(_dataDir, MarkerFile);
            bool unclean = File.Exists(marker);
            bool safeFlag = Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, "-safe", StringComparison.OrdinalIgnoreCase));
            try
            {
                Directory.CreateDirectory(_dataDir);
                SafeFile.WriteAtomic(marker, "started_utc=" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\n" +
                                             "version=" + BuildInfo.Label + "\n", false);
            }
            catch (Exception) { /* a read-only disk must not stop the game */ }
            Application.quitting += OnQuitting;

            SafeMode = safeFlag || unclean;
            SafeReason = safeFlag ? "-safe" : unclean ? "last run did not exit cleanly" : "";
            ApplyQuality();
            ClampWindow();
            if (unclean) _noticeUntil = Time.realtimeSinceStartup + 12f;
            Debug.Log($"[CM.Governor] quality={QualitySettings.names[QualitySettings.GetQualityLevel()]} " +
                      $"renderScale={CurrentRenderScale():0.##} safe={SafeMode}{(SafeMode ? " (" + SafeReason + ")" : "")} " +
                      $"gpu=\"{SystemInfo.graphicsDeviceName}\" vram={SystemInfo.graphicsMemorySize}MB ram={SystemInfo.systemMemorySize}MB " +
                      $"refresh={Screen.currentResolution.refreshRateRatio.value:0.#}Hz data={_dataDir}");
        }

        void OnQuitting()
        {
            try
            {
                if (_dataDir != null) File.Delete(Path.Combine(_dataDir, MarkerFile));
            }
            catch (Exception) { }
        }

        static float CurrentRenderScale() =>
            QualitySettings.renderPipeline is UniversalRenderPipelineAsset a ? a.renderScale
            : GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset d ? d.renderScale : 1f;

        static void SetRenderScale(float s)
        {
            if (QualitySettings.renderPipeline is UniversalRenderPipelineAsset a) a.renderScale = s;
            else if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset d) d.renderScale = s;
        }

        void ApplyQuality()
        {
            if (SafeMode)
            {
                QualitySettings.SetQualityLevel(0, true);
                SetRenderScale(SafeRenderScale);
                if (QualitySettings.renderPipeline is UniversalRenderPipelineAsset a) a.shadowDistance = Mathf.Min(a.shadowDistance, 40f);
                return;
            }
            string path = Path.Combine(_dataDir, ProfileFile);
            int level;
            float scale;
            string reason;
            string text = SafeFile.ReadVersioned(path, ProfileVersion, out string why);
            var f = BuildInfo.Parse(text);
            if (text != null && f.TryGetValue("level", out string ls) && int.TryParse(ls, out level) &&
                f.TryGetValue("render_scale", out string ss) && float.TryParse(ss, NumberStyles.Float, CultureInfo.InvariantCulture, out scale))
            {
                reason = f.TryGetValue("reason", out string r) ? r : "stored";
            }
            else
            {
                (level, scale, reason) = PickQuality(SystemInfo.graphicsDeviceName, SystemInfo.graphicsMemorySize,
                    SystemInfo.systemMemorySize, QualitySettings.names.Length, QualitySettings.GetQualityLevel());
                if (why != null) Debug.LogWarning($"[CM.Governor] {ProfileFile}: {why}; picked defaults again");
                try
                {
                    SafeFile.WriteAtomic(path, SafeFile.Versioned(ProfileVersion,
                        "level=" + level + "\nrender_scale=" + scale.ToString("0.##", CultureInfo.InvariantCulture) +
                        "\nreason=" + reason + "\ngpu=" + SystemInfo.graphicsDeviceName + "\nram_mb=" + SystemInfo.systemMemorySize + "\n"), true);
                }
                catch (Exception) { }
            }
            int chosen = PlayerPrefs.GetInt(PrefQuality, -1);   // the player's own choice on the settings page wins
            if (chosen >= 0 && chosen < QualitySettings.names.Length) level = chosen;
            level = Mathf.Clamp(level, 0, QualitySettings.names.Length - 1);
            if (level != QualitySettings.GetQualityLevel()) QualitySettings.SetQualityLevel(level, true);
            SetRenderScale(Mathf.Clamp(scale, 0.5f, 1f));
        }

        static void ClampWindow()
        {
            int nw = Display.main.systemWidth, nh = Display.main.systemHeight;
            (int w, int h) = ClampToNative(Screen.width, Screen.height, nw, nh);
            if (SafeMode && Screen.fullScreenMode == FullScreenMode.Windowed) (w, h) = ClampToNative(w, h, 1280, 720);
            if (w != Screen.width || h != Screen.height) Screen.SetResolution(w, h, Screen.fullScreenMode);
        }

        void OnApplicationFocus(bool focus) => _focused = focus;

        void OnApplicationPause(bool pause)
        {
            if (pause) _focused = false;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _checkAge += dt;
            if (_checkAge >= 0.5f || _reapply)
            {
                _checkAge = 0f;
                _reapply = false;
                _prefCap = SanitiseCap(PlayerPrefs.GetInt(PrefFpsCap, DefaultCap));
                _prefBg = PlayerPrefs.GetInt(PrefBgThrottle, 1);
                _prefVSync = PlayerPrefs.GetInt(PrefVSync, 1);
                _title = FindAnyObjectByType<TitleScreen>() != null;
                if (_input == null) _input = FindAnyObjectByType<HumanInputSource>();
                _inputPaused = _input != null && _input.State != null && _input.State.Paused;
            }

            AutoplayArgs.Options o = AutoplayArgs.Current;
            bool background = !_focused && _prefBg != 0 && !o.Autoplay;
            int cap;
            int vsync, target;
            if (o.Autoplay && o.NoVsync)
            {
                cap = 0;
                vsync = 0;
                target = NoVsyncTarget;   // -novsync: vsync off with a high target, never unbounded (DG-49)
            }
            else if (background)
            {
                cap = BackgroundFps;
                vsync = 0;
                target = BackgroundFps;
            }
            else
            {
                cap = EffectiveCap(SafeMode ? Math.Min(_prefCap == 0 ? SafeCap : _prefCap, SafeCap) : _prefCap, _title || _inputPaused || MenuOpen);
                (vsync, target) = FrameSettings(cap, Screen.currentResolution.refreshRateRatio.value);
                if ((_forceTarget || _prefVSync == 0) && cap > 0)
                {
                    vsync = 0;   // vsync off by choice, or the display ignores vSyncCount > 1: still capped
                    target = cap;
                }
                else if (_prefVSync == 0)
                {
                    vsync = 0;   // "display rate" with vsync off: cap at the refresh rate, never unbounded
                    double hz = Screen.currentResolution.refreshRateRatio.value;
                    target = double.IsNaN(hz) || hz < 20 ? 60 : (int)Math.Round(hz);
                }
            }
            if (cap != AppliedCap) { _measureAge = 0f; _measureFrames = 0; }
            if (QualitySettings.vSyncCount != vsync) QualitySettings.vSyncCount = vsync;
            if (Application.targetFrameRate != target) Application.targetFrameRate = target;
            AppliedVSync = vsync;
            AppliedTarget = target;
            AppliedCap = cap;

            if (background != _pausedAudio)
            {
                AudioListener.pause = background;
                _pausedAudio = background;
            }

            // The display may not honour vSyncCount > 1 (a 120 Hz panel at cap 60): measure, and fall back to the target.
            if (cap > 0 && vsync > 0 && !_forceTarget)
            {
                _measureAge += dt;
                _measureFrames++;
                if (_measureAge >= 2f)
                {
                    double fps = _measureFrames / _measureAge;
                    if (fps > cap * 1.25) _forceTarget = true;
                    _measureAge = 0f;
                    _measureFrames = 0;
                }
            }

            string notice = null;
            if (SafeMode && (_title || Time.realtimeSinceStartup < _noticeUntil))
                notice = SafeReason == "-safe" ? "Safe settings (-safe)." : "The last session ended unexpectedly. Started in safe settings.";
            if (AutoplayArgs.StoppedNotice != null && Time.realtimeSinceStartup < AutoplayArgs.StoppedNoticeUntil)
                notice = (notice != null ? notice + "\n" : "") + AutoplayArgs.StoppedNotice;
            _gui.Notice = notice;
            _gui.enabled = notice != null;
        }
    }

    /// <summary>The small notice line at the top of the screen (safe start, a stopped system); OnGUI only while shown.</summary>
    public sealed class FrameGovernorGui : MonoBehaviour
    {
        public string Notice;
        GUIStyle _style;

        void OnGUI()
        {
            if (string.IsNullOrEmpty(Notice)) return;
            float s = Mathf.Max(0.5f, Screen.height / 1080f);
            if (_style == null) _style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            _style.fontSize = Mathf.RoundToInt(18 * s);
            float w = 760 * s;
            GUI.Box(new Rect((Screen.width - w) / 2f, 18 * s, w, (Notice.Contains("\n") ? 64 : 38) * s), Notice, _style);
        }
    }

    /// <summary>
    /// Atomic, versioned, corruption-tolerant small files: write to PATH.tmp then rename over PATH (the old file kept as
    /// PATH.bak when asked); a file starts with "cm_format_version=N"; a missing, unreadable or wrong-version file reads
    /// as null (the caller falls back to defaults), trying PATH.bak before giving up.
    /// </summary>
    public static class SafeFile
    {
        public const string VersionKey = "cm_format_version";

        public static string Versioned(int version, string body) => VersionKey + "=" + version + "\n" + body;

        public static void WriteAtomic(string path, string text, bool keepBak)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                if (keepBak) File.Copy(path, path + ".bak", true);
                File.Delete(path);
            }
            File.Move(tmp, path);
        }

        /// <summary>The body of a versioned file, or null with the reason in why ("missing", "corrupt", "version 2").</summary>
        public static string ReadVersioned(string path, int version, out string why)
        {
            string body = TryRead(path, version, out why);
            if (body != null) return body;
            // A crash between delete and rename in WriteAtomic leaves only the .bak: read it whenever the file is not good.
            string bak = TryRead(path + ".bak", version, out _);
            if (bak != null) why = null;
            return bak;
        }

        static string TryRead(string path, int version, out string why)
        {
            why = null;
            if (!File.Exists(path))
            {
                why = "missing";
                return null;
            }
            try
            {
                string t = File.ReadAllText(path);
                int nl = t.IndexOf('\n');
                string first = (nl >= 0 ? t.Substring(0, nl) : t).Trim();
                if (!first.StartsWith(VersionKey + "=", StringComparison.Ordinal))
                {
                    why = "corrupt (no version line)";
                    return null;
                }
                if (!int.TryParse(first.Substring(VersionKey.Length + 1), out int v) || v != version)
                {
                    why = "format version " + first.Substring(VersionKey.Length + 1) + ", expected " + version;
                    return null;
                }
                return nl >= 0 ? t.Substring(nl + 1) : "";
            }
            catch (Exception e)
            {
                why = "unreadable: " + e.GetType().Name;
                return null;
            }
        }
    }
}
