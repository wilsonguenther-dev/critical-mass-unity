using System.Collections.Generic;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Progression;
using CriticalMass.Sim.Config;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>
    /// HOME, the lobby (FlowState.Menu; the type keeps its DG-01 name so every caller of ShowMenu lands here). Left: the
    /// registry's lines as compact tiles (How to play, Barracks, Controls, Settings, Codex, Credits, Quit and anything a
    /// later item registers; lines with no screen are left out, never shown as coming soon), REMATCH when a match was left
    /// through the pause menu, CHANGE MODE, and the big primary button: PLAY with the selected mode and class (opens the
    /// mode select on it), or PLAY AGAIN with the last mode after a match this session (straight to the ready screen).
    /// Centre: the player's fighter on a diorama with the Kaiju egg (HomeStage), turning to the cursor. Top right: the
    /// player card (name, level and its bar, coins, record), where the last match's rewards land. Bottom: a tip from the
    /// words pack beside the mascot. Keyboard, mouse and controller share one focus; Tab or Y opens the mode select from
    /// anywhere on the screen. Back: the title.
    /// </summary>
    public sealed class MainMenu : FlowScreen
    {
        public const float ColX = 96f, ColW = 540f, RailY = 168f, RailStep = 60f, RailH = 50f;
        public const float RematchY = 628f, ChangeY = 742f, PrimaryY = 830f, PrimaryH = 150f;
        public const float StageX = 680f, StageY = 226f, StageW = 1144f, StageH = 640f;
        public const float CardX = 1300f, CardY = 40f, CardW = 524f, CardH = 170f;
        public const float TipSeconds = 9f, LandSeconds = 1.6f;

        /// <summary>Lines the play flow owns (they live on the mode cards, not on the home rail).</summary>
        public static readonly HashSet<string> PlayFlowIds = new HashSet<string> { OverlayRegistry.PlayNow, OverlayRegistry.Play, OverlayRegistry.Spectate };

        readonly Dictionary<string, MenuItem> _items = new Dictionary<string, MenuItem>();
        readonly List<MenuItem> _order = new List<MenuItem>();
        List<MenuEntry> _entries;
        MenuItem _primary, _change, _rematch;
        Text _primarySub, _rematchNote, _className, _name, _level, _xp, _coins, _record, _tip, _land;
        VectorShape _xpBar;
        PromptStrip _primaryGlyph, _changeGlyph;
        RawImage _stageImage, _mascot;
        HudIcon _portrait;
        readonly HomeStage _stage = new HomeStage();
        HomeOffer _offer;
        IReadOnlyList<string> _tips;
        int _tipIndex;
        float _tipAt, _landAt = -1f;
        int _tier = 2;
        MatchRewards _landing;
        static int _landedForMatch = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => _landedForMatch = -1;

        /// <summary>The rail's lines, in order (tests).</summary>
        public IReadOnlyList<MenuEntry> Entries => _entries;
        public IReadOnlyList<MenuItem> Items => _order;
        public MenuItem PlayItem => _primary;
        public MenuItem ChangeModeItem => _change;
        public MenuItem RematchItem => _rematch;
        public HomeOffer CurrentOffer => _offer;
        public string PrimarySubText => _primarySub != null ? _primarySub.text : null;
        public bool RewardsLanding => _landAt >= 0f;

        /// <summary>The tile of an entry id ("barracks", "settings", ...); "playnow" and "play" are the primary button.</summary>
        public MenuItem Item(string id)
        {
            EnsureBuilt();
            if (id == OverlayRegistry.PlayNow || id == OverlayRegistry.Play) return _primary;
            return _items.TryGetValue(id, out MenuItem it) ? it : null;
        }

        public void FocusEntry(string id) => Focus(Item(id));

        protected override string HintKeys => "ENTER SELECT   ·   TAB CHANGE MODE   ·   ESC TITLE";
        protected override string HintPad => "A SELECT   ·   Y CHANGE MODE   ·   B TITLE";
        protected override float WashAlpha => 0.4f;

        protected override void BuildContent(RectTransform space)
        {
            VectorShape band = MenuUi.Shape(space, "Column Band", ShapeKind.RoundRect, ColX - 32f, 140f, ColW + 64f, 870f,
                CMTokens.WithAlpha(Navy, 0.6f), 22f);
            band.OutlinePx = 0f;
            band.RimPx = 0f;
            MenuUi.Label(space, "Wordmark", CMTokens.Display, 46, Cream, TextAnchor.MiddleLeft, ColX, 40f, 760f, 64f, "CRITICAL MASS", 4f);
            MenuUi.Label(space, "Tagline", CMTokens.Ui, 22, CMTokens.WithAlpha(Cream, 0.85f), TextAnchor.MiddleLeft, ColX, 100f, 760f, 32f,
                "16 v 16. Hold the hill. Turn the Kaiju.", 2f);

            // Rail: the registry's lines that lead somewhere (the play lines live on the mode cards).
            _entries = new List<MenuEntry>();
            List<MenuEntry> all = OverlayRegistry.MenuEntries();
            for (int i = 0; i < all.Count; i++)
                if (all[i].Available && !PlayFlowIds.Contains(all[i].Id)) _entries.Add(all[i]);
            for (int i = 0; i < _entries.Count; i++)
            {
                MenuEntry entry = _entries[i];
                MenuItem it = MenuUi.Item(space, "Entry " + entry.Id, ColX, RailY + i * RailStep, ColW * 0.78f, RailH, entry.Label,
                    MenuItem.Kind.Button, 24, false, CMTokens.Display);
                it.Body.Radius = 14f;
                it.Body.RimPx = 2f;
                it.Submitted = () => Choose(entry);
                _items[entry.Id] = it;
                _order.Add(it);
            }

            _rematch = MenuUi.Item(space, "Rematch", ColX, RematchY, ColW, 56f, "REMATCH   ·   SAME SEED", MenuItem.Kind.Button, 24, false,
                CMTokens.Display);
            _rematch.Body.Radius = 14f;
            _rematch.Submitted = () => PlayFlow.Rematch(_offer);
            _rematchNote = MenuUi.Label(space, "Rematch Note", CMTokens.Ui, 17, CMTokens.WithAlpha(Cream, 0.85f), TextAnchor.MiddleLeft,
                ColX + 4f, RematchY + 60f, ColW, 26f, "The match you left can't be resumed. This replays it from the start.", 2f);

            _change = MenuUi.Item(space, "Change Mode", ColX, ChangeY, ColW, 60f, "CHANGE MODE", MenuItem.Kind.Button, 26, false, CMTokens.Display);
            _change.Body.Radius = 14f;
            _change.Submitted = OpenModes;
            _changeGlyph = Glyphs(_change.transform, ColW - 150f, 60f, 18);

            _primary = MenuUi.Item(space, "Play", ColX, PrimaryY, ColW, PrimaryH, "PLAY", MenuItem.Kind.Button, 60, false, CMTokens.Display);
            _primary.Body.Radius = 22f;
            _primary.Body.RimPx = 5f;
            _primary.Body.SetRim(Brass);
            RectTransform pl = _primary.Label.rectTransform;
            MenuUi.Place(pl, 28f, 14f, ColW - 56f, 80f);
            _primarySub = MenuUi.Label(_primary.transform, "Sub", CMTokens.Ui, 22, Cream, TextAnchor.MiddleLeft, 30f, 94f, ColW - 60f, 36f, "", 2f);
            _primary.Value = _primarySub;   // recoloured with the focus like the label
            _primary.Submitted = PressPrimary;
            _primaryGlyph = Glyphs(_primary.transform, ColW - 170f, 80f, 20);

            BuildStage(space);
            BuildCard(space);
            BuildTip(space);
            _land = MenuUi.Label(space, "Rewards Landing", CMTokens.Display, 30, Brass, TextAnchor.MiddleCenter, 960f, 520f, 700f, 50f, "", 3f);
            _land.enabled = false;
            Wire();
            DefaultFocus = _primary;
        }

        static PromptStrip Glyphs(Transform parent, float x, float h, int size)
        {
            PromptStrip s = PromptStrip.Make(parent, "Glyphs", 30f, size, Cream);
            RectTransform r = s.Rect;
            MenuUi.Place(r, x, 0f, 150f, h);
            s.Align = TextAnchor.MiddleRight;
            return s;
        }

        void BuildStage(RectTransform space)
        {
            MenuUi.Panel(space, "Stage Frame", StageX, StageY, StageW, StageH);
            _stageImage = MenuUi.Rt(space, "Stage").gameObject.AddComponent<RawImage>();
            _stageImage.raycastTarget = false;
            MenuUi.Place(_stageImage.rectTransform, StageX + 8f, StageY + 8f, StageW - 16f, StageH - 16f);
            _stageImage.enabled = false;
            _portrait = HudIcon.Make(space, "Portrait", StageX + StageW * 0.5f - 200f, StageY + 120f, 340f, 340f);
            _portrait.Image.enabled = false;
            _className = MenuUi.Label(space, "Class Name", CMTokens.Display, 34, Cream, TextAnchor.MiddleLeft, StageX + 32f, StageY + StageH - 70f,
                StageW - 64f, 50f, "", 3f);
        }

        void BuildCard(RectTransform space)
        {
            MenuUi.Panel(space, "Player Card", CardX, CardY, CardW, CardH);
            _name = MenuUi.Label(space, "Player Name", CMTokens.Display, 30, Cream, TextAnchor.MiddleLeft, CardX + 28f, CardY + 16f, 360f, 42f, "", 3f);
            VectorShape badge = MenuUi.Shape(space, "Level Badge", ShapeKind.Hexagon, CardX + CardW - 98f, CardY + 14f, 76f, 76f, Brass, 8f);
            badge.OutlinePx = 4f;
            badge.RimPx = 0f;
            _level = MenuUi.Label(space, "Level", CMTokens.Display, 30, Navy, TextAnchor.MiddleCenter, CardX + CardW - 98f, CardY + 14f, 76f, 76f, "", 0f);
            _xpBar = MenuUi.Shape(space, "XP Bar", ShapeKind.RoundRect, CardX + 28f, CardY + 68f, 370f, 18f, Brass, 9f);
            _xpBar.SetEmpty(CMTokens.MeterEmpty.Color);
            _xpBar.OutlinePx = 3f;
            _xpBar.RimPx = 0f;
            _xp = MenuUi.Label(space, "XP", CMTokens.Ui, 18, CMTokens.WithAlpha(Cream, 0.9f), TextAnchor.MiddleLeft, CardX + 28f, CardY + 90f, 400f, 26f, "", 2f);
            VectorShape coin = MenuUi.Shape(space, "Coin", ShapeKind.Diamond, CardX + 28f, CardY + 126f, 26f, 26f, Brass, 4f);
            coin.OutlinePx = 3f;
            coin.RimPx = 0f;
            _coins = MenuUi.Label(space, "Coins", CMTokens.Display, 26, Cream, TextAnchor.MiddleLeft, CardX + 64f, CardY + 120f, 200f, 38f, "", 3f);
            _record = MenuUi.Label(space, "Record", CMTokens.Ui, 18, CMTokens.WithAlpha(Cream, 0.9f), TextAnchor.MiddleRight, CardX + 240f, CardY + 120f,
                CardW - 268f, 38f, "", 2f);
        }

        void BuildTip(RectTransform space)
        {
            const float y = StageY + StageH + 20f, h = 94f;
            MenuUi.Panel(space, "Tip Strip", StageX, y, StageW, h);
            _mascot = MenuUi.Rt(space, "Mascot").gameObject.AddComponent<RawImage>();
            _mascot.raycastTarget = false;
            MenuUi.Place(_mascot.rectTransform, StageX + 14f, y + 10f, 74f, 74f);
            _mascot.texture = Resources.Load<Texture2D>(MascotGuide.PortraitResource(MascotReaction.Neutral, 2));
            _mascot.enabled = _mascot.texture != null;
            float tx = _mascot.enabled ? StageX + 104f : StageX + 24f;
            MenuUi.Label(space, "Tip Label", CMTokens.Display, 20, Brass, TextAnchor.MiddleLeft, tx, y + 10f, 400f, 28f,
                MascotGuide.MascotName.ToUpperInvariant() + " SAYS", 2f);
            _tip = MenuUi.Label(space, "Tip", CMTokens.Ui, 22, Cream, TextAnchor.UpperLeft, tx, y + 40f, StageX + StageW - tx - 24f, 50f, "", 2f);
            _tip.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        /// <summary>Explicit wrapping up/down through rail, rematch (when offered), change mode and the primary button.</summary>
        void Wire()
        {
            var chain = new List<MenuItem>(_order);
            if (_rematch.interactable && _rematch.gameObject.activeSelf) chain.Add(_rematch);
            chain.Add(_change);
            chain.Add(_primary);
            ChainVertical(chain);
            if (!_rematch.gameObject.activeSelf) _rematch.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        protected override void OnShow()
        {
            _offer = PlayModes.Offer();
            MatchSetupChoice basis = SetupPrefs.EnsureLoaded();
            MatchSetupChoice shown = _offer.PlayAgain ? _offer.Setup : PlayFlow.ChoiceFor(_offer.Mode);

            // Primary button: PLAY / PLAY AGAIN with the mode, size and class it starts.
            HudText.Set(_primary.Label, _offer.PrimaryTitle);
            string mode = shown.Mode == MatchMode.Custom ? "CUSTOM MATCH" : PlayModes.Info(shown.Mode).Title;
            string sub = mode + "   ·   " + PlayModes.SizeLabel(shown.TeamSize) +
                         (shown.Mode == MatchMode.Spectate ? "   ·   WATCHING" : "   ·   " + FlowWords.ClassName(shown.Class));
            if (_offer.NextStep) sub = "NEXT: " + sub;
            HudText.Set(_primarySub, sub);

            bool rematch = _offer.Rematch;
            _rematch.gameObject.SetActive(rematch);
            _rematchNote.enabled = rematch;
            Wire();

            // Diorama: the class this button plays (the egg always), or the class portrait while that class has no model.
            ClassId cls = shown.Mode == MatchMode.Spectate ? basis.Class : shown.Class;
            TeamId team = shown.Team;
            bool model = _stage.Show(cls, team);
            _stageImage.texture = _stage.Texture;
            _stageImage.enabled = _stage.Built && _stage.Texture != null;
            _portrait.Set(UiIcons.Portrait[(int)cls, (int)team], _tier);
            _portrait.Image.enabled = !model;
            HudText.Set(_className, FlowWords.ClassName(cls) + "   ·   " + (team == TeamId.Azure ? "AZURE" : "CRIMSON"));

            RefreshCard(-1, -1);
            if (_tips == null) _tips = LoreWords.Tips();
            if (_tips != null && _tips.Count > 0)
            {
                _tipIndex = (_tipIndex + 1) % _tips.Count;
                HudText.Set(_tip, _tips[_tipIndex]);
            }
            _tipAt = Time.unscaledTime;
            StartLanding();
        }

        /// <summary>The last match's rewards land on the card once, the first time home shows after that match.</summary>
        void StartLanding()
        {
            _landAt = -1f;
            _land.enabled = false;
            MatchRewards r = MatchRewardTracker.Last;
            PlayerProfile p = Profile.Current;
            if (r == null || p == null || !r.Stats.Completed || GameFlow.MatchesStarted == _landedForMatch) return;
            if (r.CoinsAfter != p.Coins || r.SkillPointsAfter != p.SkillPoints || (r.Coins <= 0 && r.SkillPoints <= 0)) return;
            _landedForMatch = GameFlow.MatchesStarted;
            _landing = r;
            _landAt = Time.unscaledTime;
            HudText.Set(_land, "+" + r.Coins + " COINS   ·   +" + r.SkillPoints + " XP" + (r.LevelsGained > 0 ? "   ·   LEVEL UP" : ""));
            _land.enabled = true;
            RefreshCard(r.CoinsBefore, r.SkillPointsBefore);
        }

        /// <summary>The player card; coins and skill points override the profile's while the rewards land (-1: the profile's).</summary>
        void RefreshCard(int coins, int skillPoints)
        {
            PlayerProfile p = Profile.Current;
            if (p == null) return;
            int c = coins >= 0 ? coins : p.Coins;
            int sp = skillPoints >= 0 ? skillPoints : p.SkillPoints;
            LevelProgress lp = Leveling.Progress(sp);
            HudText.Set(_name, string.IsNullOrEmpty(p.PlayerName) ? PlayerProfile.DefaultName : p.PlayerName.ToUpperInvariant());
            HudText.Set(_level, NumberCache.Int(lp.Level));
            _xpBar.Fill01 = lp.Fraction;
            HudText.Set(_xp, lp.IsMax ? "TOP LEVEL" : NumberCache.Int(lp.Into) + " / " + NumberCache.Int(lp.Needed) + " XP TO LEVEL " + NumberCache.Int(lp.Level + 1));
            HudText.Set(_coins, NumberCache.Int(c));
            HudText.Set(_record, NumberCache.Int(p.MatchesPlayed) + " PLAYED   ·   " + NumberCache.Int(p.Wins) + " WON");
        }

        protected override void OnScale(float scale)
        {
            int tier = UiIcons.TierFor(scale);
            if (tier == _tier) return;
            _tier = tier;
            if (_portrait != null) _portrait.SetTier(tier);
        }

        protected override void Tick()
        {
            float dt = Time.unscaledDeltaTime;
            bool pad = ControlBindings.ActiveDevice == ControlDevice.Gamepad;
            _primaryGlyph.SetHint(pad ? "A PLAY" : "ENTER PLAY", pad ? ControlDevice.Gamepad : ControlDevice.KeyboardMouse);
            _changeGlyph.SetHint(pad ? "Y MODES" : "TAB MODES", pad ? ControlDevice.Gamepad : ControlDevice.KeyboardMouse);

            Keyboard k = Keyboard.current;
            Gamepad g = Gamepad.current;
            if (Time.unscaledTime >= MenuItem.SubmitBlockedUntil &&
                ((k != null && k.tabKey.wasPressedThisFrame) || (g != null && g.buttonNorth.wasPressedThisFrame)))
            {
                OpenModes();
                return;
            }

            // The fighter turns to the pointer over the stage, or the right stick.
            Vector2 pointer = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            float px = 0f;
            if (_stageImage != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(_stageImage.rectTransform, pointer,
                    Canvas != null ? Canvas.worldCamera : null, out Vector2 local))
            {
                Rect r = _stageImage.rectTransform.rect;
                px = (local.x - r.center.x) / Mathf.Max(1f, r.width * 0.5f);
            }
            float stick = g != null ? g.rightStick.ReadValue().x : 0f;
            _stage.Tick(dt, pointer, px, stick);

            if (_tips != null && _tips.Count > 1 && Time.unscaledTime - _tipAt >= TipSeconds)
            {
                _tipAt = Time.unscaledTime;
                _tipIndex = (_tipIndex + 1) % _tips.Count;
                HudText.Set(_tip, _tips[_tipIndex]);
            }
            TickLanding();
        }

        void TickLanding()
        {
            if (_landAt < 0f || _landing == null) return;
            float k = Mathf.Clamp01((Time.unscaledTime - _landAt) / LandSeconds);
            float e = 1f - (1f - k) * (1f - k);
            // The chip rises from the stage toward the card and fades as it arrives; the numbers count up under it.
            RectTransform rt = _land.rectTransform;
            Vector2 from = new Vector2(960f + 350f, -545f), to = new Vector2(CardX + CardW * 0.5f, -(CardY + CardH + 20f));
            rt.anchoredPosition = Vector2.Lerp(from, to, e);
            Color c = _land.color;
            c.a = k < 0.8f ? 1f : 1f - (k - 0.8f) / 0.2f;
            _land.color = c;
            int coins = Mathf.RoundToInt(Mathf.Lerp(_landing.CoinsBefore, _landing.CoinsAfter, e));
            int sp = Mathf.RoundToInt(Mathf.Lerp(_landing.SkillPointsBefore, _landing.SkillPointsAfter, e));
            RefreshCard(coins, sp);
            if (k >= 1f)
            {
                _landAt = -1f;
                _land.enabled = false;
                RefreshCard(-1, -1);
            }
        }

        void PressPrimary()
        {
            if (_offer.PlayAgain) PlayFlow.Again(_offer);
            else OpenModes();
        }

        void OpenModes() => Director.ShowModes(_offer.Mode);

        public override void Hide()
        {
            base.Hide();
            _stage.SetActive(false);
        }

        void OnDestroy() => _stage.Release();

        void Choose(MenuEntry e)
        {
            switch (e.Id)
            {
                case OverlayRegistry.Codex:
                    if (e.ScreenType == null)
                    {
                        Director.OpenPausePage(Codex.CodexPage.Label, e.Id);
                        return;
                    }
                    break;
                case OverlayRegistry.Controls:
                    Director.OpenSettings(PauseMenu.Tab.Controls, e.Id);
                    return;
                case OverlayRegistry.Settings:
                    Director.OpenSettings(PauseMenu.Tab.Audio, e.Id);
                    return;
                case OverlayRegistry.Quit:
                    PauseMenu.QuitGame();
                    return;
                case OverlayRegistry.HowTo:
                    if (e.ScreenType == null)
                    {
                        if (OverlayRegistry.StartTutorial()) Hide();
                        return;
                    }
                    break;
            }
            Director.OpenRegistered(e);
        }

        public override void Back() => Director.ShowTitle();
    }
}
