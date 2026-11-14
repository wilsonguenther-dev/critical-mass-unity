using System;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// The pure rules behind FrameGovernor and SessionKeeper (no engine calls, so EditMode tests cover every branch):
    /// frame caps (DG-49), the timed-cap safety net, battery caps, the pixel budget and first-launch quality pick
    /// (DG-50), the fullscreen toggle debounce (DG-52), queued frames (DG-56), the thermal parser and the safe-mode
    /// policy (DG-62).
    /// </summary>
    public static class FramePolicy
    {
        // Quality level indices in ProjectSettings/QualitySettings.asset (Very Low, Low, Medium, High, Very High, Ultra).
        public const int LevelVeryLow = 0, LevelLow = 1, LevelMedium = 2, LevelHigh = 3;

        /// <summary>perf-smooth-1: the default limit is OFF, i.e. vsync on and one frame per display refresh (60 on a 60 Hz
        /// screen, 120 on a 120 Hz one). A 60 cap on a 120 Hz screen means vsync every 2 refreshes, and on this Mac 87% of
        /// the frames over 33 ms were that 2-refresh slot holding a slightly late frame for a whole extra 60 Hz frame
        /// (3.7% of frames at High; locked to the display: 0.02%). The 30/60/120/144 choices keep the whole-divisor rule.</summary>
        public const int DefaultCap = 0;
        public const int MenuCap = 60;
        public const int BatteryMenuCap = 30;
        /// <summary>The idle title screen (a live 16v16 backdrop match behind the logo): 20 fps (vsync 3 on 60 Hz). perf-60hz-32p:
        /// at 30 it cost 59 to 61% of a core on the 8 GB M1 Air against the 35% idle budget (main thread 27%, render thread
        /// 15%, job workers and audio the rest). The story film and the attract tour over the title keep the menu rate.</summary>
        public const int TitleCap = 20;
        public const int BatteryMatchCap = 60;
        public const int SavePowerBackgroundFps = 15;
        public const int MinBackgroundFps = 30;
        public const int NoVsyncTarget = 240;
        public const double TimedCapRatio = 1.5;
        public const double TimedCapWindowSeconds = 2.0;

        /// <summary>The pixel budget at default settings (1080p-equivalent) and at High and above.</summary>
        public const double DefaultPixelBudget = 2_100_000;
        public const double HighPixelBudget = 3_700_000;
        public const float MidRenderScale = 0.75f;
        public const float LowRenderScale = 0.75f;
        public const float SafeRenderScale = 0.7f;
        public const float MinRenderScale = 0.4f;

        /// <summary>A long frame (seconds) that counts the session as unclean: Windows resets a GPU that stalls 2 s (TDR).</summary>
        public const double GpuStallSeconds = 1.5;

        /// <summary>perf-smooth-1: the "Unlimited" frame-rate choice (vsync off, no target).</summary>
        public const int Unlimited = -1;

        /// <summary>The frame-rate limit choices (cm.fpsCap) in screen order: 0 = OFF (no limit, locked to the display by
        /// vsync), Unlimited = vsync off with no target.</summary>
        public static readonly int[] CapChoices = { 30, 60, 120, 144, 0, Unlimited };

        /// <summary>
        /// vSyncCount and targetFrameRate for a cap (0 = display rate, Unlimited = none) on a display of refreshHz, vsync on.
        /// perf-smooth-1 rule: always vsync-locked (no tearing, even frame spacing), at the display rate divided by the
        /// largest whole number that keeps the rate at or above the cap (1 to 4): 60 on 60 and 120 Hz, 72 on 144 Hz, 82.5 on
        /// 165 Hz, 75 on 75 Hz; a cap above the display runs at the display rate. The old timed cap (vsync off, target 60 on
        /// 144 Hz) tore and paced unevenly on fixed-refresh panels.
        /// </summary>
        public static (int vSync, int target) FrameSettings(int cap, double refreshHz)
        {
            if (cap == Unlimited) return (0, -1);
            if (cap <= 0) return (1, -1);
            if (double.IsNaN(refreshHz) || refreshHz < 20) refreshHz = 60;
            int n = (int)Math.Floor(refreshHz / cap + 0.02);
            return (Math.Max(1, Math.Min(4, n)), -1);
        }

        /// <summary>A cap read from PlayerPrefs: one of CapChoices; anything else is 60.</summary>
        public static int SanitiseCap(int v) => Array.IndexOf(CapChoices, v) >= 0 ? v : DefaultCap;

        /// <summary>true for "no limit" choices (0 and Unlimited): menus, battery and the title count them as above any cap.</summary>
        static bool NoLimit(int cap) => cap == 0 || cap == Unlimited;

        /// <summary>The cap in force: title, pause and menus never above 60 (0 and Unlimited count as above).</summary>
        public static int EffectiveCap(int prefCap, bool menuLike) =>
            menuLike && (NoLimit(prefCap) || prefCap > MenuCap) ? MenuCap : prefCap;

        /// <summary>DG-49: on battery, menus at 30 and matches at 60 or below.</summary>
        public static int EffectiveCap(int prefCap, bool menuLike, bool onBattery)
        {
            int cap = EffectiveCap(prefCap, menuLike);
            if (!onBattery) return cap;
            int limit = menuLike ? BatteryMenuCap : BatteryMatchCap;
            return NoLimit(cap) || cap > limit ? limit : cap;
        }

        /// <summary>The title screen's cap: never above TitleCap (0 = display rate counts as above).</summary>
        public static int TitleCapFor(int cap) => NoLimit(cap) || cap > TitleCap ? TitleCap : cap;

        /// <summary>DG-51: unfocused, keep the normal cap (never below 30); "save power in background" brings back 15.</summary>
        public static int BackgroundCap(int foregroundCap, bool savePower)
        {
            if (savePower) return SavePowerBackgroundFps;
            if (foregroundCap == 0) return foregroundCap;
            if (foregroundCap == Unlimited) return MenuCap;   // unfocused never runs unbounded
            return Math.Max(MinBackgroundFps, foregroundCap);
        }

        /// <summary>DG-56: frames the CPU may queue ahead of the GPU: 1 at Low and below and in safe mode, else 2.</summary>
        public static int MaxQueuedFrames(int level, bool safe) => safe || level <= LevelLow ? 1 : 2;

        /// <summary>DG-50: which pipeline asset a quality level uses (0 Low, 1 Medium, 2 High).</summary>
        public static int PipelineTier(int level) => level <= LevelLow ? 0 : level == LevelMedium ? 1 : 2;

        /// <summary>The pixel budget of a quality level.</summary>
        public static double PixelBudget(int level) => level >= LevelHigh ? HighPixelBudget : DefaultPixelBudget;

        /// <summary>DG-50 pixel cap: min(chosen, sqrt(budget / (w x h))), floored to 0.01, never below MinRenderScale.</summary>
        public static float RenderScaleFor(float chosen, double budgetPixels, int width, int height)
        {
            if (float.IsNaN(chosen) || chosen <= 0) chosen = 1f;
            chosen = Math.Min(chosen, 1f);
            if (width <= 0 || height <= 0 || budgetPixels <= 0) return chosen;
            double cap = Math.Sqrt(budgetPixels / ((double)width * height));
            double s = Math.Min(chosen, cap);
            s = Math.Floor(s * 100.0) / 100.0;
            return (float)Math.Max(MinRenderScale, s);
        }

        /// <summary>true for integrated GPUs (Intel, AMD APUs, software renderers); Apple silicon counts as capable.</summary>
        public static bool IsIntegrated(string gpuName, int gpuMemoryMb)
        {
            string g = (gpuName ?? "").ToLowerInvariant();
            if (g.Contains("apple")) return false;
            if (g.Contains("intel") && !g.Contains("arc")) return true;
            if (g.Contains("radeon(tm) graphics") || g.Contains("radeon graphics") || (g.Contains("vega") && !g.Contains("rx vega"))) return true;
            if (g.Contains("microsoft basic render") || g.Contains("llvmpipe") || g.Contains("swiftshader") || g.Contains("warp")) return true;
            return gpuMemoryMb > 0 && gpuMemoryMb < 1024;
        }

        /// <summary>true for the base Apple M1 and M2 (no Pro, Max or Ultra in the name).</summary>
        public static bool IsBaseAppleM1M2(string gpuName)
        {
            string g = (gpuName ?? "").ToLowerInvariant();
            if (!(g.Contains("apple m1") || g.Contains("apple m2"))) return false;
            return !(g.Contains("pro") || g.Contains("max") || g.Contains("ultra"));
        }

        /// <summary>
        /// DG-50 first-launch pick: (quality level, render scale, reason). Never above High. Integrated Intel and AMD:
        /// Low at 0.75. Under 6 GB: Low at 0.75. 8 GB or less (systemMemoryMb at or under 8704) or a base M1 or M2: Medium
        /// at 0.75. Otherwise High at 1.0.
        /// </summary>
        public static (int level, float scale, string reason) PickQuality(string gpuName, int gpuMemoryMb, int systemMemoryMb,
            int levelCount, int defaultLevel)
        {
            int max = Math.Max(0, levelCount - 1);
            int Clamp(int l) => Math.Max(0, Math.Min(l, max));
            if (IsIntegrated(gpuName, gpuMemoryMb)) return (Clamp(LevelLow), LowRenderScale, "integrated graphics");
            if (systemMemoryMb > 0 && systemMemoryMb < 6 * 1024) return (Clamp(LevelLow), LowRenderScale, "under 6 GB memory");
            if (systemMemoryMb > 0 && systemMemoryMb <= 8704) return (Clamp(LevelMedium), MidRenderScale, "8 GB memory");
            if (IsBaseAppleM1M2(gpuName)) return (Clamp(LevelMedium), MidRenderScale, "base Apple M1 or M2");
            return (Clamp(Math.Min(LevelHigh, defaultLevel < 0 ? LevelHigh : Math.Max(defaultLevel, LevelHigh))), 1f, "capable machine");
        }

        /// <summary>A window size never above the display's size, aspect kept (also shrinks a saved size, DG-52).</summary>
        public static (int w, int h) ClampToNative(int w, int h, int nativeW, int nativeH)
        {
            if (nativeW <= 0 || nativeH <= 0 || (w <= nativeW && h <= nativeH)) return (w, h);
            double s = Math.Min((double)nativeW / w, (double)nativeH / h);
            return (Math.Max(640, Math.Min(nativeW, (int)(w * s + 1e-6))), Math.Max(360, Math.Min(nativeH, (int)(h * s + 1e-6))));
        }

        /// <summary>
        /// polish-audit-1: the window size after the player (or the OS) switched a full-screen window to a window with
        /// Alt+Enter or the title-bar control. The engine keeps the full display size, so the window's title bar and edges
        /// sit under the taskbar or off the screen; the game sizes it to a 16:9 window of about two thirds of the display
        /// (never under 1280x720 unless the display is smaller, never larger than the display). A window that is already
        /// clearly smaller than the display is left alone: (0, 0) means "keep it".
        /// </summary>
        public static (int w, int h) WindowAfterExternalToggle(int w, int h, int nativeW, int nativeH)
        {
            if (nativeW <= 0 || nativeH <= 0) return (0, 0);
            if (w < nativeW * 0.9 || h < nativeH * 0.9) return (0, 0);
            double tw = nativeW * 2.0 / 3.0, th = tw * 9.0 / 16.0;
            if (th > nativeH * 2.0 / 3.0) { th = nativeH * 2.0 / 3.0; tw = th * 16.0 / 9.0; }
            if (tw < 1280) { tw = 1280; th = 720; }
            (int cw, int ch) = ClampToNative((int)Math.Round(tw), (int)Math.Round(th), nativeW, nativeH);
            return (cw, ch);
        }

        /// <summary>
        /// polish-audit-1: true when the window mode changed without the game asking for it (Alt+Enter, the macOS
        /// full-screen control): the stored "Display" setting must follow, or Settings shows FULL SCREEN over a window and
        /// the next launch silently undoes the player's choice. A change the game itself requested within the last
        /// 3 seconds (RequestWindow) is not external.
        /// </summary>
        public static bool IsExternalModeChange(bool modeChanged, bool requestedThisMode, double secondsSinceRequest) =>
            modeChanged && !(requestedThisMode && secondsSinceRequest >= 0 && secondsSinceRequest <= 3.0);

        /// <summary>
        /// DG-62 safe-mode policy: safe only when the unclean run lasted under 60 s (a crash at startup) or after two unclean
        /// runs in a row (uncleanStreak counts the previous run). -safe always wins.
        /// </summary>
        public static (bool safe, string reason) SafeModeDecision(bool safeFlag, bool previousUnclean, double previousRunSeconds,
            int uncleanStreak)
        {
            if (safeFlag) return (true, "-safe");
            if (!previousUnclean) return (false, "");
            if (previousRunSeconds >= 0 && previousRunSeconds < 60) return (true, "the last session stopped during startup");
            if (uncleanStreak >= 2) return (true, "two sessions in a row ended unexpectedly");
            return (false, "");
        }

        /// <summary>Thermal state from macOS `pmset -g therm` output: 0 nominal, 2 serious (pure; tests).</summary>
        public static int ParsePmsetTherm(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                if (!int.TryParse(line.Substring(eq + 1).Trim(), out int v)) continue;
                if (key == "cpu_speed_limit" && v < 80) return 2;
                if ((key.Contains("thermal warning level") || key.Contains("performance warning level")) && v > 0) return 2;
            }
            return 0;
        }
    }

    /// <summary>
    /// DG-49 safety net: when the measured frame rate stays above 1.5 times the cap for 2 s while vsync is meant to hold it
    /// (display asleep, window hidden, a panel that ignores vSyncCount 2), switch to a timed cap (vsync off, target = cap).
    /// perf-60hz-32p: the display-rate default (cap 0) is covered too (its cap is the refresh rate; batch 6 left it
    /// unguarded), and so is the opposite failure: vsync presents that stall for the drawable timeout (about 1 s: a sleeping
    /// or occluded display; the Air ran 50 such frames a minute with its panel off). StallFrames frames of StallSeconds or
    /// more inside StallWindowSeconds while vsync is on switch to the timed cap as well.
    /// </summary>
    public sealed class TimedCapGuard
    {
        /// <summary>The drawable-timeout signature: a vsync frame of 0.9 to 1.25 s (CAMetalLayer gives up after about 1 s).
        /// Loading frames are not in that band five times in 20 s.</summary>
        public const double StallSeconds = 0.9, StallMaxSeconds = 1.25;
        public const int StallFrames = 5;
        public const double StallWindowSeconds = 20.0;
        double _age;
        int _frames;
        int _cap = -2;
        double _stallAge;
        int _stalls;

        /// <summary>true once the timed cap took over (until Reset).</summary>
        public bool ForceTimed { get; private set; }

        /// <summary>Why the timed cap took over ("" while it has not).</summary>
        public string Why { get; private set; } = "";

        public void Reset()
        {
            ForceTimed = false;
            Why = "";
            _age = 0;
            _frames = 0;
            _stallAge = 0;
            _stalls = 0;
        }

        /// <summary>One frame of dt seconds at this cap; vsyncActive: vsync is what holds the cap now; refreshHz: the display.</summary>
        public void Feed(double dt, int cap, bool vsyncActive, double refreshHz = 60)
        {
            if (cap != _cap)
            {
                // perf-60hz-32p: a new cap is judged afresh. The timed cap used to stick across caps: on the Air the 20 fps
                // title (vsync 3, hidden window) tripped it and the whole match then ran on a 60 timer (5.6% of frames at
                // 33 ms or more against 0.16% on vsync 1).
                _cap = cap;
                ForceTimed = false;
                Why = "";
                _age = 0;
                _frames = 0;
                _stallAge = 0;
                _stalls = 0;
            }
            if (cap < 0 || ForceTimed) return;
            if (double.IsNaN(refreshHz) || refreshHz < 20) refreshHz = 60;
            double rate = cap > 0 ? cap : refreshHz;
            if (vsyncActive)
            {
                _stallAge += Math.Max(0, dt);
                if (dt >= StallSeconds && dt <= StallMaxSeconds) _stalls++;
                if (_stalls >= StallFrames)
                {
                    ForceTimed = true;
                    Why = "vsync presents stalled (" + _stalls + " frames of about 1 s: display asleep or window occluded)";
                    return;
                }
                if (_stallAge >= StallWindowSeconds)
                {
                    _stallAge = 0;
                    _stalls = 0;
                }
            }
            _age += Math.Max(0, dt);
            _frames++;
            if (_age < FramePolicy.TimedCapWindowSeconds) return;
            double fps = _frames / _age;
            if (vsyncActive && fps > rate * FramePolicy.TimedCapRatio)
            {
                ForceTimed = true;
                Why = "vsync not holding (" + Math.Round(fps) + " fps against " + Math.Round(rate) + ")";
            }
            _age = 0;
            _frames = 0;
        }

        /// <summary>The frame settings for a cap with the guard applied (cap 0 timed at the display rate).</summary>
        public (int vSync, int target) Settings(int cap, double refreshHz)
        {
            (int v, int t) = FramePolicy.FrameSettings(cap, refreshHz);
            if (!ForceTimed || cap < 0) return (v, t);
            if (cap > 0) return (0, cap);
            if (double.IsNaN(refreshHz) || refreshHz < 20) refreshHz = 60;
            return (0, (int)Math.Round(refreshHz));
        }
    }

    /// <summary>DG-52: at least MinGapSeconds between fullscreen or window-size changes.</summary>
    public sealed class ToggleDebouncer
    {
        public const double MinGapSeconds = 1.0;
        double _last = double.NegativeInfinity;

        public int Fired { get; private set; }

        /// <summary>true when a change may run now (and records it).</summary>
        public bool TryFire(double nowSeconds)
        {
            if (nowSeconds - _last < MinGapSeconds) return false;
            _last = nowSeconds;
            Fired++;
            return true;
        }
    }

    /// <summary>
    /// DG-60 the fixed-step accumulator with a catch-up cap: at most MaxTicksPerFrame ticks per frame (scaled up with the
    /// time scale so a fast autoplay still keeps pace); at the cap the rest of the backlog is dropped and counted. The sim
    /// never reads the clock: the same inputs give the same world, only fewer ticks per wall second after a hitch.
    /// </summary>
    public sealed class SimClock
    {
        public const int MaxTicksPerFrame = 4;
        /// <summary>The input clamp on one frame's wall time, seconds (DG-05's spiral guard, kept).</summary>
        public const double MaxDelta = 0.25;

        double _acc;

        public double Accumulator => _acc;
        public double DroppedSimSeconds { get; private set; }
        public long DroppedTicks { get; private set; }
        public int LastTicks { get; private set; }

        /// <summary>The tick cap for a time scale and step: 4 at the game's pace; more when one 30 fps frame needs more.</summary>
        public static int TickCap(double timeScale, double dt)
        {
            if (dt <= 0) return MaxTicksPerFrame;
            int need = (int)Math.Ceiling(timeScale * (1.0 / 30.0) / dt);
            return Math.Max(MaxTicksPerFrame, need + 1);
        }

        /// <summary>Adds one frame and returns how many ticks to run now.</summary>
        public int Advance(double unscaledDt, double timeScale, double dt, bool paused)
        {
            if (!paused && unscaledDt > 0 && timeScale > 0) _acc += Math.Min(unscaledDt, MaxDelta) * timeScale;
            int cap = TickCap(timeScale, dt);
            int n = 0;
            while (_acc >= dt && n < cap)
            {
                _acc -= dt;
                n++;
            }
            if (_acc >= dt)
            {
                long extra = (long)Math.Floor(_acc / dt);
                DroppedTicks += extra;
                DroppedSimSeconds += extra * dt;
                _acc -= extra * dt;
            }
            LastTicks = n;
            return n;
        }

        public double Alpha(double dt) => dt > 0 ? _acc / dt : 0;
    }

    /// <summary>
    /// Triangle budget per quality level (integrator smoke 2026-11-12: 4.47 M triangles at High against a 1 M budget; every
    /// CMToon mesh draws three times: lit, inverted-hull outline, shadow caster). Scenery (WorldView's combined groups) is
    /// where the cut is safe; characters keep outline and shadow at every level (readability in a 16v16 crowd).
    /// Low: scenery without outline and without shadow casting. Medium: scenery outline kept, no scenery shadows.
    /// High and above: everything. FrameGovernor sets the level in players; the editor stays at High.
    /// </summary>
    public static class RenderTier
    {
        public static int Level { get; private set; } = FramePolicy.LevelHigh;
        public static event Action Changed;

        /// <summary>(scenery outline, scenery shadows) for a quality level (pure; tests).</summary>
        public static (bool sceneryOutline, bool sceneryShadows) Policy(int level) =>
            level <= FramePolicy.LevelLow ? (false, false) : level == FramePolicy.LevelMedium ? (true, false) : (true, true);

        public static void Set(int level)
        {
            if (level == Level) return;
            Level = level;
            Changed?.Invoke();
        }
    }

    /// <summary>Counters the F3 overlay, the heartbeat and the perf report read (written on the main thread only).</summary>
    public static class RuntimeCounters
    {
        public static double SimDroppedSeconds;
        public static long SimDroppedTicks;
        public static long HudEventsDropped;
        public static int ThermalDrops;
        public static int DisplayChanges;

        public static void Reset()
        {
            SimDroppedSeconds = 0;
            SimDroppedTicks = 0;
            HudEventsDropped = 0;
        }
    }
}
