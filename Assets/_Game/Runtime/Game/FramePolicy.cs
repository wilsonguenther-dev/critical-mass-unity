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

        public const int DefaultCap = 60;
        public const int MenuCap = 60;
        public const int BatteryMenuCap = 30;
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

        /// <summary>DG-49: on battery, menus at 30 and matches at 60 or below.</summary>
        public static int EffectiveCap(int prefCap, bool menuLike, bool onBattery)
        {
            int cap = EffectiveCap(prefCap, menuLike);
            if (!onBattery) return cap;
            int limit = menuLike ? BatteryMenuCap : BatteryMatchCap;
            return cap == 0 || cap > limit ? limit : cap;
        }

        /// <summary>DG-51: unfocused, keep the normal cap (never below 30); "save power in background" brings back 15.</summary>
        public static int BackgroundCap(int foregroundCap, bool savePower)
        {
            if (savePower) return SavePowerBackgroundFps;
            if (foregroundCap == 0) return foregroundCap;
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
    /// </summary>
    public sealed class TimedCapGuard
    {
        double _age;
        int _frames;
        int _cap = -1;

        /// <summary>true once the timed cap took over (until Reset).</summary>
        public bool ForceTimed { get; private set; }

        public void Reset()
        {
            ForceTimed = false;
            _age = 0;
            _frames = 0;
        }

        /// <summary>One frame of dt seconds at this cap; vsyncActive: vsync is what holds the cap now.</summary>
        public void Feed(double dt, int cap, bool vsyncActive)
        {
            if (cap != _cap)
            {
                _cap = cap;
                _age = 0;
                _frames = 0;
            }
            if (cap <= 0 || ForceTimed) return;
            _age += Math.Max(0, dt);
            _frames++;
            if (_age < FramePolicy.TimedCapWindowSeconds) return;
            double fps = _frames / _age;
            if (vsyncActive && fps > cap * FramePolicy.TimedCapRatio) ForceTimed = true;
            _age = 0;
            _frames = 0;
        }

        /// <summary>The frame settings for a cap with the guard applied.</summary>
        public (int vSync, int target) Settings(int cap, double refreshHz)
        {
            (int v, int t) = FramePolicy.FrameSettings(cap, refreshHz);
            if (ForceTimed && cap > 0) return (0, cap);
            return (v, t);
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
