using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b12-t: the Kaiju ownership tally (SimProfile.KaijuOwnTally) the player's KaijuOwnershipLog feeds once per sim tick
    /// and writes at match end as "[CM.KaijuOwn] ...". Pure counting over a scripted state sequence, no Unity. Every case
    /// prints what it measured, the value and the expected value.
    /// </summary>
    [TestFixture]
    public sealed class KaijuOwnershipLogTests
    {
        static double Hz => GameConfig.CreateDefault().SimHz;

        static void Feed(ref SimProfile.KaijuOwnTally t, KaijuState s, double seconds, double hz)
        {
            int n = (int)Math.Round(seconds * hz);
            for (int i = 0; i < n; i++) t.Sample(s);
        }

        static void Check(string what, double value, double expected)
        {
            Console.WriteLine("[b12-t KaijuOwn] " + what + " value=" + value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                + " expected=" + expected.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            Assert.AreEqual(expected, value, 1e-9, what);
        }

        [Test]
        public void ScriptedSequenceGivesExactSecondsSwitchesAndLongestHold()
        {
            double hz = Hz;
            var t = new SimProfile.KaijuOwnTally();
            Feed(ref t, KaijuState.Egg, 30, hz);
            Feed(ref t, KaijuState.Neutral, 10, hz);
            Feed(ref t, KaijuState.Azure, 50, hz);
            Feed(ref t, KaijuState.Berserk, 5, hz);
            Feed(ref t, KaijuState.Crimson, 20, hz);
            Console.WriteLine("[b12-t KaijuOwn] sequence egg 30 s, neutral 10 s, azure 50 s, berserk 5 s, crimson 20 s at " + hz + " Hz");
            Check("egg seconds", t.EggTicks / hz, 30);
            Check("neutral seconds", t.NeutralTicks / hz, 10);
            Check("azure seconds", t.AzureTicks / hz, 50);
            Check("berserk seconds", t.BerserkTicks / hz, 5);
            Check("crimson seconds", t.CrimsonTicks / hz, 20);
            Check("switches", t.Switches, 3);
            Check("first owned seconds", t.EverOwned ? t.FirstOwnedTick / hz : -1, 40);
            Check("longest hold seconds", t.LongestHoldTicks / hz, 50);
            Check("longest hold is azure (1 = yes)", t.LongestHoldState == KaijuState.Azure ? 1 : 0, 1);
            Check("samples", t.Samples, Math.Round(115 * hz));
        }

        [Test]
        public void NeverOwnedHasNoSwitchesAndNoHold()
        {
            double hz = Hz;
            var t = new SimProfile.KaijuOwnTally();
            Feed(ref t, KaijuState.Egg, 12, hz);
            Feed(ref t, KaijuState.Neutral, 8, hz);
            Feed(ref t, KaijuState.Berserk, 4, hz);
            Console.WriteLine("[b12-t KaijuOwn] sequence egg 12 s, neutral 8 s, berserk 4 s (never owned)");
            Check("switches", t.Switches, 0);
            Check("ever owned (1 = yes)", t.EverOwned ? 1 : 0, 0);
            Check("longest hold seconds", t.LongestHoldTicks / hz, 0);
            t.Reset();
            Check("samples after Reset", t.Samples, 0);
        }

        [Test]
        public void BackAndForthCountsEveryOwnerChangeAndKeepsTheLongerRun()
        {
            double hz = Hz;
            var t = new SimProfile.KaijuOwnTally();
            Feed(ref t, KaijuState.Neutral, 2, hz);
            Feed(ref t, KaijuState.Crimson, 15, hz);
            Feed(ref t, KaijuState.Azure, 6, hz);
            Feed(ref t, KaijuState.Crimson, 9, hz);
            Console.WriteLine("[b12-t KaijuOwn] sequence neutral 2 s, crimson 15 s, azure 6 s, crimson 9 s");
            Check("switches", t.Switches, 3);
            Check("crimson seconds", t.CrimsonTicks / hz, 24);
            Check("longest hold seconds", t.LongestHoldTicks / hz, 15);
            Check("longest hold is crimson (1 = yes)", t.LongestHoldState == KaijuState.Crimson ? 1 : 0, 1);
            Check("first owned seconds", t.FirstOwnedTick / hz, 2);
        }
    }
}
