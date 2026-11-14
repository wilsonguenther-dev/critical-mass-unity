using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.View.Moments
{
    /// <summary>
    /// BIG MOMENTS: short screen takeovers with illustrated cut-ins for big hits, power-ups and Kaiju events, plus small
    /// corner stingers for streaks (view only: reads the World through SimDriverBehaviour.Ticked, never writes it; visual
    /// time is its own clock, paused with the game). Installed on the Game root by the DG-01 overlay registry for matches.
    /// <para>One priority queue (MomentQueue) for every full-screen moment. Centre banners (Banner) share it: a takeover that
    /// is not allowed over a banner waits for it; a Kaiju or end-frame takeover plays over it and the banner is held
    /// (Banner.Hold) and enters fresh afterwards, so the two never stack.</para>
    /// <para>Layers (each recipe picks some): picture-hold freeze with the world desaturated except attacker and victim,
    /// one-frame invert, white flash, radial speed lines, a cut-in band wiping across with a portrait (image, else the live
    /// 3D model rendered off-screen, else the HUD portrait icon, else a shape) and a title on a text-free plate, burst,
    /// power-up aura rings and rising sparks with an edge glow, Kaiju screen-edge cracks, and the end frame. Reduced motion
    /// stills every layer (fades only); reduce flashing removes the invert and caps the flash; at most 3 flashes a second
    /// always; camera shake is never used. Input is never blocked (no raycasts, nothing interactable).</para>
    /// <para>API: <see cref="Request"/>, <see cref="Play"/>, <see cref="PlayGallery"/>, <see cref="StopAll"/>,
    /// <see cref="IsPlaying"/>, <see cref="Started"/>, <see cref="Ended"/>; settings in <see cref="BigMomentsSettings"/>.</para>
    /// </summary>
    [Flow.GameOverlay(Order = 53, Scope = Flow.OverlayScope.MatchOnly)]
    public sealed class BigMoments : MonoBehaviour
    {
        public const int SortingOrder = 34, GradeSortingOrder = 8;
        public const float RefW = 1920f, RefH = 1080f;
        public const float BandLength = 2900f, BandThick = 330f, DiagonalDeg = -11f;
        public const float PortraitSize = 780f, TitleW = 1080f, TitleH = 270f;
        /// <summary>hud-art-1: the cut-out's navy drop shadow, canvas px (mirrored for the right-hand entry).</summary>
        public const float ShadowDx = 22f, ShadowDy = -16f;
        /// <summary>hud-art-1: entry (back-out ease) and exit (ease-in) of the character, wall seconds.</summary>
        public const float PortraitIn = 0.30f, PortraitOut = 0.26f;
        public const float StingerW = 560f, StingerH = 140f, StingerTop = 380f, StingerRight = 24f;
        public const int Sparks = 12, Rings = 3;
        public const float SlowMotionFactor = 0.35f, SlowMotionTail = 0.35f;
        public const string AudioTakeover = "Audio/BigMoments/bm-takeover", AudioStinger = "Audio/BigMoments/bm-stinger";
        public const string GradeShader = "UI/Shaders/CMMomentGrade";

        public static BigMoments Instance { get; private set; }
        public static event Action<ActiveMoment> Started;
        public static event Action<MomentId> Ended;

        /// <summary>The most one frame advances the director's clock (seconds).</summary>
        public const float MaxFrameSeconds = 0.25f;   // SimClock.MaxDelta: slow editor capture frames still end a takeover

        /// <summary>A takeover is on screen or a banner-preempting one is about to start.</summary>
        public static bool IsPlaying => Instance != null && Instance._queue.Playing;

        private readonly MomentQueue _queue = new MomentQueue();
        private readonly MomentTriggers _triggers = new MomentTriggers();
        private readonly FlashLimiter _flashes = new FlashLimiter();
        private SimDriverBehaviour _driver;
        private HumanInputSource _input;
        private Action<World> _onTick;
        private double _clock;
        private int _seen = -1, _seenStinger = -1;
        private MomentLook _look;
        private MomentId _playingId;

        // gallery (development builds)
        private MomentId[] _gallery;
        private int _galleryAt = -1;
        private double _galleryNext;

        // view
        private Canvas _canvas, _gradeCanvas;
        private CanvasGroup _group, _stingerGroup;
        private RectTransform _root, _band, _portraitRt, _titleRt, _stinger, _auraRt;
        private RawImage _freeze, _dim, _speed, _bandFill, _bandLines, _burst, _portrait, _mask, _plate, _flash, _edge, _grade;
        private RawImage _portraitShadow, _plateAccent;
        private readonly RawImage[] _cracks = new RawImage[4];
        private readonly RawImage[] _rings = new RawImage[Rings];
        private readonly RawImage[] _sparks = new RawImage[Sparks];
        private VectorShape _plateShape, _portraitShape, _stingerShape;
        private RawImage _stingerPlate, _stingerIcon;
        private Text _title, _subtitle, _stingerTitle, _stingerSub;
        private Material _frozenMat, _liveMat;
        private RenderTexture _freezeRt;
        private int _captureFrame = -1;
        private ModelPortrait _model;
        private bool _colorForced;
        private double _savedScale;
        private bool _slowed;
        private double _slowUntil;
        private bool _fromRight;
        private Color _accent;
        private bool _flashOk, _invertOk;
        private AudioSource _audio;
        private AudioClip _takeoverClip, _stingerClip;
        private bool _clipsLoaded;

        // ---------------------------------------------------------------- API

        /// <summary>Offers a moment (other items may raise their own). Off/reduced settings, cooldowns and rarity apply.</summary>
        public static OfferResult Request(MomentRequest r)
        {
            if (Instance == null) return OfferResult.Unknown;
            World w = Instance.World;
            return Instance._queue.Offer(r, Instance._clock, BigMomentsSettings.Mode, w != null && Instance._triggers.TightFight(w));
        }

        /// <summary>Plays a recipe now with the local player as its actor, ignoring rarity (debug, captures, tests).</summary>
        public static void Play(MomentId id, MomentScale scale = MomentScale.Takeover)
        {
            if (Instance == null) return;
            Instance.Force(id, scale);
        }

        /// <summary>
        /// hud-art-1: plays a recipe with an explicit class, team and Kaiju state (captures and the art review walk play
        /// every class card for both teams and every Kaiju state without staging a fight).
        /// </summary>
        public static void Play(MomentId id, ClassId cls, TeamId team, KaijuState kaiju, MomentScale scale = MomentScale.Takeover)
        {
            if (Instance == null) return;
            Instance._override = true;
            Instance._overrideCls = cls;
            Instance._overrideTeam = team;
            Instance._overrideKaiju = kaiju;
            try { Instance.Force(id, scale); }
            finally { Instance._override = false; }
        }

        private bool _override;
        private ClassId _overrideCls;
        private TeamId _overrideTeam;
        private KaijuState _overrideKaiju;

        /// <summary>Plays every recipe in turn (and the three streak stingers), one after another.</summary>
        public static void PlayGallery()
        {
            if (Instance == null) return;
            Instance._gallery = MomentRecipes.Gallery();
            Instance._galleryAt = 0;
            Instance._galleryNext = Instance._clock;
        }

        public static bool GalleryRunning => Instance != null && Instance._galleryAt >= 0;

        public static void StopAll()
        {
            if (Instance == null) return;
            Instance._galleryAt = -1;
            Instance._queue.Clear();
        }

        /// <summary>For Banner.Hold: true while a takeover plays or one that preempts banners is waiting.</summary>
        public static bool HoldBanner() => Instance != null && Instance.HoldsBanner();

        private World World => _driver != null ? _driver.World : null;

        // ---------------------------------------------------------------- life cycle

        private void Awake()
        {
            Instance = this;
            _driver = GetComponent<SimDriverBehaviour>();
            _input = GetComponent<HumanInputSource>();
            _onTick = OnTick;
            if (_driver != null) _driver.Ticked += _onTick;
            Banner.Hold = HoldBanner;
        }

        private void OnDestroy()
        {
            if (_driver != null) _driver.Ticked -= _onTick;
            RestoreSlowMotion();
            ReleaseColor();
            if (_model != null) _model.Dispose();
            if (_freezeRt != null)
            {
                _freezeRt.Release();
                Destroy(_freezeRt);
            }
            if (_frozenMat != null) Destroy(_frozenMat);
            if (_liveMat != null) Destroy(_liveMat);
            if (Instance == this)
            {
                Instance = null;
                if (Banner.Hold == (Func<bool>)HoldBanner) Banner.Hold = null;
            }
        }

        private void OnDisable()
        {
            RestoreSlowMotion();
        }

        private bool HoldsBanner()
        {
            if (_queue.Playing) return true;
            return _queue.WaitingCount > 0 && _queue.HasWaitingPreempt;
        }

        private void OnTick(World w)
        {
            if (_galleryAt >= 0) return;
            _triggers.Observe(w);
            if (_triggers.Count == 0) return;
            BigMomentsMode mode = BigMomentsSettings.Mode;
            bool tight = _triggers.TightFight(w);
            for (int i = 0; i < _triggers.Count; i++) _queue.Offer(_triggers.Get(i), _clock, mode, tight);
        }

        private void Force(MomentId id, MomentScale scale)
        {
            MomentRecipe r = MomentRecipes.Get(id);
            if (r == null) return;
            World w = World;
            var req = new MomentRequest { Id = id, Actor = MomentTriggers.LocalActor, Victim = -1, PlayerOwned = true, Count = 3 };
            if (w != null && w.ActorCount > 0)
            {
                req.Team = w.Actors[0].Team;
                req.Cls = w.Actors[0].Cls;
                req.Kaiju = w.Kaiju.State == KaijuState.Egg ? KaijuState.Neutral : w.Kaiju.State;
                req.X = w.X[0];
                req.Z = w.Z[0];
                // a victim for the colour holes: the nearest enemy
                double best = double.MaxValue;
                for (int i = 1; i < w.ActorCount; i++)
                {
                    if (w.Actors[i].Team == req.Team || !w.Actors[i].Alive) continue;
                    double dx = w.X[i] - w.X[0], dz = w.Z[i] - w.Z[0], d = dx * dx + dz * dz;
                    if (d < best) { best = d; req.Victim = i; }
                }
            }
            if (r.Art == MomentArtKind.Kaiju && id == MomentId.Berserk) req.Kaiju = KaijuState.Berserk;
            if (_override)
            {
                req.Cls = _overrideCls;
                req.Team = _overrideTeam;
                req.Kaiju = _overrideKaiju;
            }
            _queue.Force(req, scale, _clock);
        }

        private void LateUpdate() { var cost = CriticalMass.Runtime.Game.FrameCost.Enter(); LateUpdateCost(); CriticalMass.Runtime.Game.FrameCost.Exit(CriticalMass.Runtime.Game.FrameCost.Slot.BigMoments, cost); }

        void LateUpdateCost()
        {
            bool paused = _input != null && _input.State.Paused;
            // Integration batch 4: one hitch frame (the match-start load, a shader warm-up) advances the director at most
            // MaxFrameSeconds, so a takeover that just started never ends behind a single slow frame unseen.
            if (!paused) _clock += Mathf.Min(Time.unscaledDeltaTime, MaxFrameSeconds);
            double now = _clock;

            DebugKeys();
            if (_galleryAt >= 0) StepGallery(now);

            _queue.Update(now, Banner.IsOpen);
            if (_seen != _queue.Version)
            {
                _seen = _queue.Version;
                if (_queue.Playing) Begin(_queue.Current);
                else Finish();
            }
            if (_seenStinger != _queue.StingerVersion)
            {
                _seenStinger = _queue.StingerVersion;
                if (_queue.StingerPlaying) BeginStinger(_queue.CurrentStinger);
                else if (_stinger != null) _stinger.gameObject.SetActive(false);
            }
            // Integration batch 4: a takeover claims the HUD's centre lane (HudZones top priority); banners, the lockout and
            // autopilot lines, Kaiju callouts and the depth HUD's job line and event timer wait until it ends.
            if (_queue.Playing) HudZones.Hold(HudZone.Callout, HudPriority.Takeover);
            if (_canvas == null) return;
            FitCanvas(_canvas);
            if (_queue.Playing) Animate(now);
            if (_queue.StingerPlaying && _stinger != null) AnimateStinger(now);
            if (_slowed && now >= _slowUntil) RestoreSlowMotion();
        }

        private void DebugKeys()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UnityEngine.InputSystem.Keyboard kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null || !kb.f8Key.wasPressedThisFrame) return;
            if (_galleryAt >= 0) StopAll();
            else PlayGallery();
#endif
        }

        private void StepGallery(double now)
        {
            if (_gallery == null || _galleryAt >= _gallery.Length)
            {
                _galleryAt = -1;
                return;
            }
            if (_queue.Playing || _queue.StingerPlaying || now < _galleryNext) return;
            MomentId id = _gallery[_galleryAt++];
            MomentRecipe r = MomentRecipes.Get(id);
            Force(id, r.Scale);
            _galleryNext = now + (r.Scale == MomentScale.Takeover ? MomentQueue.MaxSeconds : MomentQueue.StingerSeconds) + 0.4;
        }

        // ---------------------------------------------------------------- takeover

        private void Begin(in ActiveMoment m)
        {
            if (_canvas == null) Build();
            MomentRecipe r = MomentRecipes.Get(m.Request.Id);
            _playingId = r.Id;
            _look = BigMomentsSettings.Look();
            MomentRequest q = m.Request;
            _fromRight = q.Team == TeamId.Crimson;
            _accent = Accent(r, q);
            _canvas.gameObject.SetActive(true);
            if (_stinger != null) _stinger.gameObject.SetActive(false);
            _group.alpha = 1f;
            MomentLayers L = r.Layers;

            // freeze (picture hold): captured at the end of this frame, shown from the next
            bool freeze = Has(L, MomentLayers.Freeze) && r.FreezeSeconds > 0f;
            _freeze.gameObject.SetActive(false);
            _captureFrame = -1;
            if (freeze && EnsureFreezeTarget())
            {
                ScreenCapture.CaptureScreenshotIntoRenderTexture(_freezeRt);
                _captureFrame = Time.frameCount;
                _freeze.texture = _freezeRt;
            }
            _flashOk = Has(L, MomentLayers.Flash) && _flashes.TryFlash(_clock);
            _invertOk = Has(L, MomentLayers.Invert) && _captureFrame >= 0 && _look.Invert && _flashes.TryFlash(_clock);   // the invert frame is the frozen picture

            bool grade = Has(L, MomentLayers.Grade) && _liveMat != null;
            _grade.gameObject.SetActive(grade);
            if (grade) ForceColor();
            _dim.gameObject.SetActive(true);
            _dim.color = CMTokens.WithAlpha(CMTokens.VoidNavy.Color, 0f);

            _speed.gameObject.SetActive(Has(L, MomentLayers.SpeedLines));
            _speed.color = CMTokens.WithAlpha(CMTokens.WarmCream.Color, 0f);
            _burst.gameObject.SetActive(Has(L, MomentLayers.Burst));
            _burst.color = CMTokens.WithAlpha(_accent, 0f);

            bool band = Has(L, MomentLayers.Band);
            _band.gameObject.SetActive(band);
            bool end = Has(L, MomentLayers.EndFrame) || r.Band == BandStyle.Horizontal;
            _band.localRotation = Quaternion.Euler(0f, 0f, end ? 0f : (_fromRight ? -DiagonalDeg : DiagonalDeg));
            _bandFill.color = _accent;
            _bandLines.color = CMTokens.WithAlpha(CMTokens.WarmCream.Color, 0.45f);

            SetupPortrait(r, q, Has(L, MomentLayers.Portrait));

            bool title = Has(L, MomentLayers.Title);
            _titleRt.gameObject.SetActive(title);
            if (title) SetupTitle(r, q);

            for (int i = 0; i < _cracks.Length; i++)
            {
                _cracks[i].gameObject.SetActive(Has(L, MomentLayers.Cracks));
                _cracks[i].color = CMTokens.WithAlpha(q.Kaiju == KaijuState.Berserk ? CMTokens.Corruption.Color : CMTokens.WarmCream.Color, 0f);
            }
            _edge.gameObject.SetActive(Has(L, MomentLayers.EdgeGlow));
            _edge.color = CMTokens.WithAlpha(_accent, 0f);
            _auraRt.gameObject.SetActive(Has(L, MomentLayers.Aura));
            _flash.gameObject.SetActive(_flashOk);
            _flash.color = new Color(1f, 1f, 1f, 0f);

            StartSlowMotion(r);
            PlaySound(true, m);
            Started?.Invoke(m);
        }

        private void Finish()
        {
            if (_canvas != null) _canvas.gameObject.SetActive(false);
            if (_grade != null) _grade.gameObject.SetActive(false);
            if (_model != null) _model.End();
            ReleaseColor();
            RestoreSlowMotion();
            if (_playingId != MomentId.None)
            {
                MomentId id = _playingId;
                _playingId = MomentId.None;
                Ended?.Invoke(id);
            }
        }

        private static bool Has(MomentLayers set, MomentLayers l) => (set & l) != 0;

        private static Color Accent(MomentRecipe r, in MomentRequest q)
        {
            if (r.Art == MomentArtKind.Kaiju || r.Id == MomentId.Quake)
            {
                if (q.Kaiju == KaijuState.Berserk) return CMTokens.ParseHex(ArtMapping.GlowBerserk);   // hud-art-1: the art's own berserk glow
                if (q.Kaiju == KaijuState.Azure) return CMTokens.Azure.Color;
                if (q.Kaiju == KaijuState.Crimson) return CMTokens.Crimson.Color;
                return CMTokens.ParseHex(ArtMapping.GlowNeutral);
            }
            return CMTokens.Team(q.Team);
        }

        private void SetupPortrait(MomentRecipe r, in MomentRequest q, bool on)
        {
            if (_model != null) _model.End();   // one live render at a time: the last takeover's model is released first
            _portraitRt.gameObject.SetActive(on);
            if (!on) return;
            _mask.gameObject.SetActive(false);
            _portraitShape.gameObject.SetActive(false);
            _portrait.gameObject.SetActive(true);
            _portrait.color = Color.white;
            Texture tex = null;
            PortraitSource src;
            if (r.Art == MomentArtKind.Kaiju)
            {
                KaijuState s = q.Kaiju == KaijuState.Egg ? KaijuState.Neutral : q.Kaiju;
                src = MomentArt.ResolveKaiju(s, Model().CanRender(ArtMapping.KaijuAdult), true, out Texture2D img);
                if (src == PortraitSource.TeamImage || src == PortraitSource.Painted) tex = img;
                else if (src == PortraitSource.LiveModel) tex = _model.BeginKaiju(s);
                if (tex == null) tex = UiIcons.Get(UiIcons.Adult[s == KaijuState.Azure ? 1 : s == KaijuState.Crimson ? 2 : s == KaijuState.Berserk ? 3 : 0], 4);
            }
            else
            {
                ClassId c = q.Cls;
                int ci = (int)c;
                bool hudIcon = ci >= 0 && ci < UiIcons.Classes.Length;
                src = MomentArt.ResolvePortrait(c, q.Team, Model().CanRender(ModelPortrait.ClassKey(c)), hudIcon, out Texture2D img, out Texture2D mask);
                if (src == PortraitSource.TeamImage || src == PortraitSource.TintedImage || src == PortraitSource.Painted) tex = img;
                if (src == PortraitSource.TintedImage && mask != null)
                {
                    _mask.gameObject.SetActive(true);
                    _mask.texture = mask;
                    _mask.color = CMTokens.Team(q.Team);
                }
                if (src == PortraitSource.LiveModel) tex = _model.BeginClass(c, q.Team);
                if (tex == null && hudIcon) tex = UiIcons.Get(UiIcons.Portrait[ci, q.Team == TeamId.Azure ? 0 : 1], 4);
            }
            if (tex == null)
            {
                _portrait.gameObject.SetActive(false);
                _portraitShape.gameObject.SetActive(true);
                _portraitShape.SetFill(_accent);
            }
            _portrait.texture = tex;
            // hud-art-1: a true cut-out (bm-portrait / bm-kaiju image or the live model) gets the navy drop shadow and breaks out
            // of the band; a legacy 16:9 painting keeps its old square crop and no shadow
            bool cutOut = tex != null && src != PortraitSource.Painted && src != PortraitSource.HudIcon;
            _portraitShadow.gameObject.SetActive(cutOut);
            _portraitShadow.texture = tex;
            // mirror for the right-hand entry (portraits are painted facing right)
            Rect crop = src == PortraitSource.Painted ? MomentArt.PaintedCrop(tex) : new Rect(0f, 0f, 1f, 1f);
            Rect uv = _fromRight ? new Rect(crop.xMax, 0f, -crop.width, 1f) : crop;
            LastPortraitSource = src;
            _portrait.uvRect = uv;
            _mask.uvRect = uv;
            _portraitShadow.uvRect = uv;
            _portraitShadow.rectTransform.anchoredPosition = new Vector2(_fromRight ? -ShadowDx : ShadowDx, ShadowDy);
        }

        /// <summary>The source the last takeover's portrait used (tests, the capture walk's log).</summary>
        public static PortraitSource LastPortraitSource { get; private set; } = PortraitSource.Shape;

        private ModelPortrait Model()
        {
            if (_model == null) _model = new ModelPortrait(transform);
            return _model;
        }

        private void SetupTitle(MomentRecipe r, in MomentRequest q)
        {
            string words = r.Id == MomentId.Hatch && q.Kaiju == KaijuState.Berserk ? MomentWords.Title(MomentId.Berserk) : MomentWords.Title(r.Id);
            HudText.Set(_title, words);
            string sub = Subtitle(r, q);
            HudText.Set(_subtitle, sub);
            _subtitle.gameObject.SetActive(!string.IsNullOrEmpty(sub));
            Texture2D plate = MomentArt.Load(MomentArt.PlateName(r.Plate));
            _plate.gameObject.SetActive(plate != null);
            _plateShape.gameObject.SetActive(plate == null);
            Texture2D accentBar = MomentArt.Load(MomentArt.PlateAccent);
            _plateAccent.gameObject.SetActive(plate != null && accentBar != null);
            if (accentBar != null)
            {
                _plateAccent.texture = accentBar;
                _plateAccent.color = _accent;
                _plateAccent.uvRect = _fromRight ? new Rect(1f, 0f, -1f, 1f) : new Rect(0f, 0f, 1f, 1f);
            }
            if (plate != null)
            {
                _plate.texture = plate;
                _plate.color = CMTokens.VoidNavy.Color;
                _plate.uvRect = _fromRight ? new Rect(1f, 0f, -1f, 1f) : new Rect(0f, 0f, 1f, 1f);
            }
            else
            {
                _plateShape.SetFill(CMTokens.VoidNavy.Color);
                _plateShape.SetRim(_accent);
            }
            _title.color = CMTokens.WarmCream.Color;
        }

        private string Subtitle(MomentRecipe r, in MomentRequest q)
        {
            if (Has(r.Layers, MomentLayers.EndFrame) || r.Id == MomentId.MatchPoint) return MomentWords.TeamWord(q.Team);
            if (r.Art == MomentArtKind.Kaiju)
            {
                if (r.Id == MomentId.Hatch || r.Id == MomentId.AllegianceTurns)
                    return q.Kaiju == KaijuState.Azure ? KaijuMomentTracker.WordSidesWith + "AZURE" :
                        q.Kaiju == KaijuState.Crimson ? KaijuMomentTracker.WordSidesWith + "CRIMSON" :
                        q.Kaiju == KaijuState.Berserk ? KaijuMomentTracker.WordCorruptionWon.ToUpperInvariant() :
                        KaijuMomentTracker.WordSidesWith + KaijuMomentTracker.WordNobody;
                return null;
            }
            return q.PlayerOwned ? "YOU" : MomentWords.TeamWord(q.Team) + " " + MomentArt.ClassName(q.Cls).ToUpperInvariant();
        }

        private void Animate(double now)
        {
            ActiveMoment m = _queue.Current;
            MomentRecipe r = MomentRecipes.Get(m.Request.Id);
            float t = (float)(now - m.StartedAt), S = m.Seconds;
            bool motion = _look.Motion;
            float freezeS = _captureFrame >= 0 ? Mathf.Min(r.FreezeSeconds, S * 0.25f) : 0f;
            float tb = t - freezeS;                    // band time (after the hold)
            float outIn = S - t;                       // seconds left
            float exit = Mathf.Clamp01(1f - outIn / 0.18f);
            _group.alpha = 1f - Ease(Mathf.Clamp01(1f - outIn / 0.15f));

            // freeze + invert
            bool showFreeze = _captureFrame >= 0 && Time.frameCount > _captureFrame && t < freezeS;
            if (_freeze.gameObject.activeSelf != showFreeze) _freeze.gameObject.SetActive(showFreeze);
            if (showFreeze && _frozenMat != null)
            {
                _frozenMat.SetFloat(IdInvert, _invertOk && t < 0.05f ? 1f : 0f);
                SetHoles(_frozenMat, m.Request);
            }

            // live grade (world desaturated, attacker and victim in colour)
            if (_grade.gameObject.activeSelf)
            {
                float g = 1f - Mathf.Clamp01((t - (S - 0.3f)) / 0.3f);
                _liveMat.SetFloat(IdSaturation, Mathf.Lerp(1f, 0.15f, g));
                _liveMat.SetFloat(IdBrightness, Mathf.Lerp(1f, 0.8f, g));
                _liveMat.SetFloat(IdInvert, 0f);
                SetHoles(_liveMat, m.Request);
            }

            bool end = Has(r.Layers, MomentLayers.EndFrame);
            _dim.color = CMTokens.WithAlpha(CMTokens.VoidNavy.Color, (end ? 0.55f : 0.32f) * Mathf.Clamp01(tb / 0.12f));

            if (_flash.gameObject.activeSelf)
            {
                float f = Mathf.Clamp01(1f - tb / Mathf.Max(0.01f, _look.FlashSeconds));
                _flash.color = new Color(1f, 1f, 1f, tb < 0f ? 0f : _look.FlashAlpha * f * f);
            }

            float enter = Ease(Mathf.Clamp01(tb / 0.18f));
            float side = _fromRight ? 1f : -1f;
            if (_speed.gameObject.activeSelf)
            {
                _speed.color = CMTokens.WithAlpha(CMTokens.WarmCream.Color, _look.SpeedLineAlpha * enter);
                float s = motion ? Mathf.Lerp(1.15f, 1f, enter) : 1f;
                _speed.rectTransform.localScale = new Vector3(s, s, 1f);
                if (motion) _speed.rectTransform.localRotation = Quaternion.Euler(0f, 0f, t * 25f * side);
            }

            if (_band.gameObject.activeSelf)
            {
                float x = motion ? Mathf.Lerp(side * BandLength, 0f, enter) + Mathf.Lerp(0f, -side * BandLength, Ease(exit)) : 0f;
                _band.anchoredPosition = new Vector2(x, end ? 40f : 0f);
                float a = motion ? 1f : enter * (1f - exit);
                _bandFill.color = CMTokens.WithAlpha(_accent, a);
                _bandLines.color = CMTokens.WithAlpha(CMTokens.WarmCream.Color, 0.22f * a);
                if (motion)
                {
                    Rect uv = _bandLines.uvRect;
                    uv.x = Mathf.Repeat(t * 1.6f * -side, 1f);
                    _bandLines.uvRect = uv;
                }
            }

            if (_portraitRt.gameObject.activeSelf)
            {
                // hud-art-1: the character springs in from its side (back-out ease, a small overshoot), drifts while it holds, then
                // leaves the way the band leaves (ease-in) in the last PortraitOut seconds; the drop shadow follows it
                float pIn = Mathf.Clamp01((tb - 0.04f) / PortraitIn);
                float pe = BackOut(pIn);
                float po = EaseIn(Mathf.Clamp01(1f - outIn / PortraitOut));
                float baseX = side * 500f;
                float x = motion ? baseX + side * Mathf.LerpUnclamped(760f, 0f, pe) - side * 40f * Mathf.Clamp01(tb / S) - side * 1100f * po : baseX;
                _portraitRt.anchoredPosition = new Vector2(x, end ? 60f : 40f);
                float sc = motion ? Mathf.Lerp(1.08f, 1f, Ease(pIn)) : 1f;
                _portraitRt.localScale = new Vector3(sc, sc, 1f);
                float a = motion ? (pIn > 0f ? 1f : 0f) : Ease(pIn) * (1f - exit);
                _portrait.color = new Color(1f, 1f, 1f, a);
                if (_portraitShadow.gameObject.activeSelf) _portraitShadow.color = CMTokens.WithAlpha(CMTokens.VoidNavy.Color, 0.55f * a);
                if (_mask.gameObject.activeSelf) _mask.color = CMTokens.WithAlpha(CMTokens.Team(m.Request.Team), a);
                if (_model != null && _model.Rendering) _model.Pose(Mathf.Clamp01(t / S), motion);
            }

            if (_burst.gameObject.activeSelf)
            {
                float be = Ease(Mathf.Clamp01((tb - 0.04f) / 0.16f));
                _burst.color = CMTokens.WithAlpha(Color.Lerp(_accent, Color.white, 0.35f), 0.9f * be * (1f - exit));
                float s = motion ? Mathf.Lerp(0.5f, 1f, be) : 1f;
                _burst.rectTransform.localScale = new Vector3(s, s, 1f);
                _burst.rectTransform.anchoredPosition = new Vector2(side * 520f, 30f);
                if (motion) _burst.rectTransform.localRotation = Quaternion.Euler(0f, 0f, t * 12f);
            }

            if (_titleRt.gameObject.activeSelf)
            {
                float te = Mathf.Clamp01((tb - 0.1f) / 0.14f);
                float s = motion ? Mathf.Lerp(1.3f, 1f, Ease(te)) : 1f;
                _titleRt.localScale = new Vector3(s, s, 1f);
                _titleRt.anchoredPosition = new Vector2(end ? 0f : -side * 260f, end ? -170f : -150f);
                float a = te > 0f ? (motion ? 1f : te) : 0f;
                SetTitleAlpha(a * (1f - exit));
            }

            for (int i = 0; i < _cracks.Length; i++)
            {
                if (!_cracks[i].gameObject.activeSelf) break;
                float ce = Ease(Mathf.Clamp01(tb / 0.15f));
                _cracks[i].color = CMTokens.WithAlpha(_cracks[i].color, 0.9f * ce);
                float s = motion ? Mathf.Lerp(0.7f, 1f, ce) : 1f;
                _cracks[i].rectTransform.localScale = new Vector3(s, s, 1f);
            }

            if (_edge.gameObject.activeSelf)
            {
                // a slow breathe (well under 3 Hz), still with reduced motion
                // hud-art-1: a frame, not a wash (the owner's captures were flooded with the accent at 0.6..0.8)
                float pulse = motion ? 0.26f + 0.08f * Mathf.Sin(t * Mathf.PI * 2f * 1.5f) : 0.26f;
                _edge.color = CMTokens.WithAlpha(_accent, pulse * Mathf.Clamp01(tb / 0.12f));
            }

            if (_auraRt.gameObject.activeSelf) AnimateAura(m.Request, t, motion);
        }

        private void SetTitleAlpha(float a)
        {
            _title.color = CMTokens.WithAlpha(CMTokens.WarmCream.Color, a);
            _subtitle.color = CMTokens.WithAlpha(CMTokens.WarmCream.Color, a);
            if (_plate.gameObject.activeSelf) _plate.color = CMTokens.WithAlpha(CMTokens.VoidNavy.Color, a);
            if (_plateAccent.gameObject.activeSelf) _plateAccent.color = CMTokens.WithAlpha(_accent, a);
            if (_plateShape.gameObject.activeSelf) _plateShape.SetFill(CMTokens.WithAlpha(CMTokens.VoidNavy.Color, a));
        }

        private void AnimateAura(in MomentRequest q, float t, bool motion)
        {
            Vector2 p = Vector2.zero;
            if (ActorCanvasPoint(q.Actor, out Vector2 cp)) p = cp;
            _auraRt.anchoredPosition = p;
            for (int i = 0; i < _rings.Length; i++)
            {
                float ph = motion ? Mathf.Repeat(t / 0.7f + i / (float)_rings.Length, 1f) : (i + 1f) / (_rings.Length + 1f);
                float s = Mathf.Lerp(0.4f, 1.5f, ph);
                _rings[i].rectTransform.localScale = new Vector3(s, s * 0.45f, 1f);   // a ground ring seen from the high camera
                _rings[i].color = CMTokens.WithAlpha(_accent, (1f - ph) * 0.85f);
            }
            for (int i = 0; i < _sparks.Length; i++)
            {
                float seed = i * 0.6180339f;
                float ph = motion ? Mathf.Repeat(t / 0.9f + seed, 1f) : Mathf.Repeat(seed, 1f);
                float ang = seed * Mathf.PI * 2f;
                _sparks[i].rectTransform.anchoredPosition = new Vector2(Mathf.Cos(ang) * 80f, -20f + ph * 220f);
                _sparks[i].color = CMTokens.WithAlpha(Color.Lerp(_accent, Color.white, 0.5f), Mathf.Sin(ph * Mathf.PI));
            }
        }

        // ---------------------------------------------------------------- stinger

        private void BeginStinger(in ActiveMoment m)
        {
            if (_canvas == null) Build();
            _canvas.gameObject.SetActive(true);
            _group.alpha = 1f;
            // only the corner shows: every takeover layer off
            SetTakeoverLayers(false);
            _stinger.gameObject.SetActive(true);
            _look = BigMomentsSettings.Look();
            MomentRecipe r = MomentRecipes.Get(m.Request.Id);
            MomentRequest q = m.Request;
            Color team = r.Art == MomentArtKind.Kaiju ? Accent(r, q) : CMTokens.Team(q.Team);
            HudText.Set(_stingerTitle, r.Id == MomentId.Hatch && q.Kaiju == KaijuState.Berserk ? MomentWords.Title(MomentId.Berserk) : MomentWords.Title(r.Id));
            HudText.Set(_stingerSub, Subtitle(r, q) ?? string.Empty);
            Texture2D plate = MomentArt.Load(MomentArt.PlateName(PlateKind.Stinger));
            _stingerPlate.gameObject.SetActive(plate != null);
            _stingerShape.gameObject.SetActive(plate == null);
            if (plate != null)
            {
                _stingerPlate.texture = plate;
                _stingerPlate.color = team;
            }
            else _stingerShape.SetFill(team);
            int ci = (int)q.Cls;
            Texture2D icon = r.Art == MomentArtKind.Kaiju ? UiIcons.Get(UiIcons.Hatchling, 2) :
                ci >= 0 && ci < UiIcons.Classes.Length ? UiIcons.Get(UiIcons.Portrait[ci, q.Team == TeamId.Azure ? 0 : 1], 2) : null;
            _stingerIcon.texture = icon;
            _stingerIcon.gameObject.SetActive(icon != null);
            PlaySound(false, m);
            Started?.Invoke(m);
        }

        private void AnimateStinger(double now)
        {
            if (!_queue.Playing) _canvas.gameObject.SetActive(true);
            ActiveMoment m = _queue.CurrentStinger;
            float t = (float)(now - m.StartedAt), S = m.Seconds;
            float e = Ease(Mathf.Clamp01(t / 0.16f)), x = Mathf.Clamp01(1f - (S - t) / 0.2f);
            bool motion = _look.Motion;
            _stinger.anchoredPosition = new Vector2(-StingerRight + (motion ? (1f - e) * 300f + x * 300f : 0f), -StingerTop);
            _stingerGroup.alpha = motion ? 1f - x : e * (1f - x);
        }

        private void SetTakeoverLayers(bool on)
        {
            if (on) return;
            _freeze.gameObject.SetActive(false);
            _dim.gameObject.SetActive(false);
            _speed.gameObject.SetActive(false);
            _band.gameObject.SetActive(false);
            _burst.gameObject.SetActive(false);
            _portraitRt.gameObject.SetActive(false);
            _titleRt.gameObject.SetActive(false);
            for (int i = 0; i < _cracks.Length; i++) _cracks[i].gameObject.SetActive(false);
            _edge.gameObject.SetActive(false);
            _auraRt.gameObject.SetActive(false);
            _flash.gameObject.SetActive(false);
            if (_grade != null) _grade.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- helpers

        private static readonly int IdInvert = Shader.PropertyToID("_Invert"), IdSaturation = Shader.PropertyToID("_Saturation"),
            IdBrightness = Shader.PropertyToID("_Brightness"), IdHoleA = Shader.PropertyToID("_HoleA"), IdHoleB = Shader.PropertyToID("_HoleB"),
            IdAspect = Shader.PropertyToID("_Aspect"), IdFrozen = Shader.PropertyToID("_Frozen"), IdFlipY = Shader.PropertyToID("_FlipY");

        private void SetHoles(Material mat, in MomentRequest q)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) return;
            mat.SetFloat(IdAspect, cam.pixelHeight > 0 ? cam.pixelWidth / (float)cam.pixelHeight : 1.7778f);
            mat.SetVector(IdHoleA, Hole(cam, q.Actor));
            mat.SetVector(IdHoleB, Hole(cam, q.Victim));
        }

        private Vector4 Hole(UnityEngine.Camera cam, int actor)
        {
            World w = World;
            if (w == null || actor < 0 || actor >= w.ActorCount) return Vector4.zero;
            Vector3 vp = cam.WorldToViewportPoint(ActorWorld(w, actor));
            if (vp.z <= 0f) return Vector4.zero;
            return new Vector4(vp.x, vp.y, 0.09f, 1f);
        }

        private static Vector3 ActorWorld(World w, int i)
        {
            double x = w.X[i], z = w.Z[i];
            return SimSpace.ToWorld(x, GroundHeight.At(w.Map, x, z) + 1.0, z);
        }

        /// <summary>The actor's position in the takeover root's canvas units (centre origin).</summary>
        private bool ActorCanvasPoint(int actor, out Vector2 p)
        {
            p = Vector2.zero;
            World w = World;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (w == null || cam == null || actor < 0 || actor >= w.ActorCount) return false;
            Vector3 vp = cam.WorldToViewportPoint(ActorWorld(w, actor));
            if (vp.z <= 0f) return false;
            Rect r = _root.rect;
            p = new Vector2((vp.x - 0.5f) * r.width, (vp.y - 0.5f) * r.height);
            return true;
        }

        private bool EnsureFreezeTarget()
        {
            if (_frozenMat == null) return false;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return false;   // integration batch 3
            int w = Screen.width, h = Screen.height;
            if (w < 16 || h < 16) return false;
            if (_freezeRt != null && (_freezeRt.width != w || _freezeRt.height != h))
            {
                _freezeRt.Release();
                Destroy(_freezeRt);
                _freezeRt = null;
            }
            if (_freezeRt == null)
            {
                _freezeRt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "BM Freeze", hideFlags = HideFlags.DontSave };
                _freezeRt.Create();
            }
            // the capture lands upside down where UVs start at the top (Metal, D3D)
            _frozenMat.SetFloat(IdFlipY, SystemInfo.graphicsUVStartsAtTop ? 1f : 0f);
            return true;
        }

        private void ForceColor()
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) return;
            var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
            if (data.requiresColorTexture) return;
            data.requiresColorTexture = true;
            _colorForced = true;
        }

        private void ReleaseColor()
        {
            if (!_colorForced) return;
            _colorForced = false;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) return;
            // GameHud's death grade wants the opaque copy while the local player is dead: leave it on then
            World w = World;
            bool dead = w != null && w.ActorCount > 0 && !w.Actors[0].Alive;
            UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam).requiresColorTexture = dead;
        }

        /// <summary>
        /// Optional real slow motion (setting, default off; single player only): the driver's TimeScale is wall pacing only
        /// (the same ticks run either way, so the sim result is unchanged), and capture mode (TimeScale 0) is never touched.
        /// </summary>
        private void StartSlowMotion(MomentRecipe r)
        {
            if (!_look.SlowMotion || _driver == null || SimDriverBehaviour.CaptureMode || _slowed) return;
            if (!(r.FreezeSeconds > 0f) || _driver.TimeScale <= 0) return;
            _savedScale = _driver.TimeScale;
            _driver.TimeScale = _savedScale * SlowMotionFactor;
            _slowed = true;
            _slowUntil = _clock + r.FreezeSeconds + SlowMotionTail;
        }

        private void RestoreSlowMotion()
        {
            if (!_slowed) return;
            _slowed = false;
            // only undo our own change (a pace change made meanwhile wins)
            if (_driver != null && Math.Abs(_driver.TimeScale - _savedScale * SlowMotionFactor) < 1e-9) _driver.TimeScale = _savedScale;
        }

        private void PlaySound(bool takeover, in ActiveMoment m)
        {
            if (!_clipsLoaded)
            {
                _clipsLoaded = true;
                _takeoverClip = Resources.Load<AudioClip>(AudioTakeover);
                _stingerClip = Resources.Load<AudioClip>(AudioStinger);
            }
            AudioClip clip = takeover ? _takeoverClip : _stingerClip;
            if (clip != null)
            {
                if (_audio == null)
                {
                    _audio = gameObject.AddComponent<AudioSource>();
                    _audio.playOnAwake = false;
                    _audio.spatialBlend = 0f;
                    _audio.outputAudioMixerGroup = Menu.GameSettings.EffectsGroup;
                }
                _audio.PlayOneShot(clip);
            }
            try
            {
                if (takeover) BigMomentsAudio.Sink?.OnTakeover(m.Request.Id, m.Seconds, m.Request.PlayerOwned);
                else BigMomentsAudio.Sink?.OnStinger(m.Request.Id);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>hud-art-1: back-out (overshoot about 8 %, then settle) for the character's entry.</summary>
        public static float BackOut(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.2f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        /// <summary>hud-art-1: ease-in (cubic) for exits.</summary>
        public static float EaseIn(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t;
        }

        private static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            float u = 1f - t;
            return 1f - u * u * u;
        }

        private void FitCanvas(Canvas c)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null && c.worldCamera != cam) c.worldCamera = cam;
            if (cam != null && _gradeCanvas != null && _gradeCanvas.worldCamera != cam) _gradeCanvas.worldCamera = cam;
            float pw = cam != null ? cam.pixelWidth : Screen.width, ph = cam != null ? cam.pixelHeight : Screen.height;
            if (pw < 1 || ph < 1) return;
            float s = HudLayout.Scale(pw, ph);
            if (Mathf.Abs(c.scaleFactor - s) > 1e-4f) c.scaleFactor = s;
            if (_gradeCanvas != null && Mathf.Abs(_gradeCanvas.scaleFactor - s) > 1e-4f) _gradeCanvas.scaleFactor = s;
        }

        // ---------------------------------------------------------------- build (once, on the first moment)

        private Canvas MakeCanvas(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            Canvas c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.sortingOrder = order;
            c.planeDistance = 0.5f;
            c.worldCamera = UnityEngine.Camera.main;
            return c;
        }

        private void Build()
        {
            _canvas = MakeCanvas("Big Moments Canvas", SortingOrder);
            _group = _canvas.gameObject.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;
            _root = Rt(_canvas.transform, "Takeover");
            Stretch(_root);

            Shader sh = Resources.Load<Shader>(GradeShader);
            if (sh != null && sh.isSupported)
            {
                _frozenMat = new Material(sh) { name = "BM Frozen Grade", hideFlags = HideFlags.DontSave };
                _frozenMat.SetFloat(IdFrozen, 1f);
                _frozenMat.SetFloat(IdSaturation, 0.15f);
                _frozenMat.SetFloat(IdBrightness, 0.8f);
                _liveMat = new Material(sh) { name = "BM Live Grade", hideFlags = HideFlags.DontSave };
                _liveMat.SetFloat(IdFrozen, 0f);
            }

            // below the HUD: the live grade samples the world only
            _gradeCanvas = MakeCanvas("Big Moments Grade Canvas", GradeSortingOrder);
            CanvasGroup gg = _gradeCanvas.gameObject.AddComponent<CanvasGroup>();
            gg.interactable = false;
            gg.blocksRaycasts = false;
            _grade = Raw(_gradeCanvas.transform, "World Grade", null, Color.white);
            Stretch(_grade.rectTransform);
            _grade.material = _liveMat;
            _grade.gameObject.SetActive(false);

            _freeze = Raw(_root, "Freeze", null, Color.white);
            Stretch(_freeze.rectTransform);
            _freeze.material = _frozenMat;
            _dim = Raw(_root, "Dim", null, CMTokens.VoidNavy.Color);
            Stretch(_dim.rectTransform);
            _speed = Raw(_root, "Speed Lines", ProceduralSheets.SheetOrFallback(MomentArt.SpeedRadial), Color.white);
            Centre(_speed.rectTransform, Vector2.zero, new Vector2(2400f, 2400f));
            _edge = Raw(_root, "Edge Glow", ProceduralSheets.SheetOrFallback(MomentArt.EdgeGlow), Color.white);
            Stretch(_edge.rectTransform);
            for (int i = 0; i < _cracks.Length; i++)
            {
                _cracks[i] = Raw(_root, "Cracks " + i, ProceduralSheets.SheetOrFallback(MomentArt.CracksCorner), Color.white);
                RectTransform rt = _cracks[i].rectTransform;
                // 0 top-left, 1 top-right, 2 bottom-left, 3 bottom-right; the sheet is the top-left corner, mirrored by scale
                Vector2 a = new Vector2(i % 2 == 0 ? 0f : 1f, i < 2 ? 1f : 0f);
                rt.anchorMin = rt.anchorMax = a;
                rt.pivot = a;
                rt.sizeDelta = new Vector2(620f, 620f);
                rt.anchoredPosition = Vector2.zero;
                // mirror by UV (not by negative scale, which would swing the image off screen around the corner pivot)
                _cracks[i].uvRect = new Rect(i % 2 == 0 ? 0f : 1f, i < 2 ? 0f : 1f, i % 2 == 0 ? 1f : -1f, i < 2 ? 1f : -1f);
            }

            _band = Rt(_root, "Band");
            Centre(_band, Vector2.zero, new Vector2(BandLength, BandThick));
            _bandFill = Raw(_band, "Band Fill", ProceduralSheets.SheetOrFallback(MomentArt.Band), Color.white);
            Stretch(_bandFill.rectTransform);
            _bandFill.uvRect = new Rect(0f, 0f, BandLength / 1400f, 1f);
            _bandLines = Raw(_band, "Band Lines", ProceduralSheets.SheetOrFallback(MomentArt.SpeedBand), Color.white);
            Stretch(_bandLines.rectTransform);
            _bandLines.uvRect = new Rect(0f, 0f, BandLength / 1400f, 1f);

            _burst = Raw(_root, "Burst", ProceduralSheets.SheetOrFallback(MomentArt.Burst), Color.white);
            Centre(_burst.rectTransform, Vector2.zero, new Vector2(760f, 760f));

            _auraRt = Rt(_root, "Aura");
            Centre(_auraRt, Vector2.zero, new Vector2(10f, 10f));
            for (int i = 0; i < _rings.Length; i++)
            {
                _rings[i] = Raw(_auraRt, "Ring " + i, ProceduralSheets.SheetOrFallback(MomentArt.AuraRing), Color.white);
                Centre(_rings[i].rectTransform, Vector2.zero, new Vector2(260f, 260f));
            }
            for (int i = 0; i < _sparks.Length; i++)
            {
                _sparks[i] = Raw(_auraRt, "Spark " + i, ProceduralSheets.SheetOrFallback(MomentArt.Spark), Color.white);
                Centre(_sparks[i].rectTransform, Vector2.zero, new Vector2(34f, 34f));
            }

            _portraitRt = Rt(_root, "Portrait");
            Centre(_portraitRt, Vector2.zero, new Vector2(PortraitSize, PortraitSize));
            _portraitShape = MenuShape(_portraitRt, "Portrait Shape", ShapeKind.Shield, new Vector2(PortraitSize * 0.55f, PortraitSize * 0.7f));
            _portraitShadow = Raw(_portraitRt, "Portrait Shadow", null, CMTokens.WithAlpha(CMTokens.VoidNavy.Color, 0.55f));
            Stretch(_portraitShadow.rectTransform);
            _portraitShadow.gameObject.SetActive(false);
            _portrait = Raw(_portraitRt, "Portrait Image", null, Color.white);
            Stretch(_portrait.rectTransform);
            _mask = Raw(_portraitRt, "Portrait Team Mask", null, Color.white);
            Stretch(_mask.rectTransform);

            _titleRt = Rt(_root, "Title");
            Centre(_titleRt, Vector2.zero, new Vector2(TitleW, TitleH));
            _plateShape = MenuShape(_titleRt, "Plate Shape", ShapeKind.Chamfer, new Vector2(TitleW * 0.92f, TitleH * 0.7f));
            _plateShape.Radius = 30f;
            _plate = Raw(_titleRt, "Plate", null, Color.white);
            Stretch(_plate.rectTransform);
            _plateAccent = Raw(_titleRt, "Plate Accent", null, Color.white);
            Stretch(_plateAccent.rectTransform);
            _plateAccent.gameObject.SetActive(false);
            _title = CriticalMass.Runtime.Menu.MenuUi.Fit(HudText.Make(_titleRt, "Title Text", CMTokens.Display, UiScale.TypeHero, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter, 5f), 96);
            Centre(_title.rectTransform, new Vector2(0f, 14f), new Vector2(TitleW * 0.8f, 120f));
            _title.resizeTextMinSize = 40;
            _title.resizeTextMaxSize = 96;
            CriticalMass.Runtime.Menu.MenuUi.Contain(_title, 1);   // hud-containment-1: one rule (shrink, then ellipsis)
            _subtitle = CriticalMass.Runtime.Menu.MenuUi.Fit(HudText.Make(_titleRt, "Subtitle", CMTokens.Ui, UiScale.TypeLead, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter, 3f), 34);
            Centre(_subtitle.rectTransform, new Vector2(0f, -72f), new Vector2(TitleW * 0.8f, 44f));

            _flash = Raw(_root, "Flash", null, Color.white);
            Stretch(_flash.rectTransform);

            // corner stinger (top right, under the kill feed)
            _stinger = Rt(_canvas.transform, "Stinger");
            _stinger.anchorMin = _stinger.anchorMax = new Vector2(1f, 1f);
            _stinger.pivot = new Vector2(1f, 1f);
            _stinger.sizeDelta = new Vector2(StingerW, StingerH);
            _stingerGroup = _stinger.gameObject.AddComponent<CanvasGroup>();
            _stingerGroup.interactable = false;
            _stingerGroup.blocksRaycasts = false;
            _stingerShape = MenuShape(_stinger, "Stinger Shape", ShapeKind.Chamfer, new Vector2(StingerW, StingerH * 0.8f));
            _stingerShape.Radius = 18f;
            _stingerPlate = Raw(_stinger, "Stinger Plate", null, Color.white);
            Stretch(_stingerPlate.rectTransform);
            _stingerIcon = Raw(_stinger, "Stinger Icon", null, Color.white);
            _stingerIcon.rectTransform.anchorMin = _stingerIcon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            _stingerIcon.rectTransform.pivot = new Vector2(0f, 0.5f);
            _stingerIcon.rectTransform.sizeDelta = new Vector2(104f, 104f);
            _stingerIcon.rectTransform.anchoredPosition = new Vector2(24f, 0f);
            _stingerTitle = CriticalMass.Runtime.Menu.MenuUi.Fit(HudText.Make(_stinger, "Stinger Title", CMTokens.Display, UiScale.TypeDisplay, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft, 4f), 46);
            Place(_stingerTitle.rectTransform, new Vector2(140f, 14f), new Vector2(StingerW - 170f, 56f));
            _stingerSub = HudText.Make(_stinger, "Stinger Sub", CMTokens.Ui, UiScale.TypeBody, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft, 2.5f);
            Place(_stingerSub.rectTransform, new Vector2(140f, -34f), new Vector2(StingerW - 170f, 30f));
            _stinger.gameObject.SetActive(false);

            _canvas.gameObject.SetActive(false);
        }

        private static RectTransform Rt(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        private static void Centre(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }

        private static void Place(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }

        private static RawImage Raw(Transform parent, string name, Texture tex, Color c)
        {
            RectTransform rt = Rt(parent, name);
            RawImage img = rt.gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        private static VectorShape MenuShape(Transform parent, string name, ShapeKind kind, Vector2 size)
        {
            RectTransform rt = Rt(parent, name);
            VectorShape s = rt.gameObject.AddComponent<VectorShape>();
            s.Kind = kind;
            s.raycastTarget = false;
            Centre(rt, Vector2.zero, size);
            return s;
        }
    }
}
