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

        const float X0 = 152f, ColR = 1300f, ColRW = 470f, RowY = 300f, RowH = 54f, Rows = 8;

        Action _onClosed;
        Page _page = Page.Home;
        RectTransform _home, _join, _room;
        Text _title, _sub, _status, _code, _roomCode, _more, _startWhy;
        readonly Text[] _rows = new Text[(int)Rows];
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
            OnlineChatHud.Visible = false;
            Hide();
            StopTyping();
            Action a = _onClosed;
            _onClosed = null;
            a?.Invoke();
        }

        protected override void BuildContent(RectTransform space)
        {
            _title = MenuUi.Label(space, "Title", CMTokens.Display, UiScale.TypeDisplay, Cream, TextAnchor.MiddleLeft, X0, 52f, 1100f, 70f, "PLAY ONLINE", 4f);
            MenuUi.Contain(_title, 1);
            _sub = MenuUi.Label(space, "Sub", CMTokens.Ui, UiScale.TypeBody, CMTokens.WithAlpha(Cream, 0.85f), TextAnchor.MiddleLeft, X0, 122f, 1500f, 34f,
                "Play one match with friends on other computers. Bots fill every empty seat.", 2f);
            MenuUi.Contain(_sub, 1);
            _status = MenuUi.Label(space, "Status", CMTokens.Ui, UiScale.TypeBody, Brass, TextAnchor.UpperLeft, X0, 772f, 1100f, 80f, "", 2f);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            MenuUi.Contain(_status, 2);

            // Home
            _home = MenuUi.Rt(space, "Home");
            MenuUi.Stretch(_home);
            _create = Button(_home, "Create Room", X0, 220f, 620f, 96f, "CREATE ROOM", CreateRoom, UiScale.TypeHeading);
            _joinBtn = Button(_home, "Join Room", X0, 336f, 620f, 96f, "JOIN ROOM", () => Go(Page.Join), UiScale.TypeHeading);
            _backHome = Button(_home, "Back", X0, 870f, 300f, 84f, "BACK", Close, UiScale.TypeHeading);
            ChainVertical(new List<MenuItem> { _create, _joinBtn, _backHome });

            // Join
            _join = MenuUi.Rt(space, "Join");
            MenuUi.Stretch(_join);
            MenuUi.Label(_join, "Code Label", CMTokens.Ui, UiScale.TypeSmall, CMTokens.WithAlpha(Cream, 0.85f), TextAnchor.MiddleLeft, X0, 204f, 900f, 30f,
                "ROOM CODE", 2f);
            _code = MenuUi.Label(_join, "Code", CMTokens.Display, UiScale.TypeHero, Cream, TextAnchor.MiddleLeft, X0, 236f, 1000f, 130f, "", 6f);
            MenuUi.Contain(_code, 1);
            Text howto = MenuUi.Label(_join, "Code Hint", CMTokens.Ui, UiScale.TypeBody, CMTokens.WithAlpha(Cream, 0.85f), TextAnchor.MiddleLeft, X0, 380f, 1400f, 34f,
                "Type the 6-character code your friend sees on their screen. Ctrl or Cmd + V pastes it.", 2f);
            MenuUi.Contain(howto, 1);
            _joinGo = Button(_join, "Join", X0, 440f, 420f, 90f, "JOIN", JoinTyped, UiScale.TypeHeading);
            _backJoin = Button(_join, "Back", X0, 870f, 300f, 84f, "BACK", () => Go(Page.Home), UiScale.TypeHeading);
            ChainVertical(new List<MenuItem> { _joinGo, _backJoin });

            // Room
            _room = MenuUi.Rt(space, "Room");
            MenuUi.Stretch(_room);
            _roomCode = MenuUi.Label(_room, "Room Code", CMTokens.Display, UiScale.TypeTitle, Cream, TextAnchor.MiddleLeft, X0, 186f, 760f, 90f, "", 4f);
            MenuUi.Contain(_roomCode, 1);
            _copy = Button(_room, "Copy Code", 940f, 196f, 300f, 74f, "COPY CODE", CopyCode, UiScale.TypeButton);
            for (int i = 0; i < _rows.Length; i++)
            {
                _rows[i] = MenuUi.Label(_room, "Member " + i, CMTokens.Ui, UiScale.TypeBody, Cream, TextAnchor.MiddleLeft, X0, RowY + i * RowH, 1080f, RowH - 6f, "", 2f);
                MenuUi.Contain(_rows[i], 1);
            }
            _more = MenuUi.Label(_room, "More", CMTokens.Ui, UiScale.TypeSmall, CMTokens.WithAlpha(Cream, 0.8f), TextAnchor.MiddleLeft, X0, RowY + Rows * RowH, 1080f, 30f, "", 2f);
            MenuUi.Contain(_more, 1);
            _team = Stepper(_room, "Team", ColR, RowY, ColRW, 72f, "TEAM", () => _myTeam == TeamId.Azure ? "AZURE" : "CRIMSON", StepTeam, _refresh);
            _class = Stepper(_room, "Class", ColR, RowY + 90f, ColRW, 72f, "CLASS", () => _myClass.ToString().ToUpperInvariant(), StepClass, _refresh);
            _ready = Button(_room, "Ready", ColR, RowY + 190f, ColRW, 84f, "READY", ToggleReady, UiScale.TypeHeading);
            _start = Button(_room, "Start", ColR, RowY + 190f, ColRW, 84f, "START MATCH", StartMatch, UiScale.TypeHeading);
            _startWhy = MenuUi.Label(_room, "Start Why", CMTokens.Ui, UiScale.TypeSmall, CMTokens.WithAlpha(Cream, 0.85f), TextAnchor.UpperLeft,
                ColR, RowY + 286f, ColRW, 60f, "", 2f);
            _startWhy.horizontalOverflow = HorizontalWrapMode.Wrap;
            MenuUi.Contain(_startWhy, 2);
            _leave = Button(_room, "Leave", X0, 870f, 300f, 84f, "LEAVE ROOM", Leave, UiScale.TypeHeading);
            DefaultFocus = _create;
            Go(Page.Home);
        }

        void Go(Page p)
        {
            _page = p;
            if (_home == null) return;
            _home.gameObject.SetActive(p == Page.Home);
            _join.gameObject.SetActive(p == Page.Join);
            _room.gameObject.SetActive(p == Page.Room);
            if (p == Page.Join) StartTyping(); else StopTyping();
            if (p == Page.Join) { _typed = ""; ShowCode(); }
            if (p == Page.Room) { _shownVersion = -1; RefreshRoom(); }
            // Chat lives in the room page's right column, under START / READY (its own box; the match moves it to its HUD lane).
            OnlineChatHud.Lane = new Rect(ColR, RowY + 356f, ColRW, HudZones.ChatLines * HudZones.ChatLineH + 6f + HudZones.ChatInputH);
            OnlineChatHud.Visible = p == Page.Room;
            OnlineChatHud.Centred = true;
            HudText.Set(_sub, p == Page.Room ? "Y chats to everyone, Shift + Y to your team. Bots fill every empty seat."
                : "Play one match with friends on other computers. Bots fill every empty seat.");
            if (p == Page.Home) SetStatus(OnlineConfig.Configured ? "" : "Online play is not switched on in this build yet.");
            HudText.Set(_title, p == Page.Room ? (OnlineMatch.Host != null ? "YOUR ROOM" : "ROOM") : p == Page.Join ? "JOIN A ROOM" : p == Page.Busy ? "CONNECTING..." : "PLAY ONLINE");
            MenuItem focus = p == Page.Home ? _create : p == Page.Join ? _joinGo : p == Page.Room ? (OnlineMatch.Host != null ? _start : _ready) : null;
            if (focus != null) Focus(focus);
        }

        void SetStatus(string s) => HudText.Set(_status, s ?? "");

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
            if (!OnlineConfig.Configured) { SetStatus("Online play is not switched on in this build yet."); return; }
            string code = RelayTransport.CleanCode(_typed);
            if (code == null) { SetStatus("A room code is 6 letters and numbers, for example K7Q2PF."); return; }
            RelayTransport t = RelayTransport.Connect(OnlineConfig.RelayUrl, code, null);
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
            if (string.IsNullOrEmpty(OnlineMatch.Code)) return;
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
            HudText.Set(_roomCode, OnlineMatch.Code != null ? "ROOM  " + OnlineMatch.Code : "");
            int shown = 0, count = members != null ? members.Count : 0;
            for (int i = 0; i < _rows.Length; i++)
            {
                RoomMember m = members != null && i < count ? members[i] : null;
                if (m != null && !m.Confirmed) m = null;
                HudText.Set(_rows[i], m == null ? "" : Row(m, i == 0));
                if (m != null) shown++;
            }
            HudText.Set(_more, count > _rows.Length ? "and " + (count - _rows.Length) + " more" : "");
            if (host)
            {
                bool ok = OnlineMatch.Host.CanStart(out string why);
                _start.interactable = ok;
                HudText.Set(_startWhy, ok ? "Everyone is ready. Bots take the other seats." : why);
                SetStatus(string.IsNullOrEmpty(OnlineMatch.Host.Notice) ? "Send the code to your friend. They choose JOIN ROOM and type it." : OnlineMatch.Host.Notice);
            }
            else if (OnlineMatch.Client != null)
            {
                HudText.Set(_startWhy, "The host starts the match when everyone is ready.");
                if (OnlineMatch.Client.Phase == RoomPhase.Lobby) SetStatus("You are in. Pick a team and a class, then press READY.");
            }
        }

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
                    SetStatus(e is RoomException ? e.Message : "Could not make a room. Check your internet connection.");
                    Go(Page.Home);
                    return;
                }
                (string code, string key) = t.Result;
                RelayTransport tr = RelayTransport.Connect(OnlineConfig.RelayUrl, code, key);
                OnlineMatch.HostRoom(tr, code, PlayerName());
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
                if (OnlineMatch.Session != null) { Hide(); return; }   // the match is loading
                RefreshRoom();
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
                    if ((kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) && _typed.Length == RoomCodes.Length) JoinTyped();
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

        void OnChar(char c)
        {
            if (_page != Page.Join || _typed.Length >= RoomCodes.Length) return;
            char u = char.ToUpperInvariant(c);
            if (RoomCodes.Alphabet.IndexOf(u) < 0) return;
            _typed += u;
            ShowCode();
        }

        void ShowCode()
        {
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
