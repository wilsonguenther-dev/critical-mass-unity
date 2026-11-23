using System;
using CriticalMass.Runtime.Game;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// fix/hang-watchdog-locked-screen: the hang watchdog's decision (HangWatchdogRule.Step) driven with timestamps, frame
    /// counts and excuses the way the watchdog thread drives it (one look every 0.5 s). Pure: no Unity, no threads, no clock.
    /// </summary>
    public sealed class HangWatchdogRuleTests
    {
        const double Look = 0.5;

        /// <summary>Runs looks from t0 (exclusive) to t1 (inclusive); returns the time of the first Kill, or -1.</summary>
        static double Run(ref WatchdogState s, double t0, double t1, Func<double, int> frames, Func<double, NoFrameExcuse> excuses,
                          Action<double, WatchdogVerdict> each = null)
        {
            for (double t = t0 + Look; t <= t1 + 1e-9; t += Look)
            {
                var x = new WatchdogSample { Now = t, LoopGap = Look, Frames = frames(t), Excuses = excuses(t) };
                WatchdogVerdict v = HangWatchdogRule.Step(ref s, x);
                each?.Invoke(t, v);
                if (v == WatchdogVerdict.Kill) return t;
            }
            return -1;
        }

        static int Ticking(double t) => (int)(t * 60);
        static NoFrameExcuse NoExcuse(double t) => NoFrameExcuse.None;

        [Test]
        public void FramesAdvancing_NeverFires()
        {
            var s = HangWatchdogRule.Start(0, 0);
            int records = 0;
            double kill = Run(ref s, 0, 600, Ticking, NoExcuse, (t, v) => { if (v == WatchdogVerdict.HangRecord) records++; });
            Assert.AreEqual(-1, kill);
            Assert.AreEqual(0, records);
        }

        [Test]
        public void RealHang_RecordAtFive_KillAtSixty()
        {
            var s = HangWatchdogRule.Start(0, 100);
            double firstRecord = -1;
            int records = 0;
            double kill = Run(ref s, 0, 120, t => 100, NoExcuse, (t, v) =>
            {
                if (v != WatchdogVerdict.HangRecord) return;
                records++;
                if (firstRecord < 0) firstRecord = t;
            });
            Assert.AreEqual(5.0, firstRecord, 1e-9, "the hang record is written once at 5 s");
            Assert.AreEqual(1, records);
            Assert.AreEqual(60.0, kill, 1e-9, "a real hang still aborts at 60 s");
        }

        [Test]
        public void TheReportedCrash_LaunchedLocked_NoFramesFor63s_DoesNotAbort()
        {
            // 2026-11-22 23:22: no frame from launch while the screen was locked; the old watchdog aborted at 60 s (63 s after launch).
            var s = HangWatchdogRule.Start(0, 0);
            Assert.AreEqual(-1, Run(ref s, 0, 63, t => 0, t => NoFrameExcuse.ScreenLocked));
        }

        [TestCase(NoFrameExcuse.Paused)]
        [TestCase(NoFrameExcuse.ScreenLocked)]
        [TestCase(NoFrameExcuse.DisplayAsleep)]
        [TestCase(NoFrameExcuse.Minimised)]
        [TestCase(NoFrameExcuse.Debugger)]
        [TestCase(NoFrameExcuse.LongWork)]
        [TestCase(NoFrameExcuse.MainThreadPumping)]
        public void AnyExcuse_HoldsTheClock_ForAnHour(NoFrameExcuse why)
        {
            var s = HangWatchdogRule.Start(0, 7);
            int records = 0;
            double kill = Run(ref s, 0, 3600, t => 7, t => why, (t, v) =>
            {
                Assert.AreEqual(WatchdogVerdict.Excused, v);
                if (v == WatchdogVerdict.HangRecord) records++;
            });
            Assert.AreEqual(-1, kill);
            Assert.AreEqual(0, records, "no hang record while frames are not expected");
            Assert.AreEqual(why, s.LastExcuses);
        }

        [Test]
        public void LockFor90s_ThenFramesResume_NoAbort_AndGraceBeforeArming()
        {
            var s = HangWatchdogRule.Start(0, 0);
            Func<double, int> frames = t => t < 10 ? Ticking(t) : t < 100 ? Ticking(10) : Ticking(t);
            Func<double, NoFrameExcuse> excuses = t => t >= 10 && t < 100 ? NoFrameExcuse.ScreenLocked : NoFrameExcuse.None;
            int grace = 0;
            double kill = Run(ref s, 0, 200, frames, excuses, (t, v) =>
            {
                if (v == WatchdogVerdict.Grace) { grace++; Assert.That(t, Is.GreaterThanOrEqualTo(100).And.LessThan(115)); }
                if (t >= 115) Assert.AreEqual(WatchdogVerdict.Ok, v);
            });
            Assert.AreEqual(-1, kill);
            Assert.AreEqual(30, grace, "15 s of grace after the unlock (30 looks)");
        }

        [Test]
        public void HangRightAfterUnlock_KilledAfterGracePlusSixty()
        {
            var s = HangWatchdogRule.Start(0, 0);
            // Locked 10..100 s, then unlocked but the main thread is stuck: no frames, no probe answer.
            double kill = Run(ref s, 0, 400, t => t < 10 ? Ticking(t) : Ticking(10),
                              t => t >= 10 && t < 100 ? NoFrameExcuse.ScreenLocked : NoFrameExcuse.None);
            Assert.AreEqual(100 + HangWatchdogRule.GraceSeconds + HangWatchdogRule.KillSeconds, kill, 1e-9);
        }

        [Test]
        public void MachineSleep_ByLoopGap_IsExcused_ThenGrace()
        {
            var s = HangWatchdogRule.Start(0, 5);
            Assert.AreEqual(WatchdogVerdict.Stalled, HangWatchdogRule.Step(ref s, new WatchdogSample { Now = 0.5, LoopGap = 0.5, Frames = 5 }));
            // The nap took an hour by the wall clock (macOS's monotonic clock does not count sleep).
            Assert.AreEqual(WatchdogVerdict.Excused, HangWatchdogRule.Step(ref s, new WatchdogSample { Now = 1.0, LoopGap = 3600, Frames = 5 }));
            Assert.AreEqual(NoFrameExcuse.MachineSlept, s.LastExcuses);
            Assert.AreEqual(WatchdogVerdict.Grace, HangWatchdogRule.Step(ref s, new WatchdogSample { Now = 1.5, LoopGap = 0.5, Frames = 5 }));
            Assert.AreEqual(-1, Run(ref s, 1.5, 1.5 + HangWatchdogRule.GraceSeconds + HangWatchdogRule.KillSeconds - 1, t => 5, NoExcuse));
        }

        [Test]
        public void ANapOfTwoSeconds_IsNotASleep()
        {
            var s = HangWatchdogRule.Start(0, 5);
            Assert.AreEqual(WatchdogVerdict.Stalled, HangWatchdogRule.Step(ref s, new WatchdogSample { Now = 2.0, LoopGap = 2.0, Frames = 5 }));
            Assert.IsFalse(s.Excused);
        }

        [Test]
        public void FlickeringExcuse_NeverAddsUpToAKill()
        {
            // 20 s locked, 20 s unlocked without frames (inside the grace and a little after), repeated for 10 minutes.
            var s = HangWatchdogRule.Start(0, 1);
            Assert.AreEqual(-1, Run(ref s, 0, 600, t => 1, t => ((int)(t / 20)) % 2 == 0 ? NoFrameExcuse.ScreenLocked : NoFrameExcuse.None));
        }

        [Test]
        public void SecondStall_WritesASecondRecord()
        {
            var s = HangWatchdogRule.Start(0, 0);
            int records = 0;
            // Stall 0..10 s, frames 10..20 s, stall 20..30 s.
            Func<double, int> frames = t => t < 10 ? 0 : t < 20 ? Ticking(t) : Ticking(20);
            Assert.AreEqual(-1, Run(ref s, 0, 30, frames, NoExcuse, (t, v) => { if (v == WatchdogVerdict.HangRecord) records++; }));
            Assert.AreEqual(2, records);
        }

        [Test]
        public void AFrameAfterTheHangRecord_ResetsTheClock()
        {
            var s = HangWatchdogRule.Start(0, 0);
            // 50 s stalled, one frame, 50 s stalled again: never 60 s in a row.
            Assert.AreEqual(-1, Run(ref s, 0, 100, t => t < 50 ? 0 : 1, NoExcuse));
        }

        [Test]
        public void Describe_ListsEveryFlag()
        {
            Assert.AreEqual("none", HangWatchdogRule.Describe(NoFrameExcuse.None));
            Assert.AreEqual("ScreenLocked,MainThreadPumping", HangWatchdogRule.Describe(NoFrameExcuse.MainThreadPumping | NoFrameExcuse.ScreenLocked));
        }
    }
}
