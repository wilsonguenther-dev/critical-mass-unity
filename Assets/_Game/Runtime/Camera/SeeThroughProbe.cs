using System;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Runtime.Camera
{
    /// <summary>A vertical cylinder that can stand between the camera and the player (a tree, the Kaiju), sim space.</summary>
    public struct SeeThroughCylinder
    {
        public double X, Z, R, Y0, Y1;
    }

    /// <summary>
    /// "Is the player covered?" (DG-20, pure, view only). Two sight lines run from the camera to the player's hips and
    /// chest; one is blocked when it passes under the top of a wall or a higher terrace (the grid, as the grid view
    /// draws it: island height plus a 1.5 u block for wall and intact breakable cells) or through a cylinder (trees from
    /// the dressed world, the hatched Kaiju). The see-through silhouette is drawn only while this is true, so the
    /// player's own limbs never ghost through his body in open ground.
    /// </summary>
    public static class SeeThroughProbe
    {
        /// <summary>Top of a wall block above its island height (ActorViews.RaisedTop, the grid view's block).</summary>
        public const double WallHeight = 1.5;
        /// <summary>Ray march step along the ground (u): a quarter cell.</summary>
        public const double Step = 0.25;
        /// <summary>Cells this close to the player (u) never count: his own cell and the ledge he stands on.</summary>
        public const double SelfRadius = 0.75;
        /// <summary>The sight lines' heights above the player's ground (hips, chest).</summary>
        public static readonly double[] ProbeHeights = { 0.6, 1.4 };
        /// <summary>March cap per sight line (a 100 u line at 0.25 u).</summary>
        public const int MaxSteps = 400;

        /// <summary>True when a wall, a terrace, a tree or the Kaiju covers the player at (px, pz) on ground py.</summary>
        public static bool Covered(MapDef map, double camX, double camY, double camZ, double px, double py, double pz,
            SeeThroughCylinder[] cylinders, int cylinderCount)
        {
            for (int i = 0; i < ProbeHeights.Length; i++)
                if (Blocked(map, camX, camY, camZ, px, py + ProbeHeights[i], pz, cylinders, cylinderCount)) return true;
            return false;
        }

        /// <summary>One sight line from the camera to (tx, ty, tz).</summary>
        public static bool Blocked(MapDef map, double cx, double cy, double cz, double tx, double ty, double tz,
            SeeThroughCylinder[] cylinders, int cylinderCount)
        {
            double dx = tx - cx, dy = ty - cy, dz = tz - cz;
            double horiz = Math.Sqrt(dx * dx + dz * dz);
            if (map != null && map.Cells != null && horiz > 1e-6)
            {
                int steps = (int)Math.Ceiling(horiz / Step);
                if (steps > MaxSteps) steps = MaxSteps;
                for (int i = 1; i < steps; i++)
                {
                    double t = (double)i / steps;
                    double x = cx + dx * t, z = cz + dz * t;
                    double ex = tx - x, ez = tz - z;
                    if (ex * ex + ez * ez < SelfRadius * SelfRadius) continue;
                    if (x < 0 || z < 0 || x >= map.Width || z >= map.Height) continue;
                    double y = cy + dy * t;
                    if (y < Top(map, x, z)) return true;
                }
            }
            if (cylinders != null)
            {
                int n = Math.Min(cylinderCount, cylinders.Length);
                for (int k = 0; k < n; k++)
                    if (HitsCylinder(cx, cy, cz, dx, dy, dz, cylinders[k])) return true;
            }
            return false;
        }

        /// <summary>The drawn top of the grid at sim (x, z): island height, plus the wall block on a wall cell.</summary>
        public static double Top(MapDef map, double x, double z)
        {
            int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
            if (cx < 0 || cz < 0 || cx >= map.Width || cz >= map.Height) return double.NegativeInfinity;
            int i = cz * map.Width + cx;
            byte c = map.Cells[i];
            double h = map.Height01 != null ? map.Height01[i] : 0;
            return c == Cell.Wall || c == Cell.BreakableIntact ? h + WallHeight : h;
        }

        /// <summary>Segment origin + s * d (s in 0..1, the end excluded by 2%) against a capped vertical cylinder.</summary>
        public static bool HitsCylinder(double ox, double oy, double oz, double dx, double dy, double dz, in SeeThroughCylinder c)
        {
            double fx = ox - c.X, fz = oz - c.Z;
            double a = dx * dx + dz * dz;
            if (a < 1e-12) return false;
            double b = 2 * (fx * dx + fz * dz);
            double cc = fx * fx + fz * fz - c.R * c.R;
            double disc = b * b - 4 * a * cc;
            if (disc < 0) return false;
            double sq = Math.Sqrt(disc);
            double s0 = (-b - sq) / (2 * a), s1 = (-b + sq) / (2 * a);
            // The part of the segment inside the infinite cylinder, then its height range against [Y0, Y1].
            double lo = Math.Max(s0, 0), hi = Math.Min(s1, 0.98);
            if (lo > hi) return false;
            double ya = oy + dy * lo, yb = oy + dy * hi;
            double ymin = Math.Min(ya, yb), ymax = Math.Max(ya, yb);
            return ymax >= c.Y0 && ymin <= c.Y1;
        }
    }
}
