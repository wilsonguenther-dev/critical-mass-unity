using System;
using System.IO;
using System.Reflection;
using CriticalMass.Runtime.Codex;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menu
{
    /// <summary>
    /// The highlights of a finished match (DG-23): three stills captured at the moment (first knock-out, the hatch, the
    /// biggest multi-knock-out) with their time, and SAVE PHOTO (or F12), which saves the framed three-still card with
    /// the logo under persistentDataPath/Photos. Keyboard, mouse and controller share one focus (MenuItem).
    /// <para>Until the game-flow results screen (DG-01) exists this shows itself 2 s after the match ends
    /// (AutoShowOnMatchEnd). The results screen sets AutoShowOnMatchEnd = false and calls Show() / Hide() itself.</para>
    /// </summary>
    [CriticalMass.Runtime.Flow.GameOverlay(Order = 57, Scope = CriticalMass.Runtime.Flow.OverlayScope.MatchOnly)]   // installed once by GameBootstrap through the overlay registry
    public sealed class ResultsHighlights : MonoBehaviour
    {
        public const float AutoShowDelaySeconds = 2f;
        const float PanelX = 240f, PanelY = 200f, PanelW = 1440f, PanelH = 660f;
        const float TileW = 432f, TileH = 243f, TileGap = 24f, TileY = 320f;

        /// <summary>Show on its own when a match ends (false once a results screen drives it).</summary>
        public static bool AutoShowOnMatchEnd = true;
        public static event Action Shown, Hidden;

        static ResultsHighlights _instance;
        public static bool IsOpen => _instance != null && _instance._open;

        Canvas _canvas;
        readonly RawImage[] _stills = new RawImage[3];
        readonly Text[] _labels = new Text[3], _times = new Text[3], _empty = new Text[3];
        MenuItem _share, _close;
        Text _status;
        bool _open, _built;
        SimDriverBehaviour _driver;
        PauseMenu _menu;
        World _world;
        int _epoch = -1;
        bool _shownThisMatch;
        float _endedAt = -1f;

        /// <summary>Opens the highlights for the current match (no-op without a game).</summary>
        public static void Show()
        {
            if (_instance == null) return;
            _instance.Open();
        }

        public static void Hide()
        {
            if (_instance == null) return;
            _instance.Close();
        }

        /// <summary>The label text under highlight <paramref name="k"/> (tests).</summary>
        public static string LabelText(MomentKind k) => _instance != null && _instance._labels[(int)k] != null ? _instance._labels[(int)k].text : string.Empty;
        /// <summary>The still shown for highlight <paramref name="k"/> (null when none).</summary>
        public static Texture StillShown(MomentKind k) => _instance != null && _instance._stills[(int)k] != null ? _instance._stills[(int)k].texture : null;

        void OnEnable() => _instance = this;

        void OnDisable()
        {
            if (_instance == this) _instance = null;
        }

        void OnDestroy() => MomentRecorder.PhotoSaved -= OnSaved;

        void Awake() => MomentRecorder.PhotoSaved += OnSaved;

        void Update()
        {
            if (_driver == null) _driver = GetComponent<SimDriverBehaviour>();
            if (_menu == null) _menu = GetComponent<PauseMenu>();
            World w = _driver != null ? _driver.World : null;
            if (w != null && (!ReferenceEquals(w, _world) || w.Epoch != _epoch))
            {
                _world = w;
                _epoch = w.Epoch;
                _shownThisMatch = false;
                _endedAt = -1f;
                if (_open) Close();
            }
            if (AutoShowOnMatchEnd && w != null && !_shownThisMatch && w.Match.Phase == MatchPhase.Ended && !FlowRunning())
            {
                if (_endedAt < 0f) _endedAt = Time.unscaledTime;
                if (Time.unscaledTime - _endedAt >= AutoShowDelaySeconds) Open();
            }
            if (!_open) return;
            bool menuOpen = _menu != null && _menu.IsOpen;
            if (menuOpen) return;
            Gamepad pad = Gamepad.current;
            if (pad != null && pad.buttonEast.wasPressedThisFrame)
            {
                Close();
                return;
            }
            EventSystem es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject == null && NavigationPressed()) Select(_share);
        }

        static PropertyInfo _flowCurrent;
        static bool _flowLooked;

        /// <summary>
        /// True when the game-flow director (DG-01) runs: its results screen owns the end of the match, so this panel
        /// waits to be called (Show) instead of covering it. Found by name, so this file compiles with or without DG-01.
        /// </summary>
        static bool FlowRunning()
        {
            if (!_flowLooked)
            {
                _flowLooked = true;
                Type t = typeof(ResultsHighlights).Assembly.GetType("CriticalMass.Runtime.Flow.FlowDirector");
                _flowCurrent = t != null ? t.GetProperty("Current", BindingFlags.Public | BindingFlags.Static) : null;
            }
            if (_flowCurrent == null) return false;
            object cur = _flowCurrent.GetValue(null);
            return cur is UnityEngine.Object o ? o != null : cur != null;
        }

        static bool NavigationPressed()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && (kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame ||
                               kb.tabKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) return true;
            Gamepad pad = Gamepad.current;
            return pad != null && (pad.dpad.left.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame ||
                                   pad.leftStick.ReadValue().sqrMagnitude > 0.25f);
        }

        void Open()
        {
            Build();
            _open = true;
            _shownThisMatch = true;
            Refresh();
            HudText.Set(_status, string.Empty);
            _canvas.enabled = true;
            Select(_share);
            Shown?.Invoke();
        }

        void Close()
        {
            if (!_open) return;
            _open = false;
            if (_canvas != null) _canvas.enabled = false;
            if (EventSystem.current != null && _share != null && EventSystem.current.currentSelectedGameObject != null &&
                EventSystem.current.currentSelectedGameObject.transform.IsChildOf(_canvas.transform))
                EventSystem.current.SetSelectedGameObject(null);
            Hidden?.Invoke();
        }

        static void Select(Selectable s)
        {
            if (s == null || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(null);
            s.Select();
        }

        void Refresh()
        {
            MomentRecorder rec = MomentRecorder.Current;
            for (int k = 0; k < 3; k++)
            {
                var kind = (MomentKind)k;
                bool has = rec != null && rec.Has(kind);
                Moment m = has ? rec.Get(kind) : default;
                Texture2D still = has ? rec.Still(kind) : null;
                _stills[k].texture = still;
                _stills[k].enabled = still != null;
                HudText.Set(_labels[k], Label(kind, has, m));
                HudText.Set(_times[k], has ? "AT " + NumberCache.Mmss(m.MatchTime) : string.Empty);
                HudText.Set(_empty[k], still != null ? string.Empty : has ? "NO PICTURE" : Missing(kind));
            }
        }

        static string Label(MomentKind k, bool has, Moment m)
        {
            switch (k)
            {
                case MomentKind.FirstKnockOut: return "FIRST KNOCK-OUT";
                case MomentKind.Hatch:
                    if (!has) return "THE HATCH";
                    return m.Code == 1 ? "THE HATCH: AZURE" : m.Code == 2 ? "THE HATCH: CRIMSON" : m.Code == 3 ? "THE HATCH: BERSERK" : "THE HATCH: NO SIDE";
                default:
                    if (!has) return "MULTI KNOCK-OUT";
                    return m.Count == 2 ? "DOUBLE KNOCK-OUT" : m.Count == 3 ? "TRIPLE KNOCK-OUT" : m.Count + "X KNOCK-OUT";
            }
        }

        static string Missing(MomentKind k) =>
            k == MomentKind.FirstKnockOut ? "NO KNOCK-OUTS" : k == MomentKind.Hatch ? "THE EGG HELD" : "NO MULTI KNOCK-OUT";

        void Share()
        {
            MomentRecorder rec = MomentRecorder.Current;
            if (rec == null) return;
            HudText.Set(_status, rec.SaveHighlightsCard() ? "Saving..." : "A photo is already being saved.");
        }

        void OnSaved(string path)
        {
            if (_open && _status != null) HudText.Set(_status, "Saved to " + Path.GetFileName(path));
        }

        void Build()
        {
            if (_built) return;
            _built = true;
            PauseMenu.EnsureEventSystem(transform);
            _canvas = OverlayUi.Make(transform, "Highlights Canvas", OverlayUi.HighlightsOrder, true, false, out RectTransform space);
            Image wash = OverlayUi.Fill(_canvas.transform, "Wash", CMTokens.WithAlpha(CMTokens.VoidNavy.Color, 0.55f), true);
            MenuUi.Stretch(wash.rectTransform);
            wash.transform.SetAsFirstSibling();
            MenuUi.Panel(space, "Panel", PanelX, PanelY, PanelW, PanelH);
            MenuUi.Label(space, "Title", CMTokens.Display, 56, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter, PanelX, PanelY + 24f, PanelW, 80f,
                "HIGHLIGHTS", 4f);
            float x0 = PanelX + (PanelW - (3 * TileW + 2 * TileGap)) * 0.5f;
            for (int k = 0; k < 3; k++)
            {
                float x = x0 + k * (TileW + TileGap);
                VectorShape frame = MenuUi.Shape(space, "Frame " + k, ShapeKind.RoundRect, x - 6f, TileY - 6f, TileW + 12f, TileH + 12f,
                    CMTokens.VoidNavy.Color, 12f);
                frame.RimPx = 2f;
                _empty[k] = MenuUi.Label(space, "Empty " + k, CMTokens.Display, 22, CMTokens.Stone.Color, TextAnchor.MiddleCenter, x, TileY, TileW, TileH, "", 2f);
                RawImage img = MenuUi.Rt(space, "Still " + k).gameObject.AddComponent<RawImage>();
                MenuUi.Place(img.rectTransform, x, TileY, TileW, TileH);
                img.raycastTarget = false;
                img.enabled = false;
                _stills[k] = img;
                _labels[k] = MenuUi.Label(space, "Label " + k, CMTokens.Display, 24, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft, x, TileY + TileH + 16f,
                    TileW, 36f, "", 3f);
                _times[k] = MenuUi.Label(space, "Time " + k, CMTokens.Mono, 20, CMTokens.ToolBrass.Color, TextAnchor.MiddleLeft, x, TileY + TileH + 52f,
                    TileW, 30f, "", 2f);
            }
            const float bw = 460f, cw = 300f, gap = 24f, by = PanelY + PanelH - 132f;
            float bx = PanelX + (PanelW - (bw + gap + cw)) * 0.5f;
            _share = MenuUi.Item(space, "Share", bx, by, bw, 72f, "SAVE PHOTO   " + CriticalMass.Runtime.Input.InputGlyphs.ForPath("<Keyboard>/f12").Words.ToUpperInvariant(), MenuItem.Kind.Button, 30, true, CMTokens.Display);
            _share.Body.Radius = 16f;
            _share.Submitted = Share;
            _close = MenuUi.Item(space, "Close", bx + bw + gap, by, cw, 72f, "CLOSE", MenuItem.Kind.Button, 30, true, CMTokens.Display);
            _close.Body.Radius = 16f;
            _close.Submitted = Close;
            _share.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = _close, selectOnRight = _close };
            _close.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = _share, selectOnRight = _share };
            _status = MenuUi.Label(space, "Status", CMTokens.Ui, 20, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter, PanelX, by + 82f, PanelW, 30f, "", 2f);
            _canvas.enabled = false;
        }
    }
}
