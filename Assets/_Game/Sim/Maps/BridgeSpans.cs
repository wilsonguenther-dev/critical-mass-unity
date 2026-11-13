using System;
using System.Collections.Generic;

namespace CriticalMass.Sim.Maps
{
    /// <summary>
    /// DG-02 (port of the browser's CM-33 bridge fix, branch cm/CM-33 at 003c970, map.ts, D-069): one straight bridge span
    /// in world units (a Bridges row, or its mirror x' = MirrorX - x), with its unit axis (Ux, Uz), length, end heights
    /// (the raster's lerp ends) and whether it is diagonal (neither axis-aligned). The cell raster is unchanged, so bot
    /// fields, Kaiju clearance and every cell rule see the same grid as before; Collide walks DIAGONAL spans against this
    /// analytic segment instead of the cell staircase, so a diagonal bridge is as wide to walk as its drawn deck.
    /// </summary>
    public sealed class BridgeSpan
    {
        public double X0;
        public double Z0;
        public double X1;
        public double Z1;
        public double Ux;
        public double Uz;
        public double Len;
        public double H0;
        public double H1;
        public bool Diagonal;
        /// <summary>Metadata only (no rule reads it): the SkybreakBasinMap.Bridges row this span comes from (0 = B1).</summary>
        public int Row;
        /// <summary>Metadata only: true for the mirrored copy (x' = MirrorX - x) of that row.</summary>
        public bool Mirror;
    }

    /// <summary>The span table and its geometry helpers (browser map.ts on cm/CM-33). Pure functions of the map table.</summary>
    public static class BridgeSpans
    {
        /// <summary>Every painted span in table order: each Bridges row, then its mirror when Mirror is true (16 spans).</summary>
        public static readonly IReadOnlyList<BridgeSpan> All = Build();

        /// <summary>The diagonal spans only, in table order (the ones Collide walks as a rail).</summary>
        public static readonly IReadOnlyList<BridgeSpan> Diagonal = Filter(All);

        private static BridgeSpan MakeSpan(double x0, double z0, double x1, double z1, double h0, double h1)
        {
            double len = Hypot(x1 - x0, z1 - z0);
            double ux = len > 0 ? (x1 - x0) / len : 1;
            double uz = len > 0 ? (z1 - z0) / len : 0;
            return new BridgeSpan
            {
                X0 = x0, Z0 = z0, X1 = x1, Z1 = z1, Ux = ux, Uz = uz, Len = len, H0 = h0, H1 = h1,
                Diagonal = len > 0 && x1 != x0 && z1 != z0,
            };
        }

        private static IReadOnlyList<BridgeSpan> Build()
        {
            var output = new List<BridgeSpan>();
            IReadOnlyList<SkybreakBasinMap.Bridge> rows = SkybreakBasinMap.Bridges;
            for (int k = 0; k < rows.Count; k++)
            {
                SkybreakBasinMap.Bridge b = rows[k];
                double h0 = SkybreakBasinMap.EndpointHeight(b.X0, b.Z0);
                double h1 = SkybreakBasinMap.EndpointHeight(b.X1, b.Z1);
                BridgeSpan own = MakeSpan(b.X0, b.Z0, b.X1, b.Z1, h0, h1);
                own.Row = k;
                output.Add(own);
                if (!b.Mirror) continue;
                BridgeSpan mirror = MakeSpan(SkybreakBasinMap.MirrorX - b.X0, b.Z0, SkybreakBasinMap.MirrorX - b.X1, b.Z1, h0, h1);
                mirror.Row = k;
                mirror.Mirror = true;
                output.Add(mirror);
            }
            return Array.AsReadOnly(output.ToArray());
        }

        /// <summary>
        /// The map's name for span index i of All: "B3" for the Bridges row B3, "B3 mirror" for its Crimson-side copy (the
        /// index is NOT the bridge number: All[4] is B3, because every mirrored row adds two spans).
        /// </summary>
        public static string Name(int i)
        {
            BridgeSpan s = All[i];
            return "B" + (s.Row + 1) + (s.Mirror ? " mirror" : "");
        }

        private static IReadOnlyList<BridgeSpan> Filter(IReadOnlyList<BridgeSpan> all)
        {
            var output = new List<BridgeSpan>();
            for (int k = 0; k < all.Count; k++) if (all[k].Diagonal) output.Add(all[k]);
            return Array.AsReadOnly(output.ToArray());
        }

        /// <summary>
        /// sqrt(x^2 + z^2). The browser uses Math.hypot; the port uses the correctly rounded Math.Sqrt of the same sum
        /// (deterministic on every machine), which may differ from V8's hypot by an ulp, far inside the 1e-4 oracle tolerance.
        /// </summary>
        public static double Hypot(double x, double z)
        {
            return Math.Sqrt(x * x + z * z);
        }

        /// <summary>Along-axis coordinate of (x, z) on span s, in world units from (X0, Z0) (unclamped; 0..Len is the span).</summary>
        public static double Along(BridgeSpan s, double x, double z)
        {
            return (x - s.X0) * s.Ux + (z - s.Z0) * s.Uz;
        }

        /// <summary>Signed lateral offset of (x, z) from span s's axis line (+ = toward the normal (-Uz, Ux)).</summary>
        public static double Lateral(BridgeSpan s, double x, double z)
        {
            return (z - s.Z0) * s.Ux - (x - s.X0) * s.Uz;
        }

        /// <summary>Distance from (x, z) to span s's centre SEGMENT (along clamped to 0..Len, so the ends are round).</summary>
        public static double Distance(BridgeSpan s, double x, double z)
        {
            double a = Along(s, x, z);
            if (a < 0) a = 0;
            else if (a > s.Len) a = s.Len;
            return Hypot(s.X0 + s.Ux * a - x, s.Z0 + s.Uz * a - z);
        }

        /// <summary>
        /// Deck-top height at (x, z) when the point lies within BridgeHalfWidth of a span segment (first span in table
        /// order), else NaN: the lerp the raster gives a BRIDGE cell (H0 + (H1 - H0) t, t clamped), at the point. The view
        /// may stand bodies on it over a VOID notch of a diagonal staircase.
        /// </summary>
        public static double DeckHeightAt(double x, double z)
        {
            for (int k = 0; k < All.Count; k++)
            {
                BridgeSpan s = All[k];
                if (Distance(s, x, z) > SkybreakBasinMap.BridgeHalfWidth) continue;
                double t = Along(s, x, z) / s.Len;
                if (t < 0) t = 0;
                else if (t > 1) t = 1;
                return s.H0 + (s.H1 - s.H0) * t;
            }
            return double.NaN;
        }
    }
}
