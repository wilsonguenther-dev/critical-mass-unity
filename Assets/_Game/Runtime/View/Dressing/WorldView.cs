using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.View.Dressing
{
    /// <summary>
    /// The dressed world (2026-11-12): builds the WorldDresser plan from the running Sim's map and the bases layouts,
    /// and draws it as one combined mesh per 16 x 16 cell chunk per look material (sky and clouds in one group), so the
    /// thousands of kit pieces cost a few hundred draws. Read-only on the Sim: it reads World.Map and GridVersion and never
    /// writes. The grey-box GridChunkMeshView keeps drawing every cell this view does not cover (Draws), and any covered
    /// cell whose type changed since the plan; ground chunks re-mesh when cells dig or place; breakable wall pieces swap
    /// intact / cracked / destroyed from their cells. Without the kit catalog (art not imported) it draws nothing and the
    /// grey box stays whole.
    /// </summary>
    public sealed class WorldView : MonoBehaviour
    {
        public const int ChunkSize = 16;

        private static bool[] _dressed;
        private static byte[] _dressedTypes;

        /// <summary>Bumped whenever the covered-cell mask changes (the grey-box view rebuilds its chunks).</summary>
        public static int DressVersion { get; private set; }

        /// <summary>True when the dressed world draws cell i of its current type.</summary>
        public static bool Draws(int i, byte currentType) =>
            _dressed != null && i >= 0 && i < _dressed.Length && _dressed[i] && _dressedTypes[i] == currentType;

        /// <summary>The plan the view drew (tests and the capture read it).</summary>
        public WorldPlan Plan { get; private set; }
        public int Triangles { get; private set; }
        public int Renderers { get; private set; }

        private SimDriverBehaviour _driver;
        private Sim.Core.World _world;
        private MapDef _map;
        private int _seenGridVersion;
        private byte[] _cells;
        private Transform _root;
        private WorldKitCatalog _catalog;
        private readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();
        private readonly Dictionary<Mesh, MeshData> _meshData = new Dictionary<Mesh, MeshData>();
        private readonly Dictionary<int, GameObject> _groundChunks = new Dictionary<int, GameObject>();
        private readonly List<(WorldBreakable b, MeshFilter mf, string state)> _breakables = new List<(WorldBreakable, MeshFilter, string)>();
        private Material _ground;
        private Texture2D _field;

        private void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            if (_driver == null) _driver = FindAnyObjectByType<SimDriverBehaviour>();
        }

        private void LateUpdate()
        {
            if (_driver == null || _driver.World == null) return;
            Sim.Core.World w = _driver.World;
            if (w != _world || w.Map != _map)
            {
                Build(w);
                return;
            }
            if (Plan == null || w.GridVersion == _seenGridVersion) return;
            _seenGridVersion = w.GridVersion;
            var dirtyChunks = new HashSet<int>();
            bool breakables = false;
            byte[] cells = w.Map.Cells;
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] == _cells[i]) continue;
                bool wasLand = WorldDresser.Solid(_cells[i]), isLand = WorldDresser.Solid(cells[i]);
                _cells[i] = cells[i];
                if (wasLand != isLand) dirtyChunks.Add(ChunkOf(i % _map.Width + 0.5, i / _map.Width + 0.5));
                breakables = true;
            }
            foreach (int c in dirtyChunks) BuildGroundChunk(c);
            if (breakables) SyncBreakables();
        }

        private void OnDestroy()
        {
            Clear();
            if (_field != null) Destroy(_field);
        }

        private void Clear()
        {
            if (_root != null) Destroy(_root.gameObject);
            _root = null;
            _groundChunks.Clear();
            _breakables.Clear();
            _dressed = null;
            _dressedTypes = null;
            DressVersion++;
        }

        private int ChunkOf(double simX, double simZ)
        {
            int cx = Mathf.Clamp((int)Math.Floor(simX) / ChunkSize, 0, 63), cz = Mathf.Clamp((int)Math.Floor(simZ) / ChunkSize, 0, 63);
            return cz * 64 + cx;
        }

        private void Build(Sim.Core.World w)
        {
            Clear();
            _world = w;
            _map = w.Map;
            _seenGridVersion = w.GridVersion;
            _cells = (byte[])_map.Cells.Clone();
            _catalog = WorldKitCatalog.Load();
            if (_catalog == null || _catalog.Names.Length == 0) return;
            Plan = WorldDresser.Plan(_map, BasesFile.Load(), _catalog.Centroid, _catalog.Body);
            _root = new GameObject("World Art").transform;
            _root.SetParent(transform, false);
            if (_field != null) Destroy(_field);
            _field = WorldField.Bake(Plan);
            Triangles = 0;
            Renderers = 0;

            var groups = new Dictionary<(int chunk, Material mat), List<(MeshData d, int sub, Matrix4x4 m)>>();
            foreach (WorldPlacement p in Plan.Placements)
            {
                if (p.Piece == "env_ground_cell") continue; // drawn per chunk by BuildGroundChunk
                GameObject model = _catalog.Find(p.Piece);
                if (model == null) continue;
                int chunk = p.Cat == "sky" ? -1 : ChunkOf(p.SimX, p.SimZ);
                bool layout = p.Piece.StartsWith("cm_layout_", StringComparison.Ordinal);
                foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (layout && !mf.name.Contains("cm_district_")) continue; // listed items are placed on their own
                    MeshData d = Data(mf.sharedMesh);
                    if (d == null) continue;
                    var r = mf.GetComponent<MeshRenderer>();
                    Material[] mats = r != null ? r.sharedMaterials : new Material[0];
                    Matrix4x4 local = model.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    Matrix4x4 m = p.Matrix * local;
                    int pieceChunk = chunk;
                    if (layout)
                    {
                        Vector3 c = m.MultiplyPoint3x4(mf.sharedMesh.bounds.center);
                        pieceChunk = ChunkOf(c.x, -c.z);
                    }
                    for (int s = 0; s < d.Subs.Length; s++)
                    {
                        Material mat = Resolve(s < mats.Length ? mats[s] : null, p.Team);
                        if (mat == null) continue;
                        if (!groups.TryGetValue((pieceChunk, mat), out var list)) groups[(pieceChunk, mat)] = list = new List<(MeshData, int, Matrix4x4)>();
                        list.Add((d, s, m));
                    }
                }
            }
            foreach (var kv in groups) MakeGroup("Chunk " + kv.Key.chunk + " " + kv.Key.mat.name, kv.Key.mat, kv.Value);
            int chunksX = (_map.Width + ChunkSize - 1) / ChunkSize, chunksZ = (_map.Height + ChunkSize - 1) / ChunkSize;
            for (int cz = 0; cz < chunksZ; cz++)
            for (int cx = 0; cx < chunksX; cx++)
                BuildGroundChunk(cz * 64 + cx);
            foreach (WorldBreakable b in Plan.Breakables)
            {
                var go = new GameObject("Breakable " + b.Family);
                go.transform.SetParent(_root, false);
                Vector3 pos = b.Matrix.GetColumn(3);
                go.transform.SetPositionAndRotation(pos, b.Matrix.rotation);
                var mf = go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                _breakables.Add((b, mf, null));
            }
            SyncBreakables();
            _dressed = Plan.Dressed;
            _dressedTypes = Plan.CellsAtPlan;
            DressVersion++;
        }

        private void SyncBreakables()
        {
            byte[] cells = _map.Cells;
            for (int k = 0; k < _breakables.Count; k++)
            {
                var (b, mf, state) = _breakables[k];
                int intact = 0, cracked = 0, destroyed = 0;
                foreach (int i in b.Cells)
                {
                    if (cells[i] == Cell.BreakableIntact) intact++;
                    else if (cells[i] == Cell.BreakableCracked) cracked++;
                    else destroyed++;
                }
                string s = b.Cells.Count == 0 || intact == b.Cells.Count ? "intact" : (intact == 0 && cracked == 0 ? "destroyed" : "cracked");
                if (s == state) continue;
                GameObject model = _catalog.Find(b.Family + s);
                MeshFilter src = model != null ? model.GetComponentInChildren<MeshFilter>(true) : null;
                mf.sharedMesh = src != null ? src.sharedMesh : null;
                var r = mf.GetComponent<MeshRenderer>();
                MeshRenderer sr = src != null ? src.GetComponent<MeshRenderer>() : null;
                if (sr != null)
                {
                    Material[] mats = sr.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = Resolve(mats[i], b.Team);
                    r.sharedMaterials = mats;
                }
                _breakables[k] = (b, mf, s);
            }
        }

        /// <summary>Ground of one chunk: env_ground_cell on every land cell at its height (current grid).</summary>
        private void BuildGroundChunk(int chunk)
        {
            if (_root == null) return;
            if (_groundChunks.TryGetValue(chunk, out GameObject old) && old != null)
            {
                Destroy(old.GetComponent<MeshFilter>().sharedMesh);
                Destroy(old);
            }
            GameObject model = _catalog.Find("env_ground_cell");
            MeshFilter mf = model != null ? model.GetComponentInChildren<MeshFilter>(true) : null;
            MeshData d = mf != null ? Data(mf.sharedMesh) : null;
            if (d == null) return;
            int x0 = (chunk % 64) * ChunkSize, z0 = (chunk / 64) * ChunkSize;
            var list = new List<(MeshData, int, Matrix4x4)>();
            for (int cz = z0; cz < Math.Min(z0 + ChunkSize, _map.Height); cz++)
            for (int cx = x0; cx < Math.Min(x0 + ChunkSize, _map.Width); cx++)
            {
                int i = cz * _map.Width + cx;
                if (!WorldDresser.Solid(_map.Cells[i])) continue;
                list.Add((d, 0, Matrix4x4.Translate(new Vector3(cx + 0.5f, (float)_map.Height01[i], -(cz + 0.5f)))));
            }
            if (list.Count == 0) return;
            _groundChunks[chunk] = MakeGroup("Ground " + chunk, GroundMaterial(), list);
        }

        private Material GroundMaterial()
        {
            if (_ground != null) return _ground;
            var baseMat = Resources.Load<Material>(WorldKit.ResourcePath(WorldLook.Ground));
            if (baseMat == null) return null;
            _ground = new Material(baseMat) { name = "WorldGround (field)" };
            _ground.SetTexture("_FieldTex", _field);
            _ground.SetVector("_FieldRect", new Vector4(0, 0, _map.Width, _map.Height));
            return _ground;
        }

        /// <summary>The material for a kit slot and team: the look material, a team clone where the look swaps team.</summary>
        private Material Resolve(Material src, WorldTeam team)
        {
            if (src == null) return null;
            if (!Enum.TryParse(src.name.StartsWith("World", StringComparison.Ordinal) ? src.name.Substring(5) : "", out WorldLook look)) return src;
            if (look == WorldLook.Ground) return GroundMaterial();
            bool swap = look != WorldLook.Pool && WorldKit.TeamSwapped(look) && (team == WorldTeam.Crimson || (team == WorldTeam.Neutral && (look == WorldLook.Base || look == WorldLook.BaseFlat)));
            if (!swap) return src;
            string key = src.name + "|" + team;
            if (_mats.TryGetValue(key, out Material m) && m != null) return m;
            m = new Material(src) { name = src.name + " " + team };
            if (look == WorldLook.GlowDecal) m.SetFloat("_SwapTeam", 1f);
            else if (look == WorldLook.Crystal)
            {
                m.SetColor("_TeamCloth", Hex(WorldKit.EnvCrimsonCloth));
                m.SetColor("_TeamGlow", Hex(WorldKit.EnvCrimsonGlow));
                m.SetColor("_TeamDark", Hex(WorldKit.EnvCrimsonDark));
            }
            else
            {
                bool crimson = team == WorldTeam.Crimson;
                m.SetColor("_TeamCloth", Hex(crimson ? WorldKit.BaseCrimsonCloth : WorldKit.BaseNeutralCloth));
                m.SetColor("_TeamGlow", Hex(crimson ? WorldKit.BaseCrimsonGlow : WorldKit.BaseNeutralGlow));
                m.SetColor("_TeamDark", Hex(crimson ? WorldKit.BaseCrimsonDark : WorldKit.BaseNeutralDark));
            }
            _mats[key] = m;
            return m;
        }

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color c);
            return c;
        }

        // ------------------------------------------------------------------------------------------- combining
        private sealed class MeshData
        {
            public Vector3[] V, N;
            public Color[] C;
            public Vector2[] UV;
            public List<Vector3> UV2 = new List<Vector3>(), UV3 = new List<Vector3>();
            public int[][] Subs;
        }

        private MeshData Data(Mesh mesh)
        {
            if (mesh == null) return null;
            if (_meshData.TryGetValue(mesh, out MeshData d)) return d;
            if (!mesh.isReadable)
            {
                _meshData[mesh] = null;
                return null;
            }
            d = new MeshData { V = mesh.vertices, N = mesh.normals, C = mesh.colors, UV = mesh.uv, Subs = new int[mesh.subMeshCount][] };
            mesh.GetUVs(2, d.UV2);
            mesh.GetUVs(3, d.UV3);
            for (int s = 0; s < mesh.subMeshCount; s++) d.Subs[s] = mesh.GetTriangles(s);
            _meshData[mesh] = d;
            return d;
        }

        /// <summary>One combined mesh: positions, normals, UV2 (foam extrusion) and UV3 (outline direction) transformed.</summary>
        private GameObject MakeGroup(string name, Material mat, List<(MeshData d, int sub, Matrix4x4 m)> parts)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var c = new List<Color>();
            var uv = new List<Vector2>();
            var uv2 = new List<Vector3>();
            var uv3 = new List<Vector3>();
            var tri = new List<int>();
            var map = new Dictionary<int, int>();
            foreach (var (d, sub, m) in parts)
            {
                Matrix4x4 nm = m.inverse.transpose;
                map.Clear();
                foreach (int i in d.Subs[sub])
                {
                    if (!map.TryGetValue(i, out int j))
                    {
                        j = v.Count;
                        map[i] = j;
                        v.Add(m.MultiplyPoint3x4(d.V[i]));
                        n.Add(d.N.Length > i ? nm.MultiplyVector(d.N[i]).normalized : Vector3.up);
                        c.Add(d.C.Length > i ? d.C[i] : Color.white);
                        uv.Add(d.UV.Length > i ? d.UV[i] : Vector2.zero);
                        uv2.Add(d.UV2.Count > i && d.UV2[i].sqrMagnitude > 0.25f ? nm.MultiplyVector(d.UV2[i]).normalized : Vector3.zero);
                        uv3.Add(d.UV3.Count > i ? nm.MultiplyVector(d.UV3[i]).normalized : n[j]);
                    }
                    tri.Add(j);
                }
            }
            var mesh = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetColors(c);
            mesh.SetUVs(0, uv);
            mesh.SetUVs(2, uv2);
            mesh.SetUVs(3, uv3);
            mesh.SetTriangles(tri, 0, true);
            mesh.UploadMeshData(true);
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            bool casts = Enum.TryParse(mat.name.StartsWith("World", StringComparison.Ordinal) ? mat.name.Substring(5).Split(' ')[0] : "", out WorldLook look) && WorldKit.CastsShadows(look);
            r.shadowCastingMode = casts ? ShadowCastingMode.On : ShadowCastingMode.Off;
            Triangles += tri.Count / 3;
            Renderers++;
            return go;
        }
    }
}
