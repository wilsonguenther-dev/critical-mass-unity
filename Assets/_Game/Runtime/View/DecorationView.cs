using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CriticalMass.Runtime.View
{
    /// <summary>
    /// DG-09 minimal view hook (placeholder look; the effects and art come later). Read-only on the Sim.
    /// <para>Knock-down pillars: one stone column per MapDef.Decorations entry at its base cell; once the sim knocks it over
    /// it tips about its base toward the fall direction (World.Topple: Ramp or Block) over Topple.FallTicks sim ticks, eased
    /// in, and ends lying across its footprint. Timing comes from sim ticks (Topple.FallPhase at the driver's alpha).</para>
    /// <para>Carried blocks: a small stack of dirt cubes (one per block, MinerTerraform.CarryMax at most) floats over each
    /// carrying miner; lighter cubes in Place mode, darker in Dig mode, so the human sees what F will do.</para>
    /// <para>Holes and placed cells need nothing here: WorldView re-meshes ground chunks and GridChunkMeshView draws every
    /// changed cell when World.GridVersion moves (the existing dig path). Ramp cells draw as Bridge, a block as Wall.</para>
    /// <para>Self-installs on the game's SimDriverBehaviour object after every scene load (the pause-menu installer pattern).
    /// Everything is built once per map and reused: no per-frame allocation.</para>
    /// </summary>
    public sealed class DecorationView : MonoBehaviour
    {
        /// <summary>Column height in world units [guess: the footprint length plus half a cell, so the fallen column covers it].</summary>
        public const float PillarHeight = 4.4f;
        public const float PillarWidth = 0.9f;
        private const float CubeSize = 0.32f;
        private const float CarryLift = 2.0f;
        private const string StoneHex = "#8C8578";
        private const string DigHex = "#6B4F35";
        private const string PlaceHex = "#B9925E";

        /// <summary>True while a view is attached to the running game.</summary>
        public static bool IsOpen => _instance != null;
        private static DecorationView _instance;

        private SimDriverBehaviour _driver;
        private World _world;
        private MapDef _map;
        private Transform _root;
        private Transform[] _pivots = new Transform[0];
        private Renderer[] _cubes = new Renderer[0];
        private int _cubesPerActor;
        private Material _digMat;
        private Material _placeMat;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Attach();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach();

        /// <summary>Adds the view to the game's driver object (creating the game first when the Boot scene is open); returns it, or null without a game.</summary>
        public static DecorationView Attach()
        {
            GameBootstrap.Build(SceneManager.GetActiveScene());
            SimDriverBehaviour driver = Object.FindAnyObjectByType<SimDriverBehaviour>();
            if (driver == null) return null;
            DecorationView v = driver.GetComponent<DecorationView>();
            return v != null ? v : driver.gameObject.AddComponent<DecorationView>();
        }

        private void Awake()
        {
            _instance = this;
            _driver = GetComponent<SimDriverBehaviour>();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_root != null) Destroy(_root.gameObject);
        }

        private void LateUpdate()
        {
            if (_driver == null) _driver = GetComponent<SimDriverBehaviour>();
            World w = _driver != null ? _driver.World : null;
            if (w == null) return;
            if (w != _world || w.Map != _map) Build(w);
            float alpha = (float)_driver.Alpha;
            DrawPillars(w, alpha);
            DrawCarry(w, alpha);
        }

        private void Build(World w)
        {
            if (_root != null) Destroy(_root.gameObject);
            _world = w;
            _map = w.Map;
            _root = new GameObject("DG-09 Decorations").transform;
            _root.SetParent(transform, false);
            Material stone = ToonMaterials.Prop(StoneHex);
            _digMat = ToonMaterials.Prop(DigHex);
            _placeMat = ToonMaterials.Prop(PlaceHex);

            DecorationDef[] decs = _map.Decorations ?? new DecorationDef[0];
            _pivots = new Transform[decs.Length];
            for (int k = 0; k < decs.Length; k++)
            {
                var pivot = new GameObject("Pillar " + k).transform;
                pivot.SetParent(_root, false);
                GameObject col = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(col.GetComponent<Collider>());
                col.name = "Column";
                col.transform.SetParent(pivot, false);
                // A Unity cylinder is 2 units tall: scale y by half the height, lift it so its foot sits on the pivot.
                col.transform.localScale = new Vector3(PillarWidth, PillarHeight * 0.5f, PillarWidth);
                col.transform.localPosition = new Vector3(0, PillarHeight * 0.5f, 0);
                col.GetComponent<Renderer>().sharedMaterial = stone;
                _pivots[k] = pivot;
            }

            _cubesPerActor = Terraform.CarryMax(w.Config);
            int cap = World.ActorCapacity(w.Config);
            _cubes = new Renderer[cap * _cubesPerActor];
            for (int i = 0; i < _cubes.Length; i++)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(cube.GetComponent<Collider>());
                cube.name = "Carried block";
                cube.transform.SetParent(_root, false);
                cube.transform.localScale = new Vector3(CubeSize, CubeSize, CubeSize);
                Renderer r = cube.GetComponent<Renderer>();
                r.sharedMaterial = _digMat;
                r.enabled = false;
                _cubes[i] = r;
            }
        }

        private void DrawPillars(World w, float alpha)
        {
            DecorationDef[] decs = _map.Decorations;
            if (decs == null) return;
            for (int k = 0; k < _pivots.Length && k < decs.Length; k++)
            {
                DecorationDef d = decs[k];
                double bx = d.Cx + 0.5, bz = d.Cz + 0.5;
                Transform p = _pivots[k];
                p.localPosition = SimSpace.ToWorld(bx, GroundHeight.At(_map, bx, bz), bz);
                int mode = w.Topple.Fallen[k];
                if (mode == Topple.Standing)
                {
                    p.localRotation = Quaternion.identity;
                    continue;
                }
                CellDir dir = Topple.Dir(d, mode);
                // Sim (dx, dz) to world: SimSpace negates z. Rotating up about (up x dir) by +angle tips the top toward dir.
                var dirWorld = new Vector3(dir.Dx, 0, -dir.Dz);
                double u = Topple.FallPhase(w, k, alpha);
                float eased = (float)(u * u); // gravity-like ease-in
                p.localRotation = Quaternion.AngleAxis(90f * eased, Vector3.Cross(Vector3.up, dirWorld));
            }
        }

        private void DrawCarry(World w, float alpha)
        {
            int cap = _cubes.Length / (_cubesPerActor > 0 ? _cubesPerActor : 1);
            for (int id = 0; id < cap; id++)
            {
                int carried = id < w.ActorCount && w.Actors[id].Alive ? w.Terraform.Carried[id] : 0;
                Material m = Terraform.ModeOf(w, id) == TerraformMode.Place ? _placeMat : _digMat;
                double x = 0, z = 0;
                float y = 0;
                if (carried > 0)
                {
                    x = w.PrevX[id] + (w.X[id] - w.PrevX[id]) * alpha;
                    z = w.PrevZ[id] + (w.Z[id] - w.PrevZ[id]) * alpha;
                    y = (float)GroundHeight.At(_map, x, z) + CarryLift;
                }
                for (int b = 0; b < _cubesPerActor; b++)
                {
                    Renderer r = _cubes[id * _cubesPerActor + b];
                    bool on = b < carried;
                    if (r.enabled != on) r.enabled = on;
                    if (!on) continue;
                    if (r.sharedMaterial != m) r.sharedMaterial = m;
                    r.transform.localPosition = SimSpace.ToWorld(x, y + b * (CubeSize + 0.06f), z);
                }
            }
        }
    }
}
