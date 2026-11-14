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
        public const float FadeSeconds = 10f, FadeOut = 1.5f;

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
        readonly Text[] _lines = new Text[HudZones.ChatLines];
        readonly float[] _born = new float[HudZones.ChatLines];
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

        /// <summary>online-offline-cost-1: removes the chat HUD and its canvas (when the player leaves the room).</summary>
        public static void Drop()
        {
            if (_one != null)
            {
                if (Application.isPlaying) Destroy(_one.gameObject);
                else DestroyImmediate(_one.gameObject);
            }
            _one = null;
            Typing = false;
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
            for (int i = 0; i < _lines.Length; i++)
            {
                _lines[i] = MenuUi.Label(_lane, "Line " + i, CMTokens.Ui, UiScale.TypeSmall, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft,
                    0f, i * HudZones.ChatLineH, 100f, HudZones.ChatLineH - 4f, "", 2f);
                MenuUi.Contain(_lines[i], 1);
            }
            _input = MenuUi.Label(_lane, "Typing", CMTokens.Ui, UiScale.TypeSmall, CMTokens.ToolBrass.Color, TextAnchor.MiddleLeft,
                0f, HudZones.ChatLines * HudZones.ChatLineH + 6f, 100f, HudZones.ChatInputH, "", 2f);
            MenuUi.Contain(_input, 1);
            _placed = new Rect(-1, -1, 0, 0);
        }

        void Place()
        {
            if (_placed == Lane && _placedCentred == Centred) return;
            _placed = Lane;
            _placedCentred = Centred;
            _lane.SetParent(Centred ? _space : _root, false);
            _lane.anchorMin = _lane.anchorMax = new Vector2(0f, 1f);
            _lane.pivot = new Vector2(0f, 1f);
            _lane.anchoredPosition = new Vector2(Lane.x, -Lane.y);
            _lane.sizeDelta = new Vector2(Lane.width, Lane.height);
            float w = Lane.width;
            for (int i = 0; i < _lines.Length; i++) _lines[i].rectTransform.sizeDelta = new Vector2(w, HudZones.ChatLineH - 4f);
            _input.rectTransform.sizeDelta = new Vector2(w, HudZones.ChatInputH);
        }

        void Update()
        {
            using var __ab = CriticalMass.Runtime.Game.AllocBy.Upd(this);
            ChatChannel ch = Channel;
            bool show = Visible && ch != null && OnlineMatch.InRoom;
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
            float now = Time.unscaledTime;
            for (int i = 0; i < _lines.Length; i++)
            {
                float age = now - _born[i];
                float a = Typing ? 1f : age < FadeSeconds ? 1f : Mathf.Clamp01(1f - (age - FadeSeconds) / FadeOut);
                Color c = _lines[i].color;
                if (!Mathf.Approximately(c.a, a)) { c.a = a; _lines[i].color = c; }
            }
        }

        void Redraw(ChatChannel ch)
        {
            _seenVersion = ch.Version;
            var lines = ch.Lines;
            int n = Mathf.Min(_lines.Length, lines.Count);
            for (int i = 0; i < _lines.Length; i++)
            {
                int k = lines.Count - n + i;
                if (i >= n) { HudText.Set(_lines[i], ""); continue; }
                ChatLine l = lines[k];
                string s = l.Scope == ChatScope.System ? l.Text : (l.Scope == ChatScope.Team ? "[TEAM] " : "") + l.From + ": " + l.Text;
                if (ContainedText.SourceOf(_lines[i]) != s) { HudText.Set(_lines[i], s); _born[i] = Time.unscaledTime; }
                _lines[i].color = l.Scope == ChatScope.System ? CMTokens.ToolBrass.Color : CMTokens.WarmCream.Color;
            }
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
