using System;
using CriticalMass.Runtime.Game;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// px lane T: the Unity-side player-seat logger (Runtime/Game/PlayerSeatLogger.cs, docs/testing/PLAYER-SEAT-TELEMETRY.md)
    /// is off by default, only a flag or the dev pref switches it on, and with it off the per-frame and per-tick paths
    /// allocate nothing.
    /// </summary>
    public sealed class PlayerSeatLoggerTests
    {
        [Test]
        public void OffByDefault_OnlyTheFlagOrTheDevPrefSwitchesItOn()
        {
            Assert.IsFalse(PlayerSeatLogger.Active, "nothing switched the logger on in edit mode");
            Assert.IsNull(PlayerSeatLogHook.AfterTick, "the tick hook has no listener while the logger is off");
            Assert.IsFalse(PlayerSeatLogger.Wanted(new string[0], 0), "no flag, pref unset: off");
            Assert.IsFalse(PlayerSeatLogger.Wanted(new[] { "-batchmode", "-autoplay" }, 0), "other flags: off");
            Assert.IsTrue(PlayerSeatLogger.Wanted(new[] { "-batchmode", PlayerSeatLogger.Flag }, 0), "the flag switches it on (Editor)");
            Assert.IsTrue(PlayerSeatLogger.Wanted(new string[0], 1), "the dev pref switches it on (Editor)");
        }

        [Test]
        public void WhenOffTheFrameAndTickPathsAllocateNothing()
        {
            Assert.IsFalse(PlayerSeatLogger.Active);
            for (int i = 0; i < 100; i++) { PlayerSeatLogger.Pump(); PlayerSeatLogHook.AfterTick?.Invoke(null, null); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++)
            {
                PlayerSeatLogger.Pump();
                PlayerSeatLogHook.AfterTick?.Invoke(null, null);
            }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Console.WriteLine("[px-T] logger off: " + bytes + " bytes over 10000 frames + ticks (limit 0)");
            Assert.AreEqual(0, bytes);
        }
    }
}
