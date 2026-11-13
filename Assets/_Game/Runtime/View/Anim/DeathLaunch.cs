using System;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Runtime.View.Anim
{
    /// <summary>One knock-out's flight, planned once on the Death event (sim space, wall seconds).</summary>
    public struct LaunchPlan
    {
        /// <summary>Unit direction in sim space (the decoded hit direction).</summary>
        public float DirX, DirZ;
        /// <summary>Total ground distance (u) after the wall clamp; 0 = no launch.</summary>
        public float Distance;
        /// <summary>Apex of the first arc (u).</summary>
        public float Apex;
        /// <summary>Tumble about cross(up, dir) over the first arc (degrees): a whole turn, so the Death clip lands upright.</summary>
        public float Tumble;
        /// <summary>Lands over Void: keeps falling after the first arc instead of bouncing.</summary>
        public bool Falls;
        /// <summary>Wall seconds of the first arc (the landing force fires at its end).</summary>
        public float Flight;
    }

    /// <summary>
    /// Death launch (DG-88 view, audit finding 5): an analytic arc, no physics engine. Distance by what dealt the blow
    /// (ForceTable.LaunchDistance: light 0.4, heavy 1.2, critical 2.2, Kaiju melee 3, slam or blast 4), apex 0.4 to 1.2,
    /// one bounce at restitution 0.3, a 0.2 u slide; clamped 0.35 u short of the first Wall, breakable or barricade cell
    /// along the path (grid walk at 0.1 u); over Void it falls. Closed form, so a replay draws the same flight.
    /// </summary>
    public static class DeathLaunch
    {
        public const float Restitution = 0.3f, Slide = 0.2f, Gravity = 30f, WallGap = 0.35f, SlideSeconds = 0.15f;

        public static bool Blocks(byte cell) =>
            cell == Cell.Wall || cell == Cell.BreakableIntact || cell == Cell.BreakableCracked || cell == Cell.Barricade;

        public static LaunchPlan Plan(MapDef map, double x, double z, double dirX, double dirZ, byte tier, int other, float motionScale = 1f)
        {
            var p = new LaunchPlan();
            double len = Math.Sqrt(dirX * dirX + dirZ * dirZ);
            if (len < 1e-6) return p;
            dirX /= len;
            dirZ /= len;
            float dist = ForceTable.LaunchDistance(tier, other);
            float apex = 0.4f + 0.8f * Math.Max(0f, Math.Min(1f, (dist - 0.4f) / 3.6f));
            // Exact grid walk (every cell the line enters, corners included): stop WallGap short of the first blocking cell.
            if (map != null && map.Cells != null)
            {
                int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
                int sx = dirX > 0 ? 1 : -1, sz = dirZ > 0 ? 1 : -1;
                double ax = Math.Abs(dirX), az = Math.Abs(dirZ);
                double tMaxX = ax > 1e-9 ? (dirX > 0 ? cx + 1 - x : x - cx) / ax : double.MaxValue;
                double tMaxZ = az > 1e-9 ? (dirZ > 0 ? cz + 1 - z : z - cz) / az : double.MaxValue;
                double tDx = ax > 1e-9 ? 1.0 / ax : double.MaxValue, tDz = az > 1e-9 ? 1.0 / az : double.MaxValue;
                for (int guard = 0; guard < 64; guard++)
                {
                    double tEnter;
                    if (tMaxX < tMaxZ) { cx += sx; tEnter = tMaxX; tMaxX += tDx; }
                    else { cz += sz; tEnter = tMaxZ; tMaxZ += tDz; }
                    if (tEnter > dist) break;
                    if (cx < 0 || cz < 0 || cx >= map.Width || cz >= map.Height) break;
                    if (Blocks(map.Cells[cz * map.Width + cx]))
                    {
                        dist = (float)Math.Max(0.0, tEnter - WallGap);
                        break;
                    }
                }
                int lx = (int)Math.Floor(x + dirX * dist), lz = (int)Math.Floor(z + dirZ * dist);
                p.Falls = lx < 0 || lz < 0 || lx >= map.Width || lz >= map.Height || map.Cells[lz * map.Width + lx] == Cell.Void;
            }
            p.DirX = (float)dirX;
            p.DirZ = (float)dirZ;
            p.Distance = dist;
            p.Apex = apex * motionScale;
            // A whole turn or none (the body must land upright for the Death clip): reduced motion drops it.
            p.Tumble = dist >= 2.2f && motionScale >= 0.99f ? 360f : 0f;
            p.Flight = 2f * (float)Math.Sqrt(2f * Math.Max(0.05f, apex) / Gravity);
            return p;
        }

        /// <summary>Wall seconds from the death to the end of the slide.</summary>
        public static float Duration(in LaunchPlan p) => p.Flight * (1f + Restitution) + SlideSeconds;

        /// <summary>Ground distance along the direction, height above the ground and tumble angle at t wall seconds.</summary>
        public static void Evaluate(in LaunchPlan p, float t, out float along, out float height, out float tumble)
        {
            along = 0f;
            height = 0f;
            tumble = 0f;
            if (p.Distance <= 0f || t <= 0f) return;
            float ground = Math.Max(0f, p.Distance - Slide);
            float d1 = ground * 0.8f, d2 = ground * 0.2f;
            float t1 = p.Flight, t2 = p.Flight * Restitution;
            if (t < t1)
            {
                float s = t / t1;
                along = d1 * s;
                height = 4f * p.Apex * s * (1f - s);
                tumble = p.Tumble * s;
                return;
            }
            tumble = p.Tumble;
            if (p.Falls)
            {
                // Over the void: no bounce, the body drops out of the world (FxScene's rim spray covers the edge).
                float tf = t - t1;
                along = d1 + d2 * Math.Min(1f, tf / Math.Max(1e-3f, t2));
                height = -0.5f * Gravity * tf * tf;
                return;
            }
            if (t < t1 + t2)
            {
                float s = (t - t1) / t2;
                along = d1 + d2 * s;
                height = 4f * p.Apex * Restitution * Restitution * s * (1f - s);
                return;
            }
            float u = Math.Min(1f, (t - t1 - t2) / SlideSeconds);
            along = ground + (p.Distance - ground) * (1f - (1f - u) * (1f - u));
        }
    }
}
