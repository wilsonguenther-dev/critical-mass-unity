using System;

namespace CriticalMass.Runtime.Game
{
    /// <summary>Why the game is not expected to finish frames right now (any one of them excuses a frameless stretch).</summary>
    [Flags]
    public enum NoFrameExcuse
    {
        None = 0,
        /// <summary>OnApplicationPause(true), or unfocused with Application.runInBackground off.</summary>
        Paused = 1,
        /// <summary>macOS CGSession says the screen is locked; Windows has no input desktop (locked, switch user, UAC).</summary>
        ScreenLocked = 2,
        /// <summary>macOS CGDisplayIsAsleep on the main display.</summary>
        DisplayAsleep = 4,
        /// <summary>Windows: the game window is minimised or hidden.</summary>
        Minimised = 8,
        /// <summary>A managed debugger is attached (a breakpoint stops frames).</summary>
        Debugger = 16,
        /// <summary>Code declared a long synchronous job on the main thread (SessionKeeper.LongWork).</summary>
        LongWork = 32,
        /// <summary>The watchdog's own 0.5 s nap took over SleepGapSeconds by the monotonic or the wall clock (machine sleep, suspend).</summary>
        MachineSlept = 64,
        /// <summary>The main thread answered the watchdog's probe (macOS main dispatch queue; Windows WM_NULL): it is alive and
        /// pumping its run loop, so frames stopped because the OS stopped asking for them (locked screen, occluded or
        /// napping app, a modal move/resize loop), not because the main thread is stuck.</summary>
        MainThreadPumping = 128,
    }

    public enum WatchdogVerdict
    {
        /// <summary>Frames are advancing and the watchdog is armed.</summary>
        Ok,
        /// <summary>No frames, but they are not expected: the clock is held at zero.</summary>
        Excused,
        /// <summary>Within GraceSeconds of an excuse ending: the clock is held at zero.</summary>
        Grace,
        /// <summary>No frames for under HangSeconds, or a hang record already written: counting.</summary>
        Stalled,
        /// <summary>No frames for HangSeconds: write the hang record (once per stall).</summary>
        HangRecord,
        /// <summary>No frames for KillSeconds with nothing excusing it: a real hang; write the final record and abort.</summary>
        Kill,
    }

    /// <summary>One watchdog look (every ~0.5 s): the clocks, the frame counter and what currently excuses missing frames.</summary>
    public struct WatchdogSample
    {
        /// <summary>Monotonic seconds since the watchdog started.</summary>
        public double Now;
        /// <summary>Seconds since the previous look: the larger of the monotonic and the wall-clock gap (macOS's monotonic clock
        /// stops while the machine sleeps; the wall clock does not).</summary>
        public double LoopGap;
        /// <summary>The main thread's frame counter.</summary>
        public int Frames;
        public NoFrameExcuse Excuses;
    }

    public struct WatchdogState
    {
        public int SeenFrames;
        /// <summary>Monotonic time the stall clock counts from (last frame, last excused look or the end of the grace).</summary>
        public double CountFrom;
        /// <summary>The stall clock does not count before this time (the grace after an excuse ends).</summary>
        public double ArmAt;
        public bool Excused;
        public bool HangWritten;
        /// <summary>The excuses at the last excused look (for the log and the hang record).</summary>
        public NoFrameExcuse LastExcuses;
        /// <summary>Seconds without a frame the clock currently counts.</summary>
        public double Stalled;
    }

    /// <summary>
    /// fix/hang-watchdog-locked-screen: the hang watchdog's decision as a pure function (edit-mode tests drive it with
    /// timestamps, frame counts and excuses). The rule: only time in which the game was expected to finish frames and did
    /// not counts. A look with any excuse (paused, screen locked, display asleep, minimised, debugger, declared long work,
    /// machine slept, or the main thread alive and pumping its run loop) holds the clock at zero; once the excuses end, the
    /// clock stays at zero for GraceSeconds more, frames or not; then HangSeconds without a frame writes a hang record and
    /// KillSeconds aborts. A main thread stuck in code (a deadlock, an endless loop, a minutes-long synchronous job nobody
    /// declared) answers no probe, has no excuse and is still killed at KillSeconds.
    /// </summary>
    public static class HangWatchdogRule
    {
        public const double HangSeconds = 5;
        public const double KillSeconds = 60;
        /// <summary>The 0.5 s nap taking longer than this means the machine slept or the process was suspended.</summary>
        public const double SleepGapSeconds = 3;
        /// <summary>After an excuse ends: the first frames after an unlock or a wake may be slow (surfaces rebuilt, App Nap lifting).</summary>
        public const double GraceSeconds = 15;

        public static WatchdogState Start(double now, int frames) =>
            new WatchdogState { SeenFrames = frames, CountFrom = now, ArmAt = now };

        public static WatchdogVerdict Step(ref WatchdogState s, WatchdogSample x)
        {
            NoFrameExcuse why = x.Excuses;
            if (x.LoopGap > SleepGapSeconds) why |= NoFrameExcuse.MachineSlept;

            if (why != NoFrameExcuse.None)
            {
                s.Excused = true;
                s.LastExcuses = why;
                s.SeenFrames = x.Frames;
                s.CountFrom = x.Now;
                s.HangWritten = false;
                s.Stalled = 0;
                return WatchdogVerdict.Excused;
            }
            if (s.Excused)
            {
                s.Excused = false;
                s.ArmAt = x.Now + GraceSeconds;
            }
            if (x.Now < s.ArmAt)
            {
                s.SeenFrames = x.Frames;
                s.CountFrom = x.Now;
                s.HangWritten = false;
                s.Stalled = 0;
                return WatchdogVerdict.Grace;
            }
            if (x.Frames != s.SeenFrames)
            {
                s.SeenFrames = x.Frames;
                s.CountFrom = x.Now;
                s.HangWritten = false;
                s.Stalled = 0;
                return WatchdogVerdict.Ok;
            }
            s.Stalled = x.Now - Math.Max(s.CountFrom, s.ArmAt);
            if (s.Stalled >= KillSeconds) return WatchdogVerdict.Kill;
            if (s.Stalled >= HangSeconds && !s.HangWritten)
            {
                s.HangWritten = true;
                return WatchdogVerdict.HangRecord;
            }
            return WatchdogVerdict.Stalled;
        }

        /// <summary>The excuses as a short comma list for logs ("none" when empty).</summary>
        public static string Describe(NoFrameExcuse e)
        {
            if (e == NoFrameExcuse.None) return "none";
            var parts = new System.Collections.Generic.List<string>();
            foreach (NoFrameExcuse f in (NoFrameExcuse[])Enum.GetValues(typeof(NoFrameExcuse)))
                if (f != NoFrameExcuse.None && (e & f) != 0) parts.Add(f.ToString());
            return string.Join(",", parts);
        }
    }
}
