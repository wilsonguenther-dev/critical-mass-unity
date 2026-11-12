using System.Collections.Generic;
using UnityEngine;

namespace CriticalMass.Editor.Art
{
    /// <summary>Import-time mesh channels for the world art (2026-11-12).</summary>
    public static class WorldMeshBake
    {
        /// <summary>
        /// UV3 = the average of the normals of every vertex at the same position (quantised to 0.1 mm), normalised: the
        /// outline direction CM/Toon reads with _OutlineSource 2. Hard-edged kit meshes split normals at every edge, and
        /// extruding along the split normals tears the hull open at the corners; the averaged direction keeps it closed.
        /// </summary>
        public static void SmoothOutlineDirections(Mesh mesh)
        {
            Vector3[] v = mesh.vertices;
            Vector3[] n = mesh.normals;
            if (v.Length == 0 || n.Length != v.Length) return;
            var sum = new Dictionary<Vector3Int, Vector3>();
            var keys = new Vector3Int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                keys[i] = new Vector3Int(Mathf.RoundToInt(v[i].x * 1e4f), Mathf.RoundToInt(v[i].y * 1e4f), Mathf.RoundToInt(v[i].z * 1e4f));
                sum.TryGetValue(keys[i], out Vector3 s);
                sum[keys[i]] = s + n[i];
            }
            var outl = new List<Vector3>(v.Length);
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 d = sum[keys[i]];
                outl.Add(d.sqrMagnitude > 1e-8f ? d.normalized : n[i]);
            }
            mesh.SetUVs(3, outl);
        }

        /// <summary>The waterfall pool's outline (kit2.waterfall "edge"), local metres, z inland.</summary>
        static readonly Vector2[] PoolEdge =
        {
            new Vector2(0.44f, -0.06f), new Vector2(0.44f, 0.42f), new Vector2(0.26f, 0.70f), new Vector2(-0.26f, 0.70f),
            new Vector2(-0.44f, 0.42f), new Vector2(-0.44f, -0.06f),
        };

        /// <summary>
        /// Constant-width foam (kit README v3): the pool foam band is a 0.15 u strip inside the pool edge at y 0.015 in the
        /// FBX (kit geometry is built in Unity space: local x along the lip, +z inland, so the kit's coordinates apply as they are). Its inner vertices
        /// are moved onto the edge and UV2 gets the unit inward direction, so CM/Toon pushes them _ExtrudePx pixels in clip
        /// space: the band holds the same width in pixels across the frame. Other vertices get a zero UV2.
        /// </summary>
        public static void FoamExtrusion(Mesh mesh)
        {
            Vector3[] v = mesh.vertices;
            Color[] c = mesh.colors;
            var dirs = new List<Vector3>(v.Length);
            int moved = 0;
            for (int i = 0; i < v.Length; i++)
            {
                dirs.Add(Vector3.zero);
                if (Mathf.Abs(v[i].y - 0.015f) > 0.0007f) continue;
                if (c.Length == v.Length && c[i].r < 0.85f) continue; // foam is near-white
                var p = new Vector2(v[i].x, v[i].z);
                Vector2 best = p;
                float bestD = float.MaxValue;
                for (int k = 0; k + 1 < PoolEdge.Length; k++)
                {
                    Vector2 a = PoolEdge[k], b = PoolEdge[k + 1], e = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, e) / e.sqrMagnitude);
                    Vector2 q = a + e * t;
                    float d = (p - q).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = q; }
                }
                float dist = Mathf.Sqrt(bestD);
                if (dist < 0.08f || dist > 0.25f) continue; // edge vertices stay; only the inner ring extrudes
                Vector2 dir = (p - best) / dist;
                v[i] = new Vector3(best.x, v[i].y, best.y);
                dirs[i] = new Vector3(dir.x, 0f, dir.y);
                moved++;
            }
            if (moved == 0) return;
            mesh.vertices = v;
            mesh.SetUVs(2, dirs);
            mesh.RecalculateBounds();
        }
    }
}
