using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Art;
using UnityEditor;
using UnityEngine;

namespace CriticalMass.Editor.Art
{
    /// <summary>
    /// Batch 12 item AB, sharpness switch "normals" (art audit): smoothed lighting normals for the rigged characters.
    /// The HY character art paints one flat colour per triangle, and the per-triangle colour SPLITS every vertex, so the
    /// imported normals are flat per triangle and Unity's "Calculate" normals could not smooth across the splits either.
    /// Here the normals are smoothed BY POSITION: vertices are welded by position (WeldMetres), and each vertex averages
    /// the normals of the faces at its position whose angle to its own normal is under SharpSwitches.SmoothNormalsAngleDeg
    /// (60; corner-angle weighted), so curved forms shade smoothly and hard edges above 60 degrees stay hard.
    /// Runtime-switchable: the imported normal stays in NORMAL and the smoothed one goes into UV3; CM/Toon lerps between
    /// them by _SmoothNormals (ArtMaterials sets it from SharpSwitches.Normals). UV3 is not skinned, so it is stored in the
    /// vertex's own frame (imported normal N, the TANGENT channel's outline direction made orthogonal to N, and N x T),
    /// which skinning does rotate: CMToonCommon.hlsl CMToonSmoothNormalOS rebuilds the frame from the skinned N and T. The
    /// tangent channel itself (WorldMeshBake.SmoothOutlineTangents, written by ArtImportPostprocessor) is not touched,
    /// which is why this runs after it (GetPostprocessOrder). Where T is nearly parallel to N the frame is undefined;
    /// those vertices store (0, 0, 1) and keep the imported normal (logged as "degenerate").
    /// Cost: UV3 float3 = 12 bytes per vertex per character mesh asset (shared by every instance; not skinned).
    /// </summary>
    public sealed class SharpNormalsPostprocessor : AssetPostprocessor
    {
        /// <summary>After ArtImportPostprocessor (default order 0), whose tangents the frame is built on.</summary>
        public const int Order = 100;
        public const float WeldMetres = 1e-4f;
        /// <summary>Length of T made orthogonal to N under which the frame counts as undefined (same value as the shader).</summary>
        public const float DegenerateFrame = 0.05f;

        /// <summary>Bump when the smoothing or the encoding changes: Unity re-imports the character models.</summary>
        public override uint GetVersion() => 1;

        public override int GetPostprocessOrder() => Order;

        private void OnPostprocessModel(GameObject root)
        {
            if (!ArtImportPostprocessor.IsArt(assetPath)) return;
            ModelSpec spec = ArtImportPostprocessor.SpecFor(assetPath);
            if (!ArtMaterials.BakesSmoothNormals(spec)) return;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                Vector3[] v = mesh.vertices, n = mesh.normals;
                Vector4[] t = mesh.tangents;
                if (v.Length == 0 || n.Length != v.Length) continue;
                Vector3[] s = SmoothByPosition(v, n, mesh.triangles, SharpSwitches.SmoothNormalsAngleDeg, out int positions);
                Vector3[] enc = EncodeInFrame(n, t.Length == v.Length ? t : null, s, out int degenerate);
                mesh.SetUVs(3, enc);
                float mean = 0f, max = 0f;
                for (int i = 0; i < v.Length; i++)
                {
                    float a = Vector3.Angle(n[i], s[i]);
                    mean += a;
                    if (a > max) max = a;
                }
                Debug.Log($"[SharpNormals] {assetPath} {r.name}: {v.Length} vertices on {positions} positions, smoothed normal " +
                          $"moved {mean / v.Length:0.0} deg on average (max {max:0.0}), {degenerate} degenerate frames kept imported, " +
                          $"UV3 +{v.Length * 12} bytes");
            }
        }

        static Vector3Int Key(Vector3 p) =>
            new Vector3Int(Mathf.RoundToInt(p.x / WeldMetres), Mathf.RoundToInt(p.y / WeldMetres), Mathf.RoundToInt(p.z / WeldMetres));

