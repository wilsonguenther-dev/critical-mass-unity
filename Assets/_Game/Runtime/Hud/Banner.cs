using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>The banner's look. Corruption has its own (violet plate, shards, cream type with a violet edge).</summary>
    public enum BannerStyle { Neutral, Azure, Crimson, Corruption, Warning }

    /// <summary>One banner: a headline, the reason under it, an icon (left) and an optional badge (right).</summary>
    public struct BannerSpec
    {
        public string Headline;
        public string Reason;
        /// <summary>UiIcons name shown in the left disc (null = none).</summary>
        public string Icon;
        /// <summary>UiIcons name shown in the right disc (null = none).</summary>
        public string Badge;
        public BannerStyle Style;
        /// <summary>Seconds on the banner clock between the end of the entrance and the start of the exit (0 = default).</summary>
        public double HoldSeconds;
        /// <summary>A higher priority replaces the banner on screen; equal or lower waits its turn.</summary>
        public int Priority;
    }

    /// <summary>
    /// The banner's timeline and queue (pure, testable). Entrance 0.18 s, hold, exit 0.2 s on the banner clock; at most
    /// Capacity banners wait (the oldest of the lowest priority is dropped when full). A clock that runs backwards (a
    /// new match restarts sim time) clears everything.
    /// </summary>
    public sealed class BannerQueue
    {
        public const int Capacity = 4;
        public const double InSeconds = 0.18, OutSeconds = 0.2;
        /// <summary>Default hold in banner-clock seconds: sim seconds in a match, 3.5 s of wall time at the default speed 2.</summary>
        public const double DefaultHold = 7.0;

        private readonly BannerSpec[] _waiting = new BannerSpec[Capacity];
        private int _count;
        private double _shownAt, _hideAt = double.PositiveInfinity, _last = double.NegativeInfinity;

        public bool HasCurrent { get; private set; }
        public BannerSpec Current { get; private set; }
        /// <summary>Bumped each time Current changes or clears (the view rebuilds on it).</summary>
        public int Version { get; private set; }
        public int WaitingCount => _count;

        public double Hold(in BannerSpec s) => s.HoldSeconds > 0 ? s.HoldSeconds : DefaultHold;

        public void Clear()
        {
            _count = 0;
            if (HasCurrent) Version++;
            HasCurrent = false;
            _hideAt = double.PositiveInfinity;
        }

        public void Show(in BannerSpec s, double now)
        {
            Rewind(now);
            if (!HasCurrent) { Start(s, now); return; }
            if (s.Priority > Current.Priority)
            {
                Start(s, now);
                return;
            }
            if (_count == Capacity)
            {
                int drop = 0;
                for (int i = 1; i < _count; i++) if (_waiting[i].Priority < _waiting[drop].Priority) drop = i;
                if (_waiting[drop].Priority > s.Priority) return;
                for (int i = drop; i < _count - 1; i++) _waiting[i] = _waiting[i + 1];
                _count--;
            }
            _waiting[_count++] = s;
        }

        /// <summary>
        /// Holds the banner on screen back while a full-screen takeover plays (Banner.Hold): its entrance restarts at now, so
        /// it enters fresh, with its full hold, once the takeover is over. Waiting banners stay queued.
        /// </summary>
        public void Postpone(double now)
        {
            Rewind(now);
            if (!HasCurrent) return;
            _shownAt = now;
            if (!double.IsPositiveInfinity(_hideAt)) _hideAt = now;
        }

        /// <summary>Starts the exit of the banner on screen now (waiting banners follow).</summary>
        public void Hide(double now)
        {
            if (!HasCurrent) return;
            double outAt = now;
            double end = _shownAt + InSeconds + Hold(Current);
            if (outAt < end) _hideAt = outAt;
        }

        /// <summary>Advance: when the banner on screen has finished, the next waiting one (highest priority first) starts.</summary>
        public void Update(double now)
        {
            Rewind(now);
            if (!HasCurrent) return;
            if (now < ExitStart() + OutSeconds) return;
            if (_count == 0)
            {
                HasCurrent = false;
                _hideAt = double.PositiveInfinity;
                Version++;
                return;
            }
            int best = 0;
            for (int i = 1; i < _count; i++) if (_waiting[i].Priority > _waiting[best].Priority) best = i;
            BannerSpec next = _waiting[best];
            for (int i = best; i < _count - 1; i++) _waiting[i] = _waiting[i + 1];
            _count--;
            Start(next, now);
        }

        /// <summary>0..1 opacity at now.</summary>
        public float Alpha(double now)
        {
            if (!HasCurrent) return 0f;
            double t = now - _shownAt;
            if (t < InSeconds) return (float)EaseOut(t / InSeconds);
            double e = now - ExitStart();
            if (e > 0) return (float)Math.Max(0, 1 - e / OutSeconds);
            return 1f;
        }

        /// <summary>Scale (1.12 down to 1 on the entrance) and lift (0 up to 1 over the exit) at now.</summary>
        public void Motion(double now, out float scale, out float lift)
        {
            scale = 1f;
            lift = 0f;
            if (!HasCurrent) return;
            double t = now - _shownAt;
            if (t < InSeconds) scale = (float)(1.12 - 0.12 * EaseOut(t / InSeconds));
            double e = now - ExitStart();
            if (e > 0) lift = (float)Math.Min(1, e / OutSeconds);
        }

        private double ExitStart() => Math.Min(_shownAt + InSeconds + Hold(Current), _hideAt);

        private void Start(in BannerSpec s, double now)
        {
            Current = s;
            HasCurrent = true;
            _shownAt = now;
            _hideAt = double.PositiveInfinity;
            Version++;
        }

        private void Rewind(double now)
        {
            if (now < _last - 1e-9) Clear();
            _last = now;
        }

        private static double EaseOut(double t)
        {
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            double u = 1 - t;
            return 1 - u * u * u;
        }
    }

    /// <summary>
    /// The big centre banner (DG-17 hatch and berserk moments; DG-37 reuses it for the Kaiju goal). A chunky navy plate
    /// with team-colour bands, the Kaiju icon left, a crest or corruption badge right, the headline in the display face
    /// and the reason under it, so the outcome reads by icon and words as well as by colour. View only.
    /// <para>API: <see cref="Show"/>, <see cref="Hide"/>, <see cref="IsOpen"/>, <see cref="Current"/>, the
    /// <see cref="Shown"/> and <see cref="Hidden"/> events. The banner clock is the match's interpolated sim time when a
    /// SimDriverBehaviour is on the same object (paused with the game, identical in captures), else unscaled real time;
    /// <see cref="Clock"/> overrides both. Self-installs on the game object like the pause menu (no overlay registry on
    /// origin yet: DG-01 can call Show / Hide directly).</para>
    /// </summary>
    [Flow.GameOverlay(Order = 50)]   // installed by GameBootstrap through the DG-01 overlay registry
    public sealed class Banner : MonoBehaviour
    {
        /// <summary>Reference layout (1920 x 1080 units): centre of the banner below the match cluster.</summary>
        public const float Width = 1080f, Height = 168f, CentreFromTop = 284f;
        public const int SortingOrder = 15;

        private static readonly BannerQueue Queue = new BannerQueue();

        /// <summary>Overrides the banner clock (seconds) when set.</summary>
        public static Func<double> Clock;
        /// <summary>
        /// Big moments (feat/big-moments): while this returns true the banner is hidden and postponed, so a full-screen
        /// takeover and a banner never stack (one queue for full-screen moments). Null = never held.
        /// </summary>
        public static Func<bool> Hold;
        public static event Action<BannerSpec> Shown;
        public static event Action Hidden;

        public static bool IsOpen => Queue.HasCurrent;
        public static BannerSpec Current => Queue.Current;
        public static Banner Instance { get; private set; }

        public static void Show(BannerSpec spec) => Queue.Show(spec, Now());
        public static void Hide() => Queue.Hide(Now());
        /// <summary>Drops the banner on screen and every waiting one at once.</summary>
        public static void ClearAll() => Queue.Clear();

        private static double Now()
        {
            if (Clock != null) return Clock();
            if (Instance != null && Instance._driver != null && Instance._driver.World != null)
            {
                SimDriverBehaviour d = Instance._driver;
                return d.World.Match.Time + d.Alpha / d.Config.SimHz;
            }
            return Time.unscaledTimeAsDouble;
        }

        // ---------------------------------------------------------------- view

        private SimDriverBehaviour _driver;
        private Canvas _canvas;
        private CanvasGroup _group;
        private RectTransform _root;
        private VectorShape _plate, _bandTop, _bandBottom, _iconDisc, _badgeDisc;
        private HudIcon _icon, _badge;
        private Text _headline, _reason;
        private TextOutline _headlineOutline;
        private readonly VectorShape[] _shards = new VectorShape[8];
        private int _seenVersion = -1;
        private bool _wasOpen;
        private int _tier = 2;

        private static readonly Vector2[] ShardAt =
        {
            new Vector2(-520f, -70f), new Vector2(-300f, -86f), new Vector2(90f, -84f), new Vector2(430f, -74f),
            new Vector2(-460f, 78f), new Vector2(-120f, 88f), new Vector2(260f, 86f), new Vector2(530f, 64f),
        };

        private void Awake()
        {
            Instance = this;
            _driver = GetComponent<SimDriverBehaviour>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Build()
        {
            var go = new GameObject("Banner Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.sortingOrder = SortingOrder;
            _canvas.planeDistance = 0.5f;
            _group = go.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;

            _root = Rect(go.transform, "Banner", new Vector2(0.5f, 1f), new Vector2(0f, -CentreFromTop), new Vector2(Width, Height));
            for (int i = 0; i < _shards.Length; i++)
            {
                _shards[i] = Shape(_root, "Shard " + i, ShapeKind.Diamond, ShardAt[i], new Vector2(i % 2 == 0 ? 34f : 24f, i % 2 == 0 ? 52f : 38f), CMTokens.Corruption.Color);
                _shards[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, (i * 37) % 70 - 35);
            }
            _plate = Shape(_root, "Plate", ShapeKind.Chamfer, Vector2.zero, new Vector2(Width, Height), CMTokens.PanelNavy.Color);
            _plate.Radius = 26f;
            _bandTop = Shape(_root, "Band Top", ShapeKind.RoundRect, new Vector2(0f, -Height * 0.5f + 16f), new Vector2(Width - 120f, 10f), CMTokens.Azure.Color);
            _bandBottom = Shape(_root, "Band Bottom", ShapeKind.RoundRect, new Vector2(0f, Height * 0.5f - 16f), new Vector2(Width - 120f, 10f), CMTokens.Azure.Color);
            _bandTop.Radius = _bandBottom.Radius = -1f;
            _bandTop.RimPx = _bandBottom.RimPx = 0f;
            _bandTop.OutlinePx = _bandBottom.OutlinePx = 2f;

            _iconDisc = Shape(_root, "Icon Disc", ShapeKind.Circle, new Vector2(-Width * 0.5f + 40f, 0f), new Vector2(150f, 150f), CMTokens.VoidNavy.Color);
            _iconDisc.RimPx = 4f;
            _icon = HudIcon.Make(_iconDisc.transform, "Icon", 15f, 15f, 120f, 120f);
            _badgeDisc = Shape(_root, "Badge Disc", ShapeKind.Circle, new Vector2(Width * 0.5f - 34f, 0f), new Vector2(104f, 104f), CMTokens.VoidNavy.Color);
            _badgeDisc.RimPx = 3f;
            _badge = HudIcon.Make(_badgeDisc.transform, "Badge", 12f, 12f, 80f, 80f);

            _headline = HudText.Make(_root, "Headline", CMTokens.Display, 50, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter, 4f);
            Centre(_headline.rectTransform, new Vector2(20f, 22f), new Vector2(Width - 300f, 64f));
            _headlineOutline = _headline.GetComponent<TextOutline>();
            _reason = HudText.Make(_root, "Reason", CMTokens.Ui, 28, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter, 2.5f);
            Centre(_reason.rectTransform, new Vector2(20f, -36f), new Vector2(Width - 300f, 36f));
            go.SetActive(false);
        }

        private void LateUpdate()
        {
            double now = Now();
            if (Hold != null && Hold())
            {
                Queue.Postpone(now);
                if (_canvas != null && _canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(false);
                return;
            }
            Queue.Update(now);
            bool open = Queue.HasCurrent;
            if (open && _canvas == null) Build();
            if (_canvas == null) return;
            if (open != _canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(open);
            if (_seenVersion != Queue.Version)
            {
                _seenVersion = Queue.Version;
                if (open)
                {
                    Apply(Queue.Current);
                    Shown?.Invoke(Queue.Current);
                }
            }
            if (_wasOpen && !open) Hidden?.Invoke();
            _wasOpen = open;
            if (!open) return;

            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null && _canvas.worldCamera != cam) _canvas.worldCamera = cam;
            float pw = cam != null ? cam.pixelWidth : Screen.width, ph = cam != null ? cam.pixelHeight : Screen.height;
            if (pw >= 1 && ph >= 1)
            {
                float s = HudLayout.Scale(pw, ph);
                if (Mathf.Abs(_canvas.scaleFactor - s) > 1e-4f) _canvas.scaleFactor = s;
                int tier = UiIcons.TierFor(s);
                if (tier != _tier)
                {
                    _tier = tier;
                    _icon.SetTier(tier);
                    _badge.SetTier(tier);
                }
            }
            _group.alpha = Queue.Alpha(now);
            Queue.Motion(now, out float scale, out float lift);
            _root.localScale = new Vector3(scale, scale, 1f);
            _root.anchoredPosition = new Vector2(0f, -CentreFromTop + 18f * lift);
            if (Queue.Current.Style == BannerStyle.Corruption)
            {
                // the shards breathe (view only, on the banner clock so a capture replays it)
                for (int i = 0; i < _shards.Length; i++)
                {
                    float k = 1f + 0.08f * Mathf.Sin((float)(now * 5.0) + i * 1.7f);
                    _shards[i].rectTransform.localScale = new Vector3(k, k, 1f);
                }
            }
        }

        private void Apply(in BannerSpec s)
        {
            Color accent = Accent(s.Style);
            bool corrupt = s.Style == BannerStyle.Corruption;
            Color navy = CMTokens.VoidNavy.Color;
            _plate.SetFill(corrupt ? Color.Lerp(navy, CMTokens.Corruption.Color, 0.38f) : CMTokens.PanelNavy.Color);
            _plate.Radius = corrupt ? 44f : 26f;
            _plate.SetRim(corrupt ? CMTokens.Corruption.Color : CMTokens.WarmCream.Color);
            _bandTop.SetFill(accent);
            _bandBottom.SetFill(accent);
            _iconDisc.SetRim(accent);
            _badgeDisc.SetRim(accent);
            for (int i = 0; i < _shards.Length; i++) _shards[i].gameObject.SetActive(corrupt);
            _icon.Set(s.Icon, _tier);
            _iconDisc.gameObject.SetActive(!string.IsNullOrEmpty(s.Icon));
            _badge.Set(s.Badge, _tier);
            _badgeDisc.gameObject.SetActive(!string.IsNullOrEmpty(s.Badge));
            // Team colours are bright against the navy plate; corruption type stays cream with a violet edge.
            bool teamType = s.Style == BannerStyle.Azure || s.Style == BannerStyle.Crimson || s.Style == BannerStyle.Warning;
            _headline.color = teamType ? accent : CMTokens.WarmCream.Color;
            if (_headlineOutline != null)
            {
                _headlineOutline.OutlineColor = corrupt ? CMTokens.Corruption.Color : navy;
                _headline.SetVerticesDirty();
            }
            HudText.Set(_headline, s.Headline ?? string.Empty);
            HudText.Set(_reason, s.Reason ?? string.Empty);
            _reason.gameObject.SetActive(!string.IsNullOrEmpty(s.Reason));
        }

        public static Color Accent(BannerStyle s)
        {
            switch (s)
            {
                case BannerStyle.Azure: return CMTokens.Azure.Color;
                case BannerStyle.Crimson: return CMTokens.Crimson.Color;
                case BannerStyle.Corruption: return CMTokens.Corruption.Color;
                case BannerStyle.Warning: return CMTokens.MeterOrange.Color;
                default: return CMTokens.Stone.Color;
            }
        }

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

        /// <summary>A shape centred at pos (y up) from the centre of its parent.</summary>
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

    /// <summary>Adds the Banner to the game's object after the scene loads (the pause menu's MenuInstaller pattern).</summary>
    public static class BannerInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            // Domain reload is off on entering Play Mode: statics survive, so start clean and resubscribe once.
            Banner.ClearAll();
            // Installed by the DG-01 overlay registry ([GameOverlay] on Banner); this only resets statics.
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach(scene);

        /// <summary>Adds a Banner to the HumanInputSource's object (the game root); returns it (null: no game).</summary>
        public static Banner Attach(Scene scene)
        {
            GameBootstrap.Build(scene);
            HumanInputSource input = UnityEngine.Object.FindAnyObjectByType<HumanInputSource>();
            if (input == null) return null;
            Banner b = input.GetComponent<Banner>();
            return b != null ? b : input.gameObject.AddComponent<Banner>();
        }
    }
}
