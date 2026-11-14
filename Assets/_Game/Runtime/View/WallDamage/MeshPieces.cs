using System.Collections.Generic;
using UnityEngine;

namespace CriticalMass.Runtime.View.WallDamage
{
    /// <summary>One piece cut from an authored mesh: its own mesh (vertices around Pivot) and where Pivot sits in the source.</summary>
    public sealed class MeshPiece
    {
        public Mesh Mesh;
        /// <summary>The piece centre in the source mesh's space (the piece mesh is drawn at this local position).</summary>
        public Vector3 Pivot;
        /// <summary>Crack overlays for stages 1..3 (index 0..2), in the piece's own space; null when none.</summary>
        public Mesh[] Cracks;
        /// <summary>wall-damage-2: a dark see-through shell just outside the piece (the cracked stage's darker tint); null when none.</summary>
        public Mesh Tint;
        public float Size;
    }

    /// <summary>
    /// wall-damage-1: cuts authored meshes into pieces at load (never per frame): connected islands (a barricade's planks and
    /// posts, a wall's blocks) or the triangles nearest each Sim cell (one breakable wall section into one piece per cell).
    /// Also bakes thin crack strips that lie ON the piece's own faces (they move and tilt with it). Needs readable meshes:
    /// every Try* returns null for a mesh that is not, and the callers keep the authored look.
    /// </summary>
    public static class MeshPieces
    {
        /// <summary>capture-gaps-1: fully opaque and a step darker (was 1B223A at F0), so the stroke holds against shaded stone.</summary>
        public static readonly Color32 CrackColour = new Color32(0x12, 0x16, 0x28, 0xFF);

        static readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>();
        static readonly List<Vector2> UV = new List<Vector2>();
        static readonly List<Color32> C = new List<Color32>();

        /// <summary>Splits src into at most maxPieces pieces by connected islands (vertices welded by position).</summary>
        public static MeshPiece[] TryIslands(Mesh src, int maxPieces, int seed)
        {
            if (src == null || !src.isReadable || src.vertexCount == 0) return null;
            Vector3[] v = src.vertices;
            int n = v.Length;
            int[] parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            var weld = new Dictionary<Vector3Int, int>(n);
            for (int i = 0; i < n; i++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(v[i].x * 2000f), Mathf.RoundToInt(v[i].y * 2000f), Mathf.RoundToInt(v[i].z * 2000f));
                if (weld.TryGetValue(key, out int j)) Union(parent, i, j);
                else weld[key] = i;
            }
            int subs = src.subMeshCount;
            var tris = new int[subs][];
            for (int s = 0; s < subs; s++)
            {
                tris[s] = src.GetTriangles(s);
                int[] t = tris[s];
                for (int k = 0; k + 2 < t.Length; k += 3) { Union(parent, t[k], t[k + 1]); Union(parent, t[k], t[k + 2]); }
            }
            // Island ids, their centres and sizes.
            var ids = new Dictionary<int, int>();
            var centre = new List<Vector3>();
            var count = new List<int>();
            int[] islandOf = new int[n];
            for (int i = 0; i < n; i++)
            {
                int r = Find(parent, i);
                if (!ids.TryGetValue(r, out int id)) { id = ids.Count; ids[r] = id; centre.Add(Vector3.zero); count.Add(0); }
                islandOf[i] = id;
                centre[id] += v[i];
                count[id]++;
            }
            int islands = ids.Count;
            for (int k = 0; k < islands; k++) centre[k] /= Mathf.Max(1, count[k]);
            // Too many islands: bucket them along the mesh's long axis into maxPieces groups.
            int[] groupOf = new int[islands];
            int groups = islands;
            if (islands > maxPieces)
            {
                Vector3 ext = src.bounds.size;
                int axis = ext.x >= ext.y && ext.x >= ext.z ? 0 : ext.y >= ext.z ? 1 : 2;
                var order = new List<int>(islands);
                for (int k = 0; k < islands; k++) order.Add(k);
                order.Sort((a, b) => centre[a][axis].CompareTo(centre[b][axis]));
                for (int r = 0; r < islands; r++) groupOf[order[r]] = r * maxPieces / islands;
                groups = maxPieces;
            }
            else for (int k = 0; k < islands; k++) groupOf[k] = k;
            var pieces = new MeshPiece[groups];
            for (int g = 0; g < groups; g++)
            {
                pieces[g] = Build(src, v, tris, (s, k) => groupOf[islandOf[tris[s][k]]] == g, seed + g * 7);
            }
            return Compact(pieces);
        }

