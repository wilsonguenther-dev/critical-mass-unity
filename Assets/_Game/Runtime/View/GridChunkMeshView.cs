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
    /// Art direction 2026-11-12: floor cells use CM/ToonGround (world-space cell pattern, continuous across chunks, no
    /// outline; cliff faces in rock dark), walls, bridges, breakables and barricades use CM/Toon with the 2 px structure
    /// line (UV3 carries each box corner's outward direction so the hull has no gaps at the corners), water cells use
    /// the flat ground variant, and every water cell side that touches land gets a 0.15 u white foam band (FoamWidth)
    /// on the water surface. The current Skybreak Basin map has no Water cells, so no foam is drawn on it yet.
    /// </summary>
    public sealed class GridChunkMeshView : MonoBehaviour
    {
        public const int ChunkSize = 16;

        private const int LookCream = 0;
        private const int LookBrass = 1;
        private const int LookStone = 2;
        private const int LookMeat = 3;
        private const int LookWater = 4;
        private const int LookFoam = 5;
        private const int LookCount = 6;

        /// <summary>Foam band width on the water side of every land/water contact (art direction 2.5), u.</summary>
        public const double FoamWidth = 0.15;
        /// <summary>Water surface height (u) and the foam's lift above it (no z-fighting).</summary>
        private const double WaterTop = -0.2;
        private const double FoamLift = 0.01;

        private SimDriverBehaviour _driver;
        private World _world;
        private MapDef _map;
        private byte[] _cellsCopy;
        private sbyte[] _liftCopy;   // b15 terrain height: the lift layer as last drawn (null without the rule)
        private int _seenGridVersion;
        private int _chunksX;
        private int _chunksZ;
        private GameObject[] _chunkObjects;
        private Mesh[] _meshes;
        private bool[] _dirty;
        private Material[] _materials;

        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector3> _outline = new List<Vector3>();
        private readonly List<int>[] _tris =
        {
            new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>(),
        };

        private int _seenDressVersion;

        // b14 terrain look: changed terrain (pits, filled and placed blocks, short wall blocks) is TerrainEditMesh's, one
        // extra mesh per chunk that has any, created on first need (a match with no digs makes none).
        private readonly TerrainEditMesh _edit = new TerrainEditMesh();
        private GameObject[] _editObjects;
        private Mesh[] _editMeshes;
        private int[] _editPits, _editBlocks, _editWalls;
        private int _seenEditCaptures = -1;

        /// <summary>Pit cells, earth block cells and short wall cells drawn now over all chunks (WallShots prints them).</summary>
        public static int EditPits { get; private set; }
        public static int EditBlocks { get; private set; }
        public static int EditShortWalls { get; private set; }
        /// <summary>Wall time of the last chunk rebuild, ms (the hitch budget's proof).</summary>
        public static double LastRebuildMs { get; private set; }
        private static readonly System.Diagnostics.Stopwatch RebuildWatch = new System.Diagnostics.Stopwatch();

        private void Start()
        {
            // World art (2026-11-12): the dressed world draws ground, bridges and the walls the bases kit covers; this
            // view keeps drawing every cell it does not cover, and any covered cell whose type changes (dig, breakables).
            if (GetComponent<Dressing.WorldView>() == null) gameObject.AddComponent<Dressing.WorldView>();
            _driver = GetComponent<SimDriverBehaviour>();
            if (_driver == null) _driver = FindAnyObjectByType<SimDriverBehaviour>();
            if (_driver != null && _driver.World != null) BuildAll(_driver.World);
        }

        // Integration batch 3 (perf: 206 shadow casters at High against 150): the cell chunks are scenery, so they cast only
        // where RenderTier allows scenery shadows (High and above), like WorldView's groups; they used to cast at every level.
        private static UnityEngine.Rendering.ShadowCastingMode ChunkShadows() =>
            RenderTier.Policy(RenderTier.Level).sceneryShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;

        private void OnEnable() => RenderTier.Changed += ApplyTier;
        private void OnDisable() => RenderTier.Changed -= ApplyTier;

        private void ApplyTier()
        {
            if (_chunkObjects == null) return;
            var mode = ChunkShadows();
            foreach (GameObject go in _chunkObjects)
            {
                var r = go != null ? go.GetComponent<MeshRenderer>() : null;
                if (r != null && r.shadowCastingMode != mode) r.shadowCastingMode = mode;
            }
            if (_editObjects != null)
                foreach (GameObject go in _editObjects)
                {
                    var r = go != null ? go.GetComponent<MeshRenderer>() : null;
                    if (r != null && r.shadowCastingMode != mode) r.shadowCastingMode = mode;
                }
        }

        private void LateUpdate() { var cost = CriticalMass.Runtime.Game.FrameCost.Enter(); LateUpdateCost(); CriticalMass.Runtime.Game.FrameCost.Exit(CriticalMass.Runtime.Game.FrameCost.Slot.Ground, cost); }

        void LateUpdateCost()
        {
            if (_driver == null) return;
            World w = _driver.World;
            if (w == null) return;
            if (w != _world || w.Map != _map || w.Map.Cells.Length != _cellsCopy.Length)
            {
                BuildAll(w);
                return;
            }
            if (Dressing.WorldView.DressVersion != _seenDressVersion)
            {
                _seenDressVersion = Dressing.WorldView.DressVersion;
                if (_dirty != null) for (int c = 0; c < _dirty.Length; c++) RebuildChunk(c);
            }
            TerrainEdits.Sync(w);
            if (TerrainEdits.Captures != _seenEditCaptures)
            {
                // A new map or a repaint (World.Epoch): every chunk again, so filled blocks of the last match go away.
                _seenEditCaptures = TerrainEdits.Captures;
                System.Array.Copy(w.Map.Cells, _cellsCopy, _cellsCopy.Length);
                if (_liftCopy != null && w.Map.Lift != null) System.Array.Copy(w.Map.Lift, _liftCopy, _liftCopy.Length);
                _seenGridVersion = w.GridVersion;
                for (int c = 0; c < _dirty.Length; c++) { _dirty[c] = false; RebuildChunk(c); }
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
                // b14: a pit's depth and walls read cells up to two away, and a spoil lump sits on the neighbour, so every
                // chunk within two cells rebuilds (still only the chunks around the edit, never the whole map).
                MarkDirtyAround(i % width, i / width);
                any = true;
            }
            sbyte[] lift = w.Map.Lift;   // b15: a level change rebuilds the chunks around it too
            if (lift != null && _liftCopy != null)
            {
                for (int i = 0; i < lift.Length; i++)
                {
                    if (lift[i] == _liftCopy[i]) continue;
                    _liftCopy[i] = lift[i];
                    MarkDirtyAround(i % width, i / width);
                    any = true;
                }
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

        private void MarkDirtyAround(int cx, int cz)
        {
            int ax = Mathf.Max(0, cx - 2) / ChunkSize, bx = Mathf.Min(_map.Width - 1, cx + 2) / ChunkSize;
            int az = Mathf.Max(0, cz - 2) / ChunkSize, bz = Mathf.Min(_map.Height - 1, cz + 2) / ChunkSize;
            for (int z = az; z <= bz; z++)
            for (int x = ax; x <= bx; x++)
                _dirty[z * _chunksX + x] = true;
        }

        private void BuildAll(World w)
        {
            DestroyChunks();
            _world = w;
            _map = w.Map;
            _cellsCopy = (byte[])_map.Cells.Clone();
            _liftCopy = _map.Lift != null ? (sbyte[])_map.Lift.Clone() : null;
            _seenGridVersion = w.GridVersion;
            TerrainEdits.Sync(w);
            _seenEditCaptures = TerrainEdits.Captures;
            _seenDressVersion = Dressing.WorldView.DressVersion;
            _chunksX = (_map.Width + ChunkSize - 1) / ChunkSize;
            _chunksZ = (_map.Height + ChunkSize - 1) / ChunkSize;

            if (_materials == null)
            {
                _materials = new Material[LookCount];
                _materials[LookCream] = ToonMaterials.Ground(ToonMaterials.GroundKind.Grass);
                _materials[LookBrass] = ToonMaterials.Structure(Palette.Brass);
                _materials[LookStone] = ToonMaterials.Structure(Palette.Stone);
                _materials[LookMeat] = ToonMaterials.Structure(Palette.Meat);
                _materials[LookWater] = ToonMaterials.Ground(ToonMaterials.GroundKind.Water);
                _materials[LookFoam] = ToonMaterials.Ground(ToonMaterials.GroundKind.Foam);
            }

            int count = _chunksX * _chunksZ;
            _chunkObjects = new GameObject[count];
            _meshes = new Mesh[count];
            _dirty = new bool[count];
            _editObjects = new GameObject[count];
            _editMeshes = new Mesh[count];
            _editPits = new int[count];
            _editBlocks = new int[count];
            _editWalls = new int[count];
            EditPits = EditBlocks = EditShortWalls = 0;
            for (int c = 0; c < count; c++)
            {
                int chx = c % _chunksX;
                int chz = c / _chunksX;
                var go = new GameObject("Chunk " + chx + "," + chz);
                go.transform.SetParent(transform, false);
                var mesh = new Mesh { name = "GridChunk " + chx + "," + chz };
                mesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = _materials;
                mr.shadowCastingMode = ChunkShadows();
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
            if (_editObjects != null)
                for (int c = 0; c < _editObjects.Length; c++) if (_editObjects[c] != null) Destroy(_editObjects[c]);
            if (_editMeshes != null)
                for (int c = 0; c < _editMeshes.Length; c++) if (_editMeshes[c] != null) Destroy(_editMeshes[c]);
            _editObjects = null;
            _editMeshes = null;
        }

        private void RebuildChunk(int c)
        {
            RebuildWatch.Restart();
            int chx = c % _chunksX;
            int chz = c / _chunksX;
            int x0 = chx * ChunkSize;
            int z0 = chz * ChunkSize;
            int x1 = Mathf.Min(x0 + ChunkSize, _map.Width);
            int z1 = Mathf.Min(z0 + ChunkSize, _map.Height);

            _verts.Clear();
            _normals.Clear();
            _outline.Clear();
            for (int k = 0; k < _tris.Length; k++) _tris[k].Clear();

            TerrainEdits.Arrays(_map, out byte[] pristine, out bool[] touched);
            for (int cz = z0; cz < z1; cz++)
            {
                for (int cx = x0; cx < x1; cx++)
                {
                    int i = cz * _map.Width + cx;
                    // b14: changed terrain and short walls are TerrainEditMesh's (a barricade on an edited cell keeps its
                    // grey-box body when the barricade model is not imported).
                    byte cur = _cellsCopy[i];
                    TerrainEdits.Kind kind = pristine != null ? TerrainEdits.Classify(pristine[i], cur, touched[i], _liftCopy != null ? _liftCopy[i] : (sbyte)0) : TerrainEdits.Kind.None;
                    if (TerrainEditMesh.Owns(cur, kind) && !(cur == Cell.Barricade && !BarricadeModelShown)) continue;
                    if (Dressing.WorldView.Draws(i, _cellsCopy[i])) continue;
                    if (!CellBox(_cellsCopy[i], _map.Height01[i], out double top, out double bottom, out int look)) continue;
                    AddBox(cx, cx + 1, cz, cz + 1, bottom, top, _tris[look]);
                    if (_cellsCopy[i] == Cell.Water) AddFoam(cx, cz);
                }
            }

            Mesh mesh = _meshes[c];
            mesh.Clear();
            mesh.subMeshCount = _tris.Length;
            mesh.SetVertices(_verts);
            mesh.SetNormals(_normals);
            mesh.SetUVs(3, _outline);
            for (int k = 0; k < _tris.Length; k++) mesh.SetTriangles(_tris[k], k, false);
            mesh.RecalculateBounds();

            _edit.Lift = _liftCopy;   // b15
            _edit.StepU = _map.LiftStepU > 0 ? _map.LiftStepU : 0.45;
            _edit.Build(_map.Width, _map.Height, _cellsCopy, _map.Height01, pristine, touched, x0, z0, x1, z1);
            if (_edit.Tris.Count > 0 || _editMeshes[c] != null)
            {
                if (_editMeshes[c] == null)
                {
                    var go = new GameObject("Terrain Edits " + chx + "," + chz);
                    go.transform.SetParent(transform, false);
                    var em = new Mesh { name = "TerrainEdits " + chx + "," + chz };
                    em.MarkDynamic();
                    go.AddComponent<MeshFilter>().sharedMesh = em;
                    var er = go.AddComponent<MeshRenderer>();
                    er.sharedMaterial = TerrainEditMesh.Material();
                    er.shadowCastingMode = ChunkShadows();
                    _editObjects[c] = go;
                    _editMeshes[c] = em;
                }
                bool has = _edit.Upload(_editMeshes[c]);
                var r = _editObjects[c].GetComponent<MeshRenderer>();
                if (r.enabled != has) r.enabled = has;
            }
            EditPits += _edit.PitCells - _editPits[c];
            EditBlocks += _edit.BlockCells - _editBlocks[c];
            EditShortWalls += _edit.ShortWallCells - _editWalls[c];
            _editPits[c] = _edit.PitCells;
            _editBlocks[c] = _edit.BlockCells;
            _editWalls[c] = _edit.ShortWallCells;
            LastRebuildMs = RebuildWatch.Elapsed.TotalMilliseconds;
        }

        /// <summary>True when the art catalog carries the Miner barricade model (art import 2026-11-12).</summary>
        private static bool BarricadeModelShown =>
            CriticalMass.Runtime.View.Art.ArtCatalog.Load()?.Find(CriticalMass.Runtime.View.Art.ArtMapping.MinerBarricade) != null;

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
                    // With the Miner barricade model imported (ActorViews draws it), the cell is plain floor under it.
                    if (BarricadeModelShown) { top = h; bottom = h - 0.5; look = LookCream; return true; }
                    top = h + 1.0; bottom = h - 0.5; look = LookMeat; return true;
                case Cell.LowWall:   // b13-hook: a short wall (item B restyles it)
                    // b14: not reached in RebuildChunk (TerrainEditMesh draws the bevelled stone block, top WallView.ShortTop);
                    // kept for any other caller of CellBox.
                    top = h + 0.6; bottom = h - 0.02; look = LookStone; return true;
                case Cell.Water:
                    top = WaterTop; bottom = WaterTop - 0.2; look = LookWater; return true;
                default:
                    top = 0; bottom = 0; look = 0; return false;
            }
        }

        /// <summary>Six faces of the sim box [x0, x1] x [y0, y1] x [z0, z1], each with an outward sim normal.</summary>
        private void AddBox(double x0, double x1, double z0, double z1, double y0, double y1, List<int> tris)
        {
            _cx = (x0 + x1) / 2; _cy = (y0 + y1) / 2; _cz = (z0 + z1) / 2;
            AddQuad(x0, y1, z0, x1, y1, z0, x1, y1, z1, x0, y1, z1, 0, 1, 0, tris);   // top
            AddQuad(x0, y0, z0, x0, y0, z1, x1, y0, z1, x1, y0, z0, 0, -1, 0, tris);  // bottom
            AddQuad(x1, y0, z0, x1, y0, z1, x1, y1, z1, x1, y1, z0, 1, 0, 0, tris);   // +x
            AddQuad(x0, y0, z0, x0, y1, z0, x0, y1, z1, x0, y0, z1, -1, 0, 0, tris);  // -x
            AddQuad(x0, y0, z1, x0, y1, z1, x1, y1, z1, x1, y0, z1, 0, 0, 1, tris);   // +z (sim)
            AddQuad(x0, y0, z0, x1, y0, z0, x1, y1, z0, x0, y1, z0, 0, 0, -1, tris);  // -z (sim)
        }

        /// <summary>The centre of the box being added: each corner's outline direction (UV3) points away from it.</summary>
        private double _cx, _cy, _cz;

        /// <summary>Land/water contact (the water-edge hook): a FoamWidth band inside water cell (cx, cz) along each side whose neighbour is land.</summary>
        private void AddFoam(int cx, int cz)
        {
            double y = WaterTop + FoamLift;
            double w = FoamWidth;
            List<int> t = _tris[LookFoam];
            _cx = cx + 0.5; _cy = y - 1; _cz = cz + 0.5;
            if (IsLand(cx - 1, cz)) AddQuad(cx, y, cz, cx + w, y, cz, cx + w, y, cz + 1, cx, y, cz + 1, 0, 1, 0, t);
            if (IsLand(cx + 1, cz)) AddQuad(cx + 1 - w, y, cz, cx + 1, y, cz, cx + 1, y, cz + 1, cx + 1 - w, y, cz + 1, 0, 1, 0, t);
            if (IsLand(cx, cz - 1)) AddQuad(cx, y, cz, cx + 1, y, cz, cx + 1, y, cz + w, cx, y, cz + w, 0, 1, 0, t);
            if (IsLand(cx, cz + 1)) AddQuad(cx, y, cz + 1 - w, cx + 1, y, cz + 1 - w, cx + 1, y, cz + 1, cx, y, cz + 1, 0, 1, 0, t);
        }

        /// <summary>True for a drawn, non-water cell inside the map.</summary>
        private bool IsLand(int cx, int cz)
        {
            if (cx < 0 || cz < 0 || cx >= _map.Width || cz >= _map.Height) return false;
            byte c = _cellsCopy[cz * _map.Width + cx];
            return c != Cell.Water && c != Cell.Void;
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
            _outline.Add(SimSpace.ToWorld(ax - _cx, ay - _cy, az - _cz).normalized);
            _outline.Add(SimSpace.ToWorld(bx - _cx, by - _cy, bz - _cz).normalized);
            _outline.Add(SimSpace.ToWorld(cx - _cx, cy - _cy, cz - _cz).normalized);
            _outline.Add(SimSpace.ToWorld(dx - _cx, dy - _cy, dz - _cz).normalized);

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
