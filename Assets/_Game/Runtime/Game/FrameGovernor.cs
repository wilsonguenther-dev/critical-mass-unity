using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Input;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// DG-05, hardened by DG-49 to DG-52, DG-56 and DG-62 (2026-11-12 panel). Installs itself (DontDestroyOnLoad) and only
    /// touches engine settings, never game or Sim state. The pure rules live in FramePolicy (tests).
    /// <para>One owner: this is the ONLY code that writes QualitySettings.vSyncCount, Application.targetFrameRate and
    /// QualitySettings.maxQueuedFrames. GameSettings stores the player's choices and calls Reapply.</para>
    /// <para>Frame cap: cm.fpsCap (30, 60, 120, 0 = display rate; default 60) through vsync when it divides the display,
    /// else a timed cap; menus at 60; on battery menus at 30 and matches at 60 or below; -novsync = vsync off with a 240
    /// target, never unlimited; if the measured rate stays over 1.5x the cap for 2 s, a timed cap takes over.</para>
    /// <para>Background (DG-51): an unfocused window keeps drawing at the normal cap (never below 30) with sound on, so a
    /// streamer who clicks chat never shows a frozen or silent game; "save power in background" (cm.bgThrottle, default
    /// off) brings back 15 fps.</para>
    /// <para>Pixels (DG-50): render scale = min(chosen, sqrt(budget / screen pixels)), budget 2.1 MP (3.7 MP at High and
    /// above), applied to the level's pipeline asset in the player only (the editor never runs this, so project files
    /// never change). Heat: a serious thermal state (macOS pmset, checked every 2 min off the main thread) drops one level.</para>
    /// <para>Displays (DG-52): a change of window size, display count or refresh rate re-reads the display, re-applies the
    /// cap and the pixel budget. Window mode and size changes go through RequestWindow: at least 1 s apart, never
    /// exclusive fullscreen, never larger than the display.</para>
    /// <para>Safe mode (DG-62): SessionKeeper decides (a crash at startup, or two unclean sessions in a row, or -safe);
    /// safe mode selects the Low asset at 0.7 scale; the notice lives in Settings (SafeNotice), not on the title.</para>
    /// Files (DATA = GamePaths.DataDir): machine-profile.json (+ .bak, atomic, versioned); see SessionKeeper for the rest.
    /// </summary>
    public sealed class FrameGovernor : MonoBehaviour
    {
        public const string PrefFpsCap = "cm.fpsCap";
        public const string PrefBgThrottle = "cm.bgThrottle";
        public const string PrefVSync = "cm.video.vsync";
        public const string PrefQuality = "cm.video.quality";
        public const int DefaultCap = FramePolicy.DefaultCap;
        public const int MenuCap = FramePolicy.MenuCap;
        public const int BackgroundFps = FramePolicy.SavePowerBackgroundFps;
        public const int SafeCap = 30;
        public const float LowRenderScale = FramePolicy.LowRenderScale;
        public const float SafeRenderScale = FramePolicy.SafeRenderScale;
        public const int ProfileVersion = 1;
        public const string ProfileFile = "machine-profile.json";
        public const string MarkerFile = SessionKeeper.MarkerFile;
        public const int NoVsyncTarget = FramePolicy.NoVsyncTarget;

        /// <summary>Set by any menu while it is open: the frame cap drops to 60 (MenuCap).</summary>
        public static bool MenuOpen;

        public static bool SafeMode => SessionKeeper.SafeMode;
        public static string SafeReason => SessionKeeper.SafeReason;

        /// <summary>The neutral line the settings page shows when this session started in lighter settings (null otherwise).</summary>
        public static string SafeNotice => SessionKeeper.SafeMode
            ? (SessionKeeper.SafeReason == "-safe" ? "Started with light video settings (-safe)."
                : "This session started with lighter video settings after an unexpected exit. Choose a quality level to change it.")
            : null;

        public static int AppliedVSync { get; private set; }
        public static int AppliedTarget { get; private set; }
        public static int AppliedCap { get; private set; }
        public static float AppliedRenderScale { get; private set; } = 1f;
        public static int AppliedQueuedFrames { get; private set; }
        /// <summary>true while the title screen is up (read by SimDriverBehaviour's backdrop throttle).</summary>
        public static bool TitleUp { get; private set; }
        /// <summary>perf-60hz-32p: the title is up and idle (no story film, no attract tour over it): the backdrop's LOD1,
        /// half-rate poses and the 20 fps title cap apply.</summary>
        public static bool IdleTitle { get; private set; }

        /// <summary>The quality the run asked for (-quality or the stored choice); the perf gate compares it with the real one.</summary>
        public static string RequestedQuality { get; private set; } = "";

        static GameObject _host;
        static bool _reapply;
        static readonly ToggleDebouncer WindowDebounce = new ToggleDebouncer();
        static (int w, int h, FullScreenMode mode)? _pendingWindow;
        static string _notice;
        static float _noticeUntil;

        /// <summary>The settings page saved a frame setting: re-read them on the next frame.</summary>
        public static void Reapply() => _reapply = true;

        static int _forceLevel = -1;

        /// <summary>perf-smooth-1 measurement only (autoplay -autoplayPacing): null or "rule" = the frame cap rule; "display" =
        /// vsync every refresh, no cap; "timed60" = vsync off, a timed 60.</summary>
        public static string PacingOverride;

        /// <summary>perf-60hz-32p: why the frame pacing in force was chosen (perf report "pacing_reason", Player.log).</summary>
        public static string PacingReason { get; private set; }

        /// <summary>perf-smooth-1 (autoplay -autoplayQualities): run the next match at this level, as -quality NAME would.</summary>
        public static void ForceRunQuality(string name)
        {
            int l = QualityArg(new[] { "-quality", name }, QualitySettings.names);
            if (l < 0) return;
            _forceLevel = l;
            _reapply = true;
            if (l != QualitySettings.GetQualityLevel()) QualitySettings.SetQualityLevel(l, true);
            RenderTier.Set(l);   // now, so the next scene's world builds at this tier
        }

        /// <summary>A short line at the top of the screen for a few seconds ("Settings could not be saved").</summary>
        public static void ShowNotice(string text, float seconds = 6f)
        {
            _notice = text;
            _noticeUntil = Time.realtimeSinceStartup + seconds;
        }

        /// <summary>
        /// DG-52: change the window. Exclusive fullscreen becomes borderless; the size is clamped to the display; changes
        /// closer than 1 s apart are held and the newest runs when the gap has passed. Player builds only.
        /// </summary>
        public static void RequestWindow(int w, int h, FullScreenMode mode)
        {
            if (Application.isEditor || Application.isBatchMode) return;
            if (mode == FullScreenMode.ExclusiveFullScreen || mode == FullScreenMode.MaximizedWindow) mode = FullScreenMode.FullScreenWindow;
            (w, h) = FramePolicy.ClampToNative(w, h, Display.main.systemWidth, Display.main.systemHeight);
            _pendingWindow = (w, h, mode);
        }

        string _dataDir;
        bool _focused = true, _title, _inputPaused, _onBattery;
        float _checkAge = 10f;
        int _prefCap = DefaultCap, _prefBg, _prefVSync = 1, _baseLevel = -1, _seenW, _seenH, _seenDisplays;
        double _seenHz;
        float _chosenScale = 1f;
        int _argCap = -1;   // -fpsCap N (measurement runs: the floor profile uses 30); -1 = none
        readonly TimedCapGuard _guard = new TimedCapGuard();
        HumanInputSource _input;
        FrameGovernorGui _gui;
        Thread _thermalThread;
        volatile int _thermal;
        volatile bool _stopThermal;
        bool _thermalDropped;
        float _coolSince;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            Application.runInBackground = true;   // DG-51; ProjectSetup sets the same
            if (_host != null) return;
            _host = new GameObject("CriticalMass FrameGovernor");
            DontDestroyOnLoad(_host);
            _host.AddComponent<FrameGovernor>();
        }

        // ── pure parts kept for DG-05 callers and tests (FramePolicy holds the rules) ──

        public static (int vSync, int target) FrameSettings(int cap, double refreshHz) => FramePolicy.FrameSettings(cap, refreshHz);
        public static int SanitiseCap(int v) => FramePolicy.SanitiseCap(v);
        public static int EffectiveCap(int prefCap, bool menuLike) => FramePolicy.EffectiveCap(prefCap, menuLike);
        public static bool IsIntegrated(string gpuName, int gpuMemoryMb) => FramePolicy.IsIntegrated(gpuName, gpuMemoryMb);
        public static (int level, float scale, string reason) PickQuality(string gpuName, int gpuMemoryMb, int systemMemoryMb, int levelCount, int defaultLevel) =>
            FramePolicy.PickQuality(gpuName, gpuMemoryMb, systemMemoryMb, levelCount, defaultLevel);
        public static (int w, int h) ClampToNative(int w, int h, int nativeW, int nativeH) => FramePolicy.ClampToNative(w, h, nativeW, nativeH);

        /// <summary>The level asked for by -quality NAME|INDEX, or -1 (pure; tests).</summary>
        public static int QualityArg(string[] args, string[] names)
        {
            if (args == null || names == null) return -1;
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (!string.Equals(args[i], "-quality", StringComparison.OrdinalIgnoreCase)) continue;
                string v = args[i + 1];
                if (int.TryParse(v, out int n)) return n >= 0 && n < names.Length ? n : -1;
                for (int k = 0; k < names.Length; k++)
                    if (string.Equals(names[k], v, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(names[k].Replace(" ", ""), v, StringComparison.OrdinalIgnoreCase)) return k;
            }
            return -1;
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
            SessionKeeper.EnsureStarted();
            if (SessionKeeper.SecondInstance)
            {
                enabled = false;
                Application.Quit(0);
                return;
            }
            _dataDir = GamePaths.DataDir;
            if (SessionKeeper.ToolRun) return;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], "-fpsCap", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out int c))
                    _argCap = SanitiseCap(c);
            ApplyQuality();
            ClampWindow();
            ReadDisplay();
            StartThermal();
            Debug.Log($"[CM.Governor] quality={QualitySettings.names[QualitySettings.GetQualityLevel()]} requested={RequestedQuality} " +
                      $"renderScale={AppliedRenderScale:0.##} safe={SafeMode}{(SafeMode ? " (" + SafeReason + ")" : "")} " +
                      $"gpu=\"{SystemInfo.graphicsDeviceName}\" api={SystemInfo.graphicsDeviceType} vram={SystemInfo.graphicsMemorySize}MB " +
                      $"ram={SystemInfo.systemMemorySize}MB refresh={Screen.currentResolution.refreshRateRatio.value:0.#}Hz " +
                      $"screen={Screen.width}x{Screen.height} mode={Screen.fullScreenMode} displays={Display.displays.Length} data={_dataDir}");
        }

        void OnDestroy()
        {
            _stopThermal = true;
        }

        static UniversalRenderPipelineAsset CurrentAsset() =>
            QualitySettings.renderPipeline as UniversalRenderPipelineAsset ?? GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;

        void ApplyQuality()
        {
            string[] names = QualitySettings.names;
            int level;
            float scale;
            if (SafeMode)
            {
                level = Math.Min(FramePolicy.LevelLow, names.Length - 1);
                scale = SafeRenderScale;
                RequestedQuality = names[level];
            }
            else
            {
                string path = Path.Combine(_dataDir, ProfileFile);
                string text = SafeFile.ReadVersioned(path, ProfileVersion, out string why);
                var f = BuildInfo.Parse(text);
                if (text != null && f.TryGetValue("level", out string ls) && int.TryParse(ls, out level) &&
                    f.TryGetValue("render_scale", out string ss) && float.TryParse(ss, NumberStyles.Float, CultureInfo.InvariantCulture, out scale))
                {
                }
                else
                {
                    string reason;
                    (level, scale, reason) = FramePolicy.PickQuality(SystemInfo.graphicsDeviceName, SystemInfo.graphicsMemorySize,
                        SystemInfo.systemMemorySize, names.Length, FramePolicy.LevelHigh);
                    if (why != null && why != "missing") Debug.LogWarning($"[CM.Governor] {ProfileFile}: {why}; picked defaults again");
                    SafeFile.TryWriteAtomic(path, SafeFile.Versioned(ProfileVersion,
                        "level=" + level + "\nrender_scale=" + scale.ToString("0.##", CultureInfo.InvariantCulture) +
                        "\nreason=" + reason + "\ngpu=" + SystemInfo.graphicsDeviceName + "\nram_mb=" + SystemInfo.systemMemorySize + "\n"), true, out _);
                }
                // The player's own choice wins, except in an isolated measurement run (-dataDir): the owner's choice must not leak in.
                int chosen = GamePaths.Isolated ? -1 : PlayerPrefs.GetInt(PrefQuality, -1);
                if (chosen >= 0 && chosen < names.Length)
                {
                    level = chosen;
                    if (level >= FramePolicy.LevelHigh) scale = 1f;
                }
                int arg = QualityArg(Environment.GetCommandLineArgs(), names);
                if (arg >= 0)
                {
                    level = arg;
                    scale = level <= FramePolicy.LevelLow ? LowRenderScale : 1f;
                }
                level = Mathf.Clamp(level, 0, names.Length - 1);
                RequestedQuality = names[level];
            }
            _baseLevel = level;
            _chosenScale = Mathf.Clamp(scale, FramePolicy.MinRenderScale, 1f);
            SetLevel(level);
        }

        void SetLevel(int level)
        {
            level = Mathf.Clamp(level, 0, QualitySettings.names.Length - 1);
            if (level != QualitySettings.GetQualityLevel()) QualitySettings.SetQualityLevel(level, true);
            RenderTier.Set(level);
            ApplyPixels();
            int queued = FramePolicy.MaxQueuedFrames(level, SafeMode);
            if (QualitySettings.maxQueuedFrames != queued) QualitySettings.maxQueuedFrames = queued;
            AppliedQueuedFrames = queued;
            if (SafeMode && CurrentAsset() is UniversalRenderPipelineAsset a) a.shadowDistance = Mathf.Min(a.shadowDistance, 40f);
            _guard.Reset();
        }

        void ApplyPixels()
        {
            int level = QualitySettings.GetQualityLevel();
            float s = FramePolicy.RenderScaleFor(_chosenScale, FramePolicy.PixelBudget(level), Screen.width, Screen.height);
            UniversalRenderPipelineAsset a = CurrentAsset();
            // Player only (Awake disables this component in the editor): the asset in a player is an in-memory object,
            // so the project's .asset file can never change.
            if (a != null && Mathf.Abs(a.renderScale - s) > 0.001f) a.renderScale = s;
            AppliedRenderScale = s;
        }

        static void ClampWindow()
        {
            int nw = Display.main.systemWidth, nh = Display.main.systemHeight;
            (int w, int h) = FramePolicy.ClampToNative(Screen.width, Screen.height, nw, nh);
            FullScreenMode mode = Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen ? FullScreenMode.FullScreenWindow : Screen.fullScreenMode;
            if (SafeMode && mode == FullScreenMode.Windowed) (w, h) = FramePolicy.ClampToNative(w, h, 1280, 720);
            if (w != Screen.width || h != Screen.height || mode != Screen.fullScreenMode) RequestWindow(w, h, mode);
        }

        FullScreenMode? _seenMode;
        FullScreenMode _requestedMode = FullScreenMode.FullScreenWindow;
        double _requestedAt = double.NegativeInfinity;

        /// <summary>
        /// polish-audit-1: Alt+Enter (allowFullscreenSwitch) or the OS changed the window mode behind the settings page:
        /// store the new mode as the player's "Display" choice, and give a new window a sensible size instead of the full
        /// display size (title bar under the taskbar). Player builds only (this component is off in the editor).
        /// </summary>
        void WatchExternalModeChange()
        {
            FullScreenMode now = Screen.fullScreenMode;
            if (_seenMode == null) { _seenMode = now; return; }
            bool changed = now != _seenMode.Value;
            _seenMode = now;
            if (!FramePolicy.IsExternalModeChange(changed, now == _requestedMode, Time.realtimeSinceStartupAsDouble - _requestedAt)) return;
            bool full = now != FullScreenMode.Windowed;
            if (!GamePaths.Isolated) CriticalMass.Runtime.Menu.GameSettings.RememberFullScreen(full);   // never the owner's prefs from a -dataDir run
            Debug.Log($"[CM.Governor] window mode changed outside the game ({now}): Display setting stored as {(full ? "full screen" : "windowed")}");
            if (full) return;
            (int w, int h) = FramePolicy.WindowAfterExternalToggle(Screen.width, Screen.height, Display.main.systemWidth, Display.main.systemHeight);
            if (w > 0 && h > 0) RequestWindow(w, h, FullScreenMode.Windowed);
        }

        void ReadDisplay()
        {
            _seenW = Screen.width;
            _seenH = Screen.height;
            _seenDisplays = Display.displays.Length;
            _seenHz = Screen.currentResolution.refreshRateRatio.value;
        }

        void OnApplicationFocus(bool focus) => _focused = focus;

        void OnApplicationPause(bool pause)
        {
            if (pause) _focused = false;
        }

        // ── heat (macOS: pmset off the main thread, every 2 minutes; other platforms: none, UNVERIFIED API) ──

        void StartThermal()
        {
            if (Application.platform != RuntimePlatform.OSXPlayer || GamePaths.HasFlag("-noThermal")) return;
            _thermalThread = new Thread(() =>
            {
                while (!_stopThermal)
                {
                    try
                    {
                        var psi = new ProcessStartInfo("/usr/bin/pmset", "-g therm")
                        {
                            UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
                        };
                        using (Process p = Process.Start(psi))
                        {
                            string text = p.StandardOutput.ReadToEnd();
                            if (!p.WaitForExit(2000)) p.Kill();
                            _thermal = FramePolicy.ParsePmsetTherm(text);
                        }
                    }
                    catch (Exception)
                    {
                        return;   // no pmset: no heat signal, never an error
                    }
                    for (int i = 0; i < 120 && !_stopThermal; i++) Thread.Sleep(1000);
                }
            }) { IsBackground = true, Name = "CM thermal" };
            _thermalThread.Start();
        }

        void UpdateThermal()
        {
            bool hot = _thermal >= 2;
            if (hot && !_thermalDropped && _baseLevel > 0)
            {
                _thermalDropped = true;
                RuntimeCounters.ThermalDrops++;
                Debug.LogWarning("[CM.Governor] the machine reports a serious thermal state; one quality step lower until it cools");
                SetLevel(QualitySettings.GetQualityLevel() - 1);
                _coolSince = -1f;
            }
            else if (!hot && _thermalDropped)
            {
                if (_coolSince < 0) _coolSince = Time.realtimeSinceStartup;
                else if (Time.realtimeSinceStartup - _coolSince > 300f)
                {
                    _thermalDropped = false;
                    SetLevel(QualitySettings.GetQualityLevel() + 1);
                }
            }
        }

        // ── per frame ──

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            SessionKeeper.Tick(dt);
            if (SessionKeeper.ToolRun) return;
            _checkAge += dt;
            if (_checkAge >= 0.5f || _reapply)
            {
                bool reapplied = _reapply;
                _checkAge = 0f;
                _reapply = false;
                // An isolated measurement run (-dataDir) never reads the owner's choices: defaults plus its own flags.
                bool own = !GamePaths.Isolated;
                _prefCap = _argCap >= 0 ? _argCap : own ? SanitiseCap(PlayerPrefs.GetInt(PrefFpsCap, DefaultCap)) : DefaultCap;
                _prefBg = own ? PlayerPrefs.GetInt(PrefBgThrottle, 0) : 0;
                _prefVSync = own ? PlayerPrefs.GetInt(PrefVSync, 1) : 1;
                _title = FindAnyObjectByType<TitleScreen>() != null;
                TitleUp = _title;
                if (_input == null) _input = FindAnyObjectByType<HumanInputSource>();
                _inputPaused = _input != null && _input.State != null && _input.State.Paused;
                _onBattery = SystemInfo.batteryStatus == BatteryStatus.Discharging;
                SessionKeeper.Phase = _title ? "title" : _inputPaused ? "paused" : MenuOpen ? "menu" : "match";
                if (reapplied && _forceLevel >= 0)
                {
                    _baseLevel = _forceLevel;
                    _chosenScale = _forceLevel <= FramePolicy.LevelLow ? LowRenderScale : 1f;
                    RequestedQuality = QualitySettings.names[_forceLevel];
                    SetLevel(_forceLevel);
                    _forceLevel = -1;
                }
                else if (reapplied)
                {
                    int q = QualitySettings.GetQualityLevel();
                    if (q != _baseLevel && !_thermalDropped)
                    {
                        _baseLevel = q;
                        _chosenScale = q >= FramePolicy.LevelHigh ? 1f : Math.Min(_chosenScale, 1f);
                    }
                    SetLevel(q);
                }
                if (Screen.width != _seenW || Screen.height != _seenH || Display.displays.Length != _seenDisplays ||
                    Math.Abs(Screen.currentResolution.refreshRateRatio.value - _seenHz) > 0.5)
                {
                    RuntimeCounters.DisplayChanges++;
                    Debug.Log($"[CM.Governor] display changed: {_seenW}x{_seenH}@{_seenHz:0.#} -> {Screen.width}x{Screen.height}@" +
                              $"{Screen.currentResolution.refreshRateRatio.value:0.#} displays {_seenDisplays} -> {Display.displays.Length}");
                    ReadDisplay();
                    ApplyPixels();
                    _guard.Reset();
                }
                UpdateThermal();
                WatchExternalModeChange();
            }

            if (_pendingWindow.HasValue && WindowDebounce.TryFire(Time.realtimeSinceStartupAsDouble))
            {
                (int w, int h, FullScreenMode mode) = _pendingWindow.Value;
                _pendingWindow = null;
                if (w != Screen.width || h != Screen.height || mode != Screen.fullScreenMode)
                {
                    Screen.SetResolution(w, h, mode);
                    _requestedMode = mode;
                    _requestedAt = Time.realtimeSinceStartupAsDouble;
                }
            }

            AutoplayArgs.Options o = AutoplayArgs.Current;
            bool menuLike = _title || _inputPaused || MenuOpen;
            int fg = EffectiveCapFor(menuLike);
            bool background = !_focused && !o.Autoplay;
            int cap;
            int vsync, target;
            string reasonGuard = null;
            if (o.Autoplay && o.NoVsync)
            {
                cap = 0;
                vsync = 0;
                target = NoVsyncTarget;   // -novsync: vsync off with a high target, never unbounded (DG-49)
            }
            else
            {
                cap = background ? FramePolicy.BackgroundCap(fg, _prefBg != 0) : fg;
                double hz = Screen.currentResolution.refreshRateRatio.value;
                (vsync, target) = _guard.Settings(cap, hz);
                if (cap == FramePolicy.Unlimited)
                {
                    vsync = 0;   // perf-smooth-1: the player chose UNLIMITED: vsync off, no target
                    target = -1;
                }
                else if (_prefVSync == 0 || (background && _prefBg != 0))
                {
                    vsync = 0;   // vsync off by choice (or the save-power cap): still capped, never unbounded
                    target = cap > 0 ? cap : (double.IsNaN(hz) || hz < 20 ? 60 : (int)Math.Round(hz));
                }
                _guard.Feed(dt, cap, vsync > 0, hz);
                if (_guard.ForceTimed && cap >= 0 && vsync == 0 && target > 0 && _prefVSync != 0) reasonGuard = _guard.Why;
            }
            string reason = vsync > 0 ? "vsync " + vsync : target > 0 ? "timed " + target : "unlimited";
            if (reasonGuard != null) reason += " (" + reasonGuard + ")";
            if (o.Autoplay && PacingOverride == "display") { vsync = 1; target = -1; reason = "override display"; }
            else if (o.Autoplay && PacingOverride == "timed60") { vsync = 0; target = 60; reason = "override timed60"; }
            // perf-60hz-32p: hidden automation windows keep vsync too. Measured on the Air in one session (build a81efe4,
            // Medium): vsync 1 = 0.67% of frames at 33 ms or more, p95 16.8 ms; a timed 60 (vsync off, targetFrameRate 60) =
            // 7.59%, p95 24.2 ms. A timer is the worse pacer on macOS, hidden or not.
            if (QualitySettings.vSyncCount != vsync) QualitySettings.vSyncCount = vsync;
            if (Application.targetFrameRate != target) Application.targetFrameRate = target;
            AppliedVSync = vsync;
            AppliedTarget = target;
            AppliedCap = cap;
            if (reason != PacingReason)
            {
                PacingReason = reason;
                Debug.Log($"[CM.Governor] pacing: {reason} (cap {cap}, vSyncCount {vsync}, targetFrameRate {target}, " +
                          $"{Screen.currentResolution.refreshRateRatio.value:0.#} Hz{(AutomationWindow.Active ? ", hidden window" : "")})");
            }

            string notice = null;
            if (_notice != null && Time.realtimeSinceStartup < _noticeUntil) notice = _notice;
            if (AutoplayArgs.StoppedNotice != null && Time.realtimeSinceStartup < AutoplayArgs.StoppedNoticeUntil)
                notice = (notice != null ? notice + "\n" : "") + AutoplayArgs.StoppedNotice;
            if (SystemEvents.Notice != null) notice = (notice != null ? notice + "\n" : "") + SystemEvents.Notice;
            _gui.Notice = notice;
            _gui.enabled = notice != null;
        }

        int EffectiveCapFor(bool menuLike)
        {
            int pref = SafeMode ? Math.Min(_prefCap == 0 ? SafeCap : _prefCap, SafeCap) : _prefCap;
            int cap = FramePolicy.EffectiveCap(pref, menuLike, _onBattery);
            // perf-60hz-32p: the idle title runs at TitleCap; the story film and the attract tour over it keep the menu rate.
            IdleTitle = _title && !Menus.StoryIntro.IsPlaying && !Menus.AttractMode.IsTouring;
            return IdleTitle ? FramePolicy.TitleCapFor(cap) : cap;
        }
    }

    /// <summary>The small notice line at the top of the screen (a stopped system, a failed save); OnGUI only while shown.</summary>
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
}