        /// <summary>
        /// Splits a wall-section mesh into one piece per Sim cell: each triangle goes to the cell whose centre is nearest its
        /// centre (toSim maps a source-space point to sim x, z). Pieces keep the section's space (Pivot = zero) so they draw
        /// exactly where the authored section drew. A cell with no triangles gets null.
        /// </summary>
        public static MeshPiece[] TryCells(Mesh src, System.Func<Vector3, Vector2> toSim, IList<int> cells, int width, int seed)
        {
            if (src == null || !src.isReadable || cells == null || cells.Count == 0) return null;
            Vector3[] v = src.vertices;
            int subs = src.subMeshCount;
            var tris = new int[subs][];
            var owner = new int[subs][];
            for (int s = 0; s < subs; s++)
            {
                tris[s] = src.GetTriangles(s);
                int[] t = tris[s];
                owner[s] = new int[t.Length / 3];
                for (int k = 0; k + 2 < t.Length; k += 3)
                {
                    Vector2 p = toSim((v[t[k]] + v[t[k + 1]] + v[t[k + 2]]) / 3f);
                    int best = 0;
                    float bd = float.MaxValue;
                    for (int c = 0; c < cells.Count; c++)
                    {
                        float dx = p.x - (cells[c] % width + 0.5f), dz = p.y - (cells[c] / width + 0.5f);
                        float d = dx * dx + dz * dz;
                        if (d < bd) { bd = d; best = c; }
                    }
                    owner[s][k / 3] = best;
                }
            }
            var pieces = new MeshPiece[cells.Count];
            for (int c = 0; c < cells.Count; c++)
            {
                int cc = c;
                pieces[c] = Build(src, v, tris, (s, k) => owner[s][k / 3] == cc, seed + c * 13, false);
            }
            return pieces;
        }

        /// <summary>
        /// wall-damage-2: cuts a mesh into up to n x n x n chunky pieces by where each triangle's centre falls in its bounds
        /// (a wall block is one welded island, so islands alone would throw it as one slab). Pieces are recentred.
        /// </summary>
        public static MeshPiece[] TryGrid(Mesh src, int n, int seed)
        {
            if (src == null || !src.isReadable || src.vertexCount == 0 || n < 1) return null;
            Vector3[] v = src.vertices;
            Bounds bb = src.bounds;
            Vector3 lo = bb.min, sz = Vector3.Max(bb.size, Vector3.one * 1e-4f);
            int subs = src.subMeshCount;
            var tris = new int[subs][];
            var cellOf = new int[subs][];
            for (int s = 0; s < subs; s++)
            {
                tris[s] = src.GetTriangles(s);
                int[] t = tris[s];
                cellOf[s] = new int[t.Length / 3];
                for (int k = 0; k + 2 < t.Length; k += 3)
                {
                    Vector3 c = (v[t[k]] + v[t[k + 1]] + v[t[k + 2]]) / 3f - lo;
                    int gx = Mathf.Clamp((int)(c.x / sz.x * n), 0, n - 1), gy = Mathf.Clamp((int)(c.y / sz.y * n), 0, n - 1), gz = Mathf.Clamp((int)(c.z / sz.z * n), 0, n - 1);
                    cellOf[s][k / 3] = (gx * n + gy) * n + gz;
                }
            }
            var pieces = new MeshPiece[n * n * n];
            for (int g = 0; g < pieces.Length; g++)
            {
                int gg = g;
                pieces[g] = Build(src, v, tris, (s, k) => cellOf[s][k / 3] == gg, seed + g * 11);
            }
            return Compact(pieces);
        }

