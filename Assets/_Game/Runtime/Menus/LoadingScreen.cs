using System;
using System.Collections;
using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.View;
using CriticalMass.Runtime.View.Dressing;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>The real loading steps, in order (gap list DG-13).</summary>
    public enum LoadStep { World = 0, Dressing = 1, Views = 2, WarmUp = 3 }

    /// <summary>
    /// The loading screen (DG-13): full-bleed key art (KeyArt, or Resources "UI/KeyArt/loading"; without art, a sky
    /// gradient and the logo), one lore line and one gameplay tip from the words pack (DG-04, built-in fallbacks), the
    /// controls card for the device used last, and a progress bar driven by the real steps: World, Dressing, Views,
    /// WarmUp (frames rendered behind the opaque screen so the first match frames do not hitch). Progress is the sum of
    /// finished step weights plus the current step's fraction, and is a running maximum, so it never goes backward; the
    /// bar eases toward it. Finish hides the screen once the bar is full and it has been up at least MinSeconds (1.2 s).
    /// <para>Game flow use: Show(seed), then Report / Complete per step (or Run a list of step coroutines), then Finish.
    /// On main without the game flow, player builds show it on boot (ShowOnBoot) and watch the boot's own steps.</para>
    /// Screen Space - Camera canvas, sorting order 60 (above every menu); survives scene loads while open.
    /// </summary>
    public sealed class LoadingScreen : MonoBehaviour
    {
        public const float MinSeconds = 1.2f;
        /// <summary>Play Now and Play again: the screen stays only as long as the build (plus the bar's short fill).</summary>
        public const float QuickMinSeconds = 0.35f;
        public const float FadeOutSeconds = 0.2f;
        public const int WarmUpFrames = 4;
        /// <summary>A watched boot step that never reports is completed after this (the screen never hangs).</summary>
        public const float StepTimeoutSeconds = 6f;

        /// <summary>Share of the bar per step (sums to 1).</summary>
        public static readonly float[] Weights = { 0.35f, 0.3f, 0.2f, 0.15f };
        /// <summary>The line under the bar per step (no technology names).</summary>
        public static readonly string[] StepLabels = { "RAISING THE BASIN", "DRESSING THE WORLD", "WAKING THE BANNERS", "WARMING UP" };

        /// <summary>Player builds show it on boot until the game flow takes over (set false there).</summary>
        public static bool ShowOnBoot
        {
            get => _bootOverride ?? false;   // integration batch 2: the game flow shows it (GameFlow.BeginMatch), never the boot
            set => _bootOverride = value;
        }

        static bool? _bootOverride;

        /// <summary>
        /// The game flow shows the screen from Play / Play again until the new world has drawn (GameFlow.BeginMatch,
        /// FlowDirector). Off in batch mode (gates and tests start matches back to back and never look at it).
        /// </summary>
        public static bool UseInFlow
        {
            get => _flowOverride ?? !Application.isBatchMode;
            set => _flowOverride = value;
        }

        static bool? _flowOverride;

        /// <summary>Opens the screen for a match that is about to load; it waits (bar at zero) until WatchBuilt.</summary>
        public static LoadingScreen ShowForMatch(int seed) => ShowForMatch(seed, null, false);

        /// <summary>
        /// As ShowForMatch(seed), naming the match speed (speedLine, e.g. "STANDARD SPEED · 1X"; null: none) and, quick, with
        /// the shorter minimum time on screen (QuickMinSeconds) for Play Now and Play again.
        /// </summary>
        public static LoadingScreen ShowForMatch(int seed, string speedLine, bool quick)
        {
            LoadingScreen s = Show(seed);
            s._watchBoot = false;
            s._minSeconds = quick ? QuickMinSeconds : MinSeconds;
            s.SpeedLine = speedLine ?? string.Empty;
            HudText.Set(s._speed, s.SpeedLine);
            return s;
        }

        /// <summary>The new scene's game exists: complete each step as the world, its dressing and its views report in.</summary>
        public static void WatchBuilt()
        {
            if (_instance == null || !_instance._open) return;
            _instance._watchBoot = true;
            _instance._stepStart = Time.unscaledTime;
        }

        /// <summary>Key art for the slot; null = Resources "UI/KeyArt/loading", then the gradient and logo.</summary>
        public static Texture KeyArt;

        /// <summary>Lore line rect (1000 x 150, Ui 34) and tip rect (950 x 80, Ui 26) and the sizes they may shrink to.</summary>
        public const float LoreW = 1000f, LoreH = 150f, TipW = 950f, TipH = 80f;
        public const int LoreSize = 34, LoreMinSize = 22, TipSize = 26, TipMinSize = 18;

        static LoadingScreen _instance;
        public static LoadingScreen Instance => _instance;
        public static bool IsOpen => _instance != null && _instance._open;
        public static event Action Hidden;

        public Canvas Canvas { get; private set; }
        /// <summary>The progress target (running maximum of reports), 0..1.</summary>
        public float Target { get; private set; }
        /// <summary>The bar as drawn, 0..1; never decreases.</summary>
        public float Shown { get; private set; }
        public LoadStep CurrentStep { get; private set; }
        public string LoreLine { get; private set; } = "";
        public string Tip { get; private set; } = "";
        /// <summary>The match speed line under the tip ("" when none was given).</summary>
        public string SpeedLine { get; private set; } = "";
        /// <summary>The minimum time on screen for this opening (MinSeconds, or QuickMinSeconds for Play Now / Play again).</summary>
        public float MinimumSeconds => _minSeconds;
        public float OpenSeconds => _open || _closing ? Time.unscaledTime - _openedAt : 0f;
        public bool FinishRequested => _finish;
        public ControlsCard Controls { get; private set; }

        bool _open, _closing, _finish, _watchBoot;
        float _openedAt, _closeAt;
        readonly bool[] _done = new bool[4];
        readonly float[] _frac = new float[4];
        float _stepStart;
        int _warmFrames;
        Action _onHidden;
        CanvasGroup _group;
        RawImage _art;
        SkyGradient _sky;
        RawImage _logo;
        Text _lore, _tip, _step, _pct, _speed;
        float _minSeconds = MinSeconds;
        VectorShape _bar;
        bool _dirtyCard;

        // ---------------------------------------------------------------- static API

        /// <summary>Opens the screen (or returns the open one). seed picks the lore line and the tip.</summary>
        public static LoadingScreen Show(int seed = 0)
        {
            if (_instance == null)
            {
                var go = new GameObject("Loading Screen");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<LoadingScreen>();
            }
            _instance.Open(seed);
            return _instance;
        }

        /// <summary>Progress inside a step, 0..1; earlier steps count as done. Ignored when closed.</summary>
        public static void Report(LoadStep step, float fraction01)
        {
            if (_instance == null || !_instance._open) return;
            _instance.SetStep(step, Mathf.Clamp01(fraction01));
        }

        public static void Complete(LoadStep step) => Report(step, 1f);

        /// <summary>Every step done; hides when the bar is full and MinSeconds have passed, then calls onHidden.</summary>
        public static void Finish(Action onHidden = null)
        {
            if (_instance == null || !_instance._open)
            {
                onHidden?.Invoke();
                return;
            }
            for (int i = 0; i < 4; i++) _instance.SetStep((LoadStep)i, 1f);
            _instance._finish = true;
            _instance._onHidden = onHidden;
        }

        /// <summary>
        /// Runs steps in order as coroutines (each yields while it works; set progress with Report as it goes) and
        /// finishes. A null entry completes its step at once. Start it with StartCoroutine on any live behaviour.
        /// </summary>
        public static IEnumerator Run(IList<Func<IEnumerator>> steps, Action onHidden = null)
        {
            for (int i = 0; i < 4; i++)
            {
                Func<IEnumerator> s = steps != null && i < steps.Count ? steps[i] : null;
                if (s != null)
                {
                    IEnumerator e = s();
                    while (e != null && e.MoveNext()) yield return e.Current;
                }
                Complete((LoadStep)i);
            }
            if (IsOpen) yield return WarmUp();
            Finish(onHidden);
        }

        /// <summary>The WarmUp step: lets the camera draw the new world WarmUpFrames times behind the screen.</summary>
        public static IEnumerator WarmUp()
        {
            for (int f = 1; f <= WarmUpFrames; f++)
            {
                yield return null;
                Report(LoadStep.WarmUp, (float)f / WarmUpFrames);
            }
        }

        /// <summary>Progress for a set of finished flags and fractions (pure; tests).</summary>
        public static float Progress(bool[] done, float[] frac)
        {
            float p = 0f;
            for (int i = 0; i < 4; i++) p += Weights[i] * (done[i] ? 1f : Mathf.Clamp01(frac[i]));
            return Mathf.Clamp01(p);
        }

        // ---------------------------------------------------------------- lifecycle

        void OnEnable()
        {
            if (_instance == null) _instance = this;
            ControlBindings.ActiveDeviceChanged += OnDevice;
            ControlBindings.BindingsChanged += OnBindings;
        }

        void OnDisable()
        {
            ControlBindings.ActiveDeviceChanged -= OnDevice;
            ControlBindings.BindingsChanged -= OnBindings;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        void OnDevice(ControlDevice d) => _dirtyCard = true;
        void OnBindings() => _dirtyCard = true;

        void Open(int seed)
        {
            Build();
            if (_open && !_closing) return;
            _open = true;
            _closing = false;
            _finish = false;
            _onHidden = null;
            _openedAt = Time.unscaledTime;
            _stepStart = _openedAt;
            for (int i = 0; i < 4; i++)
            {
                _done[i] = false;
                _frac[i] = 0f;
            }
            Target = 0f;
            Shown = 0f;
            _minSeconds = MinSeconds;
            SpeedLine = string.Empty;
            HudText.Set(_speed, SpeedLine);
            _warmFrames = 0;
            CurrentStep = LoadStep.World;
            _bar.Fill01 = 0f;
            _group.alpha = 1f;
            Canvas.enabled = true;
            PickWords(seed);
            ApplyArt();
            ControlBindings.Sample();
            Controls.Refresh(ControlBindings.ActiveDevice);
            HudText.Set(_step, StepLabels[0]);
            HudText.Set(_pct, NumberCache.Int(0) + "%");
            MenuCanvas.UpdateScale(Canvas);
        }

        void PickWords(int seed)
        {
            var rng = new System.Random(seed != 0 ? seed : Environment.TickCount);
            IReadOnlyList<string> lore = LoreWords.LoreLines();
            IReadOnlyList<string> tips = LoreWords.Tips();
            LoreLine = lore.Count > 0 ? lore[rng.Next(lore.Count)] : "";
            Tip = tips.Count > 0 ? tips[rng.Next(tips.Count)] : "";
            HudText.Set(_lore, LoreLine);
            HudText.Set(_tip, Tip);
        }

        void SetStep(LoadStep step, float fraction)
        {
            int s = (int)step;
            for (int i = 0; i < s; i++) _done[i] = true;
            if (fraction >= 1f) _done[s] = true;
            else _frac[s] = Mathf.Max(_frac[s], fraction);
            Target = Mathf.Max(Target, Progress(_done, _frac));
            int cur = 0;
            while (cur < 3 && _done[cur]) cur++;
            if ((int)CurrentStep != cur)
            {
                CurrentStep = (LoadStep)cur;
                _stepStart = Time.unscaledTime;
                HudText.Set(_step, StepLabels[cur]);
            }
        }

        // ---------------------------------------------------------------- build

        void Build()
        {
            if (Canvas != null) return;
            Canvas = MenuCanvas.Make(transform, "Loading Canvas", 60, out RectTransform space);
            _group = Canvas.gameObject.AddComponent<CanvasGroup>();
            var root = (RectTransform)Canvas.transform;

            // Opaque base so nothing of the world shows through while it is being built.
            Image bg = MenuUi.Rt(root, "Base").gameObject.AddComponent<Image>();
            MenuUi.Stretch(bg.rectTransform);
            bg.color = CMTokens.VoidNavy.Color;
            bg.raycastTarget = true;
            bg.transform.SetAsFirstSibling();
            _sky = MenuUi.Rt(root, "Sky").gameObject.AddComponent<SkyGradient>();
            MenuUi.Stretch(_sky.rectTransform);
            _sky.Top = CMTokens.SkyTop.Color;
            _sky.Bottom = Color.Lerp(CMTokens.SkyHorizon.Color, CMTokens.VoidNavy.Color, 0.55f);
            _sky.raycastTarget = false;
            _sky.transform.SetSiblingIndex(1);
            _art = MenuUi.Rt(root, "Key Art").gameObject.AddComponent<RawImage>();
            MenuUi.Stretch(_art.rectTransform);
            _art.raycastTarget = false;
            _art.transform.SetSiblingIndex(2);
            // Bottom shade so the words and the bar read on any art.
            SkyGradient shade = MenuUi.Rt(root, "Shade").gameObject.AddComponent<SkyGradient>();
            MenuUi.Stretch(shade.rectTransform);
            shade.Top = CMTokens.WithAlpha(CMTokens.VoidNavy.Color, 0f);
            shade.Bottom = CMTokens.WithAlpha(CMTokens.VoidNavy.Color, 0.85f);
            shade.raycastTarget = false;
            shade.transform.SetSiblingIndex(3);

            Color cream = CMTokens.WarmCream.Color, brass = CMTokens.ToolBrass.Color;
            _logo = MenuUi.Rt(space, "Logo").gameObject.AddComponent<RawImage>();
            MenuUi.Place(_logo.rectTransform, 110f, 80f, 560f, 560f * 470f / 600f);
            _logo.raycastTarget = false;

            _lore = MenuUi.Label(space, "Lore", CMTokens.Ui, 34, cream, TextAnchor.LowerLeft, 120f, 560f, 1000f, 150f, "", 3f);
            MenuUi.Fit(_lore, LoreMinSize);   // 2026-11-12: a long words-pack line shrinks before it spills (LayoutTextFitTests)
            VectorShape tipPanel = MenuUi.Panel(space, "Tip Panel", 110f, 740f, 1010f, 150f);
            MenuUi.Label(tipPanel.transform, "Tip Head", CMTokens.Display, 26, brass, TextAnchor.MiddleLeft, 30f, 18f, 300f, 36f, "TIP", 3f);
            _tip = MenuUi.Label(tipPanel.transform, "Tip", CMTokens.Ui, 26, cream, TextAnchor.UpperLeft, 30f, 58f, 950f, 80f, "", 2f);
            MenuUi.Fit(_tip, TipMinSize);

            Controls = ControlsCard.Build(space, 1220f, 110f, 590f, 780f, "Loading Controls");

            _step = MenuUi.Label(space, "Step", CMTokens.Ui, 22, cream, TextAnchor.MiddleLeft, 110f, 930f, 900f, 34f, StepLabels[0], 2f);
            _pct = MenuUi.Label(space, "Percent", CMTokens.Mono, 22, cream, TextAnchor.MiddleRight, 1510f, 930f, 300f, 34f, "0%", 2f);
            // The match speed (Play Now / setup), between the step line and the percent, in brass so it reads as a setting.
            _speed = MenuUi.Label(space, "Speed", CMTokens.Display, 24, brass, TextAnchor.MiddleCenter, 760f, 930f, 600f, 34f, "", 2f);
            _bar = MenuUi.Shape(space, "Bar", ShapeKind.RoundRect, 110f, 972f, 1700f, 30f, CMTokens.Biomass.Color, -1f);
            _bar.EmptyColor = CMTokens.MeterEmpty.Color;
            _bar.OutlinePx = 3f;
            _bar.RimPx = 2f;
            _bar.Fill01 = 0f;
            Canvas.enabled = false;
        }

        void ApplyArt()
        {
            Texture art = KeyArt != null ? KeyArt : Resources.Load<Texture2D>("UI/KeyArt/loading");
            _art.texture = art;
            _art.enabled = art != null;
            _logo.enabled = art == null;
            if (art == null)
            {
                float h = Canvas.worldCamera != null ? Canvas.worldCamera.pixelHeight : Screen.height;
                _logo.texture = Resources.Load<Texture2D>(h > 1440 ? "UI/Logo/critical-mass-logo@4x" : "UI/Logo/critical-mass-logo@2x");
                _logo.enabled = _logo.texture != null;
                return;
            }
            // Aspect-fill: crop the art's uvRect to the screen's aspect.
            UnityEngine.Camera cam = Canvas.worldCamera;
            float sw = cam != null ? cam.pixelWidth : Screen.width, sh = cam != null ? cam.pixelHeight : Screen.height;
            float sa = sh > 0 ? sw / sh : 16f / 9f, ta = art.height > 0 ? (float)art.width / art.height : sa;
            _art.uvRect = ta > sa ? new Rect((1f - sa / ta) * 0.5f, 0f, sa / ta, 1f) : new Rect(0f, (1f - ta / sa) * 0.5f, 1f, ta / sa);
        }

        // ---------------------------------------------------------------- update

        void Update()
        {
            if (!_open && !_closing) return;
            MenuCanvas.UpdateScale(Canvas);
            ControlBindings.Sample();
            if (_dirtyCard)
            {
                _dirtyCard = false;
                Controls.Refresh(ControlBindings.ActiveDevice);
            }
            if (_watchBoot) WatchBoot();

            if (_closing)
            {
                float a = 1f - (Time.unscaledTime - _closeAt) / FadeOutSeconds;
                _group.alpha = Mathf.Clamp01(a);
                if (a <= 0f) EndClose();
                return;
            }

            // Ease toward the target, never back: fast enough that a finished load fills in about 0.3 s.
            float next = Mathf.MoveTowards(Shown, Target, Time.unscaledDeltaTime * 3.2f);
            if (next > Shown)
            {
                Shown = next;
                _bar.Fill01 = Shown;
                HudText.Set(_pct, NumberCache.Int(Mathf.FloorToInt(Shown * 100f + 1e-3f)) + "%");
            }
            if (_finish && Shown >= 0.999f && OpenSeconds >= _minSeconds)
            {
                _open = false;
                _closing = true;
                _closeAt = Time.unscaledTime;
                _bar.Fill01 = 1f;
            }
        }

        void EndClose()
        {
            _closing = false;
            _watchBoot = false;
            Canvas.enabled = false;
            Action cb = _onHidden;
            _onHidden = null;
            cb?.Invoke();
            Hidden?.Invoke();
        }

        // ---------------------------------------------------------------- boot watch (no game flow yet)

        /// <summary>Shows the screen over the boot and completes each step when the game reports it built.</summary>
        public static LoadingScreen ShowForBoot()
        {
            LoadingScreen s = Show(0);
            s._watchBoot = true;
            return s;
        }

        void WatchBoot()
        {
            if (!_open) return;
            bool timedOut = Time.unscaledTime - _stepStart > StepTimeoutSeconds;
            switch (CurrentStep)
            {
                case LoadStep.World:
                {
                    SimDriverBehaviour d = FindAnyObjectByType<SimDriverBehaviour>();
                    if ((d != null && d.World != null) || timedOut) Complete(LoadStep.World);
                    break;
                }
                case LoadStep.Dressing:
                    if ((FindAnyObjectByType<WorldView>() != null && WorldView.DressVersion > 0) || timedOut) Complete(LoadStep.Dressing);
                    break;
                case LoadStep.Views:
                    if (FindAnyObjectByType<ActorViews>() != null || timedOut) Complete(LoadStep.Views);
                    break;
                default:
                    if (_finish) break;
                    _warmFrames++;
                    Report(LoadStep.WarmUp, (float)_warmFrames / WarmUpFrames);
                    if (_warmFrames >= WarmUpFrames) Finish();
                    break;
            }
        }
    }

    /// <summary>A vertical two-colour gradient over the rect (vertex colours, one quad).</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SkyGradient : MaskableGraphic
    {
        public Color Top = Color.white, Bottom = Color.black;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            vh.AddVert(new Vector3(r.xMin, r.yMin), Bottom, Vector4.zero);
            vh.AddVert(new Vector3(r.xMax, r.yMin), Bottom, Vector4.zero);
            vh.AddVert(new Vector3(r.xMax, r.yMax), Top, Vector4.zero);
            vh.AddVert(new Vector3(r.xMin, r.yMax), Top, Vector4.zero);
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(0, 2, 3);
        }
    }

    /// <summary>Player builds without the game flow: the loading screen over the boot (LoadingScreen.ShowOnBoot).</summary>
    public static class LoadingScreenInstaller
    {
        static bool _shownThisLaunch;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!LoadingScreen.ShowOnBoot || _shownThisLaunch) return;
            _shownThisLaunch = true;
            LoadingScreen.ShowForBoot();
        }
    }
}