        /// <summary>
        /// Smoothed normal per vertex (pure; tests): welded by position, the corner-angle weighted average of the face normals
        /// at the vertex's position that lie within maxAngleDeg of the vertex's own (imported) normal. A vertex no triangle
        /// uses, or with no face inside the angle, keeps its normal. positions = distinct welded positions.
        /// </summary>
        public static Vector3[] SmoothByPosition(Vector3[] v, Vector3[] n, int[] tri, float maxAngleDeg, out int positions)
        {
            int nv = v.Length, nt = tri.Length / 3;
            var keys = new Vector3Int[nv];
            var at = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < nv; i++) keys[i] = Key(v[i]);
            var fn = new Vector3[nt];
            var w = new float[nt * 3];
            for (int f = 0; f < nt; f++)
            {
                Vector3 a = v[tri[3 * f]], b = v[tri[3 * f + 1]], c = v[tri[3 * f + 2]];
                Vector3 cr = Vector3.Cross(b - a, c - a);
                fn[f] = cr.sqrMagnitude > 1e-20f ? cr.normalized : Vector3.zero;
                w[3 * f] = Vector3.Angle(b - a, c - a) * Mathf.Deg2Rad;
                w[3 * f + 1] = Vector3.Angle(c - b, a - b) * Mathf.Deg2Rad;
                w[3 * f + 2] = Vector3.Angle(a - c, b - c) * Mathf.Deg2Rad;
                for (int k = 0; k < 3; k++)
                {
                    Vector3Int key = keys[tri[3 * f + k]];
                    if (!at.TryGetValue(key, out List<int> list)) at[key] = list = new List<int>(6);
                    list.Add(3 * f + k);
                }
            }
            positions = at.Count;
            float cosMax = Mathf.Cos(maxAngleDeg * Mathf.Deg2Rad);
            var outN = new Vector3[nv];
            for (int i = 0; i < nv; i++)
            {
                Vector3 own = n[i].sqrMagnitude > 1e-12f ? n[i].normalized : Vector3.up;
                Vector3 sum = Vector3.zero;
                if (at.TryGetValue(keys[i], out List<int> corners))
                    foreach (int c in corners)
                    {
                        Vector3 f = fn[c / 3];
                        if (f != Vector3.zero && Vector3.Dot(f, own) >= cosMax) sum += f * w[c];
                    }
                outN[i] = sum.sqrMagnitude > 1e-12f ? sum.normalized : own;
            }
            return outN;
        }

        /// <summary>
        /// The smoothed normal s in the vertex frame (pure; tests): x along T made orthogonal to N, y along N x T, z along N.
        /// No tangents, or T nearly parallel to N: (0, 0, 1), which decodes to the imported normal.
        /// </summary>
        public static Vector3[] EncodeInFrame(Vector3[] n, Vector4[] t, Vector3[] s, out int degenerate)
        {
            degenerate = 0;
            var e = new Vector3[n.Length];
            for (int i = 0; i < n.Length; i++)
            {
                if (t == null || !Frame(n[i], t[i], out Vector3 N, out Vector3 T, out Vector3 B))
                {
                    e[i] = new Vector3(0f, 0f, 1f);
                    degenerate++;
                    continue;
                }
                e[i] = new Vector3(Vector3.Dot(s[i], T), Vector3.Dot(s[i], B), Vector3.Dot(s[i], N));
            }
            return e;
        }

        /// <summary>The C# twin of CMToonSmoothNormalOS at _SmoothNormals 1 (tests): the normal a skinned N and T decode to.</summary>
        public static Vector3 DecodeInFrame(Vector3 n, Vector4 t, Vector3 e)
        {
            if (!Frame(n, t, out Vector3 N, out Vector3 T, out Vector3 B)) return n.normalized;
            return (e.x * T + e.y * B + e.z * N).normalized;
        }

        static bool Frame(Vector3 n, Vector4 t, out Vector3 N, out Vector3 T, out Vector3 B)
        {
            N = n.normalized;
            Vector3 tt = new Vector3(t.x, t.y, t.z);
            T = tt - N * Vector3.Dot(N, tt);
            float len = T.magnitude;
            if (len < DegenerateFrame)
            {
                B = Vector3.zero;
                return false;
            }
            T /= len;
            B = Vector3.Cross(N, T);
            return true;
        }
    }
}