        /// <summary>wall-damage-2: a copy of a piece pushed out along its normals, every vertex one colour (drawn with the crack material).</summary>
        public static Mesh TintShell(Mesh piece, Color32 colour, float push)
        {
            if (piece == null || !piece.isReadable || piece.vertexCount == 0) return null;
            Vector3[] v = piece.vertices;
            Vector3[] nrm = piece.normals;
            bool hasN = nrm != null && nrm.Length == v.Length;
            var cols = new Color32[v.Length];
            var uvs = new Vector2[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                if (hasN) v[i] += nrm[i] * push;
                cols[i] = colour;
                uvs[i] = new Vector2(0.5f, 0.5f);
            }
            var m = new Mesh { name = piece.name + " tint" };
            if (v.Length > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = v;
            m.colors32 = cols;
            m.uv = uvs;
            var all = new List<int>();
            for (int s = 0; s < piece.subMeshCount; s++) all.AddRange(piece.GetTriangles(s));
            m.SetTriangles(all, 0, true);
            return m;
        }

        static MeshPiece[] Compact(MeshPiece[] p)
        {
            int n = 0;
            for (int i = 0; i < p.Length; i++) if (p[i] != null) n++;
            if (n == p.Length) return p;
            var o = new MeshPiece[n];
            n = 0;
            for (int i = 0; i < p.Length; i++) if (p[i] != null) o[n++] = p[i];
            return o;
        }

        /// <summary>A piece from the triangles that pass keep(submesh, index of the triangle's first corner).</summary>
        static MeshPiece Build(Mesh src, Vector3[] v, int[][] tris, System.Func<int, int, bool> keep, int seed, bool recentre = true)
        {
            Vector3[] nrm = src.normals;
            Color32[] col = src.colors32;
            Vector2[] uv = src.uv;
            bool hasN = nrm != null && nrm.Length == v.Length, hasC = col != null && col.Length == v.Length, hasUv = uv != null && uv.Length == v.Length;
            var map = new Dictionary<int, int>();
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var cols = new List<Color32>();
            var uvs = new List<Vector2>();
            var outTris = new List<int>[tris.Length];
            int total = 0;
            for (int s = 0; s < tris.Length; s++)
            {
                outTris[s] = new List<int>();
                int[] t = tris[s];
                for (int k = 0; k + 2 < t.Length; k += 3)
                {
                    if (!keep(s, k)) continue;
                    for (int j = 0; j < 3; j++)
                    {
                        int vi = t[k + j];
                        if (!map.TryGetValue(vi, out int ni))
                        {
                            ni = verts.Count;
                            map[vi] = ni;
                            verts.Add(v[vi]);
                            if (hasN) norms.Add(nrm[vi]);
                            if (hasC) cols.Add(col[vi]);
                            if (hasUv) uvs.Add(uv[vi]);
                        }
                        outTris[s].Add(ni);
                    }
                    total++;
                }
            }
            if (total == 0) return null;
            Vector3 pivot = Vector3.zero;
            if (recentre)
            {
                Vector3 lo = verts[0], hi = verts[0];
                for (int i = 1; i < verts.Count; i++) { lo = Vector3.Min(lo, verts[i]); hi = Vector3.Max(hi, verts[i]); }
                pivot = (lo + hi) * 0.5f;
                for (int i = 0; i < verts.Count; i++) verts[i] -= pivot;
            }
            var m = new Mesh { name = src.name + " piece" };
            if (verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts);
            if (hasN) m.SetNormals(norms);
            if (hasC) m.SetColors(cols);
            if (hasUv) m.SetUVs(0, uvs);
            m.subMeshCount = tris.Length;
            for (int s = 0; s < tris.Length; s++) m.SetTriangles(outTris[s], s, false);
            m.RecalculateBounds();
            if (!hasN) m.RecalculateNormals();
            var piece = new MeshPiece { Mesh = m, Pivot = pivot, Size = m.bounds.size.magnitude };
            piece.Cracks = new Mesh[3];
            for (int st = 1; st <= 3; st++) piece.Cracks[st - 1] = CrackMesh(m, seed, st);
            return piece;
        }

        /// <summary>
        /// Crack strips on a piece's own faces for a stage (cumulative: stage 3 holds stage 2's lines and more). Each line starts
        /// on one of the larger faces (picked by seed) and walks 3 short segments that stay inside that face's triangle.
        /// </summary>
        public static Mesh CrackMesh(Mesh piece, int seed, int stage)
        {
            int lines = DamageLook.CrackLines(stage);
            if (piece == null || lines == 0) return null;
            Vector3[] v = piece.vertices;
            int[] t = piece.triangles;
            int triCount = t.Length / 3;
            if (triCount == 0) return null;
            // The larger faces first (cracks on slivers would not read).
            // wall-damage-2: the cracks must read from the high gameplay camera, so they sit mostly on the faces that look up
            // (the camera sees tops first), and they are wide dark strokes, not hairlines.
            var big = new List<int>();
            var up = new List<int>();
            float maxArea = 0f;
            for (int k = 0; k < triCount; k++) maxArea = Mathf.Max(maxArea, Area(v, t, k));
            for (int k = 0; k < triCount; k++)
            {
                if (Area(v, t, k) < maxArea * 0.12f) continue;
                big.Add(k);
                Vector3 fn = Vector3.Cross(v[t[k * 3 + 1]] - v[t[k * 3]], v[t[k * 3 + 2]] - v[t[k * 3]]);
                if (fn.sqrMagnitude > 1e-12f && fn.normalized.y > 0.45f) up.Add(k);
            }
            if (big.Count == 0 || maxArea <= 0f) return null;
            float size = piece.bounds.size.magnitude;
            float seg = Mathf.Clamp(size * 0.2f, DamageLook.CrackSegMin, DamageLook.CrackSegMax), width = Mathf.Clamp(seg * 0.4f, DamageLook.CrackWidthMin, DamageLook.CrackWidthMax), lift = Mathf.Clamp(size * 0.005f, 0.006f, 0.015f);
            V.Clear(); N.Clear(); UV.Clear(); C.Clear();
            var idx = new List<int>();
            for (int l = 0; l < lines; l++)
            {
                List<int> from = up.Count > 0 && l % 3 != 2 ? up : big;
                int k = from[(int)(DamageLook.Hash01(seed + l * 41) * from.Count) % from.Count];
                Vector3 a = v[t[k * 3]], b = v[t[k * 3 + 1]], c = v[t[k * 3 + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-12f) continue;
                n.Normalize();
                float r1 = 0.2f + 0.6f * DamageLook.Hash01(seed + l * 53 + 1), r2 = (1f - r1) * DamageLook.Hash01(seed + l * 59 + 2);
                Vector3 p = a + (b - a) * r1 + (c - a) * r2;
                Vector3 dir = Vector3.ProjectOnPlane(Quaternion.AngleAxis(DamageLook.Hash01(seed + l * 67 + 3) * 360f, n) * (b - a), n).normalized;
                for (int sgi = 0; sgi < 4; sgi++)
                {
                    float turn = (DamageLook.Hash01(seed + l * 71 + sgi * 5) - 0.5f) * 80f;
                    dir = Quaternion.AngleAxis(turn, n) * dir;
                    Vector3 q = p + dir * seg * (0.6f + 0.6f * DamageLook.Hash01(seed + l * 73 + sgi));
                    if (!Inside(a, b, c, n, q))
                    {
                        // Clip the stroke to the face instead of dropping it (short faces still get a visible crack).
                        Vector3 mid = p + (q - p) * 0.5f;
                        if (!Inside(a, b, c, n, mid)) break;
                        q = mid;
                    }
                    Vector3 side = Vector3.Cross(n, dir).normalized * width * (sgi == 0 ? 1f : 0.7f);
                    int bi = V.Count;
                    V.Add(p - side + n * lift); V.Add(p + side + n * lift); V.Add(q + side * 0.5f + n * lift); V.Add(q - side * 0.5f + n * lift);
                    for (int j = 0; j < 4; j++) { N.Add(n); UV.Add(new Vector2(0.5f, 0.5f)); C.Add(CrackColour); }
                    idx.Add(bi); idx.Add(bi + 1); idx.Add(bi + 2); idx.Add(bi); idx.Add(bi + 2); idx.Add(bi + 3);
                    p = q;
                }
            }
            if (idx.Count == 0) return null;
            var m = new Mesh { name = piece.name + " cracks " + stage };
            m.SetVertices(V);
            m.SetNormals(N);
            m.SetUVs(0, UV);
            m.SetColors(C);
            m.SetTriangles(idx, 0, true);
            return m;
        }

        static float Area(Vector3[] v, int[] t, int k) => Vector3.Cross(v[t[k * 3 + 1]] - v[t[k * 3]], v[t[k * 3 + 2]] - v[t[k * 3]]).magnitude * 0.5f;

        static bool Inside(Vector3 a, Vector3 b, Vector3 c, Vector3 n, Vector3 p) =>
            Vector3.Dot(Vector3.Cross(b - a, p - a), n) >= 0f && Vector3.Dot(Vector3.Cross(c - b, p - b), n) >= 0f && Vector3.Dot(Vector3.Cross(a - c, p - c), n) >= 0f;

        static int Find(int[] p, int i)
        {
            while (p[i] != i) { p[i] = p[p[i]]; i = p[i]; }
            return i;
        }

        static void Union(int[] p, int a, int b)
        {
            a = Find(p, a);
            b = Find(p, b);
            if (a != b) p[a < b ? b : a] = a < b ? a : b;
        }

        static Material _crackMat;

        /// <summary>The crack strip material: the affordance shader (unlit, alpha, depth-tested) on a white texture, tinted by vertex colour.</summary>
        public static Material CrackMaterial()
        {
            if (_crackMat != null) return _crackMat;
            Shader s = Shader.Find("CM/Affordance");
            if (s == null) return null;
            _crackMat = new Material(s) { name = "CM Cracks", mainTexture = Texture2D.whiteTexture };
            return _crackMat;
        }
    }
}
