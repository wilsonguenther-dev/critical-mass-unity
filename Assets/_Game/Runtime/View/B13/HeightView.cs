using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.B13
{
    /// <summary>
    /// b15 terrain height: the occlusion indicator. A living character below ground level (in a dug pit, TerrainHeight level
    /// below 0) gets a small team-coloured pip a little above the pit's rim, straight over it, so the high camera still reads
    /// who is in the pit and which team when the pit wall hides the body. Visual only (reads the sim, never writes it). No
    /// per-frame allocation: one pooled primitive per actor slot made at the first frame of a match, one shared unlit
    /// material, one MaterialPropertyBlock per team colour. Adds one draw call per character in a pit (none otherwise).
    /// </summary>
    public sealed class HeightView : MonoBehaviour
    {
        public const float PipSize = 0.32f, PipFlat = 0.55f, AboveRim = 0.35f;
        static readonly Color AzureTint = new Color(0.35f, 0.70f, 1.00f), CrimsonTint = new Color(1.00f, 0.38f, 0.32f);

        /// <summary>Pips shown in the last frame (tests and captures read it).</summary>
        public static int PipsShown { get; private set; }

        SimDriverBehaviour _driver;
        Transform[] _pips = new Transform[0];
        Renderer[] _renderers = new Renderer[0];
        sbyte[] _team = new sbyte[0];
        MaterialPropertyBlock _azure, _crimson;
        Material _mat;

        void Awake()
        {
            _driver = GetComponentInParent<SimDriverBehaviour>();
            _azure = new MaterialPropertyBlock();
            _azure.SetColor("_BaseColor", AzureTint);
            _azure.SetColor("_Color", AzureTint);
            _crimson = new MaterialPropertyBlock();
            _crimson.SetColor("_BaseColor", CrimsonTint);
            _crimson.SetColor("_Color", CrimsonTint);
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            _mat = sh != null ? new Material(sh) { name = "b15 height pip" } : null;
        }

        void Grow(int n)
        {
            // warm-up only: the actor count is fixed per match
            int old = _pips.Length;
            System.Array.Resize(ref _pips, n);
            System.Array.Resize(ref _renderers, n);
            System.Array.Resize(ref _team, n);
            for (int i = old; i < n; i++)
            {
                GameObject q = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                q.name = "Height Pip " + i;
                Collider c = q.GetComponent<Collider>();
                if (c != null) Destroy(c);
                q.transform.SetParent(transform, false);
                q.transform.localScale = new Vector3(PipSize, PipSize * PipFlat, PipSize);
                Renderer r = q.GetComponent<Renderer>();
                if (_mat != null) r.sharedMaterial = _mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                _pips[i] = q.transform;
                _renderers[i] = r;
                _team[i] = -1;
                q.SetActive(false);
            }
        }

        void LateUpdate()
        {
            if (_driver == null) _driver = GetComponentInParent<SimDriverBehaviour>();
            World w = _driver != null ? _driver.World : null;
            PipsShown = 0;
            if (w == null || !TerrainHeight.On(w) || w.Depth.Height == null)
            {
                for (int i = 0; i < _pips.Length; i++) if (_pips[i].gameObject.activeSelf) _pips[i].gameObject.SetActive(false);
                return;
            }
            if (_pips.Length < w.ActorCount) Grow(w.ActorCount);
            double alpha = _driver.Alpha;
            sbyte[] level = w.Depth.Height.Level;
            for (int id = 0; id < _pips.Length; id++)
            {
                bool show = id < w.ActorCount && w.Actors[id].Alive && level[id] != TerrainHeightState.NoLevel && level[id] < 0;
                GameObject go = _pips[id].gameObject;
                if (!show)
                {
                    if (go.activeSelf) go.SetActive(false);
                    continue;
                }
                double x = w.PrevX[id] + (w.X[id] - w.PrevX[id]) * alpha;
                double z = w.PrevZ[id] + (w.Z[id] - w.PrevZ[id]) * alpha;
                double rim = GroundHeight.IslandHeightAt(w.Map, x, z);
                _pips[id].localPosition = SimSpace.ToWorld(x, rim + AboveRim, z);
                sbyte team = (sbyte)(w.Actors[id].Team == TeamId.Azure ? 0 : 1);
                if (_team[id] != team)
                {
                    _team[id] = team;
                    _renderers[id].SetPropertyBlock(team == 0 ? _azure : _crimson);
                }
                if (!go.activeSelf) go.SetActive(true);
                PipsShown++;
            }
        }
    }
}
