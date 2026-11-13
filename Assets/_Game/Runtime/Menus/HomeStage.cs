using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Progression;
using CriticalMass.Runtime.View;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using UnityEngine;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>
    /// The home screen's diorama: the player's fighter (last used class, team material, their Barracks cosmetics) idling on
    /// a small stone plinth with a hill plate in the team colour, the Kaiju egg on its own pedestal behind, all far below the
    /// map on the class preview layer and drawn by one camera into one render texture. The same parts the class preview and
    /// the Barracks turntable use (ArtCatalog, ModelView, ArtMaterials, Cosmetics); nothing is created per frame. The fighter
    /// turns to face the cursor (or the right stick) and drifts back to centre when nothing moves. View only: it never reads
    /// or writes the sim. With no graphics device (batch tests) it builds nothing.
    /// </summary>
    public sealed class HomeStage
    {
        /// <summary>The class preview's layer (ClassPreview.Layer): its camera sits 10000 units away with a 60 unit far plane.</summary>
        public const int Layer = ClassPreview.Layer;
        public const int TexW = 1280, TexH = 708;
        static readonly Vector3 Origin = new Vector3(-5000f, -800f, 5000f);
        /// <summary>Most the fighter turns toward the cursor either way.</summary>
        public const float LookDeg = 50f;
        /// <summary>Added to the facing if a model's front turns out to point away from the camera (integrator check).</summary>
        public const float FacingOffsetDeg = 0f;
        const float TurnSharpness = 6f, IdleReturnSeconds = 3f;
        static readonly Vector3 FighterAt = new Vector3(-0.55f, 0.06f, 0.35f);
        static readonly Vector3 EggAt = new Vector3(1.55f, 0.24f, -1.15f);

        GameObject _rig;
        UnityEngine.Camera _cam;
        RenderTexture _rt;
        Transform _holder;
        CosmeticsDresser _dresser;
        ModelView _fighter, _egg;
        Renderer _plate;
        ClassId _cls;
        TeamId _team;
        float _yaw, _idle, _stillFor;
        Vector2 _lastPointer;

        public Texture Texture => _rt;
        public bool HasFighter => _fighter != null;
        public bool HasEgg => _egg != null;
        public bool Built => _rig != null;

        static bool HasGraphics => SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;

        /// <summary>Shows cls for team; false when there is no stage (no graphics) or the class has no model yet (portrait instead).</summary>
        public bool Show(ClassId cls, TeamId team)
        {
            if (!Application.isPlaying || !HasGraphics) return false;
            EnsureRig();
            ArtCatalog cat = ArtCatalog.Load();
            if (_egg == null && cat != null) _egg = MakeModel(cat, ArtMapping.KaijuEgg, "Home Egg", EggAt, 0f);
            if (_egg != null) _egg.SetMaterial(ArtMaterials.ForGlow(_egg.Spec, ArtMapping.GlowNeutral));

            if (_plate != null) _plate.sharedMaterial = ToonMaterials.Prop(team == TeamId.Azure ? Palette.Azure : Palette.Crimson);
            string key = ArtMapping.ClassModel(cls);
            if (_fighter == null || _cls != cls || _team != team)
            {
                DropFighter();
                if (key != null && cat != null) _fighter = MakeModel(cat, key, "Home Fighter " + key, FighterAt, 0f);
                if (_fighter != null) _fighter.SetMaterial(ArtMaterials.ForTeam(_fighter.Spec, team));
                _cls = cls;
                _team = team;
            }
            if (_fighter != null)
            {
                PlayerProfile p = Profile.Current;
                Cosmetics.Apply(_dresser, _fighter.Transform, cls, p != null ? p.Loadout(cls) : null, p != null ? p.Level : 1);
                SetLayer(_fighter.Root.transform, Layer);
            }
            SetActive(true);
            return _fighter != null;
        }

        ModelView MakeModel(ArtCatalog cat, string key, string name, Vector3 at, float yaw)
        {
            ArtEntry e = cat.Find(key);
            if (e == null || ArtMapping.Spec(key) == null) return null;
            var m = new ModelView(e, _rig.transform, name);
            m.Transform.localPosition = at;
            m.SetFacing(Quaternion.Euler(0f, yaw + FacingOffsetDeg, 0f));
            m.SetActive(true);
            SetLayer(m.Root.transform, Layer);
            return m;
        }

        void EnsureRig()
        {
            if (_rig != null) return;
            _rig = new GameObject("Home Stage");
            _rig.transform.position = Origin;
            var holder = new GameObject("Home Stage Pool");
            holder.transform.SetParent(_rig.transform, false);
            holder.SetActive(false);
            _holder = holder.transform;
            _dresser = new CosmeticsDresser(_holder);

            Prim(PrimitiveType.Cylinder, "Plinth", new Vector3(0f, -0.2f, 0f), new Vector3(4.6f, 0.2f, 4.6f), Palette.Stone);
            _plate = Prim(PrimitiveType.Cylinder, "Hill Plate", new Vector3(-0.2f, 0.03f, 0.2f), new Vector3(2.8f, 0.03f, 2.8f), Palette.Azure);
            Prim(PrimitiveType.Cylinder, "Egg Pedestal", new Vector3(EggAt.x, 0.12f, EggAt.z), new Vector3(1.0f, 0.12f, 1.0f), Palette.Stone);

            _rt = new RenderTexture(TexW, TexH, 24, RenderTextureFormat.ARGB32) { name = "Home Stage", antiAliasing = 4 };
            _rt.Create();
            var camGo = new GameObject("Home Stage Camera");
            camGo.transform.SetParent(_rig.transform, false);
            _cam = camGo.AddComponent<UnityEngine.Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.Lerp(CMTokens.PanelNavy.Color, CMTokens.VoidNavy.Color, 0.35f);
            _cam.cullingMask = 1 << Layer;
            _cam.fieldOfView = 27f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 40f;
            _cam.targetTexture = _rt;
            _cam.transform.localPosition = new Vector3(0.25f, 2.1f, 7.4f);
            _cam.transform.localRotation = Quaternion.LookRotation(new Vector3(0.35f, 0.95f, 0f) - _cam.transform.localPosition, Vector3.up);
        }

        Renderer Prim(PrimitiveType type, string name, Vector3 at, Vector3 scale, string hex)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Collider col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.SetParent(_rig.transform, false);
            go.transform.localPosition = at;
            go.transform.localScale = scale;
            go.layer = Layer;
            Renderer r = go.GetComponent<Renderer>();
            r.sharedMaterial = ToonMaterials.Prop(hex);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        public void SetActive(bool on)
        {
            if (_rig != null) _rig.SetActive(on);
            if (_cam != null) _cam.enabled = on && HasGraphics;
        }

        /// <summary>
        /// Per-frame view work while the home screen shows: idle poses, and the fighter's turn toward pointer (screen x of the
        /// pointer relative to the stage image, -1 left .. 1 right; null when the pointer is outside it) or the stick.
        /// </summary>
        public void Tick(float dt, Vector2 pointer, float pointerX, float stickX)
        {
            if (_rig == null) return;
            _idle += dt;
            if ((pointer - _lastPointer).sqrMagnitude > 4f) _stillFor = 0f;
            else _stillFor += dt;
            _lastPointer = pointer;
            float want;
            if (Mathf.Abs(stickX) > 0.2f) want = -LookDeg * stickX;
            else if (_stillFor < IdleReturnSeconds) want = -LookDeg * Mathf.Clamp(pointerX, -1f, 1f);
            else want = 8f * Mathf.Sin(_idle * 0.4f);   // nobody is pointing: a slow look around
            // Owner walk 1: a drag on the fighter turns it by hand; it stays where it was left for a moment, then drifts back.
            if (Time.unscaledTime >= _manualUntil) _yaw = Mathf.Lerp(_yaw, want, 1f - Mathf.Exp(-TurnSharpness * dt));
            if (_fighter != null)
            {
                _fighter.SetFacing(Quaternion.Euler(0f, _yaw + FacingOffsetDeg, 0f));
                _fighter.BeginPose();
                _fighter.Pose(ArtMapping.Idle, _idle, 1f, true);
                _fighter.Evaluate();
            }
            if (_egg != null)
            {
                _egg.BeginPose();
                _egg.Pose(ArtMapping.Idle, _idle, 1f, true);
                _egg.Evaluate();
            }
        }

        float _manualUntil = -1f;
        static readonly Vector3[] BoxCorners = new Vector3[8];

        /// <summary>
        /// Owner walk 1: where the fighter stands in the stage texture, 0..1 (x right, y DOWN from the top), from its renderers'
        /// bounds through the stage camera; false with no fighter. Home puts the fighter's click target there.
        /// </summary>
        public bool FighterRect01(out Rect r)
        {
            r = default;
            if (_fighter == null || _cam == null) return false;
            bool any = false;
            Bounds b = default;
            foreach (Renderer rd in _fighter.Root.GetComponentsInChildren<Renderer>())
            {
                if (!rd.enabled) continue;
                if (!any) { b = rd.bounds; any = true; }
                else b.Encapsulate(rd.bounds);
            }
            if (!any) return false;
            Vector3 mn = b.min, mx = b.max;
            for (int i = 0; i < 8; i++)
                BoxCorners[i] = new Vector3((i & 1) == 0 ? mn.x : mx.x, (i & 2) == 0 ? mn.y : mx.y, (i & 4) == 0 ? mn.z : mx.z);
            float x0 = 1f, y0 = 1f, x1 = 0f, y1 = 0f;
            for (int i = 0; i < 8; i++)
            {
                Vector3 v = _cam.WorldToViewportPoint(BoxCorners[i]);
                x0 = Mathf.Min(x0, v.x); x1 = Mathf.Max(x1, v.x);
                y0 = Mathf.Min(y0, 1f - v.y); y1 = Mathf.Max(y1, 1f - v.y);
            }
            r = Rect.MinMaxRect(Mathf.Clamp01(x0), Mathf.Clamp01(y0), Mathf.Clamp01(x1), Mathf.Clamp01(y1));
            return r.width > 0.02f && r.height > 0.02f;
        }

        /// <summary>The fighter's facing, degrees (0 faces the camera).</summary>
        public float Yaw => _yaw;

        /// <summary>Owner walk 1: turn the fighter by hand (a drag on Home): deg added to the facing, held for a moment.</summary>
        public void Drag(float deg)
        {
            _yaw = Mathf.Repeat(_yaw + deg + 180f, 360f) - 180f;
            _manualUntil = Time.unscaledTime + IdleReturnSeconds;
            if (_fighter != null) _fighter.SetFacing(Quaternion.Euler(0f, _yaw + FacingOffsetDeg, 0f));
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        void DropFighter()
        {
            _dresser?.Detach();
            if (_fighter != null) _fighter.Dispose();
            _fighter = null;
        }

        /// <summary>Destroys models, pieces, camera and texture (the home screen is gone or a match is loading).</summary>
        public void Release()
        {
            DropFighter();
            if (_egg != null) _egg.Dispose();
            _egg = null;
            _dresser?.Clear();
            _dresser = null;
            if (_cam != null) _cam.targetTexture = null;
            if (_rt != null)
            {
                _rt.Release();
                Object.Destroy(_rt);
            }
            _rt = null;
            if (_rig != null) Object.Destroy(_rig);
            _rig = null;
            _cam = null;
            _plate = null;
        }
    }
}
