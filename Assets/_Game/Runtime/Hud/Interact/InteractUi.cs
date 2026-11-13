using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Interact
{
    /// <summary>Canvas and widget helpers shared by the full map, pings, tooltips and cards (HUD tokens and fonts).</summary>
    public static class InteractUi
    {
        public static Color Navy => CMTokens.VoidNavy.Color;
        public static Color Cream => CMTokens.WarmCream.Color;
        public static Color Brass => CMTokens.ToolBrass.Color;
        public static Color Panel => CMTokens.PanelNavy.Color;

        /// <summary>
        /// A Screen Space - Camera canvas on the gameplay camera (drawn in the camera's own render like the HUD, so captures
        /// show it), with a 1920x1080 reference "Space" centred in it. No GraphicRaycaster: this item does its own hit tests,
        /// so the pause menu's EventSystem never sees these widgets.
        /// </summary>
        public static Canvas MakeCanvas(Transform parent, string name, int sortingOrder, out RectTransform root, out RectTransform space)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            Canvas c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.sortingOrder = sortingOrder;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            c.worldCamera = cam;
            c.planeDistance = cam != null ? Mathf.Max(0.42f, cam.nearClipPlane * 1.4f) : 0.42f;
            root = (RectTransform)go.transform;
            space = MenuUi.Rt(root, "Space");
            space.anchorMin = space.anchorMax = new Vector2(0.5f, 0.5f);
            space.pivot = new Vector2(0.5f, 0.5f);
            space.sizeDelta = new Vector2(HudLayout.RefW, HudLayout.RefH);
            UpdateScale(c);
            return c;
        }

        /// <summary>
        /// Owner walk 1: MakeCanvas for a match HUD layer. The space covers the safe area and every piece placed MenuUi.Place
        /// style goes into its anchor frame (HudSafeSpace, HudFrames), so it keeps its distance to its own screen edge at any
        /// screen shape and never sits under the notch. Modal panels (scoreboard, station, full map) keep MakeCanvas.
        /// </summary>
        public static Canvas MakeHudCanvas(Transform parent, string name, int sortingOrder, out RectTransform root, out RectTransform space)
        {
            Canvas c = MakeCanvas(parent, name, sortingOrder, out root, out space);
            HudSafeSpace.Attach(space, true);
            return c;
        }

        /// <summary>Canvas scale for the camera's pixel size (same rule as the HUD and pause menu).</summary>
        public static void UpdateScale(Canvas c)
        {
            if (c == null) return;
            UnityEngine.Camera cam = c.worldCamera;
            if (cam == null)
            {
                cam = UnityEngine.Camera.main;
                if (cam != null)
                {
                    c.worldCamera = cam;
                    c.planeDistance = Mathf.Max(0.42f, cam.nearClipPlane * 1.4f);
                }
            }
            float w = cam != null ? cam.pixelWidth : Screen.width, h = cam != null ? cam.pixelHeight : Screen.height;
            if (w < 1 || h < 1) return;
            float s = HudLayout.Scale(w, h);
            if (Mathf.Abs(c.scaleFactor - s) > 1e-4f) c.scaleFactor = s;
        }

        /// <summary>A rect anchored at the parent's centre, pivot top-left, at (x, y) from the centre (y up).</summary>
        public static RectTransform Floating(Transform parent, string name, float w, float h)
        {
            RectTransform rt = MenuUi.Rt(parent, name);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        /// <summary>A rect anchored and pivoted at the parent's centre (markers placed by anchoredPosition from the centre).</summary>
        public static RectTransform Centred(Transform parent, string name, float w, float h)
        {
            RectTransform rt = MenuUi.Rt(parent, name);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        /// <summary>A label that wraps inside width w (HudText labels overflow by default).</summary>
        public static Text Wrapped(Transform parent, string name, Typeface face, int size, Color c, float x, float y, float w, float h, string text)
        {
            Text t = MenuUi.Label(parent, name, face, size, c, TextAnchor.UpperLeft, x, y, w, h, text, 2f);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = 1.05f;
            return t;
        }

        /// <summary>Preferred height of a wrapped label at its width (call when its text changes, not per frame).</summary>
        public static float HeightOf(Text t)
        {
            var settings = t.GetGenerationSettings(new Vector2(t.rectTransform.rect.width, 0f));
            float scale = t.pixelsPerUnit > 0 ? t.pixelsPerUnit : 1f;
            return t.cachedTextGeneratorForLayout.GetPreferredHeight(t.text, settings) / scale;
        }

        public static Image Wash(Transform parent, string name, float alpha)
        {
            Image img = MenuUi.Rt(parent, name).gameObject.AddComponent<Image>();
            MenuUi.Stretch(img.rectTransform);
            img.color = CMTokens.WithAlpha(Navy, alpha);
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Clamps a top-left-pivot rect of size (w, h) at pos inside bounds (all in the same centre-origin space).</summary>
        public static Vector2 ClampTopLeft(Vector2 pos, float w, float h, Rect bounds, float pad = 12f)
        {
            pos.x = Mathf.Clamp(pos.x, bounds.xMin + pad, Mathf.Max(bounds.xMin + pad, bounds.xMax - w - pad));
            pos.y = Mathf.Clamp(pos.y, bounds.yMin + h + pad, Mathf.Max(bounds.yMin + h + pad, bounds.yMax - pad));
            return pos;
        }
    }

    /// <summary>
    /// The HUD tooltip: name, what it means and its key, in a chunky navy panel near the element. Strings are set only
    /// when the hovered element changes (or the device does), so hovering costs nothing per frame.
    /// </summary>
    public sealed class Tooltip
    {
        public const float W = 400f;
        RectTransform _rt;
        VectorShape _panel;
        Text _title, _body, _keyLabel, _key;
        VectorShape _keyChip;
        float _h;
        object _owner;
        string _keyText;

        public bool Visible => _rt != null && _rt.gameObject.activeSelf;
        public object Owner => _owner;
        public string Title => _title != null ? _title.text : null;
        public string Body => _body != null ? _body.text : null;

        public void Build(Transform parent)
        {
            _rt = InteractUi.Floating(parent, "Tooltip", W, 120f);
            _panel = MenuUi.Panel(_rt, "Panel", 0f, 0f, W, 120f);
            _title = MenuUi.Label(_rt, "Title", CMTokens.Display, UiScale.TypeBody, InteractUi.Cream, TextAnchor.MiddleLeft, 18f, 12f, W - 36f, 30f, "", 2f);
            _body = InteractUi.Wrapped(_rt, "Body", CMTokens.Ui, UiScale.TypeSmall, InteractUi.Cream, 18f, 46f, W - 36f, 40f, "");
            _keyLabel = MenuUi.Fit(MenuUi.Label(_rt, "Key Word", CMTokens.Ui, UiScale.TypeMin, InteractUi.Brass, TextAnchor.MiddleLeft, 18f, 0f, 60f, 26f, "KEY", 2f), 15);
            _keyChip = MenuUi.Shape(_rt, "Key Chip", ShapeKind.RoundRect, 70f, 0f, 120f, 26f, InteractUi.Navy, -1f);
            _keyChip.RimColor = InteractUi.Cream;
            _key = MenuUi.Fit(MenuUi.Label(_keyChip.transform, "Key", CMTokens.Mono, UiScale.TypeMin, InteractUi.Cream, TextAnchor.MiddleCenter, 0f, 0f, 120f, 26f, "", 0f), 15);
            _rt.gameObject.SetActive(false);
        }

        /// <summary>Shows the tooltip for owner (re-lays out only when owner or texts change).</summary>
        public void Show(object owner, string title, string body, string key, Vector2 topLeft, Rect bounds)
        {
            if (_rt == null) return;
            bool changed = !ReferenceEquals(owner, _owner) || _title.text != title || _body.text != body || _keyText != key;
            if (changed)
            {
                _owner = owner;
                HudText.Set(_title, title);
                HudText.Set(_body, body ?? "");
                _keyText = key;
                float bodyH = string.IsNullOrEmpty(body) ? 0f : InteractUi.HeightOf(_body);
                bool hasKey = !string.IsNullOrEmpty(key);
                _keyLabel.gameObject.SetActive(hasKey);
                _keyChip.gameObject.SetActive(hasKey);
                float keyY = 46f + bodyH + 10f;
                if (hasKey)
                {
                    HudText.Set(_key, key);
                    float kw = Mathf.Max(56f, 18f + 11f * key.Length);
                    MenuUi.Place(_keyLabel.rectTransform, 18f, keyY, 50f, 26f);
                    MenuUi.Place(_keyChip.rectTransform, 70f, keyY, kw, 26f);
                    MenuUi.Place(_key.rectTransform, 0f, 0f, kw, 26f);
                }
                _h = hasKey ? keyY + 26f + 16f : 46f + bodyH + 16f;
                MenuUi.Place(_body.rectTransform, 18f, 46f, W - 36f, Mathf.Max(20f, bodyH));
                MenuUi.Place(_panel.rectTransform, 0f, 0f, W, _h);
                _rt.sizeDelta = new Vector2(W, _h);
            }
            _rt.anchoredPosition = InteractUi.ClampTopLeft(topLeft, W, _h, bounds);
            if (!_rt.gameObject.activeSelf) _rt.gameObject.SetActive(true);
        }

        public void Hide(object owner = null)
        {
            if (_rt == null || (owner != null && !ReferenceEquals(owner, _owner))) return;
            _owner = null;
            if (_rt.gameObject.activeSelf) _rt.gameObject.SetActive(false);
        }

        public float Height => _h;
    }

    /// <summary>
    /// A centred info card (Kaiju card, ability card, health numbers, character card fallback): title, a brass line and up
    /// to six wrapped body lines. Modal while open (attacks are held back); closes on Escape, B, a click outside or the
    /// element clicked again.
    /// </summary>
    public sealed class InfoCard
    {
        public const int MaxLines = 6;
        public const float W = 700f;
        RectTransform _rt;
        VectorShape _panel;
        Text _title, _sub, _hint;
        readonly Text[] _lines = new Text[MaxLines];
        string _id;

        public bool IsOpen => _rt != null && _rt.gameObject.activeSelf;
        /// <summary>Which card is open ("kaiju", "ability-1", ...), null when closed.</summary>
        public string Id => IsOpen ? _id : null;
        public string TitleText => _title != null ? _title.text : null;
        public string LineText(int i) => i >= 0 && i < MaxLines && _lines[i] != null ? _lines[i].text : null;
        public RectTransform Rect => _rt;

        public void Build(RectTransform space)
        {
            _rt = MenuUi.Rt(space, "Info Card");
            _rt.anchorMin = _rt.anchorMax = new Vector2(0.5f, 0.5f);
            _rt.pivot = new Vector2(0.5f, 0.5f);
            _rt.sizeDelta = new Vector2(W, 300f);
            _panel = MenuUi.Panel(_rt, "Panel", 0f, 0f, W, 300f);
            _title = MenuUi.Fit(MenuUi.Label(_rt, "Title", CMTokens.Display, UiScale.TypeLead, InteractUi.Cream, TextAnchor.MiddleLeft, 32f, 22f, W - 64f, 46f, "", 3f), 34);
            _sub = MenuUi.Label(_rt, "Sub", CMTokens.Ui, UiScale.TypeSmall, InteractUi.Brass, TextAnchor.MiddleLeft, 32f, 70f, W - 64f, 28f, "", 2f);
            for (int i = 0; i < MaxLines; i++) _lines[i] = InteractUi.Wrapped(_rt, "Line " + i, CMTokens.Ui, UiScale.TypeSmall, InteractUi.Cream, 32f, 110f, W - 64f, 30f, "");
            _hint = MenuUi.Label(_rt, "Hint", CMTokens.Ui, UiScale.TypeMin, CMTokens.WithAlpha(InteractUi.Cream, 0.8f), TextAnchor.MiddleRight, 32f, 0f, W - 64f, 24f, "", 2f);
            _rt.gameObject.SetActive(false);
        }

        /// <summary>Opens the card; lines may be null or fewer than MaxLines. Allocation happens here, never per frame.</summary>
        public void Open(string id, string title, string sub, string[] lines, string hint)
        {
            if (_rt == null) return;
            _id = id;
            HudText.Set(_title, title ?? "");
            HudText.Set(_sub, sub ?? "");
            _sub.gameObject.SetActive(!string.IsNullOrEmpty(sub));
            float y = string.IsNullOrEmpty(sub) ? 78f : 110f;
            for (int i = 0; i < MaxLines; i++)
            {
                string s = lines != null && i < lines.Length ? lines[i] : null;
                bool on = !string.IsNullOrEmpty(s);
                _lines[i].gameObject.SetActive(on);
                if (!on) continue;
                HudText.Set(_lines[i], s);
                float h = InteractUi.HeightOf(_lines[i]);
                MenuUi.Place(_lines[i].rectTransform, 32f, y, W - 64f, h);
                y += h + 12f;
            }
            HudText.Set(_hint, hint ?? "");
            MenuUi.Place(_hint.rectTransform, 32f, y + 6f, W - 64f, 24f);
            float height = y + 52f;
            MenuUi.Place(_panel.rectTransform, 0f, 0f, W, height);
            _rt.sizeDelta = new Vector2(W, height);
            _rt.gameObject.SetActive(true);
        }

        public void Close()
        {
            if (_rt != null && _rt.gameObject.activeSelf) _rt.gameObject.SetActive(false);
            _id = null;
        }

        /// <summary>Whether a canvas-root-local point (centre origin) is inside the card.</summary>
        public bool Contains(RectTransform root, Vector2 rootLocal)
        {
            if (!IsOpen) return false;
            Vector2 c = _rt.anchoredPosition;   // centred in the space, which is centred in the root
            Vector2 half = _rt.sizeDelta * 0.5f;
            return Mathf.Abs(rootLocal.x - c.x) <= half.x && Mathf.Abs(rootLocal.y - c.y) <= half.y;
        }
    }
}
