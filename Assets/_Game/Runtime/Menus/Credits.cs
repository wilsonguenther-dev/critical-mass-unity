using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>One credit: a role and a first name.</summary>
    public readonly struct CreditLine
    {
        public readonly string Role;
        public readonly string Name;

        public CreditLine(string role, string name)
        {
            Role = role;
            Name = name;
        }
    }

    /// <summary>One sound library from the sound credits file: its heading, licence, page and the files used from it.</summary>
    public sealed class SoundPack
    {
        public string Name;
        public string Licence;
        public string Page;
        public readonly List<string> Files = new List<string>();
    }

    /// <summary>
    /// What the credits say. People are only those named in this repo's own docs, by first name and the role the docs give
    /// them (README/MAX-ITEMS: Max's mechanics work list; ARCHITECTURE/PARITY: Jeisil's Visual Asset Bible; MAX-ITEMS: the
    /// owner, Wilson; PARITY: the team raters Wilson, Jeisil, Aidan and Max), plus Travis as the game's creator. Tools are
    /// described, never named. Sound credits are read from the sound lane's CREDITS.md when it is present.
    /// </summary>
    public static class CreditsContent
    {
        public static readonly CreditLine Creator = new CreditLine("Created by", "Travis");

        public static readonly CreditLine[] Team =
        {
            new CreditLine("Project owner", "Wilson"),
            new CreditLine("Visual asset bible and art direction", "Jeisil"),
            new CreditLine("Game mechanics", "Max"),
            new CreditLine("Play review", "Aidan"),
        };

        /// <summary>The tools, described generically (no technology or vendor names on screen).</summary>
        public static readonly string[] Tools =
        {
            "A commercial game engine, with a toon look and outlines written for this game",
            "An open-source 3D suite: every model, rig and animation built by script",
            "A local, open-licence voice model for the narration",
            "Three open-licence typefaces (SIL Open Font License 1.1)",
            "Sound effects from public-domain (CC0) libraries, listed on the right",
        };

        public const string SoundIntro = "Public domain (CC0 1.0). Credit is not required; we give it anyway.";
        public const string NoSound = "No sound library ships in this build.";

        /// <summary>Resources names tried for the sound credits (a CREDITS.md inside any Resources folder imports as text).</summary>
        public static readonly string[] SoundCreditsResources = { "CREDITS", "Audio/CREDITS" };
        /// <summary>The sound lane's credits file (feat/combat-feel): read directly in the editor.</summary>
        public const string SoundCreditsAssetPath = "Assets/_Game/Audio/CREDITS.md";
        public const int MaxPacksShown = 6;

        static readonly Regex Backticked = new Regex("`([^`]+)`");

        /// <summary>The sound credits markdown, or null when none ships.</summary>
        public static string ReadSoundCredits()
        {
            for (int i = 0; i < SoundCreditsResources.Length; i++)
            {
                TextAsset ta = Resources.Load<TextAsset>(SoundCreditsResources[i]);
                if (ta != null) return ta.text;
            }
#if UNITY_EDITOR
            string path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), SoundCreditsAssetPath);
            if (System.IO.File.Exists(path)) return System.IO.File.ReadAllText(path);
