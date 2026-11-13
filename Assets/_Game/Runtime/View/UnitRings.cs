using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.View
{
    /// <summary>
    /// DG-03: the ground ring under every living unit (art direction 6.4). Your own ring is Tool Brass, 0.06 u thick, with
    /// four ticks on the screen axes; allies get a solid team-colour ring, enemies a dashed one (8 dashes), 0.04 u, all of
    /// radius 0.55 u. Shape differs, not only colour (bible p03/p05). Every ring has a Void Navy toon outline so it reads
    /// on grass, path, stone and the brass hill plate alike.
    ///
    /// How it draws: one dynamic mesh, one quad per actor slot, sized once for World.ActorCapacity (32 at 16v16) and
    /// reused every frame (dead or unused slots collapse to a point). One draw call, no per-frame allocation, no
    /// GameObject per unit. The ring shape is a signed distance in the CM/UnitRing shader, so edges stay crisp at any
    /// zoom, and the stroke has a pixel floor (1080p widths: 2.9 px allies and enemies, 4.4 px yours) so a ring at the far
    /// edge of the perspective view never thins below readable. Radius stays in world units so the ring hugs the feet.
    ///
    /// Reads the Sim only (O-13 positions through SimSpace, ground through GroundHeight); never writes it. Installs itself
    /// next to the sim driver (UnitRingsInstaller), so no shared file changes.
    /// </summary>
    public sealed class UnitRings : MonoBehaviour
    {
        /// <summary>Ring styles, written to the shader as uv1.x.</summary>
        public enum Style { Hidden = -1, Self = 0, Ally = 1, Enemy = 2 }

        /// <summary>Art direction 6.4 sizes, world units.</summary>
        public const float Radius = 0.55f, SelfWidth = 0.06f, TeamWidth = 0.04f, TickLength = 0.13f, TickWidth = 0.06f;
        /// <summary>Dash count of an enemy ring and the share of each period that is drawn.</summary>
        public const int EnemyDashes = 8;
        public const float DashFill = 0.62f;
        /// <summary>Half the quad side: radius + tick + outline headroom.</summary>
        public const float QuadHalf = 0.86f;
        /// <summary>
        /// Height above the ground: over the hill plate (0.05 u thick in ActorViews), plus the depth offset in the shader,
        /// so the ring is never buried on the hill and still hides behind walls, bodies and the Kaiju.
        /// </summary>
        public const float Lift = 0.075f;
        /// <summary>The local player's actor id (World.CreateWorld puts the human first).</summary>
        public const int SelfId = 0;

        public const string ShaderResource = "UnitRings/CMUnitRing";

        private SimDriverBehaviour _driver;
        private World _world;
        private GameConfig _config;
        private Mesh _mesh;
        private Material _material;
        private GameObject _drawObject;
        private Vector3[] _vertices;
        private Vector4[] _uv0;
        private Vector4[] _uv1;
        private Style[] _styles;
        private Vector3[] _centres;
        private int _visible;

        /// <summary>Slots in the pool (World.ActorCapacity of the bound config).</summary>
        public int Capacity => _styles != null ? _styles.Length : 0;

        /// <summary>Rings drawn after the last Sync.</summary>
        public int VisibleCount => _visible;

        /// <summary>The mesh (tests and the capture check read it).</summary>
        public Mesh Mesh => _mesh;

        /// <summary>The shared ring material (null before Bind or under a missing shader).</summary>
        public Material Material => _material;

        /// <summary>Style of slot i after the last Sync.</summary>
        public Style StyleOf(int i) => _styles[i];

        /// <summary>World-space centre of slot i's ring after the last Sync (meaningless when hidden).</summary>
        public Vector3 CentreOf(int i) => _centres[i];

        private void Start()
        {
            if (_world != null) return;
            _driver = GetComponent<SimDriverBehaviour>();
            if (_driver != null && _driver.World != null) Bind(_driver.World, _driver.Config);
            else enabled = false;
        }

        private void LateUpdate() { var cost = CriticalMass.Runtime.Game.FrameCost.Enter(); LateUpdateCost(); CriticalMass.Runtime.Game.FrameCost.Exit(CriticalMass.Runtime.Game.FrameCost.Slot.Rings, cost); }

        void LateUpdateCost()
        {
            if (_world == null) return;
            if (_driver != null && _driver.World != _world) Bind(_driver.World, _driver.Config);
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
            Sync(_driver != null ? _driver.Alpha : 1.0, fwd);
        }

        private void OnDestroy()
        {
            Release(_drawObject);
            Release(_mesh);
            Release(_material);
        }

        private static void Release(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        /// <summary>
        /// Builds the pool for a world: one quad per actor slot, the index buffer once, a fixed bounds box over the map.
        /// Called by Start (from the driver) or directly by tests; calling it again for a new world rebuilds the pool.
        /// </summary>
        public void Bind(World world, GameConfig config)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            int n = World.ActorCapacity(config);
            _styles = new Style[n];
            _centres = new Vector3[n];
            _vertices = new Vector3[n * 4];
            _uv0 = new Vector4[n * 4];
            _uv1 = new Vector4[n * 4];
            var indices = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                _styles[i] = Style.Hidden;
                int v = i * 4, t = i * 6;
                indices[t] = v; indices[t + 1] = v + 1; indices[t + 2] = v + 2;
                indices[t + 3] = v; indices[t + 4] = v + 2; indices[t + 5] = v + 3;
                // Local ring coordinates in world units, fixed per corner.
                _uv0[v] = new Vector4(-QuadHalf, -QuadHalf, 0, 0);
                _uv0[v + 1] = new Vector4(-QuadHalf, QuadHalf, 0, 0);
                _uv0[v + 2] = new Vector4(QuadHalf, QuadHalf, 0, 0);
                _uv0[v + 3] = new Vector4(QuadHalf, -QuadHalf, 0, 0);
            }

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "Unit Rings" };
                _mesh.MarkDynamic();
            }
            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, _uv0);
            _mesh.SetUVs(1, _uv1);
            _mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            MapDef map = world.Map;
            float w = map != null ? map.Width : 256f, h = map != null ? map.Height : 256f;
            Vector3 lo = SimSpace.ToWorld(-4, -4, h + 4), hi = SimSpace.ToWorld(w + 4, 12, -4);
            var b = new Bounds();
            b.SetMinMax(Vector3.Min(lo, hi), Vector3.Max(lo, hi));
            _mesh.bounds = b;

            if (_material == null)
            {
                var shader = UnityEngine.Resources.Load<Shader>(ShaderResource);
                if (shader != null)
                {
                    _material = new Material(shader) { name = "CM UnitRing" };
                    _material.SetColor("_SelfColor", Hex(Palette.Brass));
                    _material.SetColor("_AzureColor", Hex(Palette.Azure));
                    _material.SetColor("_CrimsonColor", Hex(Palette.Crimson));
                    _material.SetColor("_OutlineColor", Hex(Palette.VoidNavy));
                    _material.SetFloat("_Radius", Radius);
                    _material.SetFloat("_SelfWidth", SelfWidth);
                    _material.SetFloat("_TeamWidth", TeamWidth);
                    _material.SetFloat("_TickLength", TickLength);
                    _material.SetFloat("_TickWidth", TickWidth);
                    _material.SetFloat("_Dashes", EnemyDashes);
                    _material.SetFloat("_DashFill", DashFill);
                }
            }

            if (_drawObject == null)
            {
                _drawObject = new GameObject("Unit Rings");
                _drawObject.transform.SetParent(transform, false);
                _drawObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
                var r = _drawObject.AddComponent<MeshRenderer>();
                r.sharedMaterial = _material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.allowOcclusionWhenDynamic = false;
            }
            // The draw object sits at the world origin whatever the parent does (ring vertices are world positions).
            _drawObject.transform.position = Vector3.zero;
            _drawObject.transform.rotation = Quaternion.identity;
            _drawObject.transform.localScale = Vector3.one;
        }

        /// <summary>
        /// One view update: every slot's style and quad from the sim, interpolated by alpha like ActorViews.
        /// cameraForward orients the self ring's ticks to the screen axes. Allocates nothing.
        /// </summary>
        public void Sync(double alpha, Vector3 cameraForward)
        {
            if (_world == null) return;
            World w = _world;
            MapDef map = w.Map;
            Vector2 up = new Vector2(cameraForward.x, cameraForward.z);
            float tickAngle = up.sqrMagnitude > 1e-8f ? Mathf.Atan2(up.y, up.x) : 0f;
            TeamId selfTeam = w.ActorCount > SelfId ? w.Actors[SelfId].Team : TeamId.Azure;
            int visible = 0;
            for (int i = 0; i < _styles.Length; i++)
            {
                int v = i * 4;
                Style s = Style.Hidden;
                Actor a = i < w.ActorCount ? w.Actors[i] : null;
                if (a != null && a.Alive) s = i == SelfId ? Style.Self : a.Team == selfTeam ? Style.Ally : Style.Enemy;
                _styles[i] = s;
                if (s == Style.Hidden)
                {
                    _vertices[v] = _vertices[v + 1] = _vertices[v + 2] = _vertices[v + 3] = Vector3.zero;
                    continue;
                }
                visible++;
                double x = w.PrevX[i] + (w.X[i] - w.PrevX[i]) * alpha;
                double z = w.PrevZ[i] + (w.Z[i] - w.PrevZ[i]) * alpha;
                double gh = map != null ? GroundHeight.At(map, x, z) : 0;
                Vector3 c = SimSpace.ToWorld(x, gh + Lift, z);
                _centres[i] = c;
                _vertices[v] = new Vector3(c.x - QuadHalf, c.y, c.z - QuadHalf);
                _vertices[v + 1] = new Vector3(c.x - QuadHalf, c.y, c.z + QuadHalf);
                _vertices[v + 2] = new Vector3(c.x + QuadHalf, c.y, c.z + QuadHalf);
                _vertices[v + 3] = new Vector3(c.x + QuadHalf, c.y, c.z - QuadHalf);
                var info = new Vector4((float)s, a.Team == TeamId.Azure ? 0f : 1f, tickAngle, 0f);
                _uv1[v] = _uv1[v + 1] = _uv1[v + 2] = _uv1[v + 3] = info;
            }
            _visible = visible;
            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            _mesh.SetVertices(_vertices, 0, _vertices.Length, flags);
            _mesh.SetUVs(1, _uv1, 0, _uv1.Length, flags);
        }

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color c);
            return c;
        }
    }

    /// <summary>
    /// Puts a UnitRings next to every SimDriverBehaviour that appears, without touching GameBootstrap: a hidden watcher
    /// created after the first scene load looks for a driver (every 15 frames while there is none, a cheap null check
    /// once it has one) and adds the component once. Works for the Boot build today and for a world created later on
    /// Play (DG-01).
    /// </summary>
    public static class UnitRingsInstaller
    {
        private static UnitRingsWatcher _watcher;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_watcher != null) return;   // domain reload is off: a watcher may survive between play sessions
            var go = new GameObject("Unit Rings Installer") { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            _watcher = go.AddComponent<UnitRingsWatcher>();
        }

        /// <summary>Adds UnitRings to the driver's GameObject unless it already has one; returns the component.</summary>
        public static UnitRings EnsureOn(SimDriverBehaviour driver)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            UnitRings rings = driver.GetComponent<UnitRings>();
            return rings != null ? rings : driver.gameObject.AddComponent<UnitRings>();
        }
    }

    /// <summary>The installer's watcher (see UnitRingsInstaller).</summary>
    public sealed class UnitRingsWatcher : MonoBehaviour
    {
        private const int SearchEveryFrames = 15;
        private SimDriverBehaviour _driver;
        private int _wait;

        private void Update()
        {
            if (_driver != null) return;
            if (--_wait > 0) return;
            _wait = SearchEveryFrames;
            _driver = FindAnyObjectByType<SimDriverBehaviour>();
            if (_driver != null) UnitRingsInstaller.EnsureOn(_driver);
        }
    }
}
