using System;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>
    /// The attract-mode idle clock, free of Unity objects so tests can step it: after IdleSeconds on the title with no
    /// input the tour starts; any input (or leaving the title) ends it on the same step.
    /// </summary>
    public sealed class AttractClock
    {
        public enum Change { None, Enter, Exit }

        public const double DefaultIdleSeconds = 20.0;
        const double Eps = 1e-6;

        public double IdleSeconds = DefaultIdleSeconds;
        public double Idle { get; set; }
        public bool Touring { get; private set; }

        /// <summary>One frame: titleOpen = the title is up and nothing else covers it; anyInput = any key, click, mouse
        /// move, controller button or stick this frame.</summary>
        public Change Step(double dt, bool titleOpen, bool anyInput)
        {
            if (!titleOpen || anyInput)
            {
                Idle = 0;
                if (!Touring) return Change.None;
                Touring = false;
                return Change.Exit;
            }
            if (Touring) return Change.None;
            Idle += Math.Max(0, dt);
            if (Idle < IdleSeconds - Eps) return Change.None;
            Touring = true;
            return Change.Enter;
        }

        /// <summary>Starts the tour now (capture tool, tests).</summary>
        public void ForceEnter()
        {
            Touring = true;
        }

        public void Reset()
        {
            Idle = 0;
            Touring = false;
        }
    }

    /// <summary>
    /// DG-28 attract mode: after 20 s idle on the title, the camera tours the hill, both bases, the shrine and the Kaiju (the
    /// egg before the hatch), one slow orbiting shot each with a caption, and loops until any input returns to the title
    /// on the same frame. Shots dip through Void Navy (0.25 s); with reduced motion on (PlayerPrefs "cm.reducedMotion")
    /// the shots hold still. View only: it reads sim positions and never writes to the sim; the match behind the title
    /// keeps running as before.
    /// <para>The tour drives its own Cinemachine camera at priority 100 (cut in and cut out through
    /// CinemachineCore.GetBlendOverride, which passes every other blend through unchanged), so the gameplay rig is not
    /// edited.</para>
    /// <para>Public API for the game-flow item (DG-01): <see cref="IsTouring"/>, <see cref="Begin"/>, <see cref="End"/>,
    /// <see cref="Started"/>, <see cref="Ended"/>, <see cref="TitleOpen"/> (set it to the flow's own "title is showing"
    /// test; by default the tour runs while a TitleScreen component is on the game), <see cref="Enabled"/>.</para>
    /// </summary>
    [Flow.GameOverlay(Order = 41)]   // installed by GameBootstrap through the DG-01 overlay registry
    public sealed class AttractMode : MonoBehaviour
    {
        public const double IdleSeconds = AttractClock.DefaultIdleSeconds;
        public const float ShotSeconds = 6.5f;
        public const float DipSeconds = 0.25f;
        public const int TourPriority = 100;

        /// <summary>Master switch (true by default).</summary>
        public static bool Enabled = true;
        /// <summary>Batch mode (gates, captures) never tours unless a test sets this.</summary>
        public static bool AllowInBatchMode;
        /// <summary>Optional override of "the title is showing"; null = a TitleScreen component is on the game object.</summary>
        public static Func<bool> TitleOpen;

        public static AttractMode Instance { get; private set; }
        public static bool IsTouring => Instance != null && Instance._touring;
        public static event Action Started;
        public static event Action Ended;

        public AttractClock Clock { get; } = new AttractClock();
        /// <summary>Index of the shot on screen (0 hill, 1 Azure base, 2 Crimson base, 3 shrine, 4 Kaiju).</summary>
        public int ShotIndex { get; private set; }
        /// <summary>The tour's camera (inactive while not touring).</summary>
        public CinemachineCamera TourCamera => _cam;

        enum ShotKind { Hill, AzureBase, CrimsonBase, Shrine, Kaiju }

        const int ShotCount = 5;
        static readonly ShotKind[] Order = { ShotKind.Hill, ShotKind.AzureBase, ShotKind.CrimsonBase, ShotKind.Shrine, ShotKind.Kaiju };
        static readonly float[] Distance = { 36f, 24f, 24f, 24f, 18f };
        static readonly float[] YawOffset = { 0f, -28f, 28f, -18f, 22f };
        const float PitchDeg = 33f;
        const float OrbitDeg = 22f;
        const float LookUp = 1.4f;
        const float TourFov = 38f;

        // Captions (built-in; the words pack has no attract lines yet). Kaiju lines follow its state at the shot's start.
        static readonly string[] Titles = { "THE HILL", "AZURE BASE", "CRIMSON BASE", "THE SHRINE" };
        static readonly string[] Lines =
        {
            "Whoever stands on it is heard. Most hill time wins.",
            "Azure fights under the shield.",
            "Crimson fights under the flame.",
            "Far from the hill, the egg sleeps. Every step toward it is a step off the hill.",
        };
        const string EggTitle = "THE EGG";
        const string EggLine = "Living biomass makes it loyal. Corruption makes it angry at everyone.";
        const string KaijuTitle = "THE KAIJU";
        const string NeutralLine = "Fed evenly, it sides with no one. It fights whoever hits it.";
        const string AzureLine = "Azure fed it most. It fights for Azure.";
        const string CrimsonLine = "Crimson fed it most. It fights for Crimson.";
        const string BerserkTitle = "BERSERK";
        const string BerserkLine = "Corruption won. It attacks everyone.";
        const string PressAnyKey = "PRESS ANY KEY";

        SimDriverBehaviour _driver;
        TitleScreen _title;
        int _titleLookFrame;
        bool _touring;
        float _shotT;
        bool _reduced;
        float _breathe;
        GameObject _camGo;
        CinemachineCamera _cam;
        static CinemachineCamera _cutCam;
        static CinemachineCore.GetBlendOverrideDelegate _prevOverride;
        static readonly CinemachineCore.GetBlendOverrideDelegate OurOverride = BlendOverride;

        bool _built;
        Canvas _canvas;
        Image _dip;
        Text _capTitle, _capLine, _press;
        VectorShape _accent;

        void Awake()
        {
            if (Instance == null) Instance = this;
            _driver = GetComponent<SimDriverBehaviour>();
        }

        void OnDestroy()
        {
            if (_touring) StopTour(false);
            if (_camGo != null) Destroy(_camGo);
            if (Instance == this) Instance = null;
        }

        // ---- static API ----

        /// <summary>Starts the tour now (capture tool, tests). False when there is no game.</summary>
        public static bool Begin()
        {
            if (Instance == null) return false;
            Instance.Clock.ForceEnter();
            Instance.StartTour();
            return true;
        }

        /// <summary>Ends the tour now (as any input does).</summary>
        public static void End()
        {
            if (Instance == null || !Instance._touring) return;
            Instance.Clock.Reset();
            Instance.StopTour(true);
        }

        // ---- frame ----

        void Update()
        {
            OverlayGuard.Tick();
            bool active = Enabled && (!Application.isBatchMode || AllowInBatchMode);
            bool titleOpen = active && IsTitleOpen() && !StoryIntro.IsPlaying && !Credits.IsOpen;
            bool input = AnyInput();
            AttractClock.Change c = Clock.Step(Time.unscaledDeltaTime, titleOpen, input);
            if (c == AttractClock.Change.Enter) StartTour();
            else if (c == AttractClock.Change.Exit) StopTour(true);
            if (_touring) Tour(Time.unscaledDeltaTime);
        }

        bool IsTitleOpen()
        {
            if (TitleOpen != null) return TitleOpen();
            if (_title == null && Time.frameCount >= _titleLookFrame)
            {
                _titleLookFrame = Time.frameCount + 15;
                _title = GetComponent<TitleScreen>();
            }
            return _title != null;
        }

        /// <summary>Any key, mouse button, mouse movement or scroll, controller button, trigger or stick this frame.</summary>
        public static bool AnyInput()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) return true;
            Mouse m = Mouse.current;
            if (m != null)
            {
                if (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame || m.middleButton.wasPressedThisFrame) return true;
                if (m.delta.ReadValue().sqrMagnitude > 4f || m.scroll.ReadValue().sqrMagnitude > 0.01f) return true;
            }
            Gamepad p = Gamepad.current;
            if (p == null) return false;
            return p.buttonSouth.wasPressedThisFrame || p.buttonEast.wasPressedThisFrame || p.buttonWest.wasPressedThisFrame ||
                   p.buttonNorth.wasPressedThisFrame || p.startButton.wasPressedThisFrame || p.selectButton.wasPressedThisFrame ||
                   p.leftShoulder.wasPressedThisFrame || p.rightShoulder.wasPressedThisFrame ||
                   p.leftTrigger.wasPressedThisFrame || p.rightTrigger.wasPressedThisFrame ||
                   p.leftStickButton.wasPressedThisFrame || p.rightStickButton.wasPressedThisFrame ||
                   p.dpad.up.wasPressedThisFrame || p.dpad.down.wasPressedThisFrame ||
                   p.dpad.left.wasPressedThisFrame || p.dpad.right.wasPressedThisFrame ||
                   p.leftStick.ReadValue().sqrMagnitude > 0.09f || p.rightStick.ReadValue().sqrMagnitude > 0.09f;
        }

        // ---- tour ----

        void StartTour()
        {
            if (_touring) return;
            if (_driver == null) _driver = GetComponent<SimDriverBehaviour>();
            if (_driver == null || _driver.World == null) return;
            Build();
            _touring = true;
            _reduced = StoryIntro.ReducedMotion;
            _breathe = 0f;
            OverlayGuard.Begin(gameObject, ConsumePause);
            InstallBlendOverride();
            _cutCam = _cam;
            ShotIndex = 0;
            _shotT = 0f;
            BeginShot();
            Pose();
            _camGo.SetActive(true);
            _canvas.enabled = true;
            Started?.Invoke();
        }

        bool ConsumePause()
        {
            End();
            return true;
        }

        void StopTour(bool raise)
        {
            if (!_touring) return;
            _touring = false;
            if (_camGo != null) _camGo.SetActive(false);   // cut back (BlendOverride) on the brain's next update
            if (_canvas != null) _canvas.enabled = false;
            OverlayGuard.End();
            if (raise) Ended?.Invoke();
        }

        void Tour(float dt)
        {
            if (_driver == null || _driver.World == null)
            {
                End();
                return;
            }
            UpdateScale();
            _shotT += Mathf.Min(dt, 0.1f);
            if (_shotT >= ShotSeconds)
            {
                _shotT -= ShotSeconds;
                ShotIndex = (ShotIndex + 1) % ShotCount;
                BeginShot();
            }
            Pose();

            // Dip through Void Navy at both ends of every shot (fades are not motion: kept with reduced motion).
            float a = 0f;
            if (_shotT < DipSeconds) a = 1f - _shotT / DipSeconds;
            else if (_shotT > ShotSeconds - DipSeconds) a = (_shotT - (ShotSeconds - DipSeconds)) / DipSeconds;
            Color dc = _dip.color;
            dc.a = Mathf.Clamp01(a);
            _dip.color = dc;

            if (!_reduced)
            {
                _breathe += dt;
                Color pc = _press.color;
                pc.a = 0.75f + 0.25f * Mathf.Cos(_breathe * Mathf.PI);   // the title's own slow 0.5 Hz breathe
                _press.color = pc;
            }
        }

        void BeginShot()
        {
            ShotKind k = Order[ShotIndex];
            string title, line;
            Color accent;
            switch (k)
            {
                case ShotKind.Kaiju:
                    KaijuState s = _driver.World.Kaiju != null ? _driver.World.Kaiju.State : KaijuState.Egg;
                    KaijuCaption(s, out title, out line, out accent);
                    break;
                default:
                    int i = (int)k;
                    title = Titles[i];
                    line = Lines[i];
                    accent = k == ShotKind.AzureBase ? CMTokens.Azure.Color
                        : k == ShotKind.CrimsonBase ? CMTokens.Crimson.Color
                        : k == ShotKind.Shrine ? CMTokens.EggSeam.Color
                        : CMTokens.ToolBrass.Color;
                    break;
            }
            HudText.Set(_capTitle, title);
            HudText.Set(_capLine, line);
            _accent.SetFill(accent);
        }

        /// <summary>The Kaiju shot's caption for a state (public for tests).</summary>
        public static void KaijuCaption(KaijuState s, out string title, out string line, out Color accent)
        {
            switch (s)
            {
                case KaijuState.Egg: title = EggTitle; line = EggLine; accent = CMTokens.EggSeam.Color; return;
                case KaijuState.Azure: title = KaijuTitle; line = AzureLine; accent = CMTokens.Azure.Color; return;
                case KaijuState.Crimson: title = KaijuTitle; line = CrimsonLine; accent = CMTokens.Crimson.Color; return;
                case KaijuState.Berserk: title = BerserkTitle; line = BerserkLine; accent = CMTokens.Corruption.Color; return;
                default: title = KaijuTitle; line = NeutralLine; accent = CMTokens.WarmCream.Color; return;
            }
        }

        /// <summary>Where shot k looks, in sim (x, z); the Kaiju is read live every frame.</summary>
        void ShotTarget(ShotKind k, out double x, out double z)
        {
            World w = _driver.World;
            MapDef map = w.Map;
            switch (k)
            {
                case ShotKind.Hill: x = map.Hill.X; z = map.Hill.Z; return;
                case ShotKind.AzureBase: x = map.Pads[(int)TeamId.Azure].X; z = map.Pads[(int)TeamId.Azure].Z; return;
                case ShotKind.CrimsonBase: x = map.Pads[(int)TeamId.Crimson].X; z = map.Pads[(int)TeamId.Crimson].Z; return;
                case ShotKind.Shrine: x = map.Shrine.X; z = map.Shrine.Z; return;
                default:
                    if (w.Kaiju != null)
                    {
                        x = w.Kaiju.X;
                        z = w.Kaiju.Z;
                    }
                    else
                    {
                        x = map.Shrine.X;
                        z = map.Shrine.Z;
                    }
                    return;
            }
        }

        /// <summary>Camera pose for shot i at progress u (0..1) around pivot: a slow orbit and push-in, or still.</summary>
        public static void ShotPose(Vector3 pivot, double cameraYawDeg, int i, float u, bool reduced, out Vector3 pos, out Quaternion rot)
        {
            float yaw = (float)(360.0 - cameraYawDeg) + YawOffset[i];
            float dist = Distance[i];
            if (!reduced)
            {
                yaw += (u - 0.5f) * OrbitDeg;
                dist *= Mathf.Lerp(1.06f, 0.96f, u);
            }
            rot = Quaternion.Euler(PitchDeg, yaw, 0f);
            Vector3 look = pivot + Vector3.up * LookUp;
            pos = look + rot * new Vector3(0f, 0f, -dist);
        }

        void Pose()
        {
            ShotKind k = Order[ShotIndex];
            ShotTarget(k, out double x, out double z);
            World w = _driver.World;
            double h = GroundHeight.At(w.Map, x, z);
            Vector3 pivot = SimSpace.ToWorld(x, h, z);
            if (k == ShotKind.Kaiju && w.Kaiju != null && w.Kaiju.State != KaijuState.Egg) pivot += Vector3.up * 1.5f;
            ShotPose(pivot, _driver.Config.Render.CameraYawDeg, ShotIndex, Mathf.Clamp01(_shotT / ShotSeconds), _reduced,
                out Vector3 pos, out Quaternion rot);
            _camGo.transform.SetPositionAndRotation(pos, rot);
        }

        // ---- cut in, cut out ----

        void InstallBlendOverride()
        {
            if (CinemachineCore.GetBlendOverride == OurOverride) return;
            _prevOverride = CinemachineCore.GetBlendOverride;
            CinemachineCore.GetBlendOverride = OurOverride;
        }

        static CinemachineBlendDefinition BlendOverride(ICinemachineCamera from, ICinemachineCamera to,
            CinemachineBlendDefinition defaultBlend, UnityEngine.Object owner)
        {
            if (_cutCam != null && (ReferenceEquals(from, _cutCam) || ReferenceEquals(to, _cutCam)))
                return new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
            return _prevOverride != null ? _prevOverride(from, to, defaultBlend, owner) : defaultBlend;
        }

        // ---- build (once) ----

        void Build()
        {
            if (_built) return;
            _built = true;

            _camGo = new GameObject("Attract Camera");
            _camGo.SetActive(false);
            if (gameObject.scene.IsValid()) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_camGo, gameObject.scene);
            _cam = _camGo.AddComponent<CinemachineCamera>();
            _cam.Priority = TourPriority;
            _cam.Lens.ModeOverride = LensSettings.OverrideModes.None;
            _cam.Lens.FieldOfView = TourFov;
            _cam.Lens.NearClipPlane = GameplayCamera.NearClip;
            _cam.Lens.FarClipPlane = GameplayCamera.FarClip;
            _cam.Lens.Dutch = 0f;

            var go = new GameObject("Attract Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.sortingOrder = 25;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            _canvas.worldCamera = cam;
            _canvas.planeDistance = cam != null ? Mathf.Max(0.42f, cam.nearClipPlane * 1.35f) : 0.42f;
            var root = (RectTransform)go.transform;

            RectTransform space = MenuUi.Rt(root, "Space");
            space.anchorMin = space.anchorMax = new Vector2(0.5f, 0.5f);
            space.pivot = new Vector2(0.5f, 0.5f);
            space.sizeDelta = new Vector2(HudLayout.RefW, HudLayout.RefH);

            RawImage logo = MenuUi.Rt(space, "Logo").gameObject.AddComponent<RawImage>();
            MenuUi.Place(logo.rectTransform, 64f, 48f, 300f, 300f * 470f / 600f);
            logo.texture = Resources.Load<Texture2D>("UI/Logo/critical-mass-logo@2x");
            logo.enabled = logo.texture != null;
            logo.raycastTarget = false;

            // Caption: a chunky panel lower left with a team- or state-coloured accent bar.
            MenuUi.Panel(space, "Caption Panel", 64f, 836f, 900f, 160f);
            _accent = MenuUi.Shape(space, "Accent", ShapeKind.RoundRect, 88f, 860f, 14f, 112f, CMTokens.ToolBrass.Color, 7f);
            _accent.OutlinePx = 2f;
            _accent.RimPx = 0f;
            _capTitle = MenuUi.Label(space, "Caption Title", CMTokens.Display, 40, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft,
                124f, 856f, 820f, 56f, "", CMTokens.OutlinePx);
            _capLine = MenuUi.Label(space, "Caption Line", CMTokens.Ui, 26, CMTokens.WarmCream.Color, TextAnchor.UpperLeft,
                124f, 916f, 816f, 70f, "", 2f);
            _capLine.horizontalOverflow = HorizontalWrapMode.Wrap;

            _press = MenuUi.Label(space, "Press", CMTokens.Display, 30, CMTokens.WarmCream.Color, TextAnchor.MiddleRight,
                HudLayout.RefW - 664f, 940f, 600f, 50f, PressAnyKey, CMTokens.OutlinePx);

            _dip = MenuUi.Rt(root, "Dip").gameObject.AddComponent<Image>();
            MenuUi.Stretch(_dip.rectTransform);
            _dip.color = CMTokens.VoidNavy.Color;
            _dip.raycastTarget = false;

            _canvas.enabled = false;
            UpdateScale();
        }

        void UpdateScale()
        {
            UnityEngine.Camera cam = _canvas.worldCamera;
            if (cam == null)
            {
                cam = UnityEngine.Camera.main;
                if (cam != null) _canvas.worldCamera = cam;
            }
            float w = cam != null ? cam.pixelWidth : Screen.width, h = cam != null ? cam.pixelHeight : Screen.height;
            if (w < 1 || h < 1) return;
            float s = HudLayout.Scale(w, h);
            if (Mathf.Abs(_canvas.scaleFactor - s) > 1e-4f) _canvas.scaleFactor = s;
        }
    }
}
