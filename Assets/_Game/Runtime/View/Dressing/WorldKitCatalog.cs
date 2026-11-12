using System;
using UnityEngine;

namespace CriticalMass.Runtime.View.Dressing
{
    /// <summary>
    /// Every environment and bases kit model by piece name (the FBX file name), for the world view at run time. Written
    /// by the editor (ArtCatalogBuilder.BuildWorld) to Resources/World/WorldKitCatalog.asset; sorted by name.
    /// </summary>
    public sealed class WorldKitCatalog : ScriptableObject
    {
        public const string ResourcePath = "World/WorldKitCatalog";
        public const string AssetPath = "Assets/_Game/Resources/" + ResourcePath + ".asset";

        public string[] Names = new string[0];
        public GameObject[] Models = new GameObject[0];

        private static WorldKitCatalog _loaded;

        public static WorldKitCatalog Load()
        {
            if (_loaded == null) _loaded = Resources.Load<WorldKitCatalog>(ResourcePath);
            return _loaded;
        }

        [NonSerialized] private System.Collections.Generic.Dictionary<string, Vector3> _centroids;

        /// <summary>Vertex centroid of a kit model in its own frame (zero when missing or not readable).</summary>
        public Vector3 Centroid(string piece)
        {
            _centroids ??= new System.Collections.Generic.Dictionary<string, Vector3>();
            if (_centroids.TryGetValue(piece, out Vector3 c)) return c;
            GameObject model = Find(piece);
            Vector3 sum = Vector3.zero;
            int n = 0;
            if (model != null)
                foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                    Matrix4x4 m = model.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    foreach (Vector3 v in mf.sharedMesh.vertices) { sum += m.MultiplyPoint3x4(v); n++; }
                }
            return _centroids[piece] = n > 0 ? sum / n : Vector3.zero;
        }

        [NonSerialized] private System.Collections.Generic.Dictionary<string, Vector3[]> _bodies;

        /// <summary>A kit model's vertices 0.3 to 2 u above its pivot (the part a unit would walk into), own frame.</summary>
        public Vector3[] Body(string piece)
        {
            _bodies ??= new System.Collections.Generic.Dictionary<string, Vector3[]>();
            if (_bodies.TryGetValue(piece, out Vector3[] b)) return b;
            var list = new System.Collections.Generic.List<Vector3>();
            GameObject model = Find(piece);
            if (model != null)
                foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                    Matrix4x4 m = model.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    foreach (Vector3 v in mf.sharedMesh.vertices)
                    {
                        Vector3 w = m.MultiplyPoint3x4(v);
                        if (w.y > 0.3f && w.y < 2f) list.Add(w);
                    }
                }
            return _bodies[piece] = list.ToArray();
        }

        public GameObject Find(string piece)
        {
            int i = Array.BinarySearch(Names, piece, StringComparer.Ordinal);
            return i >= 0 ? Models[i] : null;
        }
    }
}
