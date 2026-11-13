using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.View;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CriticalMass.Runtime.Codex
{
    /// <summary>
    /// The 8 Basin Relics in the world (DG-25): one small brass gem per RelicPlacement spot, bobbing and turning slowly
    /// above the ground, for every relic not yet found. Walking over one (actor 0 alive within PickupRadius) marks it in
    /// CodexProgress and opens its lore card. View side only: it reads World positions and never writes the sim; the
    /// gems are built once (9 objects at most) and only toggled afterwards.
    /// </summary>
    public sealed class RelicField : MonoBehaviour
    {
        public const double PickupRadius = 1.1;
        const float HoverHeight = 0.8f, BobAmplitude = 0.12f, BobHz = 0.6f, SpinDegPerSecond = 50f;

        SimDriverBehaviour _driver;
        MapDef _map;
        RelicPlacement.Spot[] _spots;
        GameObject[] _views;
        float[] _baseY;

        /// <summary>The spots in use (null before the world exists).</summary>
        public RelicPlacement.Spot[] Spots => _spots;

        /// <summary>Raised with the relic index when the player picks one up.</summary>
        public static event System.Action<int> PickedUp;

        void LateUpdate()
        {
            if (_driver == null) _driver = GetComponent<SimDriverBehaviour>();
            if (_driver == null || _driver.World == null) return;
            World w = _driver.World;
            if (!ReferenceEquals(_map, w.Map)) Setup(w);
            Animate();
            Collect(w);
        }

        void Setup(World w)
        {
            if (_views != null) for (int i = 0; i < _views.Length; i++) if (_views[i] != null) Destroy(_views[i]);
            _map = w.Map;
            _spots = RelicPlacement.Place(w.Map);
            _views = new GameObject[_spots.Length];
            _baseY = new float[_spots.Length];
            for (int i = 0; i < _spots.Length; i++)
            {
                double x = _spots[i].X, z = _spots[i].Z;
                float y = (float)GroundHeight.At(w.Map, x, z) + HoverHeight;
                _baseY[i] = y;
                _views[i] = RelicViews.Make("Relic " + CodexProgress.IdOf(i), Palette.Brass, transform, 1f);
                _views[i].transform.position = SimSpace.ToWorld(x, y, z);
                _views[i].SetActive(!CodexProgress.IsFound(i));
            }
        }

        void Animate()
        {
            float t = Time.time;
            for (int i = 0; i < _views.Length; i++)
            {
                GameObject v = _views[i];
                if (v == null || !v.activeSelf) continue;
                Vector3 p = v.transform.position;
                p.y = _baseY[i] + BobAmplitude * Mathf.Sin((t * BobHz + i * 0.37f) * Mathf.PI * 2f);
                v.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, t * SpinDegPerSecond + i * 40f, 0f));
            }
        }

        void Collect(World w)
        {
            const int me = HudBinder.LocalActor;
            if (w.ActorCount <= me || !w.Actors[me].Alive) return;
            double px = w.X[me], pz = w.Z[me], r2 = PickupRadius * PickupRadius;
            for (int i = 0; i < _spots.Length; i++)
            {
                if (_views[i] == null || !_views[i].activeSelf) continue;
                double dx = px - _spots[i].X, dz = pz - _spots[i].Z;
                if (dx * dx + dz * dz > r2) continue;
                _views[i].SetActive(false);
                if (!CodexProgress.Mark(i)) continue;
                LoreCard.ShowRelic(CodexProgress.IdOf(i), "BASIN RELIC   " + CodexProgress.CountLabel(CodexProgress.FoundCount));
                PickedUp?.Invoke(i);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            PauseMenu.AddPage(new PauseMenu.ExtraPage
            {
                Label = CodexPage.Label,
                Title = CodexPage.Title,
                Build = CodexPage.Build,
                Opened = CodexPage.Refresh,
            });
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Attach(SceneManager.GetActiveScene());
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach(scene);

        /// <summary>Adds the relic field to the game object; null when there is no game in the scene.</summary>
        public static RelicField Attach(Scene scene)
        {
            PauseMenu menu = MenuInstaller.Attach(scene);
            if (menu == null) return null;
            RelicField f = menu.GetComponent<RelicField>();
            return f != null ? f : menu.gameObject.AddComponent<RelicField>();
        }
    }

    /// <summary>The relic gem: a flat-shaded octahedron (8 faces), toon material with a pivot outline (convex). Mesh shared.</summary>
    public static class RelicViews
    {
        static Mesh _gem;

        public static GameObject Make(string name, string hex, Transform parent, float scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(scale, scale, scale);
            go.AddComponent<MeshFilter>().sharedMesh = Gem();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ToonMaterials.Prop(hex, true);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go;
        }

        public static Mesh Gem()
        {
            if (_gem != null) return _gem;
            const float r = 0.24f, top = 0.42f, bottom = -0.3f;
            var ring = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f;
                ring[i] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            }
            var up = new Vector3(0f, top, 0f);
            var down = new Vector3(0f, bottom, 0f);
            var v = new Vector3[24];
            var n = new Vector3[24];
            var tris = new int[24];
            int k = 0;
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = ring[i], b = ring[(i + 1) % 4];
                k = Face(v, n, tris, k, up, b, a);
                k = Face(v, n, tris, k, down, a, b);
            }
            _gem = new Mesh { name = "Relic Gem" };
            _gem.vertices = v;
            _gem.normals = n;
            _gem.triangles = tris;
            _gem.RecalculateBounds();
            return _gem;
        }

        static int Face(Vector3[] v, Vector3[] n, int[] t, int k, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            v[k] = a; v[k + 1] = b; v[k + 2] = c;
            n[k] = n[k + 1] = n[k + 2] = normal;
            t[k] = k; t[k + 1] = k + 1; t[k + 2] = k + 2;
            return k + 3;
        }
    }
}
