using System;
using System.Collections.Generic;
using UnityEngine;

namespace CriticalMass.Runtime.View
{
    /// <summary>
    /// b15 (WORLD-AUDIT W02): the hill's zone plate as a round disc instead of a flattened cube. Unit size like the cube it
    /// replaces (diameter 1, height 1, centred), so ActorViews keeps scaling it to (2R, thickness, 2R). The rim wobbles by up
    /// to EdgeWobble of the radius from a fixed hash (identical every run), bevelled top edge, flat-shaded sides. One mesh for
    /// the process (built once, shared, never per match).
    /// </summary>
    public static class HillDisc
    {
        public const int Segments = 48;
        public const float EdgeWobble = 0.035f, Bevel = 0.06f;

        static Mesh _mesh;

        public static Mesh Mesh => _mesh != null ? _mesh : (_mesh = Build());

        /// <summary>Rim radius (diameter 1 = radius 0.5) at segment k.</summary>
        public static float Radius(int k)
        {
            uint h = Dressing.WorldDresser.H32(k % Segments, 7, 8811);
            return 0.5f * (1f - EdgeWobble * ((h % 1000u) / 1000f));
        }

        static Mesh Build()
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            float top = 0.5f, bot = -0.5f, bev = top - 0.35f;   // the bevel drops a third of the (scaled-thin) height
            // Top: fan over an inner ring (radius minus Bevel), then the bevel ring down to the rim.
            int c = v.Count;
            v.Add(new Vector3(0, top, 0)); n.Add(Vector3.up);
            for (int k = 0; k < Segments; k++)
            {
                float a = k * Mathf.PI * 2f / Segments, r = Radius(k) - Bevel;
                v.Add(new Vector3(Mathf.Cos(a) * r, top, Mathf.Sin(a) * r)); n.Add(Vector3.up);
            }
            for (int k = 0; k < Segments; k++) { t.Add(c); t.Add(c + 1 + (k + 1) % Segments); t.Add(c + 1 + k); }
            for (int k = 0; k < Segments; k++)
            {
                int k1 = (k + 1) % Segments;
                float a0 = k * Mathf.PI * 2f / Segments, a1 = k1 * Mathf.PI * 2f / Segments;
                Vector3 i0 = new Vector3(Mathf.Cos(a0) * (Radius(k) - Bevel), top, Mathf.Sin(a0) * (Radius(k) - Bevel));
                Vector3 i1 = new Vector3(Mathf.Cos(a1) * (Radius(k1) - Bevel), top, Mathf.Sin(a1) * (Radius(k1) - Bevel));
                Vector3 o0 = new Vector3(Mathf.Cos(a0) * Radius(k), bev, Mathf.Sin(a0) * Radius(k));
                Vector3 o1 = new Vector3(Mathf.Cos(a1) * Radius(k1), bev, Mathf.Sin(a1) * Radius(k1));
                Vector3 b0 = new Vector3(o0.x, bot, o0.z), b1 = new Vector3(o1.x, bot, o1.z);
                Quad(v, n, t, i0, i1, o1, o0, (Vector3.up + Out(a0, a1)).normalized);
                Quad(v, n, t, o0, o1, b1, b0, Out(a0, a1));
            }
            var m = new Mesh { name = "Hill disc" };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        static Vector3 Out(float a0, float a1)
        {
            float a = (a0 + a1) * 0.5f;
            if (a1 < a0) a += Mathf.PI;
            return new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
        }

        static void Quad(List<Vector3> v, List<Vector3> n, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nn)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            n.Add(nn); n.Add(nn); n.Add(nn); n.Add(nn);
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), nn) > 0) { t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3); }
            else { t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2); }
        }
    }
}
