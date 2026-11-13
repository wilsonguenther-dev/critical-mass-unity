using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Interact;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Menus;
using CriticalMass.Runtime.View.Mechanics;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Mechanics
{
    /// <summary>
    /// The emote wheel (A15 / DG-107 "an emote key"): hold the Emote binding (T, or press the left stick; rebindable in the
    /// Controls tab), steer with the mouse or the right stick, release to play CHEER, WAVE, TAUNT or BOW on your fighter
    /// (MechanicsPoses) with a speech bubble over your head. A quick tap plays CHEER. View only: emotes never reach the sim,
    /// so they cannot change a replay. Blocked while paused, dead, in class select or on the full map.
    /// </summary>
    public sealed class EmoteWheel : MonoBehaviour
    {
        public const int SortingOrder = 15;
        public const float Radius = 112f, DeadZone = 26f, TapSeconds = 0.22f, ChipW = 170f, ChipH = 48f;

        public static EmoteWheel Instance { get; private set; }
        public bool IsOpen { get; private set; }
        public int Selected { get; private set; } = -1;
        /// <summary>Raised when an emote starts (k = 0..3).</summary>
        public static event System.Action<int> Played;

        public readonly InteractKey Key = new InteractKey("Emote", UnityEngine.InputSystem.Key.T, GamepadButton.LeftStick, "T", "L3");

        Canvas _canvas;
        RectTransform _root, _space, _wheel, _bubble;
        readonly VectorShape[] _chips = new VectorShape[Emotes.Count];
        readonly Text[] _labels = new Text[Emotes.Count];
        Text _hub, _bubbleText;
        CanvasGroup _bubbleGroup;
        SimDriverBehaviour _driver;
        CombatLog _log;
        float _openedAt, _lastPlay = -9f;
        bool _bound, _wasPressed;
        int _logSearch;
        Vector2 _mouseAtOpen;

        static readonly Vector2[] Offsets = { new Vector2(0f, Radius), new Vector2(Radius, 0f), new Vector2(0f, -Radius), new Vector2(-Radius, 0f) };

        void Awake()
        {
            Instance = this;
            _driver = GetComponent<SimDriverBehaviour>();
            _canvas = InteractUi.MakeCanvas(transform, "Emote Canvas", SortingOrder, out _root, out _space);
            Color cream = CMTokens.WarmCream.Color;
            _wheel = InteractUi.Centred(_space, "Emote Wheel", 2f * Radius + ChipW, 2f * Radius + ChipH);
            VectorShape hub = MenuUi.Shape(_wheel, "Hub", ShapeKind.Circle, 0f, 0f, 66f, 66f, CMTokens.VoidNavy.Color, 8f);
            Centre(hub.rectTransform, Vector2.zero, 66f, 66f);
            hub.RimColor = cream;
            _hub = MenuUi.Fit(MenuUi.Label(hub.transform, "Word", CMTokens.Ui, UiScale.TypeMin, cream, TextAnchor.MiddleCenter, 0f, 0f, 66f, 66f, "EMOTE", 2f), 13);
            for (int k = 0; k < Emotes.Count; k++)
            {
                VectorShape chip = MenuUi.Shape(_wheel, "Chip " + k, ShapeKind.RoundRect, 0f, 0f, ChipW, ChipH, CMTokens.PanelNavy.Color, 14f);
                Centre(chip.rectTransform, Offsets[k], ChipW, ChipH);
                chip.OutlinePx = 4f;
                chip.RimPx = 2f;
                _chips[k] = chip;
                _labels[k] = MenuUi.Label(chip.transform, "Label", CMTokens.Display, UiScale.TypeSmall, cream, TextAnchor.MiddleCenter, 0f, 0f, ChipW, ChipH, Emotes.Names[k], 2f);
            }
            _wheel.gameObject.SetActive(false);

            _bubble = InteractUi.Centred(_space, "Emote Bubble", 150f, 48f);
            _bubbleGroup = _bubble.gameObject.AddComponent<CanvasGroup>();
            _bubbleGroup.blocksRaycasts = false;
            VectorShape bs = MenuUi.Shape(_bubble, "Panel", ShapeKind.RoundRect, 0f, 0f, 150f, 48f, cream, 16f);
            bs.OutlinePx = 4f;
            bs.RimPx = 0f;
            _bubbleText = MenuUi.Label(_bubble, "Word", CMTokens.Display, UiScale.TypeBody, CMTokens.VoidNavy.Color, TextAnchor.MiddleCenter, 0f, 0f, 150f, 48f, "", 0f);
            _bubble.gameObject.SetActive(false);
            ControlBindings.BindingsChanged += Rebind;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            ControlBindings.BindingsChanged -= Rebind;
        }

        void Rebind() => _bound = false;

        static void Centre(RectTransform rt, Vector2 at, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = at;
        }

        bool Blocked(World w)
        {
            if (w == null || w.Match.Phase == MatchPhase.Ended) return true;
            if (_log != null && (_log.Paused || _log.Suppressed)) return true;
            if (FullMap.IsOpen || ClassSelect.IsOpen) return true;
            MechanicsWorldView v = MechanicsWorldView.Active;
            int me = v != null ? v.Feed.LocalActor : 0;
            return me < 0 || me >= w.ActorCount || !w.Actors[me].Alive;
        }

        void Update()
        {
            if (!_bound)
            {
                _bound = true;
                Key.Bind(InputSystem.actions != null ? InputSystem.actions.FindActionMap(ControlBindings.MapName, false) : null);
                HudText.Set(_hub, Key.Glyph(ControlBindings.ActiveDevice) + "\nEMOTE");
            }
            InteractUi.UpdateScale(_canvas);
            if (_canvas.worldCamera == null) _canvas.worldCamera = UnityEngine.Camera.main;
            if (_log == null && --_logSearch <= 0) { _logSearch = 30; _log = FindAnyObjectByType<CombatLog>(); }
            World w = _driver != null ? _driver.World : null;
            bool pressed = Key.IsPressed;
            bool blocked = Blocked(w);
            float now = Time.realtimeSinceStartup;
            if (blocked)
            {
                if (IsOpen) Close();
                _wasPressed = pressed;
                Bubble(w);
                return;
            }
            if (pressed && !_wasPressed) Open(now);
            if (IsOpen) Steer();
            if (!pressed && _wasPressed && IsOpen) Release(now);
            _wasPressed = pressed;
            Bubble(w);
        }

        void Open(float now)
        {
            IsOpen = true;
            Selected = -1;
            _openedAt = now;
            Mouse mouse = Mouse.current;
            _mouseAtOpen = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            _wheel.anchoredPosition = Vector2.zero;
            Paint();
            _wheel.gameObject.SetActive(true);
        }

        void Steer()
        {
            Vector2 d = Vector2.zero;
            Gamepad pad = Gamepad.current;
            if (ControlBindings.ActiveDevice == ControlDevice.Gamepad && pad != null) d = pad.rightStick.ReadValue() * Radius;
            else
            {
                Mouse mouse = Mouse.current;
                if (mouse != null) d = (mouse.position.ReadValue() - _mouseAtOpen) / Mathf.Max(0.01f, _canvas.scaleFactor);
            }
            int s = Emotes.Sector(d, DeadZone);
            if (s == Selected) return;
            Selected = s;
            Paint();
        }

        void Paint()
        {
            for (int k = 0; k < Emotes.Count; k++)
            {
                bool on = k == Selected;
                _chips[k].SetFill(on ? CMTokens.ToolBrass.Color : CMTokens.PanelNavy.Color);
                _labels[k].color = on ? CMTokens.VoidNavy.Color : CMTokens.WarmCream.Color;
                float sc = on ? 1.08f : 1f;
                _chips[k].rectTransform.localScale = new Vector3(sc, sc, 1f);
            }
        }

        void Release(float now)
        {
            int sel = Selected;
            bool tap = now - _openedAt <= TapSeconds;
            Close();
            if (sel < 0 && tap) sel = 0;
            if (sel < 0 || now - _lastPlay < Emotes.Cooldown) return;
            _lastPlay = now;
            MechanicsPoses.Play(sel);
            HudText.Set(_bubbleText, Emotes.Bubble[sel]);
            Played?.Invoke(sel);
        }

        void Close()
        {
            IsOpen = false;
            Selected = -1;
            if (_wheel.gameObject.activeSelf) _wheel.gameObject.SetActive(false);
        }

        /// <summary>The speech bubble over the local player while an emote plays.</summary>
        void Bubble(World w)
        {
            float env = MechanicsPoses.EmoteEnvelope();
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            MechanicsWorldView v = MechanicsWorldView.Active;
            int me = v != null ? v.Feed.LocalActor : 0;
            bool on = env > 0f && w != null && cam != null && me >= 0 && me < w.ActorCount && w.Actors[me].Alive;
            if (_bubble.gameObject.activeSelf != on) _bubble.gameObject.SetActive(on);
            if (!on) return;
            Vector3 head = SimSpace.ToWorld(w.X[me], GroundHeight.At(w.Map, w.X[me], w.Z[me]) + 2.6, w.Z[me]);
            Vector3 vp = cam.WorldToViewportPoint(head);
            if (vp.z <= 0f) { _bubble.gameObject.SetActive(false); return; }
            Vector2 size = _space.rect.size;
            _bubble.anchoredPosition = new Vector2((vp.x - 0.5f) * size.x, (vp.y - 0.5f) * size.y);
            _bubbleGroup.alpha = env;
            float s = 0.85f + 0.15f * env;
            _bubble.localScale = new Vector3(s, s, 1f);
        }
    }
}
