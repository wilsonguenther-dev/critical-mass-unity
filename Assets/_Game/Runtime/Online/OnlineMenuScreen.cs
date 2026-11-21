using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CriticalMass.Net;
using CriticalMass.Net.Relay;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Progression;
using CriticalMass.Sim.Config;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Online
{
    /// <summary>
    /// online-rooms-2: PLAY ONLINE. Three pages on one screen, each a fixed grid of boxes (1920 x 1080 reference units),
    /// every text contained in its own box (TextFit) so no two boxes ever overlap at any screen shape:
    ///   Home: CREATE ROOM, JOIN ROOM, BACK.
    ///   Join: type the 6-character code (keyboard; Ctrl or Cmd + V pastes), JOIN, BACK.
    ///   Room: the code (COPY CODE for the host), who is in (name, team, class, ready, ping), your TEAM and CLASS, READY
    ///         (joiners) or START (host, only when everyone is ready), LEAVE. Bots fill every empty seat.
    /// Every failure is one plain sentence in the status box. No service or vendor name appears anywhere.
    /// </summary>
    [MenuEntry("online", "PLAY ONLINE", 12)]
    public sealed class OnlineMenuScreen : FlowScreen, IMenuScreen, IMenuBack
    {
        enum Page { Home, Join, Busy, Room }


        Action _onClosed;
        Page _page = Page.Home;
        RectTransform _home, _join, _room;
        Text _title, _sub, _status, _code, _roomCode, _more, _startWhy, _codeHint;
        // b15-zero-cost: the join box also takes an address (192.168.1.5 or 1.2.3.4:47800); local-network rooms are listed.
        const int TypedMax = 21;
        const string CodeHintText = "Type the 6-character code from your friend's message, or press PASTE.";
        float _lanShownAt = -10f;
        bool _startWarned;
        MenuItem _create, _joinBtn, _backHome, _joinGo, _backJoin, _copy, _team, _class, _ready, _start, _leave;
        readonly List<Action> _refresh = new List<Action>();
        string _typed = "";
        Task<(string code, string key)> _creating;
        int _shownVersion = -1;
        TeamId _myTeam = TeamId.Azure;
        ClassId _myClass = ClassId.Warrior;
        bool _myReady;

        protected override string HintKeys => _page == Page.Join ? "TYPE THE CODE   ·   ENTER JOIN   ·   ESC BACK" : "ENTER CHOOSE   ·   ARROWS MOVE   ·   ESC BACK";
        protected override string HintPad => "A CHOOSE   ·   B BACK";

        public void Open(Action onClosed)
        {
            _onClosed = onClosed;
            if (Director == null) Director = FlowDirector.Current;
            EnsureBuilt();
            Show();
            Go(OnlineMatch.InRoom ? Page.Room : Page.Home);
        }

        public override void Back()
        {
            if (OnlineChatHud.PanelOpen) { OnlineChatHud.ClosePanelFromMenu(); return; }   // b17-chat: BACK closes the chat settings first
            if (OnlineChatHud.EatsKeys || OnlineChatHud.WheelOpen) return;   // ESC while typing (or the frame it closed) cancels the line only
            switch (_page)
            {
                case Page.Join: Go(Page.Home); return;
                case Page.Busy: _creating = null; OnlineMatch.Leave(); Go(Page.Home); return;
                case Page.Room: Leave(); return;
                default: Close(); return;
            }
        }

        void Close()
        {
            AnyOpen = false;
            StopNaming(false);
            OnlineChatHud.Visible = false;
            Hide();
            StopTyping();
            Action a = _onClosed;
            _onClosed = null;
            a?.Invoke();
        }

        /// <summary>online-rooms-5: true while the Play Online screen is up (the announcer and banners stay quiet).</summary>
        public static bool AnyOpen { get; private set; }

        const int NameMaxOnline = 12;
        readonly Text[,] _cells = new Text[OnlineLayout.MaxRows, OnlineLayout.CellCount];
        readonly Text[,] _heads = new Text[2, OnlineLayout.CellCount];
        bool? _tableTwo;

        /// <summary>
        /// online-rooms-6: places the table for one column (up to 16 players, the whole width) or two (17 to 32), with one
        /// fixed font size for every cell and head (no per-cell shrinking: a long name is cut with an ellipsis at its
        /// column). The second column and its head exist only in the two-column layout.
        /// </summary>
        void LayTable(bool two)
        {
            if (_tableTwo == two) return;
            _tableTwo = two;
            int size = OnlineLayout.RowSize(two);
            for (int col = 0; col < 2; col++)
                for (int c = 0; c < OnlineLayout.CellCount; c++)
                {
                    bool on = col == 0 || two;
                    Text h = _heads[col, c];
                    h.gameObject.SetActive(on);
                    if (on) Fix(h, OnlineLayout.HeadCell(col, c, two), size);
                }
            for (int i = 0; i < OnlineLayout.MaxRows; i++)
            {
                bool on = two || i < OnlineLayout.RowsPerColumn;
                for (int c = 0; c < OnlineLayout.CellCount; c++)
                {
                    Text t = _cells[i, c];
                    t.gameObject.SetActive(on);
                    if (on) Fix(t, OnlineLayout.Cell(i, c, two), size);
                }
            }
        }

        static void Fix(Text t, Rect r, int size)
        {
            MenuUi.Place(t.rectTransform, r.x, r.y, r.width, r.height);
            t.fontSize = size;
            ContainedText.Attach(t, size, size, 1);   // one size; too long = the ellipsis, never a smaller font
        }
        Text _count, _nameText;
        MenuItem _name, _paste;
        bool _naming { get => s_naming; set { s_naming = value; if (!value) s_namingEnded = Time.frameCount; } }
        static bool s_naming;
        static int s_namingEnded = -10;
        /// <summary>b17-chat: the room's name field is being typed in (or closed this frame): its letters are not chat keys.</summary>
        public static bool TypingName => s_naming || Time.frameCount <= s_namingEnded + 1;
        string _nameTyped = "";

        protected override void BuildContent(RectTransform space)
        {
            // A dim scrim over the attract match, then one navy panel; every element sits in its OnlineLayout region.
            VectorShape scrim = MenuUi.Shape(space, "Scrim", ShapeKind.RoundRect, -2000f, -2000f, OnlineLayout.RefW + 4000f, OnlineLayout.RefH + 4000f,
                CMTokens.WithAlpha(CMTokens.VoidNavy.Color, 0.62f), 0f);
            scrim.OutlinePx = 0f;
            scrim.RimPx = 0f;
            Rect P = OnlineLayout.Panel;
            MenuUi.Panel(space, "Panel", P.x, P.y, P.width, P.height);
            _title = L(space, "Title", OnlineLayout.Title, CMTokens.Display, UiScale.TypeDisplay, Cream, TextAnchor.MiddleLeft, "PLAY ONLINE", 1);
            _sub = L(space, "Sub", OnlineLayout.Sub, CMTokens.Ui, UiScale.TypeBody, CMTokens.WithAlpha(Cream, 0.85f), TextAnchor.MiddleLeft, "", 1);
            _status = L(space, "Status", OnlineLayout.Status, CMTokens.Ui, UiScale.TypeBody, Brass, TextAnchor.MiddleLeft, "", 2);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;

            // Home: your name (chosen once, filtered), CREATE ROOM, JOIN ROOM, BACK.
            _home = MenuUi.Rt(space, "Home");
            MenuUi.Stretch(_home);
            L(_home, "Name Label", OnlineLayout.NameLabel, CMTokens.Ui, UiScale.TypeSmall, CMTokens.WithAlpha(Cream, 0.85f), TextAnchor.MiddleLeft,
                "YOUR NAME (OTHERS SEE IT)", 1);
            _name = Btn(_home, "Name", OnlineLayout.NameBox, "", ToggleNaming, UiScale.TypeHeading);
            _nameText = _name.Label;
            _create = Btn(_home, "Create Room", OnlineLayout.CreateButton, "CREATE ROOM", CreateRoom, UiScale.TypeHeading);
            _joinBtn = Btn(_home, "Join Room", OnlineLayout.JoinButton, "JOIN ROOM", () => Go(Page.Join), UiScale.TypeHeading);
            _backHome = Btn(_home, "Back", OnlineLayout.Back, "BACK", Close, UiScale.TypeHeading);
            ChainVertical(new List<MenuItem> { _name, _create, _joinBtn, _backHome });

            // Join: the code, PASTE, JOIN, BACK.
            _join = MenuUi.Rt(space, "Join");
            MenuUi.Stretch(_join);
            L(_join, "Code Label", OnlineLayout.CodeLabel, CMTokens.Ui, UiScale.TypeSmall, CMTokens.WithAlpha(Cream, 0.85f), TextAnchor.MiddleLeft, "ROOM CODE", 1);
            _code = L(_join, "Code", OnlineLayout.CodeBig, CMTokens.Display, UiScale.TypeHero, Cream, TextAnchor.MiddleLeft, "", 1);
            _codeHint = L(_join, "Code Hint", OnlineLayout.CodeHint, CMTokens.Ui, UiScale.TypeBody, CMTokens.WithAlpha(Cream, 0.85f), TextAnchor.MiddleLeft,
                CodeHintText, 1);
            _paste = Btn(_join, "Paste", OnlineLayout.PasteButton, "PASTE", PasteCode, UiScale.TypeHeading);
            _joinGo = Btn(_join, "Join", OnlineLayout.JoinGo, "JOIN", JoinTyped, UiScale.TypeHeading);
            _backJoin = Btn(_join, "Back", OnlineLayout.Back, "BACK", () => Go(Page.Home), UiScale.TypeHeading);
            ChainVertical(new List<MenuItem> { _paste, _joinGo, _backJoin });

            // Room: header code + COPY CODE + count; the player table; TEAM, CLASS, START/READY; the chat box; LEAVE.
            _room = MenuUi.Rt(space, "Room");
            MenuUi.Stretch(_room);
            _roomCode = L(_room, "Room Code", OnlineLayout.Code, CMTokens.Display, UiScale.TypeTitle, Cream, TextAnchor.MiddleRight, "", 1);
            _copy = Btn(_room, "Copy Code", OnlineLayout.Copy, "COPY CODE", CopyCode, UiScale.TypeButton);
            _count = L(_room, "Count", OnlineLayout.Count, CMTokens.Ui, UiScale.TypeBody, Brass, TextAnchor.MiddleRight, "", 1);
            string[] heads = { "PLAYER", "TEAM", "CLASS", "READY", "PING" };
            for (int col = 0; col < 2; col++)
                for (int c = 0; c < OnlineLayout.CellCount; c++)
                    _heads[col, c] = L(_room, "Head " + col + c, OnlineLayout.HeadCell(col, c, true), CMTokens.Ui, UiScale.TypeMin, CMTokens.WithAlpha(Cream, 0.7f),
                        OnlineLayout.CellAnchor(c), heads[c], 1);
            for (int i = 0; i < OnlineLayout.MaxRows; i++)
                for (int c = 0; c < OnlineLayout.CellCount; c++)
                    _cells[i, c] = L(_room, "Cell " + i + "." + c, OnlineLayout.Cell(i, c, true), CMTokens.Ui, UiScale.TypeMin, Cream, OnlineLayout.CellAnchor(c), "", 1);
            var probeRoot = new GameObject("Online Table Probe");   // online-rooms-8: columns sized on the real font
            try { OnlineLayout.MeasureWith(HudText.Make(probeRoot.transform, "Probe", CMTokens.Ui, UiScale.TypeMin, Color.white, TextAnchor.MiddleLeft, 2f)); }
            finally { if (Application.isPlaying) Destroy(probeRoot); else DestroyImmediate(probeRoot); }
            _tableTwo = null;
            LayTable(false);
            _more = _count;
            _team = Stepper(_room, "Team", OnlineLayout.Team.x, OnlineLayout.Team.y, OnlineLayout.Team.width, OnlineLayout.Team.height, "TEAM",
                () => _myTeam == TeamId.Azure ? "AZURE" : "CRIMSON", StepTeam, _refresh);
            _class = Stepper(_room, "Class", OnlineLayout.Class.x, OnlineLayout.Class.y, OnlineLayout.Class.width, OnlineLayout.Class.height, "CLASS",
                () => _myClass.ToString().ToUpperInvariant(), StepClass, _refresh);
            _ready = Btn(_room, "Ready", OnlineLayout.StartButton, "READY", ToggleReady, UiScale.TypeHeading);
            _start = Btn(_room, "Start", OnlineLayout.StartButton, "START MATCH", StartMatch, UiScale.TypeHeading);
            _startWhy = L(_room, "Start Why", OnlineLayout.StartWhy, CMTokens.Ui, UiScale.TypeSmall, CMTokens.WithAlpha(Cream, 0.85f), TextAnchor.UpperLeft, "", 2);
            _startWhy.horizontalOverflow = HorizontalWrapMode.Wrap;
            Rect ch = OnlineLayout.Chat;
            VectorShape chatBox = MenuUi.Shape(_room, "Chat Box", ShapeKind.RoundRect, ch.x, ch.y, ch.width, ch.height, CMTokens.WithAlpha(CMTokens.VoidNavy.Color, 0.75f), 12f);
            chatBox.OutlinePx = 2f;
            _leave = Btn(_room, "Leave", OnlineLayout.Leave, "LEAVE ROOM", Leave, UiScale.TypeHeading);
            Btn(_room, "Chat Settings", OnlineLayout.ChatSettings, "CHAT SETTINGS", OnlineChatHud.ToggleSettings, UiScale.TypeBody);   // b17-chat
            DefaultFocus = _create;
            Go(Page.Home);
        }

        static Text L(Transform parent, string name, Rect r, Typeface face, int size, Color c, TextAnchor anchor, string text, int lines)
        {
            Text t = MenuUi.Label(parent, name, face, size, c, anchor, r.x, r.y, r.width, r.height, text, 2f);
            t.resizeTextMaxSize = size; // b11-af-2 follow-up: shrink-only, never past the designed size
            t.resizeTextMinSize = ContainedText.FloorFor(size);
            MenuUi.Contain(t, lines);
            return t;
        }

        static MenuItem Btn(Transform parent, string name, Rect r, string text, Action act, int size) => Button(parent, name, r.x, r.y, r.width, r.height, text, act, size);

        protected override void OnShow() { base.OnShow(); AnyOpen = true; }

        public override void Hide() { AnyOpen = false; OnlineChatHud.Visible = false; base.Hide(); }   // online-rooms-6: the room's chat box goes with the room screen

        void Go(Page p)
        {
            _page = p;
            AnyOpen = true;
            if (_home == null) return;
            _home.gameObject.SetActive(p == Page.Home);
            _join.gameObject.SetActive(p == Page.Join);
            _room.gameObject.SetActive(p == Page.Room);
            if (p != Page.Home) StopNaming(false);
            if (p == Page.Join) StartTyping(); else StopTyping();
            if (p != Page.Join) OnlineMatch.StopLanListening();   // b15-zero-cost: listen for local rooms only on the join page
            if (p == Page.Join) { _typed = ""; ShowCode(); }
            if (p == Page.Room) { _shownVersion = -1; RefreshRoom(); }
            if (p == Page.Home) ShowName();
            // Chat lives in its own bordered box on the room page (the match moves it to the HUD lane).
            OnlineChatHud.Lane = OnlineLayout.ChatLane;
            OnlineChatHud.Visible = p == Page.Room;
            OnlineChatHud.Centred = true;
            HudText.Set(_sub, p == Page.Room ? "Y chats to everyone, Shift + Y to your team. Bots fill every empty seat."
                : p == Page.Join ? "Join a friend's room with the code they sent you."
                : "Play one match with friends on other computers. Bots fill every empty seat.");
            if (p == Page.Home) SetStatus(OnlineConfig.Configured ? "" : "Online play is not switched on in this build yet.");
            HudText.Set(_title, p == Page.Room ? (OnlineMatch.Host != null ? "YOUR ROOM" : "ROOM") : p == Page.Join ? "JOIN A ROOM" : p == Page.Busy ? "CONNECTING..." : "PLAY ONLINE");
            MenuItem focus = p == Page.Home ? _create : p == Page.Join ? _joinGo : p == Page.Room ? (OnlineMatch.Host != null ? _start : _ready) : null;
            if (focus != null) Focus(focus);
        }

        // ---------------------------------------------------------------- name (chosen once, filtered like chat)

        void ShowName()
        {
            string n = _naming ? _nameTyped + "_" : ChatFilter.CleanName(PlayerName());
            HudText.Set(_nameText, _naming ? n : n + "   (CHANGE)");
        }

        void ToggleNaming()
        {
            if (_naming) { StopNaming(true); return; }
            if (Keyboard.current == null) return;
            _naming = true;
            _nameTyped = "";
            Keyboard.current.onTextInput += OnNameChar;
            if (UnityEngine.EventSystems.EventSystem.current != null) UnityEngine.EventSystems.EventSystem.current.sendNavigationEvents = false;
            SetStatus("Type your name, then press ENTER (letters, numbers, - and _; up to 12).");
            ShowName();
        }

        void OnNameChar(char c)
        {
            if (!_naming || char.IsControl(c) || _nameTyped.Length >= NameMaxOnline) return;
            char u = char.ToUpperInvariant(c);
            if ((u >= 'A' && u <= 'Z') || (u >= '0' && u <= '9') || u == '-' || u == '_' || (u == ' ' && _nameTyped.Length > 0)) { _nameTyped += u; ShowName(); }
        }

        void StopNaming(bool save)
        {
            if (!_naming) return;
            _naming = false;
            if (Keyboard.current != null) Keyboard.current.onTextInput -= OnNameChar;
            if (UnityEngine.EventSystems.EventSystem.current != null) UnityEngine.EventSystems.EventSystem.current.sendNavigationEvents = true;
            if (save && _nameTyped.Trim().Length > 0 && Profile.Current != null)
            {
                string clean = PlayerProfile.CleanName(_nameTyped.Trim());
                if (clean == PlayerProfile.DefaultName && _nameTyped.Trim().ToUpperInvariant() != PlayerProfile.DefaultName)
                    SetStatus("That name is not allowed. Pick another one.");
                else
                {
                    Profile.Current.PlayerName = clean.Length > NameMaxOnline ? clean.Substring(0, NameMaxOnline).TrimEnd() : clean;
                    Profile.Commit();
                    SetStatus("Others will see you as " + Profile.Current.PlayerName + ".");
                }
            }
            else if (!save) SetStatus("");
            ShowName();
        }

        void PasteCode()
        {
            string pasted = RelayTransport.CleanCode(GUIUtility.systemCopyBuffer);
            string raw = (GUIUtility.systemCopyBuffer ?? "").Trim();
            if (pasted == null && raw.Length <= TypedMax && DirectAddress.TryParse(raw, out _, out _)) pasted = raw;   // b15-zero-cost: an address
            if (pasted == null) { SetStatus("The copied text is not a room code. Copy the 6-character code and try again."); return; }
            _typed = pasted;
            ShowCode();
            SetStatus("Code pasted. Press JOIN.");
        }

        void SetStatus(string s) => HudText.Set(_status, s ?? "");

        /// <summary>online-rooms-4, -onlineShots only: show a page as it would be, with a status line and a typed code.</summary>
        public void CapturePage(string page, string status = null, string typed = null)
        {
            Page p = page == "join" ? Page.Join : page == "busy" ? Page.Busy : page == "room" ? Page.Room : Page.Home;
            if (typed != null) { _typed = typed; ShowCode(); }
            if (p == Page.Room) _shownVersion = -1;
            Go(p);
            if (p == Page.Room) RefreshRoom();
            if (status != null) SetStatus(status);
        }

        /// <summary>online-rooms-4, -onlineShots only: the joiner's READY state for the capture.</summary>
        public void CaptureReady(bool ready)
        {
            _myReady = ready;
            HudText.Set(_ready.Label, ready ? "NOT READY" : "READY");
        }

        // ---------------------------------------------------------------- create / join

        void CreateRoom()
        {
            if (!OnlineConfig.Configured) { SetStatus("Online play is not switched on in this build yet."); return; }
            SetStatus("Making a room...");
            Go(Page.Busy);
            _creating = RelayTransport.CreateRoomAsync(OnlineConfig.RelayUrl, CancellationToken.None);
        }

        void JoinTyped()
        {
            string code = RelayTransport.CleanCode(_typed);
            if (code == null && DirectAddress.TryParse(_typed, out string host, out int port))
            {
                // b15-zero-cost: straight to the host's computer, no room service at all.
                OnlineMatch.JoinRoom(DirectClient.Connect(host, port, 5000), null, PlayerName());
                SetStatus("Connecting to " + _typed.Trim() + "...");
                Go(Page.Room);
                return;
            }
            if (code == null) { SetStatus("A room code is 6 letters and numbers, for example K7Q2PF. Or type an address, for example 192.168.1.5."); return; }
            ITransport t;
            if (OnlineMatch.LanGames().Exists(g => g.Code == code))
            {
                // b15-zero-cost: the room is on this network: join it directly (free); the room service only if that fails.
                LanGame g = OnlineMatch.LanGames().Find(x => x.Code == code);
                string url = OnlineConfig.RelayUrl;
                t = new FallbackTransport(DirectClient.Connect(g.Address, g.Port, 2000),
                    OnlineConfig.Configured ? () => (ITransport)RelayTransport.Connect(url, code, null) : (Func<ITransport>)null);
            }
            else
            {
                if (!OnlineConfig.Configured) { SetStatus("Online play is not switched on in this build yet."); return; }
                t = RelayTransport.Connect(OnlineConfig.RelayUrl, code, null);
            }
            OnlineMatch.JoinRoom(t, code, PlayerName());
            SetStatus("Joining room " + code + "...");
            Go(Page.Room);
        }

        static string PlayerName()
        {
            string n = Profile.Current != null ? Profile.Current.PlayerName : null;
            return string.IsNullOrEmpty(n) ? "PLAYER" : n;
        }

        void CopyCode()
        {
            if (string.IsNullOrEmpty(OnlineMatch.Code))
            {
                // b15-zero-cost: a room for network friends only: copy the address they type.
                System.Collections.Generic.List<string> a = DirectAddress.LocalAddresses();
                if (OnlineMatch.Direct == null || a.Count == 0) return;
                GUIUtility.systemCopyBuffer = a[0] + (OnlineMatch.Direct.Port != DirectHost.DefaultPort ? ":" + OnlineMatch.Direct.Port : "");
                SetStatus("Address copied. Friends on your network choose JOIN ROOM and type it.");
                return;
            }
            GUIUtility.systemCopyBuffer = OnlineMatch.Code;
            SetStatus("Code copied. Send it to your friend.");
        }

        // ---------------------------------------------------------------- room

        void StepTeam(int d)
        {
            _myTeam = _myTeam == TeamId.Azure ? TeamId.Crimson : TeamId.Azure;
            SendPick();
        }

        void StepClass(int d)
        {
            int n = ClassIds.All.Count;
            _myClass = (ClassId)((((int)_myClass + (d < 0 ? -1 : 1)) % n + n) % n);
            SendPick();
        }

        void SendPick()
        {
            if (OnlineMatch.Host != null) OnlineMatch.Host.SetLocalPick(_myTeam, _myClass);
            else OnlineMatch.Client?.SetPick(_myTeam, _myClass);
            foreach (Action r in _refresh) r();
        }

        void ToggleReady()
        {
            if (OnlineMatch.Client == null) return;
            _myReady = !_myReady;
            OnlineMatch.Client.SetReady(_myReady);
            HudText.Set(_ready.Label, _myReady ? "NOT READY" : "READY");
        }

        void StartMatch()
        {
            // b15-zero-cost: when the day's online allowance is nearly used, say so once before starting (a second press
            // starts anyway: players who came by code may be cut off if the room service stops; network players are not).
            if (!_startWarned && OnlineMatch.Code != null && OnlineMatch.RelayUsedToday >= 0 && OnlineMatch.Host != null
                && RelayBudget.MatchesLeft(Math.Max(2, OnlineMatch.Host.Members.Count), OnlineMatch.RelayUsedToday) < 1)
            {
                _startWarned = true;
                SetStatus(RelayBudget.HostLine(OnlineMatch.Host.Members.Count, OnlineMatch.RelayUsedToday) + " Press START again to play anyway.");
                return;
            }
            _startWarned = false;
            string why = OnlineMatch.StartAsHost();
            if (why != null) SetStatus(why);
        }

        void Leave()
        {
            OnlineMatch.Leave();
            _myReady = false;
            HudText.Set(_ready.Label, "READY");
            Go(Page.Home);
        }

        void RefreshRoom()
        {
            bool host = OnlineMatch.Host != null;
            List<RoomMember> members = host ? OnlineMatch.Host.Members : OnlineMatch.Client != null ? OnlineMatch.Client.Members : null;
            int version = host ? OnlineMatch.Host.Version : OnlineMatch.Client != null ? OnlineMatch.Client.Version : 0;
            _copy.gameObject.SetActive(host);
            _start.gameObject.SetActive(host);
            _ready.gameObject.SetActive(!host);
            ChainVertical(host ? new List<MenuItem> { _team, _class, _start, _copy, _leave } : new List<MenuItem> { _team, _class, _ready, _leave });
            if (version == _shownVersion) return;
            _shownVersion = version;
            HudText.Set(_roomCode, OnlineMatch.Code != null ? "ROOM  " + Spaced(OnlineMatch.Code) : host ? "ON YOUR NETWORK" : "");
            int shown = 0, count = members != null ? members.Count : 0, readyCount = 0;
            var confirmed = new List<RoomMember>(count);
            if (members != null) foreach (RoomMember rm in members) if (rm.Confirmed) confirmed.Add(rm);
            LayTable(OnlineLayout.TwoColumns(confirmed.Count));
            for (int i = 0; i < OnlineLayout.MaxRows; i++)
            {
                RoomMember m = i < confirmed.Count ? confirmed[i] : null;
                bool isHostRow = m != null && m.Peer == Peers.Host;
                if (m != null) { shown++; if (m.Ready || isHostRow) readyCount++; }
                HudText.Set(_cells[i, 0], m == null ? "" : ChatFilter.CleanName(m.Name));
                HudText.Set(_cells[i, 1], m == null ? "" : m.Team == TeamId.Azure ? "AZURE" : "CRIMSON");
                _cells[i, 1].color = m != null && m.Team == TeamId.Crimson ? CMTokens.CrimsonInk.Color : CMTokens.Azure.Color;
                HudText.Set(_cells[i, 2], m == null ? "" : m.Class.ToString().ToUpperInvariant());
                HudText.Set(_cells[i, 3], m == null ? "" : isHostRow ? "HOST" : m.Ready ? "READY" : "NOT READY");
                _cells[i, 3].color = m != null && !isHostRow && !m.Ready ? CMTokens.WithAlpha(Cream, 0.6f) : Brass;
                HudText.Set(_cells[i, 4], m == null || isHostRow ? "" : Math.Min(OnlineLayout.MaxPingShown, m.PingMs) + " MS");
            }
            int seats = 2 * (host ? OnlineMatch.Host.TeamSize : OnlineMatch.Client != null ? Math.Max(1, OnlineMatch.Client.TeamSize) : 16);
            HudText.Set(_count, $"{readyCount} OF {shown} READY   ·   {Math.Max(0, seats - shown)} BOTS");
            if (host)
            {
                bool ok = OnlineMatch.Host.CanStart(out string why);
                _start.interactable = ok;
                string ways = OnlineMatch.HostWaysLine();   // b15-zero-cost: the free ways in and the day's online allowance
                HudText.Set(_startWhy, (ok ? "Everyone is ready. Bots take the other seats." : why) + (ways.Length > 0 ? " " + ways : ""));
                // Joins and leaves are chat notices (the chat box); the status line keeps the instruction or a real problem.
                string notice = OnlineMatch.Host.Notice;
                bool roster = string.IsNullOrEmpty(notice) || notice.EndsWith(" joined.") || notice.EndsWith(" left the room.");
                SetStatus(roster ? (OnlineMatch.Code != null ? "Send the code to your friends. They choose JOIN ROOM and type it."
                    : "Online rooms are not available right now. Friends on your network choose JOIN ROOM and type your address.") : notice);
            }
            else if (OnlineMatch.Client != null)
            {
                HudText.Set(_startWhy, "The host starts the match when everyone is ready.");
                if (OnlineMatch.Client.Phase == RoomPhase.Lobby)
                    SetStatus(_myReady ? "You are ready. Waiting for the host to start the match." : "You are in. Pick a team and a class, then press READY.");
                else if (OnlineMatch.Client.Phase == RoomPhase.Connecting || OnlineMatch.Retrying || OnlineMatch.WillRetry)
                    SetStatus(OnlineMatch.Code != null ? "Connecting to room " + Spaced(OnlineMatch.Code) + "..." : "Connecting...");
            }
        }

        /// <summary>K7Q2PF as "K7Q 2PF": easier to read aloud and to type from a text message (typing accepts either).</summary>
        public static string Spaced(string code) => code != null && code.Length == 6 ? code.Substring(0, 3) + " " + code.Substring(3) : code;

        static string Row(RoomMember m, bool isHost)
        {
            string team = m.Team == TeamId.Azure ? "AZURE" : "CRIMSON";
            string ready = isHost ? "HOST" : m.Ready ? "READY" : "NOT READY";
            string ping = isHost ? "" : "   " + m.PingMs + " MS";
            return m.Name.ToUpperInvariant() + "   ·   " + team + "   ·   " + m.Class.ToString().ToUpperInvariant() + "   ·   " + ready + ping;
        }

        protected override void Tick()
        {
            if (_page == Page.Busy && _creating != null && _creating.IsCompleted)
            {
                Task<(string code, string key)> t = _creating;
                _creating = null;
                if (t.IsFaulted || t.IsCanceled)
                {
                    Exception e = t.Exception != null ? t.Exception.GetBaseException() : null;
                    // b15-zero-cost: no online room (offline, or the room service is full for the day): open a room that
                    // friends on this network, or who type this computer's address, can still join.
                    try
                    {
                        OnlineMatch.HostRoom(OnlineMatch.HostTransport(null), null, PlayerName());
                        Go(Page.Room);
                        SetStatus("Online rooms are not available right now. Friends on your network choose JOIN ROOM and type your address.");
                    }
                    catch (RoomException)
                    {
                        SetStatus(e is RoomException ? e.Message : "Could not make a room. Check your internet connection.");
                        Go(Page.Home);
                    }
                    return;
                }
                (string code, string key) = t.Result;
                RelayTransport tr = RelayTransport.Connect(OnlineConfig.RelayUrl, code, key);
                OnlineMatch.HostRoom(OnlineMatch.HostTransport(tr), code, PlayerName());
                SetStatus("Send the code to your friend. They choose JOIN ROOM and type it.");
                Go(Page.Room);
                return;
            }
            if (_page == Page.Room)
            {
                if (OnlineMatch.Client != null && OnlineMatch.Client.Phase == RoomPhase.Closed && !OnlineMatch.Retrying && !OnlineMatch.WillRetry)
                {
                    string why = OnlineMatch.Client.Error;
                    OnlineMatch.Leave();
                    Go(Page.Home);
                    SetStatus(why);
                    return;
                }
                if (OnlineMatch.Host != null && OnlineMatch.Host.Phase == RoomPhase.Closed)
                {
                    string why = OnlineMatch.Host.Notice;
                    OnlineMatch.Leave();
                    Go(Page.Home);
                    SetStatus(why);
                    return;
                }
                if (!OnlineMatch.InRoom) { Go(Page.Home); return; }
                if (OnlineMatch.Session != null) { AnyOpen = false; Hide(); return; }   // the match is loading
                RefreshRoom();
            }
            if (_naming)
            {
                Keyboard nk = Keyboard.current;
                if (nk != null)
                {
                    if (nk.enterKey.wasPressedThisFrame || nk.numpadEnterKey.wasPressedThisFrame) StopNaming(true);
                    else if (nk.escapeKey.wasPressedThisFrame) StopNaming(false);
                    else if (nk.backspaceKey.wasPressedThisFrame && _nameTyped.Length > 0) { _nameTyped = _nameTyped.Substring(0, _nameTyped.Length - 1); ShowName(); }
                }
                return;
            }
            if (_page == Page.Join && Time.unscaledTime - _lanShownAt > 0.5f)
            {
                // b15-zero-cost: rooms on this network, in the hint line (typing their code joins them directly, for free).
                _lanShownAt = Time.unscaledTime;
                List<LanGame> lan = OnlineMatch.LanGames();
                HudText.Set(_codeHint, lan.Count == 0 ? CodeHintText
                    : "On your network: " + ChatFilter.CleanName(lan[0].Name) + "'s room" + (string.IsNullOrEmpty(lan[0].Code) ? ", address " + lan[0].Address : ", code " + Spaced(lan[0].Code))
                      + (lan.Count > 1 ? " and " + (lan.Count - 1) + " more" : "") + ". Type the code or the address.");
            }
            if (_page == Page.Join)
            {
                Keyboard kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb.backspaceKey.wasPressedThisFrame && _typed.Length > 0) { _typed = _typed.Substring(0, _typed.Length - 1); ShowCode(); }
                    bool mod = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed || kb.leftCommandKey.isPressed || kb.rightCommandKey.isPressed;
                    if (mod && kb.vKey.wasPressedThisFrame)
                    {
                        string pasted = RelayTransport.CleanCode(GUIUtility.systemCopyBuffer);
                        if (pasted != null) { _typed = pasted; ShowCode(); }
                    }
                    if ((kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                        && (RelayTransport.CleanCode(_typed) != null || DirectAddress.TryParse(_typed, out _, out _))) JoinTyped();
                }
            }
        }

        // ---------------------------------------------------------------- code typing

        bool _typing;

        void StartTyping()
        {
            if (_typing || Keyboard.current == null) return;
            _typing = true;
            Keyboard.current.onTextInput += OnChar;
        }

        void StopTyping()
        {
            if (!_typing) return;
            _typing = false;
            if (Keyboard.current != null) Keyboard.current.onTextInput -= OnChar;
        }

        /// <summary>b15-zero-cost: typed text that can only be an address (a dot, a colon, or 0 / 1, which codes never use).</summary>
        static bool LooksLikeAddress(string t) => t.IndexOf('.') >= 0 || t.IndexOf(':') >= 0 || t.IndexOf('0') >= 0 || t.IndexOf('1') >= 0;

        void OnChar(char c)
        {
            if (_page != Page.Join) return;
            char u = char.ToUpperInvariant(c);
            bool addrChar = (u >= '0' && u <= '9') || u == '.' || u == ':';
            if (RoomCodes.Alphabet.IndexOf(u) < 0 && !addrChar) return;
            string next = _typed + u;
            if (next.Length > (LooksLikeAddress(next) ? TypedMax : RoomCodes.Length)) return;
            if (LooksLikeAddress(next) && !addrChar) return;   // an address is digits, dots and a colon
            _typed = next;
            ShowCode();
        }

        void ShowCode()
        {
            if (LooksLikeAddress(_typed)) { HudText.Set(_code, _typed + "_"); SetStatus(""); return; }
            var cs = new char[RoomCodes.Length * 2 - 1];
            for (int i = 0; i < RoomCodes.Length; i++)
            {
                cs[i * 2] = i < _typed.Length ? _typed[i] : '_';
                if (i < RoomCodes.Length - 1) cs[i * 2 + 1] = ' ';
            }
            HudText.Set(_code, new string(cs));
            if (_typed.Length > 0) SetStatus("");
        }

        void OnDestroy() => StopTyping();
    }
}
