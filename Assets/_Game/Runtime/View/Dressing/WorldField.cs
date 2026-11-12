using System;
using System.Collections.Generic;
using UnityEngine;

namespace CriticalMass.Runtime.View.Dressing
{
    /// <summary>
    /// The ground shader's field texture (environment kit v3 README section 8, layout2.fields): 4 texels per unit, RGBA
    /// half, row 0 on the sim z0 side. R = path distance (u), G = feature distance (u, to rim and ledge edges and to the
    /// hub disc circles; capped at 12 u, the macro bands only need it near features), B = dirt mask 0..1 (Biomass and
    /// Corruption sites, 0.4 u soft edge), A = ledge shadow 0..1 (0.5 u band on the lower ground in front of each ledge).
    /// Deterministic; rebaked when the grid changes.
    /// </summary>
    public static class WorldField
    {
        public const int TexelsPerUnit = 4;
        const double GCap = 12.0;
        const double PathW = 1.9;

        public static Color[] Compute(WorldPlan plan, out int cols, out int rows)
        {
            cols = plan.Width * TexelsPerUnit;
            rows = plan.Height * TexelsPerUnit;
            var px = new Color[cols * rows];
            // Feature segments (rim + ledge edges), bucketed on a 4 u grid.
            const int B = 4;
            int bw = plan.Width / B + 1, bh = plan.Height / B + 1;
            var buckets = new List<WorldEdge>[bw * bh];
            void Add(WorldEdge e)
            {
                int x0 = Mathf.Clamp((int)(Math.Min(e.Px, e.Qx) / B), 0, bw - 1), x1 = Mathf.Clamp((int)(Math.Max(e.Px, e.Qx) / B), 0, bw - 1);
                int z0 = Mathf.Clamp((int)(Math.Min(e.Pz, e.Qz) / B), 0, bh - 1), z1 = Mathf.Clamp((int)(Math.Max(e.Pz, e.Qz) / B), 0, bh - 1);
                for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    (buckets[z * bw + x] ??= new List<WorldEdge>()).Add(e);
            }
            foreach (WorldEdge e in plan.Rim) Add(e);
            foreach (WorldEdge e in plan.Ledge) Add(e);
            int reach = (int)Math.Ceiling(GCap / B);
            var ledgeSet = new HashSet<WorldEdge>(plan.Ledge);
            for (int j = 0; j < rows; j++)
            {
                double z = (j + 0.5) / TexelsPerUnit;
                for (int i = 0; i < cols; i++)
                {
                    double x = (i + 0.5) / TexelsPerUnit;
                    double r = WorldDresser.PathDist(plan, x, z);
                    double g = GCap;
                    int bx = Mathf.Clamp((int)(x / B), 0, bw - 1), bz = Mathf.Clamp((int)(z / B), 0, bh - 1);
                    for (int zz = Math.Max(0, bz - reach); zz <= Math.Min(bh - 1, bz + reach); zz++)
                    for (int xx = Math.Max(0, bx - reach); xx <= Math.Min(bw - 1, bx + reach); xx++)
                    {
                        List<WorldEdge> l = buckets[zz * bw + xx];
                        if (l == null) continue;
                        foreach (WorldEdge e in l) g = Math.Min(g, WorldDresser.SegDist(x, z, e.Px, e.Pz, e.Qx, e.Qz));
                    }
                    foreach (double[] c in plan.PathDiscs)
                        g = Math.Min(g, Math.Abs(Math.Sqrt((x - c[0]) * (x - c[0]) + (z - c[1]) * (z - c[1])) - c[2]));
                    double b = 0;
                    foreach (double[] s in plan.Sites)
                    {
                        double d = Math.Sqrt((x - s[0]) * (x - s[0]) + (z - s[1]) * (z - s[1]));
                        b = Math.Max(b, 1 - Math.Max(0, Math.Min(1, (d - (s[2] - 0.2)) / 0.4)));
                    }
                    double a = 0;
                    List<WorldEdge> near = buckets[bz * bw + bx];
                    if (near != null)
                        foreach (WorldEdge e in near)
                        {
                            if (!ledgeSet.Contains(e)) continue;
                            double dx = e.Qx - e.Px, dz = e.Qz - e.Pz;
                            double t = ((x - e.Px) * dx + (z - e.Pz) * dz) / (dx * dx + dz * dz);
                            double sN = (x - e.Px) * e.Nx + (z - e.Pz) * e.Nz;
                            if (t >= -0.05 && t <= 1.05 && sN >= 0 && sN <= 0.5) a = Math.Max(a, 1 - sN / 0.5);
                        }
                    px[j * cols + i] = new Color((float)r, (float)g, (float)b, (float)a);
                }
            }
            return px;
        }

        public static Texture2D Bake(WorldPlan plan)
        {
            Color[] px = Compute(plan, out int cols, out int rows);
            var tex = new Texture2D(cols, rows, TextureFormat.RGBAHalf, false, true)
            {
                name = "WorldField", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
