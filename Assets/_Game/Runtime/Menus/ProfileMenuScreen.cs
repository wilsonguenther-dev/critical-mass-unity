using System;
using System.Collections.Generic;
using System.Globalization;
using CriticalMass.Net;
using CriticalMass.Net.Replay;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Progression;
using CriticalMass.Runtime.Replay;
using CriticalMass.Sim.Config;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>
    /// b15 profile: the main menu's PROFILE line. Three pages in one contained screen (ProfileLayout; nothing scrolls):
    /// MASTERY (the six classes, each class's mastery track, what it is earned by, the next reward, the cosmetic look
    /// with four steppers over the unlocked items, and the class's career totals), MATCH HISTORY (the last 50 finished
    /// matches, eight a page with PREV / NEXT; choosing a row watches its replay when the file is still kept) and
    /// ACHIEVEMENTS (honest progress bars; achievements this game's rules cannot award are hidden). Keyboard, controller
    /// and pointer navigable; every word from MetaWords.
    /// </summary>
    [MenuEntry(MenuId, "PROFILE", 32)]
    public sealed class ProfileMenuScreen : MonoBehaviour, IMenuScreen, IMenuBack
    {
        public const string MenuId = "profile";

        public static ProfileMenuScreen Instance { get; private set; }
        public bool IsOpen => _open;
        /// <summary>b15 view polish (item 4): the profile screen is open (MenuGate).</summary>
        public static bool AnyOpen => Instance != null && Instance._open;
        public ProfileTab Tab { get; private set; }
        public ClassId Class { get; private set; }
        public int HistoryPage { get; private set; }
        public string Notice { get; private set; } = "";
        /// <summary>The space every element is placed in (tests).</summary>
        public RectTransform Space => _space;

        /// <summary>The profile shown (tests set one; null = Profile.Current).</summary>
        public PlayerProfile Shown;
        PlayerProfile P => Shown ?? Profile.Current;

        Action _onClosed;
        bool _built, _open, _dirty;
        Canvas _canvas;
        RectTransform _space;
        readonly RectTransform[] _pages = new RectTransform[ProfileLayout.TabCount];
        readonly MenuItem[] _tabs = new MenuItem[ProfileLayout.TabCount];
        readonly MenuItem[] _classes = new MenuItem[ProfileLayout.ClassCount];
        readonly MenuItem[] _look = new MenuItem[4];
        readonly MenuItem[] _rows = new MenuItem[ProfileLayout.HistoryRows];
        readonly AchCell[] _ach = new AchCell[ProfileLayout.AchCols * ProfileLayout.AchRows];
        MenuItem _prev, _next, _back;
        Text _player, _heading, _xpText, _earned, _nextLine, _career, _careerTotals, _detail, _empty, _achCount, _pageText, _hint;
        Text _cardTitle, _cardTrim, _cardNote;
        VectorShape _xpFill, _cardDisc, _cardEmblem;
        HudIcon _cardPortrait;
        readonly VectorShape[] _swatches = new VectorShape[3];
        ControlDevice _hintDevice = (ControlDevice)(-1);

        sealed class AchCell
        {
            public RectTransform Root;
            public Text Title, State, Rule;
            public VectorShape Fill;
            public float BarW;
        }

        /// <summary>Sample fixed colours (leather, skin, metal) the colours swatch shows trimmed.</summary>
        static readonly string[] SwatchHex = { "#8A5A3C", "#D9A27A", "#9AA0A8" };

        void Awake() { if (Instance == null) Instance = this; }

        void OnDestroy()
        {
            if (_open) Close(false);
            if (Instance == this) Instance = null;
        }

        public void Open(Action onClosed)
        {
            _onClosed = onClosed;
            Build();
            Notice = "";
            HistoryPage = 0;
            Class = ClassSelect.SelectedClass;
            _open = true;
            _canvas.enabled = true;
            OverlayGuard.Begin(gameObject, ConsumePause);
            MenuItem.SubmitBlockedUntil = Time.unscaledTime + 0.15f;
            ShowTab(ProfileTab.Mastery);
        }

        public void Back() => Close(true);

        bool ConsumePause()
        {
            Close(true);
            return true;
        }

        void Close(bool raise)
        {
            _open = false;
            if (_dirty && Shown == null) Profile.Commit();
            _dirty = false;
            if (_canvas != null) _canvas.enabled = false;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            OverlayGuard.End();
            if (!raise) return;
            Action a = _onClosed;
            _onClosed = null;
            a?.Invoke();
        }

        void Update()
        {
            OverlayGuard.Tick();
            if (!_open) return;
            ProgressionUi.UpdateScale(_canvas);
            ControlBindings.Sample();
            ControlDevice d = ControlBindings.ActiveDevice;
            if (d != _hintDevice)
            {
                _hintDevice = d;
                HudText.Set(_hint, d == ControlDevice.Gamepad ? MetaWords.HintGamepad : MetaWords.HintKeyboard);
            }
            Gamepad pad = Gamepad.current;
            Keyboard kb = Keyboard.current;
            if ((pad != null && pad.buttonEast.wasPressedThisFrame) || (kb != null && kb.escapeKey.wasPressedThisFrame))
            {
                Close(true);
                return;
            }
            EventSystem es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject == null && NavigationPressed()) Select(FirstFocus());
        }

        static bool NavigationPressed()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame)) return true;
            Gamepad pad = Gamepad.current;
            return pad != null && (pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame || pad.leftStick.ReadValue().sqrMagnitude > 0.25f);
        }

        static void Select(Selectable s)
        {
            if (s == null || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(null);
            s.Select();
        }

        Selectable FirstFocus()
        {
            switch (Tab)
            {
                case ProfileTab.Mastery: return _classes[(int)Class];
                case ProfileTab.History:
                    foreach (MenuItem r in _rows) if (r.gameObject.activeSelf) return r;
                    return _back;
                default: return _tabs[(int)Tab];
            }
        }

        // ------------------------------------------------------------------ pages

        public void ShowTab(ProfileTab tab)
        {
            Tab = tab;
            for (int i = 0; i < ProfileLayout.TabCount; i++)
            {
                bool on = i == (int)tab;
                _pages[i].gameObject.SetActive(on);
                _tabs[i].Body.SetRim(on ? CMTokens.ToolBrass.Color : CMTokens.WarmCream.Color);
            }
            bool paged = tab == ProfileTab.History;
            _prev.gameObject.SetActive(paged);
            _next.gameObject.SetActive(paged);
            _pageText.gameObject.SetActive(paged);
            Refresh();
            Wire();
            Select(FirstFocus());
        }

        /// <summary>Shows history page k (0-based; wraps), as PREV / NEXT do.</summary>
        public void ShowHistoryPage(int k)
        {
            int pages = MatchHistory.PageCount(P.History?.Length ?? 0);
            HistoryPage = ((k % pages) + pages) % pages;
            Notice = "";
            if (Tab == ProfileTab.History) { Refresh(); Wire(); }
        }

        public void ShowClass(ClassId cls)
        {
            Class = cls;
            Refresh();
        }

        public void Refresh()
        {
            PlayerProfile p = P;
            HudText.Set(_player, MetaWords.F("{0}   {1} {2}   {3}", p.PlayerName, ProgressionWords.Level, p.Level,
                MetaWords.F(MetaWords.CareerLine, p.MatchesPlayed, p.Wins, p.Draws, p.Losses)));
            switch (Tab)
            {
                case ProfileTab.Mastery: RefreshMastery(p); break;
                case ProfileTab.History: RefreshHistory(p); break;
                default: RefreshAchievements(p); break;
            }
        }

        void RefreshMastery(PlayerProfile p)
        {
            for (int i = 0; i < _classes.Length; i++)
            {
                ClassMastery cm = p.MasteryOf((ClassId)i);
                HudText.Set(_classes[i].Label, MetaWords.F("{0}  {1}", ProgressionWords.ClassNames[i], cm.Level));
                _classes[i].Body.SetRim(i == (int)Class ? CMTokens.ToolBrass.Color : CMTokens.WarmCream.Color);
            }
            ClassMastery m = p.MasteryOf(Class);
            int level = m.Level;
            HudText.Set(_heading, MetaWords.F("{0}   {1}", ProgressionWords.ClassNames[(int)Class], MetaWords.F(MetaWords.MasteryLevelOf, level, MasteryRules.MaxLevel)));
            float prog = MasteryRules.Progress(m.Xp);
            MenuUi.Place(_xpFill.rectTransform, 0f, 0f, Mathf.Max(0f, ProfileLayout.XpBar.width * prog), ProfileLayout.XpBar.height);
            _xpFill.gameObject.SetActive(prog > 0f);
            HudText.Set(_xpText, level >= MasteryRules.MaxLevel ? MetaWords.MaxMastery
                : MetaWords.F(MetaWords.XpToNext, (MasteryRules.TotalFor(level + 1) - m.Xp).ToString("#,0", CultureInfo.InvariantCulture), level + 1));
            HudText.Set(_earned, MetaWords.F(MetaWords.EarnedBy, MetaWords.JobLine[(int)Class]));
            MasteryReward? next = MasteryRewards.NextAfter(Class, level);
            HudText.Set(_nextLine, next.HasValue
                ? MetaWords.F(MetaWords.NextReward, next.Value.Level, MetaWords.F(MetaWords.RewardLine, next.Value.KindName, next.Value.Name))
                : string.Empty);
            ClassLoadout l = p.Loadout(Class);
            HudText.Set(_look[0].Value, MasteryRewards.Name(Class, RewardKind.Title, l.Title));
            HudText.Set(_look[1].Value, MasteryRewards.Name(Class, RewardKind.Frame, l.Frame));
            HudText.Set(_look[2].Value, MasteryRewards.Name(Class, RewardKind.Trim, l.Trim));
            HudText.Set(_look[3].Value, MasteryRewards.Name(Class, RewardKind.Emblem, l.Badge));
            CosmeticLook look = LookRegistry.Local(p, Class);
            _cardPortrait.Set(UiIcons.Portrait[(int)Class, 0], 2);
            LookView.Ring(_cardDisc, look.Frame, 4f, CMTokens.VoidNavy.Color);
            LookView.SetEmblem(_cardEmblem, Class, look.Emblem);
            HudText.Set(_cardTitle, look.Title > 0 ? MetaWords.Title(Class, look.Title) : p.PlayerName);
            HudText.Set(_cardTrim, MetaWords.F(MetaWords.RewardLine, MetaWords.SlotTrim, MasteryRewards.Name(Class, RewardKind.Trim, look.Trim)));
            TrimSpec t = MasteryRewards.Trim(look.Trim);
            for (int k = 0; k < _swatches.Length; k++)
            {
                ColorUtility.TryParseHtmlString(SwatchHex[k], out Color fixedC);
                _swatches[k].SetFill(t.Apply(fixedC));
            }
            if (m.Matches == 0)
            {
                HudText.Set(_career, MetaWords.Career);
                HudText.Set(_careerTotals, MetaWords.NoMatchesYet);
            }
            else
            {
                HudText.Set(_career, MetaWords.F("{0}   {1}", MetaWords.Career, MetaWords.F(MetaWords.CareerLine, m.Matches, m.Wins, m.Draws, m.Losses)));
                RoleStat[] job = MasteryRules.Job[(int)Class];
                HudText.Set(_careerTotals, MetaWords.F("{0}     {1}     {2}     {3}",
                    MetaWords.StatValue(job[0], m.Total(job[0])), MetaWords.StatValue(job[1], m.Total(job[1])),
                    MetaWords.StatValue(RoleStat.KnockOuts, m.KnockOuts), MetaWords.StatValue(RoleStat.HillSeconds, m.HillSeconds)));
            }
        }

        void StepLook(RewardKind kind, int dir)
        {
            PlayerProfile p = P;
            ClassLoadout l = p.Loadout(Class);
            int level = p.MasteryOf(Class).Level;
            int cur = kind == RewardKind.Title ? l.Title : kind == RewardKind.Frame ? l.Frame : kind == RewardKind.Trim ? l.Trim : l.Badge;
            int v = NextUnlocked(kind, cur, dir, level);
            if (kind == RewardKind.Title) l.Title = v;
            else if (kind == RewardKind.Frame) l.Frame = v;
            else if (kind == RewardKind.Trim) l.Trim = v;
            else l.Badge = v;
            _dirty = true;
            if (Shown == null) Profile.Commit();   // the scoreboard, class card and online look read it at once
            RefreshMastery(p);
        }

        /// <summary>The next unlocked id after cur in direction dir, wrapping through 0 (pure; tests).</summary>
        public static int NextUnlocked(RewardKind kind, int cur, int dir, int level)
        {
            int n = MasteryRewards.Count(kind) + 1;
            int step = dir < 0 ? -1 : 1;
            int v = cur;
            for (int i = 0; i < n; i++)
            {
                v = ((v + step) % n + n) % n;
                if (MasteryRewards.Unlocked(kind, v, level)) return v;
            }
            return 0;
        }

        void RefreshHistory(PlayerProfile p)
        {
            int count = p.History != null ? p.History.Length : 0;
            int pages = MatchHistory.PageCount(count);
            HistoryPage = Mathf.Clamp(HistoryPage, 0, pages - 1);
            List<MatchRecord> page = MatchHistory.Page(p, HistoryPage);
            for (int i = 0; i < _rows.Length; i++)
            {
                bool on = i < page.Count;
                _rows[i].gameObject.SetActive(on);
                if (on) HudText.Set(_rows[i].Label, MatchHistory.Row(page[i]));
            }
            _empty.gameObject.SetActive(count == 0);
            HudText.Set(_empty, MetaWords.HistoryEmpty);
            HudText.Set(_pageText, MetaWords.F(MetaWords.PageOf, HistoryPage + 1, pages));
            _prev.interactable = _next.interactable = pages > 1;
            HudText.Set(_detail, Notice.Length > 0 ? Notice : page.Count > 0 ? Detail(page[0]) : string.Empty);
        }

        /// <summary>The detail line of a record: its turning point (when the match summary gave one), the bots tag and the replay state.</summary>
        public static string Detail(MatchRecord r)
        {
            if (r == null) return string.Empty;
            string replay = string.IsNullOrEmpty(r.ReplayFile) ? MetaWords.NoReplay
                : ReplayExists(r.ReplayFile) ? MetaWords.WatchReplay : MetaWords.ReplayGone;
            string tags = r.BotOnly ? MetaWords.BotMatchTag + "   " : string.Empty;
            return string.IsNullOrEmpty(r.TurningPoint) ? tags + replay : r.TurningPoint + "   " + tags + replay;
        }

        static bool ReplayExists(string file)
        {
            try { return System.IO.File.Exists(System.IO.Path.Combine(ReplayStore.Folder, PlayerProfile.SafeFileName(file))); }
            catch (Exception) { return false; }
        }

        void ChooseRow(int row)
        {
            List<MatchRecord> page = MatchHistory.Page(P, HistoryPage);
            if (row < 0 || row >= page.Count) return;
            MatchRecord r = page[row];
            string file = PlayerProfile.SafeFileName(r.ReplayFile);
            if (file.Length == 0 || !ReplayExists(file))
            {
                Notice = file.Length == 0 ? MetaWords.NoReplay : MetaWords.ReplayGone;
                HudText.Set(_detail, Notice);
                return;
            }
            ReplayData d = ReplayStore.Load(System.IO.Path.Combine(ReplayStore.Folder, file), out string why);
            if (d == null || !ReplayPlayback.Play(d, out why))
            {
                Notice = why ?? MetaWords.ReplayGone;
                HudText.Set(_detail, Notice);
                return;
            }
            Close(false);   // the replay's reload replaces the menus
        }

        void FocusRow(int row)
        {
            List<MatchRecord> page = MatchHistory.Page(P, HistoryPage);
            Notice = "";
            if (row >= 0 && row < page.Count) HudText.Set(_detail, Detail(page[row]));
        }

        void RefreshAchievements(PlayerProfile p)
        {
            GameConfig cfg = GameConfig.CreateDefault();
            int shown = 0, unlocked = 0;
            for (int i = 0; i < Achievements.All.Length && i < _ach.Length; i++)
            {
                AchCell c = _ach[i];
                bool earnable = Achievements.Earnable(i, cfg);
                AchievementRecord rec = p.Achievement(Achievements.All[i].Id);
                bool done = rec.Unlocked;
                bool show = earnable || done;
                c.Root.gameObject.SetActive(show);
                if (!show) continue;
                shown++;
                if (done) unlocked++;
                int target = Achievements.Target[i];
                int best = Mathf.Clamp(done ? target : rec.Best, 0, target);
                HudText.Set(c.Title, Achievements.All[i].Title.ToUpperInvariant());
                HudText.Set(c.Rule, Achievements.All[i].Description);
                HudText.Set(c.State, done ? MetaWords.Unlocked : target > 1 ? MetaWords.F(MetaWords.BestOfTarget, best, target) : MetaWords.NotYet);
                float f = target > 0 ? best / (float)target : 0f;
                MenuUi.Place(c.Fill.rectTransform, 0f, 0f, c.BarW * f, ProfileLayout.AchBar(ProfileLayout.AchCell(i)).height);
                c.Fill.gameObject.SetActive(f > 0f);
                c.Fill.SetFill(done ? CMTokens.ToolBrass.Color : CMTokens.WarmCream.Color);
            }
            HudText.Set(_achCount, MetaWords.F(MetaWords.AchievementsCount, unlocked, shown));
        }

        // ------------------------------------------------------------------ navigation

        void Wire()
        {
            for (int i = 0; i < _tabs.Length; i++)
            {
                var n = new Navigation { mode = Navigation.Mode.Explicit };
                n.selectOnLeft = _tabs[(i + _tabs.Length - 1) % _tabs.Length];
                n.selectOnRight = _tabs[(i + 1) % _tabs.Length];
                n.selectOnDown = FirstFocus() == (Selectable)_tabs[(int)Tab] ? _back : FirstFocus();
                n.selectOnUp = _back;
                _tabs[i].navigation = n;
            }
            var col = new List<Selectable>();
            if (Tab == ProfileTab.Mastery)
            {
                foreach (MenuItem c in _classes) col.Add(c);
                foreach (MenuItem s in _look) col.Add(s);
            }
            else if (Tab == ProfileTab.History)
                foreach (MenuItem r in _rows) if (r.gameObject.activeSelf) col.Add(r);
            var bottom = new List<Selectable>();
            if (_prev.gameObject.activeSelf) bottom.Add(_prev);
            if (_next.gameObject.activeSelf) bottom.Add(_next);
            bottom.Add(_back);
            Selectable tab = _tabs[(int)Tab];
            for (int i = 0; i < col.Count; i++)
            {
                var n = new Navigation { mode = Navigation.Mode.Explicit };
                n.selectOnUp = i > 0 ? col[i - 1] : tab;
                n.selectOnDown = i < col.Count - 1 ? col[i + 1] : bottom[bottom.Count - 1];
                col[i].navigation = n;
            }
            for (int i = 0; i < bottom.Count; i++)
            {
                var n = new Navigation { mode = Navigation.Mode.Explicit };
                n.selectOnLeft = i > 0 ? bottom[i - 1] : null;
                n.selectOnRight = i < bottom.Count - 1 ? bottom[i + 1] : null;
                n.selectOnUp = col.Count > 0 ? col[col.Count - 1] : tab;
                n.selectOnDown = tab;
                bottom[i].navigation = n;
            }
        }

        // ------------------------------------------------------------------ build

        Text L(Transform parent, string name, Typeface face, int size, Color c, TextAnchor a, Rect r, string text = "", int min = ProfileLayout.SmallestType)
        {
            Text t = MenuUi.Label(parent, name, face, size, c, a, r.x, r.y, r.width, r.height, text, 2f);
            return MenuUi.Fit(t, Mathf.Min(size, min));
        }

        /// <summary>Builds the canvas once (tests call it directly).</summary>
        public void Build()
        {
            if (_built) return;
            _built = true;
            PauseMenu.EnsureEventSystem(transform);
            _canvas = ProgressionUi.MakeCanvas(transform, "Profile Canvas", 50, out _space);
            ProgressionUi.Wash(_canvas.transform, 0.92f).transform.SetAsFirstSibling();
            Color cream = CMTokens.WarmCream.Color, brass = CMTokens.ToolBrass.Color;
            RectTransform space = _space;

            L(space, "Title", CMTokens.Display, UiScale.TypeTitle, cream, TextAnchor.MiddleCenter, ProfileLayout.Title, MetaWords.ProfileTitle, UiScale.TypeLead);
            _player = L(space, "Player", CMTokens.Ui, UiScale.TypeBody, CMTokens.WithAlpha(cream, 0.9f), TextAnchor.MiddleCenter, ProfileLayout.PlayerLine);
            string[] tabWords = { MetaWords.TabMastery, MetaWords.TabHistory, MetaWords.TabAchievements };
            for (int i = 0; i < ProfileLayout.TabCount; i++)
            {
                Rect r = ProfileLayout.Tab(i);
                var tab = (ProfileTab)i;
                _tabs[i] = MenuUi.Item(space, "Tab " + i, r.x, r.y, r.width, r.height, tabWords[i], MenuItem.Kind.Button, UiScale.TypeButton, true, CMTokens.Display);
                _tabs[i].Submitted = () => ShowTab(tab);
            }
            for (int i = 0; i < ProfileLayout.TabCount; i++)
            {
                _pages[i] = MenuUi.Rt(space, "Page " + i);
                MenuUi.Place(_pages[i], 0f, 0f, HudLayout.RefW, HudLayout.RefH);
            }
            BuildMastery(_pages[(int)ProfileTab.Mastery], cream, brass);
            BuildHistory(_pages[(int)ProfileTab.History], cream);
            BuildAchievements(_pages[(int)ProfileTab.Achievements], cream, brass);

            Rect pr = ProfileLayout.PrevButton, nr = ProfileLayout.NextButton, br = ProfileLayout.BackButton;
            _prev = MenuUi.Item(space, "Prev", pr.x, pr.y, pr.width, pr.height, MetaWords.Prev, MenuItem.Kind.Button, UiScale.TypeLead, true, CMTokens.Display);
            _prev.Submitted = () => ShowHistoryPage(HistoryPage - 1);
            _pageText = L(space, "Page", CMTokens.Ui, UiScale.TypeBody, cream, TextAnchor.MiddleCenter, ProfileLayout.PageText);
            _next = MenuUi.Item(space, "Next", nr.x, nr.y, nr.width, nr.height, MetaWords.Next, MenuItem.Kind.Button, UiScale.TypeLead, true, CMTokens.Display);
            _next.Submitted = () => ShowHistoryPage(HistoryPage + 1);
            _back = MenuUi.Item(space, "Back", br.x, br.y, br.width, br.height, MetaWords.Back, MenuItem.Kind.Button, UiScale.TypeLead, true, CMTokens.Display);
            _back.Submitted = () => Close(true);
            _hint = L(space, "Hint", CMTokens.Ui, UiScale.TypeSmall, CMTokens.WithAlpha(cream, 0.85f), TextAnchor.MiddleCenter, ProfileLayout.Hint, MetaWords.HintKeyboard);
            _canvas.enabled = false;
        }

        void BuildMastery(RectTransform page, Color cream, Color brass)
        {
            for (int i = 0; i < ProfileLayout.ClassCount; i++)
            {
                Rect r = ProfileLayout.ClassButton(i);
                var cls = (ClassId)i;
                _classes[i] = MenuUi.Item(page, "Class " + i, r.x, r.y, r.width, r.height, ProgressionWords.ClassNames[i], MenuItem.Kind.Button, UiScale.TypeButton, false, CMTokens.Display);
                _classes[i].Submitted = () => ShowClass(cls);
            }
            _heading = L(page, "Heading", CMTokens.Display, UiScale.TypeLead, cream, TextAnchor.MiddleLeft, ProfileLayout.ClassHeading);
            Rect xb = ProfileLayout.XpBar;
            VectorShape track = MenuUi.Shape(page, "XP Track", ShapeKind.RoundRect, xb.x, xb.y, xb.width, xb.height, CMTokens.MeterEmpty.Color, 10f);
            track.RimPx = 0f;
            _xpFill = MenuUi.Shape(track.transform, "XP Fill", ShapeKind.RoundRect, 0f, 0f, 0f, xb.height, brass, 10f);
            _xpFill.RimPx = 0f;
            _xpText = L(page, "XP Text", CMTokens.Ui, UiScale.TypeBody, cream, TextAnchor.MiddleLeft, ProfileLayout.XpText);
            _earned = L(page, "Earned By", CMTokens.Ui, UiScale.TypeBody, CMTokens.WithAlpha(cream, 0.9f), TextAnchor.MiddleLeft, ProfileLayout.EarnedBy);
            _nextLine = L(page, "Next Reward", CMTokens.Ui, UiScale.TypeBody, brass, TextAnchor.MiddleLeft, ProfileLayout.NextLine);
            string[] slots = { MetaWords.SlotTitle, MetaWords.SlotFrame, MetaWords.SlotTrim, MetaWords.SlotEmblem };
            RewardKind[] kinds = { RewardKind.Title, RewardKind.Frame, RewardKind.Trim, RewardKind.Emblem };
            for (int k = 0; k < 4; k++)
            {
                Rect r = ProfileLayout.LookRow(k);
                RewardKind kind = kinds[k];
                MenuItem it = MenuUi.Item(page, "Look " + k, r.x, r.y, r.width, r.height, slots[k], MenuItem.Kind.Stepper, UiScale.TypeButton, false, CMTokens.Display);
                const float valueX = 300f, arrowW = 40f;
                it.Value = MenuUi.Fit(MenuUi.Label(it.transform, "Value", CMTokens.Ui, UiScale.TypeButton, MenuItem.IdleText, TextAnchor.MiddleCenter,
                    valueX + arrowW, 0f, r.width - valueX - 2f * arrowW - 16f, r.height, "", 2f), ProfileLayout.SmallestType);
                MenuUi.Label(it.transform, "Left", CMTokens.Display, UiScale.TypeButton, brass, TextAnchor.MiddleCenter, valueX, 0f, arrowW, r.height, "<", 2f);
                MenuUi.Label(it.transform, "Right", CMTokens.Display, UiScale.TypeButton, brass, TextAnchor.MiddleCenter, r.width - arrowW - 16f, 0f, arrowW, r.height, ">", 2f);
                // the slot word keeps to the label column
                MenuUi.Place(it.Label.rectTransform, 18f, 0f, valueX - 24f, r.height);
                it.Stepped = d => StepLook(kind, d);
                _look[k] = it;
            }
            Rect lc = ProfileLayout.LookCard;
            MenuUi.Panel(page, "Look Card", lc.x, lc.y, lc.width, lc.height);
            const float disc = 150f;
            _cardDisc = MenuUi.Shape(page, "Look Disc", ShapeKind.Circle, lc.x + 24f, lc.y + 24f, disc, disc, CMTokens.Azure.Color, 0f);
            _cardDisc.RimPx = 0f;
            _cardDisc.OutlinePx = 4f;
            _cardPortrait = HudIcon.Make(_cardDisc.transform, "Portrait", 8f, 8f, disc - 16f, disc - 16f);
            _cardEmblem = LookView.Emblem(page, "Look Emblem", lc.x + 24f + disc - 46f, lc.y + 24f + disc - 46f, 52f);
            float tx = lc.x + 24f + disc + 20f, tw = lc.xMax - tx - 20f;
            _cardTitle = L(page, "Look Title", CMTokens.Display, UiScale.TypeBody, brass, TextAnchor.MiddleLeft, new Rect(tx, lc.y + 30f, tw, 60f));
            _cardTitle.horizontalOverflow = HorizontalWrapMode.Wrap;
            _cardTrim = L(page, "Look Trim", CMTokens.Ui, UiScale.TypeSmall, cream, TextAnchor.MiddleLeft, new Rect(tx, lc.y + 96f, tw, 28f));
            for (int k = 0; k < _swatches.Length; k++)
            {
                _swatches[k] = MenuUi.Shape(page, "Swatch " + k, ShapeKind.Circle, tx + k * 44f, lc.y + 132f, 36f, 36f, Color.gray, 0f);
                _swatches[k].RimPx = 0f;
                _swatches[k].OutlinePx = 2f;
            }
            _cardNote = L(page, "Look Note", CMTokens.Ui, UiScale.TypeSmall, CMTokens.WithAlpha(cream, 0.85f), TextAnchor.UpperLeft,
                new Rect(lc.x + 24f, lc.y + 190f, lc.width - 48f, 64f), MetaWords.TeamColoursStay);
            _cardNote.horizontalOverflow = HorizontalWrapMode.Wrap;
            _career = L(page, "Career", CMTokens.Display, UiScale.TypeBody, brass, TextAnchor.MiddleLeft, ProfileLayout.Career);
            _careerTotals = L(page, "Career Totals", CMTokens.Ui, UiScale.TypeBody, cream, TextAnchor.UpperLeft, ProfileLayout.CareerTotals);
            _careerTotals.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        void BuildHistory(RectTransform page, Color cream)
        {
            for (int i = 0; i < ProfileLayout.HistoryRows; i++)
            {
                Rect r = ProfileLayout.HistoryRow(i);
                int row = i;
                MenuItem it = MenuUi.Item(page, "Row " + i, r.x, r.y, r.width, r.height, "", MenuItem.Kind.Button, UiScale.TypeBody, false, CMTokens.Mono);
                it.Submitted = () => ChooseRow(row);
                it.gameObject.AddComponent<ProfileRowFocus>().Focused = () => FocusRow(row);
                _rows[i] = it;
            }
            _detail = L(page, "Detail", CMTokens.Ui, UiScale.TypeBody, CMTokens.ToolBrass.Color, TextAnchor.MiddleCenter, ProfileLayout.HistoryDetail);
            _detail.horizontalOverflow = HorizontalWrapMode.Wrap;
            _empty = L(page, "Empty", CMTokens.Ui, UiScale.TypeLead, cream, TextAnchor.MiddleCenter, ProfileLayout.HistoryEmpty, MetaWords.HistoryEmpty);
            _empty.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        void BuildAchievements(RectTransform page, Color cream, Color brass)
        {
            _achCount = L(page, "Count", CMTokens.Display, UiScale.TypeButton, brass, TextAnchor.MiddleCenter, ProfileLayout.AchCount);
            for (int i = 0; i < _ach.Length; i++)
            {
                Rect c = ProfileLayout.AchCell(i);
                var cell = new AchCell { Root = MenuUi.Rt(page, "Achievement " + i) };
                MenuUi.Place(cell.Root, 0f, 0f, HudLayout.RefW, HudLayout.RefH);
                VectorShape back = MenuUi.Shape(cell.Root, "Back", ShapeKind.RoundRect, c.x, c.y, c.width, c.height, CMTokens.WithAlpha(CMTokens.PanelNavy.Color, 0.9f), 14f);
                back.RimPx = 0f;
                cell.Title = L(cell.Root, "Title", CMTokens.Display, UiScale.TypeBody, cream, TextAnchor.MiddleLeft, ProfileLayout.AchTitle(c));
                cell.State = L(cell.Root, "State", CMTokens.Mono, UiScale.TypeSmall, brass, TextAnchor.MiddleRight, ProfileLayout.AchState(c));
                cell.Rule = L(cell.Root, "Rule", CMTokens.Ui, UiScale.TypeSmall, CMTokens.WithAlpha(cream, 0.9f), TextAnchor.MiddleLeft, ProfileLayout.AchRule(c));
                Rect b = ProfileLayout.AchBar(c);
                VectorShape track = MenuUi.Shape(cell.Root, "Bar", ShapeKind.RoundRect, b.x, b.y, b.width, b.height, CMTokens.MeterEmpty.Color, 6f);
                track.RimPx = 0f;
                track.OutlinePx = 0f;
                cell.Fill = MenuUi.Shape(track.transform, "Fill", ShapeKind.RoundRect, 0f, 0f, 0f, b.height, cream, 6f);
                cell.Fill.RimPx = 0f;
                cell.Fill.OutlinePx = 0f;
                cell.BarW = b.width;
                _ach[i] = cell;
            }
        }

        // ------------------------------------------------------------------ test probes

        public MenuItem TabItem(ProfileTab t) => _tabs[(int)t];
        public MenuItem LookItem(int k) => _look[k];
        public MenuItem HistoryItem(int i) => _rows[i];
        public string HeadingText => _heading != null ? _heading.text : null;
        public string DetailText => _detail != null ? _detail.text : null;
        public string AchievementState(int i) => _ach[i].State.text;
        public bool AchievementShown(int i) => _ach[i].Root.gameObject.activeSelf;
    }

    /// <summary>b15 profile: calls Focused when a history row is selected (keyboard, controller or pointer).</summary>
    public sealed class ProfileRowFocus : MonoBehaviour, ISelectHandler
    {
        public Action Focused;
        public void OnSelect(BaseEventData e) => Focused?.Invoke();
    }
}
