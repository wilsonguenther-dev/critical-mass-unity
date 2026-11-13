using System.Collections.Generic;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Spectator;
using CriticalMass.Sim.Config;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>
    /// DG-01 match setup: team (Azure or Crimson), size (16 v 16 or 5 v 5), speed (Relaxed 0.75x, Standard 1x, Fast 2x;
    /// MatchSpeed, default Standard), bot difficulty (DG-21), and the five class cards (portrait in the chosen team's colour, name, role line, attack and ability names,
    /// difficulty pips; words from DG-04's pack when present). Demolitions and Builder show as coming soon until their
    /// items add them to Playable. Moving the focus onto a card picks it; A / Enter on a card jumps to Start. START MATCH
    /// creates the world with a new seed (GameFlow.BeginMatch). Back: the main menu (or the results, for Change class).
    /// The last setup is remembered between launches (SetupPrefs, which Play Now reads too).
    /// <para>2026-11-12: the team line has a third choice beside the two teams, SPECTATE: the match starts with your fighter
    /// playing itself and the free spectator camera (SpectatorMode.SpectateNextMatch); START MATCH reads WATCH MATCH.</para>
    /// </summary>
    public sealed class MatchSetup : FlowScreen
    {
        /// <summary>Classes the sim can play in this build; later items (DG-22, DG-24) add theirs.</summary>
        public static readonly HashSet<ClassId> Playable = new HashSet<ClassId>
        {
            ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder, ClassId.Healer,   // batch 3: DG-22, DG-24, DG-40
        };

        /// <summary>Cards on the class row: every class the sim has (six since DG-40).</summary>
        public static int CardCount => ClassIds.All.Count;

        public const float CardW = 250f, CardH = 350f, CardGap = 16f, CardsY = 470f;
        /// <summary>Inner padding of the class name on a card (the layout test measures names against CardW - 2 * NamePad).</summary>
        public const float NamePad = 10f;

        /// <summary>Back goes to the results (Change class) instead of the main menu.</summary>
        public bool FromResults;

        public MatchSetupChoice Choice;

        /// <summary>SPECTATE is chosen on the team line (the next match starts in spectator mode).</summary>
        public bool Spectate { get; private set; }

        MenuItem _team, _size, _pace, _bots, _start, _back;
        readonly MenuItem[] _cards = new MenuItem[CardCount];
        readonly HudIcon[] _portraits = new HudIcon[CardCount];
        readonly Text[][] _cardTexts = new Text[CardCount][];
        HudIcon _crest;
        readonly List<System.Action> _refresh = new List<System.Action>();
        int _tier = 2;
        static double _matchSeconds = -1;

        public MenuItem TeamItem => _team;
        public MenuItem SizeItem => _size;
        public MenuItem PaceItem => _pace;
        public MenuItem StartItem => _start;
        public MenuItem BackItem => _back;
        public MenuItem Card(ClassId c) => _cards[(int)c];

        protected override string HintKeys => "ENTER SELECT   ·   LEFT / RIGHT CHANGE   ·   ESC BACK";
        protected override string HintPad => "A SELECT   ·   LEFT / RIGHT CHANGE   ·   B BACK";

        protected override void BuildContent(RectTransform space)
        {
            MenuUi.Label(space, "Title", CMTokens.Display, UiScale.TypeDisplay, Cream, TextAnchor.MiddleCenter, 460f, 40f, 1000f, 80f, "MATCH SETUP", 4f);
            MenuUi.Panel(space, "Panel", 140f, 140f, 1640f, 860f);

            const float sx = 460f, sw = 1000f, sh = 58f, step = 70f, sy = 170f;
            _team = Stepper(space, "Team", sx, sy, sw, sh, "TEAM",
                () => Spectate ? "SPECTATE" : Choice.Team == TeamId.Azure ? "AZURE" : "CRIMSON", StepTeam, _refresh);
            _crest = HudIcon.Make(_team.transform, "Crest", sw * 0.42f - 92f, 7f, 44f, 44f);
            _size = Stepper(space, "Size", sx, sy + step, sw, sh, "MATCH SIZE", () => Choice.TeamSize <= 5 ? "5 V 5" : "16 V 16",
                d => Choice.TeamSize = Choice.TeamSize <= 5 ? 16 : 5, _refresh);
            _pace = Stepper(space, "Pace", sx, sy + 2 * step, sw, sh, "SPEED", PaceText,
                d => Choice.Pace = MatchSpeed.PaceOf(MatchSpeed.Step(MatchSpeed.FromPace(Choice.Pace), d)), _refresh);
            // DG-21: the enemy bots' difficulty (Easy, Normal, Hard); allies and the backdrop stay Normal.
            _bots = Stepper(space, "Bots", sx, sy + 3 * step, sw, sh, "ENEMY BOTS", BotText, StepBots, _refresh);

            int n = CardCount;
            float total = n * CardW + (n - 1) * CardGap;
            float x0 = 960f - total * 0.5f;
            for (int i = 0; i < n; i++) BuildCard(space, ClassIds.All[i], x0 + i * (CardW + CardGap));

            _back = Button(space, "Back", 200f, 880f, 380f, 80f, "BACK", Back);
            _start = Button(space, "Start", 1340f, 880f, 380f, 80f, "START MATCH", StartMatch, UiScale.TypeLead);
            _start.Body.RimPx = 4f;
            _start.Body.SetRim(Brass);
            Wire();
            DefaultFocus = _start;
        }

        void BuildCard(RectTransform space, ClassId c, float x)
        {
            int i = (int)c;
            bool live = Playable.Contains(c);
            MenuItem card = MenuUi.Item(space, "Card " + c, x, CardsY, CardW, CardH, "", MenuItem.Kind.Button, UiScale.TypeHeading, true, CMTokens.Display);
            card.Body.Radius = 18f;
            card.Body.RimPx = 2f;
            ClassId pick = c;
            // Integration batch 2: A / Enter on a card opens DG-10's full class screen (abilities, glyphs, turntable).
            card.Submitted = () => OpenClassDetail(pick);
            _cards[i] = card;
            _portraits[i] = HudIcon.Make(card.transform, "Portrait", (CardW - 150f) * 0.5f, 20f, 150f, 150f);
            // 2026-11-12: best-fit never shrank this (the overflow was unbounded), so DEMOLITIONS EXPERT spilled past the
            // card. MenuUi.Fit bounds it: one line when it fits, else two lines at a smaller size, inside the card's padding.
            Text name = MenuUi.Label(card.transform, "Name", CMTokens.Display, UiScale.TypeHeading, Cream, TextAnchor.MiddleCenter,
                NamePad, 174f, CardW - NamePad * 2f, 54f, FlowWords.ClassName(c), 3f);
            MenuUi.Fit(name, UiScale.TypeSmall);
            Text role = MenuUi.Fit(MenuUi.Label(card.transform, "Role", CMTokens.Ui, UiScale.TypeBody, Cream, TextAnchor.MiddleCenter, 10f, 230f,
                CardW - 20f, 32f, FlowWords.ClassRole(c), 2f), UiScale.TypeMin);
            Text abil = MenuUi.Fit(MenuUi.Label(card.transform, "Abilities", CMTokens.Ui, UiScale.TypeMin, Cream, TextAnchor.MiddleCenter, 10f, 264f,
                CardW - 20f, 28f, FlowWords.AbilityLine(c), 2f), UiScale.TypeMin);
            _cardTexts[i] = new[] { name, role, abil };
            if (live)
            {
                int diff = FlowWords.Difficulty(c);
                for (int k = 0; k < 3; k++)
                {
                    VectorShape pip = MenuUi.Shape(card.transform, "Pip " + k, ShapeKind.Diamond, CardW * 0.5f - 46f + k * 34f, 304f, 24f, 24f,
                        k < diff ? Brass : CMTokens.MeterEmpty.Color, 4f);
                    pip.RimPx = 0f;
                    pip.OutlinePx = 2f;
                }
            }
            else
            {
                card.interactable = false;
                card.Submitted = null;
                MenuUi.Label(card.transform, "Soon", CMTokens.Ui, UiScale.TypeSmall, Brass, TextAnchor.MiddleCenter, 0f, 300f, CardW, 32f, "COMING SOON", 2f);
                for (int k = 0; k < 3; k++) _cardTexts[i][k].color = CMTokens.WithAlpha(Cream, 0.45f);
            }
        }

        static readonly CriticalMass.Sim.Core.BotDifficulty[] BotSteps =
        {
            CriticalMass.Sim.Core.BotDifficulty.Easy, CriticalMass.Sim.Core.BotDifficulty.Normal, CriticalMass.Sim.Core.BotDifficulty.Hard,
        };

        string BotText()
        {
            switch (Choice.BotDifficulty)
            {
                case CriticalMass.Sim.Core.BotDifficulty.Easy: return "EASY   ·   SLOW TO REACT, MISS THEIR THROWS";
                case CriticalMass.Sim.Core.BotDifficulty.Hard: return "HARD   ·   QUICK, FOCUS CARRIERS AND THE HURT";
                default: return "NORMAL";
            }
        }

        void StepBots(int d)
        {
            int at = System.Array.IndexOf(BotSteps, Choice.BotDifficulty);
            if (at < 0) at = 1;
            int k = BotSteps.Length;
            Choice.BotDifficulty = BotSteps[((at + (d < 0 ? -1 : 1)) % k + k) % k];
        }

        /// <summary>Tests and the capture walk: pick the enemy bots' difficulty.</summary>
        public void SetBotDifficulty(CriticalMass.Sim.Core.BotDifficulty d)
        {
            Choice.BotDifficulty = d;
            RefreshAll();
        }

        public MenuItem BotsItem => _bots;

        /// <summary>The speed line: "STANDARD · 1X · 10:00 MATCH" (the match length in real minutes at that speed).</summary>
        string PaceText()
        {
            if (_matchSeconds <= 0) _matchSeconds = GameConfig.CreateDefault().MatchSeconds;
            MatchSpeedChoice c = MatchSpeed.FromPace(Choice.Pace);
            return MatchSpeed.Label(c) + "   ·   " + MatchSpeed.Multiplier(c) + "   ·   " +
                   NumberCache.Mmss(MatchSpeed.ToReal(_matchSeconds, MatchSpeed.PaceOf(c))) + " MATCH";
        }

        /// <summary>The speed chosen on the SPEED line.</summary>
        public MatchSpeedChoice Speed => MatchSpeed.FromPace(Choice.Pace);

        public void SetSpeed(MatchSpeedChoice c)
        {
            Choice.Pace = MatchSpeed.PaceOf(c);
            RefreshAll();
        }

        /// <summary>Explicit navigation: steppers in a column, the card row, then Back / Start.</summary>
        void Wire()
        {
            var cards = new List<MenuItem>();
            for (int i = 0; i < CardCount; i++)
                if (_cards[i].interactable) cards.Add(_cards[i]);
            MenuItem firstCard = cards.Count > 0 ? cards[0] : _start;
            Nav(_team, _start, _size, null, null);
            Nav(_size, _team, _pace, null, null);
            Nav(_pace, _size, _bots, null, null);
            Nav(_bots, _pace, firstCard, null, null);
            for (int i = 0; i < cards.Count; i++)
                Nav(cards[i], _bots, _start, cards[(i + cards.Count - 1) % cards.Count], cards[(i + 1) % cards.Count]);
            for (int i = 0; i < CardCount; i++)
                if (!_cards[i].interactable) _cards[i].navigation = new Navigation { mode = Navigation.Mode.None };
            Nav(_back, firstCard, _team, _start, _start);
            Nav(_start, firstCard, _team, _back, _back);
        }

        static void Nav(MenuItem it, Selectable up, Selectable down, Selectable left, Selectable right)
        {
            it.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = up,
                selectOnDown = down,
                selectOnLeft = left,
                selectOnRight = right,
            };
        }

        /// <summary>The card row's up and the buttons' up go to the chosen card.</summary>
        void RewireToChosen()
        {
            MenuItem chosen = _cards[(int)Choice.Class];
            if (chosen == null || !chosen.interactable) return;
            SetUp(_start, chosen);
            SetUp(_back, chosen);
            Navigation n = _bots.navigation;
            n.selectOnDown = chosen;
            _bots.navigation = n;
        }

        static void SetUp(MenuItem it, Selectable up)
        {
            Navigation n = it.navigation;
            n.selectOnUp = up;
            it.navigation = n;
        }

        protected override void OnShow()
        {
            Choice = SetupPrefs.EnsureLoaded();
            Choice.Mode = MatchMode.Custom;   // home lobby: the setup screen is the CUSTOM mode (standard rules)
            if (!Playable.Contains(Choice.Class)) Choice.Class = ClassId.Warrior;
            if (Choice.TeamSize != 5) Choice.TeamSize = 16;
            Choice.Pace = Choice.Pace > 0 ? MatchSpeed.Snap(Choice.Pace) : MatchSpeed.DefaultPace;
            Spectate = SpectatorMode.SpectateNextMatch;
            RefreshAll();
        }

        public void SetTeam(TeamId t)
        {
            Choice.Team = t;
            Spectate = false;
            RefreshAll();
        }

        /// <summary>SPECTATE on the team line: watch the match (actor 0 plays itself on the Azure side).</summary>
        public void SetSpectate(bool on)
        {
            Spectate = on;
            if (on) Choice.Team = TeamId.Azure;
            RefreshAll();
        }

        /// <summary>The team line cycles AZURE, CRIMSON, SPECTATE (right) or back (left).</summary>
        void StepTeam(int d)
        {
            int i = Spectate ? 2 : Choice.Team == TeamId.Azure ? 0 : 1;
            i = (i + (d < 0 ? 2 : 1)) % 3;
            if (i == 2) SetSpectate(true);
            else SetTeam(i == 0 ? TeamId.Azure : TeamId.Crimson);
        }

        public void SetClass(ClassId c)
        {
            if (!Playable.Contains(c)) return;
            Choice.Class = c;
            RefreshAll();
        }

        public void SetSize(int teamSize)
        {
            Choice.TeamSize = teamSize <= 5 ? 5 : 16;
            RefreshAll();
        }

        public void SetPace(double pace)
        {
            Choice.Pace = MatchSpeed.Snap(pace);
            RefreshAll();
        }

        void RefreshAll()
        {
            if (_team == null) return;
            for (int i = 0; i < _refresh.Count; i++) _refresh[i]();
            int t = (int)Choice.Team;
            _crest.Set(UiIcons.Crest[t], _tier);
            _crest.Image.enabled = !Spectate;
            if (_start != null) HudText.Set(_start.Label, Spectate ? "WATCH MATCH" : "START MATCH");
            for (int i = 0; i < CardCount; i++)
            {
                _portraits[i].Set(UiIcons.Portrait[i, t], _tier);
                bool chosen = i == (int)Choice.Class;
                VectorShape body = _cards[i].Body;
                body.SetRim(chosen ? Brass : Cream);
                body.RimPx = chosen ? 5f : 2f;
                body.SetVerticesDirty();
                if (!_cards[i].interactable) _portraits[i].Image.color = CMTokens.WithAlpha(Color.white, 0.4f);
            }
            RewireToChosen();
        }

        protected override void OnScale(float scale)
        {
            int tier = UiIcons.TierFor(scale);
            if (tier == _tier) return;
            _tier = tier;
            RefreshAll();
        }

        protected override void Tick()
        {
            // Focus on a playable card picks it; focused cards draw navy text on the brass fill.
            MenuItem f = Focused;
            for (int i = 0; i < CardCount; i++)
            {
                if (!_cards[i].interactable) continue;
                bool focus = f == _cards[i];
                if (focus && (int)Choice.Class != i) SetClass((ClassId)i);
                Color c = focus ? Navy : Cream;
                Text[] ts = _cardTexts[i];
                for (int k = 0; k < ts.Length; k++)
                {
                    if (ts[k].color == c) continue;
                    ts[k].color = c;
                    TextOutline o = ts[k].GetComponent<TextOutline>();
                    if (o != null) o.enabled = !focus;
                    ts[k].SetVerticesDirty();
                }
            }
        }

        /// <summary>
        /// Opens DG-10's class screen on c over this one (this screen hides so one screen owns Back). Confirming there
        /// picks the class and returns here with START focused; backing out returns with the choice unchanged.
        /// </summary>
        public void OpenClassDetail(ClassId c)
        {
            SetClass(c);
            GameFlow.LastSetup = Choice;                       // Show() reads the choice back from here
            SpectatorMode.SpectateNextMatch = Spectate;
            bool fromResults = FromResults;
            ClassSelect.SelectedClass = Choice.Class;
            Hide();
            ClassSelect.ShowSetup(Choice.Team, picked => ReturnFromClassDetail(picked, fromResults),
                () => ReturnFromClassDetail(null, fromResults));
        }

        void ReturnFromClassDetail(ClassId? picked, bool fromResults)
        {
            if (picked.HasValue && Playable.Contains(picked.Value))
            {
                MatchSetupChoice s = GameFlow.LastSetup;
                s.Class = picked.Value;
                GameFlow.LastSetup = s;
            }
            FromResults = fromResults;
            Show();
            Focus(_start);
        }

        public void StartMatch()
        {
            ClassSelect.SelectedClass = Choice.Class;          // DG-10 reads it for the respawn strip and the pause menu

            Choice.Mode = MatchMode.Custom;
            SetupPrefs.Save(Choice);
            SpectatorMode.SpectateNextMatch = Spectate;
            GameFlow.BeginMatch(Choice);
        }

        public override void Back()
        {
            if (FromResults) Director.ReturnToResults();
            else Director.ShowModes(MatchMode.Custom);   // home lobby: back onto the CUSTOM card that opened setup
        }
    }
}
