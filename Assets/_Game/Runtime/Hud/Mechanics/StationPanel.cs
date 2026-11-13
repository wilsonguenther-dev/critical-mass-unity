using System;
using System.Globalization;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Interact;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Runtime.View.Mechanics;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Mechanics
{
    /// <summary>
    /// The base station panel (U-065). Opens with OpenStation (B, or press the right stick) while the local player stands in
    /// their own station ring; closes on leaving the ring, OpenStation again, Escape / Start, or the controller's B. Six class
    /// tiles switch class live (keys 1 to 6; the sim's 5 s switch limit is shown and counted down), and an upgrade row per
    /// class shows its level pips and the next price in wood and water, greyed when the team stock is short; only a Builder
    /// may buy (everyone else sees why). A purchase writes InputFrame.StationBuy = class index + 1 for one tick
    /// (DepthInputLatch); a switch reuses ClassPick (HumanInputState.QueueClassPick). Mouse: hover and click. Controller: the
    /// right stick moves the focus, A confirms (the panel holds A, Attack and Ability so the player never jumps or swings).
    /// Keyboard: 1 to 6 switch, SHIFT + 1 to 6 buy, ENTER (StationBuy) confirms the focus.
    /// </summary>
    public sealed class StationPanel : MonoBehaviour
    {
        public const int SortingOrder = 16;
        public const float PanelW = 580f, TileW = 176f, TileH = 70f, RowH = 44f;
        public const int Items = 12;

        public static StationPanel Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._open;
        public static event Action<bool> OpenChanged;

        public static void Open() { if (Instance != null) Instance.SetOpen(true); }
        public static void Close() { if (Instance != null) Instance.SetOpen(false); }
        public static void Toggle() { if (Instance != null) Instance.SetOpen(!Instance._open); }

        /// <summary>The focused item: 0..5 class tiles, 6..11 upgrade rows (tests).</summary>
        public int Focus => _focus;

        Canvas _canvas;
        RectTransform _root, _space, _panel;
        CanvasGroup _group;
        SimDriverBehaviour _driver;
        HumanInputSource _human;
        InputAction _openAction, _buyAction;
        bool _open;
        float _openedAt;
        int _focus;
        float _stickReadyAt;
        Func<bool> _savedPause;
        bool _pauseHooked;

        Text _title, _stock, _limit, _who;
        readonly VectorShape[] _item = new VectorShape[Items];
        readonly Text[] _itemName = new Text[Items], _itemNote = new Text[Items];
        readonly VectorShape[,] _pip = new VectorShape[Items, 3];
        readonly string[] _priceText = new string[6];
        readonly int[] _priceKey = { -1, -1, -1, -1, -1, -1 };
        static readonly string[] Digits = { "1", "2", "3", "4", "5", "6" };

        void Awake()
        {
            Instance = this;
            _driver = GetComponent<SimDriverBehaviour>();
            DepthWords.Ensure();
            Build();
        }

        void OnDestroy()
        {
            SetOpen(false);
            if (Instance == this) Instance = null;
        }

        void Build()
        {
            _canvas = InteractUi.MakeCanvas(transform, "Station Canvas", SortingOrder, out _root, out _space);
            Color cream = CMTokens.WarmCream.Color, brass = CMTokens.ToolBrass.Color;
            float h = 64f + 34f + 2f * (TileH + 10f) + 46f + 6f * (RowH + 6f) + 22f;
            _panel = MenuUi.Rt(_space, "Base Station");
            MenuUi.Place(_panel, HudLayout.RefW - 32f - PanelW, 300f, PanelW, h);
            _group = _panel.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            MenuUi.Panel(_panel, "Panel", 0f, 0f, PanelW, h);
            _title = MenuUi.Label(_panel, "Title", CMTokens.Display, 26, brass, TextAnchor.MiddleLeft, 22f, 12f, 300f, 40f, DepthWords.Station, 3f);
            _stock = MenuUi.Label(_panel, "Stock", CMTokens.Mono, 17, cream, TextAnchor.MiddleRight, 300f, 12f, PanelW - 322f, 40f, "", 2f);
            float y = 64f;
            MenuUi.Label(_panel, "Switch Head", CMTokens.Ui, 17, CMTokens.WithAlpha(cream, 0.85f), TextAnchor.MiddleLeft, 22f, y, 240f, 28f, DepthWords.SwitchClass, 2f);
            _limit = MenuUi.Label(_panel, "Switch Limit", CMTokens.Ui, 15, CMTokens.WithAlpha(cream, 0.75f), TextAnchor.MiddleRight, 260f, y, PanelW - 282f, 28f, "", 2f);
            y += 34f;
            for (int k = 0; k < 6; k++)
            {
                float x = 22f + (k % 3) * (TileW + 9f), ty = y + (k / 3) * (TileH + 10f);
                BuildItem(k, x, ty, TileW, TileH, true);
            }
            y += 2f * (TileH + 10f) + 6f;
            MenuUi.Label(_panel, "Upgrade Head", CMTokens.Ui, 17, CMTokens.WithAlpha(cream, 0.85f), TextAnchor.MiddleLeft, 22f, y, 200f, 30f, DepthWords.Upgrade, 2f);
            _who = MenuUi.Label(_panel, "Who", CMTokens.Ui, 14, CMTokens.WithAlpha(cream, 0.75f), TextAnchor.MiddleRight, 200f, y, PanelW - 222f, 30f, "", 1.5f);
            _who.resizeTextForBestFit = true;
            _who.resizeTextMinSize = 10;
            _who.resizeTextMaxSize = 14;
            y += 40f;
            for (int k = 0; k < 6; k++) BuildItem(6 + k, 22f, y + k * (RowH + 6f), PanelW - 44f, RowH, false);
            _panel.gameObject.SetActive(false);
        }

        void BuildItem(int i, float x, float y, float w, float h, bool tile)
        {
            Color cream = CMTokens.WarmCream.Color;
            VectorShape s = MenuUi.Shape(_panel, (tile ? "Class Tile " : "Upgrade Row ") + i, ShapeKind.RoundRect, x, y, w, h, CMTokens.PanelNavy.Color, 12f);
            s.OutlinePx = 3f;
            s.RimPx = 2f;
            s.RimColor = CMTokens.WithAlpha(cream, 0.4f);
            _item[i] = s;
            int cls = i % 6;
            string name = KillWords.ClassUpper[cls];
            if (tile)
            {
                VectorShape key = MenuUi.Shape(s.transform, "Key", ShapeKind.RoundRect, 8f, 8f, 26f, 26f, CMTokens.VoidNavy.Color, 6f);
                key.RimPx = 2f;
                key.RimColor = cream;
                MenuUi.Label(key.transform, "Digit", CMTokens.Mono, 15, cream, TextAnchor.MiddleCenter, 0f, 0f, 26f, 26f, Digits[cls], 1.5f);
                _itemName[i] = MenuUi.Label(s.transform, "Name", CMTokens.Ui, 17, cream, TextAnchor.MiddleLeft, 40f, 6f, w - 46f, 28f, name, 2f);
                _itemName[i].resizeTextForBestFit = true;
                _itemName[i].resizeTextMinSize = 11;
                _itemName[i].resizeTextMaxSize = 17;
                _itemNote[i] = MenuUi.Label(s.transform, "Note", CMTokens.Ui, 13, CMTokens.ToolBrass.Color, TextAnchor.MiddleLeft, 10f, 40f, w - 76f, 24f, "", 1.5f);
                for (int p = 0; p < 3; p++) _pip[i, p] = Pip(s.transform, w - 62f + p * 18f, 44f);
            }
            else
            {
                _itemName[i] = MenuUi.Label(s.transform, "Name", CMTokens.Ui, 18, cream, TextAnchor.MiddleLeft, 14f, 0f, 170f, h, name, 2f);
                for (int p = 0; p < 3; p++) _pip[i, p] = Pip(s.transform, 190f + p * 20f, (h - 16f) * 0.5f);
                _itemNote[i] = MenuUi.Label(s.transform, "Price", CMTokens.Mono, 16, cream, TextAnchor.MiddleRight, 260f, 0f, w - 274f, h, "", 2f);
            }
        }

        static VectorShape Pip(Transform p, float x, float y)
        {
            VectorShape pip = MenuUi.Shape(p, "Pip", ShapeKind.Diamond, x, y, 14f, 16f, CMTokens.MeterEmpty.Color, 3f);
            pip.OutlinePx = 2f;
            pip.RimPx = 0f;
            return pip;
        }

        // ------------------------------------------------------------------ open / close

        void SetOpen(bool on)
        {
            if (_open == on) return;
            _open = on;
            _openedAt = Time.unscaledTime;
            DepthInputLatch.SuppressButtons = on;
            if (_panel != null) _panel.gameObject.SetActive(on);
            HookPause(on);
            if (on) { DepthOut.Sound("depth-station-open"); _focus = Mathf.Clamp(_focus, 0, Items - 1); }
            OpenChanged?.Invoke(on);
        }

        /// <summary>While open, Escape / Start closes the panel instead of pausing (chained to whoever held the hook).</summary>
        void HookPause(bool on)
        {
            if (_human == null) _human = GetComponent<HumanInputSource>();
            if (_human == null) return;
            if (on && !_pauseHooked)
            {
                _savedPause = _human.PauseHandler;
                Func<bool> saved = _savedPause;
                _human.PauseHandler = () =>
                {
                    if (_open) { SetOpen(false); return true; }
                    return saved != null && saved();
                };
                _pauseHooked = true;
            }
            else if (!on && _pauseHooked)
            {
                _human.PauseHandler = _savedPause;
                _savedPause = null;
                _pauseHooked = false;
            }
        }

        InputAction Action(string name)
        {
            InputActionMap map = InputSystem.actions != null ? InputSystem.actions.FindActionMap("Player", false) : null;
            return map != null ? map.FindAction(name, false) : null;
        }

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            if (_driver == null) _driver = GetComponent<SimDriverBehaviour>();
            World w = _driver != null ? _driver.World : null;
            if (_openAction == null) _openAction = Action("OpenStation");
            if (_buyAction == null) _buyAction = Action("StationBuy");
            InteractUi.UpdateScale(_canvas);
            if (_canvas.worldCamera == null) _canvas.worldCamera = UnityEngine.Camera.main;
            const int me = 0;
            bool paused = _human != null && _human.State.Paused;
            bool can = w != null && me < w.ActorCount && w.Match.Phase != MatchPhase.Ended && DepthRead.StationsOn(w) && DepthRead.InStation(w, me) && !paused;
            if (!can) { if (_open) SetOpen(false); return; }
            if (_openAction != null && _openAction.WasPressedThisFrame()) SetOpen(!_open);
            if (!_open) return;
            float age = Time.unscaledTime - _openedAt;
            _group.alpha = FeelGate.ReduceMotion ? 1f : Mathf.Clamp01(age / 0.15f);
            Refresh(w, me);
            HandleInput(w, me);
        }

        void Refresh(World w, int me)
        {
            Actor a = w.Actors[me];
            TeamId t = a.Team;
            int wood = DepthRead.Stock(w, t, DepthRead.StockWood), water = DepthRead.Stock(w, t, DepthRead.StockWater);
            HudText.Set(_stock, StockLine(wood, water));
            int cd = DepthRead.SwitchCdTicks(w, me);
            double hz = w.Config.SimHz;
            int limitS = StationRules.Seconds((int)w.Config.Respawn.ClassSwitchCooldownTicks, hz);
            HudText.Set(_limit, cd > 0 ? DepthLines.Wait(0, StationRules.Seconds(cd, hz)) : LimitLine(limitS));
            bool builder = a.Alive && a.Cls == ClassId.Builder;
            HudText.Set(_who, builder ? string.Empty : DepthWords.OnlyBuilders);
            int max = DepthRead.MaxLevel(w);
            for (int k = 0; k < 6; k++)
            {
                ClassId c = ClassIds.All[k];
                int lvl = DepthRead.Level(w, t, c);
                // tile
                StationRules.Switch sw = StationRules.CanSwitch(a.Alive, true, a.Cls, c, cd);
                Style(k, sw == StationRules.Switch.Ok || sw == StationRules.Switch.Current, sw == StationRules.Switch.Current);
                HudText.Set(_itemNote[k], sw == StationRules.Switch.Current ? DepthWords.Current : sw == StationRules.Switch.Cooldown ? DepthLines.Int(StationRules.Seconds(cd, hz)) : string.Empty);
                Pips(k, lvl, max);
                // upgrade row
                bool next = DepthRead.NextCost(w, t, c, out int cw, out int cwa);
                StationRules.Buy b = StationRules.CanBuy(a.Alive, a.Cls == ClassId.Builder, true, next, wood, water, cw, cwa);
                Style(6 + k, b == StationRules.Buy.Ok, false);
                HudText.Set(_itemNote[6 + k], next ? Price(k, cw, cwa) : DepthWords.MaxLevel);
                Pips(6 + k, lvl, max);
            }
        }

        string _stockLine;
        int _stockKey = -1;
        string StockLine(int wood, int water)
        {
            int key = wood * 1000 + water;
            if (key != _stockKey) { _stockKey = key; _stockLine = DepthWords.Wood + " " + wood.ToString(CultureInfo.InvariantCulture) + "   " + DepthWords.Water + " " + water.ToString(CultureInfo.InvariantCulture); }
            return _stockLine;
        }

        string _limitLine;
        int _limitKey = -1;
        string LimitLine(int s)
        {
            if (s != _limitKey) { _limitKey = s; _limitLine = DepthWords.SwitchLimit + s.ToString(CultureInfo.InvariantCulture) + " S"; }
            return _limitLine;
        }

        string Price(int k, int wood, int water)
        {
            int key = wood * 1000 + water;
            if (_priceKey[k] != key)
            {
                _priceKey[k] = key;
                _priceText[k] = wood.ToString(CultureInfo.InvariantCulture) + " " + DepthWords.Wood + "  " + water.ToString(CultureInfo.InvariantCulture) + " " + DepthWords.Water;
            }
            return _priceText[k];
        }

        void Style(int i, bool usable, bool current)
        {
            bool focused = i == _focus;
            VectorShape s = _item[i];
            Color fill = current ? CMTokens.ToolBrass.Color : usable ? CMTokens.PanelNavy.Color : CMTokens.MeterEmpty.Color;
            if (s.FillColor != fill) s.SetFill(fill);
            float rim = focused ? 4f : 2f;
            if (!Mathf.Approximately(s.RimPx, rim)) { s.RimPx = rim; s.SetVerticesDirty(); }
            Color rc = focused ? CMTokens.WarmCream.Color : CMTokens.WithAlpha(CMTokens.WarmCream.Color, 0.35f);
            if (s.RimColor != rc) s.SetRim(rc);
            Color text = current ? CMTokens.VoidNavy.Color : usable ? CMTokens.WarmCream.Color : CMTokens.Stone.Color;
            if (_itemName[i].color != text) _itemName[i].color = text;
            if (_itemNote[i].color != text && i >= 6) _itemNote[i].color = text;
        }

        void Pips(int i, int lvl, int max)
        {
            for (int p = 0; p < 3; p++)
            {
                VectorShape pip = _pip[i, p];
                bool on = p < Mathf.Max(1, max);
                if (pip.gameObject.activeSelf != on) pip.gameObject.SetActive(on);
                if (!on) continue;
                Color c = p < lvl ? CMTokens.ToolBrass.Color : CMTokens.MeterEmpty.Color;
                if (pip.FillColor != c) pip.SetFill(c);
            }
        }

        void HandleInput(World w, int me)
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            Gamepad pad = Gamepad.current;
            UnityEngine.Camera cam = _canvas.worldCamera;
            // mouse: hover focuses, click confirms
            if (mouse != null && cam != null)
            {
                Vector2 mp = mouse.position.ReadValue();
                for (int i = 0; i < Items; i++)
                {
                    if (!RectTransformUtility.RectangleContainsScreenPoint(_item[i].rectTransform, mp, cam)) continue;
                    if (mouse.delta.ReadValue().sqrMagnitude > 0f) _focus = i;
                    if (mouse.leftButton.wasPressedThisFrame) { _focus = i; Confirm(w, me, i); }
                    break;
                }
            }
            // keyboard: 1..6 switch, SHIFT + 1..6 buy
            if (kb != null)
            {
                bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                for (int k = 0; k < 6; k++)
                {
                    if (!DigitPressed(kb, k)) continue;
                    _focus = shift ? 6 + k : k;
                    Confirm(w, me, _focus);
                }
            }
            if (_buyAction != null && _buyAction.WasPressedThisFrame()) Confirm(w, me, _focus);
            if (pad != null)
            {
                Vector2 st = pad.rightStick.ReadValue();
                float now = Time.unscaledTime;
                if (st.sqrMagnitude > 0.5f * 0.5f && now >= _stickReadyAt)
                {
                    _stickReadyAt = now + 0.18f;
                    _focus = Step(_focus, st);
                    DepthOut.Sound("depth-station-move");
                }
                else if (st.sqrMagnitude < 0.2f * 0.2f) _stickReadyAt = 0f;
                if (pad.buttonSouth.wasPressedThisFrame) Confirm(w, me, _focus);
                if (pad.buttonEast.wasPressedThisFrame) SetOpen(false);
            }
        }

        static bool DigitPressed(Keyboard kb, int k)
        {
            switch (k)
            {
                case 0: return kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame;
                case 1: return kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame;
                case 2: return kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame;
                case 3: return kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame;
                case 4: return kb.digit5Key.wasPressedThisFrame || kb.numpad5Key.wasPressedThisFrame;
                default: return kb.digit6Key.wasPressedThisFrame || kb.numpad6Key.wasPressedThisFrame;
            }
        }

        /// <summary>Focus navigation: tiles are a 3 x 2 grid, then the six upgrade rows in a column.</summary>
        public static int Step(int focus, Vector2 d)
        {
            bool horiz = Mathf.Abs(d.x) > Mathf.Abs(d.y);
            if (focus < 6)
            {
                int col = focus % 3, row = focus / 3;
                if (horiz) col = Mathf.Clamp(col + (d.x > 0 ? 1 : -1), 0, 2);
                else if (d.y < 0) { if (row == 1) return 6; row = 1; }
                else row = 0;
                return row * 3 + col;
            }
            if (horiz) return focus;
            if (d.y < 0) return Mathf.Min(11, focus + 1);
            return focus == 6 ? 3 : focus - 1;
        }

        /// <summary>Acts on item i: a tile switches class (ClassPick), a row buys (StationBuy for one tick). Refusals say why.</summary>
        void Confirm(World w, int me, int i)
        {
            Actor a = w.Actors[me];
            int k = i % 6;
            ClassId c = ClassIds.All[k];
            if (i < 6)
            {
                StationRules.Switch s = StationRules.CanSwitch(a.Alive, DepthRead.InStation(w, me), a.Cls, c, DepthRead.SwitchCdTicks(w, me));
                if (s == StationRules.Switch.Current) return;
                if (s == StationRules.Switch.Cooldown) { DepthOut.Tell(DepthLines.Wait(0, StationRules.Seconds(DepthRead.SwitchCdTicks(w, me), w.Config.SimHz))); DepthOut.Sound("depth-no"); return; }
                if (s == StationRules.Switch.NotAtStation) { DepthOut.Tell(DepthWords.GoToStation); DepthOut.Sound("depth-no"); return; }
                if (_human == null) _human = GetComponent<HumanInputSource>();
                if (_human != null) _human.State.QueueClassPick(c);
                DepthOut.Sound("depth-station-pick");
                return;
            }
            TeamId t = a.Team;
            bool next = DepthRead.NextCost(w, t, c, out int cw, out int cwa);
            StationRules.Buy b = StationRules.CanBuy(a.Alive, a.Cls == ClassId.Builder, DepthRead.InStation(w, me), next,
                DepthRead.Stock(w, t, DepthRead.StockWood), DepthRead.Stock(w, t, DepthRead.StockWater), cw, cwa);
            if (b != StationRules.Buy.Ok) { DepthOut.Tell(StationRules.Words(b)); DepthOut.Sound("depth-no"); return; }
            DepthInputLatch.QueueStationBuy(k);
            DepthOut.Sound("depth-station-buy");
        }
    }
}
