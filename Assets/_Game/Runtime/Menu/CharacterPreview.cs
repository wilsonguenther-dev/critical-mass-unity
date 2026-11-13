using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Progression;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using UnityEngine;

namespace CriticalMass.Runtime.Menu
{
    /// <summary>
    /// The character panel's turntable: one instance of the class model far below the map on its own layer, a second
    /// camera that sees only that layer, and one small render texture. Built when the panel opens, everything (texture,
    /// camera, model, cosmetic pieces) released when it closes; nothing is created per frame. The model idles (sim-free
    /// view time) and turns 18 degrees a second. Returns false from Show when the class has no model yet (the panel then
    /// shows the portrait).
    /// </summary>
    public sealed class CharacterPreview
    {
        /// <summary>Layer only the preview camera draws (removed from the main camera's mask while the preview lives).</summary>
        public const int Layer = 31;
        public const int TexWidth = 512, TexHeight = 640;
        static readonly Vector3 Stage = new Vector3(0f, -300f, 0f);
        const float TurnDegPerSecond = 18f;

        RenderTexture _rt;
        UnityEngine.Camera _cam;
        GameObject _rig;
        ModelView _model;
        CosmeticsDresser _dresser;
        Transform _holder;
        ClassId _cls;
        TeamId _team;
        float _yaw = 200f, _idle, _heldUntil = -1f;

        /// <summary>Owner walk 1: turn the model by hand (a drag on the Barracks preview); the slow turn waits 2 s after.</summary>
        public void Turn(float deg)
        {
            _yaw = (_yaw + deg + 360f) % 360f;
            _heldUntil = Time.unscaledTime + 2f;
            if (_model != null) _model.Transform.localRotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        public float Yaw => _yaw;
        int _mainMaskRemoved = -1;

        public Texture Texture => _rt;
        public bool Live => _model != null;

        public bool Show(ClassId cls, TeamId team, ClassLoadout loadout, int level)
        {
            string key = ArtMapping.ClassModel(cls);
            ArtEntry entry = key != null ? ArtCatalog.Load()?.Find(key) : null;
            if (entry == null || ArtMapping.Spec(key) == null)
            {
                DropModel();
                return false;
            }
            EnsureStage();
            if (_model == null || _cls != cls || _team != team || _model.Entry != entry)
            {
                DropModel();
                _model = new ModelView(entry, _rig.transform, "Preview " + key);
                _model.SetMaterial(ArtMaterials.ForTeam(_model.Spec, team));
                _model.Transform.localPosition = Vector3.zero;
                _cls = cls;
                _team = team;
            }
            _model.SetActive(true);
            Cosmetics.Apply(_dresser, _model.Transform, cls, loadout, level);
            SetLayer(_model.Root.transform, Layer);
            return true;
        }

        void EnsureStage()
        {
            if (_rig != null) return;
            _rig = new GameObject("Character Preview");
            _rig.transform.position = Stage;
            var holder = new GameObject("Preview Pool");
            holder.transform.SetParent(_rig.transform, false);
            holder.SetActive(false);
            _holder = holder.transform;
            _dresser = new CosmeticsDresser(_holder);

            _rt = new RenderTexture(TexWidth, TexHeight, 24, RenderTextureFormat.ARGB32) { name = "Character Preview", antiAliasing = 2 };
            _rt.Create();
            var camGo = new GameObject("Preview Camera");
            camGo.transform.SetParent(_rig.transform, false);
            _cam = camGo.AddComponent<UnityEngine.Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = CMTokens.PanelNavy.Color;
            _cam.cullingMask = 1 << Layer;
            _cam.fieldOfView = 26f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 30f;
            _cam.targetTexture = _rt;
            _cam.transform.localPosition = new Vector3(0f, 1.45f, 5.4f);
            _cam.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, 1.0f, 0f) - _cam.transform.localPosition, Vector3.up);

            UnityEngine.Camera main = UnityEngine.Camera.main;
            if (main != null && (main.cullingMask & (1 << Layer)) != 0)
            {
                _mainMaskRemoved = main.GetInstanceID();
                main.cullingMask &= ~(1 << Layer);
            }
        }

        /// <summary>Per-frame view work while visible: turn and idle (no allocation).</summary>
        public void Tick(float dt)
        {
            if (_model == null) return;
            if (Time.unscaledTime >= _heldUntil) _yaw = (_yaw + TurnDegPerSecond * dt) % 360f;
            _idle += dt;
            _model.Transform.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            _model.BeginPose();
            _model.Pose(ArtMapping.Idle, _idle, 1f, true);
            _model.Evaluate();
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        void DropModel()
        {
            _dresser?.Detach();
            if (_model != null) _model.Dispose();
            _model = null;
        }

        /// <summary>Integration batch 5: Destroy in play, DestroyImmediate in edit mode (EditMode tests open and close the Barracks).</summary>
        static void Kill(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        /// <summary>Destroys the model, the pieces, the camera and the render texture.</summary>
        public void Release()
        {
            DropModel();
            _dresser?.Clear();
            _dresser = null;
            if (_cam != null) _cam.targetTexture = null;
            if (_rt != null)
            {
                _rt.Release();
                Kill(_rt);
            }
            _rt = null;
            if (_rig != null) Kill(_rig);
            _rig = null;
            _cam = null;
            UnityEngine.Camera main = UnityEngine.Camera.main;
            if (main != null && main.GetInstanceID() == _mainMaskRemoved) main.cullingMask |= 1 << Layer;
            _mainMaskRemoved = -1;
        }
    }
}
