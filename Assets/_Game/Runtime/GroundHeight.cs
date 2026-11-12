using System;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime
{
    /// <summary>
    /// The one Runtime ground-height helper (review finding R20261112T1918-2-1). Port of picking.ts islandHeightAt at
    /// 09e0904: map.Height01 of the cell under a sim point, with the cell clamped to the map (never 0 off the map). The
    /// browser's groundHeightAt adds daisLiftAt (the 0.15 u hill dais, CM-25); the Unity view does not draw the dais yet,
    /// so At is the island height alone until it does. Used for the mouse-aim plane (pickHumanAim, humanInput.ts:166) and
    /// for every ActorViews ground height.
    /// </summary>
    public static class GroundHeight
    {
        /// <summary>map.Height01 of the cell under sim (x, z), the cell clamped to the map (picking.ts islandHeightAt).</summary>
        public static double IslandHeightAt(MapDef map, double x, double z)
        {
            double fx = Math.Floor(x);
            double fz = Math.Floor(z);
            // Same comparisons as the browser: NaN and negatives go to 0, past the edge goes to the last cell.
            int cx = !(fx >= 0) ? 0 : fx >= map.Width ? map.Width - 1 : (int)fx;
            int cz = !(fz >= 0) ? 0 : fz >= map.Height ? map.Height - 1 : (int)fz;
            return map.Height01[cz * map.Width + cx];
        }

        /// <summary>Visual ground height under sim (x, z): the island height (the dais lift joins once the dais is drawn).</summary>
        public static double At(MapDef map, double x, double z) => IslandHeightAt(map, x, z);

        /// <summary>
        /// The horizontal Unity plane at height h. Unity's Plane keeps n.p + distance = 0, so the plane y = h is
        /// (up, -h); the browser's planeY = h.
        /// </summary>
        public static Plane PlaneAt(double h) => new Plane(Vector3.up, -(float)h);

        /// <summary>
        /// pickHumanAim (humanInput.ts:160-170): intersect the pointer ray with the plane at the ground height of the
        /// human's cell (humanX, humanZ are the sim position of actor 0) and return the sim ground point. False when the
        /// ray never meets that plane in front of its origin.
        /// </summary>
        public static bool TryAimPoint(Ray ray, MapDef map, double humanX, double humanZ, out double simX, out double simZ)
        {
            double h = map == null ? 0 : At(map, humanX, humanZ);
            if (PlaneAt(h).Raycast(ray, out float d))
            {
                SimSpace.ToSim(ray.GetPoint(d), out simX, out simZ);
                return true;
            }
            simX = simZ = 0;
            return false;
        }
    }
}
