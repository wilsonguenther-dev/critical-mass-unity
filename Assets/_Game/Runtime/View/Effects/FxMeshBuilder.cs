using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>
    /// One dynamic effect mesh rebuilt each frame from preallocated arrays (no per-frame allocation after construction).
    /// Every effect of a layer lands in one mesh, so a layer is one draw call. Shapes are flat toon shapes in vertex
    /// colour: a navy outline copy first, then the fill and its tone steps on top (draw order = index order).
    /// When the vertex budget is full further shapes are dropped (the pools cap first; this is the last guard).
    /// GPU side: the vertex and index buffers are sized ONCE to the budget in the constructor and only their contents and
    /// the sub-mesh index count change per frame (SetVertexBufferData / SetIndexBufferData / SetSubMesh), so a hit or a
    /// frame never creates, grows or frees a GPU buffer.
    /// <para>v2: every vertex carries a second UNorm8x4 attribute (TEXCOORD0); its x is the additive amount. The shader
    /// blends premultiplied (One, OneMinusSrcAlpha), so a vertex with Add = 255 adds its colour (the explosion light pulse)
    /// while Add = 0 alpha-blends as before: glow and toon shapes share one material and one draw call.</para>
    /// </summary>
    public sealed class FxMeshBuilder
    {
        public readonly int MaxVerts;
        public readonly int MaxIndices;
        [StructLayout(LayoutKind.Sequential)]
        private struct Vert
        {
            public Vector3 Pos;
            public Color32 Col;
            public Color32 Ext;
        }

        private const MeshUpdateFlags Quiet = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds |
                                              MeshUpdateFlags.DontResetBoneBounds | MeshUpdateFlags.DontNotifyMeshUsers;
        private readonly Vert[] _v;
        private readonly int[] _idx;
        public int VertCount { get; private set; }
        public int IndexCount { get; private set; }
        /// <summary>Shapes refused because the budget was full (since the last Clear).</summary>
        public int Dropped { get; private set; }
        public readonly Mesh Mesh;
        /// <summary>Additive amount written into every vertex added while set (0 alpha blend, 255 fully additive).</summary>
        public byte Add;
        /// <summary>follow-camera-2: primitives outside the low camera's view or beyond ViewCull.FxDrawDistance are skipped
        /// (on for the combat and skill effect builders; off for ground paint stamps and gameplay markers).</summary>
        public bool ViewCulled;

        public FxMeshBuilder(string name, int maxVerts, int maxIndices)
        {
            MaxVerts = maxVerts;
            MaxIndices = maxIndices;
            _v = new Vert[maxVerts];
            _idx = new int[maxIndices];
            Mesh = new Mesh { name = name };
            Mesh.MarkDynamic();
            Mesh.SetVertexBufferParams(maxVerts,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.UNorm8, 4));
            Mesh.SetIndexBufferParams(maxIndices, IndexFormat.UInt32);
            Mesh.SetVertexBufferData(_v, 0, 0, maxVerts, 0, Quiet);
            Mesh.SetIndexBufferData(_idx, 0, 0, maxIndices, Quiet);
            Mesh.subMeshCount = 1;
            Mesh.SetSubMesh(0, new SubMeshDescriptor(0, 0, MeshTopology.Triangles), Quiet);
            Mesh.bounds = new Bounds(Vector3.zero, new Vector3(10000f, 10000f, 10000f));
        }

        public void Clear()
        {
            VertCount = 0;
            IndexCount = 0;
            Dropped = 0;
            Add = 0;
        }

        public bool Room(int verts, int indices)
        {
            if (VertCount + verts <= MaxVerts && IndexCount + indices <= MaxIndices) return true;
            Dropped++;
            return false;
        }

        public int V(Vector3 p, Color32 c)
        {
            _v[VertCount].Pos = p;
            _v[VertCount].Col = c;
            _v[VertCount].Ext = new Color32(Add, 0, 0, 0);
            return VertCount++;
        }

        public void T(int a, int b, int c)
        {
            _idx[IndexCount++] = a;
            _idx[IndexCount++] = b;
            _idx[IndexCount++] = c;
        }

        /// <summary>
        /// Copies this frame's vertices and indices into the fixed GPU buffers and draws only IndexCount indices
        /// (huge bounds: effects are never culled per shape). No buffer is (re)allocated.
        /// </summary>
        public void Upload()
        {
            if (VertCount > 0) Mesh.SetVertexBufferData(_v, 0, 0, VertCount, 0, Quiet);
            if (IndexCount > 0) Mesh.SetIndexBufferData(_idx, 0, 0, IndexCount, Quiet);
            Mesh.SetSubMesh(0, new SubMeshDescriptor(0, IndexCount, MeshTopology.Triangles), Quiet);
        }

        // ---- shapes (all flat; "right" and "up" span the shape's plane) ----

        /// <summary>A filled polygon (fan) of n points at radius r(k) given by radii[] (or r when radii is null).</summary>
        public void Blob(Vector3 c, Vector3 right, Vector3 up, float r, float[] radii, int n, float rot, Color32 col)
        {
            if (ViewCulled && !CriticalMass.Runtime.Camera.ViewCull.FxVisible(c, r)) return;
            if (!Room(n + 1, n * 3)) return;
            int centre = V(c, col);
            for (int k = 0; k < n; k++)
            {
                float a = rot + k * Mathf.PI * 2f / n;
                float rr = r * (radii != null ? radii[k % radii.Length] : 1f);
                V(c + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * rr, col);
            }
            for (int k = 0; k < n; k++) T(centre, centre + 1 + k, centre + 1 + (k + 1) % n);
        }

        /// <summary>A star with `points` spikes: outer radius r, inner radius r * inner.</summary>
        public void Star(Vector3 c, Vector3 right, Vector3 up, float r, float inner, int points, float rot, Color32 col)
        {
            if (ViewCulled && !CriticalMass.Runtime.Camera.ViewCull.FxVisible(c, r)) return;
            int n = points * 2;
            if (!Room(n + 1, n * 3)) return;
            int centre = V(c, col);
            for (int k = 0; k < n; k++)
            {
                float a = rot + k * Mathf.PI / points;
                float rr = (k & 1) == 0 ? r : r * inner;
                V(c + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * rr, col);
            }
            for (int k = 0; k < n; k++) T(centre, centre + 1 + k, centre + 1 + (k + 1) % n);
        }

        /// <summary>An annulus (or an arc of one) from radius r0 to r1, between angles a0 and a1, in `seg` segments.</summary>
        public void Ring(Vector3 c, Vector3 right, Vector3 up, float r0, float r1, float a0, float a1, int seg, Color32 col)
        {
            Ring(c, right, up, r0, r1, a0, a1, seg, col, col);
        }

        /// <summary>Annulus with an inner and an outer colour (alpha ramps for trails).</summary>
        public void Ring(Vector3 c, Vector3 right, Vector3 up, float r0, float r1, float a0, float a1, int seg, Color32 inner, Color32 outer)
        {
            if (ViewCulled && !CriticalMass.Runtime.Camera.ViewCull.FxVisible(c, r1)) return;
            if (seg < 1 || r1 <= r0) return;
            if (!Room((seg + 1) * 2, seg * 6)) return;
            int start = VertCount;
            for (int k = 0; k <= seg; k++)
            {
                float a = Mathf.Lerp(a0, a1, (float)k / seg);
                Vector3 d = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                V(c + d * r0, inner);
                V(c + d * r1, outer);
            }
            for (int k = 0; k < seg; k++)
            {
                int i0 = start + k * 2;
                T(i0, i0 + 1, i0 + 3);
                T(i0, i0 + 3, i0 + 2);
            }
        }

        /// <summary>A crescent (swing trail): the band's half-width tapers from 0 at a0 to `w` at a1 (the blade end).</summary>
        public void Crescent(Vector3 c, Vector3 right, Vector3 up, float r, float w, float a0, float a1, int seg, Color32 tail, Color32 head)
        {
            if (ViewCulled && !CriticalMass.Runtime.Camera.ViewCull.FxVisible(c, r + w)) return;
            if (seg < 1) return;
            if (!Room((seg + 1) * 2, seg * 6)) return;
            int start = VertCount;
            for (int k = 0; k <= seg; k++)
            {
                float t = (float)k / seg;
                float a = Mathf.Lerp(a0, a1, t);
                float hw = w * Mathf.Sqrt(t);
                Vector3 d = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                Color32 col = Color32.Lerp(tail, head, t);
                V(c + d * (r - hw), col);
                V(c + d * (r + hw), col);
            }
            for (int k = 0; k < seg; k++)
            {
                int i0 = start + k * 2;
                T(i0, i0 + 1, i0 + 3);
                T(i0, i0 + 3, i0 + 2);
            }
        }

        /// <summary>A quad from a centre, half extents along two axes.</summary>
        public void Quad(Vector3 c, Vector3 halfRight, Vector3 halfUp, Color32 bottom, Color32 top)
        {
            if (ViewCulled && !CriticalMass.Runtime.Camera.ViewCull.FxVisible(c, Mathf.Max(halfRight.magnitude, halfUp.magnitude))) return;
            if (!Room(4, 6)) return;
            int s = V(c - halfRight - halfUp, bottom);
            V(c + halfRight - halfUp, bottom);
            V(c + halfRight + halfUp, top);
            V(c - halfRight + halfUp, top);
            T(s, s + 1, s + 2);
            T(s, s + 2, s + 3);
        }

        private static readonly int[] CubeFaces =
        {
            // +Y, -Y, +X, -X, +Z, -Z as corner indices (counter-clockwise seen from outside)
            // (Unity front faces wind clockwise: cross(b - a, c - a) is the outward normal for each quad below.)
            2, 3, 7, 6,   0, 1, 5, 4,   1, 2, 6, 5,   0, 4, 7, 3,   4, 5, 6, 7,   0, 3, 2, 1,
        };

        /// <summary>
        /// A toon cube chunk: an inverted, enlarged navy hull (the outline) then the fill with three flat tone steps baked
        /// per face (top light, sides mid, underside shadow). Needs a Cull Back material.
        /// </summary>
        public void Chunk(Vector3 c, Quaternion rot, Vector3 half, float outline, Color32 light, Color32 mid, Color32 dark, Color32 navy)
        {
            if (ViewCulled && !CriticalMass.Runtime.Camera.ViewCull.FxVisible(c, half.magnitude + outline)) return;
            if (!Room(48, 72)) return;
            AddBox(c, rot, half + Vector3.one * outline, navy, navy, navy, true);
            AddBox(c, rot, half, light, mid, dark, false);
        }

        /// <summary>
        /// A cartoon bone (gib): a shaft and two knuckle knobs, each an outlined toon box (144 vertices). `half` is the
        /// shaft half length along the rotated x axis, `thick` the shaft half thickness.
        /// </summary>
        public void Bone(Vector3 c, Quaternion rot, float half, float thick, float outline, Color32 light, Color32 mid, Color32 dark, Color32 navy)
        {
            if (ViewCulled && !CriticalMass.Runtime.Camera.ViewCull.FxVisible(c, half + thick + outline)) return;
            if (!Room(144, 216)) return;
            Vector3 ax = rot * Vector3.right;
            var shaft = new Vector3(half, thick, thick);
            var knob = new Vector3(thick * 1.25f, thick * 1.7f, thick * 1.7f);
            AddBox(c, rot, shaft + Vector3.one * outline, navy, navy, navy, true);
            AddBox(c + ax * half, rot, knob + Vector3.one * outline, navy, navy, navy, true);
            AddBox(c - ax * half, rot, knob + Vector3.one * outline, navy, navy, navy, true);
            AddBox(c, rot, shaft, light, mid, dark, false);
            AddBox(c + ax * half, rot, knob, light, mid, dark, false);
            AddBox(c - ax * half, rot, knob, light, mid, dark, false);
        }

        private readonly Vector3[] _corner = new Vector3[8];

        private void AddBox(Vector3 c, Quaternion rot, Vector3 h, Color32 top, Color32 side, Color32 bottom, bool inverted)
        {
            for (int i = 0; i < 8; i++)
            {
                var o = new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
                _corner[i] = c + rot * o;
            }
            // corner layout: bit0 x, bit1 y, bit2 z -> remap the face table (written for 0..7 = x-y-z cube order below)
            for (int f = 0; f < 6; f++)
            {
                Color32 col = f == 0 ? top : f == 1 ? bottom : side;
                int s = VertCount;
                for (int k = 0; k < 4; k++) V(_corner[Remap[CubeFaces[f * 4 + k]]], col);
                if (inverted) { T(s, s + 2, s + 1); T(s, s + 3, s + 2); }
                else { T(s, s + 1, s + 2); T(s, s + 2, s + 3); }
            }
        }

        // Face table corner ids (0 = (-,-,-), 1 = (+,-,-), 2 = (+,+,-), 3 = (-,+,-), 4 = (-,-,+), 5 = (+,-,+), 6 = (+,+,+), 7 = (-,+,+))
        // mapped to the bit layout used by _corner.
        private static readonly int[] Remap = { 0, 1, 3, 2, 4, 5, 7, 6 };
    }
}
