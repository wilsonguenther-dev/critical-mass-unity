using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>The mascot's expressions; each has a portrait image by name (MascotGuide.PortraitResource).</summary>
    public enum MascotReaction { Neutral, Cheer, Wince, Point, Surprised }

    /// <summary>
    /// The mascot's words: a line id resolves through Lookup (the words-pack loader sets it when it exists; ids are
    /// "trial.&lt;step id&gt;", "mascot.cheer.0" and so on), then through fallbacks registered in code, then to the id itself.
    /// </summary>
    public static class MascotLines
    {
        /// <summary>The words pack: returns the text of an id, or null/"" when it has none.</summary>
        public static Func<string, string> Lookup;

        static readonly Dictionary<string, string> Fallbacks = new Dictionary<string, string>
        {
            { "mascot.hello", "I'm " + MascotGuide.MascotName + ". I'll keep this short." },
            { "mascot.cheer.0", "Nice. Textbook." },
            { "mascot.cheer.1", "Look at you go." },
            { "mascot.cheer.2", "Easy. Next one." },
            { "mascot.cheer.3", "That'll do." },
            { "mascot.wince", "Ouch. Shake it off, you're back in a moment." },
            { "mascot.back", "Missed me?" },
        };

        /// <summary>Adds or replaces a built-in fallback line.</summary>
        public static void Register(string id, string text)
        {
            if (!string.IsNullOrEmpty(id) && text != null) Fallbacks[id] = text;
        }

        public static string Resolve(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            if (Lookup != null)
            {
                string s = Lookup(id);
                if (!string.IsNullOrEmpty(s)) return s;
            }
            return Fallbacks.TryGetValue(id, out string f) ? f : id;
        }

        public const int CheerCount = 4;
        static readonly string[] CheerIds = { "mascot.cheer.0", "mascot.cheer.1", "mascot.cheer.2", "mascot.cheer.3" };
        public static string CheerId(int n) => CheerIds[((n % CheerCount) + CheerCount) % CheerCount];
    }

    /// <summary>
    /// The mascot guide: a baby Kaiju hatchling (working name in MascotName) who pops in at a left screen corner in a
    /// chunky speech-bubble panel with a portrait and says one line at a time. Reusable by the guided match, the loading
    /// screen, results and tips: Say(lineId), SayText, React(kind), PointToward, Hide, Dismiss and BringBack. View only:
    /// it reads nothing from the sim and writes nothing to it.
    /// <para>Placement: docked at the lower-left (above the player card) or, when the player's screen position would fall
    /// under it (AvoidScreenPoint), the upper-left; never the centre of the screen. Dismissed, it shrinks to a portrait
    /// chip; G on the keyboard or D-pad Up on a controller toggles it (AcceptsToggle). Transitions are under 250 ms and use
    /// unscaled time.</para>
    /// <para>Portraits: Resources/UI/Mascot/mascot-&lt;expression&gt;@2x (or @4x above 1440p); the sources live in
    /// Assets/_Game/Art/UI/Mascot (brief: Obsidian unity-port/briefs/mascot.md). Until they exist the HUD's hatchling icon
    /// stands in, and the expression is carried by pose (hop, squash, tilt).</para>
    /// </summary>
    public sealed class MascotGuide : MonoBehaviour
    {
        /// <summary>The mascot's name, in one place (a placeholder the owner can change).</summary>
        public const string MascotName = "Hatch";

        public const string PortraitFolder = "UI/Mascot/";
        public static readonly string[] Expressions = { "neutral", "happy", "wince", "point", "surprised" };

        /// <summary>The mascot on screen (one per game).</summary>
        public static MascotGuide Instance { get; private set; }

        /// <summary>Raised when the player dismisses (false) or brings back (true) the mascot.</summary>
        public static event Action<bool> Toggled;

        public bool IsOpen { get; private set; }
        public bool IsDismissed { get; private set; }
        /// <summary>The keyboard G / D-pad Up toggle is live (the owner of the mascot turns it off on menus).</summary>
        public bool AcceptsToggle = true;
        public MascotReaction Reaction { get; private set; }
        public string Line => _line != null ? _line.text : string.Empty;
        public string Hint => _hint != null ? _hint.text : string.Empty;
        public Canvas Canvas { get; private set; }

        // reference layout (1920x1080, top-left origin)
        public const float PanelW = 620f, PanelH = 176f, Margin = 24f;
        public const float LowerY = 1080f - 24f - 132f - 24f - PanelH;   // above the HUD player card
        public const float UpperY = 150f;                               // under the top edge, left of the match cluster
        const float Portrait = 148f, ChipSize = 84f;
        const float PopSeconds = 0.18f, ReactSeconds = 0.22f, SlideSeconds = 0.2f;

        RectTransform _space, _panel, _portraitPivot, _pointPivot, _chip;
        CanvasGroup _group;
        RawImage _portrait, _chipPortrait;
        Text _name, _line, _hint, _chipKey;
        VectorShape _bubble, _pointArrow;
        float _popT = 1f, _reactT = 1f, _slideT = 1f;
        float _fromY, _toY = LowerY;
        bool _upper;
        int _tier = -1;
        bool _hasArtLooked;
        readonly Texture2D[] _art = new Texture2D[5];
        Vector2 _pointDir;
        bool _pointing;

        /// <summary>The mascot on host (created on first use).</summary>
        public static MascotGuide Ensure(GameObject host)
        {
            if (Instance != null) return Instance;
            if (host == null) return null;
            MascotGuide m = host.GetComponent<MascotGuide>();
            if (m == null) m = host.AddComponent<MascotGuide>();
            return m;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            Build();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (Canvas != null) Destroy(Canvas.gameObject);
        }

        // ---------------------------------------------------------------- public API

        /// <summary>Says the line with this id (words pack, else fallback) and shows the mascot.</summary>
        public void Say(string lineId) => SayText(MascotLines.Resolve(lineId));

        /// <summary>Says ready-made text (glyphs already filled in) and shows the mascot.</summary>
        public void SayText(string text)
        {
            Show();
            if (_line.text != text)
            {
                _line.text = text;
                if (!IsDismissed) _popT = 0.5f;   // a short re-pop so a new line is noticed
            }
        }

        /// <summary>The small key line under the speech ("" hides it).</summary>
        public void SetHint(string text)
        {
            HudText.Set(_hint, text ?? string.Empty);
        }

        public void React(MascotReaction kind)
        {
            Reaction = kind;
            _reactT = 0f;
            _pointing = kind == MascotReaction.Point && _pointDir.sqrMagnitude > 0f;
            ApplyPortrait();
        }

        /// <summary>
        /// Points the mascot at a screen-space direction (pixels, y up). From Neutral it switches to the Point expression;
        /// a running Cheer, Wince or Surprised keeps its face and the direction is used once it points again. Cheap enough
        /// to call every frame.
        /// </summary>
        public void PointToward(Vector2 screenDirection)
        {
            _pointDir = screenDirection.sqrMagnitude > 1e-6f ? screenDirection.normalized : Vector2.zero;
            if (Reaction == MascotReaction.Neutral && _pointDir.sqrMagnitude > 0f) React(MascotReaction.Point);
            _pointing = Reaction == MascotReaction.Point && _pointDir.sqrMagnitude > 0f;
        }

        public void StopPointing()
        {
            _pointing = false;
            if (Reaction == MascotReaction.Point) React(MascotReaction.Neutral);
        }

        public void Show()
        {
            if (IsOpen) return;
            IsOpen = true;
            Canvas.enabled = true;
            _popT = 0f;
        }

        /// <summary>Takes the mascot off screen entirely (its owner is done with it).</summary>
        public void Hide()
        {
            IsOpen = false;
            if (Canvas != null) Canvas.enabled = false;
        }

        /// <summary>The player sends the mascot away: it shrinks to a portrait chip in the corner.</summary>
        public void Dismiss()
        {
            if (IsDismissed) return;
            IsDismissed = true;
            Toggled?.Invoke(false);
        }

        /// <summary>The player calls the mascot back.</summary>
        public void BringBack()
        {
            if (!IsDismissed) return;
            IsDismissed = false;
            _popT = 0f;
            Toggled?.Invoke(true);
        }

        /// <summary>
        /// Moves the panel to the other left corner when this screen point (pixels, y up; the player) would sit under it,
        /// so the mascot never covers the player.
        /// </summary>
        public void AvoidScreenPoint(Vector2 px)
        {
            if (Canvas == null || Canvas.worldCamera == null) return;
            float s = Canvas.scaleFactor;
            float sh = Canvas.worldCamera.pixelHeight;
            // Panel rect in pixels (y up) at the current dock, padded by 60 reference px.
            float pad = 60f * s;
            float x0 = Margin * s - pad, x1 = (Margin + PanelW) * s + pad;
            float yTopDown = (_upper ? UpperY : LowerY);
            float y1 = sh - yTopDown * s + pad, y0 = sh - (yTopDown + PanelH) * s - pad;
            bool under = px.x >= x0 && px.x <= x1 && px.y >= y0 && px.y <= y1;
            if (!under) return;
            _upper = !_upper;
            _fromY = _toY;
            _toY = _upper ? UpperY : LowerY;
            _slideT = 0f;
        }

        // ---------------------------------------------------------------- build

        void Build()
        {
            var go = new GameObject("Mascot Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            Canvas = go.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceCamera;
            Canvas.sortingOrder = 25;   // over the HUD (10) and title (20), under the pause menu (40)
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            Canvas.worldCamera = cam;
            Canvas.planeDistance = cam != null ? Mathf.Max(0.42f, cam.nearClipPlane * 1.35f) : 0.42f;
            _group = go.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;

            _space = Rt(go.transform, "Space");
            _space.anchorMin = _space.anchorMax = new Vector2(0.5f, 0.5f);
            _space.pivot = new Vector2(0.5f, 0.5f);
            _space.sizeDelta = new Vector2(HudLayout.RefW, HudLayout.RefH);

            _panel = Rt(_space, "Mascot Panel");
            Place(_panel, Margin, LowerY, PanelW, PanelH);

            // Speech bubble: chunky sticker panel to the right of the portrait, with a tail toward it.
            _bubble = Shape(_panel, "Bubble", ShapeKind.RoundRect, Portrait * 0.62f, 14f, PanelW - Portrait * 0.62f, PanelH - 28f,
                CMTokens.PanelNavy.Color, 22f);
            _bubble.OutlinePx = 4f;
            _bubble.RimPx = 3f;
            VectorShape tail = Shape(_panel, "Tail", ShapeKind.TriangleRight, Portrait * 0.62f - 16f, PanelH * 0.5f - 14f, 20f, 28f,
                CMTokens.PanelNavy.Color, 0f);
            tail.transform.localRotation = Quaternion.Euler(0, 0, 180f);
            tail.RimPx = 0f;

            float tx = Portrait + 18f, tw = PanelW - Portrait - 38f;
            _name = HudText.Make(_bubble.transform.parent, "Name", CMTokens.Display, UiScale.TypeBody, CMTokens.ToolBrass.Color, TextAnchor.MiddleLeft, 3f);
            Place(_name.rectTransform, tx, 24f, tw, 30f);
            _name.text = MascotName.ToUpperInvariant();
            _line = CriticalMass.Runtime.Menu.MenuUi.Fit(HudText.Make(_panel, "Line", CMTokens.Ui, UiScale.TypeButton, CMTokens.WarmCream.Color, TextAnchor.UpperLeft, 2.5f), 26);
            Place(_line.rectTransform, tx, 56f, tw, 74f);
            _line.horizontalOverflow = HorizontalWrapMode.Wrap;
            _line.verticalOverflow = VerticalWrapMode.Truncate;
            _line.lineSpacing = 0.95f;
            _hint = HudText.Make(_panel, "Hint", CMTokens.Mono, UiScale.TypeMin, CMTokens.WithAlpha(CMTokens.WarmCream.Color, 0.85f), TextAnchor.MiddleLeft, 2f);
            Place(_hint.rectTransform, tx, PanelH - 46f, tw, 22f);

            // Portrait medallion: Void Navy disc, Warm Cream rim, the face on top. The pivot carries pose (hop, squash, tilt).
            _portraitPivot = Rt(_panel, "Portrait Pivot");
            Place(_portraitPivot, 0f, (PanelH - Portrait) * 0.5f, Portrait, Portrait);
            VectorShape disc = Shape(_portraitPivot, "Disc", ShapeKind.Circle, 0f, 0f, Portrait, Portrait, CMTokens.VoidNavy.Color, 0f);
            disc.OutlinePx = 4f;
            disc.RimPx = 3f;
            disc.RimColor = CMTokens.EggSeam.Color;
            _portrait = Raw(_portraitPivot, "Face", 10f, 10f, Portrait - 20f, Portrait - 20f);

            // Point arrow: orbits the portrait toward the world arrow while pointing.
            _pointPivot = Rt(_portraitPivot, "Point Pivot");
            Place(_pointPivot, Portrait * 0.5f, Portrait * 0.5f, 0f, 0f);
            _pointArrow = Shape(_pointPivot, "Point", ShapeKind.TriangleRight, 0f, 0f, 26f, 30f, CMTokens.ToolBrass.Color, 0f);
            _pointArrow.RimPx = 0f;
            RectTransform pr = _pointArrow.rectTransform;
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
            pr.anchoredPosition = new Vector2(Portrait * 0.5f + 14f, 0f);
            _pointPivot.gameObject.SetActive(false);

            // Dismissed chip: the portrait alone, small, with its key.
            _chip = Rt(_space, "Mascot Chip");
            Place(_chip, Margin, LowerY + PanelH - ChipSize, ChipSize, ChipSize);
            VectorShape chipDisc = Shape(_chip, "Disc", ShapeKind.Circle, 0f, 0f, ChipSize, ChipSize, CMTokens.VoidNavy.Color, 0f);
            chipDisc.RimColor = CMTokens.EggSeam.Color;
            _chipPortrait = Raw(_chip, "Face", 8f, 8f, ChipSize - 16f, ChipSize - 16f);
            _chipKey = HudText.Make(_chip, "Key", CMTokens.Mono, UiScale.TypeMin, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter, 2.5f);
            Place(_chipKey.rectTransform, -8f, ChipSize - 4f, ChipSize + 16f, 22f);
            _chip.gameObject.SetActive(false);

            Canvas.enabled = false;
            UpdateScale();
            ApplyPortrait();
        }

        static RectTransform Rt(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>(x, y) from the parent's top-left, y down, size (w, h); pivot centre.</summary>
        static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x + w * 0.5f, -(y + h * 0.5f));
        }

        static VectorShape Shape(Transform parent, string name, ShapeKind kind, float x, float y, float w, float h, Color fill, float radius)
        {
            RectTransform rt = Rt(parent, name);
            Place(rt, x, y, w, h);
            VectorShape s = rt.gameObject.AddComponent<VectorShape>();
            s.Kind = kind;
            s.Radius = radius;
            s.FillColor = fill;
            s.raycastTarget = false;
            return s;
        }

        static RawImage Raw(Transform parent, string name, float x, float y, float w, float h)
        {
            RectTransform rt = Rt(parent, name);
            Place(rt, x, y, w, h);
            RawImage img = rt.gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
            return img;
        }

        // ---------------------------------------------------------------- portraits

        /// <summary>Resources path of an expression's portrait at a tier (2 or 4).</summary>
        public static string PortraitResource(MascotReaction r, int tier) =>
            PortraitFolder + "mascot-" + Expressions[(int)r] + (tier >= 4 ? "@4x" : "@2x");

        void ApplyPortrait()
        {
            if (_portrait == null) return;
            int tier = Canvas != null && Canvas.scaleFactor > 1.5f ? 4 : 2;
            if (tier != _tier || !_hasArtLooked)
            {
                _tier = tier;
                _hasArtLooked = true;
                for (int i = 0; i < _art.Length; i++)
                {
                    _art[i] = Resources.Load<Texture2D>(PortraitResource((MascotReaction)i, tier));
                    if (_art[i] == null && tier == 4) _art[i] = Resources.Load<Texture2D>(PortraitResource((MascotReaction)i, 2));
                }
            }
            Texture2D t = _art[(int)Reaction];
            if (t == null) t = _art[(int)MascotReaction.Neutral];
            if (t == null) t = UiIcons.Get(UiIcons.Hatchling, tier);   // stand-in until the mascot art lands
            _portrait.texture = t;
            _portrait.enabled = t != null;
            _chipPortrait.texture = t;
            _chipPortrait.enabled = t != null;
        }

        /// <summary>True when the dedicated portrait art is present (else the hatchling icon stands in).</summary>
        public bool HasOwnArt => _art[(int)MascotReaction.Neutral] != null;

        // ---------------------------------------------------------------- frame

        void UpdateScale()
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
            if (Mathf.Abs(Canvas.scaleFactor - s) > 1e-4f)
            {
                Canvas.scaleFactor = s;
                ApplyPortrait();
            }
            // The left docks hug the screen's left edge at any aspect: offset the space so x = 0 is the screen edge.
            float spaceLeft = -(w / s - HudLayout.RefW) * 0.5f;
            float spaceTop = -(h / s - HudLayout.RefH) * 0.5f;
            _space.anchoredPosition = new Vector2(spaceLeft, -spaceTop);
        }

        void Update()
        {
            if (!IsOpen) return;
            UpdateScale();
            if (AcceptsToggle && TogglePressed())
            {
                if (IsDismissed) BringBack();
                else Dismiss();
            }
            float dt = Time.unscaledDeltaTime;
            _popT = Mathf.Min(1f, _popT + dt / PopSeconds);
            _reactT = Mathf.Min(1f, _reactT + dt / ReactSeconds);
            _slideT = Mathf.Min(1f, _slideT + dt / SlideSeconds);

            bool full = !IsDismissed;
            if (_panel.gameObject.activeSelf != full) _panel.gameObject.SetActive(full);
            if (_chip.gameObject.activeSelf == full) _chip.gameObject.SetActive(!full);

            float ease = 1f - (1f - _popT) * (1f - _popT);
            _group.alpha = Mathf.Lerp(0f, 1f, Mathf.Clamp01(_popT * 2f));
            float y = Mathf.Lerp(_fromY, _toY, 1f - (1f - _slideT) * (1f - _slideT));
            if (_slideT >= 1f) _fromY = _toY;
            Place(_panel, Margin - (1f - ease) * 40f, y, PanelW, PanelH);
            _panel.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, ease);
            Place(_chip, Margin, y + PanelH - ChipSize, ChipSize, ChipSize);
            HudText.Set(_chipKey, ControlDeviceKey());

            // Pose: the expression lives in the portrait art; the pose sells it on the stand-in too.
            float k = 1f - _reactT;
            Vector2 off = Vector2.zero;
            float rot = 0f;
            Vector3 scl = Vector3.one;
            switch (Reaction)
            {
                case MascotReaction.Cheer:
                    off.y = Mathf.Sin(_reactT * Mathf.PI) * 14f;
                    scl = new Vector3(1f - 0.05f * k, 1f + 0.06f * k, 1f);
                    break;
                case MascotReaction.Wince:
                    off.x = Mathf.Sin(_reactT * Mathf.PI * 4f) * 6f * k;
                    scl = new Vector3(1.06f, 0.92f, 1f);
                    break;
                case MascotReaction.Surprised:
                    scl = Vector3.one * (1f + 0.08f * Mathf.Sin(_reactT * Mathf.PI));
                    break;
                case MascotReaction.Point:
                    if (_pointing) rot = Mathf.Clamp(_pointDir.x, -1f, 1f) * -8f;
                    break;
            }
            _portraitPivot.anchoredPosition = new Vector2(Portrait * 0.5f + off.x, -(PanelH * 0.5f) + off.y);
            _portraitPivot.localRotation = Quaternion.Euler(0, 0, rot);
            _portraitPivot.localScale = scl;
            _portrait.color = Reaction == MascotReaction.Wince ? new Color(0.85f, 0.85f, 0.9f, 1f) : Color.white;

            bool showPoint = _pointing && Reaction == MascotReaction.Point;
            if (_pointPivot.gameObject.activeSelf != showPoint) _pointPivot.gameObject.SetActive(showPoint);
            if (showPoint) _pointPivot.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(_pointDir.y, _pointDir.x) * Mathf.Rad2Deg);
        }

        static bool TogglePressed()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.gKey.wasPressedThisFrame) return true;
            Gamepad pad = Gamepad.current;
            return pad != null && pad.dpad.up.wasPressedThisFrame;
        }

        /// <summary>The toggle key's glyph on the device used last.</summary>
        public static string ControlDeviceKey() =>
            CriticalMass.Runtime.Input.ControlBindings.ActiveDevice == CriticalMass.Runtime.Input.ControlDevice.Gamepad ? "D-PAD UP" : "G";
    }
}
