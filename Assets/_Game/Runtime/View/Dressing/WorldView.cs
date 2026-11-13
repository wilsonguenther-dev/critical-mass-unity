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
    /// Art refresh 2 (2026-11-12): kit pieces combine per 32 x 32 cell group (GroupSize) per material, which cut the
    /// renderer count; a dig or place re-plans the world (WorldDresser.Plan is a pure function of the map) and rebuilds
    /// only the groups whose placements changed (ChangedGroups), so lips, cliffs and ledges follow the new edge; the hill
    /// site's team materials take the colours of the team holding the hill (MatchState.HillState, HillSwatch).
    /// World reactions (2026-11-12): pieces that move (WorldKit.SwayKind: grass, flowers, bushes, trees, banners, bridge
    /// decks, waterfalls) combine onto a sway clone of their look material (_SwayOn 1, _WaterFlow 1 for water) and every
    /// vertex carries UV5 = (pivot world x, pivot world z, weight, kind): weight = height squared within the piece for
    /// foliage and trees, distance from the pole for banners, sin(pi t) along the deck for bridges. Still pieces (ledges,
    /// ground cells, cliffs) keep the plain material and pay nothing. Sway group bounds grow 0.6 u each way.
    /// Scenery LODs (2026-11-12 triangle budget): groups are 16 x 16 cells (the sky too, so off-screen chunks are frustum
    /// culled), and every group is combined once per level of detail it needs at the current quality (SceneryLod: the
    /// lighter kit files *_lod1 / *_lod2, tiny pieces culled at level 2); LateUpdate shows one level per chunk from the
    /// ground distance between the camera's look point and the chunk (SceneryLod.Pick, with hysteresis). Small clutter is
    /// thinned by quality and only large pieces cast shadows. A quality change rebuilds the groups once. Still one material
    /// per combined mesh (SRP Batcher), UV5 sway data per vertex as before (LOD meshes keep the source bounds).
    /// </summary>
    public sealed class WorldView : MonoBehaviour
    {
        public const int ChunkSize = 16;
        /// <summary>Cells per side of one combined kit group (ground chunks stay ChunkSize).</summary>
        public const int GroupSize = 16;
        /// <summary>Sky groups (clouds, sky rocks) use their own keys: SkyKeyBase + the ground key of their position.</summary>
        public const int SkyKeyBase = 1 << 16;

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
        private readonly Dictionary<int, Chunk> _groups = new Dictionary<int, Chunk>();

        /// <summary>One spatial chunk of combined kit groups: its objects, its renderers per level of detail (with whether
        /// each may cast), the level it shows and its world bounds (the LOD distance is measured to them).</summary>
        private sealed class Chunk
        {
            public readonly List<GameObject> Objects = new List<GameObject>();
            public readonly List<MeshRenderer>[] ByLevel = { new List<MeshRenderer>(), new List<MeshRenderer>(), new List<MeshRenderer>() };
            /// <summary>The chunk's one shadow-only renderer (null when nothing in it is large enough to cast).</summary>
            public MeshRenderer Shadow;
            public int ShadowTris;
            public Bounds Bounds;
            public bool HasBounds;
            public int Shown = -1;
            public readonly int[] Tris = new int[SceneryLod.Levels];
        }

        // Scenery LODs: the quality the groups were built for (levels and clutter density).
        private int _builtQuality = -1;
        private int _groundTris;
        private UnityEngine.Camera _cam;

        /// <summary>Chunks drawn at each level right now (tests and the perf report).</summary>
        public readonly int[] ChunksAtLevel = new int[SceneryLod.Levels];

        /// <summary>Triangles combined per level over all chunks at the current quality (memory, not what is drawn).</summary>
        public readonly int[] TrianglesAtLevel = new int[SceneryLod.Levels];

        /// <summary>Kit chunks (16 x 16 cells, sky separate) the view built.</summary>
        public int ChunkCount => _groups.Count;

        private readonly List<Material> _hillMats = new List<Material>();
        private readonly Dictionary<Material, Material> _swayMats = new Dictionary<Material, Material>();

        /// <summary>Per-part sway data baked into UV5 by MakeGroup (kind 0 = still).</summary>
        private struct SwayInfo
        {
            public float PivotX, PivotZ, SpanA, SpanB;
            public byte Kind;
        }

        /// <summary>The sway clone of a look material (_SwayOn 1; water also flows). Hill-tinted materials never sway.</summary>
        private Material SwayMaterial(Material src, byte kind)
        {
            if (src == null || kind == 0 || _hillMats.Contains(src) || !src.HasProperty("_SwayOn")) return src;
            if (_swayMats.TryGetValue(src, out Material m) && m != null) return m;
            m = new Material(src) { name = src.name + " (sway)" };
            m.SetFloat("_SwayOn", 1f);
            if (kind == 7 && m.HasProperty("_WaterFlow")) m.SetFloat("_WaterFlow", 1f);
            _swayMats[src] = m;
            return m;
        }
        private HillState _hillShown = (HillState)(-1);
        // DG-58: the bases file is parsed once per world build (it was re-read and re-parsed on every dig), the dirty-chunk
        // set is reused, and digs are merged into at most one re-plan every ReplanEveryFrames frames.
        public const int ReplanEveryFrames = 6;
        private BasesFile _bases;
        private readonly HashSet<int> _dirtyChunks = new HashSet<int>();
        private bool _replanPending;
        private int _lastReplanFrame = -1000;

        /// <summary>How many times this view parsed the bases file (DG-58 test: once per world build).</summary>
        public int BasesParses { get; private set; }

        /// <summary>World re-plans run since the view was created (tests).</summary>
        public int Replans { get; private set; }

        /// <summary>The hill tint shown now (tests read it).</summary>
        public HillState HillShown => _hillShown;

        // Render tier (2026-11-12 triangle budget): the materials and renderers the scenery uses, so a quality change can
        // drop the scenery outline pass and scenery shadow casting without touching the characters.
        private readonly HashSet<Material> _sceneryMats = new HashSet<Material>();

        /// <summary>Scenery triangles drawn once (the perf report's world_triangles; outline and shadow passes multiply it).</summary>
        public static int LastTriangles { get; private set; }

        private void OnEnable() => RenderTier.Changed += ApplyTier;
        private void OnDisable() => RenderTier.Changed -= ApplyTier;

        /// <summary>Applies RenderTier.Policy to every scenery renderer and material (players only for materials: they are
        /// shared project assets in the editor).</summary>
        private void ApplyTier()
        {
            // Scenery LODs: another quality level needs other levels of detail or another clutter density: rebuild once.
            if (Plan != null && _root != null && NeedsRebuild(_builtQuality, RenderTier.Level))
            {
                var keys = new List<int>(_groups.Keys);
                foreach (int g in keys) DestroyGroup(g);
                BuildGroups(null);
                for (int k = 0; k < _breakables.Count; k++) _breakables[k] = (_breakables[k].b, _breakables[k].mf, null);
                if (_map != null) SyncBreakables();
            }
            (bool outline, bool shadows) = RenderTier.Policy(RenderTier.Level);
            if (!Application.isEditor)
                foreach (Material m in _sceneryMats)
                    if (m != null && m.GetShaderPassEnabled("SRPDefaultUnlit") != outline) m.SetShaderPassEnabled("SRPDefaultUnlit", outline);
            foreach (Chunk ch in _groups.Values)
                if (ch.Shadow != null) ch.Shadow.enabled = shadows;
        }

        /// <summary>True when groups built for quality "built" are wrong for quality "now" (levels built or clutter
        /// density differ).</summary>
        public static bool NeedsRebuild(int built, int now)
        {
            if (built < 0) return true;
            if (SceneryLod.ClutterDensity(built) != SceneryLod.ClutterDensity(now)) return true;
            for (int l = 0; l < SceneryLod.Levels; l++)
                if (SceneryLod.Builds(built, l) != SceneryLod.Builds(now, l)) return true;
            return false;
        }

        /// <summary>
        /// Shows one level of detail per chunk: the ground distance from the camera's look point to the chunk's bounds
        /// picks it (SceneryLod.Pick with hysteresis); a level the current quality did not build falls back to the nearest
        /// one that was built. Renderers of the other levels are disabled (they are not culled, drawn or shadowed). No
        /// allocations: it walks the chunk table once per frame.
        /// </summary>
        private void UpdateLods()
        {
            if (_groups.Count == 0) return;
            if (_cam == null || !_cam.isActiveAndEnabled) _cam = UnityEngine.Camera.main;
            // No camera yet (or a capture without one tagged MainCamera): every chunk shows the finest level built.
            bool haveCam = _cam != null;
            Vector3 look = haveCam ? SceneryLod.LookPoint(_cam.transform.position, _cam.transform.forward) : Vector3.zero;
            int q = _builtQuality;
            for (int l = 0; l < SceneryLod.Levels; l++) ChunksAtLevel[l] = 0;
            int shown = 0;
            foreach (Chunk ch in _groups.Values)
            {
                int want = SceneryLod.Pick(q, haveCam && ch.HasBounds ? SceneryLod.GroundDistance(look, ch.Bounds) : 0f, ch.Shown);
                int show = BuiltLevel(q, want);
                if (show != ch.Shown)
                {
                    for (int l = 0; l < SceneryLod.Levels; l++)
                    {
                        List<MeshRenderer> rs = ch.ByLevel[l];
                        for (int i = 0; i < rs.Count; i++)
                            if (rs[i] != null) rs[i].enabled = l == show;
                    }
                    ch.Shown = show;
                }
                ChunksAtLevel[show]++;
                shown += ch.Tris[show];
            }
            LastTriangles = shown + _groundTris;
        }

        /// <summary>The nearest level to "want" that quality q builds (pure; tests).</summary>
        public static int BuiltLevel(int q, int want)
        {
            if (SceneryLod.Builds(q, want)) return want;
            for (int d = 1; d < SceneryLod.Levels; d++)
            {
                if (want + d < SceneryLod.Levels && SceneryLod.Builds(q, want + d)) return want + d;
                if (want - d >= 0 && SceneryLod.Builds(q, want - d)) return want - d;
            }
            return 0;
        }

        // Physics audit DG-80 (R-M8): small props (WorldLook.Prop: barrels, crates, braziers, light pools, pebbles) never cast
        // at any level (QualityTable.PropShadows); structures, bases, crystals, trees and cliff fringes keep casting where
        // RenderTier allows scenery shadows.
        private static bool CastsShadows(Material mat) =>
            mat != null && Enum.TryParse(mat.name.StartsWith("World", StringComparison.Ordinal) ? mat.name.Substring(5).Split(' ')[0] : "", out WorldLook look) &&
            WorldKit.CastsShadows(look) && (look != WorldLook.Prop || QualityTable.Current.PropShadows);

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
            if (Plan == null) return;
            UpdateLods();
            if (w.Match != null) ApplyHill(w.Match.HillState);
            if (_replanPending && Time.frameCount - _lastReplanFrame >= ReplanEveryFrames) Replan();
            if (w.GridVersion == _seenGridVersion) return;
            _seenGridVersion = w.GridVersion;
            HashSet<int> dirtyChunks = _dirtyChunks;
            dirtyChunks.Clear();
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
            if (dirtyChunks.Count > 0)
            {
                if (Time.frameCount - _lastReplanFrame >= ReplanEveryFrames) Replan();
                else _replanPending = true;   // merged with the next digs into one re-plan
            }
            if (breakables) SyncBreakables();
        }

        /// <summary>A dig or place changed land: plan again on the current grid and rebuild the groups that differ.</summary>
        private void Replan()
        {
            _replanPending = false;
            _lastReplanFrame = Time.frameCount;
            Replans++;
            WorldPlan next = WorldDresser.Plan(_map, _bases, _catalog.Centroid, _catalog.Body);
            HashSet<int> changed = ChangedGroups(Plan, next);
            Plan = next;
            foreach (int g in changed) DestroyGroup(g);
            BuildGroups(changed);
            _dressed = Plan.Dressed;
            _dressedTypes = Plan.CellsAtPlan;
            DressVersion++;
        }

        /// <summary>The combined-group key of a sim position (sky placements: -1, one group for the whole sky).</summary>
        public static int GroupOf(double simX, double simZ)
        {
            int gx = Mathf.Clamp((int)Math.Floor(simX) / GroupSize, -1, 62) + 1, gz = Mathf.Clamp((int)Math.Floor(simZ) / GroupSize, -1, 62) + 1;
            return gz * 64 + gx;
        }

        static int GroupOf(WorldPlacement p) => p.Cat == "sky" ? SkyKeyBase + GroupOf(p.SimX, p.SimZ) : GroupOf(p.SimX, p.SimZ);

        /// <summary>The sim-space cell rectangle (x0, z0, x1, z1) a ground group key covers (tests; sky keys map to the
        /// same rectangle as their ground key). Edge keys also take the clamped placements beyond the map.</summary>
        public static (int x0, int z0, int x1, int z1) GroupRect(int key)
        {
            if (key >= SkyKeyBase) key -= SkyKeyBase;
            int gx = key % 64 - 1, gz = key / 64 - 1;
            return (gx * GroupSize, gz * GroupSize, (gx + 1) * GroupSize, (gz + 1) * GroupSize);
        }

        /// <summary>Groups whose placements (piece, position, yaw, team) differ between two plans.</summary>
        public static HashSet<int> ChangedGroups(WorldPlan a, WorldPlan b)
        {
            Dictionary<int, (long sum, int n)> Sig(WorldPlan p)
            {
                var d = new Dictionary<int, (long, int)>();
                foreach (WorldPlacement q in p.Placements)
                {
                    if (q.Piece == "env_ground_cell") continue;
                    Vector3 t = q.Matrix.GetColumn(3);
                    Vector3 f = q.Matrix.GetColumn(2);
                    long h = q.Piece.GetHashCode() * 31L + Mathf.RoundToInt(t.x * 100f) * 7919L + Mathf.RoundToInt(t.y * 100f) * 104729L +
                             Mathf.RoundToInt(t.z * 100f) * 1299709L + Mathf.RoundToInt(f.x * 1000f) * 15485863L + Mathf.RoundToInt(f.z * 1000f) * 32452843L +
                             (long)q.Team * 49979687L;
                    int g = GroupOf(q);
                    d.TryGetValue(g, out (long s, int n) cur);
                    d[g] = (cur.s + h, cur.n + 1);
                }
                return d;
            }
            Dictionary<int, (long, int)> sa = Sig(a), sb = Sig(b);
            var changed = new HashSet<int>();
            foreach (var kv in sa) if (!sb.TryGetValue(kv.Key, out var o) || !o.Equals(kv.Value)) changed.Add(kv.Key);
            foreach (var kv in sb) if (!sa.ContainsKey(kv.Key)) changed.Add(kv.Key);
            return changed;
        }

        /// <summary>The hill site's team colours per hill state (bases kit swatches): neutral grey when nobody or both hold it.</summary>
        public static (string cloth, string glow, string dark) HillSwatch(HillState s)
        {
            switch (s)
            {
                case HillState.Azure: return (WorldKit.BaseAuthoredCloth, WorldKit.BaseAuthoredGlow, WorldKit.BaseAuthoredDark);
                case HillState.Crimson: return (WorldKit.BaseCrimsonCloth, WorldKit.BaseCrimsonGlow, WorldKit.BaseCrimsonDark);
                default: return (WorldKit.BaseNeutralCloth, WorldKit.BaseNeutralGlow, WorldKit.BaseNeutralDark);
            }
        }

        /// <summary>Tints every hill-site material (ring, rim, arcs, horns, banners, breakables) for the hill state.</summary>
        public void ApplyHill(HillState s)
        {
            if (s == _hillShown) return;
            _hillShown = s;
            foreach (Material m in _hillMats) TintHill(m, s);
        }

        /// <summary>Sets one hill-site material's team channels to the hill state's swatch.</summary>
        public static void TintHill(Material m, HillState s)
        {
            if (m == null) return;
            var (cloth, glow, dark) = HillSwatch(s);
            m.SetColor("_TeamCloth", Hex(cloth));
            m.SetColor("_TeamGlow", Hex(glow));
            m.SetColor("_TeamDark", Hex(dark));
        }

        /// <summary>The hill-site materials ApplyHill tints (tests read them).</summary>
        public IReadOnlyList<Material> HillMaterials => _hillMats;

        private void DestroyGroup(int g)
        {
            if (!_groups.TryGetValue(g, out Chunk ch)) return;
            for (int l = 0; l < SceneryLod.Levels; l++)
                foreach (MeshRenderer r in ch.ByLevel[l])
                {
                    if (r == null) continue;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) TrianglesAtLevel[l] -= (int)(mf.sharedMesh.GetIndexCount(0) / 3);
                }
            foreach (GameObject go in ch.Objects)
            {
                if (go == null) continue;
                Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
                Renderer r = go.GetComponent<Renderer>();
                if (r != null) Renderers--;
                if (mesh != null)
                {
                    Triangles -= (int)(mesh.GetIndexCount(0) / 3);
                    Destroy(mesh);
                }
                Destroy(go);
            }
            _groups.Remove(g);
        }

        /// <summary>
        /// Combines the plan's kit pieces per chunk, per level of detail the current quality builds and per material; only
        /// the chunks in "only" when it is set. Small clutter is thinned by quality; a piece tiny on screen is left out of
        /// level 2 (SceneryLod.Resolve). The drawn groups never cast: each chunk gets ONE shadow-only proxy instead (the
        /// lightest level of its large casting pieces, one material, so one shadow draw per chunk).
        /// </summary>
        private void BuildGroups(HashSet<int> only)
        {
            int q = RenderTier.Level;
            _builtQuality = q;
            var groups = new Dictionary<(int chunk, int level, Material mat), List<(MeshData d, int sub, Matrix4x4 m, SwayInfo sw)>>();
            var shadows = new Dictionary<int, List<(MeshData d, int sub, Matrix4x4 m, SwayInfo sw)>>();
            Func<string, bool> hasFile = n => _catalog.Find(n) != null;
            List<(int chunk, Material mat, MeshData d, int sub, Matrix4x4 m)> parts = _parts;
            foreach (WorldPlacement p in Plan.Placements)
            {
                if (p.Piece == "env_ground_cell") continue; // drawn per chunk by BuildGroundChunk
                if (_catalog.Find(p.Piece) == null) continue;
                if (!SceneryLod.KeepClutter(p.Piece, p.SimX, p.SimZ, q)) continue;
                int chunk = GroupOf(p);
                bool layout = p.Piece.StartsWith("cm_layout_", StringComparison.Ordinal);
                if (only != null && !layout && !only.Contains(chunk)) continue;
                Vector3 pivot = p.Matrix.MultiplyPoint3x4(Vector3.zero);
                var sw = new SwayInfo { PivotX = pivot.x, PivotZ = pivot.z, SpanA = p.SpanA, SpanB = p.SpanB, Kind = layout ? (byte)0 : WorldKit.SwayKind(p.Piece) };
                for (int level = 0; level < SceneryLod.Levels; level++)
                {
                    if (!SceneryLod.Builds(q, level)) continue;
                    int drawn = SceneryLod.Resolve(p.Piece, level, hasFile);
                    if (drawn < 0) continue; // tiny on screen: culled at the far level
                    CollectParts(p, SceneryLod.LevelPiece(p.Piece, drawn), chunk, layout, only, parts);
                    foreach (var (pc, mat, d, sub, m) in parts)
                    {
                        Material look = SwayMaterial(mat, sw.Kind);
                        if (!groups.TryGetValue((pc, level, look), out var list)) groups[(pc, level, look)] = list = new List<(MeshData, int, Matrix4x4, SwayInfo)>();
                        list.Add((d, sub, m, sw));
                    }
                }
                if (!SceneryLod.LargeEnoughToCast(p.Piece)) continue;
                int lightest = SceneryLod.Resolve(p.Piece, SceneryLod.ShadowProxyLevel, hasFile);
                if (lightest < 0) continue;
                CollectParts(p, SceneryLod.LevelPiece(p.Piece, lightest), chunk, layout, only, parts);
                foreach (var (pc, mat, d, sub, m) in parts)
                {
                    if (!CastsShadows(mat)) continue;
                    if (!shadows.TryGetValue(pc, out var list)) shadows[pc] = list = new List<(MeshData, int, Matrix4x4, SwayInfo)>();
                    list.Add((d, sub, m, default(SwayInfo)));
                }
            }
            foreach (var kv in groups)
            {
                (int chunk, int level, Material mat) = kv.Key;
                GameObject go = MakeGroup("Group " + chunk + " L" + level + " " + mat.name, mat, kv.Value, ShadowCastingMode.Off);
                Chunk ch = ChunkFor(chunk, go);
                var r = go.GetComponent<MeshRenderer>();
                ch.ByLevel[level].Add(r);
                int tris = (int)(go.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0) / 3);
                ch.Tris[level] += tris;
                TrianglesAtLevel[level] += tris;
                if (ch.HasBounds) ch.Bounds.Encapsulate(r.bounds);
                else
                {
                    ch.Bounds = r.bounds;
                    ch.HasBounds = true;
                }
                r.enabled = false; // UpdateLods shows one level
                ch.Shown = -1;
            }
            Material caster = ShadowMaterial();
            bool shadowsOn = RenderTier.Policy(q).sceneryShadows;
            if (caster != null)
                foreach (var kv in shadows)
                {
                    GameObject go = MakeGroup("Shadow " + kv.Key, caster, kv.Value, ShadowCastingMode.ShadowsOnly);
                    Chunk ch = ChunkFor(kv.Key, go);
                    ch.Shadow = go.GetComponent<MeshRenderer>();
                    ch.ShadowTris = (int)(go.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0) / 3);
                    ch.Shadow.enabled = shadowsOn;
                }
            UpdateLods();
        }

        private readonly List<(int chunk, Material mat, MeshData d, int sub, Matrix4x4 m)> _parts =
            new List<(int, Material, MeshData, int, Matrix4x4)>();

        private Chunk ChunkFor(int key, GameObject go)
        {
            if (!_groups.TryGetValue(key, out Chunk ch)) _groups[key] = ch = new Chunk();
            ch.Objects.Add(go);
            return ch;
        }

        /// <summary>The look material every shadow proxy uses (a structure: it has the toon ShadowCaster pass).</summary>
        private static Material ShadowMaterial() => Resources.Load<Material>(WorldKit.ResourcePath(WorldLook.Structure));

        /// <summary>The meshes of one placement drawn with the file "file" (a level of its piece): chunk, team-resolved
        /// material, mesh data, sub-mesh and world matrix per part. Layouts draw only their cm_district_* children, each in
        /// the chunk of its own centre.</summary>
        private void CollectParts(WorldPlacement p, string file, int chunk, bool layout, HashSet<int> only,
            List<(int chunk, Material mat, MeshData d, int sub, Matrix4x4 m)> into)
        {
            into.Clear();
            GameObject model = _catalog.Find(file);
            if (model == null) return;
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
                    pieceChunk = GroupOf(c.x, -c.z);
                    if (only != null && !only.Contains(pieceChunk)) continue;
                }
                for (int s = 0; s < d.Subs.Length; s++)
                {
                    Material mat = Resolve(s < mats.Length ? mats[s] : null, p.Team);
                    if (mat != null) into.Add((pieceChunk, mat, d, s, m));
                }
            }
        }

        private void OnDestroy()
        {
            Clear();
            if (_field != null) Destroy(_field);
            _field = null;
        }

        /// <summary>
        /// DG-58: destroys everything this view created, not only the root object: every combined group and ground-chunk
        /// mesh, every cloned material (team and hill clones) and the ground material (which pointed at the old field
        /// texture), so play-again and world rebuilds never leak.
        /// </summary>
        private void Clear()
        {
            var groupKeys = new List<int>(_groups.Keys);
            foreach (int g in groupKeys) DestroyGroup(g);
            foreach (GameObject chunk in _groundChunks.Values)
            {
                if (chunk == null) continue;
                MeshFilter mf = chunk.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
            }
            foreach (Material m in _mats.Values)
                if (m != null) Destroy(m);
            _mats.Clear();
            _hillMats.Clear();
            _sceneryMats.Clear();
            if (_ground != null) Destroy(_ground);
            _ground = null;
            _replanPending = false;
            if (_root != null) Destroy(_root.gameObject);
            _root = null;
            _groundChunks.Clear();
            _groundTris = 0;
            _builtQuality = -1;
            for (int l = 0; l < SceneryLod.Levels; l++) TrianglesAtLevel[l] = ChunksAtLevel[l] = 0;
            _breakables.Clear();
            _groups.Clear();

            _hillShown = (HillState)(-1);
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
            _bases = BasesFile.Load();
            BasesParses++;
            Plan = WorldDresser.Plan(_map, _bases, _catalog.Centroid, _catalog.Body);
            _root = new GameObject("World Art").transform;
            _root.SetParent(transform, false);
            if (_field != null) Destroy(_field);
            _field = WorldField.Bake(Plan);
            Triangles = 0;
            Renderers = 0;

            BuildGroups(null);
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
                // Scenery LODs: at a quality that never draws level 0 (Low and below) breakables use their level 1 file.
                GameObject model = SceneryLod.Builds(RenderTier.Level, 0) ? null : _catalog.Find(SceneryLod.LevelPiece(b.Family + s, 1));
                if (model == null) model = _catalog.Find(b.Family + s);
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
                Mesh oldMesh = old.GetComponent<MeshFilter>().sharedMesh;
                if (oldMesh != null) _groundTris -= (int)(oldMesh.GetIndexCount(0) / 3);
                Destroy(oldMesh);
                Destroy(old);
            }
            _groundChunks.Remove(chunk);
            GameObject model = _catalog.Find("env_ground_cell");
            MeshFilter mf = model != null ? model.GetComponentInChildren<MeshFilter>(true) : null;
            MeshData d = mf != null ? Data(mf.sharedMesh) : null;
            if (d == null) return;
            int x0 = (chunk % 64) * ChunkSize, z0 = (chunk / 64) * ChunkSize;
            var list = new List<(MeshData, int, Matrix4x4, SwayInfo)>();
            for (int cz = z0; cz < Math.Min(z0 + ChunkSize, _map.Height); cz++)
            for (int cx = x0; cx < Math.Min(x0 + ChunkSize, _map.Width); cx++)
            {
                int i = cz * _map.Width + cx;
                if (!WorldDresser.Solid(_map.Cells[i])) continue;
                list.Add((d, 0, Matrix4x4.Translate(new Vector3(cx + 0.5f, (float)_map.Height01[i], -(cz + 0.5f))), default(SwayInfo)));
            }
            if (list.Count == 0) return;
            GameObject ground = MakeGroup("Ground " + chunk, GroundMaterial(), list);
            _groundChunks[chunk] = ground;
            _groundTris += (int)(ground.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0) / 3);
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
            bool hill = team == WorldTeam.Hill && (look == WorldLook.Base || look == WorldLook.BaseFlat);
            bool swap = look != WorldLook.Pool && WorldKit.TeamSwapped(look) && (team == WorldTeam.Crimson || hill || (team == WorldTeam.Neutral && (look == WorldLook.Base || look == WorldLook.BaseFlat)));
            if (!swap) return src;
            string key = src.name + "|" + team;
            if (_mats.TryGetValue(key, out Material m) && m != null) return m;
            m = new Material(src) { name = src.name + " " + team };
            if (hill)
            {
                // Hill: its own material per look, tinted by ApplyHill from the Sim's hill state (neutral until then).
                TintHill(m, _hillShown);
                _hillMats.Add(m);
                _mats[key] = m;
                return m;
            }
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
            /// <summary>Mesh-local bounds (sway weights are relative to the piece).</summary>
            public Vector3 Min, Max;
        }

        /// <summary>UV5 weight of one mesh-local vertex for a sway kind (0 root .. 1 free tip).</summary>
        private static float SwayWeight(MeshData d, Vector3 v, in SwayInfo sw)
        {
            switch (sw.Kind)
            {
                case 1:
                case 2:
                case 3:
                {
                    float h = Mathf.Clamp01((v.y - d.Min.y) / Mathf.Max(1e-3f, d.Max.y - d.Min.y));
                    return h * h;
                }
                case 4:
                case 6:
                {
                    // Banner / lantern: away from the pole (mesh origin) and down from the top hinge for the lantern.
                    float r = Mathf.Max(1e-3f, Mathf.Max(Mathf.Max(Mathf.Abs(d.Min.x), Mathf.Abs(d.Max.x)), Mathf.Max(Mathf.Abs(d.Min.z), Mathf.Abs(d.Max.z))));
                    float k = Mathf.Clamp01(new Vector2(v.x, v.z).magnitude / r);
                    if (sw.Kind == 6) k = Mathf.Clamp01((d.Max.y - v.y) / Mathf.Max(1e-3f, d.Max.y - d.Min.y));
                    return k * k;
                }
                case 5:
                {
                    if (sw.SpanB <= sw.SpanA) return 1f; // a deck cell not under a span: the local pressure alone shapes the dip
                    float a = Mathf.Clamp01((v.z - d.Min.z) / Mathf.Max(1e-3f, d.Max.z - d.Min.z));
                    return Mathf.Sin(Mathf.PI * Mathf.Lerp(sw.SpanA, sw.SpanB, a));
                }
                case 7:
                    return 1f;
                default:
                    return 0f;
            }
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
            d.Min = mesh.bounds.min;
            d.Max = mesh.bounds.max;
            mesh.GetUVs(2, d.UV2);
            mesh.GetUVs(3, d.UV3);
            for (int s = 0; s < mesh.subMeshCount; s++) d.Subs[s] = mesh.GetTriangles(s);
            _meshData[mesh] = d;
            return d;
        }

        /// <summary>One combined mesh: positions, normals, UV2 (foam extrusion) and UV3 (outline direction) transformed.</summary>
        private GameObject MakeGroup(string name, Material mat, List<(MeshData d, int sub, Matrix4x4 m, SwayInfo sw)> parts,
            ShadowCastingMode? casting = null)
        {
            // UV5 only on groups whose material reads it (the sway clones): still groups carry no extra channel.
            bool swayMat = mat != null && mat.HasProperty("_SwayOn") && mat.GetFloat("_SwayOn") > 0.5f;
            var uv5 = swayMat ? new List<Vector4>() : null;
            bool sways = false;
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var c = new List<Color>();
            var uv = new List<Vector2>();
            var uv2 = new List<Vector3>();
            var uv3 = new List<Vector3>();
            var tri = new List<int>();
            var map = new Dictionary<int, int>();
            foreach (var (d, sub, m, sw) in parts)
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
                        if (uv5 != null)
                        {
                            float wgt = SwayWeight(d, d.V[i], in sw);
                            if (wgt > 0f) sways = true;
                            uv5.Add(new Vector4(sw.PivotX, sw.PivotZ, wgt, sw.Kind));
                        }
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
            if (uv5 != null) mesh.SetUVs(5, uv5);
            mesh.SetTriangles(tri, 0, true);
            if (sways)
            {
                Bounds b = mesh.bounds;
                b.Expand(1.2f); // the sway clamps at 0.6 u: never culled while bent
                mesh.bounds = b;
            }
            mesh.UploadMeshData(true);
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            bool casts = CastsShadows(mat) && RenderTier.Policy(RenderTier.Level).sceneryShadows;
            r.shadowCastingMode = casting ?? (casts ? ShadowCastingMode.On : ShadowCastingMode.Off);
            if (mat != _ground && casting != ShadowCastingMode.ShadowsOnly && _sceneryMats.Add(mat) && !Application.isEditor)
                mat.SetShaderPassEnabled("SRPDefaultUnlit", RenderTier.Policy(RenderTier.Level).sceneryOutline);
            Triangles += tri.Count / 3;

            Renderers++;
            return go;
        }
    }
}
