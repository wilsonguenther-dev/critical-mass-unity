using System.Globalization;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// b12-t: how long the Kaiju spent in each state over a recorded match, and how often it changed owner. AutoplayArgs
    /// calls Begin at the recorded match start (it hooks SimDriverBehaviour.Ticked, so it samples exactly once per sim
    /// tick) and End at the match end, which writes one line:
    /// <c>[CM.KaijuOwn] egg=..s neutral=..s azure=..s crimson=..s berserk=..s switches=N firstOwned=..s longestHold=..s(team)</c>
    /// The counting is SimProfile.KaijuOwnTally (pure, in the Sim assembly, so Sim.Tests drives it without Unity).
    /// Reads the Sim only; allocates nothing per tick.
    /// </summary>
    public static class KaijuOwnershipLog
    {
        public const string Tag = "[CM.KaijuOwn]";
        static SimProfile.KaijuOwnTally _tally;
        static SimDriverBehaviour _driver;
        static readonly System.Action<World> OnTickHandler = OnTick;

        /// <summary>The tally so far (tests and the perf report).</summary>
        public static SimProfile.KaijuOwnTally Tally => _tally;

        /// <summary>Start a fresh tally on this driver's ticks (replaces any earlier hook).</summary>
        public static void Begin(SimDriverBehaviour driver)
        {
            Stop();
            _tally.Reset();
            _driver = driver;
            if (_driver != null) _driver.Ticked += OnTickHandler;
        }

        static void OnTick(World w)
        {
            if (w != null && w.Kaiju != null) _tally.Sample(w.Kaiju.State);
        }

        /// <summary>Unhook (the tally is kept until the next Begin).</summary>
        public static void Stop()
        {
            if (_driver != null) _driver.Ticked -= OnTickHandler;
            _driver = null;
        }

        /// <summary>Unhook and log the line; returns it (null when nothing was sampled).</summary>
        public static string End(double simHz)
        {
            Stop();
            if (_tally.Samples == 0) return null;
            string line = Line(_tally, simHz);
            Debug.Log(line);
            return line;
        }

        static string S(int ticks, double hz) => (hz > 0 ? ticks / hz : 0).ToString("0.0", CultureInfo.InvariantCulture) + "s";

        static string Team(KaijuState s) => s == KaijuState.Azure ? "azure" : s == KaijuState.Crimson ? "crimson" : "none";

        /// <summary>The match-end line of a tally (pure; tests).</summary>
        public static string Line(SimProfile.KaijuOwnTally t, double hz) =>
            Tag + " egg=" + S(t.EggTicks, hz) + " neutral=" + S(t.NeutralTicks, hz) + " azure=" + S(t.AzureTicks, hz) +
            " crimson=" + S(t.CrimsonTicks, hz) + " berserk=" + S(t.BerserkTicks, hz) +
            " switches=" + t.Switches.ToString(CultureInfo.InvariantCulture) +
            " firstOwned=" + (t.EverOwned ? S(t.FirstOwnedTick, hz) : "never") +
            " longestHold=" + S(t.LongestHoldTicks, hz) + "(" + (t.LongestHoldTicks > 0 ? Team(t.LongestHoldState) : "none") + ")";

        /// <summary>The tally as one JSON object for the perf report (pure; tests).</summary>
        public static string Json(SimProfile.KaijuOwnTally t, double hz)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            double k = hz > 0 ? 1.0 / hz : 0;
            return string.Format(c,
                "{{\"egg_s\": {0:0.###}, \"neutral_s\": {1:0.###}, \"azure_s\": {2:0.###}, \"crimson_s\": {3:0.###}, \"berserk_s\": {4:0.###}, " +
                "\"switches\": {5}, \"first_owned_s\": {6}, \"longest_hold_s\": {7:0.###}, \"longest_hold_team\": \"{8}\"}}",
                t.EggTicks * k, t.NeutralTicks * k, t.AzureTicks * k, t.CrimsonTicks * k, t.BerserkTicks * k, t.Switches,
                t.EverOwned ? (t.FirstOwnedTick * k).ToString("0.###", c) : "null", t.LongestHoldTicks * k,
                t.LongestHoldTicks > 0 ? Team(t.LongestHoldState) : "none");
        }
    }
}
