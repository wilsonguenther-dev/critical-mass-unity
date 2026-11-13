using System.Collections.Generic;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Config;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>
    /// DG-01 match setup: team (Azure or Crimson), size (16 v 16 or 5 v 5), pace (2x or 1x), bot difficulty (coming soon,
    /// DG-21), and the five class cards (portrait in the chosen team's colour, name, role line, attack and ability names,
    /// difficulty pips; words from DG-04's pack when present). Demolitions and Builder show as coming soon until their
    /// items add them to Playable. Moving the focus onto a card picks it; A / Enter on a card jumps to Start. START MATCH
    /// creates the world with a new seed (GameFlow.BeginMatch). Back: the main menu (or the results, for Change class).
    /// The last setup is remembered between launches (PlayerPrefs).
    /// </summary>
    public sealed class MatchSetup : FlowScreen
    {
        /// <summary>Classes the sim can play in this build; later items (DG-22, DG-24) add theirs.</summary>
        public static readonly HashSet<ClassId> Playable = new HashSet<ClassId> { ClassId.Warrior, ClassId.Farmer, ClassId.Miner };

        const string PrefTeam = "cm.flow.setup.team", PrefClass = "cm.flow.setup.class", PrefSize = "cm.flow.setup.size",
            PrefPace = "cm.flow.setup.pace";

        public const float CardW = 296f, CardH = 350f, CardGap = 22f, CardsY = 470f;

        /// <summary>Back goes to the results (Change class) instead of the main menu.</summary>
        public bool FromResults;

        public MatchSetupChoice Choice;

        MenuItem _team, _size, _pace, _bots, _start, _back;
        readonly MenuItem[] _cards = new MenuItem[5];
        readonly HudIcon[] _portraits = new HudIcon[5];
        readonly Text[][] _cardTexts = new Text[5][];
        HudIcon _crest;
        readonly List<System.Action> _refresh = new List<System.Action>();
        static bool _prefsLoaded;
        int _tier = 2;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => _prefsLoaded = false;

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
            MenuUi.Label(space, "Title", CMTokens.Display, 56, Cream, TextAnchor.MiddleCenter, 460f, 40f, 1000f, 80f, "MATCH SETUP", 4f);
            MenuUi.Panel(space, "Panel", 140f, 140f, 1640f, 860f);

            const float sx = 460f, sw = 1000f, sh = 58f, step = 70f, sy = 170f;
            _team = Stepper(space, "Team", sx, sy, sw, sh, "TEAM", () => Choice.Team == TeamId.Azure ? "AZURE" : "CRIMSON",
                d => SetTeam(Choice.Team == TeamId.Azure ? TeamId.Crimson : TeamId.Azure), _refresh);
            _crest = HudIcon.Make(_team.transform, "Crest", sw * 0.42f - 92f, 7f, 44f, 44f);
            _size = Stepper(space, "Size", sx, sy + step, sw, sh, "MATCH SIZE", () => Choice.TeamSize <= 5 ? "5 V 5" : "16 V 16",
                d => Choice.TeamSize = Choice.TeamSize <= 5 ? 16 : 5, _refresh);
            _pace = Stepper(space, "Pace", sx, sy + 2 * step, sw, sh, "PACE", PaceText,
                d => Choice.Pace = Choice.Pace >= 2 ? 1 : 2, _refresh);
            _bots = Stepper(space, "Bots", sx, sy + 3 * step, sw, sh, "BOTS", () => "NORMAL", d => { }, _refresh);
            MarkComingSoon(_bots, sw, sh);

            float total = 5 * CardW + 4 * CardGap;
            float x0 = 960f - total * 0.5f;
            for (int i = 0; i < 5; i++) BuildCard(space, (ClassId)i, x0 + i * (CardW + CardGap));

            _back = Button(space, "Back", 200f, 880f, 380f, 80f, "BACK", Back);
            _start = Button(space, "Start", 1340f, 880f, 380f, 80f, "START MATCH", StartMatch, 36);
            _start.Body.RimPx = 4f;
            _start.Body.SetRim(Brass);
            Wire();
            DefaultFocus = _start;
        }

        void BuildCard(RectTransform space, ClassId c, float x)
        {
            int i = (int)c;
            bool live = Playable.Contains(c);
            MenuItem card = MenuUi.Item(space, "Card " + c, x, CardsY, CardW, CardH, "", MenuItem.Kind.Button, 30, true, CMTokens.Display);
            card.Body.Radius = 18f;
            card.Body.RimPx = 2f;
            ClassId pick = c;
            card.Submitted = () =>
            {
                SetClass(pick);
                Focus(_start);
            };
            _cards[i] = card;
            _portraits[i] = HudIcon.Make(card.transform, "Portrait", (CardW - 150f) * 0.5f, 20f, 150f, 150f);
            Text name = MenuUi.Label(card.transform, "Name", CMTokens.Display, 30, Cream, TextAnchor.MiddleCenter, 0f, 180f, CardW, 44f,
                FlowWords.ClassName(c), 3f);
            Text role = MenuUi.Label(card.transform, "Role", CMTokens.Ui, 22, Cream, TextAnchor.MiddleCenter, 10f, 226f, CardW - 20f, 32f,
                FlowWords.ClassRole(c), 2f);
            Text abil = MenuUi.Label(card.transform, "Abilities", CMTokens.Ui, 17, Cream, TextAnchor.MiddleCenter, 10f, 262f, CardW - 20f, 28f,
                FlowWords.AbilityLine(c), 2f);
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
                MenuUi.Label(card.transform, "Soon", CMTokens.Ui, 18, Brass, TextAnchor.MiddleCenter, 0f, 300f, CardW, 32f, "COMING SOON", 2f);
                for (int k = 0; k < 3; k++) _cardTexts[i][k].color = CMTokens.WithAlpha(Cream, 0.45f);
            }
        }

        string PaceText()
        {
            double clock = GameConfig.CreateDefault().MatchSeconds;
            double pace = Choice.Pace >= 2 ? 2 : 1;
            return (pace >= 2 ? "2X" : "1X") + "   ·   " + NumberCache.Mmss(clock) + " CLOCK IN " +
                   Mathf.RoundToInt((float)(clock / pace / 60.0)) + " MIN";
        }

        /// <summary>Explicit navigation: steppers in a column, the card row, then Back / Start.</summary>
        void Wire()
        {
            var cards = new List<MenuItem>();
            for (int i = 0; i < 5; i++)
                if (_cards[i].interactable) cards.Add(_cards[i]);
            MenuItem firstCard = cards.Count > 0 ? cards[0] : _start;
            Nav(_team, _start, _size, null, null);
            Nav(_size, _team, _pace, null, null);
            Nav(_pace, _size, firstCard, null, null);
            _bots.navigation = new Navigation { mode = Navigation.Mode.None };
            for (int i = 0; i < cards.Count; i++)
                Nav(cards[i], _pace, _start, cards[(i + cards.Count - 1) % cards.Count], cards[(i + 1) % cards.Count]);
            for (int i = 0; i < 5; i++)
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
            Navigation n = _pace.navigation;
            n.selectOnDown = chosen;
            _pace.navigation = n;
        }

        static void SetUp(MenuItem it, Selectable up)
        {
            Navigation n = it.navigation;
            n.selectOnUp = up;
            it.navigation = n;
        }

        protected override void OnShow()
        {
            if (!_prefsLoaded && GameFlow.MatchesStarted == 0)
            {
                _prefsLoaded = true;
                MatchSetupChoice d = MatchSetupChoice.Default;
                GameFlow.LastSetup = new MatchSetupChoice
                {
                    Team = PlayerPrefs.GetInt(PrefTeam, (int)d.Team) == 1 ? TeamId.Crimson : TeamId.Azure,
                    Class = (ClassId)Mathf.Clamp(PlayerPrefs.GetInt(PrefClass, (int)d.Class), 0, 4),
                    TeamSize = PlayerPrefs.GetInt(PrefSize, d.TeamSize) <= 5 ? 5 : 16,
                    Pace = PlayerPrefs.GetInt(PrefPace, 2) <= 1 ? 1 : 2,
                };
            }
            Choice = GameFlow.LastSetup;
            if (!Playable.Contains(Choice.Class)) Choice.Class = ClassId.Warrior;
            if (Choice.TeamSize != 5) Choice.TeamSize = 16;
            Choice.Pace = Choice.Pace >= 2 ? 2 : 1;
            RefreshAll();
        }

        public void SetTeam(TeamId t)
        {
            Choice.Team = t;
            RefreshAll();
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
            Choice.Pace = pace >= 2 ? 2 : 1;
            RefreshAll();
        }

        void RefreshAll()
        {
            if (_team == null) return;
            for (int i = 0; i < _refresh.Count; i++) _refresh[i]();
            int t = (int)Choice.Team;
            _crest.Set(UiIcons.Crest[t], _tier);
            for (int i = 0; i < 5; i++)
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
            for (int i = 0; i < 5; i++)
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

        public void StartMatch()
        {
            PlayerPrefs.SetInt(PrefTeam, (int)Choice.Team);
            PlayerPrefs.SetInt(PrefClass, (int)Choice.Class);
            PlayerPrefs.SetInt(PrefSize, Choice.TeamSize);
            PlayerPrefs.SetInt(PrefPace, Choice.Pace >= 2 ? 2 : 1);
            GameFlow.BeginMatch(Choice);
        }

        public override void Back()
        {
            if (FromResults) Director.ReturnToResults();
            else Director.ShowMenu(OverlayRegistry.Play);
        }
    }
}
