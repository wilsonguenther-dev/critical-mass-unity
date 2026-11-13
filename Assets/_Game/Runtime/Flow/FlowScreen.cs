using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Flow
{
    /// <summary>
    /// Base of the DG-01 screens (main menu, match setup, results), built in code like the HUD and the pause menu: a
    /// Screen Space - Camera canvas above the HUD (sorting 30, under the pause menu's 40), a Void Navy wash, a 1920x1080
    /// layout space and MenuItem stickers from MenuWidgets, so keyboard (arrows, Enter), mouse (hover, click) and controller
    /// (stick or d-pad, A) share one EventSystem focus, as in the pause menu. B, Escape and Start mean Back (Escape and
    /// Start arrive through FlowDirector's PauseHandler, B here). Focus is never lost: when nothing of this screen is
    /// selected, the last focused item (or the default) is selected again, so a focus is always visible.
    /// <para>Show plays a 180 ms fade and 28 px rise (unscaled time, view only; never touches the sim). Hide is instant.</para>
    /// </summary>
    public abstract class FlowScreen : MonoBehaviour
    {
        public const float TransitionSeconds = 0.18f;
        public const float RisePx = 28f;
        /// <summary>A press that opened a screen must not also press its focused button.</summary>
        public const float SubmitGuardSeconds = 0.15f;

        public Canvas Canvas { get; private set; }
        public bool IsVisible { get; private set; }
        public FlowDirector Director;

        /// <summary>The focused item when the screen opens.</summary>
        protected MenuItem DefaultFocus;
        protected RectTransform Space { get; private set; }
        Text _hint;
        PromptStrip _hintStrip;
        CanvasGroup _group;
        RectTransform _rise;
        MenuItem _lastFocus;
        float _shownAt;
        bool _built;

        protected static Color Navy => CMTokens.VoidNavy.Color;
        protected static Color Cream => CMTokens.WarmCream.Color;
        protected static Color Brass => CMTokens.ToolBrass.Color;

        /// <summary>Builds the screen's content into the 1920x1080 space (y down from the top-left).</summary>
        protected abstract void BuildContent(RectTransform space);

        /// <summary>Back: Escape, Start, B or the screen's Back button. Never a dead end.</summary>
        public abstract void Back();

        protected virtual string HintKeys => "ENTER SELECT   ·   ESC BACK";
        protected virtual string HintPad => "A SELECT   ·   B BACK";
        protected virtual float WashAlpha => 0.55f;

        public void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            PauseMenu.EnsureEventSystem(transform);
            var go = new GameObject(GetType().Name + " Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            Canvas = go.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceCamera;
            Canvas.sortingOrder = 30;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            Canvas.worldCamera = cam;
            Canvas.planeDistance = cam != null ? Mathf.Max(0.42f, cam.nearClipPlane * 1.35f) : 0.42f;
            go.AddComponent<GraphicRaycaster>();
            _group = go.AddComponent<CanvasGroup>();
            var root = (RectTransform)go.transform;

            Image wash = MenuUi.Rt(root, "Wash").gameObject.AddComponent<Image>();
            MenuUi.Stretch(wash.rectTransform);
            wash.color = CMTokens.WithAlpha(Navy, WashAlpha);
            wash.raycastTarget = true;   // nothing behind the screen reacts to the mouse

            _rise = MenuUi.Rt(root, "Rise");
            MenuUi.Stretch(_rise);
            RectTransform space = MenuUi.Rt(_rise, "Space");
            space.anchorMin = space.anchorMax = new Vector2(0.5f, 0.5f);
            space.pivot = new Vector2(0.5f, 0.5f);
            space.sizeDelta = new Vector2(HudLayout.RefW, HudLayout.RefH);
            Space = space;
            BuildContent(space);
            _hint = MenuUi.Label(space, "Hint", CMTokens.Ui, 20, Cream, TextAnchor.MiddleCenter, 260f, 1026f, 1400f, 36f, "", 2f);
            Canvas.enabled = false;
            UpdateScale();
        }

        public virtual void Show()
        {
            EnsureBuilt();
            IsVisible = true;
            Canvas.enabled = true;
            _shownAt = Time.unscaledTime;
            _group.alpha = 0f;
            _rise.anchoredPosition = new Vector2(0f, -RisePx);
            MenuItem.SubmitBlockedUntil = Mathf.Max(MenuItem.SubmitBlockedUntil, Time.unscaledTime + SubmitGuardSeconds);
            OnShow();
            _lastFocus = null;
            Focus(DefaultFocus);
            UpdateHint();
            UpdateScale();
        }

        protected virtual void OnShow()
        {
        }

        public virtual void Hide()
        {
            if (!IsVisible) return;
            IsVisible = false;
            if (Canvas != null) Canvas.enabled = false;
            EventSystem es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null && Canvas != null &&
                es.currentSelectedGameObject.transform.IsChildOf(Canvas.transform))
                es.SetSelectedGameObject(null);
        }

        /// <summary>Selects item (null: the default focus).</summary>
        public void Focus(MenuItem item)
        {
            if (item == null || !item.IsInteractable()) item = DefaultFocus;
            if (item == null || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(null);
            item.Select();
            _lastFocus = item;
        }

        /// <summary>The focused item of this screen, or null.</summary>
        public MenuItem Focused
        {
            get
            {
                EventSystem es = EventSystem.current;
                GameObject sel = es != null ? es.currentSelectedGameObject : null;
                if (sel == null || Canvas == null || !sel.transform.IsChildOf(Canvas.transform)) return null;
                return sel.GetComponent<MenuItem>();
            }
        }

        public void UpdateScale()
        {
            if (Canvas == null) return;
            UnityEngine.Camera cam = Canvas.worldCamera;
            if (cam == null)
            {
                cam = UnityEngine.Camera.main;
                if (cam != null) Canvas.worldCamera = cam;
            }
            float w = cam != null ? cam.pixelWidth : Screen.width, h = cam != null ? cam.pixelHeight : Screen.height;
            if (w < 1 || h < 1) return;
            float s = HudLayout.Scale(w, h);
            if (Mathf.Abs(Canvas.scaleFactor - s) > 1e-4f) Canvas.scaleFactor = s;
            OnScale(s);
        }

        /// <summary>The canvas scale changed or was applied (icon tiers).</summary>
        protected virtual void OnScale(float scale)
        {
        }

        void UpdateHint()
        {
            if (_hint == null) return;
            string hint = ControlBindings.ActiveDevice == ControlDevice.Gamepad ? HintPad : HintKeys;
            HudText.Set(_hint, hint);
            // 2026-11-12: the hint's key words are drawn as glyphs (the text stays as the hidden plain line).
            if (_hintStrip == null && _hint != null)
            {
                _hintStrip = PromptStrip.Make(_hint.transform.parent, "Hint Prompts", 30f, _hint.fontSize, _hint.color);
                RectTransform hr = _hint.rectTransform, sr = _hintStrip.Rect;
                sr.anchorMin = hr.anchorMin;
                sr.anchorMax = hr.anchorMax;
                sr.pivot = hr.pivot;
                sr.sizeDelta = new Vector2(hr.sizeDelta.x, 44f);
                sr.anchoredPosition = hr.anchoredPosition;
                _hint.enabled = false;
            }
            if (_hintStrip != null) _hintStrip.SetHint(hint, ControlBindings.ActiveDevice);
        }

        protected virtual void Update()
        {
            if (!IsVisible) return;
            UpdateScale();
            UpdateHint();
            float k = Mathf.Clamp01((Time.unscaledTime - _shownAt) / TransitionSeconds);
            float e = 1f - (1f - k) * (1f - k) * (1f - k);   // ease-out cubic
            _group.alpha = e;
            _rise.anchoredPosition = new Vector2(0f, -RisePx * (1f - e));

            // Focus is always visible: put it back when the mouse clicked empty space or the item went away.
            MenuItem f = Focused;
            if (f != null && f.IsInteractable() && f.isActiveAndEnabled) _lastFocus = f;
            else Focus(_lastFocus);

            Gamepad pad = Gamepad.current;
            if (pad != null && pad.buttonEast.wasPressedThisFrame && Time.unscaledTime >= MenuItem.SubmitBlockedUntil) Back();
            else Tick();
        }

        /// <summary>Per-frame work while visible.</summary>
        protected virtual void Tick()
        {
        }

        // ---- build helpers ----

        /// <summary>A big centred Display-face button.</summary>
        protected static MenuItem Button(Transform parent, string name, float x, float y, float w, float h, string text, Action act,
            int size = 34)
        {
            MenuItem it = MenuUi.Item(parent, name, x, y, w, h, text, MenuItem.Kind.Button, size, true, CMTokens.Display);
            it.Body.Radius = 16f;
            it.Submitted = act;
            return it;
        }

        /// <summary>Dims an item, tags it COMING SOON and takes it out of navigation (and of the mouse focus).</summary>
        protected static void MarkComingSoon(MenuItem it, float w, float h)
        {
            it.interactable = false;
            it.Submitted = null;
            MenuUi.Label(it.transform, "Soon", CMTokens.Ui, 16, Brass, TextAnchor.MiddleRight, w - 210f, 0f, 190f, h, "COMING SOON", 2f);
        }

        /// <summary>
        /// A stepper line: label left, "&lt; value &gt;" right; left/right (and a click on its left or right part) change it.
        /// </summary>
        protected static MenuItem Stepper(Transform parent, string name, float x, float y, float w, float h, string label,
            Func<string> value, Action<int> step, List<Action> refreshers)
        {
            MenuItem it = MenuUi.Item(parent, name, x, y, w, h, label, MenuItem.Kind.Stepper, 26, false, CMTokens.Display);
            it.Body.Radius = 14f;
            float vx = w * 0.42f, vw = w * 0.5f;
            it.Value = MenuUi.Label(it.transform, "Value", CMTokens.Display, 26, MenuItem.IdleText, TextAnchor.MiddleCenter, vx, 0f, vw, h, "", 2f);
            MenuUi.Label(it.transform, "Left", CMTokens.Display, 28, Brass, TextAnchor.MiddleCenter, vx - 40f, 0f, 40f, h, "<", 2f);
            MenuUi.Label(it.transform, "Right", CMTokens.Display, 28, Brass, TextAnchor.MiddleCenter, vx + vw, 0f, 40f, h, ">", 2f);
            Action refresh = () => HudText.Set(it.Value, value());
            it.Stepped = d =>
            {
                step(d);
                refresh();
            };
            refreshers?.Add(refresh);
            refresh();
            return it;
        }

        /// <summary>Explicit wrapping up/down navigation through the interactable items (coming-soon lines are skipped).</summary>
        public static void ChainVertical(IList<MenuItem> items)
        {
            var live = new List<MenuItem>();
            for (int i = 0; i < items.Count; i++)
                if (items[i] != null && items[i].interactable) live.Add(items[i]);
            for (int i = 0; i < live.Count; i++)
            {
                live[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = live[(i + live.Count - 1) % live.Count],
                    selectOnDown = live[(i + 1) % live.Count],
                };
            }
            for (int i = 0; i < items.Count; i++)
                if (items[i] != null && !items[i].interactable) items[i].navigation = new Navigation { mode = Navigation.Mode.None };
        }
    }
}
