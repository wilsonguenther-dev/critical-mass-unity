using System;
using UnityEngine;

namespace CriticalMass.Runtime.View.Art
{
    /// <summary>
    /// Measures which way a model's art faces, in the model root's own frame, so a test can prove the face is on the side
    /// the view turns toward the sim facing (the root's +Z: ActorViews sets localRotation = Euler(0, YawDeg(facing), 0),
    /// and ModelView.SetFacing adds ModelSpec.ArtYawDeg). Two independent signals:
    ///   face: the centroid of the painted face decal's vertices (FaceSlot submesh, or UV0 texels the decal paints)
    ///         relative to the head bone, on the ground plane;
    ///   toes: foot bone to toes bone, averaged over both feet (toes are in front of the ankle).
    /// Yaw is degrees from +Z toward +X: 0 = the face leads, 180 = the back leads. Editor and test use only.
    /// </summary>
    public static class ModelFacing
    {
        public struct Reading
        {
            public bool HasFace, HasToes;
            public float FaceYaw, ToesYaw;
            public int FaceVerts;
            public override string ToString() =>
                (HasFace ? "face " + FaceYaw.ToString("0") + " deg (" + FaceVerts + " verts)" : "no face") + ", " +
                (HasToes ? "toes " + ToesYaw.ToString("0") + " deg" : "no toes");
        }

        /// <summary>Signed angle in (-180, 180].</summary>
        public static float Wrap(float deg)
        {
            deg %= 360f;
            if (deg > 180f) deg -= 360f;
            if (deg <= -180f) deg += 360f;
            return deg;
        }

        static float YawOf(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

        static Transform FindDeep(Transform t, string name)
        {
            if (string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase)) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform f = FindDeep(t.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }

        /// <summary>
        /// Measure an instantiated model (its root is put at the origin with no rotation first). Pose it before calling
        /// (AnimationClip.SampleAnimation) to measure what the game shows. decalAlpha maps a UV0 to the decal's alpha
        /// (null when the model has no mixed decal).
        /// </summary>
        public static Reading Measure(GameObject instance, ModelSpec spec, Func<Vector2, float> decalAlpha)
        {
            var r = new Reading();
            Transform root = instance.transform;
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            Matrix4x4 toRoot = root.worldToLocalMatrix;

            // Toes.
            Vector3 sum = Vector3.zero;
            int feet = 0;
            foreach (string side in new[] { "l", "r" })
            {
                Transform foot = FindDeep(root, "foot." + side), toes = FindDeep(root, "toes." + side);
                if (foot == null || toes == null) continue;
                Vector3 d = toRoot.MultiplyVector(toes.position - foot.position);
                d.y = 0f;
                if (d.sqrMagnitude < 1e-8f) continue;
                sum += d.normalized;
                feet++;
            }
            if (feet > 0 && sum.sqrMagnitude > 1e-6f) { r.HasToes = true; r.ToesYaw = YawOf(sum); }

            // Face decal.
            if (spec == null || spec.FaceTexture == null || spec.Meshes == null || spec.Meshes.Length == 0) return r;
            Transform bodyT = FindDeep(root, spec.Meshes[0]);
            if (bodyT == null) return r;
            Mesh mesh = null;
            bool baked = false;
            var smr = bodyT.GetComponent<SkinnedMeshRenderer>();
            if (smr != null)
            {
                mesh = new Mesh();
                smr.BakeMesh(mesh, true);
                baked = true;
            }
            else
            {
                var mf = bodyT.GetComponent<MeshFilter>();
                if (mf != null) mesh = mf.sharedMesh;
            }
            if (mesh == null) return r;
            try
            {
                Vector3[] v = mesh.vertices;
                Vector2[] uv = mesh.uv;
                bool[] isFace = new bool[v.Length];
                if (spec.FaceSlot >= 0 && spec.FaceSlot < mesh.subMeshCount)
                {
                    foreach (int i in mesh.GetTriangles(spec.FaceSlot)) isFace[i] = true;
                }
                else if (decalAlpha != null && uv != null && uv.Length == v.Length)
                {
                    for (int i = 0; i < v.Length; i++) isFace[i] = decalAlpha(uv[i]) > 0.5f;
                }
                // Baked vertices are in the renderer's space without its scale applied twice: position and rotation only.
                Matrix4x4 m = baked ? Matrix4x4.TRS(bodyT.position, bodyT.rotation, Vector3.one) : bodyT.localToWorldMatrix;
                Vector3 c = Vector3.zero, all = Vector3.zero;
                int n = 0;
                for (int i = 0; i < v.Length; i++)
                {
                    Vector3 p = toRoot.MultiplyPoint3x4(m.MultiplyPoint3x4(v[i]));
                    all += p;
                    if (!isFace[i]) continue;
                    c += p;
                    n++;
                }
                if (n == 0) return r;
                c /= n;
                Transform head = FindDeep(root, "head");
                Vector3 refP = head != null ? toRoot.MultiplyPoint3x4(head.position) : all / v.Length;
                Vector3 d = c - refP;
                d.y = 0f;
                if (d.sqrMagnitude < 1e-8f) return r;
                r.HasFace = true;
                r.FaceYaw = YawOf(d);
                r.FaceVerts = n;
            }
            finally
            {
                if (baked) UnityEngine.Object.DestroyImmediate(mesh);
            }
            return r;
        }
    }
}
