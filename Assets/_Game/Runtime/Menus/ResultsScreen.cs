using System.Globalization;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>
    /// DG-01 results: VICTORY / DEFEAT / DRAW from the player's side, and why: both teams' hill seconds (words and a split
    /// bar, the only win condition), the Kaiju's outcome and its reason (allegiance or instability at the hatch, its
    /// knock-outs), the player's own line (knock-outs, deaths, hill time, biomass and corruption fed, damage, points) and
    /// an MVP per team (MatchStats points). PLAY AGAIN is focused, so one press starts the next match on a new seed;
    /// CHANGE CLASS opens match setup (its Back returns here); MAIN MENU. Back = Main menu.
    /// </summary>
    public sealed class ResultsScreen : FlowScreen
    {
        MenuItem _again, _change, _menu;
        Text _title, _line, _reason, _azSecs, _crSecs, _footer;
        VectorShape _bar;
        HudIcon _kaijuIcon, _youPortrait;
        Text _kaijuCaption, _kaijuReason, _kaijuKOs, _kaijuKills, _youName;
        readonly Text[] _statValues = new Text[7];
        readonly HudIcon[] _mvpPortrait = new HudIcon[2];
        readonly Text[] _mvpName = new Text[2], _mvpLine = new Text[2], _mvpPoints = new Text[2];
        World _world;
        MatchStats _stats;
        MatchSummary _sum;
        int _tier = 2;

        public MatchSummary Summary => _sum;
        public MatchStats Stats => _stats;
        public MenuItem PlayAgainItem => _again;
        public MenuItem ChangeClassItem => _change;
        public MenuItem MainMenuItem => _menu;
        public string TitleText => _title != null ? _title.text : "";
        public string ReasonText => _reason != null ? _reason.text : "";

        protected override string HintKeys => "ENTER SELECT   ·   ESC MAIN MENU";
        protected override string HintPad => "A SELECT   ·   B MAIN MENU";
        protected override float WashAlpha => 0.72f;

        static readonly string[] StatNames = { "KNOCK-OUTS", "DEATHS", "HILL TIME", "BIOMASS FED", "CORRUPTION FED", "DAMAGE", "POINTS" };

        protected override void BuildContent(RectTransform space)
        {
            _title = MenuUi.Label(space, "Title", CMTokens.Display, 104, Cream, TextAnchor.MiddleCenter, 260f, 30f, 1400f, 130f, "", 6f);
            _line = MenuUi.Label(space, "Line", CMTokens.Ui, 26, Cream, TextAnchor.MiddleCenter, 260f, 160f, 1400f, 38f, "", 2f);
            _reason = MenuUi.Label(space, "Reason", CMTokens.Display, 32, Brass, TextAnchor.MiddleCenter, 160f, 204f, 1600f, 50f, "", 3f);

            // Hill split bar: Azure's share of the hill seconds from the left, Crimson's the rest.
            _azSecs = MenuUi.Label(space, "Azure Hill", CMTokens.Mono, 26, CMTokens.Azure.Color, TextAnchor.MiddleRight, 220f, 268f, 160f, 40f, "", 3f);
            _bar = MenuUi.Shape(space, "Hill Bar", ShapeKind.RoundRect, 400f, 270f, 1120f, 36f, CMTokens.Azure.Color, -1f);
            _bar.EmptyColor = CMTokens.Crimson.Color;
            _crSecs = MenuUi.Label(space, "Crimson Hill", CMTokens.Mono, 26, CMTokens.Crimson.Color, TextAnchor.MiddleLeft, 1540f, 268f, 160f, 40f, "", 3f);

            BuildKaiju(space, 120f);
            BuildYou(space, 700f);
            BuildMvps(space, 1280f);

            _menu = Button(space, "Main Menu", 240f, 880f, 440f, 84f, "MAIN MENU", Back);
            _change = Button(space, "Change Class", 740f, 880f, 440f, 84f, "CHANGE CLASS", () => Director.ShowSetup(true));
            _again = Button(space, "Play Again", 1240f, 880f, 440f, 84f, "PLAY AGAIN", () => Director.PlayAgain(), 38);
            _again.Body.RimPx = 4f;
            _again.Body.SetRim(Brass);
            MenuItem[] row = { _menu, _change, _again };
            for (int i = 0; i < 3; i++)
            {
                row[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = row[(i + 2) % 3],
                    selectOnRight = row[(i + 1) % 3],
                };
            }
            DefaultFocus = _again;
            _footer = MenuUi.Label(space, "Footer", CMTokens.Mono, 18, CMTokens.WithAlpha(Cream, 0.7f), TextAnchor.MiddleRight, 1180f, 990f,
                600f, 30f, "", 2f);
        }

        static Text Wrap(Text t)
        {
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            return t;
        }

        void Card(RectTransform space, string name, float x, string heading)
        {
            MenuUi.Panel(space, name, x, 340f, 520f, 510f);
            MenuUi.Label(space, name + " Heading", CMTokens.Display, 30, Brass, TextAnchor.MiddleCenter, x, 356f, 520f, 44f, heading, 3f);
        }

        void BuildKaiju(RectTransform space, float x)
        {
            Card(space, "Kaiju Card", x, "THE KAIJU");
            _kaijuIcon = HudIcon.Make(space, "Kaiju Icon", x + 180f, 408f, 160f, 160f);
            _kaijuCaption = Wrap(MenuUi.Label(space, "Kaiju Caption", CMTokens.Ui, 24, Cream, TextAnchor.UpperCenter, x + 30f, 580f, 460f, 70f, "", 2f));
            _kaijuReason = Wrap(MenuUi.Label(space, "Kaiju Reason", CMTokens.Ui, 21, Brass, TextAnchor.UpperCenter, x + 30f, 656f, 460f, 60f, "", 2f));
            _kaijuKOs = MenuUi.Label(space, "Kaiju KOs", CMTokens.Ui, 20, Cream, TextAnchor.MiddleCenter, x + 20f, 732f, 480f, 32f, "", 2f);
            _kaijuKills = MenuUi.Label(space, "Kaiju Kills", CMTokens.Ui, 20, Cream, TextAnchor.MiddleCenter, x + 20f, 772f, 480f, 32f, "", 2f);
        }

        void BuildYou(RectTransform space, float x)
        {
            Card(space, "You Card", x, "YOUR MATCH");
            _youPortrait = HudIcon.Make(space, "You Portrait", x + 30f, 410f, 110f, 110f);
            _youName = Wrap(MenuUi.Label(space, "You Name", CMTokens.Display, 26, Cream, TextAnchor.MiddleLeft, x + 156f, 420f, 340f, 90f, "", 3f));
            for (int i = 0; i < StatNames.Length; i++)
            {
                float y = 534f + i * 42f;
                bool last = i == StatNames.Length - 1;
                MenuUi.Label(space, "Stat " + i, CMTokens.Ui, 22, last ? Brass : Cream, TextAnchor.MiddleLeft, x + 40f, y, 280f, 38f, StatNames[i], 2f);
                _statValues[i] = MenuUi.Label(space, "Stat Value " + i, CMTokens.Mono, 24, last ? Brass : Cream, TextAnchor.MiddleRight,
                    x + 300f, y, 180f, 38f, "", 2f);
            }
        }

        void BuildMvps(RectTransform space, float x)
        {
            Card(space, "MVP Card", x, "MVP");
            for (int t = 0; t < 2; t++)
            {
                float y = 414f + t * 214f;
                Color team = t == 0 ? CMTokens.Azure.Color : CMTokens.Crimson.Color;
                VectorShape tag = MenuUi.Shape(space, "MVP Tag " + t, ShapeKind.RoundRect, x + 30f, y, 460f, 196f, CMTokens.WithAlpha(team, 0.22f), 14f);
                tag.OutlinePx = 0f;
                tag.RimPx = 2f;
                tag.RimColor = team;
                MenuUi.Label(space, "MVP Team " + t, CMTokens.Display, 22, team, TextAnchor.MiddleLeft, x + 50f, y + 8f, 400f, 34f,
                    t == 0 ? "AZURE" : "CRIMSON", 3f);
                _mvpPortrait[t] = HudIcon.Make(space, "MVP Portrait " + t, x + 46f, y + 50f, 110f, 110f);
                _mvpName[t] = MenuUi.Label(space, "MVP Name " + t, CMTokens.Display, 26, Cream, TextAnchor.MiddleLeft, x + 172f, y + 50f, 300f, 40f, "", 3f);
                _mvpLine[t] = Wrap(MenuUi.Label(space, "MVP Line " + t, CMTokens.Ui, 19, Cream, TextAnchor.UpperLeft, x + 172f, y + 94f, 300f, 50f, "", 2f));
                _mvpPoints[t] = MenuUi.Label(space, "MVP Points " + t, CMTokens.Mono, 22, Brass, TextAnchor.MiddleLeft, x + 172f, y + 146f, 300f, 34f, "", 2f);
            }
        }

        /// <summary>Fills the screen from a finished match (FlowDirector.ShowResults).</summary>
        public void Bind(World w, MatchStats stats, MatchSummary sum)
        {
            _world = w;
            _stats = stats;
            _sum = sum;
            EnsureBuilt();
            Refresh();
        }

        static string I(double v) => Mathf.RoundToInt((float)v).ToString(CultureInfo.InvariantCulture);

        string ActorName(int id)
        {
            if (id == 0) return "YOU";
            ActorStats a = _stats.Actors[id];
            int slot = _world != null ? World.TeamSlot(_world, id) : id;
            return FlowWords.ClassName(a.Cls) + " " + (slot + 1).ToString(CultureInfo.InvariantCulture);
        }

        void Refresh()
        {
            if (_sum == null || _stats == null || _world == null) return;
            MatchState m = _world.Match;
            bool draw = _sum.Winner == MatchResult.Draw;
            TeamId winTeam = _sum.Winner == MatchResult.Crimson ? TeamId.Crimson : TeamId.Azure;
            _title.text = _sum.Title;
            _title.color = draw ? Cream : CMTokens.Team(winTeam);
            HudText.Set(_line, _sum.Line);
            HudText.Set(_reason, _sum.Reason);
            double az = m.HillSeconds.Azure, cr = m.HillSeconds.Crimson;
            HudText.Set(_azSecs, I(az) + " s");
            HudText.Set(_crSecs, I(cr) + " s");
            _bar.Fill01 = az + cr > 0 ? (float)(az / (az + cr)) : 0.5f;

            int adult = _sum.Kaiju == KaijuState.Azure ? 1 : _sum.Kaiju == KaijuState.Crimson ? 2 : _sum.Kaiju == KaijuState.Berserk ? 3 : 0;
            _kaijuIcon.Set(_sum.Kaiju == KaijuState.Egg ? UiIcons.Egg : UiIcons.Adult[adult], _tier);
            HudText.Set(_kaijuCaption, _sum.KaijuCaption);
            HudText.Set(_kaijuReason, _sum.KaijuReason);
            HudText.Set(_kaijuKOs, _sum.KaijuKOLine);
            HudText.Set(_kaijuKills, "Players it knocked out: Azure " + _stats.KaijuKillsOfTeam[0] + ", Crimson " + _stats.KaijuKillsOfTeam[1] + ".");

            if (_stats.ActorCount > 0)
            {
                ActorStats me = _stats.Actors[0];
                _youPortrait.Set(UiIcons.Portrait[(int)me.Cls, (int)me.Team], _tier);
                HudText.Set(_youName, FlowWords.ClassName(me.Cls) + "\n" + (me.Team == TeamId.Azure ? "AZURE" : "CRIMSON"));
                _youName.color = CMTokens.Team(me.Team);
                string[] v =
                {
                    me.Kills.ToString(CultureInfo.InvariantCulture), me.Deaths.ToString(CultureInfo.InvariantCulture),
                    I(me.HillSeconds) + " s", me.Biomass.ToString(CultureInfo.InvariantCulture),
                    me.Corruption.ToString(CultureInfo.InvariantCulture), I(me.Damage), me.Points.ToString(CultureInfo.InvariantCulture),
                };
                for (int i = 0; i < v.Length; i++) HudText.Set(_statValues[i], v[i]);
            }

            for (int t = 0; t < 2; t++)
            {
                int id = _stats.Mvp((TeamId)t);
                if (id < 0)
                {
                    HudText.Set(_mvpName[t], "-");
                    HudText.Set(_mvpLine[t], "");
                    HudText.Set(_mvpPoints[t], "");
                    continue;
                }
                ActorStats a = _stats.Actors[id];
                _mvpPortrait[t].Set(UiIcons.Portrait[(int)a.Cls, t], _tier);
                HudText.Set(_mvpName[t], ActorName(id));
                _mvpName[t].color = id == 0 ? Brass : Cream;
                HudText.Set(_mvpLine[t], a.Kills + " KO  ·  " + I(a.HillSeconds) + " s HILL  ·  " + a.Biomass + " BIOMASS");
                HudText.Set(_mvpPoints[t], a.Points.ToString(CultureInfo.InvariantCulture) + " PTS");
            }

            HudText.Set(_footer, "SEED " + _stats.Seed.ToString(CultureInfo.InvariantCulture) + "   ·   " + _stats.TeamSize + " V " +
                                 _stats.TeamSize + "   ·   MATCH " + Mathf.Max(1, GameFlow.MatchesStarted));
        }

        protected override void OnScale(float scale)
        {
            int tier = UiIcons.TierFor(scale);
            if (tier == _tier) return;
            _tier = tier;
            Refresh();
        }

        public override void Back() => Director.ToMainMenu();
    }
}
