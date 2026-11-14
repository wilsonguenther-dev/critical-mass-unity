using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.View.Skills
{
    /// <summary>
    /// feat/skill-effects-1: draws SkillFxCore into three fixed-budget meshes (ground decals, solid toon chunks, air
    /// billboards; three draw calls, the MechanicsWorldView pattern), registers the procedural arm overlay and, with
    /// -skillShots, the capture director. The WORLD tab's "Skill effects" switch (on by default) hides the drawing; the
    /// animation cues keep running so no skill fires with the character standing still. Installs itself on the driver
    /// (SkillFxInstaller), so no shared file changes for wiring.
    /// </summary>
    public sealed class SkillFxView : MonoBehaviour
    {
        public const int GroundVerts = 9000, SolidVerts = 24000, AirVerts = 6000;

        public static SkillFxView Active { get; private set; }

        public readonly SkillFxCore Core = new SkillFxCore();
        public readonly SkillPoseOverlay Overlay = new SkillPoseOverlay();

        SimDriverBehaviour _driver;
        World _world;
        FxMeshBuilder _ground, _solid, _air;
        Material _groundMat, _solidMat, _airMat;
        GameObject _groundGo, _solidGo, _airGo;
        bool _wasOff;

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            if (_driver == null || _driver.World == null) { enabled = false; return; }
            _ground = new FxMeshBuilder("Skill Fx Ground", GroundVerts, GroundVerts * 3);
            _solid = new FxMeshBuilder("Skill Fx Solid", SolidVerts, SolidVerts * 2);
            _air = new FxMeshBuilder("Skill Fx Air", AirVerts, AirVerts * 3);
            Shader sh = UnityEngine.Resources.Load<Shader>("CMFxUnlit");
            if (sh == null) sh = Shader.Find("CM/FxUnlit");
            if (sh == null) { enabled = false; return; }
            _groundMat = Mat(sh, "Skill Fx Ground", 0, 0, -2, 2986);
            _solidMat = Mat(sh, "Skill Fx Solid", 2, 1, 0, 2456);
            _airMat = Mat(sh, "Skill Fx Air", 0, 0, 0, 3096);
            _groundGo = Layer("Skill Fx Ground", _ground, _groundMat);
            _solidGo = Layer("Skill Fx Solid", _solid, _solidMat);
            _airGo = Layer("Skill Fx Air", _air, _airMat);
            _world = _driver.World;
            _driver.Ticked -= OnTicked;
            _driver.Ticked += OnTicked;
            PostPoseHooks.Register(Overlay);
            Active = this;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), SkillCapture.Flag) >= 0 && GetComponent<SkillCapture>() == null)
                gameObject.AddComponent<SkillCapture>();
        }

        void OnDestroy()
        {
            if (_driver != null) _driver.Ticked -= OnTicked;
            PostPoseHooks.Unregister(Overlay);
            if (Active == this) Active = null;
            Release(_groundGo); Release(_solidGo); Release(_airGo);
            Release(_groundMat); Release(_solidMat); Release(_airMat);
            if (_ground != null) Release(_ground.Mesh);
            if (_solid != null) Release(_solid.Mesh);
            if (_air != null) Release(_air.Mesh);
        }

        static void Release(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        static Material Mat(Shader sh, string name, int cull, int zwrite, int offset, int queue)
        {
            var m = new Material(sh) { name = name, renderQueue = queue };
            m.SetFloat("_Cull", cull);
            m.SetFloat("_ZWrite", zwrite);
            m.SetFloat("_OffsetUnits", offset);
            return m;
        }

        GameObject Layer(string n, FxMeshBuilder b, Material m)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = b.Mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        void OnTicked(World w)
        {
            _world = w;
            Core.Read(w);
        }

        void LateUpdate()
        {
            if (_driver == null || _ground == null) return;
            World w = _driver.World;
            if (w == null) return;
            if (!WorldFeedbackSettings.SkillEffects)
            {
                if (!_wasOff)
                {
                    _ground.Clear(); _solid.Clear(); _air.Clear();
                    _ground.Upload(); _solid.Upload(); _air.Upload();
                    _wasOff = true;
                }
                return;
            }
            _wasOff = false;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            Vector3 right = cam != null ? cam.transform.right : Vector3.right;
            Vector3 up = cam != null ? cam.transform.up : Vector3.up;
            _ground.Clear();
            _solid.Clear();
            _air.Clear();
            Core.Build(w, _driver.Alpha, right, up, _ground, _solid, _air);
            _ground.Upload();
            _solid.Upload();
            _air.Upload();
        }

        /// <summary>Vertices used last frame per layer (capture log).</summary>
        public string Budget => _ground == null ? "-" : _ground.VertCount + "/" + _solid.VertCount + "/" + _air.VertCount + " dropped " + (_ground.Dropped + _solid.Dropped + _air.Dropped);
    }

    /// <summary>Puts a SkillFxView on the sim driver of every match (the MechanicsWorldViewInstaller pattern).</summary>
    public static class SkillFxInstaller
    {
        static SkillFxWatcher _watcher;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (_watcher != null) return;
            var go = new GameObject("Skill Fx Installer") { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            _watcher = go.AddComponent<SkillFxWatcher>();
        }

        public static SkillFxView EnsureOn(SimDriverBehaviour driver)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            SkillFxView v = driver.GetComponent<SkillFxView>();
            if (v == null) v = driver.gameObject.AddComponent<SkillFxView>();
            return v;
        }
    }

    /// <summary>The installer's watcher: finds the driver every 15 frames until it exists.</summary>
    public sealed class SkillFxWatcher : MonoBehaviour
    {
        const int SearchEveryFrames = 15;
        SimDriverBehaviour _driver;
        int _wait;

        void Update()
        {
            if (_driver != null) return;
            if (--_wait > 0) return;
            _wait = SearchEveryFrames;
            _driver = FindAnyObjectByType<SimDriverBehaviour>();
            if (_driver != null) SkillFxInstaller.EnsureOn(_driver);
        }
    }
}
