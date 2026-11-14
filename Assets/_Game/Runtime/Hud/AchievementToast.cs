using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>
    /// The achievements list for a results screen: all ten, earned-this-match on Tool Brass, unlocked earlier in cream,
    /// locked dimmed, with "n of 10" in the header. Built in code into any parent (the game-flow results screen calls
    /// Build; until it exists AchievementToast shows one itself at match end). Refresh is allocation-free.
    /// </summary>
    public sealed class AchievementList
    {
        public const float W = 760f, RowH = 46f, Pitch = 52f, HeadH = 92f;
        public static float Height => HeadH + Achievements.All.Length * Pitch + 16f;

        public RectTransform Rt { get; private set; }
        readonly VectorShape[] _plates = new VectorShape[Achievements.All.Length];
        readonly Text[] _titles = new Text[Achievements.All.Length];
        readonly Text[] _lines = new Text[Achievements.All.Length];
        readonly Text[] _states = new Text[Achievements.All.Length];
        Text _count;

        /// <summary>Builds the list with its top-left at (x, y) reference px in parent (MenuUi convention).</summary>
        public static AchievementList Build(Transform parent, float x, float y)
        {
            var l = new AchievementList();
            l.Rt = MenuUi.Rt(parent, "Achievements");
            MenuUi.Place(l.Rt, x, y, W, Height);
            MenuUi.Panel(l.Rt, "Panel", 0f, 0f, W, Height);
            MenuUi.Label(l.Rt, "Title", CMTokens.Display, UiScale.TypeLead, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft, 28f, 20f, 460f, 52f, "ACHIEVEMENTS", 3f);
            l._count = MenuUi.Label(l.Rt, "Count", CMTokens.Ui, UiScale.TypeBody, CMTokens.ToolBrass.Color, TextAnchor.MiddleRight, 420f, 20f, 312f, 52f, "", 2f);
            for (int i = 0; i < Achievements.All.Length; i++)
            {
                float ry = HeadH + i * Pitch;
                l._plates[i] = MenuUi.Shape(l.Rt, "Row " + i, ShapeKind.RoundRect, 20f, ry, W - 40f, RowH, CMTokens.VoidNavy.Color, 12f);
                l._plates[i].RimPx = 0f;
                l._titles[i] = MenuUi.Label(l.Rt, "Name " + i, CMTokens.Ui, UiScale.TypeBody, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft, 40f, ry, 220f, RowH, Achievements.All[i].Title.ToUpperInvariant(), 2f);
                l._lines[i] = MenuUi.Label(l.Rt, "Rule " + i, CMTokens.Ui, UiScale.TypeMin, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft, 262f, ry, 360f, RowH, Achievements.All[i].Description, 2f);
                l._lines[i].horizontalOverflow = HorizontalWrapMode.Wrap;
                l._states[i] = MenuUi.Label(l.Rt, "State " + i, CMTokens.Ui, UiScale.TypeMin, CMTokens.WarmCream.Color, TextAnchor.MiddleRight, 610f, ry, 120f, RowH, "", 2f);
            }
            return l;
        }

        static string[] _counts;
        const string Earned = "EARNED", Unlocked = "UNLOCKED", Locked = "LOCKED";

        public void Refresh(Achievements a)
        {
            if (a == null) return;
            int n = Achievements.All.Length;
            if (_counts == null)
            {
                _counts = new string[n + 1];
                for (int i = 0; i <= n; i++) _counts[i] = i + " OF " + n + " UNLOCKED";
            }
            HudText.Set(_count, _counts[Mathf.Clamp(a.UnlockedCount, 0, n)]);
            for (int i = 0; i < n; i++)
            {
                bool now = a.EarnedThisMatch(i), ever = a.IsUnlocked(i);
                _plates[i].SetFill(now ? CMTokens.ToolBrass.Color : CMTokens.WithAlpha(CMTokens.VoidNavy.Color, ever ? 0.7f : 0.35f));
                Color text = now ? CMTokens.VoidNavy.Color : CMTokens.WithAlpha(CMTokens.WarmCream.Color, ever ? 1f : 0.45f);
                if (_titles[i].color != text) { _titles[i].color = text; _lines[i].color = text; _states[i].color = text; }
                Outline(_titles[i], now);
                Outline(_lines[i], now);
                Outline(_states[i], now);
                HudText.Set(_states[i], now ? Earned : ever ? Unlocked : Locked);
            }
        }

        static void Outline(Text t, bool onBrass)
        {
            TextOutline o = t.GetComponent<TextOutline>();
            Color c = onBrass ? CMTokens.WarmCream.Color : CMTokens.VoidNavy.Color;
            if (o != null && o.OutlineColor != c) { o.OutlineColor = c; t.SetVerticesDirty(); }
        }

        public string State(int i) => i >= 0 && i < _states.Length ? _states[i].text : null;
    }

    /// <summary>
    /// DG-38 live hookup and toast. Creates Achievements.Current (saved in PlayerPrefs; an in-memory store under the
    /// capture tool, autopilot or batch mode so tools never touch the player's save), feeds it every CombatTally event and
    /// the local hill seconds, and shows a toast top-centre under the match cluster for each new unlock (queued, one at a
    /// time: 0.2 s in, 3.2 s held, 0.2 s out). At match end it shows the list unless ShowListAtMatchEnd is false (the
    /// game-flow results screen sets it false and builds AchievementList itself). View only.
    /// </summary>
    public sealed class AchievementToast : MonoBehaviour
    {
        public const float InSeconds = 0.2f, HoldSeconds = 3.2f, OutSeconds = 0.2f, ListDelaySeconds = 1.5f;
        // Polish pass 1: the right column under the big-moment stingers (HudZones.Toast), off the centre lane.
        public const float ToastW = HudZones.ColumnW, ToastH = 96f, ToastTop = HudZones.ToastTop, ToastRight = HudLayout.Margin;
        public const int QueueCap = 10;

        /// <summary>false: the results screen hosts the list, this component shows none at match end.</summary>
        public static bool ShowListAtMatchEnd = true;

        public Canvas Canvas { get; private set; }
        public bool ToastShowing => _showing >= 0;
        /// <summary>Index of the achievement on the toast now (-1 when none).</summary>
        public int ToastIndex => _showing;
        public string ToastTitle => _title != null ? _title.text : string.Empty;
        public AchievementList List { get; private set; }

        CombatLog _log;
        bool _subscribed;
        Achievements _ach;
        RectTransform _toast;
        CanvasGroup _group;
        Text _title, _line;
        readonly int[] _queue = new int[QueueCap];
        int _qHead, _qCount;
        int _showing = -1;
        float _age;
        float _endAge;
        RectTransform _listBox;

        void Awake() => Build();
        void OnDisable() => Unsubscribe();

        void OnDestroy()
        {
            if (_ach != null) _ach.Unlocked -= Enqueue;
            if (Achievements.Current == _ach) Achievements.Current = null;
        }

        public void Build()
        {
            if (Canvas != null) return;
            Canvas = FeedUi.MakeCanvas(transform, "Achievements Canvas", 18);
            _toast = FeedUi.Anchored(Canvas.transform, "Achievement Toast", new Vector2(1f, 1f), -ToastRight, -ToastTop, ToastW, ToastH);
            _group = _toast.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            MenuUi.Panel(_toast, "Panel", 0f, 0f, ToastW, ToastH);
            VectorShape medal = MenuUi.Shape(_toast, "Medal", ShapeKind.Shield, 18f, 14f, 60f, 68f, CMTokens.ToolBrass.Color, 10f);
            medal.OutlinePx = 3f;
            MenuUi.Shape(medal.transform, "Gem", ShapeKind.Diamond, 18f, 20f, 24f, 24f, CMTokens.WarmCream.Color, 4f).OutlinePx = 2f;
            MenuUi.Label(_toast, "Kicker", CMTokens.Ui, UiScale.TypeMin, CMTokens.ToolBrass.Color, TextAnchor.MiddleLeft, 98f, 10f, 480f, 22f, "ACHIEVEMENT UNLOCKED", 2f);
            _title = MenuUi.Label(_toast, "Title", CMTokens.Display, UiScale.TypeButton, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft, 98f, 30f, 480f, 36f, "", 3f);
            _line = MenuUi.Label(_toast, "Rule", CMTokens.Ui, UiScale.TypeSmall, CMTokens.WithAlpha(CMTokens.WarmCream.Color, 0.88f), TextAnchor.MiddleLeft, 98f, 64f, 480f, 24f, "", 2f);
            _toast.gameObject.SetActive(false);

            float lw = AchievementList.W, lh = AchievementList.Height;
            _listBox = FeedUi.Anchored(Canvas.transform, "Match End", new Vector2(0.5f, 0.5f), 0f, 0f, lw, lh);
            List = AchievementList.Build(_listBox, 0f, 0f);
            _listBox.gameObject.SetActive(false);
        }

        /// <summary>Uses a (tests pass their own with a memory store).</summary>
        public void Use(Achievements a)
        {
            if (_ach == a) return;
            if (_ach != null) _ach.Unlocked -= Enqueue;
            _ach = a;
            if (_ach != null) _ach.Unlocked += Enqueue;
            Achievements.Current = a;
        }

        void Subscribe()
        {
            if (_subscribed || _log == null) return;
            _log.Log.Emitted += OnEvent;
            _log.Log.MatchReset += OnMatchReset;
            _subscribed = true;
        }

        void Unsubscribe()
        {
            if (!_subscribed || _log == null) return;
            _log.Log.Emitted -= OnEvent;
            _log.Log.MatchReset -= OnMatchReset;
            _subscribed = false;
        }

        void OnEvent(CombatEvent e)
        {
            if (_ach != null && (_log == null || !_log.TitleUp)) _ach.Feed(e);
        }

        void OnMatchReset()
        {
            if (_ach != null) _ach.ResetMatch();
            _endAge = 0f;
            if (_listBox != null && _listBox.gameObject.activeSelf) _listBox.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (_log == null) _log = GetComponent<CombatLog>();
            int local = _log != null ? _log.LocalActor : HudBinder.LocalActor;
            World w = _log != null && _log.Driver != null ? _log.Driver.World : null;
            if (_ach == null && _log != null && _log.Driver != null)
            {
                bool tool = SimDriverBehaviour.CaptureMode || _log.Driver.HumanAutopilot || Application.isBatchMode;
                Use(new Achievements(tool ? (IAchievementStore)new MemoryAchievementStore() : new PlayerPrefsAchievementStore()) { LocalActor = local });
            }
            Subscribe();
            FeedUi.Refresh(Canvas);
            bool hide = _log == null || _log.Suppressed;
            if (Canvas.enabled == hide) Canvas.enabled = !hide;
            if (w != null && _ach != null && local >= 0 && local < w.ActorCount)
            {
                _ach.LocalTeam = w.Actors[local].Team;
                if (!_log.TitleUp) _ach.Sample(w.Actors[local].HillSeconds);
            }
            float dt = _log != null && _log.Paused ? 0f : Time.unscaledDeltaTime;
            Tick(dt);
            bool ended = w != null && w.Match.Phase == MatchPhase.Ended;
            _endAge = ended ? _endAge + dt : 0f;
            bool list = ShowListAtMatchEnd && ended && _endAge >= ListDelaySeconds && _ach != null;
            if (_listBox.gameObject.activeSelf != list) _listBox.gameObject.SetActive(list);
            if (list) List.Refresh(_ach);
        }

        void Enqueue(int index)
        {
            if (_qCount >= QueueCap) return;   // hard cap; the list still shows every unlock
            _queue[(_qHead + _qCount) % QueueCap] = index;
            _qCount++;
        }

        /// <summary>Advances the toast (tests drive it with fixed steps).</summary>
        public void Tick(float dt)
        {
            if (Canvas == null) Build();
            if (_showing < 0)
            {
                if (_qCount == 0) return;
                _showing = _queue[_qHead];
                _qHead = (_qHead + 1) % QueueCap;
                _qCount--;
                _age = 0f;
                HudText.Set(_title, Achievements.All[_showing].Title.ToUpperInvariantCached(_showing));
                HudText.Set(_line, Achievements.All[_showing].Description);
                _toast.gameObject.SetActive(true);
            }
            HudZones.Hold(HudZone.Toast, HudPriority.Banner);   // hud-containment-2: notices wait behind the toast (ToastLane)
            _age += dt;
            float total = InSeconds + HoldSeconds + OutSeconds;
            if (_age >= total)
            {
                _showing = -1;
                _toast.gameObject.SetActive(false);
                return;
            }
            float a = _age < InSeconds ? FeedUi.Ease(_age / InSeconds) : _age > InSeconds + HoldSeconds ? 1f - (_age - InSeconds - HoldSeconds) / OutSeconds : 1f;
            if (Mathf.Abs(_group.alpha - a) > 1e-3f) _group.alpha = a;
            // Slides in from the right edge (18 px, none with Reduce motion).
            _toast.anchoredPosition = new Vector2(-ToastRight + (UiMotion.Reduced ? 0f : (1f - a) * 18f), -ToastTop);
        }

        public int Queued => _qCount;
    }

    /// <summary>Upper-case achievement titles, built once (the toast never allocates while a match runs).</summary>
    static class AchievementTitleCache
    {
        static string[] _upper;

        public static string ToUpperInvariantCached(this string title, int index)
        {
            if (_upper == null)
            {
                _upper = new string[Achievements.All.Length];
                for (int i = 0; i < _upper.Length; i++) _upper[i] = Achievements.All[i].Title.ToUpperInvariant();
            }
            return index >= 0 && index < _upper.Length ? _upper[index] : title;
        }
    }
}
