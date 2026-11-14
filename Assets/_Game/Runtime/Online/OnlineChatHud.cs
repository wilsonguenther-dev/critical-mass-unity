using CriticalMass.Net;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Online
{
    /// <summary>
    /// online-rooms-2: chat on screen, in the room and in the match. Its own lane (HudZones.Chat in a match, a box in the
    /// room screen's right column there), at most HudZones.ChatLines lines plus the typing line, every line a contained
    /// one-line box, older lines fade after FadeSeconds. Y opens chat to everyone, Shift+Y to your team; Enter sends,
    /// Escape cancels. While typing, this machine sends neutral inputs (OnlineMatch reads Typing), so letters typed into
    /// chat never move the fighter and no movement key is ever taken from the game. /mute NAME and /unmute NAME hide or
    /// show one player's lines on this machine. Text changes only when a line arrives or a key is typed (no per-frame
    /// allocation); fading only changes alpha.
    /// </summary>
    public sealed class OnlineChatHud : MonoBehaviour
    {
        public const float FadeSeconds = 8f, FadeOut = 1.5f;
        /// <summary>At most this many text lines on screen (wrapped lines count; oldest drop off the top).</summary>
        public const int MaxLines = 5;

        /// <summary>true while the local player is typing a chat line.</summary>
        public static bool Typing { get; private set; }
        /// <summary>Typing, or the frame (and the one after) the box closed: keyboard presses belong to chat, not the game.</summary>
        public static bool EatsKeys => Typing || Time.frameCount <= _closedFrame + 1;
        static int _closedFrame = -10;
        /// <summary>Where the lane sits (1920 x 1080 reference units, top-left origin). The room screen moves it into its own column.</summary>
        public static Rect Lane = HudZones.Chat;
        /// <summary>Shown at all (the room screen and the online match turn it on).</summary>
        public static bool Visible;
        /// <summary>true: Lane is in the menu's centred 1920 x 1080 space (the room screen); false: from the screen's top-left
        /// corner like the HUD clusters (the match).</summary>
        public static bool Centred;

        static OnlineChatHud _one;
        Canvas _canvas;
        RectTransform _lane, _root, _space;
        bool _placedCentred;
        Text _body;
        float _born = -100f;
        Text _input;
        int _seenVersion = -1;
        Rect _placed;
        string _typed = "";
        ChatScope _scope;
        int _openedFrame = -1;

        public static void Ensure()
        {
            if (_one != null) return;
            var go = new GameObject("Online Chat");
            DontDestroyOnLoad(go);
            _one = go.AddComponent<OnlineChatHud>();
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
            _root = (RectTransform)go.transform;
            _space = MenuUi.Rt(_root, "Centred Space");
            _space.anchorMin = _space.anchorMax = new Vector2(0.5f, 0.5f);
            _space.pivot = new Vector2(0.5f, 0.5f);
            _space.sizeDelta = new Vector2(HudLayout.RefW, HudLayout.RefH);
            _lane = MenuUi.Rt(_root, "Lane");
            _lane.anchorMin = _lane.anchorMax = new Vector2(0f, 1f);
            _lane.pivot = new Vector2(0f, 1f);
            // online-rooms-5: one wrapped box (newest line at the bottom), so a long line wraps instead of running off.
            _body = MenuUi.Label(_lane, "Lines", CMTokens.Ui, UiScale.TypeSmall, CMTokens.WarmCream.Color, TextAnchor.LowerLeft,
                0f, 0f, 100f, 100f, "", 2f);
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.supportRichText = false;   // typed text is shown as typed; TextFit measures plain text
            MenuUi.Contain(_body, MaxLines);
            _input = MenuUi.Label(_lane, "Typing", CMTokens.Ui, UiScale.TypeSmall, CMTokens.ToolBrass.Color, TextAnchor.MiddleLeft,
                0f, HudZones.ChatLines * HudZones.ChatLineH + 6f, 100f, HudZones.ChatInputH, "", 2f);
            MenuUi.Contain(_input, 1);
            _placed = new Rect(-1, -1, 0, 0);
            _lane.gameObject.AddComponent<RectMask2D>();   // online-rooms-6: nothing draws past the box's edges
        }

        static bool PauseOpen()
        {
            if (_pause == null && Time.frameCount >= _pauseLookAt) { _pause = FindAnyObjectByType<PauseMenu>(); _pauseLookAt = Time.frameCount + 30; }
            return _pause != null && _pause.IsOpen;
        }

        static PauseMenu _pause;
        static int _pauseLookAt;

        void Place()
        {
            if (_placed == Lane && _placedCentred == Centred) return;
            _placed = Lane;
            _placedCentred = Centred;
            _lane.SetParent(Centred ? _space : _root, false);
            // Top-left anchored, top-left pivot: resizing never shifts the lane (the old centre pivot pushed it off the left edge).
            _lane.anchorMin = _lane.anchorMax = new Vector2(0f, 1f);
            _lane.pivot = new Vector2(0f, 1f);
            _lane.anchoredPosition = new Vector2(Lane.x, -Lane.y);
            _lane.sizeDelta = new Vector2(Lane.width, Lane.height);
            float bodyH = Lane.height - HudZones.ChatInputH - 6f;
            MenuUi.Place(_body.rectTransform, 0f, 0f, Lane.width, bodyH);
            MenuUi.Place(_input.rectTransform, 0f, bodyH + 6f, Lane.width, HudZones.ChatInputH);
            _seenVersion = -1;
        }

        void Update()
        {
            ChatChannel ch = Channel;
            // online-rooms-6: never behind the match's stopped panel or the pause menu (nothing online outside a modal's panel).
            bool show = Visible && ch != null && OnlineMatch.InRoom && !OnlineMatchOverlay.ModalUp && !PauseOpen();
            if (_canvas.enabled != show) _canvas.enabled = show;
            if (!show) { StopTyping(); return; }
            Place();
            if (ch.Version != _seenVersion) Redraw(ch);
            Keyboard kb = Keyboard.current;
            if (kb != null && !Typing && kb.yKey.wasPressedThisFrame)
            {
                _scope = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed ? ChatScope.Team : ChatScope.All;
                StartTyping();
            }
            else if (kb != null && Typing)
            {
                if (kb.escapeKey.wasPressedThisFrame) StopTyping();
                else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Send(ch);
                else if (kb.backspaceKey.wasPressedThisFrame && _typed.Length > 0) { _typed = _typed.Substring(0, _typed.Length - 1); ShowTyped(); }
            }
            float age = Time.unscaledTime - _born;
            float alpha = Typing || OnlineMenuScreen.AnyOpen ? 1f : age < FadeSeconds ? 1f : Mathf.Clamp01(1f - (age - FadeSeconds) / FadeOut);
            Color c = _body.color;
            if (!Mathf.Approximately(c.a, alpha)) { c.a = alpha; _body.color = c; }
        }

        /// <summary>
        /// The newest lines that fit the box, oldest first. online-rooms-6: each line's wrapped height is measured on the
        /// box's own font and width (TextFit), and the oldest line is dropped whenever the next would not fit whole, so the
        /// box shows only whole lines (it used to estimate the wrap, overflow its top edge and cut the oldest line in half).
        /// </summary>
        void Redraw(ChatChannel ch)
        {
            _seenVersion = ch.Version;
            var lines = ch.Lines;
            int size = UiScale.TypeSmall;
            float width = Mathf.Max(40f, _placed.width);
            float lineH = Mathf.Max(1f, TextFit.LineHeightOf(_body, size));
            float bodyH = _placed.height - HudZones.ChatInputH - 6f;
            int room = Mathf.Min(MaxLines, Mathf.Max(1, Mathf.FloorToInt(bodyH / lineH)));
            var picked = new System.Collections.Generic.List<string>();
            int used = 0;
            for (int k = lines.Count - 1; k >= 0; k--)
            {
                ChatLine l = lines[k];
                string text = l.Scope == ChatScope.System ? "· " + l.Text : (l.Scope == ChatScope.Team ? "[TEAM] " : "") + l.From + ": " + l.Text;
                int need = Mathf.Max(1, Mathf.RoundToInt(TextFit.HeightOf(_body, text, size, width, true) / lineH));
                if (used + need > room) break;
                used += need;
                picked.Insert(0, text);
            }
            HudText.Set(_body, string.Join("\n", picked));
            _born = Time.unscaledTime;
        }

        void StartTyping()
        {
            if (Typing || Keyboard.current == null) return;
            Typing = true;
            _typed = "";
            _openedFrame = Time.frameCount;
            Keyboard.current.onTextInput += OnChar;
            ShowTyped();
        }

        void StopTyping()
        {
            if (!Typing) return;
            Typing = false;
            _closedFrame = Time.frameCount;
            if (Keyboard.current != null) Keyboard.current.onTextInput -= OnChar;
            HudText.Set(_input, "");
        }

        void OnChar(char c)
        {
            if (!Typing || char.IsControl(c) || _typed.Length >= ChatFilter.MaxLength) return;
            if (Time.frameCount <= _openedFrame + 1 && _typed.Length == 0 && (c == 'y' || c == 'Y')) return;   // the key press that opened chat
            _typed += c;
            ShowTyped();
        }

        void ShowTyped() => HudText.Set(_input, (_scope == ChatScope.Team ? "TO TEAM: " : "TO ALL: ") + _typed + "_");

        void Send(ChatChannel ch)
        {
            string t = _typed.Trim();
            StopTyping();
            if (t.Length == 0) return;
            if (t.StartsWith("/mute ") || t.StartsWith("/unmute "))
            {
                bool mute = t.StartsWith("/mute ");
                string who = ChatFilter.CleanName(t.Substring(mute ? 6 : 8));
                int peer = PeerNamed(who);
                ch.Notice(peer < 0 ? "No player called " + who + "." : (mute ? who + " is muted for you." : who + " can be heard again."), OnlineMatch.NowMs);
                if (peer >= 0) ch.SetMuted(peer, mute);
                return;
            }
            string why = ch.Say(t, _scope, OnlineMatch.NowMs);
            if (why != null) ch.Notice(why, OnlineMatch.NowMs);
        }

        static int PeerNamed(string name)
        {
            var members = OnlineMatch.Host != null ? OnlineMatch.Host.Members : OnlineMatch.Client?.Members;
            if (members == null) return -1;
            foreach (RoomMember m in members) if (string.Equals(m.Name, name, System.StringComparison.OrdinalIgnoreCase)) return m.Peer;
            return -1;
        }

        void OnDestroy()
        {
            StopTyping();
            if (_one == this) _one = null;
        }
    }
}
