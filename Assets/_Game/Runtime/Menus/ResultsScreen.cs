using System.Globalization;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
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
    /// DG-01 results: VICTORY / DEFEAT / DRAW from the player's side, and why: both teams' hill seconds (words and a split
    /// bar, the only win condition), the Kaiju's outcome and its reason (allegiance or instability at the hatch, its
    /// knock-outs), the player's own line (knock-outs, deaths, hill time, biomass and corruption fed, damage, points) and
    /// an MVP per team (MatchStats points). PLAY AGAIN is focused, so one press starts the next match on a new seed;
    /// CHANGE CLASS opens match setup (its Back returns here); MAIN MENU. Back = Main menu.
    /// <para>2026-11-12 (UI rough spots): ONE layout (ResultsLayout). Headline on top, a tab row (SUMMARY, REWARDS,
    /// ACHIEVEMENTS, HIGHLIGHTS), one page at a time in the content area, and the way-out row (MAIN MENU, CHANGE CLASS,
    /// PLAY AGAIN) always visible under it. Nothing opens over anything else: the rewards drawer, the achievement list and
    /// the separate highlights overlay that used to cover the cards are now pages. The highlights page draws its own
    /// stills and, when a still could not be captured, the moment's fighter (or the hatched Kaiju) instead of an empty
    /// tile. LB / RB (or Q / E) switch pages; Back returns to the summary first, then to the main menu.</para>
    /// </summary>
    public sealed class ResultsScreen : FlowScreen
    {
        MenuItem _again, _change, _menu, _save;
        readonly MenuItem[] _tabs = new MenuItem[ResultsLayout.TabCount];
        readonly VectorShape[] _tabMarks = new VectorShape[ResultsLayout.TabCount];
        readonly RectTransform[] _pages = new RectTransform[ResultsLayout.TabCount];
        RewardsPanel _rewards;
        Text _noRewards, _achCount, _saveStatus;
        readonly VectorShape[] _achPlates = new VectorShape[Achievements.All.Length];
        readonly Text[] _achTitle = new Text[Achievements.All.Length], _achLine = new Text[Achievements.All.Length],
            _achState = new Text[Achievements.All.Length];
        readonly RawImage[] _stills = new RawImage[3];
        readonly HudIcon[] _stand = new HudIcon[3];
        readonly VectorShape[] _tileFill = new VectorShape[3];
        readonly Text[] _tileLabel = new Text[3], _tileTime = new Text[3], _tileEmpty = new Text[3];
        bool _photoHooked;

        public static readonly string[] TabNames = { "SUMMARY", "REWARDS", "ACHIEVEMENTS", "HIGHLIGHTS" };

        /// <summary>The page on show.</summary>
        public ResultsTab ActiveTab { get; private set; } = ResultsTab.Summary;
        public MenuItem TabItem(ResultsTab t) => _tabs[(int)t];
        public MenuItem RewardsItem => _tabs[(int)ResultsTab.Rewards];
        public MenuItem HighlightsItem => _tabs[(int)ResultsTab.Highlights];
        public RewardsPanel Rewards => _rewards;
        public bool RewardsOpen => ActiveTab == ResultsTab.Rewards;
        public bool AchievementsOpen => ActiveTab == ResultsTab.Achievements;
        public bool HighlightsOpen => ActiveTab == ResultsTab.Highlights;
        /// <summary>The still or stand-in shown on highlight tile k (tests): a captured still, else null.</summary>
        public Texture StillShown(int k) => k >= 0 && k < 3 && _stills[k] != null && _stills[k].enabled ? _stills[k].texture : null;
        /// <summary>True when tile k shows a picture (a captured still or the fighter stand-in), never an empty tile.</summary>
        public bool TileHasPicture(int k) => k >= 0 && k < 3 && ((_stills[k] != null && _stills[k].enabled) ||
                                                                  (_stand[k] != null && _stand[k].Image.enabled));
        /// <summary>The page roots (tests: exactly one is active).</summary>
        public RectTransform Page(ResultsTab t) => _pages[(int)t];
        public string SaveStatusText => _saveStatus != null ? _saveStatus.text : string.Empty;

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

        protected override string HintKeys => "ENTER SELECT   ·   ESC BACK";
        protected override string HintPad => "A SELECT   ·   B BACK";
        protected override float WashAlpha => 0.78f;

        static readonly string[] StatNames = { "KNOCK-OUTS", "DEATHS", "HILL TIME", "BIOMASS FED", "CORRUPTION FED", "DAMAGE", "POINTS" };

        static Text L(Transform parent, string name, Typeface face, int size, Color c, TextAnchor a, Rect r, float outline, int min = UiScale.TypeMin) =>
            MenuUi.Fit(MenuUi.Label(parent, name, face, size, c, a, r.x, r.y, r.width, r.height, "", outline), min);

        protected override void BuildContent(RectTransform space)
        {
            // ---- headline zone ----
            _title = L(space, "Title", CMTokens.Display, UiScale.TypeHero, Cream, TextAnchor.MiddleCenter, ResultsLayout.Title, 6f, UiScale.TypeTitle);
            _line = L(space, "Line", CMTokens.Ui, UiScale.TypeBody, Cream, TextAnchor.MiddleCenter, ResultsLayout.Line, 2f);
            _reason = L(space, "Reason", CMTokens.Display, UiScale.TypeHeading, Brass, TextAnchor.MiddleCenter, ResultsLayout.Reason, 3f, UiScale.TypeSmall);
            // Hill split bar: Azure's share of the hill seconds from the left, Crimson's the rest.
            _azSecs = L(space, "Azure Hill", CMTokens.Mono, UiScale.TypeBody, CMTokens.Azure.Color, TextAnchor.MiddleRight, ResultsLayout.AzureSecs, 3f);
            Rect hb = ResultsLayout.HillBar;
            _bar = MenuUi.Shape(space, "Hill Bar", ShapeKind.RoundRect, hb.x, hb.y, hb.width, hb.height, CMTokens.Azure.Color, -1f);
            _bar.EmptyColor = CMTokens.Crimson.Color;
            _crSecs = L(space, "Crimson Hill", CMTokens.Mono, UiScale.TypeBody, CMTokens.Crimson.Color, TextAnchor.MiddleLeft, ResultsLayout.CrimsonSecs, 3f);

            // ---- tab row ----
            for (int i = 0; i < ResultsLayout.TabCount; i++)
            {
                Rect r = ResultsLayout.Tab(i);
                var tab = (ResultsTab)i;
                MenuItem it = MenuUi.Item(space, "Tab " + TabNames[i], r.x, r.y, r.width, r.height, TabNames[i], MenuItem.Kind.Button,
                    UiScale.TypeBody, true, CMTokens.Display);
                it.Body.Radius = 14f;
                it.Submitted = () => ShowTab(tab);
                _tabs[i] = it;
                // The page on show: a brass bar under its tab (focus is the brass fill, so the two never read the same).
                _tabMarks[i] = MenuUi.Shape(space, "Tab Mark " + i, ShapeKind.RoundRect, r.x + 24f, r.yMax + 3f, r.width - 48f, 6f, Brass, 3f);
                _tabMarks[i].OutlinePx = 0f;
                _tabMarks[i].RimPx = 0f;
            }

            // ---- pages (one visible at a time, all inside ResultsLayout.Content) ----
            for (int i = 0; i < ResultsLayout.TabCount; i++)
            {
                _pages[i] = MenuUi.Rt(space, "Page " + TabNames[i]);
                MenuUi.Place(_pages[i], 0f, 0f, HudLayout.RefW, HudLayout.RefH);
            }
            RectTransform sum = _pages[(int)ResultsTab.Summary];
            BuildKaiju(sum, ResultsLayout.Card(0).x);
            BuildYou(sum, ResultsLayout.Card(1).x);
            BuildMvps(sum, ResultsLayout.Card(2).x);
            BuildRewardsPage(_pages[(int)ResultsTab.Rewards]);
            BuildAchievementsPage(_pages[(int)ResultsTab.Achievements]);
            BuildHighlightsPage(_pages[(int)ResultsTab.Highlights]);

            // ---- the way out, always visible ----
            Rect mr = ResultsLayout.MainMenuButton, cr = ResultsLayout.ChangeClassButton, pr = ResultsLayout.PlayAgainButton;
            // Home lobby (feat/lobby): one press back to HOME, where this match's rewards land on the player card.
            _menu = Button(space, "Main Menu", mr.x, mr.y, mr.width, mr.height, "HOME", () => Director.ToMainMenu(), UiScale.TypeButton);
            _change = Button(space, "Change Class", cr.x, cr.y, cr.width, cr.height, "CHANGE CLASS", () => Director.ShowSetup(true), UiScale.TypeButton);
            _again = Button(space, "Play Again", pr.x, pr.y, pr.width, pr.height, "PLAY AGAIN", () => Director.PlayAgain(), UiScale.TypeHeading);
            _again.Body.RimPx = 4f;
            _again.Body.SetRim(Brass);
            WireNavigation();

            // The results own the end of the match: the highlights, the rewards and the achievement list never open by
            // themselves over it (their own overlays stay for builds without the flow).
            ResultsHighlights.AutoShowOnMatchEnd = false;
            RewardsOverlay.AutoShow = false;
            AchievementToast.ShowListAtMatchEnd = false;
            DefaultFocus = _again;
            _footer = L(space, "Footer", CMTokens.Mono, UiScale.TypeSmall, CMTokens.WithAlpha(Cream, 0.7f), TextAnchor.MiddleRight, ResultsLayout.Footer, 2f);
            ShowTab(ResultsTab.Summary);
        }

        /// <summary>Tabs: left/right wrap, down to the buttons. Buttons: left/right wrap, up to the tab on show.</summary>
        void WireNavigation()
        {
            MenuItem[] row = { _menu, _change, _again };
            for (int i = 0; i < _tabs.Length; i++)
            {
                _tabs[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = _tabs[(i + _tabs.Length - 1) % _tabs.Length],
                    selectOnRight = _tabs[(i + 1) % _tabs.Length],
                    selectOnDown = ActiveTab == ResultsTab.Highlights && _save != null ? _save : _again,
                };
            }
            MenuItem up = _tabs[(int)ActiveTab];
            if (ActiveTab == ResultsTab.Highlights && _save != null)
            {
                up = _save;
                _save.navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit, selectOnUp = _tabs[(int)ActiveTab], selectOnDown = _again,
                };
            }
            for (int i = 0; i < row.Length; i++)
            {
                row[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = row[(i + row.Length - 1) % row.Length],
                    selectOnRight = row[(i + 1) % row.Length],
                    selectOnUp = up,
                };
            }
        }

        /// <summary>Shows one page; the others hide. Never covers the headline or the button row.</summary>
        public void ShowTab(ResultsTab tab)
        {
            ActiveTab = tab;
            MomentRecorder.HighlightsPageOpen = tab == ResultsTab.Highlights && IsVisible;
            for (int i = 0; i < ResultsLayout.TabCount; i++)
            {
                bool on = i == (int)tab;
                if (_pages[i] != null && _pages[i].gameObject.activeSelf != on) _pages[i].gameObject.SetActive(on);
                if (_tabMarks[i] != null) _tabMarks[i].gameObject.SetActive(on);
            }
            switch (tab)
            {
                case ResultsTab.Rewards: RefreshRewards(); break;
                case ResultsTab.Achievements: RefreshAchievements(); break;
                case ResultsTab.Highlights: RefreshHighlights(); break;
            }
            if (_menu != null) WireNavigation();
            // A focus left on a page that just hid moves to the tab of the page on show (focus is never lost).
            MenuItem f = Focused;
            if (f != null && !f.gameObject.activeInHierarchy && _tabs[(int)tab] != null) Focus(_tabs[(int)tab]);
        }

        void StepTab(int d) => ShowTab((ResultsTab)(((int)ActiveTab + d + ResultsLayout.TabCount) % ResultsLayout.TabCount));

        // ---------------------------------------------------------------- rewards page

        void BuildRewardsPage(RectTransform page)
        {
            _rewards = RewardsPanel.Create(page, 0f, 0f);
            Rect f = ResultsLayout.Fitted(RewardsPanel.Width, RewardsPanel.PanelHeight);
            float s = ResultsLayout.FitScale(RewardsPanel.Width, RewardsPanel.PanelHeight);
            RectTransform rt = (RectTransform)_rewards.transform;
            rt.localScale = new Vector3(s, s, 1f);
            rt.anchoredPosition = new Vector2(f.center.x, -f.center.y);
            _rewards.gameObject.SetActive(false);
            Rect c = ResultsLayout.Content;
            _noRewards = L(page, "No Rewards", CMTokens.Ui, UiScale.TypeBody, Cream, TextAnchor.MiddleCenter,
                new Rect(c.x, c.center.y - 24f, c.width, 48f), 2f);
        }

        void RefreshRewards()
        {
            if (_rewards == null) return;
            MatchRewards r = MatchRewardTracker.Last;
            bool has = r != null;
            if (has) _rewards.Show(r);
            else _rewards.gameObject.SetActive(false);
            HudText.Set(_noRewards, has ? string.Empty : "This match paid nothing out: finish a match to earn coins and SP.");
        }

        // ---------------------------------------------------------------- achievements page

        void BuildAchievementsPage(RectTransform page)
        {
            _achCount = L(page, "Count", CMTokens.Display, UiScale.TypeButton, Brass, TextAnchor.MiddleCenter, ResultsLayout.AchCount, 3f);
            int n = Mathf.Min(Achievements.All.Length, ResultsLayout.AchCols * ResultsLayout.AchRowsPerCol);
            for (int i = 0; i < n; i++)
            {
                Rect r = ResultsLayout.AchCell(i);
                VectorShape plate = MenuUi.Shape(page, "Achievement " + i, ShapeKind.RoundRect, r.x, r.y, r.width, r.height, CMTokens.VoidNavy.Color, 14f);
                plate.RimPx = 2f;
                _achPlates[i] = plate;
                Transform t = plate.transform;
                _achTitle[i] = L(t, "Name", CMTokens.Display, UiScale.TypeBody, Cream, TextAnchor.MiddleLeft, new Rect(24f, 8f, r.width - 220f, 32f), 2f);
                HudText.Set(_achTitle[i], Achievements.All[i].Title.ToUpperInvariant());
                _achLine[i] = L(t, "Rule", CMTokens.Ui, UiScale.TypeSmall, Cream, TextAnchor.UpperLeft, new Rect(24f, 42f, r.width - 220f, 36f), 2f);
                HudText.Set(_achLine[i], Achievements.All[i].Description);
                _achState[i] = L(t, "State", CMTokens.Display, UiScale.TypeSmall, Cream, TextAnchor.MiddleRight, new Rect(r.width - 196f, 0f, 172f, r.height), 2f);
            }
        }

        void RefreshAchievements()
        {
            Achievements a = Achievements.Current;
            int n = Achievements.All.Length;
            HudText.Set(_achCount, a != null ? a.UnlockedCount + " OF " + n + " UNLOCKED" : "0 OF " + n + " UNLOCKED");
            for (int i = 0; i < n && i < _achPlates.Length; i++)
            {
                if (_achPlates[i] == null) continue;
                bool now = a != null && a.EarnedThisMatch(i), ever = a != null && a.IsUnlocked(i);
                _achPlates[i].SetFill(now ? Brass : CMTokens.WithAlpha(CMTokens.PanelNavy.Color, ever ? 1f : 0.7f));
                Color text = now ? Navy : CMTokens.WithAlpha(Cream, ever ? 1f : 0.6f);
                _achTitle[i].color = text;
                _achLine[i].color = text;
                _achState[i].color = now ? Navy : ever ? Brass : CMTokens.WithAlpha(Cream, 0.6f);
                HudText.Set(_achState[i], now ? "EARNED" : ever ? "UNLOCKED" : "LOCKED");
            }
        }

        // ---------------------------------------------------------------- highlights page

        void BuildHighlightsPage(RectTransform page)
        {
            for (int k = 0; k < 3; k++)
            {
                Rect r = ResultsLayout.Tile(k);
                VectorShape frame = MenuUi.Shape(page, "Frame " + k, ShapeKind.RoundRect, r.x - 6f, r.y - 6f, r.width + 12f, r.height + 12f, Navy, 12f);
                frame.RimPx = 2f;
                _tileFill[k] = MenuUi.Shape(page, "Fill " + k, ShapeKind.RoundRect, r.x, r.y, r.width, r.height, CMTokens.PanelNavy.Color, 8f);
                _tileFill[k].OutlinePx = 0f;
                _tileFill[k].RimPx = 0f;
                // Stand-in picture: the fighter who made the moment (or the hatched Kaiju), drawn when no still was captured.
                _stand[k] = HudIcon.Make(page, "Stand-in " + k, r.center.x - 100f, r.y + 24f, 200f, 200f);
                _stand[k].Image.enabled = false;
                _tileEmpty[k] = L(page, "Empty " + k, CMTokens.Display, UiScale.TypeBody, CMTokens.Stone.Color, TextAnchor.LowerCenter,
                    new Rect(r.x, r.yMax - 44f, r.width, 36f), 2f);
                RawImage img = MenuUi.Rt(page, "Still " + k).gameObject.AddComponent<RawImage>();
                MenuUi.Place(img.rectTransform, r.x, r.y, r.width, r.height);
                img.raycastTarget = false;
                img.enabled = false;
                _stills[k] = img;
                _tileLabel[k] = L(page, "Label " + k, CMTokens.Display, UiScale.TypeBody, Cream, TextAnchor.MiddleLeft, ResultsLayout.TileLabel(k), 3f);
                _tileTime[k] = L(page, "Time " + k, CMTokens.Mono, UiScale.TypeSmall, Brass, TextAnchor.MiddleLeft, ResultsLayout.TileTime(k), 2f);
            }
            Rect sb = ResultsLayout.SaveButton;
            _save = MenuUi.Item(page, "Save Photo", sb.x, sb.y, sb.width, sb.height,
                "SAVE PHOTO   " + CriticalMass.Runtime.Input.InputGlyphs.ForPath("<Keyboard>/f12").Words.ToUpperInvariant(),
                MenuItem.Kind.Button, UiScale.TypeButton, true, CMTokens.Display);
            _save.Body.Radius = UiScale.ButtonRadius;
            _save.Submitted = SavePhoto;
            _saveStatus = L(page, "Save Status", CMTokens.Ui, UiScale.TypeSmall, Cream, TextAnchor.MiddleCenter, ResultsLayout.SaveStatus, 2f);
        }

        void RefreshHighlights()
        {
            MomentRecorder rec = MomentRecorder.Current;
            for (int k = 0; k < 3; k++)
            {
                if (_stills[k] == null) continue;
                var kind = (MomentKind)k;
                bool has = rec != null && rec.Has(kind);
                Moment m = has ? rec.Get(kind) : default;
                Texture2D still = has ? rec.Still(kind) : null;
                _stills[k].texture = still;
                _stills[k].enabled = still != null;
                string icon = has && still == null ? StandInIcon(kind, m) : null;
                if (icon != null) _stand[k].Set(icon, _tier);
                _stand[k].Image.enabled = icon != null && _stand[k].Image.texture != null;
                _tileFill[k].SetFill(has && still == null ? StandInFill(kind, m) : CMTokens.PanelNavy.Color);
                HudText.Set(_tileLabel[k], HighlightLabel(kind, has, m));
                HudText.Set(_tileTime[k], has ? "AT " + NumberCache.Mmss(m.MatchTime) : string.Empty);
                // Text in the tile only when there is no moment at all (says what did not happen), never "NO PICTURE".
                HudText.Set(_tileEmpty[k], has ? string.Empty : MissingLabel(kind));
            }
            HudText.Set(_saveStatus, string.Empty);
            if (_save != null) _save.interactable = rec != null;
        }

        /// <summary>The picture a moment shows when its still is missing: the knocking-out fighter, or the hatched Kaiju.</summary>
        string StandInIcon(MomentKind kind, Moment m)
        {
            if (kind == MomentKind.Hatch)
                return m.Code >= 0 && m.Code < UiIcons.Adult.Length ? UiIcons.Adult[m.Code] : UiIcons.Egg;
            if (_stats != null && m.Actor >= 0 && m.Actor < _stats.ActorCount)
            {
                ActorStats a = _stats.Actors[m.Actor];
                return UiIcons.Portrait[(int)a.Cls, (int)a.Team];
            }
            return null;
        }

        Color StandInFill(MomentKind kind, Moment m)
        {
            if (kind == MomentKind.Hatch)
                return m.Code == 1 ? CMTokens.WithAlpha(CMTokens.Azure.Color, 0.45f) : m.Code == 2 ? CMTokens.WithAlpha(CMTokens.Crimson.Color, 0.45f)
                    : CMTokens.WithAlpha(CMTokens.Corruption.Color, 0.45f);
            if (_stats != null && m.Actor >= 0 && m.Actor < _stats.ActorCount)
                return CMTokens.WithAlpha(CMTokens.Team(_stats.Actors[m.Actor].Team), 0.45f);
            return CMTokens.PanelNavy.Color;
        }

        static string HighlightLabel(MomentKind k, bool has, Moment m)
        {
            switch (k)
            {
                case MomentKind.FirstKnockOut: return "FIRST KNOCK-OUT";
                case MomentKind.Hatch:
                    if (!has) return "THE HATCH";
                    return m.Code == 1 ? "THE HATCH: AZURE" : m.Code == 2 ? "THE HATCH: CRIMSON" : m.Code == 3 ? "THE HATCH: BERSERK" : "THE HATCH: NO SIDE";
                default:
                    if (!has) return "MULTI KNOCK-OUT";
                    return m.Count == 2 ? "DOUBLE KNOCK-OUT" : m.Count == 3 ? "TRIPLE KNOCK-OUT" : m.Count + "X KNOCK-OUT";
            }
        }

        static string MissingLabel(MomentKind k) =>
            k == MomentKind.FirstKnockOut ? "NO KNOCK-OUTS" : k == MomentKind.Hatch ? "THE EGG HELD" : "NO MULTI KNOCK-OUT";

        void SavePhoto()
        {
            MomentRecorder rec = MomentRecorder.Current;
            if (rec == null) return;
            if (!_photoHooked)
            {
                _photoHooked = true;
                MomentRecorder.PhotoSaved += OnPhotoSaved;
            }
            HudText.Set(_saveStatus, rec.SaveHighlightsCard() ? "Saving..." : "A photo is already being saved.");
        }

        void OnPhotoSaved(string path)
        {
            if (IsVisible && _saveStatus != null) HudText.Set(_saveStatus, "Saved to " + System.IO.Path.GetFileName(path));
        }

        static Text Wrap(Text t) => MenuUi.Fit(t, UiScale.TypeMin);

        static float Top => ResultsLayout.CardsTop;

        static void Card(RectTransform parent, string name, float x, string heading)
        {
            MenuUi.Panel(parent, name, x, Top, ResultsLayout.CardW, ResultsLayout.CardH);
            MenuUi.Fit(MenuUi.Label(parent, name + " Heading", CMTokens.Display, UiScale.TypeHeading, Brass, TextAnchor.MiddleCenter, x,
                Top + 16f, ResultsLayout.CardW, 44f, heading, 3f), UiScale.TypeSmall);
        }

        void BuildKaiju(RectTransform p, float x)
        {
            Card(p, "Kaiju Card", x, "THE KAIJU");
            _kaijuIcon = HudIcon.Make(p, "Kaiju Icon", x + 196f, Top + 68f, 128f, 128f);
            _kaijuCaption = Wrap(MenuUi.Label(p, "Kaiju Caption", CMTokens.Ui, UiScale.TypeBody, Cream, TextAnchor.UpperCenter, x + 32f, Top + 212f, 456f, 72f, "", 2f));
            _kaijuReason = Wrap(MenuUi.Fit(MenuUi.Label(p, "Kaiju Reason", CMTokens.Ui, UiScale.TypeBody, Brass, TextAnchor.UpperCenter, x + 32f, Top + 292f, 456f, 64f, "", 2f), 21));
            _kaijuKOs = Wrap(MenuUi.Label(p, "Kaiju KOs", CMTokens.Ui, UiScale.TypeSmall, Cream, TextAnchor.MiddleCenter, x + 32f, Top + 372f, 456f, 48f, "", 2f));
            _kaijuKills = Wrap(MenuUi.Label(p, "Kaiju Kills", CMTokens.Ui, UiScale.TypeSmall, Cream, TextAnchor.MiddleCenter, x + 32f, Top + 428f, 456f, 48f, "", 2f));
        }

        void BuildYou(RectTransform p, float x)
        {
            Card(p, "You Card", x, "YOUR MATCH");
            _youPortrait = HudIcon.Make(p, "You Portrait", x + 32f, Top + 72f, 104f, 104f);
            _youName = Wrap(MenuUi.Fit(MenuUi.Label(p, "You Name", CMTokens.Display, UiScale.TypeButton, Cream, TextAnchor.MiddleLeft, x + 156f, Top + 76f, 332f, 96f, "", 3f), 26));
            for (int i = 0; i < StatNames.Length; i++)
            {
                float y = Top + 196f + i * 42f;
                bool last = i == StatNames.Length - 1;
                Wrap(MenuUi.Label(p, "Stat " + i, CMTokens.Ui, UiScale.TypeBody, last ? Brass : Cream, TextAnchor.MiddleLeft, x + 40f, y, 260f, 38f, StatNames[i], 2f));
                _statValues[i] = Wrap(MenuUi.Label(p, "Stat Value " + i, CMTokens.Mono, UiScale.TypeBody, last ? Brass : Cream, TextAnchor.MiddleRight,
                    x + 300f, y, 180f, 38f, "", 2f));
            }
        }

        void BuildMvps(RectTransform p, float x)
        {
            Card(p, "MVP Card", x, "MVP");
            for (int t = 0; t < 2; t++)
            {
                float y = Top + 72f + t * 214f;
                Color team = t == 0 ? CMTokens.Azure.Color : CMTokens.Crimson.Color;
                VectorShape tag = MenuUi.Shape(p, "MVP Tag " + t, ShapeKind.RoundRect, x + 30f, y, 460f, 196f, CMTokens.WithAlpha(team, 0.22f), 14f);
                tag.OutlinePx = 0f;
                tag.RimPx = 2f;
                tag.RimColor = team;
                // Inset 24 px inside the tag (it used to start at the tag's edge and clip "AZURE" / "CRIMSON").
                MenuUi.Label(p, "MVP Team " + t, CMTokens.Display, UiScale.TypeBody, team, TextAnchor.MiddleLeft, x + 54f, y + 8f, 400f, 34f,
                    t == 0 ? "AZURE" : "CRIMSON", 3f);
                _mvpPortrait[t] = HudIcon.Make(p, "MVP Portrait " + t, x + 50f, y + 50f, 110f, 110f);
                _mvpName[t] = Wrap(MenuUi.Fit(MenuUi.Label(p, "MVP Name " + t, CMTokens.Display, UiScale.TypeButton, Cream, TextAnchor.MiddleLeft, x + 176f, y + 50f, 296f, 40f, "", 3f), 26));
                _mvpLine[t] = Wrap(MenuUi.Label(p, "MVP Line " + t, CMTokens.Ui, UiScale.TypeSmall, Cream, TextAnchor.UpperLeft, x + 176f, y + 94f, 296f, 50f, "", 2f));
                _mvpPoints[t] = Wrap(MenuUi.Label(p, "MVP Points " + t, CMTokens.Mono, UiScale.TypeBody, Brass, TextAnchor.MiddleLeft, x + 176f, y + 146f, 296f, 34f, "", 2f));
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
            // Every match opens on the summary (headline, the player's line, the MVPs); the rewards are one tab away.
            ShowTab(ResultsTab.Summary);
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
            if (ActiveTab == ResultsTab.Highlights) RefreshHighlights();
        }

        void OnDestroy()
        {
            if (_photoHooked) MomentRecorder.PhotoSaved -= OnPhotoSaved;
        }

        public override void Hide()
        {
            MomentRecorder.HighlightsPageOpen = false;
            if (ResultsHighlights.IsOpen) ResultsHighlights.Hide();
            base.Hide();
        }

        protected override void Tick()
        {
            // LB / RB (Q / E on the keyboard) step through the pages; F12 on the highlights page saves the photo card.
            UnityEngine.InputSystem.Gamepad pad = UnityEngine.InputSystem.Gamepad.current;
            UnityEngine.InputSystem.Keyboard kb = UnityEngine.InputSystem.Keyboard.current;
            if ((pad != null && pad.leftShoulder.wasPressedThisFrame) || (kb != null && kb.qKey.wasPressedThisFrame)) StepTab(-1);
            else if ((pad != null && pad.rightShoulder.wasPressedThisFrame) || (kb != null && kb.eKey.wasPressedThisFrame)) StepTab(1);
        }

        /// <summary>Back returns to the summary page first, then goes home.</summary>
        public override void Back()
        {
            if (ActiveTab != ResultsTab.Summary)
            {
                ShowTab(ResultsTab.Summary);
                Focus(_tabs[(int)ResultsTab.Summary]);
                return;
            }
            Director.ToMainMenu();
        }
    }
}
