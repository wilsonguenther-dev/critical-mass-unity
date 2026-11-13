using System;
using System.Collections.Generic;
using System.Globalization;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Progression;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menu
{
    /// <summary>Canvas and drawing helpers shared by the progression screens (same craft as the pause menu).</summary>
    public static class ProgressionUi
    {
        /// <summary>
        /// A Screen Space - Camera canvas on the main camera (overlay when there is no camera yet, e.g. a menu before the
        /// world exists) with a raycaster, and a centred 1920x1080 layout space (top-left placement via MenuUi.Place).
        /// </summary>
        public static Canvas MakeCanvas(Transform parent, string name, int order, out RectTransform space)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            Canvas c = go.AddComponent<Canvas>();
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null)
            {
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = Mathf.Max(0.4f, cam.nearClipPlane * 1.3f);
            }
            else c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            go.AddComponent<GraphicRaycaster>();
            space = MenuUi.Rt(go.transform, "Space");
            space.anchorMin = space.anchorMax = new Vector2(0.5f, 0.5f);
            space.pivot = new Vector2(0.5f, 0.5f);
            space.sizeDelta = new Vector2(HudLayout.RefW, HudLayout.RefH);
            UpdateScale(c);
            return c;
        }

        /// <summary>Canvas scale min(w/1920, h/1080) for the camera's (or screen's) pixel size; no work when unchanged.</summary>
        public static void UpdateScale(Canvas c)
        {
            if (c == null) return;
            UnityEngine.Camera cam = c.renderMode == RenderMode.ScreenSpaceCamera ? c.worldCamera : null;
            float w = cam != null ? cam.pixelWidth : Screen.width, h = cam != null ? cam.pixelHeight : Screen.height;
            if (w < 1 || h < 1) return;
            float s = HudLayout.Scale(w, h);
            if (Mathf.Abs(c.scaleFactor - s) > 1e-4f) c.scaleFactor = s;
        }

        /// <summary>A full-screen Void Navy wash that also swallows clicks.</summary>
        public static Image Wash(Transform parent, float alpha)
        {
            Image wash = MenuUi.Rt(parent, "Wash").gameObject.AddComponent<Image>();
            MenuUi.Stretch(wash.rectTransform);
            wash.color = CMTokens.WithAlpha(CMTokens.VoidNavy.Color, alpha);
            wash.raycastTarget = true;
            return wash;
        }

        /// <summary>A brass coin glyph (outer disc, darker inner disc), size px.</summary>
        public static VectorShape Coin(Transform parent, string name, float x, float y, float size)
        {
            VectorShape outer = MenuUi.Shape(parent, name, ShapeKind.Circle, x, y, size, size, CMTokens.ToolBrass.Color, 0f);
            outer.OutlinePx = 3f;
            outer.RimPx = 0f;
            float inner = size * 0.56f;
            VectorShape i = MenuUi.Shape(outer.transform, "Inner", ShapeKind.Circle, (size - inner) * 0.5f, (size - inner) * 0.5f, inner, inner,
                Color.Lerp(CMTokens.ToolBrass.Color, CMTokens.VoidNavy.Color, 0.28f), 0f);
            i.OutlinePx = 0f;
            i.RimPx = 0f;
            return outer;
        }

        public static string Thousands(int v) => v.ToString("#,0", CultureInfo.InvariantCulture);

        /// <summary>Wraps a label inside its rect (labels are made with overflow by default).</summary>
        public static Text Wrap(Text t)
        {
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        /// <summary>Explicit vertical navigation through items (wrapping), as the pause menu does.</summary>
        public static void ChainVertical(IList<Selectable> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                Navigation n = items[i].navigation;
                n.mode = Navigation.Mode.Explicit;
                n.selectOnUp = items[(i + items.Count - 1) % items.Count];
                n.selectOnDown = items[(i + 1) % items.Count];
                items[i].navigation = n;
            }
        }

        public static void ChainHorizontal(IList<Selectable> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                Navigation n = items[i].navigation;
                n.mode = Navigation.Mode.Explicit;
                n.selectOnLeft = items[(i + items.Count - 1) % items.Count];
                n.selectOnRight = items[(i + 1) % items.Count];
                items[i].navigation = n;
            }
        }

        public static void Select(Selectable s)
        {
            if (s == null || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(null);
            s.Select();
        }

        /// <summary>Ease out (cubic), 0..1.</summary>
        public static float EaseOut(float t)
        {
            t = Mathf.Clamp01(t);
            float u = 1f - t;
            return 1f - u * u * u;
        }
    }

    /// <summary>
    /// A level badge and skill-point bar (results, Barracks, character panel). Plain object, not a component: the owner
    /// calls Set (static) or Animate per frame. Built in 1920x1080 reference units at (x, y), width w, height 76.
    /// </summary>
    public sealed class LevelBar
    {
        public const float Height = 76f;
        public RectTransform Root { get; private set; }
        VectorShape _badge, _bar;
        Text _level, _caption, _sp;
        int _shownLevel = -1;
        int _shownSp = -1;
        float _popAt = -10f;

        public static LevelBar Build(Transform parent, string name, float x, float y, float w)
        {
            var b = new LevelBar();
            b.Root = MenuUi.Rt(parent, name);
            MenuUi.Place(b.Root, x, y, w, Height);
            b._badge = MenuUi.Shape(b.Root, "Badge", ShapeKind.Hexagon, 0f, 0f, 76f, 76f, CMTokens.ToolBrass.Color, 0f);
            b._badge.OutlinePx = 4f;
            b._badge.RimPx = 2f;
            b._level = MenuUi.Label(b._badge.transform, "Level", CMTokens.Display, UiScale.TypeHeading, CMTokens.VoidNavy.Color, TextAnchor.MiddleCenter,
                0f, 2f, 76f, 72f, "1", 0f);
            b._caption = MenuUi.Label(b.Root, "Caption", CMTokens.Display, UiScale.TypeSmall, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft,
                92f, 0f, w - 92f, 30f, ProgressionWords.Level + " 1", 2f);
            b._bar = MenuUi.Shape(b.Root, "Bar", ShapeKind.RoundRect, 92f, 36f, w - 92f, 30f, CMTokens.ToolBrass.Color, -1f);
            b._bar.SetEmpty(CMTokens.MeterEmpty.Color);
            b._bar.OutlinePx = 3f;
            b._bar.RimPx = 2f;
            b._sp = MenuUi.Label(b.Root, "Skill Points", CMTokens.Mono, UiScale.TypeMin, CMTokens.WarmCream.Color, TextAnchor.MiddleRight,
                92f, 0f, w - 92f, 30f, "", 2f);
            return b;
        }

        /// <summary>Shows a skill-point total (text rebuilt only when the number changes).</summary>
        public void Set(int skillPoints)
        {
            LevelProgress p = Leveling.Progress(skillPoints);
            _bar.Fill01 = p.Fraction;
            if (p.Level != _shownLevel)
            {
                if (_shownLevel > 0 && p.Level > _shownLevel) _popAt = Time.unscaledTime;
                _shownLevel = p.Level;
                HudText.Set(_level, p.Level.ToString(CultureInfo.InvariantCulture));
                HudText.Set(_caption, p.IsMax ? ProgressionWords.MaxLevel : ProgressionWords.Level + " " + p.Level.ToString(CultureInfo.InvariantCulture));
            }
            if (skillPoints != _shownSp)
            {
                _shownSp = skillPoints;
                HudText.Set(_sp, p.IsMax
                    ? ProgressionUi.Thousands(skillPoints) + " " + ProgressionWords.Sp
                    : ProgressionUi.Thousands(p.Into) + " / " + ProgressionUi.Thousands(p.Needed) + " " + ProgressionWords.Sp);
            }
        }

        /// <summary>Per-frame view work: the 180 ms badge pop after a level up (scale only).</summary>
        public void Tick()
        {
            float t = (Time.unscaledTime - _popAt) / 0.18f;
            float s = t >= 0f && t < 1f ? 1f + 0.16f * (1f - ProgressionUi.EaseOut(t)) : 1f;
            Vector3 want = new Vector3(s, s, 1f);
            if (_badge.rectTransform.localScale != want) _badge.rectTransform.localScale = want;
        }
    }

    /// <summary>
    /// The results reward panel (DG-39): level bar filling from the old skill points to the new (240 ms per level crossed,
    /// a pop and "LEVEL UP" with what it unlocked), then the breakdown, one row per source (count, coins, skill points),
    /// and the totals. Self-contained: the game-flow results screen (DG-01) calls RewardsPanel.Create(parent, x, y) on its
    /// own canvas and Show(MatchTracker rewards); until then RewardsOverlay hosts it. View only.
    /// </summary>
    public sealed class RewardsPanel : MonoBehaviour
    {
        public const float Width = 760f, PanelHeight = 600f;
        public const int MaxRows = 9;
        const float SweepPerLevel = 0.24f;
        const float RowStagger = 0.05f;
        const float RowFade = 0.16f;

        LevelBar _bar;
        Text _outcome, _levelUp, _unlock, _totalCoins, _totalSp;
        readonly Text[,] _cells = new Text[MaxRows, 4];
        readonly CanvasGroup[] _rows = new CanvasGroup[MaxRows];
        MatchRewards _r;
        float _start;
        float _sweep;
        int _rowCount;
        bool _built;

        public bool Animating { get; private set; }

        /// <summary>Builds the panel at (x, y) top-left in the parent's 1920x1080 reference space.</summary>
        public static RewardsPanel Create(Transform parent, float x, float y)
        {
            RectTransform rt = MenuUi.Rt(parent, "Rewards Panel");
            MenuUi.Place(rt, x, y, Width, PanelHeight);
            RewardsPanel p = rt.gameObject.AddComponent<RewardsPanel>();
            p.Build();
            return p;
        }

        void Build()
        {
            if (_built) return;
            _built = true;
            Transform t = transform;
            Color cream = CMTokens.WarmCream.Color, brass = CMTokens.ToolBrass.Color, stone = CMTokens.Stone.Color;
            MenuUi.Panel(t, "Panel", 0f, 0f, Width, PanelHeight);
            MenuUi.Fit(MenuUi.Label(t, "Title", CMTokens.Display, UiScale.TypeLead, cream, TextAnchor.MiddleLeft, 32f, 20f, 400f, 46f, ProgressionWords.Rewards, 3f), 34);
            _outcome = MenuUi.Label(t, "Outcome", CMTokens.Display, UiScale.TypeHeading, brass, TextAnchor.MiddleRight, Width - 352f, 20f, 320f, 46f, "", 3f);
            _bar = LevelBar.Build(t, "Level Bar", 32f, 80f, Width - 64f);
            _levelUp = MenuUi.Label(t, "Level Up", CMTokens.Display, UiScale.TypeBody, brass, TextAnchor.MiddleLeft, 32f, 164f, 260f, 30f, "", 2f);
            _unlock = MenuUi.Label(t, "Unlock", CMTokens.Ui, UiScale.TypeSmall, cream, TextAnchor.MiddleLeft, 250f, 164f, Width - 282f, 30f, "", 2f);
            MenuUi.Label(t, "Head Coins", CMTokens.Ui, UiScale.TypeMin, stone, TextAnchor.MiddleRight, 480f, 204f, 110f, 26f, ProgressionWords.Coins, 0f);
            MenuUi.Label(t, "Head SP", CMTokens.Ui, UiScale.TypeMin, stone, TextAnchor.MiddleRight, 600f, 204f, 128f, 26f, ProgressionWords.Sp, 0f);
            for (int i = 0; i < MaxRows; i++)
            {
                RectTransform row = MenuUi.Rt(t, "Row " + i);
                MenuUi.Place(row, 0f, 234f + i * 34f, Width, 34f);
                _rows[i] = row.gameObject.AddComponent<CanvasGroup>();
                _rows[i].alpha = 0f;
                _cells[i, 0] = MenuUi.Label(row, "Label", CMTokens.Ui, UiScale.TypeSmall, cream, TextAnchor.MiddleLeft, 32f, 0f, 330f, 34f, "", 2f);
                _cells[i, 1] = MenuUi.Label(row, "Count", CMTokens.Mono, UiScale.TypeSmall, stone, TextAnchor.MiddleRight, 340f, 0f, 120f, 34f, "", 0f);
                _cells[i, 2] = MenuUi.Label(row, "Coins", CMTokens.Mono, UiScale.TypeSmall, cream, TextAnchor.MiddleRight, 480f, 0f, 110f, 34f, "", 2f);
                _cells[i, 3] = MenuUi.Label(row, "SP", CMTokens.Mono, UiScale.TypeSmall, cream, TextAnchor.MiddleRight, 600f, 0f, 128f, 34f, "", 2f);
            }
            VectorShape rule = MenuUi.Shape(t, "Rule", ShapeKind.RoundRect, 32f, 544f, Width - 64f, 3f, stone, 0f);
            rule.OutlinePx = 0f;
            rule.RimPx = 0f;
            MenuUi.Label(t, "Total", CMTokens.Display, UiScale.TypeBody, cream, TextAnchor.MiddleLeft, 32f, 552f, 200f, 40f, "TOTAL", 2f);
            ProgressionUi.Coin(t, "Total Coin", 440f, 558f, 28f);
            _totalCoins = MenuUi.Label(t, "Total Coins", CMTokens.Mono, UiScale.TypeBody, brass, TextAnchor.MiddleRight, 470f, 552f, 120f, 40f, "", 2f);
            _totalSp = MenuUi.Label(t, "Total SP", CMTokens.Mono, UiScale.TypeBody, brass, TextAnchor.MiddleRight, 600f, 552f, 128f, 40f, "", 2f);
        }

        /// <summary>Shows a match's rewards and starts the fill (view time, unscaled).</summary>
        public void Show(MatchRewards r)
        {
            Build();
            _r = r;
            if (r == null) return;
            gameObject.SetActive(true);
            HudText.Set(_outcome, r.Stats.Completed ? ProgressionWords.Outcome(r.Stats.Outcome) : string.Empty);
            HudText.Set(_levelUp, string.Empty);
            HudText.Set(_unlock, string.Empty);
            _rowCount = Mathf.Min(MaxRows, r.Lines.Count);
            for (int i = 0; i < MaxRows; i++)
            {
                bool on = i < _rowCount;
                _rows[i].alpha = 0f;
                if (!on)
                {
                    for (int k = 0; k < 4; k++) HudText.Set(_cells[i, k], string.Empty);
                    continue;
                }
                RewardLine l = r.Lines[i];
                HudText.Set(_cells[i, 0], ProgressionWords.Source(l.Source));
                HudText.Set(_cells[i, 1], ProgressionWords.Count(l));
                HudText.Set(_cells[i, 2], l.Coins > 0 ? "+" + ProgressionUi.Thousands(l.Coins) : "-");
                HudText.Set(_cells[i, 3], l.SkillPoints > 0 ? "+" + ProgressionUi.Thousands(l.SkillPoints) : "-");
            }
            HudText.Set(_totalCoins, "+" + ProgressionUi.Thousands(r.Coins));
            HudText.Set(_totalSp, "+" + ProgressionUi.Thousands(r.SkillPoints));
            _bar.Set(r.SkillPointsBefore);
            _sweep = SweepPerLevel * Mathf.Max(1, r.LevelsGained + 1);
            _start = Time.unscaledTime;
            Animating = true;
        }

        /// <summary>Jumps every animation to its end.</summary>
        public void Skip()
        {
            if (_r == null) return;
            _start = Time.unscaledTime - _sweep - RowStagger * MaxRows - RowFade;
        }

        void Update()
        {
            if (_bar != null) _bar.Tick();
            if (!Animating || _r == null) return;
            float t = Time.unscaledTime - _start;
            float f = _sweep > 0f ? ProgressionUi.EaseOut(t / _sweep) : 1f;
            int sp = _r.SkillPointsBefore + Mathf.RoundToInt((_r.SkillPointsAfter - _r.SkillPointsBefore) * f);
            int before = Leveling.LevelFor(_r.SkillPointsBefore), now = Leveling.LevelFor(sp);
            _bar.Set(sp);
            if (now > before && _levelUp.text.Length == 0)
            {
                HudText.Set(_levelUp, ProgressionWords.LevelUp);
                HudText.Set(_unlock, UnlockLine(before + 1, _r.LevelAfter));
            }
            bool rowsDone = true;
            for (int i = 0; i < _rowCount; i++)
            {
                float a = Mathf.Clamp01((t - i * RowStagger) / RowFade);
                if (_rows[i].alpha != a) _rows[i].alpha = a;
                rowsDone &= a >= 1f;
            }
            if (f >= 1f && rowsDone) Animating = false;
        }

        /// <summary>"Unlocked: HORNED HELM, SUNBURST" for the levels from..to (all classes, at most 3 names).</summary>
        public static string UnlockLine(int from, int to)
        {
            var names = new List<string>();
            for (int l = from; l <= to; l++)
                foreach (CosmeticItem it in CosmeticItems.UnlockedAtLevel(l))
                    if (!names.Contains(it.DisplayName)) names.Add(it.DisplayName);
            if (names.Count == 0) return string.Empty;
            int more = names.Count - 3;
            string s = "Unlocked: " + string.Join(", ", names.GetRange(0, Mathf.Min(3, names.Count)));
            return more > 0 ? s + " and " + more.ToString(CultureInfo.InvariantCulture) + " more" : s;
        }
    }

    /// <summary>
    /// Shows the reward panel by itself at match end while the game-flow results screen (DG-01) does not exist yet:
    /// a wash, the panel and PLAY AGAIN / BARRACKS / CONTINUE. DG-01 sets AutoShow = false and places RewardsPanel in
    /// its own results screen instead. Keyboard, mouse and controller share one focus; Escape or B continues.
    /// </summary>
    public sealed class RewardsOverlay : MonoBehaviour
    {
        /// <summary>False: never show at match end (DG-01 hosts the panel itself).</summary>
        public static bool AutoShow = true;
        public static RewardsOverlay Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._open;
        public static event Action Opened, Closed;

        Canvas _canvas;
        RewardsPanel _panel;
        MenuItem _first;
        HumanInputSource _input;
        Func<bool> _prevPause;
        bool _open, _built;

        void OnEnable()
        {
            Instance = this;
            MatchRewardTracker.RewardsReady += OnRewards;
        }

        void OnDisable()
        {
            MatchRewardTracker.RewardsReady -= OnRewards;
            if (_open) Hide();
            if (Instance == this) Instance = null;
        }

        void OnRewards(MatchRewards r)
        {
            if (AutoShow) Show(r);
        }

        public static void ShowLast()
        {
            if (Instance != null && MatchRewardTracker.Last != null) Instance.Show(MatchRewardTracker.Last);
        }

        public void Show(MatchRewards r)
        {
            Build();
            _open = true;
            _canvas.enabled = true;
            _panel.Show(r);
            _input = GetComponent<HumanInputSource>();
            if (_input != null)
            {
                _prevPause = _input.PauseHandler;
                _input.PauseHandler = OnPause;
            }
            PauseMenu.EnsureEventSystem(transform);
            ProgressionUi.Select(_first);
            Opened?.Invoke();
        }

        public static void Hide()
        {
            RewardsOverlay o = Instance;
            if (o == null || !o._open) return;
            o._open = false;
            if (o._canvas != null) o._canvas.enabled = false;
            if (o._input != null && o._input.PauseHandler == (Func<bool>)o.OnPause) o._input.PauseHandler = o._prevPause;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            Closed?.Invoke();
        }

        bool OnPause()
        {
            Hide();
            return true;
        }

        void Build()
        {
            if (_built) return;
            _built = true;
            _canvas = ProgressionUi.MakeCanvas(transform, "Rewards Canvas", 30, out RectTransform space);
            ProgressionUi.Wash(_canvas.transform, 0.66f).transform.SetAsFirstSibling();
            _panel = RewardsPanel.Create(space, (HudLayout.RefW - RewardsPanel.Width) * 0.5f, 140f);
            string[] labels = { "PLAY AGAIN", "BARRACKS", "CONTINUE" };
            Action[] acts = { PlayAgain, () => Barracks.Show(), Hide };
            var items = new List<Selectable>();
            float w = 236f, gap = 26f, x0 = (HudLayout.RefW - (3 * w + 2 * gap)) * 0.5f;
            for (int i = 0; i < labels.Length; i++)
            {
                MenuItem it = MenuUi.Item(space, labels[i], x0 + i * (w + gap), 770f, w, 76f, labels[i], MenuItem.Kind.Button, UiScale.TypeButton, true,
                    CMTokens.Display);
                it.Body.Radius = 16f;
                it.Submitted = acts[i];
                items.Add(it);
            }
            ProgressionUi.ChainHorizontal(items);
            _first = (MenuItem)items[0];
            _canvas.enabled = false;
        }

        void Update()
        {
            if (!_open) return;
            ProgressionUi.UpdateScale(_canvas);
            if (CharacterPanel.IsOpen) return;
            Keyboard kb = Keyboard.current;
            Gamepad pad = Gamepad.current;
            if (_panel.Animating && ((kb != null && kb.spaceKey.wasPressedThisFrame) || (pad != null && pad.buttonNorth.wasPressedThisFrame)))
                _panel.Skip();
            if (pad != null && pad.buttonEast.wasPressedThisFrame) Hide();
            EventSystem es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject == null &&
                ((kb != null && (kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) ||
                 (pad != null && (pad.dpad.left.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame))))
                ProgressionUi.Select(_first);
        }

        static void PlayAgain()
        {
            Hide();
            PauseMenu.PendingAfterReload = PauseMenu.AfterReload.Play;
            SceneManager.LoadScene(GameBootstrap.BootScenePath);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            AutoShow = true;
            Opened = null;
            Closed = null;
            Instance = null;
        }
    }
}
