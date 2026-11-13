using System;
using System.Collections.Generic;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Maps;

namespace CriticalMass.Runtime.Input
{
    /// <summary>
    /// Easy movement (2026-11-12, art director: "move diagonally without needing to hold multiple keys"; owner: stuck on
    /// the diagonal bridge corner). Pure maths on the human's move vector in sim world space, applied by
    /// HumanInputState.Fill once per sim tick just before the step, so it adds no latency and reaches the sim only as
    /// actor 0's InputFrame move (the normal input path; bots and replays of recorded frames are untouched).
    /// <list type="bullet">
    /// <item>PointerMove: hold the walk button and the human walks toward the ground point under the cursor, any angle,
    /// easing down over PointerRamp and stopping inside PointerDeadZone.</item>
    /// <item>LaneSteer ("Movement assist", keyboard, ONE direction key): on or approaching a bridge span whose axis is
    /// within LaneMaxAngleDeg of the pressed direction, the move follows the span axis (plus a gentle pull to its centre
    /// line). Every bridge of Skybreak Basin is within 45 degrees of one of the four keys at today's 45 degree camera.</item>
    /// <item>CornerSlide ("Movement assist", any device): when the step ahead is blocked (Collide.SweepActor, the sim's
    /// own pure sweep, predicts under SlideClearShare of a short probe) the move turns by the smallest multiple of 15
    /// degrees, up to SlideMaxAngleDeg, that still makes forward progress, so a body slides along a corner, a staircase
    /// edge or a bridge rail instead of stopping. A head-on push into a flat wall makes no forward progress at any angle and
    /// still stops.</item>
    /// </list>
    /// No allocation; reads World only.
    /// </summary>
    public static class MoveAssist
    {
        /// <summary>World units around the pointer's ground point inside which pointer walking stops.</summary>
        public const double PointerDeadZone = 0.4;
        /// <summary>World units beyond the dead zone over which pointer walking eases from rest to full speed.</summary>
        public const double PointerRamp = 0.8;

        /// <summary>A span counts as "the lane" when its axis is within this many degrees of the pressed direction.</summary>
        public const double LaneMaxAngleDeg = 50;
        /// <summary>World units before a span's entry mouth where lane assist starts steering.</summary>
        public const double LaneApproach = 3.0;
        /// <summary>World units past the far mouth where lane assist lets go.</summary>
        public const double LaneExit = 1.0;
        /// <summary>Largest |lateral| from a span axis that lane assist captures (deck half width + 1.5 u).</summary>
        public const double LaneReach = SkybreakBasinMap.BridgeHalfWidth + 1.5;
        /// <summary>Centre-line pull per unit of lateral offset, and its cap (about 31 degrees).</summary>
        public const double LaneCentreGain = 0.6;
        public const double LaneCentreMax = 0.6;

        /// <summary>Largest turn corner sliding may add (degrees), tried in SlideSteps equal steps.</summary>
        public const double SlideMaxAngleDeg = 60;
        public const int SlideSteps = 4;
        /// <summary>The straight probe is "clear" when it advances at least this share of its length.</summary>
        public const double SlideClearShare = 0.5;
        /// <summary>A turned probe must still advance at least this share along the intended direction.</summary>
        public const double SlideMinForward = 0.3;
        /// <summary>Probe length in sim ticks of full-speed travel.</summary>
        public const double SlideProbeTicks = 3;

        static readonly double LaneCos = Math.Cos(LaneMaxAngleDeg * Math.PI / 180);

        /// <summary>
        /// The move toward the ground point (tx, tz) from the human at (hx, hz): unit direction scaled 0..1 by the ease
        /// ramp. False (no move) inside the dead zone or for a non-finite target.
        /// </summary>
        public static bool PointerMove(double hx, double hz, double tx, double tz, out double mx, out double mz)
        {
            mx = 0;
            mz = 0;
            double dx = tx - hx;
            double dz = tz - hz;
            double d = Math.Sqrt(dx * dx + dz * dz);
            if (!(d > PointerDeadZone) || double.IsInfinity(d)) return false;
            double k = (d - PointerDeadZone) / PointerRamp;
            if (k > 1) k = 1;
            mx = dx / d * k;
            mz = dz / d * k;
            return true;
        }

