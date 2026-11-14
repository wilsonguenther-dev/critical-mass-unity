using System;
using System.Globalization;
using CriticalMass.Runtime.Game;
using UnityEngine;

namespace CriticalMass.Runtime.View.Anim
{
    /// <summary>
    /// fix/movement-shiver-1, the view half of the "no shiver" detector: watches one drawn quantity (a yaw in degrees, or a
    /// drawn ground position) frame by frame and counts direction reversals (the change this frame against the last real
    /// change: opposite sign for a yaw, more than 120 degrees apart for a position). More than MaxReversals in one
    /// WindowSeconds window of sim time is a flag: one `[CM.Shiver] view ...` log line (automation runs or -shiverLog only,
    /// at most LogCap lines; the count is always kept). No allocation except the log line.
    /// </summary>
    public sealed class ViewShiverProbe
    {
        public const double WindowSeconds = 1.0;
        public const int MaxReversals = 3;
        public const int LogCap = 100;
        /// <summary>A change smaller than this (degrees, or metres) does not count for direction.</summary>
        public double MinYawStep = 0.3, MinMoveStep = 0.005;

        private readonly string _name;
        private double _winStart = double.NaN;
        private int _rev;
        private double _last, _lastX, _lastZ;
        private static int _logOn = -1;
        private static int _logged;

        /// <summary>Flags raised so far (read by captures and tests).</summary>
        public int Flags { get; private set; }

        public ViewShiverProbe(string name) { _name = name; }

        public void Reset()
        {
            _winStart = double.NaN;
            _rev = 0;
            _last = _lastX = _lastZ = 0;
        }

        /// <summary>A yaw change (degrees) drawn this frame at sim time now, the body at (x, z).</summary>
        public void SampleYaw(double step, double now, double x, double z)
        {
            Window(now, x, z, "yaw");
            if (Math.Abs(step) < MinYawStep) return;
            if (_last != 0 && Math.Sign(step) != Math.Sign(_last)) _rev++;
            _last = step;
        }

        /// <summary>A drawn ground move (dx, dz in sim units) this frame at sim time now, the body at (x, z).</summary>
        public void SampleMove(double dx, double dz, double now, double x, double z)
        {
            Window(now, x, z, "position");
            double len = Math.Sqrt(dx * dx + dz * dz);
            if (len < MinMoveStep) return;
            double ux = dx / len, uz = dz / len;
            if ((_lastX != 0 || _lastZ != 0) && ux * _lastX + uz * _lastZ < -0.5) _rev++;
            _lastX = ux;
            _lastZ = uz;
        }

        private void Window(double now, double x, double z, string what)
        {
            if (double.IsNaN(_winStart) || now < _winStart) { _winStart = now; _rev = 0; return; }
            if (now - _winStart < WindowSeconds) return;
            if (_rev > MaxReversals)
            {
                Flags++;
                if (_logOn < 0) _logOn = SimDriverBehaviour.ShiverLogWanted() ? 1 : 0;   // automation or -shiverLog only (see there)
                if (_logOn == 1 && _logged < LogCap)
                {
                    _logged++;
                    CultureInfo ic = CultureInfo.InvariantCulture;
                    Debug.Log("[CM.Shiver] view " + _name + " " + what + " reversed " + _rev.ToString(ic) + " times in " +
                              (now - _winStart).ToString("F2", ic) + " s at (" + x.ToString("F1", ic) + ", " + z.ToString("F1", ic) + ")");
                }
            }
            _winStart = now;
            _rev = 0;
        }
    }
}
