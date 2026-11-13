using CriticalMass.Sim.Config;
using UnityEngine;

namespace CriticalMass.Runtime.Flow
{
    /// <summary>The three match speeds the player picks in match setup, left to right on the stepper.</summary>
    public enum MatchSpeedChoice
    {
        /// <summary>0.75x: three sim seconds for every four wall seconds.</summary>
        Relaxed = 0,
        /// <summary>1x: one sim second per wall second (the default).</summary>
        Standard = 1,
        /// <summary>2x: the old default pace (CM-66).</summary>
        Fast = 2,
    }

    /// <summary>
    /// Match speed (owner, 2026-11-12: "should we add the slow mode version too? Does it have to be 2x?"). The pace is the
    /// driver's wall time scale (MatchSetupChoice.Pace -> MatchRequest.Pace -> SimDriverBehaviour.TimeScale: sim seconds per
    /// wall second). The sim runs the same fixed ticks at any pace, so 0.75 is as valid as 1 or 2 and nothing under Sim
    /// changes. The default comes from the sim config (GameConfig.DefaultSpeed); while that constant is still the old 2x
    /// the view defaults to Standard (a rules builder moves the sim default to 1 in its own change).
    /// <para>Real seconds: every timing the player reads (match clock, hatch countdown, respawn countdown, class
    /// cooldowns, highlight times) is sim seconds divided by DisplayPace, the pace of the world on screen, so a
    /// "10" on a countdown means ten seconds on the wall at any speed.</para>
    /// </summary>
    public static class MatchSpeed
    {
        public const double RelaxedPace = 0.75, StandardPace = 1.0, FastPace = 2.0;

        /// <summary>Stepper order, left to right.</summary>
        public static readonly MatchSpeedChoice[] Order = { MatchSpeedChoice.Relaxed, MatchSpeedChoice.Standard, MatchSpeedChoice.Fast };

        /// <summary>
        /// Relaxed is offered: the pace is a plain positive double handed to the driver's fixed-step clock (it takes any
        /// scale; BigMoments' slow motion and the spectator speeds already run fractional scales), so 0.75 needs no sim support.
        /// </summary>
        public static bool RelaxedSupported => true;

        static double _configDefault = -1;
        static double _displayPace = 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            _configDefault = -1;
            _displayPace = 1;
        }

        /// <summary>GameConfig.CreateDefault().DefaultSpeed, read once.</summary>
        public static double ConfigDefaultSpeed
        {
            get
            {
                if (_configDefault <= 0) _configDefault = GameConfig.CreateDefault().DefaultSpeed;
                return _configDefault;
            }
        }

        /// <summary>The default for a sim default speed: Standard while the sim still says 2x, else the nearest choice.</summary>
        public static MatchSpeedChoice DefaultFor(double configDefaultSpeed) =>
            configDefaultSpeed >= FastPace - 1e-9 || configDefaultSpeed <= 0 ? MatchSpeedChoice.Standard : FromPace(configDefaultSpeed);

        public static MatchSpeedChoice DefaultChoice => DefaultFor(ConfigDefaultSpeed);

        public static double DefaultPace => PaceOf(DefaultChoice);

        public static double PaceOf(MatchSpeedChoice c)
        {
            switch (c)
            {
                case MatchSpeedChoice.Relaxed: return RelaxedSupported ? RelaxedPace : StandardPace;
                case MatchSpeedChoice.Fast: return FastPace;
                default: return StandardPace;
            }
        }

        /// <summary>The choice nearest a pace (0 or less: the default).</summary>
        public static MatchSpeedChoice FromPace(double pace)
        {
            if (pace <= 0) return DefaultChoice;
            if (pace < (RelaxedPace + StandardPace) * 0.5) return RelaxedSupported ? MatchSpeedChoice.Relaxed : MatchSpeedChoice.Standard;
            if (pace < (StandardPace + FastPace) * 0.5) return MatchSpeedChoice.Standard;
            return MatchSpeedChoice.Fast;
        }

        /// <summary>Any pace snapped to one of the offered speeds.</summary>
        public static double Snap(double pace) => PaceOf(FromPace(pace));

        /// <summary>Steps the choice left (d &lt; 0) or right, wrapping; Relaxed is skipped when it is not offered.</summary>
        public static MatchSpeedChoice Step(MatchSpeedChoice c, int d)
        {
            int at = System.Array.IndexOf(Order, c);
            if (at < 0) at = 1;
            int n = Order.Length;
            for (int k = 0; k < n; k++)
            {
                at = ((at + (d < 0 ? -1 : 1)) % n + n) % n;
                if (Order[at] != MatchSpeedChoice.Relaxed || RelaxedSupported) return Order[at];
            }
            return c;
        }

        public static string Label(MatchSpeedChoice c)
        {
            switch (c)
            {
                case MatchSpeedChoice.Relaxed: return "RELAXED";
                case MatchSpeedChoice.Fast: return "FAST";
                default: return "STANDARD";
            }
        }

        public static string Multiplier(MatchSpeedChoice c)
        {
            switch (c)
            {
                case MatchSpeedChoice.Relaxed: return "0.75X";
                case MatchSpeedChoice.Fast: return "2X";
                default: return "1X";
            }
        }

        /// <summary>The line the loading screen shows, e.g. "STANDARD SPEED · 1X".</summary>
        public static string LoadingLine(double pace)
        {
            MatchSpeedChoice c = FromPace(pace);
            return Label(c) + " SPEED   ·   " + Multiplier(c);
        }

        /// <summary>Sim seconds per wall second of the world on screen (set when a world is built; 1 when unknown).</summary>
        public static double DisplayPace
        {
            get => _displayPace;
            set => _displayPace = value > 0 ? value : 1;
        }

        /// <summary>Sim seconds as wall seconds at the pace on screen (no allocation; safe every frame).</summary>
        public static double ToReal(double simSeconds) => simSeconds / (_displayPace > 0 ? _displayPace : 1);

        /// <summary>Sim seconds as wall seconds at a given pace (setup previews).</summary>
        public static double ToReal(double simSeconds, double pace) => simSeconds / (pace > 0 ? pace : 1);
    }
}