        /// <summary>
        /// Lane assist for a single held key whose unit world direction is (kx, kz), the human at (x, z). True with the
        /// steered unit direction when a span qualifies (the closest axis wins, ties to the first in table order).
        /// </summary>
        public static bool LaneSteer(double x, double z, double kx, double kz, out double mx, out double mz)
        {
            mx = kx;
            mz = kz;
            IReadOnlyList<BridgeSpan> spans = BridgeSpans.All;
            BridgeSpan best = null;
            double bestLat = double.MaxValue;
            double bestSign = 0;
            for (int i = 0; i < spans.Count; i++)
            {
                BridgeSpan s = spans[i];
                double c = kx * s.Ux + kz * s.Uz;
                if (Math.Abs(c) < LaneCos) continue;
                double sign = c > 0 ? 1 : -1;
                double a = BridgeSpans.Along(s, x, z);
                double p = sign > 0 ? a : s.Len - a; // distance travelled from the entry mouth
                if (p < -LaneApproach || p > s.Len + LaneExit) continue;
                double l = Math.Abs(BridgeSpans.Lateral(s, x, z));
                if (l > LaneReach || l >= bestLat) continue;
                best = s;
                bestLat = l;
                bestSign = sign;
            }
            if (best == null) return false;
            double lat = BridgeSpans.Lateral(best, x, z);
            double along = BridgeSpans.Along(best, x, z);
            double travelled = bestSign > 0 ? along : best.Len - along;
            double corr = 0;
            if (travelled <= best.Len)
            {
                corr = -lat * LaneCentreGain;
                if (corr > LaneCentreMax) corr = LaneCentreMax;
                else if (corr < -LaneCentreMax) corr = -LaneCentreMax;
            }
            // Lateral grows along the normal (-Uz, Ux) (BridgeSpans.Lateral).
            double dx = bestSign * best.Ux - best.Uz * corr;
            double dz = bestSign * best.Uz + best.Ux * corr;
            double len = Math.Sqrt(dx * dx + dz * dz);
            mx = dx / len;
            mz = dz / len;
            return true;
        }

        /// <summary>
        /// Corner sliding for actor id's move (mx, mz) (any length up to 1). True with the turned move (same length) when
        /// the straight step is blocked and a turn of at most SlideMaxAngleDeg still advances; false leaves it unchanged.
        /// </summary>
        public static bool CornerSlide(World w, int id, double mx, double mz, out double ox, out double oz)
        {
            ox = mx;
            oz = mz;
            double m = Math.Sqrt(mx * mx + mz * mz);
            if (!(m > 1e-4) || w == null || id < 0 || id >= w.ActorCount) return false;
            Actor a = w.Actors[id];
            if (!a.Alive || a.AirLen > 0 || a.StandUp > 0) return false;
            double probe = w.Config.Class(a.Cls).MoveSpeed * SimStep.Dt(w.Config) * SlideProbeTicks;
            if (!(probe > 0)) return false;
            double ux = mx / m;
            double uz = mz / m;
            double x0 = w.X[id];
            double z0 = w.Z[id];
            double straight = Forward(w, id, x0, z0, ux, uz, ux, uz, probe);
            if (straight >= SlideClearShare * probe) return false;
            double bestF = straight;
            double bx = 0, bz = 0;
            bool found = false;
            for (int k = 1; k <= SlideSteps; k++)
            {
                double ang = k * (SlideMaxAngleDeg / SlideSteps) * Math.PI / 180;
                double c = Math.Cos(ang);
                double s = Math.Sin(ang);
                for (int side = 0; side < 2; side++)
                {
                    double sn = side == 0 ? s : -s;
                    double rx = ux * c - uz * sn;
                    double rz = ux * sn + uz * c;
                    double f = Forward(w, id, x0, z0, rx, rz, ux, uz, probe);
                    if (f > bestF + 1e-9)
                    {
                        bestF = f;
                        bx = rx;
                        bz = rz;
                        found = true;
                    }
                }
                // The smallest turn that frees the step wins (a larger one would steer the body more than needed).
                if (found && bestF >= SlideMinForward * probe) break;
            }
            if (!found || bestF < SlideMinForward * probe) return false;
            ox = bx * m;
            oz = bz * m;
            return true;
        }

        /// <summary>How far a probe of length len along (dx, dz) from (x0, z0) advances along (ux, uz).</summary>
        static double Forward(World w, int id, double x0, double z0, double dx, double dz, double ux, double uz, double len)
        {
            Collide.SweepActor(w, id, x0, z0, dx * len, dz * len, false, out double x, out double z);
            return (x - x0) * ux + (z - z0) * uz;
        }
    }
}
