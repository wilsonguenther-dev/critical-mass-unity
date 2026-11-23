using System;
using System.Collections.Generic;
using CriticalMass.Net;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Interact;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Online
{
    /// <summary>
    /// online-rooms-2: chat on screen, in the room and in the match. Its own lane (HudZones.Chat in a match, a box in the
    /// room screen's right column there), at most MaxLines lines plus the typing line, older lines fade after FadeSeconds.
    /// <para>b17-chat:</para>
    /// <list type="bullet">
    /// <item>Keys. In the match ENTER types to your team, SHIFT+ENTER to everyone (T is the emote wheel and Y walks to the
    /// pointer there); in the room T types to your team and Y to everyone (nothing else uses them on that screen). TAB
    /// switches TEAM / ALL while typing, ENTER sends, ESC cancels. Not while a station panel is open (ENTER buys there).
    /// While typing the menu's navigation is paused, every keyboard read of the Player map is gated (EatsKeys: HumanInputSource,
    /// InteractKey) and the fighter gets neutral input (SimDriverBehaviour reads Typing), so no key reaches a game binding.</item>
    /// <item>Typed chat ships OFF (ChatSettings / ChatDefaults, players may be minors); quick chat is on: on a controller
    /// HOLD D-PAD RIGHT in the match (a tap still zooms the minimap) or HOLD Y/TRIANGLE in the room, steer with the right
    /// stick, let go to send one of QuickChat's eight callouts. A teammate's callout also drops a short marker through the
    /// existing ping markers (Pings.Mirror, view only, never the sim).</item>
    /// <item>Names in their team colour (rich text; every typed '&lt;' '&gt;' becomes a look-alike, ChatText.EscapeRich);
    /// links are never clickable; blocked words masked unless this player turned the filter off.</item>
    /// <item>Scroll-back: PAGE UP / PAGE DOWN or the mouse wheel over the box, in the room and on the pause screen (there
    /// the box moves to the pause screen's right margin and shows more lines).</item>
    /// <item>CHAT SETTINGS (room screen button, or the button over the pause screen's chat box): chat, typed chat, word
    /// filter, mute everyone, voice, and every player with MUTE and REPORT (a report mutes at once and saves the
    /// evidence in chat-reports.txt on this computer; there is no server).</item>
    /// </list>
    /// Text changes only when a line arrives or a key is typed; fading only changes alpha.
    /// </summary>
    public sealed class OnlineChatHud : MonoBehaviour
    {
        public const float FadeSeconds = 8f, FadeOut = 1.5f;
        /// <summary>At most this many text lines on screen (wrapped lines count; oldest drop off the top).</summary>
        public const int MaxLines = 5;
        /// <summary>b17-chat: lines in the pause screen's box.</summary>
        public const int PauseLines = 18;
        /// <summary>b17-chat: the pause screen's chat box, in its right margin (the pause panel spans x 280..1640).</summary>
        public static readonly Rect PauseLane = new Rect(1652f, 214f, 256f, 776f);
        /// <summary>b17-chat: a controller button held this long opens the quick-chat wheel (shorter is the button's own tap).</summary>
        public const float HoldSeconds = 0.3f;

        /// <summary>true while the local player is typing a chat line.</summary>
        public static bool Typing { get; private set; }
        /// <summary>Typing, or the frame (and the one after) the box closed: keyboard presses belong to chat, not the game.</summary>
        public static bool EatsKeys => Typing || PanelOpen || Time.frameCount <= _closedFrame + 1;
        static int _closedFrame = -10;
        /// <summary>Where the lane sits (1920 x 1080 reference units, top-left origin). The room screen moves it into its own column.</summary>
        public static Rect Lane = HudZones.Chat;
        /// <summary>Shown at all (the room screen and the online match turn it on).</summary>
        public static bool Visible;
        /// <summary>true: Lane is in the menu's centred 1920 x 1080 space (the room screen); false: from the screen's top-left
        /// corner like the HUD clusters (the match).</summary>
        public static bool Centred;
        /// <summary>b17-chat: in an online match with chat on, the controller's D-pad right is a tap for the minimap zoom and a
        /// hold for quick chat (HudInteract zooms on a short release instead of on the press).</summary>
        public static bool PadQuickChatOwnsZoom => _one != null && _one._quickChatLive && OnlineMatch.InMatch && !_one._inPause && !HudFocusOn
            && !Hud.Mechanics.B15RulesHud.VoteOwnsDpad && InputContext.HudOwnsDpad(InputContext.Current);
        /// <summary>b17-chat: D-pad right was tapped (released before HoldSeconds): HudInteract zooms the minimap once.</summary>
        public static bool TakeZoomTap()
        {
            if (!_zoomTap) return false;
            _zoomTap = false;
            return true;
        }
        static bool _zoomTap;
        static bool HudFocusOn => HudInteract.Instance != null && HudInteract.Instance.HudFocus;
        /// <summary>b17-chat: the chat settings panel is open (the room screen's BACK closes it first).</summary>
        public static bool PanelOpen => _one != null && _one._panel != null && _one._panel.gameObject.activeSelf;
        /// <summary>b17-chat: the quick-chat wheel is open.</summary>
        public static bool WheelOpen => _one != null && _one._wheelOpen;

        static OnlineChatHud _one;
        Canvas _canvas;
        RectTransform _lane, _root, _space;
        bool _placedCentred;
        Text _body;
        float _born = -100f;
        Text _input;
        int _seenVersion = -1, _seenSettings = -1;
        Rect _placed;
        int _placedLines = -1;
        string _typed = "";
        ChatScope _scope;
        int _openedFrame = -1;
        int _scroll;
        bool _inPause;
        ChatChannel _bound;
        bool _quickChatLive;
        bool _typedOffTold, _voiceTold, _typedTold;

        public static void Ensure()
        {
            if (_one != null) return;
            var go = new GameObject("Online Chat");
            DontDestroyOnLoad(go);
            _one = go.AddComponent<OnlineChatHud>();
            go.AddComponent<OnlineVoice>();
        }

        static ChatChannel Channel => OnlineMatch.Host != null ? OnlineMatch.Host.Chat : OnlineMatch.Client?.Chat;

        void Start()
        {
            var go = new GameObject("Chat Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 55;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(HudLayout.RefW, HudLayout.RefH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Shrink;   // the HUD's uniform fit: the whole 1920 x 1080 space always fits
            go.AddComponent<GraphicRaycaster>();   // b17-chat: the settings panel's buttons take clicks
            _root = (RectTransform)go.transform;
            _space = MenuUi.Rt(_root, "Centred Space");
            _space.anchorMin = _space.anchorMax = new Vector2(0.5f, 0.5f);
            _space.pivot = new Vector2(0.5f, 0.5f);
            _space.sizeDelta = new Vector2(HudLayout.RefW, HudLayout.RefH);
            _lane = MenuUi.Rt(_root, "Lane");
            _lane.anchorMin = _lane.anchorMax = new Vector2(0f, 1f);
            _lane.pivot = new Vector2(0f, 1f);
            // online-rooms-5: one wrapped box (newest line at the bottom), so a long line wraps instead of running off.
            _body = MakeBody(_lane, UiScale.TypeSmall, 100f, 100f, true);
            _input = MenuUi.Label(_lane, "Typing", CMTokens.Ui, UiScale.TypeSmall, CMTokens.ToolBrass.Color, TextAnchor.MiddleLeft,
                0f, HudZones.ChatLines * HudZones.ChatLineH + 6f, 100f, HudZones.ChatInputH, "", 2f);
            _input.supportRichText = false;
            ShrinkOnly(_input, UiScale.TypeSmall);
            MenuUi.Contain(_input, 1);
            _placed = new Rect(-1, -1, 0, 0);
            _lane.gameObject.AddComponent<RectMask2D>();   // online-rooms-6: nothing draws past the box's edges
            BuildWheel();
            BuildPauseButton();
        }

        static bool PauseOpen() => PauseMenu.AnyOpen;

        void Place(Rect lane, bool centred, int lines)
        {
            if (_placed == lane && _placedCentred == centred && _placedLines == lines) return;
            _placed = lane;
            _placedCentred = centred;
            _placedLines = lines;
            _lane.SetParent(centred ? _space : _root, false);
            // Top-left anchored, top-left pivot: resizing never shifts the lane (the old centre pivot pushed it off the left edge).
            _lane.anchorMin = _lane.anchorMax = new Vector2(0f, 1f);
            _lane.pivot = new Vector2(0f, 1f);
            _lane.anchoredPosition = new Vector2(lane.x, -lane.y);
            _lane.sizeDelta = new Vector2(lane.width, lane.height);
            float bodyH = lane.height - HudZones.ChatInputH - 6f;
            MenuUi.Place(_body.rectTransform, 0f, 0f, lane.width, bodyH);
            MenuUi.Place(_input.rectTransform, 0f, bodyH + 6f, lane.width, HudZones.ChatInputH);
            MenuUi.Contain(_body, lines);
            _seenVersion = -1;
        }

        void Update()
        {
            ChatChannel ch = Channel;
            if (ch != _bound) Bind(ch);
            if (ch != null && ChatSettings.Changed != _seenSettings) { _seenSettings = ChatSettings.Changed; ChatSettings.Apply(ch); _seenVersion = -1; }
            _inPause = PauseOpen() && OnlineMatch.InMatch;
            // online-rooms-6: never behind the match's stopped panel; b17-chat: on the pause screen the box moves to its margin.
            bool show = Visible && ch != null && OnlineMatch.InRoom && !OnlineMatchOverlay.ModalUp && (!PauseOpen() || _inPause);
            if (_canvas.enabled != show) _canvas.enabled = show;
            _quickChatLive = show && ch.Enabled;
            if (!show) { StopTyping(); CloseWheel(false); ClosePanelNow(); return; }
            _canvas.sortingOrder = PanelOpen || _inPause ? 90 : 55;   // over the pause screen (40), under the scene fade (100)
            if (_pauseButton != null && _pauseButton.gameObject.activeSelf != _inPause) _pauseButton.gameObject.SetActive(_inPause);
            if (_inPause) Place(PauseLane, true, PauseLines);
            else Place(Lane, Centred, MaxLines);
            if (ch.Version != _seenVersion) Redraw(ch);
            Keyboard kb = Keyboard.current;
            if (!PanelOpen) TickKeys(ch, kb);
            else if (kb != null && kb.escapeKey.wasPressedThisFrame) ClosePanel();
            TickScroll(ch, kb);
            TickWheel(ch);
            if (!Typing) ShowIdleLine();
            float age = Time.unscaledTime - _born;
            float alpha = Typing || OnlineMenuScreen.AnyOpen || _inPause || _scroll > 0 ? 1f : age < FadeSeconds ? 1f : Mathf.Clamp01(1f - (age - FadeSeconds) / FadeOut);
            Color c = _body.color;
            if (!Mathf.Approximately(c.a, alpha)) { c.a = alpha; _body.color = c; }
            if (_reenableNav && Time.frameCount > _closedFrame + 1) { _reenableNav = false; SetGameKeys(true); }
        }

        void Bind(ChatChannel ch)
        {
            if (_bound != null) _bound.Received -= OnLine;
            _bound = ch;
            _scroll = 0;
            if (ch != null) { ch.Received += OnLine; ChatSettings.Apply(ch); }
            _seenSettings = ChatSettings.Changed;
        }

        void TickKeys(ChatChannel ch, Keyboard kb)
        {
            if (kb == null) return;
            if (!Typing)
            {
                if (_inPause || WheelOpen || CharacterPanel.IsTyping || OnlineMenuScreen.TypingName) return;
                bool inMatch = OnlineMatch.InMatch;
                bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                bool open = false;
                if (inMatch)
                {
                    if ((kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) && !Hud.Mechanics.StationPanel.IsOpen && !FullMap.IsOpen && !HudFocusOn)
                    { _scope = shift ? ChatScope.All : ChatScope.Team; open = true; }
                }
                else if (OnlineMenuScreen.AnyOpen)
                {
                    if (kb.tKey.wasPressedThisFrame) { _scope = ChatScope.Team; open = true; }
                    else if (kb.yKey.wasPressedThisFrame) { _scope = shift ? ChatScope.Team : ChatScope.All; open = true; }
                }
                if (!open) return;
                if (!ch.Enabled) { TellOnce(ch, ref _typedOffTold, "Chat is off. Turn it on in CHAT SETTINGS."); return; }
                if (!ch.TypedText)
                {
                    TellOnce(ch, ref _typedOffTold, "Typed chat is off. Turn it on in CHAT SETTINGS. Controller: hold " + (inMatch ? "D-pad right" : "Y") + " for quick chat.");
                    return;
                }
                StartTyping();
                return;
            }
            if (kb.escapeKey.wasPressedThisFrame) StopTyping();
            else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) { if (Time.frameCount > _openedFrame) Send(ch); }
            else if (kb.tabKey.wasPressedThisFrame) { _scope = _scope == ChatScope.Team ? ChatScope.All : ChatScope.Team; ShowTyped(); }
            else if (kb.backspaceKey.wasPressedThisFrame && _typed.Length > 0) { _typed = _typed.Substring(0, _typed.Length - 1); ShowTyped(); }
        }

        static void TellOnce(ChatChannel ch, ref bool told, string words)
        {
            if (told) return;
            told = true;
            ch.Notice(words, OnlineMatch.NowMs);
        }

        void TickScroll(ChatChannel ch, Keyboard kb)
        {
            if (!(OnlineMenuScreen.AnyOpen || _inPause)) { if (_scroll != 0) { _scroll = 0; _seenVersion = -1; } return; }
            int d = 0;
            if (kb != null && !Typing)
            {
                if (kb.pageUpKey.wasPressedThisFrame) d += 3;
                if (kb.pageDownKey.wasPressedThisFrame) d -= 3;
                if (kb.endKey.wasPressedThisFrame) d = -_scroll;
            }
            Mouse m = Mouse.current;
            if (m != null && Over(_lane, m.position.ReadValue()))
            {
                float s = m.scroll.ReadValue().y;
                if (s > 0.1f) d += 1; else if (s < -0.1f) d -= 1;
            }
            if (d == 0) return;
            int max = Mathf.Max(0, ch.Lines.Count - 1);
            int next = Mathf.Clamp(_scroll + d, 0, max);
            if (next != _scroll) { _scroll = next; _seenVersion = -1; }
        }

        bool Over(RectTransform rt, Vector2 screen) => RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);

        void ShowIdleLine()
        {
            string s = OnlineVoice.StatusLine;
            if (string.IsNullOrEmpty(s) && _scroll > 0) s = "OLDER LINES (" + _scroll + " BACK)   PAGE DOWN / END: NEWEST";
            else if (string.IsNullOrEmpty(s) && _inPause) s = "PAGE UP / DOWN OR THE WHEEL: SCROLL";
            HudText.Set(_input, s ?? "");
        }

        /// <summary>
        /// The newest lines that fit the box, oldest first (minus the scroll-back). online-rooms-6: each line's wrapped height
        /// is measured on the box's own font and width (TextFit), and the oldest line is dropped whenever the next would not
        /// fit whole. b17-chat: measured on the plain words, drawn with the team-coloured names.
        /// </summary>
        void Redraw(ChatChannel ch)
        {
            _seenVersion = ch.Version;
            var lines = ch.Lines;
            float width = Mathf.Max(40f, _placed.width);
            float bodyH = _placed.height - HudZones.ChatInputH - 6f;
            _texts.Clear();
            _rich.Clear();
            int end = Mathf.Max(0, lines.Count - _scroll);
            for (int k = 0; k < end; k++)
            {
                ChatLine l = lines[k];
                l.Text = ch.Shown(l);
                _texts.Add(ChatText.Plain(l));
                _rich.Add(ChatText.Rich(l, TeamHex(l.Team)));
            }
            ShowWholeLines(_body, _texts, _rich, UiScale.TypeSmall, width, bodyH, _inPause ? PauseLines : MaxLines, _picked);
            _born = Time.unscaledTime;
        }

        static string TeamHex(int team)
        {
            if (team != 0 && team != 1) return null;
            return ColorUtility.ToHtmlStringRGB(CMTokens.TeamInk((TeamId)team));
        }

        // b11-af: reused on every redraw (a redraw runs only when a chat line arrives, never per frame).
        readonly List<string> _texts = new List<string>(32);
        readonly List<string> _rich = new List<string>(32);
        readonly List<string> _picked = new List<string>(PauseLines);
        static readonly List<UILineInfo> GenLines = new List<UILineInfo>(16);
        static readonly TextGenerator Gen = new TextGenerator(512);

        /// <summary>
        /// b11-af (lobby notice box cut its top line in half when it scrolled): puts on body the newest of texts (oldest
        /// first in the list) that fit a width x height box as WHOLE lines, at most maxLines drawn lines, and returns what it
        /// set. picked receives the shown lines, oldest first. Allocation only when a line arrives, never per frame.
        /// </summary>
        public static string ShowWholeLines(Text body, IList<string> texts, int size, float width, float height, int maxLines, List<string> picked) =>
            ShowWholeLines(body, texts, null, size, width, height, maxLines, picked);

        /// <summary>b17-chat: as above, measuring texts (plain) and drawing shown[k] (rich) instead when given.</summary>
        public static string ShowWholeLines(Text body, IList<string> texts, IList<string> shownTexts, int size, float width, float height,
            int maxLines, List<string> picked)
        {
            picked.Clear();
            if (body == null) return string.Empty;
            IList<string> draw = shownTexts != null && shownTexts.Count == texts.Count ? shownTexts : texts;
            float lineH = Mathf.Max(1f, TextFit.LineHeightOf(body, size));
            int cap = Mathf.Max(1, maxLines), used = 0;
            string measured = string.Empty;
            int first = texts.Count;
            for (int k = texts.Count - 1; k >= 0; k--)
            {
                // the line cap counts wrapped lines (as before); the fit is the whole block's measured height against the box
                int need = Mathf.Max(1, Mathf.RoundToInt(TextFit.HeightOf(body, texts[k], size, width, true) / lineH));
                if (used + need > cap) break;
                string cand = first == texts.Count ? texts[k] : texts[k] + "\n" + measured;
                if (TextFit.HeightOf(body, cand, size, width, true) > height + 0.5f) break;
                used += need;
                first = k;
                measured = cand;
            }
            if (first == texts.Count && texts.Count > 0) first = texts.Count - 1;   // the newest line alone always shows
            for (int k = first; k < texts.Count; k++) picked.Add(draw[k]);
            string shown = string.Join("\n", picked);
            HudText.Set(body, shown);
            // The final word: Unity's own layout of what the rule set. Drop the oldest line while the top one pokes out.
            while (picked.Count > 1 && (!TopLineInside(body) || DrawnLines(body) > cap))
            {
                picked.RemoveAt(0);
                shown = string.Join("\n", picked);
                HudText.Set(body, shown);
            }
            return shown;
        }

        /// <summary>
        /// b11-af-2: the chat lines label, built exactly as the lane uses it (tests build it with this too): small type,
        /// bottom-aligned, wrapped, plain text, under the text rule with a SHRINK-ONLY range (size down to its floor).
        /// b17-chat: rich = the lane's own label (team-coloured names; every typed tag is escaped before it gets here).
        /// </summary>
        public static Text MakeBody(Transform parent, int size, float w, float h) => MakeBody(parent, size, w, h, false);

        public static Text MakeBody(Transform parent, int size, float w, float h, bool rich)
        {
            Text t = MenuUi.Label(parent, "Lines", CMTokens.Ui, size, CMTokens.WarmCream.Color, TextAnchor.LowerLeft, 0f, 0f, w, h, "", 2f);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.supportRichText = rich;
            ShrinkOnly(t, size);
            MenuUi.Contain(t, MaxLines);
            return t;
        }

        /// <summary>b11-af-2: the range MenuUi.Contain reads, set to the label's own size down to its floor (never larger).</summary>
        static void ShrinkOnly(Text t, int size)
        {
            t.resizeTextMaxSize = size;
            t.resizeTextMinSize = ContainedText.FloorFor(size);
        }

        /// <summary>b11-af-2: lines Unity draws for t at its current settings (generator), else TextFit's greedy count.</summary>
        public static int DrawnLines(Text t)
        {
            if (t == null || string.IsNullOrEmpty(t.text)) return 0;
            Rect r = t.rectTransform.rect;
            if (Populated(t, r) && GenLines.Count > 0 && GenLines[0].height > 0) return GenLines.Count;
            float lh = Mathf.Max(1f, TextFit.LineHeightOf(t, t.fontSize));
            return Mathf.Max(1, Mathf.RoundToInt(TextFit.HeightOf(t, t.text, t.fontSize, r.width, t.horizontalOverflow == HorizontalWrapMode.Wrap) / lh));
        }

        /// <summary>
        /// b11-af-2, tests: the label's box (rect, pivot space), size range and settings, and every drawn line's top and
        /// bottom in the same space (Unity's generator / ppu), so one failed run shows where each line sits.
        /// </summary>
        public static string Describe(Text t)
        {
            if (t == null) return "(no label)";
            Rect r = t.rectTransform.rect;
            float ppu = TextFit.PixelsPerUnit(t);
            var sb = new System.Text.StringBuilder(256);
            sb.Append("box x ").Append(r.xMin.ToString("0.0")).Append("..").Append(r.xMax.ToString("0.0"))
              .Append(" y ").Append(r.yMin.ToString("0.0")).Append("..").Append(r.yMax.ToString("0.0"))
              .Append(" (").Append(r.width.ToString("0.0")).Append(" x ").Append(r.height.ToString("0.0")).Append(")")
              .Append(" ppu ").Append(ppu.ToString("0.000")).Append(" size ").Append(t.fontSize)
              .Append(" range ").Append(t.resizeTextMinSize).Append("..").Append(t.resizeTextMaxSize)
              .Append(" wrap ").Append(t.horizontalOverflow).Append(" v ").Append(t.verticalOverflow).Append(" align ").Append(t.alignment)
              .Append(" lineH(TextFit) ").Append(TextFit.LineHeightOf(t, t.fontSize).ToString("0.00"))
              .Append(" height(TextFit) ").Append(TextFit.HeightOf(t, t.text, t.fontSize, r.width, t.horizontalOverflow == HorizontalWrapMode.Wrap).ToString("0.0"));
            if (Populated(t, r))
            {
                sb.Append(" generator lines ").Append(GenLines.Count).Append(':');
                for (int i = 0; i < GenLines.Count; i++)
                {
                    UILineInfo l = GenLines[i];
                    sb.Append(" [").Append(i).Append(" top ").Append((l.topY / ppu).ToString("0.0"))
                      .Append(" bottom ").Append(((l.topY - l.height) / ppu).ToString("0.0")).Append(" h ").Append((l.height / ppu).ToString("0.0")).Append(']');
                }
            }
            else sb.Append(" generator: no layout");
            sb.Append(" text \"").Append(t.text.Replace("\n", " | ")).Append('"');
            return sb.ToString();
        }

        /// <summary>Unity's generator run on t's text and settings (as Text draws it); its lines land in GenLines.</summary>
        static bool Populated(Text t, Rect r)
        {
            GenLines.Clear();
            if (t.font == null) return false;
            TextGenerationSettings st = t.GetGenerationSettings(r.size);
            st.generateOutOfBounds = true;
            if (!Gen.Populate(t.text, st)) return false;
            Gen.GetLines(GenLines);
            return true;
        }

        /// <summary>
        /// b11-af: true when the first drawn line of t sits wholly inside t's rect (its top edge at or under the rect's top),
        /// from Unity's own line layout at t's current settings and canvas scale; where the font gives no metrics (headless
        /// runner) from TextFit's measured height of the drawn text. Tests and ShowWholeLines.
        /// </summary>
        public static bool TopLineInside(Text t)
        {
            if (t == null || string.IsNullOrEmpty(t.text)) return true;
            Rect r = t.rectTransform.rect;
            bool wrap = t.horizontalOverflow == HorizontalWrapMode.Wrap;
            if (Populated(t, r) && GenLines.Count > 0 && GenLines[0].height > 0)
                return GenLines[0].topY / TextFit.PixelsPerUnit(t) <= r.yMax + 0.5f;   // generator space is the rect's pivot space x ppu
            return TextFit.HeightOf(t, t.text, t.fontSize, r.width, wrap) <= r.height + 0.5f;
        }

        // ---- typing ----------------------------------------------------------------------------------------------------

        bool _reenableNav;

        void StartTyping()
        {
            if (Typing || Keyboard.current == null) return;
            Typing = true;
            _typed = "";
            _openedFrame = Time.frameCount;
            Keyboard.current.onTextInput += OnChar;
            SetGameKeys(false);
            ShowTyped();
        }

        void StopTyping()
        {
            if (!Typing) return;
            Typing = false;
            _closedFrame = Time.frameCount;
            if (Keyboard.current != null) Keyboard.current.onTextInput -= OnChar;
            HudText.Set(_input, "");
            _reenableNav = true;   // the game's keys come back the frame after next (EatsKeys covers the closing frame)
        }

        /// <summary>
        /// b17-chat: while typing the menu ignores navigation and submit keys. The Player map stays on (HumanControlGuard
        /// would call a switched-off map a lockout): instead every keyboard read of it is gated by EatsKeys (HumanInputSource,
        /// InteractKey) and the fighter gets neutral input (SimDriverBehaviour), so no typed key reaches a game binding.
        /// </summary>
        void SetGameKeys(bool on)
        {
            if (EventSystem.current != null) EventSystem.current.sendNavigationEvents = on;
        }

        void OnChar(char c)
        {
            if (!Typing || char.IsControl(c) || _typed.Length >= ChatFilter.MaxLength) return;
            if (Time.frameCount <= _openedFrame + 1 && _typed.Length == 0 && (c == 'y' || c == 'Y' || c == 't' || c == 'T')) return;   // the key press that opened chat
            _typed += c;
            ShowTyped();
        }

        void ShowTyped() => HudText.Set(_input, (_scope == ChatScope.Team ? "TO TEAM (TAB: ALL): " : "TO ALL (TAB: TEAM): ") + _typed + "_");

        void Send(ChatChannel ch)
        {
            string t = _typed.Trim();
            StopTyping();
            if (t.Length == 0) return;
            string lower = t.ToLowerInvariant();
            if (lower.StartsWith("/mute ") || lower.StartsWith("/unmute ") || lower.StartsWith("/report "))
            {
                int sp = t.IndexOf(' ');
                string who = ChatFilter.CleanName(t.Substring(sp + 1));
                int peer = PeerNamed(who);
                if (peer < 0) { ch.Notice("No player called " + who + ".", OnlineMatch.NowMs); return; }
                if (lower.StartsWith("/report ")) { Report(ch, peer); return; }
                bool mute = lower.StartsWith("/mute ");
                ch.SetMuted(peer, mute);
                ch.Notice(mute ? who + " is muted for you." : who + " can be heard again.", OnlineMatch.NowMs);
                return;
            }
            string why = ch.Say(t, _scope, OnlineMatch.NowMs);
            if (why != null) ch.Notice(why, OnlineMatch.NowMs);
        }

        static List<RoomMember> Members => OnlineMatch.Host != null ? OnlineMatch.Host.Members : OnlineMatch.Client?.Members;

        static int LocalPeer => OnlineMatch.Host != null ? Peers.Host : OnlineMatch.Client != null ? OnlineMatch.Client.LocalPeer : -1;

        static int PeerNamed(string name)
        {
            var members = Members;
            if (members == null) return -1;
            foreach (RoomMember m in members) if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) return m.Peer;
            return -1;
        }

        /// <summary>b17-chat: a report: muted at once, the evidence appended to chat-reports.txt on this computer.</summary>
        static void Report(ChatChannel ch, int peer)
        {
            string text = ch.Report(peer, OnlineMatch.NowMs, DateTime.UtcNow.ToString("u"));
            if (text == null) return;
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(CriticalMass.Runtime.Game.GamePaths.PersistentRoot, "chat-reports.txt"), text + "\n"); }
            catch (Exception e) { Debug.LogWarning("chat report not saved: " + e.Message); }
            Debug.Log("[chat] " + text);
        }

        // ---- callouts: markers -----------------------------------------------------------------------------------------

        void OnLine(ChatLine l)
        {
            if (l.Callout < 0 || !QuickChat.Valid(l.Callout)) return;
            int kind = QuickChat.MarkerKind[l.Callout];
            LockstepSession s = OnlineMatch.Session;
            if (kind < 0 || s == null || s.World == null || l.Scope != ChatScope.Team) return;
            World w = s.World;
            int slot = s.Setup.SlotOfPeer(l.FromPeer);
            int actor = slot >= 0 ? s.Setup.Humans[slot].Actor : -1;
            double x, z;
            switch (QuickChat.Marker[l.Callout])
            {
                case CalloutMarker.Hill: x = w.Map.Hill.X; z = w.Map.Hill.Z; break;
                case CalloutMarker.Kaiju:
                    if (w.Match.Hatched && w.Kaiju.State != KaijuState.Egg) { x = w.Kaiju.X; z = w.Kaiju.Z; }
                    else { x = w.Map.Shrine.X; z = w.Map.Shrine.Z; }
                    break;
                case CalloutMarker.OwnBase:
                    int t = l.Team == 1 ? 1 : 0;
                    if (w.Map.Pads == null || t >= w.Map.Pads.Length) return;
                    x = w.Map.Pads[t].X; z = w.Map.Pads[t].Z;
                    break;
                case CalloutMarker.Sender:
                    if (actor < 0 || actor >= w.ActorCount) return;
                    x = w.X[actor]; z = w.Z[actor];
                    break;
                default: return;
            }
            try { Pings.Mirror(w, (PingKind)kind, x, z, Mathf.Max(0, actor), PingBook.Life); }
            catch (Exception e) { Debug.LogWarning("callout marker: " + e.Message); }   // a marker never breaks chat
        }

        // ---- the quick-chat wheel (controller) -------------------------------------------------------------------------

        RectTransform _wheel;
        readonly Text[] _wheelText = new Text[QuickChat.Count];
        readonly VectorShape[] _wheelBack = new VectorShape[QuickChat.Count];
        bool _wheelOpen;
        int _wheelPick = -1;
        float _holdSince = -1f;
        bool _holdUsed;
        const float WheelR = 230f;

        void BuildWheel()
        {
            _wheel = MenuUi.Rt(_space, "Quick Chat");
            MenuUi.Place(_wheel, 0f, 0f, HudLayout.RefW, HudLayout.RefH);
            MenuUi.Label(_wheel, "Title", CMTokens.Display, UiScale.TypeBody, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter,
                760f, 520f, 400f, 40f, "QUICK CHAT", 2f);
            for (int i = 0; i < QuickChat.Count; i++)
            {
                float a = i * Mathf.PI * 2f / QuickChat.Count;   // 0 = up, clockwise
                float cx = 960f + Mathf.Sin(a) * WheelR, cy = 540f - Mathf.Cos(a) * WheelR;
                _wheelBack[i] = MenuUi.Shape(_wheel, "Slot " + i, ShapeKind.RoundRect, cx - 130f, cy - 26f, 260f, 52f, CMTokens.WithAlpha(CMTokens.VoidNavy.Color, 0.85f), 14f);
                _wheelText[i] = MenuUi.Label(_wheel, "Callout " + i, CMTokens.Ui, UiScale.TypeSmall, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter,
                    cx - 124f, cy - 24f, 248f, 48f, (QuickChat.Scope[i] == ChatScope.All ? "ALL: " : "") + QuickChat.Text[i], 2f);
                _wheelText[i].supportRichText = false;
            }
            _wheel.gameObject.SetActive(false);
        }

        void TickWheel(ChatChannel ch)
        {
            Gamepad pad = Gamepad.current;
            bool inMatch = OnlineMatch.InMatch && !_inPause;
            bool inRoom = OnlineMenuScreen.AnyOpen && !OnlineMatch.InMatch && !PanelOpen;
            if (pad == null || !ch.Enabled || Typing || !(inMatch || inRoom) || (inMatch && !PadQuickChatOwnsZoom)) { CloseWheel(false); _holdSince = -1f; return; }
            ButtonControl held = inMatch ? pad.dpad.right : pad.buttonNorth;
            if (held.isPressed)
            {
                if (_holdSince < 0f) { _holdSince = Time.unscaledTime; _holdUsed = false; }
                if (!_wheelOpen && !_holdUsed && Time.unscaledTime - _holdSince >= HoldSeconds) OpenWheel();
            }
            else
            {
                if (_wheelOpen) CloseWheel(true);
                else if (inMatch && _holdSince >= 0f && !_holdUsed) _zoomTap = true;   // a short tap is still the minimap zoom
                _holdSince = -1f;
            }
            if (!_wheelOpen) return;
            Vector2 d = pad.rightStick.ReadValue();
            if (d.sqrMagnitude < 0.25f) d = inRoom ? pad.leftStick.ReadValue() : d;
            int pick = -1;
            if (d.sqrMagnitude >= 0.25f)
            {
                float ang = Mathf.Atan2(d.x, d.y);   // 0 = up, clockwise positive
                if (ang < 0f) ang += Mathf.PI * 2f;
                pick = Mathf.RoundToInt(ang / (Mathf.PI * 2f / QuickChat.Count)) % QuickChat.Count;
            }
            if (pick != _wheelPick)
            {
                _wheelPick = pick;
                for (int i = 0; i < QuickChat.Count; i++)
                {
                    _wheelBack[i].SetFill(i == pick ? CMTokens.ToolBrass.Color : CMTokens.WithAlpha(CMTokens.VoidNavy.Color, 0.85f));
                    _wheelText[i].color = i == pick ? CMTokens.VoidNavy.Color : CMTokens.WarmCream.Color;
                }
            }
            if (pad.buttonEast.wasPressedThisFrame) { _holdUsed = true; CloseWheel(false); _closedFrame = Time.frameCount; }   // B cancels (and is not also BACK)
        }

        void OpenWheel()
        {
            _wheelOpen = true;
            _holdUsed = true;
            _wheelPick = -2;
            _wheel.gameObject.SetActive(true);
        }

        void CloseWheel(bool send)
        {
            if (!_wheelOpen) return;
            _wheelOpen = false;
            _wheel.gameObject.SetActive(false);
            ChatChannel ch = Channel;
            if (send && ch != null && _wheelPick >= 0)
            {
                string why = ch.Callout(_wheelPick, OnlineMatch.NowMs);
                if (why != null) ch.Notice(why, OnlineMatch.NowMs);
            }
            _wheelPick = -1;
        }

        // ---- the settings panel ----------------------------------------------------------------------------------------

        RectTransform _panel;
        MenuItem _pauseButton;
        readonly List<MenuItem> _panelItems = new List<MenuItem>();
        readonly List<MenuItem> _rowMute = new List<MenuItem>(), _rowReport = new List<MenuItem>();
        readonly List<Text> _rowName = new List<Text>();
        readonly List<int> _rowPeer = new List<int>();
        MenuItem _chatBtn, _typedBtn, _filterBtn, _muteAllBtn, _voiceBtn, _closeBtn;
        GameObject _selectedBefore;
        int _panelVersion = -1;
        const int Rows = 16;

        void BuildPauseButton()
        {
            _pauseButton = MenuUi.Item(_space, "Chat Settings (pause)", PauseLane.x, PauseLane.y - 66f, PauseLane.width, 56f, "CHAT SETTINGS",
                MenuItem.Kind.Button, UiScale.TypeSmall, true);
            _pauseButton.Submitted = () => ToggleSettings();
            _pauseButton.gameObject.SetActive(false);
        }

        /// <summary>b17-chat: opens or closes the chat settings panel (the room screen's CHAT SETTINGS button).</summary>
        public static void ToggleSettings()
        {
            if (_one == null) return;
            if (PanelOpen) _one.ClosePanel(); else _one.OpenPanel();
        }

        /// <summary>b17-chat: the room screen's BACK while the panel is open closes the panel only.</summary>
        public static void ClosePanelFromMenu() { if (_one != null) _one.ClosePanel(); }

        void BuildPanel()
        {
            _panel = MenuUi.Rt(_space, "Chat Settings");
            MenuUi.Place(_panel, 0f, 0f, HudLayout.RefW, HudLayout.RefH);
            VectorShape scrim = MenuUi.Shape(_panel, "Scrim", ShapeKind.RoundRect, 0f, 0f, HudLayout.RefW, HudLayout.RefH, CMTokens.WithAlpha(CMTokens.VoidNavy.Color, 0.55f), 0f, true);
            scrim.OutlinePx = 0f;
            scrim.RimPx = 0f;
            MenuUi.Panel(_panel, "Panel", 300f, 90f, 1320f, 900f);
            MenuUi.Label(_panel, "Title", CMTokens.Display, UiScale.TypeHeading, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft, 340f, 110f, 900f, 50f, "CHAT SETTINGS", 2f);
            MenuUi.Label(_panel, "Note", CMTokens.Ui, UiScale.TypeSmall, CMTokens.WithAlpha(CMTokens.WarmCream.Color, 0.85f), TextAnchor.UpperLeft, 340f, 162f, 1240f, 60f,
                "Typed chat is off unless you turn it on. Quick chat (controller: hold D-pad right in a match, Y in the room) is always there. Reports stay on this computer.", 2f)
                .horizontalOverflow = HorizontalWrapMode.Wrap;
            float y = 232f;
            _chatBtn = Btn("Chat", 340f, y, 400f, () => ChatSettings.Chat = !ChatSettings.Chat);
            _typedBtn = Btn("Typed", 760f, y, 400f, ToggleTyped);
            _filterBtn = Btn("Filter", 1180f, y, 400f, () => ChatSettings.WordFilter = !ChatSettings.WordFilter);
            y += 70f;
            _muteAllBtn = Btn("Mute All", 340f, y, 400f, () => ChatSettings.MuteAll = !ChatSettings.MuteAll);
            _voiceBtn = Btn("Voice", 760f, y, 400f, ToggleVoice);
            _closeBtn = Btn("Close", 1180f, y, 400f, ClosePanel);
            HudText.Set(_closeBtn.Label, "CLOSE");
            MenuUi.Label(_panel, "Players", CMTokens.Ui, UiScale.TypeSmall, CMTokens.WithAlpha(CMTokens.WarmCream.Color, 0.7f), TextAnchor.MiddleLeft, 340f, 378f, 900f, 30f,
                "PLAYERS (MUTE: HIDE THEM FOR YOU. REPORT: MUTE AND SAVE WHAT THEY SAID)", 2f);
            for (int i = 0; i < Rows * 2; i++)
            {
                int col = i / Rows, row = i % Rows;
                float x = 340f + col * 630f, ry = 414f + row * 35f;
                _rowName.Add(MenuUi.Label(_panel, "Name " + i, CMTokens.Ui, UiScale.TypeMin, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft, x, ry, 300f, 32f, "", 2f));
                _rowName[i].supportRichText = false;
                int idx = i;
                MenuItem mute = MenuUi.Item(_panel, "Mute " + i, x + 310f, ry, 140f, 32f, "MUTE", MenuItem.Kind.Button, UiScale.TypeMin, true);
                mute.Submitted = () => MuteRow(idx);
                MenuItem rep = MenuUi.Item(_panel, "Report " + i, x + 460f, ry, 140f, 32f, "REPORT", MenuItem.Kind.Button, UiScale.TypeMin, true);
                rep.Submitted = () => ReportRow(idx);
                _rowMute.Add(mute);
                _rowReport.Add(rep);
                _rowPeer.Add(-1);
            }
            _panel.gameObject.SetActive(false);
        }

        MenuItem Btn(string name, float x, float y, float w, Action act)
        {
            MenuItem it = MenuUi.Item(_panel, name, x, y, w, 56f, "", MenuItem.Kind.Button, UiScale.TypeSmall, true);
            it.Submitted = act;
            _panelItems.Add(it);
            return it;
        }

        void ToggleTyped()
        {
            ChatSettings.TypedText = !ChatSettings.TypedText;
            if (ChatSettings.TypedText && Channel != null && !_typedTold) { _typedTold = true; Channel.Notice(ChatDefaults.TypedNotice, OnlineMatch.NowMs); }
        }

        void ToggleVoice()
        {
            ChatSettings.Voice = !ChatSettings.Voice;
            if (ChatSettings.Voice && Channel != null && !_voiceTold)
            {
                _voiceTold = true;
                Channel.Notice(ChatDefaults.VoiceNotice + " Hold V (controller: LB) in the room to talk.", OnlineMatch.NowMs);
            }
        }

        void MuteRow(int i)
        {
            ChatChannel ch = Channel;
            int peer = _rowPeer[i];
            if (ch == null || peer < 0) return;
            ch.SetMuted(peer, !ch.IsMuted(peer));
            _panelVersion = -1;
        }

        void ReportRow(int i)
        {
            ChatChannel ch = Channel;
            int peer = _rowPeer[i];
            if (ch == null || peer < 0) return;
            Report(ch, peer);
            _panelVersion = -1;
        }

        void OpenPanel()
        {
            if (Channel == null) return;
            if (_panel == null) BuildPanel();
            StopTyping();
            CloseWheel(false);
            _panel.gameObject.SetActive(true);
            _panelVersion = -1;
            RefreshPanel();
            if (EventSystem.current != null)
            {
                _selectedBefore = EventSystem.current.currentSelectedGameObject;
                EventSystem.current.SetSelectedGameObject(_chatBtn.gameObject);
            }
        }

        void ClosePanel()
        {
            if (!PanelOpen) return;
            ClosePanelNow();
            _closedFrame = Time.frameCount;
            if (EventSystem.current != null && _selectedBefore != null) EventSystem.current.SetSelectedGameObject(_selectedBefore);
            _selectedBefore = null;
        }

        void ClosePanelNow()
        {
            if (_panel != null && _panel.gameObject.activeSelf) _panel.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (PanelOpen) RefreshPanel();
        }

        void RefreshPanel()
        {
            ChatChannel ch = Channel;
            if (ch == null) { ClosePanelNow(); return; }
            HudText.Set(_chatBtn.Label, ChatSettings.ChatLabel);
            HudText.Set(_typedBtn.Label, ChatSettings.TypedLabel);
            HudText.Set(_filterBtn.Label, ChatSettings.FilterLabel);
            HudText.Set(_muteAllBtn.Label, ChatSettings.MuteAllLabel);
            HudText.Set(_voiceBtn.Label, ChatSettings.VoiceLabel);
            var members = Members;
            int version = ch.Version * 64 + (members != null ? members.Count : 0);
            if (version == _panelVersion) return;
            _panelVersion = version;
            int me = LocalPeer, n = 0;
            if (members != null)
                foreach (RoomMember m in members)
                {
                    if (m.Peer == me || n >= _rowPeer.Count) continue;
                    _rowPeer[n] = m.Peer;
                    HudText.Set(_rowName[n], (m.Team == TeamId.Azure ? "AZURE  " : "CRIMSON  ") + m.Name);
                    _rowName[n].color = CMTokens.TeamInk(m.Team);
                    HudText.Set(_rowMute[n].Label, ch.IsMuted(m.Peer) ? "UNMUTE" : "MUTE");
                    n++;
                }
            for (int i = 0; i < _rowPeer.Count; i++)
            {
                bool on = i < n;
                if (!on) _rowPeer[i] = -1;
                if (_rowName[i].gameObject.activeSelf != on) _rowName[i].gameObject.SetActive(on);
                if (_rowMute[i].gameObject.activeSelf != on) _rowMute[i].gameObject.SetActive(on);
                if (_rowReport[i].gameObject.activeSelf != on) _rowReport[i].gameObject.SetActive(on);
            }
        }

        void OnDestroy()
        {
            StopTyping();
            if (_reenableNav) SetGameKeys(true);
            if (_bound != null) _bound.Received -= OnLine;
            if (_one == this) _one = null;
        }
    }
}
