using System;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>
    /// Owner walk 1 ("the warrior is still not clickable"): the fighter standing on the home diorama (and the big model on
    /// the class card) as a control, the way lobby screens of the genre work. Hover or focus draws a Tool Brass ring around
    /// the fighter and raises a prompt chip, CUSTOMISE with its key (Enter / A, or C anywhere on Home); a click, Enter or A
    /// opens the Barracks for that class; a drag turns the fighter (and never also counts as a click). It is a Selectable,
    /// so the keyboard and the controller reach it through the same focus as every button, the UI sounds bind to it, and
    /// the pointer turns into the game's pointer over it. View only.
    /// </summary>
    public sealed class FighterHotspot : Selectable, IPointerClickHandler, ISubmitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        /// <summary>Click, Enter or A.</summary>
        public Action Submitted;
        /// <summary>Degrees of turn per reference px dragged (the caller turns the model).</summary>
        public Action<float> Dragged;
        public const float DegreesPerPx = 0.45f;
        public VectorShape Ring;
        public RectTransform Chip;
        public PromptStrip ChipGlyphs;
        CanvasGroup _chipGroup;
        bool _dragging;
        float _show;

        /// <summary>The hover or focus look is on (tests).</summary>
        public bool Lit { get; private set; }
        public bool Dragging => _dragging;

        /// <summary>
        /// Builds the hotspot over (x, y, w, h) of <paramref name="parent"/> (reference px, top-left origin) with its ring
        /// and a CUSTOMISE chip centred under the fighter's feet at chipY.
        /// </summary>
        public static FighterHotspot Make(RectTransform parent, string name, float x, float y, float w, float h, float chipY, string words)
        {
            RectTransform rt = MenuUi.Rt(parent, name);
            MenuUi.Place(rt, x, y, w, h);
            Image hit = rt.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);   // invisible, but the raycaster sees it
            hit.raycastTarget = true;
            FighterHotspot f = rt.gameObject.AddComponent<FighterHotspot>();
            f.targetGraphic = hit;
            f.transition = Transition.None;
            f.Ring = MenuUi.Shape(rt, "Ring", ShapeKind.RoundRect, 0f, 0f, w, h, new Color(1f, 1f, 1f, 0f), 26f);
            f.Ring.OutlinePx = 3f;
            f.Ring.RimPx = 4f;
            f.Ring.SetRim(CMTokens.ToolBrass.Color);
            f.Ring.raycastTarget = false;

            const float cw = 300f, ch = 56f;
            f.Chip = MenuUi.Rt(rt, "Prompt");
            MenuUi.Place(f.Chip, (w - cw) * 0.5f, chipY - y, cw, ch);
            VectorShape body = MenuUi.Shape(f.Chip, "Body", ShapeKind.RoundRect, 0f, 0f, cw, ch, CMTokens.VoidNavy.Color, 16f);
            body.OutlinePx = 3f;
            body.RimPx = 3f;
            body.SetRim(CMTokens.ToolBrass.Color);
            body.raycastTarget = false;
            f.ChipGlyphs = PromptStrip.Make(f.Chip, "Glyphs", 30f, UiScale.TypeBody, CMTokens.WarmCream.Color, CMTokens.Display);
            MenuUi.Place(f.ChipGlyphs.Rect, 12f, 0f, cw - 24f, ch);
            f.ChipGlyphs.Align = TextAnchor.MiddleCenter;
            f.ChipGlyphs.SetHint("ENTER " + words, ControlDevice.KeyboardMouse);
            f._chipGroup = f.Chip.gameObject.AddComponent<CanvasGroup>();
            f._chipGroup.blocksRaycasts = false;
            f._chipGroup.alpha = 0f;
            f.Ring.enabled = false;
            f._words = words;
            return f;
        }

        string _words = "CUSTOMISE";

        /// <summary>Re-fits the ring and the chip after the hotspot moved or resized (w x h reference px).</summary>
        public void Fit(float w, float h)
        {
            if (Ring != null) MenuUi.Place(Ring.rectTransform, 0f, 0f, w, h);
            if (Chip != null)
            {
                Vector2 cs = Chip.sizeDelta;
                MenuUi.Place(Chip, (w - cs.x) * 0.5f, h - cs.y - 10f, cs.x, cs.y);
            }
        }
        ControlDevice _device = (ControlDevice)(-1);

        void Update()
        {
            ControlDevice d = ControlBindings.ActiveDevice;
            if (d != _device && ChipGlyphs != null)
            {
                _device = d;
                ChipGlyphs.SetHint((d == ControlDevice.Gamepad ? "A " : "ENTER ") + _words, d);
            }
            float target = Lit || _dragging ? 1f : 0f;
            _show = Mathf.MoveTowards(_show, target, Time.unscaledDeltaTime / 0.12f);
            if (_chipGroup != null) _chipGroup.alpha = _show;
            if (Ring != null && Ring.enabled != _show > 0.01f) Ring.enabled = _show > 0.01f;
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            Lit = state == SelectionState.Highlighted || state == SelectionState.Selected || state == SelectionState.Pressed;
            if (Ring != null)
            {
                // keyboard / controller focus draws the thick cream ring the buttons use; hover the brass ring
                bool keyFocus = state == SelectionState.Selected && !MenuItem.PointerNavigated;
                Ring.SetRim(keyFocus ? CMTokens.WarmCream.Color : CMTokens.ToolBrass.Color);
                Ring.RimPx = keyFocus ? MenuItem.FocusRingPx : 4f;
                Ring.SetVerticesDirty();
            }
            if (instant) _show = Lit ? 1f : 0f;
        }

        public override void OnPointerEnter(PointerEventData e)
        {
            MenuItem.PointerNavigated = true;
            base.OnPointerEnter(e);
            UiCursor.Hover(true);
            if (IsInteractable() && EventSystem.current != null && EventSystem.current.currentSelectedGameObject != gameObject) Select();
        }

        public override void OnPointerExit(PointerEventData e)
        {
            base.OnPointerExit(e);
            UiCursor.Hover(false);
        }

        public override void OnMove(AxisEventData e)
        {
            MenuItem.PointerNavigated = false;
            base.OnMove(e);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (!IsInteractable() || e.button != PointerEventData.InputButton.Left || _dragging || Time.unscaledTime < MenuItem.SubmitBlockedUntil) return;
            Submitted?.Invoke();
        }

        public void OnSubmit(BaseEventData e)
        {
            if (!IsInteractable() || Time.unscaledTime < MenuItem.SubmitBlockedUntil) return;
            Submitted?.Invoke();
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            _dragging = true;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_dragging) return;
            Canvas c = GetComponentInParent<Canvas>();
            float scale = c != null && c.rootCanvas.scaleFactor > 1e-4f ? c.rootCanvas.scaleFactor : 1f;
            Dragged?.Invoke(-e.delta.x / scale * DegreesPerPx);
        }

        public void OnEndDrag(PointerEventData e) => _dragging = false;

        protected override void OnDisable()
        {
            base.OnDisable();
            _dragging = false;
            if (UiCursor.Hovering) UiCursor.Reset();
        }
    }
}
