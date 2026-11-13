using System;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menu
{
    /// <summary>
    /// One focusable line of the pause menu and settings page, the same object for keyboard, mouse and controller:
    /// a Button (Enter / A / click), a Stepper (left and right change the value; a click steps forward, or back on the left
    /// third) or a Slider (left and right step it; click or drag sets it). Hovering with the mouse selects it, so the
    /// keyboard, the mouse and the controller share one focus. Drawn as a HUD sticker: Panel Navy fill, Void Navy outline,
    /// Warm Cream rim; focused = Tool Brass fill with Void Navy text.
    /// </summary>
    public sealed class MenuItem : Selectable, IPointerClickHandler, ISubmitHandler, IDragHandler, IInitializePotentialDragHandler
    {
        public enum Kind { Button, Stepper, Slider }

        public Kind ItemKind = Kind.Button;
        public Action Submitted;
        /// <summary>Stepper and Slider: -1 or +1.</summary>
        public Action<int> Stepped;
        /// <summary>Slider: an absolute value 0..1 from the pointer.</summary>
        public Action<float> SetFromPointer;
        public VectorShape Body;
        public Text Label;
        /// <summary>Optional second text (stepper or slider value) recoloured with the focus.</summary>
        public Text Value;
        /// <summary>Slider bar (Fill01 shows the value).</summary>
        public VectorShape Bar;

        /// <summary>Submits are ignored until this unscaled time (a rebind's key must not also press the button).</summary>
        public static float SubmitBlockedUntil;

        public static Color IdleFill => CMTokens.PanelNavy.Color;
        public static Color FocusFill => CMTokens.ToolBrass.Color;
        public static Color IdleText => CMTokens.WarmCream.Color;
        public static Color FocusText => CMTokens.VoidNavy.Color;

        bool Blocked => Time.unscaledTime < SubmitBlockedUntil;

        public void OnPointerClick(PointerEventData e)
        {
            if (!IsInteractable() || e.button != PointerEventData.InputButton.Left || Blocked) return;
            switch (ItemKind)
            {
                case Kind.Button:
                    Submitted?.Invoke();
                    break;
                case Kind.Stepper:
                    Stepped?.Invoke(LocalX01(e) < 1f / 3f ? -1 : 1);
                    break;
                case Kind.Slider:
                    SetFromPointer?.Invoke(BarX01(e));
                    break;
            }
        }

        public void OnSubmit(BaseEventData e)
        {
            if (!IsInteractable() || Blocked) return;
            if (ItemKind == Kind.Button) Submitted?.Invoke();
            else if (ItemKind == Kind.Stepper) Stepped?.Invoke(1);
        }

        public override void OnMove(AxisEventData e)
        {
            if (ItemKind != Kind.Button && IsInteractable() &&
                (e.moveDir == MoveDirection.Left || e.moveDir == MoveDirection.Right))
            {
                Stepped?.Invoke(e.moveDir == MoveDirection.Left ? -1 : 1);
                e.Use();
                return;
            }
            base.OnMove(e);
        }

        public void OnInitializePotentialDrag(PointerEventData e) => e.useDragThreshold = false;

        public void OnDrag(PointerEventData e)
        {
            if (ItemKind == Kind.Slider && IsInteractable()) SetFromPointer?.Invoke(BarX01(e));
        }

        public override void OnPointerEnter(PointerEventData e)
        {
            base.OnPointerEnter(e);
            if (IsInteractable() && EventSystem.current != null && EventSystem.current.currentSelectedGameObject != gameObject)
                Select();
        }

        float LocalX01(PointerEventData e)
        {
            RectTransform rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera ?? e.enterEventCamera, out Vector2 p))
                return 0.5f;
            Rect r = rt.rect;
            return Mathf.Clamp01((p.x - r.xMin) / Mathf.Max(1f, r.width));
        }

        float BarX01(PointerEventData e)
        {
            RectTransform rt = Bar != null ? Bar.rectTransform : (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera ?? e.enterEventCamera, out Vector2 p))
                return 0f;
            Rect r = rt.rect;
            return Mathf.Clamp01((p.x - r.xMin) / Mathf.Max(1f, r.width));
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            bool focus = state == SelectionState.Selected || state == SelectionState.Highlighted || state == SelectionState.Pressed;
            bool off = state == SelectionState.Disabled;
            if (Body != null)
            {
                Color f = focus ? FocusFill : IdleFill;
                if (state == SelectionState.Pressed) f = Color.Lerp(f, CMTokens.VoidNavy.Color, 0.2f);
                if (off) f = CMTokens.WithAlpha(f, 0.5f);
                Body.SetFill(f);
            }
            // States: idle (navy), focus = hover (brass; hovering moves the shared focus), pressed (darker brass and a
            // 3 % shrink, view only), disabled (half alpha, stone text, skipped by navigation).
            float sc = state == SelectionState.Pressed ? UiScale.PressedScale : 1f;
            if (transform.localScale.x != sc) transform.localScale = new Vector3(sc, sc, 1f);
            Color t = focus ? FocusText : IdleText;
            if (off) t = CMTokens.WithAlpha(CMTokens.Stone.Color, 0.9f);
            if (Label != null) Label.color = t;
            if (Value != null) Value.color = t;
            // Outlined text on a brass fill reads as a smudge: drop the outline while focused.
            SetOutline(Label, !focus);
            SetOutline(Value, !focus);
        }

        static void SetOutline(Text t, bool on)
        {
            if (t == null) return;
            TextOutline o = t.GetComponent<TextOutline>();
            if (o != null && o.enabled != on)
            {
                o.enabled = on;
                t.SetVerticesDirty();
            }
        }
    }

    /// <summary>Layout helpers in 1920x1080 reference units, top-left origin (as the HUD builds itself).</summary>
    public static class MenuUi
    {
        public static RectTransform Rt(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Places rt at (x, y) from the parent's top-left, y down, size (w, h).</summary>
        public static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x + w * 0.5f, -(y + h * 0.5f));
        }

        /// <summary>
        /// Never clip: the text wraps inside its rect and best-fit shrinks it from its size down to min (uGUI best-fit
        /// only shrinks when the overflow modes are bounded, which HudText.Make leaves unbounded; that is why the match
        /// setup's DEMOLITIONS EXPERT spilled past its card even with best-fit on).
        /// </summary>
        public static Text Fit(Text t, int min = UiScale.TypeMin)
        {
            if (t == null) return null;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextMaxSize = Mathf.Max(t.fontSize, min);
            t.resizeTextMinSize = Mathf.Min(min, t.fontSize);
            t.resizeTextForBestFit = true;
            return t;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
        }

        public static VectorShape Shape(Transform parent, string name, ShapeKind kind, float x, float y, float w, float h,
            Color fill, float radius = 12f, bool raycast = false)
        {
            RectTransform rt = Rt(parent, name);
            Place(rt, x, y, w, h);
            VectorShape s = rt.gameObject.AddComponent<VectorShape>();
            s.Kind = kind;
            s.Radius = radius;
            s.FillColor = fill;
            s.raycastTarget = raycast;
            return s;
        }

        public static Text Label(Transform parent, string name, Typeface face, int size, Color c, TextAnchor anchor,
            float x, float y, float w, float h, string text, float outline = CMTokens.OutlinePx)
        {
            Text t = HudText.Make(parent, name, face, size, c, anchor, outline);
            Place(t.rectTransform, x, y, w, h);
            t.text = text;
            return t;
        }

        /// <summary>A chunky panel: Panel Navy fill, 3 px Void Navy outline, 2 px Warm Cream rim, 18 px corners.</summary>
        public static VectorShape Panel(Transform parent, string name, float x, float y, float w, float h)
        {
            VectorShape s = Shape(parent, name, ShapeKind.RoundRect, x, y, w, h, CMTokens.PanelNavy.Color, 18f);
            s.OutlinePx = 4f;
            s.RimPx = 3f;
            return s;
        }

        /// <summary>A focusable sticker line with a label (left, or centred when centre is true).</summary>
        public static MenuItem Item(Transform parent, string name, float x, float y, float w, float h, string text,
            MenuItem.Kind kind, int fontSize = 24, bool centre = false, Typeface? face = null)
        {
            VectorShape body = Shape(parent, name, ShapeKind.RoundRect, x, y, w, h, MenuItem.IdleFill, 12f, true);
            body.RimPx = 2f;
            MenuItem item = body.gameObject.AddComponent<MenuItem>();
            item.ItemKind = kind;
            item.Body = body;
            item.targetGraphic = body;
            item.transition = Selectable.Transition.None;
            float pad = centre ? 0f : 18f;
            item.Label = Label(body.transform, "Label", face ?? CMTokens.Ui, fontSize, MenuItem.IdleText,
                centre ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft, pad, 0f, w - pad * 2f, h, text, 2f);
            // One line that shrinks before it clips (largest UI scale, long words-pack labels).
            Fit(item.Label, Mathf.Min(fontSize, Mathf.Max(UiScale.TypeMin, Mathf.RoundToInt(fontSize * 0.7f))));
            return item;
        }
    }
}
