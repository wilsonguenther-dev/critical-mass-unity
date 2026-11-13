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
    /// <para>Creator acceptance: while a pillar stands, two flat ground arrows show which ways it can fall (warm = ramp,
    /// red = wall) and every counted hit wobbles it, so a player sees it is a thing to knock over and which way.</para>
    /// <para>Self-installs on the game's SimDriverBehaviour object after every scene load (the pause-menu installer pattern).
    /// Everything is built once per map and reused: no per-frame allocation.</para>
    /// </summary>
    [CriticalMass.Runtime.Flow.GameOverlay(Order = 21, Scope = CriticalMass.Runtime.Flow.OverlayScope.Always)]   // installed once by GameBootstrap through the overlay registry
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
        // Creator acceptance: which way each pillar can fall (two ground arrows while it stands) and a wobble per counted hit.
        private const string RampHex = "#F2C14E";
        private const string WallHex = "#C8553D";
        private const float WobbleSeconds = 0.4f;
        private Transform[] _markers = new Transform[0];
        private byte[] _lastHp = new byte[0];
        private float[] _hitAt = new float[0];

        // Integration batch 2: installed by GameBootstrap through the DG-01 overlay registry ([GameOverlay]); Attach stays for tests.
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

            // Fall markers: a warm arrow on the ground toward the ramp side, a red one toward the wall side.
            Material rampMat = ToonMaterials.Prop(RampHex);
            Material wallMat = ToonMaterials.Prop(WallHex);
            _markers = new Transform[decs.Length];
            _lastHp = new byte[decs.Length];
            _hitAt = new float[decs.Length];
            for (int k = 0; k < decs.Length; k++)
            {
                var m = new GameObject("Pillar " + k + " fall marks").transform;
                m.SetParent(_root, false);
                Arrow(m, decs[k].Ramp, rampMat, "Ramp arrow");
                Arrow(m, decs[k].Block, wallMat, "Wall arrow");
                _markers[k] = m;
                _lastHp[k] = w.Topple.Hp != null && k < w.Topple.Hp.Length ? w.Topple.Hp[k] : (byte)0;
                _hitAt[k] = -10f;
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

        /// <summary>A flat ground arrow from the pillar foot along dir: a shaft and a diamond head (no colliders).</summary>
        private static void Arrow(Transform parent, CellDir dir, Material mat, string name)
        {
            var a = new GameObject(name).transform;
            a.SetParent(parent, false);
            var dirWorld = new Vector3(dir.Dx, 0, -dir.Dz);
            if (dirWorld.sqrMagnitude < 1e-4f) dirWorld = Vector3.forward;
            a.localRotation = Quaternion.LookRotation(dirWorld.normalized, Vector3.up);
            GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(shaft.GetComponent<Collider>());
            shaft.name = "Shaft";
            shaft.transform.SetParent(a, false);
            shaft.transform.localScale = new Vector3(0.32f, 0.06f, 1.5f);
            shaft.transform.localPosition = new Vector3(0, 0.05f, 1.35f);
            shaft.GetComponent<Renderer>().sharedMaterial = mat;
            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(head.GetComponent<Collider>());
            head.name = "Head";
            head.transform.SetParent(a, false);
            head.transform.localScale = new Vector3(0.75f, 0.06f, 0.75f);
            head.transform.localPosition = new Vector3(0, 0.05f, 2.25f);
            head.transform.localRotation = Quaternion.Euler(0, 45f, 0);
            head.GetComponent<Renderer>().sharedMaterial = mat;
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
                bool standing = mode == Topple.Standing;
                if (k < _markers.Length)
                {
                    Transform mk = _markers[k];
                    if (mk.gameObject.activeSelf != standing) mk.gameObject.SetActive(standing);
                    if (standing) mk.localPosition = p.localPosition;
                }
                if (standing)
                {
                    // A counted hit (Hp dropped) wobbles the column for WobbleSeconds: view-only, wall time.
                    byte hp = w.Topple.Hp != null && k < w.Topple.Hp.Length ? w.Topple.Hp[k] : (byte)0;
                    if (k < _lastHp.Length)
                    {
                        if (hp < _lastHp[k]) _hitAt[k] = Time.unscaledTime;
                        _lastHp[k] = hp;
                    }
                    float since = k < _hitAt.Length ? Time.unscaledTime - _hitAt[k] : 10f;
                    if (since >= 0f && since < WobbleSeconds)
                    {
                        float amp = 9f * (1f - since / WobbleSeconds);
                        p.localRotation = Quaternion.Euler(amp * Mathf.Sin(since * 40f), 0f, amp * 0.6f * Mathf.Cos(since * 33f));
                    }
                    else p.localRotation = Quaternion.identity;
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
