using System;
using System.Globalization;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CriticalMass.Runtime.View
{
    /// <summary>The Kaiju's big moments, in the order a match meets them.</summary>
    public enum KaijuMomentKind { None, HatchSoon, BerserkLocked, Hatch, KnockedOut, Recovered }

    /// <summary>
    /// Reads the sim's Kaiju story frame by frame (pure, testable, view only): the hatch-soon callout at 30 s, the egg's
    /// crack stage, the berserk lock, the hatch (its outcome, the allegiance tally behind it and the tick it happened on,
    /// for the 18-tick spotlight), knock-outs and recoveries. Events come from the World's event ring, read from a cursor
    /// the way GameHud reads it, so several ticks per frame (8x) never miss one; a new match (Epoch) starts it over.
    /// </summary>
    public sealed class KaijuMomentTracker
    {
        public const double HatchSoonSeconds = 30;
        /// <summary>The hatch spotlight: 18 sim ticks = 0.6 s at SimHz 30.</summary>
        public const int SpotlightTicks = 18;
        /// <summary>Seconds before the hatch each crack stage starts (stage 1, 2, 3).</summary>
        public static readonly double[] CrackAt = { 60, 30, 10 };
        public const int MaxPending = 8;

        private int _epoch = int.MinValue;
        private int _cursor = -1;
        private int _lastTick;
        private bool _soonFired;
        private readonly KaijuMomentKind[] _pending = new KaijuMomentKind[MaxPending];
        private int _pendingCount;

        /// <summary>Moments raised by the last Observe (oldest first).</summary>
        public int PendingCount => _pendingCount;
        public KaijuMomentKind Pending(int i) => _pending[i];

        public bool HatchSeen { get; private set; }
        /// <summary>World.Tick of the tick the hatch ran on (spotlight frame 0), -1 before the hatch.</summary>
        public int HatchTick { get; private set; } = -1;
        public KaijuState HatchState { get; private set; }
        public double HatchX { get; private set; }
        public double HatchZ { get; private set; }
        /// <summary>Allegiance at the hatch (the sim freezes it from the hatch on).</summary>
        public double HatchAzure { get; private set; }
        public double HatchCrimson { get; private set; }
        public bool BerserkLocked { get; private set; }

        public void Reset()
        {
            _cursor = -1;
            _soonFired = false;
            _pendingCount = 0;
            HatchSeen = false;
            HatchTick = -1;
            BerserkLocked = false;
        }

        public void Observe(World w)
        {
            _pendingCount = 0;
            if (w == null) return;
            if (w.Epoch != _epoch)
            {
                _epoch = w.Epoch;
                Reset();
                _lastTick = w.Tick;
            }
            MatchState m = w.Match;
            GameConfig c = w.Config;
            BerserkLocked = m.BerserkLocked;

            if (_cursor < 0 || _cursor > w.EventHead) _cursor = Math.Max(0, w.EventHead - World.EventCapacity);
            if (w.EventHead - _cursor > World.EventCapacity) _cursor = w.EventHead - World.EventCapacity;
            for (; _cursor < w.EventHead; _cursor++)
            {
                SimEvent e = w.Events[_cursor % World.EventCapacity];
                switch (e.Type)
                {
                    case SimEventType.BerserkLocked: Raise(KaijuMomentKind.BerserkLocked); break;
                    case SimEventType.Hatch: SeeHatch(w, StateOfCode((int)Math.Round(e.Value)), e.X, e.Z); break;
                    case SimEventType.KaijuKO: Raise(KaijuMomentKind.KnockedOut); break;
                    case SimEventType.KaijuRecover: Raise(KaijuMomentKind.Recovered); break;
                }
            }
            // The ring can be lapped in theory: the flag is the fallback.
            if (m.Hatched && !HatchSeen) SeeHatch(w, w.Kaiju.State, w.Kaiju.X, w.Kaiju.Z);

            if (!_soonFired && !m.Hatched && c.HatchSeconds - m.Time <= HatchSoonSeconds && c.HatchSeconds - m.Time > 0)
            {
                _soonFired = true;
                Raise(KaijuMomentKind.HatchSoon);
            }
            _lastTick = w.Tick;
        }

        private void SeeHatch(World w, KaijuState state, double x, double z)
        {
            if (HatchSeen) return;
            HatchSeen = true;
            HatchState = state;
            HatchX = x;
            HatchZ = z;
            HatchAzure = w.Match.Allegiance.Azure;
            HatchCrimson = w.Match.Allegiance.Crimson;
            // Several ticks can run between two frames: a natural hatch ran on the tick that started at HatchSeconds, so
            // count back from the clock; a forced one is placed on the oldest tick it could have been.
            int ran = Math.Max(0, w.Tick - _lastTick - 1);
            double hz = w.Config.SimHz;
            double since = w.Match.Time - w.Config.HatchSeconds;
            int back = since >= -1e-6 ? (int)Math.Round(since * hz) - 1 : ran;
            if (back < 0) back = 0;
            if (back > ran) back = ran;
            HatchTick = w.Tick - back;
            Raise(KaijuMomentKind.Hatch);
        }

        private void Raise(KaijuMomentKind k)
        {
            if (_pendingCount < MaxPending) _pending[_pendingCount++] = k;
        }

        /// <summary>The 'hatch' event value (Kaiju.HatchCode) back to a state.</summary>
        public static KaijuState StateOfCode(int code) =>
            code == 1 ? KaijuState.Azure : code == 2 ? KaijuState.Crimson : code == 3 ? KaijuState.Berserk : KaijuState.Neutral;

        /// <summary>True on the 18 ticks from the hatch tick on.</summary>
        public bool SpotlightActive(int tick) => HatchTick >= 0 && tick >= HatchTick && tick - HatchTick < SpotlightTicks;

        /// <summary>0 before 60 s to the hatch, then 1, 2 and 3 at 60, 30 and 10 s; 0 once hatched.</summary>
        public static int CrackStage(double untilHatch, bool hatched)
        {
            if (hatched) return 0;
            int s = 0;
            for (int i = 0; i < CrackAt.Length; i++) if (untilHatch <= CrackAt[i]) s = i + 1;
            return s;
        }

        // ---------------------------------------------------------------- words (built-in; the words pack's lines)

        public const string WordSidesWith = "THE KAIJU SIDES WITH ";
        public const string WordNobody = "NOBODY";
        public const string WordBerserk = "BERSERK";
        public const string WordCorruptionWon = "corruption won";
        public const string WordFed = "fed ";
        public const string WordTo = " to ";
        public const string WordDeadEven = "dead even at ";
        public const string WordEggCracking = "THE EGG IS CRACKING!";
        public const string WordEggCorrupted = "THE EGG IS CORRUPTED";
        public const string WordEggCorruptedReason = "too much corruption: it will hatch berserk";
        public const string WordKaijuDown = "THE KAIJU IS DOWN!";
        public const string WordKaijuUp = "THE KAIJU IS BACK UP";

        public static string TeamWord(TeamId t) => t == TeamId.Azure ? "AZURE" : "CRIMSON";

        /// <summary>
        /// The hatch banner, headline and reason: "THE KAIJU SIDES WITH AZURE" / "fed 64 to 41", "BERSERK" / "corruption
        /// won", "THE KAIJU SIDES WITH NOBODY" / "dead even at 50". Tallies are whole numbers, or one decimal when two
        /// different tallies would round to the same whole number.
        /// </summary>
        public static void BannerParts(KaijuState s, double azure, double crimson, out string headline, out string reason)
        {
            switch (s)
            {
                case KaijuState.Berserk:
                    headline = WordBerserk;
                    reason = WordCorruptionWon;
                    return;
                case KaijuState.Azure:
                case KaijuState.Crimson:
                {
                    bool az = s == KaijuState.Azure;
                    double win = az ? azure : crimson, lose = az ? crimson : azure;
                    headline = WordSidesWith + TeamWord(az ? TeamId.Azure : TeamId.Crimson);
                    bool fine = Math.Round(win) == Math.Round(lose) && win != lose;
                    reason = WordFed + Tally(win, fine) + WordTo + Tally(lose, fine);
                    return;
                }
                default:
                    headline = WordSidesWith + WordNobody;
                    reason = WordDeadEven + Tally(azure, false);
                    return;
            }
        }

        /// <summary>The banner as one line, "HEADLINE: reason" (the acceptance check compares this).</summary>
        public static string BannerText(KaijuState s, double azure, double crimson)
        {
            BannerParts(s, azure, crimson, out string h, out string r);
            return h + ": " + r;
        }

        public static string Tally(double v, bool oneDecimal) =>
            oneDecimal ? v.ToString("0.0", CultureInfo.InvariantCulture) : Math.Round(v).ToString("0", CultureInfo.InvariantCulture);

        /// <summary>The banner spec for a hatch outcome (icon and badge as well as colour).</summary>
        public static BannerSpec HatchBanner(KaijuState s, double azure, double crimson)
        {
            BannerParts(s, azure, crimson, out string h, out string r);
            var b = new BannerSpec { Headline = h, Reason = r, Priority = 3 };
            switch (s)
            {
                case KaijuState.Azure: b.Style = BannerStyle.Azure; b.Icon = UiIcons.Adult[1]; b.Badge = UiIcons.Crest[0]; break;
                case KaijuState.Crimson: b.Style = BannerStyle.Crimson; b.Icon = UiIcons.Adult[2]; b.Badge = UiIcons.Crest[1]; break;
                case KaijuState.Berserk: b.Style = BannerStyle.Corruption; b.Icon = UiIcons.Adult[3]; b.Badge = UiIcons.Corruption; break;
                default: b.Style = BannerStyle.Neutral; b.Icon = UiIcons.Hatchling; b.Badge = null; break;
            }
            return b;
        }

        public static BannerSpec BerserkLockBanner() => new BannerSpec
        {
            Headline = WordEggCorrupted, Reason = WordEggCorruptedReason, Style = BannerStyle.Corruption,
            Icon = UiIcons.Egg, Badge = UiIcons.Corruption, Priority = 2,
        };
    }

    /// <summary>
    /// DG-17: the Kaiju's big moments on screen (view only; reads the World, never writes it; timing from sim ticks and
    /// sim time so a capture replays it).
    /// <list type="bullet">
    /// <item>Callout pill at hatch minus 30 s ("THE EGG IS CRACKING!" with a live countdown), at a knock-out and at a
    /// recovery.</item>
    /// <item>Egg crack stages 1 to 3 at 60, 30 and 10 s, glowing seam teal, or corruption violet once the egg is locked
    /// berserk.</item>
    /// <item>Hatch spotlight: 18 ticks (0.6 s at 30 Hz) the screen dims outside a 6 u ground circle round the hatch;
    /// no shake. Berserk: a violet dim with a jagged crystal edge.</item>
    /// <item>The hatch banner (Banner) with its reason, icon and badge; a corruption banner at the berserk lock.</item>
    /// <item>A health bar over the Kaiju (damage chip, knock-out recovery, shard notches when berserk).</item>
    /// <item>The HUD's Kaiju edge pointer (GameHud "Pointer 1", distance already shown) takes the Kaiju's face and
    /// allegiance colour; berserk pulses violet.</item>
    /// </list>
    /// API for other items: <see cref="MomentRaised"/>, <see cref="Tracker"/>, <see cref="SpotlightOn"/>.
    /// </summary>
    [Flow.GameOverlay(Order = 51, Scope = Flow.OverlayScope.MatchOnly)]   // installed by GameBootstrap through the DG-01 overlay registry
    // Physics audit M3: runs after CinemachineBrain (LateUpdate order 100), so world-anchored marks use this frame's camera.
    [DefaultExecutionOrder(200)]
    public sealed class KaijuMoments : MonoBehaviour
    {
        public const double SpotlightRadius = 6;
        public const int SpotlightSegments = 48;
        public const double CalloutSeconds = 6;
        public const int WorldSortingOrder = 9, CalloutSortingOrder = 11;
        public const float BarWidth = 210f, BarHeight = 18f;
        /// <summary>Egg art height (ArtMapping Kaiju_Egg) and the raised shrine block it rests on.</summary>
        public const double EggHeight = 1.7, ShrineBlock = 1.5;

        /// <summary>Raised once per moment as the tracker sees it (DG-18 announcer, DG-38 achievements).</summary>
        public static event Action<KaijuMomentKind> MomentRaised;
        public static KaijuMoments Instance { get; private set; }

        public SimDriverBehaviour Driver;
        public readonly KaijuMomentTracker Tracker = new KaijuMomentTracker();
        /// <summary>True on the frames the hatch spotlight dims the screen.</summary>
        public bool SpotlightOn { get; private set; }
        public int CrackStageShown { get; private set; }

        private Canvas _worldCanvas, _calloutCanvas;
        private RectTransform _worldRoot;
        private SpotlightGraphic _spot;
        private EggCrackGraphic _cracks;
        private RectTransform _bar;
        private VectorShape _barChip, _barFill, _barDisc;
        private HudIcon _barIcon;
        private Text _barWord;
        private readonly VectorShape[] _notches = new VectorShape[5];
        private RectTransform _callout;
        private VectorShape _calloutPill;
        private HudIcon _calloutIcon;
        private Text _calloutText, _calloutClock;
        private KaijuMomentKind _calloutKind;
        private double _calloutAt = double.NegativeInfinity;
        private float _chip = 1f;
        private double _chipHoldUntil, _chipLast;
        private int _tier = 2;

        // GameHud's Kaiju edge pointer (found by name, the HudPrompts pattern; nothing in GameHud changes).
        private bool _pointerLooked;
        private RectTransform _pointer;
        private VectorShape _pointerDisc;
        private RawImage _pointerIcon;
        private Text _pointerDist;

        private void Awake()
        {
            Instance = this;
            if (Driver == null) Driver = GetComponent<SimDriverBehaviour>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---------------------------------------------------------------- build

        private void Build()
        {
            _worldCanvas = MakeCanvas("Kaiju Moments World", WorldSortingOrder);
            _worldRoot = (RectTransform)_worldCanvas.transform;

            var spotGo = new GameObject("Hatch Spotlight", typeof(RectTransform));
            spotGo.layer = 5;
            spotGo.transform.SetParent(_worldRoot, false);
            _spot = spotGo.AddComponent<SpotlightGraphic>();
            RectTransform srt = _spot.rectTransform;
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = Vector2.one;
            srt.pivot = Vector2.zero;
            srt.sizeDelta = Vector2.zero;
            srt.anchoredPosition = Vector2.zero;
            _spot.raycastTarget = false;
            spotGo.SetActive(false);

            var crackGo = new GameObject("Egg Cracks", typeof(RectTransform));
            crackGo.layer = 5;
            crackGo.transform.SetParent(_worldRoot, false);
            _cracks = crackGo.AddComponent<EggCrackGraphic>();
            _cracks.raycastTarget = false;
            RectTransform crt = _cracks.rectTransform;
            crt.anchorMin = crt.anchorMax = Vector2.zero;
            crt.pivot = new Vector2(0.5f, 0.5f);
            crackGo.SetActive(false);

            _bar = Rect(_worldRoot, "Kaiju Health", Vector2.zero, Vector2.zero, new Vector2(BarWidth + 70f, 56f));
            VectorShape plate = Shape(_bar, "Plate", ShapeKind.RoundRect, new Vector2(18f, 0f), new Vector2(BarWidth + 26f, 34f), CMTokens.PanelNavy.Color);
            plate.Radius = -1f;
            _barChip = Shape(_bar, "Chip", ShapeKind.RoundRect, new Vector2(22f, 0f), new Vector2(BarWidth, BarHeight), CMTokens.WarmCream.Color);
            _barChip.Radius = -1f;
            _barChip.RimPx = 0f;
            _barChip.OutlinePx = 2f;
            _barChip.EmptyColor = CMTokens.MeterEmpty.Color;
            _barFill = Shape(_bar, "Fill", ShapeKind.RoundRect, new Vector2(22f, 0f), new Vector2(BarWidth, BarHeight), CMTokens.Stone.Color);
            _barFill.Radius = -1f;
            _barFill.RimPx = 0f;
            _barFill.OutlinePx = 0f;
            for (int i = 0; i < _notches.Length; i++)
            {
                _notches[i] = Shape(_bar, "Shard " + i, ShapeKind.Diamond, new Vector2(22f - BarWidth * 0.5f + BarWidth * (i + 0.5f) / _notches.Length, 19f),
                    new Vector2(12f, 18f), CMTokens.Corruption.Color);
                _notches[i].RimPx = 0f;
                _notches[i].OutlinePx = 2f;
            }
            _barDisc = Shape(_bar, "Disc", ShapeKind.Circle, new Vector2(-BarWidth * 0.5f - 18f, 0f), new Vector2(50f, 50f), CMTokens.VoidNavy.Color);
            _barIcon = HudIcon.Make(_barDisc.transform, "Icon", 5f, 5f, 40f, 40f);
            _barWord = HudText.Make(_bar, "Word", CMTokens.Ui, 15, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter, 2f);
            Centre(_barWord.rectTransform, new Vector2(22f, -26f), new Vector2(BarWidth, 20f));
            _bar.gameObject.SetActive(false);

            _calloutCanvas = MakeCanvas("Kaiju Callout", CalloutSortingOrder);
            _callout = Rect(_calloutCanvas.transform, "Callout", new Vector2(0.5f, 1f), new Vector2(0f, -168f), new Vector2(520f, 54f));
            _calloutPill = Shape(_callout, "Pill", ShapeKind.RoundRect, Vector2.zero, new Vector2(520f, 54f), CMTokens.PanelNavy.Color);
            _calloutPill.Radius = -1f;
            VectorShape disc = Shape(_callout, "Disc", ShapeKind.Circle, new Vector2(-236f, 0f), new Vector2(64f, 64f), CMTokens.VoidNavy.Color);
            _calloutIcon = HudIcon.Make(disc.transform, "Icon", 8f, 8f, 48f, 48f);
            _calloutText = HudText.Make(_callout, "Words", CMTokens.Ui, 24, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft, 2.5f);
            Centre(_calloutText.rectTransform, new Vector2(-14f, 0f), new Vector2(360f, 40f));
            _calloutClock = HudText.Make(_callout, "Clock", CMTokens.Mono, 26, CMTokens.WarmCream.Color, TextAnchor.MiddleRight, 2.5f);
            Centre(_calloutClock.rectTransform, new Vector2(200f, 0f), new Vector2(90f, 40f));
            _callout.gameObject.SetActive(false);
        }

        private Canvas MakeCanvas(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            Canvas c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.sortingOrder = order;
            c.planeDistance = 0.5f;
            c.pixelPerfect = false;
            return c;
        }

        // ---------------------------------------------------------------- frame

        private void LateUpdate()
        {
            World w = Driver != null ? Driver.World : null;
            if (w == null) return;
            if (_worldCanvas == null) Build();
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) return;
            float scale = FitCanvas(_worldCanvas, cam);
            FitCanvas(_calloutCanvas, cam);
            int tier = UiIcons.TierFor(scale);
            if (tier != _tier)
            {
                _tier = tier;
                _barIcon.SetTier(tier);
                _calloutIcon.SetTier(tier);
            }

            Tracker.Observe(w);
            double simNow = w.Match.Time + Driver.Alpha / w.Config.SimHz;
            for (int i = 0; i < Tracker.PendingCount; i++) OnMoment(Tracker.Pending(i), simNow);

            ApplySpotlight(w, cam, scale);
            ApplyCracks(w, cam, scale, simNow);
            ApplyHealthBar(w, cam, scale, simNow);
            ApplyCallout(w, simNow);
            ApplyPointer(w, simNow);
        }

        private void OnMoment(KaijuMomentKind k, double simNow)
        {
            switch (k)
            {
                case KaijuMomentKind.HatchSoon:
                case KaijuMomentKind.KnockedOut:
                case KaijuMomentKind.Recovered:
                    _calloutKind = k;
                    _calloutAt = simNow;
                    break;
                case KaijuMomentKind.BerserkLocked:
                    Banner.Show(KaijuMomentTracker.BerserkLockBanner());
                    break;
                case KaijuMomentKind.Hatch:
                    Banner.Show(KaijuMomentTracker.HatchBanner(Tracker.HatchState, Tracker.HatchAzure, Tracker.HatchCrimson));
                    if (_calloutKind == KaijuMomentKind.HatchSoon) _calloutAt = double.NegativeInfinity;
                    break;
            }
            MomentRaised?.Invoke(k);
        }

        private static float FitCanvas(Canvas c, UnityEngine.Camera cam)
        {
            if (c.worldCamera != cam) c.worldCamera = cam;
            float pw = cam.pixelWidth, ph = cam.pixelHeight;
            if (pw < 1 || ph < 1) return c.scaleFactor;
            float s = HudLayout.Scale(pw, ph);
            if (Mathf.Abs(c.scaleFactor - s) > 1e-4f) c.scaleFactor = s;
            return s;
        }

        private static bool Project(UnityEngine.Camera cam, Vector3 world, out Vector2 px)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            px = new Vector2(sp.x, sp.y);
            return sp.z > 0 && sp.x >= 0 && sp.y >= 0 && sp.x <= cam.pixelWidth && sp.y <= cam.pixelHeight;
        }

        private static Color KaijuColour(KaijuState k) =>
            k == KaijuState.Azure ? CMTokens.Azure.Color : k == KaijuState.Crimson ? CMTokens.Crimson.Color :
            k == KaijuState.Berserk ? CMTokens.Corruption.Color : k == KaijuState.Egg ? CMTokens.EggSeam.Color : CMTokens.Stone.Color;

        private static string KaijuIcon(KaijuState k) =>
            k == KaijuState.Azure ? UiIcons.Adult[1] : k == KaijuState.Crimson ? UiIcons.Adult[2] :
            k == KaijuState.Berserk ? UiIcons.Adult[3] : k == KaijuState.Egg ? UiIcons.Egg : UiIcons.Hatchling;

        // ---------------------------------------------------------------- spotlight

        private void ApplySpotlight(World w, UnityEngine.Camera cam, float scale)
        {
            // M11: reduce motion turns the full-screen spotlight dim off (FeelGate table: Motion | Flashing).
            bool on = Tracker.SpotlightActive(w.Tick) && Effects.FeelGate.Allowed(Effects.FeelEffect.KaijuSpotlight);
            SpotlightOn = on;
            if (_spot.gameObject.activeSelf != on) _spot.gameObject.SetActive(on);
            if (!on) return;
            MapDef map = w.Map;
            double hx = Tracker.HatchX, hz = Tracker.HatchZ;
            double h = GroundHeight.At(map, hx, hz);
            Vector3 c = cam.WorldToScreenPoint(SimSpace.ToWorld(hx, h, hz));
            bool berserk = Tracker.HatchState == KaijuState.Berserk;
            Vector2[] ring = _spot.Ring;
            for (int i = 0; i < SpotlightSegments; i++)
            {
                double a = i * 2 * Math.PI / SpotlightSegments;
                // corruption: a jagged crystal edge (every other vertex pushed out)
                double r = SpotlightRadius * (berserk ? (i % 2 == 0 ? 1.14 : 0.9) : 1.0);
                Vector3 p = cam.WorldToScreenPoint(SimSpace.ToWorld(hx + Math.Cos(a) * r, h, hz + Math.Sin(a) * r));
                ring[i] = new Vector2(p.x, p.y) / scale;
            }
            _spot.Centre = new Vector2(c.x, c.y) / scale;
            _spot.Feather = berserk ? 0.04f : 0.14f;
            _spot.Dim = berserk ? CMTokens.WithAlpha(Color.Lerp(CMTokens.VoidNavy.Color, CMTokens.Corruption.Color, 0.32f), 0.64f)
                                : CMTokens.WithAlpha(CMTokens.VoidNavy.Color, 0.58f);
            _spot.Count = SpotlightSegments;
            _spot.SetVerticesDirty();
        }

        // ---------------------------------------------------------------- egg cracks

        private void ApplyCracks(World w, UnityEngine.Camera cam, float scale, double simNow)
        {
            MatchState m = w.Match;
            int stage = KaijuMomentTracker.CrackStage(w.Config.HatchSeconds - m.Time, m.Hatched || w.Kaiju.State != KaijuState.Egg);
            CrackStageShown = stage;
            bool on = stage > 0;
            Vector2 bottom = Vector2.zero, top = Vector2.zero;
            if (on)
            {
                Kaiju k = w.Kaiju;
                MapDef map = w.Map;
                double baseY = GroundHeight.At(map, k.X, k.Z) + RaisedTop(map, k.X, k.Z);
                on = Project(cam, SimSpace.ToWorld(k.X, baseY, k.Z), out bottom) & Project(cam, SimSpace.ToWorld(k.X, baseY + EggHeight, k.Z), out top);
            }
            if (_cracks.gameObject.activeSelf != on) _cracks.gameObject.SetActive(on);
            if (!on) return;
            float hPx = Vector2.Distance(top, bottom) / scale;
            RectTransform rt = _cracks.rectTransform;
            // the shell's middle 78%, so the lines stay on the shell
            rt.sizeDelta = new Vector2(hPx * 0.56f, hPx * 0.78f);
            rt.anchoredPosition = (bottom + top) * 0.5f / scale;
            Color glow = Tracker.BerserkLocked ? CMTokens.Corruption.Color : CMTokens.EggSeam.Color;
            float pulse = stage >= 3 ? 0.8f + 0.2f * Mathf.Sin((float)(simNow * 6.0)) : 1f;
            _cracks.Set(stage, CMTokens.WithAlpha(glow, pulse), Tracker.BerserkLocked);
        }

        private static double RaisedTop(MapDef map, double x, double z)
        {
            int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
            if (map.Cells == null || cx < 0 || cz < 0 || cx >= map.Width || cz >= map.Height) return 0;
            byte c = map.Cells[cz * map.Width + cx];
            return c == Cell.Wall || c == Cell.BreakableIntact ? ShrineBlock : 0;
        }

        // ---------------------------------------------------------------- health bar

        private void ApplyHealthBar(World w, UnityEngine.Camera cam, float scale, double simNow)
        {
            Kaiju k = w.Kaiju;
            bool hatched = w.Match.Hatched && k.State != KaijuState.Egg;
            bool on = false;
            Vector2 p = Vector2.zero;
            if (hatched)
            {
                double a = Driver.Alpha;
                double x = k.PrevX + (k.X - k.PrevX) * a, z = k.PrevZ + (k.Z - k.PrevZ) * a;
                on = Project(cam, SimSpace.ToWorld(x, GroundHeight.At(w.Map, x, z) + w.Config.Render.KaijuHeight + 0.9, z), out p);
            }
            if (_bar.gameObject.activeSelf != on) _bar.gameObject.SetActive(on);
            if (!hatched) { _chip = 1f; return; }

            bool ko = k.KoIn > 0;
            float hp = k.MaxHp > 0 ? Mathf.Clamp01((float)(k.Hp / k.MaxHp)) : 0f;
            float shown = ko ? Mathf.Clamp01(1f - (float)(k.KoIn / Math.Max(1e-6, w.Config.Kaiju.KoSeconds))) : hp;
            // damage chip: holds 0.4 sim s after the last full frame, then drains at 60% per sim second (sim time, so a
            // capture replays it)
            double dSim = Math.Max(0, simNow - _chipLast);
            _chipLast = simNow;
            if (ko || shown >= _chip - 1e-4f)
            {
                _chip = shown;
                _chipHoldUntil = simNow + 0.4;
            }
            else if (simNow >= _chipHoldUntil) _chip = Mathf.Max(shown, _chip - 0.6f * (float)dSim);
            if (!on) return;
            _bar.anchoredPosition = p / scale;
            Color c = ko ? CMTokens.Stone.Color : KaijuColour(k.State);
            _barFill.SetFill(c);
            _barFill.Fill01 = shown;
            _barChip.Fill01 = _chip;
            _barDisc.SetRim(c);
            _barIcon.Set(KaijuIcon(k.State), _tier);
            bool berserk = k.State == KaijuState.Berserk && !ko;
            for (int i = 0; i < _notches.Length; i++)
            {
                if (_notches[i].gameObject.activeSelf != berserk) _notches[i].gameObject.SetActive(berserk);
                if (berserk)
                {
                    float s = 1f + 0.12f * Mathf.Sin((float)(simNow * 5.0) + i * 1.3f);
                    _notches[i].rectTransform.localScale = new Vector3(s, s, 1f);
                }
            }
            HudText.Set(_barWord, ko ? WordDown : StateWord(k.State));
            _barWord.color = ko || k.State == KaijuState.Neutral ? CMTokens.WarmCream.Color : c;
        }

        public const string WordDown = "DOWN";

        private static string StateWord(KaijuState s) =>
            s == KaijuState.Azure ? "AZURE" : s == KaijuState.Crimson ? "CRIMSON" : s == KaijuState.Berserk ? "BERSERK" : "NEUTRAL";

        // ---------------------------------------------------------------- callout

        private void ApplyCallout(World w, double simNow)
        {
            double age = simNow - _calloutAt;
            bool on = age >= 0 && age < CalloutSeconds && _calloutKind != KaijuMomentKind.None;
            if (_callout.gameObject.activeSelf != on) _callout.gameObject.SetActive(on);
            if (!on) return;
            bool corrupt = Tracker.BerserkLocked || w.Kaiju.State == KaijuState.Berserk;
            Color accent = corrupt ? CMTokens.Corruption.Color : _calloutKind == KaijuMomentKind.HatchSoon ? CMTokens.EggSeam.Color : KaijuColour(w.Kaiju.State);
            _calloutPill.SetRim(accent);
            _calloutPill.SetFill(corrupt ? Color.Lerp(CMTokens.VoidNavy.Color, CMTokens.Corruption.Color, 0.38f) : CMTokens.PanelNavy.Color);
            bool soon = _calloutKind == KaijuMomentKind.HatchSoon;
            _calloutIcon.Set(soon ? UiIcons.Egg : KaijuIcon(w.Kaiju.State), _tier);
            HudText.Set(_calloutText, soon ? KaijuMomentTracker.WordEggCracking :
                _calloutKind == KaijuMomentKind.KnockedOut ? KaijuMomentTracker.WordKaijuDown : KaijuMomentTracker.WordKaijuUp);
            _calloutClock.gameObject.SetActive(soon);
            if (soon) HudText.Set(_calloutClock, NumberCache.Mmss(w.Config.HatchSeconds - w.Match.Time));
            // entrance: 0.2 sim s drop-in from 14 units above
            float t = Mathf.Clamp01((float)(age / 0.2));
            _callout.anchoredPosition = new Vector2(0f, -168f + 14f * (1f - t) * (1f - t));
        }

        // ---------------------------------------------------------------- the HUD's Kaiju pointer

        private void ApplyPointer(World w, double simNow)
        {
            if (!_pointerLooked)
            {
                _pointerLooked = true;
                GameHud hud = GetComponent<GameHud>();
                Transform p = hud != null && hud.Root != null ? hud.Root.Find("World Layer/Pointer 1") : null;
                if (p != null)
                {
                    _pointer = (RectTransform)p;
                    Transform disc = p.Find("Icon");
                    if (disc != null)
                    {
                        _pointerDisc = disc.GetComponent<VectorShape>();
                        Transform icon = disc.Find("Egg");
                        if (icon != null) _pointerIcon = icon.GetComponent<RawImage>();
                    }
                    Transform dist = p.Find("Distance");
                    if (dist != null) _pointerDist = dist.GetComponent<Text>();
                }
            }
            if (_pointer == null || !_pointer.gameObject.activeSelf) return;
            KaijuState s = w.Match.Hatched ? w.Kaiju.State : KaijuState.Egg;
            Color c = KaijuColour(s);
            if (_pointerDisc != null) _pointerDisc.SetRim(c);
            if (_pointerIcon != null)
            {
                Texture2D t = UiIcons.Get(s == KaijuState.Egg ? UiIcons.MapEgg : KaijuIcon(s), _tier);
                if (t != null && _pointerIcon.texture != t) _pointerIcon.texture = t;
            }
            if (_pointerDist != null) _pointerDist.color = s == KaijuState.Neutral || s == KaijuState.Egg ? CMTokens.WarmCream.Color : c;
            float k = s == KaijuState.Berserk ? 1f + 0.07f * Mathf.Sin((float)(simNow * 6.0)) : 1f;
            _pointer.localScale = new Vector3(k, k, 1f);
        }

        // ---------------------------------------------------------------- helpers

        private static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return rt;
        }

        private static VectorShape Shape(Transform parent, string name, ShapeKind kind, Vector2 pos, Vector2 size, Color fill)
        {
            RectTransform rt = Rect(parent, name, new Vector2(0.5f, 0.5f), pos, size);
            VectorShape s = rt.gameObject.AddComponent<VectorShape>();
            s.Kind = kind;
            s.FillColor = fill;
            s.raycastTarget = false;
            return s;
        }

        private static void Centre(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }
    }

    /// <summary>
    /// The hatch spotlight's dim: a full-screen mesh with a hole along a projected ground ring (Ring, canvas units from
    /// the bottom-left), feathered inward by Feather of the ring's size. Rebuilt only on the spotlight's 18 ticks.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SpotlightGraphic : MaskableGraphic
    {
        public readonly Vector2[] Ring = new Vector2[KaijuMoments.SpotlightSegments];
        public int Count;
        public Vector2 Centre;
        public float Feather = 0.14f;
        public Color Dim = new Color(0f, 0f, 0f, 0.58f);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            int n = Mathf.Min(Count, Ring.Length);
            if (n < 3) return;
            Rect r = rectTransform.rect;
            float far = 4f * Mathf.Max(r.width, r.height) + 1f;
            Color clear = Dim;
            clear.a = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 inner = Ring[i];
                Vector2 d = inner - Centre;
                float len = d.magnitude;
                Vector2 dir = len > 1e-4f ? d / len : Vector2.up;
                Vector2 feather = Centre + d * (1f + Feather);
                Vector2 outer = Centre + dir * Mathf.Max(far, len * 2f);
                vh.AddVert(inner, clear, Vector4.zero);
                vh.AddVert(feather, Dim, Vector4.zero);
                vh.AddVert(outer, Dim, Vector4.zero);
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int a0 = i * 3, b0 = j * 3;
                vh.AddTriangle(a0, b0, a0 + 1);
                vh.AddTriangle(b0, b0 + 1, a0 + 1);
                vh.AddTriangle(a0 + 1, b0 + 1, a0 + 2);
                vh.AddTriangle(b0 + 1, b0 + 2, a0 + 2);
            }
        }
    }

    /// <summary>
    /// The egg's crack stages over the egg: jagged lines in the egg's own frame (x, y in -0.5..0.5 of the rect, top up),
    /// a Void Navy under-stroke and a glow stroke (seam teal, or corruption violet with thicker shards once locked
    /// berserk). Stage 1: one crack from the crown; 2: three; 3: six. Rebuilt only when the stage or colour changes.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class EggCrackGraphic : MaskableGraphic
    {
        private static readonly Vector2[][] Cracks =
        {
            new[] { new Vector2(0.02f, 0.48f), new Vector2(-0.06f, 0.32f), new Vector2(0.07f, 0.18f), new Vector2(-0.03f, 0.04f), new Vector2(0.08f, -0.08f) },
            new[] { new Vector2(-0.34f, 0.16f), new Vector2(-0.20f, 0.11f), new Vector2(-0.24f, -0.01f), new Vector2(-0.09f, -0.05f), new Vector2(-0.13f, -0.18f) },
            new[] { new Vector2(0.36f, 0.08f), new Vector2(0.21f, 0.13f), new Vector2(0.25f, 0.00f), new Vector2(0.11f, -0.03f), new Vector2(0.15f, -0.16f) },
            new[] { new Vector2(-0.06f, 0.32f), new Vector2(-0.17f, 0.37f), new Vector2(-0.26f, 0.30f) },
            new[] { new Vector2(0.07f, 0.18f), new Vector2(0.19f, 0.25f), new Vector2(0.25f, 0.35f) },
            new[] { new Vector2(0.01f, -0.40f), new Vector2(-0.09f, -0.28f), new Vector2(0.03f, -0.18f), new Vector2(-0.04f, -0.06f) },
        };
        private static readonly int[] CracksForStage = { 0, 1, 3, 6 };

        private int _stage;
        private Color _glow;
        private bool _corrupt;

        public void Set(int stage, Color glow, bool corrupt)
        {
            if (stage == _stage && glow == _glow && corrupt == _corrupt) return;
            _stage = stage;
            _glow = glow;
            _corrupt = corrupt;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            int n = CracksForStage[Mathf.Clamp(_stage, 0, 3)];
            Rect r = rectTransform.rect;
            if (n == 0 || r.height < 1f) return;
            float glowW = r.height * (_corrupt ? 0.05f : 0.035f);
            float navyW = glowW + 2f * CMTokens.OutlinePx * 0.5f;
            Color navy = CMTokens.VoidNavy.Color;
            for (int pass = 0; pass < 2; pass++)
                for (int c = 0; c < n; c++)
                {
                    Vector2[] pts = Cracks[c];
                    for (int i = 0; i + 1 < pts.Length; i++)
                    {
                        Vector2 a = new Vector2(r.center.x + pts[i].x * r.width, r.center.y + pts[i].y * r.height);
                        Vector2 b = new Vector2(r.center.x + pts[i + 1].x * r.width, r.center.y + pts[i + 1].y * r.height);
                        // lines taper toward their tips
                        float taper = 1f - 0.5f * (i + 1f) / pts.Length;
                        Segment(vh, a, b, (pass == 0 ? navyW : glowW) * taper, pass == 0 ? navy : _glow);
                    }
                }
        }

        private static void Segment(VertexHelper vh, Vector2 a, Vector2 b, float width, Color c)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            Vector2 nrm = new Vector2(-d.y, d.x) / len * (width * 0.5f);
            Vector2 ext = d / len * (width * 0.5f);
            int i0 = vh.currentVertCount;
            vh.AddVert(a - ext - nrm, c, Vector4.zero);
            vh.AddVert(a - ext + nrm, c, Vector4.zero);
            vh.AddVert(b + ext + nrm, c, Vector4.zero);
            vh.AddVert(b + ext - nrm, c, Vector4.zero);
            vh.AddTriangle(i0, i0 + 1, i0 + 2);
            vh.AddTriangle(i0, i0 + 2, i0 + 3);
        }
    }

    /// <summary>Adds KaijuMoments to the game's object after the scene loads (the pause menu's MenuInstaller pattern).</summary>
    public static class KaijuMomentsInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            // Installed by the DG-01 overlay registry ([GameOverlay], match only, never behind the menus).
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach(scene);

        /// <summary>Adds KaijuMoments (and the Banner it shows) to the game root; returns it (null: no game).</summary>
        public static KaijuMoments Attach(Scene scene)
        {
            GameBootstrap.Build(scene);
            HumanInputSource input = UnityEngine.Object.FindAnyObjectByType<HumanInputSource>();
            if (input == null) return null;
            GameObject go = input.gameObject;
            if (go.GetComponent<Banner>() == null) go.AddComponent<Banner>();
            KaijuMoments km = go.GetComponent<KaijuMoments>();
            if (km == null)
            {
                km = go.AddComponent<KaijuMoments>();
                km.Driver = go.GetComponent<SimDriverBehaviour>();
            }
            return km;
        }
    }
}
