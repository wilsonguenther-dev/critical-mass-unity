using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Hud.Interact;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Progression;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>
    /// Home lobby READY screen, between class select and the match: both rosters fill in row by row (your fighter, then
    /// every bot by its in-match callsign and class portrait, each tagged BOT), the map name, the objective in one line,
    /// then a 3 second countdown. Purely presentational and honest: the rosters are the real ones (World.BuildRoster with
    /// the same config, team size and class the match is created from; rosters do not depend on the seed), and nothing
    /// claims other people are online. START NOW (focused) or Enter / A skips the wait; Back / Escape / B goes back one
    /// level (class select, the mode cards or home, wherever the launch came from).
    /// </summary>
    public sealed class ReadyScreen : FlowScreen
    {
        public const int MaxRows = 16;
        public const float RevealStart = 0.1f, RevealStep = 0.045f, RevealFade = 0.15f, CountdownSeconds = 3f;
        const float PanelW = 700f, PanelH = 600f, PanelY = 226f, LeftX = 120f, RightX = 1100f, RowsTop = 300f, RowsH = 510f;

        static readonly string[] Digits = { "3", "2", "1", "GO" };
        static GameConfig _cfg;

        PlayLaunch _launch;
        bool _launched;
        float _shownAt;
        int _rowsPerSide;
        int _tier = 2;
        Text _title, _sub, _objective, _footer, _count, _countLabel;
        readonly Text[] _teamName = new Text[2], _teamRole = new Text[2];
        readonly RectTransform[,] _rows = new RectTransform[2, MaxRows];
        readonly CanvasGroup[,] _rowGroups = new CanvasGroup[2, MaxRows];
        readonly HudIcon[,] _icons = new HudIcon[2, MaxRows];
        readonly Text[,] _names = new Text[2, MaxRows], _classes = new Text[2, MaxRows], _tags = new Text[2, MaxRows];
        readonly string[,] _iconNames = new string[2, MaxRows];
        readonly ClassId[] _roster = new ClassId[MaxRows];
        MenuItem _start, _back;

        public MenuItem StartItem => _start;
        public MenuItem BackItem => _back;
        public PlayLaunch Launch => _launch;
        /// <summary>Rows shown per team (the team size).</summary>
        public int RowsPerSide => _rowsPerSide;
        public bool Launched => _launched;

        /// <summary>A row's name and tag (tests): side 0 = the player's team.</summary>
        public string RowName(int side, int i) => _names[side, i] != null ? _names[side, i].text : null;
        public string RowTag(int side, int i) => _tags[side, i] != null ? _tags[side, i].text : null;
        public bool RowShown(int side, int i) => _rows[side, i] != null && _rows[side, i].gameObject.activeSelf;

        protected override string HintKeys => "ENTER START NOW   ·   ESC BACK";
        protected override string HintPad => "A START NOW   ·   B BACK";

        protected override void BuildContent(RectTransform space)
        {
            _title = MenuUi.Label(space, "Title", CMTokens.Display, 56, Cream, TextAnchor.MiddleCenter, 360f, 36f, 1200f, 76f, "MATCH READY", 4f);
            _sub = MenuUi.Label(space, "Sub", CMTokens.Display, 26, Brass, TextAnchor.MiddleCenter, 160f, 112f, 1600f, 40f, "", 3f);
            _objective = MenuUi.Label(space, "Objective", CMTokens.Ui, 24, Cream, TextAnchor.MiddleCenter, 160f, 160f, 1600f, 40f, "", 2f);

            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? LeftX : RightX;
                MenuUi.Panel(space, side == 0 ? "Your Team" : "Enemy Team", x, PanelY, PanelW, PanelH);
                _teamName[side] = MenuUi.Label(space, "Team Name " + side, CMTokens.Display, 34, Cream, TextAnchor.MiddleLeft, x + 32f, PanelY + 18f,
                    PanelW * 0.5f, 48f, "", 3f);
                _teamRole[side] = MenuUi.Label(space, "Team Role " + side, CMTokens.Ui, 20, CMTokens.WithAlpha(Cream, 0.8f), TextAnchor.MiddleRight,
                    x + PanelW * 0.5f, PanelY + 18f, PanelW * 0.5f - 32f, 48f, "", 2f);
                for (int i = 0; i < MaxRows; i++) BuildRow(space, side, i, x);
            }

            MenuUi.Label(space, "Versus", CMTokens.Display, 44, Cream, TextAnchor.MiddleCenter, 820f, 380f, 280f, 60f, "VS", 4f);
            _count = MenuUi.Label(space, "Countdown", CMTokens.Display, 120, Brass, TextAnchor.MiddleCenter, 820f, 450f, 280f, 160f, "", 5f);
            _countLabel = MenuUi.Label(space, "Countdown Label", CMTokens.Ui, 22, Cream, TextAnchor.MiddleCenter, 820f, 612f, 280f, 34f, "", 2f);
            _footer = MenuUi.Label(space, "Footer", CMTokens.Ui, 22, CMTokens.WithAlpha(Cream, 0.9f), TextAnchor.MiddleCenter, 160f, 838f, 1600f, 34f, "", 2f);

            _back = Button(space, "Back", LeftX, 890f, 300f, 84f, "BACK", Back, 30);
            _start = Button(space, "Start Now", RightX + PanelW - 340f, 890f, 340f, 84f, "START NOW", StartNow, 34);
            _start.Body.RimPx = 4f;
            _start.Body.SetRim(Brass);
            _start.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = _back, selectOnRight = _back };
            _back.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = _start, selectOnRight = _start };
            DefaultFocus = _start;
        }

        void BuildRow(RectTransform space, int side, int i, float x)
        {
            RectTransform row = MenuUi.Rt(space, "Row " + side + "." + i);
            MenuUi.Place(row, x + 24f, RowsTop, PanelW - 48f, 30f);
            _rowGroups[side, i] = row.gameObject.AddComponent<CanvasGroup>();
            _rows[side, i] = row;
            _icons[side, i] = HudIcon.Make(row, "Portrait", 0f, 0f, 30f, 30f);
            _names[side, i] = MenuUi.Label(row, "Name", CMTokens.Display, 22, Cream, TextAnchor.MiddleLeft, 44f, 0f, 300f, 30f, "", 2f);
            _classes[side, i] = MenuUi.Label(row, "Class", CMTokens.Ui, 18, CMTokens.WithAlpha(Cream, 0.8f), TextAnchor.MiddleLeft, 356f, 0f, 200f, 30f, "", 2f);
            _tags[side, i] = MenuUi.Label(row, "Tag", CMTokens.Ui, 18, Brass, TextAnchor.MiddleRight, PanelW - 48f - 130f, 0f, 130f, 30f, "", 2f);
            row.gameObject.SetActive(false);
        }

        /// <summary>Fills the screen for a launch (rosters, words); the countdown starts when it shows.</summary>
        public void Bind(PlayLaunch launch)
        {
            EnsureBuilt();
            _launch = launch;
            _launched = false;
            if (launch == null) return;
            MatchSetupChoice c = launch.Choice;
            ModeInfo info = PlayModes.Info(c.Mode);
            if (_cfg == null) _cfg = GameConfig.CreateDefault();
            int n = World.ClampTeamSize(_cfg, c.TeamSize);
            if (n > MaxRows) n = MaxRows;
            _rowsPerSide = n;
            bool watching = c.Mode == MatchMode.Spectate;

            HudText.Set(_title, launch.Rematch ? "REMATCH READY" : watching ? "MATCH READY TO WATCH" : "MATCH READY");
            HudText.Set(_sub, (c.Mode == MatchMode.Custom ? "CUSTOM MATCH" : info.Title) + "   ·   " + PlayModes.MapName + "   ·   " +
                PlayModes.SizeLabel(n) + "   ·   " + MatchSpeed.Label(MatchSpeed.FromPace(c.Pace)));
            HudText.Set(_objective, Objective(c.Mode));
            HudText.Set(_footer, Footer(launch, watching));

            TeamId mine = c.Team, other = World.OtherTeam(c.Team);
            for (int side = 0; side < 2; side++)
            {
                TeamId team = side == 0 ? mine : other;
                _teamName[side].text = team == TeamId.Azure ? "AZURE" : "CRIMSON";
                _teamName[side].color = CMTokens.Team(team);
                _teamRole[side].text = watching ? (side == 0 ? "HOLDS YOUR FIGHTER" : "BOTS") : side == 0 ? "YOUR TEAM" : "ENEMY TEAM";
                World.BuildRoster(_cfg, n, side == 0 ? c.Class : (ClassId?)null, _roster);
                float step = Mathf.Min(64f, RowsH / n);
                float h = Mathf.Min(step - 4f, 52f);
                for (int i = 0; i < MaxRows; i++)
                {
                    bool on = i < n;
                    _rows[side, i].gameObject.SetActive(on);
                    if (!on) continue;
                    RectTransform row = _rows[side, i];
                    float x = side == 0 ? LeftX : RightX;
                    MenuUi.Place(row, x + 24f, RowsTop + i * step, PanelW - 48f, h);
                    ClassId cls = _roster[i];
                    _iconNames[side, i] = UiIcons.Portrait[(int)cls, (int)team];
                    RectTransform ir = _icons[side, i].Rt;
                    ir.sizeDelta = new Vector2(h, h);
                    ir.anchoredPosition = new Vector2(h * 0.5f, -h * 0.5f);
                    _icons[side, i].Set(_iconNames[side, i], _tier);
                    bool human = side == 0 && i == 0;
                    string name = human && !watching ? PlayerName() : Callsigns.ForSlot(team, i).ToUpperInvariant();
                    bool big = h >= 44f;
                    SetRowText(_names[side, i], name, h, big ? 28 : 22);
                    SetRowText(_classes[side, i], FlowWords.ClassName(cls), h, big ? 22 : 18);
                    SetRowText(_tags[side, i], human ? (watching ? "AUTOPILOT" : "YOU") : "BOT", h, big ? 22 : 18);
                    _names[side, i].color = human && !watching ? Brass : Cream;
                    _rowGroups[side, i].alpha = 0f;
                }
            }
        }

        static void SetRowText(Text t, string s, float h, int size)
        {
            t.text = s;
            t.fontSize = size;
            RectTransform r = t.rectTransform;
            r.sizeDelta = new Vector2(r.sizeDelta.x, h);
            r.anchoredPosition = new Vector2(r.anchoredPosition.x, -h * 0.5f);
        }

        static string PlayerName()
        {
            string n = Profile.Current != null ? Profile.Current.PlayerName : null;
            return string.IsNullOrEmpty(n) ? "YOU" : n.ToUpperInvariant();
        }

        /// <summary>The objective in one line for a mode.</summary>
        public static string Objective(MatchMode m)
        {
            switch (m)
            {
                case MatchMode.Spectate: return "Watch both teams fight for the hill. Most hill time at the final whistle wins.";
                case MatchMode.Practice: return "No pressure: the bots never target you. Learn the hill, the egg and your class.";
                case MatchMode.Training: return "Follow " + Hud.MascotGuide.MascotName + ". One step at a time, then a real fight.";
                default: return "Hold the hill with more fighters than they have. Feed the egg and the Kaiju fights for you.";
            }
        }

        static string Footer(PlayLaunch l, bool watching)
        {
            string s = watching
                ? "You are watching, not playing. Every fighter is a bot, your own one on autopilot."
                : "Offline match. Every fighter except you is a bot.";
            if (l.Rematch) s += "  The match you left cannot be resumed: this replays it from the start on the same seed.";
            return s;
        }

        protected override void OnShow()
        {
            _shownAt = Time.unscaledTime;
            HudText.Set(_count, "");
            HudText.Set(_countLabel, "");
            for (int side = 0; side < 2; side++)
                for (int i = 0; i < _rowsPerSide; i++) _rowGroups[side, i].alpha = 0f;
        }

        protected override void OnScale(float scale)
        {
            int tier = UiIcons.TierFor(scale);
            if (tier == _tier) return;
            _tier = tier;
            for (int side = 0; side < 2; side++)
                for (int i = 0; i < _rowsPerSide; i++)
                    if (_iconNames[side, i] != null) _icons[side, i].Set(_iconNames[side, i], _tier);
        }

        /// <summary>When the rows have all landed (seconds after Show).</summary>
        public float RevealEnd => RevealStart + RevealStep * Mathf.Max(0, _rowsPerSide - 1) + RevealFade;

        protected override void Tick()
        {
            if (_launch == null || _launched) return;
            float t = Time.unscaledTime - _shownAt;
            if (t <= RevealEnd + 0.05f)
            {
                for (int i = 0; i < _rowsPerSide; i++)
                {
                    float a = Mathf.Clamp01((t - RevealStart - RevealStep * i) / RevealFade);
                    for (int side = 0; side < 2; side++)
                        if (_rowGroups[side, i].alpha != a) _rowGroups[side, i].alpha = a;
                }
                return;
            }
            float c = t - RevealEnd;
            int digit = Mathf.Clamp((int)c, 0, Digits.Length - 1);
            HudText.Set(_count, Digits[digit]);
            HudText.Set(_countLabel, digit < 3 ? "STARTING" : "");
            if (c >= CountdownSeconds) StartNow();
        }

        /// <summary>START NOW, Enter / A, or the end of the countdown: the match (once).</summary>
        public void StartNow()
        {
            if (_launched || _launch == null) return;
            _launched = true;
            PlayFlow.Launch(_launch);
        }

        public override void Back()
        {
            if (_launched) return;
            PlayLaunch l = _launch;
            if (l == null || l.BackTo == LobbyScreen.Home)
            {
                Director.ShowMenu();
                return;
            }
            if (l.BackTo == LobbyScreen.ClassSelect)
            {
                PlayFlow.PickClass(Director, l.Choice.Mode);
                return;
            }
            Director.ShowModes(l.Choice.Mode);
        }
    }
}
