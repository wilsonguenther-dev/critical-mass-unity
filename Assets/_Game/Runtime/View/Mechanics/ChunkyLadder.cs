using System.Collections.Generic;
using UnityEngine;

namespace CriticalMass.Runtime.View.Mechanics
{
    /// <summary>
    /// b14 polish (owner review: the built ladder read as thin rails): a chunky wooden ladder built in code once per process.
    /// Two thick bevelled rails (octagonal section), five fat rungs, rope lashings round the rail tops and a top cross lash.
    /// The mesh sits in the frame StagedBuildView's A-frame expects of the authored placed ladder: foot at z = 0 (max z), it
    /// leans back to z = -Run while it rises to y = Rise, centred on x. Vertex colours are the CM/Toon art contract (sRGB rgb,
    /// alpha 0 = mask code 0, fixed and banded). About 330 triangles; readable (the staged build cuts it). The ghost copies
    /// carry a flat green or red with a baked top light for CM/FxUnlit (alpha blended).
    /// </summary>
    public static class ChunkyLadder
    {
        public const float Rise = 1.6f, Run = 0.9f, HalfWidth = 0.21f, RailR = 0.062f, RungR = 0.042f, LashR = 0.078f;
        public const int Rungs = 5, Sides = 8;
        public static readonly Color32 Wood = new Color32(0x9A, 0x6A, 0x3A, 0), RungWood = new Color32(0xB9, 0x87, 0x4E, 0),
            Rope = new Color32(0xD9, 0xC0, 0x8A, 0);

        static Mesh _mesh, _ghostOk, _ghostBad;

        /// <summary>The shared ladder mesh (rebuilt only if a previous play session destroyed it).</summary>
        public static Mesh Mesh => _mesh != null ? _mesh : (_mesh = Build());

        /// <summary>The ghost copy: green (will build) or red (refused), alpha 0.45, top faces lighter.</summary>
        public static Mesh Ghost(bool ok)
        {
            if (ok) return _ghostOk != null ? _ghostOk : (_ghostOk = Tinted(Mesh, new Color32(0x5C, 0xE0, 0x7A, 115), "Ghost OK"));
            return _ghostBad != null ? _ghostBad : (_ghostBad = Tinted(Mesh, new Color32(0xF0, 0x55, 0x4A, 115), "Ghost Refused"));
        }

        public static int TriangleCount => Mesh.triangles.Length / 3;

        static void Prism(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t, Vector3 a, Vector3 b, float r, Color32 col)
        {
            Vector3 axis = (b - a).normalized;
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 w = Vector3.Cross(axis, u);
            // sides: each face its own normal (flat facets read as a bevelled square timber at 8 sides)
            for (int i = 0; i < Sides; i++)
            {
                float a0 = (i + 0.5f) * Mathf.PI * 2f / Sides, a1 = (i + 1.5f) * Mathf.PI * 2f / Sides, am = (i + 1f) * Mathf.PI * 2f / Sides;
                Vector3 d0 = u * Mathf.Cos(a0) + w * Mathf.Sin(a0), d1 = u * Mathf.Cos(a1) + w * Mathf.Sin(a1);
                Vector3 nm = (u * Mathf.Cos(am) + w * Mathf.Sin(am)).normalized;
                int s = v.Count;
                v.Add(a + d0 * r); v.Add(a + d1 * r); v.Add(b + d1 * r); v.Add(b + d0 * r);
                for (int k = 0; k < 4; k++) { n.Add(nm); c.Add(col); }
                t.Add(s); t.Add(s + 1); t.Add(s + 2); t.Add(s); t.Add(s + 2); t.Add(s + 3);
            }
            // caps
            for (int e = 0; e < 2; e++)
            {
                Vector3 p = e == 0 ? a : b, nm = e == 0 ? -axis : axis;
                int s = v.Count;
                for (int i = 0; i < Sides; i++)
                {
                    float an = (i + 0.5f) * Mathf.PI * 2f / Sides;
                    v.Add(p + (u * Mathf.Cos(an) + w * Mathf.Sin(an)) * r); n.Add(nm); c.Add(col);
                }
                for (int i = 1; i < Sides - 1; i++)
                {
                    if (e == 0) { t.Add(s); t.Add(s + i + 1); t.Add(s + i); }
                    else { t.Add(s); t.Add(s + i); t.Add(s + i + 1); }
                }
            }
        }

        static Vector3 Rail(float side, float f) => new Vector3(side * HalfWidth, Rise * f, -Run * f);

        static Mesh Build()
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color32>(); var t = new List<int>();
            for (int s = -1; s <= 1; s += 2)
            {
                Prism(v, n, c, t, Rail(s, -0.02f), Rail(s, 1.04f), RailR, Wood);
                // rope lashings: two wraps near the top of each rail, one at the foot
                Prism(v, n, c, t, Rail(s, 0.86f), Rail(s, 0.91f), LashR, Rope);
                Prism(v, n, c, t, Rail(s, 0.94f), Rail(s, 0.985f), LashR, Rope);
                Prism(v, n, c, t, Rail(s, 0.06f), Rail(s, 0.10f), LashR * 0.95f, Rope);
            }
            for (int i = 0; i < Rungs; i++)
            {
                float f = 0.14f + i * (0.80f / (Rungs - 1));
                Vector3 l = Rail(-1, f), r = Rail(1, f);
                // rungs stand proud of the rails a little (they read from above as bars, not lines)
                Prism(v, n, c, t, l - Vector3.right * 0.05f, r + Vector3.right * 0.05f, RungR, RungWood);
            }
            // the top cross lash: a rope bar just over the top rung
            Prism(v, n, c, t, Rail(-1, 0.975f) - Vector3.right * 0.03f, Rail(1, 0.975f) + Vector3.right * 0.03f, 0.03f, Rope);
            var m = new Mesh { name = "Chunky Ladder" };
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        static Mesh Tinted(Mesh src, Color32 col, string name)
        {
            var m = Object.Instantiate(src);
            m.name = "Chunky Ladder " + name;
            Vector3[] nn = src.normals;
            var cs = new Color32[nn.Length];
            for (int i = 0; i < nn.Length; i++)
            {
                float k = 0.72f + 0.28f * Mathf.Clamp01(nn[i].y * 0.5f + 0.5f);
                cs[i] = new Color32((byte)(col.r * k), (byte)(col.g * k), (byte)(col.b * k), col.a);
            }
            m.colors32 = cs;
            return m;
        }
    }
}
