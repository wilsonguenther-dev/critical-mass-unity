using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View
{
    /// <summary>
    /// The grey-box ground: one mesh per 16 x 16 cell chunk built from the Sim cell types and heights, one submesh per
    /// cell look; chunks whose cells changed are rebuilt when <c>World.GridVersion</c> moves.
    /// </summary>
    public sealed class GridChunkMeshView : MonoBehaviour
    {
        public const int ChunkSize = 16;

        private const int LookCream = 0;
        private const int LookBrass = 1;
        private const int LookStone = 2;
        private const int LookMeat = 3;
        private const int LookSkyTop = 4;
        private static readonly string[] LookHex = { Palette.Cream, Palette.Brass, Palette.Stone, Palette.Meat, Palette.SkyTop };

        private SimDriverBehaviour _driver;
        private World _world;
        private MapDef _map;
        private byte[] _cellsCopy;
        private int _seenGridVersion;
        private int _chunksX;
        private int _chunksZ;
        private GameObject[] _chunkObjects;
        private Mesh[] _meshes;
        private bool[] _dirty;
        private Material[] _materials;

        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<int>[] _tris =
        {
            new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>(),
        };

        private void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            if (_driver == null) _driver = FindAnyObjectByType<SimDriverBehaviour>();
            if (_driver != null && _driver.World != null) BuildAll(_driver.World);
        }

        private void LateUpdate()
        {
            if (_driver == null) return;
            World w = _driver.World;
            if (w == null) return;
            if (w != _world || w.Map != _map || w.Map.Cells.Length != _cellsCopy.Length)
            {
                BuildAll(w);
                return;
            }
            if (w.GridVersion == _seenGridVersion) return;
            _seenGridVersion = w.GridVersion;

            byte[] cells = w.Map.Cells;
            int width = w.Map.Width;
            bool any = false;
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] == _cellsCopy[i]) continue;
                _cellsCopy[i] = cells[i];
                int cx = i % width;
                int cz = i / width;
                _dirty[(cz / ChunkSize) * _chunksX + cx / ChunkSize] = true;
                any = true;
            }
            if (!any) return;
            for (int c = 0; c < _dirty.Length; c++)
            {
                if (!_dirty[c]) continue;
                _dirty[c] = false;
                RebuildChunk(c);
            }
        }

        private void OnDestroy()
        {
            DestroyChunks();
        }

        private void BuildAll(World w)
        {
            DestroyChunks();
            _world = w;
            _map = w.Map;
            _cellsCopy = (byte[])_map.Cells.Clone();
            _seenGridVersion = w.GridVersion;
            _chunksX = (_map.Width + ChunkSize - 1) / ChunkSize;
            _chunksZ = (_map.Height + ChunkSize - 1) / ChunkSize;

            if (_materials == null)
            {
                _materials = new Material[LookHex.Length];
                for (int k = 0; k < LookHex.Length; k++) _materials[k] = PlaceholderMaterials.Get(LookHex[k]);
            }

            int count = _chunksX * _chunksZ;
            _chunkObjects = new GameObject[count];
            _meshes = new Mesh[count];
            _dirty = new bool[count];
            for (int c = 0; c < count; c++)
            {
                int chx = c % _chunksX;
                int chz = c / _chunksX;
                var go = new GameObject("Chunk " + chx + "," + chz);
                go.transform.SetParent(transform, false);
                var mesh = new Mesh { name = "GridChunk " + chx + "," + chz };
                mesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = _materials;
                _chunkObjects[c] = go;
                _meshes[c] = mesh;
                RebuildChunk(c);
            }
        }

        private void DestroyChunks()
        {
            if (_chunkObjects != null)
            {
                for (int c = 0; c < _chunkObjects.Length; c++)
                {
                    if (_chunkObjects[c] != null) Destroy(_chunkObjects[c]);
                }
            }
            if (_meshes != null)
            {
                for (int c = 0; c < _meshes.Length; c++)
                {
                    if (_meshes[c] != null) Destroy(_meshes[c]);
                }
            }
            _chunkObjects = null;
            _meshes = null;
        }

        private void RebuildChunk(int c)
        {
            int chx = c % _chunksX;
            int chz = c / _chunksX;
            int x0 = chx * ChunkSize;
            int z0 = chz * ChunkSize;
            int x1 = Mathf.Min(x0 + ChunkSize, _map.Width);
            int z1 = Mathf.Min(z0 + ChunkSize, _map.Height);

            _verts.Clear();
            _normals.Clear();
            for (int k = 0; k < _tris.Length; k++) _tris[k].Clear();

            for (int cz = z0; cz < z1; cz++)
            {
                for (int cx = x0; cx < x1; cx++)
                {
                    int i = cz * _map.Width + cx;
                    if (!CellBox(_cellsCopy[i], _map.Height01[i], out double top, out double bottom, out int look)) continue;
                    AddBox(cx, cx + 1, cz, cz + 1, bottom, top, _tris[look]);
                }
            }

            Mesh mesh = _meshes[c];
            mesh.Clear();
            mesh.subMeshCount = _tris.Length;
            mesh.SetVertices(_verts);
            mesh.SetNormals(_normals);
            for (int k = 0; k < _tris.Length; k++) mesh.SetTriangles(_tris[k], k, false);
            mesh.RecalculateBounds();
        }

        /// <summary>The box a cell is drawn as (top, bottom, look); false for Void (not drawn).</summary>
        private static bool CellBox(byte cell, double h, out double top, out double bottom, out int look)
        {
            switch (cell)
            {
                case Cell.Floor:
                case Cell.BreakableDestroyed:
                    top = h; bottom = h - 0.5; look = LookCream; return true;
                case Cell.Bridge:
                    top = h; bottom = h - 0.2; look = LookBrass; return true;
                case Cell.Wall:
                    top = h + 1.5; bottom = h - 0.5; look = LookStone; return true;
                case Cell.BreakableIntact:
                    top = h + 1.5; bottom = h - 0.5; look = LookBrass; return true;
                case Cell.BreakableCracked:
                    top = h + 0.75; bottom = h - 0.5; look = LookBrass; return true;
                case Cell.Barricade:
                    top = h + 1.0; bottom = h - 0.5; look = LookMeat; return true;
                case Cell.Water:
                    top = -0.2; bottom = -0.4; look = LookSkyTop; return true;
                default:
                    top = 0; bottom = 0; look = 0; return false;
            }
        }

        /// <summary>Six faces of the sim box [x0, x1] x [y0, y1] x [z0, z1], each with an outward sim normal.</summary>
        private void AddBox(double x0, double x1, double z0, double z1, double y0, double y1, List<int> tris)
        {
            AddQuad(x0, y1, z0, x1, y1, z0, x1, y1, z1, x0, y1, z1, 0, 1, 0, tris);   // top
            AddQuad(x0, y0, z0, x0, y0, z1, x1, y0, z1, x1, y0, z0, 0, -1, 0, tris);  // bottom
            AddQuad(x1, y0, z0, x1, y0, z1, x1, y1, z1, x1, y1, z0, 1, 0, 0, tris);   // +x
            AddQuad(x0, y0, z0, x0, y1, z0, x0, y1, z1, x0, y0, z1, -1, 0, 0, tris);  // -x
            AddQuad(x0, y0, z1, x0, y1, z1, x1, y1, z1, x1, y0, z1, 0, 0, 1, tris);   // +z (sim)
            AddQuad(x0, y0, z0, x1, y0, z0, x1, y1, z0, x0, y1, z0, 0, 0, -1, tris);  // -z (sim)
        }

        /// <summary>
        /// One quad from four sim corners in cyclic order. Corners and normal go through SimSpace; the triangle winding is
        /// picked after the conversion so the face's front (Unity: Cross(b - a, c - a)) points along the outward normal.
        /// </summary>
        private void AddQuad(
            double ax, double ay, double az, double bx, double by, double bz,
            double cx, double cy, double cz, double dx, double dy, double dz,
            double nx, double ny, double nz, List<int> tris)
        {
            Vector3 a = SimSpace.ToWorld(ax, ay, az);
            Vector3 b = SimSpace.ToWorld(bx, by, bz);
            Vector3 c = SimSpace.ToWorld(cx, cy, cz);
            Vector3 d = SimSpace.ToWorld(dx, dy, dz);
            Vector3 n = SimSpace.ToWorld(nx, ny, nz);   // ToWorld is linear, so it maps directions too

            int i0 = _verts.Count;
            _verts.Add(a);
            _verts.Add(b);
            _verts.Add(c);
            _verts.Add(d);
            _normals.Add(n);
            _normals.Add(n);
            _normals.Add(n);
            _normals.Add(n);

            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) > 0)
            {
                tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
                tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 3);
            }
            else
            {
                tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 1);
                tris.Add(i0); tris.Add(i0 + 3); tris.Add(i0 + 2);
            }
        }
    }
}