#endif
            return null;
        }

        /// <summary>
        /// Parses CREDITS.md: every "## " section that has a "- Files (n): `a.ogg`, ..." line is a pack; its "- Page:" and
        /// "- Licence:" (or "- License:") lines fill the rest. Sections without files (introductions) are skipped.
        /// </summary>
        public static List<SoundPack> ParseSoundCredits(string md)
        {
            var packs = new List<SoundPack>();
            if (string.IsNullOrEmpty(md)) return packs;
            SoundPack cur = null;
            string[] lines = md.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i].Trim();
                if (l.StartsWith("## ", StringComparison.Ordinal))
                {
                    if (cur != null && cur.Files.Count > 0) packs.Add(cur);
                    cur = new SoundPack { Name = l.Substring(3).Trim() };
                    continue;
                }
                if (cur == null || !l.StartsWith("- ", StringComparison.Ordinal)) continue;
                string body = l.Substring(2);
                if (StartsWithKey(body, "Page:")) cur.Page = ShortUrl(body.Substring(5).Trim());
                else if (StartsWithKey(body, "Licence:") || StartsWithKey(body, "License:"))
                {
                    string v = body.Substring(8).Trim();
                    int p = v.IndexOf(" (", StringComparison.Ordinal);
                    cur.Licence = p > 0 ? v.Substring(0, p).Trim() : v;
                }
                else if (StartsWithKey(body, "Files"))
                {
                    foreach (Match m in Backticked.Matches(body)) cur.Files.Add(m.Groups[1].Value.Trim());
                }
            }
            if (cur != null && cur.Files.Count > 0) packs.Add(cur);
            return packs;
        }

        static bool StartsWithKey(string s, string key) => s.StartsWith(key, StringComparison.OrdinalIgnoreCase);

        /// <summary>"https://www.example.org/a/b" -> "example.org/a/b".</summary>
        public static string ShortUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;
            string s = url.Trim();
            if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) s = s.Substring(8);
            else if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) s = s.Substring(7);
            if (s.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) s = s.Substring(4);
            return s.TrimEnd('/');
        }

        /// <summary>"31 SOUNDS · CC0 1.0" style detail line for a pack.</summary>
        public static string PackDetail(SoundPack p)
        {
            string n = p.Files.Count.ToString(CultureInfo.InvariantCulture) + (p.Files.Count == 1 ? " sound" : " sounds");
            return string.IsNullOrEmpty(p.Licence) ? n : n + "   ·   " + p.Licence;
        }
    }

    /// <summary>
    /// DG-29 credits screen: the creator, the team (roles and first names from the repo's docs), the tools described
    /// generically, and the public-domain sound libraries read from CREDITS.md. Three chunky panels on a Void Navy wash, one
    /// focused BACK button; Esc, B, Enter, A or a click on BACK closes it. Keyboard, mouse and controller share the
    /// EventSystem focus, like the pause menu. Opens with a 0.2 s fade.
    /// <para>Public API for the game-flow item (DG-01): <see cref="Show"/>, <see cref="Hide"/>, <see cref="IsOpen"/>,
    /// <see cref="Opened"/>, <see cref="Closed"/>.</para>
    /// </summary>
    public sealed class Credits : MonoBehaviour
    {
        public static Credits Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._open;
        public static event Action Opened;
        public static event Action Closed;

        public Canvas Canvas { get; private set; }
        /// <summary>The packs on screen (after the last Show).</summary>
        public List<SoundPack> Packs { get; private set; } = new List<SoundPack>();

        const float FadeSeconds = 0.2f;
        const float ColY = 176f, ColH = 716f, ColW = 552f, ColGap = 36f, ColX0 = 96f;

        bool _built, _open;
        CanvasGroup _group;
        float _t;
        RectTransform _soundCol;
        readonly List<GameObject> _soundRows = new List<GameObject>();
        MenuItem _back;
        Text _hint;
        ControlDevice _hintDevice = (ControlDevice)(-1);

        void Awake()
        {
            if (Instance == null) Instance = this;
        }

        void OnDestroy()
        {
            if (_open) Close(false);
            if (Instance == this) Instance = null;
        }

        // ---- static API ----

        /// <summary>Opens the credits. False when no game is loaded.</summary>
        public static bool Show()
        {
            Credits c = Instance != null ? Instance : StoryAttractCreditsInstaller.AttachCredits();
            if (c == null) return false;
            c.Open();
            return true;
        }

        public static void Hide()
        {
            if (Instance != null && Instance._open) Instance.Close(true);
        }

        // ---- open, close ----

        void Open()
        {
            if (_open) return;
            if (StoryIntro.IsPlaying) StoryIntro.Skip();
            AttractMode.End();
            Build();
            RefreshSound();
            _open = true;
            _t = 0f;
            _group.alpha = 0f;
            OverlayGuard.Begin(gameObject, ConsumePause);
            Canvas.enabled = true;
            MenuItem.SubmitBlockedUntil = Time.unscaledTime + 0.15f;   // the key that opened the credits must not press BACK
            Select(_back);
            Opened?.Invoke();
        }

        bool ConsumePause()
        {
            Hide();
            return true;
        }

        void Close(bool raise)
        {
            _open = false;
            if (Canvas != null) Canvas.enabled = false;
            if (EventSystem.current != null && _back != null && EventSystem.current.currentSelectedGameObject == _back.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
            OverlayGuard.End();
            if (raise) Closed?.Invoke();
        }

        void Update()
        {
            OverlayGuard.Tick();
            if (!_open) return;
            UpdateScale();
            UpdateHint();
            _t += Time.unscaledDeltaTime;
            _group.alpha = Mathf.Clamp01(_t / FadeSeconds);
            Gamepad pad = Gamepad.current;
            Keyboard kb = Keyboard.current;
            // Escape normally arrives as the Pause press (ConsumePause); reading the key too covers a rebound Pause.
            if ((pad != null && pad.buttonEast.wasPressedThisFrame) || (kb != null && kb.escapeKey.wasPressedThisFrame))
            {
                Hide();
                return;
            }
            EventSystem es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject == null && NavigationPressed()) Select(_back);
        }

        static bool NavigationPressed()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame ||
                               kb.tabKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) return true;
            Gamepad pad = Gamepad.current;
            return pad != null && (pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame ||
                                   pad.buttonSouth.wasPressedThisFrame || pad.leftStick.ReadValue().sqrMagnitude > 0.25f);
        }

        static void Select(Selectable s)
        {
            if (s == null || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(null);
            s.Select();
        }

        void UpdateHint()
        {
            ControlBindings.Sample();
            ControlDevice d = ControlBindings.ActiveDevice;
            if (d == _hintDevice) return;
            _hintDevice = d;
            HudText.Set(_hint, d == ControlDevice.Gamepad ? "A OR B   BACK" : "ENTER OR ESC   BACK");
        }

        void UpdateScale()
        {
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
        }

        // ---- build ----

        static Color Cream => CMTokens.WarmCream.Color;
        static Color Brass => CMTokens.ToolBrass.Color;

        void Build()
        {
            if (_built) return;
            _built = true;
            PauseMenu.EnsureEventSystem(transform);

            var go = new GameObject("Credits Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            Canvas = go.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceCamera;
            Canvas.sortingOrder = 50;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            Canvas.worldCamera = cam;
            Canvas.planeDistance = cam != null ? Mathf.Max(0.38f, cam.nearClipPlane * 1.25f) : 0.38f;
            go.AddComponent<GraphicRaycaster>();
            _group = go.AddComponent<CanvasGroup>();
            var root = (RectTransform)go.transform;

            // Void Navy wash that also catches clicks, so nothing behind reacts.
            Image wash = MenuUi.Rt(root, "Wash").gameObject.AddComponent<Image>();
            MenuUi.Stretch(wash.rectTransform);
            wash.color = CMTokens.WithAlpha(CMTokens.VoidNavy.Color, 0.9f);
            wash.raycastTarget = true;

            RectTransform space = MenuUi.Rt(root, "Space");
            space.anchorMin = space.anchorMax = new Vector2(0.5f, 0.5f);
            space.pivot = new Vector2(0.5f, 0.5f);
            space.sizeDelta = new Vector2(HudLayout.RefW, HudLayout.RefH);

            MenuUi.Label(space, "Title", CMTokens.Display, 64, Cream, TextAnchor.MiddleCenter, 460f, 56f, 1000f, 90f, "CREDITS", 4f);

            RectTransform team = Column(space, 0, "TEAM");
            RectTransform tools = Column(space, 1, "MADE WITH");
            _soundCol = Column(space, 2, "SOUND");

            // Team: the creator large, then each member's first name and role.
            MenuUi.Label(team, "Creator Role", CMTokens.Ui, 22, Brass, TextAnchor.MiddleLeft, 36f, 96f, ColW - 72f, 30f,
                CreditsContent.Creator.Role.ToUpperInvariant(), 2f);
            MenuUi.Label(team, "Creator", CMTokens.Display, 52, Cream, TextAnchor.MiddleLeft, 36f, 128f, ColW - 72f, 70f,
                CreditsContent.Creator.Name.ToUpperInvariant(), CMTokens.OutlinePx);
            for (int i = 0; i < CreditsContent.Team.Length; i++)
            {
                float y = 236f + i * 112f;
                CreditLine c = CreditsContent.Team[i];
                MenuUi.Label(team, "Name " + i, CMTokens.Display, 32, Cream, TextAnchor.MiddleLeft, 36f, y, ColW - 72f, 44f,
                    c.Name.ToUpperInvariant(), CMTokens.OutlinePx);
                Text role = MenuUi.Label(team, "Role " + i, CMTokens.Ui, 22, CMTokens.WithAlpha(Cream, 0.85f), TextAnchor.UpperLeft,
                    36f, y + 46f, ColW - 72f, 56f, c.Role, 2f);
                role.horizontalOverflow = HorizontalWrapMode.Wrap;
            }

            // Tools, described.
            for (int i = 0; i < CreditsContent.Tools.Length; i++)
            {
                float y = 104f + i * 116f;
                MenuUi.Shape(tools, "Dot " + i, ShapeKind.Diamond, 36f, y + 8f, 18f, 18f, Brass).RimPx = 0f;
                Text t = MenuUi.Label(tools, "Tool " + i, CMTokens.Ui, 24, Cream, TextAnchor.UpperLeft, 68f, y, ColW - 104f, 104f,
                    CreditsContent.Tools[i], 2f);
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
            }

            Text intro = MenuUi.Label(_soundCol, "Sound Intro", CMTokens.Ui, 20, CMTokens.WithAlpha(Cream, 0.85f), TextAnchor.UpperLeft,
                36f, 96f, ColW - 72f, 60f, CreditsContent.SoundIntro, 2f);
            intro.horizontalOverflow = HorizontalWrapMode.Wrap;

            _back = MenuUi.Item(space, "Back", 760f, 924f, 400f, 80f, "BACK", MenuItem.Kind.Button, 34, true, CMTokens.Display);
            _back.Body.Radius = 16f;
            _back.Submitted = Hide;
            var nav = new Navigation { mode = Navigation.Mode.None };
            _back.navigation = nav;
            _hint = MenuUi.Label(space, "Hint", CMTokens.Ui, 20, Cream, TextAnchor.MiddleCenter, 260f, 1024f, 1400f, 36f, "", 2f);

            Canvas.enabled = false;
            UpdateScale();
        }

        static RectTransform Column(RectTransform space, int i, string heading)
        {
            float x = ColX0 + i * (ColW + ColGap);
            VectorShape panel = MenuUi.Panel(space, heading + " Panel", x, ColY, ColW, ColH);
            RectTransform col = panel.rectTransform;
            MenuUi.Label(col, "Heading", CMTokens.Display, 30, Brass, TextAnchor.MiddleLeft, 36f, 28f, ColW - 72f, 48f, heading,
                CMTokens.OutlinePx);
            return col;
        }

        /// <summary>Re-reads CREDITS.md and rebuilds the sound column (on each Show, never during a match).</summary>
        void RefreshSound()
        {
            for (int i = 0; i < _soundRows.Count; i++) Destroy(_soundRows[i]);
            _soundRows.Clear();
            Packs = CreditsContent.ParseSoundCredits(CreditsContent.ReadSoundCredits());
            if (Packs.Count == 0)
            {
                Text none = MenuUi.Label(_soundCol, "No Sound", CMTokens.Ui, 22, Cream, TextAnchor.UpperLeft, 36f, 176f, ColW - 72f, 60f,
                    CreditsContent.NoSound, 2f);
                none.horizontalOverflow = HorizontalWrapMode.Wrap;
                _soundRows.Add(none.gameObject);
                return;
            }
            int n = Mathf.Min(Packs.Count, CreditsContent.MaxPacksShown);
            for (int i = 0; i < n; i++)
            {
                SoundPack p = Packs[i];
                float y = 172f + i * 88f;
                RectTransform row = MenuUi.Rt(_soundCol, "Pack " + i);
                MenuUi.Place(row, 36f, y, ColW - 72f, 84f);
                _soundRows.Add(row.gameObject);
                MenuUi.Label(row, "Name", CMTokens.Display, 22, Cream, TextAnchor.MiddleLeft, 0f, 0f, ColW - 72f, 30f,
                    p.Name.ToUpperInvariant(), 2f);
                MenuUi.Label(row, "Detail", CMTokens.Ui, 20, Brass, TextAnchor.MiddleLeft, 0f, 30f, ColW - 72f, 26f,
                    CreditsContent.PackDetail(p), 2f);
                if (!string.IsNullOrEmpty(p.Page))
                    MenuUi.Label(row, "Page", CMTokens.Mono, 16, CMTokens.WithAlpha(Cream, 0.8f), TextAnchor.MiddleLeft, 0f, 56f,
                        ColW - 72f, 24f, p.Page, 0f);
            }
        }
    }
}
