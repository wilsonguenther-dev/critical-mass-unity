using System;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.Hud.Ui
{
    /// <summary>
    /// Sim ground point to minimap pixel, CAMERA-UP: up on the minimap is up on the screen. The gameplay camera looks along
    /// yaw 45 (Euler(55, 315, 0)), so the minimap is the sim ground rotated by the same yaw: screen right = sim
    /// (cos yaw, -sin yaw), screen down = sim (sin yaw, cos yaw) (sim z maps to Unity -z, SimSpace). At yaw 45 the Azure base
    /// sits upper left, Crimson lower right, the shrine upper right and the corruption island lower left, exactly as the
    /// player sees them. The image is fitted to the land's rotated bounds (every non-void cell corner) plus the cliff depth
    /// under the lowest island and a margin; pixel (0, 0) is the top-left, y down, PxPerUnit pixels per sim unit at 1x
    /// (1x = canvas units on a 1080p screen; tier 2 and 4 multiply every pixel coordinate).
    /// </summary>
    public sealed class MinimapProjection
    {
        /// <summary>Minimap pixels per sim unit at 1x.</summary>
        public const double PxPerUnit = 2.1;
        /// <summary>Margin around the land at 1x, in pixels (room for the Void Navy outline and the haze).</summary>
        public const double MarginPx = 5.0;
        /// <summary>Cliff face depth under an island, sim units, plus CliffPerHeight per unit of island height.</summary>
        public const double CliffUnits = 2.4, CliffPerHeight = 1.2;

        public readonly double YawDeg;
        /// <summary>u (right) = x Rx + z Rz; v (down) = x Dx + z Dz, in sim units.</summary>
        public readonly double Rx, Rz, Dx, Dz;
        /// <summary>(u, v) in sim units of pixel (0, 0)'s top-left corner.</summary>
        public readonly double U0, V0;
        /// <summary>Image size at 1x, pixels.</summary>
        public readonly int Width, Height;
        public readonly int MapWidth, MapHeight;

        MinimapProjection(double yawDeg, double u0, double v0, int w, int h, int mw, int mh)
        {
            YawDeg = yawDeg;
            double y = yawDeg * Math.PI / 180.0;
            Rx = Math.Cos(y); Rz = -Math.Sin(y);
            Dx = Math.Sin(y); Dz = Math.Cos(y);
            U0 = u0; V0 = v0; Width = w; Height = h; MapWidth = mw; MapHeight = mh;
        }

        /// <summary>Screen-up as a sim ground direction (unit).</summary>
        public double UpX => -Dx;
        public double UpZ => -Dz;

        public static MinimapProjection For(MapDef map, double yawDeg)
        {
            var probe = new MinimapProjection(yawDeg, 0, 0, 0, 0, map.Width, map.Height);
            double uMin = double.MaxValue, uMax = double.MinValue, vMin = double.MaxValue, vMax = double.MinValue, vCliff = double.MinValue;
            for (int cz = 0; cz < map.Height; cz++)
                for (int cx = 0; cx < map.Width; cx++)
                {
                    int i = cz * map.Width + cx;
                    byte c = map.Cells[i];
                    if (c == Cell.Void) continue;
                    double h = map.Height01 != null ? map.Height01[i] : 0;
                    for (int k = 0; k < 4; k++)
                    {
                        double x = cx + (k & 1), z = cz + (k >> 1);
                        double u = probe.U(x, z), v = probe.V(x, z);
                        if (u < uMin) uMin = u;
                        if (u > uMax) uMax = u;
                        if (v < vMin) vMin = v;
                        if (v > vMax) vMax = v;
                        if (c != Cell.Bridge && v + CliffUnits + CliffPerHeight * h > vCliff) vCliff = v + CliffUnits + CliffPerHeight * h;
                    }
                }
            if (vCliff > vMax) vMax = vCliff;
            double m = MarginPx / PxPerUnit;
            int w = (int)Math.Ceiling((uMax - uMin + 2 * m) * PxPerUnit);
            int hpx = (int)Math.Ceiling((vMax - vMin + 2 * m) * PxPerUnit);
            double u0 = uMin - m - (w / PxPerUnit - (uMax - uMin + 2 * m)) * 0.5;
            double v0 = vMin - m - (hpx / PxPerUnit - (vMax - vMin + 2 * m)) * 0.5;
            return new MinimapProjection(yawDeg, u0, v0, w, hpx, map.Width, map.Height);
        }

        public double U(double x, double z) => x * Rx + z * Rz;
        public double V(double x, double z) => x * Dx + z * Dz;

        /// <summary>Sim point to minimap pixel coordinates (top-left origin, y down; pixel i covers [i, i + 1)).</summary>
        public Vector2 ToPixel(double simX, double simZ, int tier = 1)
        {
            double k = PxPerUnit * tier;
            return new Vector2((float)((U(simX, simZ) - U0) * k), (float)((V(simX, simZ) - V0) * k));
        }

        /// <summary>Minimap pixel coordinates back to the sim ground point.</summary>
        public void ToSim(double px, double py, int tier, out double simX, out double simZ)
        {
            double k = PxPerUnit * tier;
            double u = px / k + U0, v = py / k + V0;
            simX = u * Rx + v * Dx;
            simZ = u * Rz + v * Dz;
        }

        /// <summary>A sim facing (radians, 0 = +x, sim (cos f, sin f)) as a uGUI z rotation (degrees, counter-clockwise, y up)
        /// of a marker that points right at 0.</summary>
        public float FacingToUiDeg(double facing) => (float)(-(facing * 180.0 / Math.PI + YawDeg));
    }
}
